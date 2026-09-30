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
    private int _pendingGreater;

    internal Parser(ImmutableArray<SyntaxToken> tokens, CancellationToken cancellationToken)
    {
        _tokens = tokens;
        _cancellationToken = cancellationToken;
    }

    private SyntaxNode ParseTypeParameters(bool interfaceParameters = false)
    {
        var children = new List<SyntaxElement> { Expect(TokenKind.Less) };
        if (interfaceParameters && Current.Kind == TokenKind.Identifier && Current.Text == "this")
        {
            children.Add(Take());
        }
        children.Add(Expect(TokenKind.Identifier));
        while (Current.Kind == TokenKind.Comma)
        {
            children.Add(Take());
            if (interfaceParameters && Current.Kind == TokenKind.Identifier && Current.Text == "this")
            {
                children.Add(Take());
            }
            children.Add(Expect(TokenKind.Identifier));
        }
        children.Add(Expect(TokenKind.Greater));
        return Node(SyntaxKind.TypeParameterList, children);
    }

    private SyntaxNode ParseConformances()
    {
        var children = new List<SyntaxElement> { Take(), ParseType() };
        while (Current.Kind == TokenKind.Comma)
        {
            children.Add(Take());
            children.Add(ParseType());
        }
        return Node(SyntaxKind.ConformanceList, children);
    }

    private SyntaxNode ParseConstraint()
    {
        var children = new List<SyntaxElement> {
            Take(), Expect(TokenKind.Identifier), Expect(TokenKind.Colon),
        };
        while (true)
        {
            var start = _position;
            if (Current.Kind == TokenKind.OpenBracket)
            {
                children.Add(Node(SyntaxKind.Attribute,
                    [Take(), Expect(TokenKind.Identifier), Expect(TokenKind.CloseBracket)]));
            }
            else
            {
                children.Add(ParseType());
            }
            if (_position == start)
            {
                if (Current.Kind is not (TokenKind.OpenBrace or TokenKind.EndOfFile))
                {
                    children.Add(Node(SyntaxKind.SkippedSyntax, [Take()]));
                }
                break;
            }
            if (Current.Kind != TokenKind.Comma)
            {
                break;
            }
            children.Add(Take());
        }
        return Node(SyntaxKind.ConstraintClause, children);
    }

    private bool LooksLikeGenericCall()
    {
        var depth = 0;
        var previous = TokenKind.Identifier;
        for (var offset = 0; offset < _tokens.Length - _position; offset++)
        {
            if ((offset & 63) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }
            var kind = Peek(offset).Kind;
            if (kind == TokenKind.Less)
            {
                // Qualified names do not increase nesting; only type arguments use the parser's depth bound.
                if (previous != TokenKind.Identifier || ++depth > MaximumDepth)
                {
                    return false;
                }
            }
            else if (kind == TokenKind.Greater)
            {
                if (depth == 0 || previous is not (TokenKind.Identifier or TokenKind.VoidKeyword
                    or TokenKind.Greater or TokenKind.Question or TokenKind.QuestionQuestion))
                {
                    return false;
                }
                if (--depth == 0)
                {
                    return Peek(offset + 1).Kind == TokenKind.OpenParen;
                }
            }
            else if (kind is TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned)
            {
                var count = kind == TokenKind.ShiftRight ? 2 : 3;
                if (depth < count || previous is not (TokenKind.Identifier or TokenKind.VoidKeyword
                    or TokenKind.Greater or TokenKind.Question or TokenKind.QuestionQuestion))
                {
                    return false;
                }
                depth -= count;
                if (depth == 0)
                {
                    return Peek(offset + 1).Kind == TokenKind.OpenParen;
                }
                kind = TokenKind.Greater;
            }
            else if (kind is TokenKind.Identifier or TokenKind.VoidKeyword)
            {
                if (previous is not (TokenKind.Less or TokenKind.Comma or TokenKind.Dot
                    or TokenKind.WriteKeyword))
                {
                    return false;
                }
            }
            else if (kind == TokenKind.WriteKeyword)
            {
                if (previous is not (TokenKind.Less or TokenKind.Comma))
                {
                    return false;
                }
            }
            else if (kind == TokenKind.Dot)
            {
                if (previous != TokenKind.Identifier)
                {
                    return false;
                }
            }
            else if (kind == TokenKind.Comma)
            {
                if (depth == 0 || previous is not (TokenKind.Identifier or TokenKind.VoidKeyword
                    or TokenKind.Greater or TokenKind.Question or TokenKind.QuestionQuestion))
                {
                    return false;
                }
            }
            else if (kind is TokenKind.Question or TokenKind.QuestionQuestion)
            {
                if (previous is not (TokenKind.Identifier or TokenKind.VoidKeyword or TokenKind.Greater
                    or TokenKind.Question or TokenKind.QuestionQuestion))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
            previous = kind;
        }
        return false;
    }

    private SyntaxNode ParseTypeArguments()
    {
        var children = new List<SyntaxElement> { Take(), ParseType() };
        while (Current.Kind == TokenKind.Comma)
        {
            children.Add(Take());
            children.Add(ParseType());
        }
        children.Add(Expect(TokenKind.Greater));
        return Node(SyntaxKind.TypeParameterList, children);
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

        while (Current.Kind == TokenKind.OpenBracket)
        {
            var attribute = new List<SyntaxElement> { Take(), Expect(TokenKind.Identifier) };
            if (Current.Kind == TokenKind.OpenParen)
            {
                attribute.Add(Take());
                attribute.Add(Expect(TokenKind.Identifier));
                attribute.Add(Expect(TokenKind.CloseParen));
            }
            attribute.Add(Expect(TokenKind.CloseBracket));
            children.Add(Node(SyntaxKind.Attribute, attribute));
        }

        while (IsModifier(Current.Kind))
        {
            children.Add(Take());
        }

        SyntaxKind kind;

        if (Current.Kind is TokenKind.StructKeyword or TokenKind.ClassKeyword)
        {
            var isClass = Current.Kind == TokenKind.ClassKeyword;
            children.Add(Take());
            var name = Expect(TokenKind.Identifier);
            children.Add(name);
            if (!isClass && Current.Kind == TokenKind.Colon)
            {
                children.Add(ParseConformances());
            }
            children.Add(Expect(TokenKind.OpenBrace));

            while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
            {
                var start = _position;
                children.Add(ParseDeclaration(name.Token.Name));
                EnsureProgress(start, children);
            }

            children.Add(Expect(TokenKind.CloseBrace));
            kind = isClass ? SyntaxKind.ClassDeclaration : SyntaxKind.StructDeclaration;
        }
        else if (Current.Kind == TokenKind.InterfaceKeyword)
        {
            children.Add(Take());
            children.Add(Expect(TokenKind.Identifier));
            children.Add(ParseTypeParameters(interfaceParameters: true));
            children.Add(Expect(TokenKind.OpenBrace));
            while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
            {
                var start = _position;
                children.Add(ParseDeclaration(null));
                EnsureProgress(start, children);
            }
            children.Add(Expect(TokenKind.CloseBrace));
            kind = SyntaxKind.InterfaceDeclaration;
        }
        else if (containingType is not null && Current.Kind == TokenKind.Identifier
            && Current.Name == containingType && Peek(1).Kind == TokenKind.OpenParen)
        {
            children.Add(Take());
            children.Add(ParseParameters());
            children.Add(ParseCallableBody());
            kind = SyntaxKind.ConstructorDeclaration;
        }
        else if (IsTypeStart(Current.Kind))
        {
            children.Add(ParseType());
            if (Current.Kind == TokenKind.OperatorKeyword)
            {
                children.Add(Take());
                children.Add(Current.Kind is TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash
                    or TokenKind.Percent or TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret
                    or TokenKind.EqualsEquals or TokenKind.BangEquals or TokenKind.Less or TokenKind.LessEquals
                    or TokenKind.Greater or TokenKind.GreaterEquals or TokenKind.Bang or TokenKind.Tilde
                    or TokenKind.PlusPlus or TokenKind.MinusMinus
                    ? Take() : Expect(TokenKind.Plus));
                children.Add(ParseParameters());
                children.Add(Current.Kind == TokenKind.Semicolon ? Take() : ParseCallableBody());
                kind = SyntaxKind.OperatorDeclaration;
            }
            else
            {
                children.Add(Expect(TokenKind.Identifier));

                if (Current.Kind == TokenKind.Less)
                {
                    children.Add(ParseTypeParameters());
                }
                if (Current.Kind == TokenKind.OpenParen)
                {
                    children.Add(ParseParameters());
                    while (Current.Kind == TokenKind.WhereKeyword)
                    {
                        children.Add(ParseConstraint());
                    }
                    children.Add(ParseCallableBody());
                    kind = SyntaxKind.FunctionDeclaration;
                }
                else if (containingType is not null && children.TrueForAll(static child =>
                    child is not SyntaxNode { Kind: SyntaxKind.TypeParameterList }))
                {
                    if (Current.Kind == TokenKind.Arrow)
                    {
                        children.Add(ParseCallableBody());
                        kind = SyntaxKind.PropertyDeclaration;
                    }
                    else if (Current.Kind == TokenKind.OpenBrace)
                    {
                        children.Add(Take());
                        var accessor = new List<SyntaxElement>();
                        while (Current.Kind == TokenKind.OpenBracket)
                        {
                            accessor.Add(Node(SyntaxKind.Attribute,
                                [Take(), Expect(TokenKind.Identifier), Expect(TokenKind.CloseBracket)]));
                        }
                        if (Current.Kind != TokenKind.Identifier || Current.Text != "get")
                        {
                            Report("TRM1002", "A property requires an explicit get accessor.", Current.Span);
                            children.Add(SkipDeclaration());
                        }
                        else
                        {
                            accessor.Add(Take());
                            if (Current.Kind is not (TokenKind.OpenBrace or TokenKind.Arrow))
                            {
                                Report("TRM1002", "A getter requires a body.", Current.Span);
                                accessor.Add(SkipDeclaration());
                            }
                            else
                            {
                                accessor.Add(ParseCallableBody());
                            }
                            children.Add(Node(SyntaxKind.GetAccessor, accessor));
                        }
                        if (Current.Kind != TokenKind.CloseBrace)
                        {
                            Report("TRM1002", "Only a single explicit getter is supported.", Current.Span);
                            children.Add(SkipDeclaration());
                        }
                        children.Add(Expect(TokenKind.CloseBrace));
                        kind = SyntaxKind.PropertyDeclaration;
                    }
                    else
                    {
                        if (Current.Kind == TokenKind.Equals)
                        {
                            children.Add(Take());
                            children.Add(ParseExpression());
                        }
                        children.Add(Expect(TokenKind.Semicolon));
                        kind = SyntaxKind.FieldDeclaration;
                    }
                }
                else
                {
                    Report("TRM1002", $"Expected a function declaration; this construct is outside {SyntaxTree.LanguageVersion}.", Current.Span);
                    children.Add(SkipDeclaration());
                    kind = SyntaxKind.SkippedSyntax;
                }
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
        var children = new List<SyntaxElement>();
        if (Current.Kind == TokenKind.WriteKeyword)
        {
            children.Add(Take());
        }
        children.Add(Current.Kind == TokenKind.VoidKeyword ? Take() : Expect(TokenKind.Identifier));

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

        if (Current.Kind is TokenKind.Question or TokenKind.QuestionQuestion)
        {
            var hasModifier = false;
            while (Current.Kind is TokenKind.Question or TokenKind.QuestionQuestion)
            {
                var modifier = Take();
                children.Add(modifier);
                for (var index = hasModifier ? 0 : 1; index < modifier.Token.Text.Length; index++)
                {
                    Report("TRM1005", "An optional modifier cannot be repeated on the same source type.",
                        new TextSpan(modifier.Span.Start + index, 1));
                }
                hasModifier = true;
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
            if (Current.Kind == TokenKind.Identifier && Current.Text == "init"
                && Peek(1).Kind == TokenKind.RefKeyword)
            {
                parameter.Add(Take());
                parameter.Add(Take());
                parameter.Add(Expect(TokenKind.Less));
                parameter.Add(ParseType());
                parameter.Add(Expect(TokenKind.Greater));
            }
            else
            {
                ParsePassingMode(parameter);
                parameter.Add(ParseType());
            }
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
            if (Current.Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword
                or TokenKind.PrivateKeyword or TokenKind.StructKeyword or TokenKind.UniqueKeyword
                || (Current.Kind == TokenKind.StaticKeyword && !IsStaticProofStart()))
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

    private SyntaxNode ParseCallableBody()
    {
        if (Current.Kind != TokenKind.Arrow)
        {
            return ParseBlock();
        }

        var children = new List<SyntaxElement> { Take() };
        if (Current.Kind is TokenKind.RequireKeyword or TokenKind.Underscore)
        {
            children.Add(ParseStatement());
        }
        else
        {
            children.Add(ParseExpressionStatement());
        }

        return Node(SyntaxKind.ExpressionBody, children);
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
            case TokenKind.DoKeyword:
            {
                node = Node(SyntaxKind.DoStatement,
                    [Take(), ParseBlock(), Expect(TokenKind.WhileKeyword), Expect(TokenKind.OpenParen),
                        ParseExpression(), Expect(TokenKind.CloseParen), Expect(TokenKind.Semicolon)]);
                break;
            }
            case TokenKind.RequireKeyword:
            {
                node = Node(SyntaxKind.RequireStatement, [Take(), ParseExpression(), Expect(TokenKind.Semicolon)]);
                break;
            }
            case TokenKind.StaticKeyword:
            {
                var proof = IsStaticProofStart();
                var modifier = Take();
                if (proof)
                {
                    var isEnsure = Current.Kind == TokenKind.Identifier;
                    node = Node(isEnsure ? SyntaxKind.StaticEnsureStatement : SyntaxKind.StaticRequireStatement,
                        [modifier, Take(), ParseExpression(), Expect(TokenKind.Semicolon)]);
                }
                else
                {
                    Report("TRM1001", "Expected 'require' or 'ensure' after 'static' in a body.",
                        Current.Span);
                    node = Node(SyntaxKind.SkippedSyntax, [modifier]);
                }
                break;
            }
            case TokenKind.TryKeyword:
            {
                var children = new List<SyntaxElement> { Take(), ParseBlock() };
                if (Current.Kind == TokenKind.CatchKeyword)
                {
                    children.Add(Take());
                    children.Add(ParseBlock());
                    if (Current.Kind == TokenKind.FinallyKeyword)
                    {
                        children.Add(Take());
                        children.Add(ParseBlock());
                    }
                    node = Node(SyntaxKind.TryCatchStatement, children);
                }
                else
                {
                    children.Add(Expect(TokenKind.FinallyKeyword));
                    children.Add(ParseBlock());
                    node = Node(SyntaxKind.TryFinallyStatement, children);
                }
                break;
            }
            case TokenKind.UnsafeKeyword:
            {
                node = Node(SyntaxKind.UnsafeStatement, [Take(), ParseBlock()]);
                break;
            }
            case TokenKind.ForKeyword:
            {
                var children = new List<SyntaxElement> { Take(), Expect(TokenKind.OpenParen) };
                children.Add(Current.Kind == TokenKind.Semicolon ? Node(SyntaxKind.EmptyStatement, [])
                    : IsVacantLocalStart() ? ParseVacantLocal(semicolon: false)
                    : Current.Kind is TokenKind.VarKeyword or TokenKind.MutKeyword || LooksLikeLocal()
                        ? ParseLocal(semicolon: false) : ParseExpressionStatement(semicolon: false));
                children.Add(Expect(TokenKind.Semicolon));
                children.Add(Current.Kind == TokenKind.Semicolon ? Node(SyntaxKind.OmittedExpression, []) : ParseExpression());
                children.Add(Expect(TokenKind.Semicolon));
                children.Add(Current.Kind == TokenKind.CloseParen ? Node(SyntaxKind.EmptyStatement, [])
                    : ParseExpressionStatement(semicolon: false));
                children.Add(Expect(TokenKind.CloseParen));
                children.Add(ParseBlock());
                node = Node(SyntaxKind.ForStatement, children);
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
            case TokenKind.BreakKeyword:
            case TokenKind.ContinueKeyword:
            {
                var kind = Current.Kind == TokenKind.BreakKeyword ? SyntaxKind.BreakStatement : SyntaxKind.ContinueStatement;
                var children = new List<SyntaxElement> { Take() };
                if (Current.Kind == TokenKind.Identifier)
                {
                    children.Add(Take());
                }
                children.Add(Expect(TokenKind.Semicolon));
                node = Node(kind, children);
                break;
            }
            case TokenKind.Semicolon:
            {
                node = Node(SyntaxKind.EmptyStatement, [Take()]);
                break;
            }
            case TokenKind.Underscore:
            {
                node = Node(SyntaxKind.DiscardStatement,
                    [Take(), Expect(TokenKind.Equals), ParseExpression(), Expect(TokenKind.Semicolon)]);
                break;
            }
            default:
            {
                if (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Colon
                    && Peek(2).Kind is TokenKind.WhileKeyword or TokenKind.ForKeyword or TokenKind.DoKeyword)
                {
                    node = Node(SyntaxKind.LabeledLoopStatement, [Take(), Take(), ParseStatement()]);
                }
                else if (Current.Kind == TokenKind.Identifier && Current.Text == "const"
                    && LooksLikeLocal(1))
                {
                    var keyword = Take();
                    node = Node(SyntaxKind.ConstDeclaration, [keyword, ParseLocal()]);
                }
                else if (IsVacantLocalStart())
                {
                    node = ParseVacantLocal();
                }
                else if (Current.Kind is TokenKind.VarKeyword or TokenKind.MutKeyword || LooksLikeLocal())
                {
                    node = ParseLocal();
                }
                else
                {
                    node = ParseExpressionStatement();
                }

                break;
            }
        }

        _depth--;

        return node;
    }

    private SyntaxNode ParseExpressionStatement(bool semicolon = true)
    {
        var children = new List<SyntaxElement> { ParseExpression() };
        var assignment = Current.Kind is TokenKind.Equals or TokenKind.QuestionQuestionEquals
            or TokenKind.PlusEquals or TokenKind.MinusEquals or TokenKind.StarEquals
            or TokenKind.SlashEquals or TokenKind.PercentEquals or TokenKind.AmpersandEquals
            or TokenKind.PipeEquals or TokenKind.CaretEquals or TokenKind.ShiftLeftEquals
            or TokenKind.ShiftRightEquals or TokenKind.ShiftRightUnsignedEquals;
        var compound = assignment && Current.Kind is not (TokenKind.Equals or TokenKind.QuestionQuestionEquals);

        if (assignment)
        {
            children.Add(Take());
            children.Add(ParseExpression());
        }

        if (semicolon)
        {
            children.Add(Expect(TokenKind.Semicolon));
        }

        return Node(compound ? SyntaxKind.CompoundAssignmentStatement
            : assignment ? SyntaxKind.AssignmentStatement : SyntaxKind.ExpressionStatement, children);
    }

    private SyntaxNode ParseLocal(bool semicolon = true)
    {
        var children = new List<SyntaxElement>();

        if (Current.Kind == TokenKind.MutKeyword)
        {
            children.Add(Take());
        }

        children.Add(Current.Kind == TokenKind.VarKeyword ? Take() : ParseType());
        children.Add(Expect(TokenKind.Identifier));
        children.Add(Expect(TokenKind.Equals));
        if (Current.Kind == TokenKind.Semicolon)
        {
            Report("TRM1001", "An ordinary local requires an initializer.", Current.Span);
            children.Add(Node(SyntaxKind.OmittedExpression, []));
        }
        else
        {
            children.Add(ParseExpression());
        }

        if (semicolon)
        {
            children.Add(Expect(TokenKind.Semicolon));
        }

        return Node(SyntaxKind.VariableDeclaration, children);
    }

    private bool IsVacantLocalStart() => Current.Kind == TokenKind.Identifier
        && Current.Text == "init"
        && (Peek(1).Kind == TokenKind.VarKeyword || LooksLikeLocal(1));

    private SyntaxNode ParseVacantLocal(bool semicolon = true)
    {
        var children = new List<SyntaxElement> {
            Take(), Current.Kind == TokenKind.VarKeyword ? Take() : ParseType(),
            Expect(TokenKind.Identifier),
        };
        if (semicolon)
        {
            children.Add(Expect(TokenKind.Semicolon));
        }
        return Node(SyntaxKind.VacantLocalDeclaration, children);
    }

    private bool LooksLikeLocal(int initialOffset = 0)
    {
        var offset = initialOffset + (Peek(initialOffset).Kind == TokenKind.WriteKeyword ? 1 : 0);
        if (Peek(offset).Kind != TokenKind.Identifier)
        {
            return false;
        }
        offset++;

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
                else if (kind is TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned)
                {
                    nesting -= kind == TokenKind.ShiftRight ? 2 : 3;
                    if (nesting < 0)
                    {
                        return false;
                    }
                }
                else if (kind is not (TokenKind.Identifier or TokenKind.VoidKeyword or TokenKind.Comma or TokenKind.Dot or TokenKind.Question or TokenKind.QuestionQuestion))
                {
                    return false;
                }
            }
            while (nesting != 0);
        }

        while (Peek(offset).Kind is TokenKind.Question or TokenKind.QuestionQuestion)
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

        if (Current.Kind == TokenKind.Identifier && Current.Text == "const"
            && 13 >= parentPrecedence && (Peek(1).Kind is TokenKind.Identifier
                or TokenKind.NumericLiteral or TokenKind.StringLiteral
                or TokenKind.TrueKeyword or TokenKind.FalseKeyword or TokenKind.NullKeyword
                or TokenKind.NewKeyword or TokenKind.MoveKeyword or TokenKind.Bang or TokenKind.Tilde
                || (Peek(1).Kind == TokenKind.OpenParen && Peek(1).Span.Start > Current.Span.End)))
        {
            left = Node(SyntaxKind.ConstExpression, [Take(), ParseExpression(13)]);
        }
        else if (unaryPrecedence != 0 && unaryPrecedence >= parentPrecedence)
        {
            var op = Take();
            if (op.Token.Kind is TokenKind.PlusPlus or TokenKind.MinusMinus
                && ((Current.Kind is TokenKind.Plus or TokenKind.Minus or TokenKind.PlusPlus or TokenKind.MinusMinus
                        && Current.Span.Start == op.Span.End)
                    || (_position >= 2 && _tokens[_position - 2].Kind is TokenKind.Plus or TokenKind.Minus
                        && _tokens[_position - 2].Span.End == op.Span.Start)))
            {
                Report("TRM1001", "Separate adjacent operator glyphs with trivia.", Current.Span);
            }
            left = Node(op.Token.Kind is TokenKind.PlusPlus or TokenKind.MinusMinus
                ? SyntaxKind.UpdateExpression : SyntaxKind.UnaryExpression,
                [op, ParseExpression(unaryPrecedence)]);
        }
        else
        {
            left = ParsePrimary();
        }

        while (true)
        {
            if (Current.Kind == TokenKind.Identifier && Current.Text == "is" && parentPrecedence < 7)
            {
                var keyword = Take();
                if (Current.Kind == TokenKind.NullKeyword)
                {
                    left = Node(SyntaxKind.IsNullExpression, [left, keyword, Take()]);
                }
                else
                {
                    var some = Expect(TokenKind.Identifier);
                    if (some.Token.Text != "some")
                    {
                        Report("TRM1001", "Expected 'some' or 'null' after 'is'.", some.Span);
                    }
                    left = Node(SyntaxKind.IsSomeExpression, [left, keyword, some, Expect(TokenKind.Identifier)]);
                }
                continue;
            }
            var precedence = BinaryPrecedence(Current.Kind);

            if (precedence == 0 || precedence < parentPrecedence
                || (precedence == parentPrecedence && Current.Kind != TokenKind.QuestionQuestion))
            {
                break;
            }

            left = Node(SyntaxKind.BinaryExpression, [left, Take(), ParseExpression(precedence)]);
        }

        if (parentPrecedence == 0 && Current.Kind == TokenKind.Question)
        {
            var question = Take();
            var whenTrue = ParseExpression();
            var colon = Expect(TokenKind.Colon);
            var whenFalse = ParseExpression();
            left = Node(SyntaxKind.ConditionalExpression, [left, question, whenTrue, colon, whenFalse]);
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
        else if (Current.Kind is TokenKind.NumericLiteral or TokenKind.StringLiteral or TokenKind.TrueKeyword
            or TokenKind.FalseKeyword or TokenKind.NullKeyword)
        {
            expression = Node(SyntaxKind.LiteralExpression, [Take()]);
        }
        else if (Current.Kind == TokenKind.Identifier)
        {
            var name = Take();
            expression = Current.Kind == TokenKind.Less && LooksLikeGenericCall()
                ? Node(SyntaxKind.GenericNameExpression, [name, ParseTypeArguments()])
                : Node(SyntaxKind.NameExpression, [name]);
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
            else if (Current.Kind is TokenKind.PlusPlus or TokenKind.MinusMinus)
            {
                var op = Take();
                if (Current.Kind is TokenKind.Plus or TokenKind.Minus or TokenKind.PlusPlus or TokenKind.MinusMinus
                    && Current.Span.Start == op.Span.End)
                {
                    Report("TRM1001", "Separate adjacent operator glyphs with trivia.", Current.Span);
                }
                expression = Node(SyntaxKind.UpdateExpression, [expression, op]);
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

            var isOut = Current.Kind == TokenKind.OutKeyword
                || (Current.Kind == TokenKind.Identifier && Current.Text == "init"
                    && Peek(1).Kind is TokenKind.VarKeyword or TokenKind.Identifier);
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
        if (Current.Kind == TokenKind.Identifier && Current.Text == "init"
            && Peek(1).Kind is TokenKind.VarKeyword or TokenKind.Identifier)
        {
            children.Add(Take());
        }
        else if (Current.Kind == TokenKind.MutKeyword)
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
        if (kind == TokenKind.Greater && _pendingGreater == 0
            && Current.Kind is TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned
            && Current.Text == (Current.Kind == TokenKind.ShiftRight ? ">>" : ">>>"))
        {
            _pendingGreater = Current.Kind == TokenKind.ShiftRight ? 2 : 3;
        }
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

        if (_pendingGreater > 0)
        {
            _pendingGreater--;
            if (_pendingGreater == 0)
            {
                _position++;
            }
        }
        else if (_position < _tokens.Length - 1)
        {
            _position++;
        }

        return new SyntaxTokenElement(token);
    }

    private SyntaxToken Peek(int offset)
    {
        if (_pendingGreater > 0)
        {
            if (offset < _pendingGreater)
            {
                var original = _tokens[_position];
                var index = original.Text.Length - _pendingGreater + offset;
                return new SyntaxToken(TokenKind.Greater, ">", new TextSpan(original.Span.Start + index, 1),
                    index == 0 ? original.LeadingTrivia : []);
            }
            offset -= _pendingGreater;
            return _tokens[Math.Min(_position + 1 + offset, _tokens.Length - 1)];
        }
        return _tokens[Math.Min(_position + offset, _tokens.Length - 1)];
    }

    private bool IsStaticProofStart() => Current.Kind == TokenKind.StaticKeyword
        && (Peek(1).Kind == TokenKind.RequireKeyword
            || (Peek(1).Kind == TokenKind.Identifier && Peek(1).Text == "ensure"));

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
        => kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword or TokenKind.PrivateKeyword or TokenKind.StaticKeyword or TokenKind.MutKeyword or TokenKind.UniqueKeyword;

    private static bool IsTypeStart(TokenKind kind) => kind is TokenKind.Identifier or TokenKind.VoidKeyword
        or TokenKind.WriteKeyword;

    private static int UnaryPrecedence(TokenKind kind) => kind is TokenKind.Plus or TokenKind.Minus or TokenKind.Bang
        or TokenKind.Tilde or TokenKind.MoveKeyword or TokenKind.PlusPlus or TokenKind.MinusMinus ? 13 : 0;

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Non-operator tokens have zero precedence.")]
    private static int BinaryPrecedence(TokenKind kind) => kind switch {
        TokenKind.Star or TokenKind.Slash or TokenKind.Percent => 12,
        TokenKind.Plus or TokenKind.Minus => 11,
        TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned => 10,
        TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals => 8,
        TokenKind.EqualsEquals or TokenKind.BangEquals => 7,
        TokenKind.Ampersand => 6,
        TokenKind.Caret => 5,
        TokenKind.Pipe => 4,
        TokenKind.AmpersandAmpersand => 3,
        TokenKind.PipePipe => 2,
        TokenKind.QuestionQuestion => 1,
        _ => 0,
    };

    [SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Private parser unwind signal; always caught by ParseCompilationUnit.")]
    [SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Private parser unwind signal, never part of the public API.")]
    private sealed class NestingLimitException : Exception;
}
