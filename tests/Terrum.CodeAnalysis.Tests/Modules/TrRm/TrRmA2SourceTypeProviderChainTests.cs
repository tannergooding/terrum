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
        var rootArtifact = CreateRootArtifact();
        var providerArtifact = CreateProviderArtifact();
        var leafArtifact = CreateLeafProviderArtifact();
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
        Assert.That(pairOrigin.ExportKeys, Has.Length.EqualTo(1));
        Assert.That(pairOrigin.ExportKeys[0], Is.EqualTo("PairKey"));
        Assert.That(pairOrigin.Expectations, Has.Length.EqualTo(2));
        Assert.That(pairOrigin.Expectations.Any(static expectation
            => expectation.UsingModule.ModuleIdentity == "Root"
                && expectation.ImportRow == 1
                && expectation.ExportKey == "PairKey"), Is.True);
        Assert.That(pairOrigin.Expectations.Any(static expectation
            => expectation.UsingModule.ModuleIdentity == "Provider"
                && expectation.ImportRow == 1
                && expectation.ExportKey == "LeafKey"), Is.True);

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

        var replacementLeaf = CreateLeafProviderArtifact(pack: 8);
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

    private static byte[] CreateRootArtifact()
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Root", 0), ("Provider", 0), ("LeafProvider", 0),
            ("Pair", 0), ("Leaf", 0), ("Value", 0), ("PairKey", 0),
            ("LeafKey", 0), ("Container", 0), ("Child", 0), ("Data", 0),
            ("ContainerKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 13);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
        ], TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var features = new TrRmFeatureTableReader(featuresBytes, 1,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 2, 3, 0, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            2, 3, 0, 0, 1);
        var moduleRefsBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(3, 1, 0),
            new TrRmModuleRefRow(4, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefsBytes, 2,
            strings, modules);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0x0D),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 2),
        ], 3, 2, 0, 0,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 3, 3, 2,
            0, 0, TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 5, 0x26, 1, 1, 0, 0, 1, 2, 0, 0),
            new TrRmTypeDefRow(1, 6, 0x26, 2, 1, 0, 0, 3, 2, 0, 0),
            new TrRmTypeDefRow(1, 10, 0x26, 3, 1, 0, 0, 5, 2, 0, 0),
        ], strings, modules, 3, 0, 6, 0,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 3, strings,
            modules, 3, 0, 6, 0,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 11, 3, 0),
            new TrRmFieldDefRow(2, 12, 1, 0),
            new TrRmFieldDefRow(3, 11, 2, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 3, strings,
            typeDefs, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var (layoutBytes, layoutFieldBytes)
            = CreateSingleFieldLayouts(typeDefs, fields, targetProfiles, 3);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 6, typeDefs,
            targetProfiles, 6);
        var layoutFields = new TrRmLayoutFieldTableReader(layoutFieldBytes, 6,
            layouts, typeDefs, fields);
        var importsBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 8, 0x1300_0001),
            new TrRmImportRow(2, 9, 0x1300_0002),
        ], strings, moduleRefs, typeDefs, default, default);
        var imports = new TrRmImportTableReader(importsBytes, 2, strings,
            moduleRefs, typeDefs, default, default);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x1300_0003, 13),
        ], strings, typeDefs, default, default, imports);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 13, stringsBytes),
            new TrRmSection(0x0004, 0, 1, featuresBytes),
            new TrRmSection(0x0010, 0, 2, moduleRefsBytes),
            new TrRmSection(0x0011, 0, 3, typeSpecsBytes),
            new TrRmSection(0x0013, 0, 3, typeDefsBytes),
            new TrRmSection(0x0014, 0, 3, fieldsBytes),
            new TrRmSection(0x0015, 0, 6, layoutBytes),
            new TrRmSection(0x001C, 0, 2, importsBytes),
            new TrRmSection(0x001D, 0, 1, exportsBytes),
            new TrRmSection(0x0023, 0, 6, layoutFieldBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
    }

    private static byte[] CreateProviderArtifact(bool coLocateLeafProvider = false)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Provider", 0), ("LeafProvider", 0), ("Pair", 0),
            ("Leaf", 0), ("Child", 0), ("Data", 0), ("LeafKey", 0),
            ("PairKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 9);
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
        var typeDefCount = coLocateLeafProvider ? 3 : 2;
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
        var typeDefRows = new List<TrRmTypeDefRow>
        {
            new(1, 4, 0x26, 1, 1, 0, 0, 1, 2, 0, 0),
            new(1, 5, 0x26, 2, 1, 0, 0, 3, 2, 0, 0),
        };
        var fieldRows = new List<TrRmFieldDefRow>
        {
            new(1, 6, 2, 0),
            new(2, 7, 1, 0),
        };
        if (coLocateLeafProvider)
        {
            typeDefRows.Add(new TrRmTypeDefRow(1, 5, 0x26, 3, 1,
                0, 0, 5, 2, 0, 0));
            fieldRows.Add(new TrRmFieldDefRow(3, 7, 1, 0));
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
            new TrRmImportRow(1, 8, 0x1300_0002),
        ], strings, moduleRefs, typeDefs, default, default);
        var imports = new TrRmImportTableReader(importsBytes, 1, strings,
            moduleRefs, typeDefs, default, default);
        var exportRows = new List<TrRmExportRow>
        {
            new(0x1300_0001, 9),
        };
        if (coLocateLeafProvider)
        {
            exportRows.Add(new TrRmExportRow(0x1300_0003, 8));
        }
        var exportsBytes = TrRmExportTableWriter.Encode(exportRows, strings,
            typeDefs, default, default, imports);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, (uint)moduleRows.Length,
                modulesBytes),
            new TrRmSection(0x0002, 0, 9, stringsBytes),
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
            ("LeafKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 5);
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
        ], strings, typeDefs, default, default, default);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 5, stringsBytes),
            new TrRmSection(0x0004, 0, 1, featuresBytes),
            new TrRmSection(0x0011, 0, 1, typeSpecsBytes),
            new TrRmSection(0x0013, 0, 1, typeDefsBytes),
            new TrRmSection(0x0014, 0, 1, fieldsBytes),
            new TrRmSection(0x0015, 0, 2, layoutBytes),
            new TrRmSection(0x001D, 0, 1, exportsBytes),
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
            var defaultLayoutRow = layouts.Count + 1;
            layouts.Add(new TrRmLayoutRow(typeDefRow, 0, 1, 0, 0, pack,
                layoutFields.Count + 1, 1));
            layoutFields.Add(new TrRmLayoutFieldRow(defaultLayoutRow, 0,
                typeDefRow));
            var targetLayoutRow = layouts.Count + 1;
            layouts.Add(new TrRmLayoutRow(typeDefRow, 1, 1, 1, 0, pack,
                layoutFields.Count + 1, 1));
            layoutFields.Add(new TrRmLayoutFieldRow(targetLayoutRow, 0,
                typeDefRow));
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
