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

    [Fact]
    public void FindsSectionsAtDirectoryBoundariesAndRejectsMissingKinds()
    {
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, new byte[44]),
            new TrRmSection(0x0002, 0, 1, TrRmStringTableWriter.EncodeNames([("A", 0)])),
            new TrRmSection(0x8000, 1, 1, new byte[] { 1 }),
            new TrRmSection(0xFFFF, 3, 1, new byte[] { 2 }),
        ]);
        var index = new TrRmArtifactIndex(artifact);

        Assert.True(index.TryGetSection(0x0001, out var first, out var firstCount));
        Assert.Equal(44, first.Length);
        Assert.Equal(1, firstCount);
        Assert.True(index.TryGetSection(0xFFFF, out var last, out var lastCount));
        Assert.Equal((byte)2, last[0]);
        Assert.Equal(1, lastCount);
        foreach (var missing in new ushort[] { 0, 3, 0x7FFF, 0x8001 })
        {
            Assert.False(index.TryGetSection(missing, out var absent, out var count));
            Assert.True(absent.IsEmpty);
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public void IndexesModuleReferencesAndSourceFilesThroughOneArtifact()
    {
        var artifact = ArtifactWithMetadataTables();
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x0002, out var nameBytes, out var nameCount));
        var names = new TrRmStringTableReader(nameBytes, nameCount);
        Assert.True(index.TryGetSection(0x0001, out var moduleBytes, out var moduleCount));
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount, names, 1, 0, 0, 1);
        Assert.True(index.TryGetSection(0x0010, out var refBytes, out var refCount));
        var refs = new TrRmModuleRefTableReader(refBytes, refCount, names, modules);
        Assert.Equal("B", refs.GetProducerIdentity(1));
        Assert.Equal(1, refs.GetOwnerModule(1));
        Assert.True(index.TryGetSection(0x001F, out var sourceBytes, out var sourceCount));
        var sources = new TrRmSourceFileTableReader(sourceBytes, sourceCount, names, modules);
        Assert.Equal("src/A.terrum", sources.GetPath(1));
        Assert.Equal(1, sources.GetOwnerModule(1));
    }

    [Fact]
    public void ComposesFramedStringTypeSpecSignatureAndCallableMetadata()
    {
        var artifact = ArtifactWithCallableMetadata();
        Assert.Equal("Run", ReadCallableName(artifact));

        var namesLength = TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0)]).Length;
        var methodOffset = 32 + 5 * 16 + 44 + namesLength + 11;
        artifact[methodOffset + 12] = 3;
        _ = new TrRmArtifactIndex(artifact);
        Assert.Throws<InvalidDataException>(() => ReadCallableName(artifact));
    }

    [Fact]
    public void StructuralValidatorComposesAllPresentKnownMetadataTables()
    {
        TrRmStructuralMetadataValidator.Validate(MinimalArtifact());
        TrRmStructuralMetadataValidator.Validate(ArtifactWithMetadataTables());
        TrRmStructuralMetadataValidator.Validate(ArtifactWithCallableMetadata());
        var withBlob = ArtifactWithCallableMetadata(includeEmptyBlob: true);
        TrRmStructuralMetadataValidator.Validate(withBlob);
        TrRmStructuralMetadataValidator.Validate(ArtifactWithCallableMetadata(includeBody: true));
        TrRmStructuralMetadataValidator.Validate(ArtifactWithCallableMetadata(includeSourceMap: true));
        TrRmStructuralMetadataValidator.Validate(ArtifactWithCallableMetadata(includeOutputState: true));
        TrRmStructuralMetadataValidator.Validate(WithReferenceSlottedExtension());

        var invalidOutput = ArtifactWithCallableMetadata(includeOutputState: true);
        invalidOutput[^8] = 1;
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(invalidOutput));

        var invalidBlob = ArtifactWithCallableMetadata(includeEmptyBlob: true);
        var blobOffset = 32 + 6 * 16 + 44
            + TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0)]).Length;
        invalidBlob[blobOffset + 4] = 1;
        _ = new TrRmArtifactIndex(invalidBlob);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(invalidBlob));

        var invalidBody = ArtifactWithCallableMetadata(includeBody: true);
        invalidBody[^5] = 0;
        _ = new TrRmArtifactIndex(invalidBody);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(invalidBody));

        var invalidMap = ArtifactWithCallableMetadata(includeSourceMap: true);
        invalidMap[^12] = 1;
        _ = new TrRmArtifactIndex(invalidMap);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(invalidMap));

        var invalid = ArtifactWithCallableMetadata();
        var stringsLength = TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0)]).Length;
        invalid[32 + 5 * 16 + 44 + stringsLength + 11 + 12] = 3;
        _ = new TrRmArtifactIndex(invalid);
        Assert.Throws<InvalidDataException>(() => TrRmStructuralMetadataValidator.Validate(invalid));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 0xFF)]
    [InlineData(10, 1)]
    [InlineData(11, 1)]
    public void StructuralValidatorRejectsMalformedUnreferencedStringRows(int rowOffset,
        byte replacement)
    {
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0), ("Unused", 0)]);
        var secondRow = 12 + (int)BinaryPrimitives.ReadUInt32LittleEndian(strings.AsSpan(4));
        strings[secondRow + rowOffset] = replacement;
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, MinimalArtifact().AsSpan(64, 44).ToArray()),
            new TrRmSection(0x0002, 0, 2, strings),
        ]);
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x0002, out var section, out var count));
        Assert.Equal("A", new TrRmStringTableReader(section, count).DecodeName(1));
        Assert.Throws<InvalidDataException>(() => TrRmStructuralMetadataValidator.Validate(artifact));
    }

    [Fact]
    public void StructuralValidatorPermitsUnreferencedStringContentWithNul()
    {
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0), ("Unused", 0)]);
        var secondRow = 12 + (int)BinaryPrimitives.ReadUInt32LittleEndian(strings.AsSpan(4));
        strings[secondRow + 4] = 0;
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, MinimalArtifact().AsSpan(64, 44).ToArray()),
            new TrRmSection(0x0002, 0, 2, strings),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x0002, out var section, out var count));
        var sectionBytes = section.ToArray();
        Assert.Throws<InvalidDataException>(() =>
            new TrRmStringTableReader(sectionBytes, count).DecodeName(2));
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

        var absentTarget = WithReferenceSlottedExtension();
        WriteU16(absentTarget, 144, 0x0010);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(absentTarget));

        var danglingBody = WithReferenceSlottedExtension();
        WriteU16(danglingBody, 144, 0x0020);
        WriteU16(danglingBody, 146, 1);
        Write(danglingBody, 152, 1);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(danglingBody));
    }

    [Theory]
    [InlineData((ushort)1)]
    [InlineData((ushort)2)]
    [InlineData((ushort)3)]
    public void EnumeratesOrderedSectionsIncludingOptionalExtensionPolicies(ushort policy)
    {
        var artifact = WithReferenceSlottedExtension();
        WriteU16(artifact, 66, policy);
        var index = new TrRmArtifactIndex(artifact);
        Assert.Equal(3, index.SectionCount);
        var preserved = new TrRmSection[index.SectionCount];
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags, out var payload, out var count);
            preserved[entry] = new TrRmSection(kind, flags, (uint)count, payload.ToArray());
        }

        Assert.Equal((ushort)0x8000, preserved[2].Kind);
        Assert.Equal(policy, preserved[2].Flags);
        Assert.Equal(artifact, TrRmArtifactWriter.Encode(preserved));
        var sectionCount = index.SectionCount;
        Assert.Throws<InvalidDataException>(() =>
            GetIndexedSection(artifact, -1));
        Assert.Throws<InvalidDataException>(() =>
            GetIndexedSection(artifact, sectionCount));
    }

    private static void GetIndexedSection(byte[] artifact, int entry)
    {
        var index = new TrRmArtifactIndex(artifact);
        index.GetSection(entry, out _, out _, out _, out _);
    }

    [Fact]
    public void RemapsOnlyOptionalExtensionSlotsAndRejectsDanglingTargets()
    {
        var original = WithReferenceSlottedExtension();
        var index = new TrRmArtifactIndex(original);
        var sections = new TrRmSection[index.SectionCount];
        for (var entry = 0; entry < sections.Length; entry++)
        {
            index.GetSection(entry, out var kind, out var flags, out var bytes, out var count);
            sections[entry] = new TrRmSection(kind, flags, (uint)count, bytes.ToArray());
        }

        var extension = sections[2];
        sections[2] = TrRmOptionalSectionSlots.Replace(extension,
            [new TrRmReferenceSlot(0x0001, 0, 1, 0, 0)]);
        Assert.Equal(extension.Bytes.Span[20], sections[2].Bytes.Span[20]);
        Assert.Equal((ushort)0x0002,
            BinaryPrimitives.ReadUInt16LittleEndian(extension.Bytes.Span[4..]));
        var remapped = new TrRmArtifactIndex(TrRmArtifactWriter.Encode(sections));
        Assert.True(remapped.TryGetSection(0x8000, out var payload, out _));
        Assert.Equal((ushort)0x0001, BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]));
        Assert.Equal(extension.Bytes.Span[20], payload[20]);

        sections[2] = TrRmOptionalSectionSlots.Replace(extension,
            [new TrRmReferenceSlot(0x0001, 0, 2, 0, 0)]);
        Assert.Throws<InvalidDataException>(() => TrRmArtifactWriter.Encode(sections));
        Assert.Throws<InvalidDataException>(() =>
            TrRmOptionalSectionSlots.Replace(extension, []));
        Assert.Throws<InvalidDataException>(() =>
            TrRmOptionalSectionSlots.Replace(extension with { Flags = 1 },
                [new TrRmReferenceSlot(0x0001, 0, 1, 0, 0)]));

        var atomic = TrRmOptionalSectionSlots.ReplaceInArtifact(original, 0x8000,
            [new TrRmReferenceSlot(0x0001, 0, 1, 0, 0)]);
        Assert.Equal((ushort)0x0002,
            BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(144)));
        Assert.Equal((ushort)0x0001,
            BinaryPrimitives.ReadUInt16LittleEndian(atomic.AsSpan(144)));
        Assert.Throws<InvalidDataException>(() =>
            TrRmOptionalSectionSlots.ReplaceInArtifact(original, 0x8000,
                [new TrRmReferenceSlot(0x0001, 0, 2, 0, 0)]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmOptionalSectionSlots.ReplaceInArtifact(original, 0x8001,
                [new TrRmReferenceSlot(0x0001, 0, 1, 0, 0)]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmOptionalSectionSlots.ReplaceInArtifact(original[..^1], 0x8000,
                [new TrRmReferenceSlot(0x0001, 0, 1, 0, 0)]));
        var opaquePolicy = (byte[])original.Clone();
        WriteU16(opaquePolicy, 66, 3);
        Assert.Throws<InvalidDataException>(() =>
            TrRmOptionalSectionSlots.ReplaceInArtifact(opaquePolicy, 0x8000,
                [new TrRmReferenceSlot(0x0001, 0, 1, 0, 0)]));
    }

    [Fact]
    public void InsertsSlottedExtensionWithoutMutatingExistingArtifact()
    {
        var baseArtifact = MinimalArtifact();
        var framed = WithReferenceSlottedExtension();
        var existing = new TrRmArtifactIndex(framed);
        Assert.True(existing.TryGetSection(0x8000, out var payload, out var count));
        var section = new TrRmSection(0x8000, 2, (uint)count, payload.ToArray());
        var authored = TrRmOptionalSectionSlots.Create(0x8000, 1,
            [new TrRmReferenceSlot(0x0002, 0, 1, 0, 0)], new byte[] { 0 });
        Assert.Equal(section.Bytes.ToArray(), authored.Bytes.ToArray());
        Assert.Equal(framed, TrRmOptionalSectionSlots.InsertInArtifact(baseArtifact, authored));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(baseArtifact.AsSpan(12)));

        Assert.Throws<ArgumentException>(() =>
            TrRmOptionalSectionSlots.InsertInArtifact(framed, section));
        Assert.Throws<InvalidDataException>(() =>
            TrRmOptionalSectionSlots.InsertInArtifact(baseArtifact,
                TrRmOptionalSectionSlots.Replace(section,
                    [new TrRmReferenceSlot(0x0002, 0, 2, 0, 0)])));
        Assert.Throws<InvalidDataException>(() =>
            TrRmOptionalSectionSlots.InsertInArtifact(baseArtifact, section with { Flags = 1 }));
        Assert.Throws<InvalidDataException>(() =>
            TrRmOptionalSectionSlots.InsertInArtifact(baseArtifact[..^1], section));
        Assert.Throws<InvalidDataException>(() =>
            TrRmOptionalSectionSlots.Create(0x7FFF, 1, [], ReadOnlyMemory<byte>.Empty));
        Assert.Throws<InvalidDataException>(() =>
            TrRmOptionalSectionSlots.Create(0x8000, 0, [], ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void ArtifactChangesDropDiscardOnRewriteExtensions()
    {
        var original = WithReferenceSlottedExtension();
        var index = new TrRmArtifactIndex(original);
        var sections = new TrRmSection[index.SectionCount + 2];
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags, out var bytes, out var count);
            sections[entry] = new TrRmSection(kind, flags, (uint)count, bytes.ToArray());
        }
        sections[^2] = new TrRmSection(0x8001, 1, 1, new byte[] { 9 });
        sections[^1] = new TrRmSection(0x8002, 3, 1, new byte[] { 42 });
        var withDiscard = TrRmArtifactWriter.Encode(sections);
        var replacement = new[] { new TrRmReferenceSlot(0x0001, 0, 1, 0, 0) };
        var remapped = new TrRmArtifactIndex(
            TrRmOptionalSectionSlots.ReplaceInArtifact(withDiscard, 0x8000, replacement));
        Assert.True(remapped.TryGetSection(0x8001, out _, out _));
        Assert.False(remapped.TryGetSection(0x8002, out _, out _));
        Assert.Equal(4, remapped.SectionCount);

        var added = new TrRmArtifactIndex(TrRmOptionalSectionSlots.InsertInArtifact(withDiscard,
            TrRmOptionalSectionSlots.Create(0x8003, 1, [], new byte[] { 1 })));
        Assert.True(added.TryGetSection(0x8001, out _, out _));
        Assert.False(added.TryGetSection(0x8002, out _, out _));
        Assert.True(added.TryGetSection(0x8003, out _, out _));
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

    private static byte[] ArtifactWithMetadataTables()
    {
        var names = TrRmStringTableWriter.EncodeNames(
            [("A", 0), ("B", 1), ("src/A.terrum", 0)]);
        var artifact = new byte[32 + 4 * 16 + 44 + names.Length + 12 + 8];
        "TRRM"u8.CopyTo(artifact);
        WriteU16(artifact, 4, 1);
        Write(artifact, 8, 32);
        Write(artifact, 12, 4);
        Write(artifact, 16, (uint)artifact.Length);
        Write(artifact, 20, 32);
        var moduleOffset = 96;
        var stringOffset = moduleOffset + 44;
        var refOffset = stringOffset + names.Length;
        var sourceOffset = refOffset + 12;
        WriteU16(artifact, 32, 0x0001);
        Write(artifact, 32 + 4, 1);
        Write(artifact, 32 + 8, (uint)moduleOffset);
        Write(artifact, 32 + 12, 44);
        WriteU16(artifact, 48, 0x0002);
        Write(artifact, 48 + 4, 3);
        Write(artifact, 48 + 8, (uint)stringOffset);
        Write(artifact, 48 + 12, (uint)names.Length);
        WriteU16(artifact, 64, 0x0010);
        Write(artifact, 64 + 4, 1);
        Write(artifact, 64 + 8, (uint)refOffset);
        Write(artifact, 64 + 12, 12);
        WriteU16(artifact, 80, 0x001F);
        Write(artifact, 80 + 4, 1);
        Write(artifact, 80 + 8, (uint)sourceOffset);
        Write(artifact, 80 + 12, 8);
        Write(artifact, moduleOffset, 1);
        Write(artifact, moduleOffset + 4, 1);
        Write(artifact, moduleOffset + 28, 1);
        Write(artifact, moduleOffset + 32, 1);
        Write(artifact, moduleOffset + 36, 1);
        Write(artifact, moduleOffset + 40, 1);
        names.CopyTo(artifact.AsSpan(stringOffset));
        Write(artifact, refOffset, 2);
        Write(artifact, refOffset + 4, 1);
        Write(artifact, sourceOffset, 3);
        return artifact;
    }

    private static string ReadCallableName(byte[] artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x0002, out var stringRows, out var stringCount));
        var strings = new TrRmStringTableReader(stringRows, stringCount);
        Assert.True(index.TryGetSection(0x0001, out var moduleRows, out var moduleCount));
        var modules = new TrRmModuleTableReader(moduleRows, moduleCount, strings, 0, 0, 1, 0);
        var types = new TrRmTypeDefTableReader([], 0, strings, modules, 0, 0, 0, 0);
        Assert.True(index.TryGetSection(0x0011, out var typeRows, out var typeCount));
        var typeSpecs = new TrRmTypeSpecTableReader(typeRows, typeCount, 0, 0, 0, 0);
        Assert.True(index.TryGetSection(0x0017, out var signatureRows, out var signatureCount));
        var signatures = new TrRmMethodSigTableReader(signatureRows, signatureCount, typeSpecs, 0, 0);
        Assert.True(index.TryGetSection(0x0016, out var methodRows, out var methodCount));
        var methods = new TrRmMethodDefTableReader(methodRows, methodCount, strings,
            modules, types, signatures, 0, 0);
        return methods.GetName(1);
    }

    private static byte[] ArtifactWithCallableMetadata(bool includeEmptyBlob = false,
        bool includeBody = false, bool includeSourceMap = false, bool includeOutputState = false)
    {
        var strings = includeSourceMap
            ? TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0), ("src/file", 0)])
            : TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0)]);
        var module = new byte[44];
        Write(module, 0, 1);
        Write(module, 4, 1);
        Write(module, 28, 1);
        Write(module, 32, 1);
        Write(module, 36, 1);
        Write(module, 40, 1);
        byte[] typeSpec = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        if (includeOutputState)
        {
            typeSpec[^1] = 1;
        }

        var method = new byte[28];
        Write(method, 4, 2);
        Write(method, 8, 1);
        Write(method, 12, 2);
        if (includeOutputState)
        {
            Write(method, 16, 1);
            Write(method, 20, 1);
        }
        if (includeBody || includeSourceMap)
        {
            Write(method, 24, 1);
        }

        var signature = new byte[24];
        Write(signature, 0, 1);
        if (includeOutputState)
        {
            Write(signature, 8, 1);
            Write(signature, 12, 1);
        }

        byte[][] sections = includeEmptyBlob
            ? [module, strings, TrRmVariableTableWriter.EncodeBlobs([ReadOnlyMemory<byte>.Empty]),
                typeSpec, method, signature]
            : [module, strings, typeSpec, method, signature];
        ushort[] kinds = includeEmptyBlob
            ? [0x0001, 0x0002, 0x0003, 0x0011, 0x0016, 0x0017]
            : [0x0001, 0x0002, 0x0011, 0x0016, 0x0017];
        if (includeOutputState)
        {
            var param = new byte[16];
            Write(param, 0, 1);
            Write(param, 8, 1);
            param[12] = 2;
            var contract = new byte[24];
            Write(contract, 4, 16);
            Write(contract, 8, 0x1600_0001);
            WriteU16(contract, 12, 2);
            contract[20] = 1;
            sections = [.. sections, param, contract];
            kinds = [.. kinds, 0x0018, 0x001B];
        }

        if (includeSourceMap)
        {
            var source = new byte[8];
            Write(source, 0, 3);
            sections = [.. sections, source];
            kinds = [.. kinds, 0x001F];
        }

        if (includeBody || includeSourceMap)
        {
            sections = [.. sections, TrRmVariableTableWriter.EncodeBodies([new byte[] { 0 }])];
            kinds = [.. kinds, 0x0020];
        }

        if (includeSourceMap)
        {
            var map = new byte[52];
            Write(map, 0, 2);
            WriteU16(map, 4, 0x0020);
            WriteU16(map, 6, 1);
            Write(map, 8, 1);
            Write(map, 12, 1);
            WriteU16(map, 20, 0x001F);
            Write(map, 24, 1);
            Write(map, 36, 1);
            Write(map, 40, 2);
            Write(map, 48, 2);
            sections = [.. sections, map];
            kinds = [.. kinds, 0x7F00];
        }
        var length = 32 + sections.Length * 16;
        foreach (var section in sections)
        {
            length += section.Length;
        }

        var artifact = new byte[length];
        "TRRM"u8.CopyTo(artifact);
        WriteU16(artifact, 4, 1);
        Write(artifact, 8, 32);
        Write(artifact, 12, (uint)sections.Length);
        Write(artifact, 16, (uint)length);
        Write(artifact, 20, 32);
        var offset = 32 + sections.Length * 16;
        for (var entry = 0; entry < sections.Length; entry++)
        {
            var directory = 32 + entry * 16;
            WriteU16(artifact, directory, kinds[entry]);
            Write(artifact, directory + 4,
                entry == 1 ? (includeSourceMap ? 3u : 2u) : 1u);
            if (kinds[entry] == 0x7F00)
            {
                WriteU16(artifact, directory + 2, 2);
            }
            Write(artifact, directory + 8, (uint)offset);
            Write(artifact, directory + 12, (uint)sections[entry].Length);
            sections[entry].CopyTo(artifact.AsSpan(offset));
            offset += sections[entry].Length;
        }

        return artifact;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteU16(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
}
