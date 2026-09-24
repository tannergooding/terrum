// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

public sealed class Lexer
{
    private static readonly string[] NumericTags =
    [
        "i8", "i16", "i32", "i64", "i128",
        "u8", "u16", "u32", "u64", "u128",
        "f16", "f32", "f64", "f128",
    ];

    private readonly SourceText _source;
    private readonly CancellationToken _cancellationToken;
    private readonly ImmutableArray<Diagnostic>.Builder _diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
    private int _position;

    private Lexer(SourceText source, CancellationToken cancellationToken)
    {
        _source = source;
        _cancellationToken = cancellationToken;
    }

    public static LexResult Lex(SourceText source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        Lexer lexer = new(source, cancellationToken);
        ImmutableArray<SyntaxToken>.Builder tokens = ImmutableArray.CreateBuilder<SyntaxToken>();

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImmutableArray<SyntaxTrivia> trivia = lexer.ReadTrivia();
            int start = lexer._position;
            TokenKind kind = lexer.ReadToken();
            TextSpan span = new(start, lexer._position - start);
            tokens.Add(new SyntaxToken(kind, source.GetSubText(span), span, trivia));
        }
        while (tokens[^1].Kind != TokenKind.EndOfFile);

        return new LexResult(source, tokens.ToImmutable(), lexer._diagnostics.ToImmutable());
    }

    private bool AtEnd => _position == _source.Length;
    private char Current => AtEnd ? '\0' : _source[_position];
    private char Peek(int offset) => offset < _source.Length - _position ? _source[_position + offset] : '\0';
    private static bool IsDigit(char value) => value is >= '0' and <= '9';
    private static bool IsHex(char value) => IsDigit(value) || value is >= 'a' and <= 'f' or >= 'A' and <= 'F';
    private static bool IsNewline(char value) => value is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';
    private static bool IsSpace(char value) =>
        value is '\t' or '\v' or '\f' or ' ' or '\u00A0' or '\u1680'
            or >= '\u2000' and <= '\u200A' or '\u202F' or '\u205F' or '\u3000';

    private ImmutableArray<SyntaxTrivia> ReadTrivia()
    {
        ImmutableArray<SyntaxTrivia>.Builder trivia = ImmutableArray.CreateBuilder<SyntaxTrivia>();

        while (!AtEnd)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            int start = _position;
            TriviaKind kind;
            if (IsSpace(Current))
            {
                kind = TriviaKind.Whitespace;
                do
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    _position++;
                }
                while (!AtEnd && IsSpace(Current));
            }
            else if (IsNewline(Current))
            {
                kind = TriviaKind.NewLine;
                _position += Current == '\r' && Peek(1) == '\n' ? 2 : 1;
            }
            else if (Current == '/' && Peek(1) == '/')
            {
                kind = TriviaKind.SingleLineComment;
                _position += 2;
                while (!AtEnd && !IsNewline(Current))
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    AdvanceCommentScalar();
                }
            }
            else if (Current == '/' && Peek(1) == '*')
            {
                kind = TriviaKind.MultiLineComment;
                _position += 2;
                while (!AtEnd && !(Current == '*' && Peek(1) == '/'))
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    AdvanceCommentScalar();
                }
                if (AtEnd)
                {
                    Report("TRM0003", "Unterminated block comment.", start, _position - start);
                }
                else
                {
                    _position += 2;
                }
            }
            else
            {
                break;
            }

            TextSpan span = new(start, _position - start);
            trivia.Add(new SyntaxTrivia(kind, span, _source.GetSubText(span)));
        }

        return trivia.ToImmutable();
    }

    private void AdvanceCommentScalar()
    {
        if (TryScalar(_position, out _, out int width))
        {
            _position += width;
        }
        else
        {
            Report("TRM0001", "Unpaired UTF-16 surrogate.", _position, 1);
            _position++;
        }
    }

    private TokenKind ReadToken()
    {
        if (AtEnd)
        {
            return TokenKind.EndOfFile;
        }

        int start = _position;
        if (TryScalar(_position, out int scalar, out int width) && (scalar == '_' || UnicodeXid.IsStart(scalar)))
        {
            _position += width;
            while (!AtEnd && TryScalar(_position, out scalar, out width) &&
                   (scalar == '_' || UnicodeXid.IsContinue(scalar)))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _position += width;
            }

            return Keyword(_source.Text.AsSpan(start, _position - start));
        }

        if (IsDigit(Current))
        {
            return ReadNumber();
        }

        if (Current == '"')
        {
            return ReadString();
        }

        _position++;
        switch (_source[start])
        {
            case '(': return TokenKind.OpenParen;
            case ')': return TokenKind.CloseParen;
            case '{': return TokenKind.OpenBrace;
            case '}': return TokenKind.CloseBrace;
            case '[': return TokenKind.OpenBracket;
            case ']': return TokenKind.CloseBracket;
            case ';': return TokenKind.Semicolon;
            case ',': return TokenKind.Comma;
            case '.': return TokenKind.Dot;
            case ':': return TokenKind.Colon;
            case '?': return TokenKind.Question;
            case '+': return TokenKind.Plus;
            case '-': return TokenKind.Minus;
            case '*': return TokenKind.Star;
            case '/': return TokenKind.Slash;
            case '%': return TokenKind.Percent;
            case '~': return TokenKind.Tilde;
            case '^': return TokenKind.Caret;
            case '!': return Compound('=', TokenKind.BangEquals, TokenKind.Bang);
            case '=': return Compound('=', TokenKind.EqualsEquals, TokenKind.Equals);
            case '<': return Compound('=', TokenKind.LessEquals, TokenKind.Less);
            case '>': return Compound('=', TokenKind.GreaterEquals, TokenKind.Greater);
            case '&': return Compound('&', TokenKind.AmpersandAmpersand, TokenKind.Ampersand);
            case '|': return Compound('|', TokenKind.PipePipe, TokenKind.Pipe);
            default:
                if (char.IsHighSurrogate(_source[start]) && !AtEnd && char.IsLowSurrogate(Current))
                {
                    _position++;
                }
                Report("TRM0001", char.IsSurrogate(_source[start]) && _position - start == 1
                    ? "Unpaired UTF-16 surrogate."
                    : "Unrecognized character.", start, _position - start);
                return TokenKind.Bad;
        }
    }

    private TokenKind Compound(char second, TokenKind compound, TokenKind single)
    {
        if (!AtEnd && Current == second)
        {
            _position++;
            return compound;
        }

        return single;
    }

    private TokenKind ReadNumber()
    {
        int start = _position;
        while (!AtEnd && IsDigit(Current))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _position++;
        }

        if (Current == '.' && IsDigit(Peek(1)))
        {
            _position++;
            while (!AtEnd && IsDigit(Current))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _position++;
            }
        }

        bool malformedExponent = false;
        if (Current is 'e' or 'E')
        {
            int afterExponent = _position + 1;
            bool sign = Peek(1) is '+' or '-';
            bool nextStartsIdentifier = afterExponent < _source.Length &&
                TryScalar(afterExponent, out int scalar, out _) &&
                (scalar == '_' || UnicodeXid.IsContinue(scalar));
            if (IsDigit(Peek(sign ? 2 : 1)) || sign || !nextStartsIdentifier)
            {
                _position++;
                if (sign)
                {
                    _position++;
                }
                if (!IsDigit(Current) || AtEnd)
                {
                    malformedExponent = true;
                    Report("TRM0004", "Exponent requires an ASCII digit.", start, _position - start);
                }
                else
                {
                    while (!AtEnd && IsDigit(Current))
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        _position++;
                    }
                }
            }
        }

        if (!malformedExponent)
        {
            _position += ReadNumericTag();
        }

        return TokenKind.NumericLiteral;
    }

    private int ReadNumericTag()
    {
        ReadOnlySpan<char> remaining = _source.Text.AsSpan(_position);
        foreach (string tag in NumericTags)
        {
            if (!remaining.StartsWith(tag, StringComparison.Ordinal))
            {
                continue;
            }

            int end = _position + tag.Length;
            if (end == _source.Length ||
                !(TryScalar(end, out int scalar, out _) && (scalar == '_' || UnicodeXid.IsContinue(scalar))))
            {
                return tag.Length;
            }
        }

        return 0;
    }

    private TokenKind ReadString()
    {
        int start = _position++;
        bool terminated = false;
        while (!AtEnd && !IsNewline(Current))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (Current == '"')
            {
                _position++;
                terminated = true;
                break;
            }

            if (Current != '\\')
            {
                if (char.IsSurrogate(Current) &&
                    !(char.IsHighSurrogate(Current) && char.IsLowSurrogate(Peek(1))))
                {
                    Report("TRM0001", "Unpaired UTF-16 surrogate.", _position, 1);
                }
                _position += char.IsHighSurrogate(Current) && char.IsLowSurrogate(Peek(1)) ? 2 : 1;
                continue;
            }

            int escapeStart = _position++;
            if (AtEnd || IsNewline(Current))
            {
                Report("TRM0005", "Incomplete string escape.", escapeStart, 1);
                break;
            }

            char escape = Current;
            if (TryScalar(_position, out _, out int escapeWidth))
            {
                _position += escapeWidth;
            }
            else
            {
                Report("TRM0001", "Unpaired UTF-16 surrogate.", _position, 1);
                _position++;
            }
            if (escape is 'n' or 'r' or 't' or '\\' or '"' or '0')
            {
                continue;
            }

            if (escape is 'u' or 'U')
            {
                int digits = escape == 'u' ? 4 : 8;
                int value = 0;
                int count = 0;
                while (count < digits && !AtEnd && IsHex(Current))
                {
                    value = (value << 4) | (IsDigit(Current) ? Current - '0' : (Current | 0x20) - 'a' + 10);
                    _position++;
                    count++;
                }
                if (count == digits && Rune.IsValid(value))
                {
                    continue;
                }
            }
            Report("TRM0005", "Invalid string escape.", escapeStart, _position - escapeStart);
        }

        if (!terminated)
        {
            Report("TRM0002", "Unterminated string literal.", start, _position - start);
        }

        return TokenKind.StringLiteral;
    }

    private bool TryScalar(int position, out int scalar, out int width)
    {
        OperationStatus status = Rune.DecodeFromUtf16(_source.Text.AsSpan(position), out Rune rune, out width);
        scalar = rune.Value;

        return status == OperationStatus.Done;
    }

    private void Report(string code, string message, int start, int length)
        => _diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, message, new TextSpan(start, length)));

    private static TokenKind Keyword(ReadOnlySpan<char> text) => text switch
    {
        "using" => TokenKind.UsingKeyword,
        "namespace" => TokenKind.NamespaceKeyword,
        "public" => TokenKind.PublicKeyword,
        "internal" => TokenKind.InternalKeyword,
        "private" => TokenKind.PrivateKeyword,
        "static" => TokenKind.StaticKeyword,
        "void" => TokenKind.VoidKeyword,
        "var" => TokenKind.VarKeyword,
        "mut" => TokenKind.MutKeyword,
        "ref" => TokenKind.RefKeyword,
        "out" => TokenKind.OutKeyword,
        "if" => TokenKind.IfKeyword,
        "else" => TokenKind.ElseKeyword,
        "while" => TokenKind.WhileKeyword,
        "return" => TokenKind.ReturnKeyword,
        "true" => TokenKind.TrueKeyword,
        "false" => TokenKind.FalseKeyword,
        "struct" => TokenKind.StructKeyword,
        "class" => TokenKind.ClassKeyword,
        "interface" => TokenKind.InterfaceKeyword,
        "enum" => TokenKind.EnumKeyword,
        "union" => TokenKind.UnionKeyword,
        "unique" => TokenKind.UniqueKeyword,
        "own" => TokenKind.OwnKeyword,
        "move" => TokenKind.MoveKeyword,
        "new" => TokenKind.NewKeyword,
        "for" => TokenKind.ForKeyword,
        "foreach" => TokenKind.ForeachKeyword,
        "break" => TokenKind.BreakKeyword,
        "continue" => TokenKind.ContinueKeyword,
        "switch" => TokenKind.SwitchKeyword,
        "case" => TokenKind.CaseKeyword,
        "default" => TokenKind.DefaultKeyword,
        "null" => TokenKind.NullKeyword,
        "async" => TokenKind.AsyncKeyword,
        "await" => TokenKind.AwaitKeyword,
        "require" => TokenKind.RequireKeyword,
        "try" => TokenKind.TryKeyword,
        "catch" => TokenKind.CatchKeyword,
        "finally" => TokenKind.FinallyKeyword,
        "throw" => TokenKind.ThrowKeyword,
        _ => TokenKind.Identifier,
    };
}
