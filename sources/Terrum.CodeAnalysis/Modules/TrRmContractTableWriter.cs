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
        var rowCount = rows.Count;
        if (rowCount is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var sectionLength = ((long)rowCount + 1) * sizeof(uint);
        var validatedRows = new TrRmContractRow[rowCount];
        for (var index = 0; index < rowCount; index++)
        {
            var row = rows[index];
            sectionLength += 8L + row.Payload.Length;
            if (sectionLength > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(rows));
            }

            validatedRows[index] = row;
        }

        var result = new byte[(int)sectionLength];
        var indexLength = (rowCount + 1) * sizeof(uint);
        var dataOffset = indexLength;
        for (var index = 0; index < validatedRows.Length; index++)
        {
            var row = validatedRows[index];
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * sizeof(uint)),
                (uint)(dataOffset - indexLength));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(dataOffset), row.OwnerDefId);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(dataOffset + 4), row.Kind);
            row.Payload.Span.CopyTo(result.AsSpan(dataOffset + 8));
            dataOffset += 8 + row.Payload.Length;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(rowCount * sizeof(uint)),
            (uint)(dataOffset - indexLength));
        _ = new TrRmContractTableReader(result, rowCount,
            types, methods, signatures, parameters, typeSpecs);
        return result;
    }
}
