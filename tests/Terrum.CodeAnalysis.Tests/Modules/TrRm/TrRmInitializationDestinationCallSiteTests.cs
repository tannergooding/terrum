using System;
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmInitializationDestinationCallSiteTests
{
    [TestCase(3, 3, 5, true)]
    [TestCase(5, 5, 5, true)]
    [TestCase(2, 2, 1, true)]
    [TestCase(3, 3, 1, false)]
    [TestCase(2, 2, 5, false)]
    [TestCase(3, 2, 5, false)]
    [TestCase(5, 5, 2, false)]
    [TestCase(7, 7, 5, false)]
    [TestCase(9, 9, 5, false)]
    public void CallSiteSourceUseMatchesInitializationDestinationFormal(
        int formalTypeSpecRef, int actualTypeSpecRef, byte sourceUse,
        bool isValid)
    {
        var artifact = CreateCallSiteArtifact((uint)formalTypeSpecRef,
            (uint)actualTypeSpecRef, sourceUse);

        if (isValid)
        {
            ValidateRegisterBodyMaps(artifact);
        }
        else
        {
            _ = Assert.Throws<InvalidDataException>(
                () => ValidateRegisterBodyMaps(artifact));
        }
    }

    [Test]
    public void CallSiteSourceUseFiveRejectsAPlaceHandle()
    {
        var artifact = CreateCallSiteArtifact(3, 2, 5,
            usePlaceHandle: true);

        _ = Assert.Throws<InvalidDataException>(
            () => ValidateRegisterBodyMaps(artifact));
    }

    [Test]
    public void StandardDDFormsTypedInitializationReferenceForCallSite()
    {
        var artifact = CreateCallSiteArtifact(3, 2, 5,
            formInitializationReference: true);

        ValidateRegisterBodyMaps(artifact);
    }

    [Test]
    public void StandardDDRetainsWriteAuthorityOnlyForRebindableHome()
    {
        var writable = CreateCallSiteArtifact(6, 2, 5,
            formInitializationReference: true,
            placeSelectionFlags: 3, localHomeFlags: 1);
        ValidateRegisterBodyMaps(writable);

        var notRebindable = CreateCallSiteArtifact(6, 2, 5,
            formInitializationReference: true,
            placeSelectionFlags: 3);
        _ = Assert.Throws<InvalidDataException>(
            () => ValidateRegisterBodyMaps(notRebindable));
    }

    [Test]
    public void InitializationPlaceSelectionFlagsCannotAuthorizePlaceLoad()
    {
        var homeMap = new byte[8];
        homeMap[0] = 3;
        var typeMap = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(typeMap, 0xF200_0008);
        byte[] entryState = [1, 0, 0, 0, 0, 0, 0, 0];
        var selection = TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
            1, 3, 1, 0, 0, 0, 0, 0, 0, 0, 0,
            TrRmArtifactSchema.Standard, flags: 1);
        ReadOnlyMemory<byte>[] records = [
            new(entryState), new(selection),
        ];

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmRegisterBodyWriter.Encode(homeMap, typeMap, default,
                default, default,
                [new TrRmRegisterBodyBlock(0, 6, 0)], records,
                new byte[] { 0xD8, 0, 1, 0, 0x18, 0 }));
    }

    [TestCase(3, 2, 1, 1, true)]
    [TestCase(6, 2, 2, 1, true)]
    [TestCase(3, 2, 2, 1, false)]
    [TestCase(3, 3, 1, 1, false)]
    public void SemanticStoreValidatesInitializationReferenceTargetAndValue(
        int placeTypeSpecRef, int valueTypeSpecRef, byte transition,
        byte sourceUse, bool isValid)
    {
        var artifact = CreateInitializationStoreArtifact(
            (uint)placeTypeSpecRef, (uint)valueTypeSpecRef, transition,
            sourceUse);

        if (isValid)
        {
            ValidateRegisterBodyMaps(artifact, methodDefRow: 1);
        }
        else
        {
            _ = Assert.Throws<InvalidDataException>(() =>
                ValidateRegisterBodyMaps(artifact, methodDefRow: 1));
        }
    }

    private static void ValidateRegisterBodyMaps(byte[] artifact,
        int methodDefRow = 2)
    {
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(methodDefRow);
    }

    private static byte[] CreateInitializationStoreArtifact(
        uint placeTypeSpecRef, uint valueTypeSpecRef, byte transition,
        byte sourceUse)
    {
        ReadOnlyMemory<byte>[] typeSpecRows = [
            new(TrRmTypeSpecTableWriter.Builtin(0)),
            new(TrRmTypeSpecTableWriter.Builtin(8)),
            new(TrRmTypeSpecTableWriter.InitializationReference(
                0xF200_0008)),
            new(TrRmTypeSpecTableWriter.Builtin(8, 0x04)),
            new(TrRmTypeSpecTableWriter.IndexedFamily(0x8A, 4)),
            new(TrRmTypeSpecTableWriter.InitializationReference(
                0xF200_0008, isWritable: true)),
        ];
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length, 0, 0, 0);
        var typeMap = TrRmRegisterBodyMapBuilder.Create([
            0x1100_0000u | placeTypeSpecRef,
            0x1100_0000u | valueTypeSpecRef,
        ], []);

        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("Fill", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 1, 0, 0);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0000, 0, 1, 2, 0, 0),
        ], typeSpecs, 2, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 2, 0);
        var parameterBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, placeTypeSpecRef, 0),
            new TrRmParamRow(1, 1, valueTypeSpecRef, 0),
        ], signatures, typeSpecs);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 2, 0, 0, 1),
        ], strings, modules, typeDefs, signatures, 0, 1);

        var homeMap = new byte[16];
        homeMap[0] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(2),
            typeMap.GetTypeMapIndex(0x1100_0000u | placeTypeSpecRef));
        BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(4), 0);
        homeMap[8] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(10),
            typeMap.GetTypeMapIndex(0x1100_0000u | valueTypeSpecRef));
        BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(12), 1);
        byte[] entryState = [1, 0, 0, 0, 0, 0, 0, 0];
        var store = TrRmRegisterBodyRecordWriter.EncodeSemanticStore(2, 2,
            transition, sourceUse, 0, 1, TrRmArtifactSchema.Standard);
        var body = TrRmRegisterBodyWriter.Encode(homeMap, typeMap.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0, 4, 0)],
            [new ReadOnlyMemory<byte>(entryState),
                new ReadOnlyMemory<byte>(store)],
            new byte[] { 0x92, 1, 0x18, 0 });

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 3, stringBytes),
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Length,
                typeSpecBytes),
            new TrRmSection(0x0016, 0, 1, methodBytes),
            new TrRmSection(0x0017, 0, 1, signatureBytes),
            new TrRmSection(0x0018, 0, 2, parameterBytes),
            new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies(
                    [new ReadOnlyMemory<byte>(body)])),
        ]);
    }

    private static byte[] CreateCallSiteArtifact(uint formalTypeSpecRef,
        uint actualTypeSpecRef, byte sourceUse,
        bool usePlaceHandle = false,
        bool formInitializationReference = false,
        ushort placeSelectionFlags = 1, byte localHomeFlags = 0)
    {
        if (usePlaceHandle && formInitializationReference)
        {
            throw new ArgumentException(
                "The artifact cannot use two incompatible call inputs.");
        }

        ReadOnlyMemory<byte>[] typeSpecRows = [
            new(TrRmTypeSpecTableWriter.Builtin(0)),
            new(TrRmTypeSpecTableWriter.Builtin(8)),
            new(TrRmTypeSpecTableWriter.InitializationReference(
                0xF200_0008)),
            new(TrRmTypeSpecTableWriter.Builtin(8, 0x04)),
            new(TrRmTypeSpecTableWriter.IndexedFamily(0x8A, 4)),
            new(TrRmTypeSpecTableWriter.InitializationReference(
                0xF200_0008, isWritable: true)),
            new(TrRmTypeSpecTableWriter.InitializationReference(
                0xF200_0000)),
            new(TrRmTypeSpecTableWriter.Builtin(0, 0x04)),
            new(TrRmTypeSpecTableWriter.IndexedFamily(0x8A, 8)),
        ];
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length, 0, 0, 0);

        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("Initialize", 0), ("Forward", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 4);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 0, 2, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 2, 0, 0);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0000, 0, 1, 1, 0, 0),
            new TrRmMethodSigRow(0xF200_0000, 0,
                usePlaceHandle || formInitializationReference ? 0 : 2,
                usePlaceHandle || formInitializationReference ? 0 : 1,
                0, 0),
        ], typeSpecs,
            usePlaceHandle || formInitializationReference ? 1 : 2, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 2,
            typeSpecs,
            usePlaceHandle || formInitializationReference ? 1 : 2, 0);
        TrRmParamRow[] parameterRows = [
            new TrRmParamRow(1, 0, formalTypeSpecRef, 0),
        ];
        if (!usePlaceHandle && !formInitializationReference)
        {
            parameterRows = [
                .. parameterRows,
                new TrRmParamRow(2, 0, actualTypeSpecRef, 0),
            ];
        }
        var parameterBytes = TrRmParamTableWriter.Encode(parameterRows,
            signatures, typeSpecs);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 2, 0, 0, 1),
            new TrRmMethodDefRow(0, 4, 2, 2, 0, 0, 2),
        ], strings, modules, typeDefs, signatures, 0, 2);

        byte[] entryState = [1, 0, 0, 0, 0, 0, 0, 0];
        var targetBody = TrRmRegisterBodyWriter.Encode(default, default,
            default, default, default,
            [new TrRmRegisterBodyBlock(0, 2, 0)],
            [new ReadOnlyMemory<byte>(entryState)], new byte[] { 0x18, 0 });

        byte[] homeMap;
        ReadOnlyMemory<byte> callerEntryState;
        if (usePlaceHandle)
        {
            homeMap = [];
            var placeEntryState = new byte[13];
            placeEntryState[0] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(
                placeEntryState.AsSpan(4), 1);
            placeEntryState[9] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(
                placeEntryState.AsSpan(10), 0);
            placeEntryState[12] = 3;
            callerEntryState = placeEntryState;
        }
        else if (formInitializationReference)
        {
            homeMap = new byte[8];
            homeMap[0] = 3;
            homeMap[1] = localHomeFlags;
            BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(2), 0);
            callerEntryState = entryState;
        }
        else
        {
            homeMap = new byte[8];
            homeMap[0] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(2), 0);
            callerEntryState = entryState;
        }

        var typeMap = new byte[
            formInitializationReference ? 2 * sizeof(uint) : sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(typeMap,
            0x1100_0000u | actualTypeSpecRef);
        if (formInitializationReference)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                typeMap.AsSpan(sizeof(uint)),
                0x1100_0000u | formalTypeSpecRef);
        }
        var methodMap = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(methodMap, 0x1600_0001);
        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 0, 0,
            0, [new TrRmRegisterBodyCallInput(
                usePlaceHandle ? (byte)1 : formInitializationReference
                    ? (byte)1 : (byte)2, sourceUse, 0)], []);
        var placeSelection = formInitializationReference
            ? TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                1, 3, 1, 0, 0, 0, 0, 0, 0, 0, 0,
                TrRmArtifactSchema.Standard,
                flags: placeSelectionFlags)
            : null;
        ReadOnlyMemory<byte>[] callerRecords = formInitializationReference
            ? [callerEntryState, new ReadOnlyMemory<byte>(placeSelection!),
                new ReadOnlyMemory<byte>(callSite)]
            : [callerEntryState, new ReadOnlyMemory<byte>(callSite)];
        var callerCode = formInitializationReference
            ? new byte[] { 0xDD, 0, 1, 0, 0x90, 2, 0x18, 0 }
            : new byte[] { 0x90, 1, 0x18, 0 };
        var callerBody = TrRmRegisterBodyWriter.Encode(homeMap, typeMap,
            methodMap, default, default,
            [new TrRmRegisterBodyBlock(0, (uint)callerCode.Length, 0)],
            callerRecords, callerCode);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 4, stringBytes),
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Length,
                typeSpecBytes),
            new TrRmSection(0x0016, 0, 2, methodBytes),
            new TrRmSection(0x0017, 0, 2, signatureBytes),
            new TrRmSection(0x0018, 0, (uint)parameterRows.Length,
                parameterBytes),
            new TrRmSection(0x0020, 0, 2,
                TrRmVariableTableWriter.EncodeBodies(
                    [new ReadOnlyMemory<byte>(targetBody),
                        new ReadOnlyMemory<byte>(callerBody)])),
        ]);
    }
}
