// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A parameter type, passing mode, and source-call adaptation contract.</summary>
/// <param name="OwnerMethodSigRow">The owning MethodSig row.</param>
/// <param name="Ordinal">The zero-based formal ordinal.</param>
/// <param name="TypeSpecRef">The complete parameter TypeSpecRef.</param>
/// <param name="Mode">The selected schema's parameter mode.</param>
/// <param name="Flags">The Standard source-call adaptation flags.</param>
public readonly record struct TrRmParamRow(
    int OwnerMethodSigRow, int Ordinal, uint TypeSpecRef, byte Mode,
    byte Flags = 0);

/// <summary>Emits Param rows with checked MethodSig ownership, mode, flags and ordinal.</summary>
public static class TrRmParamTableWriter
{
    /// <summary>Encodes Param rows using the Standard schema.</summary>
    /// <param name="rows">The Param rows in table order.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <returns>The encoded fixed-width Param section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmParamRow> rows,
        TrRmMethodSigTableReader signatures, TrRmTypeSpecTableReader types)
        => Encode(rows, signatures, types, TrRmArtifactSchema.Standard);

    internal static byte[] Encode(IReadOnlyList<TrRmParamRow> rows,
        TrRmMethodSigTableReader signatures, TrRmTypeSpecTableReader types,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var rowCount = (ulong)rows.Count;
        var work = checked(1UL + (rowCount * (1UL + 5UL + 16UL)));
        resourceLedger?.ConsumeWork(work);
        var result = new byte[rows.Count * 16];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 16;
            Write(result, offset, row.OwnerMethodSigRow);
            Write(result, offset + 4, row.Ordinal);
            Write(result, offset + 8, row.TypeSpecRef);
            result[offset + 12] = row.Mode;
            result[offset + 13] = row.Flags;
        }

        _ = new TrRmParamTableReader(result, rows.Count, signatures, types,
            schema, resourceLedger);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
