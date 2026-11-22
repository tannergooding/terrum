// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private enum ForeignNativeArgumentQuality : byte
    {
        Identity,
        AccessAttenuation,
        Contextual,
    }

    private sealed record ForeignNativeInterfaceCandidate(
        BoundInterfaceMemberIdentity Identity,
        BoundInterfaceMemberDefinition Member,
        ImmutableArray<int> ArgumentParameterIndices,
        ImmutableArray<ForeignNativeArgumentQuality> Qualities);

    private bool TryBindForeignNativeInterfaceInvocation(
        SyntaxNode invocation, SyntaxNode interfaceName,
        string memberName, SyntaxNode arguments,
        out BoundExpression bound)
    {
        bound = ErrorExpression(invocation.Span);
        if (HasVariableReceiverName(interfaceName))
        {
            return false;
        }

        var diagnosticCount = DiagnosticCount;
        if (!TryResolveForeignNativeInterfaceOwner(interfaceName,
                new HashSet<AliasDefinition>(),
                out var owner, out var ownerType))
        {
            return DiagnosticCount != diagnosticCount;
        }
        if (owner.ForeignNativeInterface is not
            BoundForeignNativeInterfaceLayout foreignLayout)
        {
            return false;
        }

        var staticMemberResolution = ResolveStaticMember(interfaceName,
            memberName, out _);
        if (staticMemberResolution
                is DeclaredTypeResolution.Ambiguous
                    or DeclaredTypeResolution.Unique)
        {
            return false;
        }

        var argumentNodes = arguments.Children.OfType<SyntaxNode>()
            .ToArray();
        if (argumentNodes.Length
            > SemanticVerifier.MaximumArguments)
        {
            Report("TRM3005",
                "A foreign-interface call exceeds the argument limit.",
                arguments.Span);
            return true;
        }
        if (argumentNodes.Length == 0
            || GetArgumentName(argumentNodes[0]) is not null
            || !TryGetForeignCallArgumentValue(argumentNodes[0],
                out var receiverSyntax)
            || receiverSyntax.Kind
                == SyntaxKind.NativeOutputAddressExpression)
        {
            Report("TRM3011",
                "A foreign-interface call requires a first unlabelled ordinary receiver argument.",
                arguments.Span);
            return true;
        }

        var members = owner.Members.Where(member =>
                StringComparer.Ordinal.Equals(member.Name, memberName))
            .ToArray();
        if (members.Length == 0)
        {
            Report("TRM3011",
                $"Foreign interface '{owner.QualifiedName}' has no member '{memberName}'.",
                invocation.Span);
            return true;
        }

        var receiverType = KnownType(receiverSyntax);
        var candidates = new List<ForeignNativeInterfaceCandidate>();
        foreach (var member in members)
        {
            if (TryCreateForeignNativeInterfaceCandidate(ownerType,
                    member, receiverType, argumentNodes, out var candidate))
            {
                candidates.Add(candidate);
            }
        }

        if (candidates.Count == 0)
        {
            Report("TRM3011",
                $"No foreign-interface member '{memberName}' is applicable to the supplied receiver and arguments.",
                invocation.Span);
            return true;
        }

        var winners = candidates.Where(candidate =>
                candidates.All(other => ReferenceEquals(candidate, other)
                    || ForeignCandidateDominates(candidate, other)))
            .ToArray();
        if (winners is not [{ } selected])
        {
            Report("TRM3011",
                $"Foreign-interface member '{memberName}' is ambiguous.",
                invocation.Span);
            return true;
        }

        var member = selected.Member;
        if (_isBindingConstEvalBody)
        {
            Report("TRM3005",
                "A ForeignNativeInterface call cannot be evaluated during ConstEval.",
                invocation.Span);
            return true;
        }

        if (member.ResultType is not BoundInterfaceType {
                Kind: BoundInterfaceTypeKind.Primitive,
            } resultType
            || member.NativeCallableConvention is not
                NativeCallingConvention convention
            || !ForeignNativeInterfaceContract.TryGetReceiverType(member,
                [.. _nativePointerTypes], out var expectedReceiverType))
        {
            Report("TRM3030",
                "The selected foreign-interface method has an incomplete native signature.",
                invocation.Span);
            return true;
        }

        var receiverKnownType = receiverType;
        if (receiverKnownType is not PrimitiveType actualReceiverType)
        {
            Report("TRM3011",
                "A foreign-interface receiver requires a typed native pointer value.",
                receiverSyntax.Span);
            return true;
        }
        var receiver = BindExpression(receiverSyntax, actualReceiverType);
        if (!ForeignNativeInterfaceContract.IsNativeValueTypeCompatible(
                receiver.Type, expectedReceiverType,
                [.. _nativePointerTypes], out _))
        {
            Report("TRM3011",
                "The receiver does not provide the selected method's required pointer access.",
                receiverSyntax.Span);
            return true;
        }

        var nonNullReceiver = receiver;
        while (nonNullReceiver is BoundUnsafeExpression unsafeExpression)
        {
            nonNullReceiver = unsafeExpression.Expression;
        }
        if (DirectNativeImportContract.IsNullNativePointerValue(
                nonNullReceiver))
        {
            Report("TRM3030",
                "A foreign-interface call requires a statically non-null receiver.",
                receiverSyntax.Span);
            return true;
        }

        var boundArguments
            = ImmutableArray.CreateBuilder<BoundExpression>(
                member.Parameters.Length);
        var argumentParameterIndices
            = ImmutableArray.CreateBuilder<int>(
                member.Parameters.Length);
        var suppliedParameters = new bool[member.Parameters.Length];
        for (var sourceIndex = 1;
            sourceIndex < argumentNodes.Length; sourceIndex++)
        {
            var argument = argumentNodes[sourceIndex];
            var formalOrdinal = selected.ArgumentParameterIndices[
                sourceIndex - 1];
            if (!TryGetForeignCallArgumentValue(argument,
                    out var valueSyntax)
                || (uint)formalOrdinal
                    >= (uint)member.Parameters.Length)
            {
                Report("TRM3011",
                    "A foreign-interface argument has an invalid value or formal mapping.",
                    argument.Span);
                boundArguments.Add(ErrorExpression(argument.Span));
                argumentParameterIndices.Add(formalOrdinal);
                continue;
            }

            var parameter = member.Parameters[formalOrdinal];
            if (parameter.Type is not BoundInterfaceType {
                    Kind: BoundInterfaceTypeKind.Primitive,
                } parameterType)
            {
                Report("TRM3030",
                    "The selected foreign-interface formal is not a native value type.",
                    argument.Span);
                boundArguments.Add(ErrorExpression(argument.Span));
                argumentParameterIndices.Add(formalOrdinal);
                continue;
            }

            var hasOutput = IsForeignNativeOutputFormal(owner, member,
                formalOrdinal);
            BoundExpression boundArgument;
            if (hasOutput
                && valueSyntax.Kind
                    == SyntaxKind.NativeOutputAddressExpression)
            {
                if (!TryGetForeignNativeOutputPointee(owner, member,
                        formalOrdinal, out var pointeeType))
                {
                    Report("TRM3030",
                        "A selected NativeOutput formal has no valid writable pointer cell type.",
                        argument.Span);
                    boundArgument = ErrorExpression(argument.Span);
                }
                else
                {
                    boundArgument = BindNativeOutputReservation(
                        valueSyntax, pointeeType, argument.Span);
                }
            }
            else
            {
                var knownType = KnownType(valueSyntax);
                var expectedType = parameterType.PrimitiveType;
                var bindingType = knownType is PrimitiveType suppliedType
                    && ForeignNativeInterfaceContract
                        .IsNativeValueTypeCompatible(suppliedType,
                            expectedType, [.. _nativePointerTypes],
                            out _)
                        ? suppliedType : expectedType;
                boundArgument = BindExpression(valueSyntax, bindingType);
                if (!ForeignNativeInterfaceContract
                        .IsNativeValueTypeCompatible(boundArgument.Type,
                            expectedType, [.. _nativePointerTypes],
                            out _)
                    || (hasOutput
                        && boundArgument
                            is not BoundNativeOutputReservation))
                {
                    Report("TRM3011",
                        "A foreign-interface argument does not satisfy the selected formal contract.",
                        argument.Span);
                    boundArgument = ErrorExpression(argument.Span);
                }
            }

            boundArguments.Add(boundArgument);
            argumentParameterIndices.Add(formalOrdinal);
            suppliedParameters[formalOrdinal] = true;
        }

        for (var formalOrdinal = 0;
            formalOrdinal < member.Parameters.Length; formalOrdinal++)
        {
            if (suppliedParameters[formalOrdinal])
            {
                continue;
            }

            var parameter = member.Parameters[formalOrdinal];
            if (IsForeignNativeOutputFormal(owner, member, formalOrdinal))
            {
                Report("TRM3011",
                    $"NativeOutput formal '{parameter.Name}' requires an '&init' reservation.",
                    arguments.Span);
                boundArguments.Add(ErrorExpression(arguments.Span));
                argumentParameterIndices.Add(formalOrdinal);
                continue;
            }

            if (parameter.DefaultValue is BoundConstant defaultValue
                && parameter.Type is BoundInterfaceType {
                    Kind: BoundInterfaceTypeKind.Primitive,
                } defaultType
                && defaultValue.Type == defaultType.PrimitiveType)
            {
                boundArguments.Add(defaultValue with {
                    Span = arguments.Span,
                });
                argumentParameterIndices.Add(formalOrdinal);
                continue;
            }

            Report("TRM3011",
                $"Required foreign-interface parameter '{parameter.Name}' was not supplied.",
                arguments.Span);
            boundArguments.Add(ErrorExpression(arguments.Span));
            argumentParameterIndices.Add(formalOrdinal);
        }

        if (boundArguments.Count + 1
            > SemanticVerifier.MaximumArguments)
        {
            Report("TRM3005",
                "A foreign-interface call exceeds the argument limit including its receiver.",
                arguments.Span);
            return true;
        }

        if (_unsafeDepth == 0)
        {
            Report("TRM3028",
                "Calling a ForeignNativeInterface method requires an explicit unsafe context.",
                invocation.Span);
        }

        bound = new BoundCall(BoundCall.ForeignInterfaceFunction,
            boundArguments.MoveToImmutable(), resultType.PrimitiveType,
            invocation.Span) {
            ForeignInterfaceCall = new BoundForeignInterfaceCall(
                receiver, selected.Identity,
                foreignLayout.VTableByteOffset,
                convention, member.RequiresMutableReceiver,
                member.DoesNotReturn, member.NativeOutputContracts),
            ArgumentParameterIndices =
                argumentParameterIndices.MoveToImmutable(),
            IsUnsafeAuthorized = _unsafeDepth != 0,
        };
        return true;
    }

    private PrimitiveType? GetForeignNativeInterfaceInvocationType(
        SyntaxNode invocation)
    {
        var diagnosticCount = DiagnosticCount;
        var sourceDiagnosticCount = _sourceDiagnostics?.Count ?? 0;
        try
        {
            if (invocation.Kind != SyntaxKind.InvocationExpression
                || invocation.Children.Length != 2
                || invocation.Children[0] is not SyntaxNode {
                    Kind: SyntaxKind.MemberAccessExpression,
                } callee)
            {
                return null;
            }

            var interfaceName = (SyntaxNode)callee.Children[0];
            if (HasVariableReceiverName(interfaceName))
            {
                return null;
            }
            var memberName
                = ((SyntaxTokenElement)callee.Children[2]).Token.Name;
            if (!TryResolveForeignNativeInterfaceOwner(interfaceName,
                    new HashSet<AliasDefinition>(),
                    out var owner, out var ownerType))
            {
                return null;
            }
            var staticMemberResolution = ResolveStaticMember(interfaceName,
                memberName, out _);
            if (staticMemberResolution
                    is DeclaredTypeResolution.Ambiguous
                        or DeclaredTypeResolution.Unique)
            {
                return null;
            }

            var callArguments = ((SyntaxNode)invocation.Children[1])
                .Children.OfType<SyntaxNode>().ToArray();
            if (!TryGetForeignCallReceiverArgument(callArguments,
                    out var receiverSyntax))
            {
                return null;
            }

            var receiverType = KnownType(receiverSyntax);
            var candidates = new List<ForeignNativeInterfaceCandidate>();
            foreach (var member in owner.Members.Where(member =>
                         StringComparer.Ordinal.Equals(member.Name,
                             memberName)))
            {
                if (TryCreateForeignNativeInterfaceCandidate(ownerType,
                        member, receiverType, callArguments,
                        out var candidate))
                {
                    candidates.Add(candidate);
                }
            }

            var winners = candidates.Where(candidate =>
                    candidates.All(other => ReferenceEquals(candidate, other)
                        || ForeignCandidateDominates(candidate, other)))
                .ToArray();
            return winners is [{ } selected]
                && selected.Member.ResultType
                    is BoundInterfaceType {
                        Kind: BoundInterfaceTypeKind.Primitive,
                    } resultType
                    ? resultType.PrimitiveType : null;
        }
        finally
        {
            if (_diagnostics is not null
                && _diagnostics.Count > diagnosticCount)
            {
                _diagnostics.RemoveRange(diagnosticCount,
                    _diagnostics.Count - diagnosticCount);
            }
            if (_sourceDiagnostics is not null
                && _sourceDiagnostics.Count > sourceDiagnosticCount)
            {
                _sourceDiagnostics.RemoveRange(sourceDiagnosticCount,
                    _sourceDiagnostics.Count - sourceDiagnosticCount);
            }
        }
    }

    private bool TryResolveForeignNativeInterfaceOwner(
        SyntaxNode typeSyntax, HashSet<AliasDefinition> aliases,
        out BoundInterfaceDefinition owner,
        out BoundInterfaceType interfaceType)
    {
        owner = null!;
        interfaceType = null!;
        if (typeSyntax.Kind == SyntaxKind.GenericNameExpression
            || (typeSyntax.Kind == SyntaxKind.TypeName
                && typeSyntax.Children.OfType<SyntaxNode>().Any()))
        {
            return false;
        }

        var name = NameText(typeSyntax);
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (FindAliasDefinition(name) is AliasDefinition alias)
        {
            if (!aliases.Add(alias))
            {
                Report("TRM3005",
                    $"Type alias '{alias.QualifiedName}' contains a cycle.",
                    typeSyntax.Span);
                return false;
            }
            if (!alias.TypeParameters.IsEmpty || alias.IsInvalid)
            {
                return false;
            }

            var previousContext = CaptureContext();
            try
            {
                SetContext(alias.Tree, alias.Target);
                return TryResolveForeignNativeInterfaceOwner(
                    alias.Target, aliases, out owner, out interfaceType);
            }
            finally
            {
                RestoreContext(previousContext);
            }
        }

        var diagnosticCount = DiagnosticCount;
        if (ResolveInterfaceName(name, 0, _interfaceDeclarationGroups,
                typeSyntax.Span, out var resolved)
                is not string interfaceName
            || resolved is null
            || DiagnosticCount != diagnosticCount)
        {
            return false;
        }

        var matches = _interfaces.Where(definition =>
                StringComparer.Ordinal.Equals(definition.QualifiedName,
                    interfaceName)
                && definition.ModuleImport == resolved.ModuleImport
                && definition.PrivateForeignInterfaceOrigin?.Owner
                    == resolved.PrivateForeignInterfaceBinding)
            .ToArray();
        if (matches is not [{ } definition]
            || definition.ForeignNativeInterface is null)
        {
            return false;
        }

        owner = definition;
        interfaceType = new BoundInterfaceType(
            BoundInterfaceTypeKind.Nominal, PrimitiveType.Void,
            definition.QualifiedName, -1, [], false, false, false, false) {
            ModuleImport = definition.ModuleImport,
            PrivateForeignInterfaceBinding
                = resolved.PrivateForeignInterfaceBinding,
        };
        return true;
    }

    private bool TryCreateForeignNativeInterfaceCandidate(
        BoundInterfaceType ownerType,
        BoundInterfaceMemberDefinition member,
        PrimitiveType? receiverType,
        SyntaxNode[] callArguments,
        out ForeignNativeInterfaceCandidate candidate)
    {
        candidate = null!;
        if (receiverType is not PrimitiveType actualReceiverType
            || member.Kind != BoundInterfaceMemberKind.Callable
            || member.Accessibility != BoundInterfaceAccessibility.Public
            || member.IsStatic || member.IsExternal || member.IsPartial
            || member.IsAsync || member.HasBody
            || !member.TypeParameters.IsEmpty
            || !member.Constraints.IsEmpty
            || !member.DeclaredRequiresUnsafeCaller
            || !member.RequiresUnsafeCaller
            || member.NativeCallableConvention is null
            || member.ResultType is not BoundInterfaceType {
                Kind: BoundInterfaceTypeKind.Primitive,
            }
            || member.Parameters.IsDefault
            || !ForeignNativeInterfaceContract.TryGetReceiverType(member,
                [.. _nativePointerTypes], out var expectedReceiverType)
            || !ForeignNativeInterfaceContract
                .IsNativeValueTypeCompatible(actualReceiverType,
                    expectedReceiverType, [.. _nativePointerTypes],
                    out var receiverAttenuation))
            || !TryGetForeignCallReceiverArgument(callArguments,
                out _))
        {
            return false;
        }

        var mappings = ImmutableArray.CreateBuilder<int>(
            callArguments.Length - 1);
        var qualities = ImmutableArray.CreateBuilder<
            ForeignNativeArgumentQuality>(callArguments.Length);
        qualities.Add(receiverAttenuation
            ? ForeignNativeArgumentQuality.AccessAttenuation
            : ForeignNativeArgumentQuality.Identity);

        var assigned = new bool[member.Parameters.Length];
        var nextPositionalParameter = 0;
        var hasOutOfPositionNamedArgument = false;
        for (var sourceIndex = 1;
            sourceIndex < callArguments.Length; sourceIndex++)
        {
            var argument = callArguments[sourceIndex];
            if (!TryGetForeignCallArgumentValue(argument,
                    out var valueSyntax))
            {
                return false;
            }

            var argumentName = GetArgumentName(argument);
            int formalOrdinal;
            if (argumentName is not null)
            {
                formalOrdinal = -1;
                for (var index = 0; index < member.Parameters.Length;
                    index++)
                {
                    if (StringComparer.Ordinal.Equals(
                            member.Parameters[index].Name,
                            argumentName.Token.Name))
                    {
                        formalOrdinal = index;
                        break;
                    }
                }
                if (formalOrdinal < 0
                    || assigned[formalOrdinal])
                {
                    return false;
                }
                if (formalOrdinal == nextPositionalParameter)
                {
                    nextPositionalParameter++;
                }
                else
                {
                    hasOutOfPositionNamedArgument = true;
                }
            }
            else
            {
                if (hasOutOfPositionNamedArgument)
                {
                    return false;
                }
                formalOrdinal = nextPositionalParameter++;
            }

            if ((uint)formalOrdinal >= (uint)member.Parameters.Length)
            {
                return false;
            }
            assigned[formalOrdinal] = true;
            mappings.Add(formalOrdinal);

            var isOutputFormal = IsForeignNativeOutputFormal(owner, member,
                formalOrdinal);
            if (valueSyntax.Kind
                == SyntaxKind.NativeOutputAddressExpression)
            {
                if (!isOutputFormal)
                {
                    return false;
                }
                qualities.Add(ForeignNativeArgumentQuality.Contextual);
                continue;
            }
            if (isOutputFormal
                || member.Parameters[formalOrdinal].Type
                    is not BoundInterfaceType {
                        Kind: BoundInterfaceTypeKind.Primitive,
                    } parameterType)
            {
                return false;
            }

            if (KnownType(valueSyntax) is PrimitiveType suppliedType)
            {
                if (!ForeignNativeInterfaceContract
                    .IsNativeValueTypeCompatible(suppliedType,
                        parameterType.PrimitiveType,
                        [.. _nativePointerTypes],
                        out var accessAttenuation))
                {
                    return false;
                }
                qualities.Add(accessAttenuation
                    ? ForeignNativeArgumentQuality.AccessAttenuation
                    : ForeignNativeArgumentQuality.Identity);
            }
            else if (IsForeignContextualLiteral(valueSyntax,
                         parameterType.PrimitiveType))
            {
                qualities.Add(ForeignNativeArgumentQuality.Contextual);
            }
            else
            {
                return false;
            }
        }

        for (var formalOrdinal = 0;
            formalOrdinal < member.Parameters.Length; formalOrdinal++)
        {
            if (assigned[formalOrdinal])
            {
                continue;
            }

            if (IsForeignNativeOutputFormal(owner, member,
                    formalOrdinal)
                || !HasForeignNativeParameterDefault(
                    member.Parameters[formalOrdinal]))
            {
                return false;
            }
        }

        var identity = new BoundInterfaceMemberIdentity(ownerType,
            member.Ordinal, member.DeclarationKey);
        candidate = new ForeignNativeInterfaceCandidate(identity, member,
            mappings.MoveToImmutable(), qualities.MoveToImmutable());
        return true;
    }

    private static bool ForeignCandidateDominates(
        ForeignNativeInterfaceCandidate left,
        ForeignNativeInterfaceCandidate right)
    {
        if (left.Qualities.Length != right.Qualities.Length)
        {
            return false;
        }

        var isStrictlyBetter = false;
        for (var index = 0; index < left.Qualities.Length; index++)
        {
            if ((byte)left.Qualities[index]
                > (byte)right.Qualities[index])
            {
                return false;
            }
            if ((byte)left.Qualities[index]
                < (byte)right.Qualities[index])
            {
                isStrictlyBetter = true;
            }
        }

        return isStrictlyBetter;
    }

    private static bool TryGetForeignCallReceiverArgument(
        SyntaxNode[] callArguments, out SyntaxNode receiver)
    {
        receiver = null!;
        return callArguments.Length > 0
            && GetArgumentName(callArguments[0]) is null
            && TryGetForeignCallArgumentValue(callArguments[0],
                out receiver)
            && receiver.Kind != SyntaxKind.NativeOutputAddressExpression;
    }

    private static bool TryGetForeignCallArgumentValue(
        SyntaxNode argument, out SyntaxNode value)
    {
        value = null!;
        var valueIndex = GetArgumentName(argument) is null ? 0 : 2;
        if (argument.Children.Length != valueIndex + 1
            || argument.Children[valueIndex] is not SyntaxNode valueNode)
        {
            return false;
        }

        value = valueNode;
        return true;
    }

    private bool IsForeignContextualLiteral(SyntaxNode expression,
        PrimitiveType targetType)
    {
        while (expression.Kind == SyntaxKind.ParenthesizedExpression
            && expression.Children.Length > 1)
        {
            expression = (SyntaxNode)expression.Children[1];
        }

        if (expression.Kind == SyntaxKind.LiteralExpression
            && expression.Children is [
                SyntaxTokenElement { Token.Kind: TokenKind.NumericLiteral },
            ])
        {
            return IsForeignNumericLiteralTarget(targetType);
        }
        if (expression.Kind == SyntaxKind.LiteralExpression
            && expression.Children is [
                SyntaxTokenElement { Token.Kind: TokenKind.CharacterLiteral },
            ])
        {
            return targetType is PrimitiveType.Char8
                or PrimitiveType.Char16 or PrimitiveType.Char32
                or PrimitiveType.Rune;
        }
        if (expression.Kind == SyntaxKind.UnaryExpression
            && expression.Children.Length == 2
            && expression.Children[0] is SyntaxTokenElement {
                Token.Kind: TokenKind.Plus or TokenKind.Minus,
            }
            && expression.Children[1] is SyntaxNode operand
            && operand.Kind == SyntaxKind.LiteralExpression
            && operand.Children is [
                SyntaxTokenElement { Token.Kind: TokenKind.NumericLiteral },
            ])
        {
            return IsForeignNumericLiteralTarget(targetType);
        }
        if (expression.Kind == SyntaxKind.InvocationExpression
            && expression.Children.Length == 2
            && expression.Children[0] is SyntaxNode {
                Kind: SyntaxKind.NameExpression,
            } callee)
        {
            var name = NameText(callee);
            return (name == "utf8z"
                    && targetType == PrimitiveType.NativeUtf8ZPointer)
                || (name == "utf16z"
                    && targetType == PrimitiveType.NativeUtf16ZPointer);
        }

        return false;
    }

    private static bool IsForeignNumericLiteralTarget(PrimitiveType type) =>
        type is PrimitiveType.Int8 or PrimitiveType.Int16
            or PrimitiveType.Int32 or PrimitiveType.Int64
            or PrimitiveType.UInt8 or PrimitiveType.UInt16
            or PrimitiveType.UInt32 or PrimitiveType.UInt64
            or PrimitiveType.NativeInt or PrimitiveType.NativeUInt
            or PrimitiveType.Char8 or PrimitiveType.Char16
            or PrimitiveType.Char32 or PrimitiveType.Rune
            or PrimitiveType.Float32 or PrimitiveType.Float64;

    private static bool HasForeignNativeParameterDefault(
        BoundInterfaceParameter parameter) =>
        parameter.DefaultValue is not null
        || parameter.DefaultExpression is not null
        || parameter.HasPendingDefault;

    private static NativeOutputContract? GetForeignNativeOutputContract(
        BoundInterfaceMemberDefinition member, int formalOrdinal) =>
        member.NativeOutputContracts.FirstOrDefault(output =>
            output.FormalOrdinal == formalOrdinal);

    private bool IsForeignNativeOutputFormal(
        BoundInterfaceDefinition owner,
        BoundInterfaceMemberDefinition member, int formalOrdinal)
    {
        if (GetForeignNativeOutputContract(member, formalOrdinal) is not null)
        {
            return true;
        }

        var interfaceIndex = _interfaces.FindIndex(definition =>
            StringComparer.Ordinal.Equals(definition.QualifiedName,
                owner.QualifiedName)
            && definition.ModuleImport == owner.ModuleImport);
        return interfaceIndex >= 0
            && _pendingInterfaceNativeOutputConditions.Any(pending =>
                pending.InterfaceIndex == interfaceIndex
                && pending.MemberOrdinal == member.Ordinal
                && pending.FormalOrdinal == formalOrdinal);
    }

    private bool TryGetForeignNativeOutputPointee(
        BoundInterfaceDefinition owner,
        BoundInterfaceMemberDefinition member, int formalOrdinal,
        out PrimitiveType pointeeType)
    {
        pointeeType = default;
        if (GetForeignNativeOutputContract(member, formalOrdinal)
            is NativeOutputContract output)
        {
            pointeeType = output.PointeeType;
            return true;
        }

        return IsForeignNativeOutputFormal(owner, member, formalOrdinal)
            && (uint)formalOrdinal < (uint)member.Parameters.Length
            && NativeOutputContractValidator.TryGetInterfaceOutputPointee(
                member.Parameters[formalOrdinal], _structs,
                [.. _nativePointerTypes],
                [.. _nativeFunctionPointerSignatures], _enumDefinitions,
                out pointeeType);
    }
}
