// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmVariableTableWriterTests
{
    [Test]
    public void EncodesEmptyAndNonemptyBlobRowsWithIndependentStorage()
    {
        byte[] source = [1, 2, 3];
        var bytes = TrRmVariableTableWriter.EncodeBlobs([ReadOnlyMemory<byte>.Empty, source]);
        source[0] = 0;

        Assert.That(Offsets(bytes, 2), Is.EqualTo(new uint[] { 0, 0, 3 }));
        var reader = new TrRmBlobTableReader(bytes, 2);
        Assert.That(reader.Count, Is.EqualTo(2));
        Assert.That(reader.GetRow(1).IsEmpty, Is.True);
        Assert.That(reader.GetRow(2).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public void BlobContentsRemainOpaqueAndRowsAreIndependentlyIndexed()
    {
        var first = new byte[] { 0x16, 0, 0, 1 };
        var last = new byte[] { 0xFF, 0, 0x16, 0 };
        var section = TrRmVariableTableWriter.EncodeBlobs([first, ReadOnlyMemory<byte>.Empty, last]);
        var reader = new TrRmBlobTableReader(section, 3);
        Assert.That(reader.Count, Is.EqualTo(3));

        Assert.That(reader.GetRow(1).ToArray(), Is.EqualTo(first));
        Assert.That(reader.GetRow(2).IsEmpty, Is.True);
        Assert.That(reader.GetRow(3).ToArray(), Is.EqualTo(last));
        _ = Assert.Throws<InvalidDataException>(() => ReadBlob(section, 0, 3));
        _ = Assert.Throws<InvalidDataException>(() => ReadBlob(section, 4, 3));
    }

    [Test]
    public void BlobReaderChargesOffsetsAndPayloadAtTheExactWorkBoundary()
    {
        var section = TrRmVariableTableWriter.EncodeBlobs([
            new byte[] { 1, 2, 3, 4 },
        ]);
        var exactLedger = new TrRmA3LayoutResourceLedger();
        exactLedger.ConsumeWork(
            TrRmA3LayoutResourceLedger.WorkLimit - 28);
        var exactReader = new TrRmBlobTableReader(section, 1, exactLedger);

        Assert.That(exactReader.GetRow(1).ToArray(),
            Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        Assert.That(exactLedger.WorkRemaining, Is.Zero);

        var shortLedger = new TrRmA3LayoutResourceLedger();
        shortLedger.ConsumeWork(
            TrRmA3LayoutResourceLedger.WorkLimit - 27);
        var shortReader = new TrRmBlobTableReader(section, 1, shortLedger);
        TrRmResourceLimitException? exception = null;
        try
        {
            _ = shortReader.GetRow(1);
        }
        catch (TrRmResourceLimitException caught)
        {
            exception = caught;
        }

        Assert.That(exception!.Required,
            Is.EqualTo(new System.Numerics.BigInteger(
                TrRmA3LayoutResourceLedger.WorkLimit + 1)));
    }

    [Test]
    public void RejectsMalformedBlobIndexes()
    {
        var section = TrRmVariableTableWriter.EncodeBlobs([
            ReadOnlyMemory<byte>.Empty, new byte[] { 1, 2, 3 },
        ]);
        var changedStart = (byte[])section.Clone();
        changedStart[0] = 1;
        _ = Assert.Throws<InvalidDataException>(() => ReadBlob(changedStart, 1, 2));

        var changedEnd = (byte[])section.Clone();
        changedEnd[8] = 2;
        _ = Assert.Throws<InvalidDataException>(() => ReadBlob(changedEnd, 1, 2));

        var descending = (byte[])section.Clone();
        descending[4] = 2;
        descending[8] = 1;
        _ = Assert.Throws<InvalidDataException>(() => ReadBlob(descending, 1, 2));
    }

    private static byte[] ReadBlob(byte[] section, int row, int count)
    {
        var reader = new TrRmBlobTableReader(section, count);
        return reader.GetRow(row).ToArray();
    }

    [Test]
    public void EncodesNonemptyBodyRowsWithoutInterpretingInstructions()
    {
        var bytes = TrRmVariableTableWriter.EncodeBodies([
            new byte[] { 0xFF }, new byte[] { 1, 2 },
        ]);

        Assert.That(Offsets(bytes, 2), Is.EqualTo(new uint[] { 0, 1, 3 }));
        var reader = new TrRmBodyTableReader(bytes, 2);
        Assert.That(reader.Count, Is.EqualTo(2));
        Assert.That(reader.GetRow(1).ToArray(), Is.EqualTo(new byte[] { 0xFF }));
        Assert.That(reader.GetRow(2).ToArray(), Is.EqualTo(new byte[] { 1, 2 }));
        _ = Assert.Throws<InvalidDataException>(() => ReadBody(bytes, 0, 2));
        _ = Assert.Throws<InvalidDataException>(() => ReadBody(bytes, 3, 2));
    }

    [Test]
    public void RejectsEmptyOrMalformedBodyRows()
    {
        var section = TrRmVariableTableWriter.EncodeBodies([new byte[] { 1 },
            new byte[] { 2, 3 }]);

        var emptyRow = (byte[])section.Clone();
        emptyRow[4] = 0;
        _ = Assert.Throws<InvalidDataException>(() => ReadBody(emptyRow, 1, 2));

        var pastEnd = (byte[])section.Clone();
        pastEnd[8] = 4;
        _ = Assert.Throws<InvalidDataException>(() => ReadBody(pastEnd, 2, 2));
    }

    private static byte[] ReadBody(byte[] section, int row, int count)
    {
        var reader = new TrRmBodyTableReader(section, count);
        return reader.GetRow(row).ToArray();
    }

    [Test]
    public void RejectsAbsentTablesAndEmptyBodies()
    {
        _ = Assert.Throws<ArgumentNullException>(() => TrRmVariableTableWriter.EncodeBlobs(null!));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => TrRmVariableTableWriter.EncodeBlobs([]));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => TrRmVariableTableWriter.EncodeBodies([]));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmVariableTableWriter.EncodeBodies([new byte[] { 1 }, ReadOnlyMemory<byte>.Empty]));
    }

    private static uint[] Offsets(byte[] bytes, int count)
    {
        var offsets = new uint[count + 1];
        for (var index = 0; index < offsets.Length; index++)
        {
            offsets[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint)));
        }

        return offsets;
    }
}
