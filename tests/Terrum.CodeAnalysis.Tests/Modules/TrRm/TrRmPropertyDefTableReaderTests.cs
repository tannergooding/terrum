// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmPropertyDefTableReaderTests
{
    [Test]
    public void GetterResultAndDeclaringTypeMatchWithoutNameInference()
    {
        Assert.That(ReadName(Property(), 1), Is.EqualTo("Value"));
        Assert.Throws<InvalidDataException>(() => ReadName(Property(), 0));
        Assert.Throws<InvalidDataException>(() => ReadName(Property(), 2));
    }
    [TestCase(0,(byte)(0))]
    [TestCase(0,(byte)(2))]
    [TestCase(4,(byte)(0))]
    [TestCase(8,(byte)(0))]
    [TestCase(8,(byte)(2))]
    [TestCase(12,(byte)(0))]
    [TestCase(12,(byte)(2))]
    [TestCase(16,(byte)(3))]
    [TestCase(16,(byte)(4))]
    public void RejectsBadPropertyReferencesAndFlags(int offset, byte replacement)
    {
        var bytes = Property();
        bytes[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => ReadName(bytes, 1));
    }

    [Test]
    public void RejectsGetterWithDifferentOwnerOrResult()
    {
        var moduleLevel = Method();
        Write(moduleLevel, 0, 0);
        var moduleLevelSignature = Signature();
        Write(moduleLevelSignature, 4, 0);
        Assert.Throws<InvalidDataException>(() =>
            ReadName(Property(), 1, method: moduleLevel, signature: moduleLevelSignature));

        var wrongResult = Signature();
        Write(wrongResult, 0, 2);
        Assert.Throws<InvalidDataException>(() => ReadName(Property(), 1, signature: wrongResult));
        Assert.Throws<InvalidDataException>(() => ReadName(Property()[..^1], 1));
    }

    private static string ReadName(byte[] property, int row, byte[]? method = null,
        byte[]? signature = null)
    {
        var strings = new TrRmStringTableReader(TrRmStringTableWriter.EncodeTextRows(
            [("", 0), ("Module", 0), ("Widget", 0), ("Read", 0), ("Value", 0)]), 5);
        var modules = new TrRmModuleTableReader(Module(), 1, strings, 0, 1, 1, 0);
        var types = new TrRmTypeDefTableReader(Type(), 1, strings, modules, 0, 0, 0, 0);
        byte[] typeRows = [
            0, 0, 0, 0, 3, 0, 0, 0, 9, 0, 0, 0,
            1, 0, 8, 2, 0, 1, 0, 0, 0,
        ];
        var typeSpecs = new TrRmTypeSpecTableReader(typeRows, 2, 1, 0, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signature ?? Signature(), 1,
            typeSpecs, 0, 0);
        var methods = new TrRmMethodDefTableReader(method ?? Method(), 1,
            strings, modules, types, signatures, 0, 0);
        var properties = new TrRmPropertyDefTableReader(property, 1,
            strings, types, typeSpecs, methods, signatures);
        return properties.GetName(row);
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
        Write(row, 4, 3);
        Write(row, 8, 4);
        return row;
    }

    private static byte[] Signature()
    {
        var row = new byte[24];
        Write(row, 0, 1);
        Write(row, 4, 2);
        return row;
    }

    private static byte[] Method()
    {
        var row = new byte[28];
        Write(row, 0, 1);
        Write(row, 4, 4);
        Write(row, 8, 1);
        return row;
    }

    private static byte[] Property()
    {
        var row = new byte[20];
        Write(row, 0, 1);
        Write(row, 4, 5);
        Write(row, 8, 1);
        Write(row, 12, 1);
        return row;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
