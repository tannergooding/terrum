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
    internal sealed record GenericTemplate(string Name, ImmutableArray<string> Parameters,
        ImmutableDictionary<TokenKind, string> Operations, SyntaxNode Expression, SyntaxNode Declaration,
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
        var parameters = children.FirstOrDefault(static child => child.Kind == SyntaxKind.ParameterList);
        var body = children.LastOrDefault(static child => child.Kind == SyntaxKind.Block);
        var nameToken = ((SyntaxTokenElement)declaration.Children.First(element =>
            element is SyntaxTokenElement { Token.Kind: TokenKind.Identifier })).Token;
        var qualified = Qualify(nameToken.Text);
        var arguments = parameters?.Children.OfType<SyntaxNode>().ToArray() ?? [];
        var parameterNames = ImmutableArray.CreateBuilder<string>(arguments.Length);
        var operations = ImmutableDictionary.CreateBuilder<TokenKind, string>();
        var copy = false;
        var noThrow = declaration.Children.OfType<SyntaxNode>()
            .Any(static node => node.Kind == SyntaxKind.Attribute && NameText(node) == "[NoThrow]");
        var valid = typeNames.Length == 1 && returnType is not null && body is not null
            && NameText(returnType) == typeNames[0]
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
            if (argument.Kind != SyntaxKind.Parameter
                || argument.Children is not [SyntaxNode argumentType, SyntaxTokenElement argumentName]
                || argumentType.Kind != SyntaxKind.TypeName
                || typeNames.Length != 1 || NameText(argumentType) != typeNames[0]
                || parameterNames.Contains(argumentName.Token.Text))
            {
                valid = false;
            }
            else
            {
                parameterNames.Add(argumentName.Token.Text);
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
        var expression = TemplateReturnExpression(body);
        if (expression is null || !copy || operations.Count == 0
            || !CheckTemplateExpression(expression, parameterNames.ToImmutableHashSet(StringComparer.Ordinal),
                [.. operations.Keys], 0)
            || (noThrow && operations.Values.Any(protocol => !_protocols[protocol].IsNoThrow)))
        {
            valid = false;
        }
        if (!valid || _templates.Count >= MaximumStaticSpecializations)
        {
            Report("TRM3007", "Generic function requires one local Copy type parameter, declared operator constraints, and a single constrained return expression.", declaration.Span);
            return;
        }
        if (!_templates.TryAdd(qualified, new GenericTemplate(qualified,
            parameterNames.ToImmutable(), operations.ToImmutable(), expression!, declaration, _tree, noThrow)))
        {
            Report("TRM3003", $"Duplicate generic function '{qualified}'.", declaration.Span);
        }
    }

    private static SyntaxNode? TemplateReturnExpression(SyntaxNode? body)
    {
        if (body?.Children is [SyntaxTokenElement, SyntaxNode statement, SyntaxTokenElement]
            && statement.Kind == SyntaxKind.ReturnStatement
            && statement.Children is [SyntaxTokenElement, SyntaxNode returned, SyntaxTokenElement])
        {
            return returned;
        }
        return null;
    }

    private static bool CheckTemplateExpression(SyntaxNode expression, ImmutableHashSet<string> parameters,
        ImmutableHashSet<TokenKind> operators, int depth)
    {
        if (depth >= MaximumSemanticDepth)
        {
            return false;
        }
        if (expression.Kind == SyntaxKind.NameExpression)
        {
            return parameters.Contains(((SyntaxTokenElement)expression.Children[0]).Token.Text);
        }
        if (expression.Kind == SyntaxKind.ParenthesizedExpression)
        {
            return CheckTemplateExpression((SyntaxNode)expression.Children[1],
                parameters, operators, depth + 1);
        }
        return expression.Kind == SyntaxKind.BinaryExpression
                && operators.Contains(((SyntaxTokenElement)expression.Children[1]).Token.Kind)
                && CheckTemplateExpression((SyntaxNode)expression.Children[0], parameters, operators, depth + 1)
                && CheckTemplateExpression((SyntaxNode)expression.Children[2], parameters, operators, depth + 1);
    }

    private PrimitiveType? GenericCallType(SyntaxNode callee)
    {
        var arguments = ((SyntaxNode)callee.Children[1]).Children.OfType<SyntaxNode>().ToArray();
        return arguments.Length == 1 && TryResolveGenericArgument(NameText(arguments[0]), out var type) ? type : null;
    }

    private int InstantiateGeneric(SyntaxNode callee)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var name = ((SyntaxTokenElement)callee.Children[0]).Token.Text;
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
        var template = names.Count == 1 ? _templates[names.Single()] : null;
        var arguments = ((SyntaxNode)callee.Children[1]).Children.OfType<SyntaxNode>().ToArray();
        if (template is null || arguments.Length != 1
            || !TryResolveGenericArgument(NameText(arguments[0]), out var concrete)
            || !IsStruct(concrete))
        {
            Report("TRM3011", "A generic call requires one explicit, unambiguous concrete struct argument.", callee.Span);
            return -1;
        }
        var typeName = _structs[(int)concrete - 100].Name;
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
        if (_structs[(int)concrete - 100].IsUnique
            || !_conformances.TryGetValue(typeName, out var witnesses)
            || template.Operations.Values.Any(protocol => !witnesses.Contains(protocol)))
        {
            Report("TRM3007", $"'{typeName}' lacks the declared Copy static protocol witnesses.", callee.Span);
            return -1;
        }
        var parameters = template.Parameters.Select(parameter => new Parameter(parameter, concrete,
            callee.Span, false)).ToImmutableArray();
        var index = _signatures.Count;
        _functions.Add(instantiated, index);
        _signatures.Add(new Signature(instantiated, concrete, parameters,
            template.Declaration.Children.OfType<SyntaxNode>().Last(static node => node.Kind == SyntaxKind.Block),
            template.Declaration, template.Tree, null, false, false, true, false, true, template.IsNoThrow, false) {
            Generic = new GenericInstantiation(template, concrete),
        });
        return index;
    }

    private BoundBlock BindGenericBody(GenericInstantiation instantiation)
    {
        var template = instantiation.Template;
        var result = BindTemplateExpression(template.Expression, instantiation.Type, template);
        var returned = new BoundReturn(result, template.Expression.Span);
        return new BoundBlock([returned], template.Declaration.Span);
    }

    private BoundExpression BindTemplateExpression(SyntaxNode expression, PrimitiveType type, GenericTemplate template)
    {
        if (expression.Kind == SyntaxKind.NameExpression)
        {
            var name = ((SyntaxTokenElement)expression.Children[0]).Token.Text;
            var slot = template.Parameters.IndexOf(name);
            return new BoundRead(slot, type, expression.Span);
        }
        if (expression.Kind == SyntaxKind.ParenthesizedExpression)
        {
            return BindTemplateExpression((SyntaxNode)expression.Children[1], type, template);
        }
        if (expression.Kind == SyntaxKind.BinaryExpression)
        {
            var operation = ((SyntaxTokenElement)expression.Children[1]).Token.Kind;
            var left = BindTemplateExpression((SyntaxNode)expression.Children[0], type, template);
            var right = BindTemplateExpression((SyntaxNode)expression.Children[2], type, template);
            var name = _structs[(int)type - 100].Name + "." + OperatorName(operation);
            if (!_functions.TryGetValue(name, out var index))
            {
                Report("TRM3007", $"No concrete operator '{name}' fulfills the declared constraint.", expression.Span);
                return ErrorExpression(expression.Span);
            }
            return new BoundCall(index, [left, right], type, expression.Span);
        }
        throw new InvalidOperationException("Unvalidated generic expression.");
    }

    private string Qualify(string name) => string.IsNullOrEmpty(_namespace) ? name : _namespace + "." + name;

    private bool TryResolveGenericArgument(string name, out PrimitiveType type)
    {
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
