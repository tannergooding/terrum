// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmConstructedTypeSpecBindingTests
{
    [Test]
    public void BindsLocalAndImportedGenericTypeDefinitionsAndAllowsAcyclicForwardArguments()
    {
        TrRmStructuralMetadataValidator.Validate(CreateArtifact(
            formalKind: 1, [4]));
        TrRmStructuralMetadataValidator.Validate(CreateArtifact(
            formalKind: 1, [4], importedGenericType: true));
    }

    [Test]
    public void RejectsWrongArityAndTypeSpecReferencesToConstantFormals()
    {
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateArtifact(
                formalKind: 1, [4, 4])));

        var rows = ConstructedRows(formalKind: 2, [1]);
        rows[2] = TrRmTypeSpecTableWriter.Reference(0x04, 1);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateArtifact(
                formalKind: 2, [1], typeSpecRows: rows, constantSourceCount: 1)));
    }

    [Test]
    public void ChecksConstantArgumentAndDefaultSourceBounds()
    {
        TrRmStructuralMetadataValidator.Validate(CreateArtifact(
            formalKind: 2, [1], constantSourceCount: 1,
            constantTypeSpecRef: 2, defaultArgumentRef: 1));

        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateArtifact(
                formalKind: 2, [2], constantSourceCount: 1,
                constantTypeSpecRef: 2)));

        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateArtifact(
                formalKind: 2, [1], constantSourceCount: 1,
                constantTypeSpecRef: 2, defaultArgumentRef: 2)));
    }

    [Test]
    public void RejectsImportedGenericReferencesWithNonTypeExpectations()
    {
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateArtifact(
                formalKind: 1, [4], importedGenericType: true,
                importExpectedDefId: 0x1600_0001, includeMethodImport: true)));
    }

    [Test]
    public void RejectsCyclesInConstructedTypeArguments()
    {
        var rows = ConstructedRows(formalKind: 1, [4]);
        rows[3] = TrRmTypeSpecTableWriter.Wrapped(0x06, 3);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateArtifact(
                formalKind: 1, [4], typeSpecRows: rows)));
    }

    private static ReadOnlyMemory<byte>[] ConstructedRows(byte formalKind, uint[] arguments)
    {
        var root = TrRmTypeSpecTableWriter.Reference(0x02, 1);
        var formal = formalKind == 1
            ? TrRmTypeSpecTableWriter.Reference(0x04, 1)
            : TrRmTypeSpecTableWriter.Builtin(8);
        return [
            root,
            formal,
            TrRmTypeSpecTableWriter.Constructed(1, arguments),
            TrRmTypeSpecTableWriter.Builtin(9),
        ];
    }

    private static byte[] CreateArtifact(byte formalKind, uint[] arguments,
        bool importedGenericType = false, uint importExpectedDefId = 0x1300_0001,
        bool includeMethodImport = false, int constantSourceCount = 0,
        uint constantTypeSpecRef = 0, uint defaultArgumentRef = 0,
        ReadOnlyMemory<byte>[]? typeSpecRows = null)
    {
        if (typeSpecRows is null)
        {
            typeSpecRows = ConstructedRows(formalKind, arguments);
            if (importedGenericType)
            {
                typeSpecRows[0] = TrRmTypeSpecTableWriter.Reference(0x03, 1);
            }
        }
        var strings = TrRmStringTableWriter.EncodeNames([
            ("Module", 0), ("Provider", 0), ("Example", 0), ("T", 0),
            ("Method", 0), ("GenericKey", 0),
        ]);
        var stringReader = new TrRmStringTableReader(strings, 6);
        var moduleRefCount = importedGenericType ? 1 : 0;
        var methodCount = includeMethodImport ? 1 : 0;
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], stringReader, moduleRefCount, 1, methodCount, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, stringReader,
            moduleRefCount, 1, methodCount, 0);
        var moduleRefBytes = importedGenericType
            ? TrRmModuleRefTableWriter.Encode([
                new TrRmModuleRefRow(2, 1, 0),
            ], stringReader, modules)
            : [];
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            1, moduleRefCount, 1);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length, 1, moduleRefCount, 1);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(3, 4, 6,
                0, 0, 1, 1, 1, 1, 0, 0),
        ], stringReader, modules, 0, 1, 1, 0);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 1,
            stringReader, modules, 0, 1, 1, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var signatureBytes = includeMethodImport
            ? TrRmMethodSigTableWriter.Encode([
                new TrRmMethodSigRow(0xF200_0008, 0, 0, 0, 0, 0),
            ], typeSpecs, 0, 1)
            : [];
        var signatures = includeMethodImport
            ? new TrRmMethodSigTableReader(signatureBytes, 1, typeSpecs, 0, 1)
            : default;
        var methodBytes = includeMethodImport
            ? TrRmMethodDefTableWriter.Encode([
                new TrRmMethodDefRow(0, 5, 1, 0, 0, 0, 0),
            ], stringReader, modules, types, signatures, 0, 0)
            : [];
        var methods = new TrRmMethodDefTableReader(methodBytes, methodCount,
            stringReader, modules, types, signatures, 0, 0);
        var genericParamBytes = formalKind == 2
            && defaultArgumentRef > (uint)constantSourceCount
                ? EncodeConstantFormalWithoutValidation(constantTypeSpecRef, defaultArgumentRef)
                : TrRmGenericParamTableWriter.Encode([
                    new TrRmGenericParamRow(0x1300_0001, 0, 4, 0, 0,
                        formalKind, formalKind == 1 ? (byte)1 : (byte)0,
                        formalKind == 1 ? (byte)1 : (byte)0,
                        constantTypeSpecRef, defaultArgumentRef),
                ], stringReader, types, methods, signatures, typeSpecs, 0,
                    constantSourceCount);
        var importBytes = importedGenericType
            ? TrRmImportTableWriter.Encode([
                new TrRmImportRow(1, 6, importExpectedDefId),
            ], stringReader,
                new TrRmModuleRefTableReader(moduleRefBytes, 1, stringReader, modules),
                types, methods, default)
            : [];
        var constantSourceBytes = new byte[constantSourceCount * 8];
        var blobBytes = constantSourceCount == 0 ? []
            : TrRmVariableTableWriter.EncodeBlobs([new byte[] { 0, 0, 0, 0 }]);
        var frozenValueNodeBytes = new byte[constantSourceCount == 0 ? 0 : 24];
        if (constantSourceCount != 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(frozenValueNodeBytes, 2);
            frozenValueNodeBytes[4] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(
                frozenValueNodeBytes.AsSpan(8), 1);
        }
        for (var row = 0; row < constantSourceCount; row++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantSourceBytes.AsSpan(row * 8), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantSourceBytes.AsSpan(row * 8 + 4), 1);
        }

        var sections = new System.Collections.Generic.List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 6, strings),
        };
        if (constantSourceCount != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0, 1, blobBytes));
        }
        if (importedGenericType)
        {
            sections.Add(new TrRmSection(0x0010, 0, 1, moduleRefBytes));
        }
        sections.Add(new TrRmSection(0x0011, 0,
            (uint)typeSpecRows.Length, typeSpecBytes));
        sections.Add(new TrRmSection(0x0013, 0, 1, typeDefBytes));
        sections.Add(new TrRmSection(0x0015, 0, 1, layoutBytes));
        if (includeMethodImport)
        {
            sections.Add(new TrRmSection(0x0016, 0, 1, methodBytes));
            sections.Add(new TrRmSection(0x0017, 0, 1, signatureBytes));
        }
        sections.Add(new TrRmSection(0x0019, 0, 1, genericParamBytes));
        if (importedGenericType)
        {
            sections.Add(new TrRmSection(0x001C, 0, 1, importBytes));
        }
        if (constantSourceCount != 0)
        {
            sections.Add(new TrRmSection(0x0030, 0,
                (uint)constantSourceCount, constantSourceBytes));
            sections.Add(new TrRmSection(0x003F, 0, 1, frozenValueNodeBytes));
        }

        return TrRmArtifactWriter.Encode(sections);
    }

    private static byte[] EncodeConstantFormalWithoutValidation(
        uint constantTypeSpecRef, uint defaultArgumentRef)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x1300_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 4);
        bytes[20] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), constantTypeSpecRef);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), defaultArgumentRef);
        return bytes;
    }
}
