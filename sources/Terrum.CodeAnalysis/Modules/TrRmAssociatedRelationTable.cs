// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A typed relation between an interface-associated type and a required type.</summary>
/// <param name="DeclaringInterfaceTypeSpecRef">The complete declaring interface TypeSpecRef.</param>
/// <param name="AssociatedTypeDefReference">The direct or imported AssociatedTypeDef identity.</param>
/// <param name="RequiredTypeSpecRef">The complete required TypeSpecRef.</param>
public readonly record struct TrRmAssociatedRelationRow(
    uint DeclaringInterfaceTypeSpecRef,
    uint AssociatedTypeDefReference,
    uint RequiredTypeSpecRef);

/// <summary>Validates associated-relation type references and declaration identities.</summary>
public readonly ref struct TrRmAssociatedRelationTableReader
{
    private const int RowSize = 12;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated AssociatedRelation rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates complete TypeSpecRefs and AssociatedTypeReference namespaces.</summary>
    /// <param name="section">The borrowed 12-byte AssociatedRelation rows.</param>
    /// <param name="recordCount">The number of AssociatedRelation rows.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="associatedTypeDefCount">The number of AssociatedTypeDef rows.</param>
    /// <param name="imports">The validated Import table.</param>
    public TrRmAssociatedRelationTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmTypeSpecTableReader types,
        int associatedTypeDefCount, TrRmImportTableReader imports)
    {
        if (!ValidCount(recordCount) || !ValidCount(associatedTypeDefCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidAssociatedRelation();
        }

        _rows = section;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var declaringInterface = Read(data, 0);
            var associatedTypeDef = Read(data, 4);
            var requiredType = Read(data, 8);
            if (declaringInterface == 0 || requiredType == 0
                || !ValidAssociatedTypeReference(associatedTypeDef,
                    associatedTypeDefCount, imports))
            {
                throw InvalidAssociatedRelation();
            }

            if (!types.GetTypeSpecRef(declaringInterface).IsRow)
            {
                throw InvalidAssociatedRelation();
            }

            _ = types.GetTypeSpecRef(requiredType);
        }
    }

    /// <summary>Gets the complete declaring-interface TypeSpecRef.</summary>
    public uint GetDeclaringInterfaceTypeSpecRef(int row)
        => Read(GetRow(row), 0);

    /// <summary>Gets the direct or imported AssociatedTypeDef identity.</summary>
    public uint GetAssociatedTypeDefReference(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the complete required TypeSpecRef.</summary>
    public uint GetRequiredTypeSpecRef(int row)
        => Read(GetRow(row), 8);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidAssociatedRelation();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool ValidAssociatedTypeReference(uint reference,
        int associatedTypeDefCount, TrRmImportTableReader imports)
    {
        var row = (int)(reference & 0xFF_FFFF);
        if (row <= 0)
        {
            return false;
        }

        return (reference >> 24) switch
        {
            0x20 => row <= associatedTypeDefCount,
            0x1C => row <= imports.Count
                && (imports.GetExpectedDefinitionForImportRow(row) >> 24) == 0x20,
            _ => false,
        };
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static InvalidDataException InvalidAssociatedRelation()
        => new("Invalid TRRM AssociatedRelation table.");
}

/// <summary>Encodes associated-relation rows with checked type and declaration references.</summary>
public static class TrRmAssociatedRelationTableWriter
{
    private const int RowSize = 12;

    /// <summary>Encodes AssociatedRelation rows in one-based table order.</summary>
    /// <param name="rows">The exact interface-associated type relations.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="associatedTypeDefCount">The number of AssociatedTypeDef rows.</param>
    /// <param name="imports">The validated Import table.</param>
    /// <returns>The encoded AssociatedRelation section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmAssociatedRelationRow> rows,
        TrRmTypeSpecTableReader types, int associatedTypeDefCount,
        TrRmImportTableReader imports)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * RowSize];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * RowSize;
            Write(result, offset, row.DeclaringInterfaceTypeSpecRef);
            Write(result, offset + 4, row.AssociatedTypeDefReference);
            Write(result, offset + 8, row.RequiredTypeSpecRef);
        }

        _ = new TrRmAssociatedRelationTableReader(
            result, rows.Count, types, associatedTypeDefCount, imports);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
