// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmStringTableWriterTests
{
    [Test]
    public void EmitsIndexedWorkedExampleExactly()
    {
        Assert.That(TrRmStringTableWriter.EncodeNames([("Answer", 0), ("Example", 0)]), Is.EqualTo(new byte[]
        {
            0, 0, 0, 0, 12, 0, 0, 0, 24, 0, 0, 0,
            0, 6, 0, 0, 65, 110, 115, 119, 101, 114, 0, 0,
            0, 7, 0, 0, 69, 120, 97, 109, 112, 108, 101, 0,
        }));
    }

    [Test]
    public void AllEncodingsRoundTripWithoutChangingNameIdentity()
    {
        const string Name = "\u00E9\uD83D\uDE00";
        var section = TrRmStringTableWriter.EncodeNames(
            [(Name, 1), (Name, 2), (Name, 3), ("A", 0)]);
        var reader = new TrRmStringTableReader(section, 4);
        Assert.That(reader.DecodeName(1), Is.EqualTo(Name));
        Assert.That(reader.DecodeName(2), Is.EqualTo(Name));
        Assert.That(reader.DecodeName(3), Is.EqualTo(Name));
        Assert.That(reader.DecodeName(4), Is.EqualTo("A"));
    }
    [TestCase(255,"FF0000")]
    [TestCase(256,"000100")]
    [TestCase(65535,"FFFF00")]
    [TestCase(65536,"000001")]
    [TestCase(0x12345,"452301")]
    public void FullU24UnitCountsPreserveEachLittleEndianOctet(int units, string hex)
    {
        foreach (var encoding in new[] { 0, 1, 2, 3 })
        {
            var text = new string('A', units);
            var section = TrRmStringTableWriter.EncodeNames([(text, encoding)]);
            Assert.That(section.AsSpan(9, 3).ToArray(), Is.EqualTo(Convert.FromHexString(hex)));
            Assert.That(new TrRmStringTableReader(section, 1).DecodeName(1), Is.EqualTo(text));
        }
    }

    [Test]
    public void EmptyNamespaceRoundTripsInAllEncodingsWithoutBecomingAValidName()
    {
        var section = TrRmStringTableWriter.EncodeTextRows(
            [("", 0), ("", 1), ("", 2), ("", 3), ("Widget", 0)]);
        var reader = new TrRmStringTableReader(section, 5);
        for (var row = 1; row <= 4; row++)
        {
            Assert.That(reader.DecodeNamespace(row), Is.EqualTo(""));
            Assert.Throws<InvalidDataException>(() => DecodeName(section, row));
        }

        Assert.That(reader.DecodeName(5), Is.EqualTo("Widget"));
        Assert.Throws<ArgumentException>(() =>
            TrRmStringTableWriter.EncodeTextRows([("X\0Y", 1)]));
    }

    [Test]
    public void AsciiNulRejectionPrecedesNonAsciiRejection()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            TrRmStringTableWriter.EncodeNames([("A\u00E9\0B", 0)]))!;
        Assert.That(exception.ParamName, Is.EqualTo("names"));
    }

    private static string DecodeName(byte[] section, int row)
        => new TrRmStringTableReader(section, 5).DecodeName(row);
    [TestCase("",0)]
    [TestCase("A\0B",0)]
    [TestCase("\u00E9",0)]
    public void RejectsInvalidOrUnencodableNames(string text, int encoding)
    {
        Assert.Throws<ArgumentException>(() => TrRmStringTableWriter.EncodeNames([(text, encoding)]));
    }

    [Test]
    public void RejectsUnpairedSurrogatesWithoutReplacement()
    {
        var high = new string('\uD800', 1);
        var low = new string('\uDC00', 1);
        Assert.Throws<ArgumentException>(() => TrRmStringTableWriter.EncodeNames([(high, 1)]));
        Assert.Throws<ArgumentException>(() => TrRmStringTableWriter.EncodeNames([(low, 2)]));
        Assert.Throws<ArgumentException>(() => TrRmStringTableWriter.EncodeNames([(high, 3)]));
    }

    [Test]
    public void RejectsAbsentAndUnassignedTableFormats()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrRmStringTableWriter.EncodeNames([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrRmStringTableWriter.EncodeNames([("A", 4)]));
    }

    [Test]
    public void UnitLimitIsPerEncodingNotPerScalar()
    {
        var text = new string('\u00E9', 8_388_608);
        Assert.Throws<ArgumentOutOfRangeException>(() => TrRmStringTableWriter.EncodeNames([(text, 1)]));
        var section = TrRmStringTableWriter.EncodeNames([(text, 2)]);
        Assert.That(section[8], Is.EqualTo((byte)2));
        Assert.That(section[9], Is.EqualTo((byte)0));
        Assert.That(section[10], Is.EqualTo((byte)0));
        Assert.That(section[11], Is.EqualTo((byte)0x80));
        Assert.That(section.Length, Is.EqualTo(16_777_232));
        var reader = new TrRmStringTableReader(section, 1);
        Assert.That(reader.DecodeName(1), Is.EqualTo(text));
    }
}
