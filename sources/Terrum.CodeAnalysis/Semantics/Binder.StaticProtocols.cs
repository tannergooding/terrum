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
    private readonly Dictionary<BoundGenericMethodImport, int>
        _genericMethodImportOrdinals = new(ReferenceEqualityComparer.Instance);
    private GenericInstantiation? _genericMethodTypeTermOwnerContext;
    private bool _hasGenericMethodTypeTermOwnerContext;

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
        internal PrimitiveType PlaceholderType { get; init; }
        internal byte OwnershipDomain { get; init; } = 1;
        internal byte NullabilityDomain { get; init; } = 1;
        internal SyntaxNode? TypeDefaultSyntax { get; init; }
        internal bool RequiresBlittable { get; init; }
        internal bool RequiresWritable { get; init; }
        internal ImmutableArray<BoundInterfaceType> InterfaceConstraints
            { get; init; } = [];
        internal ImmutableArray<int> DefaultTypeParameterDependencies
            { get; init; } = [];
    }

    private readonly record struct GenericMethodFormalSyntax(
        int Ordinal, int TypeFormalOrdinal, int ConstantFormalOrdinal,
        string Name, BoundGenericMethodFormalKind Kind,
        SyntaxNode? TypeDefault, SyntaxNode? ConstantDomain,
        SyntaxNode? ConstantDefault);

    internal sealed record GenericMethodConstantFormal(
        int Ordinal, int ConstantOrdinal, string Name,
        BoundGenericMethodTypeTerm Domain, PrimitiveType? PlaceholderType,
        SyntaxNode? DefaultSyntax,
        ImmutableArray<int> DefaultFormalDependencies)
    {
        internal int ProofParameterIndex { get; init; } = -1;
        internal ImmutableArray<int> DefaultTypeFormalDependencies
            { get; init; } = [];
    }

    private sealed record GenericMethodCallArguments(
        PrimitiveType?[] TypeArguments,
        bool[] RequireTypeInference,
        bool[] ExplicitTypeDefaults,
        SyntaxNode?[] ConstantArguments,
        bool[] RequireConstantInference,
        bool[] ExplicitConstantDefaults)
    {
        internal BoundGenericMethodConstantArgument?[]
            InferredConstantArguments { get; init; } = [];
        internal BoundGenericMethodConstantArgument?[]
            PreboundConstantArguments { get; init; } = [];
        internal PrimitiveType?[] InferredTypeDefaultArguments
            { get; init; } = [];
    }

    internal sealed record GenericParameter(string Name, PrimitiveType? ScalarType, int TypeParameterIndex,
        bool IsOut, bool IsInit)
    {
        internal CheckedReferenceTypeDefinition? ReferenceTypePattern
        { get; init; }
        internal SyntaxNode? TypeTermSyntax { get; init; }
        internal BoundGenericMethodTypeTerm? TypeTerm { get; init; }
        internal SyntaxNode? DefaultSyntax { get; init; }
        internal BoundExpression? DefaultExpression { get; init; }
        internal BoundConstant? DefaultValue { get; init; }
        internal ImmutableArray<
            BoundGenericMethodParameterDefaultFormalDependency>
            DefaultFormalDependencies
            { get; init; } = [];

        internal PrimitiveType Resolve(ImmutableArray<PrimitiveType> types) =>
            ScalarType ?? types[TypeParameterIndex];
    }
    internal sealed record GenericTemplate(string Name, ImmutableArray<GenericTypeParameter> TypeParameters,
        PrimitiveType? ScalarReturnType, int ReturnTypeParameterIndex, ImmutableArray<GenericParameter> Parameters,
        SyntaxNode Body, SyntaxNode Declaration, SyntaxTree Tree)
    {
        internal int DefinitionOrdinal { get; init; } = -1;
        internal int FormalTypeOwnerOrdinal { get; init; } = -1;
        internal int SourceGenericTypeDefinitionIndex { get; init; } = -1;
        internal bool IsPublic { get; init; }
        internal bool RequiresUnsafeCaller { get; init; }
        internal bool DoesNotReturn { get; init; }
        internal bool IsStatic { get; init; } = true;
        internal bool IsMutable { get; init; }
        internal string? MemberName { get; init; }
        internal string? ContainingType { get; init; }
        internal string? OwnerName { get; init; }
        internal PrimitiveType? OwnerType { get; init; }
        internal int OwnerGenericTypeDefinitionIndex { get; init; } = -1;
        internal ImmutableArray<PrimitiveType> OwnerTypeArguments
            { get; init; } = [];
        internal ImmutableArray<BoundGenericMethodConstantArgument>
            OwnerConstantArguments { get; init; } = [];
        internal ImmutableDictionary<string, PrimitiveType>
            OwnerTypeArgumentMap { get; init; }
                = [];
        internal CheckedReferenceTypeDefinition?
            ReferenceReturnTypePattern { get; init; }
        internal SyntaxNode? ReturnTypeTermSyntax { get; init; }
        internal BoundGenericMethodTypeTerm? ReturnTypeTerm
            { get; init; }
        internal BoundEvaluationFormSchemaBinding?
            PureEvaluationFormSchema { get; init; }
        internal BoundEvaluationFormSchemaBinding?
            ConstEvalSchema { get; init; }
        internal ImmutableArray<BoundDependencyContract>
            DependencyContracts { get; init; } = [];
        internal ImmutableArray<SourceDeclarationContractClause>
            SourceDeclarationContractClauses { get; init; } = [];
        internal ImmutableArray<BoundDeclarationContractClause>
            BoundDeclarationContractClauses { get; init; } = [];
        internal ImmutableArray<GenericMethodConstantFormal> ConstantFormals
            { get; init; } = [];
        internal ImmutableArray<BoundGenericMethodFormalSlot> FormalOrder
            { get; init; } = [];
        internal BoundGenericMethodImport? ImportedMethod { get; init; }

        internal PrimitiveType ReturnType(ImmutableArray<PrimitiveType> types) =>
            ScalarReturnType
            ?? (ReturnTypeTerm is null
                ? types[ReturnTypeParameterIndex]
                : throw new InvalidOperationException(
                    "A generic return type term requires substitution."));

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

    internal sealed record GenericInstantiation(
        GenericTemplate Template, ImmutableArray<PrimitiveType> Types)
    {
        internal ImmutableArray<BoundGenericMethodConstantArgument>
            ConstantArguments { get; init; } = [];
    }

    private static ImmutableArray<BoundGenericTypeFormalOwner>
        CreateGenericMethodTypeFormalOwners(
        CompilationReference? reference,
        IEnumerable<BoundGenericMethodDefinition>? definitions = null,
        BoundGenericTypeFormalOwner? additionalOwner = null)
    {
        var owners = ImmutableArray.CreateBuilder<
            BoundGenericTypeFormalOwner>();
        var visitedImports = new HashSet<BoundGenericMethodImport>(
            ReferenceEqualityComparer.Instance);
        if (reference is not null)
        {
            foreach (var import in reference.GenericMethodImports)
            {
                VisitImport(import);
            }
            foreach (var function in reference.Functions)
            {
                VisitFunction(function);
            }
        }
        if (definitions is not null)
        {
            foreach (var definition in definitions)
            {
                owners.Add(CreateGenericMethodTypeFormalOwner(
                    definition.FormalTypeOwnerOrdinal,
                    definition.Formals));
            }
        }
        if (additionalOwner is BoundGenericTypeFormalOwner owner)
        {
            owners.Add(owner);
        }

        return owners.ToImmutable();

        void VisitImport(BoundGenericMethodImport import)
        {
            if (!visitedImports.Add(import))
            {
                return;
            }

            owners.Add(CreateGenericMethodTypeFormalOwner(
                import.FormalTypeOwnerOrdinal, import.Formals));
            if (import.Binding is BoundGenericMethodImportBinding binding)
            {
                VisitImport(binding.Expected);
                VisitImport(binding.Provider);
            }

            VisitFunction(import.ProofFunction);
            foreach (var function in import.ProofSupportFunctions)
            {
                VisitFunction(function);
            }
        }

        void VisitFunction(BoundFunction function)
        {
            if (function.ImportedGenericMethod
                is BoundGenericMethodImport imported)
            {
                VisitImport(imported);
            }
            if (!BoundTreeTraversal.VisitExpressions(function.Body,
                    expression =>
                    {
                        if (expression is BoundCall {
                                ImportedGenericMethodCall:
                                    BoundGenericMethodImport importedCall,
                            })
                        {
                            VisitImport(importedCall);
                        }
                    }))
            {
                throw new InvalidOperationException(
                    "A generic MethodDef proof Body cannot be traversed to collect formal owners.");
            }
        }

    }

    private static BoundGenericTypeFormalOwner
        CreateGenericMethodTypeFormalOwner(int ordinal,
            ImmutableArray<BoundGenericMethodFormal> formals) =>
        new(ordinal, [.. formals.Select(static formal =>
            new BoundGenericTypeFormal(formal.Ordinal,
                formal.PlaceholderType, formal.OwnershipDomain,
                formal.NullabilityDomain) {
                RequiresWritable = formal.RequiresWritable,
                RequiresBlittable = formal.RequiresBlittable,
            })]);

    private ImmutableArray<BoundFunction>.Builder? _genericProofFunctions;
    private Binder? _genericProofSource;
    private bool _hasValidatedGenericTemplates;
    private bool _isValidatingGenericTemplates;

    private PrimitiveType? HydrateProofType(
        PrimitiveType sourceType,
        bool allowFloatingScalar = false,
        bool allowFloatingStructFields = false,
        bool allowUnique = false)
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
                    if (HydrateProofType(field.Type,
                        allowFloatingScalar: allowFloatingStructFields,
                        allowFloatingStructFields: allowFloatingStructFields,
                        allowUnique: allowUnique)
                        is not PrimitiveType fieldType)
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
        if (BoundSpanTypeTypes.IsType(sourceType))
        {
            var sourceIndex = BoundSpanTypeTypes.Index(sourceType);
            if ((uint)sourceIndex >= (uint)source._spanTypes.Count)
            {
                return null;
            }

            var sourceSpan = source._spanTypes[sourceIndex];
            var genericContext = _genericContext
                is GenericInstantiation capturedContext
                ? capturedContext : null;
            var elementFormalOrdinal = -1;
            if (genericContext is GenericInstantiation spanContext)
            {
                if (!TryGetGenericMethodSpanFormalOrdinal(sourceSpan,
                        spanContext, out elementFormalOrdinal))
                {
                    return null;
                }
            }
            else if (sourceSpan.ElementFormalDefinitionOrdinal != -1)
            {
                return null;
            }

            if (HydrateProofType(sourceSpan.ElementType,
                    allowFloatingScalar, allowFloatingStructFields,
                    allowUnique) is not PrimitiveType elementType)
            {
                return null;
            }

            var elementFormalDefinitionOrdinal = -1;
            if (elementFormalOrdinal >= 0
                && genericContext is GenericInstantiation selectedContext)
            {
                if (selectedContext.Template.DefinitionOrdinal >= 0)
                {
                    elementFormalDefinitionOrdinal
                        = selectedContext.Template.DefinitionOrdinal;
                }
                else if (selectedContext.Template.ImportedMethod
                    is BoundGenericMethodImport imported
                    && imported.FormalTypeOwnerOrdinal >= 0)
                {
                    elementFormalDefinitionOrdinal
                        = checked(
                            BoundSpanTypeContract
                                .ImportedFormalDefinitionOrdinal
                            - imported.FormalTypeOwnerOrdinal);
                }
                else
                {
                    return null;
                }
            }

            var isElementWritable = sourceSpan.IsElementWritable
                && elementFormalOrdinal >= 0;
            if (sourceSpan.IsElementWritable && !isElementWritable
                && !IsWritableGenericMethodTypeArgument(elementType))
            {
                return null;
            }

            return GetSpanType(elementType,
                elementFormalDefinitionOrdinal, elementFormalOrdinal,
                isElementWritable, sourceSpan.IsWritable,
                sourceSpan.IsInitialization, default);
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
        if (CheckedReferenceTypes.IsType(sourceType))
        {
            var sourceIndex = CheckedReferenceTypes.Index(sourceType);
            if ((uint)sourceIndex >= (uint)source._checkedReferenceTypes.Count)
            {
                return null;
            }

            var sourceReference = source._checkedReferenceTypes[sourceIndex];
            if (HydrateProofType(sourceReference.ReferentType,
                    allowFloatingScalar, allowFloatingStructFields,
                    allowUnique) is not PrimitiveType referentType)
            {
                return null;
            }

            return GetCheckedReferenceType(
                sourceReference with { ReferentType = referentType },
                default);
        }
        if (NativePointerTypes.IsType(sourceType))
        {
            return (uint)NativePointerTypes.Index(sourceType)
                < (uint)_nativePointerTypes.Count
                ? sourceType : null;
        }
        if (NativeFunctionPointerTypes.IsType(sourceType))
        {
            return (uint)NativeFunctionPointerTypes.Index(sourceType)
                < (uint)_nativeFunctionPointerSignatures.Count
                ? sourceType : null;
        }
        if (NativeFixedArrayTypes.IsType(sourceType))
        {
            return (uint)NativeFixedArrayTypes.Index(sourceType)
                < (uint)_nativeFixedArrayTypes.Count
                ? sourceType : null;
        }
        if (BoundGenericNominalTypeTypes.IsType(sourceType))
        {
            return (uint)BoundGenericNominalTypeTypes.Index(sourceType)
                < (uint)_genericNominalTypeInstances.Count
                ? sourceType : null;
        }
        if (!source.IsStruct(sourceType))
        {
            return sourceType is PrimitiveType.Void or PrimitiveType.Bool
                or PrimitiveType.Char16 or PrimitiveType.Int8
                or PrimitiveType.UInt8 or PrimitiveType.Int16
                or PrimitiveType.UInt16 or PrimitiveType.Int32
                or PrimitiveType.UInt32 or PrimitiveType.Int64
                or PrimitiveType.UInt64 or PrimitiveType.NativeInt
                or PrimitiveType.NativeUInt or PrimitiveType.Int128
                or PrimitiveType.UInt128
                || (allowFloatingScalar && sourceType is PrimitiveType.Float32 or PrimitiveType.Float64)
                ? sourceType : null;
        }
        var structure = source._structs[(int)sourceType - 100];
        if (_structNames.TryGetValue(structure.Name, out var existing))
        {
            return existing;
        }
        if (structure.IsUnique && !allowUnique)
        {
            return null;
        }
        var fields = ImmutableArray.CreateBuilder<BoundStructField>(structure.Fields.Length);
        foreach (var field in structure.Fields)
        {
            if (HydrateProofType(field.Type, allowFloatingScalar: allowFloatingStructFields,
                allowFloatingStructFields: allowFloatingStructFields,
                allowUnique: allowUnique) is not PrimitiveType fieldType)
            {
                return null;
            }
            fields.Add(field with { Type = fieldType });
        }
        ImmutableArray<PrimitiveType> proofOwnerTypeArguments = [];
        ImmutableArray<BoundGenericMethodConstantArgument>
            proofOwnerConstantArguments = [];
        if (structure.SourceGenericTypeDefinitionIndex >= 0)
        {
            var sourceStructureIndex = (int)sourceType - 100;
            var sourceInstance = source._sourceGenericTypeInstances
                .SingleOrDefault(instance =>
                    instance.StructureIndex == sourceStructureIndex);
            if (sourceInstance is null
                || sourceInstance.DefinitionIndex
                    != structure.SourceGenericTypeDefinitionIndex
                || sourceInstance.TypeArguments.IsDefault
                || sourceInstance.ConstantArguments.IsDefault)
            {
                return null;
            }

            var typeArguments = ImmutableArray.CreateBuilder<PrimitiveType>(
                sourceInstance.TypeArguments.Length);
            foreach (var argument in sourceInstance.TypeArguments)
            {
                if (HydrateProofType(argument,
                    allowFloatingScalar: true,
                    allowFloatingStructFields: true,
                    allowUnique: true) is not PrimitiveType proofArgument)
                {
                    return null;
                }
                typeArguments.Add(proofArgument);
            }
            proofOwnerTypeArguments = typeArguments.MoveToImmutable();
            var constantArguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodConstantArgument>(
                    sourceInstance.ConstantArguments.Length);
            foreach (var argument in sourceInstance.ConstantArguments)
            {
                if (!TryHydrateGenericMethodConstantArgument(this, argument,
                    out var proofArgument))
                {
                    return null;
                }
                constantArguments.Add(proofArgument);
            }
            proofOwnerConstantArguments = constantArguments.MoveToImmutable();
        }
        var result = (PrimitiveType)(100 + _structs.Count);
        _structNames.Add(structure.Name, result);
        _structs.Add(structure with { Fields = fields.MoveToImmutable() });
        if (structure.SourceGenericTypeDefinitionIndex >= 0)
        {
            _sourceGenericTypeInstances.Add(
                new BoundSourceGenericTypeInstance((int)result - 100,
                    structure.SourceGenericTypeDefinitionIndex,
                    proofOwnerTypeArguments) {
                    ConstantArguments = proofOwnerConstantArguments,
                });
        }
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
        "nint" => PrimitiveType.NativeInt,
        "nuint" => PrimitiveType.NativeUInt,
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
            || IsCopySpanType(type)
            || IsCopyNativeFixedArrayType(type)
            || IsStruct(type)
            || (ManagedClassTypes.IsManaged(type)
                && ((int)type < ManagedClassTypes.First
                    || ((uint)ManagedClassTypes.ClassIndex(type)
                            < (uint)_classes.Count
                        && _classes[ManagedClassTypes.ClassIndex(type)]
                            .IsUnique == false)))
            || IsCopyGenericNominalType(type)
            || (CheckedReferenceTypeContract.IsValidType(type,
                    checkedReferenceTypes)
                && CheckedReferenceTypeContract.IsSupportedReferenceStoreValueType(
                    type, _structs, _classes, _enumDefinitions.Count,
                    checkedReferenceTypes, _nativePointerTypes));
    }

    private bool IsCopySpanType(PrimitiveType type) =>
        BoundSpanTypeTypes.IsValidType(type, _spanTypes)
        && !_spanTypes[BoundSpanTypeTypes.Index(type)].IsInitialization;

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
        if (definition is null
                || definition.NominalKind != 1
                || definition.TransparentConstantCarrierType is null
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
            or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.NativeInt or PrimitiveType.NativeUInt
            or PrimitiveType.Float32 or PrimitiveType.Float64
        || (IsStruct(type) && !_structs[(int)type - 100].IsUnique);

    private bool IsCopyGenericArgumentType(PrimitiveType type) =>
        !IsUniqueGenericArgumentType(type)
        && (IsDefaultCopyGenericMethodFormalType(type)
            || type is PrimitiveType.Float32 or PrimitiveType.Float64
            || IsCopyGenericType(type));

    private bool IsDefaultCopyGenericMethodFormalType(PrimitiveType type)
    {
        if (_genericContext is not GenericInstantiation context)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < context.Types.Length; ordinal++)
        {
            if (context.Types[ordinal] == type
                && context.Template.TypeParameters[ordinal]
                    .OwnershipDomain == 1)
            {
                return true;
            }
        }

        return false;
    }

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
            var implementingType = (PrimitiveType)(100 + index);
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
                if (protocolName is null)
                {
                    if (!TryBindInterfaceConformanceType(name, index,
                            out var interfaceType))
                    {
                        Report("TRM3007",
                            "Conformance must name a declared interface instance.",
                            name.Span);
                        continue;
                    }
                    if (_pendingInterfaceConformances.Any(conformance =>
                            conformance.ImplementingType
                                == implementingType
                            && BoundInterfaceContract.AreEquivalent(
                                conformance.InterfaceType, interfaceType)))
                    {
                        Report("TRM3007",
                            "A type cannot declare the same complete interface conformance more than once.",
                            name.Span);
                        continue;
                    }
                    var sourceInputOrdinal = _trees.IndexOf(tree);
                    if (sourceInputOrdinal < 0)
                    {
                        throw new InvalidOperationException(
                            "A source conformance has no input ordinal.");
                    }
                    _pendingInterfaceConformances.Add(
                        new PendingInterfaceConformance(implementingType,
                            interfaceType, tree, declaration, name,
                            sourceInputOrdinal));
                    continue;
                }
                if (!MatchesProtocolArguments(_protocols[protocolName],
                    partNames, simpleTypeName, typeName))
                {
                    Report("TRM3007",
                        "Conformance requires a distinct declared local protocol with exact operand and result types.",
                        name.Span);
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

        foreach (var (index, (tree, declaration)) in _classDeclarations)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SetContext(tree, declaration);
            var classType = _classes[index];
            var classTypeName = _classNames[classType.Name];
            var list = declaration.Children.OfType<SyntaxNode>()
                .FirstOrDefault(static child =>
                    child.Kind == SyntaxKind.ConformanceList);
            if (list is null)
            {
                continue;
            }

            foreach (var conformance in list.Children.OfType<SyntaxNode>())
            {
                var shape = TryProtocolApplication(conformance,
                    out var head, out _, out _);
                if (shape && FindProtocol(head) is not null)
                {
                    Report("TRM3007",
                        "Static operator witnesses require a struct implementation.",
                        conformance.Span);
                    continue;
                }
                if (!TryBindInterfaceConformanceType(conformance,
                    new Dictionary<string, int>(StringComparer.Ordinal),
                    [], out var interfaceType))
                {
                    Report("TRM3007",
                        "Conformance must name a declared interface instance.",
                        conformance.Span);
                    continue;
                }
                if (_pendingInterfaceConformances.Any(existing =>
                        existing.ImplementingType == classTypeName
                        && BoundInterfaceContract.AreEquivalent(
                            existing.InterfaceType, interfaceType)))
                {
                    Report("TRM3007",
                        "A type cannot declare the same complete interface conformance more than once.",
                        conformance.Span);
                    continue;
                }
                var sourceInputOrdinal = _trees.IndexOf(tree);
                if (sourceInputOrdinal < 0)
                {
                    throw new InvalidOperationException(
                        "A source conformance has no input ordinal.");
                }
                _pendingInterfaceConformances.Add(
                    new PendingInterfaceConformance(classTypeName,
                        interfaceType, tree, declaration, conformance,
                        sourceInputOrdinal));
            }
        }
    }

    private bool TryBindInterfaceConformanceType(
        SyntaxNode node, int structureIndex,
        out BoundInterfaceType interfaceType)
    {
        if ((uint)structureIndex >= (uint)_structs.Count)
        {
            interfaceType = null!;
            return false;
        }
        return TryBindInterfaceConformanceType(node,
            (PrimitiveType)(100 + structureIndex), out interfaceType);
    }

    private bool TryBindInterfaceConformanceType(
        SyntaxNode node, PrimitiveType implementingType,
        out BoundInterfaceType interfaceType)
    {
        interfaceType = null!;
        if (!TryGetInterfaceConformanceOwnerContext(implementingType,
            out var ownerTypeParameterMap, out var ownerArguments))
        {
            return false;
        }
        return TryBindInterfaceConformanceType(node,
            ownerTypeParameterMap, ownerArguments, out interfaceType);
    }

    private bool TryGetInterfaceConformanceOwnerContext(
        PrimitiveType implementingType,
        out Dictionary<string, int> ownerTypeParameterMap,
        out ImmutableArray<BoundInterfaceTypeArgument> ownerArguments)
    {
        ownerTypeParameterMap = new Dictionary<string, int>(
            StringComparer.Ordinal);
        ownerArguments = [];
        if (!IsStruct(implementingType))
        {
            return ManagedClassTypes.IsClass(implementingType)
                && (uint)ManagedClassTypes.ClassIndex(implementingType)
                    < (uint)_classes.Count;
        }

        var structureIndex = (int)implementingType - 100;
        if ((uint)structureIndex >= (uint)_structs.Count)
        {
            return false;
        }
        var definitionIndex = _structs[structureIndex]
            .SourceGenericTypeDefinitionIndex;
        if (definitionIndex < 0)
        {
            return true;
        }
        if ((uint)definitionIndex
            >= (uint)_sourceGenericTypeDefinitions.Count)
        {
            return false;
        }
        var definition = _sourceGenericTypeDefinitions[definitionIndex];
        var instance = _sourceGenericTypeInstances.SingleOrDefault(
            candidate => candidate.StructureIndex == structureIndex);
        if (definition.Formals.IsDefault
            || instance is null
            || instance.DefinitionIndex != definitionIndex
            || instance.TypeArguments.IsDefault
            || instance.TypeArguments.Length != definition.Formals.Length)
        {
            return false;
        }
        foreach (var formal in definition.Formals)
        {
            if (!ownerTypeParameterMap.TryAdd(formal.Name, formal.Ordinal))
            {
                return false;
            }
        }
        ownerArguments = [
            .. instance.TypeArguments.Select(
                static argument => BoundInterfaceTypeArgument.ForType(
                    ConcreteInterfaceType(argument))),
        ];
        return true;
    }

    private bool TryBindInterfaceConformanceType(
        SyntaxNode node,
        Dictionary<string, int> ownerTypeParameterMap,
        ImmutableArray<BoundInterfaceTypeArgument> ownerArguments,
        out BoundInterfaceType interfaceType)
    {
        interfaceType = null!;
        var diagnosticCount = DiagnosticCount;
        if (BindInterfaceType(node, ownerTypeParameterMap,
                _interfaceDeclarationGroups, ownerTypeParameterMap.Count)
                is not BoundInterfaceType {
                    Kind: BoundInterfaceTypeKind.Nominal,
                } boundInterface
            || DiagnosticCount != diagnosticCount)
        {
            return false;
        }

        interfaceType = ownerArguments.IsEmpty
            ? boundInterface
            : BoundInterfaceContract.SubstituteTypeParameters(
                boundInterface, ownerArguments);
        return !interfaceType.IsWritable && !interfaceType.IsOptional
            && !interfaceType.IsGarbageCollected
            && !interfaceType.IsInitialization
            && interfaceType.TypeArguments.All(static argument =>
            argument.Kind != BoundInterfaceTypeArgumentKind.ExistentialType);
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
        ValidateInterfaceConformances();
    }

    private static ImmutableArray<GenericMethodFormalSyntax>
        ParseGenericMethodFormals(SyntaxNode typeParameters,
            out bool valid)
    {
        var formals = ImmutableArray.CreateBuilder<GenericMethodFormalSyntax>();
        var current = new List<SyntaxElement>();
        var typeFormalOrdinal = 0;
        var constantFormalOrdinal = 0;
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
            if (current.Count is 3 or 5
                && current[0] is SyntaxTokenElement {
                    Token.Kind: TokenKind.Identifier,
                    Token.Text: "const",
                }
                && current[1] is SyntaxNode constantDomain
                && IsTypeSyntax(constantDomain)
                && current[2] is SyntaxTokenElement {
                    Token.Kind: TokenKind.Identifier,
                } constantName
                && (current.Count == 3
                    || (current[3] is SyntaxTokenElement {
                            Token.Kind: TokenKind.Equals,
                        }
                        && current[4] is SyntaxNode)))
            {
                var ordinal = formals.Count;
                formals.Add(new GenericMethodFormalSyntax(ordinal, -1,
                    constantFormalOrdinal, constantName.Token.Name,
                    BoundGenericMethodFormalKind.Constant, null,
                    constantDomain,
                    current.Count == 5
                        ? (SyntaxNode)current[4] : null));
                constantFormalOrdinal++;
                return true;
            }
            if (current.Count == 1
                && current[0] is SyntaxTokenElement {
                    Token.Kind: TokenKind.Identifier,
                } name)
            {
                formals.Add(new GenericMethodFormalSyntax(formals.Count,
                    typeFormalOrdinal, -1, name.Token.Name,
                    BoundGenericMethodFormalKind.Type, null, null, null));
                typeFormalOrdinal++;
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
                formals.Add(new GenericMethodFormalSyntax(formals.Count,
                    typeFormalOrdinal, -1, defaultedName.Token.Name,
                    BoundGenericMethodFormalKind.Type, defaultType, null,
                    null));
                typeFormalOrdinal++;
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

    private void DeclareGeneric(SyntaxNode declaration,
        SyntaxNode typeParameters, string? containingType = null)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        PrimitiveType? ownerType = null;
        var ownerTypeName = containingType is null
            ? null : Qualify(containingType);
        var ownerTypeMap = _genericOwnerTypeArguments
            ?? [];
        var ownerTypeDefinitionIndex = -1;
        ImmutableArray<PrimitiveType> ownerTypeArguments = [];
        if (ownerTypeName is not null)
        {
            if (!_structNames.TryGetValue(ownerTypeName,
                    out var resolvedOwnerType)
                && !_classNames.TryGetValue(ownerTypeName,
                    out resolvedOwnerType))
            {
                Report("TRM3005",
                    "A generic method owner must be a closed declared type.",
                    declaration.Span);
                return;
            }

            ownerType = resolvedOwnerType;
            if (IsStruct(resolvedOwnerType))
            {
                var ownerStructureIndex = (int)resolvedOwnerType - 100;
                ownerTypeDefinitionIndex = _structs[
                    ownerStructureIndex].SourceGenericTypeDefinitionIndex;
                if (ownerTypeDefinitionIndex >= 0)
                {
                    var ownerInstance = _sourceGenericTypeInstances
                        .SingleOrDefault(instance =>
                            instance.StructureIndex
                                == ownerStructureIndex);
                    if (ownerInstance is null
                        || ownerInstance.DefinitionIndex
                            != ownerTypeDefinitionIndex
                        || ownerInstance.TypeArguments.IsDefault
                        || ownerTypeMap.Count
                            != ownerInstance.TypeArguments.Length)
                    {
                        Report("TRM3005",
                            "A generic method owner has no complete closed type argument mapping.",
                            declaration.Span);
                        return;
                    }

                    ownerTypeArguments = ownerInstance.TypeArguments;
                }
                else if (ownerTypeMap.Count != 0)
                {
                    Report("TRM3005",
                        "A nongeneric method owner cannot retain generic type arguments.",
                        declaration.Span);
                    return;
                }
            }
        }
        var ownerTypeParameterMap = ownerTypeDefinitionIndex >= 0
            ? _sourceGenericTypeDefinitions[
                    ownerTypeDefinitionIndex].Formals.ToDictionary(
                static formal => formal.Name,
                static formal => formal.Ordinal,
                StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);

        var children = declaration.Children.OfType<SyntaxNode>().ToArray();
        var methodFormals = ParseGenericMethodFormals(typeParameters,
            out var typeFormalsValid);
        var typeFormals = methodFormals
            .Where(static formal =>
                formal.Kind == BoundGenericMethodFormalKind.Type)
            .ToImmutableArray();
        var typeNames = typeFormals.Select(static formal => formal.Name)
            .ToArray();
        var hasPrimitiveTypeParameter = false;
        var formalNames = new HashSet<string>(StringComparer.Ordinal);
        var hasDuplicateTypeParameter = methodFormals
            .Any(formal => !formalNames.Add(formal.Name));
        var typeParameterIndices = typeNames.Length is >= 2 and <= 8
            ? new Dictionary<string, int>(StringComparer.Ordinal) : null;
        var methodTypeParameterMap = new Dictionary<string, int>(
            StringComparer.Ordinal);
        for (var index = 0; index < typeNames.Length; index++)
        {
            var typeName = typeNames[index];
            _ = methodTypeParameterMap.TryAdd(typeName,
                typeFormals[index].Ordinal);
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
        var doesNotReturn = false;
        BoundEvaluationFormSchemaBinding? pureSchemaBinding = null;
        BoundEvaluationFormSchemaBinding? constEvalSchemaBinding = null;
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
                var returnTarget = child.Children.Length > 2
                    && child.Children[1] is SyntaxTokenElement {
                        Token.Kind: TokenKind.ReturnKeyword,
                    }
                    && child.Children[2] is SyntaxTokenElement {
                        Token.Kind: TokenKind.Colon,
                    };
                if (NameText(child) == "[NoThrow]")
                {
                    Report("TRM3017", "The general NoThrow source contract is retired.", child.Span);
                    hasRetiredNoThrow = true;
                }
                else if (NameText(child) == "[DoesNotReturn]"
                    && !doesNotReturn)
                {
                    doesNotReturn = true;
                }
                else if (!returnTarget && constEvalSchemaBinding is null
                    && TryGetSelectedEvaluationFormSchemaBinding(
                        AttributeName(child), constEval: true,
                        out var selectedConstEvalSchema)
                    && HasNoAttributeArguments(child))
                {
                    constEvalSchemaBinding = selectedConstEvalSchema;
                }
                else if (!returnTarget && pureSchemaBinding is null
                    && TryGetSelectedEvaluationFormSchemaBinding(
                        AttributeName(child), constEval: false,
                        out var selectedPureSchema)
                    && HasNoAttributeArguments(child))
                {
                    pureSchemaBinding = selectedPureSchema;
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
        var staticModifierCount = 0;
        var mutableModifierCount = 0;
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
            if (tokenKind == TokenKind.StaticKeyword)
            {
                staticModifierCount++;
            }
            if (tokenKind == TokenKind.MutKeyword)
            {
                mutableModifierCount++;
            }
            hasInvalidModifier |= tokenKind is TokenKind.PrivateKeyword
                or TokenKind.UniqueKeyword or TokenKind.ExternKeyword
                || (ownerType is null
                    && tokenKind is TokenKind.StaticKeyword
                        or TokenKind.MutKeyword);
        }
        var isStatic = ownerType is null || staticModifierCount != 0;
        var isMutable = mutableModifierCount != 0;
        var containingTypeName = ownerType is PrimitiveType closedOwner
            ? IsStruct(closedOwner)
                ? _structs[(int)closedOwner - 100].Name
                : _classes[ManagedClassTypes.ClassIndex(closedOwner)].Name
            : null;
        var qualified = containingTypeName is null
            ? Qualify(nameToken?.Name
                ?? throw new InvalidOperationException())
            : containingTypeName + "."
                + (nameToken?.Name
                    ?? throw new InvalidOperationException());
        var arguments = parameters?.Children.OfType<SyntaxNode>().ToArray() ?? [];
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        var templateParameters = ImmutableArray.CreateBuilder<GenericParameter>(arguments.Length);
        var operations = typeNames.Select(static _ =>
            ImmutableDictionary.CreateBuilder<StaticOperatorKey, GenericOperatorConstraint>()).ToArray();
        var interfaceConstraints = typeNames.Select(static _ =>
            ImmutableArray.CreateBuilder<BoundInterfaceType>()).ToArray();
        var methodConstantParameters = new Dictionary<string,
            InterfaceConstantFormal>(StringComparer.Ordinal);
        foreach (var constantFormal in methodFormals.Where(static formal =>
            formal.Kind == BoundGenericMethodFormalKind.Constant))
        {
            if (constantFormal.ConstantDomain is not SyntaxNode domainSyntax
                || domainSyntax.Kind != SyntaxKind.TypeName)
            {
                continue;
            }

            var diagnosticCount = DiagnosticCount;
            if (BindInterfaceType(domainSyntax, ownerTypeParameterMap,
                    _interfaceDeclarationGroups, ownerTypeParameterMap.Count,
                    methodTypeParameterMap, methodFormals.Length,
                    methodConstantParameters: methodConstantParameters,
                    allowGenericNominalConstantDomain: true,
                    isGenericNominalConstantDomain: true)
                is BoundInterfaceType constantDomain
                && DiagnosticCount == diagnosticCount)
            {
                methodConstantParameters.Add(constantFormal.Name,
                    new InterfaceConstantFormal(constantDomain,
                        BoundInterfaceTypeParameterScope.Member,
                        constantFormal.Ordinal));
            }
        }
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
        SyntaxNode? returnTypeTermSyntax = null;
        CheckedReferenceTypeDefinition? referenceReturnPattern = null;
        var hasInvalidReferenceReturnPattern = false;
        if (isReferenceReturn)
        {
            if (genericReturnNode is not { Kind: SyntaxKind.TypeName })
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
                else if (returnIndex >= 0)
                {
                    referenceReturnPattern =
                        new CheckedReferenceTypeDefinition(
                            PrimitiveType.Void, isWritable,
                            isGarbageCollected, isInitialization,
                            isOptional);
                }
                else if (ownerTypeMap.TryGetValue(returnName,
                    out var ownerReferentType))
                {
                    scalarReturn = GetCheckedReferenceType(
                        new CheckedReferenceTypeDefinition(
                            ownerReferentType, isWritable,
                            isGarbageCollected, isInitialization,
                            isOptional), returnTypeSyntax.Span);
                }
                else
                {
                    hasInvalidReferenceReturnPattern = true;
                }
            }
        }
        else if (returnIndex < 0
            && ownerTypeMap.TryGetValue(returnName,
                out var ownerReturnType))
        {
            scalarReturn ??= ownerReturnType;
        }
        else if (!isReferenceReturn && returnIndex < 0
            && scalarReturn is null)
        {
            returnTypeTermSyntax = genericReturnNode;
        }
        var requiresUnsafeCaller = unsafeModifierCount != 0;
        var typeDefaultsAreOrdered = typeFormals.Select((formal, ordinal) =>
            GenericTypeDefaultDependencies(formal.TypeDefault, typeNames)
                .All(dependency => dependency < ordinal))
            .All(static isValid => isValid);
        var valid = methodFormals.Length is >= 1 and <= 8
            && typeFormalsValid
            && !hasDuplicateTypeParameter
            && typeDefaultsAreOrdered
            && returnType is not null && body is not null
            && (returnIndex >= 0 || scalarReturn is not null
                || returnTypeTermSyntax is not null)
            && arguments.Length <= 8
            && unsafeModifierCount <= 1
            && publicModifierCount <= 1
            && staticModifierCount <= 1
            && mutableModifierCount <= 1
            && !(isStatic && isMutable)
            && !hasInvalidModifier
            && !hasInvalidAttribute
            && !hasInvalidReferenceReturnPattern;
        foreach (var argument in arguments)
        {
            var defaultSyntax = argument.Children.Length >= 4
                && argument.Children[^2] is SyntaxTokenElement {
                    Token.Kind: TokenKind.Equals,
                }
                ? argument.Children[^1] as SyntaxNode : null;
            var parameterFormChildren = defaultSyntax is null
                ? argument.Children
                : argument.Children.Slice(0,
                    argument.Children.Length - 2);
            var isOut = parameterFormChildren is [SyntaxTokenElement { Token.Kind: TokenKind.OutKeyword },
                SyntaxNode, SyntaxTokenElement];
            var isLegacyInit = parameterFormChildren is [SyntaxTokenElement { Token.Text: "init" },
                SyntaxTokenElement { Token.Kind: TokenKind.RefKeyword },
                SyntaxTokenElement { Token.Kind: TokenKind.Less }, SyntaxNode,
                SyntaxTokenElement { Token.Kind: TokenKind.Greater }, SyntaxTokenElement];
            if (isLegacyInit)
            {
                Report("TRM3001",
                    "Parameter-only init ref<T> is not admitted; use ref<init T>.",
                    argument.Span);
                valid = false;
            }
            var argumentType = isLegacyInit
                ? parameterFormChildren[3] as SyntaxNode
                : parameterFormChildren.Length >= 2
                    ? parameterFormChildren[^2] as SyntaxNode : null;
            var referenceTypeArgument = argumentType?.Kind
                == SyntaxKind.ReferenceTypeName;
            var genericTypeNode = referenceTypeArgument
                ? argumentType!.Children.OfType<SyntaxNode>().SingleOrDefault()
                : argumentType;
            if (argument.Kind != SyntaxKind.Parameter
                || (parameterFormChildren is not [
                        SyntaxNode, SyntaxTokenElement,
                    ]
                    && !isOut && !isLegacyInit)
                || genericTypeNode is null
                || genericTypeNode.Kind != SyntaxKind.TypeName
                || parameterFormChildren[^1]
                    is not SyntaxTokenElement argumentName
                || !parameterNames.Add(argumentName.Token.Name))
            {
                valid = false;
            }
            else
            {
                var typeName = NameText(genericTypeNode);
                var index = FindTypeParameter(typeName);
                CheckedReferenceTypeDefinition? referencePattern = null;
                var isInitializationReference = false;
                PrimitiveType? scalar = null;
                SyntaxNode? typeTermSyntax = null;
                if (referenceTypeArgument)
                {
                    if (isOut || isLegacyInit)
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
                        isInitializationReference = isInitialization;
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
                        else if (index >= 0)
                        {
                            referencePattern =
                                new CheckedReferenceTypeDefinition(
                                    PrimitiveType.Void, isWritable,
                                    isGarbageCollected,
                                    isInitialization, isOptional);
                            typeTermSyntax = argumentType;
                        }
                        else if (ownerTypeMap.TryGetValue(typeName,
                            out var ownerReferentType))
                        {
                            referencePattern =
                                new CheckedReferenceTypeDefinition(
                                    ownerReferentType, isWritable,
                                    isGarbageCollected,
                                    isInitialization, isOptional);
                            typeTermSyntax = argumentType;
                        }
                        else
                        {
                            var diagnosticCount = DiagnosticCount;
                            var referentType = BindType(genericTypeNode,
                                allowVoid: false);
                            if (DiagnosticCount != diagnosticCount)
                            {
                                valid = false;
                            }
                            else
                            {
                                referencePattern =
                                    new CheckedReferenceTypeDefinition(
                                        referentType, isWritable,
                                        isGarbageCollected,
                                        isInitialization, isOptional);
                                typeTermSyntax = argumentType;
                            }
                        }
                    }
                }
                else
                {
                    scalar = GenericScalarType(typeName)
                        ?? ((isOut || isLegacyInit)
                            ? GenericOutputScalarType(typeName) : null)
                        ?? (ownerTypeMap.TryGetValue(typeName,
                            out var ownerArgumentType)
                            ? ownerArgumentType : null);
                    if (index < 0 && scalar is null)
                    {
                        typeTermSyntax = genericTypeNode;
                    }
                }
                if (index < 0 && scalar is null
                    && typeTermSyntax is null)
                {
                    valid = false;
                }
                if (defaultSyntax is not null
                    && (isOut || referenceTypeArgument
                        || isInitializationReference))
                {
                    Report("TRM3005",
                        "Reference and output parameters cannot declare defaults.",
                        defaultSyntax.Span);
                    valid = false;
                }
                templateParameters.Add(new GenericParameter(argumentName.Token.Name,
                    index < 0 ? scalar : null, index,
                    isOut, isInitializationReference) {
                    ReferenceTypePattern = referencePattern,
                    TypeTermSyntax = typeTermSyntax,
                    DefaultSyntax = defaultSyntax,
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
                if (protocol is null)
                {
                    var diagnosticCount = DiagnosticCount;
                    var interfaceType = item.Kind is SyntaxKind.TypeName
                            or SyntaxKind.ReferenceTypeName
                        ? BindInterfaceType(item, ownerTypeParameterMap,
                            _interfaceDeclarationGroups,
                            ownerTypeParameterMap.Count,
                            methodTypeParameterMap, methodFormals.Length,
                            methodConstantParameters:
                                methodConstantParameters,
                            allowGenericNominalConstantDomain: true)
                        : null;
                    if (interfaceType is {
                            Kind: BoundInterfaceTypeKind.Nominal,
                            IsWritable: false,
                            IsOptional: false,
                            IsGarbageCollected: false,
                            IsInitialization: false,
                        })
                    {
                        if (interfaceConstraints[index].Any(existing =>
                                BoundInterfaceContract.AreEquivalent(
                                    existing, interfaceType)))
                        {
                            valid = false;
                        }
                        else
                        {
                            interfaceConstraints[index].Add(interfaceType);
                        }
                    }
                    else
                    {
                        if (DiagnosticCount == diagnosticCount)
                        {
                            Report("TRM3005",
                                "A generic interface constraint must name a declared interface type.",
                                item.Span);
                        }
                        valid = false;
                    }
                }
                else if (!MatchesProtocolArguments(_protocols[protocol], partNames,
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
        var outputParameters = templateParameters.Where(
            static parameter => parameter.IsOut || parameter.IsInit).ToArray();
        var hasGenericOutputType = outputParameters.Any(parameter =>
            parameter.TypeParameterIndex >= 0
            || !GenericTypeDefaultDependencies(parameter.TypeTermSyntax,
                typeNames).IsEmpty);
        var hasUnsupportedFixedOutputType = outputParameters.Any(parameter =>
        {
            if (parameter.TypeParameterIndex >= 0
                || !GenericTypeDefaultDependencies(parameter.TypeTermSyntax,
                    typeNames).IsEmpty)
            {
                return false;
            }

            var outputType = parameter.IsInit
                && parameter.ReferenceTypePattern
                    is CheckedReferenceTypeDefinition referencePattern
                ? referencePattern.ReferentType
                : parameter.ScalarType;
            return outputType is not PrimitiveType scalarType
                || !IsSupportedNonBooleanGenericOutputType(scalarType);
        });
        if (body is null
            || (scalarReturn == PrimitiveType.Void
                && !hasGenericOutputType)
            || (scalarReturn is PrimitiveType.Float32 or PrimitiveType.Float64
                && outputParameters.Length == 0)
            || (outputParameters.Length != 0
                && scalarReturn != PrimitiveType.Bool
                && (scalarReturn is null
                    || !hasGenericOutputType
                    || hasUnsupportedFixedOutputType))
            )
        {
            valid = false;
        }
        var genericTypeParameters = typeFormals.Select((formal, index) =>
            new GenericTypeParameter(formal.Name,
                operations[index].ToImmutable()) {
                PlaceholderType
                    = (PrimitiveType)(100 + formal.TypeFormalOrdinal),
                OwnershipDomain = ownershipDomains[index],
                NullabilityDomain = nullabilityDomains[index],
                RequiresWritable = writableRequirements[index],
                TypeDefaultSyntax = formal.TypeDefault,
                RequiresBlittable = blittableRequirements[index],
                InterfaceConstraints = interfaceConstraints[index]
                    .ToImmutable(),
                DefaultTypeParameterDependencies =
                    GenericTypeDefaultDependencies(
                        formal.TypeDefault, typeNames),
            }).ToImmutableArray();
        var candidateTemplate = new GenericTemplate(qualified,
            genericTypeParameters, scalarReturn, returnIndex,
            templateParameters.ToImmutable(), body!, declaration, _tree) {
            IsPublic = publicModifierCount != 0,
            RequiresUnsafeCaller = requiresUnsafeCaller,
            DoesNotReturn = doesNotReturn,
            IsStatic = isStatic,
            IsMutable = isMutable,
            MemberName = containingType is null
                ? null : nameToken?.Name,
            ContainingType = containingType,
            OwnerName = containingTypeName,
            OwnerType = ownerType,
            OwnerGenericTypeDefinitionIndex =
                ownerTypeDefinitionIndex,
            OwnerTypeArguments = ownerTypeArguments,
            OwnerConstantArguments =
                _genericOwnerTypeArguments is not null
                    && _genericContext
                        is GenericInstantiation ownerContext
                    ? ownerContext.ConstantArguments : [],
            OwnerTypeArgumentMap = ownerTypeMap,
            ReferenceReturnTypePattern = referenceReturnPattern,
            ReturnTypeTermSyntax = returnTypeTermSyntax,
            PureEvaluationFormSchema = pureSchemaBinding,
            ConstEvalSchema = constEvalSchemaBinding,
            SourceDeclarationContractClauses = [.. declaration.Children
                .OfType<SyntaxNode>()
                .Where(static child =>
                    child.Kind == SyntaxKind.ContractClause)
                .Select(clause =>
                    new SourceDeclarationContractClause(_tree, clause))],
        };
        var formalOrder = methodFormals.Select(static formal =>
            new BoundGenericMethodFormalSlot(formal.Ordinal,
                formal.Kind, formal.TypeFormalOrdinal,
                formal.ConstantFormalOrdinal)).ToImmutableArray();
        var provisionalDefinitionOrdinal = _genericMethodSignatures.Count
            + _templates.Values.Count(static template =>
                template.DefinitionOrdinal < 0
                && template.ImportedMethod is null);
        var formalTypeOwnerOrdinal
            = checked(GetNextGenericMethodFormalTypeOwnerOrdinal()
                + provisionalDefinitionOrdinal);
        if (formalTypeOwnerOrdinal >= BoundSpanTypeTypes.MaximumCount)
        {
            Report("TRM3016",
                "The source exceeds the supported number of generic MethodDef formal owners.",
                declaration.Span);
            return;
        }
        candidateTemplate = candidateTemplate with {
            DefinitionOrdinal = provisionalDefinitionOrdinal,
            FormalTypeOwnerOrdinal = formalTypeOwnerOrdinal,
            FormalOrder = formalOrder,
        };
        var constantFormalCount = methodFormals.Count(static formal =>
            formal.Kind == BoundGenericMethodFormalKind.Constant);
        var constantFormals = ImmutableArray.CreateBuilder<
            GenericMethodConstantFormal>(constantFormalCount);
        foreach (var formal in methodFormals.Where(static formal =>
                     formal.Kind == BoundGenericMethodFormalKind.Constant))
        {
            var domain = formal.ConstantDomain is SyntaxNode domainSyntax
                ? BindGenericMethodTypeTerm(domainSyntax, candidateTemplate)
                : null;
            var defaultDependencies =
                GenericMethodConstantDefaultDependencies(formal.ConstantDefault,
                    methodFormals, formal.Ordinal, out var dependenciesValid);
            if (domain is not BoundGenericMethodTypeTerm domainTerm
                || !IsGenericMethodConstantDomain(domainTerm,
                    candidateTemplate)
                || !dependenciesValid)
            {
                valid = false;
                continue;
            }

            var placeholderType = domainTerm.Kind
                    == BoundGenericMethodTypeTermKind.Concrete
                ? domainTerm.ConcreteType : (PrimitiveType?)null;
            constantFormals.Add(new GenericMethodConstantFormal(
                formal.Ordinal, formal.ConstantFormalOrdinal, formal.Name,
                domainTerm, placeholderType, formal.ConstantDefault,
                defaultDependencies));
            candidateTemplate = candidateTemplate with {
                ConstantFormals = constantFormals.ToImmutable(),
            };
        }

        candidateTemplate = candidateTemplate with {
            DefinitionOrdinal = -1,
            ConstantFormals = constantFormals.ToImmutable(),
            FormalOrder = formalOrder,
        };

        if (!valid || _templates.Count >= MaximumStaticSpecializations)
        {
            Report("TRM3007",
                "Generic functions require distinct default-domain type formals, supported value and output modes, and explicit constraints for any operators.",
                declaration.Span);
            return;
        }
        if (!_templates.TryAdd(qualified, candidateTemplate))
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

    private int GetNextGenericMethodFormalTypeOwnerOrdinal()
        => _genericMethodFormalTypeOwnerBase;

    private bool IsGenericMethodConstantDomain(
        BoundGenericMethodTypeTerm domain, GenericTemplate template)
    {
        if (domain.IsWritable || domain.IsGarbageCollected
            || domain.IsInitialization || domain.IsOptional)
        {
            return false;
        }

        if (domain.Kind == BoundGenericMethodTypeTermKind.Concrete)
        {
            var type = domain.ConcreteType;
            if (PrimitiveTypeFacts.IsIntegerOrWide(type)
                || type == PrimitiveType.Bool
                || (BoundEnumTypes.IsType(type)
                    && (uint)BoundEnumTypes.Index(type)
                        < (uint)_enumDefinitions.Count))
            {
                return true;
            }

            if (!IsStruct(type))
            {
                return false;
            }

            var structureIndex = checked((int)type - 100);
            var structure = _structs[structureIndex];
            return structure.IsTransparent && !structure.IsUnique
                && structure.BlittableTargetRequirement
                    == FixedLayoutStorage.WindowsX64TargetRequirement
                && structure.Fields is [{ } carrier]
                && (PrimitiveTypeFacts.IsIntegerOrWide(carrier.Type)
                    || carrier.Type == PrimitiveType.Bool)
                && IsCopyGenericType(type);
        }

        if (domain.Kind == BoundGenericMethodTypeTermKind.ImportedGeneric)
        {
            if (domain.NominalTypeIdentity
                    is not BoundGenericNominalTypeIdentity identity
                || domain.TypeArguments.IsDefault
                || !domain.ConstantArguments.IsEmpty
                || !_genericNominalTypeDefinitions.Any(candidate =>
                    candidate.Identity == identity))
            {
                return false;
            }

            var definition = _genericNominalTypeDefinitions.Single(
                candidate => candidate.Identity == identity);
            var carrier = definition.TransparentConstantCarrierType;
            if (definition.NominalKind != 1
                || carrier is not PrimitiveType importedCarrier
                || (!PrimitiveTypeFacts.IsIntegerOrWide(importedCarrier)
                    && importedCarrier != PrimitiveType.Bool)
                || definition.Formals.IsDefault
                || definition.Formals.Length
                    != domain.TypeArguments.Length
                || definition.Formals.Any(static formal =>
                    formal.Kind != BoundGenericArgumentKind.Type
                    || formal.Constraints.IsDefault
                    || !formal.Constraints.IsEmpty)
                || GetGenericMethodTypeTermOwnershipDomain(domain,
                    template, 0) != 1)
            {
                return false;
            }

            for (var ordinal = 0;
                ordinal < domain.TypeArguments.Length; ordinal++)
            {
                var formal = definition.Formals[ordinal];
                var argument = domain.TypeArguments[ordinal];
                if (GetGenericMethodTypeTermOwnershipDomain(argument,
                        template, 0) is not byte ownershipDomain
                    || (formal.OwnershipDomain == 1
                        && ownershipDomain != 1)
                    || (formal.OwnershipDomain == 2
                        && ownershipDomain != 2)
                    || (formal.NullabilityDomain == 1
                        && !IsGenericMethodTypeTermNonNullable(argument,
                            template)))
                {
                    return false;
                }
            }

            return true;
        }

        if (domain.Kind != BoundGenericMethodTypeTermKind.SourceGeneric
            || (uint)domain.SourceGenericTypeDefinitionIndex
                >= (uint)_sourceGenericTypeDefinitions.Count
            || domain.TypeArguments.IsDefault)
        {
            return false;
        }

        var sourceDefinition = _sourceGenericTypeDefinitions[
            domain.SourceGenericTypeDefinitionIndex];
        var carrierType = sourceDefinition.TransparentConstantCarrierType;
        if (sourceDefinition.IsUnique
            || carrierType is not PrimitiveType genericCarrierType
            || (!PrimitiveTypeFacts.IsIntegerOrWide(genericCarrierType)
                && genericCarrierType != PrimitiveType.Bool)
            || sourceDefinition.Formals.IsDefault
            || sourceDefinition.FormalOrder.IsDefault
            || sourceDefinition.FormalOrder.Length
                != sourceDefinition.Formals.Length
            || sourceDefinition.Formals.Count(static formal =>
                formal.Kind == BoundGenericMethodFormalKind.Type)
                != domain.TypeArguments.Length
            || sourceDefinition.ConstantFormals.IsDefault
            || sourceDefinition.ConstantFormals.Length
                != domain.ConstantArguments.Length
            || GetGenericMethodTypeTermOwnershipDomain(domain, template, 0)
                != 1)
        {
            return false;
        }

        var typeArgumentOrdinal = 0;
        var constantArgumentOrdinal = 0;
        foreach (var slot in sourceDefinition.FormalOrder)
        {
            if ((uint)slot.Ordinal
                >= (uint)sourceDefinition.Formals.Length)
            {
                return false;
            }

            var formal = sourceDefinition.Formals[slot.Ordinal];
            if (formal is null || formal.Ordinal != slot.Ordinal
                || formal.Kind != slot.Kind)
            {
                return false;
            }

            if (slot.Kind == BoundGenericMethodFormalKind.Type)
            {
                if ((uint)slot.TypeFormalOrdinal
                        >= (uint)domain.TypeArguments.Length)
                {
                    return false;
                }

                var argument = domain.TypeArguments[
                    typeArgumentOrdinal++];
                if (GetGenericMethodTypeTermOwnershipDomain(argument,
                        template, 0) is not byte ownershipDomain
                    || (formal.OwnershipDomain == 1
                        && ownershipDomain != 1)
                    || (formal.OwnershipDomain == 2
                        && ownershipDomain != 2)
                    || (formal.NullabilityDomain == 1
                        && !IsGenericMethodTypeTermNonNullable(argument,
                            template))
                    || (formal.RequiresBlittable
                        && !IsGenericMethodBlittableTypeTerm(argument,
                            template, 0)))
                {
                    return false;
                }

                continue;
            }

            if (slot.Kind != BoundGenericMethodFormalKind.Constant
                || (uint)slot.ConstantFormalOrdinal
                    >= (uint)sourceDefinition.ConstantFormals.Length
                || (uint)constantArgumentOrdinal
                    >= (uint)domain.ConstantArguments.Length)
            {
                return false;
            }

            var constantFormal = sourceDefinition.ConstantFormals[
                slot.ConstantFormalOrdinal];
            var constantArgument = domain.ConstantArguments[
                constantArgumentOrdinal++];
            if (constantFormal is null
                || constantFormal.Ordinal != formal.Ordinal
                || constantFormal.ConstantOrdinal
                    != slot.ConstantFormalOrdinal
                || !BoundGenericMethodTypeTerms
                    .TrySubstituteSourceGenericTypeFormals(
                        constantFormal.Domain, domain.TypeArguments,
                        domain.SourceGenericTypeDefinitionIndex,
                        domain.ConstantArguments, out var constantDomain)
                || !BoundGenericMethodTypeTerms
                    .TryGetConstantFormalDependencies(constantDomain,
                        out _)
                || !IsGenericMethodConstantDomain(constantDomain, template))
            {
                return false;
            }

            if (constantArgument.Kind
                == BoundGenericMethodTypeConstantArgumentKind.Value)
            {
                if (!TryResolveGenericMethodTypeTerm(constantDomain,
                        default, out var domainType)
                    || !TryGetGenericMethodConstantCarrierType(domainType,
                        out var constantCarrierType)
                    || !GenericNominalTypesEquivalent(
                        constantArgument.DomainType, domainType)
                    || constantArgument.CarrierType
                        != constantCarrierType
                    || !constantArgument.Bits.IsCanonicalFor(
                        constantCarrierType))
                {
                    return false;
                }
            }
            else if (constantArgument.Kind
                    != BoundGenericMethodTypeConstantArgumentKind.Formal
                || !IsGenericMethodConstantDomainFormal(
                    constantArgument, constantDomain, template))
            {
                return false;
            }
        }

        return typeArgumentOrdinal == domain.TypeArguments.Length
            && constantArgumentOrdinal == domain.ConstantArguments.Length;
    }

    private static bool IsGenericMethodConstantDomainFormal(
        BoundGenericMethodTypeConstantArgument argument,
        BoundGenericMethodTypeTerm domain, GenericTemplate template)
    {
        if (argument.FormalOwnerKind
                == BoundGenericTypeFormalOwnerKind.GenericMethod
            && argument.FormalDefinitionOrdinal
                == template.DefinitionOrdinal
            && (uint)argument.FormalOrdinal
                < (uint)template.ConstantFormals.Length)
        {
            return AreGenericMethodTypeTermsEquivalent(
                template.ConstantFormals[argument.FormalOrdinal].Domain,
                domain);
        }

        if (argument.FormalOwnerKind
                == BoundGenericTypeFormalOwnerKind.SourceGenericType
            && argument.FormalDefinitionOrdinal
                == template.SourceGenericTypeDefinitionIndex
            && (uint)argument.FormalOrdinal
                < (uint)template.ConstantFormals.Length)
        {
            return AreGenericMethodTypeTermsEquivalent(
                template.ConstantFormals[argument.FormalOrdinal].Domain,
                domain);
        }

        return false;
    }

    private static ImmutableArray<int>
        GenericMethodConstantDefaultDependencies(
            SyntaxNode? defaultSyntax,
            ImmutableArray<GenericMethodFormalSyntax> formals,
            int currentFormalOrdinal, out bool valid)
    {
        valid = true;
        if (defaultSyntax is null)
        {
            return [];
        }

        var formalsByName = new Dictionary<string, GenericMethodFormalSyntax>(
            StringComparer.Ordinal);
        foreach (var formal in formals)
        {
            _ = formalsByName.TryAdd(formal.Name, formal);
        }

        var dependencies = new SortedSet<int>();
        var pending = new Stack<SyntaxNode>();
        pending.Push(defaultSyntax);
        while (pending.TryPop(out var node))
        {
            if (node.Kind == SyntaxKind.NameExpression
                && node.Children.FirstOrDefault()
                    is SyntaxTokenElement name
                && formalsByName.TryGetValue(name.Token.Name,
                    out var referencedFormal)
                && referencedFormal.Kind
                    == BoundGenericMethodFormalKind.Constant)
            {
                if (referencedFormal.Ordinal >= currentFormalOrdinal)
                {
                    valid = false;
                }
                else
                {
                    _ = dependencies.Add(referencedFormal.Ordinal);
                }
            }

            foreach (var child in node.Children.OfType<SyntaxNode>())
            {
                pending.Push(child);
            }
        }

        return [.. dependencies];
    }

    private ImmutableArray<int> GenericStructConstantDefaultDependencies(
        SyntaxNode? defaultSyntax,
        ImmutableArray<GenericMethodFormalSyntax> formals,
        int currentFormalOrdinal, out bool valid)
    {
        var dependencies = new SortedSet<int>(
            GenericMethodConstantDefaultDependencies(defaultSyntax,
                formals, currentFormalOrdinal, out valid));
        if (!valid || defaultSyntax is null)
        {
            return [.. dependencies];
        }

        var formalsByName = new Dictionary<string, GenericMethodFormalSyntax>(
            StringComparer.Ordinal);
        foreach (var formal in formals)
        {
            _ = formalsByName.TryAdd(formal.Name, formal);
        }

        var pending = new Stack<SyntaxNode>();
        pending.Push(defaultSyntax);
        while (pending.TryPop(out var node))
        {
            var arguments = node.Kind == SyntaxKind.TypeName
                ? node.Children.OfType<SyntaxNode>().ToArray() : [];
            if (arguments.Length > 0)
            {
                var name = TypeNameIdentifier(node);
                if (TryFindGenericStructTemplateForTypeTerm(name,
                    out var sourceTemplate))
                {
                    for (var index = 0;
                        index < arguments.Length
                            && index < sourceTemplate.FormalOrder.Length;
                        index++)
                    {
                        if (sourceTemplate.FormalOrder[index].Kind
                                == BoundGenericMethodFormalKind.Constant
                            && TryGetBareGenericConstantName(
                                arguments[index], out var argumentName)
                            && formalsByName.TryGetValue(argumentName,
                                out var referencedFormal)
                            && referencedFormal.Kind
                                == BoundGenericMethodFormalKind.Constant)
                        {
                            if (referencedFormal.Ordinal
                                >= currentFormalOrdinal)
                            {
                                valid = false;
                            }
                            else
                            {
                                _ = dependencies.Add(
                                    referencedFormal.Ordinal);
                            }
                        }
                    }
                }
                else if (TryFindGenericNominalTypeDefinitionForTypeTerm(
                    name, out var nominalDefinition))
                {
                    for (var index = 0;
                        index < arguments.Length
                            && index < nominalDefinition.Formals.Length;
                        index++)
                    {
                        if (nominalDefinition.Formals[index].Kind
                                == BoundGenericArgumentKind.Constant
                            && TryGetBareGenericConstantName(
                                arguments[index], out var argumentName)
                            && formalsByName.TryGetValue(argumentName,
                                out var referencedFormal)
                            && referencedFormal.Kind
                                == BoundGenericMethodFormalKind.Constant)
                        {
                            if (referencedFormal.Ordinal
                                >= currentFormalOrdinal)
                            {
                                valid = false;
                            }
                            else
                            {
                                _ = dependencies.Add(
                                    referencedFormal.Ordinal);
                            }
                        }
                    }
                }
            }

            foreach (var child in node.Children.OfType<SyntaxNode>())
            {
                pending.Push(child);
            }
        }

        return [.. dependencies];
    }

    private static bool TryGetBareGenericConstantName(
        SyntaxNode argument, out string name)
    {
        name = string.Empty;
        if (argument.Kind != SyntaxKind.TypeName
            || argument.Children.OfType<SyntaxNode>().Any())
        {
            return false;
        }

        var identifiers = argument.Children
            .OfType<SyntaxTokenElement>()
            .Where(static element =>
                element.Token.Kind == TokenKind.Identifier)
            .ToArray();
        if (identifiers is not [{ } identifier])
        {
            return false;
        }

        name = identifier.Token.Name;
        return true;
    }

    private PrimitiveType? GenericCallType(SyntaxNode callee, SyntaxNode? callArguments = null)
    {
        var name = ((SyntaxTokenElement)callee.Children[0]).Token.Name;
        if (TryFindVariable(name, out _) || FindGenericTemplate(name) is not GenericTemplate template)
        {
            return null;
        }
        if (template.ScalarReturnType is PrimitiveType scalarReturnType)
        {
            return scalarReturnType;
        }
        if (callee.Kind == SyntaxKind.GenericNameExpression)
        {
            if (!TryReadGenericMethodArguments(callee, template,
                    out var actuals))
            {
                return null;
            }
            var completedTypes = callArguments is SyntaxNode callArgumentList
                ? InferGenericTypes(template, callArgumentList, null, out _,
                    actuals.TypeArguments,
                    actuals.RequireTypeInference,
                    actuals.ExplicitTypeDefaults, actuals)
                : CompleteGenericTypeArguments(template,
                    actuals.TypeArguments, actuals.RequireTypeInference,
                    actuals.ExplicitTypeDefaults, actuals);
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
                var callerOwnershipDomain
                    = _genericUniqueRefinements.TryGetValue(type,
                        out var refinedUnique)
                        ? refinedUnique ? (byte)2 : (byte)1
                        : callerFormal.OwnershipDomain;
                if ((formal.OwnershipDomain == 1
                        && callerOwnershipDomain != 1)
                    || (formal.OwnershipDomain == 2
                        && callerOwnershipDomain != 2))
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

    private string? GenericMethodInterfaceConstraintFailure(
        GenericTemplate template, ImmutableArray<PrimitiveType> typeArguments,
        ImmutableArray<BoundGenericMethodConstantArgument> constantArguments,
        int ordinal)
    {
        if ((uint)ordinal >= (uint)template.TypeParameters.Length
            || typeArguments.Length != template.TypeParameters.Length)
        {
            return "interface witness requirement";
        }

        foreach (var constraint in template.TypeParameters[ordinal]
            .InterfaceConstraints)
        {
            if (!TrySubstituteGenericMethodInterfaceType(constraint,
                    template, typeArguments, constantArguments,
                    out var requiredInterface))
            {
                return "interface witness requirement";
            }

            var actualType = typeArguments[ordinal];
            if (TryGetGenericInterfaceFormal(actualType,
                    out var formalScope, out var formalOrdinal))
            {
                var callerTypeFormal = _genericContext
                    is GenericInstantiation currentCaller
                    && formalScope
                        == BoundInterfaceTypeParameterScope.Member
                    ? currentCaller.Template.FormalOrder.FirstOrDefault(
                        slot => slot.Ordinal == formalOrdinal
                            && slot.Kind
                                == BoundGenericMethodFormalKind.Type)
                    : null;
                if (formalScope
                        != BoundInterfaceTypeParameterScope.Member
                    || _genericContext is not GenericInstantiation caller
                    || callerTypeFormal is null
                    || (uint)callerTypeFormal.TypeFormalOrdinal
                        >= (uint)caller.Template.TypeParameters.Length)
                {
                    return "interface witness requirement";
                }
                var callerConstraints = caller.Template.TypeParameters[
                    callerTypeFormal.TypeFormalOrdinal]
                    .InterfaceConstraints;
                if (!callerConstraints.Any(callerConstraint =>
                        BoundInterfaceContract.IsInInheritanceClosure(
                            callerConstraint, requiredInterface,
                            GenericMethodInterfaceDefinitions)))
                {
                    return "interface witness requirement";
                }
                continue;
            }

            var implementingType = ManagedClassTypes.IsClass(actualType)
                ? ManagedClassTypes.Readonly(actualType) : actualType;
            if (!_interfaceWitnesses.Any(witness =>
                    witness.ImplementingType == implementingType
                    && BoundInterfaceContract.IsInInheritanceClosure(
                        witness.InterfaceType, requiredInterface,
                        GenericMethodInterfaceDefinitions)))
            {
                return "interface witness requirement";
            }
        }
        return null;
    }

    private ImmutableArray<BoundInterfaceDefinition>
        GenericMethodInterfaceDefinitions =>
            _interfaces.Count != 0
                ? [.. _interfaces]
                : _genericProofSource is Binder proofSource
                    ? [.. proofSource._interfaces]
                    : [];
    private int _genericInterfaceRequirementStubOrdinal;

    private bool TrySubstituteGenericMethodInterfaceType(
        BoundInterfaceType type, GenericTemplate template,
        ImmutableArray<PrimitiveType> typeArguments,
        ImmutableArray<BoundGenericMethodConstantArgument>
            constantArguments,
        out BoundInterfaceType substituted, int depth = 0)
    {
        substituted = null!;
        if (depth > SemanticVerifier.MaximumDepth
            || type.TypeArguments.IsDefault)
        {
            return false;
        }
        if (type.Kind == BoundInterfaceTypeKind.TypeParameter)
        {
            if (type.IsWritable || type.IsOptional
                || type.IsGarbageCollected || type.IsInitialization)
            {
                return false;
            }
            PrimitiveType actual;
            if (type.TypeParameterScope
                    == BoundInterfaceTypeParameterScope.Member)
            {
                var formalSlot = template.FormalOrder.FirstOrDefault(slot =>
                    slot.Ordinal == type.TypeParameterOrdinal
                    && slot.Kind == BoundGenericMethodFormalKind.Type);
                if (formalSlot is null
                    || (uint)formalSlot.TypeFormalOrdinal
                        >= (uint)typeArguments.Length)
                {
                    return false;
                }
                actual = typeArguments[formalSlot.TypeFormalOrdinal];
            }
            else if (type.TypeParameterScope
                    == BoundInterfaceTypeParameterScope.Interface)
            {
                if ((uint)type.TypeParameterOrdinal
                    >= (uint)template.OwnerTypeArguments.Length)
                {
                    return false;
                }
                actual = template.OwnerTypeArguments[
                    type.TypeParameterOrdinal];
            }
            else
            {
                return false;
            }
            return TryCreateGenericInterfaceActual(actual,
                out substituted);
        }

        var typeArgumentsBuilder =
            ImmutableArray.CreateBuilder<BoundInterfaceTypeArgument>(
                type.TypeArguments.Length);
        foreach (var argument in type.TypeArguments)
        {
            if (argument is null)
            {
                return false;
            }
            BoundInterfaceType? argumentType = null;
            BoundInterfaceType? constantType = null;
            if (argument.Type is BoundInterfaceType typeArgument
                && !TrySubstituteGenericMethodInterfaceType(
                    typeArgument, template, typeArguments,
                    constantArguments,
                    out argumentType, depth + 1))
            {
                return false;
            }
            if (argument.ConstantType
                is BoundInterfaceType constantTypeArgument
                && !TrySubstituteGenericMethodInterfaceType(
                    constantTypeArgument, template, typeArguments,
                    constantArguments,
                    out constantType, depth + 1))
            {
                return false;
            }
            if (argument.ConstantParameterScope
                == BoundInterfaceTypeParameterScope.Member)
            {
                var targetSlot = template.FormalOrder.FirstOrDefault(slot =>
                    slot.Ordinal == argument.ConstantParameterOrdinal
                    && slot.Kind
                        == BoundGenericMethodFormalKind.Constant);
                if (targetSlot is null
                    || (uint)targetSlot.ConstantFormalOrdinal
                        >= (uint)constantArguments.Length)
                {
                    return false;
                }

                var actual = constantArguments[
                    targetSlot.ConstantFormalOrdinal];
                if (actual.Kind
                    == BoundGenericMethodConstantArgumentKind.Value)
                {
                    if (!TryCreateGenericInterfaceActual(
                        actual.DomainType, out var actualDomain))
                    {
                        return false;
                    }
                    var value = actual.DomainType == actual.CarrierType
                        ? new BoundConstant(actual.Bits,
                            actual.DomainType, default)
                        : new BoundConstant(actual.Bits,
                            actual.DomainType, actual.CarrierType, default);
                    typeArgumentsBuilder.Add(
                        BoundInterfaceTypeArgument.ForConstant(
                            actualDomain, value));
                    continue;
                }

                if (actual.Kind
                        != BoundGenericMethodConstantArgumentKind.Formal
                    || _genericContext is not GenericInstantiation caller
                    || actual.CallerDefinitionOrdinal
                        != caller.Template.DefinitionOrdinal
                    || caller.Template.ConstantFormals
                        .FirstOrDefault(formal =>
                            formal.ConstantOrdinal
                                == actual.CallerConstantFormalOrdinal)
                        is not GenericMethodConstantFormal callerFormal
                    || !TryCreateGenericInterfaceConstantDomain(
                        caller, callerFormal, out var forwardedDomain))
                {
                    return false;
                }
                var callerSlot = caller.Template.FormalOrder
                    .FirstOrDefault(slot => slot.Kind
                        == BoundGenericMethodFormalKind.Constant
                        && slot.ConstantFormalOrdinal
                            == actual.CallerConstantFormalOrdinal);
                if (callerSlot is null)
                {
                    return false;
                }
                typeArgumentsBuilder.Add(
                    BoundInterfaceTypeArgument.ForConstantParameter(
                        forwardedDomain,
                        BoundInterfaceTypeParameterScope.Member,
                        callerSlot.Ordinal));
                continue;
            }
            if (argument.ConstantParameterScope
                    != BoundInterfaceTypeParameterScope.None
                || argument.ConstantParameterOrdinal != -1
                || (argument.ConstantValue is not null
                    && argument.Kind
                        != BoundInterfaceTypeArgumentKind.Constant))
            {
                return false;
            }
            typeArgumentsBuilder.Add(argument with {
                Type = argumentType,
                ConstantType = constantType,
            });
        }

        substituted = type with {
            TypeArguments = typeArgumentsBuilder.MoveToImmutable(),
        };
        return true;
    }

    private bool TryCreateGenericInterfaceConstantDomain(
        GenericInstantiation caller,
        GenericMethodConstantFormal formal,
        out BoundInterfaceType domain)
    {
        domain = null!;
        if (formal.Domain.Kind
            == BoundGenericMethodTypeTermKind.Concrete)
        {
            domain = ConcreteInterfaceType(formal.Domain.ConcreteType);
            return true;
        }
        if (formal.Domain.Kind
            == BoundGenericMethodTypeTermKind.Formal
            && (uint)formal.Domain.FormalOrdinal
                < (uint)caller.Types.Length)
        {
            return TryCreateGenericInterfaceActual(
                caller.Types[formal.Domain.FormalOrdinal], out domain);
        }

        if (formal.Domain.Kind
                == BoundGenericMethodTypeTermKind.ImportedGeneric
            && formal.Domain.NominalTypeIdentity
                is BoundGenericNominalTypeIdentity identity
            && TryFindGenericNominalDefinitionByIdentity(
                identity, out var definition)
            && definition.TransparentConstantCarrierType is not null
            && TryCreateGenericInterfaceTypeArgument(
                formal.Domain, caller, requireTransparentCarrier: true,
                out domain))
        {
            return true;
        }
        if (formal.Domain.Kind
            == BoundGenericMethodTypeTermKind.SourceGeneric)
        {
            return TryCreateGenericInterfaceTypeArgument(formal.Domain,
                caller, requireTransparentCarrier: true, out domain);
        }

        var typeArguments = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(caller.Types.Length);
        foreach (var typeArgument in caller.Types)
        {
            if (!TryGetGenericMethodTypeArgumentTerm(typeArgument,
                out var typeTerm))
            {
                return false;
            }
            typeArguments.Add(typeTerm);
        }

        var constantArguments = caller.ConstantArguments
            .Select(static argument =>
                (BoundGenericMethodConstantArgument?)argument)
            .ToImmutableArray();
        if (!BoundGenericMethodTypeTerms.TrySubstitute(formal.Domain,
                typeArguments.MoveToImmutable(), constantArguments,
                out var domainTerm)
            || !TryResolveGenericMethodTypeTerm(domainTerm,
                caller.Template.Declaration.Span,
                out var resolvedDomain))
        {
            return false;
        }

        return TryCreateGenericInterfaceActual(resolvedDomain, out domain);
    }

    private bool TryCreateGenericInterfaceTypeArgument(
        BoundGenericMethodTypeTerm term, GenericInstantiation caller,
        bool requireTransparentCarrier, out BoundInterfaceType boundType)
    {
        boundType = null!;
        if (term.TypeArguments.IsDefault || term.ConstantArguments.IsDefault)
        {
            return false;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.Concrete)
        {
            boundType = ConcreteInterfaceType(term.ConcreteType);
            return term.ConcreteType != PrimitiveType.Void;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.Formal)
        {
            var formalSlot = caller.Template.FormalOrder.FirstOrDefault(
                slot => slot.Kind == BoundGenericMethodFormalKind.Type
                    && slot.TypeFormalOrdinal == term.FormalOrdinal);
            if (formalSlot is null)
            {
                return false;
            }

            boundType = new BoundInterfaceType(
                BoundInterfaceTypeKind.TypeParameter,
                PrimitiveType.Void, null, formalSlot.Ordinal, [],
                false, false, false, false) {
                TypeParameterScope =
                    BoundInterfaceTypeParameterScope.Member,
            };
            return true;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.SourceGeneric)
        {
            if ((uint)term.SourceGenericTypeDefinitionIndex
                    >= (uint)_sourceGenericTypeDefinitions.Count
                || (requireTransparentCarrier
                    && (term.IsWritable || term.IsOptional
                        || term.IsGarbageCollected
                        || term.IsInitialization)))
            {
                return false;
            }

            var sourceDefinition = _sourceGenericTypeDefinitions[
                term.SourceGenericTypeDefinitionIndex];
            if (sourceDefinition.DefinitionIndex
                    != term.SourceGenericTypeDefinitionIndex
                || sourceDefinition.Formals.IsDefaultOrEmpty
                || sourceDefinition.FormalOrder.IsDefault
                || sourceDefinition.Formals.Length
                    != sourceDefinition.FormalOrder.Length
                || (requireTransparentCarrier
                    && sourceDefinition.TransparentConstantCarrierType
                        is null))
            {
                return false;
            }

            var typeFormalCount = sourceDefinition.Formals.Count(
                static formal => formal.Kind
                    == BoundGenericMethodFormalKind.Type);
            var constantFormalCount = sourceDefinition.Formals.Count(
                static formal => formal.Kind
                    == BoundGenericMethodFormalKind.Constant);
            if (typeFormalCount != term.TypeArguments.Length
                || constantFormalCount != term.ConstantArguments.Length)
            {
                return false;
            }

            var genericArguments = ImmutableArray.CreateBuilder<
                BoundInterfaceTypeArgument>(
                    sourceDefinition.Formals.Length);
            foreach (var formal in sourceDefinition.Formals)
            {
                if ((uint)formal.Ordinal
                        >= (uint)sourceDefinition.FormalOrder.Length
                    || sourceDefinition.FormalOrder[formal.Ordinal]
                        is not BoundGenericMethodFormalSlot slot
                    || slot.Ordinal != formal.Ordinal)
                {
                    return false;
                }

                if (formal.Kind == BoundGenericMethodFormalKind.Type)
                {
                    if (formal.RequiresBlittable
                        || formal.OwnershipDomain != 1
                        || formal.NullabilityDomain != 1
                        || (uint)formal.TypeFormalOrdinal
                            >= (uint)term.TypeArguments.Length
                        || slot.TypeFormalOrdinal
                            != formal.TypeFormalOrdinal
                        || slot.ConstantFormalOrdinal != -1
                        || !TryCreateGenericInterfaceTypeArgument(
                            term.TypeArguments[formal.TypeFormalOrdinal],
                            caller, requireTransparentCarrier: false,
                            out var typeArgument))
                    {
                        return false;
                    }

                    genericArguments.Add(
                        BoundInterfaceTypeArgument.ForType(typeArgument));
                    continue;
                }

                if (formal.Kind
                        != BoundGenericMethodFormalKind.Constant
                    || (uint)formal.ConstantFormalOrdinal
                        >= (uint)term.ConstantArguments.Length
                    || slot.ConstantFormalOrdinal
                        != formal.ConstantFormalOrdinal
                    || slot.TypeFormalOrdinal != -1
                    || formal.ConstantDomain is not
                        BoundGenericMethodTypeTerm formalDomain
                    || !BoundGenericMethodTypeTerms
                        .TrySubstituteSourceGenericTypeFormals(
                        formalDomain, term.TypeArguments,
                        sourceDefinition.DefinitionIndex,
                        term.ConstantArguments,
                        out var substitutedConstantDomain)
                    || !BoundGenericMethodTypeTerms
                        .TryGetConstantFormalDependencies(
                            substitutedConstantDomain, out _)
                    || !TryCreateGenericInterfaceTypeArgument(
                        substitutedConstantDomain, caller,
                        requireTransparentCarrier: true,
                        out var constantDomain)
                    || !TryCreateGenericInterfaceConstantArgument(
                        term.ConstantArguments[
                            formal.ConstantFormalOrdinal],
                        caller, constantDomain,
                        out var constantArgument))
                {
                    return false;
                }

                genericArguments.Add(constantArgument);
            }

            boundType = new BoundInterfaceType(
                BoundInterfaceTypeKind.GenericNominal,
                PrimitiveType.Void, sourceDefinition.QualifiedName, -1,
                genericArguments.MoveToImmutable(), term.IsWritable,
                term.IsOptional, term.IsGarbageCollected,
                term.IsInitialization) {
                SourceGenericTypeDefinitionIndex =
                    sourceDefinition.DefinitionIndex,
                IsGenericNominalConstantDomain =
                    requireTransparentCarrier,
            };
            return true;
        }

        if (term.Kind
                != BoundGenericMethodTypeTermKind.ImportedGeneric
            || term.NominalTypeIdentity
                is not BoundGenericNominalTypeIdentity identity
            || !TryFindGenericNominalDefinitionByIdentity(
                identity, out var definition)
            || (requireTransparentCarrier
                && (term.IsWritable || term.IsOptional
                    || term.IsGarbageCollected
                    || term.IsInitialization))
            || (requireTransparentCarrier
                && definition.TransparentConstantCarrierType is null)
            || definition.Formals.IsDefaultOrEmpty
            || definition.Formals.Count(static formal =>
                formal.Kind == BoundGenericArgumentKind.Type)
                != term.TypeArguments.Length
            || definition.Formals.Count(static formal =>
                formal.Kind == BoundGenericArgumentKind.Constant)
                != term.ConstantArguments.Length
            || definition.Formals.Any(static formal =>
                formal.Constraints.IsDefault
                || !formal.Constraints.IsEmpty))
        {
            return false;
        }

        var typeArguments = ImmutableArray.CreateBuilder<
            BoundInterfaceTypeArgument>(definition.Formals.Length);
        var typeArgumentOrdinal = 0;
        var constantArgumentOrdinal = 0;
        foreach (var formal in definition.Formals)
        {
            if (formal.Kind == BoundGenericArgumentKind.Type)
            {
                if (!TryCreateGenericInterfaceTypeArgument(
                        term.TypeArguments[typeArgumentOrdinal++], caller,
                        requireTransparentCarrier: false,
                        out var boundTypeArgument))
                {
                    return false;
                }

                typeArguments.Add(
                    BoundInterfaceTypeArgument.ForType(boundTypeArgument));
                continue;
            }

            if (formal.Kind != BoundGenericArgumentKind.Constant
                || formal.ConstantDomainType is not
                    PrimitiveType domainType
                || formal.ConstantCarrierType is not
                    PrimitiveType carrierType
                || (!PrimitiveTypeFacts.IsIntegerOrWide(carrierType)
                    && carrierType != PrimitiveType.Bool
                    && !BoundEnumTypes.IsType(domainType))
                || (formal.ConstantDomainTypeTerm is not null
                    && (formal.ConstantDomainTypeTerm.Kind
                            != BoundGenericMethodTypeTermKind.Concrete
                        || formal.ConstantDomainTypeTerm.ConcreteType
                            != domainType)))
            {
                return false;
            }

            var constantArgument = term.ConstantArguments[
                constantArgumentOrdinal++];
            if (constantArgument.Kind
                    == BoundGenericMethodTypeConstantArgumentKind.Value
                && (constantArgument.DomainType != domainType
                    || constantArgument.CarrierType != carrierType))
            {
                return false;
            }

            var constantDomain = ConcreteInterfaceType(domainType);
            if (!TryCreateGenericInterfaceConstantArgument(
                    constantArgument, caller, constantDomain,
                    out var boundConstantArgument))
            {
                return false;
            }
            typeArguments.Add(boundConstantArgument);
        }

        if (typeArgumentOrdinal != term.TypeArguments.Length
            || constantArgumentOrdinal != term.ConstantArguments.Length)
        {
            return false;
        }

        boundType = new BoundInterfaceType(
            BoundInterfaceTypeKind.GenericNominal,
            PrimitiveType.Void, definition.Namespace + "." + definition.Name,
            -1, typeArguments.MoveToImmutable(), term.IsWritable,
            term.IsOptional, term.IsGarbageCollected,
            term.IsInitialization) {
            GenericNominalTypeIdentity = identity,
            IsGenericNominalConstantDomain = requireTransparentCarrier,
        };
        return true;
    }

    private bool TryCreateGenericInterfaceConstantArgument(
        BoundGenericMethodTypeConstantArgument argument,
        GenericInstantiation caller, BoundInterfaceType expectedDomain,
        out BoundInterfaceTypeArgument boundArgument)
    {
        boundArgument = null!;
        if (argument.Kind
            == BoundGenericMethodTypeConstantArgumentKind.Value)
        {
            if (expectedDomain.Kind
                    != BoundInterfaceTypeKind.Primitive
                || argument.DomainType != expectedDomain.PrimitiveType
                || !argument.Bits.IsCanonicalFor(argument.CarrierType))
            {
                return false;
            }

            var value = argument.DomainType == argument.CarrierType
                ? new BoundConstant(argument.Bits, argument.DomainType,
                    default)
                : new BoundConstant(argument.Bits, argument.DomainType,
                    argument.CarrierType, default);
            boundArgument = BoundInterfaceTypeArgument.ForConstant(
                expectedDomain, value);
            return true;
        }

        if (argument.Kind
                != BoundGenericMethodTypeConstantArgumentKind.Formal
            || argument.FormalOwnerKind
                != BoundGenericTypeFormalOwnerKind.GenericMethod
            || argument.FormalDefinitionOrdinal
                != caller.Template.DefinitionOrdinal
            || caller.Template.ConstantFormals.FirstOrDefault(formal =>
                formal.ConstantOrdinal == argument.FormalOrdinal)
                is not GenericMethodConstantFormal callerFormal
            || !TryCreateGenericInterfaceConstantDomain(caller,
                callerFormal, out var actualDomain)
            || !BoundInterfaceContract.AreEquivalent(expectedDomain,
                actualDomain))
        {
            return false;
        }

        var callerSlot = caller.Template.FormalOrder.FirstOrDefault(slot =>
            slot.Kind == BoundGenericMethodFormalKind.Constant
            && slot.ConstantFormalOrdinal == argument.FormalOrdinal);
        if (callerSlot is null)
        {
            return false;
        }

        boundArgument = BoundInterfaceTypeArgument.ForConstantParameter(
            expectedDomain, BoundInterfaceTypeParameterScope.Member,
            callerSlot.Ordinal);
        return true;
    }

    private bool TryFindGenericNominalDefinitionByIdentity(
        BoundGenericNominalTypeIdentity identity,
        out BoundGenericNominalTypeDefinition definition)
    {
        definition = null!;
        foreach (var candidate in _genericNominalTypeDefinitions)
        {
            if (candidate.Identity != identity)
            {
                continue;
            }
            if (definition is not null)
            {
                definition = null!;
                return false;
            }
            definition = candidate;
        }

        return definition is not null;
    }

    private bool TryCreateGenericInterfaceActual(
        PrimitiveType actual, out BoundInterfaceType boundType)
    {
        boundType = null!;
        if (_genericProofFunctions is not null
            && _genericContext is GenericInstantiation context)
        {
            var matchingMethodFormals = context.Types
                .Select((type, ordinal) => (type, ordinal))
                .Where(item => item.type == actual)
                .Select(static item => item.ordinal)
                .ToArray();
            if (matchingMethodFormals.Length == 1)
            {
                var formalSlot = context.Template.FormalOrder
                    .FirstOrDefault(slot => slot.Kind
                        == BoundGenericMethodFormalKind.Type
                        && slot.TypeFormalOrdinal
                            == matchingMethodFormals[0]);
                if (formalSlot is null)
                {
                    return false;
                }
                boundType = new BoundInterfaceType(
                    BoundInterfaceTypeKind.TypeParameter,
                    PrimitiveType.Void, null, formalSlot.Ordinal, [],
                    false, false, false, false) {
                    TypeParameterScope =
                        BoundInterfaceTypeParameterScope.Member,
                };
                return true;
            }
            if (matchingMethodFormals.Length > 1)
            {
                return false;
            }

            var matchingOwnerFormals = context.Template.OwnerTypeArguments
                .Select((type, ordinal) => (type, ordinal))
                .Where(item => item.type == actual)
                .Select(static item => item.ordinal)
                .ToArray();
            if (matchingOwnerFormals.Length == 1)
            {
                boundType = new BoundInterfaceType(
                    BoundInterfaceTypeKind.TypeParameter,
                    PrimitiveType.Void, null, matchingOwnerFormals[0], [],
                    false, false, false, false) {
                    TypeParameterScope =
                        BoundInterfaceTypeParameterScope.Interface,
                };
                return true;
            }
            if (matchingOwnerFormals.Length > 1)
            {
                return false;
            }
        }

        boundType = ConcreteInterfaceType(actual);
        return true;
    }

    private bool TryGetGenericInterfaceFormal(
        PrimitiveType type,
        out BoundInterfaceTypeParameterScope scope,
        out int ordinal)
    {
        scope = BoundInterfaceTypeParameterScope.None;
        ordinal = -1;
        if (_genericProofFunctions is null
            || _genericContext is not GenericInstantiation context)
        {
            return false;
        }

        var methodFormals = context.Types
            .Select((actual, index) => (actual, index))
            .Where(item => item.actual == type)
            .Select(static item => item.index)
            .ToArray();
        if (methodFormals.Length == 1)
        {
            scope = BoundInterfaceTypeParameterScope.Member;
            var formalSlot = context.Template.FormalOrder
                .FirstOrDefault(slot => slot.Kind
                    == BoundGenericMethodFormalKind.Type
                    && slot.TypeFormalOrdinal == methodFormals[0]);
            if (formalSlot is null)
            {
                return false;
            }
            ordinal = formalSlot.Ordinal;
            return true;
        }
        if (methodFormals.Length > 1)
        {
            return false;
        }

        var ownerFormals = context.Template.OwnerTypeArguments
            .Select((actual, index) => (actual, index))
            .Where(item => item.actual == type)
            .Select(static item => item.index)
            .ToArray();
        if (ownerFormals.Length == 1)
        {
            scope = BoundInterfaceTypeParameterScope.Interface;
            ordinal = ownerFormals[0];
            return true;
        }
        return false;
    }

    private bool TryGetGenericUniqueProperty(SyntaxNode node,
        out int formalOrdinal, out PrimitiveType formalType)
    {
        formalOrdinal = -1;
        formalType = default;
        if (node.Kind != SyntaxKind.IsPatternExpression
            || node.Children.Length != 3
            || node.Children[0] is not SyntaxNode operand
            || node.Children[2] is not SyntaxNode {
                Kind: SyntaxKind.UniqueTypePropertyPattern,
            }
            || _genericContext is not GenericInstantiation context)
        {
            return false;
        }

        while (operand.Kind == SyntaxKind.ParenthesizedExpression)
        {
            operand = (SyntaxNode)operand.Children[1];
        }
        if (operand.Kind != SyntaxKind.NameExpression)
        {
            return false;
        }
        var name = ((SyntaxTokenElement)operand.Children[0]).Token.Name;
        if (TryFindVariable(name, out _))
        {
            return false;
        }
        for (var ordinal = 0;
            ordinal < context.Template.TypeParameters.Length; ordinal++)
        {
            if (context.Template.TypeParameters[ordinal].Name != name)
            {
                continue;
            }

            formalOrdinal = ordinal;
            formalType = context.Types[ordinal];
            return true;
        }

        return false;
    }

    private T BindWithGenericUniqueRefinements<T>(
        ImmutableArray<BoundGenericUniquePropertyFact> facts,
        Func<T> bind, out Dictionary<PrimitiveType, bool> result)
    {
        var previous = new Dictionary<PrimitiveType, bool>(
            _genericUniqueRefinements);
        result = new Dictionary<PrimitiveType, bool>(previous);
        try
        {
            foreach (var fact in facts)
            {
                _genericUniqueRefinements[fact.FormalType] = fact.IsUnique;
            }
            var value = bind();
            result = new Dictionary<PrimitiveType, bool>(
                _genericUniqueRefinements);
            return value;
        }
        finally
        {
            _genericUniqueRefinements.Clear();
            foreach (var (type, isUnique) in previous)
            {
                _genericUniqueRefinements.Add(type, isUnique);
            }
        }
    }

    private void SetGenericUniqueRefinements(
        IReadOnlyDictionary<PrimitiveType, bool> refinements)
    {
        _genericUniqueRefinements.Clear();
        foreach (var (type, isUnique) in refinements)
        {
            _genericUniqueRefinements.Add(type, isUnique);
        }
    }

    private static Dictionary<PrimitiveType, bool>
        MergeGenericUniqueRefinements(
            Dictionary<PrimitiveType, bool> first,
            Dictionary<PrimitiveType, bool> second)
    {
        var result = new Dictionary<PrimitiveType, bool>();
        foreach (var (type, isUnique) in first)
        {
            if (second.TryGetValue(type, out var otherIsUnique)
                && otherIsUnique == isUnique)
            {
                result.Add(type, isUnique);
            }
        }
        return result;
    }

    private bool CanCompleteGenericPropertyStatement(
        BoundStatement statement) =>
        statement switch {
            BoundReturn or BoundBreak or BoundContinue => false,
            BoundBlock block => block.Statements.All(
                CanCompleteGenericPropertyStatement),
            BoundIf conditional => CanCompleteIf(conditional),
            BoundTryFinally cleanup =>
                CanCompleteGenericPropertyStatement(cleanup.Body)
                && CanCompleteGenericPropertyStatement(cleanup.Finally),
            BoundUnsafe unsafeStatement =>
                CanCompleteGenericPropertyStatement(unsafeStatement.Body),
            BoundTryCatch recovery =>
                (CanCompleteGenericPropertyStatement(recovery.Body)
                    || CanCompleteGenericPropertyStatement(recovery.Catch))
                && (recovery.Finally is null
                    || CanCompleteGenericPropertyStatement(
                        recovery.Finally)),
            _ => true,
        };

    private bool CanCompleteIf(BoundIf conditional)
    {
        var flow = BoundGenericUniquePropertyFlowAnalysis.Analyze(
            conditional.Condition, _variables);
        return (flow.CanBeTrue
                && CanCompleteGenericPropertyStatement(conditional.Then))
            || (flow.CanBeFalse
                && (conditional.Else is null
                    || CanCompleteGenericPropertyStatement(
                        conditional.Else)));
    }

    private int GenericUniqueRefinementOrdinal(PrimitiveType type)
    {
        if (_genericContext is not GenericInstantiation context
            || !_genericUniqueRefinements.TryGetValue(type,
                out var isUnique) || !isUnique)
        {
            return -1;
        }

        for (var ordinal = 0; ordinal < context.Types.Length; ordinal++)
        {
            if (context.Types[ordinal] == type)
            {
                return ordinal;
            }
        }

        return -1;
    }

    private int GenericCopyRefinementOrdinal(PrimitiveType type)
    {
        if (_genericContext is not GenericInstantiation context
            || !_genericUniqueRefinements.TryGetValue(type,
                out var isUnique) || isUnique
            || !IsUniqueWithoutGenericRefinement(type))
        {
            return -1;
        }

        for (var ordinal = 0; ordinal < context.Types.Length; ordinal++)
        {
            if (context.Types[ordinal] == type)
            {
                return ordinal;
            }
        }

        return -1;
    }

    private static BoundMove? FindUnrefinedGenericUniqueMove(BoundBlock body,
        GenericTemplate template, ImmutableArray<PrimitiveType> types)
    {
        BoundMove? unrefinedMove = null;
        _ = BoundTreeTraversal.VisitExpressions(body, expression => {
            if (expression is not BoundMove move || unrefinedMove is not null)
            {
                return;
            }

            for (var ordinal = 0; ordinal < types.Length; ordinal++)
            {
                if (template.TypeParameters[ordinal].OwnershipDomain == 3
                    && move.Type == types[ordinal]
                    && move.GenericUniqueRefinementOrdinal != ordinal)
                {
                    unrefinedMove = move;
                }
            }
        });
        return unrefinedMove;
    }

    private bool IsWritableGenericMethodTypeArgument(PrimitiveType type)
    {
        if (BoundSpanTypeTypes.IsValidType(type, _spanTypes)
            && _spanTypes[BoundSpanTypeTypes.Index(type)].IsWritable)
        {
            return true;
        }
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

    private static GenericMethodCallArguments EmptyGenericMethodCallArguments(
        GenericTemplate template) =>
        new(new PrimitiveType?[template.TypeParameters.Length],
            new bool[template.TypeParameters.Length],
            new bool[template.TypeParameters.Length],
            new SyntaxNode?[template.ConstantFormals.Length],
            new bool[template.ConstantFormals.Length],
            new bool[template.ConstantFormals.Length]) {
            InferredConstantArguments =
                new BoundGenericMethodConstantArgument?[
                    template.ConstantFormals.Length],
            PreboundConstantArguments =
                new BoundGenericMethodConstantArgument?[
                    template.ConstantFormals.Length],
            InferredTypeDefaultArguments =
                new PrimitiveType?[template.TypeParameters.Length],
        };

    private bool TryReadGenericMethodArguments(SyntaxNode callee,
        GenericTemplate template, out GenericMethodCallArguments actuals)
    {
        actuals = EmptyGenericMethodCallArguments(template);
        var arguments = callee.Kind switch {
            SyntaxKind.GenericNameExpression
                when callee.Children.Length >= 2
                    && callee.Children[1] is SyntaxNode genericArguments
                => genericArguments,
            SyntaxKind.MemberAccessExpression
                when callee.Children.Length >= 4
                    && callee.Children[3] is SyntaxNode memberArguments
                => memberArguments,
            _ => null,
        };
        if (arguments is null)
        {
            return false;
        }

        var suppliedArguments = arguments.Children
            .OfType<SyntaxNode>().ToArray();
        if (suppliedArguments.Length > template.FormalOrder.Length)
        {
            return false;
        }
        for (var ordinal = 0; ordinal < suppliedArguments.Length; ordinal++)
        {
            var argument = suppliedArguments[ordinal];
            var formalSlot = template.FormalOrder[ordinal];
            if (formalSlot.Ordinal != ordinal)
            {
                return false;
            }

            var diagnosticCount = DiagnosticCount;
            if (formalSlot.Kind == BoundGenericMethodFormalKind.Type)
            {
                var typeOrdinal = formalSlot.TypeFormalOrdinal;
                if ((uint)typeOrdinal
                    >= (uint)template.TypeParameters.Length)
                {
                    return false;
                }

                if (argument.Kind
                    == SyntaxKind.GenericInferenceArgument)
                {
                    actuals.RequireTypeInference[typeOrdinal] = true;
                }
                else if (argument.Kind
                    == SyntaxKind.GenericDefaultArgument)
                {
                    actuals.ExplicitTypeDefaults[typeOrdinal] = true;
                }
                else if (TryResolveGenericArgument(argument,
                    out var typeArgument))
                {
                    actuals.TypeArguments[typeOrdinal] = typeArgument;
                }
                else
                {
                    if (argument.Kind
                        == SyntaxKind.ConstantGenericArgument)
                    {
                        if (DiagnosticCount == diagnosticCount)
                        {
                            Report("TRM3005",
                                "A constant actual cannot satisfy a type formal.",
                                argument.Span);
                        }
                    }
                    else
                    {
                        Report("TRM3011",
                            "A supplied type argument must resolve to one complete type.",
                            argument.Span);
                    }
                    return false;
                }
            }
            else if (formalSlot.Kind
                    == BoundGenericMethodFormalKind.Constant
                && (uint)formalSlot.ConstantFormalOrdinal
                    < (uint)template.ConstantFormals.Length)
            {
                var constantOrdinal = formalSlot.ConstantFormalOrdinal;
                if (argument.Kind
                    == SyntaxKind.GenericInferenceArgument)
                {
                    actuals.RequireConstantInference[constantOrdinal] = true;
                }
                else if (argument.Kind
                    == SyntaxKind.GenericDefaultArgument)
                {
                    actuals.ExplicitConstantDefaults[constantOrdinal] = true;
                }
                else if (argument.Kind
                        is SyntaxKind.ConstantGenericArgument
                            or SyntaxKind.TypeName)
                {
                    actuals.ConstantArguments[constantOrdinal] = argument;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private bool TryCompleteGenericMethodConstantArguments(
        GenericTemplate template, ImmutableArray<PrimitiveType> types,
        GenericMethodCallArguments supplied, TextSpan useSite,
        out ImmutableArray<BoundGenericMethodConstantArgument> arguments)
    {
        var result = ImmutableArray.CreateBuilder<
            BoundGenericMethodConstantArgument>(
                template.ConstantFormals.Length);
        for (var ordinal = 0;
            ordinal < template.ConstantFormals.Length; ordinal++)
        {
            var formal = template.ConstantFormals[ordinal];
            if (!TryResolveGenericMethodConstantDomain(template, formal,
                types, useSite, out var domainTerm, out var domainType,
                out var carrierType,
                [.. result.Select(static argument =>
                    (BoundGenericMethodConstantArgument?)argument)]))
            {
                Report("TRM3005",
                    $"Constant formal '{formal.Name}' has no supported closed domain at this call.",
                    useSite);
                arguments = [];
                return false;
            }

            var inferred = supplied.InferredConstantArguments[ordinal];
            if (supplied.RequireConstantInference[ordinal]
                && inferred is null)
            {
                Report("TRM3011",
                    $"Constant formal '{formal.Name}' requires explicit or structurally inferred constant evidence.",
                    useSite);
                arguments = [];
                return false;
            }

            BoundGenericMethodConstantArgument argument;
            if (supplied.PreboundConstantArguments[ordinal] is
                BoundGenericMethodConstantArgument prebound)
            {
                if (prebound.Ordinal != formal.Ordinal
                    || prebound.ConstantOrdinal != formal.ConstantOrdinal
                    || !AreGenericMethodTypeTermsEquivalent(
                        prebound.Domain, domainTerm)
                    || (prebound.Kind
                            == BoundGenericMethodConstantArgumentKind.Value
                        ? !GenericNominalTypesEquivalent(
                            prebound.DomainType, domainType)
                            || prebound.CarrierType != carrierType
                            || !prebound.Bits.IsCanonicalFor(carrierType)
                        : prebound.Kind
                                != BoundGenericMethodConstantArgumentKind.Formal
                            || !IsValidGenericMethodConstantFormalOwnerIdentity(
                                prebound)))
                {
                    arguments = [];
                    return false;
                }

                argument = prebound with {
                    Domain = domainTerm,
                    DomainType = prebound.Kind
                            == BoundGenericMethodConstantArgumentKind.Value
                        ? domainType : PrimitiveType.Void,
                    CarrierType = prebound.Kind
                            == BoundGenericMethodConstantArgumentKind.Value
                        ? carrierType : PrimitiveType.Void,
                };
            }
            else if (supplied.ConstantArguments[ordinal]
                is SyntaxNode syntax)
            {
                if (!TryBindGenericMethodConstantArgument(formal,
                    domainTerm, domainType, carrierType, syntax,
                    out argument))
                {
                    arguments = [];
                    return false;
                }
            }
            else if (supplied.ExplicitConstantDefaults[ordinal]
                || (inferred is null && formal.DefaultSyntax is not null))
            {
                if (formal.DefaultSyntax is null)
                {
                    Report("TRM3011",
                        $"Constant formal '{formal.Name}' has no declared default.",
                        useSite);
                    arguments = [];
                    return false;
                }
                if (!TryEvaluateGenericMethodConstantDefault(template,
                    formal, domainTerm, domainType, carrierType, result,
                    useSite, out argument))
                {
                    Report("TRM3005",
                        $"The default for constant formal '{formal.Name}' could not produce a required value in its exact domain.",
                        useSite);
                    arguments = [];
                    return false;
                }
            }
            else if (inferred is BoundGenericMethodConstantArgument
                inferredValue)
            {
                if (inferredValue.Kind
                    == BoundGenericMethodConstantArgumentKind.Value)
                {
                    argument = inferredValue with {
                        Domain = domainTerm,
                        DomainType = domainType,
                        CarrierType = carrierType,
                    };
                    if (!argument.Bits.IsCanonicalFor(carrierType))
                    {
                        Report("TRM3005",
                            $"Inferred evidence for constant formal '{formal.Name}' is not canonical in its exact domain.",
                            useSite);
                        arguments = [];
                        return false;
                    }
                }
                else if (inferredValue.Kind
                        == BoundGenericMethodConstantArgumentKind.Formal
                    && IsValidGenericMethodConstantFormalOwnerIdentity(
                        inferredValue)
                    && AreGenericMethodTypeTermsEquivalent(
                        inferredValue.Domain, domainTerm))
                {
                    argument = inferredValue with {
                        Domain = domainTerm,
                        DomainType = PrimitiveType.Void,
                        CarrierType = PrimitiveType.Void,
                        Bits = default,
                    };
                }
                else
                {
                    Report("TRM3005",
                        $"Inferred evidence for constant formal '{formal.Name}' has an invalid owner or domain.",
                        useSite);
                    arguments = [];
                    return false;
                }
            }
            else
            {
                Report("TRM3011",
                    $"Constant formal '{formal.Name}' requires an explicit value or a declared default.",
                    useSite);
                arguments = [];
                return false;
            }

            if (inferred is BoundGenericMethodConstantArgument
                    inferredEvidence
                && !GenericMethodConstantArgumentsEqual(argument,
                    inferredEvidence with {
                        Domain = domainTerm,
                        DomainType = inferredEvidence.Kind
                                == BoundGenericMethodConstantArgumentKind.Value
                            ? domainType : PrimitiveType.Void,
                        CarrierType = inferredEvidence.Kind
                                == BoundGenericMethodConstantArgumentKind.Value
                            ? carrierType : PrimitiveType.Void,
                    }))
            {
                Report("TRM3007",
                    $"The supplied value for constant formal '{formal.Name}' conflicts with its inferred evidence.",
                    useSite);
                arguments = [];
                return false;
            }

            result.Add(argument);
        }

        arguments = result.MoveToImmutable();
        return true;
    }

    private static bool IsValidGenericMethodConstantFormalOwnerIdentity(
        BoundGenericMethodConstantArgument argument)
        => argument.CallerDefinitionOrdinal >= 0
            && argument.CallerConstantFormalOrdinal >= 0
            && (argument.CallerFormalOwnerKind
                    == BoundGenericTypeFormalOwnerKind.GenericMethod
                ? argument.CallerFormalOrdinal >= 0
                : argument.CallerFormalOwnerKind
                        == BoundGenericTypeFormalOwnerKind.SourceGenericType
                    && argument.CallerFormalOrdinal == -1);

    private bool TryResolveGenericMethodConstantDomain(
        GenericTemplate template, GenericMethodConstantFormal formal,
        ImmutableArray<PrimitiveType> types, TextSpan useSite,
        out BoundGenericMethodTypeTerm domainTerm,
        out PrimitiveType domainType, out PrimitiveType carrierType,
        ImmutableArray<BoundGenericMethodConstantArgument?>
            constantArguments = default)
    {
        domainTerm = null!;
        domainType = PrimitiveType.Void;
        carrierType = PrimitiveType.Void;
        var typeArguments = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(types.Length);
        foreach (var type in types)
        {
            if (!TryGetGenericMethodTypeArgumentTerm(type,
                out var typeTerm))
            {
                return false;
            }
            typeArguments.Add(typeTerm);
        }

        var checkedConstantArguments = constantArguments.IsDefault
            ? []
            : constantArguments;
        var typeArgumentsArray = typeArguments.MoveToImmutable();
        if (template.SourceGenericTypeDefinitionIndex >= 0)
        {
            if (!TryConvertGenericMethodConstantArguments(
                    checkedConstantArguments, out var sourceArguments)
                || !BoundGenericMethodTypeTerms
                    .TrySubstituteSourceGenericTypeFormals(formal.Domain,
                        typeArgumentsArray,
                        template.SourceGenericTypeDefinitionIndex,
                        sourceArguments, out domainTerm))
            {
                return false;
            }
        }
        else if (!BoundGenericMethodTypeTerms.TrySubstitute(formal.Domain,
            typeArgumentsArray, checkedConstantArguments,
            out domainTerm))
        {
            return false;
        }
        if (!BoundGenericMethodTypeTerms.TryGetFormalDependencies(
            domainTerm, out var dependencies))
        {
            return false;
        }
        if (!BoundGenericMethodTypeTerms
                .TryGetConstantFormalDependencies(domainTerm,
                    out var constantDependencies))
        {
            return false;
        }
        if (!dependencies.IsEmpty || !constantDependencies.IsEmpty)
        {
            return true;
        }

        return TryResolveGenericMethodTypeTerm(domainTerm, useSite,
                out domainType)
            && TryGetGenericMethodConstantCarrierType(domainType,
                out carrierType);
    }

    private static bool TryConvertGenericMethodConstantArguments(
        ImmutableArray<BoundGenericMethodConstantArgument?> sourceArguments,
        out ImmutableArray<BoundGenericMethodTypeConstantArgument>
            constantArguments)
    {
        constantArguments = [];
        if (sourceArguments.IsDefault)
        {
            return false;
        }

        var result = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeConstantArgument>(
                sourceArguments.Length);
        foreach (var argument in sourceArguments)
        {
            if (argument is null)
            {
                return false;
            }

            if (argument.Kind
                == BoundGenericMethodConstantArgumentKind.Value)
            {
                if (argument.DomainType == PrimitiveType.Void
                    || argument.CarrierType == PrimitiveType.Void
                    || !argument.Bits.IsCanonicalFor(argument.CarrierType))
                {
                    return false;
                }

                result.Add(new BoundGenericMethodTypeConstantArgument(
                    BoundGenericMethodTypeConstantArgumentKind.Value, -1,
                    argument.DomainType, argument.CarrierType,
                    argument.Bits));
                continue;
            }

            if (argument.Kind
                    != BoundGenericMethodConstantArgumentKind.Formal
                || argument.CallerDefinitionOrdinal < 0
                || argument.CallerConstantFormalOrdinal < 0)
            {
                return false;
            }
            if (argument.CallerFormalOwnerKind
                == BoundGenericTypeFormalOwnerKind.GenericMethod)
            {
                if (argument.CallerFormalOrdinal < 0)
                {
                    return false;
                }
            }
            else if (argument.CallerFormalOwnerKind
                    != BoundGenericTypeFormalOwnerKind.SourceGenericType
                || argument.CallerFormalOrdinal != -1)
            {
                return false;
            }

            result.Add(new BoundGenericMethodTypeConstantArgument(
                BoundGenericMethodTypeConstantArgumentKind.Formal,
                argument.CallerConstantFormalOrdinal,
                PrimitiveType.Void, PrimitiveType.Void, default) {
                FormalDefinitionOrdinal =
                    argument.CallerDefinitionOrdinal,
                FormalOwnerKind = argument.CallerFormalOwnerKind,
            });
        }

        constantArguments = result.MoveToImmutable();
        return true;
    }

    private static bool TryGetGenericMethodSpanFormalOrdinal(
        BoundSpanTypeDefinition spanType, GenericInstantiation context,
        out int formalOrdinal)
    {
        formalOrdinal = -1;
        if (spanType.ElementFormalDefinitionOrdinal == -1)
        {
            return spanType.ElementFormalOrdinal == -1
                && !spanType.IsElementWritable;
        }

        int expectedOwner;
        if (spanType.ElementFormalDefinitionOrdinal >= 0)
        {
            if (context.Template.DefinitionOrdinal < 0)
            {
                return false;
            }
            expectedOwner = context.Template.DefinitionOrdinal;
        }
        else if (context.Template.ImportedMethod
            is BoundGenericMethodImport imported
            && imported.FormalTypeOwnerOrdinal >= 0)
        {
            expectedOwner = checked(
                BoundSpanTypeContract.ImportedFormalDefinitionOrdinal
                - imported.FormalTypeOwnerOrdinal);
        }
        else
        {
            return false;
        }

        if (spanType.ElementFormalDefinitionOrdinal != expectedOwner)
        {
            return false;
        }

        formalOrdinal = spanType.ElementFormalOrdinal;
        return (uint)formalOrdinal < (uint)context.Types.Length
            && (uint)formalOrdinal
                < (uint)context.Template.TypeParameters.Length
            && context.Types[formalOrdinal] == spanType.ElementType
            && (!spanType.IsElementWritable
                || context.Template.TypeParameters[formalOrdinal]
                    .RequiresWritable);
    }

    private bool TryGetGenericMethodTypeArgumentTerm(PrimitiveType type,
        out BoundGenericMethodTypeTerm term)
    {
        if (_genericProofFunctions is not null
            && _genericContext is GenericInstantiation context)
        {
            for (var ordinal = 0; ordinal < context.Types.Length; ordinal++)
            {
                if (context.Types[ordinal] == type)
                {
                    term = new BoundGenericMethodTypeTerm(
                        BoundGenericMethodTypeTermKind.Formal,
                        PrimitiveType.Void, ordinal, -1, [], false, false,
                        false, false);
                    return true;
                }
            }

            var structureIndex = (int)type - 100;
            var instance = _sourceGenericTypeInstances.FirstOrDefault(
                candidate => candidate.StructureIndex == structureIndex);
            if (structureIndex >= 0
                && instance is not null
                && (uint)instance.DefinitionIndex
                    < (uint)_sourceGenericTypeDefinitions.Count)
            {
                var typeArguments = ImmutableArray.CreateBuilder<
                    BoundGenericMethodTypeTerm>(
                        instance.TypeArguments.Length);
                foreach (var typeArgument in instance.TypeArguments)
                {
                    if (!TryGetGenericMethodTypeArgumentTerm(typeArgument,
                        out var nestedTerm))
                    {
                        term = null!;
                        return false;
                    }
                    typeArguments.Add(nestedTerm);
                }
                if (!TryConvertSourceGenericConstantArguments(
                    instance.ConstantArguments, out var constantArguments))
                {
                    term = null!;
                    return false;
                }

                term = new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.SourceGeneric,
                    PrimitiveType.Void, -1, instance.DefinitionIndex,
                    typeArguments.MoveToImmutable(), false, false, false,
                    false) {
                    ConstantArguments = constantArguments,
                };
                return true;
            }
        }

        if (BoundSpanTypeTypes.IsValidType(type, _spanTypes))
        {
            var spanType = _spanTypes[BoundSpanTypeTypes.Index(type)];
            if (_genericContext is not GenericInstantiation spanContext
                || !TryGetGenericMethodSpanFormalOrdinal(spanType,
                    spanContext, out _)
                || !TryGetGenericMethodTypeArgumentTerm(
                    spanType.ElementType, out var elementTerm)
                || (spanType.IsElementWritable
                    && elementTerm.Kind
                        != BoundGenericMethodTypeTermKind.Formal))
            {
                term = null!;
                return false;
            }

            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Span,
                PrimitiveType.Void, -1, -1, [elementTerm],
                spanType.IsWritable, false, spanType.IsInitialization,
                false) {
                IsElementWritable = spanType.IsElementWritable,
            };
            return true;
        }

        if (BoundGenericNominalTypeTypes.IsType(type))
        {
            var instanceIndex = BoundGenericNominalTypeTypes.Index(type);
            if ((uint)instanceIndex
                    >= (uint)_genericNominalTypeInstances.Count)
            {
                term = null!;
                return false;
            }

            var instance = _genericNominalTypeInstances[instanceIndex];
            var definition = _genericNominalTypeDefinitions.FirstOrDefault(
                candidate => candidate.Identity == instance.Definition);
            if (definition is null || instance.Arguments.IsDefault
                || definition.Formals.Length != instance.Arguments.Length)
            {
                term = null!;
                return false;
            }

            var typeArguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeTerm>(
                    definition.Formals.Count(static formal =>
                        formal.Kind == BoundGenericArgumentKind.Type));
            var constantArguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeConstantArgument>(
                    definition.Formals.Count(static formal =>
                        formal.Kind == BoundGenericArgumentKind.Constant));
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                var formal = definition.Formals[ordinal];
                var actual = instance.Arguments[ordinal];
                if (formal.Kind == BoundGenericArgumentKind.Type)
                {
                    if (actual.Kind != BoundGenericArgumentKind.Type
                        || !TryGetGenericMethodTypeArgumentTerm(actual,
                            out var nestedTerm))
                    {
                        term = null!;
                        return false;
                    }

                    var qualifiedArgument
                        = ApplyGenericQueryTypeQualifiers(nestedTerm,
                            actual.Qualifiers);
                    if (!TryNormalizeGenericMethodFormalWriteQualifier(
                            qualifiedArgument, context.Template,
                            out var normalizedArgument))
                    {
                        term = null!;
                        return false;
                    }
                    typeArguments.Add(normalizedArgument);
                    continue;
                }

                if (formal.Kind != BoundGenericArgumentKind.Constant)
                {
                    term = null!;
                    return false;
                }

                if (actual.HasDecodedConstant
                    && actual.ConstantDomainType is PrimitiveType domainType
                    && actual.ConstantCarrierType
                        is PrimitiveType carrierType
                    && new ScalarBits(actual.ConstantLow,
                        actual.ConstantHigh)
                        .IsCanonicalFor(carrierType))
                {
                    constantArguments.Add(
                        new BoundGenericMethodTypeConstantArgument(
                            BoundGenericMethodTypeConstantArgumentKind.Value,
                            -1, domainType, carrierType,
                            new ScalarBits(actual.ConstantLow,
                                actual.ConstantHigh)));
                    continue;
                }

                if (actual.HasConstantFormalIdentity)
                {
                    constantArguments.Add(
                        new BoundGenericMethodTypeConstantArgument(
                            BoundGenericMethodTypeConstantArgumentKind.Formal,
                            actual.ConstantFormalOrdinal,
                            PrimitiveType.Void, PrimitiveType.Void,
                            default) {
                            FormalDefinitionOrdinal =
                                actual.ConstantFormalDefinitionOrdinal,
                            FormalOwnerKind =
                                actual.ConstantFormalOwnerKind,
                        });
                    continue;
                }

                term = null!;
                return false;
            }

            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.ImportedGeneric,
                PrimitiveType.Void, -1, -1,
                typeArguments.MoveToImmutable(),
                (instance.Qualifiers & 0x01) != 0,
                (instance.Qualifiers & 0x02) != 0, false, false) {
                NominalTypeIdentity = instance.Definition,
                ConstantArguments = constantArguments.MoveToImmutable(),
            };
            if (instance.TypeSpecForm == 0x06)
            {
                term = new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.OptionalValue,
                    PrimitiveType.Void, -1, -1, [term], false, false,
                    false, false);
            }
            return true;
        }

        if (CheckedReferenceTypes.IsType(type))
        {
            var remaining = SemanticVerifier.MaximumAnalysisVisits;
            if (TryBuildGenericQueryTypeIdentityTerm(type, context,
                    ref remaining, 0, out term))
            {
                return true;
            }

            term = null!;
            return false;
        }

        if (ManagedClassTypes.IsOptional(type))
        {
            if (!TryGetGenericMethodTypeArgumentTerm(
                    ManagedClassTypes.Required(type), out var requiredTerm))
            {
                term = null!;
                return false;
            }

            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.OptionalValue,
                PrimitiveType.Void, -1, -1, [requiredTerm], false, false,
                false, false);
            return true;
        }

        term = new BoundGenericMethodTypeTerm(
            BoundGenericMethodTypeTermKind.Concrete, type, -1, -1, [],
            false, false, false, false);
        return true;
    }

    private bool TryGetGenericMethodTypeArgumentTerm(
        BoundGenericNominalTypeArgument argument,
        out BoundGenericMethodTypeTerm term)
    {
        if (argument.TypeArgumentTerm is
            BoundGenericMethodTypeTerm typeArgumentTerm)
        {
            if (!BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                    typeArgumentTerm, out var dependencies)
                || (!dependencies.IsEmpty
                    && (_genericContext is not GenericInstantiation context
                        || !argument.HasTypeFormalOwnerIdentity
                        || argument.TypeFormalDefinitionOrdinal
                            != context.Template.FormalTypeOwnerOrdinal
                        || dependencies.Any(dependency =>
                            (uint)dependency
                                >= (uint)context.Types.Length))))
            {
                term = null!;
                return false;
            }

            term = typeArgumentTerm;
            return true;
        }

        return TryGetGenericMethodTypeArgumentTerm(argument.Type, out term);
    }

    private static bool TryConvertSourceGenericConstantArguments(
        ImmutableArray<BoundGenericMethodConstantArgument> sourceArguments,
        out ImmutableArray<BoundGenericMethodTypeConstantArgument>
            constantArguments)
    {
        constantArguments = [];
        if (sourceArguments.IsDefault
            || !TryConvertGenericMethodConstantArguments(
                [.. sourceArguments.Select(static argument =>
                    (BoundGenericMethodConstantArgument?)argument)],
                out var converted))
        {
            return false;
        }

        constantArguments = converted;
        return true;
    }

    private bool TryGetGenericQueryTypeIdentityTerm(PrimitiveType type,
        out BoundGenericMethodTypeTerm term)
    {
        term = null!;
        if (_genericContext is not GenericInstantiation context)
        {
            return false;
        }

        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        if (!HasGenericQueryIdentitySubstitution(type, context,
            ref remaining, 0, out var hasSubstitution))
        {
            return false;
        }
        if (!hasSubstitution)
        {
            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Concrete, type, -1, -1,
                [], false, false, false, false) {
                AllowsVoid = type == PrimitiveType.Void,
            };
            return true;
        }

        remaining = SemanticVerifier.MaximumAnalysisVisits;
        if (!TryBuildGenericQueryTypeIdentityTerm(type, context,
            ref remaining, 0, out term)
            || !BoundGenericMethodTypeTerms.TryGetFormalDependencies(term,
                out var typeFormalDependencies)
            || !BoundGenericMethodTypeTerms
                .TryGetConstantFormalDependencies(term,
                    out var constantFormalDependencies)
            || (typeFormalDependencies.IsEmpty
                && constantFormalDependencies.IsEmpty))
        {
            term = null!;
            return false;
        }
        return true;
    }

    private bool HasGenericQueryIdentitySubstitution(
        PrimitiveType type, GenericInstantiation context,
        ref int remaining, int depth,
        out bool hasSubstitution)
    {
        hasSubstitution = false;
        if (depth > SemanticVerifier.MaximumDepth || --remaining < 0)
        {
            return false;
        }
        if (context.Types.Contains(type))
        {
            hasSubstitution = true;
            return true;
        }
        if (BoundGenericNominalTypeTypes.IsType(type))
        {
            var index = BoundGenericNominalTypeTypes.Index(type);
            if ((uint)index >= (uint)_genericNominalTypeInstances.Count)
            {
                return false;
            }
            foreach (var argument in _genericNominalTypeInstances[index]
                .Arguments)
            {
                if (argument.Kind == BoundGenericArgumentKind.Type)
                {
                    if (argument.Type == PrimitiveType.Void
                        || !HasGenericQueryIdentitySubstitution(
                            argument.Type, context, ref remaining,
                            depth + 1, out var argumentHasSubstitution))
                    {
                        return false;
                    }
                    if (argumentHasSubstitution)
                    {
                        hasSubstitution = true;
                        return true;
                    }
                }
                else if (argument.Kind
                        == BoundGenericArgumentKind.Constant
                    && argument.HasConstantFormalIdentity)
                {
                    hasSubstitution = true;
                    return true;
                }
            }
        }
        else if (CheckedReferenceTypes.IsType(type))
        {
            var index = CheckedReferenceTypes.Index(type);
            if ((uint)index >= (uint)_checkedReferenceTypes.Count)
            {
                return false;
            }
            return HasGenericQueryIdentitySubstitution(
                _checkedReferenceTypes[index].ReferentType, context,
                ref remaining, depth + 1, out hasSubstitution);
        }
        else if (BoundSpanTypeTypes.IsType(type))
        {
            var index = BoundSpanTypeTypes.Index(type);
            if ((uint)index >= (uint)_spanTypes.Count)
            {
                return false;
            }
            var spanType = _spanTypes[index];
            return TryGetGenericMethodSpanFormalOrdinal(spanType, context,
                    out _)
                && HasGenericQueryIdentitySubstitution(
                    spanType.ElementType, context, ref remaining,
                    depth + 1, out hasSubstitution);
        }
        else if (ManagedClassTypes.IsOptional(type))
        {
            return HasGenericQueryIdentitySubstitution(
                ManagedClassTypes.Required(type), context, ref remaining,
                depth + 1, out hasSubstitution);
        }
        else if (NativePointerTypes.IsType(type))
        {
            var index = NativePointerTypes.Index(type);
            if ((uint)index >= (uint)_nativePointerTypes.Count)
            {
                return false;
            }
            return HasGenericQueryIdentitySubstitution(
                _nativePointerTypes[index].PointeeType, context,
                ref remaining, depth + 1, out hasSubstitution);
        }
        else if (NativeFixedArrayTypes.IsType(type))
        {
            var index = NativeFixedArrayTypes.Index(type);
            if ((uint)index >= (uint)_nativeFixedArrayTypes.Count)
            {
                return false;
            }
            return HasGenericQueryIdentitySubstitution(
                _nativeFixedArrayTypes[index].ElementType, context,
                ref remaining, depth + 1, out hasSubstitution);
        }
        else if (NativeFunctionPointerTypes.IsType(type))
        {
            var index = NativeFunctionPointerTypes.Index(type);
            if ((uint)index >= (uint)_nativeFunctionPointerSignatures.Count)
            {
                return false;
            }
            var signature = _nativeFunctionPointerSignatures[index];
            foreach (var parameterType in signature.ParameterTypes)
            {
                if (!HasGenericQueryIdentitySubstitution(parameterType,
                    context, ref remaining, depth + 1,
                    out var parameterHasSubstitution))
                {
                    return false;
                }
                if (parameterHasSubstitution)
                {
                    hasSubstitution = true;
                    return true;
                }
            }
            return HasGenericQueryIdentitySubstitution(signature.ReturnType,
                context, ref remaining, depth + 1,
                out hasSubstitution);
        }
        else
        {
            var structureIndex = (int)type - 100;
            var instance = _sourceGenericTypeInstances.FirstOrDefault(
                candidate => candidate.StructureIndex == structureIndex);
            if (structureIndex >= 0 && instance is not null)
            {
                foreach (var typeArgument in instance.TypeArguments)
                {
                    if (!HasGenericQueryIdentitySubstitution(typeArgument,
                        context, ref remaining, depth + 1,
                        out var argumentHasSubstitution))
                    {
                        return false;
                    }
                    if (argumentHasSubstitution)
                    {
                        hasSubstitution = true;
                        return true;
                    }
                }
            }
        }

        return true;
    }

    private bool TryBuildGenericQueryTypeIdentityTerm(
        PrimitiveType type, GenericInstantiation context,
        ref int remaining, int depth,
        out BoundGenericMethodTypeTerm term)
    {
        term = null!;
        if (depth > SemanticVerifier.MaximumDepth || --remaining < 0)
        {
            return false;
        }
        for (var ordinal = 0; ordinal < context.Types.Length; ordinal++)
        {
            if (context.Types[ordinal] == type)
            {
                term = new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Formal,
                    PrimitiveType.Void, ordinal, -1, [], false, false,
                    false, false);
                return true;
            }
        }

        if (BoundGenericNominalTypeTypes.IsType(type))
        {
            var index = BoundGenericNominalTypeTypes.Index(type);
            if ((uint)index >= (uint)_genericNominalTypeInstances.Count)
            {
                return false;
            }

            var instance = _genericNominalTypeInstances[index];
            var definition = _genericNominalTypeDefinitions
                .FirstOrDefault(candidate =>
                    candidate.Identity == instance.Definition);
            if (definition is null
                || definition.Formals.Length != instance.Arguments.Length
                || (instance.Qualifiers & ~0x03) != 0)
            {
                return false;
            }

            var typeArguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeTerm>();
            var constantArguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeConstantArgument>();
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                var formal = definition.Formals[ordinal];
                var argument = instance.Arguments[ordinal];
                if (formal.Kind == BoundGenericArgumentKind.Type)
                {
                    if (argument.Kind != BoundGenericArgumentKind.Type
                        || argument.Type == PrimitiveType.Void)
                    {
                        return false;
                    }
                    BoundGenericMethodTypeTerm typeArgument;
                    if (argument.TypeArgumentTerm is
                        BoundGenericMethodTypeTerm retainedTypeArgument)
                    {
                        if (!BoundGenericMethodTypeTerms
                                .TryGetFormalDependencies(
                                    retainedTypeArgument,
                                    out var retainedDependencies)
                            || (!retainedDependencies.IsEmpty
                                && (!argument.HasTypeFormalOwnerIdentity
                                    || argument.TypeFormalDefinitionOrdinal
                                        != context.Template
                                            .FormalTypeOwnerOrdinal
                                    || retainedDependencies.Any(
                                        dependency => (uint)dependency
                                            >= (uint)context.Types.Length))))
                        {
                            return false;
                        }

                        typeArgument = retainedTypeArgument;
                    }
                    else if (!TryBuildGenericQueryTypeIdentityTerm(
                        argument.Type, context, ref remaining,
                        depth + 1, out typeArgument))
                    {
                        return false;
                    }
                    var qualifiedArgument
                        = ApplyGenericQueryTypeQualifiers(typeArgument,
                            argument.Qualifiers);
                    if (!TryNormalizeGenericMethodFormalWriteQualifier(
                            qualifiedArgument, context.Template,
                            out var normalizedArgument))
                    {
                        return false;
                    }
                    typeArguments.Add(normalizedArgument);
                }
                else if (formal.Kind
                        == BoundGenericArgumentKind.Constant
                    && argument.Kind == BoundGenericArgumentKind.Constant)
                {
                    if (argument.HasConstantFormalIdentity)
                    {
                        constantArguments.Add(
                            new BoundGenericMethodTypeConstantArgument(
                                BoundGenericMethodTypeConstantArgumentKind
                                    .Formal,
                                argument.ConstantFormalOrdinal,
                                PrimitiveType.Void, PrimitiveType.Void,
                                default) {
                                FormalDefinitionOrdinal =
                                    argument.ConstantFormalDefinitionOrdinal,
                                FormalOwnerKind =
                                    argument.ConstantFormalOwnerKind,
                            });
                    }
                    else if (argument.HasDecodedConstant
                        && argument.ConstantDomainType is PrimitiveType
                            domainType
                        && argument.ConstantCarrierType is PrimitiveType
                            carrierType
                        && new ScalarBits(argument.ConstantLow,
                                argument.ConstantHigh)
                            .IsCanonicalFor(carrierType))
                    {
                        constantArguments.Add(
                            new BoundGenericMethodTypeConstantArgument(
                                BoundGenericMethodTypeConstantArgumentKind
                                    .Value,
                                -1, domainType, carrierType,
                                new ScalarBits(argument.ConstantLow,
                                    argument.ConstantHigh)));
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.ImportedGeneric,
                PrimitiveType.Void, -1, -1,
                typeArguments.ToImmutable(),
                (instance.Qualifiers & 0x01) != 0,
                (instance.Qualifiers & 0x02) != 0, false, false) {
                NominalTypeIdentity = instance.Definition,
                ConstantArguments = constantArguments.ToImmutable(),
            };
            if (instance.TypeSpecForm == 0x06)
            {
                term = new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.OptionalValue,
                    PrimitiveType.Void, -1, -1, [term], false, false,
                    false, false);
            }
            return true;
        }

        if (BoundSpanTypeTypes.IsValidType(type, _spanTypes))
        {
            var spanType = _spanTypes[BoundSpanTypeTypes.Index(type)];
            if (!TryGetGenericMethodSpanFormalOrdinal(spanType, context,
                    out _)
                || !TryBuildGenericQueryTypeIdentityTerm(
                    spanType.ElementType, context, ref remaining,
                    depth + 1, out var elementType)
                || (spanType.IsElementWritable
                    && elementType.Kind
                        != BoundGenericMethodTypeTermKind.Formal))
            {
                return false;
            }

            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Span,
                PrimitiveType.Void, -1, -1, [elementType],
                spanType.IsWritable, false, spanType.IsInitialization,
                false) {
                IsElementWritable = spanType.IsElementWritable,
            };
            return true;
        }

        if (CheckedReferenceTypes.IsType(type))
        {
            var index = CheckedReferenceTypes.Index(type);
            if ((uint)index >= (uint)_checkedReferenceTypes.Count)
            {
                return false;
            }
            var reference = _checkedReferenceTypes[index];
            if (!TryBuildGenericQueryTypeIdentityTerm(
                reference.ReferentType, context, ref remaining,
                depth + 1, out var referent))
            {
                return false;
            }
            if (reference.IsReferentWritable)
            {
                if (referent.Kind
                        != BoundGenericMethodTypeTermKind.Formal
                    || (uint)referent.FormalOrdinal
                        >= (uint)context.Template.TypeParameters.Length
                    || !context.Template.TypeParameters[
                        referent.FormalOrdinal].RequiresWritable)
                {
                    return false;
                }
            }
            term = reference.IsOptionalValue
                ? new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.OptionalValue,
                    PrimitiveType.Void, -1, -1, [referent], false, false,
                    false, false)
                : new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.CheckedReference,
                    PrimitiveType.Void, -1, -1, [referent],
                    reference.IsWritable, reference.IsGarbageCollected,
                    reference.IsInitialization, reference.IsOptional);
            return true;
        }

        if (ManagedClassTypes.IsOptional(type))
        {
            if (!TryBuildGenericQueryTypeIdentityTerm(
                ManagedClassTypes.Required(type), context,
                ref remaining, depth + 1, out var required))
            {
                return false;
            }
            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.OptionalValue,
                PrimitiveType.Void, -1, -1, [required], false, false,
                false, false);
            return true;
        }

        if (NativePointerTypes.IsType(type))
        {
            var index = NativePointerTypes.Index(type);
            if ((uint)index >= (uint)_nativePointerTypes.Count
                || !TryBuildGenericQueryTypeIdentityTerm(
                    _nativePointerTypes[index].PointeeType, context,
                    ref remaining, depth + 1,
                    out var pointee))
            {
                return false;
            }
            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.NativePointer,
                PrimitiveType.Void, -1, -1, [pointee],
                _nativePointerTypes[index].IsWritable,
                false, false, false);
            return true;
        }

        if (NativeFixedArrayTypes.IsType(type))
        {
            var index = NativeFixedArrayTypes.Index(type);
            if ((uint)index >= (uint)_nativeFixedArrayTypes.Count
                || !TryBuildGenericQueryTypeIdentityTerm(
                    _nativeFixedArrayTypes[index].ElementType, context,
                    ref remaining, depth + 1,
                    out var element))
            {
                return false;
            }
            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.NativeFixedArray,
                PrimitiveType.Void, -1, -1, [element],
                false, false, false, false) {
                ElementCount = _nativeFixedArrayTypes[index].ElementCount,
            };
            return true;
        }

        if (NativeFunctionPointerTypes.IsType(type))
        {
            var index = NativeFunctionPointerTypes.Index(type);
            if ((uint)index >= (uint)_nativeFunctionPointerSignatures.Count)
            {
                return false;
            }
            var signature = _nativeFunctionPointerSignatures[index];
            var typeArguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeTerm>(
                    signature.ParameterTypes.Length + 1);
            foreach (var parameterType in signature.ParameterTypes)
            {
                if (!TryBuildGenericQueryTypeIdentityTerm(parameterType,
                    context, ref remaining, depth + 1,
                    out var parameter))
                {
                    return false;
                }
                typeArguments.Add(parameter);
            }
            if (signature.ReturnType == PrimitiveType.Void)
            {
                typeArguments.Add(new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Concrete,
                    PrimitiveType.Void, -1, -1, [], false, false,
                    false, false) {
                    AllowsVoid = true,
                });
            }
            else if (TryBuildGenericQueryTypeIdentityTerm(
                signature.ReturnType, context, ref remaining,
                depth + 1, out var returnType))
            {
                typeArguments.Add(returnType);
            }
            else
            {
                return false;
            }

            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.NativeFunctionPointer,
                PrimitiveType.Void, -1, -1,
                typeArguments.MoveToImmutable(), false, false, false,
                false) {
                CallingConvention = signature.CallingConvention,
                ParameterCount = signature.ParameterTypes.Length,
            };
            return true;
        }

        var structureIndex = (int)type - 100;
        var sourceInstance = _sourceGenericTypeInstances.FirstOrDefault(
            candidate => candidate.StructureIndex == structureIndex);
        if (structureIndex >= 0 && sourceInstance is not null)
        {
            var typeArguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeTerm>(
                    sourceInstance.TypeArguments.Length);
            foreach (var typeArgument in sourceInstance.TypeArguments)
            {
                if (!TryBuildGenericQueryTypeIdentityTerm(typeArgument,
                    context, ref remaining, depth + 1,
                    out var nestedTerm))
                {
                    return false;
                }
                typeArguments.Add(nestedTerm);
            }
            if (!TryConvertSourceGenericConstantArguments(
                sourceInstance.ConstantArguments,
                out var constantArguments))
            {
                return false;
            }

            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.SourceGeneric,
                PrimitiveType.Void, -1, sourceInstance.DefinitionIndex,
                typeArguments.MoveToImmutable(), false, false, false,
                false) {
                ConstantArguments = constantArguments,
            };
            return true;
        }

        term = new BoundGenericMethodTypeTerm(
            BoundGenericMethodTypeTermKind.Concrete, type, -1, -1, [],
            false, false, false, false) {
            AllowsVoid = type == PrimitiveType.Void,
        };
        return true;
    }

    private static BoundGenericMethodTypeTerm
        ApplyGenericQueryTypeQualifiers(
            BoundGenericMethodTypeTerm term, byte qualifiers)
    {
        if (qualifiers == 0
            || !BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                term, out var typeFormalDependencies)
            || !BoundGenericMethodTypeTerms
                .TryGetConstantFormalDependencies(term,
                    out var constantFormalDependencies)
            || (typeFormalDependencies.IsEmpty
                && constantFormalDependencies.IsEmpty))
        {
            return term;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.OptionalValue
            && term.TypeArguments.Length == 1)
        {
            return term with {
                TypeArguments = [
                    ApplyGenericQueryTypeQualifiers(
                        term.TypeArguments[0], qualifiers),
                ],
            };
        }

        return term with {
            IsWritable = (qualifiers & 0x01) != 0,
            IsGarbageCollected = (qualifiers & 0x02) != 0,
            IsInitialization = (qualifiers & 0x04) != 0,
        };
    }

    private bool TryGetGenericMethodConstantCarrierType(
        PrimitiveType domainType, out PrimitiveType carrierType)
    {
        if (PrimitiveTypeFacts.IsIntegerOrWide(domainType)
            || domainType == PrimitiveType.Bool)
        {
            carrierType = domainType;
            return true;
        }
        if (BoundEnumTypes.IsType(domainType))
        {
            var enumIndex = BoundEnumTypes.Index(domainType);
            if ((uint)enumIndex < (uint)_enumDefinitions.Count)
            {
                carrierType = _enumDefinitions[enumIndex].UnderlyingType;
                return true;
            }
        }
        else if (IsStruct(domainType))
        {
            var structure = _structs[(int)domainType - 100];
            if (structure.IsTransparent && !structure.IsUnique
                && structure.BlittableTargetRequirement
                    == FixedLayoutStorage.WindowsX64TargetRequirement
                && structure.Fields is [{ } field]
                && (PrimitiveTypeFacts.IsIntegerOrWide(field.Type)
                    || field.Type == PrimitiveType.Bool))
            {
                carrierType = field.Type;
                return true;
            }
        }
        else if (BoundGenericNominalTypeTypes.IsType(domainType)
            && (uint)BoundGenericNominalTypeTypes.Index(domainType)
                < (uint)_genericNominalTypeInstances.Count)
        {
            var instance = _genericNominalTypeInstances[
                BoundGenericNominalTypeTypes.Index(domainType)];
            var definition = _genericNominalTypeDefinitions.FirstOrDefault(
                candidate => candidate.Identity == instance.Definition);
            if (definition?.TransparentConstantCarrierType
                is PrimitiveType importedCarrier)
            {
                carrierType = importedCarrier;
                return true;
            }
        }

        carrierType = PrimitiveType.Void;
        return false;
    }

    private bool TryBindGenericMethodConstantArgument(
        GenericMethodConstantFormal formal,
        BoundGenericMethodTypeTerm domainTerm, PrimitiveType domainType,
        PrimitiveType carrierType, SyntaxNode syntax,
        out BoundGenericMethodConstantArgument argument)
    {
        argument = null!;
        if (TryBindGenericMethodConstantFormalForward(
            formal, domainTerm, domainType, carrierType,
            syntax, out argument))
        {
            return true;
        }

        if (domainType == PrimitiveType.Void
            || carrierType == PrimitiveType.Void
            || !TryGetGenericConstantArgumentExpression(syntax,
                out var expressionSyntax))
        {
            Report("TRM3005",
                $"A constant actual for '{formal.Name}' requires a value in its exact domain.",
                syntax.Span);
            return false;
        }

        var diagnosticCount = DiagnosticCount;
        var expression = BindExpression(expressionSyntax, domainType);
        if (DiagnosticCount != diagnosticCount
            || expression.Type != domainType)
        {
            Report("TRM3005",
                $"A constant actual for '{formal.Name}' must have the exact domain '{TypeName(domainType)}'.",
                syntax.Span);
            return false;
        }

        var constant = FoldScalarConstant(expression)
            as BoundConstant;
        if (constant is null && CheckConstExpression(expression, out _))
        {
            var functions = GetConstEvalFunctionBodies();
            try
            {
                constant = EvaluateConstExpression(expression,
                    ConstSlots(_constScalarValues, _variables.Count),
                    functions, 0, new ConstEvalState(_cancellationToken));
            }
            catch (ConstEvalFailure failure)
            {
                Report("TRM3005",
                    $"A constant actual for '{formal.Name}' failed required evaluation: {failure.Message}",
                    syntax.Span);
                return false;
            }
        }

        if (constant is null
            || constant.Type != domainType
            || constant.RepresentationType != carrierType
            || !constant.Bits.IsCanonicalFor(carrierType))
        {
            Report("TRM3005",
                $"A constant actual for '{formal.Name}' must be a complete, valid value in its exact domain.",
                syntax.Span);
            return false;
        }

        argument = new BoundGenericMethodConstantArgument(formal.Ordinal,
            formal.ConstantOrdinal,
            BoundGenericMethodConstantArgumentKind.Value, domainTerm, -1,
            domainType, carrierType, constant.Bits);
        return true;
    }

    private bool TryBindGenericMethodConstantFormalForward(
        GenericMethodConstantFormal formal,
        BoundGenericMethodTypeTerm domainTerm, PrimitiveType domainType,
        PrimitiveType carrierType, SyntaxNode syntax,
        out BoundGenericMethodConstantArgument argument)
    {
        argument = null!;
        if (_genericContext is not GenericInstantiation caller
            || !TryGetGenericConstantArgumentName(syntax, out var name))
        {
            return false;
        }

        var callerFormal = caller.Template.ConstantFormals
            .FirstOrDefault(candidate => candidate.Name == name);
        if (callerFormal is null)
        {
            var ownerDefinitionIndex
                = caller.Template.OwnerGenericTypeDefinitionIndex;
            if ((uint)ownerDefinitionIndex
                    >= (uint)_sourceGenericTypeDefinitions.Count)
            {
                return false;
            }

            var ownerFormal = _sourceGenericTypeDefinitions[
                ownerDefinitionIndex].ConstantFormals.FirstOrDefault(
                    candidate => candidate.Name == name);
            if (ownerFormal is null
                || !TryGetGenericMethodOwnerConstantFormalVariableIndex(
                    caller.Template, ownerDefinitionIndex,
                    ownerFormal.ConstantOrdinal, out var ownerVariableIndex,
                    out var selectedOwnerFormal)
                || selectedOwnerFormal.Name != name
                || !AreGenericMethodTypeTermsEquivalent(
                    caller.Template.OwnerConstantArguments[
                        ownerFormal.ConstantOrdinal].Domain, domainTerm)
                || !TryFindVariable(name, out var ownerVariable)
                || ownerVariable != ownerVariableIndex
                || !TryResolveGenericMethodTypeTerm(
                    caller.Template.OwnerConstantArguments[
                        ownerFormal.ConstantOrdinal].Domain,
                    syntax.Span, out var ownerFormalType)
                || ownerFormalType != _variables[ownerVariable].Type
                || !TryGetGenericMethodConstantCarrierType(
                    ownerFormalType, out _))
            {
                return false;
            }

            argument = new BoundGenericMethodConstantArgument(
                formal.Ordinal, formal.ConstantOrdinal,
                BoundGenericMethodConstantArgumentKind.Formal, domainTerm,
                -1, PrimitiveType.Void, PrimitiveType.Void, default) {
                CallerDefinitionOrdinal = ownerDefinitionIndex,
                CallerConstantFormalOrdinal =
                    ownerFormal.ConstantOrdinal,
                CallerFormalOwnerKind =
                    BoundGenericTypeFormalOwnerKind.SourceGenericType,
            };
            return true;
        }
        var callerFormalOwnerKind
            = caller.Template.DefinitionOrdinal >= 0
                ? BoundGenericTypeFormalOwnerKind.GenericMethod
                : caller.Template.SourceGenericTypeDefinitionIndex >= 0
                    ? BoundGenericTypeFormalOwnerKind.SourceGenericType
                    : (BoundGenericTypeFormalOwnerKind?)null;
        if (callerFormalOwnerKind is null)
        {
            return false;
        }
        var callerDefinitionOrdinal
            = callerFormalOwnerKind.Value
                == BoundGenericTypeFormalOwnerKind.GenericMethod
                ? caller.Template.DefinitionOrdinal
                : caller.Template.SourceGenericTypeDefinitionIndex;
        var callerFormalOrdinal
            = callerFormalOwnerKind.Value
                == BoundGenericTypeFormalOwnerKind.GenericMethod
                ? callerFormal.Ordinal : -1;

        var hasVariable = TryFindVariable(name, out var variableSlot);
        if ((uint)callerFormal.ConstantOrdinal
                < (uint)caller.ConstantArguments.Length)
        {
            if (!hasVariable)
            {
                var callerArgument = caller.ConstantArguments[
                    callerFormal.ConstantOrdinal];
                if ((callerArgument.Kind
                        is BoundGenericMethodConstantArgumentKind.Value
                            or BoundGenericMethodConstantArgumentKind.Formal)
                    && AreGenericMethodTypeTermsEquivalent(
                        callerArgument.Domain, domainTerm))
                {
                    if (callerArgument.Kind
                        == BoundGenericMethodConstantArgumentKind.Value
                        && (!GenericNominalTypesEquivalent(
                                callerArgument.DomainType, domainType)
                            || callerArgument.CarrierType != carrierType))
                    {
                        return false;
                    }

                    argument = callerArgument with {
                        Ordinal = formal.Ordinal,
                        ConstantOrdinal = formal.ConstantOrdinal,
                        Domain = domainTerm,
                        CallerDefinitionOrdinal =
                            callerArgument.Kind
                                == BoundGenericMethodConstantArgumentKind.Formal
                                ? callerDefinitionOrdinal
                                : -1,
                        CallerFormalOrdinal =
                            callerArgument.Kind
                                == BoundGenericMethodConstantArgumentKind.Formal
                                ? callerFormalOrdinal : -1,
                        CallerConstantFormalOrdinal =
                            callerArgument.Kind
                                == BoundGenericMethodConstantArgumentKind.Formal
                                ? callerFormal.ConstantOrdinal : -1,
                        CallerFormalOwnerKind =
                            callerArgument.Kind
                                == BoundGenericMethodConstantArgumentKind.Formal
                                ? callerFormalOwnerKind.Value
                                : BoundGenericTypeFormalOwnerKind.GenericMethod,
                    };
                    return true;
                }
            }
        }

        if (_genericProofFunctions is not null
            && callerFormal.ProofParameterIndex >= 0
            && hasVariable
            && variableSlot == callerFormal.ProofParameterIndex
            && AreGenericMethodTypeTermsEquivalent(
                callerFormal.Domain, domainTerm))
        {
            argument = new BoundGenericMethodConstantArgument(
                formal.Ordinal, formal.ConstantOrdinal,
                BoundGenericMethodConstantArgumentKind.Formal, domainTerm,
                callerFormalOrdinal, PrimitiveType.Void,
                PrimitiveType.Void, default) {
                CallerDefinitionOrdinal = callerDefinitionOrdinal,
                CallerConstantFormalOrdinal =
                    callerFormal.ConstantOrdinal,
                CallerFormalOwnerKind = callerFormalOwnerKind.Value,
            };
            return true;
        }

        return false;
    }

    private static bool TryGetGenericConstantArgumentExpression(
        SyntaxNode argument, out SyntaxNode expression)
    {
        if (argument.Kind == SyntaxKind.ConstantGenericArgument
            && argument.Children.OfType<SyntaxNode>().SingleOrDefault()
                is SyntaxNode constantExpression)
        {
            expression = constantExpression;
            return true;
        }
        if (argument.Kind != SyntaxKind.TypeName
            || argument.Children.OfType<SyntaxNode>().Any())
        {
            expression = null!;
            return false;
        }

        var tokens = argument.Children.OfType<SyntaxTokenElement>()
            .ToArray();
        if (tokens.Length == 1
            && tokens[0].Token.Kind == TokenKind.Identifier)
        {
            expression = new SyntaxNode(SyntaxKind.NameExpression,
                [tokens[0]], argument.Span.Start);
            return true;
        }
        if (tokens.Length < 3 || tokens.Length % 2 == 0
            || tokens[0].Token.Kind != TokenKind.Identifier)
        {
            expression = null!;
            return false;
        }

        var current = new SyntaxNode(SyntaxKind.NameExpression,
            [tokens[0]], argument.Span.Start);
        for (var index = 1; index < tokens.Length; index += 2)
        {
            if (tokens[index].Token.Text != "."
                || tokens[index + 1].Token.Kind
                    != TokenKind.Identifier)
            {
                expression = null!;
                return false;
            }
            current = new SyntaxNode(SyntaxKind.MemberAccessExpression,
                [current, tokens[index], tokens[index + 1]],
                argument.Span.Start);
        }

        expression = current;
        return true;
    }

    private static bool TryGetGenericConstantArgumentName(
        SyntaxNode argument, out string name)
    {
        var expression = argument;
        if (expression.Kind == SyntaxKind.ConstantGenericArgument
            && expression.Children.OfType<SyntaxNode>().SingleOrDefault()
                is SyntaxNode constantExpression)
        {
            expression = constantExpression;
        }
        while (expression.Kind == SyntaxKind.ParenthesizedExpression
            && expression.Children.OfType<SyntaxNode>().SingleOrDefault()
                is SyntaxNode inner)
        {
            expression = inner;
        }

        if (expression.Kind == SyntaxKind.NameExpression
            && expression.Children.FirstOrDefault()
                is SyntaxTokenElement token
            && token.Token.Kind == TokenKind.Identifier)
        {
            name = token.Token.Name;
            return true;
        }

        if (expression.Kind == SyntaxKind.TypeName
            && expression.Children.OfType<SyntaxNode>().Any() == false
            && expression.Children.OfType<SyntaxTokenElement>().ToArray()
                is [{ Token.Kind: TokenKind.Identifier } identifier])
        {
            name = identifier.Token.Name;
            return true;
        }

        name = string.Empty;
        return false;
    }

    private bool TryEvaluateGenericMethodConstantDefault(
        GenericTemplate template, GenericMethodConstantFormal formal,
        BoundGenericMethodTypeTerm domainTerm, PrimitiveType domainType,
        PrimitiveType carrierType,
        ImmutableArray<BoundGenericMethodConstantArgument>.Builder prior,
        TextSpan useSite, out BoundGenericMethodConstantArgument argument)
    {
        argument = null!;
        if ((uint)template.DefinitionOrdinal
                >= (uint)_genericMethodDefinitions.Count
            || formal.DefaultSyntax is null)
        {
            return false;
        }
        var definition = _genericMethodDefinitions[
            template.DefinitionOrdinal];
        if ((uint)formal.ConstantOrdinal
                >= (uint)definition.ConstantFormals.Length
            || definition.ConstantFormals[formal.ConstantOrdinal].Default
                is not BoundGenericMethodConstantDefault defaultInfo)
        {
            return false;
        }
        var proofProgram = definition.ProofProgram;
        if ((uint)defaultInfo.ProofFunctionIndex
                >= (uint)proofProgram.Functions.Length
            || proofProgram.Functions[defaultInfo.ProofFunctionIndex]
                is not BoundFunction defaultFunction
            || !TryRebindGenericMethodConstantDefaultCalls(
                defaultFunction, proofProgram, out var expression))
        {
            return false;
        }

        var substitutions = new Dictionary<int, BoundExpression>();
        for (var dependencyIndex = 0;
            dependencyIndex < defaultInfo.FormalDependencies.Length;
            dependencyIndex++)
        {
            var dependencyOrdinal = defaultInfo.FormalDependencies[
                dependencyIndex];
            var dependencyFormal = template.ConstantFormals
                .FirstOrDefault(candidate =>
                    candidate.Ordinal == dependencyOrdinal);
            if (dependencyFormal is null
                || (uint)dependencyFormal.ConstantOrdinal
                    >= (uint)prior.Count)
            {
                return false;
            }

            var dependency = prior[dependencyFormal.ConstantOrdinal];
            if (expression is BoundRead read
                && read.Variable == dependencyIndex
                && dependency.Kind
                    is BoundGenericMethodConstantArgumentKind.Value
                        or BoundGenericMethodConstantArgumentKind.Formal)
            {
                if (dependency.Kind
                    == BoundGenericMethodConstantArgumentKind.Formal)
                {
                    argument = dependency with {
                        Ordinal = formal.Ordinal,
                        ConstantOrdinal = formal.ConstantOrdinal,
                        Domain = domainTerm,
                    };
                    return true;
                }
                if (!GenericNominalTypesEquivalent(
                        dependency.DomainType, domainType)
                    || dependency.CarrierType != carrierType)
                {
                    return false;
                }
            }

            if (!TryCreateBoundGenericMethodConstantExpression(
                dependency, expression.Span, out var valueExpression))
            {
                return false;
            }
            substitutions.Add(dependencyIndex, valueExpression);
        }

        var substituted = SubstituteGenericMethodConstantDefault(
            expression, substitutions);
        var constant = FoldScalarConstant(substituted)
            as BoundConstant;
        if (constant is null
            && CheckConstExpression(substituted, out _))
        {
            var functions = GetConstEvalFunctionBodies();
            try
            {
                constant = EvaluateConstExpression(substituted,
                    ConstSlots(_constScalarValues, _variables.Count),
                    functions, 0,
                    new ConstEvalState(_cancellationToken));
            }
            catch (ConstEvalFailure)
            {
                return false;
            }
        }
        if (constant is null || constant.Type != domainType
            || constant.RepresentationType != carrierType
            || !constant.Bits.IsCanonicalFor(carrierType))
        {
            return false;
        }

        argument = new BoundGenericMethodConstantArgument(formal.Ordinal,
            formal.ConstantOrdinal,
            BoundGenericMethodConstantArgumentKind.Value, domainTerm, -1,
            domainType, carrierType, constant.Bits);
        return true;
    }

    private bool TryRebindGenericMethodConstantDefaultCalls(
        BoundFunction defaultFunction, BoundProgram proofProgram,
        out BoundExpression expression)
    {
        expression = null!;
        var canRebind = true;
        var rewrittenFunction = BoundTreeTraversal.RewriteExpressions(
            defaultFunction, item =>
            {
                if (item is not BoundCall call)
                {
                    return item;
                }

                if ((uint)call.Function >= (uint)proofProgram.Functions.Length
                    || proofProgram.Functions[call.Function]
                        is not BoundFunction proofTarget
                    || !_functions.TryGetValue(proofTarget.Name,
                        out var sourceFunctionIndex)
                    || (uint)sourceFunctionIndex >= (uint)_signatures.Count
                    || _signatures[sourceFunctionIndex].Name
                        != proofTarget.Name
                    || !_signatures[sourceFunctionIndex].IsConstEval)
                {
                    canRebind = false;
                    return item;
                }

                return call with {
                    Function = sourceFunctionIndex,
                    Type = _signatures[sourceFunctionIndex].ReturnType,
                };
            });
        if (!canRebind
            || rewrittenFunction.Body.Statements
                is not [BoundReturn { Value: BoundExpression value }])
        {
            return false;
        }

        expression = value;
        return true;
    }

    private static BoundExpression SubstituteGenericMethodConstantDefault(
        BoundExpression expression,
        IReadOnlyDictionary<int, BoundExpression> substitutions)
    {
        if (expression is BoundRead read
            && substitutions.TryGetValue(read.Variable, out var value))
        {
            return value with { Span = expression.Span };
        }

        return expression.RewriteNestedExpressions(child =>
            SubstituteGenericMethodConstantDefault(child,
                substitutions));
    }

    private string GenericMethodInstantiationName(
        GenericTemplate template,
        string[] typeNames,
        ImmutableArray<BoundGenericMethodConstantArgument> constants)
    {
        var targetName = template.Name;
        if (template.ImportedMethod is BoundGenericMethodImport import)
        {
            // Separate retained views can have identical local rows and spelling.
            if (!_genericMethodImportOrdinals.TryGetValue(import,
                    out var importOrdinal))
            {
                importOrdinal = _genericMethodImportOrdinals.Count;
                _genericMethodImportOrdinals.Add(import, importOrdinal);
            }

            targetName = "<G1Import:"
                + importOrdinal.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + ":" + targetName + ">";
        }

        var name = targetName + "<" + string.Join(", ", typeNames) + ">";
        if (constants.IsEmpty)
        {
            return name;
        }

        var identities = constants.Select(argument =>
            argument.Kind
                == BoundGenericMethodConstantArgumentKind.Value
                ? string.Concat(
                    argument.ConstantOrdinal.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ":V:", GenericMethodTypeTermIdentity(
                        argument.Domain),
                    ":", ((int)argument.DomainType).ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ":", ((int)argument.CarrierType).ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ":", argument.Bits.High.ToString("X16",
                        System.Globalization.CultureInfo.InvariantCulture),
                    argument.Bits.Low.ToString("X16",
                        System.Globalization.CultureInfo.InvariantCulture))
                : string.Concat(
                    argument.ConstantOrdinal.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ":F:", argument.CallerDefinitionOrdinal.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ":", ((byte)argument.CallerFormalOwnerKind).ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ":", argument.CallerFormalOrdinal.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ":", argument.CallerConstantFormalOrdinal.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ":", GenericMethodTypeTermIdentity(argument.Domain)))
            .ToArray();
        return name + "$constant{" + string.Join(";", identities) + "}";
    }

    private static string GenericMethodTypeTermIdentity(
        BoundGenericMethodTypeTerm term)
    {
        var nominalIdentity = term.NominalTypeIdentity
            is BoundGenericNominalTypeIdentity identity
                ? string.Concat(
                    identity.ProviderIdentity.Length.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ":", identity.ProviderIdentity,
                    identity.ModuleIdentity.Length.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ":", identity.ModuleIdentity,
                    ":", identity.DefinitionId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture))
                : "-";
        return string.Concat(
            ((byte)term.Kind).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ":", ((int)term.ConcreteType).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ":", term.FormalOrdinal.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ":", term.SourceGenericTypeDefinitionIndex.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ":", term.IsWritable ? "1" : "0",
            ":", term.IsGarbageCollected ? "1" : "0",
            ":", term.IsInitialization ? "1" : "0",
            ":", term.IsOptional ? "1" : "0",
            ":", term.ElementCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ":", ((byte)term.CallingConvention).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ":", term.ParameterCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ":", term.AllowsVoid ? "1" : "0",
            ":", nominalIdentity,
            ":", term.TypeArguments.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ":", string.Join("|", term.TypeArguments.Select(
                GenericMethodTypeTermIdentity)),
            ":", string.Join("|", term.ConstantArguments.Select(
                static argument => argument.Kind
                    == BoundGenericMethodTypeConstantArgumentKind.Formal
                    ? string.Concat(
                    "F:", ((byte)argument.FormalOwnerKind).ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ":", argument.FormalDefinitionOrdinal.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ":", argument.FormalOrdinal.ToString(
                            System.Globalization.CultureInfo.InvariantCulture))
                        : string.Concat(
                            "V:", ((int)argument.DomainType).ToString(
                                System.Globalization.CultureInfo.InvariantCulture),
                            ":", ((int)argument.CarrierType).ToString(
                                System.Globalization.CultureInfo.InvariantCulture),
                            ":", argument.Bits.High.ToString("X16",
                                System.Globalization.CultureInfo.InvariantCulture),
                            argument.Bits.Low.ToString("X16",
                                System.Globalization.CultureInfo.InvariantCulture)))));
    }

    private bool TryInferGenericMethodConstantArgumentsFromTypedEvidence(
        GenericTemplate template, SyntaxNode arguments,
        ImmutableArray<PrimitiveType> typeArguments,
        PrimitiveType? expected, GenericMethodCallArguments supplied)
    {
        var evidence = new List<BoundGenericMethodConstantArgument>?[
            template.ConstantFormals.Length];
        var typeArgumentTerms = typeArguments
            .Select(static type => (PrimitiveType?)type).ToArray();
        var actualArguments = arguments.Children
            .OfType<SyntaxNode>().ToArray();
        for (var ordinal = 0;
            ordinal < template.Parameters.Length
                && ordinal < actualArguments.Length; ordinal++)
        {
            if (template.Parameters[ordinal].TypeTerm
                is not BoundGenericMethodTypeTerm parameterTerm
                || !ContainsGenericMethodConstantArguments(parameterTerm))
            {
                continue;
            }

            var actualType = GetGenericMethodArgumentType(
                template.Parameters[ordinal], actualArguments[ordinal]);
            if (actualType is not PrimitiveType type)
            {
                continue;
            }

            var candidateEvidence = new List<
                BoundGenericMethodConstantArgument>?[
                    template.ConstantFormals.Length];
            if (!TryMatchGenericMethodTypeTermForConstants(template,
                    parameterTerm, type, typeArgumentTerms,
                    candidateEvidence))
            {
                return false;
            }
            MergeConstantEvidence(evidence, candidateEvidence);
        }

        if (expected is PrimitiveType expectedType
            && template.ReturnTypeTerm
                is BoundGenericMethodTypeTerm returnTerm
            && ContainsGenericMethodConstantArguments(returnTerm))
        {
            var candidateEvidence = new List<
                BoundGenericMethodConstantArgument>?[
                    template.ConstantFormals.Length];
            if (!TryMatchGenericMethodTypeTermForConstants(template,
                    returnTerm, expectedType, typeArgumentTerms,
                    candidateEvidence))
            {
                return false;
            }
            MergeConstantEvidence(evidence, candidateEvidence);
        }

        for (var ordinal = 0; ordinal < evidence.Length; ordinal++)
        {
            if (evidence[ordinal] is not { Count: > 0 } values)
            {
                continue;
            }

            var selected = values[0];
            if (values.Skip(1).Any(value =>
                !GenericMethodConstantArgumentsEqual(selected, value)))
            {
                return false;
            }

            var existing = supplied.InferredConstantArguments[ordinal];
            if (existing is not null
                && !GenericMethodConstantArgumentsEqual(selected,
                    existing with {
                        Domain = selected.Domain,
                        DomainType = selected.DomainType,
                        CarrierType = selected.CarrierType,
                    }))
            {
                return false;
            }
            supplied.InferredConstantArguments[ordinal] = selected;
        }

        return true;
    }

    private PrimitiveType? GetGenericMethodArgumentType(
        GenericParameter parameter, SyntaxNode argument)
    {
        if (argument.Children is [SyntaxNode value])
        {
            return KnownType(value);
        }
        if (parameter is { IsOut: true, IsInit: false }
            && argument.Children is [
                SyntaxTokenElement {
                    Token.Kind: TokenKind.OutKeyword,
                },
                SyntaxNode {
                    Kind: SyntaxKind.NameExpression,
                } output,
            ])
        {
            return KnownType(output);
        }
        if (parameter.IsInit
            && argument.Children is [
                SyntaxTokenElement {
                    Token.Text: "init",
                },
                SyntaxNode {
                    Kind: SyntaxKind.NameExpression,
                } initOutput,
            ])
        {
            return KnownType(initOutput);
        }

        return null;
    }

    private static bool ContainsGenericMethodConstantArguments(
        BoundGenericMethodTypeTerm term) =>
        !term.ConstantArguments.IsDefaultOrEmpty
        || term.TypeArguments.Any(
            ContainsGenericMethodConstantArguments);

    private bool TryInferGenericMethodConstantArgumentsFromPinnedDefaults(
        GenericTemplate template, SyntaxNode arguments,
        PrimitiveType? expected, GenericMethodCallArguments supplied)
    {
        var evidence = new List<BoundGenericMethodConstantArgument>?[
            template.ConstantFormals.Length];
        var typeEvidence = new PrimitiveType?[
            template.TypeParameters.Length];
        var actualArguments = arguments.Children
            .OfType<SyntaxNode>().ToArray();
        for (var typeOrdinal = 0;
            typeOrdinal < template.TypeParameters.Length; typeOrdinal++)
        {
            if ((uint)typeOrdinal >= (uint)supplied.ExplicitTypeDefaults.Length
                || !supplied.ExplicitTypeDefaults[typeOrdinal]
                || GenericMethodTypeDefault(template, typeOrdinal)
                    is not BoundGenericMethodTypeDefault typeDefault)
            {
                continue;
            }

            for (var parameterOrdinal = 0;
                parameterOrdinal < template.Parameters.Length
                    && parameterOrdinal < actualArguments.Length;
                parameterOrdinal++)
            {
                var parameter = template.Parameters[parameterOrdinal];
                if (parameter.TypeParameterIndex != typeOrdinal)
                {
                    continue;
                }

                var argument = actualArguments[parameterOrdinal];
                PrimitiveType? actualType = null;
                if (argument.Children is [SyntaxNode value])
                {
                    actualType = InferGenericParameterType(parameter,
                        KnownType(value));
                }
                else if (parameter is { IsOut: true, IsInit: false }
                    && argument.Children is [
                        SyntaxTokenElement {
                            Token.Kind: TokenKind.OutKeyword,
                        },
                        SyntaxNode {
                            Kind: SyntaxKind.NameExpression,
                        } outOutput,
                    ])
                {
                    actualType = KnownType(outOutput);
                }
                else if (parameter.IsInit
                    && argument.Children is [
                        SyntaxTokenElement {
                            Token.Text: "init",
                        },
                        SyntaxNode {
                            Kind: SyntaxKind.NameExpression,
                        } initOutput,
                    ])
                {
                    actualType = KnownType(initOutput);
                }

                if (actualType is not PrimitiveType type)
                {
                    continue;
                }

                var candidateEvidence = new List<
                    BoundGenericMethodConstantArgument>?[
                        template.ConstantFormals.Length];
                if (TryMatchGenericMethodTypeTermForConstants(template,
                        typeDefault.Term, type, supplied.TypeArguments,
                        candidateEvidence))
                {
                    MergeConstantEvidence(evidence, candidateEvidence);
                    if (!AddTypeDefaultEvidence(typeEvidence,
                        typeOrdinal, type))
                    {
                        return false;
                    }
                }
            }

            if (expected is PrimitiveType expectedType
                && template.ReturnTypeParameterIndex == typeOrdinal)
            {
                var resultType = template.ReferenceReturnTypePattern
                    is CheckedReferenceTypeDefinition referencePattern
                        ? InferGenericReferenceType(referencePattern,
                            expectedType)
                        : expectedType;
                if (resultType is PrimitiveType type)
                {
                    var candidateEvidence = new List<
                        BoundGenericMethodConstantArgument>?[
                            template.ConstantFormals.Length];
                    if (TryMatchGenericMethodTypeTermForConstants(template,
                            typeDefault.Term, type, supplied.TypeArguments,
                            candidateEvidence))
                    {
                        MergeConstantEvidence(evidence,
                            candidateEvidence);
                        if (!AddTypeDefaultEvidence(typeEvidence,
                            typeOrdinal, type))
                        {
                            return false;
                        }
                    }
                }
            }
        }

        for (var ordinal = 0; ordinal < typeEvidence.Length; ordinal++)
        {
            if (typeEvidence[ordinal] is PrimitiveType type)
            {
                supplied.InferredTypeDefaultArguments[ordinal] = type;
            }
        }

        for (var ordinal = 0; ordinal < evidence.Length; ordinal++)
        {
            if (evidence[ordinal] is not { Count: > 0 } values)
            {
                continue;
            }

            var selected = values[0];
            if (values.Skip(1).Any(value =>
                !GenericMethodConstantArgumentsEqual(selected, value)))
            {
                return false;
            }
            supplied.InferredConstantArguments[ordinal] = selected;
        }

        return true;
    }

    private bool AddTypeDefaultEvidence(PrimitiveType?[] evidence,
        int ordinal, PrimitiveType candidate)
    {
        if (evidence[ordinal] is PrimitiveType established)
        {
            return GenericNominalTypesEquivalent(established, candidate);
        }

        evidence[ordinal] = candidate;
        return true;
    }

    private bool GenericNominalTypesEquivalent(
        PrimitiveType left, PrimitiveType right)
    {
        if (left == right)
        {
            return true;
        }
        if (!BoundGenericNominalTypeTypes.IsType(left)
            || !BoundGenericNominalTypeTypes.IsType(right))
        {
            return false;
        }

        var leftIndex = BoundGenericNominalTypeTypes.Index(left);
        var rightIndex = BoundGenericNominalTypeTypes.Index(right);
        if ((uint)leftIndex >= (uint)_genericNominalTypeInstances.Count
            || (uint)rightIndex >= (uint)_genericNominalTypeInstances.Count)
        {
            return false;
        }

        var leftInstance = _genericNominalTypeInstances[leftIndex];
        var rightInstance = _genericNominalTypeInstances[rightIndex];
        if (leftInstance.Definition != rightInstance.Definition
            || leftInstance.Qualifiers != rightInstance.Qualifiers
            || leftInstance.Arguments.Length
                != rightInstance.Arguments.Length)
        {
            return false;
        }

        for (var ordinal = 0;
            ordinal < leftInstance.Arguments.Length; ordinal++)
        {
            var leftArgument = leftInstance.Arguments[ordinal];
            var rightArgument = rightInstance.Arguments[ordinal];
            if (leftArgument.Kind != rightArgument.Kind)
            {
                return false;
            }
            if (leftArgument.Kind == BoundGenericArgumentKind.Type)
            {
                if (leftArgument.Qualifiers != rightArgument.Qualifiers
                    || leftArgument.HasTypeFormalOwnerIdentity
                        != rightArgument.HasTypeFormalOwnerIdentity
                    || (leftArgument.HasTypeFormalOwnerIdentity
                        && (leftArgument.TypeFormalOwnerKind
                                != rightArgument.TypeFormalOwnerKind
                            || leftArgument.TypeFormalDefinitionOrdinal
                                != rightArgument.TypeFormalDefinitionOrdinal
                            || leftArgument.TypeFormalOrdinal
                                != rightArgument.TypeFormalOrdinal))
                    || (leftArgument.TypeArgumentTerm is null)
                        != (rightArgument.TypeArgumentTerm is null)
                    || (leftArgument.TypeArgumentTerm is
                            BoundGenericMethodTypeTerm leftTypeArgumentTerm
                        && rightArgument.TypeArgumentTerm is
                            BoundGenericMethodTypeTerm rightTypeArgumentTerm
                        && !AreGenericMethodTypeTermsEquivalent(
                            leftTypeArgumentTerm, rightTypeArgumentTerm))
                    || leftArgument.NominalTypeIdentity
                        != rightArgument.NominalTypeIdentity
                    || (leftArgument.NominalTypeIdentity is null
                        && !GenericNominalTypesEquivalent(
                            leftArgument.Type, rightArgument.Type)))
                {
                    return false;
                }
                continue;
            }

            if (leftArgument.HasConstantFormalIdentity
                || rightArgument.HasConstantFormalIdentity)
            {
                if (!leftArgument.HasConstantFormalIdentity
                    || !rightArgument.HasConstantFormalIdentity
                    || leftArgument.ConstantFormalOwnerKind
                        != rightArgument.ConstantFormalOwnerKind
                    || leftArgument.ConstantFormalDefinitionOrdinal
                        != rightArgument.ConstantFormalDefinitionOrdinal
                    || leftArgument.ConstantFormalOrdinal
                        != rightArgument.ConstantFormalOrdinal
                    || !AreGenericMethodTypeTermsEquivalent(
                        leftArgument.ConstantFormalDomain!,
                        rightArgument.ConstantFormalDomain!))
                {
                    return false;
                }
                continue;
            }

            if (leftArgument.HasDecodedConstant
                && rightArgument.HasDecodedConstant)
            {
                if (leftArgument.ConstantDomainType
                        is not PrimitiveType leftDomainType
                    || rightArgument.ConstantDomainType
                        is not PrimitiveType rightDomainType
                    || !GenericNominalTypesEquivalent(leftDomainType,
                        rightDomainType)
                    || leftArgument.ConstantCarrierType
                        != rightArgument.ConstantCarrierType
                    || leftArgument.ConstantLow
                        != rightArgument.ConstantLow
                    || leftArgument.ConstantHigh
                        != rightArgument.ConstantHigh)
                {
                    return false;
                }
            }
            else if (leftArgument.ConstantProviderIdentity
                    != rightArgument.ConstantProviderIdentity
                || leftArgument.ConstantSourceRow
                    != rightArgument.ConstantSourceRow
                || leftArgument.ConstantLow != rightArgument.ConstantLow
                || leftArgument.ConstantHigh != rightArgument.ConstantHigh)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryMatchGenericMethodTypeTermForConstants(
        GenericTemplate template, BoundGenericMethodTypeTerm term,
        PrimitiveType actualType,
        PrimitiveType?[] typeArguments,
        List<BoundGenericMethodConstantArgument>?[] evidence)
    {
        if (term.Kind == BoundGenericMethodTypeTermKind.Concrete)
        {
            return term.ConcreteType == actualType;
        }
        if (term.Kind == BoundGenericMethodTypeTermKind.Formal)
        {
            return (uint)term.FormalOrdinal < (uint)typeArguments.Length
                && typeArguments[term.FormalOrdinal] is PrimitiveType formalType
                && formalType == actualType;
        }
        if (term.Kind == BoundGenericMethodTypeTermKind.SourceGeneric)
        {
            if ((int)actualType < 100)
            {
                return false;
            }
            var instance = _sourceGenericTypeInstances.FirstOrDefault(
                candidate => candidate.StructureIndex
                    == (int)actualType - 100);
            if (instance is null
                || instance.DefinitionIndex
                    != term.SourceGenericTypeDefinitionIndex
                || instance.TypeArguments.Length != term.TypeArguments.Length)
            {
                return false;
            }
            for (var ordinal = 0;
                ordinal < term.TypeArguments.Length; ordinal++)
            {
                if (!TryMatchGenericMethodTypeTermForConstants(template,
                    term.TypeArguments[ordinal],
                    instance.TypeArguments[ordinal], typeArguments,
                    evidence))
                {
                    return false;
                }
            }
            return true;
        }
        if (term.Kind
                != BoundGenericMethodTypeTermKind.ImportedGeneric
            || term.NominalTypeIdentity
                is not BoundGenericNominalTypeIdentity identity
            || !BoundGenericNominalTypeTypes.IsType(actualType))
        {
            return false;
        }

        var instanceIndex = BoundGenericNominalTypeTypes.Index(actualType);
        if ((uint)instanceIndex >= (uint)_genericNominalTypeInstances.Count)
        {
            return false;
        }
        var instanceType = _genericNominalTypeInstances[instanceIndex];
        var definitions = _genericNominalTypeDefinitions
            .Where(candidate => candidate.Identity == identity).ToArray();
        if (definitions.Length != 1)
        {
            return false;
        }
        var definition = definitions[0];
        if (instanceType.Definition != identity
            || instanceType.Arguments.Length != definition.Formals.Length
            || term.TypeArguments.Length
                != definition.Formals.Count(static formal =>
                    formal.Kind == BoundGenericArgumentKind.Type)
            || term.ConstantArguments.Length
                != definition.Formals.Count(static formal =>
                    formal.Kind == BoundGenericArgumentKind.Constant))
        {
            return false;
        }

        var typeOrdinal = 0;
        var constantOrdinal = 0;
        for (var formalOrdinal = 0;
            formalOrdinal < definition.Formals.Length; formalOrdinal++)
        {
            var formal = definition.Formals[formalOrdinal];
            var actual = instanceType.Arguments[formalOrdinal];
            if (formal.Kind == BoundGenericArgumentKind.Type)
            {
                if (actual.Kind != BoundGenericArgumentKind.Type
                    || !TryMatchGenericMethodTypeTermForConstants(template,
                        term.TypeArguments[typeOrdinal++], actual.Type,
                        typeArguments, evidence))
                {
                    return false;
                }
                continue;
            }

            if (formal.Kind != BoundGenericArgumentKind.Constant
                || actual.Kind != BoundGenericArgumentKind.Constant)
            {
                return false;
            }

            var constantPattern =
                term.ConstantArguments[constantOrdinal++];
            var domainTypeArguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeTerm>(
                    definition.Formals.Count(static candidate =>
                        candidate.Kind == BoundGenericArgumentKind.Type));
            for (var typeFormalOrdinal = 0;
                typeFormalOrdinal < definition.Formals.Length;
                typeFormalOrdinal++)
            {
                if (definition.Formals[typeFormalOrdinal].Kind
                    != BoundGenericArgumentKind.Type)
                {
                    continue;
                }

                var typeArgument = instanceType.Arguments[
                    typeFormalOrdinal];
                if (typeArgument.Kind != BoundGenericArgumentKind.Type
                    || !TryGetGenericMethodTypeArgumentTerm(
                        typeArgument.Type, out var typeArgumentTerm))
                {
                    return false;
                }

                var qualifiedDomainTypeArgument
                    = ApplyGenericQueryTypeQualifiers(typeArgumentTerm,
                        typeArgument.Qualifiers);
                if (!TryNormalizeGenericMethodFormalWriteQualifier(
                        qualifiedDomainTypeArgument, template,
                        out var normalizedDomainTypeArgument))
                {
                    return false;
                }
                domainTypeArguments.Add(normalizedDomainTypeArgument);
            }

            var checkedDomainTypeArguments
                = domainTypeArguments.MoveToImmutable();
            BoundGenericMethodTypeTerm domainTerm;
            if (formal.ConstantDomainTypeTerm is
                BoundGenericMethodTypeTerm constantDomainTerm)
            {
                if (!BoundGenericMethodTypeTerms.TrySubstitute(
                    constantDomainTerm, checkedDomainTypeArguments, [],
                    out domainTerm))
                {
                    return false;
                }
            }
            else if (formal.ConstantDomainType
                is PrimitiveType constantDomainType)
            {
                domainTerm = new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Concrete,
                    constantDomainType, -1, -1, [],
                    false, false, false, false);
            }
            else
            {
                return false;
            }

            var hasValue = actual.HasDecodedConstant;
            if (hasValue)
            {
                if (actual.ConstantDomainType is not PrimitiveType
                        actualDomainType
                    || actual.ConstantCarrierType is not PrimitiveType
                        carrierType
                    || !TryGetGenericMethodTypeArgumentTerm(
                        actualDomainType, out var actualDomainTerm)
                    || !AreGenericMethodTypeTermsEquivalent(actualDomainTerm,
                        domainTerm)
                    || !TryGetGenericMethodConstantCarrierType(
                        actualDomainType, out var expectedCarrier)
                    || carrierType != expectedCarrier
                    || !new ScalarBits(actual.ConstantLow,
                        actual.ConstantHigh).IsCanonicalFor(carrierType))
                {
                    return false;
                }
            }
            else if (!actual.HasConstantFormalIdentity
                || !AreGenericMethodTypeTermsEquivalent(
                    actual.ConstantFormalDomain!, domainTerm))
            {
                return false;
            }

            if (constantPattern.Kind
                == BoundGenericMethodTypeConstantArgumentKind.Value)
            {
                if (!hasValue
                    || constantPattern.DomainType
                        != actual.ConstantDomainType
                    || constantPattern.CarrierType
                        != actual.ConstantCarrierType
                    || constantPattern.Bits.Low != actual.ConstantLow
                    || constantPattern.Bits.High != actual.ConstantHigh)
                {
                    return false;
                }
                continue;
            }

            if (constantPattern.Kind
                    != BoundGenericMethodTypeConstantArgumentKind.Formal
                || constantPattern.FormalDefinitionOrdinal
                    != template.DefinitionOrdinal
                || (uint)constantPattern.FormalOrdinal
                    >= (uint)template.ConstantFormals.Length
                || !TryGetGenericMethodConstantFormalDomain(
                    template, constantPattern.FormalOrdinal,
                    typeArguments, out var methodFormalDomain)
                || !AreGenericMethodTypeTermsEquivalent(
                    methodFormalDomain, domainTerm))
            {
                return false;
            }

            var methodFormal = template.ConstantFormals[
                constantPattern.FormalOrdinal];
            BoundGenericMethodConstantArgument inferred;
            if (hasValue)
            {
                inferred = new BoundGenericMethodConstantArgument(
                    methodFormal.Ordinal, methodFormal.ConstantOrdinal,
                    BoundGenericMethodConstantArgumentKind.Value,
                    domainTerm, -1,
                    actual.ConstantDomainType!.Value,
                    actual.ConstantCarrierType!.Value,
                    new ScalarBits(actual.ConstantLow,
                        actual.ConstantHigh));
            }
            else
            {
                int callerFormalOrdinal;
                BoundGenericTypeFormalOwnerKind callerFormalOwnerKind;
                if (actual.ConstantFormalOwnerKind
                    == BoundGenericTypeFormalOwnerKind.GenericMethod)
                {
                    var callerFormal = _genericMethodSignatures
                        .FirstOrDefault(signature =>
                            signature.DefinitionOrdinal
                                == actual.ConstantFormalDefinitionOrdinal)?
                        .ConstantFormals.FirstOrDefault(candidate =>
                            candidate.ConstantOrdinal
                                == actual.ConstantFormalOrdinal);
                    if (callerFormal is null)
                    {
                        return false;
                    }

                    callerFormalOrdinal = callerFormal.Ordinal;
                    callerFormalOwnerKind =
                        BoundGenericTypeFormalOwnerKind.GenericMethod;
                }
                else if (actual.ConstantFormalOwnerKind
                    == BoundGenericTypeFormalOwnerKind.SourceGenericType)
                {
                    if ((uint)actual.ConstantFormalDefinitionOrdinal
                            >= (uint)_sourceGenericTypeDefinitions.Count)
                    {
                        return false;
                    }

                    var callerFormal = _sourceGenericTypeDefinitions[
                        actual.ConstantFormalDefinitionOrdinal]
                        .ConstantFormals.FirstOrDefault(candidate =>
                            candidate.ConstantOrdinal
                                == actual.ConstantFormalOrdinal);
                    if (callerFormal is null)
                    {
                        return false;
                    }

                    callerFormalOrdinal = -1;
                    callerFormalOwnerKind =
                        BoundGenericTypeFormalOwnerKind.SourceGenericType;
                }
                else
                {
                    return false;
                }

                inferred = new BoundGenericMethodConstantArgument(
                    methodFormal.Ordinal, methodFormal.ConstantOrdinal,
                    BoundGenericMethodConstantArgumentKind.Formal,
                    domainTerm, callerFormalOrdinal,
                    PrimitiveType.Void, PrimitiveType.Void, default) {
                    CallerDefinitionOrdinal =
                        actual.ConstantFormalDefinitionOrdinal,
                    CallerConstantFormalOrdinal =
                        actual.ConstantFormalOrdinal,
                    CallerFormalOwnerKind = callerFormalOwnerKind,
                };
            }
            evidence[methodFormal.ConstantOrdinal] ??= [];
            evidence[methodFormal.ConstantOrdinal]!.Add(inferred);
        }

        return true;
    }

    private bool IsValidGenericMethodConstantFormalForward(
        GenericInstantiation context,
        BoundGenericMethodTypeConstantArgument pattern,
        BoundGenericMethodConstantArgument actual)
    {
        if (pattern.FormalDefinitionOrdinal
                != context.Template.DefinitionOrdinal
            || actual.ConstantOrdinal != pattern.FormalOrdinal
            || actual.CallerDefinitionOrdinal < 0
            || actual.CallerFormalOrdinal < 0
            || actual.CallerConstantFormalOrdinal < 0)
        {
            return false;
        }

        var callerSignature = _genericMethodSignatures.FirstOrDefault(
            signature => signature.DefinitionOrdinal
                == actual.CallerDefinitionOrdinal);
        var callerFormal = callerSignature?.ConstantFormals.FirstOrDefault(
            formal => formal.ConstantOrdinal
                == actual.CallerConstantFormalOrdinal);
        return callerFormal is not null
            && callerFormal.Ordinal == actual.CallerFormalOrdinal
            && AreGenericMethodTypeTermsEquivalent(actual.Domain,
                callerFormal.Domain);
    }

    private bool TryGetGenericMethodConstantFormalDomain(
        GenericTemplate template, int constantFormalOrdinal,
        PrimitiveType?[] typeArguments,
        out BoundGenericMethodTypeTerm domain)
    {
        domain = null!;
        if ((uint)constantFormalOrdinal
                >= (uint)template.ConstantFormals.Length
            || typeArguments.Length != template.TypeParameters.Length)
        {
            return false;
        }

        var typeTerms = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(typeArguments.Length);
        for (var ordinal = 0; ordinal < typeArguments.Length; ordinal++)
        {
            if (typeArguments[ordinal] is PrimitiveType actualType)
            {
                if (!TryGetGenericMethodTypeArgumentTerm(actualType,
                    out var actualTerm))
                {
                    return false;
                }
                typeTerms.Add(actualTerm);
            }
            else
            {
                typeTerms.Add(new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Formal,
                    PrimitiveType.Void, ordinal, -1, [],
                    false, false, false, false));
            }
        }

        return BoundGenericMethodTypeTerms.TrySubstitute(
            template.ConstantFormals[constantFormalOrdinal].Domain,
            typeTerms.MoveToImmutable(), [], out domain);
    }

    private static void MergeConstantEvidence(
        List<BoundGenericMethodConstantArgument>?[] destination,
        List<BoundGenericMethodConstantArgument>?[] source)
    {
        for (var ordinal = 0; ordinal < source.Length; ordinal++)
        {
            if (source[ordinal] is not { Count: > 0 } values)
            {
                continue;
            }

            destination[ordinal] ??= [];
            destination[ordinal]!.AddRange(values);
        }
    }

    private bool GenericMethodConstantArgumentsEqual(
        BoundGenericMethodConstantArgument left,
        BoundGenericMethodConstantArgument right)
    {
        if (left.Kind != right.Kind
            || left.ConstantOrdinal != right.ConstantOrdinal
            || !AreGenericMethodTypeTermsEquivalent(left.Domain,
                right.Domain))
        {
            return false;
        }

        return left.Kind switch {
            BoundGenericMethodConstantArgumentKind.Value =>
                GenericNominalTypesEquivalent(left.DomainType,
                    right.DomainType)
                && left.CarrierType == right.CarrierType
                && left.Bits == right.Bits,
            BoundGenericMethodConstantArgumentKind.Formal =>
                left.CallerDefinitionOrdinal
                    == right.CallerDefinitionOrdinal
                && left.CallerFormalOwnerKind
                    == right.CallerFormalOwnerKind
                && left.CallerFormalOrdinal == right.CallerFormalOrdinal
                && left.CallerConstantFormalOrdinal
                    == right.CallerConstantFormalOrdinal,
            _ => false,
        };
    }

    private static bool HasUnresolvedGenericMethodTypeFormal(
        BoundGenericMethodTypeTerm term,
        PrimitiveType?[] inferredTypes, bool[]? explicitDefaults,
        int depth = 0)
    {
        if (depth > SemanticVerifier.MaximumDepth
            || term.TypeArguments.IsDefault
            || (term.Kind != BoundGenericMethodTypeTermKind.Span
                && term.IsElementWritable))
        {
            return true;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.Formal)
        {
            return (uint)term.FormalOrdinal < (uint)inferredTypes.Length
                && inferredTypes[term.FormalOrdinal] is null
                && (explicitDefaults is null
                    || !explicitDefaults[term.FormalOrdinal]);
        }

        return term.TypeArguments.Any(argument =>
            HasUnresolvedGenericMethodTypeFormal(argument, inferredTypes,
                explicitDefaults, depth + 1));
    }

    private bool TryInferGenericMethodTypeTerm(
        BoundGenericMethodTypeTerm term, PrimitiveType actualType,
        PrimitiveType?[] inferredTypes, bool[]? explicitDefaults,
        int depth = 0)
    {
        if (depth > SemanticVerifier.MaximumDepth
            || term.TypeArguments.IsDefault
            || (term.Kind != BoundGenericMethodTypeTermKind.Span
                && term.IsElementWritable))
        {
            return false;
        }

        switch (term.Kind)
        {
            case BoundGenericMethodTypeTermKind.Concrete:
                return term.ConcreteType == actualType;

            case BoundGenericMethodTypeTermKind.Formal:
                if ((uint)term.FormalOrdinal
                    >= (uint)inferredTypes.Length)
                {
                    return false;
                }
                if (explicitDefaults is not null
                    && explicitDefaults[term.FormalOrdinal]
                    && inferredTypes[term.FormalOrdinal] is null)
                {
                    return true;
                }
                if (inferredTypes[term.FormalOrdinal]
                    is PrimitiveType established)
                {
                    return established == actualType;
                }
                inferredTypes[term.FormalOrdinal] = actualType;
                return true;

            case BoundGenericMethodTypeTermKind.Span:
                if (term.TypeArguments.Length != 1
                    || !BoundSpanTypeTypes.IsValidType(actualType,
                        _spanTypes))
                {
                    return false;
                }

                var actualSpanType
                    = _spanTypes[BoundSpanTypeTypes.Index(actualType)];
                if ((_genericContext is GenericInstantiation context
                        ? !TryGetGenericMethodSpanFormalOrdinal(
                            actualSpanType, context, out _)
                        : actualSpanType.ElementFormalDefinitionOrdinal
                            != -1)
                    || term.IsInitialization
                        != actualSpanType.IsInitialization
                    || (term.IsWritable
                        && !actualSpanType.IsWritable)
                    || (term.IsElementWritable
                        && !actualSpanType.IsElementWritable
                        && !IsWritableGenericMethodTypeArgument(
                            actualSpanType.ElementType)))
                {
                    return false;
                }

                return TryInferGenericMethodTypeTerm(
                    term.TypeArguments[0], actualSpanType.ElementType,
                    inferredTypes, explicitDefaults, depth + 1);

            case BoundGenericMethodTypeTermKind.SourceGeneric:
                var structureIndex = (int)actualType - 100;
                if ((uint)structureIndex >= (uint)_structs.Count)
                {
                    return false;
                }
                var instance = _sourceGenericTypeInstances.FirstOrDefault(
                    candidate => candidate.StructureIndex == structureIndex);
                if (instance is null
                    || instance.DefinitionIndex
                        != term.SourceGenericTypeDefinitionIndex
                    || instance.TypeArguments.Length
                        != term.TypeArguments.Length)
                {
                    return false;
                }
                for (var ordinal = 0;
                    ordinal < term.TypeArguments.Length; ordinal++)
                {
                    if (!TryInferGenericMethodTypeTerm(
                        term.TypeArguments[ordinal],
                        instance.TypeArguments[ordinal], inferredTypes,
                        explicitDefaults, depth + 1))
                    {
                        return false;
                    }
                }
                return true;

            case BoundGenericMethodTypeTermKind.ImportedGeneric:
                if (term.NominalTypeIdentity
                        is not BoundGenericNominalTypeIdentity identity
                    || !BoundGenericNominalTypeTypes.IsType(actualType))
                {
                    return false;
                }

                var instanceIndex
                    = BoundGenericNominalTypeTypes.Index(actualType);
                if ((uint)instanceIndex
                        >= (uint)_genericNominalTypeInstances.Count)
                {
                    return false;
                }

                var importedInstance
                    = _genericNominalTypeInstances[instanceIndex];
                var importedDefinition
                    = _genericNominalTypeDefinitions.FirstOrDefault(
                        candidate => candidate.Identity == identity);
                if (importedDefinition is null
                    || importedInstance.Definition != identity
                    || importedInstance.Arguments.Length
                        != importedDefinition.Formals.Length
                    || term.TypeArguments.Length
                        != importedDefinition.Formals.Count(
                            static formal => formal.Kind
                                == BoundGenericArgumentKind.Type)
                    || term.ConstantArguments.Length
                        != importedDefinition.Formals.Count(
                            static formal => formal.Kind
                                == BoundGenericArgumentKind.Constant))
                {
                    return false;
                }

                var importedTypeOrdinal = 0;
                for (var formalOrdinal = 0;
                    formalOrdinal < importedDefinition.Formals.Length;
                    formalOrdinal++)
                {
                    var formal = importedDefinition.Formals[formalOrdinal];
                    var actual = importedInstance.Arguments[formalOrdinal];
                    if (formal.Kind == BoundGenericArgumentKind.Type)
                    {
                        if (actual.Kind != BoundGenericArgumentKind.Type
                            || !TryInferGenericMethodTypeTerm(
                                term.TypeArguments[importedTypeOrdinal++],
                                actual.Type, inferredTypes,
                                explicitDefaults, depth + 1))
                        {
                            return false;
                        }
                    }
                    else if (formal.Kind
                            != BoundGenericArgumentKind.Constant
                        || actual.Kind != BoundGenericArgumentKind.Constant)
                    {
                        return false;
                    }
                }
                return true;

            default:
                return false;
        }
    }

    private ImmutableArray<PrimitiveType>? InferGenericTypes(GenericTemplate template, SyntaxNode arguments,
        PrimitiveType? expected, out bool conflictingArguments,
        PrimitiveType?[]? explicitTypeArguments = null,
        bool[]? requireInference = null,
        bool[]? explicitDefaults = null,
        GenericMethodCallArguments? callActuals = null)
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
            var parameter = template.Parameters[index];
            var parameterIndex = parameter.TypeParameterIndex;
            if (parameterIndex < 0
                && parameter.TypeTerm is BoundGenericMethodTypeTerm typeTerm
                && HasUnresolvedGenericMethodTypeFormal(typeTerm, inferred,
                    explicitDefaults))
            {
                var typeTermArgument = supplied[index];
                PrimitiveType? actualType = null;
                if (typeTermArgument.Children
                    is [SyntaxNode typeTermValue])
                {
                    actualType = KnownType(typeTermValue);
                }
                else if (parameter is { IsOut: true, IsInit: false }
                    && typeTermArgument.Children is [
                        SyntaxTokenElement {
                            Token.Kind: TokenKind.OutKeyword,
                        },
                        SyntaxNode {
                            Kind: SyntaxKind.NameExpression,
                        } typeTermOutput,
                    ])
                {
                    actualType = KnownType(typeTermOutput);
                }
                else if (parameter.IsInit
                    && typeTermArgument.Children is [
                        SyntaxTokenElement {
                            Token.Text: "init",
                        },
                        SyntaxNode {
                            Kind: SyntaxKind.NameExpression,
                        } typeTermInitOutput,
                    ])
                {
                    actualType = KnownType(typeTermInitOutput);
                }

                if (actualType is PrimitiveType type
                    && !TryInferGenericMethodTypeTerm(typeTerm, type,
                        inferred, explicitDefaults))
                {
                    conflictingArguments = true;
                    return null;
                }
                continue;
            }
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
        if (template.ReturnTypeTerm
                is BoundGenericMethodTypeTerm returnTypeTerm
            && expected is PrimitiveType expectedResult
            && HasUnresolvedGenericMethodTypeFormal(returnTypeTerm,
                inferred, explicitDefaults))
        {
            var expectedTypeArgument = template.ReferenceReturnTypePattern
                is CheckedReferenceTypeDefinition expectedReference
                    ? InferGenericReferenceType(expectedReference,
                        expectedResult)
                    : expectedResult;
            if (expectedTypeArgument is not PrimitiveType expectedType
                || !TryInferGenericMethodTypeTerm(returnTypeTerm,
                    expectedType, inferred, explicitDefaults))
            {
                conflictingArguments = true;
                return null;
            }
        }

        if (template.ScalarReturnType is null
            && template.ReturnTypeTerm is null
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
            requireInference, explicitDefaults, callActuals);
    }

    private ImmutableArray<PrimitiveType>? CompleteGenericTypeArguments(
        GenericTemplate template, PrimitiveType?[] suppliedTypes,
        bool[]? requireInference = null,
        bool[]? explicitDefaults = null,
        GenericMethodCallArguments? callActuals = null)
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
            var typeDefault = GenericMethodTypeDefault(template, ordinal);
            if ((formal.TypeDefaultSyntax is null
                    && template.ImportedMethod is null)
                || typeDefault is null
                || formal.DefaultTypeParameterDependencies.Any(
                    dependency => dependency < 0 || dependency >= ordinal))
            {
                return null;
            }
            if (selectDefault
                && callActuals?.InferredTypeDefaultArguments[ordinal]
                    is PrimitiveType matchedType)
            {
                var failure = GenericMethodTypeArgumentConstraintFailure(
                    template, ordinal, matchedType);
                if (failure is not null)
                {
                    return null;
                }

                completed[ordinal] = matchedType;
                continue;
            }

            var typeArgumentBuilder
                = ImmutableArray.CreateBuilder<PrimitiveType>(ordinal);
            for (var index = 0; index < ordinal; index++)
            {
                typeArgumentBuilder.Add(completed[index]);
            }
            var typeArguments = typeArgumentBuilder.MoveToImmutable();
            if (BindGenericMethodTypeDefault(template, ordinal,
                    typeArguments, typeDefault, callActuals)
                is not PrimitiveType defaultType)
            {
                return null;
            }

            completed[ordinal] = defaultType;
        }

        return [.. completed];
    }

    private PrimitiveType? BindGenericMethodTypeDefault(
        GenericTemplate template, int ordinal,
        ImmutableArray<PrimitiveType> typeArguments,
        BoundGenericMethodTypeDefault? typeDefault = null,
        GenericMethodCallArguments? callActuals = null)
    {
        if ((uint)ordinal >= (uint)template.TypeParameters.Length
            || typeArguments.Length != ordinal)
        {
            return null;
        }

        typeDefault ??= GenericMethodTypeDefault(template, ordinal);
        if (typeDefault is null)
        {
            if (_genericProofFunctions is null || _genericContext is null)
            {
                return null;
            }

            var completeTypeArguments
                = ImmutableArray.CreateBuilder<PrimitiveType>(
                    template.TypeParameters.Length);
            completeTypeArguments.AddRange(typeArguments);
            while (completeTypeArguments.Count
                < template.TypeParameters.Length)
            {
                completeTypeArguments.Add(PrimitiveType.Void);
            }
            return BindGenericMethodTypeDefaultFromSyntax(template, ordinal,
                completeTypeArguments.MoveToImmutable());
        }

        var diagnosticCount = DiagnosticCount;
        var useSite = template.TypeParameters[ordinal]
            .TypeDefaultSyntax?.Span ?? default;
        if (callActuals is not null
            && !TryPrepareConstantArgumentsForTypeDefault(template,
                typeArguments, callActuals, useSite))
        {
            return null;
        }
        var actualTerms = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(typeArguments.Length);
        foreach (var typeArgument in typeArguments)
        {
            if (!TryCreateGenericMethodActualTypeTerm(typeArgument,
                out var actualTerm))
            {
                return null;
            }
            actualTerms.Add(actualTerm);
        }
        var constantActuals = callActuals is null
            ? []
            : GetConstantArgumentsForTypeDefault(callActuals);
        if (!BoundGenericMethodTypeTerms.TrySubstitute(typeDefault,
                actualTerms.MoveToImmutable(), constantActuals,
                out var substituted))
        {
            return null;
        }

        var contextConstants = template.ConstantFormals.Select(
            (constantFormal, constantOrdinal) =>
            {
                if (callActuals is not null
                    && (callActuals.PreboundConstantArguments[
                            constantOrdinal]
                        ?? callActuals.InferredConstantArguments[
                            constantOrdinal]) is
                        BoundGenericMethodConstantArgument inferred)
                {
                    return inferred;
                }

                return new BoundGenericMethodConstantArgument(
                    constantFormal.Ordinal, constantFormal.ConstantOrdinal,
                    BoundGenericMethodConstantArgumentKind.Formal,
                    constantFormal.Domain, constantFormal.Ordinal,
                    PrimitiveType.Void, PrimitiveType.Void, default) {
                    CallerDefinitionOrdinal = template.DefinitionOrdinal,
                    CallerConstantFormalOrdinal = constantOrdinal,
                };
            }).ToImmutableArray();

        var previousGenericContext = _genericContext;
        try
        {
            _genericContext = new GenericInstantiation(template,
                typeArguments) {
                ConstantArguments = contextConstants,
            };
            if (!TryResolveGenericMethodTypeTerm(substituted.Term,
                useSite, out var result)
                || DiagnosticCount != diagnosticCount
                || GenericMethodTypeArgumentConstraintFailure(template,
                    ordinal, result) is not null)
            {
                return null;
            }

            return result;
        }
        finally
        {
            _genericContext = previousGenericContext;
        }
    }

    private bool TryPrepareConstantArgumentsForTypeDefault(
        GenericTemplate template, ImmutableArray<PrimitiveType> typeArguments,
        GenericMethodCallArguments callActuals, TextSpan useSite)
    {
        for (var ordinal = 0;
            ordinal < template.ConstantFormals.Length; ordinal++)
        {
            if (callActuals.PreboundConstantArguments[ordinal] is not null
                || callActuals.InferredConstantArguments[ordinal] is not null
                || callActuals.ConstantArguments[ordinal]
                    is not SyntaxNode syntax)
            {
                continue;
            }

            var formal = template.ConstantFormals[ordinal];
            if (!TryResolveGenericMethodConstantDomain(template, formal,
                    typeArguments, useSite, out var domainTerm,
                    out var domainType, out var carrierType,
                    [.. callActuals.PreboundConstantArguments
                        .Take(ordinal)])
                || domainType == PrimitiveType.Void
                || carrierType == PrimitiveType.Void)
            {
                continue;
            }

            if (!TryBindGenericMethodConstantArgument(formal, domainTerm,
                    domainType, carrierType, syntax, out var argument))
            {
                return false;
            }
            callActuals.PreboundConstantArguments[ordinal] = argument;
        }

        return true;
    }

    private static ImmutableArray<BoundGenericMethodConstantArgument?>
        GetConstantArgumentsForTypeDefault(
            GenericMethodCallArguments callActuals)
    {
        var result = ImmutableArray.CreateBuilder<
            BoundGenericMethodConstantArgument?>(
                callActuals.InferredConstantArguments.Length);
        for (var ordinal = 0;
            ordinal < callActuals.InferredConstantArguments.Length; ordinal++)
        {
            result.Add(callActuals.PreboundConstantArguments[ordinal]
                ?? callActuals.InferredConstantArguments[ordinal]);
        }
        return result.MoveToImmutable();
    }

    private PrimitiveType? BindGenericMethodTypeDefaultFromSyntax(
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

    private BoundGenericMethodTypeDefault? GenericMethodTypeDefault(
        GenericTemplate template, int ordinal)
    {
        if (template.ImportedMethod is BoundGenericMethodImport imported
            && (uint)ordinal < (uint)imported.Formals.Length)
        {
            return imported.Formals[ordinal].TypeDefault;
        }

        var definitionOrdinal = template.DefinitionOrdinal;
        if (definitionOrdinal >= 0
            && definitionOrdinal < _genericMethodDefinitions.Count
            && _genericMethodDefinitions[definitionOrdinal]
                is BoundGenericMethodDefinition definition
            && definition.DefinitionOrdinal == definitionOrdinal
            && ordinal >= 0 && ordinal < definition.Formals.Length)
        {
            return definition.Formals[ordinal].TypeDefault;
        }

        return _genericProofSource?.GenericMethodTypeDefault(template,
            ordinal);
    }

    private bool TryResolveSourceGenericConstantArguments(
        GenericStructTemplate template,
        ImmutableArray<PrimitiveType> typeArguments,
        ImmutableArray<BoundGenericMethodTypeConstantArgument>
            sourceArguments,
        TextSpan useSite,
        out ImmutableArray<BoundGenericMethodConstantArgument>
            constantArguments)
    {
        constantArguments = [];
        if (sourceArguments.IsDefault
            || sourceArguments.Length != template.ConstantFormals.Length)
        {
            return false;
        }

        var result = ImmutableArray.CreateBuilder<
            BoundGenericMethodConstantArgument>(sourceArguments.Length);
        for (var ordinal = 0; ordinal < sourceArguments.Length; ordinal++)
        {
            var formal = template.ConstantFormals[ordinal];
            var sourceArgument = sourceArguments[ordinal];
            if (sourceArgument is null
                || !TryResolveGenericMethodConstantDomain(
                    template.FormalContext, formal, typeArguments,
                    useSite, out var domainTerm, out var domainType,
                    out var carrierType,
                    [.. result.Select(static argument =>
                        (BoundGenericMethodConstantArgument?)argument)]))
            {
                return false;
            }

            if (sourceArgument.Kind
                == BoundGenericMethodTypeConstantArgumentKind.Value)
            {
                if (domainType == PrimitiveType.Void
                    || carrierType == PrimitiveType.Void
                    || !GenericNominalTypesEquivalent(
                        sourceArgument.DomainType, domainType)
                    || sourceArgument.CarrierType != carrierType
                    || !sourceArgument.Bits.IsCanonicalFor(carrierType))
                {
                    return false;
                }

                result.Add(new BoundGenericMethodConstantArgument(
                    formal.Ordinal, formal.ConstantOrdinal,
                    BoundGenericMethodConstantArgumentKind.Value, domainTerm,
                    -1, domainType, carrierType, sourceArgument.Bits));
                continue;
            }

            if (sourceArgument.Kind
                    != BoundGenericMethodTypeConstantArgumentKind.Formal
                || _genericContext is not GenericInstantiation context)
            {
                return false;
            }

            if (sourceArgument.FormalOwnerKind
                == BoundGenericTypeFormalOwnerKind.SourceGenericType)
            {
                if (context.Template.OwnerGenericTypeDefinitionIndex
                    == sourceArgument.FormalDefinitionOrdinal)
                {
                    if ((uint)sourceArgument.FormalDefinitionOrdinal
                            >= (uint)_sourceGenericTypeDefinitions.Count)
                    {
                        return false;
                    }

                    var ownerDefinition = _sourceGenericTypeDefinitions[
                        sourceArgument.FormalDefinitionOrdinal];
                    var ownerFormal = ownerDefinition.ConstantFormals
                        .FirstOrDefault(candidate =>
                            candidate.ConstantOrdinal
                                == sourceArgument.FormalOrdinal);
                    if (ownerFormal is null
                        || (uint)ownerFormal.ConstantOrdinal
                            >= (uint)context.Template
                                .OwnerConstantArguments.Length)
                    {
                        return false;
                    }

                    var ownerArgument = context.Template
                        .OwnerConstantArguments[
                            ownerFormal.ConstantOrdinal];
                    if (ownerArgument is null
                        || !AreGenericMethodTypeTermsEquivalent(
                            ownerArgument.Domain, domainTerm))
                    {
                        return false;
                    }

                    if (ownerArgument.Kind
                        == BoundGenericMethodConstantArgumentKind.Value)
                    {
                        if (domainType == PrimitiveType.Void
                            || carrierType == PrimitiveType.Void
                            || ownerArgument.DomainType != domainType
                            || ownerArgument.CarrierType != carrierType
                            || !ownerArgument.Bits.IsCanonicalFor(
                                carrierType))
                        {
                            return false;
                        }
                    }
                    else if (ownerArgument.Kind
                            != BoundGenericMethodConstantArgumentKind.Formal
                        || ownerArgument.CallerFormalOwnerKind
                            != BoundGenericTypeFormalOwnerKind.SourceGenericType
                        || ownerArgument.CallerDefinitionOrdinal
                            != sourceArgument.FormalDefinitionOrdinal
                        || ownerArgument.CallerFormalOrdinal != -1
                        || ownerArgument.CallerConstantFormalOrdinal
                            != ownerFormal.ConstantOrdinal)
                    {
                        return false;
                    }

                    result.Add(ownerArgument with {
                        Ordinal = formal.Ordinal,
                        ConstantOrdinal = formal.ConstantOrdinal,
                        Domain = domainTerm,
                    });
                    continue;
                }

                if (context.Template.SourceGenericTypeDefinitionIndex
                        != sourceArgument.FormalDefinitionOrdinal
                    || !context.Template.ConstantFormals.Any(
                        callerFormal => callerFormal.ConstantOrdinal
                            == sourceArgument.FormalOrdinal))
                {
                    return false;
                }

                var ownerFormal = context.Template.ConstantFormals.Single(
                    candidate => candidate.ConstantOrdinal
                        == sourceArgument.FormalOrdinal);
                if (!TryResolveGenericMethodConstantDomain(
                        context.Template, ownerFormal, context.Types,
                        useSite, out var ownerDomain, out _, out _,
                        [.. context.ConstantArguments.Select(
                            static argument =>
                                (BoundGenericMethodConstantArgument?)argument)])
                    || !AreGenericMethodTypeTermsEquivalent(domainTerm,
                        ownerDomain))
                {
                    return false;
                }

                if ((uint)ownerFormal.ConstantOrdinal
                        < (uint)context.ConstantArguments.Length
                    && context.ConstantArguments[
                        ownerFormal.ConstantOrdinal] is
                        BoundGenericMethodConstantArgument ownerArgument)
                {
                    if (!AreGenericMethodTypeTermsEquivalent(
                            ownerArgument.Domain, ownerDomain))
                    {
                        return false;
                    }

                    if (ownerArgument.Kind
                        == BoundGenericMethodConstantArgumentKind.Value)
                    {
                        if (ownerArgument.DomainType == PrimitiveType.Void
                            || ownerArgument.CarrierType
                                == PrimitiveType.Void
                            || !ownerArgument.Bits.IsCanonicalFor(
                                ownerArgument.CarrierType))
                        {
                            return false;
                        }
                    }
                    else if (ownerArgument.Kind
                            != BoundGenericMethodConstantArgumentKind.Formal
                        || ownerArgument.CallerFormalOwnerKind
                            != BoundGenericTypeFormalOwnerKind.SourceGenericType
                        || ownerArgument.CallerDefinitionOrdinal
                            != sourceArgument.FormalDefinitionOrdinal
                        || ownerArgument.CallerFormalOrdinal != -1
                        || ownerArgument.CallerConstantFormalOrdinal
                            != ownerFormal.ConstantOrdinal)
                    {
                        return false;
                    }

                    result.Add(ownerArgument with {
                        Ordinal = formal.Ordinal,
                        ConstantOrdinal = formal.ConstantOrdinal,
                        Domain = domainTerm,
                    });
                    continue;
                }

                if (context.Template.DefinitionOrdinal >= 0)
                {
                    return false;
                }

                result.Add(new BoundGenericMethodConstantArgument(
                    formal.Ordinal, formal.ConstantOrdinal,
                    BoundGenericMethodConstantArgumentKind.Formal,
                    domainTerm, -1, PrimitiveType.Void, PrimitiveType.Void,
                    default) {
                    CallerDefinitionOrdinal =
                        sourceArgument.FormalDefinitionOrdinal,
                    CallerConstantFormalOrdinal = ownerFormal.ConstantOrdinal,
                    CallerFormalOwnerKind =
                        BoundGenericTypeFormalOwnerKind.SourceGenericType,
                });
                continue;
            }

            if (sourceArgument.FormalOwnerKind
                    != BoundGenericTypeFormalOwnerKind.GenericMethod
                || context.Template.DefinitionOrdinal
                    != sourceArgument.FormalDefinitionOrdinal
                || !context.Template.ConstantFormals.Any(callerFormal =>
                    callerFormal.ConstantOrdinal
                        == sourceArgument.FormalOrdinal))
            {
                return false;
            }

            var callerFormal = context.Template.ConstantFormals.Single(
                candidate => candidate.ConstantOrdinal
                    == sourceArgument.FormalOrdinal);
            if (!TryResolveGenericMethodConstantDomain(context.Template,
                    callerFormal, context.Types, useSite,
                    out var callerDomain, out var callerDomainType,
                    out var callerCarrierType,
                    [.. context.ConstantArguments.Select(static argument =>
                        (BoundGenericMethodConstantArgument?)argument)])
                || !AreGenericMethodTypeTermsEquivalent(domainTerm,
                    callerDomain))
            {
                return false;
            }

            if ((uint)callerFormal.ConstantOrdinal
                    < (uint)context.ConstantArguments.Length
                && context.ConstantArguments[
                    callerFormal.ConstantOrdinal] is
                    BoundGenericMethodConstantArgument callerArgument)
            {
                if (!AreGenericMethodTypeTermsEquivalent(
                        callerArgument.Domain, callerDomain))
                {
                    return false;
                }

                if (callerArgument.Kind
                    == BoundGenericMethodConstantArgumentKind.Value)
                {
                    if (callerArgument.DomainType != callerDomainType
                        || callerArgument.CarrierType
                            != callerCarrierType
                        || !callerArgument.Bits.IsCanonicalFor(
                            callerCarrierType))
                    {
                        return false;
                    }
                    result.Add(callerArgument with {
                        Ordinal = formal.Ordinal,
                        ConstantOrdinal = formal.ConstantOrdinal,
                        Domain = domainTerm,
                    });
                    continue;
                }

                if (callerArgument.Kind
                        != BoundGenericMethodConstantArgumentKind.Formal
                    || callerArgument.CallerDefinitionOrdinal < 0
                    || callerArgument.CallerFormalOrdinal < 0
                    || callerArgument.CallerConstantFormalOrdinal < 0)
                {
                    return false;
                }

                result.Add(callerArgument with {
                    Ordinal = formal.Ordinal,
                    ConstantOrdinal = formal.ConstantOrdinal,
                    Domain = domainTerm,
                });
                continue;
            }

            return false;
        }

        constantArguments = result.MoveToImmutable();
        return true;
    }

    private bool TryResolveGenericMethodTypeTerm(
        BoundGenericMethodTypeTerm term, TextSpan useSite,
        out PrimitiveType type)
    {
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        var previousOwnerContext = _genericMethodTypeTermOwnerContext;
        var previousOwnerContextSet
            = _hasGenericMethodTypeTermOwnerContext;
        if (!previousOwnerContextSet)
        {
            _genericMethodTypeTermOwnerContext
                = _genericContext as GenericInstantiation;
            _hasGenericMethodTypeTermOwnerContext = true;
        }

        try
        {
            return TryResolveGenericMethodTypeTerm(term, useSite,
                ref remaining, 0, out type);
        }
        finally
        {
            _genericMethodTypeTermOwnerContext = previousOwnerContext;
            _hasGenericMethodTypeTermOwnerContext = previousOwnerContextSet;
        }
    }

    private bool TryResolveGenericMethodTypeTerm(
        BoundGenericMethodTypeTerm term, TextSpan useSite,
        ref int remaining, int depth, out PrimitiveType type)
    {
        type = default;
        if (term is null || depth > SemanticVerifier.MaximumDepth
            || --remaining < 0 || term.TypeArguments.IsDefault
            || term.ConstantArguments.IsDefault
            || (term.Kind != BoundGenericMethodTypeTermKind.Span
                && term.IsElementWritable)
            || term.ConstantArguments.Length > remaining)
        {
            return false;
        }
        remaining -= term.ConstantArguments.Length;

        switch (term.Kind)
        {
            case BoundGenericMethodTypeTermKind.Concrete:
                if (term.AllowsVoid)
                {
                    if (term.ConcreteType != PrimitiveType.Void)
                    {
                        return false;
                    }
                    type = PrimitiveType.Void;
                    return true;
                }
                type = term.ConcreteType;
                return type != PrimitiveType.Void;

            case BoundGenericMethodTypeTermKind.Formal:
                if (_genericContext is not GenericInstantiation context
                    || (uint)term.FormalOrdinal
                        >= (uint)context.Types.Length
                    || (uint)term.FormalOrdinal
                        >= (uint)context.Template.TypeParameters.Length
                    || (term.IsWritable
                        && !context.Template.TypeParameters[
                            term.FormalOrdinal].RequiresWritable))
                {
                    return false;
                }
                type = context.Types[term.FormalOrdinal];
                return true;

            case BoundGenericMethodTypeTermKind.CheckedReference:
                if (term.TypeArguments.Length != 1
                    || (term.IsGarbageCollected
                        && term.IsInitialization)
                    || !TryResolveGenericMethodTypeTerm(
                        term.TypeArguments[0], useSite, ref remaining,
                        depth + 1, out var referent))
                {
                    return false;
                }
                var isReferentWritable = false;
                var referentTerm = term.TypeArguments[0];
                if (referentTerm.Kind
                    == BoundGenericMethodTypeTermKind.Formal
                    && referentTerm.IsWritable)
                {
                    if (_genericContext
                            is not GenericInstantiation referenceContext
                        || (uint)referentTerm.FormalOrdinal
                            >= (uint)referenceContext.Template
                                .TypeParameters.Length
                        || !referenceContext.Template.TypeParameters[
                            referentTerm.FormalOrdinal].RequiresWritable)
                    {
                        return false;
                    }
                }
                type = GetCheckedReferenceType(
                    new CheckedReferenceTypeDefinition(referent,
                        term.IsWritable, term.IsGarbageCollected,
                        term.IsInitialization, term.IsOptional,
                        IsReferentWritable: isReferentWritable), useSite);
                return true;

            case BoundGenericMethodTypeTermKind.SourceGeneric:
                if ((uint)term.SourceGenericTypeDefinitionIndex
                        >= (uint)_sourceGenericTypeDefinitions.Count)
                {
                    return false;
                }
                var sourceDefinition = _sourceGenericTypeDefinitions[
                    term.SourceGenericTypeDefinitionIndex];
                if (!_genericStructTemplates.TryGetValue(
                        sourceDefinition.QualifiedName,
                        out var sourceTemplate)
                    || sourceTemplate.DefinitionIndex
                        != term.SourceGenericTypeDefinitionIndex)
                {
                    return false;
                }
                var sourceArguments = ImmutableArray.CreateBuilder<
                    PrimitiveType>(term.TypeArguments.Length);
                foreach (var argument in term.TypeArguments)
                {
                    if (!TryResolveGenericMethodTypeTerm(argument, useSite,
                        ref remaining, depth + 1,
                        out var sourceArgument))
                    {
                        return false;
                    }
                    sourceArguments.Add(sourceArgument);
                }
                var completedTypeArguments = sourceArguments.MoveToImmutable();
                if (!TryResolveSourceGenericConstantArguments(
                    sourceTemplate, completedTypeArguments,
                    term.ConstantArguments, useSite,
                    out var constantArguments))
                {
                    return false;
                }
                var diagnosticCount = DiagnosticCount;
                type = InstantiateGenericStruct(sourceTemplate,
                    completedTypeArguments, constantArguments, useSite);
                return DiagnosticCount == diagnosticCount;

            case BoundGenericMethodTypeTermKind.ImportedGeneric:
                if (term.NominalTypeIdentity
                        is not BoundGenericNominalTypeIdentity identity)
                {
                    return false;
                }
                BoundGenericNominalTypeDefinition? nominalDefinition = null;
                foreach (var candidate in _genericNominalTypeDefinitions)
                {
                    if (candidate.Identity != identity)
                    {
                        continue;
                    }
                    if (nominalDefinition is not null)
                    {
                        return false;
                    }
                    nominalDefinition = candidate;
                }
                if (nominalDefinition is null)
                {
                    return false;
                }
                if (nominalDefinition.Formals.Length
                    != term.TypeArguments.Length
                        + term.ConstantArguments.Length)
                {
                    return false;
                }

                var resolvedTypeArguments = ImmutableArray.CreateBuilder<
                    PrimitiveType>(term.TypeArguments.Length);
                foreach (var typeArgument in term.TypeArguments)
                {
                    if (!TryResolveGenericMethodTypeTerm(typeArgument,
                        useSite, ref remaining, depth + 1,
                        out var resolvedTypeArgument))
                    {
                        return false;
                    }
                    resolvedTypeArguments.Add(resolvedTypeArgument);
                }
                var checkedTypeArguments
                    = resolvedTypeArguments.MoveToImmutable();
                var nominalArguments = ImmutableArray.CreateBuilder<
                    BoundGenericNominalTypeArgument>(
                        nominalDefinition.Formals.Length);
                var typeArgumentOrdinal = 0;
                var constantArgumentOrdinal = 0;
                foreach (var formal in nominalDefinition.Formals)
                {
                    if (formal.Kind == BoundGenericArgumentKind.Type)
                    {
                        if ((uint)typeArgumentOrdinal
                                >= (uint)checkedTypeArguments.Length)
                        {
                            return false;
                        }

                        nominalArguments.Add(
                            BoundGenericNominalTypeArgument.ForType(
                                checkedTypeArguments[
                                    typeArgumentOrdinal++]));
                        continue;
                    }

                    if (formal.Kind != BoundGenericArgumentKind.Constant
                        || (uint)constantArgumentOrdinal
                            >= (uint)term.ConstantArguments.Length)
                    {
                        return false;
                    }

                    BoundGenericMethodTypeTerm constantDomainTerm;
                    if (formal.ConstantDomainTypeTerm is
                        BoundGenericMethodTypeTerm domainTemplate)
                    {
                        if (!BoundGenericMethodTypeTerms.TrySubstitute(
                            domainTemplate, term.TypeArguments, [],
                            out constantDomainTerm))
                        {
                            return false;
                        }
                    }
                    else if (formal.ConstantDomainType
                        is PrimitiveType constantDomainType)
                    {
                        constantDomainTerm = new BoundGenericMethodTypeTerm(
                            BoundGenericMethodTypeTermKind.Concrete,
                            constantDomainType, -1, -1, [],
                            false, false, false, false);
                    }
                    else
                    {
                        return false;
                    }

                    if (!BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                            constantDomainTerm, out _)
                        || !BoundGenericMethodTypeTerms
                            .TryGetConstantFormalDependencies(
                                constantDomainTerm,
                                out var constantDomainDependencies)
                        || !constantDomainDependencies.IsEmpty)
                    {
                        return false;
                    }

                    var constantArgument =
                        term.ConstantArguments[constantArgumentOrdinal++];
                    if (constantArgument.Kind
                        == BoundGenericMethodTypeConstantArgumentKind.Formal)
                    {
                        if (constantArgument.FormalOwnerKind
                            == BoundGenericTypeFormalOwnerKind
                                .SourceGenericType)
                        {
                            if ((uint)constantArgument
                                    .FormalDefinitionOrdinal
                                    >= (uint)_sourceGenericTypeDefinitions
                                        .Count
                                || _sourceGenericTypeDefinitions[
                                    constantArgument
                                        .FormalDefinitionOrdinal]
                                    .ConstantFormals.FirstOrDefault(
                                        candidate => candidate
                                            .ConstantOrdinal
                                            == constantArgument
                                                .FormalOrdinal) is null)
                            {
                                return false;
                            }

                            nominalArguments.Add(
                                BoundGenericNominalTypeArgument
                                    .ForConstantFormal(
                                        constantArgument
                                            .FormalDefinitionOrdinal,
                                        constantArgument.FormalOrdinal,
                                        constantDomainTerm,
                                        BoundGenericTypeFormalOwnerKind
                                            .SourceGenericType));
                            continue;
                        }

                        if (constantArgument.FormalOwnerKind
                                != BoundGenericTypeFormalOwnerKind
                                    .GenericMethod
                            || _genericContext is not GenericInstantiation
                                genericContext
                            || (uint)constantArgument.FormalOrdinal
                                >= (uint)genericContext
                                    .ConstantArguments.Length
                            || genericContext.ConstantArguments[
                                constantArgument.FormalOrdinal] is not
                                    BoundGenericMethodConstantArgument actual)
                        {
                            return false;
                        }

                        if (actual.Kind
                            == BoundGenericMethodConstantArgumentKind.Value)
                        {
                            if (!TryResolveGenericMethodTypeTerm(
                                    constantDomainTerm, useSite,
                                    ref remaining, depth + 1,
                                    out var constantDomain)
                                || !TryGetGenericMethodConstantCarrierType(
                                    constantDomain,
                                    out var constantCarrier))
                            {
                                return false;
                            }
                            if (constantArgument.FormalDefinitionOrdinal
                                    != genericContext.Template
                                        .DefinitionOrdinal
                                || !GenericNominalTypesEquivalent(
                                    actual.DomainType, constantDomain)
                                || actual.CarrierType != constantCarrier
                                || !actual.Bits.IsCanonicalFor(
                                    constantCarrier))
                            {
                                return false;
                            }

                            nominalArguments.Add(
                                BoundGenericNominalTypeArgument.ForConstant(
                                    actual.DomainType, actual.CarrierType,
                                    actual.Bits.Low, actual.Bits.High));
                        }
                        else if (actual.Kind
                                == BoundGenericMethodConstantArgumentKind.Formal
                            && IsValidGenericMethodConstantFormalForward(
                                genericContext, constantArgument, actual))
                        {
                            nominalArguments.Add(
                                BoundGenericNominalTypeArgument
                                    .ForConstantFormal(
                                        actual.CallerDefinitionOrdinal,
                                        actual.CallerConstantFormalOrdinal,
                                        actual.Domain,
                                        actual.CallerFormalOwnerKind));
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else if (constantArgument.Kind
                            == BoundGenericMethodTypeConstantArgumentKind.Value)
                    {
                        if (!TryResolveGenericMethodTypeTerm(
                                constantDomainTerm, useSite, ref remaining,
                                depth + 1, out var constantDomain)
                            || !TryGetGenericMethodConstantCarrierType(
                                constantDomain, out var constantCarrier)
                            || !GenericNominalTypesEquivalent(
                                constantArgument.DomainType,
                                constantDomain)
                            || constantArgument.CarrierType != constantCarrier
                            || !constantArgument.Bits.IsCanonicalFor(
                                constantCarrier))
                        {
                            return false;
                        }
                        nominalArguments.Add(
                            BoundGenericNominalTypeArgument.ForConstant(
                                constantArgument.DomainType,
                                constantArgument.CarrierType,
                                constantArgument.Bits.Low,
                                constantArgument.Bits.High));
                    }
                    else
                    {
                        return false;
                    }
                }

                return TryInstantiateGenericNominalTypeArguments(
                    nominalDefinition, nominalArguments.MoveToImmutable(),
                    useSite, out type);

            case BoundGenericMethodTypeTermKind.Span:
                if (term.TypeArguments.Length != 1
                    || term.IsGarbageCollected || term.IsOptional
                    || term.AllowsVoid
                    || !TryResolveGenericMethodTypeTerm(
                        term.TypeArguments[0], useSite, ref remaining,
                        depth + 1, out var spanElementType))
                {
                    return false;
                }

                var spanFormalDefinitionOrdinal = -1;
                var spanFormalOrdinal = -1;
                var isElementWritable = false;
                if (term.TypeArguments[0].Kind
                    == BoundGenericMethodTypeTermKind.Formal)
                {
                    if (_genericContext is not GenericInstantiation
                            typeContext
                        || (uint)term.TypeArguments[0].FormalOrdinal
                            >= (uint)typeContext.Template.TypeParameters
                                .Length
                        || (term.IsElementWritable
                            && !typeContext.Template.TypeParameters[
                                term.TypeArguments[0].FormalOrdinal]
                                .RequiresWritable))
                    {
                        return false;
                    }

                    var ownerContext = _hasGenericMethodTypeTermOwnerContext
                        ? _genericMethodTypeTermOwnerContext
                        : _genericContext as GenericInstantiation;
                    if (ownerContext is not null)
                    {
                        for (var ownerFormal = 0;
                            ownerFormal < ownerContext.Types.Length;
                            ownerFormal++)
                        {
                            if (ownerContext.Types[ownerFormal]
                                != spanElementType)
                            {
                                continue;
                            }

                            if ((uint)ownerFormal
                                >= (uint)ownerContext.Template
                                    .TypeParameters.Length)
                            {
                                return false;
                            }
                            if (term.IsElementWritable
                                && !ownerContext.Template.TypeParameters[
                                    ownerFormal].RequiresWritable)
                            {
                                return false;
                            }

                            var ownerDefinitionOrdinal
                                = ownerContext.Template.DefinitionOrdinal;
                            if (ownerDefinitionOrdinal < 0
                                && ownerContext.Template.ImportedMethod
                                    is BoundGenericMethodImport importedOwner
                                && importedOwner.FormalTypeOwnerOrdinal >= 0)
                            {
                                ownerDefinitionOrdinal
                                    = BoundSpanTypeContract
                                        .ImportedFormalDefinitionOrdinal
                                    - importedOwner
                                        .FormalTypeOwnerOrdinal;
                            }
                            if (ownerDefinitionOrdinal >= 0
                                || ownerDefinitionOrdinal
                                    <= BoundSpanTypeContract
                                        .ImportedFormalDefinitionOrdinal)
                            {
                                spanFormalDefinitionOrdinal
                                    = ownerDefinitionOrdinal;
                                spanFormalOrdinal = ownerFormal;
                                isElementWritable
                                    = term.IsElementWritable;
                            }
                            break;
                        }
                    }
                }
                if (term.IsElementWritable && !isElementWritable
                    && !IsWritableGenericMethodTypeArgument(
                        spanElementType))
                {
                    return false;
                }

                var spanDiagnosticCount = DiagnosticCount;
                type = GetSpanType(spanElementType,
                    spanFormalDefinitionOrdinal, spanFormalOrdinal,
                    isElementWritable, term.IsWritable,
                    term.IsInitialization, useSite);
                return DiagnosticCount == spanDiagnosticCount;

            case BoundGenericMethodTypeTermKind.NativePointer:
                if (term.TypeArguments.Length != 1
                    || !TryResolveGenericMethodTypeTerm(
                        term.TypeArguments[0], useSite, ref remaining,
                        depth + 1, out var pointee))
                {
                    return false;
                }
                type = GetNativePointerType(pointee, term.IsWritable,
                    useSite);
                return true;

            case BoundGenericMethodTypeTermKind.NativeFixedArray:
                if (term.TypeArguments.Length != 1
                    || !NativeFixedArrayContract.SupportsRuntimePolicy(
                        _runtimePolicy)
                    || !TryResolveGenericMethodTypeTerm(
                        term.TypeArguments[0], useSite, ref remaining,
                        depth + 1, out var element)
                    || !NativeFixedArrayContract.TryCreateType(element,
                        term.ElementCount, _structs,
                        _nativeFixedArrayTypes, out type, out _,
                        _checkedReferenceTypes, _enumDefinitions))
                {
                    return false;
                }
                return true;

            case BoundGenericMethodTypeTermKind.NativeFunctionPointer:
                if (term.ParameterCount < 0
                    || term.TypeArguments.Length
                        != term.ParameterCount + 1)
                {
                    return false;
                }
                var functionTypes = ImmutableArray.CreateBuilder<
                    PrimitiveType>(term.TypeArguments.Length);
                foreach (var argument in term.TypeArguments)
                {
                    if (!TryResolveGenericMethodTypeTerm(argument, useSite,
                        ref remaining, depth + 1,
                        out var functionType))
                    {
                        return false;
                    }
                    functionTypes.Add(functionType);
                }
                var returnType = term.TypeArguments[^1].AllowsVoid
                    ? PrimitiveType.Void : functionTypes[^1];
                var signature = new NativeFunctionPointerSignatureDefinition(
                    term.CallingConvention, returnType,
                    functionTypes.ToImmutable()
                        .RemoveAt(term.ParameterCount));
                if (!NativeFunctionPointerContract.IsValidSignature(
                    signature, [.. _nativeFunctionPointerSignatures]))
                {
                    return false;
                }
                type = GetNativeFunctionPointerType(signature);
                return true;

            case BoundGenericMethodTypeTermKind.OptionalValue:
                if (term.TypeArguments.Length != 1
                    || !TryResolveGenericMethodTypeTerm(
                        term.TypeArguments[0], useSite, ref remaining,
                        depth + 1, out var payload)
                    || TryGetOptionalType(payload, useSite)
                        is not PrimitiveType optional)
                {
                    return false;
                }
                type = optional;
                return true;

            default:
                return false;
        }
    }

    private bool TryCreateGenericMethodActualTypeTerm(
        PrimitiveType type, out BoundGenericMethodTypeTerm term)
    {
        term = null!;
        if (_genericContext is GenericInstantiation context)
        {
            for (var ordinal = 0; ordinal < context.Types.Length; ordinal++)
            {
                if (context.Types[ordinal] != type)
                {
                    continue;
                }
                term = new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Formal,
                    PrimitiveType.Void, ordinal, -1, [],
                    false, false, false, false);
                return true;
            }
        }

        if (type == PrimitiveType.Void)
        {
            return false;
        }

        if (BoundSpanTypeTypes.IsValidType(type, _spanTypes))
        {
            return TryGetGenericMethodTypeArgumentTerm(type, out term);
        }

        if (ManagedClassTypes.IsOptional(type))
        {
            return TryGetGenericMethodTypeArgumentTerm(type, out term);
        }

        term = new BoundGenericMethodTypeTerm(
            BoundGenericMethodTypeTermKind.Concrete, type, -1, -1, [],
            false, false, false, false);
        return true;
    }

    private bool TryInstantiateGenericNominalTypeArguments(
        BoundGenericNominalTypeDefinition definition,
        ImmutableArray<BoundGenericNominalTypeArgument> arguments,
        TextSpan useSite, out PrimitiveType type)
    {
        type = default;
        if (definition.Formals.IsDefaultOrEmpty
            || definition.Formals.Length != arguments.Length
            || definition.Formals.Any(static formal =>
                formal.Constraints.IsDefault
                || !formal.Constraints.IsEmpty))
        {
            return false;
        }

        var typeArgumentTerms = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(
                definition.Formals.Count(static formal =>
                    formal.Kind == BoundGenericArgumentKind.Type));
        for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
        {
            if (definition.Formals[ordinal].Kind
                != BoundGenericArgumentKind.Type)
            {
                continue;
            }

            var argument = arguments[ordinal];
            if (argument.Kind != BoundGenericArgumentKind.Type
                || !TryGetGenericMethodTypeArgumentTerm(argument,
                    out var typeArgumentTerm))
            {
                return false;
            }

            var qualifiedTypeArgument = ApplyGenericQueryTypeQualifiers(
                typeArgumentTerm, argument.Qualifiers);
            if (_genericContext is GenericInstantiation genericContext)
            {
                if (!TryNormalizeGenericMethodFormalWriteQualifier(
                        qualifiedTypeArgument, genericContext.Template,
                        out var normalizedTypeArgument))
                {
                    return false;
                }
                typeArgumentTerms.Add(normalizedTypeArgument);
            }
            else
            {
                typeArgumentTerms.Add(qualifiedTypeArgument);
            }
        }
        var checkedTypeArguments = typeArgumentTerms.MoveToImmutable();

        for (var ordinal = 0; ordinal < arguments.Length; ordinal++)
        {
            var formal = definition.Formals[ordinal];
            var argument = arguments[ordinal];
            if (formal.Kind == BoundGenericArgumentKind.Type)
            {
                if (argument.Kind != BoundGenericArgumentKind.Type
                    || formal.OwnershipDomain is not (1 or 2 or 3)
                    || formal.NullabilityDomain is not (1 or 2))
                {
                    return false;
                }

                var unique = IsUniqueGenericArgumentType(argument.Type);
                var copy = IsCopyGenericArgumentType(argument.Type)
                    || BoundEnumTypes.IsType(argument.Type);
                if ((formal.OwnershipDomain == 1
                        && (!copy || unique))
                    || (formal.OwnershipDomain == 2 && !unique)
                    || (formal.NullabilityDomain == 1
                        && IsSupportedOptionalType(argument.Type)))
                {
                    return false;
                }
                continue;
            }

            if (formal.Kind != BoundGenericArgumentKind.Constant
                || argument.Kind != BoundGenericArgumentKind.Constant)
            {
                return false;
            }

            BoundGenericMethodTypeTerm domainTerm;
            if (formal.ConstantDomainTypeTerm
                is BoundGenericMethodTypeTerm constantDomainTerm)
            {
                if (!BoundGenericMethodTypeTerms.TrySubstitute(
                    constantDomainTerm, checkedTypeArguments,
                    out domainTerm))
                {
                    return false;
                }
            }
            else if (formal.ConstantDomainType
                is PrimitiveType constantDomainType)
            {
                domainTerm = new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Concrete,
                    constantDomainType, -1, -1, [], false, false, false,
                    false);
            }
            else
            {
                return false;
            }

            if ((formal.ConstantDomainType is PrimitiveType knownDomain
                && (domainTerm.Kind
                        != BoundGenericMethodTypeTermKind.Concrete
                    || domainTerm.ConcreteType != knownDomain))
                || !BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                    domainTerm, out var domainDependencies)
                || !BoundGenericMethodTypeTerms
                    .TryGetConstantFormalDependencies(domainTerm,
                        out var constantDomainDependencies)
                || !constantDomainDependencies.IsEmpty)
            {
                return false;
            }

            if (argument.HasConstantFormalIdentity)
            {
                if (!AreGenericMethodTypeTermsEquivalent(
                    argument.ConstantFormalDomain!, domainTerm))
                {
                    return false;
                }
            }
            else if (argument.HasDecodedConstant)
            {
                if (!domainDependencies.IsEmpty
                    || !TryResolveGenericMethodTypeTerm(domainTerm,
                        useSite, out var domainType)
                    || !TryGetGenericMethodConstantCarrierType(domainType,
                        out var carrierType)
                    || argument.ConstantDomainType
                        is not PrimitiveType actualDomainType
                    || !GenericNominalTypesEquivalent(actualDomainType,
                        domainType)
                    || argument.ConstantCarrierType != carrierType
                    || (formal.ConstantCarrierType
                        is PrimitiveType formalCarrier
                        && formalCarrier != carrierType)
                    || !new ScalarBits(argument.ConstantLow,
                        argument.ConstantHigh)
                        .IsCanonicalFor(carrierType))
                {
                    return false;
                }
            }
            else if (argument.ConstantProviderIdentity
                    != definition.Identity.ProviderIdentity
                || argument.ConstantSourceRow == 0
                || argument.ConstantSourceRow
                    > (uint)definition.ConstantSourceCount
                || !domainDependencies.IsEmpty)
            {
                return false;
            }
        }

        var key = BoundGenericNominalTypeContract.CreateUseKey(
            definition.Identity, arguments, 0);
        if (_genericNominalTypeUses.TryGetValue(key, out type))
        {
            return true;
        }
        if (_genericNominalTypeInstances.Count
            >= BoundGenericNominalTypeTypes.MaximumCount)
        {
            Report("TRM3005",
                "The generic nominal type instance limit was exceeded.",
                useSite);
            return false;
        }

        type = BoundGenericNominalTypeTypes.Type(
            _genericNominalTypeInstances.Count);
        _genericNominalTypeInstances.Add(
            new BoundGenericNominalTypeInstance(definition.Identity,
                arguments, 0));
        _genericNominalTypeUses.Add(key, type);
        return true;
    }

    private static bool TryNormalizeGenericMethodFormalWriteQualifier(
        BoundGenericMethodTypeTerm term, GenericTemplate template,
        out BoundGenericMethodTypeTerm normalizedTerm)
    {
        normalizedTerm = term;
        if (term.Kind == BoundGenericMethodTypeTermKind.OptionalValue
            && term.TypeArguments.Length == 1)
        {
            if (!TryNormalizeGenericMethodFormalWriteQualifier(
                    term.TypeArguments[0], template,
                    out var normalizedArgument))
            {
                return false;
            }
            normalizedTerm = term with {
                TypeArguments = [normalizedArgument],
            };
            return true;
        }

        if (term.Kind != BoundGenericMethodTypeTermKind.Formal
            || !term.IsWritable)
        {
            return true;
        }

        if ((uint)term.FormalOrdinal
                >= (uint)template.TypeParameters.Length
            || !template.TypeParameters[term.FormalOrdinal]
                .RequiresWritable)
        {
            return false;
        }

        normalizedTerm = term with { IsWritable = false };
        return true;
    }

    private BoundGenericMethodTypeTerm? BindGenericMethodTypeTerm(
        SyntaxNode type, GenericTemplate template, bool applyOptional = true,
        IReadOnlyDictionary<string, BoundGenericMethodTypeTerm>?
            typeSubstitutions = null)
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
                        functionPointerSyntax, template, applyOptional: false,
                        typeSubstitutions: typeSubstitutions);
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
                || BindGenericMethodTypeTerm(referents[0], template,
                    typeSubstitutions: typeSubstitutions)
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

            if (referent.Kind == BoundGenericMethodTypeTermKind.Formal
                && referent.IsWritable)
            {
                if ((uint)referent.FormalOrdinal
                        >= (uint)template.TypeParameters.Length
                    || !template.TypeParameters[
                        referent.FormalOrdinal].RequiresWritable)
                {
                    return null;
                }

                referent = referent with { IsWritable = false };
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
                || BindGenericMethodTypeTerm(elementSyntax, template,
                    typeSubstitutions: typeSubstitutions)
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
                applyOptional: false,
                typeSubstitutions: typeSubstitutions);
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
                template, typeSubstitutions: typeSubstitutions);
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
                template, typeSubstitutions: typeSubstitutions);
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

                if (BindGenericMethodTypeTerm(argument, template,
                    typeSubstitutions: typeSubstitutions)
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

        if (typeArguments.Length == 0
            && typeSubstitutions is not null
            && typeSubstitutions.TryGetValue(name,
                out var substitutedType))
        {
            var qualifiedSubstitution = substitutedType with {
                IsWritable = substitutedType.IsWritable
                    || isWritableType,
                IsGarbageCollected = substitutedType.IsGarbageCollected
                    || isGarbageCollectedType,
            };
            return TryNormalizeGenericMethodFormalWriteQualifier(
                    qualifiedSubstitution, template,
                    out var normalizedSubstitution)
                ? normalizedSubstitution : null;
        }

        var formalOrdinal = template.ParameterIndex(name);
        if (formalOrdinal >= 0)
        {
            if (typeArguments.Length != 0)
            {
                return null;
            }

            var formalTerm = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Formal,
                PrimitiveType.Void, formalOrdinal, -1, [],
                isWritableType, isGarbageCollectedType, false, false);
            return TryNormalizeGenericMethodFormalWriteQualifier(
                    formalTerm, template, out var normalizedFormalTerm)
                ? normalizedFormalTerm : null;
        }

        if (typeArguments.Length == 0
            && template.OwnerTypeArgumentMap.TryGetValue(name,
                out var ownerArgumentType))
        {
            if (!TryCreateGenericMethodActualTypeTerm(ownerArgumentType,
                    out var ownerArgumentTerm))
            {
                return null;
            }

            return ownerArgumentTerm with {
                IsWritable = isWritableType,
                IsGarbageCollected = isGarbageCollectedType,
            };
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
            var sourceDefinition = (uint)definitionIndex
                    < (uint)_sourceGenericTypeDefinitions.Count
                ? _sourceGenericTypeDefinitions[definitionIndex] : null;
            if ((uint)definitionIndex
                    >= (uint)_sourceGenericTypeDefinitions.Count
                || sourceDefinition is null
                || sourceDefinition.Formals.Length
                    != genericStructTemplate.FormalOrder.Length
                || genericStructTemplate.FormalOrder.Length
                    < typeArguments.Length)
            {
                return null;
            }

            var arguments = new BoundGenericMethodTypeTerm?[
                genericStructTemplate.TypeParameters.Length];
            var resolvedTypeArguments = new bool[arguments.Length];
            var constantArguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeConstantArgument>(
                    genericStructTemplate.ConstantFormals.Length);
            var expectedFormalOrdinal = 0;
            var typeArgumentVersion = 0;
            var cachedTypeArgumentVersion = -1;
            ImmutableArray<BoundGenericMethodTypeTerm>
                cachedTypeArguments = [];

            bool AreSourceGenericFormalDependenciesBound(
                ImmutableArray<int> dependencies)
            {
                if (dependencies.IsDefault)
                {
                    return false;
                }

                foreach (var dependency in dependencies)
                {
                    if ((uint)dependency
                        >= (uint)sourceDefinition.Formals.Length)
                    {
                        return false;
                    }

                    var dependencyFormal
                        = sourceDefinition.Formals[dependency];
                    if (dependencyFormal.Kind
                            == BoundGenericMethodFormalKind.Type
                        ? (uint)dependencyFormal.TypeFormalOrdinal
                                >= (uint)resolvedTypeArguments.Length
                            || !resolvedTypeArguments[
                                dependencyFormal.TypeFormalOrdinal]
                        : dependencyFormal.Kind
                                != BoundGenericMethodFormalKind.Constant
                            || (uint)dependencyFormal.ConstantFormalOrdinal
                                >= (uint)constantArguments.Count)
                    {
                        return false;
                    }
                }

                return true;
            }

            ImmutableArray<BoundGenericMethodTypeTerm>
                GetTypeArgumentsForSubstitution()
            {
                if (cachedTypeArgumentVersion != typeArgumentVersion)
                {
                    cachedTypeArguments = arguments
                        .Select((argument, ordinal) => argument
                            ?? new BoundGenericMethodTypeTerm(
                                BoundGenericMethodTypeTermKind.Formal,
                                PrimitiveType.Void, ordinal, -1, [],
                                false, false, false, false))
                        .ToImmutableArray();
                    cachedTypeArgumentVersion = typeArgumentVersion;
                }

                return cachedTypeArguments;
            }

            bool AreSourceGenericTypeDependenciesBound(
                ImmutableArray<int> dependencies)
            {
                if (dependencies.IsDefault)
                {
                    return false;
                }

                foreach (var dependency in dependencies)
                {
                    if ((uint)dependency
                            >= (uint)resolvedTypeArguments.Length
                        || !resolvedTypeArguments[dependency])
                    {
                        return false;
                    }
                }

                return true;
            }

            foreach (var formal in genericStructTemplate.FormalOrder)
            {
                if (formal.Ordinal != expectedFormalOrdinal
                    || (uint)formal.Ordinal
                        >= (uint)sourceDefinition.Formals.Length
                    || (uint)formal.Ordinal
                        >= (uint)genericStructTemplate.FormalSyntax.Length)
                {
                    return null;
                }
                expectedFormalOrdinal++;

                var sourceFormal = sourceDefinition.Formals[formal.Ordinal];
                var argumentSyntax = formal.Ordinal < typeArguments.Length
                    ? typeArguments[formal.Ordinal] : null;
                if (formal.Kind == BoundGenericMethodFormalKind.Type)
                {
                    if (formal.TypeFormalOrdinal != sourceFormal.TypeFormalOrdinal
                        || (uint)formal.TypeFormalOrdinal
                            >= (uint)arguments.Length)
                    {
                        return null;
                    }

                    if (argumentSyntax is null
                        || argumentSyntax.Kind
                            == SyntaxKind.GenericDefaultArgument)
                    {
                        var defaultSyntax =
                            genericStructTemplate.FormalSyntax[
                                formal.Ordinal].TypeDefault;
                        if (!sourceFormal.HasTypeDefault
                            || defaultSyntax is null
                            || !AreSourceGenericTypeDependenciesBound(
                                sourceFormal.DefaultTypeFormalDependencies)
                            || !AreSourceGenericFormalDependenciesBound(
                                sourceFormal.DefaultFormalDependencies)
                            || BindGenericMethodTypeTerm(defaultSyntax,
                                genericStructTemplate.FormalContext)
                                is not BoundGenericMethodTypeTerm defaultTerm
                            || !BoundGenericMethodTypeTerms
                                .TrySubstituteSourceGenericTypeFormals(
                                    defaultTerm,
                                    GetTypeArgumentsForSubstitution(),
                                    definitionIndex,
                                    constantArguments.ToImmutable(),
                                    out var boundDefault))
                        {
                            return null;
                        }
                        arguments[formal.TypeFormalOrdinal] = boundDefault;
                    }
                    else
                    {
                        if (argumentSyntax.Kind
                                is SyntaxKind.ConstantGenericArgument
                                    or SyntaxKind.GenericInferenceArgument
                            || BindGenericMethodTypeTerm(argumentSyntax,
                                template,
                                typeSubstitutions: typeSubstitutions)
                                is not BoundGenericMethodTypeTerm
                                    boundArgument)
                        {
                            return null;
                        }
                        arguments[formal.TypeFormalOrdinal] = boundArgument;
                    }
                    resolvedTypeArguments[formal.TypeFormalOrdinal] = true;
                    typeArgumentVersion++;
                    continue;
                }

                if (formal.Kind
                        != BoundGenericMethodFormalKind.Constant
                    || formal.ConstantFormalOrdinal
                        != sourceFormal.ConstantFormalOrdinal
                    || (uint)formal.ConstantFormalOrdinal
                        >= (uint)genericStructTemplate.ConstantFormals.Length)
                {
                    return null;
                }

                var constantFormal = genericStructTemplate.ConstantFormals[
                    formal.ConstantFormalOrdinal];
                var typeArgumentsForSubstitution
                    = GetTypeArgumentsForSubstitution();
                if (constantFormal.Ordinal != formal.Ordinal
                    || constantFormal.ConstantOrdinal
                        != formal.ConstantFormalOrdinal
                    || !BoundGenericMethodTypeTerms
                        .TrySubstituteSourceGenericTypeFormals(
                            constantFormal.Domain,
                            typeArgumentsForSubstitution,
                            definitionIndex,
                            constantArguments.ToImmutable(),
                            out var constantDomain)
                    || !BoundGenericMethodTypeTerms
                        .TryGetConstantFormalDependencies(constantDomain,
                            out _))
                {
                    return null;
                }

                var usesDeclaredDefault = argumentSyntax is null
                    || argumentSyntax.Kind
                        == SyntaxKind.GenericDefaultArgument;
                if (usesDeclaredDefault)
                {
                    if (!sourceFormal.HasConstantDefault
                        || !AreSourceGenericTypeDependenciesBound(
                            sourceFormal.DefaultTypeFormalDependencies)
                        || !AreSourceGenericFormalDependenciesBound(
                            sourceFormal.DefaultFormalDependencies)
                        || constantFormal.DefaultSyntax is not
                            SyntaxNode defaultSyntax)
                    {
                        return null;
                    }
                    argumentSyntax = defaultSyntax;
                }

                if (argumentSyntax is not SyntaxNode selectedArgumentSyntax)
                {
                    return null;
                }

                BoundGenericMethodTypeConstantArgument constantArgument;
                if (usesDeclaredDefault
                    && TryGetGenericConstantArgumentName(
                        selectedArgumentSyntax,
                        out var defaultName)
                    && genericStructTemplate.ConstantFormals.FirstOrDefault(
                        candidate => candidate.Name == defaultName)
                        is GenericMethodConstantFormal previousFormal
                    && previousFormal.Ordinal < formal.Ordinal
                    && (uint)previousFormal.ConstantOrdinal
                        < (uint)constantArguments.Count)
                {
                    if (!BoundGenericMethodTypeTerms
                        .TrySubstituteSourceGenericTypeFormals(
                            previousFormal.Domain,
                            typeArgumentsForSubstitution,
                            definitionIndex,
                            constantArguments.ToImmutable(),
                            out var previousDomain)
                        || !AreGenericMethodTypeTermsEquivalent(
                            previousDomain, constantDomain))
                    {
                        return null;
                    }
                    constantArgument = constantArguments[
                        previousFormal.ConstantOrdinal];
                }
                else if (usesDeclaredDefault
                    && (!sourceFormal.DefaultFormalDependencies.IsEmpty
                        || !sourceFormal.DefaultTypeFormalDependencies
                            .IsEmpty))
                {
                    return null;
                }
                else if (selectedArgumentSyntax.Kind
                        == SyntaxKind.GenericInferenceArgument
                    || !TryBindGenericMethodTypeConstantArgument(template,
                        constantFormal, constantDomain,
                        selectedArgumentSyntax,
                        out constantArgument))
                {
                    return null;
                }
                constantArguments.Add(constantArgument);
            }

            if (expectedFormalOrdinal != sourceDefinition.Formals.Length
                || arguments.Any(static argument => argument is null)
                || constantArguments.Count
                    != genericStructTemplate.ConstantFormals.Length)
            {
                return null;
            }

            var typeArgumentsBound = arguments
                .Select(static argument => argument!).ToImmutableArray();
            return new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.SourceGeneric,
                PrimitiveType.Void, -1, definitionIndex,
                typeArgumentsBound, isWritableType,
                isGarbageCollectedType, false, false) {
                ConstantArguments = constantArguments.MoveToImmutable(),
            };
        }
        if (hasImportedGenericDefinition)
        {
            var definition = genericNominalDefinition;
            if ((definition.NominalKind != 2
                    && !(definition.NominalKind == 1
                        && definition.TransparentConstantCarrierType
                            is not null))
                || typeArguments.Length > definition.Formals.Length)
            {
                return null;
            }

            var actualSyntaxes = new SyntaxNode?[
                definition.Formals.Length];
            Array.Copy(typeArguments, actualSyntaxes,
                typeArguments.Length);
            var arguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeTerm>(
                    definition.Formals.Count(static formal =>
                        formal.Kind == BoundGenericArgumentKind.Type));
            for (var ordinal = 0;
                ordinal < definition.Formals.Length; ordinal++)
            {
                var formal = definition.Formals[ordinal];
                var argument = actualSyntaxes[ordinal];
                if (formal.Kind == BoundGenericArgumentKind.Type)
                {
                    if (formal.Constraints.IsDefault
                        || !formal.Constraints.IsEmpty
                        || formal.OwnershipDomain is not (1 or 2 or 3)
                        || formal.OwnershipDomain == 2
                        || formal.NullabilityDomain is not (1 or 2))
                    {
                        return null;
                    }

                    BoundGenericMethodTypeTerm boundArgument;
                    if (argument is null
                        || argument.Kind
                            == SyntaxKind.GenericDefaultArgument)
                    {
                        if (formal.Default is not
                            {
                                Kind: BoundGenericArgumentKind.Type,
                                DecodedType: PrimitiveType decodedType,
                            } defaultType
                            || defaultType.ProviderIdentity
                                != definition.Identity.ProviderIdentity)
                        {
                            return null;
                        }

                        var defaultTerm =
                            new BoundGenericMethodTypeTerm(
                                BoundGenericMethodTypeTermKind.Concrete,
                                decodedType, -1, -1, [],
                                false, false, false, false);
                        boundArgument = ApplyGenericQueryTypeQualifiers(
                            defaultTerm, defaultType.TypeQualifiers);
                    }
                    else
                    {
                        if (argument.Kind
                                is SyntaxKind.ConstantGenericArgument
                                    or SyntaxKind.GenericInferenceArgument
                            || BindGenericMethodTypeTerm(argument, template,
                                typeSubstitutions: typeSubstitutions)
                                is not BoundGenericMethodTypeTerm
                                    explicitArgument)
                        {
                            return null;
                        }

                        boundArgument = explicitArgument;
                    }

                    arguments.Add(boundArgument);
                }
                else if (formal.Kind != BoundGenericArgumentKind.Constant
                    || formal.Constraints.IsDefault
                    || !formal.Constraints.IsEmpty)
                {
                    return null;
                }
            }

            var checkedTypeArguments = arguments.MoveToImmutable();
            var constantArguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeConstantArgument>(
                    definition.Formals.Count(static formal =>
                        formal.Kind == BoundGenericArgumentKind.Constant));
            var constantArgumentsByFormal =
                new BoundGenericMethodTypeConstantArgument?[
                    definition.Formals.Length];
            var constantDomainsByFormal =
                new BoundGenericMethodTypeTerm?[
                    definition.Formals.Length];
            for (var ordinal = 0;
                ordinal < definition.Formals.Length; ordinal++)
            {
                var formal = definition.Formals[ordinal];
                if (formal.Kind != BoundGenericArgumentKind.Constant)
                {
                    continue;
                }

                BoundGenericMethodTypeTerm domainTerm;
                if (formal.ConstantDomainTypeTerm is
                    BoundGenericMethodTypeTerm constantDomainTerm)
                {
                    if (!BoundGenericMethodTypeTerms.TrySubstitute(
                        constantDomainTerm, checkedTypeArguments, [],
                        out domainTerm))
                    {
                        return null;
                    }
                }
                else if (formal.ConstantDomainType
                    is PrimitiveType constantDomainType)
                {
                    domainTerm = new BoundGenericMethodTypeTerm(
                        BoundGenericMethodTypeTermKind.Concrete,
                        constantDomainType, -1, -1, [],
                        false, false, false, false);
                }
                else
                {
                    return null;
                }

                BoundGenericMethodTypeConstantArgument boundConstantArgument;
                var constantSyntax = actualSyntaxes[ordinal];
                if (constantSyntax is null
                    || constantSyntax.Kind
                        == SyntaxKind.GenericDefaultArgument)
                {
                    if (!TryGetImportedGenericConstantDefault(
                        formal, ordinal, definition,
                        constantArgumentsByFormal,
                        constantDomainsByFormal, domainTerm,
                        out boundConstantArgument))
                    {
                        return null;
                    }
                }
                else if (constantSyntax.Kind
                            == SyntaxKind.GenericInferenceArgument
                    || !TryBindGenericMethodTypeConstantArgument(template,
                        domainTerm, constantSyntax,
                        out boundConstantArgument))
                {
                    return null;
                }

                constantArgumentsByFormal[ordinal] =
                    boundConstantArgument;
                constantDomainsByFormal[ordinal] = domainTerm;
                constantArguments.Add(boundConstantArgument);
            }

            return new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.ImportedGeneric,
                PrimitiveType.Void, -1, -1,
                checkedTypeArguments, isWritableType,
                isGarbageCollectedType, false, false) {
                NominalTypeIdentity = definition.Identity,
                ConstantArguments = constantArguments.ToImmutable(),
            };
        }

        if (FindAliasDefinition(name) is AliasDefinition alias)
        {
            return BindGenericMethodTypeAlias(alias, type, typeArguments,
                template, isWritableType, isGarbageCollectedType,
                typeSubstitutions);
        }

        var formalNames = template.TypeParameters
            .Select(static formal => formal.Name).ToArray();
        if (!GenericTypeDefaultDependencies(type, formalNames).IsEmpty)
        {
            return null;
        }

        return BindConcreteGenericMethodTypeTerm(type, template);
    }

    private bool TryGetImportedGenericConstantDefault(
        BoundGenericNominalTypeFormal formal, int formalOrdinal,
        BoundGenericNominalTypeDefinition definition,
        BoundGenericMethodTypeConstantArgument?[] argumentsByFormal,
        BoundGenericMethodTypeTerm?[] domainsByFormal,
        BoundGenericMethodTypeTerm domain,
        out BoundGenericMethodTypeConstantArgument argument)
    {
        argument = null!;
        if (formal.Default is not
                {
                    Kind: BoundGenericArgumentKind.Constant,
                } defaultValue
            || defaultValue.ProviderIdentity
                != definition.Identity.ProviderIdentity)
        {
            return false;
        }

        if (defaultValue.EarlierFormalOrdinal is int earlierFormalOrdinal)
        {
            if ((uint)earlierFormalOrdinal >= (uint)formalOrdinal
                || definition.Formals[earlierFormalOrdinal].Kind
                    != BoundGenericArgumentKind.Constant
                || argumentsByFormal[earlierFormalOrdinal] is not
                    BoundGenericMethodTypeConstantArgument earlierArgument
                || domainsByFormal[earlierFormalOrdinal] is not
                    BoundGenericMethodTypeTerm earlierDomain
                || !AreGenericMethodTypeTermsEquivalent(
                    earlierDomain, domain))
            {
                return false;
            }

            argument = earlierArgument;
            return true;
        }

        if (defaultValue.DecodedConstantArgument is not
            BoundGenericNominalTypeArgument decoded)
        {
            return false;
        }

        if (decoded.HasConstantFormalIdentity)
        {
            if (!AreGenericMethodTypeTermsEquivalent(
                    decoded.ConstantFormalDomain!, domain))
            {
                return false;
            }

            argument = new BoundGenericMethodTypeConstantArgument(
                BoundGenericMethodTypeConstantArgumentKind.Formal,
                decoded.ConstantFormalOrdinal, PrimitiveType.Void,
                PrimitiveType.Void, default) {
                FormalDefinitionOrdinal =
                    decoded.ConstantFormalDefinitionOrdinal,
                FormalOwnerKind = decoded.ConstantFormalOwnerKind,
            };
            return true;
        }

        if (!decoded.HasDecodedConstant
            || decoded.ConstantDomainType is not PrimitiveType domainType
            || decoded.ConstantCarrierType is not PrimitiveType carrierType
            || !TryGetGenericMethodTypeArgumentTerm(domainType,
                out var actualDomain)
            || !AreGenericMethodTypeTermsEquivalent(actualDomain, domain)
            || !TryGetGenericMethodConstantCarrierType(domainType,
                out var expectedCarrier)
            || expectedCarrier != carrierType)
        {
            return false;
        }

        var bits = new ScalarBits(decoded.ConstantLow,
            decoded.ConstantHigh);
        if (!bits.IsCanonicalFor(carrierType))
        {
            return false;
        }

        argument = new BoundGenericMethodTypeConstantArgument(
            BoundGenericMethodTypeConstantArgumentKind.Value, -1,
            domainType, carrierType, bits);
        return true;
    }

    private BoundGenericMethodTypeTerm? BindGenericMethodTypeAlias(
        AliasDefinition alias, SyntaxNode useSite,
        SyntaxNode[] typeArguments, GenericTemplate template,
        bool isWritable, bool isGarbageCollected,
        IReadOnlyDictionary<string, BoundGenericMethodTypeTerm>?
            typeSubstitutions)
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

        var substitutions = new Dictionary<string,
            BoundGenericMethodTypeTerm>(StringComparer.Ordinal);
        if (typeSubstitutions is not null)
        {
            foreach (var (name, value) in typeSubstitutions)
            {
                substitutions.Add(name, value);
            }
        }
        for (var ordinal = 0; ordinal < alias.TypeParameters.Length;
            ordinal++)
        {
            var argument = typeArguments[ordinal];
            var diagnosticCount = DiagnosticCount;
            if (argument.Kind
                    is SyntaxKind.ConstantGenericArgument
                        or SyntaxKind.ExistentialTypeArgument
                        or SyntaxKind.GenericInferenceArgument
                        or SyntaxKind.GenericDefaultArgument
                || BindGenericMethodTypeTerm(argument, template,
                    typeSubstitutions: typeSubstitutions)
                    is not BoundGenericMethodTypeTerm boundArgument)
            {
                if (DiagnosticCount == diagnosticCount)
                {
                    Report("TRM3005",
                        "A generic alias requires complete type arguments.",
                        argument.Span);
                }
                return null;
            }

            substitutions[alias.TypeParameters[ordinal]] = boundArgument;
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

        var previousContext = CaptureContext();
        SetContext(alias.Tree, alias.Declaration);
        var aliasDiagnosticCount = DiagnosticCount;
        alias.IsResolving = true;
        _aliasResolutionStack.Add(alias);
        try
        {
            if (BindGenericMethodTypeTerm(alias.Target, template,
                    typeSubstitutions: substitutions)
                is not BoundGenericMethodTypeTerm target
                || DiagnosticCount != aliasDiagnosticCount)
            {
                if (alias.TypeParameters.IsEmpty)
                {
                    alias.IsInvalid = true;
                }
                return null;
            }

            var qualifiedTarget = target with {
                IsWritable = target.IsWritable || isWritable,
                IsGarbageCollected = target.IsGarbageCollected
                    || isGarbageCollected,
            };
            return TryNormalizeGenericMethodFormalWriteQualifier(
                    qualifiedTarget, template, out var normalizedTarget)
                ? normalizedTarget : null;
        }
        finally
        {
            _aliasResolutionStack.RemoveAt(
                _aliasResolutionStack.Count - 1);
            alias.IsResolving = false;
            RestoreContext(previousContext);
        }
    }

    private bool TryBindGenericMethodTypeConstantArgument(
        GenericTemplate template, GenericMethodConstantFormal formal,
        BoundGenericMethodTypeTerm domain,
        SyntaxNode syntax,
        out BoundGenericMethodTypeConstantArgument argument)
    {
        argument = null!;
        if (TryGetGenericConstantArgumentName(syntax, out var name))
        {
            var callerFormal = template.ConstantFormals.FirstOrDefault(
                candidate => candidate.Name == name);
            if (callerFormal is not null
                && template.DefinitionOrdinal >= 0
                && AreGenericMethodTypeTermsEquivalent(
                    callerFormal.Domain, domain))
            {
                argument = new BoundGenericMethodTypeConstantArgument(
                    BoundGenericMethodTypeConstantArgumentKind.Formal,
                    callerFormal.ConstantOrdinal, PrimitiveType.Void,
                    PrimitiveType.Void, default) {
                    FormalDefinitionOrdinal = template.DefinitionOrdinal,
                };
                return true;
            }
            if (callerFormal is not null
                && template.SourceGenericTypeDefinitionIndex >= 0
                && AreGenericMethodTypeTermsEquivalent(
                    callerFormal.Domain, domain))
            {
                argument = new BoundGenericMethodTypeConstantArgument(
                    BoundGenericMethodTypeConstantArgumentKind.Formal,
                    callerFormal.ConstantOrdinal, PrimitiveType.Void,
                    PrimitiveType.Void, default) {
                    FormalDefinitionOrdinal =
                        template.SourceGenericTypeDefinitionIndex,
                    FormalOwnerKind =
                        BoundGenericTypeFormalOwnerKind.SourceGenericType,
                };
                return true;
            }
        }

        if (!TryResolveGenericMethodTypeTerm(domain, syntax.Span,
                out var domainType)
            || !TryGetGenericMethodConstantCarrierType(domainType,
                out var carrierType))
        {
            return false;
        }

        var closedFormal = formal with {
            Domain = domain,
            PlaceholderType = domainType,
        };
        if (!TryBindGenericMethodConstantArgument(closedFormal, domain,
                domainType, carrierType, syntax, out var boundArgument))
        {
            return false;
        }

        if (boundArgument.Kind
            == BoundGenericMethodConstantArgumentKind.Value)
        {
            argument = new BoundGenericMethodTypeConstantArgument(
                BoundGenericMethodTypeConstantArgumentKind.Value, -1,
                boundArgument.DomainType, boundArgument.CarrierType,
                boundArgument.Bits);
            return true;
        }

        if (boundArgument.Kind
                == BoundGenericMethodConstantArgumentKind.Formal
            && boundArgument.CallerDefinitionOrdinal >= 0
            && boundArgument.CallerConstantFormalOrdinal >= 0)
        {
            argument = new BoundGenericMethodTypeConstantArgument(
                BoundGenericMethodTypeConstantArgumentKind.Formal,
                boundArgument.CallerConstantFormalOrdinal,
                PrimitiveType.Void, PrimitiveType.Void, default) {
                FormalDefinitionOrdinal =
                    boundArgument.CallerDefinitionOrdinal,
            };
            return true;
        }

        return false;
    }

    private bool TryBindGenericMethodTypeConstantArgument(
        GenericTemplate template, BoundGenericMethodTypeTerm domain,
        SyntaxNode syntax,
        out BoundGenericMethodTypeConstantArgument argument)
    {
        argument = null!;
        if (TryGetGenericConstantArgumentName(syntax, out var name)
            && template.DefinitionOrdinal >= 0)
        {
            var callerFormal = template.ConstantFormals.FirstOrDefault(
                candidate => candidate.Name == name);
            if (callerFormal is not null)
            {
                if (!AreGenericMethodTypeTermsEquivalent(
                        callerFormal.Domain, domain))
                {
                    return false;
                }

                argument = new BoundGenericMethodTypeConstantArgument(
                    BoundGenericMethodTypeConstantArgumentKind.Formal,
                    callerFormal.ConstantOrdinal, PrimitiveType.Void,
                    PrimitiveType.Void, default) {
                    FormalDefinitionOrdinal = template.DefinitionOrdinal,
                };
                return true;
            }

            var ownerDefinitionOrdinal
                = template.OwnerGenericTypeDefinitionIndex;
            if ((uint)ownerDefinitionOrdinal
                    < (uint)_sourceGenericTypeDefinitions.Count)
            {
                var ownerDefinition = _sourceGenericTypeDefinitions[
                    ownerDefinitionOrdinal];
                var ownerFormal = ownerDefinition.ConstantFormals
                    .FirstOrDefault(candidate => candidate.Name == name);
                if (ownerFormal is not null)
                {
                    if (template.OwnerConstantArguments.IsDefault
                        || (uint)ownerFormal.ConstantOrdinal
                            >= (uint)template.OwnerConstantArguments.Length)
                    {
                        return false;
                    }

                    var ownerActual = template.OwnerConstantArguments[
                        ownerFormal.ConstantOrdinal];
                    if (!AreGenericMethodTypeTermsEquivalent(
                        ownerActual.Domain, domain))
                    {
                        return false;
                    }

                    if (ownerActual.Kind
                        == BoundGenericMethodConstantArgumentKind.Value)
                    {
                        if (!TryResolveGenericMethodTypeTerm(domain,
                                syntax.Span, out var domainType)
                            || !TryGetGenericMethodConstantCarrierType(
                                domainType, out var carrierType)
                            || !GenericNominalTypesEquivalent(
                                ownerActual.DomainType, domainType)
                            || ownerActual.CarrierType != carrierType
                            || !ownerActual.Bits.IsCanonicalFor(carrierType))
                        {
                            return false;
                        }

                        argument =
                            new BoundGenericMethodTypeConstantArgument(
                                BoundGenericMethodTypeConstantArgumentKind
                                    .Value, -1, domainType, carrierType,
                                ownerActual.Bits);
                        return true;
                    }

                    if (ownerActual.Kind
                            != BoundGenericMethodConstantArgumentKind.Formal
                        || ownerActual.CallerDefinitionOrdinal < 0
                        || ownerActual.CallerConstantFormalOrdinal < 0
                        || ownerActual.CallerFormalOwnerKind is not (
                            BoundGenericTypeFormalOwnerKind.GenericMethod
                            or BoundGenericTypeFormalOwnerKind
                                .SourceGenericType))
                    {
                        return false;
                    }

                    argument =
                        new BoundGenericMethodTypeConstantArgument(
                            BoundGenericMethodTypeConstantArgumentKind
                                .Formal,
                            ownerActual.CallerConstantFormalOrdinal,
                            PrimitiveType.Void, PrimitiveType.Void, default) {
                            FormalDefinitionOrdinal =
                                ownerActual.CallerDefinitionOrdinal,
                            FormalOwnerKind =
                                ownerActual.CallerFormalOwnerKind,
                        };
                    return true;
                }
            }
        }

        if (!BoundGenericMethodTypeTerms.TryGetFormalDependencies(domain,
                out var typeFormalDependencies)
            || !typeFormalDependencies.IsEmpty
            || !BoundGenericMethodTypeTerms.TryGetConstantFormalDependencies(
                domain, out var constantFormalDependencies)
            || !constantFormalDependencies.IsEmpty
            || !TryResolveGenericMethodTypeTerm(domain, syntax.Span,
                out var domainType)
            || !TryGetGenericMethodConstantCarrierType(domainType,
                out var carrierType))
        {
            return false;
        }

        var temporaryFormal = new GenericMethodConstantFormal(-1, -1,
            "$type-constant", domain, domainType, null, []);
        if (!TryBindGenericMethodConstantArgument(temporaryFormal, domain,
                domainType, carrierType, syntax, out var boundArgument))
        {
            return false;
        }

        if (boundArgument.Kind
            == BoundGenericMethodConstantArgumentKind.Value)
        {
            argument = new BoundGenericMethodTypeConstantArgument(
                BoundGenericMethodTypeConstantArgumentKind.Value, -1,
                boundArgument.DomainType, boundArgument.CarrierType,
                boundArgument.Bits);
            return true;
        }

        if (boundArgument.Kind
                == BoundGenericMethodConstantArgumentKind.Formal
            && boundArgument.CallerDefinitionOrdinal >= 0
            && boundArgument.CallerConstantFormalOrdinal >= 0)
        {
            argument = new BoundGenericMethodTypeConstantArgument(
                BoundGenericMethodTypeConstantArgumentKind.Formal,
                boundArgument.CallerConstantFormalOrdinal,
                PrimitiveType.Void, PrimitiveType.Void, default) {
                FormalDefinitionOrdinal =
                    boundArgument.CallerDefinitionOrdinal,
            };
            return true;
        }

        return false;
    }

    private bool TryBindGenericMethodTypeConstantArgument(
        GenericTemplate template, BoundGenericNominalTypeFormal formal,
        SyntaxNode syntax,
        out BoundGenericMethodTypeConstantArgument argument)
    {
        argument = null!;
        if (formal.ConstantDomainType is not PrimitiveType domainType
            || !TryGetGenericMethodConstantCarrierType(domainType,
                out var carrierType))
        {
            return false;
        }
        if (formal.ConstantCarrierType is PrimitiveType formalCarrier
            && formalCarrier != carrierType)
        {
            return false;
        }

        if (syntax.Kind == SyntaxKind.TypeName
            && !syntax.Children.OfType<SyntaxNode>().Any())
        {
            var name = TypeNameIdentifier(syntax);
            var constantFormal = template.ConstantFormals.FirstOrDefault(
                candidate => candidate.Name == name);
            if (constantFormal is not null)
            {
                if (constantFormal.Domain.Kind
                        != BoundGenericMethodTypeTermKind.Concrete
                    || constantFormal.Domain.ConcreteType != domainType)
                {
                    return false;
                }

                argument = new BoundGenericMethodTypeConstantArgument(
                    BoundGenericMethodTypeConstantArgumentKind.Formal,
                    constantFormal.ConstantOrdinal, PrimitiveType.Void,
                    PrimitiveType.Void, default) {
                    FormalDefinitionOrdinal = template.DefinitionOrdinal,
                };
                return true;
            }
        }

        var domainTerm = new BoundGenericMethodTypeTerm(
            BoundGenericMethodTypeTermKind.Concrete, domainType, -1, -1,
            [], false, false, false, false);
        var temporaryFormal = new GenericMethodConstantFormal(-1, -1,
            formal.Name, domainTerm, domainType, null, []);
        if (!TryBindGenericMethodConstantArgument(temporaryFormal,
            domainTerm, domainType, carrierType, syntax,
            out var boundArgument)
            || boundArgument.Kind
                != BoundGenericMethodConstantArgumentKind.Value)
        {
            return false;
        }

        argument = new BoundGenericMethodTypeConstantArgument(
            BoundGenericMethodTypeConstantArgumentKind.Value, -1,
            boundArgument.DomainType, boundArgument.CarrierType,
            boundArgument.Bits);
        return true;
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
                if ((!candidate.Formals.IsEmpty
                        || IsSourceLookupStandardClassDefinition(candidate))
                    && GetNominalTypeQualifiedName(candidate) == qualifiedName
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

    private static ImmutableArray<int>
        GenericMethodTypeTermConstantDependencies(
            BoundGenericMethodTypeTerm term)
    {
        var dependencies = new SortedSet<int>();
        var pending = new Stack<BoundGenericMethodTypeTerm>();
        pending.Push(term);
        while (pending.TryPop(out var current))
        {
            foreach (var argument in current.ConstantArguments)
            {
                if (argument.Kind
                    == BoundGenericMethodTypeConstantArgumentKind.Formal)
                {
                    _ = dependencies.Add(argument.FormalOrdinal);
                }
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
            if (current.ConstantArguments.Any(static argument =>
                argument.Kind
                    == BoundGenericMethodTypeConstantArgumentKind.Formal))
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
                        || nominalDefinition.Formals.IsDefault)
                    {
                        return false;
                    }
                    var typeArgumentOrdinal = 0;
                    var constantArgumentOrdinal = 0;
                    foreach (var formal in nominalDefinition.Formals)
                    {
                        if (formal.Kind == BoundGenericArgumentKind.Type)
                        {
                            if ((uint)typeArgumentOrdinal
                                >= (uint)current.Term.TypeArguments.Length)
                            {
                                return false;
                            }
                            var argument = current.Term.TypeArguments[
                                typeArgumentOrdinal++];
                            var argumentDomain =
                                GetGenericMethodTypeTermOwnershipDomain(
                                    argument, template, 0);
                            if (formal.Constraints.IsDefault
                                || !formal.Constraints.IsEmpty
                                || formal.OwnershipDomain is not (1 or 2 or 3)
                                || formal.OwnershipDomain == 2
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
                            continue;
                        }

                        if (formal.Kind
                                != BoundGenericArgumentKind.Constant
                            || (uint)constantArgumentOrdinal
                                >= (uint)current.Term.ConstantArguments
                                    .Length
                            || formal.ConstantDomainType is not
                                PrimitiveType constantDomain
                            || !TryGetGenericMethodConstantCarrierType(
                                constantDomain, out var constantCarrier)
                            || (formal.ConstantCarrierType is
                                PrimitiveType formalCarrier
                                && formalCarrier != constantCarrier))
                        {
                            return false;
                        }

                        var constantArgument =
                            current.Term.ConstantArguments[
                                constantArgumentOrdinal++];
                        if (constantArgument.Kind
                                == BoundGenericMethodTypeConstantArgumentKind.Formal)
                        {
                            if ((uint)constantArgument.FormalOrdinal
                                    >= (uint)template.ConstantFormals
                                        .Length
                                || template.ConstantFormals[
                                    constantArgument.FormalOrdinal].Domain
                                    is not {
                                        Kind:
                                            BoundGenericMethodTypeTermKind.Concrete,
                                        ConcreteType: var formalDomain,
                                    }
                                || formalDomain != constantDomain)
                            {
                                return false;
                            }
                        }
                        else if (constantArgument.Kind
                                != BoundGenericMethodTypeConstantArgumentKind.Value
                            || !GenericNominalTypesEquivalent(
                                constantArgument.DomainType,
                                constantDomain)
                            || constantArgument.CarrierType
                                != constantCarrier
                            || !constantArgument.Bits.IsCanonicalFor(
                                constantCarrier))
                        {
                            return false;
                        }
                    }
                    if (typeArgumentOrdinal
                            != current.Term.TypeArguments.Length
                        || constantArgumentOrdinal
                            != current.Term.ConstantArguments.Length)
                    {
                        return false;
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
                case BoundGenericMethodTypeTermKind.Span:
                    if (current.Term.FormalOrdinal != -1
                        || current.Term.SourceGenericTypeDefinitionIndex != -1
                        || current.Term.ConcreteType != PrimitiveType.Void
                        || current.Term.IsGarbageCollected
                        || current.Term.IsOptional
                        || current.Term.NominalTypeIdentity is not null
                        || current.Term.TypeArguments.Length != 1
                        || (current.Term.IsElementWritable
                            && (current.Term.TypeArguments[0].Kind
                                    != BoundGenericMethodTypeTermKind.Formal
                                || (uint)current.Term.TypeArguments[0]
                                    .FormalOrdinal
                                    >= (uint)template.TypeParameters.Length
                                || !template.TypeParameters[
                                    current.Term.TypeArguments[0]
                                        .FormalOrdinal]
                                    .RequiresWritable)))
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
            case BoundGenericMethodTypeTermKind.Span:
                return !term.IsInitialization
                    && term.TypeArguments.Length == 1 ? (byte)1 : null;
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
                if (importedDefinition is null)
                {
                    return null;
                }
                if (importedDefinition.NominalKind == 1
                    && importedDefinition.TransparentConstantCarrierType
                        is not null)
                {
                    return 1;
                }
                if (importedDefinition.NominalKind != 2)
                {
                    return null;
                }
                return _reference?.CoreTaskType?.Definition == identity
                    ? (byte)2 : null;
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
            || definition.FormalOrder.IsDefault
            || definition.FormalOrder.Length
                != definition.Formals.Length
            || definition.Formals.Count(static formal =>
                formal.Kind == BoundGenericMethodFormalKind.Type)
                != term.TypeArguments.Length
            || definition.ConstantFormals.IsDefault
            || definition.ConstantFormals.Length
                != term.ConstantArguments.Length)
        {
            return null;
        }

        var domain = (byte)1;
        var typeArgumentOrdinal = 0;
        foreach (var slot in definition.FormalOrder)
        {
            if (slot.Kind != BoundGenericMethodFormalKind.Type
                || (uint)slot.TypeFormalOrdinal
                    >= (uint)term.TypeArguments.Length)
            {
                continue;
            }

            var formal = definition.Formals[slot.Ordinal];
            var argument = term.TypeArguments[typeArgumentOrdinal++];
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

        return typeArgumentOrdinal == term.TypeArguments.Length
            ? domain : null;
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
            BoundGenericMethodTypeTermKind.Span => !term.IsOptional,
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
            BoundGenericMethodTypeTermKind.Span =>
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
        GenericTemplate template, GenericParameter parameter,
        ImmutableArray<PrimitiveType> types, TextSpan span,
        ImmutableArray<BoundGenericMethodConstantArgument>
            constantArguments = default)
    {
        if (parameter.TypeTerm is BoundGenericMethodTypeTerm typeTerm)
        {
            if (constantArguments.IsDefault)
            {
                constantArguments =
                    CreateFormalGenericMethodConstantArguments(template);
            }

            var previous = _genericContext;
            var previousOwnerContext
                = _genericMethodTypeTermOwnerContext;
            var previousOwnerContextSet
                = _hasGenericMethodTypeTermOwnerContext;
            try
            {
                _genericContext = new GenericInstantiation(template, types) {
                    ConstantArguments = constantArguments,
                };
                if (!previousOwnerContextSet)
                {
                    _genericMethodTypeTermOwnerContext
                        = previous as GenericInstantiation;
                    _hasGenericMethodTypeTermOwnerContext = true;
                }
                if (!TryResolveGenericMethodTypeTerm(typeTerm, span,
                    out var resolved))
                {
                    throw new InvalidOperationException(
                        "A generic method parameter type term could not be resolved.");
                }

                return resolved;
            }
            finally
            {
                _genericContext = previous;
                _genericMethodTypeTermOwnerContext
                    = previousOwnerContext;
                _hasGenericMethodTypeTermOwnerContext
                    = previousOwnerContextSet;
            }
        }

        if (parameter.ReferenceTypePattern
                is not CheckedReferenceTypeDefinition referencePattern)
        {
            if (parameter.TypeTerm is null
                && parameter.ScalarType is null
                && (parameter.TypeParameterIndex < 0
                    || parameter.TypeParameterIndex >= types.Length))
            {
                throw new InvalidOperationException(
                    "A generic method parameter type is unresolved.");
            }
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

    private static ImmutableArray<BoundGenericMethodConstantArgument>
        CreateFormalGenericMethodConstantArguments(
            GenericTemplate template)
        => [
            .. template.ConstantFormals.Select(formal =>
                new BoundGenericMethodConstantArgument(formal.Ordinal,
                    formal.ConstantOrdinal,
                    BoundGenericMethodConstantArgumentKind.Formal,
                    formal.Domain, formal.Ordinal, PrimitiveType.Void,
                    PrimitiveType.Void, default) {
                    CallerDefinitionOrdinal = template.DefinitionOrdinal,
                    CallerConstantFormalOrdinal = formal.ConstantOrdinal,
                }),
        ];

    private PrimitiveType ResolveGenericReturnType(
        GenericTemplate template, ImmutableArray<PrimitiveType> types,
        TextSpan span,
        ImmutableArray<BoundGenericMethodConstantArgument>
            constantArguments = default)
    {
        if (template.ReturnTypeTerm
            is BoundGenericMethodTypeTerm returnTypeTerm)
        {
            var previous = _genericContext;
            var previousOwnerContext
                = _genericMethodTypeTermOwnerContext;
            var previousOwnerContextSet
                = _hasGenericMethodTypeTermOwnerContext;
            var diagnosticCount = DiagnosticCount;
            try
            {
                _genericContext = new GenericInstantiation(template, types) {
                    ConstantArguments = constantArguments.IsDefault
                        ? CreateFormalGenericMethodConstantArguments(template)
                        : constantArguments,
                };
                if (!previousOwnerContextSet)
                {
                    _genericMethodTypeTermOwnerContext
                        = previous as GenericInstantiation;
                    _hasGenericMethodTypeTermOwnerContext = true;
                }
                if (TryResolveGenericMethodTypeTerm(returnTypeTerm, span,
                    out var resolvedType)
                    && DiagnosticCount == diagnosticCount)
                {
                    return resolvedType;
                }

                if (DiagnosticCount == diagnosticCount)
                {
                    Report("TRM3007",
                        "A generic method result type term could not be substituted in this context.",
                        span);
                }
                return PrimitiveType.Void;
            }
            finally
            {
                _genericContext = previous;
                _genericMethodTypeTermOwnerContext
                    = previousOwnerContext;
                _hasGenericMethodTypeTermOwnerContext
                    = previousOwnerContextSet;
            }
        }

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

    private void AddReferenceGenericMethodImports(
        CompilationReference reference)
    {
        var importedDefinitions = new HashSet<(
            string ProviderIdentity, string SchemaIdentity, string DigestHex,
            uint MethodDefId)>();
        foreach (var imported in reference.GenericMethodImports)
        {
            var origin = imported.Origin;
            if (!origin.IsMethodDefTarget
                && origin.MethodDefId != 0
                && !importedDefinitions.Add((
                    origin.ProviderIdentity, origin.SchemaIdentity,
                    Convert.ToHexString(
                        origin.ProviderArtifactDigest.AsSpan()),
                    origin.MethodDefId)))
            {
                continue;
            }

            var proofFunction = imported.ProofFunction;
            var hasClassMethod = TryGetStandardClassGenericMethodOwner(
                reference, imported, out var ownerClass,
                out var classMethod);
            PrimitiveType? ownerType = null;
            var ownerName = string.Empty;
            if (hasClassMethod)
            {
                if (classMethod.GenericParameterCount
                        != imported.Formals.Length
                    || (classMethod.ReceiverTypeSpecReference == 0)
                        != imported.Signature.IsStatic
                    || !TryGetStandardClassGenericMethodOwnerType(reference,
                        ownerClass, out var selectedOwnerType))
                {
                    Report("TRM3005",
                        "A selected generic class MethodDef has an incomplete owner or receiver view.",
                        _trees[0].Root.Span);
                    continue;
                }

                ownerType = selectedOwnerType;
                if (!imported.Signature.IsStatic)
                {
                    if (proofFunction.ParameterCount == 0
                        || proofFunction.Variables.IsDefaultOrEmpty)
                    {
                        Report("TRM3005",
                            "A selected generic instance MethodDef has no receiver variable.",
                            _trees[0].Root.Span);
                        continue;
                    }

                    var receiverType
                        = proofFunction.Variables[0].Type;
                    if (!BoundGenericNominalTypeContract
                            .IsNominalClassType(receiverType,
                                reference.GenericNominalTypeInstances,
                                reference.GenericNominalTypeDefinitions))
                    {
                        Report("TRM3005",
                            "A selected generic instance MethodDef has no nominal class receiver.",
                            _trees[0].Root.Span);
                        continue;
                    }

                    var receiverInstance = reference
                        .GenericNominalTypeInstances[
                            BoundGenericNominalTypeTypes.Index(
                                receiverType)];
                    if (receiverInstance.Definition != ownerClass.Identity
                        || !receiverInstance.Arguments.IsEmpty
                        || receiverInstance.Qualifiers != 0)
                    {
                        Report("TRM3005",
                            "A selected generic instance MethodDef receiver does not match its owner TypeDef.",
                            _trees[0].Root.Span);
                        continue;
                    }
                }

                ownerName = string.IsNullOrEmpty(ownerClass.Namespace)
                    ? ownerClass.Name
                    : ownerClass.Namespace + "." + ownerClass.Name;
            }

            var receiverCount = imported.Signature.IsStatic ? 0 : 1;
            var parameterCount = proofFunction.ParameterCount
                - receiverCount;
            if (parameterCount < 0
                || proofFunction.Variables.Length
                    < proofFunction.ParameterCount
                || (receiverCount != 0
                    && proofFunction.Variables[0].Name != "this"))
            {
                Report("TRM3005",
                    "A selected generic class MethodDef has an invalid receiver or parameter view.",
                    _trees[0].Root.Span);
                continue;
            }

            var typeParameters = ImmutableArray.CreateBuilder<
                GenericTypeParameter>(imported.Formals.Length);
            foreach (var formal in imported.Formals)
            {
                typeParameters.Add(new GenericTypeParameter(formal.Name,
                    []) {
                    PlaceholderType = formal.PlaceholderType,
                    OwnershipDomain = formal.OwnershipDomain,
                    NullabilityDomain = formal.NullabilityDomain,
                    RequiresBlittable = formal.RequiresBlittable,
                    RequiresWritable = formal.RequiresWritable,
                    InterfaceConstraints =
                        formal.InterfaceConstraints,
                    DefaultTypeParameterDependencies =
                        formal.TypeDefault?.FormalDependencies ?? [],
                });
            }

            var parameters = ImmutableArray.CreateBuilder<GenericParameter>(
                parameterCount);
            for (var parameter = 0; parameter < parameterCount; parameter++)
            {
                var variable = proofFunction.Variables[
                    receiverCount + parameter];
                var defaults = imported.OrdinaryParameterDefaults;
                var parameterDefault
                    = defaults.Length == parameterCount
                        ? defaults[parameter] : null;
                if (!defaults.IsDefaultOrEmpty
                    && defaults.Length != parameterCount)
                {
                    Report("TRM3005",
                        "The imported generic method has an incomplete ordinary default vector.",
                        proofFunction.Span);
                }

                var formalOrdinal = -1;
                for (var ordinal = 0;
                    ordinal < imported.Formals.Length; ordinal++)
                {
                    if (imported.Formals[ordinal].PlaceholderType
                        == variable.Type)
                    {
                        formalOrdinal = ordinal;
                        break;
                    }
                }

                if (formalOrdinal >= 0)
                {
                    parameters.Add(new GenericParameter(variable.Name,
                        null, formalOrdinal, variable.IsOut,
                        variable.IsInit) {
                        DefaultValue =
                            parameterDefault?.Value as BoundConstant,
                        DefaultFormalDependencies =
                            parameterDefault?.FormalDependencies ?? [],
                    });
                }
                else if (TryCreateImportedGenericMethodTypeTerm(
                        variable.Type, imported.Formals,
                        reference.SpanTypes,
                        imported.FormalTypeOwnerOrdinal, 0,
                        out var typeTerm)
                    && BoundGenericMethodTypeTerms
                        .TryGetFormalDependencies(typeTerm,
                            out var dependencies)
                    && !dependencies.IsEmpty)
                {
                    parameters.Add(new GenericParameter(variable.Name,
                        null, -1, variable.IsOut, variable.IsInit) {
                        TypeTerm = typeTerm,
                        DefaultValue =
                            parameterDefault?.Value as BoundConstant,
                        DefaultFormalDependencies =
                            parameterDefault?.FormalDependencies ?? [],
                    });
                }
                else
                {
                    parameters.Add(new GenericParameter(variable.Name,
                        variable.Type, -1, variable.IsOut,
                        variable.IsInit) {
                        DefaultValue =
                            parameterDefault?.Value as BoundConstant,
                        DefaultFormalDependencies =
                            parameterDefault?.FormalDependencies ?? [],
                    });
                }
            }

            var returnFormalOrdinal = -1;
            for (var ordinal = 0; ordinal < imported.Formals.Length;
                ordinal++)
            {
                if (imported.Formals[ordinal].PlaceholderType
                    == proofFunction.ReturnType)
                {
                    returnFormalOrdinal = ordinal;
                    break;
                }
            }

            PrimitiveType? scalarReturnType
                = returnFormalOrdinal < 0
                    ? proofFunction.ReturnType : null;
            BoundGenericMethodTypeTerm? returnTypeTerm = null;
            if (returnFormalOrdinal < 0
                && TryCreateImportedGenericMethodTypeTerm(
                    proofFunction.ReturnType, imported.Formals,
                    reference.SpanTypes,
                    imported.FormalTypeOwnerOrdinal, 0,
                    out var candidateReturnTypeTerm)
                && BoundGenericMethodTypeTerms
                    .TryGetFormalDependencies(candidateReturnTypeTerm,
                        out var returnTypeDependencies)
                && !returnTypeDependencies.IsEmpty)
            {
                scalarReturnType = null;
                returnTypeTerm = candidateReturnTypeTerm;
            }

            var tree = _trees[0];
            var templateName = hasClassMethod
                ? ownerName + "." + classMethod.Name
                : proofFunction.Name;
            var template = new GenericTemplate(
                templateName, typeParameters.ToImmutable(),
                scalarReturnType,
                returnFormalOrdinal, parameters.ToImmutable(),
                tree.Root, tree.Root, tree) {
                DefinitionOrdinal = -1,
                FormalTypeOwnerOrdinal =
                    imported.FormalTypeOwnerOrdinal,
                IsPublic = hasClassMethod
                    ? (classMethod.Flags & 3) == 2 : true,
                RequiresUnsafeCaller =
                    imported.Signature.RequiresUnsafeCaller,
                DoesNotReturn =
                    imported.ProofFunction.DoesNotReturn,
                IsStatic = imported.Signature.IsStatic,
                IsMutable = imported.Signature.IsMutable,
                MemberName = hasClassMethod ? classMethod.Name : null,
                ContainingType = hasClassMethod ? ownerName : null,
                OwnerName = hasClassMethod ? ownerName : null,
                OwnerType = ownerType,
                FormalOrder = imported.Signature.FormalOrder,
                ImportedMethod = imported,
                ReturnTypeTerm = returnTypeTerm,
                DependencyContracts = proofFunction.DependencyContracts,
                BoundDeclarationContractClauses =
                    imported.ProofFunction.DeclarationContractClauses,
            };
            if (!_templates.TryAdd(template.Name, template))
            {
                Report("TRM3001",
                    $"The imported generic method key '{template.Name}' is ambiguous.",
                    tree.Root.Span);
            }
        }
    }

    private GenericTemplate CreateGenericTemplateFromImport(
        BoundGenericMethodImport imported, SyntaxTree tree,
        IReadOnlyList<BoundSpanTypeDefinition> spanTypes)
    {
        var typeParameters = ImmutableArray.CreateBuilder<
            GenericTypeParameter>(imported.Formals.Length);
        foreach (var formal in imported.Formals)
        {
            typeParameters.Add(new GenericTypeParameter(formal.Name, []) {
                PlaceholderType = formal.PlaceholderType,
                OwnershipDomain = formal.OwnershipDomain,
                NullabilityDomain = formal.NullabilityDomain,
                RequiresBlittable = formal.RequiresBlittable,
                RequiresWritable = formal.RequiresWritable,
                InterfaceConstraints = formal.InterfaceConstraints,
                DefaultTypeParameterDependencies =
                    formal.TypeDefault?.FormalDependencies ?? [],
            });
        }

        var proofFunction = imported.ProofFunction;
        var hasClassOwner
            = imported.Origin.ClassOwner is BoundGenericMethodClassOwner;
        var receiverCount = imported.Signature.IsStatic ? 0 : 1;
        var parameterCount = proofFunction.ParameterCount - receiverCount;
        var ownerName = hasClassOwner
            ? proofFunction.DeclaringStruct ?? string.Empty
            : string.Empty;
        PrimitiveType? ownerType = null;
        if (parameterCount < 0
            || proofFunction.Variables.Length < proofFunction.ParameterCount
            || (receiverCount != 0
                && (proofFunction.ParameterCount == 0
                    || proofFunction.Variables.IsDefaultOrEmpty
                    || proofFunction.Variables[0].Name != "this")))
        {
            throw new InvalidOperationException(
                "The imported generic method has an invalid receiver or parameter view.");
        }

        if (hasClassOwner)
        {
            if (receiverCount != 0)
            {
                var receiverType = proofFunction.Variables[0].Type;
                if (!BoundGenericNominalTypeContract.IsNominalClassType(
                        receiverType, _genericNominalTypeInstances,
                        _genericNominalTypeDefinitions)
                    || _genericNominalTypeInstances[
                        BoundGenericNominalTypeTypes.Index(receiverType)]
                        .Definition
                        != imported.Origin.ClassOwner!.Identity)
                {
                    throw new InvalidOperationException(
                        "The imported generic instance MethodDef has an invalid class receiver.");
                }

                ownerType = receiverType;
            }
            else
            {
                for (var index = 0;
                    index < _genericNominalTypeInstances.Count; index++)
                {
                    var instance = _genericNominalTypeInstances[index];
                    if (instance.Definition
                            == imported.Origin.ClassOwner!.Identity
                        && instance.Arguments.IsEmpty
                        && instance.Qualifiers == 0
                        && instance.TypeSpecReference == 0
                        && instance.TypeSpecProviderIdentity is null
                        && instance.TypeSpecForm == 0)
                    {
                        ownerType = BoundGenericNominalTypeTypes.Type(index);
                        break;
                    }
                }

                if (ownerType is null)
                {
                    throw new InvalidOperationException(
                        "An imported generic class MethodDef has no selected owner TypeDef instance.");
                }
            }
        }

        var importedDefaults = imported.OrdinaryParameterDefaults;
        if (!importedDefaults.IsDefaultOrEmpty
            && importedDefaults.Length != parameterCount)
        {
            throw new InvalidOperationException(
                "The imported generic method default vector has an invalid extent.");
        }

        var parameters = ImmutableArray.CreateBuilder<GenericParameter>(
            parameterCount);
        for (var parameter = 0; parameter < parameterCount; parameter++)
        {
            var variable = proofFunction.Variables[
                receiverCount + parameter];
            var parameterDefault = importedDefaults.Length
                == parameterCount
                    ? importedDefaults[parameter] : null;
            var formalOrdinal = -1;
            for (var ordinal = 0;
                ordinal < imported.Formals.Length; ordinal++)
            {
                if (imported.Formals[ordinal].PlaceholderType
                    == variable.Type)
                {
                    formalOrdinal = ordinal;
                    break;
                }
            }

            if (formalOrdinal >= 0)
            {
                parameters.Add(new GenericParameter(variable.Name, null,
                    formalOrdinal, variable.IsOut, variable.IsInit) {
                    DefaultValue =
                        parameterDefault?.Value as BoundConstant,
                    DefaultFormalDependencies =
                        parameterDefault?.FormalDependencies ?? [],
                });
            }
            else if (TryCreateImportedGenericMethodTypeTerm(
                    variable.Type, imported.Formals, spanTypes,
                    imported.FormalTypeOwnerOrdinal, 0, out var typeTerm)
                && BoundGenericMethodTypeTerms
                    .TryGetFormalDependencies(typeTerm,
                        out var dependencies)
                && !dependencies.IsEmpty)
            {
                parameters.Add(new GenericParameter(variable.Name, null,
                    -1, variable.IsOut, variable.IsInit) {
                    TypeTerm = typeTerm,
                    DefaultValue =
                        parameterDefault?.Value as BoundConstant,
                    DefaultFormalDependencies =
                        parameterDefault?.FormalDependencies ?? [],
                });
            }
            else
            {
                parameters.Add(new GenericParameter(variable.Name,
                    variable.Type, -1, variable.IsOut, variable.IsInit) {
                    DefaultValue =
                        parameterDefault?.Value as BoundConstant,
                    DefaultFormalDependencies =
                        parameterDefault?.FormalDependencies ?? [],
                });
            }
        }

        var returnFormalOrdinal = -1;
        for (var ordinal = 0; ordinal < imported.Formals.Length; ordinal++)
        {
            if (imported.Formals[ordinal].PlaceholderType
                == proofFunction.ReturnType)
            {
                returnFormalOrdinal = ordinal;
                break;
            }
        }

        PrimitiveType? scalarReturnType = returnFormalOrdinal < 0
            ? proofFunction.ReturnType : null;
        BoundGenericMethodTypeTerm? returnTypeTerm = null;
        if (returnFormalOrdinal < 0
            && TryCreateImportedGenericMethodTypeTerm(
                proofFunction.ReturnType, imported.Formals, spanTypes,
                imported.FormalTypeOwnerOrdinal, 0,
                out var candidateReturnTypeTerm)
            && BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                candidateReturnTypeTerm, out var returnTypeDependencies)
            && !returnTypeDependencies.IsEmpty)
        {
            scalarReturnType = null;
            returnTypeTerm = candidateReturnTypeTerm;
        }

        var name = hasClassOwner
            ? ownerName + "." + proofFunction.Name
            : imported.Origin.IsMethodDefTarget
            ? "<MethodDef:"
                + imported.Origin.MethodDefId.ToString("X8",
                    System.Globalization.CultureInfo.InvariantCulture)
                + ">"
            : imported.Origin.ExportKey;
        return new GenericTemplate(name, typeParameters.ToImmutable(),
            scalarReturnType,
            returnFormalOrdinal, parameters.ToImmutable(), tree.Root,
            tree.Root, tree) {
            DefinitionOrdinal = -1,
            IsPublic = hasClassOwner
                ? imported.Signature.IsPublic
                : !imported.Origin.IsMethodDefTarget,
            FormalTypeOwnerOrdinal =
                imported.FormalTypeOwnerOrdinal,
            RequiresUnsafeCaller = imported.Signature.RequiresUnsafeCaller,
            DoesNotReturn = proofFunction.DoesNotReturn,
            IsStatic = imported.Signature.IsStatic,
            IsMutable = imported.Signature.IsMutable,
            MemberName = hasClassOwner ? proofFunction.Name : null,
            ContainingType = hasClassOwner ? ownerName : null,
            OwnerName = hasClassOwner ? ownerName : null,
            OwnerType = ownerType,
            FormalOrder = imported.Signature.FormalOrder,
            ImportedMethod = imported,
            ReturnTypeTerm = returnTypeTerm,
            BoundDeclarationContractClauses =
                imported.ProofFunction.DeclarationContractClauses,
        };
    }

    private static bool TryGetStandardClassGenericMethodOwner(
        CompilationReference reference, BoundGenericMethodImport imported,
        out BoundStandardClassTypeView owner,
        out BoundStandardClassMethodDeclaration method)
    {
        var origin = imported.Origin;
        foreach (var declaration in reference.StandardClassDeclarations)
        {
            if (StringComparer.Ordinal.Equals(
                    declaration.Declaration.Identity.ModuleIdentity,
                    origin.ProviderIdentity))
            {
                foreach (var candidate
                    in declaration.Declaration.Methods)
                {
                    if (candidate.DefinitionId == origin.MethodDefId
                        && candidate.GenericParameterCount != 0)
                    {
                        owner = declaration.Declaration;
                        method = candidate;
                        return true;
                    }
                }
            }

            foreach (var expectation in declaration.Expectations)
            {
                if (!StringComparer.Ordinal.Equals(
                        expectation.ExpectedDeclaration.Identity
                            .ModuleIdentity, origin.ProviderIdentity))
                {
                    continue;
                }

                foreach (var candidate
                    in expectation.ExpectedDeclaration.Methods)
                {
                    if (candidate.DefinitionId == origin.MethodDefId
                        && candidate.GenericParameterCount != 0)
                    {
                        owner = declaration.Declaration;
                        method = candidate;
                        return true;
                    }
                }
            }
        }

        owner = null!;
        method = null!;
        return false;
    }

    private static bool TryGetStandardClassGenericMethodOwnerType(
        CompilationReference reference, BoundStandardClassTypeView owner,
        out PrimitiveType type)
    {
        for (var index = 0;
            index < reference.GenericNominalTypeInstances.Length; index++)
        {
            var instance = reference.GenericNominalTypeInstances[index];
            if (instance.Definition == owner.Identity
                && instance.Arguments.IsEmpty
                && instance.Qualifiers == 0
                && instance.TypeSpecReference == 0
                && instance.TypeSpecProviderIdentity is null
                && instance.TypeSpecForm == 0)
            {
                type = BoundGenericNominalTypeTypes.Type(index);
                return true;
            }
        }

        type = PrimitiveType.Void;
        return false;
    }

    private static bool TryCreateImportedGenericMethodTypeTerm(
        PrimitiveType type,
        ImmutableArray<BoundGenericMethodFormal> formals,
        IReadOnlyList<BoundSpanTypeDefinition> spanTypes,
        int formalTypeOwnerOrdinal, int depth,
        out BoundGenericMethodTypeTerm term, bool allowFormal = true)
    {
        term = null!;
        if (depth > SemanticVerifier.MaximumDepth
            || formals.IsDefault
            || spanTypes is null)
        {
            return false;
        }

        for (var ordinal = 0; allowFormal && ordinal < formals.Length;
            ordinal++)
        {
            if (formals[ordinal].PlaceholderType == type)
            {
                term = new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Formal,
                    PrimitiveType.Void, ordinal, -1, [],
                    false, false, false, false);
                return true;
            }
        }

        if (!BoundSpanTypeTypes.IsValidType(type, spanTypes))
        {
            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Concrete,
                type, -1, -1, [], false, false, false, false) {
                AllowsVoid = type == PrimitiveType.Void,
            };
            return type != PrimitiveType.Void;
        }

        var span = spanTypes[BoundSpanTypeTypes.Index(type)];
        BoundGenericMethodTypeTerm elementTerm;
        if (span.ElementFormalDefinitionOrdinal
            == BoundSpanTypeContract.ImportedFormalDefinitionOrdinal
                - formalTypeOwnerOrdinal)
        {
            var ordinal = span.ElementFormalOrdinal;
            if ((uint)ordinal >= (uint)formals.Length
                || formals[ordinal].PlaceholderType != span.ElementType)
            {
                return false;
            }
            elementTerm = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Formal,
                PrimitiveType.Void, ordinal, -1, [],
                false, false, false, false);
        }
        else if (span.ElementFormalDefinitionOrdinal == -1)
        {
            if (!TryCreateImportedGenericMethodTypeTerm(
                span.ElementType, formals, spanTypes,
                formalTypeOwnerOrdinal, depth + 1,
                out elementTerm, allowFormal: false))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        if (span.IsElementWritable
            && (elementTerm.Kind
                    != BoundGenericMethodTypeTermKind.Formal
                || !formals[elementTerm.FormalOrdinal].RequiresWritable))
        {
            return false;
        }

        term = new BoundGenericMethodTypeTerm(
            BoundGenericMethodTypeTermKind.Span,
            PrimitiveType.Void, -1, -1, [elementTerm],
            span.IsWritable, false, span.IsInitialization, false) {
            IsElementWritable = span.IsElementWritable,
        };
        return true;
    }

    private GenericTemplate? FindGenericTemplate(string name)
    {
        var currentOwner = _genericContext?.Template.OwnerName
            ?? (_currentStruct is string currentStruct
                ? Qualify(currentStruct) : null);
        if (currentOwner is not null
            && _templates.TryGetValue(currentOwner + "." + name,
                out var ownerTemplate))
        {
            return ownerTemplate;
        }

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

    private GenericTemplate? FindGenericMemberTemplate(
        PrimitiveType ownerType, string memberName)
    {
        if (ManagedClassTypes.IsClass(ownerType))
        {
            ownerType = ManagedClassTypes.Readonly(ownerType);
        }

        GenericTemplate? result = null;
        foreach (var template in _templates.Values)
        {
            if (template.OwnerType != ownerType
                || template.MemberName != memberName)
            {
                continue;
            }

            if (result is not null)
            {
                return null;
            }
            result = template;
        }

        return result;
    }

    private int InstantiateGeneric(SyntaxNode callee,
        SyntaxNode? callArguments = null, PrimitiveType? expected = null,
        GenericTemplate? selectedTemplate = null,
        ImmutableArray<PrimitiveType>? exactTypeArguments = null)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var nameElement = callee.Kind == SyntaxKind.MemberAccessExpression
            ? callee.Children[2] : callee.Children[0];
        var name = nameElement is SyntaxTokenElement nameToken
            ? nameToken.Token.Name : selectedTemplate?.Name ?? string.Empty;
        if (selectedTemplate is null
            && TryFindVariable(name, out _))
        {
            Report("TRM3011", $"'{name}' is a variable, not a function.", callee.Span);
            return -1;
        }
        var template = selectedTemplate ?? FindGenericTemplate(name);
        if (template is null)
        {
            Report("TRM3011",
                "A generic call requires a declared generic method.",
                callee.Span);
            return -1;
        }
        var argumentDiagnosticCount = DiagnosticCount;
        var callActuals = exactTypeArguments is not null
            ? EmptyGenericMethodCallArguments(template)
            : callee.Kind == SyntaxKind.GenericNameExpression
                || (callee.Kind == SyntaxKind.MemberAccessExpression
                    && callee.Children.Length >= 4)
                ? TryReadGenericMethodArguments(callee, template,
                    out var parsedActuals) ? parsedActuals : null
                : EmptyGenericMethodCallArguments(template);
        if (callActuals is null)
        {
            if (DiagnosticCount != argumentDiagnosticCount)
            {
                return -1;
            }

            var genericArguments = callee.Kind switch {
                SyntaxKind.GenericNameExpression
                    when callee.Children.ElementAtOrDefault(1)
                        is SyntaxNode nameArguments => nameArguments,
                SyntaxKind.MemberAccessExpression
                    when callee.Children.ElementAtOrDefault(3)
                        is SyntaxNode memberArguments => memberArguments,
                _ => null,
            };
            var suppliedCount = genericArguments?.Children
                .OfType<SyntaxNode>().Count() ?? 0;
            if (suppliedCount > template.FormalOrder.Length)
            {
                Report("TRM3011",
                    "A generic call supplies more arguments than the selected method formals.",
                    callee.Span);
                return -1;
            }
            Report("TRM3007",
                "Generic arguments must match the mixed formal order and kinds.",
                callee.Span);
            return -1;
        }
        ImmutableArray<PrimitiveType>? concrete = null;
        if (exactTypeArguments is ImmutableArray<PrimitiveType> exactTypes)
        {
            if (exactTypes.Length != template.TypeParameters.Length)
            {
                Report("TRM3007",
                    "A GenericCall does not provide every selected type formal.",
                    callee.Span);
                return -1;
            }

            concrete = exactTypes;
        }
        else if (callee.Kind == SyntaxKind.GenericNameExpression
            || (callee.Kind == SyntaxKind.MemberAccessExpression
                && callee.Children.Length >= 4))
        {
            if (callArguments is SyntaxNode arguments)
            {
                if (!TryInferGenericMethodConstantArgumentsFromPinnedDefaults(
                    template, arguments, expected, callActuals))
                {
                    Report("TRM3007",
                        "Typed arguments or result context establish conflicting generic constants.",
                        arguments.Span);
                    return -1;
                }
                concrete = InferGenericTypes(template, arguments,
                    expected, out var conflictingArguments,
                    callActuals.TypeArguments,
                    callActuals.RequireTypeInference,
                    callActuals.ExplicitTypeDefaults,
                    callActuals);
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
                    callActuals.TypeArguments,
                    callActuals.RequireTypeInference,
                    callActuals.ExplicitTypeDefaults,
                    callActuals);
            }
        }
        else if (callArguments is not null)
        {
            concrete = InferGenericTypes(template, callArguments, expected, out var conflicting);
            if (conflicting)
            {
                Report("TRM3007", "Typed arguments or result context establish conflicting generic types.",
                    callArguments.Span);
                return -1;
            }
        }
        if (concrete is not ImmutableArray<PrimitiveType> types)
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
        if (callArguments is SyntaxNode typedArguments
            && callActuals is not null
            && !TryInferGenericMethodConstantArgumentsFromTypedEvidence(
                template, typedArguments, types, expected, callActuals))
        {
            Report("TRM3007",
                "Typed arguments or result context establish conflicting generic constants.",
                typedArguments.Span);
            return -1;
        }
        if (!TryCompleteGenericMethodConstantArguments(template, types,
            callActuals!, callee.Span, out var constantArguments))
        {
            return -1;
        }
        for (var ordinal = 0; ordinal < types.Length; ordinal++)
        {
            if (GenericMethodInterfaceConstraintFailure(template, types,
                constantArguments, ordinal) is string interfaceFailure)
            {
                var formal = template.TypeParameters[ordinal];
                Report("TRM3005",
                    $"Type argument for formal '{formal.Name}' does not satisfy its {interfaceFailure}.",
                    callee.Span);
                return -1;
            }
        }
        var instantiatedReturnType = ResolveGenericReturnType(template,
            types, callee.Span, constantArguments);
        if (instantiatedReturnType != PrimitiveType.Bool
            && template.Parameters.Any(parameter => parameter.IsOut
                && !parameter.IsInit
                && !IsSupportedNonBooleanGenericOutputType(
                    ResolveGenericParameterType(template, parameter, types,
                        callee.Span, constantArguments))))
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
        var instantiated = GenericMethodInstantiationName(template,
            typeNames, constantArguments);
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
                ResolveGenericParameterType(template, parameter, types,
                    callee.Span, constantArguments),
                callee.Span, parameter.IsOut, parameter.IsInit) {
                DefaultSyntax = parameter.DefaultSyntax,
                DefaultExpression = parameter.DefaultExpression,
                DefaultValue = parameter.DefaultValue,
                DefaultFormalDependencies =
                    parameter.DefaultFormalDependencies,
            })
            .ToImmutableArray();
        var index = _signatures.Count;
        _functions.Add(instantiated, index);
        var signature = new Signature(instantiated, instantiatedReturnType, parameters,
            _genericProofFunctions is null ? template.Body : null,
            _genericProofFunctions is null ? template.Declaration : null,
            template.Tree, template.ContainingType, false, false,
            template.ImportedMethod is not null || !template.IsPublic,
            template.IsMutable, template.IsStatic,
            false, template.DoesNotReturn) {
            RequiresUnsafeCaller = template.RequiresUnsafeCaller,
            DependencyContracts = template.DependencyContracts,
            DeclarationContractClauses =
                template.SourceDeclarationContractClauses,
            GenericOwnerTypeArguments = template.OwnerType is null
                ? null : template.OwnerTypeArgumentMap,
        };
        if (_genericProofFunctions is not null)
        {
            var variables = ImmutableArray.CreateBuilder<BoundVariable>(
                parameters.Length + (template.IsStatic ? 0 : 1));
            if (!template.IsStatic)
            {
                if (template.OwnerType is not PrimitiveType ownerType)
                {
                    throw new InvalidOperationException(
                        "A generic instance method has no closed owner type.");
                }

                variables.Add(new BoundVariable("this", ownerType,
                    template.IsMutable, callee.Span));
            }
            variables.AddRange(parameters.Select(parameter =>
                new BoundVariable(parameter.Name, parameter.Type,
                    parameter.IsOut, parameter.Span) {
                    IsOut = parameter.IsOut,
                    IsInit = parameter.IsInit,
                }));
            var external = new BoundFunction(instantiated,
                signature.ReturnType, variables.ToImmutable(),
                parameters.Length + (template.IsStatic ? 0 : 1),
                new BoundBlock([], callee.Span), callee.Span) {
                IsExternal = true,
                IsStatic = template.IsStatic,
                DeclaringStruct = template.OwnerName,
                DeclarationContractClauses =
                    BindGenericMethodDeclarationContractClauses(
                        template, types, constantArguments, parameters,
                        signature.ReturnType, callee.Span),
                GenericMethodProofCall = template.ImportedMethod is null
                    ? new BoundGenericMethodInstantiation(
                        template.DefinitionOrdinal, types) {
                        ConstantArguments = constantArguments,
                    }
                    : null,
                ImportedGenericMethod = template.ImportedMethod,
                ImportedGenericMethodTypeArguments =
                    template.ImportedMethod is null ? [] : types,
                DoesNotReturn = template.DoesNotReturn,
                RequiresUnsafeCaller = template.RequiresUnsafeCaller,
                DependencyContracts = template.DependencyContracts,
            };
            signature = signature with { External = external };
            _genericProofFunctions.Add(external);
        }
        signature = signature with {
            Generic = new GenericInstantiation(template, types) {
                ConstantArguments = constantArguments,
            },
        };
        _signatures.Add(signature);
        BindConstEvalFunctionsFrom(index);
        return index;
    }

    private ImmutableArray<BoundDeclarationContractClause>
        BindGenericMethodDeclarationContractClauses(
            GenericTemplate template, ImmutableArray<PrimitiveType> types,
            ImmutableArray<BoundGenericMethodConstantArgument>
                constantArguments,
            ImmutableArray<Parameter> parameters, PrimitiveType returnType,
            TextSpan span)
    {
        var receiverCount = template.OwnerType is not null
            && !template.IsStatic ? 1 : 0;
        var runtimeParameterCount = parameters.Length + receiverCount;
        if (template.ImportedMethod is BoundGenericMethodImport imported)
        {
            if (types.Length != imported.Formals.Length
                || constantArguments.Length
                    != imported.Signature.ConstantFormals.Length)
            {
                throw new InvalidOperationException(
                    "An imported generic method has mismatched contract actuals.");
            }

            var substitutions = new Dictionary<PrimitiveType,
                PrimitiveType>(types.Length);
            for (var ordinal = 0; ordinal < types.Length; ordinal++)
            {
                substitutions.Add(imported.Formals[ordinal].PlaceholderType,
                    types[ordinal]);
            }
            var importedClauses =
                BoundTreeTraversal.RewriteDeclarationContractClauses(
                    imported.ProofFunction.DeclarationContractClauses,
                    type => substitutions.TryGetValue(type,
                        out var actual) ? actual : type);
            return RewriteGenericMethodContractConstantFormals(
                importedClauses, template, constantArguments,
                runtimeParameterCount, span).DeclarationContractClauses;
        }

        if (template.SourceDeclarationContractClauses.IsDefaultOrEmpty)
        {
            return [];
        }

        var variableStart = _variables.Count;
        var previousScopes = _scopes.ToArray();
        var previousGenericContext = _genericContext;
        var previousReturnType = _returnType;
        var previousCurrentStruct = _currentStruct;
        var previousHasReceiver = _hasReceiver;
        var previousGenericOwnerTypeArguments
            = _genericOwnerTypeArguments;
        var previousLocalStart = _localStart;
        try
        {
            _scopes.Clear();
            _scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
            _genericContext = new GenericInstantiation(template, types) {
                ConstantArguments = constantArguments,
            };
            _returnType = returnType;
            _currentStruct = template.ContainingType;
            _hasReceiver = receiverCount != 0;
            _genericOwnerTypeArguments = template.OwnerTypeArgumentMap.IsEmpty
                ? null : template.OwnerTypeArgumentMap;

            if (receiverCount != 0)
            {
                if (template.OwnerType is not PrimitiveType ownerType)
                {
                    throw new InvalidOperationException(
                        "A generic instance method has no closed owner type.");
                }

                _ = AddVariable("this", ownerType, template.IsMutable, span);
            }
            foreach (var parameter in parameters)
            {
                var slot = AddVariable(parameter.Name, parameter.Type,
                    parameter.IsOut, parameter.Span);
                _variables[slot] = _variables[slot] with {
                    IsOut = parameter.IsOut,
                    IsInit = parameter.IsInit,
                };
            }
            foreach (var formal in template.ConstantFormals)
            {
                if ((uint)formal.ConstantOrdinal
                    >= (uint)constantArguments.Length)
                {
                    throw new InvalidOperationException(
                        "A generic method contract has a missing constant actual.");
                }

                _ = AddVariable(formal.Name,
                    constantArguments[formal.ConstantOrdinal].CarrierType,
                    false, span);
            }

            _localStart = _variables.Count;
            var clauses = BindDeclarationContractClauses(
                template.SourceDeclarationContractClauses);
            var function = new BoundFunction(template.Name, returnType,
                [.. _variables.Skip(variableStart)],
                _variables.Count - variableStart,
                new BoundBlock([], span), span) {
                DeclarationContractClauses = clauses,
            };
            function = BoundTreeTraversal.RewriteVariables(function,
                function.Variables,
                variable => variable - variableStart);
            function = RewriteGenericMethodContractConstants(function,
                template, constantArguments, runtimeParameterCount, span,
                previousGenericContext);
            return function.DeclarationContractClauses;
        }
        finally
        {
            _scopes.Clear();
            _scopes.AddRange(previousScopes);
            if (_variables.Count > variableStart)
            {
                _variables.RemoveRange(variableStart,
                    _variables.Count - variableStart);
            }
            _genericContext = previousGenericContext;
            _returnType = previousReturnType;
            _currentStruct = previousCurrentStruct;
            _hasReceiver = previousHasReceiver;
            _genericOwnerTypeArguments = previousGenericOwnerTypeArguments;
            _localStart = previousLocalStart;
        }
    }

    private BoundFunction RewriteGenericMethodContractConstantFormals(
        ImmutableArray<BoundDeclarationContractClause> clauses,
        GenericTemplate template,
        ImmutableArray<BoundGenericMethodConstantArgument> constantArguments,
        int runtimeParameterCount, TextSpan span)
    {
        var function = new BoundFunction(template.Name, PrimitiveType.Void,
            [], 0, new BoundBlock([], span), span) {
            DeclarationContractClauses = clauses,
        };
        return RewriteGenericMethodContractConstants(function, template,
            constantArguments, runtimeParameterCount, span, _genericContext);
    }

    private BoundFunction RewriteGenericMethodContractConstants(
        BoundFunction function, GenericTemplate template,
        ImmutableArray<BoundGenericMethodConstantArgument> constantArguments,
        int runtimeParameterCount, TextSpan span,
        GenericInstantiation? callerContext)
    {
        if (template.ConstantFormals.IsEmpty)
        {
            return function;
        }

        return BoundTreeTraversal.RewriteExpressions(function, expression =>
        {
            if (expression is not BoundRead read
                || read.Variable < runtimeParameterCount)
            {
                return expression;
            }

            var ordinal = read.Variable - runtimeParameterCount;
            if ((uint)ordinal >= (uint)template.ConstantFormals.Length)
            {
                return expression;
            }

            var constantOrdinal = template.ConstantFormals[ordinal]
                .ConstantOrdinal;
            if ((uint)constantOrdinal >= (uint)constantArguments.Length)
            {
                throw new InvalidOperationException(
                    "A generic method contract constant has no actual.");
            }

            var actual = constantArguments[constantOrdinal];
            if (actual.Kind == BoundGenericMethodConstantArgumentKind.Value)
            {
                return TryCreateBoundGenericMethodConstantExpression(actual,
                    read.Span, out var constant)
                    ? constant : ErrorExpression(read.Span);
            }

            if (actual.Kind
                    == BoundGenericMethodConstantArgumentKind.Formal
                && callerContext is GenericInstantiation caller
                && actual.CallerDefinitionOrdinal
                    == caller.Template.DefinitionOrdinal
                && caller.Template.ConstantFormals.FirstOrDefault(formal =>
                    formal.ConstantOrdinal
                        == actual.CallerConstantFormalOrdinal)
                    is GenericMethodConstantFormal callerFormal)
            {
                var callerRuntimeParameterCount
                    = caller.Template.Parameters.Length
                        + (caller.Template.OwnerType is not null
                            && !caller.Template.IsStatic ? 1 : 0);
                var callerVariable = callerRuntimeParameterCount
                    + callerFormal.ConstantOrdinal;
                if ((uint)callerVariable < (uint)_variables.Count)
                {
                    return new BoundRead(callerVariable,
                        _variables[callerVariable].Type, read.Span);
                }
            }

            Report("TRM3007",
                "A generic method contract constant must forward a formal in its caller proof.",
                span);
            return ErrorExpression(read.Span);
        });
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

    private bool TryGetGenericMethodConstantValue(string name,
        out BoundGenericMethodConstantArgument argument)
    {
        argument = null!;
        if (_genericContext is not GenericInstantiation context)
        {
            return false;
        }

        var formal = context.Template.ConstantFormals.FirstOrDefault(
            candidate => candidate.Name == name);
        if (formal is not null)
        {
            if (context.ConstantArguments.IsDefault
                || (uint)formal.ConstantOrdinal
                    >= (uint)context.ConstantArguments.Length)
            {
                return false;
            }

            var actual = context.ConstantArguments[
                formal.ConstantOrdinal];
            if (actual.Kind
                != BoundGenericMethodConstantArgumentKind.Value)
            {
                return false;
            }

            argument = actual;
            return true;
        }

        var ownerDefinitionIndex
            = context.Template.OwnerGenericTypeDefinitionIndex;
        if (ownerDefinitionIndex < 0
            || ownerDefinitionIndex
                >= _sourceGenericTypeDefinitions.Count
            || context.Template.OwnerConstantArguments.IsDefault)
        {
            return false;
        }

        var ownerDefinition
            = _sourceGenericTypeDefinitions[ownerDefinitionIndex];
        var ownerFormal = ownerDefinition.ConstantFormals
            .FirstOrDefault(candidate => candidate.Name == name);
        if (ownerFormal is null
            || (uint)ownerFormal.ConstantOrdinal
                >= (uint)context.Template.OwnerConstantArguments.Length)
        {
            return false;
        }

        var ownerArgument = context.Template.OwnerConstantArguments[
            ownerFormal.ConstantOrdinal];
        if (ownerArgument.Kind
            != BoundGenericMethodConstantArgumentKind.Value)
        {
            return false;
        }

        argument = ownerArgument;
        return true;
    }

    private bool TryCreateBoundGenericMethodConstantExpression(
        BoundGenericMethodConstantArgument argument, TextSpan span,
        out BoundExpression expression)
    {
        expression = ErrorExpression(span);
        if (argument.Kind != BoundGenericMethodConstantArgumentKind.Value
            || !TryGetGenericMethodConstantCarrierType(
                argument.DomainType, out var carrierType)
            || carrierType != argument.CarrierType
            || !argument.Bits.IsCanonicalFor(carrierType))
        {
            return false;
        }

        if (argument.DomainType == carrierType)
        {
            expression = new BoundConstant(argument.Bits,
                argument.DomainType, span);
            return true;
        }

        expression = new BoundConstant(argument.Bits,
            argument.DomainType, carrierType, span);
        return true;
    }

    private static (BoundInterfaceType Interface,
        BoundInterfaceMemberDefinition Member)
        GetExpectedImportedInterfaceRequirement(
            BoundGenericMethodImport imported, int formalOrdinal,
            BoundInterfaceType providerInterface,
            BoundInterfaceMemberIdentity providerRequirement)
    {
        if (imported.Binding is not BoundGenericMethodImportBinding binding
            || (uint)formalOrdinal >= (uint)binding.Provider.Formals.Length
            || (uint)formalOrdinal >= (uint)binding.Expected.Formals.Length
            || !BoundInterfaceContract.AreEquivalentProviderInterfaceViews(
                providerInterface,
                providerRequirement.DeclaringInterface))
        {
            throw new InvalidOperationException(
                "An imported G1 interface requirement has no expected binding view.");
        }

        var providerConstraints
            = binding.Provider.Formals[formalOrdinal].InterfaceConstraints;
        var expectedConstraints
            = binding.Expected.Formals[formalOrdinal].InterfaceConstraints;
        var matchingProviderConstraints = providerConstraints
            .Where(constraint =>
                BoundInterfaceContract
                    .AreEquivalentProviderInterfaceViews(constraint,
                        providerInterface)).ToArray();
        if (matchingProviderConstraints.Length != 1)
        {
            throw new InvalidOperationException(
                "An imported G1 interface requirement has no unique provider Constraint.");
        }

        var matchingExpectedConstraints = expectedConstraints
            .Where(constraint =>
                BoundInterfaceContract
                    .AreEquivalentProviderInterfaceViews(constraint,
                        matchingProviderConstraints[0])).ToArray();
        if (matchingExpectedConstraints.Length != 1)
        {
            throw new InvalidOperationException(
                "An imported G1 interface requirement has no unique expected Constraint.");
        }

        var expectedInterface = matchingExpectedConstraints[0];
        var expectedDefinitions = binding.ExpectedInterfaceDefinitions
            .Where(definition =>
                StringComparer.Ordinal.Equals(definition.QualifiedName,
                    expectedInterface.NominalName)
                && definition.Formals.Length
                    == expectedInterface.TypeArguments.Length
                && definition.ModuleImport
                    == expectedInterface.ModuleImport).ToArray();
        if (expectedDefinitions.Length != 1)
        {
            throw new InvalidOperationException(
                "An imported G1 interface requirement has no retained expected declaration.");
        }

        var expectedMembers = expectedDefinitions[0].Members
            .Where(member => StringComparer.Ordinal.Equals(
                member.DeclarationKey,
                providerRequirement.DeclarationKey)).ToArray();
        if (expectedMembers.Length != 1
            || expectedMembers[0].Kind != BoundInterfaceMemberKind.Callable
            || providerRequirement.AccessorOrdinal != -1)
        {
            throw new InvalidOperationException(
                "An imported G1 interface requirement has no exact expected member.");
        }

        return (expectedInterface, expectedMembers[0]);
    }

    private static BoundInterfaceCallIdentity
        RemapImportedG1InterfaceCallIdentity(
            BoundGenericMethodImport imported,
            BoundInterfaceCallIdentity identity)
    {
        if (!identity.IsConstraintCall)
        {
            throw new InvalidOperationException(
                "An imported G1 Body has unsupported closed interface evidence.");
        }
        if (!BoundInterfaceContract.AreEquivalentProviderInterfaceViews(
                identity.WitnessInterface,
                identity.RequestedInterface))
        {
            throw new InvalidOperationException(
                "An imported G1 interface call has a mismatched witness view.");
        }

        var formalOrdinal = -1;
        for (var ordinal = 0; ordinal < imported.Formals.Length; ordinal++)
        {
            if (imported.Formals[ordinal].PlaceholderType
                != identity.ImplementingType)
            {
                continue;
            }

            if (formalOrdinal >= 0)
            {
                throw new InvalidOperationException(
                    "An imported G1 interface call has an ambiguous implementing formal.");
            }
            formalOrdinal = ordinal;
        }
        if (formalOrdinal < 0)
        {
            throw new InvalidOperationException(
                "An imported G1 interface call has no implementing formal.");
        }

        var (expectedInterface, expectedMember)
            = GetExpectedImportedInterfaceRequirement(imported,
                formalOrdinal, identity.RequestedInterface,
                identity.Requirement);
        return identity with {
            WitnessInterface = expectedInterface,
            RequestedInterface = expectedInterface,
            Requirement = new BoundInterfaceMemberIdentity(
                expectedInterface, expectedMember.Ordinal,
                expectedMember.DeclarationKey,
                identity.Requirement.AccessorOrdinal),
        };
    }

    private static BoundFunction RemapImportedG1InterfaceSupportFunction(
        BoundGenericMethodImport imported, BoundFunction supportFunction)
    {
        if (supportFunction.InterfaceRequirement
                is not BoundInterfaceMemberIdentity requirement
            || supportFunction.Variables.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException(
                "An imported G1 interface-call proof target has no requirement identity.");
        }

        var formalOrdinal = -1;
        for (var ordinal = 0; ordinal < imported.Formals.Length; ordinal++)
        {
            if (imported.Formals[ordinal].PlaceholderType
                != supportFunction.Variables[0].Type)
            {
                continue;
            }
            if (formalOrdinal >= 0)
            {
                throw new InvalidOperationException(
                    "An imported G1 interface-call proof target has an ambiguous implementing formal.");
            }
            formalOrdinal = ordinal;
        }
        if (formalOrdinal < 0)
        {
            throw new InvalidOperationException(
                "An imported G1 interface-call proof target has no implementing formal.");
        }

        var (expectedInterface, expectedMember)
            = GetExpectedImportedInterfaceRequirement(imported,
                formalOrdinal, requirement.DeclaringInterface,
                requirement);
        if (expectedMember.Parameters.Length
            != supportFunction.ParameterCount - 1
            || supportFunction.ParameterCount
                != supportFunction.Variables.Length)
        {
            throw new InvalidOperationException(
                "The expected G1 interface requirement changes its proof target arity.");
        }

        var variables = supportFunction.Variables.ToBuilder();
        for (var ordinal = 0;
            ordinal < expectedMember.Parameters.Length; ordinal++)
        {
            variables[ordinal + 1] = variables[ordinal + 1] with {
                Name = expectedMember.Parameters[ordinal].Name,
            };
        }

        return supportFunction with {
            Variables = variables.MoveToImmutable(),
            InterfaceRequirement = new BoundInterfaceMemberIdentity(
                expectedInterface, expectedMember.Ordinal,
                expectedMember.DeclarationKey, requirement.AccessorOrdinal),
        };
    }

    private static BoundFunction RemapImportedG1InterfaceCalls(
        BoundGenericMethodImport imported, BoundFunction function)
        => BoundTreeTraversal.RewriteExpressions(function,
            expression =>
            {
                if (expression is BoundCall {
                        InterfaceCallIdentity:
                            BoundInterfaceCallIdentity identity,
                    } call)
                {
                    return call with {
                        InterfaceCallIdentity =
                            RemapImportedG1InterfaceCallIdentity(imported,
                                identity),
                    };
                }

                return expression;
            });

    private BoundBlock BindGenericBody(GenericInstantiation instantiation)
    {
        var template = instantiation.Template;
        if (template.ImportedMethod is BoundGenericMethodImport imported)
        {
            var actuals = instantiation.Types;
            var formals = imported.Formals;
            if (actuals.Length != formals.Length)
            {
                throw new InvalidOperationException(
                    "An imported generic method has a mismatched type argument count.");
            }

            var substitutions = new Dictionary<PrimitiveType, PrimitiveType>(
                formals.Length);
            for (var ordinal = 0; ordinal < formals.Length; ordinal++)
            {
                substitutions.Add(formals[ordinal].PlaceholderType,
                    actuals[ordinal]);
            }
            var importedProofFunction = imported.ProofFunction;
            var importedSupportFunctions
                = imported.ProofSupportFunctions;
            if (imported.Binding is not null
                && !importedSupportFunctions.IsEmpty)
            {
                importedProofFunction
                    = RemapImportedG1InterfaceCalls(imported,
                        importedProofFunction);
                importedSupportFunctions = [
                    .. importedSupportFunctions.Select(supportFunction =>
                        RemapImportedG1InterfaceSupportFunction(imported,
                            supportFunction)),
                ];
            }

            var proofSupportFunctionStart = -1;
            if (!importedSupportFunctions.IsEmpty)
            {
                if (_genericProofFunctions is null)
                {
                    throw new InvalidOperationException(
                        "An imported interface-call Body has no generic proof function table.");
                }

                proofSupportFunctionStart = _genericProofFunctions.Count;
                foreach (var supportFunction
                    in importedSupportFunctions)
                {
                    var supportFunctionName = "$g1-interface-requirement."
                        + _genericProofFunctions.Count.ToString(
                            System.Globalization.CultureInfo.InvariantCulture);
                    _genericProofFunctions.Add(
                        BoundTreeTraversal.RewriteTypes(supportFunction,
                            type => substitutions.TryGetValue(type,
                                out var actual) ? actual : type) with {
                            Name = supportFunctionName,
                        });
                }
            }
            var rewritten = BoundTreeTraversal.RewriteTypes(
                importedProofFunction,
                type => substitutions.TryGetValue(type, out var actual)
                    ? actual : type);
            rewritten = BoundTreeTraversal.RewriteCalls(rewritten,
                call =>
                {
                    if (call.InterfaceCallIdentity is not null)
                    {
                        if (proofSupportFunctionStart < 0
                            || (uint)call.Function
                                >= (uint)importedSupportFunctions.Length)
                        {
                            throw new InvalidOperationException(
                                "An imported interface call has no proof support function.");
                        }

                        call = call with {
                            Function = checked(proofSupportFunctionStart
                                + call.Function),
                        };
                    }

                    if (call.ImportedGenericMethodCall
                            is not BoundGenericMethodImport targetImport
                        || call.ImportedGenericMethodTypeArguments.Length
                            != targetImport.Formals.Length)
                    {
                        return call;
                    }

                    var targetTemplate = CreateGenericTemplateFromImport(
                        targetImport, _trees[0], _spanTypes);
                    var targetFunction = InstantiateGeneric(_trees[0].Root,
                        selectedTemplate: targetTemplate,
                        exactTypeArguments:
                            call.ImportedGenericMethodTypeArguments);
                    return targetFunction < 0 ? call : call with {
                        Function = targetFunction,
                        ImportedGenericMethodCall = targetTemplate.ImportedMethod,
                    };
                });
            if (rewritten.ParameterCount != template.Parameters.Length
                || _variables.Count != rewritten.ParameterCount)
            {
                throw new InvalidOperationException(
                    "The imported generic proof Body does not match its selected signature.");
            }
            _variables.AddRange(rewritten.Variables.Skip(
                rewritten.ParameterCount));
            return rewritten.Body;
        }

        if (instantiation.ConstantArguments.IsDefaultOrEmpty
            && !template.ConstantFormals.IsEmpty)
        {
            instantiation = instantiation with {
                ConstantArguments =
                    CreateFormalGenericMethodConstantArguments(template),
            };
        }
        var previous = _genericContext;
        var previousRefinements = new Dictionary<PrimitiveType, bool>(
            _genericUniqueRefinements);
        _genericContext = instantiation;
        try
        {
            return BindCallableBody(template.Body);
        }
        finally
        {
            _genericContext = previous;
            SetGenericUniqueRefinements(previousRefinements);
        }
    }

    private bool TryBindGenericMethodParameterTypeTerms(
        GenericTemplate template, out GenericTemplate bound)
    {
        bound = template;
        var parameters = template.Parameters.ToBuilder();
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            if (parameter.TypeTermSyntax is not SyntaxNode typeSyntax)
            {
                continue;
            }

            if (BindGenericMethodTypeTerm(typeSyntax, template)
                is not BoundGenericMethodTypeTerm typeTerm)
            {
                Report("TRM3005",
                    $"Parameter '{parameter.Name}' has an unsupported generic type term.",
                    typeSyntax.Span);
                return false;
            }

            parameters[index] = parameter with {
                TypeTerm = typeTerm,
                TypeTermSyntax = null,
            };
        }

        bound = template with { Parameters = parameters.ToImmutable() };
        return true;
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
        proofBinder._currentStruct = template.ContainingType;
        proofBinder._hasReceiver = template.OwnerType is not null
            && !template.IsStatic;
        if (!template.OwnerTypeArgumentMap.IsEmpty)
        {
            proofBinder._aliasTypeArguments.Add(
                template.OwnerTypeArgumentMap);
        }

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
                proofBinder.ResolveGenericParameterType(template, parameter,
                    symbolicTypes, template.Declaration.Span),
                template.Declaration.Span, parameter.IsOut,
                parameter.IsInit)).ToImmutableArray();
        var returnType = proofBinder.ResolveGenericReturnType(template,
            symbolicTypes, template.Declaration.Span,
            CreateFormalGenericMethodConstantArguments(template));
        contracts = proofBinder.BindDependencyContracts(attributes, parameters,
            returnType, containingType: template.ContainingType,
            isStatic: template.IsStatic,
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
        if (_isValidatingGenericTemplates)
        {
            return true;
        }

        _isValidatingGenericTemplates = true;
        try
        {
            while (_templates.Values.Any(static template =>
                template.DefinitionOrdinal < 0
                    && template.ImportedMethod is null))
            {
                if (!ValidateGenericTemplateBatch())
                {
                    return false;
                }
            }

            _hasValidatedGenericTemplates = true;
            return true;
        }
        finally
        {
            _isValidatingGenericTemplates = false;
        }
    }

    private bool ValidateGenericTemplateBatch()
    {
        var templates = _templates.Values
            .Where(static template => template.DefinitionOrdinal < 0
                && template.ImportedMethod is null)
            .OrderBy(template => _trees.IndexOf(template.Tree))
            .ThenBy(static template => template.Declaration.Span.Start)
            .ToArray();
        var usedProtocolNames = _templates.Values
            .SelectMany(static template => template.TypeParameters)
            .SelectMany(static formal => formal.Operations.Values)
            .Select(static requirement => requirement.Protocol)
            .ToHashSet(StringComparer.Ordinal);
        var protocols = _protocols.Values
            .Where(protocol => usedProtocolNames.Contains(protocol.Name)
                && !_genericMethodOperatorProtocolOrdinals.ContainsKey(
                    protocol.Name))
            .OrderBy(protocol => _trees.IndexOf(protocol.Tree))
            .ThenBy(static protocol => protocol.Declaration.Span.Start)
            .ToArray();
        if (usedProtocolNames.Any(protocol =>
            !_protocols.ContainsKey(protocol)))
        {
            throw new InvalidOperationException(
                "A generic method operator requirement has no declared protocol.");
        }
        foreach (var protocol in protocols)
        {
            var protocolIndex = _genericMethodOperatorProtocols.Count;
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
                DefinitionOrdinal = _genericMethodSignatures.Count,
            };
            if (template.ReturnTypeTermSyntax is SyntaxNode returnSyntax)
            {
                if (BindGenericMethodTypeTerm(returnSyntax, template)
                    is not BoundGenericMethodTypeTerm returnTypeTerm)
                {
                    return false;
                }

                template = template with {
                    ReturnTypeTerm = returnTypeTerm,
                };
            }
            if (!TryBindGenericMethodParameterTypeTerms(template,
                    out var boundTemplate))
            {
                return false;
            }
            template = boundTemplate;
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
                new BoundGenericMethodSignature(
                    template.DefinitionOrdinal, template.Name,
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
                            InterfaceConstraints =
                                formal.InterfaceConstraints,
                        })]) {
                    IsStatic = template.IsStatic,
                    IsMutable = template.IsMutable,
                    PureEvaluationFormSchema =
                        template.PureEvaluationFormSchema,
                    ConstEvalSchema = template.ConstEvalSchema,
                    Owner = template.OwnerType is PrimitiveType ownerType
                        ? new BoundGenericMethodOwner(ownerType,
                            template.OwnerGenericTypeDefinitionIndex,
                            template.OwnerTypeArguments) {
                            ConstantArguments =
                                template.OwnerConstantArguments,
                        }
                        : null,
                    ConstantFormals = [
                        .. template.ConstantFormals.Select(formal =>
                            new BoundGenericMethodConstantFormalSignature(
                                formal.Ordinal, formal.ConstantOrdinal,
                                formal.Name, formal.Domain,
                                formal.DefaultSyntax is not null,
                                formal.DefaultFormalDependencies)),
                    ],
                    FormalOrder = template.FormalOrder,
                });
        }

        BindGenericMethodParameterDefaultExpressions();
        if (DiagnosticCount != 0)
        {
            return false;
        }
        for (var index = 0; index < templates.Length; index++)
        {
            if (!_templates.TryGetValue(templates[index].Name,
                    out var updatedTemplate))
            {
                return false;
            }
            templates[index] = updatedTemplate;
        }

        var hasGenericMethodOwner = _genericMethodSignatures.Any(
            static signature => signature.Owner is not null);
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
            CoreEvaluationFormIdentitySelection? proofEvaluationFormSelection
                = null;
            ImmutableArray<CoreEvaluationFormIdentitySelection>
                proofEvaluationFormSelections = [];
            var pureBinding = template.PureEvaluationFormSchema;
            var constEvalBinding = template.ConstEvalSchema;
            if (pureBinding is not null || constEvalBinding is not null)
            {
                if (_coreEvaluationFormIdentitySelection
                    is not CoreEvaluationFormIdentitySelection
                        selectedEvaluationForm)
                {
                    throw new InvalidOperationException(
                        "A selected generic evaluation form has no host identity selection.");
                }

                proofEvaluationFormSelection =
                    selectedEvaluationForm with {
                        PureSchema = pureBinding?.Selector,
                        ConstEvalSchema = constEvalBinding?.Selector,
                    };
                proofEvaluationFormSelections =
                    [proofEvaluationFormSelection];
            }
            var proofBinder = new Binder([template.Tree], [], _cancellationToken,
                isLibrary: true, runtimePolicy: _runtimePolicy,
                sourceArithmeticProfile: _sourceArithmeticProfile,
                nativeCoreRegistration: _nativeCoreRegistration,
                layoutCoreRegistration: _layoutCoreRegistration,
                coreExceptionIdentitySelections:
                    _coreExceptionIdentitySelections,
                coreContractFailureIdentitySelection:
                    _coreContractFailureIdentitySelection,
                coreContractFailureIdentitySelections:
                    _coreContractFailureIdentitySelections,
                coreEvaluationFormIdentitySelection:
                    proofEvaluationFormSelection,
                coreEvaluationFormIdentitySelections:
                    proofEvaluationFormSelections,
                coreEvaluationFormSchemaProviders:
                    _coreEvaluationFormSchemaProviders);
            proofBinder._structs.AddRange(_structs);
            foreach (var (name, type) in _structNames)
            {
                proofBinder._structNames.Add(name, type);
            }
            proofBinder._classes.AddRange(_classes);
            foreach (var (name, type) in _classNames)
            {
                proofBinder._classNames.Add(name, type);
            }
            proofBinder._sourceGenericTypeInstances.AddRange(
                _sourceGenericTypeInstances);
            proofBinder._genericNominalTypeInstances.AddRange(
                _genericNominalTypeInstances);
            foreach (var (key, type) in _genericNominalTypeUses)
            {
                proofBinder._genericNominalTypeUses.Add(key, type);
            }
            proofBinder._checkedReferenceTypes.AddRange(
                _checkedReferenceTypes);
            proofBinder._nativeFunctionPointerSignatures.AddRange(
                _nativeFunctionPointerSignatures);
            proofBinder._nativePointerTypes.AddRange(
                _nativePointerTypes);
            proofBinder._nativeFixedArrayTypes.AddRange(
                _nativeFixedArrayTypes);
            if (hasGenericMethodOwner)
            {
                foreach (var (name, witnesses) in _conformances)
                {
                    proofBinder._conformances.Add(name,
                        new Dictionary<string, PrimitiveType>(witnesses,
                            StringComparer.Ordinal));
                }
            }
            proofBinder._enumDefinitions.AddRange(_enumDefinitions);
            proofBinder._genericMethodSignatures.AddRange(
                _genericMethodSignatures);
            proofBinder._sourceGenericTypeDefinitions.AddRange(
                _sourceGenericTypeDefinitions);
            proofBinder._genericNominalTypeDefinitions.AddRange(
                _genericNominalTypeDefinitions);
            foreach (var (genericName, genericStructTemplate)
                in _genericStructTemplates)
            {
                proofBinder._genericStructTemplates.Add(genericName,
                    genericStructTemplate);
            }
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
            proofBinder._interfaces.AddRange(_interfaces);
            foreach (var (key, declarations) in _interfaceDeclarationGroups)
            {
                proofBinder._interfaceDeclarationGroups.Add(key,
                    [.. declarations]);
            }
            proofBinder.SetContext(template.Tree, template.Declaration);
            proofBinder._genericProofSource = this;
            var proofOwnerType = template.OwnerType;
            var proofOwnerTypeArguments = template.OwnerTypeArguments;
            var proofOwnerTypeMap = ImmutableDictionary
                .CreateBuilder<string, PrimitiveType>(
                    StringComparer.Ordinal);
            foreach (var (formalName, ownerArgument)
                in template.OwnerTypeArgumentMap)
            {
                if (proofBinder.HydrateProofType(ownerArgument,
                    allowFloatingScalar: true,
                    allowFloatingStructFields: true,
                    allowUnique: true)
                    is not PrimitiveType proofOwnerArgument)
                {
                    Report("TRM3007",
                        "A generic method owner argument cannot hydrate in the proof context.",
                        template.Declaration.Span);
                    return false;
                }
                proofOwnerTypeMap.Add(formalName, proofOwnerArgument);
            }
            if (template.OwnerType is PrimitiveType sourceOwnerType)
            {
                if (proofBinder.HydrateProofType(sourceOwnerType,
                    allowFloatingScalar: true,
                    allowFloatingStructFields: true,
                    allowUnique: true)
                    is not PrimitiveType hydratedOwnerType)
                {
                    Report("TRM3007",
                        "A generic method owner cannot hydrate in the proof context.",
                        template.Declaration.Span);
                    return false;
                }

                proofOwnerType = hydratedOwnerType;
                var ownerArguments = ImmutableArray.CreateBuilder<
                    PrimitiveType>(template.OwnerTypeArguments.Length);
                foreach (var ownerArgument in template.OwnerTypeArguments)
                {
                    if (proofBinder.HydrateProofType(ownerArgument,
                        allowFloatingScalar: true,
                        allowFloatingStructFields: true,
                        allowUnique: true)
                        is not PrimitiveType proofOwnerArgument)
                    {
                        Report("TRM3007",
                            "A generic method owner argument cannot hydrate in the proof context.",
                            template.Declaration.Span);
                        return false;
                    }
                    ownerArguments.Add(proofOwnerArgument);
                }
                proofOwnerTypeArguments = ownerArguments.MoveToImmutable();
            }
            var proofOwnerMap = proofOwnerTypeMap.ToImmutable();
            var proofScalarReturnType = template.ScalarReturnType;
            if (proofScalarReturnType is PrimitiveType sourceReturnType
                && proofBinder.HydrateProofType(sourceReturnType,
                    allowFloatingScalar: true,
                    allowFloatingStructFields: true,
                    allowUnique: true)
                    is PrimitiveType proofReturnType)
            {
                proofScalarReturnType = proofReturnType;
            }
            else if (proofScalarReturnType is not null)
            {
                Report("TRM3007",
                    "A generic method result type cannot hydrate in the proof context.",
                    template.Declaration.Span);
                return false;
            }

            var proofParameters = ImmutableArray.CreateBuilder<
                GenericParameter>(template.Parameters.Length);
            foreach (var parameter in template.Parameters)
            {
                var scalarType = parameter.ScalarType;
                if (scalarType is PrimitiveType sourceParameterType)
                {
                    if (proofBinder.HydrateProofType(sourceParameterType,
                        allowFloatingScalar: true,
                        allowFloatingStructFields: true,
                        allowUnique: true)
                        is not PrimitiveType proofParameterType)
                    {
                        Report("TRM3007",
                            "A generic method parameter type cannot hydrate in the proof context.",
                            template.Declaration.Span);
                        return false;
                    }
                    scalarType = proofParameterType;
                }

                proofParameters.Add(parameter with {
                    ScalarType = scalarType,
                });
            }

            var proofTypeParameters = ImmutableArray.CreateBuilder<
                GenericTypeParameter>(template.TypeParameters.Length);
            foreach (var formal in template.TypeParameters)
            {
                var operations = ImmutableDictionary.CreateBuilder<
                    StaticOperatorKey, GenericOperatorConstraint>();
                foreach (var (key, constraint) in formal.Operations)
                {
                    var fixedResult = constraint.FixedResult;
                    if (fixedResult is PrimitiveType sourceFixedResult)
                    {
                        if (proofBinder.HydrateProofType(
                            sourceFixedResult,
                            allowFloatingScalar: true,
                            allowFloatingStructFields: true,
                            allowUnique: true)
                            is not PrimitiveType proofFixedResult)
                        {
                            Report("TRM3007",
                                "A generic method operator result type cannot hydrate in the proof context.",
                                template.Declaration.Span);
                            return false;
                        }
                        fixedResult = proofFixedResult;
                    }
                    operations.Add(key, constraint with {
                        FixedResult = fixedResult,
                    });
                }

                proofTypeParameters.Add(formal with {
                    Operations = operations.ToImmutable(),
                });
            }

            var proofOwnerConstants = ImmutableArray.CreateBuilder<
                BoundGenericMethodConstantArgument>(
                    template.OwnerConstantArguments.Length);
            foreach (var ownerConstant in template.OwnerConstantArguments)
            {
                if (!TryHydrateGenericMethodConstantArgument(proofBinder,
                    ownerConstant, out var proofOwnerConstant))
                {
                    Report("TRM3007",
                        "A generic method owner constant cannot hydrate in the proof context.",
                        template.Declaration.Span);
                    return false;
                }
                proofOwnerConstants.Add(proofOwnerConstant);
            }

            var proofTemplate = template with {
                TypeParameters = proofTypeParameters.MoveToImmutable(),
                ScalarReturnType = proofScalarReturnType,
                Parameters = proofParameters.MoveToImmutable(),
                OwnerType = proofOwnerType,
                OwnerTypeArguments = proofOwnerTypeArguments,
                OwnerConstantArguments =
                    proofOwnerConstants.MoveToImmutable(),
                OwnerTypeArgumentMap = proofOwnerMap,
            };
            var receiverCount = proofTemplate.OwnerType is not null
                && !proofTemplate.IsStatic ? 1 : 0;
            var runtimeParameterCount = proofTemplate.Parameters.Length
                + receiverCount;
            proofBinder._currentStruct = proofTemplate.ContainingType;
            proofBinder._hasReceiver = proofTemplate.OwnerType is not null
                && !proofTemplate.IsStatic;
            if (!proofOwnerMap.IsEmpty)
            {
                proofBinder._aliasTypeArguments.Add(proofOwnerMap);
            }

            var functions = ImmutableArray.CreateBuilder<BoundFunction>();
            var probeTypes = ImmutableArray.CreateBuilder<PrimitiveType>(template.TypeParameters.Length);
            proofBinder._genericProofFunctions = functions;
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
                var probeStructureIndex = proofBinder._structs.Count;
                var probeType = (PrimitiveType)(100 + probeStructureIndex);
                probeTypes.Add(probeType);
                proofBinder._structs.Add(new BoundStruct(probeName,
                    [new BoundStructField("$opaque", PrimitiveType.Int32, 0, 4) { IsPrivate = true }], 4) {
                    IsInternal = true,
                    IsUnique = template.TypeParameters[parameterIndex]
                        .OwnershipDomain is 2 or 3,
                    Alignment = sizeof(int),
                    RequiredAlignment = 1,
                });
                _ = proofBinder._laidOut.Add(probeStructureIndex);
            }

            var symbolicTypes = probeTypes.MoveToImmutable();
            var proofConstantFormals = ImmutableArray.CreateBuilder<
                GenericMethodConstantFormal>(
                    template.ConstantFormals.Length);
            var proofDomainTypes = new List<(
                BoundGenericMethodTypeTerm Domain,
                PrimitiveType Type)>();
            foreach (var formal in template.ConstantFormals)
            {
                PrimitiveType? proofType = null;
                if (formal.Domain.Kind
                    == BoundGenericMethodTypeTermKind.Concrete)
                {
                    proofType = proofBinder.HydrateProofType(
                        formal.Domain.ConcreteType);
                }
                else if (formal.Domain.Kind
                    == BoundGenericMethodTypeTermKind.SourceGeneric)
                {
                    foreach (var candidate in proofDomainTypes)
                    {
                        if (AreGenericMethodTypeTermsEquivalent(
                            candidate.Domain, formal.Domain))
                        {
                            proofType = candidate.Type;
                            break;
                        }
                    }
                    if (proofType is null)
                    {
                        var proofTypeArguments =
                            ImmutableArray.CreateBuilder<
                                BoundGenericMethodTypeTerm>(
                                    symbolicTypes.Length);
                        foreach (var symbolicType in symbolicTypes)
                        {
                            proofTypeArguments.Add(
                                new BoundGenericMethodTypeTerm(
                                    BoundGenericMethodTypeTermKind.Concrete,
                                    symbolicType, -1, -1, [], false, false,
                                    false, false));
                        }

                        if (BoundGenericMethodTypeTerms.TrySubstitute(
                            formal.Domain,
                            proofTypeArguments.MoveToImmutable(),
                            out var proofDomain))
                        {
                            var previousGenericContext
                                = proofBinder._genericContext;
                            try
                            {
                                proofBinder._genericContext =
                                    new GenericInstantiation(proofTemplate,
                                        symbolicTypes) {
                                        ConstantArguments =
                                            CreateFormalGenericMethodConstantArguments(
                                                proofTemplate),
                                    };
                                if (proofBinder
                                        .TryResolveGenericMethodTypeTerm(
                                            proofDomain,
                                            template.Declaration.Span,
                                            out var sourceProofType)
                                    && proofBinder
                                        .TryGetGenericMethodConstantCarrierType(
                                            sourceProofType, out _))
                                {
                                    proofType = sourceProofType;
                                }
                            }
                            finally
                            {
                                proofBinder._genericContext =
                                    previousGenericContext;
                            }
                        }
                    }
                    proofType ??= CreateGenericConstantFormalPlaceholder(
                        proofBinder, formal.ConstantOrdinal);
                }
                else if (formal.Domain.Kind
                    == BoundGenericMethodTypeTermKind.ImportedGeneric)
                {
                    var proofTypeArguments = ImmutableArray.CreateBuilder<
                        BoundGenericMethodTypeTerm>(symbolicTypes.Length);
                    foreach (var symbolicType in symbolicTypes)
                    {
                        proofTypeArguments.Add(
                            new BoundGenericMethodTypeTerm(
                                BoundGenericMethodTypeTermKind.Concrete,
                                symbolicType, -1, -1, [], false, false,
                                false, false));
                    }

                    if (BoundGenericMethodTypeTerms.TrySubstitute(
                        formal.Domain, proofTypeArguments.MoveToImmutable(),
                        out var proofDomain))
                    {
                        var previousGenericContext
                            = proofBinder._genericContext;
                        try
                        {
                            proofBinder._genericContext =
                                new GenericInstantiation(proofTemplate,
                                    symbolicTypes) {
                                    ConstantArguments =
                                        CreateFormalGenericMethodConstantArguments(
                                            proofTemplate),
                                };
                            if (proofBinder.TryResolveGenericMethodTypeTerm(
                                    proofDomain, template.Declaration.Span,
                                    out var importedProofType)
                                && proofBinder
                                    .TryGetGenericMethodConstantCarrierType(
                                        importedProofType, out _))
                            {
                                proofType = importedProofType;
                            }
                        }
                        finally
                        {
                            proofBinder._genericContext =
                                previousGenericContext;
                        }
                    }
                }

                if (proofType is not PrimitiveType hydratedType)
                {
                    Report("TRM3007",
                        "A constant generic domain must hydrate to a complete proof type.",
                        template.Declaration.Span);
                    return false;
                }

                proofDomainTypes.Add((formal.Domain, hydratedType));
                proofConstantFormals.Add(formal with {
                    PlaceholderType = hydratedType,
                    ProofParameterIndex = runtimeParameterCount
                        + formal.ConstantOrdinal,
                });
            }
            proofTemplate = proofTemplate with {
                ConstantFormals = proofConstantFormals.MoveToImmutable(),
            };
            if (!TryHydrateGenericMethodSignatures(proofBinder,
                [.. _genericMethodSignatures],
                out var proofGenericMethodSignatures))
            {
                Report("TRM3007",
                    "A constant generic domain must hydrate in the generic proof context.",
                    template.Declaration.Span);
                return false;
            }
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
                var constantFormalDependencies =
                    defaultTypeTerm is BoundGenericMethodTypeTerm constantTerm
                        ? GenericMethodTypeTermConstantDependencies(
                            constantTerm) : [];
                var formalOrderOrdinal = template.FormalOrder
                    .FirstOrDefault(slot =>
                        slot.Kind == BoundGenericMethodFormalKind.Type
                        && slot.TypeFormalOrdinal == formalIndex)?.Ordinal
                    ?? -1;
                if (defaultTypeTerm is null
                    || !formalDependencies.SequenceEqual(
                        formal.DefaultTypeParameterDependencies)
                    || formalOrderOrdinal < 0
                    || constantFormalDependencies.Any(dependency =>
                        (uint)dependency
                            >= (uint)template.ConstantFormals.Length
                        || template.ConstantFormals[dependency].Ordinal
                            >= formalOrderOrdinal)
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
                    && !ContainsOpenGenericTypeTerm(defaultTypeTerm))
                {
                    if (!TryHydrateGenericMethodTypeTerm(proofBinder,
                            defaultTypeTerm,
                            out var proofDefaultTypeTerm)
                        || proofBinder.BindGenericMethodTypeDefault(
                            proofTemplate, formalIndex,
                            symbolicTypes[..formalIndex],
                            new BoundGenericMethodTypeDefault(
                                proofDefaultTypeTerm,
                                formalDependencies))
                            is not PrimitiveType)
                    {
                        Report("TRM3007",
                            "A generic type default does not bind to a supported complete value type.",
                            formal.TypeDefaultSyntax.Span);
                        return false;
                    }
                }

                typeDefaults[formalIndex]
                    = new BoundGenericMethodTypeDefault(defaultTypeTerm,
                        formalDependencies) {
                        ConstantFormalDependencies =
                            constantFormalDependencies,
                    };
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
            proofBinder._templates[proofTemplate.Name] = proofTemplate;
            var symbolicName = template.Name + "<" + string.Join(", ",
                symbolicTypes.Select(type => proofBinder._structs[(int)type - 100].Name)) + ">";
            var symbolicParameters = proofTemplate.Parameters.Select(parameter =>
                new Parameter(parameter.Name,
                    proofBinder.ResolveGenericParameterType(proofTemplate,
                        parameter, symbolicTypes, template.Declaration.Span),
                    template.Declaration.Span, parameter.IsOut,
                    parameter.IsInit)).ToImmutableArray();
            var proofOwnerConstantVariables
                = ImmutableArray.CreateBuilder<BoundVariable>();
            if (proofTemplate.OwnerGenericTypeDefinitionIndex >= 0)
            {
                var ownerDefinitionIndex
                    = proofTemplate.OwnerGenericTypeDefinitionIndex;
                if ((uint)ownerDefinitionIndex
                        >= (uint)proofBinder
                            ._sourceGenericTypeDefinitions.Count)
                {
                    Report("TRM3007",
                        "A generic method owner has no proof source-generic definition.",
                        template.Declaration.Span);
                    return false;
                }

                var ownerDefinition = proofBinder
                    ._sourceGenericTypeDefinitions[ownerDefinitionIndex];
                if (ownerDefinition.ConstantFormals.Length
                    != proofTemplate.OwnerConstantArguments.Length)
                {
                    Report("TRM3007",
                        "A generic method owner has an incomplete proof constant environment.",
                        template.Declaration.Span);
                    return false;
                }

                var previousGenericContext = proofBinder._genericContext;
                var previousOwnerTypeArguments
                    = proofBinder._genericOwnerTypeArguments;
                try
                {
                    proofBinder._genericContext = new GenericInstantiation(
                        proofTemplate, symbolicTypes) {
                        ConstantArguments =
                            CreateFormalGenericMethodConstantArguments(
                                proofTemplate),
                    };
                    proofBinder._genericOwnerTypeArguments
                        = proofTemplate.OwnerTypeArgumentMap;
                    foreach (var ownerFormal in ownerDefinition
                        .ConstantFormals)
                    {
                        var ownerArgument
                            = proofTemplate.OwnerConstantArguments[
                                ownerFormal.ConstantOrdinal];
                        if (ownerArgument.Kind
                            == BoundGenericMethodConstantArgumentKind.Value)
                        {
                            continue;
                        }

                        if (ownerArgument.Kind
                                != BoundGenericMethodConstantArgumentKind.Formal
                            || !IsValidGenericMethodConstantFormalOwnerIdentity(
                                ownerArgument))
                        {
                            Report("TRM3007",
                                "A generic method owner constant has an unsupported proof identity.",
                                template.Declaration.Span);
                            return false;
                        }

                        if (proofTemplate.ConstantFormals.Any(formal =>
                            formal.Name == ownerFormal.Name))
                        {
                            continue;
                        }

                        if (!proofBinder.TryResolveGenericMethodTypeTerm(
                                ownerArgument.Domain,
                                template.Declaration.Span,
                                out var ownerFormalType)
                            || !proofBinder
                                .TryGetGenericMethodConstantCarrierType(
                                    ownerFormalType, out _))
                        {
                            Report("TRM3007",
                                "A generic method owner constant domain cannot hydrate in the proof context.",
                                template.Declaration.Span);
                            return false;
                        }

                        proofOwnerConstantVariables.Add(new BoundVariable(
                            ownerFormal.Name, ownerFormalType, false,
                            template.Declaration.Span));
                    }
                }
                finally
                {
                    proofBinder._genericContext = previousGenericContext;
                    proofBinder._genericOwnerTypeArguments
                        = previousOwnerTypeArguments;
                }
            }
            var symbolicVariablesBuilder = ImmutableArray.CreateBuilder<
                BoundVariable>(symbolicParameters.Length
                    + receiverCount
                    + proofTemplate.ConstantFormals.Length
                    + proofOwnerConstantVariables.Count);
            if (receiverCount != 0)
            {
                var receiverType = proofTemplate.OwnerType!.Value;
                var receiverIsMutable = proofTemplate.IsMutable;
                if (proofBinder.IsClass(receiverType)
                    && proofBinder._classes[
                        ManagedClassTypes.ClassIndex(receiverType)]
                        .IsMutable
                    && proofTemplate.IsMutable)
                {
                    receiverType = ManagedClassTypes.Writable(receiverType);
                }
                else if (proofBinder.IsClass(receiverType))
                {
                    receiverIsMutable = false;
                }

                symbolicVariablesBuilder.Add(new BoundVariable("this",
                    receiverType, receiverIsMutable,
                    template.Declaration.Span));
            }
            foreach (var parameter in symbolicParameters)
            {
                symbolicVariablesBuilder.Add(new BoundVariable(
                    parameter.Name, parameter.Type, parameter.IsOut,
                    parameter.Span) {
                    IsOut = parameter.IsOut,
                    IsInit = parameter.IsInit,
                });
            }
            foreach (var formal in proofTemplate.ConstantFormals)
            {
                if (formal.PlaceholderType is not PrimitiveType constantType)
                {
                    throw new InvalidOperationException(
                        "A generic constant formal has no proof type.");
                }

                symbolicVariablesBuilder.Add(new BoundVariable(formal.Name,
                    constantType, false, template.Declaration.Span));
            }
            symbolicVariablesBuilder.AddRange(
                proofOwnerConstantVariables);
            var symbolicVariables = symbolicVariablesBuilder
                .MoveToImmutable();
            var symbolicReturn = proofBinder.ResolveGenericReturnType(
                proofTemplate, symbolicTypes, template.Declaration.Span,
                CreateFormalGenericMethodConstantArguments(proofTemplate));
            while (functions.Count < proofBinder._signatures.Count)
            {
                var missingSignature = proofBinder._signatures[
                    functions.Count];
                BoundFunction proofFunction;
                if (missingSignature.External is BoundFunction external)
                {
                    proofFunction = external;
                }
                else if (missingSignature.Body is not null)
                {
                    proofFunction =
                        proofBinder.BindSignatureBodyPreservingContext(
                            missingSignature);
                }
                else
                {
                    Report("TRM3007",
                        "A generic proof type dependency has no source body or checked external contract.",
                        template.Declaration.Span);
                    return false;
                }
                functions.Add(proofFunction);
            }

            var symbolicIndex = proofBinder._signatures.Count;
            proofBinder._functions.Add(symbolicName, symbolicIndex);
            var symbolicExternal = new BoundFunction(symbolicName,
                symbolicReturn, symbolicVariables,
                runtimeParameterCount
                    + proofTemplate.ConstantFormals.Length
                    + proofOwnerConstantVariables.Count,
                new BoundBlock([], template.Declaration.Span),
                template.Declaration.Span) {
                IsExternal = true,
                IsStatic = proofTemplate.IsStatic,
                DeclaringStruct = proofTemplate.OwnerName,
                DoesNotReturn = template.DoesNotReturn,
                GenericMethodProofDefinitionOrdinal =
                    template.DefinitionOrdinal,
                GenericMethodProofTypeFormalOwnerOrdinal =
                    template.FormalTypeOwnerOrdinal,
                GenericMethodProofTypeParameters = symbolicTypes,
                RequiresUnsafeCaller = template.RequiresUnsafeCaller,
                DependencyContracts = template.DependencyContracts,
            };
            proofBinder._signatures.Add(new Signature(symbolicName, symbolicReturn, symbolicParameters,
                null, null, template.Tree, proofTemplate.ContainingType,
                false, false, !proofTemplate.IsPublic,
                proofTemplate.IsMutable, proofTemplate.IsStatic,
                false, false, symbolicExternal) {
                GenericOwnerTypeArguments = proofTemplate.OwnerType is null
                    ? null : proofTemplate.OwnerTypeArgumentMap,
                DependencyContracts = template.DependencyContracts,
            });
            functions.Add(proofBinder._signatures[symbolicIndex].External
                ?? throw new InvalidOperationException("Missing generic proof signature."));

            if (!TryBindGenericMethodConstantDefaults(proofBinder,
                proofTemplate, symbolicTypes, functions,
                out var constantDefaults))
            {
                return false;
            }

            proofBinder._scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
            proofBinder._returnType = symbolicReturn;
            if (receiverCount != 0)
            {
                var receiverType = proofTemplate.OwnerType!.Value;
                var receiverIsMutable = proofTemplate.IsMutable;
                if (proofBinder.IsClass(receiverType)
                    && proofBinder._classes[
                        ManagedClassTypes.ClassIndex(receiverType)]
                        .IsMutable
                    && proofTemplate.IsMutable)
                {
                    receiverType = ManagedClassTypes.Writable(receiverType);
                }
                else if (proofBinder.IsClass(receiverType))
                {
                    receiverIsMutable = false;
                }

                _ = proofBinder.AddVariable("this", receiverType,
                    receiverIsMutable, template.Declaration.Span);
            }
            foreach (var parameter in proofTemplate.Parameters)
            {
                var slot = proofBinder.AddVariable(parameter.Name,
                    proofBinder.ResolveGenericParameterType(proofTemplate,
                        parameter, symbolicTypes, template.Declaration.Span),
                    parameter.IsOut, template.Declaration.Span);
                proofBinder._variables[slot] = proofBinder._variables[slot] with {
                    IsOut = parameter.IsOut,
                    IsInit = parameter.IsInit,
                };
            }
            foreach (var formal in proofTemplate.ConstantFormals)
            {
                if (formal.PlaceholderType is not PrimitiveType constantType)
                {
                    throw new InvalidOperationException(
                        "A generic constant formal has no proof type.");
                }

                _ = proofBinder.AddVariable(formal.Name, constantType,
                    false, template.Declaration.Span);
            }
            foreach (var ownerVariable in proofOwnerConstantVariables)
            {
                _ = proofBinder.AddVariable(ownerVariable.Name,
                    ownerVariable.Type, false, ownerVariable.Span);
            }
            proofBinder._localStart = proofBinder._variables.Count;
            var body = proofBinder.BindGenericBody(
                new GenericInstantiation(proofTemplate, symbolicTypes) {
                    ConstantArguments =
                        CreateFormalGenericMethodConstantArguments(
                            proofTemplate),
                });
            if (template.TypeParameters.Any(static formal =>
                    formal.OwnershipDomain == 3)
                && FindUnrefinedGenericUniqueMove(body, template,
                    symbolicTypes) is BoundMove unrefinedMove)
            {
                Report("TRM3007",
                    "A move from a unique? formal requires the matching true type-property refinement.",
                    unrefinedMove.Span);
                return false;
            }
            if (symbolicReturn == PrimitiveType.Void)
            {
                if (!template.DoesNotReturn)
                {
                    var end = new TextSpan(template.Body.Span.End, 0);
                    body = body with { Statements = body.Statements.Add(new BoundReturn(null, end)) };
                }
            }
            for (var parameterIndex = proofTemplate.Parameters.Length - 1;
                parameterIndex >= 0; parameterIndex--)
            {
                var parameter = proofTemplate.Parameters[parameterIndex];
                var parameterType = proofBinder.ResolveGenericParameterType(
                    proofTemplate, parameter, symbolicTypes,
                    template.Declaration.Span);
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
                            new BoundDrop(receiverCount + parameterIndex,
                                cleanupFunction,
                                template.Declaration.Span) {
                                IsOutput = parameter.IsOut,
                            },
                        ], template.Declaration.Span),
                        template.Declaration.Span),
                ], template.Declaration.Span);
            }
            var previousDeclarationContractContext
                = proofBinder._genericContext;
            ImmutableArray<BoundDeclarationContractClause>
                declarationContractClauses;
            try
            {
                proofBinder._genericContext =
                    new GenericInstantiation(proofTemplate, symbolicTypes) {
                    ConstantArguments =
                        CreateFormalGenericMethodConstantArguments(
                            proofTemplate),
                };
                declarationContractClauses =
                    proofBinder.BindDeclarationContractClauses(
                        proofTemplate.SourceDeclarationContractClauses);
            }
            finally
            {
                proofBinder._genericContext
                    = previousDeclarationContractContext;
            }
            if (proofBinder.DiagnosticCount != 0)
            {
                foreach (var diagnostic in proofBinder.Diagnostics)
                {
                    AddDiagnostic(diagnostic, template.Tree);
                }
                Report("TRM3007", "Generic function requires a body valid using only its declared type constraints.",
                    template.Declaration.Span);
                return false;
            }

            var function = new BoundFunction(symbolicName, symbolicReturn,
                [.. proofBinder._variables],
                runtimeParameterCount + template.ConstantFormals.Length
                    + proofOwnerConstantVariables.Count,
                body, template.Declaration.Span) {
                GenericMethodProofDefinitionOrdinal =
                    template.DefinitionOrdinal,
                    GenericMethodProofTypeFormalOwnerOrdinal =
                        template.FormalTypeOwnerOrdinal,
                IsStatic = proofTemplate.IsStatic,
                DeclaringStruct = proofTemplate.OwnerName,
                GenericMethodProofTypeParameters = symbolicTypes,
                DoesNotReturn = proofTemplate.DoesNotReturn,
                RequiresUnsafeCaller = template.RequiresUnsafeCaller,
                DependencyContracts = template.DependencyContracts,
                DeclarationContractClauses =
                    declarationContractClauses,
                PureEvaluationFormSchema =
                    proofTemplate.PureEvaluationFormSchema,
                ConstEvalSchema = proofTemplate.ConstEvalSchema,
            };
            functions[symbolicIndex] = function;
            var proofInterfaces = ImmutableArray.CreateBuilder<
                BoundInterfaceDefinition>(_interfaces.Count);
            var proofInterfacesHydrated = true;
            foreach (var interfaceDefinition in _interfaces)
            {
                proofInterfaces.Add(BoundTreeTraversal.RewriteTypes(
                    interfaceDefinition, type =>
                    {
                        if (proofBinder.HydrateProofType(type,
                            allowFloatingScalar: true,
                            allowFloatingStructFields: true,
                            allowUnique: true)
                            is PrimitiveType proofType)
                        {
                            return proofType;
                        }
                        proofInterfacesHydrated = false;
                        return type;
                    }));
            }
            if (!proofInterfacesHydrated)
            {
                Report("TRM3007",
                    "A generic method interface constraint cannot hydrate in the proof context.",
                    template.Declaration.Span);
                return false;
            }
            var proofFormalOwnersBuilder =
                ImmutableArray.CreateBuilder<BoundGenericTypeFormalOwner>(
                    proofBinder._referenceGenericMethodTypeFormalOwners
                        .Length
                    + proofBinder._genericMethodDefinitions.Count + 1);
            proofFormalOwnersBuilder.AddRange(
                proofBinder._referenceGenericMethodTypeFormalOwners);
            foreach (var proofDefinition in
                proofBinder._genericMethodDefinitions)
            {
                proofFormalOwnersBuilder.Add(
                    CreateGenericMethodTypeFormalOwner(
                        proofDefinition.FormalTypeOwnerOrdinal,
                        proofDefinition.Formals));
            }
            proofFormalOwnersBuilder.Add(new BoundGenericTypeFormalOwner(
                template.FormalTypeOwnerOrdinal,
                [.. template.TypeParameters.Select((formal, ordinal) =>
                    new BoundGenericTypeFormal(ordinal,
                        symbolicTypes[ordinal], formal.OwnershipDomain,
                        formal.NullabilityDomain) {
                        RequiresWritable = formal.RequiresWritable,
                        RequiresBlittable = formal.RequiresBlittable,
                    })]));
            var proofFormalOwners
                = proofFormalOwnersBuilder.MoveToImmutable();
            var proof = new BoundProgram(functions.ToImmutable(), -1) {
                Structures = [.. proofBinder._structs],
                EnumDefinitions = [.. proofBinder._enumDefinitions],
                SourceGenericTypeDefinitions =
                    [.. proofBinder._sourceGenericTypeDefinitions],
                SourceGenericTypeInstances =
                    [.. proofBinder._sourceGenericTypeInstances],
                GenericNominalTypeDefinitions =
                    [.. proofBinder._genericNominalTypeDefinitions],
                GenericNominalTypeInstances =
                    [.. proofBinder._genericNominalTypeInstances],
                GenericMethodTypeFormalOwners =
                    proofFormalOwners,
                StandardClassDeclarations =
                    [.. proofBinder._standardClassDeclarations],
                NativeCoreRegistration = proofBinder._nativeCoreRegistration,
                NativeOutputCoreRegistration
                    = proofBinder._nativeOutputCoreRegistration,
                LayoutCoreRegistration = proofBinder._layoutCoreRegistration,
                CoreExceptionIdentitySelections =
                    proofBinder._coreExceptionIdentitySelections,
                CoreContractFailureIdentitySelection =
                    proofBinder._coreContractFailureIdentitySelection,
                CoreContractFailureIdentitySelections =
                    proofBinder._coreContractFailureIdentitySelections,
                CoreEvaluationFormIdentitySelection =
                    proofEvaluationFormSelection,
                CoreEvaluationFormIdentitySelections =
                    proofEvaluationFormSelections,
                CoreEvaluationFormSchemaProviders =
                    proofBinder._coreEvaluationFormSchemaProviders,
                NativeFunctionPointerSignatures = [.. proofBinder._nativeFunctionPointerSignatures],
                NativePointerTypes = [.. proofBinder._nativePointerTypes],
                SpanTypes = [.. proofBinder._spanTypes],
                NativeFixedArrayTypes = [.. proofBinder._nativeFixedArrayTypes],
                CheckedReferenceTypes = [.. proofBinder._checkedReferenceTypes],
                Classes = [.. proofBinder._classes],
                Interfaces = proofInterfaces.MoveToImmutable(),
                SourceInputCount = _trees.Length,
                AllowUnmaterializedInterfaceDefaults = true,
                RuntimePolicy = _runtimePolicy,
                GenericMethodSignatures = proofGenericMethodSignatures,
                GenericMethodOperatorProtocols =
                    [.. _genericMethodOperatorProtocols],
            };
            if (!SemanticVerifier.TryVerifySource(proof, out _, out var span,
                out var limit, out var effect,
                allowGenericProofDependencies: true,
                allowUnmaterializedInterfaceDefaults: true))
            {
                var code = effect switch {
                    "NoThrow" => "TRM3017",
                    "NormalReturn" => "TRM3012",
                    "FinallyReturn" => "TRM3019",
                    "FinallyNoReturn" => "TRM3020",
                    "LoopTransfer" => "TRM3008",
                    _ => limit ? "TRM3016" : "TRM3007",
                };
                Report(code,
                    $"Generic method '{template.Name}' violates its declared type, control-flow, or effect contract.",
                    span == default ? template.Declaration.Span : span);
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
                    InterfaceConstraints = formal.InterfaceConstraints,
                });
            }
            var constantFormalDefinitions =
                ImmutableArray.CreateBuilder<BoundGenericMethodConstantFormal>(
                    template.ConstantFormals.Length);
            for (var constantOrdinal = 0;
                constantOrdinal < proofTemplate.ConstantFormals.Length;
                constantOrdinal++)
            {
                var formal = proofTemplate.ConstantFormals[constantOrdinal];
                var placeholderType = formal.PlaceholderType
                    ?? throw new InvalidOperationException(
                        "A generic constant formal has no proof type.");
                constantFormalDefinitions.Add(
                    new BoundGenericMethodConstantFormal(formal.Ordinal,
                        constantOrdinal, formal.Name, formal.Domain,
                        placeholderType,
                        runtimeParameterCount + constantOrdinal,
                        constantDefaults[constantOrdinal]));
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
                    ConstantFormals = [
                            .. signature.ConstantFormals.Select((formal,
                                constantOrdinal) =>
                                formal with {
                                    HasDefault = constantDefaults[
                                        constantOrdinal] is not null,
                                    DefaultFormalDependencies =
                                        constantDefaults[constantOrdinal]
                                            ?.FormalDependencies ?? [],
                                }),
                    ],
                };
            _genericMethodDefinitions.Add(new BoundGenericMethodDefinition(
                template.DefinitionOrdinal, template.Name, template.IsPublic,
                template.RequiresUnsafeCaller, symbolicIndex,
                sourceInputOrdinal, template.Declaration.Span,
                formals.MoveToImmutable(), proof) {
                FormalTypeOwnerOrdinal =
                    template.FormalTypeOwnerOrdinal,
                IsStatic = template.IsStatic,
                IsMutable = template.IsMutable,
                Owner = template.OwnerType is PrimitiveType ownerType
                    ? new BoundGenericMethodOwner(ownerType,
                        template.OwnerGenericTypeDefinitionIndex,
                        template.OwnerTypeArguments) {
                        ConstantArguments =
                            template.OwnerConstantArguments,
                    }
                    : null,
                ConstantFormals =
                    constantFormalDefinitions.MoveToImmutable(),
                FormalOrder = template.FormalOrder,
                RuntimeParameterCount = runtimeParameterCount,
            });
            _layoutKindsUsedInSource |= proofBinder._layoutKindsUsedInSource;
        }

        for (var definitionIndex = 0;
            definitionIndex < _genericMethodDefinitions.Count;
            definitionIndex++)
        {
            var definition = _genericMethodDefinitions[definitionIndex];
            var proofProgram = definition.ProofProgram with {
                GenericMethodSignatures =
                    MergeProofGenericMethodSignatures(
                        [.. _genericMethodSignatures],
                        definition.ProofProgram.GenericMethodSignatures),
            };
            _genericMethodDefinitions[definitionIndex] = definition with {
                ProofProgram = proofProgram,
            };
        }

        return true;
    }

    private static bool TryHydrateGenericMethodSignatures(
        Binder proofBinder,
        ImmutableArray<BoundGenericMethodSignature> signatures,
        out ImmutableArray<BoundGenericMethodSignature> proofSignatures)
    {
        var result = ImmutableArray.CreateBuilder<
            BoundGenericMethodSignature>(signatures.Length);
        foreach (var signature in signatures)
        {
            if (signature is null || signature.Formals.IsDefault
                || signature.ConstantFormals.IsDefault)
            {
                proofSignatures = [];
                return false;
            }

            var formals = ImmutableArray.CreateBuilder<
                BoundGenericMethodFormalSignature>(
                    signature.Formals.Length);
            foreach (var formal in signature.Formals)
            {
                if (formal is null
                    || formal.InterfaceConstraints.IsDefault)
                {
                    proofSignatures = [];
                    return false;
                }

                var interfaceConstraints = ImmutableArray.CreateBuilder<
                    BoundInterfaceType>(formal.InterfaceConstraints.Length);
                foreach (var constraint in formal.InterfaceConstraints)
                {
                    var hydrated = true;
                    interfaceConstraints.Add(
                        BoundTreeTraversal.RewriteTypes(constraint,
                            type =>
                            {
                                if (proofBinder.HydrateProofType(type,
                                    allowFloatingScalar: true,
                                    allowFloatingStructFields: true,
                                    allowUnique: true)
                                    is PrimitiveType proofType)
                                {
                                    return proofType;
                                }
                                hydrated = false;
                                return type;
                            }));
                    if (!hydrated)
                    {
                        proofSignatures = [];
                        return false;
                    }
                }
                formals.Add(formal with {
                    InterfaceConstraints =
                        interfaceConstraints.ToImmutable(),
                });
            }

            var constantFormals = ImmutableArray.CreateBuilder<
                BoundGenericMethodConstantFormalSignature>(
                    signature.ConstantFormals.Length);
            foreach (var formal in signature.ConstantFormals)
            {
                if (formal is null
                    || !TryHydrateGenericMethodTypeTerm(proofBinder,
                        formal.Domain, out var proofDomain))
                {
                    proofSignatures = [];
                    return false;
                }

                constantFormals.Add(formal with {
                    Domain = proofDomain,
                });
            }

            BoundGenericMethodOwner? proofOwner = null;
            if (signature.Owner is BoundGenericMethodOwner owner)
            {
                if (owner.TypeArguments.IsDefault
                    || owner.ConstantArguments.IsDefault
                    || proofBinder.HydrateProofType(owner.ClosedType,
                        allowFloatingScalar: true,
                        allowFloatingStructFields: true,
                        allowUnique: true)
                        is not PrimitiveType proofOwnerType)
                {
                    proofSignatures = [];
                    return false;
                }

                var proofOwnerArguments = ImmutableArray.CreateBuilder<
                    PrimitiveType>(owner.TypeArguments.Length);
                foreach (var ownerArgument in owner.TypeArguments)
                {
                    if (proofBinder.HydrateProofType(ownerArgument,
                        allowFloatingScalar: true,
                        allowFloatingStructFields: true,
                        allowUnique: true)
                        is not PrimitiveType proofOwnerArgument)
                    {
                        proofSignatures = [];
                        return false;
                    }
                    proofOwnerArguments.Add(proofOwnerArgument);
                }

                var proofOwnerConstants = ImmutableArray.CreateBuilder<
                    BoundGenericMethodConstantArgument>(
                        owner.ConstantArguments.Length);
                foreach (var ownerConstant in owner.ConstantArguments)
                {
                    if (!TryHydrateGenericMethodConstantArgument(proofBinder,
                        ownerConstant, out var proofOwnerConstant))
                    {
                        proofSignatures = [];
                        return false;
                    }
                    proofOwnerConstants.Add(proofOwnerConstant);
                }

                proofOwner = owner with {
                    ClosedType = proofOwnerType,
                    TypeArguments = proofOwnerArguments
                        .MoveToImmutable(),
                    ConstantArguments = proofOwnerConstants
                        .MoveToImmutable(),
                };
            }

            result.Add(signature with {
                Formals = formals.MoveToImmutable(),
                Owner = proofOwner,
                ConstantFormals = constantFormals.MoveToImmutable(),
            });
        }

        proofSignatures = result.MoveToImmutable();
        return true;
    }

    private static bool TryHydrateGenericMethodTypeTerm(
        Binder proofBinder, BoundGenericMethodTypeTerm term,
        out BoundGenericMethodTypeTerm hydrated, int depth = 0)
    {
        hydrated = null!;
        if (depth > SemanticVerifier.MaximumDepth
            || term.TypeArguments.IsDefault
            || term.ConstantArguments.IsDefault)
        {
            return false;
        }

        var concreteType = term.ConcreteType;
        if (term.Kind == BoundGenericMethodTypeTermKind.Concrete)
        {
            if (proofBinder.HydrateProofType(concreteType)
                is not PrimitiveType proofType)
            {
                return false;
            }
            concreteType = proofType;
        }
        else if (term.Kind
                == BoundGenericMethodTypeTermKind.SourceGeneric
            && (uint)term.SourceGenericTypeDefinitionIndex
                >= (uint)proofBinder._sourceGenericTypeDefinitions.Count)
        {
            return false;
        }
        else if (term.Kind
                == BoundGenericMethodTypeTermKind.ImportedGeneric
            && (term.NominalTypeIdentity
                    is not BoundGenericNominalTypeIdentity identity
                || !proofBinder._genericNominalTypeDefinitions.Any(
                    definition => definition.Identity == identity)))
        {
            return false;
        }

        var typeArguments = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(term.TypeArguments.Length);
        foreach (var argument in term.TypeArguments)
        {
            if (!TryHydrateGenericMethodTypeTerm(proofBinder, argument,
                out var hydratedArgument, depth + 1))
            {
                return false;
            }
            typeArguments.Add(hydratedArgument);
        }

        var constantArguments = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeConstantArgument>(
                term.ConstantArguments.Length);
        foreach (var argument in term.ConstantArguments)
        {
            if (argument is null)
            {
                return false;
            }

            if (argument.Kind
                == BoundGenericMethodTypeConstantArgumentKind.Value)
            {
                if (proofBinder.HydrateProofType(argument.DomainType)
                        is not PrimitiveType domainType
                    || proofBinder.HydrateProofType(argument.CarrierType)
                        is not PrimitiveType carrierType)
                {
                    return false;
                }
                constantArguments.Add(argument with {
                    DomainType = domainType,
                    CarrierType = carrierType,
                });
            }
            else if (argument.Kind
                == BoundGenericMethodTypeConstantArgumentKind.Formal)
            {
                constantArguments.Add(argument);
            }
            else
            {
                return false;
            }
        }

        hydrated = term with {
            ConcreteType = concreteType,
            TypeArguments = typeArguments.MoveToImmutable(),
            ConstantArguments = constantArguments.MoveToImmutable(),
        };
        return true;
    }

    private static bool TryHydrateGenericMethodConstantArgument(
        Binder proofBinder,
        BoundGenericMethodConstantArgument argument,
        out BoundGenericMethodConstantArgument hydrated)
    {
        hydrated = null!;
        if (!TryHydrateGenericMethodTypeTerm(proofBinder, argument.Domain,
                out var domain))
        {
            return false;
        }

        if (argument.Kind == BoundGenericMethodConstantArgumentKind.Value)
        {
            if (proofBinder.HydrateProofType(argument.DomainType)
                    is not PrimitiveType domainType
                || proofBinder.HydrateProofType(argument.CarrierType)
                    is not PrimitiveType carrierType)
            {
                return false;
            }
            hydrated = argument with {
                Domain = domain,
                DomainType = domainType,
                CarrierType = carrierType,
            };
            return true;
        }

        if (argument.Kind
                != BoundGenericMethodConstantArgumentKind.Formal
            || argument.CallerDefinitionOrdinal < 0
            || argument.CallerConstantFormalOrdinal < 0)
        {
            return false;
        }
        if (argument.CallerFormalOwnerKind
            == BoundGenericTypeFormalOwnerKind.GenericMethod)
        {
            if (argument.CallerFormalOrdinal < 0)
            {
                return false;
            }
        }
        else if (argument.CallerFormalOwnerKind
                != BoundGenericTypeFormalOwnerKind.SourceGenericType
            || argument.CallerFormalOrdinal != -1)
        {
            return false;
        }

        hydrated = argument with { Domain = domain };
        return true;
    }

    private static PrimitiveType CreateGenericConstantFormalPlaceholder(
        Binder proofBinder, int constantOrdinal)
    {
        var placeholderType = (PrimitiveType)(100
            + proofBinder._structs.Count);
        var name = "$C" + constantOrdinal.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        proofBinder._structs.Add(new BoundStruct(name, [
            new BoundStructField("$opaque", PrimitiveType.Int32, 0,
                sizeof(int)) {
                IsPrivate = true,
            },
        ], sizeof(int)) {
            IsInternal = true,
            Alignment = sizeof(int),
            RequiredAlignment = 1,
        });
        return placeholderType;
    }

    private static bool AreGenericMethodTypeTermsEquivalent(
        BoundGenericMethodTypeTerm left,
        BoundGenericMethodTypeTerm right, int depth = 0) =>
        BoundGenericMethodTypeTerms.AreEquivalent(left, right, depth);

    private static ImmutableArray<BoundGenericMethodSignature>
        MergeProofGenericMethodSignatures(
            ImmutableArray<BoundGenericMethodSignature> sourceSignatures,
            ImmutableArray<BoundGenericMethodSignature> proofSignatures)
    {
        if (sourceSignatures.IsDefault || proofSignatures.IsDefault
                || sourceSignatures.Length < proofSignatures.Length)
        {
            throw new InvalidOperationException(
                "Generic proof signature catalogs do not match.");
        }

        var result = ImmutableArray.CreateBuilder<
                BoundGenericMethodSignature>(proofSignatures.Length);
        for (var signatureIndex = 0;
                signatureIndex < proofSignatures.Length; signatureIndex++)
        {
            var source = sourceSignatures[signatureIndex];
            var proof = proofSignatures[signatureIndex];
            if (source is null || proof is null
                    || source.DefinitionOrdinal != signatureIndex
                    || proof.DefinitionOrdinal != signatureIndex
                    || source.Name != proof.Name
                    || source.IsStatic != proof.IsStatic
                    || source.IsMutable != proof.IsMutable
                    || (source.Owner is null) != (proof.Owner is null)
                    || source.Formals.IsDefault
                    || proof.Formals.IsDefault
                    || source.Formals.Length != proof.Formals.Length
                    || source.ConstantFormals.IsDefault
                    || proof.ConstantFormals.IsDefault
                    || source.ConstantFormals.Length
                        != proof.ConstantFormals.Length)
            {
                throw new InvalidOperationException(
                    "Generic proof constant formal catalogs do not match.");
            }

            result.Add(source with {
                Formals = [
                    .. source.Formals.Select((formal, formalOrdinal) =>
                        formal with {
                            InterfaceConstraints =
                                proof.Formals[formalOrdinal]
                                    .InterfaceConstraints,
                        }),
                ],
                IsStatic = proof.IsStatic,
                IsMutable = proof.IsMutable,
                Owner = proof.Owner,
                ConstantFormals = [
                    .. source.ConstantFormals.Select((formal,
                        constantOrdinal) =>
                        formal with {
                            Domain = proof.ConstantFormals[
                                constantOrdinal].Domain,
                        }),
                ],
            });
        }

        return result.MoveToImmutable();
    }

    private BoundFunction BindSignatureBodyPreservingContext(
        Signature signature, bool preserveConstEvalFunctions = false)
    {
        var context = CaptureContext();
        var variables = _variables.ToArray();
        var scopes = _scopes.ToArray();
        var statementScopes = _statementScopes.ToArray();
        var loops = _loops.ToArray();
        var switches = _switches.ToArray();
        var unresolvedVacantLocals = _unresolvedVacantLocals.ToArray();
        var constLocals = _constLocals.ToArray();
        var constScalarValues = new Dictionary<int, BoundConstant>(
            _constScalarValues);
        var constRequired = _constRequired.ToArray();
        var aliasTypeArguments = _aliasTypeArguments.ToArray();
        var genericUniqueRefinements
            = new Dictionary<PrimitiveType, bool>(
                _genericUniqueRefinements);
        var localStart = _localStart;
        var returnType = _returnType;
        var currentStruct = _currentStruct;
        var isConstructor = _constructor;
        var hasReceiver = _hasReceiver;
        var genericOwnerTypeArguments = _genericOwnerTypeArguments;
        var genericContext = _genericContext;
        var bindingContractPhase = _bindingContractPhase;
        var staticConstructorIdentity =
            _bindingStaticConstructorIdentity;
        var staticFieldInitializerIndex
            = _bindingStaticFieldInitializerIndex;
        var queryPlaceBindingDepth = _queryPlaceBindingDepth;
        var finallyLoopDepth = _finallyLoopDepth;
        var unsafeDepth = _unsafeDepth;
        var layoutAttributeConstantDepth
            = _layoutAttributeConstantDepth;
        var genericProofFunctions = _genericProofFunctions;
        var genericProofSource = _genericProofSource;
        var boundConstEvalFunctions = _boundConstEvalFunctions;
        var isBindingConstEvalBody = _isBindingConstEvalBody;
        var hasRequiredExpression = _hasRequiredExpression;
        var hasStaticProof = _hasStaticProof;
        var staticProofVariables = _staticProofVariables;
        var staticProofJoinConstants = new HashSet<(
            PrimitiveType Type, ScalarBits Bits)>(_staticProofJoinConstants);

        try
        {
            return BindSignatureBody(signature);
        }
        finally
        {
            RestoreContext(context);
            _variables.Clear();
            _variables.AddRange(variables);
            _scopes.Clear();
            _scopes.AddRange(scopes);
            _statementScopes.Clear();
            _statementScopes.AddRange(statementScopes);
            _loops.Clear();
            _loops.AddRange(loops);
            _switches.Clear();
            _switches.AddRange(switches);
            _unresolvedVacantLocals.Clear();
            _unresolvedVacantLocals.UnionWith(unresolvedVacantLocals);
            _constLocals.Clear();
            _constLocals.UnionWith(constLocals);
            _constScalarValues.Clear();
            foreach (var (variable, value) in constScalarValues)
            {
                _constScalarValues.Add(variable, value);
            }
            _constRequired.Clear();
            _constRequired.UnionWith(constRequired);
            _aliasTypeArguments.Clear();
            _aliasTypeArguments.AddRange(aliasTypeArguments);
            _genericUniqueRefinements.Clear();
            foreach (var (type, isUnique) in genericUniqueRefinements)
            {
                _genericUniqueRefinements.Add(type, isUnique);
            }
            _localStart = localStart;
            _returnType = returnType;
            _currentStruct = currentStruct;
            _constructor = isConstructor;
            _hasReceiver = hasReceiver;
            _genericOwnerTypeArguments = genericOwnerTypeArguments;
            _genericContext = genericContext;
            _bindingContractPhase = bindingContractPhase;
            _bindingStaticConstructorIdentity
                = staticConstructorIdentity;
            _bindingStaticFieldInitializerIndex
                = staticFieldInitializerIndex;
            _queryPlaceBindingDepth = queryPlaceBindingDepth;
            _finallyLoopDepth = finallyLoopDepth;
            _unsafeDepth = unsafeDepth;
            _layoutAttributeConstantDepth
                = layoutAttributeConstantDepth;
            _genericProofFunctions = genericProofFunctions;
            _genericProofSource = genericProofSource;
            if (!preserveConstEvalFunctions)
            {
                _boundConstEvalFunctions = boundConstEvalFunctions;
            }
            _isBindingConstEvalBody = isBindingConstEvalBody;
            _hasRequiredExpression = hasRequiredExpression;
            _hasStaticProof = hasStaticProof;
            _staticProofVariables = staticProofVariables;
            _staticProofJoinConstants.Clear();
            _staticProofJoinConstants.UnionWith(
                staticProofJoinConstants);
        }
    }

    private bool TryBindGenericMethodConstantDefaults(
        Binder proofBinder, GenericTemplate template,
        ImmutableArray<PrimitiveType> symbolicTypes,
        ImmutableArray<BoundFunction>.Builder proofFunctions,
        out ImmutableArray<BoundGenericMethodConstantDefault?> defaults)
    {
        defaults = [];
        var result = ImmutableArray.CreateBuilder<
            BoundGenericMethodConstantDefault?>(
                template.ConstantFormals.Length);
        foreach (var formal in template.ConstantFormals)
        {
            if (formal.DefaultSyntax is not SyntaxNode defaultSyntax)
            {
                result.Add(null);
                continue;
            }

            if (proofBinder._variables.Count != 0
                || proofBinder._scopes.Count != 0
                || formal.PlaceholderType is not PrimitiveType expectedType)
            {
                throw new InvalidOperationException(
                    "A constant default proof has no isolated context.");
            }

            var previousContext = proofBinder.CaptureContext();
            var previousGenericContext = proofBinder._genericContext;
            var diagnosticCount = proofBinder.DiagnosticCount;
            try
            {
                proofBinder.SetContext(template.Tree, defaultSyntax);
                proofBinder._genericContext = new GenericInstantiation(
                    template, symbolicTypes) {
                    ConstantArguments =
                        CreateFormalGenericMethodConstantArguments(template),
                };
                proofBinder._scopes.Add(
                    new Dictionary<string, int>(StringComparer.Ordinal));
                foreach (var dependency in formal.DefaultFormalDependencies)
                {
                    var dependencyFormal = template.ConstantFormals
                        .FirstOrDefault(candidate =>
                            candidate.Ordinal == dependency);
                    if (dependencyFormal is null
                        || dependency >= formal.Ordinal
                        || dependencyFormal.PlaceholderType
                            is not PrimitiveType dependencyType)
                    {
                        Report("TRM3007",
                            "A constant generic default may depend only on earlier constant formals with compatible domains.",
                            defaultSyntax.Span);
                        return false;
                    }

                    _ = proofBinder.AddVariable(dependencyFormal.Name,
                        dependencyType, false, defaultSyntax.Span);
                }

                var expression = proofBinder.BindExpression(defaultSyntax,
                    expectedType);
                if (proofBinder.DiagnosticCount != diagnosticCount
                    || expression.Type != expectedType
                    || proofBinder._variables.Count
                        != formal.DefaultFormalDependencies.Length)
                {
                    foreach (var diagnostic in proofBinder.Diagnostics)
                    {
                        AddDiagnostic(diagnostic, template.Tree);
                    }
                    Report("TRM3007",
                        "A constant generic default must bind to its declared domain using only earlier constant formals.",
                        defaultSyntax.Span);
                    return false;
                }

                var functionIndex = proofFunctions.Count;
                proofFunctions.Add(new BoundFunction(
                    template.Name + "$constant-default$"
                        + formal.Ordinal.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                    expectedType, [.. proofBinder._variables],
                    proofBinder._variables.Count,
                    new BoundBlock([
                        new BoundReturn(expression, defaultSyntax.Span),
                    ], defaultSyntax.Span), defaultSyntax.Span) {
                    GenericMethodProofDefinitionOrdinal =
                        template.DefinitionOrdinal,
                        GenericMethodProofTypeFormalOwnerOrdinal =
                            template.FormalTypeOwnerOrdinal,
                    GenericMethodProofTypeParameters = symbolicTypes,
                    GenericMethodConstantDefaultFormalOrdinal =
                        formal.Ordinal,
                    GenericMethodConstantDefaultDependencies =
                        formal.DefaultFormalDependencies,
                });
                result.Add(new BoundGenericMethodConstantDefault(
                    functionIndex, formal.DefaultFormalDependencies));
            }
            finally
            {
                proofBinder._variables.Clear();
                proofBinder._scopes.Clear();
                proofBinder._genericContext = previousGenericContext;
                proofBinder.RestoreContext(previousContext);
            }
        }

        defaults = result.MoveToImmutable();
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
