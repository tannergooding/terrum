// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A target-specific TypeDef layout and its ordered instance-field range.</summary>
/// <param name="OwnerTypeDefRow">The one-based owning TypeDef row.</param>
/// <param name="TargetProfileRef">The one-based TargetProfile row, or zero for the default.</param>
/// <param name="LayoutKind">The assigned layout-kind code.</param>
/// <param name="Flags">The assigned Layout flags.</param>
/// <param name="Alignment">The declared aggregate alignment, or zero when omitted.</param>
/// <param name="Pack">The declared packing limit, or zero when inapplicable.</param>
/// <param name="LayoutFieldFirst">The first owned LayoutField row, or zero when empty.</param>
/// <param name="LayoutFieldCount">The number of ordered LayoutField rows.</param>
public readonly record struct TrRmLayoutRow(
    int OwnerTypeDefRow, uint TargetProfileRef, byte LayoutKind, byte Flags,
    uint Alignment, uint Pack, int LayoutFieldFirst, int LayoutFieldCount);

/// <summary>Emits complete Layout variants with checked owners and field ranges.</summary>
public static class TrRmLayoutTableWriter
{
    /// <summary>Encodes and validates target-specific Layout variants.</summary>
    /// <param name="rows">The Layout rows in one-based table order.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="targetProfiles">The validated TargetProfile table.</param>
    /// <param name="layoutFieldCount">The number of rows in the LayoutField table.</param>
    /// <returns>The encoded Layout section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmLayoutRow> rows,
        TrRmTypeDefTableReader types, TrRmTargetProfileTableReader targetProfiles,
        int layoutFieldCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 28];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 28;
            Write(result, offset, row.OwnerTypeDefRow);
            Write(result, offset + 4, row.TargetProfileRef);
            result[offset + 8] = row.LayoutKind;
            result[offset + 9] = row.Flags;
            Write(result, offset + 12, row.Alignment);
            Write(result, offset + 16, row.Pack);
            Write(result, offset + 20, row.LayoutFieldFirst);
            Write(result, offset + 24, row.LayoutFieldCount);
        }

        _ = new TrRmLayoutTableReader(result, rows.Count,
            types, targetProfiles, layoutFieldCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => Write(bytes, offset, unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
