// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A registry-shaped Constraint owner, kind and payload.</summary>
/// <param name="OwnerGenericParamRow">The one-based owning GenericParam row.</param>
/// <param name="Kind">The assigned Constraint kind.</param>
/// <param name="Payload">The kind-specific payload word.</param>
public readonly record struct TrRmConstraintRow(
    int OwnerGenericParamRow, ushort Kind, uint Payload);

/// <summary>Emits assigned Constraint rows without proving the generic requirements.</summary>
public static class TrRmConstraintTableWriter
{
    /// <summary>Encodes Constraint rows and validates their owners and payload forms.</summary>
    /// <param name="rows">The Constraint rows in one-based table order.</param>
    /// <param name="formals">The validated GenericParam table.</param>
    /// <param name="types">The validated TypeSpec table used by type-valued payloads.</param>
    /// <returns>The encoded Constraint section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmConstraintRow> rows,
        TrRmGenericParamTableReader formals, TrRmTypeSpecTableReader types)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 12];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 12;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), unchecked((uint)row.OwnerGenericParamRow));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset + 4), row.Kind);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 8), row.Payload);
        }

        _ = new TrRmConstraintTableReader(result, rows.Count, formals, types);
        return result;
    }
}
