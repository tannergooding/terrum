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
    [InlineData("### Checked add end to end", 109, 5, false, 38, 36, 3)]
    [InlineData("## Value and place state through checked arithmetic", 126, 5, true, 51, 49, 7)]
    [InlineData("## Worked bytes and illustrative size", 61, 1, false, 18, 16, 1)]
    public void ProposedBodyExamplesHaveDirectDefinitionAndUseIndexes(
        string heading, int sectionLength, int valueCount, bool hasPlace,
        int blockLength, int terminatorOffset, int operationCount)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var document = File.ReadAllText(Path.Combine(root, "docs", "proposals",
            "terrum-il-instruction-set.md"));
        var start = document.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var next = document.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        var section = document[start..(next < 0 ? document.Length : next)];
        var match = Regex.Match(section, @"```text\r?\n(?<hex>(?:[0-9A-Fa-f ]+\r?\n)+)```");
        Assert.True(match.Success);
        var bytes = Convert.FromHexString(Regex.Replace(match.Groups["hex"].Value, @"\s+", ""));

        Assert.Equal(sectionLength, bytes.Length);
        var position = 0;
        Assert.Equal(0u, ReadUInt32(bytes, ref position));
        Assert.Equal((uint)(sectionLength - 8), ReadUInt32(bytes, ref position));
        Assert.Equal(1u, ReadUInt32(bytes, ref position));
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

    private static uint ReadUInt32(byte[] bytes, ref int position)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, sizeof(uint)));
        position += sizeof(uint);
        return value;
    }
}
