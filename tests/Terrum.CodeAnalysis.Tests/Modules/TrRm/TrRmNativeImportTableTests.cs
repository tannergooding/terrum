// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmNativeImportTableTests
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
        Assert.That(fixture.EntryNames, Is.EqualTo(["entry", "Data"]));
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
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateFixture(
                [callable], features: []).Artifact));
        Assert.DoesNotThrow(() => TrRmStructuralMetadataValidator.Validate(
            CreateFixture([callable], features: [TrRmFeatureRow.DirectNativeCalls]).Artifact));

        var data = new TrRmNativeImportRow(2, 2, 1, 0, 1, 6, 0);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateFixture(
                [data], features: [TrRmFeatureRow.DirectNativeCalls]).Artifact));
        Assert.DoesNotThrow(() => TrRmStructuralMetadataValidator.Validate(
            CreateFixture([data], features: [TrRmFeatureRow.NativeDataImports]).Artifact));
    }

    [Test]
    public void StandardRowsApplyNativeCallableSignatureAndImportedDataTypeConstraints()
    {
        var callable = new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateFixture(
                [callable], methodParameterMode: 1, encodeWithWriter: false).Artifact));

        var data = new TrRmNativeImportRow(2, 1, 0, 0, 1, 6, 0);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateFixture(
                [data], fieldTypeSpecRef: 2, encodeWithWriter: false).Artifact));
        _ = Assert.Throws<InvalidDataException>(() =>
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
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(autoLayout.Artifact));

        var differentProfileOnly = CreateFixture([data], fieldTypeSpecRef: 3,
            moduleTargetRequirement: TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            blittableProfileId: 2, encodeWithWriter: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(differentProfileOnly.Artifact));

        var profileOnlyForUnselectedTarget = CreateFixture([data], fieldTypeSpecRef: 3,
            blittableProfileId: TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
            encodeWithWriter: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(profileOnlyForUnselectedTarget.Artifact));
    }

    [Test]
    public void StandardNativeOutputCellRequiresEligibleDirectImport()
    {
        var import = new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7);
        var fixture = CreateFixture([import], methodParameterMode: 0,
            methodParameterPointeeCode: 8, methodParameterWritable: true,
            nativeOutputCellContract: true);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForForeignInvocationDraft(
                        fixture.Artifact)));

        var aggregate = CreateFixture([import], methodParameterMode: 0,
            methodParameterAggregate: true, moduleTargetRequirement:
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            blittableProfileId:
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
            nativeOutputCellContract: true);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForForeignInvocationDraft(
                        aggregate.Artifact)));

        var callBody = CreateFixture([import], methodParameterMode: 0,
            methodParameterPointeeCode: 8, methodParameterWritable: true,
            nativeOutputCellContract: true, nativeOutputCallBody: true,
            features: [
                TrRmFeatureRow.DirectNativeCalls,
                TrRmFeatureRow.NativeOutputCellsCandidate,
                TrRmFeatureRow.PublishedPredicates,
            ],
            moduleTargetRequirement:
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            methodOwnerType: 0);
        ValidateNativeOutputBodyMap(callBody.Artifact);
        var callBodyInput = new TrRmStandardArtifactInput(
            "trrm/1-draft-foreign-invocation-contracts-1",
            callBody.Artifact);
        var loadedCallBody = TrRmExecutableCompiler.LoadStandard(
            callBodyInput, "Module", []);
        var loadedCaller = loadedCallBody.Functions.Single(
            static function => function.Name == "Probe");
        BoundNativeOutputReservation? loadedReservation = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedCaller.Body,
            expression =>
            {
                if (expression is BoundNativeOutputReservation reservation)
                {
                    loadedReservation = reservation;
                }
            }), Is.True);
        Assert.That(loadedReservation, Is.Not.Null);

        var mismatchedHome = CreateFixture([import], methodParameterMode: 0,
            methodParameterPointeeCode: 8, methodParameterWritable: true,
            nativeOutputCellContract: true, nativeOutputCallBody: true,
            methodOwnerType: 0,
            outputCallHomeTypeSpecRef: 0xF200_000D);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateNativeOutputBodyMap(mismatchedHome.Artifact));

        var unreservedCall = CreateFixture([import], methodParameterMode: 0,
            methodParameterPointeeCode: 8, methodParameterWritable: true,
            nativeOutputCellContract: true, nativeOutputCallBody: true,
            methodOwnerType: 0,
            nativeOutputCallCode: [0xE0, 0, 1, 0, 0x18, 0]);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateNativeOutputBodyMap(unreservedCall.Artifact));

        var unbound = CreateFixture([], methodParameterMode: 0,
            methodParameterPointeeCode: 8, methodParameterWritable: true,
            nativeOutputCellContract: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForForeignInvocationDraft(
                        unbound.Artifact)));

        _ = Assert.Throws<InvalidDataException>(() =>
            CreateFixture([import], methodParameterMode: 0,
                methodParameterPointeeCode: 8, methodParameterWritable: false,
                nativeOutputCellContract: true));
    }

    [Test]
    public void StandardValidatorDoesNotUseDevelopmentFunctionPointerSignatureRows()
    {
        var data = new TrRmNativeImportRow(2, 1, 0, 0, 1, 6, 0);
        var developmentIndexedType = CreateFixture([data],
            fieldTypeSpecRef: 4, nativeFunctionPointerSignatureCount: 1,
            encodeWithWriter: false);
        _ = Assert.Throws<InvalidDataException>(() =>
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
            _ = Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator.Validate(
                    CreateFixture([row], encodeWithWriter: false).Artifact));
        }

        var instanceMethod = CreateFixture(
            [new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7)],
            methodReceiver: 1, encodeWithWriter: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(instanceMethod.Artifact));

        var instanceField = CreateFixture(
            [new TrRmNativeImportRow(2, 1, 0, 0, 1, 6, 0)],
            fieldFlags: 0, encodeWithWriter: false);
        _ = Assert.Throws<InvalidDataException>(() =>
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
            _ = Assert.Throws<InvalidDataException>(() =>
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
            _ = Assert.Throws<InvalidDataException>(() =>
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
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(nonAsciiEntry.Artifact));

        var methodDuplicate = new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateFixture(
                [methodDuplicate, methodDuplicate], encodeWithWriter: false).Artifact));
        var fieldDuplicate = new TrRmNativeImportRow(2, 1, 0, 0, 1, 6, 0);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateFixture(
                [fieldDuplicate, fieldDuplicate], encodeWithWriter: false).Artifact));
    }

    [Test]
    public void EntryNameFallbackPreservesDeclarationTextAndExplicitNamesRequireOnlyAscii()
    {
        var method = new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7);
        var explicitAscii = CreateFixture([method], entryName: "entry \u0001");
        Assert.That(explicitAscii.EntryNames, Is.EqualTo(["entry \u0001"]));
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(explicitAscii.Artifact));

        var methodDefault = method with { EntryNameStringRow = 0 };
        var fieldDefault = new TrRmNativeImportRow(2, 1, 0, 0, 1, 6, 0);
        var declarationNames = CreateFixture([methodDefault, fieldDefault],
            methodName: "Café", methodNameEncoding: 1,
            fieldName: "Donnée", fieldNameEncoding: 1);
        Assert.That(declarationNames.EntryNames, Is.EqualTo(["Café", "Donnée"]));
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
        Assert.That(fixture.NativeExportNames, Is.EqualTo(["export", "Data"]));
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(fixture.Artifact));

        var noFeature = CreateFixture([], features: [], nativeExports: [method],
            includeNativeCallableContract: true);
        _ = Assert.Throws<InvalidDataException>(() =>
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
            _ = Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator.Validate(fixture.Artifact));
        }

        var missingClause = CreateFixture([], nativeExports: [method],
            encodeWithWriter: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(missingClause.Artifact));

        var invalidFlags = CreateFixture([], nativeExports: [method],
            includeNativeCallableContract: true, nativeExportFlags: 1,
            encodeWithWriter: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(invalidFlags.Artifact));

        var invalidReserved = CreateFixture([], nativeExports: [method],
            includeNativeCallableContract: true, nativeExportReserved: 1,
            encodeWithWriter: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(invalidReserved.Artifact));

        var instanceMethod = CreateFixture([], nativeExports: [method],
            includeNativeCallableContract: true, methodReceiver: 1,
            encodeWithWriter: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(instanceMethod.Artifact));

        var instanceField = CreateFixture([], nativeExports: [field],
            fieldFlags: 0, encodeWithWriter: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(instanceField.Artifact));
    }

    [Test]
    public void NativeExportRowsRejectDuplicateOwnersAndSymbolNames()
    {
        var method = new TrRmNativeExportRow(1, 1, 8, 2);
        var field = new TrRmNativeExportRow(2, 1, 8, 0);
        var duplicateOwner = CreateFixture([], nativeExports: [method, method],
            includeNativeCallableContract: true, encodeWithWriter: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(duplicateOwner.Artifact));

        var duplicateName = CreateFixture([], nativeExports: [method, field],
            includeNativeCallableContract: true, encodeWithWriter: false);
        _ = Assert.Throws<InvalidDataException>(() =>
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
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(assertionAnchor.Artifact));

        var missingFeature = CreateFixture([], features: [],
            standardNativeFunctionPointerAnchorMethodDefRow: 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(missingFeature.Artifact));

        var valueFeature = CreateFixture([], fieldTypeSpecRef: 4,
            standardNativeFunctionPointerAnchorMethodDefRow: 1,
            features: [TrRmFeatureRow.RootlessNativeValues]);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(valueFeature.Artifact));

        var missingValueFeature = CreateFixture([], fieldTypeSpecRef: 4,
            standardNativeFunctionPointerAnchorMethodDefRow: 1,
            features: [TrRmFeatureRow.NativeBoundaryContracts]);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(missingValueFeature.Artifact));

        var callableAnchor = CreateFixture([], includeNativeCallableContract: true,
            standardNativeFunctionPointerAnchorMethodDefRow: 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(callableAnchor.Artifact));

        var importedAnchor = CreateFixture([
                new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7),
            ], standardNativeFunctionPointerAnchorMethodDefRow: 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(importedAnchor.Artifact));
    }

    [Test]
    public void StandardFeatureTwoClosureIncludesFunctionPointersInConstructedArguments()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateConstructedFunctionPointerUseArtifact(includeRootlessFeature: false)));
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateConstructedFunctionPointerUseArtifact(includeRootlessFeature: true)));
    }

    [Test]
    public void StandardFeatureTwoClosureIncludesUnionPayloadFunctionPointers()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateConstructedFunctionPointerUseArtifact(
                    includeRootlessFeature: false,
                    useInUnionCaseField: true)));
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateConstructedFunctionPointerUseArtifact(
                    includeRootlessFeature: true,
                    useInUnionCaseField: true)));
    }

    [Test]
    public void StandardFeatureTwoClosureIncludesTextCaptureFunctionPointers()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateConstructedFunctionPointerUseArtifact(
                    includeRootlessFeature: false,
                    useInTextCapture: true)));
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateConstructedFunctionPointerUseArtifact(
                    includeRootlessFeature: true,
                    useInTextCapture: true)));
    }

    [Test]
    public void StaticFieldFeatureTwoClosureIncludesFunctionPointerValues()
    {
        var withoutFeature = CreateConstructedFunctionPointerUseArtifact(
            includeRootlessFeature: false, useInStaticField: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(withoutFeature)));

        var withFeature = CreateConstructedFunctionPointerUseArtifact(
            includeRootlessFeature: true, useInStaticField: true);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(withFeature)));
    }

    [Test]
    public void StaticPublicationTypeIdentityUsesFunctionPointerAnchors()
    {
        var equivalentAnchors = CreateConstructedFunctionPointerUseArtifact(
            includeRootlessFeature: true, useInStaticPublication: true);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        equivalentAnchors)));

        var differentConvention = CreateConstructedFunctionPointerUseArtifact(
            includeRootlessFeature: true, useInStaticPublication: true,
            secondAnchorConvention: 2);
        var differentSignature = CreateConstructedFunctionPointerUseArtifact(
            includeRootlessFeature: true, useInStaticPublication: true,
            secondAnchorHasUnitResult: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        differentConvention)));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        differentSignature)));
    }

    [Test]
    public void StandardBodyTypeMapFeatureClosureIncludesConstructedFunctionPointerArguments()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
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
        _ = Assert.Throws<InvalidDataException>(() =>
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
    public void StandardPropertyGetterAcceptsEquivalentFunctionPointerAnchors()
    {
        var artifact = CreateAnchorMethodMapArtifact(
            0x1600_0001, includeRootlessFeature: true,
            secondAnchorConvention: 1, distinctSecondAnchor: true,
            includePropertyFunctionPointerIdentity: true);

        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(artifact)));
    }

    [Test]
    public void StandardPropertyGetterRejectsDifferentFunctionPointerAnchors()
    {
        var differentConvention = CreateAnchorMethodMapArtifact(
            0x1600_0001, includeRootlessFeature: true,
            secondAnchorConvention: 2, distinctSecondAnchor: true,
            includePropertyFunctionPointerIdentity: true);
        var differentSignature = CreateAnchorMethodMapArtifact(
            0x1600_0001, includeRootlessFeature: true,
            secondAnchorConvention: 1, distinctSecondAnchor: true,
            secondAnchorHasUnitResult: true,
            includePropertyFunctionPointerIdentity: true);
        var differentEffects = CreateAnchorMethodMapArtifact(
            0x1600_0001, includeRootlessFeature: true,
            secondAnchorConvention: 1, distinctSecondAnchor: true,
            secondAnchorEffectsMask: 4,
            includePropertyFunctionPointerIdentity: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(differentConvention)));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(differentSignature)));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(differentEffects)));
    }

    [Test]
    public void StandardBodyTypeMapRequiresRootlessNativeValuesForFunctionPointers()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateAnchorMethodMapArtifact(CreateAnchorMethodMapArtifact(
                0x1600_0001, includeFunctionPointerTypeMap: true)));
        Assert.DoesNotThrow(() =>
            ValidateAnchorMethodMapArtifact(CreateAnchorMethodMapArtifact(
                0x1600_0001, includeRootlessFeature: true,
                includeFunctionPointerTypeMap: true)));
    }

    [Test]
    public void ForeignInvocationDraftNativeAddressMapBindsTargetAnchorAndResult()
    {
        var registerResult = CreateAnchorMethodMapArtifact(0x1600_0001,
            includeNativeAddress: true, firstAnchorConvention: 0,
            anchorHasUnitResult: true,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);
        Assert.DoesNotThrow(() => ValidateAnchorMethodMapArtifact(registerResult,
            TrRmArtifactSchema.ForeignInvocationDraft));

        var homeResult = CreateAnchorMethodMapArtifact(0x1600_0001,
            includeNativeAddress: true, firstAnchorConvention: 0,
            anchorHasUnitResult: true, nativeAddressResultKind: 2,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);
        Assert.DoesNotThrow(() => ValidateAnchorMethodMapArtifact(homeResult,
            TrRmArtifactSchema.ForeignInvocationDraft));

        var nativeCallableTarget = CreateAnchorMethodMapArtifact(0x1600_0001,
            includeNativeAddress: true, includeNativeCallableTarget: true,
            firstAnchorConvention: 1, anchorHasUnitResult: true,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);
        Assert.DoesNotThrow(() =>
            ValidateAnchorMethodMapArtifact(nativeCallableTarget,
                TrRmArtifactSchema.ForeignInvocationDraft));

        var wrongConvention = CreateAnchorMethodMapArtifact(0x1600_0001,
            includeNativeAddress: true, firstAnchorConvention: 1,
            anchorHasUnitResult: true,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateAnchorMethodMapArtifact(wrongConvention,
                TrRmArtifactSchema.ForeignInvocationDraft));

        var wrongNativeCallableConvention = CreateAnchorMethodMapArtifact(0x1600_0001,
            includeNativeAddress: true, includeNativeCallableTarget: true,
            firstAnchorConvention: 2, anchorHasUnitResult: true,
            nativeCallableConvention: 1,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateAnchorMethodMapArtifact(wrongNativeCallableConvention,
                TrRmArtifactSchema.ForeignInvocationDraft));

        var wrongSignature = CreateAnchorMethodMapArtifact(0x1600_0001,
            includeNativeAddress: true, firstAnchorConvention: 0,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateAnchorMethodMapArtifact(wrongSignature,
                TrRmArtifactSchema.ForeignInvocationDraft));

        var anchorAsTarget = CreateAnchorMethodMapArtifact(0x1600_0002,
            includeNativeAddress: true, firstAnchorConvention: 0,
            anchorHasUnitResult: true,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateAnchorMethodMapArtifact(anchorAsTarget,
                TrRmArtifactSchema.ForeignInvocationDraft));

        var wrongTypeMapForm = CreateAnchorMethodMapArtifact(0x1600_0001,
            includeNativeAddress: true, firstAnchorConvention: 0,
            anchorHasUnitResult: true,
            nativeAddressPointerTypeToken: 0x1100_0002,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateAnchorMethodMapArtifact(wrongTypeMapForm,
                TrRmArtifactSchema.ForeignInvocationDraft));

        var wrongHomeType = CreateAnchorMethodMapArtifact(0x1600_0001,
            includeNativeAddress: true, firstAnchorConvention: 0,
            anchorHasUnitResult: true, nativeAddressResultKind: 2,
            nativeAddressHomeTypeToken: 0x1100_0002,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateAnchorMethodMapArtifact(wrongHomeType,
                TrRmArtifactSchema.ForeignInvocationDraft));
    }

    [Test]
    public void ForeignInvocationDraftNativeAddressMapUsesNativeImportConvention()
    {
        var valid = CreateNativeImportAddressArtifact(callConv: 1);
        Assert.DoesNotThrow(() => ValidateNativeAddressArtifact(valid,
            TrRmArtifactSchema.ForeignInvocationDraft));

        var wrongConvention = CreateNativeImportAddressArtifact(callConv: 2);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateNativeAddressArtifact(wrongConvention,
                TrRmArtifactSchema.ForeignInvocationDraft));
    }

    [Test]
    public void ForeignInvocationDraftNativeImportRetainsFullWidthParameterContract()
    {
        var features = new[] {
            TrRmFeatureRow.DirectNativeCalls,
            TrRmFeatureRow.NativeDataImports,
            TrRmFeatureRow.ForeignInvocationContractsCandidate,
        };
        Assert.That(features.Select(static feature => feature.FeatureId),
            Is.EquivalentTo(new uint[] { 3, 6, 16 }));
        var fixture = CreateFixture([
                new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7),
            ],
            features: features,
            methodParameterMode: 0,
            foreignInvocationDraft: true,
            nativeParameterContract: StandardNativeParameterContract(
                ordinal: 0, access: 2, extentKind: 1, extent: uint.MaxValue),
            methodParameterPointeeCode: 0x0D,
            methodParameterWritable: true);

        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(fixture.Artifact));
        Assert.That(tables.StandardNativeImports.HasImportForMethod(1), Is.True);
        var parameter = tables.Contracts.GetNativeParameters(1).Single();
        Assert.That(parameter.ParameterOrdinal, Is.Zero);
        Assert.That(parameter.Access, Is.EqualTo(2));
        Assert.That(parameter.Extent, Is.EqualTo(uint.MaxValue));

        _ = Assert.Throws<InvalidDataException>(() => CreateFixture([
                new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7),
            ],
            features: [TrRmFeatureRow.DirectNativeCalls,
                TrRmFeatureRow.NativeDataImports],
            methodParameterMode: 0,
            foreignInvocationDraft: true,
            nativeParameterContract: StandardNativeParameterContract(
                ordinal: 0, access: 2, extentKind: 0, extent: 0),
            methodParameterPointeeCode: 0x0D,
            methodParameterWritable: true));

        _ = Assert.Throws<InvalidDataException>(() => CreateFixture([
                new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7),
            ],
            features: features,
            methodParameterMode: 0,
            nativeParameterContract: StandardNativeParameterContract(
                ordinal: 0, access: 2, extentKind: 0, extent: 0),
            methodParameterPointeeCode: 0x0D,
            methodParameterWritable: true));
    }

    [Test]
    public void ForeignInvocationDraftNativeAddressRequiresExactParameterProjection()
    {
        var equalContracts = CreateForeignInvocationNativeAddressArtifact(1, 1);
        Assert.DoesNotThrow(() =>
            ValidateForeignInvocationNativeAddressArtifact(equalContracts));

        var differentExtent = CreateForeignInvocationNativeAddressArtifact(1, 2);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateForeignInvocationNativeAddressArtifact(differentExtent));

        var missingAnchorContract = CreateForeignInvocationNativeAddressArtifact(1, null);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateForeignInvocationNativeAddressArtifact(missingAnchorContract));
    }

    private static Fixture CreateFixture(
        TrRmNativeImportRow[] rows,
        TrRmFeatureRow[]? features = null,
        IReadOnlyList<TrRmNativeExportRow>? nativeExports = null,
        string libraryName = "kernel32",
        int libraryEncoding = 0,
        string entryName = "entry",
        int entryEncoding = 0,
        string methodName = "Call",
        int methodNameEncoding = 0,
        int methodOwnerType = 1,
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
        bool encodeWithWriter = true,
        bool foreignInvocationDraft = false,
        TrRmContractRow? nativeParameterContract = null,
        byte? methodParameterPointeeCode = null,
        bool methodParameterWritable = false,
        bool nativeOutputCellContract = false,
        bool methodParameterAggregate = false,
        bool nativeOutputCallBody = false,
        uint outputCallHomeTypeSpecRef = 0xF200_0008,
        byte[]? nativeOutputCallCode = null)
    {
        foreignInvocationDraft |= nativeOutputCellContract;
        if (nativeOutputCallBody && !nativeOutputCellContract)
        {
            throw new ArgumentException(
                "A NativeOutput call Body requires its declaration contract.",
                nameof(nativeOutputCallBody));
        }

        var methodCount = nativeOutputCallBody ? 2 : 1;
        var schema = foreignInvocationDraft
            ? TrRmArtifactSchema.ForeignInvocationDraft
            : TrRmArtifactSchema.Standard;
        if (features is null)
        {
            var defaultFeatures = new List<TrRmFeatureRow>();
            if (rows.Length != 0)
            {
                defaultFeatures.Add(TrRmFeatureRow.DirectNativeCalls);
                defaultFeatures.Add(TrRmFeatureRow.NativeDataImports);
            }
            if (nativeExports is { Count: > 0 })
            {
                defaultFeatures.Add(TrRmFeatureRow.NativeExportEntries);
            }
            if (standardNativeFunctionPointerAnchorMethodDefRow.HasValue
                && rows.Length == 0 && nativeExports is not { Count: > 0 })
            {
                defaultFeatures.Add(TrRmFeatureRow.RootlessNativeValues);
            }
            if (standardAnchorPredicatePhase.HasValue)
            {
                defaultFeatures.Add(TrRmFeatureRow.PublishedPredicates);
            }
            if (nativeOutputCellContract)
            {
                defaultFeatures.Add(TrRmFeatureRow.NativeOutputCellsCandidate);
                defaultFeatures.Add(TrRmFeatureRow.PublishedPredicates);
            }
            if (foreignInvocationDraft && nativeParameterContract is not null)
            {
                defaultFeatures.Add(TrRmFeatureRow.ForeignInvocationContractsCandidate);
            }
            if (defaultFeatures.Count == 0)
            {
                defaultFeatures.Add(TrRmFeatureRow.DirectNativeCalls);
                defaultFeatures.Add(TrRmFeatureRow.NativeDataImports);
            }

            features = [.. defaultFeatures];
        }
        var stringRows = new List<(string Text, int Encoding)>
        {
            ("", 0), ("Module", 0), ("T", 0), (methodName, methodNameEncoding),
            (fieldName, fieldNameEncoding),
            (libraryName, libraryEncoding), (entryName, entryEncoding), ("export", 0),
        };
        if (nativeOutputCallBody)
        {
            stringRows.Add(("Probe", 0));
        }
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringBytes, stringRows.Count);
        var featureBytes = features.Length == 0 ? []
            : TrRmFeatureTableWriter.Encode(features, schema);
        var featureTable = new TrRmFeatureTableReader(featureBytes, features.Length, schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, moduleTargetRequirement, 0, 0, 1, 1, 1, 1,
                features.Length == 0 ? 0 : 1, features.Length),
        ], strings, 0, 1, methodCount, 0, features.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, methodCount, 0, features.Length);
        if (foreignInvocationDraft)
        {
            TrRmFeatureManifestValidator.Validate(featureTable, modules, schema);
        }
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
        uint methodParameterTypeSpecRef = 1;
        if (methodParameterPointeeCode is byte pointeeCode)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C,
                pointeeCode, methodParameterWritable ? (byte)1 : (byte)0));
            methodParameterTypeSpecRef = checked((uint)typeSpecRows.Count);
        }
        else if (methodParameterAggregate)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02, 1));
            var pointerType = new byte[7];
            pointerType[0] = 0x0C;
            pointerType[1] = 1;
            pointerType[2] = 0x8C;
            Write(pointerType, 3, typeSpecRows.Count);
            typeSpecRows.Add(pointerType);
            methodParameterTypeSpecRef = checked((uint)typeSpecRows.Count);
        }
        if (nativeOutputCellContract)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Builtin(1));
        }
        var typeSpecSchema = foreignInvocationDraft
            ? TrRmArtifactSchema.ForeignInvocationDraft
            : nativeFunctionPointerSignatureCount == 0
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
            + (standardAnchorPredicatePhase.HasValue ? 1 : 0)
            + (nativeParameterContract.HasValue ? 1 : 0)
            + (nativeOutputCellContract ? 5 : 0);
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
                profileLayoutFlags, 0,
                profileLayoutKind is 1 or 2 ? (byte)1 : (byte)0, 0, 0);
        }
        var layoutBytes = encodeWithWriter
            ? TrRmLayoutTableWriter.Encode(layoutRows, typeDefs, targetProfiles, 0)
            : EncodeRawLayoutRows(layoutRows);
        var layouts = encodeWithWriter
            ? new TrRmLayoutTableReader(layoutBytes, layoutCount, typeDefs,
                targetProfiles, 0)
            : default;
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 5, fieldTypeSpecRef, fieldFlags),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 1,
            strings, typeDefs, typeSpecs);
        var parameterCount = methodParameterMode.HasValue ? 1 : 0;
        TrRmMethodSigRow[] signatureRows = nativeOutputCallBody
            ? [
                new TrRmMethodSigRow(1, methodReceiver,
                    parameterCount == 0 ? 0 : 1, parameterCount, 0, 0),
                new TrRmMethodSigRow(2, 0, 0, 0, 0, 0),
            ]
            : [
                new TrRmMethodSigRow(1, methodReceiver,
                    parameterCount == 0 ? 0 : 1, parameterCount, 0, 0),
            ];
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, parameterCount, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Length,
            typeSpecs, parameterCount, 0);
        var parameterBytes = parameterCount == 0 ? [] : TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, methodParameterTypeSpecRef, methodParameterMode!.Value),
        ], signatures, typeSpecs, schema);
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
        if (nativeParameterContract is TrRmContractRow nativeParameterRow)
        {
            contractRows.Add(nativeParameterRow);
        }
        if (standardAnchorPredicatePhase is byte predicatePhase)
        {
            var predicatePayload = new byte[12];
            predicatePayload[0] = predicatePhase;
            Write(predicatePayload, 8, 1);
            contractRows.Add(new TrRmContractRow(0x1600_0001, 0x0B,
                predicatePayload));
        }
        if (nativeOutputCellContract)
        {
            contractRows.Add(NativeOutputStateContract(0, 1, 1));
            contractRows.Add(NativeOutputStateContract(0, 2, 0));
            contractRows.Add(NativeOutputDependencyContract(0));
            contractRows.Add(PredicateGraphContract(0, 1));
            contractRows.Add(PredicateGraphContract(1, 2));
        }

        TrRmMethodDefRow[] methodRows = nativeOutputCallBody
            ? [
                new TrRmMethodDefRow(methodOwnerType, 4, 1, 2, 1,
                    contractRows.Count, 0),
                new TrRmMethodDefRow(0, 9, 2, 1, 0, 0, 1),
            ]
            : [
                new TrRmMethodDefRow(methodOwnerType, 4, 1, 2, 1,
                    contractRows.Count, 0),
            ];
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows, strings,
            modules, typeDefs, signatures, contractRows.Count,
            nativeOutputCallBody ? 1 : 0);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodRows.Length, strings,
            modules, typeDefs, signatures, contractRows.Count,
            nativeOutputCallBody ? 1 : 0);
        var contractBytes = foreignInvocationDraft
            ? TrRmContractTableWriter.EncodeForeignInvocationDraft(contractRows,
                typeDefs, methods, signatures, parameters, typeSpecs,
                featureTable, modules, nativeOutputCellContract ? 2
                    : standardAnchorPredicatePhase.HasValue ? 1 : 0)
            : standardAnchorPredicatePhase.HasValue
            ? TrRmContractTableWriter.EncodeWithPredicateGraphs(contractRows,
                typeDefs, methods, signatures, parameters, typeSpecs,
                featureTable, modules, predicateGraphCount: 1)
            : TrRmContractTableWriter.Encode(contractRows,
                typeDefs, methods, signatures, parameters, typeSpecs);
        var contracts = foreignInvocationDraft
            ? new TrRmContractTableReader(contractBytes, contractRows.Count,
                typeDefs, methods, signatures, parameters, typeSpecs, 0,
                featureTable, modules, schema,
                predicateGraphCount: nativeOutputCellContract ? 2
                    : standardAnchorPredicatePhase.HasValue ? 1 : 0)
            : standardAnchorPredicatePhase.HasValue
            ? new TrRmContractTableReader(contractBytes, contractRows.Count,
                typeDefs, methods, signatures, parameters, typeSpecs, 0,
                featureTable, modules, TrRmArtifactSchema.Standard,
                predicateGraphCount: 1)
            : new TrRmContractTableReader(contractBytes, contractRows.Count,
                typeDefs, methods, signatures, parameters, typeSpecs);
        var nativeImportBytes = rows.Length == 0 ? []
            : encodeWithWriter
            ? TrRmNativeImportTableWriter.Encode(rows, strings, methods, fields,
                typeDefs, default, modules, signatures, parameters, typeSpecs, contracts,
                layouts)
            : EncodeRawRows(rows);
        byte[]? bodyBytes = null;
        if (nativeOutputCallBody)
        {
            var maps = TrRmRegisterBodyMapBuilder.Create(
                [outputCallHomeTypeSpecRef], [0x1600_0001]);
            var homeMap = new byte[8];
            homeMap[0] = 3;
            Write(homeMap, 2, maps.GetTypeMapIndex(
                outputCallHomeTypeSpecRef));
            var entryState = new byte[8];
            entryState[0] = 0x01;
            var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(
                1, 1, maps.GetMethodMapIndex(0x1600_0001), 0,
                [new TrRmRegisterBodyCallInput(2, 6, 0)], [], schema: schema);
            var reservation = TrRmRegisterBodyRecordWriter
                .EncodeNativeOutputReservation(1, 0, 0);
            var code = nativeOutputCallCode ?? [
                0xDF, 0, 2, 0, 0xE0, 0, 1, 0, 0x18, 0,
            ];
            bodyBytes = TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
                maps.MethodMap, default, default,
                [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
                [entryState, callSite, reservation], code,
                contractRows.Count, schema);
        }
        var entryNames = new string[encodeWithWriter ? rows.Length : 0];
        if (encodeWithWriter)
        {
            var nativeImports = new TrRmNativeImportTableReader(nativeImportBytes,
                rows.Length, strings, methods, fields, typeDefs, default, modules,
                signatures, parameters, typeSpecs, contracts, layouts);
            for (var row = 1; row <= rows.Length; row++)
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
        if (nativeOutputCellContract)
        {
            predicateGraphBytes = TrRmPredicateGraphTableWriter.Encode([
                new TrRmPredicateGraphRow(1, 1),
                new TrRmPredicateGraphRow(2, 2),
            ], nodeCount: 2, readFootprintCount: 2);
            predicateReadFootprintBytes =
                TrRmPredicateReadFootprintTableWriter.Encode([
                    new TrRmPredicateReadFootprintRow(1, 1, 0, 0),
                    new TrRmPredicateReadFootprintRow(2, 1, 0, 0),
                ], rootCount: 2, dependencyCount: 0);
            predicateReadRootBytes = new byte[16];
            predicateReadRootBytes[0] = 5;
            predicateReadRootBytes[8] = 5;
            predicateNodeBytes = new byte[32];
            Write(predicateNodeBytes, 0, 3);
            Write(predicateNodeBytes, 4, typeSpecRows.Count);
            Write(predicateNodeBytes, 8, 1);
            Write(predicateNodeBytes, 12, 1);
            Write(predicateNodeBytes, 16, 4);
            Write(predicateNodeBytes, 20, typeSpecRows.Count);
            Write(predicateNodeBytes, 24, 2);
            Write(predicateNodeBytes, 28, 1);
            predicateNodeOperandBytes = new byte[16];
            predicateNodeOperandBytes[0] = 1;
            Write(predicateNodeOperandBytes, 4, 1);
            predicateNodeOperandBytes[8] = 1;
            Write(predicateNodeOperandBytes, 12, 2);
        }
        else if (standardAnchorPredicatePhase.HasValue)
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
        if (features.Length != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0, (uint)features.Length, featureBytes));
        }
        sections.Add(new TrRmSection(0x0011, 0, (uint)typeSpecRows.Count, typeSpecBytes));
        sections.Add(new TrRmSection(0x0013, 0, 1, typeDefBytes));
        sections.Add(new TrRmSection(0x0014, 0, 1, fieldBytes));
        sections.Add(new TrRmSection(0x0015, 0, (uint)layoutCount, layoutBytes));
        sections.Add(new TrRmSection(0x0016, 0,
            (uint)methodRows.Length, methodBytes));
        sections.Add(new TrRmSection(0x0017, 0,
            (uint)signatureRows.Length, signatureBytes));
        if (parameterCount != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0, (uint)parameterCount, parameterBytes));
        }
        sections.Add(new TrRmSection(0x001B, 0,
            (uint)contractRows.Count, contractBytes));
        if (bodyBytes is not null)
        {
            sections.Add(new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([bodyBytes])));
        }
        if (rows.Length != 0)
        {
            sections.Add(new TrRmSection(0x0021, 0, (uint)rows.Length,
                nativeImportBytes));
        }
        if (nativeExportRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0022, 0,
                (uint)nativeExportRows.Count, nativeExportBytes));
        }
        if (predicateGraphBytes is not null)
        {
            var predicateCount = nativeOutputCellContract ? 2u : 1u;
            sections.Add(new TrRmSection(0x0037, 0, predicateCount,
                predicateGraphBytes));
            sections.Add(new TrRmSection(0x0038, 0, predicateCount,
                predicateReadFootprintBytes!));
            sections.Add(new TrRmSection(0x0039, 0, predicateCount,
                predicateReadRootBytes!));
            sections.Add(new TrRmSection(0x003B, 0, predicateCount,
                predicateNodeBytes!));
            sections.Add(new TrRmSection(0x003C, 0, predicateCount,
                predicateNodeOperandBytes!));
        }
        if (blittableProfileId.HasValue)
        {
            sections.Add(new TrRmSection(0x0048, 0, 1, targetProfileBytes));
        }

        return new Fixture(TrRmArtifactWriter.Encode(sections, schema),
            nativeImportBytes, entryNames, nativeExportBytes, nativeExportNames);
    }

    private static TrRmContractRow StandardNativeParameterContract(
        uint ordinal, byte access, byte extentKind, uint extent)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, ordinal);
        payload[4] = access;
        payload[5] = 1;
        payload[6] = extentKind;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), extent);
        return new TrRmContractRow(0x1600_0001, 0x0E, payload);
    }

    private static void ValidateNativeOutputBodyMap(byte[] artifact)
    {
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        tables.ValidateRegisterBodyMapsForMethod(2);
    }

    private static TrRmContractRow NativeOutputStateContract(uint ordinal,
        uint predicateGraph, byte state)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, ordinal);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
            predicateGraph);
        payload[8] = state;
        return new TrRmContractRow(0x1600_0001, 2, payload);
    }

    private static TrRmContractRow PredicateGraphContract(uint ordinal,
        uint predicateGraph)
    {
        var payload = new byte[12];
        payload[0] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), ordinal);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8),
            predicateGraph);
        return new TrRmContractRow(0x1600_0001, 0x0B, payload);
    }

    private static TrRmContractRow NativeOutputDependencyContract(
        uint ordinal)
    {
        var payload = new byte[12];
        payload[0] = 2;
        payload[3] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), ordinal);
        return new TrRmContractRow(0x1600_0001, 4, payload);
    }

    private static byte[] EncodeRawRows(TrRmNativeImportRow[] rows)
    {
        var bytes = new byte[rows.Length * 16];
        for (var index = 0; index < rows.Length; index++)
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
        bool includeNativeAddress = false,
        bool includeNativeCallableTarget = false,
        byte firstAnchorConvention = 1,
        byte nativeCallableConvention = 1,
        bool anchorHasUnitResult = false,
        byte nativeAddressResultKind = 1,
        uint? nativeAddressPointerTypeToken = null,
        uint? nativeAddressHomeTypeToken = null,
        byte? secondAnchorConvention = null,
        bool distinctSecondAnchor = false,
        bool secondAnchorHasUnitResult = false,
        uint? anchorEffectsMask = null,
        uint? secondAnchorEffectsMask = null,
        byte? anchorDependencyPhase = null,
        byte? secondAnchorDependencyPhase = null,
        TrRmArtifactSchema schema = TrRmArtifactSchema.Standard,
        bool includePropertyFunctionPointerIdentity = false)
    {
        if (includeNativeAddress
            != (schema == TrRmArtifactSchema.ForeignInvocationDraft))
        {
            throw new ArgumentException(
                "NativeAddress test artifacts require explicit ForeignInvocationDraft selection.",
                nameof(schema));
        }

        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("main", 0), ("anchor", 0), ("anchor2", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 5);
        var methodCount = distinctSecondAnchor ? 3 : 2;
        if (includePropertyFunctionPointerIdentity && !distinctSecondAnchor)
        {
            throw new ArgumentException(
                "Property identity coverage requires two anchor rows.",
                nameof(includePropertyFunctionPointerIdentity));
        }
        var typeDefCount = includePropertyFunctionPointerIdentity ? 1 : 0;
        var artifactMethodCount = methodCount
            + (includePropertyFunctionPointerIdentity ? 1 : 0);
        TrRmFeatureRow[] featureRows = includeNativeAddress
            ? [TrRmFeatureRow.NativeBoundaryContracts, TrRmFeatureRow.RootlessNativeValues,
                TrRmFeatureRow.DirectNativeCalls, TrRmFeatureRow.NativeExportEntries,
                TrRmFeatureRow.ForeignInvocationContractsCandidate]
            : includeRootlessFeature
                ? [TrRmFeatureRow.NativeBoundaryContracts, TrRmFeatureRow.RootlessNativeValues]
                : [TrRmFeatureRow.NativeBoundaryContracts];
        var featuresBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var featureReader = new TrRmFeatureTableReader(featuresBytes,
            featureRows.Length, schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1,
                featureRows.Length, 1),
        ], strings, 0, typeDefCount, artifactMethodCount, 0,
            featureRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, typeDefCount, artifactMethodCount, 0, featureRows.Length);
        var typeSpecRows = new List<ReadOnlyMemory<byte>>
        {
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.NativeFunctionPointer(firstAnchorConvention, 2),
        };
        if (secondAnchorConvention is byte convention)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.NativeFunctionPointer(
                convention, distinctSecondAnchor ? 3u : 2u));
        }
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount, 0, 0, 0, schema: schema,
            methodDefCount: artifactMethodCount);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecRows.Count,
            typeDefCount, 0, 0, 0, schema: schema,
            methodDefCount: artifactMethodCount);
        var signatureRows = new List<TrRmMethodSigRow>
        {
            new TrRmMethodSigRow(0xF200_0000, 0, 0, 0, 0, 0),
            new TrRmMethodSigRow(anchorHasUnitResult ? 0xF200_0000u : 0xF200_0008u,
                0, 0, 0, 0, 0),
        };
        if (distinctSecondAnchor)
        {
            signatureRows.Add(new TrRmMethodSigRow(
                secondAnchorHasUnitResult ? 0xF200_0000u : 0xF200_0008u,
                0, 0, 0, 0, 0));
        }
        var contractRows = new List<TrRmContractRow>();
        var targetContractFirst = 0;
        if (includeNativeCallableTarget)
        {
            targetContractFirst = contractRows.Count + 1;
            contractRows.Add(new TrRmContractRow(0x1600_0001, 1,
                new byte[] { 4, 0, 0, 0 }));
            contractRows.Add(new TrRmContractRow(0x1600_0001, 0x0C,
                new byte[] { nativeCallableConvention, 0, 0, 0 }));
        }
        var (First, Count) = AddAnchorContracts(contractRows, 2,
            anchorEffectsMask, anchorDependencyPhase);
        var secondAnchorContractFirst = distinctSecondAnchor
            ? AddAnchorContracts(contractRows, 3, secondAnchorEffectsMask,
                secondAnchorDependencyPhase)
            : (First: 0, Count: 0);
        var typeDefBytes = typeDefCount == 0 ? [] : TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 6, 0, 0, 0, 0, 1, 1, 0, 0),
        ], strings, modules, 0, 0, 1, contractRows.Count);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules, 0, 0, typeDefCount, contractRows.Count);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = typeDefCount == 0 ? [] : TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], typeDefs, targetProfiles, 0);
        var methodRows = new List<TrRmMethodDefRow>
        {
            new(0, 3, 1, 0, targetContractFirst,
                includeNativeCallableTarget ? 2 : 0, 1),
            new(0, 4, 2, 0, First,
                Count, 0),
        };
        if (distinctSecondAnchor)
        {
            methodRows.Add(new TrRmMethodDefRow(0, 5, 3, 0,
                secondAnchorContractFirst.First, secondAnchorContractFirst.Count, 0));
        }
        if (includePropertyFunctionPointerIdentity)
        {
            signatureRows.Add(new TrRmMethodSigRow(4, 0, 0, 0, 0, 0));
            methodRows.Add(new TrRmMethodDefRow(1, 3, signatureRows.Count,
                0, 0, 0, 0));
        }
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count, typeSpecs, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows, strings,
            modules, typeDefs, signatures, contractRows.Count, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes, methodRows.Count,
            strings, modules, typeDefs, signatures, contractRows.Count, 1);
        var contractBytes = contractRows.Count == 0 ? []
            : schema == TrRmArtifactSchema.ForeignInvocationDraft
                ? TrRmContractTableWriter.EncodeForeignInvocationDraft(
                    contractRows, typeDefs, methods, signatures, default,
                    typeSpecs, featureReader, modules, predicateGraphCount: 0)
                : TrRmContractTableWriter.Encode(contractRows, typeDefs, methods,
                    signatures, default, typeSpecs);
        var body = includeNativeAddress
            ? CreateNativeAddressBody(methodMapToken,
                nativeAddressPointerTypeToken ?? 0x1100_0003,
                nativeAddressResultKind,
                nativeAddressHomeTypeToken)
            : CreateMethodMapBody(methodMapToken, includeFunctionPointerTypeMap);
        var sections = new List<TrRmSection>
        {
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 5, stringsBytes),
            new TrRmSection(0x0004, 0, (uint)featureRows.Length, featuresBytes),
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Count, typeSpecBytes),
        };
        byte[] propertyBytes = [];
        if (typeDefCount != 0)
        {
            sections.Add(new TrRmSection(0x0013, 0,
                (uint)typeDefCount, typeDefBytes));
            sections.Add(new TrRmSection(0x0015, 0, 1, layoutBytes));
            propertyBytes = new byte[20];
            Write(propertyBytes, 0, 1);
            Write(propertyBytes, 4, 4);
            Write(propertyBytes, 8, 3);
            Write(propertyBytes, 12, methodRows.Count);
        }
        sections.Add(new TrRmSection(0x0016, 0,
            (uint)methodRows.Count, methodBytes));
        sections.Add(new TrRmSection(0x0017, 0,
            (uint)signatureRows.Count, signatureBytes));
        if (contractRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001B, 0,
                (uint)contractRows.Count, contractBytes));
        }
        if (typeDefCount != 0)
        {
            sections.Add(new TrRmSection(0x001E, 0, 1, propertyBytes));
        }
        sections.Add(new TrRmSection(0x0020, 0, 1,
            TrRmVariableTableWriter.EncodeBodies([body])));

        return TrRmArtifactWriter.Encode(sections, schema);
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
        bool includeRootlessFeature, bool useInBodyTypeMap = false,
        bool useInUnionCaseField = false, bool useInTextCapture = false,
        bool useInStaticField = false, bool useInStaticPublication = false,
        byte secondAnchorConvention = 1, bool secondAnchorHasUnitResult = false)
    {
        var useStaticField = useInStaticField || useInStaticPublication;
        var schema = useStaticField
            ? TrRmArtifactSchema.GeneralPredicateGraphsDraft
            : TrRmArtifactSchema.Standard;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("anchor", 0), ("Use", 0), ("Box", 0),
            ("T", 0), ("Generated", 0), ("StaticCallback", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 8);
        var typeDefCount = useInTextCapture || useStaticField ? 2 : 1;
        var methodCount = useInStaticPublication ? 3 : 2;
        TrRmFeatureRow[] featureRows = includeRootlessFeature
            ? [TrRmFeatureRow.NativeBoundaryContracts, TrRmFeatureRow.RootlessNativeValues]
            : [TrRmFeatureRow.NativeBoundaryContracts];
        if (useStaticField)
        {
            featureRows = [.. featureRows, TrRmFeatureRow.GeneralPredicateGraphsCandidate];
        }
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1, featureRows.Length, 1),
        ], strings, 0, typeDefCount, methodCount, 0, featureRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, typeDefCount, methodCount, 0, featureRows.Length);
        var typeSpecRows = new List<ReadOnlyMemory<byte>>
        {
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.NativeFunctionPointer(1, 1),
        };
        if (useInStaticPublication)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.NativeFunctionPointer(
                secondAnchorConvention, 3));
        }
        var genericTypeSpecRef = checked((uint)typeSpecRows.Count + 1);
        typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02, 1));
        var staticFieldTypeSpecRef = checked((uint)typeSpecRows.Count + 1);
        typeSpecRows.Add(TrRmTypeSpecTableWriter.Constructed(
            genericTypeSpecRef, [3]));
        var staticPublicationExtentTypeSpecRef = staticFieldTypeSpecRef;
        if (useInStaticPublication)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Constructed(
                genericTypeSpecRef, [4]));
            staticPublicationExtentTypeSpecRef
                = checked((uint)typeSpecRows.Count);
        }
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount, 0, 1, 0,
            schema, methodDefCount: methodCount);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count,
            typeDefCount, 0, 1, 0,
            schema, methodDefCount: methodCount);
        var typeDefRows = useInTextCapture || useStaticField
            ? new[] {
                new TrRmTypeDefRow(1, 5, 6, 0, 0, 1, 1, 1, 1, 0, 0),
                new TrRmTypeDefRow(1, 7, 4, 0, 0, 0, 0, 2, 1, 0, 0),
            }
            : [
                new TrRmTypeDefRow(1, 5, 6, 0, 0, 1, 1, 1, 1, 0, 0),
            ];
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, 0, 1, typeDefCount, 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules, 0, 1, typeDefCount, 0, schema);
        var parameterCount = useInBodyTypeMap || useInUnionCaseField
            || useInTextCapture || useStaticField ? 0 : 1;
        var signatureRows = new List<TrRmMethodSigRow>
        {
            new TrRmMethodSigRow(1, 0, 0, 0, 0, 0),
            new TrRmMethodSigRow(2, 0, parameterCount == 0 ? 0 : 1,
                parameterCount, 0, 0),
        };
        if (useInStaticPublication)
        {
            signatureRows.Add(new TrRmMethodSigRow(
                secondAnchorHasUnitResult ? 2u : 1u, 0, 0, 0, 0, 0));
        }
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, parameterCount, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count,
            typeSpecs, parameterCount, 0);
        var methodRows = new List<TrRmMethodDefRow>
        {
            new TrRmMethodDefRow(0, 3, 1, 0, 0, 0, 0),
            new TrRmMethodDefRow(0, 4, 2, 0, 0, 0,
                useInBodyTypeMap ? 1 : 0),
        };
        if (useInStaticPublication)
        {
            methodRows.Add(new TrRmMethodDefRow(0, 8, 3, 0, 0, 0, 0));
        }
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows, strings,
            modules, typeDefs, signatures, 0,
            useInBodyTypeMap ? 1 : 0);
        var methods = new TrRmMethodDefTableReader(methodBytes, methodCount,
            strings, modules, typeDefs, signatures, 0,
            useInBodyTypeMap ? 1 : 0);
        var parameterBytes = parameterCount == 0 ? [] : TrRmParamTableWriter.Encode([
            new TrRmParamRow(2, 0, 5, 0),
        ], signatures, typeSpecs);
        var genericParamBytes = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1300_0001, 0, 6, 0, 0, 1, 1, 1, 0, 0),
        ], strings, typeDefs, methods, signatures, typeSpecs, 0);
        byte[] unionCaseDefBytes = [];
        byte[] unionCaseFieldBytes = [];
        if (useInUnionCaseField)
        {
            unionCaseDefBytes = TrRmUnionCaseDefTableWriter.Encode([
                new TrRmUnionCaseDefRow(1, 0, 4, 1, 1),
            ], strings, typeDefs, payloadFieldCount: 1);
            var unionCaseDefs = new TrRmUnionCaseDefTableReader(
                unionCaseDefBytes, 1, strings, typeDefs, payloadFieldCount: 1);
            unionCaseFieldBytes = TrRmUnionCaseFieldTableWriter.Encode([
                new TrRmUnionCaseFieldRow(1, 0, 6, 3),
            ], strings, typeSpecs, unionCaseDefs);
        }
        byte[] textContentShapeBytes = [];
        byte[] textCaptureShapeBytes = [];
        if (useInTextCapture)
        {
            textContentShapeBytes = TrRmTextContentShapeTableWriter.Encode([
                new TrRmTextContentShapeRow(2, 1, 1, 0, 0),
            ], typeDefs, captureCount: 1, fragmentCount: 0);
            var textContentShapes = new TrRmTextContentShapeTableReader(
                textContentShapeBytes, 1, typeDefs, captureCount: 1,
                fragmentCount: 0);
            textCaptureShapeBytes = TrRmTextCaptureShapeTableWriter.Encode([
                new TrRmTextCaptureShapeRow(1, 0, 5, 1),
            ], typeSpecs, textContentShapes);
        }
        byte[] staticFieldBytes = [];
        byte[] staticFieldContractBytes = [];
        byte[] eagerGroupBytes = [];
        byte[] staticPublicationBytes = [];
        var staticPublicationCount = useInStaticPublication ? 1 : 0;
        if (useStaticField)
        {
            eagerGroupBytes = TrRmEagerGroupTableWriter.Encode([
                new TrRmEagerGroupRow(2, 1, 1, 0, 0, 0),
            ], typeDefCount, initializerCount: 0, methodDefCount: methodCount, schema);
            var eagerGroups = new TrRmEagerGroupTableReader(
                eagerGroupBytes, 1, typeDefCount, initializerCount: 0,
                methodDefCount: methodCount, schema);
            staticFieldBytes = TrRmStaticFieldTableWriter.Encode([
                new TrRmStaticFieldRow(2, 8, staticFieldTypeSpecRef, 0, 1),
            ], strings, typeDefs, typeSpecs, eagerGroups, schema);
            var staticFields = new TrRmStaticFieldTableReader(
                staticFieldBytes, 1, strings, typeDefs, typeSpecs,
                eagerGroups, schema);
            staticFieldContractBytes = TrRmStaticFieldContractTableWriter.Encode([
                new TrRmStaticFieldContractRow(1, 1, 0, 0, 0, 0,
                    useInStaticPublication ? 1 : 0),
            ], staticFieldCount: 1, lazyUnitCount: 0,
                publicationCount: staticPublicationCount,
                staticFields, schema);
            if (useInStaticPublication)
            {
                staticPublicationBytes = new byte[24];
                Write(staticPublicationBytes, 0, 1);
                staticPublicationBytes[4] = 1;
                staticPublicationBytes[5] = 1;
                staticPublicationBytes[6] = 1;
                staticPublicationBytes[7] = 1;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    staticPublicationBytes.AsSpan(8),
                    staticPublicationExtentTypeSpecRef);
                BinaryPrimitives.WriteUInt64LittleEndian(
                    staticPublicationBytes.AsSpan(12), 1);
            }
        }
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutRows = useInTextCapture || useStaticField
            ? new[] {
                new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
                new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
            }
            : [
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            ];
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows,
            typeDefs, targetProfiles, 0);
        var sections = new List<TrRmSection>
        {
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 8, stringsBytes),
            new TrRmSection(0x0004, 0, (uint)featureRows.Length, featureBytes),
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Count, typeSpecBytes),
            new TrRmSection(0x0013, 0, (uint)typeDefCount, typeDefBytes),
            new TrRmSection(0x0015, 0, (uint)typeDefCount, layoutBytes),
            new TrRmSection(0x0016, 0, (uint)methodRows.Count, methodBytes),
            new TrRmSection(0x0017, 0, (uint)signatureRows.Count, signatureBytes),
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
        if (useInUnionCaseField)
        {
            sections.Add(new TrRmSection(0x0034, 0, 1, unionCaseDefBytes));
            sections.Add(new TrRmSection(0x0035, 0, 1, unionCaseFieldBytes));
        }
        if (useInTextCapture)
        {
            sections.Add(new TrRmSection(0x002D, 0, 1, textContentShapeBytes));
            sections.Add(new TrRmSection(0x002E, 0, 1, textCaptureShapeBytes));
            sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        }
        if (useStaticField)
        {
            sections.Add(new TrRmSection(0x0055, 0, 1, staticFieldBytes));
            sections.Add(new TrRmSection(0x0056, 0, 1, staticFieldContractBytes));
            sections.Add(new TrRmSection(0x0057, 0, 1, eagerGroupBytes));
            if (useInStaticPublication)
            {
                sections.Add(new TrRmSection(0x005C, 0,
                    (uint)staticPublicationCount, staticPublicationBytes));
            }
            sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
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

    private static byte[] CreateNativeAddressBody(uint methodMapToken,
        uint pointerTypeToken, byte resultKind, uint? homeTypeToken)
    {
        var hasHome = resultKind == 2;
        var hasDistinctHomeType = hasHome && homeTypeToken is uint selectedHomeType
            && selectedHomeType != pointerTypeToken;
        var typeTokens = hasDistinctHomeType
            ? new[] { pointerTypeToken, homeTypeToken.GetValueOrDefault() }
            : [pointerTypeToken];
        var mapMask = (byte)(0x06 | (hasHome ? 0x01 : 0));
        var body = new List<byte> { 1, 1, mapMask, 0, 0 };
        if (hasHome)
        {
            WriteUInt32(body, 1);
        }
        WriteUInt32(body, (uint)typeTokens.Length);
        WriteUInt32(body, 1);
        WriteUInt32(body, 1);
        WriteUInt32(body, 2);
        WriteUInt32(body, 6);
        if (hasHome)
        {
            body.AddRange([3, 0, (byte)(hasDistinctHomeType ? 1 : 0),
                0, 0, 0, 0, 0]);
        }
        foreach (var typeToken in typeTokens)
        {
            WriteUInt32(body, typeToken);
        }
        WriteUInt32(body, methodMapToken);
        WriteUInt32(body, 0);
        WriteUInt32(body, 6);
        WriteUInt16(body, 0);
        WriteUInt16(body, 0);
        WriteUInt32(body, 0);
        WriteUInt32(body, 8);
        WriteUInt32(body, 20);
        body.AddRange([1, 0, 0, 0, 0, 0, 0, 0]);
        body.AddRange([
            0x0D, 0, 0, 0, resultKind, 0, 0, 0,
            0, 0, 0, 0,
        ]);
        body.AddRange([0xE9, 0, 1, 0, 0x18, 0]);
        return [.. body];
    }

    private static byte[] CreateNativeImportAddressArtifact(byte callConv)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("target", 0), ("anchor", 0), ("caller", 0),
            ("kernel32", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 6);
        TrRmFeatureRow[] featureRows = [
            TrRmFeatureRow.NativeBoundaryContracts,
            TrRmFeatureRow.RootlessNativeValues,
            TrRmFeatureRow.DirectNativeCalls,
            TrRmFeatureRow.NativeExportEntries,
            TrRmFeatureRow.ForeignInvocationContractsCandidate,
        ];
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows,
            TrRmArtifactSchema.ForeignInvocationDraft);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1,
                featureRows.Length, 1),
        ], strings, 0, 0, 3, 0, featureRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 3, 0, featureRows.Length);
        var typeSpecRows = new ReadOnlyMemory<byte>[] {
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.NativeFunctionPointer(1, 2),
        };
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            0, 0, 0, 0, schema: TrRmArtifactSchema.ForeignInvocationDraft,
            methodDefCount: 3);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 2,
            0, 0, 0, 0, schema: TrRmArtifactSchema.ForeignInvocationDraft,
            methodDefCount: 3);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 1);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layouts = new TrRmLayoutTableReader([], 0, typeDefs, targetProfiles, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0000u, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 0, 0);
        var contractRows = new List<TrRmContractRow> {
            new(0x1600_0001, 1, new byte[] { 4, 0, 0, 0 }),
        };
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 0, 1, 1, 0),
            new TrRmMethodDefRow(0, 4, 1, 0, 0, 0, 0),
            new TrRmMethodDefRow(0, 5, 1, 0, 0, 0, 1),
        ], strings, modules, typeDefs, signatures, 1, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes, 3, strings,
            modules, typeDefs, signatures, 1, 1);
        var featureReader = new TrRmFeatureTableReader(featureBytes,
            featureRows.Length, TrRmArtifactSchema.ForeignInvocationDraft);
        var contractBytes = TrRmContractTableWriter.EncodeForeignInvocationDraft(
            contractRows, typeDefs, methods, signatures, default, typeSpecs,
            featureReader, modules, predicateGraphCount: 0);
        var contracts = new TrRmContractTableReader(contractBytes, 1,
            typeDefs, methods, signatures, default, typeSpecs, 0,
            featureReader, modules, TrRmArtifactSchema.ForeignInvocationDraft,
            predicateGraphCount: 0);
        var fields = default(TrRmFieldDefTableReader);
        var nativeImportBytes = TrRmNativeImportTableWriter.Encode([
            new TrRmNativeImportRow(1, 1, 0, callConv, 1, 6, 0),
        ], strings, methods, fields, typeDefs, default, modules, signatures,
            default, typeSpecs, contracts, layouts);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 6, stringBytes),
            new TrRmSection(0x0004, 0, (uint)featureRows.Length, featureBytes),
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Length, typeSpecBytes),
            new TrRmSection(0x0016, 0, 3, methodBytes),
            new TrRmSection(0x0017, 0, 1, signatureBytes),
            new TrRmSection(0x001B, 0, 1, contractBytes),
            new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([
                    CreateNativeAddressBody(0x1600_0001, 0x1100_0002, 1, null),
                ])),
            new TrRmSection(0x0021, 0, 1, nativeImportBytes),
        ], TrRmArtifactSchema.ForeignInvocationDraft);
    }

    private static byte[] CreateForeignInvocationNativeAddressArtifact(
        uint? targetExtent, uint? anchorExtent)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("target", 0), ("anchor", 0),
            ("caller", 0), ("test.dll", 0), ("Read", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 7);
        TrRmFeatureRow[] featureRows = [
            TrRmFeatureRow.NativeBoundaryContracts,
            TrRmFeatureRow.RootlessNativeValues,
            TrRmFeatureRow.DirectNativeCalls,
            TrRmFeatureRow.NativeExportEntries,
            TrRmFeatureRow.ForeignInvocationContractsCandidate,
        ];
        var featureBytes = TrRmFeatureTableWriter.Encode(
            featureRows, TrRmArtifactSchema.ForeignInvocationDraft);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1,
                featureRows.Length, 1),
        ], strings, 0, 0, 3, 0, featureRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 3, 0, featureRows.Length);
        var typeSpecRows = new ReadOnlyMemory<byte>[] {
            TrRmTypeSpecTableWriter.Builtin(0x0D),
            TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0x0D),
            TrRmTypeSpecTableWriter.NativeFunctionPointer(1, 2),
            TrRmTypeSpecTableWriter.Builtin(0),
        };
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            0, 0, 0, 0, TrRmArtifactSchema.ForeignInvocationDraft,
            methodDefCount: 3);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecRows.Length,
            0, 0, 0, 0, TrRmArtifactSchema.ForeignInvocationDraft,
            methodDefCount: 3);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 1, 1, 0, 0),
            new TrRmMethodSigRow(4, 0, 0, 0, 0, 0),
        ], typeSpecs, 1, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 2,
            typeSpecs, 1, 0);
        var parameters = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, 2, 0),
        ], signatures, typeSpecs, TrRmArtifactSchema.ForeignInvocationDraft);
        var parameterReader = new TrRmParamTableReader(parameters, 1,
            signatures, typeSpecs, TrRmArtifactSchema.ForeignInvocationDraft);
        var contractRows = new List<TrRmContractRow>();
        AddTargetContract(contractRows, 1, targetExtent);
        var targetContractCount = contractRows.Count;
        var anchorContractFirst = contractRows.Count + 1;
        AddTargetContract(contractRows, 2, anchorExtent);
        var anchorContractCount = contractRows.Count - anchorContractFirst + 1;
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, contractRows.Count);
        var methodRows = new List<TrRmMethodDefRow>
        {
            new(0, 3, 1, 0, 1, targetContractCount, 0),
            new(0, 4, 1, 0, anchorContractCount == 0 ? 0 : anchorContractFirst,
                anchorContractCount, 0),
            new(0, 5, 2, 0, 0, 0, 1),
        };
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows, strings,
            modules, typeDefs, signatures, contractRows.Count, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes, methodRows.Count,
            strings, modules, typeDefs, signatures, contractRows.Count, 1);
        var contractBytes = TrRmContractTableWriter.EncodeForeignInvocationDraft(
            contractRows, typeDefs, methods, signatures, parameterReader,
            typeSpecs, new TrRmFeatureTableReader(featureBytes, featureRows.Length,
                TrRmArtifactSchema.ForeignInvocationDraft),
            modules, predicateGraphCount: 0);
        var contracts = new TrRmContractTableReader(contractBytes, contractRows.Count,
            typeDefs, methods, signatures, parameterReader, typeSpecs, 0,
            new TrRmFeatureTableReader(featureBytes, featureRows.Length,
                TrRmArtifactSchema.ForeignInvocationDraft),
            modules, TrRmArtifactSchema.ForeignInvocationDraft);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layouts = new TrRmLayoutTableReader([], 0, typeDefs,
            targetProfiles, 0);
        var nativeImports = TrRmNativeImportTableWriter.Encode([
            new TrRmNativeImportRow(1, 1, 0, 1, 1, 6, 7),
        ], strings, methods, default, typeDefs, default, modules,
            signatures, parameterReader, typeSpecs, contracts, layouts);
        var body = CreateNativeAddressBody(0x1600_0001, 0x1100_0003, 1, null);
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 7, stringBytes),
            new(0x0004, 0, (uint)featureRows.Length, featureBytes),
            new(0x0011, 0, (uint)typeSpecRows.Length, typeSpecBytes),
            new(0x0016, 0, (uint)methodRows.Count, methodBytes),
            new(0x0017, 0, 2, signatureBytes),
            new(0x0018, 0, 1, parameters),
            new(0x001B, 0, (uint)contractRows.Count, contractBytes),
            new(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([body])),
            new(0x0021, 0, 1, nativeImports),
        };
        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);

        static void AddTargetContract(List<TrRmContractRow> rows, int methodRow,
            uint? extent)
        {
            var effectPayload = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(effectPayload, 4);
            rows.Add(new TrRmContractRow(0x1600_0000u | (uint)methodRow, 1,
                effectPayload));
            if (extent is uint nativeExtent)
            {
                rows.Add(TrRmNativeImportTableTests.StandardNativeParameterContract(
                    0, 1, nativeExtent == 0 ? (byte)0 : (byte)1, nativeExtent) with
                {
                    OwnerDefId = 0x1600_0000u | (uint)methodRow,
                });
            }
        }
    }

    private static void ValidateForeignInvocationNativeAddressArtifact(byte[] artifact)
    {
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        tables.ValidateRegisterBodyMapsForMethod(3);
    }

    private static void WriteUInt16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
    }

    private static void WriteUInt32(List<byte> bytes, uint value)
    {
        bytes.Add(unchecked((byte)value));
        bytes.Add(unchecked((byte)(value >> 8)));
        bytes.Add(unchecked((byte)(value >> 16)));
        bytes.Add(unchecked((byte)(value >> 24)));
    }

    private static byte[] EncodeRawLayoutRows(TrRmLayoutRow[] rows)
    {
        var bytes = new byte[rows.Length * 28];
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var offset = index * 28;
            Write(bytes, offset, row.OwnerTypeDefRow);
            Write(bytes, offset + 4, checked((int)row.TargetProfileRef));
            bytes[offset + 8] = row.LayoutKind;
            bytes[offset + 9] = row.Flags;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 12),
                row.Alignment);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 16),
                row.Pack);
            Write(bytes, offset + 20, row.LayoutFieldFirst);
            Write(bytes, offset + 24, row.LayoutFieldCount);
        }

        return bytes;
    }

    private static void ValidateAnchorMethodMapArtifact(byte[] artifact,
        TrRmArtifactSchema schema = TrRmArtifactSchema.Standard)
    {
        var index = schema == TrRmArtifactSchema.ForeignInvocationDraft
            ? TrRmArtifactIndex.ForForeignInvocationDraft(artifact)
            : new TrRmArtifactIndex(artifact);
        var tables = schema == TrRmArtifactSchema.ForeignInvocationDraft
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(index)
            : TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(index);
        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    private static void ValidateNativeAddressArtifact(byte[] artifact,
        TrRmArtifactSchema schema)
    {
        var index = schema == TrRmArtifactSchema.ForeignInvocationDraft
            ? TrRmArtifactIndex.ForForeignInvocationDraft(artifact)
            : new TrRmArtifactIndex(artifact);
        var tables = schema == TrRmArtifactSchema.ForeignInvocationDraft
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(index)
            : TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(index);
        tables.ValidateRegisterBodyMapsForMethod(3);
    }

    private static uint Read(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), unchecked((uint)value));

    private sealed record Fixture(byte[] Artifact, byte[] NativeImportBytes,
        string[] EntryNames, byte[] NativeExportBytes, string[] NativeExportNames);
}
