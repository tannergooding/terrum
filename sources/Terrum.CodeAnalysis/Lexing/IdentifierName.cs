// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Text;

namespace Terrum.CodeAnalysis.Lexing;

/// <summary>The ordinal identifier identity and approved keyword-escape vocabulary.</summary>
public static class IdentifierName
{
    /// <summary>Gets the closed escape catalog from the source grammar vocabulary and contextual table.</summary>
    public static ImmutableArray<string> EscapableKeywords { get; } =
    [
        "using", "namespace", "public", "internal", "private", "struct", "class",
        "interface", "enum", "union", "alias", "type", "partial", "extension",
        "extern", "static", "operator", "get", "set", "init", "field", "this",
        "where", "const", "void", "var", "unique", "own", "move", "mut", "ref",
        "write", "pin", "implicit", "params", "if", "else", "switch", "default",
        "when", "is", "some", "and", "or", "not", "while", "do", "for", "in",
        "break", "continue", "return", "yield", "async", "await", "require",
        "ensure", "try", "catch", "finally", "throw", "defer", "unsafe", "policy",
        "true", "false", "null", "new", "via",
    ];

    /// <summary>Tests an exact reserved or contextual spelling, without case folding or normalization.</summary>
    public static bool CanEscape(ReadOnlySpan<char> spelling)
    {
        foreach (var keyword in EscapableKeywords)
        {
            if (spelling.SequenceEqual(keyword))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tests whether raw text is a valid contiguous keyword escape.</summary>
    public static bool IsEscape(ReadOnlySpan<char> text)
        => text.Length > 1 && text[0] == '@' && CanEscape(text[1..]);

    /// <summary>Gets the ordinal name of validated identifier text, preserving all other spellings.</summary>
    public static string GetName(string text)
        => IsEscape(text.AsSpan()) ? text[1..] : text;

    // Preserve the complete current identifier candidate, including rejected escapes.
    internal static int EscapeLength(ReadOnlySpan<char> source)
    {
        var length = 1;
        if (source.Length > 1 && TryReadScalar(source[1..], out var scalar, out var width)
            && (scalar == '_' || UnicodeXid.IsStart(scalar)))
        {
            length += width;
            while (length < source.Length
                && TryReadScalar(source[length..], out scalar, out width)
                && (scalar == '_' || UnicodeXid.IsContinue(scalar)))
            {
                length += width;
            }
        }

        return length;
    }

    private static bool TryReadScalar(ReadOnlySpan<char> source, out int scalar, out int width)
    {
        if (source.IsEmpty)
        {
            scalar = 0;
            width = 0;
            return false;
        }

        var unit = source[0];
        if (!char.IsSurrogate(unit))
        {
            scalar = unit;
            width = 1;
            return true;
        }

        var status = Rune.DecodeFromUtf16(source, out var rune, out width);
        scalar = rune.Value;
        return status == OperationStatus.Done;
    }
}
