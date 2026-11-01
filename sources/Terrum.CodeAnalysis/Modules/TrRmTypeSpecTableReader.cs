// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmTypeSpecRefInfo(
    uint Value, int Row, byte Form, byte Qualifiers, byte TypeCode, byte ArgumentCode)
{
    internal bool IsRow => Row != 0;
}

/// <summary>Structurally validated TypeSpec rows; executable type support is a separate contract.</summary>
/// <remarks>The borrowed section bytes must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmTypeSpecTableReader
{
    internal const ushort NativeFunctionPointerTypeKeyStart = 0x200;
    private const long TypeIdentityBase = 0x1000_0000_0000;
    // Keep normalized-key bucket collisions independent of process-randomized hash seeds.
    private const uint StructuralHashOffset = 2_166_136_261;
    private const uint StructuralHashPrime = 16_777_619;
    private static readonly byte[] s_existentialTypeSpec = [0x0F, 0];

    private readonly TrRmVariableTableIndex _rows;
    private readonly int _recordCount;
    private readonly int _typeDefCount;
    private readonly int _importCount;
    private readonly int _genericParamCount;
    private readonly int _nativeFunctionPointerSignatureCount;
    private readonly int _nativeFunctionPointerTypeKeyCount;
    private readonly int _methodDefCount;
    private readonly int _predicateCaptureCount;
    private readonly TrRmArtifactSchema _schema;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;
    private readonly ushort[] _compactValueTypeKeys;

    /// <summary>Creates a reader for TypeSpec rows and their referenced table counts.</summary>
    public TrRmTypeSpecTableReader(ReadOnlySpan<byte> section, int recordCount,
        int typeDefCount, int importCount, int genericParamCount,
        int nativeFunctionPointerSignatureCount = 0)
        : this(section, recordCount, typeDefCount, importCount, genericParamCount,
            nativeFunctionPointerSignatureCount, TrRmArtifactSchema.Standard)
    {
    }

    internal TrRmTypeSpecTableReader(ReadOnlySpan<byte> section, int recordCount,
        int typeDefCount, int importCount, int genericParamCount,
        int nativeFunctionPointerSignatureCount, TrRmArtifactSchema schema,
        int methodDefCount = 0, int predicateCaptureCount = 0,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (!ValidCount(recordCount) || !ValidCount(typeDefCount)
            || !ValidCount(importCount)
            || !ValidCount(genericParamCount)
            || !ValidCount(nativeFunctionPointerSignatureCount))
        {
            throw InvalidType();
        }
        if (!ValidCount(methodDefCount) || !ValidCount(predicateCaptureCount)
            || schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.Development
                or TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft
                or TrRmArtifactSchema.CopyOriginDraft
                or TrRmArtifactSchema.ForeignInvocationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                or TrRmArtifactSchema.ScalarFrozenValuesDraft
                or TrRmArtifactSchema.NominalEnumsDraft
                or TrRmArtifactSchema.RootPinRuntimeDraft
                or TrRmArtifactSchema.GeneralPredicateGraphsDraft)
            || (!schema.IsDevelopmentProfile()
                && nativeFunctionPointerSignatureCount != 0))
        {
            throw InvalidType();
        }

        resourceLedger?.ConsumeWork(checked((ulong)recordCount));
        _rows = new TrRmVariableTableIndex(section, recordCount, 2, 1);
        _recordCount = recordCount;
        _typeDefCount = typeDefCount;
        _importCount = importCount;
        _genericParamCount = genericParamCount;
        _nativeFunctionPointerSignatureCount = nativeFunctionPointerSignatureCount;
        _methodDefCount = methodDefCount;
        _predicateCaptureCount = predicateCaptureCount;
        _schema = schema;
        _resourceLedger = resourceLedger;
        _nativeFunctionPointerTypeKeyCount = schema.IsDevelopmentProfile()
            ? nativeFunctionPointerSignatureCount
            : CountNativeFunctionPointerRows();
        for (var row = 1; row <= recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            Validate(row);
        }
        _compactValueTypeKeys = CreateCompactValueTypeKeys();
    }

    public ReadOnlySpan<byte> GetRow(int row)
    {
        return GetRowCore(row);
    }

    private ReadOnlySpan<byte> GetRowCore(int row)
    {
        _resourceLedger?.ConsumeWork();
        return _rows.GetRow(row);
    }

    internal TrRmTypeSpecRefInfo GetTypeSpecRef(
        uint value, bool allowNull = false, bool allowInitQualifier = false)
    {
        _resourceLedger?.ConsumeWork();
        if (value == 0)
        {
            return allowNull ? default : throw InvalidType();
        }

        if ((value & 0xFF00_0000) == 0)
        {
            if (value > (uint)_recordCount)
            {
                throw InvalidType();
            }

            var row = checked((int)value);
            var data = GetRowCore(row);
            _resourceLedger?.ConsumeWork(2);
            _resourceLedger?.ConsumeWork(4);
            if (!allowInitQualifier && (data[1] & 0x04) != 0)
            {
                throw InvalidType();
            }

            _resourceLedger?.ConsumeWork(
                (data.Length > 2 ? 2UL : 0UL)
                + (data.Length > 3 ? 2UL : 0UL));
            _resourceLedger?.ConsumeWork();
            return new TrRmTypeSpecRefInfo(value, row, data[0], data[1],
                data.Length > 2 ? data[2] : (byte)0,
                data.Length > 3 ? data[3] : (byte)0);
        }

        if ((value & 0xFF00_0000) == 0xF200_0000)
        {
            if ((value & 0x00FF_FF00) != 0)
            {
                throw InvalidType();
            }

            var code = (byte)(value & 0xFF);
            if (code > 0x1A)
            {
                throw InvalidType();
            }

            return new TrRmTypeSpecRefInfo(value, 0,
                code == 0x1A ? (byte)0x0F : (byte)0x01, 0, code, 0);
        }

        if ((value & 0xFF00_0000) == 0xF300_0000)
        {
            var qualifiers = (byte)((value >> 16) & 0xFF);
            var familyCode = (byte)((value >> 8) & 0xFF);
            var argumentCode = (byte)(value & 0xFF);
            if (!IsAssignedInlineFamily(familyCode)
                || argumentCode > 0x19 || (qualifiers & 0xF8) != 0
                || (!allowInitQualifier && (qualifiers & 0x04) != 0)
                || ((qualifiers & 0x02) != 0 && familyCode is not (0x8B or 0x8D)))
            {
                throw InvalidType();
            }

            return new TrRmTypeSpecRefInfo(value, 0, 0x01,
                qualifiers, familyCode, argumentCode);
        }

        throw InvalidType();
    }

    internal ReadOnlySpan<byte> GetTypeSpecBytes(uint typeSpecRef,
        bool allowInitQualifier = false)
    {
        var reference = GetTypeSpecRef(typeSpecRef,
            allowInitQualifier: allowInitQualifier);
        if (reference.IsRow)
        {
            return GetRowCore(reference.Row);
        }

        if (reference.Form == 0x0F)
        {
            return s_existentialTypeSpec;
        }

        var isFamily = reference.TypeCode >= 0x80;
        var byteCount = isFamily ? 4 : 3;
        _resourceLedger?.ConsumeWork(checked((ulong)byteCount + 1UL));
        var bytes = new byte[byteCount];
        bytes[0] = 0x01;
        bytes[1] = reference.Qualifiers;
        bytes[2] = reference.TypeCode;
        if (isFamily)
        {
            bytes[3] = reference.ArgumentCode;
        }

        return bytes;
    }

    internal ushort GetCompactValueTypeKey(uint typeSpecRef)
    {
        var reference = GetTypeSpecRef(typeSpecRef);
        if (reference.IsRow)
        {
            return GetCompactValueTypeKey(reference.Row);
        }

        if (reference.Form == 0x01 && reference.TypeCode < 0x80
            && reference.TypeCode is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9
                or 0x0B or 0x0C or 0x0D or 0x0E or 0x14 or 0x15)
        {
            return reference.TypeCode;
        }

        if (reference.Form == 0x01 && reference.TypeCode == 0x8C
            && reference.ArgumentCode == 0 && reference.Qualifiers == 0)
        {
            return 0x8C;
        }

        for (var row = 1; row <= _recordCount; row++)
        {
            var data = GetRowCore(row);
            _resourceLedger?.ConsumeWork();
            if (data.Length != 4)
            {
                continue;
            }

            _resourceLedger?.ConsumeWork(8);
            var matches = (data[0] == 0x01)
                & (data[1] == reference.Qualifiers)
                & (data[2] == reference.TypeCode)
                & (data[3] == reference.ArgumentCode);
            if (!matches)
            {
                continue;
            }

            _resourceLedger?.ConsumeWork(2);
            var compactKey = _compactValueTypeKeys[row];
            if (compactKey != 0)
            {
                return compactKey;
            }
        }

        throw InvalidType();
    }

    internal bool AreEquivalentTypeSpecRefs(uint left, uint right)
    {
        var allowInitQualifier = false;
        ulong depth = 1;
        _resourceLedger?.ReserveWorkAtDepth(1, depth);
        while (true)
        {
            _resourceLedger?.ConsumeWork();
            _resourceLedger?.CheckDepthFromCurrent(depth);
            var leftReference = GetTypeSpecRef(left,
                allowInitQualifier: allowInitQualifier);
            var rightReference = GetTypeSpecRef(right,
                allowInitQualifier: allowInitQualifier);
            _resourceLedger?.ConsumeWork(2);
            if (leftReference.Form != rightReference.Form
                || leftReference.Qualifiers != rightReference.Qualifiers)
            {
                return false;
            }

            var leftRow = leftReference.IsRow ? GetRowCore(leftReference.Row) : default;
            var rightRow = rightReference.IsRow ? GetRowCore(rightReference.Row) : default;
            switch (leftReference.Form)
            {
                case 0x01:
                    _resourceLedger?.ConsumeWork(2);
                    return leftReference.TypeCode == rightReference.TypeCode
                        && leftReference.ArgumentCode == rightReference.ArgumentCode;

                case 0x02:
                case 0x03:
                case 0x04:
                    _resourceLedger?.ConsumeWork(2);
                    if (!leftReference.IsRow || !rightReference.IsRow)
                    {
                        return false;
                    }

                    var leftIdentity = Read(leftRow, 2);
                    var rightIdentity = Read(rightRow, 2);
                    _resourceLedger?.ConsumeWork();
                    return leftIdentity == rightIdentity;

                case 0x05:
                    _resourceLedger?.ConsumeWork(2);
                    return leftReference.IsRow && rightReference.IsRow
                        && RowsEqual(leftRow, rightRow);

                case >= 0x06 and <= 0x0B:
                case 0x0E:
                    _resourceLedger?.ConsumeWork(2);
                    if (!leftReference.IsRow || !rightReference.IsRow)
                    {
                        return false;
                    }

                    var leftChild = Read(leftRow, 2);
                    var rightChild = Read(rightRow, 2);
                    var childDepth = checked(depth + 1);
                    _resourceLedger?.ReserveWorkAtDepth(1, childDepth);
                    left = leftChild;
                    right = rightChild;
                    depth = childDepth;
                    allowInitQualifier = false;
                    break;

                case 0x0C:
                    _resourceLedger?.ConsumeWork(2);
                    if (!leftReference.IsRow || !rightReference.IsRow)
                    {
                        return false;
                    }

                    _resourceLedger?.ConsumeWork(3);
                    if (leftRow[2] != rightRow[2])
                    {
                        return false;
                    }

                    var leftFamilyChild = Read(leftRow, 3);
                    var rightFamilyChild = Read(rightRow, 3);
                    var familyChildDepth = checked(depth + 1);
                    _resourceLedger?.ReserveWorkAtDepth(1,
                        familyChildDepth);
                    left = leftFamilyChild;
                    right = rightFamilyChild;
                    depth = familyChildDepth;
                    allowInitQualifier = leftRow[2] == 0x8A;
                    break;

                case 0x0D:
                    _resourceLedger?.ConsumeWork(2);
                    return leftReference.IsRow && rightReference.IsRow
                        && RowsEqual(leftRow, rightRow);

                case 0x0F:
                    return true;

                case 0x10:
                    _resourceLedger?.ConsumeWork(2);
                    if (!leftReference.IsRow || !rightReference.IsRow)
                    {
                        return false;
                    }

                    var leftPredicateCapture = Read(leftRow, 2);
                    var rightPredicateCapture = Read(rightRow, 2);
                    _resourceLedger?.ConsumeWork();
                    return leftPredicateCapture == rightPredicateCapture;

                case 0x11:
                    _resourceLedger?.ConsumeWork(19);
                    if (!leftReference.IsRow || !rightReference.IsRow
                        || BinaryPrimitives.ReadUInt64LittleEndian(leftRow[6..])
                            != BinaryPrimitives.ReadUInt64LittleEndian(rightRow[6..]))
                    {
                        return false;
                    }

                    var leftElement = Read(leftRow, 2);
                    var rightElement = Read(rightRow, 2);
                    var elementDepth = checked(depth + 1);
                    _resourceLedger?.ReserveWorkAtDepth(1, elementDepth);
                    left = leftElement;
                    right = rightElement;
                    depth = elementDepth;
                    allowInitQualifier = false;
                    break;

                default:
                    return false;
            }
        }
    }

    internal int GetTypeSpecStructuralHash(uint typeSpecRef)
    {
        var hash = StructuralHashOffset;
        var allowInitQualifier = false;
        ulong depth = 1;
        _resourceLedger?.ReserveWorkAtDepth(1, depth);
        while (true)
        {
            _resourceLedger?.ConsumeWork();
            _resourceLedger?.CheckDepthFromCurrent(depth);
            var reference = GetTypeSpecRef(typeSpecRef,
                allowInitQualifier: allowInitQualifier);
            _resourceLedger?.ConsumeWork(2);
            hash = MixStructuralHash(hash, reference.Form);
            hash = MixStructuralHash(hash, reference.Qualifiers);
            if (!reference.IsRow)
            {
                _resourceLedger?.ConsumeWork(2);
                hash = MixStructuralHash(hash, reference.TypeCode);
                hash = MixStructuralHash(hash, reference.ArgumentCode);
                _resourceLedger?.ConsumeWork();
                return unchecked((int)hash);
            }

            var row = GetRow(reference.Row);
            switch (reference.Form)
            {
                case 0x01:
                    _resourceLedger?.ConsumeWork(2);
                    hash = MixStructuralHash(hash, reference.TypeCode);
                    hash = MixStructuralHash(hash, reference.ArgumentCode);
                    _resourceLedger?.ConsumeWork();
                    return unchecked((int)hash);

                case 0x02:
                case 0x03:
                case 0x04:
                    var identity = Read(row, 2);
                    _resourceLedger?.ConsumeWork();
                    hash = MixStructuralHash(hash, identity);
                    _resourceLedger?.ConsumeWork();
                    return unchecked((int)hash);

                case >= 0x06 and <= 0x0B:
                case 0x0E:
                    var unaryChild = Read(row, 2);
                    var unaryDepth = checked(depth + 1);
                    _resourceLedger?.ReserveWorkAtDepth(1, unaryDepth);
                    typeSpecRef = unaryChild;
                    depth = unaryDepth;
                    allowInitQualifier = false;
                    break;

                case 0x0C:
                    _resourceLedger?.ConsumeWork(2);
                    hash = MixStructuralHash(hash, row[2]);
                    var indexedChild = Read(row, 3);
                    var indexedDepth = checked(depth + 1);
                    _resourceLedger?.ReserveWorkAtDepth(1, indexedDepth);
                    typeSpecRef = indexedChild;
                    depth = indexedDepth;
                    allowInitQualifier = row[2] == 0x8A;
                    break;

                case 0x0F:
                    _resourceLedger?.ConsumeWork();
                    return unchecked((int)hash);

                case 0x10:
                    var predicateCapture = Read(row, 2);
                    _resourceLedger?.ConsumeWork();
                    hash = MixStructuralHash(hash, predicateCapture);
                    _resourceLedger?.ConsumeWork();
                    return unchecked((int)hash);

                case 0x11:
                    _resourceLedger?.ConsumeWork(sizeof(ulong) + 1);
                    var elementCount
                        = BinaryPrimitives.ReadUInt64LittleEndian(row[6..]);
                    _resourceLedger?.ConsumeWork(2);
                    hash = MixStructuralHash(hash, elementCount);
                    var arrayElement = Read(row, 2);
                    var arrayDepth = checked(depth + 1);
                    _resourceLedger?.ReserveWorkAtDepth(1, arrayDepth);
                    typeSpecRef = arrayElement;
                    depth = arrayDepth;
                    allowInitQualifier = false;
                    break;

                default:
                    for (var index = 2; index < row.Length; index++)
                    {
                        _resourceLedger?.ConsumeWork(2);
                        hash = MixStructuralHash(hash, row[index]);
                    }
                    _resourceLedger?.ConsumeWork();
                    return unchecked((int)hash);
            }
        }
    }

    private static uint MixStructuralHash(uint hash, byte value)
        => unchecked((hash ^ value) * StructuralHashPrime);

    private static uint MixStructuralHash(uint hash, uint value)
        => unchecked((hash ^ value) * StructuralHashPrime);

    private static uint MixStructuralHash(uint hash, ulong value)
    {
        hash = MixStructuralHash(hash, unchecked((uint)value));
        return MixStructuralHash(hash, unchecked((uint)(value >> 32)));
    }

    private bool RowsEqual(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        _resourceLedger?.ConsumeWork();
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            _resourceLedger?.ConsumeWork(3);
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    internal bool TryGetFixedInlineArray(uint typeSpecRef,
        out uint elementTypeSpecRef, out ulong elementCount)
    {
        var type = GetTypeSpecRef(typeSpecRef);
        if (_schema.UsesForeignInvocationStorageFixedArrays()
            && type.IsRow && type.Form == 0x11 && type.Qualifiers == 0)
        {
            var row = GetRowCore(type.Row);
            elementTypeSpecRef = Read(row, 2);
            _resourceLedger?.ConsumeWork(sizeof(ulong) + 1);
            elementCount = BinaryPrimitives.ReadUInt64LittleEndian(row[6..]);
            return true;
        }

        elementTypeSpecRef = 0;
        elementCount = 0;
        return false;
    }

    internal int GetNominalTypeDefRow(uint typeSpecRef,
        TrRmTypeDefTableReader typeDefs, TrRmImportTableReader imports)
    {
        var typeSpec = GetTypeSpecRef(typeSpecRef);
        if (!typeSpec.IsRow)
        {
            throw InvalidType();
        }

        var data = GetRowCore(typeSpec.Row);
        _resourceLedger?.ConsumeWork(2);
        if (typeSpec.Form == 0x05)
        {
            var genericTypeSpec = GetTypeSpecRef(Read(data, 2));
            if (!genericTypeSpec.IsRow || genericTypeSpec.Form is not (0x02 or 0x03))
            {
                throw InvalidType();
            }

            data = GetRowCore(genericTypeSpec.Row);
        }

        _resourceLedger?.ConsumeWork(2);
        var definition = data[0] switch
        {
            0x02 => 0x1300_0000u | Read(data, 2),
            0x03 => imports.GetExpectedDefinitionForImportRow(checked((int)Read(data, 2))),
            _ => throw InvalidType(),
        };
        _resourceLedger?.ConsumeWork(5);
        var typeDefRow = checked((int)(definition & 0x00FF_FFFF));
        if ((definition >> 24) != 0x13 || typeDefRow <= 0 || typeDefRow > typeDefs.Count)
        {
            throw InvalidType();
        }

        return typeDefRow;
    }

    public int Count => _recordCount;
    /// <summary>The number of native function-pointer signatures in the table.</summary>
    public int NativeFunctionPointerSignatureCount => _nativeFunctionPointerSignatureCount;
    internal int NativeFunctionPointerTypeKeyCount => _nativeFunctionPointerTypeKeyCount;
    internal TrRmArtifactSchema Schema => _schema;
    internal TrRmA3LayoutResourceLedger? ResourceLedger => _resourceLedger;

    internal bool TryGetNativeFunctionPointerAnchor(int typeSpecRow,
        out byte callingConvention, out int methodDefRow)
    {
        callingConvention = 0;
        methodDefRow = 0;
        _resourceLedger?.ConsumeWork(2);
        if (_schema.IsDevelopmentProfile()
            || typeSpecRow <= 0 || typeSpecRow > _recordCount)
        {
            return false;
        }

        var type = GetRowCore(typeSpecRow);
        var typeForm = ReadByte(type, 0);
        _resourceLedger?.ConsumeWork();
        if (typeForm != 0x0D || type.Length != 7)
        {
            return false;
        }

        callingConvention = ReadByte(type, 2);
        methodDefRow = checked((int)Read(type, 3));
        return true;
    }

    internal int GetConstructedArgumentCount(int row)
    {
        var bytes = GetRowCore(row);
        _resourceLedger?.ConsumeWork(2);
        if (bytes[0] != 0x05)
        {
            throw InvalidType();
        }

        _resourceLedger?.ConsumeWork();
        return (bytes.Length - 6) / sizeof(uint);
    }

    internal int GetPredicateCaptureReference(int row)
    {
        var bytes = GetRowCore(row);
        _resourceLedger?.ConsumeWork(2);
        if (bytes[0] != 0x10)
        {
            throw InvalidType();
        }

        return checked((int)Read(bytes, 2));
    }

    internal uint GetConstructedArgument(int row, int ordinal)
    {
        var bytes = GetRowCore(row);
        var count = GetConstructedArgumentCount(row);
        _resourceLedger?.ConsumeWork();
        if ((uint)ordinal >= (uint)count)
        {
            throw InvalidType();
        }

        return Read(bytes, 6 + (ordinal * sizeof(uint)));
    }

    internal bool ContainsNativeFunctionPointerValue(uint typeSpecRef,
        TrRmTypeDefTableReader typeDefs, TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports)
    {
        _resourceLedger?.ConsumeWork(2);
        _resourceLedger?.ReserveWorkAtDepth(1, 1);
        var pending = new Stack<(uint TypeSpecRef, ulong Depth)>();
        var visitedRows = new HashSet<int>();
        pending.Push((typeSpecRef, 1));
        while (pending.TryPop(out var current))
        {
            _resourceLedger?.ConsumeWork();
            _resourceLedger?.CheckDepthFromCurrent(current.Depth);
            var reference = GetTypeSpecRef(current.TypeSpecRef,
                allowInitQualifier: true);
            if (reference.Form == 0x0D)
            {
                return true;
            }

            if (!reference.IsRow)
            {
                continue;
            }

            _resourceLedger?.ConsumeWork(2);
            if (!visitedRows.Add(reference.Row))
            {
                continue;
            }

            var type = GetRow(reference.Row);
            switch (reference.Form)
            {
                case >= 0x06 and <= 0x0B:
                case 0x0E:
                    var unaryChild = Read(type, 2);
                    var unaryDepth = checked(current.Depth + 1);
                    _resourceLedger?.ReserveWorkAtDepth(1, unaryDepth);
                    pending.Push((unaryChild, unaryDepth));
                    break;
                case 0x0C:
                    var indexedChild = Read(type, 3);
                    var indexedDepth = checked(current.Depth + 1);
                    _resourceLedger?.ReserveWorkAtDepth(1, indexedDepth);
                    pending.Push((indexedChild, indexedDepth));
                    break;
                case 0x11:
                    var arrayChild = Read(type, 2);
                    var arrayDepth = checked(current.Depth + 1);
                    _resourceLedger?.ReserveWorkAtDepth(1, arrayDepth);
                    pending.Push((arrayChild, arrayDepth));
                    break;
                case 0x05:
                    var typeDef = GetNominalTypeDefRow(current.TypeSpecRef,
                        typeDefs, imports);
                    var (formalFirst, formalCount) = typeDefs.GetGenericRange(typeDef);
                    var argumentCount = GetConstructedArgumentCount(reference.Row);
                    if (argumentCount != formalCount)
                    {
                        throw InvalidType();
                    }

                    for (var ordinal = argumentCount - 1; ordinal >= 0; ordinal--)
                    {
                        var formalKind = genericParams.GetFormalKind(
                            formalFirst + ordinal);
                        if (formalKind == 1)
                        {
                            var argument = GetConstructedArgument(reference.Row,
                                ordinal);
                            var argumentDepth = checked(current.Depth + 1);
                            _resourceLedger?.ReserveWorkAtDepth(1,
                                argumentDepth);
                            pending.Push((argument, argumentDepth));
                        }
                        else if (formalKind != 2)
                        {
                            throw InvalidType();
                        }
                    }
                    break;
            }
        }

        return false;
    }

    internal ushort GetCompactValueTypeKey(int row)
    {
        _resourceLedger?.ConsumeWork(2);
        if (row <= 0 || row >= _compactValueTypeKeys.Length
            || _compactValueTypeKeys[row] == 0)
        {
            throw InvalidType();
        }

        return _compactValueTypeKeys[row];
    }

    internal bool TryGetNativeFunctionPointerSignatureRow(ushort typeKey, out int signatureRow)
    {
        _resourceLedger?.ConsumeWork(4);
        var index = (int)typeKey - NativeFunctionPointerTypeKeyStart;
        if (_schema.IsDevelopmentProfile()
            && index >= 0 && index < _nativeFunctionPointerSignatureCount
            && index < ushort.MaxValue - NativeFunctionPointerTypeKeyStart + 1)
        {
            signatureRow = index + 1;
            return true;
        }

        signatureRow = 0;
        return false;
    }

    internal bool TryGetNativeFunctionPointerTypeSpecRow(ushort typeKey,
        out int typeSpecRow)
    {
        _resourceLedger?.ConsumeWork(4);
        if (!_schema.IsDevelopmentProfile())
        {
            var index = (int)typeKey - NativeFunctionPointerTypeKeyStart;
            if (index >= 0 && index < _nativeFunctionPointerTypeKeyCount
                && index < ushort.MaxValue - NativeFunctionPointerTypeKeyStart + 1)
            {
                for (var row = 1; row <= _recordCount; row++)
                {
                    var data = GetRowCore(row);
                    _resourceLedger?.ConsumeWork(4);
                    var isFunctionPointer = data[0] == 0x0D;
                    var matchesTypeKey = _compactValueTypeKeys[row] == typeKey;
                    if (isFunctionPointer && matchesTypeKey)
                    {
                        typeSpecRow = row;
                        return true;
                    }
                }
            }
        }

        typeSpecRow = 0;
        return false;
    }

    internal int NativeFunctionPointerTypeKeyEnd
    {
        get
        {
            _resourceLedger?.ConsumeWork(3);
            return Math.Min(ushort.MaxValue + 1,
                NativeFunctionPointerTypeKeyStart
                    + _nativeFunctionPointerTypeKeyCount);
        }
    }

    internal bool IsNativePointerTypeKey(ushort typeKey)
    {
        _resourceLedger?.ConsumeWork(2);
        return typeKey == 0x8C || typeKey >= NativeFunctionPointerTypeKeyEnd;
    }

    private ushort[] CreateCompactValueTypeKeys()
    {
        var entryCount = checked((ulong)_recordCount + 1);
        _resourceLedger?.ConsumeWork(checked(2UL
            + (entryCount * (sizeof(ushort) + sizeof(long) + 2UL))));
        var keys = new ushort[_recordCount + 1];
        var identities = new long[_recordCount + 1];
        _resourceLedger?.ConsumeWork(3);
        var pointerIdentities = new Dictionary<(long Pointee, byte Qualifiers),
            (long Identity, ushort TypeKey)>();
        var fixedArrayIdentities = new Dictionary<(long Element, ulong Count), long>();
        var standardFunctionPointerKeys = new Dictionary<(byte Convention, uint Anchor),
            ushort>();
        var nextPointerIdentity = 0;
        var nextFixedArrayIdentity = 0;
        var nextFunctionPointerTypeKey = 0;
        var nextPointerTypeKey = 0;
        var firstPointerTypeKey = NativeFunctionPointerTypeKeyStart
            + _nativeFunctionPointerTypeKeyCount;
        var pointerTypeKeyCapacity = Math.Max(0, ushort.MaxValue - firstPointerTypeKey + 1);
        var reader = this;

        for (var row = 1; row <= _recordCount; row++)
        {
            _resourceLedger?.ConsumeWork(1UL + sizeof(long) + sizeof(ushort));
            var bytes = GetRowCore(row);
            _resourceLedger?.ConsumeWork(2);
            if (bytes[0] == 0x01 && bytes.Length == 3 && bytes[1] == 0)
            {
                var code = bytes[2];
                identities[row] = AtomicIdentity(code);
                if (IsCompactAtom(code))
                {
                    keys[row] = code;
                }
            }
            else if (TryGetNativePointer(reader, bytes, identities,
                out var pointeeIdentity, out var qualifiers))
            {
                _resourceLedger?.ConsumeWork(3);
                if (!pointerIdentities.TryGetValue((pointeeIdentity, qualifiers),
                    out var pointerIdentity))
                {
                    var identity = TypeIdentityBase + 0x30000 + ++nextPointerIdentity;
                    ushort typeKey = 0;
                    if (pointeeIdentity != AtomicIdentity(0) || qualifiers != 0)
                    {
                        if (nextPointerTypeKey < pointerTypeKeyCapacity)
                        {
                            typeKey = (ushort)(firstPointerTypeKey + nextPointerTypeKey);
                        }
                        nextPointerTypeKey++;
                    }
                    else
                    {
                        typeKey = 0x8C;
                    }

                    pointerIdentity = (identity, typeKey);
                    _resourceLedger?.ConsumeWork(1UL + sizeof(long)
                        + sizeof(byte) + sizeof(long) + sizeof(ushort));
                    pointerIdentities.Add((pointeeIdentity, qualifiers), pointerIdentity);
                }

                identities[row] = pointerIdentity.Identity;
                keys[row] = pointerIdentity.TypeKey;
            }
            else if (bytes[0] == 0x01 && bytes.Length == 4)
            {
                identities[row] = FamilyIdentity(bytes[1], bytes[2], bytes[3]);
            }
            else if (bytes[0] == 0x06 && bytes.Length == 6)
            {
                var innerReference = GetTypeSpecRef(Read(bytes, 2));
                if (innerReference.Form == 0x01 && innerReference.Qualifiers == 0
                    && innerReference.TypeCode < 0x80
                    && IsCompactOptionalAtom(innerReference.TypeCode))
                {
                    identities[row] = TypeIdentityBase + 0x10000
                        + innerReference.TypeCode;
                    keys[row] = (ushort)(0x100 | innerReference.TypeCode);
                }
            }
            else if (bytes[0] == 0x0F && bytes.Length == 2)
            {
                identities[row] = TypeIdentityBase + 0x40000;
            }
            else if (bytes[0] == 0x11 && bytes.Length == 14 && bytes[1] == 0)
            {
                var elementTypeSpecRef = Read(bytes, 2);
                var elementReference = GetTypeSpecRef(elementTypeSpecRef);
                var elementIdentity = elementReference.IsRow
                    ? identities[elementReference.Row]
                    : TypeIdentity(elementReference);
                if (elementIdentity != 0)
                {
                    _resourceLedger?.ConsumeWork(sizeof(ulong) + 1);
                    var identityKey = (elementIdentity,
                        BinaryPrimitives.ReadUInt64LittleEndian(bytes[6..]));
                    _resourceLedger?.ConsumeWork(3);
                    if (!fixedArrayIdentities.TryGetValue(identityKey,
                        out var identity))
                    {
                        identity = TypeIdentityBase + 0x60000
                            + ++nextFixedArrayIdentity;
                        _resourceLedger?.ConsumeWork(1UL + sizeof(long)
                            + sizeof(ulong) + sizeof(long));
                        fixedArrayIdentities.Add(identityKey, identity);
                    }

                    identities[row] = identity;
                }
            }
            else if (bytes[0] is 0x02 or 0x03 && bytes.Length == 6 && bytes[1] == 0)
            {
                identities[row] = TypeIdentityBase + ((long)bytes[0] << 32)
                    + Read(bytes, 2);
            }
            else if (_schema.IsDevelopmentProfile()
                && bytes[0] == 0x0D && bytes.Length == 6 && bytes[1] == 0)
            {
                var signature = Read(bytes, 2);
                identities[row] = TypeIdentityBase + 0x20000 + signature;
                var typeKey = NativeFunctionPointerTypeKeyStart + signature - 1;
                if (typeKey <= ushort.MaxValue)
                {
                    keys[row] = (ushort)typeKey;
                }
            }
            else if (!_schema.IsDevelopmentProfile()
                && bytes[0] == 0x0D && bytes.Length == 7 && bytes[1] == 0)
            {
                identities[row] = TypeIdentityBase + 0x20000 + Read(bytes, 3);
                var identity = (bytes[2], Read(bytes, 3));
                _resourceLedger?.ConsumeWork(3);
                if (!standardFunctionPointerKeys.TryGetValue(identity, out var typeKey))
                {
                    var assignedTypeKey = NativeFunctionPointerTypeKeyStart
                        + nextFunctionPointerTypeKey++;
                    if (assignedTypeKey <= ushort.MaxValue)
                    {
                        typeKey = (ushort)assignedTypeKey;
                        _resourceLedger?.ConsumeWork(1UL + sizeof(byte)
                            + sizeof(uint) + sizeof(ushort));
                        standardFunctionPointerKeys.Add(identity, typeKey);
                    }
                    else
                    {
                        typeKey = 0;
                    }
                }

                keys[row] = typeKey;
            }
        }

        return keys;

        static bool TryGetNativePointer(TrRmTypeSpecTableReader reader,
            ReadOnlySpan<byte> bytes, long[] identities,
            out long pointeeIdentity, out byte qualifiers)
        {
            pointeeIdentity = 0;
            qualifiers = 0;
            if (bytes.Length == 4 && bytes[0] == 0x01 && bytes[2] == 0x8C)
            {
                pointeeIdentity = AtomicIdentity(bytes[3]);
                qualifiers = bytes[1];
                return pointeeIdentity != 0;
            }
            if (bytes.Length == 7 && bytes[0] == 0x0C && bytes[2] == 0x8C)
            {
                var pointee = reader.GetTypeSpecRef(reader.Read(bytes, 3),
                    allowInitQualifier: true);
                if (pointee.IsRow)
                {
                    var pointeeRow = pointee.Row;
                    pointeeIdentity = pointeeRow > 0 && pointeeRow < identities.Length
                        ? identities[pointeeRow] : 0;
                }
                else
                {
                    pointeeIdentity = TypeIdentity(pointee);
                }

                qualifiers = bytes[1];
                return pointeeIdentity != 0;
            }

            return false;
        }

        static long AtomicIdentity(byte code)
        {
            return IsCompactAtom(code) || code == 0
                ? TypeIdentityBase + code + 1
                : 0;
        }

        static bool IsCompactAtom(byte code)
        {
            return code is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9
                or 0x0B or 0x0C or 0x0D or 0x0E or 0x14 or 0x15;
        }

        static bool IsCompactOptionalAtom(byte code)
        {
            return code is 1 or 3 or 8 or 9 or 0x0D or 0x0E;
        }

        static long FamilyIdentity(byte qualifiers, byte familyCode, byte argumentCode)
        {
            return TypeIdentityBase + 0x50000 + ((long)qualifiers << 16)
                + ((long)familyCode << 8) + argumentCode;
        }

        static long TypeIdentity(TrRmTypeSpecRefInfo reference)
        {
            return reference.Form switch
            {
                0x01 when reference.TypeCode < 0x80
                    => AtomicIdentity(reference.TypeCode),
                0x01 => FamilyIdentity(reference.Qualifiers,
                    reference.TypeCode, reference.ArgumentCode),
                0x0F => TypeIdentityBase + 0x40000,
                _ => 0,
            };
        }
    }

    private void Validate(int row)
    {
        var bytes = GetRowCore(row);
        var qualifiers = ReadByte(bytes, 1);
        _resourceLedger?.ConsumeWork();
        if ((qualifiers & ~7) != 0)
        {
            throw InvalidType();
        }

        var form = ReadByte(bytes, 0);
        switch (form)
        {
            case 0x01:
                var code = ReadByte(bytes, 2);
                _resourceLedger?.ConsumeWork();
                if (code <= 0x19)
                {
                    RequireLength(bytes, 3);
                    _resourceLedger?.ConsumeWork();
                    if ((qualifiers & ~0x04) != 0)
                    {
                        throw InvalidType();
                    }
                }
                else
                {
                    _resourceLedger?.ConsumeWork();
                    if (IsAssignedInlineFamily(code))
                    {
                        RequireLength(bytes, 4);
                        var argumentCode = ReadByte(bytes, 3);
                        _resourceLedger?.ConsumeWork(2);
                        if (argumentCode > 0x19
                            || ((qualifiers & 0x02) != 0
                                && code is not (0x8B or 0x8D)))
                        {
                            throw InvalidType();
                        }
                    }
                    else
                    {
                        throw InvalidType();
                    }
                }
                break;

            case 0x02:
                ValidateTypedReference(bytes, _typeDefCount);
                break;
            case 0x03:
                ValidateTypedReference(bytes, _importCount);
                break;
            case 0x04:
                ValidateTypedReference(bytes, _genericParamCount);
                break;
            case 0x05:
                _resourceLedger?.ConsumeWork(2);
                if (bytes.Length < 10 || (bytes.Length - 6) % sizeof(uint) != 0)
                {
                    throw InvalidType();
                }
                var generic = Read(bytes, 2);
                RequireEarlier(generic, row);
                var genericType = ReadByte(GetRowCore((int)generic), 0);
                _resourceLedger?.ConsumeWork();
                if (genericType is not (0x02 or 0x03))
                {
                    throw InvalidType();
                }

                for (var offset = 6; offset < bytes.Length; offset += sizeof(uint))
                {
                    _resourceLedger?.ConsumeWork();
                    var argumentWord = Read(bytes, offset);
                    _resourceLedger?.ConsumeWork();
                    if (argumentWord == 0)
                    {
                        throw InvalidType();
                    }

                    _resourceLedger?.ConsumeWork();
                    if ((argumentWord & 0xFF00_0000) != 0)
                    {
                        _resourceLedger?.ConsumeWork();
                        if ((argumentWord & 0xFF00_0000) is not (0xF200_0000 or 0xF300_0000))
                        {
                            throw InvalidType();
                        }

                        _ = GetTypeSpecRef(argumentWord);
                    }
                }
                break;
            case >= 0x06 and <= 0x0B:
                RequireLength(bytes, 6);
                var inner = Read(bytes, 2);
                var innerReference = GetTypeSpecRef(inner);
                if (innerReference.IsRow)
                {
                    RequireEarlier(inner, row);
                }
                if (bytes[0] != 0x06 && IsNoncanonicalAtomic(innerReference))
                {
                    throw InvalidType();
                }
                break;
            case 0x0C:
                RequireLength(bytes, 7);
                var familyCode = ReadByte(bytes, 2);
                _resourceLedger?.ConsumeWork();
                if (!IsIndexedFamily(familyCode))
                {
                    throw InvalidType();
                }
                var argument = Read(bytes, 3);
                var familyArgument = GetTypeSpecRef(argument,
                    allowInitQualifier: familyCode == 0x8A);
                if (familyArgument.IsRow)
                {
                    RequireEarlier(argument, row);
                }
                if (IsNoncanonicalAtomic(familyArgument))
                {
                    throw InvalidType();
                }
                break;
            case 0x0E:
                RequireLength(bytes, 6);
                _resourceLedger?.ConsumeWork();
                if ((qualifiers & ~1) != 0)
                {
                    throw InvalidType();
                }
                var initInner = Read(bytes, 2);
                var initReference = GetTypeSpecRef(initInner);
                if (initReference.IsRow)
                {
                    RequireEarlier(initInner, row);
                }
                break;
            case 0x0F:
                RequireLength(bytes, 2);
                _resourceLedger?.ConsumeWork();
                if (qualifiers != 0)
                {
                    throw InvalidType();
                }
                break;
            case 0x10:
                RequireLength(bytes, 6);
                var capture = Read(bytes, 2);
                _resourceLedger?.ConsumeWork(5);
                if (!_schema.UsesGeneralPredicateGraphs() || qualifiers != 0
                    || capture is 0 or > 0xFF_FFFF
                    || capture > (uint)_predicateCaptureCount)
                {
                    throw InvalidType();
                }
                break;
            case 0x11:
                RequireLength(bytes, 14);
                _resourceLedger?.ConsumeWork(2);
                if (!_schema.UsesForeignInvocationStorageFixedArrays()
                    || qualifiers != 0)
                {
                    throw InvalidType();
                }
                var elementTypeSpecRef = Read(bytes, 2);
                var elementReference = GetTypeSpecRef(elementTypeSpecRef);
                if (elementReference.IsRow)
                {
                    RequireEarlier(elementTypeSpecRef, row);
                }
                break;
            case 0x0D:
                _resourceLedger?.ConsumeWork(1);
                if (qualifiers != 0)
                {
                    throw InvalidType();
                }
                if (_schema.IsDevelopmentProfile())
                {
                    RequireLength(bytes, 6);
                    var signature = Read(bytes, 2);
                    if (signature == 0
                        || signature > _nativeFunctionPointerSignatureCount)
                    {
                        throw InvalidType();
                    }
                }
                else
                {
                    RequireLength(bytes, 7);
                    var methodDef = Read(bytes, 3);
                    var convention = ReadByte(bytes, 2);
                    _resourceLedger?.ConsumeWork(3);
                    if (convention > 4 || methodDef == 0
                        || methodDef > _methodDefCount)
                    {
                        throw InvalidType();
                    }
                }
                break;
            default:
                throw InvalidType();
        }
    }

    private int CountNativeFunctionPointerRows()
    {
        var count = 0;
        for (var row = 1; row <= _recordCount; row++)
        {
            var data = GetRowCore(row);
            _resourceLedger?.ConsumeWork(2);
            if (data[0] == 0x0D)
            {
                count++;
            }
        }

        return count;
    }

    private void ValidateTypedReference(ReadOnlySpan<byte> bytes,
        int tableCount)
    {
        RequireLength(bytes, 6);
        var reference = Read(bytes, 2);
        _resourceLedger?.ConsumeWork(2);
        if (reference == 0 || reference > tableCount)
        {
            throw InvalidType();
        }
    }

    private static bool IsIndexedFamily(byte familyCode)
        => IsAssignedInlineFamily(familyCode);

    private bool IsNoncanonicalAtomic(TrRmTypeSpecRefInfo reference)
    {
        _resourceLedger?.ConsumeWork(4);
        return reference.IsRow && reference.Form == 0x01
            && reference.TypeCode < 0x80 && reference.Qualifiers == 0;
    }

    private void RequireEarlier(uint reference, int row)
    {
        _resourceLedger?.ConsumeWork(2);
        if (reference == 0 || reference >= row)
        {
            throw InvalidType();
        }
    }

    private uint Read(ReadOnlySpan<byte> bytes, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset, sizeof(uint)));
    }

    private byte ReadByte(ReadOnlySpan<byte> bytes, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(byte) + 1);
        return bytes[offset];
    }

    private void RequireLength(ReadOnlySpan<byte> bytes, int length)
    {
        _resourceLedger?.ConsumeWork();
        if (bytes.Length != length)
        {
            throw InvalidType();
        }
    }

    private static bool IsAssignedInlineFamily(byte familyCode)
        => familyCode is (>= 0x80 and <= 0x84) or 0x8A or (>= 0x8B and <= 0x8F);

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static InvalidDataException InvalidType()
        => new("Invalid TRRM TypeSpec row.");
}
