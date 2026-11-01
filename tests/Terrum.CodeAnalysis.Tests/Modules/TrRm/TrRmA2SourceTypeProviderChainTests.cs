// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmA2SourceTypeProviderChainTests
{
    private const string SchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";

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
            Is.EquivalentTo(new[] { "PairKey", "PairAlias" }));
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
            Is.EquivalentTo(new[] { "PairKey", "PairAlias" }));
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
            """), reference);
        Assert.That(sourceConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, sourceConsumer.Diagnostics));
        var hiddenDependency = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Consumer {
                public Leaf Value;
            }
            """), reference);
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
            Is.EquivalentTo(new[] { "PairKey", "PairAlias" }));
        Assert.That(reencodedPair.StandardTypeOrigin.Expectations.Count(
            static expectation
                => expectation.UsingModule.ModuleIdentity == "Consumer"),
            Is.EqualTo(2));
        var reencodedLeaf = consumerReference.Structures.Single(
            static structure => structure.Name == "Leaf");
        Assert.That(reencodedLeaf.StandardTypeOrigin!.ExportKeys,
            Is.EquivalentTo(new[] { "LeafKey", "ProviderLeafKey" }));
        var hiddenReencodedDependency = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Consumer {
                    public Container Value;
                }
                """), consumerReference);
        Assert.That(hiddenReencodedDependency.HasErrors, Is.True);

        var secondSourceConsumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Consumer2 {
                    public Consumer Value;
                }
                """), consumerReference);
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
            Is.EquivalentTo(new[] { "LeafKey", "ProviderLeafKey" }));

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
            """), reference);
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
                new[] { "PairKey", "PairAlias" })), Is.True);
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

    private static byte[] CreateRootArtifact(bool duplicatePairAlias = false,
        bool multiplePairViews = false)
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
        ], TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var features = new TrRmFeatureTableReader(featuresBytes, 1,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
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
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes,
            typeSpecRows.Count, typeDefCount, 3, 0, 0,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
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
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes,
            typeDefCount, strings, modules, fieldCount, 0, layoutCount, 0,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
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
        ], TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var (layoutBytes, layoutFieldBytes)
            = CreateSingleFieldLayouts(typeDefs, fields, targetProfiles,
                typeDefCount);
        var layoutFieldCount = multiplePairViews ? 10 : 6;
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutCount,
            typeDefs, targetProfiles, layoutFieldCount);
        var layoutFields = new TrRmLayoutFieldTableReader(layoutFieldBytes,
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
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var imports = new TrRmImportTableReader(importsBytes, 3, strings,
            moduleRefs, typeDefs, default, default, default, default, fields,
            features, modules,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
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
        ], TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
    }

    private static byte[] CreateProviderArtifact(bool coLocateLeafProvider = false,
        bool splitPairAlias = false, bool relocatePair = false)
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
        ], TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
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
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 2,
            typeDefCount, 1, 0, 0,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
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
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, typeDefCount,
            strings, modules, fieldRows.Count, 0, layoutCount, 0,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, fieldRows.Count,
            strings, typeDefs, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var (layoutBytes, layoutFieldBytes)
            = CreateSingleFieldLayouts(typeDefs, fields, targetProfiles,
                typeDefCount);
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutCount,
            typeDefs, targetProfiles, layoutCount);
        var layoutFields = new TrRmLayoutFieldTableReader(layoutFieldBytes,
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
        ], TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
    }

    private static byte[] CreateLeafProviderArtifact(uint pack = 16)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("LeafProvider", 0), ("Leaf", 0), ("Data", 0),
            ("LeafKey", 0), ("ProviderLeafKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 6);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
        ], TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, 1, 0, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 1, 0, 0, 1);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0x0D),
        ], 1, 0, 0, 0,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 1, 1, 0,
            0, 0, TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x26, 1, 1, 0, 0, 1, 2, 0, 0),
        ], strings, modules, 1, 0, 2, 0,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 1, strings,
            modules, 1, 0, 2, 0,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 4, 1, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 1, strings,
            typeDefs, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var (layoutBytes, layoutFieldBytes)
            = CreateSingleFieldLayouts(typeDefs, fields, targetProfiles, 1,
                pack);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 2, typeDefs,
            targetProfiles, 2);
        var layoutFields = new TrRmLayoutFieldTableReader(layoutFieldBytes, 2,
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
        ], TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
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
