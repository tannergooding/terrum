// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Tests.Semantics.Generics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmExecutionAuditTests
{
    private static readonly byte[] s_return42 = [4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1];
    [TestCase(true,20)]
    [TestCase(false,42)]
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
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase(PrimitiveType.Int32,0xFFFF_FFF9UL,3UL,0xFFFF_FFFEUL,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt32,0xFFFF_FFFFUL,3UL,0x5555_5555UL,0UL)]
    [TestCase(PrimitiveType.Int64,0xFFFF_FFFF_FFFF_FFF9UL,3UL,        0xFFFF_FFFF_FFFF_FFFEUL,0xFFFF_FFFF_FFFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt64,0xFFFF_FFFF_FFFF_FFFFUL,10UL,        0x1999_9999_9999_9999UL,5UL)]
    public void IndependentCheckedDivRemDefinesAndExecutesBothResults(PrimitiveType type,
        ulong dividend, ulong divisor, ulong quotient, ulong remainder)
    {
        var artifact = Artifact(CheckedDivRemBody(type, dividend, divisor, quotient, remainder));
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase(PrimitiveType.Int32,7UL,0UL,unchecked((int)0xE0540002))]
    [TestCase(PrimitiveType.Int32,0x8000_0000UL,0xFFFF_FFFFUL,unchecked((int)0xE0540001))]
    [TestCase(PrimitiveType.Int64,0x8000_0000_0000_0000UL,0xFFFF_FFFF_FFFF_FFFFUL,        unchecked((int)0xE0540001))]
    public async Task IndependentCheckedDivRemPreservesFailureStatuses(PrimitiveType type,
        ulong dividend, ulong divisor, int expectedStatus)
    {
        var artifact = Artifact(CheckedDivRemBody(type, dividend, divisor, 0, 0));
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(await StaticProtocolTests.RunAsync(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast))
                .ConfigureAwait(true), Is.EqualTo(expectedStatus));
    }
    [TestCase(PrimitiveType.Int32,(byte)(10),0x7FFF_FFFFUL,1UL,0x8000_0000UL)]
    [TestCase(PrimitiveType.UInt32,(byte)(10),0xFFFF_FFFFUL,1UL,0UL)]
    [TestCase(PrimitiveType.Int64,(byte)(10),0x7FFF_FFFF_FFFF_FFFFUL,1UL,0x8000_0000_0000_0000UL)]
    [TestCase(PrimitiveType.UInt64,(byte)(10),0xFFFF_FFFF_FFFF_FFFFUL,1UL,0UL)]
    [TestCase(PrimitiveType.Int32,(byte)(11),0x8000_0000UL,1UL,0x7FFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt32,(byte)(11),0UL,1UL,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.Int64,(byte)(11),0x8000_0000_0000_0000UL,1UL,0x7FFF_FFFF_FFFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt64,(byte)(11),0UL,1UL,0xFFFF_FFFF_FFFF_FFFFUL)]
    [TestCase(PrimitiveType.Int32,(byte)(12),0x4000_0000UL,4UL,0UL)]
    [TestCase(PrimitiveType.UInt32,(byte)(12),0x8000_0000UL,2UL,0UL)]
    [TestCase(PrimitiveType.Int64,(byte)(12),0x4000_0000_0000_0000UL,4UL,0UL)]
    [TestCase(PrimitiveType.UInt64,(byte)(12),0x8000_0000_0000_0000UL,2UL,0UL)]
    public void IndependentWrappingBinaryExecutesAtTheOperandWidth(PrimitiveType type,
        byte kind, ulong left, ulong right, ulong expected)
    {
        var artifact = Artifact(WrappingBinaryBody(type, kind, left, right, expected));
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase(PrimitiveType.Int32,PrimitiveType.UInt64,(byte)(1),0xFFFF_FFFFUL,0xFFFF_FFFF_FFFF_FFFFUL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.Int32,(byte)(1),0xFFFF_FFFF_7FFF_FFFFUL,0x7FFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt64,PrimitiveType.UInt32,(byte)(1),0x1_0000_0001UL,1UL)]
    [TestCase(PrimitiveType.UInt32,PrimitiveType.Int32,(byte)(1),0xFFFF_FFFFUL,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.UInt32,(byte)(1),0xFFFF_FFFFUL,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt32,PrimitiveType.Int64,(byte)(2),0xFFFF_FFFFUL,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.Int32,PrimitiveType.Int64,(byte)(2),0xFFFF_FFFFUL,0xFFFF_FFFF_FFFF_FFFFUL)]
    [TestCase(PrimitiveType.Int32,PrimitiveType.UInt64,(byte)(2),0xFFFF_FFFFUL,0UL)]
    [TestCase(PrimitiveType.Int32,PrimitiveType.UInt32,(byte)(2),0xFFFF_FFFFUL,0UL)]
    [TestCase(PrimitiveType.UInt32,PrimitiveType.Int32,(byte)(2),0xFFFF_FFFFUL,0x7FFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt32,PrimitiveType.Int32,(byte)(2),0x7FFF_FFFFUL,0x7FFF_FFFFUL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.Int32,(byte)(2),0x8000_0000_0000_0000UL,0x8000_0000UL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.Int32,(byte)(2),0xFFFF_FFFF_7FFF_FFFFUL,0x8000_0000UL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.Int32,(byte)(2),0x0000_0000_8000_0000UL,0x7FFF_FFFFUL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.UInt32,(byte)(2),0xFFFF_FFFF_FFFF_FFFFUL,0UL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.UInt32,(byte)(2),0xFFFF_FFFFUL,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.UInt32,(byte)(2),0x0000_0001_0000_0000UL,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.UInt64,(byte)(2),1UL,1UL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.UInt64,(byte)(2),0xFFFF_FFFF_FFFF_FFFFUL,0UL)]
    [TestCase(PrimitiveType.UInt64,PrimitiveType.Int64,(byte)(2),0x7FFF_FFFF_FFFF_FFFFUL,0x7FFF_FFFF_FFFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt64,PrimitiveType.Int64,(byte)(2),0xFFFF_FFFF_FFFF_FFFFUL,0x7FFF_FFFF_FFFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt64,PrimitiveType.Int32,(byte)(2),0x0000_0000_8000_0000UL,0x7FFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt64,PrimitiveType.Int32,(byte)(2),0x7FFF_FFFFUL,0x7FFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt64,PrimitiveType.UInt32,(byte)(2),0x0000_0001_0000_0000UL,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt64,PrimitiveType.UInt32,(byte)(2),0xFFFF_FFFFUL,0xFFFF_FFFFUL)]
    public void IndependentIntegerConvertExecutesWrapAndSaturation(PrimitiveType sourceType,
        PrimitiveType destinationType, byte policy, ulong sourceBits, ulong expectedBits)
    {
        var artifact = Artifact(IntegerConvertBody(sourceType, destinationType, policy,
            sourceBits, expectedBits));
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(1));
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase(false,0xD800u,55296)]
    [TestCase(false,0xFFFFu,65535)]
    [TestCase(true,0u,0)]
    [TestCase(true,0xD800u,55296)]
    [TestCase(true,0xFFFFu,65535)]
    public void IndependentCheckedConvertExecutesChar16Boundaries(
        bool narrowing, uint sourceBits, int expected)
    {
        var artifact = Artifact(CheckedChar16ConvertBody(narrowing, sourceBits));
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase(PrimitiveType.Int32,PrimitiveType.Int64,0x8000_0000UL,0xFFFF_FFFF_8000_0000UL)]
    [TestCase(PrimitiveType.Int32,PrimitiveType.UInt64,42UL,42UL)]
    [TestCase(PrimitiveType.UInt32,PrimitiveType.Int64,0xFFFF_FFFFUL,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt32,PrimitiveType.UInt64,0xFFFF_FFFFUL,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt32,PrimitiveType.Int32,0x7FFF_FFFFUL,0x7FFF_FFFFUL)]
    [TestCase(PrimitiveType.Int32,PrimitiveType.UInt32,0x7FFF_FFFFUL,0x7FFF_FFFFUL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.Int32,0xFFFF_FFFF_8000_0000UL,0x8000_0000UL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.UInt32,0xFFFF_FFFFUL,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt64,PrimitiveType.Int32,0x7FFF_FFFFUL,0x7FFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt64,PrimitiveType.UInt32,0xFFFF_FFFFUL,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt64,PrimitiveType.Int64,0x7FFF_FFFF_FFFF_FFFFUL,0x7FFF_FFFF_FFFF_FFFFUL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.UInt64,0x7FFF_FFFF_FFFF_FFFFUL,0x7FFF_FFFF_FFFF_FFFFUL)]
    public void IndependentCheckedConvertExecutesIntegerBoundaries(PrimitiveType sourceType,
        PrimitiveType destinationType, ulong sourceBits, ulong expectedBits)
    {
        var artifact = Artifact(CheckedIntegerConvertBody(sourceType,
            destinationType, sourceBits, expectedBits));
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(1));
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase(0xFFFF_FFFFu)]
    [TestCase(0x1_0000u)]
    public async Task IndependentCheckedConvertRejectsOutOfRangeChar16(uint sourceBits)
    {
        var artifact = Artifact(CheckedChar16ConvertBody(narrowing: true, sourceBits: sourceBits));
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(await StaticProtocolTests.RunAsync(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast))
                .ConfigureAwait(true), Is.EqualTo(unchecked((int)0xE0540001)));
    }
    [TestCase(PrimitiveType.Int32,PrimitiveType.UInt64,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.UInt32,PrimitiveType.Int32,0x8000_0000UL)]
    [TestCase(PrimitiveType.Int32,PrimitiveType.UInt32,0xFFFF_FFFFUL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.Int32,0xFFFF_FFFF_7FFF_FFFFUL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.Int32,0x0000_0000_8000_0000UL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.UInt32,0xFFFF_FFFF_FFFF_FFFFUL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.UInt32,0x0000_0001_0000_0000UL)]
    [TestCase(PrimitiveType.UInt64,PrimitiveType.Int32,0x0000_0000_8000_0000UL)]
    [TestCase(PrimitiveType.UInt64,PrimitiveType.UInt32,0x0000_0001_0000_0000UL)]
    [TestCase(PrimitiveType.UInt64,PrimitiveType.Int64,0x8000_0000_0000_0000UL)]
    [TestCase(PrimitiveType.Int64,PrimitiveType.UInt64,0xFFFF_FFFF_FFFF_FFFFUL)]
    public async Task IndependentCheckedConvertRejectsOutOfRangeIntegers(
        PrimitiveType sourceType, PrimitiveType destinationType, ulong sourceBits)
    {
        var artifact = Artifact(CheckedIntegerConvertBody(sourceType,
            destinationType, sourceBits, expectedBits: 0));
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(await StaticProtocolTests.RunAsync(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast))
                .ConfigureAwait(true), Is.EqualTo(unchecked((int)0xE0540001)));
    }

    [Test]
    public void IndependentBytesExecuteWithoutCompilerOrTableWriter()
    {
        var bytes = Artifact(s_return42);
        var program = TrRmExecutableCompiler.Load(bytes);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
        Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(program, NativeRuntimePolicy.CheckedExceptions));
    }
    [TestCase(new byte[] { 4, 1, 1, 0, 0, 0, 1, 2, 1, 1, 0xFF, 0x83, 1 },-1)]
    [TestCase(new byte[] { 4, 1, 1, 0, 0, 0, 1, 0xF0, 0x10, 2, 1, 1, 200, 0, 0x83, 1 },200)]
    [TestCase(new byte[] { 4, 1, 1, 0, 0, 0, 2, 1, 1, 1, 42, 3, 2, 1, 0x83, 2 },42)]
    public void IndependentSignedWidthsAndCopyAreTyped(byte[] body, int expected)
    {
        var program = TrRmExecutableCompiler.Load(Artifact(body));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
    }

    [Test]
    public void IndependentUnsignedSignedLiteralRetainsFullBitPattern()
    {
        byte[] body = [4, 1, 4, 0, 0, 0, 1, 0xF0, 0x30, 2, 1, 1,
            0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 0x83, 1];
        var program = TrRmExecutableCompiler.Load(Artifact(body, resultRow: 4, entry: false));
        var constant = ((global::System.Func<BoundConstant>)(() => { var __nunitActual1 = ((global::System.Func<BoundLocal>)(() => { var __nunitActual0 = program.Functions[0].Body.Statements[0]; Assert.That(__nunitActual0, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual0!; }))().Initializer; Assert.That(__nunitActual1, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual1!; }))();
        Assert.That(constant.Type, Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(constant.Value, Is.EqualTo(-1));
    }

    [Test]
    public void IndependentCheckedSumPreservesOneDefinitionPerEvaluation()
    {
        byte[] body = [4, 1, 1, 0, 0, 0, 3,
            1, 1, 1, 20, 1, 2, 1, 1, 0x15, 3, 1, 1, 2, 0x83, 3];
        var program = TrRmExecutableCompiler.Load(Artifact(body));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(21));
        Assert.That(program.Functions[0].Variables.Length, Is.EqualTo(3));
        Assert.That(program.Functions[0].Body.Statements.Length, Is.EqualTo(4));
        var sum = ((global::System.Func<BoundBinary>)(() => { var __nunitActual3 = ((global::System.Func<BoundLocal>)(() => { var __nunitActual2 = program.Functions[0].Body.Statements[2]; Assert.That(__nunitActual2, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual2!; }))().Initializer; Assert.That(__nunitActual3, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual3!; }))();
        Assert.That(sum.Left, Is.TypeOf<BoundRead>());
        Assert.That(sum.Right, Is.TypeOf<BoundRead>());
    }
    [TestCase(1,4,2u)]
    [TestCase(1,8,1u)]
    [TestCase(1,12,2u)]
    [TestCase(1,16,1u)]
    [TestCase(0x16,8,0x11000001u)]
    [TestCase(0x16,12,3u)]
    [TestCase(0x16,24,0u)]
    [TestCase(0x17,0,2u)]
    [TestCase(0x17,4,1u)]
    public void RejectsIndependentMetadataTampering(int kind, int offset, uint value)
    {
        var artifact = Artifact(s_return42);
        WriteWord(artifact, SectionOffset(artifact, kind) + offset, value);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(10)]
    [TestCase(11)]
    [TestCase(12)]
    [TestCase(13)]
    [TestCase(14)]
    [TestCase(15)]
    public void RejectsEveryReservedContractFlagBit(int bit)
    {
        var artifact = Artifact(s_return42, effect: 1);
        var data = SectionOffset(artifact, 0x1B) + 8;
        BinaryPrimitives.WriteUInt16LittleEndian(artifact.AsSpan(data + 6), (ushort)(1 << bit));
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }
    [TestCase((uint)(8))]
    public void UnsupportedEffectsNeverDisappear(uint effect)
        => Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(Artifact(s_return42, effect: effect)));

    [Test]
    public void DoesNotReturnEffectRejectsAReachableNormalReturn()
        => Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(Artifact(s_return42, effect: 2)));

    [Test]
    public void NoThrowAuditRejectsEvenConstantSafeCheckedOperations()
    {
        byte[] body = [4, 1, 1, 0, 0, 0, 3,
            1, 1, 1, 20, 1, 2, 1, 22, 0x15, 3, 1, 1, 2, 0x83, 3];
        var artifact = Artifact(body, effect: 1);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Test]
    public void NoThrowAuditRejectsCheckedDivRem()
    {
        var artifact = Artifact(CheckedDivRemBody(PrimitiveType.Int32, 7, 3, 2, 1), effect: 1);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Test]
    public void NoThrowAllowsTotalWrappingBinary()
    {
        var artifact = Artifact(WrappingBinaryBody(PrimitiveType.Int32, 10,
            0x7FFF_FFFF, 1, 0x8000_0000), effect: 1);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void CheckedDivRemResultsMustBeConsecutiveAndDistinct()
    {
        var body = CheckedDivRemBody(PrimitiveType.Int32, 7, 3, 2, 1);
        var operation = Array.IndexOf(body, (byte)0x18);
        body[operation + 2] = 3;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(Artifact(body)));
    }

    [Test]
    public void ContractOwnerMustBeTheExactMethodDef()
    {
        var artifact = Artifact(s_return42, effect: 1);
        WriteWord(artifact, SectionOffset(artifact, 0x1B) + 8, 0x13000001);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Test]
    public void SelfCallIsAuditedWithoutExecutingUnboundedRecursion()
    {
        byte[] body = [4, 1, 1, 0, 0, 0, 1, 0x12, 1, 1, 0, 0x83, 1];
        var artifact = Artifact(body);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(loaded.Functions[0].Body.Statements, Is.Empty);
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(loaded, "source-free"));
    }

    [Test]
    public void HandwrittenForwardLinearCallUsesModuleOrdinalAndParameterSignature()
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 2,
            1, 1, 1, 41, 0x12, 2, 2, 1, 1, 0x83, 2];
        byte[] callee = [4, 1, 1, 0, 0, 0, 0, 0x83, 1];
        var artifact = LocalCallArtifact(caller, callee);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast), Is.EqualTo(41));
    }

    [Test]
    public void CallerUnsafeEffectDoesNotAuthorizeAnExecutableCall()
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 1, 0x12, 2, 2, 0, 0x83, 2];
        byte[] callee = [4, 1, 1, 0, 0, 0, 0, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            LocalCallArtifact(caller, callee, calleeCallerUnsafe: true)));
    }
    [TestCase((byte)(0))]
    [TestCase((byte)(3))]
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

    [Test]
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
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast), Is.EqualTo(41));
    }

    [Test]
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
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast), Is.EqualTo(41));
    }

    [Test]
    public void HandwrittenLinearVoidCallBetweenDefinitionsKeepsValueNumbering()
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 4,
            1, 1, 1, 41, 0x12, 0, 2, 1, 1,
            3, 2, 1, 0x12, 0, 2, 1, 2, 0x83, 2];
        byte[] callee = [4, 1, 1, 0, 0, 0, 0, 0x83, 0];
        Assert.That(NativeJit.Execute(
            TrRmExecutableCompiler.Load(LocalCallArtifact(caller, callee, resultRow: 7)),
            NativeRuntimePolicy.FailFast), Is.EqualTo(41));
    }
    [TestCase((byte)(0),(byte)(1),(byte)(0))]
    [TestCase((byte)(2),(byte)(0),(byte)(1))]
    [TestCase((byte)(2),(byte)(2),(byte)(1))]
    [TestCase((byte)(3),(byte)(1),(byte)(0))]
    public void HandwrittenCallRejectsResultCountAndModeMismatch(byte result, byte argumentCount, byte mode)
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 2,
            1, 1, 1, 41, 0x12, result, 2, argumentCount, 1, 0x83, 2];
        byte[] callee = [4, 1, 1, 0, 0, 0, 0, 0x83, 1];
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(LocalCallArtifact(caller, callee, parameterMode: mode)));
    }

    [Test]
    public void NoThrowCallRequiresCalleeDeclarationAndCannotBeForgedAfterLoad()
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 2,
            1, 1, 1, 41, 0x12, 2, 2, 1, 1, 0x83, 2];
        byte[] callee = [4, 1, 1, 0, 0, 0, 0, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            LocalCallArtifact(caller, callee, callerNoThrow: true)));
        var program = TrRmExecutableCompiler.Load(
            LocalCallArtifact(caller, callee, callerNoThrow: true, calleeNoThrow: true));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(41));
        var changedEffect = program with {
            Functions = program.Functions.SetItem(1, program.Functions[1] with { IsNoThrow = false }),
        };
        Assert.Throws<ArgumentException>(() =>
            NativeEmitter.Emit(changedEffect, NativeRuntimePolicy.FailFast));
        var changedType = program with {
            Functions = program.Functions.SetItem(1, program.Functions[1] with {
                Variables = program.Functions[1].Variables.SetItem(0,
                    program.Functions[1].Variables[0] with { Type = PrimitiveType.UInt32 }),
            }),
        };
        Assert.Throws<ArgumentException>(() =>
            NativeEmitter.Emit(changedType, NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void UncalledCalleeStillRequiresACompleteAuditedBody()
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1];
        byte[] unsupported = [4, 1, 1, 0, 0, 0, 1, 0x19, 2, 1, 0x83, 2];
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(LocalCallArtifact(caller, unsupported)));
    }

    [Test]
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
        var strings = new[] { "independent", "Main", "Callee" }.Select(text => {
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
            var row = type switch {
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
            var row = type switch {
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
            var row = value switch {
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
            var row = value switch {
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

    [Test]
    public void PublicVisibilityWithoutAnExportCannotPublishNativeSymbols()
    {
        var artifact = Artifact(s_return42, entry: false, exported: true);
        Assert.That(NativeEmitter.EmitLibrary(TrRmExecutableCompiler.Load(artifact),
            ["Main"], NativeRuntimePolicy.FailFast), Is.Not.Empty);
        artifact = Artifact(s_return42, entry: false);
        WriteWord(artifact, SectionOffset(artifact, 0x16) + 12, 2);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Test]
    public void AuditChecksNonentryMethodsAsWellAsTheEntry()
    {
        byte[] invalid = [4, 1, 4, 0, 0, 0, 3,
            1, 1, 1, 20, 1, 2, 1, 22, 0x15, 3, 1, 1, 2, 0x83, 3];
        var original = TrRmExecutableCompiler.Load(Artifact(s_return42));
        var source = original with {
            Functions = original.Functions.Add(original.Functions[0] with { Name = "Other", IsNoThrow = true }),
        };
        var bytes = TrRmExecutableCompiler.Emit(source, "two-bodies");
        var index = new TrRmArtifactIndex(bytes);
        Assert.That(index.TryGetSection(0x20, out var bodies, out var bodyCount), Is.True);
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
    [TestCase(new byte[] { 4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 2 })]
    [TestCase(new byte[] { 4, 1, 1, 0, 0, 0, 1, 3, 1, 1, 0x83, 1 })]
    [TestCase(new byte[] { 4, 1, 1, 0, 0, 0, 1, 1, 2, 1, 42, 0x83, 2 })]
    [TestCase(new byte[] { 4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1, 0 })]
    [TestCase(new byte[] { 4, 1, 1, 0, 0, 0, 1, 0xF0, 0x10, 1, 1, 1, 42, 0, 0x83, 1 })]
    [TestCase(new byte[] { 4, 1, 1, 0, 0, 0, 1, 0xF0, 0x01, 1, 1, 0, 1, 0, 42, 0x83, 1 })]
    [TestCase(new byte[] { 4, 1, 1, 0, 0, 0, 1, 0x19, 1, 1, 42, 0x83, 1 })]
    public void RejectsIndependentInvalidBodyAndNoncanonicalWidths(byte[] body)
        => Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(Artifact(body)));

    [Test]
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
