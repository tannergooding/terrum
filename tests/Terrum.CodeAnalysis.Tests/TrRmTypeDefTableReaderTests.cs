// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmTypeDefTableReaderTests
{
    [Fact]
    public void DecodesEmptyNamespaceAndOwnedFieldRange()
    {
        var names = new TrRmStringTableReader(Names(), 2);
        var modules = new TrRmModuleTableReader(Module(), 1, names, 0, 1, 0, 0);
        var types = new TrRmTypeDefTableReader(Type(), 1, names, modules, 1, 0, 0, 0);
        Assert.Equal("", types.GetNamespace(1));
        Assert.Equal("Widget", types.GetName(1));
        Assert.Equal((1, 2), types.GetFieldRange(1));
        Assert.Equal(1, types.GetOwnerModule(1));
        Assert.Throws<InvalidDataException>(() => GetName(Type(), 0));
        Assert.Throws<InvalidDataException>(() => GetName(Type(), 2));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(8, 0)]
    [InlineData(8, 3)]
    [InlineData(8, 0x40)]
    [InlineData(12, 0)]
    [InlineData(12, 2)]
    [InlineData(16, 0)]
    [InlineData(20, 1)]
    [InlineData(28, 1)]
    [InlineData(32, 1)]
    public void RejectsMalformedReferencesFlagsAndRanges(int offset, byte replacement)
    {
        var bytes = Type();
        bytes[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => Create(bytes));
    }

    [Fact]
    public void RejectsTruncatedRowsAndMismatchedOwnerCounts()
    {
        Assert.Throws<InvalidDataException>(() => Create(Type()[..^1]));
        Assert.Throws<InvalidDataException>(() => Create(Type(), fieldCount: 0));
    }

    [Fact]
    public void PrivateAutoTypeCannotPublishLayout()
    {
        var bytes = Type();
        Write(bytes, 28, 1);
        Assert.Throws<InvalidDataException>(() => CreateWithLayout(bytes));
    }

    [Fact]
    public void RejectsOverlappingAndUnownedFieldRanges()
    {
        var duplicate = new byte[80];
        Type().CopyTo(duplicate, 0);
        Type().CopyTo(duplicate, 40);
        Assert.Throws<InvalidDataException>(() => Create(duplicate, typeCount: 2));

        var unowned = Type();
        Write(unowned, 12, 0);
        Write(unowned, 16, 0);
        Assert.Throws<InvalidDataException>(() => Create(unowned));
    }

    [Fact]
    public void ValidatesFieldRowsAgainstOwnerAndTypeSpec()
    {
        var bytes = Field();
        Assert.Equal("Widget", GetFieldName(bytes, 1));
        Assert.Throws<InvalidDataException>(() => GetFieldName(bytes, 0));
        Assert.Throws<InvalidDataException>(() => GetFieldName(bytes, 2));

        foreach (var (offset, replacement) in new (int, byte)[] {
            (0, 0), (0, 2), (4, 0), (8, 0), (8, 2), (12, 3), (12, 8),
            (16, 0),
        })
        {
            var invalid = Field();
            invalid[offset] = replacement;
            Assert.Throws<InvalidDataException>(() => GetFieldName(invalid, 1));
        }

        Assert.Throws<InvalidDataException>(() => GetFieldName(Field()[..^1], 1));
        Assert.Throws<InvalidDataException>(() => GetFieldName(Field(), 1, fieldCount: 0));
    }

    private static void Create(byte[] bytes, int fieldCount = 1, int typeCount = 1)
    {
        var names = new TrRmStringTableReader(Names(), 2);
        var modules = new TrRmModuleTableReader(Module(), 1, names, 0, typeCount, 0, 0);
        _ = new TrRmTypeDefTableReader(bytes, typeCount, names, modules, fieldCount, 0, 0, 0);
    }

    private static void CreateWithLayout(byte[] bytes)
    {
        var names = new TrRmStringTableReader(Names(), 2);
        var modules = new TrRmModuleTableReader(Module(), 1, names, 0, 1, 0, 0);
        _ = new TrRmTypeDefTableReader(bytes, 1, names, modules, 1, 0, 1, 0);
    }

    private static string GetName(byte[] bytes, int row)
    {
        var names = new TrRmStringTableReader(Names(), 2);
        var modules = new TrRmModuleTableReader(Module(), 1, names, 0, 1, 0, 0);
        var types = new TrRmTypeDefTableReader(bytes, 1, names, modules, 1, 0, 0, 0);
        return types.GetName(row);
    }

    private static string GetFieldName(byte[] bytes, int row, int fieldCount = 1)
    {
        var names = new TrRmStringTableReader(Names(), 2);
        var modules = new TrRmModuleTableReader(Module(), 1, names, 0, 1, 0, 0);
        var types = new TrRmTypeDefTableReader(Type(), 1, names, modules, 1, 0, 0, 0);
        byte[] specSection = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        var specs = new TrRmTypeSpecTableReader(specSection, 1, 1, 0, 0, 0);
        var fields = new TrRmFieldDefTableReader(bytes, fieldCount, names, types, specs);
        return fields.GetName(row);
    }

    private static byte[] Field()
    {
        var bytes = new byte[20];
        Write(bytes, 0, 1);
        Write(bytes, 4, 2);
        Write(bytes, 8, 1);
        Write(bytes, 16, uint.MaxValue);
        return bytes;
    }

    private static byte[] Names()
    {
        var result = new byte[32];
        Write(result, 4, 8);
        Write(result, 8, 20);
        byte[] widget = [0, 6, 0, 0, 87, 105, 100, 103, 101, 116, 0, 0];
        widget.CopyTo(result.AsSpan(20));
        return result;
    }

    private static byte[] Module()
    {
        var bytes = new byte[44];
        Write(bytes, 0, 2);
        Write(bytes, 4, 1);
        Write(bytes, 28, 1);
        Write(bytes, 32, 1);
        Write(bytes, 36, 1);
        Write(bytes, 40, 1);
        return bytes;
    }

    private static byte[] Type()
    {
        var bytes = new byte[40];
        Write(bytes, 0, 1);
        Write(bytes, 4, 2);
        Write(bytes, 8, 4);
        Write(bytes, 12, 1);
        Write(bytes, 16, 1);
        return bytes;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
