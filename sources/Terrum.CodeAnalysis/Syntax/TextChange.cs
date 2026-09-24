// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Syntax;

/// <summary>A replacement in the UTF-16 coordinates of the original snapshot.</summary>
public sealed class TextChange
{
    /// <summary>Creates an edit targeting a range of the original snapshot.</summary>
    /// <param name="span">The half-open UTF-16 range to replace.</param>
    /// <param name="newText">The exact replacement text, which may be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="newText"/> is null.</exception>
    public TextChange(TextSpan span, string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        Span = span;
        NewText = newText;
    }

    /// <summary>Gets the replaced range in the original snapshot.</summary>
    public TextSpan Span { get; }

    /// <summary>Gets the text to insert in place of <see cref="Span"/>.</summary>
    public string NewText { get; }
}
