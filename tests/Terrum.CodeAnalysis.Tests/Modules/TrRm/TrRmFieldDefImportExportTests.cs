// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmFieldDefImportExportTests
{
    private const string LayoutQueriesSchemaIdentity
        = "trrm/1-draft-layout-queries-1";

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

    [Test]
    public void BindsFieldImportThroughExactOwnerAndLayoutContract()
    {
        var fixture = CreateProviderBindingFixture();
        var validation = BindFieldImports(fixture);
        var replacement = CreateProviderBindingFixture(
            providerTypeSpecOffset: true);
        var rebound = BindFieldImports(fixture with {
            ProviderArtifact = replacement.ProviderArtifact,
        });

        Assert.That(validation.TypeBindings, Has.Length.EqualTo(1));
        Assert.That(validation.FieldBindings, Has.Length.EqualTo(1));
        Assert.That(rebound.FieldBindings, Has.Length.EqualTo(1));
        var owner = validation.TypeBindings[0];
        Assert.That(owner.ExpectedTypeDefRow, Is.EqualTo(1));
        Assert.That(owner.ProviderTypeDefRow, Is.EqualTo(1));
        Assert.That(owner.ExportKey, Is.EqualTo("OwnerKey"));
        Assert.That(owner.ActualDefinitionId, Is.EqualTo(0x1300_0001u));
        Assert.That(owner.ExportRow, Is.EqualTo(1));

        var field = validation.FieldBindings[0];
        Assert.That(field.ExpectedFieldDefRow, Is.EqualTo(1));
        Assert.That(field.ExpectedOwnerTypeDefRow, Is.EqualTo(1));
        Assert.That(field.OwnerImportRow, Is.EqualTo(1));
        Assert.That(field.ProviderTypeDefRow, Is.EqualTo(1));
        Assert.That(field.ProviderFieldDefRow, Is.EqualTo(1));
        Assert.That(field.ExportKey, Is.EqualTo("FieldKey"));
        Assert.That(field.ExportRow, Is.EqualTo(2));
        Assert.That(rebound.FieldBindings[0].ProviderFieldDefRow,
            Is.EqualTo(field.ProviderFieldDefRow));
    }

    [Test]
    public void ComparesGenericFieldTypesByOwnerFormalOrdinal()
    {
        var fixture = CreateProviderBindingFixture(
            providerTypeSpecOffset: true, genericOwner: true);
        var validation = BindFieldImports(fixture);

        Assert.That(validation.TypeBindings, Has.Length.EqualTo(1));
        Assert.That(validation.FieldBindings, Has.Length.EqualTo(1));
    }

    [Test]
    public void RetainsExactFieldImportForFieldPathUse()
    {
        var fixture = CreateProviderBindingFixture(includeFieldPath: true);
        var validation = BindFieldImports(fixture);

        Assert.That(validation.FieldBindings, Has.Length.EqualTo(1));
        Assert.That(validation.FieldBindings[0].ImportRow, Is.EqualTo(2));
        Assert.That(0x1400_0000u
                | checked((uint)validation.FieldBindings[0].ExpectedFieldDefRow),
            Is.EqualTo(0x1400_0001u));
        Assert.That(0x1400_0000u
                | checked((uint)validation.FieldBindings[0].ProviderFieldDefRow),
            Is.EqualTo(0x1400_0001u));
    }

    [Test]
    public void RejectsDirectFieldPathUseThatBypassesImportedStub()
    {
        var fixture = CreateProviderBindingFixture(
            includeFieldImport: false, includeFieldPath: true,
            directFieldPathReference: true);

        _ = Assert.Throws<InvalidDataException>(
            () => BindFieldImports(fixture));

        var existingImportStillCannotBeBypassed
            = CreateProviderBindingFixture(includeFieldPath: true,
                directFieldPathReference: true);
        _ = Assert.Throws<InvalidDataException>(
            () => BindFieldImports(existingImportStillCannotBeBypassed));
    }

    [Test]
    public void RetainsFieldImportInTargetLayoutDependencyRange()
    {
        var fixture = CreateProviderBindingFixture(
            includeLayoutDependency: true);
        var validation = BindFieldImports(fixture);

        Assert.That(validation.FieldBindings, Has.Length.EqualTo(1));
        Assert.That(validation.FieldBindings[0].ExportKey,
            Is.EqualTo("FieldKey"));
    }

    [Test]
    public void ResolvesFieldPathOwnersThroughConstructedTypeFormalSubstitutions()
    {
        var validArtifact = CreateNestedGenericFieldPathArtifact([
            0x1400_0001, 0x1400_0002,
        ]);
        var validFixture = new ProviderBindingFixture(validArtifact, null, [
            new TrRmStandardModuleSelection(0, 1, "Example", 1, 0, 0, 1, 1),
        ]);

        Assert.That(BindFieldImports(validFixture).FieldBindings, Is.Empty);

        var wrongOwnerArtifact = CreateNestedGenericFieldPathArtifact([
            0x1400_0001, 0x1400_0003,
        ]);
        var wrongOwnerFixture = validFixture with {
            ConsumerArtifact = wrongOwnerArtifact,
        };
        _ = Assert.Throws<InvalidDataException>(
            () => BindFieldImports(wrongOwnerFixture));

        var wrongDependencyOwnerArtifact
            = CreateNestedGenericFieldPathArtifact([
                0x1400_0001, 0x1400_0002,
            ], wrongDependencyOwner: true);
        var wrongDependencyOwnerFixture = validFixture with {
            ConsumerArtifact = wrongDependencyOwnerArtifact,
        };
        _ = Assert.Throws<InvalidDataException>(
            () => BindFieldImports(wrongDependencyOwnerFixture));
    }

    [Test]
    public void RejectsMismatchedFieldFlagsTypeAndRetainedLayout()
    {
        foreach (var provider in new[]
        {
            CreateProviderBindingFixture(providerFieldFlags: 10),
            CreateProviderBindingFixture(providerTypeCode: 7),
            CreateProviderBindingFixture(providerAlignment: 4),
            CreateProviderBindingFixture(genericOwner: true,
                providerGenericFieldConcrete: true),
        })
        {
            _ = Assert.Throws<InvalidDataException>(
                () => BindFieldImports(provider));
        }
    }

    [Test]
    public void ReemitsUnchangedFieldProviderRootWithoutDroppingExpectations()
    {
        var fixture = CreateProviderBindingFixture(
            providerTypeSpecOffset: true);
        var validation = BindFieldImports(fixture);
        var originalProgram = new BoundProgram([], -1);
        var closureArtifacts
            = ImmutableArray.CreateBuilder<
                BoundStandardFieldProviderArtifact>(2);
        closureArtifacts.Add(new BoundStandardFieldProviderArtifact(0,
            LayoutQueriesSchemaIdentity,
            [.. fixture.ConsumerArtifact]));
        closureArtifacts.Add(new BoundStandardFieldProviderArtifact(1,
            LayoutQueriesSchemaIdentity,
            [.. fixture.ProviderArtifact!]));
        var closureModules = ImmutableArray.Create(
            new BoundStandardFieldProviderModule(0, 1, "Consumer", 1, 1, 0),
            new BoundStandardFieldProviderModule(1, 1, "Provider", 1, 1, 0));
        var typeEdges
            = ImmutableArray.CreateBuilder<BoundStandardTypeProviderEdge>(
                validation.TypeBindings.Length);
        foreach (var binding in validation.TypeBindings)
        {
            typeEdges.Add(new BoundStandardTypeProviderEdge(
                binding.ConsumerArtifactIndex, binding.ConsumerModuleRow,
                binding.ImportRow, binding.ModuleRefRow,
                0x1300_0000u | checked((uint)binding.ExpectedTypeDefRow),
                binding.ProducerIdentity, binding.ExportKey,
                binding.TargetRequirement, binding.RuntimePolicy,
                binding.ProviderArtifactIndex, binding.ProviderModuleRow,
                binding.ExportRow, binding.ActualDefinitionId));
        }

        var fieldEdges
            = ImmutableArray.CreateBuilder<BoundStandardFieldProviderEdge>(
                validation.FieldBindings.Length);
        foreach (var binding in validation.FieldBindings)
        {
            fieldEdges.Add(new BoundStandardFieldProviderEdge(
                binding.ConsumerArtifactIndex, binding.ConsumerModuleRow,
                binding.ImportRow, binding.ModuleRefRow,
                0x1400_0000u | checked((uint)binding.ExpectedFieldDefRow),
                binding.ExpectedOwnerTypeDefRow, binding.OwnerImportRow,
                binding.ProducerIdentity, binding.ExportKey,
                binding.TargetRequirement, binding.RuntimePolicy,
                binding.ProviderArtifactIndex, binding.ProviderModuleRow,
                binding.ProviderTypeDefRow, binding.ExportRow,
                0x1400_0000u
                    | checked((uint)binding.ProviderFieldDefRow)));
        }

        var closure = new BoundStandardFieldProviderClosure(
            "Consumer", 1, LayoutQueriesSchemaIdentity, 1, 0,
            closureArtifacts.MoveToImmutable(), closureModules,
            typeEdges.MoveToImmutable(), fieldEdges.MoveToImmutable(),
            originalProgram);
        var loadedProgram = originalProgram with {
            StandardFieldProviderClosure = closure,
        };
        var reemitted = TrRmExecutableCompiler.EmitStandard(loadedProgram,
            "Consumer", LayoutQueriesSchemaIdentity, 1);
        Assert.That(reemitted, Is.EqualTo(fixture.ConsumerArtifact));

        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(loadedProgram with {
                EntryPoint = 0,
            }, "Consumer", LayoutQueriesSchemaIdentity, 1));
    }

    [Test]
    public void RejectsFieldExportForAnotherOwnerOrdinalAndMissingProvider()
    {
        var wrongOrdinal = CreateProviderBindingFixture(
            consumerFieldCount: 2, providerFieldCount: 2,
            fieldExportRow: 2);
        _ = Assert.Throws<InvalidDataException>(
            () => BindFieldImports(wrongOrdinal));

        var missingProvider = CreateProviderBindingFixture(
            includeProviderModule: false);
        _ = Assert.Throws<InvalidDataException>(
            () => BindFieldImports(missingProvider));
    }

    private static TrRmStandardFieldProviderValidation BindFieldImports(
        ProviderBindingFixture fixture)
    {
        var consumerTables
            = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(fixture.ConsumerArtifact));
        var providerArtifact = fixture.ProviderArtifact;
        var artifacts = providerArtifact is null
            ? (byte[][])[fixture.ConsumerArtifact]
            : [fixture.ConsumerArtifact, providerArtifact];
        var schemas = new string[artifacts.Length];
        Array.Fill(schemas, LayoutQueriesSchemaIdentity);
        return TrRmFixedArrayProviderBindingValidator
            .ValidateFieldDefImports(consumerTables, artifacts, schemas,
                fixture.Modules, rootModuleRow: 1);
    }

    private static ProviderBindingFixture CreateProviderBindingFixture(
        uint providerFieldFlags = 2, byte providerTypeCode = 8,
        uint providerAlignment = 0, int consumerFieldCount = 1,
        int providerFieldCount = 1,
        int fieldExportRow = 1, bool includeProviderModule = true,
        bool providerTypeSpecOffset = false, bool includeFieldPath = false,
        bool includeFieldImport = true,
        bool directFieldPathReference = false,
        bool includeLayoutDependency = false,
        bool genericOwner = false,
        bool providerGenericFieldConcrete = false)
    {
        var consumerArtifact = CreateProviderBindingArtifact(
            "Consumer", "Provider", includeImports: true,
            fieldFlags: 2, typeCode: 8, alignment: 0,
            fieldCount: consumerFieldCount,
            includeFieldPath: includeFieldPath,
            includeFieldImport: includeFieldImport,
            directFieldPathReference: directFieldPathReference,
            includeLayoutDependency: includeLayoutDependency,
            genericOwner: genericOwner);
        var providerArtifact = includeProviderModule
            ? CreateProviderBindingArtifact("Provider", null,
                includeImports: false, fieldFlags: providerFieldFlags,
                typeCode: providerTypeCode, alignment: providerAlignment,
                fieldCount: providerFieldCount,
                fieldExportRow: fieldExportRow,
                typeSpecOffset: providerTypeSpecOffset,
                genericOwner: genericOwner,
                genericFieldConcrete: providerGenericFieldConcrete)
            : null;
        var modules = includeProviderModule
            ? ImmutableArray.Create(
                new TrRmStandardModuleSelection(0, 1, "Consumer", 1, 0,
                    0, 1, 1),
                new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0,
                    0, 1, 1))
            : [new TrRmStandardModuleSelection(0, 1, "Consumer", 1, 0,
                0, 1, 1)];
        return new ProviderBindingFixture(consumerArtifact,
            providerArtifact, modules);
    }

    private static byte[] CreateNestedGenericFieldPathArtifact(
        IReadOnlyList<uint> fieldReferences,
        bool wrongDependencyOwner = false)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Example", 0), ("Example", 0), ("Outer", 0),
            ("Container", 0), ("Other", 0), ("Inner", 0), ("Value", 0),
            ("T", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 9);
        var featureBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.LayoutQueries,
        ]);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1, 1, 1),
        ], strings, 0, 3, 0, 0, 1);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 3, 0, 0, 1);
        var typeSpecRows = new ReadOnlyMemory<byte>[] {
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 2),
            TrRmTypeSpecTableWriter.Reference(0x04, 1),
            TrRmTypeSpecTableWriter.Constructed(3, [1]),
            TrRmTypeSpecTableWriter.Constructed(2, [5]),
            TrRmTypeSpecTableWriter.Reference(0x02, 3),
        };
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            3, 0, 2);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 7,
            3, 0, 2);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(3, 4, 6, 1, 1, 1, 1, 1, 1, 0, 0),
            new TrRmTypeDefRow(3, 5, 6, 2, 1, 2, 1, 2, 1, 0, 0),
            new TrRmTypeDefRow(3, 6, 6, 3, 1, 0, 0, 3, 1, 0, 0),
        ], strings, modules, 3, 2, 3, 0);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 3, strings,
            modules, 3, 2, 3, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0, strings,
            modules, types, signatures, 0, 0);
        var genericParamBytes = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1300_0001, 0, 9, 0, 0,
                1, 1, 2, 0, 0),
            new TrRmGenericParamRow(0x1300_0002, 0, 9, 0, 0,
                1, 1, 2, 0, 0),
        ], strings, types, methods, signatures, typeSpecs, 0);
        var fields = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 7, 4, 0),
            new TrRmFieldDefRow(2, 8, 1, 0),
            new TrRmFieldDefRow(3, 8, 1, 0),
        ], strings, types, typeSpecs);
        var fieldReader = new TrRmFieldDefTableReader(fields, 3,
            strings, types, typeSpecs);
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ]);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, 1);
        var layouts = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 1, 1),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 2, 1),
            new TrRmLayoutRow(3, 0, 0, 0, 0, 0, 3, 1),
        ], types, targetProfiles, 3);
        var layoutReader = new TrRmLayoutTableReader(layouts, 3, types,
            targetProfiles, 3);
        var layoutFields = TrRmLayoutFieldTableWriter.Encode([
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(2, 0, 2),
            new TrRmLayoutFieldRow(3, 0, 3),
        ], layoutReader, types, fieldReader);
        var fieldPath = TrRmFieldPathTableWriter.Encode([
            new TrRmFieldPathRow(6, fieldReferences),
        ], typeSpecs, fieldReader, new TrRmImportTableReader([], 0,
            strings, new TrRmModuleRefTableReader([], 0, strings, modules),
            types, methods, default, default, default, fieldReader));
        uint[] targetDependencyFieldReferences = wrongDependencyOwner
            ? [fieldReferences[0], 0x1400_0003u]
            : [.. fieldReferences];
        var targetDependencyFields
            = TrRmTargetLayoutDependencyFieldTableWriter.Encode(
                targetDependencyFieldReferences, fieldReader,
                new TrRmImportTableReader([], 0, strings,
                    new TrRmModuleRefTableReader([], 0, strings, modules),
                    types, methods, default, default, default, fieldReader));
        var targetDependencies
            = TrRmTargetLayoutDependencyTableWriter.Encode([
                new TrRmTargetLayoutDependencyRow(3, 0, 0, 6, 1, 1,
                    targetDependencyFieldReferences.Length),
            ], typeSpecs, targetProfiles,
                targetDependencyFieldReferences.Length);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 9, stringBytes),
            new TrRmSection(0x0004, 0, 1, featureBytes),
            new TrRmSection(0x0011, 0, 7, typeSpecBytes),
            new TrRmSection(0x0013, 0, 3, typeDefBytes),
            new TrRmSection(0x0014, 0, 3, fields),
            new TrRmSection(0x0015, 0, 3, layouts),
            new TrRmSection(0x0019, 0, 2, genericParamBytes),
            new TrRmSection(0x0023, 0, 3, layoutFields),
            new TrRmSection(0x0045, 0, 1, targetDependencies),
            new TrRmSection(0x0048, 0, 1, targetProfileBytes),
            new TrRmSection(0x004A, 0,
                (uint)targetDependencyFieldReferences.Length,
                targetDependencyFields),
            new TrRmSection(0x004B, 0, 1, fieldPath),
        ], TrRmArtifactSchema.Standard);
    }

    private static byte[] CreateProviderBindingArtifact(
        string moduleIdentity, string? producerIdentity,
        bool includeImports, uint fieldFlags, byte typeCode,
        uint alignment, int fieldCount, int fieldExportRow = 1,
        bool typeSpecOffset = false, bool includeFieldPath = false,
        bool includeFieldImport = true,
        bool directFieldPathReference = false,
        bool includeLayoutDependency = false,
        bool genericOwner = false, bool genericFieldConcrete = false)
    {
        var stringBytes = TrRmStringTableWriter.EncodeNames([
            (moduleIdentity, 0), (producerIdentity ?? "Provider", 0),
            ("Example", 0), ("Widget", 0), ("Field", 0),
            ("OwnerKey", 0), ("FieldKey", 0), ("Other", 0), ("T", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 9);
        var moduleRefCount = includeImports ? 1 : 0;
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1, 1, 1),
        ], strings, moduleRefCount, 1, 0, 0, 1);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount, 1, 0, 0, 1);
        var featureBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.LayoutQueries,
        ]);
        var moduleRefBytes = moduleRefCount == 0 ? [] :
            TrRmModuleRefTableWriter.Encode([
                new TrRmModuleRefRow(2, 1, 0),
            ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(
            moduleRefBytes, moduleRefCount, strings, modules);
        var importRows = !includeImports
            ? []
            : includeFieldImport
                ? new[] {
                    new TrRmImportRow(1, 6, 0x1300_0001),
                    new TrRmImportRow(1, 7, 0x1400_0001),
                }
                : [new TrRmImportRow(1, 6, 0x1300_0001)];
        var typeSpecRows = new ReadOnlyMemory<byte>[
            (typeSpecOffset ? 3 : 2)];
        typeSpecRows[0] = TrRmTypeSpecTableWriter.Reference(
            includeImports ? (byte)0x03 : (byte)0x02,
            1);
        var fieldTypeSpec = typeSpecOffset ? 3u : 2u;
        if (typeSpecOffset)
        {
            typeSpecRows[1] = TrRmTypeSpecTableWriter.Builtin((byte)7);
        }
        if (genericOwner && !genericFieldConcrete)
        {
            typeSpecRows[checked((int)fieldTypeSpec - 1)]
                = new byte[] { 0x04, 0, 1, 0, 0, 0 };
        }
        else
        {
            typeSpecRows[checked((int)fieldTypeSpec - 1)]
                = TrRmTypeSpecTableWriter.Builtin(typeCode);
        }
        var genericParamCount = genericOwner ? 1 : 0;
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            1, importRows.Length, genericParamCount);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length, 1,
            importRows.Length, genericParamCount);
        var genericFirst = genericOwner ? 1 : 0;
        var genericCount = genericOwner ? 1 : 0;
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(3, 4, 6, 1, fieldCount, genericFirst,
                genericCount, 1, 1, 0, 0),
        ], strings, modules, fieldCount, genericParamCount, 1, 0);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 1, strings,
            modules, fieldCount, genericParamCount, 1, 0);
        var fieldRows = new TrRmFieldDefRow[fieldCount];
        for (var fieldOrdinal = 0; fieldOrdinal < fieldCount;
            fieldOrdinal++)
        {
            fieldRows[fieldOrdinal] = new TrRmFieldDefRow(1,
                fieldOrdinal == 0 ? 5 : 8, fieldTypeSpec, fieldFlags);
        }

        var fieldBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            types, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, fieldCount,
            strings, types, typeSpecs);
        var importBytes = importRows.Length != 0
            ? EncodeImportRows(importRows)
            : [];
        var methods = new TrRmMethodDefTableReader([], 0, strings,
            modules, types, default, 0, 0);
        var genericParamBytes = genericOwner
            ? TrRmGenericParamTableWriter.Encode([
                new TrRmGenericParamRow(0x1300_0001, 0, 9, 0, 0,
                    1, 1, 1, 0, 0),
            ], strings, types, methods, default, typeSpecs, 0)
            : [];
        var imports = new TrRmImportTableReader(importBytes,
            importRows.Length, strings, moduleRefs, types, methods, default,
            default, default, fields);
        var fieldPathBytes = Array.Empty<byte>();
        if (includeFieldPath)
        {
            fieldPathBytes = TrRmFieldPathTableWriter.Encode([
                new TrRmFieldPathRow(1, [
                    directFieldPathReference
                        ? 0x1400_0001u : 0x1C00_0002u,
                ]),
            ], typeSpecs, fields, imports);
        }
        var targetProfileBytes = includeLayoutDependency
            ? TrRmTargetProfileTableWriter.Encode([
                new TrRmTargetProfileRow(1, 1),
            ])
            : [];
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, includeLayoutDependency ? 1 : 0);
        var targetLayoutDependencyBytes = includeLayoutDependency
            ? TrRmTargetLayoutDependencyTableWriter.Encode([
                new TrRmTargetLayoutDependencyRow(3, 0, 0, 1, 1, 1, 1),
            ], typeSpecs, targetProfiles, 1)
            : [];
        var targetLayoutDependencyFieldBytes = includeLayoutDependency
            ? TrRmTargetLayoutDependencyFieldTableWriter.Encode([
                0x1C00_0002,
            ], fields, imports)
            : [];

        var layoutFieldCount = (fieldFlags & 8) == 0 ? fieldCount : 0;
        var layoutFieldFirst = layoutFieldCount == 0 ? 0 : 1;
        var layoutKind = alignment == 0 ? (byte)0 : (byte)1;
        var layoutPack = alignment == 0 ? 0u : 4u;
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, layoutKind, 0, alignment, layoutPack,
                layoutFieldFirst, layoutFieldCount),
        ], types, targetProfiles, layoutFieldCount);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 1, types,
            targetProfiles, layoutFieldCount);
        var layoutFieldRows = new TrRmLayoutFieldRow[layoutFieldCount];
        for (var fieldOrdinal = 0; fieldOrdinal < layoutFieldCount;
            fieldOrdinal++)
        {
            layoutFieldRows[fieldOrdinal] = new TrRmLayoutFieldRow(1,
                fieldOrdinal, fieldOrdinal + 1);
        }
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, types, fields);

        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 9, stringBytes),
            new(0x0004, 0, 1, featureBytes),
        };
        if (moduleRefCount != 0)
        {
            sections.Add(new TrRmSection(0x0010, 0, 1,
                moduleRefBytes));
        }

        sections.AddRange([
            new(0x0011, 0, (uint)typeSpecRows.Length, typeSpecBytes),
            new(0x0013, 0, 1, typeDefBytes),
            new(0x0014, 0, (uint)fieldCount, fieldBytes),
            new(0x0015, 0, 1, layoutBytes),
        ]);
        if (genericOwner)
        {
            sections.Add(new TrRmSection(0x0019, 0, 1,
                genericParamBytes));
        }

        if (importRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                (uint)importRows.Length,
                importBytes));
        }
        else
        {
            var exports = EncodeExportRows([
                new TrRmExportRow(0x1300_0001, 6),
                new TrRmExportRow(
                    0x1400_0000u | checked((uint)fieldExportRow), 7),
            ]);
            sections.Add(new TrRmSection(0x001D, 0, 2, exports));
        }

        if (layoutFieldCount != 0)
        {
            sections.Add(new TrRmSection(0x0023, 0,
                (uint)layoutFieldCount, layoutFieldBytes));
        }

        if (includeLayoutDependency)
        {
            sections.Add(new TrRmSection(0x0045, 0, 1,
                targetLayoutDependencyBytes));
            sections.Add(new TrRmSection(0x0048, 0, 1,
                targetProfileBytes));
            sections.Add(new TrRmSection(0x004A, 0, 1,
                targetLayoutDependencyFieldBytes));
        }
        if (includeFieldPath)
        {
            sections.Add(new TrRmSection(0x004B, 0, 1, fieldPathBytes));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.Standard);
    }

    private sealed record ProviderBindingFixture(
        byte[] ConsumerArtifact, byte[]? ProviderArtifact,
        ImmutableArray<TrRmStandardModuleSelection> Modules);

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
