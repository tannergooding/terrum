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

    /// <summary>The version of the exact-kind, lossless token/trivia interchange.</summary>
    public const int SchemaVersion = 3;

    /// <summary>The component identity for the exact-kind, lossless scanner protocol.</summary>
    public const string ComponentName = "scanner-3";

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
    /// <returns>Initialized buffers exposed as Host.Source, Host.XidStart, Host.XidContinue, Host.Tokens, Host.Diagnostics, Host.Trivia, Host.Keywords and Host.Operators.</returns>
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
            if (code is < 1 or > 5 || start < 0 || start > source.Length
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
                _ => throw new ArgumentException("Unknown scanner diagnostic.", nameof(diagnosticBytes)),
            };
            diagnostics.Add(new Diagnostic(diagnostic.Code, DiagnosticSeverity.Error, message, diagnostic.Span));
        }
        return new LexResult(source, tokens.MoveToImmutable(), diagnostics.MoveToImmutable());
    }
}

/// <summary>Legacy scanner-2 categories. Scanner-3 publishes exact <see cref="TokenKind"/> values.</summary>
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

/// <summary>A compiled scanner lexical diagnostic with its exact source range.</summary>
/// <param name="Code">The host lexer diagnostic code.</param>
/// <param name="Span">The half-open UTF-16 range in the original input.</param>
public sealed record ScannerDiagnostic(string Code, TextSpan Span);
