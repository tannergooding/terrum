// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmModuleTableReaderTests
{
    [Test]
    public void ResolvesDistinctModuleNamesAndEntryOwnership()
    {
        var strings = new TrRmStringTableReader(TwoNames(), 2);
        var modules = new TrRmModuleTableReader(TwoModules(), 2, strings, 0, 0, 2, 0);
        Assert.That(modules.GetIdentity(1), Is.EqualTo("A"));
        Assert.That(modules.GetIdentity(2), Is.EqualTo("B"));
        Assert.That(modules.TryFindModule("B", out var row), Is.True);
        Assert.That(row, Is.EqualTo(2));
        Assert.That(modules.TryFindModule("b", out row), Is.False);
        Assert.That(row, Is.EqualTo(0));
        Assert.That(modules.TryFindModule("\u00E9\U00010400", out row), Is.False);
        Assert.That(row, Is.EqualTo(0));
        Assert.Throws<ArgumentException>(() => FindModule(TwoModules(), ""));
        Assert.Throws<ArgumentException>(() => FindModule(TwoModules(), "A\0"));
        Assert.Throws<ArgumentException>(() => FindModule(TwoModules(), "\uD800"));
        Assert.Throws<ArgumentException>(() => FindModule(TwoModules(), "\uDC00"));
        Assert.Throws<InvalidDataException>(() => GetIdentity(TwoModules(), 0));
        Assert.Throws<InvalidDataException>(() => GetIdentity(TwoModules(), 3));
    }

    [Test]
    public void SelectsModulesByDecodedIdentityAcrossStringEncodings()
    {
        var section = TrRmStringTableWriter.EncodeNames([("A", 0), ("B", 2)]);
        var strings = new TrRmStringTableReader(section, 2);
        var modules = new TrRmModuleTableReader(TwoModules(), 2, strings, 0, 0, 2, 0);
        Assert.That(modules.TryFindModule("B", out var row), Is.True);
        Assert.That(row, Is.EqualTo(2));
    }
    [TestCase(0,(byte)(0))]
    [TestCase(4,(byte)(2))]
    [TestCase(8,(byte)(2))]
    [TestCase(12,(byte)(2))]
    [TestCase(16,(byte)(1))]
    [TestCase(20,(byte)(1))]
    [TestCase(24,(byte)(1))]
    [TestCase(28,(byte)(0))]
    [TestCase(32,(byte)(2))]
    [TestCase(36,(byte)(0))]
    [TestCase(40,(byte)(2))]
    [TestCase(44 + 36,(byte)(1))]
    public void RejectsInvalidFieldsAndModulePartitioning(int byteIndex, byte replacement)
    {
        var moduleBytes = TwoModules();
        moduleBytes[byteIndex] = replacement;
        Assert.Throws<InvalidDataException>(() => Create(moduleBytes));
    }

    [Test]
    public void RejectsDuplicateDecodedIdentitiesAcrossEncodings()
    {
        var bytes = TwoNames();
        bytes[20] = 2;
        bytes[24] = 65;
        Assert.Throws<InvalidDataException>(() => Create(TwoModules(), bytes));
    }

    [Test]
    public void RejectsTruncatedRowsAndOutOfRangeTableCounts()
    {
        Assert.Throws<InvalidDataException>(() => Create(TwoModules()[..^1]));
        Assert.Throws<InvalidDataException>(() => Create(TwoModules(), methodDefCount: -1));
    }

    [Test]
    public void ReadsFeatureRangesAndRejectsMalformedOrOutOfRangeRanges()
    {
        var moduleBytes = TwoModules();
        Write(moduleBytes, 16, 1);
        Write(moduleBytes, 20, 1);
        var strings = new TrRmStringTableReader(TwoNames(), 2);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, strings,
            0, 0, 2, 0, 1);
        Assert.That(modules.GetFeatureRange(1), Is.EqualTo((1, 1)));
        Assert.That(modules.GetFeatureRange(2), Is.EqualTo((0, 0)));
        Assert.Throws<InvalidDataException>(() => Create(moduleBytes, featureCount: 0));

        Write(moduleBytes, 20, 0);
        Assert.Throws<InvalidDataException>(() => Create(moduleBytes, featureCount: 1));
        Write(moduleBytes, 16, 2);
        Write(moduleBytes, 20, 1);
        Assert.Throws<InvalidDataException>(() => Create(moduleBytes, featureCount: 1));
    }

    private static void Create(byte[] modules, byte[]? names = null,
        int methodDefCount = 2, int featureCount = -1)
    {
        var strings = new TrRmStringTableReader(names ?? TwoNames(), 2);
        _ = new TrRmModuleTableReader(modules, 2, strings, 0, 0,
            methodDefCount, 0, featureCount);
    }

    private static string GetIdentity(byte[] modules, int row)
    {
        var strings = new TrRmStringTableReader(TwoNames(), 2);
        var table = new TrRmModuleTableReader(modules, 2, strings, 0, 0, 2, 0);
        return table.GetIdentity(row);
    }

    private static bool FindModule(byte[] modules, string identity)
    {
        var strings = new TrRmStringTableReader(TwoNames(), 2);
        var table = new TrRmModuleTableReader(modules, 2, strings, 0, 0, 2, 0);
        return table.TryFindModule(identity, out var row);
    }

    private static byte[] TwoNames() =>
    [
        0, 0, 0, 0, 8, 0, 0, 0, 16, 0, 0, 0,
        0, 1, 0, 0, 65, 0, 0, 0,
        0, 1, 0, 0, 66, 0, 0, 0,
    ];

    private static byte[] TwoModules()
    {
        var bytes = new byte[88];
        Write(bytes, 0, 1);
        Write(bytes, 4, 1);
        Write(bytes, 12, 1);
        Write(bytes, 28, 1);
        Write(bytes, 32, 1);
        Write(bytes, 36, 1);
        Write(bytes, 40, 1);
        Write(bytes, 44, 2);
        Write(bytes, 44 + 4, 1);
        Write(bytes, 44 + 28, 1);
        Write(bytes, 44 + 32, 1);
        Write(bytes, 44 + 36, 2);
        Write(bytes, 44 + 40, 1);
        return bytes;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
