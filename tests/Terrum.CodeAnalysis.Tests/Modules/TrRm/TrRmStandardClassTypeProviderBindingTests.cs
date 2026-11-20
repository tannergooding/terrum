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
    public void ReemitsImportedStructFieldMapBodiesWithoutSource()
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
        Assert.That(fieldPathTables.FieldPaths.Count, Is.EqualTo(1));
        var fieldPathBody = fieldPathTables.Bodies.GetRegisterBody(
            fieldPathTables.Methods.GetBodyRow(artifactReadMethodRow),
            fieldPathTables.Contracts.Count);
        var fieldPathToken
            = BinaryPrimitives.ReadUInt32LittleEndian(
                fieldPathBody.FieldMap);
        Assert.That(fieldPathToken, Is.EqualTo(0x1D00_0001u));

        var fieldPathReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    fieldPathArtifact),
                "StructFieldConsumer", [providerInput], ["ContainerKey"]);
        var fieldPathCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), fieldPathReference);
        Assert.That(fieldPathCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                fieldPathCompilation.Diagnostics));
        var fieldPathReemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)fieldPathCompilation.Program!,
            "StructFieldPathConsumerReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
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

        var relocatedProvider = CreateRelocatedClassProvider(
            "StructProvider", schemaIdentity);
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

    private static byte[] CreateRootArtifactWithTwoClassImports(
        string moduleIdentity, string providerIdentity, string exportKey,
        string firstProviderExportKey, string secondProviderExportKey,
        string schemaIdentity, bool useStructLeaves = false,
        bool includeEquality = false)
    {
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        var seedArtifact = CreateClassArtifact("SeedProvider", "Seed",
            "SeedKey", 0xF200_0008, schemaIdentity);
        var seedTables = ReadTables(seedArtifact, schemaIdentity);
        var featureSection = GetSection(seedArtifact, schemaIdentity, 0x0004);
        var featureRange = seedTables.Modules.GetFeatureRange(1);
        var featureCount = featureRange.Count;
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
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(names);
        var strings = new TrRmStringTableReader(stringBytes, names.Length);
        var schema = TrRmExecutableCompiler.GetForeignInvocationSchema(
            schemaIdentity);

        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, targetRequirement, 0, 0, 1, 1, 1, 1,
                featureRange.First, featureCount, governingRuleEdition),
        ], strings, moduleRefCount: 1, typeDefCount: 3, methodDefCount: 0,
            sourceFileCount: 0, featureCount: featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 1,
            3, 0, 0, featureCount);

        var typeSpecRows = new ReadOnlyMemory<byte>[] {
            TrRmTypeSpecTableWriter.Builtin(0x08),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 2),
        };
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            3, 2, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length, 3, 2, 0);

        var leafFlags = useStructLeaves ? 0x06u : 0x0Au;
        var typeDefRows = new[] {
            new TrRmTypeDefRow(2, 3, 0x0A, 1, 2, 0, 0, 1, 1, 0, 0),
            new TrRmTypeDefRow(2, 4, leafFlags, 3, 1, 0, 0, 2, 1, 0, 0),
            new TrRmTypeDefRow(2, 4, leafFlags, 4, 1, 0, 0, 3, 1, 0, 0),
        };
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldCount: 4, genericParamCount: 0,
            layoutCount: 3, contractCount: 0, schema: schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 3, strings,
            modules, 4, 0, 3, 0);
        var fieldRows = new[] {
            new TrRmFieldDefRow(1, 5, 2, 2),
            new TrRmFieldDefRow(1, 6, 3, 2),
            new TrRmFieldDefRow(2, 7, 0xF200_0008, 2),
            new TrRmFieldDefRow(3, 7, 0xF200_0008, 2),
        };
        var fieldBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 4, strings,
            typeDefs, typeSpecs);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutRows = new[] {
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 1, 2),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 3, 1),
            new TrRmLayoutRow(3, 0, 0, 0, 0, 0, 4, 1),
        };
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
            targetProfiles, layoutFieldCount: 4);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 3, typeDefs,
            targetProfiles, 4);
        var layoutFieldRows = new[] {
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(1, 1, 2),
            new TrRmLayoutFieldRow(2, 0, 3),
            new TrRmLayoutFieldRow(3, 0, 4),
        };
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, typeDefs, fields);
        var moduleRefBytes = new byte[12];
        Write(moduleRefBytes, 0, 8);
        Write(moduleRefBytes, 4, targetRequirement);
        var importBytes = new byte[24];
        Write(importBytes, 0, 1);
        Write(importBytes, 4, 9);
        Write(importBytes, 8, 0x1300_0002);
        Write(importBytes, 12, 1);
        Write(importBytes, 16, 10);
        Write(importBytes, 20, 0x1300_0003);
        var exportBytes = new byte[8];
        Write(exportBytes, 0, 0x1300_0001);
        Write(exportBytes, 4, 11);

        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, checked((uint)names.Length), stringBytes),
            featureSection,
            new(0x0010, 0, 1, moduleRefBytes),
            new(0x0011, 0, checked((uint)typeSpecRows.Length),
                typeSpecBytes),
            new(0x0013, 0, 3, typeDefBytes),
            new(0x0014, 0, 4, fieldBytes),
            new(0x0015, 0, 3, layoutBytes),
            new(0x001C, 0, 2, importBytes),
            new(0x001D, 0, 1, exportBytes),
            new(0x0023, 0, 4, layoutFieldBytes),
        };
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
        string? leafAliasKey = null)
    {
        var tree = SyntaxTree.Parse("""
            public class Prefix {}
            public class Leaf {
                public int32 Value;
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declarations = tree.Root.Children.OfType<SyntaxNode>()
            .Where(static node =>
                node.Kind == SyntaxKind.ClassDeclaration).ToArray();
        var prefix = declarations.Single(static declaration =>
            declaration.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Prefix"));
        var leaf = declarations.Single(static declaration =>
            declaration.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Leaf"));
        var leafField = leaf.Children.OfType<SyntaxNode>().Single(
            static node => node.Kind == SyntaxKind.FieldDeclaration);
        var prefixIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Prefix");
        var leafIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Leaf");
        var classExports = new List<TrRmSourceClassExport> {
            new TrRmSourceClassExport(program, prefixIndex,
                prefix.Span, "PrefixKey"),
            new TrRmSourceClassExport(program, leafIndex, leaf.Span,
                "LeafKey", [
                    new TrRmSourceClassMemberExport(leafIndex,
                        leafField.Span, "Leaf.Value"),
                ]),
        };
        if (leafAliasKey is not null)
        {
            classExports.Add(new TrRmSourceClassExport(program, leafIndex,
                leaf.Span, leafAliasKey));
        }

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
        string? additionalFieldExportKey = null)
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
            ("Value", 1),
        };
        var typeNameRow = 3;
        var fieldNameRow = 4;
        var leafNameRow = 0;
        var localDependencyNameRow = 0;
        var importIdentityRow = 0;
        var importKeyRow = 0;
        if (importIdentity is not null)
        {
            names.Add((importIdentity, 1));
            importIdentityRow = names.Count;
            names.Add(("Leaf", 1));
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
                new TrRmFieldDefRow(2, fieldNameRow,
                    localDependencyFieldTypeSpecReference, 2),
            }
            : new[] {
                new TrRmFieldDefRow(1, fieldNameRow,
                    boxFieldTypeSpecReference, 2),
                new TrRmFieldDefRow(2, fieldNameRow,
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
            checked((uint)typeDefCount), layoutFieldBytes));
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

        var exportCount = 1 + (fieldExportKey is null ? 0 : 1)
            + (additionalFieldExportKey is null ? 0 : 1);
        var exportBytes = new byte[checked(exportCount * 8)];
        Write(exportBytes, 0, 0x1300_0001);
        Write(exportBytes, 4, checked((uint)exportKeyRow));
        var exportOffset = 8;
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
        byte[] artifact, int bodyRow, uint rootTypeSpecRef)
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

        var fieldReference
            = BinaryPrimitives.ReadUInt32LittleEndian(body.FieldMap);
        if ((fieldReference >> 24) != 0x1C)
        {
            throw new InvalidDataException(
                "The FieldPath fixture requires a selected FieldDef Import.");
        }

        var fieldPathBytes = TrRmFieldPathTableWriter.Encode([
            new TrRmFieldPathRow(rootTypeSpecRef, [fieldReference]),
        ], tables.TypeSpecs, tables.Fields, tables.Imports);
        var fieldMap = body.FieldMap.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(fieldMap,
            0x1D00_0001u);

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

        sections.Add(new TrRmSection(0x004B, 0, 1, fieldPathBytes));
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
