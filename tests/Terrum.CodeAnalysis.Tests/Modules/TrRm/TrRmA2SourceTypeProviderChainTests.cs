// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmA2SourceTypeProviderChainTests
{
    private const string SchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
    private const string EmptyInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1";
    private const string EmptyUnitInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
    private const string LocalAggregateLayoutSchemaIdentity
        = "trrm/1-draft-local-aggregate-layout-execution-1";
    private static readonly string[] s_pairExportKeys = ["PairKey", "PairAlias"];
    private static readonly string[] s_leafExportKeys = ["LeafKey", "ProviderLeafKey"];

    [Test]
    public void StandardA2EConsumerBindsA2TypeProviders()
    {
        var rootArtifact = CreateRootArtifact();
        var providerArtifact = CreateProviderArtifact();
        var leafArtifact = CreateLeafProviderArtifact();
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                EmptyInitializationSchemaIdentity, rootArtifact),
            "Root",
            [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    providerArtifact),
                new TrRmStandardArtifactInput(SchemaIdentity, leafArtifact),
            ],
            ["ContainerKey"]);

        var pair = reference.Structures.Single(static structure
            => structure.Name == "Pair");
        Assert.That(pair.StandardTypeOrigin!.Provider.ModuleIdentity,
            Is.EqualTo("Provider"));
        Assert.That(pair.StandardTypeOrigin.Expectations.Any(
            static expectation
                => expectation.UsingModule.ModuleIdentity == "Provider"
                    && expectation.ExportKey == "ProviderLeafKey"), Is.True);

        var a2eProviderReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    EmptyInitializationSchemaIdentity, rootArtifact),
                "Root",
                [
                    new TrRmStandardArtifactInput(
                        EmptyInitializationSchemaIdentity, providerArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        leafArtifact),
                ],
                ["ContainerKey"]);
        Assert.That(a2eProviderReference.Structures.Single(
            static structure => structure.Name == "Pair")
            .StandardTypeOrigin!.Provider.ModuleIdentity,
            Is.EqualTo("Provider"));

        var a2euReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                EmptyUnitInitializationSchemaIdentity, rootArtifact),
            "Root",
            [
                new TrRmStandardArtifactInput(
                    EmptyUnitInitializationSchemaIdentity, providerArtifact),
                new TrRmStandardArtifactInput(SchemaIdentity, leafArtifact),
            ],
            ["ContainerKey"]);
        Assert.That(a2euReference.Structures.Single(
            static structure => structure.Name == "Pair")
            .StandardTypeOrigin!.Provider.ModuleIdentity,
            Is.EqualTo("Provider"));
    }

    [Test]
    public void StandardL1ConsumerBindsOnlyL1TypeProviders()
    {
        var schema = TrRmArtifactSchema.LocalAggregateLayoutDraft;
        var rootArtifact = CreateRootArtifact(schema: schema);
        var providerArtifact = CreateProviderArtifact(schema: schema);
        var leafArtifact = CreateLeafProviderArtifact(schema: schema);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(LocalAggregateLayoutSchemaIdentity,
                rootArtifact),
            "Root",
            [
                new TrRmStandardArtifactInput(
                    LocalAggregateLayoutSchemaIdentity, providerArtifact),
                new TrRmStandardArtifactInput(
                    LocalAggregateLayoutSchemaIdentity, leafArtifact),
            ],
            ["ContainerKey"]);

        var sourcePairOrigin = reference.Structures.Single(
            static structure => structure.Name == "Pair")
            .StandardTypeOrigin!;
        Assert.That(sourcePairOrigin.Provider.ModuleIdentity,
            Is.EqualTo("Provider"));
        Assert.That(sourcePairOrigin.Provider.SchemaIdentity,
            Is.EqualTo(LocalAggregateLayoutSchemaIdentity));
        Assert.That(sourcePairOrigin.ReferenceRoot!.SchemaIdentity,
            Is.EqualTo(LocalAggregateLayoutSchemaIdentity));
        Assert.That(sourcePairOrigin.DefinitionId, Is.EqualTo(0x1300_0001u));

        var mixedProviderException = Assert.Throws<NotSupportedException>(
            () => TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    LocalAggregateLayoutSchemaIdentity, rootArtifact),
                "Root",
                [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        providerArtifact),
                    new TrRmStandardArtifactInput(
                        LocalAggregateLayoutSchemaIdentity, leafArtifact),
                ],
                ["ContainerKey"]));
        Assert.That(mixedProviderException!.Message,
            Does.Contain("root and every provider artifact"));

        var sourceConsumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Consumer {
                public Container Value;
                public [Container; 2] Values;
            }
            """), reference, new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(sourceConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, sourceConsumer.Diagnostics));
        var sourceConsumerProgram = sourceConsumer.Program!;
        var wrongSchemaException = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(sourceConsumerProgram,
                "Consumer", SchemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Consumer", "ConsumerKey")]));
        Assert.That(wrongSchemaException!.Message,
            Does.Contain("selected provider schema"));
        var consumerTypeArtifact = TrRmExecutableCompiler.EmitStandard(
            sourceConsumerProgram, "Consumer",
            LocalAggregateLayoutSchemaIdentity, 1,
            [new TrRmSourceTypeExport("", "Consumer", "ConsumerKey")]);
        var consumerTypeReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    LocalAggregateLayoutSchemaIdentity,
                    consumerTypeArtifact),
                "Consumer",
                [
                    new TrRmStandardArtifactInput(
                        LocalAggregateLayoutSchemaIdentity, rootArtifact),
                    new TrRmStandardArtifactInput(
                        LocalAggregateLayoutSchemaIdentity, providerArtifact),
                    new TrRmStandardArtifactInput(
                        LocalAggregateLayoutSchemaIdentity, leafArtifact),
                ],
                ["ConsumerKey"]);
        var reemittedPair = consumerTypeReference.Structures.Single(
            static structure => structure.Name == "Pair");
        Assert.That(reemittedPair.StandardTypeOrigin!.Provider.SchemaIdentity,
            Is.EqualTo(LocalAggregateLayoutSchemaIdentity));
        Assert.That(reemittedPair.StandardTypeOrigin.ReferenceRoot!
                .SchemaIdentity,
            Is.EqualTo(LocalAggregateLayoutSchemaIdentity));
        Assert.That(reemittedPair.StandardTypeOrigin.DefinitionId,
            Is.EqualTo(sourcePairOrigin.DefinitionId));
        Assert.That(reemittedPair.StandardTypeOrigin.Provider
                .ArtifactSnapshot.AsSpan().SequenceEqual(providerArtifact),
            Is.True);
        Assert.That(reemittedPair.StandardTypeOrigin.ExportKeys,
            Is.EquivalentTo(s_pairExportKeys));
        Assert.That(reemittedPair.StandardTypeOrigin.Expectations,
            Has.Length.EqualTo(sourcePairOrigin.Expectations.Length));
        Assert.That(reemittedPair.StandardTypeOrigin.Expectations.Count(
            static expectation
                => expectation.UsingModule.ModuleIdentity == "Consumer"),
            Is.EqualTo(2));
        Assert.That(reemittedPair.StandardTypeOrigin.Expectations.All(
            static expectation
                => expectation.UsingModule.SchemaIdentity
                        == LocalAggregateLayoutSchemaIdentity
                    && expectation.Provider.SchemaIdentity
                        == LocalAggregateLayoutSchemaIdentity), Is.True);
        Assert.That(reemittedPair.StandardTypeOrigin.ExpectedDeclaration!
                .Name,
            Is.EqualTo("Pair"));
        Assert.That(reemittedPair.StandardTypeOrigin.ExpectedFixedArrayTypes
                .IsDefault,
            Is.False);
        Assert.That(reemittedPair.StandardTypeOrigin
                .ExpectedNativePointerTypes.IsDefault,
            Is.False);
        Assert.That(reemittedPair.StandardTypeOrigin.Expectations.Any(
            static expectation
                => expectation.UsingModule.ModuleIdentity == "Provider"
                    && expectation.ExportKey == "ProviderLeafKey"), Is.True);

        var executableConsumer = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Consumer {
                public Container Value;
                public [Container; 2] Values;
            }
            int32 Main() { return int32(0); }
            """), reference, new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(executableConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                executableConsumer.Diagnostics));
        var executableConsumerArtifact
            = TrRmExecutableCompiler.EmitStandard(
                executableConsumer.Program!, "Consumer",
                LocalAggregateLayoutSchemaIdentity, 1);
        var executableConsumerIndex
            = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(
                executableConsumerArtifact);
        var executableConsumerTables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    executableConsumerIndex);
        Assert.That(executableConsumerTables.Imports.Count,
            Is.GreaterThan(0));
        Assert.That(executableConsumerTables.Methods.Count,
            Is.EqualTo(1));
        executableConsumerTables.ValidateRegisterBodyMapsForMethod(1);
    }

    [Test]
    public void StandardA2SourceReferencePreservesProviderOwnedTypeImports()
    {
        var duplicateImportTriple = Assert.Throws<InvalidDataException>(
            () => CreateRootArtifact(duplicatePairAlias: true));
        Assert.That(duplicateImportTriple!.Message,
            Does.Contain("Invalid TRRM Import table"));
        var rootArtifact = CreateRootArtifact();
        var providerArtifact = CreateProviderArtifact();
        var leafArtifact = CreateLeafProviderArtifact();
        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, providerArtifact),
            "Provider",
            [new TrRmStandardArtifactInput(SchemaIdentity, leafArtifact)]);
        var retainedProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            loadedProvider, "Provider", SchemaIdentity, 1);
        var retainedAliasReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
                "Root",
                [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        retainedProviderArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity, leafArtifact),
                ],
                ["ContainerKey"]);
        var retainedPair = retainedAliasReference.Structures.Single(
            static structure => structure.Name == "Pair");
        Assert.That(retainedPair.StandardTypeOrigin!.ExportKeys,
            Is.EquivalentTo(s_pairExportKeys));
        Assert.That(retainedPair.StandardTypeOrigin.Expectations.Any(
            static expectation
                => expectation.UsingModule.ModuleIdentity == "Provider"
                    && expectation.ExportKey == "ProviderLeafKey"), Is.True);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
            "Root",
        [
            new TrRmStandardArtifactInput(SchemaIdentity, providerArtifact),
            new TrRmStandardArtifactInput(SchemaIdentity, leafArtifact),
        ],
        ["ContainerKey"]);

        Assert.That(reference.Structures, Has.Length.EqualTo(3));
        var container = reference.Structures.Single(static structure
            => structure.Name == "Container");
        Assert.That(container.StandardTypeOrigin!.IsLookupRoot, Is.True);

        var pair = reference.Structures.Single(static structure
            => structure.Name == "Pair");
        var pairOrigin = pair.StandardTypeOrigin!;
        Assert.That(pairOrigin.IsLookupRoot, Is.False);
        Assert.That(pairOrigin.Provider.ModuleIdentity, Is.EqualTo("Provider"));
        Assert.That(pairOrigin.DefinitionId, Is.EqualTo(0x1300_0001u));
        Assert.That(pairOrigin.ExportKeys, Has.Length.EqualTo(2));
        Assert.That(pairOrigin.ExportKeys,
            Is.EquivalentTo(s_pairExportKeys));
        Assert.That(pairOrigin.Expectations, Has.Length.EqualTo(3));
        Assert.That(pairOrigin.Expectations.Any(static expectation
            => expectation.UsingModule.ModuleIdentity == "Root"
                && expectation.ImportRow == 1
                && expectation.ExportKey == "PairKey"), Is.True);
        Assert.That(pairOrigin.Expectations.Any(static expectation
            => expectation.UsingModule.ModuleIdentity == "Root"
                && expectation.ImportRow == 2
                && expectation.ExportKey == "PairAlias"), Is.True);
        Assert.That(pairOrigin.Expectations.Any(static expectation
            => expectation.UsingModule.ModuleIdentity == "Provider"
                && expectation.ImportRow == 1
                && expectation.ExportKey == "ProviderLeafKey"), Is.True);
        var splitAliasProviderArtifact = CreateProviderArtifact(
            splitPairAlias: true);
        var splitAliasException = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
                "Root",
                [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        splitAliasProviderArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        leafArtifact),
                ],
                ["ContainerKey"]));
        Assert.That(splitAliasException!.Message,
            Does.Contain("conflicting provider origins"));
        var remappedPair = BoundTreeTraversal.RewriteTypes(pair, static type
            => type == (PrimitiveType)101 ? (PrimitiveType)100 : type);
        Assert.That(remappedPair.Fields[0].Type, Is.EqualTo((PrimitiveType)100));
        Assert.That(remappedPair.StandardTypeOrigin!.Provider,
            Is.SameAs(pairOrigin.Provider));
        Assert.That(remappedPair.StandardTypeOrigin.ReferenceRoot,
            Is.SameAs(pairOrigin.ReferenceRoot));
        Assert.That(remappedPair.StandardTypeOrigin.ExpectedDeclaration!
            .Fields[0].Type, Is.EqualTo((PrimitiveType)100));

        var leaf = reference.Structures.Single(static structure
            => structure.Name == "Leaf");
        var leafOrigin = leaf.StandardTypeOrigin!;
        Assert.That(leafOrigin.Provider.ModuleIdentity,
            Is.EqualTo("LeafProvider"));
        Assert.That(leafOrigin.DefinitionId, Is.EqualTo(0x1300_0001u));
        Assert.That(leafOrigin.Expectations.Single().UsingModule.ModuleIdentity,
            Is.EqualTo("Root"));
        Assert.That(leafOrigin.Expectations.Single().ExportKey,
            Is.EqualTo("LeafKey"));

        var coLocatedProviderArtifact
            = CreateProviderArtifact(coLocateLeafProvider: true);
        var coLocatedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
                "Root",
                [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        coLocatedProviderArtifact),
                ],
                ["ContainerKey"]);
        var coLocatedPair = coLocatedReference.Structures.Single(
            static structure => structure.Name == "Pair");
        var coLocatedLeaf = coLocatedReference.Structures.Single(
            static structure => structure.Name == "Leaf");
        Assert.That(coLocatedPair.StandardTypeOrigin!.Provider.ModuleRow,
            Is.EqualTo(1));
        Assert.That(coLocatedLeaf.StandardTypeOrigin!.Provider.ModuleRow,
            Is.EqualTo(2));
        Assert.That(coLocatedPair.StandardTypeOrigin.Provider.ArtifactSnapshot
            .AsSpan().SequenceEqual(coLocatedProviderArtifact), Is.True);

        var sourceConsumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Consumer {
                public Container Value;
                public [Container; 2] Values;
            }
            """), reference, new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(sourceConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, sourceConsumer.Diagnostics));
        var hiddenDependency = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Consumer {
                public Leaf Value;
            }
            """), reference, new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(hiddenDependency.HasErrors, Is.True);

        var sourceProgram = sourceConsumer.Program!;
        var replacementLeaf = CreateLeafProviderArtifact(pack: 8);
        var importedPairIndex = Enumerable.Range(0,
            sourceProgram.Structures.Length).Single(index
            => sourceProgram.Structures[index].Name == "Pair");
        var importedPair = sourceProgram.Structures[importedPairIndex];
        var alteredPair = importedPair with {
            Fields = importedPair.Fields.SetItem(0,
                importedPair.Fields[0] with { Type = PrimitiveType.Int32 }),
        };
        var alteredProgram = sourceProgram with {
            Structures = sourceProgram.Structures.SetItem(
                importedPairIndex, alteredPair),
        };
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(alteredProgram, "Consumer",
                SchemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Consumer", "ConsumerKey")]));

        var unexportedConsumerArtifact = TrRmExecutableCompiler.EmitStandard(
            sourceProgram, "Consumer", SchemaIdentity, 1);
        var unexportedConsumerProgram = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity,
                unexportedConsumerArtifact),
            "Consumer",
            [
                new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    providerArtifact),
                new TrRmStandardArtifactInput(SchemaIdentity, leafArtifact),
            ]);
        Assert.That(unexportedConsumerProgram.Structures,
            Has.Length.EqualTo(4));
        Assert.That(unexportedConsumerProgram.NativeFixedArrayTypes,
            Has.Length.EqualTo(1));
        var reemittedUnexportedConsumer
            = TrRmExecutableCompiler.EmitStandard(
                unexportedConsumerProgram, "Consumer", SchemaIdentity, 1);
        var reloadedUnexportedConsumer
            = TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    reemittedUnexportedConsumer),
                "Consumer",
                [
                    new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        providerArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity, leafArtifact),
                ]);
        Assert.That(reloadedUnexportedConsumer.Structures,
            Has.Length.EqualTo(4));
        Assert.That(reloadedUnexportedConsumer.NativeFixedArrayTypes,
            Has.Length.EqualTo(1));

        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            sourceProgram, "Consumer", SchemaIdentity, 1,
            [new TrRmSourceTypeExport("", "Consumer", "ConsumerKey")]);
        var consumerReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, consumerArtifact),
            "Consumer",
            [
                new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    providerArtifact),
                new TrRmStandardArtifactInput(SchemaIdentity, leafArtifact),
            ],
            ["ConsumerKey"]);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    consumerArtifact),
                "Consumer",
                [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        rootArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        providerArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        replacementLeaf),
                ],
                ["ConsumerKey"]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    consumerArtifact),
                "Consumer",
                [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        rootArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        splitAliasProviderArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        leafArtifact),
                ],
                ["ConsumerKey"]));
        var relocatedProviderArtifact = CreateProviderArtifact(
            relocatePair: true);
        var relocatedConsumerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    consumerArtifact),
                "Consumer",
                [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        rootArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        relocatedProviderArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        leafArtifact),
                ],
                ["ConsumerKey"]);
        Assert.That(relocatedConsumerReference.Structures.Single(
            static structure => structure.Name == "Pair")
            .StandardTypeOrigin!.DefinitionId, Is.EqualTo(0x1300_0002u));
        var reencodedContainer = consumerReference.Structures.Single(
            static structure => structure.Name == "Container");
        Assert.That(reencodedContainer.StandardTypeOrigin!.IsLookupRoot,
            Is.False);
        Assert.That(reencodedContainer.StandardTypeOrigin.Provider
            .ModuleIdentity, Is.EqualTo("Root"));
        Assert.That(reencodedContainer.StandardTypeOrigin.Expectations.Any(
            static expectation
                => expectation.UsingModule.ModuleIdentity == "Consumer"
                    && expectation.ExportKey == "ContainerKey"), Is.True);
        Assert.That(consumerReference.NativeFixedArrayTypes,
            Has.Length.EqualTo(1));
        Assert.That(consumerReference.NativeFixedArrayTypes[0].ElementCount,
            Is.EqualTo(2UL));
        var reencodedPair = consumerReference.Structures.Single(
            static structure => structure.Name == "Pair");
        Assert.That(reencodedPair.StandardTypeOrigin!.ExportKeys,
            Is.EquivalentTo(s_pairExportKeys));
        Assert.That(reencodedPair.StandardTypeOrigin.Expectations.Count(
            static expectation
                => expectation.UsingModule.ModuleIdentity == "Consumer"),
            Is.EqualTo(2));
        var reencodedLeaf = consumerReference.Structures.Single(
            static structure => structure.Name == "Leaf");
        Assert.That(reencodedLeaf.StandardTypeOrigin!.ExportKeys,
            Is.EquivalentTo(s_leafExportKeys));
        var hiddenReencodedDependency = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Consumer {
                    public Container Value;
                }
                """), consumerReference, new CompilationOptions {
                    LayoutCoreRegistration =
                        LayoutCoreRegistrationTestData.Create(),
                });
        Assert.That(hiddenReencodedDependency.HasErrors, Is.True);

        var secondSourceConsumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Consumer2 {
                    public Consumer Value;
                }
                """), consumerReference, new CompilationOptions {
                    LayoutCoreRegistration =
                        LayoutCoreRegistrationTestData.Create(),
                });
        Assert.That(secondSourceConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                secondSourceConsumer.Diagnostics));
        var secondConsumerArtifact = TrRmExecutableCompiler.EmitStandard(
            secondSourceConsumer.Program!, "Consumer2", SchemaIdentity, 1,
            [new TrRmSourceTypeExport("", "Consumer2", "Consumer2Key")]);
        var secondConsumerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    secondConsumerArtifact),
                "Consumer2",
                [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        consumerArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        rootArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        providerArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        leafArtifact),
                ],
                ["Consumer2Key"]);
        var secondReencodedLeaf = secondConsumerReference.Structures.Single(
            static structure => structure.Name == "Leaf");
        Assert.That(secondReencodedLeaf.StandardTypeOrigin!.ExportKeys,
            Is.EquivalentTo(s_leafExportKeys));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
                "Root",
            [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    providerArtifact),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    replacementLeaf),
            ],
            ["ContainerKey"]));

        var rootSnapshot
            = pairOrigin.Expectations[0].UsingModule.ArtifactSnapshot;
        var providerSnapshot = pairOrigin.Provider.ArtifactSnapshot;
        var leafSnapshot = leafOrigin.Provider.ArtifactSnapshot;
        rootArtifact[0] = 0;
        providerArtifact[0] = 0;
        leafArtifact[0] = 0;
        Assert.That(pairOrigin.Expectations[0].UsingModule.ArtifactSnapshot
            .AsSpan().SequenceEqual(rootSnapshot.AsSpan()), Is.True);
        Assert.That(pairOrigin.Provider.ArtifactSnapshot.AsSpan()
            .SequenceEqual(providerSnapshot.AsSpan()), Is.True);
        Assert.That(leafOrigin.Provider.ArtifactSnapshot.AsSpan()
            .SequenceEqual(leafSnapshot.AsSpan()), Is.True);
    }

    [Test]
    public void StandardA2SourceReencodingPreservesDistinctExpectedViews()
    {
        var rootArtifact = CreateRootArtifact(multiplePairViews: true);
        var providerArtifact = CreateProviderArtifact();
        var leafArtifact = CreateLeafProviderArtifact();
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
            "Root",
            [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    providerArtifact),
                new TrRmStandardArtifactInput(SchemaIdentity, leafArtifact),
            ],
            ["ContainerKey"]);
        var pairs = reference.Structures.Where(static structure
                => structure.Name == "Pair")
            .ToArray();
        Assert.That(pairs, Has.Length.EqualTo(2));
        Assert.That(pairs.Select(static structure
                => structure.StandardTypeOrigin!.DefinitionId)
            .Distinct().ToArray(), Has.Length.EqualTo(1));
        Assert.That(pairs.Any(static structure
            => structure.StandardTypeOrigin!.Expectations.Any(
                static expectation => expectation.ImportRow == 1
                    && expectation.ExportKey == "PairKey")), Is.True);
        Assert.That(pairs.Any(static structure
            => structure.StandardTypeOrigin!.Expectations.Any(
                static expectation => expectation.ImportRow == 2
                    && expectation.ExportKey == "PairAlias")), Is.True);

        var sourceConsumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Consumer {
                public Container Value;
            }
            """), reference, new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(sourceConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, sourceConsumer.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            sourceConsumer.Program!, "Consumer", SchemaIdentity, 1,
            [new TrRmSourceTypeExport("", "Consumer", "ConsumerKey")]);
        var consumerReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, consumerArtifact),
            "Consumer",
            [
                new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    providerArtifact),
                new TrRmStandardArtifactInput(SchemaIdentity, leafArtifact),
            ],
            ["ConsumerKey"]);
        var reencodedPairs = consumerReference.Structures.Where(
                static structure => structure.Name == "Pair")
            .ToArray();
        Assert.That(reencodedPairs, Has.Length.EqualTo(2));
        Assert.That(reencodedPairs.All(static structure
            => structure.StandardTypeOrigin!.ExportKeys.ToHashSet(
                StringComparer.Ordinal).SetEquals(
                s_pairExportKeys)), Is.True);
        Assert.That(reencodedPairs.SelectMany(static structure
                => structure.StandardTypeOrigin!.Expectations)
            .Where(static expectation
                => expectation.UsingModule.ModuleIdentity == "Consumer")
            .Select(static expectation => expectation.ExpectedDefinitionId)
            .Distinct().ToArray(), Has.Length.EqualTo(2));

        var splitAliasProvider = CreateProviderArtifact(
            splitPairAlias: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    consumerArtifact),
                "Consumer",
                [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        rootArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        splitAliasProvider),
                    new TrRmStandardArtifactInput(SchemaIdentity, leafArtifact),
                ],
                ["ConsumerKey"]));
    }

    [Test]
    public void StandardA2SourceReferenceRebindsPermutedTypeAndArrayRows()
    {
        static byte[] CreateProvider(bool reordered)
        {
            var source = reordered
                ? """
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct Element {
                        public int32 Value;
                        public int32 Size;
                    }
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct ContainerB {
                        public [Element; 3] Values;
                    }
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct ContainerA {
                        public [Element; 2] Values;
                    }
                    """
                : """
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct Element {
                        public int32 Value;
                        public int32 Size;
                    }
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct ContainerA {
                        public [Element; 2] Values;
                    }
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct ContainerB {
                        public [Element; 3] Values;
                    }
                    """;
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(source), new CompilationOptions {
                    LayoutCoreRegistration =
                        LayoutCoreRegistrationTestData.Create(),
                });
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            return TrRmExecutableCompiler.EmitStandard(
                compilation.Program!, "Provider", SchemaIdentity, 1,
                [
                    new TrRmSourceTypeExport("", "Element", "ElementKey"),
                    new TrRmSourceTypeExport("", "ContainerA",
                        "ContainerAKey"),
                    new TrRmSourceTypeExport("", "ContainerB",
                        "ContainerBKey"),
                ]);
        }

        static (int TypeDefRow, int FieldDefRow, int TypeSpecRow,
            int ElementTypeSpecRow, int LayoutRow, int FixedArrayLayoutRow)
            GetContainerARows(byte[] artifact)
        {
            var tables = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyDraft(
                            artifact));
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "ContainerAKey", out var definition), Is.True);
            var typeDefRow = checked((int)(definition & 0x00FF_FFFF));
            var (fieldDefRow, _) = tables.TypeDefs.GetFieldRange(typeDefRow);
            var layoutRow = tables.Layouts.GetLayoutForProfile(typeDefRow, 1);
            var typeSpecRef = tables.Fields.GetTypeSpecRef(fieldDefRow);
            var typeSpecRow = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef).Row;
            Assert.That(tables.TypeSpecs.TryGetFixedInlineArray(typeSpecRef,
                out var elementTypeSpecRef, out _), Is.True);
            var elementTypeSpecRow
                = tables.TypeSpecs.GetTypeSpecRef(elementTypeSpecRef).Row;
            var fixedArrayLayoutRow = tables.FixedArrayLayouts.FindRow(1,
                typeSpecRef, 1);
            return (typeDefRow, fieldDefRow, typeSpecRow,
                elementTypeSpecRow, layoutRow, fixedArrayLayoutRow);
        }

        static int FindStringRow(TrRmStringTableReader strings, string value)
        {
            for (var row = 1; row <= strings.Count; row++)
            {
                if (StringComparer.Ordinal.Equals(
                        strings.DecodeNamespace(row), value))
                {
                    return row;
                }
            }

            throw new InvalidDataException(
                "The source fixture is missing a required String row.");
        }

        static byte[] ReplaceSections(byte[] artifact,
            params (ushort Kind, byte[] Bytes)[] replacements)
        {
            var replacementBytesByKind
                = new Dictionary<ushort, byte[]>(replacements.Length);
            foreach (var (kind, bytes) in replacements)
            {
                replacementBytesByKind.Add(kind, bytes);
            }

            var artifactIndex
                = TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
            var sections = new List<TrRmSection>(
                artifactIndex.SectionCount);
            for (var sectionIndex = 0;
                sectionIndex < artifactIndex.SectionCount; sectionIndex++)
            {
                artifactIndex.GetSection(sectionIndex, out var kind,
                    out var flags, out var section, out var recordCount);
                var bytes = replacementBytesByKind.TryGetValue(kind,
                    out var replacementBytes)
                    ? replacementBytes : section.ToArray();
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), bytes));
            }

            return TrRmArtifactWriter.Encode(sections,
                TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyDraft);
        }

        static byte[] PermuteFieldDefOwnerBlocks(byte[] artifact)
        {
            var artifactIndex
                = TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
            var tables = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    artifactIndex);
            var fieldOrder = new List<int>(tables.Fields.Count);
            for (var typeDefRow = tables.TypeDefs.Count;
                typeDefRow > 0; typeDefRow--)
            {
                var (first, end)
                    = tables.TypeDefs.GetFieldRange(typeDefRow);
                for (var fieldRow = first; fieldRow < end; fieldRow++)
                {
                    fieldOrder.Add(fieldRow);
                }
            }

            if (fieldOrder.Count != tables.Fields.Count)
            {
                throw new InvalidDataException(
                    "The source fixture has incomplete FieldDef ownership.");
            }

            var newFieldRowByOld = new int[tables.Fields.Count + 1];
            var fieldRows = new TrRmFieldDefRow[fieldOrder.Count];
            for (var index = 0; index < fieldOrder.Count; index++)
            {
                var oldFieldRow = fieldOrder[index];
                var newFieldRow = index + 1;
                newFieldRowByOld[oldFieldRow] = newFieldRow;
                fieldRows[index] = new TrRmFieldDefRow(
                    tables.Fields.GetOwnerTypeDef(oldFieldRow),
                    FindStringRow(tables.Strings,
                        tables.Fields.GetName(oldFieldRow)),
                    tables.Fields.GetTypeSpecRef(oldFieldRow),
                    tables.Fields.GetFlags(oldFieldRow));
            }

            var typeDefRows = new TrRmTypeDefRow[tables.TypeDefs.Count];
            for (var typeDefRow = 1;
                typeDefRow <= tables.TypeDefs.Count; typeDefRow++)
            {
                var (oldFirst, oldEnd)
                    = tables.TypeDefs.GetFieldRange(typeDefRow);
                var (genericFirst, genericCount)
                    = tables.TypeDefs.GetGenericRange(typeDefRow);
                var (layoutFirst, layoutCount)
                    = tables.TypeDefs.GetLayoutRange(typeDefRow);
                var (contractFirst, contractCount)
                    = tables.TypeDefs.GetContractRange(typeDefRow);
                typeDefRows[typeDefRow - 1] = new TrRmTypeDefRow(
                    FindStringRow(tables.Strings,
                        tables.TypeDefs.GetNamespace(typeDefRow)),
                    FindStringRow(tables.Strings,
                        tables.TypeDefs.GetName(typeDefRow)),
                    tables.TypeDefs.GetFlags(typeDefRow),
                    oldFirst == 0 ? 0 : newFieldRowByOld[oldFirst],
                    oldEnd - oldFirst, genericFirst, genericCount,
                    layoutFirst, layoutCount, contractFirst, contractCount);
            }

            var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
                tables.Strings, tables.Modules, fieldRows.Length, 0,
                tables.Layouts.Count, tables.TypeDefs.ContractCount,
                TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyDraft);
            var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
                typeDefRows.Length, tables.Strings, tables.Modules,
                fieldRows.Length, 0, tables.Layouts.Count,
                tables.TypeDefs.ContractCount,
                TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyDraft);
            var fieldBytes = TrRmFieldDefTableWriter.Encode(fieldRows,
                tables.Strings, typeDefs, tables.TypeSpecs);
            var fields = new TrRmFieldDefTableReader(fieldBytes,
                fieldRows.Length, tables.Strings, typeDefs, tables.TypeSpecs);
            var layoutFieldRows = new TrRmLayoutFieldRow[
                tables.LayoutFields.Count];
            for (var row = 1; row <= layoutFieldRows.Length; row++)
            {
                var fieldDefRow = tables.LayoutFields.GetFieldDef(row);
                layoutFieldRows[row - 1] = new TrRmLayoutFieldRow(
                    tables.LayoutFields.GetOwnerLayout(row),
                    tables.LayoutFields.GetOrdinal(row),
                    newFieldRowByOld[fieldDefRow]);
            }

            var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
                layoutFieldRows, tables.Layouts, typeDefs, fields);
            return ReplaceSections(artifact,
                (0x0013, typeDefBytes), (0x0014, fieldBytes),
                (0x0023, layoutFieldBytes));
        }

        static byte[] ReverseLayoutVariants(byte[] artifact)
        {
            var artifactIndex
                = TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
            var tables = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    artifactIndex);
            var oldLayoutByNew = new int[tables.Layouts.Count + 1];
            for (var typeDefRow = 1;
                typeDefRow <= tables.TypeDefs.Count; typeDefRow++)
            {
                var (first, count)
                    = tables.TypeDefs.GetLayoutRange(typeDefRow);
                for (var offset = 0; offset < count; offset++)
                {
                    oldLayoutByNew[first + count - offset - 1] = first + offset;
                }
            }

            var layoutRows = new TrRmLayoutRow[tables.Layouts.Count];
            var layoutFieldRows = new List<TrRmLayoutFieldRow>(
                tables.LayoutFields.Count);
            for (var newLayoutRow = 1;
                newLayoutRow <= layoutRows.Length; newLayoutRow++)
            {
                var oldLayoutRow = oldLayoutByNew[newLayoutRow];
                var (oldFieldFirst, fieldCount)
                    = tables.Layouts.GetLayoutFieldRange(oldLayoutRow);
                var newFieldFirst = layoutFieldRows.Count + 1;
                for (var ordinal = 0; ordinal < fieldCount; ordinal++)
                {
                    var fieldDefRow
                        = tables.LayoutFields.GetFieldDef(
                            oldFieldFirst + ordinal);
                    layoutFieldRows.Add(new TrRmLayoutFieldRow(
                        newLayoutRow, ordinal, fieldDefRow));
                }

                layoutRows[newLayoutRow - 1] = new TrRmLayoutRow(
                    tables.Layouts.GetOwnerTypeDef(oldLayoutRow),
                    tables.Layouts.GetTargetProfileRef(oldLayoutRow),
                    tables.Layouts.GetLayoutKind(oldLayoutRow),
                    tables.Layouts.GetFlags(oldLayoutRow),
                    tables.Layouts.GetAlignment(oldLayoutRow),
                    tables.Layouts.GetPack(oldLayoutRow), newFieldFirst,
                    fieldCount);
            }

            var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows,
                tables.TypeDefs, tables.TargetProfiles, layoutFieldRows.Count);
            var layouts = new TrRmLayoutTableReader(layoutBytes,
                layoutRows.Length, tables.TypeDefs, tables.TargetProfiles,
                layoutFieldRows.Count);
            var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
                layoutFieldRows, layouts, tables.TypeDefs, tables.Fields);
            return ReplaceSections(artifact, (0x0015, layoutBytes),
                (0x0023, layoutFieldBytes));
        }

        static uint RemapTypeSpecRef(TrRmTypeSpecTableReader typeSpecs,
            int[] newRowByOld, uint typeSpecRef)
        {
            var reference = typeSpecs.GetTypeSpecRef(typeSpecRef);
            return reference.IsRow
                ? checked((uint)newRowByOld[reference.Row])
                : typeSpecRef;
        }

        static byte[] ReverseTypeSpecRows(byte[] artifact)
        {
            var artifactIndex
                = TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
            var tables = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    artifactIndex);
            var typeSpecCount = tables.TypeSpecs.Count;
            var newRowByOld = new int[typeSpecCount + 1];
            var oldRowByNew = new int[typeSpecCount + 1];
            var leafRows = new List<int>(typeSpecCount);
            var dependentRows = new List<int>(typeSpecCount);
            for (var oldRow = 1; oldRow <= typeSpecCount; oldRow++)
            {
                var form = tables.TypeSpecs.GetRow(oldRow)[0];
                if (form is 0x01 or 0x02 or 0x03 or 0x04 or 0x0D)
                {
                    leafRows.Add(oldRow);
                }
                else
                {
                    dependentRows.Add(oldRow);
                }
            }

            leafRows.Reverse();
            var newRow = 0;
            foreach (var oldRow in leafRows)
            {
                newRow++;
                newRowByOld[oldRow] = newRow;
                oldRowByNew[newRow] = oldRow;
            }
            foreach (var oldRow in dependentRows)
            {
                newRow++;
                newRowByOld[oldRow] = newRow;
                oldRowByNew[newRow] = oldRow;
            }

            var typeSpecRows = new ReadOnlyMemory<byte>[typeSpecCount];
            for (var newTypeSpecRow = 1;
                newTypeSpecRow <= typeSpecCount; newTypeSpecRow++)
            {
                var oldRow = oldRowByNew[newTypeSpecRow];
                var row = tables.TypeSpecs.GetRow(oldRow);
                if (tables.TypeSpecs.TryGetFixedInlineArray((uint)oldRow,
                        out var elementTypeSpecRef, out var elementCount))
                {
                    typeSpecRows[newTypeSpecRow - 1]
                        = TrRmTypeSpecTableWriter.FixedInlineArray(
                            RemapTypeSpecRef(tables.TypeSpecs, newRowByOld,
                                elementTypeSpecRef), elementCount);
                }
                else if (row[0] is >= 0x06 and <= 0x0B)
                {
                    var innerTypeSpecRef
                        = BinaryPrimitives.ReadUInt32LittleEndian(
                            row.Slice(2, sizeof(uint)));
                    typeSpecRows[newTypeSpecRow - 1]
                        = TrRmTypeSpecTableWriter.Wrapped(row[0],
                            RemapTypeSpecRef(tables.TypeSpecs, newRowByOld,
                                innerTypeSpecRef), row[1]);
                }
                else if (row[0] == 0x0C)
                {
                    var argumentTypeSpecRef
                        = BinaryPrimitives.ReadUInt32LittleEndian(
                            row.Slice(3, sizeof(uint)));
                    typeSpecRows[newTypeSpecRow - 1]
                        = TrRmTypeSpecTableWriter.IndexedFamily(row[2],
                            RemapTypeSpecRef(tables.TypeSpecs, newRowByOld,
                                argumentTypeSpecRef), row[1]);
                }
                else if (row[0] == 0x05)
                {
                    throw new NotSupportedException(
                        "This A2 row-permutation fixture does not admit constructed TypeSpecs.");
                }
                else
                {
                    typeSpecRows[newTypeSpecRow - 1] = row.ToArray();
                }
            }

            var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
                tables.TypeDefs.Count, tables.Imports.Count, 0, 0,
                TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyDraft);
            var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
                typeSpecRows.Length, tables.TypeDefs.Count,
                tables.Imports.Count, 0, 0, TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyDraft);
            var fieldRows = new TrRmFieldDefRow[tables.Fields.Count];
            for (var fieldRow = 1; fieldRow <= fieldRows.Length; fieldRow++)
            {
                fieldRows[fieldRow - 1] = new TrRmFieldDefRow(
                    tables.Fields.GetOwnerTypeDef(fieldRow),
                    FindStringRow(tables.Strings,
                        tables.Fields.GetName(fieldRow)),
                    RemapTypeSpecRef(tables.TypeSpecs, newRowByOld,
                        tables.Fields.GetTypeSpecRef(fieldRow)),
                    tables.Fields.GetFlags(fieldRow));
            }

            var fieldBytes = TrRmFieldDefTableWriter.Encode(fieldRows,
                tables.Strings, tables.TypeDefs, typeSpecs);
            var fixedArrayRows = new TrRmFixedArrayLayoutRow[
                tables.FixedArrayLayouts.Count];
            for (var row = 1; row <= fixedArrayRows.Length; row++)
            {
                var layout = tables.FixedArrayLayouts.GetRow(row);
                fixedArrayRows[row - 1] = layout with {
                    ArrayTypeSpecRow
                        = newRowByOld[layout.ArrayTypeSpecRow],
                };
            }

            var fixedArrayBytes
                = TrRmFixedArrayLayoutTableWriter.Encode(fixedArrayRows,
                    typeSpecs, tables.TargetProfiles, tables.Features,
                    tables.Modules, TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayCopyDraft);
            return ReplaceSections(artifact, (0x0011, typeSpecBytes),
                (0x0012, fixedArrayBytes), (0x0014, fieldBytes));
        }

        static byte[] ReverseFixedArrayLayoutRows(byte[] artifact)
        {
            var artifactIndex
                = TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
            var tables = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    artifactIndex);
            var reversedRows
                = new TrRmFixedArrayLayoutRow[
                    tables.FixedArrayLayouts.Count];
            for (var index = 0; index < reversedRows.Length; index++)
            {
                reversedRows[index] = tables.FixedArrayLayouts.GetRow(
                    reversedRows.Length - index);
            }

            var fixedArrayLayoutBytes
                = TrRmFixedArrayLayoutTableWriter.Encode(reversedRows,
                    tables.TypeSpecs, tables.TargetProfiles, tables.Features,
                    tables.Modules, TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayCopyDraft);
            return ReplaceSections(artifact, (0x0012, fixedArrayLayoutBytes));
        }

        var originalProvider = CreateProvider(reordered: false);
        var replacementProvider = CreateProvider(reordered: true);
        var fieldPermutedProvider
            = PermuteFieldDefOwnerBlocks(originalProvider);
        var layoutPermutedProvider = ReverseLayoutVariants(originalProvider);
        var typeSpecPermutedProvider = ReverseTypeSpecRows(originalProvider);
        var arrayPermutedProvider
            = ReverseFixedArrayLayoutRows(originalProvider);
        var (TypeDefRow, FieldDefRow, TypeSpecRow, ElementTypeSpecRow, LayoutRow, FixedArrayLayoutRow) = GetContainerARows(originalProvider);
        var replacementRows = GetContainerARows(replacementProvider);
        var fieldPermutedRows = GetContainerARows(fieldPermutedProvider);
        var layoutPermutedRows = GetContainerARows(layoutPermutedProvider);
        var typeSpecPermutedRows = GetContainerARows(
            typeSpecPermutedProvider);
        var arrayPermutedRows = GetContainerARows(arrayPermutedProvider);
        Assert.That(replacementRows.TypeDefRow,
            Is.Not.EqualTo(TypeDefRow));
        Assert.That(replacementRows.FieldDefRow,
            Is.Not.EqualTo(FieldDefRow));
        Assert.That(replacementRows.TypeSpecRow,
            Is.Not.EqualTo(TypeSpecRow));
        Assert.That(replacementRows.ElementTypeSpecRow,
            Is.EqualTo(ElementTypeSpecRow));
        Assert.That(replacementRows.LayoutRow,
            Is.Not.EqualTo(LayoutRow));
        Assert.That(replacementRows.FixedArrayLayoutRow,
            Is.Not.EqualTo(FixedArrayLayoutRow));
        Assert.That(fieldPermutedRows.TypeDefRow,
            Is.EqualTo(TypeDefRow));
        Assert.That(fieldPermutedRows.FieldDefRow,
            Is.Not.EqualTo(FieldDefRow));
        Assert.That(fieldPermutedRows.TypeSpecRow,
            Is.EqualTo(TypeSpecRow));
        Assert.That(fieldPermutedRows.ElementTypeSpecRow,
            Is.EqualTo(ElementTypeSpecRow));
        Assert.That(fieldPermutedRows.LayoutRow,
            Is.EqualTo(LayoutRow));
        Assert.That(fieldPermutedRows.FixedArrayLayoutRow,
            Is.EqualTo(FixedArrayLayoutRow));
        Assert.That(layoutPermutedRows.TypeDefRow,
            Is.EqualTo(TypeDefRow));
        Assert.That(layoutPermutedRows.FieldDefRow,
            Is.EqualTo(FieldDefRow));
        Assert.That(layoutPermutedRows.TypeSpecRow,
            Is.EqualTo(TypeSpecRow));
        Assert.That(layoutPermutedRows.ElementTypeSpecRow,
            Is.EqualTo(ElementTypeSpecRow));
        Assert.That(layoutPermutedRows.LayoutRow,
            Is.Not.EqualTo(LayoutRow));
        Assert.That(layoutPermutedRows.FixedArrayLayoutRow,
            Is.EqualTo(FixedArrayLayoutRow));
        Assert.That(typeSpecPermutedRows.TypeDefRow,
            Is.EqualTo(TypeDefRow));
        Assert.That(typeSpecPermutedRows.FieldDefRow,
            Is.EqualTo(FieldDefRow));
        Assert.That(typeSpecPermutedRows.TypeSpecRow,
            Is.EqualTo(TypeSpecRow));
        Assert.That(typeSpecPermutedRows.ElementTypeSpecRow,
            Is.Not.EqualTo(ElementTypeSpecRow));
        Assert.That(typeSpecPermutedRows.LayoutRow,
            Is.EqualTo(LayoutRow));
        Assert.That(typeSpecPermutedRows.FixedArrayLayoutRow,
            Is.EqualTo(FixedArrayLayoutRow));
        Assert.That(arrayPermutedRows.TypeDefRow,
            Is.EqualTo(TypeDefRow));
        Assert.That(arrayPermutedRows.FieldDefRow,
            Is.EqualTo(FieldDefRow));
        Assert.That(arrayPermutedRows.TypeSpecRow,
            Is.EqualTo(TypeSpecRow));
        Assert.That(arrayPermutedRows.ElementTypeSpecRow,
            Is.EqualTo(ElementTypeSpecRow));
        Assert.That(arrayPermutedRows.LayoutRow,
            Is.EqualTo(LayoutRow));
        Assert.That(arrayPermutedRows.FixedArrayLayoutRow,
            Is.Not.EqualTo(FixedArrayLayoutRow));

        var coLocatedProvider = CreateProviderArtifact(
            coLocateLeafProvider: true);
        var coLocatedPermutedProvider = ReverseLayoutVariants(
            PermuteFieldDefOwnerBlocks(ReverseTypeSpecRows(
                coLocatedProvider)));
        var coLocatedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateRootArtifact()),
                "Root",
                [new TrRmStandardArtifactInput(SchemaIdentity,
                    coLocatedPermutedProvider)],
                ["ContainerKey"]);
        Assert.That(coLocatedReference.Structures.Single(
            static structure => structure.Name == "Pair")
            .StandardTypeOrigin!.Provider.ModuleIdentity,
            Is.EqualTo("Provider"));
        Assert.That(coLocatedReference.Structures.Single(
            static structure => structure.Name == "Leaf")
            .StandardTypeOrigin!.Provider.ModuleIdentity,
            Is.EqualTo("LeafProvider"));

        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, originalProvider),
            "Provider", [], ["ContainerAKey"]);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Consumer {
                public ContainerA Value;
                public [ContainerA; 2] Values;
            }
            """), providerReference, new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumer.Program!, "Consumer", SchemaIdentity, 1,
            [new TrRmSourceTypeExport("", "Consumer", "ConsumerKey")]);

        var originalReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    consumerArtifact),
                "Consumer",
                [new TrRmStandardArtifactInput(SchemaIdentity,
                    originalProvider)],
                ["ConsumerKey"]);
        var replacementReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    consumerArtifact),
                "Consumer",
                [new TrRmStandardArtifactInput(SchemaIdentity,
                    replacementProvider)],
                ["ConsumerKey"]);
        Assert.That(originalReference.Structures.Any(static structure
            => structure.Name == "ContainerA"), Is.True);
        foreach (var providerArtifact in new[] {
            fieldPermutedProvider,
            layoutPermutedProvider,
            typeSpecPermutedProvider,
            arrayPermutedProvider,
        })
        {
            var permutedReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        consumerArtifact),
                    "Consumer",
                    [new TrRmStandardArtifactInput(SchemaIdentity,
                        providerArtifact)],
                    ["ConsumerKey"]);
            Assert.That(permutedReference.Structures.Any(static structure
                => structure.Name == "ContainerA"), Is.True);
            var loadedPermutedConsumer
                = TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        consumerArtifact),
                    "Consumer",
                    [new TrRmStandardArtifactInput(SchemaIdentity,
                        providerArtifact)]);
            Assert.That(NativeFixedArrayContract.MatchesProgram(
                loadedPermutedConsumer), Is.True);
            var reemittedPermutedConsumer
                = TrRmExecutableCompiler.EmitStandard(
                    loadedPermutedConsumer, "Consumer", SchemaIdentity, 1);
            var reemittedPermutedReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        reemittedPermutedConsumer),
                    "Consumer",
                    [new TrRmStandardArtifactInput(SchemaIdentity,
                        providerArtifact)],
                    ["ConsumerKey"]);
            Assert.That(reemittedPermutedReference.NativeFixedArrayTypes,
                Is.EqualTo(loadedPermutedConsumer.NativeFixedArrayTypes));
        }
        Assert.That(replacementReference.Structures.Single(
            static structure => structure.Name == "ContainerA")
            .StandardTypeOrigin!.DefinitionId,
            Is.EqualTo(0x1300_0000u
                | checked((uint)replacementRows.TypeDefRow)));
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, consumerArtifact),
            "Consumer",
            [new TrRmStandardArtifactInput(SchemaIdentity,
                replacementProvider)]);
        Assert.That(NativeFixedArrayContract.MatchesProgram(loadedConsumer),
            Is.True);
        var reemittedConsumer = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, "Consumer", SchemaIdentity, 1);
        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    reemittedConsumer),
                "Consumer",
                [new TrRmStandardArtifactInput(SchemaIdentity,
                    replacementProvider)],
                ["ConsumerKey"]);
        Assert.That(reemittedReference.NativeFixedArrayTypes,
            Is.EqualTo(loadedConsumer.NativeFixedArrayTypes));
    }

    private static byte[] CreateRootArtifact(bool duplicatePairAlias = false,
        bool multiplePairViews = false,
        TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Root", 0), ("Provider", 0), ("LeafProvider", 0),
            ("Pair", 0), ("Leaf", 0), ("Value", 0), ("PairKey", 0),
            ("LeafKey", 0), ("Container", 0), ("Child", 0), ("Data", 0),
            ("ContainerKey", 0), ("PairAlias", 0), ("Left", 0),
            ("Right", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 16);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
        ], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 1,
            schema);
        var typeDefCount = multiplePairViews ? 4 : 3;
        var fieldCount = multiplePairViews ? 5 : 3;
        var layoutCount = typeDefCount * 2;
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 2, typeDefCount, 0, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            2, typeDefCount, 0, 0, 1);
        var moduleRefsBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(3, 1, 0),
            new TrRmModuleRefRow(4, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefsBytes, 2,
            strings, modules);
        var typeSpecRows = new List<ReadOnlyMemory<byte>> {
            TrRmTypeSpecTableWriter.Builtin(0x0D),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 3),
        };
        if (multiplePairViews)
        {
            typeSpecRows.Add(
                TrRmTypeSpecTableWriter.Reference(0x03, 2));
        }
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount, 3, 0, 0,
            schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes,
            typeSpecRows.Count, typeDefCount, 3, 0, 0,
            schema);
        var typeDefRows = multiplePairViews
            ? new[] {
                new TrRmTypeDefRow(1, 5, 0x26, 1, 1, 0, 0, 1, 2, 0, 0),
                new TrRmTypeDefRow(1, 5, 0x26, 2, 1, 0, 0, 3, 2, 0, 0),
                new TrRmTypeDefRow(1, 6, 0x26, 3, 1, 0, 0, 5, 2, 0, 0),
                new TrRmTypeDefRow(1, 10, 0x26, 4, 2, 0, 0, 7, 2, 0, 0),
            }
            : [
                new TrRmTypeDefRow(1, 5, 0x26, 1, 1, 0, 0, 1, 2, 0, 0),
                new TrRmTypeDefRow(1, 6, 0x26, 2, 1, 0, 0, 3, 2, 0, 0),
                new TrRmTypeDefRow(1, 10, 0x26, 3, 1, 0, 0, 5, 2, 0, 0),
            ];
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldCount, 0, layoutCount, 0,
            schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes,
            typeDefCount, strings, modules, fieldCount, 0, layoutCount, 0,
            schema);
        var fieldRows = multiplePairViews
            ? new[] {
                new TrRmFieldDefRow(1, 11, 3, 0),
                new TrRmFieldDefRow(2, 11, 3, 0),
                new TrRmFieldDefRow(3, 12, 1, 0),
                new TrRmFieldDefRow(4, 15, 2, 0),
                new TrRmFieldDefRow(4, 16, 4, 0),
            }
            : [
                new TrRmFieldDefRow(1, 11, 3, 0),
                new TrRmFieldDefRow(2, 12, 1, 0),
                new TrRmFieldDefRow(3, 11, 2, 0),
            ];
        var fieldsBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, fieldCount, strings,
            typeDefs, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var (layoutBytes, layoutFieldBytes)
            = CreateSingleFieldLayouts(typeDefs, fields, targetProfiles,
                typeDefCount);
        var layoutFieldCount = multiplePairViews ? 10 : 6;
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutCount,
            typeDefs, targetProfiles, layoutFieldCount);

        _ = new TrRmLayoutFieldTableReader(layoutFieldBytes,
            layoutFieldCount,
            layouts, typeDefs, fields);
        var secondPairExpected = multiplePairViews
            ? 0x1300_0002u : 0x1300_0001u;
        var leafExpected = 0x1300_0000u
            | checked((uint)(multiplePairViews ? 3 : 2));
        var importsBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 8, 0x1300_0001),
            new TrRmImportRow(1, duplicatePairAlias ? 8 : 14,
                secondPairExpected),
            new TrRmImportRow(2, 9, leafExpected),
        ], strings, moduleRefs, typeDefs, default, default, default, default,
            fields, features, modules,
            schema);
        var imports = new TrRmImportTableReader(importsBytes, 3, strings,
            moduleRefs, typeDefs, default, default, default, default, fields,
            features, modules, schema);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x1300_0000u | (uint)typeDefCount, 13),
        ], strings, typeDefs, default, default, imports);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 16, stringsBytes),
            new TrRmSection(0x0004, 0, 1, featuresBytes),
            new TrRmSection(0x0010, 0, 2, moduleRefsBytes),
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Count,
                typeSpecsBytes),
            new TrRmSection(0x0013, 0, (uint)typeDefCount, typeDefsBytes),
            new TrRmSection(0x0014, 0, (uint)fieldCount, fieldsBytes),
            new TrRmSection(0x0015, 0, (uint)layoutCount, layoutBytes),
            new TrRmSection(0x001C, 0, 3, importsBytes),
            new TrRmSection(0x001D, 0, 1, exportsBytes),
            new TrRmSection(0x0023, 0, (uint)layoutFieldCount,
                layoutFieldBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], schema);
    }

    private static byte[] CreateProviderArtifact(bool coLocateLeafProvider = false,
        bool splitPairAlias = false, bool relocatePair = false,
        TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Provider", 0), ("LeafProvider", 0), ("Pair", 0),
            ("Leaf", 0), ("Child", 0), ("Data", 0), ("LeafKey", 0),
            ("PairKey", 0), ("PairAlias", 0), ("ProviderLeafKey", 0),
            ("Padding", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 12);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
        ], schema);
        TrRmModuleRow[] moduleRows = coLocateLeafProvider
            ? [
                new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                    FeatureFirst: 1, FeatureCount: 1),
                new TrRmModuleRow(3, 1, 0, 0, 2, 3, 1, 1,
                    FeatureFirst: 1, FeatureCount: 1),
            ]
            : [
                new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                    FeatureFirst: 1, FeatureCount: 1),
            ];
        var typeDefCount = coLocateLeafProvider || splitPairAlias
            || relocatePair ? 3 : 2;
        var modulesBytes = TrRmModuleTableWriter.Encode(moduleRows,
            strings, 1, typeDefCount, 0, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes,
            moduleRows.Length, strings, 1, typeDefCount, 0, 0, 1);
        var moduleRefsBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(3, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefsBytes, 1,
            strings, modules);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0x0D),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
        ], typeDefCount, 1, 0, 0,
            schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 2,
            typeDefCount, 1, 0, 0,
            schema);
        var typeDefRows = new List<TrRmTypeDefRow>();
        var fieldRows = new List<TrRmFieldDefRow>();
        if (relocatePair)
        {
            typeDefRows.Add(new TrRmTypeDefRow(1, 12, 0x26, 1, 1,
                0, 0, 1, 2, 0, 0));
            typeDefRows.Add(new TrRmTypeDefRow(1, 4, 0x26, 2, 1,
                0, 0, 3, 2, 0, 0));
            typeDefRows.Add(new TrRmTypeDefRow(1, 5, 0x26, 3, 1,
                0, 0, 5, 2, 0, 0));
            fieldRows.Add(new TrRmFieldDefRow(1, 7, 1, 0));
            fieldRows.Add(new TrRmFieldDefRow(2, 6, 2, 0));
            fieldRows.Add(new TrRmFieldDefRow(3, 7, 1, 0));
        }
        else
        {
            typeDefRows.Add(new TrRmTypeDefRow(1, 4, 0x26, 1, 1,
                0, 0, 1, 2, 0, 0));
            typeDefRows.Add(new TrRmTypeDefRow(1, 5, 0x26, 2, 1,
                0, 0, 3, 2, 0, 0));
            fieldRows.Add(new TrRmFieldDefRow(1, 6, 2, 0));
            fieldRows.Add(new TrRmFieldDefRow(2, 7, 1, 0));
            if (coLocateLeafProvider)
            {
                typeDefRows.Add(new TrRmTypeDefRow(1, 5, 0x26, 3, 1,
                    0, 0, 5, 2, 0, 0));
                fieldRows.Add(new TrRmFieldDefRow(3, 7, 1, 0));
            }
            else if (splitPairAlias)
            {
                typeDefRows.Add(new TrRmTypeDefRow(1, 4, 0x26, 3, 1,
                    0, 0, 5, 2, 0, 0));
                fieldRows.Add(new TrRmFieldDefRow(3, 6, 2, 0));
            }
        }

        var layoutCount = typeDefCount * 2;
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldRows.Count, 0, layoutCount, 0,
            schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, typeDefCount,
            strings, modules, fieldRows.Count, 0, layoutCount, 0,
            schema);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, fieldRows.Count,
            strings, typeDefs, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var (layoutBytes, layoutFieldBytes)
            = CreateSingleFieldLayouts(typeDefs, fields, targetProfiles,
                typeDefCount);
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutCount,
            typeDefs, targetProfiles, layoutCount);

        _ = new TrRmLayoutFieldTableReader(layoutFieldBytes,
            layoutCount, layouts, typeDefs, fields);
        var importsBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 11,
                0x1300_0000u | (uint)(relocatePair ? 3 : 2)),
        ], strings, moduleRefs, typeDefs, default, default);
        var imports = new TrRmImportTableReader(importsBytes, 1, strings,
            moduleRefs, typeDefs, default, default);
        var exportRows = new List<TrRmExportRow>
        {
            new(0x1300_0000u | (uint)(relocatePair ? 2 : 1), 9),
            new(splitPairAlias ? 0x1300_0003u
                : 0x1300_0000u | (uint)(relocatePair ? 2 : 1), 10),
        };
        if (coLocateLeafProvider)
        {
            exportRows.Add(new TrRmExportRow(0x1300_0003, 8));
            exportRows.Add(new TrRmExportRow(0x1300_0003, 11));
        }
        var exportsBytes = TrRmExportTableWriter.Encode(exportRows, strings,
            typeDefs, default, default, imports);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, (uint)moduleRows.Length,
                modulesBytes),
            new TrRmSection(0x0002, 0, 12, stringsBytes),
            new TrRmSection(0x0004, 0, 1, featuresBytes),
            new TrRmSection(0x0010, 0, 1, moduleRefsBytes),
            new TrRmSection(0x0011, 0, 2, typeSpecsBytes),
            new TrRmSection(0x0013, 0, (uint)typeDefCount, typeDefsBytes),
            new TrRmSection(0x0014, 0, (uint)fieldRows.Count, fieldsBytes),
            new TrRmSection(0x0015, 0, (uint)layoutCount, layoutBytes),
            new TrRmSection(0x001C, 0, 1, importsBytes),
            new TrRmSection(0x001D, 0, (uint)exportRows.Count,
                exportsBytes),
            new TrRmSection(0x0023, 0, (uint)layoutCount, layoutFieldBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], schema);
    }

    private static byte[] CreateLeafProviderArtifact(uint pack = 16,
        TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("LeafProvider", 0), ("Leaf", 0), ("Data", 0),
            ("LeafKey", 0), ("ProviderLeafKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 6);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
        ], schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, 1, 0, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 1, 0, 0, 1);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0x0D),
        ], 1, 0, 0, 0,
            schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 1, 1, 0,
            0, 0, schema);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x26, 1, 1, 0, 0, 1, 2, 0, 0),
        ], strings, modules, 1, 0, 2, 0,
            schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 1, strings,
            modules, 1, 0, 2, 0,
            schema);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 4, 1, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 1, strings,
            typeDefs, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var (layoutBytes, layoutFieldBytes)
            = CreateSingleFieldLayouts(typeDefs, fields, targetProfiles, 1,
                pack);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 2, typeDefs,
            targetProfiles, 2);

        _ = new TrRmLayoutFieldTableReader(layoutFieldBytes, 2,
            layouts, typeDefs, fields);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x1300_0001, 5),
            new TrRmExportRow(0x1300_0001, 6),
        ], strings, typeDefs, default, default, default);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 6, stringsBytes),
            new TrRmSection(0x0004, 0, 1, featuresBytes),
            new TrRmSection(0x0011, 0, 1, typeSpecsBytes),
            new TrRmSection(0x0013, 0, 1, typeDefsBytes),
            new TrRmSection(0x0014, 0, 1, fieldsBytes),
            new TrRmSection(0x0015, 0, 2, layoutBytes),
            new TrRmSection(0x001D, 0, 2, exportsBytes),
            new TrRmSection(0x0023, 0, 2, layoutFieldBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], schema);
    }

    private static (byte[] LayoutBytes, byte[] LayoutFieldBytes)
        CreateSingleFieldLayouts(TrRmTypeDefTableReader typeDefs,
            TrRmFieldDefTableReader fields,
            TrRmTargetProfileTableReader targetProfiles,
            int typeDefCount, uint pack = 16)
    {
        var layouts = new List<TrRmLayoutRow>(typeDefCount * 2);
        var layoutFields = new List<TrRmLayoutFieldRow>(typeDefCount * 2);
        for (var typeDefRow = 1; typeDefRow <= typeDefCount; typeDefRow++)
        {
            var (fieldStart, fieldEnd) = typeDefs.GetFieldRange(typeDefRow);
            var fieldCount = fieldEnd - fieldStart;
            var defaultLayoutRow = layouts.Count + 1;
            layouts.Add(new TrRmLayoutRow(typeDefRow, 0, 1, 0, 0, pack,
                layoutFields.Count + 1, fieldCount));
            for (var fieldRow = fieldStart; fieldRow < fieldEnd; fieldRow++)
            {
                layoutFields.Add(new TrRmLayoutFieldRow(defaultLayoutRow,
                    fieldRow - fieldStart, fieldRow));
            }
            var targetLayoutRow = layouts.Count + 1;
            layouts.Add(new TrRmLayoutRow(typeDefRow, 1, 1, 1, 0, pack,
                layoutFields.Count + 1, fieldCount));
            for (var fieldRow = fieldStart; fieldRow < fieldEnd; fieldRow++)
            {
                layoutFields.Add(new TrRmLayoutFieldRow(targetLayoutRow,
                    fieldRow - fieldStart, fieldRow));
            }
        }

        var layoutBytes = TrRmLayoutTableWriter.Encode(layouts, typeDefs,
            targetProfiles, layoutFields.Count);
        var layoutReader = new TrRmLayoutTableReader(layoutBytes,
            layouts.Count, typeDefs, targetProfiles, layoutFields.Count);
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFields, layoutReader, typeDefs, fields);
        return (layoutBytes, layoutFieldBytes);
    }
}
