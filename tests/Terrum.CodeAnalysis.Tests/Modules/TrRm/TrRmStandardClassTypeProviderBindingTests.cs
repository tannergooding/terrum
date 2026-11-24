// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmStandardClassTypeProviderBindingTests
{
    private const string C1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1";
    private const string C1M1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";
    private const string C1M1Construction2ClassParameterDefaultsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1";
    private const string C1M1Construction2PropertyInitializedStateSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1";

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews,
        StandardGenericMethodSchema.C1M1Construction2TypeBindingViews,
        StandardGenericMethodSchema.C1M1Construction2InitializerCopyEvidence)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews,
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsTypeBindingViews,
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidence)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews,
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsTypeBindingViews,
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidence)]
    public void ReadsClassTypeDefEqualityOnlyInItsExactSuccessor(
        string classSchemaIdentity, string classlessSchemaIdentity,
        string parentSchemaIdentity)
    {
        var schema = TrRmExecutableCompiler.GetForeignInvocationSchema(
            classSchemaIdentity);
        var artifact = CreateRelocatedClassProvider(
            "TypeBindingClassProvider", classSchemaIdentity);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(classSchemaIdentity, artifact),
            "TypeBindingClassProvider", ["PrefixKey", "LeafKey"]);
        Assert.That(reference.StandardClassDeclarations.Select(
                static declaration => declaration.Declaration.Name),
            Is.EquivalentTo(["Leaf", "Prefix"]));
        var sourceTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                artifact, schema, null,
                standardSchemaIdentity: classSchemaIdentity));
        var prefixDefinition = sourceTables.Exports.GetExportsForModule(1)
            .Single(static export => export.ExportKey == "PrefixKey")
            .Definition;
        var leafDefinition = sourceTables.Exports.GetExportsForModule(1)
            .Single(static export => export.ExportKey == "LeafKey")
            .Definition;
        Assert.That(sourceTables.TypeDefs.GetKind(
                checked((int)(prefixDefinition & 0x00FF_FFFF))),
            Is.EqualTo(2));
        Assert.That(sourceTables.TypeDefs.GetKind(
                checked((int)(leafDefinition & 0x00FF_FFFF))),
            Is.EqualTo(2));

        var equalityBytes = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(equalityBytes, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            equalityBytes.AsSpan(4, 4),
            prefixDefinition & 0x00FF_FFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(
            equalityBytes.AsSpan(8, 4),
            leafDefinition & 0x00FF_FFFF);
        var sourceIndex = new TrRmArtifactIndex(artifact, schema, null,
            standardSchemaIdentity: classSchemaIdentity);
        var sections = new List<TrRmSection>(sourceIndex.SectionCount + 1);
        var insertedEqualitySection = false;
        for (var sectionIndex = 0;
            sectionIndex < sourceIndex.SectionCount; sectionIndex++)
        {
            sourceIndex.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            if (!insertedEqualitySection && kind > 0x0067)
            {
                sections.Add(new TrRmSection(0x0067, 0, 1,
                    equalityBytes));
                insertedEqualitySection = true;
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount), bytes.ToArray()));
        }
        if (!insertedEqualitySection)
        {
            sections.Add(new TrRmSection(0x0067, 0, 1, equalityBytes));
        }

        var artifactWithEquality = TrRmArtifactWriter.Encode(sections,
            schema, standardSchemaIdentity: classSchemaIdentity);
        var equalityTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                artifactWithEquality, schema, null,
                standardSchemaIdentity: classSchemaIdentity));
        Assert.That(equalityTables.TypeDefBindingEqualities.Count,
            Is.EqualTo(1));
        Assert.That(equalityTables.TypeDefBindingEqualities.GetOwnerModule(1),
            Is.EqualTo(1u));
        Assert.That(equalityTables.TypeDefs.GetKind(checked((int)
                equalityTables.TypeDefBindingEqualities
                    .GetLeftTypeDef(1))),
            Is.EqualTo(2));
        Assert.That(equalityTables.TypeDefs.GetKind(checked((int)
                equalityTables.TypeDefBindingEqualities
                    .GetRightTypeDef(1))),
            Is.EqualTo(2));
        byte[] EncodeEqualityRow(uint ownerModule, uint leftTypeDef,
            uint rightTypeDef)
        {
            var bytes = (byte[])equalityBytes.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, ownerModule);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4),
                leftTypeDef);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4),
                rightTypeDef);
            var rewrittenSections = sections.Select(section =>
                section.Kind == 0x0067
                    ? new TrRmSection(0x0067, 0, 1, bytes)
                    : section).ToArray();
            return TrRmArtifactWriter.Encode(rewrittenSections, schema,
                standardSchemaIdentity: classSchemaIdentity);
        }

        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    EncodeEqualityRow(0, prefixDefinition & 0x00FF_FFFF,
                        leafDefinition & 0x00FF_FFFF),
                    schema, null,
                    standardSchemaIdentity: classSchemaIdentity)));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    EncodeEqualityRow(1,
                        prefixDefinition & 0x00FF_FFFF, 0),
                    schema, null,
                    standardSchemaIdentity: classSchemaIdentity)));

        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(artifactWithEquality, schema,
                        null,
                        standardSchemaIdentity:
                            classlessSchemaIdentity)));
        Assert.Throws<InvalidDataException>(() =>
            new TrRmArtifactIndex(artifactWithEquality, schema, null,
                standardSchemaIdentity: parentSchemaIdentity));
        Assert.Throws<InvalidDataException>(() =>
            new TrRmArtifactIndex(artifactWithEquality, schema));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void RejectsMixedClassAndStructEqualityEndpoints(
        string schemaIdentity)
    {
        var provider = CreateRelocatedClassProvider("Provider",
            schemaIdentity);
        SetTypeDefFlags(provider, 0x06, typeDefRow: 2);
        var tables = ReadTables(provider, schemaIdentity);
        var prefixDefinition = tables.Exports.GetExportsForModule(1)
            .Single(static export => export.ExportKey == "PrefixKey")
            .Definition;
        var leafDefinition = tables.Exports.GetExportsForModule(1)
            .Single(static export => export.ExportKey == "LeafKey")
            .Definition;
        var artifact = AddTypeDefBindingEquality(provider, schemaIdentity, 1,
            prefixDefinition & 0x00FF_FFFF,
            leafDefinition & 0x00FF_FFFF);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "Provider", [], ["PrefixKey", "LeafKey"]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ComposesDistinctClassViewsOfOneProviderType(
        string schemaIdentity)
    {
        var provider = CreateClassArtifact("Provider", "Leaf", "LeafKey",
            0xF200_0008, schemaIdentity);
        var rootA = CreateRootArtifact("RootA", "BoxA", "BoxAKey",
            0xF200_0008, schemaIdentity);
        var rootB = CreateRootArtifact("RootB", "BoxB", "BoxBKey",
            0xF200_0008, schemaIdentity);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            provider);
        var referenceA = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootA), "RootA",
            [providerInput], ["BoxAKey"]);
        var referenceB = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootB), "RootB",
            [providerInput], ["BoxBKey"]);

        var composed = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [referenceA, referenceB]);
        var leaf = composed.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Leaf");
        Assert.That(leaf.Declaration.Identity.ModuleIdentity,
            Is.EqualTo("Provider"));
        Assert.That(leaf.Expectations, Has.Length.EqualTo(2));
        Assert.That(leaf.Expectations.Select(
                static expectation =>
                    expectation.Binding.UsingModule.ModuleIdentity),
            Is.EquivalentTo(["RootA", "RootB"]));
        Assert.That(leaf.Expectations.Select(
                static expectation =>
                    expectation.Binding.ImportRow),
            Is.All.EqualTo(1));
        Assert.That(leaf.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));
        Assert.That(leaf.TypeDefBindingEqualityGroups[0].Endpoints,
            Has.Length.EqualTo(2));
        Assert.That(leaf.TypeDefBindingEqualityGroups[0].Endpoints.Select(
                static endpoint => endpoint.OwnerModule.ModuleIdentity),
            Is.EquivalentTo(["RootA", "RootB"]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ComposesDirectStructFieldDependenciesForClassTypeViews(
        string schemaIdentity)
    {
        var rootA = CreateArtifact("RootA", "BoxA", "BoxAKey", 0,
            schemaIdentity, null, null, null,
            localDependencyTypeName: "Leaf");
        var rootB = CreateArtifact("RootB", "BoxB", "BoxBKey", 0,
            schemaIdentity, null, null, null,
            localDependencyTypeName: "Leaf");
        SetTypeDefFlags(rootA, 0x06, typeDefRow: 2);
        SetTypeDefFlags(rootB, 0x06, typeDefRow: 2);

        var referenceA = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootA), "RootA",
            [], ["BoxAKey"]);
        var referenceB = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootB), "RootB",
            [], ["BoxBKey"]);
        var composed = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [referenceA, referenceB]);
        var leaves = composed.GenericNominalTypeDefinitions.Where(
            static definition => definition.Name == "Leaf").ToArray();
        Assert.That(leaves.Select(static definition =>
                definition.Identity.ModuleIdentity),
            Is.EquivalentTo(["RootA", "RootB"]));
        Assert.That(leaves.All(static definition =>
                definition.NominalKind == 1),
            Is.True);
        Assert.That(composed.StandardClassDeclarations.Select(
                static declaration => declaration.Declaration.Name),
            Is.EquivalentTo(["BoxA", "BoxB"]));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public void Use(BoxA first, BoxB second) {}"), composed);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ComposesStructOnlyRootWithClassFieldExpectation(
        string schemaIdentity)
    {
        var provider = CreateClassArtifact("Provider", "Leaf", "LeafKey",
            0xF200_0008, schemaIdentity);
        SetTypeDefFlags(provider, 0x06);
        var root = CreateRootArtifact("Root", "Box", "BoxKey",
            0xF200_0008, schemaIdentity);
        var classReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, root), "Root",
            [new TrRmStandardArtifactInput(schemaIdentity, provider)],
            ["BoxKey"]);
        var structReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, provider),
            "Provider", [], ["LeafKey"]);

        var composed = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [classReference, structReference]);
        var leaf = composed.GenericNominalTypeDefinitions.Single(
            static definition => definition.Name == "Leaf");
        Assert.That(leaf.NominalKind, Is.EqualTo(1));
        Assert.That(leaf.SelectedExportKeys, Is.EqualTo(["LeafKey"]));
        Assert.That(leaf.ProviderExpectations, Has.Length.EqualTo(1));
        Assert.That(leaf.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));
        Assert.That(leaf.TypeDefBindingEqualityGroups[0].Endpoints,
            Has.Length.EqualTo(2));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public void Use(Box box, Leaf leaf) {}"), composed);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ComposesNestedDirectStructClosureFromStructRoot(
        string schemaIdentity)
    {
        var classProvider = CreateClassArtifact("ClassProvider", "Box",
            "BoxKey", 0xF200_0008, schemaIdentity);
        var structProvider = CreateArtifact("StructProvider", "Leaf",
            "LeafKey", 0, schemaIdentity, null, null, null,
            localDependencyTypeName: "Inner");
        SetTypeDefFlags(structProvider, 0x06, typeDefRow: 1);
        SetTypeDefFlags(structProvider, 0x06, typeDefRow: 2);

        var classReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    classProvider),
                "ClassProvider", [], ["BoxKey"]);
        var structReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    structProvider),
                "StructProvider", [], ["LeafKey"]);
        var composed = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [classReference, structReference]);
        var structDefinitions = composed.GenericNominalTypeDefinitions
            .Where(static definition => definition.NominalKind == 1)
            .ToArray();
        Assert.That(structDefinitions.Select(
                static definition => definition.Name),
            Is.EquivalentTo(["Leaf", "Inner"]));
        Assert.That(structDefinitions.Single(
                static definition => definition.Name == "Leaf")
                .SelectedExportKeys,
            Is.EqualTo(["LeafKey"]));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public void Use(Box box, Leaf leaf) {}"), composed);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ComposesImportedStructViewWithItsDirectProviderView(
        string schemaIdentity)
    {
        var provider = CreateArtifact("Provider", "Leaf", "LeafKey", 0,
            schemaIdentity, null, null, null,
            layoutKind: 0, pack: 0,
            localDependencyTypeName: "Inner",
            localDependencyExportKey: "InnerKey");
        SetTypeDefFlags(provider, 0x06, typeDefRow: 2);
        var root = CreateRootArtifactWithNestedStructExpectation("Root",
            "Provider", "LeafKey", "InnerKey", "BoxKey", schemaIdentity);
        var classReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, root), "Root",
            [new TrRmStandardArtifactInput(schemaIdentity, provider)],
            ["BoxKey"]);
        var structReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, provider),
            "Provider", [], ["InnerKey"]);

        var composed = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [classReference, structReference]);
        var inner = composed.GenericNominalTypeDefinitions.Single(
            static definition => definition.Name == "Inner");
        Assert.That(inner.NominalKind, Is.EqualTo(1));
        Assert.That(inner.ProviderExpectations, Has.Length.EqualTo(1));
        Assert.That(inner.SelectedExportKeys, Is.EqualTo(["InnerKey"]));
        Assert.That(inner.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));
        Assert.That(inner.TypeDefBindingEqualityGroups[0].Endpoints,
            Has.Length.EqualTo(2));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public void Use(Box box, Inner inner) {}"), composed);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), composed);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "RootReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = ReadTables(reemitted, schemaIdentity);
        var rootModuleRow = Enumerable.Range(1, reemittedTables.Modules.Count)
            .Single(row => reemittedTables.Modules.GetIdentity(row)
                == "RootReemitted");
        var providerModuleRow
            = Enumerable.Range(1, reemittedTables.Modules.Count)
                .Single(row => reemittedTables.Modules.GetIdentity(row)
                    == "Provider");
        var leafRows = Enumerable.Range(1,
                reemittedTables.TypeDefs.Count)
            .Where(row => reemittedTables.TypeDefs.GetName(row) == "Leaf")
            .ToArray();
        Assert.That(leafRows, Has.Length.EqualTo(2));
        Assert.That(leafRows.Any(row =>
                reemittedTables.TypeDefs.GetOwnerModule(row)
                    == (uint)rootModuleRow),
            Is.True);
        Assert.That(leafRows.Any(row =>
                reemittedTables.TypeDefs.GetOwnerModule(row)
                    == (uint)providerModuleRow),
            Is.True);
        var innerRow = Enumerable.Range(1,
                reemittedTables.TypeDefs.Count)
            .Single(row => reemittedTables.TypeDefs.GetName(row) == "Inner");
        Assert.That(reemittedTables.TypeDefs.GetOwnerModule(innerRow),
            Is.EqualTo((uint)providerModuleRow));

        var reemittedInput = new TrRmStandardArtifactInput(
            schemaIdentity, reemitted);
        var reloadedClass = TrRmExecutableCompiler.CreateStandardReference(
            reemittedInput, "RootReemitted", [], ["BoxKey"]);
        var reloadedStruct = TrRmExecutableCompiler.CreateStandardReference(
            reemittedInput, "Provider", [], ["InnerKey"]);
        var reloadedReference = TrRmExecutableCompiler
            .CreateStandardReferenceSet([reloadedClass, reloadedStruct]);
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Box box, Inner inner) {}"),
            reloadedReference);
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ComposesNestedImportedStructViewsAcrossProviderModules(
        string schemaIdentity)
    {
        var innerProvider = CreateArtifact("InnerProvider", "Prefix",
            "PrefixKey", 0, schemaIdentity, null, null, null,
            layoutKind: 0, pack: 0, localDependencyTypeName: "Inner",
            localDependencyExportKey: "InnerKey", fieldName: "Noise",
            secondaryFieldName: "Value");
        SetTypeDefFlags(innerProvider, 0x06, typeDefRow: 2);
        var leafProvider = CreateArtifact("LeafProvider", "Leaf",
            "LeafKey", 0xF200_0008, schemaIdentity, "InnerProvider",
            "InnerKey", TrRmTypeSpecTableWriter.Reference(0x03, 1),
            layoutKind: 0, pack: 0, importedTypeName: "Inner",
            fieldName: "Child", secondaryFieldName: "Value");
        SetTypeDefFlags(leafProvider, 0x06, typeDefRow: 1);
        SetTypeDefFlags(leafProvider, 0x06, typeDefRow: 2);
        var classProvider = CreateRootArtifactWithNestedStructExpectation(
            "ClassProvider", "LeafProvider", "LeafKey", "InnerKey",
            "BoxKey", schemaIdentity,
            innerProviderIdentity: "InnerProvider");
        SetTypeDefFlags(classProvider, 0x06, typeDefRow: 2);

        var leafTables = ReadTables(leafProvider, schemaIdentity);
        Assert.That(leafTables.Fields.GetName(1), Is.EqualTo("Child"));
        Assert.That(leafTables.Fields.GetName(2), Is.EqualTo("Value"));
        var innerTables = ReadTables(innerProvider, schemaIdentity);
        Assert.That(innerTables.TypeDefs.GetName(2), Is.EqualTo("Inner"));
        Assert.That(innerTables.Fields.GetName(2), Is.EqualTo("Value"));

        var leafInput = new TrRmStandardArtifactInput(schemaIdentity,
            leafProvider);
        var innerInput = new TrRmStandardArtifactInput(schemaIdentity,
            innerProvider);
        var classReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, classProvider),
            "ClassProvider", [leafInput, innerInput], ["BoxKey"]);
        var innerReference = TrRmExecutableCompiler.CreateStandardReference(
            innerInput, "InnerProvider", [], ["InnerKey"]);
        var composed = TrRmExecutableCompiler.CreateStandardReferenceSet([
            classReference, innerReference,
        ]);
        var leaf = composed.GenericNominalTypeDefinitions.Single(
            static definition => definition.Name == "Leaf");
        var inner = composed.GenericNominalTypeDefinitions.Single(
            static definition => definition.Name == "Inner");
        Assert.That(leaf.NominalKind, Is.EqualTo(1));
        Assert.That(leaf.Provider?.ModuleIdentity,
            Is.EqualTo("LeafProvider"));
        Assert.That(inner.Provider?.ModuleIdentity,
            Is.EqualTo("InnerProvider"));
        Assert.That(inner.Identity.DefinitionId, Is.EqualTo(0x1300_0002u));
        Assert.That(inner.SelectedExportKeys, Is.EqualTo(["InnerKey"]));
        var leafExpectation = leaf.ProviderExpectations.Single();
        Assert.That(leafExpectation.UsingModule.ModuleIdentity,
            Is.EqualTo("ClassProvider"));
        Assert.That(leafExpectation.ImportRow, Is.EqualTo(1));
        Assert.That(leafExpectation.ExpectedDefinitionId,
            Is.EqualTo(0x1300_0002u));
        Assert.That(leafExpectation.ProviderDefinitionId,
            Is.EqualTo(0x1300_0001u));
        Assert.That(leafExpectation.ExportKey, Is.EqualTo("LeafKey"));
        var classInnerExpectation = inner.ProviderExpectations.Single(
            static expectation =>
                expectation.UsingModule.ModuleIdentity == "ClassProvider");
        Assert.That(classInnerExpectation.ImportRow, Is.EqualTo(2));
        Assert.That(classInnerExpectation.ExpectedDefinitionId,
            Is.EqualTo(0x1300_0003u));
        Assert.That(classInnerExpectation.ProviderDefinitionId,
            Is.EqualTo(0x1300_0002u));
        Assert.That(classInnerExpectation.ExportKey, Is.EqualTo("InnerKey"));
        var leafInnerExpectation = inner.ProviderExpectations.Single(
            static expectation =>
                expectation.UsingModule.ModuleIdentity == "LeafProvider");
        Assert.That(leafInnerExpectation.ImportRow, Is.EqualTo(1));
        Assert.That(leafInnerExpectation.ExpectedDefinitionId,
            Is.EqualTo(0x1300_0002u));
        Assert.That(leafInnerExpectation.ProviderDefinitionId,
            Is.EqualTo(0x1300_0002u));
        Assert.That(leafInnerExpectation.ExportKey, Is.EqualTo("InnerKey"));
        Assert.That(inner.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));
        Assert.That(inner.TypeDefBindingEqualityGroups[0].Endpoints,
            Has.Length.EqualTo(3));

        var incompatibleInnerProvider = CreateArtifact("InnerProvider",
            "Prefix", "PrefixKey", 0, schemaIdentity, null, null, null,
            layoutKind: 0, pack: 0, localDependencyTypeName: "Inner",
            localDependencyExportKey: "InnerKey", fieldName: "Noise",
            secondaryFieldName: "Value");
        SetTypeDefFlags(incompatibleInnerProvider, 0x0A, typeDefRow: 2);
        var incompatibleInnerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleInnerProvider),
                "InnerProvider", [], ["InnerKey"]);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReferenceSet([
                classReference, incompatibleInnerReference,
            ]));

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), composed);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "NestedStructViewsReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        Assert.That(Enumerable.Range(1, tables.Modules.Count)
                .Select(tables.Modules.GetIdentity),
            Is.EquivalentTo([
                "NestedStructViewsReemitted", "LeafProvider",
                "InnerProvider",
            ]));

        var reemittedInput = new TrRmStandardArtifactInput(
            schemaIdentity, artifact);
        var reloadedClass = TrRmExecutableCompiler.CreateStandardReference(
            reemittedInput, "NestedStructViewsReemitted",
            [], ["BoxKey"]);
        var reloadedInner = TrRmExecutableCompiler.CreateStandardReference(
            reemittedInput, "InnerProvider", [], ["InnerKey"]);
        var reloaded = TrRmExecutableCompiler.CreateStandardReferenceSet([
            reloadedClass, reloadedInner,
        ]);
        var reloadedInnerDefinition
            = reloaded.GenericNominalTypeDefinitions.Single(
                static definition => definition.Name == "Inner");
        Assert.That(reloadedInnerDefinition.ProviderExpectations.Select(
                static expectation =>
                    expectation.UsingModule.ModuleIdentity),
            Is.EquivalentTo([
                "NestedStructViewsReemitted", "LeafProvider",
            ]));
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reloaded);
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ReemitsSelectedDirectStructRootsWithClassViews(
        string schemaIdentity)
    {
        var root = CreateArtifact("Root", "Box", "BoxKey", 0,
            schemaIdentity, null, null, null,
            localDependencyTypeName: "Leaf",
            localDependencyExportKey: "LeafKey");
        SetTypeDefFlags(root, 0x06, typeDefRow: 2);

        var input = new TrRmStandardArtifactInput(schemaIdentity, root);
        var classReference = TrRmExecutableCompiler.CreateStandardReference(
            input, "Root", [], ["BoxKey"]);
        var structReference = TrRmExecutableCompiler.CreateStandardReference(
            input, "Root", [], ["LeafKey"]);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [classReference, structReference]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public void Use(Box box, Leaf leaf) {}"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!, "RootReemitted",
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        Assert.That(tables.TypeDefs.Count, Is.EqualTo(2));
        Assert.That(tables.TypeDefs.GetKind(1), Is.EqualTo(2));
        Assert.That(tables.TypeDefs.GetKind(2), Is.EqualTo(1));
        Assert.That(tables.Exports.GetExportsForModule(1)
                .Select(static export => export.Key),
            Is.EquivalentTo(["BoxKey", "LeafKey", "Use"]));

        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "RootReemitted", [], ["BoxKey", "LeafKey"]);
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Box box, Leaf leaf) {}"),
            reloadedReference);
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));

    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ReemitsSelectedStructRootsFromSeparateArtifactsWithClassViews(
        string schemaIdentity)
    {
        var classProvider = CreateClassArtifact("ClassProvider", "Box",
            "BoxKey", 0xF200_0008, schemaIdentity);
        var structProvider = CreateArtifact("StructProvider", "Leaf",
            "LeafKey", 0, schemaIdentity, null, null, null,
            localDependencyTypeName: "Inner");
        SetTypeDefFlags(structProvider, 0x06, typeDefRow: 1);
        SetTypeDefFlags(structProvider, 0x06, typeDefRow: 2);

        var classReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, classProvider),
            "ClassProvider", [], ["BoxKey"]);
        var structReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    structProvider),
                "StructProvider", [], ["LeafKey"]);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [classReference, structReference]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            string.Empty), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!, "ClassRootReemitted",
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        var typeDefRows = Enumerable.Range(1, tables.TypeDefs.Count).ToArray();
        Assert.That(typeDefRows.Select(tables.TypeDefs.GetName),
            Is.EquivalentTo(["Box", "Leaf", "Inner"]));
        var structModuleRow = Enumerable.Range(1, tables.Modules.Count)
            .Single(row => tables.Modules.GetIdentity(row)
                == "StructProvider");
        foreach (var typeName in new[] { "Leaf", "Inner" })
        {
            var typeDefRow = typeDefRows.Single(row =>
                tables.TypeDefs.GetName(row) == typeName);
            Assert.That(tables.TypeDefs.GetOwnerModule(typeDefRow),
                Is.EqualTo((uint)structModuleRow));
        }

        var artifactInput
            = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var reloadedClass
            = TrRmExecutableCompiler.CreateStandardReference(artifactInput,
                "ClassRootReemitted", [], ["BoxKey"]);
        var reloadedStruct
            = TrRmExecutableCompiler.CreateStandardReference(artifactInput,
                "StructProvider", [], ["LeafKey"]);
        var reloaded = TrRmExecutableCompiler.CreateStandardReferenceSet([
            reloadedClass, reloadedStruct,
        ]);
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Box box, Leaf leaf) {}"),
            reloaded);
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void EmitsEqualityRowsForDistinctImportedClassViews(
        string schemaIdentity)
    {
        var provider = CreateRelocatedClassProvider("Provider",
            schemaIdentity, "LeafAliasKey");
        var consumer = CreateRootArtifactWithTwoClassImports("Root",
            "Provider", "BoxKey", "LeafKey", "LeafAliasKey",
            schemaIdentity);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, consumer),
            "Root",
            [new TrRmStandardArtifactInput(schemaIdentity, provider)],
            ["BoxKey"]);
        var leaf = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Leaf");
        Assert.That(leaf.Expectations, Has.Length.EqualTo(2));
        Assert.That(leaf.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public void Use(Box value) {}"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!, "RootReemitted",
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        Assert.That(tables.TypeDefBindingEqualities.Count, Is.EqualTo(1));
        Assert.That(tables.TypeDefBindingEqualities.GetOwnerModule(1),
            Is.EqualTo(1u));
        Assert.That(tables.TypeDefBindingEqualities.GetLeftTypeDef(1),
            Is.EqualTo(2u));
        Assert.That(tables.TypeDefBindingEqualities.GetRightTypeDef(1),
            Is.EqualTo(3u));

        var reloadedReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "RootReemitted",
            [new TrRmStandardArtifactInput(schemaIdentity, provider)],
            ["BoxKey"]);
        var reloadedLeaf = reloadedReference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Leaf");
        Assert.That(reloadedLeaf.Expectations, Has.Length.EqualTo(2));
        Assert.That(reloadedLeaf.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));
        Assert.That(reloadedLeaf.TypeDefBindingEqualityGroups[0].OwnerModule?
                .ModuleIdentity,
            Is.EqualTo("RootReemitted"));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ReemitsClassRootsFromDistinctCoLocatedModules(
        string schemaIdentity)
    {
        var provider = CreateRelocatedClassProvider("CoLocated",
            schemaIdentity);
        var coLocated = SplitClassRootsAcrossModules(provider,
            schemaIdentity, "CoLocatedLeaf");
        var (coLocatedWithDistinctFeatures, distinctFeature)
            = AddFeatureRequirementToSecondModule(coLocated,
                schemaIdentity);
        var input = new TrRmStandardArtifactInput(schemaIdentity,
            coLocatedWithDistinctFeatures);
        var prefixReference = TrRmExecutableCompiler.CreateStandardReference(
            input, "CoLocated", [], ["PrefixKey"]);
        var leafReference = TrRmExecutableCompiler.CreateStandardReference(
            input, "CoLocatedLeaf", [], ["LeafKey"]);
        var selectedRootModules = prefixReference.StandardClassDeclarations
            .Concat(leafReference.StandardClassDeclarations)
            .Where(static declaration =>
                !declaration.SelectedExportKeys.IsEmpty)
            .Select(static declaration =>
                declaration.Declaration.Provider.ModuleRow)
            .Distinct()
            .ToArray();
        Assert.That(selectedRootModules, Is.EquivalentTo([1, 2]));

        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet([
            prefixReference, leafReference,
        ]);
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "CoLocatedReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        Assert.That(tables.Modules.Count, Is.EqualTo(2));
        var hasReemittedModule = false;
        var prefixModuleRow = 0;
        var prefixDefinition = 0u;
        var leafModuleRow = 0;
        var leafDefinition = 0u;
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            hasReemittedModule
                |= StringComparer.Ordinal.Equals(
                    tables.Modules.GetIdentity(moduleRow),
                    "CoLocatedReemitted");
            foreach (var export in tables.Exports
                .GetExportsForModule(moduleRow))
            {
                if (StringComparer.Ordinal.Equals(export.ExportKey,
                        "PrefixKey"))
                {
                    prefixModuleRow = moduleRow;
                    prefixDefinition = export.Definition;
                }
                else if (StringComparer.Ordinal.Equals(export.ExportKey,
                             "LeafKey"))
                {
                    leafModuleRow = moduleRow;
                    leafDefinition = export.Definition;
                }
            }
        }

        Assert.That(hasReemittedModule, Is.True);
        Assert.That(prefixModuleRow, Is.GreaterThan(0));
        Assert.That(leafModuleRow, Is.GreaterThan(0));
        Assert.That(prefixModuleRow, Is.Not.EqualTo(leafModuleRow));
        Assert.That(tables.TypeDefs.Count, Is.EqualTo(2));
        var prefixModuleIdentity
            = tables.Modules.GetIdentity(prefixModuleRow);
        var leafModuleIdentity = tables.Modules.GetIdentity(leafModuleRow);
        Assert.That(prefixModuleIdentity, Is.Not.EqualTo(leafModuleIdentity));
        var prefixRow = checked((int)(prefixDefinition
            & 0x00FF_FFFF));
        var leafRow = checked((int)(leafDefinition
            & 0x00FF_FFFF));
        Assert.That(tables.TypeDefs.GetName(prefixRow),
            Is.EqualTo("Prefix"));
        Assert.That(tables.TypeDefs.GetName(leafRow), Is.EqualTo("Leaf"));
        var (prefixFeatureStart, prefixFeatureCount)
            = tables.Modules.GetFeatureRange(prefixModuleRow);
        var (leafFeatureStart, leafFeatureCount)
            = tables.Modules.GetFeatureRange(leafModuleRow);
        var prefixFeatures = new List<TrRmFeatureRow>();
        for (var offset = 0; offset < prefixFeatureCount; offset++)
        {
            prefixFeatures.Add(tables.Features.GetFeature(
                prefixFeatureStart + offset));
        }
        var leafFeatures = new List<TrRmFeatureRow>();
        for (var offset = 0; offset < leafFeatureCount; offset++)
        {
            leafFeatures.Add(tables.Features.GetFeature(
                leafFeatureStart + offset));
        }
        Assert.That(prefixFeatures, Does.Not.Contain(distinctFeature));
        Assert.That(leafFeatures, Does.Contain(distinctFeature));
        var (leafFieldStart, leafFieldEnd)
            = tables.TypeDefs.GetFieldRange(leafRow);
        Assert.That(leafFieldEnd - leafFieldStart, Is.EqualTo(1));
        Assert.That(tables.Fields.GetName(leafFieldStart),
            Is.EqualTo("Value"));
        var (leafLayoutStart, leafLayoutCount)
            = tables.TypeDefs.GetLayoutRange(leafRow);
        Assert.That(leafLayoutCount, Is.EqualTo(1));
        Assert.That(tables.Layouts.GetOwnerTypeDef(leafLayoutStart),
            Is.EqualTo(leafRow));

        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var reloadedPrefix = TrRmExecutableCompiler.CreateStandardReference(
            reemittedInput, prefixModuleIdentity, [], ["PrefixKey"]);
        var reloadedLeaf = TrRmExecutableCompiler.CreateStandardReference(
            reemittedInput, leafModuleIdentity, [], ["LeafKey"]);
        var reloadedReference = TrRmExecutableCompiler.CreateStandardReferenceSet([
            reloadedPrefix, reloadedLeaf,
        ]);
        Assert.That(reloadedReference.StandardClassDeclarations
                .Where(static declaration =>
                    !declaration.SelectedExportKeys.IsEmpty)
                .Select(static declaration => declaration.Declaration.Name),
            Is.EquivalentTo(["Prefix", "Leaf"]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ReemitsDirectClassRootsFromSeparateArtifactsWithExactFeatures(
        string schemaIdentity)
    {
        var rootA = CreateClassArtifact("RootA", "Alpha", "AlphaKey",
            0xF200_0008, schemaIdentity);
        var rootB = CreateClassArtifact("RootB", "Beta", "BetaKey",
            0xF200_0008, schemaIdentity);
        var (rootBWithFeature, distinctFeature)
            = AddFeatureRequirementToSingleModule(rootB, schemaIdentity);
        var rootAInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootA);
        var rootBInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootBWithFeature);
        var referenceA = TrRmExecutableCompiler.CreateStandardReference(
            rootAInput, "RootA", [], ["AlphaKey"]);
        var referenceB = TrRmExecutableCompiler.CreateStandardReference(
            rootBInput, "RootB", [], ["BetaKey"]);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet([
            referenceA, referenceB,
        ]);
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "SeparateRootsReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        Assert.That(tables.Modules.Count, Is.EqualTo(2));
        var alphaModuleRow = 0;
        var betaModuleRow = 0;
        var alphaDefinition = 0u;
        var betaDefinition = 0u;
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            foreach (var export in tables.Exports
                .GetExportsForModule(moduleRow))
            {
                if (StringComparer.Ordinal.Equals(export.ExportKey,
                        "AlphaKey"))
                {
                    alphaModuleRow = moduleRow;
                    alphaDefinition = export.Definition;
                }
                else if (StringComparer.Ordinal.Equals(export.ExportKey,
                             "BetaKey"))
                {
                    betaModuleRow = moduleRow;
                    betaDefinition = export.Definition;
                }
            }
        }

        Assert.That(alphaModuleRow, Is.GreaterThan(0));
        Assert.That(betaModuleRow, Is.GreaterThan(0));
        Assert.That(alphaModuleRow, Is.Not.EqualTo(betaModuleRow));
        Assert.That(tables.TypeDefs.Count, Is.EqualTo(2));
        Assert.That(tables.Fields.Count, Is.EqualTo(2));
        Assert.That(tables.Layouts.Count, Is.EqualTo(2));
        var alphaModuleIdentity = tables.Modules.GetIdentity(alphaModuleRow);
        var betaModuleIdentity = tables.Modules.GetIdentity(betaModuleRow);
        Assert.That(new[] { alphaModuleIdentity, betaModuleIdentity },
            Does.Contain("SeparateRootsReemitted"));
        Assert.That(alphaModuleIdentity is "RootA"
                or "SeparateRootsReemitted",
            Is.True);
        Assert.That(betaModuleIdentity is "RootB"
                or "SeparateRootsReemitted",
            Is.True);
        Assert.That(tables.TypeDefs.GetName(checked((int)(
                alphaDefinition & 0x00FF_FFFF))),
            Is.EqualTo("Alpha"));
        Assert.That(tables.TypeDefs.GetName(checked((int)(
                betaDefinition & 0x00FF_FFFF))),
            Is.EqualTo("Beta"));
        var (alphaFeatureStart, alphaFeatureCount)
            = tables.Modules.GetFeatureRange(alphaModuleRow);
        var (betaFeatureStart, betaFeatureCount)
            = tables.Modules.GetFeatureRange(betaModuleRow);
        var alphaFeatures = new List<TrRmFeatureRow>();
        for (var offset = 0; offset < alphaFeatureCount; offset++)
        {
            alphaFeatures.Add(tables.Features.GetFeature(
                alphaFeatureStart + offset));
        }
        var betaFeatures = new List<TrRmFeatureRow>();
        for (var offset = 0; offset < betaFeatureCount; offset++)
        {
            betaFeatures.Add(tables.Features.GetFeature(
                betaFeatureStart + offset));
        }
        Assert.That(alphaFeatures, Does.Not.Contain(distinctFeature));
        Assert.That(betaFeatures, Does.Contain(distinctFeature));

        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var reloadedAlpha = TrRmExecutableCompiler.CreateStandardReference(
            reemittedInput, alphaModuleIdentity, [], ["AlphaKey"]);
        var reloadedBeta = TrRmExecutableCompiler.CreateStandardReference(
            reemittedInput, betaModuleIdentity, [], ["BetaKey"]);
        var reloadedReference = TrRmExecutableCompiler.CreateStandardReferenceSet([
            reloadedAlpha, reloadedBeta,
        ]);
        Assert.That(reloadedReference.StandardClassDeclarations
                .Where(static declaration =>
                    !declaration.SelectedExportKeys.IsEmpty)
                .Select(static declaration => declaration.Declaration.Name),
            Is.EquivalentTo(["Alpha", "Beta"]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ReemitsSeparateClassRootImportsWithOwnerRelativeEqualities(
        string schemaIdentity)
    {
        var provider = CreateRelocatedClassProvider("SharedProvider",
            schemaIdentity, "LeafAliasKey");
        var rootA = CreateRootArtifactWithTwoClassImports("RootA",
            "SharedProvider", "AlphaKey", "LeafKey", "LeafAliasKey",
            schemaIdentity, includeEquality: true);
        var rootB = CreateRootArtifactWithTwoClassImports("RootB",
            "SharedProvider", "BetaKey", "LeafKey", "LeafAliasKey",
            schemaIdentity);
        var (rootBWithFeature, distinctFeature)
            = AddFeatureRequirementToSingleModule(rootB, schemaIdentity);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            provider);
        var referenceA = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootA), "RootA",
            [providerInput], ["AlphaKey"]);
        var referenceB = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity,
                rootBWithFeature), "RootB",
            [providerInput], ["BetaKey"]);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet([
            referenceA, referenceB,
        ]);
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "SeparateImportedRootsReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        Assert.That(tables.Modules.Count, Is.EqualTo(2));
        Assert.That(tables.Imports.Count, Is.EqualTo(4));
        Assert.That(tables.TypeDefBindingEqualities.Count,
            Is.GreaterThan(0));
        var alphaOwnerModule = Enumerable.Range(1, tables.Modules.Count)
            .Single(moduleRow => tables.Exports
                .GetExportsForModule(moduleRow)
                .Any(static export => StringComparer.Ordinal.Equals(
                    export.ExportKey, "AlphaKey")));
        var equalityOwners = new HashSet<uint>();
        for (var equalityRow = 1;
            equalityRow <= tables.TypeDefBindingEqualities.Count;
            equalityRow++)
        {
            var ownerModule
                = tables.TypeDefBindingEqualities.GetOwnerModule(
                    equalityRow);
            Assert.That(ownerModule, Is.InRange(1u,
                checked((uint)tables.Modules.Count)));
            _ = equalityOwners.Add(ownerModule);
            var leftTypeDef = checked((int)
                tables.TypeDefBindingEqualities.GetLeftTypeDef(equalityRow));
            var rightTypeDef = checked((int)
                tables.TypeDefBindingEqualities.GetRightTypeDef(equalityRow));
            Assert.That(tables.TypeDefs.GetOwnerModule(leftTypeDef),
                Is.InRange(1, tables.Modules.Count));
            Assert.That(tables.TypeDefs.GetOwnerModule(rightTypeDef),
                Is.InRange(1, tables.Modules.Count));
        }
        Assert.That(equalityOwners, Does.Contain((uint)alphaOwnerModule));

        var alphaModule = 0;
        var betaModule = 0;
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            foreach (var export in tables.Exports
                .GetExportsForModule(moduleRow))
            {
                if (StringComparer.Ordinal.Equals(export.ExportKey,
                        "AlphaKey"))
                {
                    alphaModule = moduleRow;
                }
                else if (StringComparer.Ordinal.Equals(export.ExportKey,
                             "BetaKey"))
                {
                    betaModule = moduleRow;
                }
            }
        }
        Assert.That(alphaModule, Is.GreaterThan(0));
        Assert.That(betaModule, Is.GreaterThan(0));
        var (alphaFeatureStart, alphaFeatureCount)
            = tables.Modules.GetFeatureRange(alphaModule);
        var alphaFeatures = new List<TrRmFeatureRow>();
        for (var offset = 0; offset < alphaFeatureCount; offset++)
        {
            alphaFeatures.Add(tables.Features.GetFeature(
                alphaFeatureStart + offset));
        }
        var (betaFeatureStart, betaFeatureCount)
            = tables.Modules.GetFeatureRange(betaModule);
        var betaFeatures = new List<TrRmFeatureRow>();
        for (var offset = 0; offset < betaFeatureCount; offset++)
        {
            betaFeatures.Add(tables.Features.GetFeature(
                betaFeatureStart + offset));
        }
        Assert.That(alphaFeatures, Does.Not.Contain(distinctFeature));
        Assert.That(betaFeatures, Does.Contain(distinctFeature));
        var outputInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var reloadedA = TrRmExecutableCompiler.CreateStandardReference(
            outputInput, tables.Modules.GetIdentity(alphaModule),
            [providerInput],
            ["AlphaKey"]);
        var reloadedB = TrRmExecutableCompiler.CreateStandardReference(
            outputInput, tables.Modules.GetIdentity(betaModule),
            [providerInput],
            ["BetaKey"]);
        var reloadedReference = TrRmExecutableCompiler.CreateStandardReferenceSet([
            reloadedA, reloadedB,
        ]);
        Assert.That(reloadedReference.StandardClassDeclarations
                .Where(static declaration =>
                    !declaration.SelectedExportKeys.IsEmpty)
                .SelectMany(static declaration =>
                    declaration.SelectedExportKeys),
            Is.EquivalentTo(["AlphaKey", "BetaKey"]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ReemitsCompatibleClassImportRelocationAcrossRootArtifacts(
        string schemaIdentity)
    {
        var providerA = CreateRelocatedClassProvider("ProviderA",
            schemaIdentity, "LeafAliasKey", includePrefix: true);
        var providerB = CreateRelocatedClassProvider("ProviderB",
            schemaIdentity, "LeafAliasKey", includePrefix: false);
        var rootA = CreateRootArtifactWithTwoClassImports("RootA",
            "ProviderA", "AlphaKey", "LeafKey", "LeafAliasKey",
            schemaIdentity);
        var rootB = CreateRootArtifactWithTwoClassImports("RootB",
            "ProviderB", "BetaKey", "LeafKey", "LeafAliasKey",
            schemaIdentity);
        var providerInputA = new TrRmStandardArtifactInput(schemaIdentity,
            providerA);
        var providerInputB = new TrRmStandardArtifactInput(schemaIdentity,
            providerB);
        var referenceA = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootA), "RootA",
            [providerInputA], ["AlphaKey"]);
        var referenceB = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootB), "RootB",
            [providerInputB], ["BetaKey"]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            string.Empty), TrRmExecutableCompiler.CreateStandardReferenceSet([
                referenceA, referenceB,
            ]));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "RelocatedImportedRootsReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        Assert.That(tables.Modules.Count, Is.EqualTo(2));
        Assert.That(tables.Imports.Count, Is.EqualTo(4));
        Assert.That(tables.TypeDefBindingEqualities.Count,
            Is.GreaterThan(0));
        Assert.That(Enumerable.Range(1, tables.ModuleRefs.Count)
                .Select(tables.ModuleRefs.GetProducerIdentity),
            Is.EquivalentTo(["ProviderA", "ProviderB"]));

        var moduleByExport = new Dictionary<string, string>(
            StringComparer.Ordinal);
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            foreach (var export in tables.Exports
                .GetExportsForModule(moduleRow))
            {
                if (export.ExportKey is "AlphaKey" or "BetaKey")
                {
                    moduleByExport.Add(export.ExportKey,
                        tables.Modules.GetIdentity(moduleRow));
                }
            }
        }

        var outputInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var reloadedA = TrRmExecutableCompiler.CreateStandardReference(
            outputInput, moduleByExport["AlphaKey"], [providerInputA],
            ["AlphaKey"]);
        var reloadedB = TrRmExecutableCompiler.CreateStandardReference(
            outputInput, moduleByExport["BetaKey"], [providerInputB],
            ["BetaKey"]);
        var reloadedReference = TrRmExecutableCompiler.CreateStandardReferenceSet([
            reloadedA, reloadedB,
        ]);
        Assert.That(reloadedReference.StandardClassDeclarations
                .Where(static declaration =>
                    !declaration.SelectedExportKeys.IsEmpty)
                .SelectMany(static declaration =>
                    declaration.SelectedExportKeys),
            Is.EquivalentTo(["AlphaKey", "BetaKey"]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void RejectsSplitClassProviderAcrossSeparateRootArtifacts(
        string schemaIdentity)
    {
        var providerA = CreateClassArtifact("SharedProvider", "Leaf",
            "LeafKey", 0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0);
        var providerB = CreateClassArtifact("SharedProvider", "Leaf",
            "LeafKey", 0xF200_0009, schemaIdentity, layoutKind: 0, pack: 0);
        var rootA = CreateArtifact("RootA", "BoxA", "AlphaKey",
            TrRmTypeSpecTableWriter.Reference(0x03, 1), schemaIdentity,
            "SharedProvider", "LeafKey",
            TrRmTypeSpecTableWriter.Builtin(8), layoutKind: 0, pack: 0,
            importedTypeName: "Leaf");
        var rootB = CreateArtifact("RootB", "BoxB", "BetaKey",
            TrRmTypeSpecTableWriter.Reference(0x03, 1), schemaIdentity,
            "SharedProvider", "LeafKey",
            TrRmTypeSpecTableWriter.Builtin(9), layoutKind: 0, pack: 0,
            importedTypeName: "Leaf");
        var referenceA = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootA), "RootA", [
                new TrRmStandardArtifactInput(schemaIdentity, providerA),
            ], ["AlphaKey"]);
        var referenceB = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootB), "RootB", [
                new TrRmStandardArtifactInput(schemaIdentity, providerB),
            ], ["BetaKey"]);

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReferenceSet([
                referenceA, referenceB,
            ]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void RejectsMalformedEqualityOwnerInSeparateRootArtifact(
        string schemaIdentity)
    {
        var provider = CreateRelocatedClassProvider("SharedProvider",
            schemaIdentity, "LeafAliasKey");
        var root = CreateRootArtifactWithTwoClassImports("RootA",
            "SharedProvider", "AlphaKey", "LeafKey", "LeafAliasKey",
            schemaIdentity);
        var malformedRoot = AddTypeDefBindingEquality(root, schemaIdentity,
            ownerModule: 2, leftTypeDef: 2, rightTypeDef: 3);

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, malformedRoot),
                "RootA", [
                    new TrRmStandardArtifactInput(schemaIdentity, provider),
                ], ["AlphaKey"]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void RejectsMalformedProviderViewForSeparateRootArtifact(
        string schemaIdentity)
    {
        var provider = CreateRelocatedClassProvider("SharedProvider",
            schemaIdentity, "LeafAliasKey", includePrefix: false);
        SetTypeDefFlags(provider, 0x06);
        var root = CreateRootArtifactWithTwoClassImports("RootA",
            "SharedProvider", "AlphaKey", "LeafKey", "LeafAliasKey",
            schemaIdentity);

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, root),
                "RootA", [
                    new TrRmStandardArtifactInput(schemaIdentity, provider),
                ], ["AlphaKey"]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ReemitsNestedClassTypeSpecImportsAcrossSeparateArtifacts(
        string schemaIdentity)
    {
        var provider = CreateRelocatedClassProvider("NestedProvider",
            schemaIdentity, includePrefix: false);
        var wrappedImportedType = TrRmTypeSpecTableWriter.Wrapped(0x06,
            TrRmTypeSpecTableWriter.Reference(0x03, 1));
        var rootA = CreateArtifact("RootA", "BoxA", "BoxAKey",
            0xF200_0008, schemaIdentity, "NestedProvider", "LeafKey",
            wrappedImportedType, layoutKind: 0, pack: 0,
            importedTypeName: "Leaf");
        var rootB = CreateArtifact("RootB", "BoxB", "BoxBKey",
            0xF200_0008, schemaIdentity, "NestedProvider", "LeafKey",
            wrappedImportedType, layoutKind: 0, pack: 0,
            importedTypeName: "Leaf");
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            provider);
        var referenceA = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootA), "RootA",
            [providerInput], ["BoxAKey"]);
        var referenceB = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootB), "RootB",
            [providerInput], ["BoxBKey"]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            string.Empty), TrRmExecutableCompiler.CreateStandardReferenceSet([
                referenceA, referenceB,
            ]));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "NestedImportsReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        Assert.That(tables.Modules.Count, Is.EqualTo(2));
        Assert.That(tables.Imports.Count, Is.EqualTo(2));
        Assert.That(tables.TypeSpecs.Count, Is.EqualTo(2));

        string? rootAIdentity = null;
        string? rootBIdentity = null;
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            var rootExport = tables.Exports.GetExportsForModule(moduleRow)
                .Single(export => export.ExportKey is "BoxAKey" or "BoxBKey");
            var rootTypeDefRow = checked((int)(
                rootExport.Definition & 0x00FF_FFFF));
            var (fieldStart, fieldEnd)
                = tables.TypeDefs.GetFieldRange(rootTypeDefRow);
            Assert.That(fieldEnd - fieldStart, Is.EqualTo(1));
            var typeSpecReference
                = tables.Fields.GetTypeSpecRef(fieldStart);
            var typeSpec = tables.TypeSpecs.GetTypeSpecRef(
                typeSpecReference);
            Assert.That(typeSpec.Form, Is.EqualTo(0x06));
            var row = tables.TypeSpecs.GetRow(typeSpec.Row);
            var childReference = BinaryPrimitives.ReadUInt32LittleEndian(
                row[2..]);
            Assert.That((byte)(childReference >> 24), Is.EqualTo(0x03));
            var importRow = checked((int)(childReference & 0x00FF_FFFF));
            Assert.That(tables.Imports
                    .GetExpectedDefinitionForImportRow(importRow) >> 24,
                Is.EqualTo(0x13u));
            Assert.That(tables.ModuleRefs.GetOwnerModule(
                    tables.Imports.GetModuleRefForImportRow(importRow)),
                Is.EqualTo(moduleRow));
            if (StringComparer.Ordinal.Equals(rootExport.ExportKey,
                    "BoxAKey"))
            {
                rootAIdentity = tables.Modules.GetIdentity(moduleRow);
            }
            else
            {
                rootBIdentity = tables.Modules.GetIdentity(moduleRow);
            }
        }

        Assert.That(rootAIdentity, Is.Not.Null);
        Assert.That(rootBIdentity, Is.Not.Null);
        var outputInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var reloadedA = TrRmExecutableCompiler.CreateStandardReference(
            outputInput, rootAIdentity!, [providerInput], ["BoxAKey"]);
        var reloadedB = TrRmExecutableCompiler.CreateStandardReference(
            outputInput, rootBIdentity!, [providerInput], ["BoxBKey"]);
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty),
            TrRmExecutableCompiler.CreateStandardReferenceSet([
                reloadedA, reloadedB,
            ]));
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));

    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ReemitsClassMethodBodiesFromSeparateRootArtifacts(
        string schemaIdentity)
    {
        var providerA = CreateRelocatedClassProvider("MethodProviderA",
            schemaIdentity, includeReadMethod: true, includePrefix: true);
        var providerB = CreateRelocatedClassProvider("MethodProviderB",
            schemaIdentity, includeReadMethod: true, includePrefix: false);
        var inputA = new TrRmStandardArtifactInput(schemaIdentity, providerA);
        var inputB = new TrRmStandardArtifactInput(schemaIdentity, providerB);
        var referenceA = TrRmExecutableCompiler.CreateStandardReference(
            inputA, "MethodProviderA", [], ["LeafKey"]);
        var referenceB = TrRmExecutableCompiler.CreateStandardReference(
            inputB, "MethodProviderB", [], ["LeafKey"]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            string.Empty), TrRmExecutableCompiler.CreateStandardReferenceSet([
                referenceA, referenceB,
            ]));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "MethodRootsReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        Assert.That(tables.Modules.Count, Is.EqualTo(2));
        Assert.That(tables.Methods.Count, Is.GreaterThanOrEqualTo(4));
        Assert.That(tables.Signatures.Count,
            Is.EqualTo(tables.Methods.Count));
        Assert.That(tables.Signatures.ParamCount,
            Is.GreaterThanOrEqualTo(4));

        var rootModuleIdentities = new List<string>();
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            var leafExport = tables.Exports.GetExportsForModule(moduleRow)
                .Single(export => export.ExportKey == "LeafKey");
            var leafTypeDefRow = checked((int)(
                leafExport.Definition & 0x00FF_FFFF));
            rootModuleIdentities.Add(tables.Modules.GetIdentity(moduleRow));
            var ownedMethods = Enumerable.Range(1, tables.Methods.Count)
                .Where(methodRow =>
                    tables.Methods.GetOwnerType(methodRow)
                        == leafTypeDefRow)
                .ToArray();
            Assert.That(ownedMethods, Has.Length.EqualTo(2));
            Assert.That(ownedMethods.All(methodRow =>
                    tables.Methods.GetBodyRow(methodRow) != 0),
                Is.True);
        }

        Assert.That(rootModuleIdentities,
            Does.Contain("MethodRootsReemitted"));
        var outputInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var reloaded = rootModuleIdentities.Select(moduleIdentity =>
            TrRmExecutableCompiler.CreateStandardReference(outputInput,
                moduleIdentity, [], ["LeafKey"])).ToArray();
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty),
            TrRmExecutableCompiler.CreateStandardReferenceSet(reloaded));
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ReemitsFieldReferenceMethodBodiesAcrossSeparateArtifacts(
        string schemaIdentity)
    {
        var rootA = CreateFieldReferenceClassProvider("ReferenceRootA",
            schemaIdentity, includePrefix: false);
        var rootB = CreateFieldReferenceClassProvider("ReferenceRootB",
            schemaIdentity, includePrefix: true);
        var referenceA = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootA),
            "ReferenceRootA", [], ["BoxKey"]);
        var referenceB = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootB),
            "ReferenceRootB", [], ["PrefixKey", "BoxKey"]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            string.Empty), TrRmExecutableCompiler.CreateStandardReferenceSet([
                referenceA, referenceB,
            ]));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "ReferenceRootsReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        Assert.That(tables.Modules.Count, Is.EqualTo(2));
        var boxRows = new List<int>();
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            var moduleExports = tables.Exports.GetExportsForModule(moduleRow);
            Assert.That(moduleExports.Select(static export =>
                    export.ExportKey),
                Does.Contain("Box.ReadValue").And.Contain(
                    "Box.ReadReferenceAlias"));
            var boxExport = moduleExports
                .Single(export => export.ExportKey == "BoxKey");
            boxRows.Add(checked((int)(
                boxExport.Definition & 0x00FF_FFFF)));
        }

        Assert.That(boxRows, Has.Count.EqualTo(2));
        foreach (var boxRow in boxRows)
        {
            int GetMethodRow(string name)
                => Enumerable.Range(1, tables.Methods.Count)
                    .Single(row =>
                        tables.Methods.GetOwnerType(row) == boxRow
                        && tables.Methods.GetName(row) == name);

            var identityMethodRow = GetMethodRow("Identity");
            var identityBody = tables.Bodies.GetRow(
                tables.Methods.GetBodyRow(identityMethodRow));
            Assert.That(identityBody[0], Is.EqualTo(0x01));

            var readValueRow = GetMethodRow("ReadValue");
            var readValueBody = tables.Bodies.GetRow(
                tables.Methods.GetBodyRow(readValueRow));
            Assert.That(readValueBody[0], Is.EqualTo(0x02));

            var forwardMethodRow = GetMethodRow("Forward");
            var forwardBody = tables.Bodies.GetRow(
                tables.Methods.GetBodyRow(forwardMethodRow));
            Assert.That(forwardBody[0], Is.EqualTo(0x05));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    forwardBody[4..]),
                Is.EqualTo(0x1600_0000u
                    | checked((uint)identityMethodRow)));

            var readMethodRow = GetMethodRow("ReadReference");
            Assert.That(tables.Methods.GetBodyRow(readMethodRow),
                Is.GreaterThan(0));
            Assert.That(tables.Contracts.GetEffectsMask(readMethodRow),
                Is.EqualTo(4u));
            Assert.That(tables.Contracts.GetDependencyContracts(
                    readMethodRow),
                Is.EquivalentTo([
                    new TrRmDependencyContract(1, 2, 3, 1, 0, 0),
                ]));
            var body = tables.Bodies.GetRow(
                tables.Methods.GetBodyRow(readMethodRow));
            Assert.That(body[0], Is.EqualTo(0x04));
            var fieldToken = BinaryPrimitives.ReadUInt32LittleEndian(
                body[4..]);
            Assert.That(fieldToken >> 24, Is.EqualTo(0x14u));
            var fieldRow = checked((int)(fieldToken & 0x00FF_FFFF));
            Assert.That(tables.Fields.GetOwnerTypeDef(fieldRow),
                Is.EqualTo(boxRow));
            Assert.That(tables.Fields.GetName(fieldRow),
                Is.EqualTo("Value"));
            var dependencyRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(body[8..]));
            Assert.That(tables.Contracts.GetDependencyContractRows(
                    readMethodRow).Single().Row,
                Is.EqualTo(dependencyRow));

            if (Enumerable.Range(1, tables.Methods.Count).Any(row =>
                    tables.Methods.GetOwnerType(row) == boxRow
                    && tables.Methods.GetName(row) == "Set"))
            {
                var setBody = tables.Bodies.GetRow(
                    tables.Methods.GetBodyRow(GetMethodRow("Set")));
                Assert.That(setBody[0], Is.EqualTo(0x03));
            }
        }

        var outputInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var prefixModule = Enumerable.Range(1, tables.Modules.Count)
            .Single(moduleRow => tables.Exports
                .GetExportsForModule(moduleRow)
                .Any(static export => export.ExportKey == "PrefixKey"));
        var boxOnlyModule = Enumerable.Range(1, tables.Modules.Count)
            .Single(moduleRow => moduleRow != prefixModule);
        var reloadedA = TrRmExecutableCompiler.CreateStandardReference(
            outputInput, tables.Modules.GetIdentity(boxOnlyModule), [],
            ["BoxKey"]);
        var reloadedB = TrRmExecutableCompiler.CreateStandardReference(
            outputInput, tables.Modules.GetIdentity(prefixModule), [],
            ["PrefixKey", "BoxKey"]);

        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty),
            TrRmExecutableCompiler.CreateStandardReferenceSet([
                reloadedA, reloadedB,
            ]));
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ReemitsBodylessMethodImportsFromSeparateRootArtifacts(
        string schemaIdentity)
    {
        var provider = CreateRelocatedClassProvider("MethodImportProvider",
            schemaIdentity, "LeafAliasKey", includeReadMethod: true);
        var rootA = CreateRootArtifactWithTwoClassImports("MethodRootA",
            "MethodImportProvider", "BoxAKey", "LeafKey", "LeafAliasKey",
            schemaIdentity, firstMethodExportKey: "Leaf.Read",
            secondMethodExportKey: "Leaf.ReadAlias");
        var rootB = CreateRootArtifactWithTwoClassImports("MethodRootB",
            "MethodImportProvider", "BoxBKey", "LeafKey", "LeafAliasKey",
            schemaIdentity, firstMethodExportKey: "Leaf.Read",
            secondMethodExportKey: "Leaf.ReadAlias");
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            provider);
        var referenceA = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootA),
            "MethodRootA", [providerInput], ["BoxAKey"]);
        var referenceB = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootB),
            "MethodRootB", [providerInput], ["BoxBKey"]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            string.Empty), TrRmExecutableCompiler.CreateStandardReferenceSet([
                referenceA, referenceB,
            ]));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "MethodImportsReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        Assert.That(tables.Modules.Count, Is.EqualTo(2));
        Assert.That(tables.Imports.Count, Is.EqualTo(8));
        Assert.That(tables.Methods.Count, Is.EqualTo(4));
        Assert.That(Enumerable.Range(1, tables.Methods.Count)
                .All(row => tables.Methods.GetBodyRow(row) == 0),
            Is.True);
        Assert.That(tables.Signatures.Count, Is.EqualTo(4));
        Assert.That(tables.Signatures.ParamCount, Is.EqualTo(4));
        Assert.That(Enumerable.Range(1, tables.Imports.Count)
                .Where(row => tables.Imports
                    .GetExpectedDefinitionForImportRow(row) >> 24
                    == 0x16)
                .Select(row => tables.Imports
                    .GetBindingForImportRow(row).ExportKey),
            Is.EquivalentTo([
                "Leaf.Read", "Leaf.ReadAlias",
                "Leaf.Read", "Leaf.ReadAlias",
            ]));

        string? moduleAIdentity = null;
        string? moduleBIdentity = null;
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            foreach (var export in tables.Exports
                .GetExportsForModule(moduleRow))
            {
                if (export.ExportKey == "BoxAKey")
                {
                    moduleAIdentity = tables.Modules.GetIdentity(moduleRow);
                }
                else if (export.ExportKey == "BoxBKey")
                {
                    moduleBIdentity = tables.Modules.GetIdentity(moduleRow);
                }
            }
        }
        Assert.That(moduleAIdentity, Is.Not.Null);
        Assert.That(moduleBIdentity, Is.Not.Null);
        var outputInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var reloadedA = TrRmExecutableCompiler.CreateStandardReference(
            outputInput, moduleAIdentity!, [providerInput], ["BoxAKey"]);
        var reloadedB = TrRmExecutableCompiler.CreateStandardReference(
            outputInput, moduleBIdentity!, [providerInput], ["BoxBKey"]);
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty),
            TrRmExecutableCompiler.CreateStandardReferenceSet([
                reloadedA, reloadedB,
            ]));
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));

        var incompatibleProvider = CreateRelocatedClassProvider(
            "MethodImportProvider", schemaIdentity, "LeafAliasKey",
            includeReadMethod: false, includePrefix: false);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(outputInput,
                moduleAIdentity!, [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProvider),
                ], ["BoxAKey"]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ReemitsMethodImportDefaultsAcrossSeparateRootArtifacts(
        string schemaIdentity)
    {
        var provider = CreateRelocatedClassProvider("MethodDefaultsProvider",
            schemaIdentity, "LeafAliasKey", includeReadMethod: true,
            readDefaultValue: 9, includePrefix: false);
        var rootA = CreateRootArtifactWithTwoClassImports("DefaultsRootA",
            "MethodDefaultsProvider", "DefaultsAKey", "LeafKey",
            "LeafAliasKey", schemaIdentity,
            firstMethodExportKey: "Leaf.ReadAlias",
            secondMethodExportKey: "Leaf.Read",
            firstMethodDefault: 7, secondMethodDefault: 13);
        var rootB = CreateRootArtifactWithTwoClassImports("DefaultsRootB",
            "MethodDefaultsProvider", "DefaultsBKey", "LeafKey",
            "LeafAliasKey", schemaIdentity,
            firstMethodExportKey: "Leaf.Read",
            secondMethodExportKey: "Leaf.ReadAlias",
            firstMethodDefault: 17, secondMethodDefault: 19);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            provider);
        var referenceA = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootA),
            "DefaultsRootA", [providerInput], ["DefaultsAKey"]);
        var referenceB = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, rootB),
            "DefaultsRootB", [providerInput], ["DefaultsBKey"]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            string.Empty), TrRmExecutableCompiler.CreateStandardReferenceSet([
                referenceA, referenceB,
            ]));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "MethodDefaultsReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        Assert.That(tables.Methods.Count, Is.EqualTo(4));
        Assert.That(tables.Imports.Count, Is.EqualTo(8));
        Assert.That(GetSection(artifact, schemaIdentity, 0x0066)
                .RecordCount,
            Is.EqualTo(4u));
        Assert.That(Enumerable.Range(1, tables.Methods.Count)
                .All(row => tables.Methods.GetBodyRow(row) == 0),
            Is.True);

        var rootAIdentity = FindModuleIdentityForExport(tables,
            "DefaultsAKey");
        var rootBIdentity = FindModuleIdentityForExport(tables,
            "DefaultsBKey");
        var outputInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var reloadedA = TrRmExecutableCompiler.CreateStandardReference(
            outputInput, rootAIdentity, [providerInput], ["DefaultsAKey"]);
        var reloadedB = TrRmExecutableCompiler.CreateStandardReference(
            outputInput, rootBIdentity, [providerInput], ["DefaultsBKey"]);
        var reloaded = TrRmExecutableCompiler.CreateStandardReferenceSet([
            reloadedA, reloadedB,
        ]);
        var importedLeaf = reloaded.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Leaf");
        Assert.That(importedLeaf.Expectations
                .Select(expectation =>
                    expectation.ExpectedDeclaration.Methods.Single(
                        static method => method.Name == "Read")
                        .Parameters.Single().DefaultValue!.Value),
            Is.EquivalentTo([7, 13, 17, 19]));
        Assert.That(importedLeaf.Expectations.SelectMany(
                static expectation => expectation.MethodExpectations)
            .Select(static expectation => expectation.ExportKey),
            Is.EquivalentTo([
                "Leaf.Read", "Leaf.ReadAlias",
                "Leaf.Read", "Leaf.ReadAlias",
            ]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ReemitsClassEqualityOwnedByAnotherCoLocatedModule(
        string schemaIdentity)
    {
        var provider = CreateRelocatedClassProvider("Provider",
            schemaIdentity, "LeafAliasKey");
        var root = CreateRootArtifactWithTwoClassImports("Root", "Provider",
            "BoxKey", "LeafKey", "LeafAliasKey", schemaIdentity,
            includeEquality: true, uncalledEqualityImports: true);
        root = MoveClassEqualityOwnerToSecondModule(root, schemaIdentity,
            "RootEqualityOwner");
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            provider);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, root), "Root",
            [providerInput], ["BoxKey"]);
        var leaf = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Leaf");
        Assert.That(leaf.TypeDefBindingEqualityGroups[0].OwnerModule?
                .ModuleIdentity,
            Is.EqualTo("RootEqualityOwner"));

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "RootEqualityOwnerReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        var leafRows = new List<int>();
        for (var row = 1; row <= tables.TypeDefs.Count; row++)
        {
            if (StringComparer.Ordinal.Equals(
                    tables.TypeDefs.GetName(row), "Leaf"))
            {
                leafRows.Add(row);
            }
        }

        Assert.That(leafRows, Has.Count.EqualTo(2));
        foreach (var leafRow in leafRows)
        {
            var (fieldStart, fieldEnd)
                = tables.TypeDefs.GetFieldRange(leafRow);
            Assert.That(fieldEnd - fieldStart, Is.EqualTo(1));
            Assert.That(tables.Fields.GetName(fieldStart),
                Is.EqualTo("Value"));
            var (layoutStart, layoutCount)
                = tables.TypeDefs.GetLayoutRange(leafRow);
            Assert.That(layoutCount, Is.EqualTo(1));
            Assert.That(tables.Layouts.GetOwnerTypeDef(layoutStart),
                Is.EqualTo(leafRow));
        }

        Assert.That(tables.TypeDefBindingEqualities.Count, Is.EqualTo(1));
        Assert.That(tables.TypeDefBindingEqualities.GetOwnerModule(1),
            Is.EqualTo(1u));
        Assert.That(tables.TypeDefBindingEqualities.GetLeftTypeDef(1),
            Is.EqualTo(2u));
        Assert.That(tables.TypeDefBindingEqualities.GetRightTypeDef(1),
            Is.EqualTo(3u));

        var artifactInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var relocatedProvider = CreateRelocatedClassProvider("Provider",
            schemaIdentity, "LeafAliasKey", includePrefix: false);
        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(artifactInput,
                "RootEqualityOwnerReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        relocatedProvider),
                ], ["BoxKey"]);
        var reloadedLeaf = reloadedReference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Leaf");
        Assert.That(reloadedLeaf.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));
        Assert.That(reloadedLeaf.TypeDefBindingEqualityGroups[0]
                .OwnerModule?.ModuleIdentity,
            Is.EqualTo("RootEqualityOwner"));

        var splitProvider = CreateSplitClassAliasProvider(provider,
            schemaIdentity, "LeafAliasKey");
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(artifactInput,
                "RootEqualityOwnerReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        splitProvider),
                ], ["BoxKey"]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void PublishesDistinctClassViewsWithExactImportsAndReloads(
        string schemaIdentity)
    {
        var provider = CreateRelocatedClassProvider("Provider",
            schemaIdentity, "LeafAliasKey");
        var root = CreateRootArtifactWithTwoClassImports("Root", "Provider",
            "BoxKey", "LeafKey", "LeafAliasKey", schemaIdentity);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            provider);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity, root);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            rootInput, "Root", [providerInput], ["BoxKey"]);

        var tree = SyntaxTree.Parse("""
            public class Container {
                public Box Value;
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declaration = tree.Root.Children.OfType<SyntaxNode>().Single(
            static node => node.Kind == SyntaxKind.ClassDeclaration);
        var classIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Container");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ClassSourcePublisher", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, classIndex,
                    declaration.Span, "ContainerKey", []),
            ]);
        var tables = ReadTables(artifact, schemaIdentity);
        var leafRows = Enumerable.Range(1, tables.TypeDefs.Count)
            .Where(row => tables.TypeDefs.GetName(row) == "Leaf")
            .ToArray();
        Assert.That(leafRows, Has.Length.EqualTo(2));

        var leafImportRows = new Dictionary<int, int>();
        foreach (var leafRow in leafRows)
        {
            var imports = Enumerable.Range(1, tables.Imports.Count)
                .Where(importRow =>
                    tables.Imports.GetExpectedDefinitionForImportRow(
                        importRow)
                    == (0x1300_0000u | checked((uint)leafRow)))
                .ToArray();
            Assert.That(imports, Has.Length.EqualTo(1));
            leafImportRows.Add(leafRow, imports[0]);
            Assert.That(tables.ModuleRefs.GetProducerIdentity(
                    tables.Imports.GetModuleRefForImportRow(imports[0])),
                Is.EqualTo("Provider"));

            var (fieldStart, fieldEnd)
                = tables.TypeDefs.GetFieldRange(leafRow);
            Assert.That(fieldEnd - fieldStart, Is.EqualTo(1));
            Assert.That(tables.Fields.GetOwnerTypeDef(fieldStart),
                Is.EqualTo(leafRow));
            var (layoutStart, layoutCount)
                = tables.TypeDefs.GetLayoutRange(leafRow);
            Assert.That(layoutCount, Is.EqualTo(1));
            Assert.That(tables.Layouts.GetOwnerTypeDef(layoutStart),
                Is.EqualTo(leafRow));
            var layoutFieldRow = Enumerable.Range(1,
                    tables.LayoutFields.Count)
                .Single(row => tables.LayoutFields.GetOwnerLayout(row)
                    == layoutStart);
            Assert.That(tables.LayoutFields.GetFieldDef(layoutFieldRow),
                Is.EqualTo(fieldStart));
        }

        Assert.That(leafImportRows.Values.Select(importRow =>
                tables.Imports.GetBindingForImportRow(importRow).ExportKey),
            Is.EquivalentTo(["LeafKey", "LeafAliasKey"]));
        var boxRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Box");
        var (boxFieldStart, boxFieldEnd)
            = tables.TypeDefs.GetFieldRange(boxRow);
        Assert.That(boxFieldEnd - boxFieldStart, Is.EqualTo(2));
        var boxFieldImportRows = Enumerable.Range(boxFieldStart,
                boxFieldEnd - boxFieldStart)
            .Select(fieldRow => tables.TypeSpecs.GetTypeSpecRef(
                tables.Fields.GetTypeSpecRef(fieldRow)))
            .Select(typeSpec => {
                Assert.That(typeSpec.Form, Is.EqualTo((byte)0x03));
                return checked((int)typeSpec.Row);
            })
            .ToArray();
        Assert.That(boxFieldImportRows.Distinct(), Has.Length.EqualTo(2));
        Assert.That(boxFieldImportRows,
            Is.EquivalentTo(leafImportRows.Values));

        Assert.That(tables.TypeDefBindingEqualities.Count, Is.EqualTo(1));
        Assert.That(tables.TypeDefBindingEqualities.GetOwnerModule(1),
            Is.EqualTo(1u));
        Assert.That(tables.TypeDefBindingEqualities.GetLeftTypeDef(1),
            Is.EqualTo(checked((uint)leafRows.Min())));
        Assert.That(tables.TypeDefBindingEqualities.GetRightTypeDef(1),
            Is.EqualTo(checked((uint)leafRows.Max())));

        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ClassSourcePublisher", [rootInput, providerInput],
                ["ContainerKey"]);
        var reloadedLeaf = reloadedReference.StandardClassDeclarations
            .Single(static candidate =>
                candidate.Declaration.Name == "Leaf");
        Assert.That(reloadedLeaf.Expectations, Has.Length.EqualTo(2));
        Assert.That(reloadedLeaf.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));
        Assert.That(reloadedLeaf.TypeDefBindingEqualityGroups[0]
                .OwnerModule?.ModuleIdentity,
            Is.EqualTo("ClassSourcePublisher"));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void PublishesMethodImportsForEachClassViewOwner(
        string schemaIdentity)
    {
        var schema = TrRmExecutableCompiler.GetForeignInvocationSchema(
            schemaIdentity);
        var hasClassParameterDefaults
            = schema.UsesClassParameterDefaults();
        var provider = CreateRelocatedClassProvider("Provider",
            schemaIdentity, "LeafAliasKey", includeReadMethod: true,
            readDefaultValue: hasClassParameterDefaults ? 9 : null,
            includePrefix: false);
        var root = CreateRootArtifactWithTwoClassImports("Root", "Provider",
            "BoxKey", "LeafKey", "LeafAliasKey", schemaIdentity,
            firstMethodExportKey: "Leaf.ReadAlias",
            secondMethodExportKey: "Leaf.Read",
            firstMethodDefault: hasClassParameterDefaults ? 7 : null,
            secondMethodDefault: hasClassParameterDefaults ? 13 : null);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            provider);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity, root);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            rootInput, "Root", [providerInput], ["BoxKey"]);
        var leaf = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Leaf");
        Assert.That(leaf.Expectations, Has.Length.EqualTo(2));
        Assert.That(leaf.Expectations.All(expectation =>
                expectation.MethodExpectations.Length == 1
                && expectation.MethodExpectations[0].OwnerImportRow
                    == expectation.Binding.ImportRow),
            Is.True);

        var tree = SyntaxTree.Parse("""
            public class Container {
                public Box Value;
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declaration = tree.Root.Children.OfType<SyntaxNode>().Single(
            static node => node.Kind == SyntaxKind.ClassDeclaration);
        var classIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Container");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ClassMethodSourcePublisher", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, classIndex,
                    declaration.Span, "ContainerKey", []),
            ]);
        var tables = ReadTables(artifact, schemaIdentity);
        var leafRows = Enumerable.Range(1, tables.TypeDefs.Count)
            .Where(row => tables.TypeDefs.GetName(row) == "Leaf")
            .ToArray();
        Assert.That(leafRows, Has.Length.EqualTo(2));

        foreach (var leafRow in leafRows)
        {
            var typeImportRow = Enumerable.Range(1, tables.Imports.Count)
                .Single(importRow =>
                    tables.Imports.GetExpectedDefinitionForImportRow(
                        importRow)
                    == (0x1300_0000u | checked((uint)leafRow)));
            var typeKey = tables.Imports
                .GetBindingForImportRow(typeImportRow).ExportKey;
            Assert.That(typeKey, Is.AnyOf("LeafKey", "LeafAliasKey"));
            Assert.That(tables.ModuleRefs.GetProducerIdentity(
                    tables.Imports.GetModuleRefForImportRow(typeImportRow)),
                Is.EqualTo("Provider"));

            var methodRow = Enumerable.Range(1, tables.Methods.Count)
                .Single(row => tables.Methods.GetOwnerType(row) == leafRow);
            Assert.That(tables.Methods.GetName(methodRow), Is.EqualTo("Read"));
            Assert.That(tables.Methods.GetBodyRow(methodRow), Is.Zero);
            var methodImportRow = Enumerable.Range(1, tables.Imports.Count)
                .Single(importRow =>
                    tables.Imports.GetExpectedDefinitionForImportRow(
                        importRow)
                    == (0x1600_0000u | checked((uint)methodRow)));
            Assert.That(tables.ModuleRefs.GetProducerIdentity(
                    tables.Imports.GetModuleRefForImportRow(
                        methodImportRow)),
                Is.EqualTo("Provider"));
            Assert.That(tables.Imports.GetBindingForImportRow(methodImportRow)
                    .ExportKey,
                Is.EqualTo(typeKey == "LeafAliasKey"
                    ? "Leaf.ReadAlias" : "Leaf.Read"));

            var signatureRow = tables.Methods.GetSignature(methodRow);
            Assert.That(tables.Signatures.GetParamRange(signatureRow).Count,
                Is.EqualTo(1));
            Assert.That(tables.TypeSpecs.GetNominalTypeDefRow(
                    tables.Signatures.GetReceiverTypeSpec(signatureRow),
                    tables.TypeDefs, tables.Imports),
                Is.EqualTo(leafRow));
        }

        Assert.That(tables.TypeDefBindingEqualities.Count, Is.EqualTo(1));
        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ClassMethodSourcePublisher", [rootInput, providerInput],
                ["ContainerKey"]);
        var reloadedLeaf = reloadedReference.StandardClassDeclarations
            .Single(static candidate =>
                candidate.Declaration.Name == "Leaf");
        Assert.That(reloadedLeaf.Expectations, Has.Length.EqualTo(2));
        Assert.That(reloadedLeaf.Expectations.SelectMany(
                static expectation => expectation.MethodExpectations)
            .Select(static expectation => expectation.ExportKey),
            Is.EquivalentTo(["Leaf.Read", "Leaf.ReadAlias"]));
        Assert.That(reloadedLeaf.Expectations.All(expectation =>
                expectation.MethodExpectations.All(methodExpectation =>
                    methodExpectation.OwnerImportRow
                        == expectation.Binding.ImportRow)),
            Is.True);
        if (hasClassParameterDefaults)
        {
            Assert.That(reloadedLeaf.Expectations.Select(expectation =>
                    expectation.ExpectedDeclaration.Methods.Single(
                        static method => method.Name == "Read")
                        .Parameters.Single().DefaultValue!.Value),
                Is.EquivalentTo([7, 13]));
        }
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void PublishesFreshClassLookupAlongsideImportedViews(
        string schemaIdentity)
    {
        var provider = CreateRelocatedClassProvider("Provider",
            schemaIdentity, "LeafAliasKey");
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            provider);
        var root = CreateRootArtifactWithTwoClassImports("Root", "Provider",
            "BoxKey", "LeafKey", "LeafAliasKey", schemaIdentity);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity, root);
        var importedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                rootInput, "Root", [providerInput], ["BoxKey"]);
        var lookupReference
            = TrRmExecutableCompiler.CreateStandardReference(
                providerInput, "Provider", [], ["LeafAliasKey"]);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet([
            importedReference, lookupReference,
        ]);
        var leaf = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Leaf");
        Assert.That(leaf.Expectations, Has.Length.EqualTo(2));
        Assert.That(leaf.TypeDefBindingEqualityGroups.SelectMany(
                static group => group.Endpoints)
            .Any(static endpoint => endpoint.ImportRow == 0),
            Is.True);

        var tree = SyntaxTree.Parse("""
            public class Container {
                public Leaf Value;
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declaration = tree.Root.Children.OfType<SyntaxNode>().Single(
            static node => node.Kind == SyntaxKind.ClassDeclaration);
        var classIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Container");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "MixedClassSourcePublisher", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, classIndex,
                    declaration.Span, "ContainerKey", []),
            ]);
        var tables = ReadTables(artifact, schemaIdentity);
        var leafRows = Enumerable.Range(1, tables.TypeDefs.Count)
            .Where(row => tables.TypeDefs.GetName(row) == "Leaf")
            .Order().ToArray();
        Assert.That(leafRows, Has.Length.EqualTo(3));
        var leafImports = leafRows.Select(leafRow =>
        {
            var importRow = Enumerable.Range(1, tables.Imports.Count)
                .Single(row =>
                    tables.Imports.GetExpectedDefinitionForImportRow(row)
                    == (0x1300_0000u | checked((uint)leafRow)));
            return (ImportRow: importRow, ExportKey: tables.Imports
                .GetBindingForImportRow(importRow).ExportKey);
        }).ToArray();
        Assert.That(leafImports.Select(static import => import.ExportKey),
            Is.EquivalentTo([
                "LeafKey", "LeafAliasKey", "LeafAliasKey",
            ]));
        Assert.That(leafImports.All(import =>
                tables.ModuleRefs.GetProducerIdentity(
                    tables.Imports.GetModuleRefForImportRow(
                        import.ImportRow)) == "Provider"),
            Is.True);
        Assert.That(tables.TypeDefBindingEqualities.Count,
            Is.EqualTo(2));
        Assert.That(Enumerable.Range(1,
                tables.TypeDefBindingEqualities.Count)
            .All(row => tables.TypeDefBindingEqualities
                .GetOwnerModule(row) == 1),
            Is.True);

        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "MixedClassSourcePublisher", [rootInput, providerInput],
                ["ContainerKey"]);
        var reloadedLeaf = reloadedReference.StandardClassDeclarations
            .Single(static candidate =>
                candidate.Declaration.Name == "Leaf");
        Assert.That(reloadedLeaf.Expectations, Has.Length.EqualTo(3));
        Assert.That(reloadedLeaf.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));
        Assert.That(reloadedLeaf.TypeDefBindingEqualityGroups[0].Endpoints,
            Has.Length.EqualTo(3));
        Assert.That(reloadedLeaf.TypeDefBindingEqualityGroups[0]
                .OwnerModule?.ModuleIdentity,
            Is.EqualTo("MixedClassSourcePublisher"));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void PreservesProviderOwnedEqualityGroupsAcrossClassReemission(
        string schemaIdentity)
    {
        var provider = CreateRootArtifactWithTwoClassImports("Provider",
            "LeafProvider", "BoxKey", "LeafKey", "LeafAliasKey",
            schemaIdentity, includeEquality: true,
            uncalledEqualityImports: true);
        var leafProvider = CreateRelocatedClassProvider("LeafProvider",
            schemaIdentity, "LeafAliasKey");
        var root = CreateArtifact("Root", "Holder", "HolderKey",
            0xF200_0008, schemaIdentity, "Provider", "BoxKey",
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            layoutKind: 0, pack: 0, useRequestedLayoutForImports: true,
            importedTypeName: "Box");

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, root), "Root", [
                new TrRmStandardArtifactInput(schemaIdentity, provider),
                new TrRmStandardArtifactInput(schemaIdentity, leafProvider),
            ], ["HolderKey"]);
        var leaf = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Leaf");
        Assert.That(leaf.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));
        Assert.That(leaf.TypeDefBindingEqualityGroups[0].OwnerModule?
                .ModuleIdentity,
            Is.EqualTo("Provider"));

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public void Use(Holder value) {}"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!, "RootReemitted",
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "RootReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity, provider),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafProvider),
                ], ["HolderKey"]);
        var reloadedLeaf = reloadedReference.StandardClassDeclarations
            .Single(static declaration =>
                declaration.Declaration.Name == "Leaf");
        Assert.That(reloadedLeaf.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));
        Assert.That(reloadedLeaf.TypeDefBindingEqualityGroups[0]
                .OwnerModule?.ModuleIdentity,
            Is.EqualTo("Provider"));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void RejectsProviderTypeSplitForProviderOwnedEqualityGroup(
        string schemaIdentity)
    {
        var provider = CreateRootArtifactWithTwoClassImports("Provider",
            "LeafProviderA", "BoxKey", "LeafKey", "LeafAliasKey",
            schemaIdentity, includeEquality: true,
            uncalledEqualityImports: true,
            secondProviderIdentity: "LeafProviderB");
        var leafProviderA = CreateClassArtifact("LeafProviderA", "Leaf",
            "LeafKey", 0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0);
        var leafProviderB = CreateClassArtifact("LeafProviderB", "Leaf",
            "LeafAliasKey", 0xF200_0008, schemaIdentity, layoutKind: 0,
            pack: 0);
        var root = CreateArtifact("Root", "Holder", "HolderKey",
            0xF200_0008, schemaIdentity, "Provider", "BoxKey",
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            layoutKind: 0, pack: 0, useRequestedLayoutForImports: true,
            importedTypeName: "Box");

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, root),
                "Root", [
                    new TrRmStandardArtifactInput(schemaIdentity, provider),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafProviderA),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafProviderB),
                ], ["HolderKey"]));
    }

    [Test]
    public void BindsClassSchemaStructEqualityWithoutCopyOrBlittableProof()
    {
        var schemaIdentity
            = StandardGenericMethodSchema
                .C1M1Construction2ClassTypeBindingViews;
        var provider = CreateRelocatedClassProvider("Provider",
            schemaIdentity, "LeafAliasKey");
        SetTypeDefFlags(provider, 0x06, typeDefRow: 2);
        var consumer = CreateRootArtifactWithTwoClassImports("Root",
            "Provider", "BoxKey", "LeafKey", "LeafAliasKey",
            schemaIdentity, useStructLeaves: true,
            includeEquality: true);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, consumer),
            "Root",
            [new TrRmStandardArtifactInput(schemaIdentity, provider)],
            ["BoxKey"]);
        var leaf = reference.GenericNominalTypeDefinitions.Single(
            static definition => definition.Name == "Leaf");
        Assert.That(leaf.NominalKind, Is.EqualTo(1));
        Assert.That(leaf.ProviderExpectations, Has.Length.EqualTo(2));
        Assert.That(leaf.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public void Use(Box value) {}"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!, "StructEqualityReemitted",
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(artifact, schemaIdentity);
        Assert.That(tables.TypeDefBindingEqualities.Count, Is.EqualTo(1));
        Assert.That(tables.TypeDefs.GetKind(2), Is.EqualTo(1));
        Assert.That(tables.TypeDefs.GetKind(3), Is.EqualTo(1));
        Assert.That(leaf.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));

        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "StructEqualityReemitted",
                [new TrRmStandardArtifactInput(schemaIdentity, provider)],
                ["BoxKey"]);
        var reloadedLeaf = reloadedReference.GenericNominalTypeDefinitions
            .Single(static definition => definition.Name == "Leaf");
        Assert.That(reloadedLeaf.NominalKind, Is.EqualTo(1));
        Assert.That(reloadedLeaf.ProviderExpectations, Has.Length.EqualTo(2));
        Assert.That(reloadedLeaf.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Box value) {}"),
            reloadedReference);
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));
    }

    [Test]
    public void BindsImportedClassDeclarationsWithoutExecutableClassLayout()
    {
        var provider = CreateClassArtifact("Provider", "Leaf", "LeafKey",
            0xF200_0008);
        var consumer = CreateRootArtifact("Root", "Box", "BoxKey",
            0xF200_0008);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(C1M1SchemaIdentity, consumer),
            "Root",
            [new TrRmStandardArtifactInput(C1M1SchemaIdentity, provider)],
            ["BoxKey"]);

        Assert.That(reference.Classes, Is.Empty);
        Assert.That(reference.StandardClassDeclarations.Select(
                static declaration => declaration.Declaration.Name),
            Is.EquivalentTo(["Box", "Leaf"]));
        var importedClass = reference.StandardClassDeclarations.Single(
            static declaration => declaration.Declaration.Name == "Leaf");
        Assert.That(importedClass.ExportKeys, Is.EqualTo(["LeafKey"]));
        Assert.That(importedClass.Expectations, Has.Length.EqualTo(1));
        Assert.That(importedClass.Expectations[0].ExpectedDeclaration.Name,
            Is.EqualTo("Leaf"));
        Assert.That(importedClass.Expectations[0].Binding.ImportRow,
            Is.EqualTo(1));
        Assert.That(importedClass.Expectations[0].Binding.Provider
            .ArtifactSnapshot.AsSpan().SequenceEqual(provider), Is.True);
        Assert.That(importedClass.Declaration.TryGetDeclaredMutability(
            out var isMutable), Is.True);
        Assert.That(isMutable, Is.False);

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public void Use(Box value) {}"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var closureTypeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Leaf value) {}"), reference);
        Assert.That(closureTypeCompilation.HasErrors, Is.True);
    }

    [Test]
    public void ReemitsImportedStructFieldDependencies()
    {
        var provider = CreateClassArtifact("Provider", "Leaf", "LeafKey",
            0xF200_0008);
        SetTypeDefFlags(provider, 0x06);
        var consumer = CreateRootArtifact("Root", "Box", "BoxKey",
            0xF200_0008);
        SetTypeDefFlags(consumer, 0x06, typeDefRow: 2);

        var reference = CreateReference(consumer, provider,
            C1M1SchemaIdentity);
        var importedStruct = reference.GenericNominalTypeDefinitions
            .Single(static definition => definition.Name == "Leaf");
        Assert.That(importedStruct.NominalKind, Is.EqualTo(1));

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Box value) {}"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!, "RootReemitted",
            C1M1SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(reemitted, C1M1SchemaIdentity);
        Assert.That(tables.TypeDefs.Count, Is.EqualTo(2));
        Assert.That(tables.TypeDefs.GetKind(2), Is.EqualTo(1));
        Assert.That(tables.Imports.Count, Is.EqualTo(1));
        Assert.That(tables.Imports.GetExpectedDefinitionForImportRow(1),
            Is.EqualTo(0x1300_0002u));
        Assert.That(tables.Methods.Count, Is.EqualTo(1));
        Assert.That(tables.Exports.GetExportsForModule(1)
                .Select(static export => export.Key),
            Is.EquivalentTo(["BoxKey", "Use"]));

        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    reemitted),
                "RootReemitted",
                [new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    provider)],
                ["BoxKey"]);
        var boxCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Box value) {}"),
            reemittedReference);
        Assert.That(boxCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, boxCompilation.Diagnostics));
        var directStructUse = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Leaf value) {}"),
            reemittedReference);
        Assert.That(directStructUse.HasErrors, Is.True,
            "The provider-only struct dependency is not a source lookup root.");

        var changedFieldTypeProvider = CreateClassArtifact("Provider",
            "Leaf", "LeafKey", 0xF200_000A);
        SetTypeDefFlags(changedFieldTypeProvider, 0x06);
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateReference(consumer, changedFieldTypeProvider,
                C1M1SchemaIdentity));

        var changedProvider = CreateClassArtifact("Provider", "Leaf",
            "LeafKey", 0xF200_0008, layoutKind: 1, pack: 2);
        SetTypeDefFlags(changedProvider, 0x06);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    reemitted),
                "RootReemitted",
                [new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    changedProvider)],
                ["BoxKey"]));
    }

    [Test]
    public void EmitsClassStubsForImportedStructProviderExpectations()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var provider = CreateClassArtifact("Provider", "Leaf", "LeafKey",
            0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0);
        SetTypeDefFlags(provider, 0x06);
        var root = CreateRootArtifact("Root", "Box", "BoxKey",
            0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0,
            useRequestedLayoutForImports: true);
        SetTypeDefFlags(root, 0x06);
        SetTypeDefFlags(root, 0x06, typeDefRow: 2);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, root), "Root",
            [new TrRmStandardArtifactInput(schemaIdentity, provider)],
            ["BoxKey"]);
        var importedLeaf = reference.Structures.Single(
            static structure => structure.Name == "Leaf");
        Assert.That(importedLeaf.StandardTypeOrigin!.Expectations,
            Is.Not.Empty);

        var tree = SyntaxTree.Parse("""
            public class Container {
                private Box _box;
                public Container(Box box) { _box = box; }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var constructor = classDeclaration.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ConstructorDeclaration);
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ImportedStructProviderExpectation",
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "ContainerKey", [
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Container.Initialize") {
                            ConstructionProjectionExportKey
                                = "Container.Create",
                        },
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        Assert.That(tables.Imports.Count, Is.EqualTo(2));
        Assert.That(tables.ModuleRefs.Count, Is.EqualTo(2));
        var boxTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Box");
        var leafTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Leaf");
        var boxImportRow = Enumerable.Range(1, tables.Imports.Count)
            .Single(row => tables.Imports
                .GetExpectedDefinitionForImportRow(row)
                == (0x1300_0000u | checked((uint)boxTypeDefRow)));
        var leafImportRow = Enumerable.Range(1, tables.Imports.Count)
            .Single(row => tables.Imports
                .GetExpectedDefinitionForImportRow(row)
                == (0x1300_0000u | checked((uint)leafTypeDefRow)));
        Assert.That(tables.ModuleRefs.GetProducerIdentity(
            tables.Imports.GetModuleRefForImportRow(boxImportRow)),
            Is.EqualTo("Root"));
        Assert.That(tables.ModuleRefs.GetProducerIdentity(
            tables.Imports.GetModuleRefForImportRow(leafImportRow)),
            Is.EqualTo("Provider"));
        var (boxFieldStart, _) = tables.TypeDefs.GetFieldRange(boxTypeDefRow);
        var boxFieldType = tables.TypeSpecs.GetTypeSpecRef(
            tables.Fields.GetTypeSpecRef(boxFieldStart));
        Assert.That(boxFieldType.Form, Is.EqualTo((byte)0x03));
        Assert.That(boxFieldType.Row, Is.EqualTo(leafImportRow));
        var containerTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Container");
        var (containerFieldStart, _) = tables.TypeDefs.GetFieldRange(
            containerTypeDefRow);
        var containerFieldType = tables.TypeSpecs.GetTypeSpecRef(
            tables.Fields.GetTypeSpecRef(containerFieldStart));
        Assert.That(containerFieldType.Form, Is.EqualTo((byte)0x03));
        Assert.That(containerFieldType.Row, Is.EqualTo(boxImportRow));

        var outputReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ImportedStructProviderExpectation", [
                    new TrRmStandardArtifactInput(schemaIdentity, root),
                    new TrRmStandardArtifactInput(schemaIdentity, provider),
                ], ["ContainerKey"]);
        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Container value) {}"),
            outputReference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var incompatibleProvider = CreateClassArtifact("Provider", "Leaf",
            "LeafKey", 0xF200_000A, schemaIdentity, layoutKind: 0, pack: 0);
        SetTypeDefFlags(incompatibleProvider, 0x06);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ImportedStructProviderExpectation", [
                    new TrRmStandardArtifactInput(schemaIdentity, root),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProvider),
                ], ["ContainerKey"]));
    }

    [Test]
    public void EmitsImportedClassHandleTypeSpecsInClassMethodSignatures()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var provider = CreateClassArtifact("ClassProvider", "Leaf",
            "LeafKey", 0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0,
            fieldExportKey: "Leaf.Value");
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, provider),
            "ClassProvider", [], ["LeafKey"], ["Leaf.Value"]);

        var tree = SyntaxTree.Parse("""
            public class Container {
                private int32 _value;
                public Container(int32 value) { _value = value; }
                public static void Accept(Leaf value) {}
                public static void AcceptOptional(Leaf? value) {}
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var accept = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Accept"));
        var acceptOptional = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "AcceptOptional"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ClassHandleConsumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "ContainerKey", [
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Container.Initialize") {
                            ConstructionProjectionExportKey
                                = "Container.Create",
                        },
                        new TrRmSourceClassMemberExport(0, accept.Span,
                            "Container.Accept"),
                        new TrRmSourceClassMemberExport(0,
                            acceptOptional.Span,
                            "Container.AcceptOptional"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        var importedClassRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Leaf");
        Assert.That(tables.TypeDefs.GetKind(importedClassRow),
            Is.EqualTo(2));
        var classImportRow = Enumerable.Range(1, tables.Imports.Count)
            .Single(row => tables.Imports
                .GetExpectedDefinitionForImportRow(row)
                == (0x1300_0000u | checked((uint)importedClassRow)));
        Assert.That(tables.ModuleRefs.GetProducerIdentity(
            tables.Imports.GetModuleRefForImportRow(classImportRow)),
            Is.EqualTo("ClassProvider"));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Container.Accept", out var acceptDefinition), Is.True);
        var signature = tables.Methods.GetSignature(
            checked((int)(acceptDefinition & 0x00FF_FFFF)));
        var (parameterFirst, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        Assert.That(parameterCount, Is.EqualTo(1));
        var parameterType = tables.TypeSpecs.GetTypeSpecRef(
            tables.Parameters.GetTypeSpec(parameterFirst));
        Assert.That(parameterType.Form, Is.EqualTo((byte)0x03));
        Assert.That(parameterType.Row, Is.EqualTo(classImportRow));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Container.AcceptOptional", out var optionalAcceptDefinition),
            Is.True);
        var optionalAcceptSignature = tables.Methods.GetSignature(
            checked((int)(optionalAcceptDefinition & 0x00FF_FFFF)));
        var (optionalParameterFirst, optionalParameterCount)
            = tables.Signatures.GetParamRange(optionalAcceptSignature);
        Assert.That(optionalParameterCount, Is.EqualTo(1));
        var optionalParameterTypeSpec
            = tables.Parameters.GetTypeSpec(optionalParameterFirst);
        var optionalParameterType = tables.TypeSpecs
            .GetTypeSpecRef(optionalParameterTypeSpec);
        Assert.That(optionalParameterType.Form, Is.EqualTo((byte)0x06));
        var optionalInnerTypeSpec = BinaryPrimitives.ReadUInt32LittleEndian(
            tables.TypeSpecs.GetRow(
                checked((int)optionalParameterTypeSpec)).Slice(2, 4));
        var optionalInnerType = tables.TypeSpecs
            .GetTypeSpecRef(optionalInnerTypeSpec);
        Assert.That(optionalInnerType.Form, Is.EqualTo((byte)0x03));
        Assert.That(optionalInnerType.Row, Is.EqualTo(classImportRow));

        var outputReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ClassHandleConsumer", [
                    new TrRmStandardArtifactInput(schemaIdentity, provider),
                ], ["ContainerKey"]);
        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Container value) {}"),
            outputReference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), outputReference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            sourceFreeCompilation.Program!, "ClassHandleReemitted",
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedIndex = TrRmArtifactIndex
            .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                reemitted);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(reemittedIndex);
        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "Container.AcceptOptional", out var reemittedMethod), Is.True);
        var reemittedSignature = reemittedTables.Methods.GetSignature(
            checked((int)(reemittedMethod & 0x00FF_FFFF)));
        var (reemittedParameterFirst, reemittedParameterCount)
            = reemittedTables.Signatures.GetParamRange(reemittedSignature);
        Assert.That(reemittedParameterCount, Is.EqualTo(1));
        var reemittedOptionalTypeSpec
            = reemittedTables.Parameters.GetTypeSpec(
                reemittedParameterFirst);
        Assert.That(reemittedTables.TypeSpecs
            .GetTypeSpecRef(reemittedOptionalTypeSpec).Form,
            Is.EqualTo((byte)0x06));
        var reemittedOptionalInner = BinaryPrimitives
            .ReadUInt32LittleEndian(reemittedTables.TypeSpecs.GetRow(
                checked((int)reemittedOptionalTypeSpec)).Slice(2, 4));
        var reemittedOptionalInnerType = reemittedTables.TypeSpecs
            .GetTypeSpecRef(reemittedOptionalInner);
        Assert.That(reemittedOptionalInnerType.Form,
            Is.EqualTo((byte)0x03));
        var reemittedClassImportRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(row =>
            {
                var expected = reemittedTables.Imports
                    .GetExpectedDefinitionForImportRow(row);
                return (expected >> 24) == 0x13
                    && reemittedTables.TypeDefs.GetName(
                        checked((int)(expected & 0x00FF_FFFF)))
                        == "Leaf";
            });
        Assert.That(reemittedOptionalInnerType.Row,
            Is.EqualTo(reemittedClassImportRow));
        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "ClassHandleReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity, provider),
                ], ["ContainerKey"]);
        var reemittedConsumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Container value) {}"),
            reemittedReference);
        Assert.That(reemittedConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reemittedConsumer.Diagnostics));

        var incompatibleProvider = CreateClassArtifact("ClassProvider",
            "Leaf", "LeafKey", 0xF200_000A, schemaIdentity,
            layoutKind: 0, pack: 0, fieldExportKey: "Leaf.Value");
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ClassHandleConsumer", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProvider),
                ], ["ContainerKey"]));
    }

    [Test]
    public void ReemitsOptionalImportedClassHandleFieldTypeSpecs()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var provider = CreateClassArtifact("OptionalFieldProvider", "Leaf",
            "LeafKey", 0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0,
            fieldExportKey: "Leaf.Value");
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            provider);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "OptionalFieldProvider", [], ["LeafKey"],
            ["Leaf.Value"]);
        var tree = SyntaxTree.Parse("""
            public class Container {
                public Leaf? Value;
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, providerReference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var fieldDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.FieldDeclaration);
        var classIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Container");
        var classExport = new TrRmSourceClassExport(program, classIndex,
            classDeclaration.Span, "ContainerKey", [
                new TrRmSourceClassMemberExport(classIndex,
                    fieldDeclaration.Span, "Container.Optional"),
            ]);
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "OptionalFieldConsumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [classExport]);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "OptionalFieldConsumer", [providerInput], ["ContainerKey"],
            ["Container.Optional"]);

        static uint GetOptionalFieldInnerTypeSpec(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
        {
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "Container.Optional", out var fieldDefId), Is.True);
            var fieldRow = checked((int)(fieldDefId & 0x00FF_FFFF));
            var typeSpecReference = tables.Fields.GetTypeSpec(fieldRow);
            var optionalType = tables.TypeSpecs
                .GetTypeSpecRef(typeSpecReference);
            Assert.That(optionalType.Form, Is.EqualTo((byte)0x06));
            return BinaryPrimitives.ReadUInt32LittleEndian(
                tables.TypeSpecs.GetRow(
                    checked((int)typeSpecReference)).Slice(2, 4));
        }

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        var optionalInnerTypeSpec = GetOptionalFieldInnerTypeSpec(tables);
        var optionalInnerType = tables.TypeSpecs
            .GetTypeSpecRef(optionalInnerTypeSpec);
        Assert.That(optionalInnerType.Form, Is.EqualTo((byte)0x03));
        Assert.That(tables.ModuleRefs.GetProducerIdentity(
            tables.Imports.GetModuleRefForImportRow(
                checked((int)optionalInnerType.Row))),
            Is.EqualTo("OptionalFieldProvider"));

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            sourceFreeCompilation.Program!, "OptionalFieldReemitted",
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedIndex = TrRmArtifactIndex
            .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                reemitted);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(reemittedIndex);
        var reemittedOptionalInnerTypeSpec
            = GetOptionalFieldInnerTypeSpec(reemittedTables);
        var reemittedOptionalInnerType = reemittedTables.TypeSpecs
            .GetTypeSpecRef(reemittedOptionalInnerTypeSpec);
        Assert.That(reemittedOptionalInnerType.Form,
            Is.EqualTo((byte)0x03));
        var reemittedClassImportRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(row =>
            {
                var expected = reemittedTables.Imports
                    .GetExpectedDefinitionForImportRow(row);
                return (expected >> 24) == 0x13
                    && reemittedTables.TypeDefs.GetName(
                        checked((int)(expected & 0x00FF_FFFF)))
                        == "Leaf";
            });
        Assert.That(reemittedOptionalInnerType.Row,
            Is.EqualTo(reemittedClassImportRow));
        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "OptionalFieldReemitted", [providerInput], ["ContainerKey"],
                ["Container.Optional"]);
        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Container value) {}"),
            reemittedReference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
    }

    [Test]
    public void ReemitsImportedFieldMapBodiesWithoutSource()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var provider = CreateClassArtifact("FieldProvider", "Leaf",
            "LeafKey", 0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0,
            fieldExportKey: "Leaf.Value");
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            provider);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "FieldProvider", [], ["LeafKey"],
            ["Leaf.Value"]);
        var tree = SyntaxTree.Parse("""
            public class Container {
                public int32 Marker;
                public int32 Read(Leaf leaf) { return leaf.Value; }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var read = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Read"));
        var containerClassIndex = Enumerable.Range(0,
                program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Container");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "FieldConsumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, containerClassIndex,
                    classDeclaration.Span, "ContainerKey", [
                        new TrRmSourceClassMemberExport(containerClassIndex,
                            field.Span, "Container.Marker"),
                        new TrRmSourceClassMemberExport(containerClassIndex,
                            read.Span, "Container.Read"),
                    ]),
            ]);
        var artifactInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var artifactTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        var artifactFieldImportRow = Enumerable.Range(1,
                artifactTables.Imports.Count)
            .Single(row => artifactTables.Imports
                .GetExpectedDefinitionForImportRow(row) >> 24 == 0x14);
        Assert.That(artifactTables.Imports
                .GetBindingForImportRow(artifactFieldImportRow).ExportKey,
            Is.EqualTo("Leaf.Value"));
        Assert.That(artifactTables.Exports.TryGetDefinitionForKey(1,
            "Container.Read", out var artifactReadDefinition), Is.True);
        var artifactReadMethodRow = checked((int)(
            artifactReadDefinition & 0x00FF_FFFF));
        var artifactReadBody = artifactTables.Bodies.GetRegisterBody(
            artifactTables.Methods.GetBodyRow(artifactReadMethodRow),
            artifactTables.Contracts.Count);
        var artifactFieldMapToken
            = BinaryPrimitives.ReadUInt32LittleEndian(
                artifactReadBody.FieldMap);
        Assert.That(artifactFieldMapToken >> 24, Is.EqualTo(0x1Cu));
        Assert.That(artifactFieldMapToken & 0x00FF_FFFF,
            Is.EqualTo((uint)artifactFieldImportRow));

        var sourceFreeReference
            = TrRmExecutableCompiler.CreateStandardReference(artifactInput,
                "FieldConsumer", [providerInput], ["ContainerKey"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), sourceFreeReference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "FieldConsumerReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        var reemittedFieldImportRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(row => reemittedTables.Imports
                .GetExpectedDefinitionForImportRow(row) >> 24 == 0x14);
        Assert.That(reemittedTables.Imports
                .GetBindingForImportRow(reemittedFieldImportRow).ExportKey,
            Is.EqualTo("Leaf.Value"));
        var expectedFieldDefinition = reemittedTables.Imports
            .GetExpectedDefinitionForImportRow(reemittedFieldImportRow);
        var expectedOwnerTypeDefRow = reemittedTables.Fields
            .GetOwnerTypeDef(checked((int)(
                expectedFieldDefinition & 0x00FF_FFFF)));
        var ownerTypeImportRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(row => reemittedTables.Imports
                .GetExpectedDefinitionForImportRow(row)
                == (0x1300_0000u
                    | checked((uint)expectedOwnerTypeDefRow)));
        Assert.That(reemittedTables.Imports
                .GetExpectedDefinitionForImportRow(ownerTypeImportRow) >> 24,
            Is.EqualTo(0x13u));
        Assert.That(reemittedTables.Imports
                .GetBindingForImportRow(ownerTypeImportRow).ExportKey,
            Is.EqualTo("LeafKey"));

        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "Container.Read", out var readDefinition), Is.True);
        var readMethodRow
            = checked((int)(readDefinition & 0x00FF_FFFF));
        var readBody = reemittedTables.Bodies.GetRegisterBody(
            reemittedTables.Methods.GetBodyRow(readMethodRow),
            reemittedTables.Contracts.Count);
        Assert.That(readBody.FieldCount, Is.EqualTo(1));
        var fieldMapToken
            = BinaryPrimitives.ReadUInt32LittleEndian(readBody.FieldMap);
        Assert.That(fieldMapToken >> 24, Is.EqualTo(0x1Cu));
        Assert.That(fieldMapToken & 0x00FF_FFFF,
            Is.EqualTo((uint)reemittedFieldImportRow));
        Assert.That(readBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] { 0xDA, 0, 1, 0, 0x17, 0 }));

        var relocatedProvider = CreateRelocatedClassProvider(
            "FieldProvider", schemaIdentity);
        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "FieldConsumerReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        relocatedProvider),
                ], ["ContainerKey"]);
        var leaf = reloadedReference.StandardClassDeclarations.Single(
            static declaration => declaration.Declaration.Name == "Leaf");
        Assert.That(leaf.Expectations.Single().FieldExpectations
            .Single().ExportKey, Is.EqualTo("Leaf.Value"));

        var incompatibleProvider = CreateClassArtifact("FieldProvider",
            "Leaf", "LeafKey", 0xF200_000A, schemaIdentity,
            layoutKind: 0, pack: 0, fieldExportKey: "Leaf.Value");
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "FieldConsumerReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProvider),
                ], ["ContainerKey"]));
    }

    [Test]
    public void ReemitsImportedStructFieldPathBodiesWithTransformedOutput()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var provider = CreateClassArtifact("StructProvider", "Leaf",
            "LeafKey", 0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0,
            fieldExportKey: "Leaf.Value");
        SetTypeDefFlags(provider, 0x06);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            provider);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "StructProvider", [], ["LeafKey"],
            ["Leaf.Value"]);
        var tree = SyntaxTree.Parse("""
            public class Container {
                public int32 Marker;
                public int32 Read(Leaf leaf) { return leaf.Value; }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var read = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Read"));
        var containerClassIndex = Enumerable.Range(0,
                program.Classes.Length)
            .Single(index =>
                program.Classes[index].Name == "Container");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "StructFieldConsumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, containerClassIndex,
                    classDeclaration.Span, "ContainerKey", [
                        new TrRmSourceClassMemberExport(containerClassIndex,
                            field.Span, "Container.Marker"),
                        new TrRmSourceClassMemberExport(containerClassIndex,
                            read.Span, "Container.Read"),
                    ]),
            ]);
        var artifactInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var artifactTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        var artifactStructRow = Enumerable.Range(1,
                artifactTables.TypeDefs.Count)
            .Single(row => artifactTables.TypeDefs.GetName(row) == "Leaf");
        var artifactTypeImportRow = Enumerable.Range(1,
                artifactTables.Imports.Count)
            .Single(row => artifactTables.Imports
                .GetExpectedDefinitionForImportRow(row)
                == (0x1300_0000u | checked((uint)artifactStructRow)));
        var artifactFieldImportRow = Enumerable.Range(1,
                artifactTables.Imports.Count)
            .Single(row => artifactTables.Imports
                .GetExpectedDefinitionForImportRow(row) >> 24 == 0x14);
        var artifactFieldDefinition = artifactTables.Imports
            .GetExpectedDefinitionForImportRow(artifactFieldImportRow);
        Assert.That(artifactTables.Fields.GetOwnerTypeDef(
                checked((int)(artifactFieldDefinition & 0x00FF_FFFF))),
            Is.EqualTo(artifactStructRow));
        Assert.That(artifactTables.Imports.GetBindingForImportRow(
                artifactFieldImportRow).ExportKey,
            Is.EqualTo("Leaf.Value"));
        Assert.That(artifactTables.Imports
                .GetBindingForImportRow(artifactTypeImportRow).ExportKey,
            Is.EqualTo("LeafKey"));
        Assert.That(artifactTables.ModuleRefs.GetProducerIdentity(
                artifactTables.Imports.GetModuleRefForImportRow(
                    artifactFieldImportRow)),
            Is.EqualTo("StructProvider"));
        Assert.That(artifactTables.ModuleRefs.GetProducerIdentity(
                artifactTables.Imports.GetModuleRefForImportRow(
                    artifactTypeImportRow)),
            Is.EqualTo("StructProvider"));

        var artifactReadDefinition = artifactTables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Container.Read").Definition;
        var artifactReadMethodRow = checked((int)(
            artifactReadDefinition & 0x00FF_FFFF));
        var artifactReadBodyRow = artifactTables.Methods.GetBodyRow(
            artifactReadMethodRow);
        var artifactReadBody = artifactTables.Bodies.GetRegisterBody(
            artifactReadBodyRow,
            artifactTables.Contracts.Count);
        Assert.That(artifactReadBody.FieldCount, Is.EqualTo(1));
        var artifactFieldMapToken
            = BinaryPrimitives.ReadUInt32LittleEndian(
                artifactReadBody.FieldMap);
        Assert.That(artifactFieldMapToken,
            Is.EqualTo(0x1C00_0000u
                | checked((uint)artifactFieldImportRow)));

        var sourceFreeReference
            = TrRmExecutableCompiler.CreateStandardReference(artifactInput,
                "StructFieldConsumer", [providerInput], ["ContainerKey"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), sourceFreeReference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "StructFieldConsumerReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        var reemittedStructRow = Enumerable.Range(1,
                reemittedTables.TypeDefs.Count)
            .Single(row => reemittedTables.TypeDefs.GetName(row) == "Leaf");
        var reemittedTypeImportRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(row => reemittedTables.Imports
                .GetExpectedDefinitionForImportRow(row)
                == (0x1300_0000u | checked((uint)reemittedStructRow)));
        var reemittedFieldImportRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(row => reemittedTables.Imports
                .GetExpectedDefinitionForImportRow(row) >> 24 == 0x14);
        Assert.That(reemittedTables.Fields.GetOwnerTypeDef(checked((int)(
                reemittedTables.Imports.GetExpectedDefinitionForImportRow(
                    reemittedFieldImportRow) & 0x00FF_FFFF))),
            Is.EqualTo(reemittedStructRow));
        Assert.That(reemittedTables.Imports.GetBindingForImportRow(
                reemittedFieldImportRow).ExportKey,
            Is.EqualTo("Leaf.Value"));
        Assert.That(reemittedTables.Imports
                .GetBindingForImportRow(reemittedTypeImportRow).ExportKey,
            Is.EqualTo("LeafKey"));
        Assert.That(reemittedTables.ModuleRefs.GetProducerIdentity(
                reemittedTables.Imports.GetModuleRefForImportRow(
                    reemittedFieldImportRow)),
            Is.EqualTo("StructProvider"));

        var reemittedReadDefinition = reemittedTables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Container.Read").Definition;
        var reemittedReadBody = reemittedTables.Bodies.GetRegisterBody(
            reemittedTables.Methods.GetBodyRow(checked((int)(
                reemittedReadDefinition & 0x00FF_FFFF))),
            reemittedTables.Contracts.Count);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedReadBody.FieldMap),
            Is.EqualTo(0x1C00_0000u
                | checked((uint)reemittedFieldImportRow)));
        Assert.That(reemittedReadBody.Code.ToArray(),
            Is.EqualTo(artifactReadBody.Code.ToArray()));

        var pathRootTypeSpecRef = Enumerable.Range(1,
                artifactTables.TypeSpecs.Count)
            .Select(static row => checked((uint)row))
            .First(typeSpecRef => {
                var type = artifactTables.TypeSpecs.GetTypeSpecRef(
                    typeSpecRef);
                return type.Form == 0x03
                    && type.Row == artifactTypeImportRow;
            });
        var fieldPathArtifact = RewriteC1M1BodyFieldMapAsFieldPath(artifact,
            artifactReadBodyRow, pathRootTypeSpecRef);
        var fieldPathTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        fieldPathArtifact));
        Assert.That(fieldPathTables.FieldPaths.Count, Is.EqualTo(2));
        var fieldPathBody = fieldPathTables.Bodies.GetRegisterBody(
            fieldPathTables.Methods.GetBodyRow(artifactReadMethodRow),
            fieldPathTables.Contracts.Count);
        var fieldPathToken
            = BinaryPrimitives.ReadUInt32LittleEndian(
                fieldPathBody.FieldMap);
        Assert.That(fieldPathToken, Is.EqualTo(0x1D00_0002u));
        var wrongOwnerFieldPathArtifact
            = RewriteC1M1BodyFieldMapAsFieldPath(artifact,
                artifactReadBodyRow, pathRootTypeSpecRef, 0x1400_0001u);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    wrongOwnerFieldPathArtifact),
                "StructFieldConsumer", [providerInput], ["ContainerKey"]));

        var fieldPathReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    fieldPathArtifact),
                "StructFieldConsumer", [providerInput], ["ContainerKey"]);
        var fieldPathTree = SyntaxTree.Parse("""
            public class ExtraContainer {
                public int32 Read(Leaf leaf) { return leaf.Value; }
            }
            """);
        var fieldPathCompilation = Compilation.CreateLibrary(
            fieldPathTree, fieldPathReference);
        Assert.That(fieldPathCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                fieldPathCompilation.Diagnostics));
        var fieldPathProgram = (BoundProgram)fieldPathCompilation.Program!;
        var extraContainerDeclaration = fieldPathTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var extraReadDeclaration = extraContainerDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FunctionDeclaration);
        var extraContainerClassIndex = Enumerable.Range(0,
                fieldPathProgram.Classes.Length)
            .Single(index =>
                fieldPathProgram.Classes[index].Name == "ExtraContainer");
        var fieldPathReemitted = TrRmExecutableCompiler.EmitStandard(
            fieldPathProgram,
            "StructFieldPathConsumerReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(fieldPathProgram,
                    extraContainerClassIndex,
                    extraContainerDeclaration.Span,
                    "ExtraContainerKey", [
                        new TrRmSourceClassMemberExport(
                            extraContainerClassIndex,
                            extraReadDeclaration.Span,
                            "ExtraContainer.Read"),
                    ]),
            ]);
        var fieldPathReemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        fieldPathReemitted));
        Assert.That(fieldPathReemittedTables.FieldPaths.Count,
            Is.EqualTo(1));
        var fieldPathReemittedStructRow = Enumerable.Range(1,
                fieldPathReemittedTables.TypeDefs.Count)
            .Single(row =>
                fieldPathReemittedTables.TypeDefs.GetName(row) == "Leaf");
        var fieldPathReemittedTypeImportRow = Enumerable.Range(1,
                fieldPathReemittedTables.Imports.Count)
            .Single(row => fieldPathReemittedTables.Imports
                .GetExpectedDefinitionForImportRow(row)
                == (0x1300_0000u
                    | checked((uint)fieldPathReemittedStructRow)));
        var fieldPathReemittedFieldImportRow = Enumerable.Range(1,
                fieldPathReemittedTables.Imports.Count)
            .Single(row => fieldPathReemittedTables.Imports
                .GetExpectedDefinitionForImportRow(row) >> 24 == 0x14);
        var remappedFieldPathRoot
            = fieldPathReemittedTables.FieldPaths
                .GetRootTypeSpecRef(1);
        var remappedFieldPathRootType
            = fieldPathReemittedTables.TypeSpecs.GetTypeSpecRef(
                remappedFieldPathRoot);
        Assert.That(remappedFieldPathRootType.Form, Is.EqualTo((byte)0x03));
        Assert.That(checked((int)remappedFieldPathRootType.Row),
            Is.EqualTo(fieldPathReemittedTypeImportRow));
        Assert.That(fieldPathReemittedTables.FieldPaths
                .GetFieldReference(1, 0),
            Is.EqualTo(0x1C00_0000u
                | checked((uint)fieldPathReemittedFieldImportRow)));
        Assert.That(fieldPathReemittedTables.Imports
                .GetBindingForImportRow(fieldPathReemittedFieldImportRow)
                .ExportKey,
            Is.EqualTo("Leaf.Value"));
        var fieldPathReemittedReadDefinition
            = fieldPathReemittedTables.Exports.GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "Container.Read").Definition;
        var fieldPathReemittedBody
            = fieldPathReemittedTables.Bodies.GetRegisterBody(
                fieldPathReemittedTables.Methods.GetBodyRow(checked((int)(
                    fieldPathReemittedReadDefinition & 0x00FF_FFFF))),
                fieldPathReemittedTables.Contracts.Count);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                fieldPathReemittedBody.FieldMap),
            Is.EqualTo(0x1D00_0001u));
        var extraReadDefinition = fieldPathReemittedTables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "ExtraContainer.Read").Definition;
        var extraReadBody = fieldPathReemittedTables.Bodies
            .GetRegisterBody(
                fieldPathReemittedTables.Methods.GetBodyRow(checked((int)(
                    extraReadDefinition & 0x00FF_FFFF))),
                fieldPathReemittedTables.Contracts.Count);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                extraReadBody.FieldMap),
            Is.EqualTo(0x1C00_0000u
                | checked((uint)fieldPathReemittedFieldImportRow)));

        var relocatedProvider = CreateRelocatedClassProvider(
            "StructProvider", schemaIdentity, includePrefixField: true);
        SetTypeDefFlags(relocatedProvider, 0x06, typeDefRow: 2);
        var relocatedProviderTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        relocatedProvider));
        var relocatedLeafRow = Enumerable.Range(1,
                relocatedProviderTables.TypeDefs.Count)
            .Single(row =>
                relocatedProviderTables.TypeDefs.GetName(row) == "Leaf");
        Assert.That(relocatedLeafRow, Is.Not.EqualTo(1));
        var relocatedLeafFieldRow = Enumerable.Range(1,
                relocatedProviderTables.Fields.Count)
            .Single(row => relocatedProviderTables.Fields
                .GetOwnerTypeDef(row) == relocatedLeafRow);
        Assert.That(relocatedLeafFieldRow, Is.EqualTo(2));
        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    fieldPathReemitted),
                "StructFieldPathConsumerReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        relocatedProvider),
                ], ["ContainerKey"]);
        Assert.That(reloadedReference.GenericNominalTypeDefinitions
                .Single(static definition => definition.Name == "Leaf")
                .ProviderExpectations,
            Is.Not.Empty);

        var missingFieldProvider = CreateClassArtifact("StructProvider",
            "Leaf", "LeafKey", 0xF200_0008, schemaIdentity,
            layoutKind: 0, pack: 0);
        SetTypeDefFlags(missingFieldProvider, 0x06);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    fieldPathReemitted),
                "StructFieldPathConsumerReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        missingFieldProvider),
                ], ["ContainerKey"]));

        var incompatibleProvider = CreateClassArtifact("StructProvider",
            "Leaf", "LeafKey", 0xF200_000A, schemaIdentity,
            layoutKind: 0, pack: 0, fieldExportKey: "Leaf.Value");
        SetTypeDefFlags(incompatibleProvider, 0x06);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    fieldPathReemitted),
                "StructFieldPathConsumerReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProvider),
                ], ["ContainerKey"]));
    }

    [Test]
    public void ReemitsCopyStructParametersInNoOpClassBodies()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var provider = CreateClassArtifact("StructProvider", "Wrapper",
            "WrapperKey", 0xF200_0008, schemaIdentity, layoutKind: 0,
            pack: 0, fieldExportKey: "Wrapper.Value");
        SetTypeDefFlags(provider, 0x06);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            provider);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "StructProvider", [], ["WrapperKey"],
            ["Wrapper.Value"]);
        var tree = SyntaxTree.Parse("""
            public struct LocalWrapper { public int32 Value; }
            public class Container {
                public int32 Marker;
                public void Accept(Wrapper value) {}
                public void AcceptLocal(LocalWrapper value) {}
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var accept = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Accept"));
        var acceptLocal = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "AcceptLocal"));
        var containerClassIndex = Enumerable.Range(0,
                program.Classes.Length)
            .Single(index =>
                program.Classes[index].Name == "Container");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "StructMethodConsumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, containerClassIndex,
                    classDeclaration.Span, "ContainerKey", [
                        new TrRmSourceClassMemberExport(containerClassIndex,
                            field.Span, "Container.Marker"),
                        new TrRmSourceClassMemberExport(containerClassIndex,
                            accept.Span, "Container.Accept"),
                        new TrRmSourceClassMemberExport(containerClassIndex,
                            acceptLocal.Span, "Container.AcceptLocal"),
                    ]),
            ]);

        static void AssertStructParameterAndBody(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            string methodExportKey, string structName, bool isImported)
        {
            var structTypeDefRow = Enumerable.Range(1,
                    tables.TypeDefs.Count)
                .Single(row => tables.TypeDefs.GetName(row) == structName);
            var expectedTypeSpecForm = isImported ? (byte)0x03 : (byte)0x02;
            var expectedTypeSpecRow = isImported
                ? Enumerable.Range(1, tables.Imports.Count)
                    .Single(row => tables.Imports
                        .GetExpectedDefinitionForImportRow(row)
                        == (0x1300_0000u
                            | checked((uint)structTypeDefRow)))
                : structTypeDefRow;
            var methodDefinition = tables.Exports
                .GetExportsForModule(1)
                .Single(export =>
                    export.ExportKey == methodExportKey).Definition;
            var methodRow = checked((int)(
                methodDefinition & 0x00FF_FFFF));
            var signature = tables.Methods.GetSignature(methodRow);
            var (parameterFirst, parameterCount)
                = tables.Signatures.GetParamRange(signature);
            Assert.That(parameterCount, Is.EqualTo(1));
            Assert.That(tables.Parameters.GetMode(parameterFirst), Is.Zero);
            Assert.That(tables.Parameters.GetFlags(parameterFirst), Is.Zero);
            var parameterTypeSpec = tables.TypeSpecs.GetTypeSpecRef(
                tables.Parameters.GetTypeSpec(parameterFirst));
            Assert.That(parameterTypeSpec.Form,
                Is.EqualTo(expectedTypeSpecForm));
            Assert.That(checked((int)parameterTypeSpec.Row),
                Is.EqualTo(expectedTypeSpecRow));

            var body = tables.Bodies.GetRegisterBody(
                tables.Methods.GetBodyRow(methodRow),
                tables.Contracts.Count);
            Assert.That(body.FieldCount, Is.Zero);
            Assert.That(Enumerable.Range(0, body.TypeCount).Any(index => {
                var token = BinaryPrimitives.ReadUInt32LittleEndian(
                    body.TypeMap.Slice(index * sizeof(uint)));
                return (byte)(token >> 24) == 0x11
                    && tables.TypeSpecs.GetNominalTypeDefRow(
                        token & 0x00FF_FFFF, tables.TypeDefs,
                        tables.Imports) == structTypeDefRow;
            }), Is.True);
        }

        var artifactTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        AssertStructParameterAndBody(artifactTables, "Container.Accept",
            "Wrapper", isImported: true);
        AssertStructParameterAndBody(artifactTables,
            "Container.AcceptLocal", "LocalWrapper", isImported: false);

        var artifactInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var sourceFreeReference
            = TrRmExecutableCompiler.CreateStandardReference(artifactInput,
                "StructMethodConsumer", [providerInput], ["ContainerKey"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), sourceFreeReference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "StructMethodConsumerReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        AssertStructParameterAndBody(reemittedTables,
            "Container.Accept", "Wrapper", isImported: true);
        AssertStructParameterAndBody(reemittedTables,
            "Container.AcceptLocal", "LocalWrapper", isImported: false);

        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "StructMethodConsumerReemitted", [providerInput],
                ["ContainerKey"]);
        Assert.That(reloadedReference.GenericNominalTypeDefinitions
                .Single(static definition => definition.Name == "Wrapper")
                .ProviderExpectations,
            Is.Not.Empty);

        var incompatibleProvider = CreateClassArtifact("StructProvider",
            "Wrapper", "WrapperKey", 0xF200_000A, schemaIdentity,
            layoutKind: 0, pack: 0, fieldExportKey: "Wrapper.Value");
        SetTypeDefFlags(incompatibleProvider, 0x06);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "StructMethodConsumerReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProvider),
                ], ["ContainerKey"]));
    }

    [Test]
    public void ReemitsReadonlyReferenceParametersInNoOpClassBodies()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var tree = SyntaxTree.Parse("""
            public class Container {
                public int32 Marker;
                public void Observe(ref<int32> value) {}
                public void ObserveImplicit(implicit ref<int32> value) {}
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var observe = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Observe"));
        var observeImplicit = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "ObserveImplicit"));
        var containerClassIndex = Enumerable.Range(0,
                program.Classes.Length)
            .Single(index =>
                program.Classes[index].Name == "Container");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ReferenceMethodConsumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, containerClassIndex,
                    classDeclaration.Span, "ContainerKey", [
                        new TrRmSourceClassMemberExport(containerClassIndex,
                            field.Span, "Container.Marker"),
                        new TrRmSourceClassMemberExport(containerClassIndex,
                            observe.Span, "Container.Observe"),
                        new TrRmSourceClassMemberExport(containerClassIndex,
                            observeImplicit.Span,
                            "Container.ObserveImplicit"),
                    ]),
            ]);

        static void AssertReferenceParameter(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            string exportKey, byte expectedFlags)
        {
            var methodDefinition = tables.Exports
                .GetExportsForModule(1)
                .Single(export => export.ExportKey == exportKey).Definition;
            var methodRow = checked((int)(
                methodDefinition & 0x00FF_FFFF));
            var signature = tables.Methods.GetSignature(methodRow);
            var (parameterFirst, parameterCount)
                = tables.Signatures.GetParamRange(signature);
            Assert.That(parameterCount, Is.EqualTo(1));
            Assert.That(tables.Parameters.GetMode(parameterFirst), Is.Zero);
            Assert.That(tables.Parameters.GetFlags(parameterFirst),
                Is.EqualTo(expectedFlags));
            var parameterType = tables.TypeSpecs.GetTypeSpecRef(
                tables.Parameters.GetTypeSpec(parameterFirst));
            Assert.That(parameterType.Form, Is.EqualTo((byte)0x07));
            Assert.That(parameterType.Qualifiers, Is.Zero);

            var body = tables.Bodies.GetRegisterBody(
                tables.Methods.GetBodyRow(methodRow),
                tables.Contracts.Count);
            Assert.That(Enumerable.Range(0, body.TypeCount).Any(index => {
                var token = BinaryPrimitives.ReadUInt32LittleEndian(
                    body.TypeMap.Slice(index * sizeof(uint)));
                return (byte)(token >> 24) == 0x11
                    && tables.TypeSpecs.GetTypeSpecRef(
                        token & 0x00FF_FFFF).Form == 0x07;
            }), Is.True);
        }

        var artifactTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        AssertReferenceParameter(artifactTables, "Container.Observe", 0);
        AssertReferenceParameter(artifactTables,
            "Container.ObserveImplicit", 1);

        var artifactInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            artifactInput, "ReferenceMethodConsumer", [], ["ContainerKey"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "ReferenceMethodConsumerReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        AssertReferenceParameter(reemittedTables, "Container.Observe", 0);
        AssertReferenceParameter(reemittedTables,
            "Container.ObserveImplicit", 1);

        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "ReferenceMethodConsumerReemitted", [], ["ContainerKey"]);
        var reloadedMethod = reloadedReference.StandardClassDeclarations
            .Single(static declaration =>
                declaration.Declaration.Name == "Container")
            .Declaration.Methods.Single(static method =>
                method.Name == "Observe");
        var reloadedImplicitMethod
            = reloadedReference.StandardClassDeclarations
                .Single(static declaration =>
                    declaration.Declaration.Name == "Container")
                .Declaration.Methods.Single(static method =>
                    method.Name == "ObserveImplicit");
        Assert.That(reloadedReference.StandardClassDeclarations.Length,
            Is.EqualTo(1));
        Assert.That(reloadedMethod.Parameters, Has.Length.EqualTo(1));
        Assert.That(reloadedMethod.Parameters[0].Mode, Is.Zero);
        Assert.That(reloadedMethod.Parameters[0].Flags, Is.Zero);
        Assert.That(reloadedImplicitMethod.Parameters,
            Has.Length.EqualTo(1));
        Assert.That(reloadedImplicitMethod.Parameters[0].Mode, Is.Zero);
        Assert.That(reloadedImplicitMethod.Parameters[0].Flags, Is.EqualTo(1));
        var callerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public void Forward(Container target, int32 value) {
                    target.ObserveImplicit(value);
                }
                """), reloadedReference);
        Assert.That(callerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                callerCompilation.Diagnostics));
    }

    [Test]
    public void RejectsWritableReferenceParametersInNoOpClassBodies()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var tree = SyntaxTree.Parse("""
            public class Container {
                public int32 Marker;
                public void Observe(write ref<int32> value) {}
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var observe = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Observe"));
        var containerClassIndex = Enumerable.Range(0,
                program.Classes.Length)
            .Single(index =>
                program.Classes[index].Name == "Container");

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                "WritableReferenceMethodConsumer", schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                [], [], [
                    new TrRmSourceClassExport(program,
                        containerClassIndex, classDeclaration.Span,
                        "ContainerKey", [
                            new TrRmSourceClassMemberExport(
                                containerClassIndex, field.Span,
                                "Container.Marker"),
                            new TrRmSourceClassMemberExport(
                                containerClassIndex, observe.Span,
                                "Container.Observe"),
                        ]),
                ]));
    }

    [Test]
    public void EmitsReadonlyBodiesThroughReadonlyAndWritableClassHandles()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var provider = CreateClassArtifact("ClassProvider", "Leaf",
            "LeafKey", 0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0,
            fieldExportKey: "Leaf.Value",
            additionalFieldExportKey: "Leaf.ValueAlias");
        SetTypeDefFlags(provider, 0x8A);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, provider),
            "ClassProvider", [], ["LeafKey"],
            ["Leaf.Value", "Leaf.ValueAlias"]);

        var tree = SyntaxTree.Parse("""
            public class Container {
                private int32 _seed;
                public Container(int32 seed) { _seed = seed; }
                public int32 Read(Leaf leaf) { return leaf.Value; }
                public int32 ReadWritable(write Leaf leaf) {
                    return leaf.Value;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var read = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Read"));
        var readWritable = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "ReadWritable"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ImportedClassFieldRead", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "ContainerKey", [
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Container.Initialize") {
                            ConstructionProjectionExportKey
                                = "Container.Create",
                        },
                        new TrRmSourceClassMemberExport(0, read.Span,
                            "Container.Read"),
                        new TrRmSourceClassMemberExport(0,
                            readWritable.Span, "Container.ReadWritable"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Container.Read", out var readDefinition), Is.True);
        var readMethodRow
            = checked((int)(readDefinition & 0x00FF_FFFF));
        var fieldImportRows = Enumerable.Range(1, tables.Imports.Count)
            .Where(row => tables.Imports
                .GetExpectedDefinitionForImportRow(row) >> 24 == 0x14)
            .ToArray();
        Assert.That(fieldImportRows, Has.Length.EqualTo(2));
        Assert.That(fieldImportRows.Select(row => tables.Imports
                .GetBindingForImportRow(row).ExportKey),
            Is.EquivalentTo(["Leaf.Value", "Leaf.ValueAlias"]));
        Assert.That(fieldImportRows.Select(tables.Imports
                .GetExpectedDefinitionForImportRow).Distinct().Count(),
            Is.EqualTo(1));
        var readBodyRow = tables.Methods.GetBodyRow(readMethodRow);
        Assert.That(readBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(readBodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(readMethodRow);
        var readBody = tables.Bodies.GetRegisterBody(readBodyRow,
            tables.Contracts.Count);
        Assert.That(readBody.HomeCount, Is.EqualTo(2));
        Assert.That(readBody.GetRecord(1)[0], Is.EqualTo(0x04));
        Assert.That(readBody.GetRecord(1)[7], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            readBody.GetRecord(1).Slice(8, 2)), Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            readBody.GetRecord(1).Slice(10, 2)), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            readBody.GetRecord(1).Slice(16, 4)), Is.EqualTo(2u));
        Assert.That(readBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xDA, 0, 1, 0,
                0x17, 0,
            }));

        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Container.ReadWritable", out var writableReadDefinition), Is.True);
        var writableReadMethodRow = checked((int)(
            writableReadDefinition & 0x00FF_FFFF));
        var writableReadBodyRow
            = tables.Methods.GetBodyRow(writableReadMethodRow);
        Assert.That(writableReadBodyRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(writableReadMethodRow);
        var writableReadBody = tables.Bodies.GetRegisterBody(
            writableReadBodyRow, tables.Contracts.Count);
        Assert.That(writableReadBody.HomeCount, Is.EqualTo(2));
        Assert.That(writableReadBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xDA, 0, 1, 0,
                0x17, 0,
            }));
        var writableReadSignature = tables.Methods.GetSignature(
            writableReadMethodRow);
        var (writableParamFirst, writableParamCount)
            = tables.Signatures.GetParamRange(writableReadSignature);
        Assert.That(writableParamCount, Is.EqualTo(1));
        var writableParameterType = tables.TypeSpecs.GetTypeSpecRef(
            tables.Parameters.GetTypeSpec(writableParamFirst));
        Assert.That(writableParameterType.Form, Is.EqualTo((byte)0x03));
        Assert.That(writableParameterType.Qualifiers,
            Is.EqualTo((byte)0x01));

        var outputReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ImportedClassFieldRead", [
                    new TrRmStandardArtifactInput(schemaIdentity, provider),
                ], ["ContainerKey"], ["Leaf.Value", "Leaf.ValueAlias"]);
        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Container value) {}"),
            outputReference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), outputReference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "ImportedClassFieldWriteReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "Container.Write", out var reemittedWriteDefinition), Is.True);
        var reemittedWriteMethodRow = checked((int)(
            reemittedWriteDefinition & 0x00FF_FFFF));
        var reemittedWriteBodyRow
            = reemittedTables.Methods.GetBodyRow(
                reemittedWriteMethodRow);
        Assert.That(reemittedWriteBodyRow, Is.GreaterThan(0));
        reemittedTables.Bodies.ValidateSelectedImplementation(
            reemittedWriteBodyRow, reemittedTables.Contracts.Count);
        var reemittedWriteBody = reemittedTables.Bodies.GetRegisterBody(
            reemittedWriteBodyRow, reemittedTables.Contracts.Count);
        Assert.That(reemittedWriteBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(writeBody.GetBlockCode(0).ToArray()));
        var reemittedPlaceSelection = reemittedWriteBody.GetRecord(1);
        var reemittedFieldRow = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedPlaceSelection.Slice(16, sizeof(uint))));
        Assert.That(reemittedTables.Fields.GetName(reemittedFieldRow),
            Is.EqualTo("Value"));
        Assert.That(reemittedTables.TypeDefs.GetName(
            reemittedTables.Fields.GetOwnerTypeDef(reemittedFieldRow)),
            Is.EqualTo("Leaf"));
        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "Container.WriteConstant",
            out var reemittedWriteConstantDefinition), Is.True);
        var reemittedWriteConstantMethodRow = checked((int)(
            reemittedWriteConstantDefinition & 0x00FF_FFFF));
        var reemittedWriteConstantBodyRow
            = reemittedTables.Methods.GetBodyRow(
                reemittedWriteConstantMethodRow);
        Assert.That(reemittedWriteConstantBodyRow, Is.GreaterThan(0));
        var reemittedWriteConstantBody
            = reemittedTables.Bodies.GetRegisterBody(
                reemittedWriteConstantBodyRow,
                reemittedTables.Contracts.Count);
        Assert.That(reemittedWriteConstantBody.ConstantCount,
            Is.EqualTo(1));
        Assert.That(reemittedWriteConstantBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(writeConstantBody.GetBlockCode(0).ToArray()));
        Assert.That(reemittedTables.Features.TryGetFeature(11, out _),
            Is.True);
        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "ImportedClassFieldWriteReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity, provider),
                ], ["ContainerKey"], ["Leaf.Value"]);
        var reemittedConsumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public void Use(write Leaf leaf, int32 value) {
                    Container.Write(leaf, value);
                    Container.WriteConstant(leaf);
                    return;
                }
                """), reemittedReference);
        Assert.That(reemittedConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reemittedConsumer.Diagnostics));

        var incompatibleProvider = CreateClassArtifact("ClassProvider",
            "Leaf", "LeafKey", 0xF200_000A, schemaIdentity,
            layoutKind: 0, pack: 0, fieldExportKey: "Leaf.Value",
            additionalFieldExportKey: "Leaf.ValueAlias");
        SetTypeDefFlags(incompatibleProvider, 0x8A);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ImportedClassFieldRead", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProvider),
                ], ["ContainerKey"]));
    }

    [Test]
    public void EmitsMutableStoresThroughWritableImportedClassHandles()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var provider = CreateClassArtifact("ClassProvider", "Leaf",
            "LeafKey", 0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0,
            fieldExportKey: "Leaf.Value", fieldFlags: 0x06);
        SetTypeDefFlags(provider, 0x8A);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, provider),
            "ClassProvider", [], ["LeafKey"], ["Leaf.Value"]);

        var tree = SyntaxTree.Parse("""
            public class Container {
                private int32 _seed;
                public Container(int32 seed) { _seed = seed; }
                public static void Write(write Leaf leaf, int32 value) {
                    leaf.Value = value;
                    return;
                }
                public static void WriteConstant(write Leaf leaf) {
                    leaf.Value = 42;
                    return;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var write = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Write"));
        var writeConstant = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "WriteConstant"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ImportedClassFieldWrite", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "ContainerKey", [
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Container.Initialize") {
                            ConstructionProjectionExportKey
                                = "Container.Create",
                        },
                        new TrRmSourceClassMemberExport(0, write.Span,
                            "Container.Write"),
                        new TrRmSourceClassMemberExport(0,
                            writeConstant.Span, "Container.WriteConstant"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Container.Write", out var writeDefinition), Is.True);
        var writeMethodRow
            = checked((int)(writeDefinition & 0x00FF_FFFF));
        var writeBodyRow = tables.Methods.GetBodyRow(writeMethodRow);
        Assert.That(writeBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(writeBodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(writeMethodRow);
        var writeBody = tables.Bodies.GetRegisterBody(writeBodyRow,
            tables.Contracts.Count);
        Assert.That(writeBody.HomeCount, Is.EqualTo(2));
        Assert.That(writeBody.GetRecord(1)[5], Is.EqualTo(2));
        Assert.That(writeBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD8, 1, 1, 0,
                0xD9, 0, 2, 0,
                0x18, 0,
            }));
        var writeSignature = tables.Methods.GetSignature(writeMethodRow);
        var (parameterStart, parameterCount)
            = tables.Signatures.GetParamRange(writeSignature);
        Assert.That(parameterCount, Is.EqualTo(2));
        var receiverTypeSpec = tables.TypeSpecs.GetTypeSpecRef(
            tables.Parameters.GetTypeSpec(parameterStart));
        Assert.That(receiverTypeSpec.Form, Is.EqualTo((byte)0x03));
        Assert.That(receiverTypeSpec.Qualifiers, Is.EqualTo((byte)0x01));

        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Container.WriteConstant", out var writeConstantDefinition),
            Is.True);
        var writeConstantMethodRow = checked((int)(
            writeConstantDefinition & 0x00FF_FFFF));
        var writeConstantBodyRow
            = tables.Methods.GetBodyRow(writeConstantMethodRow);
        Assert.That(writeConstantBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            writeConstantBodyRow, tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(writeConstantMethodRow);
        var writeConstantBody = tables.Bodies.GetRegisterBody(
            writeConstantBodyRow, tables.Contracts.Count);
        Assert.That(writeConstantBody.HomeCount, Is.EqualTo(1));
        Assert.That(writeConstantBody.ConstantCount, Is.EqualTo(1));
        Assert.That(tables.Features.TryGetFeature(11, out _), Is.True);
        Assert.That(writeConstantBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD8, 1, 1, 0,
                0xD4, 0, 0, 0,
                0xD9, 0, 2, 0,
                0x18, 0,
            }));

        var outputReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ImportedClassFieldWrite", [
                    new TrRmStandardArtifactInput(schemaIdentity, provider),
                ], ["ContainerKey"], ["Leaf.Value"]);
        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Container value) {}"),
            outputReference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var incompatibleProvider = CreateClassArtifact("ClassProvider",
            "Leaf", "LeafKey", 0xF200_000A, schemaIdentity,
            layoutKind: 0, pack: 0, fieldExportKey: "Leaf.Value",
            fieldFlags: 0x06);
        SetTypeDefFlags(incompatibleProvider, 0x8A);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ImportedClassFieldWrite", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProvider),
                ], ["ContainerKey"], ["Leaf.Value"]));
    }

    [Test]
    public void RejectsImportedClassFieldStoresWithoutWriteAuthority()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var mutableProvider = CreateClassArtifact("ClassProvider", "Leaf",
            "LeafKey", 0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0,
            fieldExportKey: "Leaf.Value", fieldFlags: 0x06);
        SetTypeDefFlags(mutableProvider, 0x8A);
        var mutableReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    mutableProvider),
                "ClassProvider", [], ["LeafKey"], ["Leaf.Value"]);
        var readonlyHandle = Compilation.CreateLibrary(
            SyntaxTree.Parse(
                "public void Write(Leaf leaf, int32 value) { leaf.Value = value; }"),
            mutableReference);
        Assert.That(readonlyHandle.HasErrors, Is.True,
            string.Join(Environment.NewLine, readonlyHandle.Diagnostics));
        Assert.That(readonlyHandle.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3008"), Is.True,
            string.Join(Environment.NewLine, readonlyHandle.Diagnostics));

        var readonlyFieldProvider = CreateClassArtifact("ClassProvider",
            "Leaf", "LeafKey", 0xF200_0008, schemaIdentity,
            layoutKind: 0, pack: 0, fieldExportKey: "Leaf.Value");
        SetTypeDefFlags(readonlyFieldProvider, 0x8A);
        var readonlyFieldReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    readonlyFieldProvider),
                "ClassProvider", [], ["LeafKey"], ["Leaf.Value"]);
        var readonlyField = Compilation.CreateLibrary(
            SyntaxTree.Parse(
                "public void Write(write Leaf leaf, int32 value) { leaf.Value = value; }"),
            readonlyFieldReference);
        Assert.That(readonlyField.HasErrors, Is.True,
            string.Join(Environment.NewLine, readonlyField.Diagnostics));
        Assert.That(readonlyField.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3008"), Is.True,
            string.Join(Environment.NewLine, readonlyField.Diagnostics));
    }

    [Test]
    public void ReemitsTransitiveImportedClassTypeProviders()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var leafProvider = CreateClassArtifact("LeafProvider", "Leaf",
            "LeafKey", 0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0,
            fieldExportKey: "Leaf.Value");
        var wrapperProvider = CreateArtifact("WrapperProvider", "Wrapper",
            "WrapperKey", 0xF200_0008, schemaIdentity, "LeafProvider",
            "LeafKey", TrRmTypeSpecTableWriter.Reference(0x03, 1),
            layoutKind: 0, pack: 0, useRequestedLayoutForImports: true,
            fieldExportKey: "Wrapper.Child");
        var wrapperReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    wrapperProvider),
                "WrapperProvider", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafProvider),
                ], ["WrapperKey"], ["Wrapper.Child"]);

        var tree = SyntaxTree.Parse("""
            public class Container {
                private int32 _seed;
                public Container(int32 seed) { _seed = seed; }
                public static void Accept(Wrapper value) {}
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, wrapperReference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var accept = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Accept"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "TransitiveClassConsumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "ContainerKey", [
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Container.Initialize") {
                            ConstructionProjectionExportKey
                                = "Container.Create",
                        },
                        new TrRmSourceClassMemberExport(0, accept.Span,
                            "Container.Accept"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        var wrapperTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Wrapper");
        var leafTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Leaf");
        Assert.That(Enumerable.Range(1, tables.Imports.Count)
                .Select(tables.Imports.GetExpectedDefinitionForImportRow)
                .Where(static definition => definition >> 24 == 0x13)
                .ToArray(),
            Is.EquivalentTo([
                0x1300_0000u | checked((uint)wrapperTypeDefRow),
                0x1300_0000u | checked((uint)leafTypeDefRow),
            ]));

        var (wrapperFieldStart, wrapperFieldCount)
            = tables.TypeDefs.GetFieldRange(wrapperTypeDefRow);
        var wrapperFieldRow = Enumerable.Range(wrapperFieldStart,
                wrapperFieldCount)
            .Single(row => tables.Fields.GetName(row) == "Value");
        var wrapperFieldType = tables.TypeSpecs.GetTypeSpecRef(
            tables.Fields.GetTypeSpecRef(wrapperFieldRow));
        Assert.That(wrapperFieldType.Form, Is.EqualTo((byte)0x03));
        var leafTypeImportRow = Enumerable.Range(1, tables.Imports.Count)
            .Single(row => tables.Imports
                .GetExpectedDefinitionForImportRow(row)
                == (0x1300_0000u
                    | checked((uint)leafTypeDefRow)));
        Assert.That(wrapperFieldType.Row, Is.EqualTo(leafTypeImportRow));
        Assert.That(Enumerable.Range(1, tables.ModuleRefs.Count)
                .Select(tables.ModuleRefs.GetProducerIdentity),
            Does.Contain("WrapperProvider").And.Contain("LeafProvider"));

        var relocatedLeafProvider = CreateRelocatedClassProvider(
            "LeafProvider", schemaIdentity);
        var originalLeafTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        leafProvider));
        var relocatedLeafTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        relocatedLeafProvider));
        var originalLeafRow
            = Enumerable.Range(1, originalLeafTables.TypeDefs.Count)
                .Single(row =>
                    originalLeafTables.TypeDefs.GetName(row) == "Leaf");
        var relocatedLeafRow
            = Enumerable.Range(1, relocatedLeafTables.TypeDefs.Count)
                .Single(row =>
                    relocatedLeafTables.TypeDefs.GetName(row) == "Leaf");
        Assert.That(relocatedLeafRow, Is.Not.EqualTo(originalLeafRow));

        var outputReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "TransitiveClassConsumer", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        wrapperProvider),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        relocatedLeafProvider),
                ], ["ContainerKey"], ["Wrapper.Child"]);
        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Container value) {}"),
            outputReference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var incompatibleLeaf = CreateClassArtifact("LeafProvider", "Leaf",
            "LeafKey", 0xF200_000A, schemaIdentity, layoutKind: 0, pack: 0,
            fieldExportKey: "Leaf.Value");
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "TransitiveClassConsumer", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        wrapperProvider),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleLeaf),
                ], ["ContainerKey"], ["Wrapper.Child"]));
    }

    [Test]
    public void RejectsReadsThroughUnselectedImportedClassFields()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var provider = CreateClassArtifact("ClassProvider", "Leaf",
            "LeafKey", 0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, provider),
            "ClassFieldConsumer", [], ["LeafKey"]);

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(
                "public int32 Read(Leaf leaf) { return leaf.Value; }"),
            reference);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3006"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void ReemitsImportedClassTypeSpecProviderExpectations()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var provider = CreateClassArtifact("Provider", "Leaf", "LeafKey",
            0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0);
        var root = CreateRootArtifact("Root", "Box", "BoxKey",
            0xF200_0008, schemaIdentity, layoutKind: 0, pack: 0,
            useRequestedLayoutForImports: true);
        var reference = CreateReference(root, provider, schemaIdentity);
        Assert.That(reference.StandardClassDeclarations.Single(
                static declaration =>
                    declaration.Declaration.Name == "Leaf").Expectations,
            Is.Not.Empty);

        var tree = SyntaxTree.Parse("""
            public class Container {
                private int32 _value;
                public Container(int32 value) { _value = value; }
                public static void Accept(Box value) {}
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var accept = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Accept"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ExpectedClassProvider", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "ContainerKey", [
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Container.Initialize") {
                            ConstructionProjectionExportKey
                                = "Container.Create",
                        },
                        new TrRmSourceClassMemberExport(0, accept.Span,
                            "Container.Accept"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        var boxTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Box");
        var leafTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Leaf");
        var boxImportRow = Enumerable.Range(1, tables.Imports.Count)
            .Single(row => tables.Imports
                .GetExpectedDefinitionForImportRow(row)
                == (0x1300_0000u | checked((uint)boxTypeDefRow)));
        var leafImportRow = Enumerable.Range(1, tables.Imports.Count)
            .Single(row => tables.Imports
                .GetExpectedDefinitionForImportRow(row)
                == (0x1300_0000u | checked((uint)leafTypeDefRow)));
        Assert.That(tables.ModuleRefs.GetProducerIdentity(
            tables.Imports.GetModuleRefForImportRow(boxImportRow)),
            Is.EqualTo("Root"));
        Assert.That(tables.ModuleRefs.GetProducerIdentity(
            tables.Imports.GetModuleRefForImportRow(leafImportRow)),
            Is.EqualTo("Provider"));

        var consumerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ExpectedClassProvider", [
                    new TrRmStandardArtifactInput(schemaIdentity, root),
                    new TrRmStandardArtifactInput(schemaIdentity, provider),
                ], ["ContainerKey"]);
        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Container value) {}"),
            consumerReference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var incompatibleProvider = CreateClassArtifact("Provider", "Leaf",
            "LeafKey", 0xF200_000A, schemaIdentity, layoutKind: 0, pack: 0);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ExpectedClassProvider", [
                    new TrRmStandardArtifactInput(schemaIdentity, root),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProvider),
                ], ["ContainerKey"]));
    }

    [Test]
    public void BindsMutableC1M1ClassDeclarations()
    {
        var provider = CreateClassArtifact("Provider", "Leaf", "LeafKey",
            0xF200_0008);
        SetTypeDefFlags(provider, 0x8A);
        var consumer = CreateRootArtifact("Root", "Box", "BoxKey",
            0xF200_0008);
        SetTypeDefFlags(consumer, 0x8A, typeDefRow: 2);

        var reference = CreateReference(consumer, provider,
            C1M1SchemaIdentity);
        var importedClass = reference.StandardClassDeclarations.Single(
            static declaration => declaration.Declaration.Name == "Leaf");
        Assert.That(importedClass.Declaration.TryGetDeclaredMutability(
            out var actualMutability), Is.True);
        Assert.That(actualMutability, Is.True);
        Assert.That(importedClass.Expectations[0].ExpectedDeclaration
            .TryGetDeclaredMutability(out var expectedMutability), Is.True);
        Assert.That(expectedMutability, Is.True);
    }

    [Test]
    public void CapturesPI1PropertyBackingAndAccessorIdentitiesInClassViews()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value { get; init; }
                public Box(int32 value) { Value = value; }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var propertyDeclaration = members.Single(static node =>
            node.Kind == SyntaxKind.PropertyDeclaration);
        var constructorDeclaration = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "PI1ClassProvider",
            C1M1Construction2PropertyInitializedStateSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0,
                            propertyDeclaration.Span, "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            constructorDeclaration.Span,
                            "Box.Initialize"),
                    ]),
            ]);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                C1M1Construction2PropertyInitializedStateSchemaIdentity,
                artifact),
            "PI1ClassProvider", [], ["BoxKey"]);
        var view = reference.StandardClassDeclarations.Single(
            static declaration => declaration.Declaration.Name == "Box")
            .Declaration;

        Assert.That(view.UsesPropertyInitializedState, Is.True);
        Assert.That(view.Properties, Has.Length.EqualTo(1));
        var property = view.Properties[0];
        Assert.That(property.Name, Is.EqualTo("Value"));
        Assert.That(property.Type, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(property.TypeSpecReference, Is.GreaterThan(0u));
        Assert.That(property.GetterMethodDefinitionId >> 24,
            Is.EqualTo(0x16u));
        Assert.That(property.InitMethodDefinitionId >> 24,
            Is.EqualTo(0x16u));
        Assert.That(property.BackingFieldDefinitionId >> 24,
            Is.EqualTo(0x14u));
        Assert.That(property.SelectedExportKeys,
            Is.EqualTo(new[] { "Box.Value" }));
        Assert.That(view.Methods.Single(method =>
                method.DefinitionId == property.InitMethodDefinitionId)
                .ConstructionPropertyDefinitionId,
            Is.EqualTo(property.DefinitionId));
        Assert.That(view.Fields.Single(field =>
                field.DefinitionId == property.BackingFieldDefinitionId)
                .Name,
            Is.Empty);

        var declarationIndex
            = reference.StandardClassDeclarations.IndexOf(
                reference.StandardClassDeclarations.Single(
                    declaration => declaration.Declaration.Name == "Box"));
        var invalidDeclaration
            = reference.StandardClassDeclarations[declarationIndex] with {
                Declaration = view with {
                    Properties = view.Properties.SetItem(0, property with {
                        BackingFieldDefinitionId = 0,
                    }),
                },
            };
        Assert.That(BoundStandardClassDeclarationContract.IsValidTable(
                reference.StandardClassDeclarations.SetItem(
                    declarationIndex, invalidDeclaration),
                reference.GenericNominalTypeDefinitions),
            Is.False);
    }

    [Test]
    public void ReemitsClassImportsFromTypedCarrierWithoutSourceSnapshotPassthrough()
    {
        var provider = CreateClassArtifact("Provider", "Leaf", "LeafKey",
            0xF200_0008);
        var consumer = CreateRootArtifact("Root", "Box", "BoxKey",
            0xF200_0008);
        var reference = CreateReference(consumer, provider,
            C1M1SchemaIdentity);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(""),
            reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!, "RootReemitted",
            C1M1SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        Assert.That(reemitted.AsSpan().SequenceEqual(consumer), Is.False);
        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    reemitted),
                "RootReemitted",
                [new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    provider)],
                ["BoxKey"]);
        Assert.That(reemittedReference.Classes, Is.Empty);
        var reemittedImportedClass
            = reemittedReference.StandardClassDeclarations.Single(
                static declaration
                    => declaration.Declaration.Name == "Leaf");
        Assert.That(reemittedImportedClass.Expectations, Has.Length.EqualTo(1));
        Assert.That(reemittedImportedClass.Expectations[0].Binding.ImportRow,
            Is.EqualTo(1));
        Assert.That(reemittedImportedClass.Expectations[0].Binding
            .UsingModule.ModuleIdentity, Is.EqualTo("RootReemitted"));

        var changedProvider = CreateClassArtifact("Provider", "Leaf",
            "LeafKey", 0xF200_000A);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    reemitted),
                "RootReemitted",
                [new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    changedProvider)],
                ["BoxKey"]));
    }

    [Test]
    public void ReemitsPrivateLocalClassTypeDependenciesWithoutExportingThem()
    {
        var root = CreateArtifact("Root", "Box", "BoxKey", 0,
            C1M1SchemaIdentity, null, null, null,
            localDependencyTypeName: "Leaf");
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(C1M1SchemaIdentity, root),
            "Root", [], ["BoxKey"]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(""),
            reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!, "RootReemitted",
            C1M1SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(reemitted, C1M1SchemaIdentity);
        Assert.That(tables.TypeDefs.Count, Is.EqualTo(2));
        Assert.That(tables.TypeDefs.GetFlags(2) & 3, Is.EqualTo(0u));
        Assert.That(tables.TypeSpecs.GetNominalTypeDefRow(
            tables.Fields.GetTypeSpecRef(1), tables.TypeDefs,
            tables.Imports), Is.EqualTo(2));
        Assert.That(tables.Exports.GetExportsForModule(1),
            Has.Length.EqualTo(1));

        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    reemitted),
                "RootReemitted", [], ["BoxKey"]);
        var boxCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Box value) {}"),
            reemittedReference);
        Assert.That(boxCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, boxCompilation.Diagnostics));
        var privateTypeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Leaf value) {}"),
            reemittedReference);
        Assert.That(privateTypeCompilation.HasErrors, Is.True);
    }

    [Test]
    public void ReemitsPrivateLocalStructTypeDependenciesWithoutExportingThem()
    {
        var root = CreateArtifact("Root", "Box", "BoxKey", 0,
            C1M1SchemaIdentity, null, null, null,
            localDependencyTypeName: "Leaf");
        SetTypeDefFlags(root, 0x04, typeDefRow: 2);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(C1M1SchemaIdentity, root),
            "Root", [], ["BoxKey"]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(""),
            reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!, "RootReemitted",
            C1M1SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = ReadTables(reemitted, C1M1SchemaIdentity);
        Assert.That(tables.TypeDefs.Count, Is.EqualTo(2));
        Assert.That(tables.TypeDefs.GetKind(2), Is.EqualTo(1));
        Assert.That(tables.TypeSpecs.GetNominalTypeDefRow(
            tables.Fields.GetTypeSpecRef(1), tables.TypeDefs,
            tables.Imports), Is.EqualTo(2));
        Assert.That(tables.Exports.GetExportsForModule(1),
            Has.Length.EqualTo(1));

        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    reemitted),
                "RootReemitted", [], ["BoxKey"]);
        var boxCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Box value) {}"),
            reemittedReference);
        Assert.That(boxCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, boxCompilation.Diagnostics));
        var privateTypeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Leaf value) {}"),
            reemittedReference);
        Assert.That(privateTypeCompilation.HasErrors, Is.True);
    }

    [Test]
    public void ReemissionRejectsFieldTypeCarrierThatDiffersFromItsSnapshot()
    {
        var provider = CreateClassArtifact("Provider", "Leaf", "LeafKey",
            0xF200_0008);
        var consumer = CreateRootArtifact("Root", "Box", "BoxKey",
            0xF200_0008);
        var reference = CreateReference(consumer, provider,
            C1M1SchemaIdentity);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(""),
            reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var changedDeclarations = program.StandardClassDeclarations
            .Select(declaration => declaration.Declaration.Name == "Leaf"
                ? declaration with {
                    Declaration = declaration.Declaration with {
                        Fields = [.. declaration.Declaration.Fields.Select(
                            field => field with {
                                TypeSpecReference = 0xF200_000A,
                            })],
                    },
                }
                : declaration)
            .ToImmutableArray();
        var changedProgram = program with {
            StandardClassDeclarations = changedDeclarations,
        };
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.EmitStandard(changedProgram,
                "RootReemitted", C1M1SchemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement));
    }

    [Test]
    public void DoesNotPromoteC1UnknownClassMutability()
    {
        var provider = CreateClassArtifact("Provider", "Leaf", "LeafKey",
            0xF200_0008, C1SchemaIdentity);
        var consumer = CreateRootArtifact("Root", "Box", "BoxKey",
            0xF200_0008, C1SchemaIdentity);
        _ = Assert.Throws<NotSupportedException>(() =>
            CreateReference(consumer, provider, C1SchemaIdentity));
    }

    [Test]
    public void RejectsChangedImportedFieldTypeAndLayout()
    {
        var consumer = CreateRootArtifact("Root", "Box", "BoxKey",
            0xF200_0008);
        var changedFieldType = CreateClassArtifact("Provider", "Leaf",
            "LeafKey", 0xF200_000A);
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateReference(consumer, changedFieldType, C1M1SchemaIdentity));

        var changedLayout = CreateClassArtifact("Provider", "Leaf",
            "LeafKey", 0xF200_0008, layoutKind: 1, pack: 2);
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateReference(consumer, changedLayout, C1M1SchemaIdentity));
    }

    [Test]
    public void RejectsDirectExpectedClassStubUseAndC1M1MutabilityMismatch()
    {
        var provider = CreateClassArtifact("Provider", "Leaf", "LeafKey",
            0xF200_0008);
        var directExpectedStub = CreateRootArtifact("Root", "Box", "BoxKey",
            0xF200_0008, useDirectExpectedStub: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateReference(directExpectedStub, provider,
                C1M1SchemaIdentity));

        var mutableProvider = CreateClassArtifact("Provider", "Leaf",
            "LeafKey", 0xF200_0008, C1M1SchemaIdentity);
        SetTypeDefFlags(mutableProvider, 0x8A);
        var immutableExpectation = CreateRootArtifact("Root", "Box",
            "BoxKey", 0xF200_0008, C1M1SchemaIdentity);
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateReference(immutableExpectation, mutableProvider,
                C1M1SchemaIdentity));
    }

    private static CompilationReference CreateReference(byte[] consumer,
        byte[] provider, string schemaIdentity)
        => TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, consumer), "Root",
            [new TrRmStandardArtifactInput(schemaIdentity, provider)],
            ["BoxKey"]);

    private static byte[] SplitClassRootsAcrossModules(byte[] artifact,
        string schemaIdentity, string secondModuleIdentity)
    {
        var tables = ReadTables(artifact, schemaIdentity);
        if (tables.Modules.Count != 1
            || tables.TypeDefs.Count != 2
            || tables.Exports.GetExportsForModule(1).Single(
                static export => export.ExportKey == "PrefixKey")
                .Definition != 0x1300_0001u
            || tables.Exports.GetExportsForModule(1).Single(
                static export => export.ExportKey == "LeafKey")
                .Definition != 0x1300_0002u)
        {
            throw new InvalidOperationException(
                "The co-located class-root fixture requires Prefix and Leaf TypeDefs in rows one and two.");
        }

        var (typeDefStart, _)
            = tables.Modules.GetTypeDefRange(1);
        var (methodDefStart, methodDefEnd)
            = tables.Modules.GetMethodDefRange(1);
        var secondModuleFirstMethodDef = methodDefEnd;
        var expectedLeafMethodRow = 0;
        var validMethodPartition = true;
        for (var methodRow = methodDefStart; methodRow < methodDefEnd;
            methodRow++)
        {
            var ownerType = tables.Methods.GetOwnerType(methodRow);
            if (ownerType == 2)
            {
                if (secondModuleFirstMethodDef == methodDefEnd)
                {
                    secondModuleFirstMethodDef = methodRow;
                }

                if (methodRow != expectedLeafMethodRow
                    && expectedLeafMethodRow != 0)
                {
                    validMethodPartition = false;
                }

                expectedLeafMethodRow = methodRow + 1;
            }
            else if (secondModuleFirstMethodDef != methodDefEnd)
            {
                validMethodPartition = false;
            }
            else if (ownerType != 1)
            {
                validMethodPartition = false;
            }
        }

        if (!validMethodPartition)
        {
            throw new InvalidOperationException(
                "The co-located class-root fixture requires contiguous class-owned MethodDef ranges.");
        }

        return AddCoLocatedModule(artifact, schemaIdentity,
            secondModuleIdentity, typeDefStart + 1,
            secondModuleFirstMethodDef, 0);
    }

    private static byte[] MoveClassEqualityOwnerToSecondModule(
        byte[] artifact, string schemaIdentity, string secondModuleIdentity)
    {
        var tables = ReadTables(artifact, schemaIdentity);
        if (tables.Modules.Count != 1
            || tables.TypeDefBindingEqualities.Count != 1
            || tables.TypeDefBindingEqualities.GetOwnerModule(1) != 1)
        {
            throw new InvalidOperationException(
                "The co-located equality-owner fixture requires one root-owned equality row.");
        }

        var (_, typeDefEnd)
            = tables.Modules.GetTypeDefRange(1);
        var (_, methodDefEnd)
            = tables.Modules.GetMethodDefRange(1);
        return AddCoLocatedModule(artifact, schemaIdentity,
            secondModuleIdentity, typeDefEnd, methodDefEnd, 2);
    }

    private static byte[] AddCoLocatedModule(byte[] artifact,
        string schemaIdentity, string secondModuleIdentity,
        int secondModuleFirstTypeDef, int secondModuleFirstMethodDef,
        uint equalityOwnerModule)
    {
        var schema = TrRmExecutableCompiler.GetForeignInvocationSchema(
            schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: schemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        if (tables.Modules.Count != 1
            || !index.TryGetSection(0x0001, out var moduleSection, out _))
        {
            throw new InvalidOperationException(
                "The co-located fixture requires one source Module.");
        }

        var (moduleRefStart, moduleRefEnd)
            = tables.Modules.GetModuleRefRange(1);
        var (typeDefStart, typeDefEnd)
            = tables.Modules.GetTypeDefRange(1);
        var (methodDefStart, methodDefEnd)
            = tables.Modules.GetMethodDefRange(1);
        var (sourceFileStart, sourceFileEnd)
            = tables.Modules.GetSourceFileRange(1);
        var (featureStart, featureCount)
            = tables.Modules.GetFeatureRange(1);
        if (secondModuleFirstTypeDef < typeDefStart
            || secondModuleFirstTypeDef > typeDefEnd
            || secondModuleFirstMethodDef < methodDefStart
            || secondModuleFirstMethodDef > methodDefEnd)
        {
            throw new ArgumentOutOfRangeException(
                nameof(secondModuleFirstTypeDef));
        }

        var textRows = new List<(string Text, int Encoding)>(
            tables.Strings.Count + 1);
        for (var row = 1; row <= tables.Strings.Count; row++)
        {
            textRows.Add((tables.Strings.DecodeNamespace(row), 1));
        }

        textRows.Add((secondModuleIdentity, 1));
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(textRows);
        var strings = new TrRmStringTableReader(stringBytes,
            textRows.Count);
        var firstModuleIdentityStringRow = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(moduleSection));
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(1);
        var moduleRows = new[] {
            new TrRmModuleRow(firstModuleIdentityStringRow,
                targetRequirement, runtimePolicy, 0, moduleRefStart,
                typeDefStart, methodDefStart, sourceFileStart,
                featureCount == 0 ? 0 : featureStart, featureCount,
                tables.Modules.GetGoverningRuleEdition(1)),
            new TrRmModuleRow(textRows.Count, targetRequirement,
                runtimePolicy, 0, moduleRefEnd, secondModuleFirstTypeDef,
                secondModuleFirstMethodDef, sourceFileEnd,
                featureCount == 0 ? 0 : featureStart,
                featureCount,
                tables.Modules.GetGoverningRuleEdition(1)),
        };
        var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows, strings,
            tables.ModuleRefs.Count, tables.TypeDefs.Count,
            tables.Methods.Count, tables.Modules.SourceFileCount,
            tables.Features.Count);

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            if (kind == 0x0067 && equalityOwnerModule != 0)
            {
                var equalityBytes = bytes.ToArray();
                for (var row = 0; row < recordCount; row++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        equalityBytes.AsSpan(row * 12), equalityOwnerModule);
                }

                sections.Add(new TrRmSection(kind, flags, recordCount,
                    equalityBytes));
                continue;
            }

            sections.Add(kind switch {
                0x0001 => new TrRmSection(kind, flags, 2, moduleBytes),
                0x0002 => new TrRmSection(kind, flags,
                    checked((uint)textRows.Count), stringBytes),
                _ => new TrRmSection(kind, flags, recordCount,
                    bytes.ToArray()),
            });
        }

        if (equalityOwnerModule != 0
            && !index.TryGetSection(0x0067, out _, out _))
        {
            throw new InvalidOperationException(
                "The co-located equality-owner fixture lost its equality table.");
        }

        var result = TrRmArtifactWriter.Encode(sections, schema,
            standardSchemaIdentity: schemaIdentity);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(result, schema,
                    standardSchemaIdentity: schemaIdentity));
        return result;
    }

    private static (byte[] Artifact, TrRmFeatureRow Feature)
        AddFeatureRequirementToSecondModule(byte[] artifact,
            string schemaIdentity)
    {
        var schema = TrRmExecutableCompiler.GetForeignInvocationSchema(
            schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: schemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        if (tables.Modules.Count != 2
            || !index.TryGetSection(0x0001, out var moduleSection, out _))
        {
            throw new InvalidOperationException(
                "The per-Module Feature fixture requires two co-located Modules.");
        }

        var (featureStart, featureCount)
            = tables.Modules.GetFeatureRange(2);
        var moduleFeatures = new List<TrRmFeatureRow>(featureCount + 1);
        for (var offset = 0; offset < featureCount; offset++)
        {
            moduleFeatures.Add(tables.Features.GetFeature(
                featureStart + offset));
        }

        var feature = TrRmFeatureRow.GenericMethodCallsCandidate;
        if (moduleFeatures.Contains(feature))
        {
            throw new InvalidOperationException(
                "The per-Module Feature fixture already requires GenericMethodCalls.");
        }

        moduleFeatures.Add(feature);
        var encodedModuleFeatures
            = TrRmFeatureTableWriter.Encode(moduleFeatures, schema);
        var moduleBytes = moduleSection.ToArray();
        var secondModuleOffset = 48;
        BinaryPrimitives.WriteUInt32LittleEndian(
            moduleBytes.AsSpan(secondModuleOffset + 24, 4),
            checked((uint)(tables.Features.Count + 1)));
        BinaryPrimitives.WriteUInt32LittleEndian(
            moduleBytes.AsSpan(secondModuleOffset + 28, 4),
            checked((uint)moduleFeatures.Count));

        var featureSectionBytes = index.TryGetSection(0x0004,
                out var featureSection, out _)
            ? new byte[featureSection.Length + encodedModuleFeatures.Length]
            : encodedModuleFeatures;
        if (index.TryGetSection(0x0004, out featureSection, out _))
        {
            featureSection.CopyTo(featureSectionBytes);
            encodedModuleFeatures.CopyTo(featureSectionBytes,
                featureSection.Length);
        }

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind switch {
                0x0001 => new TrRmSection(kind, flags, 2, moduleBytes),
                0x0004 => new TrRmSection(kind, flags,
                    checked((uint)(tables.Features.Count
                        + moduleFeatures.Count)), featureSectionBytes),
                _ => new TrRmSection(kind, flags, recordCount,
                    bytes.ToArray()),
            });
        }

        if (!index.TryGetSection(0x0004, out _, out _))
        {
            sections.Add(new TrRmSection(0x0004, 0,
                checked((uint)moduleFeatures.Count), featureSectionBytes));
            sections.Sort(static (left, right) =>
                left.Kind.CompareTo(right.Kind));
        }

        var result = TrRmArtifactWriter.Encode(sections, schema,
            standardSchemaIdentity: schemaIdentity);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(result, schema,
                    standardSchemaIdentity: schemaIdentity));
        return (result, feature);
    }

    private static (byte[] Artifact, TrRmFeatureRow Feature)
        AddFeatureRequirementToSingleModule(byte[] artifact,
            string schemaIdentity)
    {
        var schema = TrRmExecutableCompiler.GetForeignInvocationSchema(
            schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: schemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        if (tables.Modules.Count != 1
            || !index.TryGetSection(0x0001, out var moduleSection, out _))
        {
            throw new InvalidOperationException(
                "The single-Module Feature fixture requires one Module.");
        }

        var (featureStart, featureCount)
            = tables.Modules.GetFeatureRange(1);
        var moduleFeatures = new List<TrRmFeatureRow>(featureCount + 1);
        for (var offset = 0; offset < featureCount; offset++)
        {
            moduleFeatures.Add(tables.Features.GetFeature(
                featureStart + offset));
        }

        var feature = TrRmFeatureRow.GenericMethodCallsCandidate;
        if (moduleFeatures.Contains(feature))
        {
            throw new InvalidOperationException(
                "The single-Module Feature fixture already requires GenericMethodCalls.");
        }

        moduleFeatures.Add(feature);
        var featureBytes = TrRmFeatureTableWriter.Encode(moduleFeatures,
            schema);
        var moduleBytes = moduleSection.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(moduleBytes.AsSpan(24),
            moduleFeatures.Count == 0 ? 0u : 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(moduleBytes.AsSpan(28),
            checked((uint)moduleFeatures.Count));

        var sections = new List<TrRmSection>(index.SectionCount + 1);
        var addedFeatureSection = false;
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind switch {
                0x0001 => new TrRmSection(kind, flags, 1, moduleBytes),
                0x0004 => AddFeatureRows(ref addedFeatureSection, kind,
                    flags, featureBytes, moduleFeatures.Count),
                _ => new TrRmSection(kind, flags, recordCount,
                    bytes.ToArray()),
            });
        }

        if (!addedFeatureSection)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                checked((uint)moduleFeatures.Count), featureBytes));
        }

        var result = TrRmArtifactWriter.Encode(sections, schema,
            standardSchemaIdentity: schemaIdentity);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(result, schema,
                    standardSchemaIdentity: schemaIdentity));
        return (result, feature);

        static TrRmSection AddFeatureRows(ref bool added, ushort kind,
            ushort flags, byte[] bytes, int count)
        {
            added = true;
            return new TrRmSection(kind, flags, checked((uint)count), bytes);
        }
    }

    private static byte[] CreateSplitClassAliasProvider(byte[] artifact,
        string schemaIdentity, string aliasKey)
    {
        var schema = TrRmExecutableCompiler.GetForeignInvocationSchema(
            schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: schemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        var leafRow = 0;
        for (var row = 1; row <= tables.TypeDefs.Count; row++)
        {
            if (!StringComparer.Ordinal.Equals(
                    tables.TypeDefs.GetName(row), "Leaf"))
            {
                continue;
            }

            if (leafRow != 0)
            {
                throw new InvalidOperationException(
                    "The class-view split fixture has multiple Leaf TypeDefs.");
            }

            leafRow = row;
        }

        var aliasStringRow = 0;
        for (var row = 1; row <= tables.Strings.Count; row++)
        {
            if (!StringComparer.Ordinal.Equals(
                    tables.Strings.DecodeName(row), aliasKey))
            {
                continue;
            }

            if (aliasStringRow != 0)
            {
                throw new InvalidOperationException(
                    "The class-view split fixture has duplicate alias strings.");
            }

            aliasStringRow = row;
        }
        if (leafRow == 0 || aliasStringRow == 0)
        {
            throw new InvalidOperationException(
                "The class-view split fixture lost its Leaf or alias String.");
        }
        if (!index.TryGetSection(0x0013, out var typeDefSection,
                out var typeDefCount)
            || !index.TryGetSection(0x0014, out var fieldSection,
                out var fieldCount)
            || !index.TryGetSection(0x0015, out var layoutSection,
                out var layoutCount)
            || !index.TryGetSection(0x001D, out var exportSection,
                out var exportCount)
            || !index.TryGetSection(0x0023, out var layoutFieldSection,
                out var layoutFieldCount)
            || typeDefCount != tables.TypeDefs.Count)
        {
            throw new InvalidOperationException(
                "The class-view split fixture is missing a required metadata table.");
        }

        var newTypeDefRow = typeDefCount + 1;
        var (leafFieldStart, leafFieldEnd)
            = tables.TypeDefs.GetFieldRange(leafRow);
        var newFieldStart = fieldCount + 1;
        var newFieldRows = new byte[checked(
            (leafFieldEnd - leafFieldStart) * 16)];
        var fieldRowMap = new Dictionary<int, int>();
        for (var offset = 0; offset < leafFieldEnd - leafFieldStart;
            offset++)
        {
            var oldFieldRow = leafFieldStart + offset;
            var newFieldRow = newFieldStart + offset;
            fieldRowMap.Add(oldFieldRow, newFieldRow);
            fieldSection.Slice((oldFieldRow - 1) * 16, 16)
                .CopyTo(newFieldRows.AsSpan(offset * 16, 16));
            BinaryPrimitives.WriteUInt32LittleEndian(
                newFieldRows.AsSpan(offset * 16), checked((uint)newTypeDefRow));
        }

        var (leafLayoutStart, leafLayoutCount)
            = tables.TypeDefs.GetLayoutRange(leafRow);
        var newLayoutStart = layoutCount + 1;
        var layoutRows = new byte[checked(leafLayoutCount * 28)];
        var layoutFieldRows
            = new List<byte[]>(leafLayoutCount);
        var nextLayoutFieldRow = layoutFieldCount + 1;
        for (var offset = 0; offset < leafLayoutCount; offset++)
        {
            var oldLayoutRow = leafLayoutStart + offset;
            var newLayoutRow = newLayoutStart + offset;
            layoutSection.Slice((oldLayoutRow - 1) * 28, 28)
                .CopyTo(layoutRows.AsSpan(offset * 28, 28));
            BinaryPrimitives.WriteUInt32LittleEndian(
                layoutRows.AsSpan(offset * 28), checked((uint)newTypeDefRow));
            var (oldLayoutFieldStart, oldLayoutFieldCount)
                = tables.Layouts.GetLayoutFieldRange(oldLayoutRow);
            BinaryPrimitives.WriteUInt32LittleEndian(
                layoutRows.AsSpan((offset * 28) + 20),
                oldLayoutFieldCount == 0
                    ? 0 : checked((uint)nextLayoutFieldRow));
            for (var fieldOffset = 0;
                fieldOffset < oldLayoutFieldCount;
                fieldOffset++)
            {
                var oldLayoutFieldRow = oldLayoutFieldStart + fieldOffset;
                var layoutFieldBytes = layoutFieldSection.Slice(
                    (oldLayoutFieldRow - 1) * 12, 12).ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(
                    layoutFieldBytes, checked((uint)newLayoutRow));
                var oldFieldRow
                    = tables.LayoutFields.GetFieldDef(oldLayoutFieldRow);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    layoutFieldBytes.AsSpan(8),
                    checked((uint)fieldRowMap[oldFieldRow]));
                layoutFieldRows.Add(layoutFieldBytes);
                nextLayoutFieldRow++;
            }
        }

        var typeDefBytes = new byte[typeDefSection.Length + 44];
        typeDefSection.CopyTo(typeDefBytes);
        typeDefSection.Slice((leafRow - 1) * 44, 44)
            .CopyTo(typeDefBytes.AsSpan(typeDefSection.Length, 44));
        BinaryPrimitives.WriteUInt32LittleEndian(
            typeDefBytes.AsSpan(typeDefSection.Length + 12),
            leafFieldEnd == leafFieldStart
                ? 0 : checked((uint)newFieldStart));
        BinaryPrimitives.WriteUInt32LittleEndian(
            typeDefBytes.AsSpan(typeDefSection.Length + 28),
            leafLayoutCount == 0 ? 0 : checked((uint)newLayoutStart));

        var fieldBytes = new byte[fieldSection.Length
            + newFieldRows.Length];
        fieldSection.CopyTo(fieldBytes);
        newFieldRows.CopyTo(fieldBytes.AsSpan(fieldSection.Length));
        var layoutBytes = new byte[layoutSection.Length + layoutRows.Length];
        layoutSection.CopyTo(layoutBytes);
        layoutRows.CopyTo(layoutBytes.AsSpan(layoutSection.Length));
        var layoutFieldBytes = new byte[layoutFieldSection.Length
            + (layoutFieldRows.Count * 12)];
        layoutFieldSection.CopyTo(layoutFieldBytes);
        for (var row = 0; row < layoutFieldRows.Count; row++)
        {
            layoutFieldRows[row].CopyTo(layoutFieldBytes.AsSpan(
                layoutFieldSection.Length + (row * 12), 12));
        }

        var exportBytes = exportSection.ToArray();
        var aliasExportCount = 0;
        for (var row = 0; row < exportCount; row++)
        {
            var offset = row * 8;
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    exportBytes.AsSpan(offset + 4))
                != (uint)aliasStringRow)
            {
                continue;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                exportBytes.AsSpan(offset),
                0x1300_0000u | checked((uint)newTypeDefRow));
            aliasExportCount++;
        }
        if (aliasExportCount != 1)
        {
            throw new InvalidOperationException(
                "The class-view split fixture requires one exact alias ExportKey.");
        }

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind switch {
                0x0013 => new TrRmSection(kind, flags,
                    checked((uint)(typeDefCount + 1)), typeDefBytes),
                0x0014 => new TrRmSection(kind, flags,
                    checked((uint)(fieldCount + (leafFieldEnd
                        - leafFieldStart))), fieldBytes),
                0x0015 => new TrRmSection(kind, flags,
                    checked((uint)(layoutCount + leafLayoutCount)),
                    layoutBytes),
                0x001D => new TrRmSection(kind, flags, recordCount,
                    exportBytes),
                0x0023 => new TrRmSection(kind, flags,
                    checked((uint)(layoutFieldCount
                        + layoutFieldRows.Count)), layoutFieldBytes),
                _ => new TrRmSection(kind, flags, recordCount,
                    bytes.ToArray()),
            });
        }

        var result = TrRmArtifactWriter.Encode(sections, schema,
            standardSchemaIdentity: schemaIdentity);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(result, schema,
                    standardSchemaIdentity: schemaIdentity));
        return result;
    }

    private static byte[] AddTypeDefBindingEquality(byte[] artifact,
        string schemaIdentity, uint ownerModule, uint leftTypeDef,
        uint rightTypeDef)
    {
        var schema
            = TrRmExecutableCompiler.GetForeignInvocationSchema(
                schemaIdentity);
        var sourceIndex = new TrRmArtifactIndex(artifact, schema, null,
            standardSchemaIdentity: schemaIdentity);
        var equalityBytes = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(equalityBytes, ownerModule);
        BinaryPrimitives.WriteUInt32LittleEndian(
            equalityBytes.AsSpan(4, 4), leftTypeDef);
        BinaryPrimitives.WriteUInt32LittleEndian(
            equalityBytes.AsSpan(8, 4), rightTypeDef);
        var sections = new List<TrRmSection>(
            sourceIndex.SectionCount + 1);
        var inserted = false;
        for (var sectionIndex = 0;
            sectionIndex < sourceIndex.SectionCount; sectionIndex++)
        {
            sourceIndex.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            if (!inserted && kind > 0x0067)
            {
                sections.Add(new TrRmSection(0x0067, 0, 1,
                    equalityBytes));
                inserted = true;
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount), bytes.ToArray()));
        }

        if (!inserted)
        {
            sections.Add(new TrRmSection(0x0067, 0, 1, equalityBytes));
        }

        return TrRmArtifactWriter.Encode(sections, schema,
            standardSchemaIdentity: schemaIdentity);
    }

    private static byte[] CreateRootArtifact(string moduleIdentity,
        string typeName, string exportKey, uint expectedLeafFieldType,
        string schemaIdentity = C1M1SchemaIdentity,
        bool useDirectExpectedStub = false, byte layoutKind = 1,
        uint pack = 1, bool useRequestedLayoutForImports = false)
    {
        var importTypeSpec = TrRmTypeSpecTableWriter.Reference(
            useDirectExpectedStub ? (byte)0x02 : (byte)0x03,
            useDirectExpectedStub ? 2u : 1u);
        return CreateArtifact(moduleIdentity, typeName, exportKey,
            expectedLeafFieldType, schemaIdentity, "Provider", "LeafKey",
            importTypeSpec, layoutKind, pack,
            useRequestedLayoutForImports: useRequestedLayoutForImports);
    }

    internal static byte[] CreateRootArtifactWithTwoClassImports(
        string moduleIdentity, string providerIdentity, string exportKey,
        string firstProviderExportKey, string secondProviderExportKey,
        string schemaIdentity, bool useStructLeaves = false,
        bool includeEquality = false,
        bool uncalledEqualityImports = false,
        string? secondProviderIdentity = null,
        string? firstMethodExportKey = null,
        string? secondMethodExportKey = null,
        int? firstMethodDefault = null,
        int? secondMethodDefault = null,
        bool includeMethodParameterNames = false)
    {
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        var seedArtifact = CreateClassArtifact("SeedProvider", "Seed",
            "SeedKey", 0xF200_0008, schemaIdentity);
        var seedTables = ReadTables(seedArtifact, schemaIdentity);
        var featureRange = seedTables.Modules.GetFeatureRange(1);
        var featureRows = new List<TrRmFeatureRow>(featureRange.Count);
        for (var featureRow = featureRange.First;
            featureRow < featureRange.First + featureRange.Count;
            featureRow++)
        {
            featureRows.Add(seedTables.Features.GetFeature(featureRow));
        }
        var governingRuleEdition
            = seedTables.Modules.GetGoverningRuleEdition(1);
        var names = new (string Text, int Encoding)[] {
            (moduleIdentity, 1),
            ("", 1),
            ("Box", 1),
            ("Leaf", 1),
            ("First", 1),
            ("Second", 1),
            ("Value", 1),
            (providerIdentity, 1),
            (secondProviderExportKey, 1),
            (firstProviderExportKey, 1),
            (exportKey, 1),
        };
        var secondProviderIdentityRow = 0;
        if (secondProviderIdentity is not null)
        {
            secondProviderIdentityRow = names.Length + 1;
            names = [.. names, (secondProviderIdentity, 1)];
        }
        var methodNameRow = 0;
        var firstMethodExportKeyRow = 0;
        var secondMethodExportKeyRow = 0;
        var methodParameterNameRow = 0;
        var methodCount = 0;
        var hasMethodDefaults = firstMethodDefault is not null
            || secondMethodDefault is not null;
        var hasMethodParameterSurface = hasMethodDefaults
            || includeMethodParameterNames;
        if (firstMethodExportKey is not null
            || secondMethodExportKey is not null)
        {
            if (firstMethodExportKey is null
                || secondMethodExportKey is null
                || secondProviderIdentity is not null)
            {
                throw new ArgumentException(
                    "Two class MethodDef Imports require one provider and two exact ExportKeys.");
            }
            if (hasMethodDefaults
                && (firstMethodDefault is null
                    || secondMethodDefault is null
                    || !TrRmExecutableCompiler
                        .GetForeignInvocationSchema(schemaIdentity)
                        .UsesClassParameterDefaults()))
            {
                throw new ArgumentException(
                    "Exact class MethodDef defaults require the matching CM1 schema and both expected views.");
            }
            if (includeMethodParameterNames
                && !TrRmExecutableCompiler
                    .GetForeignInvocationSchema(schemaIdentity)
                    .UsesClassParameterDefaults())
            {
                throw new ArgumentException(
                    "Class MethodDef parameter names require the CM1 parameter-surface schema.");
            }

            methodNameRow = names.Length + 1;
            firstMethodExportKeyRow = methodNameRow + 1;
            secondMethodExportKeyRow = methodNameRow + 2;
            names = [
                .. names,
                ("Read", 1),
                (firstMethodExportKey, 1),
                (secondMethodExportKey, 1),
            ];
            if (hasMethodParameterSurface)
            {
                methodParameterNameRow = names.Length + 1;
                names = [.. names, ("value", 1)];
            }
            methodCount = 2;
        }
        else if (hasMethodParameterSurface)
        {
            throw new ArgumentException(
                "Class MethodDef parameter surfaces require the two expected method views.");
        }

        var schema = TrRmExecutableCompiler.GetForeignInvocationSchema(
            schemaIdentity);
        if (methodCount != 0
            && schema.UsesObjectConstruction()
            && !featureRows.Contains(
                TrRmFeatureRow.ClassConstructionBodiesCandidate))
        {
            featureRows.Add(TrRmFeatureRow.ClassConstructionBodiesCandidate);
        }
        if (hasMethodParameterSurface
            && !featureRows.Contains(TrRmFeatureRow.MethodParameterLabelsCandidate))
        {
            featureRows.Add(TrRmFeatureRow.MethodParameterLabelsCandidate);
        }
        if (hasMethodDefaults
            && !featureRows.Contains(TrRmFeatureRow.TypedFrozenValues))
        {
            featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
        }

        var featureCount = featureRows.Count;
        var featureFirst = featureCount == 0 ? 0 : 1;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(names);
        var strings = new TrRmStringTableReader(stringBytes, names.Length);
        var featureBytes = featureCount == 0 ? []
            : TrRmFeatureTableWriter.Encode(featureRows, schema);
        var moduleRefCount = secondProviderIdentity is null ? 1 : 2;

        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, targetRequirement, 0, 0, 1, 1, 1, 1,
                featureFirst, featureCount, governingRuleEdition),
        ], strings, moduleRefCount, typeDefCount: 3,
            methodDefCount: methodCount,
            sourceFileCount: 0, featureCount: featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount,
            3, methodCount, 0, featureCount);

        var typeSpecRows = new ReadOnlyMemory<byte>[] {
            TrRmTypeSpecTableWriter.Builtin(0x08),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 2),
        };
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            3, 2, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length, 3, 2, 0);
        var frozenValueBuilder = new TrRmFrozenScalarValueBuilder();
        var defaultConstantSourceRows = new uint[2];
        if (hasMethodDefaults)
        {
            defaultConstantSourceRows[0] = frozenValueBuilder.Add(
                new BoundConstant(firstMethodDefault!.Value,
                    PrimitiveType.Int32, default)).ConstantSourceRow;
            defaultConstantSourceRows[1] = frozenValueBuilder.Add(
                new BoundConstant(secondMethodDefault!.Value,
                    PrimitiveType.Int32, default)).ConstantSourceRow;
        }
        var frozenValueTables = frozenValueBuilder.Encode(typeSpecs);
        var parameterSurfaceBytes
            = hasMethodParameterSurface ? new byte[32] : [];
        for (var index = 0; index < defaultConstantSourceRows.Length
                && hasMethodParameterSurface;
            index++)
        {
            var offset = index * 16;
            Write(parameterSurfaceBytes, offset,
                checked((uint)(index + 1)));
            Write(parameterSurfaceBytes, offset + 4, 0);
            Write(parameterSurfaceBytes, offset + 8,
                checked((uint)methodParameterNameRow));
            Write(parameterSurfaceBytes, offset + 12,
                defaultConstantSourceRows[index]);
        }

        var leafFlags = useStructLeaves ? 0x06u : 0x0Au;
        var typeDefRows = uncalledEqualityImports
            ? new[] {
                new TrRmTypeDefRow(2, 3, 0x0A, 1, 1, 0, 0, 1, 1, 0, 0),
                new TrRmTypeDefRow(2, 4, leafFlags, 2, 1, 0, 0, 2, 1, 0, 0),
                new TrRmTypeDefRow(2, 4, leafFlags, 3, 1, 0, 0, 3, 1, 0, 0),
            }
            : [
                new TrRmTypeDefRow(2, 3, 0x0A, 1, 2, 0, 0, 1, 1, 0, 0),
                new TrRmTypeDefRow(2, 4, leafFlags, 3, 1, 0, 0, 2, 1, 0, 0),
                new TrRmTypeDefRow(2, 4, leafFlags, 4, 1, 0, 0, 3, 1, 0, 0),
            ];
        var fieldCount = uncalledEqualityImports ? 3 : 4;
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldCount, genericParamCount: 0,
            layoutCount: 3, contractCount: 0, schema: schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 3, strings,
            modules, fieldCount, 0, 3, 0);
        var fieldRows = uncalledEqualityImports
            ? [
                new TrRmFieldDefRow(1, 7, 0xF200_0008, 2),
                new TrRmFieldDefRow(2, 7, 0xF200_0008, 2),
                new TrRmFieldDefRow(3, 7, 0xF200_0008, 2),
            ]
            : [
                new TrRmFieldDefRow(1, 5, 2, 2),
                new TrRmFieldDefRow(1, 6, 3, 2),
                new TrRmFieldDefRow(2, 7, 0xF200_0008, 2),
                new TrRmFieldDefRow(3, 7, 0xF200_0008, 2),
            ];
        var fieldBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, fieldCount, strings,
            typeDefs, typeSpecs);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutRows = uncalledEqualityImports
            ? [
                new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 1, 1),
                new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 2, 1),
                new TrRmLayoutRow(3, 0, 0, 0, 0, 0, 3, 1),
            ]
            : [
                new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 1, 2),
                new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 3, 1),
                new TrRmLayoutRow(3, 0, 0, 0, 0, 0, 4, 1),
            ];
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
            targetProfiles, fieldCount);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 3, typeDefs,
            targetProfiles, fieldCount);
        var layoutFieldRows = uncalledEqualityImports
            ? [
                new TrRmLayoutFieldRow(1, 0, 1),
                new TrRmLayoutFieldRow(2, 0, 2),
                new TrRmLayoutFieldRow(3, 0, 3),
            ]
            : [
                new TrRmLayoutFieldRow(1, 0, 1),
                new TrRmLayoutFieldRow(1, 1, 2),
                new TrRmLayoutFieldRow(2, 0, 3),
                new TrRmLayoutFieldRow(3, 0, 4),
            ];
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, typeDefs, fields);
        var moduleRefBytes = new byte[checked(moduleRefCount * 12)];
        Write(moduleRefBytes, 0, 8);
        Write(moduleRefBytes, 4, targetRequirement);
        if (moduleRefCount == 2)
        {
            Write(moduleRefBytes, 12,
                checked((uint)secondProviderIdentityRow));
            Write(moduleRefBytes, 16, targetRequirement);
        }
        var importRows = new List<TrRmImportRow> {
            new(1, secondProviderIdentity is null ? 9 : 10,
                0x1300_0002),
            new(moduleRefCount == 2 ? 2 : 1,
                secondProviderIdentity is null ? 10 : 9,
                0x1300_0003),
        };
        TrRmMethodDefTableReader methods = default;
        byte[] methodBytes = [];
        byte[] signatureBytes = [];
        byte[] parameterBytes = [];
        if (methodCount != 0)
        {
            var signatures = new[] {
                new TrRmMethodSigRow(0xF200_0000u, 2, 1, 1, 0, 0),
                new TrRmMethodSigRow(0xF200_0000u, 3, 2, 1, 0, 0),
            };
            signatureBytes = TrRmMethodSigTableWriter.Encode(signatures,
                typeSpecs, parameterCount: 2, genericParamCount: 0);
            var signatureReader = new TrRmMethodSigTableReader(
                signatureBytes, signatures.Length, typeSpecs, 2, 0);
            var parameters = new[] {
                new TrRmParamRow(1, 0, 0xF200_0008, 0, 0),
                new TrRmParamRow(2, 0, 0xF200_0008, 0, 0),
            };
            parameterBytes = TrRmParamTableWriter.Encode(parameters,
                signatureReader, typeSpecs, schema);
            var parameterReader = new TrRmParamTableReader(parameterBytes,
                parameters.Length, signatureReader, typeSpecs, schema);
            var methodsRows = new[] {
                new TrRmMethodDefRow(2, methodNameRow, 1, 2, 0, 0, 0),
                new TrRmMethodDefRow(3, methodNameRow, 2, 2, 0, 0, 0),
            };
            methodBytes = TrRmMethodDefTableWriter.Encode(methodsRows,
                strings, modules, typeDefs, signatureReader,
                contractCount: 0, bodyCount: 0, schema: schema);
            methods = new TrRmMethodDefTableReader(methodBytes,
                methodsRows.Length, strings, modules, typeDefs,
                signatureReader, 0, 0, schema);
            _ = parameterReader;
            importRows.Add(new TrRmImportRow(1, firstMethodExportKeyRow,
                0x1600_0001));
            importRows.Add(new TrRmImportRow(1, secondMethodExportKeyRow,
                0x1600_0002));
        }

        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes,
            moduleRefCount, strings, modules);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureCount, schema);
        byte[] importBytes;
        if (methodCount == 0)
        {
            importBytes = new byte[24];
            Write(importBytes, 0, 1);
            Write(importBytes, 4,
                secondProviderIdentity is null ? 9u : 10u);
            Write(importBytes, 8, 0x1300_0002);
            Write(importBytes, 12, moduleRefCount == 2 ? 2u : 1u);
            Write(importBytes, 16,
                secondProviderIdentity is null ? 10u : 9u);
            Write(importBytes, 20, 0x1300_0003);
        }
        else
        {
            importBytes = TrRmImportTableWriter.Encode(importRows, strings,
                moduleRefs, typeDefs, methods, default, default, default,
                fields, features, modules, schema);
        }
        var exportBytes = new byte[8];
        Write(exportBytes, 0, 0x1300_0001);
        Write(exportBytes, 4, 11);

        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, checked((uint)names.Length), stringBytes),
            new TrRmSection(0x0004, 0,
                checked((uint)featureCount), featureBytes),
            new(0x0010, 0, checked((uint)moduleRefCount),
                moduleRefBytes),
            new(0x0011, 0, checked((uint)typeSpecRows.Length),
                typeSpecBytes),
            new(0x0013, 0, 3, typeDefBytes),
            new(0x0014, 0, checked((uint)fieldCount), fieldBytes),
            new(0x0015, 0, 3, layoutBytes),
            new(0x001C, 0, checked((uint)importRows.Count), importBytes),
            new(0x001D, 0, 1, exportBytes),
            new(0x0023, 0, checked((uint)fieldCount), layoutFieldBytes),
        };
        if (methodCount != 0)
        {
            sections.Add(new TrRmSection(0x0016, 0,
                checked((uint)methodCount), methodBytes));
            sections.Add(new TrRmSection(0x0017, 0, 2, signatureBytes));
            sections.Add(new TrRmSection(0x0018, 0, 2, parameterBytes));
        }
        if (frozenValueTables.BlobCount != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                checked((uint)frozenValueTables.BlobCount),
                frozenValueTables.BlobSection));
        }
        if (frozenValueTables.ConstantSourceCount != 0)
        {
            sections.Add(new TrRmSection(0x0030, 0,
                checked((uint)frozenValueTables.ConstantSourceCount),
                frozenValueTables.ConstantSourceSection));
            sections.Add(new TrRmSection(0x003F, 0,
                checked((uint)frozenValueTables.FrozenValueNodeCount),
                frozenValueTables.FrozenValueNodeSection));
        }
        if (hasMethodParameterSurface)
        {
            sections.Add(new TrRmSection(0x0066, 0, 2,
                parameterSurfaceBytes));
        }
        if (includeEquality)
        {
            var equalityBytes = new byte[12];
            Write(equalityBytes, 0, 1);
            Write(equalityBytes, 4, 2);
            Write(equalityBytes, 8, 3);
            sections.Add(new TrRmSection(0x0067, 0, 1, equalityBytes));
        }
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections, schema,
            standardSchemaIdentity: schemaIdentity);
        _ = ReadTables(artifact, schemaIdentity);
        return artifact;
    }

    private static byte[] CreateRootArtifactWithNestedStructExpectation(
        string moduleIdentity, string providerIdentity,
        string leafExportKey, string innerExportKey, string exportKey,
        string schemaIdentity, string? innerProviderIdentity = null)
    {
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        var seedArtifact = CreateClassArtifact("SeedProvider", "Seed",
            "SeedKey", 0xF200_0008, schemaIdentity);
        var seedTables = ReadTables(seedArtifact, schemaIdentity);
        var featureSection = GetSection(seedArtifact, schemaIdentity, 0x0004);
        var featureRange = seedTables.Modules.GetFeatureRange(1);
        var featureCount = featureRange.Count;
        var selectedInnerProviderIdentity
            = innerProviderIdentity ?? providerIdentity;
        var hasSeparateInnerProvider
            = !StringComparer.Ordinal.Equals(
                selectedInnerProviderIdentity, providerIdentity);
        var names = new List<(string Text, int Encoding)> {
            (moduleIdentity, 1),
            ("", 1),
            ("Box", 1),
            ("Leaf", 1),
            ("Inner", 1),
            ("Value", 1),
            (providerIdentity, 1),
        };
        var providerIdentityRow = names.Count;
        var innerProviderIdentityRow = providerIdentityRow;
        if (hasSeparateInnerProvider)
        {
            names.Add((selectedInnerProviderIdentity, 1));
            innerProviderIdentityRow = names.Count;
        }

        names.Add((leafExportKey, 1));
        var leafExportKeyRow = names.Count;
        names.Add((innerExportKey, 1));
        var innerExportKeyRow = names.Count;
        names.Add((exportKey, 1));
        var exportKeyRow = names.Count;
        var moduleRefCount = hasSeparateInnerProvider ? 2 : 1;
        var innerProviderModuleRefRow = hasSeparateInnerProvider ? 2u : 1u;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(names);
        var strings = new TrRmStringTableReader(stringBytes, names.Count);
        var typeSpecRows = new ReadOnlyMemory<byte>[] {
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 2),
        };
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            3, 2, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length, 3, 2, 0);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, targetRequirement, 0, 0, 1, 1, 1, 1,
                featureRange.First, featureCount,
                seedTables.Modules.GetGoverningRuleEdition(1)),
        ], strings, moduleRefCount, typeDefCount: 3, methodDefCount: 0,
            sourceFileCount: 0, featureCount: featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount, 3, 0, 0, featureCount);
        var typeDefRows = new[] {
            new TrRmTypeDefRow(2, 3, 0x0A, 1, 1, 0, 0, 1, 1, 0, 0),
            new TrRmTypeDefRow(2, 4, 0x0A, 2, 1, 0, 0, 2, 1, 0, 0),
            new TrRmTypeDefRow(2, 5, 0x06, 3, 1, 0, 0, 3, 1, 0, 0),
        };
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldCount: 3, genericParamCount: 0,
            layoutCount: 3, contractCount: 0,
            schema: TrRmExecutableCompiler.GetForeignInvocationSchema(
                schemaIdentity));
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 3, strings,
            modules, 3, 0, 3, 0);
        var fieldRows = new[] {
            new TrRmFieldDefRow(1, 6, 1, 2),
            new TrRmFieldDefRow(2, 6, 2, 2),
            new TrRmFieldDefRow(3, 6, 0xF200_0008, 2),
        };
        var fieldBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 3, strings,
            typeDefs, typeSpecs);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutRows = new[] {
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 1, 1),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 2, 1),
            new TrRmLayoutRow(3, 0, 0, 0, 0, 0, 3, 1),
        };
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
            targetProfiles, layoutFieldCount: 3);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 3, typeDefs,
            targetProfiles, 3);
        var layoutFieldRows = new[] {
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(2, 0, 2),
            new TrRmLayoutFieldRow(3, 0, 3),
        };
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, typeDefs, fields);
        var moduleRefBytes = new byte[moduleRefCount * 12];
        Write(moduleRefBytes, 0, checked((uint)providerIdentityRow));
        Write(moduleRefBytes, 4, targetRequirement);
        if (moduleRefCount == 2)
        {
            Write(moduleRefBytes, 12,
                checked((uint)innerProviderIdentityRow));
            Write(moduleRefBytes, 16, targetRequirement);
        }
        var importBytes = new byte[24];
        Write(importBytes, 0, 1);
        Write(importBytes, 4, checked((uint)leafExportKeyRow));
        Write(importBytes, 8, 0x1300_0002);
        Write(importBytes, 12, innerProviderModuleRefRow);
        Write(importBytes, 16, checked((uint)innerExportKeyRow));
        Write(importBytes, 20, 0x1300_0003);
        var exportBytes = new byte[8];
        Write(exportBytes, 0, 0x1300_0001);
        Write(exportBytes, 4, checked((uint)exportKeyRow));
        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, checked((uint)names.Count), stringBytes),
            featureSection,
            new(0x0010, 0, checked((uint)moduleRefCount), moduleRefBytes),
            new(0x0011, 0, checked((uint)typeSpecRows.Length),
                typeSpecBytes),
            new(0x0013, 0, 3, typeDefBytes),
            new(0x0014, 0, 3, fieldBytes),
            new(0x0015, 0, 3, layoutBytes),
            new(0x001C, 0, 2, importBytes),
            new(0x001D, 0, 1, exportBytes),
            new(0x0023, 0, 3, layoutFieldBytes),
        };
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifactSchema
            = TrRmExecutableCompiler.GetForeignInvocationSchema(
                schemaIdentity);
        var artifact = TrRmArtifactWriter.Encode(sections, artifactSchema,
            standardSchemaIdentity: schemaIdentity);
        _ = ReadTables(artifact, schemaIdentity);
        return artifact;
    }

    private static byte[] CreateClassArtifact(string moduleIdentity,
        string typeName, string exportKey, uint fieldTypeSpecReference,
        string schemaIdentity = C1M1SchemaIdentity,
        byte layoutKind = 1, uint pack = 1,
        string? fieldExportKey = null, uint fieldFlags = 2,
        string? additionalFieldExportKey = null)
        => CreateArtifact(moduleIdentity, typeName, exportKey,
            fieldTypeSpecReference, schemaIdentity, null, null, null,
            layoutKind, pack, fieldExportKey: fieldExportKey,
            fieldFlags: fieldFlags,
            additionalFieldExportKey: additionalFieldExportKey);

    private static byte[] CreateRelocatedClassProvider(
        string moduleIdentity, string schemaIdentity,
        string? leafAliasKey = null, bool includeReadMethod = false,
        int? readDefaultValue = null, bool includePrefix = true,
        bool includePrefixField = false)
    {
        var prefixText = includePrefix
            ? includePrefixField
                ? "public class Prefix { public int32 Noise; public Prefix() { } }"
                : "public class Prefix { public Prefix() { } }"
            : string.Empty;
        var methodDefault = readDefaultValue is int defaultValue
            ? $" = {defaultValue}" : string.Empty;
        var readMethodText = includeReadMethod
            ? $"public void Read(int32 value{methodDefault}) {{}}"
            : string.Empty;
        var tree = SyntaxTree.Parse($$"""
            {{prefixText}}
            public class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
                {{readMethodText}}
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declarations = tree.Root.Children.OfType<SyntaxNode>()
            .Where(static node =>
                node.Kind == SyntaxKind.ClassDeclaration).ToArray();
        var leaf = declarations.Single(static declaration =>
            declaration.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Leaf"));
        var leafField = leaf.Children.OfType<SyntaxNode>().Single(
            static node => node.Kind == SyntaxKind.FieldDeclaration);
        var readMethod = includeReadMethod
            ? leaf.Children.OfType<SyntaxNode>().Single(
                static node =>
                    node.Kind == SyntaxKind.FunctionDeclaration)
            : null;
        var leafMemberExports
            = ImmutableArray.CreateBuilder<TrRmSourceClassMemberExport>();
        leafMemberExports.Add(new TrRmSourceClassMemberExport(0,
            leafField.Span, "Leaf.Value"));
        if (readMethod is not null)
        {
            leafMemberExports.Add(new TrRmSourceClassMemberExport(
                0, readMethod.Span, "Leaf.Read"));
            leafMemberExports.Add(new TrRmSourceClassMemberExport(
                0, readMethod.Span, "Leaf.ReadAlias"));
        }

        var classExports = new List<TrRmSourceClassExport>();
        if (includePrefix)
        {
            var prefix = declarations.Single(static declaration =>
                declaration.Children.OfType<SyntaxTokenElement>().Any(
                    static token => token.Token.Name == "Prefix"));
            classExports.Add(new TrRmSourceClassExport(program, 0,
                prefix.Span, "PrefixKey"));
        }

        classExports.Add(new TrRmSourceClassExport(program, 0,
            leaf.Span, "LeafKey", leafMemberExports.ToImmutable()));
        if (leafAliasKey is not null)
        {
            classExports.Add(new TrRmSourceClassExport(program, 0,
                leaf.Span, leafAliasKey));
        }

        return TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], classExports);
    }

    private static byte[] CreateFieldReferenceClassProvider(
        string moduleIdentity, string schemaIdentity, bool includePrefix)
    {
        var prefixText = includePrefix
            ? "public class Prefix { public Prefix() { } }"
            : string.Empty;
        var mutableClass
            = TrRmExecutableCompiler.GetForeignInvocationSchema(
                schemaIdentity).UsesMethodReceiverAccess();
        var mutableModifier = mutableClass ? "mut " : string.Empty;
        var setMethod = mutableClass
            ? "public mut void Set(int32 value) { this.Value = value; }"
            : string.Empty;
        var tree = SyntaxTree.Parse($$"""
            {{prefixText}}
            public {{mutableModifier}}class Box {
                public {{mutableModifier}}int32 Value;
                public Box(int32 value) { Value = value; }
                public int32 Identity(int32 value) { return value; }
                public int32 Forward(int32 value) { return Identity(value); }
                public int32 ReadValue() { return this.Value; }
                [return: DependsOn(this)]
                public unsafe ref<int32> ReadReference() {
                    return ref this.Value;
                }
                {{setMethod}}
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declarations = tree.Root.Children.OfType<SyntaxNode>()
            .Where(static node =>
                node.Kind == SyntaxKind.ClassDeclaration).ToArray();
        var box = declarations.Single(static declaration =>
            declaration.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Box"));
        var boxMembers = box.Children.OfType<SyntaxNode>();
        var field = boxMembers.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        SyntaxNode GetFunction(string name)
            => boxMembers.Single(node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                StringComparer.Ordinal.Equals(token.Token.Name, name)));
        var members
            = ImmutableArray.CreateBuilder<TrRmSourceClassMemberExport>();
        members.Add(new TrRmSourceClassMemberExport(0, field.Span,
            "Box.Value"));
        foreach (var methodName in new[] {
            "Identity", "Forward", "ReadValue", "ReadReference",
        })
        {
            var method = GetFunction(methodName);
            members.Add(new TrRmSourceClassMemberExport(0,
                method.Span, "Box." + methodName));
            if (methodName == "ReadReference")
            {
                members.Add(new TrRmSourceClassMemberExport(0,
                    method.Span, "Box.ReadReferenceAlias"));
            }
        }
        if (mutableClass)
        {
            var set = GetFunction("Set");
            members.Add(new TrRmSourceClassMemberExport(0,
                set.Span, "Box.Set"));
        }
        var classExports = new List<TrRmSourceClassExport>();
        if (includePrefix)
        {
            var prefix = declarations.Single(static declaration =>
                declaration.Children.OfType<SyntaxTokenElement>().Any(
                    static token => token.Token.Name == "Prefix"));
            classExports.Add(new TrRmSourceClassExport(program, 0,
                prefix.Span, "PrefixKey"));
        }

        classExports.Add(new TrRmSourceClassExport(program, 0,
            box.Span, "BoxKey", members.ToImmutable()));
        return TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], classExports);
    }

    private static byte[] CreateArtifact(string moduleIdentity,
        string typeName, string exportKey, uint fieldTypeSpecReference,
        string schemaIdentity, string? importIdentity, string? importKey,
        byte[]? importedFieldTypeSpec, byte layoutKind = 1, uint pack = 1,
        string? localDependencyTypeName = null,
        bool useRequestedLayoutForImports = false,
        string? fieldExportKey = null, uint fieldFlags = 2,
        string? additionalFieldExportKey = null,
        string? localDependencyExportKey = null,
        string? importedTypeName = null, string? fieldName = null,
        string? secondaryFieldName = null)
    {
        var seedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public bool Seed(bool publish, int32 value,
                    ref<init int32> result) {
                    if (publish) {
                        *result = value;
                        return true;
                    }
                    return false;
                }
                """));
        Assert.That(seedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, seedCompilation.Diagnostics));
        var targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        var seedArtifact = TrRmExecutableCompiler.EmitStandard(
            seedCompilation.Program!, moduleIdentity, schemaIdentity,
            targetRequirement);
        var seedTables = ReadTables(seedArtifact, schemaIdentity);
        var featureSection = GetSection(seedArtifact, schemaIdentity, 0x0004);
        var featureRange = seedTables.Modules.GetFeatureRange(1);
        var featureCount = featureRange.Count;

        var names = new List<(string Text, int Encoding)> {
            (moduleIdentity, 1),
            ("", 1),
            (typeName, 1),
            (fieldName ?? "Value", 1),
        };
        var typeNameRow = 3;
        var fieldNameRow = 4;
        var secondaryFieldNameRow = fieldNameRow;
        if (secondaryFieldName is not null)
        {
            names.Add((secondaryFieldName, 1));
            secondaryFieldNameRow = names.Count;
        }
        var leafNameRow = 0;
        var localDependencyNameRow = 0;
        var importIdentityRow = 0;
        var importKeyRow = 0;
        if (importIdentity is not null)
        {
            names.Add((importIdentity, 1));
            importIdentityRow = names.Count;
            names.Add((importedTypeName ?? "Leaf", 1));
            leafNameRow = names.Count;
            names.Add((importKey!, 1));
            importKeyRow = names.Count;
        }
        if (localDependencyTypeName is not null)
        {
            names.Add((localDependencyTypeName, 1));
            localDependencyNameRow = names.Count;
        }
        names.Add((exportKey, 1));
        var exportKeyRow = names.Count;
        var localDependencyExportKeyRow = 0;
        if (localDependencyExportKey is not null)
        {
            names.Add((localDependencyExportKey, 1));
            localDependencyExportKeyRow = names.Count;
        }
        var fieldExportKeyRow = 0;
        if (fieldExportKey is not null)
        {
            names.Add((fieldExportKey, 1));
            fieldExportKeyRow = names.Count;
        }
        var additionalFieldExportKeyRow = 0;
        if (additionalFieldExportKey is not null)
        {
            names.Add((additionalFieldExportKey, 1));
            additionalFieldExportKeyRow = names.Count;
        }
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(names);
        var strings = new TrRmStringTableReader(stringBytes, names.Count);

        var importCount = importIdentity is null ? 0 : 1;
        if (importCount != 0 && localDependencyTypeName is not null)
        {
            throw new ArgumentException(
                "The local TypeDef dependency fixture cannot also import a provider.");
        }
        var moduleRefCount = importCount;
        var typeDefCount = importCount == 0
            && localDependencyTypeName is null ? 1 : 2;
        var fieldCount = typeDefCount;
        var typeSpecRows = new List<ReadOnlyMemory<byte>>();
        var boxFieldTypeSpecReference = fieldTypeSpecReference;
        var localDependencyFieldTypeSpecReference
            = fieldTypeSpecReference;
        if (localDependencyTypeName is not null)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02, 2));
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Builtin(8));
            boxFieldTypeSpecReference = 1;
            localDependencyFieldTypeSpecReference = 2;
        }
        else if (importedFieldTypeSpec is not null)
        {
            typeSpecRows.Add(importedFieldTypeSpec);
            boxFieldTypeSpecReference = 1;
        }
        else
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Builtin(8));
        }
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount, importCount, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, typeDefCount, importCount, 0);

        var featureFirst = featureRange.First;
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, targetRequirement, 0, 0, 1, 1, 1, 1,
                featureFirst, featureCount,
                seedTables.Modules.GetGoverningRuleEdition(1)),
        ], strings, moduleRefCount, typeDefCount, 0, 0, featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount, typeDefCount, 0, 0, featureCount);
        var typeDefRows = importCount == 0
            && localDependencyTypeName is null
            ? new[] {
                new TrRmTypeDefRow(2, typeNameRow, 0x0A, 1, 1,
                    0, 0, 1, 1, 0, 0),
            }
            : importCount == 0
            ? new[] {
                new TrRmTypeDefRow(2, typeNameRow, 0x0A, 1, 1,
                    0, 0, 1, 1, 0, 0),
                new TrRmTypeDefRow(2, localDependencyNameRow, 0x08,
                    2, 1, 0, 0, 2, 1, 0, 0),
            }
            : new[] {
                new TrRmTypeDefRow(2, typeNameRow, 0x0A, 1, 1,
                    0, 0, 1, 1, 0, 0),
                new TrRmTypeDefRow(2, leafNameRow, 0x0A, 2, 1,
                    0, 0, 2, 1, 0, 0),
            };
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldCount, 0, typeDefCount, 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules, fieldCount, 0, typeDefCount, 0);
        var fieldRows = importCount == 0
            && localDependencyTypeName is null
            ? new[] {
                new TrRmFieldDefRow(1, fieldNameRow,
                    fieldTypeSpecReference, fieldFlags),
            }
            : importCount == 0
            ? new[] {
                new TrRmFieldDefRow(1, fieldNameRow,
                    boxFieldTypeSpecReference, fieldFlags),
                new TrRmFieldDefRow(2, secondaryFieldNameRow,
                    localDependencyFieldTypeSpecReference, 2),
            }
            : new[] {
                new TrRmFieldDefRow(1, fieldNameRow,
                    boxFieldTypeSpecReference, 2),
                new TrRmFieldDefRow(2, secondaryFieldNameRow,
                    fieldTypeSpecReference, 2),
            };
        var fieldBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, fieldCount,
            strings, typeDefs, typeSpecs);
        var profileSection = TryGetSection(seedArtifact, schemaIdentity,
            0x0003);
        var targetProfiles = profileSection is TrRmSection presentProfiles
            ? new TrRmTargetProfileTableReader(presentProfiles.Bytes.Span,
                checked((int)presentProfiles.RecordCount))
            : new TrRmTargetProfileTableReader([], 0);
        var layoutRows = new List<TrRmLayoutRow>(typeDefCount);
        var layoutFieldRows = new List<TrRmLayoutFieldRow>(typeDefCount);
        for (var typeDefRow = 1; typeDefRow <= typeDefCount; typeDefRow++)
        {
            layoutRows.Add(new TrRmLayoutRow(typeDefRow, 0,
                importCount == 0 || useRequestedLayoutForImports
                    ? layoutKind : (byte)1,
                0, 0,
                importCount == 0 || useRequestedLayoutForImports
                    ? pack : 1,
                typeDefRow, 1));
            layoutFieldRows.Add(new TrRmLayoutFieldRow(typeDefRow,
                0, typeDefRow));
        }
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
            targetProfiles, typeDefCount);
        var layouts = new TrRmLayoutTableReader(layoutBytes, typeDefCount,
            typeDefs, targetProfiles, typeDefCount);
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, typeDefs, fields);

        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, checked((uint)names.Count), stringBytes),
            featureSection,
            new(0x0013, 0, checked((uint)typeDefCount), typeDefBytes),
            new(0x0014, 0, checked((uint)fieldCount), fieldBytes),
        };
        if (profileSection is TrRmSection targetProfile)
        {
            sections.Insert(2, targetProfile);
        }
        sections.Add(new TrRmSection(0x0011, 0,
            checked((uint)typeSpecRows.Count), typeSpecBytes));
        sections.Add(new TrRmSection(0x0015, 0,
            checked((uint)typeDefCount), layoutBytes));
        sections.Add(new TrRmSection(0x0023, 0,
            checked((uint)fieldCount), layoutFieldBytes));
        if (importCount != 0)
        {
            var moduleRefBytes = new byte[12];
            Write(moduleRefBytes, 0, checked((uint)importIdentityRow));
            Write(moduleRefBytes, 4, targetRequirement);
            var importBytes = new byte[12];
            Write(importBytes, 0, 1);
            Write(importBytes, 4, checked((uint)importKeyRow));
            Write(importBytes, 8, 0x1300_0002);
            sections.Add(new TrRmSection(0x0010, 0, 1, moduleRefBytes));
            sections.Add(new TrRmSection(0x001C, 0, 1, importBytes));
        }

        var exportCount = 1 + (localDependencyExportKey is null ? 0 : 1)
            + (fieldExportKey is null ? 0 : 1)
            + (additionalFieldExportKey is null ? 0 : 1);
        var exportBytes = new byte[checked(exportCount * 8)];
        Write(exportBytes, 0, 0x1300_0001);
        Write(exportBytes, 4, checked((uint)exportKeyRow));
        var exportOffset = 8;
        if (localDependencyExportKey is not null)
        {
            Write(exportBytes, exportOffset, 0x1300_0002);
            Write(exportBytes, exportOffset + 4,
                checked((uint)localDependencyExportKeyRow));
            exportOffset += 8;
        }
        if (fieldExportKey is not null)
        {
            Write(exportBytes, exportOffset, 0x1400_0001);
            Write(exportBytes, exportOffset + 4,
                checked((uint)fieldExportKeyRow));
            exportOffset += 8;
        }
        if (additionalFieldExportKey is not null)
        {
            Write(exportBytes, exportOffset, 0x1400_0001);
            Write(exportBytes, exportOffset + 4,
                checked((uint)additionalFieldExportKeyRow));
        }
        sections.Add(new TrRmSection(0x001D, 0,
            checked((uint)exportCount), exportBytes));
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifactSchema = StringComparer.Ordinal.Equals(schemaIdentity,
                C1SchemaIdentity)
            ? TrRmArtifactSchema.C1Draft
            : TrRmArtifactSchema.C1M1Draft;
        var artifact = TrRmArtifactWriter.Encode(sections, artifactSchema);
        _ = ReadTables(artifact, schemaIdentity);
        return artifact;
    }

    private static byte[] RewriteC1M1BodyFieldMapAsFieldPath(
        byte[] artifact, int bodyRow, uint rootTypeSpecRef,
        uint? fieldReferenceOverride = null)
    {
        var schema
            = TrRmArtifactSchema
                .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft;
        var index = TrRmArtifactIndex
            .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        if (body.FieldCount != 1)
        {
            throw new InvalidDataException(
                "The FieldPath fixture requires one FieldMap entry.");
        }

        var sourceFieldReference
            = BinaryPrimitives.ReadUInt32LittleEndian(body.FieldMap);
        if ((sourceFieldReference >> 24) != 0x1C)
        {
            throw new InvalidDataException(
                "The FieldPath fixture requires a selected FieldDef Import.");
        }
        var fieldReference = fieldReferenceOverride ?? sourceFieldReference;

        var fieldPathRow
            = new TrRmFieldPathRow(rootTypeSpecRef, [fieldReference]);
        var fieldPathBytes = TrRmFieldPathTableWriter.Encode([
            fieldPathRow,
            fieldPathRow,
        ], tables.TypeSpecs, tables.Fields, tables.Imports);
        var fieldMap = body.FieldMap.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(fieldMap,
            0x1D00_0002u);

        var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            blocks[blockIndex] = new(
                BinaryPrimitives.ReadUInt32LittleEndian(block),
                BinaryPrimitives.ReadUInt32LittleEndian(
                    block.Slice(sizeof(uint))),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    block.Slice(2 * sizeof(uint))));
        }

        var records = new ReadOnlyMemory<byte>[body.RecordCount];
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            records[recordIndex] = body.GetRecord(recordIndex).ToArray();
        }

        var rewrittenBody = TrRmRegisterBodyWriter.Encode(
            body.HomeMap.ToArray(), body.TypeMap.ToArray(),
            body.MethodMap.ToArray(), fieldMap,
            body.ConstantMap.ToArray(), blocks, records,
            contractCount: tables.Contracts.Count, schema: schema);
        var bodyRows = new ReadOnlyMemory<byte>[tables.Bodies.Count];
        for (var row = 1; row <= tables.Bodies.Count; row++)
        {
            bodyRows[row - 1] = row == bodyRow
                ? rewrittenBody : tables.Bodies.GetRow(row).ToArray();
        }

        var bodyBytes = TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var sections = new List<TrRmSection>(index.SectionCount + 1);
        var hasBodySection = false;
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            if (kind == 0x0020)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)tables.Bodies.Count), bodyBytes));
                hasBodySection = true;
            }
            else if (kind != 0x004B)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), bytes.ToArray()));
            }
        }

        if (!hasBodySection)
        {
            throw new InvalidDataException(
                "The FieldPath fixture has no Body section.");
        }

        sections.Add(new TrRmSection(0x004B, 0, 2, fieldPathBytes));
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections);
    }

    private static TrRmStructuralMetadataValidator.ValidatedExecutionTables
        ReadTables(byte[] artifact, string schemaIdentity)
    {
        var index = new TrRmArtifactIndex(artifact,
            TrRmExecutableCompiler.GetForeignInvocationSchema(
                schemaIdentity),
            standardSchemaIdentity: schemaIdentity);
        return TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
    }

    private static TrRmSection GetSection(byte[] artifact,
        string schemaIdentity, ushort kind)
        => TryGetSection(artifact, schemaIdentity, kind)
            ?? throw new InvalidDataException(
                $"TRRM section 0x{kind:X4} is missing.");

    private static TrRmSection? TryGetSection(byte[] artifact,
        string schemaIdentity, ushort kind)
    {
        var index = new TrRmArtifactIndex(artifact,
            TrRmExecutableCompiler.GetForeignInvocationSchema(
                schemaIdentity),
            standardSchemaIdentity: schemaIdentity);
        return index.TryGetSection(kind, out var bytes, out var count)
            ? new TrRmSection(kind, 0, checked((uint)count),
                bytes.ToArray())
            : null;
    }

    private static void SetTypeDefFlags(byte[] artifact, uint flags,
        int typeDefRow = 1)
    {
        var sectionCount = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(12)));
        for (var sectionIndex = 0; sectionIndex < sectionCount;
            sectionIndex++)
        {
            var directoryOffset = 32 + (sectionIndex * 16);
            if (BinaryPrimitives.ReadUInt16LittleEndian(
                    artifact.AsSpan(directoryOffset)) != 0x0013)
            {
                continue;
            }

            var sectionOffset = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    artifact.AsSpan(directoryOffset + 8)));
            Write(artifact,
                sectionOffset + ((typeDefRow - 1) * 44) + 8, flags);
            return;
        }

        throw new InvalidDataException("The TypeDef section is missing.");
    }

    private static void Write(Span<byte> bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);
}
