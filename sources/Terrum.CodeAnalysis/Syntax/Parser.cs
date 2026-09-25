// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Syntax;

internal sealed class Parser
{
    private const int MaximumDepth = 128;
    private readonly ImmutableArray<SyntaxToken> _tokens;
    private readonly CancellationToken _cancellationToken;
    private readonly List<Diagnostic> _diagnostics = [];
    private int _position;
    private int _depth;

    internal Parser(ImmutableArray<SyntaxToken> tokens, CancellationToken cancellationToken)
    {
        _tokens = tokens;
        _cancellationToken = cancellationToken;
    }

    internal IEnumerable<Diagnostic> Diagnostics => _diagnostics;

    private SyntaxToken Current => Peek(0);

    internal SyntaxNode ParseCompilationUnit()
    {
        var children = new List<SyntaxElement>();

        try
        {
            var seenDeclaration = false;
            var seenNamespace = false;

            while (Current.Kind != TokenKind.EndOfFile)
            {
                var start = _position;

                if (Current.Kind is TokenKind.UsingKeyword or TokenKind.NamespaceKeyword)
                {
                    var isNamespace = Current.Kind == TokenKind.NamespaceKeyword;

                    if (seenDeclaration || (isNamespace && seenNamespace) || (!isNamespace && seenNamespace))
                    {
                        Report("TRM1003", "Using directives must precede the optional file-scoped namespace and declarations.", Current.Span);
                    }

                    children.Add(ParseDirective(isNamespace));
                    seenNamespace |= isNamespace;
                }
                else
                {
                    seenDeclaration = true;
                    children.Add(ParseDeclaration(null));
                }

                EnsureProgress(start, children);
            }

            children.Add(Take());
        }
        catch (NestingLimitException)
        {
            Report("TRM1004", $"Syntax nesting exceeds the supported limit of {MaximumDepth}.", Current.Span);
            // Keep every source token even when abandoning a partially constructed recursive subtree.
            children.Clear();
            var skipped = new List<SyntaxElement>(_tokens.Length - 1);

            for (var index = 0; index < _tokens.Length - 1; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                skipped.Add(new SyntaxTokenElement(_tokens[index]));
            }

            children.Add(Node(SyntaxKind.SkippedSyntax, skipped));
            children.Add(new SyntaxTokenElement(_tokens[^1]));
        }

        return Node(SyntaxKind.CompilationUnit, children);
    }

    private SyntaxNode ParseDirective(bool isNamespace)
    {
        var children = new List<SyntaxElement> { Take(), Expect(TokenKind.Identifier) };

        while (Current.Kind == TokenKind.Dot)
        {
            children.Add(Take());
            children.Add(Expect(TokenKind.Identifier));
        }

        children.Add(Expect(TokenKind.Semicolon));

        return Node(isNamespace ? SyntaxKind.NamespaceDeclaration : SyntaxKind.UsingDirective, children);
    }

    private SyntaxNode ParseDeclaration(string? containingType)
    {
        Enter();
        var children = new List<SyntaxElement>();

        while (IsModifier(Current.Kind))
        {
            children.Add(Take());
        }

        SyntaxKind kind;

        if (Current.Text == "struct")
        {
            children.Add(Take());
            var name = Expect(TokenKind.Identifier);
            children.Add(name);
            children.Add(Expect(TokenKind.OpenBrace));

            while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
            {
                var start = _position;
                children.Add(ParseDeclaration(name.Token.Text));
                EnsureProgress(start, children);
            }

            children.Add(Expect(TokenKind.CloseBrace));
            kind = SyntaxKind.StructDeclaration;
        }
        else if (containingType is not null && Current.Kind == TokenKind.Identifier
            && Current.Text == containingType && Peek(1).Kind == TokenKind.OpenParen)
        {
            children.Add(Take());
            children.Add(ParseParameters());
            children.Add(ParseBlock());
            kind = SyntaxKind.ConstructorDeclaration;
        }
        else if (IsTypeStart(Current.Kind))
        {
            children.Add(ParseType());
            children.Add(Expect(TokenKind.Identifier));

            if (Current.Kind == TokenKind.OpenParen)
            {
                children.Add(ParseParameters());
                children.Add(ParseBlock());
                kind = SyntaxKind.FunctionDeclaration;
            }
            else if (containingType is not null)
            {
                if (Current.Kind == TokenKind.Equals)
                {
                    children.Add(Take());
                    children.Add(ParseExpression());
                }

                children.Add(Expect(TokenKind.Semicolon));
                kind = SyntaxKind.FieldDeclaration;
            }
            else
            {
                Report("TRM1002", $"Expected a function declaration; this construct is outside {SyntaxTree.LanguageVersion}.", Current.Span);
                children.Add(SkipDeclaration());
                kind = SyntaxKind.SkippedSyntax;
            }
        }
        else
        {
            Report("TRM1002", $"Expected a function or struct declaration; this construct is outside {SyntaxTree.LanguageVersion}.", Current.Span);
            children.Add(SkipDeclaration());
            kind = SyntaxKind.SkippedSyntax;
        }

        _depth--;

        return Node(kind, children);
    }

    private SyntaxNode SkipDeclaration()
    {
        var children = new List<SyntaxElement>();
        var braces = 0;

        while (Current.Kind != TokenKind.EndOfFile)
        {
            if (Current.Kind == TokenKind.CloseBrace && braces == 0)
            {
                break;
            }

            var kind = Current.Kind;
            children.Add(Take());

            if (kind == TokenKind.OpenBrace)
            {
                braces++;
            }
            else if (kind == TokenKind.CloseBrace)
            {
                braces--;

                if (braces == 0)
                {
                    break;
                }
            }
            else if (kind == TokenKind.Semicolon && braces == 0)
            {
                break;
            }
        }

        return Node(SyntaxKind.SkippedSyntax, children);
    }

    private SyntaxNode ParseType()
    {
        Enter();
        var children = new List<SyntaxElement>
        {
            Current.Kind == TokenKind.VoidKeyword ? Take() : Expect(TokenKind.Identifier),
        };

        while (Current.Kind == TokenKind.Dot)
        {
            children.Add(Take());
            children.Add(Expect(TokenKind.Identifier));
        }

        if (Current.Kind == TokenKind.Less)
        {
            children.Add(Take());
            children.Add(ParseType());

            while (Current.Kind == TokenKind.Comma)
            {
                children.Add(Take());
                children.Add(ParseType());
            }

            children.Add(Expect(TokenKind.Greater));
        }

        if (Current.Kind == TokenKind.Question)
        {
            children.Add(Take());

            while (Current.Kind == TokenKind.Question)
            {
                Report("TRM1005", "An optional modifier cannot be repeated on the same source type.", Current.Span);
                children.Add(Take());
            }
        }

        _depth--;

        return Node(SyntaxKind.TypeName, children);
    }

    private SyntaxNode ParseParameters()
    {
        var children = new List<SyntaxElement> { Expect(TokenKind.OpenParen) };

        while (Current.Kind is not (TokenKind.CloseParen or TokenKind.OpenBrace or TokenKind.EndOfFile))
        {
            var start = _position;
            var parameter = new List<SyntaxElement>();
            ParsePassingMode(parameter);
            parameter.Add(ParseType());
            parameter.Add(Expect(TokenKind.Identifier));
            children.Add(Node(SyntaxKind.Parameter, parameter));

            if (Current.Kind != TokenKind.Comma)
            {
                EnsureProgress(start, children);
                break;
            }

            children.Add(Take());

            if (Current.Kind == TokenKind.CloseParen)
            {
                Report("TRM1001", "Expected a parameter after ','.", Current.Span);
            }
        }

        children.Add(Expect(TokenKind.CloseParen));

        return Node(SyntaxKind.ParameterList, children);
    }

    private SyntaxNode ParseBlock()
    {
        Enter();
        if (Current.Kind != TokenKind.OpenBrace)
        {
            Report("TRM1001", "Expected a braced block.", new TextSpan(Current.Span.Start, 0));
            var missingBlock = Node(SyntaxKind.Block, [Missing(TokenKind.OpenBrace), Missing(TokenKind.CloseBrace)]);
            _depth--;

            return missingBlock;
        }

        var children = new List<SyntaxElement> { Expect(TokenKind.OpenBrace) };

        while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
        {
            if (Current.Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword or TokenKind.PrivateKeyword or TokenKind.StaticKeyword or TokenKind.StructKeyword)
            {
                break;
            }

            var start = _position;
            children.Add(ParseStatement());
            EnsureProgress(start, children);
        }

        children.Add(Expect(TokenKind.CloseBrace));
        _depth--;

        return Node(SyntaxKind.Block, children);
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Other tokens enter expression parsing or recovery; they are not distinct statement forms.")]
    private SyntaxNode ParseStatement()
    {
        Enter();
        SyntaxNode node;

        switch (Current.Kind)
        {
            case TokenKind.OpenBrace:
            {
                node = ParseBlock();
                break;
            }
            case TokenKind.IfKeyword:
            case TokenKind.WhileKeyword:
            {
                var isIf = Current.Kind == TokenKind.IfKeyword;
                var children = new List<SyntaxElement>
            {
                    Take(), Expect(TokenKind.OpenParen), ParseExpression(), Expect(TokenKind.CloseParen), ParseBlock(),
                };

                if (isIf && Current.Kind == TokenKind.ElseKeyword)
                {
                    var clause = new List<SyntaxElement> { Take() };
                    clause.Add(Current.Kind == TokenKind.IfKeyword ? ParseStatement() : ParseBlock());
                    children.Add(Node(SyntaxKind.ElseClause, clause));
                }

                node = Node(isIf ? SyntaxKind.IfStatement : SyntaxKind.WhileStatement, children);
                break;
            }
            case TokenKind.RequireKeyword:
            {
                node = Node(SyntaxKind.RequireStatement, [Take(), ParseExpression(), Expect(TokenKind.Semicolon)]);
                break;
            }
            case TokenKind.ReturnKeyword:
            {
                var children = new List<SyntaxElement> { Take() };

                if (Current.Kind is not (TokenKind.Semicolon or TokenKind.CloseBrace or TokenKind.EndOfFile))
                {
                    children.Add(ParseExpression());
                }

                children.Add(Expect(TokenKind.Semicolon));
                node = Node(SyntaxKind.ReturnStatement, children);
                break;
            }
            case TokenKind.Semicolon:
            {
                node = Node(SyntaxKind.EmptyStatement, [Take()]);
                break;
            }
            default:
            {
                if (Current.Kind is TokenKind.VarKeyword or TokenKind.MutKeyword || LooksLikeLocal())
                {
                    node = ParseLocal();
                }
                else
                {
                    var children = new List<SyntaxElement> { ParseExpression() };
                    var assignment = Current.Kind == TokenKind.Equals;

                    if (assignment)
                    {
                        children.Add(Take());
                        children.Add(ParseExpression());
                    }

                    children.Add(Expect(TokenKind.Semicolon));
                    node = Node(assignment ? SyntaxKind.AssignmentStatement : SyntaxKind.ExpressionStatement, children);
                }

                break;
            }
        }

        _depth--;

        return node;
    }

    private SyntaxNode ParseLocal()
    {
        var children = new List<SyntaxElement>();

        if (Current.Kind == TokenKind.MutKeyword)
        {
            children.Add(Take());
        }

        children.Add(Current.Kind == TokenKind.VarKeyword ? Take() : ParseType());
        children.Add(Expect(TokenKind.Identifier));
        children.Add(Expect(TokenKind.Equals));
        children.Add(ParseExpression());
        children.Add(Expect(TokenKind.Semicolon));

        return Node(SyntaxKind.VariableDeclaration, children);
    }

    private bool LooksLikeLocal()
    {
        if (Current.Kind != TokenKind.Identifier)
        {
            return false;
        }

        var offset = 1;

        while (Peek(offset).Kind == TokenKind.Dot && Peek(offset + 1).Kind == TokenKind.Identifier)
        {
            offset += 2;
        }

        if (Peek(offset).Kind == TokenKind.Less)
        {
            var nesting = 0;

            do
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var kind = Peek(offset++).Kind;

                if (kind == TokenKind.Less)
                {
                    nesting++;
                }
                else if (kind == TokenKind.Greater)
                {
                    nesting--;
                }
                else if (kind is not (TokenKind.Identifier or TokenKind.VoidKeyword or TokenKind.Comma or TokenKind.Dot or TokenKind.Question))
                {
                    return false;
                }
            }
            while (nesting != 0);
        }

        while (Peek(offset).Kind == TokenKind.Question)
        {
            offset++;
        }

        return Peek(offset).Kind == TokenKind.Identifier;
    }

    private SyntaxNode ParseExpression(int parentPrecedence = 0)
    {
        Enter();
        SyntaxNode left;
        var unaryPrecedence = UnaryPrecedence(Current.Kind);

        if (unaryPrecedence != 0 && unaryPrecedence >= parentPrecedence)
        {
            left = Node(SyntaxKind.UnaryExpression, [Take(), ParseExpression(unaryPrecedence)]);
        }
        else if (Current.Kind == TokenKind.OpenParen && Peek(1).Text is "int32" or "uint32" or "char16"
            && Peek(2).Kind == TokenKind.CloseParen)
        {
            left = Node(SyntaxKind.CastExpression, [Take(), Take(), Take(), ParseExpression(11)]);
        }
        else
        {
            left = ParsePrimary();
        }

        while (true)
        {
            var precedence = BinaryPrecedence(Current.Kind);

            if (precedence == 0 || precedence <= parentPrecedence)
            {
                break;
            }

            left = Node(SyntaxKind.BinaryExpression, [left, Take(), ParseExpression(precedence)]);
        }

        _depth--;

        return left;
    }

    private SyntaxNode ParsePrimary()
    {
        SyntaxNode expression;

        if (Current.Kind == TokenKind.OpenParen)
        {
            var children = new List<SyntaxElement> { Take() };
            var isVoid = Current.Kind == TokenKind.CloseParen;

            if (!isVoid)
            {
                children.Add(ParseExpression());
            }

            children.Add(Expect(TokenKind.CloseParen));
            expression = Node(isVoid ? SyntaxKind.VoidExpression : SyntaxKind.ParenthesizedExpression, children);
        }
        else if (Current.Kind == TokenKind.NewKeyword)
        {
            expression = Node(SyntaxKind.ObjectCreationExpression, [Take(), ParseType(), ParseArguments()]);
        }
        else if (Current.Kind is TokenKind.NumericLiteral or TokenKind.StringLiteral or TokenKind.TrueKeyword or TokenKind.FalseKeyword)
        {
            expression = Node(SyntaxKind.LiteralExpression, [Take()]);
        }
        else if (Current.Kind == TokenKind.Identifier)
        {
            expression = Node(SyntaxKind.NameExpression, [Take()]);
        }
        else
        {
            Report("TRM1001", "Expected an expression.", Current.Span);
            expression = Node(SyntaxKind.MissingExpression, [Missing(TokenKind.Identifier)]);
        }

        while (true)
        {
            if (Current.Kind == TokenKind.Dot)
            {
                expression = Node(SyntaxKind.MemberAccessExpression, [expression, Take(), Expect(TokenKind.Identifier)]);
            }
            else if (Current.Kind == TokenKind.OpenParen)
            {
                expression = Node(SyntaxKind.InvocationExpression, [expression, ParseArguments()]);
            }
            else if (Current.Kind == TokenKind.OpenBracket)
            {
                expression = Node(SyntaxKind.IndexExpression, [expression, Take(), ParseExpression(), Expect(TokenKind.CloseBracket)]);
            }
            else
            {
                break;
            }
        }

        return expression;
    }

    private SyntaxNode ParseArguments()
    {
        var children = new List<SyntaxElement> { Expect(TokenKind.OpenParen) };

        while (Current.Kind is not (TokenKind.CloseParen or TokenKind.Semicolon or TokenKind.CloseBrace or TokenKind.EndOfFile))
        {
            var start = _position;
            var argument = new List<SyntaxElement>();

            if (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Colon)
            {
                argument.Add(Take());
                argument.Add(Take());
            }

            var isOut = Current.Kind == TokenKind.OutKeyword;
            ParsePassingMode(argument);

            if (isOut && Current.Kind == TokenKind.VarKeyword)
            {
                argument.Add(Take());
                argument.Add(Expect(TokenKind.Identifier));
            }
            else
            {
                argument.Add(ParseExpression());
            }

            children.Add(Node(SyntaxKind.Argument, argument));

            if (Current.Kind != TokenKind.Comma)
            {
                EnsureProgress(start, children);
                break;
            }

            children.Add(Take());

            if (Current.Kind == TokenKind.CloseParen)
            {
                Report("TRM1001", "Expected an argument after ','.", Current.Span);
            }
        }

        children.Add(Expect(TokenKind.CloseParen));

        return Node(SyntaxKind.ArgumentList, children);
    }

    private void ParsePassingMode(List<SyntaxElement> children)
    {
        if (Current.Kind == TokenKind.MutKeyword)
        {
            children.Add(Take());
            children.Add(Expect(TokenKind.RefKeyword));
        }
        else if (Current.Kind is TokenKind.RefKeyword or TokenKind.OutKeyword)
        {
            children.Add(Take());
        }
    }

    private void EnsureProgress(int start, List<SyntaxElement> children)
    {
        if (_position == start && Current.Kind != TokenKind.EndOfFile)
        {
            Report("TRM1002", "Unexpected token.", Current.Span);
            children.Add(Node(SyntaxKind.SkippedSyntax, [Take()]));
        }
    }

    private SyntaxTokenElement Expect(TokenKind kind)
    {
        if (Current.Kind == kind)
        {
            return Take();
        }

        Report("TRM1001", $"Expected {kind}.", new TextSpan(Current.Span.Start, 0));

        return Missing(kind);
    }

    private SyntaxTokenElement Missing(TokenKind kind) => new(SyntaxToken.Missing(kind, Current.Span.Start));

    private SyntaxTokenElement Take()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var token = Current;

        if (_position < _tokens.Length - 1)
        {
            _position++;
        }

        return new SyntaxTokenElement(token);
    }

    private SyntaxToken Peek(int offset) => _tokens[Math.Min(_position + offset, _tokens.Length - 1)];

    private SyntaxNode Node(SyntaxKind kind, List<SyntaxElement> children) => new(kind, [.. children], Current.Span.Start);

    private void Report(string code, string message, TextSpan span) => _diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, message, span));

    private void Enter()
    {
        _cancellationToken.ThrowIfCancellationRequested();

        if (++_depth > MaximumDepth)
        {
            throw new NestingLimitException();
        }
    }

    private static bool IsModifier(TokenKind kind)
        => kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword or TokenKind.PrivateKeyword or TokenKind.StaticKeyword or TokenKind.MutKeyword;

    private static bool IsTypeStart(TokenKind kind) => kind is TokenKind.Identifier or TokenKind.VoidKeyword;

    private static int UnaryPrecedence(TokenKind kind) => kind is TokenKind.Plus or TokenKind.Minus or TokenKind.Bang or TokenKind.Tilde ? 11 : 0;

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Non-operator tokens have zero precedence.")]
    private static int BinaryPrecedence(TokenKind kind) => kind switch {
        TokenKind.Star or TokenKind.Slash or TokenKind.Percent => 10,
        TokenKind.Plus or TokenKind.Minus => 9,
        TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals => 7,
        TokenKind.EqualsEquals or TokenKind.BangEquals => 6,
        TokenKind.Ampersand => 5,
        TokenKind.Caret => 4,
        TokenKind.Pipe => 3,
        TokenKind.AmpersandAmpersand => 2,
        TokenKind.PipePipe => 1,
        _ => 0,
    };

    [SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Private parser unwind signal; always caught by ParseCompilationUnit.")]
    [SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Private parser unwind signal, never part of the public API.")]
    private sealed class NestingLimitException : Exception;
}
