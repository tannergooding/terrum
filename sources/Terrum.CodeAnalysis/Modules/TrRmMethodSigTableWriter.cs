// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Describes one MethodSig row and its parameter ranges.</summary>
/// <param name="ResultTypeSpecRef">The result TypeSpec reference.</param>
/// <param name="ReceiverTypeSpecRef">The receiver TypeSpec reference, or zero when absent.</param>
/// <param name="ParamFirst">The first one-based Param row, or zero when there are no parameters.</param>
/// <param name="ParamCount">The number of Param rows.</param>
/// <param name="GenericFirst">The first one-based generic parameter row, or zero when there are none.</param>
/// <param name="GenericCount">The number of generic parameter rows.</param>
public readonly record struct TrRmMethodSigRow(
    uint ResultTypeSpecRef, uint ReceiverTypeSpecRef,
    int ParamFirst, int ParamCount, int GenericFirst, int GenericCount);

/// <summary>Emits MethodSig rows with checked type references and disjoint Param ranges.</summary>
public static class TrRmMethodSigTableWriter
{
    /// <summary>Encodes MethodSig rows and validates type references and owned ranges.</summary>
    /// <param name="rows">The MethodSig rows in table order.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="paramCount">The total number of Param rows.</param>
    /// <param name="genericParamCount">The total number of generic parameter rows.</param>
    /// <returns>The encoded MethodSig section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmMethodSigRow> rows,
        TrRmTypeSpecTableReader types, int paramCount, int genericParamCount)
        => Encode(rows, types, paramCount, genericParamCount, null);

    internal static byte[] Encode(IReadOnlyList<TrRmMethodSigRow> rows,
        TrRmTypeSpecTableReader types, int paramCount, int genericParamCount,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        resourceLedger?.ConsumeWork(checked(1UL
            + ((ulong)rows.Count
                * (1UL + 6UL + (6UL * sizeof(uint))))));
        var result = new byte[rows.Count * 24];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 24;
            Write(result, offset, row.ResultTypeSpecRef);
            Write(result, offset + 4, row.ReceiverTypeSpecRef);
            Write(result, offset + 8, row.ParamFirst);
            Write(result, offset + 12, row.ParamCount);
            Write(result, offset + 16, row.GenericFirst);
            Write(result, offset + 20, row.GenericCount);
        }

        _ = new TrRmMethodSigTableReader(result, rows.Count, types,
            paramCount, genericParamCount, resourceLedger);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
