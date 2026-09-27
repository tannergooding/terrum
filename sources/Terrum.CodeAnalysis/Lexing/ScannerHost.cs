// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

/// <summary>Supplies owned input and output storage to the compiled Terrum scanner component.</summary>
public static class ScannerHost
{
    /// <summary>The maximum source length accepted by this bounded scanner adapter.</summary>
    public const int MaximumSourceLength = 16_384;

    /// <summary>The version of the lossless scanner and integer-decoder interchange.</summary>
    public const int SchemaVersion = 4;

    /// <summary>The component identity for the lossless scanner and integer-decoder protocol.</summary>
    public const string ComponentName = "scanner-4";

    // Lexemes are packed as (kind, UTF-16 length, code units...) and terminated by kind -1.
    private static readonly (TokenKind Kind, string Text)[] s_operatorSpellings =
    [
        (TokenKind.OpenParen, "("), (TokenKind.CloseParen, ")"),
        (TokenKind.OpenBrace, "{"), (TokenKind.CloseBrace, "}"),
        (TokenKind.OpenBracket, "["), (TokenKind.CloseBracket, "]"),
        (TokenKind.Semicolon, ";"), (TokenKind.Comma, ","), (TokenKind.Dot, "."),
        (TokenKind.Colon, ":"), (TokenKind.Question, "?"), (TokenKind.Plus, "+"),
        (TokenKind.Minus, "-"), (TokenKind.Star, "*"), (TokenKind.Slash, "/"),
        (TokenKind.Percent, "%"), (TokenKind.Bang, "!"), (TokenKind.Tilde, "~"),
        (TokenKind.Equals, "="), (TokenKind.EqualsEquals, "=="),
        (TokenKind.BangEquals, "!="), (TokenKind.Less, "<"),
        (TokenKind.LessEquals, "<="), (TokenKind.Greater, ">"),
        (TokenKind.GreaterEquals, ">="), (TokenKind.Ampersand, "&"),
        (TokenKind.AmpersandAmpersand, "&&"), (TokenKind.Pipe, "|"),
        (TokenKind.PipePipe, "||"), (TokenKind.Caret, "^"),
    ];

    private static readonly ImmutableArray<int> s_keywords = CreateKeywords();
    private static readonly ImmutableArray<int> s_operators = CreateOperators();

    /// <summary>Creates fixed buffers containing source code units, shared pinned tables and vacant token records.</summary>
    /// <param name="source">The immutable source snapshot, including any malformed UTF-16 units.</param>
    /// <returns>Initialized buffers exposed as Host.Source, Host.XidStart, Host.XidContinue, Host.Tokens, Host.Diagnostics, Host.Trivia, Host.Keywords, Host.Operators and Host.IntegerValues.</returns>
    /// <exception cref="ArgumentNullException">The source is null.</exception>
    /// <exception cref="ArgumentException">The source exceeds this component's resource limit.</exception>
    public static ImmutableArray<HostBuffer> CreateBuffers(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > MaximumSourceLength)
        {
            throw new ArgumentException("The scanner input exceeds its bounded source limit.", nameof(source));
        }

        return [
            new HostBuffer("Source", PrimitiveType.Char16, [.. source.Text], false),
            new HostBuffer("XidStart", PrimitiveType.Int32, [.. UnicodeIdentifierProfile.StartRanges], false),
            new HostBuffer("XidContinue", PrimitiveType.Int32, [.. UnicodeIdentifierProfile.ContinueRanges], false),
            new HostBuffer("Tokens", PrimitiveType.Int32, [.. new int[(source.Length + 1) * 3]], true),
            new HostBuffer("Diagnostics", PrimitiveType.Int32, [.. new int[1 + (source.Length * 3)]], true),
            new HostBuffer("Trivia", PrimitiveType.Int32, [.. new int[1 + (source.Length * 4)]], true),
            new HostBuffer("Keywords", PrimitiveType.Int32, s_keywords, false),
            new HostBuffer("Operators", PrimitiveType.Int32, s_operators, false),
            new HostBuffer("IntegerValues", PrimitiveType.Int32, [.. new int[1 + (source.Length * 5)]], true),
        ];
    }

    private static ImmutableArray<int> CreateKeywords()
    {
        var words = ImmutableArray.CreateBuilder<int>();
        foreach (var kind in Enum.GetValues<TokenKind>())
        {
            var name = kind.ToString();
            if (name.EndsWith("Keyword", StringComparison.Ordinal))
            {
                AddLexeme(words, kind, char.ToLowerInvariant(name[0]) + name[1..^"Keyword".Length]);
            }
        }

        words.Add(-1);
        return words.ToImmutable();
    }

    private static ImmutableArray<int> CreateOperators()
    {
        var words = ImmutableArray.CreateBuilder<int>();
        foreach (var (kind, text) in s_operatorSpellings)
        {
            AddLexeme(words, kind, text);
        }

        words.Add(-1);
        return words.ToImmutable();
    }

    private static void AddLexeme(ImmutableArray<int>.Builder words, TokenKind kind, string text)
    {
        words.Add((int)kind);
        words.Add(text.Length);
        foreach (var unit in text)
        {
            words.Add(unit);
        }
    }

    /// <summary>Validates and reads the published prefix of the scanner's token buffer.</summary>
    /// <param name="source">The original source snapshot.</param>
    /// <param name="count">The record count returned by compiled Scan, including EOF.</param>
    /// <param name="bytes">Little-endian token-buffer words copied after native execution completes.</param>
    /// <returns>Token records in source order.</returns>
    /// <exception cref="ArgumentNullException">The source is null.</exception>
    /// <exception cref="ArgumentException">The result violates the scanner interchange contract.</exception>
    public static ImmutableArray<ScannerToken> ReadTokens(SourceText source, int count, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (count <= 0 || count > (long)source.Length + 1 || count > bytes.Length / 12)
        {
            throw new ArgumentException("Invalid scanner result count.", nameof(count));
        }

        var tokens = ImmutableArray.CreateBuilder<ScannerToken>(count);
        var previousEnd = 0;
        for (var index = 0; index < count; index++)
        {
            var record = bytes.Slice(index * 12, 12);
            var kind = BinaryPrimitives.ReadInt32LittleEndian(record);
            var start = BinaryPrimitives.ReadInt32LittleEndian(record[4..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(record[8..]);
            if (!Enum.IsDefined((TokenKind)kind) || start < previousEnd || start > source.Length
                || length < 0 || length > source.Length - start
                || (index == count - 1
                    ? kind != (int)TokenKind.EndOfFile || start != source.Length || length != 0
                    : kind == (int)TokenKind.EndOfFile || length == 0))
            {
                throw new ArgumentException("Invalid scanner token record.", nameof(bytes));
            }

            tokens.Add(new ScannerToken((TokenKind)kind, new TextSpan(start, length)));
            previousEnd = start + length;
        }

        return tokens.MoveToImmutable();
    }

    /// <summary>Reads and validates trivia assigned to each token, including EOF, and verifies lossless source coverage.</summary>
    /// <param name="source">The original source snapshot.</param>
    /// <param name="tokens">The token records read from the same scan.</param>
    /// <param name="bytes">Little-endian trivia words: count followed by (kind, start, length, token index) records.</param>
    /// <returns>Leading trivia records in source order.</returns>
    /// <exception cref="ArgumentNullException">The source is null.</exception>
    /// <exception cref="ArgumentException">The result violates the lossless scanner interchange contract.</exception>
    public static ImmutableArray<ScannerTrivia> ReadTrivia(SourceText source, ImmutableArray<ScannerToken> tokens, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (tokens.IsDefaultOrEmpty || bytes.Length < 4)
        {
            throw new ArgumentException("Missing scanner tokens or trivia count.", nameof(bytes));
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (count < 0 || count > source.Length || count > (bytes.Length - 4) / 16)
        {
            throw new ArgumentException("Invalid scanner trivia count.", nameof(bytes));
        }

        var trivia = ImmutableArray.CreateBuilder<ScannerTrivia>(count);
        var position = 0;
        var tokenIndex = 0;
        for (var index = 0; index < count; index++)
        {
            var record = bytes.Slice(4 + (index * 16), 16);
            var kind = BinaryPrimitives.ReadInt32LittleEndian(record);
            var start = BinaryPrimitives.ReadInt32LittleEndian(record[4..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(record[8..]);
            var owner = BinaryPrimitives.ReadInt32LittleEndian(record[12..]);
            if (!Enum.IsDefined((TriviaKind)kind) || owner < tokenIndex || owner >= tokens.Length
                || start < 0 || length <= 0 || length > source.Length - start)
            {
                throw new ArgumentException("Invalid scanner trivia record.", nameof(bytes));
            }

            while (tokenIndex < owner)
            {
                if (tokens[tokenIndex].Span.Start != position)
                {
                    throw new ArgumentException("Scanner records do not cover the source.", nameof(bytes));
                }
                position = tokens[tokenIndex++].Span.End;
            }

            if (start != position || start + length > tokens[owner].Span.Start)
            {
                throw new ArgumentException("Scanner trivia overlaps its token.", nameof(bytes));
            }

            trivia.Add(new ScannerTrivia((TriviaKind)kind, new TextSpan(start, length), owner));
            position += length;
        }

        while (tokenIndex < tokens.Length)
        {
            if (tokens[tokenIndex].Span.Start != position)
            {
                throw new ArgumentException("Scanner records do not cover the source.", nameof(bytes));
            }
            position = tokens[tokenIndex++].Span.End;
        }

        return trivia.MoveToImmutable();
    }

    /// <summary>Validates published integer-decoder records without repeating the native magnitude computation.</summary>
    /// <param name="source">The source scanned by the component.</param>
    /// <param name="tokens">Token records from the same scan, including EOF.</param>
    /// <param name="bytes">Little-endian words: count, then (token index, status, low 32 bits, high 32 bits, explicit tag) records.</param>
    /// <returns>Decoded unsigned magnitudes for integer-form numeric tokens; omitted noninteger tokens have no record.</returns>
    /// <exception cref="ArgumentNullException">The source is null.</exception>
    /// <exception cref="ArgumentException">A count, record, numeric spelling, tag, or canonical overflow payload is invalid.</exception>
    public static ImmutableArray<ScannerIntegerValue> ReadIntegerValues(SourceText source,
        ImmutableArray<ScannerToken> tokens, ReadOnlySpan<byte> bytes)
        => ReadIntegerValuesCore(source, tokens, bytes, static token => token.Kind, static token => token.Span);

    /// <summary>Validates integer-decoder records against tokens already materialized for the official parser.</summary>
    /// <param name="source">The source scanned by the component.</param>
    /// <param name="tokens">The scanner's adapted lexical tokens, including EOF.</param>
    /// <param name="bytes">Little-endian words: count, then (token index, status, low 32 bits, high 32 bits, explicit tag) records.</param>
    /// <returns>Decoded integer magnitudes without re-lexing or rematerializing token records.</returns>
    /// <exception cref="ArgumentNullException">The source is null.</exception>
    /// <exception cref="ArgumentException">A count, record, numeric spelling, tag, or canonical overflow payload is invalid.</exception>
    public static ImmutableArray<ScannerIntegerValue> ReadIntegerValues(SourceText source,
        ImmutableArray<SyntaxToken> tokens, ReadOnlySpan<byte> bytes)
        => ReadIntegerValuesCore(source, tokens, bytes, static token => token.Kind, static token => token.Span);

    private static ImmutableArray<ScannerIntegerValue> ReadIntegerValuesCore<TToken>(SourceText source,
        ImmutableArray<TToken> tokens, ReadOnlySpan<byte> bytes, Func<TToken, TokenKind> getKind,
        Func<TToken, TextSpan> getSpan) where TToken : class
    {
        ArgumentNullException.ThrowIfNull(source);
        if (tokens.IsDefaultOrEmpty || bytes.Length < 4)
        {
            throw new ArgumentException("Missing scanner tokens or integer-value count.", nameof(bytes));
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (count < 0 || count > source.Length || count >= tokens.Length || count > (bytes.Length - 4) / 20)
        {
            throw new ArgumentException("Invalid scanner integer-value count.", nameof(bytes));
        }

        var values = ImmutableArray.CreateBuilder<ScannerIntegerValue>(count);
        var previousIndex = -1;
        for (var index = 0; index < count; index++)
        {
            var record = bytes.Slice(4 + (index * 20), 20);
            var tokenIndex = BinaryPrimitives.ReadInt32LittleEndian(record);
            var status = (ScannerIntegerStatus)BinaryPrimitives.ReadInt32LittleEndian(record[4..]);
            var low = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]);
            var high = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
            var tag = (ScannerIntegerTag)BinaryPrimitives.ReadInt32LittleEndian(record[16..]);
            if (tokenIndex <= previousIndex || tokenIndex >= tokens.Length - 1
                || !Enum.IsDefined(status) || !Enum.IsDefined(tag)
                || (status == ScannerIntegerStatus.MagnitudeOverflow && (low != 0 || high != 0)))
            {
                throw new ArgumentException("Invalid scanner integer-value record.", nameof(bytes));
            }

            if (tokens[tokenIndex] is not TToken token)
            {
                throw new ArgumentException("Missing integer token reference.", nameof(bytes));
            }

            var span = getSpan(token);
            var suffix = tag switch {
                ScannerIntegerTag.None => "",
                ScannerIntegerTag.I8 => "i8",
                ScannerIntegerTag.I16 => "i16",
                ScannerIntegerTag.I32 => "i32",
                ScannerIntegerTag.I64 => "i64",
                ScannerIntegerTag.I128 => "i128",
                ScannerIntegerTag.U8 => "u8",
                ScannerIntegerTag.U16 => "u16",
                ScannerIntegerTag.U32 => "u32",
                ScannerIntegerTag.U64 => "u64",
                ScannerIntegerTag.U128 => "u128",
                _ => throw new ArgumentException("Invalid scanner integer tag.", nameof(bytes)),
            };
            if (getKind(token) != TokenKind.NumericLiteral || span.Start > source.Length
                || span.Length > source.Length - span.Start
                || span.Length <= suffix.Length)
            {
                throw new ArgumentException("Invalid integer token reference.", nameof(bytes));
            }

            var spelling = source.Text.AsSpan(span.Start, span.Length);
            var digits = spelling[..^suffix.Length];
            if (!spelling.EndsWith(suffix, StringComparison.Ordinal)
                || IntegerDigitCount(digits) != digits.Length)
            {
                throw new ArgumentException("Integer record does not match its source token.", nameof(bytes));
            }

            values.Add(new ScannerIntegerValue(tokenIndex, suffix,
                status == ScannerIntegerStatus.MagnitudeOverflow,
                status == ScannerIntegerStatus.Decoded ? ((ulong)high << 32) | low : null));
            previousIndex = tokenIndex;
        }

        return values.MoveToImmutable();
    }

    internal static int IntegerRadix(ReadOnlySpan<char> text) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16
            : text.StartsWith("0b", StringComparison.OrdinalIgnoreCase) ? 2 : 10;

    private static bool IsRadixDigit(char digit, int radix)
    {
        if (digit is >= '0' and <= '9')
        {
            return radix != 2 || digit <= '1';
        }
        if (radix == 16)
        {
            return digit is (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
        }
        return false;
    }

    internal static int IntegerDigitCount(ReadOnlySpan<char> text)
    {
        var radix = IntegerRadix(text);
        var index = radix == 10 ? 0 : 2;
        var hasDigit = false;
        while (index < text.Length)
        {
            if (IsRadixDigit(text[index], radix))
            {
                hasDigit = true;
                index++;
                continue;
            }
            if (text[index] != '_' || (!hasDigit && radix == 10))
            {
                break;
            }
            var next = index + 1;
            while (next < text.Length && text[next] == '_')
            {
                next++;
            }
            if (next == text.Length || !IsRadixDigit(text[next], radix))
            {
                break;
            }
            index = next;
        }
        return hasDigit ? index : 0;
    }

    internal static bool TryParseIntegerMagnitude(ReadOnlySpan<char> text, out ulong magnitude)
    {
        var radix = IntegerRadix(text);
        magnitude = 0;
        for (var index = radix == 10 ? 0 : 2; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '_')
            {
                continue;
            }
            var digit = character switch {
                >= '0' and <= '9' => character - '0',
                >= 'a' and <= 'f' => character - 'a' + 10,
                _ => character - 'A' + 10,
            };
            if (magnitude > (ulong.MaxValue - (uint)digit) / (uint)radix)
            {
                return false;
            }
            magnitude = (magnitude * (uint)radix) + (uint)digit;
        }
        return true;
    }

    internal static void ValidateIntegerValueCoverage(SourceText source,
        ImmutableArray<SyntaxToken> tokens, ImmutableArray<ScannerIntegerValue> values)
    {
        if (values.IsDefault)
        {
            throw new ArgumentException("Missing scanner integer values.", nameof(values));
        }

        var next = 0;
        for (var tokenIndex = 0; tokenIndex < tokens.Length - 1; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            if (token.Kind != TokenKind.NumericLiteral)
            {
                continue;
            }

            var text = source.Text.AsSpan(token.Span.Start, token.Span.Length);
            var digits = IntegerDigitCount(text);

            var tag = text[digits..] switch {
                [] => "",
                ['i', '8'] => "i8",
                ['i', '1', '6'] => "i16",
                ['i', '3', '2'] => "i32",
                ['i', '6', '4'] => "i64",
                ['i', '1', '2', '8'] => "i128",
                ['u', '8'] => "u8",
                ['u', '1', '6'] => "u16",
                ['u', '3', '2'] => "u32",
                ['u', '6', '4'] => "u64",
                ['u', '1', '2', '8'] => "u128",
                _ => null,
            };
            if (digits == 0 || tag is null)
            {
                continue;
            }

            if (next >= values.Length || values[next] is not ScannerIntegerValue value
                || value.TokenIndex != tokenIndex || value.Tag != tag
                || value.IsOverflow == value.Magnitude.HasValue)
            {
                throw new ArgumentException("Scanner integer values do not match the lexical stream.", nameof(values));
            }
            next++;
        }
        if (next != values.Length)
        {
            throw new ArgumentException("Scanner integer values do not match the lexical stream.", nameof(values));
        }
    }

    /// <summary>Validates and reads the published diagnostic prefix from the compiled scanner.</summary>
    /// <param name="source">The original source snapshot.</param>
    /// <param name="bytes">Little-endian words containing a count followed by (code, start, length) records.</param>
    /// <returns>Diagnostic codes and spans in reporting order.</returns>
    /// <exception cref="ArgumentNullException">The source is null.</exception>
    /// <exception cref="ArgumentException">The result violates the scanner interchange contract.</exception>
    public static ImmutableArray<ScannerDiagnostic> ReadDiagnostics(SourceText source, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (bytes.Length < 4)
        {
            throw new ArgumentException("Missing scanner diagnostic count.", nameof(bytes));
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (count < 0 || count > source.Length || count > (bytes.Length - 4) / 12)
        {
            throw new ArgumentException("Invalid scanner diagnostic count.", nameof(bytes));
        }

        var diagnostics = ImmutableArray.CreateBuilder<ScannerDiagnostic>(count);
        for (var index = 0; index < count; index++)
        {
            var record = bytes.Slice(4 + (index * 12), 12);
            var code = BinaryPrimitives.ReadInt32LittleEndian(record);
            var start = BinaryPrimitives.ReadInt32LittleEndian(record[4..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(record[8..]);
            if (code is < 1 or > 9 || start < 0 || start > source.Length
                || length <= 0 || length > source.Length - start)
            {
                throw new ArgumentException("Invalid scanner diagnostic record.", nameof(bytes));
            }

            diagnostics.Add(new ScannerDiagnostic($"TRM{code:0000}", new TextSpan(start, length)));
        }

        return diagnostics.MoveToImmutable();
    }

    /// <summary>Adapts a completed native scan into the lexical input consumed by the official parser, without re-lexing.</summary>
    /// <param name="source">The exact source snapshot used by the scanner.</param>
    /// <param name="count">The scanner's token count, including EOF.</param>
    /// <param name="tokenBytes">The copied token buffer.</param>
    /// <param name="triviaBytes">The copied leading-trivia buffer.</param>
    /// <param name="diagnosticBytes">The copied lexical-diagnostic buffer.</param>
    /// <param name="cancellationToken">A token to cancel record materialization.</param>
    /// <returns>A lossless lexical result with source-owned text and scanner-supplied classification.</returns>
    /// <exception cref="ArgumentNullException">The source is null.</exception>
    /// <exception cref="ArgumentException">The records violate the scanner interchange contract.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static LexResult ReadLexResult(SourceText source, int count, ReadOnlySpan<byte> tokenBytes,
        ReadOnlySpan<byte> triviaBytes, ReadOnlySpan<byte> diagnosticBytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var records = ReadTokens(source, count, tokenBytes);
        var trivia = ReadTrivia(source, records, triviaBytes);
        var diagnosticRecords = ReadDiagnostics(source, diagnosticBytes);
        var tokens = ImmutableArray.CreateBuilder<SyntaxToken>(records.Length);
        var triviaIndex = 0;
        for (var index = 0; index < records.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leading = ImmutableArray.CreateBuilder<SyntaxTrivia>();
            while (triviaIndex < trivia.Length && trivia[triviaIndex].TokenIndex == index)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = trivia[triviaIndex++];
                leading.Add(new SyntaxTrivia(item.Kind, item.Span, source.Text.Substring(item.Span.Start, item.Span.Length)));
            }
            var record = records[index];
            tokens.Add(new SyntaxToken(record.Kind, source.Text.Substring(record.Span.Start, record.Span.Length),
                record.Span, leading.ToImmutable()));
        }
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>(diagnosticRecords.Length);
        foreach (var diagnostic in diagnosticRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = diagnostic.Code switch {
                "TRM0001" => "Unrecognized character or malformed UTF-16 code unit.",
                "TRM0002" => "Unterminated string literal.",
                "TRM0003" => "Unterminated block comment.",
                "TRM0004" => "An exponent requires at least one decimal digit.",
                "TRM0005" => "Invalid string escape sequence.",
                "TRM0006" => "Numeric width suffixes are not supported.",
                "TRM0007" => "Hexadecimal prefix requires an ASCII hex digit.",
                "TRM0008" => "Binary prefix requires an ASCII binary digit.",
                "TRM0009" => "Digit separators must appear between digits.",
                _ => throw new ArgumentException("Unknown scanner diagnostic.", nameof(diagnosticBytes)),
            };
            diagnostics.Add(new Diagnostic(diagnostic.Code, DiagnosticSeverity.Error, message, diagnostic.Span));
        }
        return new LexResult(source, tokens.MoveToImmutable(), diagnostics.MoveToImmutable());
    }
}

/// <summary>Legacy scanner-2 categories. Current scans publish exact <see cref="TokenKind"/> values.</summary>
public enum ScannerTokenKind
{
    /// <summary>The end of the source snapshot.</summary>
    EndOfFile,
    /// <summary>An identifier or unclassified keyword spelling.</summary>
    Identifier,
    /// <summary>An ASCII numeric literal spelling, including fractions, exponents and width tags.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names a lexical category rather than a CLR storage type.")]
    Integer,
    /// <summary>An operator or punctuation token.</summary>
    Punctuation,
    /// <summary>An unrecognized source scalar or malformed UTF-16 unit.</summary>
    Invalid,
    /// <summary>A raw quoted string spelling, including malformed or unterminated strings.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names a lexical category rather than a CLR storage type.")]
    String,
}

/// <summary>A compiled scanner token with an exact source range.</summary>
/// <param name="Kind">The exact host lexical token kind.</param>
/// <param name="Span">The half-open UTF-16 range in the original input.</param>
public sealed record ScannerToken(TokenKind Kind, TextSpan Span);

/// <summary>A leading trivia record associated with a token (including EOF).</summary>
/// <param name="Kind">The host lexical trivia kind.</param>
/// <param name="Span">The half-open UTF-16 source range.</param>
/// <param name="TokenIndex">The owning token's zero-based index.</param>
public sealed record ScannerTrivia(TriviaKind Kind, TextSpan Span, int TokenIndex);

/// <summary>Whether an integer-form numeric token's unsigned magnitude fits in 64 bits.</summary>
public enum ScannerIntegerStatus
{
    /// <summary>The exact unsigned magnitude is available.</summary>
    Decoded,
    /// <summary>The magnitude exceeds <see cref="ulong.MaxValue"/>; the payload is zero.</summary>
    MagnitudeOverflow,
}

/// <summary>The explicit integer-width suffix of a numeric token, without applying range typing.</summary>
public enum ScannerIntegerTag
{
    /// <summary>No integer-width suffix.</summary>
    None,
    /// <summary>The i8 suffix.</summary>
    I8,
    /// <summary>The i16 suffix.</summary>
    I16,
    /// <summary>The i32 suffix.</summary>
    I32,
    /// <summary>The i64 suffix.</summary>
    I64,
    /// <summary>The i128 suffix.</summary>
    I128,
    /// <summary>The u8 suffix.</summary>
    U8,
    /// <summary>The u16 suffix.</summary>
    U16,
    /// <summary>The u32 suffix.</summary>
    U32,
    /// <summary>The u64 suffix.</summary>
    U64,
    /// <summary>The u128 suffix.</summary>
    U128,
}

/// <summary>A native-decoded unsigned integer magnitude, without a default or target type.</summary>
/// <param name="TokenIndex">The owning numeric token's zero-based index.</param>
/// <param name="Tag">The exact lowercase integer-width suffix, or an empty string when untagged.</param>
/// <param name="IsOverflow">Whether the magnitude exceeds <see cref="ulong.MaxValue"/>.</param>
/// <param name="Magnitude">The exact unsigned value when available; null on overflow.</param>
public sealed record ScannerIntegerValue(int TokenIndex, string Tag, bool IsOverflow, ulong? Magnitude);

/// <summary>A compiled scanner lexical diagnostic with its exact source range.</summary>
/// <param name="Code">The host lexer diagnostic code.</param>
/// <param name="Span">The half-open UTF-16 range in the original input.</param>
public sealed record ScannerDiagnostic(string Code, TextSpan Span);
