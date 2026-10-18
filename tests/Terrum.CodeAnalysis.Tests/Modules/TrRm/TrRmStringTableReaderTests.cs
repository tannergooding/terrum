// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmStringTableReaderTests
{
    [Test]
    public void IndexesNamesByOneBasedRowWithoutScanningPreviousRows()
    {
        var table = new TrRmStringTableReader(TwoRowSection(), 2);
        Assert.That(table.DecodeName(2), Is.EqualTo("Example"));
        Assert.That(table.DecodeName(1), Is.EqualTo("Answer"));
        Assert.Throws<InvalidDataException>(() => DecodeName(TwoRowSection(), 2, 0));
        Assert.Throws<InvalidDataException>(() => DecodeName(TwoRowSection(), 2, 3));
    }
    [TestCase(0,(byte)(1))]
    [TestCase(4,(byte)(0))]
    [TestCase(4,(byte)(11))]
    [TestCase(4,(byte)(20))]
    [TestCase(8,(byte)(20))]
    [TestCase(8,(byte)(28))]
    public void RejectsMalformedOffsets(int byteIndex, byte replacement)
    {
        var bytes = TwoRowSection();
        bytes[byteIndex] = replacement;
        Assert.Throws<InvalidDataException>(() => new TrRmStringTableReader(bytes, 2));
    }
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(0x1_000000)]
    public void RejectsInvalidRecordCounts(int recordCount)
    {
        Assert.Throws<InvalidDataException>(() => new TrRmStringTableReader(TwoRowSection(), recordCount));
    }

    [Test]
    public void ValidatesSelectedRowAfterTheIndex()
    {
        var bytes = TwoRowSection();
        bytes[24] = 4;
        var table = new TrRmStringTableReader(bytes, 2);
        Assert.That(table.DecodeName(1), Is.EqualTo("Answer"));
        Assert.Throws<InvalidDataException>(() => DecodeName(bytes, 2, 2));
    }

    [Test]
    public void RejectsAnIndexDamagedAfterConstruction()
    {
        var bytes = TwoRowSection();
        Assert.Throws<InvalidDataException>(() => DecodeNameAfterMutation(bytes));
    }

    private static string DecodeNameAfterMutation(byte[] section)
    {
        var table = new TrRmStringTableReader(section, 2);
        section[8] = 255;
        return table.DecodeName(2);
    }

    private static string DecodeName(byte[] section, int recordCount, int row)
    {
        var table = new TrRmStringTableReader(section, recordCount);
        return table.DecodeName(row);
    }

    private static byte[] TwoRowSection() =>
    [
        0, 0, 0, 0, 12, 0, 0, 0, 24, 0, 0, 0,
        0, 6, 0, 0, 65, 110, 115, 119, 101, 114, 0, 0,
        0, 7, 0, 0, 69, 120, 97, 109, 112, 108, 101, 0,
    ];
}
