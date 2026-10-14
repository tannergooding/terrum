// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmTypeDefTableReaderTests
{
    [Test]
    public void DecodesEmptyNamespaceAndOwnedFieldRange()
    {
        var names = new TrRmStringTableReader(Names(), 2);
        var modules = new TrRmModuleTableReader(Module(), 1, names, 0, 1, 0, 0);
        var types = new TrRmTypeDefTableReader(Type(), 1, names, modules, 1, 0, 0, 0);
        Assert.That(types.GetNamespace(1), Is.EqualTo(""));
        Assert.That(types.GetName(1), Is.EqualTo("Widget"));
        Assert.That(types.GetFieldRange(1), Is.EqualTo((1, 2)));
        Assert.That(types.GetOwnerModule(1), Is.EqualTo(1));
        Assert.Throws<InvalidDataException>(() => GetName(Type(), 0));
        Assert.Throws<InvalidDataException>(() => GetName(Type(), 2));
    }
    [TestCase(0,(byte)(0))]
    [TestCase(4,(byte)(0))]
    [TestCase(8,(byte)(0))]
    [TestCase(8,(byte)(3))]
    [TestCase(8,(byte)(0x40))]
    [TestCase(12,(byte)(0))]
    [TestCase(12,(byte)(2))]
    [TestCase(16,(byte)(0))]
    [TestCase(20,(byte)(1))]
    [TestCase(28,(byte)(1))]
    [TestCase(32,(byte)(1))]
    public void RejectsMalformedReferencesFlagsAndRanges(int offset, byte replacement)
    {
        var bytes = Type();
        bytes[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => Create(bytes));
    }

    [Test]
    public void RejectsTruncatedRowsAndMismatchedOwnerCounts()
    {
        Assert.Throws<InvalidDataException>(() => Create(Type()[..^1]));
        Assert.Throws<InvalidDataException>(() => Create(Type(), fieldCount: 0));
    }

    [Test]
    public void PrivateAutoTypeCannotPublishLayout()
    {
        var bytes = Type();
        Write(bytes, 28, 1);
        Assert.Throws<InvalidDataException>(() => CreateWithLayout(bytes));
    }

    [Test]
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

    [Test]
    public void ValidatesFieldRowsAgainstOwnerAndTypeSpec()
    {
        var bytes = Field();
        Assert.That(GetFieldName(bytes, 1), Is.EqualTo("Widget"));
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
        => TrRmStringTableWriter.EncodeTextRows([("", 0), ("Widget", 0)]);

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
