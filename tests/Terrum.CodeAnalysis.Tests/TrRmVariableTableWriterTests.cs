// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
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
        var index = new TrRmVariableTableIndex(bytes, 2, 0, 1);
        Assert.True(index.GetRow(1).IsEmpty);
        Assert.Equal(new byte[] { 1, 2, 3 }, index.GetRow(2).ToArray());
    }

    [Fact]
    public void EncodesNonemptyBodyRowsWithoutInterpretingInstructions()
    {
        var bytes = TrRmVariableTableWriter.EncodeBodies([
            new byte[] { 0xFF }, new byte[] { 1, 2 },
        ]);

        Assert.Equal(new uint[] { 0, 1, 3 }, Offsets(bytes, 2));
        var index = new TrRmVariableTableIndex(bytes, 2, 1, 1);
        Assert.Equal(new byte[] { 0xFF }, index.GetRow(1).ToArray());
        Assert.Equal(new byte[] { 1, 2 }, index.GetRow(2).ToArray());
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
