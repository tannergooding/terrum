// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private const int MaximumStaticSpecializations = 32;
    private readonly Dictionary<string, StaticProtocol> _protocols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GenericTemplate> _templates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, PrimitiveType>> _conformances = new(StringComparer.Ordinal);

    internal readonly record struct StaticOperatorKey(TokenKind Operator, int Arity);
    private sealed record StaticProtocol(string Name, StaticOperatorKey Key,
        bool RequiresUnsafeCaller, bool HasSelf, SyntaxNode Declaration, SyntaxTree Tree);
    internal sealed record GenericOperatorConstraint(string Protocol, PrimitiveType? FixedResult,
        int ResultTypeParameterIndex)
    {
        internal PrimitiveType Resolve(ImmutableArray<PrimitiveType> types) =>
            FixedResult ?? types[ResultTypeParameterIndex];
    }
    internal sealed record GenericTypeParameter(string Name,
        ImmutableDictionary<StaticOperatorKey, GenericOperatorConstraint> Operations)
    {
        internal byte OwnershipDomain { get; init; } = 1;
        internal byte NullabilityDomain { get; init; } = 1;
        internal SyntaxNode? TypeDefaultSyntax { get; init; }
        internal bool RequiresBlittable { get; init; }
        internal bool RequiresWritable { get; init; }
        internal ImmutableArray<int> DefaultTypeParameterDependencies
            { get; init; } = [];
    }

    private readonly record struct GenericMethodTypeFormalSyntax(
        string Name, SyntaxNode? TypeDefault);

    internal sealed record GenericParameter(string Name, PrimitiveType? ScalarType, int TypeParameterIndex,
        bool IsOut, bool IsInit)
    {
        internal CheckedReferenceTypeDefinition? ReferenceTypePattern
        { get; init; }

        internal PrimitiveType Resolve(ImmutableArray<PrimitiveType> types) =>
            ScalarType ?? types[TypeParameterIndex];
    }
    internal sealed record GenericTemplate(string Name, ImmutableArray<GenericTypeParameter> TypeParameters,
        PrimitiveType? ScalarReturnType, int ReturnTypeParameterIndex, ImmutableArray<GenericParameter> Parameters,
        SyntaxNode Body, SyntaxNode Declaration, SyntaxTree Tree)
    {
        internal int DefinitionOrdinal { get; init; } = -1;
        internal bool IsPublic { get; init; }
        internal bool RequiresUnsafeCaller { get; init; }
        internal CheckedReferenceTypeDefinition?
            ReferenceReturnTypePattern { get; init; }
        internal ImmutableArray<BoundDependencyContract>
            DependencyContracts { get; init; } = [];

        internal PrimitiveType ReturnType(ImmutableArray<PrimitiveType> types) =>
            ScalarReturnType ?? types[ReturnTypeParameterIndex];

        internal int ParameterIndex(string name)
        {
            for (var index = 0; index < TypeParameters.Length; index++)
            {
                if (TypeParameters[index].Name == name)
                {
                    return index;
                }
            }
            return -1;
        }
    }

    private static ImmutableArray<BoundGenericMethodOperatorRequirement>
        GenericOperatorRequirements(GenericTypeParameter formal,
            Dictionary<string, int> protocolOrdinals) =>
        [.. formal.Operations
            .OrderBy(static pair => pair.Key.Operator)
            .ThenBy(static pair => pair.Key.Arity)
            .Select(pair =>
                new BoundGenericMethodOperatorRequirement(
                    pair.Key.Operator, pair.Key.Arity,
                    pair.Value.Protocol, pair.Value.FixedResult,
                    pair.Value.ResultTypeParameterIndex,
                    protocolOrdinals[pair.Value.Protocol]))];

    internal sealed record GenericInstantiation(GenericTemplate Template, ImmutableArray<PrimitiveType> Types);
    private ImmutableArray<BoundFunction>.Builder? _genericProofFunctions;
    private Binder? _genericProofSource;

    private PrimitiveType? HydrateProofType(
        PrimitiveType sourceType,
        bool allowFloatingScalar = false,
        bool allowFloatingStructFields = false)
    {
        var source = _genericProofSource ?? throw new InvalidOperationException("Missing generic proof source.");
        if (ManagedClassTypes.IsManaged(sourceType))
        {
            if ((int)sourceType < ManagedClassTypes.First)
            {
                return sourceType;
            }
            var @class = source._classes[ManagedClassTypes.ClassIndex(sourceType)];
            if (!_classNames.TryGetValue(@class.Name, out var mapped))
            {
                mapped = (PrimitiveType)(ManagedClassTypes.First + _classes.Count);
                _classNames.Add(@class.Name, mapped);
                _classes.Add(@class with { Fields = [] });
                var classFields = ImmutableArray.CreateBuilder<BoundStructField>(@class.Fields.Length);
                foreach (var field in @class.Fields)
                {
                    if (HydrateProofType(field.Type) is not PrimitiveType fieldType)
                    {
                        return null;
                    }
                    classFields.Add(field with { Type = fieldType });
                }
                _classes[ManagedClassTypes.ClassIndex(mapped)] = @class with { Fields = classFields.MoveToImmutable() };
            }
            if (ManagedClassTypes.IsWritable(sourceType))
            {
                mapped = ManagedClassTypes.Writable(mapped);
            }
            return ManagedClassTypes.IsOptional(sourceType) ? ManagedClassTypes.Optional(mapped) : mapped;
        }
        if (BoundEnumTypes.IsType(sourceType))
        {
            var sourceIndex = BoundEnumTypes.Index(sourceType);
            if ((uint)sourceIndex >= (uint)source._enumDefinitions.Count)
            {
                return null;
            }

            var identity = source._enumDefinitions[sourceIndex].Identity;
            for (var index = 0; index < _enumDefinitions.Count; index++)
            {
                if (_enumDefinitions[index].Identity == identity)
                {
                    return BoundEnumTypes.Type(index);
                }
            }

            return null;
        }
        if (!source.IsStruct(sourceType))
        {
            return sourceType is PrimitiveType.Void or PrimitiveType.Bool or PrimitiveType.Char16
                or PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
                || (allowFloatingScalar && sourceType is PrimitiveType.Float32 or PrimitiveType.Float64)
                ? sourceType : null;
        }
        var structure = source._structs[(int)sourceType - 100];
        if (structure.IsUnique)
        {
            return null;
        }
        if (_structNames.TryGetValue(structure.Name, out var existing))
        {
            return existing;
        }
        var fields = ImmutableArray.CreateBuilder<BoundStructField>(structure.Fields.Length);
        foreach (var field in structure.Fields)
        {
            if (HydrateProofType(field.Type, allowFloatingScalar: allowFloatingStructFields,
                allowFloatingStructFields: allowFloatingStructFields) is not PrimitiveType fieldType)
            {
                return null;
            }
            fields.Add(field with { Type = fieldType });
        }
        var result = (PrimitiveType)(100 + _structs.Count);
        _structNames.Add(structure.Name, result);
        _structs.Add(structure with { Fields = fields.MoveToImmutable() });
        if (source._conformances.TryGetValue(structure.Name, out var witnesses))
        {
            var mapped = new Dictionary<string, PrimitiveType>(StringComparer.Ordinal);
            foreach (var (protocol, type) in witnesses)
            {
                mapped.Add(protocol, HydrateProofType(type)
                    ?? throw new InvalidOperationException("Missing proof witness result."));
            }
            _conformances.Add(structure.Name, mapped);
        }
        return result;
    }

    private bool HydrateProofFunction(int sourceIndex)
    {
        var source = _genericProofSource ?? throw new InvalidOperationException("Missing generic proof source.");
        var declaration = source._signatures[sourceIndex];
        var owner = declaration.StructName is null ? null
            : declaration.Name[..declaration.Name.LastIndexOf('.', StringComparison.Ordinal)];
        var allowFloating = owner is null || source._structNames.ContainsKey(owner);
        if (declaration.IsCleanup || declaration.IsClassConstructor
            || HydrateProofType(declaration.ReturnType,
                allowFloatingScalar: allowFloating, allowFloatingStructFields: allowFloating)
                is not PrimitiveType result)
        {
            return false;
        }
        var parameters = ImmutableArray.CreateBuilder<Parameter>(declaration.Parameters.Length);
        foreach (var parameter in declaration.Parameters)
        {
            if (HydrateProofType(parameter.Type,
                allowFloatingScalar: allowFloating, allowFloatingStructFields: allowFloating)
                is not PrimitiveType type)
            {
                return false;
            }
            parameters.Add(parameter with { Type = type });
        }
        PrimitiveType? receiver = null;
        if (owner is not null)
        {
            if (!source._structNames.TryGetValue(owner, out var sourceType)
                && !source._classNames.TryGetValue(owner, out sourceType))
            {
                return false;
            }
            if (HydrateProofType(sourceType,
                allowFloatingStructFields: source._structNames.ContainsKey(owner))
                is not PrimitiveType mapped)
            {
                return false;
            }
            receiver = source.IsClass(sourceType) && declaration.IsMutable
                ? ManagedClassTypes.Writable(mapped) : mapped;
        }
        var variables = ImmutableArray.CreateBuilder<BoundVariable>();
        if (receiver is PrimitiveType receiverType && !declaration.IsStatic)
        {
            variables.Add(new BoundVariable("this", receiverType,
                declaration.External?.Variables[0].IsMutable
                    ?? (declaration.IsConstructor
                        || (declaration.IsMutable && !ManagedClassTypes.IsClass(receiverType))),
                declaration.Tree.Root.Span));
        }
        variables.AddRange(parameters.Select(parameter => new BoundVariable(parameter.Name,
            parameter.Type, parameter.IsOut, parameter.Span) {
            IsOut = parameter.IsOut,
            IsInit = parameter.IsInit,
        }));
        var external = new BoundFunction(declaration.Name, result, variables.ToImmutable(),
            parameters.Count + (receiver is not null && !declaration.IsStatic && !declaration.IsConstructor ? 1 : 0),
            new BoundBlock([], declaration.Tree.Root.Span), declaration.Tree.Root.Span) {
            IsExternal = true,
            IsConstructor = declaration.IsConstructor,
            IsStatic = declaration.IsStatic,
            IsOperator = declaration.IsOperator,
            NoFailureProof = declaration.NoFailureProof,
            DoesNotReturn = declaration.DoesNotReturn,
            RequiresUnsafeCaller = declaration.RequiresUnsafeCaller,
            DeclaringStruct = owner,
        };
        _functions.Add(declaration.Name, _signatures.Count);
        _signatures.Add(declaration with {
            ReturnType = result,
            Parameters = parameters.MoveToImmutable(),
            External = external,
        });
        (_genericProofFunctions ?? throw new InvalidOperationException("Missing generic proof functions.")).Add(external);
        return true;
    }

    private static bool IsStaticBinaryOperator(TokenKind kind) => kind is TokenKind.Plus or TokenKind.Minus
        or TokenKind.Star or TokenKind.Slash or TokenKind.Percent or TokenKind.Ampersand
        or TokenKind.Pipe or TokenKind.Caret or TokenKind.EqualsEquals or TokenKind.BangEquals
        or TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals;

    private static bool IsStaticUnaryOperator(TokenKind kind) =>
        kind is TokenKind.Plus or TokenKind.Minus or TokenKind.Bang or TokenKind.Tilde
            or TokenKind.PlusPlus or TokenKind.MinusMinus;

    internal static bool IsComparisonOperator(TokenKind kind) => kind is TokenKind.EqualsEquals or TokenKind.BangEquals
        or TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals;

    private static PrimitiveType StaticOperatorResult(TokenKind kind, PrimitiveType operand) =>
        IsComparisonOperator(kind) ? PrimitiveType.Bool : operand;

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Only supported static operators have names.")]
    private static string OperatorName(StaticOperatorKey key) => key switch {
        { Operator: TokenKind.Plus, Arity: 1 } => "op_UnaryPlus",
        { Operator: TokenKind.Minus, Arity: 1 } => "op_UnaryNegation",
        { Operator: TokenKind.Bang, Arity: 1 } => "op_LogicalNot",
        { Operator: TokenKind.Tilde, Arity: 1 } => "op_OnesComplement",
        { Operator: TokenKind.PlusPlus, Arity: 1 } => "op_Increment",
        { Operator: TokenKind.MinusMinus, Arity: 1 } => "op_Decrement",
        { Operator: TokenKind.Plus, Arity: 2 } => "op_Addition",
        { Operator: TokenKind.Minus, Arity: 2 } => "op_Subtraction",
        { Operator: TokenKind.Star, Arity: 2 } => "op_Multiply",
        { Operator: TokenKind.Slash, Arity: 2 } => "op_Division",
        { Operator: TokenKind.Percent, Arity: 2 } => "op_Modulus",
        { Operator: TokenKind.Ampersand, Arity: 2 } => "op_BitwiseAnd",
        { Operator: TokenKind.Pipe, Arity: 2 } => "op_BitwiseOr",
        { Operator: TokenKind.Caret, Arity: 2 } => "op_ExclusiveOr",
        { Operator: TokenKind.EqualsEquals, Arity: 2 } => "op_Equality",
        { Operator: TokenKind.BangEquals, Arity: 2 } => "op_Inequality",
        { Operator: TokenKind.Less, Arity: 2 } => "op_LessThan",
        { Operator: TokenKind.LessEquals, Arity: 2 } => "op_LessThanOrEqual",
        { Operator: TokenKind.Greater, Arity: 2 } => "op_GreaterThan",
        { Operator: TokenKind.GreaterEquals, Arity: 2 } => "op_GreaterThanOrEqual",
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    internal static bool TryGetStaticOperatorKey(string name, string owner, out StaticOperatorKey key)
    {
        key = default;
        var nameSpan = name.AsSpan();
        var ownerSpan = owner.AsSpan();
        if (nameSpan.Length <= ownerSpan.Length + 1
            || !nameSpan.StartsWith(ownerSpan, StringComparison.Ordinal)
            || nameSpan[ownerSpan.Length] != '.')
        {
            return false;
        }
        var operatorName = nameSpan[(ownerSpan.Length + 1)..];
        if (operatorName.StartsWith("$producer.", StringComparison.Ordinal))
        {
            operatorName = operatorName["$producer.".Length..];
        }
        key = operatorName.ToString() switch {
            "op_UnaryPlus" => new StaticOperatorKey(TokenKind.Plus, 1),
            "op_UnaryNegation" => new StaticOperatorKey(TokenKind.Minus, 1),
            "op_LogicalNot" => new StaticOperatorKey(TokenKind.Bang, 1),
            "op_OnesComplement" => new StaticOperatorKey(TokenKind.Tilde, 1),
            "op_Increment" => new StaticOperatorKey(TokenKind.PlusPlus, 1),
            "op_Decrement" => new StaticOperatorKey(TokenKind.MinusMinus, 1),
            "op_Addition" => new StaticOperatorKey(TokenKind.Plus, 2),
            "op_Subtraction" => new StaticOperatorKey(TokenKind.Minus, 2),
            "op_Multiply" => new StaticOperatorKey(TokenKind.Star, 2),
            "op_Division" => new StaticOperatorKey(TokenKind.Slash, 2),
            "op_Modulus" => new StaticOperatorKey(TokenKind.Percent, 2),
            "op_BitwiseAnd" => new StaticOperatorKey(TokenKind.Ampersand, 2),
            "op_BitwiseOr" => new StaticOperatorKey(TokenKind.Pipe, 2),
            "op_ExclusiveOr" => new StaticOperatorKey(TokenKind.Caret, 2),
            "op_Equality" => new StaticOperatorKey(TokenKind.EqualsEquals, 2),
            "op_Inequality" => new StaticOperatorKey(TokenKind.BangEquals, 2),
            "op_LessThan" => new StaticOperatorKey(TokenKind.Less, 2),
            "op_LessThanOrEqual" => new StaticOperatorKey(TokenKind.LessEquals, 2),
            "op_GreaterThan" => new StaticOperatorKey(TokenKind.Greater, 2),
            "op_GreaterThanOrEqual" => new StaticOperatorKey(TokenKind.GreaterEquals, 2),
            _ => default,
        };
        return key.Arity != 0;
    }

    private static PrimitiveType? GenericScalarType(string name) => name switch {
        "int32" => PrimitiveType.Int32,
        "uint32" => PrimitiveType.UInt32,
        "int64" => PrimitiveType.Int64,
        "uint64" => PrimitiveType.UInt64,
        "char16" => PrimitiveType.Char16,
        "bool" => PrimitiveType.Bool,
        _ => null,
    };

    private static PrimitiveType? GenericOutputScalarType(string name) => name switch {
        "float32" => PrimitiveType.Float32,
        "float64" => PrimitiveType.Float64,
        _ => null,
    };

    private bool IsCopyGenericType(PrimitiveType type)
    {
        var checkedReferenceTypes = _checkedReferenceTypes.ToImmutableArray();
        return PrimitiveTypeFacts.ScalarWidth(type) > 0
            || type == PrimitiveType.NativeRawPointer
            || NativePointerTypes.IsType(type)
            || NativeFunctionPointerTypes.IsType(type)
            || IsCopyNativeFixedArrayType(type)
            || IsStruct(type)
            || (ManagedClassTypes.IsManaged(type)
                && ((int)type < ManagedClassTypes.First
                    || ManagedClassTypes.ClassIndex(type) < _classes.Count))
            || IsCopyGenericNominalType(type)
            || (CheckedReferenceTypeContract.IsValidType(type,
                    checkedReferenceTypes)
                && CheckedReferenceTypeContract.IsSupportedReferenceStoreValueType(
                    type, _structs, _classes, _enumDefinitions.Count,
                    checkedReferenceTypes));
    }

    private bool IsCopyNativeFixedArrayType(PrimitiveType type)
    {
        if (!NativeFixedArrayTypes.IsType(type))
        {
            return false;
        }

        var index = NativeFixedArrayTypes.Index(type);
        if ((uint)index >= (uint)_nativeFixedArrayTypes.Count)
        {
            return false;
        }

        var elementType = _nativeFixedArrayTypes[index].ElementType;
        return !IsUnique(elementType)
            && IsCopyGenericArgumentType(elementType)
            && NativeFixedArrayContract.IsRootlessCopyFieldType(type,
                _structs, _nativeFixedArrayTypes,
                _checkedReferenceTypes);
    }

    private bool IsCopyGenericNominalType(PrimitiveType type)
    {
        if (!BoundGenericNominalTypeTypes.IsType(type))
        {
                return false;
        }

        var index = BoundGenericNominalTypeTypes.Index(type);
        if ((uint)index >= (uint)_genericNominalTypeInstances.Count)
        {
                return false;
        }

        var instance = _genericNominalTypeInstances[index];
        var definition = _genericNominalTypeDefinitions.FirstOrDefault(
                candidate => candidate.Identity == instance.Definition);
        if (definition is null || definition.NominalKind != 2
                || definition.Formals.IsDefault
                || definition.Formals.Length != instance.Arguments.Length
                || BoundGenericNominalTypeContract.IsCoreTaskType(type,
                    _genericNominalTypeInstances, _reference?.CoreTaskType))
        {
                return false;
        }

        for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
        {
                var formal = definition.Formals[ordinal];
                var argument = instance.Arguments[ordinal];
                if (formal.Kind != BoundGenericArgumentKind.Type
                    || formal.Constraints.IsDefault
                    || !formal.Constraints.IsEmpty
                    || formal.OwnershipDomain is not (1 or 2 or 3)
                    || formal.NullabilityDomain is not (1 or 2)
                    || argument.Kind != BoundGenericArgumentKind.Type
                    || argument.Qualifiers != 0
                    || (formal.NullabilityDomain == 1
                        && (ManagedClassTypes.IsOptional(argument.Type)
                            || SourceOptionalTypes.IsOptional(argument.Type,
                                _checkedReferenceTypes)))
                    || (formal.OwnershipDomain == 1
                        && !IsCopyGenericArgumentType(argument.Type))
                    || (formal.OwnershipDomain == 2
                        && !IsUnique(argument.Type))
                    || (formal.OwnershipDomain == 3
                        && !IsUnique(argument.Type)
                        && !IsCopyGenericArgumentType(argument.Type)))
                {
                    return false;
                }
        }

        return true;
    }

    private bool IsSupportedNonBooleanGenericOutputType(PrimitiveType type) =>
        type is PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64
        || (IsStruct(type) && !_structs[(int)type - 100].IsUnique);

    private bool IsCopyGenericArgumentType(PrimitiveType type) =>
        !IsUniqueGenericArgumentType(type)
        && (type is PrimitiveType.Float32 or PrimitiveType.Float64
            || IsCopyGenericType(type));

    private void DeclareProtocols()
    {
        foreach (var tree in _trees)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            foreach (var (sourceTree, node) in _scopedDeclarations.Where(item =>
                item.Tree == tree && item.Declaration.Kind == SyntaxKind.InterfaceDeclaration))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                SetContext(sourceTree, node);
                SyntaxToken? nameToken = null;
                SyntaxNode? parameters = null;
                SyntaxNode? operatorMember = null;
                var operatorCount = 0;
                var hasInvalidInterfaceModifier = false;
                var hasInvalidDeclaration = false;
                foreach (var child in node.Children)
                {
                    if (child is SyntaxTokenElement tokenElement)
                    {
                        nameToken ??= tokenElement.Token.Kind == TokenKind.Identifier ? tokenElement.Token : null;
                        hasInvalidInterfaceModifier |= tokenElement.Token.Kind is TokenKind.PublicKeyword
                            or TokenKind.PrivateKeyword or TokenKind.StaticKeyword or TokenKind.MutKeyword
                            or TokenKind.UniqueKeyword or TokenKind.ExternKeyword;
                    }
                    else if (child is SyntaxNode childNode)
                    {
                        if (childNode.Kind == SyntaxKind.TypeParameterList)
                        {
                            parameters ??= childNode;
                        }
                        else if (childNode.Kind == SyntaxKind.OperatorDeclaration)
                        {
                            operatorCount++;
                            operatorMember ??= childNode;
                        }
                        else
                        {
                            hasInvalidDeclaration = true;
                        }
                    }
                }
                var name = nameToken?.Name ?? throw new InvalidOperationException();
                if (!node.Children.OfType<SyntaxNode>()
                    .Any(static child => child.Kind == SyntaxKind.OperatorDeclaration))
                {
                    continue;
                }
                if (TryPrimitiveType(name, out _))
                {
                    Report("TRM3003", $"The primitive type '{name}' cannot be declared as an interface.", node.Span);
                    continue;
                }
                var qualified = Qualify(name);
                var variables = new string[3];
                var variableCount = 0;
                var formalTokenCount = 0;
                var selfMarkerCount = 0;
                var selfMarkerPosition = -1;
                var hasPrimitiveVariable = false;
                var hasDuplicateVariable = false;
                if (parameters is not null)
                {
                    foreach (var child in parameters.Children)
                    {
                        if (child is not SyntaxTokenElement tokenElement)
                        {
                            continue;
                        }
                        var token = tokenElement.Token;
                        if (token.Kind == TokenKind.Identifier)
                        {
                            if (token.Text == "this")
                            {
                                selfMarkerCount++;
                                if (selfMarkerPosition < 0)
                                {
                                    selfMarkerPosition = formalTokenCount;
                                }
                            }
                            else
                            {
                                var variable = token.Name;
                                hasPrimitiveVariable |= TryPrimitiveType(variable, out _);
                                if (variableCount < variables.Length)
                                {
                                    for (var index = 0; index < variableCount; index++)
                                    {
                                        hasDuplicateVariable |= StringComparer.Ordinal.Equals(
                                            variables[index], variable);
                                    }
                                    variables[variableCount] = variable;
                                }
                                variableCount++;
                            }
                        }
                        formalTokenCount++;
                    }
                }
                var hasSelf = selfMarkerCount == 1 && formalTokenCount > 2 && selfMarkerPosition == 1;
                if (hasPrimitiveVariable)
                {
                    Report("TRM3003", "Primitive type names cannot be used as interface type parameters.",
                        node.Span);
                    continue;
                }
                if (selfMarkerCount > 1 || (selfMarkerCount == 1 && !hasSelf)
                    || variableCount is not (2 or 3) || hasDuplicateVariable
                    || hasInvalidInterfaceModifier)
                {
                    Report("TRM3001", "Local static operator interfaces require two or three distinct type parameters and internal visibility.", node.Span);
                    continue;
                }
                if (operatorCount != 1 || hasInvalidDeclaration || operatorMember is not SyntaxNode member)
                {
                    Report("TRM3001", "A local static operator interface must declare exactly one operator.", node.Span);
                    continue;
                }
                TokenKind? op = null;
                var unsafeModifierCount = 0;
                var staticModifierCount = 0;
                var hasInvalidModifier = false;
                var hasInvalidAttribute = false;
                var hasRetiredNoThrow = false;
                var signatureCount = 0;
                SyntaxNode? firstSignature = null;
                SyntaxNode? formals = null;
                foreach (var child in member.Children)
                {
                    if (child is SyntaxTokenElement tokenElement)
                    {
                        var tokenKind = tokenElement.Token.Kind;
                        if (op is null && (IsStaticBinaryOperator(tokenKind) || IsStaticUnaryOperator(tokenKind)))
                        {
                            op = tokenKind;
                        }
                        if (tokenKind == TokenKind.UnsafeKeyword)
                        {
                            unsafeModifierCount++;
                        }
                        if (tokenKind == TokenKind.StaticKeyword)
                        {
                            staticModifierCount++;
                        }
                        hasInvalidModifier |= tokenKind is TokenKind.PrivateKeyword or TokenKind.PublicKeyword
                            or TokenKind.InternalKeyword or TokenKind.MutKeyword or TokenKind.UniqueKeyword
                            or TokenKind.ExternKeyword;
                    }
                    else if (child is SyntaxNode { Kind: SyntaxKind.Attribute } attribute)
                    {
                        if (NameText(attribute) == "[NoThrow]")
                        {
                            Report("TRM3017", "The general NoThrow source contract is retired.", attribute.Span);
                            hasRetiredNoThrow = true;
                        }
                        else
                        {
                            hasInvalidAttribute = true;
                        }
                    }
                    else if (child is SyntaxNode syntaxNode && syntaxNode.Kind != SyntaxKind.Attribute)
                    {
                        firstSignature ??= syntaxNode;
                        signatureCount++;
                        if (syntaxNode.Kind == SyntaxKind.ParameterList)
                        {
                            formals ??= syntaxNode;
                        }
                    }
                }
                var arity = 0;
                SyntaxNode? firstArgument = null;
                SyntaxNode? secondArgument = null;
                if (formals is not null)
                {
                    foreach (var child in formals.Children)
                    {
                        if (child is not SyntaxNode argument)
                        {
                            continue;
                        }
                        if (arity == 0)
                        {
                            firstArgument = argument;
                        }
                        else if (arity == 1)
                        {
                            secondArgument = argument;
                        }
                        arity++;
                    }
                }
                if (hasRetiredNoThrow)
                {
                    continue;
                }
                if (op is not TokenKind operation
                    || !((arity == 1 && IsStaticUnaryOperator(operation))
                        || (arity == 2 && IsStaticBinaryOperator(operation)))
                    || variableCount != arity + 1 || signatureCount != 2
                    || firstSignature is not SyntaxNode {
                        Kind: SyntaxKind.TypeName or SyntaxKind.ReferenceTypeName
                    } resultType
                    || NameText(resultType) != variables[variableCount - 1]
                    || firstArgument is not { Children: [SyntaxNode first, SyntaxTokenElement] }
                    || NameText(first) != variables[0]
                    || (arity == 2 && (secondArgument is not { Children: [SyntaxNode second, SyntaxTokenElement] }
                        || NameText(second) != variables[1]))
                    || member.Children[^1] is not SyntaxTokenElement { Token.Kind: TokenKind.Semicolon }
                    || staticModifierCount != 1
                    || hasInvalidModifier
                    || hasInvalidAttribute
                    || unsafeModifierCount > 1)
                {
                    Report("TRM3001", "Static operator protocol signatures must match their operand and result formals and end in ';'.", member.Span);
                    continue;
                }
                if (qualified == DirectNativeImportContract.AttributeFullyQualifiedName
                    || !_protocols.TryAdd(qualified, new StaticProtocol(qualified,
                    new StaticOperatorKey(operation, arity), unsafeModifierCount != 0,
                    hasSelf, node, tree)))
                {
                    Report("TRM3003", $"Duplicate static operator interface '{qualified}'.", node.Span);
                }
            }
        }
    }

    private void DeclareConformances()
    {
        foreach (var protocol in _protocols.Values)
        {
            if (_structNames.ContainsKey(protocol.Name))
            {
                SetContext(protocol.Tree, protocol.Declaration);
                Report("TRM3003", $"Protocol and struct cannot share '{protocol.Name}'.", protocol.Declaration.Span);
            }
        }
        foreach (var (index, (tree, declaration)) in _structDeclarations)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SetContext(tree, declaration);
            var typeName = _structs[index].Name;
            var list = declaration.Children.OfType<SyntaxNode>()
                .FirstOrDefault(static child => child.Kind == SyntaxKind.ConformanceList);
            if (list is null)
            {
                continue;
            }
            var simpleTypeName = StructName(declaration).Name;
            var witnesses = new Dictionary<string, PrimitiveType>(StringComparer.Ordinal);
            if (!_structs[index].IsInternal)
            {
                Report("TRM3007", "Static protocol witnesses are compilation-local and require an internal struct.", declaration.Span);
            }
            foreach (var name in list.Children.OfType<SyntaxNode>())
            {
                var shape = TryProtocolApplication(name, out var head, out var parts, out var partNames);
                var protocolName = shape ? FindProtocol(head) : null;
                if (protocolName is null
                    || !MatchesProtocolArguments(_protocols[protocolName], partNames,
                        simpleTypeName, typeName))
                {
                    Report("TRM3007", "Conformance requires a distinct declared local protocol with exact operand and result types.", name.Span);
                    continue;
                }
                var result = BindType(parts[^1], allowVoid: false);
                if (!IsCopyGenericType(result) || IsUnique(result)
                    || (_protocols[protocolName].Key.Operator is TokenKind.PlusPlus or TokenKind.MinusMinus
                        && result != _structNames[typeName])
                    || witnesses.Any(item => _protocols[item.Key].Key == _protocols[protocolName].Key
                        && item.Value != result)
                    || !witnesses.TryAdd(protocolName, result))
                {
                    Report("TRM3007", "Conformance requires a unique Copy result for each operator signature.", name.Span);
                }
            }
            _conformances.Add(typeName, witnesses);
        }
    }

    private void ValidateConformances()
    {
        foreach (var (name, protocols) in _conformances)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var type = _structNames[name];
            foreach (var (protocolName, result) in protocols)
            {
                var protocol = _protocols[protocolName];
                var operation = name + "." + OperatorName(protocol.Key);
                if (!_functions.TryGetValue(operation, out var index))
                {
                    SetContext(protocol.Tree, protocol.Declaration);
                    Report("TRM3007", $"'{name}' promises '{protocolName}' but has no matching operator.", protocol.Declaration.Span);
                    continue;
                }
                var signature = _signatures[index];
                if (signature.ReturnType != result
                    || signature.Parameters.Length != protocol.Key.Arity
                    || signature.Parameters.Any(parameter => parameter.Type != type || parameter.IsOut)
                    || !signature.IsStatic || !signature.IsOperator || signature.IsPrivate || signature.IsConstructor)
                {
                    SetContext(signature.Tree, signature.Declaration);
                    Report("TRM3007", $"'{name}' does not fulfill the static operator contract '{protocolName}'.",
                        signature.Declaration?.Span ?? protocol.Declaration.Span);
                }
            }
        }
    }

    private static ImmutableArray<GenericMethodTypeFormalSyntax>
        ParseGenericMethodTypeFormals(SyntaxNode typeParameters,
            out bool valid)
    {
        var formals = ImmutableArray.CreateBuilder<
            GenericMethodTypeFormalSyntax>();
        var current = new List<SyntaxElement>();
        valid = true;
        foreach (var child in typeParameters.Children)
        {
            if (child is SyntaxTokenElement {
                    Token.Kind: TokenKind.Less or TokenKind.Greater,
                })
            {
                continue;
            }
            if (child is SyntaxTokenElement {
                    Token.Kind: TokenKind.Comma,
                })
            {
                valid &= TryAddFormal();
                current.Clear();
                continue;
            }

            current.Add(child);
        }

        valid &= TryAddFormal();
        return formals.ToImmutable();

        bool TryAddFormal()
        {
            if (current.Count == 1
                && current[0] is SyntaxTokenElement {
                    Token.Kind: TokenKind.Identifier,
                } name)
            {
                formals.Add(new GenericMethodTypeFormalSyntax(
                    name.Token.Name, null));
                return true;
            }
            if (current.Count == 3
                && current[0] is SyntaxTokenElement {
                    Token.Kind: TokenKind.Identifier,
                } defaultedName
                && current[1] is SyntaxTokenElement {
                    Token.Kind: TokenKind.Equals,
                }
                && current[2] is SyntaxNode defaultType
                && IsTypeSyntax(defaultType))
            {
                formals.Add(new GenericMethodTypeFormalSyntax(
                    defaultedName.Token.Name, defaultType));
                return true;
            }

            return false;
        }
    }

    private static ImmutableArray<int> GenericTypeDefaultDependencies(
        SyntaxNode? type, string[] typeNames)
    {
        if (type is null)
        {
            return [];
        }

        var dependencies = new SortedSet<int>();
        var pending = new Stack<SyntaxNode>();
        pending.Push(type);
        while (pending.TryPop(out var node))
        {
            if (node.Kind == SyntaxKind.TypeName
                && !node.Children.OfType<SyntaxNode>().Any()
                && node.Children.OfType<SyntaxTokenElement>()
                    .Where(static element =>
                        element.Token.Kind == TokenKind.Identifier)
                    .ToArray()
                    is [{ } identifier])
            {
                var ordinal = Array.IndexOf(typeNames,
                    identifier.Token.Name);
                if (ordinal >= 0)
                {
                    _ = dependencies.Add(ordinal);
                }
            }

            foreach (var child in node.Children.OfType<SyntaxNode>())
            {
                pending.Push(child);
            }
        }

        return [.. dependencies];
    }

    private static bool ContainsMoveSyntax(SyntaxNode node)
    {
        var pending = new Stack<SyntaxNode>();
        pending.Push(node);
        while (pending.TryPop(out var current))
        {
            if (current.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Kind == TokenKind.MoveKeyword))
            {
                return true;
            }
            foreach (var child in current.Children.OfType<SyntaxNode>())
            {
                pending.Push(child);
            }
        }

        return false;
    }

    private void DeclareGeneric(SyntaxNode declaration, SyntaxNode typeParameters)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var children = declaration.Children.OfType<SyntaxNode>().ToArray();
        var typeFormals = ParseGenericMethodTypeFormals(typeParameters,
            out var typeFormalsValid);
        var typeNames = typeFormals.Select(static formal => formal.Name)
            .ToArray();
        var hasPrimitiveTypeParameter = false;
        var hasDuplicateTypeParameter = false;
        var typeParameterIndices = typeNames.Length is >= 2 and <= 8
            ? new Dictionary<string, int>(StringComparer.Ordinal) : null;
        for (var index = 0; index < typeNames.Length; index++)
        {
            var typeName = typeNames[index];
            hasPrimitiveTypeParameter |= TryPrimitiveType(typeName, out _);
            if (typeParameterIndices is not null)
            {
                hasDuplicateTypeParameter |= !typeParameterIndices.TryAdd(typeName, index);
            }
        }
        if (hasPrimitiveTypeParameter)
        {
            Report("TRM3003", "Primitive type names cannot be used as type parameters.",
                typeParameters.Span);
            return;
        }
        SyntaxNode? returnType = null;
        SyntaxNode? parameters = null;
        SyntaxNode? body = null;
        var hasInvalidAttribute = false;
        var hasRetiredNoThrow = false;
        foreach (var child in children)
        {
            if (returnType is null && IsTypeSyntax(child))
            {
                returnType = child;
            }
            if (parameters is null && child.Kind == SyntaxKind.ParameterList)
            {
                parameters = child;
            }
            if (child.Kind is SyntaxKind.Block or SyntaxKind.ExpressionBody)
            {
                body = child;
            }
            if (child.Kind == SyntaxKind.Attribute)
            {
                if (NameText(child) == "[NoThrow]")
                {
                    Report("TRM3017", "The general NoThrow source contract is retired.", child.Span);
                    hasRetiredNoThrow = true;
                }
                else if (!TryGetDependencyRelation(AttributeName(child),
                    out _))
                {
                    hasInvalidAttribute = true;
                }
            }
        }
        if (hasRetiredNoThrow)
        {
            return;
        }
        var isReferenceReturn = returnType?.Kind
            == SyntaxKind.ReferenceTypeName;
        var genericReturnNode = isReferenceReturn
            ? returnType!.Children.OfType<SyntaxNode>().SingleOrDefault()
            : returnType;
        var returnName = genericReturnNode is null
            ? "" : NameText(genericReturnNode);
        var scalarReturn = isReferenceReturn ? null
            : returnName == "void" ? PrimitiveType.Void
            : GenericScalarType(returnName)
                ?? GenericOutputScalarType(returnName);
        SyntaxToken? nameToken = null;
        var unsafeModifierCount = 0;
        var publicModifierCount = 0;
        var hasInvalidModifier = false;
        foreach (var element in declaration.Children)
        {
            if (element is not SyntaxTokenElement tokenElement)
            {
                continue;
            }
            var token = tokenElement.Token;
            nameToken ??= token.Kind == TokenKind.Identifier ? token : null;
            var tokenKind = token.Kind;
            if (tokenKind == TokenKind.UnsafeKeyword)
            {
                unsafeModifierCount++;
            }
            if (tokenKind == TokenKind.PublicKeyword)
            {
                publicModifierCount++;
            }
            hasInvalidModifier |= tokenKind is TokenKind.PrivateKeyword
                or TokenKind.StaticKeyword or TokenKind.MutKeyword or TokenKind.UniqueKeyword
                or TokenKind.ExternKeyword;
        }
        var qualified = Qualify(nameToken?.Name ?? throw new InvalidOperationException());
        var arguments = parameters?.Children.OfType<SyntaxNode>().ToArray() ?? [];
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        var templateParameters = ImmutableArray.CreateBuilder<GenericParameter>(arguments.Length);
        var operations = typeNames.Select(static _ =>
            ImmutableDictionary.CreateBuilder<StaticOperatorKey, GenericOperatorConstraint>()).ToArray();
        var constrained = new HashSet<string>(StringComparer.Ordinal);
        var blittableRequirements = new bool[typeNames.Length];
        var ownershipDomains = Enumerable.Repeat((byte)1,
            typeNames.Length).ToArray();
        var ownershipSpecified = new bool[typeNames.Length];
        var nullabilityDomains = Enumerable.Repeat((byte)1,
            typeNames.Length).ToArray();
        var nullabilitySpecified = new bool[typeNames.Length];
        var writableRequirements = new bool[typeNames.Length];
        var returnIndex = FindTypeParameter(returnName);
        CheckedReferenceTypeDefinition? referenceReturnPattern = null;
        var hasInvalidReferenceReturnPattern = false;
        if (isReferenceReturn)
        {
            if (genericReturnNode is not { Kind: SyntaxKind.TypeName }
                || returnIndex < 0)
            {
                hasInvalidReferenceReturnPattern = true;
            }
            else
            {
                var returnTypeSyntax = returnType!;
                var isGarbageCollected = returnTypeSyntax.Children
                    .OfType<SyntaxTokenElement>()
                    .Any(static element =>
                        element.Token.Kind == TokenKind.GcKeyword);
                var isInitialization = returnTypeSyntax.Children
                    .TakeWhile(static child => child is not SyntaxNode)
                    .OfType<SyntaxTokenElement>()
                    .Any(static element =>
                        element.Token.Text == "init");
                var isWritable = returnTypeSyntax.Children[0]
                    is SyntaxTokenElement {
                        Token.Kind: TokenKind.WriteKeyword,
                    };
                var isOptional = returnTypeSyntax.Children[^1]
                    is SyntaxTokenElement {
                        Token.Kind: TokenKind.Question,
                    };
                if (isGarbageCollected && isInitialization)
                {
                    hasInvalidReferenceReturnPattern = true;
                }
                else
                {
                    referenceReturnPattern =
                        new CheckedReferenceTypeDefinition(
                            PrimitiveType.Void, isWritable,
                            isGarbageCollected, isInitialization,
                            isOptional);
                }
            }
        }
        var requiresUnsafeCaller = unsafeModifierCount != 0;
        var typeDefaultsAreOrdered = typeFormals.Select((formal, ordinal) =>
            GenericTypeDefaultDependencies(formal.TypeDefault, typeNames)
                .All(dependency => dependency < ordinal))
            .All(static isValid => isValid);
        var valid = typeNames.Length is >= 1 and <= 8
            && typeFormalsValid
            && !hasDuplicateTypeParameter
            && typeDefaultsAreOrdered
            && returnType is not null && body is not null
            && (returnIndex >= 0 || scalarReturn is not null)
            && arguments.Length is >= 1 and <= 8
            && unsafeModifierCount <= 1
            && publicModifierCount <= 1
            && !hasInvalidModifier
            && !hasInvalidAttribute
            && !hasInvalidReferenceReturnPattern;
        foreach (var argument in arguments)
        {
            var isOut = argument.Children is [SyntaxTokenElement { Token.Kind: TokenKind.OutKeyword },
                SyntaxNode, SyntaxTokenElement];
            var isInit = argument.Children is [SyntaxTokenElement { Token.Text: "init" },
                SyntaxTokenElement { Token.Kind: TokenKind.RefKeyword },
                SyntaxTokenElement { Token.Kind: TokenKind.Less }, SyntaxNode,
                SyntaxTokenElement { Token.Kind: TokenKind.Greater }, SyntaxTokenElement];
            var argumentType = isInit ? argument.Children[3] as SyntaxNode
                : argument.Children.Length >= 2 ? argument.Children[^2] as SyntaxNode : null;
            var referenceTypeArgument = argumentType?.Kind
                == SyntaxKind.ReferenceTypeName;
            var genericTypeNode = referenceTypeArgument
                ? argumentType!.Children.OfType<SyntaxNode>().SingleOrDefault()
                : argumentType;
            if (argument.Kind != SyntaxKind.Parameter
                || (argument.Children is not [SyntaxNode, SyntaxTokenElement] && !isOut && !isInit)
                || genericTypeNode is null
                || genericTypeNode.Kind != SyntaxKind.TypeName
                || argument.Children[^1] is not SyntaxTokenElement argumentName
                || !parameterNames.Add(argumentName.Token.Name))
            {
                valid = false;
            }
            else
            {
                var typeName = NameText(genericTypeNode);
                var index = FindTypeParameter(typeName);
                CheckedReferenceTypeDefinition? referencePattern = null;
                PrimitiveType? scalar = null;
                if (referenceTypeArgument)
                {
                    if (index < 0 || isOut || isInit)
                    {
                        valid = false;
                    }
                    else
                    {
                        var isGarbageCollected = argumentType!.Children
                            .OfType<SyntaxTokenElement>()
                            .Any(static element =>
                                element.Token.Kind == TokenKind.GcKeyword);
                        var isInitialization = argumentType.Children
                            .TakeWhile(static child => child is not SyntaxNode)
                            .OfType<SyntaxTokenElement>()
                            .Any(static element =>
                                element.Token.Text == "init");
                        var isWritable = argumentType.Children[0]
                            is SyntaxTokenElement {
                                Token.Kind: TokenKind.WriteKeyword,
                            };
                        var isOptional = argumentType.Children[^1]
                            is SyntaxTokenElement {
                                Token.Kind: TokenKind.Question,
                            };
                        if (isGarbageCollected && isInitialization)
                        {
                            valid = false;
                        }
                        referencePattern =
                            new CheckedReferenceTypeDefinition(
                                PrimitiveType.Void, isWritable,
                                isGarbageCollected, isInitialization,
                                isOptional);
                    }
                }
                else
                {
                    scalar = GenericScalarType(typeName)
                        ?? ((isOut || isInit)
                            ? GenericOutputScalarType(typeName) : null);
                }
                if (index < 0 && scalar is null)
                {
                    valid = false;
                }
                templateParameters.Add(new GenericParameter(argumentName.Token.Name,
                    index < 0 ? scalar : null, index, isOut || isInit,
                    isInit) {
                    ReferenceTypePattern = referencePattern,
                });
            }
        }
        foreach (var constraint in children.Where(static child => child.Kind == SyntaxKind.ConstraintClause))
        {
            if (constraint.Children[1] is not SyntaxTokenElement formal
                || !constrained.Add(formal.Token.Name))
            {
                valid = false;
                continue;
            }
            var index = FindTypeParameter(formal.Token.Name);
            if (index < 0)
            {
                valid = false;
                continue;
            }
            foreach (var item in constraint.Children.OfType<SyntaxNode>())
            {
                if (item.Kind == SyntaxKind.Attribute)
                {
                    var registeredBlittable = _layoutCoreRegistration
                        is LayoutCoreRegistrationSet layoutRegistration
                        && IsRegisteredLayoutAttribute(item,
                            layoutRegistration.Blittable);
                    if (!registeredBlittable
                        || !HasNoAttributeArguments(item)
                        || blittableRequirements[index])
                    {
                        valid = false;
                    }
                    else
                    {
                        blittableRequirements[index] = true;
                    }
                    continue;
                }
                if (item.Kind == SyntaxKind.ConstraintTerm)
                {
                    var constraintTokens = item.Children
                        .OfType<SyntaxTokenElement>().ToArray();
                    if (constraintTokens.Length == 1
                        && constraintTokens[0].Token.Kind
                            == TokenKind.WriteKeyword
                        && !writableRequirements[index])
                    {
                        writableRequirements[index] = true;
                    }
                    else if (constraintTokens.Length is 1 or 2
                        && constraintTokens[0].Token.Kind
                            == TokenKind.UniqueKeyword
                        && (constraintTokens.Length == 1
                            || constraintTokens[1].Token.Kind
                                == TokenKind.Question)
                        && !ownershipSpecified[index])
                    {
                        ownershipDomains[index]
                            = constraintTokens.Length == 1 ? (byte)2 : (byte)3;
                        ownershipSpecified[index] = true;
                    }
                    else if (constraintTokens.Length == 2
                        && constraintTokens[0].Token.Kind
                            == TokenKind.NullKeyword
                        && constraintTokens[1].Token.Kind
                            == TokenKind.Question
                        && !nullabilitySpecified[index])
                    {
                        nullabilityDomains[index] = 2;
                        nullabilitySpecified[index] = true;
                    }
                    else
                    {
                        valid = false;
                    }
                    continue;
                }
                var shape = TryProtocolApplication(item, out var head, out var parts, out var partNames);
                var protocol = shape ? FindProtocol(head) : null;
                if (protocol is null || !MatchesProtocolArguments(_protocols[protocol], partNames,
                        formal.Token.Name)
                    || operations[index].ContainsKey(_protocols[protocol].Key))
                {
                    valid = false;
                }
                else
                {
                    var resultName = partNames[^1];
                    var resultIndex = FindTypeParameter(resultName);
                    var fixedResult = resultIndex >= 0 ? null : GenericScalarType(resultName);
                    if (resultIndex < 0 && fixedResult is null)
                    {
                        fixedResult = BindType(parts[^1], allowVoid: false);
                    }
                    if (fixedResult is PrimitiveType concrete
                        && (!IsCopyGenericType(concrete) || IsUnique(concrete)))
                    {
                        valid = false;
                    }
                    if ((_protocols[protocol].Key.Operator is TokenKind.PlusPlus or TokenKind.MinusMinus)
                        && (resultIndex != index || fixedResult is not null))
                    {
                        valid = false;
                    }
                    operations[index].Add(_protocols[protocol].Key,
                        new GenericOperatorConstraint(protocol, fixedResult, resultIndex));
                }
            }
        }
        var outputParameters = templateParameters.Where(static parameter => parameter.IsOut).ToArray();
        if (body is null
            || (scalarReturn == PrimitiveType.Void
                && !outputParameters.Any(static parameter => parameter.TypeParameterIndex >= 0))
            || (scalarReturn is PrimitiveType.Float32 or PrimitiveType.Float64
                && outputParameters.Length == 0)
            || (outputParameters.Length != 0
                && scalarReturn != PrimitiveType.Bool
                && (scalarReturn is null
                    || !outputParameters.Any(static parameter => parameter.TypeParameterIndex >= 0)
                    || outputParameters.Any(parameter => parameter.TypeParameterIndex < 0
                        && (parameter.ScalarType is not PrimitiveType scalarType
                            || !IsSupportedNonBooleanGenericOutputType(scalarType)))))
            )
        {
            valid = false;
        }
        if (!valid || _templates.Count >= MaximumStaticSpecializations)
        {
            Report("TRM3007",
                "Generic functions require distinct default-domain type formals, supported value and output modes, and explicit constraints for any operators.",
                declaration.Span);
            return;
        }
        if (!_templates.TryAdd(qualified, new GenericTemplate(qualified,
            [.. typeFormals.Select((formal, index) =>
                new GenericTypeParameter(formal.Name,
                    operations[index].ToImmutable()) {
                    OwnershipDomain = ownershipDomains[index],
                    NullabilityDomain = nullabilityDomains[index],
                    RequiresWritable = writableRequirements[index],
                    TypeDefaultSyntax = formal.TypeDefault,
                    RequiresBlittable = blittableRequirements[index],
                    DefaultTypeParameterDependencies =
                        GenericTypeDefaultDependencies(
                            formal.TypeDefault, typeNames),
                })],
            scalarReturn, returnIndex, templateParameters.ToImmutable(), body!, declaration, _tree) {
            IsPublic = publicModifierCount != 0,
            RequiresUnsafeCaller = requiresUnsafeCaller,
            ReferenceReturnTypePattern = referenceReturnPattern,
        }))
        {
            Report("TRM3003", $"Duplicate generic function '{qualified}'.", declaration.Span);
        }

        int FindTypeParameter(string name)
        {
            if (typeParameterIndices is not null)
            {
                return typeParameterIndices.TryGetValue(name, out var index) ? index : -1;
            }
            return Array.IndexOf(typeNames, name);
        }
    }

    private PrimitiveType? GenericCallType(SyntaxNode callee, SyntaxNode? callArguments = null)
    {
        var name = ((SyntaxTokenElement)callee.Children[0]).Token.Name;
        if (TryFindVariable(name, out _) || FindGenericTemplate(name) is not GenericTemplate template)
        {
            return null;
        }
        if (callee.Kind == SyntaxKind.GenericNameExpression)
        {
            if (!TryReadGenericMethodTypeArguments(callee, template,
                    out var suppliedTypes, out var requireInference,
                    out var explicitDefaults))
            {
                return null;
            }
            var completedTypes = callArguments is SyntaxNode callArgumentList
                ? InferGenericTypes(template, callArgumentList, null, out _,
                    suppliedTypes, requireInference, explicitDefaults)
                : CompleteGenericTypeArguments(template, suppliedTypes,
                    requireInference, explicitDefaults);
            return completedTypes
                is ImmutableArray<PrimitiveType> resolvedTypeArguments
                    ? ResolveGenericReturnType(template,
                        resolvedTypeArguments, callee.Span)
                    : null;
        }
        if (template.ScalarReturnType is PrimitiveType scalar)
        {
            return scalar;
        }
        var inferred = callArguments is null ? null
            : InferGenericTypes(template, callArguments, null, out _);
        return inferred is ImmutableArray<PrimitiveType> types
                ? ResolveGenericReturnType(template, types, callee.Span)
                : null;
    }

    private string? GenericMethodTypeArgumentConstraintFailure(
        GenericTemplate template, int ordinal, PrimitiveType type)
    {
        var formal = template.TypeParameters[ordinal];
        if (_genericProofFunctions is not null
            && _genericContext is GenericInstantiation enclosing)
        {
            for (var callerOrdinal = 0;
                callerOrdinal < enclosing.Types.Length; callerOrdinal++)
            {
                if (enclosing.Types[callerOrdinal] != type)
                {
                    continue;
                }

                var callerFormal
                    = enclosing.Template.TypeParameters[callerOrdinal];
                if ((formal.OwnershipDomain == 1
                        && callerFormal.OwnershipDomain != 1)
                    || (formal.OwnershipDomain == 2
                        && callerFormal.OwnershipDomain != 2))
                {
                    return "ownership domain";
                }
                if (formal.NullabilityDomain == 1
                    && callerFormal.NullabilityDomain == 2
                    && !callerFormal.RequiresBlittable)
                {
                    return "nullability domain";
                }
                if (formal.RequiresBlittable
                    && !callerFormal.RequiresBlittable)
                {
                    return "Blittable constraint";
                }
                if (formal.RequiresWritable
                    && !callerFormal.RequiresWritable)
                {
                    return "write requirement";
                }
                return null;
            }
        }

        var isUnique = IsUniqueGenericArgumentType(type);
        var isCopy = IsCopyGenericArgumentType(type)
            || BoundEnumTypes.IsType(type);
        if ((formal.OwnershipDomain == 1
                && (!isCopy || isUnique))
            || (formal.OwnershipDomain == 2 && !isUnique)
            || (formal.OwnershipDomain == 3 && !isCopy && !isUnique))
        {
            return "ownership domain";
        }
        if (formal.NullabilityDomain == 1
            && IsSupportedOptionalType(type))
        {
            return "nullability domain";
        }
        if (formal.RequiresBlittable
            && !IsBlittableGenericArgumentType(type, []))
        {
            return "Blittable constraint";
        }
        if (formal.RequiresWritable
            && !IsWritableGenericMethodTypeArgument(type))
        {
            return "write requirement";
        }

        return null;
    }

    private bool IsWritableGenericMethodTypeArgument(PrimitiveType type)
    {
        if (ManagedClassTypes.IsManaged(type)
            && ManagedClassTypes.IsWritable(type))
        {
            return true;
        }
        if (CheckedReferenceTypeContract.IsValidType(type,
                _checkedReferenceTypes)
            && _checkedReferenceTypes[CheckedReferenceTypes.Index(type)]
                .IsWritable)
        {
            return true;
        }
        if (NativePointerTypeContract.IsValidType(type,
                [.. _nativePointerTypes])
            && _nativePointerTypes[NativePointerTypes.Index(type)]
                .IsWritable)
        {
            return true;
        }
        return IsSupportedOptionalType(type)
            && TryGetRequiredOptionalType(type, out var payloadType)
            && IsWritableGenericMethodTypeArgument(payloadType);
    }

    private bool TryReadGenericMethodTypeArguments(SyntaxNode callee,
        GenericTemplate template, out PrimitiveType?[] suppliedTypes,
        out bool[] requireInference, out bool[] explicitDefaults)
    {
        suppliedTypes = new PrimitiveType?[template.TypeParameters.Length];
        requireInference = new bool[template.TypeParameters.Length];
        explicitDefaults = new bool[template.TypeParameters.Length];
        if (callee.Kind != SyntaxKind.GenericNameExpression
            || callee.Children.Length < 2
            || callee.Children[1] is not SyntaxNode arguments)
        {
            return false;
        }

        var typeArguments = arguments.Children.OfType<SyntaxNode>().ToArray();
        if (typeArguments.Length > template.TypeParameters.Length)
        {
            return false;
        }
        for (var ordinal = 0; ordinal < typeArguments.Length; ordinal++)
        {
            var argument = typeArguments[ordinal];
            if (argument.Kind == SyntaxKind.GenericInferenceArgument)
            {
                requireInference[ordinal] = true;
            }
            else if (argument.Kind == SyntaxKind.GenericDefaultArgument)
            {
                explicitDefaults[ordinal] = true;
            }
            else if (TryResolveGenericArgument(argument,
                out var typeArgument))
            {
                suppliedTypes[ordinal] = typeArgument;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private ImmutableArray<PrimitiveType>? InferGenericTypes(GenericTemplate template, SyntaxNode arguments,
        PrimitiveType? expected, out bool conflictingArguments,
        PrimitiveType?[]? explicitTypeArguments = null,
        bool[]? requireInference = null,
        bool[]? explicitDefaults = null)
    {
        conflictingArguments = false;
        var typeParameterCount = template.TypeParameters.Length;
        if ((explicitTypeArguments is not null
                && explicitTypeArguments.Length != typeParameterCount)
            || (requireInference is not null
                && requireInference.Length != typeParameterCount)
            || (explicitDefaults is not null
                && explicitDefaults.Length != typeParameterCount))
        {
            return null;
        }

        var inferred = new PrimitiveType?[typeParameterCount];
        if (explicitTypeArguments is not null)
        {
            Array.Copy(explicitTypeArguments, inferred,
                typeParameterCount);
        }
        var supplied = arguments.Children.OfType<SyntaxNode>().ToArray();
        for (var index = 0; index < Math.Min(supplied.Length, template.Parameters.Length); index++)
        {
            var parameterIndex = template.Parameters[index].TypeParameterIndex;
            if (parameterIndex < 0
                || (explicitTypeArguments is not null
                    && (inferred[parameterIndex] is not null
                        || (explicitDefaults is not null
                            && explicitDefaults[parameterIndex]))))
            {
                continue;
            }
            var argument = supplied[index];
            PrimitiveType? candidate = null;
            if (argument.Children is [SyntaxNode value])
            {
                candidate = InferGenericParameterType(
                    template.Parameters[index], KnownType(value));
            }
            else if (template.Parameters[index] is { IsOut: true, IsInit: false }
                && argument.Children is [SyntaxTokenElement { Token.Kind: TokenKind.OutKeyword },
                    SyntaxNode { Kind: SyntaxKind.NameExpression } output])
            {
                candidate = KnownType(output);
            }
            else if (template.Parameters[index] is { IsInit: true }
                && argument.Children is [SyntaxTokenElement { Token.Text: "init" },
                    SyntaxNode { Kind: SyntaxKind.NameExpression } initOutput])
            {
                candidate = KnownType(initOutput);
            }
            if (candidate is not PrimitiveType typed)
            {
                continue;
            }
            if (inferred[parameterIndex] is PrimitiveType established && established != typed)
            {
                conflictingArguments = true;
                return null;
            }
            inferred[parameterIndex] = typed;
        }
        if (template.ScalarReturnType is null
            && expected is PrimitiveType result
            && (explicitDefaults is null
                || !explicitDefaults[template.ReturnTypeParameterIndex]))
        {
            var index = template.ReturnTypeParameterIndex;
            var referenceReturnPattern
                = template.ReferenceReturnTypePattern;
            var expectedTypeArgument = referenceReturnPattern
                is CheckedReferenceTypeDefinition expectedReference
                    ? InferGenericReferenceType(expectedReference, result)
                    : result;
            if (expectedTypeArgument is not PrimitiveType inferredResult)
            {
                conflictingArguments = true;
                return null;
            }
            if (inferred[index] is PrimitiveType established)
            {
                var compatible = referenceReturnPattern is null
                    ? CanImplicitlyConvert(established, inferredResult,
                        arguments.Span)
                    : established == inferredResult;
                if (!compatible)
                {
                    conflictingArguments = true;
                    return null;
                }
            }
            else
            {
                inferred[index] = inferredResult;
            }
        }
        return CompleteGenericTypeArguments(template, inferred,
            requireInference, explicitDefaults);
    }

    private ImmutableArray<PrimitiveType>? CompleteGenericTypeArguments(
        GenericTemplate template, PrimitiveType?[] suppliedTypes,
        bool[]? requireInference = null,
        bool[]? explicitDefaults = null)
    {
        if (suppliedTypes.Length != template.TypeParameters.Length)
        {
            return null;
        }
        if ((requireInference is not null
                && requireInference.Length != suppliedTypes.Length)
            || (explicitDefaults is not null
                && explicitDefaults.Length != suppliedTypes.Length))
        {
            return null;
        }

        var completed = new PrimitiveType[suppliedTypes.Length];
        for (var ordinal = 0; ordinal < suppliedTypes.Length; ordinal++)
        {
            var selectDefault = explicitDefaults is not null
                && explicitDefaults[ordinal];
            if (!selectDefault
                && suppliedTypes[ordinal] is PrimitiveType supplied)
            {
                completed[ordinal] = supplied;
                continue;
            }
            if (requireInference is not null
                && requireInference[ordinal])
            {
                return null;
            }

            var formal = template.TypeParameters[ordinal];
            if (formal.TypeDefaultSyntax is null
                || formal.DefaultTypeParameterDependencies.Any(
                    dependency => dependency < 0 || dependency >= ordinal))
            {
                return null;
            }

            var typeArguments = completed.Select((type, index) =>
                index < ordinal ? type : PrimitiveType.Void)
                .ToImmutableArray();
            if (BindGenericMethodTypeDefault(template, ordinal,
                    typeArguments) is not PrimitiveType defaultType)
            {
                return null;
            }

            completed[ordinal] = defaultType;
        }

        return [.. completed];
    }

    private PrimitiveType? BindGenericMethodTypeDefault(
        GenericTemplate template, int ordinal,
        ImmutableArray<PrimitiveType> typeArguments)
    {
        if ((uint)ordinal >= (uint)template.TypeParameters.Length
            || typeArguments.Length != template.TypeParameters.Length
            || template.TypeParameters[ordinal].TypeDefaultSyntax
                is not SyntaxNode defaultType)
        {
            return null;
        }

        var previousContext = CaptureContext();
        var previousGenericContext = _genericContext;
        var diagnosticCount = DiagnosticCount;
        try
        {
            SetContext(template.Tree, defaultType);
            _genericContext = new GenericInstantiation(template,
                typeArguments);
            var result = BindType(defaultType, allowVoid: false);
            return DiagnosticCount == diagnosticCount
                && GenericMethodTypeArgumentConstraintFailure(template,
                    ordinal, result) is null ? result : null;
        }
        finally
        {
            _genericContext = previousGenericContext;
            RestoreContext(previousContext);
        }
    }

    private BoundGenericMethodTypeTerm? BindGenericMethodTypeTerm(
        SyntaxNode type, GenericTemplate template, bool applyOptional = true)
    {
        if (type.Kind == SyntaxKind.AttributedTypeName)
        {
            if (type.Children.Length is not (2 or 3)
                || type.Children is not [
                    SyntaxNode attribute,
                    SyntaxNode functionPointerSyntax,
                    ..
                ]
                || attribute.Kind != SyntaxKind.Attribute
                || functionPointerSyntax.Kind != SyntaxKind.TypeName
                || TypeNameIdentifier(functionPointerSyntax) != "fnptr"
                || !TryBindNativeCallConvAttribute(attribute,
                    out var callingConvention,
                    out var conventionExpression))
            {
                return null;
            }

            var hasOptional = type.Children.Length == 3
                || functionPointerSyntax.Children[^1]
                    is SyntaxTokenElement {
                        Token.Kind: TokenKind.Question,
                    };
            var functionPointerTerm = BindGenericMethodTypeTerm(
                functionPointerSyntax, template, applyOptional: false);
            if (functionPointerTerm is not BoundGenericMethodTypeTerm term)
            {
                return null;
            }
            if (GenericMethodTypeTermDependencies(term).IsEmpty)
            {
                return BindConcreteGenericMethodTypeTerm(type, template);
            }
            if (conventionExpression is not null
                || SetGenericMethodFunctionPointerConvention(term,
                    callingConvention) is not BoundGenericMethodTypeTerm
                    converted)
            {
                return null;
            }

            return hasOptional
                ? new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.OptionalValue,
                    PrimitiveType.Void, -1, -1, [converted],
                    false, false, false, false)
                : converted;
        }

        if (type.Kind == SyntaxKind.ReferenceTypeName)
        {
            var referents = type.Children.OfType<SyntaxNode>().ToArray();
            if (referents.Length != 1
                || BindGenericMethodTypeTerm(referents[0], template)
                    is not BoundGenericMethodTypeTerm referent)
            {
                return null;
            }
            if (type.Children.OfType<SyntaxTokenElement>()
                .Any(static token =>
                    token.Token.Kind == TokenKind.UniqueKeyword))
            {
                return null;
            }

            var isWritable = type.Children[0] is SyntaxTokenElement {
                Token.Kind: TokenKind.WriteKeyword,
            };
            var isGarbageCollected = type.Children
                .OfType<SyntaxTokenElement>()
                .Any(static token =>
                    token.Token.Kind == TokenKind.GcKeyword);
            var isInitialization = type.Children
                .OfType<SyntaxTokenElement>()
                .Any(static token =>
                    token.Token.Kind == TokenKind.Identifier
                    && token.Token.Text == "init");
            var isOptional = type.Children[^1]
                is SyntaxTokenElement {
                    Token.Kind: TokenKind.Question,
                };
            if (isGarbageCollected && isInitialization)
            {
                return null;
            }

            return new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.CheckedReference,
                PrimitiveType.Void, -1, -1, [referent], isWritable,
                isGarbageCollected, isInitialization, isOptional);
        }

        if (type.Kind == SyntaxKind.FixedArrayTypeName)
        {
            if (!NativeFixedArrayContract.SupportsRuntimePolicy(
                _runtimePolicy))
            {
                Report("TRM3030",
                    "Fixed-array storage is not supported under the selected runtime policy.",
                    type.Span);
                return null;
            }
            if (type.Children is not [
                    SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
                    SyntaxNode elementSyntax,
                    SyntaxTokenElement { Token.Kind: TokenKind.Semicolon },
                    SyntaxNode extentSyntax,
                    SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket },
                ]
                || BindGenericMethodTypeTerm(elementSyntax, template)
                    is not BoundGenericMethodTypeTerm elementTerm
                || !TryBindGenericMethodFixedArrayExtent(extentSyntax,
                    out var elementCount))
            {
                return null;
            }

            if (GenericMethodTypeTermDependencies(elementTerm).IsEmpty)
            {
                return BindConcreteGenericMethodTypeTerm(type, template);
            }

            return new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.NativeFixedArray,
                PrimitiveType.Void, -1, -1, [elementTerm],
                false, false, false, false) {
                ElementCount = elementCount,
            };
        }

        if (type.Kind != SyntaxKind.TypeName)
        {
            return GenericTypeDefaultDependencies(type,
                    [.. template.TypeParameters.Select(static formal =>
                        formal.Name)]).IsEmpty
                ? BindConcreteGenericMethodTypeTerm(type, template)
                : null;
        }

        var typeArguments = type.Children.OfType<SyntaxNode>().ToArray();
        var name = TypeNameIdentifier(type);
        var isWritableType = type.Children[0]
            is SyntaxTokenElement {
                Token.Kind: TokenKind.WriteKeyword,
            };
        var isGarbageCollectedType = type.Children
            .OfType<SyntaxTokenElement>()
            .Any(static token =>
                token.Token.Kind == TokenKind.GcKeyword);
        var isUniqueType = type.Children.OfType<SyntaxTokenElement>()
            .Any(static token =>
                token.Token.Kind == TokenKind.UniqueKeyword);
        var isOptionalType = type.Children[^1]
            is SyntaxTokenElement {
                Token.Kind: TokenKind.Question,
            } && applyOptional;
        if (isUniqueType)
        {
            return null;
        }
        if (applyOptional && isOptionalType)
        {
            var payload = BindGenericMethodTypeTerm(type, template,
                applyOptional: false);
            if (payload is not BoundGenericMethodTypeTerm optionalPayload)
            {
                return null;
            }
            return optionalPayload.Kind
                    == BoundGenericMethodTypeTermKind.Concrete
                ? optionalPayload
                : new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.OptionalValue,
                    PrimitiveType.Void, -1, -1, [optionalPayload],
                    false, false, false, false);
        }

        if (name == "option" && typeArguments.Length == 1)
        {
            var payload = BindGenericMethodTypeTerm(typeArguments[0],
                template);
            return payload is BoundGenericMethodTypeTerm optionalPayload
                ? new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.OptionalValue,
                    PrimitiveType.Void, -1, -1, [optionalPayload],
                    false, false, false, false)
                : null;
        }

        if (name == "ptr" && typeArguments.Length == 1)
        {
            if (isGarbageCollectedType)
            {
                return null;
            }
            if (TypeNameIdentifier(typeArguments[0]) == "void"
                && !typeArguments[0].Children.OfType<SyntaxNode>().Any())
            {
                return BindConcreteGenericMethodTypeTerm(type, template);
            }

            var pointee = BindGenericMethodTypeTerm(typeArguments[0],
                template);
            if (pointee is not BoundGenericMethodTypeTerm pointeeTerm)
            {
                return null;
            }
            if (GenericMethodTypeTermDependencies(pointeeTerm).IsEmpty)
            {
                return BindConcreteGenericMethodTypeTerm(type, template);
            }

            return new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.NativePointer,
                PrimitiveType.Void, -1, -1, [pointeeTerm],
                isWritableType, false, false, false);
        }

        if (name == "fnptr" && typeArguments.Length != 0)
        {
            if (isWritableType || isGarbageCollectedType)
            {
                return null;
            }

            var functionTypeArguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeTerm>(typeArguments.Length);
            for (var index = 0; index < typeArguments.Length; index++)
            {
                var argument = typeArguments[index];
                if (index == typeArguments.Length - 1
                    && TypeNameIdentifier(argument) == "void"
                    && !argument.Children.OfType<SyntaxNode>().Any())
                {
                    functionTypeArguments.Add(
                        new BoundGenericMethodTypeTerm(
                            BoundGenericMethodTypeTermKind.Concrete,
                            PrimitiveType.Void, -1, -1, [],
                            false, false, false, false) {
                            AllowsVoid = true,
                        });
                    continue;
                }

                if (BindGenericMethodTypeTerm(argument, template)
                    is not BoundGenericMethodTypeTerm functionTypeArgument)
                {
                    return null;
                }
                functionTypeArguments.Add(functionTypeArgument);
            }

            var arguments = functionTypeArguments.MoveToImmutable();
            if (arguments.SelectMany(static argument =>
                    GenericMethodTypeTermDependencies(argument))
                .Distinct().Any())
            {
                return new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.NativeFunctionPointer,
                    PrimitiveType.Void, -1, -1, arguments,
                    false, false, false, false) {
                    CallingConvention = NativeCallingConvention.Native,
                    ParameterCount = typeArguments.Length - 1,
                };
            }

            return BindConcreteGenericMethodTypeTerm(type, template);
        }

        var formalOrdinal = template.ParameterIndex(name);
        if (formalOrdinal >= 0)
        {
            if (typeArguments.Length != 0)
            {
                return null;
            }

            return new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Formal,
                PrimitiveType.Void, formalOrdinal, -1, [],
                isWritableType, isGarbageCollectedType, false, false);
        }

        var hasSourceGenericTemplate =
            TryFindGenericStructTemplateForTypeTerm(name,
                out var genericStructTemplate)
            && typeArguments.Length != 0;
        var hasImportedGenericDefinition =
            TryFindGenericNominalTypeDefinitionForTypeTerm(name,
                out var genericNominalDefinition)
            && typeArguments.Length != 0;
        if (hasSourceGenericTemplate && hasImportedGenericDefinition)
        {
            return null;
        }
        if (hasSourceGenericTemplate)
        {
            var definitionIndex = genericStructTemplate.DefinitionIndex;
            if ((uint)definitionIndex
                    >= (uint)_sourceGenericTypeDefinitions.Count
                || _sourceGenericTypeDefinitions[definitionIndex]
                    .Formals.Length != typeArguments.Length)
            {
                return null;
            }

            var arguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeTerm>(typeArguments.Length);
            foreach (var argument in typeArguments)
            {
                if (BindGenericMethodTypeTerm(argument, template)
                    is not BoundGenericMethodTypeTerm boundArgument)
                {
                    return null;
                }
                arguments.Add(boundArgument);
            }

            return new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.SourceGeneric,
                PrimitiveType.Void, -1, definitionIndex,
                arguments.MoveToImmutable(), isWritableType,
                isGarbageCollectedType, false, false);
        }
        if (hasImportedGenericDefinition)
        {
            var definition = genericNominalDefinition;
            if (definition.NominalKind != 2
                || definition.Formals.Length != typeArguments.Length
                || definition.Formals.Any(static formal =>
                    formal.Kind != BoundGenericArgumentKind.Type
                    || formal.Constraints.IsDefault
                    || !formal.Constraints.IsEmpty
                    || formal.OwnershipDomain is not (1 or 2 or 3)
                    || formal.OwnershipDomain == 2
                    || formal.NullabilityDomain is not (1 or 2)))
            {
                return null;
            }

            var arguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeTerm>(typeArguments.Length);
            foreach (var argument in typeArguments)
            {
                if (BindGenericMethodTypeTerm(argument, template)
                    is not BoundGenericMethodTypeTerm boundArgument)
                {
                    return null;
                }
                arguments.Add(boundArgument);
            }

            return new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.ImportedGeneric,
                PrimitiveType.Void, -1, -1,
                arguments.MoveToImmutable(), isWritableType,
                isGarbageCollectedType, false, false) {
                NominalTypeIdentity = definition.Identity,
            };
        }

        var formalNames = template.TypeParameters
            .Select(static formal => formal.Name).ToArray();
        if (!GenericTypeDefaultDependencies(type, formalNames).IsEmpty)
        {
            return null;
        }

        return BindConcreteGenericMethodTypeTerm(type, template);
    }

    private static BoundGenericMethodTypeTerm?
        SetGenericMethodFunctionPointerConvention(
            BoundGenericMethodTypeTerm term,
            NativeCallingConvention callingConvention)
    {
        if (term.Kind
            == BoundGenericMethodTypeTermKind.NativeFunctionPointer)
        {
            return term with {
                CallingConvention = callingConvention,
            };
        }
        if (term.Kind == BoundGenericMethodTypeTermKind.OptionalValue
            && term.TypeArguments.Length == 1
            && SetGenericMethodFunctionPointerConvention(
                term.TypeArguments[0], callingConvention)
                is BoundGenericMethodTypeTerm functionPointer)
        {
            return term with {
                TypeArguments = [functionPointer],
            };
        }

        return null;
    }

    private bool TryBindGenericMethodFixedArrayExtent(
        SyntaxNode extentNode, out ulong elementCount)
    {
        elementCount = 0;
        var diagnosticCount = DiagnosticCount;
        var extentExpression = BindExpression(extentNode,
            PrimitiveType.UInt64);
        var boundExtent = FoldFixedArrayExtentExpression(extentExpression);
        if (boundExtent is not BoundConstant
            && extentExpression.Type == PrimitiveType.UInt64
            && CheckConstExpression(extentExpression, out _)
            && _boundConstEvalFunctions is { } constEvalFunctions)
        {
            try
            {
                boundExtent = EvaluateConstExpression(extentExpression,
                    ConstSlots(_constScalarValues, _variables.Count),
                    constEvalFunctions, 0,
                    new ConstEvalState(_cancellationToken));
            }
            catch (ConstEvalFailure failure)
            {
                Report("TRM3030",
                    $"A fixed-array extent failed required constant evaluation: {failure.Message}",
                    extentNode.Span);
            }
        }

        if (DiagnosticCount != diagnosticCount
            || boundExtent is not BoundConstant {
                Type: PrimitiveType.UInt64,
            } constant
            || constant.Bits.High != 0)
        {
            Report("TRM3030",
                "A fixed-array extent must be an exact closed uint64 value.",
                extentNode.Span);
            return false;
        }

        elementCount = constant.Bits.Low;
        return true;
    }

    private BoundGenericMethodTypeTerm? BindConcreteGenericMethodTypeTerm(
        SyntaxNode type, GenericTemplate template)
    {
        var previousContext = CaptureContext();
        var previousGenericContext = _genericContext;
        var diagnosticCount = DiagnosticCount;
        try
        {
            SetContext(template.Tree, type);
            _genericContext = null;
            var concreteType = BindType(type, allowVoid: false);
            return DiagnosticCount == diagnosticCount
                && (IsCopyGenericArgumentType(concreteType)
                    || IsUniqueGenericArgumentType(concreteType)
                    || BoundEnumTypes.IsType(concreteType))
                ? new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Concrete,
                    concreteType, -1, -1, [], false, false, false, false)
                : null;
        }
        finally
        {
            _genericContext = previousGenericContext;
            RestoreContext(previousContext);
        }
    }

    private bool TryFindGenericStructTemplateForTypeTerm(string name,
        out GenericStructTemplate template)
    {
        var candidates = new List<GenericStructTemplate>();

        void Add(string candidateName)
        {
            if (_genericStructTemplates.TryGetValue(candidateName,
                    out var candidate)
                && !candidates.Any(existing =>
                    existing.DefinitionIndex == candidate.DefinitionIndex))
            {
                candidates.Add(candidate);
            }
        }

        if (name.Contains('.', StringComparison.Ordinal))
        {
            Add(name);
        }
        else
        {
            Add(Qualify(name));
            var parent = _namespace;
            while (parent.Contains('.', StringComparison.Ordinal))
            {
                parent = parent[..parent.LastIndexOf('.',
                    StringComparison.Ordinal)];
                Add(parent + "." + name);
            }
            foreach (var imported in _usings)
            {
                Add(imported + "." + name);
            }
        }

        if (candidates.Count == 1)
        {
            template = candidates[0];
            return true;
        }

        template = null!;
        return false;
    }

    private bool TryFindGenericNominalTypeDefinitionForTypeTerm(
        string name, out BoundGenericNominalTypeDefinition definition)
    {
        var candidates = new List<BoundGenericNominalTypeDefinition>();

        void Add(string qualifiedName)
        {
            foreach (var candidate in _genericNominalTypeDefinitions)
            {
                if (!candidate.Formals.IsEmpty
                    && candidate.Namespace + "." + candidate.Name
                        == qualifiedName
                    && !candidates.Any(existing =>
                        existing.Identity == candidate.Identity))
                {
                    candidates.Add(candidate);
                }
            }
        }

        if (name.Contains('.', StringComparison.Ordinal))
        {
            Add(name);
        }
        else
        {
            Add(Qualify(name));
            var parent = _namespace;
            while (parent.Contains('.', StringComparison.Ordinal))
            {
                parent = parent[..parent.LastIndexOf('.',
                    StringComparison.Ordinal)];
                Add(parent + "." + name);
            }
            foreach (var imported in _usings)
            {
                Add(imported + "." + name);
            }
        }

        var coreTaskCandidate = candidates.FirstOrDefault(candidate =>
            BoundGenericNominalTypeContract.IsGenericNominalTypeName(
                candidate.Namespace + "." + candidate.Name));
        if (coreTaskCandidate is not null
            && (_reference?.CoreTaskType is not CoreTaskTypeIdentity coreTask
                || coreTaskCandidate.Identity != coreTask.Definition))
        {
            definition = null!;
            return false;
        }
        if (candidates.Count == 1)
        {
            definition = candidates[0];
            return true;
        }

        definition = null!;
        return false;
    }

    private static ImmutableArray<int>
        GenericMethodTypeTermDependencies(
            BoundGenericMethodTypeTerm term)
    {
        var dependencies = new SortedSet<int>();
        var pending = new Stack<BoundGenericMethodTypeTerm>();
        pending.Push(term);
        while (pending.TryPop(out var current))
        {
            if (current.Kind
                == BoundGenericMethodTypeTermKind.Formal)
            {
                _ = dependencies.Add(current.FormalOrdinal);
            }
            foreach (var child in current.TypeArguments)
            {
                pending.Push(child);
            }
        }
        return [.. dependencies];
    }

    private static bool ContainsOpenGenericTypeTerm(
        BoundGenericMethodTypeTerm term)
    {
        var pending = new Stack<BoundGenericMethodTypeTerm>();
        pending.Push(term);
        while (pending.TryPop(out var current))
        {
            if (current.Kind
                is BoundGenericMethodTypeTermKind.SourceGeneric
                    or BoundGenericMethodTypeTermKind.ImportedGeneric
                    or BoundGenericMethodTypeTermKind.NativePointer
                    or BoundGenericMethodTypeTermKind.NativeFixedArray
                    or BoundGenericMethodTypeTermKind.NativeFunctionPointer)
            {
                return true;
            }
            if (current.Kind == BoundGenericMethodTypeTermKind.Formal
                && current.IsWritable)
            {
                return true;
            }
            foreach (var child in current.TypeArguments)
            {
                pending.Push(child);
            }
        }
        return false;
    }

    private bool IsGenericMethodTypeTermInDomain(
        BoundGenericMethodTypeTerm term, GenericTemplate template,
        int formalOrdinal)
    {
        var targetFormal = template.TypeParameters[formalOrdinal];
        if (GetGenericMethodTypeTermOwnershipDomain(term, template, 0)
                is not byte ownershipDomain
            || (targetFormal.OwnershipDomain == 1
                && ownershipDomain != 1)
            || (targetFormal.OwnershipDomain == 2
                && ownershipDomain != 2)
            || (targetFormal.OwnershipDomain == 3
                && ownershipDomain is not (1 or 2 or 3))
            || (targetFormal.NullabilityDomain == 1
                && !IsGenericMethodTypeTermNonNullable(term, template))
            || (targetFormal.RequiresBlittable
                && !IsGenericMethodBlittableTypeTerm(term, template, 0))
            || (targetFormal.RequiresWritable
                && !IsGenericMethodTypeTermWritable(term, template)))
        {
            return false;
        }

        var pending = new Stack<(BoundGenericMethodTypeTerm Term, int Owner)>();
        pending.Push((term, formalOrdinal));
        while (pending.TryPop(out var current))
        {
            switch (current.Term.Kind)
            {
                case BoundGenericMethodTypeTermKind.Concrete:
                    if ((!IsCopyGenericArgumentType(
                            current.Term.ConcreteType)
                        && !IsUniqueGenericArgumentType(
                            current.Term.ConcreteType)
                        && !BoundEnumTypes.IsType(
                            current.Term.ConcreteType))
                        || current.Term.IsOptional)
                    {
                        return false;
                    }
                    break;
                case BoundGenericMethodTypeTermKind.Formal:
                    if (current.Term.FormalOrdinal < 0
                        || current.Term.FormalOrdinal >= current.Owner
                        || current.Term.IsOptional
                        || (current.Term.IsWritable
                            && !template.TypeParameters[
                                current.Term.FormalOrdinal]
                                .RequiresWritable))
                    {
                        return false;
                    }
                    break;
                case BoundGenericMethodTypeTermKind.CheckedReference:
                    if (current.Term.TypeArguments.IsDefaultOrEmpty
                        || current.Term.TypeArguments.Length != 1
                        || (current.Term.IsGarbageCollected
                            && current.Term.IsInitialization))
                    {
                        return false;
                    }
                    break;
                case BoundGenericMethodTypeTermKind.SourceGeneric:
                    if ((uint)current.Term.SourceGenericTypeDefinitionIndex
                            >= (uint)_sourceGenericTypeDefinitions.Count
                        || current.Term.IsWritable
                        || current.Term.IsGarbageCollected
                        || current.Term.IsInitialization
                        || current.Term.IsOptional)
                    {
                        return false;
                    }
                    var definition = _sourceGenericTypeDefinitions[
                        current.Term.SourceGenericTypeDefinitionIndex];
                    if (definition.Formals.IsDefault
                        || definition.Formals.Length
                            != current.Term.TypeArguments.Length)
                    {
                        return false;
                    }
                    for (var index = 0;
                        index < definition.Formals.Length; index++)
                    {
                        var formal = definition.Formals[index];
                        var argument = current.Term.TypeArguments[index];
                        var argumentDomain =
                            GetGenericMethodTypeTermOwnershipDomain(
                                argument, template, 0);
                        if (formal.OwnershipDomain is not (1 or 2 or 3)
                            || formal.NullabilityDomain is not (1 or 2)
                            || argumentDomain is not (1 or 2 or 3)
                            || (formal.OwnershipDomain == 1
                                && argumentDomain != 1)
                            || (formal.OwnershipDomain == 2
                                && argumentDomain != 2)
                            || (formal.NullabilityDomain == 1
                                && !IsGenericMethodTypeTermNonNullable(
                                    argument, template))
                            || (formal.RequiresBlittable
                                && !IsGenericMethodBlittableTypeTerm(
                                    argument, template, 0)))
                        {
                            return false;
                        }
                        pending.Push((argument, current.Owner));
                    }
                    continue;
                case BoundGenericMethodTypeTermKind.ImportedGeneric:
                    if (current.Term.FormalOrdinal != -1
                        || current.Term.SourceGenericTypeDefinitionIndex != -1
                        || current.Term.ConcreteType != PrimitiveType.Void
                        || current.Term.IsWritable
                        || current.Term.IsGarbageCollected
                        || current.Term.IsInitialization
                        || current.Term.IsOptional
                        || current.Term.NominalTypeIdentity
                            is not BoundGenericNominalTypeIdentity identity)
                    {
                        return false;
                    }

                    var nominalDefinition = _genericNominalTypeDefinitions
                        .SingleOrDefault(candidate =>
                            candidate.Identity == identity);
                    if (nominalDefinition is null
                        || nominalDefinition.NominalKind != 2
                        || nominalDefinition.Formals.IsDefault
                        || nominalDefinition.Formals.Length
                            != current.Term.TypeArguments.Length)
                    {
                        return false;
                    }
                    for (var index = 0;
                        index < nominalDefinition.Formals.Length; index++)
                    {
                        var formal = nominalDefinition.Formals[index];
                        var argument = current.Term.TypeArguments[index];
                        var argumentDomain =
                            GetGenericMethodTypeTermOwnershipDomain(
                                argument, template, 0);
                        if (formal.Kind != BoundGenericArgumentKind.Type
                            || formal.Constraints.IsDefault
                            || !formal.Constraints.IsEmpty
                            || formal.OwnershipDomain is not (1 or 2 or 3)
                            || formal.NullabilityDomain is not (1 or 2)
                            || argumentDomain is not (1 or 2 or 3)
                            || (formal.OwnershipDomain == 1
                                && argumentDomain != 1)
                            || (formal.OwnershipDomain == 2
                                && argumentDomain != 2)
                            || (formal.NullabilityDomain == 1
                                && !IsGenericMethodTypeTermNonNullable(
                                    argument, template)))
                        {
                            return false;
                        }
                        pending.Push((argument, current.Owner));
                    }
                    continue;
                case BoundGenericMethodTypeTermKind.NativePointer:
                    if (current.Term.FormalOrdinal != -1
                        || current.Term.SourceGenericTypeDefinitionIndex != -1
                        || current.Term.ConcreteType != PrimitiveType.Void
                        || current.Term.IsGarbageCollected
                        || current.Term.IsInitialization
                        || current.Term.IsOptional
                        || current.Term.NominalTypeIdentity is not null
                        || current.Term.TypeArguments.Length != 1
                        || !IsGenericMethodNativePointeeTerm(
                            current.Term.TypeArguments[0], template, 0))
                    {
                        return false;
                    }
                    break;
                case BoundGenericMethodTypeTermKind.NativeFunctionPointer:
                    if (!IsGenericMethodFunctionPointerSignatureTerm(
                        current.Term, template, 0))
                    {
                        return false;
                    }
                    continue;
                case BoundGenericMethodTypeTermKind.NativeFixedArray:
                    if (current.Term.FormalOrdinal != -1
                        || current.Term.SourceGenericTypeDefinitionIndex != -1
                        || current.Term.ConcreteType != PrimitiveType.Void
                        || current.Term.IsWritable
                        || current.Term.IsGarbageCollected
                        || current.Term.IsInitialization
                        || current.Term.IsOptional
                        || current.Term.CallingConvention
                            != NativeCallingConvention.Native
                        || current.Term.ParameterCount != 0
                        || current.Term.AllowsVoid
                        || current.Term.NominalTypeIdentity is not null
                        || current.Term.TypeArguments.Length != 1
                        || !IsGenericMethodFixedArrayElementTerm(
                            current.Term.TypeArguments[0], template, 0))
                    {
                        return false;
                    }
                    break;
                case BoundGenericMethodTypeTermKind.OptionalValue:
                    if (current.Term.TypeArguments.IsDefaultOrEmpty
                        || current.Term.TypeArguments.Length != 1)
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }

            foreach (var child in current.Term.TypeArguments)
            {
                pending.Push((child, current.Owner));
            }
        }

        return true;
    }

    private byte? GetGenericMethodTypeTermOwnershipDomain(
        BoundGenericMethodTypeTerm term, GenericTemplate template,
        int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            return null;
        }

        switch (term.Kind)
        {
            case BoundGenericMethodTypeTermKind.Concrete:
                if (IsUniqueGenericArgumentType(term.ConcreteType))
                {
                    return 2;
                }
                return IsCopyGenericArgumentType(term.ConcreteType)
                        || BoundEnumTypes.IsType(term.ConcreteType)
                    ? (byte)1 : null;
            case BoundGenericMethodTypeTermKind.Formal:
                return (uint)term.FormalOrdinal
                        < (uint)template.TypeParameters.Length
                    ? template.TypeParameters[term.FormalOrdinal]
                        .OwnershipDomain
                    : null;
            case BoundGenericMethodTypeTermKind.CheckedReference:
                return term.TypeArguments.Length == 1
                    && GetGenericMethodTypeTermOwnershipDomain(
                        term.TypeArguments[0], template, depth + 1) == 1
                    ? (byte)1 : null;
            case BoundGenericMethodTypeTermKind.OptionalValue:
                return term.TypeArguments.Length == 1
                    ? GetGenericMethodTypeTermOwnershipDomain(
                        term.TypeArguments[0], template, depth + 1)
                    : null;
            case BoundGenericMethodTypeTermKind.SourceGeneric:
                return GetSourceGenericMethodTypeTermOwnershipDomain(
                    term, template, depth + 1);
            case BoundGenericMethodTypeTermKind.ImportedGeneric:
                if (term.NominalTypeIdentity
                    is not BoundGenericNominalTypeIdentity identity)
                {
                    return null;
                }
                var importedDefinition =
                    _genericNominalTypeDefinitions.FirstOrDefault(
                        candidate => candidate.Identity == identity);
                if (importedDefinition is null
                    || importedDefinition.NominalKind != 2)
                {
                    return null;
                }
                return BoundGenericNominalTypeContract.IsGenericNominalTypeName(
                        importedDefinition.Namespace + "."
                            + importedDefinition.Name)
                    && _reference?.CoreTaskType?.Definition == identity
                    ? (byte)2 : (byte)1;
            case BoundGenericMethodTypeTermKind.NativePointer:
            case BoundGenericMethodTypeTermKind.NativeFixedArray:
            case BoundGenericMethodTypeTermKind.NativeFunctionPointer:
                return 1;
            default:
                return null;
        }
    }

    private byte? GetSourceGenericMethodTypeTermOwnershipDomain(
        BoundGenericMethodTypeTerm term, GenericTemplate template,
        int depth)
    {
        if ((uint)term.SourceGenericTypeDefinitionIndex
                >= (uint)_sourceGenericTypeDefinitions.Count)
        {
            return null;
        }

        var definition = _sourceGenericTypeDefinitions[
            term.SourceGenericTypeDefinitionIndex];
        if (definition.IsUnique)
        {
            return 2;
        }
        if (definition.Formals.IsDefault
            || definition.Formals.Length != term.TypeArguments.Length)
        {
            return null;
        }

        var domain = (byte)1;
        for (var index = 0; index < definition.Formals.Length; index++)
        {
            var formal = definition.Formals[index];
            var argument = term.TypeArguments[index];
            var argumentDomain =
                GetGenericMethodTypeTermOwnershipDomain(argument,
                    template, depth + 1);
            if (argumentDomain is not (1 or 2 or 3)
                || (formal.OwnershipDomain == 1
                    && argumentDomain != 1)
                || (formal.OwnershipDomain == 2
                    && argumentDomain != 2)
                || (formal.OwnershipDomain == 3
                    && argumentDomain is not (1 or 2 or 3))
                || (formal.NullabilityDomain == 1
                    && !IsGenericMethodTypeTermNonNullable(
                        argument, template))
                || (formal.RequiresBlittable
                    && !IsGenericMethodBlittableTypeTerm(
                        argument, template, depth + 1)))
            {
                return null;
            }

            if (argumentDomain != 1)
            {
                domain = 3;
            }
        }

        return domain;
    }

    private bool IsGenericMethodTypeTermNonNullable(
        BoundGenericMethodTypeTerm term, GenericTemplate template) =>
        term.Kind switch {
            BoundGenericMethodTypeTermKind.Concrete =>
                !IsSupportedOptionalType(term.ConcreteType),
            BoundGenericMethodTypeTermKind.Formal =>
                !term.IsOptional
                && (uint)term.FormalOrdinal
                    < (uint)template.TypeParameters.Length
                && template.TypeParameters[term.FormalOrdinal]
                    .NullabilityDomain == 1,
            BoundGenericMethodTypeTermKind.CheckedReference =>
                !term.IsOptional,
            BoundGenericMethodTypeTermKind.OptionalValue => false,
            BoundGenericMethodTypeTermKind.SourceGeneric
                or BoundGenericMethodTypeTermKind.ImportedGeneric
                or BoundGenericMethodTypeTermKind.NativePointer
                or BoundGenericMethodTypeTermKind.NativeFixedArray
                or BoundGenericMethodTypeTermKind.NativeFunctionPointer =>
                !term.IsOptional,
            _ => false,
        };

    private bool IsGenericMethodTypeTermWritable(
        BoundGenericMethodTypeTerm term, GenericTemplate template) =>
        term.Kind switch {
            BoundGenericMethodTypeTermKind.Concrete =>
                IsWritableGenericMethodTypeArgument(term.ConcreteType),
            BoundGenericMethodTypeTermKind.Formal =>
                (uint)term.FormalOrdinal
                    < (uint)template.TypeParameters.Length
                && template.TypeParameters[term.FormalOrdinal]
                    .RequiresWritable,
            BoundGenericMethodTypeTermKind.CheckedReference =>
                term.IsWritable,
            BoundGenericMethodTypeTermKind.OptionalValue =>
                term.TypeArguments.Length == 1
                && IsGenericMethodTypeTermWritable(
                    term.TypeArguments[0], template),
            BoundGenericMethodTypeTermKind.NativePointer =>
                term.IsWritable,
            _ => false,
        };

    private bool IsGenericMethodBlittableTypeTerm(
        BoundGenericMethodTypeTerm term, GenericTemplate template,
        int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            return false;
        }

        return term.Kind switch {
            BoundGenericMethodTypeTermKind.Concrete =>
                IsBlittableGenericArgumentType(term.ConcreteType, []),
            BoundGenericMethodTypeTermKind.Formal =>
                (uint)term.FormalOrdinal
                    < (uint)template.TypeParameters.Length
                && template.TypeParameters[term.FormalOrdinal]
                    .RequiresBlittable
                && !term.IsGarbageCollected
                && !term.IsInitialization
                && !term.IsOptional,
            BoundGenericMethodTypeTermKind.NativePointer =>
                !term.IsGarbageCollected
                && !term.IsInitialization
                && !term.IsOptional
                && term.TypeArguments.Length == 1
                && IsGenericMethodNativePointeeTerm(
                    term.TypeArguments[0], template, depth + 1),
            BoundGenericMethodTypeTermKind.NativeFunctionPointer =>
                IsGenericMethodFunctionPointerSignatureTerm(
                    term, template, depth + 1),
            BoundGenericMethodTypeTermKind.NativeFixedArray =>
                !term.IsWritable
                && !term.IsGarbageCollected
                && !term.IsInitialization
                && !term.IsOptional
                && term.TypeArguments.Length == 1
                && IsGenericMethodFixedArrayElementTerm(
                    term.TypeArguments[0], template, depth + 1),
            _ => false,
        };
    }

    private bool IsGenericMethodFixedArrayElementTerm(
        BoundGenericMethodTypeTerm term, GenericTemplate template,
        int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            return false;
        }

        return term.Kind switch {
            BoundGenericMethodTypeTermKind.Concrete =>
                IsCopyGenericArgumentType(term.ConcreteType)
                && IsBlittableGenericArgumentType(term.ConcreteType, []),
            BoundGenericMethodTypeTermKind.Formal =>
                (uint)term.FormalOrdinal
                < (uint)template.TypeParameters.Length
                && template.TypeParameters[term.FormalOrdinal]
                is {
                    OwnershipDomain: 1,
                    RequiresBlittable: true,
                }
                && !term.IsGarbageCollected
                && !term.IsInitialization
                && !term.IsOptional,
            BoundGenericMethodTypeTermKind.NativePointer =>
                IsGenericMethodBlittableTypeTerm(
                term, template, depth + 1),
            BoundGenericMethodTypeTermKind.NativeFunctionPointer =>
                IsGenericMethodFunctionPointerSignatureTerm(
                term, template, depth + 1),
            BoundGenericMethodTypeTermKind.NativeFixedArray =>
                term.TypeArguments.Length == 1
                && IsGenericMethodFixedArrayElementTerm(
                term.TypeArguments[0], template, depth + 1),
            _ => false,
        };
    }

    private bool IsGenericMethodNativePointeeTerm(
        BoundGenericMethodTypeTerm term, GenericTemplate template,
        int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            return false;
        }

        return term.Kind switch {
            BoundGenericMethodTypeTermKind.Concrete =>
                NativePointerTypeContract.IsSourcePointee(
                    term.ConcreteType, _structs, _checkedReferenceTypes,
                    _nativeLoweringAgreement, _nativeFixedArrayTypes),
            BoundGenericMethodTypeTermKind.Formal =>
                (uint)term.FormalOrdinal
                    < (uint)template.TypeParameters.Length
                && template.TypeParameters[term.FormalOrdinal]
                    .RequiresBlittable
                && !term.IsGarbageCollected
                && !term.IsInitialization
                && !term.IsOptional,
            BoundGenericMethodTypeTermKind.NativePointer =>
                term.TypeArguments.Length == 1
                && !term.IsGarbageCollected
                && !term.IsInitialization
                && !term.IsOptional
                && IsGenericMethodNativePointeeTerm(
                    term.TypeArguments[0], template, depth + 1),
            BoundGenericMethodTypeTermKind.NativeFunctionPointer =>
                IsGenericMethodFunctionPointerSignatureTerm(
                    term, template, depth + 1),
            _ => false,
        };
    }

    private bool IsGenericMethodFunctionPointerSignatureTerm(
        BoundGenericMethodTypeTerm term, GenericTemplate template,
        int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth
            || term.Kind
            != BoundGenericMethodTypeTermKind.NativeFunctionPointer
            || term.FormalOrdinal != -1
            || term.SourceGenericTypeDefinitionIndex != -1
            || term.ConcreteType != PrimitiveType.Void
            || term.IsWritable
            || term.IsGarbageCollected
            || term.IsInitialization
            || term.IsOptional
            || term.NominalTypeIdentity is not null
            || term.ElementCount != 0
            || term.AllowsVoid
            || term.ParameterCount < 0
            || term.ParameterCount >= SemanticVerifier.MaximumArguments
            || term.TypeArguments.Length != term.ParameterCount + 1
            || !NativeFunctionPointerContract.IsKnownConvention(
            term.CallingConvention))
        {
            return false;
        }

        for (var index = 0; index < term.ParameterCount; index++)
        {
            if (!IsGenericMethodFunctionPointerValueTerm(
            term.TypeArguments[index], template, allowVoid: false,
            depth + 1))
            {
            return false;
            }
        }

        return IsGenericMethodFunctionPointerValueTerm(
            term.TypeArguments[^1], template, allowVoid: true,
            depth + 1);
    }

    private bool IsGenericMethodFunctionPointerValueTerm(
        BoundGenericMethodTypeTerm term, GenericTemplate template,
        bool allowVoid, int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            return false;
        }

        switch (term.Kind)
        {
            case BoundGenericMethodTypeTermKind.Concrete:
            if (term.FormalOrdinal != -1
                || term.SourceGenericTypeDefinitionIndex != -1
                || term.NominalTypeIdentity is not null
                || !term.TypeArguments.IsEmpty
                || term.IsWritable
                || term.IsGarbageCollected
                || term.IsInitialization
                || term.IsOptional
                || term.CallingConvention
                    != NativeCallingConvention.Native
                || term.ParameterCount != 0
                || term.ElementCount != 0)
            {
                return false;
            }
            if (term.AllowsVoid)
            {
                return allowVoid
                    && term.ConcreteType == PrimitiveType.Void;
            }
            return term.ConcreteType != PrimitiveType.Void
                && (PrimitiveTypeFacts.ScalarWidth(
                        term.ConcreteType, _enumDefinitions) > 0
                    || term.ConcreteType
                        == PrimitiveType.NativeRawPointer
                    || NativePointerTypes.IsType(
                        term.ConcreteType)
                    || NativeFunctionPointerTypes.IsType(
                        term.ConcreteType)
                    || BoundEnumTypes.IsType(term.ConcreteType));
            case BoundGenericMethodTypeTermKind.NativePointer:
            return IsGenericMethodNativePointeeTerm(term, template,
                depth + 1);
            case BoundGenericMethodTypeTermKind.NativeFunctionPointer:
            return IsGenericMethodFunctionPointerSignatureTerm(
                term, template, depth + 1);
            default:
            return false;
        }
    }

    private PrimitiveType? InferGenericParameterType(
        GenericParameter parameter, PrimitiveType? argumentType)
    {
        if (argumentType is not PrimitiveType typed
            || parameter.ReferenceTypePattern
                is not CheckedReferenceTypeDefinition expectedReference)
        {
            return argumentType;
        }
        return InferGenericReferenceType(expectedReference, typed);
    }

    private PrimitiveType? InferGenericReferenceType(
        CheckedReferenceTypeDefinition expectedReference,
        PrimitiveType typed)
    {
        if (!CheckedReferenceTypeContract.IsValidType(typed,
                [.. _checkedReferenceTypes]))
        {
            return null;
        }
        var actualReference = _checkedReferenceTypes[
            CheckedReferenceTypes.Index(typed)];
        return actualReference.IsWritable == expectedReference.IsWritable
            && actualReference.IsGarbageCollected
                == expectedReference.IsGarbageCollected
            && actualReference.IsInitialization
                == expectedReference.IsInitialization
            && actualReference.IsOptional == expectedReference.IsOptional
            && actualReference.IsOptionalValue
                == expectedReference.IsOptionalValue
            ? actualReference.ReferentType : null;
    }

    private PrimitiveType ResolveGenericParameterType(
        GenericParameter parameter, ImmutableArray<PrimitiveType> types,
        TextSpan span)
    {
        if (parameter.ReferenceTypePattern
                is not CheckedReferenceTypeDefinition referencePattern)
        {
            return parameter.Resolve(types);
        }
        if ((uint)parameter.TypeParameterIndex >= (uint)types.Length)
        {
            throw new InvalidOperationException(
                "Generic checked-reference parameter has an invalid type argument.");
        }
        return GetCheckedReferenceType(referencePattern with {
            ReferentType = types[parameter.TypeParameterIndex],
        }, span);
    }

    private PrimitiveType ResolveGenericReturnType(
        GenericTemplate template, ImmutableArray<PrimitiveType> types,
        TextSpan span)
    {
        if (template.ReferenceReturnTypePattern
            is not CheckedReferenceTypeDefinition referencePattern)
        {
            return template.ReturnType(types);
        }
        if ((uint)template.ReturnTypeParameterIndex >= (uint)types.Length)
        {
            throw new InvalidOperationException(
                "Generic checked-reference result has an invalid type argument.");
        }
        return GetCheckedReferenceType(referencePattern with {
            ReferentType = types[template.ReturnTypeParameterIndex],
        }, span);
    }

    private GenericTemplate? FindGenericTemplate(string name)
    {
        var candidateName = string.Empty;
        var hasCandidate = false;
        var qualifiedName = Qualify(name);
        if (_templates.TryGetValue(qualifiedName, out var candidate))
        {
            candidateName = qualifiedName;
            hasCandidate = true;
        }
        foreach (var imported in _usings)
        {
            var qualified = imported + "." + name;
            if (!_templates.TryGetValue(qualified, out var template))
            {
                continue;
            }
            if (hasCandidate && candidateName != qualified)
            {
                return null;
            }

            candidateName = qualified;
            candidate = template;
            hasCandidate = true;
        }

        return candidate;
    }

    private int InstantiateGeneric(SyntaxNode callee, SyntaxNode? callArguments = null, PrimitiveType? expected = null)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var name = ((SyntaxTokenElement)callee.Children[0]).Token.Name;
        if (TryFindVariable(name, out _))
        {
            Report("TRM3011", $"'{name}' is a variable, not a function.", callee.Span);
            return -1;
        }
        var template = FindGenericTemplate(name);
        ImmutableArray<PrimitiveType>? concrete = null;
        if (template is not null
            && callee.Kind == SyntaxKind.GenericNameExpression)
        {
            if (TryReadGenericMethodTypeArguments(callee, template,
                    out var suppliedTypes, out var requireInference,
                    out var explicitDefaults))
            {
                if (callArguments is SyntaxNode arguments)
                {
                    concrete = InferGenericTypes(template, arguments,
                        expected, out var conflictingArguments,
                        suppliedTypes, requireInference,
                        explicitDefaults);
                    if (conflictingArguments)
                    {
                        Report("TRM3007",
                            "Typed arguments or result context establish conflicting generic types.",
                            arguments.Span);
                        return -1;
                    }
                }
                else
                {
                    concrete = CompleteGenericTypeArguments(template,
                        suppliedTypes, requireInference,
                        explicitDefaults);
                }
            }
        }
        else if (template is not null && callee.Kind != SyntaxKind.GenericNameExpression
            && callArguments is not null)
        {
            concrete = InferGenericTypes(template, callArguments, expected, out var conflicting);
            if (conflicting)
            {
                Report("TRM3007", "Typed arguments or result context establish conflicting generic types.",
                    callArguments.Span);
                return -1;
            }
        }
        if (template is null
            || concrete is not ImmutableArray<PrimitiveType> types)
        {
            Report("TRM3011", "A generic call requires each default-domain type from a complete explicit list, typed arguments, or its result context.",
                callee.Span);
            return -1;
        }
        for (var ordinal = 0; ordinal < types.Length; ordinal++)
        {
            var formal = template.TypeParameters[ordinal];
            if (GenericMethodTypeArgumentConstraintFailure(template,
                ordinal, types[ordinal]) is string failure)
            {
                if (failure == "ownership domain"
                    && formal.OwnershipDomain == 1)
                {
                    Report("TRM3011",
                        "A generic call requires each type argument to satisfy its default Copy domain.",
                        callee.Span);
                    return -1;
                }
                Report("TRM3005",
                    $"Type argument for formal '{formal.Name}' does not satisfy its {failure}.",
                    callee.Span);
                return -1;
            }
        }
        var instantiatedReturnType = ResolveGenericReturnType(template,
            types, callee.Span);
        if (instantiatedReturnType != PrimitiveType.Bool
            && template.Parameters.Any(parameter => parameter.IsOut && !parameter.IsInit
                && !IsSupportedNonBooleanGenericOutputType(parameter.Resolve(types))))
        {
            Report("TRM3011", "A non-Boolean generic output requires a supported scalar or non-unique Copy type.",
                callee.Span);
            return -1;
        }
        var typeNames = types.Select(GenericMethodTypeName).ToArray();
        if (types.Select((type, index) => (type, index)).Any(item => {
            var required = template.TypeParameters[item.index].Operations.Values;
            if (_genericProofFunctions is not null && _genericContext is GenericInstantiation enclosing)
            {
                for (var callerIndex = 0; callerIndex < enclosing.Types.Length; callerIndex++)
                {
                    if (enclosing.Types[callerIndex] == item.type)
                    {
                        return required.Any(operation =>
                            !enclosing.Template.TypeParameters[callerIndex].Operations.Values.Any(caller =>
                                caller.Protocol == operation.Protocol
                                && caller.Resolve(enclosing.Types) == operation.Resolve(types)));
                    }
                }
            }
            return required.Any()
                && (!IsStruct(item.type) || !_conformances.TryGetValue(typeNames[item.index], out var witnesses)
                    || required.Any(operation => !witnesses.TryGetValue(operation.Protocol, out var result)
                        || result != operation.Resolve(types)));
        }))
        {
            Report("TRM3007", "A helper call requires declared protocol witnesses for every selected Copy type.",
                callee.Span);
            return -1;
        }
        var instantiated = template.Name + "<" + string.Join(", ", typeNames) + ">";
        if (_functions.TryGetValue(instantiated, out var existing))
        {
            return existing;
        }
        if (_signatures.Count >= SemanticVerifier.MaximumFunctions
            || _signatures.Count(static signature => signature.Generic is not null) >= MaximumStaticSpecializations)
        {
            Report("TRM3016", "Closed generic specialization limit exceeded.", callee.Span);
            return -1;
        }
        var parameters = template.Parameters.Select(parameter =>
            new Parameter(parameter.Name,
                ResolveGenericParameterType(parameter, types, callee.Span),
                callee.Span, parameter.IsOut, parameter.IsInit))
            .ToImmutableArray();
        var index = _signatures.Count;
        _functions.Add(instantiated, index);
        var signature = new Signature(instantiated, instantiatedReturnType, parameters,
            _genericProofFunctions is null ? template.Body : null,
            _genericProofFunctions is null ? template.Declaration : null,
            template.Tree, null, false, false, true, false, true, false, false) {
            RequiresUnsafeCaller = template.RequiresUnsafeCaller,
            DependencyContracts = template.DependencyContracts,
        };
        if (_genericProofFunctions is not null)
        {
            var variables = parameters.Select(parameter => new BoundVariable(parameter.Name,
                parameter.Type, parameter.IsOut, parameter.Span) {
                IsOut = parameter.IsOut,
                IsInit = parameter.IsInit,
            }).ToImmutableArray();
            var external = new BoundFunction(instantiated, signature.ReturnType, variables, parameters.Length,
                new BoundBlock([], callee.Span), callee.Span) {
                IsExternal = true,
                RequiresUnsafeCaller = template.RequiresUnsafeCaller,
                DependencyContracts = template.DependencyContracts,
            };
            signature = signature with { External = external };
            _genericProofFunctions.Add(external);
        }
        else
        {
            signature = signature with { Generic = new GenericInstantiation(template, types) };
        }
        _signatures.Add(signature);
        return index;
    }

    private string GenericMethodTypeName(PrimitiveType type)
    {
        if (NativeFixedArrayTypes.IsType(type))
        {
            var arrayIndex = NativeFixedArrayTypes.Index(type);
            if ((uint)arrayIndex >= (uint)_nativeFixedArrayTypes.Count)
            {
                throw new InvalidOperationException(
                    "A generic method type argument has no fixed-array instance.");
            }

            var array = _nativeFixedArrayTypes[arrayIndex];
            return "$fixed-array{" + (int)array.ElementType + ";"
                + array.ElementCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + "}";
        }
        if (!BoundGenericNominalTypeTypes.IsType(type))
        {
            return IsStruct(type)
                ? _structs[(int)type - 100].Name
                : TypeName(type);
        }

        var index = BoundGenericNominalTypeTypes.Index(type);
        if ((uint)index >= (uint)_genericNominalTypeInstances.Count)
        {
            throw new InvalidOperationException(
                "A generic method type argument has no nominal instance.");
        }

        var instance = _genericNominalTypeInstances[index];
        return "$nominal{" + BoundGenericNominalTypeContract.CreateUseKey(
            instance.Definition, instance.Arguments, instance.Qualifiers,
            instance.TypeSpecForm) + "}";
    }

    private BoundBlock BindGenericBody(GenericInstantiation instantiation)
    {
        var template = instantiation.Template;
        var previous = _genericContext;
        _genericContext = instantiation;
        try
        {
            return BindCallableBody(template.Body);
        }
        finally
        {
            _genericContext = previous;
        }
    }

    private bool TryBindGenericTemplateDependencyContracts(
        GenericTemplate template,
        out ImmutableArray<BoundDependencyContract> contracts)
    {
        contracts = [];
        var attributes = template.Declaration.Children
            .OfType<SyntaxNode>()
            .Where(attribute => attribute.Kind == SyntaxKind.Attribute
                && TryGetDependencyRelation(AttributeName(attribute), out _))
            .Select(static attribute => (Attribute: attribute,
                InlineParameterOrdinal: (int?)null))
            .ToImmutableArray();
        if (attributes.IsEmpty)
        {
            return true;
        }

        var proofBinder = new Binder([template.Tree], [],
            _cancellationToken, isLibrary: true,
            runtimePolicy: _runtimePolicy,
            sourceArithmeticProfile: _sourceArithmeticProfile,
            nativeCoreRegistration: _nativeCoreRegistration,
            layoutCoreRegistration: _layoutCoreRegistration,
            coreExceptionIdentitySelections:
                _coreExceptionIdentitySelections);
        proofBinder._structs.AddRange(_structs);
        proofBinder._classes.AddRange(_classes);
        proofBinder._enumDefinitions.AddRange(_enumDefinitions);
        proofBinder._checkedReferenceTypes.AddRange(_checkedReferenceTypes);
        proofBinder._nativeFunctionPointerSignatures.AddRange(
            _nativeFunctionPointerSignatures);
        proofBinder._nativePointerTypes.AddRange(_nativePointerTypes);
        proofBinder._nativeFixedArrayTypes.AddRange(_nativeFixedArrayTypes);
        for (var enumIndex = 0;
            enumIndex < proofBinder._enumDefinitions.Count; enumIndex++)
        {
            proofBinder._enumNames.Add(
                proofBinder._enumDefinitions[enumIndex].Name,
                BoundEnumTypes.Type(enumIndex));
        }
        foreach (var (tree, context) in _contexts)
        {
            proofBinder._contexts.Add(tree, context);
        }
        foreach (var (node, context) in _nodeContexts)
        {
            proofBinder._nodeContexts.Add(node, context);
        }
        foreach (var (name, alias) in _aliases)
        {
            proofBinder._aliases.Add(name, alias);
        }
        proofBinder.SetContext(template.Tree, template.Declaration);

        var probeTypes = ImmutableArray.CreateBuilder<PrimitiveType>(
            template.TypeParameters.Length);
        for (var parameterIndex = 0;
            parameterIndex < template.TypeParameters.Length; parameterIndex++)
        {
            var probeName = template.TypeParameters.Length == 1
                ? "$T" : "$T" + parameterIndex;
            var probeType = (PrimitiveType)(100
                + proofBinder._structs.Count);
            probeTypes.Add(probeType);
            proofBinder._structs.Add(new BoundStruct(probeName,
                [new BoundStructField("$opaque", PrimitiveType.Int32, 0,
                    sizeof(int)) { IsPrivate = true }],
                sizeof(int)) {
                IsInternal = true,
                Alignment = sizeof(int),
                RequiredAlignment = 1,
            });
        }

        var symbolicTypes = probeTypes.MoveToImmutable();
        var parameters = template.Parameters.Select(parameter =>
            new Parameter(parameter.Name,
                proofBinder.ResolveGenericParameterType(parameter,
                    symbolicTypes, template.Declaration.Span),
                template.Declaration.Span, parameter.IsOut,
                parameter.IsInit)).ToImmutableArray();
        var returnType = proofBinder.ResolveGenericReturnType(template,
            symbolicTypes, template.Declaration.Span);
        contracts = proofBinder.BindDependencyContracts(attributes, parameters,
            returnType, containingType: null, isStatic: true,
            constructor: false, classConstructor: false,
            nativeOutputParameters: [],
            declarationSpan: template.Declaration.Span);
        if (proofBinder.DiagnosticCount == 0)
        {
            return true;
        }

        foreach (var diagnostic in proofBinder.Diagnostics)
        {
            AddDiagnostic(diagnostic, template.Tree);
        }
        return false;
    }

    private bool ValidateGenericTemplates()
    {
        var templates = _templates.Values
            .OrderBy(template => _trees.IndexOf(template.Tree))
            .ThenBy(static template => template.Declaration.Span.Start)
            .ToArray();
        _genericMethodDefinitions.Clear();
        _genericMethodSignatures.Clear();
        _genericMethodOperatorProtocols.Clear();
        _genericMethodOperatorProtocolOrdinals.Clear();
        var usedProtocolNames = templates
            .SelectMany(static template => template.TypeParameters)
            .SelectMany(static formal => formal.Operations.Values)
            .Select(static requirement => requirement.Protocol)
            .ToHashSet(StringComparer.Ordinal);
        var protocols = _protocols.Values
            .Where(protocol => usedProtocolNames.Contains(protocol.Name))
            .OrderBy(protocol => _trees.IndexOf(protocol.Tree))
            .ThenBy(static protocol => protocol.Declaration.Span.Start)
            .ToArray();
        if (protocols.Length != usedProtocolNames.Count)
        {
            throw new InvalidOperationException(
                "A generic method operator requirement has no declared protocol.");
        }
        for (var protocolIndex = 0; protocolIndex < protocols.Length;
            protocolIndex++)
        {
            var protocol = protocols[protocolIndex];
            var sourceInputOrdinal = _trees.IndexOf(protocol.Tree);
            if (sourceInputOrdinal < 0
                || !_genericMethodOperatorProtocolOrdinals.TryAdd(
                    protocol.Name, protocolIndex))
            {
                throw new InvalidOperationException(
                    "A generic method operator protocol has invalid source identity.");
            }
            _genericMethodOperatorProtocols.Add(
                new BoundGenericMethodOperatorProtocol(protocolIndex,
                    protocol.Name, sourceInputOrdinal,
                    protocol.Declaration.Span, protocol.Key.Operator,
                    protocol.Key.Arity, protocol.HasSelf,
                    protocol.RequiresUnsafeCaller));
        }
        for (var index = 0; index < templates.Length; index++)
        {
            var template = templates[index] with {
                DefinitionOrdinal = index,
            };
            if (!TryBindGenericTemplateDependencyContracts(template,
                out var dependencyContracts))
            {
                return false;
            }
            template = template with {
                DependencyContracts = dependencyContracts,
            };
            templates[index] = template;
            _templates[template.Name] = template;
            _genericMethodSignatures.Add(
                new BoundGenericMethodSignature(index, template.Name,
                    template.IsPublic, template.RequiresUnsafeCaller,
                    [.. template.TypeParameters.Select((formal,
                        ordinal) =>
                        new BoundGenericMethodFormalSignature(ordinal,
                            formal.Name, formal.OwnershipDomain,
                            formal.NullabilityDomain,
                            GenericOperatorRequirements(formal,
                                _genericMethodOperatorProtocolOrdinals)) {
                            RequiresBlittable = formal.RequiresBlittable,
                            RequiresWritable = formal.RequiresWritable,
                        })]));
        }

        foreach (var template in templates)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SetContext(template.Tree, template.Declaration);
            var genericTypeNames = template.TypeParameters
                .Select(static parameter => parameter.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (ContainsUnconstrainedGenericOptionalType(template.Body,
                    genericTypeNames))
            {
                Report("TRM3007",
                    "An unconstrained generic Copy formal cannot be used as an optional value type.",
                    template.Declaration.Span);
                return false;
            }
            var proofBinder = new Binder([template.Tree], [], _cancellationToken,
                isLibrary: true, runtimePolicy: _runtimePolicy,
                sourceArithmeticProfile: _sourceArithmeticProfile,
                nativeCoreRegistration: _nativeCoreRegistration,
                layoutCoreRegistration: _layoutCoreRegistration,
                coreExceptionIdentitySelections:
                    _coreExceptionIdentitySelections);
            proofBinder._enumDefinitions.AddRange(_enumDefinitions);
            for (var enumIndex = 0; enumIndex < proofBinder._enumDefinitions.Count;
                enumIndex++)
            {
                proofBinder._enumNames.Add(proofBinder._enumDefinitions[enumIndex].Name,
                    BoundEnumTypes.Type(enumIndex));
            }
            foreach (var (tree, context) in _contexts)
            {
                proofBinder._contexts.Add(tree, context);
            }
            foreach (var (node, context) in _nodeContexts)
            {
                proofBinder._nodeContexts.Add(node, context);
            }
            foreach (var (name, alias) in _aliases)
            {
                proofBinder._aliases.Add(name, alias);
            }
            proofBinder.SetContext(template.Tree, template.Declaration);
            var functions = ImmutableArray.CreateBuilder<BoundFunction>();
            var probeTypes = ImmutableArray.CreateBuilder<PrimitiveType>(template.TypeParameters.Length);
            proofBinder._genericProofFunctions = functions;
            proofBinder._genericProofSource = this;
            var collect = NativeManagedNodeIntrinsics.Functions.Single(static function =>
                function.Intrinsic == BoundIntrinsic.NodeCollect);
            proofBinder._functions.Add(collect.Name, proofBinder._signatures.Count);
            proofBinder._signatures.Add(new Signature(collect.Name, collect.ReturnType, [],
                null, null, template.Tree, collect.DeclaringStruct, false, false, true, false,
                collect.IsStatic, collect.NoFailureProof, false, collect));
            functions.Add(collect);
            for (var parameterIndex = 0; parameterIndex < template.TypeParameters.Length; parameterIndex++)
            {
                var probeName = template.TypeParameters.Length == 1 ? "$T" : "$T" + parameterIndex;
                // Symbolic formals must not alias a concrete source structure in this proof.
                var probeType = (PrimitiveType)(100 + proofBinder._structs.Count);
                probeTypes.Add(probeType);
                proofBinder._structs.Add(new BoundStruct(probeName,
                    [new BoundStructField("$opaque", PrimitiveType.Int32, 0, 4) { IsPrivate = true }], 4) {
                    IsInternal = true,
                    IsUnique = template.TypeParameters[parameterIndex]
                        .OwnershipDomain is 2 or 3,
                    Alignment = sizeof(int),
                    RequiredAlignment = 1,
                });
            }
            var symbolicTypes = probeTypes.MoveToImmutable();
            var typeDefaults =
                new BoundGenericMethodTypeDefault?[
                    template.TypeParameters.Length];
            for (var formalIndex = 0;
                formalIndex < template.TypeParameters.Length; formalIndex++)
            {
                var formal = template.TypeParameters[formalIndex];
                if (formal.TypeDefaultSyntax is null)
                {
                    continue;
                }

                var defaultTypeTerm = BindGenericMethodTypeTerm(
                    formal.TypeDefaultSyntax, template);
                var formalDependencies =
                    defaultTypeTerm is BoundGenericMethodTypeTerm term
                        ? GenericMethodTypeTermDependencies(term) : [];
                if (defaultTypeTerm is null
                    || !formalDependencies.SequenceEqual(
                        formal.DefaultTypeParameterDependencies)
                    || !IsGenericMethodTypeTermInDomain(
                        defaultTypeTerm, template, formalIndex))
                {
                    Report("TRM3007",
                        "A generic type default must be a complete value type in the formal's default domain and may depend only on earlier formals.",
                        formal.TypeDefaultSyntax.Span);
                    return false;
                }
                if (defaultTypeTerm.Kind
                        != BoundGenericMethodTypeTermKind.Concrete
                    && !ContainsOpenGenericTypeTerm(defaultTypeTerm)
                    && proofBinder.BindGenericMethodTypeDefault(template,
                        formalIndex, symbolicTypes)
                        is not PrimitiveType)
                {
                    Report("TRM3007",
                        "A generic type default does not bind to a supported complete value type.",
                        formal.TypeDefaultSyntax.Span);
                    return false;
                }

                typeDefaults[formalIndex]
                    = new BoundGenericMethodTypeDefault(defaultTypeTerm,
                        formalDependencies);
            }
            for (var parameterIndex = 0; parameterIndex < template.TypeParameters.Length; parameterIndex++)
            {
                var probeType = symbolicTypes[parameterIndex];
                var probeName = proofBinder._structs[(int)probeType - 100].Name;
                foreach (var (key, constraint) in template.TypeParameters[parameterIndex].Operations)
                {
                    var name = probeName + "." + OperatorName(key);
                    var operatorSpan = template.Declaration.Span;
                    ImmutableArray<BoundVariable> variables = key.Arity == 1
                        ? [new BoundVariable("value", probeType, false, operatorSpan)]
                        : [new BoundVariable("left", probeType, false, operatorSpan),
                            new BoundVariable("right", probeType, false, operatorSpan)];
                    var resultType = constraint.FixedResult is PrimitiveType fixedResult
                        ? proofBinder.HydrateProofType(fixedResult)
                            ?? throw new InvalidOperationException("Missing proof operator result.")
                        : constraint.Resolve(symbolicTypes);
                    var external = new BoundFunction(name, resultType, variables, key.Arity,
                        new BoundBlock([], operatorSpan), operatorSpan) {
                        IsExternal = true,
                        IsStatic = true,
                        IsOperator = true,
                        RequiresUnsafeCaller = _protocols[constraint.Protocol].RequiresUnsafeCaller,
                        DeclaringStruct = probeName,
                    };
                    proofBinder._functions.Add(name, proofBinder._signatures.Count);
                    var parameters = variables.Select(variable =>
                        new Parameter(variable.Name, probeType, operatorSpan, false)).ToImmutableArray();
                    proofBinder._signatures.Add(new Signature(name, resultType, parameters,
                        null, null, template.Tree, probeName, false, false, false, false, true,
                        external.NoFailureProof, false, external) {
                        IsOperator = true,
                        RequiresUnsafeCaller = external.RequiresUnsafeCaller,
                    });
                    functions.Add(external);
                }
            }

            // Helper bodies cannot confer witnesses or effects on their caller. Only the
            // requested declared signature is materialized during this isolated proof.
            foreach (var helper in _templates.Values)
            {
                proofBinder._templates.Add(helper.Name, helper);
            }
            var symbolicName = template.Name + "<" + string.Join(", ",
                symbolicTypes.Select(type => proofBinder._structs[(int)type - 100].Name)) + ">";
            var symbolicParameters = template.Parameters.Select(parameter =>
                new Parameter(parameter.Name,
                    proofBinder.ResolveGenericParameterType(parameter,
                        symbolicTypes, template.Declaration.Span),
                    template.Declaration.Span, parameter.IsOut,
                    parameter.IsInit)).ToImmutableArray();
            var symbolicVariables = symbolicParameters.Select(parameter => new BoundVariable(parameter.Name,
                parameter.Type, parameter.IsOut, parameter.Span) {
                IsOut = parameter.IsOut,
                IsInit = parameter.IsInit,
            }).ToImmutableArray();
            var symbolicReturn = proofBinder.ResolveGenericReturnType(
                template, symbolicTypes, template.Declaration.Span);
            var symbolicIndex = proofBinder._signatures.Count;
            proofBinder._functions.Add(symbolicName, symbolicIndex);
            var symbolicExternal = new BoundFunction(symbolicName,
                symbolicReturn, symbolicVariables, symbolicParameters.Length,
                new BoundBlock([], template.Declaration.Span),
                template.Declaration.Span) {
                IsExternal = true,
                RequiresUnsafeCaller = template.RequiresUnsafeCaller,
                DependencyContracts = template.DependencyContracts,
            };
            proofBinder._signatures.Add(new Signature(symbolicName, symbolicReturn, symbolicParameters,
                null, null, template.Tree, null, false, false, true, false,
                true, false, false, symbolicExternal) {
                DependencyContracts = template.DependencyContracts,
            });
            functions.Add(proofBinder._signatures[symbolicIndex].External
                ?? throw new InvalidOperationException("Missing generic proof signature."));

            proofBinder._scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
            proofBinder._returnType = symbolicReturn;
            foreach (var parameter in template.Parameters)
            {
                var slot = proofBinder.AddVariable(parameter.Name,
                    proofBinder.ResolveGenericParameterType(parameter,
                        symbolicTypes, template.Declaration.Span),
                    parameter.IsOut, template.Declaration.Span);
                if (parameter.IsOut)
                {
                    proofBinder._variables[slot] = proofBinder._variables[slot] with {
                        IsOut = true,
                        IsInit = parameter.IsInit,
                    };
                }
            }
            proofBinder._localStart = proofBinder._variables.Count;
            var body = proofBinder.BindGenericBody(new GenericInstantiation(template, symbolicTypes));
            if (template.TypeParameters.Any(static formal =>
                    formal.OwnershipDomain == 3)
                && ContainsMoveSyntax(template.Body))
            {
                Report("TRM3007",
                    "A unique? formal requires a checked type-property refinement before moving its value.",
                    template.Declaration.Span);
                return false;
            }
            if (symbolicReturn == PrimitiveType.Void)
            {
                var end = new TextSpan(template.Body.Span.End, 0);
                body = body with { Statements = body.Statements.Add(new BoundReturn(null, end)) };
            }
            for (var parameterIndex = template.Parameters.Length - 1;
                parameterIndex >= 0; parameterIndex--)
            {
                var parameter = template.Parameters[parameterIndex];
                var parameterType = proofBinder.ResolveGenericParameterType(
                    parameter, symbolicTypes, template.Declaration.Span);
                if (!proofBinder.IsUnique(parameterType))
                {
                    continue;
                }

                var cleanupMethod = proofBinder.CleanupMethod(parameterType);
                var cleanupFunction = cleanupMethod is not null
                    && proofBinder._functions.TryGetValue(cleanupMethod,
                        out var found) ? found : -1;
                body = new BoundBlock([
                    new BoundTryFinally(body,
                        new BoundBlock([
                            new BoundDrop(parameterIndex, cleanupFunction,
                                template.Declaration.Span) {
                                IsOutput = parameter.IsOut,
                            },
                        ], template.Declaration.Span),
                        template.Declaration.Span),
                ], template.Declaration.Span);
            }
            if (proofBinder.DiagnosticCount != 0)
            {
                foreach (var diagnostic in proofBinder.Diagnostics)
                {
                    AddDiagnostic(diagnostic, template.Tree);
                }
                Report("TRM3007", "Generic function requires a body valid using only its declared Copy and operator constraints.",
                    template.Declaration.Span);
                return false;
            }

            var function = new BoundFunction(symbolicName, symbolicReturn,
                [.. proofBinder._variables],
                template.Parameters.Length, body, template.Declaration.Span) {
                RequiresUnsafeCaller = template.RequiresUnsafeCaller,
                DependencyContracts = template.DependencyContracts,
            };
            functions[symbolicIndex] = function;
            var proof = new BoundProgram(functions.ToImmutable(), -1) {
                Structures = [.. proofBinder._structs],
                EnumDefinitions = [.. proofBinder._enumDefinitions],
                NativeCoreRegistration = proofBinder._nativeCoreRegistration,
                NativeOutputCoreRegistration
                    = proofBinder._nativeOutputCoreRegistration,
                LayoutCoreRegistration = proofBinder._layoutCoreRegistration,
                CoreExceptionIdentitySelections =
                    proofBinder._coreExceptionIdentitySelections,
                NativeFunctionPointerSignatures = [.. proofBinder._nativeFunctionPointerSignatures],
                NativePointerTypes = [.. proofBinder._nativePointerTypes],
                NativeFixedArrayTypes = [.. proofBinder._nativeFixedArrayTypes],
                CheckedReferenceTypes = [.. proofBinder._checkedReferenceTypes],
                Classes = [.. proofBinder._classes],
                SourceInputCount = _trees.Length,
                RuntimePolicy = _runtimePolicy,
                GenericMethodSignatures = [.. _genericMethodSignatures],
                GenericMethodOperatorProtocols =
                    [.. _genericMethodOperatorProtocols],
            };
            if (!SemanticVerifier.TryVerifySource(proof, out _, out var span, out var limit, out var effect,
                allowGenericProofDependencies: true))
            {
                var code = effect switch {
                    "NoThrow" => "TRM3017",
                    "NormalReturn" => "TRM3012",
                    "FinallyReturn" => "TRM3019",
                    "FinallyNoReturn" => "TRM3020",
                    "LoopTransfer" => "TRM3008",
                    _ => limit ? "TRM3016" : "TRM3007",
                };
                Report(code, "Generic definition violates its declared type, control-flow, or effect contract.", span);
                return false;
            }
            var sourceInputOrdinal = _trees.IndexOf(template.Tree);
            if (sourceInputOrdinal < 0)
            {
                throw new InvalidOperationException(
                    "A generic method template has no source input.");
            }
            var formals = ImmutableArray.CreateBuilder<BoundGenericMethodFormal>(
                template.TypeParameters.Length);
            for (var formalIndex = 0;
                formalIndex < template.TypeParameters.Length; formalIndex++)
            {
                var formal = template.TypeParameters[formalIndex];
                var requirements = formal.Operations
                    .OrderBy(static pair => pair.Key.Operator)
                    .ThenBy(static pair => pair.Key.Arity)
                    .Select(pair =>
                        new BoundGenericMethodOperatorRequirement(
                            pair.Key.Operator, pair.Key.Arity,
                            pair.Value.Protocol, pair.Value.FixedResult,
                            pair.Value.ResultTypeParameterIndex,
                            _genericMethodOperatorProtocolOrdinals[
                                pair.Value.Protocol]))
                    .ToImmutableArray();
                formals.Add(new BoundGenericMethodFormal(formalIndex,
                    formal.Name, symbolicTypes[formalIndex],
                    formal.OwnershipDomain, formal.NullabilityDomain,
                    requirements) {
                    TypeDefault = typeDefaults[formalIndex],
                    RequiresBlittable = formal.RequiresBlittable,
                    RequiresWritable = formal.RequiresWritable,
                });
            }
            var signature = _genericMethodSignatures[
                template.DefinitionOrdinal];
            _genericMethodSignatures[template.DefinitionOrdinal]
                = signature with {
                    Formals = [
                        .. signature.Formals.Select((formal, ordinal) =>
                            formal with {
                                HasTypeDefault =
                                    typeDefaults[ordinal] is not null,
                                DefaultTypeParameterDependencies =
                                    typeDefaults[ordinal]
                                        ?.FormalDependencies ?? [],
                            }),
                    ],
                };
            _genericMethodDefinitions.Add(new BoundGenericMethodDefinition(
                template.DefinitionOrdinal, template.Name, template.IsPublic,
                template.RequiresUnsafeCaller, symbolicIndex,
                sourceInputOrdinal, template.Declaration.Span,
                formals.MoveToImmutable(), proof));
            _layoutKindsUsedInSource |= proofBinder._layoutKindsUsedInSource;
        }

        for (var definitionIndex = 0;
            definitionIndex < _genericMethodDefinitions.Count;
            definitionIndex++)
        {
            var definition = _genericMethodDefinitions[definitionIndex];
            var proofProgram = definition.ProofProgram with {
                GenericMethodSignatures = [.. _genericMethodSignatures],
            };
            _genericMethodDefinitions[definitionIndex] = definition with {
                ProofProgram = proofProgram,
            };
        }

        return true;
    }

    private static bool ContainsUnconstrainedGenericOptionalType(
        SyntaxNode node, HashSet<string> genericTypeNames)
    {
        var typeTokens = node.Children.OfType<SyntaxTokenElement>().ToArray();
        var identifiers = typeTokens.Where(static element =>
            element.Token.Kind == TokenKind.Identifier).ToArray();
        var hasOptionalSuffix = typeTokens.Length > 0
            && typeTokens[^1].Token.Kind == TokenKind.Question;
        if (node.Kind == SyntaxKind.TypeName && hasOptionalSuffix
            && identifiers.Length == 1
            && genericTypeNames.Contains(identifiers[0].Token.Name)
            && typeTokens.All(static element =>
                element.Token.Kind is TokenKind.Identifier
                    or TokenKind.WriteKeyword or TokenKind.GcKeyword
                    or TokenKind.Question))
        {
            return true;
        }
        if (node.Kind == SyntaxKind.TypeName
            && identifiers.Length == 1
            && identifiers[0].Token.Text == "option"
            && node.Children.OfType<SyntaxNode>().Any(child =>
                IsBareGenericTypeName(child, genericTypeNames)))
        {
            return true;
        }
        return node.Children.OfType<SyntaxNode>().Any(child =>
            ContainsUnconstrainedGenericOptionalType(child,
                genericTypeNames));
    }

    private static bool IsBareGenericTypeName(SyntaxNode node,
        HashSet<string> genericTypeNames)
    {
        if (node.Kind != SyntaxKind.TypeName
            || node.Children.OfType<SyntaxNode>().Any())
        {
            return false;
        }
        var identifiers = node.Children.OfType<SyntaxTokenElement>()
            .Where(static element =>
                element.Token.Kind == TokenKind.Identifier).ToArray();
        return identifiers.Length == 1
            && genericTypeNames.Contains(identifiers[0].Token.Name)
            && node.Children.OfType<SyntaxTokenElement>().Count() == 1;
    }

    private string Qualify(string name) => string.IsNullOrEmpty(_namespace) ? name : _namespace + "." + name;

    private bool TryResolveGenericArgument(SyntaxNode argument, out PrimitiveType type)
    {
        if (argument.Kind != SyntaxKind.TypeName)
        {
            type = default;
            return false;
        }
        var diagnosticCount = DiagnosticCount;
        type = BindType(argument, allowVoid: false);
        return DiagnosticCount == diagnosticCount;
    }

    private string? FindProtocol(string name)
    {
        if (name.Contains('.', StringComparison.Ordinal))
        {
            return _protocols.ContainsKey(name) ? name : null;
        }
        var candidate = Qualify(name);
        var hasCandidate = _protocols.ContainsKey(candidate);
        foreach (var imported in _usings)
        {
            var qualified = imported + "." + name;
            if (!_protocols.ContainsKey(qualified))
            {
                continue;
            }

            if (hasCandidate && candidate != qualified)
            {
                return null;
            }

            candidate = qualified;
            hasCandidate = true;
        }
        return hasCandidate ? candidate : null;
    }

    private static bool TryProtocolApplication(SyntaxNode type, out string head, out SyntaxNode[] arguments,
        out string[] argumentNames)
    {
        arguments = [.. type.Children.OfType<SyntaxNode>()];
        var typeName = NameText(type);
        var tokenIndex = 0;
        var headLength = 0;
        var open = -1;
        SyntaxToken? lastToken = null;
        foreach (var child in type.Children)
        {
            if (child is not SyntaxTokenElement tokenElement)
            {
                continue;
            }
            var token = tokenElement.Token;
            if (open < 0)
            {
                if (token.Kind == TokenKind.Less)
                {
                    open = tokenIndex;
                }
                else
                {
                    headLength += token.Name.Length;
                }
            }
            lastToken = token;
            tokenIndex++;
        }
        head = open > 0 ? typeName[..headLength] : "";
        if (type.Kind != SyntaxKind.TypeName || open <= 0 || arguments.Length is not (1 or 2 or 3)
            || lastToken?.Kind != TokenKind.Greater)
        {
            argumentNames = [];
            return false;
        }
        argumentNames = new string[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            argumentNames[index] = NameText(arguments[index]);
        }
        return typeName == head + "<" + string.Join(",", argumentNames) + ">";
    }

    private static bool MatchesProtocolArguments(StaticProtocol protocol, string[] argumentNames, string type,
        string? qualified = null)
    {
        var operandCount = protocol.Key.Arity - (protocol.HasSelf ? 1 : 0);
        if (argumentNames.Length != operandCount + 1)
        {
            return false;
        }
        for (var index = 0; index < operandCount; index++)
        {
            if (argumentNames[index] != type && argumentNames[index] != qualified)
            {
                return false;
            }
        }
        if (protocol.Key.Arity == 1)
        {
            return true;
        }
        var result = argumentNames[^1];
        return IsComparisonOperator(protocol.Key.Operator)
            ? result == "bool"
            : result == type || result == qualified;
    }
}
