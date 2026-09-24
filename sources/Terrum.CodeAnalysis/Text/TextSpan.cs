// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

using System;

namespace Terrum.CodeAnalysis.Text;

/// <summary>A half-open range of UTF-16 source offsets.</summary>
public readonly record struct TextSpan
{
    public TextSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > int.MaxValue - start)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Start = start;
        Length = length;
    }

    public int Start { get; }
    public int Length { get; }
    public int End => Start + Length;
}
