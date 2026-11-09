// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
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
    private const string A2R1SchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-repeat-initialization-1";
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
    public void StandardA2R1NativeParameterRetainsImportedProviderTypeMap()
    {
        const string schemaIdentity
            = A2R1SchemaIdentity;
        static byte[] CreateProviderArtifact(bool relocateContainer = false,
            bool changeContainerField = false)
        {
            var prefix = relocateContainer
                ? """
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct Prefix {
                        public uint32 Marker;
                    }

                    """
                : "";
            var container = changeContainerField
                ? """
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct Container {
                        public mut uint32 Other;
                    }
                    """
                : """
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct Container {
                        public mut uint32 Data;
                    }
                    """;
            var exports = relocateContainer
                ? new[] {
                    new TrRmSourceTypeExport("", "Prefix", "PrefixKey"),
                    new TrRmSourceTypeExport("", "Container",
                        "ContainerKey"),
                }
                : new[] {
                    new TrRmSourceTypeExport("", "Container",
                        "ContainerKey"),
                };
            return CreateSourceTypeLibraryArtifact("Root", SchemaIdentity,
                prefix + container, exports);
        }

        var rootArtifact = CreateProviderArtifact();
        var coreNativeRegistration
            = NativeCoreRegistrationTests.CreateRegistration();
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
            "Root", [],
            ["ContainerKey"]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "a2r1-test.dll",
                "Observe",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            public extern unsafe void Observe(ptr<Container> source,
                uint64 byteCount);

            void Main(ptr<Container> source,
                uint64 byteCount) {
                unsafe {
                    var callback = &Observe;
                    callback(source, byteCount);
                }
            }
            """), reference, new CompilationOptions {
                NativeCoreRegistration = coreNativeRegistration,
                LayoutCoreRegistration =
                    NativeCoreRegistrationTests.CreateLayoutRegistration(
                        coreNativeRegistration),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, coreNativeRegistration.UsingModuleIdentity,
            schemaIdentity, 1);
        var index
            = TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                    artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var inspectRow = 0;
        for (var method = 1; method <= tables.Methods.Count; method++)
        {
            if (tables.Methods.GetName(method) == "Main")
            {
                inspectRow = method;
                break;
            }
        }
        Assert.That(inspectRow, Is.Not.Zero);
        tables.ValidateRegisterBodyMapsForMethod(inspectRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(inspectRow), tables.Contracts.Count);
        Assert.That(body.TypeMap.Length, Is.GreaterThan(0));
        var typeDefImportRows = new HashSet<int>();
        for (var importRow = 1; importRow <= tables.Imports.Count;
            importRow++)
        {
            if ((tables.Imports.GetExpectedDefinitionForImportRow(importRow)
                    >> 24) == 0x13)
            {
                _ = typeDefImportRows.Add(importRow);
            }
        }
        var bodyTypeMapReferencesImport = false;
        for (var offset = 0; offset < body.TypeMap.Length;
            offset += sizeof(uint))
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap[offset..]);
            if ((token >> 24) != 0x11)
            {
                continue;
            }

            var typeSpecRow = tables.TypeSpecs.GetRow(
                checked((int)(token & 0x00FF_FFFF)));
            var importedTypeSpecRow = typeSpecRow.Length == 7
                && typeSpecRow[0] == 0x0C && typeSpecRow[2] == 0x8C
                ? tables.TypeSpecs.GetTypeSpecRef(
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        typeSpecRow[3..]))
                : default;
            var importRow = importedTypeSpecRow is {
                    IsRow: true,
                    Form: 0x03,
                }
                ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    tables.TypeSpecs.GetRow(importedTypeSpecRow.Row)[2..]))
                : 0;
            if (typeSpecRow.Length == 6 && typeSpecRow[0] == 0x03)
            {
                importRow = checked((int)BinaryPrimitives
                    .ReadUInt32LittleEndian(typeSpecRow[2..]));
            }
            if (typeDefImportRows.Contains(importRow))
            {
                bodyTypeMapReferencesImport = true;
                break;
            }
        }
        Assert.That(bodyTypeMapReferencesImport, Is.True);

        var providers = new[] {
            new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
        };
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            coreNativeRegistration.UsingModuleIdentity, providers);
        var caller = loaded.Functions.Single(
            static function => function.Name == "Main");
        var containerIndex = loaded.Structures
            .Select((structure, index) => (structure, index))
            .Single(static item => item.structure.Name == "Container")
            .index;
        var containerType = (PrimitiveType)(100 + containerIndex);
        Assert.That(loaded.NativePointerTypes.Any(pointer =>
            pointer.PointeeType == containerType), Is.True);
        Assert.That(caller.Body.Statements, Is.Not.Empty);
        var containerOrigin = loaded.Structures[containerIndex]
            .StandardTypeOrigin!;
        Assert.That(containerOrigin.Provider.ModuleIdentity,
            Is.EqualTo("Root"));
        Assert.That(containerOrigin.Expectations.Any(expectation =>
            expectation.ExportKey == "ContainerKey"
            && expectation.UsingModule.ModuleIdentity
                == coreNativeRegistration.UsingModuleIdentity), Is.True);

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            coreNativeRegistration.UsingModuleIdentity, schemaIdentity, 1);
        var relocatedProviders = new[] {
            new TrRmStandardArtifactInput(SchemaIdentity,
                CreateProviderArtifact(relocateContainer: true)),
        };
        var relocated = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            coreNativeRegistration.UsingModuleIdentity, relocatedProviders);
        var relocatedCaller = relocated.Functions.Single(
            static function => function.Name == "Main");
        var relocatedContainerIndex = relocated.Structures
            .Select((structure, index) => (structure, index))
            .Single(static item => item.structure.Name == "Container").index;
        var relocatedContainerType
            = (PrimitiveType)(100 + relocatedContainerIndex);
        Assert.That(relocated.NativePointerTypes.Any(pointer =>
            pointer.PointeeType == relocatedContainerType), Is.True);
        Assert.That(relocatedCaller.Body.Statements, Is.Not.Empty);
        var relocatedContainerOrigin = relocated.Structures[
            relocatedContainerIndex].StandardTypeOrigin!;
        Assert.That(relocatedContainerOrigin.Provider.ModuleIdentity,
            Is.EqualTo("Root"));
        Assert.That(relocatedContainerOrigin.DefinitionId,
            Is.Not.EqualTo(containerOrigin.DefinitionId));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                coreNativeRegistration.UsingModuleIdentity,
                [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        CreateProviderArtifact(changeContainerField: true)),
                ]));
    }

    [Test]
    public void StandardA2R1SourceTypeCompositionRetainsMixedProviderSchemas()
    {
        var rootArtifact = CreateSourceTypeLibraryArtifact("Root",
            SchemaIdentity, """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Container {
                public mut uint32 Data;
            }
            """,
            [new TrRmSourceTypeExport("", "Container", "ContainerKey")]);
        var rootReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
            "Root", [], ["ContainerKey"]);
        var repeatProviderArtifact = CreateSourceTypeLibraryArtifact(
            "RepeatProvider", A2R1SchemaIdentity, """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Wrapper {
                public mut Container Value;
            }
            """,
            [new TrRmSourceTypeExport("", "Wrapper", "WrapperKey")],
            rootReference);
        var repeatProviderReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(A2R1SchemaIdentity,
                    repeatProviderArtifact),
                "RepeatProvider",
                [new TrRmStandardArtifactInput(SchemaIdentity,
                    rootArtifact)],
                ["WrapperKey"]);
        var outerArtifact = CreateSourceTypeLibraryArtifact("RepeatOuter",
            A2R1SchemaIdentity, """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Outer {
                public mut Wrapper Value;
            }
            """,
            [new TrRmSourceTypeExport("", "Outer", "OuterKey")],
            repeatProviderReference);
        var providers = new[] {
            new TrRmStandardArtifactInput(A2R1SchemaIdentity,
                repeatProviderArtifact),
            new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
        };
        var outerReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(A2R1SchemaIdentity, outerArtifact),
            "RepeatOuter", providers, ["OuterKey"]);

        var outerOrigin = outerReference.Structures.Single(
            static structure => structure.Name == "Outer")
            .StandardTypeOrigin!;
        var wrapperOrigin = outerReference.Structures.Single(
            static structure => structure.Name == "Wrapper")
            .StandardTypeOrigin!;
        var containerOrigin = outerReference.Structures.Single(
            static structure => structure.Name == "Container")
            .StandardTypeOrigin!;
        Assert.That(outerOrigin.Provider.SchemaIdentity,
            Is.EqualTo(A2R1SchemaIdentity));
        Assert.That(wrapperOrigin.Provider.SchemaIdentity,
            Is.EqualTo(A2R1SchemaIdentity));
        Assert.That(containerOrigin.Provider.SchemaIdentity,
            Is.EqualTo(SchemaIdentity));
        Assert.That(wrapperOrigin.Expectations.Any(expectation =>
            expectation.UsingModule.ModuleIdentity == "RepeatProvider"
            && expectation.ExportKey == "ContainerKey"), Is.True,
            string.Join(Environment.NewLine,
                wrapperOrigin.Expectations.Select(static expectation =>
                    $"{expectation.UsingModule.ModuleIdentity} -> "
                    + $"{expectation.ProducerIdentity} ({expectation.ExportKey})")));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(A2R1SchemaIdentity, outerArtifact),
            "RepeatOuter", providers);
        var reencoded = TrRmExecutableCompiler.EmitStandard(loaded,
            "RepeatOuter", A2R1SchemaIdentity, 1);
        var reencodedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(A2R1SchemaIdentity,
                    reencoded),
                "RepeatOuter", providers, ["OuterKey"]);
        Assert.That(reencodedReference.Structures.Single(
                static structure => structure.Name == "Wrapper")
            .StandardTypeOrigin!.Provider.SchemaIdentity,
            Is.EqualTo(A2R1SchemaIdentity));
        Assert.That(reencodedReference.Structures.Single(
                static structure => structure.Name == "Container")
            .StandardTypeOrigin!.Provider.SchemaIdentity,
            Is.EqualTo(SchemaIdentity));

        var incompatibleRoot = CreateSourceTypeLibraryArtifact("Root",
            SchemaIdentity, """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Container {
                public mut uint32 Other;
            }
            """,
            [new TrRmSourceTypeExport("", "Container", "ContainerKey")]);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(A2R1SchemaIdentity,
                    reencoded),
                "RepeatOuter",
                [
                    providers[0],
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        incompatibleRoot),
                ]));

        var a2eRootArtifact = CreateSourceTypeLibraryArtifact("Root",
            EmptyInitializationSchemaIdentity, """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Container {
                public mut uint32 Data;
            }
            """,
            [new TrRmSourceTypeExport("", "Container", "ContainerKey")]);
        var unsupportedProviderSchema
            = Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(A2R1SchemaIdentity,
                        repeatProviderArtifact),
                    "RepeatProvider",
                    [
                        new TrRmStandardArtifactInput(
                            EmptyInitializationSchemaIdentity,
                            a2eRootArtifact),
                    ],
                    ["WrapperKey"]));
        Assert.That(unsupportedProviderSchema!.Message,
            Does.Contain("A2R1 TypeDef provider edges"));
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
    public async Task StandardA2SourceReferenceRebindsPermutedTypeAndArrayRows()
    {
        static byte[] CreateProvider(bool reordered)
        {
            var source = reordered
                ? """
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct Element {
                        public mut int32 Value;
                        public mut int32 Size;
                    }
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct ContainerB {
                        public mut [Element; 3] Values;
                    }
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct ContainerA {
                        public mut [Element; 2] Values;
                    }
                    """
                : """
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct Element {
                        public mut int32 Value;
                        public mut int32 Size;
                    }
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct ContainerA {
                        public mut [Element; 2] Values;
                    }
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct ContainerB {
                        public mut [Element; 3] Values;
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

        if (OperatingSystem.IsWindows()
            && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                == System.Runtime.InteropServices.Architecture.X64)
        {
            static byte[] CreateBodyProvider(bool reordered)
            {
                var source = reordered
                    ? """
                        [Blittable] [Layout(LayoutKind.Sequential)]
                        public struct ContainerB {
                            public mut [int32; 3] Values;
                        }
                        [Blittable] [Layout(LayoutKind.Sequential)]
                        public struct ContainerA {
                            public mut [int32; 2] Values;
                            public mut [int32; 1] Sibling;
                        }
                        """
                    : """
                        [Blittable] [Layout(LayoutKind.Sequential)]
                        public struct ContainerA {
                            public mut [int32; 2] Values;
                            public mut [int32; 1] Sibling;
                        }
                        [Blittable] [Layout(LayoutKind.Sequential)]
                        public struct ContainerB {
                            public mut [int32; 3] Values;
                        }
                        """;
                return CreateSourceTypeLibraryArtifact(
                    "a2r1-global-permutation-provider", SchemaIdentity,
                    source,
                    [
                        new TrRmSourceTypeExport("", "ContainerA",
                            "ContainerAKey"),
                        new TrRmSourceTypeExport("", "ContainerB",
                            "ContainerBKey"),
                    ]);
            }

            var bodyProvider = CreateBodyProvider(reordered: false);
            const string bodyProviderIdentity
                = "a2r1-global-permutation-provider";
            var bodyProviderReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        bodyProvider),
                    bodyProviderIdentity, [],
                    ["ContainerAKey", "ContainerBKey"]);
            const string bodySource = """
                [Terrum.Interop.NativeImport(
                    "kernel32.dll",
                    "RtlMoveMemory",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
                [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
                internal extern void RtlMoveMemory(
                    write ptr<int32> destination,
                    ptr<int32> source,
                    uint64 byteCount);

                int32 Main() {
                    int32 zeroIndex = 0;
                    int32 oneIndex = 1;
                    [int32; 2] values = [-1, 0];
                    int32 siblingValue = 99;
                    [int32; 1] sibling = [siblingValue];
                    int32 replacement = 42;
                    init mut ContainerA backing;
                    backing.Values = values;
                    backing.Sibling = sibling;
                    mut int32 observed = 0;
                    uint64 byteCount = 4;
                    unsafe {
                        backing.Values[oneIndex] = replacement;
                        var callback = &RtlMoveMemory;
                        callback(&observed,
                            &backing.Sibling[zeroIndex], byteCount);
                    }
                    return observed;
                }
                """;
            var nativeRegistration
                = NativeCoreRegistrationTests.CreateRegistration();
            var bodyCompilation = Compilation.Create(
                SyntaxTree.Parse(bodySource), bodyProviderReference,
                new CompilationOptions {
                    RuntimePolicy = BoundRuntimePolicy.FailFast,
                    NativeCoreRegistration = nativeRegistration,
                    LayoutCoreRegistration
                        = NativeCoreRegistrationTests.CreateLayoutRegistration(
                            nativeRegistration),
                });
            Assert.That(bodyCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    bodyCompilation.Diagnostics));
            const string bodyModuleIdentity = "a2r1-permuted-provider-body";
            const string valueProbeModuleIdentity
                = "a2r1-permuted-provider-body-mutated-value";
            var bodyArtifact = TrRmExecutableCompiler.EmitStandard(
                bodyCompilation.Program!, bodyModuleIdentity,
                A2R1SchemaIdentity, 1);
            var bodyInput = new TrRmStandardArtifactInput(
                A2R1SchemaIdentity, bodyArtifact);
            var valueProbeSource = bodySource.Replace(
                "&backing.Sibling[zeroIndex]",
                "&backing.Values[oneIndex]", StringComparison.Ordinal);
            var valueProbeCompilation = Compilation.Create(
                SyntaxTree.Parse(valueProbeSource), bodyProviderReference,
                new CompilationOptions {
                    RuntimePolicy = BoundRuntimePolicy.FailFast,
                    NativeCoreRegistration = nativeRegistration,
                    LayoutCoreRegistration
                        = NativeCoreRegistrationTests.CreateLayoutRegistration(
                            nativeRegistration),
                });
            Assert.That(valueProbeCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    valueProbeCompilation.Diagnostics));
            var valueProbeArtifact = TrRmExecutableCompiler.EmitStandard(
                valueProbeCompilation.Program!, valueProbeModuleIdentity,
                A2R1SchemaIdentity, 1);
            var valueProbeInput = new TrRmStandardArtifactInput(
                A2R1SchemaIdentity, valueProbeArtifact);
            var reorderedBodyProvider = CreateBodyProvider(reordered: true);
            var fieldDefPermutedBodyProvider
                = PermuteFieldDefOwnerBlocks(bodyProvider);
            var layoutPermutedBodyProvider
                = ReverseLayoutVariants(bodyProvider);
            var typeSpecPermutedBodyProvider
                = ReverseTypeSpecRows(bodyProvider);
            var arrayPermutedBodyProvider
                = ReverseFixedArrayLayoutRows(bodyProvider);
            var allTablesPermutedProvider
                = ReverseFixedArrayLayoutRows(
                    ReverseTypeSpecRows(
                        ReverseLayoutVariants(
                            PermuteFieldDefOwnerBlocks(bodyProvider))));
            Assert.That(allTablesPermutedProvider,
                Is.Not.EqualTo(bodyProvider));
            var bodyProviderCases
                = new (string Name, byte[] Artifact)[] {
                    ("original", bodyProvider),
                    ("typedef-rows", reorderedBodyProvider),
                    ("fielddef-rows", fieldDefPermutedBodyProvider),
                    ("layout-rows", layoutPermutedBodyProvider),
                    ("typespec-rows", typeSpecPermutedBodyProvider),
                    ("fixed-array-layout-rows", arrayPermutedBodyProvider),
                    ("combined-table-permutations",
                        allTablesPermutedProvider),
                };
            var sourceHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(bodySource)));
            var valueProbeSourceHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(valueProbeSource)));
            var bodyHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(bodyArtifact));
            var valueProbeHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    valueProbeArtifact));
            var compilerPath = typeof(TrRmExecutableCompiler).Assembly.Location;
            var compilerHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(compilerPath)));
            var nativeLibraryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "kernel32.dll");
            var nativeLibraryHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(nativeLibraryPath)));
            byte[]? expectedReemittedBody = null;
            byte[]? expectedReemittedValueProbe = null;

            foreach (var (providerName, providerArtifact)
                in bodyProviderCases)
            {
                var providerInput = new TrRmStandardArtifactInput(
                    SchemaIdentity, providerArtifact);
                var loadedBody = TrRmExecutableCompiler.LoadStandard(
                    bodyInput, bodyModuleIdentity, [providerInput]);
                Assert.That(NativeFixedArrayContract.MatchesProgram(loadedBody),
                    Is.True);
                var reemittedBody = TrRmExecutableCompiler.EmitStandard(
                    loadedBody, bodyModuleIdentity, A2R1SchemaIdentity, 1);
                var reemittedBodyInput = new TrRmStandardArtifactInput(
                    A2R1SchemaIdentity, reemittedBody);
                var reloadedBody = TrRmExecutableCompiler.LoadStandard(
                    reemittedBodyInput, bodyModuleIdentity, [providerInput]);
                Assert.That(TrRmExecutableCompiler.EmitStandard(reloadedBody,
                    bodyModuleIdentity, A2R1SchemaIdentity, 1),
                    Is.EqualTo(reemittedBody));
                Assert.That(reloadedBody.NativeFixedArrayTypes,
                    Is.EqualTo(loadedBody.NativeFixedArrayTypes));
                if (expectedReemittedBody is null)
                {
                    expectedReemittedBody = reemittedBody;
                }
                else
                {
                    Assert.That(reemittedBody,
                        Is.EqualTo(expectedReemittedBody));
                }
                var jitResult = NativeModuleCompiler.ExecuteStandard(
                    reemittedBodyInput, bodyModuleIdentity, [providerInput],
                    NativeRuntimePolicy.FailFast);
                Assert.That(jitResult, Is.EqualTo(99));
                var image = NativeModuleCompiler.EmitStandardExecutable(
                    reemittedBodyInput, bodyModuleIdentity, [providerInput],
                    NativeRuntimePolicy.FailFast);
                await TrRmForeignInvocationAddressEmitterTests
                    .AssertNativeImageExitCodeAsync(image,
                        $"{bodyModuleIdentity}-{providerName}", 99)
                    .ConfigureAwait(true);
                var loadedValueProbe = TrRmExecutableCompiler.LoadStandard(
                    valueProbeInput, valueProbeModuleIdentity,
                    [providerInput]);
                Assert.That(NativeFixedArrayContract.MatchesProgram(
                    loadedValueProbe), Is.True);
                var reemittedValueProbe
                    = TrRmExecutableCompiler.EmitStandard(loadedValueProbe,
                        valueProbeModuleIdentity, A2R1SchemaIdentity, 1);
                var reemittedValueProbeInput
                    = new TrRmStandardArtifactInput(A2R1SchemaIdentity,
                        reemittedValueProbe);
                var reloadedValueProbe
                    = TrRmExecutableCompiler.LoadStandard(
                        reemittedValueProbeInput,
                        valueProbeModuleIdentity, [providerInput]);
                Assert.That(TrRmExecutableCompiler.EmitStandard(
                    reloadedValueProbe, valueProbeModuleIdentity,
                    A2R1SchemaIdentity, 1), Is.EqualTo(reemittedValueProbe));
                Assert.That(reloadedValueProbe.NativeFixedArrayTypes,
                    Is.EqualTo(loadedValueProbe.NativeFixedArrayTypes));
                if (expectedReemittedValueProbe is null)
                {
                    expectedReemittedValueProbe = reemittedValueProbe;
                }
                else
                {
                    Assert.That(reemittedValueProbe,
                        Is.EqualTo(expectedReemittedValueProbe));
                }
                var valueProbeJitResult
                    = NativeModuleCompiler.ExecuteStandard(
                        reemittedValueProbeInput, valueProbeModuleIdentity,
                        [providerInput], NativeRuntimePolicy.FailFast);
                Assert.That(valueProbeJitResult, Is.EqualTo(42));
                var valueProbeImage
                    = NativeModuleCompiler.EmitStandardExecutable(
                        reemittedValueProbeInput, valueProbeModuleIdentity,
                        [providerInput], NativeRuntimePolicy.FailFast);
                await TrRmForeignInvocationAddressEmitterTests
                    .AssertNativeImageExitCodeAsync(valueProbeImage,
                        $"{valueProbeModuleIdentity}-{providerName}", 42)
                    .ConfigureAwait(true);
                TestContext.WriteLine(
                    $"A2R1 provider-global Body: provider={providerName}; target=WindowsX64; policy=FailFast; sibling-result={jitResult}; mutated-value-result={valueProbeJitResult}; source-sha256={sourceHash}; consumer-sha256={bodyHash}; value-probe-source-sha256={valueProbeSourceHash}; value-probe-consumer-sha256={valueProbeHash}; provider-sha256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(providerArtifact))}; compiler={compilerPath}; compiler-sha256={compilerHash}; native-library={nativeLibraryPath}; native-library-sha256={nativeLibraryHash}.");
            }

            var incompatibleProviderArtifact
                = CreateSourceTypeLibraryArtifact(
                    bodyProviderIdentity, SchemaIdentity,
                    """
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct ContainerA {
                        public mut [int32; 3] Values;
                        public mut [int32; 1] Sibling;
                    }
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct ContainerB {
                        public mut [int32; 3] Values;
                    }
                    """,
                    [
                        new TrRmSourceTypeExport("", "ContainerA",
                            "ContainerAKey"),
                        new TrRmSourceTypeExport("", "ContainerB",
                            "ContainerBKey"),
                    ]);
            var incompatibleProviderInput
                = new TrRmStandardArtifactInput(SchemaIdentity,
                    incompatibleProviderArtifact);
            _ = Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(bodyInput,
                    bodyModuleIdentity, [incompatibleProviderInput]));
        }
        else
        {
            TestContext.WriteLine(
                "A2R1 provider-global Body execution requires Windows x64.");
        }
    }

    [Test]
    public async Task StandardA2R1ConsumerGlobalRowsRetainMultiProviderTypes()
    {
        Assume.That(OperatingSystem.IsWindows()
            && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                == System.Runtime.InteropServices.Architecture.X64);

        static void AssertProviderExpectation(BoundProgram program,
            string providerIdentity, string consumerIdentity,
            int providerModuleRow, string typeName, string exportKey)
        {
            var origin = program.Structures.Single(
                structure => structure.Name == typeName)
                .StandardTypeOrigin!;
            Assert.That(origin.Provider.ModuleIdentity,
                Is.EqualTo(providerIdentity));
            Assert.That(origin.Provider.ModuleRow,
                Is.EqualTo(providerModuleRow));
            Assert.That(origin.Expectations.Any(expectation =>
                expectation.UsingModule.ModuleIdentity == consumerIdentity
                    && expectation.ExportKey == exportKey), Is.True);
        }

        var leafProviderArtifact = CreateLeafProviderArtifact();
        var leafProviderInput = new TrRmStandardArtifactInput(SchemaIdentity,
            leafProviderArtifact);
        var leafProviderReference
            = TrRmExecutableCompiler.CreateStandardReference(
                leafProviderInput, "LeafProvider", [], ["LeafKey"]);
        const string providerIdentity = "consumer-global-provider";
        byte[] CreateProvider(bool includePrefix, int arrayExtent = 2,
            string schemaIdentity = SchemaIdentity)
        {
            var prefix = includePrefix
                ? """
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct Prefix {
                        public int32 Tag;
                    }

                    """
                : "";
            var source = prefix + """
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct ContainerA {
                    public mut [int32; 2] Values;
                    public mut [int32; 1] Sibling;
                }
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct UsesLeaf {
                    public Leaf Child;
                }
                """;
            if (arrayExtent != 2)
            {
                source = source.Replace("[int32; 2] Values;",
                    $"[int32; {arrayExtent}] Values;",
                    StringComparison.Ordinal);
            }
            return CreateSourceTypeLibraryArtifact(
                providerIdentity, schemaIdentity, source,
                [
                new TrRmSourceTypeExport("", "ContainerA", "ContainerAKey"),
                new TrRmSourceTypeExport("", "UsesLeaf", "UsesLeafKey"),
                ], leafProviderReference);
        }
        var providerArtifact = CreateProvider(includePrefix: false);
        var providerInput = new TrRmStandardArtifactInput(SchemaIdentity,
            providerArtifact);
        var providerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                providerInput, providerIdentity, [leafProviderInput],
                ["ContainerAKey", "UsesLeafKey"]);
        const string bodySource = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void CopyContainer(
                write ptr<UsesLeaf> destination,
                ptr<UsesLeaf> source,
                uint64 byteCount);

            int32 Main() {
                int32 zeroIndex = 0;
                int32 oneIndex = 1;
                [int32; 2] values = [-1, 0];
                int32 siblingValue = 99;
                [int32; 1] sibling = [siblingValue];
                int32 replacement = 42;
                init mut ContainerA backing;
                backing.Values = values;
                backing.Sibling = sibling;
                mut int32 observed = 0;
                uint64 byteCount = 4;
                unsafe {
                    backing.Values[oneIndex] = replacement;
                    var callback = &RtlMoveMemory;
                    callback(&observed,
                        &backing.Sibling[zeroIndex], byteCount);
                }
                return observed;
            }
            """;
        var nativeRegistration
            = NativeCoreRegistrationTests.CreateRegistration();
        var compilation = Compilation.Create(
            SyntaxTree.Parse(bodySource), providerReference,
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration = nativeRegistration,
                LayoutCoreRegistration
                    = NativeCoreRegistrationTests.CreateLayoutRegistration(
                        nativeRegistration),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        const string consumerIdentity = "consumer-global-row-consumer";
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, consumerIdentity, A2R1SchemaIdentity, 1);
        var input = new TrRmStandardArtifactInput(A2R1SchemaIdentity,
            artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                        artifact));
        Assert.That(tables.ModuleRefs.Count, Is.GreaterThan(1));
        Assert.That(tables.Imports.Count, Is.GreaterThan(1));
        Assert.That(tables.StandardNativeImports.Count, Is.EqualTo(2));
        Assert.That(tables.Methods.Count, Is.GreaterThan(3));
        Assert.That(tables.Signatures.Count, Is.GreaterThan(2));
        Assert.That(tables.Signatures.ParamCount, Is.GreaterThan(3));
        var anchorTypeSpecRows = 0;
        for (var row = 1; row <= tables.TypeSpecs.Count; row++)
        {
            if (tables.TypeSpecs.GetRow(row)[0] == 0x0D)
            {
                anchorTypeSpecRows++;
            }
        }
        Assert.That(anchorTypeSpecRows, Is.EqualTo(1));
        var moduleRefIdentities = new List<string>(tables.ModuleRefs.Count);
        for (var row = 1; row <= tables.ModuleRefs.Count; row++)
        {
            moduleRefIdentities.Add(
                tables.ModuleRefs.GetProducerIdentity(row));
        }
        TestContext.WriteLine(
            $"A2R1 consumer global rows: modules={tables.Modules.Count}; moduleRefs={tables.ModuleRefs.Count}[{string.Join(",", moduleRefIdentities)}]; imports={tables.Imports.Count}; typeSpecs={tables.TypeSpecs.Count}; methods={tables.Methods.Count}; methodSigs={tables.Signatures.Count}; params={tables.Signatures.ParamCount}; nativeImports={tables.StandardNativeImports.Count}; nativeFunctionPointerAnchors={anchorTypeSpecRows}; fieldPaths={tables.FieldPaths.Count}.");
        var methodSummaries = new List<string>(tables.Methods.Count);
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            methodSummaries.Add(
                $"{row}:{tables.Methods.GetName(row)} sig={tables.Methods.GetSignature(row)} body={tables.Methods.GetBodyRow(row)} contracts={tables.Methods.GetContractRange(row)} native={tables.StandardNativeImports.HasImportForMethod(row)}");
        }
        var mainMethodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                mainMethodRow = row;
                break;
            }
        }
        var mainBody = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainMethodRow), tables.Contracts.Count);
        var methodMapTokens = new List<string>();
        for (var offset = 0; offset < mainBody.MethodMap.Length;
            offset += sizeof(uint))
        {
            methodMapTokens.Add(BinaryPrimitives.ReadUInt32LittleEndian(
                mainBody.MethodMap[offset..]).ToString("X8"));
        }
        TestContext.WriteLine(
            $"A2R1 consumer MethodDefs=[{string.Join(";", methodSummaries)}]; Main.TypeMap={Convert.ToHexString(mainBody.TypeMap)}; Main.MethodMap=[{string.Join(",", methodMapTokens)}]; Main.FieldMapBytes={mainBody.FieldMap.Length}; Main.ConstantMapBytes={mainBody.ConstantMap.Length}.");

        var fieldMapArtifact = AddA2R1BodyFieldMapToken(artifact, "Main");
        var fieldMapPermutedArtifact
            = PermuteA2R1ConsumerGlobalRows(fieldMapArtifact);
        var fieldMapPermutedTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                        fieldMapPermutedArtifact));
        var fieldMapMainRow = 0;
        for (var row = 1;
            row <= fieldMapPermutedTables.Methods.Count; row++)
        {
            if (fieldMapPermutedTables.Methods.GetName(row) == "Main")
            {
                fieldMapMainRow = row;
                break;
            }
        }
        var fieldMapBody = fieldMapPermutedTables.Bodies.GetRegisterBody(
            fieldMapPermutedTables.Methods.GetBodyRow(fieldMapMainRow),
            fieldMapPermutedTables.Contracts.Count);
        Assert.That(fieldMapBody.FieldMap.Length, Is.EqualTo(sizeof(uint)));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            fieldMapBody.FieldMap), Is.EqualTo(0x1400_0001u));

        var providers = new[] { providerInput, leafProviderInput };
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            consumerIdentity, providers);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            consumerIdentity, A2R1SchemaIdentity, 1);
        var reemittedInput = new TrRmStandardArtifactInput(
            A2R1SchemaIdentity, reemitted);
        var reloaded = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            consumerIdentity, providers);
        Assert.That(TrRmExecutableCompiler.EmitStandard(reloaded,
            consumerIdentity, A2R1SchemaIdentity, 1), Is.EqualTo(reemitted));
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            consumerIdentity, providers, NativeRuntimePolicy.FailFast),
            Is.EqualTo(99));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedInput, consumerIdentity, providers,
            NativeRuntimePolicy.FailFast);

        var permutedArtifact = PermuteA2R1ConsumerGlobalRows(artifact);
        Assert.That(permutedArtifact, Is.Not.EqualTo(artifact));
        var permutedInput = new TrRmStandardArtifactInput(
            A2R1SchemaIdentity, permutedArtifact);
        var permutedTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                        permutedArtifact));
        var permutedMainMethodRow = 0;
        for (var row = 1; row <= permutedTables.Methods.Count; row++)
        {
            if (permutedTables.Methods.GetName(row) == "Main")
            {
                permutedMainMethodRow = row;
                break;
            }
        }
        Assert.That(permutedMainMethodRow, Is.Not.EqualTo(mainMethodRow));
        Assert.That(permutedTables.Methods.GetBodyRow(permutedMainMethodRow),
            Is.EqualTo(tables.Methods.GetBodyRow(mainMethodRow)));
        var permutedAnchorRow = 0;
        for (var row = 1; row <= permutedTables.TypeSpecs.Count; row++)
        {
            var typeSpec = permutedTables.TypeSpecs.GetRow(row);
            if (typeSpec[0] == 0x0D)
            {
                permutedAnchorRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(typeSpec[3..]));
                break;
            }
        }
        Assert.That(permutedAnchorRow, Is.Not.Zero);
        Assert.That(permutedTables.Methods.GetName(permutedAnchorRow),
            Is.EqualTo("__trrm_fnptr_anchor_1"));

        var permutedLoaded = TrRmExecutableCompiler.LoadStandard(
            permutedInput, consumerIdentity, providers);
        var permutedReemitted = TrRmExecutableCompiler.EmitStandard(
            permutedLoaded, consumerIdentity, A2R1SchemaIdentity, 1);
        var permutedReemittedInput = new TrRmStandardArtifactInput(
            A2R1SchemaIdentity, permutedReemitted);
        var permutedReloaded = TrRmExecutableCompiler.LoadStandard(
            permutedReemittedInput, consumerIdentity, providers);
        Assert.That(TrRmExecutableCompiler.EmitStandard(permutedReloaded,
            consumerIdentity, A2R1SchemaIdentity, 1),
            Is.EqualTo(permutedReemitted));
        Assert.That(NativeModuleCompiler.ExecuteStandard(
            permutedReemittedInput, consumerIdentity, providers,
            NativeRuntimePolicy.FailFast), Is.EqualTo(99));
        var permutedImage = NativeModuleCompiler.EmitStandardExecutable(
            permutedReemittedInput, consumerIdentity, providers,
            NativeRuntimePolicy.FailFast);

        var shiftedProviderInput = new TrRmStandardArtifactInput(
            SchemaIdentity, CreateProvider(includePrefix: true));
        var shiftedProviders = new[] {
            shiftedProviderInput,
            leafProviderInput,
        };
        var shiftedProviderLoaded = TrRmExecutableCompiler.LoadStandard(
            permutedReemittedInput, consumerIdentity, shiftedProviders);
        var shiftedProviderReemitted = TrRmExecutableCompiler.EmitStandard(
            shiftedProviderLoaded, consumerIdentity, A2R1SchemaIdentity, 1);
        var shiftedProviderReemittedInput = new TrRmStandardArtifactInput(
            A2R1SchemaIdentity, shiftedProviderReemitted);
        Assert.That(NativeModuleCompiler.ExecuteStandard(
            shiftedProviderReemittedInput, consumerIdentity,
            shiftedProviders, NativeRuntimePolicy.FailFast),
            Is.EqualTo(99));
        var shiftedProviderImage
            = NativeModuleCompiler.EmitStandardExecutable(
                shiftedProviderReemittedInput, consumerIdentity,
                shiftedProviders, NativeRuntimePolicy.FailFast);

        var coLocatedProviderArtifact
            = CreateProvider(includePrefix: false,
                schemaIdentity: A2R1SchemaIdentity);
        var coLocatedArtifact
            = CombineA2R1ConsumerAndTypeProviderArtifacts(artifact,
                coLocatedProviderArtifact);
        var coLocatedInput = new TrRmStandardArtifactInput(
            A2R1SchemaIdentity, coLocatedArtifact);
        var coLocatedTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                        coLocatedArtifact));
        Assert.That(coLocatedTables.Modules.Count, Is.EqualTo(2));
        Assert.That(coLocatedTables.Modules.GetIdentity(1),
            Is.EqualTo(consumerIdentity));
        Assert.That(coLocatedTables.Modules.GetIdentity(2),
            Is.EqualTo(providerIdentity));
        var (consumerTypeDefStart, consumerTypeDefEnd)
            = coLocatedTables.Modules.GetTypeDefRange(1);
        var (providerTypeDefStart, providerTypeDefEnd)
            = coLocatedTables.Modules.GetTypeDefRange(2);
        Assert.That(providerTypeDefStart, Is.EqualTo(consumerTypeDefEnd));
        Assert.That(providerTypeDefEnd,
            Is.EqualTo(coLocatedTables.TypeDefs.Count + 1));
        Assert.That(consumerTypeDefStart, Is.EqualTo(1));
        var providerFieldCount = 0;
        for (var row = 1; row <= coLocatedTables.Fields.Count; row++)
        {
            var owner = coLocatedTables.Fields.GetOwnerTypeDef(row);
            if (owner >= providerTypeDefStart && owner < providerTypeDefEnd)
            {
                providerFieldCount++;
            }
        }
        Assert.That(providerFieldCount, Is.GreaterThan(0));
        var providerLayoutCount = 0;
        for (var row = 1; row <= coLocatedTables.Layouts.Count; row++)
        {
            var owner = coLocatedTables.Layouts.GetOwnerTypeDef(row);
            if (owner >= providerTypeDefStart && owner < providerTypeDefEnd)
            {
                providerLayoutCount++;
            }
        }
        Assert.That(providerLayoutCount, Is.GreaterThan(0));

        var coLocatedLoaded = TrRmExecutableCompiler.LoadStandard(
            coLocatedInput, consumerIdentity, [leafProviderInput]);
        AssertProviderExpectation(coLocatedLoaded, providerIdentity,
            consumerIdentity, 2, "ContainerA", "ContainerAKey");
        AssertProviderExpectation(coLocatedLoaded, providerIdentity,
            consumerIdentity, 2, "UsesLeaf", "UsesLeafKey");
        Assert.That(NativeModuleCompiler.ExecuteStandard(
            coLocatedInput, consumerIdentity, [leafProviderInput],
            NativeRuntimePolicy.FailFast), Is.EqualTo(99));
        var coLocatedReemitted = TrRmExecutableCompiler.EmitStandard(
            coLocatedLoaded, consumerIdentity, A2R1SchemaIdentity, 1);
        var coLocatedReemittedInput = new TrRmStandardArtifactInput(
            A2R1SchemaIdentity, coLocatedReemitted);
        var coLocatedReloaded = TrRmExecutableCompiler.LoadStandard(
            coLocatedReemittedInput, consumerIdentity, providers);
        AssertProviderExpectation(coLocatedReloaded, providerIdentity,
            consumerIdentity, 1, "ContainerA", "ContainerAKey");
        AssertProviderExpectation(coLocatedReloaded, providerIdentity,
            consumerIdentity, 1, "UsesLeaf", "UsesLeafKey");
        Assert.That(NativeModuleCompiler.ExecuteStandard(
            coLocatedReemittedInput, consumerIdentity, providers,
            NativeRuntimePolicy.FailFast), Is.EqualTo(99));
        var coLocatedImage = NativeModuleCompiler.EmitStandardExecutable(
            coLocatedInput, consumerIdentity, [leafProviderInput],
            NativeRuntimePolicy.FailFast);

        var bodyPermutedConsumer
            = TrRmForeignInvocationAddressEmitterTests
                .PermuteA2BodyLocalMaps(artifact, "Main",
                    isRepeatInitialization: true);
        var globalPermutedConsumer
            = PermuteA2R1ConsumerGlobalRows(bodyPermutedConsumer);
        Assert.That(globalPermutedConsumer, Is.Not.EqualTo(artifact));
        var coLocatedPermutedArtifact
            = CombineA2R1ConsumerAndTypeProviderArtifacts(
                globalPermutedConsumer, coLocatedProviderArtifact);
        var coLocatedPermutedInput = new TrRmStandardArtifactInput(
            A2R1SchemaIdentity, coLocatedPermutedArtifact);
        var coLocatedPermutedLoaded = TrRmExecutableCompiler.LoadStandard(
            coLocatedPermutedInput, consumerIdentity, [leafProviderInput]);
        AssertProviderExpectation(coLocatedPermutedLoaded, providerIdentity,
            consumerIdentity, 2, "ContainerA", "ContainerAKey");
        AssertProviderExpectation(coLocatedPermutedLoaded, providerIdentity,
            consumerIdentity, 2, "UsesLeaf", "UsesLeafKey");
        Assert.That(NativeModuleCompiler.ExecuteStandard(
            coLocatedPermutedInput, consumerIdentity, [leafProviderInput],
            NativeRuntimePolicy.FailFast), Is.EqualTo(99));
        var coLocatedPermutedReemitted
            = TrRmExecutableCompiler.EmitStandard(coLocatedPermutedLoaded,
                consumerIdentity, A2R1SchemaIdentity, 1);
        var coLocatedPermutedReemittedInput
            = new TrRmStandardArtifactInput(A2R1SchemaIdentity,
                coLocatedPermutedReemitted);
        var coLocatedPermutedReloaded
            = TrRmExecutableCompiler.LoadStandard(
                coLocatedPermutedReemittedInput, consumerIdentity, providers);
        AssertProviderExpectation(coLocatedPermutedReloaded,
            providerIdentity, consumerIdentity, 1, "ContainerA",
            "ContainerAKey");
        AssertProviderExpectation(coLocatedPermutedReloaded,
            providerIdentity, consumerIdentity, 1, "UsesLeaf",
            "UsesLeafKey");
        Assert.That(NativeModuleCompiler.ExecuteStandard(
            coLocatedPermutedReemittedInput, consumerIdentity, providers,
            NativeRuntimePolicy.FailFast), Is.EqualTo(99));
        var coLocatedPermutedImage
            = NativeModuleCompiler.EmitStandardExecutable(
                coLocatedPermutedInput, consumerIdentity,
                [leafProviderInput], NativeRuntimePolicy.FailFast);

        var shiftedCoLocatedProviderArtifact
            = CreateProvider(includePrefix: true,
                schemaIdentity: A2R1SchemaIdentity);
        var shiftedCoLocatedArtifact
            = CombineA2R1ConsumerAndTypeProviderArtifacts(
                globalPermutedConsumer,
                shiftedCoLocatedProviderArtifact);
        var shiftedCoLocatedInput = new TrRmStandardArtifactInput(
            A2R1SchemaIdentity, shiftedCoLocatedArtifact);
        var shiftedCoLocatedLoaded = TrRmExecutableCompiler.LoadStandard(
            shiftedCoLocatedInput, consumerIdentity, [leafProviderInput]);
        AssertProviderExpectation(shiftedCoLocatedLoaded, providerIdentity,
            consumerIdentity, 2, "ContainerA", "ContainerAKey");
        AssertProviderExpectation(shiftedCoLocatedLoaded, providerIdentity,
            consumerIdentity, 2, "UsesLeaf", "UsesLeafKey");
        Assert.That(NativeModuleCompiler.ExecuteStandard(
            shiftedCoLocatedInput, consumerIdentity, [leafProviderInput],
            NativeRuntimePolicy.FailFast), Is.EqualTo(99));
        var shiftedCoLocatedImage
            = NativeModuleCompiler.EmitStandardExecutable(
                shiftedCoLocatedInput, consumerIdentity,
                [leafProviderInput], NativeRuntimePolicy.FailFast);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(input, consumerIdentity,
                [leafProviderInput]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(coLocatedInput,
                consumerIdentity, []));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(coLocatedInput,
                consumerIdentity, [providerInput, leafProviderInput]));
        var incompatibleCoLocatedProviderArtifact
            = CreateProvider(includePrefix: false, arrayExtent: 3,
                schemaIdentity: A2R1SchemaIdentity);
        var incompatibleCoLocatedArtifact
            = CombineA2R1ConsumerAndTypeProviderArtifacts(
                globalPermutedConsumer,
                incompatibleCoLocatedProviderArtifact);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(A2R1SchemaIdentity,
                    incompatibleCoLocatedArtifact),
                consumerIdentity, [leafProviderInput]));

        var malformedTypeSpecRows
            = new ReadOnlyMemory<byte>[permutedTables.TypeSpecs.Count];
        var malformedTypeSpecChanged = false;
        for (var row = 1; row <= malformedTypeSpecRows.Length; row++)
        {
            var typeSpec = permutedTables.TypeSpecs.GetRow(row).ToArray();
            if (!malformedTypeSpecChanged && typeSpec[0] == 0x03)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(typeSpec.AsSpan(2),
                    checked((uint)permutedTables.Imports.Count + 1));
                malformedTypeSpecChanged = true;
            }
            malformedTypeSpecRows[row - 1] = typeSpec;
        }
        Assert.That(malformedTypeSpecChanged, Is.True);
        var malformedTypeSpecArtifact = ReplaceA2R1Sections(
            permutedArtifact,
            (0x0011, TrRmVariableTableWriter.EncodeBlobs(
                malformedTypeSpecRows)));
        var malformedTypeSpecInput = new TrRmStandardArtifactInput(
            A2R1SchemaIdentity, malformedTypeSpecArtifact);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(malformedTypeSpecInput,
                consumerIdentity, providers));

        var rootModuleRefRow = 0;
        var leafModuleRefRow = 0;
        for (var row = 1; row <= permutedTables.ModuleRefs.Count; row++)
        {
            var identity = permutedTables.ModuleRefs
                .GetProducerIdentity(row);
            if (identity == providerIdentity)
            {
                rootModuleRefRow = row;
            }
            else if (identity == "LeafProvider")
            {
                leafModuleRefRow = row;
            }
        }
        Assert.That(rootModuleRefRow, Is.Not.Zero);
        Assert.That(leafModuleRefRow, Is.Not.Zero);
        var splitImportRow = 0;
        for (var importRow = 1;
            importRow <= permutedTables.Imports.Count; importRow++)
        {
            if (permutedTables.Imports.GetModuleRefForImportRow(importRow)
                    != rootModuleRefRow
                || (permutedTables.Imports
                    .GetExpectedDefinitionForImportRow(importRow) >> 24)
                    != 0x13)
            {
                continue;
            }
            for (var typeSpecRow = 1;
                typeSpecRow <= permutedTables.TypeSpecs.Count; typeSpecRow++)
            {
                var typeSpec
                    = permutedTables.TypeSpecs.GetRow(typeSpecRow);
                if (typeSpec[0] == 0x03
                    && BinaryPrimitives.ReadUInt32LittleEndian(
                        typeSpec[2..]) == (uint)importRow)
                {
                    splitImportRow = importRow;
                    break;
                }
            }
            if (splitImportRow != 0)
            {
                break;
            }
        }
        Assert.That(splitImportRow, Is.Not.Zero);
        var importSection = GetA2R1Section(permutedArtifact, 0x001C);
        BinaryPrimitives.WriteUInt32LittleEndian(
            importSection.Bytes.AsSpan((splitImportRow - 1) * 12),
            checked((uint)leafModuleRefRow));
        var splitProviderArtifact = ReplaceA2R1Sections(permutedArtifact,
            (0x001C, importSection.Bytes));
        var splitProviderInput = new TrRmStandardArtifactInput(
            A2R1SchemaIdentity, splitProviderArtifact);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(splitProviderInput,
                consumerIdentity, providers));

        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(image, consumerIdentity, 99)
            .ConfigureAwait(true);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(permutedImage,
                $"{consumerIdentity}-permuted", 99)
            .ConfigureAwait(true);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(shiftedProviderImage,
                $"{consumerIdentity}-shifted-provider", 99)
            .ConfigureAwait(true);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(coLocatedImage,
                $"{consumerIdentity}-co-located", 99)
            .ConfigureAwait(true);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(coLocatedPermutedImage,
                $"{consumerIdentity}-co-located-permuted", 99)
            .ConfigureAwait(true);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(shiftedCoLocatedImage,
                $"{consumerIdentity}-co-located-shifted-provider", 99)
            .ConfigureAwait(true);
    }

    private static byte[] CreateSourceTypeLibraryArtifact(
        string moduleIdentity, string schemaIdentity, string source,
        IReadOnlyList<TrRmSourceTypeExport> typeExports,
        CompilationReference? reference = null)
    {
        var syntaxTree = SyntaxTree.Parse(source);
        var options = new CompilationOptions {
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        };
        var compilation = reference is null
            ? Compilation.CreateLibrary(syntaxTree, options)
            : Compilation.CreateLibrary(syntaxTree, reference, options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return TrRmExecutableCompiler.EmitStandard(compilation.Program!,
            moduleIdentity, schemaIdentity, 1, typeExports);
    }

    private static byte[] PermuteA2R1ConsumerGlobalRows(byte[] artifact)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft;
        var artifactIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                artifactIndex);
        Assert.That(tables.Modules.Count, Is.EqualTo(1));
        Assert.That(tables.GenericParams.Count, Is.Zero);

        static int[] ReverseRows(int count)
        {
            var newRowByOld = new int[count + 1];
            for (var oldRow = 1; oldRow <= count; oldRow++)
            {
                newRowByOld[oldRow] = count - oldRow + 1;
            }

            return newRowByOld;
        }

        static uint RemapTypeSpecRef(uint value, int[] newRowByOld)
        {
            if (value == 0 || (value & 0xFF00_0000) != 0)
            {
                return value;
            }

            var oldRow = checked((int)value);
            if ((uint)oldRow >= (uint)newRowByOld.Length)
            {
                throw new InvalidDataException(
                    "The A2R1 consumer TypeSpec permutation found an invalid row reference.");
            }

            return checked((uint)newRowByOld[oldRow]);
        }

        static int MapRow(int oldRow, int[] newRowByOld, string tableName)
        {
            if (oldRow <= 0 || oldRow >= newRowByOld.Length)
            {
                throw new InvalidDataException(
                    $"The A2R1 consumer {tableName} permutation found an invalid row reference.");
            }

            return newRowByOld[oldRow];
        }

        static (byte[] Bytes, int Count) GetSection(byte[] source,
            ushort requestedKind)
        {
            var sourceIndex = TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                    source);
            for (var sectionIndex = 0;
                sectionIndex < sourceIndex.SectionCount; sectionIndex++)
            {
                sourceIndex.GetSection(sectionIndex, out var kind,
                    out _, out var section, out var recordCount);
                if (kind == requestedKind)
                {
                    return (section.ToArray(), recordCount);
                }
            }

            throw new InvalidDataException(
                $"The A2R1 consumer permutation is missing section 0x{requestedKind:X4}.");
        }

        var methodDefRowByOld = ReverseRows(tables.Methods.Count);
        var oldMethodDefRowByNew = ReverseRows(tables.Methods.Count);
        var methodSigRowByOld = ReverseRows(tables.Signatures.Count);
        var oldMethodSigRowByNew = ReverseRows(tables.Signatures.Count);
        var moduleRefRowByOld = ReverseRows(tables.ModuleRefs.Count);
        var importRowByOld = ReverseRows(tables.Imports.Count);
        var nativeImportRowByOld
            = ReverseRows(tables.StandardNativeImports.Count);

        var leafTypeSpecRows = new List<int>(tables.TypeSpecs.Count);
        var dependentTypeSpecRows = new List<int>(tables.TypeSpecs.Count);
        for (var oldRow = 1; oldRow <= tables.TypeSpecs.Count; oldRow++)
        {
            var form = tables.TypeSpecs.GetRow(oldRow)[0];
            if (form is 0x01 or 0x02 or 0x03 or 0x04 or 0x0D)
            {
                leafTypeSpecRows.Add(oldRow);
            }
            else
            {
                dependentTypeSpecRows.Add(oldRow);
            }
        }
        leafTypeSpecRows.Reverse();
        var oldTypeSpecRowByNew
            = new int[tables.TypeSpecs.Count + 1];
        var typeSpecRowByOld = new int[tables.TypeSpecs.Count + 1];
        var nextTypeSpecRow = 1;
        foreach (var oldRow in leafTypeSpecRows)
        {
            oldTypeSpecRowByNew[nextTypeSpecRow] = oldRow;
            typeSpecRowByOld[oldRow] = nextTypeSpecRow++;
        }
        foreach (var oldRow in dependentTypeSpecRows)
        {
            oldTypeSpecRowByNew[nextTypeSpecRow] = oldRow;
            typeSpecRowByOld[oldRow] = nextTypeSpecRow++;
        }
        Assert.That(typeSpecRowByOld.Skip(1).Where((newRow, index)
                => newRow != index + 1), Is.Not.Empty);

        uint RemapSpec(uint typeSpecRef)
            => RemapTypeSpecRef(typeSpecRef, typeSpecRowByOld);

        var typeSpecRows = new ReadOnlyMemory<byte>[tables.TypeSpecs.Count];
        for (var newRow = 1; newRow <= typeSpecRows.Length; newRow++)
        {
            var oldRow = oldTypeSpecRowByNew[newRow];
            var row = tables.TypeSpecs.GetRow(oldRow);
            if (tables.TypeSpecs.TryGetFixedInlineArray(
                    checked((uint)oldRow), out var elementTypeSpecRef,
                    out var elementCount))
            {
                typeSpecRows[newRow - 1]
                    = TrRmTypeSpecTableWriter.FixedInlineArray(
                        RemapSpec(elementTypeSpecRef), elementCount);
                continue;
            }

            var form = row[0];
            switch (form)
            {
                case 0x03:
                {
                    var oldImportRow = checked((int)
                        BinaryPrimitives.ReadUInt32LittleEndian(row[2..]));
                    typeSpecRows[newRow - 1]
                        = TrRmTypeSpecTableWriter.Reference(form,
                            checked((uint)MapRow(oldImportRow,
                                importRowByOld, "Import")), row[1]);
                    break;
                }

                case 0x05:
                {
                    if (row.Length < 10
                        || (row.Length - 6) % sizeof(uint) != 0)
                    {
                        throw new InvalidDataException(
                            "The A2R1 consumer permutation found a malformed constructed TypeSpec.");
                    }

                    var argumentCount
                        = (row.Length - 6) / sizeof(uint);
                    var arguments = new uint[argumentCount];
                    for (var argument = 0; argument < argumentCount;
                        argument++)
                    {
                        arguments[argument]
                            = RemapSpec(BinaryPrimitives
                                .ReadUInt32LittleEndian(row[(6
                                    + (argument * sizeof(uint)))..]));
                    }

                    typeSpecRows[newRow - 1]
                        = TrRmTypeSpecTableWriter.Constructed(
                            RemapSpec(BinaryPrimitives
                                .ReadUInt32LittleEndian(row[2..])),
                            arguments, row[1]);
                    break;
                }

                case >= 0x06 and <= 0x0B:
                    typeSpecRows[newRow - 1]
                        = TrRmTypeSpecTableWriter.Wrapped(form,
                            RemapSpec(BinaryPrimitives
                                .ReadUInt32LittleEndian(row[2..])),
                            row[1]);
                    break;

                case 0x0C:
                    typeSpecRows[newRow - 1]
                        = TrRmTypeSpecTableWriter.IndexedFamily(row[2],
                            RemapSpec(BinaryPrimitives
                                .ReadUInt32LittleEndian(row[3..])),
                            row[1]);
                    break;

                case 0x0D:
                {
                    var oldAnchor = checked((int)
                        BinaryPrimitives.ReadUInt32LittleEndian(row[3..]));
                    typeSpecRows[newRow - 1]
                        = TrRmTypeSpecTableWriter.NativeFunctionPointer(
                            row[2], checked((uint)MapRow(oldAnchor,
                                methodDefRowByOld, "MethodDef")));
                    break;
                }

                case 0x01 or 0x02 or 0x04 or 0x0F or 0x10:
                    typeSpecRows[newRow - 1] = row.ToArray();
                    break;

                default:
                    throw new NotSupportedException(
                        $"The A2R1 consumer TypeSpec form 0x{form:X2} is outside this permutation.");
            }
        }

        var (moduleRefSection, moduleRefCount)
            = GetSection(artifact, 0x0010);
        Assert.That(moduleRefCount, Is.EqualTo(tables.ModuleRefs.Count));
        var moduleRefBytes = new byte[moduleRefSection.Length];
        for (var oldRow = 1; oldRow <= moduleRefCount; oldRow++)
        {
            var newRow = moduleRefRowByOld[oldRow];
            moduleRefSection.AsSpan((oldRow - 1) * 12, 12)
                .CopyTo(moduleRefBytes.AsSpan((newRow - 1) * 12));
        }

        var (importSection, importCount) = GetSection(artifact, 0x001C);
        Assert.That(importCount, Is.EqualTo(tables.Imports.Count));
        var importBytes = new byte[importSection.Length];
        for (var oldRow = 1; oldRow <= importCount; oldRow++)
        {
            var newRow = importRowByOld[oldRow];
            var sourceOffset = (oldRow - 1) * 12;
            var destination = importBytes.AsSpan((newRow - 1) * 12, 12);
            importSection.AsSpan(sourceOffset, 12).CopyTo(destination);
            var oldModuleRef = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    importSection.AsSpan(sourceOffset, sizeof(uint))));
            BinaryPrimitives.WriteUInt32LittleEndian(destination,
                checked((uint)MapRow(oldModuleRef, moduleRefRowByOld,
                    "ModuleRef")));
        }

        var (fieldSection, fieldDefCount) = GetSection(artifact, 0x0014);
        Assert.That(fieldDefCount, Is.EqualTo(tables.Fields.Count));
        var fieldBytes = fieldSection.ToArray();
        for (var row = 1; row <= fieldDefCount; row++)
        {
            var offset = ((row - 1) * 16) + 8;
            var oldTypeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(
                fieldBytes.AsSpan(offset));
            BinaryPrimitives.WriteUInt32LittleEndian(fieldBytes.AsSpan(offset),
                RemapSpec(oldTypeSpecRef));
        }

        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            tables.TypeDefs.Count, tables.Imports.Count,
            tables.GenericParams.Count, 0, schema,
            methodDefCount: tables.Methods.Count,
            predicateCaptureCount: tables.PredicateCaptures.Count);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length, tables.TypeDefs.Count, tables.Imports.Count,
            tables.GenericParams.Count, 0, schema, tables.Methods.Count,
            tables.PredicateCaptures.Count);

        var parameterRows = new List<TrRmParamRow>(
            tables.Signatures.ParamCount);
        var signatureRows = new List<TrRmMethodSigRow>(
            tables.Signatures.Count);
        for (var newSignatureRow = 1;
            newSignatureRow <= tables.Signatures.Count;
            newSignatureRow++)
        {
            var oldSignatureRow
                = oldMethodSigRowByNew[newSignatureRow];
            var (parameterStart, parameterCount)
                = tables.Signatures.GetParamRange(oldSignatureRow);
            var (genericFirst, genericCount)
                = tables.Signatures.GetGenericRange(oldSignatureRow);
            Assert.That(genericCount, Is.Zero);
            Assert.That(genericFirst, Is.Zero);
            var newParameterFirst = parameterCount == 0
                ? 0 : parameterRows.Count + 1;
            signatureRows.Add(new TrRmMethodSigRow(
                RemapSpec(tables.Signatures.GetResultTypeSpec(
                    oldSignatureRow)),
                RemapSpec(tables.Signatures.GetReceiverTypeSpec(
                    oldSignatureRow)),
                newParameterFirst, parameterCount, 0, 0));
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var oldParameterRow = parameterStart + ordinal;
                parameterRows.Add(new TrRmParamRow(newSignatureRow, ordinal,
                    RemapSpec(tables.Parameters.GetTypeSpec(
                        oldParameterRow)),
                    tables.Parameters.GetMode(oldParameterRow),
                    tables.Parameters.GetFlags(oldParameterRow)));
            }
        }
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, parameterRows.Count, tables.GenericParams.Count);
        var parameterBytes = new byte[parameterRows.Count * 16];
        for (var parameterIndex = 0;
            parameterIndex < parameterRows.Count; parameterIndex++)
        {
            var row = parameterRows[parameterIndex];
            var offset = parameterIndex * 16;
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterBytes.AsSpan(offset),
                checked((uint)row.OwnerMethodSigRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterBytes.AsSpan(offset + 4),
                checked((uint)row.Ordinal));
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterBytes.AsSpan(offset + 8), row.TypeSpecRef);
            parameterBytes[offset + 12] = row.Mode;
            parameterBytes[offset + 13] = row.Flags;
        }

        var (methodSection, methodCount) = GetSection(artifact, 0x0016);
        Assert.That(methodCount, Is.EqualTo(tables.Methods.Count));
        var methodBytes = new byte[methodSection.Length];
        var contractFirstByOldMethod = new int[methodCount + 1];
        var contractCountByOldMethod = new int[methodCount + 1];
        var oldMethodRowByNew = ReverseRows(methodCount);

        var (contractSection, contractCount) = GetSection(artifact, 0x001B);
        Assert.That(contractCount, Is.EqualTo(tables.Contracts.Count));
        var contractRows = new byte[contractCount + 1][];
        var contractTable = new TrRmVariableTableIndex(contractSection,
            contractCount, 8, 4);
        for (var row = 1; row <= contractCount; row++)
        {
            contractRows[row] = contractTable.GetRow(row).ToArray();
        }

        var reorderedContracts = new List<byte[]>(contractCount);
        for (var newMethodRow = 1; newMethodRow <= methodCount;
            newMethodRow++)
        {
            var oldMethodRow = oldMethodRowByNew[newMethodRow];
            var (oldContractFirst, oldContractCount)
                = tables.Methods.GetContractRange(oldMethodRow);
            contractCountByOldMethod[oldMethodRow] = oldContractCount;
            if (oldContractCount == 0)
            {
                continue;
            }

            contractFirstByOldMethod[oldMethodRow]
                = reorderedContracts.Count + 1;
            for (var offset = 0; offset < oldContractCount; offset++)
            {
                var row = contractRows[oldContractFirst + offset].ToArray();
                var owner = BinaryPrimitives.ReadUInt32LittleEndian(row);
                if ((owner >> 24) != 0x16
                    || (owner & 0x00FF_FFFF)
                        != (uint)oldMethodRow)
                {
                    throw new InvalidDataException(
                        "The A2R1 consumer permutation found a non-method Contract owner.");
                }
                BinaryPrimitives.WriteUInt32LittleEndian(row,
                    0x1600_0000u
                        | checked((uint)methodDefRowByOld[oldMethodRow]));
                reorderedContracts.Add(row);
            }
        }
        Assert.That(reorderedContracts, Has.Count.EqualTo(contractCount));
        var contractIndexLength
            = checked((reorderedContracts.Count + 1) * sizeof(uint));
        var contractDataLength = reorderedContracts.Sum(
            static row => row.Length);
        var contractBytes = new byte[
            checked(contractIndexLength + contractDataLength)];
        var contractDataOffset = contractIndexLength;
        for (var row = 0; row < reorderedContracts.Count; row++)
        {
            var contractRow = reorderedContracts[row];
            if ((contractRow.Length & 3) != 0)
            {
                throw new InvalidDataException(
                    "The A2R1 consumer Contract row is not four-byte aligned.");
            }
            BinaryPrimitives.WriteUInt32LittleEndian(
                contractBytes.AsSpan(row * sizeof(uint)),
                checked((uint)(contractDataOffset - contractIndexLength)));
            contractRow.CopyTo(contractBytes.AsSpan(contractDataOffset));
            contractDataOffset += contractRow.Length;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(
            contractBytes.AsSpan(reorderedContracts.Count * sizeof(uint)),
            checked((uint)(contractDataOffset - contractIndexLength)));

        for (var oldMethodRow = 1; oldMethodRow <= methodCount;
            oldMethodRow++)
        {
            var newMethodRow = methodDefRowByOld[oldMethodRow];
            var source = methodSection.AsSpan((oldMethodRow - 1) * 28, 28);
            var destination = methodBytes.AsSpan((newMethodRow - 1) * 28, 28);
            source.CopyTo(destination);
            var oldSignatureRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(source[8..]));
            BinaryPrimitives.WriteUInt32LittleEndian(destination[8..],
                checked((uint)methodSigRowByOld[oldSignatureRow]));
            BinaryPrimitives.WriteUInt32LittleEndian(
                destination[16..],
                checked((uint)contractFirstByOldMethod[oldMethodRow]));
            BinaryPrimitives.WriteUInt32LittleEndian(
                destination[20..],
                checked((uint)contractCountByOldMethod[oldMethodRow]));
        }

        var (moduleSection, moduleCount) = GetSection(artifact, 0x0001);
        Assert.That(moduleCount, Is.EqualTo(tables.Modules.Count));
        var moduleBytes = moduleSection.ToArray();
        for (var moduleRow = 1; moduleRow <= moduleCount; moduleRow++)
        {
            var oldEntryMethod = tables.Modules.GetEntryMethod(moduleRow);
            if (oldEntryMethod != 0)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    moduleBytes.AsSpan(((moduleRow - 1) * 48) + 16),
                    checked((uint)methodDefRowByOld[oldEntryMethod]));
            }
        }

        var (nativeImportSection, nativeImportCount)
            = GetSection(artifact, 0x0021);
        Assert.That(nativeImportCount,
            Is.EqualTo(tables.StandardNativeImports.Count));
        var nativeImportBytes = new byte[nativeImportSection.Length];
        for (var oldRow = 1; oldRow <= nativeImportCount; oldRow++)
        {
            var newRow = nativeImportRowByOld[oldRow];
            var source = nativeImportSection.AsSpan(
                (oldRow - 1) * 16, 16);
            var destination = nativeImportBytes.AsSpan(
                (newRow - 1) * 16, 16);
            source.CopyTo(destination);
            if (source[0] == 1)
            {
                var oldOwner = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(source[4..]));
                BinaryPrimitives.WriteUInt32LittleEndian(destination[4..],
                    checked((uint)methodDefRowByOld[oldOwner]));
            }
        }

        var fixedArrayRows = new TrRmFixedArrayLayoutRow[
            tables.FixedArrayLayouts.Count];
        for (var row = 1; row <= fixedArrayRows.Length; row++)
        {
            var layout = tables.FixedArrayLayouts.GetRow(row);
            fixedArrayRows[row - 1] = layout with {
                ArrayTypeSpecRow
                    = typeSpecRowByOld[layout.ArrayTypeSpecRow],
            };
        }
        var fixedArrayLayoutBytes
            = TrRmFixedArrayLayoutTableWriter.Encode(fixedArrayRows,
                typeSpecs, tables.TargetProfiles, tables.Features,
                tables.Modules, schema);

        byte[]? fieldPathBytes = null;
        if (tables.FieldPaths.Count > 0)
        {
            var fieldPathRows = new ReadOnlyMemory<byte>[
                tables.FieldPaths.Count];
            for (var row = 1; row <= tables.FieldPaths.Count; row++)
            {
                var fieldCount = tables.FieldPaths.GetFieldCount(row);
                var rowBytes = new byte[8 + (fieldCount * sizeof(uint))];
                BinaryPrimitives.WriteUInt32LittleEndian(rowBytes,
                    RemapTypeSpecRef(
                        tables.FieldPaths.GetRootTypeSpecRef(row),
                        typeSpecRowByOld));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    rowBytes.AsSpan(4), checked((uint)fieldCount));
                for (var ordinal = 0; ordinal < fieldCount; ordinal++)
                {
                    var reference
                        = tables.FieldPaths.GetFieldReference(row, ordinal);
                    var kind = reference >> 24;
                    var fieldRow = checked((int)(reference
                        & 0x00FF_FFFF));
                    var remapped = kind switch {
                        0x14 => reference,
                        0x1C => 0x1C00_0000u
                            | checked((uint)MapRow(fieldRow,
                                importRowByOld, "Import")),
                        _ => throw new InvalidDataException(
                            $"The A2R1 consumer FieldPath token kind 0x{kind:X2} is not remappable."),
                    };
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        rowBytes.AsSpan(8 + (ordinal * sizeof(uint))),
                        remapped);
                }
                fieldPathRows[row - 1] = rowBytes;
            }
            fieldPathBytes
                = TrRmVariableTableWriter.EncodeBlobs(fieldPathRows);
        }

        var bodyWithRemappedMaps
            = TrRmForeignInvocationAddressEmitterTests
                .RewriteA2BodyGlobalReferences(artifact, "Main",
                    (_, typeMap, methodMap, fieldMap, constantMap, _, _) =>
                    {
                        for (var offset = 0; offset < typeMap.Length;
                            offset += sizeof(uint))
                        {
                            var token
                                = BinaryPrimitives.ReadUInt32LittleEndian(
                                    typeMap.AsSpan(offset));
                            var kind = token >> 24;
                            var row = checked((int)(token & 0x00FF_FFFF));
                            var remapped = kind switch {
                                0x11 => 0x1100_0000u
                                    | checked((uint)MapRow(row,
                                        typeSpecRowByOld, "TypeSpec")),
                                0x1C => 0x1C00_0000u
                                    | checked((uint)MapRow(row,
                                        importRowByOld, "Import")),
                                0x13 or 0xF2 or 0xF3 => token,
                                _ => throw new InvalidDataException(
                                    $"The A2R1 consumer TypeMap token kind 0x{kind:X2} is not remappable."),
                            };
                            BinaryPrimitives.WriteUInt32LittleEndian(
                                typeMap.AsSpan(offset), remapped);
                        }

                        for (var offset = 0; offset < methodMap.Length;
                            offset += sizeof(uint))
                        {
                            var token
                                = BinaryPrimitives.ReadUInt32LittleEndian(
                                    methodMap.AsSpan(offset));
                            var kind = token >> 24;
                            var row = checked((int)(token & 0x00FF_FFFF));
                            var remapped = kind switch {
                                0x16 => 0x1600_0000u
                                    | checked((uint)MapRow(row,
                                        methodDefRowByOld, "MethodDef")),
                                0x1C => 0x1C00_0000u
                                    | checked((uint)MapRow(row,
                                        importRowByOld, "Import")),
                                _ => throw new InvalidDataException(
                                    $"The A2R1 consumer MethodMap token kind 0x{kind:X2} is not remappable."),
                            };
                            BinaryPrimitives.WriteUInt32LittleEndian(
                                methodMap.AsSpan(offset), remapped);
                        }

                        for (var offset = 0; offset < fieldMap.Length;
                            offset += sizeof(uint))
                        {
                            var token
                                = BinaryPrimitives.ReadUInt32LittleEndian(
                                    fieldMap.AsSpan(offset));
                            var kind = token >> 24;
                            var row = checked((int)(token & 0x00FF_FFFF));
                            var remapped = kind switch {
                                0x1C => 0x1C00_0000u
                                    | checked((uint)MapRow(row,
                                        importRowByOld, "Import")),
                                0x14 or 0x1D => token,
                                _ => throw new InvalidDataException(
                                    $"The A2R1 consumer FieldMap token kind 0x{kind:X2} is not remappable."),
                            };
                            BinaryPrimitives.WriteUInt32LittleEndian(
                                fieldMap.AsSpan(offset), remapped);
                        }

                        for (var offset = 0; offset < constantMap.Length;
                            offset += 8)
                        {
                            var typeSpecRef
                                = BinaryPrimitives.ReadUInt32LittleEndian(
                                    constantMap.AsSpan(offset));
                            BinaryPrimitives.WriteUInt32LittleEndian(
                                constantMap.AsSpan(offset),
                                RemapTypeSpecRef(typeSpecRef,
                                    typeSpecRowByOld));
                        }
                    }, isRepeatInitialization: true);

        var replacements = new List<(ushort Kind, byte[] Bytes)> {
            (0x0001, moduleBytes),
            (0x0010, moduleRefBytes),
            (0x0011, typeSpecBytes),
            (0x0012, fixedArrayLayoutBytes),
            (0x0014, fieldBytes),
            (0x0016, methodBytes),
            (0x0017, signatureBytes),
            (0x0018, parameterBytes),
            (0x001B, contractBytes),
            (0x001C, importBytes),
            (0x0021, nativeImportBytes),
        };
        if (fieldPathBytes is not null)
        {
            replacements.Add((0x0019, fieldPathBytes));
        }

        var permutedArtifact = ReplaceA2R1Sections(bodyWithRemappedMaps,
            [.. replacements]);
        var permutedTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                        permutedArtifact));

        static void AssertMappedBodyMap(ReadOnlySpan<byte> original,
            ReadOnlySpan<byte> permuted, Func<uint, uint> remap)
        {
            Assert.That(permuted.Length, Is.EqualTo(original.Length));
            Assert.That(original.Length % sizeof(uint), Is.Zero);
            for (var offset = 0; offset < original.Length;
                offset += sizeof(uint))
            {
                var originalToken = BinaryPrimitives
                    .ReadUInt32LittleEndian(original[offset..]);
                var expected = remap(originalToken);
                var actual = BinaryPrimitives
                    .ReadUInt32LittleEndian(permuted[offset..]);
                Assert.That(actual, Is.EqualTo(expected));
            }
        }

        var originalMainRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                originalMainRow = row;
                break;
            }
        }
        Assert.That(originalMainRow, Is.Not.Zero);
        var permutedMainRow = methodDefRowByOld[originalMainRow];
        var originalMainBody = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(originalMainRow),
            tables.Contracts.Count);
        var permutedMainBody = permutedTables.Bodies.GetRegisterBody(
            permutedTables.Methods.GetBodyRow(permutedMainRow),
            permutedTables.Contracts.Count);
        AssertMappedBodyMap(originalMainBody.TypeMap,
            permutedMainBody.TypeMap, token => {
                var kind = token >> 24;
                var row = checked((int)(token & 0x00FF_FFFF));
                return kind switch {
                    0x11 => 0x1100_0000u
                        | checked((uint)MapRow(row, typeSpecRowByOld,
                            "TypeSpec")),
                    0x1C => 0x1C00_0000u
                        | checked((uint)MapRow(row, importRowByOld,
                            "Import")),
                    0x13 or 0xF2 or 0xF3 => token,
                    _ => throw new InvalidDataException(
                        $"The A2R1 consumer TypeMap token kind 0x{kind:X2} is not remappable."),
                };
            });
        AssertMappedBodyMap(originalMainBody.MethodMap,
            permutedMainBody.MethodMap, token => {
                var kind = token >> 24;
                var row = checked((int)(token & 0x00FF_FFFF));
                return kind switch {
                    0x16 => 0x1600_0000u
                        | checked((uint)MapRow(row, methodDefRowByOld,
                            "MethodDef")),
                    0x1C => 0x1C00_0000u
                        | checked((uint)MapRow(row, importRowByOld,
                            "Import")),
                    _ => throw new InvalidDataException(
                        $"The A2R1 consumer MethodMap token kind 0x{kind:X2} is not remappable."),
                };
            });
        AssertMappedBodyMap(originalMainBody.FieldMap,
            permutedMainBody.FieldMap, token => {
                var kind = token >> 24;
                var row = checked((int)(token & 0x00FF_FFFF));
                return kind switch {
                    0x1C => 0x1C00_0000u
                        | checked((uint)MapRow(row, importRowByOld,
                            "Import")),
                    0x14 or 0x1D => token,
                    _ => throw new InvalidDataException(
                        $"The A2R1 consumer FieldMap token kind 0x{kind:X2} is not remappable."),
                };
            });
        Assert.That(permutedMainBody.ConstantMap.Length,
            Is.EqualTo(originalMainBody.ConstantMap.Length));
        Assert.That(originalMainBody.ConstantMap.Length,
            Is.GreaterThan(0));
        Assert.That(originalMainBody.ConstantMap.Length % 8, Is.Zero);
        for (var offset = 0;
            offset < originalMainBody.ConstantMap.Length; offset += 8)
        {
            var originalTypeSpec = BinaryPrimitives
                .ReadUInt32LittleEndian(
                    originalMainBody.ConstantMap[offset..]);
            var permutedTypeSpec = BinaryPrimitives
                .ReadUInt32LittleEndian(
                    permutedMainBody.ConstantMap[offset..]);
            Assert.That(permutedTypeSpec,
                Is.EqualTo(RemapTypeSpecRef(originalTypeSpec,
                    typeSpecRowByOld)));
            Assert.That(permutedMainBody.ConstantMap.Slice(offset + 4, 4)
                .SequenceEqual(originalMainBody.ConstantMap.Slice(
                    offset + 4, 4)), Is.True);
        }

        return permutedArtifact;
    }

    private static byte[] CombineA2R1ConsumerAndTypeProviderArtifacts(
        byte[] consumerArtifact, byte[] providerArtifact)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft;
        var consumerIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                consumerArtifact);
        var providerIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                providerArtifact);
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                consumerIndex);
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                providerIndex);
        Assert.That(consumerTables.Modules.Count, Is.EqualTo(1));
        Assert.That(providerTables.Modules.Count, Is.EqualTo(1));
        Assert.That(providerTables.TypeDefs.Count, Is.GreaterThan(0));
        Assert.That(providerTables.Fields.Count, Is.GreaterThan(0));
        Assert.That(providerTables.Methods.Count, Is.Zero);
        Assert.That(providerTables.Signatures.Count, Is.Zero);
        Assert.That(providerTables.Contracts.Count, Is.Zero);
        Assert.That(providerTables.Bodies.Count, Is.Zero);
        Assert.That(providerTables.StandardNativeImports.Count, Is.Zero);
        Assert.That(providerTables.GenericParams.Count, Is.Zero);
        Assert.That(providerTables.Modules.SourceFileCount, Is.Zero);
        Assert.That(consumerTables.Modules.SourceFileCount, Is.Zero);

        static (byte[] Bytes, int Count, ushort Flags) GetSection(
            TrRmArtifactIndex index, ushort requestedKind)
        {
            for (var sectionIndex = 0;
                sectionIndex < index.SectionCount; sectionIndex++)
            {
                index.GetSection(sectionIndex, out var kind, out var flags,
                    out var bytes, out var recordCount);
                if (kind == requestedKind)
                {
                    return (bytes.ToArray(), recordCount, flags);
                }
            }

            return ([], 0, 0);
        }

        static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
            => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);

        static void WriteUInt32(Span<byte> bytes, int offset, uint value)
            => BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..],
                value);

        static uint RemapTypeSpecRef(uint typeSpecRef, int typeSpecOffset)
            => typeSpecRef != 0
                && (typeSpecRef & 0xFF00_0000u) == 0
                    ? checked(typeSpecRef + (uint)typeSpecOffset)
                    : typeSpecRef;

        static uint RemapDefinition(uint definition, int typeDefOffset,
            int fieldDefOffset)
        {
            var kind = definition >> 24;
            var row = definition & 0x00FF_FFFFu;
            var rowOffset = kind switch {
                0x13 => typeDefOffset,
                0x14 => fieldDefOffset,
                _ => throw new NotSupportedException(
                    $"The co-located type-only provider Import/Export kind 0x{kind:X2} is unsupported."),
            };
            return (definition & 0xFF00_0000u)
                | checked(row + (uint)rowOffset);
        }

        static ReadOnlyMemory<byte> RemapTypeSpecRow(
            TrRmTypeSpecTableReader typeSpecs, int row,
            int typeDefOffset, int importOffset, int typeSpecOffset,
            int genericParamOffset, int methodDefOffset)
        {
            var data = typeSpecs.GetRow(row);
            if (typeSpecs.TryGetFixedInlineArray(checked((uint)row),
                    out var elementTypeSpecRef, out var elementCount))
            {
                return TrRmTypeSpecTableWriter.FixedInlineArray(
                    RemapTypeSpecRef(elementTypeSpecRef, typeSpecOffset),
                    elementCount);
            }

            var form = data[0];
            switch (form)
            {
                case 0x01 or 0x0F:
                    return data.ToArray();

                case 0x02 or 0x03 or 0x04:
                {
                    var rowOffset = form switch {
                        0x02 => typeDefOffset,
                        0x03 => importOffset,
                        _ => genericParamOffset,
                    };
                    var referencedRow = checked(
                        ReadUInt32(data, 2) + (uint)rowOffset);
                    return TrRmTypeSpecTableWriter.Reference(form,
                        referencedRow, data[1]);
                }

                case 0x05:
                {
                    var argumentCount = (data.Length - 6)
                        / sizeof(uint);
                    var arguments = new uint[argumentCount];
                    for (var argument = 0; argument < argumentCount;
                        argument++)
                    {
                        arguments[argument] = RemapTypeSpecRef(
                            ReadUInt32(data,
                                6 + (argument * sizeof(uint))),
                            typeSpecOffset);
                    }

                    return TrRmTypeSpecTableWriter.Constructed(
                        RemapTypeSpecRef(ReadUInt32(data, 2),
                            typeSpecOffset),
                        arguments, data[1]);
                }

                case >= 0x06 and <= 0x0B:
                    return TrRmTypeSpecTableWriter.Wrapped(form,
                        RemapTypeSpecRef(ReadUInt32(data, 2),
                            typeSpecOffset), data[1]);

                case 0x0C:
                    return TrRmTypeSpecTableWriter.IndexedFamily(data[2],
                        RemapTypeSpecRef(ReadUInt32(data, 3),
                            typeSpecOffset), data[1]);

                case 0x0D:
                    return TrRmTypeSpecTableWriter.NativeFunctionPointer(
                        data[2], checked(ReadUInt32(data, 3)
                            + (uint)methodDefOffset));

                case 0x10:
                    return TrRmTypeSpecTableWriter.PredicateCapturedType(
                        checked(ReadUInt32(data, 2)
                            + (uint)genericParamOffset));

                default:
                    throw new NotSupportedException(
                        $"The co-located type-only provider TypeSpec form 0x{form:X2} is unsupported.");
            }
        }

        var supportedProviderSections = new HashSet<ushort> {
            0x0001, 0x0002, 0x0004, 0x0010, 0x0011, 0x0012, 0x0013,
            0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x001B, 0x001C,
            0x001D, 0x0020, 0x0021, 0x0023, 0x0048,
        };
        for (var sectionIndex = 0;
            sectionIndex < providerIndex.SectionCount; sectionIndex++)
        {
            providerIndex.GetSection(sectionIndex, out var kind, out _,
                out _, out var recordCount);
            if (recordCount != 0
                && !supportedProviderSections.Contains(kind))
            {
                throw new NotSupportedException(
                    $"The co-located type-only provider section 0x{kind:X4} is unsupported.");
            }
        }

        var consumerStringSection = GetSection(consumerIndex, 0x0002);
        var providerStringSection = GetSection(providerIndex, 0x0002);
        var consumerStringIndex = new TrRmVariableTableIndex(
            consumerStringSection.Bytes, consumerStringSection.Count, 8, 4);
        var providerStringIndex = new TrRmVariableTableIndex(
            providerStringSection.Bytes, providerStringSection.Count, 8, 4);
        var stringRows = new ReadOnlyMemory<byte>[
            consumerStringSection.Count + providerStringSection.Count];
        for (var row = 1; row <= consumerStringSection.Count; row++)
        {
            stringRows[row - 1] = consumerStringIndex.GetRow(row).ToArray();
        }
        for (var row = 1; row <= providerStringSection.Count; row++)
        {
            stringRows[consumerStringSection.Count + row - 1]
                = providerStringIndex.GetRow(row).ToArray();
        }
        var stringBytes = TrRmVariableTableWriter.EncodeBlobs(stringRows);
        var strings = new TrRmStringTableReader(stringBytes,
            stringRows.Length);

        var consumerFeatureSection = GetSection(consumerIndex, 0x0004);
        var providerFeatureSection = GetSection(providerIndex, 0x0004);
        var featureBytes = new byte[consumerFeatureSection.Bytes.Length
            + providerFeatureSection.Bytes.Length];
        consumerFeatureSection.Bytes.CopyTo(featureBytes, 0);
        providerFeatureSection.Bytes.CopyTo(featureBytes,
            consumerFeatureSection.Bytes.Length);
        var featureCount = consumerFeatureSection.Count
            + providerFeatureSection.Count;
        var features = new TrRmFeatureTableReader(featureBytes,
            featureCount, schema);

        var targetProfileRows = new List<TrRmTargetProfileRow>(
            consumerTables.TargetProfiles.Count
                + providerTables.TargetProfiles.Count);
        var targetProfileRowByIdentity
            = new Dictionary<(uint ProfileId, uint RuleRevision), int>();
        var providerTargetProfileRowByOld
            = new int[providerTables.TargetProfiles.Count + 1];
        for (var row = 1; row <= consumerTables.TargetProfiles.Count; row++)
        {
            var profile = consumerTables.TargetProfiles.GetProfile(row);
            var targetRow = targetProfileRows.Count + 1;
            targetProfileRows.Add(profile);
            targetProfileRowByIdentity.Add(
                (profile.ProfileId, profile.RuleRevision), targetRow);
        }
        for (var row = 1; row <= providerTables.TargetProfiles.Count; row++)
        {
            var profile = providerTables.TargetProfiles.GetProfile(row);
            if (!targetProfileRowByIdentity.TryGetValue(
                    (profile.ProfileId, profile.RuleRevision),
                    out var targetRow))
            {
                targetRow = targetProfileRows.Count + 1;
                targetProfileRows.Add(profile);
                targetProfileRowByIdentity.Add(
                    (profile.ProfileId, profile.RuleRevision), targetRow);
            }

            providerTargetProfileRowByOld[row] = targetRow;
        }
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode(
            targetProfileRows, schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, targetProfileRows.Count, schema);

        var consumerModuleSection = GetSection(consumerIndex, 0x0001);
        var providerModuleSection = GetSection(providerIndex, 0x0001);
        var consumerModuleIdentityStringRow = checked((int)ReadUInt32(
            consumerModuleSection.Bytes, 0));
        var (consumerTargetRequirement, consumerRuntimePolicy)
            = consumerTables.Modules.GetRequirements(1);
        var (consumerModuleRefStart, _) = consumerTables.Modules
            .GetModuleRefRange(1);
        var (consumerTypeDefStart, _) = consumerTables.Modules
            .GetTypeDefRange(1);
        var (consumerMethodStart, _) = consumerTables.Modules
            .GetMethodDefRange(1);
        var (consumerSourceFileStart, _) = consumerTables.Modules
            .GetSourceFileRange(1);
        var (consumerFeatureStart, consumerModuleFeatureCount)
            = consumerTables.Modules.GetFeatureRange(1);
        var providerModuleIdentityStringRow = checked((int)ReadUInt32(
            providerModuleSection.Bytes, 0)
            + consumerStringSection.Count);
        var (providerTargetRequirement, providerRuntimePolicy)
            = providerTables.Modules.GetRequirements(1);
        var (providerModuleRefStart, _) = providerTables.Modules
            .GetModuleRefRange(1);
        var (providerTypeDefStart, _) = providerTables.Modules
            .GetTypeDefRange(1);
        var (providerMethodStart, _) = providerTables.Modules
            .GetMethodDefRange(1);
        var (providerSourceFileStart, _) = providerTables.Modules
            .GetSourceFileRange(1);
        var (providerFeatureStart, providerModuleFeatureCount)
            = providerTables.Modules.GetFeatureRange(1);
        var providerEntryMethod = providerTables.Modules.GetEntryMethod(1);
        if (providerEntryMethod != 0)
        {
            providerEntryMethod += consumerTables.Methods.Count;
        }
        var moduleRows = new[] {
            new TrRmModuleRow(consumerModuleIdentityStringRow,
                consumerTargetRequirement, consumerRuntimePolicy,
                consumerTables.Modules.GetEntryMethod(1),
                consumerModuleRefStart, consumerTypeDefStart,
                consumerMethodStart, consumerSourceFileStart,
                consumerFeatureStart, consumerModuleFeatureCount,
                consumerTables.Modules.GetGoverningRuleEdition(1)),
            new TrRmModuleRow(providerModuleIdentityStringRow,
                providerTargetRequirement, providerRuntimePolicy,
                providerEntryMethod,
                providerModuleRefStart + consumerTables.ModuleRefs.Count,
                providerTypeDefStart + consumerTables.TypeDefs.Count,
                providerMethodStart + consumerTables.Methods.Count,
                providerSourceFileStart
                    + consumerTables.Modules.SourceFileCount,
                providerFeatureStart == 0 ? 0
                    : providerFeatureStart + consumerFeatureSection.Count,
                providerModuleFeatureCount,
                providerTables.Modules.GetGoverningRuleEdition(1)),
        };
        var moduleRefCount = consumerTables.ModuleRefs.Count
            + providerTables.ModuleRefs.Count;
        var typeDefCount = consumerTables.TypeDefs.Count
            + providerTables.TypeDefs.Count;
        var methodDefCount = consumerTables.Methods.Count
            + providerTables.Methods.Count;
        var sourceFileCount = consumerTables.Modules.SourceFileCount
            + providerTables.Modules.SourceFileCount;
        var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows,
            strings, moduleRefCount, typeDefCount, methodDefCount,
            sourceFileCount, featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes,
            moduleRows.Length, strings, moduleRefCount, typeDefCount,
            methodDefCount, sourceFileCount, featureCount);

        var consumerModuleRefSection = GetSection(consumerIndex, 0x0010);
        var providerModuleRefSection = GetSection(providerIndex, 0x0010);
        var moduleRefRows = new List<TrRmModuleRefRow>(moduleRefCount);
        for (var row = 0; row < consumerModuleRefSection.Count; row++)
        {
            var data = consumerModuleRefSection.Bytes.AsSpan(row * 12, 12);
            moduleRefRows.Add(new TrRmModuleRefRow(
                checked((int)ReadUInt32(data, 0)),
                ReadUInt32(data, 4), ReadUInt32(data, 8)));
        }
        for (var row = 0; row < providerModuleRefSection.Count; row++)
        {
            var data = providerModuleRefSection.Bytes.AsSpan(row * 12, 12);
            moduleRefRows.Add(new TrRmModuleRefRow(
                checked((int)ReadUInt32(data, 0)
                    + consumerStringSection.Count),
                ReadUInt32(data, 4), ReadUInt32(data, 8)));
        }
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode(moduleRefRows,
            strings, modules);

        var consumerTypeSpecSection = GetSection(consumerIndex, 0x0011);
        var providerTypeSpecSection = GetSection(providerIndex, 0x0011);
        var typeSpecRows = new ReadOnlyMemory<byte>[
            consumerTypeSpecSection.Count + providerTypeSpecSection.Count];
        var consumerTypeSpecIndex = new TrRmVariableTableIndex(
            consumerTypeSpecSection.Bytes, consumerTypeSpecSection.Count,
            2, 1);
        var providerTypeSpecIndex = new TrRmVariableTableIndex(
            providerTypeSpecSection.Bytes, providerTypeSpecSection.Count,
            2, 1);
        for (var row = 1; row <= consumerTypeSpecSection.Count; row++)
        {
            typeSpecRows[row - 1]
                = consumerTypeSpecIndex.GetRow(row).ToArray();
        }
        for (var row = 1; row <= providerTypeSpecSection.Count; row++)
        {
            typeSpecRows[consumerTypeSpecSection.Count + row - 1]
                = RemapTypeSpecRow(providerTables.TypeSpecs, row,
                    consumerTables.TypeDefs.Count,
                    consumerTables.Imports.Count,
                    consumerTables.TypeSpecs.Count,
                    consumerTables.GenericParams.Count,
                    consumerTables.Methods.Count);
        }
        var typeSpecCount = typeSpecRows.Length;
        var genericParamCount = consumerTables.GenericParams.Count
            + providerTables.GenericParams.Count;
        var predicateCaptureCount = consumerTables.PredicateCaptures.Count
            + providerTables.PredicateCaptures.Count;
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount,
            consumerTables.Imports.Count + providerTables.Imports.Count,
            genericParamCount, 0, schema,
            methodDefCount: methodDefCount,
            predicateCaptureCount: predicateCaptureCount);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecCount, typeDefCount,
            consumerTables.Imports.Count + providerTables.Imports.Count,
            genericParamCount, 0, schema, methodDefCount,
            predicateCaptureCount);

        var consumerTypeDefSection = GetSection(consumerIndex, 0x0013);
        var providerTypeDefSection = GetSection(providerIndex, 0x0013);
        var typeDefRows = new List<TrRmTypeDefRow>(typeDefCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var tables = artifact == 0 ? consumerTables : providerTables;
            var section = artifact == 0
                ? consumerTypeDefSection : providerTypeDefSection;
            var stringOffset = artifact == 0
                ? 0 : consumerStringSection.Count;
            var fieldOffset = artifact == 0
                ? 0 : consumerTables.Fields.Count;
            var genericParamOffset = artifact == 0
                ? 0 : consumerTables.GenericParams.Count;
            var layoutOffset = artifact == 0
                ? 0 : consumerTables.Layouts.Count;
            var contractOffset = artifact == 0
                ? 0 : consumerTables.Contracts.Count;
            for (var row = 1; row <= tables.TypeDefs.Count; row++)
            {
                var data = section.Bytes.AsSpan((row - 1) * 44, 44);
                var (fieldStart, fieldEnd)
                    = tables.TypeDefs.GetFieldRange(row);
                var (genericFirst, genericCount)
                    = tables.TypeDefs.GetGenericRange(row);
                var (layoutFirst, layoutCount)
                    = tables.TypeDefs.GetLayoutRange(row);
                var (contractFirst, contractCount)
                    = tables.TypeDefs.GetContractRange(row);
                typeDefRows.Add(new TrRmTypeDefRow(
                    checked((int)ReadUInt32(data, 0) + stringOffset),
                    checked((int)ReadUInt32(data, 4) + stringOffset),
                    tables.TypeDefs.GetFlags(row),
                    fieldStart == 0 ? 0 : fieldStart + fieldOffset,
                    fieldEnd - fieldStart,
                    genericFirst == 0 ? 0
                        : genericFirst + genericParamOffset,
                    genericCount,
                    layoutFirst == 0 ? 0 : layoutFirst + layoutOffset,
                    layoutCount,
                    contractFirst == 0 ? 0 : contractFirst + contractOffset,
                    contractCount));
            }
        }
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules,
            consumerTables.Fields.Count + providerTables.Fields.Count,
            genericParamCount,
            consumerTables.Layouts.Count + providerTables.Layouts.Count,
            consumerTables.Contracts.Count + providerTables.Contracts.Count,
            schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules,
            consumerTables.Fields.Count + providerTables.Fields.Count,
            genericParamCount,
            consumerTables.Layouts.Count + providerTables.Layouts.Count,
            consumerTables.Contracts.Count + providerTables.Contracts.Count,
            schema);

        var consumerFieldSection = GetSection(consumerIndex, 0x0014);
        var providerFieldSection = GetSection(providerIndex, 0x0014);
        var fieldRows = new List<TrRmFieldDefRow>(
            consumerTables.Fields.Count + providerTables.Fields.Count);
        for (var row = 1; row <= consumerTables.Fields.Count; row++)
        {
            var data = consumerFieldSection.Bytes.AsSpan((row - 1) * 16, 16);
            fieldRows.Add(new TrRmFieldDefRow(
                checked((int)ReadUInt32(data, 0)),
                checked((int)ReadUInt32(data, 4)),
                ReadUInt32(data, 8), ReadUInt32(data, 12)));
        }
        for (var row = 1; row <= providerTables.Fields.Count; row++)
        {
            var data = providerFieldSection.Bytes.AsSpan((row - 1) * 16, 16);
            fieldRows.Add(new TrRmFieldDefRow(
                checked((int)ReadUInt32(data, 0)
                    + consumerTables.TypeDefs.Count),
                checked((int)ReadUInt32(data, 4)
                    + consumerStringSection.Count),
                RemapTypeSpecRef(ReadUInt32(data, 8),
                    consumerTables.TypeSpecs.Count),
                ReadUInt32(data, 12)));
        }
        var fieldBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, fieldRows.Count,
            strings, typeDefs, typeSpecs);

        var consumerLayoutFieldSection = GetSection(consumerIndex, 0x0023);
        var providerLayoutFieldSection = GetSection(providerIndex, 0x0023);
        var layoutFieldRows = new List<TrRmLayoutFieldRow>(
            consumerTables.LayoutFields.Count
                + providerTables.LayoutFields.Count);
        for (var row = 1; row <= consumerTables.LayoutFields.Count; row++)
        {
            layoutFieldRows.Add(new TrRmLayoutFieldRow(
                consumerTables.LayoutFields.GetOwnerLayout(row),
                consumerTables.LayoutFields.GetOrdinal(row),
                consumerTables.LayoutFields.GetFieldDef(row)));
        }
        for (var row = 1; row <= providerTables.LayoutFields.Count; row++)
        {
            layoutFieldRows.Add(new TrRmLayoutFieldRow(
                providerTables.LayoutFields.GetOwnerLayout(row)
                    + consumerTables.Layouts.Count,
                providerTables.LayoutFields.GetOrdinal(row),
                providerTables.LayoutFields.GetFieldDef(row)
                    + consumerTables.Fields.Count));
        }

        var consumerLayoutSection = GetSection(consumerIndex, 0x0015);
        var providerLayoutSection = GetSection(providerIndex, 0x0015);
        var layoutRows = new List<TrRmLayoutRow>(
            consumerTables.Layouts.Count + providerTables.Layouts.Count);
        for (var row = 1; row <= consumerTables.Layouts.Count; row++)
        {
            var (fieldFirst, fieldCount)
                = consumerTables.Layouts.GetLayoutFieldRange(row);
            layoutRows.Add(new TrRmLayoutRow(
                consumerTables.Layouts.GetOwnerTypeDef(row),
                consumerTables.Layouts.GetTargetProfileRef(row),
                consumerTables.Layouts.GetLayoutKind(row),
                consumerTables.Layouts.GetFlags(row),
                consumerTables.Layouts.GetAlignment(row),
                consumerTables.Layouts.GetPack(row), fieldFirst, fieldCount));
        }
        for (var row = 1; row <= providerTables.Layouts.Count; row++)
        {
            var (fieldFirst, fieldCount)
                = providerTables.Layouts.GetLayoutFieldRange(row);
            var oldProfile = providerTables.Layouts
                .GetTargetProfileRef(row);
            var newProfile = oldProfile == 0 ? 0
                : checked((uint)providerTargetProfileRowByOld[
                    checked((int)oldProfile)]);
            layoutRows.Add(new TrRmLayoutRow(
                providerTables.Layouts.GetOwnerTypeDef(row)
                    + consumerTables.TypeDefs.Count,
                newProfile,
                providerTables.Layouts.GetLayoutKind(row),
                providerTables.Layouts.GetFlags(row),
                providerTables.Layouts.GetAlignment(row),
                providerTables.Layouts.GetPack(row),
                fieldFirst == 0 ? 0
                    : fieldFirst + consumerTables.LayoutFields.Count,
                fieldCount));
        }
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows,
            typeDefs, targetProfiles, layoutFieldRows.Count);
        var layouts = new TrRmLayoutTableReader(layoutBytes,
            layoutRows.Count, typeDefs, targetProfiles,
            layoutFieldRows.Count);
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, typeDefs, fields);

        var consumerFixedArraySection = GetSection(consumerIndex, 0x0012);
        var providerFixedArraySection = GetSection(providerIndex, 0x0012);
        var fixedArrayRows = new List<TrRmFixedArrayLayoutRow>(
            consumerTables.FixedArrayLayouts.Count
                + providerTables.FixedArrayLayouts.Count);
        for (var row = 1;
            row <= consumerTables.FixedArrayLayouts.Count; row++)
        {
            fixedArrayRows.Add(
                consumerTables.FixedArrayLayouts.GetRow(row));
        }
        for (var row = 1;
            row <= providerTables.FixedArrayLayouts.Count; row++)
        {
            var layout = providerTables.FixedArrayLayouts.GetRow(row);
            fixedArrayRows.Add(layout with {
                OwnerModuleRow = layout.OwnerModuleRow
                    + consumerTables.Modules.Count,
                ArrayTypeSpecRow = layout.ArrayTypeSpecRow
                    + consumerTables.TypeSpecs.Count,
                TargetProfileRef = checked((uint)
                    providerTargetProfileRowByOld[
                        checked((int)layout.TargetProfileRef)]),
            });
        }
        var fixedArrayBytes = TrRmFixedArrayLayoutTableWriter.Encode(
            fixedArrayRows, typeSpecs, targetProfiles, features, modules,
            schema);

        var consumerImportSection = GetSection(consumerIndex, 0x001C);
        var providerImportSection = GetSection(providerIndex, 0x001C);
        var importBytes = new byte[consumerImportSection.Bytes.Length
            + providerImportSection.Bytes.Length];
        consumerImportSection.Bytes.CopyTo(importBytes, 0);
        providerImportSection.Bytes.CopyTo(importBytes,
            consumerImportSection.Bytes.Length);
        for (var row = 0; row < providerImportSection.Count; row++)
        {
            var data = importBytes.AsSpan(
                consumerImportSection.Bytes.Length + (row * 12), 12);
            WriteUInt32(data, 0,
                checked(ReadUInt32(data, 0)
                    + (uint)consumerTables.ModuleRefs.Count));
            WriteUInt32(data, 4,
                checked(ReadUInt32(data, 4)
                    + (uint)consumerStringSection.Count));
            WriteUInt32(data, 8, RemapDefinition(
                ReadUInt32(data, 8), consumerTables.TypeDefs.Count,
                consumerTables.Fields.Count));
        }
        var importCount = consumerImportSection.Count
            + providerImportSection.Count;

        var consumerExportSection = GetSection(consumerIndex, 0x001D);
        var providerExportSection = GetSection(providerIndex, 0x001D);
        var exportBytes = new byte[consumerExportSection.Bytes.Length
            + providerExportSection.Bytes.Length];
        consumerExportSection.Bytes.CopyTo(exportBytes, 0);
        providerExportSection.Bytes.CopyTo(exportBytes,
            consumerExportSection.Bytes.Length);
        for (var row = 0; row < providerExportSection.Count; row++)
        {
            var data = exportBytes.AsSpan(
                consumerExportSection.Bytes.Length + (row * 8), 8);
            WriteUInt32(data, 0, RemapDefinition(
                ReadUInt32(data, 0), consumerTables.TypeDefs.Count,
                consumerTables.Fields.Count));
            WriteUInt32(data, 4,
                checked(ReadUInt32(data, 4)
                    + (uint)consumerStringSection.Count));
        }
        var exportCount = consumerExportSection.Count
            + providerExportSection.Count;

        var outputSections
            = new Dictionary<ushort, (ushort Flags, int Count, byte[] Bytes)>();
        for (var sectionIndex = 0;
            sectionIndex < consumerIndex.SectionCount; sectionIndex++)
        {
            consumerIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var bytes, out var recordCount);
            outputSections.Add(kind,
                (flags, recordCount, bytes.ToArray()));
        }

        void SetSection(ushort kind, byte[] bytes, int count,
            ushort fallbackFlags)
        {
            var flags = outputSections.TryGetValue(kind, out var existing)
                ? existing.Flags : fallbackFlags;
            outputSections[kind] = (flags, count, bytes);
        }

        SetSection(0x0001, moduleBytes, moduleRows.Length,
            consumerModuleSection.Flags);
        SetSection(0x0002, stringBytes, stringRows.Length,
            consumerStringSection.Flags);
        SetSection(0x0004, featureBytes, featureCount,
            consumerFeatureSection.Flags);
        SetSection(0x0010, moduleRefBytes, moduleRefRows.Count,
            consumerModuleRefSection.Flags != 0
                ? consumerModuleRefSection.Flags
                : providerModuleRefSection.Flags);
        SetSection(0x0011, typeSpecBytes, typeSpecCount,
            consumerTypeSpecSection.Flags);
        SetSection(0x0012, fixedArrayBytes, fixedArrayRows.Count,
            consumerFixedArraySection.Flags != 0
                ? consumerFixedArraySection.Flags
                : providerFixedArraySection.Flags);
        SetSection(0x0013, typeDefBytes, typeDefCount,
            GetSection(consumerIndex, 0x0013).Flags);
        SetSection(0x0014, fieldBytes, fieldRows.Count,
            consumerFieldSection.Flags);
        SetSection(0x0015, layoutBytes, layoutRows.Count,
            consumerLayoutSection.Flags != 0
                ? consumerLayoutSection.Flags
                : providerLayoutSection.Flags);
        SetSection(0x001C, importBytes, importCount,
            consumerImportSection.Flags != 0
                ? consumerImportSection.Flags
                : providerImportSection.Flags);
        SetSection(0x001D, exportBytes, exportCount,
            consumerExportSection.Flags != 0
                ? consumerExportSection.Flags
                : providerExportSection.Flags);
        SetSection(0x0023, layoutFieldBytes, layoutFieldRows.Count,
            consumerLayoutFieldSection.Flags != 0
                ? consumerLayoutFieldSection.Flags
                : providerLayoutFieldSection.Flags);
        SetSection(0x0048, targetProfileBytes, targetProfileRows.Count,
            GetSection(consumerIndex, 0x0048).Flags != 0
                ? GetSection(consumerIndex, 0x0048).Flags
                : GetSection(providerIndex, 0x0048).Flags);

        var sections = new List<TrRmSection>(outputSections.Count);
        foreach (var (kind, section) in outputSections)
        {
            sections.Add(new TrRmSection(kind, section.Flags,
                checked((uint)section.Count), section.Bytes));
        }
        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));
        var combinedArtifact = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                        combinedArtifact));
        return combinedArtifact;
    }

    private static (byte[] Bytes, int RecordCount) GetA2R1Section(
        byte[] artifact, ushort requestedKind)
    {
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                artifact);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out _, out var bytes,
                out var recordCount);
            if (kind == requestedKind)
            {
                return (bytes.ToArray(), recordCount);
            }
        }

        throw new InvalidDataException(
            $"The A2R1 artifact is missing section 0x{requestedKind:X4}.");
    }

    private static byte[] AddA2R1BodyFieldMapToken(byte[] artifact,
        string methodName)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var methodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == methodName)
            {
                methodRow = row;
                break;
            }
        }
        Assert.That(methodRow, Is.Not.Zero);
        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        var ownerModule = tables.Methods.GetOwnerModule(methodRow);
        var fieldRow = 0;
        for (var row = 1; row <= tables.Fields.Count; row++)
        {
            var ownerType = tables.Fields.GetOwnerTypeDef(row);
            if (tables.TypeDefs.GetOwnerModule(ownerType) == ownerModule
                && (tables.Fields.GetFlags(row) & 8) == 0)
            {
                fieldRow = row;
                break;
            }
        }
        Assert.That(fieldRow, Is.Not.Zero);

        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var fieldMap = new byte[body.FieldMap.Length + sizeof(uint)];
        body.FieldMap.CopyTo(fieldMap);
        BinaryPrimitives.WriteUInt32LittleEndian(
            fieldMap.AsSpan(body.FieldMap.Length),
            0x1400_0000u | checked((uint)fieldRow));
        var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            blocks[blockIndex] = new(
                BinaryPrimitives.ReadUInt32LittleEndian(block),
                BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(block[8..]));
        }
        var records = new ReadOnlyMemory<byte>[body.RecordCount];
        for (var recordIndex = 0; recordIndex < records.Length;
            recordIndex++)
        {
            records[recordIndex] = body.GetRecord(recordIndex).ToArray();
        }
        var rewrittenBody = TrRmRegisterBodyWriter.Encode(
            body.HomeMap.ToArray(), body.TypeMap.ToArray(),
            body.MethodMap.ToArray(), fieldMap, body.ConstantMap.ToArray(),
            blocks, records, body.Code.ToArray(), tables.Contracts.Count,
            schema);

        var bodySection = GetA2R1Section(artifact, 0x0020);
        var bodyTable = new TrRmBodyTableReader(bodySection.Bytes,
            bodySection.RecordCount, schema);
        var bodyRows = new ReadOnlyMemory<byte>[bodySection.RecordCount];
        for (var row = 1; row <= bodyRows.Length; row++)
        {
            bodyRows[row - 1] = row == bodyRow
                ? rewrittenBody : bodyTable.GetRow(row).ToArray();
        }
        var rewrittenBodySection
            = TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var featureRows = new List<TrRmFeatureRow>(
            tables.Features.Count + 1);
        var hasLayoutQueries = false;
        for (var row = 1; row <= tables.Features.Count; row++)
        {
            var feature = tables.Features.GetFeature(row);
            featureRows.Add(feature);
            hasLayoutQueries |= feature.FeatureId
                == TrRmFeatureTableReader.LayoutQueriesFeatureId;
        }
        if (!hasLayoutQueries)
        {
            featureRows.Add(TrRmFeatureRow.LayoutQueries);
        }
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var moduleSection = GetA2R1Section(artifact, 0x0001);
        Assert.That(tables.Modules.Count, Is.EqualTo(1));
        BinaryPrimitives.WriteUInt32LittleEndian(
            moduleSection.Bytes.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            moduleSection.Bytes.AsSpan(28),
            checked((uint)featureRows.Count));
        var result = ReplaceA2R1SectionsWithCounts(artifact,
            (0x0001, moduleSection.Bytes, moduleSection.RecordCount),
            (0x0004, featureBytes, featureRows.Count),
            (0x0020, rewrittenBodySection, bodySection.RecordCount));
        _ = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                        result));
        return result;
    }

    private static byte[] ReplaceA2R1Sections(byte[] source,
        params (ushort Kind, byte[] Bytes)[] replacements)
    {
        var replacementBytesByKind
            = new Dictionary<ushort, (byte[] Bytes, int? RecordCount)>(
                replacements.Length);
        foreach (var (kind, bytes) in replacements)
        {
            replacementBytesByKind.Add(kind, (bytes, null));
        }

        return ReplaceA2R1SectionsCore(source, replacementBytesByKind);
    }

    private static byte[] ReplaceA2R1SectionsWithCounts(byte[] source,
        params (ushort Kind, byte[] Bytes, int RecordCount)[] replacements)
    {
        var replacementBytesByKind
            = new Dictionary<ushort, (byte[] Bytes, int? RecordCount)>(
                replacements.Length);
        foreach (var (kind, bytes, recordCount) in replacements)
        {
            replacementBytesByKind.Add(kind, (bytes, recordCount));
        }

        return ReplaceA2R1SectionsCore(source, replacementBytesByKind);
    }

    private static byte[] ReplaceA2R1SectionsCore(byte[] source,
        Dictionary<ushort, (byte[] Bytes, int? RecordCount)>
            replacementBytesByKind)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft;

        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                source);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            var (sectionBytes, replacementRecordCount)
                = replacementBytesByKind.TryGetValue(kind,
                    out var replacement)
                    ? replacement : (bytes.ToArray(), (int?)null);
            var sectionRecordCount = replacementRecordCount ?? recordCount;
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)sectionRecordCount), sectionBytes));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
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
