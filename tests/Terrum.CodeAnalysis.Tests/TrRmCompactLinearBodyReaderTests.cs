// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmCompactLinearBodyReaderTests
{
    [Fact]
    public void ReconstructsCheckedAddDefinitionsAndLastUses()
    {
        byte[] row =
        [
            0x04, 1, 1, 0, 0, 0, 3,
            0x15, 3, 1, 1, 2,
            0x02, 4, 1, 3,
            0x15, 5, 1, 3, 4,
            0x83, 5,
        ];
        var index = Inspect(row, 2);
        Assert.Equal(3, index.Operations);
        Assert.Equal(5, index.ValueCount);
        Assert.Equal([-1, -1, 7, 12, 16], index.Definitions);
        Assert.Equal([0, 0, 2, 2, 3], index.LastUses);
        Assert.Equal([1, 1, 1, 1, 1], index.UseBlocks);
    }

    [Theory]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1 }, 0)]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 2, 1, 1, 0xFF, 0x83, 1 }, 0)]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 0, 0x83, 1 }, 1)]
    public void ReadsConstantAndParameterReturns(byte[] row, int parameterCount)
    {
        var index = Inspect(row, parameterCount);
        Assert.Equal(1, index.ValueCount);
        Assert.Equal(parameterCount == 0 ? 7 : -1, index.Definitions[0]);
        Assert.Equal(index.Operations, index.LastUses[0]);
        Assert.Equal([1], index.UseBlocks);
    }

    [Theory]
    [InlineData(new byte[] { 0x05, 1, 0, 1, 0, 0, 0, 0, 0, 0x83, 1 })]
    [InlineData(new byte[] { 0x06, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0x83, 1 })]
    public void BodyLocalWidthDoesNotWidenInstructionOperands(byte[] row)
        => Assert.Equal([-1], Inspect(row, 1).Definitions);

    [Fact]
    public void ReadsWideSignedConstantWithoutChangingBodyLocalWidth()
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x10, 2, 1, 1, 200, 0, 0x83, 1];
        var index = Inspect(row, 0);
        Assert.Equal([7], index.Definitions);
        Assert.Equal([1], index.LastUses);
    }

    [Theory]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 1,
        0xF0, 0x20, 1, 1, 1, 0, 0, 1, 0, 0x83, 1 }, (byte)9)]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 1,
        0xF0, 0x30, 1, 1, 1, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0x83, 1 }, (byte)9)]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 1,
        2, 1, 1, 0xFF, 0x83, 1 }, (byte)9)]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 1,
        0xF0, 0x30, 2, 1, 1, 0xFF, 0xFF, 0xFF, 0xFF,
        0, 0, 0, 0, 0x83, 1 }, (byte)0x0D)]
    public void AcceptsFullWidthAndSignedNumericConstants(byte[] row, byte typeCode)
        => Assert.Equal([1], Inspect(row, 0, typeCode: typeCode).UseBlocks);

    [Theory]
    [InlineData((byte)0x0D)]
    [InlineData((byte)0x0E)]
    public void RejectsNegativeSignedLiteralForUnsignedType(byte typeCode)
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 1, 2, 1, 1, 0xFF, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => Inspect(row, 0, typeCode: typeCode));
    }

    [Fact]
    public void AcceptsValueFreeFailWithoutTypeMap()
    {
        byte[] row = [0x04, 0, 0, 0x87, 1];
        var index = Inspect(row, 0);
        Assert.Equal(0, index.ValueCount);
        Assert.Equal(0, index.Operations);
        Assert.Empty(index.Definitions);
    }

    [Fact]
    public void UnusedParameterHasNoUseBlock()
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 1, 1, 2, 1, 42, 0x83, 2];
        var index = Inspect(row, 1);
        Assert.Equal([0, 1], index.UseBlocks);
        Assert.Equal([0, 1], index.LastUses);
    }

    [Fact]
    public void MatchesMethodSignatureByResolvedTypeRatherThanTypeSpecRow()
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 0, 0x83, 1];
        Assert.Equal([1], Inspect(row, 1, duplicateSignatureType: true).UseBlocks);
    }

    [Theory]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1, 0 }, 0)]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 1, 2, 1, 42, 0x83, 1 }, 0)]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x04, 1, 1, 1, 42, 0x83, 1 }, 0)]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x00, 1, 1, 1, 42, 0x83, 1 }, 0)]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 0x15, 1, 1, 1, 2, 0x83, 1 }, 0)]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 3, 1, 0x83, 1 }, 0)]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 0, 0x83, 2 }, 1)]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 0, 0x81, 1 }, 1)]
    [InlineData(new byte[] { 0x04, 1, 1, 0, 0, 0, 0, 0x87, 5 }, 0)]
    public void RejectsMalformedOrUnsupportedBodies(byte[] row, int parameterCount)
        => Assert.Throws<InvalidDataException>(() => Inspect(row, parameterCount));

    [Fact]
    public void RejectsCheckedExceptionPolicyAndNonValueParameters()
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 0, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => Inspect(row, 1, runtimePolicy: 1));
        Assert.Throws<InvalidDataException>(() => Inspect(row, 1, parameterMode: 2));
    }

    [Fact]
    public void RejectsUnknownTypeMapReference()
    {
        byte[] row = [0x04, 1, 2, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => Inspect(row, 0));
    }

    private static (int Operations, int ValueCount, int[] Definitions, int[] LastUses,
        int[] UseBlocks) Inspect(
        byte[] row, int parameterCount, byte runtimePolicy = 0, byte parameterMode = 0,
        bool duplicateSignatureType = false, byte typeCode = 8)
    {
        var names = new TrRmStringTableReader(
            TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0)]), 2);
        var moduleBytes = new byte[44];
        Write(moduleBytes, 0, 1);
        Write(moduleBytes, 4, 1);
        Write(moduleBytes, 8, runtimePolicy);
        Write(moduleBytes, 28, 1);
        Write(moduleBytes, 32, 1);
        Write(moduleBytes, 36, 1);
        Write(moduleBytes, 40, 1);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, names, 0, 0, 1, 0);
        var typeDefs = new TrRmTypeDefTableReader([], 0, names, modules, 0, 0, 0, 0);
        byte[] typeBytes = duplicateSignatureType
            ? [0, 0, 0, 0, 3, 0, 0, 0, 6, 0, 0, 0, 1, 0, typeCode, 1, 0, typeCode]
            : [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, typeCode];
        var types = new TrRmTypeSpecTableReader(typeBytes, duplicateSignatureType ? 2 : 1,
            0, 0, 0, 0);
        var signatureType = duplicateSignatureType ? 2u : 1u;
        var signatureBytes = new byte[24];
        Write(signatureBytes, 0, signatureType);
        Write(signatureBytes, 8, parameterCount == 0 ? 0u : 1u);
        Write(signatureBytes, 12, (uint)parameterCount);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1, types, parameterCount, 0);
        var paramBytes = new byte[parameterCount * 16];
        for (var index = 0; index < parameterCount; index++)
        {
            Write(paramBytes, index * 16, 1);
            Write(paramBytes, index * 16 + 4, (uint)index);
            Write(paramBytes, index * 16 + 8, signatureType);
            paramBytes[index * 16 + 12] = parameterMode;
        }

        var parameters = parameterCount == 0 ? default
            : new TrRmParamTableReader(paramBytes, parameterCount, signatures, types);
        var methodBytes = new byte[28];
        Write(methodBytes, 4, 2);
        Write(methodBytes, 8, 1);
        Write(methodBytes, 12, 2);
        Write(methodBytes, 24, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1, names, modules,
            typeDefs, signatures, 0, 1);
        var bodyBytes = new byte[8 + row.Length];
        Write(bodyBytes, 4, (uint)row.Length);
        row.CopyTo(bodyBytes, 8);
        var bodies = new TrRmBodyTableReader(bodyBytes, 1);
        var reader = new TrRmCompactLinearBodyReader(bodies, 1, modules, methods,
            signatures, parameters, types);
        var definitions = new int[reader.ValueCount];
        var lastUses = new int[reader.ValueCount];
        var useBlocks = new int[reader.ValueCount];
        for (var index = 0; index < reader.ValueCount; index++)
        {
            definitions[index] = reader.GetDefinitionOffset(index + 1);
            lastUses[index] = reader.GetLastUseOrdinal(index + 1);
            useBlocks[index] = reader.GetUseBlockCount(index + 1);
            Assert.Equal(typeCode, reader.GetTypeCode(index + 1));
        }

        return (reader.OperationCount, reader.ValueCount, definitions, lastUses, useBlocks);
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
