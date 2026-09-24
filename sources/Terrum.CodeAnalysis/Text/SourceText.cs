// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

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

        ImmutableArray<SourceLine>.Builder lines = ImmutableArray.CreateBuilder<SourceLine>();
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            int lineBreakLength = text[index] switch
            {
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

    public static SourceText From(string text, string path = "")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(path);

        return new SourceText(text, path);
    }

    public string Text { get; }
    public string Path { get; }
    public int Length => Text.Length;
    public char this[int index] => Text[index];
    public ImmutableArray<SourceLine> Lines { get; }

    public string GetSubText(TextSpan span)
    {
        if (span.Start > Length || span.Length > Length - span.Start)
        {
            throw new ArgumentOutOfRangeException(nameof(span));
        }

        return Text.Substring(span.Start, span.Length);
    }

    public LinePosition GetLinePosition(int position)
    {
        if (position < 0 || position > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        int low = 0;
        int high = Lines.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
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

    public override string ToString() => Text;
}
