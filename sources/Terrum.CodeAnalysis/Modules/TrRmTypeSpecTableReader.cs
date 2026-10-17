// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Structurally validated TypeSpec rows; executable type support is a separate contract.</summary>
/// <remarks>The borrowed section bytes must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmTypeSpecTableReader
{
    internal const ushort NativeFunctionPointerTypeKeyStart = 0x200;
    private const long TypeIdentityBase = 0x1000_0000_0000;

    private readonly TrRmVariableTableIndex _rows;
    private readonly int _recordCount;
    private readonly int _typeDefCount;
    private readonly int _importCount;
    private readonly int _genericParamCount;
    private readonly int _typeArgCount;
    private readonly int _nativeFunctionPointerSignatureCount;
    private readonly ushort[] _compactValueTypeKeys;

    /// <summary>Creates a reader for TypeSpec rows and their referenced table counts.</summary>
    public TrRmTypeSpecTableReader(ReadOnlySpan<byte> section, int recordCount,
        int typeDefCount, int importCount, int genericParamCount, int typeArgCount,
        int nativeFunctionPointerSignatureCount = 0)
    {
        if (!ValidCount(typeDefCount) || !ValidCount(importCount)
            || !ValidCount(genericParamCount) || !ValidCount(typeArgCount)
            || !ValidCount(nativeFunctionPointerSignatureCount))
        {
            throw InvalidType();
        }

        _rows = new TrRmVariableTableIndex(section, recordCount, 3, 1);
        _recordCount = recordCount;
        _typeDefCount = typeDefCount;
        _importCount = importCount;
        _genericParamCount = genericParamCount;
        _typeArgCount = typeArgCount;
        _nativeFunctionPointerSignatureCount = nativeFunctionPointerSignatureCount;
        for (var row = 1; row <= recordCount; row++)
        {
            Validate(row);
        }
        _compactValueTypeKeys = CreateCompactValueTypeKeys();
    }

    public ReadOnlySpan<byte> GetRow(int row)
        => _rows.GetRow(row);

    public int Count => _recordCount;
    public int TypeArgCount => _typeArgCount;
    /// <summary>The number of native function-pointer signatures in the table.</summary>
    public int NativeFunctionPointerSignatureCount => _nativeFunctionPointerSignatureCount;

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
        if (index >= 0 && index < _nativeFunctionPointerSignatureCount
            && index < ushort.MaxValue - NativeFunctionPointerTypeKeyStart + 1)
        {
            signatureRow = index + 1;
            return true;
        }

        signatureRow = 0;
        return false;
    }

    internal int NativeFunctionPointerTypeKeyEnd
        => Math.Min(ushort.MaxValue + 1,
            NativeFunctionPointerTypeKeyStart + _nativeFunctionPointerSignatureCount);

    internal bool IsNativePointerTypeKey(ushort typeKey)
        => typeKey == 0x8C || typeKey >= NativeFunctionPointerTypeKeyEnd;

    private ushort[] CreateCompactValueTypeKeys()
    {
        var keys = new ushort[_recordCount + 1];
        var identities = new long[_recordCount + 1];
        var pointerIdentities = new Dictionary<(long Pointee, byte Qualifiers),
            (long Identity, ushort TypeKey)>();
        var nextPointerIdentity = 0;
        var nextPointerTypeKey = 0;
        var firstPointerTypeKey = NativeFunctionPointerTypeKeyStart
            + _nativeFunctionPointerSignatureCount;
        var pointerTypeKeyCapacity = Math.Max(0, ushort.MaxValue - firstPointerTypeKey + 1);

        for (var row = 1; row <= _recordCount; row++)
        {
            var bytes = _rows.GetRow(row);
            if (bytes[0] == 0x01 && bytes.Length == 3 && bytes[1] == 0)
            {
                var code = bytes[2];
                if (IsCompactAtom(code))
                {
                    identities[row] = AtomicIdentity(code);
                    keys[row] = code;
                }
            }
            else if (bytes[0] == 0x06 && bytes.Length == 6 && bytes[1] == 0)
            {
                var innerRow = Read(bytes, 2);
                var inner = _rows.GetRow((int)innerRow);
                if (inner.Length == 3 && inner[0] == 0x01 && inner[1] == 0
                    && IsCompactOptionalAtom(inner[2]))
                {
                    identities[row] = TypeIdentityBase + 0x10000 + inner[2];
                    keys[row] = (ushort)(0x100 | inner[2]);
                }
            }
            else if (bytes[0] is 0x02 or 0x03 && bytes.Length == 6 && bytes[1] == 0)
            {
                identities[row] = TypeIdentityBase + ((long)bytes[0] << 32)
                    + Read(bytes, 2);
            }
            else if (bytes[0] == 0x0D && bytes.Length == 6 && bytes[1] == 0)
            {
                var signature = Read(bytes, 2);
                identities[row] = TypeIdentityBase + 0x20000 + signature;
                var typeKey = NativeFunctionPointerTypeKeyStart + signature - 1;
                if (typeKey <= ushort.MaxValue)
                {
                    keys[row] = (ushort)typeKey;
                }
            }
            else if (TryGetNativePointer(bytes, identities, out var pointeeIdentity,
                out var qualifiers))
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
        }

        return keys;

        static bool TryGetNativePointer(ReadOnlySpan<byte> bytes, long[] identities,
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
                var pointeeRow = Read(bytes, 3);
                if (pointeeRow == 0 || pointeeRow >= identities.Length)
                {
                    return false;
                }

                pointeeIdentity = identities[pointeeRow];
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
            return IsCompactOptionalAtom(code) || code is 0x14 or 0x15;
        }

        static bool IsCompactOptionalAtom(byte code)
        {
            return code is 1 or 3 or 8 or 9 or 0x0D or 0x0E;
        }
    }

    private void Validate(int row)
    {
        var bytes = _rows.GetRow(row);
        if ((bytes[1] & ~1) != 0)
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
                }
                else if (code is >= 0x80 and <= 0x8F)
                {
                    RequireLength(bytes, 4);
                    if (bytes[3] > 0x19)
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
                RequireLength(bytes, 14);
                var generic = Read(bytes, 2);
                RequireEarlier(generic, row);
                if (_rows.GetRow((int)generic)[0] is not (0x02 or 0x03))
                {
                    throw InvalidType();
                }

                var first = Read(bytes, 6);
                var count = Read(bytes, 10);
                if (first == 0 || first > _typeArgCount || count == 0
                    || count > (uint)_typeArgCount - first + 1)
                {
                    throw InvalidType();
                }
                break;
            case >= 0x06 and <= 0x0B:
                RequireLength(bytes, 6);
                var inner = Read(bytes, 2);
                RequireEarlier(inner, row);
                if (bytes[0] != 0x06)
                {
                    RejectNoncanonicalInline(inner);
                }
                break;
            case 0x0C:
                RequireLength(bytes, 7);
                if (bytes[2] is < 0x80 or (> 0x8A and not 0x8C))
                {
                    throw InvalidType();
                }
                var argument = Read(bytes, 3);
                RequireEarlier(argument, row);
                RejectNoncanonicalInline(argument);
                break;
            case 0x0D:
                RequireLength(bytes, 6);
                var signature = Read(bytes, 2);
                if (bytes[1] != 0 || signature == 0
                    || signature > _nativeFunctionPointerSignatureCount)
                {
                    throw InvalidType();
                }
                break;
            default:
                throw InvalidType();
        }
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

    private void RejectNoncanonicalInline(uint reference)
    {
        var inner = _rows.GetRow((int)reference);
        if (inner[0] == 0x01 && inner[1] == 0 && inner.Length == 3)
        {
            throw InvalidType();
        }
    }

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

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static InvalidDataException InvalidType()
        => new("Invalid TRRM TypeSpec row.");
}
