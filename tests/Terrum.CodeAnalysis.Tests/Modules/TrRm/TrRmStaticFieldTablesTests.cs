// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmStaticFieldTablesTests
{
    [Test]
    public void ReadsStaticFieldsGroupsContractsAndOrderedInitializers()
    {
        var fixture = CreateTypeFixture();
        var groups = TrRmEagerGroupTableWriter.Encode([
            new TrRmEagerGroupRow(1, 1, 1, 1, 2, 0),
        ], typeDefCount: 1, initializerCount: 2, methodDefCount: 2,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var groupReader = new TrRmEagerGroupTableReader(groups, 1,
            typeDefCount: 1, initializerCount: 2, methodDefCount: 2,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var fields = TrRmStaticFieldTableWriter.Encode([
            new TrRmStaticFieldRow(1, 4, 0xF200_0008, 0, 1),
            new TrRmStaticFieldRow(1, 5, 0xF200_0008, 1, 1),
        ], fixture.Strings, fixture.TypeDefs, default, groupReader,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var fieldReader = new TrRmStaticFieldTableReader(fields, 2,
            fixture.Strings, fixture.TypeDefs, default, groupReader,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var contracts = TrRmStaticFieldContractTableWriter.Encode([
            new TrRmStaticFieldContractRow(1, 1, 2, 0, 0, 0, 1),
            new TrRmStaticFieldContractRow(2, 1, 2, 0, 1, 0, 0),
        ], staticFieldCount: 2, lazyUnitCount: 0, publicationCount: 1,
            fieldReader, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var contractReader = new TrRmStaticFieldContractTableReader(
            contracts, 2, 2, 0, 1, fieldReader,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var initializers = TrRmEagerInitializerTableWriter.Encode([
            new TrRmEagerInitializerRow(1, 1, 2, 2),
            new TrRmEagerInitializerRow(1, 0, 1, 1),
        ], groupReader, fieldReader, contractReader, methodDefCount: 2,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var initializerReader = new TrRmEagerInitializerTableReader(
            initializers, 2, groupReader, fieldReader, contractReader, 2,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var lazyUnits = TrRmLazyUnitTableWriter.Encode(
            Array.Empty<TrRmLazyUnitRow>(), groupReader, initializerReader,
            fieldReader, contractReader,
            methodDefCount: 2, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var lazyReader = new TrRmLazyUnitTableReader(
            lazyUnits, 0, groupReader, initializerReader, fieldReader,
            contractReader, 2,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var constantSources = default(TrRmConstantSourceTableReader);
        var publications = TrRmStaticImmutablePublicationTableWriter.Encode([
            new TrRmStaticImmutablePublicationRow(1, 1, 1, 1, 1,
                0xF200_0008, 1, 0),
        ], fieldReader, contractReader, default, lazyReader, constantSources,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var publicationReader = new TrRmStaticImmutablePublicationTableReader(
            publications, 1, fieldReader, contractReader, default, lazyReader,
            constantSources, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var dependencies = TrRmStaticDependencyTableWriter.Encode(
            Array.Empty<TrRmStaticDependencyRow>(), default,
            fieldReader, contractReader, groupReader, publicationReader.Count,
            default, fixture.TypeDefs, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var dependencyReader = new TrRmStaticDependencyTableReader(
            dependencies, 0, default, fieldReader, contractReader, groupReader,
            publicationReader.Count, default, fixture.TypeDefs,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var accesses = TrRmStaticAccessTableWriter.Encode([
            new TrRmStaticAccessRow(1, 1, 1, 0),
        ], fieldReader, contractReader, dependencyReader, default,
            fixture.TypeDefs, default, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var accessReader = new TrRmStaticAccessTableReader(accesses, 1,
            fieldReader, contractReader, dependencyReader, default,
            fixture.TypeDefs, default,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);

        Assert.That(fieldReader.GetFlags(2), Is.EqualTo(1u));
        Assert.That(contractReader.GetDeclarationOrdinal(2), Is.EqualTo(1u));
        Assert.That(initializerReader.GetOrdinal(1), Is.EqualTo(1u));
        Assert.That(initializerReader.GetStaticFieldRow(2), Is.EqualTo(1));
        Assert.That(lazyReader.Count, Is.Zero);
        Assert.That(publicationReader.GetValueCount(1), Is.EqualTo(1ul));
        Assert.That(publicationReader.GetPublicationRowForStaticField(1), Is.EqualTo(1));
        Assert.That(accessReader.GetTargetRow(1), Is.EqualTo(1u));
    }

    [Test]
    public void RejectsInvalidStaticTagsRangesOwnershipAndOrdinals()
    {
        var fixture = CreateTypeFixture();
        var groups = TrRmEagerGroupTableWriter.Encode([
            new TrRmEagerGroupRow(1, 1, 1, 1, 1, 0),
        ], 1, 1, 1, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var groupReader = new TrRmEagerGroupTableReader(
            groups, 1, 1, 1, 1, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var fields = TrRmStaticFieldTableWriter.Encode([
            new TrRmStaticFieldRow(1, 4, 0xF200_0008, 0, 1),
        ], fixture.Strings, fixture.TypeDefs, default, groupReader,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var fieldReader = new TrRmStaticFieldTableReader(
            fields, 1, fixture.Strings, fixture.TypeDefs, default, groupReader,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var validContracts = TrRmStaticFieldContractTableWriter.Encode([
            new TrRmStaticFieldContractRow(1, 1, 2, 0, 0, 0, 0),
        ], 1, 0, 0, fieldReader, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var contractReader = new TrRmStaticFieldContractTableReader(
            validContracts, 1, 1, 0, 0, fieldReader,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmEagerGroupTableWriter.Encode([
                new TrRmEagerGroupRow(1, 3, 1, 0, 0, 0),
            ], 1, 0, 0, TrRmArtifactSchema.GeneralPredicateGraphsDraft));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmEagerGroupTableWriter.Encode([
                new TrRmEagerGroupRow(1, 2, 1, 0, 0, 0),
            ], 1, 0, 0, TrRmArtifactSchema.GeneralPredicateGraphsDraft));
        var invalidFieldFlagsRejected = false;
        try
        {
            _ = TrRmStaticFieldTableWriter.Encode([
                new TrRmStaticFieldRow(1, 4, 0xF200_0008, 2, 1),
            ], fixture.Strings, fixture.TypeDefs, default, groupReader,
                TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        }
        catch (InvalidDataException)
        {
            invalidFieldFlagsRejected = true;
        }
        Assert.That(invalidFieldFlagsRejected, Is.True);

        var invalidDeclarationOrdinalsRejected = false;
        try
        {
            _ = TrRmStaticFieldContractTableWriter.Encode([
                new TrRmStaticFieldContractRow(1, 1, 2, 0, 1, 0, 0),
            ], 1, 0, 0, fieldReader,
                TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        }
        catch (InvalidDataException)
        {
            invalidDeclarationOrdinalsRejected = true;
        }
        Assert.That(invalidDeclarationOrdinalsRejected, Is.True);

        var invalidInitializerOrdinalRejected = false;
        try
        {
            _ = TrRmEagerInitializerTableWriter.Encode([
                new TrRmEagerInitializerRow(1, 1, 1, 1),
            ], groupReader, fieldReader, contractReader, 1,
                TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        }
        catch (InvalidDataException)
        {
            invalidInitializerOrdinalRejected = true;
        }
        Assert.That(invalidInitializerOrdinalRejected, Is.True);
    }

    [Test]
    public void ResolvesStaticFieldImportsDependenciesAccessesAndExports()
    {
        var stringsBytes = TrRmStringTableWriter.EncodeNames([
            ("module", 0), ("Type", 0), ("Field", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 3);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.GeneralPredicateGraphsCandidate,
        ], TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var features = new TrRmFeatureTableReader(
            featuresBytes, 1, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1, 1, 1),
        ], strings, moduleRefCount: 1, typeDefCount: 1, methodDefCount: 0,
            sourceFileCount: 0, featureCount: 1);
        var modules = new TrRmModuleTableReader(
            modulesBytes, 1, strings, 1, 1, 0, 0, 1);
        var moduleRefsBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(1, 0, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(
            moduleRefsBytes, 1, strings, modules);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 2, 6, 0, 0, 0, 0, 0, 0, 0, 0),
        ], strings, modules, fieldCount: 0, genericParamCount: 0,
            layoutCount: 0, contractCount: 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 1, strings,
            modules, 0, 0, 0, 0, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var groupsBytes = TrRmEagerGroupTableWriter.Encode([
            new TrRmEagerGroupRow(1, 1, 1, 0, 0, 0),
        ], 1, 0, 0, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var groups = new TrRmEagerGroupTableReader(
            groupsBytes, 1, 1, 0, 0, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var fieldsBytes = TrRmStaticFieldTableWriter.Encode([
            new TrRmStaticFieldRow(1, 3, 0xF200_0008, 0, 1),
        ], strings, typeDefs, default, groups,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var fields = new TrRmStaticFieldTableReader(fieldsBytes, 1, strings,
            typeDefs, default, groups, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var expectedContractsBytes = TrRmStaticFieldContractTableWriter.Encode([
            new TrRmStaticFieldContractRow(1, 2, 2, 0, 0, 0, 0),
        ], 1, 0, 0, fields, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var expectedContracts = new TrRmStaticFieldContractTableReader(
            expectedContractsBytes, 1, 1, 0, 0, fields,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var methods = default(TrRmMethodDefTableReader);
        var properties = default(TrRmPropertyDefTableReader);
        var importsBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 2, 0x1300_0001),
            new TrRmImportRow(1, 3, 0x2400_0001),
        ], strings, moduleRefs, typeDefs, methods, properties,
            default, default, default, features, modules,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft,
            default, fields, expectedContracts);
        var imports = new TrRmImportTableReader(importsBytes, 2,
            strings, moduleRefs, typeDefs, methods, properties,
            default, default, default, features, modules,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft,
            default, fields, expectedContracts);
        var dependencyBytes = TrRmStaticDependencyTableWriter.Encode([
            new TrRmStaticDependencyRow(2, 0, 1, 0),
        ], imports, fields, expectedContracts, groups, 0, default, typeDefs,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var dependencies = new TrRmStaticDependencyTableReader(
            dependencyBytes, 1, imports, fields, expectedContracts, groups,
            0, default, typeDefs, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var accessBytes = TrRmStaticAccessTableWriter.Encode([
            new TrRmStaticAccessRow(2, 1, 1, 0),
        ], fields, expectedContracts, dependencies, default, typeDefs, imports,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var accesses = new TrRmStaticAccessTableReader(accessBytes, 1, fields,
            expectedContracts, dependencies, default, typeDefs, imports,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);

        var ownedContractsBytes = TrRmStaticFieldContractTableWriter.Encode([
            new TrRmStaticFieldContractRow(1, 1, 2, 0, 0, 0, 0),
        ], 1, 0, 0, fields, TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var ownedContracts = new TrRmStaticFieldContractTableReader(
            ownedContractsBytes, 1, 1, 0, 0, fields,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
        var noImports = new TrRmImportTableReader([], 0, strings, moduleRefs,
            typeDefs, methods, properties);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x2400_0001, 3),
        ], strings, typeDefs, methods, properties, noImports,
            default, default, default, features, modules,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft,
            default, fields, ownedContracts);
        var exports = new TrRmExportTableReader(exportsBytes, 1,
            strings, typeDefs, methods, properties, noImports,
            default, default, default, features, modules,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft,
            default, fields, ownedContracts);

        Assert.That(imports.IsExpectedDefinition(0x2400_0001), Is.True);
        Assert.That(dependencies.GetFieldImportRow(1), Is.EqualTo(2));
        Assert.That(accesses.GetTargetKind(1), Is.EqualTo((byte)2));
        var importedExpectationExportRejected = false;
        try
        {
            _ = TrRmExportTableWriter.Encode([
                new TrRmExportRow(0x2400_0001, 3),
            ], strings, typeDefs, methods, properties, noImports,
                default, default, default, features, modules,
                TrRmArtifactSchema.GeneralPredicateGraphsDraft,
                default, fields, expectedContracts);
        }
        catch (InvalidDataException)
        {
            importedExpectationExportRejected = true;
        }
        Assert.That(importedExpectationExportRejected, Is.True);
        Assert.That(exportsBytes.Length, Is.EqualTo(8));
        Assert.That(exports.HasExportForMethod(1), Is.False);
    }

    private static TypeFixture CreateTypeFixture()
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("Type", 0), ("FieldA", 0), ("FieldB", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 5);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 0, 0),
        ], strings, moduleRefCount: 0, typeDefCount: 1, methodDefCount: 0,
            sourceFileCount: 0, featureCount: 0);
        var modules = new TrRmModuleTableReader(
            moduleBytes, 1, strings, 0, 1, 0, 0, 0);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 4, 0, 0, 0, 0, 1, 1, 0, 0),
        ], strings, modules, fieldCount: 0, genericParamCount: 0,
            layoutCount: 1, contractCount: 0);
        var typeDefs = new TrRmTypeDefTableReader(
            typeDefBytes, 1, strings, modules, 0, 0, 1, 0);
        return new TypeFixture(strings, typeDefs);
    }

    private readonly ref struct TypeFixture
    {
        internal TypeFixture(TrRmStringTableReader strings,
            TrRmTypeDefTableReader typeDefs)
        {
            Strings = strings;
            TypeDefs = typeDefs;
        }

        internal TrRmStringTableReader Strings { get; }
        internal TrRmTypeDefTableReader TypeDefs { get; }
    }
}
