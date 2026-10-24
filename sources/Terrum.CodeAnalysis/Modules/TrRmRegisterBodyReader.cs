// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly ref struct TrRmRegisterBodyReader
{
    private const int BlockSize = 12;
    private const int RecordHeaderSize = 4;

    private readonly ReadOnlySpan<byte> _homeMap;
    private readonly ReadOnlySpan<byte> _typeMap;
    private readonly ReadOnlySpan<byte> _methodMap;
    private readonly ReadOnlySpan<byte> _fieldMap;
    private readonly ReadOnlySpan<byte> _constantMap;
    private readonly ReadOnlySpan<byte> _blocks;
    private readonly ReadOnlySpan<byte> _recordOffsets;
    private readonly ReadOnlySpan<byte> _recordData;
    private readonly ReadOnlySpan<byte> _code;
    private readonly int _recordCount;
    private readonly int _contractCount;

    internal TrRmRegisterBodyReader(ReadOnlySpan<byte> body, int contractCount = 0)
    {
        if (contractCount is < 0 or > 0xFF_FFFF
            || body.Length < 17 || body[0] != 0x01 || body[1] != 0x01
            || (body[2] & 0xE0) != 0 || ReadUInt16(body, 3) != 0)
        {
            throw InvalidBody();
        }

        MapMask = body[2];
        _contractCount = contractCount;
        var position = 5;
        var homeCount = ReadMapCount(body, ref position, MapMask, 0);
        var typeCount = ReadMapCount(body, ref position, MapMask, 1);
        var methodCount = ReadMapCount(body, ref position, MapMask, 2);
        var fieldCount = ReadMapCount(body, ref position, MapMask, 3);
        var constantCount = ReadMapCount(body, ref position, MapMask, 4);
        BlockCount = ReadCount(body, ref position);
        _recordCount = ReadCount(body, ref position);
        CodeByteLength = ReadCount(body, ref position);
        if (BlockCount is < 1 or > 65_536
            || _recordCount is < 1 or > 65_536
            || CodeByteLength == 0)
        {
            throw InvalidBody();
        }

        HomeCount = homeCount;
        TypeCount = typeCount;
        MethodCount = methodCount;
        FieldCount = fieldCount;
        ConstantCount = constantCount;

        var mapStart = position;
        var blockStart = AddExtent(mapStart,
            (long)homeCount * 8 + (long)typeCount * 4 + (long)methodCount * 4
                + (long)fieldCount * 4 + (long)constantCount * 8,
            body.Length);
        var recordOffsetsStart = AddExtent(blockStart,
            (long)BlockCount * BlockSize, body.Length);
        var recordDataStart = AddExtent(recordOffsetsStart,
            ((long)_recordCount + 1) * sizeof(uint), body.Length);

        _homeMap = body.Slice(mapStart, homeCount * 8);
        var nextMap = mapStart + homeCount * 8;
        _typeMap = body.Slice(nextMap, typeCount * 4);
        nextMap += typeCount * 4;
        _methodMap = body.Slice(nextMap, methodCount * 4);
        nextMap += methodCount * 4;
        _fieldMap = body.Slice(nextMap, fieldCount * 4);
        nextMap += fieldCount * 4;
        _constantMap = body.Slice(nextMap, constantCount * 8);
        _blocks = body.Slice(blockStart, BlockCount * BlockSize);
        _recordOffsets = body.Slice(recordOffsetsStart,
            (_recordCount + 1) * sizeof(uint));

        if (ReadUInt32(_recordOffsets, 0) != 0)
        {
            throw InvalidBody();
        }

        var previousOffset = 0u;
        for (var record = 1; record <= _recordCount; record++)
        {
            var nextOffset = ReadUInt32(_recordOffsets, record * sizeof(uint));
            if (nextOffset < previousOffset + RecordHeaderSize
                || nextOffset > body.Length - recordDataStart)
            {
                throw InvalidBody();
            }

            previousOffset = nextOffset;
        }

        var codeStart = AddExtent(recordDataStart, previousOffset, body.Length);
        if (CodeByteLength != body.Length - codeStart)
        {
            throw InvalidBody();
        }

        _recordData = body.Slice(recordDataStart, (int)previousOffset);
        _code = body.Slice(codeStart, CodeByteLength);
        for (var record = 0; record < _recordCount; record++)
        {
            ValidateRecord(GetRecord(record));
        }
        ValidateBlocks();
    }

    internal byte MapMask { get; }
    internal int HomeCount { get; }
    internal int TypeCount { get; }
    internal int MethodCount { get; }
    internal int FieldCount { get; }
    internal int ConstantCount { get; }
    internal int BlockCount { get; }
    internal int RecordCount => _recordCount;
    internal int CodeByteLength { get; }
    internal ReadOnlySpan<byte> HomeMap => _homeMap;
    internal ReadOnlySpan<byte> TypeMap => _typeMap;
    internal ReadOnlySpan<byte> MethodMap => _methodMap;
    internal ReadOnlySpan<byte> FieldMap => _fieldMap;
    internal ReadOnlySpan<byte> ConstantMap => _constantMap;
    internal ReadOnlySpan<byte> Code => _code;

    internal ReadOnlySpan<byte> GetBlock(int blockIndex)
    {
        if ((uint)blockIndex >= (uint)BlockCount)
        {
            throw InvalidBody();
        }

        return _blocks.Slice(blockIndex * BlockSize, BlockSize);
    }

    internal ReadOnlySpan<byte> GetRecord(int recordIndex)
    {
        if ((uint)recordIndex >= (uint)_recordCount)
        {
            throw InvalidBody();
        }

        var start = ReadUInt32(_recordOffsets, recordIndex * sizeof(uint));
        var end = ReadUInt32(_recordOffsets, (recordIndex + 1) * sizeof(uint));
        return _recordData.Slice((int)start, (int)(end - start));
    }

    internal ReadOnlySpan<byte> GetBlockCode(int blockIndex)
    {
        var block = GetBlock(blockIndex);
        var start = ReadUInt32(block, 0);
        var length = ReadUInt32(block, 4);
        return _code.Slice((int)start, (int)length);
    }

    private void ValidateBlocks()
    {
        uint codeCursor = 0;
        for (var blockIndex = 0; blockIndex < BlockCount; blockIndex++)
        {
            var block = _blocks.Slice(blockIndex * BlockSize, BlockSize);
            var codeOffset = ReadUInt32(block, 0);
            var codeLength = ReadUInt32(block, 4);
            var entryStateRecord = ReadUInt16(block, 8);
            if (codeOffset != codeCursor || codeLength == 0
                || codeLength > CodeByteLength - codeCursor
                || entryStateRecord >= _recordCount
                || ReadUInt16(block, 10) != 0
                || GetRecord(entryStateRecord)[0] != 0x01)
            {
                throw InvalidBody();
            }

            ValidateInstructions(_code.Slice((int)codeOffset, (int)codeLength));
            codeCursor += codeLength;
        }

        if (codeCursor != CodeByteLength)
        {
            throw InvalidBody();
        }
    }

    private static void ValidateInstructions(ReadOnlySpan<byte> block)
    {
        var position = 0;
        var lastOpcode = -1;
        while (position < block.Length)
        {
            var opcode = block[position];
            if (opcode is 0xB4 or 0xB6 or 0xE8 or 0xEA)
            {
                throw InvalidBody();
            }

            var instructionLength = InstructionLength(opcode);
            if (instructionLength > block.Length - position)
            {
                throw InvalidBody();
            }

            position += instructionLength;
            if (IsDefiniteTerminator(opcode) && position != block.Length)
            {
                throw InvalidBody();
            }

            lastOpcode = opcode;
        }

        if (!IsPotentialTerminator(lastOpcode))
        {
            throw InvalidBody();
        }
    }

    private static int InstructionLength(byte opcode)
    {
        if (opcode <= 0x0F)
        {
            return 1;
        }

        if (opcode is >= 0x10 and <= 0x1A
            or >= 0x20 and <= 0x2B
            or >= 0x30 and <= 0x36
            or 0x40 or 0x41
            or >= 0x50 and <= 0x52
            or >= 0x60 and <= 0x67
            or >= 0x70 and <= 0x7F
            or >= 0x80 and <= 0x83
            or >= 0x90 and <= 0x92
            or >= 0xA0 and <= 0xAF
            or >= 0xB0 and <= 0xB8)
        {
            return 2;
        }

        if (opcode is >= 0xC0 and <= 0xCB
            or >= 0xD0 and <= 0xDE
            or >= 0xE0 and <= 0xE8
            or 0xEA or 0xEB)
        {
            return 4;
        }

        throw InvalidBody();
    }

    private static bool IsPotentialTerminator(int opcode)
        => opcode == 0x00
            || opcode is 0x17 or 0x18
                or >= 0xA0 and <= 0xAF
                or 0xB0 or 0xB1 or 0xB2 or 0xB4 or 0xB5 or 0xB6 or 0xB7 or 0xB8
                or 0xE0
                or >= 0xE3 and <= 0xE6
                or 0xE8 or 0xEA or 0xEB;

    private static bool IsDefiniteTerminator(byte opcode)
        => opcode == 0x00
            || opcode is 0x17 or 0x18
                or >= 0xA0 and <= 0xAF
                or 0xB0 or 0xB1 or 0xB2 or 0xB4 or 0xB5 or 0xB6 or 0xB7 or 0xB8
                or >= 0xE3 and <= 0xE6
                or 0xE8 or 0xEA or 0xEB;

    private static int ReadMapCount(ReadOnlySpan<byte> body, ref int position,
        byte mapMask, int bit)
    {
        if ((mapMask & (1 << bit)) == 0)
        {
            return 0;
        }

        var count = ReadCount(body, ref position);
        if (count > 65_536)
        {
            throw InvalidBody();
        }

        return count;
    }

    private static int ReadCount(ReadOnlySpan<byte> body, ref int position)
    {
        if (position < 0 || position > body.Length - sizeof(uint))
        {
            throw InvalidBody();
        }

        var value = ReadUInt32(body, position);
        position += sizeof(uint);
        if (value > int.MaxValue)
        {
            throw InvalidBody();
        }

        return (int)value;
    }

    private static int AddExtent(int start, long length, int limit)
    {
        var end = start + length;
        if (start < 0 || length < 0 || end > limit || end > int.MaxValue)
        {
            throw InvalidBody();
        }

        return (int)end;
    }

    private void ValidateRecord(ReadOnlySpan<byte> record)
    {
        if (record.Length < RecordHeaderSize || record[0] is < 0x01 or > 0x0B
            || record[1] != 0 || ReadUInt16(record, 2) != 0)
        {
            throw InvalidBody();
        }

        var payload = record[RecordHeaderSize..];
        switch (record[0])
        {
            case 0x01:
                ValidateBlockEntryState(payload);
                break;
            case 0x02:
                ValidateCallSite(payload);
                break;
            case 0x03:
                if (payload.Length != 16 || payload[0] is not (1 or 2)
                    || payload[1] is < 1 or > 5
                    || payload[2] is < 1 or > 3
                    || payload[3] > 5 || payload[6] > 1
                    || payload[7] != 0 || ReadUInt16(payload, 10) != 0
                    || (payload[6] == 0
                        && (ReadUInt16(payload, 8) != 0
                            || ReadUInt32(payload, 12) != 0))
                    || (payload[3] == 0
                        && (payload[6] != 0 || ReadUInt32(payload, 12) != 0))
                    || (ReadUInt32(payload, 12) != 0
                        && ReadUInt32(payload, 12) > _contractCount))
                {
                    throw InvalidBody();
                }
                break;
            case 0x04:
                if (payload.Length != 20
                    || payload[0] is < 1 or > 4 || payload[1] is < 1 or > 5
                    || payload[2] > 2 || payload[3] > 2
                    || payload[8] > 2 || payload[9] > 2
                    || !IsValidSelector(payload[2], ReadUInt16(payload, 4))
                    || !IsValidSelector(payload[3], ReadUInt16(payload, 6))
                    || !IsValidSelector(payload[8], ReadUInt16(payload, 10))
                    || ReadUInt16(payload, 16) >= TypeCount
                    || !IsValidTargetReference(payload)
                    || ReadUInt16(payload, 18) != 0)
                {
                    throw InvalidBody();
                }
                break;
            case 0x05:
                if (payload.Length != 16 || payload[0] is < 1 or > 2
                    || payload[1] is < 1 or > 2
                    || payload[2] is < 1 or > 3
                    || payload[3] is < 1 or > 5
                    || payload[12] > 1 || payload[13] != 0
                    || !IsValidSelector(payload[0], ReadUInt16(payload, 4))
                    || !IsValidSelector(payload[1], ReadUInt16(payload, 6))
                    || (payload[12] == 0
                        && (ReadUInt16(payload, 14) != 0
                            || ReadUInt32(payload, 8) != 0))
                    || (payload[12] != 0
                        && (ReadUInt32(payload, 8) == 0
                            || ReadUInt32(payload, 8) > _contractCount)))
                {
                    throw InvalidBody();
                }
                break;
            case 0x06:
                if (payload.Length != 4
                    || ReadUInt16(payload, 0) >= BlockCount
                    || ReadUInt16(payload, 2) >= BlockCount)
                {
                    throw InvalidBody();
                }
                break;
            case 0x07:
                ValidateSwitch(payload);
                break;
            case 0x08:
                if (payload.Length != 8 || payload[4] is not (1 or 2)
                    || ReadUInt16(payload, 0) >= TypeCount
                    || ReadUInt16(payload, 2) >= _recordCount
                    || GetRecord(ReadUInt16(payload, 2))[0] != 0x0B
                    || payload[5] > 15
                    || payload[7] != 0
                    || (payload[4] == 1 && payload[6] != 0)
                    || (payload[4] == 2 && payload[6] > 15))
                {
                    throw InvalidBody();
                }
                break;
            case 0x09:
                if (payload.Length != 8 || payload[0] is < 1 or > 3
                    || payload[7] != 0)
                {
                    throw InvalidBody();
                }
                if (payload[0] == 2
                    ? payload[1] != 0 || ReadUInt16(payload, 2) != 0
                    : payload[1] is not (1 or 2)
                        || !IsValidSelector(payload[1], ReadUInt16(payload, 2))
                        || ReadUInt16(payload, 4) != 0)
                {
                    throw InvalidBody();
                }
                break;
            case 0x0A:
                ValidateRegionExit(payload);
                break;
            case 0x0B:
                ValidateConstructorBinding(payload);
                break;
        }
    }

    private void ValidateBlockEntryState(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < sizeof(uint))
        {
            throw InvalidBody();
        }

        var registerCount = ReadUInt32(payload, 0);
        if (registerCount > 14
            || 4L + registerCount * 5L != payload.Length)
        {
            throw InvalidBody();
        }

        var previousRegister = -1;
        for (var row = 0; row < registerCount; row++)
        {
            var offset = 4 + row * 5;
            var register = payload[offset];
            var carrier = payload[offset + 1];
            var typeMapIndex = ReadUInt16(payload, offset + 2);
            var access = payload[offset + 4];
            if (register > 13 || register <= previousRegister || carrier > 2
                || (carrier == 0 && (access != 0 || typeMapIndex >= TypeCount))
                || (carrier == 1 && (access is < 1 or > 5
                    || typeMapIndex >= TypeCount))
                || (carrier == 2 && (typeMapIndex != 0 || access != 0)))
            {
                throw InvalidBody();
            }

            previousRegister = register;
        }
    }

    private void ValidateCallSite(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 16 || payload[0] is < 1 or > 3
            || payload[1] > 2 || ReadUInt16(payload, 2) != 0)
        {
            throw InvalidBody();
        }

        var resultIndex = ReadUInt16(payload, 6);
        var inputCount = ReadUInt32(payload, 8);
        var preparationCount = ReadUInt32(payload, 12);
        var targetIndex = ReadUInt16(payload, 4);
        if ((payload[0] == 1 && targetIndex >= MethodCount)
            || (payload[0] == 2 && targetIndex > 13)
            || (payload[0] == 3 && targetIndex >= HomeCount)
            || (payload[1] == 1 && resultIndex is > 15 or 14)
            || (payload[1] == 2 && resultIndex >= HomeCount)
            || (payload[1] == 0 && resultIndex != 0)
            || inputCount > 65_536 || preparationCount > 65_536
            || 16L + inputCount * 4L + preparationCount * 2L != payload.Length)
        {
            throw InvalidBody();
        }

        var inputOffset = 16;
        for (var input = 0; input < inputCount; input++)
        {
            var selectorKind = payload[inputOffset];
            var sourceUse = payload[inputOffset + 1];
            var selectorIndex = ReadUInt16(payload, inputOffset + 2);
            if (selectorKind > 2 || !IsValidSelector(selectorKind, selectorIndex)
                || (selectorKind == 0
                    ? sourceUse != 0 || selectorIndex != 0
                    : sourceUse is < 1 or > 5))
            {
                throw InvalidBody();
            }

            inputOffset += 4;
        }

        for (var preparation = 0; preparation < preparationCount; preparation++)
        {
            var recordIndex = ReadUInt16(payload, inputOffset + preparation * 2);
            if (recordIndex >= _recordCount || GetRecord(recordIndex)[0] != 0x03)
            {
                throw InvalidBody();
            }
        }
    }

    private void ValidateSwitch(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12 || payload[0] is < 1 or > 2 || payload[1] != 0
            || (payload[0] == 1 && ReadUInt16(payload, 2) > 15)
            || (payload[0] == 2 && ReadUInt16(payload, 2) >= HomeCount)
            || ReadUInt16(payload, 4) >= TypeCount
            || ReadUInt16(payload, 6) >= BlockCount)
        {
            throw InvalidBody();
        }

        var caseCount = ReadUInt32(payload, 8);
        if (caseCount > 65_536 || 12L + caseCount * 4L != payload.Length)
        {
            throw InvalidBody();
        }

        for (var item = 0; item < caseCount; item++)
        {
            var offset = 12 + item * 4;
            if (ReadUInt16(payload, offset) >= ConstantCount
                || ReadUInt16(payload, offset + 2) >= BlockCount)
            {
                throw InvalidBody();
            }
        }
    }

    private void ValidateConstructorBinding(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 16)
        {
            throw InvalidBody();
        }

        if (ReadUInt16(payload, 0) >= MethodCount
            || ReadUInt16(payload, 2) >= TypeCount)
        {
            throw InvalidBody();
        }

        var suppliedCount = ReadUInt32(payload, 4);
        var formalCount = ReadUInt32(payload, 8);
        var bindingCount = ReadUInt32(payload, 12);
        if (suppliedCount != 0 || bindingCount != formalCount
            || 16L + bindingCount * 8L != payload.Length)
        {
            throw InvalidBody();
        }

        for (var binding = 0; binding < bindingCount; binding++)
        {
            var offset = 16 + binding * 8;
            var formalOrdinal = ReadUInt32(payload, offset);
            var bindingKind = payload[offset + 4];
            var sourceUse = payload[offset + 5];
            var constantMapIndex = ReadUInt16(payload, offset + 6);
            if (formalOrdinal != (uint)binding || bindingKind is not (1 or 2)
                || sourceUse > 5 || constantMapIndex >= ConstantCount)
            {
                throw InvalidBody();
            }
        }
    }

    private void ValidateRegionExit(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8 || payload[1] > 1
            || (payload[1] == 0 && ReadUInt16(payload, 2) != 0)
            || (payload[1] == 1 && ReadUInt16(payload, 2) >= BlockCount))
        {
            throw InvalidBody();
        }

        var regionCount = ReadUInt32(payload, 4);
        if (8L + regionCount * 2L != payload.Length)
        {
            throw InvalidBody();
        }
    }

    private bool IsValidSelector(byte kind, ushort index)
        => kind switch
        {
            0 => index == 0,
            1 => index <= 15,
            2 => index < HomeCount,
            _ => false,
        };

    private bool IsValidTargetReference(ReadOnlySpan<byte> payload)
    {
        var targetKind = payload[0];
        var referenceKind = payload[9];
        var reference = ReadUInt32(payload, 12);
        return targetKind switch
        {
            1 => referenceKind == 0 && reference < (uint)HomeCount,
            2 or 4 => referenceKind == 1 && reference != 0
                && (reference & 0xFF00_0000) == 0,
            3 => referenceKind == 2 && reference < (uint)MethodCount,
            _ => false,
        };
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(ushort))
        {
            throw InvalidBody();
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, sizeof(ushort)));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(uint))
        {
            throw InvalidBody();
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidBody()
        => new("Invalid TRRM register Body.");
}

internal static class TrRmBodyImplementationReader
{
    internal static void Validate(ReadOnlySpan<byte> body, int contractCount)
    {
        if (body.IsEmpty)
        {
            throw InvalidBody();
        }

        switch (body[0])
        {
            case 0x01:
                _ = new TrRmRegisterBodyReader(body, contractCount);
                break;
            case 0x02:
                if (body.Length != 8 || body[1] != 0 || ReadUInt16(body, 2) != 0
                    || !IsToken(ReadUInt32(body, 4), 0x14, 0x1C))
                {
                    throw InvalidBody();
                }
                break;
            case 0x03:
                if (body.Length != 12 || body[1] != 0 || ReadUInt16(body, 2) != 0
                    || !IsToken(ReadUInt32(body, 4), 0x14, 0x1C)
                    || body[10] is < 1 or > 3 || body[11] != 0)
                {
                    throw InvalidBody();
                }
                break;
            case 0x04:
                var dependencyRow = ReadUInt32(body, 8);
                if (body.Length != 12 || body[1] != 0 || ReadUInt16(body, 2) != 0
                    || !IsToken(ReadUInt32(body, 4), 0x14, 0x1C)
                    || dependencyRow == 0 || dependencyRow > contractCount)
                {
                    throw InvalidBody();
                }
                break;
            case 0x05:
                if (body.Length != 8 || body[1] != 0 || ReadUInt16(body, 2) != 0
                    || !IsToken(ReadUInt32(body, 4), 0x16, 0x1C))
                {
                    throw InvalidBody();
                }
                break;
            default:
                throw InvalidBody();
        }
    }

    private static bool IsToken(uint token, byte firstKind, byte secondKind)
    {
        var kind = (byte)(token >> 24);
        var row = token & 0x00FF_FFFF;
        return row != 0 && (kind == firstKind || kind == secondKind);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(ushort))
        {
            throw InvalidBody();
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, sizeof(ushort)));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(uint))
        {
            throw InvalidBody();
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidBody()
        => new("Invalid TRRM Body implementation.");
}
