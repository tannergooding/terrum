// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmArtifactIndexTests
{
    [Fact]
    public void LocatesRequiredSectionsAndResolvesModuleIdentity()
    {
        var artifact = MinimalArtifact();
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x0002, out var stringSection, out var stringCount));
        var strings = new TrRmStringTableReader(stringSection, stringCount);
        Assert.True(index.TryGetSection(0x0001, out var moduleSection, out var moduleCount));
        var modules = new TrRmModuleTableReader(moduleSection, moduleCount, strings, 0, 0, 0, 0);
        Assert.Equal("A", modules.GetIdentity(1));
        Assert.False(index.TryGetSection(0x0010, out var absent, out var absentCount));
        Assert.True(absent.IsEmpty);
        Assert.Equal(0, absentCount);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 2)]
    [InlineData(6, 1)]
    [InlineData(8, 0)]
    [InlineData(12, 0)]
    [InlineData(16, 0)]
    [InlineData(20, 0)]
    [InlineData(24, 1)]
    [InlineData(32 + 2, 1)]
    [InlineData(32 + 4, 0)]
    [InlineData(32 + 12, 43)]
    [InlineData(48, 1)]
    [InlineData(48 + 2, 1)]
    [InlineData(48 + 8, 109)]
    public void RejectsMalformedHeaderAndDirectory(int byteIndex, byte replacement)
    {
        var artifact = MinimalArtifact();
        artifact[byteIndex] = replacement;
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(artifact));
    }

    [Fact]
    public void RejectsOverlapsNonzeroGapsAndTrailingBytes()
    {
        var overlap = MinimalArtifact();
        Write(overlap, 48 + 8, 104);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(overlap));

        var trailing = new byte[125];
        MinimalArtifact().CopyTo(trailing, 0);
        Write(trailing, 16, (uint)trailing.Length);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(trailing));

        var gap = new byte[128];
        MinimalArtifact().AsSpan(0, 108).CopyTo(gap);
        MinimalArtifact().AsSpan(108).CopyTo(gap.AsSpan(112));
        Write(gap, 16, (uint)gap.Length);
        Write(gap, 48 + 8, 112);
        gap[109] = 1;
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(gap));
    }

    [Fact]
    public void PermitsZeroFilledAlignmentGaps()
    {
        var original = MinimalArtifact();
        var gap = new byte[128];
        original.AsSpan(0, 108).CopyTo(gap);
        original.AsSpan(108).CopyTo(gap.AsSpan(112));
        Write(gap, 16, (uint)gap.Length);
        Write(gap, 48 + 8, 112);
        var index = new TrRmArtifactIndex(gap);
        Assert.True(index.TryGetSection(0x0002, out var strings, out var count));
        Assert.Equal("A", new TrRmStringTableReader(strings, count).DecodeName(1));
    }

    [Fact]
    public void ValidatesReferenceSlottedExtensionsAndTargetRows()
    {
        var artifact = WithReferenceSlottedExtension();
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x8000, out var section, out var count));
        Assert.Equal(1, count);
        Assert.Equal(21, section.Length);

        var missingTarget = WithReferenceSlottedExtension();
        Write(missingTarget, 144 + 4, 2);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(missingTarget));

        var danglingBody = WithReferenceSlottedExtension();
        WriteU16(danglingBody, 144, 0x0020);
        WriteU16(danglingBody, 146, 1);
        Write(danglingBody, 152, 1);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(danglingBody));
    }

    [Theory]
    [InlineData((ushort)1)]
    [InlineData((ushort)3)]
    public void SkipsNonsemanticExtensionsUnderTheirRegisteredPolicy(ushort policy)
    {
        var artifact = WithReferenceSlottedExtension();
        WriteU16(artifact, 66, policy);
        Assert.True(new TrRmArtifactIndex(artifact).TryGetSection(0x8000, out _, out var count));
        Assert.Equal(1, count);
    }

    [Fact]
    public void RejectsUnknownRequiredAndUnregisteredExtensionPolicies()
    {
        var required = WithReferenceSlottedExtension();
        WriteU16(required, 64, 0x0030);
        WriteU16(required, 66, 0);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(required));

        var unregistered = WithReferenceSlottedExtension();
        WriteU16(unregistered, 66, 0x8002);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(unregistered));
    }

    private static byte[] MinimalArtifact()
    {
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0)]);
        var artifact = new byte[32 + 2 * 16 + 44 + strings.Length];
        "TRRM"u8.CopyTo(artifact);
        WriteU16(artifact, 4, 1);
        Write(artifact, 8, 32);
        Write(artifact, 12, 2);
        Write(artifact, 16, (uint)artifact.Length);
        Write(artifact, 20, 32);
        WriteU16(artifact, 32, 0x0001);
        Write(artifact, 32 + 4, 1);
        Write(artifact, 32 + 8, 64);
        Write(artifact, 32 + 12, 44);
        WriteU16(artifact, 48, 0x0002);
        Write(artifact, 48 + 4, 1);
        Write(artifact, 48 + 8, 108);
        Write(artifact, 48 + 12, (uint)strings.Length);
        Write(artifact, 64, 1);
        Write(artifact, 64 + 4, 1);
        Write(artifact, 64 + 28, 1);
        Write(artifact, 64 + 32, 1);
        Write(artifact, 64 + 36, 1);
        Write(artifact, 64 + 40, 1);
        strings.CopyTo(artifact.AsSpan(108));
        return artifact;
    }

    private static byte[] WithReferenceSlottedExtension()
    {
        var original = MinimalArtifact();
        var artifact = new byte[161];
        original.AsSpan(0, 64).CopyTo(artifact);
        original.AsSpan(64, 44).CopyTo(artifact.AsSpan(80));
        original.AsSpan(108).CopyTo(artifact.AsSpan(124));
        Write(artifact, 12, 3);
        Write(artifact, 16, (uint)artifact.Length);
        Write(artifact, 32 + 8, 80);
        Write(artifact, 48 + 8, 124);
        WriteU16(artifact, 64, 0x8000);
        WriteU16(artifact, 66, 2);
        Write(artifact, 68, 1);
        Write(artifact, 72, 140);
        Write(artifact, 76, 21);
        Write(artifact, 140, 1);
        WriteU16(artifact, 144, 0x0002);
        Write(artifact, 148, 1);
        return artifact;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteU16(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
}
