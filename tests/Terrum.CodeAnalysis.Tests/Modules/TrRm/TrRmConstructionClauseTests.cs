// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmConstructionClauseTests
{
    [Test]
    public void ReadsInitializerAndProjectionAndCanonicalizesMemberIds()
    {
        var result = Create([0x1400_0002, 0x1400_0001]);

        Assert.That(result.Initializer.RoleTag, Is.EqualTo(1));
        Assert.That(result.Initializer.InitializerMethodDefRow, Is.Zero);
        Assert.That(result.Initializer.InitializedMemberDefIds,
            Is.EqualTo(new uint[] { 0x1400_0001, 0x1400_0002 }));
        Assert.That(result.Projection.RoleTag, Is.EqualTo(2));
        Assert.That(result.Projection.InitializerMethodDefRow, Is.EqualTo(1));

        var rows = new TrRmVariableTableIndex(result.Section, 2, 8, 1);
        var initializerRow = rows.GetRow(1);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            initializerRow[16..]), Is.EqualTo(0x1400_0001));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            initializerRow[20..]), Is.EqualTo(0x1400_0002));
    }

    [Test]
    public void RejectsMalformedRolesDuplicateMembersAndStaticMembers()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            Create([0x1400_0001], roleTag: 3));
        _ = Assert.Throws<InvalidDataException>(() =>
            Create([0x1400_0001, 0x1400_0001]));
        _ = Assert.Throws<InvalidDataException>(() =>
            Create([0x1400_0001], firstFieldFlags: 0x08));
    }

    [Test]
    public void RequiresProjectionToNameAClassInitializerInTheSameArtifact()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            Create([0x1400_0001], includeInitializer: false));
        _ = Assert.Throws<InvalidDataException>(() =>
            Create([0x1400_0001], initializerMethodDefRow: 2));
    }

    [Test]
    public void AllowsBodylessConstructionExpectationsOnlyThroughImports()
    {
        var result = Create([0x1400_0001],
            importedExpectations: true);
        Assert.That(result.Initializer.RoleTag, Is.EqualTo(1));
        Assert.That(result.Projection.InitializerMethodDefRow, Is.EqualTo(1));

        _ = Assert.Throws<InvalidDataException>(() =>
            Create([0x1400_0001], bodylessLocal: true));
    }

    [Test]
    public void C1M1DoesNotAdmitTheSuccessorConstructionPayload()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            Create([0x1400_0001],
                schema: TrRmArtifactSchema.C1M1Draft));
    }

    [Test]
    public void RequiresClassHandleQualificationsToMatchDeclaredMutability()
    {
        var mutable = Create([0x1400_0001], mutableClass: true);
        Assert.That(mutable.Initializer.RoleTag, Is.EqualTo(1));
        _ = Assert.Throws<InvalidDataException>(() =>
            Create([0x1400_0001], mutableClass: true,
                classHandleQualifier: 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            Create([0x1400_0001], classHandleQualifier: 0x02));
    }

    private static ConstructionResult Create(uint[] memberDefIds,
        byte roleTag = 1, uint initializerMethodDefRow = 1,
        bool includeInitializer = true, uint firstFieldFlags = 0,
        bool importedExpectations = false, bool bodylessLocal = false,
        bool mutableClass = false, byte? classHandleQualifier = null,
        TrRmArtifactSchema schema =
            TrRmArtifactSchema.C1M1ConstructionDraft)
    {
        var contractRows = new List<TrRmContractRow>();
        if (includeInitializer)
        {
            contractRows.Add(new TrRmContractRow(0x1600_0001, 3,
                InitializerPayload(memberDefIds, roleTag)));
        }

        contractRows.Add(new TrRmContractRow(0x1600_0002, 3,
            ProjectionPayload(initializerMethodDefRow)));
        var names = TrRmStringTableWriter.EncodeTextRows([
            ("Module", 0), ("", 0), ("Widget", 0), ("First", 0),
            ("Second", 0), ("Initialize", 0), ("Create", 0),
            ("Provider", 0), ("InitializeKey", 0), ("CreateKey", 0),
        ]);
        var strings = new TrRmStringTableReader(names, 10);
        var moduleRefCount = importedExpectations ? 1 : 0;
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                0, 0, 1, 1, 1, 1),
        ], strings, moduleRefCount, typeDefCount: 1,
            methodDefCount: 2, sourceFileCount: 0, featureCount: 0);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            moduleRefCount, typeDefCount: 1, methodDefCount: 2,
            sourceFileCount: 0, featureCount: 0);
        var features = new TrRmFeatureTableReader([], 0, schema);
        var handleQualifier = classHandleQualifier
            ?? (mutableClass ? (byte)1 : (byte)0);
        ReadOnlyMemory<byte>[] typeSpecRows = [
            TrRmTypeSpecTableWriter.Reference(0x02, 1,
                handleQualifier),
        ];
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount: 1, importCount: 0, genericParamCount: 0,
            nativeFunctionPointerSignatureCount: 0, schema,
            methodDefCount: 2);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1,
            typeDefCount: 1, importCount: 0, genericParamCount: 0,
            nativeFunctionPointerSignatureCount: 0, schema,
            methodDefCount: 2);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(2, 3, mutableClass ? 0x8Au : 0x0Au, 1, 2,
                0, 0, 0, 0, 0, 0),
        ], strings, modules, fieldCount: 2, genericParamCount: 0,
            layoutCount: 0, contractCount: contractRows.Count, schema);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 1, strings,
            modules, fieldCount: 2, genericParamCount: 0, layoutCount: 0,
            contractCount: contractRows.Count, schema);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 4, 0xF200_0008, firstFieldFlags),
            new TrRmFieldDefRow(1, 5, 0xF200_0008, 0),
        ], strings, types, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 2, strings,
            types, typeSpecs);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0000, 1, 0, 0, 0, 0),
            new TrRmMethodSigRow(1, 0, 0, 0, 0, 0),
        ], typeSpecs, paramCount: 0, genericParamCount: 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 2,
            typeSpecs, paramCount: 0, genericParamCount: 0);
        var methodRows = includeInitializer
            ? new[] {
                new TrRmMethodDefRow(1, 6, 1, 0, 1, 1,
                    importedExpectations || bodylessLocal ? 0 : 1),
                new TrRmMethodDefRow(1, 7, 2, 0, 2, 1,
                    importedExpectations || bodylessLocal ? 0 : 2),
            }
            : new[] {
                new TrRmMethodDefRow(1, 6, 1, 0, 0, 0,
                    importedExpectations || bodylessLocal ? 0 : 1),
                new TrRmMethodDefRow(1, 7, 2, 0, 1, 1,
                    importedExpectations || bodylessLocal ? 0 : 2),
            };
        var bodyCount = importedExpectations || bodylessLocal ? 0 : 2;
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows,
            strings, modules, types, signatures, contractRows.Count,
            bodyCount);
        var methods = new TrRmMethodDefTableReader(methodBytes, 2, strings,
            modules, types, signatures, contractRows.Count, bodyCount);
        var imports = default(TrRmImportTableReader);
        if (importedExpectations)
        {
            var moduleRefsBytes = TrRmModuleRefTableWriter.Encode([
                new TrRmModuleRefRow(8,
                    TrRmModuleTableReader
                        .WindowsX64NativeTargetRequirement, 0),
            ], strings, modules);
            var moduleRefs = new TrRmModuleRefTableReader(
                moduleRefsBytes, 1, strings, modules);
            var importsBytes = TrRmImportTableWriter.Encode([
                new TrRmImportRow(1, 9, 0x1600_0001),
                new TrRmImportRow(1, 10, 0x1600_0002),
            ], strings, moduleRefs, types, methods, default, fields: fields);
            imports = new TrRmImportTableReader(importsBytes, 2, strings,
                moduleRefs, types, methods, default, fields: fields);
        }
        var section = TrRmContractTableWriter.EncodeC1M1ConstructionDraft(
            contractRows, types, methods, signatures, default, typeSpecs,
            fields, default, imports, features, modules,
            predicateGraphCount: 0);
        var contracts = new TrRmContractTableReader(section,
            contractRows.Count, types, methods, signatures, default,
            typeSpecs, fieldPathCount: 0, features, modules, schema,
            predicateGraphCount: 0, fields: fields, imports: imports);
        var initializer = includeInitializer
            ? contracts.GetConstructionClause(1) : default;
        var projection = contracts.GetConstructionClause(2);
        return new ConstructionResult(section, initializer, projection);
    }

    private static byte[] InitializerPayload(uint[] memberDefIds,
        byte roleTag)
    {
        var payload = new byte[8 + (memberDefIds.Length * sizeof(uint))];
        payload[0] = roleTag;
        Write(payload, 4, checked((uint)memberDefIds.Length));
        for (var index = 0; index < memberDefIds.Length; index++)
        {
            Write(payload, 8 + (index * sizeof(uint)), memberDefIds[index]);
        }

        return payload;
    }

    private static byte[] ProjectionPayload(uint initializerMethodDefRow)
    {
        var payload = new byte[8];
        payload[0] = 2;
        Write(payload, 4, initializerMethodDefRow);
        return payload;
    }

    private static void Write(Span<byte> bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);

    private readonly record struct ConstructionResult(byte[] Section,
        TrRmConstructionClauseContract Initializer,
        TrRmConstructionClauseContract Projection);
}
