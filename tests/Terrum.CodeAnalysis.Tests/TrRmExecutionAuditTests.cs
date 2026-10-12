// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmExecutionAuditTests
{
    private static readonly byte[] s_return42 = [4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1];

    [Theory]
    [InlineData(true, 20)]
    [InlineData(false, 42)]
    public void IndependentEagerSelectUsesAlreadyDefinedValues(bool chooseFirst, int expected)
    {
        byte[] body =
        [
            4, 2, 1, 0, 0, 0, 2, 0, 0, 0, 4,
            1, 1, 2, chooseFirst ? (byte)1 : (byte)0,
            1, 2, 1, 20,
            1, 3, 1, 42,
            7, 4, 1, 2, 3,
            0x83, 4,
        ];
        var artifact = Artifact(body);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(expected, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(PrimitiveType.Int32, 0xFFFF_FFF9UL, 3UL, 0xFFFF_FFFEUL, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt32, 0xFFFF_FFFFUL, 3UL, 0x5555_5555UL, 0UL)]
    [InlineData(PrimitiveType.Int64, 0xFFFF_FFFF_FFFF_FFF9UL, 3UL,
        0xFFFF_FFFF_FFFF_FFFEUL, 0xFFFF_FFFF_FFFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt64, 0xFFFF_FFFF_FFFF_FFFFUL, 10UL,
        0x1999_9999_9999_9999UL, 5UL)]
    public void IndependentCheckedDivRemDefinesAndExecutesBothResults(PrimitiveType type,
        ulong dividend, ulong divisor, ulong quotient, ulong remainder)
    {
        var artifact = Artifact(CheckedDivRemBody(type, dividend, divisor, quotient, remainder));
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(42, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(PrimitiveType.Int32, 7UL, 0UL, unchecked((int)0xE0540002))]
    [InlineData(PrimitiveType.Int32, 0x8000_0000UL, 0xFFFF_FFFFUL, unchecked((int)0xE0540001))]
    [InlineData(PrimitiveType.Int64, 0x8000_0000_0000_0000UL, 0xFFFF_FFFF_FFFF_FFFFUL,
        unchecked((int)0xE0540001))]
    public async Task IndependentCheckedDivRemPreservesFailureStatuses(PrimitiveType type,
        ulong dividend, ulong divisor, int expectedStatus)
    {
        var artifact = Artifact(CheckedDivRemBody(type, dividend, divisor, 0, 0));
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(expectedStatus,
            await StaticProtocolTests.RunAsync(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast))
                .ConfigureAwait(true));
    }

    [Theory]
    [InlineData(PrimitiveType.Int32, 10, 0x7FFF_FFFFUL, 1UL, 0x8000_0000UL)]
    [InlineData(PrimitiveType.UInt32, 10, 0xFFFF_FFFFUL, 1UL, 0UL)]
    [InlineData(PrimitiveType.Int64, 10, 0x7FFF_FFFF_FFFF_FFFFUL, 1UL, 0x8000_0000_0000_0000UL)]
    [InlineData(PrimitiveType.UInt64, 10, 0xFFFF_FFFF_FFFF_FFFFUL, 1UL, 0UL)]
    [InlineData(PrimitiveType.Int32, 11, 0x8000_0000UL, 1UL, 0x7FFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt32, 11, 0UL, 1UL, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.Int64, 11, 0x8000_0000_0000_0000UL, 1UL, 0x7FFF_FFFF_FFFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt64, 11, 0UL, 1UL, 0xFFFF_FFFF_FFFF_FFFFUL)]
    [InlineData(PrimitiveType.Int32, 12, 0x4000_0000UL, 4UL, 0UL)]
    [InlineData(PrimitiveType.UInt32, 12, 0x8000_0000UL, 2UL, 0UL)]
    [InlineData(PrimitiveType.Int64, 12, 0x4000_0000_0000_0000UL, 4UL, 0UL)]
    [InlineData(PrimitiveType.UInt64, 12, 0x8000_0000_0000_0000UL, 2UL, 0UL)]
    public void IndependentWrappingBinaryExecutesAtTheOperandWidth(PrimitiveType type,
        byte kind, ulong left, ulong right, ulong expected)
    {
        var artifact = Artifact(WrappingBinaryBody(type, kind, left, right, expected));
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        Assert.Equal(42, NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(PrimitiveType.Int32, PrimitiveType.UInt64, 1, 0xFFFF_FFFFUL, 0xFFFF_FFFF_FFFF_FFFFUL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.Int32, 1, 0xFFFF_FFFF_7FFF_FFFFUL, 0x7FFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt64, PrimitiveType.UInt32, 1, 0x1_0000_0001UL, 1UL)]
    [InlineData(PrimitiveType.UInt32, PrimitiveType.Int32, 1, 0xFFFF_FFFFUL, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.UInt32, 1, 0xFFFF_FFFFUL, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt32, PrimitiveType.Int64, 2, 0xFFFF_FFFFUL, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.Int32, PrimitiveType.Int64, 2, 0xFFFF_FFFFUL, 0xFFFF_FFFF_FFFF_FFFFUL)]
    [InlineData(PrimitiveType.Int32, PrimitiveType.UInt64, 2, 0xFFFF_FFFFUL, 0UL)]
    [InlineData(PrimitiveType.Int32, PrimitiveType.UInt32, 2, 0xFFFF_FFFFUL, 0UL)]
    [InlineData(PrimitiveType.UInt32, PrimitiveType.Int32, 2, 0xFFFF_FFFFUL, 0x7FFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt32, PrimitiveType.Int32, 2, 0x7FFF_FFFFUL, 0x7FFF_FFFFUL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.Int32, 2, 0x8000_0000_0000_0000UL, 0x8000_0000UL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.Int32, 2, 0xFFFF_FFFF_7FFF_FFFFUL, 0x8000_0000UL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.Int32, 2, 0x0000_0000_8000_0000UL, 0x7FFF_FFFFUL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.UInt32, 2, 0xFFFF_FFFF_FFFF_FFFFUL, 0UL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.UInt32, 2, 0xFFFF_FFFFUL, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.UInt32, 2, 0x0000_0001_0000_0000UL, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.UInt64, 2, 1UL, 1UL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.UInt64, 2, 0xFFFF_FFFF_FFFF_FFFFUL, 0UL)]
    [InlineData(PrimitiveType.UInt64, PrimitiveType.Int64, 2, 0x7FFF_FFFF_FFFF_FFFFUL, 0x7FFF_FFFF_FFFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt64, PrimitiveType.Int64, 2, 0xFFFF_FFFF_FFFF_FFFFUL, 0x7FFF_FFFF_FFFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt64, PrimitiveType.Int32, 2, 0x0000_0000_8000_0000UL, 0x7FFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt64, PrimitiveType.Int32, 2, 0x7FFF_FFFFUL, 0x7FFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt64, PrimitiveType.UInt32, 2, 0x0000_0001_0000_0000UL, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt64, PrimitiveType.UInt32, 2, 0xFFFF_FFFFUL, 0xFFFF_FFFFUL)]
    public void IndependentIntegerConvertExecutesWrapAndSaturation(PrimitiveType sourceType,
        PrimitiveType destinationType, byte policy, ulong sourceBits, ulong expectedBits)
    {
        var artifact = Artifact(IntegerConvertBody(sourceType, destinationType, policy,
            sourceBits, expectedBits));
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(1, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(false, 0xD800u, 55296)]
    [InlineData(false, 0xFFFFu, 65535)]
    [InlineData(true, 0u, 0)]
    [InlineData(true, 0xD800u, 55296)]
    [InlineData(true, 0xFFFFu, 65535)]
    public void IndependentCheckedConvertExecutesChar16Boundaries(
        bool narrowing, uint sourceBits, int expected)
    {
        var artifact = Artifact(CheckedChar16ConvertBody(narrowing, sourceBits));
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(expected, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(PrimitiveType.Int32, PrimitiveType.Int64, 0x8000_0000UL, 0xFFFF_FFFF_8000_0000UL)]
    [InlineData(PrimitiveType.Int32, PrimitiveType.UInt64, 42UL, 42UL)]
    [InlineData(PrimitiveType.UInt32, PrimitiveType.Int64, 0xFFFF_FFFFUL, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt32, PrimitiveType.UInt64, 0xFFFF_FFFFUL, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt32, PrimitiveType.Int32, 0x7FFF_FFFFUL, 0x7FFF_FFFFUL)]
    [InlineData(PrimitiveType.Int32, PrimitiveType.UInt32, 0x7FFF_FFFFUL, 0x7FFF_FFFFUL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.Int32, 0xFFFF_FFFF_8000_0000UL, 0x8000_0000UL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.UInt32, 0xFFFF_FFFFUL, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt64, PrimitiveType.Int32, 0x7FFF_FFFFUL, 0x7FFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt64, PrimitiveType.UInt32, 0xFFFF_FFFFUL, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt64, PrimitiveType.Int64, 0x7FFF_FFFF_FFFF_FFFFUL, 0x7FFF_FFFF_FFFF_FFFFUL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.UInt64, 0x7FFF_FFFF_FFFF_FFFFUL, 0x7FFF_FFFF_FFFF_FFFFUL)]
    public void IndependentCheckedConvertExecutesIntegerBoundaries(PrimitiveType sourceType,
        PrimitiveType destinationType, ulong sourceBits, ulong expectedBits)
    {
        var artifact = Artifact(CheckedIntegerConvertBody(sourceType,
            destinationType, sourceBits, expectedBits));
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(1, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(0xFFFF_FFFFu)]
    [InlineData(0x1_0000u)]
    public async Task IndependentCheckedConvertRejectsOutOfRangeChar16(uint sourceBits)
    {
        var artifact = Artifact(CheckedChar16ConvertBody(narrowing: true, sourceBits: sourceBits));
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(unchecked((int)0xE0540001),
            await StaticProtocolTests.RunAsync(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast))
                .ConfigureAwait(true));
    }

    [Theory]
    [InlineData(PrimitiveType.Int32, PrimitiveType.UInt64, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.UInt32, PrimitiveType.Int32, 0x8000_0000UL)]
    [InlineData(PrimitiveType.Int32, PrimitiveType.UInt32, 0xFFFF_FFFFUL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.Int32, 0xFFFF_FFFF_7FFF_FFFFUL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.Int32, 0x0000_0000_8000_0000UL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.UInt32, 0xFFFF_FFFF_FFFF_FFFFUL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.UInt32, 0x0000_0001_0000_0000UL)]
    [InlineData(PrimitiveType.UInt64, PrimitiveType.Int32, 0x0000_0000_8000_0000UL)]
    [InlineData(PrimitiveType.UInt64, PrimitiveType.UInt32, 0x0000_0001_0000_0000UL)]
    [InlineData(PrimitiveType.UInt64, PrimitiveType.Int64, 0x8000_0000_0000_0000UL)]
    [InlineData(PrimitiveType.Int64, PrimitiveType.UInt64, 0xFFFF_FFFF_FFFF_FFFFUL)]
    public async Task IndependentCheckedConvertRejectsOutOfRangeIntegers(
        PrimitiveType sourceType, PrimitiveType destinationType, ulong sourceBits)
    {
        var artifact = Artifact(CheckedIntegerConvertBody(sourceType,
            destinationType, sourceBits, expectedBits: 0));
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(unchecked((int)0xE0540001),
            await StaticProtocolTests.RunAsync(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast))
                .ConfigureAwait(true));
    }

    [Fact]
    public void IndependentBytesExecuteWithoutCompilerOrTableWriter()
    {
        var bytes = Artifact(s_return42);
        var program = TrRmExecutableCompiler.Load(bytes);
        Assert.Equal(42, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
        Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(program, NativeRuntimePolicy.CheckedExceptions));
    }

    [Theory]
    [InlineData(new byte[] { 4, 1, 1, 0, 0, 0, 1, 2, 1, 1, 0xFF, 0x83, 1 }, -1)]
    [InlineData(new byte[] { 4, 1, 1, 0, 0, 0, 1, 0xF0, 0x10, 2, 1, 1, 200, 0, 0x83, 1 }, 200)]
    [InlineData(new byte[] { 4, 1, 1, 0, 0, 0, 2, 1, 1, 1, 42, 3, 2, 1, 0x83, 2 }, 42)]
    public void IndependentSignedWidthsAndCopyAreTyped(byte[] body, int expected)
    {
        var program = TrRmExecutableCompiler.Load(Artifact(body));
        Assert.Equal(expected, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void IndependentUnsignedSignedLiteralRetainsFullBitPattern()
    {
        byte[] body = [4, 1, 4, 0, 0, 0, 1, 0xF0, 0x30, 2, 1, 1,
            0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 0x83, 1];
        var program = TrRmExecutableCompiler.Load(Artifact(body, resultRow: 4, entry: false));
        var constant = Assert.IsType<BoundConstant>(
            Assert.IsType<BoundLocal>(program.Functions[0].Body.Statements[0]).Initializer);
        Assert.Equal(PrimitiveType.UInt32, constant.Type);
        Assert.Equal(-1, constant.Value);
    }

    [Fact]
    public void IndependentCheckedSumPreservesOneDefinitionPerEvaluation()
    {
        byte[] body = [4, 1, 1, 0, 0, 0, 3,
            1, 1, 1, 20, 1, 2, 1, 1, 0x15, 3, 1, 1, 2, 0x83, 3];
        var program = TrRmExecutableCompiler.Load(Artifact(body));
        Assert.Equal(21, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        Assert.Equal(3, program.Functions[0].Variables.Length);
        Assert.Equal(4, program.Functions[0].Body.Statements.Length);
        var sum = Assert.IsType<BoundBinary>(
            Assert.IsType<BoundLocal>(program.Functions[0].Body.Statements[2]).Initializer);
        Assert.IsType<BoundRead>(sum.Left);
        Assert.IsType<BoundRead>(sum.Right);
    }

    [Theory]
    [InlineData(1, 4, 2u)]
    [InlineData(1, 8, 1u)]
    [InlineData(1, 12, 2u)]
    [InlineData(1, 16, 1u)]
    [InlineData(0x16, 8, 0x11000001u)]
    [InlineData(0x16, 12, 3u)]
    [InlineData(0x16, 24, 0u)]
    [InlineData(0x17, 0, 2u)]
    [InlineData(0x17, 4, 1u)]
    public void RejectsIndependentMetadataTampering(int kind, int offset, uint value)
    {
        var artifact = Artifact(s_return42);
        WriteWord(artifact, SectionOffset(artifact, kind) + offset, value);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void RejectsEveryReservedContractFlagBit(int bit)
    {
        var artifact = Artifact(s_return42, effect: 1);
        var data = SectionOffset(artifact, 0x1B) + 8;
        BinaryPrimitives.WriteUInt16LittleEndian(artifact.AsSpan(data + 6), (ushort)(1 << bit));
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Theory]
    [InlineData(8)]
    public void UnsupportedEffectsNeverDisappear(uint effect)
        => Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(Artifact(s_return42, effect: effect)));

    [Fact]
    public void DoesNotReturnEffectRejectsAReachableNormalReturn()
        => Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(Artifact(s_return42, effect: 2)));

    [Fact]
    public void NoThrowAuditRejectsEvenConstantSafeCheckedOperations()
    {
        byte[] body = [4, 1, 1, 0, 0, 0, 3,
            1, 1, 1, 20, 1, 2, 1, 22, 0x15, 3, 1, 1, 2, 0x83, 3];
        var artifact = Artifact(body, effect: 1);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Fact]
    public void NoThrowAuditRejectsCheckedDivRem()
    {
        var artifact = Artifact(CheckedDivRemBody(PrimitiveType.Int32, 7, 3, 2, 1), effect: 1);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Fact]
    public void NoThrowAllowsTotalWrappingBinary()
    {
        var artifact = Artifact(WrappingBinaryBody(PrimitiveType.Int32, 10,
            0x7FFF_FFFF, 1, 0x8000_0000), effect: 1);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(42, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void CheckedDivRemResultsMustBeConsecutiveAndDistinct()
    {
        var body = CheckedDivRemBody(PrimitiveType.Int32, 7, 3, 2, 1);
        var operation = Array.IndexOf(body, (byte)0x18);
        body[operation + 2] = 3;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(Artifact(body)));
    }

    [Fact]
    public void ContractOwnerMustBeTheExactMethodDef()
    {
        var artifact = Artifact(s_return42, effect: 1);
        WriteWord(artifact, SectionOffset(artifact, 0x1B) + 8, 0x13000001);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Fact]
    public void SelfCallIsAuditedWithoutExecutingUnboundedRecursion()
    {
        byte[] body = [4, 1, 1, 0, 0, 0, 1, 0x12, 1, 1, 0, 0x83, 1];
        var artifact = Artifact(body);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.Empty(loaded.Functions[0].Body.Statements);
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(loaded, "source-free"));
    }

    [Fact]
    public void HandwrittenForwardLinearCallUsesModuleOrdinalAndParameterSignature()
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 2,
            1, 1, 1, 41, 0x12, 2, 2, 1, 1, 0x83, 2];
        byte[] callee = [4, 1, 1, 0, 0, 0, 0, 0x83, 1];
        var artifact = LocalCallArtifact(caller, callee);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        Assert.Equal(41, NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void CallerUnsafeEffectDoesNotAuthorizeAnExecutableCall()
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 1, 0x12, 2, 2, 0, 0x83, 2];
        byte[] callee = [4, 1, 1, 0, 0, 0, 0, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            LocalCallArtifact(caller, callee, calleeCallerUnsafe: true)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void HandwrittenCallRejectsForeignOrNonexistentOrdinal(byte ordinal)
    {
        byte[] caller = ordinal == 0
            ? [4, 1, 1, 0, 0, 0, 2,
                1, 1, 1, 41, 0x12, 2, 0, 2, 0, 0, 0x16, 1, 1, 0x83, 2]
            : [4, 1, 1, 0, 0, 0, 2,
                1, 1, 1, 41, 0x12, 2, ordinal, 1, 1, 0x83, 2];
        byte[] callee = [4, 1, 1, 0, 0, 0, 0, 0x83, 1];
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(LocalCallArtifact(caller, callee)));
    }

    [Fact]
    public void HandwrittenIndexedForwardCallExecutesAndTracksUses()
    {
        byte[] caller =
        [
            8, 1, 2, 0, 1, 1, 0, 0, 0,
            1, 1, 1, 1, 0, 2, 7, 11, 0, 18, 16,
            0x10, 2, 1, 1, 2, 2, 2,
            1, 1, 1, 41, 0x12, 2, 2, 1, 1, 0x83, 2,
        ];
        byte[] callee = [4, 1, 1, 0, 0, 0, 0, 0x83, 1];
        var artifact = LocalCallArtifact(caller, callee);
        TrRmStructuralMetadataValidator.ValidateCompact(artifact);
        Assert.Equal(41, NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void HandwrittenIndexedVoidCallHasNoDefinitionOrValueSlot()
    {
        byte[] caller =
        [
            8, 1, 1, 0, 1, 1, 0, 0, 0,
            1, 1, 0, 1, 5, 0, 16, 14,
            0x10, 1, 1, 2, 2,
            1, 1, 1, 41, 0x12, 0, 2, 1, 1, 0x83, 1,
        ];
        byte[] callee = [4, 1, 1, 0, 0, 0, 0, 0x83, 0];
        var artifact = LocalCallArtifact(caller, callee, resultRow: 7);
        TrRmStructuralMetadataValidator.ValidateCompact(artifact);
        Assert.Equal(41, NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void HandwrittenLinearVoidCallBetweenDefinitionsKeepsValueNumbering()
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 4,
            1, 1, 1, 41, 0x12, 0, 2, 1, 1,
            3, 2, 1, 0x12, 0, 2, 1, 2, 0x83, 2];
        byte[] callee = [4, 1, 1, 0, 0, 0, 0, 0x83, 0];
        Assert.Equal(41, NativeJit.Execute(
            TrRmExecutableCompiler.Load(LocalCallArtifact(caller, callee, resultRow: 7)),
            NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(2, 0, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 1, 0)]
    public void HandwrittenCallRejectsResultCountAndModeMismatch(byte result, byte argumentCount, byte mode)
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 2,
            1, 1, 1, 41, 0x12, result, 2, argumentCount, 1, 0x83, 2];
        byte[] callee = [4, 1, 1, 0, 0, 0, 0, 0x83, 1];
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(LocalCallArtifact(caller, callee, parameterMode: mode)));
    }

    [Fact]
    public void NoThrowCallRequiresCalleeDeclarationAndCannotBeForgedAfterLoad()
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 2,
            1, 1, 1, 41, 0x12, 2, 2, 1, 1, 0x83, 2];
        byte[] callee = [4, 1, 1, 0, 0, 0, 0, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            LocalCallArtifact(caller, callee, callerNoThrow: true)));
        var program = TrRmExecutableCompiler.Load(
            LocalCallArtifact(caller, callee, callerNoThrow: true, calleeNoThrow: true));
        Assert.Equal(41, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        var changedEffect = program with
        {
            Functions = program.Functions.SetItem(1, program.Functions[1] with { IsNoThrow = false }),
        };
        Assert.Throws<ArgumentException>(() =>
            NativeEmitter.Emit(changedEffect, NativeRuntimePolicy.FailFast));
        var changedType = program with
        {
            Functions = program.Functions.SetItem(1, program.Functions[1] with
            {
                Variables = program.Functions[1].Variables.SetItem(0,
                    program.Functions[1].Variables[0] with { Type = PrimitiveType.UInt32 }),
            }),
        };
        Assert.Throws<ArgumentException>(() =>
            NativeEmitter.Emit(changedType, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void UncalledCalleeStillRequiresACompleteAuditedBody()
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1];
        byte[] unsupported = [4, 1, 1, 0, 0, 0, 1, 0x19, 2, 1, 0x83, 2];
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(LocalCallArtifact(caller, unsupported)));
    }

    [Fact]
    public void CallArgumentTypeIsAuditedAgainstParameterMetadata()
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 2,
            1, 1, 1, 41, 0x12, 2, 2, 1, 1, 0x83, 2];
        byte[] callee = [4, 2, 1, 0, 0, 0, 4, 0, 0, 0,
            1, 1, 2, 1, 42, 0x83, 2];
        var independentlyValidTarget = LocalCallArtifact(s_return42, callee);
        WriteWord(independentlyValidTarget, SectionOffset(independentlyValidTarget, 0x18) + 8, 4);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(independentlyValidTarget);
        var artifact = LocalCallArtifact(caller, callee);
        WriteWord(artifact, SectionOffset(artifact, 0x18) + 8, 4);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    // Independent metadata and Body bytes; no production compiler, table writer or source parser.
    internal static byte[] LocalCallArtifact(byte[] caller, byte[] callee,
        int resultRow = 1, byte parameterMode = 0, bool callerNoThrow = false,
        bool calleeNoThrow = false, bool calleeCallerUnsafe = false)
    {
        byte[] codes = [8, 1, 3, 0x0D, 9, 0x0E, 0];
        var strings = new[] { "independent", "Main", "Callee" }.Select(text =>
        {
            var utf8 = Encoding.UTF8.GetBytes(text);
            var row = new byte[(4 + utf8.Length + 1 + 3) & ~3];
            WriteWord(row, 0, ((uint)utf8.Length << 8) | 1);
            utf8.CopyTo(row, 4);
            return row;
        });
        var calleeEffectMask = (calleeNoThrow ? 1u : 0u) | (calleeCallerUnsafe ? 4u : 0u);
        var hasCalleeEffect = calleeEffectMask != 0;
        var contractCount = (callerNoThrow ? 1 : 0) + (hasCalleeEffect ? 1 : 0);
        var calleeContractFirst = hasCalleeEffect ? callerNoThrow ? 2u : 1u : 0;
        var sections = new List<(ushort Kind, uint Count, byte[] Bytes)>
        {
            (1, 1, Words(1, 1, 0, 1, 0, 0, 0, 1, 1, 1, 1)),
            (2, 3, Rows(strings)),
            (0x11, 7, Rows(codes.Select(code => new byte[] { 1, 0, code }))),
            (0x16, 2, Words(
                0, 2, 1, 1, callerNoThrow ? 1u : 0, callerNoThrow ? 1u : 0, 1,
                0, 3, 2, 1, calleeContractFirst,
                hasCalleeEffect ? 1u : 0, 2)),
            (0x17, 2, Words(1, 0, 0, 0, 0, 0,
                (uint)resultRow, 0, 1, 1, 0, 0)),
            (0x18, 1, Words(2, 0, 1, parameterMode)),
        };
        if (contractCount != 0)
        {
            var clauses = new List<byte[]>();
            if (callerNoThrow)
            {
                clauses.Add(Words(0x16000001, 1, 1));
            }
            if (hasCalleeEffect)
            {
                clauses.Add(Words(0x16000002, 1, calleeEffectMask));
            }
            sections.Add((0x1B, (uint)contractCount, Rows(clauses)));
        }
        sections.Add((0x20, 2, Rows([caller, callee])));
        return Assemble(sections);
    }

    private static byte[] IntegerConvertBody(PrimitiveType sourceType,
        PrimitiveType destinationType, byte policy, ulong sourceBits, ulong expectedBits)
    {
        var typeRows = new List<int>();
        int TypeId(PrimitiveType type)
        {
            var row = type switch
            {
                PrimitiveType.Int32 => 1,
                PrimitiveType.Bool => 2,
                PrimitiveType.UInt32 => 4,
                PrimitiveType.Int64 => 5,
                PrimitiveType.UInt64 => 6,
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
            var existing = typeRows.IndexOf(row);
            if (existing >= 0)
            {
                return existing + 1;
            }
            typeRows.Add(row);
            return typeRows.Count;
        }

        var sourceTypeId = TypeId(sourceType);
        var destinationTypeId = TypeId(destinationType);
        _ = TypeId(PrimitiveType.Bool);
        var int32TypeId = TypeId(PrimitiveType.Int32);
        var body = new List<byte> { 4, (byte)typeRows.Count };
        foreach (var row in typeRows)
        {
            body.AddRange(Words((uint)row));
        }
        body.Add(7);
        AddConstant(1, sourceTypeId, sourceBits);
        body.AddRange([6, 2, (byte)destinationTypeId, policy, 1]);
        AddConstant(3, destinationTypeId, expectedBits);
        body.AddRange([5, 4, 4, 2, 3]);
        AddConstant(5, int32TypeId, 1);
        AddConstant(6, int32TypeId, 0);
        body.AddRange([7, 7, 4, 5, 6, 0x83, 7]);
        return body.ToArray();

        void AddConstant(int result, int typeId, ulong bits)
        {
            var dataWidth = bits <= byte.MaxValue ? 1
                : bits <= ushort.MaxValue ? 2
                : bits <= uint.MaxValue ? 4 : 8;
            if (dataWidth == 1)
            {
                body.AddRange([1, (byte)result, (byte)typeId, (byte)bits]);
            }
            else
            {
                body.AddRange([0xF0, (byte)(dataWidth switch { 2 => 0x10, 4 => 0x20, _ => 0x30 }),
                    1, (byte)result, (byte)typeId]);
                for (var index = 0; index < dataWidth; index++)
                {
                    body.Add(unchecked((byte)(bits >> (index * 8))));
                }
            }
        }
    }

    private static byte[] CheckedIntegerConvertBody(PrimitiveType sourceType,
        PrimitiveType destinationType, ulong sourceBits, ulong expectedBits)
    {
        var typeRows = new List<int>();
        int TypeId(PrimitiveType type)
        {
            var row = type switch
            {
                PrimitiveType.Int32 => 1,
                PrimitiveType.Bool => 2,
                PrimitiveType.UInt32 => 4,
                PrimitiveType.Int64 => 5,
                PrimitiveType.UInt64 => 6,
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
            var existing = typeRows.IndexOf(row);
            if (existing >= 0)
            {
                return existing + 1;
            }
            typeRows.Add(row);
            return typeRows.Count;
        }

        var sourceTypeId = TypeId(sourceType);
        var destinationTypeId = TypeId(destinationType);
        _ = TypeId(PrimitiveType.Bool);
        var int32TypeId = TypeId(PrimitiveType.Int32);
        var body = new List<byte> { 4, (byte)typeRows.Count };
        foreach (var row in typeRows)
        {
            body.AddRange(Words((uint)row));
        }
        body.Add(7);
        AddConstant(1, sourceTypeId, sourceBits);
        body.AddRange([0x16, 2, (byte)destinationTypeId, 1]);
        AddConstant(3, destinationTypeId, expectedBits);
        body.AddRange([5, 4, 4, 2, 3]);
        AddConstant(5, int32TypeId, 1);
        AddConstant(6, int32TypeId, 0);
        body.AddRange([7, 7, 4, 5, 6, 0x83, 7]);
        return body.ToArray();

        void AddConstant(int result, int typeId, ulong bits)
        {
            var dataWidth = bits <= byte.MaxValue ? 1
                : bits <= ushort.MaxValue ? 2
                : bits <= uint.MaxValue ? 4 : 8;
            if (dataWidth == 1)
            {
                body.AddRange([1, (byte)result, (byte)typeId, (byte)bits]);
            }
            else
            {
                body.AddRange([0xF0, (byte)(dataWidth switch { 2 => 0x10, 4 => 0x20, _ => 0x30 }),
                    1, (byte)result, (byte)typeId]);
                for (var index = 0; index < dataWidth; index++)
                {
                    body.Add(unchecked((byte)(bits >> (index * 8))));
                }
            }
        }
    }

    private static byte[] CheckedChar16ConvertBody(bool narrowing, uint sourceBits)
    {
        var sourceTypeId = narrowing ? 1 : 2;
        var destinationTypeId = narrowing ? 2 : 1;
        var body = new List<byte> { 4, 2 };
        body.AddRange(Words(1, 3));
        body.Add(narrowing ? (byte)3 : (byte)2);
        var dataWidth = sourceBits <= byte.MaxValue ? 1
            : sourceBits <= ushort.MaxValue ? 2 : 4;
        if (dataWidth == 1)
        {
            body.AddRange([1, 1, (byte)sourceTypeId, (byte)sourceBits]);
        }
        else
        {
            body.AddRange([0xF0, (byte)(dataWidth == 2 ? 0x10 : 0x20),
                1, 1, (byte)sourceTypeId]);
            for (var index = 0; index < dataWidth; index++)
            {
                body.Add(unchecked((byte)(sourceBits >> (index * 8))));
            }
        }
        if (narrowing)
        {
            body.AddRange([0x16, 2, (byte)destinationTypeId, 1,
                0x16, 3, 1, 2, 0x83, 3]);
        }
        else
        {
            body.AddRange([0x16, 2, (byte)destinationTypeId, 1, 0x83, 2]);
        }
        return body.ToArray();
    }

    private static byte[] CheckedDivRemBody(PrimitiveType type,
        ulong dividend, ulong divisor, ulong quotient, ulong remainder)
    {
        var typeRows = new List<int>();
        int TypeId(PrimitiveType value)
        {
            var row = value switch
            {
                PrimitiveType.Int32 => 1,
                PrimitiveType.Bool => 2,
                PrimitiveType.UInt32 => 4,
                PrimitiveType.Int64 => 5,
                PrimitiveType.UInt64 => 6,
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            };
            var existing = typeRows.IndexOf(row);
            if (existing >= 0)
            {
                return existing + 1;
            }
            typeRows.Add(row);
            return typeRows.Count;
        }

        var valueType = TypeId(type);
        var boolType = TypeId(PrimitiveType.Bool);
        var int32Type = TypeId(PrimitiveType.Int32);
        var body = new List<byte> { 4, (byte)typeRows.Count };
        foreach (var row in typeRows)
        {
            body.AddRange(Words((uint)row));
        }
        body.Add(12);
        AddConstant(1, valueType, dividend);
        AddConstant(2, valueType, divisor);
        body.AddRange([0x18, 3, 4, 1, 2]);
        AddConstant(5, valueType, quotient);
        AddConstant(6, valueType, remainder);
        body.AddRange([5, 7, 4, 3, 5, 5, 8, 4, 4, 6]);
        AddConstant(9, boolType, 0);
        body.AddRange([7, 10, 7, 8, 9]);
        AddConstant(11, int32Type, 42);
        AddConstant(12, int32Type, 0);
        body.AddRange([7, 13, 10, 11, 12, 0x83, 13]);
        return body.ToArray();

        void AddConstant(int result, int typeId, ulong bits)
        {
            var dataWidth = bits <= byte.MaxValue ? 1
                : bits <= ushort.MaxValue ? 2
                : bits <= uint.MaxValue ? 4 : 8;
            if (dataWidth == 1)
            {
                body.AddRange([1, (byte)result, (byte)typeId, (byte)bits]);
            }
            else
            {
                body.AddRange([0xF0, (byte)(dataWidth switch { 2 => 0x10, 4 => 0x20, _ => 0x30 }),
                    1, (byte)result, (byte)typeId]);
                for (var index = 0; index < dataWidth; index++)
                {
                    body.Add(unchecked((byte)(bits >> (index * 8))));
                }
            }
        }
    }

    private static byte[] WrappingBinaryBody(PrimitiveType type,
        byte kind, ulong left, ulong right, ulong expected)
    {
        var typeRows = new List<int>();
        int TypeId(PrimitiveType value)
        {
            var row = value switch
            {
                PrimitiveType.Int32 => 1,
                PrimitiveType.Bool => 2,
                PrimitiveType.UInt32 => 4,
                PrimitiveType.Int64 => 5,
                PrimitiveType.UInt64 => 6,
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            };
            var existing = typeRows.IndexOf(row);
            if (existing >= 0)
            {
                return existing + 1;
            }
            typeRows.Add(row);
            return typeRows.Count;
        }

        var valueType = TypeId(type);
        var boolType = TypeId(PrimitiveType.Bool);
        var int32Type = TypeId(PrimitiveType.Int32);
        var body = new List<byte> { 4, (byte)typeRows.Count };
        foreach (var row in typeRows)
        {
            body.AddRange(Words((uint)row));
        }
        body.Add(8);
        AddConstant(1, valueType, left);
        AddConstant(2, valueType, right);
        body.AddRange([5, 3, kind, 1, 2]);
        AddConstant(4, valueType, expected);
        body.AddRange([5, 5, 4, 3, 4]);
        AddConstant(6, int32Type, 42);
        AddConstant(7, int32Type, 0);
        body.AddRange([7, 8, 5, 6, 7, 0x83, 8]);
        return body.ToArray();

        void AddConstant(int result, int typeId, ulong bits)
        {
            var dataWidth = bits <= byte.MaxValue ? 1
                : bits <= ushort.MaxValue ? 2
                : bits <= uint.MaxValue ? 4 : 8;
            if (dataWidth == 1)
            {
                body.AddRange([1, (byte)result, (byte)typeId, (byte)bits]);
            }
            else
            {
                body.AddRange([0xF0, (byte)(dataWidth switch { 2 => 0x10, 4 => 0x20, _ => 0x30 }),
                    1, (byte)result, (byte)typeId]);
                for (var index = 0; index < dataWidth; index++)
                {
                    body.Add(unchecked((byte)(bits >> (index * 8))));
                }
            }
        }
    }

    [Fact]
    public void PublicVisibilityWithoutAnExportCannotPublishNativeSymbols()
    {
        var artifact = Artifact(s_return42, entry: false, exported: true);
        Assert.NotEmpty(NativeEmitter.EmitLibrary(TrRmExecutableCompiler.Load(artifact),
            ["Main"], NativeRuntimePolicy.FailFast));
        artifact = Artifact(s_return42, entry: false);
        WriteWord(artifact, SectionOffset(artifact, 0x16) + 12, 2);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Fact]
    public void AuditChecksNonentryMethodsAsWellAsTheEntry()
    {
        byte[] invalid = [4, 1, 4, 0, 0, 0, 3,
            1, 1, 1, 20, 1, 2, 1, 22, 0x15, 3, 1, 1, 2, 0x83, 3];
        var original = TrRmExecutableCompiler.Load(Artifact(s_return42));
        var source = original with
        {
            Functions = original.Functions.Add(original.Functions[0] with { Name = "Other", IsNoThrow = true }),
        };
        var bytes = TrRmExecutableCompiler.Emit(source, "two-bodies");
        var index = new TrRmArtifactIndex(bytes);
        Assert.True(index.TryGetSection(0x20, out var bodies, out var bodyCount));
        var firstBody = new TrRmBodyTableReader(bodies, bodyCount).GetRow(1).ToArray();
        var sections = new List<TrRmSection>();
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags, out var payload, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)count,
                kind == 0x20 ? Rows([firstBody, invalid]) : payload.ToArray()));
        }
        var tampered = TrRmArtifactWriter.Encode(sections);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(tampered);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(tampered));
    }

    [Theory]
    [InlineData(new byte[] { 4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 2 })]
    [InlineData(new byte[] { 4, 1, 1, 0, 0, 0, 1, 3, 1, 1, 0x83, 1 })]
    [InlineData(new byte[] { 4, 1, 1, 0, 0, 0, 1, 1, 2, 1, 42, 0x83, 2 })]
    [InlineData(new byte[] { 4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1, 0 })]
    [InlineData(new byte[] { 4, 1, 1, 0, 0, 0, 1, 0xF0, 0x10, 1, 1, 1, 42, 0, 0x83, 1 })]
    [InlineData(new byte[] { 4, 1, 1, 0, 0, 0, 1, 0xF0, 0x01, 1, 1, 0, 1, 0, 42, 0x83, 1 })]
    [InlineData(new byte[] { 4, 1, 1, 0, 0, 0, 1, 0x19, 1, 1, 42, 0x83, 1 })]
    public void RejectsIndependentInvalidBodyAndNoncanonicalWidths(byte[] body)
        => Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(Artifact(body)));

    [Fact]
    public void RejectsEveryTruncatedArtifactPrefix()
    {
        var artifact = Artifact(s_return42);
        for (var length = 0; length < artifact.Length; length++)
        {
            var prefix = artifact[..length];
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(prefix));
        }
    }

    // This fixture deliberately does not use any production metadata, Body or artifact writer.
    internal static byte[] Artifact(byte[] body, int resultRow = 1, bool entry = true,
        uint? effect = null, bool exported = false)
    {
        byte[] codes = [8, 1, 3, 0x0D, 9, 0x0E, 0];
        var strings = new List<byte[]>();
        foreach (var text in new[] { "independent", "Main" })
        {
            var utf8 = Encoding.UTF8.GetBytes(text);
            var row = new byte[(4 + utf8.Length + 1 + 3) & ~3];
            WriteWord(row, 0, ((uint)utf8.Length << 8) | 1);
            utf8.CopyTo(row, 4);
            strings.Add(row);
        }
        var sections = new List<(ushort Kind, uint Count, byte[] Bytes)>
        {
            (1, 1, Words(1, 1, 0, entry ? 1u : 0u, 0, 0, 0, 1, 1, 1, 1)),
            (2, 2, Rows(strings)),
            (0x11, 7, Rows(codes.Select(code => new byte[] { 1, 0, code }))),
            (0x16, 1, Words(0, 2, 1, exported ? 2u : 1u, effect.HasValue ? 1u : 0u,
                effect.HasValue ? 1u : 0u, 1)),
            (0x17, 1, Words((uint)resultRow, 0, 0, 0, 0, 0)),
        };
        if (effect.HasValue)
        {
            sections.Add((0x1B, 1, Rows([Words(0x16000001, 1, effect.Value)])));
        }
        if (exported)
        {
            sections.Add((0x1D, 1, Words(0x16000001, 2)));
        }
        sections.Add((0x20, 1, Rows([body])));
        return Assemble(sections);
    }

    private static byte[] Assemble(List<(ushort Kind, uint Count, byte[] Bytes)> sections)
    {
        var offset = 32 + sections.Count * 16;
        var artifact = new byte[offset + sections.Sum(section => section.Bytes.Length)];
        "TRRM"u8.CopyTo(artifact);
        WriteWord(artifact, 4, 1);
        WriteWord(artifact, 8, 32);
        WriteWord(artifact, 12, (uint)sections.Count);
        WriteWord(artifact, 16, (uint)artifact.Length);
        WriteWord(artifact, 20, 32);
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            WriteWord(artifact, 32 + index * 16, section.Kind);
            WriteWord(artifact, 36 + index * 16, section.Count);
            WriteWord(artifact, 40 + index * 16, (uint)offset);
            WriteWord(artifact, 44 + index * 16, (uint)section.Bytes.Length);
            section.Bytes.CopyTo(artifact, offset);
            offset += section.Bytes.Length;
        }
        return artifact;
    }

    private static byte[] Rows(IEnumerable<byte[]> source)
    {
        var rows = source.ToArray();
        var indexSize = (rows.Length + 1) * 4;
        var result = new byte[indexSize + rows.Sum(row => row.Length)];
        var offset = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index].CopyTo(result, indexSize + offset);
            offset += rows[index].Length;
            WriteWord(result, (index + 1) * 4, (uint)offset);
        }
        return result;
    }

    private static byte[] Words(params uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++)
        {
            WriteWord(bytes, index * 4, words[index]);
        }
        return bytes;
    }

    private static int SectionOffset(byte[] artifact, int kind)
    {
        var count = (int)ReadWord(artifact, 12);
        for (var index = 0; index < count; index++)
        {
            var offset = 32 + index * 16;
            if (BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(offset)) == kind)
            {
                return (int)ReadWord(artifact, offset + 8);
            }
        }
        throw new InvalidOperationException("Fixture section missing.");
    }

    private static uint ReadWord(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
