// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmMethodSigTableReaderTests
{
    [Test]
    public void ValidatesParameterModesAndZeroBasedOrdinals()
    {
        var types = Types();
        var signatures = new TrRmMethodSigTableReader(Signature(), 1, types, 2, 0);
        Assert.That(signatures.GetParamOwner(1), Is.EqualTo(1));
        Assert.That(signatures.GetParamOwner(2), Is.EqualTo(1));
        Assert.Throws<InvalidDataException>(() => GetOwner(0));
        Assert.Throws<InvalidDataException>(() => GetOwner(3));
        _ = new TrRmParamTableReader(Params(), 2, signatures, types);
    }

    [Test]
    public void ParameterRangesCanBeAssignedOutOfSignatureOrder()
    {
        var sigs = new byte[48];
        Write(sigs, 0, 1);
        Write(sigs, 8, 2);
        Write(sigs, 12, 1);
        Write(sigs, 24, 1);
        Write(sigs, 32, 1);
        Write(sigs, 36, 1);
        var paramRows = Params();
        Write(paramRows, 0, 2);
        Write(paramRows, 16, 1);
        Write(paramRows, 20, 0);

        var types = Types();
        var signatures = new TrRmMethodSigTableReader(sigs, 2, types, 2, 0);
        Assert.That(signatures.GetParamOwner(1), Is.EqualTo(2));
        Assert.That(signatures.GetParamOwner(2), Is.EqualTo(1));
        _ = new TrRmParamTableReader(paramRows, 2, signatures, types);
    }
    [TestCase(0,(byte)(0))]
    [TestCase(0,(byte)(2))]
    [TestCase(4,(byte)(2))]
    [TestCase(8,(byte)(0))]
    [TestCase(8,(byte)(2))]
    [TestCase(12,(byte)(0))]
    [TestCase(12,(byte)(3))]
    [TestCase(16,(byte)(1))]
    [TestCase(20,(byte)(1))]
    public void RejectsInvalidSignatureReferencesAndRanges(int offset, byte replacement)
    {
        var bytes = Signature();
        bytes[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => CreateSignature(bytes));
    }

    [Test]
    public void RejectsOverlappingAndUnownedParameterRanges()
    {
        var overlapping = new byte[48];
        Signature().CopyTo(overlapping, 0);
        Signature().CopyTo(overlapping, 24);
        Assert.Throws<InvalidDataException>(() => CreateSignature(overlapping, recordCount: 2));

        var unowned = Signature();
        Write(unowned, 12, 1);
        Assert.Throws<InvalidDataException>(() => CreateSignature(unowned));
        Assert.Throws<InvalidDataException>(() => CreateSignature(Signature()[..^1]));
    }
    [TestCase(0,(byte)(0))]
    [TestCase(0,(byte)(2))]
    [TestCase(4,(byte)(1))]
    [TestCase(8,(byte)(0))]
    [TestCase(8,(byte)(2))]
    [TestCase(12,(byte)(5))]
    [TestCase(13,(byte)(1))]
    [TestCase(14,(byte)(1))]
    [TestCase(15,(byte)(1))]
    [TestCase(20,(byte)(0))]
    [TestCase(24,(byte)(0))]
    [TestCase(28,(byte)(5))]
    public void RejectsInvalidParameterOwnerOrdinalAndFlags(int offset, byte replacement)
    {
        var bytes = Params();
        bytes[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => CreateParams(bytes));
    }

    [Test]
    public void RejectsWrongParameterCountAndStride()
    {
        Assert.Throws<InvalidDataException>(() => CreateParams(Params(), recordCount: 1));
        Assert.Throws<InvalidDataException>(() => CreateParams(Params()[..^1]));
    }

    private static int GetOwner(int row)
    {
        var signatures = new TrRmMethodSigTableReader(Signature(), 1, Types(), 2, 0);
        return signatures.GetParamOwner(row);
    }

    private static void CreateSignature(byte[] bytes, int recordCount = 1)
    {
        _ = new TrRmMethodSigTableReader(bytes, recordCount, Types(), 2, 0);
    }

    private static void CreateParams(byte[] bytes, int recordCount = 2)
    {
        var types = Types();
        var signatures = new TrRmMethodSigTableReader(Signature(), 1, types, 2, 0);
        _ = new TrRmParamTableReader(bytes, recordCount, signatures, types);
    }

    private static TrRmTypeSpecTableReader Types()
    {
        byte[] row = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        return new TrRmTypeSpecTableReader(row, 1, 0, 0, 0, 0);
    }

    private static byte[] Signature()
    {
        var row = new byte[24];
        Write(row, 0, 1);
        Write(row, 8, 1);
        Write(row, 12, 2);
        return row;
    }

    private static byte[] Params()
    {
        var rows = new byte[32];
        Write(rows, 0, 1);
        Write(rows, 8, 1);
        Write(rows, 16, 1);
        Write(rows, 20, 1);
        Write(rows, 24, 1);
        rows[28] = 4;
        return rows;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
