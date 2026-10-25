// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmAssociatedTypeImportExportTests
{
    [Test]
    public void BindsExplicitAssociatedMemberAndOwnerImportsToPublicProviderRows()
    {
        var fixture = CreateFixture();
        var imports = new[]
        {
            new TrRmImportRow(1, 6, 0x1300_0002),
            new TrRmImportRow(1, 7, 0x2000_0002),
        };
        var exports = new[]
        {
            new TrRmExportRow(0x1300_0001, 6),
            new TrRmExportRow(0x2000_0001, 7),
        };

        Assert.That(Read(fixture, imports, exports, writeRows: true), Is.True);
    }

    [Test]
    public void ValidatesDirectAndImportedAssociatedRelationReferences()
    {
        var fixture = CreateFixture();
        Assert.That(Read(fixture, [], []), Is.False);

        var imports = new[]
        {
            new TrRmImportRow(1, 6, 0x1300_0002),
            new TrRmImportRow(1, 7, 0x2000_0002),
        };
        Assert.That(Read(fixture, imports, []), Is.True);
    }

    [Test]
    public void BindsImportedWitnessAssociatedTypeIdentityToItsExpectedStub()
    {
        var fixture = CreateFixture(defaultTypeSpecRef: 1);
        var strings = new TrRmStringTableReader(fixture.Strings, 7);
        var modules = new TrRmModuleTableReader(fixture.Modules, 2, strings,
            1, 2, 0, 0);
        var moduleRefs = new TrRmModuleRefTableReader(fixture.ModuleRefs, 1,
            strings, modules);
        var types = new TrRmTypeDefTableReader(fixture.TypeDefs, 2,
            strings, modules, 0, 0, 2, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(
            fixture.TypeSpecs, 2, 2, 2, 0, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var generics = new TrRmGenericParamTableReader([], 0,
            strings, types, methods, signatures, typeSpecs, 0);
        var interfaces = new TrRmInterfaceDefTableReader(fixture.InterfaceDefs, 2,
            types, generics, 0, 0, 2, 0, 0);
        var associatedTypeDefs = new TrRmAssociatedTypeDefTableReader(
            fixture.AssociatedTypeDefs, 2, strings, typeSpecs, interfaces);
        var importRows = new[]
        {
            new TrRmImportRow(1, 6, 0x1300_0002),
            new TrRmImportRow(1, 7, 0x2000_0002),
        };
        var importBytes = TrRmImportTableWriter.Encode(importRows, strings,
            moduleRefs, types, methods, default, interfaces, associatedTypeDefs);
        var imports = new TrRmImportTableReader(importBytes, importRows.Length,
            strings, moduleRefs, types, methods, default, interfaces, associatedTypeDefs);
        var interfaceBases = new TrRmInterfaceBaseTableReader([], 0,
            typeSpecs, interfaces);
        var witnessBytes = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(1, 2, 0, 0, 1, 1, 0, 0),
        ], typeSpecs, 0, 1, 0);
        var witnesses = new TrRmInterfaceWitnessTableReader(
            witnessBytes, 1, typeSpecs, 0, 1, 0);
        var associatedBindingBytes = TrRmWitnessAssociatedTypeTableWriter.Encode([
            new TrRmWitnessAssociatedTypeRow(1, 2, 0x1C00_0002, 1, 2),
        ], witnesses, typeSpecs, types, interfaces, interfaceBases,
            associatedTypeDefs, imports);

        Assert.That(Read(fixture, importRows, [], interfaceWitnessBytes: witnessBytes,
            witnessAssociatedTypeBytes: associatedBindingBytes), Is.True);
    }

    [Test]
    public void StructuralValidatorRejectsMalformedAssociatedRelations()
    {
        var fixture = CreateFixture();
        var malformed = (byte[])fixture.RelationBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(4), 0x1400_0001);
        Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [], [], associatedRelationBytes: malformed));

        malformed = (byte[])fixture.RelationBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(8), 0xF400_0008);
        Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [], [], associatedRelationBytes: malformed));

        malformed = (byte[])fixture.RelationBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(4), 0x2000_0003);
        Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [], [], associatedRelationBytes: malformed));

        malformed = (byte[])fixture.RelationBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed, 0xF200_0008);
        Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [], [], associatedRelationBytes: malformed));

        malformed = (byte[])fixture.RelationBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed, 0xF300_8B08);
        Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [], [], associatedRelationBytes: malformed));
    }

    [Test]
    public void RejectsAssociatedImportsWithoutTheirExpectedOwnerOrInWrongModule()
    {
        var fixture = CreateFixture();
        var exports = new[] { new TrRmExportRow(0x2000_0001, 7) };
        Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [new TrRmImportRow(1, 7, 0x2000_0002)], exports));
        Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [
                new TrRmImportRow(1, 6, 0x1300_0002),
                new TrRmImportRow(1, 7, 0x2000_0001),
            ], exports));
        Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [
                new TrRmImportRow(1, 6, 0x1300_0002),
                new TrRmImportRow(1, 7, 0x2000_0003),
            ], exports));
    }

    [Test]
    public void RejectsExpectedStubsAndMembersOfImportedOwnersAsExports()
    {
        var fixture = CreateFixture();
        var imports = new[]
        {
            new TrRmImportRow(1, 6, 0x1300_0002),
            new TrRmImportRow(1, 7, 0x2000_0002),
        };
        Assert.Throws<InvalidDataException>(() =>
            Read(fixture, imports, [new TrRmExportRow(0x2000_0002, 7)]));
        Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [new TrRmImportRow(1, 6, 0x1300_0002)],
                [new TrRmExportRow(0x2000_0002, 7)]));
    }

    [Test]
    public void RejectsAssociatedExportsWithNonpublicInterfaceOwners()
    {
        var fixture = CreateFixture(providerTypeFlags: 13);

        Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [], [new TrRmExportRow(0x2000_0001, 7)]));
    }

    private static bool Read(Fixture fixture,
        IReadOnlyList<TrRmImportRow> importRows,
        IReadOnlyList<TrRmExportRow> exportRows, bool writeRows = false,
        byte[]? associatedRelationBytes = null,
        byte[]? interfaceWitnessBytes = null,
        byte[]? witnessAssociatedTypeBytes = null)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 7);
        var modules = new TrRmModuleTableReader(fixture.Modules, 2, strings,
            1, 2, 0, 0);
        var moduleRefs = new TrRmModuleRefTableReader(fixture.ModuleRefs, 1,
            strings, modules);
        var types = new TrRmTypeDefTableReader(fixture.TypeDefs, 2,
            strings, modules, 0, 0, 2, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layouts = new TrRmLayoutTableReader(fixture.Layouts, 2,
            types, targetProfiles, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(
            fixture.TypeSpecs, 2, 2, 0, 0, 0);
        var generics = new TrRmGenericParamTableReader([], 0,
            strings, types, methods, signatures, typeSpecs, 0);
        var interfaces = new TrRmInterfaceDefTableReader(fixture.InterfaceDefs, 2,
            types, generics, 0, 0, 2, 0, 0);
        var associatedTypeDefs = new TrRmAssociatedTypeDefTableReader(
            fixture.AssociatedTypeDefs, 2, strings, typeSpecs, interfaces);

        var importBytes = writeRows
            ? TrRmImportTableWriter.Encode(importRows, strings, moduleRefs,
                types, methods, default, interfaces, associatedTypeDefs)
            : EncodeImportRows(importRows);
        var imports = new TrRmImportTableReader(importBytes, importRows.Count,
            strings, moduleRefs, types, methods, default, interfaces, associatedTypeDefs);
        var relations = associatedRelationBytes ?? TrRmAssociatedRelationTableWriter.Encode([
            new TrRmAssociatedRelationRow(importRows.Count > 1 ? 2u : 1u,
                importRows.Count > 1 ? 0x1C00_0002u : 0x2000_0001u, 1),
        ], typeSpecs, 2, imports);
        var exportBytes = writeRows
            ? TrRmExportTableWriter.Encode(exportRows, strings, types, methods,
                default, imports, interfaces, associatedTypeDefs)
            : EncodeExportRows(exportRows);
        _ = new TrRmExportTableReader(exportBytes, exportRows.Count,
            strings, types, methods, default, imports, interfaces, associatedTypeDefs);

        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 2, fixture.Modules),
            new(0x0002, 0, 7, fixture.Strings),
            new(0x0010, 0, 1, fixture.ModuleRefs),
            new(0x0011, 0, 2, fixture.TypeSpecs),
            new(0x0013, 0, 2, fixture.TypeDefs),
            new(0x0015, 0, 2, fixture.Layouts),
            new(0x0025, 0, 2, fixture.InterfaceDefs),
            new(0x0027, 0, 2, fixture.AssociatedTypeDefs),
            new(0x0036, 0, 1, relations),
        };
        if (importBytes.Length != 0)
        {
            sections.Insert(6,
                new TrRmSection(0x001C, 0, (uint)importRows.Count, importBytes));
        }
        if (exportBytes.Length != 0)
        {
            sections.Insert(importBytes.Length == 0 ? 6 : 7,
                new TrRmSection(0x001D, 0, (uint)exportRows.Count, exportBytes));
        }
        if (interfaceWitnessBytes is { Length: > 0 })
        {
            sections.Insert(sections.Count - 1, new TrRmSection(0x0028, 0,
                (uint)(interfaceWitnessBytes.Length / 32), interfaceWitnessBytes));
        }
        if (witnessAssociatedTypeBytes is { Length: > 0 })
        {
            sections.Insert(sections.Count - 1, new TrRmSection(0x002A, 0,
                (uint)(witnessAssociatedTypeBytes.Length / 20), witnessAssociatedTypeBytes));
        }
        TrRmStructuralMetadataValidator.Validate(TrRmArtifactWriter.Encode(sections));
        return imports.IsExpectedDefinition(0x2000_0002);
    }

    private static Fixture CreateFixture(uint providerTypeFlags = 14,
        uint defaultTypeSpecRef = 0)
    {
        var stringBytes = TrRmStringTableWriter.EncodeNames([
            ("Provider", 0), ("Consumer", 0), ("Example", 0), ("IExample", 0),
            ("Item", 0), ("IExampleKey", 0), ("ItemKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 7);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
            new TrRmModuleRow(2, 1, 0, 0, 1, 2, 1, 1),
        ], strings, 1, 2, 0, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, strings,
            1, 2, 0, 0, 0);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(1, 1, 0),
        ], strings, modules);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(3, 4, providerTypeFlags,
                0, 0, 0, 0, 1, 1, 0, 0),
            new TrRmTypeDefRow(3, 4, 14,
                0, 0, 0, 0, 2, 1, 0, 0),
        ], strings, modules, 0, 0, 2, 0);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 2,
            strings, modules, 0, 0, 2, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 2),
        ], 2, 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 2, 2, 0, 0, 0);
        var generics = new TrRmGenericParamTableReader([], 0,
            strings, types, methods, signatures, typeSpecs, 0);
        var interfaceDefBytes = TrRmInterfaceDefTableWriter.Encode([
            new TrRmInterfaceDefRow(1, 0, 0, 0, 0, 0,
                1, 1, 0, 0, 0, 0),
            new TrRmInterfaceDefRow(2, 0, 0, 0, 0, 0,
                2, 1, 0, 0, 0, 0),
        ], types, generics, 0, 0, 2, 0, 0);
        var interfaces = new TrRmInterfaceDefTableReader(interfaceDefBytes, 2,
            types, generics, 0, 0, 2, 0, 0);
        var associatedTypeDefBytes = TrRmAssociatedTypeDefTableWriter.Encode([
            new TrRmAssociatedTypeDefRow(1, 5, defaultTypeSpecRef),
            new TrRmAssociatedTypeDefRow(2, 5, defaultTypeSpecRef),
        ], strings, typeSpecs, interfaces);
        var relationBytes = TrRmAssociatedRelationTableWriter.Encode([
            new TrRmAssociatedRelationRow(1, 0x2000_0001, 1),
        ], typeSpecs, 2, default);

        return new Fixture(stringBytes, moduleBytes, moduleRefBytes,
            typeSpecBytes, typeDefBytes, layoutBytes, interfaceDefBytes,
            associatedTypeDefBytes, relationBytes);
    }

    private static byte[] EncodeImportRows(IReadOnlyList<TrRmImportRow> rows)
    {
        var result = new byte[rows.Count * 12];
        for (var row = 0; row < rows.Count; row++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(row * 12),
                unchecked((uint)rows[row].ModuleRefRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(row * 12 + 4), unchecked((uint)rows[row].ExportKeyStringRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(row * 12 + 8), rows[row].ExpectedLocalDefId);
        }

        return result;
    }

    private static byte[] EncodeExportRows(IReadOnlyList<TrRmExportRow> rows)
    {
        var result = new byte[rows.Count * 8];
        for (var row = 0; row < rows.Count; row++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(row * 8), rows[row].LocalDefId);
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(row * 8 + 4), unchecked((uint)rows[row].ExportKeyStringRow));
        }

        return result;
    }

    private sealed record Fixture(
        byte[] Strings,
        byte[] Modules,
        byte[] ModuleRefs,
        byte[] TypeSpecs,
        byte[] TypeDefs,
        byte[] Layouts,
        byte[] InterfaceDefs,
        byte[] AssociatedTypeDefs,
        byte[] RelationBytes);
}
