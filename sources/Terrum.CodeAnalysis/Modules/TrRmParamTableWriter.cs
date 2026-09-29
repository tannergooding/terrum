// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmParamRow(
    int OwnerMethodSigRow, int Ordinal, int TypeSpecRow, byte Mode);

/// <summary>Emits Param rows with checked MethodSig ownership, mode and ordinal.</summary>
public static class TrRmParamTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmParamRow> rows,
        TrRmMethodSigTableReader signatures, TrRmTypeSpecTableReader types)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 16];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 16;
            Write(result, offset, row.OwnerMethodSigRow);
            Write(result, offset + 4, row.Ordinal);
            Write(result, offset + 8, row.TypeSpecRow);
            result[offset + 12] = row.Mode;
        }

        _ = new TrRmParamTableReader(result, rows.Count, signatures, types);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), unchecked((uint)value));
}
