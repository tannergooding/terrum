// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates FieldDef references, flags and TypeDef range ownership.</summary>
/// <remarks>The borrowed FieldDef, TypeDef and String sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmFieldDefTableReader
{
    private const int RowSize = 16;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly TrRmTypeDefTableReader _types;
    private readonly TrRmTypeSpecTableReader _typeSpecs;
    private readonly int _recordCount;
    private readonly TrRmArtifactSchema _schema;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated FieldDef rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates FieldDef rows and their owning TypeDef ranges.</summary>
    /// <param name="section">The borrowed 16-byte-row FieldDef section.</param>
    /// <param name="recordCount">The number of FieldDef rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="typeSpecs">The validated TypeSpec table.</param>
    public TrRmFieldDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs)
        : this(section, recordCount, strings, types, typeSpecs, null)
    {
    }

    internal TrRmFieldDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmA3LayoutResourceLedger? resourceLedger)
        : this(section, recordCount, strings, types, typeSpecs,
            TrRmArtifactSchema.Standard, resourceLedger)
    {
    }

    internal TrRmFieldDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        resourceLedger?.ConsumeWork(4);
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || recordCount != types.FieldCount || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidField();
        }

        _rows = section;
        _strings = strings;
        _types = types;
        _typeSpecs = typeSpecs;
        _recordCount = recordCount;
        _schema = schema;
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var owner = Read(row - 1, 0);
            var name = Read(row - 1, 4);
            var spec = Read(row - 1, 8);
            var flags = Read(row - 1, 12);
            var propertyBacking = (flags & 0x10) != 0;
            var permitsPropertyBacking = schema.UsesPropertyInitializedState();
            _resourceLedger?.ConsumeWork(6);
            if (owner == 0 || owner > types.Count || owner != types.GetFieldOwner(row)
                || (propertyBacking
                    ? !permitsPropertyBacking || name != 0
                        || (flags & 0x0Bu) != 0
                    : name is 0 or > 0xFF_FFFF)
                || (flags & (permitsPropertyBacking ? ~31u : ~15u)) != 0
                || (flags & 3) == 3)
            {
                throw InvalidField();
            }

            _ = typeSpecs.GetTypeSpecRef(spec);
            if (!propertyBacking)
            {
                _resourceLedger?.ConsumeWork();
                _ = _strings.DecodeName((int)name);
            }
        }
    }

    /// <summary>Gets the owning TypeDef row for a validated FieldDef row.</summary>
    public int GetOwnerTypeDef(int row)
    {
        CheckRow(row);
        return _types.GetFieldOwner(row);
    }

    /// <summary>Gets the decoded source name for a FieldDef row.</summary>
    /// <remarks>Returns an empty string for a PropertyBacking FieldDef.</remarks>
    public string GetName(int row)
    {
        _resourceLedger?.ConsumeWork();
        CheckRow(row);
        var name = Read(row - 1, 4);
        if (name == 0 && IsPropertyBackingField(row))
        {
            return string.Empty;
        }
        if (name == 0 || name > 0xFF_FFFF)
        {
            throw InvalidField();
        }

        _resourceLedger?.ConsumeWork();
        return _strings.DecodeName((int)name);
    }

    /// <summary>Gets the complete type reference for a validated FieldDef row.</summary>
    public uint GetTypeSpecRef(int row)
    {
        CheckRow(row);
        return Read(row - 1, 8);
    }

    /// <summary>Gets the referenced TypeSpec row when the FieldDef uses a row reference.</summary>
    public int GetTypeSpecRow(int row)
    {
        var reference = _typeSpecs.GetTypeSpecRef(GetTypeSpecRef(row));
        if (!reference.IsRow)
        {
            throw InvalidField();
        }

        return reference.Row;
    }

    /// <summary>Gets the flags for a validated FieldDef row.</summary>
    public uint GetFlags(int row)
    {
        CheckRow(row);
        return Read(row - 1, 12);
    }

    /// <summary>Gets whether a FieldDef is the hidden backing field of a property.</summary>
    public bool IsPropertyBackingField(int row)
    {
        CheckRow(row);
        return _schema.UsesPropertyInitializedState()
            && (Read(row - 1, 12) & 0x10) != 0;
    }

    private void CheckRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidField();
        }
    }

    private uint Read(int row, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            _rows.Slice((row * RowSize) + offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidField()
        => new("Invalid TRRM FieldDef table.");
}
