// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmTypeSpecTableWriterTests
{
    [Fact]
    public void EncodesDependentRowsAndOwnsTheirInput()
    {
        byte[] primitive = [1, 0, 8];
        var bytes = TrRmTypeSpecTableWriter.Encode([
            primitive,
            new byte[] { 2, 0, 1, 0, 0, 0 },
            new byte[] { 5, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 },
        ], 1, 0, 0, 1);
        primitive[2] = 9;

        var reader = new TrRmTypeSpecTableReader(bytes, 3, 1, 0, 0, 1);
        Assert.Equal(new byte[] { 1, 0, 8 }, reader.GetRow(1).ToArray());
        Assert.Equal((byte)5, reader.GetRow(3)[0]);
        Assert.Equal(39, bytes.Length);
    }

    [Fact]
    public void RejectsMalformedRowsAndMissingReferencedTables()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TrRmTypeSpecTableWriter.Encode(null!, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmTypeSpecTableWriter.Encode([], 0, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() =>
            TrRmTypeSpecTableWriter.Encode([ReadOnlyMemory<byte>.Empty], 0, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() =>
            TrRmTypeSpecTableWriter.Encode([new byte[] { 1, 0, 0x1A }], 0, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() =>
            TrRmTypeSpecTableWriter.Encode([new byte[] { 2, 0, 1, 0, 0, 0 }], 0, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() =>
            TrRmTypeSpecTableWriter.Encode([new byte[] { 7, 0, 1, 0, 0, 0 }], 0, 0, 0, 0));
    }
}
