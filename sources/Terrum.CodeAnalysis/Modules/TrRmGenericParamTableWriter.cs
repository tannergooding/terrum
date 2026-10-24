// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A registry-shaped generic formal and its owner ranges and domains.</summary>
/// <param name="OwnerDefId">The TypeDef or MethodDef DefId.</param>
/// <param name="Ordinal">The zero-based formal ordinal.</param>
/// <param name="NameStringRow">The one-based formal-name String row.</param>
/// <param name="ConstraintFirst">The first owned Constraint row, or zero when empty.</param>
/// <param name="ConstraintCount">The number of owned Constraint rows.</param>
/// <param name="FormalKind">The assigned type or constant formal kind.</param>
/// <param name="OwnershipDomain">The type-formal ownership domain, or zero for a constant formal.</param>
/// <param name="NullabilityDomain">The type-formal nullability domain, or zero for a constant formal.</param>
/// <param name="ConstantTypeSpecRef">The constant formal's TypeSpecRef, or zero for a type formal.</param>
/// <param name="DefaultArgumentRef">The type or constant default reference, or zero when absent.</param>
public readonly record struct TrRmGenericParamRow(
    uint OwnerDefId, int Ordinal, int NameStringRow, int ConstraintFirst, int ConstraintCount,
    byte FormalKind, byte OwnershipDomain, byte NullabilityDomain,
    uint ConstantTypeSpecRef, uint DefaultArgumentRef);

/// <summary>Emits formal rows with checked type/method ownership, ordinals and constraint ranges.</summary>
public static class TrRmGenericParamTableWriter
{
    /// <summary>Encodes GenericParam rows and validates ownership, domains and Constraint ranges.</summary>
    /// <param name="rows">The GenericParam rows in one-based table order.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    /// <param name="constraintCount">The number of Constraint rows.</param>
    /// <returns>The encoded GenericParam section.</returns>
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

        var result = new byte[rows.Count * 32];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 32;
            Write(result, offset, row.OwnerDefId);
            Write(result, offset + 4, unchecked((uint)row.Ordinal));
            Write(result, offset + 8, unchecked((uint)row.NameStringRow));
            Write(result, offset + 12, unchecked((uint)row.ConstraintFirst));
            Write(result, offset + 16, unchecked((uint)row.ConstraintCount));
            result[offset + 20] = row.FormalKind;
            result[offset + 21] = row.OwnershipDomain;
            result[offset + 22] = row.NullabilityDomain;
            Write(result, offset + 24, row.ConstantTypeSpecRef);
            Write(result, offset + 28, row.DefaultArgumentRef);
        }

        _ = new TrRmGenericParamTableReader(result, rows.Count,
            strings, types, methods, signatures, constraintCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
