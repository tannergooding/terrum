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
    private static readonly byte[] s_existentialTypeSpec = [0x0F, 0];

    private readonly TrRmVariableTableIndex _rows;
    private readonly int _recordCount;
    private readonly int _typeDefCount;
    private readonly int _importCount;
    private readonly int _genericParamCount;
    private readonly int _nativeFunctionPointerSignatureCount;
    private readonly int _nativeFunctionPointerTypeKeyCount;
    private readonly int _methodDefCount;
    private readonly TrRmArtifactSchema _schema;
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
        int methodDefCount = 0)
    {
        if (!ValidCount(typeDefCount) || !ValidCount(importCount)
            || !ValidCount(genericParamCount)
            || !ValidCount(nativeFunctionPointerSignatureCount))
        {
            throw InvalidType();
        }
        if (!ValidCount(methodDefCount)
            || schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.Development or TrRmArtifactSchema.CopyOriginDraft)
            || (schema != TrRmArtifactSchema.Development
                && nativeFunctionPointerSignatureCount != 0))
        {
            throw InvalidType();
        }

        _rows = new TrRmVariableTableIndex(section, recordCount, 2, 1);
        _recordCount = recordCount;
        _typeDefCount = typeDefCount;
        _importCount = importCount;
        _genericParamCount = genericParamCount;
        _nativeFunctionPointerSignatureCount = nativeFunctionPointerSignatureCount;
        _methodDefCount = methodDefCount;
        _schema = schema;
        _nativeFunctionPointerTypeKeyCount = schema == TrRmArtifactSchema.Development
            ? nativeFunctionPointerSignatureCount
            : CountNativeFunctionPointerRows();
        for (var row = 1; row <= recordCount; row++)
        {
            Validate(row);
        }
        _compactValueTypeKeys = CreateCompactValueTypeKeys();
    }

    public ReadOnlySpan<byte> GetRow(int row)
        => _rows.GetRow(row);

    internal TrRmTypeSpecRefInfo GetTypeSpecRef(
        uint value, bool allowNull = false, bool allowInitQualifier = false)
    {
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
            var data = _rows.GetRow(row);
            if (!allowInitQualifier && (data[1] & 0x04) != 0)
            {
                throw InvalidType();
            }

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
            return _rows.GetRow(reference.Row);
        }

        if (reference.Form == 0x0F)
        {
            return s_existentialTypeSpec;
        }

        var isFamily = reference.TypeCode >= 0x80;
        var bytes = new byte[isFamily ? 4 : 3];
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
            var data = _rows.GetRow(row);
            if (data.Length == 4 && data[0] == 0x01
                && data[1] == reference.Qualifiers
                && data[2] == reference.TypeCode
                && data[3] == reference.ArgumentCode
                && _compactValueTypeKeys[row] != 0)
            {
                return _compactValueTypeKeys[row];
            }
        }

        throw InvalidType();
    }

    internal bool AreEquivalentTypeSpecRefs(uint left, uint right)
    {
        var allowInitQualifier = false;
        while (true)
        {
            var leftReference = GetTypeSpecRef(left,
                allowInitQualifier: allowInitQualifier);
            var rightReference = GetTypeSpecRef(right,
                allowInitQualifier: allowInitQualifier);
            if (leftReference.Form != rightReference.Form
                || leftReference.Qualifiers != rightReference.Qualifiers)
            {
                return false;
            }

            var leftRow = leftReference.IsRow ? _rows.GetRow(leftReference.Row) : default;
            var rightRow = rightReference.IsRow ? _rows.GetRow(rightReference.Row) : default;
            switch (leftReference.Form)
            {
                case 0x01:
                    return leftReference.TypeCode == rightReference.TypeCode
                        && leftReference.ArgumentCode == rightReference.ArgumentCode;

                case 0x02:
                case 0x03:
                case 0x04:
                    return leftReference.IsRow && rightReference.IsRow
                        && Read(leftRow, 2) == Read(rightRow, 2);

                case 0x05:
                    return leftReference.IsRow && rightReference.IsRow
                        && leftRow.SequenceEqual(rightRow);

                case >= 0x06 and <= 0x0B:
                case 0x0E:
                    if (!leftReference.IsRow || !rightReference.IsRow)
                    {
                        return false;
                    }
                    left = Read(leftRow, 2);
                    right = Read(rightRow, 2);
                    allowInitQualifier = false;
                    break;

                case 0x0C:
                    if (!leftReference.IsRow || !rightReference.IsRow
                        || leftRow[2] != rightRow[2])
                    {
                        return false;
                    }
                    left = Read(leftRow, 3);
                    right = Read(rightRow, 3);
                    allowInitQualifier = leftRow[2] == 0x8A;
                    break;

                case 0x0D:
                    return leftReference.IsRow && rightReference.IsRow
                        && leftRow.SequenceEqual(rightRow);

                case 0x0F:
                    return true;

                default:
                    return false;
            }
        }
    }

    internal int GetNominalTypeDefRow(uint typeSpecRef,
        TrRmTypeDefTableReader typeDefs, TrRmImportTableReader imports)
    {
        var typeSpec = GetTypeSpecRef(typeSpecRef);
        if (!typeSpec.IsRow)
        {
            throw InvalidType();
        }

        var data = _rows.GetRow(typeSpec.Row);
        if (typeSpec.Form == 0x05)
        {
            var genericTypeSpec = GetTypeSpecRef(Read(data, 2));
            if (!genericTypeSpec.IsRow || genericTypeSpec.Form is not (0x02 or 0x03))
            {
                throw InvalidType();
            }

            data = _rows.GetRow(genericTypeSpec.Row);
        }

        var definition = data[0] switch
        {
            0x02 => 0x1300_0000u | Read(data, 2),
            0x03 => imports.GetExpectedDefinitionForImportRow(checked((int)Read(data, 2))),
            _ => throw InvalidType(),
        };
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

    internal bool TryGetNativeFunctionPointerAnchor(int typeSpecRow,
        out byte callingConvention, out int methodDefRow)
    {
        callingConvention = 0;
        methodDefRow = 0;
        if (_schema == TrRmArtifactSchema.Development
            || typeSpecRow <= 0 || typeSpecRow > _recordCount)
        {
            return false;
        }

        var type = _rows.GetRow(typeSpecRow);
        if (type[0] != 0x0D || type.Length != 7)
        {
            return false;
        }

        callingConvention = type[2];
        methodDefRow = checked((int)Read(type, 3));
        return true;
    }

    internal int GetConstructedArgumentCount(int row)
    {
        var bytes = _rows.GetRow(row);
        if (bytes[0] != 0x05)
        {
            throw InvalidType();
        }

        return (bytes.Length - 6) / sizeof(uint);
    }

    internal uint GetConstructedArgument(int row, int ordinal)
    {
        var bytes = _rows.GetRow(row);
        var count = GetConstructedArgumentCount(row);
        if ((uint)ordinal >= (uint)count)
        {
            throw InvalidType();
        }

        return Read(bytes, 6 + ordinal * sizeof(uint));
    }

    internal ushort GetCompactValueTypeKey(int row)
    {
        if (row <= 0 || row >= _compactValueTypeKeys.Length
            || _compactValueTypeKeys[row] == 0)
        {
            throw InvalidType();
        }

        return _compactValueTypeKeys[row];
    }

    internal bool TryGetNativeFunctionPointerSignatureRow(ushort typeKey, out int signatureRow)
    {
        var index = (int)typeKey - NativeFunctionPointerTypeKeyStart;
        if (_schema == TrRmArtifactSchema.Development
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
        if (_schema != TrRmArtifactSchema.Development)
        {
            var index = (int)typeKey - NativeFunctionPointerTypeKeyStart;
            if (index >= 0 && index < _nativeFunctionPointerTypeKeyCount
                && index < ushort.MaxValue - NativeFunctionPointerTypeKeyStart + 1)
            {
                for (var row = 1; row <= _recordCount; row++)
                {
                    if (_rows.GetRow(row)[0] == 0x0D
                        && _compactValueTypeKeys[row] == typeKey)
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
        => Math.Min(ushort.MaxValue + 1,
            NativeFunctionPointerTypeKeyStart + _nativeFunctionPointerTypeKeyCount);

    internal bool IsNativePointerTypeKey(ushort typeKey)
        => typeKey == 0x8C || typeKey >= NativeFunctionPointerTypeKeyEnd;

    private ushort[] CreateCompactValueTypeKeys()
    {
        var keys = new ushort[_recordCount + 1];
        var identities = new long[_recordCount + 1];
        var pointerIdentities = new Dictionary<(long Pointee, byte Qualifiers),
            (long Identity, ushort TypeKey)>();
        var standardFunctionPointerKeys = new Dictionary<(byte Convention, uint Anchor),
            ushort>();
        var nextPointerIdentity = 0;
        var nextFunctionPointerTypeKey = 0;
        var nextPointerTypeKey = 0;
        var firstPointerTypeKey = NativeFunctionPointerTypeKeyStart
            + _nativeFunctionPointerTypeKeyCount;
        var pointerTypeKeyCapacity = Math.Max(0, ushort.MaxValue - firstPointerTypeKey + 1);
        var reader = this;

        for (var row = 1; row <= _recordCount; row++)
        {
            var bytes = _rows.GetRow(row);
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
            else if (bytes[0] is 0x02 or 0x03 && bytes.Length == 6 && bytes[1] == 0)
            {
                identities[row] = TypeIdentityBase + ((long)bytes[0] << 32)
                    + Read(bytes, 2);
            }
            else if (_schema == TrRmArtifactSchema.Development
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
            else if (_schema != TrRmArtifactSchema.Development
                && bytes[0] == 0x0D && bytes.Length == 7 && bytes[1] == 0)
            {
                identities[row] = TypeIdentityBase + 0x20000 + Read(bytes, 3);
                var identity = (bytes[2], Read(bytes, 3));
                if (!standardFunctionPointerKeys.TryGetValue(identity, out var typeKey))
                {
                    var assignedTypeKey = NativeFunctionPointerTypeKeyStart
                        + nextFunctionPointerTypeKey++;
                    if (assignedTypeKey <= ushort.MaxValue)
                    {
                        typeKey = (ushort)assignedTypeKey;
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
                var pointee = reader.GetTypeSpecRef(Read(bytes, 3),
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
            => TypeIdentityBase + 0x50000 + ((long)qualifiers << 16)
                + ((long)familyCode << 8) + argumentCode;

        static long TypeIdentity(TrRmTypeSpecRefInfo reference)
            => reference.Form switch
            {
                0x01 when reference.TypeCode < 0x80
                    => AtomicIdentity(reference.TypeCode),
                0x01 => FamilyIdentity(reference.Qualifiers,
                    reference.TypeCode, reference.ArgumentCode),
                0x0F => TypeIdentityBase + 0x40000,
                _ => 0,
            };
    }

    private void Validate(int row)
    {
        var bytes = _rows.GetRow(row);
        if ((bytes[1] & ~7) != 0)
        {
            throw InvalidType();
        }

        switch (bytes[0])
        {
            case 0x01:
                var code = bytes[2];
                if (code <= 0x19)
                {
                    RequireLength(bytes, 3);
                    if (bytes[1] != 0)
                    {
                        throw InvalidType();
                    }
                }
                else if (IsAssignedInlineFamily(code))
                {
                    RequireLength(bytes, 4);
                    if (bytes[3] > 0x19
                        || ((bytes[1] & 0x02) != 0 && code is not (0x8B or 0x8D)))
                    {
                        throw InvalidType();
                    }
                }
                else
                {
                    throw InvalidType();
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
                if (bytes.Length < 10 || (bytes.Length - 6) % sizeof(uint) != 0)
                {
                    throw InvalidType();
                }
                var generic = Read(bytes, 2);
                RequireEarlier(generic, row);
                if (_rows.GetRow((int)generic)[0] is not (0x02 or 0x03))
                {
                    throw InvalidType();
                }

                for (var offset = 6; offset < bytes.Length; offset += sizeof(uint))
                {
                    var argumentWord = Read(bytes, offset);
                    if (argumentWord == 0)
                    {
                        throw InvalidType();
                    }

                    if ((argumentWord & 0xFF00_0000) != 0)
                    {
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
                if (!IsIndexedFamily(bytes[2]))
                {
                    throw InvalidType();
                }
                var argument = Read(bytes, 3);
                var familyArgument = GetTypeSpecRef(argument,
                    allowInitQualifier: bytes[2] == 0x8A);
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
                if ((bytes[1] & ~1) != 0)
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
                if (bytes[1] != 0)
                {
                    throw InvalidType();
                }
                break;
            case 0x0D:
                if (bytes[1] != 0)
                {
                    throw InvalidType();
                }
                if (_schema == TrRmArtifactSchema.Development)
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
                    if (bytes[2] is < 1 or > 4 || methodDef == 0
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
            if (_rows.GetRow(row)[0] == 0x0D)
            {
                count++;
            }
        }

        return count;
    }

    private void ValidateTypedReference(ReadOnlySpan<byte> bytes, int tableCount)
    {
        RequireLength(bytes, 6);
        var reference = Read(bytes, 2);
        if (reference == 0 || reference > tableCount)
        {
            throw InvalidType();
        }
    }

    private static bool IsIndexedFamily(byte familyCode)
        => IsAssignedInlineFamily(familyCode);

    private static bool IsNoncanonicalAtomic(TrRmTypeSpecRefInfo reference)
        => reference.IsRow && reference.Form == 0x01 && reference.TypeCode < 0x80;

    private static void RequireEarlier(uint reference, int row)
    {
        if (reference == 0 || reference >= row)
        {
            throw InvalidType();
        }
    }

    private static uint Read(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));

    private static void RequireLength(ReadOnlySpan<byte> bytes, int length)
    {
        if (bytes.Length != length)
        {
            throw InvalidType();
        }
    }

    private static bool IsAssignedInlineFamily(byte familyCode)
        => familyCode is >= 0x80 and <= 0x84 or 0x8A or >= 0x8B and <= 0x8F;

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static InvalidDataException InvalidType()
        => new("Invalid TRRM TypeSpec row.");
}
