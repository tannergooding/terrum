// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmTypeSpecTableReaderTests
{
    [Test]
    public void DecodesWorkedInt32RowWithDirectIndex()
    {
        byte[] section = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        var reader = new TrRmTypeSpecTableReader(section, 1, 0, 0, 0, 0);
        Assert.That(reader.GetRow(1).ToArray(), Is.EqualTo(new byte[] { 1, 0, 8 }));
        Assert.Throws<InvalidDataException>(() => ReadRow(section, 0));
        Assert.Throws<InvalidDataException>(() => ReadRow(section, 2));
    }

    [Test]
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
        Assert.That(reader.GetRow(6)[0], Is.EqualTo((byte)0x0C));
        Assert.That(reader.GetRow(8)[0], Is.EqualTo((byte)0x05));
    }

    [Test]
    public void ReusesCompactKeysForEquivalentNativePointerTypes()
    {
        var section = Rows(
            [0x01, 0, 0x0D],
            [0x01, 0, 0x8C, 0x0D],
            [0x01, 0, 0x8C, 0x0D],
            [0x01, 1, 0x8C, 0x0D],
            [0x01, 0, 0x8C, 0]);
        var reader = new TrRmTypeSpecTableReader(section, 5, 0, 0, 0, 0);

        Assert.That(reader.GetCompactValueTypeKey(2),
            Is.EqualTo(reader.GetCompactValueTypeKey(3)));
        Assert.That(reader.GetCompactValueTypeKey(2),
            Is.Not.EqualTo(reader.GetCompactValueTypeKey(4)));
        Assert.That(reader.GetCompactValueTypeKey(5), Is.EqualTo(0x8C));
    }

    [Test]
    public void RejectsNativePointerTypeKeyExhaustionAfterEquivalentPointers()
    {
        var signatureCount = ushort.MaxValue - TrRmTypeSpecTableReader.NativeFunctionPointerTypeKeyStart;
        var rows = new ReadOnlyMemory<byte>[signatureCount + 5];
        rows[0] = TrRmTypeSpecTableWriter.Builtin(8);
        rows[1] = TrRmTypeSpecTableWriter.Builtin(0x0D);
        for (var index = 0; index < signatureCount; index++)
        {
            rows[index + 2] = TrRmTypeSpecTableWriter.Reference(0x0D, (uint)(index + 1));
        }
        var firstPointerRow = signatureCount + 3;
        rows[firstPointerRow - 1] = TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0x0D);
        rows[firstPointerRow] = TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0x0D);
        rows[firstPointerRow + 1] = TrRmTypeSpecTableWriter.InlineFamily(0x8C, 8);

        var section = TrRmTypeSpecTableWriter.Encode(rows, 0, 0, 0, 0, signatureCount);
        var reader = new TrRmTypeSpecTableReader(section, rows.Length, 0, 0, 0, 0,
            signatureCount);

        Assert.That(reader.GetCompactValueTypeKey(firstPointerRow), Is.EqualTo(ushort.MaxValue));
        Assert.That(reader.GetCompactValueTypeKey(firstPointerRow + 1), Is.EqualTo(ushort.MaxValue));
        Assert.Throws<InvalidDataException>(() => GetCompactValueTypeKey(section,
            rows.Length, signatureCount, firstPointerRow + 2));
    }

    [TestCase(new byte[] { 0x00, 0, 8 })]
    [TestCase(new byte[] { 0x01, 2, 8 })]
    [TestCase(new byte[] { 0x01, 0, 0x1A })]
    [TestCase(new byte[] { 0x01, 0, 0xFF })]
    [TestCase(new byte[] { 0x01, 0, 0x80 })]
    [TestCase(new byte[] { 0x01, 0, 0x80, 0x80 })]
    [TestCase(new byte[] { 0x01, 0, 8, 0 })]
    [TestCase(new byte[] { 0x02, 0, 0, 0, 0, 0 })]
    [TestCase(new byte[] { 0x02, 0, 2, 0, 0, 0 })]
    [TestCase(new byte[] { 0x03, 0, 1, 0, 0, 0 })]
    [TestCase(new byte[] { 0x04, 0, 1, 0, 0, 0 })]
    [TestCase(new byte[] { 0x06, 0, 1, 0, 0, 0 })]
    [TestCase(new byte[] { 0x0C, 0, 0x8B, 1, 0, 0, 0 })]
    public void RejectsInvalidRowShapesAndReferences(byte[] row)
    {
        Assert.Throws<InvalidDataException>(() => Create(Rows(row)));
    }

    [Test]
    public void RejectsNoncanonicalIndexedAtomicArguments()
    {
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([1, 0, 8], [7, 0, 1, 0, 0, 0]), count: 2));
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([1, 0, 8], [0x0C, 0, 0x8A, 1, 0, 0, 0]), count: 2));
    }
    [TestCase(new byte[] { 5, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 })]
    [TestCase(new byte[] { 5, 0, 1, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0 })]
    public void RejectsInvalidConstructedArgumentRanges(byte[] constructed)
    {
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([2, 0, 1, 0, 0, 0], constructed), count: 2, typeArgCount: 1));
    }

    [Test]
    public void ConstructedFormRequiresNominalGenericBase()
    {
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([1, 0, 8], [5, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0]),
                count: 2, typeArgCount: 1));
    }

    [Test]
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

    private static ushort GetCompactValueTypeKey(byte[] bytes, int count,
        int nativeFunctionPointerSignatureCount, int row)
    {
        var reader = new TrRmTypeSpecTableReader(bytes, count, 0, 0, 0, 0,
            nativeFunctionPointerSignatureCount);
        return reader.GetCompactValueTypeKey(row);
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
