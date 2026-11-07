// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmInterfaceConstantTableTests
{
    [Test]
    public void ValidatesInterfaceAndAssociatedConstantDeclarations()
    {
        var fixture = CreateFixture();

        TrRmStructuralMetadataValidator.Validate(fixture.Artifact);
        AssertRows(fixture);
    }

    [Test]
    public void InterfaceAndAssociatedConstantReadersChargeAtExactWorkBoundary()
    {
        var fixture = CreateFixture();
        TrRmA3ResourceLedgerTestHelper.AssertExactWork(
            ledger => AssertRows(fixture, ledger));
    }

    [Test]
    public void RejectsMalformedConstantRowsAndOrphanedRangesInArtifacts()
    {
        var fixture = CreateFixture();
        var malformedInterfaceConst = (byte[])fixture.InterfaceConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformedInterfaceConst, 2);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            malformedInterfaceConst, fixture.AssociatedConstDefs);

        malformedInterfaceConst = (byte[])fixture.InterfaceConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedInterfaceConst.AsSpan(sizeof(uint)), 0);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            malformedInterfaceConst, fixture.AssociatedConstDefs);

        malformedInterfaceConst = (byte[])fixture.InterfaceConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedInterfaceConst.AsSpan(2 * sizeof(uint)), 0);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            malformedInterfaceConst, fixture.AssociatedConstDefs);

        malformedInterfaceConst = (byte[])fixture.InterfaceConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedInterfaceConst.AsSpan(3 * sizeof(uint)), 2);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            malformedInterfaceConst, fixture.AssociatedConstDefs);

        var malformedAssociatedConst = (byte[])fixture.AssociatedConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformedAssociatedConst, 2);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            fixture.InterfaceConstDefs, malformedAssociatedConst);

        malformedAssociatedConst = (byte[])fixture.AssociatedConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedAssociatedConst.AsSpan(sizeof(uint)), 0);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            fixture.InterfaceConstDefs, malformedAssociatedConst);

        malformedAssociatedConst = (byte[])fixture.AssociatedConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedAssociatedConst.AsSpan(2 * sizeof(uint)), 0);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            fixture.InterfaceConstDefs, malformedAssociatedConst);

        var malformedInterface = (byte[])fixture.InterfaceDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedInterface.AsSpan(10 * sizeof(uint)), 0);
        AssertInvalid(fixture, malformedInterface,
            fixture.InterfaceConstDefs, fixture.AssociatedConstDefs);

        malformedInterface = (byte[])fixture.InterfaceDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedInterface.AsSpan(8 * sizeof(uint)), 0);
        AssertInvalid(fixture, malformedInterface,
            fixture.InterfaceConstDefs, fixture.AssociatedConstDefs);
    }

    [Test]
    public void InitializedInterfaceConstantRequiresTypedFrozenValuesFeature()
    {
        var fixture = CreateFixture();
        var moduleBytes = (byte[])fixture.Modules.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(moduleBytes.AsSpan(24), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(moduleBytes.AsSpan(28), 0);
        var noFeatureFixture = fixture with { Modules = moduleBytes };

        _ = Assert.Throws<NotSupportedException>(() => TrRmStructuralMetadataValidator.Validate(
            CreateArtifact(noFeatureFixture, fixture.InterfaceDefs,
                fixture.InterfaceConstDefs, fixture.AssociatedConstDefs,
                includeFeature11: false)));
    }

    [Test]
    public void AssociatedConstantDefCandidateUsesExactOwnerImportAndExportRules()
    {
        var fixture = CreateFixture();
        var strings = new TrRmStringTableReader(fixture.Strings, 4);
        var schema = TrRmArtifactSchema.GeneralPredicateGraphsDraft;
        var featuresBytes = TrRmFeatureTableWriter.Encode(
            [TrRmFeatureRow.TypedFrozenValues], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 1, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1, 1, 1),
        ], strings, moduleRefCount: 1, typeDefCount: 1, methodDefCount: 0,
            sourceFileCount: 0, featureCount: 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            1, 1, 0, 0, 1);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs, 1, 0, 0, 0, 0);
        var types = new TrRmTypeDefTableReader(fixture.TypeDefs, 1,
            strings, modules, 0, 0, 1, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var properties = default(TrRmPropertyDefTableReader);
        var genericParams = new TrRmGenericParamTableReader([], 0,
            strings, types, methods, signatures, typeSpecs, 0, 1);
        var interfaces = new TrRmInterfaceDefTableReader(fixture.InterfaceDefs,
            1, types, genericParams, 0, 0, 0, 1, 1);
        var associatedConsts = new TrRmAssociatedConstDefTableReader(
            fixture.AssociatedConstDefs, 1, strings, typeSpecs, interfaces);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(2, 0, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(
            moduleRefBytes, 1, strings, modules);
        var emptyImports = new TrRmImportTableReader([], 0, strings, moduleRefs,
            types, methods, properties, interfaces, default, default,
            features, modules, schema, associatedConsts);

        _ = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x2500_0001, 4),
        ], strings, types, methods, properties, emptyImports,
            interfaces, default, default, features, modules, schema, associatedConsts);

        var importBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 3, 0x1300_0001),
            new TrRmImportRow(1, 4, 0x2500_0001),
        ], strings, moduleRefs, types, methods, properties,
            interfaces, default, default, features, modules, schema, associatedConsts);
        var imports = new TrRmImportTableReader(importBytes, 2, strings,
            moduleRefs, types, methods, properties, interfaces, default, default,
            features, modules, schema, associatedConsts);
        Assert.That(imports.IsExpectedDefinition(0x2500_0001), Is.True);

        var typeSpecsWithOwnerBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
        ], typeDefCount: 1, importCount: 0, genericParamCount: 0);
        var typeSpecsWithOwner = new TrRmTypeSpecTableReader(
            typeSpecsWithOwnerBytes, 2, 1, 0, 0, 0);
        var interfaceBases = new TrRmInterfaceBaseTableReader(
            [], 0, typeSpecsWithOwner, interfaces);
        var witnessesBytes = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(2, 2, 0, 0, 0, 0, 1, 1),
        ], typeSpecsWithOwner, 0, 0, 1);
        var witnesses = new TrRmInterfaceWitnessTableReader(
            witnessesBytes, 1, typeSpecsWithOwner, 0, 0, 1);
        var constantSources = new TrRmConstantSourceTableReader(
            fixture.ConstantSources, 1, 1, 0, 0);
        var importedBindingBytes = TrRmWitnessAssociatedConstTableWriter.Encode([
            new TrRmWitnessAssociatedConstRow(1, 2, 0x1C00_0002, 1),
        ], witnesses, typeSpecsWithOwner, types, interfaces, interfaceBases,
            associatedConsts, imports, constantSources, schema);
        var importedBinding = new TrRmWitnessAssociatedConstTableReader(
            importedBindingBytes, 1, witnesses, typeSpecsWithOwner,
            types, interfaces, interfaceBases, associatedConsts, imports,
            constantSources, schema);
        Assert.That(importedBinding.GetAssociatedConstDefReference(1),
            Is.EqualTo(0x1C00_0002u));

        var missingOwnerRejected = false;
        try
        {
            _ = TrRmImportTableWriter.Encode([
                new TrRmImportRow(1, 4, 0x2500_0001),
            ], strings, moduleRefs, types, methods, properties,
                interfaces, default, default, features, modules, schema,
                associatedConsts);
        }
        catch (InvalidDataException)
        {
            missingOwnerRejected = true;
        }
        Assert.That(missingOwnerRejected, Is.True);

        var importedExpectationExportRejected = false;
        try
        {
            _ = TrRmExportTableWriter.Encode([
                new TrRmExportRow(0x2500_0001, 4),
            ], strings, types, methods, properties, imports,
                interfaces, default, default, features, modules, schema,
                associatedConsts);
        }
        catch (InvalidDataException)
        {
            importedExpectationExportRejected = true;
        }
        Assert.That(importedExpectationExportRejected, Is.True);

        var standardProfileRejected = false;
        try
        {
            _ = TrRmExportTableWriter.Encode([
                new TrRmExportRow(0x2500_0001, 4),
            ], strings, types, methods, properties, emptyImports,
                interfaces, default, default, associatedConsts);
        }
        catch (InvalidDataException)
        {
            standardProfileRejected = true;
        }
        Assert.That(standardProfileRejected, Is.True);
    }

    [Test]
    public void ValidatesWitnessAssociatedConstantIdentityAndUniqueBinding()
    {
        var fixture = CreateFixture();
        var strings = new TrRmStringTableReader(fixture.Strings, 4);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 1, 0, 0, 1);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
        ], typeDefCount: 1, importCount: 0, genericParamCount: 0);
        var typeSpecs = new TrRmTypeSpecTableReader(
            typeSpecsBytes, 2, 1, 0, 0, 0);
        var types = new TrRmTypeDefTableReader(fixture.TypeDefs, 1,
            strings, modules, 0, 0, 1, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var genericParams = new TrRmGenericParamTableReader([], 0,
            strings, types, methods, signatures, typeSpecs, 0, 1);
        var interfaces = new TrRmInterfaceDefTableReader(fixture.InterfaceDefs,
            1, types, genericParams, 0, 0, 0, 1, 1);
        var interfaceBases = new TrRmInterfaceBaseTableReader(
            [], 0, typeSpecs, interfaces);
        var associatedConsts = new TrRmAssociatedConstDefTableReader(
            fixture.AssociatedConstDefs, 1, strings, typeSpecs, interfaces);
        var imports = default(TrRmImportTableReader);
        var constantSources = new TrRmConstantSourceTableReader(
            fixture.ConstantSources, 1, 1, 0, 0);
        var witnessesBytes = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(2, 2, 0, 0, 0, 0, 1, 1),
        ], typeSpecs, memberCount: 0, associatedTypeCount: 0,
            associatedConstCount: 1);
        var witnesses = new TrRmInterfaceWitnessTableReader(
            witnessesBytes, 1, typeSpecs, 0, 0, 1);
        var selectedBinding = new TrRmWitnessAssociatedConstRow(
            1, 2, 0x2500_0001, 1);
        var selectedBindingBytes = TrRmWitnessAssociatedConstTableWriter.Encode([
            selectedBinding,
        ], witnesses, typeSpecs, types, interfaces, interfaceBases,
            associatedConsts, imports, constantSources, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var reader = new TrRmWitnessAssociatedConstTableReader(
            selectedBindingBytes, 1, witnesses, typeSpecs, types, interfaces,
            interfaceBases, associatedConsts, imports, constantSources,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);

        Assert.That(reader.GetWitnessRow(1), Is.EqualTo(1));
        Assert.That(reader.GetDeclaringInterfaceTypeSpecRef(1), Is.EqualTo(2u));
        Assert.That(reader.GetAssociatedConstDefReference(1), Is.EqualTo(0x2500_0001u));
        Assert.That(reader.GetConstantSourceRow(1), Is.EqualTo(1));

        var wrongDefinitionRejected = false;
        try
        {
            _ = TrRmWitnessAssociatedConstTableWriter.Encode([
                selectedBinding with { AssociatedConstDefReference = 0x2400_0001 },
            ], witnesses, typeSpecs, types, interfaces, interfaceBases,
                associatedConsts, imports, constantSources,
                TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        }
        catch (InvalidDataException)
        {
            wrongDefinitionRejected = true;
        }
        Assert.That(wrongDefinitionRejected, Is.True);

        var duplicateWitnessesBytes = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(2, 2, 0, 0, 0, 0, 1, 2),
        ], typeSpecs, memberCount: 0, associatedTypeCount: 0,
            associatedConstCount: 2);
        var duplicateWitnesses = new TrRmInterfaceWitnessTableReader(
            duplicateWitnessesBytes, 1, typeSpecs, 0, 0, 2);
        var duplicateRejected = false;
        try
        {
            _ = TrRmWitnessAssociatedConstTableWriter.Encode([
                selectedBinding,
                selectedBinding,
            ], duplicateWitnesses, typeSpecs, types, interfaces, interfaceBases,
                associatedConsts, imports, constantSources,
                TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        }
        catch (InvalidDataException)
        {
            duplicateRejected = true;
        }
        Assert.That(duplicateRejected, Is.True);
    }

    [Test]
    public void GeneralProfileStructurallyValidatesWitnessAssociatedConstRows()
    {
        var fixture = CreateFixture();
        var schema = TrRmArtifactSchema.GeneralPredicateGraphsDraft;
        var strings = new TrRmStringTableReader(fixture.Strings, 4);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 1, 0, 0, 1);
        var featuresBytes = TrRmFeatureTableWriter.Encode(
            [TrRmFeatureRow.TypedFrozenValues], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 1, schema);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
        ], typeDefCount: 1, importCount: 0, genericParamCount: 0);
        var typeSpecs = new TrRmTypeSpecTableReader(
            typeSpecsBytes, 2, 1, 0, 0, 0);
        var types = new TrRmTypeDefTableReader(fixture.TypeDefs, 1,
            strings, modules, 0, 0, 1, 0);
        var profiles = new TrRmTargetProfileTableReader([], 0);
        var layouts = new TrRmLayoutTableReader(
            fixture.Layouts, 1, types, profiles, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var genericParams = new TrRmGenericParamTableReader([], 0,
            strings, types, methods, signatures, typeSpecs, 0, 1);
        var interfaces = new TrRmInterfaceDefTableReader(
            fixture.InterfaceDefs, 1, types, genericParams,
            0, 0, 0, 1, 1);
        var interfaceBases = new TrRmInterfaceBaseTableReader(
            [], 0, typeSpecs, interfaces);
        var associatedConsts = new TrRmAssociatedConstDefTableReader(
            fixture.AssociatedConstDefs, 1, strings, typeSpecs, interfaces);
        var interfaceWitnessesBytes = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(2, 2, 0, 0, 0, 0, 1, 1),
        ], typeSpecs, memberCount: 0, associatedTypeCount: 0,
            associatedConstCount: 1);
        var interfaceWitnesses = new TrRmInterfaceWitnessTableReader(
            interfaceWitnessesBytes, 1, typeSpecs, 0, 0, 1);
        var blobBytes = TrRmVariableTableWriter.EncodeBlobs([
            new ReadOnlyMemory<byte>([0, 0, 0, 0]),
        ]);
        var blobs = new TrRmBlobTableReader(blobBytes, 1);
        var frozenValueBytes = TrRmFrozenValueNodeTableWriter.Encode([
            new TrRmFrozenValueNodeRow(1, 1, 1, 0, 0, 0),
        ], typeSpecs, blobs, childCount: 0, unionCaseDefCount: 0);
        var constantSources = new TrRmConstantSourceTableReader(
            fixture.ConstantSources, 1, 1, 0, 0);
        var moduleRefs = new TrRmModuleRefTableReader([], 0, strings, modules);
        var imports = new TrRmImportTableReader([], 0, strings, moduleRefs,
            types, methods, default, interfaces, default, default,
            features, modules, schema, associatedConsts);
        var witnessAssociatedConstBytes = TrRmWitnessAssociatedConstTableWriter.Encode([
            new TrRmWitnessAssociatedConstRow(1, 2, 0x2500_0001, 1),
        ], interfaceWitnesses, typeSpecs, types, interfaces, interfaceBases,
            associatedConsts, imports, constantSources, schema);
        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, fixture.Modules),
            new(0x0002, 0, 4, fixture.Strings),
            new(0x0003, 0, 1, blobBytes),
            new(0x0004, 0, 1, featuresBytes),
            new(0x0011, 0, 2, typeSpecsBytes),
            new(0x0013, 0, 1, fixture.TypeDefs),
            new(0x0015, 0, 1, fixture.Layouts),
            new(0x0025, 0, 1, fixture.InterfaceDefs),
            new(0x0028, 0, 1, interfaceWitnessesBytes),
            new(0x0030, 0, 1, fixture.ConstantSources),
            new(0x0031, 0, 1, fixture.InterfaceConstDefs),
            new(0x0032, 0, 1, fixture.AssociatedConstDefs),
            new(0x0033, 0, 1, witnessAssociatedConstBytes),
            new(0x003F, 0, 1, frozenValueBytes),
        };
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(artifact));

        Assert.That(tables.WitnessAssociatedConsts.Count, Is.EqualTo(1));
        Assert.That(tables.WitnessAssociatedConsts.GetAssociatedConstDefReference(1),
            Is.EqualTo(0x2500_0001u));
        Assert.That(layouts.Count, Is.EqualTo(1));
    }

    [Test]
    public void GeneralProfileRejectsDuplicateWitnessBindingsAcrossEquivalentAnchors()
    {
        var singleBinding = CreateFunctionPointerWitnessArtifact(
            duplicateBinding: false);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        singleBinding)));

        var duplicateBinding = CreateFunctionPointerWitnessArtifact(
            duplicateBinding: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        duplicateBinding)));
    }

    private static byte[] CreateFunctionPointerWitnessArtifact(
        bool duplicateBinding)
    {
        var schema = TrRmArtifactSchema.GeneralPredicateGraphsDraft;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Interface", 0), ("Associated", 0),
            ("T", 0), ("anchor", 0), ("anchor2", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 7);
        TrRmFeatureRow[] featureRows = [
            TrRmFeatureRow.NativeBoundaryContracts,
            TrRmFeatureRow.RootlessNativeValues,
            TrRmFeatureRow.TypedFrozenValues,
        ];
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1,
                featureRows.Length),
        ], strings, moduleRefCount: 0, typeDefCount: 1, methodDefCount: 2,
            sourceFileCount: 0, featureCount: featureRows.Length);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 1, 2, 0, featureRows.Length);
        var typeSpecRows = new ReadOnlyMemory<byte>[] {
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.NativeFunctionPointer(1, 1),
            TrRmTypeSpecTableWriter.NativeFunctionPointer(1, 2),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Constructed(4, [2]),
            TrRmTypeSpecTableWriter.Constructed(4, [3]),
        };
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount: 1, importCount: 0, genericParamCount: 1,
            nativeFunctionPointerSignatureCount: 0, schema,
            methodDefCount: 2);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length, typeDefCount: 1, importCount: 0,
            genericParamCount: 1, nativeFunctionPointerSignatureCount: 0,
            schema, methodDefCount: 2);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 2, 14, 0, 0, 1, 1, 1, 1, 0, 0),
        ], strings, modules, fieldCount: 0, genericParamCount: 1,
            layoutCount: 1, contractCount: 0);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 1,
            strings, modules, 0, 1, 1, 0, schema);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], types, targetProfiles, layoutFieldCount: 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 0, 0, 0, 0),
            new TrRmMethodSigRow(1, 0, 0, 0, 0, 0),
        ], typeSpecs, paramCount: 0, genericParamCount: 1);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 2,
            typeSpecs, 0, 1);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 6, 1, 0, 0, 0, 0),
            new TrRmMethodDefRow(0, 7, 2, 0, 0, 0, 0),
        ], strings, modules, types, signatures, contractCount: 0, bodyCount: 0);
        var methods = new TrRmMethodDefTableReader(methodBytes, 2,
            strings, modules, types, signatures, 0, 0);
        var genericParamBytes = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1300_0001, 0, 5, 0, 0, 1, 1, 1, 0, 0),
        ], strings, types, methods, signatures, typeSpecs, constraintCount: 0);
        var genericParams = new TrRmGenericParamTableReader(genericParamBytes,
            1, strings, types, methods, signatures, typeSpecs, 0, 1);
        var interfaceDefBytes = TrRmInterfaceDefTableWriter.Encode([
            new TrRmInterfaceDefRow(1, 0, 0, 0, 0, 0, 0, 0,
                1, 1, 0, 0),
        ], types, genericParams, interfaceBaseCount: 0,
            interfaceMethodCount: 0, associatedTypeCount: 0,
            associatedConstCount: 1, interfaceConstCount: 0);
        var interfaces = new TrRmInterfaceDefTableReader(interfaceDefBytes,
            1, types, genericParams, 0, 0, 0, 1, 0);
        var interfaceBase = new TrRmInterfaceBaseTableReader(
            [], 0, typeSpecs, interfaces);
        var associatedConstBytes = TrRmAssociatedConstDefTableWriter.Encode([
            new TrRmAssociatedConstDefRow(1, 4, 1),
        ], strings, typeSpecs, interfaces);
        var associatedConsts = new TrRmAssociatedConstDefTableReader(
            associatedConstBytes, 1, strings, typeSpecs, interfaces);
        TrRmWitnessAssociatedConstRow[] bindingRows = duplicateBinding
            ? [
                new TrRmWitnessAssociatedConstRow(1, 5, 0x2500_0001, 1),
                new TrRmWitnessAssociatedConstRow(1, 6, 0x2500_0001, 1),
            ]
            : [
                new TrRmWitnessAssociatedConstRow(1, 5, 0x2500_0001, 1),
            ];
        var interfaceWitnessBytes = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(5, 5, 0, 0, 0, 0, 1,
                bindingRows.Length),
        ], typeSpecs, memberCount: 0, associatedTypeCount: 0,
            associatedConstCount: bindingRows.Length);
        var interfaceWitnesses = new TrRmInterfaceWitnessTableReader(
            interfaceWitnessBytes, 1, typeSpecs, 0, 0, bindingRows.Length);
        var blobBytes = TrRmVariableTableWriter.EncodeBlobs([
            new ReadOnlyMemory<byte>([0, 0, 0, 0]),
        ]);
        var blobs = new TrRmBlobTableReader(blobBytes, 1);
        var frozenValueBytes = TrRmFrozenValueNodeTableWriter.Encode([
            new TrRmFrozenValueNodeRow(1, 1, 1, 0, 0, 0),
        ], typeSpecs, blobs, childCount: 0, unionCaseDefCount: 0);
        var constantSourceBytes = TrRmConstantSourceTableWriter.Encode([
            new TrRmConstantSourceRow(1, 1),
        ], frozenValueNodeCount: 1, constantExecutionCount: 0,
            targetValueBindingCount: 0);
        var constantSources = new TrRmConstantSourceTableReader(
            constantSourceBytes, 1, 1, 0, 0);
        var witnessAssociatedConstBytes =
            TrRmWitnessAssociatedConstTableWriter.Encode(bindingRows,
                interfaceWitnesses, typeSpecs, types, interfaces, interfaceBase,
                associatedConsts, default, constantSources, schema);
        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, modulesBytes),
            new(0x0002, 0, 7, stringBytes),
            new(0x0003, 0, 1, blobBytes),
            new(0x0004, 0, (uint)featureRows.Length, featureBytes),
            new(0x0011, 0, (uint)typeSpecRows.Length, typeSpecBytes),
            new(0x0013, 0, 1, typeDefBytes),
            new(0x0015, 0, 1, layoutBytes),
            new(0x0016, 0, 2, methodBytes),
            new(0x0017, 0, 2, signatureBytes),
            new(0x0019, 0, 1, genericParamBytes),
            new(0x0025, 0, 1, interfaceDefBytes),
            new(0x0028, 0, 1, interfaceWitnessBytes),
            new(0x0030, 0, 1, constantSourceBytes),
            new(0x0032, 0, 1, associatedConstBytes),
            new(0x0033, 0, (uint)bindingRows.Length,
                witnessAssociatedConstBytes),
            new(0x003F, 0, 1, frozenValueBytes),
        };
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static Fixture CreateFixture()
    {
        var stringBytes = TrRmStringTableWriter.EncodeNames([
            ("Module", 0), ("Interface", 0), ("Associated", 0), ("Fixed", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 4);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1, 1, 1),
        ], strings, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 0, 0, 1);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            new byte[] { 1, 0, 8 },
        ], 0, 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1, 0, 0, 0, 0);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 2, 14, 0, 0, 0, 0, 1, 1, 0, 0),
        ], strings, modules, 0, 0, 1, 0);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 1,
            strings, modules, 0, 0, 1, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var generics = new TrRmGenericParamTableReader([], 0,
            strings, types, methods, signatures, typeSpecs, 0, 1);
        var interfaceDefBytes = TrRmInterfaceDefTableWriter.Encode([
            new TrRmInterfaceDefRow(1, 0, 0, 0, 0, 0,
                0, 0, 1, 1, 1, 1),
        ], types, generics, 0, 0, 0, 1, 1);
        var interfaces = new TrRmInterfaceDefTableReader(interfaceDefBytes, 1,
            types, generics, 0, 0, 0, 1, 1);
        var interfaceConstDefBytes = TrRmInterfaceConstDefTableWriter.Encode([
            new TrRmInterfaceConstDefRow(1, 4, 1, 1),
        ], strings, typeSpecs, interfaces, 1);
        var associatedConstDefBytes = TrRmAssociatedConstDefTableWriter.Encode([
            new TrRmAssociatedConstDefRow(1, 3, 1),
        ], strings, typeSpecs, interfaces);
        var constantSourceBytes = new byte[2 * sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(constantSourceBytes, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            constantSourceBytes.AsSpan(sizeof(uint)), 1);
        var fixture = new Fixture(stringBytes, moduleBytes, typeSpecBytes,
            typeDefBytes, layoutBytes, interfaceDefBytes, interfaceConstDefBytes,
            associatedConstDefBytes, constantSourceBytes);
        return fixture with
        {
            Artifact = CreateArtifact(fixture, interfaceDefBytes,
                interfaceConstDefBytes, associatedConstDefBytes),
        };
    }

    private static void AssertRows(Fixture fixture,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 4);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 1, 0, 0, 1);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs, 1, 0, 0, 0, 0);
        var types = new TrRmTypeDefTableReader(fixture.TypeDefs, 1,
            strings, modules, 0, 0, 1, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layouts = new TrRmLayoutTableReader(fixture.Layouts, 1,
            types, targetProfiles, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var generics = new TrRmGenericParamTableReader([], 0,
            strings, types, methods, signatures, typeSpecs, 0, 1);
        var interfaces = new TrRmInterfaceDefTableReader(fixture.InterfaceDefs,
            1, types, generics, 0, 0, 0, 1, 1, resourceLedger);
        var interfaceConsts = new TrRmInterfaceConstDefTableReader(
            fixture.InterfaceConstDefs, 1, strings, typeSpecs, interfaces, 1,
            resourceLedger);
        var associatedConsts = new TrRmAssociatedConstDefTableReader(
            fixture.AssociatedConstDefs, 1, strings, typeSpecs, interfaces,
            resourceLedger);

        Assert.That(interfaceConsts.GetOwnerInterfaceDef(1), Is.EqualTo(1));
        Assert.That(interfaceConsts.GetNameStringRow(1), Is.EqualTo(4));
        Assert.That(interfaceConsts.GetConstantTypeSpecRef(1), Is.EqualTo(1u));
        Assert.That(interfaceConsts.GetConstantSourceRow(1), Is.EqualTo(1));
        Assert.That(associatedConsts.GetOwnerInterfaceDef(1), Is.EqualTo(1));
        Assert.That(associatedConsts.GetNameStringRow(1), Is.EqualTo(3));
        Assert.That(associatedConsts.GetConstantTypeSpecRef(1), Is.EqualTo(1u));
        Assert.That(layouts.Count, Is.EqualTo(1));
    }

    private static void AssertInvalid(Fixture fixture, byte[] interfaceDefBytes,
        byte[] interfaceConstDefBytes, byte[] associatedConstDefBytes)
        => Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateArtifact(
                fixture, interfaceDefBytes, interfaceConstDefBytes,
                associatedConstDefBytes)));

    private static byte[] CreateArtifact(Fixture fixture, byte[] interfaceDefBytes,
        byte[] interfaceConstDefBytes, byte[] associatedConstDefBytes,
        bool includeFeature11 = true)
    {
        var blobBytes = TrRmVariableTableWriter.EncodeBlobs(
            [new byte[] { 0, 0, 0, 0 }]);
        var frozenValueBytes = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(frozenValueBytes, 1);
        frozenValueBytes[4] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(
            frozenValueBytes.AsSpan(8), 1);
        var sections = new List<TrRmSection> {
            new TrRmSection(0x0001, 0, 1, fixture.Modules),
            new TrRmSection(0x0002, 0, 4, fixture.Strings),
            new TrRmSection(0x0003, 0, 1, blobBytes),
        };
        if (includeFeature11)
        {
            sections.Add(new TrRmSection(0x0004, 0, 1,
                new byte[] { 11, 0, 0, 0, 1, 0, 0, 0 }));
        }
        sections.AddRange([
            new TrRmSection(0x0011, 0, 1, fixture.TypeSpecs),
            new TrRmSection(0x0013, 0, 1, fixture.TypeDefs),
            new TrRmSection(0x0015, 0, 1, fixture.Layouts),
            new TrRmSection(0x0025, 0, 1, interfaceDefBytes),
            new TrRmSection(0x0030, 0, 1, fixture.ConstantSources),
            new TrRmSection(0x0031, 0, 1, interfaceConstDefBytes),
            new TrRmSection(0x0032, 0, 1, associatedConstDefBytes),
            new TrRmSection(0x003F, 0, 1, frozenValueBytes),
        ]);
        return TrRmArtifactWriter.Encode(sections);
    }

    private sealed record Fixture(
        byte[] Strings,
        byte[] Modules,
        byte[] TypeSpecs,
        byte[] TypeDefs,
        byte[] Layouts,
        byte[] InterfaceDefs,
        byte[] InterfaceConstDefs,
        byte[] AssociatedConstDefs,
        byte[] ConstantSources)
    {
        public byte[] Artifact { get; init; } = [];
    }
}
