// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Structurally validated TypeSpec rows; executable type support is a separate contract.</summary>
/// <remarks>The borrowed section bytes must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmTypeSpecTableReader
{
    private readonly TrRmVariableTableIndex _rows;
    private readonly int _typeDefCount;
    private readonly int _importCount;
    private readonly int _genericParamCount;
    private readonly int _typeArgCount;

    public TrRmTypeSpecTableReader(ReadOnlySpan<byte> section, int recordCount,
        int typeDefCount, int importCount, int genericParamCount, int typeArgCount)
    {
        if (!ValidCount(typeDefCount) || !ValidCount(importCount)
            || !ValidCount(genericParamCount) || !ValidCount(typeArgCount))
        {
            throw InvalidType();
        }

        _rows = new TrRmVariableTableIndex(section, recordCount, 3, 1);
        _typeDefCount = typeDefCount;
        _importCount = importCount;
        _genericParamCount = genericParamCount;
        _typeArgCount = typeArgCount;
        for (var row = 1; row <= recordCount; row++)
        {
            Validate(row);
        }
    }

    public ReadOnlySpan<byte> GetRow(int row)
        => _rows.GetRow(row);

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
                if (bytes[2] is < 0x80 or > 0x8A)
                {
                    throw InvalidType();
                }
                var argument = Read(bytes, 3);
                RequireEarlier(argument, row);
                RejectNoncanonicalInline(argument);
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
