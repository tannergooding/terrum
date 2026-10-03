// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    [InlineData(4)]
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
        bool calleeNoThrow = false)
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
        var contractCount = (callerNoThrow ? 1 : 0) + (calleeNoThrow ? 1 : 0);
        var sections = new List<(ushort Kind, uint Count, byte[] Bytes)>
        {
            (1, 1, Words(1, 1, 0, 1, 0, 0, 0, 1, 1, 1, 1)),
            (2, 3, Rows(strings)),
            (0x11, 7, Rows(codes.Select(code => new byte[] { 1, 0, code }))),
            (0x16, 2, Words(
                0, 2, 1, 1, callerNoThrow ? 1u : 0, callerNoThrow ? 1u : 0, 1,
                0, 3, 2, 1, calleeNoThrow ? (uint)(contractCount) : 0,
                calleeNoThrow ? 1u : 0, 2)),
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
            if (calleeNoThrow)
            {
                clauses.Add(Words(0x16000002, 1, 1));
            }
            sections.Add((0x1B, (uint)contractCount, Rows(clauses)));
        }
        sections.Add((0x20, 2, Rows([caller, callee])));
        return Assemble(sections);
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
