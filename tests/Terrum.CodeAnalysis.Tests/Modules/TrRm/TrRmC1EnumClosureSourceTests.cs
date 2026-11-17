using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmC1EnumClosureSourceTests
{
    private static readonly string[] s_schemaIdentities = [
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1",
    ];

    [Test]
    public void EmitsExactSourceEnumClosureUnderEachEN1Identity()
    {
        var secondary = SyntaxTree.Parse("""
            namespace Demo {
                partial enum Shade {
                    Earlier = -128
                }
            }
            """, "secondary.terrum");
        var primary = SyntaxTree.Parse("""
            namespace Demo {
                public partial enum Shade : int8 {
                    Minimum = -128,
                    Successor
                }
                public enum Empty : int32 { }
                internal enum Wide : uint64 { Maximum = 18446744073709551615 }
            }
            """, "primary.terrum");
        var fileLocalFirst = SyntaxTree.Parse("""
            namespace Demo {
                file enum Local : nint { Value = 7 }
            }
            """, "first.terrum");
        var fileLocalSecond = SyntaxTree.Parse("""
            namespace Demo {
                file enum Local : nint { Value = 7 }
            }
            """, "second.terrum");
        var compilation = Compilation.CreateLibrary([
            secondary, primary, fileLocalFirst, fileLocalSecond,
        ]);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var exports = ImmutableArray.Create(
            CreateExport(program, primary, 1, "Shade",
                ["type.shade", "type.shade.alias"], "shade"),
            CreateExport(program, primary, 1, "Empty", ["type.empty"],
                "empty"),
            CreateExport(program, primary, 1, "Wide", [], "wide"),
            CreateExport(program, fileLocalFirst, 2, "Local", [], "local-a"),
            CreateExport(program, fileLocalSecond, 3, "Local", [], "local-b"));

        foreach (var schemaIdentity in s_schemaIdentities)
        {
            var artifact = TrRmExecutableCompiler.EmitStandard(program,
                "SourceEnums", schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                [], [], [], exports);
            var tables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    artifact, SchemaFor(schemaIdentity)));

            Assert.That(tables.TypeDefs.Count, Is.EqualTo(5));
            Assert.That(tables.EnumDefs.Count, Is.EqualTo(5));
            Assert.That(tables.EnumMembers.Count, Is.EqualTo(6));
            Assert.That(tables.TypeSpecs.Count, Is.EqualTo(4));
            Assert.That(tables.ConstantSources.Count, Is.EqualTo(5));
            Assert.That(tables.Features.GetFeature(1),
                Is.EqualTo(TrRmFeatureRow.TypedFrozenValues));
            Assert.That(tables.Features.GetFeature(2).FeatureId,
                Is.EqualTo(18u));
            Assert.That(tables.TargetProfiles.GetProfile(1),
                Is.EqualTo(new TrRmTargetProfileRow(1, 1)));
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "type.shade", out var shadeDefinition), Is.True);
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "type.shade.alias", out var shadeAlias), Is.True);
            Assert.That(shadeAlias, Is.EqualTo(shadeDefinition));
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "type.empty", out _), Is.True);
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "type.wide", out _), Is.False);

            var localRows = Enumerable.Range(1, tables.TypeDefs.Count)
                .Where(row => tables.TypeDefs.GetName(row) == "Local")
                .ToArray();
            Assert.That(localRows, Has.Length.EqualTo(2));
            Assert.That(localRows.Select(tables.TypeDefs.GetFlags),
                Is.All.EqualTo(0x10u));

            var shadeType = checked((int)(shadeDefinition
                & 0x00FF_FFFF));
            var shadeEnum = tables.EnumDefs.GetRowForTypeDef(shadeType);
            var (memberFirst, memberCount)
                = tables.EnumDefs.GetMemberRange(shadeEnum);
            Assert.That(memberCount, Is.EqualTo(3));
            var sourceRows = Enumerable.Range(memberFirst, memberCount)
                .Select(tables.EnumMembers.GetValueSourceRef).ToArray();
            Assert.That(sourceRows[0], Is.EqualTo(sourceRows[1]));
            Assert.That(tables.EnumMembers.GetDeclarationKey(memberFirst),
                Is.Not.EqualTo(tables.EnumMembers.GetDeclarationKey(
                    memberFirst + 1)));
            Assert.That(ReadEnumValue(tables, memberFirst), Is.EqualTo(-128));
            Assert.That(ReadEnumValue(tables, memberFirst + 1),
                Is.EqualTo(-128));
            Assert.That(ReadEnumValue(tables, memberFirst + 2),
                Is.EqualTo(-127));

            var wideType = Enumerable.Range(1, tables.TypeDefs.Count)
                .Single(row => tables.TypeDefs.GetName(row) == "Wide");
            var wideEnum = tables.EnumDefs.GetRowForTypeDef(wideType);
            var (wideFirst, wideCount)
                = tables.EnumDefs.GetMemberRange(wideEnum);
            Assert.That(wideCount, Is.EqualTo(1));
            Assert.That(ReadEnumValue(tables, wideFirst),
                Is.EqualTo(BigInteger.Parse("18446744073709551615")));
            Assert.That(tables.Modules.GetRequirements(1).TargetRequirement,
                Is.EqualTo(1u));
        }
    }

    [Test]
    public void ReconstructsSelectedAndClosureOnlyStandardEnumIdentities()
    {
        var secondary = SyntaxTree.Parse("""
            namespace Demo {
                partial enum Shade { Earlier = -128 }
            }
            """, "secondary.terrum");
        var primary = SyntaxTree.Parse("""
            namespace Demo {
                public partial enum Shade : int8 {
                    Minimum = -128,
                    Successor
                }
                internal enum Wide : uint64 {
                    Maximum = 18446744073709551615
                }
            }
            """, "primary.terrum");
        var fileLocal = SyntaxTree.Parse("""
            namespace Demo {
                file enum Local : nint { Value = 7 }
            }
            """, "local.terrum");
        var compilation = Compilation.CreateLibrary([
            secondary, primary, fileLocal,
        ]);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var enumExports = ImmutableArray.Create(
            CreateExport(program, primary, 1, "Shade",
                ["type.shade", "type.shade.alias"], "shade"),
            CreateExport(program, primary, 1, "Wide", [], "wide"),
            CreateExport(program, fileLocal, 2, "Local", [], "local"));
        foreach (var schemaIdentity in s_schemaIdentities)
        {
            var artifact = TrRmExecutableCompiler.EmitStandard(program,
                "SourceEnums", schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                [], [], [], enumExports);

            var reference = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "SourceEnums", [], ["type.shade", "type.shade.alias"]);
            Assert.That(reference.EnumDefinitions, Has.Length.EqualTo(3));
            var shade = reference.EnumDefinitions.Single(
                static definition => definition.Name == "Demo.Shade");
            Assert.That(shade.Identity,
                Is.TypeOf<BoundEnumDeclarationIdentity.Standard>());
            var standardShade
                = (BoundEnumDeclarationIdentity.Standard)shade.Identity;
            Assert.That(standardShade.Value.SelectedExportKeys,
                Is.EqualTo(new[] { "type.shade", "type.shade.alias" }));
            Assert.That(reference.EnumDefinitions.Single(
                    static definition => definition.Name == "Demo.Wide")
                    .Identity,
                Is.TypeOf<BoundEnumDeclarationIdentity.Standard>());
            Assert.That(reference.EnumDefinitions.Single(
                    static definition => definition.Name == "Demo.Local")
                    .Identity,
                Is.TypeOf<BoundEnumDeclarationIdentity.Standard>());

            _ = Assert.Throws<ArgumentException>(() =>
                TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(schemaIdentity, artifact),
                    "SourceEnums", [], ["type.wide"]));

            var sourceFreeCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(string.Empty), reference);
            Assert.That(sourceFreeCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    sourceFreeCompilation.Diagnostics));
            var reemitted = TrRmExecutableCompiler.EmitStandard(
                (BoundProgram)sourceFreeCompilation.Program!,
                "SourceEnumsRemapped", schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
            Assert.That(reemitted.AsSpan().SequenceEqual(artifact), Is.False);
            var reemittedReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                    "SourceEnumsRemapped", [],
                    ["type.shade", "type.shade.alias"]);
            Assert.That(reemittedReference.EnumDefinitions,
                Has.Length.EqualTo(3));
            Assert.That(reemittedReference.EnumDefinitions.Single(
                    static definition => definition.Name == "Demo.Shade")
                    .Members.Select(static member => member.Value),
                Is.EqualTo(new BigInteger[] { -128, -128, -127 }));
        }
    }

    [Test]
    public void BindsDirectEnumImportsAndRetainsExpectedSnapshots()
    {
        var schemaIdentity = s_schemaIdentities[0];
        var providerTree = SyntaxTree.Parse("""
            public enum Mode : int32 {
                Read = 1,
                Write = 2
            }
            """, "provider.terrum");
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerEnumExport = CreateExport(providerProgram, providerTree,
            0, "Mode", ["type.mode"], "mode");
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "EnumProvider", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [], [providerEnumExport]);

        var consumerTree = SyntaxTree.Parse("""
            public enum Root : int32 { None = 0 }
            public enum Mode : int32 {
                Read = 1,
                Write = 2
            }
            """, "consumer.terrum");
        var consumerCompilation = Compilation.CreateLibrary(consumerTree);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = (BoundProgram)consumerCompilation.Program!;
        var rootExport = CreateExport(consumerProgram, consumerTree, 0,
            "Root", ["type.root"], "root");
        var expectedMode = CreateExport(consumerProgram, consumerTree, 0,
            "Mode", [], "mode");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "EnumConsumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [], [rootExport, expectedMode]);
        var consumerTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(consumerArtifact,
                        SchemaFor(schemaIdentity)));
        var expectedModeRow = Enumerable.Range(1,
                consumerTables.TypeDefs.Count)
            .Single(row => consumerTables.TypeDefs.GetName(row) == "Mode");
        consumerArtifact = AddEnumTypeImport(consumerArtifact,
            schemaIdentity, "EnumProvider", "type.mode",
            0x1300_0000u | checked((uint)expectedModeRow));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "EnumConsumer",
            [new TrRmStandardArtifactInput(schemaIdentity, providerArtifact)],
            ["type.root"]);
        Assert.That(reference.EnumDefinitions, Has.Length.EqualTo(2));
        var mode = reference.EnumDefinitions.Single(
            static definition => definition.Name == "Mode");
        Assert.That(mode.Identity,
            Is.TypeOf<BoundEnumDeclarationIdentity.Standard>());
        var standardMode
            = (BoundEnumDeclarationIdentity.Standard)mode.Identity;
        Assert.That(standardMode.Value.Declaration.Site.Snapshot.ModuleIdentity,
            Is.EqualTo("EnumProvider"));
        Assert.That(standardMode.Value.ExportKeys,
            Is.EqualTo(new[] { "type.mode" }));
        Assert.That(standardMode.Value.SelectedExportKeys, Is.Empty);
        Assert.That(standardMode.Value.OriginalDeclarations, Has.Length.EqualTo(2));
        Assert.That(standardMode.Value.Expectations, Has.Length.EqualTo(1));
        Assert.That(standardMode.Value.Expectations[0].Binding.ImportRow,
            Is.EqualTo(1));

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "EnumConsumerRemapped", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(reemitted,
                        SchemaFor(schemaIdentity)));
        var reemittedModeRow = Enumerable.Range(1,
                reemittedTables.TypeDefs.Count)
            .Single(row => reemittedTables.TypeDefs.GetName(row) == "Mode");
        var reemittedModeEnum
            = reemittedTables.EnumDefs.GetRowForTypeDef(reemittedModeRow);
        var (reemittedMemberFirst, reemittedMemberCount)
            = reemittedTables.EnumDefs.GetMemberRange(reemittedModeEnum);
        Assert.That(reemittedMemberCount, Is.EqualTo(2));
        Assert.That(reemittedTables.ModuleRefs.Count, Is.EqualTo(1));
        Assert.That(reemittedTables.Imports.Count, Is.EqualTo(1));
        var sourceRow = checked((int)
            reemittedTables.EnumMembers.GetValueSourceRef(
                reemittedMemberFirst));
        var nodeRow = checked((int)
            reemittedTables.ConstantSources.GetPayloadReference(sourceRow));
        var nodeTypeSpecRef
            = reemittedTables.FrozenValueNodes.GetTypeSpecRef(nodeRow);
        Assert.That(reemittedTables.TypeSpecs
                .GetTypeSpecRef(nodeTypeSpecRef).Form,
            Is.EqualTo(0x03));
        Assert.That(ReadEnumValue(reemittedTables, reemittedMemberFirst),
            Is.EqualTo(1));
        Assert.That(ReadEnumValue(reemittedTables, reemittedMemberFirst + 1),
            Is.EqualTo(2));

        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "EnumConsumerRemapped",
                [new TrRmStandardArtifactInput(schemaIdentity,
                    providerArtifact)],
                ["type.root"]);
        var reemittedMode = reemittedReference.EnumDefinitions.Single(
            static definition => definition.Name == "Mode");
        var reemittedModeOrigin
            = (BoundEnumDeclarationIdentity.Standard)
                reemittedMode.Identity;
        Assert.That(reemittedModeOrigin.Value.Expectations,
            Has.Length.EqualTo(1));
        Assert.That(reemittedModeOrigin.Value.Expectations[0].Binding
                .ProducerIdentity,
            Is.EqualTo("EnumProvider"));

        var proxyModeExport = CreateExport(consumerProgram, consumerTree, 0,
            "Mode", ["type.mode"], "mode");
        var proxyConsumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "EnumConsumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [], [rootExport, proxyModeExport]);
        var proxyConsumerTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(proxyConsumerArtifact,
                        SchemaFor(schemaIdentity)));
        var proxyModeRow = Enumerable.Range(1,
                proxyConsumerTables.TypeDefs.Count)
            .Single(row => proxyConsumerTables.TypeDefs.GetName(row) == "Mode");
        _ = Assert.Throws<InvalidDataException>(() =>
            AddEnumTypeImport(proxyConsumerArtifact, schemaIdentity,
                "EnumProvider", "type.mode",
                0x1300_0000u | checked((uint)proxyModeRow)));

        var incompatibleProviderTree = SyntaxTree.Parse("""
            public enum Mode : int32 {
                Read = 1,
                Write = 3
            }
            """, "incompatible-provider.terrum");
        var incompatibleProviderCompilation
            = Compilation.CreateLibrary(incompatibleProviderTree);
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderProgram
            = (BoundProgram)incompatibleProviderCompilation.Program!;
        var incompatibleProvider = TrRmExecutableCompiler.EmitStandard(
            incompatibleProviderProgram, "EnumProvider", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [], [
                CreateExport(incompatibleProviderProgram,
                    incompatibleProviderTree, 0, "Mode", ["type.mode"],
                    "mode"),
            ]);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact),
                "EnumConsumer",
                [new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleProvider)],
                ["type.root"]));
    }

    [Test]
    public void RejectsParentSchemasAndLegacyEnumMemberInputs()
    {
        var tree = SyntaxTree.Parse(
            "public enum Color : int32 { Red = 7 }");
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var enumExport = CreateExport(program, tree, 0, "Color",
            ["type.color"], "color");

        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "SourceEnums",
                "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1",
                1, [], [], [], [enumExport]));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "SourceEnums",
                s_schemaIdentities[0], 1,
                [new TrRmSourceTypeExport("","Color","type.color") {
                    EnumMemberDeclarationKeys = ["legacy"],
                }], [], [], [enumExport]));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "SourceEnums",
                s_schemaIdentities[0], 1, [], [], [], []));
    }

    private static TrRmSourceEnumDeclarationExport CreateExport(
        BoundProgram program, SyntaxTree tree, int sourceInputOrdinal,
        string name, ImmutableArray<string> exportKeys, string keyPrefix)
    {
        var declaration = tree.Root.DescendantNodes().Single(node =>
            node.Kind == SyntaxKind.EnumDeclaration
            && node.Children.OfType<SyntaxTokenElement>()
                .Last(static token => token.Token.Kind
                    == TokenKind.Identifier).Token.Name == name);
        var definition = program.EnumDefinitions.Single(item =>
            item.Identity is BoundEnumDeclarationIdentity.Source source
            && source.Value.Parts.Any(part =>
                part.IsPrimary
                && ReferenceEquals(part.Tree, tree)
                && ReferenceEquals(part.Declaration, declaration)));
        var memberKeys = Enumerable.Range(0, definition.Members.Length)
            .Select(ordinal => $"{keyPrefix}.member.{ordinal}")
            .ToImmutableArray();
        return new TrRmSourceEnumDeclarationExport(program,
            sourceInputOrdinal, declaration.Span, exportKeys, memberKeys);
    }

    private static BigInteger ReadEnumValue(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int memberRow)
    {
        var sourceRow = checked((int)
            tables.EnumMembers.GetValueSourceRef(memberRow));
        var nodeRow = checked((int)
            tables.ConstantSources.GetPayloadReference(sourceRow));
        var typeSpecRef
            = tables.FrozenValueNodes.GetTypeSpecRef(nodeRow);
        var ownerType = tables.EnumMembers.GetOwnerTypeDef(memberRow);
        var enumRow = tables.EnumDefs.GetRowForTypeDef(ownerType);
        var underlying = tables.TypeSpecs.GetTypeSpecRef(
            tables.EnumDefs.GetUnderlyingTypeSpecRef(enumRow));
        if (!TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                underlying.TypeCode, out var primitiveType))
        {
            throw new InvalidDataException(
                "The structural enum base has no primitive type.");
        }
        var ownerModule = tables.TypeDefs.GetOwnerModule(ownerType);
        var payload
            = TrRmConstantSourceValueValidator.GetFrozenScalarPayload(
                sourceRow, typeSpecRef, ownerModule, tables.ConstantSources,
                tables.FrozenValueNodes, tables.Blobs, tables.TypeSpecs,
                tables.Features, tables.Modules, tables.TypeDefs,
                tables.EnumDefs, tables.Imports);
        return new BigInteger(payload,
            isUnsigned: !PrimitiveTypeFacts.IsSignedIntegerOrWide(
                primitiveType), isBigEndian: false);
    }

    private static byte[] AddEnumTypeImport(byte[] artifact,
        string schemaIdentity, string providerIdentity, string exportKey,
        uint expectedDefinitionId)
    {
        var schema = SchemaFor(schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(index);
        var sections = new List<TrRmSection>(index.SectionCount + 2);
        var textRows = new List<(string Text, int Encoding)>();
        var stringsByText = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var stringSection
            = index.TryGetSection(0x0002, out var originalStringBytes,
                out var originalStringCount)
                ? new TrRmStringTableReader(originalStringBytes,
                    originalStringCount)
                : throw new InvalidDataException(
                    "The source enum artifact has no String table.");
        for (var row = 1; row <= stringSection.Count; row++)
        {
            var text = stringSection.DecodeNamespace(row);
            textRows.Add((text, GetTextEncoding(text)));
            _ = stringsByText.TryAdd(text, row);
        }

        int AddString(string text)
        {
            if (!stringsByText.TryGetValue(text, out var row))
            {
                row = textRows.Count + 1;
                textRows.Add((text, GetTextEncoding(text)));
                stringsByText.Add(text, row);
            }

            return row;
        }

        var providerIdentityRow = AddString(providerIdentity);
        var exportKeyRow = AddString(exportKey);
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(textRows);
        var strings = new TrRmStringTableReader(stringBytes,
            textRows.Count);
        if (!index.TryGetSection(0x0001, out var moduleBytes,
                out var moduleCount))
        {
            throw new InvalidDataException(
                "The source enum artifact has no Module table.");
        }

        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount,
            strings, 1, tables.TypeDefs.Count, 0, 0,
            tables.Features.Count);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(providerIdentityRow, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes, 1,
            strings, modules);
        if (!index.TryGetSection(0x0013, out var typeDefBytes,
                out var typeDefCount))
        {
            throw new InvalidDataException(
                "The source enum artifact has no TypeDef table.");
        }

        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            typeDefCount, strings, modules, tables.Fields.Count, 0,
            tables.Layouts.Count, 0, schema);
        if (!index.TryGetSection(0x0004, out var featureBytes,
                out var featureCount))
        {
            throw new InvalidDataException(
                "The source enum artifact has no Feature table.");
        }

        var features = new TrRmFeatureTableReader(featureBytes,
            featureCount, schema);
        var importBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, exportKeyRow, expectedDefinitionId),
        ], strings, moduleRefs, typeDefs, default, default, default, default,
            default, features, modules, schema);

        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind is 0x0010 or 0x001C)
            {
                throw new InvalidDataException(
                    "The source enum fixture already contains import metadata.");
            }

            sections.Add(kind == 0x0002
                ? new TrRmSection(kind, flags,
                    checked((uint)textRows.Count), stringBytes)
                : new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
        }

        sections.Add(new TrRmSection(0x0010, 0, 1, moduleRefBytes));
        sections.Add(new TrRmSection(0x001C, 0, 1, importBytes));
        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));
        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(result, schema));
        return result;

        static int GetTextEncoding(string text)
            => text.Any(static character => character > 0x7F) ? 1 : 0;
    }

    private static TrRmArtifactSchema SchemaFor(string schemaIdentity)
        => schemaIdentity switch {
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1"
                => TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1"
                => TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1"
                => TrRmArtifactSchema.C1M1ConstructionL1R1PropertyInitializedStateEnumClosureDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1"
                => TrRmArtifactSchema.C1M1Construction2L1R1PropertyInitializedStateEnumClosureDraft,
            _ => throw new ArgumentOutOfRangeException(nameof(schemaIdentity)),
        };
}
