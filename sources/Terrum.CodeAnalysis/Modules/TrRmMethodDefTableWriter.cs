// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Describes one MethodDef row and its owned metadata ranges.</summary>
/// <param name="OwnerTypeDefRow">The owning TypeDef row, or zero for a module-level method.</param>
/// <param name="NameStringRow">The String row containing the method name.</param>
/// <param name="SignatureRow">The one-based MethodSig row.</param>
/// <param name="Flags">The MethodDef flags.</param>
/// <param name="ContractFirst">The first owned Contract row, or zero when there are none.</param>
/// <param name="ContractCount">The number of owned Contract rows.</param>
/// <param name="BodyRow">The one-based Body row, or zero when the method has no body.</param>
public readonly record struct TrRmMethodDefRow(
    int OwnerTypeDefRow, int NameStringRow, int SignatureRow, uint Flags,
    int ContractFirst, int ContractCount, int BodyRow);

/// <summary>Emits MethodDef rows with checked module, signature and Body ownership.</summary>
public static class TrRmMethodDefTableWriter
{
    /// <summary>Encodes MethodDef rows and validates their ownership and references.</summary>
    /// <param name="rows">The MethodDef rows in table order.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="modules">The validated Module table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    /// <param name="contractCount">The total number of Contract rows.</param>
    /// <param name="bodyCount">The total number of Body rows.</param>
    /// <returns>The encoded MethodDef section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmMethodDefRow> rows,
        TrRmStringTableReader strings, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types, TrRmMethodSigTableReader signatures,
        int contractCount, int bodyCount)
        => Encode(rows, strings, modules, types, signatures, contractCount,
            bodyCount, null);

    internal static byte[] Encode(IReadOnlyList<TrRmMethodDefRow> rows,
        TrRmStringTableReader strings, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types, TrRmMethodSigTableReader signatures,
        int contractCount, int bodyCount,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        resourceLedger?.ConsumeWork(checked(1UL
            + ((ulong)rows.Count
                * (1UL + 7UL + (7UL * sizeof(uint))))));
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
            signatures, contractCount, bodyCount, resourceLedger);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => Write(bytes, offset, unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
