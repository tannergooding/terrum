// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmContractRow(uint OwnerDefId, ushort Kind, ReadOnlyMemory<byte> Payload);

/// <summary>Emits assigned clause shapes without establishing their executable promises.</summary>
public static class TrRmContractTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmContractRow> rows,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var sectionLength = ((long)rows.Count + 1) * sizeof(uint);
        foreach (var row in rows)
        {
            sectionLength += 8L + row.Payload.Length;
            if (sectionLength > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(rows));
            }
        }

        var records = new ReadOnlyMemory<byte>[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var data = new byte[8 + row.Payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(data, row.OwnerDefId);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), row.Kind);
            row.Payload.Span.CopyTo(data.AsSpan(8));
            records[index] = data;
        }

        var result = TrRmVariableTableWriter.EncodeContracts(records);
        _ = new TrRmContractTableReader(result, rows.Count,
            types, methods, signatures, parameters, typeSpecs);
        return result;
    }
}
