// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmBodyExampleTests
{
    [Theory]
    [InlineData("GeneralCheckedAdd", 105, 5, false, 38, 36, 3)]
    [InlineData("## Value and place state through checked arithmetic", 122, 5, true, 51, 49, 7)]
    [InlineData("GeneralConstantReturn", 57, 1, false, 18, 16, 1)]
    public void BodyExamplesHaveDirectDefinitionAndUseIndexes(
        string heading, int sectionLength, int valueCount, bool hasPlace,
        int blockLength, int terminatorOffset, int operationCount)
    {
        var bytes = ReadExample(heading);

        Assert.Equal(sectionLength, bytes.Length);
        var position = 0;
        Assert.Equal(0u, ReadUInt32(bytes, ref position));
        Assert.Equal((uint)(sectionLength - 8), ReadUInt32(bytes, ref position));
        Assert.Equal((byte)0, bytes[position++]);
        Assert.Equal((byte)1, bytes[position++]);
        Assert.Equal((byte)valueCount, bytes[position++]);
        Assert.Equal((byte)(hasPlace ? 1 : 0), bytes[position++]);
        Assert.Equal((byte)0, bytes[position++]);
        Assert.Equal((byte)1, bytes[position++]);
        Assert.Equal(1u, ReadUInt32(bytes, ref position));
        if (hasPlace)
        {
            Assert.Equal(new byte[] { 1, 0, 5, 1 }, bytes.AsSpan(position, 4).ToArray());
            position += 4;
        }

        for (var value = 0; value < valueCount; value++)
        {
            Assert.Equal((byte)1, bytes[position++]);
            Assert.Equal((byte)(hasPlace && value == 0 ? 1 : 0), bytes[position++]);
            Assert.Equal((byte)1, bytes[position++]);
        }

        Assert.Equal((byte)0, bytes[position++]);
        Assert.Equal((byte)valueCount, bytes[position++]);
        uint[] expectedDefinitions = hasPlace ? [24, 27, 34, 37, 41]
            : valueCount == 1 ? [12]
            : [uint.MaxValue, uint.MaxValue, 22, 27, 31];
        foreach (var expected in expectedDefinitions)
        {
            Assert.Equal(expected, ReadUInt32(bytes, ref position));
        }

        Assert.Equal(0u, ReadUInt32(bytes, ref position));
        Assert.Equal((uint)blockLength, ReadUInt32(bytes, ref position));
        Assert.Equal((uint)terminatorOffset, ReadUInt32(bytes, ref position));
        var block = bytes.AsSpan(position);
        Assert.Equal(blockLength, block.Length);
        var cursor = 0;
        Assert.Equal((byte)0, block[cursor++]);
        var parameters = block[cursor++];
        Assert.Equal((byte)(valueCount == 5 && !hasPlace ? 2 : 0), parameters);
        for (var parameter = 0; parameter < parameters; parameter++)
        {
            Assert.Equal((byte)(parameter + 1), block[cursor++]);
        }
        Assert.Equal((byte)0, block[cursor++]);
        Assert.Equal((byte)0, block[cursor++]);
        Assert.Equal((byte)0, block[cursor++]);
        var starts = block[cursor++];
        Assert.Equal((byte)(hasPlace ? 1 : 0), starts);
        if (hasPlace)
        {
            Assert.Equal((byte)1, block[cursor++]);
        }
        var ends = block[cursor++];
        Assert.Equal((byte)(hasPlace ? 1 : 0), ends);
        if (hasPlace)
        {
            Assert.Equal((byte)1, block[cursor++]);
        }
        Assert.Equal((byte)valueCount, block[cursor++]);
        byte[] expectedLastUses = hasPlace ? [6, 2, 5, 5, 7]
            : valueCount == 1 ? [1]
            : [0, 0, 2, 2, 3];
        for (var value = 0; value < valueCount; value++)
        {
            Assert.Equal((byte)(value + 1), block[cursor++]);
            Assert.Equal(expectedLastUses[value], block[cursor++]);
        }
        Assert.Equal((byte)(hasPlace ? 1 : 0), block[cursor++]);
        if (hasPlace)
        {
            Assert.Equal((byte)1, block[cursor++]);
            Assert.Equal((byte)operationCount, block[cursor++]);
        }
        Assert.Equal((byte)operationCount, block[cursor++]);
        var observedDefinitions = new int[valueCount + 1];
        var observedLastUses = new int[valueCount + 1];
        Array.Fill(observedDefinitions, -1);
        Array.Fill(observedLastUses, -1);
        void Define(byte value, int offset)
        {
            Assert.InRange(value, 1, valueCount);
            Assert.Equal(-1, observedDefinitions[value]);
            observedDefinitions[value] = offset;
        }
        void Use(byte value, int ordinal)
        {
            Assert.InRange(value, 1, valueCount);
            observedLastUses[value] = ordinal;
        }

        for (var ordinal = 0; ordinal < operationCount; ordinal++)
        {
            var offset = cursor;
            switch (block[cursor++])
            {
                case 0x01 or 0x02:
                    Define(block[cursor++], offset);
                    Assert.Equal((byte)1, block[cursor++]);
                    cursor++;
                    break;
                case 0x0B:
                    Define(block[cursor++], offset);
                    Assert.Equal((byte)1, block[cursor++]);
                    break;
                case 0x0D:
                    Define(block[cursor++], offset);
                    Use(block[cursor++], ordinal);
                    break;
                case 0x0E or 0x0F:
                    Use(block[cursor++], ordinal);
                    Use(block[cursor++], ordinal);
                    break;
                case 0x15:
                    Define(block[cursor++], offset);
                    Assert.Equal((byte)1, block[cursor++]);
                    Use(block[cursor++], ordinal);
                    Use(block[cursor++], ordinal);
                    break;
                default:
                    Assert.Fail($"Unexpected operation at block offset {offset}.");
                    break;
            }
        }
        Assert.Equal(terminatorOffset, cursor);
        Assert.Equal((byte)0x83, block[terminatorOffset]);
        Assert.Equal((byte)(valueCount == 1 ? 1 : 5), block[terminatorOffset + 1]);
        Use(block[terminatorOffset + 1], operationCount);
        for (var value = 1; value <= valueCount; value++)
        {
            var expected = expectedDefinitions[value - 1];
            Assert.Equal(expected == uint.MaxValue ? -1 : (int)expected, observedDefinitions[value]);
            Assert.Equal(expectedLastUses[value - 1], observedLastUses[value]);
        }
    }

    [Fact]
    public void ConditionalMergeHasIndexedDefsUsesAndNormalEdges()
    {
        var bytes = ReadExample("GeneralConditionalMerge");
        Assert.Equal(165, bytes.Length);
        var position = 0;
        Assert.Equal(0u, ReadUInt32(bytes, ref position));
        Assert.Equal(157u, ReadUInt32(bytes, ref position));
        Assert.Equal(new byte[] { 0, 4, 4, 0, 0, 2 },
            bytes.AsSpan(position, 6).ToArray());
        position += 6;
        Assert.Equal(1u, ReadUInt32(bytes, ref position));
        Assert.Equal(2u, ReadUInt32(bytes, ref position));
        for (var value = 1; value <= 4; value++)
        {
            Assert.Equal((byte)(value == 1 ? 1 : 2), bytes[position++]);
            Assert.Equal((byte)0, bytes[position++]);
            Assert.Equal((byte)1, bytes[position++]);
        }
        Assert.Equal(new byte[] { 0, 3, 3, 3, 4 },
            bytes.AsSpan(position, 5).ToArray());
        position += 5;
        for (var value = 0; value < 4; value++)
        {
            Assert.Equal(uint.MaxValue, ReadUInt32(bytes, ref position));
        }
        int[] offsets = [0, 23, 41, 59, 74];
        int[] terminators = [17, 14, 14, 13];
        foreach (var offset in offsets)
        {
            Assert.Equal((uint)offset, ReadUInt32(bytes, ref position));
        }
        foreach (var terminator in terminators)
        {
            Assert.Equal((uint)terminator, ReadUInt32(bytes, ref position));
        }
        var blockData = bytes.AsSpan(position);
        Assert.Equal(74, blockData.Length);
        byte[] usedValues = [1, 3, 2, 4];
        for (var blockId = 1; blockId <= 4; blockId++)
        {
            var block = blockData[offsets[blockId - 1]..offsets[blockId]];
            var cursor = 0;
            Assert.Equal((byte)0, block[cursor++]);
            byte[][] parameters = [[1, 2, 3], [], [], [4]];
            Assert.Equal((byte)parameters[blockId - 1].Length, block[cursor++]);
            foreach (var parameter in parameters[blockId - 1])
            {
                Assert.Equal(parameter, block[cursor++]);
            }
            byte[][] liveIns = [[], [3], [2], []];
            byte[][] liveOuts = [[2, 3], [3], [2], []];
            foreach (var expected in new[] { liveIns[blockId - 1], liveOuts[blockId - 1] })
            {
                Assert.Equal((byte)expected.Length, block[cursor++]);
                foreach (var value in expected)
                {
                    Assert.Equal(value, block[cursor++]);
                }
            }
            Assert.Equal((byte)0, block[cursor++]);
            Assert.Equal((byte)0, block[cursor++]);
            Assert.Equal((byte)0, block[cursor++]);
            Assert.Equal((byte)1, block[cursor++]);
            Assert.Equal(usedValues[blockId - 1], block[cursor++]);
            Assert.Equal((byte)0, block[cursor++]);
            Assert.Equal((byte)0, block[cursor++]);
            Assert.Equal((byte)0, block[cursor++]);
            Assert.Equal(terminators[blockId - 1], cursor);
            if (blockId == 1)
            {
                Assert.Equal(new byte[] { 0x81, 1, 2, 0, 3, 0 }, block[cursor..].ToArray());
            }
            else if (blockId == 4)
            {
                Assert.Equal(new byte[] { 0x83, 4 }, block[cursor..].ToArray());
            }
            else
            {
                Assert.Equal(new byte[] { 0x80, 4, 1, usedValues[blockId - 1] },
                    block[cursor..].ToArray());
            }
        }
    }

    [Theory]
    [InlineData("### Checked add end to end (informative)", 31, 3, 5, 2)]
    [InlineData("### Compact linear constant return", 21, 1, 1, 0)]
    [InlineData("#### Compact linear parameter return", 17, 0, 1, 1)]
    public void CompactLinearDerivesDefinitionsAndUses(
        string heading, int sectionLength, int operationCount, int valueCount,
        int parameterCount)
    {
        var bytes = ReadExample(heading);
        Assert.Equal(sectionLength, bytes.Length);
        var position = 0;
        Assert.Equal(0u, ReadUInt32(bytes, ref position));
        Assert.Equal((uint)(sectionLength - 8), ReadUInt32(bytes, ref position));
        Assert.Equal((byte)0x04, bytes[position++]);
        Assert.Equal((byte)1, bytes[position++]);
        Assert.Equal(1u, ReadUInt32(bytes, ref position));
        Assert.Equal((byte)operationCount, bytes[position++]);

        var definitions = new int[valueCount + 1];
        var lastUses = new int[valueCount + 1];
        Array.Fill(definitions, -1);
        Array.Fill(lastUses, -1);
        var nextValue = parameterCount + 1;
        for (var ordinal = 0; ordinal < operationCount; ordinal++)
        {
            var offset = position;
            var opcode = bytes[position++];
            Assert.True(opcode is 0x01 or 0x02 or 0x15);
            Assert.Equal((byte)nextValue++, bytes[position++]);
            definitions[nextValue - 1] = offset;
            if (opcode == 0x15)
            {
                Assert.Equal((byte)1, bytes[position++]);
                lastUses[bytes[position++]] = ordinal;
                lastUses[bytes[position++]] = ordinal;
            }
            else
            {
                Assert.Equal((byte)1, bytes[position++]);
                position++;
            }
        }
        Assert.Equal(valueCount + 1, nextValue);
        Assert.Equal((byte)0x83, bytes[position++]);
        lastUses[bytes[position++]] = operationCount;
        Assert.Equal(bytes.Length, position);
        Assert.Equal(valueCount == 5
            ? new[] { -1, -1, -1, 15, 20, 24 }
            : parameterCount == 1 ? new[] { -1, -1 } : new[] { -1, 15 },
            definitions);
        Assert.Equal(valueCount == 5
            ? new[] { -1, 0, 0, 2, 2, 3 }
            : new[] { -1, operationCount }, lastUses);
    }

    [Fact]
    public void CompactIndexedMergeRetainsDirectFlowFacts()
    {
        var bytes = ReadExample("#### Compact indexed conditional merge");
        Assert.Equal(100, bytes.Length);
        var position = 0;
        Assert.Equal(0u, ReadUInt32(bytes, ref position));
        Assert.Equal(92u, ReadUInt32(bytes, ref position));
        Assert.Equal(new byte[] { 0x08, 4, 4, 0, 2 },
            bytes.AsSpan(position, 5).ToArray());
        position += 5;
        Assert.Equal(1u, ReadUInt32(bytes, ref position));
        Assert.Equal(2u, ReadUInt32(bytes, ref position));
        Assert.Equal(new byte[] { 1, 1, 2, 1, 2, 1, 2, 1 },
            bytes.AsSpan(position, 8).ToArray());
        position += 8;
        Assert.Equal(new byte[] { 0, 3, 3, 3, 4 },
            bytes.AsSpan(position, 5).ToArray());
        position += 5;
        Assert.Equal(new byte[] { 255, 255, 255, 255 },
            bytes.AsSpan(position, 4).ToArray());
        position += 4;
        int[] offsets = [0, 18, 31, 44, 53];
        int[] terminators = [12, 9, 9, 7];
        foreach (var offset in offsets)
        {
            Assert.Equal((byte)offset, bytes[position++]);
        }
        foreach (var offset in terminators)
        {
            Assert.Equal((byte)offset, bytes[position++]);
        }
        Assert.Equal(53, bytes.Length - position);
        byte[][] parameters = [[1, 2, 3], [], [], [4]];
        byte[][] liveIns = [[], [3], [2], []];
        byte[][] liveOuts = [[2, 3], [3], [2], []];
        byte[] usedValues = [1, 3, 2, 4];
        for (var blockId = 1; blockId <= 4; blockId++)
        {
            var block = bytes.AsSpan(position + offsets[blockId - 1],
                offsets[blockId] - offsets[blockId - 1]);
            var cursor = 0;
            var flags = block[cursor++];
            Assert.Equal((byte)(blockId == 1 ? 0x1A : blockId == 4 ? 0x12 : 0x1C),
                flags);
            foreach (var (bit, values) in new[] {
                (0x02, parameters[blockId - 1]),
                (0x04, liveIns[blockId - 1]),
                (0x08, liveOuts[blockId - 1]),
            })
            {
                Assert.Equal(values.Length != 0, (flags & bit) != 0);
                if (values.Length != 0)
                {
                    Assert.Equal((byte)values.Length, block[cursor++]);
                    foreach (var value in values)
                    {
                        Assert.Equal(value, block[cursor++]);
                    }
                }
            }
            Assert.Equal((byte)1, block[cursor++]);
            Assert.Equal(usedValues[blockId - 1], block[cursor++]);
            Assert.Equal((byte)0, block[cursor++]);
            Assert.Equal((byte)0, block[cursor++]);
            Assert.Equal(terminators[blockId - 1], cursor);
            if (blockId == 1)
            {
                Assert.Equal(new byte[] { 0x81, 1, 2, 0, 3, 0 }, block[cursor..].ToArray());
            }
            else if (blockId == 4)
            {
                Assert.Equal(new byte[] { 0x83, 4 }, block[cursor..].ToArray());
            }
            else
            {
                Assert.Equal(new byte[] { 0x80, 4, 1, usedValues[blockId - 1] },
                    block[cursor..].ToArray());
            }
        }
    }

    [Fact]
    public void CompactIndexedConditionalAddPreservesCheckedArm()
    {
        var bytes = ReadExample("#### Compact indexed conditional sum");
        Assert.Equal(90, bytes.Length);
        var position = 0;
        Assert.Equal(0u, ReadUInt32(bytes, ref position));
        Assert.Equal(82u, ReadUInt32(bytes, ref position));
        Assert.Equal(new byte[] { 0x08, 3, 4, 0, 2 },
            bytes.AsSpan(position, 5).ToArray());
        position += 5;
        Assert.Equal(1u, ReadUInt32(bytes, ref position));
        Assert.Equal(2u, ReadUInt32(bytes, ref position));
        Assert.Equal(new byte[] { 1, 1, 2, 2, 2, 1, 2, 1 },
            bytes.AsSpan(position, 8).ToArray());
        position += 8;
        Assert.Equal(new byte[] { 0, 3, 4, 4 },
            bytes.AsSpan(position, 4).ToArray());
        position += 4;
        Assert.Equal(new byte[] { 255, 255, 255, 12 },
            bytes.AsSpan(position, 4).ToArray());
        position += 4;
        int[] offsets = [0, 18, 37, 46];
        int[] terminators = [12, 17, 7];
        foreach (var offset in offsets)
        {
            Assert.Equal((byte)offset, bytes[position++]);
        }
        foreach (var offset in terminators)
        {
            Assert.Equal((byte)offset, bytes[position++]);
        }
        Assert.Equal(46, bytes.Length - position);
        byte[][] parameters = [[1, 2, 3], [], []];
        byte[][] liveIns = [[], [2, 3], [2]];
        byte[][] liveOuts = [[2, 3], [], []];
        byte[][] usedValues = [[1, 0], [2, 0, 3, 0, 4, 1], [2, 0]];
        for (var blockId = 1; blockId <= 3; blockId++)
        {
            var block = bytes.AsSpan(position + offsets[blockId - 1],
                offsets[blockId] - offsets[blockId - 1]);
            var cursor = 0;
            var flags = block[cursor++];
            Assert.Equal((byte)(blockId == 1 ? 0x1A : 0x14), flags);
            foreach (var (bit, values) in new[] {
                (0x02, parameters[blockId - 1]),
                (0x04, liveIns[blockId - 1]),
                (0x08, liveOuts[blockId - 1]),
            })
            {
                Assert.Equal(values.Length != 0, (flags & bit) != 0);
                if (values.Length != 0)
                {
                    Assert.Equal((byte)values.Length, block[cursor++]);
                    foreach (var value in values)
                    {
                        Assert.Equal(value, block[cursor++]);
                    }
                }
            }
            var uses = usedValues[blockId - 1];
            Assert.Equal((byte)(uses.Length / 2), block[cursor++]);
            foreach (var value in uses)
            {
                Assert.Equal(value, block[cursor++]);
            }
            Assert.Equal((byte)(blockId == 2 ? 1 : 0), block[cursor++]);
            if (blockId == 2)
            {
                Assert.Equal(12, cursor);
                Assert.Equal(new byte[] { 0x15, 4, 1, 2, 3 },
                    block.Slice(cursor, 5).ToArray());
                cursor += 5;
            }
            Assert.Equal(terminators[blockId - 1], cursor);
            Assert.Equal(blockId == 1
                ? new byte[] { 0x81, 1, 2, 0, 3, 0 }
                : blockId == 2 ? new byte[] { 0x83, 4 }
                : new byte[] { 0x83, 2 }, block[cursor..].ToArray());
        }
    }

    [Fact]
    public void CompactIndexedReaderInspectsConditionalAdd()
    {
        var section = ReadExample("#### Compact indexed conditional sum");
        Assert.Equal((3, 4), InspectIndexed(section));
    }

    [Fact]
    public void CompactIndexedReaderInspectsForwardMergeArguments()
    {
        var section = ReadExample("#### Compact indexed conditional merge");
        Assert.Equal((4, 4), InspectIndexed(section));
    }

    [Theory]
    [InlineData(75, 3)]
    [InlineData(77, 1)]
    [InlineData(89, 2)]
    [InlineData(93, 3)]
    public void CompactIndexedReaderRejectsInvalidMergeEdges(int offset, byte value)
    {
        var section = ReadExample("#### Compact indexed conditional merge");
        section[offset] = value;
        Assert.Throws<InvalidDataException>(() => InspectIndexed(section));
    }

    [Theory]
    [InlineData(24, 1)]
    [InlineData(36, 13)]
    [InlineData(41, 11)]
    [InlineData(44, 0x3A)]
    [InlineData(58, 1)]
    [InlineData(60, 2)]
    [InlineData(77, 4)]
    [InlineData(76, 8)]
    public void CompactIndexedReaderRejectsInvalidIndexesAndEdges(int offset, byte value)
    {
        var section = ReadExample("#### Compact indexed conditional sum");
        section[offset] = value;
        Assert.Throws<InvalidDataException>(() => InspectIndexed(section));
    }

    [Fact]
    public void CompactIndexedReaderBoundsMutatedAndTruncatedRows()
    {
        var example = ReadExample("#### Compact indexed conditional sum");
        for (var length = 8; length < example.Length; length++)
        {
            var section = example[..length];
            BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(4), (uint)(length - 8));
            Assert.Throws<InvalidDataException>(() => InspectIndexed(section));
        }

        for (var offset = 8; offset < example.Length; offset++)
        {
            var section = (byte[])example.Clone();
            section[offset] = 0xFF;
            try
            {
                _ = InspectIndexed(section);
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    [Fact]
    public void CompactIndexedReaderAllowsValueFreeFailureWithNonvoidSignature()
    {
        byte[] section =
        [
            0, 0, 0, 0, 14, 0, 0, 0,
            0x08, 1, 0, 0, 0, 0, 0, 0, 4, 2, 0, 0, 0x87, 1,
        ];
        Assert.Equal((1, 0), InspectIndexed(section, 0));
    }

    [Fact]
    public void CompactIndexedReaderChecksConstantAndCopyDefinitions()
    {
        var section = IndexedConstantCopy();
        Assert.Equal((1, 2), InspectIndexed(section, 0));

        section[35] = 2;
        section[38] = 0xFF;
        Assert.Equal((1, 2), InspectIndexed(section, 0));
        Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(section, 0, secondTypeCode: 0x0D));
    }

    [Fact]
    public void CompactIndexedReaderRejectsDuplicateResolvedTypeMapKeys()
    {
        var original = IndexedConstantCopy();
        var section = new byte[original.Length + sizeof(uint)];
        original.AsSpan(0, 17).CopyTo(section);
        section[12] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(4),
            (uint)(section.Length - 8));
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(17), 1);
        original.AsSpan(17).CopyTo(section.AsSpan(21));

        Assert.Equal((1, 2), InspectIndexed(section, 0));
        Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(section, 0, firstTypeCode: 8, secondTypeCode: 8));
    }

    [Theory]
    [InlineData(13, 0)]
    [InlineData(23, 0)]
    [InlineData(35, 0xF0)]
    [InlineData(37, 0)]
    [InlineData(41, 2)]
    [InlineData(38, 0xFF)]
    public void CompactIndexedReaderRejectsInvalidScalarOperations(int offset, byte value)
    {
        var section = IndexedConstantCopy();
        section[offset] = value;
        if (offset == 38)
        {
            section[13] = 1;
            Assert.Throws<InvalidDataException>(() =>
                InspectIndexed(section, 0, resultTypeSpec: 1));
            return;
        }

        Assert.Throws<InvalidDataException>(() => InspectIndexed(section, 0));
    }

    [Fact]
    public void CompactIndexedReaderBoundsWideBitPatternsAndSignedValues()
    {
        var section = WideIndexedConstant();
        Assert.Equal((1, 2), InspectIndexed(section, 0, secondTypeCode: 9));
        Assert.Throws<InvalidDataException>(() => InspectIndexed(section, 0));

        section[37] = 2;
        BinaryPrimitives.WriteUInt64LittleEndian(section.AsSpan(40), 1UL << 63);
        Assert.Equal((1, 2), InspectIndexed(section, 0, secondTypeCode: 9));
        Assert.Throws<InvalidDataException>(() => InspectIndexed(section, 0));
        Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(section, 0, secondTypeCode: 0x0E));

        section[36] = 0x3C;
        Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(section, 0, secondTypeCode: 9));
    }

    [Fact]
    public void CompactIndexedReaderAllowsWideReferenceOperandsIndependentlyOfBodyLayout()
    {
        var original = IndexedConstantCopy();
        var section = new byte[original.Length + 4];
        original.AsSpan(0, 39).CopyTo(section);
        original.AsSpan(42).CopyTo(section.AsSpan(46));
        section[4] = 40;
        section[26] = 20;
        section[27] = 18;
        byte[] copy = [0xF0, 1, 3, 2, 0, 1, 0];
        copy.CopyTo(section, 39);
        Assert.Equal((1, 2), InspectIndexed(section, 0));

        section[40] = 3;
        Assert.Throws<InvalidDataException>(() => InspectIndexed(section, 0));
        section[40] = 0x31;
        Assert.Throws<InvalidDataException>(() => InspectIndexed(section, 0));
    }

    [Fact]
    public void CompactIndexedReaderChecksPrefixedReturnWidth()
    {
        var original = IndexedConstantCopy();
        var section = new byte[original.Length + 3];
        original.AsSpan(0, 42).CopyTo(section);
        section[4] = 39;
        section[26] = 19;
        byte[] terminator = [0xF0, 1, 0x83, 2, 0];
        terminator.CopyTo(section, 42);
        Assert.Equal((1, 2), InspectIndexed(section, 0));

        section[43] = 5;
        Assert.Throws<InvalidDataException>(() => InspectIndexed(section, 0));
    }

    [Fact]
    public void CompactIndexedReaderChecksPrefixedBranchReferenceAndCountWidths()
    {
        var original = ReadExample("#### Compact indexed conditional sum");
        var section = new byte[original.Length + 7];
        original.AsSpan(0, 56).CopyTo(section);
        original.AsSpan(62).CopyTo(section.AsSpan(69));
        section[4] = 89;
        section[38] = 25;
        section[39] = 44;
        section[40] = 53;
        byte[] terminator =
        [
            0xF0, 5, 0x81, 1, 0, 2, 0, 0, 0, 3, 0, 0, 0,
        ];
        terminator.CopyTo(section, 56);
        Assert.Equal((3, 4), InspectIndexed(section));

        section[57] = 0x25;
        Assert.Throws<InvalidDataException>(() => InspectIndexed(section));
    }

    [Theory]
    [InlineData(new byte[] { 0x82, 1, 1, 0, 3, 0, 2, 0 })]
    [InlineData(new byte[] { 0x82, 1, 2, 0, 3, 0, 1, 2, 0, 2, 0 })]
    [InlineData(new byte[] { 0xF0, 0x10, 0x82, 1, 1, 0, 0, 3, 0, 2, 0 })]
    [InlineData(new byte[] { 0xF0, 0x05, 0x82, 1, 0, 1, 0, 0, 3, 0, 0, 0,
        2, 0, 0, 0 })]
    public void CompactIndexedReaderChecksSwitchEdges(byte[] terminator)
        => Assert.Equal((3, 4), InspectIndexed(IndexedBoolSwitch(terminator)));

    [Fact]
    public void CompactIndexedReaderChecksChar16SwitchPatternWidth()
    {
        byte[] terminator = [0xF0, 0x10, 0x82, 1, 1, 0xFF, 0xFF, 3, 0, 2, 0];
        Assert.Equal((3, 4), InspectIndexed(IndexedBoolSwitch(terminator),
            firstTypeCode: 3));

        byte[] invalid = [0xF0, 0x20, 0x82, 1, 1, 0, 0, 1, 0, 3, 0, 2, 0];
        Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(IndexedBoolSwitch(invalid), firstTypeCode: 3));
    }

    [Fact]
    public void CompactIndexedReaderChecksIntegerSwitchPatternWidth()
    {
        byte[] terminator = [0xF0, 0x20, 0x82, 1, 1, 0xFF, 0xFF, 0xFF, 0xFF,
            3, 0, 2, 0];
        Assert.Equal((3, 4), InspectIndexed(IndexedBoolSwitch(terminator),
            firstTypeCode: 8, secondTypeCode: 0x0D));

        byte[] invalid = [0xF0, 0x30, 0x82, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0,
            3, 0, 2, 0];
        Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(IndexedBoolSwitch(invalid),
                firstTypeCode: 8, secondTypeCode: 0x0D));
    }

    [Theory]
    [InlineData(new byte[] { 0x82, 1, 1, 2, 3, 0, 2, 0 })]
    [InlineData(new byte[] { 0x82, 1, 2, 1, 3, 0, 0, 2, 0, 2, 0 })]
    [InlineData(new byte[] { 0x82, 1, 10, 0, 3, 0, 2, 0 })]
    [InlineData(new byte[] { 0x82, 1, 1, 0, 1, 0, 2, 0 })]
    [InlineData(new byte[] { 0xF0, 0x40, 0x82, 1, 1, 0, 3, 0, 2, 0 })]
    public void CompactIndexedReaderRejectsMalformedSwitch(byte[] terminator)
        => Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(IndexedBoolSwitch(terminator)));

    private static byte[] IndexedBoolSwitch(byte[] terminator)
    {
        var original = ReadExample("#### Compact indexed conditional sum");
        var extra = terminator.Length - 6;
        var section = new byte[original.Length + extra];
        original.AsSpan(0, 56).CopyTo(section);
        terminator.CopyTo(section, 56);
        original.AsSpan(62).CopyTo(section.AsSpan(56 + terminator.Length));
        section[4] = (byte)(82 + extra);
        section[38] = (byte)(18 + extra);
        section[39] = (byte)(37 + extra);
        section[40] = (byte)(46 + extra);
        return section;
    }

    [Theory]
    [InlineData(new byte[] { 4, 2, 2, 1 }, new byte[] { 2 }, (byte)2)]
    [InlineData(new byte[] { 5, 3, 4, 1, 2 }, new byte[] { 2, 2 }, (byte)1)]
    [InlineData(new byte[] { 5, 3, 6, 1, 2 }, new byte[] { 2, 2 }, (byte)1)]
    [InlineData(new byte[] { 5, 3, 10, 1, 2 }, new byte[] { 2, 2 }, (byte)2)]
    [InlineData(new byte[] { 5, 3, 4, 1, 2 }, new byte[] { 1, 1 }, (byte)1)]
    [InlineData(new byte[] { 7, 4, 1, 2, 3 }, new byte[] { 1, 2, 2 }, (byte)2)]
    [InlineData(new byte[] { 4, 2, 1, 1 }, new byte[] { 1 }, (byte)1)]
    [InlineData(new byte[] { 0x15, 3, 2, 1, 2 }, new byte[] { 2, 2 }, (byte)2)]
    [InlineData(new byte[] { 0x15, 3, 3, 1, 2 }, new byte[] { 2, 2 }, (byte)2)]
    [InlineData(new byte[] { 0x15, 3, 4, 1, 2 }, new byte[] { 2, 2 }, (byte)2)]
    [InlineData(new byte[] { 0x15, 3, 5, 1, 2 }, new byte[] { 2, 2 }, (byte)2)]
    [InlineData(new byte[] { 0x15, 3, 6, 1, 2 }, new byte[] { 2, 3 }, (byte)2)]
    [InlineData(new byte[] { 0x15, 3, 7, 1, 2 }, new byte[] { 2, 3 }, (byte)2)]
    [InlineData(new byte[] { 0x16, 2, 3, 1 }, new byte[] { 2 }, (byte)3)]
    [InlineData(new byte[] { 0x16, 2, 2, 1 }, new byte[] { 3 }, (byte)2)]
    [InlineData(new byte[] { 0x17, 2, 1 }, new byte[] { 2 }, (byte)2)]
    public void CompactIndexedReaderChecksScalarOperatorResults(
        byte[] operation, byte[] parameters, byte resultType)
    {
        var section = IndexedScalarOperation(operation, parameters, resultType);
        Assert.Equal((1, parameters.Length + 1),
            InspectIndexed(section, parameters.Length, resultTypeSpec: resultType,
                parameterTypeIds: parameters, thirdTypeCode: 0x0D));
    }

    [Theory]
    [InlineData(new byte[] { 4, 2, 0, 1 }, new byte[] { 2 }, (byte)2)]
    [InlineData(new byte[] { 5, 3, 13, 1, 2 }, new byte[] { 2, 2 }, (byte)2)]
    [InlineData(new byte[] { 5, 3, 4, 1, 2 }, new byte[] { 2, 2 }, (byte)2)]
    [InlineData(new byte[] { 7, 4, 2, 1, 3 }, new byte[] { 1, 2, 2 }, (byte)2)]
    [InlineData(new byte[] { 5, 3, 1, 1, 2 }, new byte[] { 1, 1 }, (byte)1)]
    [InlineData(new byte[] { 0x15, 3, 6, 1, 2 }, new byte[] { 2, 2 }, (byte)2)]
    [InlineData(new byte[] { 0x15, 3, 8, 1, 2 }, new byte[] { 2, 2 }, (byte)2)]
    [InlineData(new byte[] { 0x16, 2, 1, 1 }, new byte[] { 2 }, (byte)2)]
    public void CompactIndexedReaderRejectsInvalidScalarOperatorResults(
        byte[] operation, byte[] parameters, byte resultType)
        => Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(IndexedScalarOperation(operation, parameters, resultType),
                parameters.Length, resultTypeSpec: resultType,
                parameterTypeIds: parameters, thirdTypeCode: 0x0D));

    [Theory]
    [InlineData(1, (byte)0x0E, (byte)8)]
    [InlineData(2, (byte)0x0E, (byte)8)]
    [InlineData(3, (byte)0x0D, (byte)8)]
    [InlineData(3, (byte)0x0E, (byte)9)]
    public void CompactIndexedReaderAcceptsIntegerConvertPolicies(byte policy,
        byte destinationType, byte sourceType)
    {
        var operation = new byte[] { 6, 2, 3, policy, 1 };
        Assert.Equal((1, 2), InspectIndexed(
            IndexedScalarOperation(operation, [2], 3), 1,
            secondTypeCode: sourceType, resultTypeSpec: 3,
            parameterTypeIds: [2], thirdTypeCode: destinationType));
    }

    [Fact]
    public void CompactIndexedReaderAcceptsPrefixedConvertReferenceWidth()
    {
        byte[] operation = [0xF0, 1, 6, 2, 0, 3, 0, 1, 1, 0];
        Assert.Equal((1, 2), InspectIndexed(
            IndexedScalarOperation(operation, [2], 3), 1, resultTypeSpec: 3,
            parameterTypeIds: [2], thirdTypeCode: 0x0D));
    }

    [Theory]
    [InlineData(new byte[] { 6, 2, 3, 0, 1 }, new byte[] { 2 }, (byte)3, (byte)0x0D)]
    [InlineData(new byte[] { 6, 2, 3, 4, 1 }, new byte[] { 2 }, (byte)3, (byte)0x0D)]
    [InlineData(new byte[] { 6, 2, 2, 1, 1 }, new byte[] { 2 }, (byte)3, (byte)0x0D)]
    [InlineData(new byte[] { 6, 2, 3, 3, 1 }, new byte[] { 2 }, (byte)3, (byte)0x0E)]
    [InlineData(new byte[] { 6, 2, 3, 1, 1 }, new byte[] { 1 }, (byte)3, (byte)0x0D)]
    public void CompactIndexedReaderRejectsInvalidIntegerConvert(
        byte[] operation, byte[] parameters, byte resultType, byte destinationType)
        => Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(IndexedScalarOperation(operation, parameters, resultType),
                parameters.Length, resultTypeSpec: resultType,
                parameterTypeIds: parameters, thirdTypeCode: destinationType));

    [Theory]
    [InlineData(new byte[] { 6, 2, 3, 1, 1 }, new byte[] { 2 }, (byte)3)]
    [InlineData(new byte[] { 6, 2, 2, 2, 1 }, new byte[] { 3 }, (byte)2)]
    public void CompactIndexedReaderRejectsOptionalIntegerConvert(
        byte[] operation, byte[] parameters, byte resultType)
        => Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(IndexedScalarOperation(operation, parameters, resultType),
                parameters.Length, resultTypeSpec: resultType,
                parameterTypeIds: parameters, thirdTypeOptional: true));

    [Theory]
    [InlineData((byte)3, (byte)8)]
    [InlineData((byte)3, (byte)0x0E)]
    [InlineData((byte)8, (byte)3)]
    [InlineData((byte)0x0D, (byte)3)]
    [InlineData((byte)9, (byte)3)]
    public void CompactIndexedReaderChecksChar16RangeConvert(byte source, byte destination)
    {
        var section = IndexedScalarOperation([0x16, 2, 3, 1], [2], 3);
        Assert.Equal((1, 2), InspectIndexed(section, parameterCount: 1,
            secondTypeCode: source, thirdTypeCode: destination,
            resultTypeSpec: 3, parameterTypeIds: [2]));
    }

    [Theory]
    [InlineData(new byte[] { 0x16, 2, 3, 1 }, new byte[] { 2 }, (byte)3)]
    [InlineData(new byte[] { 0x16, 2, 2, 1 }, new byte[] { 3 }, (byte)2)]
    public void CompactIndexedReaderRejectsOptionalChar16RangeConvert(
        byte[] operation, byte[] parameters, byte resultType)
        => Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(IndexedScalarOperation(operation, parameters, resultType),
                parameters.Length, secondTypeCode: 3, resultTypeSpec: resultType,
                parameterTypeIds: parameters, thirdTypeOptional: true));

    [Theory]
    [InlineData(new byte[] { 1, 1, 3, 42 }, new byte[] { }, (byte)3, false)]
    [InlineData(new byte[] { 0xF0, 0x10, 1, 1, 3, 0xFF, 0xFF },
        new byte[] { }, (byte)3, false)]
    [InlineData(new byte[] { 3, 2, 1 }, new byte[] { 3 }, (byte)3, false)]
    [InlineData(new byte[] { 5, 3, 4, 1, 2 }, new byte[] { 3, 3 }, (byte)1, false)]
    [InlineData(new byte[] { 5, 3, 5, 1, 2 }, new byte[] { 3, 3 }, (byte)1, false)]
    [InlineData(new byte[] { 8, 2, 3, 1 }, new byte[] { 2 }, (byte)3, true)]
    [InlineData(new byte[] { 9, 1, 3 }, new byte[] { }, (byte)3, true)]
    [InlineData(new byte[] { 0x0A, 2, 1 }, new byte[] { 3 }, (byte)1, true)]
    public void CompactIndexedReaderPreservesChar16CodeUnits(
        byte[] operation, byte[] parameters, byte resultType, bool optional)
    {
        Assert.Equal((1, parameters.Length + 1),
            InspectIndexed(IndexedScalarOperation(operation, parameters, resultType),
                parameters.Length, secondTypeCode: optional ? (byte)3 : (byte)8,
                resultTypeSpec: resultType,
                parameterTypeIds: parameters, thirdTypeCode: optional ? null : (byte)3,
                thirdTypeOptional: optional));
    }

    [Theory]
    [InlineData(new byte[] { 0xF0, 0x20, 1, 1, 3, 0, 0, 1, 0 },
        new byte[] { }, (byte)3)]
    [InlineData(new byte[] { 2, 1, 3, 42 }, new byte[] { }, (byte)3)]
    [InlineData(new byte[] { 4, 2, 2, 1 }, new byte[] { 3 }, (byte)3)]
    [InlineData(new byte[] { 5, 3, 6, 1, 2 }, new byte[] { 3, 3 }, (byte)1)]
    [InlineData(new byte[] { 5, 3, 10, 1, 2 }, new byte[] { 3, 3 }, (byte)3)]
    [InlineData(new byte[] { 6, 2, 3, 1, 1 }, new byte[] { 3 }, (byte)3)]
    public void CompactIndexedReaderRejectsIntegerOperationsOnChar16(
        byte[] operation, byte[] parameters, byte resultType)
        => Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(IndexedScalarOperation(operation, parameters, resultType),
                parameters.Length, resultTypeSpec: resultType,
                parameterTypeIds: parameters, thirdTypeCode: 3));

    [Fact]
    public void CompactIndexedReaderRejectsUnsignedCheckedNegate()
    {
        var section = IndexedScalarOperation([0x17, 2, 1], [2], 2);
        Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(section, 1, secondTypeCode: 0x0D,
                parameterTypeIds: [2]));
    }

    [Theory]
    [InlineData(new byte[] { 8, 2, 3, 1 }, new byte[] { 2 }, (byte)3)]
    [InlineData(new byte[] { 9, 1, 3 }, new byte[] { }, (byte)3)]
    [InlineData(new byte[] { 0x0A, 2, 1 }, new byte[] { 3 }, (byte)1)]
    [InlineData(new byte[] { 3, 2, 1 }, new byte[] { 3 }, (byte)3)]
    [InlineData(new byte[] { 7, 4, 1, 2, 3 }, new byte[] { 1, 3, 3 }, (byte)3)]
    public void CompactIndexedReaderPreservesOptionalPresence(
        byte[] operation, byte[] parameters, byte resultType)
    {
        var section = IndexedScalarOperation(operation, parameters, resultType);
        Assert.Equal((1, parameters.Length + 1),
            InspectIndexed(section, parameters.Length, resultTypeSpec: resultType,
                parameterTypeIds: parameters, thirdTypeOptional: true));
    }

    [Fact]
    public void CompactIndexedReaderAllowsOnlyOptionalChar16Equality()
    {
        var optionalCharEquality = IndexedScalarOperation(
            [5, 3, 4, 1, 2], [3, 3], 1);
        Assert.Equal((1, 3), InspectIndexed(optionalCharEquality, 2,
            resultTypeSpec: 1, parameterTypeIds: [3, 3], secondTypeCode: 3,
            thirdTypeOptional: true));

        var optionalIntegerEquality = IndexedScalarOperation(
            [5, 3, 4, 1, 2], [3, 3], 1);
        Assert.Throws<InvalidDataException>(() => InspectIndexed(optionalIntegerEquality, 2,
            resultTypeSpec: 1, parameterTypeIds: [3, 3], secondTypeCode: 8,
            thirdTypeOptional: true));
    }

    [Theory]
    [InlineData(new byte[] { 8, 2, 3, 1 }, new byte[] { 3 }, (byte)3)]
    [InlineData(new byte[] { 9, 1, 2 }, new byte[] { }, (byte)3)]
    [InlineData(new byte[] { 0x0A, 2, 1 }, new byte[] { 2 }, (byte)1)]
    [InlineData(new byte[] { 1, 1, 3, 1 }, new byte[] { }, (byte)3)]
    public void CompactIndexedReaderRejectsOptionalScalarConfusion(
        byte[] operation, byte[] parameters, byte resultType)
        => Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(IndexedScalarOperation(operation, parameters, resultType),
                parameters.Length, resultTypeSpec: resultType,
                parameterTypeIds: parameters, thirdTypeOptional: true));

    [Fact]
    public void CompactIndexedReaderRejectsCheckedExceptionPolicy()
    {
        var section = IndexedScalarOperation([0x15, 3, 1, 1, 2], [2, 2], 2);
        Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(section, 2, parameterTypeIds: [2, 2],
                runtimePolicy: 1));
    }

    [Fact]
    public void CompactIndexedReaderIndexesTwoResultsFromOneCheckedOperation()
    {
        Assert.Equal((1, 4), InspectIndexed(IndexedDivRem(), 2,
            parameterTypeIds: [2, 2]));
    }

    [Theory]
    [InlineData(26, 1)]
    [InlineData(34, 13)]
    [InlineData(52, 3)]
    [InlineData(53, 3)]
    public void CompactIndexedReaderRejectsMalformedDualResults(int offset, byte value)
    {
        var section = IndexedDivRem();
        section[offset] = value;
        Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(section, 2, parameterTypeIds: [2, 2]));
    }

    [Theory]
    [InlineData(new byte[] { 0x12, 1, 1, 0 }, new byte[0], 1)]
    [InlineData(new byte[] { 0x12, 2, 1, 1, 1 }, new byte[] { 2 }, 2)]
    [InlineData(new byte[] { 0xF0, 0x04, 0x12, 2, 1, 1, 0, 1 }, new byte[] { 2 }, 2)]
    [InlineData(new byte[] { 0xF0, 0x05, 0x12, 2, 0, 1, 0, 1, 0, 1, 0 },
        new byte[] { 2 }, 2)]
    public void CompactIndexedReaderChecksLocalScalarCalls(
        byte[] operation, byte[] parameters, int values)
        => Assert.Equal((1, values), InspectIndexed(
            IndexedScalarOperation(operation, parameters, 2),
            parameters.Length, resultTypeSpec: 2, parameterTypeIds: parameters));

    [Fact]
    public void CompactIndexedReaderPreservesOptionalCallSignatures()
        => Assert.Equal((1, 2), InspectIndexed(
            IndexedScalarOperation([0x12, 2, 1, 1, 1], [3], 3),
            parameterCount: 1, resultTypeSpec: 3, parameterTypeIds: [3],
            thirdTypeOptional: true));

    [Fact]
    public void CompactIndexedReaderResolvesTheSecondLocalMethod()
    {
        var section = IndexedScalarOperation([0x12, 2, 2, 1, 1], [2], 2);
        Assert.Equal((1, 2), InspectIndexed(section, parameterCount: 1,
            parameterTypeIds: [2], secondMethod: true));
        Assert.Equal((1, 2), InspectIndexed(section, parameterCount: 1,
            parameterTypeIds: [2], secondMethod: true, ownedTarget: true));
        Assert.Throws<InvalidDataException>(() => InspectIndexed(section,
            parameterCount: 1, parameterTypeIds: [2], secondMethod: true,
            importedTarget: true));
        Assert.Equal((1, 2), InspectIndexed(section, parameterCount: 1,
            parameterTypeIds: [2], secondMethod: true,
            importedTarget: true, expectedTarget: true));
        Assert.Equal((1, 2), InspectIndexed(section, parameterCount: 1,
            parameterTypeIds: [2], secondMethod: true,
            importedTarget: true, expectedTarget: true, ownedTarget: true));
        Assert.Throws<InvalidDataException>(() => InspectIndexed(section,
            parameterCount: 1, parameterTypeIds: [2], secondMethod: true,
            importedTarget: true, expectedTarget: true, ownedTarget: true,
            privateOwner: true));
        Assert.Throws<InvalidDataException>(() => InspectIndexed(section,
            parameterCount: 1, parameterTypeIds: [2], secondMethod: true,
            expectedTarget: true));
    }

    [Fact]
    public void CompactIndexedReaderChecksPackedDirectCallIdentity()
    {
        var body = IndexedScalarOperation([0x12, 2, 0, 2, 0, 0, 0x16, 1, 1],
            [2], 2);
        Assert.Equal((1, 2), InspectIndexed(body, parameterCount: 1,
            parameterTypeIds: [2], secondMethod: true, packedTarget: true));
        Assert.Equal((1, 2), InspectIndexed(body, parameterCount: 1,
            parameterTypeIds: [2], secondMethod: true, packedTarget: true,
            callerTargetRequirement: 1));
        Assert.Throws<InvalidDataException>(() => InspectIndexed(body,
            parameterCount: 1, parameterTypeIds: [2], secondMethod: true,
            packedTarget: true, packedTargetRuntimePolicy: 1));
        Assert.Throws<InvalidDataException>(() => InspectIndexed(body,
            parameterCount: 1, parameterTypeIds: [2], secondMethod: true,
            packedTarget: true, packedTargetRequirement: 1));
        Assert.Equal((1, 2), InspectIndexed(body, parameterCount: 1,
            parameterTypeIds: [2], secondMethod: true, packedTarget: true,
            callerTargetRequirement: 1, packedTargetRequirement: 1));
        Assert.Equal((1, 2), InspectIndexed(body, parameterCount: 1,
            parameterTypeIds: [2], secondMethod: true, packedTarget: true,
            ownedTarget: true));
        Assert.Throws<InvalidDataException>(() => InspectIndexed(body,
            parameterCount: 1, parameterTypeIds: [2], secondMethod: true,
            packedTarget: true, ownedTarget: true, privateOwner: true));
        var wide = IndexedScalarOperation(
            [0xF0, 5, 0x12, 2, 0, 0, 0, 2, 0, 0, 0x16, 1, 0, 1, 0], [2], 2);
        Assert.Equal((1, 2), InspectIndexed(wide, parameterCount: 1,
            parameterTypeIds: [2], secondMethod: true, packedTarget: true));

        foreach (var (offset, value) in new (int, byte)[]
        {
            (3, 1),
            (3, 3),
            (6, 0x15),
        })
        {
            var invalid = IndexedScalarOperation([0x12, 2, 0, 2, 0, 0, 0x16, 1, 1],
                [2], 2);
            var direct = invalid.AsSpan().IndexOf(new byte[] { 0x12, 2, 0, 2, 0, 0, 0x16, 1, 1 });
            Assert.True(direct >= 0);
            invalid[direct + offset] = value;
            Assert.Throws<InvalidDataException>(() => InspectIndexed(invalid,
                parameterCount: 1, parameterTypeIds: [2],
                secondMethod: true, packedTarget: true));
        }
        Assert.Throws<InvalidDataException>(() => InspectIndexed(body,
            parameterCount: 1, parameterTypeIds: [2], secondMethod: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactIndexedReaderRejectsGenericExternalOwner(bool packedTarget)
    {
        var operation = packedTarget
            ? new byte[] { 0x12, 2, 0, 2, 0, 0, 0x16, 1, 1 }
            : [0x12, 2, 2, 1, 1];
        var section = IndexedScalarOperation(operation, [2], 2);
        Assert.Equal((1, 2), InspectIndexed(section, parameterCount: 1,
            parameterTypeIds: [2], secondMethod: true, ownedTarget: true,
            packedTarget: packedTarget, importedTarget: !packedTarget,
            expectedTarget: !packedTarget));
        Assert.Throws<InvalidDataException>(() => InspectIndexed(section,
            parameterCount: 1, parameterTypeIds: [2], secondMethod: true,
            ownedTarget: true, packedTarget: packedTarget, importedTarget: !packedTarget,
            expectedTarget: !packedTarget, genericOwner: true));
    }

    [Theory]
    [InlineData(new byte[] { 0x12, 1, 0, 0 }, new byte[0])]
    [InlineData(new byte[] { 0x12, 1, 2, 0 }, new byte[0])]
    [InlineData(new byte[] { 0x12, 1, 1, 1, 1 }, new byte[0])]
    [InlineData(new byte[] { 0x12, 2, 1, 0 }, new byte[] { 2 })]
    [InlineData(new byte[] { 0x12, 0, 1, 0 }, new byte[0])]
    [InlineData(new byte[] { 0xF0, 0x0C, 0x12, 1, 1, 0 }, new byte[0])]
    public void CompactIndexedReaderRejectsInvalidLocalScalarCalls(
        byte[] operation, byte[] parameters)
        => Assert.Throws<InvalidDataException>(() =>
            InspectIndexed(IndexedScalarOperation(operation, parameters, 2),
                parameters.Length, resultTypeSpec: 2, parameterTypeIds: parameters));

    [Fact]
    public void CompactIndexedReaderAllowsVoidCallsWithoutValueDefinitions()
    {
        byte[] row =
        [
            0x08, 1, 0, 0, 0,
            0, 0, 0, 8, 6,
            0, 1, 0x12, 0, 1, 0, 0x83, 0,
        ];
        var section = new byte[row.Length + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(4), (uint)row.Length);
        row.CopyTo(section, 8);
        Assert.Equal((1, 0), InspectIndexed(section, parameterCount: 0,
            resultTypeSpec: 1, firstTypeCode: 0));
    }

    [Fact]
    public void CompactIndexedReaderChecksLoopRegionAndBackedge()
    {
        var body = IndexedScalarLoop();
        Assert.Equal((4, 7), InspectIndexed(body, parameterCount: 1,
            resultTypeSpec: 2, parameterTypeIds: [2]));
        Assert.Equal((4, 7), InspectIndexed(IndexedScalarLoop(entryHeader: true),
            parameterCount: 1, resultTypeSpec: 2, parameterTypeIds: [2]));
        Assert.Equal((7, 13), InspectIndexed(IndexedScalarLoop(siblingLoop: true),
            parameterCount: 1, resultTypeSpec: 2, parameterTypeIds: [2]));

        var region = body.AsSpan().IndexOf(new byte[] { 1, 0, 2, 4, 0 });
        Assert.True(region >= 0);
        foreach (var (offset, replacement) in new[]
            { (0, (byte)2), (2, (byte)3), (3, (byte)3), (4, (byte)1) })
        {
            var invalid = (byte[])body.Clone();
            invalid[region + offset] = replacement;
            Assert.Throws<InvalidDataException>(() => InspectIndexed(invalid,
                parameterCount: 1, resultTypeSpec: 2, parameterTypeIds: [2]));
        }

        var backedge = body.AsSpan().LastIndexOf(new byte[] { 0x80, 2, 1, 6 });
        Assert.True(backedge >= 0);
        var wrongTarget = (byte[])body.Clone();
        wrongTarget[backedge + 1] = 1;
        Assert.Throws<InvalidDataException>(() => InspectIndexed(wrongTarget,
            parameterCount: 1, resultTypeSpec: 2, parameterTypeIds: [2]));
        var missingBackedge = (byte[])body.Clone();
        missingBackedge[backedge + 1] = 4;
        Assert.Throws<InvalidDataException>(() => InspectIndexed(missingBackedge,
            parameterCount: 1, resultTypeSpec: 2, parameterTypeIds: [2]));
        Assert.Throws<InvalidDataException>(() => InspectIndexed(
            IndexedScalarLoop(ghostLiveness: true), parameterCount: 1,
            resultTypeSpec: 2, parameterTypeIds: [2]));
    }

    [Fact]
    public void CompactIndexedReaderRejectsMalformedSiblingLoopRegions()
    {
        var body = IndexedScalarLoop(siblingLoop: true);
        var region = body.AsSpan().IndexOf(new byte[] { 1, 0, 5, 7, 0 });
        Assert.True(region >= 0);
        foreach (var (offset, replacement) in new[]
            { (1, (byte)1), (2, (byte)2), (3, (byte)4) })
        {
            var invalid = (byte[])body.Clone();
            invalid[region + offset] = replacement;
            Assert.Throws<InvalidDataException>(() => InspectIndexed(invalid,
                parameterCount: 1, resultTypeSpec: 2, parameterTypeIds: [2]));
        }

        var backedge = body.AsSpan().LastIndexOf(new byte[] { 0x80, 5, 1, 12 });
        Assert.True(backedge >= 0);
        var noSecondBackedge = (byte[])body.Clone();
        noSecondBackedge[backedge + 1] = 7;
        Assert.Throws<InvalidDataException>(() => InspectIndexed(noSecondBackedge,
            parameterCount: 1, resultTypeSpec: 2, parameterTypeIds: [2]));
    }

    [Fact]
    public void CompactIndexedReaderChecksNestedLoopRegions()
    {
        var body = IndexedNestedLoop();
        Assert.Equal((6, 1), InspectIndexed(body, parameterCount: 1,
            resultTypeSpec: 1, parameterTypeIds: [1]));

        var innerRegion = body.AsSpan().IndexOf(new byte[] { 1, 1, 2, 4, 0 });
        Assert.True(innerRegion >= 0);
        foreach (var (offset, replacement) in new[]
            { (1, (byte)0), (1, (byte)2), (2, (byte)3), (3, (byte)5) })
        {
            var invalid = (byte[])body.Clone();
            invalid[innerRegion + offset] = replacement;
            Assert.Throws<InvalidDataException>(() => InspectIndexed(invalid,
                parameterCount: 1, resultTypeSpec: 1, parameterTypeIds: [1]));
        }

        var outerRegion = body.AsSpan().IndexOf(new byte[] { 1, 0, 1, 6, 0 });
        Assert.True(outerRegion >= 0);
        var wrongOuterExit = (byte[])body.Clone();
        wrongOuterExit[outerRegion + 3] = 5;
        Assert.Throws<InvalidDataException>(() => InspectIndexed(wrongOuterExit,
            parameterCount: 1, resultTypeSpec: 1, parameterTypeIds: [1]));

        var skipInnerHeader = (byte[])body.Clone();
        var outerBranch = body.AsSpan().IndexOf(new byte[] { 0x81, 1, 2, 0, 6, 0 });
        Assert.True(outerBranch >= 0);
        skipInnerHeader[outerBranch + 2] = 3;
        Assert.Throws<InvalidDataException>(() => InspectIndexed(skipInnerHeader,
            parameterCount: 1, resultTypeSpec: 1, parameterTypeIds: [1]));

        var skipInnerExit = (byte[])body.Clone();
        var innerBranch = body.AsSpan().IndexOf(new byte[] { 0x81, 1, 3, 0, 4, 0 });
        Assert.True(innerBranch >= 0);
        skipInnerExit[innerBranch + 4] = 5;
        Assert.Throws<InvalidDataException>(() => InspectIndexed(skipInnerExit,
            parameterCount: 1, resultTypeSpec: 1, parameterTypeIds: [1]));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CompactIndexedReaderRejectsMissingNestedLoopPropagation(
        bool omitLiveIn, bool omitLiveOut)
        => Assert.Throws<InvalidDataException>(() => InspectIndexed(
            IndexedNestedLoop(omitPropagatedLiveIn: omitLiveIn, omitPropagatedLiveOut: omitLiveOut),
            parameterCount: 1, resultTypeSpec: 1, parameterTypeIds: [1]));

    [Fact]
    public void CompactIndexedReaderRejectsUnusedLivenessCirculatingThroughNestedLoops()
    {
        Assert.Equal((6, 2), InspectIndexed(IndexedNestedLoop(unusedValue: true),
            parameterCount: 1, resultTypeSpec: 1, parameterTypeIds: [1]));
        Assert.Throws<InvalidDataException>(() => InspectIndexed(
            IndexedNestedLoop(ghostLiveness: true), parameterCount: 1,
            resultTypeSpec: 1, parameterTypeIds: [1]));
    }

    internal static byte[] IndexedNestedLoop(bool ghostLiveness = false,
        bool omitPropagatedLiveIn = false, bool omitPropagatedLiveOut = false,
        bool unusedValue = false)
    {
        unusedValue |= ghostLiveness;
        byte[][] blocks =
        [
            [0x1B, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0x81, 1, 2, 0, 6, 0],
            [0x1D, 2, 1, 1, 1, 1, 1, 1, 0, 0, 0x81, 1, 3, 0, 4, 0],
            [0x0D, 2, 1, 1, 1, 1, 0, 0x80, 2, 0],
            [0x0D, 1, 1, 1, 1, 1, 0, 0x80, 5, 0],
            [0x1D, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0x80, 1, 1, 1],
            [0x14, 1, 1, 1, 1, 0, 0, 0x83, 1],
        ];
        if (unusedValue)
        {
            blocks[0] = [0x1B, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 1, 0, 0x81, 1, 2, 0, 6, 0];
        }
        if (ghostLiveness)
        {
            // Value 2 is an unused entry constant: advertised cyclic liveness has no use seed.
            blocks[0] = [0x1B, 1, 1, 1, 2, 1, 2, 1, 1, 1, 1, 1, 2, 1, 0, 0x81, 1, 2, 0, 6, 0];
            blocks[1] = [0x1D, 2, 2, 1, 2, 2, 1, 2, 1, 1, 0, 0, 0x81, 1, 3, 0, 4, 0];
            blocks[2] = [0x0D, 2, 2, 1, 2, 2, 1, 2, 0, 0x80, 2, 0];
        }
        if (omitPropagatedLiveIn)
        {
            blocks[2] = [0x09, 2, 1, 1, 0, 0x80, 2, 0];
        }
        if (omitPropagatedLiveOut)
        {
            blocks[2] = [0x05, 2, 1, 1, 0, 0x80, 2, 0];
        }
        var row = new List<byte>
        {
            0x08, 6, (byte)(unusedValue ? 2 : 1), 2, 1,
            1, 0, 0, 0,
            1, 0, 1, 6, 0,
            1, 1, 2, 4, 0,
            1, 4,
        };
        if (unusedValue)
        {
            row.AddRange([1, 0]);
        }
        row.Add(0);
        for (var block = 0; block < blocks.Length; block++)
        {
            row.Add((byte)(unusedValue ? 2 : 1));
        }
        row.Add(0xFF);
        if (unusedValue)
        {
            row.Add((byte)(ghostLiveness ? 11 : 10));
        }
        row.Add(0);
        var offset = 0;
        foreach (var block in blocks)
        {
            offset += block.Length;
            row.Add((byte)offset);
        }

        byte[] terminatorLengths = [6, 6, 3, 3, 4, 2];
        for (var index = 0; index < blocks.Length; index++)
        {
            row.Add((byte)(blocks[index].Length - terminatorLengths[index]));
        }

        foreach (var block in blocks)
        {
            row.AddRange(block);
        }

        return TrRmVariableTableWriter.EncodeBodies([row.ToArray()]);
    }

    internal static byte[] IndexedScalarLoop(bool ghostLiveness = false,
        bool entryHeader = false, bool siblingLoop = false)
    {
        var blocks = new List<byte[]>();
        var definitions = siblingLoop
            ? new byte[] { 0xFF, 0, 0xFF, 0, 0, 0, 0xFF, 0xFF, 0, 0, 0, 0, 0xFF }
            : new byte[] { 0xFF, 0, 0xFF, 0, 0, 0, 0xFF };
        var terminators = new List<byte>();
        void AddBlock(byte[] header, byte[][] operations, byte[] terminator, params int[] results)
        {
            var block = new List<byte>(header);
            for (var index = 0; index < operations.Length; index++)
            {
                if (index < results.Length)
                {
                    definitions[results[index] - 1] = (byte)block.Count;
                }

                block.AddRange(operations[index]);
            }

            terminators.Add((byte)block.Count);
            block.AddRange(terminator);
            blocks.Add([.. block]);
        }

        AddBlock(entryHeader
            ? [0x1B, 1, 1, 1, 2, 1, 2, 1, 2, 1, 1]
            : [0x1A, 1, 1, 2, 1, 2, 1, 2, 1, 1],
            [[2, 2, 1, 0]], [0x80, 2, 1, 2], 2);
        AddBlock(ghostLiveness
            ? [0x1F, 1, 1, 3, 2, 1, 2, 3, 1, 2, 3, 3, 1, 0, 3, 1, 4, 1, 1]
            : entryHeader
                ? [0x1F, 1, 1, 3, 1, 1, 1, 3, 3, 1, 0, 3, 1, 4, 1, 1]
                : [0x1F, 1, 1, 3, 1, 1, 2, 1, 3, 3, 1, 0, 3, 1, 4, 1, 1],
            [[5, 4, 6, 3, 1]], [0x81, 4, 3, 0, 4, 1, 3], 4);
        AddBlock(ghostLiveness
            ? [0x1D, 1, 3, 1, 2, 3, 3, 1, 2, 6, 3, 3, 1, 5, 1, 6, 2, 2]
            : entryHeader
                ? [0x1D, 1, 1, 3, 1, 6, 3, 3, 1, 5, 1, 6, 2, 2]
                : [0x1D, 1, 2, 1, 3, 2, 1, 6, 3, 3, 1, 5, 1, 6, 2, 2],
            [[2, 5, 1, 1], [0x15, 6, 1, 3, 5]],
            [0x80, (byte)(entryHeader ? 1 : 2), 1, 6], 5, 6);
        AddBlock(siblingLoop
            ? [0x1A, 1, 7, 1, 7, 1, 7, 0, 0]
            : [0x12, 1, 7, 1, 7, 0, 0],
            [], siblingLoop ? [0x80, 5, 1, 7] : [0x83, 7]);
        if (siblingLoop)
        {
            AddBlock([0x1B, 2, 1, 8, 1, 8, 3, 8, 2, 9, 1, 10, 2, 2],
                [[2, 9, 1, 0], [5, 10, 6, 8, 9]],
                [0x81, 10, 6, 0, 7, 1, 8], 9, 10);
            AddBlock([0x1D, 2, 1, 8, 1, 12, 3, 8, 1, 11, 1, 12, 2, 2],
                [[2, 11, 1, 1], [0x15, 12, 1, 8, 11]],
                [0x80, 5, 1, 12], 11, 12);
            AddBlock([0x12, 1, 13, 1, 13, 0, 0],
                [], [0x83, 13]);
        }

        var row = new List<byte> { 0x08, (byte)blocks.Count,
            (byte)definitions.Length, (byte)(siblingLoop ? 2 : 1), 2,
            2, 0, 0, 0, 1, 0, 0, 0,
            1, 0, (byte)(entryHeader ? 1 : 2), 4, 0 };
        if (siblingLoop)
        {
            row.AddRange([1, 0, 5, 7, 0]);
        }
        var types = siblingLoop
            ? new byte[] { 1, 1, 1, 2, 1, 1, 1, 1, 1, 2, 1, 1, 1 }
            : new byte[] { 1, 1, 1, 2, 1, 1, 1 };
        var uses = siblingLoop
            ? new byte[] { 1, 1, 2, 1, 1, 1, 1, 2, 1, 1, 1, 1, 1 }
            : new byte[] { 1, 1, 2, 1, 1, 1, 1 };
        for (var value = 0; value < types.Length; value++)
        {
            row.Add(types[value]);
            row.Add(uses[value]);
        }
        row.AddRange(siblingLoop
            ? [0, 2, 4, 6, 7, 10, 12, 13]
            : [0, 2, 4, 6, 7]);
        row.AddRange(definitions);
        var offset = 0;
        row.Add(0);
        foreach (var block in blocks)
        {
            offset += block.Length;
            row.Add((byte)offset);
        }
        row.AddRange(terminators);
        foreach (var block in blocks)
        {
            row.AddRange(block);
        }

        return TrRmVariableTableWriter.EncodeBodies([row.ToArray()]);
    }

    private static byte[] IndexedDivRem()
        =>
        [
            0, 0, 0, 0, 49, 0, 0, 0,
            0x08, 1, 4, 0, 2, 1, 0, 0, 0, 2, 0, 0, 0,
            2, 1, 2, 1, 2, 0, 2, 1,
            0, 4, 0xFF, 0xFF, 12, 12, 0, 19, 17,
            0x12, 2, 1, 2, 3, 1, 0, 2, 0, 4, 1, 1,
            0x18, 3, 4, 1, 2, 0x83, 4,
        ];

    internal static byte[] IndexedScalarOperation(byte[] operation, byte[] parameters,
        byte resultType)
    {
        var count = parameters.Length + 1;
        var mappedCount = parameters.Contains((byte)3) || resultType == 3 ? 3 : 2;
        var row = new List<byte> { 0x08, 1, (byte)count, 0, (byte)mappedCount };
        for (var typeId = 1; typeId <= mappedCount; typeId++)
        {
            row.Add((byte)typeId);
            row.Add(0);
            row.Add(0);
            row.Add(0);
        }
        foreach (var type in parameters)
        {
            row.Add(type);
            row.Add(1);
        }

        row.Add(resultType);
        row.Add(1);
        row.Add(0);
        row.Add((byte)count);
        for (var index = 0; index < parameters.Length; index++)
        {
            row.Add(0xFF);
        }

        var definition = row.Count;
        row.Add(0);
        row.Add(0);
        var blockLength = row.Count;
        row.Add(0);
        var terminator = row.Count;
        row.Add(0);
        var start = row.Count;
        row.Add((byte)(parameters.Length == 0 ? 0x10 : 0x12));
        if (parameters.Length != 0)
        {
            row.Add((byte)parameters.Length);
            for (var value = 1; value <= parameters.Length; value++)
            {
                row.Add((byte)value);
            }
        }

        row.Add((byte)count);
        for (var value = 1; value <= count; value++)
        {
            row.Add((byte)value);
            row.Add((byte)(value == count ? 1 : 0));
        }

        row.Add(1);
        row[definition] = (byte)(row.Count - start);
        row.AddRange(operation);
        row[terminator] = (byte)(row.Count - start);
        row.Add(0x83);
        row.Add((byte)count);
        row[blockLength] = (byte)(row.Count - start);
        var section = new byte[8 + row.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(4), (uint)row.Count);
        row.CopyTo(section, 8);
        return section;
    }

    private static byte[] WideIndexedConstant()
    {
        var original = IndexedConstantCopy();
        var section = new byte[original.Length + 9];
        original.AsSpan(0, 35).CopyTo(section);
        original.AsSpan(39).CopyTo(section.AsSpan(48));
        section[4] = 45;
        section[24] = 20;
        section[26] = 25;
        section[27] = 23;
        section[35] = 0xF0;
        section[36] = 0x30;
        section[37] = 1;
        section[38] = 1;
        section[39] = 1;
        BinaryPrimitives.WriteUInt64LittleEndian(section.AsSpan(40), ulong.MaxValue);
        return section;
    }

    private static byte[] IndexedConstantCopy()
        =>
        [
            0, 0, 0, 0, 36, 0, 0, 0,
            0x08, 1, 2, 0, 1, 2, 0, 0, 0,
            1, 1, 1, 1, 0, 2, 7, 11, 0, 16, 14,
            0x10, 2, 1, 1, 2, 2, 2,
            1, 1, 1, 42, 3, 2, 1, 0x83, 2,
        ];

    private static (int Blocks, int Values) InspectIndexed(byte[] section,
        int parameterCount = 3, byte secondTypeCode = 8, int resultTypeSpec = 2,
        byte[]? parameterTypeIds = null, byte? thirdTypeCode = null,
        byte runtimePolicy = 0, bool thirdTypeOptional = false,
        byte firstTypeCode = 1, bool secondMethod = false,
        bool importedTarget = false, bool expectedTarget = false,
        bool ownedTarget = false, bool packedTarget = false,
        bool privateOwner = false, byte callerTargetRequirement = 0,
        byte packedTargetRequirement = 0, byte packedTargetRuntimePolicy = 0,
        bool genericOwner = false)
    {
        var names = new TrRmStringTableReader(
            TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0)]), 2);
        var module = new byte[packedTarget ? 88 : 44];
        foreach (var offset in new[] { 0, 4, 28, 32, 36, 40 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(offset), 1);
        }

        if (packedTarget)
        {
            foreach (var offset in new[] { 48, 72, 76, 84 })
            {
                BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(offset), 1);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(44), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(48), packedTargetRequirement);
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(52), packedTargetRuntimePolicy);
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(80), 2);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(4), callerTargetRequirement);
        BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(8), runtimePolicy);
        var modules = new TrRmModuleTableReader(module, packedTarget ? 2 : 1,
            names, expectedTarget ? 1 : 0,
            ownedTarget ? 1 : 0,
            secondMethod ? 2 : 1, 0);
        byte[] reference = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(reference, 2);
        var references = new TrRmModuleRefTableReader(expectedTarget ? reference : [],
            expectedTarget ? 1 : 0, names, modules);
        byte[] typeDef = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(typeDef, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(typeDef.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(typeDef.AsSpan(8),
            (packedTarget || expectedTarget) && !privateOwner ? 6u : 4u);
        if (genericOwner)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(typeDef.AsSpan(20), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(typeDef.AsSpan(24), 1);
        }

        var defs = new TrRmTypeDefTableReader(ownedTarget ? typeDef : [],
            ownedTarget ? 1 : 0, names, modules, 0, genericOwner ? 1 : 0, 0, 0);
        byte[] typeBytes = (thirdTypeOptional, thirdTypeCode) switch
        {
            (true, _) => [0, 0, 0, 0, 3, 0, 0, 0, 6, 0, 0, 0, 12, 0, 0, 0,
                1, 0, firstTypeCode, 1, 0, secondTypeCode, 6, 0, 2, 0, 0, 0],
            (false, byte third) => [0, 0, 0, 0, 3, 0, 0, 0, 6, 0, 0, 0, 9, 0, 0, 0,
                1, 0, firstTypeCode, 1, 0, secondTypeCode, 1, 0, third],
            _ => [0, 0, 0, 0, 3, 0, 0, 0, 6, 0, 0, 0,
                1, 0, firstTypeCode, 1, 0, secondTypeCode],
        };
        var types = new TrRmTypeSpecTableReader(typeBytes,
            thirdTypeCode is null && !thirdTypeOptional ? 2 : 3, 0, 0, 0, 0);
        var signature = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(signature, (uint)resultTypeSpec);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(8), parameterCount == 0 ? 0u : 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(12), (uint)parameterCount);
        var signatures = new TrRmMethodSigTableReader(signature, 1, types, parameterCount, 0);
        var paramBytes = new byte[parameterCount * 16];
        for (var index = 0; index < parameterCount; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(paramBytes.AsSpan(index * 16), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(paramBytes.AsSpan(index * 16 + 4), (uint)index);
            BinaryPrimitives.WriteUInt32LittleEndian(paramBytes.AsSpan(index * 16 + 8),
                parameterTypeIds is null ? (index == 0 ? 1u : 2u) : parameterTypeIds[index]);
        }

        var parameters = parameterCount == 0 ? default
            : new TrRmParamTableReader(paramBytes, parameterCount, signatures, types);
        var method = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(24), 1);
        byte[] methodRows = secondMethod ? new byte[56] : method;
        if (secondMethod)
        {
            method.CopyTo(methodRows, 0);
            method.CopyTo(methodRows, 28);
            BinaryPrimitives.WriteUInt32LittleEndian(methodRows.AsSpan(28),
                ownedTarget ? 1u : 0u);
            if (packedTarget || expectedTarget)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(methodRows.AsSpan(40), 2);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(methodRows.AsSpan(52),
                importedTarget ? 0u : 2u);
        }
        var bodyCount = secondMethod && !importedTarget ? 2 : 1;
        byte[] bodyRows = section;
        if (bodyCount == 2)
        {
            var length = section.Length - 8;
            var second = packedTarget
                ? IndexedScalarOperation([3, 2, 1], [2], 2)
                : section;
            var secondLength = second.Length - 8;
            bodyRows = new byte[12 + length + secondLength];
            BinaryPrimitives.WriteUInt32LittleEndian(bodyRows.AsSpan(4), (uint)length);
            BinaryPrimitives.WriteUInt32LittleEndian(bodyRows.AsSpan(8),
                (uint)(length + secondLength));
            section.AsSpan(8).CopyTo(bodyRows.AsSpan(12));
            second.AsSpan(8).CopyTo(bodyRows.AsSpan(12 + length));
        }
        var methods = new TrRmMethodDefTableReader(methodRows, secondMethod ? 2 : 1,
            names, modules, defs, signatures, 0, bodyCount);
        byte[] import = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(import, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(import.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(import.AsSpan(8), 0x1600_0002);
        var imports = new TrRmImportTableReader(expectedTarget ? import : [],
            expectedTarget ? 1 : 0, names, references, defs, methods, default);
        var bodies = new TrRmBodyTableReader(bodyRows, bodyCount);
        var reader = new TrRmCompactIndexedBodyReader(bodies, 1, modules,
            methods, signatures, parameters, types, defs, imports);
        return (reader.BlockCount, reader.ValueCount);
    }

    internal static byte[] ReadExample(string heading)
    {
        var generalHex = heading switch
        {
            "GeneralCheckedAdd" => """
                00 00 00 00 61 00 00 00
                00 01 05 00 00 01 01 00 00 00
                01 00 01 01 00 01 01 00 01 01 00 01 01 00 01
                00 05
                FF FF FF FF FF FF FF FF 16 00 00 00 1B 00 00 00 1F 00 00 00
                00 00 00 00 26 00 00 00 24 00 00 00
                00 02 01 02 00 00 00 00 00 05 01 00 02 00 03 02 04 02 05 03 00 03
                15 03 01 01 02 02 04 01 03 15 05 01 03 04 83 05
                """,
            "GeneralConstantReturn" => """
                00 00 00 00 31 00 00 00
                00 01 01 00 00 01
                01 00 00 00
                01 00 01 00 01 0C 00 00 00
                00 00 00 00 12 00 00 00 10 00 00 00
                00 00 00 00 00 00 00 01 01 01 00 01 01 01 01 2A 83 01
                """,
            "GeneralConditionalMerge" => """
                00 00 00 00 9D 00 00 00
                00 04 04 00 00 02 01 00 00 00 02 00 00 00
                01 00 01 02 00 01 02 00 01 02 00 01
                00 03 03 03 04
                FF FF FF FF FF FF FF FF FF FF FF FF FF FF FF FF
                00 00 00 00 17 00 00 00 29 00 00 00
                3B 00 00 00 4A 00 00 00
                11 00 00 00 0E 00 00 00 0E 00 00 00 0D 00 00 00
                00 03 01 02 03 00 02 02 03 00 00 00 01 01 00 00 00
                81 01 02 00 03 00
                00 00 01 03 01 03 00 00 00 01 03 00 00 00
                80 04 01 03
                00 00 01 02 01 02 00 00 00 01 02 00 00 00
                80 04 01 02
                00 01 04 00 00 00 00 00 01 04 00 00 00
                83 04
                """,
            _ => null,
        };

        if (generalHex is not null)
        {
            return Convert.FromHexString(Regex.Replace(generalHex, @"\s+", ""));
        }

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var document = File.ReadAllText(Path.Combine(root, "docs", "specs",
            "il-instruction-set.md"));
        var start = document.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var next = document.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        var section = document[start..(next < 0 ? document.Length : next)];
        var match = Regex.Match(section, @"```text\r?\n(?<hex>(?:[0-9A-Fa-f ]+\r?\n)+)```");
        Assert.True(match.Success);
        return Convert.FromHexString(Regex.Replace(match.Groups["hex"].Value, @"\s+", ""));
    }

    private static uint ReadUInt32(byte[] bytes, ref int position)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, sizeof(uint)));
        position += sizeof(uint);
        return value;
    }
}
