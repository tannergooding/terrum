// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmModuleRefTableReaderTests
{
    [Test]
    public void ResolvesPackedProducerRequirementsAndModuleOwnership()
    {
        var strings = new TrRmStringTableReader(ThreeNames(), 3);
        var modules = new TrRmModuleTableReader(TwoModules(), 2, strings, 2, 0, 2, 0);
        var refs = new TrRmModuleRefTableReader(TwoRefs(), 2, strings, modules);
        Assert.That(refs.GetProducerIdentity(1), Is.EqualTo("B"));
        Assert.That(refs.GetOwnerModule(1), Is.EqualTo(1));
        Assert.That(refs.GetProducerIdentity(2), Is.EqualTo("A"));
        Assert.That(refs.GetOwnerModule(2), Is.EqualTo(2));
        Assert.Throws<InvalidDataException>(() => GetProducerIdentity(TwoRefs(), 0));
        Assert.Throws<InvalidDataException>(() => GetOwnerModule(TwoRefs(), 3));
    }

    [Test]
    public void RetainsExternalProducerForLaterBinding()
    {
        var bytes = TwoRefs();
        Write(bytes, 12, 3);
        var strings = new TrRmStringTableReader(ThreeNames(), 3);
        var modules = new TrRmModuleTableReader(TwoModules(), 2, strings, 2, 0, 2, 0);
        var refs = new TrRmModuleRefTableReader(bytes, 2, strings, modules);
        Assert.That(refs.GetProducerIdentity(2), Is.EqualTo("X"));
        Assert.That(refs.GetOwnerModule(2), Is.EqualTo(2));
    }
    [TestCase(0,(byte)(0))]
    [TestCase(3,(byte)(1))]
    [TestCase(4,(byte)(0))]
    [TestCase(4,(byte)(2))]
    [TestCase(8,(byte)(2))]
    [TestCase(12 + 8,(byte)(1))]
    public void RejectsInvalidReferencesAndPackedProducerMismatches(int byteIndex, byte replacement)
    {
        var bytes = TwoRefs();
        bytes[byteIndex] = replacement;
        Assert.Throws<InvalidDataException>(() => Create(bytes));
    }

    [Test]
    public void RejectsTruncatedSectionsAndCountMismatch()
    {
        Assert.Throws<InvalidDataException>(() => Create(TwoRefs()[..^1]));
        Assert.Throws<InvalidDataException>(() => CreateWithCount(TwoRefs(), 1));
    }

    private static void Create(byte[] bytes)
    {
        var strings = new TrRmStringTableReader(ThreeNames(), 3);
        var modules = new TrRmModuleTableReader(TwoModules(), 2, strings, 2, 0, 2, 0);
        _ = new TrRmModuleRefTableReader(bytes, 2, strings, modules);
    }

    private static void CreateWithCount(byte[] bytes, int count)
    {
        var strings = new TrRmStringTableReader(ThreeNames(), 3);
        var modules = new TrRmModuleTableReader(TwoModules(), 2, strings, 2, 0, 2, 0);
        _ = new TrRmModuleRefTableReader(bytes, count, strings, modules);
    }

    private static string GetProducerIdentity(byte[] bytes, int row)
    {
        var strings = new TrRmStringTableReader(ThreeNames(), 3);
        var modules = new TrRmModuleTableReader(TwoModules(), 2, strings, 2, 0, 2, 0);
        var refs = new TrRmModuleRefTableReader(bytes, 2, strings, modules);
        return refs.GetProducerIdentity(row);
    }

    private static int GetOwnerModule(byte[] bytes, int row)
    {
        var strings = new TrRmStringTableReader(ThreeNames(), 3);
        var modules = new TrRmModuleTableReader(TwoModules(), 2, strings, 2, 0, 2, 0);
        var refs = new TrRmModuleRefTableReader(bytes, 2, strings, modules);
        return refs.GetOwnerModule(row);
    }

    private static byte[] ThreeNames() =>
    [
        0, 0, 0, 0, 8, 0, 0, 0, 16, 0, 0, 0, 24, 0, 0, 0,
        0, 1, 0, 0, 65, 0, 0, 0,
        0, 1, 0, 0, 66, 0, 0, 0,
        0, 1, 0, 0, 88, 0, 0, 0,
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
        Write(bytes, 44 + 28, 2);
        Write(bytes, 44 + 32, 1);
        Write(bytes, 44 + 36, 2);
        Write(bytes, 44 + 40, 1);
        return bytes;
    }

    private static byte[] TwoRefs()
    {
        var bytes = new byte[24];
        Write(bytes, 0, 2);
        Write(bytes, 4, 1);
        Write(bytes, 12, 1);
        Write(bytes, 12 + 4, 1);
        return bytes;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
