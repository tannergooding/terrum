// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmCompactLinearBodyReaderTests
{
    [Test]
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
        Assert.That(index.Operations, Is.EqualTo(3));
        Assert.That(index.ValueCount, Is.EqualTo(5));
        Assert.That(index.Definitions, Is.EqualTo([-1, -1, 7, 12, 16]));
        Assert.That(index.LastUses, Is.EqualTo([0, 0, 2, 2, 3]));
        Assert.That(index.UseBlocks, Is.EqualTo([1, 1, 1, 1, 1]));
    }
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1 },0)]
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 2, 1, 1, 0xFF, 0x83, 1 },0)]
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 0, 0x83, 1 },1)]
    public void ReadsConstantAndParameterReturns(byte[] row, int parameterCount)
    {
        var index = Inspect(row, parameterCount);
        Assert.That(index.ValueCount, Is.EqualTo(1));
        Assert.That(index.Definitions[0], Is.EqualTo(parameterCount == 0 ? 7 : -1));
        Assert.That(index.LastUses[0], Is.EqualTo(index.Operations));
        Assert.That(index.UseBlocks, Is.EqualTo([1]));
    }
    [TestCase(new byte[] { 0x05, 1, 0, 1, 0, 0, 0, 0, 0, 0x83, 1 })]
    [TestCase(new byte[] { 0x06, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0x83, 1 })]
    public void BodyLocalWidthDoesNotWidenInstructionOperands(byte[] row)
        => Assert.That(Inspect(row, 1).Definitions, Is.EqualTo([-1]));

    [Test]
    public void ReadsWideSignedConstantWithoutChangingBodyLocalWidth()
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x10, 2, 1, 1, 200, 0, 0x83, 1];
        var index = Inspect(row, 0);
        Assert.That(index.Definitions, Is.EqualTo([7]));
        Assert.That(index.LastUses, Is.EqualTo([1]));
    }
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 1,
        0xF0, 0x20, 1, 1, 1, 0, 0, 1, 0, 0x83, 1 },(byte)9)]
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 1,
        0xF0, 0x30, 1, 1, 1, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0x83, 1 },(byte)9)]
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 1,
        2, 1, 1, 0xFF, 0x83, 1 },(byte)9)]
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 1,
        0xF0, 0x30, 2, 1, 1, 0xFF, 0xFF, 0xFF, 0xFF,
        0, 0, 0, 0, 0x83, 1 },(byte)0x0D)]
    public void AcceptsFullWidthAndSignedNumericConstants(byte[] row, byte typeCode)
        => Assert.That(Inspect(row, 0, typeCode: typeCode).UseBlocks, Is.EqualTo([1]));
    [TestCase((byte)0x0D)]
    [TestCase((byte)0x0E)]
    public void RejectsNegativeSignedLiteralForUnsignedType(byte typeCode)
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 1, 2, 1, 1, 0xFF, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => Inspect(row, 0, typeCode: typeCode));
    }

    [Test]
    public void AcceptsValueFreeFailWithoutTypeMap()
    {
        byte[] row = [0x04, 0, 0, 0x87, 1];
        var index = Inspect(row, 0);
        Assert.That(index.ValueCount, Is.EqualTo(0));
        Assert.That(index.Operations, Is.EqualTo(0));
        Assert.That(index.Definitions, Is.Empty);
    }

    [Test]
    public void ReturnZeroRequiresVoidResultAndNoValueDefinition()
    {
        byte[] row = [0x04, 0, 0, 0x83, 0];
        var index = Inspect(row, 0, typeCode: 0);
        Assert.That(index.ValueCount, Is.EqualTo(0));
        Assert.That(index.Operations, Is.EqualTo(0));
        Assert.That(index.UseBlocks, Is.Empty);
        Assert.Throws<InvalidDataException>(() => Inspect(row, 0));

        byte[] invalid = [0x04, 0, 0, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => Inspect(invalid, 0, typeCode: 0));
    }

    [Test]
    public void OptionalPresenceRetainsCompleteValueTypes()
    {
        byte[] make =
        [
            0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1,
            8, 2, 2, 1, 0x83, 2,
        ];
        var wrapped = Inspect(make, 1, secondTypeCode: 1, secondTypeOptional: true,
            resultUsesSecondType: true, typeCode: 1);
        Assert.That(wrapped.Optional, Is.EqualTo([false, true]));
        Assert.That(wrapped.TypeCodes, Is.EqualTo([1, 1]));
        Assert.That(wrapped.LastUses, Is.EqualTo([0, 1]));

        byte[] none = [0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 9, 1, 2, 0x83, 1];
        Assert.That(Inspect(none, 0, typeCode: 1, secondTypeCode: 1,
            secondTypeOptional: true, resultUsesSecondType: true).Optional, Is.EqualTo([true]));

        byte[] present = [0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0x0A, 2, 1, 0x83, 2];
        var checkedPresence = Inspect(present, 1, typeCode: 1, secondTypeCode: 1,
            secondTypeOptional: true, parameterUsesSecondType: true);
        Assert.That(checkedPresence.Optional, Is.EqualTo([true, false]));
        Assert.That(checkedPresence.LastUses, Is.EqualTo([0, 1]));
    }

    [Test]
    public void OptionalCopyAndSelectKeepPresenceQualified()
    {
        byte[] copied = [0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 3, 2, 1, 0x83, 2];
        var copy = Inspect(copied, 1, typeCode: 8, secondTypeCode: 8,
            secondTypeOptional: true, parameterUsesSecondType: true, resultUsesSecondType: true);
        Assert.That(copy.Optional, Is.EqualTo([true, true]));
        Assert.That(copy.TypeCodes, Is.EqualTo([8, 8]));

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
        Assert.That(choice.Optional, Is.EqualTo([false, true, true, true]));
        Assert.That(choice.LastUses, Is.EqualTo([3, 3, 3, 4]));
    }

    [Test]
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
    [TestCase(new byte[] { 3, 2, 1 },(byte)8)]
    [TestCase(new byte[] { 4, 2, 1, 1 },(byte)1)]
    [TestCase(new byte[] { 4, 2, 2, 1 },(byte)8)]
    [TestCase(new byte[] { 4, 2, 3, 1 },(byte)0x0D)]
    [TestCase(new byte[] { 0x17, 2, 1 },(byte)8)]
    [TestCase(new byte[] { 0x17, 2, 1 },(byte)9)]
    public void ScalarCopyUnaryAndNegateRetainType(byte[] operation, byte typeCode)
    {
        var row = OneParameterOperation(operation);
        var index = Inspect(row, 1, typeCode: typeCode);
        Assert.That(index.TypeCodes, Is.EqualTo([typeCode, typeCode]));
        Assert.That(index.UseBlocks, Is.EqualTo([1, 1]));
        Assert.That(index.LastUses, Is.EqualTo([0, 1]));
    }
    [TestCase(new byte[] { 4, 2, 1, 1 },(byte)8)]
    [TestCase(new byte[] { 4, 2, 2, 1 },(byte)1)]
    [TestCase(new byte[] { 4, 2, 0, 1 },(byte)8)]
    [TestCase(new byte[] { 0x17, 2, 1 },(byte)0x0D)]
    [TestCase(new byte[] { 3, 2, 2 },(byte)8)]
    public void RejectsMismatchedUnaryAndForwardCopy(byte[] operation, byte typeCode)
        => Assert.Throws<InvalidDataException>(() =>
            Inspect(OneParameterOperation(operation), 1, typeCode: typeCode));
    [TestCase(new byte[] { 5, 3, 1, 1, 2 },(byte)8)]
    [TestCase(new byte[] { 5, 3, 4, 1, 2 },(byte)8)]
    [TestCase(new byte[] { 5, 3, 6, 1, 2 },(byte)8)]
    [TestCase(new byte[] { 5, 3, 10, 1, 2 },(byte)8)]
    [TestCase(new byte[] { 5, 3, 4, 1, 2 },(byte)1)]
    [TestCase(new byte[] { 5, 3, 5, 1, 2 },(byte)3)]
    public void BinaryOperationChecksItsScalarResult(byte[] operation, byte sourceType)
    {
        var returnsBool = operation[2] is >= 4 and <= 9;
        var differentResult = returnsBool && sourceType != 1;
        var row = TwoParameterOperation(operation, differentResult);
        var index = Inspect(row, 2, typeCode: sourceType,
            secondTypeCode: differentResult ? (byte)1 : null,
            resultUsesSecondType: differentResult);
        Assert.That(index.TypeCodes[2], Is.EqualTo(returnsBool ? (byte)1 : sourceType));
        Assert.That(index.LastUses, Is.EqualTo([0, 0, 1]));
    }
    [TestCase(new byte[] { 5, 3, 0, 1, 2 },(byte)8)]
    [TestCase(new byte[] { 5, 3, 1, 1, 2 },(byte)1)]
    [TestCase(new byte[] { 5, 3, 6, 1, 2 },(byte)3)]
    [TestCase(new byte[] { 0x15, 3, 6, 1, 2 },(byte)8)]
    public void RejectsInvalidBinaryKindOrOperandType(byte[] operation, byte typeCode)
        => Assert.Throws<InvalidDataException>(() =>
            Inspect(TwoParameterOperation(operation, false), 2, typeCode: typeCode));

    [Test]
    public void ComparisonMustMapItsImplicitBooleanResult()
    {
        byte[] operation = [5, 3, 4, 1, 2];
        Assert.Throws<InvalidDataException>(() =>
            Inspect(TwoParameterOperation(operation, false), 2));
    }

    [Test]
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
        Assert.That(index.TypeCodes, Is.EqualTo([1, 8, 8, 8]));
        Assert.That(index.LastUses, Is.EqualTo([3, 3, 3, 4]));

        row[25] = 2;
        Assert.Throws<InvalidDataException>(() => Inspect(row, 0, secondTypeCode: 1));
    }

    [Test]
    public void CheckedShiftRequiresUnsigned32Count()
    {
        byte[] row =
        [
            0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 2,
            1, 2, 2, 1,
            0x15, 3, 6, 1, 2,
            0x83, 3,
        ];
        Assert.That(Inspect(row, 1, secondTypeCode: 0x0D).TypeCodes, Is.EqualTo([8, 0x0D, 8]));
        Assert.Throws<InvalidDataException>(() => Inspect(row, 1, secondTypeCode: 1));
    }
    [TestCase(new byte[] { 6, 2, 2, 1, 1 },(byte)8,(byte)0x0D)]
    [TestCase(new byte[] { 6, 2, 2, 2, 1 },(byte)9,(byte)0x0D)]
    [TestCase(new byte[] { 6, 2, 2, 3, 1 },(byte)8,(byte)0x0D)]
    [TestCase(new byte[] { 0x16, 2, 2, 1 },(byte)3,(byte)0x0D)]
    [TestCase(new byte[] { 0x16, 2, 2, 1 },(byte)8,(byte)3)]
    public void NumericConversionsRetainSelectedPolicyAndTargetType(
        byte[] operation, byte sourceType, byte destinationType)
    {
        var row = OneParameterMappedOperation(operation);
        var index = Inspect(row, 1, typeCode: sourceType,
            secondTypeCode: destinationType, resultUsesSecondType: true);
        Assert.That(index.TypeCodes, Is.EqualTo([sourceType, destinationType]));
        Assert.That(index.LastUses, Is.EqualTo([0, 1]));
    }

    [Test]
    public void CheckedDivRemDefinesTwoValuesAtOneOperation()
    {
        byte[] row =
        [
            0x04, 1, 1, 0, 0, 0, 1,
            0x18, 3, 4, 1, 2,
            0x83, 4,
        ];
        var index = Inspect(row, 2);
        Assert.That(index.ValueCount, Is.EqualTo(4));
        Assert.That(index.Definitions, Is.EqualTo([-1, -1, 7, 7]));
        Assert.That(index.LastUses, Is.EqualTo([0, 0, 0, 1]));
        Assert.That(index.UseBlocks, Is.EqualTo([1, 1, 0, 1]));
        Assert.That(index.TypeCodes, Is.EqualTo([8, 8, 8, 8]));

        row[9] = 5;
        Assert.Throws<InvalidDataException>(() => Inspect(row, 2));
    }

    [Test]
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
        Assert.That(index.ValueCount, Is.EqualTo(6));
        Assert.That(index.Definitions, Is.EqualTo([-1, -1, 7, 7, 12, 12]));
        Assert.That(index.LastUses, Is.EqualTo([0, 0, 1, 1, 0, 2]));
    }
    [TestCase(new byte[] { 6, 2, 2, 0, 1 },(byte)8,(byte)0x0D)]
    [TestCase(new byte[] { 6, 2, 2, 4, 1 },(byte)8,(byte)0x0D)]
    [TestCase(new byte[] { 6, 2, 2, 3, 1 },(byte)8,(byte)0x0E)]
    [TestCase(new byte[] { 6, 2, 2, 1, 1 },(byte)3,(byte)0x0D)]
    [TestCase(new byte[] { 0x16, 2, 2, 1 },(byte)1,(byte)8)]
    [TestCase(new byte[] { 0x16, 2, 3, 1 },(byte)8,(byte)0x0D)]
    public void RejectsInvalidConversionPolicyTypesAndTypeId(
        byte[] operation, byte sourceType, byte destinationType)
        => Assert.Throws<InvalidDataException>(() =>
            Inspect(OneParameterMappedOperation(operation), 1, typeCode: sourceType,
                secondTypeCode: destinationType, resultUsesSecondType: true));

    [Test]
    public void UnusedParameterHasNoUseBlock()
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 1, 1, 2, 1, 42, 0x83, 2];
        var index = Inspect(row, 1);
        Assert.That(index.UseBlocks, Is.EqualTo([0, 1]));
        Assert.That(index.LastUses, Is.EqualTo([0, 1]));
    }

    [Test]
    public void MatchesMethodSignatureByResolvedTypeRatherThanTypeSpecRow()
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 0, 0x83, 1];
        Assert.That(Inspect(row, 1, duplicateSignatureType: true).UseBlocks, Is.EqualTo([1]));
    }
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1, 0 },0)]
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 1, 2, 1, 42, 0x83, 1 },0)]
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x04, 1, 1, 1, 42, 0x83, 1 },0)]
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x00, 1, 1, 1, 42, 0x83, 1 },0)]
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 0x15, 1, 1, 1, 2, 0x83, 1 },0)]
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 1, 3, 1, 0x83, 1 },0)]
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 0, 0x83, 2 },1)]
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 0, 0x81, 1 },1)]
    [TestCase(new byte[] { 0x04, 1, 1, 0, 0, 0, 0, 0x87, 5 },0)]
    public void RejectsMalformedOrUnsupportedBodies(byte[] row, int parameterCount)
        => Assert.Throws<InvalidDataException>(() => Inspect(row, parameterCount));

    [Test]
    public void RejectsCheckedExceptionPolicyAndNonValueParameters()
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 0, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => Inspect(row, 1, runtimePolicy: 1));
        Assert.Throws<InvalidDataException>(() => Inspect(row, 1, parameterMode: 2));
    }

    [Test]
    public void RejectsUnknownTypeMapReference()
    {
        byte[] row = [0x04, 1, 2, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => Inspect(row, 0));
    }

    [Test]
    public void RejectsDuplicateResolvedTypesInTypeMap()
    {
        byte[] row = [0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => Inspect(row, 0, secondTypeCode: 8));
    }
    [TestCase(new byte[] { 0x12, 2, 2, 1, 1 })]
    [TestCase(new byte[] { 0xF0, 0x04, 0x12, 2, 2, 1, 0, 1 })]
    [TestCase(new byte[] { 0xF0, 0x05, 0x12, 2, 0, 2, 0, 1, 0, 1, 0 })]
    public void InspectsLocalStaticScalarCall(byte[] operation)
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 1, .. operation, 0x83, 2];
        var index = Inspect(row, 1, secondMethod: true);
        Assert.That(index.ValueCount, Is.EqualTo(2));
        Assert.That(index.Definitions, Is.EqualTo([-1, 7]));
        Assert.That(index.LastUses, Is.EqualTo([0, 1]));
        Assert.That(index.UseBlocks, Is.EqualTo([1, 1]));
    }

    [Test]
    public void InspectsVoidLocalCallWithoutValueDefinition()
    {
        byte[] row = [0x04, 0, 1, 0x12, 0, 2, 0, 0x83, 0];
        var index = Inspect(row, 0, typeCode: 0, secondMethod: true);
        Assert.That(index.Operations, Is.EqualTo(1));
        Assert.That(index.ValueCount, Is.EqualTo(0));
        Assert.That(index.Definitions, Is.Empty);
    }
    [TestCase(new byte[] { 0x12, 2, 0, 1, 1 })]
    [TestCase(new byte[] { 0x12, 2, 3, 1, 1 })]
    [TestCase(new byte[] { 0x12, 2, 2, 0, 1 })]
    [TestCase(new byte[] { 0x12, 2, 2, 2, 1 })]
    [TestCase(new byte[] { 0x12, 0, 2, 1, 1 })]
    [TestCase(new byte[] { 0x12, 2, 2, 1, 2 })]
    [TestCase(new byte[] { 0xF0, 0x0C, 0x12, 2, 2, 1, 1 })]
    public void RejectsMalformedLocalCalls(byte[] operation)
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 1, .. operation, 0x83, 2];
        Assert.Throws<InvalidDataException>(() => Inspect(row, 1, secondMethod: true));
    }

    [Test]
    public void RejectsLocalCallToMethodWithoutBodyOrMatchingParameterMode()
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 1, 0x12, 2, 2, 1, 1, 0x83, 2];
        Assert.Throws<InvalidDataException>(() => Inspect(row, 1,
            secondMethod: true, secondBody: false));
        Assert.Throws<InvalidDataException>(() => Inspect(row, 1,
            secondMethod: true, parameterMode: 2));
    }
    [TestCase((byte)(1),(byte)(0),(byte)(0),(byte)(2),true)]
    [TestCase((byte)(0),(byte)(0),(byte)(0),(byte)(2),true)]
    [TestCase((byte)(0),(byte)(1),(byte)(0),(byte)(2),false)]
    [TestCase((byte)(1),(byte)(0),(byte)(1),(byte)(2),false)]
    [TestCase((byte)(1),(byte)(0),(byte)(0),(byte)(0),false)]
    public void ChecksPackedPublicGlobalCall(byte callerRequirement,
        byte targetRequirement, byte targetPolicy, byte targetVisibility, bool valid)
    {
        byte[] row =
        [
            0x04, 1, 1, 0, 0, 0, 1,
            0x12, 2, 0, 2, 0, 0, 0x16, 1, 1, 0x83, 2,
        ];
        if (valid)
        {
            var index = Inspect(row, 1, secondMethod: true, packedTarget: true,
                callerRequirement: callerRequirement, targetRequirement: targetRequirement,
                targetPolicy: targetPolicy, targetVisibility: targetVisibility);
            Assert.That(index.ValueCount, Is.EqualTo(2));
            Assert.That(index.LastUses, Is.EqualTo([0, 1]));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => Inspect(row, 1,
                secondMethod: true, packedTarget: true,
                callerRequirement: callerRequirement, targetRequirement: targetRequirement,
                targetPolicy: targetPolicy, targetVisibility: targetVisibility));
        }
    }
    [TestCase((byte)(0),(byte)(0x15))]
    [TestCase((byte)(0),(byte)(0x17))]
    [TestCase((byte)(1),(byte)(0x16))]
    [TestCase((byte)(3),(byte)(0x16))]
    public void RejectsMalformedPackedCallTarget(byte targetRow, byte kind)
    {
        byte[] row =
        [
            0x04, 1, 1, 0, 0, 0, 1,
            0x12, 2, 0, targetRow, 0, 0, kind, 1, 1, 0x83, 2,
        ];
        Assert.Throws<InvalidDataException>(() => Inspect(row, 1,
            secondMethod: true, packedTarget: true));
    }
    [TestCase((byte)(1),(byte)(0),(byte)(0),(byte)(2),true)]
    [TestCase((byte)(0),(byte)(0),(byte)(0),(byte)(2),true)]
    [TestCase((byte)(0),(byte)(1),(byte)(0),(byte)(2),false)]
    [TestCase((byte)(1),(byte)(0),(byte)(1),(byte)(2),false)]
    [TestCase((byte)(1),(byte)(0),(byte)(0),(byte)(0),false)]
    public void ChecksRetainedImportedGlobalCall(byte callerRequirement,
        byte targetRequirement, byte targetPolicy, byte targetVisibility, bool valid)
    {
        byte[] row = [0x04, 1, 1, 0, 0, 0, 1, 0x12, 2, 2, 1, 1, 0x83, 2];
        if (valid)
        {
            var index = Inspect(row, 1, secondMethod: true, secondBody: false,
                expectedTarget: true, callerRequirement: callerRequirement,
                targetRequirement: targetRequirement, targetPolicy: targetPolicy,
                targetVisibility: targetVisibility);
            Assert.That(index.ValueCount, Is.EqualTo(2));
            Assert.That(index.LastUses, Is.EqualTo([0, 1]));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => Inspect(row, 1,
                secondMethod: true, secondBody: false, expectedTarget: true,
                callerRequirement: callerRequirement, targetRequirement: targetRequirement,
                targetPolicy: targetPolicy, targetVisibility: targetVisibility));
        }
    }
    [TestCase(false,false,(byte)(2),(byte)(6),false,true)]
    [TestCase(true,false,(byte)(2),(byte)(6),false,true)]
    [TestCase(false,true,(byte)(2),(byte)(6),false,true)]
    [TestCase(false,false,(byte)(0),(byte)(6),false,true)]
    [TestCase(true,false,(byte)(0),(byte)(6),false,false)]
    [TestCase(false,true,(byte)(0),(byte)(6),false,false)]
    [TestCase(true,false,(byte)(2),(byte)(4),false,false)]
    [TestCase(false,true,(byte)(2),(byte)(4),false,false)]
    [TestCase(true,false,(byte)(2),(byte)(6),true,false)]
    [TestCase(false,true,(byte)(2),(byte)(6),true,false)]
    public void ChecksTypeOwnedStaticCall(bool packedTarget, bool expectedTarget,
        byte targetVisibility, byte ownerFlags, bool genericOwner, bool valid)
    {
        byte[] row = packedTarget
            ? [0x04, 1, 1, 0, 0, 0, 1,
                0x12, 2, 0, 2, 0, 0, 0x16, 1, 1, 0x83, 2]
            : [0x04, 1, 1, 0, 0, 0, 1, 0x12, 2, 2, 1, 1, 0x83, 2];
        if (valid)
        {
            var index = Inspect(row, 1, secondMethod: true, packedTarget: packedTarget,
                secondBody: !expectedTarget, expectedTarget: expectedTarget,
                targetVisibility: targetVisibility, ownerFlags: ownerFlags,
                genericOwner: genericOwner);
            Assert.That(index.ValueCount, Is.EqualTo(2));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => Inspect(row, 1,
                secondMethod: true, packedTarget: packedTarget,
                secondBody: !expectedTarget, expectedTarget: expectedTarget,
                targetVisibility: targetVisibility, ownerFlags: ownerFlags,
                genericOwner: genericOwner));
        }
    }
    [TestCase(true,(byte)(1),(byte)(0),(byte)(0),true)]
    [TestCase(true,(byte)(0),(byte)(1),(byte)(0),false)]
    [TestCase(true,(byte)(1),(byte)(0),(byte)(1),false)]
    [TestCase(false,(byte)(1),(byte)(0),(byte)(0),true)]
    [TestCase(false,(byte)(0),(byte)(1),(byte)(0),false)]
    [TestCase(false,(byte)(1),(byte)(0),(byte)(1),false)]
    public void ChecksTypeOwnedExternalCallRequirements(bool packedTarget,
        byte callerRequirement, byte targetRequirement, byte targetPolicy, bool valid)
    {
        byte[] row = packedTarget
            ? [0x04, 1, 1, 0, 0, 0, 1,
                0x12, 2, 0, 2, 0, 0, 0x16, 1, 1, 0x83, 2]
            : [0x04, 1, 1, 0, 0, 0, 1, 0x12, 2, 2, 1, 1, 0x83, 2];
        if (valid)
        {
            Assert.That(Inspect(row, 1, secondMethod: true,
                packedTarget: packedTarget, expectedTarget: !packedTarget,
                secondBody: packedTarget, ownerFlags: 6,
                callerRequirement: callerRequirement, targetRequirement: targetRequirement,
                targetPolicy: targetPolicy).ValueCount, Is.EqualTo(2));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => Inspect(row, 1,
                secondMethod: true, packedTarget: packedTarget, expectedTarget: !packedTarget,
                secondBody: packedTarget, ownerFlags: 6,
                callerRequirement: callerRequirement, targetRequirement: targetRequirement,
                targetPolicy: targetPolicy));
        }
    }

    internal static (int Operations, int ValueCount, int[] Definitions, int[] LastUses,
        int[] UseBlocks, byte[] TypeCodes, bool[] Optional) Inspect(
        byte[] row, int parameterCount, byte runtimePolicy = 0, byte parameterMode = 0,
        bool duplicateSignatureType = false, byte typeCode = 8,
        byte? secondTypeCode = null, bool resultUsesSecondType = false,
        bool secondTypeOptional = false, bool parameterUsesSecondType = false,
        bool secondMethod = false, bool secondBody = true, bool packedTarget = false,
        byte callerRequirement = 1, byte targetRequirement = 0, byte targetPolicy = 0,
        byte targetVisibility = 2, bool expectedTarget = false,
        byte? ownerFlags = null, bool genericOwner = false)
    {
        var names = new TrRmStringTableReader(
            TrRmStringTableWriter.EncodeTextRows([("Module", 0), ("Run", 0), ("", 0)]), 3);
        var moduleBytes = new byte[packedTarget ? 88 : 44];
        Write(moduleBytes, 0, 1);
        Write(moduleBytes, 4, callerRequirement);
        Write(moduleBytes, 8, runtimePolicy);
        Write(moduleBytes, 28, 1);
        Write(moduleBytes, 32, 1);
        Write(moduleBytes, 36, 1);
        Write(moduleBytes, 40, 1);
        if (packedTarget)
        {
            foreach (var offset in new[] { 48, 72, 76, 84 })
            {
                Write(moduleBytes, offset, 1);
            }

            Write(moduleBytes, 44, 2);
            Write(moduleBytes, 48, targetRequirement);
            Write(moduleBytes, 52, targetPolicy);
            Write(moduleBytes, 80, 2);
        }

        var modules = new TrRmModuleTableReader(moduleBytes, packedTarget ? 2 : 1,
            names, expectedTarget ? 1 : 0, ownerFlags is null ? 0 : 1,
            secondMethod ? 2 : 1, 0);
        byte[] reference = new byte[12];
        Write(reference, 0, 2);
        Write(reference, 4, targetRequirement);
        Write(reference, 8, targetPolicy);
        var references = new TrRmModuleRefTableReader(expectedTarget ? reference : [],
            expectedTarget ? 1 : 0, names, modules);
        var ownerBytes = ownerFlags is null ? [] : new byte[40];
        if (ownerFlags is not null)
        {
            Write(ownerBytes, 0, 3);
            Write(ownerBytes, 4, 2);
            Write(ownerBytes, 8, ownerFlags.Value);
            if (genericOwner)
            {
                Write(ownerBytes, 20, 1);
                Write(ownerBytes, 24, 1);
            }
        }

        var typeDefs = new TrRmTypeDefTableReader(ownerBytes,
            ownerFlags is null ? 0 : 1, names, modules, 0, genericOwner ? 1 : 0, 0, 0);
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
            if (ownerFlags is not null)
            {
                Write(methodBytes, 28, 1);
            }

            Write(methodBytes, 40, targetVisibility);
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
        byte[] import = new byte[12];
        Write(import, 0, 1);
        Write(import, 4, 2);
        Write(import, 8, 0x1600_0002);
        var imports = new TrRmImportTableReader(expectedTarget ? import : [],
            expectedTarget ? 1 : 0, names, references, typeDefs, methods, default);
        var reader = new TrRmCompactLinearBodyReader(bodies, 1, modules, methods,
            signatures, parameters, types, typeDefs, imports);
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

    internal static int GetOperationCount(byte[] row, int parameterCount, byte typeCode = 8,
        byte? secondTypeCode = null, bool resultUsesSecondType = false,
        bool secondMethod = false, bool packedTarget = false, bool expectedTarget = false)
        => Inspect(row, parameterCount, typeCode: typeCode, secondTypeCode: secondTypeCode,
            resultUsesSecondType: resultUsesSecondType, secondMethod: secondMethod,
            packedTarget: packedTarget, expectedTarget: expectedTarget).Operations;

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
