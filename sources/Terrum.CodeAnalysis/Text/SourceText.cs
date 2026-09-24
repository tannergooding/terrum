// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Text;

/// <summary>An immutable UTF-16 source snapshot, retaining even malformed surrogate units.</summary>
public sealed class SourceText
{
    private SourceText(string text, string path)
    {
        Text = text;
        Path = path;

        var lines = ImmutableArray.CreateBuilder<SourceLine>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var lineBreakLength = text[index] switch {
                '\r' when index + 1 < text.Length && text[index + 1] == '\n' => 2,
                '\r' or '\n' or '\u0085' or '\u2028' or '\u2029' => 1,
                _ => 0,
            };
            if (lineBreakLength == 0)
            {
                continue;
            }

            lines.Add(new SourceLine(new TextSpan(start, index - start),
                                     new TextSpan(start, index + lineBreakLength - start)));
            index += lineBreakLength - 1;
            start = index + 1;
        }

        lines.Add(new SourceLine(new TextSpan(start, text.Length - start),
                                 new TextSpan(start, text.Length - start)));
        Lines = lines.ToImmutable();
    }

    /// <summary>Creates a source snapshot without normalizing line endings or surrogate units.</summary>
    /// <param name="text">The source content.</param>
    /// <param name="path">The associated path, or an empty string for unnamed source.</param>
    /// <returns>The immutable source snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="path"/> is null.</exception>
    public static SourceText From(string text, string path = "")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(path);

        return new SourceText(text, path);
    }

    /// <summary>Gets the original UTF-16 source content.</summary>
    public string Text { get; }
    /// <summary>Gets the path supplied when this snapshot was created.</summary>
    public string Path { get; }
    /// <summary>Gets the length of the source in UTF-16 code units.</summary>
    public int Length => Text.Length;
    /// <summary>Gets the UTF-16 code unit at a zero-based source offset.</summary>
    /// <param name="index">The zero-based source offset.</param>
    /// <returns>The code unit at <paramref name="index"/>.</returns>
    public char this[int index] => Text[index];
    /// <summary>Gets the source lines, including a final empty line when the source ends in a line break.</summary>
    public ImmutableArray<SourceLine> Lines { get; }

    /// <summary>Extracts the source content within a half-open UTF-16 range.</summary>
    /// <param name="span">The range within this snapshot.</param>
    /// <returns>The exact source substring.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="span"/> extends past the end of this snapshot.</exception>
    public string GetSubText(TextSpan span)
    {
        if (span.Start > Length || span.Length > Length - span.Start)
        {
            throw new ArgumentOutOfRangeException(nameof(span));
        }

        return Text.Substring(span.Start, span.Length);
    }

    /// <summary>Maps a UTF-16 source offset to a zero-based line and character position.</summary>
    /// <param name="position">The offset, including the position immediately after the last code unit.</param>
    /// <returns>The line position; an offset within a line break is measured from that line's start.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is outside the source.</exception>
    public LinePosition GetLinePosition(int position)
    {
        if (position < 0 || position > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var low = 0;
        var high = Lines.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (Lines[middle].Span.Start <= position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return new LinePosition(high, position - Lines[high].Span.Start);
    }

    /// <summary>Returns the original source content.</summary>
    /// <returns>The unmodified source string.</returns>
    public override string ToString() => Text;
}
