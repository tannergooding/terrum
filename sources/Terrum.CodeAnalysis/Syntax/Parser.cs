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
    private readonly SourceText _source;
    private readonly HashSet<TextSpan> _typePunctuation = [];
    private readonly CancellationToken _cancellationToken;
    private readonly List<Diagnostic> _diagnostics = [];
    private int _position;
    private int _depth;
    private int _pendingGreater;
    private int _missingTokens;

    internal Parser(SourceText source, ImmutableArray<SyntaxToken> tokens, CancellationToken cancellationToken)
    {
        _source = source;
        _tokens = tokens;
        _cancellationToken = cancellationToken;
    }

    private SyntaxNode ParseTypeParameters(bool interfaceParameters = false, bool interfaceMember = false)
    {
        var missing = _missingTokens;
        var children = new List<SyntaxElement> { ExpectAttached(TokenKind.Less) };
        while (true)
        {
            ParseGenericFormal(children, interfaceParameters);
            if (Current.Kind != TokenKind.Comma)
            {
                break;
            }
            children.Add(Take());
        }
        children.Add(ExpectTypeCloser(missing));
        return Node(SyntaxKind.TypeParameterList, children);
    }

    private void ParseGenericFormal(List<SyntaxElement> children, bool interfaceParameters)
    {
        while (Current.Kind == TokenKind.OpenBracket && !IsNativeCallConvTypeAttributeStart())
        {
            children.Add(ParseAttribute());
        }
        if (interfaceParameters && Current.Kind == TokenKind.Identifier && Current.Text == "this")
        {
            children.Add(Take());
            children.Add(Expect(TokenKind.Identifier));
            return;
        }
        if (Current.Kind == TokenKind.Identifier && Current.Text == "const")
        {
            children.Add(Take());
            children.Add(ParseType());
            children.Add(Expect(TokenKind.Identifier));
            if (Current.Kind == TokenKind.Equals)
            {
                children.Add(TakeSeparated());
                children.Add(ParseExpression(allowGreaterAsTypeTerminator: true));
            }
            return;
        }
        children.Add(Expect(TokenKind.Identifier));
        if (Current.Kind == TokenKind.Equals)
        {
            children.Add(TakeSeparated());
            children.Add(ParseType());
        }
    }

    private SyntaxNode ParseConformances()
    {
        var children = new List<SyntaxElement> { TakeSeparated(), ParseType() };
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
            Take(), Expect(TokenKind.Identifier), ExpectSeparated(TokenKind.Colon),
        };
        while (true)
        {
            var start = _position;
            if (Current.Kind == TokenKind.OpenBracket)
            {
                children.Add(Node(SyntaxKind.Attribute,
                    [Take(), Expect(TokenKind.Identifier), Expect(TokenKind.CloseBracket)]));
            }
            else if (Current.Kind is TokenKind.WriteKeyword or TokenKind.UniqueKeyword
                or TokenKind.NullKeyword)
            {
                var term = new List<SyntaxElement> { Take() };
                if (term[0] is SyntaxTokenElement {
                        Token.Kind: TokenKind.UniqueKeyword or TokenKind.NullKeyword,
                    }
                    && Current.Kind == TokenKind.Question)
                {
                    term.Add(Take());
                }
                children.Add(Node(SyntaxKind.ConstraintTerm, term));
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

    private bool IsContextualDeclarationPrefix() =>
        Current.Kind == TokenKind.Identifier
        && Current.Text is "partial" or "file" or "friend";

    private SyntaxNode ParseFriendVisibility()
    {
        var missing = _missingTokens;
        var children = new List<SyntaxElement> { Take(), ExpectAttached(TokenKind.Less), ParseFriendTarget() };
        while (Current.Kind == TokenKind.Comma)
        {
            children.Add(Take());
            children.Add(ParseFriendTarget());
        }
        children.Add(ExpectTypeCloser(missing));
        return Node(SyntaxKind.FriendVisibility, children);
    }

    private SyntaxNode ParseFriendTarget()
    {
        var children = new List<SyntaxElement> { Expect(TokenKind.Identifier) };
        while (Current.Kind == TokenKind.Dot)
        {
            children.Add(TakeDot(qualified: true));
            children.Add(Expect(TokenKind.Identifier));
        }
        if (Current.Kind == TokenKind.Less)
        {
            var missing = _missingTokens;
            children.Add(ExpectAttached(TokenKind.Less));
            while (true)
            {
                if (Current.Kind == TokenKind.Underscore)
                {
                    children.Add(Take());
                }
                else if (IsTypeStart(Current.Kind)
                    || Current.Kind is TokenKind.RefKeyword or TokenKind.GcKeyword)
                {
                    children.Add(ParseType());
                }
                else
                {
                    children.Add(ParseExpression());
                }
                if (Current.Kind != TokenKind.Comma)
                {
                    break;
                }
                children.Add(Take());
            }
            children.Add(ExpectTypeCloser(missing));
        }
        return Node(SyntaxKind.FriendTarget, children);
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
        var missing = _missingTokens;
        var children = new List<SyntaxElement> { ExpectAttached(TokenKind.Less), ParseType() };
        while (Current.Kind == TokenKind.Comma)
        {
            children.Add(Take());
            children.Add(ParseType());
        }
        children.Add(ExpectTypeCloser(missing));
        return Node(SyntaxKind.TypeParameterList, children);
    }

    internal IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    private SyntaxToken Current => Peek(0);

    internal SyntaxNode ParseCompilationUnit()
    {
        var children = new List<SyntaxElement>();

        try
        {
            var seenDeclaration = false;
            var seenFileScopedNamespace = false;
            var seenNamespaceBlock = false;

            while (Current.Kind != TokenKind.EndOfFile)
            {
                var start = _position;

                if (Current.Kind == TokenKind.UsingKeyword)
                {
                    if (seenDeclaration)
                    {
                        Report("TRM1003", "Using directives must precede declarations in their scope.", Current.Span);
                    }

                    children.Add(ParseDirective(isNamespace: false));
                }
                else if (Current.Kind == TokenKind.NamespaceKeyword)
                {
                    var declaration = ParseNamespaceDeclaration(allowFileScoped: true);
                    if (declaration.Kind == SyntaxKind.NamespaceDeclaration)
                    {
                        if (seenDeclaration || seenFileScopedNamespace || seenNamespaceBlock)
                        {
                            Report("TRM1003",
                                "A file-scoped namespace must be the only namespace form and precede declarations.",
                                declaration.Span);
                        }
                        seenFileScopedNamespace = true;
                    }
                    else
                    {
                        if (seenFileScopedNamespace)
                        {
                            Report("TRM1003",
                                "File-scoped namespaces and namespace blocks cannot be mixed.",
                                declaration.Span);
                        }
                        seenNamespaceBlock = true;
                        seenDeclaration = true;
                    }
                    children.Add(declaration);
                }
                else
                {
                    seenDeclaration = true;
                    children.Add(ParseDeclaration(null, isFileScope: true));
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

        ValidateDeferredPunctuation();
        return Node(SyntaxKind.CompilationUnit, children);
    }

    private SyntaxNode ParseNamespaceDeclaration(bool allowFileScoped)
    {
        Enter();
        var children = new List<SyntaxElement> { Take(), Expect(TokenKind.Identifier) };
        while (Current.Kind == TokenKind.Dot)
        {
            children.Add(TakeDot(qualified: true));
            children.Add(Expect(TokenKind.Identifier));
        }

        if (Current.Kind != TokenKind.OpenBrace)
        {
            if (!allowFileScoped)
            {
                Report("TRM1003", "A namespace block cannot contain a file-scoped namespace.", Current.Span);
            }
            children.Add(Expect(TokenKind.Semicolon));
            _depth--;
            return Node(SyntaxKind.NamespaceDeclaration, children);
        }

        children.Add(Take());
        var seenDeclaration = false;
        while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
        {
            var start = _position;
            if (Current.Kind == TokenKind.UsingKeyword)
            {
                if (seenDeclaration)
                {
                    Report("TRM1003", "Using directives must precede declarations in their scope.", Current.Span);
                }
                children.Add(ParseDirective(isNamespace: false));
            }
            else if (Current.Kind == TokenKind.NamespaceKeyword)
            {
                seenDeclaration = true;
                children.Add(ParseNamespaceDeclaration(allowFileScoped: false));
            }
            else
            {
                seenDeclaration = true;
                children.Add(ParseDeclaration(null, isFileScope: true));
            }
            EnsureProgress(start, children);
        }

        children.Add(Expect(TokenKind.CloseBrace));
        _depth--;
        return Node(SyntaxKind.NamespaceBlockDeclaration, children);
    }

    private SyntaxNode ParseDirective(bool isNamespace)
    {
        var children = new List<SyntaxElement> { Take(), Expect(TokenKind.Identifier) };

        while (Current.Kind == TokenKind.Dot)
        {
            children.Add(TakeDot(qualified: true));
            children.Add(Expect(TokenKind.Identifier));
        }

        children.Add(Expect(TokenKind.Semicolon));

        return Node(isNamespace ? SyntaxKind.NamespaceDeclaration : SyntaxKind.UsingDirective, children);
    }

    private SyntaxNode ParseAttribute()
    {
        var attribute = new List<SyntaxElement> { Take() };
        if (Current.Kind == TokenKind.ReturnKeyword && Peek(1).Kind == TokenKind.Colon)
        {
            attribute.Add(Take());
            attribute.Add(Take());
        }
        else if (Current.Kind == TokenKind.Identifier && Current.Text == "this"
            && Peek(1).Kind == TokenKind.Colon)
        {
            attribute.Add(Take());
            attribute.Add(Take());
        }
        else if (Current.Kind == TokenKind.Identifier && Current.Name == "method"
            && Peek(1).Kind == TokenKind.Colon)
        {
            attribute.Add(Take());
            attribute.Add(Take());
        }
        else if (Current.Kind == TokenKind.Identifier && Current.Text == "param"
            && Peek(1).Kind == TokenKind.OpenParen)
        {
            attribute.Add(Take());
            attribute.Add(ExpectAttached(TokenKind.OpenParen));
            attribute.Add(Expect(TokenKind.Identifier));
            attribute.Add(Expect(TokenKind.CloseParen));
            attribute.Add(Expect(TokenKind.Colon));
        }
        var name = Expect(TokenKind.Identifier);
        attribute.Add(name);
        var attributeName = name.Token.Name;
        while (Current.Kind == TokenKind.Dot)
        {
            attribute.Add(TakeDot(qualified: true));
            var component = Expect(TokenKind.Identifier);
            attribute.Add(component);
            attributeName = string.Concat(attributeName, ".", component.Token.Name);
        }
        if (Current.Kind == TokenKind.OpenParen)
        {
            attribute.Add(ExpectAttached(TokenKind.OpenParen));
            if (IsSourceRelationshipAttribute(attributeName))
            {
                var nestedParentheses = 0;
                while (Current.Kind != TokenKind.EndOfFile)
                {
                    if (Current.Kind == TokenKind.CloseParen)
                    {
                        if (nestedParentheses == 0)
                        {
                            break;
                        }
                        nestedParentheses--;
                    }
                    else if (Current.Kind == TokenKind.OpenParen)
                    {
                        nestedParentheses++;
                    }
                    attribute.Add(Take());
                }
            }
            else if (IsNativeImportAttributeName(attributeName)
                || IsNativeCallableAttributeName(attributeName))
            {
                ParseNativeDeclarationAttributeArguments(attribute);
            }
            else if (IsNativeCallConvAttributeName(attributeName))
            {
                if (Current.Kind != TokenKind.CloseParen)
                {
                    attribute.Add(ParseExpression());
                }
            }
            else if (attributeName is "Terrum.Interop.NativeOutput" or "NativeOutput")
            {
                attribute.Add(Expect(TokenKind.Identifier));
                attribute.Add(Expect(TokenKind.Colon));
                attribute.Add(Expect(TokenKind.Identifier));
                while (Current.Kind == TokenKind.Dot)
                {
                    attribute.Add(TakeDot(qualified: true));
                    attribute.Add(Expect(TokenKind.Identifier));
                }
            }
            else if (attributeName == "Terrum.Interop.NativeVTableMethod")
            {
                attribute.Add(Expect(TokenKind.NumericLiteral));
            }
            else if (attributeName == "Terrum.Interop.NativeParameter")
            {
                while (Current.Kind is not TokenKind.CloseParen
                    and not TokenKind.EndOfFile)
                {
                    attribute.Add(Current.Kind is TokenKind.Identifier or TokenKind.NumericLiteral
                        or TokenKind.StringLiteral ? Take() : Expect(TokenKind.Identifier));
                    if (Current.Kind != TokenKind.Comma)
                    {
                        break;
                    }
                    attribute.Add(Take());
                }
            }
            else if (attributeName == "Layout")
            {
                while (Current.Kind is not (TokenKind.CloseParen or TokenKind.EndOfFile))
                {
                    attribute.Add(Current.Kind is TokenKind.Identifier or TokenKind.NumericLiteral
                        or TokenKind.Dot or TokenKind.Colon or TokenKind.Comma
                        ? Take() : Expect(TokenKind.Identifier));
                }
            }
            else if (Current.Kind == TokenKind.StringLiteral)
            {
                attribute.Add(Take());
            }
            else
            {
                attribute.Add(Expect(TokenKind.Identifier));
            }
            attribute.Add(Expect(TokenKind.CloseParen));
        }
        attribute.Add(Expect(TokenKind.CloseBracket));
        return Node(SyntaxKind.Attribute, attribute);
    }

    private static bool IsSourceRelationshipAttribute(string name)
        => name is "DependsOn" or "Captures" or "ForwardsBacking"
            or "Terrum.CodeAnalysis.DependsOn"
            or "Terrum.CodeAnalysis.Captures"
            or "Terrum.CodeAnalysis.ForwardsBacking";

    private static bool IsNativeCallConvAttributeName(string name)
        => name is "NativeCallConv" or "Terrum.Interop.NativeCallConv";

    private static bool IsNativeImportAttributeName(string name)
        => name is "NativeImport" or "Terrum.Interop.NativeImport";

    private static bool IsNativeCallableAttributeName(string name)
        => name is "NativeCallable" or "Terrum.Interop.NativeCallable";

    private void ParseNativeDeclarationAttributeArguments(List<SyntaxElement> attribute)
    {
        while (Current.Kind is not (TokenKind.CloseParen or TokenKind.EndOfFile))
        {
            if (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Colon)
            {
                attribute.Add(Take());
                attribute.Add(Take());
                attribute.Add(ParseExpression());
            }
            else if (Current.Kind == TokenKind.StringLiteral)
            {
                attribute.Add(Take());
            }
            else
            {
                attribute.Add(ParseExpression());
            }

            if (Current.Kind != TokenKind.Comma)
            {
                break;
            }
            attribute.Add(Take());
        }
    }

    private bool IsNativeCallConvTypeAttributeStart(int offset = 0)
    {
        if (Peek(offset).Kind != TokenKind.OpenBracket)
        {
            return false;
        }

        var shortName = Peek(offset + 1).Name == "NativeCallConv"
            && Peek(offset + 2).Kind is TokenKind.OpenParen or TokenKind.CloseBracket;
        var qualifiedName = Peek(offset + 1).Name == "Terrum"
            && Peek(offset + 2).Kind == TokenKind.Dot
            && Peek(offset + 3).Name == "Interop"
            && Peek(offset + 4).Kind == TokenKind.Dot
            && Peek(offset + 5).Name == "NativeCallConv";
        return shortName || qualifiedName;
    }

    private int NativeCallConvTypeAttributeEndOffset(int offset)
    {
        while (Peek(offset).Kind is not (TokenKind.CloseBracket or TokenKind.EndOfFile))
        {
            offset++;
        }

        return Peek(offset).Kind == TokenKind.CloseBracket ? offset + 1 : offset;
    }

    private SyntaxNode ParseDeclaration(string? containingType, bool isFileScope = false,
        bool isInterfaceMember = false)
    {
        Enter();
        var children = new List<SyntaxElement>();

        while (Current.Kind == TokenKind.OpenBracket && !IsNativeCallConvTypeAttributeStart())
        {
            children.Add(ParseAttribute());
        }

        while (IsModifier(Current.Kind)
            || (isInterfaceMember && Current.Kind == TokenKind.AsyncKeyword)
            || IsContextualDeclarationPrefix())
        {
            children.Add(Current.Kind == TokenKind.Identifier && Current.Text == "friend"
                ? ParseFriendVisibility() : Take());
        }

        SyntaxKind kind;

        if (isFileScope && Current.Kind == TokenKind.AliasKeyword)
        {
            children.Add(Take());
            children.Add(Expect(TokenKind.Identifier));
            if (Current.Kind == TokenKind.Less)
            {
                children.Add(ParseTypeParameters());
            }
            children.Add(Current.Kind == TokenKind.Equals ? TakeSeparated() : Expect(TokenKind.Equals));
            children.Add(ParseType());
            children.Add(Expect(TokenKind.Semicolon));
            kind = SyntaxKind.AliasDeclaration;
        }
        else if (isInterfaceMember
            && Current.Kind is TokenKind.StructKeyword or TokenKind.ClassKeyword or TokenKind.InterfaceKeyword)
        {
            Report("TRM1002", "An interface cannot declare a nested nominal type.", Current.Span);
            children.Add(SkipDeclaration());
            kind = SyntaxKind.SkippedSyntax;
        }
        else if (Current.Kind is TokenKind.StructKeyword or TokenKind.ClassKeyword)
        {
            var isClass = Current.Kind == TokenKind.ClassKeyword;
            children.Add(Take());
            var name = Expect(TokenKind.Identifier);
            children.Add(name);
            if (Current.Kind == TokenKind.Less)
            {
                children.Add(ParseTypeParameters());
            }
            if (Current.Kind == TokenKind.Colon)
            {
                children.Add(ParseConformances());
            }
            while (Current.Kind == TokenKind.WhereKeyword)
            {
                children.Add(ParseConstraint());
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
        else if (!isInterfaceMember && Current.Kind == TokenKind.InterfaceKeyword)
        {
            children.Add(Take());
            children.Add(Expect(TokenKind.Identifier));
            if (Current.Kind == TokenKind.Less)
            {
                children.Add(ParseTypeParameters(interfaceParameters: true));
            }
            if (Current.Kind == TokenKind.Colon)
            {
                children.Add(ParseConformances());
            }
            while (Current.Kind == TokenKind.WhereKeyword)
            {
                children.Add(ParseConstraint());
            }
            children.Add(Expect(TokenKind.OpenBrace));
            while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
            {
                var start = _position;
                children.Add(ParseDeclaration(null, isInterfaceMember: true));
                EnsureProgress(start, children);
            }
            children.Add(Expect(TokenKind.CloseBrace));
            kind = SyntaxKind.InterfaceDeclaration;
        }
        else if (isInterfaceMember && Current.Kind == TokenKind.Identifier && Current.Text == "abstract"
            && Peek(1).Kind == TokenKind.StaticKeyword)
        {
            Report("TRM1002", "An interface member does not accept an abstract modifier.", Current.Span);
            children.Add(SkipDeclaration());
            kind = SyntaxKind.SkippedSyntax;
        }
        else if (isInterfaceMember && Current.Kind == TokenKind.Identifier && Current.Text == "type")
        {
            children.Add(Take());
            children.Add(Expect(TokenKind.Identifier));
            if (Current.Kind == TokenKind.Equals)
            {
                children.Add(TakeSeparated());
                children.Add(ParseType());
            }
            children.Add(Expect(TokenKind.Semicolon));
            kind = SyntaxKind.AssociatedTypeRequirement;
        }
        else if (isInterfaceMember && Current.Kind == TokenKind.Identifier && Current.Text == "const")
        {
            children.Add(Take());
            children.Add(ParseType());
            children.Add(Expect(TokenKind.Identifier));
            if (Current.Kind == TokenKind.Equals)
            {
                children.Add(TakeSeparated());
                children.Add(ParseExpression());
            }
            children.Add(Expect(TokenKind.Semicolon));
            kind = SyntaxKind.InterfaceConstantDeclaration;
        }
        else if (containingType is not null && Current.Kind == TokenKind.Identifier
            && Current.Name == containingType && Peek(1).Kind == TokenKind.OpenParen)
        {
            children.Add(Take());
            children.Add(ParseParameters());
            children.Add(ParseCallableBody());
            kind = SyntaxKind.ConstructorDeclaration;
        }
        else if (IsTypeStart(Current.Kind) || IsNativeCallConvTypeAttributeStart())
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
                if (isInterfaceMember)
                {
                    ParseContractClauses(children);
                }
                children.Add(Current.Kind == TokenKind.Semicolon ? Take() : ParseCallableBody());
                kind = SyntaxKind.OperatorDeclaration;
            }
            else if (isInterfaceMember && Current.Kind == TokenKind.Identifier && Current.Text == "this")
            {
                children.Add(Take());
                children.Add(ParseIndexParameters());
                children.Add(Expect(TokenKind.OpenBrace));
                children.Add(ParseInterfaceAccessor("get"));
                var indexerAccessor = NextInterfaceAccessorName();
                if (indexerAccessor is "set" or "init")
                {
                    children.Add(ParseInterfaceAccessor(indexerAccessor));
                }
                if (Current.Kind != TokenKind.CloseBrace)
                {
                    Report("TRM1002", "An interface indexer allows one getter and at most one setter or initializer.",
                        Current.Span);
                    children.Add(SkipDeclaration());
                }
                children.Add(Expect(TokenKind.CloseBrace));
                kind = SyntaxKind.IndexerDeclaration;
            }
            else
            {
                children.Add(Expect(TokenKind.Identifier));

                if (Current.Kind == TokenKind.Less)
                {
                    children.Add(ParseTypeParameters(interfaceMember: isInterfaceMember));
                }
                if (Current.Kind == TokenKind.OpenParen)
                {
                    children.Add(ParseParameters());
                    while (Current.Kind == TokenKind.WhereKeyword)
                    {
                        children.Add(ParseConstraint());
                    }
                    if (isInterfaceMember)
                    {
                        ParseContractClauses(children);
                    }
                    children.Add(ParseCallableBody());
                    kind = SyntaxKind.FunctionDeclaration;
                }
                else if ((containingType is not null || isInterfaceMember) && children.TrueForAll(static child =>
                    child is not SyntaxNode { Kind: SyntaxKind.TypeParameterList }))
                {
                    if (!isInterfaceMember && Current.Kind == TokenKind.Arrow)
                    {
                        children.Add(ParseCallableBody());
                        kind = SyntaxKind.PropertyDeclaration;
                    }
                    else if (Current.Kind == TokenKind.OpenBrace)
                    {
                        children.Add(Take());
                        if (isInterfaceMember)
                        {
                            children.Add(ParseInterfaceAccessor("get"));
                            var propertyAccessor = NextInterfaceAccessorName();
                            if (propertyAccessor is "set" or "init")
                            {
                                children.Add(ParseInterfaceAccessor(propertyAccessor));
                            }
                            if (Current.Kind != TokenKind.CloseBrace)
                            {
                                Report("TRM1002", "An interface property allows one getter and at most one setter or initializer.",
                                    Current.Span);
                                children.Add(SkipDeclaration());
                            }
                        }
                        else if (isInterfaceMember)
                        {
                            Report("TRM1002",
                                "An interface member must be a callable, associated declaration, property, or indexer.",
                                Current.Span);
                            children.Add(SkipDeclaration());
                            kind = SyntaxKind.SkippedSyntax;
                        }
                        else
                        {
                            var accessor = new List<SyntaxElement>();
                            while (Current.Kind == TokenKind.OpenBracket)
                            {
                                accessor.Add(ParseAttribute());
                            }
                            while (Current.Kind == TokenKind.UnsafeKeyword)
                            {
                                accessor.Add(Take());
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
                        }
                        children.Add(Expect(TokenKind.CloseBrace));
                        kind = SyntaxKind.PropertyDeclaration;
                    }
                    else
                    {
                        if (Current.Kind == TokenKind.Equals)
                        {
                            children.Add(TakeSeparated());
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
        var missing = _missingTokens;
        if (IsNativeCallConvTypeAttributeStart())
        {
            var attribute = ParseAttribute();
            var attributedType = ParseType();
            _depth--;
            return Node(SyntaxKind.AttributedTypeName, [attribute, attributedType]);
        }

        var children = new List<SyntaxElement>();
        if (Current.Kind == TokenKind.WriteKeyword)
        {
            children.Add(Take());
        }
        if (Current.Kind == TokenKind.GcKeyword)
        {
            children.Add(Take());
        }

        var typeKind = SyntaxKind.TypeName;
        if (Current.Kind == TokenKind.RefKeyword)
        {
            typeKind = SyntaxKind.ReferenceTypeName;
            children.Add(Take());
            children.Add(ExpectAttached(TokenKind.Less));
            if (Current.Kind == TokenKind.Identifier && Current.Text == "init")
            {
                children.Add(Take());
            }
            children.Add(ParseType());
            children.Add(ExpectTypeCloser(missing));
        }
        else
        {
            children.Add(Current.Kind == TokenKind.VoidKeyword ? Take() : Expect(TokenKind.Identifier));

            while (Current.Kind == TokenKind.Dot)
            {
                children.Add(TakeDot(qualified: true));
                children.Add(Expect(TokenKind.Identifier));
            }

            var isInlineArray = children.Count == 1
                && children[0] is SyntaxTokenElement { Token.Kind: TokenKind.Identifier, Token.Text: "InlineArray" };
            if (isInlineArray && Current.Kind == TokenKind.Less)
            {
                children.Add(ExpectAttached(TokenKind.Less));
                children.Add(ParseType());
                children.Add(Expect(TokenKind.Comma));
                children.Add(Expect(TokenKind.NumericLiteral));
                children.Add(ExpectTypeCloser(missing));
                if (Current.Kind is TokenKind.Question or TokenKind.QuestionQuestion)
                {
                    children.Add(Take());
                }

                _depth--;
                return Node(SyntaxKind.InlineArrayTypeName, children);
            }

            if (Current.Kind == TokenKind.Less)
            {
                children.Add(ExpectAttached(TokenKind.Less));
                children.Add(ParseTypeArgument());

                while (Current.Kind == TokenKind.Comma)
                {
                    children.Add(Take());
                    children.Add(ParseTypeArgument());
                }

                children.Add(ExpectTypeCloser(missing));
            }
        }

        ParseOptionalTypeSuffix(children, missing);

        _depth--;

        return Node(typeKind, children);
    }

    private SyntaxNode ParseTypeArgument()
    {
        if (Current.Kind == TokenKind.Underscore)
        {
            return Node(SyntaxKind.ExistentialTypeArgument, [Take()]);
        }

        if (Current.Kind is TokenKind.NumericLiteral or TokenKind.StringLiteral
            or TokenKind.CharacterLiteral or TokenKind.TrueKeyword or TokenKind.FalseKeyword
            or TokenKind.NullKeyword)
        {
            return Node(SyntaxKind.ConstantGenericArgument, [ParseExpression(13)]);
        }

        if (Current.Kind is TokenKind.Plus or TokenKind.Minus
            && Peek(1).Kind == TokenKind.NumericLiteral)
        {
            return Node(SyntaxKind.ConstantGenericArgument, [ParseExpression(13)]);
        }

        return ParseType();
    }

    private void ParseOptionalTypeSuffix(List<SyntaxElement> children, int missing)
    {
        var hasModifier = false;
        while (Current.Kind is TokenKind.Question or TokenKind.QuestionQuestion)
        {
            if (!hasModifier && !IsAttachedToPrevious())
            {
                Report("TRM1011", "An optional type modifier must attach to the type's final token.", Current.Span);
            }
            var modifier = Take();
            if (missing == _missingTokens)
            {
                _ = _typePunctuation.Add(modifier.Span);
            }
            children.Add(modifier);
            for (var index = hasModifier ? 0 : 1; index < modifier.Token.Text.Length; index++)
            {
                Report("TRM1005", "An optional modifier cannot be repeated on the same source type.",
                    new TextSpan(modifier.Span.Start + index, 1));
            }
            hasModifier = true;
        }
    }

    private SyntaxNode ParseParameters()
    {
        var children = new List<SyntaxElement> { ExpectAttached(TokenKind.OpenParen) };

        while (Current.Kind is not (TokenKind.CloseParen or TokenKind.OpenBrace or TokenKind.EndOfFile))
        {
            var start = _position;
            children.Add(ParseParameter());

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

    private SyntaxNode ParseIndexParameters()
    {
        var children = new List<SyntaxElement> { Expect(TokenKind.OpenBracket) };
        while (Current.Kind is not (TokenKind.CloseBracket or TokenKind.OpenBrace or TokenKind.EndOfFile))
        {
            var start = _position;
            children.Add(ParseParameter());
            if (Current.Kind != TokenKind.Comma)
            {
                EnsureProgress(start, children);
                break;
            }
            children.Add(Take());
            if (Current.Kind == TokenKind.CloseBracket)
            {
                Report("TRM1001", "Expected an index parameter after ','.", Current.Span);
            }
        }
        children.Add(Expect(TokenKind.CloseBracket));
        return Node(SyntaxKind.IndexerParameterList, children);
    }

    private SyntaxNode ParseParameter()
    {
        var parameter = new List<SyntaxElement>();
        while (Current.Kind == TokenKind.OpenBracket && !IsNativeCallConvTypeAttributeStart())
        {
            parameter.Add(ParseAttribute());
        }
        if (Current.Kind == TokenKind.Identifier && Current.Text == "init"
            && Peek(1).Kind == TokenKind.RefKeyword)
        {
            var missing = _missingTokens;
            parameter.Add(Take());
            parameter.Add(Take());
            parameter.Add(ExpectAttached(TokenKind.Less));
            parameter.Add(ParseType());
            parameter.Add(ExpectTypeCloser(missing));
        }
        else if (Current.Kind == TokenKind.Identifier && Current.Text == "implicit"
            && Peek(1).Kind == TokenKind.RefKeyword)
        {
            var missing = _missingTokens;
            parameter.Add(Take());
            parameter.Add(Take());
            parameter.Add(ExpectAttached(TokenKind.Less));
            parameter.Add(ParseType());
            parameter.Add(ExpectTypeCloser(missing));
        }
        else if (Current.Kind == TokenKind.RefKeyword && Peek(1).Kind == TokenKind.Less)
        {
            parameter.Add(ParseType());
        }
        else
        {
            ParsePassingMode(parameter);
            parameter.Add(ParseType());
        }
        parameter.Add(Expect(TokenKind.Identifier));
        return Node(SyntaxKind.Parameter, parameter);
    }

    private bool IsInterfaceAccessor(string name) =>
        Current.Kind == TokenKind.Identifier && Current.Text == name;

    private string? NextInterfaceAccessorName()
    {
        var offset = 0;
        while (Peek(offset).Kind == TokenKind.OpenBracket)
        {
            var depth = 0;
            do
            {
                var kind = Peek(offset++).Kind;
                if (kind == TokenKind.OpenBracket)
                {
                    depth++;
                }
                else if (kind == TokenKind.CloseBracket)
                {
                    depth--;
                }
            }
            while (depth > 0 && Peek(offset).Kind != TokenKind.EndOfFile);
        }
        while (Peek(offset).Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword
            or TokenKind.PrivateKeyword or TokenKind.MutKeyword or TokenKind.UnsafeKeyword)
        {
            offset++;
        }
        var token = Peek(offset);
        return token.Kind == TokenKind.Identifier && (token.Text is "get" or "set" or "init")
            ? token.Text : null;
    }

    private SyntaxNode ParseInterfaceAccessor(string name)
    {
        var kind = name switch {
            "get" => SyntaxKind.GetAccessor,
            "set" => SyntaxKind.SetAccessor,
            "init" => SyntaxKind.InitAccessor,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        var children = new List<SyntaxElement>();
        while (Current.Kind == TokenKind.OpenBracket)
        {
            children.Add(ParseAttribute());
        }
        while (Current.Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword or TokenKind.PrivateKeyword
            or TokenKind.MutKeyword or TokenKind.UnsafeKeyword)
        {
            children.Add(Take());
        }
        if (!IsInterfaceAccessor(name))
        {
            Report("TRM1002", $"Expected the interface '{name}' accessor.", Current.Span);
            children.Add(SkipDeclaration());
            return Node(kind, children);
        }
        children.Add(Take());
        ParseContractClauses(children);
        children.Add(ParseCallableBody());
        return Node(kind, children);
    }

    private void ParseContractClauses(List<SyntaxElement> children)
    {
        while (Current.Kind == TokenKind.RequireKeyword
            || (Current.Kind == TokenKind.Identifier && Current.Text == "ensure"))
        {
            var clause = new List<SyntaxElement> { Take() };
            if (clause[0] is SyntaxTokenElement { Token.Kind: TokenKind.RequireKeyword }
                && Current.Kind == TokenKind.Less)
            {
                var missing = _missingTokens;
                clause.Add(Take());
                clause.Add(ParseType());
                clause.Add(ExpectTypeCloser(missing));
            }
            clause.Add(ParseExpression());
            children.Add(Node(SyntaxKind.ContractClause, clause));
        }
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
        if (Current.Kind == TokenKind.Semicolon)
        {
            return Node(SyntaxKind.ExternalBody, [Take()]);
        }

        if (Current.Kind != TokenKind.Arrow)
        {
            return ParseBlock();
        }

        var children = new List<SyntaxElement> { TakeSeparated() };
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
            case TokenKind.SwitchKeyword:
            {
                node = ParseSwitchStatement();
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
            case TokenKind.UnsafeKeyword when Peek(1).Kind == TokenKind.OpenBrace:
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
                if (Current.Kind == TokenKind.ContinueKeyword
                    && Peek(1).Kind is TokenKind.Underscore or TokenKind.NumericLiteral or TokenKind.OpenParen)
                {
                    node = ParseSwitchTransferStatement();
                    break;
                }
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
                    [Take(), ExpectSeparated(TokenKind.Equals), ParseExpression(), Expect(TokenKind.Semicolon)]);
                break;
            }
            default:
            {
                if (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Colon
                    && Peek(2).Kind is TokenKind.WhileKeyword or TokenKind.ForKeyword or TokenKind.DoKeyword)
                {
                    var name = Take();
                    var colon = TakeNameColon();
                    if (_source.GetLinePosition(colon.Span.Start).Line
                        != _source.GetLinePosition(Current.Span.Start).Line)
                    {
                        Report("TRM1012", "A loop label and its loop introducer must be on the same source line.", colon.Span);
                    }
                    node = Node(SyntaxKind.LabeledLoopStatement, [name, colon, ParseStatement()]);
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

    private SyntaxNode ParseSwitchStatement()
    {
        var children = new List<SyntaxElement> {
            Take(), Expect(TokenKind.OpenParen), ParseExpression(), Expect(TokenKind.CloseParen),
            Expect(TokenKind.OpenBrace),
        };
        while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
        {
            var start = _position;
            children.Add(ParseSwitchArm());
            if (Current.Kind == TokenKind.Comma)
            {
                children.Add(Take());
                if (Current.Kind == TokenKind.CloseBrace)
                {
                    break;
                }
            }
            else if (Current.Kind != TokenKind.CloseBrace)
            {
                Report("TRM1001", "Switch statement arms must be comma-separated.", Current.Span);
            }
            EnsureProgress(start, children);
        }
        children.Add(Expect(TokenKind.CloseBrace));
        return Node(SyntaxKind.SwitchStatement, children);
    }

    private SyntaxNode ParseSwitchArm()
    {
        var children = new List<SyntaxElement>();
        while (Current.Kind == TokenKind.OpenBracket)
        {
            children.Add(ParseAttribute());
        }
        children.Add(ParseSwitchPattern());
        if (Current.Kind == TokenKind.Identifier && Current.Text == "when")
        {
            children.Add(Node(SyntaxKind.SwitchWhenClause, [Take(), ParseExpression()]));
        }
        children.Add(Expect(TokenKind.Arrow));
        children.Add(ParseBlock());
        return Node(SyntaxKind.SwitchArm, children);
    }

    private SyntaxNode ParseSwitchPattern()
    {
        Enter();
        var pattern = ParseSwitchPatternOr();
        _depth--;
        return pattern;
    }

    private SyntaxNode ParseSwitchPatternOr()
    {
        var left = ParseSwitchPatternAnd();
        while (Current.Kind == TokenKind.Identifier && Current.Text == "or")
        {
            var op = Take();
            left = Node(SyntaxKind.SwitchOrPattern,
                [left, op, ParseSwitchPatternAnd()]);
        }
        return left;
    }

    private SyntaxNode ParseSwitchPatternAnd()
    {
        var left = ParseSwitchPatternNot();
        while (Current.Kind == TokenKind.Identifier && Current.Text == "and")
        {
            var op = Take();
            left = Node(SyntaxKind.SwitchAndPattern,
                [left, op, ParseSwitchPatternNot()]);
        }
        return left;
    }

    private SyntaxNode ParseSwitchPatternNot()
    {
        var operators = new List<SyntaxElement>();
        while (Current.Kind == TokenKind.Identifier && Current.Text == "not")
        {
            operators.Add(Take());
        }

        var pattern = ParseSwitchPatternAtom();
        for (var index = operators.Count - 1; index >= 0; index--)
        {
            pattern = Node(SyntaxKind.SwitchNotPattern, [operators[index], pattern]);
        }
        return pattern;
    }

    private SyntaxNode ParseSwitchPatternAtom()
    {
        if (Current.Kind == TokenKind.Underscore)
        {
            return Node(SyntaxKind.SwitchWildcardPattern, [Take()]);
        }
        if (Current.Kind == TokenKind.NullKeyword)
        {
            return Node(SyntaxKind.SwitchNullPattern, [Take()]);
        }
        if (Current.Kind == TokenKind.RefKeyword
            || (Current.Kind == TokenKind.WriteKeyword && Peek(1).Kind == TokenKind.RefKeyword)
            || (Current.Kind == TokenKind.MutKeyword
                && (Peek(1).Kind == TokenKind.RefKeyword
                    || (Peek(1).Kind == TokenKind.WriteKeyword
                        && Peek(2).Kind == TokenKind.RefKeyword))))
        {
            return ParseSwitchReferenceTypePattern();
        }
        if (Current.Kind == TokenKind.VarKeyword
            || (Current.Kind == TokenKind.MutKeyword && Peek(1).Kind == TokenKind.VarKeyword))
        {
            var children = new List<SyntaxElement>();
            if (Current.Kind == TokenKind.MutKeyword)
            {
                children.Add(Take());
            }
            children.Add(Take());
            if (Current.Kind == TokenKind.Question && IsAttachedToPrevious())
            {
                children.Add(Take());
            }
            children.Add(Current.Kind == TokenKind.Underscore
                ? Take() : Expect(TokenKind.Identifier));
            return Node(SyntaxKind.SwitchInferredBindingPattern, children);
        }
        var mutable = Current.Kind == TokenKind.MutKeyword;
        var typeOffset = mutable ? 1 : 0;
        if (LooksLikeSwitchGenericTypePattern(typeOffset))
        {
            var children = new List<SyntaxElement>();
            if (mutable)
            {
                children.Add(Take());
            }
            var genericType = ParseSwitchGenericTypePattern();
            if (Current.Kind == TokenKind.Dot)
            {
                return ParseSwitchQualifiedPattern(genericType, includeCaseName: true);
            }
            children.Add(genericType);
            if (Current.Kind == TokenKind.Identifier
                && Current.Text is not ("when" or "and" or "or"))
            {
                children.Add(Take());
            }
            return Node(SyntaxKind.SwitchTypedBindingPattern, children);
        }
        if (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Dot)
        {
            var qualifiedType = ParseType();
            if (Current.Kind == TokenKind.Identifier
                && Current.Text is not ("when" or "and" or "or"))
            {
                return Node(SyntaxKind.SwitchTypedBindingPattern,
                    [qualifiedType, Take()]);
            }
            return ParseSwitchQualifiedPattern(qualifiedType, includeCaseName: false);
        }
        var typeStart = Peek(typeOffset).Kind is TokenKind.Identifier or TokenKind.WriteKeyword
            or TokenKind.GcKeyword or TokenKind.RefKeyword or TokenKind.VoidKeyword;
        var typeCoreOffset = typeOffset;
        if (Peek(typeCoreOffset).Kind == TokenKind.WriteKeyword)
        {
            typeCoreOffset++;
        }
        if (Peek(typeCoreOffset).Kind == TokenKind.GcKeyword)
        {
            typeCoreOffset++;
        }
        var afterType = Peek(typeCoreOffset + 1).Kind;
        if (typeStart && (mutable || Peek(typeOffset).Kind == TokenKind.WriteKeyword
            || afterType is TokenKind.Identifier or TokenKind.Question
                or TokenKind.QuestionQuestion or TokenKind.Less))
        {
            var children = new List<SyntaxElement>();
            if (mutable)
            {
                children.Add(Take());
            }
            children.Add(ParseType());
            if (Current.Kind == TokenKind.Identifier)
            {
                children.Add(Take());
            }
            return Node(SyntaxKind.SwitchTypedBindingPattern, children);
        }
        if (Current.Kind == TokenKind.OpenParen)
        {
            var children = new List<SyntaxElement> { Take(), ParseSwitchPattern() };
            if (Current.Kind != TokenKind.Comma)
            {
                children.Add(Expect(TokenKind.CloseParen));
                return Node(SyntaxKind.SwitchParenthesizedPattern, children);
            }

            children.Add(Take());
            children.Add(ParseSwitchPattern());
            while (Current.Kind == TokenKind.Comma)
            {
                children.Add(Take());
                children.Add(ParseSwitchPattern());
            }
            children.Add(Expect(TokenKind.CloseParen));
            return Node(SyntaxKind.SwitchTuplePattern, children);
        }

        return Node(SyntaxKind.SwitchConstantPattern, [ParseExpression()]);
    }

    private SyntaxNode ParseSwitchQualifiedPattern(SyntaxNode type, bool includeCaseName)
    {
        var children = new List<SyntaxElement> { type };
        if (includeCaseName)
        {
            children.Add(TakeDot(qualified: true));
            children.Add(Expect(TokenKind.Identifier));
        }
        if (Current.Kind == TokenKind.OpenParen)
        {
            children.Add(Take());
            if (Current.Kind != TokenKind.CloseParen)
            {
                children.Add(ParseSwitchPattern());
                while (Current.Kind == TokenKind.Comma)
                {
                    children.Add(Take());
                    children.Add(ParseSwitchPattern());
                }
            }
            children.Add(Expect(TokenKind.CloseParen));
        }
        return Node(SyntaxKind.SwitchQualifiedPattern, children);
    }

    private bool LooksLikeSwitchGenericTypePattern(int offset)
    {
        if (Peek(offset).Kind == TokenKind.WriteKeyword)
        {
            offset++;
        }
        if (Peek(offset).Kind != TokenKind.Identifier)
        {
            return false;
        }
        offset++;
        while (Peek(offset).Kind == TokenKind.Dot && Peek(offset + 1).Kind == TokenKind.Identifier)
        {
            if ((offset & 63) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }
            offset += 2;
        }
        if (Peek(offset).Kind != TokenKind.Less)
        {
            return false;
        }

        var depth = 0;
        for (; offset < _tokens.Length - _position; offset++)
        {
            if ((offset & 63) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }
            var kind = Peek(offset).Kind;
            if (kind == TokenKind.Less)
            {
                if (++depth > MaximumDepth)
                {
                    return false;
                }
            }
            else if (kind == TokenKind.Greater)
            {
                if (--depth == 0)
                {
                    return true;
                }
                if (depth < 0)
                {
                    return false;
                }
            }
            else if (kind is TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned)
            {
                depth -= kind == TokenKind.ShiftRight ? 2 : 3;
                if (depth <= 0)
                {
                    return true;
                }
            }
            else if (kind is TokenKind.EndOfFile or TokenKind.Arrow
                or TokenKind.CloseBrace or TokenKind.Comma)
            {
                return false;
            }
        }
        return false;
    }

    private SyntaxNode ParseSwitchGenericTypePattern()
    {
        Enter();
        var children = new List<SyntaxElement>();
        if (Current.Kind == TokenKind.WriteKeyword)
        {
            children.Add(Take());
        }
        children.Add(Expect(TokenKind.Identifier));
        while (Current.Kind == TokenKind.Dot)
        {
            children.Add(TakeDot(qualified: true));
            children.Add(Expect(TokenKind.Identifier));
        }
        var missing = _missingTokens;
        children.Add(ExpectAttached(TokenKind.Less));
        children.Add(ParseSwitchPatternTypeArgument());
        while (Current.Kind == TokenKind.Comma)
        {
            children.Add(Take());
            children.Add(ParseSwitchPatternTypeArgument());
        }
        children.Add(ExpectTypeCloser(missing));
        if (Current.Kind == TokenKind.Question && IsAttachedToPrevious())
        {
            children.Add(Take());
        }
        var pattern = Node(SyntaxKind.SwitchGenericTypePattern, children);
        _depth--;
        return pattern;
    }

    private SyntaxNode ParseSwitchPatternTypeArgument()
    {
        if (Current.Kind == TokenKind.Underscore)
        {
            return Node(SyntaxKind.SwitchPatternTypeArgument, [Take()]);
        }
        if (Current.Kind == TokenKind.VarKeyword)
        {
            var children = new List<SyntaxElement> { Take() };
            if (Current.Kind == TokenKind.Question && IsAttachedToPrevious())
            {
                children.Add(Take());
            }
            if (Current.Kind is TokenKind.Identifier or TokenKind.Underscore)
            {
                children.Add(Take());
            }
            return Node(SyntaxKind.SwitchPatternTypeArgument, children);
        }
        if (Current.Kind is TokenKind.NumericLiteral or TokenKind.StringLiteral
            or TokenKind.CharacterLiteral or TokenKind.TrueKeyword
            or TokenKind.FalseKeyword or TokenKind.NullKeyword)
        {
            return Node(SyntaxKind.SwitchPatternTypeArgument, [ParsePrimary()]);
        }
        if (Current.Kind == TokenKind.OpenParen)
        {
            var children = new List<SyntaxElement> {
                Take(), ParseExpression(), Expect(TokenKind.CloseParen),
            };
            return Node(SyntaxKind.SwitchPatternTypeArgument, children);
        }
        if (LooksLikeSwitchGenericTypePattern(offset: 0))
        {
            return Node(SyntaxKind.SwitchPatternTypeArgument, [ParseSwitchGenericTypePattern()]);
        }
        return Node(SyntaxKind.SwitchPatternTypeArgument, [ParseType()]);
    }

    private SyntaxNode ParseSwitchReferenceTypePattern()
    {
        var children = new List<SyntaxElement>();
        if (Current.Kind == TokenKind.MutKeyword)
        {
            children.Add(Take());
        }
        if (Current.Kind == TokenKind.WriteKeyword)
        {
            children.Add(Take());
        }
        children.Add(Expect(TokenKind.RefKeyword));
        var missing = _missingTokens;
        children.Add(ExpectAttached(TokenKind.Less));
        children.Add(ParseSwitchPatternTypeArgument());
        children.Add(ExpectTypeCloser(missing));
        children.Add(Current.Kind == TokenKind.Underscore
            ? Take() : Expect(TokenKind.Identifier));
        return Node(SyntaxKind.SwitchReferenceTypePattern, children);
    }

    private SyntaxNode ParseSwitchTransferStatement()
    {
        var children = new List<SyntaxElement> { Take() };
        if (Current.Kind == TokenKind.Underscore)
        {
            children.Add(Take());
        }
        else if (Current.Kind == TokenKind.NumericLiteral)
        {
            children.Add(ParsePrimary());
        }
        else
        {
            children.Add(ParseExpression());
        }
        children.Add(Expect(TokenKind.Semicolon));
        return Node(SyntaxKind.SwitchTransferStatement, children);
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
        var lifetimeEnd = Current.Kind == TokenKind.Equals && Peek(1).Kind == TokenKind.Underscore;

        if (assignment)
        {
            children.Add(TakeSeparated());
            children.Add(lifetimeEnd ? Take() : ParseExpression());
        }

        if (semicolon)
        {
            children.Add(Expect(TokenKind.Semicolon));
        }

        return Node(lifetimeEnd ? SyntaxKind.LifetimeEndStatement
            : compound ? SyntaxKind.CompoundAssignmentStatement
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
        children.Add(ExpectSeparated(TokenKind.Equals));
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

    private bool IsVacantLocalStart()
    {
        if (Current.Kind != TokenKind.Identifier || Current.Text != "init")
        {
            return false;
        }

        var offset = Peek(1).Kind == TokenKind.MutKeyword ? 2 : 1;
        return Peek(offset).Kind == TokenKind.VarKeyword || LooksLikeLocal(offset);
    }

    private SyntaxNode ParseVacantLocal(bool semicolon = true)
    {
        var children = new List<SyntaxElement> { Take() };
        if (Current.Kind == TokenKind.MutKeyword)
        {
            children.Add(Take());
        }
        children.Add(Current.Kind == TokenKind.VarKeyword ? Take() : ParseType());
        children.Add(Expect(TokenKind.Identifier));
        if (semicolon)
        {
            children.Add(Expect(TokenKind.Semicolon));
        }
        return Node(SyntaxKind.VacantLocalDeclaration, children);
    }

    private bool LooksLikeLocal(int initialOffset = 0)
    {
        var offset = IsNativeCallConvTypeAttributeStart(initialOffset)
            ? NativeCallConvTypeAttributeEndOffset(initialOffset)
            : initialOffset;
        if (Peek(offset).Kind == TokenKind.WriteKeyword)
        {
            offset++;
        }
        if (Peek(offset).Kind == TokenKind.GcKeyword)
        {
            offset++;
        }
        if (Peek(offset).Kind is TokenKind.RefKeyword or TokenKind.Identifier or TokenKind.VoidKeyword)
        {
            offset++;
        }
        else
        {
            return false;
        }

        while (Peek(offset).Kind == TokenKind.Dot && Peek(offset + 1).Kind == TokenKind.Identifier)
        {
            offset += 2;
        }

        if (Peek(offset).Kind == TokenKind.Less)
        {
            var inlineArrayType = Peek(offset - 1).Text == "InlineArray";
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
                else if (kind is not (TokenKind.Identifier or TokenKind.VoidKeyword or TokenKind.RefKeyword
                    or TokenKind.GcKeyword or TokenKind.WriteKeyword or TokenKind.Comma or TokenKind.Dot
                    or TokenKind.Question or TokenKind.QuestionQuestion)
                    && !(inlineArrayType && nesting == 1 && kind == TokenKind.NumericLiteral))
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

        return Peek(offset).Kind == TokenKind.Identifier
            && Peek(offset + 1).Kind != TokenKind.OpenParen;
    }

    private SyntaxNode ParseExpression(int parentPrecedence = 0, bool allowGreaterAsTypeTerminator = false)
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
            left = Node(SyntaxKind.ConstExpression,
                [Take(), ParseExpression(13, allowGreaterAsTypeTerminator)]);
        }
        else if (Current.Kind == TokenKind.RefKeyword)
        {
            var children = new List<SyntaxElement> { Take() };
            if (Current.Kind == TokenKind.Identifier && Current.Text == "init")
            {
                children.Add(Take());
            }
            children.Add(ParseExpression(13, allowGreaterAsTypeTerminator));
            left = Node(SyntaxKind.ReferenceExpression, children);
        }
        else if (unaryPrecedence != 0 && unaryPrecedence >= parentPrecedence)
        {
            var op = Current.Kind is TokenKind.MoveKeyword or TokenKind.AwaitKeyword
                ? Take() : TakePrefix();
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
                [op, ParseExpression(unaryPrecedence, allowGreaterAsTypeTerminator)]);
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
                else if (Current.Kind == TokenKind.Identifier && Current.Text == "init")
                {
                    left = Node(SyntaxKind.IsInitializedExpression, [left, keyword, Take()]);
                }
                else if (Current.Kind == TokenKind.VarKeyword)
                {
                    var variable = Take();
                    if (Current.Kind == TokenKind.Question && IsAttachedToPrevious())
                    {
                        left = Node(SyntaxKind.IsVarNullableExpression,
                            [left, keyword, variable, Take(), Expect(TokenKind.Identifier)]);
                    }
                    else
                    {
                        left = Node(SyntaxKind.IsSomeExpression, [left, keyword, variable, Expect(TokenKind.Identifier)]);
                    }
                }
                else if (Current.Kind == TokenKind.Identifier && Current.Text == "some")
                {
                    var some = Expect(TokenKind.Identifier);
                    left = Node(SyntaxKind.IsSomeExpression, [left, keyword, some, Expect(TokenKind.Identifier)]);
                }
                else
                {
                    left = Node(SyntaxKind.IsPatternExpression,
                        [left, keyword, ParseSwitchPattern()]);
                }
                continue;
            }
            var precedence = BinaryPrecedence(Current.Kind);

            if ((allowGreaterAsTypeTerminator && Current.Kind == TokenKind.Greater
                    && !CanStartExpression(Peek(1).Kind))
                || precedence == 0 || precedence < parentPrecedence
                || (precedence == parentPrecedence && Current.Kind != TokenKind.QuestionQuestion))
            {
                break;
            }

            left = Node(SyntaxKind.BinaryExpression,
                [left, TakeSeparated(), ParseExpression(precedence, allowGreaterAsTypeTerminator)]);
        }

        if (parentPrecedence == 0 && Current.Kind == TokenKind.Question)
        {
            var question = TakeSeparated();
            var whenTrue = ParseExpression(allowGreaterAsTypeTerminator: allowGreaterAsTypeTerminator);
            var colon = ExpectSeparated(TokenKind.Colon);
            var whenFalse = ParseExpression(allowGreaterAsTypeTerminator: allowGreaterAsTypeTerminator);
            left = Node(SyntaxKind.ConditionalExpression, [left, question, whenTrue, colon, whenFalse]);
        }

        _depth--;

        return left;
    }

    private SyntaxNode ParsePrimary()
    {
        SyntaxNode expression;

        if (Current.Kind == TokenKind.InterpolatedRawTextStart)
        {
            expression = ParseInterpolatedRawTextExpression();
        }
        else if (Current.Kind == TokenKind.UnsafeKeyword && Peek(1).Kind == TokenKind.OpenParen)
        {
            expression = Node(SyntaxKind.UnsafeExpression,
                [Take(), Expect(TokenKind.OpenParen), ParseExpression(), Expect(TokenKind.CloseParen)]);
        }
        else if (Current.Kind == TokenKind.OpenBracket)
        {
            var children = new List<SyntaxElement> { Take() };
            while (Current.Kind is not (TokenKind.CloseBracket or TokenKind.EndOfFile))
            {
                var start = _position;
                if (Current.Kind == TokenKind.DotDot)
                {
                    children.Add(Node(SyntaxKind.CollectionSpreadElement, [Take(), ParseExpression()]));
                }
                else
                {
                    children.Add(ParseExpression());
                }
                if (Current.Kind != TokenKind.Comma)
                {
                    break;
                }
                children.Add(Take());
                if (_position == start)
                {
                    break;
                }
            }
            children.Add(Expect(TokenKind.CloseBracket));
            expression = Node(SyntaxKind.ArrayInitializerExpression, children);
        }
        else if (Current.Kind == TokenKind.OpenParen)
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
        else if (Current.Kind is TokenKind.NumericLiteral or TokenKind.StringLiteral or TokenKind.CharacterLiteral
            or TokenKind.TrueKeyword
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
                expression = Node(SyntaxKind.MemberAccessExpression, [expression, TakeDot(qualified: false), Expect(TokenKind.Identifier)]);
            }
            else if (Current.Kind == TokenKind.OpenParen)
            {
                expression = Node(SyntaxKind.InvocationExpression, [expression, ParseArguments()]);
            }
            else if (Current.Kind == TokenKind.OpenBracket)
            {
                expression = Node(SyntaxKind.IndexExpression, [expression, ExpectAttached(TokenKind.OpenBracket),
                    ParseIndexArgument(), Expect(TokenKind.CloseBracket)]);
            }
            else if (Current.Kind is TokenKind.PlusPlus or TokenKind.MinusMinus)
            {
                if (!IsAttachedToPrevious())
                {
                    Report("TRM1009", "A symbolic postfix operator must attach to the preceding operand token.", Current.Span);
                }
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

    private SyntaxNode ParseIndexArgument()
    {
        var start = Current.Kind is TokenKind.DotDot or TokenKind.DotDotCaret
            ? Node(SyntaxKind.OmittedIndexBound, [])
            : ParseIndexBound();
        if (Current.Kind is not (TokenKind.DotDot or TokenKind.DotDotCaret))
        {
            return start;
        }

        var connector = Take();
        SyntaxNode end;
        if (Current.Kind == TokenKind.CloseBracket && connector.Token.Kind == TokenKind.DotDot)
        {
            end = Node(SyntaxKind.OmittedIndexBound, []);
        }
        else if (connector.Token.Kind == TokenKind.DotDotCaret)
        {
            end = ParseExpression(13);
            if (connector.Span.End != end.Span.Start)
            {
                Report("TRM1009", "The '..^' operator must attach to its offset operand token.", connector.Span);
            }
        }
        else
        {
            end = ParseIndexBound();
        }

        return Node(SyntaxKind.SliceRangeExpression, [start, connector, end]);
    }

    private SyntaxNode ParseIndexBound()
    {
        if (Current.Kind != TokenKind.Caret)
        {
            return ParseExpression();
        }

        var caret = TakePrefix();
        return Node(SyntaxKind.EndRelativeIndexBound, [caret, ParseExpression(13)]);
    }

    private SyntaxNode ParseInterpolatedRawTextExpression()
    {
        var children = new List<SyntaxElement> { Take() };
        while (Current.Kind is not (TokenKind.InterpolatedRawTextEnd or TokenKind.EndOfFile))
        {
            if (Current.Kind == TokenKind.InterpolatedRawTextSegment)
            {
                children.Add(Node(SyntaxKind.InterpolatedRawTextSegment, [Take()]));
            }
            else if (Current.Kind == TokenKind.InterpolationHoleStart)
            {
                children.Add(ParseInterpolationHole());
            }
            else
            {
                Report("TRM1001", "Expected a raw interpolated-text segment or hole.", Current.Span);
                children.Add(Take());
            }
        }
        children.Add(Expect(TokenKind.InterpolatedRawTextEnd));
        return Node(SyntaxKind.InterpolatedRawTextExpression, children);
    }

    private SyntaxNode ParseInterpolationHole()
    {
        var children = new List<SyntaxElement> { Take() };
        if (Current.Kind is TokenKind.Comma or TokenKind.Colon or TokenKind.InterpolationHoleEnd)
        {
            Report("TRM1001", "An interpolation hole requires an expression.", Current.Span);
            children.Add(Node(SyntaxKind.MissingExpression, [Missing(TokenKind.Identifier)]));
        }
        else
        {
            children.Add(ParseExpression());
        }

        if (Current.Kind == TokenKind.Comma)
        {
            children.Add(Take());
            if (Current.Kind is TokenKind.Colon or TokenKind.InterpolationHoleEnd)
            {
                Report("TRM1001", "An interpolation alignment requires an expression.", Current.Span);
                children.Add(Node(SyntaxKind.MissingExpression, [Missing(TokenKind.Identifier)]));
            }
            else
            {
                children.Add(ParseExpression());
            }
        }

        if (Current.Kind == TokenKind.Colon)
        {
            var colon = Take();
            if (Current.Kind is TokenKind.Identifier or TokenKind.StringLiteral)
            {
                children.Add(Node(SyntaxKind.InterpolationFormatSpecifier, [colon, Take()]));
            }
            else if (Current.Kind == TokenKind.OpenParen)
            {
                children.Add(Node(SyntaxKind.InterpolationFormatSpecifier, [colon, ParsePrimary()]));
            }
            else
            {
                Report("TRM1001", "An interpolation format requires an identifier, string, or parenthesized expression.",
                    Current.Span);
                children.Add(Node(SyntaxKind.InterpolationFormatSpecifier,
                    [colon, Missing(TokenKind.Identifier)]));
            }
        }

        children.Add(Expect(TokenKind.InterpolationHoleEnd));
        return Node(SyntaxKind.InterpolationHole, children);
    }

    private SyntaxNode ParseArguments()
    {
        var children = new List<SyntaxElement> { ExpectAttached(TokenKind.OpenParen) };

        while (Current.Kind is not (TokenKind.CloseParen or TokenKind.Semicolon or TokenKind.CloseBrace or TokenKind.EndOfFile))
        {
            var start = _position;
            var argument = new List<SyntaxElement>();

            if (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Colon)
            {
                argument.Add(Take());
                argument.Add(TakeNameColon());
            }

            var isOut = Current.Kind == TokenKind.OutKeyword
                || (Current.Kind == TokenKind.Identifier && Current.Text == "init"
                    && Peek(1).Kind is TokenKind.VarKeyword or TokenKind.Identifier or TokenKind.Star);
            if (Current.Kind == TokenKind.Ampersand
                && Peek(1).Kind == TokenKind.Identifier && Peek(1).Text == "init")
            {
                argument.Add(Node(SyntaxKind.NativeOutputAddressExpression,
                    [Take(), Take(), ParseExpression()]));
            }
            else if (Current.Kind == TokenKind.RefKeyword)
            {
                argument.Add(ParseExpression());
            }
            else
            {
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
            && Peek(1).Kind is TokenKind.VarKeyword or TokenKind.Identifier or TokenKind.Star)
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

    private bool IsAttachedToPrevious() =>
        _position > 0 && _tokens[_position - 1].Span.End == Current.Span.Start;

    private SyntaxTokenElement TakeSeparated()
    {
        if (IsAttachedToPrevious() || Current.Span.End == Peek(1).Span.Start)
        {
            Report("TRM1008", "The operator must be separated from both neighboring tokens by trivia.", Current.Span);
        }

        return Take();
    }

    private SyntaxTokenElement ExpectSeparated(TokenKind kind) =>
        Current.Kind == kind ? TakeSeparated() : Expect(kind);

    private SyntaxTokenElement TakeNameColon()
    {
        if (!IsAttachedToPrevious())
        {
            Report("TRM1010", "A name-prefix colon must attach to the preceding name.", Current.Span);
        }

        return Take();
    }

    private SyntaxTokenElement TakePrefix()
    {
        if (Current.Span.End != Peek(1).Span.Start)
        {
            Report("TRM1009", "A symbolic prefix operator must attach to the following operand token.", Current.Span);
        }

        return Take();
    }

    private SyntaxTokenElement TakeDot(bool qualified)
    {
        var leftAttached = IsAttachedToPrevious();
        var rightAttached = Current.Span.End == Peek(1).Span.Start;
        if (qualified ? !leftAttached || !rightAttached : !leftAttached && !rightAttached)
        {
            Report("TRM1007", qualified
                ? "A qualified-name dot must attach on both sides."
                : "An expression member-access dot must attach on at least one side.", Current.Span);
        }

        return Take();
    }

    private SyntaxTokenElement ExpectAttached(TokenKind kind)
    {
        if (Current.Kind == kind && _position > 0 && !IsAttachedToPrevious())
        {
            Report("TRM1006", "The opening delimiter must attach to the preceding name or receiver.", Current.Span);
        }

        return Expect(kind);
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

    private SyntaxTokenElement ExpectTypeCloser(int missingBefore)
    {
        var closer = Expect(TokenKind.Greater);
        if (!closer.Token.IsMissing && missingBefore == _missingTokens)
        {
            _ = _typePunctuation.Add(closer.Span);
        }
        return closer;
    }

    private void ValidateDeferredPunctuation()
    {
        for (var index = 0; index < _tokens.Length - 1; index++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!IsTypePunctuation(_tokens[index]))
            {
                continue;
            }
            var first = index;
            var start = _tokens[index].Span.Start;
            var end = _tokens[index].Span.End;
            while (index + 1 < _tokens.Length - 1 && _tokens[index + 1].Span.Start == end
                && IsTypePunctuation(_tokens[index + 1]))
            {
                end = _tokens[++index].Span.End;
            }
            if (PunctuationRun.Classify(_source.Text.AsSpan(start, end - start), out _) != PunctuationRunKind.TypeBoundary)
            {
                continue;
            }
            var consumed = true;
            for (var item = first; item <= index && consumed; item++)
            {
                var token = _tokens[item];
                if (_typePunctuation.Contains(token.Span))
                {
                    continue;
                }
                // The CST consumes a compound closer one actual '>' at a time.
                for (var offset = 0; offset < token.Text.Length; offset++)
                {
                    if (token.Text[offset] != '>'
                        || !_typePunctuation.Contains(new TextSpan(token.Span.Start + offset, 1)))
                    {
                        consumed = false;
                        break;
                    }
                }
            }
            if (!consumed)
            {
                Report(PunctuationRun.InvalidCode, PunctuationRun.InvalidMessage, new TextSpan(start, end - start));
            }
        }

        static bool IsTypePunctuation(SyntaxToken token) =>
            token.Kind is TokenKind.Greater or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned
                or TokenKind.Question or TokenKind.QuestionQuestion
            && token.Text.AsSpan().IndexOfAnyExcept('>', '?') < 0;
    }

    private SyntaxTokenElement Missing(TokenKind kind)
    {
        _missingTokens++;
        return new SyntaxTokenElement(SyntaxToken.Missing(kind, Current.Span.Start));
    }

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
        => kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword or TokenKind.PrivateKeyword
            or TokenKind.StaticKeyword or TokenKind.ExternKeyword or TokenKind.MutKeyword or TokenKind.UniqueKeyword
            or TokenKind.UnsafeKeyword;

    private static bool IsTypeStart(TokenKind kind) => kind is TokenKind.Identifier or TokenKind.VoidKeyword
        or TokenKind.WriteKeyword or TokenKind.GcKeyword or TokenKind.RefKeyword;

    private static int UnaryPrecedence(TokenKind kind) =>
        kind is TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Bang
            or TokenKind.Tilde or TokenKind.Ampersand or TokenKind.MoveKeyword
            or TokenKind.PlusPlus or TokenKind.MinusMinus or TokenKind.AwaitKeyword ? 13 : 0;

    private static bool CanStartExpression(TokenKind kind) =>
        UnaryPrecedence(kind) != 0
        || kind is TokenKind.Identifier or TokenKind.RefKeyword or TokenKind.OpenBracket
            or TokenKind.OpenParen or TokenKind.NewKeyword or TokenKind.UnsafeKeyword
            or TokenKind.NumericLiteral or TokenKind.StringLiteral or TokenKind.CharacterLiteral
            or TokenKind.TrueKeyword or TokenKind.FalseKeyword or TokenKind.NullKeyword
            or TokenKind.InterpolatedRawTextStart;

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
