// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmIndexedExecutionAuditTests
{
    private static byte[] SelectBody(bool chooseFirst) =>
    [
        8, 1, 4, 0, 2,
        1, 0, 0, 0, 2, 0, 0, 0,
        2, 1, 1, 1, 1, 1, 1, 1,
        0, 4,
        11, 15, 19, 23,
        0, 30,
        28,
        16, 4, 1, 3, 2, 3, 3, 3, 4, 4, 4,
        1, 1, 2, chooseFirst ? (byte)1 : (byte)0,
        1, 2, 1, 20,
        1, 3, 1, 42,
        7, 4, 1, 2, 3,
        0x83, 4,
    ];

    private static byte[] CheckedDivRemBody =>
    [
        8, 1, 13, 0, 2, 1, 0, 0, 0, 2, 0, 0, 0,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 1, 2, 1, 2, 1, 2, 1, 1, 1, 1, 1, 1, 1,
        0, 13,
        29, 33, 37, 37, 42, 46, 50, 55, 60, 64, 69, 73, 77,
        0, 84,
        82,
        16, 13,
        1, 2, 2, 2, 3, 5, 4, 6, 5, 5, 6, 6, 7, 8, 8, 8, 9, 8, 10, 11, 11, 11, 12, 11, 13, 12,
        12,
        1, 1, 1, 7,
        1, 2, 1, 3,
        0x18, 3, 4, 1, 2,
        1, 5, 1, 2,
        1, 6, 1, 1,
        5, 7, 4, 3, 5,
        5, 8, 4, 4, 6,
        1, 9, 2, 0,
        7, 10, 7, 8, 9,
        1, 11, 1, 42,
        1, 12, 1, 0,
        7, 13, 10, 11, 12,
        0x83, 13,
    ];

    private static byte[] WrappingBinaryBody =>
    [
        8, 1, 8, 0, 3, 1, 0, 0, 0, 2, 0, 0, 0, 4, 0, 0, 0,
        3, 1, 3, 1, 3, 1, 3, 1, 2, 1, 1, 1, 1, 1, 1, 1,
        0, 8,
        19, 28, 32, 37, 41, 46, 50, 54,
        0, 61,
        59,
        16, 8, 1, 2, 2, 2, 3, 4, 4, 4, 5, 7, 6, 7, 7, 7, 8, 8,
        8,
        0xF0, 0x20, 1, 1, 3, 0xFF, 0xFF, 0xFF, 0xFF,
        1, 2, 3, 1,
        5, 3, 10, 1, 2,
        1, 4, 3, 0,
        5, 5, 4, 3, 4,
        1, 6, 1, 42,
        1, 7, 1, 0,
        7, 8, 5, 6, 7,
        0x83, 8,
    ];

    private static byte[] SwitchBody(ushort selector)
    {
        var blocks = new[]
        {
            new List<byte> { 16, 1, 1, 1, 1 },
            new List<byte> { 16, 1, 2, 1, 1, 1, 2, 1, 20, 0x83, 2 },
            new List<byte> { 16, 1, 3, 1, 1, 1, 3, 1, 42, 0x83, 3 },
            new List<byte> { 16, 1, 4, 1, 1, 1, 4, 1, 99, 0x83, 4 },
        };
        var definitions = new List<byte> { 5, 5, 5, 5 };
        var terminators = new List<byte>();
        var offsets = new List<byte> { 0 };

        if (selector <= byte.MaxValue)
        {
            blocks[0].AddRange([1, 1, 1, (byte)selector]);
        }
        else
        {
            blocks[0].AddRange([0xF0, 0x10, 1, 1, 1,
                unchecked((byte)selector), unchecked((byte)(selector >> 8))]);
        }
        terminators.Add((byte)blocks[0].Count);
        blocks[0].AddRange([0xF0, 0x10, 0x82, 1, 2, 0, 0, 2, 0, 0, 1, 3, 0, 4, 0]);
        for (var index = 1; index < blocks.Length; index++)
        {
            terminators.Add(9);
        }
        var bodyData = new List<byte>();
        foreach (var block in blocks)
        {
            bodyData.AddRange(block);
            offsets.Add((byte)bodyData.Count);
        }

        var body = new List<byte> { 8, 4, 4, 0, 1, 1, 0, 0, 0 };
        for (var value = 0; value < 4; value++)
        {
            body.AddRange([1, 1]);
        }
        body.AddRange([0, 1, 2, 3, 4]);
        body.AddRange(definitions);
        body.AddRange(offsets);
        body.AddRange(terminators);
        body.AddRange(bodyData);
        return body.ToArray();
    }

    private static byte[] WideSwitchBody(ulong selector)
    {
        static void AddUInt64(List<byte> bytes, ulong value)
        {
            for (var index = 0; index < sizeof(ulong); index++)
            {
                bytes.Add(unchecked((byte)(value >> (index * 8))));
            }
        }

        var blocks = new[]
        {
            new List<byte> { 16, 1, 1, 1, 1 },
            new List<byte> { 16, 1, 2, 1, 1, 1, 2, 1, 20, 0x83, 2 },
            new List<byte> { 16, 1, 3, 1, 1, 1, 3, 1, 42, 0x83, 3 },
            new List<byte> { 16, 1, 4, 1, 1, 1, 4, 1, 99, 0x83, 4 },
        };
        blocks[0].AddRange([0xF0, 0x30, 1, 1, 2]);
        AddUInt64(blocks[0], selector);
        var terminators = new List<byte> { (byte)blocks[0].Count, 9, 9, 9 };
        blocks[0].AddRange([0xF0, 0x30, 0x82, 1, 2]);
        AddUInt64(blocks[0], 0x1122_3344_5566_7788);
        blocks[0].AddRange([2, 0]);
        AddUInt64(blocks[0], 0xFEDC_BA98_7654_3210);
        blocks[0].AddRange([3, 0, 4, 0]);

        var offsets = new List<byte> { 0 };
        var bodyData = new List<byte>();
        foreach (var block in blocks)
        {
            bodyData.AddRange(block);
            offsets.Add((byte)bodyData.Count);
        }

        var body = new List<byte> { 8, 4, 4, 0, 2, 1, 0, 0, 0, 6, 0, 0, 0 };
        body.AddRange([2, 1, 1, 1, 1, 1, 1, 1]);
        body.AddRange([0, 1, 2, 3, 4]);
        body.AddRange([5, 5, 5, 5]);
        body.AddRange(offsets);
        body.AddRange(terminators);
        body.AddRange(bodyData);
        return body.ToArray();
    }

    private static byte[] SmallScalarSwitchBody(byte selectorTypeRow, byte selector)
    {
        var firstPattern = selectorTypeRow == 2 ? (byte)0 : (byte)65;
        var secondPattern = selectorTypeRow == 2 ? (byte)1 : (byte)66;
        var blocks = new[]
        {
            new List<byte> { 16, 1, 1, 1, 1, 1, 1, 2, selector },
            new List<byte> { 16, 1, 2, 1, 1, 1, 2, 1, 20, 0x83, 2 },
            new List<byte> { 16, 1, 3, 1, 1, 1, 3, 1, 42, 0x83, 3 },
            new List<byte> { 16, 1, 4, 1, 1, 1, 4, 1, 99, 0x83, 4 },
        };
        blocks[0].AddRange([0x82, 1, 2, firstPattern, 2, 0, secondPattern, 3, 0, 4, 0]);
        var bodyData = new List<byte>();
        var offsets = new List<byte> { 0 };
        foreach (var block in blocks)
        {
            bodyData.AddRange(block);
            offsets.Add((byte)bodyData.Count);
        }

        var body = new List<byte> { 8, 4, 4, 0, 2, 1, 0, 0, 0, selectorTypeRow, 0, 0, 0 };
        body.AddRange([2, 1, 1, 1, 1, 1, 1, 1]);
        body.AddRange([0, 1, 2, 3, 4]);
        body.AddRange([5, 5, 5, 5]);
        body.AddRange(offsets);
        body.AddRange([9, 9, 9, 9]);
        body.AddRange(bodyData);
        return body.ToArray();
    }

    private static byte[] EmptySwitchBody =>
    [
        8, 2, 2, 0, 2,
        1, 0, 0, 0, 2, 0, 0, 0,
        2, 1, 1, 1,
        0, 1, 2,
        5, 5,
        0, 14, 25,
        9, 9,
        16, 1, 1, 1, 1, 1, 1, 2, 1,
        0x82, 1, 0, 2, 0,
        16, 1, 2, 1, 1, 1, 2, 1, 42, 0x83, 2,
    ];

    // Four blocks: compute flag/x/y, branch through two arms, merge with one int32 parameter.
    private static byte[] MergeBody(bool chooseFirst) =>
    [
        8, 4, 4, 0, 2, 1, 0, 0, 0, 2, 0, 0, 0,
        2, 1, 1, 1, 1, 1, 1, 1,
        0, 3, 3, 3, 4,
        8, 12, 16, 255,
        0, 26, 39, 52, 61,
        20, 9, 9, 7,
        0x18, 2, 2, 3, 1, 1, 3, 3,
        1, 1, 2, chooseFirst ? (byte)1 : (byte)0, 1, 2, 1, 20, 1, 3, 1, 42,
        0x81, 1, 2, 0, 3, 0,
        0x1C, 1, 2, 1, 2, 1, 2, 0, 0, 0x80, 4, 1, 2,
        0x1C, 1, 3, 1, 3, 1, 3, 0, 0, 0x80, 4, 1, 3,
        0x12, 1, 4, 1, 4, 0, 0, 0x83, 4,
    ];

    // Header parameters x/y/n receive (y,x,n+1) on the backedge. The x/y copies form a cycle.
    internal static byte[] LoopBody(byte iterations) =>
    [
        8, 4, 11, 1, 2, 1, 0, 0, 0, 2, 0, 0, 0,
        1, 0, 2, 4, 0,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 1, 1, 1, 2, 2, 1, 1, 1, 1, 1,
        0, 5, 9, 10, 11,
        15, 19, 23, 27, 31, 255, 255, 255, 25, 26, 255,
        0, 41, 78, 115, 124,
        35, 30, 31, 7,
        0x18, 5, 1, 2, 3, 4, 5, 3, 1, 5, 2, 5, 3, 5, 5,
        1, 1, 1, 1, 1, 2, 1, 2, 1, 3, 1, 0, 1, 4, 1, 1, 1, 5, 1, iterations,
        0x80, 2, 3, 1, 2, 3,
        0x1F, 1, 3, 6, 7, 8, 2, 4, 5, 5, 4, 5, 6, 7, 8,
        4, 5, 0, 6, 1, 8, 0, 9, 1, 1,
        5, 9, 6, 8, 5, 0x81, 9, 3, 0, 4, 1, 6,
        0x1D, 1, 5, 4, 5, 6, 7, 8, 5, 4, 5, 6, 7, 10,
        5, 4, 0, 6, 1, 7, 1, 8, 0, 10, 1, 1,
        0x15, 10, 1, 8, 4, 0x80, 2, 3, 7, 6, 10,
        0x12, 1, 11, 1, 11, 0, 0, 0x83, 11,
    ];
    [TestCase(true,20)]
    [TestCase(false,42)]
    public void HandwrittenMergeExecutesWithoutSourceOrProductionWriters(bool chooseFirst, int expected)
    {
        var artifact = TrRmExecutionAuditTests.Artifact(MergeBody(chooseFirst));
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase(true,20)]
    [TestCase(false,42)]
    public void HandwrittenSelectUsesAlreadyDefinedValues(bool chooseFirst, int expected)
    {
        var artifact = TrRmExecutionAuditTests.Artifact(SelectBody(chooseFirst));
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void HandwrittenCheckedDivRemDefinesTwoIndexedResultsTogether()
    {
        var artifact = TrRmExecutionAuditTests.Artifact(CheckedDivRemBody);
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void HandwrittenWrappingBinaryUsesModularIntegerArithmetic()
    {
        var artifact = TrRmExecutionAuditTests.Artifact(WrappingBinaryBody);
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase((ushort)0,20)]
    [TestCase((ushort)256,42)]
    [TestCase((ushort)257,99)]
    public void HandwrittenSwitchExecutesMatchingAndDefaultEdges(ushort selector, int expected)
    {
        var artifact = TrRmExecutionAuditTests.Artifact(SwitchBody(selector));
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase(0x1122334455667788UL,20)]
    [TestCase(0xFEDCBA9876543210UL,42)]
    [TestCase(0xFFFFFFFFFFFFFFFFUL,99)]
    public void HandwrittenWideSwitchComparesExact64BitPatterns(ulong selector, int expected)
    {
        var artifact = TrRmExecutionAuditTests.Artifact(WideSwitchBody(selector));
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase((byte)2,(byte)0,20)]
    [TestCase((byte)2,(byte)1,42)]
    [TestCase((byte)3,(byte)65,20)]
    [TestCase((byte)3,(byte)66,42)]
    [TestCase((byte)3,(byte)67,99)]
    public void HandwrittenBooleanAndChar16SwitchesCompareTheirExactValues(
        byte selectorTypeRow, byte selector, int expected)
    {
        var artifact = TrRmExecutionAuditTests.Artifact(
            SmallScalarSwitchBody(selectorTypeRow, selector));
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
    }

    [Test]
    public void HandwrittenSwitchWithoutCasesAlwaysTakesDefault()
    {
        var artifact = TrRmExecutionAuditTests.Artifact(
            EmptySwitchBody);
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        Assert.That(NativeJit.Execute(
            TrRmExecutableCompiler.Load(artifact), NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void NoThrowSwitchRemainsExecutable()
    {
        var artifact = TrRmExecutionAuditTests.Artifact(SwitchBody(256), effect: 1);
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase((byte)0,(byte)0,(byte)0,(byte)0)]
    [TestCase((byte)1,(byte)1,(byte)0,(byte)0)]
    public void IndexedSwitchRejectsDuplicateAndDescendingPatterns(
        byte firstLow, byte firstHigh, byte secondLow, byte secondHigh)
    {
        var body = SwitchBody(0);
        body[49] = firstLow;
        body[50] = firstHigh;
        body[53] = secondLow;
        body[54] = secondHigh;

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }

    [Test]
    public void IndexedSwitchRejectsNonminimalPatternDataWidth()
    {
        var body = SwitchBody(0);
        body[53] = 1;
        body[54] = 0;

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }

    [Test]
    public void IndexedOperationDefinitionOffsetCannotBeZero()
    {
        var body = SelectBody(true);
        body[23] = 0;

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }

    [Test]
    public void BooleanSwitchRejectsPatternsOutsideItsValueDomain()
    {
        var body = SmallScalarSwitchBody(selectorTypeRow: 2, selector: 0);
        body[54] = 2;

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }

    [Test]
    public void TruncatingAnIndexedSwitchTerminatorNeverLoads()
    {
        var body = SwitchBody(0);
        for (var length = 44; length < 59; length++)
        {
            var truncated = body[..length];
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(truncated)));
        }
    }

    [Test]
    public void IndexedNoThrowAuditRejectsCheckedDivRem()
    {
        var artifact = TrRmExecutionAuditTests.Artifact(CheckedDivRemBody, effect: 1);
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }
    [TestCase((byte)0,1)]
    [TestCase((byte)1,2)]
    [TestCase((byte)2,1)]
    [TestCase((byte)3,2)]
    public void HandwrittenLoopUsesSimultaneousBackedgeCopies(byte iterations, int expected)
    {
        var artifact = TrRmExecutionAuditTests.Artifact(LoopBody(iterations));
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact), NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
    }
    [TestCase(5,(byte)(8))]
    [TestCase(9,(byte)(1))]
    [TestCase(13,(byte)(1))]
    [TestCase(14,(byte)(2))]
    [TestCase(19,(byte)(2))]
    [TestCase(22,(byte)(2))]
    [TestCase(26,(byte)(9))]
    [TestCase(29,(byte)(7))]
    [TestCase(30,(byte)(1))]
    [TestCase(34,(byte)(60))]
    [TestCase(35,(byte)(21))]
    [TestCase(41,(byte)(4))]
    [TestCase(60,(byte)(2))]
    [TestCase(77,(byte)(3))]
    public void RejectsForgedMapsDefinitionsOffsetsUsesLivenessAndBranchTypes(int offset, byte value)
    {
        var body = MergeBody(true);
        body[offset] = value;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }
    [TestCase(13,(byte)(2))]
    [TestCase(14,(byte)(1))]
    [TestCase(15,(byte)(3))]
    [TestCase(16,(byte)(3))]
    [TestCase(17,(byte)(1))]
    public void RejectsInvalidLoopKindParentHeaderExitAndCleanup(int offset, byte value)
    {
        var body = LoopBody(3);
        body[offset] = value;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }

    [Test]
    public void NoThrowIsAuditedAcrossLoopBodies()
    {
        var artifact = TrRmExecutionAuditTests.Artifact(LoopBody(0), effect: 1);
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Test]
    public void VoidBranchDoesNotNeedAnInventedValueOrDefault()
    {
        byte[] body = [8, 2, 0, 0, 0, 0, 0, 0, 0, 5, 9, 2, 2,
            0, 0, 0x80, 2, 0, 0, 0, 0x83, 0];
        var artifact = TrRmExecutionAuditTests.Artifact(body, resultRow: 7, entry: false);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(program.Functions[0].Variables, Is.Empty);
    }

    [Test]
    public void TruncatingAnIndexedBodyNeverLoads()
    {
        var body = LoopBody(3);
        for (var length = 1; length < body.Length; length++)
        {
            var truncated = body[..length];
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(truncated)));
        }
    }

    [Test]
    public void ADefinitionOnOnlyOneArmCannotInitializeTheOtherArm()
    {
        byte[] body =
        [
            8, 3, 2, 0, 2, 1, 0, 0, 0, 2, 0, 0, 0,
            2, 1, 1, 2,
            0, 1, 2, 2, 10, 5,
            0, 20, 31, 40, 14, 9, 7,
            0x1C, 1, 2, 1, 2, 1, 1, 1, 1,
            1, 1, 2, 0, 0x81, 1, 2, 0, 3, 0,
            0x10, 1, 2, 1, 1, 1, 2, 1, 42, 0x83, 2,
            0x14, 1, 2, 1, 2, 0, 0, 0x83, 2,
        ];
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }
}
