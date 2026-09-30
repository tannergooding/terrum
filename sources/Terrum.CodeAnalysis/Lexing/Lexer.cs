// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

/// <summary>Tokenizes source snapshots without discarding trivia or malformed UTF-16.</summary>
public sealed class Lexer
{
    private static readonly string[] s_numericTags =
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

    /// <summary>Reads the complete token stream, including EOF, and reports lexical errors.</summary>
    /// <param name="source">The immutable source snapshot to tokenize.</param>
    /// <param name="cancellationToken">A token that cancels lexing by throwing, not by returning a partial result.</param>
    /// <returns>Tokens and diagnostics in UTF-16 source coordinates.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static LexResult Lex(SourceText source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        Lexer lexer = new(source, cancellationToken);
        var tokens = ImmutableArray.CreateBuilder<SyntaxToken>();

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trivia = lexer.ReadTrivia();
            var start = lexer._position;
            var kind = lexer.ReadToken();
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
    private static bool IsHex(char value) => IsDigit(value) || value is (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
    private static bool IsBinary(char value) => value is '0' or '1';
    private static bool IsRadixDigit(char value, int radix) => radix switch {
        16 => IsHex(value),
        2 => IsBinary(value),
        _ => IsDigit(value),
    };
    private static bool IsNewline(char value) => value is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';
    private static bool IsSpace(char value) =>
        value is '\t' or '\v' or '\f' or ' ' or '\u00A0' or '\u1680'
            or (>= '\u2000' and <= '\u200A') or '\u202F' or '\u205F' or '\u3000';

    private ImmutableArray<SyntaxTrivia> ReadTrivia()
    {
        var trivia = ImmutableArray.CreateBuilder<SyntaxTrivia>();

        while (!AtEnd)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var start = _position;
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
        if (TryScalar(_position, out _, out var width))
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

        var start = _position;
        if (TryScalar(_position, out var scalar, out var width) && (scalar == '_' || UnicodeXid.IsStart(scalar)))
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
            case '(':
                return TokenKind.OpenParen;
            case ')':
                return TokenKind.CloseParen;
            case '{':
                return TokenKind.OpenBrace;
            case '}':
                return TokenKind.CloseBrace;
            case '[':
                return TokenKind.OpenBracket;
            case ']':
                return TokenKind.CloseBracket;
            case ';':
                return TokenKind.Semicolon;
            case ',':
                return TokenKind.Comma;
            case '.':
                return TokenKind.Dot;
            case ':':
                return TokenKind.Colon;
            case '?':
                if (!AtEnd && Current == '?')
                {
                    _position++;
                    return Compound('=', TokenKind.QuestionQuestionEquals, TokenKind.QuestionQuestion);
                }
                return TokenKind.Question;
            case '+':
                if (!AtEnd && Current == '+')
                {
                    _position++;
                    return TokenKind.PlusPlus;
                }
                return Compound('=', TokenKind.PlusEquals, TokenKind.Plus);
            case '-':
                if (!AtEnd && Current == '-')
                {
                    _position++;
                    return TokenKind.MinusMinus;
                }
                return Compound('=', TokenKind.MinusEquals, TokenKind.Minus);
            case '*':
                return Compound('=', TokenKind.StarEquals, TokenKind.Star);
            case '/':
                return Compound('=', TokenKind.SlashEquals, TokenKind.Slash);
            case '%':
                return Compound('=', TokenKind.PercentEquals, TokenKind.Percent);
            case '~':
                return TokenKind.Tilde;
            case '^':
                return Compound('=', TokenKind.CaretEquals, TokenKind.Caret);
            case '!':
                return Compound('=', TokenKind.BangEquals, TokenKind.Bang);
            case '=':
                if (!AtEnd && Current == '>')
                {
                    _position++;
                    return TokenKind.Arrow;
                }
                return Compound('=', TokenKind.EqualsEquals, TokenKind.Equals);
            case '<':
                if (!AtEnd && Current == '<')
                {
                    _position++;
                    return Compound('=', TokenKind.ShiftLeftEquals, TokenKind.ShiftLeft);
                }
                return Compound('=', TokenKind.LessEquals, TokenKind.Less);
            case '>':
                if (!AtEnd && Current == '>')
                {
                    _position++;
                    if (!AtEnd && Current == '>')
                    {
                        _position++;
                        return Compound('=', TokenKind.ShiftRightUnsignedEquals, TokenKind.ShiftRightUnsigned);
                    }
                    return Compound('=', TokenKind.ShiftRightEquals, TokenKind.ShiftRight);
                }
                return Compound('=', TokenKind.GreaterEquals, TokenKind.Greater);
            case '&':
                if (!AtEnd && Current == '&')
                {
                    _position++;
                    return TokenKind.AmpersandAmpersand;
                }
                return Compound('=', TokenKind.AmpersandEquals, TokenKind.Ampersand);
            case '|':
                if (!AtEnd && Current == '|')
                {
                    _position++;
                    return TokenKind.PipePipe;
                }
                return Compound('=', TokenKind.PipeEquals, TokenKind.Pipe);
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
        var start = _position;
        if (Current == '0' && Peek(1) is 'x' or 'X' or 'b' or 'B')
        {
            var hex = Peek(1) is 'x' or 'X';
            _position += 2;
            if (!ReadDigitRun(hex ? 16 : 2, allowInitialSeparators: true))
            {
                Report(hex ? "TRM0007" : "TRM0008",
                    hex ? "Hexadecimal prefix requires an ASCII hex digit."
                        : "Binary prefix requires an ASCII binary digit.", start, 2);
                return TokenKind.NumericLiteral;
            }
        }
        else
        {
            _ = ReadDigitRun(10);

            if (Current == '.' && IsDigit(Peek(1)))
            {
                _position++;
                _ = ReadDigitRun(10);
            }

            if (Current is 'e' or 'E')
            {
                var afterExponent = _position + 1;
                var sign = Peek(1) is '+' or '-';
                var nextStartsIdentifier = afterExponent < _source.Length &&
                    TryScalar(afterExponent, out var scalar, out _) &&
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
                        Report("TRM0004", "Exponent requires an ASCII digit.", start, _position - start);
                        return TokenKind.NumericLiteral;
                    }
                    else
                    {
                        _ = ReadDigitRun(10);
                    }
                }
            }
        }

        var tagLength = ReadNumericTag();
        if (tagLength != 0)
        {
            Report("TRM0006", "Numeric width suffixes are not supported.", _position, tagLength);
            _position += tagLength;
        }

        return TokenKind.NumericLiteral;
    }

    private bool ReadDigitRun(int radix, bool allowInitialSeparators = false)
    {
        var hasDigit = false;
        while (!AtEnd)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (IsRadixDigit(Current, radix))
            {
                hasDigit = true;
                _position++;
                continue;
            }
            if (Current != '_')
            {
                break;
            }

            var length = 1;
            while (Peek(length) == '_')
            {
                length++;
            }
            if ((hasDigit || allowInitialSeparators) && IsRadixDigit(Peek(length), radix))
            {
                _position += length;
                continue;
            }
            if (hasDigit)
            {
                Report("TRM0009", "Digit separators must appear between digits.", _position, length);
            }
            break;
        }
        return hasDigit;
    }

    private int ReadNumericTag()
    {
        var remaining = _source.Text.AsSpan(_position);
        foreach (var tag in s_numericTags)
        {
            if (!remaining.StartsWith(tag, StringComparison.Ordinal))
            {
                continue;
            }

            var end = _position + tag.Length;
            if (end == _source.Length ||
                !(TryScalar(end, out var scalar, out _) && (scalar == '_' || UnicodeXid.IsContinue(scalar))))
            {
                return tag.Length;
            }
        }

        return 0;
    }

    private TokenKind ReadString()
    {
        var start = _position++;
        var terminated = false;
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

            var escapeStart = _position++;
            if (AtEnd || IsNewline(Current))
            {
                Report("TRM0005", "Incomplete string escape.", escapeStart, 1);
                break;
            }

            var escape = _source.Text[_position];
            if (TryScalar(_position, out _, out var escapeWidth))
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
                var digits = escape == 'u' ? 4 : 8;
                var value = 0;
                var count = 0;
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
        var status = Rune.DecodeFromUtf16(_source.Text.AsSpan(position), out var rune, out width);
        scalar = rune.Value;

        return status == OperationStatus.Done;
    }

    private void Report(string code, string message, int start, int length)
        => _diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, message, new TextSpan(start, length)));

    private static TokenKind Keyword(ReadOnlySpan<char> text) => text switch {
        "_" => TokenKind.Underscore,
        "using" => TokenKind.UsingKeyword,
        "namespace" => TokenKind.NamespaceKeyword,
        "public" => TokenKind.PublicKeyword,
        "internal" => TokenKind.InternalKeyword,
        "private" => TokenKind.PrivateKeyword,
        "static" => TokenKind.StaticKeyword,
        "operator" => TokenKind.OperatorKeyword,
        "where" => TokenKind.WhereKeyword,
        "void" => TokenKind.VoidKeyword,
        "var" => TokenKind.VarKeyword,
        "mut" => TokenKind.MutKeyword,
        "write" => TokenKind.WriteKeyword,
        "ref" => TokenKind.RefKeyword,
        "out" => TokenKind.OutKeyword,
        "if" => TokenKind.IfKeyword,
        "else" => TokenKind.ElseKeyword,
        "while" => TokenKind.WhileKeyword,
        "do" => TokenKind.DoKeyword,
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
        "unsafe" => TokenKind.UnsafeKeyword,
        _ => TokenKind.Identifier,
    };
}
