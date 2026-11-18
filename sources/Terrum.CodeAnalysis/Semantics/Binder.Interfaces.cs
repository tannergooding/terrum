// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private readonly Dictionary<(string Name, int Arity),
        List<InterfaceDeclarationPart>> _interfaceDeclarationGroups = [];
    private readonly Dictionary<(string Name, int Arity),
        ImmutableArray<BoundInterfaceConstraint>>
        _interfaceWriteFormalRequirements = [];
    private readonly List<PendingInterfaceConformance>
        _pendingInterfaceConformances = [];
    private readonly List<PendingGenericInterfaceWitnessTarget>
        _pendingGenericInterfaceWitnessTargets = [];
    private readonly List<PendingInterfaceParameterDefault>
        _pendingInterfaceParameterDefaults = [];

    private sealed record PendingInterfaceConformance(
        PrimitiveType ImplementingType,
        BoundInterfaceType InterfaceType,
        SyntaxTree Tree,
        SyntaxNode Declaration,
        SyntaxNode Conformance,
        int SourceInputOrdinal);

    private sealed record PendingGenericInterfaceWitnessTarget(
        int WitnessIndex, int MemberOrdinal, string TemplateName,
        SyntaxTree Tree, SyntaxNode Conformance);

    private sealed record PendingInterfaceParameterDefault(
        int InterfaceIndex, int MemberOrdinal, int ParameterOrdinal,
        BoundInterfaceType Type, SyntaxTree Tree, SyntaxNode Expression);

    private sealed record AssociatedBindingSyntax(
        SyntaxNode Node,
        BoundInterfaceType Owner,
        string MemberName,
        SyntaxNode ValueType,
        SyntaxNode? Value);

    private sealed record InterfaceFormalSyntax(
        string Name,
        BoundInterfaceFormalKind Kind,
        bool IsSelf,
        SyntaxNode? ConstantType,
        SyntaxNode? TypeDefault,
        SyntaxNode? ConstantDefault,
        ImmutableArray<SyntaxNode> Attributes,
        TextSpan Span);

    private sealed record InterfaceDeclarationPart(
        SyntaxTree Tree,
        SyntaxNode Declaration,
        string QualifiedName,
        bool IsPublic,
        bool IsPartial,
        BoundInterfaceAccessibility Accessibility,
        ImmutableArray<string> AttributeTokens,
        ImmutableArray<InterfaceFormalSyntax> Formals);

    private sealed record PendingInterfaceNativeCallableConvention(
        int InterfaceIndex,
        int? MemberOrdinal,
        ImmutableArray<int> InheritedMemberOrdinals,
        SyntaxTree Tree,
        SyntaxNode Expression);

    private readonly List<PendingInterfaceNativeCallableConvention>
        _pendingInterfaceNativeCallableConventions = [];

    private sealed record ResolvedInterfaceType(
        string QualifiedName,
        BoundModuleImport? ModuleImport,
        ImmutableArray<BoundInterfaceFormal> Formals);

    private readonly record struct InterfaceConstantFormal(
        BoundInterfaceType Type,
        BoundInterfaceTypeParameterScope Scope,
        int Ordinal);

    private static ImmutableDictionary<string, InterfaceConstantFormal>
        InterfaceConstantParameters(
            ImmutableArray<BoundInterfaceFormal> formals,
            BoundInterfaceTypeParameterScope scope)
    {
        var result = ImmutableDictionary.CreateBuilder<string,
            InterfaceConstantFormal>(StringComparer.Ordinal);
        foreach (var formal in formals)
        {
            if (formal.Kind == BoundInterfaceFormalKind.Constant
                && formal.ConstantType is BoundInterfaceType constantType)
            {
                result.Add(formal.Name,
                    new InterfaceConstantFormal(constantType, scope, formal.Ordinal));
            }
        }
        return result.ToImmutable();
    }

    private void DeclareInterfaces()
    {
        _interfaceDeclarationGroups.Clear();
        var groups = _interfaceDeclarationGroups;
        foreach (var (tree, declaration) in _scopedDeclarations.Where(static item =>
                     item.Declaration.Kind == SyntaxKind.InterfaceDeclaration))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SetContext(tree, declaration);
            var interfaceTokenIndex = declaration.Children
                .Select((child, index) => (child, index))
                .First(static item => item.child is SyntaxTokenElement {
                    Token.Kind: TokenKind.InterfaceKeyword,
                }).index;
            var nameToken = declaration.Children.Skip(interfaceTokenIndex + 1)
                .OfType<SyntaxTokenElement>()
                .First(static token => token.Token.Kind == TokenKind.Identifier).Token;
            var parameters = declaration.Children.OfType<SyntaxNode>()
                .FirstOrDefault(static child => child.Kind == SyntaxKind.TypeParameterList);
            var formals = ParseInterfaceFormals(parameters);
            var qualifiedName = Qualify(nameToken.Name);
            if (_structNames.ContainsKey(qualifiedName) || _classNames.ContainsKey(qualifiedName)
                || _genericStructTemplates.ContainsKey(qualifiedName) || _aliases.ContainsKey(qualifiedName))
            {
                Report("TRM3003", $"Duplicate type declaration '{qualifiedName}'.", declaration.Span);
                continue;
            }

            var part = new InterfaceDeclarationPart(
                tree,
                declaration,
                qualifiedName,
                declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.PublicKeyword),
                declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.Identifier
                        && token.Token.Text == "partial"),
                BindInterfaceAccessibility(declaration.Children, declaration.Span),
                InterfaceAttributeTokens(declaration),
                formals);
            var key = (qualifiedName, formals.Length);
            if (!groups.TryGetValue(key, out var parts))
            {
                groups.Add(key, [part]);
            }
            else
            {
                parts.Add(part);
            }
        }

        _interfaceWriteFormalRequirements.Clear();
        foreach (var (key, parts) in groups)
        {
            _interfaceWriteFormalRequirements.Add(key,
                GetInterfaceWriteFormalOrdinals(parts));
        }

        foreach (var ((qualifiedName, _), parts) in groups)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var first = parts[0];
            SetContext(first.Tree, first.Declaration);
            if (parts.Count > 1 && parts.Any(static part => !part.IsPartial))
            {
                foreach (var duplicate in parts.Skip(1))
                {
                    SetContext(duplicate.Tree, duplicate.Declaration);
                    Report("TRM3003", $"Duplicate interface declaration '{qualifiedName}'.",
                        duplicate.Declaration.Span);
                }
                continue;
            }
            var mergeAttributeTokens = InterfaceMergeAttributeTokens(first);
            if (parts.Any(part => part.IsPublic != first.IsPublic
                || part.Accessibility != first.Accessibility
                || !InterfaceMergeAttributeTokens(part).SequenceEqual(
                    mergeAttributeTokens, StringComparer.Ordinal)
                || !InterfaceFormalsMatch(first.Formals, part.Formals)))
            {
                Report("TRM3003", $"Partial interface declarations for '{qualifiedName}' disagree.",
                    first.Declaration.Span);
                continue;
            }

            var (nativeCallableDefaultConvention,
                nativeCallableDefaultExpression) =
                    BindInterfaceNativeCallableDefault(parts, first);
            SetContext(first.Tree, first.Declaration);
            var interfaceIndex = _interfaces.Count;
            var inheritedNativeCallableMemberOrdinals = new List<int>();
            var typeParameters = first.Formals
                .Select((formal, ordinal) => (formal, ordinal))
                .Where(static item => item.formal.Kind == BoundInterfaceFormalKind.Type)
                .ToDictionary(static item => item.formal.Name, static item => item.ordinal,
                    StringComparer.Ordinal);
            var formals = BindInterfaceFormals(first.Formals, typeParameters, groups);
            var constantParameters = InterfaceConstantParameters(formals,
                BoundInterfaceTypeParameterScope.Interface);
            var bases = ImmutableArray.CreateBuilder<BoundInterfaceType>();
            var constraints = ImmutableArray.CreateBuilder<BoundInterfaceConstraint>();
            var members = ImmutableArray.CreateBuilder<BoundInterfaceMemberDefinition>();
            foreach (var part in parts)
            {
                SetContext(part.Tree, part.Declaration);
                var baseList = part.Declaration.Children.OfType<SyntaxNode>()
                    .FirstOrDefault(static child => child.Kind == SyntaxKind.ConformanceList);
                if (baseList is not null)
                {
                    foreach (var baseType in baseList.Children.OfType<SyntaxNode>())
                    {
                        foreach (var attribute in baseType.DescendantNodes()
                                     .Where(static node => node.Kind
                                         == SyntaxKind.Attribute)
                                     .Where(IsNativeCallableAttribute))
                        {
                            Report("TRM3030",
                                "NativeCallable is not valid on an interface base type.",
                                attribute.Span);
                        }
                        if (BindInterfaceType(baseType, typeParameters, groups,
                            first.Formals.Length,
                            interfaceConstantParameters: constantParameters) is
                                BoundInterfaceType boundBase)
                        {
                            if (boundBase.Kind != BoundInterfaceTypeKind.Nominal)
                            {
                                Report("TRM3005", "An interface base must name an interface declaration.",
                                    baseType.Span);
                            }
                            else
                            {
                                bases.Add(boundBase);
                            }
                        }
                    }

                    private static bool IsWriteConstraintTerm(SyntaxNode node) =>
                        node.Kind == SyntaxKind.ConstraintTerm
                            && node.Children.OfType<SyntaxTokenElement>().Any(
                                static token => token.Token.Kind == TokenKind.WriteKeyword);
                }
                for (var ordinal = 0; ordinal < part.Formals.Length; ordinal++)
                {
                    foreach (var attribute in part.Formals[ordinal].Attributes)
                    {
                        constraints.Add(new BoundInterfaceConstraint(
                            ordinal, BoundInterfaceConstraintKind.Attribute, null, AttributeName(attribute)));
                    }
                }
                BindInterfaceConstraints(part.Declaration, typeParameters, groups,
                    constraints, interfaceConstantParameters: constantParameters);
                members.AddRange(BindInterfaceMembers(part, typeParameters, groups,
                    formals, members.Count, interfaceIndex,
                    nativeCallableDefaultConvention,
                    nativeCallableDefaultExpression,
                    inheritedNativeCallableMemberOrdinals));
            }

            var boundMembers = members.ToImmutable();
            var boundConstraints = constraints.Distinct().ToImmutableArray();
            formals = ApplyInterfaceWriteRequirements(formals,
                boundConstraints);
            if (nativeCallableDefaultExpression is SyntaxNode defaultExpression)
            {
                _pendingInterfaceNativeCallableConventions.Add(
                    new PendingInterfaceNativeCallableConvention(
                        interfaceIndex, null,
                        [.. inheritedNativeCallableMemberOrdinals],
                        first.Tree, defaultExpression));
            }
            var foreignNativeInterface = BindForeignNativeInterfaceLayout(
                parts, first, formals, bases.Count, constraints.Count,
                boundMembers);
            if (!HasUniqueInterfaceMembers(members, first.Declaration.Span))
            {
                continue;
            }
            _interfaces.Add(new BoundInterfaceDefinition(
                qualifiedName,
                first.IsPublic,
                first.IsPartial,
                formals,
                [.. bases.Distinct()],
                boundConstraints,
                boundMembers)
            {
                Accessibility = first.Accessibility,
                AttributeTokens = first.AttributeTokens,
                ForeignNativeInterface = foreignNativeInterface,
                SourceInputOrdinal = _trees.IndexOf(first.Tree),
            });
        }

        if (!HasAcyclicInterfaceBases())
        {
            var (tree, declaration) = _scopedDeclarations.First(item =>
                item.Declaration.Kind == SyntaxKind.InterfaceDeclaration);
            SetContext(tree, declaration);
            Report("TRM3005", "Interface inheritance must be finite and acyclic.", declaration.Span);
        }
    }

    private void ValidateInterfaceConformances()
    {
        var consumedAssociatedBindings = new HashSet<SyntaxNode>(
            ReferenceEqualityComparer.Instance);
        foreach (var conformance in _pendingInterfaceConformances)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SetContext(conformance.Tree, conformance.Conformance);
            if (!BoundInterfaceContract.TryGetRequirementIdentities(
                conformance.InterfaceType, [.. _interfaces],
                static _ => true, out var requirements,
                genericNominalTypeDefinitions:
                    [.. _genericNominalTypeDefinitions],
                sourceGenericTypeDefinitions:
                    [.. _sourceGenericTypeDefinitions]))
            {
                Report("TRM3007",
                    "The declared interface conformance does not bind to one complete interface instance.",
                    conformance.Conformance.Span);
                continue;
            }
            var pendingGenericTargets
                = new List<(int MemberOrdinal, string TemplateName)>();
            if (TryBindInterfaceWitness(conformance, requirements,
                    pendingGenericTargets, out var witness, out var consumed))
            {
                var witnessIndex = _interfaceWitnesses.Count;
                _interfaceWitnesses.Add(witness);
                foreach (var target in pendingGenericTargets)
                {
                    _pendingGenericInterfaceWitnessTargets.Add(
                        new PendingGenericInterfaceWitnessTarget(
                            witnessIndex, target.MemberOrdinal,
                            target.TemplateName, conformance.Tree,
                            conformance.Conformance));
                }
                consumedAssociatedBindings.UnionWith(consumed);
            }
        }

        foreach (var conformance in _pendingInterfaceConformances
            .DistinctBy(static item => item.Declaration))
        {
            foreach (var binding in conformance.Declaration.Children
                .OfType<SyntaxNode>()
                .Where(static member => member.Kind
                    is SyntaxKind.AssociatedTypeBinding
                        or SyntaxKind.AssociatedConstantBinding))
            {
                if (!consumedAssociatedBindings.Contains(binding))
                {
                    SetContext(conformance.Tree, binding);
                    Report("TRM3007",
                        "The associated binding does not name a requirement of any declared interface conformance.",
                        binding.Span);
                }
            }
        }
    }

    private bool ResolvePendingGenericInterfaceWitnessTargets()
    {
        foreach (var target in _pendingGenericInterfaceWitnessTargets)
        {
            if ((uint)target.WitnessIndex
                    >= (uint)_interfaceWitnesses.Count
                || !_templates.TryGetValue(target.TemplateName,
                    out var template)
                || template.DefinitionOrdinal < 0)
            {
                SetContext(target.Tree, target.Conformance);
                Report("TRM3007",
                    "A generic interface witness target has no validated generic method definition.",
                    target.Conformance.Span);
                return false;
            }

            var witness = _interfaceWitnesses[target.WitnessIndex];
            if ((uint)target.MemberOrdinal
                    >= (uint)witness.Members.Length)
            {
                SetContext(target.Tree, target.Conformance);
                Report("TRM3007",
                    "A generic interface witness target has an invalid member ordinal.",
                    target.Conformance.Span);
                return false;
            }

            var members = witness.Members.ToBuilder();
            var member = members[target.MemberOrdinal];
            if (!StringComparer.Ordinal.Equals(
                    member.PendingGenericMethodName,
                    target.TemplateName)
                || member.FunctionIndex != -1)
            {
                SetContext(target.Tree, target.Conformance);
                Report("TRM3007",
                    "A generic interface witness target lost its selected method identity.",
                    target.Conformance.Span);
                return false;
            }

            members[target.MemberOrdinal] = member with {
                GenericMethodDefinitionOrdinal = template.DefinitionOrdinal,
                PendingGenericMethodName = null,
            };
            _interfaceWitnesses[target.WitnessIndex] = witness with {
                Members = members.MoveToImmutable(),
            };
        }

        _pendingGenericInterfaceWitnessTargets.Clear();
        return true;
    }

    private bool TryBindInterfaceWitness(
        PendingInterfaceConformance conformance,
        ImmutableArray<BoundInterfaceMemberIdentity> requirements,
        List<(int MemberOrdinal, string TemplateName)>
            pendingGenericTargets,
        out BoundInterfaceWitness witness,
        out ImmutableArray<SyntaxNode> consumedAssociatedBindings)
    {
        witness = null!;
        consumedAssociatedBindings = [];
        var typeName = InterfaceImplementingTypeName(
            conformance.ImplementingType);
        if (typeName is null)
        {
            Report("TRM3007",
                "Only declared nominal source types can provide interface witnesses.",
                conformance.Conformance.Span);
            return false;
        }

        var members = ImmutableArray.CreateBuilder<
            BoundInterfaceWitnessMember>();
        var associatedTypes = ImmutableArray.CreateBuilder<
            BoundInterfaceAssociatedTypeBinding>();
        var associatedConstants = ImmutableArray.CreateBuilder<
            BoundInterfaceAssociatedConstantBinding>();
        var consumedBindings = ImmutableArray.CreateBuilder<SyntaxNode>();
        var explicitAssociatedTypes = BindAssociatedBindingSyntax(
            conformance, SyntaxKind.AssociatedTypeBinding);
        var explicitAssociatedConstants = BindAssociatedBindingSyntax(
            conformance, SyntaxKind.AssociatedConstantBinding);
        if (explicitAssociatedTypes is null
            || explicitAssociatedConstants is null)
        {
            return false;
        }
        foreach (var requirement in requirements)
        {
            if (!BoundInterfaceContract.TryGetInterfaceMemberDefinition(
                    requirement, [.. _interfaces], static _ => true,
                    out var definition,
                    genericNominalTypeDefinitions:
                        [.. _genericNominalTypeDefinitions],
                    sourceGenericTypeDefinitions:
                        [.. _sourceGenericTypeDefinitions])
                || definition is null)
            {
                Report("TRM3007",
                    "The interface witness requirement has no exact member declaration.",
                    conformance.Conformance.Span);
                return false;
            }

            if (definition.Kind == BoundInterfaceMemberKind.Callable)
            {
                if (!TryBindInterfaceCallable(conformance,
                    typeName, requirement, definition, members,
                    pendingGenericTargets))
                {
                    return false;
                }
                continue;
            }
            if (definition.Kind == BoundInterfaceMemberKind.Property)
            {
                if ((uint)requirement.AccessorOrdinal
                        >= (uint)definition.Accessors.Length
                    || definition.Accessors[requirement.AccessorOrdinal]
                        .Kind != BoundInterfaceAccessorKind.Get
                    || !TryBindInterfacePropertyGetter(conformance,
                        typeName, requirement, definition, members))
                {
                    Report("TRM3007",
                        "This source witness supports only plain property getter requirements.",
                        conformance.Conformance.Span);
                    return false;
                }
                continue;
            }
            if (definition.Kind == BoundInterfaceMemberKind.Indexer)
            {
                if ((uint)requirement.AccessorOrdinal
                        >= (uint)definition.Accessors.Length
                    || !TryBindInterfaceIndexerAccessor(conformance,
                        typeName, requirement, definition, members))
                {
                    Report("TRM3007",
                        "This source witness does not satisfy the selected indexer accessor requirement.",
                        conformance.Conformance.Span);
                    return false;
                }
                continue;
            }

            if (definition.Kind == BoundInterfaceMemberKind.AssociatedType)
            {
                if (!TrySelectAssociatedBinding(requirement, definition,
                        explicitAssociatedTypes.Value,
                        out var explicitTypeBinding))
                {
                    Report("TRM3007",
                        "An associated type requirement has multiple source bindings.",
                        conformance.Conformance.Span);
                    return false;
                }
                if (explicitTypeBinding is AssociatedBindingSyntax
                    associatedTypeBinding)
                {
                    var diagnosticCount = DiagnosticCount;
                    if (!TryBindAssociatedValueType(
                        associatedTypeBinding.ValueType,
                        conformance.ImplementingType,
                        out var associatedType))
                    {
                        if (DiagnosticCount == diagnosticCount)
                        {
                            Report("TRM3007",
                                "The associated type binding does not name a complete supported type.",
                                associatedTypeBinding.Node.Span);
                        }
                        return false;
                    }
                    associatedTypes.Add(
                        new BoundInterfaceAssociatedTypeBinding(
                            requirement, associatedType));
                    consumedBindings.Add(associatedTypeBinding.Node);
                    continue;
                }
                if (definition.AssociatedDefaultType
                    is BoundInterfaceType associatedDefault)
                {
                    associatedTypes.Add(
                        new BoundInterfaceAssociatedTypeBinding(requirement,
                            BoundInterfaceContract
                                .SubstituteTypeParameters(associatedDefault,
                                    requirement.DeclaringInterface
                                        .TypeArguments)));
                    continue;
                }
                Report("TRM3007",
                    "A bodyless associated type requirement requires an explicit source binding.",
                    conformance.Conformance.Span);
                return false;
            }
            if (definition.Kind == BoundInterfaceMemberKind.Constant)
            {
                if (!TrySelectAssociatedBinding(requirement, definition,
                        explicitAssociatedConstants.Value,
                        out var explicitConstantBinding)
                    || explicitConstantBinding
                        is not AssociatedBindingSyntax
                            associatedConstantSyntax)
                {
                    Report("TRM3007",
                        "Every associated constant requirement requires exactly one explicit source binding.",
                        conformance.Conformance.Span);
                    return false;
                }
                var diagnosticCount = DiagnosticCount;
                if (!TryBindAssociatedConstant(associatedConstantSyntax,
                    requirement, conformance, out var associatedConstant))
                {
                    if (DiagnosticCount == diagnosticCount)
                    {
                        Report("TRM3007",
                            "The associated constant binding is not a typed compile-time constant for the declared domain.",
                            associatedConstantSyntax.Node.Span);
                    }
                    return false;
                }
                associatedConstants.Add(associatedConstant);
                consumedBindings.Add(associatedConstantSyntax.Node);
                continue;
            }

            Report("TRM3007",
                "This source conformance requires an associated binding or member form that is not yet supported.",
                conformance.Conformance.Span);
            return false;
        }

        witness = new BoundInterfaceWitness(
            conformance.ImplementingType,
            conformance.InterfaceType,
            conformance.SourceInputOrdinal,
            conformance.Conformance.Span,
            members.ToImmutable(),
            associatedTypes.ToImmutable(),
            associatedConstants.ToImmutable());
        consumedAssociatedBindings = consumedBindings.ToImmutable();
        return true;
    }

    private ImmutableArray<AssociatedBindingSyntax>?
        BindAssociatedBindingSyntax(
            PendingInterfaceConformance conformance,
            SyntaxKind bindingKind)
    {
        var result = ImmutableArray.CreateBuilder<AssociatedBindingSyntax>();
        foreach (var node in conformance.Declaration.Children
            .OfType<SyntaxNode>().Where(member =>
                member.Kind == bindingKind))
        {
            var typeNodes = node.Children.OfType<SyntaxNode>().ToArray();
            var memberName = node.Children.OfType<SyntaxTokenElement>()
                .LastOrDefault(static token =>
                    token.Token.Kind == TokenKind.Identifier)?.Token.Name;
            var ownerType = bindingKind
                == SyntaxKind.AssociatedTypeBinding
                ? typeNodes.ElementAtOrDefault(0)
                : typeNodes.ElementAtOrDefault(1);
            var valueType = bindingKind == SyntaxKind.AssociatedTypeBinding
                ? typeNodes.ElementAtOrDefault(1)
                : typeNodes.ElementAtOrDefault(0);
            var value = bindingKind == SyntaxKind.AssociatedConstantBinding
                ? typeNodes.ElementAtOrDefault(2) : null;
            if (ownerType is null || valueType is null
                || memberName is null
                || (bindingKind
                        == SyntaxKind.AssociatedConstantBinding
                    && value is null)
                || !TryBindInterfaceConformanceType(ownerType,
                    conformance.ImplementingType,
                    out var boundOwner))
            {
                SetContext(conformance.Tree, node);
                Report("TRM3007",
                    "The associated binding must name one declared interface member.",
                    node.Span);
                return null;
            }
            result.Add(new AssociatedBindingSyntax(node, boundOwner,
                memberName, valueType, value));
        }
        return result.ToImmutable();
    }

    private static bool TrySelectAssociatedBinding(
        BoundInterfaceMemberIdentity requirement,
        BoundInterfaceMemberDefinition member,
        ImmutableArray<AssociatedBindingSyntax> bindings,
        out AssociatedBindingSyntax? selected)
    {
        selected = null;
        var matches = bindings.Where(binding =>
                binding.MemberName == member.Name
                && BoundInterfaceContract.AreEquivalent(binding.Owner,
                    requirement.DeclaringInterface))
            .ToArray();
        if (matches.Length > 1)
        {
            return false;
        }
        selected = matches.SingleOrDefault();
        return true;
    }

    private bool TryBindAssociatedValueType(
        SyntaxNode node, PrimitiveType implementingType,
        out BoundInterfaceType type)
    {
        type = null!;
        if (!TryGetInterfaceConformanceOwnerContext(implementingType,
            out var ownerTypeParameterMap, out var ownerArguments))
        {
            return false;
        }
        var diagnosticCount = DiagnosticCount;
        if (BindInterfaceType(node, ownerTypeParameterMap,
                _interfaceDeclarationGroups, ownerTypeParameterMap.Count)
                is not BoundInterfaceType boundType
            || DiagnosticCount != diagnosticCount)
        {
            return false;
        }
        type = ownerArguments.IsEmpty
            ? boundType
            : BoundInterfaceContract.SubstituteTypeParameters(boundType,
                ownerArguments);
        return BoundInterfaceContract.IsValidClosedType(type,
            [.. _interfaces], static _ => true,
            [.. _genericNominalTypeDefinitions],
            [.. _sourceGenericTypeDefinitions]);
    }

    private bool TryBindAssociatedConstant(
        AssociatedBindingSyntax binding,
        BoundInterfaceMemberIdentity requirement,
        PendingInterfaceConformance conformance,
        out BoundInterfaceAssociatedConstantBinding associatedConstant)
    {
        associatedConstant = null!;
        if (!TryBindAssociatedValueType(binding.ValueType,
                conformance.ImplementingType, out var explicitType)
            || !BoundInterfaceContract.TryGetInterfaceMemberDefinition(
                requirement, [.. _interfaces], static _ => true,
                out var member,
                genericNominalTypeDefinitions:
                    [.. _genericNominalTypeDefinitions],
                sourceGenericTypeDefinitions:
                    [.. _sourceGenericTypeDefinitions])
            || member?.AssociatedConstantType
                is not BoundInterfaceType declaredType)
        {
            return false;
        }

        var expectedType = BoundInterfaceContract.SubstituteTypeParameters(
            declaredType,
            requirement.DeclaringInterface.TypeArguments);
        if (!BoundInterfaceContract.AreEquivalent(explicitType,
                expectedType)
            || !TryResolveInterfaceValueType(expectedType,
                requirement.DeclaringInterface.TypeArguments,
                binding.Node.Span, out var expectedPrimitive))
        {
            SetContext(conformance.Tree, binding.Node);
            Report("TRM3007",
                "The associated constant binding must preserve the declared complete constant type.",
                binding.Node.Span);
            return false;
        }

        SetContext(conformance.Tree, binding.Value!);
        var valueExpression = BindExpression(binding.Value!,
            expectedPrimitive);
        if (FoldScalarConstant(valueExpression)
            is not BoundConstant constant
            || constant.Type != expectedPrimitive)
        {
            Report("TRM3007",
                "An associated constant binding must be a typed compile-time constant.",
                binding.Value!.Span);
            return false;
        }

        associatedConstant = new BoundInterfaceAssociatedConstantBinding(
            requirement, expectedType, constant);
        return true;
    }

    private bool TryBindInterfaceCallable(
        PendingInterfaceConformance conformance,
        string implementingTypeName,
        BoundInterfaceMemberIdentity requirement,
        BoundInterfaceMemberDefinition member,
        ImmutableArray<BoundInterfaceWitnessMember>.Builder bindings,
        List<(int MemberOrdinal, string TemplateName)>
            pendingGenericTargets)
    {
        if (!member.TypeParameters.IsEmpty)
        {
            return TryBindGenericInterfaceCallable(conformance,
                implementingTypeName, requirement, member, bindings,
                pendingGenericTargets);
        }

        if (!member.Constraints.IsEmpty
            || !member.Attributes.IsEmpty
            || !member.ContractTokens.IsEmpty
            || !member.Dependencies.IsEmpty
            || !member.Accessors.IsEmpty
            || member.IsAsync || member.NativeCallableConvention is not null
            || member.ResultType is not BoundInterfaceType resultType
            || member.Parameters.Any(static parameter =>
                parameter.Attributes.IsEmpty == false
                || parameter.AttributeTokens.IsEmpty == false))
        {
            Report("TRM3007",
                "The interface callable requires generic, effect, dependency, native, or parameter contracts outside the current source witness binder.",
                conformance.Conformance.Span);
            return false;
        }

        var functionName = implementingTypeName + "." + member.Name;
        var matches = new List<(int Index, Signature Signature)>();
        for (var index = 0; index < _signatures.Count; index++)
        {
            var signature = _signatures[index];
            if (signature.ExplicitInterfaceOwner
                is BoundInterfaceType explicitOwner)
            {
                if (!BoundInterfaceContract.AreEquivalent(explicitOwner,
                        requirement.DeclaringInterface))
                {
                    continue;
                }
            }
            else if (signature.IsPrivate
                || (member.Accessibility
                        == BoundInterfaceAccessibility.Public
                    && signature.IsInternal))
            {
                continue;
            }
            if ((signature.ExplicitInterfaceOwner is null
                    && signature.Name != functionName)
                || !HasInterfaceImplementationOwner(signature,
                    implementingTypeName)
                || signature.IsConstructor
                || signature.IsCleanup || signature.IsOperator
                || signature.IsStatic != member.IsStatic
                || (signature.IsMutable
                    && !member.RequiresMutableReceiver)
                || signature.Parameters.Length != member.Parameters.Length
                || !TryResolveInterfaceValueType(resultType,
                    requirement.DeclaringInterface.TypeArguments,
                    conformance.Conformance.Span,
                    out var expectedResult)
                || signature.ReturnType != expectedResult)
            {
                continue;
            }

            var parameterTypesMatch = true;
            for (var parameterIndex = 0;
                parameterIndex < member.Parameters.Length;
                parameterIndex++)
            {
                var requiredParameter = member.Parameters[parameterIndex];
                var implementationParameter
                    = signature.Parameters[parameterIndex];
                if (!TryResolveInterfaceValueType(
                        requiredParameter.Type,
                        requirement.DeclaringInterface.TypeArguments,
                        conformance.Conformance.Span,
                        out var expectedParameter)
                    || implementationParameter.Type != expectedParameter
                    || !InterfaceParameterModeMatches(
                        implementationParameter, requiredParameter.Mode))
                {
                    parameterTypesMatch = false;
                    break;
                }
            }
            if (parameterTypesMatch)
            {
                matches.Add((index, signature));
            }
        }

        if (matches.Count > 1)
        {
            Report("TRM3007",
                $"More than one source member can satisfy interface requirement '{member.Name}'.",
                conformance.Conformance.Span);
            return false;
        }
        if (matches.Count == 0)
        {
            if (member.HasBody)
            {
                bindings.Add(new BoundInterfaceWitnessMember(requirement,
                    BoundInterfaceWitnessTargetKind.Default, -1,
                    requirement));
                return true;
            }

            Report("TRM3007",
                $"Type '{implementingTypeName}' has no implementation for interface requirement '{member.Name}'.",
                conformance.Conformance.Span);
            return false;
        }

        var (functionIndex, implementation) = matches[0];
        if ((implementation.RequiresUnsafeCaller
                && !member.RequiresUnsafeCaller)
            || (member.DoesNotReturn && !implementation.DoesNotReturn)
            || !implementation.DependencyContracts.IsEmpty
            || !implementation.DeclarationContractClauses.IsEmpty)
        {
            Report("TRM3007",
                $"The implementation of interface requirement '{member.Name}' narrows its published caller contract.",
                conformance.Conformance.Span);
            return false;
        }

        bindings.Add(new BoundInterfaceWitnessMember(requirement,
            implementation.ExplicitInterfaceOwner is null
                ? BoundInterfaceWitnessTargetKind.Implementation
                : BoundInterfaceWitnessTargetKind.ExplicitImplementation,
            functionIndex,
            null));
        return true;
    }

    private bool TryBindGenericInterfaceCallable(
        PendingInterfaceConformance conformance,
        string implementingTypeName,
        BoundInterfaceMemberIdentity requirement,
        BoundInterfaceMemberDefinition member,
        ImmutableArray<BoundInterfaceWitnessMember>.Builder bindings,
        List<(int MemberOrdinal, string TemplateName)>
            pendingGenericTargets)
    {
        bool IsSupportedFormal(BoundInterfaceFormal requirementFormal,
            GenericTypeParameter implementationFormal, int ordinal)
            => requirementFormal.Ordinal == ordinal
                && requirementFormal.Kind
                    == BoundInterfaceFormalKind.Type
                && !requirementFormal.IsSelf
                && requirementFormal.Attributes.IsEmpty
                && requirementFormal.AttributeTokens.IsEmpty
                && requirementFormal.ConstantType is null
                && requirementFormal.TypeDefault is null
                && requirementFormal.ConstantDefault is null
                && implementationFormal.OwnershipDomain == 1
                && implementationFormal.NullabilityDomain == 1
                && implementationFormal.Operations.IsEmpty
                && implementationFormal.TypeDefaultSyntax is null
                && !implementationFormal.RequiresBlittable
                && !implementationFormal.RequiresWritable
                && MatchesInterfaceConstraints(ordinal,
                    implementationFormal.InterfaceConstraints)
                && implementationFormal.DefaultTypeParameterDependencies
                    .IsEmpty;

        bool MatchesInterfaceConstraints(int ordinal,
            ImmutableArray<BoundInterfaceType> implementationConstraints)
        {
            var requiredConstraints = member.Constraints.Where(constraint =>
                constraint.FormalScope
                    == BoundInterfaceTypeParameterScope.Member
                && constraint.FormalOrdinal == ordinal).ToArray();
            if (requiredConstraints.Length
                    != implementationConstraints.Length
                || implementationConstraints.IsDefault)
            {
                return false;
            }

            var unmatched = implementationConstraints.ToList();
            foreach (var constraint in requiredConstraints)
            {
                if (constraint.Kind != BoundInterfaceConstraintKind.Type
                    || constraint.Type is not BoundInterfaceType requiredType)
                {
                    return false;
                }

                var substituted = BoundInterfaceContract
                    .SubstituteTypeParameters(requiredType,
                        requirement.DeclaringInterface.TypeArguments);
                var matchIndex = unmatched.FindIndex(candidate =>
                    BoundInterfaceContract.AreEquivalent(substituted,
                        candidate));
                if (matchIndex < 0)
                {
                    return false;
                }
                unmatched.RemoveAt(matchIndex);
            }

            return unmatched.Count == 0;
        }

        bool MatchesType(BoundInterfaceType requirementType,
            GenericParameter implementationParameter)
        {
            var substituted = BoundInterfaceContract
                .SubstituteTypeParameters(requirementType,
                    requirement.DeclaringInterface.TypeArguments);
            if (substituted.Kind == BoundInterfaceTypeKind.TypeParameter
                && substituted.TypeParameterScope
                    == BoundInterfaceTypeParameterScope.Member)
            {
                return implementationParameter.ScalarType is null
                    && implementationParameter.TypeParameterIndex
                        == substituted.TypeParameterOrdinal;
            }

            return implementationParameter.TypeParameterIndex < 0
                && implementationParameter.ScalarType
                    is PrimitiveType implementationType
                && TryResolveInterfaceValueType(substituted, [],
                    conformance.Conformance.Span, out var requirementTypeValue)
                && implementationType == requirementTypeValue;
        }

        bool MatchesResult(BoundInterfaceType requirementType,
            GenericTemplate implementation)
        {
            var substituted = BoundInterfaceContract
                .SubstituteTypeParameters(requirementType,
                    requirement.DeclaringInterface.TypeArguments);
            if (substituted.Kind == BoundInterfaceTypeKind.TypeParameter
                && substituted.TypeParameterScope
                    == BoundInterfaceTypeParameterScope.Member)
            {
                return implementation.ScalarReturnType is null
                    && implementation.ReturnTypeTerm is null
                    && implementation.ReturnTypeParameterIndex
                        == substituted.TypeParameterOrdinal;
            }

            return implementation.ReturnTypeParameterIndex < 0
                && implementation.ReturnTypeTerm is null
                && implementation.ScalarReturnType
                    is PrimitiveType implementationType
                && TryResolveInterfaceValueType(substituted, [],
                    conformance.Conformance.Span, out var requirementTypeValue)
                && implementationType == requirementTypeValue;
        }

        if (member.Kind != BoundInterfaceMemberKind.Callable
            || member.IsStatic
            || member.IsExternal
            || member.IsPartial
            || member.TypeParameters.IsDefault
            || member.TypeParameters.IsEmpty
            || member.Constraints.IsDefault
            || member.Constraints.Any(constraint =>
                constraint.FormalScope
                    != BoundInterfaceTypeParameterScope.Member
                || constraint.Kind != BoundInterfaceConstraintKind.Type
                || constraint.Type is null
                || (uint)constraint.FormalOrdinal
                    >= (uint)member.TypeParameters.Length)
            || member.Attributes.IsDefault
            || member.AttributeTokens.IsDefault
            || member.Attributes.Any(static attribute =>
                !StringComparer.Ordinal.Equals(attribute,
                    "DoesNotReturn"))
            || member.AttributeTokens.Length != member.Attributes.Length
            || member.DoesNotReturn
                != member.Attributes.Any(static attribute =>
                    StringComparer.Ordinal.Equals(attribute,
                        "DoesNotReturn"))
            || !member.ContractTokens.IsEmpty
            || !member.Dependencies.IsEmpty
            || !member.Accessors.IsEmpty
            || member.IsAsync || member.NativeCallableConvention is not null
            || member.ResultType is not BoundInterfaceType resultType
            || member.Parameters.Any(static parameter =>
                !parameter.Attributes.IsEmpty
                || !parameter.AttributeTokens.IsEmpty))
        {
            Report("TRM3007",
                "The generic interface callable has contracts outside the supported source witness mapping.",
                conformance.Conformance.Span);
            return false;
        }

        var matches = _templates.Values.Where(template =>
            template.ContainingType == implementingTypeName
            && template.MemberName == member.Name
            && template.OwnerType == conformance.ImplementingType
            && template.IsStatic == member.IsStatic
            && template.TypeParameters.Length
                == member.TypeParameters.Length
            && template.FormalOrder.Length
                == member.TypeParameters.Length
            && (!template.IsMutable
                || member.RequiresMutableReceiver)
            && template.ConstantFormals.IsEmpty
            && template.Parameters.Length == member.Parameters.Length
            && template.OwnerGenericTypeDefinitionIndex < 0
            && template.OwnerTypeArguments.IsEmpty
            && template.OwnerConstantArguments.IsEmpty
            && template.ReturnTypeTerm is null
            && template.ReturnTypeTermSyntax is null
            && template.DependencyContracts.IsEmpty
            && template.SourceDeclarationContractClauses.IsEmpty
            && template.BoundDeclarationContractClauses.IsEmpty
            && template.PureEvaluationFormSchema is null
            && template.ConstEvalSchema is null
            && (member.Accessibility
                    != BoundInterfaceAccessibility.Public
                || template.IsPublic)
            && (!template.RequiresUnsafeCaller
                || member.RequiresUnsafeCaller)
            && (!member.DoesNotReturn || template.DoesNotReturn)
            && template.TypeParameters.Select((formal, ordinal) =>
                IsSupportedFormal(member.TypeParameters[ordinal], formal,
                    ordinal)).All(static supported => supported)
            && template.FormalOrder.Select((slot, ordinal) =>
                slot.Ordinal == ordinal
                    && slot.Kind == BoundGenericMethodFormalKind.Type
                    && slot.TypeFormalOrdinal == ordinal
                    && slot.ConstantFormalOrdinal == -1)
                .All(static ordered => ordered)
            && template.Parameters.Select((parameter, ordinal) =>
                parameter.TypeTerm is null
                    && parameter.TypeTermSyntax is null
                    && MatchesType(member.Parameters[ordinal].Type,
                        parameter)
                    && (member.Parameters[ordinal].Mode
                        switch {
                            BoundInterfaceParameterMode.Value =>
                                !parameter.IsOut && !parameter.IsInit,
                            BoundInterfaceParameterMode.Out =>
                                parameter.IsOut && !parameter.IsInit,
                            BoundInterfaceParameterMode.Init =>
                                parameter.IsInit && !parameter.IsOut,
                            _ => false,
                        }))
                .All(static matchesParameter => matchesParameter)
            && MatchesResult(resultType, template)).ToArray();

        if (matches.Length != 1)
        {
            Report("TRM3007",
                matches.Length == 0
                    ? $"Type '{implementingTypeName}' has no generic method matching interface requirement '{member.Name}'."
                    : $"More than one generic method can satisfy interface requirement '{member.Name}'.",
                conformance.Conformance.Span);
            return false;
        }

        var selected = matches[0];
        var memberOrdinal = bindings.Count;
        bindings.Add(new BoundInterfaceWitnessMember(requirement,
            BoundInterfaceWitnessTargetKind.Implementation, -1, null) {
            PendingGenericMethodName = selected.Name,
        });
        pendingGenericTargets.Add((memberOrdinal, selected.Name));
        return true;
    }

    private static bool HasInterfaceImplementationOwner(
        Signature signature, string implementingTypeName)
        => StringComparer.Ordinal.Equals(signature.StructName,
                implementingTypeName)
            || signature.Name.StartsWith(
                implementingTypeName + ".",
                StringComparison.Ordinal);

    private bool TryBindInterfacePropertyGetter(
        PendingInterfaceConformance conformance,
        string implementingTypeName,
        BoundInterfaceMemberIdentity requirement,
        BoundInterfaceMemberDefinition member,
        ImmutableArray<BoundInterfaceWitnessMember>.Builder bindings)
    {
        var accessor = member.Accessors[requirement.AccessorOrdinal];
        if (!member.TypeParameters.IsEmpty
            || !member.Constraints.IsEmpty
            || !member.Attributes.IsEmpty
            || !member.ContractTokens.IsEmpty
            || !accessor.Attributes.IsEmpty
            || !accessor.ContractTokens.IsEmpty
            || !accessor.Dependencies.IsEmpty
            || !member.Parameters.IsEmpty
            || accessor.Kind != BoundInterfaceAccessorKind.Get
            || member.ResultType is not BoundInterfaceType resultType
            || !TryResolveInterfaceValueType(resultType,
                requirement.DeclaringInterface.TypeArguments,
                conformance.Conformance.Span, out var expectedResult))
        {
            Report("TRM3007",
                "The property getter has contracts or a type outside the supported source witness binding.",
                conformance.Conformance.Span);
            return false;
        }

        var functionName = implementingTypeName + ".#get:" + member.Name;
        var matches = new List<(int Index, Signature Signature)>();
        for (var index = 0; index < _signatures.Count; index++)
        {
            var signature = _signatures[index];
            if (signature.Name == functionName
                && signature.StructName == implementingTypeName
                && signature.PropertyName == member.Name
                && signature.Parameters.IsEmpty
                && signature.ReturnType == expectedResult
                && signature.IsStatic == member.IsStatic
                && (!signature.IsMutable || accessor.IsMutable)
                && !signature.IsPrivate
                && (accessor.Accessibility
                        != BoundInterfaceAccessibility.Public
                    || !signature.IsInternal)
                && !signature.IsConstructor
                && !signature.IsCleanup)
            {
                matches.Add((index, signature));
            }
        }
        if (matches.Count > 1)
        {
            Report("TRM3007",
                $"More than one property getter can satisfy interface requirement '{member.Name}'.",
                conformance.Conformance.Span);
            return false;
        }
        if (matches.Count == 0)
        {
            if (accessor.HasBody)
            {
                bindings.Add(new BoundInterfaceWitnessMember(requirement,
                    BoundInterfaceWitnessTargetKind.Default, -1,
                    requirement));
                return true;
            }
            Report("TRM3007",
                $"Type '{implementingTypeName}' has no getter for interface property '{member.Name}'.",
                conformance.Conformance.Span);
            return false;
        }

        var (functionIndex, implementation) = matches[0];
        if ((implementation.RequiresUnsafeCaller
                && !accessor.RequiresUnsafeCaller)
            || (accessor.DoesNotReturn && !implementation.DoesNotReturn)
            || !implementation.DependencyContracts.IsEmpty
            || !implementation.DeclarationContractClauses.IsEmpty)
        {
            Report("TRM3007",
                $"The getter for interface property '{member.Name}' narrows its published caller contract.",
                conformance.Conformance.Span);
            return false;
        }

        bindings.Add(new BoundInterfaceWitnessMember(requirement,
            implementation.ExplicitInterfaceOwner is null
                ? BoundInterfaceWitnessTargetKind.Implementation
                : BoundInterfaceWitnessTargetKind.ExplicitImplementation,
            functionIndex, null));
        return true;
    }

    private bool TryBindInterfaceIndexerAccessor(
        PendingInterfaceConformance conformance,
        string implementingTypeName,
        BoundInterfaceMemberIdentity requirement,
        BoundInterfaceMemberDefinition member,
        ImmutableArray<BoundInterfaceWitnessMember>.Builder bindings)
    {
        if ((uint)requirement.AccessorOrdinal
            >= (uint)member.Accessors.Length)
        {
            return false;
        }

        return member.Accessors[requirement.AccessorOrdinal].Kind switch {
            BoundInterfaceAccessorKind.Get =>
                TryBindInterfaceIndexerGetter(conformance,
                    implementingTypeName, requirement, member, bindings),
            BoundInterfaceAccessorKind.Set =>
                TryBindInterfaceIndexerSetter(conformance,
                    implementingTypeName, requirement, member, bindings),
            _ => false,
        };
    }

    private bool TryBindInterfaceIndexerGetter(
        PendingInterfaceConformance conformance,
        string implementingTypeName,
        BoundInterfaceMemberIdentity requirement,
        BoundInterfaceMemberDefinition member,
        ImmutableArray<BoundInterfaceWitnessMember>.Builder bindings)
    {
        var accessor = member.Accessors[requirement.AccessorOrdinal];
        if (!member.TypeParameters.IsEmpty
            || !member.Constraints.IsEmpty
            || !member.Attributes.IsEmpty
            || !member.ContractTokens.IsEmpty
            || !accessor.Attributes.IsEmpty
            || !accessor.ContractTokens.IsEmpty
            || !accessor.Dependencies.IsEmpty
            || accessor.Kind != BoundInterfaceAccessorKind.Get
            || member.Parameters.Any(static parameter =>
                parameter.Mode != BoundInterfaceParameterMode.Value
                || !parameter.Attributes.IsEmpty
                || !parameter.AttributeTokens.IsEmpty)
            || member.ResultType is not BoundInterfaceType resultType
            || !TryResolveInterfaceValueType(resultType,
                requirement.DeclaringInterface.TypeArguments,
                conformance.Conformance.Span, out var expectedResult))
        {
            Report("TRM3007",
                "The indexer getter has contracts or a type outside the supported source witness binding.",
                conformance.Conformance.Span);
            return false;
        }

        var expectedParameters = ImmutableArray.CreateBuilder<PrimitiveType>(
            member.Parameters.Length);
        foreach (var parameter in member.Parameters)
        {
            if (!TryResolveInterfaceValueType(parameter.Type,
                    requirement.DeclaringInterface.TypeArguments,
                    conformance.Conformance.Span, out var expectedParameter))
            {
                return false;
            }

            expectedParameters.Add(expectedParameter);
        }

        var functionName = implementingTypeName + ".#get:" + member.Name;
        var matches = new List<(int Index, Signature Signature)>();
        for (var index = 0; index < _signatures.Count; index++)
        {
            var signature = _signatures[index];
            var matchesExplicitOwner = signature.ExplicitInterfaceOwner
                is BoundInterfaceType explicitOwner
                && BoundInterfaceContract.AreEquivalent(explicitOwner,
                    requirement.DeclaringInterface);
            var hasAllowedNameAndAccess
                = matchesExplicitOwner ? signature.IsPrivate
                : signature.ExplicitInterfaceOwner is null
                    && signature.Name == functionName
                    && !signature.IsPrivate
                    && (accessor.Accessibility
                            != BoundInterfaceAccessibility.Public
                        || !signature.IsInternal);
            if (!HasInterfaceImplementationOwner(signature,
                    implementingTypeName)
                || signature.PropertyName != member.Name
                || signature.Parameters.Length != expectedParameters.Count
                || signature.ReturnType != expectedResult
                || signature.IsStatic != member.IsStatic
                || (signature.IsMutable && !accessor.IsMutable)
                || !hasAllowedNameAndAccess
                || signature.IsConstructor
                || signature.IsCleanup)
            {
                continue;
            }

            var parametersMatch = true;
            for (var ordinal = 0; ordinal < expectedParameters.Count;
                ordinal++)
            {
                if (signature.Parameters[ordinal].Type
                        != expectedParameters[ordinal]
                    || !InterfaceParameterModeMatches(
                        signature.Parameters[ordinal],
                        member.Parameters[ordinal].Mode))
                {
                    parametersMatch = false;
                    break;
                }
            }
            if (parametersMatch)
            {
                matches.Add((index, signature));
            }
        }
        if (matches.Count > 1)
        {
            Report("TRM3007",
                $"More than one indexer getter can satisfy interface requirement '{member.Name}'.",
                conformance.Conformance.Span);
            return false;
        }
        if (matches.Count == 0)
        {
            if (accessor.HasBody)
            {
                bindings.Add(new BoundInterfaceWitnessMember(requirement,
                    BoundInterfaceWitnessTargetKind.Default, -1,
                    requirement));
                return true;
            }
            Report("TRM3007",
                $"Type '{implementingTypeName}' has no getter for interface indexer '{member.Name}'.",
                conformance.Conformance.Span);
            return false;
        }

        var (functionIndex, implementation) = matches[0];
        if ((implementation.RequiresUnsafeCaller
                && !accessor.RequiresUnsafeCaller)
            || (accessor.DoesNotReturn && !implementation.DoesNotReturn)
            || !implementation.DependencyContracts.IsEmpty
            || !implementation.DeclarationContractClauses.IsEmpty)
        {
            Report("TRM3007",
                $"The getter for interface indexer '{member.Name}' narrows its published caller contract.",
                conformance.Conformance.Span);
            return false;
        }

        bindings.Add(new BoundInterfaceWitnessMember(requirement,
            implementation.ExplicitInterfaceOwner is null
                ? BoundInterfaceWitnessTargetKind.Implementation
                : BoundInterfaceWitnessTargetKind.ExplicitImplementation,
            functionIndex, null));
        return true;
    }

    private bool TryBindInterfaceIndexerSetter(
        PendingInterfaceConformance conformance,
        string implementingTypeName,
        BoundInterfaceMemberIdentity requirement,
        BoundInterfaceMemberDefinition member,
        ImmutableArray<BoundInterfaceWitnessMember>.Builder bindings)
    {
        var accessor = member.Accessors[requirement.AccessorOrdinal];
        if (!member.TypeParameters.IsEmpty
            || !member.Constraints.IsEmpty
            || !member.Attributes.IsEmpty
            || !member.ContractTokens.IsEmpty
            || !accessor.Attributes.IsEmpty
            || !accessor.ContractTokens.IsEmpty
            || !accessor.Dependencies.IsEmpty
            || accessor.Kind != BoundInterfaceAccessorKind.Set
            || member.Parameters.Any(static parameter =>
                parameter.Mode != BoundInterfaceParameterMode.Value
                || !parameter.Attributes.IsEmpty
                || !parameter.AttributeTokens.IsEmpty)
            || member.ResultType is not BoundInterfaceType resultType
            || !TryResolveInterfaceValueType(resultType,
                requirement.DeclaringInterface.TypeArguments,
                conformance.Conformance.Span, out var expectedValue))
        {
            Report("TRM3007",
                "The indexer setter has contracts or a type outside the supported source witness binding.",
                conformance.Conformance.Span);
            return false;
        }

        var expectedParameters = ImmutableArray.CreateBuilder<PrimitiveType>(
            member.Parameters.Length);
        foreach (var parameter in member.Parameters)
        {
            if (!TryResolveInterfaceValueType(parameter.Type,
                    requirement.DeclaringInterface.TypeArguments,
                    conformance.Conformance.Span, out var expectedParameter))
            {
                return false;
            }

            expectedParameters.Add(expectedParameter);
        }

        var functionName = implementingTypeName + ".#set:" + member.Name;
        var matches = new List<(int Index, Signature Signature)>();
        for (var index = 0; index < _signatures.Count; index++)
        {
            var signature = _signatures[index];
            var matchesExplicitOwner = signature.ExplicitInterfaceOwner
                is BoundInterfaceType explicitOwner
                && BoundInterfaceContract.AreEquivalent(explicitOwner,
                    requirement.DeclaringInterface);
            var hasAllowedNameAndAccess
                = matchesExplicitOwner ? signature.IsPrivate
                : signature.ExplicitInterfaceOwner is null
                    && signature.Name == functionName
                    && !signature.IsPrivate
                    && (accessor.Accessibility
                            != BoundInterfaceAccessibility.Public
                        || !signature.IsInternal);
            if (!HasInterfaceImplementationOwner(signature,
                    implementingTypeName)
                || signature.PropertyName != member.Name
                || signature.Parameters.Length
                    != expectedParameters.Count + 1
                || signature.ReturnType != PrimitiveType.Void
                || signature.Parameters[^1].Type != expectedValue
                || !InterfaceParameterModeMatches(signature.Parameters[^1],
                    BoundInterfaceParameterMode.Value)
                || signature.Parameters[^1]
                    .IsCanonicalImplicitReference
                || signature.IsStatic != member.IsStatic
                || (signature.IsMutable && !accessor.IsMutable)
                || !hasAllowedNameAndAccess
                || signature.IsConstructor
                || signature.IsCleanup)
            {
                continue;
            }

            var parametersMatch = true;
            for (var ordinal = 0; ordinal < expectedParameters.Count;
                ordinal++)
            {
                if (signature.Parameters[ordinal].Type
                        != expectedParameters[ordinal]
                    || !InterfaceParameterModeMatches(
                        signature.Parameters[ordinal],
                        member.Parameters[ordinal].Mode))
                {
                    parametersMatch = false;
                    break;
                }
            }
            if (parametersMatch)
            {
                matches.Add((index, signature));
            }
        }
        if (matches.Count > 1)
        {
            Report("TRM3007",
                $"More than one indexer setter can satisfy interface requirement '{member.Name}'.",
                conformance.Conformance.Span);
            return false;
        }
        if (matches.Count == 0)
        {
            if (accessor.HasBody)
            {
                bindings.Add(new BoundInterfaceWitnessMember(requirement,
                    BoundInterfaceWitnessTargetKind.Default, -1,
                    requirement));
                return true;
            }
            Report("TRM3007",
                $"Type '{implementingTypeName}' has no setter for interface indexer '{member.Name}'.",
                conformance.Conformance.Span);
            return false;
        }

        var (functionIndex, implementation) = matches[0];
        if ((implementation.RequiresUnsafeCaller
                && !accessor.RequiresUnsafeCaller)
            || (accessor.DoesNotReturn && !implementation.DoesNotReturn)
            || !implementation.DependencyContracts.IsEmpty
            || !implementation.DeclarationContractClauses.IsEmpty)
        {
            Report("TRM3007",
                $"The setter for interface indexer '{member.Name}' narrows its published caller contract.",
                conformance.Conformance.Span);
            return false;
        }

        bindings.Add(new BoundInterfaceWitnessMember(requirement,
            implementation.ExplicitInterfaceOwner is null
                ? BoundInterfaceWitnessTargetKind.Implementation
                : BoundInterfaceWitnessTargetKind.ExplicitImplementation,
            functionIndex, null));
        return true;
    }

    private bool TryResolveInterfaceValueType(
        BoundInterfaceType type,
        ImmutableArray<BoundInterfaceTypeArgument> typeArguments,
        TextSpan useSite, out PrimitiveType primitiveType)
    {
        var substituted = BoundInterfaceContract.SubstituteTypeParameters(
            type, typeArguments);
        if (substituted.Kind == BoundInterfaceTypeKind.TypeParameter
            && _genericProofFunctions is not null
            && _genericContext is GenericInstantiation context)
        {
            if (substituted.TypeParameterScope
                == BoundInterfaceTypeParameterScope.Member)
            {
                var slot = context.Template.FormalOrder
                    .FirstOrDefault(candidate =>
                        candidate.Ordinal
                            == substituted.TypeParameterOrdinal
                        && candidate.Kind
                            == BoundGenericMethodFormalKind.Type);
                if (slot is not null
                    && (uint)slot.TypeFormalOrdinal
                        < (uint)context.Types.Length)
                {
                    primitiveType = context.Types[slot.TypeFormalOrdinal];
                    return true;
                }
            }
            else if (substituted.TypeParameterScope
                    == BoundInterfaceTypeParameterScope.Interface
                && (uint)substituted.TypeParameterOrdinal
                    < (uint)context.Template.OwnerTypeArguments.Length)
            {
                primitiveType = context.Template.OwnerTypeArguments[
                    substituted.TypeParameterOrdinal];
                return true;
            }
        }
        if (substituted.Kind == BoundInterfaceTypeKind.Primitive
            && !substituted.IsWritable && !substituted.IsOptional
            && !substituted.IsGarbageCollected
            && !substituted.IsInitialization)
        {
            primitiveType = substituted.PrimitiveType;
            return true;
        }
        if (substituted.Kind == BoundInterfaceTypeKind.CheckedReference
            && substituted.TypeArguments is [
                { Kind: BoundInterfaceTypeArgumentKind.Type,
                    Type: BoundInterfaceType referent },
            ]
            && TryResolveInterfaceValueType(referent, [], useSite,
                out var referentType))
        {
            var diagnosticCount = DiagnosticCount;
            primitiveType = GetCheckedReferenceType(
                new CheckedReferenceTypeDefinition(referentType,
                    substituted.IsWritable,
                    substituted.IsGarbageCollected,
                    substituted.IsInitialization,
                    substituted.IsOptional),
                useSite);
            return DiagnosticCount == diagnosticCount;
        }

        primitiveType = default;
        return false;
    }

    private static bool InterfaceParameterModeMatches(
        Parameter implementation,
        BoundInterfaceParameterMode requirement) =>
        requirement switch {
            BoundInterfaceParameterMode.Value =>
                !implementation.IsOut && !implementation.IsInit
                    && !implementation.IsImplicitRef,
            BoundInterfaceParameterMode.Out => implementation.IsOut
                && !implementation.IsInit && !implementation.IsImplicitRef,
            BoundInterfaceParameterMode.Init => implementation.IsInit
                && !implementation.IsOut && !implementation.IsImplicitRef,
            BoundInterfaceParameterMode.ImplicitRef =>
                implementation.IsImplicitRef && !implementation.IsOut
                    && !implementation.IsInit,
            _ => false,
        };

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance",
        "CA1859:Use concrete types when possible",
        Justification = "Successful binding returns BoundCall while failures return the BoundConstant error sentinel.")]
    private BoundExpression BindInterfaceWitnessInvocation(
        SyntaxNode invocation, SyntaxNode projection, string memberName,
        SyntaxNode arguments,
        BoundInterfaceAccessorKind accessorKind
            = BoundInterfaceAccessorKind.Get)
    {
        var projectionTypes = projection.Children.OfType<SyntaxNode>()
            .ToArray();
        if (projectionTypes is not [{ } receiverNode, { } typeArgumentList]
            || typeArgumentList.Children.OfType<SyntaxNode>().SingleOrDefault()
                is not SyntaxNode interfaceTypeSyntax
            || KnownType(receiverNode) is not PrimitiveType receiverType)
        {
            Report("TRM3005",
                "A qualified interface member call requires one receiver and one complete interface type.",
                invocation.Span);
            return ErrorExpression(invocation.Span);
        }

        var methodTypeParameters = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var methodConstantParameters = new Dictionary<string,
            InterfaceConstantFormal>(StringComparer.Ordinal);
        if (_genericContext is GenericInstantiation genericContext)
        {
            for (var formalOrdinal = 0;
                 formalOrdinal < genericContext.Template.TypeParameters.Length;
                 formalOrdinal++)
            {
                var formal = genericContext.Template.TypeParameters[
                    formalOrdinal];
                var slot = genericContext.Template.FormalOrder
                    .FirstOrDefault(candidate => candidate.Kind
                        == BoundGenericMethodFormalKind.Type
                        && candidate.TypeFormalOrdinal == formalOrdinal);
                if (slot is null
                    || !methodTypeParameters.TryAdd(formal.Name,
                        slot.Ordinal))
                {
                    Report("TRM3005",
                        "The generic interface selection has an invalid type-formal scope.",
                        interfaceTypeSyntax.Span);
                    return ErrorExpression(invocation.Span);
                }
            }
            foreach (var formal in genericContext.Template.ConstantFormals)
            {
                if (!TryCreateGenericInterfaceConstantDomain(
                    genericContext, formal, out var domain))
                {
                    continue;
                }
                methodConstantParameters.Add(formal.Name,
                    new InterfaceConstantFormal(domain,
                        BoundInterfaceTypeParameterScope.Member,
                        formal.Ordinal));
            }
        }

        var diagnosticCount = DiagnosticCount;
        if (BindInterfaceType(interfaceTypeSyntax,
                new Dictionary<string, int>(StringComparer.Ordinal),
                _interfaceDeclarationGroups, 0,
                methodTypeParameters,
                _genericContext?.Template.FormalOrder.Length ?? 0,
                methodConstantParameters: methodConstantParameters)
                is not BoundInterfaceType {
                    Kind: BoundInterfaceTypeKind.Nominal,
                    IsWritable: false,
                    IsOptional: false,
                    IsGarbageCollected: false,
                    IsInitialization: false,
                } boundInterface
            || DiagnosticCount != diagnosticCount)
        {
            return ErrorExpression(invocation.Span);
        }

        var requestedInterface = boundInterface;
        if (_genericContext is GenericInstantiation substitutionContext
            && !TrySubstituteGenericMethodInterfaceType(boundInterface,
                substitutionContext.Template, substitutionContext.Types,
                substitutionContext.ConstantArguments,
                out requestedInterface))
        {
            Report("TRM3005",
                "The qualified interface type cannot be substituted in this generic context.",
                interfaceTypeSyntax.Span);
            return ErrorExpression(invocation.Span);
        }

        var receiverFormalScope =
            BoundInterfaceTypeParameterScope.Interface;
        var receiverFormalOrdinal = -1;
        var isConstraintCall = _genericProofFunctions is not null
            && TryGetGenericInterfaceFormal(receiverType,
                out receiverFormalScope, out receiverFormalOrdinal)
            && receiverFormalScope
                == BoundInterfaceTypeParameterScope.Member;
        if (isConstraintCall)
        {
            if (_genericContext is not GenericInstantiation genericCaller)
            {
                Report("TRM3005",
                    "A generic interface call has no owning generic proof context.",
                    invocation.Span);
                return ErrorExpression(invocation.Span);
            }
            var receiverFormal = genericCaller.Template.FormalOrder
                .FirstOrDefault(slot => slot.Ordinal == receiverFormalOrdinal
                    && slot.Kind == BoundGenericMethodFormalKind.Type);
            if (receiverFormal is null
                || !genericCaller.Template.TypeParameters[
                    receiverFormal.TypeFormalOrdinal]
                    .InterfaceConstraints.Any(constraint =>
                        BoundInterfaceContract.IsInInheritanceClosure(
                            constraint, requestedInterface,
                            GenericMethodInterfaceDefinitions)))
            {
                Report("TRM3005",
                    "The receiver's declared interface constraints do not provide the selected witness.",
                    invocation.Span);
                return ErrorExpression(invocation.Span);
            }
        }
        else if (!TryGetNominalInterfaceImplementingType(receiverType,
                     out var implementingType)
            || !_interfaceWitnesses.Any(witness =>
                witness.ImplementingType == implementingType
                && BoundInterfaceContract.IsInInheritanceClosure(
                    witness.InterfaceType, requestedInterface,
                    [.. _interfaces])))
        {
            Report("TRM3005",
                "The receiver has no declared witness for the selected interface.",
                invocation.Span);
            return ErrorExpression(invocation.Span);
        }
        else
        {
            receiverType = implementingType;
        }

        var callArguments = arguments.Children.OfType<SyntaxNode>().ToArray();
        if (!BoundInterfaceContract.TryGetRequirementIdentities(
                requestedInterface, GenericMethodInterfaceDefinitions,
                static _ => true, out var requirements,
                allowOpenFormals: isConstraintCall,
                genericNominalTypeDefinitions:
                    [.. _genericNominalTypeDefinitions],
                sourceGenericTypeDefinitions:
                    [.. _sourceGenericTypeDefinitions]))
        {
            Report("TRM3005",
                "The selected interface does not have a supported finite requirement closure.",
                interfaceTypeSyntax.Span);
            return ErrorExpression(invocation.Span);
        }

        var candidates = new List<(BoundInterfaceMemberIdentity Identity,
            BoundInterfaceMemberDefinition Member)>();
        foreach (var requirement in requirements)
        {
            if (!BoundInterfaceContract.TryGetInterfaceMemberDefinition(
                    requirement, GenericMethodInterfaceDefinitions,
                    static _ => true, out var member,
                    allowOpenFormals: isConstraintCall,
                    genericNominalTypeDefinitions:
                        [.. _genericNominalTypeDefinitions],
                    sourceGenericTypeDefinitions:
                        [.. _sourceGenericTypeDefinitions])
                || member is null || member.Name != memberName)
            {
                continue;
            }
            if (invocation.Kind == SyntaxKind.InvocationExpression
                && member.Kind == BoundInterfaceMemberKind.Callable
                && callArguments.Length <= member.Parameters.Length)
            {
                candidates.Add((requirement, member));
            }
            else if (invocation.Kind == SyntaxKind.MemberAccessExpression
                && member.Kind
                    == BoundInterfaceMemberKind.Property
                && callArguments.Length == 0
                && accessorKind == BoundInterfaceAccessorKind.Get
                && requirement.AccessorOrdinal >= 0
                && (uint)requirement.AccessorOrdinal
                    < (uint)member.Accessors.Length
                && member.Accessors[requirement.AccessorOrdinal].Kind
                    == BoundInterfaceAccessorKind.Get)
            {
                candidates.Add((requirement, member));
            }
            else if (invocation.Kind == SyntaxKind.IndexExpression
                && member.Kind == BoundInterfaceMemberKind.Indexer
                && InterfaceIndexerArgumentsAreApplicable(member,
                    accessorKind, callArguments)
                && requirement.AccessorOrdinal >= 0
                && (uint)requirement.AccessorOrdinal
                    < (uint)member.Accessors.Length
                && member.Accessors[requirement.AccessorOrdinal].Kind
                    == accessorKind)
            {
                candidates.Add((requirement, member));
            }
        }
        if (candidates.Count != 1)
        {
            Report("TRM3011", candidates.Count == 0
                ? $"Interface '{requestedInterface.NominalName}' has no supported member '{memberName}' for this call."
                : $"Interface member '{memberName}' is ambiguous.",
                invocation.Span);
            return ErrorExpression(invocation.Span);
        }

        var (selectedRequirement, selectedMember) = candidates[0];
        var requiresMutableReceiver
            = selectedMember.Kind == BoundInterfaceMemberKind.Callable
                ? selectedMember.RequiresMutableReceiver
                : selectedMember.Kind is (BoundInterfaceMemberKind.Property
                    or BoundInterfaceMemberKind.Indexer)
                    && selectedMember.Accessors[
                        selectedRequirement.AccessorOrdinal].IsMutable;
        if (isConstraintCall && requiresMutableReceiver)
        {
            var receiverFormalSlot
                = _genericContext is GenericInstantiation genericCaller
                    ? genericCaller.Template.FormalOrder.FirstOrDefault(
                        slot => slot.Ordinal == receiverFormalOrdinal
                            && slot.Kind
                                == BoundGenericMethodFormalKind.Type)
                    : null;
            if (_genericContext is not GenericInstantiation
                    writableGenericCaller
                || receiverFormalSlot is null
                || (uint)receiverFormalSlot.TypeFormalOrdinal
                    >= (uint)writableGenericCaller.Template.TypeParameters.Length
                || !writableGenericCaller.Template.TypeParameters[
                    receiverFormalSlot.TypeFormalOrdinal].RequiresWritable)
            {
                Report("TRM3005",
                    "A generic caller needs a writable receiver-formal contract for this mutable interface requirement.",
                    invocation.Span);
                return ErrorExpression(invocation.Span);
            }
        }

        if (selectedMember.Kind is (BoundInterfaceMemberKind.Property
                or BoundInterfaceMemberKind.Indexer)
            && selectedMember.Accessors[
                selectedRequirement.AccessorOrdinal].HasBody)
        {
            Report("TRM3011",
                "Calling an interface default property or indexer accessor is not yet supported.",
                invocation.Span);
            return ErrorExpression(invocation.Span);
        }

        int targetFunction;
        BoundInterfaceType witnessInterface;
        if (isConstraintCall)
        {
            if (!TryCreateInterfaceRequirementProofStub(
                    selectedRequirement, selectedMember, receiverType,
                    invocation.Span, out targetFunction))
            {
                return ErrorExpression(invocation.Span);
            }
            witnessInterface = requestedInterface;
        }
        else
        {
            if (!TryGetNominalInterfaceImplementingType(receiverType,
                    out var implementingType)
                || _interfaceWitnesses.Where(witness =>
                    witness.ImplementingType == implementingType
                    && BoundInterfaceContract.IsInInheritanceClosure(
                        witness.InterfaceType, requestedInterface,
                        [.. _interfaces])).ToArray() is not [
                    { } selectedWitness
                ])
            {
                Report("TRM3005",
                    "The selected interface view does not identify one declared witness.",
                    invocation.Span);
                return ErrorExpression(invocation.Span);
            }
            witnessInterface = selectedWitness.InterfaceType;
            var target = selectedWitness.Members.SingleOrDefault(binding =>
                BoundInterfaceContract.AreEquivalent(
                    binding.Requirement, selectedRequirement));
            if (target is null
                || target.TargetKind is not (
                    BoundInterfaceWitnessTargetKind.Implementation
                    or BoundInterfaceWitnessTargetKind.ExplicitImplementation))
            {
                Report("TRM3011",
                    "The selected interface member has no directly callable source implementation.",
                    invocation.Span);
                return ErrorExpression(invocation.Span);
            }
            if (target.GenericMethodDefinitionOrdinal >= 0)
            {
                var targetTemplate = _templates.Values.SingleOrDefault(
                    candidate => candidate.DefinitionOrdinal
                        == target.GenericMethodDefinitionOrdinal);
                if (targetTemplate is null
                    || invocation.Children.ElementAtOrDefault(0)
                        is not SyntaxNode callee)
                {
                    Report("TRM3007",
                        "The selected generic interface implementation has no callable generic method template.",
                        invocation.Span);
                    return ErrorExpression(invocation.Span);
                }

                targetFunction = InstantiateGeneric(callee, arguments,
                    selectedTemplate: targetTemplate);
                if (targetFunction < 0)
                {
                    return ErrorExpression(invocation.Span);
                }
            }
            else
            {
                targetFunction = target.FunctionIndex;
            }
        }

        BoundCall boundCall;
        if (selectedMember.Kind is BoundInterfaceMemberKind.Callable
            or BoundInterfaceMemberKind.Property
            or BoundInterfaceMemberKind.Indexer)
        {
            if (targetFunction < 0
                || targetFunction >= _signatures.Count)
            {
                Report("TRM3011",
                    "The selected interface implementation has no bindable target.",
                    invocation.Span);
                return ErrorExpression(invocation.Span);
            }

            var originalSignature = _signatures[targetFunction];
            var accessorValueParameter
                = selectedMember.Kind
                        == BoundInterfaceMemberKind.Indexer
                    && accessorKind == BoundInterfaceAccessorKind.Set
                        ? 1 : 0;
            var expectedParameterCount
                = selectedMember.Parameters.Length
                    + accessorValueParameter;
            if (originalSignature.Parameters.Length
                != expectedParameterCount)
            {
                Report("TRM3007",
                    "The selected interface implementation changed the requirement parameter count.",
                    invocation.Span);
                return ErrorExpression(invocation.Span);
            }

            var implementationParameters
                = ImmutableArray.CreateBuilder<Parameter>(
                    expectedParameterCount);
            for (var ordinal = 0; ordinal < expectedParameterCount;
                ordinal++)
            {
                var name = ordinal < selectedMember.Parameters.Length
                    ? selectedMember.Parameters[ordinal].Name : "value";
                implementationParameters.Add(
                    originalSignature.Parameters[ordinal] with {
                        Name = name,
                        DefaultSyntax = null,
                        DefaultExpression = ordinal
                                < selectedMember.Parameters.Length
                            ? GetInterfaceParameterDefaultExpression(
                                selectedRequirement, selectedMember,
                                ordinal,
                                originalSignature.Parameters[ordinal].Type)
                            : null,
                        DefaultValue = ordinal
                                < selectedMember.Parameters.Length
                            ? selectedMember.Parameters[ordinal]
                                .DefaultValue
                            : null,
                    });
            }
            var implementationView = originalSignature with {
                Parameters = implementationParameters.MoveToImmutable(),
            };
            var diagnosticsBeforeBinding = DiagnosticCount;
            _signatures[targetFunction] = implementationView;
            try
            {
                boundCall = selectedMember.Kind
                        == BoundInterfaceMemberKind.Indexer
                    && accessorKind == BoundInterfaceAccessorKind.Set
                    && callArguments.Length - 1
                        < selectedMember.Parameters.Length
                    ? BindInterfaceIndexerSetterWithDefaults(
                        invocation, receiverNode, targetFunction,
                        selectedRequirement, selectedMember, callArguments)
                    : BindArguments(invocation, arguments,
                        targetFunction, receiverNode,
                        allowInterfaceWitnessAccess: true);
            }
            finally
            {
                _signatures[targetFunction] = originalSignature;
            }

            if (requiresMutableReceiver && !isConstraintCall
                && DiagnosticCount == diagnosticsBeforeBinding
                && (boundCall.Arguments.IsEmpty
                    || !HasWritableInterfaceCallReceiver(
                        boundCall.Arguments[0], receiverType)))
            {
                Report("TRM3005",
                    "A mutable interface requirement needs writable receiver access.",
                    receiverNode.Span);
                return ErrorExpression(invocation.Span);
            }
        }
        else
        {
            boundCall = BindArguments(invocation, arguments, targetFunction,
                receiverNode, allowInterfaceWitnessAccess: true);
        }
        if (!_isValidatingGenericTemplates
            && !boundCall.Arguments.Any(static argument =>
                argument is BoundPendingInterfaceDefault))
        {
            boundCall = TrackInterfaceDefaultArguments(boundCall,
                selectedMember);
        }
        return boundCall with {
            InterfaceCallIdentity = new BoundInterfaceCallIdentity(
                receiverType, witnessInterface, requestedInterface,
                selectedRequirement, isConstraintCall),
        };
    }

    private BoundCall TrackInterfaceDefaultArguments(
        BoundCall call, BoundInterfaceMemberDefinition member)
    {
        if (!member.Parameters.Any(static parameter =>
                parameter.DefaultExpression is not null))
        {
            return call;
        }

        var defaults = call.DefaultedArguments.IsDefault
            ? ImmutableArray.CreateBuilder<BoundCallDefaultArgument>()
            : call.DefaultedArguments.ToBuilder();
        var existingArguments = new Dictionary<int, int>();
        for (var index = 0; index < defaults.Count; index++)
        {
            existingArguments.TryAdd(defaults[index].ArgumentIndex, index);
        }
        var receiverOffset = (uint)call.Function < (uint)_signatures.Count
            && !_signatures[call.Function].IsStatic ? 1 : 0;
        foreach (var (parameter, index) in member.Parameters
            .Select(static (parameter, index) => (parameter, index)))
        {
            if (parameter.DefaultExpression
                is not BoundExpression expression)
            {
                continue;
            }

            var argumentIndex = call.ArgumentIndex(index + receiverOffset);
            if ((uint)argumentIndex >= (uint)call.Arguments.Length
                || !ReferenceEquals(call.Arguments[argumentIndex],
                    expression))
            {
                continue;
            }

            if (existingArguments.TryGetValue(argumentIndex,
                    out var defaultedIndex))
            {
                var defaulted = defaults[defaultedIndex];
                if (defaulted.SourceInputOrdinal
                    != member.SourceInputOrdinal)
                {
                    defaults[defaultedIndex] = defaulted with {
                        SourceInputOrdinal = member.SourceInputOrdinal,
                    };
                }
                continue;
            }

            defaults.Add(new BoundCallDefaultArgument(argumentIndex, index,
                expression, parameter.DefaultValue,
                member.SourceInputOrdinal));
            existingArguments.Add(argumentIndex, defaults.Count - 1);
        }

        return defaults.Count == 0 ? call : call with {
            DefaultedArguments = defaults.ToImmutable(),
        };
    }

    private BoundExpression? GetInterfaceParameterDefaultExpression(
        BoundInterfaceMemberIdentity requirement,
        BoundInterfaceMemberDefinition member, int parameterOrdinal,
        PrimitiveType parameterType)
    {
        var parameter = member.Parameters[parameterOrdinal];
        if (parameter.HasPendingDefault)
        {
            return new BoundPendingInterfaceDefault(requirement,
                parameterOrdinal, parameterType,
                parameter.DefaultSyntaxSpan);
        }

        var expression = parameter.DefaultExpression;
        if (expression is null)
        {
            return null;
        }

        return _isValidatingGenericTemplates
            ? new BoundPendingInterfaceDefault(requirement,
                parameterOrdinal, parameterType, expression.Span)
            : expression;
    }

    private static bool InterfaceIndexerArgumentsAreApplicable(
        BoundInterfaceMemberDefinition member,
        BoundInterfaceAccessorKind accessorKind,
        SyntaxNode[] callArguments)
    {
        var indexParameterCount = member.Parameters.Length;
        var suppliedIndexCount = accessorKind
                == BoundInterfaceAccessorKind.Set
            ? callArguments.Length - 1 : callArguments.Length;
        if (suppliedIndexCount < 1
            || suppliedIndexCount > indexParameterCount
            || (accessorKind
                    is not (BoundInterfaceAccessorKind.Get
                        or BoundInterfaceAccessorKind.Set)))
        {
            return false;
        }

        for (var index = suppliedIndexCount;
            index < indexParameterCount; index++)
        {
            if (member.Parameters[index].DefaultValue is null
                && member.Parameters[index].DefaultExpression is null
                && !member.Parameters[index].HasPendingDefault)
            {
                return false;
            }
        }
        return true;
    }

    private BoundCall BindInterfaceIndexerSetterWithDefaults(
        SyntaxNode invocation, SyntaxNode receiverNode, int targetFunction,
        BoundInterfaceMemberIdentity requirement,
        BoundInterfaceMemberDefinition member, SyntaxNode[] callArguments)
    {
        var indexParameterCount = member.Parameters.Length;
        var suppliedIndexCount = callArguments.Length - 1;
        var implementationView = _signatures[targetFunction];
        var indexSignature = implementationView with {
            Parameters = implementationView.Parameters.Slice(0,
                suppliedIndexCount),
        };
        _signatures[targetFunction] = indexSignature;
        BoundCall call;
        try
        {
            call = BindArguments(invocation,
                CreateIndexCallArgumentList(invocation), targetFunction,
                receiverNode, allowInterfaceWitnessAccess: true);
        }
        finally
        {
            _signatures[targetFunction] = implementationView;
        }

        var arguments = call.Arguments.ToBuilder();
        var parameterIndices = call.ArgumentParameterIndices.IsDefault
            ? null : call.ArgumentParameterIndices.ToBuilder();
        var implicitReferenceTemporaries
            = call.ArgumentImplicitReferenceTemporaries.IsDefault
                ? null
                : call.ArgumentImplicitReferenceTemporaries.ToBuilder();
        for (var index = suppliedIndexCount;
            index < indexParameterCount; index++)
        {
            var parameterType
                = implementationView.Parameters[index].Type;
            BoundExpression? value
                = member.Parameters[index].DefaultValue;
            if (value is null
                && member.Parameters[index].HasPendingDefault)
            {
                value = new BoundPendingInterfaceDefault(requirement,
                    index, parameterType,
                    member.Parameters[index].DefaultSyntaxSpan);
            }
            else if (value is null
                && member.Parameters[index].DefaultExpression
                    is BoundExpression expression)
            {
                value = _isValidatingGenericTemplates
                    ? new BoundPendingInterfaceDefault(requirement,
                        index, parameterType, expression.Span)
                    : expression;
            }
            if (value is BoundExpression defaultValue
                && defaultValue.Type == parameterType)
            {
                arguments.Add(defaultValue is BoundConstant constant
                    ? constant with { Span = invocation.Span }
                    : defaultValue);
            }
            else
            {
                Report("TRM3011",
                    $"Required interface indexer parameter '{member.Parameters[index].Name}' was not supplied.",
                    invocation.Span);
                arguments.Add(ErrorExpression(invocation.Span));
            }
            parameterIndices?.Add(index + 1);
            implicitReferenceTemporaries?.Add(-1);
        }

        var valueNode = callArguments[^1].Children
            .OfType<SyntaxNode>().SingleOrDefault();
        var valueType = implementationView.Parameters[
            indexParameterCount].Type;
        if (valueNode is null)
        {
            Report("TRM3011",
                "An indexer setter requires an assigned value.",
                invocation.Span);
        }
        arguments.Add(valueNode is null
            ? ErrorExpression(invocation.Span)
            : BindExpression(valueNode, valueType));
        parameterIndices?.Add(indexParameterCount + 1);
        implicitReferenceTemporaries?.Add(-1);

        return call with {
            Arguments = arguments.ToImmutable(),
            ArgumentParameterIndices = parameterIndices?.ToImmutable()
                ?? call.ArgumentParameterIndices,
            ArgumentImplicitReferenceTemporaries =
                implicitReferenceTemporaries?.ToImmutable() ?? default,
        };
    }

    private bool HasWritableInterfaceCallReceiver(
        BoundExpression receiver, PrimitiveType implementingType)
    {
        for (var depth = 0;
             receiver is BoundReadonlyProjection projection;
             depth++)
        {
            if (depth >= SemanticVerifier.MaximumDepth)
            {
                return false;
            }
            receiver = projection.Value;
        }
        if (ManagedClassTypes.IsClass(implementingType))
        {
            if (!ManagedClassTypes.IsClass(receiver.Type)
                || ManagedClassTypes.IsOptional(receiver.Type)
                || ManagedClassTypes.Readonly(receiver.Type)
                    != implementingType)
            {
                return false;
            }
            if (ManagedClassTypes.IsWritable(receiver.Type))
            {
                return true;
            }
            if (receiver is BoundRead read
                && (uint)read.Variable < (uint)_variables.Count)
            {
                var sourceType = _variables[read.Variable].Type;
                return ManagedClassTypes.IsClass(sourceType)
                    && !ManagedClassTypes.IsOptional(sourceType)
                    && ManagedClassTypes.IsWritable(sourceType)
                    && ManagedClassTypes.Readonly(sourceType)
                        == implementingType;
            }
            return receiver is BoundFieldRead field
                && ManagedClassTypes.IsWritable(field.Type);
        }

        if (receiver.Type != implementingType)
        {
            return false;
        }
        return receiver switch {
            BoundMutableReceiverLoan mutable =>
                IsMutableReceiverVariable(mutable.Variable,
                    mutable.Type, implementingType),
            BoundReceiverLoan loan =>
                IsMutableReceiverVariable(loan.Variable, loan.Type,
                    implementingType),
            BoundRead read =>
                IsMutableReceiverVariable(read.Variable, read.Type,
                    implementingType),
            BoundMutableFieldLoan field =>
                IsWritableInterfaceStructFieldPlace(field.Place,
                    implementingType),
            BoundFieldLoan field =>
                IsWritableInterfaceStructFieldPlace(field.Place,
                    implementingType),
            BoundFieldRead field =>
                IsWritableInterfaceStructFieldPlace(field,
                    implementingType),
            _ => false,
        };
    }

    private bool IsMutableReceiverVariable(int variable,
        PrimitiveType receiverType, PrimitiveType implementingType) =>
        receiverType == implementingType
            && (uint)variable < (uint)_variables.Count
            && _variables[variable].Type == implementingType
            && (_variables[variable].IsMutable
                || HasWritableGenericMethodTypeFormal(receiverType));

    private bool IsWritableInterfaceStructFieldPlace(
        BoundFieldRead place, PrimitiveType implementingType)
    {
        if (place is null || place.Type != implementingType)
        {
            return false;
        }
        BoundExpression current = place;
        for (var depth = 0; depth < SemanticVerifier.MaximumDepth; depth++)
        {
            if (current is not BoundFieldRead field)
            {
                return current is BoundRead root
                    && (uint)root.Variable < (uint)_variables.Count
                    && root.Type == _variables[root.Variable].Type
                    && _variables[root.Variable].IsMutable;
            }
            if (field.Receiver is null || !IsStruct(field.Receiver.Type))
            {
                return false;
            }
            var owner = (int)field.Receiver.Type - 100;
            if ((uint)owner >= (uint)_structs.Count
                || (uint)field.Field
                    >= (uint)_structs[owner].Fields.Length
                || !_structs[owner].Fields[field.Field].IsMutable
                || _structs[owner].Fields[field.Field].Type != field.Type)
            {
                return false;
            }
            current = field.Receiver;
        }
        return false;
    }

    private bool TryGetNominalInterfaceImplementingType(
        PrimitiveType receiverType, out PrimitiveType implementingType)
    {
        implementingType = receiverType;
        if (ManagedClassTypes.IsClass(receiverType))
        {
            if (ManagedClassTypes.IsOptional(receiverType))
            {
                return false;
            }
            implementingType = ManagedClassTypes.Readonly(receiverType);
        }
        return InterfaceImplementingTypeName(implementingType) is not null;
    }

    private bool TryCreateInterfaceRequirementProofStub(
        BoundInterfaceMemberIdentity requirement,
        BoundInterfaceMemberDefinition member,
        PrimitiveType receiverType, TextSpan span,
        out int functionIndex)
    {
        functionIndex = -1;
        var isCallable = member.Kind == BoundInterfaceMemberKind.Callable;
        var accessor = member.Kind is (BoundInterfaceMemberKind.Property
                        or BoundInterfaceMemberKind.Indexer)
            && (uint)requirement.AccessorOrdinal
                < (uint)member.Accessors.Length
                ? member.Accessors[requirement.AccessorOrdinal] : null;
        var isGetter = accessor?.Kind == BoundInterfaceAccessorKind.Get;
        var isIndexerSetter = member.Kind
                    == BoundInterfaceMemberKind.Indexer
                && accessor?.Kind == BoundInterfaceAccessorKind.Set;
        if (_genericProofFunctions is null
            || _genericContext is null
                    || (!isCallable && !isGetter && !isIndexerSetter)
            || member.IsStatic
            || member.IsAsync
            || member.NativeCallableConvention is not null
            || !member.TypeParameters.IsEmpty
            || !member.Constraints.IsEmpty
            || !member.Attributes.IsEmpty
            || !member.ContractTokens.IsEmpty
            || !member.Dependencies.IsEmpty
            || member.Parameters.Any(static parameter =>
                parameter.Mode != BoundInterfaceParameterMode.Value
                || !parameter.Attributes.IsEmpty
                || !parameter.AttributeTokens.IsEmpty)
            || (accessor is not null
                && (member.DoesNotReturn
                    || (member.Kind == BoundInterfaceMemberKind.Property
                        && !member.Parameters.IsEmpty)
                    || accessor.HasBody
                    || !accessor.Attributes.IsEmpty
                    || !accessor.AttributeTokens.IsEmpty
                    || !accessor.ContractTokens.IsEmpty
                    || !accessor.Dependencies.IsEmpty))
            || member.ResultType is not BoundInterfaceType resultType
            || !TryResolveInterfaceValueType(resultType,
                requirement.DeclaringInterface.TypeArguments,
                span, out var resolvedResult))
        {
            Report("TRM3005",
                "The selected interface requirement is outside the generic proof call subset.",
                span);
            return false;
        }

        var parameterCount = member.Parameters.Length
            + (isIndexerSetter ? 1 : 0);
        var parameters = ImmutableArray.CreateBuilder<Parameter>(
            parameterCount);
        var variables = ImmutableArray.CreateBuilder<BoundVariable>(
            parameterCount + 1);
        var requiresMutableReceiver = accessor?.IsMutable
            ?? member.RequiresMutableReceiver;
        var requiresUnsafeCaller = accessor?.RequiresUnsafeCaller
            ?? member.RequiresUnsafeCaller;
        var doesNotReturn = accessor?.DoesNotReturn
            ?? member.DoesNotReturn;
        variables.Add(new BoundVariable("this", receiverType,
            requiresMutableReceiver, span));
        foreach (var parameter in member.Parameters)
        {
            if (!TryResolveInterfaceValueType(parameter.Type,
                    requirement.DeclaringInterface.TypeArguments, span,
                    out var resolvedParameter))
            {
                return false;
            }
            parameters.Add(new Parameter(parameter.Name, resolvedParameter,
                span, false) {
                DefaultValue = parameter.DefaultValue,
            });
            variables.Add(new BoundVariable(parameter.Name,
                resolvedParameter, false, span));
        }

        if (isIndexerSetter)
        {
            parameters.Add(new Parameter("value", resolvedResult,
                span, false));
            variables.Add(new BoundVariable("value", resolvedResult,
                false, span));
        }

        var returnType = isIndexerSetter
            ? PrimitiveType.Void : resolvedResult;
        var name = "$interface-requirement."
            + _genericInterfaceRequirementStubOrdinal++;
        functionIndex = _signatures.Count;
        var stubFunction = new BoundFunction(name, returnType,
            variables.ToImmutable(), parameterCount + 1,
            new BoundBlock([], span), span) {
            IsExternal = true,
            IsPrivate = true,
            IsStatic = false,
            DeclaringStruct = null,
            RequiresUnsafeCaller = requiresUnsafeCaller,
            DoesNotReturn = doesNotReturn,
            InterfaceRequirement = requirement,
        };
        var signature = new Signature(name, returnType,
            parameters.ToImmutable(), null, null, _tree, null,
            false, true, false, requiresMutableReceiver, false, false,
            doesNotReturn, stubFunction) {
            RequiresUnsafeCaller = requiresUnsafeCaller,
            DependencyContracts = [],
        };
        if (!_functions.TryAdd(name, functionIndex))
        {
            return false;
        }
        _signatures.Add(signature);
        _genericProofFunctions.Add(stubFunction);
        return true;
    }

    private string? InterfaceImplementingTypeName(PrimitiveType type)
    {
        if (ManagedClassTypes.IsClass(type))
        {
            var index = ManagedClassTypes.ClassIndex(type);
            return (uint)index < (uint)_classes.Count
                ? _classes[index].Name : null;
        }
        if (IsStruct(type))
        {
            var index = (int)type - 100;
            return (uint)index < (uint)_structs.Count
                ? _structs[index].Name : null;
        }
        return null;
    }

    private bool HasAcyclicInterfaceBases()
        => BoundInterfaceContract.HasAcyclicBases([.. _interfaces]);

    private BoundForeignNativeInterfaceLayout?
        BindForeignNativeInterfaceLayout(
            List<InterfaceDeclarationPart> parts,
            InterfaceDeclarationPart first,
            ImmutableArray<BoundInterfaceFormal> formals,
            int baseCount,
            int constraintCount,
            ImmutableArray<BoundInterfaceMemberDefinition> members)
    {
        var attributes = new List<(InterfaceDeclarationPart Part,
            SyntaxNode Attribute)>();
        foreach (var part in parts)
        {
            SetContext(part.Tree, part.Declaration);
            foreach (var attribute in part.Declaration.Children
                         .OfType<SyntaxNode>()
                         .Where(static child => child.Kind
                             == SyntaxKind.Attribute))
            {
                if (IsForeignNativeInterfaceAttribute(attribute))
                {
                    attributes.Add((part, attribute));
                }
            }
        }

        if (attributes.Count == 0)
        {
            return null;
        }

        var (attributePart, attributeNode) = attributes[0];
        SetContext(attributePart.Tree, attributePart.Declaration);
        if (_nativeCoreRegistration?.ForeignNativeInterface is not
            CoreNativeAttributeSchema registration)
        {
            Report("TRM3030",
                "ForeignNativeInterface requires an explicitly selected Core schema.",
                attributeNode.Span);
            return null;
        }

        if (attributes.Count != 1
            || !ReferenceEquals(attributePart, first)
            || parts.Count != 1 || first.IsPartial
            || !formals.IsEmpty || baseCount != 0 || constraintCount != 0
            || members.IsDefaultOrEmpty
            || members.Any(static member =>
                member.Kind != BoundInterfaceMemberKind.Callable
                || member.Accessibility != BoundInterfaceAccessibility.Public
                || member.IsStatic || member.IsExternal || member.IsPartial
                || !member.DeclaredRequiresUnsafeCaller
                || !member.RequiresUnsafeCaller
                || member.NativeCallableConvention is null
                || member.IsAsync
                || member.HasBody || !member.TypeParameters.IsEmpty
                || !member.Constraints.IsEmpty || !member.Accessors.IsEmpty))
        {
            Report("TRM3030",
                "ForeignNativeInterface requires one closed, nonpartial interface with public unsafe bodyless NativeCallable instance methods and no bases or constraints.",
                attributeNode.Span);
            return null;
        }

        var selectedAttributeName = AttributeName(attributeNode);
        if (!TryGetRegisteredNativeAttributeSchema(selectedAttributeName,
                out var selectedSchema)
            || selectedSchema.Identity != registration.Identity
            || !TryGetSingleNamedAttributeArgument(attributeNode,
                "VTableByteOffset", out var offsetExpression)
            || !TryBindLayoutUInt32(offsetExpression, out var vtableByteOffset)
            || vtableByteOffset % NativeTargetProfile.PointerSize != 0)
        {
            Report("TRM3030",
                "ForeignNativeInterface requires the selected schema and one aligned uint32 VTableByteOffset.",
                attributeNode.Span);
            return null;
        }

        return new BoundForeignNativeInterfaceLayout(vtableByteOffset);
    }

    private (NativeCallingConvention? Convention, SyntaxNode? Expression)
        BindInterfaceNativeCallableDefault(
            List<InterfaceDeclarationPart> parts,
            InterfaceDeclarationPart first)
    {
        var attributes = new List<(InterfaceDeclarationPart Part,
            SyntaxNode Attribute)>();
        foreach (var part in parts)
        {
            SetContext(part.Tree, part.Declaration);
            foreach (var attribute in part.Declaration.Children
                         .OfType<SyntaxNode>()
                         .Where(static child => child.Kind
                             == SyntaxKind.Attribute)
                         .Where(IsNativeCallableAttribute))
            {
                attributes.Add((part, attribute));
            }
        }

        if (attributes.Count == 0)
        {
            return (null, null);
        }

        var (attributePart, attributeNode) = attributes[0];
        SetContext(attributePart.Tree, attributePart.Declaration);
        if (attributes.Count != 1 || !ReferenceEquals(attributePart, first))
        {
            Report("TRM3030",
                "A NativeCallable interface default must appear once on the primary interface declaration.",
                attributeNode.Span);
            return (null, null);
        }
        if (!TryBindNativeCallableAttribute(attributeNode,
            out var convention, out var expression))
        {
            return (null, null);
        }

        // Native is only the sentinel for a convention expression awaiting ConstEval.
        return expression is null
            ? (convention, null)
            : (NativeCallingConvention.Native, expression);
    }

    private bool IsNativeCallableAttribute(SyntaxNode attribute)
    {
        var name = AttributeName(attribute);
        return (_nativeCoreRegistration?.NativeCallable is
                CoreNativeAttributeSchema selectedSchema
            && TryGetRegisteredNativeAttributeSchema(name,
                out var resolvedSchema)
            && resolvedSchema.Identity == selectedSchema.Identity)
            || IsNativeAttributeSpelling(name,
                "Terrum.Interop.NativeCallable");
    }

    private bool IsForeignNativeInterfaceAttribute(SyntaxNode attribute)
    {
        var name = AttributeName(attribute);
        return (_nativeCoreRegistration?.ForeignNativeInterface is
                CoreNativeAttributeSchema selectedSchema
            && TryGetRegisteredNativeAttributeSchema(name,
                out var resolvedSchema)
            && resolvedSchema.Identity == selectedSchema.Identity)
            || IsNativeAttributeSpelling(name,
                "Terrum.Interop.ForeignNativeInterface");
    }

    private ImmutableArray<InterfaceFormalSyntax> ParseInterfaceFormals(SyntaxNode? parameters)
    {
        if (parameters is null)
        {
            return [];
        }

        var groups = new List<List<SyntaxElement>>();
        var current = new List<SyntaxElement>();
        foreach (var child in parameters.Children)
        {
            if (child is SyntaxTokenElement { Token.Kind: TokenKind.Comma })
            {
                groups.Add(current);
                current = [];
            }
            else if (child is not SyntaxTokenElement { Token.Kind: TokenKind.Less or TokenKind.Greater })
            {
                current.Add(child);
            }
        }
        if (current.Count != 0)
        {
            groups.Add(current);
        }

        var result = ImmutableArray.CreateBuilder<InterfaceFormalSyntax>(groups.Count);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var selfCount = 0;
        for (var ordinal = 0; ordinal < groups.Count; ordinal++)
        {
            var group = groups[ordinal];
            var attributes = group.OfType<SyntaxNode>()
                .Where(static node => node.Kind == SyntaxKind.Attribute).ToImmutableArray();
            foreach (var attribute in attributes.Where(IsNativeCallableAttribute))
            {
                Report("TRM3030",
                    "NativeCallable is only valid on an interface or ordinary method declaration.",
                    attribute.Span);
            }
            var directTokens = group.OfType<SyntaxTokenElement>().ToArray();
            var firstToken = directTokens.FirstOrDefault();
            var isSelf = firstToken?.Token.Text == "this";
            var isConstant = firstToken?.Token.Text == "const";
            var typeNodes = group.OfType<SyntaxNode>()
                .Where(static node => node.Kind != SyntaxKind.Attribute).ToArray();
            SyntaxToken? nameToken = null;
            SyntaxNode? constantType = null;
            if (isSelf)
            {
                nameToken = directTokens.Skip(1)
                    .Select(static token => token.Token)
                    .FirstOrDefault(static token => token.Kind == TokenKind.Identifier);
                selfCount++;
            }
            else if (isConstant)
            {
                constantType = typeNodes.FirstOrDefault();
                var typeNodeIndex = constantType is null ? -1 : group.IndexOf(constantType);
                nameToken = typeNodeIndex < 0
                    ? null
                    : group.Skip(typeNodeIndex + 1).OfType<SyntaxTokenElement>()
                        .Select(static token => token.Token)
                        .FirstOrDefault(static token => token.Kind == TokenKind.Identifier);
            }
            else
            {
                nameToken = directTokens
                    .Select(static token => token.Token)
                    .FirstOrDefault(static token => token.Kind == TokenKind.Identifier);
            }
            if (nameToken is null || !seenNames.Add(nameToken.Name)
                || (isSelf && (ordinal != 0 || isConstant)))
            {
                Report("TRM3005", "Interface generic formals require distinct names and a single leading <this T>.",
                    firstToken?.Token.Span ?? parameters.Span);
                continue;
            }

            var equalsIndex = group.FindIndex(static element => element is SyntaxTokenElement {
                Token.Kind: TokenKind.Equals,
            });
            var defaultNode = equalsIndex < 0
                ? null : group.Skip(equalsIndex + 1).OfType<SyntaxNode>()
                    .FirstOrDefault(static node => node.Kind != SyntaxKind.Attribute);
            result.Add(new InterfaceFormalSyntax(
                nameToken.Name,
                isConstant ? BoundInterfaceFormalKind.Constant : BoundInterfaceFormalKind.Type,
                isSelf,
                constantType,
                isConstant ? null : defaultNode,
                isConstant ? defaultNode : null,
                attributes,
                firstToken?.Token.Span ?? parameters.Span));
        }
        if (selfCount > 1)
        {
            Report("TRM3005", "An interface can declare at most one <this T> formal.", parameters.Span);
        }
        return result.ToImmutable();
    }

    private static bool InterfaceFormalsMatch(
        ImmutableArray<InterfaceFormalSyntax> expected,
        ImmutableArray<InterfaceFormalSyntax> actual)
    {
        if (expected.Length != actual.Length)
        {
            return false;
        }
        for (var index = 0; index < expected.Length; index++)
        {
            if (expected[index].Name != actual[index].Name
                || expected[index].Kind != actual[index].Kind
                || expected[index].IsSelf != actual[index].IsSelf
                || !SyntaxArraysMatch(expected[index].Attributes, actual[index].Attributes)
                || NormalizeSyntax(expected[index].ConstantType) != NormalizeSyntax(actual[index].ConstantType)
                || NormalizeSyntax(expected[index].TypeDefault) != NormalizeSyntax(actual[index].TypeDefault)
                || NormalizeSyntax(expected[index].ConstantDefault) != NormalizeSyntax(actual[index].ConstantDefault))
            {
                return false;
            }
        }
        return true;
    }

    private static bool SyntaxArraysMatch(
        ImmutableArray<SyntaxNode> expected, ImmutableArray<SyntaxNode> actual)
    {
        if (expected.Length != actual.Length)
        {
            return false;
        }
        for (var index = 0; index < expected.Length; index++)
        {
            if (NormalizeSyntax(expected[index]) != NormalizeSyntax(actual[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static string? NormalizeSyntax(SyntaxNode? syntax) =>
        syntax is null ? null : string.Concat(syntax.DescendantTokens().Select(static token => token.Text));

    private ImmutableArray<BoundInterfaceFormal> BindInterfaceFormals(
        ImmutableArray<InterfaceFormalSyntax> formals,
        IReadOnlyDictionary<string, int> typeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces)
    {
        var result = ImmutableArray.CreateBuilder<BoundInterfaceFormal>(formals.Length);
        var priorTypeParameters = new Dictionary<string, int>(StringComparer.Ordinal);
        var priorConstantParameters = new Dictionary<string,
            InterfaceConstantFormal>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < formals.Length; ordinal++)
        {
            var formal = formals[ordinal];
            var constantType = formal.Kind == BoundInterfaceFormalKind.Constant
                && formal.ConstantType is SyntaxNode constantTypeSyntax
                    ? BindInterfaceType(constantTypeSyntax, priorTypeParameters,
                        interfaces, ordinal,
                        interfaceConstantParameters: priorConstantParameters,
                        allowGenericNominalConstantDomain: true,
                        isGenericNominalConstantDomain: true)
                    : null;
            var typeDefault = formal.Kind == BoundInterfaceFormalKind.Type
                && formal.TypeDefault is SyntaxNode typeDefaultSyntax
                    ? BindInterfaceType(typeDefaultSyntax, priorTypeParameters,
                        interfaces, ordinal,
                        interfaceConstantParameters: priorConstantParameters)
                    : null;
            BoundConstant? constantDefault = null;
            if (formal.ConstantDefault is SyntaxNode constantDefaultSyntax)
            {
                constantDefault = BindInterfaceConstantDefault(constantDefaultSyntax, constantType, formal.Span);
            }
            result.Add(new BoundInterfaceFormal(
                ordinal, formal.Name, formal.Kind, formal.IsSelf,
                [.. formal.Attributes.Select(AttributeName)],
                [.. formal.Attributes.Select(NormalizeSyntax).Where(static text => text is not null)
                    .Select(static text => text!)],
                constantType, typeDefault, constantDefault));
            if (formal.Kind == BoundInterfaceFormalKind.Type)
            {
                priorTypeParameters.Add(formal.Name, ordinal);
            }
            else if (constantType is not null)
            {
                priorConstantParameters.Add(formal.Name,
                    new InterfaceConstantFormal(constantType,
                        BoundInterfaceTypeParameterScope.Interface, ordinal));
            }
        }
        return result.ToImmutable();
    }

    private BoundConstant? BindInterfaceConstantDefault(
        SyntaxNode expression,
        BoundInterfaceType? constantType,
        TextSpan span,
        string subject = "An interface constant value")
    {
        if (constantType is not { Kind: BoundInterfaceTypeKind.Primitive } type
            || !ConstEvalScalar(type.PrimitiveType))
        {
            Report("TRM3005",
                $"{subject} requires a supported scalar type.", span);
            return null;
        }

        var diagnostics = DiagnosticCount;
        var bound = BindExpression(expression, type.PrimitiveType);
        var folded = FoldScalarConstant(bound);
        if (DiagnosticCount != diagnostics || folded is not BoundConstant constant
            || constant.Type != type.PrimitiveType)
        {
            Report("TRM3005",
                $"{subject} must be an exact compile-time scalar value.",
                expression.Span);
            return null;
        }
        return constant;
    }

    private BoundInterfaceType? BindInterfaceType(
        SyntaxNode node,
        IReadOnlyDictionary<string, int> typeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        int visibleTypeParameterCount,
        IReadOnlyDictionary<string, int>? methodTypeParameters = null,
        int visibleMethodTypeParameterCount = int.MaxValue,
        bool allowVoid = false,
        IReadOnlyDictionary<string, InterfaceConstantFormal>? interfaceConstantParameters = null,
        IReadOnlyDictionary<string, InterfaceConstantFormal>? methodConstantParameters = null,
        bool allowGenericNominalConstantDomain = false,
        bool isGenericNominalConstantDomain = false,
        IReadOnlyDictionary<string, BoundInterfaceType>?
            typeSubstitutions = null,
        IReadOnlyDictionary<string, BoundInterfaceTypeArgument>?
            constantSubstitutions = null)
    {
        if (node.Kind == SyntaxKind.ReferenceTypeName)
        {
            var arguments = node.Children.OfType<SyntaxNode>().ToArray();
            if (arguments.Length != 1)
            {
                Report("TRM3005", "A checked-reference type requires one referent type.", node.Span);
                return null;
            }
            var referent = BindInterfaceType(arguments[0], typeParameters, interfaces,
                visibleTypeParameterCount, methodTypeParameters,
                visibleMethodTypeParameterCount,
                interfaceConstantParameters: interfaceConstantParameters,
                methodConstantParameters: methodConstantParameters,
                allowGenericNominalConstantDomain:
                    allowGenericNominalConstantDomain,
                isGenericNominalConstantDomain:
                    isGenericNominalConstantDomain,
                typeSubstitutions: typeSubstitutions,
                constantSubstitutions: constantSubstitutions);
            if (referent is null)
            {
                return null;
            }
            var isWritable = node.Children[0] is SyntaxTokenElement {
                Token.Kind: TokenKind.WriteKeyword,
            };
            var isGc = node.Children.OfType<SyntaxTokenElement>()
                .Any(static token => token.Token.Kind == TokenKind.GcKeyword);
            var isInitialization = node.Children.OfType<SyntaxTokenElement>()
                .Any(static token => token.Token.Kind == TokenKind.Identifier
                    && token.Token.Text == "init");
            var isOptional = node.Children[^1] is SyntaxTokenElement {
                Token.Kind: TokenKind.Question,
            };
            if (isGc && isInitialization)
            {
                Report("TRM3005", "A GC-backed reference cannot be an initialization destination.", node.Span);
                return null;
            }
            return new BoundInterfaceType(
                BoundInterfaceTypeKind.CheckedReference, PrimitiveType.Void, null, -1,
                [BoundInterfaceTypeArgument.ForType(referent)],
                isWritable, isOptional, isGc, isInitialization);
        }

        if (node.Kind != SyntaxKind.TypeName)
        {
            var diagnosticCount = DiagnosticCount;
            var primitive = BindType(node, allowVoid: false);
            return DiagnosticCount == diagnosticCount
                ? ConcreteInterfaceType(primitive) : null;
        }

        var name = TypeNameIdentifier(node);
        var typeArguments = node.Children.OfType<SyntaxNode>().ToArray();
        var isWritableType = node.Children[0] is SyntaxTokenElement {
            Token.Kind: TokenKind.WriteKeyword,
        };
        var isOptionalType = node.Children[^1] is SyntaxTokenElement {
            Token.Kind: TokenKind.Question,
        };
        var isGcType = node.Children.OfType<SyntaxTokenElement>()
            .Any(static token => token.Token.Kind == TokenKind.GcKeyword);
        if (name == "void" && typeArguments.Length == 0 && allowVoid)
        {
            return ConcreteInterfaceType(PrimitiveType.Void);
        }
        if (typeSubstitutions is not null
            && typeSubstitutions.TryGetValue(name,
                out var substitutedFormal))
        {
            if (typeArguments.Length != 0)
            {
                Report("TRM3005",
                    $"Type argument substitution '{name}' cannot take type arguments.",
                    node.Span);
                return null;
            }

            return ApplyInterfaceTypeQualifiers(substitutedFormal,
                isWritableType, isOptionalType, isGcType);
        }
        if (typeParameters.TryGetValue(name, out var parameterOrdinal))
        {
            if (typeArguments.Length != 0 || parameterOrdinal >= visibleTypeParameterCount)
            {
                Report("TRM3005", $"Type formal '{name}' is not valid at this position.", node.Span);
                return null;
            }
            return new BoundInterfaceType(
                BoundInterfaceTypeKind.TypeParameter, PrimitiveType.Void, null, parameterOrdinal,
                [], isWritableType, isOptionalType, isGcType, false)
                { TypeParameterScope = BoundInterfaceTypeParameterScope.Interface };
        }
        if (methodTypeParameters is not null
            && methodTypeParameters.TryGetValue(name, out parameterOrdinal))
        {
            if (typeArguments.Length != 0 || parameterOrdinal >= visibleMethodTypeParameterCount)
            {
                Report("TRM3005", $"Method type formal '{name}' is not valid at this position.", node.Span);
                return null;
            }
            return new BoundInterfaceType(
                BoundInterfaceTypeKind.TypeParameter, PrimitiveType.Void, null, parameterOrdinal,
                [], isWritableType, isOptionalType, isGcType, false)
                { TypeParameterScope = BoundInterfaceTypeParameterScope.Member };
        }

        if (FindAliasDefinition(name) is AliasDefinition alias)
        {
            return BindInterfaceAlias(alias, node, typeArguments,
                typeParameters, interfaces, visibleTypeParameterCount,
                methodTypeParameters, visibleMethodTypeParameterCount,
                allowVoid, interfaceConstantParameters,
                methodConstantParameters,
                allowGenericNominalConstantDomain,
                isGenericNominalConstantDomain, isWritableType,
                isOptionalType, isGcType, typeSubstitutions,
                constantSubstitutions);
        }

        if (ResolveInterfaceName(name, typeArguments.Length, interfaces, node.Span,
            out var resolvedInterface) is string interfaceName
            && resolvedInterface is not null)
        {
            var arguments = ImmutableArray.CreateBuilder<BoundInterfaceTypeArgument>(
                resolvedInterface.Formals.Length);
            for (var ordinal = 0; ordinal < resolvedInterface.Formals.Length; ordinal++)
            {
                var formal = resolvedInterface.Formals[ordinal];
                if (ordinal >= typeArguments.Length)
                {
                    if (formal.TypeDefault is BoundInterfaceType typeDefault)
                    {
                        var substitutedDefault
                            = BoundInterfaceContract.SubstituteTypeParameters(
                                typeDefault, arguments.ToImmutable());
                        if (formal.RequiresWritable
                            && !substitutedDefault.IsWritable)
                        {
                            Report("TRM3005",
                                $"The default for interface formal '{formal.Name}' does not satisfy its write requirement.",
                                node.Span);
                            return null;
                        }
                        arguments.Add(BoundInterfaceTypeArgument.ForType(
                            substitutedDefault));
                        continue;
                    }
                    if (formal.Kind == BoundInterfaceFormalKind.Constant
                        && formal.ConstantType is BoundInterfaceType constantDefaultType
                        && formal.ConstantDefault is BoundConstant constantDefault)
                    {
                        var substitutedType =
                            BoundInterfaceContract.SubstituteTypeParameters(
                                constantDefaultType, arguments.ToImmutable());
                        if (substitutedType.Kind != BoundInterfaceTypeKind.Primitive
                            || substitutedType.PrimitiveType != constantDefault.Type)
                        {
                            Report("TRM3005",
                                "An interface constant default must retain its complete value domain.",
                                node.Span);
                            return null;
                        }
                        arguments.Add(BoundInterfaceTypeArgument.ForConstant(
                            substitutedType, constantDefault));
                        continue;
                    }
                    Report("TRM3005",
                        $"Interface type '{name}' omits a generic argument without a default.",
                        node.Span);
                    return null;
                }

                var argument = typeArguments[ordinal];
                if (formal.Kind == BoundInterfaceFormalKind.Type)
                {
                    if (argument.Kind
                        == SyntaxKind.ExistentialTypeArgument)
                    {
                        if (formal.RequiresWritable)
                        {
                            Report("TRM3005",
                                $"An existential type cannot satisfy the write requirement for interface formal '{formal.Name}'.",
                                argument.Span);
                            return null;
                        }
                        arguments.Add(
                            BoundInterfaceTypeArgument.ForExistentialType());
                        continue;
                    }
                    if (argument.Kind == SyntaxKind.ConstantGenericArgument)
                    {
                        Report("TRM3005",
                            "A constant generic argument cannot satisfy a type formal.",
                            argument.Span);
                        return null;
                    }
                    if (BindInterfaceType(argument, typeParameters, interfaces,
                        visibleTypeParameterCount, methodTypeParameters,
                        visibleMethodTypeParameterCount,
                        interfaceConstantParameters: interfaceConstantParameters,
                        methodConstantParameters: methodConstantParameters,
                        allowGenericNominalConstantDomain:
                            allowGenericNominalConstantDomain,
                        typeSubstitutions: typeSubstitutions,
                        constantSubstitutions: constantSubstitutions)
                        is not BoundInterfaceType boundTypeArgument)
                    {
                        return null;
                    }
                    if (formal.RequiresWritable
                        && !boundTypeArgument.IsWritable)
                    {
                        Report("TRM3005",
                            $"The type argument for interface formal '{formal.Name}' does not satisfy its write requirement.",
                            argument.Span);
                        return null;
                    }
                    arguments.Add(BoundInterfaceTypeArgument.ForType(boundTypeArgument));
                }
                else
                {
                    if (argument.Kind
                        == SyntaxKind.ExistentialTypeArgument)
                    {
                        Report("TRM3005",
                            "An existential type argument cannot satisfy a constant formal.",
                            argument.Span);
                        return null;
                    }
                    if (formal.ConstantType is not BoundInterfaceType constantType)
                    {
                        return null;
                    }
                    var substitutedConstantType =
                        BoundInterfaceContract.SubstituteTypeParameters(
                            constantType, arguments.ToImmutable());
                    if (BindInterfaceConstantTypeArgument(argument,
                            substitutedConstantType,
                            interfaceConstantParameters,
                            methodConstantParameters,
                            constantSubstitutions)
                            is not BoundInterfaceTypeArgument boundConstantArgument)
                    {
                        return null;
                    }
                    arguments.Add(boundConstantArgument);
                }
            }
            return new BoundInterfaceType(
                BoundInterfaceTypeKind.Nominal, PrimitiveType.Void, interfaceName, -1,
                arguments.MoveToImmutable(), isWritableType, isOptionalType, isGcType, false)
                { ModuleImport = resolvedInterface.ModuleImport };
        }

        if (allowGenericNominalConstantDomain
            && TryFindGenericStructTemplateForTypeTerm(name,
                out var sourceGenericTemplate))
        {
            if ((isGenericNominalConstantDomain
                    && (isWritableType || isOptionalType || isGcType))
                || (uint)sourceGenericTemplate.DefinitionIndex
                    >= (uint)_sourceGenericTypeDefinitions.Count)
            {
                Report("TRM3005",
                    $"Generic source type '{name}' is not an admitted transparent constant domain.",
                    node.Span);
                return null;
            }

            var sourceDefinition = _sourceGenericTypeDefinitions[
                sourceGenericTemplate.DefinitionIndex];
            if (sourceDefinition.DefinitionIndex
                    != sourceGenericTemplate.DefinitionIndex
                || sourceDefinition.Formals.IsDefaultOrEmpty
                || sourceDefinition.FormalOrder.IsDefault
                || sourceDefinition.Formals.Length
                    < typeArguments.Length
                || sourceDefinition.FormalOrder.Length
                    != sourceDefinition.Formals.Length
                || sourceGenericTemplate.FormalSyntax.Length
                    != sourceDefinition.Formals.Length
                || (isGenericNominalConstantDomain
                    && sourceDefinition.TransparentConstantCarrierType
                        is null))
            {
                Report("TRM3005",
                    $"Generic source type '{name}' is not an admitted transparent constant domain.",
                    node.Span);
                return null;
            }

            var genericArguments = ImmutableArray.CreateBuilder<
                BoundInterfaceTypeArgument>(
                    sourceDefinition.Formals.Length);
            var sourceTypeSubstitutions =
                CopyInterfaceSubstitutions(typeSubstitutions);
            var sourceConstantSubstitutions =
                CopyInterfaceSubstitutions(constantSubstitutions);
            foreach (var formal in sourceDefinition.Formals)
            {
                if ((uint)formal.Ordinal
                        >= (uint)sourceDefinition.Formals.Length
                    || sourceDefinition.FormalOrder[formal.Ordinal]
                        is not BoundGenericMethodFormalSlot slot
                    || slot.Ordinal != formal.Ordinal
                    || (formal.Kind == BoundGenericMethodFormalKind.Type
                        && (slot.TypeFormalOrdinal
                                < 0
                            || slot.ConstantFormalOrdinal != -1
                            || formal.OwnershipDomain != 1
                            || formal.NullabilityDomain != 1
                            || formal.RequiresBlittable))
                    || (formal.Kind
                            == BoundGenericMethodFormalKind.Constant
                        && (slot.ConstantFormalOrdinal < 0
                            || slot.TypeFormalOrdinal != -1
                            || formal.ConstantDomain is null)))
                {
                    Report("TRM3005",
                        $"Generic source type '{name}' has an unsupported formal contract for an interface type.",
                        node.Span);
                    return null;
                }

                var argument = formal.Ordinal < typeArguments.Length
                    ? typeArguments[formal.Ordinal] : null;
                var formalSyntax =
                    sourceGenericTemplate.FormalSyntax[formal.Ordinal];
                if (formal.Kind == BoundGenericMethodFormalKind.Type)
                {
                    if (argument is null
                        || argument.Kind
                            == SyntaxKind.GenericDefaultArgument)
                    {
                        argument = formalSyntax.TypeDefault;
                    }
                    if (argument is null)
                    {
                        Report("TRM3005",
                            $"Generic source type '{name}' omits a required type argument.",
                            node.Span);
                        return null;
                    }

                    if (argument.Kind
                            is SyntaxKind.ConstantGenericArgument
                                or SyntaxKind.GenericInferenceArgument
                        || BindInterfaceType(argument, typeParameters,
                            interfaces, visibleTypeParameterCount,
                            methodTypeParameters,
                            visibleMethodTypeParameterCount,
                            interfaceConstantParameters:
                                interfaceConstantParameters,
                            methodConstantParameters:
                                methodConstantParameters,
                            allowGenericNominalConstantDomain: true,
                            typeSubstitutions:
                                sourceTypeSubstitutions,
                            constantSubstitutions:
                                sourceConstantSubstitutions)
                            is not BoundInterfaceType boundTypeArgument)
                    {
                        Report("TRM3005",
                            "A generic source type requires supported type arguments.",
                            argument.Span);
                        return null;
                    }

                    genericArguments.Add(
                        BoundInterfaceTypeArgument.ForType(
                            boundTypeArgument));
                    sourceTypeSubstitutions[formal.Name] =
                        boundTypeArgument;
                    continue;
                }

                var constantDomainSyntax = formalSyntax.ConstantDomain;
                if (constantDomainSyntax is null
                    || BindInterfaceType(constantDomainSyntax,
                        typeParameters, interfaces,
                        visibleTypeParameterCount, methodTypeParameters,
                        visibleMethodTypeParameterCount,
                        interfaceConstantParameters:
                            interfaceConstantParameters,
                        methodConstantParameters:
                            methodConstantParameters,
                        allowGenericNominalConstantDomain: true,
                        isGenericNominalConstantDomain: true,
                        typeSubstitutions: sourceTypeSubstitutions,
                        constantSubstitutions:
                            sourceConstantSubstitutions)
                        is not BoundInterfaceType constantType)
                {
                    Report("TRM3005",
                        "A generic source type has an unsupported constant formal domain.",
                        constantDomainSyntax?.Span ?? node.Span);
                    return null;
                }

                if (argument is null
                    || argument.Kind == SyntaxKind.GenericDefaultArgument)
                {
                    argument = formalSyntax.ConstantDefault;
                }
                if (argument is null
                    || argument.Kind == SyntaxKind.GenericInferenceArgument
                    || BindInterfaceConstantTypeArgument(argument,
                        constantType, interfaceConstantParameters,
                        methodConstantParameters,
                        sourceConstantSubstitutions)
                        is not BoundInterfaceTypeArgument constantArgument)
                {
                    Report("TRM3005",
                        "A generic source type requires a supported exact constant argument or default.",
                        argument?.Span ?? node.Span);
                    return null;
                }
                genericArguments.Add(constantArgument);
                sourceConstantSubstitutions[formal.Name] =
                    constantArgument;
            }

            return new BoundInterfaceType(
                BoundInterfaceTypeKind.GenericNominal,
                PrimitiveType.Void, sourceDefinition.QualifiedName, -1,
                genericArguments.MoveToImmutable(),
                isWritableType, isOptionalType, isGcType, false) {
                SourceGenericTypeDefinitionIndex =
                    sourceDefinition.DefinitionIndex,
                IsGenericNominalConstantDomain =
                    isGenericNominalConstantDomain,
            };
        }

        if (allowGenericNominalConstantDomain
            && HasImportedGenericNominalTypeCandidate(name))
        {
            if ((isGenericNominalConstantDomain
                    && (isWritableType || isOptionalType || isGcType))
                || !TryFindGenericNominalTypeDefinitionForTypeTerm(name,
                    out var genericDefinition)
                || (isGenericNominalConstantDomain
                    && genericDefinition.TransparentConstantCarrierType
                        is null)
                || genericDefinition.Formals.IsDefaultOrEmpty
                || genericDefinition.Formals.Length != typeArguments.Length
                || genericDefinition.Formals.Any(static formal =>
                    formal.Constraints.IsDefault
                    || !formal.Constraints.IsEmpty))
            {
                Report("TRM3005",
                    $"Generic nominal type '{name}' is not an admitted transparent constant domain.",
                    node.Span);
                return null;
            }

            var genericArguments = ImmutableArray.CreateBuilder<
                BoundInterfaceTypeArgument>(genericDefinition.Formals.Length);
            var genericTypeSubstitutions =
                CopyInterfaceSubstitutions(typeSubstitutions);
            var genericConstantSubstitutions =
                CopyInterfaceSubstitutions(constantSubstitutions);
            foreach (var formal in genericDefinition.Formals)
            {
                var argument = typeArguments[checked((int)formal.Ordinal)];
                if (formal.Kind == BoundGenericArgumentKind.Type)
                {
                    if (argument.Kind
                            is SyntaxKind.ConstantGenericArgument
                                or SyntaxKind.GenericInferenceArgument
                                or SyntaxKind.GenericDefaultArgument
                        || BindInterfaceType(argument, typeParameters,
                            interfaces, visibleTypeParameterCount,
                            methodTypeParameters,
                            visibleMethodTypeParameterCount,
                            interfaceConstantParameters:
                                interfaceConstantParameters,
                            methodConstantParameters:
                                methodConstantParameters,
                            allowGenericNominalConstantDomain: true,
                            isGenericNominalConstantDomain: false,
                            typeSubstitutions: genericTypeSubstitutions,
                            constantSubstitutions:
                                genericConstantSubstitutions)
                            is not BoundInterfaceType boundArgument)
                    {
                        Report("TRM3005",
                            "A generic transparent constant domain requires supported type arguments.",
                            argument.Span);
                        return null;
                    }

                    genericArguments.Add(
                        BoundInterfaceTypeArgument.ForType(boundArgument));
                    genericTypeSubstitutions[formal.Name] = boundArgument;
                    continue;
                }

                if (formal.Kind != BoundGenericArgumentKind.Constant
                    || formal.ConstantDomainType is not
                        PrimitiveType domainType
                    || formal.ConstantCarrierType is not
                        PrimitiveType carrierType
                    || formal.ConstantDomainTypeTerm is not null
                    || !(PrimitiveTypeFacts.IsIntegerOrWide(carrierType)
                        || carrierType == PrimitiveType.Bool
                        || BoundEnumTypes.IsType(domainType)))
                {
                    Report("TRM3005",
                        "A generic transparent constant domain has an unsupported constant formal.",
                        argument.Span);
                    return null;
                }

                var constantType = ConcreteInterfaceType(domainType);
                if (BindInterfaceConstantTypeArgument(argument,
                        constantType, interfaceConstantParameters,
                        methodConstantParameters,
                        genericConstantSubstitutions)
                    is not BoundInterfaceTypeArgument boundConstant)
                {
                    return null;
                }

                genericArguments.Add(boundConstant);
                genericConstantSubstitutions[formal.Name] = boundConstant;
            }

            return new BoundInterfaceType(
                BoundInterfaceTypeKind.GenericNominal,
                PrimitiveType.Void,
                genericDefinition.Namespace + "." + genericDefinition.Name,
                -1, genericArguments.MoveToImmutable(),
                isWritableType, isOptionalType, isGcType, false) {
                GenericNominalTypeIdentity = genericDefinition.Identity,
                IsGenericNominalConstantDomain =
                    isGenericNominalConstantDomain,
            };
        }

        var before = DiagnosticCount;
        var concreteType = BindType(node, allowVoid);
        return DiagnosticCount == before
            ? ApplyInterfaceTypeQualifiers(
                ConcreteInterfaceType(concreteType), isWritableType,
                isOptionalType, isGcType)
            : null;
    }

    private BoundInterfaceType? BindInterfaceAlias(
        AliasDefinition alias,
        SyntaxNode useSite,
        SyntaxNode[] typeArguments,
        IReadOnlyDictionary<string, int> typeParameters,
        IReadOnlyDictionary<(string Name, int Arity),
            List<InterfaceDeclarationPart>> interfaces,
        int visibleTypeParameterCount,
        IReadOnlyDictionary<string, int>? methodTypeParameters,
        int visibleMethodTypeParameterCount,
        bool allowVoid,
        IReadOnlyDictionary<string, InterfaceConstantFormal>?
            interfaceConstantParameters,
        IReadOnlyDictionary<string, InterfaceConstantFormal>?
            methodConstantParameters,
        bool allowGenericNominalConstantDomain,
        bool isGenericNominalConstantDomain,
        bool isWritable,
        bool isOptional,
        bool isGarbageCollected,
        IReadOnlyDictionary<string, BoundInterfaceType>?
            typeSubstitutions,
        IReadOnlyDictionary<string, BoundInterfaceTypeArgument>?
            constantSubstitutions)
    {
        if (alias.IsInvalid)
        {
            return null;
        }

        if (alias.TypeParameters.Length != typeArguments.Length)
        {
            Report("TRM3005",
                $"Generic type alias '{alias.QualifiedName}' expects {alias.TypeParameters.Length} type arguments but received {typeArguments.Length}.",
                useSite.Span);
            return null;
        }

        var typeParameterList = alias.Declaration.Children
            .OfType<SyntaxNode>()
            .FirstOrDefault(static child =>
                child.Kind == SyntaxKind.TypeParameterList);
        if (typeParameterList is not null
            && typeParameterList.DescendantTokens().Any(static token =>
                token.Text == "const"))
        {
            Report("TRM3005",
                "A generic type alias can only declare type formals.",
                alias.Declaration.Span);
            return null;
        }

        if (alias.IsResolving)
        {
            var first = _aliasResolutionStack.IndexOf(alias);
            var cycle = _aliasResolutionStack.Skip(Math.Max(first, 0))
                .Select(static item => item.QualifiedName)
                .Append(alias.QualifiedName);
            Report("TRM3005",
                $"Recursive type alias cycle: {string.Join(" -> ", cycle)}.",
                useSite.Span);
            alias.IsInvalid = true;
            return null;
        }
        if (_aliasResolutionStack.Count >= MaximumSemanticDepth)
        {
            Report("TRM3015",
                $"Type alias expansion exceeds {MaximumSemanticDepth} declarations.",
                alias.Declaration.Span);
            alias.IsInvalid = true;
            return null;
        }

        var substitutions = new Dictionary<string, BoundInterfaceType>(
            StringComparer.Ordinal);
        if (typeSubstitutions is not null)
        {
            foreach (var (name, type) in typeSubstitutions)
            {
                substitutions.Add(name, type);
            }
        }
        for (var ordinal = 0; ordinal < alias.TypeParameters.Length;
            ordinal++)
        {
            var argument = typeArguments[ordinal];
            if (argument.Kind
                    is SyntaxKind.ConstantGenericArgument
                        or SyntaxKind.ExistentialTypeArgument
                        or SyntaxKind.GenericInferenceArgument
                        or SyntaxKind.GenericDefaultArgument
                || BindInterfaceType(argument, typeParameters, interfaces,
                    visibleTypeParameterCount, methodTypeParameters,
                    visibleMethodTypeParameterCount,
                    interfaceConstantParameters:
                        interfaceConstantParameters,
                    methodConstantParameters: methodConstantParameters,
                    allowGenericNominalConstantDomain: true,
                    typeSubstitutions: typeSubstitutions,
                    constantSubstitutions: constantSubstitutions)
                    is not BoundInterfaceType boundArgument)
            {
                Report("TRM3005",
                    "A generic alias requires complete type arguments.",
                    argument.Span);
                return null;
            }

            substitutions[alias.TypeParameters[ordinal]] = boundArgument;
        }

        var previousContext = CaptureContext();
        SetContext(alias.Tree, alias.Declaration);
        var diagnosticCount = DiagnosticCount;
        alias.IsResolving = true;
        _aliasResolutionStack.Add(alias);
        try
        {
            if (BindInterfaceType(alias.Target, typeParameters, interfaces,
                    visibleTypeParameterCount, methodTypeParameters,
                    visibleMethodTypeParameterCount, allowVoid,
                    interfaceConstantParameters,
                    methodConstantParameters,
                    allowGenericNominalConstantDomain,
                    isGenericNominalConstantDomain,
                    substitutions, constantSubstitutions)
                is not BoundInterfaceType target
                || DiagnosticCount != diagnosticCount)
            {
                if (alias.TypeParameters.IsEmpty)
                {
                    alias.IsInvalid = true;
                }
                return null;
            }

            if (alias.IsPublic && !IsPublicInterfaceType(target))
            {
                Report("TRM3005",
                    $"Public type alias '{alias.QualifiedName}' exposes a non-public target.",
                    useSite.Span);
                if (alias.TypeParameters.IsEmpty)
                {
                    alias.IsInvalid = true;
                }
                return null;
            }

            return ApplyInterfaceTypeQualifiers(target, isWritable,
                isOptional, isGarbageCollected);
        }
        finally
        {
            _aliasResolutionStack.RemoveAt(_aliasResolutionStack.Count - 1);
            alias.IsResolving = false;
            RestoreContext(previousContext);
        }
    }

    private bool IsPublicInterfaceType(BoundInterfaceType type)
    {
        if (type.Kind == BoundInterfaceTypeKind.TypeParameter)
        {
            return true;
        }
        if (type.Kind == BoundInterfaceTypeKind.Primitive)
        {
            return IsPublicType(type.PrimitiveType);
        }
        if (type.Kind == BoundInterfaceTypeKind.Nominal)
        {
            var definition = _interfaces.FirstOrDefault(candidate =>
                candidate.QualifiedName == type.NominalName
                && candidate.Formals.Length == type.TypeArguments.Length
                && candidate.ModuleImport == type.ModuleImport);
            if (definition is not null && !definition.IsPublic)
            {
                return false;
            }
        }
        else if (type.Kind == BoundInterfaceTypeKind.GenericNominal
            && type.SourceGenericTypeDefinitionIndex >= 0)
        {
            var index = type.SourceGenericTypeDefinitionIndex;
            if ((uint)index >= (uint)_sourceGenericTypeDefinitions.Count
                || !_sourceGenericTypeDefinitions[index].IsPublic)
            {
                return false;
            }
        }
        foreach (var argument in type.TypeArguments)
        {
            if ((argument.Type is BoundInterfaceType argumentType
                    && !IsPublicInterfaceType(argumentType))
                || (argument.ConstantType
                    is BoundInterfaceType constantType
                    && !IsPublicInterfaceType(constantType)))
            {
                return false;
            }
        }
        return true;
    }

    private ImmutableArray<BoundInterfaceFormal> GetInterfaceFormals(
        string interfaceName, int arity, BoundModuleImport? moduleImport,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces)
    {
        var existing = _interfaces.FirstOrDefault(definition =>
            definition.QualifiedName == interfaceName
            && definition.Formals.Length == arity
            && definition.ModuleImport == moduleImport);
        if (existing is not null)
        {
            return existing.Formals;
        }
        if (moduleImport is not null
            || !interfaces.TryGetValue((interfaceName, arity), out var parts)
            || parts.Count == 0)
        {
            return [];
        }

        var part = parts[0];
        var typeParameters = part.Formals
            .Select((formal, ordinal) => (formal, ordinal))
            .Where(static item => item.formal.Kind == BoundInterfaceFormalKind.Type)
            .ToDictionary(static item => item.formal.Name,
                static item => item.ordinal, StringComparer.Ordinal);
        var previousContext = CaptureContext();
        SetContext(part.Tree, part.Declaration);
        try
        {
            var writeRequirements
                = _interfaceWriteFormalRequirements.TryGetValue(
                    (interfaceName, arity), out var selectedRequirements)
                    ? selectedRequirements : [];
            return ApplyInterfaceWriteRequirements(
                BindInterfaceFormals(part.Formals, typeParameters, interfaces),
                writeRequirements);
        }
        finally
        {
            RestoreContext(previousContext);
        }
    }

    private static ImmutableArray<BoundInterfaceFormal>
        ApplyInterfaceWriteRequirements(
            ImmutableArray<BoundInterfaceFormal> formals,
            ImmutableArray<BoundInterfaceConstraint> constraints)
    {
        if (formals.IsDefault)
        {
            return formals;
        }

        var requiresWritable = new bool[formals.Length];
        foreach (var constraint in constraints)
        {
            if (constraint.Kind == BoundInterfaceConstraintKind.Write
                && constraint.FormalScope
                    == BoundInterfaceTypeParameterScope.Interface
                && (uint)constraint.FormalOrdinal < (uint)formals.Length
                && formals[constraint.FormalOrdinal].Kind
                    == BoundInterfaceFormalKind.Type)
            {
                requiresWritable[constraint.FormalOrdinal] = true;
            }
        }

        var result = formals.ToBuilder();
        for (var ordinal = 0; ordinal < result.Count; ordinal++)
        {
            result[ordinal] = result[ordinal] with {
                RequiresWritable = requiresWritable[ordinal],
            };
        }

        return result.MoveToImmutable();
    }

    private static ImmutableArray<BoundInterfaceConstraint>
        GetInterfaceWriteFormalOrdinals(
            IReadOnlyList<InterfaceDeclarationPart> parts)
    {
        var result = ImmutableArray.CreateBuilder<BoundInterfaceConstraint>();
        foreach (var part in parts)
        {
            var typeFormalOrdinals = new Dictionary<string, int>(
                StringComparer.Ordinal);
            for (var ordinal = 0; ordinal < part.Formals.Length; ordinal++)
            {
                var formal = part.Formals[ordinal];
                if (formal.Kind == BoundInterfaceFormalKind.Type)
                {
                    _ = typeFormalOrdinals.TryAdd(formal.Name, ordinal);
                }
            }
            foreach (var clause in part.Declaration.Children
                         .OfType<SyntaxNode>()
                         .Where(static node =>
                             node.Kind == SyntaxKind.ConstraintClause))
            {
                var formalName = clause.Children
                    .OfType<SyntaxTokenElement>()
                    .FirstOrDefault(static token =>
                        token.Token.Kind == TokenKind.Identifier)
                    ?.Token.Name;
                if (formalName is null
                    || !typeFormalOrdinals.TryGetValue(formalName,
                        out var formalOrdinal))
                {
                    continue;
                }

                if (clause.Children.OfType<SyntaxNode>().Any(
                        IsWriteConstraintTerm))
                {
                    result.Add(new BoundInterfaceConstraint(formalOrdinal,
                        BoundInterfaceConstraintKind.Write, null, null));
                }
            }
        }

        return result.ToImmutable();
    }

    private BoundInterfaceTypeArgument? BindInterfaceConstantTypeArgument(
        SyntaxNode argument, BoundInterfaceType constantType,
        IReadOnlyDictionary<string, InterfaceConstantFormal>? interfaceConstantParameters,
        IReadOnlyDictionary<string, InterfaceConstantFormal>? methodConstantParameters,
        IReadOnlyDictionary<string, BoundInterfaceTypeArgument>?
            constantSubstitutions = null)
    {
        if (argument.Kind == SyntaxKind.TypeName)
        {
            var name = TypeNameIdentifier(argument);
            if (argument.Children.OfType<SyntaxNode>().Any())
            {
                Report("TRM3005",
                    "A constant generic argument cannot have type arguments.",
                    argument.Span);
                return null;
            }
            if (constantSubstitutions is not null
                && constantSubstitutions.TryGetValue(name,
                    out var substitutedConstant))
            {
                if (substitutedConstant.ConstantType
                        is not BoundInterfaceType substitutedConstantType
                    || !AreInterfaceTypesEquivalent(constantType,
                        substitutedConstantType))
                {
                    Report("TRM3005",
                        "A substituted constant argument has an incompatible value domain.",
                        argument.Span);
                    return null;
                }
                return substitutedConstant with {
                    ConstantType = constantType,
                };
            }
            if (methodConstantParameters is not null
                && methodConstantParameters.TryGetValue(name,
                    out var methodConstant))
            {
                if (!AreInterfaceTypesEquivalent(constantType, methodConstant.Type))
                {
                    Report("TRM3005",
                        "A constant generic argument has an incompatible value domain.",
                        argument.Span);
                    return null;
                }
                return BoundInterfaceTypeArgument.ForConstantParameter(
                    constantType, methodConstant.Scope, methodConstant.Ordinal);
            }
            if (interfaceConstantParameters is not null
                && interfaceConstantParameters.TryGetValue(name,
                    out var interfaceConstant))
            {
                if (!AreInterfaceTypesEquivalent(constantType, interfaceConstant.Type))
                {
                    Report("TRM3005",
                        "A constant generic argument has an incompatible value domain.",
                        argument.Span);
                    return null;
                }
                return BoundInterfaceTypeArgument.ForConstantParameter(
                    constantType, interfaceConstant.Scope, interfaceConstant.Ordinal);
            }
        }

        var expression = argument.Kind switch {
            SyntaxKind.ConstantGenericArgument
                when argument.Children.OfType<SyntaxNode>().SingleOrDefault()
                    is SyntaxNode constantExpression => constantExpression,
            SyntaxKind.TypeName => new SyntaxNode(SyntaxKind.NameExpression,
                argument.Children, argument.Span.Start),
            _ => argument,
        };
        var constant = BindInterfaceConstantDefault(expression,
            constantType, argument.Span);
        return constant is BoundConstant value
            ? BoundInterfaceTypeArgument.ForConstant(constantType, value) : null;
    }

    private static bool AreInterfaceTypesEquivalent(
        BoundInterfaceType left, BoundInterfaceType right)
        => BoundInterfaceContract.AreEquivalent(left, right);

    private static BoundInterfaceType ConcreteInterfaceType(PrimitiveType type) =>
        new(BoundInterfaceTypeKind.Primitive, type, null, -1, [], false, false, false, false);

    private static BoundInterfaceType ApplyInterfaceTypeQualifiers(
        BoundInterfaceType type, bool isWritable, bool isOptional,
        bool isGarbageCollected) =>
        type with {
            IsWritable = type.IsWritable || isWritable,
            IsOptional = type.IsOptional || isOptional,
            IsGarbageCollected =
                type.IsGarbageCollected || isGarbageCollected,
        };

    private static Dictionary<string, T> CopyInterfaceSubstitutions<T>(
        IReadOnlyDictionary<string, T>? substitutions)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        if (substitutions is not null)
        {
            foreach (var (name, value) in substitutions)
            {
                result.Add(name, value);
            }
        }
        return result;
    }

    private string? ResolveInterfaceName(
        string name,
        int arity,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        TextSpan span,
        out ResolvedInterfaceType? resolvedInterface)
    {
        resolvedInterface = null;
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        if (name.Contains('.', StringComparison.Ordinal))
        {
            _ = candidates.Add(name);
        }
        else
        {
            _ = candidates.Add(name);
            _ = candidates.Add(Qualify(name));
            var parent = _namespace;
            while (parent.Contains('.', StringComparison.Ordinal))
            {
                parent = parent[..parent.LastIndexOf('.', StringComparison.Ordinal)];
                _ = candidates.Add(parent + "." + name);
            }
            foreach (var imported in _usings)
            {
                _ = candidates.Add(imported + "." + name);
            }
        }
        var localMatches = interfaces
            .Where(item => candidates.Contains(item.Key.Name)
                && CanOmitInterfaceArguments(item.Value[0].Formals, arity))
            .Select(static item => item.Key)
            .ToArray();
        var exactLocalMatches = localMatches
            .Where(key => key.Arity == arity).ToArray();
        var selectedLocalMatches = exactLocalMatches.Length == 0
            ? localMatches : exactLocalMatches;
        if (selectedLocalMatches.Length > 1)
        {
            Report("TRM3005", $"Interface type '{name}' is ambiguous.", span);
            return null;
        }
        if (selectedLocalMatches.Length == 1)
        {
            var (selectedName, selectedArity) = selectedLocalMatches[0];
            var formals = GetInterfaceFormals(selectedName, selectedArity,
                moduleImport: null, interfaces);
            resolvedInterface = new ResolvedInterfaceType(selectedName, null, formals);
            return selectedName;
        }

        var importedInterfaces = _interfaces.Where(definition =>
                definition.ModuleImport is not null
                && candidates.Contains(definition.QualifiedName)
                && CanOmitInterfaceArguments(definition.Formals, arity))
            .OrderByDescending(definition => definition.Formals.Length == arity)
            .GroupBy(static definition => definition.ModuleImport)
            .Select(static group => group.First()).ToArray();
        if (importedInterfaces.Length > 1)
        {
            Report("TRM3005", $"Imported interface type '{name}' is ambiguous.", span);
            return null;
        }
        if (importedInterfaces.Length == 1)
        {
            resolvedInterface = new ResolvedInterfaceType(
                importedInterfaces[0].QualifiedName,
                importedInterfaces[0].ModuleImport,
                importedInterfaces[0].Formals);
            return importedInterfaces[0].QualifiedName;
        }
        return null;
    }

    private static bool CanOmitInterfaceArguments(
        ImmutableArray<InterfaceFormalSyntax> formals, int suppliedCount)
    {
        if ((uint)suppliedCount > (uint)formals.Length)
        {
            return false;
        }
        for (var ordinal = suppliedCount; ordinal < formals.Length; ordinal++)
        {
            if (formals[ordinal].TypeDefault is null
                && formals[ordinal].ConstantDefault is null)
            {
                return false;
            }
        }
        return true;
    }

    private static bool CanOmitInterfaceArguments(
        ImmutableArray<BoundInterfaceFormal> formals, int suppliedCount)
    {
        if ((uint)suppliedCount > (uint)formals.Length)
        {
            return false;
        }
        for (var ordinal = suppliedCount; ordinal < formals.Length; ordinal++)
        {
            if (formals[ordinal].TypeDefault is null
                && formals[ordinal].ConstantDefault is null)
            {
                return false;
            }
        }
        return true;
    }

    private ImmutableArray<BoundInterfaceMemberDefinition> BindInterfaceMembers(
        InterfaceDeclarationPart part,
        IReadOnlyDictionary<string, int> interfaceTypeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        ImmutableArray<BoundInterfaceFormal> ownerFormals,
        int firstOrdinal,
        int interfaceIndex,
        NativeCallingConvention? interfaceDefaultConvention,
        SyntaxNode? interfaceDefaultExpression,
        List<int> inheritedDefaultMemberOrdinals)
    {
        var result = ImmutableArray.CreateBuilder<BoundInterfaceMemberDefinition>();
        var interfaceConstantParameters = InterfaceConstantParameters(ownerFormals,
            BoundInterfaceTypeParameterScope.Interface);
        foreach (var declaration in part.Declaration.Children.OfType<SyntaxNode>())
        {
            var kind = declaration.Kind switch {
                SyntaxKind.FunctionDeclaration => BoundInterfaceMemberKind.Callable,
                SyntaxKind.OperatorDeclaration => BoundInterfaceMemberKind.Operator,
                SyntaxKind.PropertyDeclaration => BoundInterfaceMemberKind.Property,
                SyntaxKind.IndexerDeclaration => BoundInterfaceMemberKind.Indexer,
                SyntaxKind.AssociatedTypeRequirement => BoundInterfaceMemberKind.AssociatedType,
                SyntaxKind.InterfaceConstantDeclaration => BoundInterfaceMemberKind.Constant,
                _ => (BoundInterfaceMemberKind?)null,
            };
            if (kind is not BoundInterfaceMemberKind memberKind)
            {
                continue;
            }

            SetContext(part.Tree, declaration);
            var nativeCallableAttributes = declaration.Children
                .OfType<SyntaxNode>()
                .Where(static child => child.Kind == SyntaxKind.Attribute)
                .Where(IsNativeCallableAttribute)
                .ToArray();
            var directNativeCallableAttributes = new HashSet<SyntaxNode>(
                nativeCallableAttributes, ReferenceEqualityComparer.Instance);
            foreach (var attribute in declaration.Children
                         .OfType<SyntaxNode>()
                         .Where(static child => child.Kind
                             is SyntaxKind.TypeParameterList
                                 or SyntaxKind.ConstraintClause)
                         .SelectMany(static child => child.DescendantNodes())
                         .Where(static child => child.Kind
                             == SyntaxKind.Attribute)
                         .Where(IsNativeCallableAttribute))
            {
                _ = directNativeCallableAttributes.Add(attribute);
            }
            foreach (var attribute in declaration.DescendantNodes()
                         .Where(static child => child.Kind
                             == SyntaxKind.Attribute)
                         .Where(IsNativeCallableAttribute)
                         .Where(attribute =>
                             !directNativeCallableAttributes.Contains(attribute)))
            {
                Report("TRM3030",
                    "NativeCallable is only valid on an interface or ordinary method declaration.",
                    attribute.Span);
            }

            NativeCallingConvention? nativeCallableConvention = null;
            SyntaxNode? nativeCallableConventionExpression = null;
            if (nativeCallableAttributes.Length != 0)
            {
                if (memberKind != BoundInterfaceMemberKind.Callable)
                {
                    Report("TRM3030",
                        "NativeCallable is only valid on an ordinary interface method.",
                        nativeCallableAttributes[0].Span);
                }
                else if (nativeCallableAttributes.Length != 1)
                {
                    Report("TRM3030",
                        "NativeCallable is a nonrepeatable interface method attribute.",
                        nativeCallableAttributes[1].Span);
                }
                else if (TryBindNativeCallableAttribute(
                    nativeCallableAttributes[0],
                    out var selectedConvention,
                    out nativeCallableConventionExpression))
                {
                    nativeCallableConvention = nativeCallableConventionExpression
                        is null
                        ? selectedConvention
                        : NativeCallingConvention.Native;
                }
            }
            else if (memberKind == BoundInterfaceMemberKind.Callable)
            {
                nativeCallableConvention = interfaceDefaultConvention;
                if (interfaceDefaultExpression is not null
                    && nativeCallableConvention is not null)
                {
                    nativeCallableConvention = NativeCallingConvention.Native;
                    inheritedDefaultMemberOrdinals.Add(
                        firstOrdinal + result.Count);
                }
            }
            if (nativeCallableConventionExpression is SyntaxNode
                methodConventionExpression)
            {
                _pendingInterfaceNativeCallableConventions.Add(
                    new PendingInterfaceNativeCallableConvention(
                        interfaceIndex, firstOrdinal + result.Count, [],
                        part.Tree, methodConventionExpression));
            }

            var typeNode = FirstInterfaceTypeNode(declaration);
            var methodTypeParameterNode = declaration.Children.OfType<SyntaxNode>()
                .FirstOrDefault(static child => child.Kind == SyntaxKind.TypeParameterList);
            var methodFormalSyntax = ParseInterfaceFormals(methodTypeParameterNode);
            var methodFormals = BindInterfaceMemberFormals(methodFormalSyntax,
                interfaceTypeParameters, interfaces, ownerFormals.Length,
                interfaceConstantParameters);
            var methodTypeParameters = methodFormals
                .Where(static formal => formal.Kind == BoundInterfaceFormalKind.Type)
                .ToDictionary(static formal => formal.Name, static formal => formal.Ordinal,
                    StringComparer.Ordinal);
            var methodConstantParameters = InterfaceConstantParameters(
                methodFormals, BoundInterfaceTypeParameterScope.Member);

            var parameters = ImmutableArray.CreateBuilder<BoundInterfaceParameter>();
            var parameterList = declaration.Children.OfType<SyntaxNode>()
                .FirstOrDefault(static child => child.Kind is SyntaxKind.ParameterList
                    or SyntaxKind.IndexerParameterList);
            if (parameterList is not null)
            {
                var parameterOrdinal = 0;
                foreach (var parameter in parameterList.Children.OfType<SyntaxNode>()
                             .Where(static child => child.Kind == SyntaxKind.Parameter))
                {
                    if (BindInterfaceParameter(parameter, interfaceTypeParameters,
                        methodTypeParameters, interfaces, ownerFormals.Length,
                        methodFormals.Length, interfaceConstantParameters,
                        methodConstantParameters, interfaceIndex,
                        firstOrdinal + result.Count, parameterOrdinal)
                        is BoundInterfaceParameter boundParameter)
                    {
                        parameters.Add(boundParameter);
                    }
                    parameterOrdinal++;
                }
            }

            BoundInterfaceType? resultType = null;
            if (memberKind is BoundInterfaceMemberKind.Callable or BoundInterfaceMemberKind.Operator)
            {
                resultType = typeNode is null ? null
                    : BindInterfaceType(typeNode, interfaceTypeParameters, interfaces,
                        ownerFormals.Length, methodTypeParameters, methodFormals.Length,
                        allowVoid: true,
                        interfaceConstantParameters: interfaceConstantParameters,
                        methodConstantParameters: methodConstantParameters);
            }
            else if (memberKind is BoundInterfaceMemberKind.Property or BoundInterfaceMemberKind.Indexer)
            {
                resultType = typeNode is null ? null
                    : BindInterfaceType(typeNode, interfaceTypeParameters, interfaces,
                        ownerFormals.Length,
                        interfaceConstantParameters: interfaceConstantParameters);
            }

            BoundInterfaceType? associatedDefaultType = null;
            if (memberKind == BoundInterfaceMemberKind.AssociatedType
                && AssociatedTypeDefaultNode(declaration) is SyntaxNode associatedDefault)
            {
                associatedDefaultType = BindInterfaceType(associatedDefault,
                    interfaceTypeParameters, interfaces, ownerFormals.Length,
                    interfaceConstantParameters: interfaceConstantParameters);
            }

            BoundInterfaceType? associatedConstantType = null;
            BoundConstant? associatedConstantDefault = null;
            if (memberKind == BoundInterfaceMemberKind.Constant && typeNode is not null)
            {
                associatedConstantType = BindInterfaceType(typeNode,
                    interfaceTypeParameters, interfaces, ownerFormals.Length,
                    interfaceConstantParameters:
                        interfaceConstantParameters,
                    allowGenericNominalConstantDomain: true,
                    isGenericNominalConstantDomain: true);
                if (AssociatedConstantDefaultNode(declaration)
                    is SyntaxNode constantDefault)
                {
                    Report("TRM3005",
                        "An interface associated constant cannot declare a default value; each witness supplies an explicit binding.",
                        constantDefault.Span);
                }
            }

            var constraints = ImmutableArray.CreateBuilder<BoundInterfaceConstraint>();
            BindInterfaceConstraints(declaration, interfaceTypeParameters, interfaces,
                constraints, methodTypeParameters, interfaceConstantParameters,
                methodConstantParameters);
            var isStatic = declaration.Children.OfType<SyntaxTokenElement>()
                .Any(static token => token.Token.Kind == TokenKind.StaticKeyword);
            var requiresMutableReceiver = memberKind
                    == BoundInterfaceMemberKind.Callable
                && declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static token =>
                        token.Token.Kind == TokenKind.MutKeyword);
            if (declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static token =>
                        token.Token.Kind == TokenKind.MutKeyword)
                && (memberKind != BoundInterfaceMemberKind.Callable
                    || isStatic))
            {
                Report("TRM3005",
                    "A mutable receiver requirement is only valid on an ordinary instance interface callable.",
                    declaration.Span);
            }
            var accessors = BindInterfaceAccessors(declaration, interfaceTypeParameters,
                methodTypeParameters, interfaces, ownerFormals.Length,
                methodFormals.Length, resultType, isStatic,
                interfaceConstantParameters, methodConstantParameters);
            var operatorToken = memberKind == BoundInterfaceMemberKind.Operator
                ? InterfaceOperatorToken(declaration) : TokenKind.Bad;
            var name = InterfaceMemberName(declaration, memberKind, typeNode, operatorToken);
            foreach (var attribute in declaration.Children.OfType<SyntaxNode>()
                         .Where(static child => child.Kind
                             == SyntaxKind.Attribute)
                         .Where(IsForeignNativeInterfaceAttribute))
            {
                Report("TRM3030",
                    "ForeignNativeInterface is only valid on an interface declaration.",
                    attribute.Span);
            }
            var attributes = InterfaceDeclarationAttributes(declaration);
            if (attributes.Contains("NoThrow", StringComparer.Ordinal))
            {
                Report("TRM3017", "The general NoThrow source contract is retired.", declaration.Span);
            }
            var contractTokens = declaration.Children.OfType<SyntaxNode>()
                .Where(static child => child.Kind == SyntaxKind.ContractClause)
                .Select(NormalizeSyntax).Where(static text => text is not null)
                .Select(static text => text!).ToImmutableArray();
            var dependencies = BindInterfaceMemberDependencies(
                declaration, memberKind, parameters.ToImmutable(), resultType);
            var declarationKey = InterfaceMemberKey(memberKind, name, operatorToken,
                parameters, methodFormals, requiresMutableReceiver,
                accessors);
            var accessibility = BindInterfaceAccessibility(declaration.Children, declaration.Span);
            var hasBody = declaration.DescendantNodes().Any(static node =>
                node.Kind is SyntaxKind.Block or SyntaxKind.ExpressionBody);
            var declaredRequiresUnsafeCaller = declaration.Children
                .OfType<SyntaxTokenElement>()
                .Any(static token =>
                    token.Token.Kind == TokenKind.UnsafeKeyword);
            if (accessibility == BoundInterfaceAccessibility.Private && !hasBody)
            {
                Report("TRM3001", "A private interface helper must have a complete body.", declaration.Span);
            }
            result.Add(new BoundInterfaceMemberDefinition(
                firstOrdinal + result.Count,
                name,
                declarationKey,
                memberKind,
                accessibility,
                isStatic,
                declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.ExternKeyword),
                declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.Identifier
                        && token.Token.Text == "partial"),
                declaredRequiresUnsafeCaller
                    || nativeCallableConvention is not null,
                attributes.Contains("DoesNotReturn", StringComparer.Ordinal),
                declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.AsyncKeyword),
                hasBody,
                attributes,
                InterfaceAttributeTokens(declaration),
                contractTokens,
                operatorToken,
                resultType,
                methodFormals,
                constraints.ToImmutable(),
                parameters.ToImmutable(),
                dependencies,
                accessors,
                associatedDefaultType,
                associatedConstantType,
                associatedConstantDefault)
            {
                DeclaredRequiresUnsafeCaller = declaredRequiresUnsafeCaller,
                RequiresMutableReceiver = requiresMutableReceiver,
                NativeCallableConvention = nativeCallableConvention,
                SourceInputOrdinal = _trees.IndexOf(part.Tree),
                SourceDeclarationSpan = declaration.Span,
            });
        }
        return result.ToImmutable();
    }

    private ImmutableArray<BoundInterfaceFormal> BindInterfaceMemberFormals(
        ImmutableArray<InterfaceFormalSyntax> formals,
        IReadOnlyDictionary<string, int> interfaceTypeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        int interfaceFormalCount,
        IReadOnlyDictionary<string, InterfaceConstantFormal> interfaceConstantParameters)
    {
        var result = ImmutableArray.CreateBuilder<BoundInterfaceFormal>(formals.Length);
        var priorMethodTypeParameters = new Dictionary<string, int>(StringComparer.Ordinal);
        var priorMethodConstantParameters = new Dictionary<string,
            InterfaceConstantFormal>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < formals.Length; ordinal++)
        {
            var formal = formals[ordinal];
            if (formal.IsSelf || interfaceTypeParameters.ContainsKey(formal.Name)
                || interfaceConstantParameters.ContainsKey(formal.Name))
            {
                Report("TRM3005", "Method generic formals cannot redeclare interface formals or use <this>.",
                    formal.Span);
                continue;
            }
            var constantType = formal.Kind == BoundInterfaceFormalKind.Constant
                && formal.ConstantType is SyntaxNode constantTypeSyntax
                    ? BindInterfaceType(constantTypeSyntax, interfaceTypeParameters, interfaces,
                        interfaceFormalCount, priorMethodTypeParameters, ordinal,
                        interfaceConstantParameters: interfaceConstantParameters,
                        methodConstantParameters: priorMethodConstantParameters)
                    : null;
            var typeDefault = formal.Kind == BoundInterfaceFormalKind.Type
                && formal.TypeDefault is SyntaxNode typeDefaultSyntax
                    ? BindInterfaceType(typeDefaultSyntax, interfaceTypeParameters, interfaces,
                        interfaceFormalCount, priorMethodTypeParameters, ordinal,
                        interfaceConstantParameters: interfaceConstantParameters,
                        methodConstantParameters: priorMethodConstantParameters)
                    : null;
            BoundConstant? constantDefault = null;
            if (formal.ConstantDefault is SyntaxNode constantDefaultSyntax)
            {
                constantDefault = BindInterfaceConstantDefault(constantDefaultSyntax,
                    constantType, formal.Span);
            }
            result.Add(new BoundInterfaceFormal(
                ordinal, formal.Name, formal.Kind, false,
                [.. formal.Attributes.Select(AttributeName)],
                [.. formal.Attributes.Select(NormalizeSyntax).Where(static text => text is not null)
                    .Select(static text => text!)],
                constantType, typeDefault, constantDefault));
            if (formal.Kind == BoundInterfaceFormalKind.Type
                && !priorMethodTypeParameters.TryAdd(formal.Name, ordinal))
            {
                Report("TRM3005", $"Duplicate method type formal '{formal.Name}'.", formal.Span);
            }
            if (formal.Kind == BoundInterfaceFormalKind.Constant
                && constantType is not null
                && !priorMethodConstantParameters.TryAdd(formal.Name,
                    new InterfaceConstantFormal(constantType,
                        BoundInterfaceTypeParameterScope.Member, ordinal)))
            {
                Report("TRM3005", $"Duplicate method constant formal '{formal.Name}'.",
                    formal.Span);
            }
        }
        return result.ToImmutable();
    }

    private ImmutableArray<BoundDependencyContract> BindInterfaceMemberDependencies(
        SyntaxNode declaration,
        BoundInterfaceMemberKind memberKind,
        ImmutableArray<BoundInterfaceParameter> parameters,
        BoundInterfaceType? resultType,
        bool? receiverOverride = null)
    {
        var attributes = new List<(SyntaxNode Attribute, int? InlineParameterOrdinal)>();
        foreach (var attribute in declaration.Children.OfType<SyntaxNode>()
                     .Where(static child => child.Kind == SyntaxKind.Attribute))
        {
            if (TryGetDependencyRelation(AttributeName(attribute), out _))
            {
                if (memberKind is BoundInterfaceMemberKind.Property or BoundInterfaceMemberKind.Indexer)
                {
                    Report("TRM3023",
                        "Dependency annotations on an interface property must target an exact accessor.",
                        attribute.Span);
                }
                else
                {
                    attributes.Add((attribute, null));
                }
            }
        }
        var parameterSyntax = declaration.Children.OfType<SyntaxNode>()
            .Where(static child => child.Kind is SyntaxKind.ParameterList
                or SyntaxKind.IndexerParameterList)
            .SelectMany(static child => child.Children.OfType<SyntaxNode>()
                .Where(static parameter => parameter.Kind == SyntaxKind.Parameter))
            .ToArray();
        var ordinal = 0;
        foreach (var parameter in parameterSyntax)
        {
            foreach (var attribute in parameter.Children.OfType<SyntaxNode>()
                         .Where(static child => child.Kind == SyntaxKind.Attribute))
            {
                if (TryGetDependencyRelation(AttributeName(attribute), out _))
                {
                    attributes.Add((attribute, ordinal));
                }
            }
            ordinal++;
        }
        if (attributes.Count == 0)
        {
            return [];
        }

        var hasReceiver = receiverOverride
            ?? declaration.Children.OfType<SyntaxTokenElement>()
                .All(static token => token.Token.Kind != TokenKind.StaticKeyword);
        var placeholderParameters = parameters.Select(static parameter => new Parameter(
            parameter.Name,
            parameter.Type.Kind == BoundInterfaceTypeKind.Primitive
                ? parameter.Type.PrimitiveType : PrimitiveType.Int32,
            default,
            parameter.Mode is BoundInterfaceParameterMode.Out or BoundInterfaceParameterMode.Init,
            parameter.Mode == BoundInterfaceParameterMode.Init,
            parameter.Mode == BoundInterfaceParameterMode.ImplicitRef)).ToImmutableArray();
        var hasResult = resultType is not null
            && !(resultType.Kind == BoundInterfaceTypeKind.Primitive
                && resultType.PrimitiveType == PrimitiveType.Void);
        var contracts = ImmutableArray.CreateBuilder<BoundDependencyContract>();
        var contractSpans = new List<TextSpan>();
        var seenTargets = new HashSet<(BoundDependencyRelation, BoundDependencyDestination, int?)>();
        foreach (var (attribute, inlineParameterOrdinal) in attributes)
        {
            if (!TryGetDependencyRelation(AttributeName(attribute), out var relation))
            {
                continue;
            }
            var target = GetDependencyAttributeTarget(attribute);
            if (inlineParameterOrdinal is int inlineOrdinal)
            {
                if (target.Kind != DependencyAttributeTargetKind.Default
                    || (uint)inlineOrdinal >= (uint)parameters.Length
                    || parameters[inlineOrdinal].Mode != BoundInterfaceParameterMode.Init
                    || relation is not (BoundDependencyRelation.DependsOn
                        or BoundDependencyRelation.ForwardsBacking))
                {
                    Report("TRM3023",
                        "An inline dependency annotation must target an init parameter and cannot select a second target.",
                        attribute.Span);
                    continue;
                }
                target = new DependencyAttributeTarget(
                    DependencyAttributeTargetKind.Parameter, parameters[inlineOrdinal].Name);
            }
            if (!TryResolveInterfaceDependencyDestination(
                relation, target, parameters, resultType, hasReceiver,
                attribute.Span, out var destination, out var destinationOrdinal))
            {
                continue;
            }
            if (!TryBindDependencySources(attribute, placeholderParameters, hasReceiver,
                out var sources))
            {
                continue;
            }
            if (relation == BoundDependencyRelation.ForwardsBacking && sources.IsEmpty)
            {
                Report("TRM3023", "ForwardsBacking requires at least one source.", attribute.Span);
                continue;
            }
            if (!seenTargets.Add((relation, destination, destinationOrdinal)))
            {
                Report("TRM3023", "A dependency schema cannot repeat the same effective target.",
                    attribute.Span);
                continue;
            }
            contracts.Add(new BoundDependencyContract(relation, destination,
                destinationOrdinal, sources));
            contractSpans.Add(attribute.Span);
        }

        var boundContracts = contracts.ToImmutable();
        for (var index = 0; index < boundContracts.Length; index++)
        {
            var forwarding = boundContracts[index];
            if (forwarding.Relation != BoundDependencyRelation.ForwardsBacking)
            {
                continue;
            }
            var hasRelationship = false;
            var permittedSources = new HashSet<BoundDependencySource>();
            foreach (var relationship in boundContracts)
            {
                if (relationship.Destination != forwarding.Destination
                    || relationship.DestinationOrdinal != forwarding.DestinationOrdinal
                    || relationship.Relation is not (BoundDependencyRelation.DependsOn
                        or BoundDependencyRelation.Captures))
                {
                    continue;
                }
                hasRelationship = true;
                permittedSources.UnionWith(relationship.Sources);
            }
            if (!hasRelationship || forwarding.Sources.Any(source => !permittedSources.Contains(source)))
            {
                Report("TRM3023",
                    "ForwardsBacking requires an existing dependency or capture relationship and cannot add sources.",
                    contractSpans[index]);
            }
        }
        return boundContracts;
    }

    private bool TryResolveInterfaceDependencyDestination(
        BoundDependencyRelation relation,
        DependencyAttributeTarget target,
        ImmutableArray<BoundInterfaceParameter> parameters,
        BoundInterfaceType? resultType,
        bool hasReceiver,
        TextSpan span,
        out BoundDependencyDestination destination,
        out int? destinationOrdinal)
    {
        destination = default;
        destinationOrdinal = null;
        var hasResult = resultType is not null
            && !(resultType.Kind == BoundInterfaceTypeKind.Primitive
                && resultType.PrimitiveType == PrimitiveType.Void);
        switch (target.Kind)
        {
            case DependencyAttributeTargetKind.Default:
                if (relation == BoundDependencyRelation.DependsOn)
                {
                    if (hasResult)
                    {
                        destination = BoundDependencyDestination.Result;
                        return true;
                    }
                    Report("TRM3023", "DependsOn requires a result or init-output target.", span);
                    return false;
                }
                if (relation == BoundDependencyRelation.Captures)
                {
                    if (hasReceiver)
                    {
                        destination = BoundDependencyDestination.Receiver;
                        return true;
                    }
                    Report("TRM3023", "Captures requires an explicit destination on a static interface member.", span);
                    return false;
                }
                if (hasResult)
                {
                    destination = BoundDependencyDestination.Result;
                    return true;
                }
                if (hasReceiver)
                {
                    destination = BoundDependencyDestination.Receiver;
                    return true;
                }
                Report("TRM3023", "ForwardsBacking requires a result or receiver destination.", span);
                return false;

            case DependencyAttributeTargetKind.Return:
                if (!hasResult || (relation == BoundDependencyRelation.Captures
                    && !CanCaptureInterfaceValue(resultType!)))
                {
                    Report("TRM3023", "The return target is incompatible with this dependency schema.", span);
                    return false;
                }
                destination = BoundDependencyDestination.Result;
                return true;

            case DependencyAttributeTargetKind.Receiver:
                if (relation == BoundDependencyRelation.DependsOn || !hasReceiver)
                {
                    Report("TRM3023", "The receiver target is incompatible with this dependency schema.", span);
                    return false;
                }
                destination = BoundDependencyDestination.Receiver;
                return true;

            case DependencyAttributeTargetKind.Parameter:
                var parameterOrdinal = FindParameterOrdinal(target.ParameterName,
                    [.. parameters.Select(static parameter => new Parameter(parameter.Name,
                        PrimitiveType.Int32, default,
                        parameter.Mode is BoundInterfaceParameterMode.Out
                            or BoundInterfaceParameterMode.Init,
                        parameter.Mode == BoundInterfaceParameterMode.Init,
                        parameter.Mode == BoundInterfaceParameterMode.ImplicitRef))]);
                if (parameterOrdinal < 0)
                {
                    Report("TRM3023", $"Dependency annotation targets unknown parameter '{target.ParameterName}'.",
                        span);
                    return false;
                }
                var formal = parameters[parameterOrdinal];
                if (relation == BoundDependencyRelation.DependsOn)
                {
                    if (formal.Mode != BoundInterfaceParameterMode.Init)
                    {
                        Report("TRM3023", "A parameter DependsOn target requires an init destination.", span);
                        return false;
                    }
                    destination = BoundDependencyDestination.InitializedOutput;
                }
                else if (formal.Mode == BoundInterfaceParameterMode.Init)
                {
                    destination = BoundDependencyDestination.InitializedOutput;
                }
                else if (relation == BoundDependencyRelation.Captures
                    && !CanCaptureInterfaceParameter(formal))
                {
                    Report("TRM3023", "Captures requires a retained formal destination.", span);
                    return false;
                }
                else if (relation == BoundDependencyRelation.ForwardsBacking
                    && !CanCaptureInterfaceParameter(formal))
                {
                    Report("TRM3023", "ForwardsBacking requires an initialized output or retained formal destination.", span);
                    return false;
                }
                else
                {
                    destination = BoundDependencyDestination.RetainedFormal;
                }
                destinationOrdinal = parameterOrdinal;
                return true;

            case DependencyAttributeTargetKind.Operation:
                Report("TRM3023", "The operation target requires an admitted completion relationship.", span);
                return false;

            default:
                Report("TRM3023", "Dependency annotation target is unsupported.", span);
                return false;
        }
    }

    private bool CanCaptureInterfaceParameter(BoundInterfaceParameter parameter) =>
        parameter.Mode is BoundInterfaceParameterMode.Out or BoundInterfaceParameterMode.Init
        or BoundInterfaceParameterMode.ImplicitRef
        || parameter.Type.Kind is BoundInterfaceTypeKind.TypeParameter or BoundInterfaceTypeKind.Nominal
        || (parameter.Type.Kind == BoundInterfaceTypeKind.CheckedReference
            && parameter.Type.IsWritable && !parameter.Type.IsGarbageCollected
            && !parameter.Type.IsInitialization && !parameter.Type.IsOptional)
        || (parameter.Type.Kind == BoundInterfaceTypeKind.Primitive
            && CanCaptureParameter(new Parameter(parameter.Name,
                parameter.Type.PrimitiveType, default, false)));

    private bool CanCaptureInterfaceValue(BoundInterfaceType type) =>
        type.Kind is BoundInterfaceTypeKind.TypeParameter or BoundInterfaceTypeKind.Nominal
        || (type.Kind == BoundInterfaceTypeKind.CheckedReference
            && type.IsWritable && !type.IsGarbageCollected
            && !type.IsInitialization && !type.IsOptional)
        || (type.Kind == BoundInterfaceTypeKind.Primitive
            && CanCaptureValue(type.PrimitiveType));

    private BoundInterfaceParameter? BindInterfaceParameter(
        SyntaxNode parameter,
        IReadOnlyDictionary<string, int> interfaceTypeParameters,
        IReadOnlyDictionary<string, int> methodTypeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        int interfaceFormalCount,
        int methodFormalCount,
        IReadOnlyDictionary<string, InterfaceConstantFormal> interfaceConstantParameters,
        IReadOnlyDictionary<string, InterfaceConstantFormal> methodConstantParameters,
        int interfaceIndex, int memberOrdinal, int parameterOrdinal)
    {
        SyntaxNode? defaultSyntax = null;
        for (var index = 0; index + 1 < parameter.Children.Length;
            index++)
        {
            if (parameter.Children[index] is SyntaxTokenElement {
                    Token.Kind: TokenKind.Equals,
                }
                && parameter.Children[index + 1] is SyntaxNode expression)
            {
                defaultSyntax = expression;
                break;
            }
        }
        var typeNode = parameter.Children.OfType<SyntaxNode>()
            .FirstOrDefault(static child => child.Kind != SyntaxKind.Attribute);
        var nameToken = parameter.Children.OfType<SyntaxTokenElement>()
            .LastOrDefault(static token => token.Token.Kind == TokenKind.Identifier)?.Token;
        if (typeNode is null || nameToken is null
            || BindInterfaceType(typeNode, interfaceTypeParameters, interfaces,
                interfaceFormalCount, methodTypeParameters, methodFormalCount,
                allowVoid: true,
                interfaceConstantParameters: interfaceConstantParameters,
                methodConstantParameters: methodConstantParameters)
                is not BoundInterfaceType type)
        {
            Report("TRM3005", "An interface parameter requires a complete type and identity.",
                parameter.Span);
            return null;
        }

        var mode = parameter.Children.OfType<SyntaxTokenElement>().Any(static token =>
                token.Token.Kind == TokenKind.OutKeyword)
            ? BoundInterfaceParameterMode.Out
            : type.Kind == BoundInterfaceTypeKind.CheckedReference
                && type.IsInitialization
                ? BoundInterfaceParameterMode.Init
            : parameter.Children.OfType<SyntaxTokenElement>().Any(static token =>
                token.Token.Kind == TokenKind.Identifier && token.Token.Text == "init")
                ? BoundInterfaceParameterMode.Init
                : parameter.Children.OfType<SyntaxTokenElement>().Any(static token =>
                    token.Token.Kind == TokenKind.Identifier && token.Token.Text == "implicit")
                    ? BoundInterfaceParameterMode.ImplicitRef
                    : BoundInterfaceParameterMode.Value;
        if (type is {
                Kind: BoundInterfaceTypeKind.Primitive,
                PrimitiveType: PrimitiveType.Void,
            }
            && mode != BoundInterfaceParameterMode.Value)
        {
            Report("TRM3005",
                "Unit is only supported for by-value interface parameters.",
                parameter.Span);
            return null;
        }
        if (defaultSyntax is not null)
        {
            if (mode != BoundInterfaceParameterMode.Value)
            {
                Report("TRM3005",
                    "Reference and output interface parameters cannot declare defaults.",
                    defaultSyntax.Span);
                return null;
            }
            if (type.Kind != BoundInterfaceTypeKind.Primitive
                || (type.PrimitiveType != PrimitiveType.Void
                    && !ConstEvalScalar(type.PrimitiveType)))
            {
                Report("TRM3005",
                    "An interface parameter default requires a supported scalar type.",
                    defaultSyntax.Span);
                return null;
            }

            _pendingInterfaceParameterDefaults.Add(
                new PendingInterfaceParameterDefault(interfaceIndex,
                    memberOrdinal, parameterOrdinal, type, _tree,
                    defaultSyntax));
        }
        var attributes = parameter.Children.OfType<SyntaxNode>()
            .Where(static child => child.Kind == SyntaxKind.Attribute)
            .Select(AttributeName).ToImmutableArray();
        var attributeTokens = parameter.Children.OfType<SyntaxNode>()
            .Where(static child => child.Kind == SyntaxKind.Attribute)
            .Select(NormalizeSyntax).Where(static text => text is not null)
            .Select(static text => text!).ToImmutableArray();
        return new BoundInterfaceParameter(nameToken.Name, type, mode,
            attributes, attributeTokens) {
            HasPendingDefault = defaultSyntax is not null,
            DefaultSyntaxSpan = defaultSyntax?.Span ?? default,
        };
    }

    private BoundExpression? BindInterfaceParameterDefaultExpression(
        SyntaxNode expression, BoundInterfaceType type)
    {
        if (type.Kind != BoundInterfaceTypeKind.Primitive
            || (type.PrimitiveType != PrimitiveType.Void
                && !ConstEvalScalar(type.PrimitiveType)))
        {
            Report("TRM3005",
                "An interface parameter default requires a supported scalar type.",
                expression.Span);
            return null;
        }

        var diagnostics = DiagnosticCount;
        var bound = BindExpression(expression, type.PrimitiveType);
        if (DiagnosticCount != diagnostics
            || bound.Type != type.PrimitiveType
            || (type.PrimitiveType == PrimitiveType.Void
                ? bound is not BoundConstant {
                    Type: PrimitiveType.Void,
                }
                : !CheckConstExpression(bound, out _)))
        {
            Report("TRM3005",
                "An interface parameter default must be an exact checked compile-time expression.",
                expression.Span);
            return null;
        }

        if (type.PrimitiveType == PrimitiveType.Void)
        {
            return bound;
        }

        var folded = FoldScalarConstant(bound);
        return folded is BoundConstant constant
            && constant.Type == type.PrimitiveType
                ? constant : bound;
    }

    private ImmutableArray<BoundInterfaceAccessor> BindInterfaceAccessors(
        SyntaxNode declaration,
        IReadOnlyDictionary<string, int> interfaceTypeParameters,
        IReadOnlyDictionary<string, int> methodTypeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        int interfaceFormalCount,
        int methodFormalCount,
        BoundInterfaceType? propertyType,
        bool isStatic,
        IReadOnlyDictionary<string, InterfaceConstantFormal> interfaceConstantParameters,
        IReadOnlyDictionary<string, InterfaceConstantFormal> methodConstantParameters)
    {
        var result = ImmutableArray.CreateBuilder<BoundInterfaceAccessor>();
        foreach (var accessor in declaration.Children.OfType<SyntaxNode>())
        {
            var kind = accessor.Kind switch {
                SyntaxKind.GetAccessor => BoundInterfaceAccessorKind.Get,
                SyntaxKind.SetAccessor => BoundInterfaceAccessorKind.Set,
                SyntaxKind.InitAccessor => BoundInterfaceAccessorKind.Init,
                _ => (BoundInterfaceAccessorKind?)null,
            };
            if (kind is not BoundInterfaceAccessorKind accessorKind)
            {
                continue;
            }
            foreach (var attribute in accessor.Children.OfType<SyntaxNode>()
                         .Where(static child => child.Kind
                             == SyntaxKind.Attribute)
                         .Where(IsForeignNativeInterfaceAttribute))
            {
                Report("TRM3030",
                    "ForeignNativeInterface is only valid on an interface declaration.",
                    attribute.Span);
            }
            var attributes = InterfaceDeclarationAttributes(accessor);
            if (attributes.Contains("NoThrow", StringComparer.Ordinal))
            {
                Report("TRM3017", "The general NoThrow source contract is retired.", accessor.Span);
            }
            var valueParameters = (accessorKind is BoundInterfaceAccessorKind.Set
                or BoundInterfaceAccessorKind.Init)
                && propertyType is BoundInterfaceType assignedType
                    ? [new BoundInterfaceParameter(
                        "value", assignedType, BoundInterfaceParameterMode.Value, [], [])]
                    : ImmutableArray<BoundInterfaceParameter>.Empty;
            var accessorResult = accessorKind == BoundInterfaceAccessorKind.Get
                ? propertyType
                : ConcreteInterfaceType(PrimitiveType.Void);
            var dependencies = BindInterfaceMemberDependencies(accessor,
                BoundInterfaceMemberKind.Callable, valueParameters, accessorResult,
                receiverOverride: !isStatic);
            var accessibility = BindInterfaceAccessibility(accessor.Children, accessor.Span);
            var hasBody = accessor.DescendantNodes().Any(static node =>
                node.Kind is SyntaxKind.Block or SyntaxKind.ExpressionBody);
            if (accessibility == BoundInterfaceAccessibility.Private && !hasBody)
            {
                Report("TRM3001", "A private interface accessor must have a complete body.", accessor.Span);
            }
            var isMutable = accessor.Children.OfType<SyntaxTokenElement>()
                .Any(static token =>
                    token.Token.Kind == TokenKind.MutKeyword);
            if (isMutable
                && (isStatic
                    || accessorKind == BoundInterfaceAccessorKind.Init))
            {
                Report("TRM3005",
                    "Mutable receiver access is only valid on ordinary instance accessors.",
                    accessor.Span);
            }
            result.Add(new BoundInterfaceAccessor(
                accessorKind,
                accessibility,
                isMutable,
                accessor.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.UnsafeKeyword),
                attributes.Contains("DoesNotReturn", StringComparer.Ordinal),
                hasBody,
                attributes,
                InterfaceAttributeTokens(accessor),
                dependencies,
                [.. accessor.Children.OfType<SyntaxNode>()
                    .Where(static child => child.Kind == SyntaxKind.ContractClause)
                    .Select(NormalizeSyntax).Where(static text => text is not null)
                    .Select(static text => text!)]));
        }
        return result.ToImmutable();
    }

    private bool HasUniqueInterfaceMembers(
        ImmutableArray<BoundInterfaceMemberDefinition>.Builder members,
        TextSpan span)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < members.Count; index++)
        {
            var member = members[index];
            if (!keys.Add(member.DeclarationKey))
            {
                Report("TRM3003", $"Duplicate interface member '{member.Name}'.", span);
                return false;
            }
            for (var previous = 0; previous < index; previous++)
            {
                var existing = members[previous];
                if (existing.Name == member.Name
                    && (existing.Kind is not (BoundInterfaceMemberKind.Callable or BoundInterfaceMemberKind.Operator)
                        || member.Kind is not (BoundInterfaceMemberKind.Callable or BoundInterfaceMemberKind.Operator)))
                {
                    Report("TRM3003", $"Conflicting interface member '{member.Name}'.", span);
                    return false;
                }
            }
        }
        return true;
    }

    private static SyntaxNode? FirstInterfaceTypeNode(SyntaxNode declaration) =>
        declaration.Children.OfType<SyntaxNode>().FirstOrDefault(static child =>
            child.Kind is SyntaxKind.TypeName or SyntaxKind.ReferenceTypeName
                or SyntaxKind.AttributedTypeName or SyntaxKind.FixedArrayTypeName);

    private static SyntaxNode? AssociatedTypeDefaultNode(SyntaxNode declaration)
    {
        for (var index = 0; index < declaration.Children.Length; index++)
        {
            if (declaration.Children[index] is not SyntaxTokenElement { Token.Kind: TokenKind.Equals })
            {
                continue;
            }
            return declaration.Children.Skip(index + 1).OfType<SyntaxNode>().FirstOrDefault();
        }
        return null;
    }

    private static SyntaxNode? AssociatedConstantDefaultNode(SyntaxNode declaration) =>
        AssociatedTypeDefaultNode(declaration);

    private BoundInterfaceAccessibility BindInterfaceAccessibility(
        IReadOnlyList<SyntaxElement> elements, TextSpan span)
    {
        var result = BoundInterfaceAccessibility.Default;
        foreach (var element in elements)
        {
            BoundInterfaceAccessibility access;
            if (element is SyntaxNode { Kind: SyntaxKind.FriendVisibility })
            {
                access = BoundInterfaceAccessibility.Friend;
            }
            else if (element is SyntaxTokenElement token)
            {
                access = token.Token.Kind switch {
                    TokenKind.PublicKeyword => BoundInterfaceAccessibility.Public,
                    TokenKind.InternalKeyword => BoundInterfaceAccessibility.Internal,
                    TokenKind.PrivateKeyword => BoundInterfaceAccessibility.Private,
                    TokenKind.Identifier when token.Token.Text == "file" => BoundInterfaceAccessibility.File,
                    _ => BoundInterfaceAccessibility.Default,
                };
            }
            else
            {
                continue;
            }
            if (access == BoundInterfaceAccessibility.Default)
            {
                continue;
            }
            if (result != BoundInterfaceAccessibility.Default)
            {
                Report("TRM3001", "An interface declaration or requirement may specify one access domain.", span);
                return result;
            }
            result = access;
        }
        return result;
    }

    private static ImmutableArray<string> InterfaceDeclarationAttributes(SyntaxNode declaration) =>
        [.. declaration.Children.OfType<SyntaxNode>()
            .Where(static child => child.Kind is SyntaxKind.Attribute or SyntaxKind.FriendVisibility)
            .Select(static child => child.Kind == SyntaxKind.Attribute
                ? AttributeName(child) : NormalizeSyntax(child) ?? "")];

    private static ImmutableArray<string> InterfaceAttributeTokens(SyntaxNode declaration) =>
        [.. declaration.Children.OfType<SyntaxNode>()
            .Where(static child => child.Kind is SyntaxKind.Attribute or SyntaxKind.FriendVisibility)
            .Select(NormalizeSyntax).Where(static text => text is not null)
            .Select(static text => text!)];

    private ImmutableArray<string> InterfaceMergeAttributeTokens(
        InterfaceDeclarationPart part)
    {
        var previousContext = CaptureContext();
        SetContext(part.Tree, part.Declaration);
        try
        {
            return [.. part.Declaration.Children.OfType<SyntaxNode>()
                .Where(static child => child.Kind is SyntaxKind.Attribute
                    or SyntaxKind.FriendVisibility)
                .Where(child => child.Kind != SyntaxKind.Attribute
                    || !IsNativeCallableAttribute(child))
                .Select(NormalizeSyntax).Where(static text => text is not null)
                .Select(static text => text!)];
        }
        finally
        {
            RestoreContext(previousContext);
        }
    }

    private static TokenKind InterfaceOperatorToken(SyntaxNode declaration)
    {
        for (var index = 0; index + 1 < declaration.Children.Length; index++)
        {
            if (declaration.Children[index] is SyntaxTokenElement {
                Token.Kind: TokenKind.OperatorKeyword,
            } && declaration.Children[index + 1] is SyntaxTokenElement operatorToken)
            {
                return operatorToken.Token.Kind;
            }
        }
        return TokenKind.Bad;
    }

    private static string InterfaceMemberName(
        SyntaxNode declaration,
        BoundInterfaceMemberKind kind,
        SyntaxNode? typeNode,
        TokenKind operatorToken)
    {
        if (kind == BoundInterfaceMemberKind.Indexer)
        {
            return "this";
        }
        if (kind == BoundInterfaceMemberKind.Operator)
        {
            for (var index = 0; index + 1 < declaration.Children.Length; index++)
            {
                if (declaration.Children[index] is SyntaxTokenElement {
                    Token.Kind: TokenKind.OperatorKeyword,
                } && declaration.Children[index + 1] is SyntaxTokenElement symbol)
                {
                    return symbol.Token.Text;
                }
            }
            return operatorToken.ToString();
        }
        if (kind == BoundInterfaceMemberKind.AssociatedType)
        {
            for (var index = 0; index + 1 < declaration.Children.Length; index++)
            {
                if (declaration.Children[index] is SyntaxTokenElement {
                    Token.Kind: TokenKind.Identifier, Token.Text: "type",
                } && declaration.Children[index + 1] is SyntaxTokenElement associatedName)
                {
                    return associatedName.Token.Name;
                }
            }
        }
        if (typeNode is not null)
        {
            var typeIndex = declaration.Children.IndexOf(typeNode);
            if (typeIndex >= 0)
            {
                var nameToken = declaration.Children.Skip(typeIndex + 1)
                    .OfType<SyntaxTokenElement>()
                    .FirstOrDefault(static token => token.Token.Kind == TokenKind.Identifier);
                if (nameToken is not null)
                {
                    return nameToken.Token.Name;
                }
            }
        }
        return "";
    }

    private static string InterfaceMemberKey(
        BoundInterfaceMemberKind kind,
        string name,
        TokenKind operatorToken,
        ImmutableArray<BoundInterfaceParameter>.Builder parameters,
        ImmutableArray<BoundInterfaceFormal> methodFormals,
        bool requiresMutableReceiver,
        ImmutableArray<BoundInterfaceAccessor> accessors)
    {
        var parts = new List<string> {
            ((byte)kind).ToString(System.Globalization.CultureInfo.InvariantCulture),
            name,
            operatorToken.ToString(),
            methodFormals.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (requiresMutableReceiver)
        {
            parts.Add("mutable-receiver");
        }
        foreach (var accessor in accessors.Where(
                     static accessor => accessor.IsMutable))
        {
            parts.Add("mutable-accessor");
            parts.Add(((byte)accessor.Kind).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }
        parts.AddRange(methodFormals.Select(static formal => ((byte)formal.Kind)
            .ToString(System.Globalization.CultureInfo.InvariantCulture)));
        foreach (var parameter in parameters)
        {
            parts.Add(((byte)parameter.Mode).ToString(System.Globalization.CultureInfo.InvariantCulture));
            parts.Add(InterfaceTypeKey(parameter.Type));
        }
        return string.Join('|', parts);
    }

    private static string InterfaceTypeKey(BoundInterfaceType type) =>
        $"{(byte)type.Kind}:{(int)type.PrimitiveType}:{type.NominalName}:{InterfaceImportKey(type.ModuleImport)}:{GenericNominalIdentityKey(type.GenericNominalTypeIdentity)}:{type.SourceGenericTypeDefinitionIndex}:{type.IsGenericNominalConstantDomain}:{type.TypeParameterScope}:{type.TypeParameterOrdinal}:{type.IsWritable}:{type.IsOptional}:{type.IsGarbageCollected}:{type.IsInitialization}[{string.Join(",", type.TypeArguments.Select(InterfaceTypeArgumentKey))}]";

    private static string GenericNominalIdentityKey(
        BoundGenericNominalTypeIdentity? identity) =>
        identity is BoundGenericNominalTypeIdentity value
            ? $"{value.ProviderIdentity.Length}:{value.ProviderIdentity}:{value.ModuleIdentity.Length}:{value.ModuleIdentity}:{value.DefinitionId}"
            : "";

    private static string InterfaceImportKey(BoundModuleImport? import) =>
        import is null ? "" : $"{import.ProducerIdentity}:{import.ExportKey}:{import.TargetRequirement}:{import.RuntimePolicy}";

    private static string InterfaceTypeArgumentKey(
        BoundInterfaceTypeArgument argument) =>
        argument.Kind switch {
            BoundInterfaceTypeArgumentKind.Type
                => $"T:{InterfaceTypeKey(argument.Type!)}",
            BoundInterfaceTypeArgumentKind.ExistentialType => "X",
            _ => $"C:{InterfaceTypeKey(argument.ConstantType!)}:"
                + (argument.ConstantValue is BoundConstant constant
                    ? $"{(int)constant.Type}:{constant.Bits.Low}:{constant.Bits.High}"
                    : $"{argument.ConstantParameterScope}:{argument.ConstantParameterOrdinal}"),
        };
    private void BindInterfaceConstraints(
        SyntaxNode declaration,
        IReadOnlyDictionary<string, int> typeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        ImmutableArray<BoundInterfaceConstraint>.Builder constraints,
        Dictionary<string, int>? methodTypeParameters = null,
        IReadOnlyDictionary<string, InterfaceConstantFormal>? interfaceConstantParameters = null,
        IReadOnlyDictionary<string, InterfaceConstantFormal>? methodConstantParameters = null)
    {
        foreach (var constraint in declaration.Children.OfType<SyntaxNode>()
                     .Where(static node => node.Kind == SyntaxKind.ConstraintClause))
        {
            var formalName = constraint.Children.OfType<SyntaxTokenElement>()
                .FirstOrDefault(static token => token.Token.Kind == TokenKind.Identifier)?.Token.Name;
            var formalOrdinal = -1;
            var formalScope = BoundInterfaceTypeParameterScope.Interface;
            var foundFormal = formalName is not null
                && typeParameters.TryGetValue(formalName, out formalOrdinal);
            if (!foundFormal && formalName is not null && methodTypeParameters is not null
                && methodTypeParameters.TryGetValue(formalName, out formalOrdinal))
            {
                foundFormal = true;
                formalScope = BoundInterfaceTypeParameterScope.Member;
            }
            if (!foundFormal || formalName is null)
            {
                Report("TRM3005", "An interface constraint must target a declared type formal.", constraint.Span);
                continue;
            }
            foreach (var item in constraint.Children.OfType<SyntaxNode>())
            {
                if (item.Kind == SyntaxKind.Attribute)
                {
                    if (IsNativeCallableAttribute(item))
                    {
                        Report("TRM3030",
                            "NativeCallable is not valid on an interface constraint.",
                            item.Span);
                    }
                    else
                    {
                        constraints.Add(new BoundInterfaceConstraint(
                            formalOrdinal, BoundInterfaceConstraintKind.Attribute, null, AttributeName(item))
                            { FormalScope = formalScope });
                    }
                }
                else if (formalScope
                        == BoundInterfaceTypeParameterScope.Interface
                    && IsWriteConstraintTerm(item))
                {
                    constraints.Add(new BoundInterfaceConstraint(
                        formalOrdinal, BoundInterfaceConstraintKind.Write,
                        null, null)
                        { FormalScope = formalScope });
                }
                else if (item.Kind == SyntaxKind.ConstraintTerm)
                {
                    Report("TRM3005",
                        "This generic property constraint is not supported by interface contracts.",
                        item.Span);
                }
                else if (BindInterfaceType(item, typeParameters, interfaces,
                    int.MaxValue, methodTypeParameters,
                    interfaceConstantParameters: interfaceConstantParameters,
                    methodConstantParameters: methodConstantParameters,
                    allowGenericNominalConstantDomain: true)
                    is BoundInterfaceType type)
                {
                    constraints.Add(new BoundInterfaceConstraint(
                        formalOrdinal, BoundInterfaceConstraintKind.Type,
                        type, null)
                    {
                        FormalScope = formalScope,
                    });
                }
            }
        }
    }
}
