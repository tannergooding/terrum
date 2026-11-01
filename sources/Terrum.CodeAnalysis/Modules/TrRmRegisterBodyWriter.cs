// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmRegisterBodyBlock(
    uint CodeOffset, uint CodeLength, ushort EntryStateRecordIndex);

internal static class TrRmRegisterBodyWriter
{
    private const int BlockSize = 12;

    internal static byte[] Encode(ReadOnlyMemory<byte> homeMap,
        ReadOnlyMemory<byte> typeMap, ReadOnlyMemory<byte> methodMap,
        ReadOnlyMemory<byte> fieldMap, ReadOnlyMemory<byte> constantMap,
        IReadOnlyList<TrRmRegisterBodyBlock> blocks,
        IReadOnlyList<ReadOnlyMemory<byte>> records, ReadOnlyMemory<byte> code,
        int contractCount = 0,
        TrRmArtifactSchema schema = TrRmArtifactSchema.Standard)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(records);
        if (contractCount is < 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(contractCount));
        }
        if (schema is not (TrRmArtifactSchema.Standard
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
            or TrRmArtifactSchema.GeneralPredicateGraphsDraft))
        {
            throw new ArgumentOutOfRangeException(nameof(schema));
        }
        if (blocks.Count is < 1 or > 65_536)
        {
            throw new ArgumentOutOfRangeException(nameof(blocks));
        }
        if (records.Count is < 1 or > 65_536)
        {
            throw new ArgumentOutOfRangeException(nameof(records));
        }
        if (code.IsEmpty)
        {
            throw new ArgumentException("Register Body code cannot be empty.", nameof(code));
        }

        var homeCount = MapCount(homeMap, 8, nameof(homeMap));
        var typeCount = MapCount(typeMap, 4, nameof(typeMap));
        var methodCount = MapCount(methodMap, 4, nameof(methodMap));
        var fieldCount = MapCount(fieldMap, 4, nameof(fieldMap));
        var constantCount = MapCount(constantMap, 8, nameof(constantMap));
        var mapMask = (byte)((homeCount != 0 ? 1 : 0)
            | (typeCount != 0 ? 2 : 0)
            | (methodCount != 0 ? 4 : 0)
            | (fieldCount != 0 ? 8 : 0)
            | (constantCount != 0 ? 16 : 0));
        var mapCount = BitOperations.PopCount((uint)mapMask);
        var mapBytes = (long)homeMap.Length + typeMap.Length + methodMap.Length
            + fieldMap.Length + constantMap.Length;

        var recordOffsets = new uint[records.Count + 1];
        var recordDataLength = 0L;
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.Length < sizeof(uint))
            {
                throw new ArgumentException("A register Body record is shorter than its header.", nameof(records));
            }

            if (recordDataLength > int.MaxValue - record.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(records));
            }

            recordOffsets[index] = checked((uint)recordDataLength);
            recordDataLength += record.Length;
        }
        recordOffsets[^1] = checked((uint)recordDataLength);

        var codeCursor = 0u;
        foreach (var block in blocks)
        {
            if (codeCursor > (uint)code.Length || block.CodeOffset != codeCursor
                || block.CodeLength == 0
                || block.CodeLength > (uint)code.Length - codeCursor
                || block.EntryStateRecordIndex >= records.Count
                || records[block.EntryStateRecordIndex].Span[0] != 0x01)
            {
                throw new ArgumentException("A register Body Block has an invalid code or entry-state range.", nameof(blocks));
            }

            codeCursor += block.CodeLength;
        }
        if (codeCursor != (uint)code.Length)
        {
            throw new ArgumentException("Register Body Blocks must cover the code contiguously.", nameof(blocks));
        }

        var size = 17L + ((long)mapCount * sizeof(uint)) + mapBytes
            + ((long)blocks.Count * BlockSize)
            + (((long)records.Count + 1) * sizeof(uint))
            + recordDataLength + code.Length;
        if (size > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(records));
        }

        var result = new byte[(int)size];
        result[0] = 0x01;
        result[1] = 0x01;
        result[2] = mapMask;
        var position = 5;
        WriteMapCount(result, ref position, mapMask, 0, homeCount);
        WriteMapCount(result, ref position, mapMask, 1, typeCount);
        WriteMapCount(result, ref position, mapMask, 2, methodCount);
        WriteMapCount(result, ref position, mapMask, 3, fieldCount);
        WriteMapCount(result, ref position, mapMask, 4, constantCount);
        WriteCount(result, ref position, blocks.Count);
        WriteCount(result, ref position, records.Count);
        WriteCount(result, ref position, code.Length);
        Copy(homeMap, result, ref position);
        Copy(typeMap, result, ref position);
        Copy(methodMap, result, ref position);
        Copy(fieldMap, result, ref position);
        Copy(constantMap, result, ref position);

        foreach (var block in blocks)
        {
            Write(result, ref position, block.CodeOffset);
            Write(result, ref position, block.CodeLength);
            Write(result, ref position, block.EntryStateRecordIndex);
            Write(result, ref position, (ushort)0);
        }
        foreach (var offset in recordOffsets)
        {
            Write(result, ref position, offset);
        }
        foreach (var record in records)
        {
            Copy(record, result, ref position);
        }
        Copy(code, result, ref position);

        if (position != result.Length)
        {
            throw new InvalidOperationException("Register Body framing size did not match its encoded extents.");
        }

        _ = new TrRmRegisterBodyReader(result, contractCount, schema);
        return result;
    }

    private static int MapCount(ReadOnlyMemory<byte> map, int rowSize, string parameterName)
    {
        if (map.Length % rowSize != 0)
        {
            throw new ArgumentException("Register Body map length must be a whole number of rows.", parameterName);
        }

        var count = map.Length / rowSize;
        if (count > 65_536)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return count;
    }

    private static void WriteMapCount(byte[] bytes, ref int position,
        byte mapMask, int bit, int count)
    {
        if ((mapMask & (1 << bit)) != 0)
        {
            Write(bytes, ref position, checked((uint)count));
        }
    }

    private static void WriteCount(byte[] bytes, ref int position, int count)
        => Write(bytes, ref position, checked((uint)count));

    private static void Write(byte[] bytes, ref int position, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(position), value);
        position += sizeof(uint);
    }

    private static void Write(byte[] bytes, ref int position, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(position), value);
        position += sizeof(ushort);
    }

    private static void Copy(ReadOnlyMemory<byte> source, byte[] destination, ref int position)
    {
        source.Span.CopyTo(destination.AsSpan(position));
        position += source.Length;
    }
}
