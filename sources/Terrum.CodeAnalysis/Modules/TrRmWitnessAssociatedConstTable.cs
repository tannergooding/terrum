// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmWitnessAssociatedConstRow(
    int WitnessRow, uint DeclaringInterfaceTypeSpecRef,
    uint AssociatedConstDefReference, int ConstantSourceRow);

internal readonly ref struct TrRmWitnessAssociatedConstTableReader
{
    private const int RowSize = 16;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly int[] _definitionRows;

    internal int Count => _recordCount;

    internal TrRmWitnessAssociatedConstTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmInterfaceWitnessTableReader witnesses,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
        TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceBaseTableReader interfaceBases,
        TrRmAssociatedConstDefTableReader associatedConstDefs,
        TrRmImportTableReader imports,
        TrRmConstantSourceTableReader constantSources,
        TrRmArtifactSchema schema, bool deferAnchorIdentity = false)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != witnesses.AssociatedConstCount
            || (long)recordCount * RowSize != section.Length
            || !schema.UsesGeneralPredicateGraphs())
        {
            throw InvalidTable();
        }

        _rows = section;
        _recordCount = recordCount;
        _definitionRows = new int[recordCount + 1];
        for (var witness = 1; witness <= witnesses.Count; witness++)
        {
            var (first, count) = witnesses.GetAssociatedConstRange(witness);
            if (count == 0)
            {
                continue;
            }

            var closure = TrRmWitnessAssociatedTypeTableReader.GetInterfaceClosure(
                witnesses.GetInterfaceTypeSpecRef(witness), typeSpecs, typeDefs,
                interfaces, interfaceBases, imports);
            var bindingKeys = new List<(uint DeclaringType, int DefinitionRow)>();
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var row = first + ordinal;
                var data = GetRow(row);
                var witnessRow = Read(data, 0);
                var declaringInterface = Read(data, 4);
                var definitionReference = Read(data, 8);
                var constantSource = Read(data, 12);
                if (witnessRow != (uint)witness
                    || constantSource is 0 or > 0xFF_FFFF
                    || constantSource > (uint)constantSources.Count
                    || !TryGetAssociatedConstDefRow(definitionReference,
                        associatedConstDefs.Count, imports, out var definitionRow))
                {
                    throw InvalidTable();
                }

                var declaringType = typeSpecs.GetTypeSpecRef(declaringInterface);
                if (!declaringType.IsRow
                    || declaringType.Form is not (0x02 or 0x03 or 0x05))
                {
                    throw InvalidTable();
                }

                var declaringTypeDef = typeSpecs.GetNominalTypeDefRow(
                    declaringInterface, typeDefs, imports);
                var ownerInterface = associatedConstDefs.GetOwnerInterfaceDef(
                    definitionRow);
                var ownerTypeDef = interfaces.GetOwnerTypeDef(ownerInterface);
                if (declaringTypeDef != ownerTypeDef
                    || !closure.Contains(declaringTypeDef))
                {
                    throw InvalidTable();
                }
                _definitionRows[row] = definitionRow;

                foreach (var (bindingDeclaringType, bindingDefinitionRow) in bindingKeys)
                {
                    if (bindingDefinitionRow == definitionRow
                        && (bindingDeclaringType == declaringInterface
                            || (!deferAnchorIdentity
                                && typeSpecs.AreEquivalentTypeSpecRefs(
                                    bindingDeclaringType, declaringInterface))))
                    {
                        throw InvalidTable();
                    }
                }
                bindingKeys.Add((declaringInterface, definitionRow));
            }
        }
    }

    internal int GetWitnessRow(int row)
        => checked((int)Read(GetRow(row), 0));

    internal uint GetDeclaringInterfaceTypeSpecRef(int row)
        => Read(GetRow(row), 4);

    internal uint GetAssociatedConstDefReference(int row)
        => Read(GetRow(row), 8);

    internal int GetConstantSourceRow(int row)
        => checked((int)Read(GetRow(row), 12));

    internal void ValidateAnchorIdentity(
        TrRmNativeFunctionPointerAnchorTableReader anchors)
    {
        var bindingTypes = new Dictionary<(int Witness, int DefinitionRow),
            List<uint>>();
        for (var row = 1; row <= _recordCount; row++)
        {
            var key = (GetWitnessRow(row), _definitionRows[row]);
            if (!bindingTypes.TryGetValue(key, out var declaringTypes))
            {
                declaringTypes = [];
                bindingTypes.Add(key, declaringTypes);
            }

            var declaringType = GetDeclaringInterfaceTypeSpecRef(row);
            foreach (var previousType in declaringTypes)
            {
                if (anchors.AreEquivalentTypeSpecRefs(previousType,
                    declaringType))
                {
                    throw InvalidTable();
                }
            }
            declaringTypes.Add(declaringType);
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

    private static bool TryGetAssociatedConstDefRow(uint reference,
        int associatedConstDefCount, TrRmImportTableReader imports,
        out int row)
    {
        var kind = reference >> 24;
        var targetRow = checked((int)(reference & 0x00FF_FFFF));
        if (kind == 0x25 && targetRow > 0
            && targetRow <= associatedConstDefCount)
        {
            row = targetRow;
            return true;
        }
        if (kind == 0x1C && targetRow > 0 && targetRow <= imports.Count)
        {
            var expected = imports.GetExpectedDefinitionForImportRow(targetRow);
            var expectedRow = checked((int)(expected & 0x00FF_FFFF));
            if ((expected >> 24) == 0x25 && expectedRow > 0
                && expectedRow <= associatedConstDefCount)
            {
                row = expectedRow;
                return true;
            }
        }

        row = 0;
        return false;
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidTable()
        => new("Invalid TRRM WitnessAssociatedConst table.");
}

internal static class TrRmWitnessAssociatedConstTableWriter
{
    private const int RowSize = 16;

    internal static byte[] Encode(
        IReadOnlyList<TrRmWitnessAssociatedConstRow> rows,
        TrRmInterfaceWitnessTableReader witnesses,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
        TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceBaseTableReader interfaceBases,
        TrRmAssociatedConstDefTableReader associatedConstDefs,
        TrRmImportTableReader imports,
        TrRmConstantSourceTableReader constantSources,
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
            Write(result, offset, unchecked((uint)row.WitnessRow));
            Write(result, offset + 4, row.DeclaringInterfaceTypeSpecRef);
            Write(result, offset + 8, row.AssociatedConstDefReference);
            Write(result, offset + 12, unchecked((uint)row.ConstantSourceRow));
        }

        _ = new TrRmWitnessAssociatedConstTableReader(result, rows.Count,
            witnesses, typeSpecs, typeDefs, interfaces, interfaceBases,
            associatedConstDefs, imports, constantSources, schema);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
