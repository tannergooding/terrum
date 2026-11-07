// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Describes a property and its getter MethodDef.</summary>
/// <param name="OwnerTypeDefRow">The owning TypeDef row.</param>
/// <param name="NameStringRow">The String row containing the property name.</param>
/// <param name="TypeSpecRef">The property's TypeSpec reference.</param>
/// <param name="GetterMethodDefRow">The one-based getter MethodDef row.</param>
/// <param name="Flags">The PropertyDef flags.</param>
public readonly record struct TrRmPropertyDefRow(
    int OwnerTypeDefRow, int NameStringRow, uint TypeSpecRef, int GetterMethodDefRow, uint Flags);

/// <summary>Emits PropertyDef rows with checked getter owner and result type.</summary>
public static class TrRmPropertyDefTableWriter
{
    /// <summary>Encodes PropertyDef rows and validates their getter references.</summary>
    /// <param name="rows">The PropertyDef rows in table order.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="typeSpecs">The validated TypeSpec table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    /// <returns>The encoded PropertyDef section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmPropertyDefRow> rows,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures)
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
            Write(result, offset, row.OwnerTypeDefRow);
            Write(result, offset + 4, row.NameStringRow);
            Write(result, offset + 8, row.TypeSpecRef);
            Write(result, offset + 12, row.GetterMethodDefRow);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 16), row.Flags);
        }

        _ = new TrRmPropertyDefTableReader(result, rows.Count,
            strings, types, typeSpecs, methods, signatures);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
