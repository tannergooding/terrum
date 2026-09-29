// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmVariableTableWriterTests
{
    [Fact]
    public void EncodesEmptyAndNonemptyBlobRowsWithIndependentStorage()
    {
        byte[] source = [1, 2, 3];
        var bytes = TrRmVariableTableWriter.EncodeBlobs([ReadOnlyMemory<byte>.Empty, source]);
        source[0] = 0;

        Assert.Equal(new uint[] { 0, 0, 3 }, Offsets(bytes, 2));
        var reader = new TrRmBlobTableReader(bytes, 2);
        Assert.Equal(2, reader.Count);
        Assert.True(reader.GetRow(1).IsEmpty);
        Assert.Equal(new byte[] { 1, 2, 3 }, reader.GetRow(2).ToArray());
    }

    [Fact]
    public void BlobContentsRemainOpaqueAndRowsAreIndependentlyIndexed()
    {
        var first = new byte[] { 0x16, 0, 0, 1 };
        var last = new byte[] { 0xFF, 0, 0x16, 0 };
        var section = TrRmVariableTableWriter.EncodeBlobs([first, ReadOnlyMemory<byte>.Empty, last]);
        var reader = new TrRmBlobTableReader(section, 3);
        Assert.Equal(3, reader.Count);

        Assert.Equal(first, reader.GetRow(1).ToArray());
        Assert.True(reader.GetRow(2).IsEmpty);
        Assert.Equal(last, reader.GetRow(3).ToArray());
        Assert.Throws<InvalidDataException>(() => ReadBlob(section, 0, 3));
        Assert.Throws<InvalidDataException>(() => ReadBlob(section, 4, 3));
    }

    [Fact]
    public void RejectsMalformedBlobIndexes()
    {
        byte[] section = TrRmVariableTableWriter.EncodeBlobs([
            ReadOnlyMemory<byte>.Empty, new byte[] { 1, 2, 3 },
        ]);
        var changedStart = (byte[])section.Clone();
        changedStart[0] = 1;
        Assert.Throws<InvalidDataException>(() => ReadBlob(changedStart, 1, 2));

        var changedEnd = (byte[])section.Clone();
        changedEnd[8] = 2;
        Assert.Throws<InvalidDataException>(() => ReadBlob(changedEnd, 1, 2));

        var descending = (byte[])section.Clone();
        descending[4] = 2;
        descending[8] = 1;
        Assert.Throws<InvalidDataException>(() => ReadBlob(descending, 1, 2));
    }

    private static byte[] ReadBlob(byte[] section, int row, int count)
    {
        var reader = new TrRmBlobTableReader(section, count);
        return reader.GetRow(row).ToArray();
    }

    [Fact]
    public void EncodesNonemptyBodyRowsWithoutInterpretingInstructions()
    {
        var bytes = TrRmVariableTableWriter.EncodeBodies([
            new byte[] { 0xFF }, new byte[] { 1, 2 },
        ]);

        Assert.Equal(new uint[] { 0, 1, 3 }, Offsets(bytes, 2));
        var reader = new TrRmBodyTableReader(bytes, 2);
        Assert.Equal(2, reader.Count);
        Assert.Equal(new byte[] { 0xFF }, reader.GetRow(1).ToArray());
        Assert.Equal(new byte[] { 1, 2 }, reader.GetRow(2).ToArray());
        Assert.Throws<InvalidDataException>(() => ReadBody(bytes, 0, 2));
        Assert.Throws<InvalidDataException>(() => ReadBody(bytes, 3, 2));
    }

    [Fact]
    public void RejectsEmptyOrMalformedBodyRows()
    {
        byte[] section = TrRmVariableTableWriter.EncodeBodies([new byte[] { 1 },
            new byte[] { 2, 3 }]);

        var emptyRow = (byte[])section.Clone();
        emptyRow[4] = 0;
        Assert.Throws<InvalidDataException>(() => ReadBody(emptyRow, 1, 2));

        var pastEnd = (byte[])section.Clone();
        pastEnd[8] = 4;
        Assert.Throws<InvalidDataException>(() => ReadBody(pastEnd, 2, 2));
    }

    private static byte[] ReadBody(byte[] section, int row, int count)
    {
        var reader = new TrRmBodyTableReader(section, count);
        return reader.GetRow(row).ToArray();
    }

    [Fact]
    public void RejectsAbsentTablesAndEmptyBodies()
    {
        Assert.Throws<ArgumentNullException>(() => TrRmVariableTableWriter.EncodeBlobs(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrRmVariableTableWriter.EncodeBlobs([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrRmVariableTableWriter.EncodeBodies([]));
        Assert.Throws<ArgumentException>(() =>
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
