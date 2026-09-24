// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

using System;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Syntax;

/// <summary>A replacement in the UTF-16 coordinates of the original snapshot.</summary>
public sealed class TextChange
{
    public TextChange(TextSpan span, string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        Span = span;
        NewText = newText;
    }

    public TextSpan Span { get; }

    public string NewText { get; }
}
