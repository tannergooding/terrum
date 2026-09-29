// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmMethodDefRow(
    int OwnerTypeDefRow, int NameStringRow, int SignatureRow, uint Flags,
    int ContractFirst, int ContractCount, int BodyRow);

/// <summary>Emits MethodDef rows with checked module, signature and Body ownership.</summary>
public static class TrRmMethodDefTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmMethodDefRow> rows,
        TrRmStringTableReader strings, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types, TrRmMethodSigTableReader signatures,
        int contractCount, int bodyCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 28];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 28;
            Write(result, offset, row.OwnerTypeDefRow);
            Write(result, offset + 4, row.NameStringRow);
            Write(result, offset + 8, row.SignatureRow);
            Write(result, offset + 12, row.Flags);
            Write(result, offset + 16, row.ContractFirst);
            Write(result, offset + 20, row.ContractCount);
            Write(result, offset + 24, row.BodyRow);
        }

        _ = new TrRmMethodDefTableReader(result, rows.Count, strings, modules, types,
            signatures, contractCount, bodyCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => Write(bytes, offset, unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
