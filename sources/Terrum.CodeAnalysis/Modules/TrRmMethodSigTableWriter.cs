// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmMethodSigRow(
    int ResultTypeSpecRow, int ReceiverTypeSpecRow,
    int ParamFirst, int ParamCount, int GenericFirst, int GenericCount);

/// <summary>Emits MethodSig rows with checked type references and disjoint Param ranges.</summary>
public static class TrRmMethodSigTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmMethodSigRow> rows,
        TrRmTypeSpecTableReader types, int paramCount, int genericParamCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 24];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 24;
            Write(result, offset, row.ResultTypeSpecRow);
            Write(result, offset + 4, row.ReceiverTypeSpecRow);
            Write(result, offset + 8, row.ParamFirst);
            Write(result, offset + 12, row.ParamCount);
            Write(result, offset + 16, row.GenericFirst);
            Write(result, offset + 20, row.GenericCount);
        }

        _ = new TrRmMethodSigTableReader(result, rows.Count, types, paramCount, genericParamCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), unchecked((uint)value));
}
