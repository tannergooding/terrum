// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private const int MaximumStaticSpecializations = 32;
    private readonly Dictionary<string, StaticProtocol> _protocols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GenericTemplate> _templates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, PrimitiveType>> _conformances = new(StringComparer.Ordinal);

    internal readonly record struct StaticOperatorKey(TokenKind Operator, int Arity);
    private sealed record StaticProtocol(string Name, StaticOperatorKey Key, bool IsNoThrow,
        bool RequiresUnsafeCaller, bool HasSelf, SyntaxNode Declaration, SyntaxTree Tree);
    internal sealed record GenericOperatorConstraint(string Protocol, PrimitiveType? FixedResult,
        int ResultTypeParameterIndex)
    {
        internal PrimitiveType Resolve(ImmutableArray<PrimitiveType> types) =>
            FixedResult ?? types[ResultTypeParameterIndex];
    }
    internal sealed record GenericTypeParameter(string Name,
        ImmutableDictionary<StaticOperatorKey, GenericOperatorConstraint> Operations);
    internal sealed record GenericParameter(string Name, PrimitiveType? ScalarType, int TypeParameterIndex,
        bool IsOut, bool IsInit)
    {
        internal PrimitiveType Resolve(ImmutableArray<PrimitiveType> types) =>
            ScalarType ?? types[TypeParameterIndex];
    }
    internal sealed record GenericTemplate(string Name, ImmutableArray<GenericTypeParameter> TypeParameters,
        PrimitiveType? ScalarReturnType, int ReturnTypeParameterIndex, ImmutableArray<GenericParameter> Parameters,
        SyntaxNode Body, SyntaxNode Declaration, SyntaxTree Tree, bool IsNoThrow)
    {
        internal bool RequiresUnsafeCaller { get; init; }

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
    internal sealed record GenericInstantiation(GenericTemplate Template, ImmutableArray<PrimitiveType> Types);
    private ImmutableArray<BoundFunction>.Builder? _genericProofFunctions;
    private Binder? _genericProofSource;

    private PrimitiveType? HydrateProofType(PrimitiveType sourceType)
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
        if (!source.IsStruct(sourceType))
        {
            return sourceType is PrimitiveType.Void or PrimitiveType.Bool or PrimitiveType.Char16
                or PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
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
            if (HydrateProofType(field.Type) is not PrimitiveType fieldType)
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
        if (declaration.IsCleanup || declaration.IsClassConstructor
            || HydrateProofType(declaration.ReturnType) is not PrimitiveType result)
        {
            return false;
        }
        var parameters = ImmutableArray.CreateBuilder<Parameter>(declaration.Parameters.Length);
        foreach (var parameter in declaration.Parameters)
        {
            if (HydrateProofType(parameter.Type) is not PrimitiveType type)
            {
                return false;
            }
            parameters.Add(parameter with { Type = type });
        }
        var owner = declaration.StructName is null ? null
            : declaration.Name[..declaration.Name.LastIndexOf('.', StringComparison.Ordinal)];
        PrimitiveType? receiver = null;
        if (owner is not null)
        {
            if (!source._structNames.TryGetValue(owner, out var sourceType)
                && !source._classNames.TryGetValue(owner, out sourceType))
            {
                return false;
            }
            if (HydrateProofType(sourceType) is not PrimitiveType mapped)
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
            parameter.Type, parameter.IsOut, parameter.Span) { IsOut = parameter.IsOut }));
        var external = new BoundFunction(declaration.Name, result, variables.ToImmutable(),
            parameters.Count + (receiver is not null && !declaration.IsStatic && !declaration.IsConstructor ? 1 : 0),
            new BoundBlock([], declaration.Tree.Root.Span), declaration.Tree.Root.Span) {
            IsExternal = true,
            IsConstructor = declaration.IsConstructor,
            IsStatic = declaration.IsStatic,
            IsNoThrow = declaration.IsNoThrow,
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

    private static bool IsComparisonOperator(TokenKind kind) => kind is TokenKind.EqualsEquals or TokenKind.BangEquals
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

    private static PrimitiveType? GenericScalarType(string name) => name switch {
        "int32" => PrimitiveType.Int32,
        "uint32" => PrimitiveType.UInt32,
        "int64" => PrimitiveType.Int64,
        "uint64" => PrimitiveType.UInt64,
        "char16" => PrimitiveType.Char16,
        "bool" => PrimitiveType.Bool,
        _ => null,
    };

    private bool IsCopyGenericType(PrimitiveType type) => type is PrimitiveType.Bool or PrimitiveType.Char16
        or PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
        || IsStruct(type)
        || (ManagedClassTypes.IsManaged(type)
            && ((int)type < ManagedClassTypes.First
                || ManagedClassTypes.ClassIndex(type) < _classes.Count));

    private void DeclareProtocols()
    {
        foreach (var tree in _trees)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SetContext(tree);
            foreach (var node in tree.Root.Children.OfType<SyntaxNode>())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (node.Kind != SyntaxKind.InterfaceDeclaration)
                {
                    continue;
                }
                var name = ((SyntaxTokenElement)node.Children.First(element =>
                    element is SyntaxTokenElement { Token.Kind: TokenKind.Identifier })).Token.Name;
                if (TryPrimitiveType(name, out _))
                {
                    Report("TRM3003", $"The primitive type '{name}' cannot be declared as an interface.", node.Span);
                    continue;
                }
                var qualified = Qualify(name);
                SyntaxNode? parameters = null;
                SyntaxNode? operatorMember = null;
                var operatorCount = 0;
                var hasInvalidInterfaceModifier = false;
                var hasInvalidDeclaration = false;
                foreach (var child in node.Children)
                {
                    if (child is SyntaxTokenElement tokenElement)
                    {
                        hasInvalidInterfaceModifier |= tokenElement.Token.Kind is TokenKind.PublicKeyword
                            or TokenKind.PrivateKeyword or TokenKind.StaticKeyword or TokenKind.MutKeyword
                            or TokenKind.UniqueKeyword;
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
                var noThrowCount = 0;
                var unsafeModifierCount = 0;
                var staticModifierCount = 0;
                var hasInvalidModifier = false;
                var hasInvalidAttribute = false;
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
                            or TokenKind.InternalKeyword or TokenKind.MutKeyword or TokenKind.UniqueKeyword;
                    }
                    else if (child is SyntaxNode { Kind: SyntaxKind.Attribute } attribute)
                    {
                        if (NameText(attribute) == "[NoThrow]")
                        {
                            noThrowCount++;
                        }
                        else
                        {
                            hasInvalidAttribute = true;
                        }
                    }
                }
                var signature = member.Children.OfType<SyntaxNode>()
                    .Where(static child => child.Kind != SyntaxKind.Attribute).ToArray();
                var formals = signature.FirstOrDefault(static child => child.Kind == SyntaxKind.ParameterList);
                var arguments = formals?.Children.OfType<SyntaxNode>().ToArray() ?? [];
                var noThrow = noThrowCount > 0;
                var arity = arguments.Length;
                if (op is not TokenKind operation
                    || !((arity == 1 && IsStaticUnaryOperator(operation))
                        || (arity == 2 && IsStaticBinaryOperator(operation)))
                    || variableCount != arity + 1 || signature.Length != 2
                    || signature[0].Kind != SyntaxKind.TypeName
                    || NameText(signature[0]) != variables[variableCount - 1]
                    || arguments[0].Children is not [SyntaxNode first, SyntaxTokenElement]
                    || NameText(first) != variables[0]
                    || (arity == 2 && (arguments[1].Children is not [SyntaxNode second, SyntaxTokenElement]
                        || NameText(second) != variables[1]))
                    || member.Children[^1] is not SyntaxTokenElement { Token.Kind: TokenKind.Semicolon }
                    || staticModifierCount != 1
                    || hasInvalidModifier
                    || hasInvalidAttribute
                    || noThrowCount > 1 || unsafeModifierCount > 1)
                {
                    Report("TRM3001", "Static operator protocol signatures must match their operand and result formals and end in ';'.", member.Span);
                    continue;
                }
                if (!_protocols.TryAdd(qualified, new StaticProtocol(qualified,
                    new StaticOperatorKey(operation, arity), noThrow, unsafeModifierCount != 0,
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
                SetContext(protocol.Tree);
                Report("TRM3003", $"Protocol and struct cannot share '{protocol.Name}'.", protocol.Declaration.Span);
            }
        }
        foreach (var (index, (tree, declaration)) in _structDeclarations)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SetContext(tree);
            var typeName = _structs[index].Name;
            var list = declaration.Children.OfType<SyntaxNode>()
                .FirstOrDefault(static child => child.Kind == SyntaxKind.ConformanceList);
            if (list is null)
            {
                continue;
            }
            var witnesses = new Dictionary<string, PrimitiveType>(StringComparer.Ordinal);
            if (!_structs[index].IsInternal)
            {
                Report("TRM3007", "Static protocol witnesses are compilation-local and require an internal struct.", declaration.Span);
            }
            foreach (var name in list.Children.OfType<SyntaxNode>())
            {
                var shape = TryProtocolApplication(name, out var head, out var parts);
                var protocolName = shape ? FindProtocol(head) : null;
                if (protocolName is null
                    || !MatchesProtocolArguments(_protocols[protocolName], parts,
                        StructName(declaration).Name, typeName))
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
                    SetContext(protocol.Tree);
                    Report("TRM3007", $"'{name}' promises '{protocolName}' but has no matching operator.", protocol.Declaration.Span);
                    continue;
                }
                var signature = _signatures[index];
                if (signature.ReturnType != result
                    || signature.Parameters.Length != protocol.Key.Arity
                    || signature.Parameters.Any(parameter => parameter.Type != type || parameter.IsOut)
                    || !signature.IsStatic || !signature.IsOperator || signature.IsPrivate || signature.IsConstructor
                    || (protocol.IsNoThrow && !signature.IsNoThrow))
                {
                    SetContext(signature.Tree);
                    Report("TRM3007", $"'{name}' does not fulfill the static operator contract '{protocolName}'.",
                        signature.Declaration?.Span ?? protocol.Declaration.Span);
                }
            }
        }
    }

    private void DeclareGeneric(SyntaxNode declaration, SyntaxNode typeParameters)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var children = declaration.Children.OfType<SyntaxNode>().ToArray();
        var typeNames = typeParameters.Children.OfType<SyntaxTokenElement>()
            .Where(static child => child.Token.Kind == TokenKind.Identifier)
            .Select(static child => child.Token.Name).ToArray();
        var hasPrimitiveTypeParameter = false;
        var hasDuplicateTypeParameter = false;
        HashSet<string>? seenTypeParameters = typeNames.Length is >= 2 and <= 8
            ? new HashSet<string>(StringComparer.Ordinal) : null;
        foreach (var typeName in typeNames)
        {
            hasPrimitiveTypeParameter |= TryPrimitiveType(typeName, out _);
            if (seenTypeParameters is not null)
            {
                hasDuplicateTypeParameter |= !seenTypeParameters.Add(typeName);
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
        foreach (var child in children)
        {
            if (returnType is null && child.Kind == SyntaxKind.TypeName)
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
        }
        var returnName = returnType is null ? "" : NameText(returnType);
        var scalarReturn = GenericScalarType(returnName);
        var nameToken = ((SyntaxTokenElement)declaration.Children.First(element =>
            element is SyntaxTokenElement { Token.Kind: TokenKind.Identifier })).Token;
        var qualified = Qualify(nameToken.Name);
        var arguments = parameters?.Children.OfType<SyntaxNode>().ToArray() ?? [];
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        var templateParameters = ImmutableArray.CreateBuilder<GenericParameter>(arguments.Length);
        var operations = typeNames.Select(static _ =>
            ImmutableDictionary.CreateBuilder<StaticOperatorKey, GenericOperatorConstraint>()).ToArray();
        var copies = new bool[typeNames.Length];
        var constrained = new HashSet<string>(StringComparer.Ordinal);
        var returnIndex = Array.IndexOf(typeNames, returnName);
        var noThrowCount = 0;
        var attributeCount = 0;
        var hasInvalidAttribute = false;
        foreach (var child in children)
        {
            if (child.Kind == SyntaxKind.Attribute)
            {
                attributeCount++;
                if (NameText(child) == "[NoThrow]")
                {
                    noThrowCount++;
                }
                else
                {
                    hasInvalidAttribute = true;
                }
            }
        }
        var noThrow = noThrowCount != 0;
        var unsafeModifierCount = 0;
        var hasInvalidModifier = false;
        foreach (var element in declaration.Children.OfType<SyntaxTokenElement>())
        {
            var tokenKind = element.Token.Kind;
            if (tokenKind == TokenKind.UnsafeKeyword)
            {
                unsafeModifierCount++;
            }
            hasInvalidModifier |= tokenKind is TokenKind.PublicKeyword or TokenKind.PrivateKeyword
                or TokenKind.StaticKeyword or TokenKind.MutKeyword or TokenKind.UniqueKeyword;
        }
        var requiresUnsafeCaller = unsafeModifierCount != 0;
        var valid = typeNames.Length is >= 1 and <= 8
            && !hasDuplicateTypeParameter
            && returnType is not null && body is not null
            && (returnIndex >= 0 || scalarReturn is not null)
            && arguments.Length is >= 1 and <= 8
            && unsafeModifierCount <= 1
            && !hasInvalidModifier
            && !hasInvalidAttribute
            && attributeCount <= 1;
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
            if (argument.Kind != SyntaxKind.Parameter
                || (argument.Children is not [SyntaxNode, SyntaxTokenElement] && !isOut && !isInit)
                || argumentType is null
                || argument.Children[^1] is not SyntaxTokenElement argumentName
                || argumentType.Kind != SyntaxKind.TypeName
                || !parameterNames.Add(argumentName.Token.Name))
            {
                valid = false;
            }
            else
            {
                var typeName = NameText(argumentType);
                var scalar = GenericScalarType(typeName);
                var index = Array.IndexOf(typeNames, typeName);
                if (index < 0 && scalar is null)
                {
                    valid = false;
                }
                templateParameters.Add(new GenericParameter(argumentName.Token.Name,
                    index < 0 ? scalar : null, index, isOut || isInit, isInit));
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
            var index = Array.IndexOf(typeNames, formal.Token.Name);
            if (index < 0)
            {
                valid = false;
                continue;
            }
            foreach (var item in constraint.Children.OfType<SyntaxNode>())
            {
                if (item.Kind == SyntaxKind.Attribute)
                {
                    if (NameText(item) != "[Copy]" || copies[index])
                    {
                        valid = false;
                    }
                    copies[index] = true;
                    continue;
                }
                var shape = TryProtocolApplication(item, out var head, out var parts);
                var protocol = shape ? FindProtocol(head) : null;
                if (protocol is null || !MatchesProtocolArguments(_protocols[protocol], parts,
                        formal.Token.Name)
                    || operations[index].ContainsKey(_protocols[protocol].Key))
                {
                    valid = false;
                }
                else
                {
                    var resultName = NameText(parts[^1]);
                    var resultIndex = Array.IndexOf(typeNames, resultName);
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
        if (body is null || copies.Any(static copy => !copy)
            || (templateParameters.Any(static parameter => parameter.IsOut) && scalarReturn != PrimitiveType.Bool)
            || Enumerable.Range(0, typeNames.Length).Any(index => returnIndex != index
                && !templateParameters.Any(parameter => parameter.TypeParameterIndex == index))
            || (noThrow && operations.Any(operators =>
                operators.Values.Any(operation => !_protocols[operation.Protocol].IsNoThrow))))
        {
            valid = false;
        }
        if (!valid || _templates.Count >= MaximumStaticSpecializations)
        {
            Report("TRM3007", "Generic function requires distinct local Copy type parameters, supported value or bool/output parameters, and explicit constraints for any operators.", declaration.Span);
            return;
        }
        if (!_templates.TryAdd(qualified, new GenericTemplate(qualified,
            [.. typeNames.Select((name, index) => new GenericTypeParameter(name, operations[index].ToImmutable()))],
            scalarReturn, returnIndex, templateParameters.ToImmutable(), body!, declaration, _tree, noThrow) {
                RequiresUnsafeCaller = requiresUnsafeCaller,
            }))
        {
            Report("TRM3003", $"Duplicate generic function '{qualified}'.", declaration.Span);
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
            var arguments = ((SyntaxNode)callee.Children[1]).Children.OfType<SyntaxNode>().ToArray();
            if (arguments.Length != template.TypeParameters.Length)
            {
                return null;
            }
            var resolvedTypes = ImmutableArray.CreateBuilder<PrimitiveType>(arguments.Length);
            foreach (var argument in arguments)
            {
                if (!TryResolveGenericArgument(argument, out var type))
                {
                    return null;
                }
                resolvedTypes.Add(type);
            }
            return template.ReturnType(resolvedTypes.MoveToImmutable());
        }
        if (template.ScalarReturnType is PrimitiveType scalar)
        {
            return scalar;
        }
        var inferred = callArguments is null ? null
            : InferGenericTypes(template, callArguments, null, out _);
        return inferred is ImmutableArray<PrimitiveType> types
            ? template.ReturnType(types) : null;
    }

    private ImmutableArray<PrimitiveType>? InferGenericTypes(GenericTemplate template, SyntaxNode arguments,
        PrimitiveType? expected, out bool conflictingArguments)
    {
        conflictingArguments = false;
        var inferred = new PrimitiveType?[template.TypeParameters.Length];
        var supplied = arguments.Children.OfType<SyntaxNode>().ToArray();
        for (var index = 0; index < Math.Min(supplied.Length, template.Parameters.Length); index++)
        {
            var parameterIndex = template.Parameters[index].TypeParameterIndex;
            if (parameterIndex < 0)
            {
                continue;
            }
            var argument = supplied[index];
            PrimitiveType? candidate = null;
            if (argument.Children is [SyntaxNode value])
            {
                candidate = KnownType(value);
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
        if (template.ScalarReturnType is null && expected is PrimitiveType result)
        {
            var index = template.ReturnTypeParameterIndex;
            if (inferred[index] is PrimitiveType established)
            {
                if (!CanImplicitlyConvert(established, result))
                {
                    conflictingArguments = true;
                    return null;
                }
            }
            else
            {
                inferred[index] = result;
            }
        }
        return inferred.Any(static item => item is null) ? null
            : [.. inferred.Select(static item => item.GetValueOrDefault())];
    }

    private GenericTemplate? FindGenericTemplate(string name)
    {
        GenericTemplate? candidate = null;
        var candidateName = string.Empty;
        var hasCandidate = false;
        var qualifiedName = Qualify(name);
        if (_templates.TryGetValue(qualifiedName, out candidate))
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
        var explicitArguments = callee.Kind == SyntaxKind.GenericNameExpression
            ? ((SyntaxNode)callee.Children[1]).Children.OfType<SyntaxNode>().ToArray() : [];
        ImmutableArray<PrimitiveType>? concrete = null;
        if (template is not null && callee.Kind == SyntaxKind.GenericNameExpression
            && explicitArguments.Length == template.TypeParameters.Length)
        {
            var resolvedTypes = ImmutableArray.CreateBuilder<PrimitiveType>(explicitArguments.Length);
            foreach (var argument in explicitArguments)
            {
                if (!TryResolveGenericArgument(argument, out var resolved))
                {
                    break;
                }
                resolvedTypes.Add(resolved);
            }
            if (resolvedTypes.Count == explicitArguments.Length)
            {
                concrete = resolvedTypes.MoveToImmutable();
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
        if (template is null || concrete is not ImmutableArray<PrimitiveType> types
            || types.Any(type => !IsCopyGenericType(type)))
        {
            Report("TRM3011", "A generic call requires each Copy type from a complete explicit list, typed arguments, or its result context.",
                callee.Span);
            return -1;
        }
        var typeNames = types.Select(type => IsStruct(type) ? _structs[(int)type - 100].Name : TypeName(type)).ToArray();
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
        if (types.Any(type => IsStruct(type) && _structs[(int)type - 100].IsUnique))
        {
            Report("TRM3007", "Generic type arguments lack their declared Copy static protocol witnesses.", callee.Span);
            return -1;
        }
        var parameters = template.Parameters.Select(parameter => new Parameter(parameter.Name, parameter.Resolve(types),
            callee.Span, parameter.IsOut, parameter.IsInit)).ToImmutableArray();
        var index = _signatures.Count;
        _functions.Add(instantiated, index);
        var signature = new Signature(instantiated, template.ReturnType(types), parameters,
            _genericProofFunctions is null ? template.Body : null,
            _genericProofFunctions is null ? template.Declaration : null,
            template.Tree, null, false, false, true, false, true, template.IsNoThrow, false) {
            RequiresUnsafeCaller = template.RequiresUnsafeCaller,
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
                IsNoThrow = template.IsNoThrow,
                RequiresUnsafeCaller = template.RequiresUnsafeCaller,
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

    private bool ValidateGenericTemplates()
    {
        foreach (var template in _templates.Values)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SetContext(template.Tree);
            var proofBinder = new Binder([template.Tree], [], _cancellationToken,
                isLibrary: true, runtimePolicy: _runtimePolicy);
            foreach (var (tree, context) in _contexts)
            {
                proofBinder._contexts.Add(tree, context);
            }
            foreach (var (name, alias) in _aliases)
            {
                proofBinder._aliases.Add(name, alias);
            }
            proofBinder.SetContext(template.Tree);
            var functions = ImmutableArray.CreateBuilder<BoundFunction>();
            var probeTypes = ImmutableArray.CreateBuilder<PrimitiveType>(template.TypeParameters.Length);
            proofBinder._genericProofFunctions = functions;
            proofBinder._genericProofSource = this;
            var collect = NativeManagedNodeIntrinsics.Functions.Single(static function =>
                function.Intrinsic == BoundIntrinsic.NodeCollect);
            proofBinder._functions.Add(collect.Name, proofBinder._signatures.Count);
            proofBinder._signatures.Add(new Signature(collect.Name, collect.ReturnType, [],
                null, null, template.Tree, collect.DeclaringStruct, false, false, true, false,
                collect.IsStatic, collect.IsNoThrow, false, collect));
            functions.Add(collect);
            for (var parameterIndex = 0; parameterIndex < template.TypeParameters.Length; parameterIndex++)
            {
                var probeName = template.TypeParameters.Length == 1 ? "$T" : "$T" + parameterIndex;
                // Symbolic formals must not alias any concrete Copy structure in this proof.
                var probeType = (PrimitiveType)(100 + proofBinder._structs.Count);
                probeTypes.Add(probeType);
                proofBinder._structs.Add(new BoundStruct(probeName,
                    [new BoundStructField("$opaque", PrimitiveType.Int32, 0, 4) { IsPrivate = true }], 4) {
                    IsInternal = true,
                });
            }
            var symbolicTypes = probeTypes.MoveToImmutable();
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
                        IsNoThrow = _protocols[constraint.Protocol].IsNoThrow,
                        RequiresUnsafeCaller = _protocols[constraint.Protocol].RequiresUnsafeCaller,
                        DeclaringStruct = probeName,
                    };
                    proofBinder._functions.Add(name, proofBinder._signatures.Count);
                    var parameters = variables.Select(variable =>
                        new Parameter(variable.Name, probeType, operatorSpan, false)).ToImmutableArray();
                    proofBinder._signatures.Add(new Signature(name, resultType, parameters,
                        null, null, template.Tree, probeName, false, false, false, false, true,
                        external.IsNoThrow, false, external) {
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
            var symbolicParameters = template.Parameters.Select(parameter => new Parameter(parameter.Name,
                parameter.Resolve(symbolicTypes), template.Declaration.Span, parameter.IsOut,
                parameter.IsInit)).ToImmutableArray();
            var symbolicVariables = symbolicParameters.Select(parameter => new BoundVariable(parameter.Name,
                parameter.Type, parameter.IsOut, parameter.Span) {
                IsOut = parameter.IsOut,
                IsInit = parameter.IsInit,
            }).ToImmutableArray();
            var symbolicReturn = template.ReturnType(symbolicTypes);
            var symbolicIndex = proofBinder._signatures.Count;
            proofBinder._functions.Add(symbolicName, symbolicIndex);
            proofBinder._signatures.Add(new Signature(symbolicName, symbolicReturn, symbolicParameters,
                null, null, template.Tree, null, false, false, true, false, true, template.IsNoThrow, false,
                new BoundFunction(symbolicName, symbolicReturn, symbolicVariables, symbolicParameters.Length,
                    new BoundBlock([], template.Declaration.Span), template.Declaration.Span) {
                    IsExternal = true,
                    IsNoThrow = template.IsNoThrow,
                    RequiresUnsafeCaller = template.RequiresUnsafeCaller,
                }));
            functions.Add(proofBinder._signatures[symbolicIndex].External
                ?? throw new InvalidOperationException("Missing generic proof signature."));

            proofBinder._scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
            proofBinder._returnType = symbolicReturn;
            foreach (var parameter in template.Parameters)
            {
                var slot = proofBinder.AddVariable(parameter.Name, parameter.Resolve(symbolicTypes), parameter.IsOut,
                    template.Declaration.Span);
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
                IsNoThrow = template.IsNoThrow,
                RequiresUnsafeCaller = template.RequiresUnsafeCaller,
            };
            functions[symbolicIndex] = function;
            var proof = new BoundProgram(functions.ToImmutable(), -1) {
                Structures = [.. proofBinder._structs],
                Classes = [.. proofBinder._classes],
                RuntimePolicy = _runtimePolicy,
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
        }
        return true;
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

    private static bool TryProtocolApplication(SyntaxNode type, out string head, out SyntaxNode[] arguments)
    {
        arguments = [.. type.Children.OfType<SyntaxNode>()];
        var tokens = type.Children.OfType<SyntaxTokenElement>().Select(static child => child.Token).ToArray();
        var open = Array.FindIndex(tokens, static token => token.Kind == TokenKind.Less);
        head = open > 0 ? string.Concat(tokens.Take(open).Select(static token => token.Name)) : "";
        return type.Kind == SyntaxKind.TypeName && open > 0 && arguments.Length is 1 or 2 or 3
            && tokens[^1].Kind == TokenKind.Greater
            && NameText(type) == head + "<" + string.Join(",", arguments.Select(NameText)) + ">";
    }

    private static bool MatchesProtocolArguments(StaticProtocol protocol, SyntaxNode[] arguments, string type,
        string? qualified = null)
    {
        var operandCount = protocol.Key.Arity - (protocol.HasSelf ? 1 : 0);
        return arguments.Length == operandCount + 1
            && arguments.Take(operandCount).All(argument =>
                NameText(argument) == type || NameText(argument) == qualified)
            && (protocol.Key.Arity == 1 || (IsComparisonOperator(protocol.Key.Operator)
                ? NameText(arguments[^1]) == "bool"
                : NameText(arguments[^1]) == type || NameText(arguments[^1]) == qualified));
    }
}
