// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmArtifactIndexTests
{
    [Test]
    public void LocatesRequiredSectionsAndResolvesModuleIdentity()
    {
        var artifact = MinimalArtifact();
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0002, out var stringSection, out var stringCount), Is.True);
        var strings = new TrRmStringTableReader(stringSection, stringCount);
        Assert.That(index.TryGetSection(0x0004, out _, out _), Is.False);
        Assert.That(index.TryGetSection(0x0001, out var moduleSection, out var moduleCount), Is.True);
        var modules = new TrRmModuleTableReader(moduleSection, moduleCount, strings, 0, 0, 0, 0);
        Assert.That(modules.GetIdentity(1), Is.EqualTo("A"));
        Assert.That(index.TryGetSection(0x0010, out var absent, out var absentCount), Is.False);
        Assert.That(absent.IsEmpty, Is.True);
        Assert.That(absentCount, Is.EqualTo(0));
    }

    [Test]
    public void StandardAndDevelopmentProfilesDoNotAliasSectionLayouts()
    {
        var sharedSections = new TrRmSection[]
        {
            new(0x0001, 0, 1, new byte[48]),
            new(0x0002, 0, 1, TrRmStringTableWriter.EncodeNames([("A", 0)])),
        };
        var standard = TrRmArtifactWriter.Encode([
            .. sharedSections,
            new(0x0030, 0, 1, new byte[8]),
            new(0x0031, 0, 1, new byte[16]),
            new(0x0032, 0, 1, new byte[12]),
            new(0x0033, 0, 1, new byte[16]),
        ]);
        var standardIndex = new TrRmArtifactIndex(standard);
        Assert.That(standardIndex.TryGetSection(0x0030, out var constantSources,
            out var constantSourceCount), Is.True);
        Assert.That(constantSources.Length, Is.EqualTo(8));
        Assert.That(constantSourceCount, Is.EqualTo(1));
        Assert.Throws<InvalidDataException>(() => TrRmArtifactIndex.ForDevelopment(standard));

        var development = TrRmArtifactWriter.EncodeDevelopment([
            .. sharedSections,
            new(0x0030, 0, 1, new byte[16]),
            new(0x0031, 0, 1, new byte[8]),
            new(0x0032, 0, 1, new byte[16]),
            new(0x0033, 0, 1, new byte[8]),
        ]);
        var developmentIndex = TrRmArtifactIndex.ForDevelopment(development);
        Assert.That(developmentIndex.TryGetSection(0x0030, out var nativeImports,
            out var nativeImportCount), Is.True);
        Assert.That(nativeImports.Length, Is.EqualTo(16));
        Assert.That(nativeImportCount, Is.EqualTo(1));
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(development));
    }

    [Test]
    public void IndexesEveryAssignedStandardFixedRowStride()
    {
        (ushort Kind, int Stride)[] assigned =
        [
            (0x0010, 12), (0x0013, 44), (0x0014, 16), (0x0015, 28),
            (0x0016, 28), (0x0017, 24), (0x0018, 16), (0x0019, 32),
            (0x001A, 12), (0x001C, 12), (0x001D, 8), (0x001E, 20),
            (0x001F, 8), (0x0021, 16), (0x0022, 16), (0x0023, 12),
            (0x0024, 12), (0x0025, 48), (0x0026, 12), (0x0027, 12),
            (0x0028, 32), (0x0029, 20), (0x002A, 20), (0x002B, 12),
            (0x002C, 12), (0x002D, 20), (0x002E, 16), (0x002F, 40),
            (0x0030, 8), (0x0031, 16), (0x0032, 12), (0x0033, 16),
            (0x0034, 20), (0x0035, 16), (0x0036, 12), (0x0037, 8),
            (0x0038, 16), (0x0039, 8), (0x003A, 8), (0x003B, 16),
            (0x003C, 8), (0x003D, 12), (0x003E, 16), (0x003F, 24),
            (0x0040, 12), (0x0041, 12), (0x0042, 80), (0x0043, 8),
            (0x0044, 8), (0x0045, 20), (0x0046, 20), (0x0047, 24),
            (0x0048, 8), (0x0049, 16), (0x004A, 4),
        ];
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, new byte[48]),
            new(0x0002, 0, 1, TrRmStringTableWriter.EncodeNames([("A", 0)])),
            new(0x0004, 0, 1,
                TrRmFeatureTableWriter.Encode([new TrRmFeatureRow(1, 1)])),
        };
        foreach (var (kind, stride) in assigned)
        {
            sections.Add(new TrRmSection(kind, 0, 1, new byte[stride]));
        }
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));

        var index = new TrRmArtifactIndex(TrRmArtifactWriter.Encode(sections));
        foreach (var (kind, stride) in assigned)
        {
            Assert.That(index.TryGetSection(kind, out var section, out var count), Is.True);
            Assert.That(section.Length, Is.EqualTo(stride), $"section 0x{kind:X4}");
            Assert.That(count, Is.EqualTo(1), $"section 0x{kind:X4}");
        }
    }

    [Test]
    public void FindsSectionsAtDirectoryBoundariesAndRejectsMissingKinds()
    {
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, new byte[48]),
            new TrRmSection(0x0002, 0, 1, TrRmStringTableWriter.EncodeNames([("A", 0)])),
            new TrRmSection(0x8000, 1, 1, new byte[] { 1 }),
            new TrRmSection(0xFFFF, 3, 1, new byte[] { 2 }),
        ]);
        var index = new TrRmArtifactIndex(artifact);

        Assert.That(index.TryGetSection(0x0001, out var first, out var firstCount), Is.True);
        Assert.That(first.Length, Is.EqualTo(48));
        Assert.That(firstCount, Is.EqualTo(1));
        Assert.That(index.TryGetSection(0xFFFF, out var last, out var lastCount), Is.True);
        Assert.That(last[0], Is.EqualTo((byte)2));
        Assert.That(lastCount, Is.EqualTo(1));
        foreach (var missing in new ushort[] { 0, 3, 0x7FFF, 0x8001 })
        {
            Assert.That(index.TryGetSection(missing, out var absent, out var count), Is.False);
            Assert.That(absent.IsEmpty, Is.True);
            Assert.That(count, Is.EqualTo(0));
        }
    }

    [Test]
    public void ReadsExactFeaturePairsAndModuleRange()
    {
        var artifact = FeatureArtifact(1, 1,
            TrRmFeatureTableWriter.Encode([TrRmFeatureRow.LayoutQueries]));
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0004, out var featureBytes, out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(features.GetFeature(1), Is.EqualTo(TrRmFeatureRow.LayoutQueries));
        Assert.That(features.TryGetFeature(TrRmFeatureTableReader.LayoutQueriesFeatureId,
            out var feature), Is.True);
        Assert.That(feature.RuleRevision, Is.EqualTo(TrRmFeatureTableReader.RuleRevision));

        Assert.That(index.TryGetSection(0x0002, out var stringBytes, out var stringCount), Is.True);
        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        Assert.That(index.TryGetSection(0x0001, out var moduleBytes, out var moduleCount), Is.True);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount, strings,
            0, 0, 0, 0, featureCount);
        Assert.That(modules.GetFeatureRange(1), Is.EqualTo((1, 1)));
    }

    [Test]
    public void OrdinaryFeatureRowsPermitReorderingAndIdenticalDuplicates()
    {
        byte[] section =
        [
            13, 0, 0, 0, 1, 0, 0, 0,
            1, 0, 0, 0, 1, 0, 0, 0,
            13, 0, 0, 0, 1, 0, 0, 0,
        ];
        var features = new TrRmFeatureTableReader(section, 3);
        Assert.That(features.GetFeature(1), Is.EqualTo(TrRmFeatureRow.LayoutQueries));
        Assert.That(features.GetFeature(2), Is.EqualTo(TrRmFeatureRow.NativeBoundaryContracts));
        Assert.That(features.TryGetFeature(13, out var feature), Is.True);
        Assert.That(feature, Is.EqualTo(TrRmFeatureRow.LayoutQueries));

        byte[] conflictingRows =
        [
            13, 0, 0, 0, 1, 0, 0, 0,
            13, 0, 0, 0, 2, 0, 0, 0,
        ];
        var conflictingFeatures = new TrRmFeatureTableReader(conflictingRows, 2);
        Assert.That(conflictingFeatures.TryGetFeature(13, out _), Is.False);
    }

    [Test]
    public void ValidatesExactFeatureManifestClosure()
    {
        var reordered = FeatureArtifact(1, 2,
        [
            2, 0, 0, 0, 1, 0, 0, 0,
            1, 0, 0, 0, 1, 0, 0, 0,
        ]);
        TrRmStructuralMetadataValidator.Validate(reordered);

        var duplicate = FeatureArtifact(1, 2,
        [
            13, 0, 0, 0, 1, 0, 0, 0,
            13, 0, 0, 0, 1, 0, 0, 0,
        ]);
        TrRmStructuralMetadataValidator.Validate(duplicate);

        var conflictingRevision = FeatureArtifact(1, 2,
        [
            13, 0, 0, 0, 1, 0, 0, 0,
            13, 0, 0, 0, 2, 0, 0, 0,
        ]);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(conflictingRevision));

        var unassignedFeature = FeatureArtifact(1, 1,
            [8, 0, 0, 0, 1, 0, 0, 0]);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(unassignedFeature));
    }

    [Test]
    public void CopyOriginDraftFeatureIsSelectedOutOfBandAndHasNoPrerequisites()
    {
        var featureBytes = TrRmFeatureTableWriter.Encode(
            [new TrRmFeatureRow(15, 1)], TrRmArtifactSchema.CopyOriginDraft);
        Assert.Throws<InvalidDataException>(() =>
            TrRmFeatureTableWriter.Encode([new TrRmFeatureRow(15, 1)]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmFeatureTableWriter.Encode([new TrRmFeatureRow(15, 2)],
                TrRmArtifactSchema.CopyOriginDraft));

        var artifact = FeatureArtifact(1, 1, featureBytes);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(artifact));
        var index = TrRmArtifactIndex.ForCopyOriginDraft(artifact);
        _ = TrRmStructuralMetadataValidator.ValidateCopyOriginDraftAndGetExecutionTables(index);

        Assert.That(index.TryGetSection(0x0002, out var stringBytes,
            out var stringCount), Is.True);
        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        Assert.That(index.TryGetSection(0x0001, out var moduleBytes,
            out var moduleCount), Is.True);
        Assert.That(index.TryGetSection(0x0004, out var selectedFeatureBytes,
            out var selectedFeatureCount), Is.True);
        var features = new TrRmFeatureTableReader(selectedFeatureBytes,
            selectedFeatureCount, TrRmArtifactSchema.CopyOriginDraft);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount,
            strings, 0, 0, 0, 0, selectedFeatureCount);

        Assert.That(TrRmFeatureManifestValidator.Requires(features, modules,
            1, 15), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(features, modules,
            1, 13), Is.False);
        var standardFeatures = new TrRmFeatureTableReader(selectedFeatureBytes,
            selectedFeatureCount);
        var standardRejectedCandidate = false;
        try
        {
            _ = TrRmFeatureManifestValidator.Requires(standardFeatures, modules, 1, 15);
        }
        catch (InvalidDataException)
        {
            standardRejectedCandidate = true;
        }
        Assert.That(standardRejectedCandidate, Is.True);
    }

    [Test]
    public void ForeignInvocationFeatureIsSelectedOutOfBandAndComposesOutputClosure()
    {
        var featureBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.NativeOutputCellsCandidate,
        ], TrRmArtifactSchema.ForeignInvocationDraft);
        Assert.Throws<InvalidDataException>(() =>
            TrRmFeatureTableWriter.Encode([TrRmFeatureRow.NativeOutputCellsCandidate]));

        var artifact = FeatureArtifact(1, 1, featureBytes);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(artifact));
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        _ = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);

        Assert.That(index.TryGetSection(0x0002, out var stringBytes,
            out var stringCount), Is.True);
        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        Assert.That(index.TryGetSection(0x0001, out var moduleBytes,
            out var moduleCount), Is.True);
        Assert.That(index.TryGetSection(0x0004, out var selectedFeatureBytes,
            out var selectedFeatureCount), Is.True);
        var features = new TrRmFeatureTableReader(selectedFeatureBytes,
            selectedFeatureCount, TrRmArtifactSchema.ForeignInvocationDraft);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount,
            strings, 0, 0, 0, 0, selectedFeatureCount);

        Assert.That(TrRmFeatureManifestValidator.Requires(features, modules,
            1, TrRmFeatureTableReader.NativeOutputCellsCandidateFeatureId), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(features, modules,
            1, TrRmFeatureTableReader.NativeBoundaryContractsFeatureId), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(features, modules,
            1, TrRmFeatureTableReader.RootlessNativeValuesFeatureId), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(features, modules,
            1, TrRmFeatureTableReader.PublishedPredicatesFeatureId), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(features, modules,
            1, TrRmFeatureTableReader.ForeignInvocationContractsCandidateFeatureId), Is.False);
    }

    [Test]
    public void DerivesPrerequisiteClosurePerModule()
    {
        var artifact = FeatureArtifact(1, 1,
            [12, 0, 0, 0, 1, 0, 0, 0]);
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0002, out var stringBytes, out var stringCount), Is.True);
        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        Assert.That(index.TryGetSection(0x0001, out var moduleBytes, out var moduleCount), Is.True);
        Assert.That(index.TryGetSection(0x0004, out var featureBytes, out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount, strings,
            0, 0, 0, 0, featureCount);

        Assert.That(TrRmFeatureManifestValidator.Requires(features, modules, 1, 12), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(features, modules, 1, 10), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(features, modules, 1, 11), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(features, modules, 1, 9), Is.False);
    }

    [Test]
    public void InspectsUnsupportedFeaturesAndRejectsMalformedPayloadsAndModuleRanges()
    {
        var feature = TrRmFeatureTableWriter.Encode([TrRmFeatureRow.LayoutQueries]);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(
            FeatureArtifact(0, 1, feature)));
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(
            FeatureArtifact(1, 2, feature)));
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(
            FeatureArtifact(0, 0, [0, 0, 0, 0, 1, 0, 0, 0])));
        _ = new TrRmArtifactIndex(
            FeatureArtifact(0, 0, [4, 0, 0, 0, 1, 0, 0, 0]));
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(
            FeatureArtifact(0, 0, [1, 0, 0, 0, 0, 0, 0, 0])));
        _ = new TrRmArtifactIndex(
            FeatureArtifact(0, 0, [1, 0, 0, 0, 9, 0, 0, 0]));
        var unsupportedRevision = FeatureArtifact(1, 1, [1, 0, 0, 0, 9, 0, 0, 0]);
        _ = new TrRmArtifactIndex(unsupportedRevision);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(unsupportedRevision));
        var unassignedFeature = FeatureArtifact(1, 1, [8, 0, 0, 0, 1, 0, 0, 0]);
        _ = new TrRmArtifactIndex(unassignedFeature);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(unassignedFeature));
        _ = new TrRmFeatureTableReader(
            [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0], 2);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(
            FeatureArtifact(0, 0, [1, 0, 0, 0, 1])));
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(
            FeatureArtifact(1, 1, null)));
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(
            FeatureArtifact(2, 1, feature)));
    }

    [Test]
    public void IndexesModuleReferencesAndSourceFilesThroughOneArtifact()
    {
        var artifact = ArtifactWithMetadataTables();
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0002, out var nameBytes, out var nameCount), Is.True);
        var names = new TrRmStringTableReader(nameBytes, nameCount);
        Assert.That(index.TryGetSection(0x0001, out var moduleBytes, out var moduleCount), Is.True);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount, names, 1, 0, 0, 1);
        Assert.That(index.TryGetSection(0x0010, out var refBytes, out var refCount), Is.True);
        var refs = new TrRmModuleRefTableReader(refBytes, refCount, names, modules);
        Assert.That(refs.GetProducerIdentity(1), Is.EqualTo("B"));
        Assert.That(refs.GetOwnerModule(1), Is.EqualTo(1));
        Assert.That(index.TryGetSection(0x001F, out var sourceBytes, out var sourceCount), Is.True);
        var sources = new TrRmSourceFileTableReader(sourceBytes, sourceCount, names, modules);
        Assert.That(sources.GetPath(1), Is.EqualTo("src/A.terrum"));
        Assert.That(sources.GetOwnerModule(1), Is.EqualTo(1));
    }

    [Test]
    public void ComposesFramedStringTypeSpecSignatureAndCallableMetadata()
    {
        var artifact = ArtifactWithCallableMetadata();
        Assert.That(ReadCallableName(artifact), Is.EqualTo("Run"));

        var namesLength = TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0)]).Length;
        var methodOffset = 32 + 5 * 16 + 48 + namesLength + 11;
        artifact[methodOffset + 12] = 3;
        _ = new TrRmArtifactIndex(artifact);
        Assert.Throws<InvalidDataException>(() => ReadCallableName(artifact));
    }

    [Test]
    public void StructuralValidatorComposesAllPresentKnownMetadataTables()
    {
        TrRmStructuralMetadataValidator.Validate(MinimalArtifact());
        TrRmStructuralMetadataValidator.Validate(ArtifactWithMetadataTables());
        TrRmStructuralMetadataValidator.Validate(ArtifactWithCallableMetadata());
        var callableWithoutBody = ArtifactWithCallableMetadata();
        TrRmStructuralMetadataValidator.ValidateCompactLinear(callableWithoutBody);
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(callableWithoutBody);
        TrRmStructuralMetadataValidator.ValidateCompact(callableWithoutBody);
        var withBlob = ArtifactWithCallableMetadata(includeEmptyBlob: true);
        TrRmStructuralMetadataValidator.Validate(withBlob);
        TrRmStructuralMetadataValidator.Validate(ArtifactWithCallableMetadata(includeBody: true));
        TrRmStructuralMetadataValidator.Validate(ArtifactWithCallableMetadata(includeSourceMap: true));
        TrRmStructuralMetadataValidator.Validate(ArtifactWithCallableMetadata(includeOutputState: true));
        TrRmStructuralMetadataValidator.Validate(ArtifactWithCallableMetadata(
            includeOutputState: true, doesNotReturn: true));
        TrRmStructuralMetadataValidator.Validate(WithReferenceSlottedExtension());

        var ambiguousOutput = ArtifactWithCallableMetadata(
            includeOutputState: true, conflictingOutputStates: true);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(ambiguousOutput));

        var invalidOutput = ArtifactWithCallableMetadata(includeOutputState: true);
        var contractOffset = GetSectionDataOffset(invalidOutput, 0x001B);
        invalidOutput[contractOffset + 48] = 2;
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(invalidOutput));

        var invalidBlob = ArtifactWithCallableMetadata(includeEmptyBlob: true);
        var blobOffset = 32 + 6 * 16 + 48
            + TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0)]).Length;
        invalidBlob[blobOffset + 4] = 1;
        _ = new TrRmArtifactIndex(invalidBlob);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(invalidBlob));

        var invalidBody = ArtifactWithCallableMetadata(includeBody: true);
        invalidBody[^1] = 0xCC;
        _ = new TrRmArtifactIndex(invalidBody);
        TrRmStructuralMetadataValidator.Validate(invalidBody);
        Assert.Throws<InvalidDataException>(() =>
            ReadSelectedRegisterBody(invalidBody));

        var invalidMap = ArtifactWithCallableMetadata(includeSourceMap: true);
        invalidMap[^12] = 1;
        _ = new TrRmArtifactIndex(invalidMap);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(invalidMap));

        var invalid = ArtifactWithCallableMetadata();
        var stringsLength = TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0)]).Length;
        invalid[32 + 5 * 16 + 48 + stringsLength + 11 + 12] = 3;
        _ = new TrRmArtifactIndex(invalid);
        Assert.Throws<InvalidDataException>(() => TrRmStructuralMetadataValidator.Validate(invalid));
    }
    [TestCase(0,(byte)(4))]
    [TestCase(1,(byte)(0xFF))]
    [TestCase(10,(byte)(1))]
    [TestCase(11,(byte)(1))]
    public void StructuralValidatorRejectsMalformedUnreferencedStringRows(int rowOffset,
        byte replacement)
    {
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0), ("Unused", 0)]);
        var secondRow = 12 + (int)BinaryPrimitives.ReadUInt32LittleEndian(strings.AsSpan(4));
        strings[secondRow + rowOffset] = replacement;
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, MinimalArtifact().AsSpan(64, 48).ToArray()),
            new TrRmSection(0x0002, 0, 2, strings),
        ]);
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0002, out var section, out var count), Is.True);
        Assert.That(new TrRmStringTableReader(section, count).DecodeName(1), Is.EqualTo("A"));
        Assert.Throws<InvalidDataException>(() => TrRmStructuralMetadataValidator.Validate(artifact));
    }

    [Test]
    public void StructuralValidatorPermitsUnreferencedStringContentWithNul()
    {
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0), ("Unused", 0)]);
        var secondRow = 12 + (int)BinaryPrimitives.ReadUInt32LittleEndian(strings.AsSpan(4));
        strings[secondRow + 4] = 0;
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, MinimalArtifact().AsSpan(64, 48).ToArray()),
            new TrRmSection(0x0002, 0, 2, strings),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0002, out var section, out var count), Is.True);
        var sectionBytes = section.ToArray();
        Assert.Throws<InvalidDataException>(() =>
            new TrRmStringTableReader(sectionBytes, count).DecodeName(2));
    }
    [TestCase(0,(byte)(0))]
    [TestCase(4,(byte)(2))]
    [TestCase(6,(byte)(1))]
    [TestCase(8,(byte)(0))]
    [TestCase(12,(byte)(0))]
    [TestCase(16,(byte)(0))]
    [TestCase(20,(byte)(0))]
    [TestCase(24,(byte)(1))]
    [TestCase(32 + 2,(byte)(1))]
    [TestCase(32 + 4,(byte)(0))]
    [TestCase(32 + 12,(byte)(43))]
    [TestCase(48,(byte)(1))]
    [TestCase(48 + 2,(byte)(1))]
    [TestCase(48 + 8,(byte)(109))]
    public void RejectsMalformedHeaderAndDirectory(int byteIndex, byte replacement)
    {
        var artifact = MinimalArtifact();
        artifact[byteIndex] = replacement;
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(artifact));
    }

    [Test]
    public void RejectsOverlapsNonzeroGapsAndTrailingBytes()
    {
        var overlap = MinimalArtifact();
        Write(overlap, 48 + 8, 104);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(overlap));

        var trailing = new byte[129];
        MinimalArtifact().CopyTo(trailing, 0);
        Write(trailing, 16, (uint)trailing.Length);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(trailing));

        var gap = new byte[132];
        MinimalArtifact().AsSpan(0, 112).CopyTo(gap);
        MinimalArtifact().AsSpan(112).CopyTo(gap.AsSpan(116));
        Write(gap, 16, (uint)gap.Length);
        Write(gap, 48 + 8, 116);
        gap[113] = 1;
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(gap));
    }

    [Test]
    public void PermitsZeroFilledAlignmentGaps()
    {
        var original = MinimalArtifact();
        var gap = new byte[132];
        original.AsSpan(0, 112).CopyTo(gap);
        original.AsSpan(112).CopyTo(gap.AsSpan(116));
        Write(gap, 16, (uint)gap.Length);
        Write(gap, 48 + 8, 116);
        var index = new TrRmArtifactIndex(gap);
        Assert.That(index.TryGetSection(0x0002, out var strings, out var count), Is.True);
        Assert.That(new TrRmStringTableReader(strings, count).DecodeName(1), Is.EqualTo("A"));
    }

    [Test]
    public void AcceptsOutOfOrderSectionPayloads()
    {
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, new byte[48]),
            new TrRmSection(0x8001, 1, 1, new byte[] { 0x11 }),
            new TrRmSection(0x8002, 1, 1, new byte[] { 0x22 }),
        ]);
        var firstStart = BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(56));
        var secondStart = BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(72));
        Write(artifact, 56, secondStart);
        Write(artifact, 72, firstStart);

        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x8001, out var first, out _), Is.True);
        Assert.That(index.TryGetSection(0x8002, out var second, out _), Is.True);
        Assert.That(first[0], Is.EqualTo((byte)0x22));
        Assert.That(second[0], Is.EqualTo((byte)0x11));
    }

    [Test]
    public void ValidatesReferenceSlottedExtensionsAndTargetRows()
    {
        var artifact = WithReferenceSlottedExtension();
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x8000, out var section, out var count), Is.True);
        Assert.That(count, Is.EqualTo(1));
        Assert.That(section.Length, Is.EqualTo(21));

        var missingTarget = WithReferenceSlottedExtension();
        Write(missingTarget, 148 + 4, 2);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(missingTarget));

        var absentTarget = WithReferenceSlottedExtension();
        WriteU16(absentTarget, 148, 0x0010);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(absentTarget));

        var danglingBody = WithReferenceSlottedExtension();
        WriteU16(danglingBody, 148, 0x0020);
        WriteU16(danglingBody, 150, 1);
        Write(danglingBody, 156, 1);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(danglingBody));
    }
    [TestCase((ushort)1)]
    [TestCase((ushort)2)]
    [TestCase((ushort)3)]
    public void EnumeratesOrderedSectionsIncludingOptionalExtensionPolicies(ushort policy)
    {
        var artifact = WithReferenceSlottedExtension();
        WriteU16(artifact, 66, policy);
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.SectionCount, Is.EqualTo(3));
        var preserved = new TrRmSection[index.SectionCount];
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags, out var payload, out var count);
            preserved[entry] = new TrRmSection(kind, flags, (uint)count, payload.ToArray());
        }

        Assert.That(preserved[2].Kind, Is.EqualTo((ushort)0x8000));
        Assert.That(preserved[2].Flags, Is.EqualTo(policy));
        Assert.That(TrRmArtifactWriter.Encode(preserved), Is.EqualTo(artifact));
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

    private static void ReadSelectedRegisterBody(byte[] artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        if (!index.TryGetSection(0x0020, out var bodyBytes, out var bodyCount))
        {
            throw new InvalidDataException("Body section is absent.");
        }

        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        _ = bodies.GetRegisterBody(1);
    }

    [Test]
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
        Assert.That(sections[2].Bytes.Span[20], Is.EqualTo(extension.Bytes.Span[20]));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(extension.Bytes.Span[4..]), Is.EqualTo((ushort)0x0002));
        var remapped = new TrRmArtifactIndex(TrRmArtifactWriter.Encode(sections));
        Assert.That(remapped.TryGetSection(0x8000, out var payload, out _), Is.True);
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]), Is.EqualTo((ushort)0x0001));
        Assert.That(payload[20], Is.EqualTo(extension.Bytes.Span[20]));

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
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(148)), Is.EqualTo((ushort)0x0002));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(atomic.AsSpan(148)), Is.EqualTo((ushort)0x0001));
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

    [Test]
    public void InsertsSlottedExtensionWithoutMutatingExistingArtifact()
    {
        var baseArtifact = MinimalArtifact();
        var framed = WithReferenceSlottedExtension();
        var existing = new TrRmArtifactIndex(framed);
        Assert.That(existing.TryGetSection(0x8000, out var payload, out var count), Is.True);
        var section = new TrRmSection(0x8000, 2, (uint)count, payload.ToArray());
        var authored = TrRmOptionalSectionSlots.Create(0x8000, 1,
            [new TrRmReferenceSlot(0x0002, 0, 1, 0, 0)], new byte[] { 0 });
        Assert.That(authored.Bytes.ToArray(), Is.EqualTo(section.Bytes.ToArray()));
        Assert.That(TrRmOptionalSectionSlots.InsertInArtifact(baseArtifact, authored), Is.EqualTo(framed));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(baseArtifact.AsSpan(12)), Is.EqualTo(2u));

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

    [Test]
    public void OptionalSlotRewritesRetainTheSelectedDevelopmentSchema()
    {
        var development = TrRmArtifactWriter.EncodeDevelopment([
            new TrRmSection(0x0001, 0, 1, new byte[48]),
            new TrRmSection(0x0002, 0, 1,
                TrRmStringTableWriter.EncodeNames([("A", 0)])),
            new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([new byte[] { 0x04 }])),
        ]);
        var slots = new[] { new TrRmReferenceSlot(0x0020, 1, 1, 1, 0) };
        var extension = TrRmOptionalSectionSlots.Create(0x8000, 1, slots,
            new byte[] { 0xA5 });

        var inserted = TrRmOptionalSectionSlots.InsertInDevelopmentArtifact(
            development, extension);
        var developmentIndex = TrRmArtifactIndex.ForDevelopment(inserted);
        Assert.That(developmentIndex.TryGetSection(0x8000, out _, out _), Is.True);
        Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(inserted));
        Assert.Throws<InvalidDataException>(() =>
            TrRmOptionalSectionSlots.InsertInArtifact(development, extension));

        var replaced = TrRmOptionalSectionSlots.ReplaceInDevelopmentArtifact(
            inserted, 0x8000, slots);
        Assert.That(TrRmArtifactIndex.ForDevelopment(replaced)
            .TryGetSection(0x8000, out _, out _), Is.True);
    }

    [Test]
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
        Assert.That(remapped.TryGetSection(0x8001, out _, out _), Is.True);
        Assert.That(remapped.TryGetSection(0x8002, out _, out _), Is.False);
        Assert.That(remapped.SectionCount, Is.EqualTo(4));

        var added = new TrRmArtifactIndex(TrRmOptionalSectionSlots.InsertInArtifact(withDiscard,
            TrRmOptionalSectionSlots.Create(0x8003, 1, [], new byte[] { 1 })));
        Assert.That(added.TryGetSection(0x8001, out _, out _), Is.True);
        Assert.That(added.TryGetSection(0x8002, out _, out _), Is.False);
        Assert.That(added.TryGetSection(0x8003, out _, out _), Is.True);
    }
    [TestCase((ushort)1)]
    [TestCase((ushort)3)]
    public void SkipsNonsemanticExtensionsUnderTheirRegisteredPolicy(ushort policy)
    {
        var artifact = WithReferenceSlottedExtension();
        WriteU16(artifact, 66, policy);
        Assert.That(new TrRmArtifactIndex(artifact).TryGetSection(0x8000, out _, out var count), Is.True);
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
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
        var artifact = new byte[32 + 2 * 16 + 48 + strings.Length];
        "TRRM"u8.CopyTo(artifact);
        WriteU16(artifact, 4, 1);
        Write(artifact, 8, 32);
        Write(artifact, 12, 2);
        Write(artifact, 16, (uint)artifact.Length);
        Write(artifact, 20, 32);
        WriteU16(artifact, 32, 0x0001);
        Write(artifact, 32 + 4, 1);
        Write(artifact, 32 + 8, 64);
        Write(artifact, 32 + 12, 48);
        WriteU16(artifact, 48, 0x0002);
        Write(artifact, 48 + 4, 1);
        Write(artifact, 48 + 8, 112);
        Write(artifact, 48 + 12, (uint)strings.Length);
        Write(artifact, 64, 1);
        Write(artifact, 64 + 4, 1);
        Write(artifact, 64 + 32, 1);
        Write(artifact, 64 + 36, 1);
        Write(artifact, 64 + 40, 1);
        Write(artifact, 64 + 44, 1);
        strings.CopyTo(artifact.AsSpan(112));
        return artifact;
    }

    private static byte[] FeatureArtifact(uint featureFirst, uint moduleFeatureCount,
        byte[]? featureBytes)
    {
        var source = MinimalArtifact();
        var module = source.AsSpan(64, 48).ToArray();
        Write(module, 24, featureFirst);
        Write(module, 28, moduleFeatureCount);
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0)]);
        var sectionCount = featureBytes is null ? 2 : 3;
        var moduleOffset = 32 + sectionCount * 16;
        var stringOffset = moduleOffset + module.Length;
        var featureOffset = stringOffset + strings.Length;
        var artifact = new byte[featureOffset + (featureBytes?.Length ?? 0)];
        "TRRM"u8.CopyTo(artifact);
        WriteU16(artifact, 4, 1);
        Write(artifact, 8, 32);
        Write(artifact, 12, (uint)sectionCount);
        Write(artifact, 16, (uint)artifact.Length);
        Write(artifact, 20, 32);

        WriteU16(artifact, 32, 0x0001);
        Write(artifact, 32 + 4, 1);
        Write(artifact, 32 + 8, (uint)moduleOffset);
        Write(artifact, 32 + 12, (uint)module.Length);
        WriteU16(artifact, 48, 0x0002);
        Write(artifact, 48 + 4, 1);
        Write(artifact, 48 + 8, (uint)stringOffset);
        Write(artifact, 48 + 12, (uint)strings.Length);
        module.CopyTo(artifact.AsSpan(moduleOffset));
        strings.CopyTo(artifact.AsSpan(stringOffset));
        if (featureBytes is not null)
        {
            WriteU16(artifact, 64, 0x0004);
            Write(artifact, 64 + 4, (uint)Math.Max(1, featureBytes.Length / 8));
            Write(artifact, 64 + 8, (uint)featureOffset);
            Write(artifact, 64 + 12, (uint)featureBytes.Length);
            featureBytes.CopyTo(artifact.AsSpan(featureOffset));
        }

        return artifact;
    }

    private static byte[] WithReferenceSlottedExtension()
    {
        var original = MinimalArtifact();
        var artifact = new byte[165];
        original.AsSpan(0, 64).CopyTo(artifact);
        original.AsSpan(64, 48).CopyTo(artifact.AsSpan(80));
        original.AsSpan(112).CopyTo(artifact.AsSpan(128));
        Write(artifact, 12, 3);
        Write(artifact, 16, (uint)artifact.Length);
        Write(artifact, 32 + 8, 80);
        Write(artifact, 48 + 8, 128);
        WriteU16(artifact, 64, 0x8000);
        WriteU16(artifact, 66, 2);
        Write(artifact, 68, 1);
        Write(artifact, 72, 144);
        Write(artifact, 76, 21);
        Write(artifact, 144, 1);
        WriteU16(artifact, 148, 0x0002);
        Write(artifact, 152, 1);
        return artifact;
    }

    private static byte[] ArtifactWithMetadataTables()
    {
        var names = TrRmStringTableWriter.EncodeNames(
            [("A", 0), ("B", 1), ("src/A.terrum", 0)]);
        var artifact = new byte[32 + 4 * 16 + 48 + names.Length + 12 + 8];
        "TRRM"u8.CopyTo(artifact);
        WriteU16(artifact, 4, 1);
        Write(artifact, 8, 32);
        Write(artifact, 12, 4);
        Write(artifact, 16, (uint)artifact.Length);
        Write(artifact, 20, 32);
        var moduleOffset = 96;
        var stringOffset = moduleOffset + 48;
        var refOffset = stringOffset + names.Length;
        var sourceOffset = refOffset + 12;
        WriteU16(artifact, 32, 0x0001);
        Write(artifact, 32 + 4, 1);
        Write(artifact, 32 + 8, (uint)moduleOffset);
        Write(artifact, 32 + 12, 48);
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
        Write(artifact, moduleOffset + 32, 1);
        Write(artifact, moduleOffset + 36, 1);
        Write(artifact, moduleOffset + 40, 1);
        Write(artifact, moduleOffset + 44, 1);
        names.CopyTo(artifact.AsSpan(stringOffset));
        Write(artifact, refOffset, 2);
        Write(artifact, refOffset + 4, 1);
        Write(artifact, sourceOffset, 3);
        return artifact;
    }

    private static string ReadCallableName(byte[] artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0002, out var stringRows, out var stringCount), Is.True);
        var strings = new TrRmStringTableReader(stringRows, stringCount);
        Assert.That(index.TryGetSection(0x0001, out var moduleRows, out var moduleCount), Is.True);
        var modules = new TrRmModuleTableReader(moduleRows, moduleCount, strings, 0, 0, 1, 0);
        var types = new TrRmTypeDefTableReader([], 0, strings, modules, 0, 0, 0, 0);
        Assert.That(index.TryGetSection(0x0011, out var typeRows, out var typeCount), Is.True);
        var typeSpecs = new TrRmTypeSpecTableReader(typeRows, typeCount, 0, 0, 0, 0);
        Assert.That(index.TryGetSection(0x0017, out var signatureRows, out var signatureCount), Is.True);
        var signatures = new TrRmMethodSigTableReader(signatureRows, signatureCount, typeSpecs, 0, 0);
        Assert.That(index.TryGetSection(0x0016, out var methodRows, out var methodCount), Is.True);
        var methods = new TrRmMethodDefTableReader(methodRows, methodCount, strings,
            modules, types, signatures, 0, 0);
        return methods.GetName(1);
    }

    private static byte[] ArtifactWithCallableMetadata(bool includeEmptyBlob = false,
        bool includeBody = false, bool includeSourceMap = false,
        bool includeOutputState = false, bool doesNotReturn = false,
        bool conflictingOutputStates = false)
    {
        var strings = includeSourceMap
            ? TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0), ("src/file", 0)])
            : TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0)]);
        var module = new byte[48];
        Write(module, 0, 1);
        Write(module, 4, 1);
        Write(module, 32, 1);
        Write(module, 36, 1);
        Write(module, 40, 1);
        Write(module, 44, 1);
        if (includeOutputState)
        {
            Write(module, 24, 1);
            Write(module, 28, 1);
        }

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
            Write(method, 20, conflictingOutputStates
                ? (doesNotReturn ? 5u : 4u)
                : (doesNotReturn ? 3u : 2u));
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
            typeSpec = TrRmTypeSpecTableWriter.Encode([
                TrRmTypeSpecTableWriter.Builtin(
                    conflictingOutputStates ? (byte)8 : (byte)1),
                new byte[]
                {
                    0x0E, 0,
                    conflictingOutputStates ? (byte)8 : (byte)1,
                    0, 0, 0xF2,
                },
            ], 0, 0, 0);
            var typeSpecIndex = includeEmptyBlob ? 3 : 2;
            sections[typeSpecIndex] = typeSpec;
            var featureBytes = TrRmFeatureTableWriter.Encode([
                TrRmFeatureRow.PublishedPredicates,
            ]);
            sections = [
                .. sections[..typeSpecIndex],
                featureBytes,
                .. sections[typeSpecIndex..],
            ];
            kinds = [
                .. kinds[..typeSpecIndex],
                0x0004,
                .. kinds[typeSpecIndex..],
            ];

            var param = new byte[16];
            Write(param, 0, 1);
            Write(param, 8, 2);
            var contractRows = new List<ReadOnlyMemory<byte>>();
            if (doesNotReturn)
            {
                var effectContract = new byte[12];
                Write(effectContract, 0, 0x1600_0001);
                WriteU16(effectContract, 4, 1);
                Write(effectContract, 8, 2);
                contractRows.Add(effectContract);
            }
            var selectorCount = conflictingOutputStates ? 2 : 1;
            for (var selector = 0; selector < selectorCount; selector++)
            {
                var predicateContract = new byte[20];
                Write(predicateContract, 0, 0x1600_0001);
                WriteU16(predicateContract, 4, 0x000B);
                predicateContract[8] = 3;
                Write(predicateContract, 12, (uint)selector);
                Write(predicateContract, 16, (uint)(selector + 1));
                contractRows.Add(predicateContract);
            }
            for (var selector = 0; selector < selectorCount; selector++)
            {
                var outputStateContract = new byte[20];
                Write(outputStateContract, 0, 0x1600_0001);
                WriteU16(outputStateContract, 4, 0x0002);
                Write(outputStateContract, 8, 0);
                Write(outputStateContract, 12, (uint)(selector + 1));
                outputStateContract[16] = selector == 0 ? (byte)1 : (byte)0;
                contractRows.Add(outputStateContract);
            }
            var contract = TrRmVariableTableWriter.EncodeBodies(contractRows);
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
            sections = [.. sections, TrRmVariableTableWriter.EncodeBodies(
                [TrRmRegisterBodyTestData.MinimalReturnBody()])];
            kinds = [.. kinds, 0x0020];
        }

        if (includeOutputState)
        {
            var selectorCount = conflictingOutputStates ? 2 : 1;
            var graphRows = new TrRmPredicateGraphRow[selectorCount];
            var footprintRows = new TrRmPredicateReadFootprintRow[selectorCount];
            var roots = new byte[selectorCount * 8];
            var nodes = new byte[selectorCount * 16];
            var operands = new byte[selectorCount * 8];
            for (var selector = 0; selector < selectorCount; selector++)
            {
                var row = selector + 1;
                graphRows[selector] = new TrRmPredicateGraphRow(row, row);
                footprintRows[selector] = new TrRmPredicateReadFootprintRow(row, 1, 0, 0);
                roots[selector * 8] = 5;
                var nodeOffset = selector * 16;
                Write(nodes, nodeOffset,
                    conflictingOutputStates
                        ? selector == 0 ? 3u : 5u
                        : 1u);
                Write(nodes, nodeOffset + 4,
                    conflictingOutputStates ? 0xF200_0008 : 0xF200_0001);
                Write(nodes, nodeOffset + 8, (uint)row);
                Write(nodes, nodeOffset + 12, 1);
                var operandOffset = selector * 8;
                operands[operandOffset] = 1;
                Write(operands, operandOffset + 4, (uint)row);
            }
            var graphs = TrRmPredicateGraphTableWriter.Encode(graphRows,
                nodeCount: selectorCount, readFootprintCount: selectorCount);
            var footprints = TrRmPredicateReadFootprintTableWriter.Encode(
                footprintRows, rootCount: selectorCount, dependencyCount: 0);
            sections = [
                .. sections,
                graphs,
                footprints,
                roots,
                nodes,
                operands,
            ];
            kinds = [
                .. kinds,
                0x0037,
                0x0038,
                0x0039,
                0x003B,
                0x003C,
            ];
        }

        if (includeSourceMap)
        {
            var map = new byte[52];
            Write(map, 0, 2);
            WriteU16(map, 4, 0);
            WriteU16(map, 6, 1);
            Write(map, 8, 1);
            Write(map, 12, 0);
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
                kinds[entry] switch
                {
                    0x0002 => includeSourceMap ? 3u : 2u,
                    0x0011 => includeOutputState ? 2u : 1u,
                    0x001B => includeOutputState
                        ? (conflictingOutputStates ? 4u : 2u)
                            + (doesNotReturn ? 1u : 0u)
                        : 1u,
                    0x0037 or 0x0038 or 0x0039 or 0x003B or 0x003C
                        => conflictingOutputStates ? 2u : 1u,
                    _ => 1u,
                });
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

    private static int GetSectionDataOffset(byte[] artifact, ushort kind)
    {
        var sectionCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(12)));
        for (var entry = 0; entry < sectionCount; entry++)
        {
            var directory = 32 + entry * 16;
            if (BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(directory)) == kind)
            {
                return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    artifact.AsSpan(directory + 8)));
            }
        }

        throw new InvalidDataException($"Missing TRRM section 0x{kind:X4}.");
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteU16(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
}
