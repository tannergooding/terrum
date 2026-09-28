// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmLayoutTableReaderTests
{
    [Fact]
    public void LinksPublishedLayoutToExactlyOneType()
    {
        Create(Layout());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 2)]
    [InlineData(4, 2)]
    [InlineData(20, 1)]
    public void RejectsMismatchedOwnerTargetAndReservedFlags(int offset, byte value)
    {
        var bytes = Layout();
        bytes[offset] = value;
        Assert.Throws<InvalidDataException>(() => Create(bytes));
    }

    [Fact]
    public void RejectsOrphanSharedAndTruncatedLayouts()
    {
        Assert.Throws<InvalidDataException>(() => Create(Layout()[..^1]));
        var orphan = new byte[48];
        Layout().CopyTo(orphan, 0);
        Layout().CopyTo(orphan, 24);
        Assert.Throws<InvalidDataException>(() => Create(orphan, layoutCount: 2));

        var duplicatedTypes = new byte[80];
        Type().CopyTo(duplicatedTypes, 0);
        Type().CopyTo(duplicatedTypes, 40);
        Assert.Throws<InvalidDataException>(() => Create(Layout(), typeRows: duplicatedTypes,
            typeCount: 2));
    }

    private static void Create(byte[] layout, byte[]? typeRows = null, int typeCount = 1,
        int layoutCount = 1)
    {
        var names = new TrRmStringTableReader(TrRmStringTableWriter.EncodeTextRows(
            [("", 0), ("Widget", 0)]), 2);
        var modules = new TrRmModuleTableReader(Module(), 1, names, 0, typeCount, 0, 0);
        var types = new TrRmTypeDefTableReader(typeRows ?? Type(), typeCount, names,
            modules, 0, 0, layoutCount, 0);
        _ = new TrRmLayoutTableReader(layout, layoutCount, types);
    }

    private static byte[] Module()
    {
        var row = new byte[44];
        Write(row, 0, 2);
        Write(row, 4, 1);
        Write(row, 28, 1);
        Write(row, 32, 1);
        Write(row, 36, 1);
        Write(row, 40, 1);
        return row;
    }

    private static byte[] Type()
    {
        var row = new byte[40];
        Write(row, 0, 1);
        Write(row, 4, 2);
        Write(row, 8, 0x14);
        Write(row, 28, 1);
        return row;
    }

    private static byte[] Layout()
    {
        var row = new byte[24];
        Write(row, 0, 1);
        Write(row, 4, 1);
        Write(row, 8, 4);
        Write(row, 12, 4);
        return row;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
