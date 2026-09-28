// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmMethodSigTableReaderTests
{
    [Fact]
    public void ValidatesParameterModesAndZeroBasedOrdinals()
    {
        var types = Types();
        var signatures = new TrRmMethodSigTableReader(Signature(), 1, types, 2, 0);
        Assert.Equal(1, signatures.GetParamOwner(1));
        Assert.Equal(1, signatures.GetParamOwner(2));
        Assert.Throws<InvalidDataException>(() => GetOwner(0));
        Assert.Throws<InvalidDataException>(() => GetOwner(3));
        _ = new TrRmParamTableReader(Params(), 2, signatures, types);
    }

    [Fact]
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
        Assert.Equal(2, signatures.GetParamOwner(1));
        Assert.Equal(1, signatures.GetParamOwner(2));
        _ = new TrRmParamTableReader(paramRows, 2, signatures, types);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 2)]
    [InlineData(4, 2)]
    [InlineData(8, 0)]
    [InlineData(8, 2)]
    [InlineData(12, 0)]
    [InlineData(12, 3)]
    [InlineData(16, 1)]
    [InlineData(20, 1)]
    public void RejectsInvalidSignatureReferencesAndRanges(int offset, byte replacement)
    {
        var bytes = Signature();
        bytes[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => CreateSignature(bytes));
    }

    [Fact]
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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 2)]
    [InlineData(4, 1)]
    [InlineData(8, 0)]
    [InlineData(8, 2)]
    [InlineData(12, 5)]
    [InlineData(13, 1)]
    [InlineData(14, 1)]
    [InlineData(15, 1)]
    [InlineData(20, 0)]
    [InlineData(24, 0)]
    [InlineData(28, 5)]
    public void RejectsInvalidParameterOwnerOrdinalAndFlags(int offset, byte replacement)
    {
        var bytes = Params();
        bytes[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => CreateParams(bytes));
    }

    [Fact]
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
