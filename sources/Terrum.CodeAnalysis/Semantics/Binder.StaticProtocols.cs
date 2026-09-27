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
    private const int MaximumStaticSpecializations = 32;
    private readonly Dictionary<string, StaticProtocol> _protocols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GenericTemplate> _templates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _conformances = new(StringComparer.Ordinal);

    private sealed record StaticProtocol(string Name, TokenKind Operator, bool IsNoThrow, SyntaxNode Declaration,
        SyntaxTree Tree);
    internal sealed record GenericParameter(string Name, PrimitiveType? ScalarType, bool IsOut);
    internal sealed record GenericTemplate(string Name, string TypeParameter, PrimitiveType? ScalarReturnType,
        ImmutableArray<GenericParameter> Parameters, ImmutableDictionary<TokenKind, string> Operations, SyntaxNode Body, SyntaxNode Declaration,
        SyntaxTree Tree, bool IsNoThrow);
    internal sealed record GenericInstantiation(GenericTemplate Template, PrimitiveType Type);

    private static string OperatorName(TokenKind kind)
    {
        if (kind == TokenKind.Plus)
        {
            return "op_Addition";
        }
        if (kind == TokenKind.Star)
        {
            return "op_Multiply";
        }
        throw new ArgumentOutOfRangeException(nameof(kind));
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
                    element is SyntaxTokenElement { Token.Kind: TokenKind.Identifier })).Token.Text;
                var qualified = Qualify(name);
                var parameters = node.Children.OfType<SyntaxNode>()
                    .FirstOrDefault(static child => child.Kind == SyntaxKind.TypeParameterList);
                var variables = parameters?.Children.OfType<SyntaxTokenElement>()
                    .Where(static child => child.Token.Kind == TokenKind.Identifier)
                    .Select(static child => child.Token.Text).ToArray() ?? [];
                if (variables.Length != 3 || variables.Distinct(StringComparer.Ordinal).Count() != 3
                    || node.Children.OfType<SyntaxTokenElement>().Any(static child =>
                        child.Token.Kind is TokenKind.PublicKeyword or TokenKind.PrivateKeyword
                            or TokenKind.StaticKeyword or TokenKind.MutKeyword or TokenKind.UniqueKeyword))
                {
                    Report("TRM3001", "Local static operator interfaces require three distinct type parameters and internal visibility.", node.Span);
                    continue;
                }
                var members = node.Children.OfType<SyntaxNode>()
                    .Where(static child => child.Kind == SyntaxKind.OperatorDeclaration).ToArray();
                if (members.Length != 1 || node.Children.OfType<SyntaxNode>()
                    .Any(static child => child.Kind is not (SyntaxKind.TypeParameterList or SyntaxKind.OperatorDeclaration)))
                {
                    Report("TRM3001", "A local static operator interface must declare exactly one operator.", node.Span);
                    continue;
                }
                var member = members[0];
                var op = member.Children.OfType<SyntaxTokenElement>()
                    .FirstOrDefault(static child => child.Token.Kind is TokenKind.Plus or TokenKind.Star)?.Token.Kind;
                var signature = member.Children.OfType<SyntaxNode>()
                    .Where(static child => child.Kind != SyntaxKind.Attribute).ToArray();
                var formals = signature.FirstOrDefault(static child => child.Kind == SyntaxKind.ParameterList);
                var arguments = formals?.Children.OfType<SyntaxNode>().ToArray() ?? [];
                var noThrow = member.Children.OfType<SyntaxNode>().Any(static child =>
                    child.Kind == SyntaxKind.Attribute && NameText(child) == "[NoThrow]");
                var noThrowCount = member.Children.OfType<SyntaxNode>().Count(static child =>
                    child.Kind == SyntaxKind.Attribute && NameText(child) == "[NoThrow]");
                if (op is not (TokenKind.Plus or TokenKind.Star) || signature.Length != 2
                    || signature[0].Kind != SyntaxKind.TypeName
                    || NameText(signature[0]) != variables[2]
                    || arguments.Length != 2
                    || arguments[0].Children is not [SyntaxNode first, SyntaxTokenElement]
                    || arguments[1].Children is not [SyntaxNode second, SyntaxTokenElement]
                    || NameText(first) != variables[0] || NameText(second) != variables[1]
                    || member.Children[^1] is not SyntaxTokenElement { Token.Kind: TokenKind.Semicolon }
                    || member.Children.OfType<SyntaxTokenElement>().Count(static child =>
                        child.Token.Kind == TokenKind.StaticKeyword) != 1
                    || member.Children.OfType<SyntaxTokenElement>().Count(static child =>
                        child.Token.Kind == TokenKind.AbstractKeyword) != 1
                    || member.Children.OfType<SyntaxTokenElement>().Any(static child =>
                        child.Token.Kind is TokenKind.PrivateKeyword or TokenKind.PublicKeyword
                            or TokenKind.InternalKeyword or TokenKind.MutKeyword or TokenKind.UniqueKeyword)
                    || member.Children.OfType<SyntaxNode>().Any(child =>
                        child.Kind == SyntaxKind.Attribute && NameText(child) != "[NoThrow]")
                    || noThrowCount > 1)
                {
                    Report("TRM3001", "Static operator protocol signatures must match (left, right) -> result and end in ';'.", member.Span);
                    continue;
                }
                if (!_protocols.TryAdd(qualified, new StaticProtocol(qualified, op.Value, noThrow, node, tree)))
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
            var witnesses = new HashSet<string>(StringComparer.Ordinal);
            if (!_structs[index].IsInternal)
            {
                Report("TRM3007", "Static protocol witnesses are compilation-local and require an internal struct.", declaration.Span);
            }
            foreach (var name in list.Children.OfType<SyntaxNode>())
            {
                var shape = TryProtocolApplication(name, out var head, out var parts);
                var protocolName = shape ? FindProtocol(head) : null;
                if (protocolName is null
                    || parts.Any(part => NameText(part) != StructName(declaration).Text
                        && NameText(part) != typeName)
                    || !witnesses.Add(protocolName))
                {
                    Report("TRM3007", "Conformance requires a distinct declared local protocol with three exact self types.", name.Span);
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
            foreach (var protocolName in protocols)
            {
                var protocol = _protocols[protocolName];
                var operation = name + "." + OperatorName(protocol.Operator);
                if (!_functions.TryGetValue(operation, out var index))
                {
                    SetContext(protocol.Tree);
                    Report("TRM3007", $"'{name}' promises '{protocolName}' but has no matching operator.", protocol.Declaration.Span);
                    continue;
                }
                var signature = _signatures[index];
                if (signature.ReturnType != type || signature.Parameters.Length != 2
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

    private void DeclareGeneric(SyntaxNode declaration)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var children = declaration.Children.OfType<SyntaxNode>().ToArray();
        var typeParameters = children.First(static child => child.Kind == SyntaxKind.TypeParameterList);
        var typeNames = typeParameters.Children.OfType<SyntaxTokenElement>()
            .Where(static child => child.Token.Kind == TokenKind.Identifier)
            .Select(static child => child.Token.Text).ToArray();
        var returnType = children.FirstOrDefault(static child => child.Kind == SyntaxKind.TypeName);
        var returnName = returnType is null ? "" : NameText(returnType);
        var scalarReturn = GenericScalarType(returnName);
        var parameters = children.FirstOrDefault(static child => child.Kind == SyntaxKind.ParameterList);
        var body = children.LastOrDefault(static child => child.Kind == SyntaxKind.Block);
        var nameToken = ((SyntaxTokenElement)declaration.Children.First(element =>
            element is SyntaxTokenElement { Token.Kind: TokenKind.Identifier })).Token;
        var qualified = Qualify(nameToken.Text);
        var arguments = parameters?.Children.OfType<SyntaxNode>().ToArray() ?? [];
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        var templateParameters = ImmutableArray.CreateBuilder<GenericParameter>(arguments.Length);
        var operations = ImmutableDictionary.CreateBuilder<TokenKind, string>();
        var copy = false;
        var noThrow = declaration.Children.OfType<SyntaxNode>()
            .Any(static node => node.Kind == SyntaxKind.Attribute && NameText(node) == "[NoThrow]");
        var valid = typeNames.Length == 1 && returnType is not null && body is not null
            && (returnName == typeNames[0] || scalarReturn is not null)
            && arguments.Length is >= 1 and <= 8
            && declaration.Children.OfType<SyntaxTokenElement>().All(static element =>
                element.Token.Kind is not (TokenKind.PublicKeyword or TokenKind.PrivateKeyword
                    or TokenKind.StaticKeyword or TokenKind.MutKeyword or TokenKind.UniqueKeyword))
            && declaration.Children.OfType<SyntaxNode>()
                .All(static node => node.Kind != SyntaxKind.Attribute || NameText(node) == "[NoThrow]")
            && declaration.Children.OfType<SyntaxNode>().Count(static node =>
                node.Kind == SyntaxKind.Attribute) <= 1;
        foreach (var argument in arguments)
        {
            var isOut = argument.Children is [SyntaxTokenElement { Token.Kind: TokenKind.OutKeyword },
                SyntaxNode, SyntaxTokenElement];
            if (argument.Kind != SyntaxKind.Parameter
                || (argument.Children is not [SyntaxNode, SyntaxTokenElement] && !isOut)
                || argument.Children[^2] is not SyntaxNode argumentType
                || argument.Children[^1] is not SyntaxTokenElement argumentName
                || argumentType.Kind != SyntaxKind.TypeName
                || typeNames.Length != 1
                || !parameterNames.Add(argumentName.Token.Text))
            {
                valid = false;
            }
            else
            {
                var typeName = NameText(argumentType);
                var scalar = GenericScalarType(typeName);
                if (typeName != typeNames[0] && scalar is null)
                {
                    valid = false;
                }
                templateParameters.Add(new GenericParameter(argumentName.Token.Text, typeName == typeNames[0]
                    ? null : scalar, isOut));
            }
        }
        foreach (var constraint in children.Where(static child => child.Kind == SyntaxKind.ConstraintClause))
        {
            if (typeNames.Length != 1 || constraint.Children[1] is not SyntaxTokenElement formal
                || formal.Token.Text != typeNames[0])
            {
                valid = false;
                continue;
            }
            foreach (var item in constraint.Children.OfType<SyntaxNode>())
            {
                if (item.Kind == SyntaxKind.Attribute)
                {
                    if (NameText(item) != "[Copy]" || copy)
                    {
                        valid = false;
                    }
                    copy = true;
                    continue;
                }
                var shape = TryProtocolApplication(item, out var head, out var parts);
                var protocol = shape ? FindProtocol(head) : null;
                if (protocol is null || parts.Any(part => NameText(part) != typeNames[0])
                    || operations.ContainsKey(_protocols[protocol].Operator))
                {
                    valid = false;
                }
                else
                {
                    operations.Add(_protocols[protocol].Operator, protocol);
                }
            }
        }
        if (body is null || !copy
            || (templateParameters.Any(static parameter => parameter.IsOut) && scalarReturn != PrimitiveType.Bool)
            || !templateParameters.Any(static parameter => parameter.ScalarType is null)
            || (noThrow && operations.Values.Any(protocol => !_protocols[protocol].IsNoThrow)))
        {
            valid = false;
        }
        if (!valid || _templates.Count >= MaximumStaticSpecializations)
        {
            Report("TRM3007", "Generic function requires one local Copy type parameter, supported value or bool/out parameters, and explicit constraints for any operators.", declaration.Span);
            return;
        }
        if (!_templates.TryAdd(qualified, new GenericTemplate(qualified, typeNames[0], scalarReturn,
            templateParameters.ToImmutable(), operations.ToImmutable(), body!, declaration, _tree, noThrow)))
        {
            Report("TRM3003", $"Duplicate generic function '{qualified}'.", declaration.Span);
        }
    }

    private PrimitiveType? GenericCallType(SyntaxNode callee, SyntaxNode? callArguments = null)
    {
        var name = ((SyntaxTokenElement)callee.Children[0]).Token.Text;
        if (TryFindVariable(name, out _) || FindGenericTemplate(name) is not GenericTemplate template)
        {
            return null;
        }
        if (callee.Kind == SyntaxKind.GenericNameExpression)
        {
            var arguments = ((SyntaxNode)callee.Children[1]).Children.OfType<SyntaxNode>().ToArray();
            return arguments.Length == 1 && TryResolveGenericArgument(NameText(arguments[0]), out var type)
                ? template.ScalarReturnType ?? type : null;
        }
        if (template.ScalarReturnType is PrimitiveType scalar)
        {
            return scalar;
        }
        var inferred = callArguments is null ? null
            : InferGenericType(template, callArguments, null, out _);
        return inferred is PrimitiveType concrete && IsStruct(concrete)
            ? concrete : null;
    }

    private PrimitiveType? InferGenericType(GenericTemplate template, SyntaxNode arguments,
        PrimitiveType? expected, out bool conflictingArguments)
    {
        conflictingArguments = false;
        PrimitiveType? inferred = null;
        var supplied = arguments.Children.OfType<SyntaxNode>().ToArray();
        for (var index = 0; index < Math.Min(supplied.Length, template.Parameters.Length); index++)
        {
            if (template.Parameters[index].ScalarType is not null)
            {
                continue;
            }
            var argument = supplied[index];
            PrimitiveType? candidate = null;
            if (argument.Children is [SyntaxNode value])
            {
                candidate = KnownType(value);
            }
            else if (template.Parameters[index].IsOut
                && argument.Children is [SyntaxTokenElement { Token.Kind: TokenKind.OutKeyword },
                    SyntaxNode { Kind: SyntaxKind.NameExpression } output])
            {
                candidate = KnownType(output);
            }
            if (candidate is not PrimitiveType typed)
            {
                continue;
            }
            if (inferred is PrimitiveType established && established != typed)
            {
                conflictingArguments = true;
                return null;
            }
            inferred = typed;
        }
        return inferred ?? (template.ScalarReturnType is null ? expected : null);
    }

    private GenericTemplate? FindGenericTemplate(string name)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (_templates.ContainsKey(Qualify(name)))
        {
            _ = names.Add(Qualify(name));
        }
        foreach (var imported in _usings)
        {
            var qualified = imported + "." + name;
            if (_templates.ContainsKey(qualified))
            {
                _ = names.Add(qualified);
            }
        }
        return names.Count == 1 ? _templates[names.Single()] : null;
    }

    private int InstantiateGeneric(SyntaxNode callee, SyntaxNode? callArguments = null, PrimitiveType? expected = null)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var name = ((SyntaxTokenElement)callee.Children[0]).Token.Text;
        if (TryFindVariable(name, out _))
        {
            Report("TRM3011", $"'{name}' is a variable, not a function.", callee.Span);
            return -1;
        }
        var template = FindGenericTemplate(name);
        var explicitArguments = callee.Kind == SyntaxKind.GenericNameExpression
            ? ((SyntaxNode)callee.Children[1]).Children.OfType<SyntaxNode>().ToArray() : [];
        if (_genericContext is GenericInstantiation caller && callArguments is null
            && (explicitArguments.Length != 1
                || NameText(explicitArguments[0]) != caller.Template.TypeParameter))
        {
            Report("TRM3007", "A constrained helper call requires the current type parameter and its declared protocol witnesses.",
                callee.Span);
            return -1;
        }
        PrimitiveType? concrete = null;
        if (template is not null)
        {
            if (callArguments is null)
            {
                if (explicitArguments.Length == 1
                    && TryResolveGenericArgument(NameText(explicitArguments[0]), out var resolved))
                {
                    concrete = resolved;
                }
            }
            else
            {
                concrete = InferGenericType(template, callArguments, expected, out var conflicting);
                if (conflicting)
                {
                    Report("TRM3007", "Typed arguments establish conflicting generic types.", callArguments.Span);
                    return -1;
                }
            }
        }
        if (template is null || concrete is not PrimitiveType type || !IsStruct(type))
        {
            Report("TRM3011", "A generic call requires one unambiguous Copy struct type from explicit arguments, typed arguments, or its result context.",
                callee.Span);
            return -1;
        }
        if (_genericContext is GenericInstantiation enclosing
            && (type != enclosing.Type || template.Operations.Values.Any(protocol =>
                !enclosing.Template.Operations.ContainsValue(protocol))))
        {
            Report("TRM3007", "A constrained helper call requires the current type parameter and its declared protocol witnesses.",
                callee.Span);
            return -1;
        }
        var typeName = _structs[(int)type - 100].Name;
        var instantiated = template.Name + "<" + typeName + ">";
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
        if (_structs[(int)type - 100].IsUnique
            || (template.Operations.Count != 0
                && (!_conformances.TryGetValue(typeName, out var witnesses)
                    || template.Operations.Values.Any(protocol => !witnesses.Contains(protocol)))))
        {
            Report("TRM3007", $"'{typeName}' lacks the declared Copy static protocol witnesses.", callee.Span);
            return -1;
        }
        var parameters = template.Parameters.Select(parameter => new Parameter(parameter.Name, parameter.ScalarType ?? type,
            callee.Span, parameter.IsOut)).ToImmutableArray();
        var index = _signatures.Count;
        _functions.Add(instantiated, index);
        _signatures.Add(new Signature(instantiated, template.ScalarReturnType ?? type, parameters,
            template.Declaration.Children.OfType<SyntaxNode>().Last(static node => node.Kind == SyntaxKind.Block),
            template.Declaration, template.Tree, null, false, false, true, false, true, template.IsNoThrow, false) {
            Generic = new GenericInstantiation(template, type),
        });
        return index;
    }

    private BoundBlock BindGenericBody(GenericInstantiation instantiation)
    {
        var template = instantiation.Template;
        var previous = _genericContext;
        _genericContext = instantiation;
        try
        {
            return BindBlock(template.Body);
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
            proofBinder._contexts.Add(template.Tree, _contexts[template.Tree]);
            proofBinder.SetContext(template.Tree);
            var probeName = "$T";
            var probeType = (PrimitiveType)100;
            var probe = new BoundStruct(probeName,
                [new BoundStructField("$opaque", PrimitiveType.Int32, 0, 4) { IsPrivate = true }], 4) {
                IsInternal = true,
            };
            var functions = ImmutableArray.CreateBuilder<BoundFunction>();
            proofBinder._structs.Add(probe);
            foreach (var (operation, protocolName) in template.Operations)
            {
                var name = probeName + "." + OperatorName(operation);
                var operatorSpan = template.Declaration.Span;
                var variables = ImmutableArray.Create(
                    new BoundVariable("left", probeType, false, operatorSpan),
                    new BoundVariable("right", probeType, false, operatorSpan));
                var external = new BoundFunction(name, probeType, variables, 2,
                    new BoundBlock([], operatorSpan), operatorSpan) {
                    IsExternal = true,
                    IsStatic = true,
                    IsNoThrow = _protocols[protocolName].IsNoThrow,
                    DeclaringStruct = probeName,
                };
                proofBinder._functions.Add(name, proofBinder._signatures.Count);
                proofBinder._signatures.Add(new Signature(name, probeType, [
                    new Parameter("left", probeType, operatorSpan, false),
                    new Parameter("right", probeType, operatorSpan, false),
                ], null, null, template.Tree, probeName, false, false, false, false, true,
                    external.IsNoThrow, false, external) { IsOperator = true });
                functions.Add(external);
            }

            // Each definition is proved independently. Calls use only the callee's declared
            // signature/effects; its body cannot lend extra capabilities to the caller.
            foreach (var helper in _templates.Values)
            {
                proofBinder._templates.Add(helper.Name, helper);
                var name = helper.Name + "<" + probeName + ">";
                var parameters = helper.Parameters.Select(parameter => new Parameter(parameter.Name,
                    parameter.ScalarType ?? probeType, helper.Declaration.Span, parameter.IsOut)).ToImmutableArray();
                var variables = parameters.Select(parameter => new BoundVariable(parameter.Name,
                    parameter.Type, parameter.IsOut, parameter.Span) { IsOut = parameter.IsOut }).ToImmutableArray();
                var returnType = helper.ScalarReturnType ?? probeType;
                var external = new BoundFunction(name, returnType, variables, parameters.Length,
                    new BoundBlock([], helper.Declaration.Span), helper.Declaration.Span) {
                    IsExternal = true,
                    IsNoThrow = helper.IsNoThrow,
                };
                proofBinder._functions.Add(name, proofBinder._signatures.Count);
                proofBinder._signatures.Add(new Signature(name, returnType, parameters, null, null,
                    helper.Tree, null, false, false, true, false, true, helper.IsNoThrow, false, external));
                functions.Add(external);
            }

            proofBinder._scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
            proofBinder._returnType = template.ScalarReturnType ?? probeType;
            foreach (var parameter in template.Parameters)
            {
                var slot = proofBinder.AddVariable(parameter.Name, parameter.ScalarType ?? probeType, parameter.IsOut,
                    template.Declaration.Span);
                if (parameter.IsOut)
                {
                    proofBinder._variables[slot] = proofBinder._variables[slot] with { IsOut = true };
                }
            }
            var body = proofBinder.BindGenericBody(new GenericInstantiation(template, probeType));
            if (proofBinder._diagnostics.Count != 0)
            {
                foreach (var diagnostic in proofBinder._diagnostics)
                {
                    _diagnostics.Add(diagnostic);
                    _sourceDiagnostics.Add(new SourceDiagnostic(template.Tree, diagnostic));
                }
                Report("TRM3007", "Generic function requires a body valid using only its declared Copy and operator constraints.",
                    template.Declaration.Span);
                return false;
            }

            var function = new BoundFunction(template.Name + "<$T>", template.ScalarReturnType ?? probeType,
                [.. proofBinder._variables],
                template.Parameters.Length, body, template.Declaration.Span) {
                IsNoThrow = template.IsNoThrow,
            };
            functions[proofBinder._functions[function.Name]] = function;
            var proof = new BoundProgram(functions.ToImmutable(), -1) {
                Structures = [probe],
                RuntimePolicy = _runtimePolicy,
            };
            if (!SemanticVerifier.TryVerifySource(proof, out _, out var span, out var limit, out var effect))
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

    private bool TryResolveGenericArgument(string name, out PrimitiveType type)
    {
        if (_genericContext is GenericInstantiation generic && name == generic.Template.TypeParameter)
        {
            type = generic.Type;
            return true;
        }
        if (name.Contains('.', StringComparison.Ordinal))
        {
            return _structNames.TryGetValue(name, out type);
        }
        var candidates = new HashSet<PrimitiveType>();
        if (_structNames.TryGetValue(name, out var direct))
        {
            _ = candidates.Add(direct);
        }
        if (_structNames.TryGetValue(Qualify(name), out var local))
        {
            _ = candidates.Add(local);
        }
        foreach (var imported in _usings)
        {
            if (_structNames.TryGetValue(imported + "." + name, out var candidate))
            {
                _ = candidates.Add(candidate);
            }
        }
        type = candidates.Count == 1 ? candidates.Single() : default;
        return candidates.Count == 1;
    }

    private string? FindProtocol(string name)
    {
        if (name.Contains('.', StringComparison.Ordinal))
        {
            return _protocols.ContainsKey(name) ? name : null;
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (_protocols.ContainsKey(Qualify(name)))
        {
            _ = names.Add(Qualify(name));
        }
        foreach (var imported in _usings)
        {
            var qualified = imported + "." + name;
            if (_protocols.ContainsKey(qualified))
            {
                _ = names.Add(qualified);
            }
        }
        return names.Count == 1 ? names.Single() : null;
    }

    private static bool TryProtocolApplication(SyntaxNode type, out string head, out SyntaxNode[] arguments)
    {
        arguments = [.. type.Children.OfType<SyntaxNode>()];
        var tokens = type.Children.OfType<SyntaxTokenElement>().Select(static child => child.Token).ToArray();
        var open = Array.FindIndex(tokens, static token => token.Kind == TokenKind.Less);
        head = open > 0 ? string.Concat(tokens.Take(open).Select(static token => token.Text)) : "";
        return type.Kind == SyntaxKind.TypeName && open > 0 && arguments.Length == 3
            && tokens[^1].Kind == TokenKind.Greater
            && NameText(type) == head + "<" + string.Join(",", arguments.Select(NameText)) + ">";
    }
}
