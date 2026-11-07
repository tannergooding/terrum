// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmRootPinProfileTests
{
    [Test]
    public void SelectsRootPinSchemaBeforeApplyingFeatureAndTargetProfile()
    {
        var artifact = CreateProfileArtifact(includeTargetProfile: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateRootPinRuntimeDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForRootPinRuntimeDraft(artifact));

        Assert.That(tables.Features.Count, Is.EqualTo(1));
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1, TrRmFeatureTableReader.RootPinRuntimeFeatureId,
            TrRmArtifactSchema.RootPinRuntimeDraft), Is.True);
        Assert.That(tables.TargetProfiles.TryGetProfile(
            TrRmTargetProfileTableReader.WindowsX64RootPinProfileId,
            TrRmTargetProfileTableReader.WindowsX64RootPinProfileRuleRevision,
            out var row), Is.True);
        Assert.That(row, Is.EqualTo(1));
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1, TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
            TrRmArtifactSchema.RootPinRuntimeDraft), Is.False);
    }

    [Test]
    public void RootPinFeatureRequiresItsSelectedTargetProfile()
    {
        var artifact = CreateProfileArtifact(includeTargetProfile: false);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateRootPinRuntimeDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForRootPinRuntimeDraft(artifact)));
    }

    [Test]
    public void TargetProfileTwoIsAssignedOnlyInTheSelectedRootPinSchema()
    {
        var row = new TrRmTargetProfileRow(
            TrRmTargetProfileTableReader.WindowsX64RootPinProfileId,
            TrRmTargetProfileTableReader.WindowsX64RootPinProfileRuleRevision);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmTargetProfileTableWriter.Encode([row]));
        var bytes = TrRmTargetProfileTableWriter.Encode([row],
            TrRmArtifactSchema.RootPinRuntimeDraft);
        var profiles = new TrRmTargetProfileTableReader(bytes, 1,
            TrRmArtifactSchema.RootPinRuntimeDraft);

        Assert.That(profiles.TryGetProfile(row.ProfileId, row.RuleRevision,
            out var profileRow), Is.True);
        Assert.That(profileRow, Is.EqualTo(1));
        Assert.That(profiles.IsAssignedProfileReference(1), Is.True);
    }

    [Test]
    public void ReferenceSupportRecordAndF0AreBoundToTheRootPinSchema()
    {
        var entryState = new byte[8];
        entryState[0] = 0x01;
        var referenceSupport =
            TrRmRegisterBodyRecordWriter.EncodeRootPinReferenceSupport(
                sourceKind: 1, resultCarrier: 1, sourceIndex: 0,
                resultRegister: 0, sourceMode: 1, sourceTypeMapIndex: 0,
                resultTypeMapIndex: 1);
        byte[] code = [0xF0, 0x01, 0x01, 0x00, 0x17, 0x00];
        var body = TrRmRegisterBodyWriter.Encode(default, new byte[8],
            default, default, default,
            [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
            [new ReadOnlyMemory<byte>(entryState),
                new ReadOnlyMemory<byte>(referenceSupport)], code,
            schema: TrRmArtifactSchema.RootPinRuntimeDraft);
        var reader = new TrRmRegisterBodyReader(body,
            schema: TrRmArtifactSchema.RootPinRuntimeDraft);

        Assert.That(reader.HasRootPinOperations, Is.True);
        Assert.That(reader.GetRecord(1)[0], Is.EqualTo((byte)0x0D));
        Assert.That(reader.GetBlockCode(0).ToArray(), Is.EqualTo(code));
        _ = Assert.Throws<InvalidDataException>(() => _ = new TrRmRegisterBodyReader(body));
        _ = Assert.Throws<InvalidDataException>(() => _ = new TrRmRegisterBodyReader(body,
            schema: TrRmArtifactSchema.ForeignInvocationDraft));

        var mismatchedRecord =
            TrRmRegisterBodyRecordWriter.EncodeRootPinReferenceSupport(
                sourceKind: 1, resultCarrier: 2, sourceIndex: 0,
                resultRegister: 0, sourceMode: 1, sourceTypeMapIndex: 0,
                resultTypeMapIndex: 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmRegisterBodyWriter.Encode(default, new byte[8], default,
                default, default,
                [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
                [new ReadOnlyMemory<byte>(entryState),
                    new ReadOnlyMemory<byte>(mismatchedRecord)], code,
                schema: TrRmArtifactSchema.RootPinRuntimeDraft));
    }

    [Test]
    public void RootPinBodyUseRequiresFeatureFourteenAtItsUsingModule()
    {
        var artifact = CreateRootPinBodyArtifact(
            includeFeature14: false, includeTargetProfile: false);

        _ = Assert.Throws<NotSupportedException>(
            () => ValidateRootPinMapUsage(artifact));
    }

    [Test]
    public void RootPinBodyAndModuleSelectTheSameProfileBeforeMapUse()
    {
        var artifact = CreateRootPinBodyArtifact(
            includeFeature14: true, includeTargetProfile: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateRootPinRuntimeDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForRootPinRuntimeDraft(artifact));
        var body = tables.Bodies.GetRegisterBody(1, tables.Contracts.Count);

        Assert.That(body.Schema, Is.EqualTo(TrRmArtifactSchema.RootPinRuntimeDraft));
        Assert.That(body.HasRootPinOperations, Is.True);
    }

    [Test]
    public void OptionalSectionRewritePreservesRootPinSchemaSelection()
    {
        var artifact = CreateProfileArtifact(includeTargetProfile: true);
        var extension = TrRmOptionalSectionSlots.Create(0x8001, 1, [],
            "Z"u8.ToArray());
        var inserted = TrRmOptionalSectionSlots.InsertInArtifact(artifact,
            extension, TrRmArtifactSchema.RootPinRuntimeDraft);
        var replaced = TrRmOptionalSectionSlots.ReplaceInArtifact(inserted,
            0x8001, [], TrRmArtifactSchema.RootPinRuntimeDraft);

        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.ValidateRootPinRuntimeDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForRootPinRuntimeDraft(replaced)));
    }

    private static byte[] CreateProfileArtifact(bool includeTargetProfile)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("root-pin-module", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 2);
        var featuresBytes = TrRmFeatureTableWriter.Encode(
            [TrRmFeatureRow.RootPinRuntimeCandidate],
            TrRmArtifactSchema.RootPinRuntimeDraft);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1),
        ], strings, 0, 0, 0, 0, 1);
        var sections = new System.Collections.Generic.List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 2, stringBytes),
            new(0x0004, 0, 1, featuresBytes),
        };
        if (includeTargetProfile)
        {
            sections.Add(new TrRmSection(0x0048, 0, 1,
                TrRmTargetProfileTableWriter.Encode([
                    new TrRmTargetProfileRow(
                        TrRmTargetProfileTableReader.WindowsX64RootPinProfileId,
                        TrRmTargetProfileTableReader.WindowsX64RootPinProfileRuleRevision),
                ], TrRmArtifactSchema.RootPinRuntimeDraft)));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.RootPinRuntimeDraft);
    }

    private static void ValidateRootPinMapUsage(byte[] artifact)
    {
        var tables = TrRmStructuralMetadataValidator
            .ValidateRootPinRuntimeDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForRootPinRuntimeDraft(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    private static byte[] CreateRootPinBodyArtifact(bool includeFeature14,
        bool includeTargetProfile)
    {
        var baseArtifact = TrRmRegisterBodyTestData.StandardInt32EntryArtifact(0x0E);
        var baseIndex = new TrRmArtifactIndex(baseArtifact);
        var typeMap = new byte[2 * sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(typeMap, 0xF200_0008);
        BinaryPrimitives.WriteUInt32LittleEndian(
            typeMap.AsSpan(sizeof(uint)), 0xF200_0008);
        var entryState = new byte[8];
        entryState[0] = 0x01;
        var referenceSupport =
            TrRmRegisterBodyRecordWriter.EncodeRootPinReferenceSupport(
                sourceKind: 1, resultCarrier: 1, sourceIndex: 0,
                resultRegister: 0, sourceMode: 1, sourceTypeMapIndex: 0,
                resultTypeMapIndex: 1);
        byte[] code = [0xF0, 0x01, 0x01, 0x00, 0x17, 0x00];
        var body = TrRmRegisterBodyWriter.Encode(default, typeMap, default,
            default, default, [new TrRmRegisterBodyBlock(0, 6, 0)],
            [new ReadOnlyMemory<byte>(entryState),
                new ReadOnlyMemory<byte>(referenceSupport)], code,
            schema: TrRmArtifactSchema.RootPinRuntimeDraft);

        var sections = new List<TrRmSection>(baseIndex.SectionCount + 2);
        for (var sectionIndex = 0; sectionIndex < baseIndex.SectionCount; sectionIndex++)
        {
            baseIndex.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            var sectionBytes = bytes.ToArray();
            if (kind == 0x0001)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(sectionBytes.AsSpan(24),
                    includeFeature14 ? 1u : 0u);
                BinaryPrimitives.WriteUInt32LittleEndian(sectionBytes.AsSpan(28),
                    includeFeature14 ? 1u : 0u);
            }
            else if (kind == 0x0020)
            {
                sectionBytes = TrRmVariableTableWriter.EncodeBodies([body]);
            }
            sections.Add(new TrRmSection(kind, flags, (uint)recordCount,
                sectionBytes));
        }

        if (includeFeature14)
        {
            sections.Add(new TrRmSection(0x0004, 0, 1,
                TrRmFeatureTableWriter.Encode(
                    [TrRmFeatureRow.RootPinRuntimeCandidate],
                    TrRmArtifactSchema.RootPinRuntimeDraft)));
        }
        if (includeTargetProfile)
        {
            sections.Add(new TrRmSection(0x0048, 0, 1,
                TrRmTargetProfileTableWriter.Encode([
                    new TrRmTargetProfileRow(
                        TrRmTargetProfileTableReader.WindowsX64RootPinProfileId,
                        TrRmTargetProfileTableReader.WindowsX64RootPinProfileRuleRevision),
                ], TrRmArtifactSchema.RootPinRuntimeDraft)));
        }

        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.RootPinRuntimeDraft);
    }
}
