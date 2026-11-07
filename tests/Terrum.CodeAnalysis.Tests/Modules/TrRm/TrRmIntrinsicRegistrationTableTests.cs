// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmIntrinsicRegistrationTableTests
{
    private const TrRmArtifactSchema s_schema
        = TrRmArtifactSchema.GeneralPredicateGraphsDraft;

    [Test]
    public void ValidatesRegistrationIdentityImportsExportsAndFeatureClosure()
    {
        var artifact = CreateArtifact(includeConstantExecutionFeature: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(artifact));

        Assert.That(tables.IntrinsicRegistrations.Count, Is.EqualTo(3));
        Assert.That(tables.IntrinsicRegistrations.GetRegistrationId(1),
            Is.EqualTo(11u));
        Assert.That(tables.IntrinsicRegistrations.GetRuleRevision(2),
            Is.EqualTo(1u));
        Assert.That(tables.Imports.IsExpectedDefinition(0x2600_0002), Is.True);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        CreateArtifact(includeConstantExecutionFeature: false))));

        _ = Assert.Throws<InvalidDataException>(() =>
            CreateArtifact(includeConstantExecutionFeature: true,
                exportRegistrationRow: 2));
    }

    [Test]
    public void BindsExpectedRegistrationImportsToExactProviderIdentityAndRevision()
    {
        var consumer = CreateArtifact(includeConstantExecutionFeature: true);
        var provider = CreateProviderArtifact();
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftProviderBindings(
                    consumer, [provider]));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftProviderBindings(
                    consumer, [CreateProviderArtifact(registrationId: 12)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftProviderBindings(
                    consumer, [CreateProviderArtifact(ruleRevision: 2)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftProviderBindings(
                    consumer, [CreateProviderArtifact(firstExportKey: "other")]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftProviderBindings(
                    consumer, []));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftProviderBindings(
                    consumer, [provider, provider]));
    }

    [Test]
    public void RejectsMalformedOwnedAndExpectedRegistrationRows()
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 2);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 0, 0),
        ], strings, moduleRefCount: 0, typeDefCount: 0, methodDefCount: 0,
            sourceFileCount: 0, featureCount: 0);
        var modules = new TrRmModuleTableReader(
            moduleBytes, 1, strings, 0, 0, 0, 0, 0);

        var duplicateOwnedIdentityRejected = false;
        try
        {
            _ = TrRmIntrinsicRegistrationTableWriter.Encode([
                new TrRmIntrinsicRegistrationRow(1, 11, 1, 1, 2),
                new TrRmIntrinsicRegistrationRow(1, 11, 1, 1, 2),
            ], modules, s_schema);
        }
        catch (InvalidDataException)
        {
            duplicateOwnedIdentityRejected = true;
        }
        Assert.That(duplicateOwnedIdentityRejected, Is.True);

        var invalidExpectedVisibilityRejected = false;
        try
        {
            _ = TrRmIntrinsicRegistrationTableWriter.Encode([
                new TrRmIntrinsicRegistrationRow(1, 11, 1, 2, 1),
            ], modules, s_schema);
        }
        catch (InvalidDataException)
        {
            invalidExpectedVisibilityRejected = true;
        }
        Assert.That(invalidExpectedVisibilityRejected, Is.True);
    }

    [Test]
    public void RequiresFeature19ForRetainedExecutionDependenciesAndViews()
    {
        Assert.DoesNotThrow(() => ValidateConstantExecutionUse(
            includeFeature19: true, includeImmutableDependency: true,
            includeViewArgument: true));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateConstantExecutionUse(includeFeature19: false,
                includeImmutableDependency: true, includeViewArgument: false));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateConstantExecutionUse(includeFeature19: false,
                includeImmutableDependency: false, includeViewArgument: true));
        Assert.DoesNotThrow(() => ValidateConstantExecutionUse(
            includeFeature19: false, includeImmutableDependency: false,
            includeViewArgument: false));
    }

    private static void ValidateConstantExecutionUse(bool includeFeature19,
        bool includeImmutableDependency, bool includeViewArgument)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeNames([
            ("module", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 1);
        var featureRows = includeFeature19
            ? new[] {
                TrRmFeatureRow.PublishedPredicates,
                TrRmFeatureRow.TypedFrozenValues,
                TrRmFeatureRow.ConstantExecution,
                TrRmFeatureRow.GeneralPredicateGraphsCandidate,
                TrRmFeatureRow.ImmutableDataIdentitiesCandidate,
            }
            : [
                TrRmFeatureRow.PublishedPredicates,
                TrRmFeatureRow.TypedFrozenValues,
                TrRmFeatureRow.ConstantExecution,
                TrRmFeatureRow.GeneralPredicateGraphsCandidate,
            ];
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, s_schema);
        var features = new TrRmFeatureTableReader(featureBytes, featureRows.Length,
            s_schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1, 1, featureRows.Length),
        ], strings, moduleRefCount: 0, typeDefCount: 0, methodDefCount: 0,
            sourceFileCount: 0, featureCount: featureRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount: 0, typeDefCount: 0, methodDefCount: 0,
            sourceFileCount: 0, featureCount: featureRows.Length);
        TrRmFeatureManifestValidator.Validate(features, modules, s_schema);

        var registrationBytes = TrRmIntrinsicRegistrationTableWriter.Encode([
            new TrRmIntrinsicRegistrationRow(1, 17, 1, 1, 2),
        ], modules, s_schema);
        var registrations = new TrRmIntrinsicRegistrationTableReader(
            registrationBytes, 1, modules, s_schema);
        var typeSpecs = default(TrRmTypeSpecTableReader);
        var substitutions = new TrRmConstantExecutionSubstitutionTableReader(
            [], 0, 0, typeSpecs, s_schema);
        var pathEntries = new TrRmLogicalViewPathEntryTableReader([], 0,
            default, default, default);
        var views = TrRmLogicalInputViewTableWriter.Encode([
            new TrRmLogicalInputViewRow(2, 1, 0xF200_0008, 0, 0),
        ], frozenValueNodeCount: 0, immutableDataCount: 1,
            constantExecutionCount: 1, typeSpecs, pathEntries);
        var viewReader = new TrRmLogicalInputViewTableReader(views, 1,
            frozenValueNodeCount: 0, immutableDataCount: 1,
            constantExecutionCount: 1, typeSpecs, pathEntries);
        var arguments = includeViewArgument
            ? TrRmConstantExecutionArgumentTableWriter.Encode([
                new TrRmConstantExecutionArgumentRow(2, 1),
            ], frozenValueNodeCount: 0, logicalInputViewCount: 1,
                constantExecutionCount: 1)
            : [];
        var argumentReader = new TrRmConstantExecutionArgumentTableReader(
            arguments, includeViewArgument ? 1 : 0,
            frozenValueNodeCount: 0, logicalInputViewCount: 1,
            constantExecutionCount: 1);
        var dependencies = includeImmutableDependency
            ? TrRmConstantExecutionDependencyTableWriter.Encode([
                new TrRmConstantExecutionDependencyRow(6, 1),
            ], methodDefCount: 0, constraintCount: 0,
                interfaceWitnessCount: 0, witnessProjectionCount: 0,
                intrinsicRegistrationCount: 1, immutableDataCount: 1,
                fieldDefCount: 0, propertyDefCount: 0, unionCaseDefCount: 0)
            : [];
        var dependencyReader = new TrRmConstantExecutionDependencyTableReader(
            dependencies, includeImmutableDependency ? 1 : 0,
            methodDefCount: 0, constraintCount: 0,
            interfaceWitnessCount: 0, witnessProjectionCount: 0,
            intrinsicRegistrationCount: 1, immutableDataCount: 1,
            fieldDefCount: 0, propertyDefCount: 0, unionCaseDefCount: 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var targetLayouts = new TrRmTargetLayoutDependencyTableReader(
            [], 0, typeSpecs, targetProfiles, fieldReferenceCount: 0);
        var executions = TrRmConstantExecutionTableWriter.Encode([
            new TrRmConstantExecutionRow(2, 1, 0xF200_0008, 0,
                0, 0, includeViewArgument ? 1 : 0, includeViewArgument ? 1 : 0,
                includeImmutableDependency ? 1 : 0,
                includeImmutableDependency ? 1 : 0, 0, 0, 0, 0, 0, 0),
        ], bodyCount: 0, substitutions, default, argumentReader, viewReader,
            dependencyReader, targetLayouts, default, default, default, default, default,
            typeSpecs, targetProfiles, registrations, default, features, modules,
            s_schema);
        var executionReader = new TrRmConstantExecutionTableReader(
            executions, 1, bodyCount: 0, substitutions, default, argumentReader,
            viewReader, dependencyReader, targetLayouts, default, default, default, default,
            default, typeSpecs, targetProfiles, registrations, default,
            features, modules, s_schema);
        var uses = includeImmutableDependency || includeViewArgument
            ? [new TrRmConstantExecutionUse(1, 1, 0)]
            : Array.Empty<TrRmConstantExecutionUse>();
        TrRmConstantExecutionFeatureUseValidator.Validate(
            executionReader, argumentReader, dependencyReader, viewReader,
            uses, features, modules, s_schema);
    }

    private static byte[] CreateArtifact(bool includeConstantExecutionFeature,
        int exportRegistrationRow = 1)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("provider", 0),
            ("owned", 0), ("expected-a", 0), ("expected-b", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 6);
        var featureRows = includeConstantExecutionFeature
            ? new[] {
                TrRmFeatureRow.PublishedPredicates,
                TrRmFeatureRow.TypedFrozenValues,
                TrRmFeatureRow.ConstantExecution,
                TrRmFeatureRow.GeneralPredicateGraphsCandidate,
            }
            : [
                TrRmFeatureRow.PublishedPredicates,
                TrRmFeatureRow.TypedFrozenValues,
                TrRmFeatureRow.GeneralPredicateGraphsCandidate,
            ];
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, s_schema);
        var featureCount = featureRows.Length;
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1, featureCount),
        ], strings, moduleRefCount: 1, typeDefCount: 0, methodDefCount: 0,
            sourceFileCount: 0, featureCount);
        var modules = new TrRmModuleTableReader(
            modulesBytes, 1, strings, 1, 0, 0, 0, featureCount);
        var moduleRefsBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(3, 0, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(
            moduleRefsBytes, 1, strings, modules);
        var registrationsBytes = TrRmIntrinsicRegistrationTableWriter.Encode([
            new TrRmIntrinsicRegistrationRow(1, 11, 1, 1, 2),
            new TrRmIntrinsicRegistrationRow(1, 11, 1, 2, 2),
            new TrRmIntrinsicRegistrationRow(1, 11, 1, 2, 2),
        ], modules, s_schema);
        var registrations = new TrRmIntrinsicRegistrationTableReader(
            registrationsBytes, 3, modules, s_schema);
        var importsBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 5, 0x2600_0002),
            new TrRmImportRow(1, 6, 0x2600_0003),
        ], strings, moduleRefs, default, default, default,
            default, default, default, new TrRmFeatureTableReader(
                featureBytes, featureCount, s_schema), modules, s_schema,
            intrinsicRegistrations: registrations);
        var imports = new TrRmImportTableReader(importsBytes, 2,
            strings, moduleRefs, default, default, default,
            default, default, default,
            new TrRmFeatureTableReader(featureBytes, featureCount, s_schema),
            modules, s_schema, intrinsicRegistrations: registrations);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(
                0x2600_0000u | (uint)exportRegistrationRow, 4),
        ], strings, default, default, default, imports,
            default, default, default,
            new TrRmFeatureTableReader(featureBytes, featureCount, s_schema),
            modules, s_schema, intrinsicRegistrations: registrations);
        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, modulesBytes),
            new(0x0002, 0, 6, stringBytes),
            new(0x0004, 0, (uint)featureCount, featureBytes),
            new(0x0010, 0, 1, moduleRefsBytes),
            new(0x001C, 0, 2, importsBytes),
            new(0x001D, 0, 1, exportsBytes),
            new(0x005D, 0, 3, registrationsBytes),
        };
        return TrRmArtifactWriter.Encode(sections, s_schema);
    }

    private static byte[] CreateProviderArtifact(uint registrationId = 11,
        uint ruleRevision = 1, string firstExportKey = "expected-a",
        string secondExportKey = "expected-b")
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("provider", 0), (firstExportKey, 0), (secondExportKey, 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 4);
        var featureRows = new[]
        {
            TrRmFeatureRow.PublishedPredicates,
            TrRmFeatureRow.TypedFrozenValues,
            TrRmFeatureRow.ConstantExecution,
            TrRmFeatureRow.GeneralPredicateGraphsCandidate,
        };
        Array.Sort(featureRows, static (left, right)
            => left.FeatureId.CompareTo(right.FeatureId));
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, s_schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1, featureRows.Length),
        ], strings, moduleRefCount: 0, typeDefCount: 0, methodDefCount: 0,
            sourceFileCount: 0, featureCount: featureRows.Length);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            moduleRefCount: 0, typeDefCount: 0, methodDefCount: 0,
            sourceFileCount: 0, featureCount: featureRows.Length);
        var moduleRefsBytes = Array.Empty<byte>();
        var moduleRefs = new TrRmModuleRefTableReader(
            moduleRefsBytes, 0, strings, modules);
        var features = new TrRmFeatureTableReader(
            featureBytes, featureRows.Length, s_schema);
        var registrationsBytes = TrRmIntrinsicRegistrationTableWriter.Encode([
            new TrRmIntrinsicRegistrationRow(1, registrationId, ruleRevision, 1, 2),
        ], modules, s_schema);
        var registrations = new TrRmIntrinsicRegistrationTableReader(
            registrationsBytes, 1, modules, s_schema);
        var importsBytes = Array.Empty<byte>();
        var imports = new TrRmImportTableReader(importsBytes, 0,
            strings, moduleRefs, default, default, default, default, default,
            default, features, modules, s_schema,
            intrinsicRegistrations: registrations);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x2600_0001, 3),
            new TrRmExportRow(0x2600_0001, 4),
        ], strings, default, default, default, imports,
            default, default, default, features, modules, s_schema,
            intrinsicRegistrations: registrations);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 4, stringsBytes),
            new TrRmSection(0x0004, 0, (uint)featureRows.Length, featureBytes),
            new TrRmSection(0x001D, 0, 2, exportsBytes),
            new TrRmSection(0x005D, 0, 1, registrationsBytes),
        ], s_schema);
    }
}
