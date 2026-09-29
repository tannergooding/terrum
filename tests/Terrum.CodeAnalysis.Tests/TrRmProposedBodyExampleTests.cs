// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmProposedBodyExampleTests
{
    [Theory]
    [InlineData("GeneralCheckedAdd", 105, 5, false, 38, 36, 3)]
    [InlineData("## Value and place state through checked arithmetic", 122, 5, true, 51, 49, 7)]
    [InlineData("GeneralConstantReturn", 57, 1, false, 18, 16, 1)]
    public void ProposedBodyExamplesHaveDirectDefinitionAndUseIndexes(
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
    public void ProposedConditionalMergeHasIndexedDefsUsesAndNormalEdges()
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
    public void ProposedCompactLinearDerivesDefinitionsAndUses(
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
    public void ProposedCompactIndexedMergeRetainsDirectFlowFacts()
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
    public void ProposedCompactIndexedConditionalAddPreservesCheckedArm()
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

    private static byte[] ReadExample(string heading)
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
        var document = File.ReadAllText(Path.Combine(root, "docs", "proposals",
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
