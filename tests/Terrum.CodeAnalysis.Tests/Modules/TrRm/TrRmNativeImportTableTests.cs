// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmNativeImportTableTests
{
    [Test]
    public void StandardWriterUsesAssignedFieldOffsetsAndValidatesCallableAndDataRows()
    {
        TrRmNativeImportRow[] rows =
        [
            new(1, 1, 0, 4, 1, 6, 7),
            new(2, 2, 2, 0, 1, 6, 0),
        ];
        var fixture = CreateFixture(rows, encodeWithWriter: true);

        Assert.That(fixture.NativeImportBytes.Length, Is.EqualTo(32));
        Assert.That(fixture.NativeImportBytes[0], Is.EqualTo(1));
        Assert.That(fixture.NativeImportBytes[1], Is.EqualTo(1));
        Assert.That(fixture.NativeImportBytes[2], Is.EqualTo(0));
        Assert.That(fixture.NativeImportBytes[3], Is.EqualTo(4));
        Assert.That(Read(fixture.NativeImportBytes, 4), Is.EqualTo(1));
        Assert.That(Read(fixture.NativeImportBytes, 8), Is.EqualTo(6));
        Assert.That(Read(fixture.NativeImportBytes, 12), Is.EqualTo(7));
        Assert.That(fixture.NativeImportBytes[16], Is.EqualTo(2));
        Assert.That(fixture.NativeImportBytes[17], Is.EqualTo(2));
        Assert.That(fixture.NativeImportBytes[18], Is.EqualTo(2));
        Assert.That(fixture.NativeImportBytes[19], Is.EqualTo(0));
        Assert.That(Read(fixture.NativeImportBytes, 20), Is.EqualTo(1));
        Assert.That(Read(fixture.NativeImportBytes, 24), Is.EqualTo(6));
        Assert.That(Read(fixture.NativeImportBytes, 28), Is.Zero);
        Assert.That(fixture.EntryNames, Is.EqualTo(new[] { "entry", "Data" }));
        Assert.DoesNotThrow(() => TrRmStructuralMetadataValidator.Validate(fixture.Artifact));
    }

    [Test]
    public void StandardRowsAcceptEveryAssignedModePolicyAndCallingConvention()
    {
        (byte Mode, byte Policy)[] resolutions = [(1, 0), (2, 1), (2, 2)];
        foreach (var (mode, policy) in resolutions)
        {
            for (byte callConv = 1; callConv <= 4; callConv++)
            {
                var callable = new TrRmNativeImportRow(
                    1, mode, policy, callConv, 1, 6, 7);
                var fixture = CreateFixture([callable]);
                Assert.DoesNotThrow(() =>
                    TrRmStructuralMetadataValidator.Validate(fixture.Artifact));
            }

            var data = new TrRmNativeImportRow(2, mode, policy, 0, 1, 6, 0);
            var dataFixture = CreateFixture([data]);
            Assert.DoesNotThrow(() =>
                TrRmStructuralMetadataValidator.Validate(dataFixture.Artifact));
        }
    }

    [Test]
    public void StandardRowsRequireTheirPerModuleFeatureClosure()
    {
        var callable = new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateFixture(
                [callable], features: []).Artifact));
        Assert.DoesNotThrow(() => TrRmStructuralMetadataValidator.Validate(
            CreateFixture([callable], features: [TrRmFeatureRow.DirectNativeCalls]).Artifact));

        var data = new TrRmNativeImportRow(2, 2, 1, 0, 1, 6, 0);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateFixture(
                [data], features: [TrRmFeatureRow.DirectNativeCalls]).Artifact));
        Assert.DoesNotThrow(() => TrRmStructuralMetadataValidator.Validate(
            CreateFixture([data], features: [TrRmFeatureRow.NativeDataImports]).Artifact));
    }

    [Test]
    public void StandardRowsApplyNativeCallableSignatureAndImportedDataTypeConstraints()
    {
        var callable = new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateFixture(
                [callable], methodParameterMode: 1, encodeWithWriter: false).Artifact));

        var data = new TrRmNativeImportRow(2, 1, 0, 0, 1, 6, 0);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateFixture(
                [data], fieldTypeSpecRef: 2, encodeWithWriter: false).Artifact));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateFixture(
                [data], fieldTypeSpecRef: 3, encodeWithWriter: false).Artifact));

        var targetBoundData = CreateFixture([data], fieldTypeSpecRef: 3,
            moduleTargetRequirement: TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            blittableProfileId: TrRmTargetProfileTableReader.WindowsX64NativeProfileId);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(targetBoundData.Artifact));

        var targetBoundUnion = CreateFixture([data], fieldTypeSpecRef: 3,
            moduleTargetRequirement: TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            blittableProfileId: TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
            profileLayoutKind: 2);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(targetBoundUnion.Artifact));

        var targetBoundAuto = CreateFixture([data], fieldTypeSpecRef: 3,
            moduleTargetRequirement: TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            blittableProfileId: TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
            profileLayoutKind: 0);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(targetBoundAuto.Artifact));

        var autoLayout = CreateFixture([data], fieldTypeSpecRef: 3,
            moduleTargetRequirement: TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            blittableProfileId: TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
            profileLayoutKind: 0, profileLayoutFlags: 0, encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(autoLayout.Artifact));

        var differentProfileOnly = CreateFixture([data], fieldTypeSpecRef: 3,
            moduleTargetRequirement: TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            blittableProfileId: 2, encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(differentProfileOnly.Artifact));

        var profileOnlyForUnselectedTarget = CreateFixture([data], fieldTypeSpecRef: 3,
            blittableProfileId: TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
            encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(profileOnlyForUnselectedTarget.Artifact));
    }

    [Test]
    public void StandardValidatorDoesNotUseDevelopmentFunctionPointerSignatureRows()
    {
        var data = new TrRmNativeImportRow(2, 1, 0, 0, 1, 6, 0);
        var developmentIndexedType = CreateFixture([data],
            fieldTypeSpecRef: 4, nativeFunctionPointerSignatureCount: 1,
            encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(developmentIndexedType.Artifact));
    }

    [Test]
    public void StandardDataImportsAcceptFunctionPointerValueFields()
    {
        var data = new TrRmNativeImportRow(2, 1, 0, 0, 1, 6, 0);
        var fixture = CreateFixture([data], fieldTypeSpecRef: 4,
            standardNativeFunctionPointerAnchorMethodDefRow: 1);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(fixture.Artifact));
    }

    [Test]
    public void StandardValidatorRejectsInvalidOwnerKindStaticnessAndCallConvention()
    {
        TrRmNativeImportRow[] invalidRows =
        [
            new(0, 1, 0, 1, 1, 6, 7),
            new(1, 1, 0, 1, 2, 6, 7),
            new(1, 1, 0, 0, 1, 6, 7),
            new(1, 1, 0, 5, 1, 6, 7),
            new(2, 1, 0, 1, 1, 6, 0),
            new(2, 1, 0, 0, 2, 6, 0),
        ];
        foreach (var row in invalidRows)
        {
            Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator.Validate(
                    CreateFixture([row], encodeWithWriter: false).Artifact));
        }

        var instanceMethod = CreateFixture(
            [new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7)],
            methodReceiver: 1, encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(instanceMethod.Artifact));

        var instanceField = CreateFixture(
            [new TrRmNativeImportRow(2, 1, 0, 0, 1, 6, 0)],
            fieldFlags: 0, encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(instanceField.Artifact));
    }

    [Test]
    public void StandardValidatorRejectsInvalidModePolicyNamesReferencesAndDuplicateOwners()
    {
        TrRmNativeImportRow[] invalidRows =
        [
            new(1, 1, 1, 1, 1, 6, 7),
            new(1, 1, 2, 1, 1, 6, 7),
            new(1, 2, 0, 1, 1, 6, 7),
            new(1, 2, 3, 1, 1, 6, 7),
            new(1, 0, 0, 1, 1, 6, 7),
            new(1, 3, 1, 1, 1, 6, 7),
            new(1, 1, 0, 1, 1, 0, 7),
            new(1, 1, 0, 1, 1, 99, 7),
            new(1, 1, 0, 1, 1, 6, 99),
        ];
        foreach (var row in invalidRows)
        {
            Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator.Validate(
                    CreateFixture([row], encodeWithWriter: false).Artifact));
        }

        foreach (var library in new[] { "", ".", "..", "lib/name", "lib\\name",
                     "lib:name", "bad\u0001", "bad\u007F", "libé" })
        {
            var fixture = CreateFixture(
                [new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7)],
                libraryName: library,
                libraryEncoding: library.Any(static value => value > 0x7F) ? 1 : 0,
                encodeWithWriter: false);
            Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator.Validate(fixture.Artifact));
        }

        foreach (var library in new[] { "a", "probe name", "libstdc++.so.6" })
        {
            var fixture = CreateFixture(
                [new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7)],
                libraryName: library);
            Assert.DoesNotThrow(() =>
                TrRmStructuralMetadataValidator.Validate(fixture.Artifact));
        }

        var nonAsciiEntry = CreateFixture(
            [new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7)],
            entryName: "entryé", entryEncoding: 1, encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(nonAsciiEntry.Artifact));

        var methodDuplicate = new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateFixture(
                [methodDuplicate, methodDuplicate], encodeWithWriter: false).Artifact));
        var fieldDuplicate = new TrRmNativeImportRow(2, 1, 0, 0, 1, 6, 0);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateFixture(
                [fieldDuplicate, fieldDuplicate], encodeWithWriter: false).Artifact));
    }

    [Test]
    public void EntryNameFallbackPreservesDeclarationTextAndExplicitNamesRequireOnlyAscii()
    {
        var method = new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7);
        var explicitAscii = CreateFixture([method], entryName: "entry \u0001");
        Assert.That(explicitAscii.EntryNames, Is.EqualTo(new[] { "entry \u0001" }));
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(explicitAscii.Artifact));

        var methodDefault = method with { EntryNameStringRow = 0 };
        var fieldDefault = new TrRmNativeImportRow(2, 1, 0, 0, 1, 6, 0);
        var declarationNames = CreateFixture([methodDefault, fieldDefault],
            methodName: "Café", methodNameEncoding: 1,
            fieldName: "Donnée", fieldNameEncoding: 1);
        Assert.That(declarationNames.EntryNames, Is.EqualTo(new[] { "Café", "Donnée" }));
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(declarationNames.Artifact));
    }

    [Test]
    public void NativeExportWriterUsesAssignedReferencesAndRequiresFeatureClosure()
    {
        var method = new TrRmNativeExportRow(1, 1, 8, 2);
        var field = new TrRmNativeExportRow(2, 1, 0, 0);
        var fixture = CreateFixture([], nativeExports: [method, field],
            includeNativeCallableContract: true);

        Assert.That(fixture.NativeExportBytes.Length, Is.EqualTo(32));
        Assert.That(fixture.NativeExportBytes[0], Is.EqualTo(1));
        Assert.That(fixture.NativeExportBytes[1], Is.Zero);
        Assert.That(fixture.NativeExportBytes[2], Is.Zero);
        Assert.That(fixture.NativeExportBytes[3], Is.Zero);
        Assert.That(Read(fixture.NativeExportBytes, 4), Is.EqualTo(1));
        Assert.That(Read(fixture.NativeExportBytes, 8), Is.EqualTo(8));
        Assert.That(Read(fixture.NativeExportBytes, 12), Is.EqualTo(2));
        Assert.That(Read(fixture.NativeExportBytes, 24), Is.Zero);
        Assert.That(Read(fixture.NativeExportBytes, 28), Is.Zero);
        Assert.That(fixture.NativeExportNames, Is.EqualTo(new[] { "export", "Data" }));
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(fixture.Artifact));

        var noFeature = CreateFixture([], features: [], nativeExports: [method],
            includeNativeCallableContract: true);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(noFeature.Artifact));
    }

    [Test]
    public void NativeExportRowsRequireExactStaticOwnersAndCallableClauseReferences()
    {
        var method = new TrRmNativeExportRow(1, 1, 0, 2);
        var field = new TrRmNativeExportRow(2, 1, 0, 0);
        var invalidRows = new[]
        {
            new TrRmNativeExportRow(0, 1, 0, 0),
            new TrRmNativeExportRow(1, 2, 0, 2),
            new TrRmNativeExportRow(1, 1, 0, 1),
            new TrRmNativeExportRow(1, 1, 0, 0),
            new TrRmNativeExportRow(2, 1, 0, 1),
            new TrRmNativeExportRow(2, 1, 0, 2),
        };
        foreach (var row in invalidRows)
        {
            var fixture = CreateFixture([], nativeExports: [row],
                includeNativeCallableContract: true, encodeWithWriter: false);
            Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator.Validate(fixture.Artifact));
        }

        var missingClause = CreateFixture([], nativeExports: [method],
            encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(missingClause.Artifact));

        var invalidFlags = CreateFixture([], nativeExports: [method],
            includeNativeCallableContract: true, nativeExportFlags: 1,
            encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(invalidFlags.Artifact));

        var invalidReserved = CreateFixture([], nativeExports: [method],
            includeNativeCallableContract: true, nativeExportReserved: 1,
            encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(invalidReserved.Artifact));

        var instanceMethod = CreateFixture([], nativeExports: [method],
            includeNativeCallableContract: true, methodReceiver: 1,
            encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(instanceMethod.Artifact));

        var instanceField = CreateFixture([], nativeExports: [field],
            fieldFlags: 0, encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(instanceField.Artifact));
    }

    [Test]
    public void NativeExportRowsRejectDuplicateOwnersAndSymbolNames()
    {
        var method = new TrRmNativeExportRow(1, 1, 8, 2);
        var field = new TrRmNativeExportRow(2, 1, 8, 0);
        var duplicateOwner = CreateFixture([], nativeExports: [method, method],
            includeNativeCallableContract: true, encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(duplicateOwner.Artifact));

        var duplicateName = CreateFixture([], nativeExports: [method, field],
            includeNativeCallableContract: true, encodeWithWriter: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(duplicateName.Artifact));
    }

    [Test]
    public void StandardFunctionPointerAnchorIsBodylessNonCallableAndFeatureGated()
    {
        var anchor = CreateFixture([], standardNativeFunctionPointerAnchorMethodDefRow: 1);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(anchor.Artifact));

        var selectorAnchor = CreateFixture([],
            standardNativeFunctionPointerAnchorMethodDefRow: 1,
            standardAnchorPredicatePhase: 3);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(selectorAnchor.Artifact));

        var assertionAnchor = CreateFixture([],
            standardNativeFunctionPointerAnchorMethodDefRow: 1,
            standardAnchorPredicatePhase: 1);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(assertionAnchor.Artifact));

        var missingFeature = CreateFixture([], features: [],
            standardNativeFunctionPointerAnchorMethodDefRow: 1);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(missingFeature.Artifact));

        var valueFeature = CreateFixture([], fieldTypeSpecRef: 4,
            standardNativeFunctionPointerAnchorMethodDefRow: 1,
            features: [TrRmFeatureRow.RootlessNativeValues]);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(valueFeature.Artifact));

        var missingValueFeature = CreateFixture([], fieldTypeSpecRef: 4,
            standardNativeFunctionPointerAnchorMethodDefRow: 1,
            features: [TrRmFeatureRow.NativeBoundaryContracts]);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(missingValueFeature.Artifact));

        var callableAnchor = CreateFixture([], includeNativeCallableContract: true,
            standardNativeFunctionPointerAnchorMethodDefRow: 1);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(callableAnchor.Artifact));

        var importedAnchor = CreateFixture([
                new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7),
            ], standardNativeFunctionPointerAnchorMethodDefRow: 1);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(importedAnchor.Artifact));
    }

    [Test]
    public void StandardFeatureTwoClosureIncludesFunctionPointersInConstructedArguments()
    {
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateConstructedFunctionPointerUseArtifact(includeRootlessFeature: false)));
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateConstructedFunctionPointerUseArtifact(includeRootlessFeature: true)));
    }

    [Test]
    public void StandardBodyTypeMapFeatureClosureIncludesConstructedFunctionPointerArguments()
    {
        Assert.Throws<InvalidDataException>(() =>
            ValidateConstructedBodyTypeMap(
                CreateConstructedFunctionPointerUseArtifact(
                    includeRootlessFeature: false, useInBodyTypeMap: true)));
        Assert.DoesNotThrow(() =>
            ValidateConstructedBodyTypeMap(
                CreateConstructedFunctionPointerUseArtifact(
                    includeRootlessFeature: true, useInBodyTypeMap: true)));
    }

    [Test]
    public void StandardBodyMethodMapCannotTargetAFunctionPointerAnchor()
    {
        Assert.DoesNotThrow(() =>
            ValidateAnchorMethodMapArtifact(
                CreateAnchorMethodMapArtifact(0x1600_0001)));
        Assert.Throws<InvalidDataException>(() =>
            ValidateAnchorMethodMapArtifact(
                CreateAnchorMethodMapArtifact(0x1600_0002)));
    }

    [Test]
    public void StandardFunctionPointerAnchorIdentityIncludesConvention()
    {
        var equivalent = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(CreateAnchorMethodMapArtifact(
                0x1600_0001, secondAnchorConvention: 1)));
        Assert.That(equivalent.NativeFunctionPointerAnchors
            .AreEquivalentTypeSpecRefs(3, 4), Is.True);

        var differentConvention =
            TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(CreateAnchorMethodMapArtifact(
                    0x1600_0001, secondAnchorConvention: 2)));
        Assert.That(differentConvention.NativeFunctionPointerAnchors
            .AreEquivalentTypeSpecRefs(3, 4), Is.False);

        var differentAnchorRow =
            TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(CreateAnchorMethodMapArtifact(
                    0x1600_0001, secondAnchorConvention: 1,
                    distinctSecondAnchor: true)));
        Assert.That(differentAnchorRow.NativeFunctionPointerAnchors
            .AreEquivalentTypeSpecRefs(3, 4), Is.True);

        var differentSignature =
            TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(CreateAnchorMethodMapArtifact(
                    0x1600_0001, secondAnchorConvention: 1,
                    distinctSecondAnchor: true, secondAnchorHasUnitResult: true)));
        Assert.That(differentSignature.NativeFunctionPointerAnchors
            .AreEquivalentTypeSpecRefs(3, 4), Is.False);

        var equivalentContracts =
            TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(CreateAnchorMethodMapArtifact(
                    0x1600_0001, secondAnchorConvention: 1,
                    distinctSecondAnchor: true, anchorEffectsMask: 0,
                    anchorDependencyPhase: 1, secondAnchorDependencyPhase: 1)));
        Assert.That(equivalentContracts.NativeFunctionPointerAnchors
            .AreEquivalentTypeSpecRefs(3, 4), Is.True);

        var differentEffects =
            TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(CreateAnchorMethodMapArtifact(
                    0x1600_0001, secondAnchorConvention: 1,
                    distinctSecondAnchor: true, anchorEffectsMask: 0,
                    secondAnchorEffectsMask: 4)));
        Assert.That(differentEffects.NativeFunctionPointerAnchors
            .AreEquivalentTypeSpecRefs(3, 4), Is.False);

        var differentDependencies =
            TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(CreateAnchorMethodMapArtifact(
                    0x1600_0001, secondAnchorConvention: 1,
                    distinctSecondAnchor: true, anchorDependencyPhase: 1,
                    secondAnchorDependencyPhase: 3)));
        Assert.That(differentDependencies.NativeFunctionPointerAnchors
            .AreEquivalentTypeSpecRefs(3, 4), Is.False);
    }

    [Test]
    public void StandardBodyTypeMapRequiresRootlessNativeValuesForFunctionPointers()
    {
        Assert.Throws<InvalidDataException>(() =>
            ValidateAnchorMethodMapArtifact(CreateAnchorMethodMapArtifact(
                0x1600_0001, includeFunctionPointerTypeMap: true)));
        Assert.DoesNotThrow(() =>
            ValidateAnchorMethodMapArtifact(CreateAnchorMethodMapArtifact(
                0x1600_0001, includeRootlessFeature: true,
                includeFunctionPointerTypeMap: true)));
    }

    private static Fixture CreateFixture(
        IReadOnlyList<TrRmNativeImportRow> rows,
        IReadOnlyList<TrRmFeatureRow>? features = null,
        IReadOnlyList<TrRmNativeExportRow>? nativeExports = null,
        string libraryName = "kernel32",
        int libraryEncoding = 0,
        string entryName = "entry",
        int entryEncoding = 0,
        string methodName = "Call",
        int methodNameEncoding = 0,
        string fieldName = "Data",
        int fieldNameEncoding = 0,
        uint methodReceiver = 0,
        uint fieldFlags = 0x08,
        uint fieldTypeSpecRef = 1,
        byte? methodParameterMode = null,
        int nativeFunctionPointerSignatureCount = 0,
        uint moduleTargetRequirement = 0,
        uint? blittableProfileId = null,
        byte profileLayoutKind = 1,
        byte profileLayoutFlags = 1,
        bool includeNativeCallableContract = false,
        byte? standardAnchorPredicatePhase = null,
        byte nativeExportFlags = 0,
        ushort nativeExportReserved = 0,
        int? standardNativeFunctionPointerAnchorMethodDefRow = null,
        bool encodeWithWriter = true)
    {
        if (features is null)
        {
            var defaultFeatures = new List<TrRmFeatureRow>();
            if (rows.Count != 0)
            {
                defaultFeatures.Add(TrRmFeatureRow.DirectNativeCalls);
                defaultFeatures.Add(TrRmFeatureRow.NativeDataImports);
            }
            if (nativeExports is { Count: > 0 })
            {
                defaultFeatures.Add(TrRmFeatureRow.NativeExportEntries);
            }
            if (standardNativeFunctionPointerAnchorMethodDefRow.HasValue
                && rows.Count == 0 && nativeExports is not { Count: > 0 })
            {
                defaultFeatures.Add(TrRmFeatureRow.RootlessNativeValues);
            }
            if (standardAnchorPredicatePhase.HasValue)
            {
                defaultFeatures.Add(TrRmFeatureRow.PublishedPredicates);
            }
            if (defaultFeatures.Count == 0)
            {
                defaultFeatures.Add(TrRmFeatureRow.DirectNativeCalls);
                defaultFeatures.Add(TrRmFeatureRow.NativeDataImports);
            }

            features = defaultFeatures;
        }
        var stringRows = new List<(string Text, int Encoding)>
        {
            ("", 0), ("Module", 0), ("T", 0), (methodName, methodNameEncoding),
            (fieldName, fieldNameEncoding),
            (libraryName, libraryEncoding), (entryName, entryEncoding), ("export", 0),
        };
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringBytes, stringRows.Count);
        var featureBytes = features.Count == 0 ? [] : TrRmFeatureTableWriter.Encode(features);
        var featureTable = new TrRmFeatureTableReader(featureBytes, features.Count);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, moduleTargetRequirement, 0, 0, 1, 1, 1, 1,
                features.Count == 0 ? 0 : 1, features.Count),
        ], strings, 0, 1, 1, 0, features.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 1, 0, features.Count);
        List<ReadOnlyMemory<byte>> typeSpecRows =
        [
            TrRmTypeSpecTableWriter.Builtin(0x0D),
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
        ];
        if (nativeFunctionPointerSignatureCount != 0)
        {
            typeSpecRows.Add(
                TrRmTypeSpecTableWriter.DevelopmentFunctionPointerSignatureReference(1));
        }
        if (standardNativeFunctionPointerAnchorMethodDefRow is int anchorMethodDefRow)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.NativeFunctionPointer(
                1, checked((uint)anchorMethodDefRow)));
        }
        if (standardAnchorPredicatePhase.HasValue)
        {
            if (!standardNativeFunctionPointerAnchorMethodDefRow.HasValue)
            {
                throw new ArgumentException(
                    "A predicate anchor requires a Standard function-pointer row.",
                    nameof(standardAnchorPredicatePhase));
            }

            typeSpecRows.Add(TrRmTypeSpecTableWriter.Builtin(1));
        }
        var typeSpecSchema = nativeFunctionPointerSignatureCount == 0
            ? TrRmArtifactSchema.Standard : TrRmArtifactSchema.Development;
        if (standardNativeFunctionPointerAnchorMethodDefRow.HasValue
            && nativeFunctionPointerSignatureCount != 0)
        {
            throw new ArgumentException(
                "A fixture cannot mix Standard and Development function-pointer rows.",
                nameof(standardNativeFunctionPointerAnchorMethodDefRow));
        }
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            1, 0, 0, nativeFunctionPointerSignatureCount, typeSpecSchema,
            standardNativeFunctionPointerAnchorMethodDefRow.HasValue ? 1 : 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecRows.Count,
            1, 0, 0, nativeFunctionPointerSignatureCount, typeSpecSchema,
            standardNativeFunctionPointerAnchorMethodDefRow.HasValue ? 1 : 0);
        var contractRecordCount = 1 + (includeNativeCallableContract ? 1 : 0)
            + (standardAnchorPredicatePhase.HasValue ? 1 : 0);
        var layoutCount = blittableProfileId.HasValue ? 2 : 1;
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 6, 1, 1, 0, 0, 1, layoutCount, 0, 0),
        ], strings, modules, 1, 0, layoutCount, contractRecordCount);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 1, strings,
            modules, 1, 0, layoutCount, contractRecordCount);
        var targetProfileBytes = blittableProfileId.HasValue ? new byte[8] : [];
        if (blittableProfileId is uint profileId)
        {
            Write(targetProfileBytes, 0, checked((int)profileId));
            Write(targetProfileBytes, 4,
                (int)TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision);
        }
        var targetProfiles = new TrRmTargetProfileTableReader(targetProfileBytes,
            blittableProfileId.HasValue ? 1 : 0);
        var layoutRows = new TrRmLayoutRow[layoutCount];
        layoutRows[0] = new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0);
        if (blittableProfileId.HasValue)
        {
            layoutRows[1] = new TrRmLayoutRow(1, 1, profileLayoutKind,
                profileLayoutFlags, 0, 0, 0, 0);
        }
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows,
            typeDefs, targetProfiles, 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutCount,
            typeDefs, targetProfiles, 0);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 5, fieldTypeSpecRef, fieldFlags),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 1,
            strings, typeDefs, typeSpecs);
        var parameterCount = methodParameterMode.HasValue ? 1 : 0;
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, methodReceiver,
                parameterCount == 0 ? 0 : 1, parameterCount, 0, 0),
        ], typeSpecs, parameterCount, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, parameterCount, 0);
        var parameterBytes = parameterCount == 0 ? [] : TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, 1, methodParameterMode!.Value),
        ], signatures, typeSpecs);
        var parameters = parameterCount == 0 ? default
            : new TrRmParamTableReader(parameterBytes, parameterCount, signatures, typeSpecs);
        var contractRows = new List<TrRmContractRow>();
        var effectPayload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(effectPayload, 4);
        contractRows.Add(new TrRmContractRow(0x1600_0001, 1, effectPayload));
        if (includeNativeCallableContract)
        {
            contractRows.Add(new TrRmContractRow(0x1600_0001, 0x0C,
                new byte[] { 3, 0, 0, 0 }));
        }
        if (standardAnchorPredicatePhase is byte predicatePhase)
        {
            var predicatePayload = new byte[12];
            predicatePayload[0] = predicatePhase;
            Write(predicatePayload, 8, 1);
            contractRows.Add(new TrRmContractRow(0x1600_0001, 0x0B,
                predicatePayload));
        }

        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(1, 4, 1, 2, 1, contractRows.Count, 0),
        ], strings, modules, typeDefs, signatures, contractRows.Count, 0);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1, strings,
            modules, typeDefs, signatures, contractRows.Count, 0);
        var contractBytes = standardAnchorPredicatePhase.HasValue
            ? TrRmContractTableWriter.EncodeWithPredicateGraphs(contractRows,
                typeDefs, methods, signatures, parameters, typeSpecs,
                featureTable, modules, predicateGraphCount: 1)
            : TrRmContractTableWriter.Encode(contractRows,
                typeDefs, methods, signatures, parameters, typeSpecs);
        var contracts = standardAnchorPredicatePhase.HasValue
            ? new TrRmContractTableReader(contractBytes, contractRows.Count,
                typeDefs, methods, signatures, parameters, typeSpecs, 0,
                featureTable, modules, TrRmArtifactSchema.Standard,
                predicateGraphCount: 1)
            : new TrRmContractTableReader(contractBytes, contractRows.Count,
                typeDefs, methods, signatures, parameters, typeSpecs);
        var nativeImportBytes = rows.Count == 0 ? []
            : encodeWithWriter
            ? TrRmNativeImportTableWriter.Encode(rows, strings, methods, fields,
                typeDefs, default, modules, signatures, parameters, typeSpecs, contracts,
                layouts)
            : EncodeRawRows(rows);
        var entryNames = new string[encodeWithWriter ? rows.Count : 0];
        if (encodeWithWriter)
        {
            var nativeImports = new TrRmNativeImportTableReader(nativeImportBytes,
                rows.Count, strings, methods, fields, typeDefs, default, modules,
                signatures, parameters, typeSpecs, contracts, layouts);
            for (var row = 1; row <= rows.Count; row++)
            {
                entryNames[row - 1] = nativeImports.GetEntryName(nativeImports.GetRow(row));
            }
        }
        var nativeExportRows = nativeExports ?? Array.Empty<TrRmNativeExportRow>();
        var nativeExportBytes = nativeExportRows.Count == 0 ? []
            : encodeWithWriter
                ? TrRmNativeExportTableWriter.Encode(nativeExportRows, strings,
                    methods, fields, typeDefs, default, modules, signatures,
                    typeSpecs, contracts, layouts)
                : EncodeRawNativeExportRows(nativeExportRows,
                    nativeExportFlags, nativeExportReserved);
        var nativeExportNames = new string[encodeWithWriter ? nativeExportRows.Count : 0];
        if (encodeWithWriter)
        {
            var nativeExportTable = new TrRmNativeExportTableReader(nativeExportBytes,
                nativeExportRows.Count, strings, methods, fields, typeDefs,
                default, modules, signatures, typeSpecs, contracts, layouts);
            for (var row = 1; row <= nativeExportRows.Count; row++)
            {
                nativeExportNames[row - 1] = nativeExportTable.GetEntryName(row);
            }
        }
        byte[]? predicateGraphBytes = null;
        byte[]? predicateReadFootprintBytes = null;
        byte[]? predicateReadRootBytes = null;
        byte[]? predicateNodeBytes = null;
        byte[]? predicateNodeOperandBytes = null;
        if (standardAnchorPredicatePhase.HasValue)
        {
            predicateGraphBytes = TrRmPredicateGraphTableWriter.Encode([
                new TrRmPredicateGraphRow(1, 1),
            ], nodeCount: 1, readFootprintCount: 1);
            predicateReadFootprintBytes = TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(1, 1, 0, 0),
            ], rootCount: 1, dependencyCount: 0);
            predicateReadRootBytes = new byte[8];
            predicateReadRootBytes[0] = 5;
            predicateNodeBytes = new byte[16];
            Write(predicateNodeBytes, 0, 3);
            Write(predicateNodeBytes, 4, typeSpecRows.Count);
            Write(predicateNodeBytes, 8, 1);
            Write(predicateNodeBytes, 12, 1);
            predicateNodeOperandBytes = new byte[8];
            predicateNodeOperandBytes[0] = 1;
            Write(predicateNodeOperandBytes, 4, 1);
        }

        List<TrRmSection> sections =
        [
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, (uint)stringRows.Count, stringBytes),
        ];
        if (features.Count != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0, (uint)features.Count, featureBytes));
        }
        sections.Add(new TrRmSection(0x0011, 0, (uint)typeSpecRows.Count, typeSpecBytes));
        sections.Add(new TrRmSection(0x0013, 0, 1, typeDefBytes));
        sections.Add(new TrRmSection(0x0014, 0, 1, fieldBytes));
        sections.Add(new TrRmSection(0x0015, 0, (uint)layoutCount, layoutBytes));
        sections.Add(new TrRmSection(0x0016, 0, 1, methodBytes));
        sections.Add(new TrRmSection(0x0017, 0, 1, signatureBytes));
        if (parameterCount != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0, (uint)parameterCount, parameterBytes));
        }
        sections.Add(new TrRmSection(0x001B, 0,
            (uint)contractRows.Count, contractBytes));
        if (rows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0021, 0, (uint)rows.Count, nativeImportBytes));
        }
        if (nativeExportRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0022, 0,
                (uint)nativeExportRows.Count, nativeExportBytes));
        }
        if (predicateGraphBytes is not null)
        {
            sections.Add(new TrRmSection(0x0037, 0, 1, predicateGraphBytes));
            sections.Add(new TrRmSection(0x0038, 0, 1, predicateReadFootprintBytes!));
            sections.Add(new TrRmSection(0x0039, 0, 1, predicateReadRootBytes!));
            sections.Add(new TrRmSection(0x003B, 0, 1, predicateNodeBytes!));
            sections.Add(new TrRmSection(0x003C, 0, 1, predicateNodeOperandBytes!));
        }
        if (blittableProfileId.HasValue)
        {
            sections.Add(new TrRmSection(0x0048, 0, 1, targetProfileBytes));
        }

        return new Fixture(TrRmArtifactWriter.Encode(sections),
            nativeImportBytes, entryNames, nativeExportBytes, nativeExportNames);
    }

    private static byte[] EncodeRawRows(IReadOnlyList<TrRmNativeImportRow> rows)
    {
        var bytes = new byte[rows.Count * 16];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 16;
            bytes[offset] = row.OwnerKind;
            bytes[offset + 1] = row.ResolutionMode;
            bytes[offset + 2] = row.ResolutionPolicy;
            bytes[offset + 3] = row.CallConv;
            Write(bytes, offset + 4, row.OwnerRef);
            Write(bytes, offset + 8, row.LibraryNameStringRow);
            Write(bytes, offset + 12, row.EntryNameStringRow);
        }

        return bytes;
    }

    private static byte[] EncodeRawNativeExportRows(
        IReadOnlyList<TrRmNativeExportRow> rows, byte flags, ushort reserved)
    {
        var bytes = new byte[rows.Count * 16];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 16;
            bytes[offset] = row.OwnerKind;
            bytes[offset + 1] = flags;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 2), reserved);
            Write(bytes, offset + 4, row.OwnerRef);
            Write(bytes, offset + 8, row.EntryNameStringRow);
            Write(bytes, offset + 12, row.NativeCallableContractRow);
        }

        return bytes;
    }

    private static byte[] CreateAnchorMethodMapArtifact(uint methodMapToken,
        bool includeRootlessFeature = false,
        bool includeFunctionPointerTypeMap = false,
        byte? secondAnchorConvention = null,
        bool distinctSecondAnchor = false,
        bool secondAnchorHasUnitResult = false,
        uint? anchorEffectsMask = null,
        uint? secondAnchorEffectsMask = null,
        byte? anchorDependencyPhase = null,
        byte? secondAnchorDependencyPhase = null)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("main", 0), ("anchor", 0), ("anchor2", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 5);
        var methodCount = distinctSecondAnchor ? 3 : 2;
        TrRmFeatureRow[] featureRows = includeRootlessFeature
            ? [TrRmFeatureRow.NativeBoundaryContracts, TrRmFeatureRow.RootlessNativeValues]
            : [TrRmFeatureRow.NativeBoundaryContracts];
        var featuresBytes = TrRmFeatureTableWriter.Encode(featureRows);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1,
                featureRows.Length, 1),
        ], strings, 0, 0, methodCount, 0, featureRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, methodCount, 0, featureRows.Length);
        var typeSpecRows = new List<ReadOnlyMemory<byte>>
        {
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.NativeFunctionPointer(1, 2),
        };
        if (secondAnchorConvention is byte convention)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.NativeFunctionPointer(
                convention, distinctSecondAnchor ? 3u : 2u));
        }
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            0, 0, 0, 0, schema: TrRmArtifactSchema.Standard, methodDefCount: methodCount);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecRows.Count,
            0, 0, 0, 0, schema: TrRmArtifactSchema.Standard, methodDefCount: methodCount);
        var signatureRows = new List<TrRmMethodSigRow>
        {
            new TrRmMethodSigRow(0xF200_0000, 0, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF200_0008, 0, 0, 0, 0, 0),
        };
        if (distinctSecondAnchor)
        {
            signatureRows.Add(new TrRmMethodSigRow(
                secondAnchorHasUnitResult ? 0xF200_0000u : 0xF200_0008u,
                0, 0, 0, 0, 0));
        }
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows, typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, signatureRows.Count,
            typeSpecs, 0, 0);
        var contractRows = new List<TrRmContractRow>();
        var anchorContractFirst = AddAnchorContracts(contractRows, 2,
            anchorEffectsMask, anchorDependencyPhase);
        var secondAnchorContractFirst = distinctSecondAnchor
            ? AddAnchorContracts(contractRows, 3, secondAnchorEffectsMask,
                secondAnchorDependencyPhase)
            : (First: 0, Count: 0);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, contractRows.Count);
        var methodRows = new List<TrRmMethodDefRow>
        {
            new(0, 3, 1, 0, 0, 0, 1),
            new(0, 4, 2, 0, anchorContractFirst.First,
                anchorContractFirst.Count, 0),
        };
        if (distinctSecondAnchor)
        {
            methodRows.Add(new TrRmMethodDefRow(0, 5, 3, 0,
                secondAnchorContractFirst.First, secondAnchorContractFirst.Count, 0));
        }
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows, strings,
            modules, typeDefs, signatures, contractRows.Count, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes, methodRows.Count,
            strings, modules, typeDefs, signatures, contractRows.Count, 1);
        var contractBytes = contractRows.Count == 0 ? []
            : TrRmContractTableWriter.Encode(contractRows, typeDefs, methods,
                signatures, default, typeSpecs);
        var body = CreateMethodMapBody(methodMapToken, includeFunctionPointerTypeMap);
        var sections = new List<TrRmSection>
        {
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 5, stringsBytes),
            new TrRmSection(0x0004, 0, (uint)featureRows.Length, featuresBytes),
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Count, typeSpecBytes),
            new TrRmSection(0x0016, 0, (uint)methodRows.Count, methodBytes),
            new TrRmSection(0x0017, 0, (uint)signatureRows.Count, signatureBytes),
        };
        if (contractRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001B, 0,
                (uint)contractRows.Count, contractBytes));
        }
        sections.Add(new TrRmSection(0x0020, 0, 1,
            TrRmVariableTableWriter.EncodeBodies([body])));

        return TrRmArtifactWriter.Encode(sections);
    }

    private static (int First, int Count) AddAnchorContracts(
        List<TrRmContractRow> rows, int methodRow, uint? effectsMask,
        byte? dependencyPhase)
    {
        var first = rows.Count + 1;
        if (effectsMask is uint effects)
        {
            var payload = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, effects);
            rows.Add(new TrRmContractRow(0x1600_0000u | (uint)methodRow, 1,
                payload));
        }
        if (dependencyPhase is byte phase)
        {
            rows.Add(new TrRmContractRow(0x1600_0000u | (uint)methodRow, 4,
                new byte[] { 1, 0, 0, phase, 0, 0, 0, 0, 0, 0, 0, 0 }));
        }

        return rows.Count == first - 1 ? (0, 0) : (first, rows.Count - first + 1);
    }

    private static byte[] CreateConstructedFunctionPointerUseArtifact(
        bool includeRootlessFeature, bool useInBodyTypeMap = false)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("anchor", 0), ("Use", 0), ("Box", 0), ("T", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 6);
        TrRmFeatureRow[] featureRows = includeRootlessFeature
            ? [TrRmFeatureRow.NativeBoundaryContracts, TrRmFeatureRow.RootlessNativeValues]
            : [TrRmFeatureRow.NativeBoundaryContracts];
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1, featureRows.Length, 1),
        ], strings, 0, 1, 2, 0, featureRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 2, 0, featureRows.Length);
        ReadOnlyMemory<byte>[] typeSpecRows = [
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.NativeFunctionPointer(1, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Constructed(4, [3]),
        ];
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            1, 0, 1, 0, schema: TrRmArtifactSchema.Standard, methodDefCount: 2);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 5,
            1, 0, 1, 0, schema: TrRmArtifactSchema.Standard, methodDefCount: 2);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 5, 6, 0, 0, 1, 1, 1, 1, 0, 0),
        ], strings, modules, 0, 1, 1, 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 1,
            strings, modules, 0, 1, 1, 0);
        var parameterCount = useInBodyTypeMap ? 0 : 1;
        TrRmMethodSigRow[] signatureRows = [
            new TrRmMethodSigRow(1, 0, 0, 0, 0, 0),
            new TrRmMethodSigRow(2, 0, parameterCount == 0 ? 0 : 1,
                parameterCount, 0, 0),
        ];
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, parameterCount, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 2,
            typeSpecs, parameterCount, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 0, 0, 0, 0),
            new TrRmMethodDefRow(0, 4, 2, 0, 0, 0,
                useInBodyTypeMap ? 1 : 0),
        ], strings, modules, typeDefs, signatures, 0,
            useInBodyTypeMap ? 1 : 0);
        var methods = new TrRmMethodDefTableReader(methodBytes, 2,
            strings, modules, typeDefs, signatures, 0,
            useInBodyTypeMap ? 1 : 0);
        var parameterBytes = parameterCount == 0 ? [] : TrRmParamTableWriter.Encode([
            new TrRmParamRow(2, 0, 5, 0),
        ], signatures, typeSpecs);
        var genericParamBytes = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1300_0001, 0, 6, 0, 0, 1, 1, 1, 0, 0),
        ], strings, typeDefs, methods, signatures, typeSpecs, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], typeDefs, targetProfiles, 0);
        var sections = new List<TrRmSection>
        {
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 6, stringsBytes),
            new TrRmSection(0x0004, 0, (uint)featureRows.Length, featureBytes),
            new TrRmSection(0x0011, 0, 5, typeSpecBytes),
            new TrRmSection(0x0013, 0, 1, typeDefBytes),
            new TrRmSection(0x0015, 0, 1, layoutBytes),
            new TrRmSection(0x0016, 0, 2, methodBytes),
            new TrRmSection(0x0017, 0, 2, signatureBytes),
        };
        if (parameterCount != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0, 1, parameterBytes));
        }
        sections.Add(new TrRmSection(0x0019, 0, 1, genericParamBytes));
        if (useInBodyTypeMap)
        {
            sections.Add(new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([CreateTypeMapBody(5)])));
        }

        return TrRmArtifactWriter.Encode(sections);
    }

    private static byte[] CreateTypeMapBody(uint typeSpecRow)
    {
        var body = new byte[54];
        body[0] = 1;
        body[1] = 1;
        body[2] = 0x02;
        Write(body, 5, 1);
        Write(body, 9, 1);
        Write(body, 13, 1);
        Write(body, 17, 1);
        Write(body, 21, unchecked((int)(0x1100_0000u | typeSpecRow)));
        Write(body, 29, 1);
        Write(body, 41, 8);
        body[45] = 1;
        return body;
    }

    private static void ValidateConstructedBodyTypeMap(byte[] artifact)
    {
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(2);
    }

    private static byte[] CreateMethodMapBody(uint methodMapToken,
        bool includeFunctionPointerTypeMap)
    {
        var body = new byte[includeFunctionPointerTypeMap ? 62 : 54];
        body[0] = 1;
        body[1] = 1;
        body[2] = includeFunctionPointerTypeMap ? (byte)0x06 : (byte)0x04;
        var mapCountsStart = 5;
        if (includeFunctionPointerTypeMap)
        {
            Write(body, mapCountsStart, 1);
            mapCountsStart += sizeof(uint);
        }
        Write(body, mapCountsStart, 1);
        var bodyCountsStart = mapCountsStart + sizeof(uint);
        Write(body, bodyCountsStart, 1);
        Write(body, bodyCountsStart + sizeof(uint), 1);
        Write(body, bodyCountsStart + (2 * sizeof(uint)), 1);
        var mapStart = bodyCountsStart + (3 * sizeof(uint));
        if (includeFunctionPointerTypeMap)
        {
            Write(body, mapStart, 0x1100_0003);
            mapStart += sizeof(uint);
        }
        Write(body, mapStart, unchecked((int)methodMapToken));
        var blockStart = mapStart + sizeof(uint);
        Write(body, blockStart + sizeof(uint), 1);
        var recordOffsetsStart = blockStart + 12;
        Write(body, recordOffsetsStart + sizeof(uint), 8);
        body[recordOffsetsStart + (2 * sizeof(uint))] = 1;
        body[^1] = 0;
        return body;
    }

    private static void ValidateAnchorMethodMapArtifact(byte[] artifact)
    {
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    private static uint Read(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), unchecked((uint)value));

    private sealed record Fixture(byte[] Artifact, byte[] NativeImportBytes,
        string[] EntryNames, byte[] NativeExportBytes, string[] NativeExportNames);
}
