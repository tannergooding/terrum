// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmTypeArgTableWriterTests
{
    [Fact]
    public void EncodesContiguousArgumentsOfConstructedType()
    {
        var specBytes = Specs();
        var specs = new TrRmTypeSpecTableReader(specBytes, 3, 1, 0, 0, 1);
        var rows = new[] { new TrRmTypeArgRow(3, 0, 1) };
        var bytes = TrRmTypeArgTableWriter.Encode(rows, specs);

        Assert.Equal(new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 }, bytes);
        rows[0] = rows[0] with { OwnerTypeSpecRow = 2 };
        _ = new TrRmTypeArgTableReader(bytes, 1, specs);
    }

    [Fact]
    public void RejectsInvalidOwnerOrdinalArgumentAndCount()
    {
        var row = new TrRmTypeArgRow(3, 0, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode([]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { OwnerTypeSpecRow = 2 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { Ordinal = 1 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { ArgumentTypeSpecRow = 3 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row, row]));
    }

    private static byte[] Encode(TrRmTypeArgRow[] rows)
    {
        var specBytes = Specs();
        var specs = new TrRmTypeSpecTableReader(specBytes, 3, 1, 0, 0, 1);
        return TrRmTypeArgTableWriter.Encode(rows, specs);
    }

    private static byte[] Specs()
        => TrRmTypeSpecTableWriter.Encode([
            new byte[] { 1, 0, 8 },
            new byte[] { 2, 0, 1, 0, 0, 0 },
            new byte[] { 5, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 },
        ], 1, 0, 0, 1);
}
