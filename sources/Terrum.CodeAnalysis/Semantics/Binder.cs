// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed class Binder
{
    private const int MaximumSemanticDepth = 128;
    private readonly SyntaxTree _tree;
    private readonly CancellationToken _cancellationToken;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly List<Signature> _signatures = [];
    private readonly Dictionary<string, int> _functions = new(StringComparer.Ordinal);
    private readonly List<BoundVariable> _variables = [];
    private readonly List<Dictionary<string, int>> _scopes = [];
    private PrimitiveType _returnType;

    internal Binder(SyntaxTree tree, CancellationToken cancellationToken)
    {
        _tree = tree;
        _cancellationToken = cancellationToken;
    }

    internal IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    internal BoundProgram? Bind()
    {
        // Parser depth limits do not bound left-associated flat expression trees.
        var pending = new Stack<(SyntaxNode Node, int Depth)>();
        pending.Push((_tree.Root, 0));

        while (pending.TryPop(out var item))
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (item.Depth >= MaximumSemanticDepth)
            {
                Report("TRM3015", $"Semantic nesting exceeds {MaximumSemanticDepth} nodes.", item.Node.Span);
                return null;
            }

            foreach (var child in item.Node.Children)
            {
                if (child is SyntaxNode node)
                {
                    pending.Push((node, item.Depth + 1));
                }
            }
        }

        foreach (var child in _tree.Root.Children)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (child is not SyntaxNode node)
            {
                continue;
            }

            if (node.Kind != SyntaxKind.FunctionDeclaration)
            {
                Report("TRM3001", "Only top-level function declarations are supported.", node.Span);
                continue;
            }

            Declare(node);
        }

        var entryPoint = -1;

        for (var index = 0; index < _signatures.Count; index++)
        {
            var signature = _signatures[index];
            if (signature.Name == "Main" && signature.Parameters.Length == 0 &&
                signature.ReturnType is PrimitiveType.Int32 or PrimitiveType.Void)
            {
                entryPoint = index;
            }
        }

        if (entryPoint < 0)
        {
            Report("TRM3002", "A parameterless int32 or void Main function is required.", _tree.Root.Span);
        }

        var bound = ImmutableArray.CreateBuilder<BoundFunction>(_signatures.Count);
        foreach (var signature in _signatures)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _variables.Clear();
            _scopes.Clear();
            _scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
            _returnType = signature.ReturnType;

            foreach (var parameter in signature.Parameters)
            {
                _ = AddVariable(parameter.Name, parameter.Type, false, parameter.Span);
            }

            var body = BindBlock(signature.Body);
            if (_returnType == PrimitiveType.Void && !AlwaysReturns(body))
            {
                var end = new TextSpan(signature.Body.Span.End, 0);
                body = body with { Statements = body.Statements.Add(new BoundReturn(null, end)) };
            }
            else if (_returnType != PrimitiveType.Void && !AlwaysReturns(body))
            {
                Report("TRM3012", $"Not all paths in '{signature.Name}' return a value.", signature.Body.Span);
            }

            bound.Add(new BoundFunction(signature.Name, signature.ReturnType, [.. _variables],
                signature.Parameters.Length, body, signature.Declaration.Span));
        }

        return _diagnostics.Count == 0 ? new BoundProgram(bound.ToImmutable(), entryPoint) : null;
    }

    private void Declare(SyntaxNode declaration)
    {
        for (var index = 0; index < declaration.Children.Length - 4; index++)
        {
            Report("TRM3001", "Function modifiers are not supported.", declaration.Children[index].Span);
        }

        var returnNode = (SyntaxNode)declaration.Children[^4];
        var nameToken = ((SyntaxTokenElement)declaration.Children[^3]).Token;
        var parameters = (SyntaxNode)declaration.Children[^2];
        var body = (SyntaxNode)declaration.Children[^1];
        var returnType = BindType(returnNode, allowVoid: true);
        var declared = ImmutableArray.CreateBuilder<Parameter>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var child in parameters.Children)
        {
            if (child is not SyntaxNode parameter)
            {
                continue;
            }

            var typeNode = (SyntaxNode)parameter.Children[^2];
            var token = ((SyntaxTokenElement)parameter.Children[^1]).Token;
            var type = BindType(typeNode, allowVoid: false);

            if (parameter.Children.Length != 2)
            {
                Report("TRM3001", "Passing modes are not supported.", parameter.Span);
            }

            if (!names.Add(token.Text))
            {
                Report("TRM3004", $"Duplicate parameter '{token.Text}'.", token.Span);
            }

            declared.Add(new Parameter(token.Text, type, token.Span));
        }

        if (!_functions.TryAdd(nameToken.Text, _signatures.Count))
        {
            Report("TRM3003", $"Duplicate function '{nameToken.Text}'; overloads are not supported.", nameToken.Span);
        }

        _signatures.Add(new Signature(nameToken.Text, returnType, declared.ToImmutable(), body, declaration));
    }

    private PrimitiveType BindType(SyntaxNode node, bool allowVoid)
    {
        var name = _tree.Source.GetSubText(node.Span);
        var type = name switch {
            "int32" => PrimitiveType.Int32,
            "bool" => PrimitiveType.Bool,
            "void" when allowVoid => PrimitiveType.Void,
            _ => (PrimitiveType?)null,
        };

        if (type is null)
        {
            Report("TRM3005", $"Type '{name}' is not supported here; only int32, bool and function-result void are supported.", node.Span);
        }

        return type ?? PrimitiveType.Int32;
    }

    private BoundBlock BindBlock(SyntaxNode block)
    {
        _scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
        var statements = ImmutableArray.CreateBuilder<BoundStatement>();

        foreach (var child in block.Children)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (child is SyntaxNode statement)
            {
                statements.Add(BindStatement(statement));
            }
        }

        _scopes.RemoveAt(_scopes.Count - 1);
        return new BoundBlock(statements.ToImmutable(), block.Span);
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Unsupported syntax kinds are rejected by the default branch.")]
    private BoundStatement BindStatement(SyntaxNode node)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        switch (node.Kind)
        {
            case SyntaxKind.Block:
                return BindBlock(node);

            case SyntaxKind.VariableDeclaration:
            {
                var isMutable = node.Children[0] is SyntaxTokenElement first && first.Token.Kind == TokenKind.MutKeyword;
                var offset = isMutable ? 1 : 0;
                var typeElement = node.Children[offset];
                var name = ((SyntaxTokenElement)node.Children[offset + 1]).Token;
                var initializerNode = (SyntaxNode)node.Children[offset + 3];
                var declaredType = typeElement is SyntaxNode typeNode ? BindType(typeNode, allowVoid: false) : (PrimitiveType?)null;
                var initializer = BindExpression(initializerNode, declaredType);

                if (declaredType is null && initializer.Type == PrimitiveType.Void)
                {
                    Report("TRM3005", "Cannot infer a variable's type from a void expression.", initializerNode.Span);
                }

                // Locals enter scope only after their initializer, including in nested blocks.
                var slot = AddVariable(name.Text, declaredType ?? initializer.Type, isMutable, name.Span);
                return new BoundLocal(slot, initializer, node.Span);
            }

            case SyntaxKind.AssignmentStatement:
            {
                var target = (SyntaxNode)node.Children[0];
                var valueNode = (SyntaxNode)node.Children[2];

                if (target.Kind != SyntaxKind.NameExpression)
                {
                    Report("TRM3008", "Assignment requires a mutable local variable.", target.Span);
                    _ = BindExpression(valueNode, null);
                    return new BoundBlock([], node.Span);
                }

                var token = ((SyntaxTokenElement)target.Children[0]).Token;
                if (!TryFindVariable(token.Text, out var slot))
                {
                    Report("TRM3006", $"Unknown or not-yet-declared variable '{token.Text}'.", token.Span);
                    _ = BindExpression(valueNode, null);
                    return new BoundBlock([], node.Span);
                }

                if (!_variables[slot].IsMutable)
                {
                    Report("TRM3008", $"Variable '{token.Text}' is immutable.", token.Span);
                }

                return new BoundAssignment(slot, BindExpression(valueNode, _variables[slot].Type), node.Span);
            }

            case SyntaxKind.ExpressionStatement:
            {
                var expressionNode = (SyntaxNode)node.Children[0];
                if (expressionNode.Kind != SyntaxKind.InvocationExpression)
                {
                    Report("TRM3009", "Only direct calls may be used as expression statements.", expressionNode.Span);
                }

                return new BoundExpressionStatement(BindExpression(expressionNode, null), node.Span);
            }

            case SyntaxKind.EmptyStatement:
                return new BoundBlock([], node.Span);

            case SyntaxKind.RequireStatement:
                return new BoundRequire(BindExpression((SyntaxNode)node.Children[1], PrimitiveType.Bool), node.Span);

            case SyntaxKind.ReturnStatement:
            {
                var valueNode = node.Children[1] as SyntaxNode;
                if (_returnType == PrimitiveType.Void)
                {
                    if (valueNode is not null)
                    {
                        var value = BindExpression(valueNode, PrimitiveType.Void);
                        if (value is BoundCall call)
                        {
                            return new BoundBlock(
                                [new BoundExpressionStatement(call, valueNode.Span), new BoundReturn(null, node.Span)],
                                node.Span);
                        }
                    }

                    return new BoundReturn(null, node.Span);
                }

                if (valueNode is null)
                {
                    Report("TRM3010", "A return value is required.", node.Span);
                    return new BoundReturn(null, node.Span);
                }

                return new BoundReturn(BindExpression(valueNode, _returnType), node.Span);
            }

            case SyntaxKind.IfStatement:
            {
                var conditionNode = (SyntaxNode)node.Children[2];
                var condition = BindExpression(conditionNode, PrimitiveType.Bool);
                var then = BindBlock((SyntaxNode)node.Children[4]);
                BoundStatement? otherwise = null;

                if (node.Children.Length > 5)
                {
                    var clause = (SyntaxNode)node.Children[5];
                    otherwise = BindStatement((SyntaxNode)clause.Children[1]);
                }

                return new BoundIf(condition, then, otherwise, node.Span);
            }

            case SyntaxKind.WhileStatement:
                return new BoundWhile(BindExpression((SyntaxNode)node.Children[2], PrimitiveType.Bool),
                    BindBlock((SyntaxNode)node.Children[4]), node.Span);

            default:
                Report("TRM3001", "This statement is not supported.", node.Span);
                return new BoundBlock([], node.Span);
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Unsupported syntax kinds are rejected by the default branch.")]
    private BoundExpression BindExpression(SyntaxNode node, PrimitiveType? expected)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        BoundExpression expression;

        switch (node.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                expression = BindExpression((SyntaxNode)node.Children[1], expected);
                break;

            case SyntaxKind.VoidExpression:
                expression = new BoundConstant(0, PrimitiveType.Void, node.Span);
                break;

            case SyntaxKind.LiteralExpression:
                expression = BindLiteral(((SyntaxTokenElement)node.Children[0]).Token, expected);
                break;

            case SyntaxKind.NameExpression:
            {
                var token = ((SyntaxTokenElement)node.Children[0]).Token;
                if (TryFindVariable(token.Text, out var slot))
                {
                    expression = new BoundRead(slot, _variables[slot].Type, node.Span);
                }
                else
                {
                    Report("TRM3006", $"Unknown or not-yet-declared variable '{token.Text}'.", token.Span);
                    expression = ErrorExpression(node.Span);
                }

                break;
            }

            case SyntaxKind.UnaryExpression:
            {
                var op = ((SyntaxTokenElement)node.Children[0]).Token;
                var operandNode = (SyntaxNode)node.Children[1];
                var operandType = op.Kind == TokenKind.Bang ? PrimitiveType.Bool : PrimitiveType.Int32;

                // The spelling of the negative minimum is a single checked int32 constant.
                if (op.Kind == TokenKind.Minus && operandNode.Kind == SyntaxKind.LiteralExpression &&
                    TryMinimumLiteral(((SyntaxTokenElement)operandNode.Children[0]).Token))
                {
                    var literal = ((SyntaxTokenElement)operandNode.Children[0]).Token;
                    if (!literal.Text.EndsWith("i32", StringComparison.Ordinal) && expected != PrimitiveType.Int32)
                    {
                        Report("TRM3014", "An unsuffixed integer literal requires an int32 context.", literal.Span);
                    }

                    expression = new BoundConstant(int.MinValue, PrimitiveType.Int32, node.Span);
                    break;
                }

                var operandContext = operandType == PrimitiveType.Bool ? operandType
                    : expected == PrimitiveType.Int32 || KnownType(operandNode) == PrimitiveType.Int32
                        ? PrimitiveType.Int32 : (PrimitiveType?)null;
                var operand = BindExpression(operandNode, operandContext);
                if (operand.Type != operandType)
                {
                    Report("TRM3007", $"Unary operator requires {TypeName(operandType)}.", op.Span);
                }
                if (op.Kind is not (TokenKind.Plus or TokenKind.Minus or TokenKind.Tilde or TokenKind.Bang))
                {
                    Report("TRM3001", "Unsupported unary operator.", op.Span);
                }

                expression = new BoundUnary(op.Kind, operand, operandType, node.Span);
                break;
            }

            case SyntaxKind.BinaryExpression:
                expression = BindBinary(node, expected);
                break;

            case SyntaxKind.InvocationExpression:
                expression = BindCall(node);
                break;

            default:
                Report("TRM3001", "This expression is not supported in the executable profile.", node.Span);
                expression = ErrorExpression(node.Span);
                break;
        }

        if (expected is not null && expression.Type != expected)
        {
            Report("TRM3007", $"Expected {TypeName(expected.Value)}, got {TypeName(expression.Type)}.", node.Span);
        }

        return expression;
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Unsupported operators are rejected by the default branch.")]
    private BoundBinary BindBinary(SyntaxNode node, PrimitiveType? expected)
    {
        var leftNode = (SyntaxNode)node.Children[0];
        var op = ((SyntaxTokenElement)node.Children[1]).Token;
        var rightNode = (SyntaxNode)node.Children[2];
        var leftKnown = KnownType(leftNode);
        var rightKnown = KnownType(rightNode);
        PrimitiveType? operandType;
        PrimitiveType resultType;

        switch (op.Kind)
        {
            case TokenKind.Plus:
            case TokenKind.Minus:
            case TokenKind.Star:
            case TokenKind.Slash:
            case TokenKind.Percent:
                operandType = leftKnown ?? rightKnown ?? (expected == PrimitiveType.Int32 ? PrimitiveType.Int32 : null);
                resultType = PrimitiveType.Int32;
                if (operandType != PrimitiveType.Int32)
                {
                    Report("TRM3007", "Arithmetic requires int32 operands with a known int32 context.", op.Span);
                }
                break;

            case TokenKind.Less:
            case TokenKind.LessEquals:
            case TokenKind.Greater:
            case TokenKind.GreaterEquals:
                operandType = leftKnown ?? rightKnown;
                resultType = PrimitiveType.Bool;
                if (operandType != PrimitiveType.Int32)
                {
                    Report("TRM3007", "Comparison requires int32 operands with a known int32 context.", op.Span);
                }
                break;

            case TokenKind.AmpersandAmpersand:
            case TokenKind.PipePipe:
                operandType = PrimitiveType.Bool;
                resultType = PrimitiveType.Bool;
                break;

            case TokenKind.Ampersand:
            case TokenKind.Pipe:
            case TokenKind.Caret:
                operandType = leftKnown ?? rightKnown ?? expected;
                resultType = operandType ?? PrimitiveType.Int32;
                if (operandType is not (PrimitiveType.Int32 or PrimitiveType.Bool))
                {
                    Report("TRM3007", "Bitwise operands must both be int32 or both be bool.", op.Span);
                }
                break;

            case TokenKind.EqualsEquals:
            case TokenKind.BangEquals:
                operandType = leftKnown ?? rightKnown;
                resultType = PrimitiveType.Bool;
                if (operandType is not (PrimitiveType.Int32 or PrimitiveType.Bool))
                {
                    Report("TRM3007", "Equality requires two operands of the same known int32 or bool type.", op.Span);
                }
                break;

            default:
                Report("TRM3001", "Unsupported binary operator.", op.Span);
                operandType = null;
                resultType = PrimitiveType.Int32;
                break;
        }

        var left = BindExpression(leftNode, operandType);
        var right = BindExpression(rightNode, operandType);
        if (operandType is null || left.Type != right.Type || operandType == PrimitiveType.Void)
        {
            Report("TRM3007", "Binary operands must have the same supported type.", op.Span);
        }

        return new BoundBinary(op.Kind, left, right, resultType, node.Span);
    }

    private BoundCall BindCall(SyntaxNode node)
    {
        var callee = (SyntaxNode)node.Children[0];
        var arguments = (SyntaxNode)node.Children[1];
        Signature? signature = null;
        var function = 0;

        if (callee.Kind == SyntaxKind.NameExpression)
        {
            var name = ((SyntaxTokenElement)callee.Children[0]).Token;
            if (TryFindVariable(name.Text, out _))
            {
                Report("TRM3011", $"'{name.Text}' is a variable, not a function.", name.Span);
            }
            else if (_functions.TryGetValue(name.Text, out function))
            {
                signature = _signatures[function];
            }
            else
            {
                Report("TRM3011", $"Unknown function '{name.Text}'.", name.Span);
            }
        }
        else
        {
            Report("TRM3011", "Only direct calls to named functions are supported.", callee.Span);
        }

        var bound = ImmutableArray.CreateBuilder<BoundExpression>();
        foreach (var child in arguments.Children)
        {
            if (child is not SyntaxNode argument)
            {
                continue;
            }

            var position = bound.Count;
            if (argument.Children.Length != 1 || argument.Children[0] is not SyntaxNode value)
            {
                Report("TRM3011", "Only positional value arguments are supported.", argument.Span);
                // The malformed argument cannot become a valid program; retain no executable value.
                bound.Add(ErrorExpression(argument.Span));
                continue;
            }

            var expected = signature is not null && position < signature.Parameters.Length
                ? signature.Parameters[position].Type : (PrimitiveType?)null;
            bound.Add(BindExpression(value, expected));
        }

        if (signature is not null && signature.Parameters.Length != bound.Count)
        {
            Report("TRM3011", $"Function '{signature.Name}' expects {signature.Parameters.Length} arguments, got {bound.Count}.", arguments.Span);
        }

        return new BoundCall(function, bound.ToImmutable(), signature?.ReturnType ?? PrimitiveType.Int32, node.Span);
    }

    private BoundConstant BindLiteral(SyntaxToken token, PrimitiveType? expected)
    {
        if (token.Kind is TokenKind.TrueKeyword or TokenKind.FalseKeyword)
        {
            return new BoundConstant(token.Kind == TokenKind.TrueKeyword ? 1 : 0, PrimitiveType.Bool, token.Span);
        }

        if (token.Kind != TokenKind.NumericLiteral)
        {
            Report("TRM3001", "Only boolean and int32 literals are supported.", token.Span);
            return ErrorExpression(token.Span);
        }

        var text = _tree.Source.GetSubText(token.Span);
        var suffix = text.EndsWith("i32", StringComparison.Ordinal);
        var digits = suffix ? text.AsSpan(0, text.Length - 3) : text.AsSpan();

        if (digits.IndexOfAny('.', 'e', 'E') >= 0 || digits.IsEmpty || !AllDigits(digits))
        {
            Report("TRM3013", "Floating-point literals and numeric tags other than i32 are not supported.", token.Span);
            return ErrorExpression(token.Span);
        }

        if (!suffix && expected != PrimitiveType.Int32)
        {
            Report("TRM3014", "An unsuffixed integer literal requires an int32 context.", token.Span);
        }

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed > int.MaxValue)
        {
            Report("TRM3013", "Integer literal is outside the int32 range.", token.Span);
            return ErrorExpression(token.Span);
        }

        return new BoundConstant((int)parsed, PrimitiveType.Int32, token.Span);
    }

    private bool TryMinimumLiteral(SyntaxToken token)
    {
        if (token.Kind != TokenKind.NumericLiteral)
        {
            return false;
        }

        var text = _tree.Source.GetSubText(token.Span);
        var digits = text.EndsWith("i32", StringComparison.Ordinal) ? text.AsSpan(0, text.Length - 3) : text.AsSpan();
        if (digits.IsEmpty || !AllDigits(digits) ||
            !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value != 2147483648L)
        {
            return false;
        }

        return true;
    }

    private static bool AllDigits(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Only expressions supported by this profile contribute a known type.")]
    [SuppressMessage("Style", "IDE0072:Add missing cases", Justification = "Unsupported token kinds have no known type.")]
    private PrimitiveType? KnownType(SyntaxNode node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                return KnownType((SyntaxNode)node.Children[1]);
            case SyntaxKind.VoidExpression:
                return PrimitiveType.Void;
            case SyntaxKind.NameExpression:
                return TryFindVariable(((SyntaxTokenElement)node.Children[0]).Token.Text, out var slot)
                    ? _variables[slot].Type : null;
            case SyntaxKind.LiteralExpression:
            {
                var token = ((SyntaxTokenElement)node.Children[0]).Token;
                return token.Kind switch {
                    TokenKind.TrueKeyword or TokenKind.FalseKeyword => PrimitiveType.Bool,
                    TokenKind.NumericLiteral when token.Text.EndsWith("i32", StringComparison.Ordinal) => PrimitiveType.Int32,
                    _ => null,
                };
            }
            case SyntaxKind.InvocationExpression:
            {
                var callee = (SyntaxNode)node.Children[0];
                if (callee.Kind == SyntaxKind.NameExpression)
                {
                    var name = ((SyntaxTokenElement)callee.Children[0]).Token.Text;
                    if (_functions.TryGetValue(name, out var index) && !TryFindVariable(name, out _))
                    {
                        return _signatures[index].ReturnType;
                    }
                }

                return null;
            }
            case SyntaxKind.UnaryExpression:
                return ((SyntaxTokenElement)node.Children[0]).Token.Kind == TokenKind.Bang
                    ? PrimitiveType.Bool : KnownType((SyntaxNode)node.Children[1]);
            case SyntaxKind.BinaryExpression:
            {
                var op = ((SyntaxTokenElement)node.Children[1]).Token.Kind;
                if (op is TokenKind.EqualsEquals or TokenKind.BangEquals or TokenKind.Less or TokenKind.LessEquals
                    or TokenKind.Greater or TokenKind.GreaterEquals or TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
                {
                    return PrimitiveType.Bool;
                }

                return KnownType((SyntaxNode)node.Children[0]) ?? KnownType((SyntaxNode)node.Children[2]);
            }
            default:
                return null;
        }
    }

    private int AddVariable(string name, PrimitiveType type, bool isMutable, TextSpan span)
    {
        foreach (var scope in _scopes)
        {
            if (scope.ContainsKey(name))
            {
                Report("TRM3004", $"Variable '{name}' already exists in an enclosing scope; shadowing is not supported.", span);
                break;
            }
        }

        var slot = _variables.Count;
        _variables.Add(new BoundVariable(name, type, isMutable, span));
        _scopes[^1][name] = slot;
        return slot;
    }

    private bool TryFindVariable(string name, out int slot)
    {
        for (var index = _scopes.Count - 1; index >= 0; index--)
        {
            if (_scopes[index].TryGetValue(name, out slot))
            {
                return true;
            }
        }

        slot = -1;
        return false;
    }

    private static bool AlwaysReturns(BoundStatement statement)
    {
        switch (statement)
        {
            case BoundReturn:
                return true;
            case BoundIf conditional:
                return conditional.Else is not null && AlwaysReturns(conditional.Then) && AlwaysReturns(conditional.Else);
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    if (AlwaysReturns(child))
                    {
                        return true;
                    }
                }

                return false;
            default:
                // A loop is not presumed to execute or terminate.
                return false;
        }
    }

    private static BoundConstant ErrorExpression(TextSpan span) => new(0, PrimitiveType.Int32, span);

    private static string TypeName(PrimitiveType type) => type switch {
        PrimitiveType.Int32 => "int32",
        PrimitiveType.Bool => "bool",
        PrimitiveType.Void => "void",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private void Report(string code, string message, TextSpan span) =>
        _diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, message, span));

    private sealed record Parameter(string Name, PrimitiveType Type, TextSpan Span);

    private sealed record Signature(string Name, PrimitiveType ReturnType, ImmutableArray<Parameter> Parameters,
        SyntaxNode Body, SyntaxNode Declaration);
}
