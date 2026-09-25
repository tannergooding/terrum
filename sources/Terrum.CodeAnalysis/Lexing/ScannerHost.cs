// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

/// <summary>Supplies owned input and output storage to the compiled Terrum scanner component.</summary>
public static class ScannerHost
{
    /// <summary>The maximum source length accepted by this bounded scanner adapter.</summary>
    public const int MaximumSourceLength = 16_384;

    /// <summary>Creates fixed buffers containing source code units, shared pinned tables and vacant token records.</summary>
    /// <param name="source">The immutable source snapshot, including any malformed UTF-16 units.</param>
    /// <returns>Initialized buffers exposed as Host.Source, Host.XidStart, Host.XidContinue, Host.Tokens and Host.Diagnostics.</returns>
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
        ];
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
            if (kind is < 0 or > 5 || start < previousEnd || start > source.Length
                || length < 0 || length > source.Length - start
                || (index == count - 1 ? kind != 0 || start != source.Length || length != 0 : kind == 0 || length == 0))
            {
                throw new ArgumentException("Invalid scanner token record.", nameof(bytes));
            }

            tokens.Add(new ScannerToken((ScannerTokenKind)kind, new TextSpan(start, length)));
            previousEnd = start + length;
        }

        return tokens.MoveToImmutable();
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
}

/// <summary>A category in the bounded compiled scanner interchange, not the complete host lexer grammar.</summary>
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
/// <param name="Kind">The bounded scanner category.</param>
/// <param name="Span">The half-open UTF-16 range in the original input.</param>
public sealed record ScannerToken(ScannerTokenKind Kind, TextSpan Span);

/// <summary>A compiled scanner lexical diagnostic with its exact source range.</summary>
/// <param name="Code">The host lexer diagnostic code.</param>
/// <param name="Span">The half-open UTF-16 range in the original input.</param>
public sealed record ScannerDiagnostic(string Code, TextSpan Span);
