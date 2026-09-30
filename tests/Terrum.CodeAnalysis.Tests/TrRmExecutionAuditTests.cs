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
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void UnsupportedEffectsNeverDisappear(uint effect)
        => Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(Artifact(s_return42, effect: effect)));

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
    public void InspectableCallIsNotAnExecutableInstruction()
    {
        byte[] body = [4, 1, 1, 0, 0, 0, 1, 0x12, 1, 1, 0, 0x83, 1];
        var artifact = Artifact(body);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
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
