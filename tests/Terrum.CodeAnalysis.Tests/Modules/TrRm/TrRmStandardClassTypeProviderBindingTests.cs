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
        Assert.That(tables.Imports.Count, Is.EqualTo(1));
        Assert.That(tables.Imports.GetExpectedDefinitionForImportRow(1),
            Is.EqualTo(0x1300_0002u));
        Assert.That(tables.Exports.GetExportsForModule(1),
            Has.Length.EqualTo(1));

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
    public void EmitsReadonlyBodiesThroughImportedClassHandleParameters()
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
                private int32 _seed;
                public Container(int32 seed) { _seed = seed; }
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
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var read = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Read"));
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

        var outputReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ImportedClassFieldRead", [
                    new TrRmStandardArtifactInput(schemaIdentity, provider),
                ], ["ContainerKey"]);
        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Container value) {}"),
            outputReference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var incompatibleProvider = CreateClassArtifact("ClassProvider",
            "Leaf", "LeafKey", 0xF200_000A, schemaIdentity,
            layoutKind: 0, pack: 0, fieldExportKey: "Leaf.Value");
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ImportedClassFieldRead", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProvider),
                ], ["ContainerKey"]));
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

    private static byte[] CreateClassArtifact(string moduleIdentity,
        string typeName, string exportKey, uint fieldTypeSpecReference,
        string schemaIdentity = C1M1SchemaIdentity,
        byte layoutKind = 1, uint pack = 1,
        string? fieldExportKey = null)
        => CreateArtifact(moduleIdentity, typeName, exportKey,
            fieldTypeSpecReference, schemaIdentity, null, null, null,
            layoutKind, pack, fieldExportKey: fieldExportKey);

    private static byte[] CreateArtifact(string moduleIdentity,
        string typeName, string exportKey, uint fieldTypeSpecReference,
        string schemaIdentity, string? importIdentity, string? importKey,
        byte[]? importedFieldTypeSpec, byte layoutKind = 1, uint pack = 1,
        string? localDependencyTypeName = null,
        bool useRequestedLayoutForImports = false,
        string? fieldExportKey = null)
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
                    fieldTypeSpecReference, 2),
            }
            : importCount == 0
            ? new[] {
                new TrRmFieldDefRow(1, fieldNameRow,
                    boxFieldTypeSpecReference, 2),
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

        var exportCount = fieldExportKey is null ? 1 : 2;
        var exportBytes = new byte[checked(exportCount * 8)];
        Write(exportBytes, 0, 0x1300_0001);
        Write(exportBytes, 4, checked((uint)exportKeyRow));
        if (fieldExportKey is not null)
        {
            Write(exportBytes, 8, 0x1400_0001);
            Write(exportBytes, 12, checked((uint)fieldExportKeyRow));
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

    private static TrRmStructuralMetadataValidator.ValidatedExecutionTables
        ReadTables(byte[] artifact, string schemaIdentity)
    {
        var index = StringComparer.Ordinal.Equals(schemaIdentity,
                C1SchemaIdentity)
            ? TrRmArtifactIndex.ForC1Draft(artifact)
            : TrRmArtifactIndex.ForC1M1Draft(artifact);
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
        var index = StringComparer.Ordinal.Equals(schemaIdentity,
                C1SchemaIdentity)
            ? TrRmArtifactIndex.ForC1Draft(artifact)
            : TrRmArtifactIndex.ForC1M1Draft(artifact);
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
