// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmImmutableDataRow(
    byte Kind, uint DeclarationDefId, uint OwnerTypeSpecRef);

internal readonly ref struct TrRmImmutableDataTableReader
{
    private const int RowSize = 12;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmImmutableDataTableReader(ReadOnlySpan<byte> section,
        int recordCount, int interfaceConstDefCount,
        int witnessAssociatedConstCount, int staticFieldCount,
        TrRmTypeSpecTableReader typeSpecs, TrRmArtifactSchema schema)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || interfaceConstDefCount is < 0 or > 0xFF_FFFF
            || witnessAssociatedConstCount is < 0 or > 0xFF_FFFF
            || staticFieldCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length
            || !schema.UsesGeneralPredicateGraphs())
        {
            throw InvalidTable();
        }

        _rows = section;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var kind = data[0];
            var definition = Read(data, 4);
            var ownerType = Read(data, 8);
            var targetRow = definition & 0x00FF_FFFF;
            var validDefinition = kind switch
            {
                1 => (definition >> 24) switch
                {
                    0x22 => IsInRange(targetRow, interfaceConstDefCount),
                    0x23 => IsInRange(targetRow, witnessAssociatedConstCount),
                    _ => false,
                },
                2 => (definition >> 24) == 0x24
                    && IsInRange(targetRow, staticFieldCount),
                _ => false,
            };
            if (data[1] != 0 || ReadU16(data, 2) != 0
                || !validDefinition)
            {
                throw InvalidTable();
            }

            if (ownerType != 0)
            {
                _ = typeSpecs.GetTypeSpecRef(ownerType);
            }
        }
    }

    internal byte GetKind(int row) => GetRow(row)[0];

    internal uint GetDeclarationDefId(int row)
        => Read(GetRow(row), 4);

    internal uint GetOwnerTypeSpecRef(int row)
        => Read(GetRow(row), 8);

    internal void ValidateOwnerBindings(
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
        TrRmImportTableReader imports,
        TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceConstDefTableReader interfaceConstDefs,
        TrRmInterfaceWitnessTableReader witnesses,
        TrRmWitnessAssociatedConstTableReader witnessAssociatedConsts,
        TrRmStaticFieldTableReader staticFields,
        TrRmStaticFieldContractTableReader staticFieldContracts,
        TrRmStaticImmutablePublicationTableReader staticPublications,
        TrRmStaticDependencyTableReader staticDependencies)
    {
        for (var row = 1; row <= _recordCount; row++)
        {
            var kind = GetKind(row);
            var definition = GetDeclarationDefId(row);
            var definitionRow = checked((int)(definition & 0x00FF_FFFF));
            var expectedOwnerTypeSpec = 0u;
            var expectedOwnerType = kind switch
            {
                1 when (definition >> 24) == 0x22
                    => interfaces.GetOwnerTypeDef(
                        interfaceConstDefs.GetOwnerInterfaceDef(definitionRow)),
                1 when (definition >> 24) == 0x23
                    => GetWitnessOwnerType(witnesses, witnessAssociatedConsts,
                        typeSpecs, typeDefs, imports, definitionRow,
                        out expectedOwnerTypeSpec),
                2 when (definition >> 24) == 0x24
                    => GetStaticFieldOwnerType(definitionRow,
                        GetOwnerTypeSpecRef(row), typeSpecs, staticFields,
                        staticFieldContracts, staticPublications,
                        staticDependencies),
                _ => throw InvalidTable(),
            };
            ValidateOwnerSubstitution(GetOwnerTypeSpecRef(row),
                expectedOwnerType, expectedOwnerTypeSpec,
                typeSpecs, typeDefs, imports);
        }
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidTable();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool IsInRange(uint row, int count)
        => row != 0 && row <= (uint)count;

    private static int GetWitnessOwnerType(
        TrRmInterfaceWitnessTableReader witnesses,
        TrRmWitnessAssociatedConstTableReader witnessAssociatedConsts,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
        TrRmImportTableReader imports, int bindingRow,
        out uint ownerTypeSpecRef)
    {
        var witness = witnessAssociatedConsts.GetWitnessRow(bindingRow);
        ownerTypeSpecRef = witnesses.GetImplementingTypeSpecRef(witness);
        return typeSpecs.GetNominalTypeDefRow(
            ownerTypeSpecRef, typeDefs, imports);
    }

    private static int GetStaticFieldOwnerType(int fieldRow,
        uint ownerTypeSpecRef, TrRmTypeSpecTableReader typeSpecs,
        TrRmStaticFieldTableReader staticFields,
        TrRmStaticFieldContractTableReader staticFieldContracts,
        TrRmStaticImmutablePublicationTableReader staticPublications,
        TrRmStaticDependencyTableReader staticDependencies)
    {
        var contract = staticFieldContracts.GetRowForStaticField(fieldRow);
        var role = staticFieldContracts.GetDeclarationRole(contract);
        var publication = staticFieldContracts.GetPublicationRow(contract);
        if (role is not (1 or 2) || publication == 0
            || staticPublications.GetPublicationRowForStaticField(fieldRow)
                != publication)
        {
            throw InvalidTable();
        }

        if (role == 2)
        {
            var dependency = staticDependencies.FindDependencyForFieldOwner(
                fieldRow, ownerTypeSpecRef, typeSpecs);
            if (staticDependencies.GetExpectedPublicationRow(dependency)
                != publication)
            {
                throw InvalidTable();
            }
        }

        return staticFields.GetOwnerTypeDefRow(fieldRow);
    }

    private static void ValidateOwnerSubstitution(uint ownerTypeSpecRef,
        int ownerTypeDef, uint expectedOwnerTypeSpecRef,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs, TrRmImportTableReader imports)
    {
        var (_, formalCount) = typeDefs.GetGenericRange(ownerTypeDef);
        if (formalCount == 0)
        {
            if (ownerTypeSpecRef != 0)
            {
                throw InvalidTable();
            }

            return;
        }

        if (ownerTypeSpecRef == 0)
        {
            throw InvalidTable();
        }

        var ownerType = typeSpecs.GetTypeSpecRef(ownerTypeSpecRef);
        if (!ownerType.IsRow || ownerType.Form != 0x05
            || typeSpecs.GetNominalTypeDefRow(
                ownerTypeSpecRef, typeDefs, imports) != ownerTypeDef
            || typeSpecs.GetConstructedArgumentCount(ownerType.Row) != formalCount
            || (expectedOwnerTypeSpecRef != 0
                && !typeSpecs.AreEquivalentTypeSpecRefs(
                    ownerTypeSpecRef, expectedOwnerTypeSpecRef)))
        {
            throw InvalidTable();
        }
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, sizeof(ushort)));

    private static InvalidDataException InvalidTable()
        => new("Invalid TRRM ImmutableData table.");
}

internal static class TrRmImmutableDataTableWriter
{
    private const int RowSize = 12;

    internal static byte[] Encode(IReadOnlyList<TrRmImmutableDataRow> rows,
        int interfaceConstDefCount, int witnessAssociatedConstCount,
        int staticFieldCount, TrRmTypeSpecTableReader typeSpecs,
        TrRmArtifactSchema schema)
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
            result[offset] = row.Kind;
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(offset + 4), row.DeclarationDefId);
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(offset + 8), row.OwnerTypeSpecRef);
        }

        _ = new TrRmImmutableDataTableReader(result, rows.Count,
            interfaceConstDefCount, witnessAssociatedConstCount,
            staticFieldCount, typeSpecs, schema);
        return result;
    }
}
