// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmTypeArgTableReaderTests
{
    [Test]
    public void ValidatesContiguousConstructedArguments()
    {
        Create(OneArgument(), 1);
    }
    [TestCase(0,(byte)(0))]
    [TestCase(0,(byte)(1))]
    [TestCase(0,(byte)(3))]
    [TestCase(4,(byte)(1))]
    [TestCase(8,(byte)(0))]
    [TestCase(8,(byte)(2))]
    public void RejectsWrongOwnerOrdinalAndTypeReferences(int offset, byte replacement)
    {
        var argument = OneArgument();
        argument[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => Create(argument, 1));
    }

    [Test]
    public void RejectsCountMismatchesAndTruncatedRows()
    {
        Assert.Throws<InvalidDataException>(() => Create(OneArgument()[..^1], 1));
        Assert.Throws<InvalidDataException>(() => Create(OneArgument(), 2));
    }

    [Test]
    public void RejectsOverlappingConstructedRanges()
    {
        var specs = IndexedTypes(
            [2, 0, 1, 0, 0, 0],
            [5, 0, 1, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0],
            [5, 0, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0]);
        var arguments = new byte[24];
        Write(arguments, 0, 2);
        Write(arguments, 8, 1);
        Write(arguments, 12, 3);
        Write(arguments, 20, 1);
        Assert.Throws<InvalidDataException>(() =>
            Create(arguments, 2, specs, typeSpecCount: 3));
    }

    private static void Create(byte[] arguments, int argumentCount,
        byte[]? specBytes = null, int typeSpecCount = 2)
    {
        var types = new TrRmTypeSpecTableReader(specBytes ?? IndexedTypes(
            [2, 0, 1, 0, 0, 0],
            [5, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0]),
            typeSpecCount, 1, 0, 0, argumentCount);
        _ = new TrRmTypeArgTableReader(arguments, argumentCount, types);
    }

    private static byte[] OneArgument()
    {
        var bytes = new byte[12];
        Write(bytes, 0, 2);
        Write(bytes, 8, 1);
        return bytes;
    }

    private static byte[] IndexedTypes(params byte[][] rows)
    {
        var total = 0;
        foreach (var row in rows)
        {
            total += row.Length;
        }

        var indexBytes = (rows.Length + 1) * sizeof(uint);
        var result = new byte[indexBytes + total];
        var offset = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            Write(result, index * sizeof(uint), (uint)offset);
            rows[index].AsSpan().CopyTo(result.AsSpan(indexBytes + offset));
            offset += rows[index].Length;
        }

        Write(result, rows.Length * sizeof(uint), (uint)offset);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
