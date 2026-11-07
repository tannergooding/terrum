// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates PropertyDef references and getter ownership and result type.</summary>
/// <remarks>The borrowed PropertyDef and referenced table sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmPropertyDefTableReader
{
    private const int RowSize = 20;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Validates PropertyDef rows and their getter method references.</summary>
    /// <param name="section">The borrowed PropertyDef section bytes.</param>
    /// <param name="recordCount">The number of PropertyDef rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="typeSpecs">The validated TypeSpec table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    public TrRmPropertyDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures)
        : this(section, recordCount, strings, types, typeSpecs, methods,
            signatures, deferGetterTypeIdentity: false,
            resourceLedger: null)
    {
    }

    internal TrRmPropertyDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, bool deferGetterTypeIdentity,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        resourceLedger?.ConsumeWork(3);
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidProperty();
        }

        _rows = section;
        _strings = strings;
        _recordCount = recordCount;
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            resourceLedger?.ConsumeWork();
            var data = GetRow(row);
            var owner = Read(data, 0);
            var name = Read(data, 4);
            var type = Read(data, 8);
            var getter = Read(data, 12);
            var flags = Read(data, 16);
            resourceLedger?.ConsumeWork(6);
            if (owner == 0 || owner > types.Count
                || name is 0 or > 0xFF_FFFF
                || flags > 2
                || getter == 0 || getter > methods.Count
                || methods.GetOwnerType((int)getter) != owner
                || (!deferGetterTypeIdentity
                    && !typeSpecs.AreEquivalentTypeSpecRefs(type,
                        signatures.GetResultTypeSpec(
                            methods.GetSignature((int)getter)))))
            {
                throw InvalidProperty();
            }

            _ = typeSpecs.GetTypeSpecRef(type);
            _ = typeSpecs.GetTypeSpecRef(signatures.GetResultTypeSpec(
                methods.GetSignature((int)getter)));
            resourceLedger?.ConsumeWork();
            _ = _strings.DecodeName((int)name);
        }
    }

    internal void ValidateGetterTypeIdentity(
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures,
        TrRmNativeFunctionPointerAnchorTableReader anchors)
    {
        for (var row = 1; row <= _recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var data = GetRow(row);
            var propertyType = Read(data, 8);
            var getter = checked((int)Read(data, 12));
            var getterType = signatures.GetResultTypeSpec(
                methods.GetSignature(getter));
            if (!anchors.AreEquivalentTypeSpecRefs(propertyType, getterType))
            {
                throw InvalidProperty();
            }
        }
    }

    /// <summary>Gets the source name of a PropertyDef row.</summary>
    /// <param name="row">The one-based PropertyDef row.</param>
    /// <returns>The property name.</returns>
    public string GetName(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidProperty();
        }

        var name = Read(GetRow(row), 4);
        if (name is 0 or > 0xFF_FFFF)
        {
            throw InvalidProperty();
        }

        return _strings.DecodeName((int)name);
    }

    /// <summary>Gets the number of validated PropertyDef rows.</summary>
    public int Count
    {
        get
        {
            _resourceLedger?.ConsumeWork();
            return _recordCount;
        }
    }

    /// <summary>Gets the owning TypeDef row of a property.</summary>
    /// <param name="row">The one-based PropertyDef row.</param>
    /// <returns>The one-based owner TypeDef row.</returns>
    public int GetOwnerType(int row)
        => (int)Read(GetRow(row), 0);

    /// <summary>Gets the flags recorded for a property.</summary>
    /// <param name="row">The one-based PropertyDef row.</param>
    /// <returns>The PropertyDef flags.</returns>
    public uint GetFlags(int row)
        => Read(GetRow(row), 16);

    /// <summary>Gets the getter MethodDef row of a property.</summary>
    /// <param name="row">The one-based PropertyDef row.</param>
    /// <returns>The one-based getter MethodDef row.</returns>
    public int GetGetterMethod(int row)
        => (int)Read(GetRow(row), 12);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidProperty();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidProperty()
        => new("Invalid TRRM PropertyDef table.");
}
