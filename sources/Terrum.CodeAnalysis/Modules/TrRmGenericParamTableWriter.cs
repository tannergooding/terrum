// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmGenericParamRow(
    uint OwnerDefId, int Ordinal, int NameStringRow, int ConstraintFirst, int ConstraintCount);

/// <summary>Emits formal rows with checked type/method ownership, ordinals and constraint ranges.</summary>
public static class TrRmGenericParamTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmGenericParamRow> rows,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        int constraintCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 20];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 20;
            Write(result, offset, row.OwnerDefId);
            Write(result, offset + 4, unchecked((uint)row.Ordinal));
            Write(result, offset + 8, unchecked((uint)row.NameStringRow));
            Write(result, offset + 12, unchecked((uint)row.ConstraintFirst));
            Write(result, offset + 16, unchecked((uint)row.ConstraintCount));
        }

        _ = new TrRmGenericParamTableReader(result, rows.Count,
            strings, types, methods, signatures, constraintCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
