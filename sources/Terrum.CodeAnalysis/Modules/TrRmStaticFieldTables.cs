// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmStaticFieldRow(
    int OwnerTypeDefRow, int NameStringRow, uint TypeSpecRef,
    uint Flags, int EagerGroupRow);

internal readonly record struct TrRmStaticFieldContractRow(
    int StaticFieldRow, byte DeclarationRole, byte Visibility,
    byte CallerUnsafe, uint DeclarationOrdinal,
    int LazyUnitRow, int PublicationRow);

internal readonly record struct TrRmEagerGroupRow(
    int OwnerTypeDefRow, byte TriggerTag, byte ExecutionPolicy,
    int InitializerFirst, int InitializerCount, int ConstructorMethodDefRow);

internal readonly record struct TrRmEagerInitializerRow(
    int EagerGroupRow, uint Ordinal, int StaticFieldRow,
    int InitializerMethodDefRow);

internal readonly record struct TrRmLazyUnitRow(
    int StaticFieldRow, int InitializerMethodDefRow);

internal readonly record struct TrRmStaticAccessRow(
    byte TargetKind, byte AuthorityKind, uint TargetRow, uint OwnerTypeSpecRef);

internal readonly record struct TrRmStaticDependencyRow(
    int FieldImportRow, uint OwnerTypeSpecRef,
    int ExpectedEagerGroupRow, int ExpectedPublicationRow);

internal readonly record struct TrRmStaticImmutablePublicationRow(
    int StaticFieldRow, byte BackingKind, byte CompletionKind,
    byte LifetimeKind, byte SharingKind, uint ExtentTypeSpecRef,
    ulong ValueCount, int ConstantSourceRow);

internal readonly ref struct TrRmStaticFieldTableReader
{
    private const int RowSize = 20;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmStaticFieldTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmStringTableReader strings,
        TrRmTypeDefTableReader typeDefs, TrRmTypeSpecTableReader typeSpecs,
        TrRmEagerGroupTableReader eagerGroups, TrRmArtifactSchema schema)
    {
        if (!ValidCount(recordCount) || (long)recordCount * RowSize != section.Length
            || !schema.UsesGeneralPredicateGraphs())
        {
            throw InvalidTable();
        }

        _rows = section;
        _recordCount = recordCount;
        var groupsWithFields = new byte[eagerGroups.Count + 1];
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var owner = Read(data, 0);
            var name = Read(data, 4);
            var type = Read(data, 8);
            var flags = Read(data, 12);
            var group = Read(data, 16);
            if (!IsRowReference(owner, typeDefs.Count)
                || !IsRowReference(name, strings.Count)
                || type == 0 || !IsRowReference(group, eagerGroups.Count)
                || flags > 1
                || eagerGroups.GetOwnerTypeDefRow(checked((int)group))
                    != checked((int)owner))
            {
                throw InvalidTable();
            }

            if (string.IsNullOrEmpty(strings.DecodeName(checked((int)name))))
            {
                throw InvalidTable();
            }
            _ = typeSpecs.GetTypeSpecRef(type);
            groupsWithFields[group] = 1;
        }

        for (var group = 1; group <= eagerGroups.Count; group++)
        {
            if (groupsWithFields[group] == 0
                && eagerGroups.GetConstructorMethodDefRow(group) == 0)
            {
                throw InvalidTable();
            }
        }
    }

    internal int GetOwnerTypeDefRow(int row)
        => checked((int)Read(GetRow(row), 0));

    internal int GetNameStringRow(int row)
        => checked((int)Read(GetRow(row), 4));

    internal uint GetTypeSpecRef(int row) => Read(GetRow(row), 8);
    internal uint GetFlags(int row) => Read(GetRow(row), 12);
    internal int GetEagerGroupRow(int row)
        => checked((int)Read(GetRow(row), 16));

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidTable();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    internal static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    internal static bool IsRowReference(uint row, int count)
        => row is > 0 and <= 0xFF_FFFF && row <= (uint)count;

    internal static bool IsValidRange(uint first, uint count, int total)
        => count == 0 ? first == 0
            : first != 0 && first <= (uint)total
                && count <= (uint)total - first + 1;

    internal static void ValidateOwnerSubstitution(uint ownerTypeSpecRef,
        int ownerTypeDef, TrRmTypeSpecTableReader typeSpecs,
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
            || typeSpecs.GetConstructedArgumentCount(ownerType.Row) != formalCount)
        {
            throw InvalidTable();
        }
    }

    internal static int GetOwnerSubstitutionHash(uint ownerTypeSpecRef,
        TrRmTypeSpecTableReader typeSpecs)
    {
        if (ownerTypeSpecRef == 0)
        {
            return 0;
        }

        var ownerType = typeSpecs.GetTypeSpecRef(ownerTypeSpecRef);
        var hash = new HashCode();
        hash.Add(ownerType.Form);
        hash.Add(ownerType.Qualifiers);
        foreach (var value in typeSpecs.GetRow(ownerType.Row))
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }

    internal static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    internal static ushort ReadU16(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, sizeof(ushort)));

    internal static void Write(byte[] data, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);

    internal static InvalidDataException InvalidTable()
        => new("Invalid TRRM StaticField table.");
}

internal readonly ref struct TrRmEagerGroupTableReader
{
    private const int RowSize = 20;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmEagerGroupTableReader(ReadOnlySpan<byte> section,
        int recordCount, int typeDefCount, int initializerCount,
        int methodDefCount, TrRmArtifactSchema schema)
    {
        if (!TrRmStaticFieldTableReader.ValidCount(recordCount)
            || !TrRmStaticFieldTableReader.ValidCount(typeDefCount)
            || !TrRmStaticFieldTableReader.ValidCount(initializerCount)
            || !TrRmStaticFieldTableReader.ValidCount(methodDefCount)
            || (long)recordCount * RowSize != section.Length
            || !schema.UsesGeneralPredicateGraphs())
        {
            throw TrRmStaticFieldTableReader.InvalidTable();
        }

        _rows = section;
        _recordCount = recordCount;
        var owners = new HashSet<int>();
        var constructors = new HashSet<int>();
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var owner = TrRmStaticFieldTableReader.Read(data, 0);
            var trigger = data[4];
            var policy = data[5];
            var first = TrRmStaticFieldTableReader.Read(data, 8);
            var count = TrRmStaticFieldTableReader.Read(data, 12);
            var constructor = TrRmStaticFieldTableReader.Read(data, 16);
            if (!TrRmStaticFieldTableReader.IsRowReference(owner, typeDefCount)
                || trigger is not (1 or 2) || policy != 1
                || TrRmStaticFieldTableReader.ReadU16(data, 6) != 0
                || !TrRmStaticFieldTableReader.IsValidRange(
                    first, count, initializerCount)
                || (constructor != 0
                    && !TrRmStaticFieldTableReader.IsRowReference(
                        constructor, methodDefCount))
                || (constructor == 0 && trigger != 1)
                || !owners.Add(checked((int)owner))
                || (constructor != 0
                    && !constructors.Add(checked((int)constructor))))
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }
        }
    }

    internal int GetOwnerTypeDefRow(int row)
        => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 0));

    internal byte GetTriggerTag(int row) => GetRow(row)[4];
    internal byte GetExecutionPolicy(int row) => GetRow(row)[5];

    internal (int First, int Count) GetInitializerRange(int row)
    {
        var data = GetRow(row);
        return (
            checked((int)TrRmStaticFieldTableReader.Read(data, 8)),
            checked((int)TrRmStaticFieldTableReader.Read(data, 12)));
    }

    internal int GetConstructorMethodDefRow(int row)
        => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 16));

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmStaticFieldTableReader.InvalidTable();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

internal readonly ref struct TrRmStaticFieldContractTableReader
{
    private const int RowSize = 20;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly int[] _contractRowsByField;

    internal int Count => _recordCount;

    internal TrRmStaticFieldContractTableReader(ReadOnlySpan<byte> section,
        int recordCount, int staticFieldCount, int lazyUnitCount,
        int publicationCount, TrRmStaticFieldTableReader staticFields,
        TrRmArtifactSchema schema)
    {
        if (!TrRmStaticFieldTableReader.ValidCount(recordCount)
            || !TrRmStaticFieldTableReader.ValidCount(staticFieldCount)
            || !TrRmStaticFieldTableReader.ValidCount(lazyUnitCount)
            || !TrRmStaticFieldTableReader.ValidCount(publicationCount)
            || recordCount != staticFieldCount
            || (long)recordCount * RowSize != section.Length
            || !schema.UsesGeneralPredicateGraphs())
        {
            throw TrRmStaticFieldTableReader.InvalidTable();
        }

        _rows = section;
        _recordCount = recordCount;
        var contractRowsByField = new int[staticFieldCount + 1];
        var ordinalsByOwner = new Dictionary<int, HashSet<uint>>();
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var field = TrRmStaticFieldTableReader.Read(data, 0);
            var role = data[4];
            var visibility = data[5];
            var callerUnsafe = data[6];
            var ordinal = TrRmStaticFieldTableReader.Read(data, 8);
            var lazyUnit = TrRmStaticFieldTableReader.Read(data, 12);
            var publication = TrRmStaticFieldTableReader.Read(data, 16);
            if (!TrRmStaticFieldTableReader.IsRowReference(field, staticFieldCount)
                || role is not (1 or 2 or 3) || visibility > 2
                || callerUnsafe > 1 || data[7] != 0
                || (lazyUnit != 0
                    && !TrRmStaticFieldTableReader.IsRowReference(
                        lazyUnit, lazyUnitCount))
                || (publication != 0
                    && !TrRmStaticFieldTableReader.IsRowReference(
                        publication, publicationCount))
                || contractRowsByField[field] != 0)
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }

            contractRowsByField[field] = row;
            var owner = staticFields.GetOwnerTypeDefRow(checked((int)field));
            if (!ordinalsByOwner.TryGetValue(owner, out var ownerOrdinals))
            {
                ownerOrdinals = [];
                ordinalsByOwner.Add(owner, ownerOrdinals);
            }
            if (!ownerOrdinals.Add(ordinal))
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }
        }

        for (var field = 1; field <= staticFieldCount; field++)
        {
            if (contractRowsByField[field] == 0)
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }
        }

        foreach (var ownerOrdinals in ordinalsByOwner.Values)
        {
            uint maximum = 0;
            foreach (var ordinal in ownerOrdinals)
            {
                maximum = Math.Max(maximum, ordinal);
            }
            if ((ulong)maximum + 1 != (ulong)ownerOrdinals.Count)
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }
        }

        _contractRowsByField = contractRowsByField;
    }

    internal int GetStaticFieldRow(int row)
        => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 0));

    internal byte GetDeclarationRole(int row) => GetRow(row)[4];
    internal byte GetVisibility(int row) => GetRow(row)[5];
    internal byte GetCallerUnsafe(int row) => GetRow(row)[6];

    internal uint GetDeclarationOrdinal(int row)
        => TrRmStaticFieldTableReader.Read(GetRow(row), 8);

    internal int GetLazyUnitRow(int row)
        => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 12));

    internal int GetPublicationRow(int row)
        => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 16));

    internal int GetRowForStaticField(int staticFieldRow)
    {
        if (staticFieldRow <= 0 || staticFieldRow >= _contractRowsByField.Length
            || _contractRowsByField[staticFieldRow] == 0)
        {
            throw TrRmStaticFieldTableReader.InvalidTable();
        }

        return _contractRowsByField[staticFieldRow];
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmStaticFieldTableReader.InvalidTable();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

internal readonly ref struct TrRmEagerInitializerTableReader
{
    private const int RowSize = 16;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmEagerInitializerTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmEagerGroupTableReader groups,
        TrRmStaticFieldTableReader fields,
        TrRmStaticFieldContractTableReader fieldContracts,
        int methodDefCount, TrRmArtifactSchema schema)
    {
        if (!TrRmStaticFieldTableReader.ValidCount(recordCount)
            || !TrRmStaticFieldTableReader.ValidCount(methodDefCount)
            || (long)recordCount * RowSize != section.Length
            || !schema.UsesGeneralPredicateGraphs())
        {
            throw TrRmStaticFieldTableReader.InvalidTable();
        }

        _rows = section;
        _recordCount = recordCount;
        var rowsOwned = new byte[recordCount + 1];
        var methods = new HashSet<int>();
        for (var group = 1; group <= groups.Count; group++)
        {
            var constructor = groups.GetConstructorMethodDefRow(group);
            if (constructor != 0 && !methods.Add(constructor))
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }
        }
        for (var group = 1; group <= groups.Count; group++)
        {
            var (first, count) = groups.GetInitializerRange(group);
            var ordinals = new byte[count];
            var declarationOrdinals = new uint[count];
            var fieldsInGroup = new HashSet<int>();
            for (var offset = 0; offset < count; offset++)
            {
                var row = first + offset;
                var data = GetRow(row);
                var ownerGroup = TrRmStaticFieldTableReader.Read(data, 0);
                var ordinal = TrRmStaticFieldTableReader.Read(data, 4);
                var field = TrRmStaticFieldTableReader.Read(data, 8);
                var method = TrRmStaticFieldTableReader.Read(data, 12);
                if (ownerGroup != (uint)group
                    || ordinal >= (uint)count
                    || ordinals[ordinal] != 0
                    || !TrRmStaticFieldTableReader.IsRowReference(
                        field, fields.Count)
                    || !TrRmStaticFieldTableReader.IsRowReference(
                        method, methodDefCount)
                    || fields.GetEagerGroupRow(checked((int)field)) != group
                    || fieldContracts.GetLazyUnitRow(
                        fieldContracts.GetRowForStaticField(checked((int)field))) != 0
                    || !fieldsInGroup.Add(checked((int)field))
                    || !methods.Add(checked((int)method))
                    || rowsOwned[row] != 0)
                {
                    throw TrRmStaticFieldTableReader.InvalidTable();
                }

                ordinals[ordinal] = 1;
                declarationOrdinals[ordinal] = fieldContracts.GetDeclarationOrdinal(
                    fieldContracts.GetRowForStaticField(checked((int)field)));
                rowsOwned[row] = 1;
            }

            for (var ordinal = 1; ordinal < count; ordinal++)
            {
                if (declarationOrdinals[ordinal]
                    <= declarationOrdinals[ordinal - 1])
                {
                    throw TrRmStaticFieldTableReader.InvalidTable();
                }
            }
        }

        for (var row = 1; row <= recordCount; row++)
        {
            if (rowsOwned[row] == 0)
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }
        }
    }

    internal int GetEagerGroupRow(int row)
        => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 0));

    internal uint GetOrdinal(int row)
        => TrRmStaticFieldTableReader.Read(GetRow(row), 4);

    internal int GetStaticFieldRow(int row)
        => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 8));

    internal int GetInitializerMethodDefRow(int row)
        => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 12));

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmStaticFieldTableReader.InvalidTable();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

internal readonly ref struct TrRmLazyUnitTableReader
{
    private const int RowSize = 8;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmLazyUnitTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmEagerGroupTableReader groups,
        TrRmEagerInitializerTableReader initializers,
        TrRmStaticFieldTableReader fields,
        TrRmStaticFieldContractTableReader fieldContracts,
        int methodDefCount, TrRmArtifactSchema schema)
    {
        if (!TrRmStaticFieldTableReader.ValidCount(recordCount)
            || !TrRmStaticFieldTableReader.ValidCount(methodDefCount)
            || (long)recordCount * RowSize != section.Length
            || !schema.UsesGeneralPredicateGraphs())
        {
            throw TrRmStaticFieldTableReader.InvalidTable();
        }

        _rows = section;
        _recordCount = recordCount;
        var fieldsOwned = new byte[fields.Count + 1];
        var methods = new HashSet<int>();
        for (var group = 1; group <= groups.Count; group++)
        {
            var constructor = groups.GetConstructorMethodDefRow(group);
            if (constructor != 0 && !methods.Add(constructor))
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }
        }
        for (var initializer = 1; initializer <= initializers.Count; initializer++)
        {
            if (!methods.Add(
                initializers.GetInitializerMethodDefRow(initializer)))
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }
        }
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var field = TrRmStaticFieldTableReader.Read(data, 0);
            var method = TrRmStaticFieldTableReader.Read(data, 4);
            if (!TrRmStaticFieldTableReader.IsRowReference(field, fields.Count)
                || !TrRmStaticFieldTableReader.IsRowReference(method, methodDefCount)
                || fieldsOwned[field] != 0
                || !methods.Add(checked((int)method))
                || fieldContracts.GetLazyUnitRow(
                    fieldContracts.GetRowForStaticField(checked((int)field))) != row)
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }

            fieldsOwned[field] = 1;
        }

        for (var field = 1; field <= fields.Count; field++)
        {
            var contract = fieldContracts.GetRowForStaticField(field);
            var hasLazyUnit = fieldContracts.GetLazyUnitRow(contract) != 0;
            var hasLazyUnitRow = fieldsOwned[field] != 0;
            if (hasLazyUnit != hasLazyUnitRow)
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }
        }
    }

    internal int GetStaticFieldRow(int row)
        => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 0));

    internal int GetInitializerMethodDefRow(int row)
        => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 4));

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmStaticFieldTableReader.InvalidTable();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

    internal readonly ref struct TrRmStaticImmutablePublicationTableReader
    {
        private const int RowSize = 24;

        private readonly ReadOnlySpan<byte> _rows;
        private readonly int _recordCount;
        private readonly int[] _publicationRowsByField;

        internal int Count => _recordCount;

        internal TrRmStaticImmutablePublicationTableReader(ReadOnlySpan<byte> section,
            int recordCount, TrRmStaticFieldTableReader fields,
            TrRmStaticFieldContractTableReader fieldContracts,
            TrRmTypeSpecTableReader typeSpecs, TrRmLazyUnitTableReader lazyUnits,
            TrRmConstantSourceTableReader constantSources,
            TrRmArtifactSchema schema, bool deferTypeIdentity = false)
        {
            if (!TrRmStaticFieldTableReader.ValidCount(recordCount)
                || (long)recordCount * RowSize != section.Length
                || !schema.UsesGeneralPredicateGraphs())
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }

            _rows = section;
            _recordCount = recordCount;
            var publicationRowsByField = new int[fields.Count + 1];
            for (var row = 1; row <= recordCount; row++)
            {
                var data = GetRow(row);
                var field = TrRmStaticFieldTableReader.Read(data, 0);
                var backing = data[4];
                var completion = data[5];
                var lifetime = data[6];
                var sharing = data[7];
                var extentType = TrRmStaticFieldTableReader.Read(data, 8);
                var valueCount = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(12, 8));
                var constantSource = TrRmStaticFieldTableReader.Read(data, 20);
                if (!TrRmStaticFieldTableReader.IsRowReference(field, fields.Count)
                    || backing != 1 || completion is not (1 or 2)
                    || lifetime != 1 || sharing != 1
                    || extentType == 0 || valueCount != 1
                    || (constantSource != 0
                        && !TrRmStaticFieldTableReader.IsRowReference(
                            constantSource, constantSources.Count))
                    || fields.GetFlags(checked((int)field)) != 0
                    || publicationRowsByField[field] != 0
                    || (!deferTypeIdentity
                        && !typeSpecs.AreEquivalentTypeSpecRefs(
                            fields.GetTypeSpecRef(checked((int)field)), extentType)))
                {
                    throw TrRmStaticFieldTableReader.InvalidTable();
                }
                _ = typeSpecs.GetTypeSpecRef(extentType);

                var contract = fieldContracts.GetRowForStaticField(checked((int)field));
                var lazyUnit = fieldContracts.GetLazyUnitRow(contract);
                var hasLazyUnit = lazyUnit != 0;
                var isEagerCompletion = completion == 1;
                var lazyUnitMatchesField = !hasLazyUnit
                    || lazyUnits.GetStaticFieldRow(lazyUnit) == checked((int)field);
                if (isEagerCompletion == hasLazyUnit
                    || !lazyUnitMatchesField
                    || fieldContracts.GetPublicationRow(contract) != row)
                {
                    throw TrRmStaticFieldTableReader.InvalidTable();
                }

                if (constantSource != 0
                    && constantSources.GetKind(checked((int)constantSource)) is not (1 or 3))
                {
                    throw TrRmStaticFieldTableReader.InvalidTable();
                }

                publicationRowsByField[field] = row;
            }

            for (var field = 1; field <= fields.Count; field++)
            {
                var contract = fieldContracts.GetRowForStaticField(field);
                if (fieldContracts.GetPublicationRow(contract)
                    != publicationRowsByField[field])
                {
                    throw TrRmStaticFieldTableReader.InvalidTable();
                }
            }

            _publicationRowsByField = publicationRowsByField;
        }

        internal int GetStaticFieldRow(int row)
            => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 0));
        internal byte GetBackingKind(int row) => GetRow(row)[4];
        internal byte GetCompletionKind(int row) => GetRow(row)[5];
        internal byte GetLifetimeKind(int row) => GetRow(row)[6];
        internal byte GetSharingKind(int row) => GetRow(row)[7];
        internal uint GetExtentTypeSpecRef(int row)
            => TrRmStaticFieldTableReader.Read(GetRow(row), 8);
        internal ulong GetValueCount(int row)
            => BinaryPrimitives.ReadUInt64LittleEndian(GetRow(row).Slice(12, 8));
        internal int GetConstantSourceRow(int row)
            => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 20));
        internal void ValidateTypeIdentity(TrRmStaticFieldTableReader fields,
            TrRmNativeFunctionPointerAnchorTableReader anchors)
        {
            for (var row = 1; row <= _recordCount; row++)
            {
                var field = GetStaticFieldRow(row);
                if (!anchors.AreEquivalentTypeSpecRefs(
                    fields.GetTypeSpecRef(field), GetExtentTypeSpecRef(row)))
                {
                    throw TrRmStaticFieldTableReader.InvalidTable();
                }
            }
        }
        internal int GetPublicationRowForStaticField(int fieldRow)
        {
            if (fieldRow <= 0 || fieldRow >= _publicationRowsByField.Length)
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }

            return _publicationRowsByField[fieldRow];
        }

        private ReadOnlySpan<byte> GetRow(int row)
        {
            if (row <= 0 || row > _recordCount)
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }

            return _rows.Slice((row - 1) * RowSize, RowSize);
        }
    }

    internal readonly ref struct TrRmStaticDependencyTableReader
    {
        private const int RowSize = 16;

        private readonly ReadOnlySpan<byte> _rows;
        private readonly int _recordCount;
        private readonly List<int>[] _dependencyRowsByField;

        internal int Count => _recordCount;

        internal TrRmStaticDependencyTableReader(ReadOnlySpan<byte> section,
            int recordCount, TrRmImportTableReader imports,
            TrRmStaticFieldTableReader fields,
            TrRmStaticFieldContractTableReader fieldContracts,
            TrRmEagerGroupTableReader groups, int publicationCount,
            TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
            TrRmArtifactSchema schema)
        {
            if (!TrRmStaticFieldTableReader.ValidCount(recordCount)
                || !TrRmStaticFieldTableReader.ValidCount(publicationCount)
                || (long)recordCount * RowSize != section.Length
                || !schema.UsesGeneralPredicateGraphs())
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }

            _rows = section;
            _recordCount = recordCount;
            var dependencyRowsByField = new List<int>[fields.Count + 1];
            var ownerSubstitutionsByImport
                = new Dictionary<(int Import, int Hash), List<uint>>();
            for (var row = 1; row <= recordCount; row++)
            {
                var data = GetRow(row);
                var import = TrRmStaticFieldTableReader.Read(data, 0);
                var ownerType = TrRmStaticFieldTableReader.Read(data, 4);
                var group = TrRmStaticFieldTableReader.Read(data, 8);
                var publication = TrRmStaticFieldTableReader.Read(data, 12);
                if (!TrRmStaticFieldTableReader.IsRowReference(import, imports.Count)
                    || !TrRmStaticFieldTableReader.IsRowReference(group, groups.Count)
                    || (publication != 0
                        && !TrRmStaticFieldTableReader.IsRowReference(
                            publication, publicationCount))
                    || (imports.GetExpectedDefinitionForImportRow(checked((int)import))
                        >> 24) != 0x24)
                {
                    throw TrRmStaticFieldTableReader.InvalidTable();
                }

                var field = checked((int)(imports.GetExpectedDefinitionForImportRow(
                    checked((int)import)) & 0x00FF_FFFF));
                if (!TrRmStaticFieldTableReader.IsRowReference(
                        (uint)field, fields.Count))
                {
                    throw TrRmStaticFieldTableReader.InvalidTable();
                }

                var contract = fieldContracts.GetRowForStaticField(field);
                var ownerTypeDef = fields.GetOwnerTypeDefRow(field);
                if (fieldContracts.GetDeclarationRole(contract) != 2
                    || fields.GetEagerGroupRow(field) != group
                    || fieldContracts.GetPublicationRow(contract) != publication)
                {
                    throw TrRmStaticFieldTableReader.InvalidTable();
                }

                TrRmStaticFieldTableReader.ValidateOwnerSubstitution(ownerType,
                    ownerTypeDef, typeSpecs, typeDefs, imports);
                var dependencyRows = dependencyRowsByField[field];
                if (dependencyRows is null)
                {
                    dependencyRows = [];
                    dependencyRowsByField[field] = dependencyRows;
                }
                dependencyRows.Add(row);
                var key = (checked((int)import),
                    TrRmStaticFieldTableReader.GetOwnerSubstitutionHash(
                        ownerType, typeSpecs));
                if (!ownerSubstitutionsByImport.TryGetValue(key, out var previous))
                {
                    previous = [];
                    ownerSubstitutionsByImport.Add(key, previous);
                }

                if (previous.AnyEquivalent(ownerType, typeSpecs))
                {
                    throw TrRmStaticFieldTableReader.InvalidTable();
                }

                previous.Add(ownerType);
            }

            _dependencyRowsByField = dependencyRowsByField;
        }

        internal int GetFieldImportRow(int row)
            => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 0));
        internal uint GetOwnerTypeSpecRef(int row)
            => TrRmStaticFieldTableReader.Read(GetRow(row), 4);
        internal int GetExpectedEagerGroupRow(int row)
            => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 8));
        internal int GetExpectedPublicationRow(int row)
            => checked((int)TrRmStaticFieldTableReader.Read(GetRow(row), 12));
        internal int FindDependencyForFieldOwner(int fieldRow,
            uint ownerTypeSpecRef, TrRmTypeSpecTableReader typeSpecs)
        {
            if (fieldRow <= 0 || fieldRow >= _dependencyRowsByField.Length
                || _dependencyRowsByField[fieldRow] is not { } dependencyRows)
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }

            var found = 0;
            foreach (var dependency in dependencyRows)
            {
                var candidateOwner = GetOwnerTypeSpecRef(dependency);
                if ((candidateOwner == 0 && ownerTypeSpecRef == 0)
                    || (candidateOwner != 0 && ownerTypeSpecRef != 0
                        && typeSpecs.AreEquivalentTypeSpecRefs(
                            candidateOwner, ownerTypeSpecRef)))
                {
                    if (found != 0)
                    {
                        throw TrRmStaticFieldTableReader.InvalidTable();
                    }

                    found = dependency;
                }
            }

            return found != 0
                ? found
                : throw TrRmStaticFieldTableReader.InvalidTable();
        }

        private ReadOnlySpan<byte> GetRow(int row)
        {
            if (row <= 0 || row > _recordCount)
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }

            return _rows.Slice((row - 1) * RowSize, RowSize);
        }
    }

    internal static class TrRmStaticOwnerSubstitutionExtensions
    {
        internal static bool AnyEquivalent(this List<uint> candidates, uint ownerTypeSpecRef,
            TrRmTypeSpecTableReader typeSpecs)
        {
            foreach (var candidate in candidates)
            {
                if ((candidate == 0 && ownerTypeSpecRef == 0)
                    || (candidate != 0 && ownerTypeSpecRef != 0
                        && typeSpecs.AreEquivalentTypeSpecRefs(
                            candidate, ownerTypeSpecRef)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal readonly ref struct TrRmStaticAccessTableReader
    {
        private const int RowSize = 12;

        private readonly ReadOnlySpan<byte> _rows;
        private readonly int _recordCount;

        internal int Count => _recordCount;

        internal TrRmStaticAccessTableReader(ReadOnlySpan<byte> section,
            int recordCount, TrRmStaticFieldTableReader fields,
            TrRmStaticFieldContractTableReader fieldContracts,
            TrRmStaticDependencyTableReader dependencies,
            TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
            TrRmImportTableReader imports, TrRmArtifactSchema schema)
        {
            if (!TrRmStaticFieldTableReader.ValidCount(recordCount)
                || (long)recordCount * RowSize != section.Length
                || !schema.UsesGeneralPredicateGraphs())
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }

            _rows = section;
            _recordCount = recordCount;
            for (var row = 1; row <= recordCount; row++)
            {
                var data = GetRow(row);
                var targetKind = data[0];
                var authorityKind = data[1];
                var target = TrRmStaticFieldTableReader.Read(data, 4);
                var ownerType = TrRmStaticFieldTableReader.Read(data, 8);
                if (targetKind is not (1 or 2) || authorityKind is not (1 or 2)
                    || TrRmStaticFieldTableReader.ReadU16(data, 2) != 0)
                {
                    throw TrRmStaticFieldTableReader.InvalidTable();
                }

                if (targetKind == 1)
                {
                    if (!TrRmStaticFieldTableReader.IsRowReference(target, fields.Count))
                    {
                        throw TrRmStaticFieldTableReader.InvalidTable();
                    }

                    var field = checked((int)target);
                    var contract = fieldContracts.GetRowForStaticField(field);
                    if (fieldContracts.GetDeclarationRole(contract) != 1)
                    {
                        throw TrRmStaticFieldTableReader.InvalidTable();
                    }

                    TrRmStaticFieldTableReader.ValidateOwnerSubstitution(ownerType,
                        fields.GetOwnerTypeDefRow(field), typeSpecs, typeDefs, imports);
                }
                else
                {
                    if (!TrRmStaticFieldTableReader.IsRowReference(
                        target, dependencies.Count))
                    {
                        throw TrRmStaticFieldTableReader.InvalidTable();
                    }

                    var dependencyOwner = dependencies.GetOwnerTypeSpecRef(
                        checked((int)target));
                    var ownerTypeIsNull = ownerType == 0;
                    var dependencyOwnerIsNull = dependencyOwner == 0;
                    var ownerTypesMatch = ownerTypeIsNull == dependencyOwnerIsNull;
                    if (ownerTypesMatch && !ownerTypeIsNull)
                    {
                        ownerTypesMatch = typeSpecs.AreEquivalentTypeSpecRefs(
                            ownerType, dependencyOwner);
                    }

                    if (!ownerTypesMatch)
                    {
                        throw TrRmStaticFieldTableReader.InvalidTable();
                    }
                }
            }
        }

        internal byte GetTargetKind(int row) => GetRow(row)[0];
        internal byte GetAuthorityKind(int row) => GetRow(row)[1];
        internal uint GetTargetRow(int row)
            => TrRmStaticFieldTableReader.Read(GetRow(row), 4);
        internal uint GetOwnerTypeSpecRef(int row)
            => TrRmStaticFieldTableReader.Read(GetRow(row), 8);

        private ReadOnlySpan<byte> GetRow(int row)
        {
            if (row <= 0 || row > _recordCount)
            {
                throw TrRmStaticFieldTableReader.InvalidTable();
            }

            return _rows.Slice((row - 1) * RowSize, RowSize);
        }
    }

    internal static class TrRmStaticFieldTableWriter
    {
        private const int RowSize = 20;

        internal static byte[] Encode(IReadOnlyList<TrRmStaticFieldRow> rows,
            TrRmStringTableReader strings, TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs, TrRmEagerGroupTableReader eagerGroups,
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
                TrRmStaticFieldTableReader.Write(
                    result, offset, unchecked((uint)row.OwnerTypeDefRow));
                TrRmStaticFieldTableReader.Write(
                    result, offset + 4, unchecked((uint)row.NameStringRow));
                TrRmStaticFieldTableReader.Write(result, offset + 8, row.TypeSpecRef);
                TrRmStaticFieldTableReader.Write(result, offset + 12, row.Flags);
                TrRmStaticFieldTableReader.Write(
                    result, offset + 16, unchecked((uint)row.EagerGroupRow));
            }

            _ = new TrRmStaticFieldTableReader(
                result, rows.Count, strings, typeDefs, typeSpecs, eagerGroups, schema);
            return result;
        }
    }

    internal static class TrRmEagerGroupTableWriter
    {
        private const int RowSize = 20;

        internal static byte[] Encode(IReadOnlyList<TrRmEagerGroupRow> rows,
            int typeDefCount, int initializerCount, int methodDefCount,
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
                TrRmStaticFieldTableReader.Write(
                    result, offset, unchecked((uint)row.OwnerTypeDefRow));
                result[offset + 4] = row.TriggerTag;
                result[offset + 5] = row.ExecutionPolicy;
                TrRmStaticFieldTableReader.Write(
                    result, offset + 8, unchecked((uint)row.InitializerFirst));
                TrRmStaticFieldTableReader.Write(
                    result, offset + 12, unchecked((uint)row.InitializerCount));
                TrRmStaticFieldTableReader.Write(
                    result, offset + 16, unchecked((uint)row.ConstructorMethodDefRow));
            }

            _ = new TrRmEagerGroupTableReader(
                result, rows.Count, typeDefCount, initializerCount, methodDefCount, schema);
            return result;
        }
    }

    internal static class TrRmStaticFieldContractTableWriter
    {
        private const int RowSize = 20;

        internal static byte[] Encode(
            IReadOnlyList<TrRmStaticFieldContractRow> rows,
            int staticFieldCount, int lazyUnitCount, int publicationCount,
            TrRmStaticFieldTableReader staticFields, TrRmArtifactSchema schema)
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
                TrRmStaticFieldTableReader.Write(
                    result, offset, unchecked((uint)row.StaticFieldRow));
                result[offset + 4] = row.DeclarationRole;
                result[offset + 5] = row.Visibility;
                result[offset + 6] = row.CallerUnsafe;
                TrRmStaticFieldTableReader.Write(result, offset + 8, row.DeclarationOrdinal);
                TrRmStaticFieldTableReader.Write(
                    result, offset + 12, unchecked((uint)row.LazyUnitRow));
                TrRmStaticFieldTableReader.Write(
                    result, offset + 16, unchecked((uint)row.PublicationRow));
            }

            _ = new TrRmStaticFieldContractTableReader(result, rows.Count,
                staticFieldCount, lazyUnitCount, publicationCount, staticFields, schema);
            return result;
        }
    }

    internal static class TrRmEagerInitializerTableWriter
    {
        private const int RowSize = 16;

        internal static byte[] Encode(IReadOnlyList<TrRmEagerInitializerRow> rows,
            TrRmEagerGroupTableReader groups, TrRmStaticFieldTableReader fields,
            TrRmStaticFieldContractTableReader fieldContracts, int methodDefCount,
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
                TrRmStaticFieldTableReader.Write(
                    result, offset, unchecked((uint)row.EagerGroupRow));
                TrRmStaticFieldTableReader.Write(result, offset + 4, row.Ordinal);
                TrRmStaticFieldTableReader.Write(
                    result, offset + 8, unchecked((uint)row.StaticFieldRow));
                TrRmStaticFieldTableReader.Write(
                    result, offset + 12, unchecked((uint)row.InitializerMethodDefRow));
            }

            _ = new TrRmEagerInitializerTableReader(result, rows.Count,
                groups, fields, fieldContracts, methodDefCount, schema);
            return result;
        }
    }

    internal static class TrRmLazyUnitTableWriter
    {
        private const int RowSize = 8;

        internal static byte[] Encode(IReadOnlyList<TrRmLazyUnitRow> rows,
            TrRmEagerGroupTableReader groups,
            TrRmEagerInitializerTableReader initializers,
            TrRmStaticFieldTableReader fields,
            TrRmStaticFieldContractTableReader fieldContracts, int methodDefCount,
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
                TrRmStaticFieldTableReader.Write(
                    result, offset, unchecked((uint)row.StaticFieldRow));
                TrRmStaticFieldTableReader.Write(
                    result, offset + 4, unchecked((uint)row.InitializerMethodDefRow));
            }

            _ = new TrRmLazyUnitTableReader(result, rows.Count,
                groups, initializers, fields, fieldContracts, methodDefCount, schema);
            return result;
        }
    }

    internal static class TrRmStaticAccessTableWriter
    {
        private const int RowSize = 12;

        internal static byte[] Encode(IReadOnlyList<TrRmStaticAccessRow> rows,
            TrRmStaticFieldTableReader fields,
            TrRmStaticFieldContractTableReader fieldContracts,
            TrRmStaticDependencyTableReader dependencies,
            TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
            TrRmImportTableReader imports, TrRmArtifactSchema schema)
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
                result[offset] = row.TargetKind;
                result[offset + 1] = row.AuthorityKind;
                TrRmStaticFieldTableReader.Write(result, offset + 4, row.TargetRow);
                TrRmStaticFieldTableReader.Write(
                    result, offset + 8, row.OwnerTypeSpecRef);
            }

            _ = new TrRmStaticAccessTableReader(result, rows.Count, fields,
                fieldContracts, dependencies, typeSpecs, typeDefs, imports, schema);
            return result;
        }
    }

    internal static class TrRmStaticDependencyTableWriter
    {
        private const int RowSize = 16;

        internal static byte[] Encode(IReadOnlyList<TrRmStaticDependencyRow> rows,
            TrRmImportTableReader imports, TrRmStaticFieldTableReader fields,
            TrRmStaticFieldContractTableReader fieldContracts,
            TrRmEagerGroupTableReader groups, int publicationCount,
            TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
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
                TrRmStaticFieldTableReader.Write(
                    result, offset, unchecked((uint)row.FieldImportRow));
                TrRmStaticFieldTableReader.Write(
                    result, offset + 4, row.OwnerTypeSpecRef);
                TrRmStaticFieldTableReader.Write(
                    result, offset + 8, unchecked((uint)row.ExpectedEagerGroupRow));
                TrRmStaticFieldTableReader.Write(
                    result, offset + 12, unchecked((uint)row.ExpectedPublicationRow));
            }

            _ = new TrRmStaticDependencyTableReader(result, rows.Count, imports,
                fields, fieldContracts, groups, publicationCount,
                typeSpecs, typeDefs, schema);
            return result;
        }
    }

    internal static class TrRmStaticImmutablePublicationTableWriter
    {
        private const int RowSize = 24;

        internal static byte[] Encode(
            IReadOnlyList<TrRmStaticImmutablePublicationRow> rows,
            TrRmStaticFieldTableReader fields,
            TrRmStaticFieldContractTableReader fieldContracts,
            TrRmTypeSpecTableReader typeSpecs, TrRmLazyUnitTableReader lazyUnits,
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
                TrRmStaticFieldTableReader.Write(
                    result, offset, unchecked((uint)row.StaticFieldRow));
                result[offset + 4] = row.BackingKind;
                result[offset + 5] = row.CompletionKind;
                result[offset + 6] = row.LifetimeKind;
                result[offset + 7] = row.SharingKind;
                TrRmStaticFieldTableReader.Write(
                    result, offset + 8, row.ExtentTypeSpecRef);
                BinaryPrimitives.WriteUInt64LittleEndian(
                    result.AsSpan(offset + 12), row.ValueCount);
                TrRmStaticFieldTableReader.Write(
                    result, offset + 20, unchecked((uint)row.ConstantSourceRow));
            }

            _ = new TrRmStaticImmutablePublicationTableReader(result, rows.Count,
                fields, fieldContracts, typeSpecs, lazyUnits, constantSources, schema);
            return result;
        }
    }
