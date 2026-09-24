// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;

namespace Terrum.CodeAnalysis.Text;

/// <summary>A half-open range of UTF-16 source offsets.</summary>
public readonly record struct TextSpan
{
    /// <summary>Creates a half-open source range.</summary>
    /// <param name="start">The zero-based UTF-16 start offset.</param>
    /// <param name="length">The range length in UTF-16 code units.</param>
    /// <exception cref="ArgumentOutOfRangeException">An argument is negative or the end offset exceeds <see cref="int.MaxValue"/>.</exception>
    public TextSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, int.MaxValue - start);

        Start = start;
        Length = length;
    }

    /// <summary>Gets the zero-based UTF-16 start offset.</summary>
    public int Start { get; }
    /// <summary>Gets the length in UTF-16 code units.</summary>
    public int Length { get; }
    /// <summary>Gets the exclusive UTF-16 end offset.</summary>
    public int End => Start + Length;
}
