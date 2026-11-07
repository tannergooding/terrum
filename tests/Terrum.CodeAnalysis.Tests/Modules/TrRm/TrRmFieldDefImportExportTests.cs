// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmFieldDefImportExportTests
{
    [Test]
    public void BindsExpectedFieldAndOwnerImportsToPublicProviderRows()
    {
        var fixture = CreateFixture();
        var imports = new[]
        {
            new TrRmImportRow(1, 6, 0x1300_0002),
            new TrRmImportRow(1, 7, 0x1400_0002),
        };
        var exports = new[]
        {
            new TrRmExportRow(0x1300_0001, 6),
            new TrRmExportRow(0x1400_0001, 7),
        };

        Assert.That(Read(fixture, imports, exports, writeRows: true,
            standardLayoutFeature: true), Is.True);
    }

    [Test]
    public void RejectsFieldImportsWithoutOwnerOrInTheWrongModule()
    {
        var fixture = CreateFixture();
        var exports = new[] { new TrRmExportRow(0x1400_0001, 7) };
        _ = Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [new TrRmImportRow(1, 7, 0x1400_0002)], exports,
                standardLayoutFeature: true));
        _ = Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [
                new TrRmImportRow(1, 6, 0x1300_0002),
                new TrRmImportRow(1, 7, 0x1400_0001),
            ], exports, standardLayoutFeature: true));
        _ = Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [
                new TrRmImportRow(1, 6, 0x1300_0002),
                new TrRmImportRow(1, 7, 0x1400_0003),
            ], exports, standardLayoutFeature: true));
    }

    [Test]
    public void RejectsFieldStubsAndFieldsOfImportedOwnersAsExports()
    {
        var fixture = CreateFixture();
        var imports = new[]
        {
            new TrRmImportRow(1, 6, 0x1300_0002),
            new TrRmImportRow(1, 7, 0x1400_0002),
        };
        _ = Assert.Throws<InvalidDataException>(() =>
            Read(fixture, imports, [new TrRmExportRow(0x1400_0002, 7)],
                standardLayoutFeature: true));
        _ = Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [new TrRmImportRow(1, 6, 0x1300_0002)],
                [new TrRmExportRow(0x1400_0002, 7)],
                standardLayoutFeature: true));
    }

    [Test]
    public void RejectsFieldExportsWithPrivateFieldOrOwner()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            Read(CreateFixture(providerFieldFlags: 9), [],
                [new TrRmExportRow(0x1400_0001, 7)],
                standardLayoutFeature: true));
        _ = Assert.Throws<InvalidDataException>(() =>
            Read(CreateFixture(providerTypeFlags: 5), [],
                [new TrRmExportRow(0x1400_0001, 7)],
                standardLayoutFeature: true));
    }

    [Test]
    public void StandardRequiresLayoutQueriesForFieldImportAndExport()
    {
        var fixture = CreateFixture();
        var imports = new[]
        {
            new TrRmImportRow(1, 6, 0x1300_0002),
            new TrRmImportRow(1, 7, 0x1400_0002),
        };
        var exports = new[]
        {
            new TrRmExportRow(0x1300_0001, 6),
            new TrRmExportRow(0x1400_0001, 7),
        };
        _ = Assert.Throws<InvalidDataException>(() =>
            Read(fixture, imports, exports, writeRows: true));
        Assert.That(Read(fixture, imports, exports, writeRows: true,
            standardLayoutFeature: true), Is.True);

        _ = Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [], exports, writeRows: true));
        Assert.That(Read(fixture, [], exports, writeRows: true,
            standardLayoutFeature: true), Is.False);
    }

    [Test]
    public void CopyOriginCandidateAllowsPrivateTypeAndFieldContractIdentities()
    {
        var fixture = CreateFixture(providerTypeFlags: 5, providerFieldFlags: 9,
            consumerTypeFlags: 5, consumerFieldFlags: 9);
        var imports = new[]
        {
            new TrRmImportRow(1, 6, 0x1300_0002),
            new TrRmImportRow(1, 7, 0x1400_0002),
        };
        var exports = new[]
        {
            new TrRmExportRow(0x1300_0001, 6),
            new TrRmExportRow(0x1400_0001, 7),
        };

        Assert.That(Read(fixture, imports, exports, writeRows: true,
            featureRows: [new TrRmFeatureRow(13, 1), new TrRmFeatureRow(15, 1)]),
            Is.True);
    }

    [Test]
    public void CopyOriginCandidateRequiresBothIdentityFeatures()
    {
        var fixture = CreateFixture(providerTypeFlags: 5, providerFieldFlags: 9,
            consumerTypeFlags: 5, consumerFieldFlags: 9);
        var imports = new[]
        {
            new TrRmImportRow(1, 6, 0x1300_0002),
            new TrRmImportRow(1, 7, 0x1400_0002),
        };
        var exports = new[]
        {
            new TrRmExportRow(0x1300_0001, 6),
            new TrRmExportRow(0x1400_0001, 7),
        };

        _ = Assert.Throws<InvalidDataException>(() =>
            Read(fixture, imports, exports, writeRows: true,
                featureRows: [new TrRmFeatureRow(13, 1)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            Read(fixture, imports, exports, writeRows: true,
                featureRows: [new TrRmFeatureRow(15, 1)]));
    }

    [Test]
    public void CopyOriginCandidateRequiresLayoutFeatureForFieldExports()
    {
        var fixture = CreateFixture(providerTypeFlags: 5, providerFieldFlags: 9);
        _ = Assert.Throws<InvalidDataException>(() =>
            Read(fixture, [], [
                new TrRmExportRow(0x1300_0001, 6),
                new TrRmExportRow(0x1400_0001, 7),
            ], writeRows: true,
                featureRows: [new TrRmFeatureRow(15, 1)]));
    }

    private static bool Read(Fixture fixture,
        TrRmImportRow[] importRows,
        TrRmExportRow[] exportRows, bool writeRows = false,
        TrRmFeatureRow[]? featureRows = null,
        bool standardLayoutFeature = false)
    {
        var copyOriginDraft = featureRows is not null;
        var schema = copyOriginDraft
            ? TrRmArtifactSchema.CopyOriginDraft : TrRmArtifactSchema.Standard;
        var selectedFeatureRows = featureRows
            ?? (standardLayoutFeature
                ? [TrRmFeatureRow.LayoutQueries]
                : []);
        var strings = new TrRmStringTableReader(fixture.Strings, 7);
        var moduleBytes = (byte[])fixture.Modules.Clone();
        var featureBytes = selectedFeatureRows.Length == 0 ? [] :
            TrRmFeatureTableWriter.Encode(selectedFeatureRows, schema);
        var featureCount = selectedFeatureRows.Length;
        if (featureCount != 0)
        {
            for (var module = 0; module < 2; module++)
            {
                var offset = module * 48;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    moduleBytes.AsSpan(offset + 24), 1);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    moduleBytes.AsSpan(offset + 28), (uint)featureCount);
            }
        }
        var features = new TrRmFeatureTableReader(featureBytes, featureCount, schema);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, strings,
            1, 2, 0, 0, featureCount);
        var references = new TrRmModuleRefTableReader(fixture.ModuleRefs, 1,
            strings, modules);
        var types = new TrRmTypeDefTableReader(fixture.TypeDefs, 2,
            strings, modules, 2, 0, 2, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs, 1,
            2, 1, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layouts = new TrRmLayoutTableReader(fixture.Layouts, 2,
            types, targetProfiles, 0);
        var fields = new TrRmFieldDefTableReader(fixture.Fields, 2,
            strings, types, typeSpecs);
        var layoutFields = new TrRmLayoutFieldTableReader([], 0,
            layouts, types, fields);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var properties = default(TrRmPropertyDefTableReader);
        var generics = new TrRmGenericParamTableReader([], 0,
            strings, types, methods, signatures, typeSpecs, 0);
        var interfaces = new TrRmInterfaceDefTableReader([], 0,
            types, generics, 0, 0, 0, 0, 0);
        var associatedTypeDefs = new TrRmAssociatedTypeDefTableReader(
            [], 0, strings, typeSpecs, interfaces);

        var importBytes = writeRows && importRows.Length != 0
            ? copyOriginDraft
                ? TrRmImportTableWriter.Encode(importRows, strings, references,
                    types, methods, properties, interfaces, associatedTypeDefs,
                    fields, features, modules, schema)
                : TrRmImportTableWriter.Encode(importRows, strings, references,
                    types, methods, properties, interfaces, associatedTypeDefs, fields)
            : EncodeImportRows(importRows);
        var imports = copyOriginDraft
            ? new TrRmImportTableReader(importBytes, importRows.Length,
                strings, references, types, methods, properties,
                interfaces, associatedTypeDefs, fields, features, modules, schema)
            : new TrRmImportTableReader(importBytes, importRows.Length,
                strings, references, types, methods, properties,
                interfaces, associatedTypeDefs, fields);
        var exportBytes = writeRows && exportRows.Length != 0
            ? copyOriginDraft
                ? TrRmExportTableWriter.Encode(exportRows, strings, types, methods,
                    properties, imports, interfaces, associatedTypeDefs, fields,
                    features, modules, schema)
                : TrRmExportTableWriter.Encode(exportRows, strings, types, methods,
                    properties, imports, interfaces, associatedTypeDefs, fields)
            : EncodeExportRows(exportRows);
        if (copyOriginDraft)
        {
            _ = new TrRmExportTableReader(exportBytes, exportRows.Length,
                strings, types, methods, properties, imports,
                interfaces, associatedTypeDefs, fields, features, modules, schema);
        }
        else
        {
            _ = new TrRmExportTableReader(exportBytes, exportRows.Length,
                strings, types, methods, properties, imports,
                interfaces, associatedTypeDefs, fields);
        }
        _ = layoutFields;

        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 2, moduleBytes),
            new(0x0002, 0, 7, fixture.Strings),
        };
        if (featureCount != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                (uint)featureCount, featureBytes));
        }
        sections.AddRange([
            new TrRmSection(0x0010, 0, 1, fixture.ModuleRefs),
            new TrRmSection(0x0011, 0, 1, fixture.TypeSpecs),
            new TrRmSection(0x0013, 0, 2, fixture.TypeDefs),
            new TrRmSection(0x0014, 0, 2, fixture.Fields),
            new TrRmSection(0x0015, 0, 2, fixture.Layouts),
        ]);
        if (importRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                (uint)importRows.Length, importBytes));
        }
        if (exportRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                (uint)exportRows.Length, exportBytes));
        }
        var artifact = TrRmArtifactWriter.Encode(sections, schema);
        if (copyOriginDraft)
        {
            _ = TrRmStructuralMetadataValidator.ValidateCopyOriginDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForCopyOriginDraft(artifact));
        }
        else
        {
            TrRmStructuralMetadataValidator.Validate(artifact);
        }
        return imports.IsExpectedDefinition(0x1400_0002);
    }

    private static Fixture CreateFixture(uint providerTypeFlags = 6,
        uint providerFieldFlags = 10, uint consumerTypeFlags = 6,
        uint consumerFieldFlags = 10)
    {
        var stringBytes = TrRmStringTableWriter.EncodeNames([
            ("Provider", 0), ("Consumer", 0), ("Example", 0), ("Widget", 0),
            ("Field", 0), ("InterfaceKey", 0), ("FieldKey", 0),
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
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 2, 1, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1, 2, 1, 0);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(3, 4, providerTypeFlags,
                1, 1, 0, 0, 1, 1, 0, 0),
            new TrRmTypeDefRow(3, 4, consumerTypeFlags,
                2, 1, 0, 0, 2, 1, 0, 0),
        ], strings, modules, 2, 0, 2, 0);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 2,
            strings, modules, 2, 0, 2, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 5, 1, providerFieldFlags),
            new TrRmFieldDefRow(2, 5, 1, consumerFieldFlags),
        ], strings, types, typeSpecs);
        return new Fixture(stringBytes, moduleBytes, moduleRefBytes,
            typeSpecBytes, typeDefBytes, layoutBytes, fieldBytes);
    }

    private static byte[] EncodeImportRows(TrRmImportRow[] rows)
    {
        var bytes = new byte[rows.Length * 12];
        for (var row = 0; row < rows.Length; row++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(row * 12),
                unchecked((uint)rows[row].ModuleRefRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan((row * 12) + 4), unchecked((uint)rows[row].ExportKeyStringRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan((row * 12) + 8), rows[row].ExpectedLocalDefId);
        }

        return bytes;
    }

    private static byte[] EncodeExportRows(TrRmExportRow[] rows)
    {
        var bytes = new byte[rows.Length * 8];
        for (var row = 0; row < rows.Length; row++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(row * 8), rows[row].LocalDefId);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan((row * 8) + 4), unchecked((uint)rows[row].ExportKeyStringRow));
        }

        return bytes;
    }

    private sealed record Fixture(
        byte[] Strings,
        byte[] Modules,
        byte[] ModuleRefs,
        byte[] TypeSpecs,
        byte[] TypeDefs,
        byte[] Layouts,
        byte[] Fields);
}
