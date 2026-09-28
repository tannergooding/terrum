// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmTypeSpecTableReaderTests
{
    [Fact]
    public void DecodesWorkedInt32RowWithDirectIndex()
    {
        byte[] section = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        var reader = new TrRmTypeSpecTableReader(section, 1, 0, 0, 0, 0);
        Assert.Equal(new byte[] { 1, 0, 8 }, reader.GetRow(1).ToArray());
        Assert.Throws<InvalidDataException>(() => ReadRow(section, 0));
        Assert.Throws<InvalidDataException>(() => ReadRow(section, 2));
    }

    [Fact]
    public void ValidatesInlineAndIndexedFamiliesWithoutInventingExecutableSupport()
    {
        var section = Rows(
            [0x01, 0, 0x08],
            [0x01, 0, 0x81, 0x08],
            [0x01, 0, 0x89, 0x08],
            [0x02, 0, 1, 0, 0, 0],
            [0x07, 0, 4, 0, 0, 0],
            [0x0C, 0, 0x8A, 4, 0, 0, 0],
            [0x06, 0, 1, 0, 0, 0],
            [0x05, 0, 4, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0]);
        var reader = new TrRmTypeSpecTableReader(section, 8, 1, 0, 0, 1);
        Assert.Equal((byte)0x0C, reader.GetRow(6)[0]);
        Assert.Equal((byte)0x05, reader.GetRow(8)[0]);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0, 8 })]
    [InlineData(new byte[] { 0x01, 2, 8 })]
    [InlineData(new byte[] { 0x01, 0, 0x1A })]
    [InlineData(new byte[] { 0x01, 0, 0xFF })]
    [InlineData(new byte[] { 0x01, 0, 0x80 })]
    [InlineData(new byte[] { 0x01, 0, 0x80, 0x80 })]
    [InlineData(new byte[] { 0x01, 0, 8, 0 })]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x02, 0, 2, 0, 0, 0 })]
    [InlineData(new byte[] { 0x03, 0, 1, 0, 0, 0 })]
    [InlineData(new byte[] { 0x04, 0, 1, 0, 0, 0 })]
    [InlineData(new byte[] { 0x06, 0, 1, 0, 0, 0 })]
    [InlineData(new byte[] { 0x0C, 0, 0x8B, 1, 0, 0, 0 })]
    public void RejectsInvalidRowShapesAndReferences(byte[] row)
    {
        Assert.Throws<InvalidDataException>(() => Create(Rows(row)));
    }

    [Fact]
    public void RejectsNoncanonicalIndexedAtomicArguments()
    {
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([1, 0, 8], [7, 0, 1, 0, 0, 0]), count: 2));
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([1, 0, 8], [0x0C, 0, 0x8A, 1, 0, 0, 0]), count: 2));
    }

    [Theory]
    [InlineData(new byte[] { 5, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 })]
    [InlineData(new byte[] { 5, 0, 1, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0 })]
    public void RejectsInvalidConstructedArgumentRanges(byte[] constructed)
    {
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([2, 0, 1, 0, 0, 0], constructed), count: 2, typeArgCount: 1));
    }

    [Fact]
    public void ConstructedFormRequiresNominalGenericBase()
    {
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([1, 0, 8], [5, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0]),
                count: 2, typeArgCount: 1));
    }

    [Fact]
    public void RejectsWrongIndexAndReferencedTableCounts()
    {
        Assert.Throws<InvalidDataException>(() => Create([0, 0, 0, 0, 4, 0, 0, 0, 1, 0, 8]));
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([2, 0, 1, 0, 0, 0]), typeDefCount: 0));
    }

    private static void Create(byte[] bytes, int count = 1, int typeDefCount = 1, int typeArgCount = 0)
    {
        _ = new TrRmTypeSpecTableReader(bytes, count, typeDefCount, 0, 0, typeArgCount);
    }

    private static byte[] ReadRow(byte[] bytes, int row)
    {
        var reader = new TrRmTypeSpecTableReader(bytes, 1, 0, 0, 0, 0);
        return reader.GetRow(row).ToArray();
    }

    private static byte[] Rows(params byte[][] rows)
    {
        var dataLength = 0;
        foreach (var row in rows)
        {
            dataLength += row.Length;
        }

        var indexLength = (rows.Length + 1) * sizeof(uint);
        var bytes = new byte[indexLength + dataLength];
        var offset = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint)), (uint)offset);
            rows[index].CopyTo(bytes.AsSpan(indexLength + offset));
            offset += rows[index].Length;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(rows.Length * sizeof(uint)), (uint)offset);
        return bytes;
    }
}
