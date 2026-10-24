// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmLayoutTableReaderTests
{
    [Test]
    public void LinksDefaultLayoutToItsTypeDef()
    {
        var names = new TrRmStringTableReader(Names(), 2);
        var modules = new TrRmModuleTableReader(Module(names, 1), 1, names,
            0, 1, 0, 0);
        var types = new TrRmTypeDefTableReader(Type(), 1, names, modules,
            0, 0, 1, 0);
        var profiles = new TrRmTargetProfileTableReader([], 0);
        var layouts = new TrRmLayoutTableReader(Layout(), 1, types, profiles, 0);
        Assert.That(layouts.Count, Is.EqualTo(1));
        Assert.That(layouts.GetOwnerTypeDef(1), Is.EqualTo(1));
        Assert.That(layouts.GetTargetProfileRef(1), Is.Zero);
        Assert.That(layouts.GetLayoutKind(1), Is.Zero);
        Assert.That(layouts.GetLayoutFieldRange(1), Is.EqualTo((0, 0)));
        Assert.That(layouts.GetLayoutForProfile(1, 0), Is.EqualTo(1));
        Assert.That(layouts.HasBlittableVariant(1), Is.False);
    }

    [TestCase(0, 0)]
    [TestCase(0, 2)]
    [TestCase(4, 2)]
    [TestCase(8, 5)]
    [TestCase(9, 2)]
    [TestCase(10, 1)]
    [TestCase(12, 3)]
    [TestCase(16, 3)]
    [TestCase(20, 1)]
    public void RejectsInvalidOwnerProfileKindFlagsAndRanges(int offset, byte value)
    {
        var bytes = Layout();
        bytes[offset] = value;
        Assert.Throws<InvalidDataException>(() =>
            Create(bytes, 1, Type(), 1, [], 0));
    }

    [Test]
    public void RejectsTruncatedOrUnownedVariantsAndMissingDefault()
    {
        Assert.Throws<InvalidDataException>(() =>
            Create(Layout()[..^1], 1, Type(), 1, [], 0));

        var orphan = new byte[56];
        Layout().CopyTo(orphan, 0);
        Layout().CopyTo(orphan, 28);
        Assert.Throws<InvalidDataException>(() =>
            Create(orphan, 2, Type(), 1, [], 0));

        var targetOnlyType = Type();
        Write(targetOnlyType, 32, 1);
        var targetOnlyLayout = Layout();
        Write(targetOnlyLayout, 4, 1);
        Assert.Throws<InvalidDataException>(() =>
            Create(targetOnlyLayout, 1, targetOnlyType, 1,
                TrRmTargetProfileTableWriter.Encode([new TrRmTargetProfileRow(1, 1)]), 0));
    }

    [Test]
    public void RejectsOverlappingTypeDefLayoutRanges()
    {
        var typeRows = new byte[88];
        Type().CopyTo(typeRows, 0);
        Type().CopyTo(typeRows, 44);
        Assert.Throws<InvalidDataException>(() =>
            Create(Layout(), 1, typeRows, 2, [], 0));
    }

    private static byte[] Names()
        => TrRmStringTableWriter.EncodeTextRows([("", 0), ("Widget", 0)]);

    private static byte[] Module(TrRmStringTableReader names, int typeDefCount)
        => TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
        ], names, 0, typeDefCount, 0, 0);

    private static void Create(byte[] layoutBytes, int layoutCount,
        byte[] typeDefBytes, int typeDefCount, byte[] profileBytes,
        int layoutFieldCount)
    {
        var names = new TrRmStringTableReader(Names(), 2);
        var moduleBytes = Module(names, typeDefCount);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, names,
            0, typeDefCount, 0, 0);
        var types = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            names, modules, 0, 0, layoutCount, 0);
        var profiles = new TrRmTargetProfileTableReader(profileBytes,
            profileBytes.Length / sizeof(ulong));
        _ = new TrRmLayoutTableReader(layoutBytes, layoutCount,
            types, profiles, layoutFieldCount);
    }

    private static byte[] Type()
    {
        var row = new byte[44];
        Write(row, 0, 1);
        Write(row, 4, 2);
        Write(row, 8, 4);
        Write(row, 28, 1);
        Write(row, 32, 1);
        return row;
    }

    private static byte[] Layout()
    {
        var row = new byte[28];
        Write(row, 0, 1);
        row[8] = 0;
        return row;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
