// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A complete implementing/interface instance and its typed binding ranges.</summary>
/// <param name="ImplementingTypeSpecRef">The complete implementing type.</param>
/// <param name="InterfaceTypeSpecRef">The complete interface instance.</param>
/// <param name="MemberFirst">The first owned WitnessMember row, or zero when empty.</param>
/// <param name="MemberCount">The number of owned WitnessMember rows.</param>
/// <param name="AssociatedTypeFirst">The first owned WitnessAssociatedType row, or zero when empty.</param>
/// <param name="AssociatedTypeCount">The number of owned WitnessAssociatedType rows.</param>
/// <param name="AssociatedConstFirst">The first owned WitnessAssociatedConst row, or zero when empty.</param>
/// <param name="AssociatedConstCount">The number of owned WitnessAssociatedConst rows.</param>
public readonly record struct TrRmInterfaceWitnessRow(
    uint ImplementingTypeSpecRef, uint InterfaceTypeSpecRef,
    int MemberFirst, int MemberCount, int AssociatedTypeFirst, int AssociatedTypeCount,
    int AssociatedConstFirst, int AssociatedConstCount);

/// <summary>An exact selected target for one witness requirement.</summary>
/// <param name="WitnessRow">The one-based owning InterfaceWitness row.</param>
/// <param name="DeclaringInterfaceTypeSpecRef">The complete declaring interface instance.</param>
/// <param name="RequirementMethodDefRow">The one-based requirement MethodDef row.</param>
/// <param name="SelectedOwnerTypeSpecRef">The optional complete selected owner type.</param>
/// <param name="SelectedMethodDefRow">The one-based selected MethodDef row.</param>
public readonly record struct TrRmWitnessMemberRow(
    int WitnessRow, uint DeclaringInterfaceTypeSpecRef,
    int RequirementMethodDefRow, uint SelectedOwnerTypeSpecRef,
    int SelectedMethodDefRow);

/// <summary>Validates witness instances and their exclusive typed binding ranges.</summary>
public readonly ref struct TrRmInterfaceWitnessTableReader
{
    private const int RowSize = 32;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated InterfaceWitness rows.</summary>
    public int Count => _recordCount;

    /// <summary>Gets the total WitnessMember rows owned by the witness table.</summary>
    public int MemberCount { get; }

    /// <summary>Gets the total WitnessAssociatedType rows owned by the witness table.</summary>
    public int AssociatedTypeCount { get; }

    /// <summary>Gets the total WitnessAssociatedConst rows owned by the witness table.</summary>
    public int AssociatedConstCount { get; }

    /// <summary>Validates complete implementing/interface types and exclusive ranges.</summary>
    /// <param name="section">The borrowed 32-byte InterfaceWitness rows.</param>
    /// <param name="recordCount">The number of InterfaceWitness rows.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="memberCount">The number of WitnessMember rows.</param>
    /// <param name="associatedTypeCount">The number of WitnessAssociatedType rows.</param>
    /// <param name="associatedConstCount">The number of WitnessAssociatedConst rows.</param>
    public TrRmInterfaceWitnessTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmTypeSpecTableReader types,
        int memberCount, int associatedTypeCount, int associatedConstCount)
        : this(section, recordCount, types, memberCount, associatedTypeCount,
            associatedConstCount, null)
    {
    }

    internal TrRmInterfaceWitnessTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmTypeSpecTableReader types,
        int memberCount, int associatedTypeCount, int associatedConstCount,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        if (!ValidCount(recordCount) || !ValidCount(memberCount)
            || !ValidCount(associatedTypeCount) || !ValidCount(associatedConstCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidInterfaceWitness();
        }

        _rows = section;
        _recordCount = recordCount;
        MemberCount = memberCount;
        AssociatedTypeCount = associatedTypeCount;
        AssociatedConstCount = associatedConstCount;
        var storageWork = checked((ulong)memberCount
            + (ulong)associatedTypeCount + (ulong)associatedConstCount + 6UL);
        resourceLedger?.ConsumeWork(storageWork);
        var membersOwned = new bool[memberCount + 1];
        var associatedTypesOwned = new bool[associatedTypeCount + 1];
        var associatedConstsOwned = new bool[associatedConstCount + 1];
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var implementingType = Read(data, 0);
            var interfaceType = Read(data, 4);
            var memberFirst = Read(data, 8);
            var members = Read(data, 12);
            var associatedTypeFirst = Read(data, 16);
            var associatedTypes = Read(data, 20);
            var associatedConstFirst = Read(data, 24);
            var associatedConsts = Read(data, 28);
            if (implementingType == 0 || interfaceType == 0
                || !ValidRange(memberFirst, members, memberCount)
                || !ValidRange(associatedTypeFirst, associatedTypes, associatedTypeCount)
                || !ValidRange(associatedConstFirst, associatedConsts, associatedConstCount))
            {
                throw InvalidInterfaceWitness();
            }

            _ = types.GetTypeSpecRef(implementingType);
            _ = types.GetTypeSpecRef(interfaceType);
            MarkRange(membersOwned, memberFirst, members, _resourceLedger);
            MarkRange(associatedTypesOwned, associatedTypeFirst,
                associatedTypes, _resourceLedger);
            MarkRange(associatedConstsOwned, associatedConstFirst,
                associatedConsts, _resourceLedger);
        }

        if (HasUnownedRows(membersOwned, _resourceLedger)
            || HasUnownedRows(associatedTypesOwned, _resourceLedger)
            || HasUnownedRows(associatedConstsOwned, _resourceLedger))
        {
            throw InvalidInterfaceWitness();
        }
    }

    /// <summary>Gets the complete implementing TypeSpecRef.</summary>
    public uint GetImplementingTypeSpecRef(int row)
        => Read(GetRow(row), 0);

    /// <summary>Gets the complete interface-instance TypeSpecRef.</summary>
    public uint GetInterfaceTypeSpecRef(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the one-based WitnessMember range.</summary>
    public (int First, int Count) GetMemberRange(int row)
    {
        var data = GetRow(row);
        return (checked((int)Read(data, 8)), checked((int)Read(data, 12)));
    }

    /// <summary>Gets the one-based WitnessAssociatedType range.</summary>
    public (int First, int Count) GetAssociatedTypeRange(int row)
    {
        var data = GetRow(row);
        return (checked((int)Read(data, 16)), checked((int)Read(data, 20)));
    }

    /// <summary>Gets the one-based WitnessAssociatedConst range.</summary>
    public (int First, int Count) GetAssociatedConstRange(int row)
    {
        var data = GetRow(row);
        return (checked((int)Read(data, 24)), checked((int)Read(data, 28)));
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidInterfaceWitness();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static void MarkRange(bool[] owners, uint first, uint count,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        for (var row = first; row < first + count; row++)
        {
            resourceLedger?.ConsumeWork(2);
            if (owners[row])
            {
                throw InvalidInterfaceWitness();
            }

            owners[row] = true;
        }
    }

    private static bool HasUnownedRows(bool[] owners,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        for (var row = 1; row < owners.Length; row++)
        {
            resourceLedger?.ConsumeWork();
            if (!owners[row])
            {
                return true;
            }
        }

        return false;
    }

    private static bool ValidRange(uint first, uint count, int total)
        => count == 0 ? first == 0
            : first != 0 && first <= (uint)total
                && count <= (uint)total - first + 1;

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidInterfaceWitness()
        => new("Invalid TRRM InterfaceWitness table.");
}

/// <summary>Validates witness member rows against their exact witness ranges.</summary>
public readonly ref struct TrRmWitnessMemberTableReader
{
    private const int RowSize = 20;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated WitnessMember rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates member owners, complete types and MethodDef row bounds.</summary>
    /// <param name="section">The borrowed 20-byte WitnessMember rows.</param>
    /// <param name="recordCount">The number of WitnessMember rows.</param>
    /// <param name="witnesses">The validated InterfaceWitness table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    public TrRmWitnessMemberTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmInterfaceWitnessTableReader witnesses,
        TrRmTypeSpecTableReader types, TrRmMethodDefTableReader methods)
        : this(section, recordCount, witnesses, types, methods, null)
    {
    }

    internal TrRmWitnessMemberTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmInterfaceWitnessTableReader witnesses,
        TrRmTypeSpecTableReader types, TrRmMethodDefTableReader methods,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        if (!ValidCount(recordCount) || recordCount != witnesses.MemberCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidWitnessMember();
        }

        _rows = section;
        _recordCount = recordCount;
        _resourceLedger = resourceLedger;
        for (var witness = 1; witness <= witnesses.Count; witness++)
        {
            _resourceLedger?.ConsumeWork();
            var (first, count) = witnesses.GetMemberRange(witness);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                _resourceLedger?.ConsumeWork();
                var row = first + ordinal;
                var data = GetRow(row);
                var owner = Read(data, 0);
                var declaringInterface = Read(data, 4);
                var requirement = Read(data, 8);
                var selectedOwner = Read(data, 12);
                var selectedMethod = Read(data, 16);
                if (owner != (uint)witness || declaringInterface == 0
                    || requirement is 0 or > 0xFF_FFFF
                    || requirement > (uint)methods.Count
                    || selectedMethod is 0 or > 0xFF_FFFF
                    || selectedMethod > (uint)methods.Count)
                {
                    throw InvalidWitnessMember();
                }

                _ = types.GetTypeSpecRef(declaringInterface);
                _ = types.GetTypeSpecRef(selectedOwner, allowNull: true);
            }
        }
    }

    /// <summary>Gets the one-based owning InterfaceWitness row.</summary>
    public int GetWitnessRow(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the complete declaring-interface TypeSpecRef.</summary>
    public uint GetDeclaringInterfaceTypeSpecRef(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the one-based requirement MethodDef row.</summary>
    public int GetRequirementMethodDef(int row)
        => checked((int)Read(GetRow(row), 8));

    /// <summary>Gets the optional complete selected-owner TypeSpecRef, or zero.</summary>
    public uint GetSelectedOwnerTypeSpecRef(int row)
        => Read(GetRow(row), 12);

    /// <summary>Gets the one-based selected MethodDef row.</summary>
    public int GetSelectedMethodDef(int row)
        => checked((int)Read(GetRow(row), 16));

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidWitnessMember();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidWitnessMember()
        => new("Invalid TRRM WitnessMember table.");
}

/// <summary>Encodes witness instances with checked type and range references.</summary>
public static class TrRmInterfaceWitnessTableWriter
{
    private const int RowSize = 32;

    /// <summary>Encodes InterfaceWitness rows in one-based table order.</summary>
    /// <param name="rows">The complete witness type pairs and binding ranges.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="memberCount">The number of WitnessMember rows.</param>
    /// <param name="associatedTypeCount">The number of WitnessAssociatedType rows.</param>
    /// <param name="associatedConstCount">The number of WitnessAssociatedConst rows.</param>
    /// <returns>The encoded InterfaceWitness section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmInterfaceWitnessRow> rows,
        TrRmTypeSpecTableReader types, int memberCount,
        int associatedTypeCount, int associatedConstCount)
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
            Write(result, offset, row.ImplementingTypeSpecRef);
            Write(result, offset + 4, row.InterfaceTypeSpecRef);
            Write(result, offset + 8, unchecked((uint)row.MemberFirst));
            Write(result, offset + 12, unchecked((uint)row.MemberCount));
            Write(result, offset + 16, unchecked((uint)row.AssociatedTypeFirst));
            Write(result, offset + 20, unchecked((uint)row.AssociatedTypeCount));
            Write(result, offset + 24, unchecked((uint)row.AssociatedConstFirst));
            Write(result, offset + 28, unchecked((uint)row.AssociatedConstCount));
        }

        _ = new TrRmInterfaceWitnessTableReader(result, rows.Count, types,
            memberCount, associatedTypeCount, associatedConstCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}

/// <summary>Encodes witness member rows with checked typed declaration references.</summary>
public static class TrRmWitnessMemberTableWriter
{
    private const int RowSize = 20;

    /// <summary>Encodes WitnessMember rows in one-based table order.</summary>
    /// <param name="rows">The exact requirement/selected-target bindings.</param>
    /// <param name="witnesses">The validated InterfaceWitness table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <returns>The encoded WitnessMember section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmWitnessMemberRow> rows,
        TrRmInterfaceWitnessTableReader witnesses,
        TrRmTypeSpecTableReader types, TrRmMethodDefTableReader methods)
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
            Write(result, offset + 8, unchecked((uint)row.RequirementMethodDefRow));
            Write(result, offset + 12, row.SelectedOwnerTypeSpecRef);
            Write(result, offset + 16, unchecked((uint)row.SelectedMethodDefRow));
        }

        _ = new TrRmWitnessMemberTableReader(
            result, rows.Count, witnesses, types, methods);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}

/// <summary>An effective associated-type selection owned by one interface witness.</summary>
/// <param name="WitnessRow">The one-based owning InterfaceWitness row.</param>
/// <param name="DeclaringInterfaceTypeSpecRef">The complete declaring interface instance.</param>
/// <param name="AssociatedTypeDefReference">The direct or imported AssociatedTypeDef identity.</param>
/// <param name="ValueTypeSpecRef">The complete selected associated type.</param>
/// <param name="BindingOrigin">The effective selection tag: 1=explicit or 2=declaration default.</param>
public readonly record struct TrRmWitnessAssociatedTypeRow(
    int WitnessRow, uint DeclaringInterfaceTypeSpecRef,
    uint AssociatedTypeDefReference, uint ValueTypeSpecRef,
    uint BindingOrigin);

/// <summary>Validates witness-associated type selections and their owning ranges.</summary>
public readonly ref struct TrRmWitnessAssociatedTypeTableReader
{
    private const int RowSize = 20;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated WitnessAssociatedType rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates typed declaration, type and selection-origin references.</summary>
    /// <param name="section">The borrowed 20-byte WitnessAssociatedType rows.</param>
    /// <param name="recordCount">The number of WitnessAssociatedType rows.</param>
    /// <param name="witnesses">The validated InterfaceWitness table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="typeDefs">The validated TypeDef table.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    /// <param name="interfaceBases">The validated InterfaceBase table.</param>
    /// <param name="associatedTypeDefs">The validated AssociatedTypeDef table.</param>
    /// <param name="imports">The validated Import table.</param>
    public TrRmWitnessAssociatedTypeTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmInterfaceWitnessTableReader witnesses,
        TrRmTypeSpecTableReader types, TrRmTypeDefTableReader typeDefs,
        TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceBaseTableReader interfaceBases,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs,
        TrRmImportTableReader imports)
        : this(section, recordCount, witnesses, types, typeDefs, interfaces,
            interfaceBases, associatedTypeDefs, imports, null)
    {
    }

    internal TrRmWitnessAssociatedTypeTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmInterfaceWitnessTableReader witnesses,
        TrRmTypeSpecTableReader types, TrRmTypeDefTableReader typeDefs,
        TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceBaseTableReader interfaceBases,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs,
        TrRmImportTableReader imports,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != witnesses.AssociatedTypeCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidWitnessAssociatedType();
        }

        _rows = section;
        _recordCount = recordCount;
        _resourceLedger = resourceLedger;
        for (var witness = 1; witness <= witnesses.Count; witness++)
        {
            _resourceLedger?.ConsumeWork();
            var (first, count) = witnesses.GetAssociatedTypeRange(witness);
            if (count == 0)
            {
                continue;
            }

            var interfaceClosure = GetInterfaceClosure(
                witnesses.GetInterfaceTypeSpecRef(witness), types, typeDefs,
                interfaces, interfaceBases, imports, _resourceLedger);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                _resourceLedger?.ConsumeWork();
                var row = first + ordinal;
                var data = GetRow(row);
                var witnessRow = Read(data, 0);
                var declaringInterface = Read(data, 4);
                var associatedTypeDef = Read(data, 8);
                var valueType = Read(data, 12);
                var bindingOrigin = Read(data, 16);
                _resourceLedger?.ConsumeWork();
                if (witnessRow != (uint)witness
                    || !TrRmAssociatedTypeReference.TryGetLocalRow(
                        associatedTypeDef, associatedTypeDefs.Count, imports,
                        out var associatedTypeDefRow)
                    || bindingOrigin is not (1 or 2))
                {
                    throw InvalidWitnessAssociatedType();
                }

                var declaringType = types.GetTypeSpecRef(declaringInterface);
                if (!declaringType.IsRow
                    || declaringType.Form is not (0x02 or 0x03 or 0x05))
                {
                    throw InvalidWitnessAssociatedType();
                }

                var declaringTypeDef = types.GetNominalTypeDefRow(
                    declaringInterface, typeDefs, imports);
                var associatedOwnerTypeDef = interfaces.GetOwnerTypeDef(
                    associatedTypeDefs.GetOwnerInterfaceDef(associatedTypeDefRow));
                _resourceLedger?.ConsumeWork();
                if (declaringTypeDef != associatedOwnerTypeDef
                    || !interfaceClosure.Contains(declaringTypeDef))
                {
                    throw InvalidWitnessAssociatedType();
                }

                _ = types.GetTypeSpecRef(valueType);
                if (bindingOrigin == 2
                    && associatedTypeDefs.GetDefaultTypeSpecRef(associatedTypeDefRow) == 0)
                {
                    throw InvalidWitnessAssociatedType();
                }
            }
        }
    }

    internal static HashSet<int> GetInterfaceClosure(uint interfaceTypeSpecRef,
        TrRmTypeSpecTableReader types, TrRmTypeDefTableReader typeDefs,
        TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceBaseTableReader interfaceBases,
        TrRmImportTableReader imports,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        var rootTypeDef = types.GetNominalTypeDefRow(
            interfaceTypeSpecRef, typeDefs, imports);
        resourceLedger?.ConsumeWork(2);
        var pending = new Stack<(int TypeDefRow, ulong Depth)>();
        var closure = new HashSet<int>();
        resourceLedger?.ReserveWorkAtDepth(1, 1);
        pending.Push((rootTypeDef, 1));
        while (pending.TryPop(out var current))
        {
            resourceLedger?.ConsumeWork();
            resourceLedger?.CheckDepthFromCurrent(current.Depth);
            resourceLedger?.ConsumeWork(2);
            if (!closure.Add(current.TypeDefRow))
            {
                continue;
            }

            var interfaceDef = interfaces.GetInterfaceDefForTypeDef(
                current.TypeDefRow);
            if (interfaceDef == 0)
            {
                throw InvalidWitnessInterfaceClosure();
            }

            var (first, count) = interfaces.GetBaseRange(interfaceDef);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                resourceLedger?.ConsumeWork();
                var baseTypeDef = types.GetNominalTypeDefRow(
                    interfaceBases.GetBaseTypeSpecRef(first + ordinal),
                    typeDefs, imports);
                if (interfaces.GetInterfaceDefForTypeDef(baseTypeDef) == 0)
                {
                    throw InvalidWitnessInterfaceClosure();
                }

                var childDepth = checked(current.Depth + 1);
                resourceLedger?.ReserveWorkAtDepth(1, childDepth);
                pending.Push((baseTypeDef, childDepth));
            }
        }

        return closure;
    }

    private static InvalidDataException InvalidWitnessInterfaceClosure()
        => new("Invalid TRRM InterfaceWitness interface closure.");

    /// <summary>Gets the one-based owning InterfaceWitness row.</summary>
    public int GetWitnessRow(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the complete declaring-interface TypeSpecRef.</summary>
    public uint GetDeclaringInterfaceTypeSpecRef(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the direct or imported AssociatedTypeDef identity.</summary>
    public uint GetAssociatedTypeDefReference(int row)
        => Read(GetRow(row), 8);

    /// <summary>Gets the complete selected value TypeSpecRef.</summary>
    public uint GetValueTypeSpecRef(int row)
        => Read(GetRow(row), 12);

    /// <summary>Gets the effective selection kind.</summary>
    public uint GetBindingOrigin(int row)
        => Read(GetRow(row), 16);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidWitnessAssociatedType();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidWitnessAssociatedType()
        => new("Invalid TRRM WitnessAssociatedType table.");
}

/// <summary>Encodes effective associated-type selections with checked witness and declaration references.</summary>
public static class TrRmWitnessAssociatedTypeTableWriter
{
    private const int RowSize = 20;

    /// <summary>Encodes WitnessAssociatedType rows in one-based table order.</summary>
    /// <param name="rows">The effective associated-type selections.</param>
    /// <param name="witnesses">The validated InterfaceWitness table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="typeDefs">The validated TypeDef table.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    /// <param name="interfaceBases">The validated InterfaceBase table.</param>
    /// <param name="associatedTypeDefs">The validated AssociatedTypeDef table.</param>
    /// <param name="imports">The validated Import table.</param>
    /// <returns>The encoded WitnessAssociatedType section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmWitnessAssociatedTypeRow> rows,
        TrRmInterfaceWitnessTableReader witnesses, TrRmTypeSpecTableReader types,
        TrRmTypeDefTableReader typeDefs, TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceBaseTableReader interfaceBases,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs,
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
            Write(result, offset, unchecked((uint)row.WitnessRow));
            Write(result, offset + 4, row.DeclaringInterfaceTypeSpecRef);
            Write(result, offset + 8, row.AssociatedTypeDefReference);
            Write(result, offset + 12, row.ValueTypeSpecRef);
            Write(result, offset + 16, row.BindingOrigin);
        }

        _ = new TrRmWitnessAssociatedTypeTableReader(result, rows.Count,
            witnesses, types, typeDefs, interfaces, interfaceBases,
            associatedTypeDefs, imports);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
