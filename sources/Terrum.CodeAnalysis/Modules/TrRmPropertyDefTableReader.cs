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
    private const int PropertyInitializedStateRowSize = 32;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly int _recordCount;
    private readonly int _rowSize;
    private readonly TrRmArtifactSchema _schema;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;
    private readonly int[]? _propertyRowsByBackingField;

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
        : this(section, recordCount, strings, types, typeSpecs, methods,
            signatures, deferGetterTypeIdentity, resourceLedger,
            TrRmArtifactSchema.Standard, default, default)
    {
    }

    internal TrRmPropertyDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, bool deferGetterTypeIdentity,
        TrRmA3LayoutResourceLedger? resourceLedger, TrRmArtifactSchema schema,
        TrRmFieldDefTableReader fields, TrRmParamTableReader parameters)
    {
        resourceLedger?.ConsumeWork(3);
        var usesPropertyInitializedState = schema.UsesPropertyInitializedState();
        var rowSize = usesPropertyInitializedState
            ? PropertyInitializedStateRowSize
            : RowSize;
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || (long)recordCount * rowSize != section.Length)
        {
            throw InvalidProperty();
        }

        _rows = section;
        _strings = strings;
        _recordCount = recordCount;
        _rowSize = rowSize;
        _schema = schema;
        _resourceLedger = resourceLedger;
        var backingFieldCount = usesPropertyInitializedState ? fields.Count : 0;
        var accessorMethodCount = usesPropertyInitializedState ? methods.Count : 0;
        if (usesPropertyInitializedState)
        {
            resourceLedger?.ConsumeWork(checked((ulong)backingFieldCount
                + (ulong)accessorMethodCount + 2UL));
        }

        var propertyRowsByBackingField = usesPropertyInitializedState
            ? new int[backingFieldCount + 1]
            : [];
        var accessorMethods = usesPropertyInitializedState
            ? new bool[accessorMethodCount + 1]
            : [];

        for (var row = 1; row <= recordCount; row++)
        {
            resourceLedger?.ConsumeWork();
            var data = GetRow(row);
            var owner = Read(data, 0);
            var name = Read(data, 4);
            var type = Read(data, 8);
            var getter = Read(data, 12);
            var flags = Read(data, 16);
            var setter = usesPropertyInitializedState ? Read(data, 20) : 0;
            var initializer = usesPropertyInitializedState ? Read(data, 24) : 0;
            var backingField = usesPropertyInitializedState ? Read(data, 28) : 0;
            resourceLedger?.ConsumeWork(6);
            if (owner == 0 || owner > types.Count
                || (usesPropertyInitializedState && types.GetKind((int)owner) != 2)
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
            if (usesPropertyInitializedState)
            {
                ValidatePropertyInitializedState(row, (int)owner, type,
                    (int)getter,
                    (int)setter, (int)initializer, (int)backingField, fields,
                    methods, signatures, typeSpecs, parameters,
                    propertyRowsByBackingField, accessorMethods);
            }

            resourceLedger?.ConsumeWork();
            _ = _strings.DecodeName((int)name);
        }

        if (usesPropertyInitializedState)
        {
            for (var row = 1; row <= fields.Count; row++)
            {
                resourceLedger?.ConsumeWork();
                if (fields.IsPropertyBackingField(row)
                    && propertyRowsByBackingField[row] == 0)
                {
                    throw InvalidProperty();
                }
            }
        }

        _propertyRowsByBackingField = usesPropertyInitializedState
            ? propertyRowsByBackingField : null;
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

    internal int GetSetterMethod(int row)
        => _schema.UsesPropertyInitializedState()
            ? (int)Read(GetRow(row), 20)
            : 0;

    internal int GetInitMethod(int row)
        => _schema.UsesPropertyInitializedState()
            ? (int)Read(GetRow(row), 24)
            : 0;

    internal int GetBackingFieldDef(int row)
        => _schema.UsesPropertyInitializedState()
            ? (int)Read(GetRow(row), 28)
            : 0;

    internal int GetPropertyDefForBackingField(int fieldRow)
    {
        _resourceLedger?.ConsumeWork();
        if (!_schema.UsesPropertyInitializedState()
            || _propertyRowsByBackingField is null
            || fieldRow <= 0 || fieldRow >= _propertyRowsByBackingField.Length)
        {
            throw InvalidProperty();
        }

        return _propertyRowsByBackingField[fieldRow];
    }

    internal uint GetTypeSpecRef(int row)
        => Read(GetRow(row), 8);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidProperty();
        }

        return _rows.Slice((row - 1) * _rowSize, _rowSize);
    }

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidProperty()
        => new("Invalid TRRM PropertyDef table.");

    private void ValidatePropertyInitializedState(int propertyRow, int owner,
        uint propertyType,
        int getter, int setter, int initializer,
        int backingField, TrRmFieldDefTableReader fields,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmTypeSpecTableReader typeSpecs, TrRmParamTableReader parameters,
        int[] propertyRowsByBackingField, bool[] accessorMethods)
    {
        _resourceLedger?.ConsumeWork(6);
        if ((setter != 0 && initializer != 0)
            || (backingField == 0 && (setter != 0 || initializer != 0))
            || (backingField != 0 && (backingField > fields.Count
                || !fields.IsPropertyBackingField(backingField)
                || fields.GetOwnerTypeDef(backingField) != owner
                || !typeSpecs.AreEquivalentTypeSpecRefs(propertyType,
                    fields.GetTypeSpecRef(backingField))
                || propertyRowsByBackingField[backingField] != 0)))
        {
            throw InvalidProperty();
        }

        if (backingField != 0)
        {
            propertyRowsByBackingField[backingField] = propertyRow;
        }

        ValidateAccessor(getter, owner, propertyType, 0, false, methods,
            signatures, typeSpecs, parameters, accessorMethods);
        if (setter != 0)
        {
            ValidateAccessor(setter, owner, propertyType, 1, false, methods,
                signatures, typeSpecs, parameters, accessorMethods);
        }

        if (initializer != 0)
        {
            ValidateAccessor(initializer, owner, propertyType, 1, true, methods,
                signatures, typeSpecs, parameters, accessorMethods);
        }

    }

    private void ValidateAccessor(int methodRow, int owner, uint propertyType,
        int expectedParamCount, bool isInitializer,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmTypeSpecTableReader typeSpecs,
        TrRmParamTableReader parameters, bool[] accessorMethods)
    {
        _resourceLedger?.ConsumeWork(5);
        if (methodRow <= 0 || methodRow > methods.Count
            || accessorMethods[methodRow]
            || methods.GetOwnerType(methodRow) != owner)
        {
            throw InvalidProperty();
        }

        var signature = methods.GetSignature(methodRow);
        var (paramStart, paramCount) = signatures.GetParamRange(signature);
        if (signatures.GetReceiverTypeSpec(signature) == 0
            || signatures.GetGenericCount(signature) != 0
            || paramCount != expectedParamCount
            || (isInitializer && (methods.GetFlags(methodRow) & 4) != 0))
        {
            throw InvalidProperty();
        }

        if (expectedParamCount == 0)
        {
            if (!typeSpecs.AreEquivalentTypeSpecRefs(propertyType,
                    signatures.GetResultTypeSpec(signature)))
            {
                throw InvalidProperty();
            }
        }
        else
        {
            var valueType = parameters.GetTypeSpec(paramStart);
            if (!typeSpecs.AreEquivalentTypeSpecRefs(valueType, propertyType)
                || parameters.GetMode(paramStart) != 0
                || parameters.GetFlags(paramStart) != 0
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    signatures.GetResultTypeSpec(signature), 0xF200_0000))
            {
                throw InvalidProperty();
            }
        }

        accessorMethods[methodRow] = true;
    }
}
