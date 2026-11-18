// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmObjectConstructionBodyTests
{
    private const TrRmArtifactSchema Schema
        = TrRmArtifactSchema.C1M1Construction2Draft;

    [TestCase(0)]
    [TestCase(15)]
    [TestCase(255)]
    [TestCase(65535)]
    public void ReadsObjectConstructionAtEveryRecordIndex(int recordIndex)
    {
        var body = CreateBody(recordIndex: recordIndex);
        var reader = new TrRmRegisterBodyReader(body, schema: Schema);

        Assert.That(reader.HasConstructionContextHome, Is.True);
        Assert.That(reader.HasObjectConstructionRecords, Is.True);
        Assert.That(TrRmRegisterBodyReader.InstructionLength(0xED, Schema),
            Is.EqualTo(4));
    }

    [Test]
    public void ReadsBeginAbortAndRegisterOrHomePublication()
    {
        _ = new TrRmRegisterBodyReader(
            CreateBody(action: 1), schema: Schema);
        _ = new TrRmRegisterBodyReader(
            CreateBody(action: 2, destinationKind: 1,
                destinationIndex: 13), schema: Schema);
        _ = new TrRmRegisterBodyReader(
            CreateBody(action: 2, destinationKind: 2,
                destinationIndex: 1), schema: Schema);
        _ = new TrRmRegisterBodyReader(
            CreateBody(action: 3), schema: Schema);
    }

    [Test]
    public void RejectsMalformedObjectConstructionRecordWriterOperands()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateConstructionRecord(0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateConstructionRecord(1, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateConstructionRecord(2, 1, 0, 14));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyRecordWriter.EncodeObjectConstruction(1,
                0, 0, 0, TrRmArtifactSchema.C1M1ConstructionDraft));
    }

    [Test]
    public void ReadsConstructionContextAtTheLastHomeIndex()
    {
        var body = CreateBody(contextHomeIndex: 65535,
            homeCount: 65536);

        Assert.That(new TrRmRegisterBodyReader(body, schema: Schema)
            .HasConstructionContextHome, Is.True);
    }

    [Test]
    public void RejectsMalformedObjectConstructionOperands()
    {
        AssertInvalid(action: 0);
        AssertInvalid(action: 1, destinationKind: 1);
        AssertInvalid(action: 1, destinationIndex: 1);
        AssertInvalid(action: 2, destinationKind: 1,
            destinationIndex: 14);
        AssertInvalid(action: 2, destinationKind: 3);
        AssertInvalid(action: 2, destinationKind: 2,
            destinationIndex: 1, extraHomeRole: 4);
        AssertInvalid(action: 3, destinationIndex: 1);
        AssertInvalid(reserved: 1);
        AssertInvalid(contextHomeRole: 3);
        AssertInvalid(instructionControl: 1);
        AssertInvalid(targetRecordKind: 1);
        AssertInvalid(schema: TrRmArtifactSchema.C1M1ConstructionDraft);
    }

    [Test]
    public void RejectsObjectConstructionAsABlockTerminator()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateBody(appendHalt: false));
    }

    [Test]
    public void RejectsOrdinaryContextHomeReads()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xD5, 0, 0, 0,
                0xB8, 1,
            ]));
    }

    [TestCase(new byte[] { 0x05, 0xB8, 1 })]
    [TestCase(new byte[] { 0x07, 0xB8, 1 })]
    [TestCase(new byte[] { 0x13, 0, 0xB8, 1 })]
    [TestCase(new byte[] { 0x14, 0, 0xB8, 1 })]
    [TestCase(new byte[] { 0x15, 0, 0xB8, 1 })]
    [TestCase(new byte[] { 0x60, 0, 0xB8, 1 })]
    [TestCase(new byte[] { 0xD5, 0, 0, 0, 0xB8, 1 })]
    [TestCase(new byte[] { 0xD6, 0x10, 0, 0, 0xB8, 1 })]
    [TestCase(new byte[] { 0xD7, 0, 0, 0, 0xB8, 1 })]
    [TestCase(new byte[] { 0xB8, 0 })]
    [TestCase(new byte[] { 0xEB, 0, 0, 0 })]
    public void RejectsDirectContextHomeOperations(byte[] code)
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody(code));
    }

    [TestCase(0x06)]
    [TestCase(0x08)]
    public void RejectsDirectContextHomeOperationsAtHomeIndexOne(
        byte opcode)
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([opcode, 0xB8, 0],
                constructionContextAtHomeIndexOne: true));
    }

    [Test]
    public void ValidatesProjectionBeginInitializerPublishLifecycle()
    {
        ValidateProjectionBody([
            0xED, 0, 1, 0,
            0xE0, 0, 2, 0,
            0xED, 0, 3, 0,
            0xB8, 1,
        ]);
    }

    [Test]
    public void ValidatesProjectionCallingAnImportedBodylessInitializer()
    {
        ValidateProjectionBody([
            0xED, 0, 1, 0,
            0xE0, 0, 2, 0,
            0xED, 0, 4, 0,
            0xB8, 1,
        ], includeContextField: true, initializerIsImported: true);
    }

    [Test]
    public void RequiresClassConstructionFeatureForBodyfulProjection()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                0xED, 0, 3, 0,
                0xB8, 1,
            ], includeConstructionFeature: false));
    }

    [Test]
    public void RequiresClassConstructionFeatureForBodyfulInitializer()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xD8, 0, 1, 0,
                0xD9, 0, 2, 0,
                0x18, 0,
            ], includeConstructionFeature: false, methodDefRow: 1,
                initializerBody: true));
    }

    [Test]
    public void RejectsGcClassConstructionContexts()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                0xED, 0, 3, 0,
                0xB8, 1,
            ], classTypeQualifiers: 0x02));
    }

    [Test]
    public void ReadsFinalFieldsThroughTheUnpublishedConstructionContext()
    {
        ValidateProjectionBody([
            0xED, 0, 1, 0,
            0xE0, 0, 2, 0,
            0xD8, 0, 3, 0,
            0xDA, 0, 3, 0,
            0xED, 0, 4, 0,
            0xB8, 1,
        ], includeContextField: true);
    }

    [Test]
    public void RejectsReadingFieldsNotGuaranteedByTheInitializer()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                0xD8, 0, 3, 0,
                0xDA, 0, 3, 0,
                0xED, 0, 4, 0,
                0xB8, 1,
            ], includeContextField: true,
                declareInitializedField: false));
    }

    [Test]
    public void ReadsConstructionFieldsThroughShortPlaceInstructions()
    {
        ValidateProjectionBody([
            0xED, 0, 1, 0,
            0xE0, 0, 2, 0,
            0x61, 0x30,
            0x63, 0,
            0xED, 0, 4, 0,
            0xB8, 1,
        ], includeContextField: true);
    }

    [Test]
    public void RejectsReadingThroughAnInitializerPlace()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0x61, 0x10,
                0x63, 0,
                0x18, 0,
            ], methodDefRow: 1, initializerBody: true));
    }

    [Test]
    public void RejectsSelectingTheInitializerReceiverAsAHome()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xD8, 0, 1, 0,
                0x18, 0,
            ], methodDefRow: 1, initializerBody: true,
                initializerReceiverAsPlaceTarget: true));
    }

    [TestCase(new byte[] { 0x13, 0, 0x18, 0 })]
    [TestCase(new byte[] { 0xD5, 0, 0, 0, 0x18, 0 })]
    [TestCase(new byte[] { 0xB8, 0 })]
    public void RejectsReadingOrReturningTheInitializerReceiverHome(
        byte[] code)
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody(code, methodDefRow: 1,
                initializerBody: true));
    }

    [Test]
    public void RejectsConstructionContextAsAnOrdinaryCallArgument()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                0xED, 0, 4, 0,
                0xB8, 1,
            ], callSiteArgumentHomeIndex: 0));
    }

    [TestCase(new byte[] { 0x82, 0, 0x18, 0 })]
    [TestCase(new byte[] { 0xE1, 0xF0, 0, 0, 0x18, 0 })]
    public void RejectsCompactCallsToAnInitializer(byte[] code)
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody(code, includeContextField: true));
    }

    [Test]
    public void AllowsBorrowingAnOrdinaryClassReceiverAfterPublication()
    {
        ValidateProjectionBody([
            0xED, 0, 1, 0,
            0xE0, 0, 2, 0,
            0xED, 0, 4, 0,
            0xE0, 0, 5, 0,
            0xB8, 1,
        ], includeContextField: true,
            callOrdinaryClassMethod: true);
    }

    [Test]
    public void PreservesReceiverFirstCallSiteFormalOrder()
    {
        ValidateProjectionBody([
            0xED, 0, 1, 0,
            0xE0, 0, 2, 0,
            0xED, 0, 4, 0,
            0xE0, 0, 5, 0,
            0xB8, 1,
        ], includeContextField: true,
            callOrdinaryClassMethod: true,
            ordinaryMethodHasParameter: true);
    }

    [Test]
    public void AllowsReadOnlyCallOnWritableClassHandle()
    {
        ValidateProjectionBody([
            0xED, 0, 1, 0,
            0xE0, 0, 2, 0,
            0xED, 0, 4, 0,
            0xE0, 0, 5, 0,
            0xB8, 1,
        ], includeContextField: true,
            callOrdinaryClassMethod: true, mutableClass: true);
    }

    [Test]
    public void AllowsWritableCallOnWritableClassHandle()
    {
        ValidateProjectionBody([
            0xED, 0, 1, 0,
            0xE0, 0, 2, 0,
            0xED, 0, 4, 0,
            0xE0, 0, 5, 0,
            0xB8, 1,
        ], includeContextField: true,
            callOrdinaryClassMethod: true,
            ordinaryReceiverSourceUse: 4, mutableClass: true,
            ordinaryReceiverWritable: true);
    }

    [TestCase(new byte[] {
        0xED, 0, 1, 0,
        0xE0, 0, 2, 0,
        0xED, 0, 4, 0,
        0x13, 0x21,
        0x82, 0x10,
        0x17, 0,
    })]
    [TestCase(new byte[] {
        0xED, 0, 1, 0,
        0xE0, 0, 2, 0,
        0xED, 0, 4, 0,
        0x13, 0x21,
        0xE1, 0xF0, 1, 0,
        0x17, 0,
    })]
    public void AllowsCompactOrdinaryClassReceiverCalls(byte[] code)
    {
        ValidateProjectionBody(code, includeContextField: true,
            callOrdinaryClassMethod: true,
            compactOrdinaryClassCall: true, publishToRegister: true,
            ordinaryMethodHasParameter: true);
    }

    [TestCase(new byte[] {
        0xED, 0, 1, 0,
        0xE0, 0, 2, 0,
        0xED, 0, 4, 0,
        0x80, 0x10,
        0x17, 0,
    })]
    [TestCase(new byte[] {
        0xED, 0, 1, 0,
        0xE0, 0, 2, 0,
        0xED, 0, 4, 0,
        0xE1, 0x10, 1, 0,
        0x17, 1,
    })]
    public void TracksCompactClassReceiverCallResults(byte[] code)
    {
        ValidateProjectionBody(code, includeContextField: true,
            callOrdinaryClassMethod: true,
            compactOrdinaryClassCall: true, publishToRegister: true,
            ordinaryMethodReturnsClass: true);
    }

    [Test]
    public void RejectsWritableMethodOnImmutableClass()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                0xED, 0, 4, 0,
                0xE0, 0, 5, 0,
                0xB8, 1,
            ], includeContextField: true,
                callOrdinaryClassMethod: true,
                ordinaryReceiverSourceUse: 4,
                ordinaryReceiverWritable: true));
    }

    [TestCase(4)]
    [TestCase(5)]
    public void RejectsIncorrectOrdinaryClassReceiverSourceUse(
        int sourceUse)
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                0xED, 0, 4, 0,
                0xE0, 0, 5, 0,
                0xB8, 1,
            ], includeContextField: true,
                callOrdinaryClassMethod: true,
                ordinaryReceiverSourceUse: sourceUse));
    }

    [Test]
    public void RejectsConstructionContextAsAnOrdinaryReceiver()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                0xED, 0, 4, 0,
                0xE0, 0, 5, 0,
                0xB8, 1,
            ], includeContextField: true,
                callOrdinaryClassMethod: true,
                ordinaryReceiverHomeIndex: 0));
    }

    [Test]
    public void RejectsTypeMismatchedOrdinaryClassReceiver()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                0xED, 0, 4, 0,
                0xE0, 0, 5, 0,
                0xB8, 1,
            ], includeContextField: true,
                callOrdinaryClassMethod: true,
                ordinaryReceiverHomeIndex: 2));
    }

    [Test]
    public void RejectsCallResultsIntoConstructionContext()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                0xED, 0, 4, 0,
                0xB8, 1,
                ], includeContextField: true,
                    callSiteReturnsClassToContext: true));
    }

    [Test]
    public void RejectsSwitchingOnAConstructionContextHome()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([0xB2, 5],
                includeContextField: true,
                switchOnConstructionContextHome: true));
    }

    [Test]
    public void RejectsConstructionContextAsAPlaceTarget()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xD8, 0, 3, 0,
                0xB8, 1,
            ], includeContextField: true,
                contextFieldTargetKind: 1,
                contextFieldBaseKind: 0,
                contextFieldTargetRefKind: 0,
                contextFieldTargetRef: 0));
    }

    [Test]
    public void RejectsNonReadConstructionContextPlaceSelectors()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xD8, 0, 3, 0,
                0xB8, 1,
            ], includeContextField: true, contextFieldAccessKind: 2));
    }

    [Test]
    public void RejectsWritingThroughAConstructionContextPlace()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                0xD8, 0, 3, 0,
                0xD9, 0, 5, 0,
                0xED, 0, 4, 0,
                0xB8, 1,
            ], includeContextField: true,
                includeConstructionContextStore: true));
    }

    [TestCase(0xDB)]
    [TestCase(0xDC)]
    [TestCase(0xDD)]
    public void RejectsNonReadConstructionContextPlaceInstructions(
        byte opcode)
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                opcode, 0, 3, 0,
                0xED, 0, 4, 0,
                0xB8, 1,
            ], includeContextField: true));
    }

    [TestCase(0x62)]
    [TestCase(0x64)]
    [TestCase(0x65)]
    [TestCase(0x66)]
    [TestCase(0x67)]
    public void RejectsNonReadConstructionContextPlaceRegisterUses(
        byte opcode)
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                0x61, 0x30,
                opcode, 0,
                0xED, 0, 4, 0,
                0xB8, 1,
            ], includeContextField: true));
    }

    [TestCase(0x62)]
    [TestCase(0x65)]
    [TestCase(0x66)]
    [TestCase(0x67)]
    public void RejectsEscapingOrRetiringInitializerFieldPlaces(
        byte opcode)
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0x61, 0x10,
                opcode, 0,
                0x18, 0,
            ], methodDefRow: 1, initializerBody: true));
    }

    [TestCase(0xDB)]
    [TestCase(0xDC)]
    [TestCase(0xDD)]
    public void RejectsNonReadInitializerPlaceInstructions(byte opcode)
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                opcode, 0, 1, 0,
                0x18, 0,
            ], methodDefRow: 1, initializerBody: true));
    }

    [Test]
    public void RejectsContextFieldAccessBeforeInitializerCompletion()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xD8, 0, 3, 0,
                0xDA, 0, 3, 0,
                0xE0, 0, 2, 0,
                0xED, 0, 4, 0,
                0xB8, 1,
            ], includeContextField: true));
    }

    [Test]
    public void RejectsPublishingWithALiveConstructionFieldPlace()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                0xD8, 0, 3, 0,
                0xED, 0, 4, 0,
                0xB8, 1,
            ], includeContextField: true));
    }

    [Test]
    public void RejectsRestartingAfterAbortingConstruction()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xED, 0, 5, 0,
                0xED, 0, 1, 0,
                0xE0, 0, 2, 0,
                0xED, 0, 4, 0,
                0xB8, 1,
            ], includeContextField: true,
                includeConstructionAbort: true));
    }

    [Test]
    public void RejectsRestartingConstructionAcrossALoopBackedge()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xED, 0, 1, 0,
                0xED, 0, 5, 0,
                0xB0, 0,
            ], includeContextField: true,
                includeConstructionAbort: true));
    }

    [Test]
    public void ValidatesInitializerReceiverFieldInitializationAuthority()
    {
        ValidateProjectionBody([
            0xD8, 0, 1, 0,
            0xD9, 0, 2, 0,
            0x18, 0,
        ], methodDefRow: 1, initializerBody: true);
    }

    [Test]
    public void ValidatesInitializerReceiverShortFieldInitialization()
    {
        ValidateProjectionBody([
        0x13, 0x11,
        0x61, 0x10,
        0x64, 0x10,
        0x18, 0,
        ], methodDefRow: 1, initializerBody: true);
    }

    [Test]
    public void RejectsInitializerShortStoresOutsideItsMemberSet()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
        ValidateProjectionBody([
            0x13, 0x11,
            0x61, 0x10,
            0x64, 0x10,
            0x18, 0,
        ], methodDefRow: 1, initializerBody: true,
            declareInitializedField: false));
    }

    [Test]
    public void RejectsInitializerStoresOutsideItsDeclaredMemberSet()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody([
                0xD8, 0, 1, 0,
                0xD9, 0, 2, 0,
                0x18, 0,
            ], methodDefRow: 1, initializerBody: true,
                declareInitializedField: false));
    }

    [TestCase(new byte[] { 0xED, 0, 1, 0, 0xED, 0, 3, 0, 0xB8, 1 })]
    [TestCase(new byte[] { 0xED, 0, 3, 0, 0xE0, 0, 2, 0, 0xB8, 1 })]
    [TestCase(new byte[] { 0xED, 0, 1, 0, 0xE0, 0, 2, 0, 0xE0, 0, 2, 0, 0xED, 0, 3, 0, 0xB8, 1 })]
    [TestCase(new byte[] { 0xED, 0, 1, 0, 0xE0, 0, 2, 0, 0xB8, 1 })]
    public void RejectsInvalidProjectionLifecycle(byte[] code)
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateProjectionBody(code));
    }

    private static void AssertInvalid(byte action = 1,
        byte destinationKind = 0, int contextHomeIndex = 0,
        int destinationIndex = 0, int reserved = 0,
        int contextHomeRole = 4, int homeCount = 2,
        int extraHomeRole = 3, int typeMapCount = 1,
        int destinationHomeTypeMapIndex = 0,
        byte instructionControl = 0, byte targetRecordKind = 0x11,
        TrRmArtifactSchema schema = Schema)
    {
        _ = Assert.Throws<InvalidDataException>(() => _ = CreateBody(
            action: action, destinationKind: destinationKind,
            contextHomeIndex: contextHomeIndex,
            destinationIndex: destinationIndex, reserved: reserved,
            contextHomeRole: contextHomeRole, homeCount: homeCount,
            extraHomeRole: extraHomeRole, typeMapCount: typeMapCount,
            destinationHomeTypeMapIndex: destinationHomeTypeMapIndex,
            instructionControl: instructionControl,
            targetRecordKind: targetRecordKind, schema: schema));
    }

    private static byte[] CreateBody(int recordIndex = 1,
        byte action = 1, byte destinationKind = 0,
        int contextHomeIndex = 0, int destinationIndex = 0,
        int reserved = 0, int contextHomeRole = 4, int homeCount = 2,
        int extraHomeRole = 3, int typeMapCount = 1,
        int destinationHomeTypeMapIndex = 0,
        byte instructionControl = 0, byte targetRecordKind = 0x11,
        bool appendHalt = true,
        TrRmArtifactSchema schema = Schema)
    {
        var actualRecordIndex = recordIndex;
        var recordCount = Math.Max(2, actualRecordIndex + 1);
        var records = new ReadOnlyMemory<byte>[recordCount];
        var entryState = new byte[8];
        entryState[0] = 0x01;
        for (var index = 0; index < records.Length; index++)
        {
            records[index] = entryState;
        }

        var construction = new byte[12];
        construction[0] = targetRecordKind;
        construction[4] = action;
        construction[5] = destinationKind;
        BinaryPrimitives.WriteUInt16LittleEndian(construction.AsSpan(6),
            checked((ushort)contextHomeIndex));
        BinaryPrimitives.WriteUInt16LittleEndian(construction.AsSpan(8),
            checked((ushort)destinationIndex));
        BinaryPrimitives.WriteUInt16LittleEndian(construction.AsSpan(10),
            checked((ushort)reserved));
        records[actualRecordIndex] = construction;

        var homes = new byte[checked(homeCount * 8)];
        for (var index = 0; index < homeCount; index++)
        {
            var home = homes.AsSpan(index * 8, 8);
            home[0] = (byte)(index == contextHomeIndex
                ? contextHomeRole
                : extraHomeRole);
            BinaryPrimitives.WriteUInt16LittleEndian(home[2..],
                checked((ushort)(index == 1
                    ? destinationHomeTypeMapIndex : 0)));
        }

        var types = new byte[checked(typeMapCount * sizeof(uint))];
        BinaryPrimitives.WriteUInt32LittleEndian(types, 0xF200_0001);
        if (typeMapCount > 1)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(types.AsSpan(4),
                0xF200_0002);
        }

        var code = new byte[appendHalt ? 5 : 4];
        code[0] = 0xED;
        code[1] = instructionControl;
        BinaryPrimitives.WriteUInt16LittleEndian(code.AsSpan(2),
            checked((ushort)actualRecordIndex));
        if (appendHalt)
        {
            code[4] = 0x00;
        }

        var entryStateRecord = (ushort)(actualRecordIndex == 0 ? 1 : 0);
        return TrRmRegisterBodyWriter.Encode(homes, types,
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty,
            [new TrRmRegisterBodyBlock(0, (uint)code.Length,
                entryStateRecord)], records, code, schema: schema);
    }

    private static void ValidateProjectionBody(byte[] code,
        bool includeConstructionFeature = true,
        bool includeContextField = false, int methodDefRow = 2,
        bool initializerBody = false,
        bool declareInitializedField = true,
        int callSiteArgumentHomeIndex = 2,
        bool switchOnConstructionContextHome = false,
        int contextFieldAccessKind = 1,
        int contextFieldTargetKind = 2,
        int contextFieldBaseKind = 2,
        int contextFieldTargetRefKind = 1,
        uint contextFieldTargetRef = 1,
        bool callSiteReturnsClassToContext = false,
        bool constructionContextAtHomeIndexOne = false,
        bool includeConstructionContextStore = false,
        bool includeConstructionAbort = false,
        bool initializerReceiverAsPlaceTarget = false,
        bool callOrdinaryClassMethod = false,
        int ordinaryReceiverSourceUse = 3,
        int ordinaryReceiverHomeIndex = 1,
        bool mutableClass = false,
        bool ordinaryReceiverWritable = false,
        bool compactOrdinaryClassCall = false,
        bool publishToRegister = false,
        bool ordinaryMethodHasParameter = false,
        bool initializerIsImported = false,
        bool ordinaryMethodReturnsClass = false,
        byte classTypeQualifiers = 0)
    {
        var featureRows = new[] {
            TrRmFeatureRow.PublishedPredicates,
            TrRmFeatureRow.TypedFrozenValues,
            TrRmFeatureRow.GeneralPredicateGraphsCandidate,
            TrRmFeatureRow.GenericMethodCallsCandidate,
        };
        var featuresToWrite = includeConstructionFeature
            ? [.. featureRows, TrRmFeatureRow.ClassConstructionBodiesCandidate]
            : featureRows;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("Module", 0), ("", 0), ("Widget", 0),
            ("Initialize", 0), ("Create", 0), ("Value", 0),
            ("Touch", 0), ("Provider", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 8);
        var featureBytes = TrRmFeatureTableWriter.Encode(featuresToWrite,
            Schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featuresToWrite.Length, Schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                0, 0, 1, 1, 1, 1, 1, featuresToWrite.Length),
        ], strings, initializerIsImported ? 1 : 0, 1,
            callOrdinaryClassMethod ? 3 : 2, 0,
            featuresToWrite.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            initializerIsImported ? 1 : 0, 1,
            callOrdinaryClassMethod ? 3 : 2, 0,
            featuresToWrite.Length);
        var moduleRefBytes = initializerIsImported
            ? TrRmModuleRefTableWriter.Encode([
                new TrRmModuleRefRow(8,
                    TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                    0),
            ], strings, modules)
            : [];
        var moduleRefs = initializerIsImported
            ? new TrRmModuleRefTableReader(moduleRefBytes, 1,
                strings, modules)
            : default;
        var typeSpecRows = new List<ReadOnlyMemory<byte>> {
            TrRmTypeSpecTableWriter.Reference(0x02, 1,
                (byte)(classTypeQualifiers
                    | (mutableClass ? 0x01 : 0))),
        };
        var ordinaryReceiverTypeSpecRef = 1u;
        if (callOrdinaryClassMethod
            && mutableClass != ordinaryReceiverWritable)
        {
            ordinaryReceiverTypeSpecRef = checked(
                (uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02, 1,
                ordinaryReceiverWritable ? (byte)0x01 : (byte)0));
        }
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            1, 0, 0, 0, Schema,
            methodDefCount: callOrdinaryClassMethod ? 3 : 2);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count,
            1, 0, 0, 0, Schema,
            methodDefCount: callOrdinaryClassMethod ? 3 : 2);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3,
                mutableClass ? 0x8Au : 0x0Au,
                1, 1, 0, 0, 0, 0, 0, 0),
        ], strings, modules, 1, 0, 0, 2, Schema);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 1, strings,
            modules, 1, 0, 0, 2, Schema);
        var fieldBytes = TrRmFieldDefTableWriter.Encode(
            [new TrRmFieldDefRow(1, 6, 0xF200_0008, 0)],
            strings, types, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 1, strings,
            types, typeSpecs);
        var signatureRows = new List<TrRmMethodSigRow> {
            new(0xF200_0000, 1, 1, 1, 0, 0),
            new(1, 0, 2, 1, 0, 0),
        };
        if (callOrdinaryClassMethod)
        {
            signatureRows.Add(new TrRmMethodSigRow(
                ordinaryMethodReturnsClass
                    ? ordinaryReceiverTypeSpecRef : 0xF200_0000,
                ordinaryReceiverTypeSpecRef,
                ordinaryMethodHasParameter ? 3 : 0,
                ordinaryMethodHasParameter ? 1 : 0, 0, 0));
        }
        var paramCount = ordinaryMethodHasParameter ? 3 : 2;
        var signatureBytes = TrRmMethodSigTableWriter.Encode(
            signatureRows, typeSpecs, paramCount, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count, typeSpecs, paramCount, 0);
        var methodRows = new List<TrRmMethodDefRow> {
            new(1, 4, 1, 0, 1, 1,
                initializerIsImported ? 0 : 1),
            new(1, 5, 2, 0, 2, 1,
                initializerIsImported ? 1 : 2),
        };
        if (callOrdinaryClassMethod)
        {
            methodRows.Add(new TrRmMethodDefRow(1, 7, 3, 0,
                0, 0, initializerIsImported ? 2 : 3));
        }
        var bodyCount = methodRows.Count
            - (initializerIsImported ? 1 : 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows,
            strings, modules, types, signatures, 2, bodyCount);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodRows.Count, strings, modules, types, signatures, 2,
            bodyCount);
        var importBytes = initializerIsImported
            ? TrRmImportTableWriter.Encode([
                new TrRmImportRow(1, 4, 0x1600_0001),
            ], strings, moduleRefs, types, methods, default, default,
                default, fields, features, modules, Schema)
            : [];
        var imports = initializerIsImported
            ? new TrRmImportTableReader(importBytes, 1, strings,
                moduleRefs, types, methods, default, default, default,
                fields, features, modules, Schema)
            : default;
        var parameterRows = new List<TrRmParamRow> {
            new(1, 0, 0xF200_0008, 0),
            new(2, 0, 0xF200_0008, 0),
        };
        if (ordinaryMethodHasParameter)
        {
            parameterRows.Add(new TrRmParamRow(3, 0,
                0xF200_0008, 0));
        }
        var parameterBytes = TrRmParamTableWriter.Encode(parameterRows,
            signatures, typeSpecs, Schema);
        var parameters = new TrRmParamTableReader(parameterBytes,
            parameterRows.Count,
            signatures, typeSpecs, Schema);
        var initializerPayload = new byte[
            declareInitializedField ? 12 : 8];
        initializerPayload[0] = 1;
        if (declareInitializedField)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                initializerPayload.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                initializerPayload.AsSpan(8), 0x1400_0001);
        }
        var projectionPayload = new byte[8];
        projectionPayload[0] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(
            projectionPayload.AsSpan(4), 1);
        var contractRows = new[] {
            new TrRmContractRow(0x1600_0001, 3, initializerPayload),
            new TrRmContractRow(0x1600_0002, 3, projectionPayload),
        };
        var contractBytes = TrRmContractTableWriter
            .EncodeC1M1Construction2Draft(contractRows, types, methods,
                signatures, parameters, typeSpecs, fields, default, imports,
                features, modules, predicateGraphCount: 0);
        var contracts = new TrRmContractTableReader(contractBytes, 2,
            types, methods, signatures, parameters, typeSpecs,
            fieldPathCount: 0, features, modules, Schema,
            predicateGraphCount: 0, fields: fields, imports: imports);
        var bodyBytes = initializerBody
            ? CreateInitializerBody(code,
                initializerReceiverAsPlaceTarget)
            : CreateProjectionBody(code, includeContextField,
                callSiteArgumentHomeIndex,
                switchOnConstructionContextHome,
                contextFieldAccessKind, contextFieldTargetKind,
                contextFieldBaseKind, contextFieldTargetRefKind,
                contextFieldTargetRef, callSiteReturnsClassToContext,
                constructionContextAtHomeIndexOne,
                includeConstructionContextStore,
                includeConstructionAbort,
                callOrdinaryClassMethod,
                ordinaryReceiverSourceUse,
                ordinaryReceiverHomeIndex, mutableClass,
                ordinaryReceiverWritable, compactOrdinaryClassCall,
                publishToRegister, ordinaryMethodHasParameter,
                initializerIsImported, ordinaryMethodReturnsClass);
        var body = new TrRmRegisterBodyReader(bodyBytes, 2, Schema);
        TrRmRegisterBodyMapValidator.Validate(body, methodDefRow, 1, features, modules,
            types, default, typeSpecs, default, default, methods, signatures,
            parameters, fields, default, default, default, default, default,
            default, default,
            contracts, imports, default, default, moduleRefs, default,
            default, default, default);
    }

    private static byte[] CreateInitializerBody(byte[] code,
        bool initializerReceiverAsPlaceTarget)
    {
        var homes = new byte[16];
        homes[0] = 1;
        homes[8] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(homes.AsSpan(10), 1);
        var typeMap = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(typeMap, 0x1100_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(typeMap.AsSpan(4),
            0xF200_0008);
        var entryState = new byte[8];
        entryState[0] = 1;
        var selection = initializerReceiverAsPlaceTarget
            ? TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 1, accessKind: 1, destinationKind: 1,
                baseKind: 0, destinationIndex: 0, baseIndex: 0,
                indexKind: 0, targetRefKind: 0, indexIndex: 0,
                targetRef: 0, referentTypeMapIndex: 1, Schema)
            : TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 2, accessKind: 3, destinationKind: 1,
                baseKind: 2, destinationIndex: 0, baseIndex: 0,
                indexKind: 0, targetRefKind: 1, indexIndex: 0,
                targetRef: 1, referentTypeMapIndex: 1, Schema);
        var store = new byte[20];
        store[0] = 0x05;
        store[4] = 1;
        store[5] = 2;
        store[6] = 1;
        store[7] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(store.AsSpan(10), 1);
        return TrRmRegisterBodyWriter.Encode(homes, typeMap,
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty,
            [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
            [entryState, selection, store], code,
            contractCount: 2, schema: Schema);
    }

    private static byte[] CreateProjectionBody(byte[] code,
        bool includeContextField, int callSiteArgumentHomeIndex,
        bool switchOnConstructionContextHome,
        int contextFieldAccessKind, int contextFieldTargetKind,
        int contextFieldBaseKind, int contextFieldTargetRefKind,
        uint contextFieldTargetRef,
        bool callSiteReturnsClassToContext,
        bool constructionContextAtHomeIndexOne,
        bool includeConstructionContextStore,
        bool includeConstructionAbort,
        bool callOrdinaryClassMethod,
        int ordinaryReceiverSourceUse,
        int ordinaryReceiverHomeIndex,
        bool mutableClass,
        bool ordinaryReceiverWritable,
        bool compactOrdinaryClassCall,
        bool publishToRegister,
        bool ordinaryMethodHasParameter,
        bool initializerIsImported,
        bool ordinaryMethodReturnsClass)
    {
        var homes = new byte[24];
        homes[0] = (byte)(constructionContextAtHomeIndexOne ? 3 : 4);
        homes[8] = (byte)(constructionContextAtHomeIndexOne ? 4 : 3);
        homes[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(homes.AsSpan(18), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(homes.AsSpan(20), 0);
        var typeMap = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(typeMap, 0x1100_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(typeMap.AsSpan(4),
            0xF200_0008);
        var methodMap = new byte[
            callSiteReturnsClassToContext || callOrdinaryClassMethod ? 8 : 4];
        BinaryPrimitives.WriteUInt32LittleEndian(methodMap,
            initializerIsImported ? 0x1C00_0001u : 0x1600_0001u);
        if (callSiteReturnsClassToContext)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodMap.AsSpan(4), 0x1600_0002);
        }
        else if (callOrdinaryClassMethod)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodMap.AsSpan(4), 0x1600_0003);
        }
        var entryState = new byte[8];
        entryState[0] = 1;
        var begin = CreateConstructionRecord(1, 0, 0, 0);
        var callSite = new byte[callSiteReturnsClassToContext ? 24 : 28];
        callSite[0] = 2;
        var callSitePayload = callSite.AsSpan(4);
        callSitePayload[0] = 1;
        if (callSiteReturnsClassToContext)
        {
            callSitePayload[1] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(callSitePayload[4..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(callSitePayload[6..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(callSitePayload[8..], 1);
            callSitePayload[16] = 2;
            callSitePayload[17] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(callSitePayload[18..], 2);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(callSitePayload[8..], 2);
            callSitePayload[16] = 2;
            callSitePayload[17] = 5;
            callSitePayload[20] = 2;
            callSitePayload[21] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(callSitePayload[22..],
                checked((ushort)callSiteArgumentHomeIndex));
        }
        var publish = CreateConstructionRecord(2,
            publishToRegister ? (byte)1 : (byte)2, 0,
            publishToRegister ? (ushort)0 : (ushort)1);
        var records = includeContextField
            ? new ReadOnlyMemory<byte>[] {
                entryState, begin, callSite,
                TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                    targetKind: (byte)contextFieldTargetKind,
                    accessKind: (byte)contextFieldAccessKind,
                    destinationKind: 1,
                    baseKind: (byte)contextFieldBaseKind,
                    destinationIndex: 0, baseIndex: 0,
                    indexKind: 0,
                    targetRefKind: (byte)contextFieldTargetRefKind,
                    indexIndex: 0, targetRef: contextFieldTargetRef,
                    referentTypeMapIndex: 1, Schema),
                publish,
            }
            : [entryState, begin, callSite, publish];
        if (switchOnConstructionContextHome)
        {
            var switchRecord = new byte[16];
            switchRecord[0] = 0x07;
            var switchPayload = switchRecord.AsSpan(4);
            switchPayload[0] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(switchPayload[2..], 0);
            BinaryPrimitives.WriteUInt16LittleEndian(switchPayload[4..], 0);
            BinaryPrimitives.WriteUInt16LittleEndian(switchPayload[6..], 0);
            records = [.. records, switchRecord];
        }
        if (includeConstructionContextStore)
        {
            var store = new byte[20];
            store[0] = 0x05;
            store[4] = 1;
            store[5] = 2;
            store[6] = 1;
            store[7] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(store.AsSpan(10), 2);
            records = [.. records, store];
        }
        if (includeConstructionAbort)
        {
            records = [.. records,
                CreateConstructionRecord(3, 0, 0, 0)];
        }
        if (callOrdinaryClassMethod && !compactOrdinaryClassCall)
        {
            var ordinaryCallSite = new byte[
                ordinaryMethodHasParameter ? 28 : 24];
            ordinaryCallSite[0] = 0x02;
            var ordinaryCallSitePayload = ordinaryCallSite.AsSpan(4);
            ordinaryCallSitePayload[0] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(
                ordinaryCallSitePayload[4..], 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                ordinaryCallSitePayload[8..],
                ordinaryMethodHasParameter ? 2u : 1u);
            ordinaryCallSitePayload[16] = 2;
            ordinaryCallSitePayload[17]
                = checked((byte)ordinaryReceiverSourceUse);
            BinaryPrimitives.WriteUInt16LittleEndian(
                ordinaryCallSitePayload[18..],
                checked((ushort)ordinaryReceiverHomeIndex));
            if (ordinaryMethodHasParameter)
            {
                ordinaryCallSitePayload[20] = 2;
                ordinaryCallSitePayload[21] = 1;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    ordinaryCallSitePayload[22..], 2);
            }
            records = [.. records, ordinaryCallSite];
        }
        return TrRmRegisterBodyWriter.Encode(homes, typeMap, methodMap,
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
            [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
            records, code,
            contractCount: 2, schema: Schema);
    }

    private static byte[] CreateConstructionRecord(byte action,
        byte destinationKind, ushort contextHome, ushort destination)
        => TrRmRegisterBodyRecordWriter.EncodeObjectConstruction(action,
            destinationKind, contextHome, destination, Schema);
}
