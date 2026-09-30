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
    public void ReturnZeroRequiresVoidResultAndNoValueDefinition()
    {
        byte[] row = [0x04, 0, 0, 0x83, 0];
        var index = Inspect(row, 0, typeCode: 0);
        Assert.Equal(0, index.ValueCount);
        Assert.Equal(0, index.Operations);
        Assert.Empty(index.UseBlocks);
        Assert.Throws<InvalidDataException>(() => Inspect(row, 0));

        byte[] invalid = [0x04, 0, 0, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => Inspect(invalid, 0, typeCode: 0));
    }

    [Fact]
    public void OptionalPresenceRetainsCompleteValueTypes()
    {
        byte[] make =
        [
            0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1,
            8, 2, 2, 1, 0x83, 2,
        ];
        var wrapped = Inspect(make, 1, secondTypeCode: 1, secondTypeOptional: true,
            resultUsesSecondType: true, typeCode: 1);
        Assert.Equal([false, true], wrapped.Optional);
        Assert.Equal([1, 1], wrapped.TypeCodes);
        Assert.Equal([0, 1], wrapped.LastUses);

        byte[] none = [0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 9, 1, 2, 0x83, 1];
        Assert.Equal([true], Inspect(none, 0, typeCode: 1, secondTypeCode: 1,
            secondTypeOptional: true, resultUsesSecondType: true).Optional);

        byte[] present = [0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0x0A, 2, 1, 0x83, 2];
        var checkedPresence = Inspect(present, 1, typeCode: 1, secondTypeCode: 1,
            secondTypeOptional: true, parameterUsesSecondType: true);
        Assert.Equal([true, false], checkedPresence.Optional);
        Assert.Equal([0, 1], checkedPresence.LastUses);
    }

    [Fact]
    public void OptionalCopyAndSelectKeepPresenceQualified()
    {
        byte[] copied = [0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 3, 2, 1, 0x83, 2];
        var copy = Inspect(copied, 1, typeCode: 8, secondTypeCode: 8,
            secondTypeOptional: true, parameterUsesSecondType: true, resultUsesSecondType: true);
        Assert.Equal([true, true], copy.Optional);
        Assert.Equal([8, 8], copy.TypeCodes);

        byte[] selected =
        [
            0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 4,
            1, 1, 1, 1,
            9, 2, 2,
            9, 3, 2,
            7, 4, 1, 2, 3,
            0x83, 4,
        ];
        var choice = Inspect(selected, 0, typeCode: 1, secondTypeCode: 1,
            secondTypeOptional: true, resultUsesSecondType: true);
        Assert.Equal([false, true, true, true], choice.Optional);
        Assert.Equal([3, 3, 3, 4], choice.LastUses);
    }

    [Fact]
    public void RejectsOptionalUseAsScalarOrUnmappedOptionalType()
    {
        byte[] header = [0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1];
        byte[] invalidNone = [.. header, 9, 1, 1, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => Inspect(invalidNone, 0, typeCode: 1,
            secondTypeCode: 1, secondTypeOptional: true));

        byte[] invalidPresent = [.. header, 0x0A, 2, 1, 0x83, 2];
        Assert.Throws<InvalidDataException>(() => Inspect(invalidPresent, 1, typeCode: 1,
            secondTypeCode: 1, secondTypeOptional: true));

        byte[] invalidConstant = [.. header, 1, 1, 2, 1, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => Inspect(invalidConstant, 0, typeCode: 1,
            secondTypeCode: 1, secondTypeOptional: true, resultUsesSecondType: true));

        byte[] nested = [.. header, 8, 2, 2, 1, 0x83, 2];
        Assert.Throws<InvalidDataException>(() => Inspect(nested, 1, typeCode: 1,
            secondTypeCode: 1, secondTypeOptional: true, parameterUsesSecondType: true,
            resultUsesSecondType: true));

        byte[] comparison = [.. header, 5, 3, 4, 1, 2, 0x83, 3];
        Assert.Throws<InvalidDataException>(() => Inspect(comparison, 2, typeCode: 1,
            secondTypeCode: 1, secondTypeOptional: true, parameterUsesSecondType: true));
    }

    [Theory]
    [InlineData(new byte[] { 3, 2, 1 }, (byte)8)]
    [InlineData(new byte[] { 4, 2, 1, 1 }, (byte)1)]
    [InlineData(new byte[] { 4, 2, 2, 1 }, (byte)8)]
    [InlineData(new byte[] { 4, 2, 3, 1 }, (byte)0x0D)]
    [InlineData(new byte[] { 0x17, 2, 1 }, (byte)8)]
    [InlineData(new byte[] { 0x17, 2, 1 }, (byte)9)]
    public void ScalarCopyUnaryAndNegateRetainType(byte[] operation, byte typeCode)
    {
        var row = OneParameterOperation(operation);
        var index = Inspect(row, 1, typeCode: typeCode);
        Assert.Equal([typeCode, typeCode], index.TypeCodes);
        Assert.Equal([1, 1], index.UseBlocks);
        Assert.Equal([0, 1], index.LastUses);
    }

    [Theory]
    [InlineData(new byte[] { 4, 2, 1, 1 }, (byte)8)]
    [InlineData(new byte[] { 4, 2, 2, 1 }, (byte)1)]
    [InlineData(new byte[] { 4, 2, 0, 1 }, (byte)8)]
    [InlineData(new byte[] { 0x17, 2, 1 }, (byte)0x0D)]
    [InlineData(new byte[] { 3, 2, 2 }, (byte)8)]
    public void RejectsMismatchedUnaryAndForwardCopy(byte[] operation, byte typeCode)
        => Assert.Throws<InvalidDataException>(() =>
            Inspect(OneParameterOperation(operation), 1, typeCode: typeCode));

    [Theory]
    [InlineData(new byte[] { 5, 3, 1, 1, 2 }, (byte)8)]
    [InlineData(new byte[] { 5, 3, 4, 1, 2 }, (byte)8)]
    [InlineData(new byte[] { 5, 3, 6, 1, 2 }, (byte)8)]
    [InlineData(new byte[] { 5, 3, 10, 1, 2 }, (byte)8)]
    [InlineData(new byte[] { 5, 3, 4, 1, 2 }, (byte)1)]
    [InlineData(new byte[] { 5, 3, 5, 1, 2 }, (byte)3)]
    public void BinaryOperationChecksItsScalarResult(byte[] operation, byte sourceType)
    {
        var returnsBool = operation[2] is >= 4 and <= 9;
        var differentResult = returnsBool && sourceType != 1;
        var row = TwoParameterOperation(operation, differentResult);
        var index = Inspect(row, 2, typeCode: sourceType,
            secondTypeCode: differentResult ? (byte)1 : null,
            resultUsesSecondType: differentResult);
        Assert.Equal(returnsBool ? (byte)1 : sourceType, index.TypeCodes[2]);
        Assert.Equal([0, 0, 1], index.LastUses);
    }

    [Theory]
    [InlineData(new byte[] { 5, 3, 0, 1, 2 }, (byte)8)]
    [InlineData(new byte[] { 5, 3, 1, 1, 2 }, (byte)1)]
    [InlineData(new byte[] { 5, 3, 6, 1, 2 }, (byte)3)]
    [InlineData(new byte[] { 0x15, 3, 6, 1, 2 }, (byte)8)]
    public void RejectsInvalidBinaryKindOrOperandType(byte[] operation, byte typeCode)
        => Assert.Throws<InvalidDataException>(() =>
            Inspect(TwoParameterOperation(operation, false), 2, typeCode: typeCode));

    [Fact]
    public void ComparisonMustMapItsImplicitBooleanResult()
    {
        byte[] operation = [5, 3, 4, 1, 2];
        Assert.Throws<InvalidDataException>(() =>
            Inspect(TwoParameterOperation(operation, false), 2));
    }

    [Fact]
    public void SelectRequiresBooleanConditionAndEqualValueTypes()
    {
        byte[] row =
        [
            0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 4,
            1, 1, 2, 1,
            1, 2, 1, 42,
            1, 3, 1, 43,
            7, 4, 1, 2, 3,
            0x83, 4,
        ];
        var index = Inspect(row, 0, secondTypeCode: 1);
        Assert.Equal([1, 8, 8, 8], index.TypeCodes);
        Assert.Equal([3, 3, 3, 4], index.LastUses);

        row[25] = 2;
        Assert.Throws<InvalidDataException>(() => Inspect(row, 0, secondTypeCode: 1));
    }

    [Fact]
    public void CheckedShiftRequiresUnsigned32Count()
    {
        byte[] row =
        [
            0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 2,
            1, 2, 2, 1,
            0x15, 3, 6, 1, 2,
            0x83, 3,
        ];
        Assert.Equal([8, 0x0D, 8], Inspect(row, 1, secondTypeCode: 0x0D).TypeCodes);
        Assert.Throws<InvalidDataException>(() => Inspect(row, 1, secondTypeCode: 1));
    }

    [Theory]
    [InlineData(new byte[] { 6, 2, 2, 1, 1 }, (byte)8, (byte)0x0D)]
    [InlineData(new byte[] { 6, 2, 2, 2, 1 }, (byte)9, (byte)0x0D)]
    [InlineData(new byte[] { 6, 2, 2, 3, 1 }, (byte)8, (byte)0x0D)]
    [InlineData(new byte[] { 0x16, 2, 2, 1 }, (byte)3, (byte)0x0D)]
    [InlineData(new byte[] { 0x16, 2, 2, 1 }, (byte)8, (byte)3)]
    public void NumericConversionsRetainSelectedPolicyAndTargetType(
        byte[] operation, byte sourceType, byte destinationType)
    {
        var row = OneParameterMappedOperation(operation);
        var index = Inspect(row, 1, typeCode: sourceType,
            secondTypeCode: destinationType, resultUsesSecondType: true);
        Assert.Equal([sourceType, destinationType], index.TypeCodes);
        Assert.Equal([0, 1], index.LastUses);
    }

    [Fact]
    public void CheckedDivRemDefinesTwoValuesAtOneOperation()
    {
        byte[] row =
        [
            0x04, 1, 1, 0, 0, 0, 1,
            0x18, 3, 4, 1, 2,
            0x83, 4,
        ];
        var index = Inspect(row, 2);
        Assert.Equal(4, index.ValueCount);
        Assert.Equal([-1, -1, 7, 7], index.Definitions);
        Assert.Equal([0, 0, 0, 1], index.LastUses);
        Assert.Equal([1, 1, 0, 1], index.UseBlocks);
        Assert.Equal([8, 8, 8, 8], index.TypeCodes);

        row[9] = 5;
        Assert.Throws<InvalidDataException>(() => Inspect(row, 2));
    }

    [Fact]
    public void MultipleDivRemOperationsKeepSequentialResultIds()
    {
        byte[] row =
        [
            0x04, 1, 1, 0, 0, 0, 2,
            0x18, 3, 4, 1, 2,
            0x18, 5, 6, 3, 4,
            0x83, 6,
        ];
        var index = Inspect(row, 2);
        Assert.Equal(6, index.ValueCount);
        Assert.Equal([-1, -1, 7, 7, 12, 12], index.Definitions);
        Assert.Equal([0, 0, 1, 1, 0, 2], index.LastUses);
    }

    [Theory]
    [InlineData(new byte[] { 6, 2, 2, 0, 1 }, (byte)8, (byte)0x0D)]
    [InlineData(new byte[] { 6, 2, 2, 4, 1 }, (byte)8, (byte)0x0D)]
    [InlineData(new byte[] { 6, 2, 2, 3, 1 }, (byte)8, (byte)0x0E)]
    [InlineData(new byte[] { 6, 2, 2, 1, 1 }, (byte)3, (byte)0x0D)]
    [InlineData(new byte[] { 0x16, 2, 2, 1 }, (byte)1, (byte)8)]
    [InlineData(new byte[] { 0x16, 2, 3, 1 }, (byte)8, (byte)0x0D)]
    public void RejectsInvalidConversionPolicyTypesAndTypeId(
        byte[] operation, byte sourceType, byte destinationType)
        => Assert.Throws<InvalidDataException>(() =>
            Inspect(OneParameterMappedOperation(operation), 1, typeCode: sourceType,
                secondTypeCode: destinationType, resultUsesSecondType: true));

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

    [Theory]
    [InlineData(new byte[] { 0x12, 2, 2, 1, 1 })]
    [InlineData(new byte[] { 0xF0, 0x04, 0x12, 2, 2, 1, 0, 1 })]
    [InlineData(new byte[] { 0xF0, 0x05, 0x12, 2, 0, 2, 0, 1, 0, 1, 0 })]
    public void InspectsLocalStaticScalarCall(byte[] operation)
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 1, .. operation, 0x83, 2];
        var index = Inspect(row, 1, secondMethod: true);
        Assert.Equal(2, index.ValueCount);
        Assert.Equal([-1, 7], index.Definitions);
        Assert.Equal([0, 1], index.LastUses);
        Assert.Equal([1, 1], index.UseBlocks);
    }

    [Fact]
    public void InspectsVoidLocalCallWithoutValueDefinition()
    {
        byte[] row = [0x04, 0, 1, 0x12, 0, 2, 0, 0x83, 0];
        var index = Inspect(row, 0, typeCode: 0, secondMethod: true);
        Assert.Equal(1, index.Operations);
        Assert.Equal(0, index.ValueCount);
        Assert.Empty(index.Definitions);
    }

    [Theory]
    [InlineData(new byte[] { 0x12, 2, 0, 1, 1 })]
    [InlineData(new byte[] { 0x12, 2, 3, 1, 1 })]
    [InlineData(new byte[] { 0x12, 2, 2, 0, 1 })]
    [InlineData(new byte[] { 0x12, 2, 2, 2, 1 })]
    [InlineData(new byte[] { 0x12, 0, 2, 1, 1 })]
    [InlineData(new byte[] { 0x12, 2, 2, 1, 2 })]
    [InlineData(new byte[] { 0xF0, 0x0C, 0x12, 2, 2, 1, 1 })]
    public void RejectsMalformedLocalCalls(byte[] operation)
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 1, .. operation, 0x83, 2];
        Assert.Throws<InvalidDataException>(() => Inspect(row, 1, secondMethod: true));
    }

    [Fact]
    public void RejectsLocalCallToMethodWithoutBodyOrMatchingParameterMode()
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 1, 0x12, 2, 2, 1, 1, 0x83, 2];
        Assert.Throws<InvalidDataException>(() => Inspect(row, 1,
            secondMethod: true, secondBody: false));
        Assert.Throws<InvalidDataException>(() => Inspect(row, 1,
            secondMethod: true, parameterMode: 2));
    }

    private static (int Operations, int ValueCount, int[] Definitions, int[] LastUses,
        int[] UseBlocks, byte[] TypeCodes, bool[] Optional) Inspect(
        byte[] row, int parameterCount, byte runtimePolicy = 0, byte parameterMode = 0,
        bool duplicateSignatureType = false, byte typeCode = 8,
        byte? secondTypeCode = null, bool resultUsesSecondType = false,
        bool secondTypeOptional = false, bool parameterUsesSecondType = false,
        bool secondMethod = false, bool secondBody = true)
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
        var modules = new TrRmModuleTableReader(moduleBytes, 1, names, 0, 0,
            secondMethod ? 2 : 1, 0);
        var typeDefs = new TrRmTypeDefTableReader([], 0, names, modules, 0, 0, 0, 0);
        var second = duplicateSignatureType ? typeCode : secondTypeCode;
        byte[] typeBytes = secondTypeOptional
            ? [0, 0, 0, 0, 3, 0, 0, 0, 9, 0, 0, 0, 1, 0, typeCode,
                6, 0, 1, 0, 0, 0]
            : second is byte secondCode
            ? [0, 0, 0, 0, 3, 0, 0, 0, 6, 0, 0, 0, 1, 0, typeCode, 1, 0, secondCode]
            : [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, typeCode];
        var types = new TrRmTypeSpecTableReader(typeBytes, second is null ? 1 : 2,
            0, 0, 0, 0);
        var signatureType = duplicateSignatureType || resultUsesSecondType ? 2u : 1u;
        var signatureBytes = new byte[24];
        Write(signatureBytes, 0, signatureType);
        Write(signatureBytes, 8, parameterCount == 0 ? 0u : 1u);
        Write(signatureBytes, 12, (uint)parameterCount);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1, types, parameterCount, 0);
        var parameterType = duplicateSignatureType || parameterUsesSecondType ? 2u : 1u;
        var paramBytes = new byte[parameterCount * 16];
        for (var index = 0; index < parameterCount; index++)
        {
            Write(paramBytes, index * 16, 1);
            Write(paramBytes, index * 16 + 4, (uint)index);
            Write(paramBytes, index * 16 + 8, parameterType);
            paramBytes[index * 16 + 12] = parameterMode;
        }

        var parameters = parameterCount == 0 ? default
            : new TrRmParamTableReader(paramBytes, parameterCount, signatures, types);
        var methodBytes = new byte[secondMethod ? 56 : 28];
        Write(methodBytes, 4, 2);
        Write(methodBytes, 8, 1);
        Write(methodBytes, 12, 2);
        Write(methodBytes, 24, 1);
        if (secondMethod)
        {
            methodBytes.AsSpan(0, 28).CopyTo(methodBytes.AsSpan(28));
            Write(methodBytes, 52, secondBody ? 2u : 0u);
        }

        var bodyCount = secondMethod && secondBody ? 2 : 1;
        var methods = new TrRmMethodDefTableReader(methodBytes,
            secondMethod ? 2 : 1, names, modules, typeDefs, signatures, 0, bodyCount);
        byte[] targetRow = parameterCount == 0 && typeCode == 0
            ? [0x04, 0, 0, 0x83, 0]
            : [0x04, 1, 1, 0, 0, 0, 0, 0x83, 1];
        var bodyBytes = secondMethod && secondBody
            ? TrRmVariableTableWriter.EncodeBodies([row, targetRow])
            : TrRmVariableTableWriter.EncodeBodies([row]);
        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var reader = new TrRmCompactLinearBodyReader(bodies, 1, modules, methods,
            signatures, parameters, types);
        var definitions = new int[reader.ValueCount];
        var lastUses = new int[reader.ValueCount];
        var useBlocks = new int[reader.ValueCount];
        var typeCodes = new byte[reader.ValueCount];
        var optional = new bool[reader.ValueCount];
        for (var index = 0; index < reader.ValueCount; index++)
        {
            definitions[index] = reader.GetDefinitionOffset(index + 1);
            lastUses[index] = reader.GetLastUseOrdinal(index + 1);
            useBlocks[index] = reader.GetUseBlockCount(index + 1);
            typeCodes[index] = reader.GetTypeCode(index + 1);
            optional[index] = reader.IsOptional(index + 1);
        }

        return (reader.OperationCount, reader.ValueCount, definitions, lastUses, useBlocks,
            typeCodes, optional);
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static byte[] OneParameterOperation(byte[] operation)
    {
        var row = new byte[7 + operation.Length + 2];
        byte[] header = [0x04, 1, 1, 0, 0, 0, 1];
        header.CopyTo(row, 0);
        operation.CopyTo(row, header.Length);
        row[^2] = 0x83;
        row[^1] = 2;
        return row;
    }

    private static byte[] TwoParameterOperation(byte[] operation, bool mapBoolean)
    {
        var row = new byte[(mapBoolean ? 11 : 7) + operation.Length + 2];
        row[0] = 0x04;
        row[1] = mapBoolean ? (byte)2 : (byte)1;
        row[2] = 1;
        if (mapBoolean)
        {
            row[6] = 2;
            row[10] = 1;
        }
        else
        {
            row[6] = 1;
        }

        operation.CopyTo(row, row.Length - operation.Length - 2);
        row[^2] = 0x83;
        row[^1] = 3;
        return row;
    }

    private static byte[] OneParameterMappedOperation(byte[] operation)
    {
        var row = new byte[11 + operation.Length + 2];
        byte[] header = [0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1];
        header.CopyTo(row, 0);
        operation.CopyTo(row, header.Length);
        row[^2] = 0x83;
        row[^1] = 2;
        return row;
    }
}
