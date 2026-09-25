// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Semantics;

internal static class ValueLayout
{
    internal const int MaximumSize = 16;

    internal static int Size(PrimitiveType type, IReadOnlyList<BoundStruct> structures) => type switch {
        PrimitiveType.Bool => 1,
        PrimitiveType.Char16 => 2,
        PrimitiveType.Int32 or PrimitiveType.UInt32 => 4,
        PrimitiveType.Int64 or PrimitiveType.UInt64 => 8,
        PrimitiveType.Void => 0,
        _ when (int)type >= 100 && (int)type - 100 < structures.Count => structures[(int)type - 100].Size,
        _ => 0,
    };

    // Callers have established that all nested layouts are acyclic and complete.
    internal static int Alignment(PrimitiveType type, IReadOnlyList<BoundStruct> structures)
    {
        if ((int)type < 100)
        {
            return Size(type, structures);
        }
        var alignment = 1;
        foreach (var field in structures[(int)type - 100].Fields)
        {
            alignment = Math.Max(alignment, Alignment(field.Type, structures));
        }
        return alignment;
    }
}
