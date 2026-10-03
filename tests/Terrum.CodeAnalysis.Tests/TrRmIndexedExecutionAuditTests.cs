// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Xunit;

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

    [Theory]
    [InlineData(true, 20)]
    [InlineData(false, 42)]
    public void HandwrittenMergeExecutesWithoutSourceOrProductionWriters(bool chooseFirst, int expected)
    {
        var artifact = TrRmExecutionAuditTests.Artifact(MergeBody(chooseFirst));
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(expected, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(true, 20)]
    [InlineData(false, 42)]
    public void HandwrittenSelectUsesAlreadyDefinedValues(bool chooseFirst, int expected)
    {
        var artifact = TrRmExecutionAuditTests.Artifact(SelectBody(chooseFirst));
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(expected, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData((byte)0, 1)]
    [InlineData((byte)1, 2)]
    [InlineData((byte)2, 1)]
    [InlineData((byte)3, 2)]
    public void HandwrittenLoopUsesSimultaneousBackedgeCopies(byte iterations, int expected)
    {
        var artifact = TrRmExecutionAuditTests.Artifact(LoopBody(iterations));
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        Assert.Equal(expected, NativeJit.Execute(TrRmExecutableCompiler.Load(artifact), NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(5, 8)]
    [InlineData(9, 1)]
    [InlineData(13, 1)]
    [InlineData(14, 2)]
    [InlineData(19, 2)]
    [InlineData(22, 2)]
    [InlineData(26, 9)]
    [InlineData(29, 7)]
    [InlineData(30, 1)]
    [InlineData(34, 60)]
    [InlineData(35, 21)]
    [InlineData(41, 4)]
    [InlineData(60, 2)]
    [InlineData(77, 3)]
    public void RejectsForgedMapsDefinitionsOffsetsUsesLivenessAndBranchTypes(int offset, byte value)
    {
        var body = MergeBody(true);
        body[offset] = value;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }

    [Theory]
    [InlineData(13, 2)]
    [InlineData(14, 1)]
    [InlineData(15, 3)]
    [InlineData(16, 3)]
    [InlineData(17, 1)]
    public void RejectsInvalidLoopKindParentHeaderExitAndCleanup(int offset, byte value)
    {
        var body = LoopBody(3);
        body[offset] = value;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }

    [Fact]
    public void NoThrowIsAuditedAcrossLoopBodies()
    {
        var artifact = TrRmExecutionAuditTests.Artifact(LoopBody(0), effect: 1);
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Fact]
    public void VoidBranchDoesNotNeedAnInventedValueOrDefault()
    {
        byte[] body = [8, 2, 0, 0, 0, 0, 0, 0, 0, 5, 9, 2, 2,
            0, 0, 0x80, 2, 0, 0, 0, 0x83, 0];
        var artifact = TrRmExecutionAuditTests.Artifact(body, resultRow: 7, entry: false);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Empty(program.Functions[0].Variables);
    }

    [Fact]
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

    [Fact]
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
