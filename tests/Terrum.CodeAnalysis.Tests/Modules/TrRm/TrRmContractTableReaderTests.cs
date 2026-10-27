// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmContractTableReaderTests
{
    [Test]
    public void DistinctTypeAndMethodRangesOwnAssignedClauses()
    {
        Create(Section(Cleanup(), Effects()));
        Create([], recordCount: 0, declaredCount: 0,
            typeFirst: 0, typeCount: 0, methodFirst: 0, methodCount: 0);
    }
    [TestCase(0,(byte)(0))]
    [TestCase(3,(byte)(0))]
    [TestCase(4,(byte)(0))]
    [TestCase(4,(byte)(6))]
    [TestCase(6,(byte)(1))]
    [TestCase(8,(byte)(0))]
    [TestCase(8,(byte)(2))]
    public void RejectsMalformedCleanupReferenceKindAndFlags(int offset, byte replacement)
    {
        var row = Cleanup();
        row[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => Create(Section(row, Effects())));
    }

    [Test]
    public void EffectsMaskRejectsUnassignedBitsAndDuplicateSingletons()
    {
        var effects = Effects();
        effects[8] = 8;
        Assert.Throws<InvalidDataException>(() => Create(Section(Cleanup(), effects)));

        Assert.Throws<InvalidDataException>(() => Create(Section(Effects(), Effects()),
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 2));
    }

    [Test]
    public void DevelopmentProfileRetiresNoThrowAndAllowsCleanupWithoutIt()
    {
        Create(Section(Cleanup(), Effects(2)));
        Create(Section(Cleanup(), Effects(4)));
        Create(Section(Cleanup(), Effects(6)));
        Create(Section(Cleanup(), Effects(0)));
        Create(Section(Effects(), Cleanup()), typeFirst: 2, methodFirst: 1);

        foreach (var mask in new uint[] { 1, 3, 5, 7 })
        {
            Assert.Throws<InvalidDataException>(() => Create(Section(Effects(mask)),
                recordCount: 1, declaredCount: 1,
                typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1));
        }
    }

    [Test]
    public void StandardEffectsRetireNoThrowAndAllowExplicitZero()
    {
        foreach (var mask in new uint[] { 2, 4, 6 })
        {
            Create(Section(Effects(mask)), recordCount: 1, declaredCount: 1,
                typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
                writeRows: true, developmentProfile: false);
        }

        Create(Section(Effects(0)), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            developmentProfile: false);
        Assert.Throws<InvalidDataException>(() =>
            Create(Section(Effects(1)), recordCount: 1, declaredCount: 1,
                typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
                writeRows: true, developmentProfile: false));
        Assert.Throws<InvalidDataException>(() =>
            Create(Section(Effects(8)), recordCount: 1, declaredCount: 1,
                typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
                writeRows: true, developmentProfile: false));
    }

    [Test]
    public void StandardCleanupClauseDoesNotRequireRetiredNoThrowEffectBit()
    {
        Create(Section(Cleanup()), recordCount: 1, declaredCount: 1,
            typeFirst: 1, typeCount: 1, methodFirst: 0, methodCount: 0,
            writeRows: true, frameArtifact: true, developmentProfile: false);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void StandardNativeCallableClausePreservesItsExactConvention(byte convention)
    {
        Create(Section(NativeCallable(convention)), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            writeRows: true, developmentProfile: false,
            expectedNativeCallableConvention: convention);
    }

    [Test]
    public void StandardNativeCallableClauseRequiresNativeBoundaryFeature()
    {
        Assert.Throws<InvalidDataException>(() => Create(
            Section(NativeCallable()), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            frameArtifact: true, developmentProfile: false));

        Create(Section(NativeCallable()), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            frameArtifact: true, developmentProfile: false,
            includeNativeBoundaryFeature: true);
    }

    [Test]
    public void StandardPredicateGraphContractUsesTypedClauseAndFeatureClosure()
    {
        var predicate = PredicateGraphContract(phase: 3, ordinal: 0, graphRow: 1);
        Create(Section(predicate), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            developmentProfile: false, predicateGraphCount: 1,
            includePublishedPredicatesFeature: true, writeRows: true,
            expectedPredicateGraphContractCount: 1);

        Assert.Throws<InvalidDataException>(() => Create(Section(predicate),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            developmentProfile: false, predicateGraphCount: 1));
        Assert.Throws<InvalidDataException>(() => Create(Section(predicate),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            developmentProfile: false));

        var selector = PredicateGraphContract(phase: 3, ordinal: 0, graphRow: 1);
        Create(Section(selector),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            developmentProfile: false, predicateGraphCount: 1,
            includePublishedPredicatesFeature: true,
            expectedPredicateGraphContractCount: 1);

        foreach (var phase in new byte[] { 0, 4 })
        {
            var invalidPhase = PredicateGraphContract(phase, 0, 1);
            Assert.Throws<InvalidDataException>(() => Create(Section(invalidPhase),
                recordCount: 1, declaredCount: 1,
                typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
                developmentProfile: false, predicateGraphCount: 1,
                includePublishedPredicatesFeature: true));
        }

        var phaseGap = Section(
            PredicateGraphContract(phase: 1, ordinal: 0, graphRow: 1),
            PredicateGraphContract(phase: 1, ordinal: 2, graphRow: 2));
        Assert.Throws<InvalidDataException>(() => Create(phaseGap,
            recordCount: 2, declaredCount: 2,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 2,
            developmentProfile: false, predicateGraphCount: 2,
            includePublishedPredicatesFeature: true));

        Assert.Throws<InvalidDataException>(() => Create(
            Section(PredicateGraphContract(phase: 1, ordinal: 0, graphRow: 2)),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            developmentProfile: false, predicateGraphCount: 1,
            includePublishedPredicatesFeature: true));

        Assert.Throws<InvalidDataException>(() => Create(Section(predicate),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            developmentProfile: true, predicateGraphCount: 1,
            includePublishedPredicatesFeature: true));
    }

    [Test]
    public void BoundedPublishedPredicateFeatureDoesNotRequireFrozenValues()
    {
        Create(Section(Effects()), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            developmentProfile: false, includePublishedPredicatesFeature: true,
            verifyPublishedPredicatePrerequisites: true);
    }

    [Test]
    public void NativeCallableClauseRejectsMalformedShapesAndDevelopmentAlias()
    {
        var convention = NativeCallable(1);
        convention[8] = 0;
        Assert.Throws<InvalidDataException>(() => Create(Section(convention),
            recordCount: 1, declaredCount: 1, typeFirst: 0, typeCount: 0,
            methodFirst: 1, methodCount: 1, developmentProfile: false));

        convention = NativeCallable(1);
        convention[8] = 5;
        Assert.Throws<InvalidDataException>(() => Create(Section(convention),
            recordCount: 1, declaredCount: 1, typeFirst: 0, typeCount: 0,
            methodFirst: 1, methodCount: 1, developmentProfile: false));

        convention = NativeCallable(1);
        convention[9] = 1;
        Assert.Throws<InvalidDataException>(() => Create(Section(convention),
            recordCount: 1, declaredCount: 1, typeFirst: 0, typeCount: 0,
            methodFirst: 1, methodCount: 1, developmentProfile: false));

        convention = NativeCallable(1);
        convention[10] = 1;
        Assert.Throws<InvalidDataException>(() => Create(Section(convention),
            recordCount: 1, declaredCount: 1, typeFirst: 0, typeCount: 0,
            methodFirst: 1, methodCount: 1, developmentProfile: false));

        Assert.Throws<InvalidDataException>(() => Create(
            Section(NativeCallable(owner: 0x1300_0001)), recordCount: 1,
            declaredCount: 1, typeFirst: 0, typeCount: 0,
            methodFirst: 1, methodCount: 1, developmentProfile: false));

        Assert.Throws<InvalidDataException>(() => Create(
            Section(NativeCallable()), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            developmentProfile: true));

        Assert.Throws<InvalidDataException>(() => Create(
            Section(NativeCallable(1), NativeCallable(2)), recordCount: 2,
            declaredCount: 2, typeFirst: 0, typeCount: 0,
            methodFirst: 1, methodCount: 2, developmentProfile: false));
    }

    [Test]
    public void RejectsOverlapsOrphansAndMismatchedDeclaredCounts()
    {
        var rows = Section(Cleanup(), Effects());
        Assert.Throws<InvalidDataException>(() => Create(rows,
            typeFirst: 1, typeCount: 2, methodFirst: 2, methodCount: 1));
        Assert.Throws<InvalidDataException>(() => Create(Section(Cleanup(), Effects(), Effects()),
            recordCount: 3, declaredCount: 3));
        Assert.Throws<InvalidDataException>(() => Create(rows, declaredCount: 3));
        Assert.Throws<InvalidDataException>(() => Create(rows[..^1]));
    }

    [Test]
    public void AssignedButNonexecutableClauseShapesAreBounded()
    {
        var output = Contract(0x1600_0001, 2, [0, 0, 0, 0, 1, 0, 0, 0]);
        var transfer = Contract(0x1600_0001, 3, [0, 0, 0, 0, 0, 0, 0, 0]);
        var dependency = Contract(0x1600_0001, 4,
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Create(Section(output), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true);
        Create(Section(transfer), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1);
        Create(Section(dependency), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1);

        output[12] = 2;
        Assert.Throws<InvalidDataException>(() => Create(Section(output),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true));
        transfer[15] = 1;
        Assert.Throws<InvalidDataException>(() => Create(Section(transfer),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1));
        Assert.Throws<InvalidDataException>(() => Create(Section(Contract(0x1600_0001, 0x12, [])),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1));
    }

    [Test]
    public void StandardOutputStateRequiresAnAttachedPredicateAndInitializationOutput()
    {
        var predicate = PredicateGraphContract(phase: 3, ordinal: 0, graphRow: 1);
        var output = OutputStateContract(ordinal: 0, graphRow: 1, state: 1);
        var section = Section(predicate, output);
        Create(section, recordCount: 2, declaredCount: 2,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 2,
            hasOutputParam: true, paramMode: 0, initializationParameter: true,
            predicateGraphCount: 1, includePublishedPredicatesFeature: true,
            writeRows: true, developmentProfile: false);

        Assert.Throws<InvalidDataException>(() => Create(Section(output),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, paramMode: 0, initializationParameter: true,
            predicateGraphCount: 1, includePublishedPredicatesFeature: true,
            developmentProfile: false));
        Assert.Throws<InvalidDataException>(() => Create(section,
            recordCount: 2, declaredCount: 2,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 2,
            predicateGraphCount: 1, includePublishedPredicatesFeature: true,
            developmentProfile: false));
        Assert.Throws<InvalidDataException>(() => Create(section,
            recordCount: 2, declaredCount: 2,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 2,
            hasOutputParam: true, paramMode: 1, initializationParameter: true,
            predicateGraphCount: 1, includePublishedPredicatesFeature: true,
            developmentProfile: false));
        Assert.Throws<InvalidDataException>(() => Create(section,
            recordCount: 2, declaredCount: 2,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 2,
            hasOutputParam: true, paramMode: 0, predicateGraphCount: 1,
            includePublishedPredicatesFeature: true, developmentProfile: false));

        output = OutputStateContract(ordinal: 0, graphRow: 1, state: 2);
        Assert.Throws<InvalidDataException>(() => Create(Section(predicate, output),
            recordCount: 2, declaredCount: 2,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 2,
            hasOutputParam: true, paramMode: 0, initializationParameter: true,
            predicateGraphCount: 1, includePublishedPredicatesFeature: true,
            developmentProfile: false));

        output = OutputStateContract(ordinal: 0, graphRow: 2, state: 1);
        Assert.Throws<InvalidDataException>(() => Create(Section(predicate, output),
            recordCount: 2, declaredCount: 2,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 2,
            hasOutputParam: true, paramMode: 0, initializationParameter: true,
            predicateGraphCount: 2, includePublishedPredicatesFeature: true,
            developmentProfile: false));
    }

    [Test]
    public void RetiredNativeVTableContractKindsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Create(
            Section(Contract(0x1300_0001, 7, new byte[20])), recordCount: 1, declaredCount: 1,
            typeFirst: 1, typeCount: 1, methodFirst: 0, methodCount: 0,
            typeFlags: 12));
        Assert.Throws<InvalidDataException>(() => Create(
            Section(Contract(0x1600_0001, 8, new byte[8])), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            methodOwnerType: 1));
    }

    [Test]
    public void RemovedNativeComOwnerAndAcquisitionClausesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Create(Section(
                Contract(0x1300_0001, 9, new byte[4])),
            recordCount: 1, declaredCount: 1,
            typeFirst: 1, typeCount: 1, methodFirst: 0, methodCount: 0,
            typeFlags: 12));

        var acquisition = Contract(0x1600_0001, 11, new byte[20]);
        Assert.Throws<InvalidDataException>(() => Create(Section(acquisition),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1));
    }

    [Test]
    public void NativeParameterRequiresAnAddressParameterAndAssignedAccess()
    {
        var contract = NativeParameter(ordinal: 0, access: 3, retention: 1, extent: 0);
        Create(Section(contract), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, pointerParameter: true);
        Assert.Throws<InvalidDataException>(() => Create(Section(contract),
            recordCount: 1, declaredCount: 1, typeFirst: 0, typeCount: 0,
            methodFirst: 1, methodCount: 1, hasOutputParam: true));
        Assert.Throws<InvalidDataException>(() => Create(
            Section(NativeParameter(ordinal: 0, access: 0, retention: 1, extent: 0)),
            recordCount: 1, declaredCount: 1, typeFirst: 0, typeCount: 0,
            methodFirst: 1, methodCount: 1, hasOutputParam: true, pointerParameter: true));
    }

    [Test]
    public void CopyOriginDraftContractUsesTypedWholeValueReferences()
    {
        Create(Section(CopyOriginContract()), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, paramMode: 0, pointerParameter: true,
            writeRows: true, frameArtifact: true,
            developmentProfile: false, copyOriginDraft: true);

        var standardRow = Section(CopyOriginContract());
        Assert.Throws<InvalidDataException>(() => Create(standardRow,
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, paramMode: 0, pointerParameter: true,
            developmentProfile: false));
        Assert.Throws<InvalidDataException>(() => Create(standardRow,
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, paramMode: 0, pointerParameter: true,
            writeRows: true, developmentProfile: false, copyOriginDraft: true,
            includeCopyOriginFeature: false));
    }

    [Test]
    public void CopyOriginDraftReferentSourcesRequireCheckedInputReferences()
    {
        var referent = CopyOriginContract();
        referent[29] = 2;
        Create(Section(referent), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, paramMode: 0, referentParameter: true,
            writeRows: true, frameArtifact: true,
            developmentProfile: false, copyOriginDraft: true);

        var pointer = CopyOriginContract();
        pointer[29] = 2;
        Assert.Throws<InvalidDataException>(() => Create(Section(pointer),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, paramMode: 0, pointerParameter: true,
            developmentProfile: false, copyOriginDraft: true));

        Assert.Throws<InvalidDataException>(() =>
            Create(Section(CopyOriginContract()), recordCount: 1,
               declaredCount: 1, typeFirst: 0, typeCount: 0,
               methodFirst: 1, methodCount: 1, hasOutputParam: true,
               paramMode: 0,
               initializationParameter: true, developmentProfile: false,
               copyOriginDraft: true));
    }

    [Test]
    public void CopyOriginDraftBindsFieldPathsAndFreshConstructionRoots()
    {
        ValidateCopyOriginFieldArtifact(
            [new CopyOriginTestFact(1, [new CopyOriginTestOrigin(1, 1, 0, 1)])]);
        ValidateCopyOriginFieldArtifact(
            [new CopyOriginTestFact(1, [new CopyOriginTestOrigin(1, 1, 0, 1)])],
            summaryKind: 2, receiver: true);

        Assert.Throws<InvalidDataException>(() =>
            ValidateCopyOriginFieldArtifact(
               [new CopyOriginTestFact(1,
                   [new CopyOriginTestOrigin(1, 1, 0, 0)])]));
        Assert.Throws<InvalidDataException>(() =>
            ValidateCopyOriginFieldArtifact(
               [new CopyOriginTestFact(1,
                   [new CopyOriginTestOrigin(1, 1, 0, 1)])],
               fieldPathRootTypeSpec: 1));
        Assert.Throws<InvalidDataException>(() =>
            ValidateCopyOriginFieldArtifact(
               [new CopyOriginTestFact(1,
                   [new CopyOriginTestOrigin(1, 1, 0, 1)])],
               summaryKind: 2));
    }

    [Test]
    public void CopyOriginDraftRejectsDuplicateAndOverlappingFacts()
    {
        Assert.Throws<InvalidDataException>(() =>
            ValidateCopyOriginFieldArtifact([
               new CopyOriginTestFact(1,
                   [new CopyOriginTestOrigin(1, 1, 0, 1)]),
               new CopyOriginTestFact(1,
                   [new CopyOriginTestOrigin(1, 1, 0, 1)]),
            ]));

        Assert.Throws<InvalidDataException>(() =>
            ValidateCopyOriginFieldArtifact([
               new CopyOriginTestFact(1, [
                   new CopyOriginTestOrigin(1, 1, 0, 1),
                   new CopyOriginTestOrigin(1, 1, 0, 1),
               ]),
            ]));

        Assert.Throws<InvalidDataException>(() =>
            ValidateCopyOriginFieldArtifact([
               new CopyOriginTestFact(0,
                   [new CopyOriginTestOrigin(1, 1, 0, 0)]),
               new CopyOriginTestFact(1,
                   [new CopyOriginTestOrigin(1, 1, 0, 1)]),
            ]));
    }

    [Test]
    public void CopyOriginDraftRejectsForeignCallableRoles()
    {
        var facts = new[]
        {
            new CopyOriginTestFact(1,
                [new CopyOriginTestOrigin(1, 1, 0, 1)]),
        };
        Assert.Throws<InvalidDataException>(() =>
            ValidateCopyOriginFieldArtifact(facts,
                nativeImportMethod: true));
        Assert.Throws<InvalidDataException>(() =>
            ValidateCopyOriginFieldArtifact(facts,
                functionPointerAnchor: true));
    }

    [Test]
    public void CopyOriginDraftWriterSortsOriginsByLogicalSourceIdentity()
    {
        var written = Create(Section(CopyOriginContractWithOrigins(1, 0)),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, paramMode: 0, writeRows: true,
            developmentProfile: false, copyOriginDraft: true,
            twoParameters: true, canonicalizeCopyOrigin: true);
        var contractRows = new TrRmVariableTableIndex(written, 1, 8, 1);
        var clause = contractRows.GetRow(1);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(clause[32..]), Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(clause[44..]), Is.EqualTo(1));
    }

    [Test]
    public void CopyOriginDraftWriterSortsFactsByDecodedFieldReferences()
    {
        ValidateCopyOriginFieldArtifact([
            new CopyOriginTestFact(1,
                [new CopyOriginTestOrigin(1, 1, 0, 1)]),
            new CopyOriginTestFact(2,
                [new CopyOriginTestOrigin(1, 1, 0, 2)]),
        ], twoFields: true, reverseFieldPathRows: true,
            writerCanonicalizesFacts: true);
    }

    [Test]
    public void CopyOriginDraftWriterSortsOriginsByDecodedFieldReferences()
    {
        ValidateCopyOriginFieldArtifact([
            new CopyOriginTestFact(1, [
                new CopyOriginTestOrigin(1, 1, 0, 1),
                new CopyOriginTestOrigin(1, 1, 0, 2),
            ]),
        ], twoFields: true, reverseFieldPathRows: true,
            writerCanonicalizesOrigins: true);
    }

    [TestCase(8, 0)]
    [TestCase(9, 1)]
    [TestCase(10, 1)]
    [TestCase(16, 0)]
    [TestCase(24, 0)]
    [TestCase(28, 3)]
    [TestCase(29, 0)]
    [TestCase(30, 1)]
    [TestCase(32, 1)]
    [TestCase(36, 1)]
    public void CopyOriginDraftRejectsMalformedTagsCountsAndReferences(
        int offset, byte replacement)
    {
        var row = CopyOriginContract();
        row[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => Create(Section(row),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, paramMode: 0, pointerParameter: true,
            developmentProfile: false, copyOriginDraft: true));
    }

    [Test]
    public void WrittenClausesComposeAcrossTypeAndMethodInFramedArtifact()
    {
        Create(Section(Cleanup(), Effects(0)), writeRows: true,
            frameArtifact: true, developmentProfile: false);
        var invalidKind = Effects(2);
        invalidKind[4] = 6;
        Assert.Throws<InvalidDataException>(() =>
            Create(Section(Cleanup(), invalidKind), writeRows: true,
                developmentProfile: false));
        Assert.Throws<InvalidDataException>(() =>
            Create(Section(Effects(2), Effects(2)),
                typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 2,
                writeRows: true, developmentProfile: false));
    }

    private static byte[] Create(byte[] section, int recordCount = 2, int declaredCount = 2,
        uint typeFirst = 1, uint typeCount = 1, uint methodFirst = 2, uint methodCount = 1,
        bool hasOutputParam = false, byte paramMode = 1, bool nonBooleanResult = false,
        bool writeRows = false, bool frameArtifact = false,
        bool pointerParameter = false, int methodOwnerType = 1, uint typeFlags = 4,
        int typeRows = 1, uint secondTypeFirst = 0, uint secondTypeCount = 0,
        bool developmentProfile = true, bool copyOriginDraft = false,
        int fieldPathCount = 0, bool includeCopyOriginFeature = true,
        bool referentParameter = false, bool initializationParameter = false,
        bool twoParameters = false, bool canonicalizeCopyOrigin = false,
        byte? expectedNativeCallableConvention = null,
        bool includeNativeBoundaryFeature = false,
        int predicateGraphCount = 0,
        bool includePublishedPredicatesFeature = false,
        int? expectedPredicateGraphContractCount = null,
        bool verifyPublishedPredicatePrerequisites = false)
    {
        var schema = copyOriginDraft ? TrRmArtifactSchema.CopyOriginDraft
            : developmentProfile ? TrRmArtifactSchema.Development
            : TrRmArtifactSchema.Standard;
        var nameBytes = TrRmStringTableWriter.EncodeTextRows(
            [("", 0), ("Module", 0), ("Widget", 0), ("Run", 0), ("Owner", 0)]);
        var names = new TrRmStringTableReader(nameBytes, 5);
        var module = new byte[48];
        Write(module, 0, 2);
        Write(module, 4, 1);
        TrRmFeatureRow[] featureRows = copyOriginDraft
            ? pointerParameter
                ? includeCopyOriginFeature
                    ? [new TrRmFeatureRow(1, 1), new TrRmFeatureRow(2, 1),
                        new TrRmFeatureRow(15, 1)]
                    : [new TrRmFeatureRow(1, 1), new TrRmFeatureRow(2, 1)]
                : includeCopyOriginFeature
                    ? [new TrRmFeatureRow(15, 1)]
                    : Array.Empty<TrRmFeatureRow>()
                : (includeNativeBoundaryFeature, includePublishedPredicatesFeature) switch
                {
                    (true, true) =>
                        [new TrRmFeatureRow(1, 1), TrRmFeatureRow.PublishedPredicates],
                    (true, false) => [new TrRmFeatureRow(1, 1)],
                    (false, true) => [TrRmFeatureRow.PublishedPredicates],
                    _ => Array.Empty<TrRmFeatureRow>(),
                };
        var featureBytes = featureRows.Length == 0 ? Array.Empty<byte>()
            : TrRmFeatureTableWriter.Encode(featureRows, schema);
        if (featureRows.Length != 0)
        {
            Write(module, 24, 1);
            Write(module, 28, (uint)featureRows.Length);
        }

        Write(module, 32, 1);
        Write(module, 36, 1);
        Write(module, 40, 1);
        Write(module, 44, 1);
        var modules = new TrRmModuleTableReader(module, 1, names, 0, typeRows,
            1, 0, featureRows.Length);
        var features = new TrRmFeatureTableReader(featureBytes, featureRows.Length,
            schema);
        if (verifyPublishedPredicatePrerequisites)
        {
            Assert.That(TrRmFeatureManifestValidator.Requires(features, modules,
                1, TrRmFeatureTableReader.PublishedPredicatesFeatureId, schema),
                Is.True);
            Assert.That(TrRmFeatureManifestValidator.Requires(features, modules,
                1, TrRmFeatureTableReader.TypedFrozenValuesFeatureId, schema),
                Is.False);
        }
        var type = new byte[typeRows * 44];
        for (var index = 0; index < typeRows; index++)
        {
            var offset = index * 44;
            Write(type, offset, 1);
            Write(type, offset + 4, index == 0 ? 3u : 4u);
            Write(type, offset + 8, index == 0 ? typeFlags : 4u);
            Write(type, offset + 28, (uint)index + 1);
            Write(type, offset + 32, 1);
            Write(type, offset + 36, index == 0 ? typeFirst : secondTypeFirst);
            Write(type, offset + 40, index == 0 ? typeCount : secondTypeCount);
        }
        var types = new TrRmTypeDefTableReader(type, typeRows, names, modules,
            0, 0, typeRows, declaredCount);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutRows = new TrRmLayoutRow[typeRows];
        for (var index = 0; index < typeRows; index++)
        {
            layoutRows[index] = new TrRmLayoutRow(index + 1, 0, 0, 0, 0, 0, 0, 0);
        }
        var layouts = TrRmLayoutTableWriter.Encode(
            layoutRows, types, targetProfiles, 0);
        byte[] spec = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        if (pointerParameter)
        {
            spec = [0, 0, 0, 0, 4, 0, 0, 0, 1, 1, 0x8C, 8];
        }
        else if (referentParameter || initializationParameter)
        {
            spec = [
                0, 0, 0, 0, 3, 0, 0, 0, 9, 0, 0, 0,
                1, 0, 8, initializationParameter ? (byte)0x0E : (byte)0x07,
                0, 8, 0, 0, 0xF2,
            ];
        }
        else if (hasOutputParam && !nonBooleanResult
            && !referentParameter && !initializationParameter)
        {
            spec[^1] = 1;
        }

        var typeSpecCount = referentParameter || initializationParameter ? 2 : 1;
        var typeSpecs = new TrRmTypeSpecTableReader(
            spec, typeSpecCount, 1, 0, 0, 0);
        var parameterCount = twoParameters ? 2 : hasOutputParam ? 1 : 0;
        var signature = new byte[24];
        Write(signature, 0, 1);
        if (parameterCount != 0)
        {
            Write(signature, 8, 1);
            Write(signature, 12, (uint)parameterCount);
        }

        var signatures = new TrRmMethodSigTableReader(signature, 1, typeSpecs,
            parameterCount, 0);
        var param = parameterCount != 0 ? new byte[parameterCount * 16] : [];
        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            var offset = ordinal * 16;
            Write(param, offset, 1);
            Write(param, offset + 4, (uint)ordinal);
            Write(param, offset + 8, (uint)typeSpecCount);
            param[offset + 12] = paramMode;
        }

        var parameters = parameterCount != 0
            ? new TrRmParamTableReader(param, parameterCount, signatures, typeSpecs)
            : default;
        var method = new byte[28];
        Write(method, 0, (uint)methodOwnerType);
        Write(method, 4, 4);
        Write(method, 8, 1);
        Write(method, 16, methodFirst);
        Write(method, 20, methodCount);
        var methods = new TrRmMethodDefTableReader(method, 1, names, modules,
            types, signatures, declaredCount, 0);
        if (writeRows)
        {
            var index = new TrRmVariableTableIndex(section, recordCount, 8, 1);
            var rows = new TrRmContractRow[recordCount];
            for (var row = 1; row <= recordCount; row++)
            {
                var data = index.GetRow(row);
                rows[row - 1] = new TrRmContractRow(
                    BinaryPrimitives.ReadUInt32LittleEndian(data),
                    BinaryPrimitives.ReadUInt16LittleEndian(data[4..]),
                    data[8..].ToArray());
            }

            var emitted = copyOriginDraft
                ? TrRmContractTableWriter.EncodeCopyOriginDraft(
                    rows, types, methods, signatures, parameters, typeSpecs,
                    default, fieldPathCount, features, modules)
                : !developmentProfile && predicateGraphCount != 0
                    ? TrRmContractTableWriter.EncodeWithPredicateGraphs(
                        rows, types, methods, signatures, parameters, typeSpecs,
                        features, modules, predicateGraphCount)
                    : TrRmContractTableWriter.Encode(
                        rows, types, methods, signatures, parameters, typeSpecs,
                        developmentProfile);
            if (!canonicalizeCopyOrigin)
            {
                Assert.That(emitted, Is.EqualTo(section));
            }
            section = emitted;
        }
        TrRmContractTableReader contractReader;
        if (copyOriginDraft)
        {
            contractReader = new TrRmContractTableReader(section, recordCount, types, methods,
                signatures, parameters, typeSpecs, fieldPathCount, features,
                modules, schema, predicateGraphCount);
            if (expectedNativeCallableConvention is { } expectedConvention)
            {
                AssertNativeCallable(contractReader, expectedConvention);
            }
        }
        else if (!developmentProfile)
        {
            contractReader = new TrRmContractTableReader(section, recordCount,
                types, methods, signatures, parameters, typeSpecs, fieldPathCount,
                features, modules, schema, predicateGraphCount);
            if (expectedNativeCallableConvention is { } expectedConvention)
            {
                AssertNativeCallable(contractReader, expectedConvention);
            }
        }
        else
        {
            contractReader = new TrRmContractTableReader(section, recordCount,
                types, methods, signatures, parameters, typeSpecs,
                developmentProfile);
            if (expectedNativeCallableConvention is { } expectedConvention)
            {
                AssertNativeCallable(contractReader, expectedConvention);
            }
        }
        if (expectedPredicateGraphContractCount is int expectedPredicateCount)
        {
            Assert.That(contractReader.GetPredicateGraphContracts(1).Length,
                Is.EqualTo(expectedPredicateCount));
        }
        if (frameArtifact)
        {
            var artifactSections = new List<TrRmSection>
            {
                new(0x0001, 0, 1, module),
                new(0x0002, 0, 5, nameBytes),
            };
            if (featureRows.Length != 0)
            {
                artifactSections.Add(new TrRmSection(0x0004, 0,
                    (uint)featureRows.Length, featureBytes));
            }
            artifactSections.AddRange([
                new TrRmSection(0x0011, 0, (uint)typeSpecCount, spec),
                new TrRmSection(0x0013, 0, (uint)typeRows, type),
                new TrRmSection(0x0015, 0, (uint)typeRows, layouts),
                new TrRmSection(0x0016, 0, 1, method),
                new TrRmSection(0x0017, 0, 1, signature),
            ]);
            if (parameterCount != 0)
            {
                artifactSections.Add(new TrRmSection(0x0018, 0,
                    (uint)parameterCount, param));
            }
            artifactSections.Add(new TrRmSection(0x001B, 0,
                (uint)recordCount, section));
            if (predicateGraphCount != 0)
            {
                var graphBytes = TrRmPredicateGraphTableWriter.Encode([
                    new TrRmPredicateGraphRow(1, 1),
                ], nodeCount: 1, readFootprintCount: predicateGraphCount);
                var footprintBytes = TrRmPredicateReadFootprintTableWriter.Encode([
                    new TrRmPredicateReadFootprintRow(0, 0, 0, 0),
                ], rootCount: 0, dependencyCount: 0);
                artifactSections.Add(new TrRmSection(0x0037, 0,
                    (uint)predicateGraphCount, graphBytes));
                artifactSections.Add(new TrRmSection(0x0038, 0, 1, footprintBytes));
                artifactSections.Add(new TrRmSection(0x003B, 0, 1, new byte[16]));
            }
            var artifact = TrRmArtifactWriter.Encode(artifactSections, schema);
            if (copyOriginDraft)
            {
                _ = TrRmStructuralMetadataValidator
                    .ValidateCopyOriginDraftAndGetExecutionTables(
                        TrRmArtifactIndex.ForCopyOriginDraft(artifact));
            }
            else if (developmentProfile)
            {
                _ = TrRmStructuralMetadataValidator
                    .ValidateDevelopmentAndGetExecutionTables(
                        TrRmArtifactIndex.ForDevelopment(artifact));
            }
            else
            {
                TrRmStructuralMetadataValidator.Validate(artifact);
            }
        }

        return section;
    }

    private static void AssertNativeCallable(TrRmContractTableReader contracts,
        byte expectedConvention)
    {
        Assert.That(contracts.HasNativeCallableContract(1), Is.True);
        Assert.That(contracts.GetNativeCallableConvention(1),
            Is.EqualTo(expectedConvention));
        Assert.That(contracts.GetNativeCallableContractRow(1), Is.EqualTo(1));
    }

    private static void ValidateCopyOriginFieldArtifact(
        CopyOriginTestFact[] facts, byte summaryKind = 1,
        bool receiver = false, uint fieldPathRootTypeSpec = 2,
        bool nativeImportMethod = false, bool functionPointerAnchor = false,
        bool twoFields = false, bool reverseFieldPathRows = false,
        bool writerCanonicalizesFacts = false,
        bool writerCanonicalizesOrigins = false)
    {
        var fieldCount = twoFields ? 2 : 1;
        var fieldPathCount = twoFields ? 2 : 1;
        var strings = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Pair", 0), ("Data", 0), ("Compose", 0),
        ]);
        var module = new byte[48];
        Write(module, 0, 2);
        Write(module, 4, 1);
        Write(module, 24, 1);
        Write(module, 28, 3);
        Write(module, 32, 1);
        Write(module, 36, 1);
        Write(module, 40, 1);
        Write(module, 44, 1);

        var features = TrRmFeatureTableWriter.Encode([
            new TrRmFeatureRow(1, 1),
            new TrRmFeatureRow(2, 1),
            new TrRmFeatureRow(15, 1),
        ], TrRmArtifactSchema.CopyOriginDraft);
        var typeSpecs = Section(
            [1, 0, 0x8C, 8],
            [2, 0, 1, 0, 0, 0]);

        var type = new byte[44];
        Write(type, 0, 1);
        Write(type, 4, 3);
        Write(type, 8, 4);
        Write(type, 12, 1);
        Write(type, 16, (uint)fieldCount);
        Write(type, 28, 1);
        Write(type, 32, 1);
        var field = new byte[fieldCount * 16];
        for (var index = 0; index < fieldCount; index++)
        {
            var offset = index * 16;
            Write(field, offset, 1);
            Write(field, offset + 4, 4);
            Write(field, offset + 8, 1);
        }

        var layout = new byte[28];
        Write(layout, 0, 1);
        Write(layout, 20, 1);
        Write(layout, 24, (uint)fieldCount);
        var layoutField = new byte[fieldCount * 12];
        for (var index = 0; index < fieldCount; index++)
        {
            var offset = index * 12;
            Write(layoutField, offset, 1);
            Write(layoutField, offset + 4, (uint)index);
            Write(layoutField, offset + 8, (uint)index + 1);
        }

        var signature = new byte[24];
        Write(signature, 0, 2);
        Write(signature, 4, receiver ? 2u : 0u);
        Write(signature, 8, 1);
        Write(signature, 12, 1);
        var parameter = new byte[16];
        Write(parameter, 0, 1);
        Write(parameter, 8, 2);
        var payloadLength = 12;
        foreach (var fact in facts)
        {
            payloadLength = checked(payloadLength + 8
                + (fact.Origins.Length * 12));
        }

        var copyOrigin = new byte[payloadLength];
        copyOrigin[0] = summaryKind;
        Write(copyOrigin, 4, 2);
        Write(copyOrigin, 8, (uint)facts.Length);
        var payloadOffset = 12;
        foreach (var fact in facts)
        {
            Write(copyOrigin, payloadOffset, fact.DestinationPath);
            Write(copyOrigin, payloadOffset + 4, (uint)fact.Origins.Length);
            payloadOffset += 8;
            foreach (var origin in fact.Origins)
            {
                copyOrigin[payloadOffset] = origin.SourceKind;
                copyOrigin[payloadOffset + 1] = origin.SourceView;
                Write(copyOrigin, payloadOffset + 4, origin.SourceOrdinal);
                Write(copyOrigin, payloadOffset + 8, origin.SourcePath);
                payloadOffset += 12;
            }
        }

        var contractRows = new List<byte[]>
        {
            Contract(0x1600_0001, 0x0D, copyOrigin),
        };
        if (functionPointerAnchor)
        {
            contractRows.Add(Effects(4));
            var nativeAbi = new byte[8];
            Write(nativeAbi, 0, 1);
            nativeAbi[4] = 3;
            contractRows.Add(Contract(0x1600_0001, 0x10, nativeAbi));
        }

        var method = new byte[28];
        Write(method, 0, 1);
        Write(method, 4, 5);
        Write(method, 8, 1);
        Write(method, 16, 1);
        Write(method, 20, (uint)contractRows.Count);
        var fieldPathRows = new byte[fieldPathCount][];
        for (var index = 0; index < fieldPathCount; index++)
        {
            var fieldPath = new byte[12];
            Write(fieldPath, 0, fieldPathRootTypeSpec);
            Write(fieldPath, 4, 1);
            var fieldDef = reverseFieldPathRows
                ? fieldPathCount - index : index + 1;
            Write(fieldPath, 8, 0x1400_0000u | (uint)fieldDef);
            fieldPathRows[index] = fieldPath;
        }
        var fieldPaths = Section(fieldPathRows);
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, module),
            new(0x0002, 0, 5, strings),
            new(0x0004, 0, 3, features),
            new(0x0011, 0, 2, typeSpecs),
            new(0x0013, 0, 1, type),
            new(0x0014, 0, (uint)fieldCount, field),
            new(0x0015, 0, 1, layout),
            new(0x0016, 0, 1, method),
            new(0x0017, 0, 1, signature),
            new(0x0018, 0, 1, parameter),
            new(0x001B, 0, (uint)contractRows.Count,
                Section(contractRows.ToArray())),
        };
        if (nativeImportMethod)
        {
            var nativeImport = new byte[16];
            nativeImport[0] = 1;
            nativeImport[1] = 1;
            nativeImport[3] = 1;
            Write(nativeImport, 4, 1);
            Write(nativeImport, 8, 2);
            sections.Add(new TrRmSection(0x0021, 0, 1, nativeImport));
        }
        sections.Add(new TrRmSection(0x0023, 0,
            (uint)fieldCount, layoutField));
        sections.Add(new TrRmSection(0x004B, 0,
            (uint)fieldPathCount, fieldPaths));
        var artifact = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.CopyOriginDraft);
        var executionTables = TrRmStructuralMetadataValidator
            .ValidateCopyOriginDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForCopyOriginDraft(artifact));
        if (writerCanonicalizesFacts)
        {
            var row = new TrRmContractRow(
                0x1600_0001, 0x0D, copyOrigin);
            var written = TrRmContractTableWriter.EncodeCopyOriginDraft(
                [row], executionTables.TypeDefs, executionTables.Methods,
                executionTables.Signatures, executionTables.Parameters,
                executionTables.TypeSpecs, executionTables.FieldPaths,
                fieldPathCount, executionTables.Features, executionTables.Modules);
            var contractIndex = new TrRmVariableTableIndex(written, 1, 8, 1);
            var canonical = contractIndex.GetRow(1);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(canonical[20..]),
                Is.EqualTo(2));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(canonical[40..]),
                Is.EqualTo(1));
        }
        if (writerCanonicalizesOrigins)
        {
            var row = new TrRmContractRow(
                0x1600_0001, 0x0D, copyOrigin);
            var written = TrRmContractTableWriter.EncodeCopyOriginDraft(
                [row], executionTables.TypeDefs, executionTables.Methods,
                executionTables.Signatures, executionTables.Parameters,
                executionTables.TypeSpecs, executionTables.FieldPaths,
                fieldPathCount, executionTables.Features, executionTables.Modules);
            var contractIndex = new TrRmVariableTableIndex(written, 1, 8, 1);
            var canonical = contractIndex.GetRow(1);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(canonical[36..]),
                Is.EqualTo(2));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(canonical[48..]),
                Is.EqualTo(1));
        }
    }

    private readonly record struct CopyOriginTestOrigin(
        byte SourceKind, byte SourceView, uint SourceOrdinal, uint SourcePath);

    private readonly record struct CopyOriginTestFact(
        uint DestinationPath, CopyOriginTestOrigin[] Origins);

    private static byte[] Cleanup(uint typeRow = 1)
    {
        var data = new byte[4];
        Write(data, 0, 1);
        return Contract(0x1300_0000u | typeRow, 5, data);
    }

    private static byte[] CopyOriginContract()
    {
        var payload = new byte[32];
        payload[0] = 1;
        Write(payload, 4, 1);
        Write(payload, 8, 1);
        Write(payload, 12, 0);
        Write(payload, 16, 1);
        payload[20] = 1;
        payload[21] = 1;
        Write(payload, 24, 0);
        Write(payload, 28, 0);
        return Contract(0x1600_0001, 0x0D, payload);
    }

    private static byte[] CopyOriginContractWithOrigins(params uint[] ordinals)
    {
        var payload = new byte[20 + (ordinals.Length * 12)];
        payload[0] = 1;
        Write(payload, 4, 1);
        Write(payload, 8, 1);
        Write(payload, 12, 0);
        Write(payload, 16, (uint)ordinals.Length);
        for (var index = 0; index < ordinals.Length; index++)
        {
            var offset = 20 + (index * 12);
            payload[offset] = 1;
            payload[offset + 1] = 1;
            Write(payload, offset + 4, ordinals[index]);
        }

        return Contract(0x1600_0001, 0x0D, payload);
    }

    private static byte[] Effects()
        => Effects(2);

    private static byte[] Effects(uint mask)
    {
        var data = new byte[4];
        Write(data, 0, mask);
        return Contract(0x1600_0001, 1, data);
    }

    private static byte[] NativeCallable(byte convention = 1,
        uint owner = 0x1600_0001)
        => Contract(owner, 0x0C, [convention, 0, 0, 0]);

    private static byte[] PredicateGraphContract(byte phase, uint ordinal,
        uint graphRow)
    {
        var data = new byte[12];
        data[0] = phase;
        Write(data, 4, ordinal);
        Write(data, 8, graphRow);
        return Contract(0x1600_0001, 0x0B, data);
    }

    private static byte[] OutputStateContract(uint ordinal, uint graphRow,
        byte state)
    {
        var data = new byte[12];
        Write(data, 0, ordinal);
        Write(data, 4, graphRow);
        data[8] = state;
        return Contract(0x1600_0001, 2, data);
    }

    private static byte[] NativeParameter(uint ordinal, byte access, byte retention, uint extent)
    {
        var data = new byte[20];
        Write(data, 0, ordinal);
        Write(data, 4, 1);
        data[8] = access;
        data[9] = retention;
        data[10] = extent == 0 ? (byte)0 : (byte)1;
        Write(data, 12, extent);
        return Contract(0x1600_0001, 0x11, data);
    }

    private static byte[] Contract(uint owner, ushort kind, byte[] data)
    {
        var row = new byte[8 + data.Length];
        Write(row, 0, owner);
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(4), kind);
        data.CopyTo(row.AsSpan(8));
        return row;
    }

    private static byte[] Section(params byte[][] rows)
    {
        var indexBytes = (rows.Length + 1) * sizeof(uint);
        var dataBytes = 0;
        foreach (var row in rows)
        {
            dataBytes += row.Length;
        }

        var section = new byte[indexBytes + dataBytes];
        var offset = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            Write(section, index * sizeof(uint), (uint)offset);
            rows[index].CopyTo(section.AsSpan(indexBytes + offset));
            offset += rows[index].Length;
        }

        Write(section, rows.Length * sizeof(uint), (uint)offset);
        return section;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
