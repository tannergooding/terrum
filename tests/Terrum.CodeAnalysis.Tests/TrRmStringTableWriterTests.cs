// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmStringTableWriterTests
{
    [Fact]
    public void EmitsIndexedWorkedExampleExactly()
    {
        Assert.Equal(new byte[]
        {
            0, 0, 0, 0, 12, 0, 0, 0, 24, 0, 0, 0,
            0, 6, 0, 0, 65, 110, 115, 119, 101, 114, 0, 0,
            0, 7, 0, 0, 69, 120, 97, 109, 112, 108, 101, 0,
        }, TrRmStringTableWriter.EncodeNames([("Answer", 0), ("Example", 0)]));
    }

    [Fact]
    public void AllEncodingsRoundTripWithoutChangingNameIdentity()
    {
        const string Name = "\u00E9\uD83D\uDE00";
        var section = TrRmStringTableWriter.EncodeNames(
            [(Name, 1), (Name, 2), (Name, 3), ("A", 0)]);
        var reader = new TrRmStringTableReader(section, 4);
        Assert.Equal(Name, reader.DecodeName(1));
        Assert.Equal(Name, reader.DecodeName(2));
        Assert.Equal(Name, reader.DecodeName(3));
        Assert.Equal("A", reader.DecodeName(4));
    }

    [Fact]
    public void EmptyNamespaceRoundTripsInAllEncodingsWithoutBecomingAValidName()
    {
        var section = TrRmStringTableWriter.EncodeTextRows(
            [("", 0), ("", 1), ("", 2), ("", 3), ("Widget", 0)]);
        var reader = new TrRmStringTableReader(section, 5);
        for (var row = 1; row <= 4; row++)
        {
            Assert.Equal("", reader.DecodeNamespace(row));
            Assert.Throws<InvalidDataException>(() => DecodeName(section, row));
        }

        Assert.Equal("Widget", reader.DecodeName(5));
        Assert.Throws<ArgumentException>(() =>
            TrRmStringTableWriter.EncodeTextRows([("X\0Y", 1)]));
    }

    private static string DecodeName(byte[] section, int row)
        => new TrRmStringTableReader(section, 5).DecodeName(row);

    [Theory]
    [InlineData("", 0)]
    [InlineData("A\0B", 0)]
    [InlineData("\u00E9", 0)]
    public void RejectsInvalidOrUnencodableNames(string text, int encoding)
    {
        Assert.Throws<ArgumentException>(() => TrRmStringTableWriter.EncodeNames([(text, encoding)]));
    }

    [Fact]
    public void RejectsUnpairedSurrogatesWithoutReplacement()
    {
        var high = new string('\uD800', 1);
        var low = new string('\uDC00', 1);
        Assert.Throws<ArgumentException>(() => TrRmStringTableWriter.EncodeNames([(high, 1)]));
        Assert.Throws<ArgumentException>(() => TrRmStringTableWriter.EncodeNames([(low, 2)]));
        Assert.Throws<ArgumentException>(() => TrRmStringTableWriter.EncodeNames([(high, 3)]));
    }

    [Fact]
    public void RejectsAbsentAndUnassignedTableFormats()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrRmStringTableWriter.EncodeNames([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrRmStringTableWriter.EncodeNames([("A", 4)]));
    }

    [Fact]
    public void UnitLimitIsPerEncodingNotPerScalar()
    {
        var text = new string('\u00E9', 8_388_608);
        Assert.Throws<ArgumentOutOfRangeException>(() => TrRmStringTableWriter.EncodeNames([(text, 1)]));
        var section = TrRmStringTableWriter.EncodeNames([(text, 2)]);
        Assert.Equal((byte)2, section[8]);
        Assert.Equal((byte)0, section[9]);
        Assert.Equal((byte)0, section[10]);
        Assert.Equal((byte)0x80, section[11]);
        Assert.Equal(16_777_232, section.Length);
        var reader = new TrRmStringTableReader(section, 1);
        Assert.Equal(text, reader.DecodeName(1));
    }
}
