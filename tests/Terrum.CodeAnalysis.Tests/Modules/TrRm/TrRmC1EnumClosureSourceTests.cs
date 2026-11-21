using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
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
    private static readonly string[] s_initializerCopyEvidenceSchemaIdentities = [
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-initializer-copy-evidence-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-initializer-copy-evidence-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1-initializer-copy-evidence-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1-initializer-copy-evidence-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1-initializer-copy-evidence-1",
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
    public void EmitsLocalStructFieldClosureAlongsideEN1Enums()
    {
        var tree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 { Dark = -1 }
                public struct ColorPayload {
                    public Shade Value;
                }
                public struct Packet {
                    public int32 Id;
                    public ColorPayload Payload;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var typeExport = new TrRmSourceTypeExport("Demo", "Packet",
            "type.packet") {
            FieldExports = [
                new TrRmSourceFieldExport("Id", "field.packet.id"),
                new TrRmSourceFieldExport("Payload", "field.packet.payload"),
            ],
        };
        var payloadTypeExport = new TrRmSourceTypeExport("Demo",
            "ColorPayload", "type.color-payload") {
            FieldExports = [
                new TrRmSourceFieldExport("Value",
                    "field.color-payload.value"),
            ],
        };
        var enumExport = CreateExport(program, tree, 0, "Shade",
            ["type.shade"], "shade");

        foreach (var schemaIdentity in s_schemaIdentities)
        {
            var artifact = TrRmExecutableCompiler.EmitStandard(program,
                "MixedSourceTypes", schemaIdentity, 1,
                [typeExport, payloadTypeExport], [], [], [enumExport]);
            var tables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    artifact, SchemaFor(schemaIdentity)));

            Assert.That(tables.TypeDefs.Count, Is.EqualTo(3));
            Assert.That(tables.Fields.Count, Is.EqualTo(3));
            Assert.That(tables.Layouts.Count, Is.EqualTo(3));
            Assert.That(tables.LayoutFields.Count, Is.EqualTo(3));
            Assert.That(Enumerable.Range(1, tables.Features.Count)
                    .Select(row => tables.Features.GetFeature(row).FeatureId),
                Does.Contain(13u));

            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "type.shade", out var enumDefinition), Is.True);
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "type.packet", out var packetDefinition), Is.True);
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "type.color-payload", out _), Is.True);
            var enumTypeDefRow = checked((int)(enumDefinition
                & 0x00FF_FFFF));
            var packetTypeDefRow = checked((int)(packetDefinition
                & 0x00FF_FFFF));
            var colorPayloadTypeDefRow = Enumerable.Range(1,
                tables.TypeDefs.Count).Single(row =>
                    tables.TypeDefs.GetName(row) == "ColorPayload");
            Assert.That(tables.TypeDefs.GetKind(enumTypeDefRow), Is.EqualTo(4));
            Assert.That(tables.TypeDefs.GetKind(packetTypeDefRow), Is.EqualTo(1));
            var (packetFieldStart, packetFieldEnd)
                = tables.TypeDefs.GetFieldRange(packetTypeDefRow);
            Assert.That(packetFieldEnd - packetFieldStart,
                Is.EqualTo(2));

            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "field.packet.id", out var idFieldDefinition), Is.True);
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "field.packet.payload", out var payloadFieldDefinition), Is.True);
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "field.color-payload.value", out _), Is.True);
            var idFieldRow = checked((int)(idFieldDefinition
                & 0x00FF_FFFF));
            var payloadFieldRow = checked((int)(payloadFieldDefinition
                & 0x00FF_FFFF));
            Assert.That(tables.Fields.GetOwnerTypeDef(idFieldRow),
                Is.EqualTo(packetTypeDefRow));
            Assert.That(tables.Fields.GetOwnerTypeDef(payloadFieldRow),
                Is.EqualTo(packetTypeDefRow));
            Assert.That(tables.Fields.GetName(idFieldRow), Is.EqualTo("Id"));
            Assert.That(tables.Fields.GetName(payloadFieldRow),
                Is.EqualTo("Payload"));
            Assert.That(tables.Fields.GetTypeSpecRef(idFieldRow),
                Is.EqualTo(0xF200_0008u));
            var payloadType = tables.TypeSpecs.GetTypeSpecRef(
                tables.Fields.GetTypeSpecRef(payloadFieldRow));
            Assert.That(payloadType.Form, Is.EqualTo((byte)0x02));
            Assert.That(payloadType.Row,
                Is.EqualTo(colorPayloadTypeDefRow));

            var (payloadFieldStart, payloadFieldEnd)
                = tables.TypeDefs.GetFieldRange(colorPayloadTypeDefRow);
            var enumFieldType = tables.TypeSpecs.GetTypeSpecRef(
                tables.Fields.GetTypeSpecRef(payloadFieldStart));
            Assert.That(enumFieldType.Form, Is.EqualTo((byte)0x02));
            Assert.That(enumFieldType.Row, Is.EqualTo(enumTypeDefRow));
            Assert.That(payloadFieldEnd - payloadFieldStart,
                Is.EqualTo(1));

            var reference = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "MixedSourceTypes", [], ["type.packet"],
                ["field.packet.id", "field.color-payload.value"]);
            Assert.That(reference.Structures, Has.Length.EqualTo(2));
            Assert.That(reference.EnumDefinitions, Has.Length.EqualTo(1));
            var selectedPayload = reference.Structures.Single(
                static structure => structure.Name == "Demo.ColorPayload");
            Assert.That(selectedPayload.Fields.Single()
                .StandardFieldOrigin!.SelectedExportKeys,
                Is.EqualTo(["field.color-payload.value"]));
            var sourceFreeCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(string.Empty), reference);
            Assert.That(sourceFreeCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    sourceFreeCompilation.Diagnostics));
            var reemitted = TrRmExecutableCompiler.EmitStandard(
                sourceFreeCompilation.Program!, "MixedSourceTypes",
                schemaIdentity, 1);
            var reemittedTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    reemitted, SchemaFor(schemaIdentity)));
            Assert.That(reemittedTables.TypeDefs.Count, Is.EqualTo(3));
            Assert.That(reemittedTables.Fields.Count, Is.EqualTo(3));
            Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
                "type.shade", out _), Is.True);
            Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
                "type.packet", out _), Is.True);
            Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
                "field.packet.id", out _), Is.True);
            Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
                "field.packet.payload", out _), Is.True);
            Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
                "field.color-payload.value", out _), Is.True);

            var reemittedReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                    "MixedSourceTypes", [], ["type.packet"],
                    ["field.packet.id", "field.color-payload.value"]);
            Assert.That(reemittedReference.Structures,
                Has.Length.EqualTo(2));
            Assert.That(reemittedReference.EnumDefinitions,
                Has.Length.EqualTo(1));
            var reemittedCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(string.Empty), reemittedReference);
            Assert.That(reemittedCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    reemittedCompilation.Diagnostics));
        }
    }

    [Test]
    public void ReemitsImportedEnumStructFieldsThroughTheirImport()
    {
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 {
                    Dark = -1,
                    Light = 1
                }
            }
            """, "enum-provider.terrum");
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerEnumExport = CreateExport(providerProgram,
            providerTree, 0, "Shade", ["type.shade"], "shade");
        var consumerTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Packet {
                    public Shade Value;
                }
            }
            """, "enum-consumer.terrum");
        var packetExport = new TrRmSourceTypeExport("Demo", "Packet",
            "type.packet") {
            FieldExports = [
                new TrRmSourceFieldExport("Value", "field.packet.value"),
            ],
        };

        foreach (var schemaIdentity in s_schemaIdentities)
        {
            var providerArtifact = TrRmExecutableCompiler.EmitStandard(
                providerProgram, "EnumProvider", schemaIdentity, 1,
                [], [], [], [providerEnumExport]);
            var providers = new[] {
                new TrRmStandardArtifactInput(schemaIdentity,
                    providerArtifact),
            };
            var providerReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    providers[0], "EnumProvider", [], ["type.shade"]);
            var consumerCompilation = Compilation.CreateLibrary(
                consumerTree, providerReference);
            Assert.That(consumerCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    consumerCompilation.Diagnostics));
            var consumerProgram
                = (BoundProgram)consumerCompilation.Program!;
            var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
                consumerProgram, "MixedEnumConsumer", schemaIdentity, 1,
                [packetExport], [], [], []);
            var consumerTables
                = TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(
                        new TrRmArtifactIndex(consumerArtifact,
                            SchemaFor(schemaIdentity)));
            Assert.That(consumerTables.Imports.Count, Is.EqualTo(1));
            Assert.That(consumerTables.TypeSpecs.GetTypeSpecRef(
                    consumerTables.Fields.GetTypeSpecRef(1)).Form,
                Is.EqualTo((byte)0x03));

            var reference = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact),
                "MixedEnumConsumer", providers, ["type.packet"]);
            Assert.That(reference.Structures, Has.Length.EqualTo(1));
            Assert.That(reference.EnumDefinitions, Has.Length.EqualTo(1));
            var enumOccurrence
                = ((BoundEnumDeclarationIdentity.Standard)
                    reference.EnumDefinitions[0].Identity).Value;
            Assert.That(enumOccurrence.Expectations, Has.Length.EqualTo(1));

            var sourceFreeCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(string.Empty), reference);
            Assert.That(sourceFreeCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    sourceFreeCompilation.Diagnostics));
            var reemitted = TrRmExecutableCompiler.EmitStandard(
                sourceFreeCompilation.Program!, "MixedEnumConsumer",
                schemaIdentity, 1);
            var reemittedTables
                = TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(
                        new TrRmArtifactIndex(reemitted,
                            SchemaFor(schemaIdentity)));
            Assert.That(reemittedTables.Imports.Count, Is.EqualTo(1));
            var reemittedPacketRow = Enumerable.Range(1,
                    reemittedTables.TypeDefs.Count)
                .Single(row =>
                    reemittedTables.TypeDefs.GetName(row) == "Packet");
            var (reemittedFieldStart, _) = reemittedTables.TypeDefs
                .GetFieldRange(reemittedPacketRow);
            var fieldType = reemittedTables.TypeSpecs.GetTypeSpecRef(
                reemittedTables.Fields.GetTypeSpecRef(reemittedFieldStart));
            Assert.That(fieldType.Form, Is.EqualTo((byte)0x03));
            Assert.That(fieldType.Row, Is.EqualTo(1));

            var reloadedReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                    "MixedEnumConsumer", providers, ["type.packet"]);
            Assert.That(reloadedReference.Structures, Has.Length.EqualTo(1));
            Assert.That(reloadedReference.EnumDefinitions,
                Has.Length.EqualTo(1));
            var reloadedCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(string.Empty), reloadedReference);
            Assert.That(reloadedCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    reloadedCompilation.Diagnostics));

            var relocatedProviderTree = SyntaxTree.Parse("""
                namespace Demo {
                    public enum Before : int8 { Zero = 0 }
                    public enum Shade : int8 {
                        Dark = -1,
                        Light = 1
                    }
                }
                """, "relocated-enum-provider.terrum");
            var relocatedProviderCompilation
                = Compilation.CreateLibrary(relocatedProviderTree);
            Assert.That(relocatedProviderCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    relocatedProviderCompilation.Diagnostics));
            var relocatedProviderProgram
                = (BoundProgram)relocatedProviderCompilation.Program!;
            var relocatedProviderArtifact
                = TrRmExecutableCompiler.EmitStandard(
                    relocatedProviderProgram, "EnumProvider",
                    schemaIdentity, 1, [], [], [], [
                        CreateExport(relocatedProviderProgram,
                            relocatedProviderTree, 0, "Before",
                            ["type.before"], "before"),
                        CreateExport(relocatedProviderProgram,
                            relocatedProviderTree, 0, "Shade",
                            ["type.shade"], "shade"),
                    ]);
            var relocatedProviderInput
                = new TrRmStandardArtifactInput(schemaIdentity,
                    relocatedProviderArtifact);
            var relocatedProviderTables
                = TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(
                        new TrRmArtifactIndex(relocatedProviderArtifact,
                            SchemaFor(schemaIdentity)));
            var originalProviderTables
                = TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(
                        new TrRmArtifactIndex(providerArtifact,
                            SchemaFor(schemaIdentity)));
            var originalShadeDefinition
                = originalProviderTables.Exports.GetExportsForModule(1)
                    .Single(static export =>
                        export.ExportKey == "type.shade").Definition;
            var relocatedShadeDefinition
                = relocatedProviderTables.Exports.GetExportsForModule(1)
                    .Single(static export =>
                        export.ExportKey == "type.shade").Definition;
            Assert.That(relocatedShadeDefinition,
                Is.Not.EqualTo(originalShadeDefinition));
            var relocatedConsumerInput
                = new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact);
            var relocatedReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    relocatedConsumerInput, "MixedEnumConsumer", [
                        relocatedProviderInput,
                    ], ["type.packet"]);
            var relocatedPacket = relocatedReference.Structures.Single(
                static structure => structure.Name == "Demo.Packet");
            var relocatedShadeType = relocatedPacket.Fields.Single(
                static field => field.Name == "Value").Type;
            Assert.That(BoundEnumTypes.IsType(relocatedShadeType), Is.True);
            var relocatedShadeIndex
                = BoundEnumTypes.Index(relocatedShadeType);
            Assert.That(relocatedReference.EnumDefinitions[
                relocatedShadeIndex].Name, Is.EqualTo("Demo.Shade"));
            Assert.That(((BoundEnumDeclarationIdentity.Standard)
                relocatedReference.EnumDefinitions[relocatedShadeIndex]
                    .Identity).Value.Declaration.Site.DefinitionId,
                Is.EqualTo(relocatedShadeDefinition));

            var relocatedSourceFreeCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(string.Empty), relocatedReference);
            Assert.That(relocatedSourceFreeCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    relocatedSourceFreeCompilation.Diagnostics));
            var relocatedReemitted = TrRmExecutableCompiler.EmitStandard(
                relocatedSourceFreeCompilation.Program!,
                "MixedEnumConsumer", schemaIdentity, 1);
            var relocatedReemittedTables
                = TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(
                        new TrRmArtifactIndex(relocatedReemitted,
                            SchemaFor(schemaIdentity)));
            var relocatedShadeImport = Enumerable.Range(1,
                    relocatedReemittedTables.Imports.Count)
                .Single(row =>
                    (relocatedReemittedTables.Imports
                        .GetExpectedDefinitionForImportRow(row) >> 24)
                    == 0x13
                    && relocatedReemittedTables.Imports
                        .GetBindingForImportRow(row).ExportKey
                        == "type.shade");
            var relocatedPacketRow = Enumerable.Range(1,
                    relocatedReemittedTables.TypeDefs.Count)
                .Single(row =>
                    relocatedReemittedTables.TypeDefs.GetName(row)
                        == "Packet");
            var (relocatedFieldStart, _) = relocatedReemittedTables.TypeDefs
                .GetFieldRange(relocatedPacketRow);
            var relocatedFieldType
                = relocatedReemittedTables.TypeSpecs.GetTypeSpecRef(
                    relocatedReemittedTables.Fields.GetTypeSpecRef(
                        relocatedFieldStart));
            Assert.That(relocatedFieldType.Form, Is.EqualTo((byte)0x03));
            Assert.That(relocatedFieldType.Row,
                Is.EqualTo(relocatedShadeImport));
            var relocatedReloadedReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(schemaIdentity,
                        relocatedReemitted), "MixedEnumConsumer", [
                        relocatedProviderInput,
                    ], ["type.packet"]);
            var reloadedRelocatedPacket
                = relocatedReloadedReference.Structures.Single(
                    static structure => structure.Name == "Demo.Packet");
            var reloadedRelocatedShadeType
                = reloadedRelocatedPacket.Fields.Single(
                    static field => field.Name == "Value").Type;
            Assert.That(relocatedReloadedReference.EnumDefinitions[
                BoundEnumTypes.Index(reloadedRelocatedShadeType)].Name,
                Is.EqualTo("Demo.Shade"));
        }
    }

    [Test]
    public void ReemitsImportedStructFieldClosure()
    {
        var schemaIdentity = s_schemaIdentities[0];
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 { Dark = -1 }
                public struct Marker {
                    public int32 Id;
                }
            }
            """, "struct-provider.terrum");
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerTypeExport = new TrRmSourceTypeExport("Demo", "Marker",
            "type.marker") {
            FieldExports = [
                new TrRmSourceFieldExport("Id", "field.marker.id"),
            ],
        };
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "StructProvider", schemaIdentity, 1,
            [providerTypeExport], [], [], [
                CreateExport(providerProgram, providerTree, 0, "Shade",
                    [], "shade"),
            ]);

        var consumerTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 { Dark = -1 }
                public struct Marker {
                    public int32 Id;
                }
                public struct Packet {
                    public Marker Value;
                }
            }
            """, "struct-consumer.terrum");
        var consumerCompilation = Compilation.CreateLibrary(consumerTree);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = (BoundProgram)consumerCompilation.Program!;
        var packetExport = new TrRmSourceTypeExport("Demo", "Packet",
            "type.packet") {
            FieldExports = [
                new TrRmSourceFieldExport("Value", "field.packet.value"),
            ],
        };
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "StructConsumer", schemaIdentity, 1,
            [packetExport], [], [], [
                CreateExport(consumerProgram, consumerTree, 0, "Shade",
                    [], "shade"),
            ]);
        var consumerTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(consumerArtifact,
                        SchemaFor(schemaIdentity)));
        var markerTypeDefRow = Enumerable.Range(1,
                consumerTables.TypeDefs.Count)
            .Single(row => consumerTables.TypeDefs.GetName(row) == "Marker");
        var (markerFieldStart, _) = consumerTables.TypeDefs
            .GetFieldRange(markerTypeDefRow);
        var packetTypeDefRow = Enumerable.Range(1,
                consumerTables.TypeDefs.Count)
            .Single(row => consumerTables.TypeDefs.GetName(row) == "Packet");
        var (packetFieldStart, _) = consumerTables.TypeDefs
            .GetFieldRange(packetTypeDefRow);
        consumerArtifact = AddTypeDefImport(consumerArtifact,
            schemaIdentity, "StructProvider", "type.marker",
            0x1300_0000u | checked((uint)markerTypeDefRow),
            "field.marker.id",
            0x1400_0000u | checked((uint)markerFieldStart));
        consumerArtifact = RewriteFieldTypeSpecAsImport(
            consumerArtifact, schemaIdentity, packetFieldStart, 1);

        var providers = new[] {
            new TrRmStandardArtifactInput(schemaIdentity, providerArtifact),
        };
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "StructConsumer", providers, ["type.packet"]);
        Assert.That(reference.Structures, Has.Length.EqualTo(2));
        var importedMarker = reference.Structures.Single(
            static structure => structure.Name == "Demo.Marker");
        Assert.That(importedMarker.StandardTypeOrigin,
            Is.Not.Null);
        Assert.That(importedMarker.StandardTypeOrigin!.Expectations,
            Has.Length.EqualTo(1));

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            sourceFreeCompilation.Program!, "StructConsumer",
            schemaIdentity, 1);
        var reemittedTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(reemitted,
                        SchemaFor(schemaIdentity)));
        Assert.That(reemittedTables.Imports.Count, Is.EqualTo(2));
        Assert.That(Enumerable.Range(1, reemittedTables.Imports.Count)
                .Select(row => reemittedTables.Imports
                    .GetExpectedDefinitionForImportRow(row) >> 24),
            Does.Contain(0x14u));
        var reemittedPacketRow = Enumerable.Range(1,
                reemittedTables.TypeDefs.Count)
            .Single(row =>
                reemittedTables.TypeDefs.GetName(row) == "Packet");
        var (reemittedFieldStart, _) = reemittedTables.TypeDefs
            .GetFieldRange(reemittedPacketRow);
        var fieldType = reemittedTables.TypeSpecs.GetTypeSpecRef(
            reemittedTables.Fields.GetTypeSpecRef(reemittedFieldStart));
        Assert.That(fieldType.Form, Is.EqualTo((byte)0x03));
        Assert.That(fieldType.Row, Is.EqualTo(1));

        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "StructConsumer", providers, ["type.packet"]);
        Assert.That(reloadedReference.Structures, Has.Length.EqualTo(2));
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reloadedReference);
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));

        var incompatibleProviderTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 { Dark = -1 }
                public struct Marker {
                    public int32 Id;
                }
            }
            """, "incompatible-struct-provider.terrum");
        var incompatibleProviderCompilation
            = Compilation.CreateLibrary(incompatibleProviderTree);
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderProgram
            = (BoundProgram)incompatibleProviderCompilation.Program!;
        var incompatibleProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleProviderProgram, "StructProvider",
                schemaIdentity, 1, [
                    new TrRmSourceTypeExport("Demo", "Marker",
                        "type.marker") {
                        FieldExports = [
                            new TrRmSourceFieldExport("Id",
                                "field.marker.other"),
                        ],
                    },
                ], [], [], [
                    CreateExport(incompatibleProviderProgram,
                        incompatibleProviderTree, 0, "Shade", [], "shade"),
                ]);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact),
                "StructConsumer", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProviderArtifact),
                ], ["type.packet"]));
    }

    [Test]
    public void ReemitsTransitiveImportedStructDiamondAcrossProviders()
    {
        var schemaIdentity = s_schemaIdentities[0];
        var leafTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 { Dark = -1 }
                public struct Leaf {
                    public int32 Id;
                    public Shade Kind;
                }
            }
            """);
        var leafCompilation = Compilation.CreateLibrary(leafTree);
        Assert.That(leafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, leafCompilation.Diagnostics));
        var leafProgram = (BoundProgram)leafCompilation.Program!;
        var leafExport = new TrRmSourceTypeExport("Demo", "Leaf",
            "type.leaf") {
            FieldExports = [
                new TrRmSourceFieldExport("Id", "field.leaf.id"),
                new TrRmSourceFieldExport("Kind", "field.leaf.kind"),
            ],
        };
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafProgram, "StructLeafProvider", schemaIdentity, 1,
            [leafExport], [], [], [
                CreateExport(leafProgram, leafTree, 0, "Shade",
                    ["type.shade"], "shade"),
            ]);
        var leafInput = new TrRmStandardArtifactInput(schemaIdentity,
            leafArtifact);
        var leafReference = TrRmExecutableCompiler.CreateStandardReference(
            leafInput, "StructLeafProvider", [],
            ["type.leaf", "type.shade"], [
                "field.leaf.id",
                "field.leaf.kind",
            ]);

        var middleTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Wrapper { public Leaf Child; }
                public struct WrapperTwin { public Leaf Child; }
            }
            """);
        var middleCompilation = Compilation.CreateLibrary(middleTree,
            leafReference);
        Assert.That(middleCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                middleCompilation.Diagnostics));
        var middleProgram = (BoundProgram)middleCompilation.Program!;
        var wrapperExport = new TrRmSourceTypeExport("Demo", "Wrapper",
            "type.wrapper") {
            FieldExports = [
                new TrRmSourceFieldExport("Child",
                    "field.wrapper.child"),
            ],
        };
        var wrapperTwinExport = new TrRmSourceTypeExport("Demo",
            "WrapperTwin", "type.wrapper.twin") {
            FieldExports = [
                new TrRmSourceFieldExport("Child",
                    "field.wrapper.twin.child"),
            ],
        };
        var middleArtifact = TrRmExecutableCompiler.EmitStandard(
            middleProgram, "StructMiddleProvider", schemaIdentity, 1,
            [wrapperExport, wrapperTwinExport], [], [], []);
        var middleTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    middleArtifact, SchemaFor(schemaIdentity)));
        var importedLeafField = Enumerable.Range(1,
                middleTables.Imports.Count)
            .Single(row =>
                (middleTables.Imports
                    .GetExpectedDefinitionForImportRow(row) >> 24) == 0x14
                && middleTables.Imports.GetBindingForImportRow(row)
                    .ExportKey == "field.leaf.id");
        Assert.That(middleTables.Imports.GetBindingForImportRow(
            importedLeafField).ProducerIdentity,
            Is.EqualTo("StructLeafProvider"));
        var middleInput = new TrRmStandardArtifactInput(schemaIdentity,
            middleArtifact);
        var middleReference = TrRmExecutableCompiler.CreateStandardReference(
            middleInput, "StructMiddleProvider", [leafInput], [
                "type.wrapper",
                "type.wrapper.twin",
            ]);
        var importedLeaf = middleReference.Structures.Single(
            static structure => structure.Name == "Demo.Leaf");
        Assert.That(importedLeaf.StandardTypeOrigin, Is.Not.Null);
        Assert.That(BoundEnumTypes.IsType(importedLeaf.Fields.Single(
            static field => field.Name == "Kind").Type), Is.True);
        Assert.That(importedLeaf.Fields.Single(
            static field => field.Name == "Id").StandardFieldOrigin,
            Is.Not.Null);
        Assert.That(middleReference.EnumDefinitions, Has.Length.EqualTo(1));

        var decoyTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 { Dark = -1 }
                public struct Decoy { public int32 Id; }
            }
            """);
        var decoyCompilation = Compilation.CreateLibrary(decoyTree);
        Assert.That(decoyCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, decoyCompilation.Diagnostics));
        var decoyProgram = (BoundProgram)decoyCompilation.Program!;
        var decoyArtifact = TrRmExecutableCompiler.EmitStandard(
            decoyProgram, "StructDecoyProvider", schemaIdentity, 1, [
                new TrRmSourceTypeExport("Demo", "Decoy", "type.decoy") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Id",
                            "field.leaf.id"),
                    ],
                },
            ], [], [], [
                CreateExport(decoyProgram, decoyTree, 0, "Shade",
                    ["type.decoy.shade"], "shade"),
            ]);
        var redirectedFieldImport = RewriteFieldImportProvider(
            middleArtifact, schemaIdentity, "field.leaf.id", decoyArtifact);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    redirectedFieldImport),
                "StructMiddleProvider", [
                    leafInput,
                    new TrRmStandardArtifactInput(schemaIdentity,
                        decoyArtifact),
                ], ["type.wrapper", "type.wrapper.twin"]));

        var consumerTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Packet {
                    public Wrapper Value;
                    public WrapperTwin Twin;
                }
            }
            """);
        var consumerCompilation = Compilation.CreateLibrary(consumerTree,
            middleReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = (BoundProgram)consumerCompilation.Program!;
        var packetExport = new TrRmSourceTypeExport("Demo", "Packet",
            "type.packet") {
            FieldExports = [
                new TrRmSourceFieldExport("Value",
                    "field.packet.value"),
                new TrRmSourceFieldExport("Twin",
                    "field.packet.twin"),
            ],
        };
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "StructConsumer", schemaIdentity, 1,
            [packetExport], [], [], []);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var providers = new[] { middleInput, leafInput };
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            consumerInput, "StructConsumer", providers, ["type.packet"]);
        Assert.That(reference.Structures, Has.Length.EqualTo(4));
        Assert.That(reference.Structures.Any(
            static structure => structure.Name == "Demo.Leaf"
                && structure.StandardTypeOrigin is not null), Is.True);
        Assert.That(reference.Structures.Any(
            static structure => structure.Name == "Demo.Wrapper"
                && structure.StandardTypeOrigin is not null), Is.True);
        Assert.That(reference.Structures.Any(
            static structure => structure.Name == "Demo.WrapperTwin"
                && structure.StandardTypeOrigin is not null), Is.True);
        var importedPacketLeaf = reference.Structures.Single(
            static structure => structure.Name == "Demo.Leaf");
        var importedShadeType = importedPacketLeaf.Fields.Single(
            static field => field.Name == "Kind").Type;
        Assert.That(BoundEnumTypes.IsType(importedShadeType), Is.True);
        var importedShade = ((BoundEnumDeclarationIdentity.Standard)
            reference.EnumDefinitions[
                BoundEnumTypes.Index(importedShadeType)].Identity).Value;
        Assert.That(importedShade.Declaration.Site.Snapshot.ModuleIdentity,
            Is.EqualTo("StructLeafProvider"));

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            sourceFreeCompilation.Program!, "StructConsumer",
            schemaIdentity, 1);
        var reemittedTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    reemitted, SchemaFor(schemaIdentity)));
        Assert.That(Enumerable.Range(1, reemittedTables.Imports.Count)
                .Any(row =>
                    (reemittedTables.Imports
                        .GetExpectedDefinitionForImportRow(row) >> 24)
                        == 0x14
                    && reemittedTables.Imports.GetBindingForImportRow(row)
                        .ExportKey == "field.leaf.id"),
            Is.True);
        var reloadedReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "StructConsumer", providers, ["type.packet"]);
        Assert.That(reloadedReference.Structures, Has.Length.EqualTo(4));
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reloadedReference);
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));
        var reloadedLeaf = reloadedReference.Structures.Single(
            static structure => structure.Name == "Demo.Leaf");
        var reloadedShadeType = reloadedLeaf.Fields.Single(
            static field => field.Name == "Kind").Type;
        Assert.That(BoundEnumTypes.IsType(reloadedShadeType), Is.True);
        Assert.That(((BoundEnumDeclarationIdentity.Standard)
            reloadedReference.EnumDefinitions[
                BoundEnumTypes.Index(reloadedShadeType)].Identity).Value
            .Declaration.Site.DefinitionId,
            Is.EqualTo(importedShade.Declaration.Site.DefinitionId));

        var relocatedLeafTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Before : int8 { Zero = 0 }
                public enum Shade : int8 { Dark = -1 }
                public struct Padding { public int32 Value; }
                public struct Leaf {
                    public int32 Id;
                    public Shade Kind;
                }
            }
            """);
        var relocatedLeafCompilation
            = Compilation.CreateLibrary(relocatedLeafTree);
        Assert.That(relocatedLeafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                relocatedLeafCompilation.Diagnostics));
        var relocatedLeafProgram
            = (BoundProgram)relocatedLeafCompilation.Program!;
        var relocatedLeafArtifact = TrRmExecutableCompiler.EmitStandard(
            relocatedLeafProgram, "StructLeafProvider", schemaIdentity, 1, [
                new TrRmSourceTypeExport("Demo", "Padding", "type.padding"),
                leafExport,
            ], [], [], [
                CreateExport(relocatedLeafProgram, relocatedLeafTree, 0,
                    "Before", ["type.before"], "before"),
                CreateExport(relocatedLeafProgram, relocatedLeafTree, 1,
                    "Shade", ["type.shade"], "shade"),
            ]);
        var relocatedLeafTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    relocatedLeafArtifact, SchemaFor(schemaIdentity)));
        var originalLeafTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    leafArtifact, SchemaFor(schemaIdentity)));
        var originalLeafTypeDefinition
            = originalLeafTables.Exports.GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "type.leaf").Definition;
        var relocatedLeafTypeDefinition
            = relocatedLeafTables.Exports.GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "type.leaf").Definition;
        var originalLeafFieldDefinition
            = originalLeafTables.Exports.GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "field.leaf.id").Definition;
        var relocatedLeafFieldDefinition
            = relocatedLeafTables.Exports.GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "field.leaf.id").Definition;
        Assert.That(relocatedLeafTypeDefinition,
            Is.Not.EqualTo(originalLeafTypeDefinition));
        Assert.That(relocatedLeafFieldDefinition,
            Is.Not.EqualTo(originalLeafFieldDefinition));
        var originalShadeDefinition
            = originalLeafTables.Exports.GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "type.shade").Definition;
        var relocatedShadeDefinition
            = relocatedLeafTables.Exports.GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "type.shade").Definition;
        Assert.That(relocatedShadeDefinition,
            Is.Not.EqualTo(originalShadeDefinition));

        var relocatedLeafInput = new TrRmStandardArtifactInput(
            schemaIdentity, relocatedLeafArtifact);
        var relocatedProviders = new[] { middleInput, relocatedLeafInput };
        var relocatedReference = TrRmExecutableCompiler.CreateStandardReference(
            consumerInput, "StructConsumer", relocatedProviders,
            ["type.packet"]);
        var remappedLeaf = relocatedReference.Structures.Single(
            static structure => structure.Name == "Demo.Leaf");
        Assert.That(remappedLeaf.StandardTypeOrigin!.DefinitionId,
            Is.EqualTo(relocatedLeafTypeDefinition));
        Assert.That(remappedLeaf.Fields.Single(
            static field => field.Name == "Id")
            .StandardFieldOrigin!.DefinitionId,
            Is.EqualTo(relocatedLeafFieldDefinition));
        var remappedShadeType = remappedLeaf.Fields.Single(
            static field => field.Name == "Kind").Type;
        Assert.That(((BoundEnumDeclarationIdentity.Standard)
            relocatedReference.EnumDefinitions[
                BoundEnumTypes.Index(remappedShadeType)].Identity).Value
            .Declaration.Site.DefinitionId,
            Is.EqualTo(relocatedShadeDefinition));
        var relocatedSourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), relocatedReference);
        Assert.That(relocatedSourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                relocatedSourceFreeCompilation.Diagnostics));
        var relocatedReemitted = TrRmExecutableCompiler.EmitStandard(
            relocatedSourceFreeCompilation.Program!, "StructConsumer",
            schemaIdentity, 1);
        var relocatedReloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    relocatedReemitted), "StructConsumer",
                relocatedProviders, ["type.packet"]);
        var reloadedRelocatedLeaf
            = relocatedReloadedReference.Structures.Single(
                static structure => structure.Name == "Demo.Leaf");
        Assert.That(reloadedRelocatedLeaf.StandardTypeOrigin!.DefinitionId,
            Is.EqualTo(relocatedLeafTypeDefinition));
        Assert.That(reloadedRelocatedLeaf.Fields.Single(
            static field => field.Name == "Id")
            .StandardFieldOrigin!.DefinitionId,
            Is.EqualTo(relocatedLeafFieldDefinition));
        var reloadedRelocatedShadeType
            = reloadedRelocatedLeaf.Fields.Single(
                static field => field.Name == "Kind").Type;
        Assert.That(((BoundEnumDeclarationIdentity.Standard)
            relocatedReloadedReference.EnumDefinitions[
                BoundEnumTypes.Index(reloadedRelocatedShadeType)]
                .Identity).Value.Declaration.Site.DefinitionId,
            Is.EqualTo(relocatedShadeDefinition));

        var relocatedMiddleTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Padding { public int32 Value; }
                public struct Wrapper { public Leaf Child; }
                public struct WrapperTwin { public Leaf Child; }
            }
            """);
        var relocatedMiddleCompilation = Compilation.CreateLibrary(
            relocatedMiddleTree, leafReference);
        Assert.That(relocatedMiddleCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                relocatedMiddleCompilation.Diagnostics));
        var relocatedMiddleProgram
            = (BoundProgram)relocatedMiddleCompilation.Program!;
        var relocatedMiddleArtifact = TrRmExecutableCompiler.EmitStandard(
            relocatedMiddleProgram, "StructMiddleProvider", schemaIdentity,
            1, [
                new TrRmSourceTypeExport("Demo", "Padding",
                    "type.padding"),
                wrapperExport,
                wrapperTwinExport,
            ], [], [], []);
        var relocatedMiddleTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    relocatedMiddleArtifact, SchemaFor(schemaIdentity)));
        var originalWrapperDefinition
            = middleTables.Exports.GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "type.wrapper").Definition;
        var relocatedWrapperDefinition
            = relocatedMiddleTables.Exports.GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "type.wrapper").Definition;
        var originalWrapperFieldDefinition
            = middleTables.Exports.GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "field.wrapper.child").Definition;
        var relocatedWrapperFieldDefinition
            = relocatedMiddleTables.Exports.GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "field.wrapper.child").Definition;
        Assert.That(relocatedWrapperDefinition,
            Is.Not.EqualTo(originalWrapperDefinition));
        Assert.That(relocatedWrapperFieldDefinition,
            Is.Not.EqualTo(originalWrapperFieldDefinition));

        var relocatedMiddleInput = new TrRmStandardArtifactInput(
            schemaIdentity, relocatedMiddleArtifact);
        var relocatedMiddleProviders = new[] {
            relocatedMiddleInput, leafInput,
        };
        var relocatedMiddleReference
            = TrRmExecutableCompiler.CreateStandardReference(
                consumerInput, "StructConsumer", relocatedMiddleProviders,
                ["type.packet"]);
        var remappedWrapper = relocatedMiddleReference.Structures.Single(
            static structure => structure.Name == "Demo.Wrapper");
        Assert.That(remappedWrapper.StandardTypeOrigin!.DefinitionId,
            Is.EqualTo(relocatedWrapperDefinition));
        Assert.That(remappedWrapper.Fields.Single(
            static field => field.Name == "Child")
            .StandardFieldOrigin!.DefinitionId,
            Is.EqualTo(relocatedWrapperFieldDefinition));
        var relocatedMiddleSourceFree = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), relocatedMiddleReference);
        Assert.That(relocatedMiddleSourceFree.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                relocatedMiddleSourceFree.Diagnostics));
        var relocatedMiddleReemitted = TrRmExecutableCompiler.EmitStandard(
            relocatedMiddleSourceFree.Program!, "StructConsumer",
            schemaIdentity, 1);
        var relocatedMiddleReloaded
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    relocatedMiddleReemitted), "StructConsumer",
                relocatedMiddleProviders, ["type.packet"]);
        var reloadedWrapper = relocatedMiddleReloaded.Structures.Single(
            static structure => structure.Name == "Demo.Wrapper");
        Assert.That(reloadedWrapper.StandardTypeOrigin!.DefinitionId,
            Is.EqualTo(relocatedWrapperDefinition));
        Assert.That(reloadedWrapper.Fields.Single(
            static field => field.Name == "Child")
            .StandardFieldOrigin!.DefinitionId,
            Is.EqualTo(relocatedWrapperFieldDefinition));

        var incompatibleLeafTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 { Dark = -1 }
                public struct Leaf {
                    public int64 Id;
                    public Shade Kind;
                }
            }
            """);
        var incompatibleLeafCompilation
            = Compilation.CreateLibrary(incompatibleLeafTree);
        Assert.That(incompatibleLeafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleLeafCompilation.Diagnostics));
        var incompatibleLeafProgram
            = (BoundProgram)incompatibleLeafCompilation.Program!;
        var incompatibleLeafArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleLeafProgram, "StructLeafProvider",
                schemaIdentity, 1, [
                    new TrRmSourceTypeExport("Demo", "Leaf",
                        "type.leaf") {
                        FieldExports = [
                            new TrRmSourceFieldExport("Id",
                                "field.leaf.id"),
                            new TrRmSourceFieldExport("Kind",
                                "field.leaf.kind"),
                        ],
                    },
                ], [], [], [
                    CreateExport(incompatibleLeafProgram,
                        incompatibleLeafTree, 0, "Shade",
                        ["type.shade"], "shade"),
                ]);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                consumerInput, "StructConsumer", [
                    middleInput,
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleLeafArtifact),
                ], ["type.packet"]));

        var incompatibleShadeTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int32 { Dark = -1 }
                public struct Leaf {
                    public int32 Id;
                    public Shade Kind;
                }
            }
            """);
        var incompatibleShadeCompilation
            = Compilation.CreateLibrary(incompatibleShadeTree);
        Assert.That(incompatibleShadeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleShadeCompilation.Diagnostics));
        var incompatibleShadeProgram
            = (BoundProgram)incompatibleShadeCompilation.Program!;
        var incompatibleShadeArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleShadeProgram, "StructLeafProvider",
                schemaIdentity, 1, [leafExport], [], [], [
                    CreateExport(incompatibleShadeProgram,
                        incompatibleShadeTree, 0, "Shade",
                        ["type.shade"], "shade"),
                ]);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                consumerInput, "StructConsumer", [
                    middleInput,
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleShadeArtifact),
                ], ["type.packet"]));
    }

    [Test]
    public void EmitsEN1ClassFieldsWithTypedEnumReferences()
    {
        var schemaIdentity = s_schemaIdentities[0];
        var tree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 { Dark = -1 }
                public class Palette {
                    public Shade ShadeValue;
                    public Shelf ShelfValue;
                    public int32 Id;
                    public Palette(Shade shade, int32 id) {
                        ShadeValue = shade;
                        Id = id;
                    }
                }
                public class Shelf {
                    public Shade ShelfShade;
                    public Shelf(Shade shelfShade) {
                        ShelfShade = shelfShade;
                    }
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .First(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var fieldDeclarations = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.FieldDeclaration)
            .ToArray();
        var classIndex = program.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(program, classIndex,
            classDeclaration.Span, "type.palette", [
                new TrRmSourceClassMemberExport(0,
                    fieldDeclarations[0].Span, "field.palette.shade"),
                new TrRmSourceClassMemberExport(0,
                    fieldDeclarations[1].Span, "field.palette.shelf"),
                new TrRmSourceClassMemberExport(0,
                    fieldDeclarations[2].Span, "field.palette.id"),
                CreateSourceClassConstructorExport(classDeclaration,
                    "constructor.palette"),
            ]);
        var enumExport = CreateExport(program, tree, 0, "Shade",
            ["type.shade"], "shade");

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "PaletteModule", schemaIdentity, 1, [], [], [classExport],
            [enumExport]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                artifact, SchemaFor(schemaIdentity)));
        Assert.That(tables.TypeDefs.Count, Is.EqualTo(3));
        Assert.That(tables.TypeDefs.GetKind(1), Is.EqualTo(4));
        Assert.That(tables.TypeDefs.GetKind(2), Is.EqualTo(2));
        Assert.That(tables.TypeDefs.GetFlags(2), Is.EqualTo(0x0Au));
        Assert.That(tables.TypeDefs.GetName(3), Is.EqualTo("Shelf"));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "type.palette", out var paletteDefinition), Is.True);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "field.palette.shade", out var shadeFieldDefinition), Is.True);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "field.palette.shelf", out var shelfFieldDefinition), Is.True);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "field.palette.id", out var idFieldDefinition), Is.True);
        var paletteTypeDefRow = checked((int)(paletteDefinition
            & 0x00FF_FFFF));
        var shadeFieldRow = checked((int)(shadeFieldDefinition
            & 0x00FF_FFFF));
        var shelfFieldRow = checked((int)(shelfFieldDefinition
            & 0x00FF_FFFF));
        var idFieldRow = checked((int)(idFieldDefinition
            & 0x00FF_FFFF));
        Assert.That(tables.Fields.GetOwnerTypeDef(shadeFieldRow),
            Is.EqualTo(paletteTypeDefRow));
        Assert.That(tables.Fields.GetOwnerTypeDef(idFieldRow),
            Is.EqualTo(paletteTypeDefRow));
        var shelfFieldType = tables.TypeSpecs.GetTypeSpecRef(
            tables.Fields.GetTypeSpecRef(shelfFieldRow));
        Assert.That(shelfFieldType.Form, Is.EqualTo((byte)0x02));
        Assert.That(shelfFieldType.Row, Is.EqualTo(3));
        var shadeFieldType = tables.TypeSpecs.GetTypeSpecRef(
            tables.Fields.GetTypeSpecRef(shadeFieldRow));
        Assert.That(shadeFieldType.Form, Is.EqualTo((byte)0x02));
        Assert.That(shadeFieldType.Row, Is.EqualTo(1));
        Assert.That(Enumerable.Range(1, tables.Features.Count)
                .Select(row => tables.Features.GetFeature(row).FeatureId),
            Does.Contain(13u).And.Contain(18u));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "PaletteModule", [], ["type.palette"]);
        Assert.That(reference.StandardClassDeclarations, Has.Length.EqualTo(2));
        Assert.That(reference.EnumDefinitions, Has.Length.EqualTo(1));
        var selectedPalette = reference.StandardClassDeclarations.Single(
            declaration => declaration.Declaration.Name == "Palette");
        Assert.That(selectedPalette.SelectedExportKeys,
            Is.EqualTo(["type.palette"]));
        var closureShelf = reference.StandardClassDeclarations.Single(
            declaration => declaration.Declaration.Name == "Shelf");
        Assert.That(closureShelf.ExportKeys, Is.Empty);
        Assert.That(closureShelf.SelectedExportKeys, Is.Empty);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            sourceFreeCompilation.Program!, "PaletteModule", schemaIdentity,
            1);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                reemitted, SchemaFor(schemaIdentity)));
        Assert.That(reemittedTables.TypeDefs.Count, Is.EqualTo(3));
        Assert.That(reemittedTables.Fields.Count, Is.EqualTo(4));
        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "type.palette", out _), Is.True);
        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "field.palette.shade", out _), Is.True);
        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "field.palette.shelf", out _), Is.True);
        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "field.palette.id", out _), Is.True);

        var reloadedReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "PaletteModule", [], ["type.palette"]);
        Assert.That(reloadedReference.StandardClassDeclarations,
            Has.Length.EqualTo(2));
        Assert.That(reloadedReference.EnumDefinitions, Has.Length.EqualTo(1));
    }

    [Test]
    public void ReemitsEN1LocalConstructorBodiesWithNominalEnumConstants()
    {
        var tree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 { Dark = -1 }
                public class Box {
                    public Shade Value;
                    public Box() { Value = Shade.Dark; }
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var fieldDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var classIndex = program.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(program, classIndex,
            classDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0,
                    fieldDeclaration.Span, "field.box.value"),
                CreateSourceClassConstructorExport(classDeclaration,
                    "constructor.box"),
            ]);
        var enumExport = CreateExport(program, tree, 0, "Shade",
            ["type.shade"], "shade");
        foreach (var schemaIdentity in new[] {
            s_schemaIdentities[0], s_schemaIdentities[1],
        })
        {
            var artifact = TrRmExecutableCompiler.EmitStandard(program,
                "EnumConstructor", schemaIdentity, 1, [], [], [classExport],
                [enumExport]);
            var input = new TrRmStandardArtifactInput(schemaIdentity,
                artifact);
            var reference = TrRmExecutableCompiler.CreateStandardReference(
                input, "EnumConstructor", [], ["type.box"]);
            var sourceFreeCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(string.Empty), reference);
            Assert.That(sourceFreeCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    sourceFreeCompilation.Diagnostics));
            var reemitted = TrRmExecutableCompiler.EmitStandard(
                sourceFreeCompilation.Program!, "EnumConstructor",
                schemaIdentity, 1);
            var tables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    reemitted, SchemaFor(schemaIdentity)));
            var classTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
                .Single(row => tables.TypeDefs.GetName(row) == "Box");
            var initializerMethodRow = Enumerable.Range(1,
                    tables.Methods.Count)
                .Single(row => tables.Methods.GetOwnerType(row)
                        == classTypeDefRow
                    && tables.Contracts.HasConstructionClause(row)
                    && tables.Contracts.GetConstructionClause(row)
                        .RoleTag == 1);
            var initializerBody = tables.Bodies.GetRegisterBody(
                tables.Methods.GetBodyRow(initializerMethodRow),
                tables.Contracts.Count);
            Assert.That(initializerBody.ConstantCount, Is.EqualTo(1),
                schemaIdentity);
            var enumConstantTypeSpec
                = BinaryPrimitives.ReadUInt32LittleEndian(
                    initializerBody.ConstantMap);
            var enumConstantType = tables.TypeSpecs.GetTypeSpecRef(
                enumConstantTypeSpec);
            Assert.That(enumConstantType.Form, Is.EqualTo((byte)0x02),
                schemaIdentity);
            Assert.That(tables.TypeDefs.GetKind(enumConstantType.Row),
                Is.EqualTo((byte)4), schemaIdentity);
            Assert.That(tables.TypeDefs.GetName(enumConstantType.Row),
                Is.EqualTo("Shade"), schemaIdentity);
            var constantSourceRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    initializerBody.ConstantMap.Slice(sizeof(uint))));
            var materialized = TrRmConstantSourceValueValidator
                .MaterializeFrozenScalar(constantSourceRow,
                    enumConstantTypeSpec, 1,
                    tables.Modules.GetRequirements(1).TargetRequirement,
                    tables.ConstantSources, tables.FrozenValueNodes,
                    tables.Blobs, tables.TypeSpecs, tables.Features,
                    tables.Modules, tables.TypeDefs, tables.EnumDefs,
                    tables.Imports, tables.A3LayoutResourceLedger,
                    tables.Fields);
            Assert.That(BoundEnumTypes.IsType(materialized.Type), Is.True,
                schemaIdentity);
            Assert.That(materialized.Value, Is.EqualTo(-1), schemaIdentity);
            Assert.That(Enumerable.Range(1, tables.Features.Count)
                    .Select(row => tables.Features.GetFeature(row).FeatureId),
                Does.Contain(11u), schemaIdentity);
            if (schemaIdentity == s_schemaIdentities[1])
            {
                var projectionMethodRow = Enumerable.Range(1,
                        tables.Methods.Count)
                    .Single(methodRow =>
                            tables.Methods.GetOwnerType(methodRow)
                                == classTypeDefRow
                            && tables.Contracts.HasConstructionClause(
                                methodRow)
                            && tables.Contracts.GetConstructionClause(
                                methodRow).RoleTag == 2);
                var projectionBody = tables.Bodies.GetRegisterBody(
                    tables.Methods.GetBodyRow(projectionMethodRow),
                    tables.Contracts.Count);
                Assert.That(projectionBody.MethodMap.Length,
                    Is.EqualTo(sizeof(uint)), schemaIdentity);
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                        projectionBody.MethodMap),
                    Is.EqualTo(0x1600_0000u
                        | checked((uint)initializerMethodRow)),
                    schemaIdentity);
            }
        }
    }

    [Test]
    public void DoesNotEmitConstruction2ProjectionForIncompleteInitializer()
    {
        var schemaIdentity = s_schemaIdentities[1];
        var tree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box {
                    public int32 Value;
                    public int32 Remaining;
                    public Box(int32 value) { Value = value; }
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var classIndex = program.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var constructorDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ConstructorDeclaration);
        var fields = classDeclaration.Children.OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.FieldDeclaration)
            .ToArray();
        var classExport = new TrRmSourceClassExport(program, classIndex,
            classDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0, fields[0].Span,
                    "field.box.value"),
                new TrRmSourceClassMemberExport(0, fields[1].Span,
                    "field.box.remaining"),
                new TrRmSourceClassMemberExport(0,
                    constructorDeclaration.Span, "constructor.box"),
            ]);
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "IncompleteBox", schemaIdentity, 1, [], [], [classExport], []);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                artifact, SchemaFor(schemaIdentity)));
        Assert.That(tables.Methods.Count, Is.EqualTo(1));
        var initializerMethod = Enumerable.Range(1, tables.Methods.Count)
            .Single(row =>
                tables.Contracts.HasConstructionClause(row)
                && tables.Contracts.GetConstructionClause(row).RoleTag
                    == 1);
        Assert.That(tables.Contracts.GetConstructionClause(
                initializerMethod).InitializedMemberDefIds,
            Has.Length.EqualTo(1));

        var constructorExport = classExport.MemberExports[^1] with {
            ConstructionProjectionExportKey = "constructor.box.create",
        };
        var projectionClassExport = classExport with {
            MemberExports = classExport.MemberExports.SetItem(
                classExport.MemberExports.Length - 1, constructorExport),
        };
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "IncompleteBox",
                schemaIdentity, 1, [], [], [projectionClassExport], []));
    }

    [Test]
    public void EmitsInitializerCopyEvidenceForNormalClassHandleFields()
    {
        var tree = SyntaxTree.Parse("""
            namespace Demo {
                public class Child {
                    public int32 Value;
                    public Child(int32 value) { Value = value; }
                }
                public class Owner {
                    public Child ChildValue;
                    public Owner(Child child) { ChildValue = child; }
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classExports = ImmutableArray.Create(
            CreateSourceClassExport(program, tree, "Child",
                "type.child", "constructor.child", (0, "field.child.value")),
            CreateSourceClassExport(program, tree, "Owner",
                "type.owner", "constructor.owner", (0, "field.owner.child")));
        foreach (var schemaIdentity in s_initializerCopyEvidenceSchemaIdentities)
        {
            var providerArtifact = EmitClassArtifact(program,
                "ClassHandleCopyProvider", schemaIdentity, classExports);
            var tables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    providerArtifact, SchemaFor(schemaIdentity)));
            var childTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
                .Single(row => tables.TypeDefs.GetName(row) == "Child");
            var ownerTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
                .Single(row => tables.TypeDefs.GetName(row) == "Owner");
            var childFieldRow = Enumerable.Range(1, tables.Fields.Count)
                .Single(row => tables.Fields.GetOwnerTypeDef(row)
                        == ownerTypeDefRow
                    && tables.Fields.GetName(row) == "ChildValue");
            var childTypeSpec = tables.TypeSpecs.GetTypeSpecRef(
                tables.Fields.GetTypeSpecRef(childFieldRow));

            Assert.That((tables.TypeDefs.GetFlags(childTypeDefRow) & 0x40u),
                Is.Zero, schemaIdentity);
            Assert.That(childTypeSpec.Form, Is.EqualTo((byte)0x02),
                schemaIdentity);
            Assert.That(childTypeSpec.Row, Is.EqualTo(childTypeDefRow),
                schemaIdentity);
            var uniqueChildArtifact = providerArtifact.ToArray();
            SetTypeDefFlags(uniqueChildArtifact, childTypeDefRow,
                tables.TypeDefs.GetFlags(childTypeDefRow) | 0x40u);
            Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(
                        new TrRmArtifactIndex(uniqueChildArtifact,
                            SchemaFor(schemaIdentity))));

            var reference = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    providerArtifact),
                "ClassHandleCopyProvider", [], [
                    "type.child", "type.owner",
                ]);
            var sourceFreeCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(string.Empty), reference);
            Assert.That(sourceFreeCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    sourceFreeCompilation.Diagnostics));
            var reemitted = TrRmExecutableCompiler.EmitStandard(
                sourceFreeCompilation.Program!,
                "ClassHandleCopyProvider", schemaIdentity, 1);
            var reemittedTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    reemitted, SchemaFor(schemaIdentity)));
            Assert.That(reemittedTables.Methods.Count,
                Is.GreaterThan(0), schemaIdentity);
            Assert.That(reemittedTables.Bodies.Count,
                Is.GreaterThan(0), schemaIdentity);
        }
    }

    [Test]
    public void EmitsDeclaredUniqueEvidenceForInitializerCopySchemas()
    {
        var tree = SyntaxTree.Parse("""
            namespace Demo {
                public unique class Child {
                    public int32 Value;
                    public Child(int32 value) { Value = value; }
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classExport = CreateSourceClassExport(program, tree, "Child",
            "type.child", "constructor.child", (0, "field.child.value"));
        foreach (var schemaIdentity in s_initializerCopyEvidenceSchemaIdentities)
        {
            var artifact = EmitClassArtifact(program, "UniqueClassProvider",
                schemaIdentity, [classExport]);
            var tables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    artifact, SchemaFor(schemaIdentity)));
            var childTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
                .Single(row => tables.TypeDefs.GetName(row) == "Child");

            Assert.That((tables.TypeDefs.GetFlags(childTypeDefRow) & 0x40u),
                Is.Not.Zero, schemaIdentity);
            var reference = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "UniqueClassProvider", [], ["type.child"]);
            var sourceFreeCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(string.Empty), reference);
            Assert.That(sourceFreeCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    sourceFreeCompilation.Diagnostics));
            var reemitted = TrRmExecutableCompiler.EmitStandard(
                sourceFreeCompilation.Program!, "UniqueClassProvider",
                schemaIdentity, 1);
            var reemittedTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    reemitted, SchemaFor(schemaIdentity)));
            var reemittedChildTypeDefRow = Enumerable.Range(1,
                    reemittedTables.TypeDefs.Count)
                .Single(row => reemittedTables.TypeDefs.GetName(row)
                    == "Child");
            Assert.That((reemittedTables.TypeDefs.GetFlags(
                    reemittedChildTypeDefRow) & 0x40u),
                Is.Not.Zero, schemaIdentity);
        }
    }

    [Test]
    public void EmitsDeclaredCopyEvidenceForInitializerStructFields()
    {
        var tree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Payload { public int32 Value; }
                public class Owner {
                    public Payload PayloadValue;
                    public Owner(Payload payload) {
                        PayloadValue = payload;
                    }
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classExports = ImmutableArray.Create(
            CreateSourceClassExport(program, tree, "Owner",
                "type.owner", "constructor.owner",
                (0, "field.owner.payload")));
        foreach (var schemaIdentity in s_initializerCopyEvidenceSchemaIdentities)
        {
            var artifact = EmitClassArtifact(program, "StructCopyProvider",
                schemaIdentity, classExports);
            var tables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    artifact, SchemaFor(schemaIdentity)));
            var payloadTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
                .Single(row => tables.TypeDefs.GetName(row) == "Payload");
            var ownerTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
                .Single(row => tables.TypeDefs.GetName(row) == "Owner");
            var payloadFieldRow = Enumerable.Range(1, tables.Fields.Count)
                .Single(row => tables.Fields.GetOwnerTypeDef(row)
                        == ownerTypeDefRow
                    && tables.Fields.GetName(row) == "PayloadValue");
            var payloadTypeSpec = tables.TypeSpecs.GetTypeSpecRef(
                tables.Fields.GetTypeSpecRef(payloadFieldRow));

            Assert.That((tables.TypeDefs.GetFlags(payloadTypeDefRow) & 0x20u),
                Is.Not.Zero, schemaIdentity);
            Assert.That(payloadTypeSpec.Form, Is.EqualTo((byte)0x02),
                schemaIdentity);
            Assert.That(payloadTypeSpec.Row, Is.EqualTo(payloadTypeDefRow),
                schemaIdentity);
            var nonCopyPayloadArtifact = artifact.ToArray();
            SetTypeDefFlags(nonCopyPayloadArtifact, payloadTypeDefRow,
                tables.TypeDefs.GetFlags(payloadTypeDefRow) & ~0x20u);
            Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(
                        new TrRmArtifactIndex(nonCopyPayloadArtifact,
                            SchemaFor(schemaIdentity))));

            var reference = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "StructCopyProvider", [], ["type.owner"]);
            var sourceFreeCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(string.Empty), reference);
            Assert.That(sourceFreeCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    sourceFreeCompilation.Diagnostics));
            var reemitted = TrRmExecutableCompiler.EmitStandard(
                sourceFreeCompilation.Program!, "StructCopyProvider",
                schemaIdentity, 1);
            var reemittedTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    reemitted, SchemaFor(schemaIdentity)));
            var reemittedPayloadRow = Enumerable.Range(1,
                    reemittedTables.TypeDefs.Count)
                .Single(row => reemittedTables.TypeDefs.GetName(row)
                    == "Payload");
            Assert.That((reemittedTables.TypeDefs.GetFlags(
                    reemittedPayloadRow) & 0x20u),
                Is.Not.Zero, schemaIdentity);
        }
    }

    [Test]
    public void RequiresMatchingImportedStructCopyEvidence()
    {
        foreach (var schemaIdentity in s_initializerCopyEvidenceSchemaIdentities
            .Skip(5))
        {
            var providerTree = SyntaxTree.Parse("""
                namespace Demo {
                    public struct Payload { public int32 Value; }
                    public class Anchor {
                        public int32 Value;
                        public Anchor(int32 value) { Value = value; }
                    }
                }
                """);
            var providerCompilation = Compilation.CreateLibrary(providerTree);
            Assert.That(providerCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    providerCompilation.Diagnostics));
            var providerProgram = (BoundProgram)providerCompilation.Program!;
            var payloadExport = new TrRmSourceTypeExport("Demo", "Payload",
                "type.payload") {
                FieldExports = [
                    new TrRmSourceFieldExport("Value",
                        "field.payload.value"),
                ],
            };
            var anchorExport = CreateSourceClassExport(providerProgram,
                providerTree, "Anchor", "type.anchor", "constructor.anchor",
                (0, "field.anchor.value"));
            var providerArtifact = TrRmExecutableCompiler.EmitStandard(
                providerProgram, "CopyProvider", schemaIdentity, 1,
                [payloadExport], [], [anchorExport]);
            var providerTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    providerArtifact, SchemaFor(schemaIdentity)));
            var payloadTypeDefRow = Enumerable.Range(1,
                    providerTables.TypeDefs.Count)
                .Single(row => providerTables.TypeDefs.GetName(row)
                    == "Payload");
            Assert.That((providerTables.TypeDefs.GetFlags(
                    payloadTypeDefRow) & 0x20u),
                Is.Not.Zero, schemaIdentity);

            var providerReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(schemaIdentity,
                        providerArtifact),
                    "CopyProvider", [], ["type.payload", "type.anchor"]);
            var consumerTree = SyntaxTree.Parse("""
                namespace Demo {
                    public class Owner {
                        public Payload Value;
                        public Owner(Payload value) { Value = value; }
                    }
                }
                """);
            var consumerCompilation = Compilation.CreateLibrary(
                consumerTree, providerReference);
            Assert.That(consumerCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    consumerCompilation.Diagnostics));
            var consumerProgram = (BoundProgram)consumerCompilation.Program!;
            var ownerExport = CreateSourceClassExport(consumerProgram,
                consumerTree, "Owner", "type.owner", "constructor.owner",
                (0, "field.owner.payload"));
            var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
                consumerProgram, "CopyConsumer", schemaIdentity, 1, [], [],
                [ownerExport]);

            var reference = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact),
                "CopyConsumer", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        providerArtifact),
                ], ["type.owner"]);
            Assert.That(reference.Structures.Single(
                    static structure => structure.Name == "Demo.Payload")
                    .DeclaredCopyEvidence,
                Is.True, schemaIdentity);

            var relocatedProviderTree = SyntaxTree.Parse("""
                namespace Demo {
                    public struct Decoy { public uint8 Byte; }
                    public struct Payload { public int32 Value; }
                    public class Anchor {
                        public int32 Value;
                        public Anchor(int32 value) { Value = value; }
                    }
                }
                """);
            var relocatedProviderCompilation
                = Compilation.CreateLibrary(relocatedProviderTree);
            Assert.That(relocatedProviderCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    relocatedProviderCompilation.Diagnostics));
            var relocatedProviderProgram
                = (BoundProgram)relocatedProviderCompilation.Program!;
            var decoyExport = new TrRmSourceTypeExport("Demo", "Decoy",
                "type.decoy") {
                FieldExports = [
                    new TrRmSourceFieldExport("Byte",
                        "field.decoy.byte"),
                ],
            };
            var relocatedPayloadExport
                = new TrRmSourceTypeExport("Demo", "Payload",
                    "type.payload") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Value",
                            "field.payload.value"),
                    ],
                };
            var relocatedAnchorExport = CreateSourceClassExport(
                relocatedProviderProgram, relocatedProviderTree, "Anchor",
                "type.anchor", "constructor.anchor",
                (0, "field.anchor.value"));
            var relocatedProviderArtifact
                = TrRmExecutableCompiler.EmitStandard(
                    relocatedProviderProgram, "CopyProvider",
                    schemaIdentity, 1, [decoyExport, relocatedPayloadExport],
                    [], [relocatedAnchorExport]);
            var relocatedProviderTables
                = TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(
                        new TrRmArtifactIndex(relocatedProviderArtifact,
                            SchemaFor(schemaIdentity)));
            var relocatedPayloadTypeDefRow
                = Enumerable.Range(1,
                        relocatedProviderTables.TypeDefs.Count)
                    .Single(row => relocatedProviderTables.TypeDefs
                        .GetName(row) == "Payload");
            Assert.That(relocatedPayloadTypeDefRow,
                Is.Not.EqualTo(payloadTypeDefRow), schemaIdentity);
            var relocatedReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(schemaIdentity,
                        consumerArtifact),
                    "CopyConsumer", [
                        new TrRmStandardArtifactInput(schemaIdentity,
                            relocatedProviderArtifact),
                    ], ["type.owner"]);
            Assert.That(relocatedReference.Structures.Single(
                    static structure => structure.Name == "Demo.Payload")
                    .DeclaredCopyEvidence,
                Is.True, schemaIdentity);
            var relocatedSourceFreeCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(string.Empty), relocatedReference);
            Assert.That(relocatedSourceFreeCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    relocatedSourceFreeCompilation.Diagnostics));
            var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
                relocatedSourceFreeCompilation.Program!, "CopyConsumer",
                schemaIdentity, 1);
            var reemittedTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    reemittedArtifact, SchemaFor(schemaIdentity)));
            var reemittedPayloadTypeDefRow
                = Enumerable.Range(1, reemittedTables.TypeDefs.Count)
                    .Single(row => reemittedTables.TypeDefs.GetName(row)
                        == "Payload");
            Assert.That((reemittedTables.TypeDefs.GetFlags(
                    reemittedPayloadTypeDefRow) & 0x20u),
                Is.Not.Zero, schemaIdentity);
            var reloadedReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(schemaIdentity,
                        reemittedArtifact),
                    "CopyConsumer", [
                        new TrRmStandardArtifactInput(schemaIdentity,
                            relocatedProviderArtifact),
                    ], ["type.owner"]);
            Assert.That(reloadedReference.Structures.Single(
                    static structure => structure.Name == "Demo.Payload")
                    .DeclaredCopyEvidence,
                Is.True, schemaIdentity);

            var missingCopyProvider = providerArtifact.ToArray();
            SetTypeDefFlags(missingCopyProvider, payloadTypeDefRow,
                providerTables.TypeDefs.GetFlags(payloadTypeDefRow) & ~0x20u);
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(schemaIdentity,
                        consumerArtifact),
                    "CopyConsumer", [
                        new TrRmStandardArtifactInput(schemaIdentity,
                            missingCopyProvider),
                    ], ["type.owner"]));

            var incompatibleProviderTree = SyntaxTree.Parse("""
                namespace Demo {
                    public struct Payload { public int64 Value; }
                    public class Anchor {
                        public int32 Value;
                        public Anchor(int32 value) { Value = value; }
                    }
                }
                """);
            var incompatibleProviderCompilation
                = Compilation.CreateLibrary(incompatibleProviderTree);
            Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    incompatibleProviderCompilation.Diagnostics));
            var incompatibleProviderProgram
                = (BoundProgram)incompatibleProviderCompilation.Program!;
            var incompatiblePayloadExport
                = new TrRmSourceTypeExport("Demo", "Payload",
                    "type.payload") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Value",
                            "field.payload.value"),
                    ],
                };
            var incompatibleAnchorExport = CreateSourceClassExport(
                incompatibleProviderProgram, incompatibleProviderTree,
                "Anchor", "type.anchor", "constructor.anchor",
                (0, "field.anchor.value"));
            var incompatibleProviderArtifact
                = TrRmExecutableCompiler.EmitStandard(
                    incompatibleProviderProgram, "CopyProvider",
                    schemaIdentity, 1, [incompatiblePayloadExport], [],
                    [incompatibleAnchorExport]);
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(schemaIdentity,
                        consumerArtifact),
                    "CopyConsumer", [
                        new TrRmStandardArtifactInput(schemaIdentity,
                            incompatibleProviderArtifact),
                    ], ["type.owner"]));
        }
    }

    [Test]
    public void ReemitsImportedConstructorMethodExpectations()
    {
        foreach (var schemaIdentity in
            s_initializerCopyEvidenceSchemaIdentities)
        {
            var providerTree = SyntaxTree.Parse("""
                namespace Demo {
                    public class Child {
                        public int32 Value;
                        public Child(int32 value) { Value = value; }
                    }
                }
                """);
            var providerCompilation = Compilation.CreateLibrary(providerTree);
            Assert.That(providerCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    providerCompilation.Diagnostics));
            var providerProgram = (BoundProgram)providerCompilation.Program!;
            var schema = SchemaFor(schemaIdentity);
            var providerClassExport
                = schema.UsesObjectConstruction()
                    ? CreateSourceClassExportWithProjection(
                        providerProgram, providerTree, "Child",
                        "type.child", "constructor.child",
                        "constructor.child.project",
                        (0, "field.child.value"))
                    : CreateSourceClassExport(providerProgram,
                        providerTree, "Child", "type.child",
                        "constructor.child", (0, "field.child.value"));
            var providerArtifact = EmitClassArtifact(providerProgram,
                "ChildProvider", schemaIdentity, [providerClassExport]);
            var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
                providerArtifact);
            var providerReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    providerInput, "ChildProvider", [], ["type.child"]);

            var consumerTree = SyntaxTree.Parse("""
                namespace Demo {
                    public class Owner {
                        public Child Value;
                        public Owner(Child value) { Value = value; }
                    }
                }
                """);
            var consumerCompilation = Compilation.CreateLibrary(
                consumerTree, providerReference);
            Assert.That(consumerCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    consumerCompilation.Diagnostics));
            var consumerProgram = (BoundProgram)consumerCompilation.Program!;
            var ownerExport = CreateSourceClassExport(consumerProgram,
                consumerTree, "Owner", "type.owner", "constructor.owner",
                (0, "field.owner.child"));
            var consumerArtifact = EmitClassArtifact(consumerProgram,
                "OwnerConsumer", schemaIdentity, [ownerExport]);
            var consumerTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    consumerArtifact, SchemaFor(schemaIdentity)));
            var ownerTypeDefRow = Enumerable.Range(1,
                    consumerTables.TypeDefs.Count)
                .Single(row => consumerTables.TypeDefs.GetName(row)
                    == "Owner");
            var ownerInitializerRow = Enumerable.Range(1,
                    consumerTables.Methods.Count)
                .Single(row => consumerTables.Methods.GetOwnerType(row)
                        == ownerTypeDefRow
                    && consumerTables.Contracts
                        .HasConstructionClause(row)
                    && consumerTables.Contracts.GetConstructionClause(row)
                        .RoleTag == 1);
            var ownerInitializerBodyRow = consumerTables.Methods
                .GetBodyRow(ownerInitializerRow);
            var ownerInitializerBody = consumerTables.Bodies.GetRegisterBody(
                ownerInitializerBodyRow, consumerTables.Contracts.Count);
            Assert.That(ownerInitializerBody.FieldCount,
                Is.GreaterThan(0), schemaIdentity);
            var malformedFieldMap = ReplaceFirstBodyFieldMapToken(
                consumerArtifact, schemaIdentity, ownerInitializerBodyRow,
                0x1400_0000u
                    | checked((uint)(consumerTables.Fields.Count + 1)));
            Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(
                        new TrRmArtifactIndex(malformedFieldMap,
                            SchemaFor(schemaIdentity))));

            var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
                consumerArtifact);
            var reference = TrRmExecutableCompiler.CreateStandardReference(
                consumerInput, "OwnerConsumer", [providerInput],
                ["type.owner"]);
            var childDeclaration = reference.StandardClassDeclarations.Single(
                static declaration =>
                    declaration.Declaration.Name == "Child"
                    && !declaration.Expectations.IsEmpty);
            var expectedMethodCount = schema.UsesObjectConstruction()
                ? 2 : 1;
            Assert.That(childDeclaration.Expectations.Single()
                .MethodExpectations, Has.Length.EqualTo(expectedMethodCount),
                schemaIdentity);
            var incompatibleProviderTree = SyntaxTree.Parse("""
                namespace Demo {
                    public class Child {
                        public int32 Value;
                        public Child(int64 value) { Value = 0; }
                    }
                }
                """);
            var incompatibleProviderCompilation
                = Compilation.CreateLibrary(incompatibleProviderTree);
            Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    incompatibleProviderCompilation.Diagnostics));
            var incompatibleProviderProgram
                = (BoundProgram)incompatibleProviderCompilation.Program!;
            var incompatibleProviderClassExport
                = schema.UsesObjectConstruction()
                    ? CreateSourceClassExportWithProjection(
                        incompatibleProviderProgram,
                        incompatibleProviderTree, "Child",
                        "type.child", "constructor.child",
                        "constructor.child.project",
                        (0, "field.child.value"))
                    : CreateSourceClassExport(
                        incompatibleProviderProgram,
                        incompatibleProviderTree, "Child", "type.child",
                        "constructor.child", (0, "field.child.value"));
            var incompatibleProviderArtifact = EmitClassArtifact(
                incompatibleProviderProgram, "ChildProvider",
                schemaIdentity, [incompatibleProviderClassExport]);
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.CreateStandardReference(
                    consumerInput, "OwnerConsumer", [
                        new TrRmStandardArtifactInput(schemaIdentity,
                            incompatibleProviderArtifact),
                    ], ["type.owner"]));

            var sourceFreeCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(string.Empty), reference);
            Assert.That(sourceFreeCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    sourceFreeCompilation.Diagnostics));
            var reemitted = TrRmExecutableCompiler.EmitStandard(
                sourceFreeCompilation.Program!, "OwnerConsumer",
                schemaIdentity, 1);
            var reemittedTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    reemitted, SchemaFor(schemaIdentity)));
            var reemittedMethodImports = Enumerable.Range(1,
                    reemittedTables.Imports.Count)
                .Where(importRow => reemittedTables.Imports
                    .GetExpectedDefinitionForImportRow(importRow) >> 24
                        == 0x16)
                .ToArray();
            Assert.That(reemittedMethodImports,
                Has.Length.EqualTo(expectedMethodCount), schemaIdentity);
            var childTypeDefRow = Enumerable.Range(1,
                    reemittedTables.TypeDefs.Count)
                .Single(row => reemittedTables.TypeDefs.GetName(row)
                    == "Child");
            var childMethodRows = Enumerable.Range(1,
                    reemittedTables.Methods.Count)
                .Where(row => reemittedTables.Methods.GetOwnerType(row)
                    == childTypeDefRow)
                .ToArray();
            Assert.That(childMethodRows,
                Has.Length.EqualTo(expectedMethodCount),
                schemaIdentity);
            var childMethodRoles = new List<byte>(childMethodRows.Length);
            foreach (var methodRow in childMethodRows)
            {
                Assert.That(reemittedTables.Methods.GetBodyRow(methodRow),
                    Is.Zero, schemaIdentity);
                Assert.That(reemittedTables.Imports.IsExpectedDefinition(
                    0x1600_0000u | checked((uint)methodRow)),
                    Is.True, schemaIdentity);
                Assert.That(reemittedTables.Contracts.HasConstructionClause(
                    methodRow), Is.True, schemaIdentity);
                childMethodRoles.Add(reemittedTables.Contracts
                    .GetConstructionClause(methodRow).RoleTag);
            }

            var expectedRoles = schema.UsesObjectConstruction()
                ? new byte[] { 1, 2 } : new byte[] { 1 };
            Assert.That(childMethodRoles.OrderBy(static role => role),
                Is.EqualTo(expectedRoles), schemaIdentity);
            var initializerRow = childMethodRows.Single(methodRow =>
                reemittedTables.Contracts.GetConstructionClause(methodRow)
                    .RoleTag == 1);
            var initializedField = reemittedTables.Contracts
                .GetConstructionClause(initializerRow)
                .InitializedMemberDefIds.Single();
            Assert.That(initializedField >> 24, Is.EqualTo(0x14u),
                schemaIdentity);
            Assert.That(reemittedTables.Imports.IsExpectedDefinition(
                initializedField), Is.True, schemaIdentity);
            if (schema.UsesObjectConstruction())
            {
                Assert.That(reemittedTables.Contracts
                    .GetConstructionClause(childMethodRows.Single(
                        methodRow =>
                            reemittedTables.Contracts
                                .GetConstructionClause(methodRow).RoleTag
                                == 2)).InitializerMethodDefRow,
                    Is.EqualTo((uint)initializerRow), schemaIdentity);
            }

            var reloadedReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(schemaIdentity,
                        reemitted),
                    "OwnerConsumer", [providerInput], ["type.owner"]);
            var reloadedChildDeclaration
                = reloadedReference.StandardClassDeclarations.Single(
                    static declaration =>
                        declaration.Declaration.Name == "Child"
                        && !declaration.Expectations.IsEmpty);
            Assert.That(reloadedChildDeclaration.Expectations.Single()
                    .MethodExpectations,
                Has.Length.EqualTo(expectedMethodCount), schemaIdentity);
        }
    }

    [Test]
    public void BindsEN1ClassFieldOffsetOnlyWithExactFieldSelection()
    {
        var schemaIdentity = s_schemaIdentities[0];
        var tree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box {
                    public int32 Value;
                    public Box(int32 value) { Value = value; }
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var fieldDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var classIndex = program.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(program, classIndex,
            classDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0,
                    fieldDeclaration.Span, "field.box.value"),
                CreateSourceClassConstructorExport(classDeclaration,
                    "constructor.box"),
            ]);
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ClassQuery", schemaIdentity, 1, [], [], [classExport], []);
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var selectedReference
            = TrRmExecutableCompiler.CreateStandardReference(input,
                "ClassQuery", [], ["type.box"], ["field.box.value"]);
        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Value));
                }
                """), selectedReference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });

        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                queryCompilation.Diagnostics));
        var unscopedQueryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return offsetof(Box, Value);
                }
                """), selectedReference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(unscopedQueryCompilation.Diagnostics.Any(
            static diagnostic => diagnostic.Message.Contains(
                "requires an explicit unsafe context",
                StringComparison.Ordinal)), Is.True);
        var unsafeDeclarationCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                unsafe nint Offset() {
                    return offsetof(Box, Value);
                }
                """), selectedReference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(unsafeDeclarationCompilation.Diagnostics.Any(
            static diagnostic => diagnostic.Message.Contains(
                "requires an explicit unsafe context",
                StringComparison.Ordinal)), Is.True);
        var query = (BoundLayoutQuery?)null;
        var function = queryCompilation.Program!.Functions.Single();
        Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
            expression => query ??= expression as BoundLayoutQuery), Is.True);
        Assert.That(query, Is.Not.Null);
        Assert.That(query!.Kind,
            Is.EqualTo(BoundLayoutQueryKind.OffsetOfType));
        Assert.That(query.Subject.FieldPath?.Steps, Has.Length.EqualTo(1));
        var step = query.Subject.FieldPath!.Steps[0];
        Assert.That(step.StandardClassFieldOrigin?.SelectedExportKeys,
            Is.EqualTo(["field.box.value"]));
        Assert.That(step.FieldType, Is.EqualTo(PrimitiveType.Int32));

        var typeOnlyReference
            = TrRmExecutableCompiler.CreateStandardReference(input,
                "ClassQuery", [], ["type.box"]);
        var typeOnlyCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Value));
                }
                """), typeOnlyReference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(typeOnlyCompilation.HasErrors, Is.True);
        Assert.That(typeOnlyCompilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains(
                "no selected Standard FieldDef ExportKey",
                StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void SelectsClassInstanceLayoutThroughExactSchemaSuccessors()
    {
        var sourceTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box {
                    public int32 Value;
                    public Box(int32 value) { Value = value; }
                }
            }
            """);
        var sourceCompilation = Compilation.CreateLibrary(sourceTree);
        Assert.That(sourceCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceCompilation.Diagnostics));
        var program = (BoundProgram)sourceCompilation.Program!;
        var classDeclaration = sourceTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var fieldDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var classIndex = program.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(program, classIndex,
            classDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0,
                    fieldDeclaration.Span, "field.box.value"),
                CreateSourceClassConstructorExport(classDeclaration,
                    "constructor.box"),
            ]);
        var changedProviderTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box {
                    public int64 Value;
                    public Box(int64 value) { Value = value; }
                }
            }
            """);
        var changedProviderCompilation
            = Compilation.CreateLibrary(changedProviderTree);
        Assert.That(changedProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                changedProviderCompilation.Diagnostics));
        var changedProviderProgram
            = (BoundProgram)changedProviderCompilation.Program!;
        var changedClassDeclaration = changedProviderTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var changedFieldDeclaration = changedClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var changedClassIndex
            = changedProviderProgram.SourceClassProgramAssociation!
                .Declarations.Single(declaration =>
                    declaration.Span == changedClassDeclaration.Span)
                .ClassIndex;
        var changedClassExport = new TrRmSourceClassExport(
            changedProviderProgram, changedClassIndex,
            changedClassDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0,
                    changedFieldDeclaration.Span, "field.box.value"),
                CreateSourceClassConstructorExport(
                    changedClassDeclaration, "constructor.box"),
            ]);

        foreach (var baseSchemaIdentity in s_schemaIdentities)
        {
            var schemaIdentity
                = $"{baseSchemaIdentity}-class-instance-layout-1";
            var artifact = TrRmExecutableCompiler.EmitStandard(program,
                "ClassLayoutProvider", schemaIdentity, 1, [], [],
                [classExport], []);
            var input = new TrRmStandardArtifactInput(schemaIdentity,
                artifact);
            var reference = TrRmExecutableCompiler.CreateStandardReference(
                input, "ClassLayoutProvider", [], ["type.box"],
                ["field.box.value"]);
            Assert.That(reference.StandardClassDeclarations.Single(
                static declaration =>
                    !declaration.SelectedExportKeys.IsEmpty)
                .Declaration.Fields.Single(
                    static field => field.Name == "Value")
                .SelectedExportKeys, Is.EqualTo(["field.box.value"]));

            var queryCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse("""
                    using Demo;
                    nint Offset() {
                        return unsafe(offsetof(Box, Value));
                    }
                    """), reference, new CompilationOptions {
                    LayoutCoreRegistration
                        = LayoutCoreRegistrationTestData.Create(),
                });
            Assert.That(queryCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    queryCompilation.Diagnostics));
            var emittedConsumer = TrRmExecutableCompiler.EmitStandard(
                queryCompilation.Program!, "ClassLayoutConsumer",
                schemaIdentity, 1);
            var consumerTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    emittedConsumer, SchemaFor(schemaIdentity), null,
                    usesNativePointerLoads: false,
                    standardSchemaIdentity: schemaIdentity));
            Assert.That(consumerTables.Methods.Count, Is.EqualTo(1));
            Assert.That(consumerTables.TargetLayoutDependencies.Count,
                Is.EqualTo(1));
            Assert.That(consumerTables.TargetLayoutDependencyFields.Count,
                Is.EqualTo(1));
            Assert.That(consumerTables.TargetLayoutDependencies
                .GetQueryKind(1), Is.EqualTo((byte)3));
            Assert.That(consumerTables.TargetLayoutDependencies
                .GetTargetProfileRow(1), Is.EqualTo(1));
            Assert.That(consumerTables.TargetLayoutDependencies
                .GetFieldRange(1).Count, Is.EqualTo(1));
            var subjectType = consumerTables.TypeSpecs.GetTypeSpecRef(
                consumerTables.TargetLayoutDependencies
                    .GetSubjectTypeSpecRef(1));
            Assert.That(subjectType.Form, Is.EqualTo((byte)0x03));
            Assert.That(consumerTables.Imports.GetExpectedDefinitionForImportRow(
                subjectType.Row) >> 24, Is.EqualTo(0x13u));
            Assert.That(subjectType,
                Is.EqualTo(consumerTables.TypeSpecs.GetTypeSpecRef(
                    consumerTables.FieldPaths.GetRootTypeSpecRef(1))));
            Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(
                        new TrRmArtifactIndex(emittedConsumer,
                            SchemaFor(baseSchemaIdentity))));
            var queryBody = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(1),
                consumerTables.Contracts.Count);
            Assert.That(queryBody.HasLayoutQueryOperation, Is.True);
            var unusedProviderArtifact = TrRmExecutableCompiler.EmitStandard(
                program, "UnusedClassLayoutProvider", schemaIdentity, 1, [],
                [], [classExport], []);
            var unusedProviderInput = new TrRmStandardArtifactInput(
                schemaIdentity, unusedProviderArtifact);
            var providerInputs = new[] { unusedProviderInput, input };
            var loadedClassQuery = TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    emittedConsumer), "ClassLayoutConsumer", providerInputs);
            Assert.That(loadedClassQuery.StandardClassDeclarations
                .Single(static declaration =>
                    !declaration.SelectedExportKeys.IsEmpty)
                .Declaration.Provider.ModuleIdentity,
                Is.EqualTo("ClassLayoutProvider"));
            BoundLayoutQuery? loadedQuery = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(
                loadedClassQuery.Functions.Single().Body,
                expression => loadedQuery ??=
                    expression as BoundLayoutQuery), Is.True);
            Assert.That(loadedQuery, Is.Not.Null);
            Assert.That(loadedQuery!.Subject.RequiresUnsafe, Is.True);
            Assert.That(loadedQuery.Subject.IsUnsafeAuthorized, Is.True);
            Assert.That(loadedQuery.Subject.FieldPath!.Steps.Single()
                .StandardClassFieldOrigin!.SelectedExportKeys,
                Is.EqualTo(["field.box.value"]));
            Assert.That(NativeEmitter.EmitLibrary(loadedClassQuery,
                ["Offset"], NativeRuntimePolicy.FailFast), Is.Not.Empty);
            Assert.That(NativeModuleCompiler.EmitStandardLibrary(
                new TrRmStandardArtifactInput(schemaIdentity,
                    emittedConsumer), "ClassLayoutConsumer", providerInputs,
                ["Offset"], NativeRuntimePolicy.FailFast), Is.Not.Empty);
            var reemittedClassQuery = TrRmExecutableCompiler.EmitStandard(
                loadedClassQuery, "ClassLayoutConsumer", schemaIdentity, 1);
            var reemittedTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    reemittedClassQuery, SchemaFor(schemaIdentity), null,
                    usesNativePointerLoads: false,
                    standardSchemaIdentity: schemaIdentity));
            Assert.That(reemittedTables.TargetLayoutDependencies.Count,
                Is.EqualTo(1));
            Assert.That(reemittedTables.TargetLayoutDependencyFields.Count,
                Is.EqualTo(1));
            Assert.That(reemittedTables.ConstantExecutions.Count, Is.Zero);
            Assert.That(reemittedTables.TargetLayoutDependencies
                .GetQueryKind(1), Is.EqualTo((byte)3));
            Assert.That(reemittedTables.TargetLayoutDependencies
                .GetFieldRange(1).Count, Is.EqualTo(1));
            var reloadedClassQuery = TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    reemittedClassQuery), "ClassLayoutConsumer",
                providerInputs);
            Assert.That(reloadedClassQuery.Functions.Single().Body
                .Statements.Single(), Is.TypeOf<BoundReturn>());
            var changedProviderArtifact
                = TrRmExecutableCompiler.EmitStandard(
                    changedProviderProgram, "ClassLayoutProvider",
                    schemaIdentity, 1, [], [], [changedClassExport], []);
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(schemaIdentity,
                        emittedConsumer), "ClassLayoutConsumer", [
                        new TrRmStandardArtifactInput(schemaIdentity,
                            changedProviderArtifact),
                    ]));

            var staticArtifact = AsStaticClassField(artifact,
                SchemaFor(schemaIdentity));
            Assert.Throws<ArgumentException>(() =>
                TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(schemaIdentity,
                        staticArtifact), "ClassLayoutProvider", [],
                    ["type.box"], ["field.box.value"]));

            var baseArtifact = TrRmExecutableCompiler.EmitStandard(program,
                "UnusedProvider", baseSchemaIdentity, 1, [], [],
                [classExport], []);
            Assert.Throws<NotSupportedException>(() =>
                TrRmExecutableCompiler.CreateStandardReference(input,
                    "ClassLayoutProvider", [
                        new TrRmStandardArtifactInput(baseSchemaIdentity,
                            baseArtifact),
                    ], ["type.box"], ["field.box.value"]));
        }
    }

    [Test]
    public void ComposesClassLayoutReferencesFromDistinctProviders()
    {
        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        var options = new CompilationOptions();

        static (TrRmStandardArtifactInput Input,
            CompilationReference Reference) CreateProviderReference(
            string moduleIdentity, string className, string typeExportKey,
            string fieldExportKey, string fieldType, string schemaIdentity,
            uint targetRequirement, CompilationOptions options)
        {
            var tree = SyntaxTree.Parse($$"""
                namespace Demo {
                    public class {{className}} {
                        public {{fieldType}} Value;
                    }
                }
                """);
            var compilation = Compilation.CreateLibrary(tree, options);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = (BoundProgram)compilation.Program!;
            var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.ClassDeclaration);
            var fieldDeclaration = classDeclaration.Children
                .OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.FieldDeclaration);
            var classIndex = program.SourceClassProgramAssociation!
                .Declarations.Single(declaration =>
                    declaration.Span == classDeclaration.Span).ClassIndex;
            var classExport = new TrRmSourceClassExport(program, classIndex,
                classDeclaration.Span, typeExportKey, [
                    new TrRmSourceClassMemberExport(0,
                        fieldDeclaration.Span, fieldExportKey),
                ]);
            var artifact = TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, targetRequirement, [], [],
                [classExport], []);
            var input = new TrRmStandardArtifactInput(schemaIdentity,
                artifact);
            return (input,
                TrRmExecutableCompiler.CreateStandardReference(input,
                    moduleIdentity, [], [typeExportKey], [fieldExportKey]));
        }

        var alpha = CreateProviderReference("AlphaProvider",
            "Alpha", "type.alpha", "field.alpha.value", "int32",
            schemaIdentity, targetRequirement, options);
        var beta = CreateProviderReference("BetaProvider", "Beta",
            "type.beta", "field.beta.value", "int32", schemaIdentity,
            targetRequirement, options);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [alpha.Reference, beta.Reference]);

        Assert.That(reference.StandardClassDeclarations, Has.Length.EqualTo(2));
        Assert.That(reference.StandardClassDeclarations
                .Select(static declaration =>
                    declaration.Declaration.Provider.ModuleIdentity),
            Is.EquivalentTo(["AlphaProvider", "BetaProvider"]));
        Assert.That(reference.StandardClassDeclarations.All(
                static declaration =>
                    !declaration.Declaration.Layouts.IsEmpty),
            Is.True);
        var incompatibleAlpha = CreateProviderReference("AlphaProvider",
            "Alpha", "type.alpha", "field.alpha.value", "int64",
            schemaIdentity, targetRequirement, options);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReferenceSet([
                alpha.Reference, incompatibleAlpha.Reference,
            ]));

        var alphaConsumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint AlphaOffset() {
                    return unsafe(offsetof(Alpha, Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(alphaConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, alphaConsumer.Diagnostics));
        var alphaArtifact = TrRmExecutableCompiler.EmitStandard(
            alphaConsumer.Program!, "AlphaConsumer", schemaIdentity,
            targetRequirement);
        var alphaLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, alphaArtifact),
            "AlphaConsumer", [alpha.Input]);
        Assert.That(alphaLoaded.Functions.Single().Name,
            Is.EqualTo("AlphaOffset"));

        var betaConsumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint BetaOffset() {
                    return unsafe(offsetof(Beta, Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(betaConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, betaConsumer.Diagnostics));
        var betaArtifact = TrRmExecutableCompiler.EmitStandard(
            betaConsumer.Program!, "BetaConsumer", schemaIdentity,
            targetRequirement);
        var betaLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, betaArtifact),
            "BetaConsumer", [beta.Input]);
        Assert.That(betaLoaded.Functions.Single().Name,
            Is.EqualTo("BetaOffset"));

        var combinedConsumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint CombinedOffset() {
                    return unsafe(offsetof(Alpha, Value))
                        + unsafe(offsetof(Beta, Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(combinedConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, combinedConsumer.Diagnostics));
        var combinedArtifact = TrRmExecutableCompiler.EmitStandard(
            combinedConsumer.Program!, "CombinedConsumer", schemaIdentity,
            targetRequirement);
        var combinedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                combinedArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(combinedTables.TargetLayoutDependencies.Count,
            Is.EqualTo(2));
        Assert.That(combinedTables.ModuleRefs.Count, Is.EqualTo(2));
        var combinedLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, combinedArtifact),
            "CombinedConsumer", [alpha.Input, beta.Input]);
        Assert.That(combinedLoaded.Functions.Single().Name,
            Is.EqualTo("CombinedOffset"));
    }

    [Test]
    public void ComposesClassLayoutReferencesWithInlineStructFields()
    {
        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Leaf {
                    public int32 Value;
                }
                public class Child {
                    public int32 Id;
                }
                public struct Payload {
                    public Leaf Header;
                    public Child Handle;
                }
                public class Alpha {
                    public Payload Data;
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var classDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(node =>
                node.Kind == SyntaxKind.ClassDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                    token.Token.Kind == TokenKind.Identifier
                    && token.Token.Name == "Alpha"));
        var classField = classDeclaration.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var classIndex = providerProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(providerProgram,
            classIndex, classDeclaration.Span, "type.alpha", [
                new TrRmSourceClassMemberExport(0, classField.Span,
                    "field.alpha.data"),
            ]);
        var childClassDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(node =>
                node.Kind == SyntaxKind.ClassDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                    token.Token.Kind == TokenKind.Identifier
                    && token.Token.Name == "Child"));
        var childClassField = childClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var childClassIndex = providerProgram
            .SourceClassProgramAssociation!.Declarations.Single(
                declaration =>
                    declaration.Span == childClassDeclaration.Span)
            .ClassIndex;
        var childClassExport = new TrRmSourceClassExport(providerProgram,
            childClassIndex, childClassDeclaration.Span, "type.child", [
                new TrRmSourceClassMemberExport(0, childClassField.Span,
                    "field.child.id"),
            ]);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "AlphaStructProvider", schemaIdentity,
            targetRequirement, [
                new TrRmSourceTypeExport("Demo", "Leaf", "type.leaf") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Value",
                            "field.leaf.value"),
                    ],
                },
                new TrRmSourceTypeExport("Demo", "Payload",
                    "type.payload") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Header",
                            "field.payload.header"),
                    ],
                },
            ], [], [classExport, childClassExport], []);
        var alphaInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var alphaReference = TrRmExecutableCompiler.CreateStandardReference(
            alphaInput, "AlphaStructProvider", [], [
                "type.alpha", "type.child", "type.leaf", "type.payload",
            ], [
                "field.alpha.data", "field.leaf.value",
                "field.payload.header", "field.child.id",
            ]);

        var betaTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Record {
                    public int64 Value;
                }
                public class Beta {
                    public Record Data;
                }
            }
            """);
        var betaCompilation = Compilation.CreateLibrary(betaTree);
        Assert.That(betaCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, betaCompilation.Diagnostics));
        var betaProgram = (BoundProgram)betaCompilation.Program!;
        var betaDeclaration = betaTree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var betaField = betaDeclaration.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var betaStruct = betaTree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.StructDeclaration);
        var betaStructField = betaStruct.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var betaClassIndex = betaProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == betaDeclaration.Span).ClassIndex;
        var betaClassExport = new TrRmSourceClassExport(betaProgram,
            betaClassIndex, betaDeclaration.Span, "type.beta", [
                new TrRmSourceClassMemberExport(0, betaField.Span,
                    "field.beta.data"),
            ]);
        var betaArtifact = TrRmExecutableCompiler.EmitStandard(betaProgram,
            "BetaProvider", schemaIdentity, targetRequirement, [
                new TrRmSourceTypeExport("Demo", "Record",
                    "type.record") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Value",
                            "field.record.value"),
                    ],
                },
            ], [],
            [betaClassExport], []);
        var betaInput = new TrRmStandardArtifactInput(schemaIdentity,
            betaArtifact);
        var betaReference = TrRmExecutableCompiler.CreateStandardReference(
            betaInput, "BetaProvider", [],
            ["type.beta", "type.record"],
            ["field.beta.data", "field.record.value"]);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [alphaReference, betaReference]);

        Assert.That(reference.Structures, Has.Length.EqualTo(3));
        var alphaDataType = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Alpha")
            .Declaration.Fields.Single().Type;
        var betaDataType = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Beta")
            .Declaration.Fields.Single().Type;
        Assert.That(reference.Structures[(int)alphaDataType - 100].Name,
            Is.EqualTo("Demo.Payload"));
        Assert.That(reference.Structures[(int)betaDataType - 100].Name,
            Is.EqualTo("Demo.Record"));
        var childDeclaration = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Child");
        var payloadHandleType = reference.Structures.Single(
            static structure => structure.Name == "Demo.Payload")
            .Fields.Single(static field => field.Name == "Handle").Type;
        Assert.That(BoundGenericNominalTypeTypes.IsType(payloadHandleType),
            Is.True);
        Assert.That(reference.GenericNominalTypeInstances[
                BoundGenericNominalTypeTypes.Index(payloadHandleType)]
                .Definition,
            Is.EqualTo(childDeclaration.Declaration.Identity));

        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint CombinedOffset() {
                    return unsafe(offsetof(Alpha, Data.Header.Value))
                        + unsafe(offsetof(Beta, Data.Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            consumer.Program!, "InlineStructConsumer", schemaIdentity,
            targetRequirement);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                artifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(tables.TargetLayoutDependencies.Count,
            Is.EqualTo(2));
        Assert.That(tables.TargetLayoutDependencyFields.Count,
            Is.EqualTo(5));
        Assert.That(tables.ModuleRefs.Count, Is.EqualTo(2));
        var payloadTypeDefRow = Enumerable.Range(
                1, tables.TypeDefs.Count)
            .Single(row =>
                tables.TypeDefs.GetName(row) == "Payload"
                && tables.Modules.GetIdentity(
                    tables.TypeDefs.GetOwnerModule(row))
                    == "InlineStructConsumer");
        var payloadFieldRange
            = tables.TypeDefs.GetFieldRange(payloadTypeDefRow);
        var payloadHandleFieldRow = Enumerable.Range(
                payloadFieldRange.Start,
                payloadFieldRange.End - payloadFieldRange.Start)
            .Single(row => tables.Fields.GetName(row) == "Handle");
        var payloadHandleTypeSpec = tables.TypeSpecs.GetTypeSpecRef(
            tables.Fields.GetTypeSpecRef(payloadHandleFieldRow));
        Assert.That(payloadHandleTypeSpec.Form, Is.EqualTo((byte)0x03));
        var payloadHandleImport = tables.Imports
            .GetBindingForImportRow(payloadHandleTypeSpec.Row);
        Assert.That(payloadHandleImport.ProducerIdentity,
            Is.EqualTo("AlphaStructProvider"));
        Assert.That(payloadHandleImport.ExportKey, Is.EqualTo("type.child"));
        var imports = Enumerable.Range(1, tables.Imports.Count)
            .Select(tables.Imports.GetBindingForImportRow).ToArray();
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "AlphaStructProvider"
                && binding.ExportKey == "type.payload"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "AlphaStructProvider"
                && binding.ExportKey == "type.leaf"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "AlphaStructProvider"
                && binding.ExportKey == "field.payload.header"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "AlphaStructProvider"
                && binding.ExportKey == "field.leaf.value"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "BetaProvider"
                && binding.ExportKey == "type.record"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "BetaProvider"
                && binding.ExportKey == "field.record.value"),
            Is.True);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "InlineStructConsumer", [alphaInput, betaInput]);
        Assert.That(loaded.Functions.Single().Name,
            Is.EqualTo("CombinedOffset"));
        var loadedPayload = loaded.Structures.Single(
            static structure => structure.Name == "Demo.Payload");
        var loadedHandleType = loadedPayload.Fields.Single(
            static field => field.Name == "Handle").Type;
        Assert.That(BoundGenericNominalTypeTypes.IsType(loadedHandleType),
            Is.True);
        var loadedChild = loaded.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Child");
        Assert.That(loaded.GenericNominalTypeInstances[
                BoundGenericNominalTypeTypes.Index(loadedHandleType)]
                .Definition,
            Is.EqualTo(loadedChild.Declaration.Identity));
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "InlineStructConsumerRemapped", schemaIdentity,
            targetRequirement);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "InlineStructConsumerRemapped",
            [alphaInput, betaInput]);
        var reloadedPayload = reloaded.Structures.Single(
            static structure => structure.Name == "Demo.Payload");
        var reloadedHandleType = reloadedPayload.Fields.Single(
            static field => field.Name == "Handle").Type;
        Assert.That(BoundGenericNominalTypeTypes.IsType(reloadedHandleType),
            Is.True);
        var reloadedChild = reloaded.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Child");
        Assert.That(reloaded.GenericNominalTypeInstances[
                BoundGenericNominalTypeTypes.Index(reloadedHandleType)]
                .Definition,
            Is.EqualTo(reloadedChild.Declaration.Identity));
    }

    [Test]
    public void ComposesClassLayoutReferencesWithEnumFields()
    {
        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        var alphaTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 { Dark = -1 }
                public class Alpha {
                    public Shade ShadeValue;
                }
            }
            """);
        var alphaCompilation = Compilation.CreateLibrary(alphaTree);
        Assert.That(alphaCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, alphaCompilation.Diagnostics));
        var alphaProgram = (BoundProgram)alphaCompilation.Program!;
        var alphaClassDeclaration = alphaTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var alphaField = alphaClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var alphaClassIndex = alphaProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == alphaClassDeclaration.Span).ClassIndex;
        var alphaClassExport = new TrRmSourceClassExport(alphaProgram,
            alphaClassIndex, alphaClassDeclaration.Span, "type.alpha", [
                new TrRmSourceClassMemberExport(0, alphaField.Span,
                    "field.alpha.shade"),
            ]);
        var alphaEnumExport = CreateExport(alphaProgram, alphaTree, 0,
            "Shade", ["type.shade"], "shade");
        var alphaArtifact = TrRmExecutableCompiler.EmitStandard(
            alphaProgram, "AlphaEnumProvider", schemaIdentity,
            targetRequirement, [], [], [alphaClassExport],
            [alphaEnumExport]);
        alphaArtifact = AddExportAlias(alphaArtifact, schemaIdentity,
            "type.alpha", "type.alpha.alias");
        alphaArtifact = AddExportAlias(alphaArtifact, schemaIdentity,
            "type.shade", "type.shade.alias");
        alphaArtifact = AddExportAlias(alphaArtifact, schemaIdentity,
            "field.alpha.shade", "field.alpha.shade.alias");
        var alphaInput = new TrRmStandardArtifactInput(schemaIdentity,
            alphaArtifact);
        var alphaReference = TrRmExecutableCompiler.CreateStandardReference(
            alphaInput, "AlphaEnumProvider", [], [
                "type.alpha", "type.shade",
            ], ["field.alpha.shade"]);
        var alphaAliasReference
            = TrRmExecutableCompiler.CreateStandardReference(alphaInput,
                "AlphaEnumProvider", [], [
                    "type.alpha.alias", "type.shade.alias",
                ], ["field.alpha.shade.alias"]);

        var betaTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Tone : int16 { Warm = 1 }
                public class Beta {
                    public int32 Value;
                }
            }
            """);
        var betaCompilation = Compilation.CreateLibrary(betaTree);
        Assert.That(betaCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, betaCompilation.Diagnostics));
        var betaProgram = (BoundProgram)betaCompilation.Program!;
        var betaClassDeclaration = betaTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var betaField = betaClassDeclaration.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var betaClassIndex = betaProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == betaClassDeclaration.Span).ClassIndex;
        var betaClassExport = new TrRmSourceClassExport(betaProgram,
            betaClassIndex, betaClassDeclaration.Span, "type.beta", [
                new TrRmSourceClassMemberExport(0, betaField.Span,
                    "field.beta.value"),
            ]);
        var betaEnumExport = CreateExport(betaProgram, betaTree, 0,
            "Tone", ["type.tone"], "tone");
        var betaArtifact = TrRmExecutableCompiler.EmitStandard(betaProgram,
            "BetaProvider", schemaIdentity, targetRequirement, [], [],
            [betaClassExport], [betaEnumExport]);
        var betaInput = new TrRmStandardArtifactInput(schemaIdentity,
            betaArtifact);
        var betaReference = TrRmExecutableCompiler.CreateStandardReference(
            betaInput, "BetaProvider", [], ["type.beta", "type.tone"],
            ["field.beta.value"]);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [alphaReference, alphaAliasReference, betaReference]);

        Assert.That(reference.EnumDefinitions, Has.Length.EqualTo(2));
        Assert.That(((BoundEnumDeclarationIdentity.Standard)
                reference.EnumDefinitions[0].Identity).Value
                .SelectedExportKeys,
            Is.EqualTo(["type.shade", "type.shade.alias"]));
        Assert.That(BoundEnumTypes.IsType(
            reference.StandardClassDeclarations.Single(
                static declaration =>
                    declaration.Declaration.Name == "Alpha")
                .Declaration.Fields.Single().Type), Is.True);

        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint CombinedOffset() {
                    return unsafe(offsetof(Alpha, ShadeValue))
                        + unsafe(offsetof(Beta, Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            consumer.Program!, "EnumLayoutConsumer", schemaIdentity,
            targetRequirement);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                artifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(tables.TargetLayoutDependencies.Count,
            Is.EqualTo(2));
        Assert.That(Enumerable.Range(1, tables.Imports.Count)
                .Select(tables.Imports.GetBindingForImportRow)
                .Any(static binding =>
                    binding.ProducerIdentity == "AlphaEnumProvider"
                    && binding.ExportKey == "type.shade"),
            Is.True);
        Assert.That(Enumerable.Range(1, tables.Imports.Count)
                .Select(tables.Imports.GetBindingForImportRow)
                .Any(static binding =>
                    binding.ProducerIdentity == "BetaProvider"
                    && binding.ExportKey == "type.tone"),
            Is.True);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "EnumLayoutConsumer", [alphaInput, betaInput]);
        Assert.That(loaded.Functions.Single().Name,
            Is.EqualTo("CombinedOffset"));
    }

    [Test]
    public void ComposesClassLayoutReferencesWithInlineStructEnumFields()
    {
        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        var alphaTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Hue : int8 { Red = 1 }
                public struct Palette {
                    public Hue Color;
                }
                public class Alpha {
                    public Palette Data;
                }
            }
            """);
        var alphaCompilation = Compilation.CreateLibrary(alphaTree);
        Assert.That(alphaCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, alphaCompilation.Diagnostics));
        var alphaProgram = (BoundProgram)alphaCompilation.Program!;
        var alphaClassDeclaration = alphaTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var alphaField = alphaClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var alphaClassIndex = alphaProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == alphaClassDeclaration.Span).ClassIndex;
        var alphaClassExport = new TrRmSourceClassExport(alphaProgram,
            alphaClassIndex, alphaClassDeclaration.Span, "type.alpha", [
                new TrRmSourceClassMemberExport(0, alphaField.Span,
                    "field.alpha.data"),
            ]);
        var paletteExport = new TrRmSourceTypeExport("Demo", "Palette",
            "type.palette") {
            FieldExports = [
                new TrRmSourceFieldExport("Color",
                    "field.palette.color"),
            ],
        };
        var hueExport = CreateExport(alphaProgram, alphaTree, 0,
            "Hue", ["type.hue"], "hue");
        var alphaArtifact = TrRmExecutableCompiler.EmitStandard(
            alphaProgram, "AlphaEnumProvider", schemaIdentity,
            targetRequirement, [paletteExport], [], [alphaClassExport],
            [hueExport]);
        var alphaInput = new TrRmStandardArtifactInput(schemaIdentity,
            alphaArtifact);
        var alphaReference = TrRmExecutableCompiler.CreateStandardReference(
            alphaInput, "AlphaEnumProvider", [], [
                "type.alpha", "type.palette", "type.hue",
            ], ["field.alpha.data", "field.palette.color"]);

        var betaTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Tone : int16 { Warm = 1 }
                public struct Record {
                    public Tone Value;
                }
                public class Beta {
                    public Record Data;
                }
            }
            """);
        var betaCompilation = Compilation.CreateLibrary(betaTree);
        Assert.That(betaCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, betaCompilation.Diagnostics));
        var betaProgram = (BoundProgram)betaCompilation.Program!;
        var betaClassDeclaration = betaTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var betaField = betaClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var betaClassIndex = betaProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == betaClassDeclaration.Span).ClassIndex;
        var betaClassExport = new TrRmSourceClassExport(betaProgram,
            betaClassIndex, betaClassDeclaration.Span, "type.beta", [
                new TrRmSourceClassMemberExport(0, betaField.Span,
                    "field.beta.data"),
            ]);
        var recordExport = new TrRmSourceTypeExport("Demo", "Record",
            "type.record") {
            FieldExports = [
                new TrRmSourceFieldExport("Value",
                    "field.record.value"),
            ],
        };
        var toneExport = CreateExport(betaProgram, betaTree, 0,
            "Tone", ["type.tone"], "tone");
        var betaArtifact = TrRmExecutableCompiler.EmitStandard(betaProgram,
            "BetaProvider", schemaIdentity, targetRequirement,
            [recordExport], [],
            [betaClassExport], [toneExport]);
        var betaInput = new TrRmStandardArtifactInput(schemaIdentity,
            betaArtifact);
        var betaReference = TrRmExecutableCompiler.CreateStandardReference(
            betaInput, "BetaProvider", [],
            ["type.beta", "type.record", "type.tone"],
            ["field.beta.data", "field.record.value"]);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [alphaReference, betaReference]);

        var palette = reference.Structures.Single(
            static structure => structure.Name == "Demo.Palette");
        Assert.That(BoundEnumTypes.IsType(palette.Fields.Single().Type),
            Is.True);
        var record = reference.Structures.Single(
            static structure => structure.Name == "Demo.Record");
        Assert.That(BoundEnumTypes.IsType(record.Fields.Single().Type),
            Is.True);
        Assert.That(reference.EnumDefinitions[
                BoundEnumTypes.Index(record.Fields.Single().Type)].Name,
            Is.EqualTo("Tone"));

        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint CombinedOffset() {
                    return unsafe(offsetof(Alpha, Data.Color))
                        + unsafe(offsetof(Beta, Data.Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            consumer.Program!, "NestedEnumLayoutConsumer", schemaIdentity,
            targetRequirement);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                artifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(tables.TargetLayoutDependencies.Count,
            Is.EqualTo(2));
        Assert.That(tables.TargetLayoutDependencyFields.Count,
            Is.EqualTo(4));
        var imports = Enumerable.Range(1, tables.Imports.Count)
            .Select(tables.Imports.GetBindingForImportRow).ToArray();
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "AlphaEnumProvider"
                && binding.ExportKey == "type.hue"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "AlphaEnumProvider"
                && binding.ExportKey == "type.palette"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "AlphaEnumProvider"
                && binding.ExportKey == "field.palette.color"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "BetaProvider"
                && binding.ExportKey == "type.record"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "BetaProvider"
                && binding.ExportKey == "field.record.value"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "BetaProvider"
                && binding.ExportKey == "type.tone"),
            Is.True);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "NestedEnumLayoutConsumer", [alphaInput, betaInput]);
        Assert.That(loaded.Functions.Single().Name,
            Is.EqualTo("CombinedOffset"));
    }

    [Test]
    public void LowersEN1ClassQueryWithImportedEnumInInlineStruct()
    {
        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        var enumTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Hue : int16 { Warm = 1 }
            }
            """);
        var enumCompilation = Compilation.CreateLibrary(enumTree);
        Assert.That(enumCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, enumCompilation.Diagnostics));
        var enumProgram = (BoundProgram)enumCompilation.Program!;
        var enumExport = CreateExport(enumProgram, enumTree, 0,
            "Hue", ["type.hue"], "hue");
        var enumArtifact = TrRmExecutableCompiler.EmitStandard(enumProgram,
            "HueProvider", schemaIdentity, targetRequirement, [], [], [],
            [enumExport]);
        var enumInput = new TrRmStandardArtifactInput(schemaIdentity,
            enumArtifact);
        var enumReference = TrRmExecutableCompiler.CreateStandardReference(
            enumInput, "HueProvider", [], ["type.hue"]);

        var alphaTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Palette {
                    public Hue Color;
                }
                public class Alpha {
                    public Palette Data;
                }
            }
            """);
        var alphaCompilation = Compilation.CreateLibrary(alphaTree,
            enumReference);
        Assert.That(alphaCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                alphaCompilation.Diagnostics));
        var alphaProgram = (BoundProgram)alphaCompilation.Program!;
        var alphaClassDeclaration = alphaTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var alphaField = alphaClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var alphaClassIndex = alphaProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == alphaClassDeclaration.Span).ClassIndex;
        var alphaClassExport = new TrRmSourceClassExport(alphaProgram,
            alphaClassIndex, alphaClassDeclaration.Span, "type.alpha", [
                new TrRmSourceClassMemberExport(0, alphaField.Span,
                    "field.alpha.data"),
            ]);
        var paletteExport = new TrRmSourceTypeExport("Demo", "Palette",
            "type.palette") {
            FieldExports = [
                new TrRmSourceFieldExport("Color",
                    "field.palette.color"),
            ],
        };
        var alphaArtifact = TrRmExecutableCompiler.EmitStandard(
            alphaProgram, "AlphaProvider", schemaIdentity,
            targetRequirement, [paletteExport], [], [alphaClassExport], []);
        var alphaInput = new TrRmStandardArtifactInput(schemaIdentity,
            alphaArtifact);
        var alphaReference = TrRmExecutableCompiler.CreateStandardReference(
            alphaInput, "AlphaProvider", [enumInput], [
                "type.alpha", "type.palette",
            ], ["field.alpha.data", "field.palette.color"]);
        Assert.That(alphaReference.EnumDefinitions, Has.Length.EqualTo(1));
        Assert.That(((BoundEnumDeclarationIdentity.Standard)
                alphaReference.EnumDefinitions[0].Identity).Value
                .Expectations, Has.Length.EqualTo(1));

        var betaTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Record {
                    public Hue Value;
                }
                public class Beta {
                    public Record Data;
                }
            }
            """);
        var betaCompilation = Compilation.CreateLibrary(betaTree,
            enumReference);
        Assert.That(betaCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                betaCompilation.Diagnostics));
        var betaProgram = (BoundProgram)betaCompilation.Program!;
        var betaClassDeclaration = betaTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var betaField = betaClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var recordExport = new TrRmSourceTypeExport("Demo", "Record",
            "type.record") {
            FieldExports = [
                new TrRmSourceFieldExport("Value",
                    "field.record.value"),
            ],
        };
        var betaClassIndex = betaProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == betaClassDeclaration.Span).ClassIndex;
        var betaClassExport = new TrRmSourceClassExport(betaProgram,
            betaClassIndex, betaClassDeclaration.Span, "type.beta", [
                new TrRmSourceClassMemberExport(0, betaField.Span,
                    "field.beta.data"),
            ]);
        var betaArtifact = TrRmExecutableCompiler.EmitStandard(betaProgram,
            "BetaProvider", schemaIdentity, targetRequirement,
            [recordExport], [],
            [betaClassExport], []);
        var betaInput = new TrRmStandardArtifactInput(schemaIdentity,
            betaArtifact);
        var betaReference = TrRmExecutableCompiler.CreateStandardReference(
            betaInput, "BetaProvider", [enumInput],
            ["type.beta", "type.record"],
            ["field.beta.data", "field.record.value"]);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [alphaReference, betaReference]);
        Assert.That(reference.EnumDefinitions, Has.Length.EqualTo(1));
        Assert.That(((BoundEnumDeclarationIdentity.Standard)
                reference.EnumDefinitions[0].Identity).Value
                .Expectations, Has.Length.EqualTo(2));

        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint CombinedOffset() {
                    return unsafe(offsetof(Alpha, Data.Color))
                        + unsafe(offsetof(Beta, Data.Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            consumer.Program!, "ImportedEnumLayoutConsumer", schemaIdentity,
            targetRequirement);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                artifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(tables.TargetLayoutDependencies.Count,
            Is.EqualTo(2));
        Assert.That(tables.TargetLayoutDependencyFields.Count,
            Is.EqualTo(4));
        Assert.That(Enumerable.Range(1, tables.Imports.Count)
                .Select(tables.Imports.GetBindingForImportRow)
                .Any(static binding =>
                    binding.ProducerIdentity == "HueProvider"
                    && binding.ExportKey == "type.hue"),
            Is.True);
        Assert.That(Enumerable.Range(1, tables.Imports.Count)
                .Select(tables.Imports.GetBindingForImportRow)
                .Any(static binding =>
                    binding.ProducerIdentity == "AlphaProvider"
                    && binding.ExportKey == "field.palette.color"),
            Is.True);
        Assert.That(Enumerable.Range(1, tables.Imports.Count)
                .Select(tables.Imports.GetBindingForImportRow)
                .Any(static binding =>
                    binding.ProducerIdentity == "BetaProvider"
                    && binding.ExportKey == "type.record"),
            Is.True);
        Assert.That(Enumerable.Range(1, tables.Imports.Count)
                .Select(tables.Imports.GetBindingForImportRow)
                .Any(static binding =>
                    binding.ProducerIdentity == "BetaProvider"
                    && binding.ExportKey == "field.record.value"),
            Is.True);
        Assert.That(Enumerable.Range(1, tables.Imports.Count)
                .Select(tables.Imports.GetBindingForImportRow)
                .Count(static binding =>
                    binding.ProducerIdentity == "HueProvider"
                    && binding.ExportKey == "type.hue"),
            Is.EqualTo(1));
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "ImportedEnumLayoutConsumer",
            [alphaInput, enumInput, betaInput]);
        Assert.That(loaded.Functions.Single().Name,
            Is.EqualTo("CombinedOffset"));
    }

    [Test]
    public void UsesFirstExplicitAliasesForEN1ClassQueryBodies()
    {
        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box {
                    public int32 Value;
                    public Box(int32 value) { Value = value; }
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var classDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var fieldDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var classIndex = providerProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(providerProgram,
            classIndex, classDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0,
                    fieldDeclaration.Span, "field.box.value"),
                CreateSourceClassConstructorExport(classDeclaration,
                    "constructor.box"),
            ]);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ClassLayoutProvider", schemaIdentity, 1,
            [], [], [classExport], []);
        providerArtifact = AddExportAlias(providerArtifact, schemaIdentity,
            "type.box", "type.box.alias");
        providerArtifact = AddExportAlias(providerArtifact, schemaIdentity,
            "field.box.value", "field.box.value.alias");
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "ClassLayoutProvider", [], [
                "type.box.alias",
                "type.box",
            ], [
                "field.box.value.alias",
                "field.box.value",
            ]);
        var selectedClass = reference.StandardClassDeclarations.Single(
            static declaration =>
                !declaration.SelectedExportKeys.IsEmpty);
        Assert.That(selectedClass.SelectedExportKeys,
            Is.EqualTo(["type.box.alias", "type.box"]));
        Assert.That(selectedClass.Declaration.Fields.Single(
            static field => field.Name == "Value").SelectedExportKeys,
            Is.EqualTo(["field.box.value.alias", "field.box.value"]));

        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, queryCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassLayoutConsumer",
            schemaIdentity, 1);
        var wrongFieldProvider = RewriteFieldImportExportKey(
            consumerArtifact, schemaIdentity, "field.box.value",
            "field.box.prefix");
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    wrongFieldProvider), "ClassLayoutConsumer",
                [providerInput]));
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(consumerTables.Imports.Count, Is.EqualTo(2));
        var rootType = consumerTables.TypeSpecs.GetTypeSpecRef(
            consumerTables.TargetLayoutDependencies
                .GetSubjectTypeSpecRef(1));
        Assert.That(rootType.Form, Is.EqualTo((byte)0x03));
        Assert.That(consumerTables.Imports.GetBindingForImportRow(
            checked((int)rootType.Row)).ExportKey,
            Is.EqualTo("type.box.alias"));
        var fieldReference
            = consumerTables.TargetLayoutDependencyFields
                .GetFieldReference(consumerTables.TargetLayoutDependencies
                    .GetFieldRange(1).First);
        Assert.That(fieldReference >> 24, Is.EqualTo(0x1Cu));
        Assert.That(consumerTables.Imports.GetBindingForImportRow(
            checked((int)(fieldReference & 0x00FF_FFFF))).ExportKey,
            Is.EqualTo("field.box.value.alias"));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", [providerInput]);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "ClassLayoutConsumer", schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "ClassLayoutConsumer", [providerInput]);
        Assert.That(NativeEmitter.EmitLibrary(reloaded, ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void LowersEN1ClassQueryFromCompleteProviderFieldLayout()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box {
                    public uint8 Prefix;
                    public int32 Value;
                    public Box(uint8 prefix, int32 value) {
                        Prefix = prefix;
                        Value = value;
                    }
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var classDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var fieldDeclarations = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.FieldDeclaration)
            .ToArray();
        var classIndex = providerProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(providerProgram,
            classIndex, classDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0,
                    fieldDeclarations[0].Span, "field.box.prefix"),
                new TrRmSourceClassMemberExport(0,
                    fieldDeclarations[1].Span, "field.box.value"),
                CreateSourceClassConstructorExport(classDeclaration,
                    "constructor.box"),
            ]);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ClassLayoutProvider", schemaIdentity, 1,
            [], [], [classExport], []);
        providerArtifact = AddExactClassLayoutVariant(providerArtifact,
            SchemaFor(schemaIdentity), 1, 1);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "ClassLayoutProvider", [], ["type.box"],
            ["field.box.value", "field.box.prefix"]);
        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Value))
                        + unsafe(offsetof(Box, Prefix))
                        + unsafe(offsetof(Box, Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, queryCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassLayoutConsumer",
            schemaIdentity, 1);
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(consumerTables.TargetLayoutDependencies.Count,
            Is.EqualTo(3));
        Assert.That(consumerTables.TargetLayoutDependencyFields.Count,
            Is.EqualTo(3));
        var reorderedDependencies = SwapTargetLayoutDependencyRows(
            consumerArtifact, schemaIdentity, 1, 2);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(reorderedDependencies,
                        SchemaFor(schemaIdentity))));
        var firstQueryFields
            = consumerTables.TargetLayoutDependencies.GetFieldRange(1);
        var secondQueryFields
            = consumerTables.TargetLayoutDependencies.GetFieldRange(2);
        var thirdQueryFields
            = consumerTables.TargetLayoutDependencies.GetFieldRange(3);
        Assert.That(firstQueryFields.Count, Is.EqualTo(1));
        Assert.That(secondQueryFields.Count, Is.EqualTo(1));
        Assert.That(thirdQueryFields.Count, Is.EqualTo(1));
        var firstFieldReference
            = consumerTables.TargetLayoutDependencyFields
                .GetFieldReference(firstQueryFields.First);
        var secondFieldReference
            = consumerTables.TargetLayoutDependencyFields
                .GetFieldReference(secondQueryFields.First);
        var thirdFieldReference
            = consumerTables.TargetLayoutDependencyFields
                .GetFieldReference(thirdQueryFields.First);
        Assert.That(firstFieldReference, Is.Not.EqualTo(secondFieldReference));
        Assert.That(thirdFieldReference, Is.EqualTo(firstFieldReference));
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", [providerInput]);
        var returned = (BoundReturn)loaded.Functions.Single().Body
            .Statements.Single();
        BoundLayoutQuery? query = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            loaded.Functions.Single().Body,
            expression => query ??= expression as BoundLayoutQuery), Is.True);
        var main = loaded.Functions.Single() with {
            Name = "Main",
            ReturnType = PrimitiveType.Int32,
            Body = new BoundBlock([
                new BoundReturn(new BoundCast(returned.Value!,
                    PrimitiveType.Int32, default), default),
            ], default),
        };
        var executable = loaded with {
            Functions = [main],
            EntryPoint = 0,
        };

        Assert.That(NativeJit.Execute(executable,
            NativeRuntimePolicy.FailFast), Is.EqualTo(2));
        Assert.That(NativeEmitter.Emit(executable,
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
        Assert.That(NativeEmitter.EmitLibrary(loaded, ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
        Assert.That(NativeModuleCompiler.EmitStandardLibrary(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", [providerInput], ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
        Assert.That(query!.Subject.RequiresUnsafe, Is.True);

        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "ClassLayoutConsumer", schemaIdentity, 1);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                reemittedArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(reemittedTables.TargetLayoutDependencies.Count,
            Is.EqualTo(3));
        Assert.That(reemittedTables.TargetLayoutDependencyFields.Count,
            Is.EqualTo(3));
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "ClassLayoutConsumer", [providerInput]);
        Assert.That(NativeEmitter.EmitLibrary(reloaded, ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);

        TrRmSourceClassExport CreateRelocatedClassExport(
            BoundProgram program, SyntaxTree tree, string name,
            string typeKey, string constructorKey,
            params string[] fieldKeys)
        {
            var declaration = tree.Root.Children.OfType<SyntaxNode>()
                .Single(node => node.Kind == SyntaxKind.ClassDeclaration
                    && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                        token.Token.Kind == TokenKind.Identifier
                        && token.Token.Name == name));
            var fields = declaration.Children.OfType<SyntaxNode>()
                .Where(static node =>
                    node.Kind == SyntaxKind.FieldDeclaration).ToArray();
            if (fields.Length != fieldKeys.Length)
            {
                throw new InvalidDataException(
                    "The relocated class fixture has an incomplete FieldDef key list.");
            }

            var classIndex = program.SourceClassProgramAssociation!
                .Declarations.Single(item =>
                    item.Span == declaration.Span).ClassIndex;
            var memberExports = fields.Select((field, index) =>
                    new TrRmSourceClassMemberExport(0, field.Span,
                        fieldKeys[index]))
                .ToImmutableArray()
                .Add(CreateSourceClassConstructorExport(declaration,
                    constructorKey));
            return new TrRmSourceClassExport(program, classIndex,
                declaration.Span, typeKey, memberExports);
        }

        var relocatedProviderTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Padding {
                    public int32 Unused;
                    public Padding(int32 unused) { Unused = unused; }
                }
                public class Box {
                    public uint8 Prefix;
                    public int32 Value;
                    public Box(uint8 prefix, int32 value) {
                        Prefix = prefix;
                        Value = value;
                    }
                }
            }
            """);
        var relocatedProviderCompilation
            = Compilation.CreateLibrary(relocatedProviderTree);
        Assert.That(relocatedProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                relocatedProviderCompilation.Diagnostics));
        var relocatedProviderProgram
            = (BoundProgram)relocatedProviderCompilation.Program!;
        var relocatedProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                relocatedProviderProgram, "ClassLayoutProvider",
                schemaIdentity, 1, [], [], [
                    CreateRelocatedClassExport(relocatedProviderProgram,
                        relocatedProviderTree, "Padding",
                        "type.padding", "constructor.padding",
                        "field.padding.unused"),
                    CreateRelocatedClassExport(relocatedProviderProgram,
                        relocatedProviderTree, "Box", "type.box",
                        "constructor.box", "field.box.prefix",
                        "field.box.value"),
                ], []);
        relocatedProviderArtifact = AddExactClassLayoutVariant(
            relocatedProviderArtifact, SchemaFor(schemaIdentity), 1, 1);
        var relocatedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, relocatedProviderArtifact);
        var originalProviderTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    providerArtifact, SchemaFor(schemaIdentity)));
        var relocatedProviderTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    relocatedProviderArtifact, SchemaFor(schemaIdentity)));
        var originalBoxDefinition = originalProviderTables.Exports
            .GetExportsForModule(1).Single(static export =>
                export.ExportKey == "type.box").Definition;
        var relocatedBoxDefinition = relocatedProviderTables.Exports
            .GetExportsForModule(1).Single(static export =>
                export.ExportKey == "type.box").Definition;
        Assert.That(relocatedBoxDefinition,
            Is.Not.EqualTo(originalBoxDefinition));

        var relocatedLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", [relocatedProviderInput]);
        var relocatedReturn = (BoundReturn)relocatedLoaded.Functions
            .Single().Body.Statements.Single();
        var relocatedMain = relocatedLoaded.Functions.Single() with {
            Name = "Main",
            ReturnType = PrimitiveType.Int32,
            Body = new BoundBlock([
                new BoundReturn(new BoundCast(relocatedReturn.Value!,
                    PrimitiveType.Int32, default), default),
            ], default),
        };
        var relocatedExecutable = relocatedLoaded with {
            Functions = [relocatedMain],
            EntryPoint = 0,
        };
        Assert.That(NativeJit.Execute(relocatedExecutable,
            NativeRuntimePolicy.FailFast), Is.EqualTo(2));
        var relocatedReemitted = TrRmExecutableCompiler.EmitStandard(
            relocatedLoaded, "ClassLayoutConsumer", schemaIdentity, 1);
        var relocatedReemittedTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    relocatedReemitted, SchemaFor(schemaIdentity)));
        Assert.That(relocatedReemittedTables
            .TargetLayoutDependencies.Count, Is.EqualTo(3));
        var relocatedReloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                relocatedReemitted), "ClassLayoutConsumer",
            [relocatedProviderInput]);
        Assert.That(NativeEmitter.EmitLibrary(relocatedReloaded, ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);

        var coLocatedArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassLayoutProvider",
            schemaIdentity, 1);
        var coLocatedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                coLocatedArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        var coLocatedRootType = coLocatedTables.TypeSpecs.GetTypeSpecRef(
            coLocatedTables.TargetLayoutDependencies
                .GetSubjectTypeSpecRef(1));
        Assert.That(coLocatedRootType.Form, Is.EqualTo((byte)0x02));
        Assert.That(coLocatedTables.TargetLayoutDependencies.Count,
            Is.EqualTo(3));
        Assert.That(Enumerable.Range(1,
                coLocatedTables.TargetLayoutDependencyFields.Count)
            .All(row => coLocatedTables
                .TargetLayoutDependencyFields.GetFieldReference(row) >> 24
                == 0x14), Is.True);
        var coLocated = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                coLocatedArtifact), "ClassLayoutProvider", []);
        Assert.That(NativeEmitter.EmitLibrary(coLocated, ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
        var coLocatedReemittedArtifact
            = TrRmExecutableCompiler.EmitStandard(coLocated,
                "ClassLayoutProvider", schemaIdentity, 1);
        var coLocatedReemitted = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                coLocatedReemittedArtifact), "ClassLayoutProvider", []);
        Assert.That(NativeEmitter.EmitLibrary(coLocatedReemitted, ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);

        var unsupportedProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(providerProgram,
                "ClassLayoutProvider", schemaIdentity, 1, [], [],
                [classExport], []);
        unsupportedProviderArtifact = AddExactClassLayoutVariant(
            unsupportedProviderArtifact, SchemaFor(schemaIdentity), 2, 1);
        var unsupportedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, unsupportedProviderArtifact);
        var unsupportedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                unsupportedProviderInput, "ClassLayoutProvider", [],
                ["type.box"], ["field.box.value", "field.box.prefix"]);
        var unsupportedQueryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Value))
                        + unsafe(offsetof(Box, Prefix))
                        + unsafe(offsetof(Box, Value));
                }
                """), unsupportedReference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(unsupportedQueryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                unsupportedQueryCompilation.Diagnostics));
        var unsupportedConsumer = TrRmExecutableCompiler.EmitStandard(
            unsupportedQueryCompilation.Program!, "ClassLayoutConsumer",
            schemaIdentity, 1);
        var unsupportedLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                unsupportedConsumer), "ClassLayoutConsumer",
            [unsupportedProviderInput]);
        Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitLibrary(unsupportedLoaded, ["Offset"],
                NativeRuntimePolicy.FailFast));
        Assert.Throws<NotSupportedException>(() =>
            NativeModuleCompiler.EmitStandardLibrary(
                new TrRmStandardArtifactInput(schemaIdentity,
                    unsupportedConsumer), "ClassLayoutConsumer",
                [unsupportedProviderInput], ["Offset"],
                NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void RefusesEN1ClassLayoutWithAnUnrepresentedTrailingHandle()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var schemaIdentity
            = s_initializerCopyEvidenceSchemaIdentities[2];
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Leaf {
                    public int32 Id;
                    public Leaf(int32 id) { Id = id; }
                }
                public class Box {
                    public int32 Value;
                    public Leaf Tail;
                    public Box(int32 value, Leaf tail) {
                        Value = value;
                    }
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        TrRmSourceClassExport CreateClassExport(string name,
            string typeKey, string constructorKey,
            params string[] fieldKeys)
        {
            var declaration = providerTree.Root.Children
                .OfType<SyntaxNode>()
                .Single(node => node.Kind == SyntaxKind.ClassDeclaration
                    && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                        token.Token.Kind == TokenKind.Identifier
                        && token.Token.Name == name));
            var fields = declaration.Children.OfType<SyntaxNode>()
                .Where(static node =>
                    node.Kind == SyntaxKind.FieldDeclaration).ToArray();
            if (fields.Length != fieldKeys.Length)
            {
                throw new InvalidDataException(
                    "The class layout fixture has an incomplete FieldDef key list.");
            }

            var classIndex = providerProgram
                .SourceClassProgramAssociation!.Declarations.Single(
                    item => item.Span == declaration.Span).ClassIndex;
            var memberExports = fields.Select((field, index) =>
                    new TrRmSourceClassMemberExport(0, field.Span,
                        fieldKeys[index]))
                .ToImmutableArray()
                .Add(CreateSourceClassConstructorExport(declaration,
                    constructorKey));
            return new TrRmSourceClassExport(providerProgram, classIndex,
                declaration.Span, typeKey, memberExports);
        }

        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ClassLayoutProvider", schemaIdentity, 1, [],
            [], [
                CreateClassExport("Leaf", "type.leaf",
                    "constructor.leaf", "field.leaf.id"),
                CreateClassExport("Box", "type.box", "constructor.box",
                    "field.box.value",
                    "field.box.tail"),
            ], []);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "ClassLayoutProvider", [], [
                "type.box",
                "type.leaf",
            ], ["field.box.value"]);
        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                queryCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassLayoutConsumer",
            schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                consumerArtifact), "ClassLayoutConsumer", [providerInput]);

        Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitLibrary(loaded, ["Offset"],
                NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void LowersEN1ClassQueryThroughInlineStructLayout()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        var layoutOptions = new CompilationOptions {
            LayoutCoreRegistration
                = LayoutCoreRegistrationTestData.Create(),
        };
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Payload {
                    public uint8 Prefix;
                    public ptr<uint8> Value;
                }
                public class Box {
                    public uint8 Prefix;
                    public Payload Data;
                    public Box(uint8 prefix) {
                        Prefix = prefix;
                    }
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree,
            layoutOptions);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var classDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var classFields = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.FieldDeclaration)
            .ToArray();
        var classIndex = providerProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(providerProgram,
            classIndex, classDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0,
                    classFields[0].Span, "field.box.prefix"),
                new TrRmSourceClassMemberExport(0,
                    classFields[1].Span, "field.box.data"),
                CreateSourceClassConstructorExport(classDeclaration,
                    "constructor.box"),
            ]);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ClassLayoutProvider", schemaIdentity, 1, [
                new TrRmSourceTypeExport("Demo", "Payload",
                    "type.payload") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Prefix",
                            "field.payload.prefix"),
                        new TrRmSourceFieldExport("Value",
                            "field.payload.value"),
                    ],
                },
            ], [], [classExport], []);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "ClassLayoutProvider", [], [
                "type.box",
                "type.payload",
            ], [
                "field.box.prefix",
                "field.box.data",
                "field.payload.prefix",
                "field.payload.value",
            ]);
        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Data.Value));
                }
                """), reference, layoutOptions);
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, queryCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassLayoutConsumer",
            schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(tables.TargetLayoutDependencies.Count, Is.EqualTo(1));
        Assert.That(tables.TargetLayoutDependencyFields.Count,
            Is.EqualTo(2));
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", [providerInput]);
        var returned = (BoundReturn)loaded.Functions.Single().Body
            .Statements.Single();
        var unsafeExpression = (BoundUnsafeExpression)returned.Value!;
        var query = (BoundLayoutQuery)unsafeExpression.Expression;
        Assert.That(query.Subject.FieldPath!.Steps, Has.Length.EqualTo(2));
        Assert.That(query.Subject.FieldPath.Steps[0]
            .StandardClassFieldOrigin!.SelectedExportKeys,
            Is.EqualTo(["field.box.data"]));
        var pointerType = query.Subject.FieldPath.Steps[1].FieldType;
        Assert.That(NativePointerTypes.IsType(pointerType), Is.True);
        Assert.That(loaded.NativePointerTypes[
            NativePointerTypes.Index(pointerType)],
            Is.EqualTo(new NativePointerTypeDefinition(
                PrimitiveType.UInt8, false)));
        var main = loaded.Functions.Single() with {
            Name = "Main",
            ReturnType = PrimitiveType.Int32,
            Body = new BoundBlock([
                new BoundReturn(new BoundCast(unsafeExpression,
                    PrimitiveType.Int32, default), default),
            ], default),
        };
        var executable = loaded with {
            Functions = [main],
            EntryPoint = 0,
        };

        Assert.That(NativeJit.Execute(executable,
            NativeRuntimePolicy.FailFast), Is.EqualTo(16));

        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "ClassLayoutConsumer", schemaIdentity, 1);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                reemittedArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(reemittedTables.TargetLayoutDependencies.Count,
            Is.EqualTo(1));
        Assert.That(reemittedTables.TargetLayoutDependencyFields.Count,
            Is.EqualTo(2));
        Assert.That(reemittedTables.ConstantExecutions.Count, Is.Zero);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "ClassLayoutConsumer", [providerInput]);
        BoundLayoutQuery? reloadedQuery = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            reloaded.Functions.Single().Body,
            expression => reloadedQuery ??=
                expression as BoundLayoutQuery), Is.True);
        Assert.That(reloadedQuery!.Subject.FieldPath!.Steps,
            Has.Length.EqualTo(2));
        var reloadedPointerType
            = reloadedQuery.Subject.FieldPath.Steps[1].FieldType;
        Assert.That(NativePointerTypes.IsType(reloadedPointerType), Is.True);
        Assert.That(reloaded.NativePointerTypes[
            NativePointerTypes.Index(reloadedPointerType)],
            Is.EqualTo(new NativePointerTypeDefinition(
                PrimitiveType.UInt8, false)));
        Assert.That(reloadedQuery.Subject.FieldPath.Steps[1]
            .StandardFieldOrigin!.SelectedExportKeys,
            Is.EqualTo(["field.payload.value"]));
    }

    [Test]
    public void ComposesClassLayoutReferencesWithClassFields()
    {
        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        var tree = SyntaxTree.Parse("""
            namespace Demo {
                public class Parent {
                    public Child Value;
                }
                public class Other {
                    public int32 Id;
                }
                public class Child {
                    public int32 Id;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;

        TrRmSourceClassExport CreateClassExport(BoundProgram sourceProgram,
            SyntaxTree sourceTree, string name, string typeExportKey,
            string fieldExportKey)
        {
            var declaration = sourceTree.Root.Children.OfType<SyntaxNode>()
                .Single(node =>
                    node.Kind == SyntaxKind.ClassDeclaration
                    && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                        token.Token.Kind == TokenKind.Identifier
                        && token.Token.Name == name));
            var field = declaration.Children.OfType<SyntaxNode>()
                .First(static node =>
                    node.Kind == SyntaxKind.FieldDeclaration);
            var classIndex = sourceProgram.SourceClassProgramAssociation!
                .Declarations.Single(item =>
                    item.Span == declaration.Span).ClassIndex;
            return new TrRmSourceClassExport(sourceProgram, classIndex,
                declaration.Span, typeExportKey, [
                    new TrRmSourceClassMemberExport(0, field.Span,
                        fieldExportKey),
                ]);
        }

        var childExport = CreateClassExport(program, tree, "Child",
            "type.child", "field.child.id");
        var parentExport = CreateClassExport(program, tree, "Parent",
            "type.parent", "field.parent.value");
        var otherExport = CreateClassExport(program, tree, "Other",
            "type.other", "field.other.id");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ClassHandleProvider", schemaIdentity, targetRequirement, [],
            [], [childExport, parentExport, otherExport], []);
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var parentReference
            = TrRmExecutableCompiler.CreateStandardReference(input,
                "ClassHandleProvider", [], ["type.parent"],
                ["field.parent.value"]);
        var childReference
            = TrRmExecutableCompiler.CreateStandardReference(input,
                "ClassHandleProvider", [], ["type.child"],
                ["field.child.id"]);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [parentReference, childReference]);

        var parent = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Parent");
        var child = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Child");
        var childType = parent.Declaration.Fields.Single().Type;
        Assert.That(BoundGenericNominalTypeTypes.IsType(childType), Is.True);
        Assert.That(reference.GenericNominalTypeInstances[
                BoundGenericNominalTypeTypes.Index(childType)].Definition,
            Is.EqualTo(child.Declaration.Identity));

        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint CombinedOffset() {
                    return unsafe(offsetof(Parent, Value))
                        + unsafe(offsetof(Child, Id));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumer.Program!, "ClassHandleConsumer", schemaIdentity,
            targetRequirement);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(tables.TargetLayoutDependencies.Count,
            Is.EqualTo(2));
        Assert.That(tables.TargetLayoutDependencyFields.Count,
            Is.EqualTo(2));
        var imports = Enumerable.Range(1, tables.Imports.Count)
            .Select(tables.Imports.GetBindingForImportRow).ToArray();
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "ClassHandleProvider"
                && binding.ExportKey == "type.child"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "ClassHandleProvider"
                && binding.ExportKey == "field.parent.value"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "ClassHandleProvider"
                && binding.ExportKey == "field.child.id"),
            Is.True);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassHandleConsumer", [input]);
        Assert.That(loaded.Functions.Single().Name,
            Is.EqualTo("CombinedOffset"));

        const string packedProviderIdentity
            = "PrivateClassHandleProvider";
        const string packedDependencyIdentity
            = "PrivateClassHandleDependency";
        const string packedLeafIdentity = "PrivateClassHandleLeaf";
        var packedTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Parent {
                    public Child Value;
                }
                public class Other {
                    public int32 Id;
                }
                public class Child {
                    public int32 Id;
                    public Leaf Link;
                }
                public class Leaf {
                    public int32 Data;
                }
            }
            """);
        var packedCompilation = Compilation.CreateLibrary(packedTree);
        Assert.That(packedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, packedCompilation.Diagnostics));
        var packedProgram = (BoundProgram)packedCompilation.Program!;
        var packedParentExport = CreateClassExport(packedProgram, packedTree,
            "Parent", "type.parent", "field.parent.value");
        var packedOtherExport = CreateClassExport(packedProgram, packedTree,
            "Other", "type.other", "field.other.id");
        var packedSingleModuleArtifact
            = TrRmExecutableCompiler.EmitStandard(packedProgram,
                packedProviderIdentity, schemaIdentity, targetRequirement,
                [], [], [packedParentExport, packedOtherExport], []);
        var packedProviderArtifact = AssignTrailingClassTypeDefsToModules(
            packedSingleModuleArtifact, schemaIdentity, [
                (packedDependencyIdentity, "Child"),
                (packedLeafIdentity, "Leaf"),
            ]);
        var packedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, packedProviderArtifact);
        var packedParentReference
            = TrRmExecutableCompiler.CreateStandardReference(
                packedProviderInput, packedProviderIdentity, [],
                ["type.parent"], ["field.parent.value"]);
        var packedOtherReference
            = TrRmExecutableCompiler.CreateStandardReference(
                packedProviderInput, packedProviderIdentity, [],
                ["type.other"], ["field.other.id"]);
        var packedReference
            = TrRmExecutableCompiler.CreateStandardReferenceSet([
                packedParentReference, packedOtherReference,
            ]);
        var packedChild
            = packedReference.StandardClassDeclarations.Single(
                static declaration =>
                    declaration.Declaration.Name == "Child");
        Assert.That(packedChild.ExportKeys, Is.Empty);
        Assert.That(packedChild.SelectedExportKeys, Is.Empty);
        Assert.That(packedChild.Expectations, Is.Empty);

        var packedQuery = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint CombinedOffset() {
                    return unsafe(offsetof(Parent, Value))
                        + unsafe(offsetof(Other, Id));
                }
                """), packedReference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(packedQuery.HasErrors, Is.False,
            string.Join(Environment.NewLine, packedQuery.Diagnostics));
        var packedArtifact = TrRmExecutableCompiler.EmitStandard(
            packedQuery.Program!, "PrivateClassHandleConsumer",
            schemaIdentity, targetRequirement);
        var packedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                packedArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(packedTables.Modules.Count, Is.EqualTo(4));
        var packedParentTypeDefRow = Enumerable.Range(
                1, packedTables.TypeDefs.Count)
            .Single(row => packedTables.TypeDefs.GetName(row) == "Parent");
        var packedChildTypeDefRow = Enumerable.Range(
                1, packedTables.TypeDefs.Count)
            .Single(row => packedTables.TypeDefs.GetName(row) == "Child");
        var packedLeafTypeDefRow = Enumerable.Range(
                1, packedTables.TypeDefs.Count)
            .Single(row => packedTables.TypeDefs.GetName(row) == "Leaf");
        Assert.That(packedTables.Modules.GetIdentity(
                packedTables.TypeDefs.GetOwnerModule(packedParentTypeDefRow)),
            Is.EqualTo(packedProviderIdentity));
        Assert.That(packedTables.Modules.GetIdentity(
                packedTables.TypeDefs.GetOwnerModule(packedChildTypeDefRow)),
            Is.EqualTo(packedDependencyIdentity));
        Assert.That(packedTables.Modules.GetIdentity(
                packedTables.TypeDefs.GetOwnerModule(packedLeafTypeDefRow)),
            Is.EqualTo(packedLeafIdentity));
        var packedParentFieldRange
            = packedTables.TypeDefs.GetFieldRange(packedParentTypeDefRow);
        var packedParentFieldTypeSpec
            = packedTables.TypeSpecs.GetTypeSpecRef(
                packedTables.Fields.GetTypeSpecRef(
                    packedParentFieldRange.Start));
        Assert.That(packedParentFieldTypeSpec.Form,
            Is.EqualTo((byte)0x02));
        Assert.That(packedParentFieldTypeSpec.Row,
            Is.EqualTo(packedChildTypeDefRow));
        var packedChildFieldRange
            = packedTables.TypeDefs.GetFieldRange(packedChildTypeDefRow);
        var packedChildFieldTypeSpec
            = packedTables.TypeSpecs.GetTypeSpecRef(
                packedTables.Fields.GetTypeSpecRef(
                    packedChildFieldRange.Start + 1));
        Assert.That(packedChildFieldTypeSpec.Form,
            Is.EqualTo((byte)0x02));
        Assert.That(packedChildFieldTypeSpec.Row,
            Is.EqualTo(packedLeafTypeDefRow));
        Assert.That(Enumerable.Range(1, packedTables.Imports.Count)
            .Select(packedTables.Imports.GetBindingForImportRow)
            .Any(static binding =>
                binding.ExportKey is "type.child" or "type.leaf"),
            Is.False);
        var packedLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, packedArtifact),
            "PrivateClassHandleConsumer", []);
        var packedLoadedChild
            = packedLoaded.StandardClassDeclarations.Single(
                static declaration =>
                    declaration.Declaration.Name == "Child");
        Assert.That(packedLoadedChild.Declaration.Provider.ModuleIdentity,
            Is.EqualTo(packedDependencyIdentity));
        var packedLoadedLeaf
            = packedLoaded.StandardClassDeclarations.Single(
                static declaration =>
                    declaration.Declaration.Name == "Leaf");
        Assert.That(packedLoadedLeaf.Declaration.Provider.ModuleIdentity,
            Is.EqualTo(packedLeafIdentity));
        var packedReemitted = TrRmExecutableCompiler.EmitStandard(
            packedLoaded, "PrivateClassHandleConsumer", schemaIdentity,
            targetRequirement);
        var packedReemittedLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                packedReemitted), "PrivateClassHandleConsumer", []);
        Assert.That(packedReemittedLoaded.StandardClassDeclarations
            .Single(static declaration =>
                declaration.Declaration.Name == "Leaf")
            .Declaration.Provider.ModuleIdentity, Is.EqualTo(packedLeafIdentity));

        var replacementClassExport = CreateClassExport(packedProgram,
            packedTree, "Child", "type.child", "field.child.id");
        var replacementArtifact = TrRmExecutableCompiler.EmitStandard(
            packedProgram, packedDependencyIdentity, schemaIdentity,
            targetRequirement, [], [], [replacementClassExport], []);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    packedArtifact), "PrivateClassHandleConsumer", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        replacementArtifact),
                ]));

        var incompatibleOwnerArtifact
            = AssignTrailingClassTypeDefsToModules(
                packedSingleModuleArtifact,
                schemaIdentity, [
                    (packedDependencyIdentity, "Child"),
                    (packedLeafIdentity, "Leaf"),
                ], dependencyTargetRequirement: 0);
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleOwnerArtifact), packedProviderIdentity, [],
                ["type.parent"], ["field.parent.value"]));
    }

    [Test]
    public void PacksUnexportedClassHandlesNestedInInlineStructs()
    {
        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Child {
                    public int32 Id;
                }
                public class Alpha {
                    public Payload Data;
                }
                public struct Payload {
                    public int32 Header;
                    public Child Handle;
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var alphaDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(node =>
                node.Kind == SyntaxKind.ClassDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                    token.Token.Kind == TokenKind.Identifier
                    && token.Token.Name == "Alpha"));
        var alphaField = alphaDeclaration.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var alphaClassIndex = providerProgram
            .SourceClassProgramAssociation!.Declarations.Single(
                declaration => declaration.Span == alphaDeclaration.Span)
            .ClassIndex;
        var alphaExport = new TrRmSourceClassExport(providerProgram,
            alphaClassIndex, alphaDeclaration.Span, "type.alpha", [
                new TrRmSourceClassMemberExport(0, alphaField.Span,
                    "field.alpha.data"),
            ]);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ChildProvider", schemaIdentity,
            targetRequirement, [
                new TrRmSourceTypeExport("Demo", "Payload",
                    "type.payload") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Header",
                            "field.payload.header"),
                    ],
                },
            ], [], [alphaExport], []);
        var packedProviderArtifact = AssignTrailingClassTypeDefsToModules(
            providerArtifact, schemaIdentity, [
                ("AlphaProvider", "Alpha"),
                ("PayloadProvider", "Payload"),
            ]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            packedProviderArtifact);
        var alphaReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "AlphaProvider", [], ["type.alpha"],
            ["field.alpha.data"]);
        var reference = alphaReference;
        var child = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Child");
        Assert.That(child.Declaration.Provider.ModuleIdentity,
            Is.EqualTo("ChildProvider"));
        Assert.That(child.ExportKeys, Is.Empty);
        Assert.That(child.SelectedExportKeys, Is.Empty);
        Assert.That(child.Expectations, Is.Empty);

        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint HeaderOffset() {
                    return unsafe(offsetof(Alpha, Data.Header));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                queryCompilation.Diagnostics));
        const string consumerModuleIdentity
            = "PackedInlineStructConsumer";
        var artifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, consumerModuleIdentity,
            schemaIdentity, targetRequirement);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                artifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(tables.Modules.Count, Is.EqualTo(4));
        var alphaTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Alpha");
        var childTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Child");
        var payloadTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Payload");
        Assert.That(tables.Modules.GetIdentity(
                tables.TypeDefs.GetOwnerModule(alphaTypeDefRow)),
            Is.EqualTo("AlphaProvider"));
        Assert.That(tables.Modules.GetIdentity(
                tables.TypeDefs.GetOwnerModule(childTypeDefRow)),
            Is.EqualTo("ChildProvider"));
        Assert.That(tables.Modules.GetIdentity(
                tables.TypeDefs.GetOwnerModule(payloadTypeDefRow)),
            Is.EqualTo("PayloadProvider"));

        var alphaFieldRange = tables.TypeDefs.GetFieldRange(alphaTypeDefRow);
        var alphaDataFieldRow = Enumerable.Range(alphaFieldRange.Start,
                alphaFieldRange.End - alphaFieldRange.Start)
            .Single(row => tables.Fields.GetName(row) == "Data");
        var alphaDataTypeSpec = tables.TypeSpecs.GetTypeSpecRef(
            tables.Fields.GetTypeSpecRef(alphaDataFieldRow));
        Assert.That(alphaDataTypeSpec.Form, Is.EqualTo((byte)0x02));
        Assert.That(alphaDataTypeSpec.Row, Is.EqualTo(payloadTypeDefRow));
        var payloadFieldRange
            = tables.TypeDefs.GetFieldRange(payloadTypeDefRow);
        var payloadHandleFieldRow = Enumerable.Range(
                payloadFieldRange.Start,
                payloadFieldRange.End - payloadFieldRange.Start)
            .Single(row => tables.Fields.GetName(row) == "Handle");
        var payloadHandleTypeSpec = tables.TypeSpecs.GetTypeSpecRef(
            tables.Fields.GetTypeSpecRef(payloadHandleFieldRow));
        Assert.That(payloadHandleTypeSpec.Form, Is.EqualTo((byte)0x02));
        Assert.That(payloadHandleTypeSpec.Row, Is.EqualTo(childTypeDefRow));
        Assert.That(Enumerable.Range(1, tables.Imports.Count)
            .Select(tables.Imports.GetBindingForImportRow)
            .Any(static binding => binding.ExportKey == "type.child"),
            Is.False);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            consumerModuleIdentity, []);
        Assert.That(loaded.StandardClassDeclarations.Single(
                static declaration =>
                    declaration.Declaration.Name == "Child")
            .Declaration.Provider.ModuleIdentity, Is.EqualTo("ChildProvider"));
        Assert.That(loaded.Structures.Single(
                static structure => structure.Name == "Demo.Payload")
            .StandardTypeOrigin!.Provider.ModuleIdentity,
            Is.EqualTo("PayloadProvider"));
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "PackedInlineStructConsumerRemapped", schemaIdentity,
            targetRequirement);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "PackedInlineStructConsumerRemapped", []);
        Assert.That(reloaded.StandardClassDeclarations.Single(
                static declaration =>
                    declaration.Declaration.Name == "Child")
            .Declaration.Provider.ModuleIdentity, Is.EqualTo("ChildProvider"));
        Assert.That(reloaded.Structures.Single(
                static structure => structure.Name == "Demo.Payload")
            .StandardTypeOrigin!.Provider.ModuleIdentity,
            Is.EqualTo("PayloadProvider"));
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                reemitted, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        var reemittedPayloadRow = Enumerable.Range(
                1, reemittedTables.TypeDefs.Count)
            .Single(row =>
                reemittedTables.TypeDefs.GetName(row) == "Payload");
        Assert.That(reemittedTables.Modules.GetIdentity(
                reemittedTables.TypeDefs.GetOwnerModule(
                    reemittedPayloadRow)),
            Is.EqualTo("PayloadProvider"));

        var replacementTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Payload {
                    public int64 Header;
                }
            }
            """);
        var replacementCompilation = Compilation.CreateLibrary(
            replacementTree);
        Assert.That(replacementCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                replacementCompilation.Diagnostics));
        var replacementProgram = (BoundProgram)replacementCompilation.Program!;
        var replacementArtifact = TrRmExecutableCompiler.EmitStandard(
            replacementProgram, "PayloadProvider", schemaIdentity,
            targetRequirement, [
                new TrRmSourceTypeExport("Demo", "Payload",
                    "type.payload") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Header",
                            "field.payload.header"),
                    ],
                },
            ], [], [], []);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                consumerModuleIdentity, [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        replacementArtifact),
                ]));
    }

    [Test]
    public void PacksImportedClassExpectationsWithPrivateClassDependencies()
    {
        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        const string childModuleIdentity = "PackedChildProvider";
        const string parentModuleIdentity = "PackedParentProvider";
        const string consumerModuleIdentity = "PackedImportConsumer";

        static TrRmSourceClassExport CreateClassExport(
            BoundProgram program, SyntaxTree tree, string className,
            string typeExportKey, string fieldExportKey)
        {
            var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
                .Single(node =>
                    node.Kind == SyntaxKind.ClassDeclaration
                    && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                        token.Token.Kind == TokenKind.Identifier
                        && token.Token.Name == className));
            var fieldDeclaration = classDeclaration.Children
                .OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.FieldDeclaration);
            var classIndex = program.SourceClassProgramAssociation!
                .Declarations.Single(item =>
                    item.Span == classDeclaration.Span).ClassIndex;
            return new TrRmSourceClassExport(program, classIndex,
                classDeclaration.Span, typeExportKey, [
                    new TrRmSourceClassMemberExport(0,
                        fieldDeclaration.Span, fieldExportKey),
                ]);
        }

        var childTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Child {
                    public int32 Id;
                }
            }
            """);
        var childCompilation = Compilation.CreateLibrary(childTree);
        Assert.That(childCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, childCompilation.Diagnostics));
        var childProgram = (BoundProgram)childCompilation.Program!;
        var childExport = CreateClassExport(childProgram, childTree,
            "Child", "type.child", "field.child.id");
        var childArtifactWithoutAlias = TrRmExecutableCompiler.EmitStandard(
            childProgram, childModuleIdentity, schemaIdentity,
            targetRequirement, [], [], [childExport], []);
        var childArtifact = AddExportAlias(childArtifactWithoutAlias,
            schemaIdentity, "field.child.id", "field.child.id.alias");
        var childInput = new TrRmStandardArtifactInput(schemaIdentity,
            childArtifact);

        var parentTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Parent {
                    public Child Value;
                    public Node Hidden;
                }
                public class Child {
                    public int32 Id;
                }
                public class Node {
                    public int32 Id;
                }
            }
            """);
        var parentCompilation = Compilation.CreateLibrary(parentTree);
        Assert.That(parentCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                parentCompilation.Diagnostics));
        var parentProgram = (BoundProgram)parentCompilation.Program!;
        var parentExport = CreateClassExport(parentProgram, parentTree,
            "Parent", "type.parent", "field.parent.value");
        var parentArtifact = TrRmExecutableCompiler.EmitStandard(
            parentProgram, parentModuleIdentity, schemaIdentity,
            targetRequirement, [], [], [parentExport], []);
        var parentTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                parentArtifact, SchemaFor(schemaIdentity)));
        var parentTypeDefRow = Enumerable.Range(
                1, parentTables.TypeDefs.Count)
            .Single(row =>
                parentTables.TypeDefs.GetName(row) == "Parent");
        var expectedChildTypeDefRow = Enumerable.Range(
                1, parentTables.TypeDefs.Count)
            .Single(row =>
                parentTables.TypeDefs.GetName(row) == "Child");
        var parentFieldRange
            = parentTables.TypeDefs.GetFieldRange(parentTypeDefRow);
        var parentValueFieldRow = Enumerable.Range(parentFieldRange.Start,
                parentFieldRange.End - parentFieldRange.Start)
            .Single(row => parentTables.Fields.GetName(row) == "Value");
        var childFieldRange = parentTables.TypeDefs.GetFieldRange(
            expectedChildTypeDefRow);
        var expectedChildFieldRow = childFieldRange.Start;
        parentArtifact = AddTypeDefImport(parentArtifact, schemaIdentity,
            childModuleIdentity, "type.child",
            0x1300_0000u | checked((uint)expectedChildTypeDefRow),
            "field.child.id",
            0x1400_0000u | checked((uint)expectedChildFieldRow),
            "field.child.id.alias");
        parentArtifact = RewriteFieldTypeSpecAsImport(parentArtifact,
            schemaIdentity, parentValueFieldRow, 1);
        var parentInput = new TrRmStandardArtifactInput(schemaIdentity,
            parentArtifact);
        var parentReference = TrRmExecutableCompiler.CreateStandardReference(
            parentInput, parentModuleIdentity, [childInput], ["type.parent"],
            ["field.parent.value", "field.child.id",
                "field.child.id.alias"]);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [parentReference]);
        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint ParentValueOffset() {
                    return unsafe(offsetof(Parent, Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                queryCompilation.Diagnostics));

        var packedArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, consumerModuleIdentity,
            schemaIdentity, targetRequirement);
        var packedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                packedArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        var parentTypeDefRow = Enumerable.Range(
                1, packedTables.TypeDefs.Count)
            .Single(row =>
                packedTables.TypeDefs.GetName(row) == "Parent");
        var childTypeDefRow = Enumerable.Range(
                1, packedTables.TypeDefs.Count)
            .Single(row =>
                packedTables.TypeDefs.GetName(row) == "Child"
                && packedTables.Modules.GetIdentity(
                    packedTables.TypeDefs.GetOwnerModule(row))
                    == childModuleIdentity);
        Assert.That(packedTables.Modules.Count, Is.EqualTo(3));
        var parentFieldRange
            = packedTables.TypeDefs.GetFieldRange(parentTypeDefRow);
        var parentFieldTypeSpec = packedTables.TypeSpecs.GetTypeSpecRef(
            packedTables.Fields.GetTypeSpecRef(parentFieldRange.Start));
        Assert.That(parentFieldTypeSpec.Form, Is.EqualTo((byte)0x02));
        Assert.That(parentFieldTypeSpec.Row, Is.EqualTo(childTypeDefRow));

        var childTypeImportRow = Enumerable.Range(1,
                packedTables.Imports.Count)
            .Single(row =>
                packedTables.Imports.GetBindingForImportRow(row)
                    .ExportKey == "type.child");
        var expectedChildTypeDefId = packedTables.Imports
            .GetExpectedDefinitionForImportRow(childTypeImportRow);
        var expectedChildTypeDefRow = checked((int)(
            expectedChildTypeDefId & 0x00FF_FFFF));
        Assert.That(packedTables.Modules.GetIdentity(
                packedTables.TypeDefs.GetOwnerModule(
                    expectedChildTypeDefRow)),
            Is.EqualTo(parentModuleIdentity));
        var childModuleRefRow = packedTables.Imports
            .GetModuleRefForImportRow(childTypeImportRow);
        Assert.That(packedTables.Modules.GetIdentity(
                packedTables.ModuleRefs.GetOwnerModule(childModuleRefRow)),
            Is.EqualTo(parentModuleIdentity));
        Assert.That(packedTables.ModuleRefs.GetProducerIdentity(
                childModuleRefRow), Is.EqualTo(childModuleIdentity));
        var childFieldImportRow = Enumerable.Range(1,
                packedTables.Imports.Count)
            .Single(row =>
                packedTables.Imports.GetBindingForImportRow(row)
                    .ExportKey == "field.child.id");
        var childFieldModuleRefRow = packedTables.Imports
            .GetModuleRefForImportRow(childFieldImportRow);
        Assert.That(packedTables.Modules.GetIdentity(
                packedTables.ModuleRefs.GetOwnerModule(
                    childFieldModuleRefRow)),
            Is.EqualTo(parentModuleIdentity));
        Assert.That(packedTables.ModuleRefs.GetProducerIdentity(
                childFieldModuleRefRow), Is.EqualTo(childModuleIdentity));
        Assert.That(Enumerable.Range(1, packedTables.Imports.Count)
            .Select(packedTables.Imports.GetBindingForImportRow)
            .Where(static binding =>
                binding.ExportKey.StartsWith("field.child.id",
                    StringComparison.Ordinal))
            .Select(static binding => binding.ExportKey),
            Is.EquivalentTo([
                "field.child.id", "field.child.id.alias",
            ]));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, packedArtifact),
            consumerModuleIdentity, []);
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            consumerModuleIdentity, schemaIdentity, targetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                reemittedArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        var reemittedChildImportRow = Enumerable.Range(
                1, reemittedTables.Imports.Count)
            .Single(row =>
                reemittedTables.Imports.GetBindingForImportRow(row)
                    .ExportKey == "type.child");
        var reemittedChildModuleRefRow = reemittedTables.Imports
            .GetModuleRefForImportRow(reemittedChildImportRow);
        Assert.That(reemittedTables.Modules.GetIdentity(
                reemittedTables.ModuleRefs.GetOwnerModule(
                    reemittedChildModuleRefRow)),
            Is.EqualTo(parentModuleIdentity));
        Assert.That(Enumerable.Range(1, reemittedTables.Imports.Count)
            .Select(reemittedTables.Imports.GetBindingForImportRow)
            .Where(static binding =>
                binding.ExportKey.StartsWith("field.child.id",
                    StringComparison.Ordinal))
            .Select(static binding => binding.ExportKey),
            Is.EquivalentTo([
                "field.child.id", "field.child.id.alias",
            ]));
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), consumerModuleIdentity, []);
        Assert.That(reloaded.StandardClassDeclarations.Any(declaration =>
                declaration.Declaration.Name == "Child"
                && declaration.Declaration.Provider.ModuleIdentity
                    == childModuleIdentity),
            Is.True);
    }

    [Test]
    public void ComposesCrossProviderClassHandleDependencies()
    {
        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;

        (TrRmStandardArtifactInput Input,
            CompilationReference Reference) CreateClassProvider(
                string moduleIdentity, string className,
                string typeExportKey, string fieldExportKey,
                string fieldType)
        {
            var tree = SyntaxTree.Parse($$"""
                namespace Demo {
                    public class {{className}} {
                        public {{fieldType}} Id;
                    }
                }
                """);
            var compilation = Compilation.CreateLibrary(tree);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = (BoundProgram)compilation.Program!;
            var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.ClassDeclaration);
            var fieldDeclaration = classDeclaration.Children
                .OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.FieldDeclaration);
            var classIndex = program.SourceClassProgramAssociation!
                .Declarations.Single(declaration =>
                    declaration.Span == classDeclaration.Span).ClassIndex;
            var classExport = new TrRmSourceClassExport(program, classIndex,
                classDeclaration.Span, typeExportKey, [
                    new TrRmSourceClassMemberExport(0,
                        fieldDeclaration.Span, fieldExportKey),
                ]);
            var artifact = TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, targetRequirement, [], [],
                [classExport], []);
            var input = new TrRmStandardArtifactInput(schemaIdentity,
                artifact);
            return (input,
                TrRmExecutableCompiler.CreateStandardReference(input,
                    moduleIdentity, [], [typeExportKey], [
                        fieldExportKey,
                    ]));
        }

        var gamma = CreateClassProvider("GammaProvider", "Gamma",
            "type.gamma", "field.gamma.id", "int32");
        var betaTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Beta {
                    public Gamma Next;
                }
            }
            """);
        var betaCompilation = Compilation.CreateLibrary(betaTree,
            gamma.Reference);
        Assert.That(betaCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, betaCompilation.Diagnostics));
        var betaProgram = (BoundProgram)betaCompilation.Program!;
        var betaDeclaration = betaTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var betaField = betaDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var betaClassIndex = betaProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == betaDeclaration.Span).ClassIndex;
        var betaExport = new TrRmSourceClassExport(betaProgram,
            betaClassIndex, betaDeclaration.Span, "type.beta", [
                new TrRmSourceClassMemberExport(0, betaField.Span,
                    "field.beta.next"),
            ]);
        var betaArtifact = TrRmExecutableCompiler.EmitStandard(betaProgram,
            "BetaProvider", schemaIdentity, targetRequirement, [], [],
            [betaExport], []);
        var betaInput = new TrRmStandardArtifactInput(schemaIdentity,
            betaArtifact);
        var beta = (Input: betaInput,
            Reference: TrRmExecutableCompiler.CreateStandardReference(
                betaInput, "BetaProvider", [gamma.Input], ["type.beta"], [
                    "field.beta.next",
                ]));
        var parentTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Parent {
                    public Beta Value;
                }
            }
            """);
        var parentCompilation = Compilation.CreateLibrary(parentTree,
            beta.Reference);
        Assert.That(parentCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                parentCompilation.Diagnostics));
        var parentProgram = (BoundProgram)parentCompilation.Program!;
        var parentDeclaration = parentTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var parentField = parentDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var parentClassIndex = parentProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == parentDeclaration.Span).ClassIndex;
        var parentExport = new TrRmSourceClassExport(parentProgram,
            parentClassIndex, parentDeclaration.Span, "type.parent", [
                new TrRmSourceClassMemberExport(0, parentField.Span,
                    "field.parent.value"),
            ]);
        var parentArtifact = TrRmExecutableCompiler.EmitStandard(
            parentProgram, "AlphaProvider", schemaIdentity,
            targetRequirement, [], [], [parentExport], []);
        var parentInput = new TrRmStandardArtifactInput(schemaIdentity,
            parentArtifact);
        var parentReference = TrRmExecutableCompiler.CreateStandardReference(
            parentInput, "AlphaProvider", [beta.Input, gamma.Input],
            ["type.parent"], ["field.parent.value"]);
        var parentOnlyReference
            = TrRmExecutableCompiler.CreateStandardReferenceSet([
                parentReference,
            ]);
        var parentOnlyQuery = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint ParentOffset() {
                    return unsafe(offsetof(Parent, Value));
                }
                """), parentOnlyReference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(parentOnlyQuery.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                parentOnlyQuery.Diagnostics));
        var parentOnlyArtifact = TrRmExecutableCompiler.EmitStandard(
            parentOnlyQuery.Program!, "ParentOnlyConsumer",
            schemaIdentity, targetRequirement);
        var parentOnlyTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                parentOnlyArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(Enumerable.Range(1, parentOnlyTables.Imports.Count)
            .Select(parentOnlyTables.Imports.GetBindingForImportRow)
            .Any(static binding =>
                binding.ProducerIdentity == "BetaProvider"
                && binding.ExportKey == "type.beta"),
            Is.True);
        Assert.That(Enumerable.Range(1, parentOnlyTables.Imports.Count)
            .Select(parentOnlyTables.Imports.GetBindingForImportRow)
            .Any(static binding =>
                binding.ProducerIdentity == "GammaProvider"
                && binding.ExportKey == "type.gamma"),
            Is.True);
        var parentOnlyLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                parentOnlyArtifact), "ParentOnlyConsumer",
            [parentInput, beta.Input, gamma.Input]);
        var parentOnlyReemitted = TrRmExecutableCompiler.EmitStandard(
            parentOnlyLoaded, "ParentOnlyConsumer", schemaIdentity,
            targetRequirement);
        _ = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                parentOnlyReemitted), "ParentOnlyConsumer",
            [parentInput, beta.Input, gamma.Input]);

        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [parentReference, beta.Reference]);
        var parent = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Parent");
        var importedBeta = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Beta");
        var importedGamma = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Gamma");
        var parentFieldType = parent.Declaration.Fields.Single().Type;
        Assert.That(BoundGenericNominalTypeTypes.IsType(parentFieldType),
            Is.True);
        Assert.That(reference.GenericNominalTypeInstances[
                BoundGenericNominalTypeTypes.Index(parentFieldType)]
                .Definition,
            Is.EqualTo(importedBeta.Declaration.Identity));
        Assert.That(importedBeta.Expectations, Has.Length.EqualTo(1));
        Assert.That(importedBeta.SelectedExportKeys,
            Is.EqualTo(["type.beta"]));
        Assert.That(importedGamma.Expectations, Has.Length.EqualTo(1));

        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint CombinedOffset() {
                    return unsafe(offsetof(Parent, Value))
                        + unsafe(offsetof(Beta, Next));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                queryCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassHandleConsumer",
            schemaIdentity, targetRequirement);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        var imports = Enumerable.Range(1, tables.Imports.Count)
            .Select(tables.Imports.GetBindingForImportRow).ToArray();
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "BetaProvider"
                && binding.ExportKey == "type.beta"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "AlphaProvider"
                && binding.ExportKey == "type.parent"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "AlphaProvider"
                && binding.ExportKey == "field.parent.value"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "BetaProvider"
                && binding.ExportKey == "field.beta.next"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "GammaProvider"
                && binding.ExportKey == "type.gamma"),
            Is.True);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassHandleConsumer", [parentInput, beta.Input, gamma.Input]);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "ClassHandleConsumer", schemaIdentity, targetRequirement);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "ClassHandleConsumer", [parentInput, beta.Input, gamma.Input]);
        Assert.That(reloaded.Functions.Single().Name,
            Is.EqualTo("CombinedOffset"));

        var incompatibleGamma = CreateClassProvider("GammaProvider",
            "Gamma", "type.gamma", "field.gamma.id", "int64");
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReferenceSet([
                parentReference, beta.Reference, incompatibleGamma.Reference,
            ]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ComposesClassRootsThroughImportedStructViewsAcrossProviders(
        string schemaIdentity)
    {
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;

        byte[] CreateStructProviderArtifact(string moduleIdentity,
            bool includePrefix, string innerValueType)
        {
            var prefixText = includePrefix
                ? "public struct Aardvark { public int32 Value; }"
                : string.Empty;
            var tree = SyntaxTree.Parse($$"""
                {{prefixText}}
                public struct Inner { public {{innerValueType}} Value; }
                public struct Leaf { public Inner Child; }
                """);
            var compilation = Compilation.CreateLibrary(tree);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = (BoundProgram)compilation.Program!;
            var exports = new List<TrRmSourceTypeExport>();
            if (includePrefix)
            {
                exports.Add(new TrRmSourceTypeExport(string.Empty,
                    "Aardvark", "type.aardvark") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Value",
                            "field.aardvark.value"),
                    ],
                });
            }

            exports.Add(new TrRmSourceTypeExport(string.Empty, "Inner",
                "type.inner") {
                FieldExports = [
                    new TrRmSourceFieldExport("Value",
                        "field.inner.value"),
                ],
            });
            exports.Add(new TrRmSourceTypeExport(string.Empty, "Leaf",
                "type.leaf") {
                FieldExports = [
                    new TrRmSourceFieldExport("Child",
                        "field.leaf.child"),
                ],
            });

            return TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, targetRequirement, exports,
                Array.Empty<TrRmSourceGenericMethodExport>(),
                Array.Empty<TrRmSourceClassExport>());
        }

        var providerArtifact = CreateStructProviderArtifact(
            "StructProvider", includePrefix: false, "int32");
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "StructProvider", [], ["type.leaf"],
            ["field.leaf.child"]);

        var classTree = SyntaxTree.Parse("""
            public class Box {
                public Leaf Value;
                public Box(Leaf value) { Value = value; }
            }
            """);
        var classCompilation = Compilation.CreateLibrary(classTree,
            providerReference);
        Assert.That(classCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, classCompilation.Diagnostics));
        var classProgram = (BoundProgram)classCompilation.Program!;
        var classDeclaration = classTree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var classIndex = classProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classArtifact = TrRmExecutableCompiler.EmitStandard(
            classProgram, "ClassProvider", schemaIdentity,
            targetRequirement, [], [], [
                new TrRmSourceClassExport(classProgram, classIndex,
                    classDeclaration.Span, "type.box", []),
            ]);
        var classInput = new TrRmStandardArtifactInput(schemaIdentity,
            classArtifact);
        var classReference = TrRmExecutableCompiler.CreateStandardReference(
            classInput, "ClassProvider", [providerInput], ["type.box"]);
        var leafStructure = classReference.Structures.Single(
            static structure => structure.Name == "Leaf");
        Assert.That(leafStructure.Fields.Select(
                static field => field.Name),
            Is.EqualTo(["Child"]));
        var innerStructure = classReference.Structures.Single(
            static structure => structure.Name == "Inner");
        Assert.That(innerStructure.Fields.Single().Name, Is.EqualTo("Value"));
        Assert.That(innerStructure.Fields.Single().Type,
            Is.EqualTo(PrimitiveType.Int32));
        var innerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                providerInput, "StructProvider", [], ["type.inner"],
                ["field.inner.value"]);

        var composed = TrRmExecutableCompiler.CreateStandardReferenceSet([
            classReference, innerReference,
        ]);
        var inner = composed.GenericNominalTypeDefinitions.Single(
            static definition => definition.Name == "Inner");
        Assert.That(inner.NominalKind, Is.EqualTo(1));
        Assert.That(inner.SelectedExportKeys, Is.EqualTo(["type.inner"]));
        Assert.That(inner.ProviderExpectations.Any(expectation =>
                expectation.UsingModule.ModuleIdentity == "ClassProvider"
                && expectation.ExportKey == "type.inner"),
            Is.True);
        var equalityEndpoints = inner.TypeDefBindingEqualityGroups
            .SelectMany(static group => group.Endpoints).ToArray();
        Assert.That(equalityEndpoints.Any(endpoint =>
                endpoint.ImportRow > 0
                && endpoint.OwnerModule.ModuleIdentity == "ClassProvider"
                && endpoint.ExportKey == "type.inner"),
            Is.True);
        Assert.That(equalityEndpoints.Any(endpoint =>
                endpoint.ImportRow == 0
                && endpoint.OwnerModule.ModuleIdentity == "StructProvider"),
            Is.True);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public void Use(Box box, Inner inner) {}"), composed);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var relocatedProviderArtifact = CreateStructProviderArtifact(
            "StructProvider", includePrefix: true, "int32");
        var relocatedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, relocatedProviderArtifact);
        var relocatedClassReference
            = TrRmExecutableCompiler.CreateStandardReference(
                classInput, "ClassProvider", [relocatedProviderInput],
                ["type.box"]);
        var relocatedInnerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                relocatedProviderInput, "StructProvider", [],
                ["type.inner"], ["field.inner.value"]);
        var relocated = TrRmExecutableCompiler.CreateStandardReferenceSet([
            relocatedClassReference, relocatedInnerReference,
        ]);
        Assert.That(relocated.GenericNominalTypeDefinitions.Single(
                static definition => definition.Name == "Inner")
            .TypeDefBindingEqualityGroups.SelectMany(
                static group => group.Endpoints)
            .Any(endpoint =>
                endpoint.ImportRow > 0
                && endpoint.OwnerModule.ModuleIdentity == "ClassProvider"),
            Is.True);

        var incompatibleProviderArtifact = CreateStructProviderArtifact(
            "StructProvider", includePrefix: false, "int64");
        var incompatibleProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, incompatibleProviderArtifact);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(classInput,
                "ClassProvider", [incompatibleProviderInput], ["type.box"]));
    }

    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews)]
    [TestCase(
        StandardGenericMethodSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews)]
    public void ComposesClassFieldsThroughImportedStructViewsAcrossProviders(
        string schemaIdentity)
    {
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;

        byte[] CreateStructProviderArtifact(string moduleIdentity,
            bool includePrefix, string innerValueType)
        {
            var prefixText = includePrefix
                ? "public struct Aardvark { public int32 Value; }"
                : string.Empty;
            var tree = SyntaxTree.Parse($$"""
                {{prefixText}}
                public struct Inner { public {{innerValueType}} Value; }
                public struct Leaf { public Inner Child; }
                """);
            var compilation = Compilation.CreateLibrary(tree);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = (BoundProgram)compilation.Program!;
            var exports = new List<TrRmSourceTypeExport>();
            if (includePrefix)
            {
                exports.Add(new TrRmSourceTypeExport(string.Empty,
                    "Aardvark", "type.aardvark") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Value",
                            "field.aardvark.value"),
                    ],
                });
            }

            exports.Add(new TrRmSourceTypeExport(string.Empty, "Inner",
                "type.inner") {
                FieldExports = [
                    new TrRmSourceFieldExport("Value",
                        "field.inner.value"),
                ],
            });
            exports.Add(new TrRmSourceTypeExport(string.Empty, "Leaf",
                "type.leaf") {
                FieldExports = [
                    new TrRmSourceFieldExport("Child",
                        "field.leaf.child"),
                ],
            });

            return TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, targetRequirement, exports,
                Array.Empty<TrRmSourceGenericMethodExport>(),
                Array.Empty<TrRmSourceClassExport>());
        }

        var providerArtifact = CreateStructProviderArtifact(
            "StructProvider", includePrefix: false, "int32");
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "StructProvider", [], ["type.leaf"],
            ["field.leaf.child"]);

        var classTree = SyntaxTree.Parse("""
            public class Box {
                public Leaf Value;
                public Box(Leaf value) { Value = value; }
            }
            """);
        var classCompilation = Compilation.CreateLibrary(classTree,
            providerReference);
        Assert.That(classCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, classCompilation.Diagnostics));
        var classProgram = (BoundProgram)classCompilation.Program!;
        var classDeclaration = classTree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var classIndex = classProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classArtifact = TrRmExecutableCompiler.EmitStandard(
            classProgram, "ClassProvider", schemaIdentity,
            targetRequirement, [], [], [
                new TrRmSourceClassExport(classProgram, classIndex,
                    classDeclaration.Span, "type.box", []),
            ]);
        var classInput = new TrRmStandardArtifactInput(schemaIdentity,
            classArtifact);
        var classReference = TrRmExecutableCompiler.CreateStandardReference(
            classInput, "ClassProvider", [providerInput], ["type.box"]);
        var innerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                providerInput, "StructProvider", [], ["type.inner"],
                ["field.inner.value"]);

        CompilationReference Compose(
            TrRmStandardArtifactInput structProviderInput,
            CompilationReference selectedInnerReference)
        {
            var selectedClassReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    classInput, "ClassProvider", [structProviderInput],
                    ["type.box"]);
            return TrRmExecutableCompiler.CreateStandardReferenceSet([
                selectedClassReference, selectedInnerReference,
            ]);
        }

        var composed = Compose(providerInput, innerReference);
        var inner = composed.GenericNominalTypeDefinitions.Single(
            static definition => definition.Name == "Inner");
        Assert.That(inner.NominalKind, Is.EqualTo(1));
        Assert.That(inner.SelectedExportKeys, Is.EqualTo(["type.inner"]));
        Assert.That(inner.ProviderExpectations.Any(expectation =>
                expectation.UsingModule.ModuleIdentity == "ClassProvider"
                && expectation.ExportKey == "type.inner"),
            Is.True);
        var equalityEndpoints = inner.TypeDefBindingEqualityGroups
            .SelectMany(static group => group.Endpoints).ToArray();
        Assert.That(equalityEndpoints.Any(endpoint =>
                endpoint.ImportRow > 0
                && endpoint.OwnerModule.ModuleIdentity == "ClassProvider"
                && endpoint.ExportKey == "type.inner"),
            Is.True);
        Assert.That(equalityEndpoints.Any(endpoint =>
                endpoint.ImportRow == 0
                && endpoint.OwnerModule.ModuleIdentity == "StructProvider"),
            Is.True);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public void Use(Box box, Inner inner) {}"), composed);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var providerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                providerArtifact,
                TrRmExecutableCompiler.GetForeignInvocationSchema(
                    schemaIdentity),
                standardSchemaIdentity: schemaIdentity));
        var relocatedProviderArtifact = CreateStructProviderArtifact(
            "StructProvider", includePrefix: true, "int32");
        var relocatedProviderTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                relocatedProviderArtifact,
                TrRmExecutableCompiler.GetForeignInvocationSchema(
                    schemaIdentity),
                standardSchemaIdentity: schemaIdentity));
        Assert.That(providerTables.Exports.TryGetDefinitionForKey(1,
                "type.inner", out var originalInner),
            Is.True);
        Assert.That(relocatedProviderTables.Exports.TryGetDefinitionForKey(1,
                "type.inner", out var relocatedInner),
            Is.True);
        Assert.That(relocatedInner, Is.Not.EqualTo(originalInner));

        var relocatedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, relocatedProviderArtifact);
        var relocatedInnerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                relocatedProviderInput, "StructProvider", [],
                ["type.inner"], ["field.inner.value"]);
        var relocated = Compose(relocatedProviderInput,
            relocatedInnerReference);
        var relocatedDefinition
            = relocated.GenericNominalTypeDefinitions.Single(
                static definition => definition.Name == "Inner");
        Assert.That(relocatedDefinition.TypeDefBindingEqualityGroups,
            Has.Length.EqualTo(1));

        var incompatibleProviderArtifact = CreateStructProviderArtifact(
            "StructProvider", includePrefix: false, "int64");
        var incompatibleProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, incompatibleProviderArtifact);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(classInput,
                "ClassProvider", [incompatibleProviderInput], ["type.box"]));
    }

    [Test]
    public void ComposesClassQueryRootsWithImportedStructFieldProviders()
    {
        var schemaIdentity
            = s_initializerCopyEvidenceSchemaIdentities[2];
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        var layoutOptions = new CompilationOptions {
            LayoutCoreRegistration
                = LayoutCoreRegistrationTestData.Create(),
        };
        var leafTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct LeafA {
                    public int32 Id;
                    public uint8 Tail;
                }
                public struct LeafB {
                    public int64 Id;
                    public uint16 Tail;
                }
            }
            """);
        var leafCompilation = Compilation.CreateLibrary(leafTree,
            layoutOptions);
        Assert.That(leafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, leafCompilation.Diagnostics));
        var leafProgram = (BoundProgram)leafCompilation.Program!;
        var leafAExport = new TrRmSourceTypeExport("Demo", "LeafA",
            "type.leaf.a") {
            FieldExports = [
                new TrRmSourceFieldExport("Id", "field.leaf.a.id"),
                new TrRmSourceFieldExport("Tail", "field.leaf.a.tail"),
            ],
        };
        var leafBExport = new TrRmSourceTypeExport("Demo", "LeafB",
            "type.leaf.b") {
            FieldExports = [
                new TrRmSourceFieldExport("Id", "field.leaf.b.id"),
                new TrRmSourceFieldExport("Tail", "field.leaf.b.tail"),
            ],
        };
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(leafProgram,
            "LeafProvider", schemaIdentity, targetRequirement,
            [leafAExport, leafBExport], [], [], []);
        var leafInput = new TrRmStandardArtifactInput(schemaIdentity,
            leafArtifact);
        var leafReference = TrRmExecutableCompiler.CreateStandardReference(
            leafInput, "LeafProvider", [],
            ["type.leaf.a", "type.leaf.b"], [
                "field.leaf.a.id", "field.leaf.a.tail",
                "field.leaf.b.id", "field.leaf.b.tail",
            ]);

        var alphaTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Wrapper {
                    public LeafA Child;
                }
                public class Alpha {
                    public Wrapper Data;
                }
            }
            """);
        var alphaCompilation = Compilation.CreateLibrary(alphaTree,
            leafReference, layoutOptions);
        Assert.That(alphaCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                alphaCompilation.Diagnostics));
        var alphaProgram = (BoundProgram)alphaCompilation.Program!;
        var alphaClassDeclaration = alphaTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var alphaField = alphaClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var alphaClassIndex = alphaProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == alphaClassDeclaration.Span).ClassIndex;
        var alphaClassExport = new TrRmSourceClassExport(alphaProgram,
            alphaClassIndex, alphaClassDeclaration.Span, "type.alpha", [
                new TrRmSourceClassMemberExport(0, alphaField.Span,
                    "field.alpha.data"),
            ]);
        var wrapperExport = new TrRmSourceTypeExport("Demo", "Wrapper",
            "type.wrapper") {
            FieldExports = [
                new TrRmSourceFieldExport("Child",
                    "field.wrapper.child"),
            ],
        };
        var alphaArtifact = TrRmExecutableCompiler.EmitStandard(
            alphaProgram, "AlphaProvider", schemaIdentity,
            targetRequirement, [wrapperExport], [], [alphaClassExport], []);
        var alphaInput = new TrRmStandardArtifactInput(schemaIdentity,
            alphaArtifact);
        var alphaReference = TrRmExecutableCompiler.CreateStandardReference(
            alphaInput, "AlphaProvider", [leafInput], [
                "type.alpha", "type.wrapper",
            ], ["field.alpha.data", "field.wrapper.child"]);
        Assert.That(alphaReference.Structures.Single(
                static structure => structure.Name == "Demo.LeafA")
                .StandardTypeOrigin!.Expectations, Has.Length.EqualTo(1));

        var betaTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct WrapperBeta {
                    public LeafB Child;
                }
                public class Beta {
                    public WrapperBeta Data;
                }
            }
            """);
        var betaCompilation = Compilation.CreateLibrary(betaTree,
            leafReference, layoutOptions);
        Assert.That(betaCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, betaCompilation.Diagnostics));
        var betaProgram = (BoundProgram)betaCompilation.Program!;
        var betaClassDeclaration = betaTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var betaField = betaClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var betaClassIndex = betaProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == betaClassDeclaration.Span).ClassIndex;
        var betaClassExport = new TrRmSourceClassExport(betaProgram,
            betaClassIndex, betaClassDeclaration.Span, "type.beta", [
                new TrRmSourceClassMemberExport(0, betaField.Span,
                    "field.beta.data"),
            ]);
        var wrapperBetaExport = new TrRmSourceTypeExport("Demo",
            "WrapperBeta", "type.wrapper.beta") {
            FieldExports = [
                new TrRmSourceFieldExport("Child",
                    "field.wrapper.beta.child"),
            ],
        };
        var betaArtifact = TrRmExecutableCompiler.EmitStandard(betaProgram,
            "BetaProvider", schemaIdentity, targetRequirement,
            [wrapperBetaExport], [],
            [betaClassExport], []);
        var betaInput = new TrRmStandardArtifactInput(schemaIdentity,
            betaArtifact);
        var betaReference = TrRmExecutableCompiler.CreateStandardReference(
            betaInput, "BetaProvider", [leafInput], [
                "type.beta", "type.wrapper.beta",
            ], ["field.beta.data", "field.wrapper.beta.child"]);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet(
            [alphaReference, betaReference]);
        Assert.That(reference.Structures.Single(
                static structure => structure.Name == "Demo.LeafA")
                .StandardTypeOrigin!.Expectations, Has.Length.EqualTo(1));
        Assert.That(reference.Structures.Single(
                static structure => structure.Name == "Demo.LeafB")
                .StandardTypeOrigin!.Expectations, Has.Length.EqualTo(1));

        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint CombinedOffset() {
                    return unsafe(offsetof(Alpha, Data.Child.Tail))
                        + unsafe(offsetof(Beta, Data.Child.Tail));
                }
                """), reference, layoutOptions);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            consumer.Program!, "ImportedStructLayoutConsumer",
            schemaIdentity, targetRequirement);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                artifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        Assert.That(tables.TargetLayoutDependencies.Count,
            Is.EqualTo(2));
        Assert.That(tables.TargetLayoutDependencyFields.Count,
            Is.EqualTo(6));
        var imports = Enumerable.Range(1, tables.Imports.Count)
            .Select(tables.Imports.GetBindingForImportRow).ToArray();
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "LeafProvider"
                && binding.ExportKey == "type.leaf.a"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "LeafProvider"
                && binding.ExportKey == "type.leaf.b"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "LeafProvider"
                && binding.ExportKey == "field.leaf.a.tail"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "LeafProvider"
                && binding.ExportKey == "field.leaf.b.tail"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "AlphaProvider"
                && binding.ExportKey == "field.wrapper.child"),
            Is.True);
        Assert.That(imports.Any(static binding =>
                binding.ProducerIdentity == "BetaProvider"
                && binding.ExportKey == "field.wrapper.beta.child"),
            Is.True);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "ImportedStructLayoutConsumer",
            [alphaInput, leafInput, betaInput]);
        Assert.That(loaded.Functions.Single().Name,
            Is.EqualTo("CombinedOffset"));
    }

    [Test]
    public void LowersEN1ClassQueryAcrossImportedStructProviders()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var schemaIdentity
            = s_initializerCopyEvidenceSchemaIdentities[2];
        var layoutOptions = new CompilationOptions {
            LayoutCoreRegistration
                = LayoutCoreRegistrationTestData.Create(),
        };
        var leafTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Leaf {
                    public int32 Id;
                    public uint8 Tail;
                }
            }
            """);
        var leafCompilation = Compilation.CreateLibrary(leafTree,
            layoutOptions);
        Assert.That(leafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, leafCompilation.Diagnostics));
        var leafProgram = (BoundProgram)leafCompilation.Program!;
        var leafExport = new TrRmSourceTypeExport("Demo", "Leaf",
            "type.leaf") {
            FieldExports = [
                new TrRmSourceFieldExport("Id", "field.leaf.id"),
                new TrRmSourceFieldExport("Tail", "field.leaf.tail"),
            ],
        };
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafProgram, "LeafLayoutProvider", schemaIdentity, 1,
            [leafExport], [], [], []);
        var leafInput = new TrRmStandardArtifactInput(schemaIdentity,
            leafArtifact);
        var leafReference = TrRmExecutableCompiler.CreateStandardReference(
            leafInput, "LeafLayoutProvider", [], ["type.leaf"], [
                "field.leaf.id",
                "field.leaf.tail",
            ]);

        var wrapperTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Wrapper {
                    public uint8 Prefix;
                    public Leaf Child;
                }
            }
            """);
        var wrapperCompilation = Compilation.CreateLibrary(wrapperTree,
            leafReference, layoutOptions);
        Assert.That(wrapperCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                wrapperCompilation.Diagnostics));
        var wrapperProgram = (BoundProgram)wrapperCompilation.Program!;
        var wrapperExport = new TrRmSourceTypeExport("Demo", "Wrapper",
            "type.wrapper") {
            FieldExports = [
                new TrRmSourceFieldExport("Prefix",
                    "field.wrapper.prefix"),
                new TrRmSourceFieldExport("Child",
                    "field.wrapper.child"),
            ],
        };
        var wrapperArtifact = TrRmExecutableCompiler.EmitStandard(
            wrapperProgram, "WrapperLayoutProvider", schemaIdentity, 1,
            [wrapperExport], [], [], []);
        var wrapperInput = new TrRmStandardArtifactInput(schemaIdentity,
            wrapperArtifact);
        var wrapperReference
            = TrRmExecutableCompiler.CreateStandardReference(wrapperInput,
                "WrapperLayoutProvider", [leafInput], ["type.wrapper"], [
                    "field.wrapper.prefix",
                    "field.wrapper.child",
                ]);

        var boxTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box {
                    public uint8 Prefix;
                    public Wrapper Value;
                    public Box(uint8 prefix, Wrapper value) {
                        Prefix = prefix;
                        Value = value;
                    }
                }
            }
            """);
        var boxCompilation = Compilation.CreateLibrary(boxTree,
            wrapperReference, layoutOptions);
        Assert.That(boxCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, boxCompilation.Diagnostics));
        var boxProgram = (BoundProgram)boxCompilation.Program!;
        var boxArtifact = TrRmExecutableCompiler.EmitStandard(boxProgram,
            "ClassLayoutProvider", schemaIdentity, 1, [], [], [
                CreateSourceClassExport(boxProgram, boxTree, "Box",
                    "type.box", "constructor.box",
                    (0, "field.box.prefix"),
                    (1, "field.box.value")),
            ], []);
        var boxInput = new TrRmStandardArtifactInput(schemaIdentity,
            boxArtifact);
        var boxReference = TrRmExecutableCompiler.CreateStandardReference(
            boxInput, "ClassLayoutProvider", [wrapperInput, leafInput],
            ["type.box"], ["field.box.prefix", "field.box.value"]);
        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Value.Child.Tail));
                }
                """), boxReference, layoutOptions);
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                queryCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassLayoutConsumer",
            schemaIdentity, 1);
        var providers = new[] { boxInput, wrapperInput, leafInput };

        static int ExecuteOffset(BoundProgram program)
        {
            var returned = (BoundReturn)program.Functions.Single().Body
                .Statements.Single();
            var main = program.Functions.Single() with {
                Name = "Main",
                ReturnType = PrimitiveType.Int32,
                Body = new BoundBlock([
                    new BoundReturn(new BoundCast(returned.Value!,
                        PrimitiveType.Int32, default), default),
                ], default),
            };
            return NativeJit.Execute(program with {
                Functions = [main],
                EntryPoint = 0,
            }, NativeRuntimePolicy.FailFast);
        }

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", providers);
        var loadedReturn = (BoundReturn)loaded.Functions.Single().Body
            .Statements.Single();
        var loadedQuery = (BoundLayoutQuery)((BoundUnsafeExpression)
            loadedReturn.Value!).Expression;
        var fieldPath = loadedQuery.Subject.FieldPath!;
        Assert.That(fieldPath.Steps, Has.Length.EqualTo(3));
        Assert.That(fieldPath.Steps[0].StandardClassFieldOrigin!
            .SelectedExportKeys, Is.EqualTo(["field.box.value"]));
        Assert.That(fieldPath.Steps[1].StandardFieldOrigin!.Provider
            .ModuleIdentity, Is.EqualTo("WrapperLayoutProvider"));
        Assert.That(fieldPath.Steps[2].StandardFieldOrigin!.Provider
            .ModuleIdentity, Is.EqualTo("LeafLayoutProvider"));
        Assert.That(ExecuteOffset(loaded), Is.EqualTo(12));
        Assert.That(NativeModuleCompiler.EmitStandardLibrary(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", providers, ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);

        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "ClassLayoutConsumer", schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "ClassLayoutConsumer", providers);
        Assert.That(ExecuteOffset(reloaded), Is.EqualTo(12));

        var relocatedLeafTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Padding { public int64 Value; }
                public struct Leaf {
                    public int32 Id;
                    public uint8 Tail;
                }
            }
            """);
        var relocatedLeafCompilation
            = Compilation.CreateLibrary(relocatedLeafTree, layoutOptions);
        Assert.That(relocatedLeafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                relocatedLeafCompilation.Diagnostics));
        var relocatedLeafArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)relocatedLeafCompilation.Program!,
            "LeafLayoutProvider", schemaIdentity, 1, [
                new TrRmSourceTypeExport("Demo", "Padding",
                    "type.padding"),
                leafExport,
            ], [], [], []);
        var relocatedLeafInput = new TrRmStandardArtifactInput(
            schemaIdentity, relocatedLeafArtifact);
        var relocatedProviders
            = new[] { boxInput, wrapperInput, relocatedLeafInput };
        var relocated = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", relocatedProviders);
        Assert.That(ExecuteOffset(relocated), Is.EqualTo(12));
        var relocatedReemitted = TrRmExecutableCompiler.EmitStandard(
            relocated, "ClassLayoutConsumer", schemaIdentity, 1);
        var relocatedReloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                relocatedReemitted), "ClassLayoutConsumer",
            relocatedProviders);
        Assert.That(ExecuteOffset(relocatedReloaded), Is.EqualTo(12));

        var incompatibleLeafTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Leaf {
                    public int32 Id;
                    public int64 Tail;
                }
            }
            """);
        var incompatibleLeafCompilation
            = Compilation.CreateLibrary(incompatibleLeafTree, layoutOptions);
        Assert.That(incompatibleLeafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleLeafCompilation.Diagnostics));
        var incompatibleLeafArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)incompatibleLeafCompilation.Program!,
            "LeafLayoutProvider", schemaIdentity, 1, [leafExport], [], [],
            []);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact), "ClassLayoutConsumer", [
                    boxInput,
                    wrapperInput,
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleLeafArtifact),
                ]));
    }

    [Test]
    public void IncludesUnselectedPrivateFieldsInEN1ClassOffsets()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box {
                    private uint8 Hidden;
                    public uint8 Prefix;
                    public int32 Value;
                    public Box(uint8 hidden, uint8 prefix, int32 value) {
                        Hidden = hidden;
                        Prefix = prefix;
                        Value = value;
                    }
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var classDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var fields = classDeclaration.Children.OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.FieldDeclaration)
            .ToArray();
        var classIndex = providerProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(providerProgram,
            classIndex, classDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0,
                    fields[2].Span, "field.box.value"),
                CreateSourceClassConstructorExport(classDeclaration,
                    "constructor.box"),
            ]);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ClassLayoutProvider", schemaIdentity, 1, [],
            [], [classExport], []);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "ClassLayoutProvider", [], ["type.box"],
            ["field.box.value"]);
        var classView = reference.StandardClassDeclarations.Single(
            static declaration =>
                !declaration.SelectedExportKeys.IsEmpty).Declaration;
        Assert.That(classView.Fields.Single(
            static field => field.Name == "Hidden").SelectedExportKeys,
            Is.Empty);
        Assert.That(classView.Fields.Single(
            static field => field.Name == "Prefix").SelectedExportKeys,
            Is.Empty);

        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, queryCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassLayoutConsumer",
            schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                consumerArtifact), "ClassLayoutConsumer", [providerInput]);
        var returned = (BoundReturn)loaded.Functions.Single().Body
            .Statements.Single();
        var main = loaded.Functions.Single() with {
            Name = "Main",
            ReturnType = PrimitiveType.Int32,
            Body = new BoundBlock([
                new BoundReturn(new BoundCast(returned.Value!,
                    PrimitiveType.Int32, default), default),
            ], default),
        };
        Assert.That(NativeJit.Execute(loaded with {
                Functions = [main],
                EntryPoint = 0,
            }, NativeRuntimePolicy.FailFast), Is.EqualTo(4));

        var coLocatedArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassLayoutProvider",
            schemaIdentity, 1);
        var coLocatedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                coLocatedArtifact, SchemaFor(schemaIdentity), null,
                usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        var prefixFieldRow = Enumerable.Range(1,
                coLocatedTables.Fields.Count)
            .Single(row =>
                coLocatedTables.Fields.GetName(row) == "Prefix");
        var malformedPath = RewriteEN1ClassQueryFieldReference(
            coLocatedArtifact, schemaIdentity, 1, 1, 0,
            0x1400_0000u
                | checked((uint)prefixFieldRow));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedPath), "ClassLayoutProvider", []));
    }

    [Test]
    public void LowersEN1ClassFieldEnumUsingItsUnderlyingLayout()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Marker : int32 { Value = 1 }
                public class Box {
                    public uint8 Prefix;
                    public Marker Value;
                    public Box(uint8 prefix, Marker value) {
                        Prefix = prefix;
                        Value = value;
                    }
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var classDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var fields = classDeclaration.Children.OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.FieldDeclaration)
            .ToArray();
        var classIndex = providerProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(providerProgram,
            classIndex, classDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0, fields[0].Span,
                    "field.box.prefix"),
                new TrRmSourceClassMemberExport(0, fields[1].Span,
                    "field.box.value"),
                CreateSourceClassConstructorExport(classDeclaration,
                    "constructor.box"),
            ]);
        var enumExport = CreateExport(providerProgram, providerTree, 0,
            "Marker", ["type.marker"], "marker");
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ClassLayoutProvider", schemaIdentity, 1, [],
            [], [classExport], [enumExport]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "ClassLayoutProvider", [], [
                "type.box",
                "type.marker",
            ], [
                "field.box.prefix",
                "field.box.value",
            ]);
        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, queryCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassLayoutConsumer",
            schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                consumerArtifact), "ClassLayoutConsumer", [providerInput]);
        var returned = (BoundReturn)loaded.Functions.Single().Body
            .Statements.Single();
        var main = loaded.Functions.Single() with {
            Name = "Main",
            ReturnType = PrimitiveType.Int32,
            Body = new BoundBlock([
                new BoundReturn(new BoundCast(returned.Value!,
                    PrimitiveType.Int32, default), default),
            ], default),
        };
        var executable = loaded with {
            Functions = [main],
            EntryPoint = 0,
        };

        Assert.That(NativeJit.Execute(executable,
            NativeRuntimePolicy.FailFast), Is.EqualTo(4));
    }

    [Test]
    public void LowersEN1RawPointerClassFieldStorage()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box {
                    public uint8 Prefix;
                    public ptr<void> Value;
                    public Box(uint8 prefix, ptr<void> value) {
                        Prefix = prefix;
                        Value = value;
                    }
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var classDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var fields = classDeclaration.Children.OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.FieldDeclaration)
            .ToArray();
        var classIndex = providerProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(providerProgram,
            classIndex, classDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0, fields[0].Span,
                    "field.box.prefix"),
                new TrRmSourceClassMemberExport(0, fields[1].Span,
                    "field.box.value"),
                CreateSourceClassConstructorExport(classDeclaration,
                    "constructor.box"),
            ]);
        var targetMismatch = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(providerProgram,
                "ClassLayoutProvider", s_schemaIdentities[0], 0, [], [],
                [classExport], []));
        Assert.That(targetMismatch!.Message,
            Does.Contain("Windows x64 target"));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ClassLayoutProvider", schemaIdentity, 1, [],
            [], [classExport], []);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "ClassLayoutProvider", [], ["type.box"], [
                "field.box.prefix",
                "field.box.value",
            ]);
        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, queryCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassLayoutConsumer",
            schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                consumerArtifact), "ClassLayoutConsumer", [providerInput]);
        var returned = (BoundReturn)loaded.Functions.Single().Body
            .Statements.Single();
        var main = loaded.Functions.Single() with {
            Name = "Main",
            ReturnType = PrimitiveType.Int32,
            Body = new BoundBlock([
                new BoundReturn(new BoundCast(returned.Value!,
                    PrimitiveType.Int32, default), default),
            ], default),
        };
        var executable = loaded with {
            Functions = [main],
            EntryPoint = 0,
        };

        Assert.That(NativeJit.Execute(executable,
            NativeRuntimePolicy.FailFast), Is.EqualTo(8));

        var reemittedConsumerArtifact = TrRmExecutableCompiler.EmitStandard(
            loaded, "ClassLayoutConsumerReemitted", schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedConsumerArtifact), "ClassLayoutConsumerReemitted",
            [providerInput]);
        var reloadedReturn = (BoundReturn)reloaded.Functions.Single().Body
            .Statements.Single();
        var reloadedQuery = (BoundLayoutQuery)((BoundUnsafeExpression)
            reloadedReturn.Value!).Expression;
        var reloadedPointerType = reloadedQuery.Subject.FieldPath!.Steps
            .Single().FieldType;
        Assert.That(reloaded.NativePointerTypes[
            NativePointerTypes.Index(reloadedPointerType)],
            Is.EqualTo(new NativePointerTypeDefinition(
                PrimitiveType.UInt8, false)));
        var reloadedMain = reloaded.Functions.Single() with {
            Name = "Main",
            ReturnType = PrimitiveType.Int32,
            Body = new BoundBlock([
                new BoundReturn(new BoundCast(reloadedReturn.Value!,
                    PrimitiveType.Int32, default), default),
            ], default),
        };
        var reloadedExecutable = reloaded with {
            Functions = [reloadedMain],
            EntryPoint = 0,
        };
        Assert.That(NativeJit.Execute(reloadedExecutable,
            NativeRuntimePolicy.FailFast), Is.EqualTo(8));

        Assert.That(NativeModuleCompiler.EmitStandardLibrary(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", [providerInput], ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void LowersEN1TypedDataPointerClassFieldStorage()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box {
                    public uint8 Prefix;
                    public ptr<uint8> Value;
                    public Box(uint8 prefix, ptr<uint8> value) {
                        Prefix = prefix;
                        Value = value;
                    }
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var classDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var fields = classDeclaration.Children.OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.FieldDeclaration)
            .ToArray();
        var classIndex = providerProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(providerProgram,
            classIndex, classDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0, fields[0].Span,
                    "field.box.prefix"),
                new TrRmSourceClassMemberExport(0, fields[1].Span,
                    "field.box.value"),
                CreateSourceClassConstructorExport(classDeclaration,
                    "constructor.box"),
            ]);
        var targetMismatch = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(providerProgram,
                "ClassLayoutProvider", s_schemaIdentities[0], 0, [], [],
                [classExport], []));
        Assert.That(targetMismatch!.Message,
            Does.Contain("Windows x64 target"));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ClassLayoutProvider", schemaIdentity, 1, [],
            [], [classExport], []);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "ClassLayoutProvider", [], ["type.box"], [
                "field.box.prefix",
                "field.box.value",
            ]);
        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, queryCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassLayoutConsumer",
            schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                consumerArtifact), "ClassLayoutConsumer", [providerInput]);
        var returned = (BoundReturn)loaded.Functions.Single().Body
            .Statements.Single();
        var boundQuery = (BoundLayoutQuery)((BoundUnsafeExpression)
            returned.Value!).Expression;
        var pointerType = boundQuery.Subject.FieldPath!.Steps.Single()
            .FieldType;
        Assert.That(NativePointerTypes.IsType(pointerType), Is.True);
        Assert.That(loaded.NativePointerTypes[
            NativePointerTypes.Index(pointerType)],
            Is.EqualTo(new NativePointerTypeDefinition(
                PrimitiveType.UInt8, false)));
        var main = loaded.Functions.Single() with {
            Name = "Main",
            ReturnType = PrimitiveType.Int32,
            Body = new BoundBlock([
                new BoundReturn(new BoundCast(returned.Value!,
                    PrimitiveType.Int32, default), default),
            ], default),
        };
        var executable = loaded with {
            Functions = [main],
            EntryPoint = 0,
        };

        Assert.That(NativeJit.Execute(executable,
            NativeRuntimePolicy.FailFast), Is.EqualTo(8));
        Assert.That(NativeModuleCompiler.EmitStandardLibrary(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", [providerInput], ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void LowersEN1ClassFieldPointerStorageAndRemapsPointees()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Marker : int32 { Value = 1 }
                public struct Payload { public int32 Value; }
                public class Box {
                    public uint8 Prefix;
                    public ptr<Payload> Address;
                    public ptr<Marker> EnumAddress;
                    public Box(uint8 prefix, ptr<Payload> address,
                        ptr<Marker> enumAddress) {
                        Prefix = prefix;
                        Address = address;
                        EnumAddress = enumAddress;
                    }
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var classDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var classFields = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.FieldDeclaration)
            .ToArray();
        var classIndex = providerProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(providerProgram,
            classIndex, classDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0,
                    classFields[0].Span, "field.box.prefix"),
                new TrRmSourceClassMemberExport(0,
                    classFields[1].Span, "field.box.address"),
                new TrRmSourceClassMemberExport(0,
                    classFields[2].Span, "field.box.enum-address"),
                CreateSourceClassConstructorExport(classDeclaration,
                    "constructor.box"),
            ]);
        var markerExport = CreateExport(providerProgram, providerTree, 0,
            "Marker", ["type.marker"], "marker");
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ClassLayoutProvider", schemaIdentity, 1, [
                new TrRmSourceTypeExport("Demo", "Payload",
                    "type.payload") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Value",
                            "field.payload.value"),
                    ],
                },
            ], [], [classExport], [markerExport]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "ClassLayoutProvider", [], ["type.box"], [
                "field.box.prefix",
                "field.box.address",
            ]);
        var payloadReference = reference.Structures.Single(
            static structure => structure.Name == "Demo.Payload");
        Assert.That(payloadReference.StandardTypeOrigin!
            .SelectedExportKeys, Is.Empty);
        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Address));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, queryCompilation.Diagnostics));
        var traversalReference
            = TrRmExecutableCompiler.CreateStandardReference(
                providerInput, "ClassLayoutProvider", [],
                ["type.box"], [
                    "field.box.prefix",
                    "field.box.address",
                    "field.box.enum-address",
                ]);
        var pointerTraversalCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Address.Value));
                }
                """), traversalReference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(pointerTraversalCompilation.HasErrors, Is.True);
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassLayoutConsumer",
            schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", [providerInput]);
        var returned = (BoundReturn)loaded.Functions.Single().Body
            .Statements.Single();
        var query = (BoundLayoutQuery)((BoundUnsafeExpression)
            returned.Value!).Expression;
        var pointerType = query.Subject.FieldPath!.Steps.Single().FieldType;
        Assert.That(NativePointerTypes.IsType(pointerType), Is.True);
        var pointeeType = loaded.NativePointerTypes[
            NativePointerTypes.Index(pointerType)].PointeeType;
        var payloadIndex = Enumerable.Range(0, loaded.Structures.Length)
            .Single(index =>
                loaded.Structures[index].Name == "Demo.Payload");
        Assert.That(pointeeType, Is.EqualTo(
            (PrimitiveType)(100 + payloadIndex)));
        var main = loaded.Functions.Single() with {
            Name = "Main",
            ReturnType = PrimitiveType.Int32,
            Body = new BoundBlock([
                new BoundReturn(new BoundCast(returned.Value!,
                    PrimitiveType.Int32, default), default),
            ], default),
        };
        var executable = loaded with {
            Functions = [main],
            EntryPoint = 0,
        };
        Assert.That(NativeJit.Execute(executable,
            NativeRuntimePolicy.FailFast), Is.EqualTo(8));

        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "ClassLayoutConsumerReemitted", schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "ClassLayoutConsumerReemitted",
            [providerInput]);
        var reloadedReturn = (BoundReturn)reloaded.Functions.Single().Body
            .Statements.Single();
        var reloadedQuery = (BoundLayoutQuery)((BoundUnsafeExpression)
            reloadedReturn.Value!).Expression;
        var reloadedPointerType = reloadedQuery.Subject.FieldPath!
            .Steps.Single().FieldType;
        Assert.That(NativePointerTypes.IsType(reloadedPointerType), Is.True);
        var reloadedPointeeType = reloaded.NativePointerTypes[
            NativePointerTypes.Index(reloadedPointerType)].PointeeType;
        var reloadedPayloadIndex = Enumerable.Range(0,
                reloaded.Structures.Length)
            .Single(index =>
                reloaded.Structures[index].Name == "Demo.Payload");
        Assert.That(reloadedPointeeType, Is.EqualTo(
            (PrimitiveType)(100 + reloadedPayloadIndex)));

        var relocatedProviderTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum ExtraMarker : int32 { Value = 0 }
                public enum Marker : int32 { Value = 1 }
                public struct Extra { public int32 Value; }
                public struct Payload { public int32 Value; }
                public class Box {
                    public uint8 Prefix;
                    public ptr<Payload> Address;
                    public ptr<Marker> EnumAddress;
                    public Box(uint8 prefix, ptr<Payload> address,
                        ptr<Marker> enumAddress) {
                        Prefix = prefix;
                        Address = address;
                        EnumAddress = enumAddress;
                    }
                }
            }
            """);
        var relocatedProviderCompilation
            = Compilation.CreateLibrary(relocatedProviderTree);
        Assert.That(relocatedProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                relocatedProviderCompilation.Diagnostics));
        var relocatedProviderProgram
            = (BoundProgram)relocatedProviderCompilation.Program!;
        var relocatedClassDeclaration
            = relocatedProviderTree.Root.Children
                .OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.ClassDeclaration);
        var relocatedClassFields = relocatedClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.FieldDeclaration)
            .ToArray();
        var relocatedClassIndex
            = relocatedProviderProgram.SourceClassProgramAssociation!
                .Declarations.Single(declaration =>
                    declaration.Span == relocatedClassDeclaration.Span)
                .ClassIndex;
        var relocatedClassExport = new TrRmSourceClassExport(
            relocatedProviderProgram, relocatedClassIndex,
            relocatedClassDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0,
                    relocatedClassFields[0].Span, "field.box.prefix"),
                new TrRmSourceClassMemberExport(0,
                    relocatedClassFields[1].Span, "field.box.address"),
                new TrRmSourceClassMemberExport(0,
                    relocatedClassFields[2].Span, "field.box.enum-address"),
                CreateSourceClassConstructorExport(
                    relocatedClassDeclaration, "constructor.box"),
            ]);
        var relocatedExtraMarkerExport = CreateExport(
            relocatedProviderProgram, relocatedProviderTree, 0,
            "ExtraMarker", ["type.extra-marker"], "extra-marker");
        var relocatedMarkerExport = CreateExport(
            relocatedProviderProgram, relocatedProviderTree, 0,
            "Marker", ["type.marker"], "marker");
        var relocatedProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                relocatedProviderProgram, "ClassLayoutProvider",
                schemaIdentity, 1, [
                    new TrRmSourceTypeExport("Demo", "Extra",
                        "type.extra"),
                    new TrRmSourceTypeExport("Demo", "Payload",
                        "type.payload") {
                        FieldExports = [
                            new TrRmSourceFieldExport("Value",
                                "field.payload.value"),
                        ],
                    },
                ], [], [relocatedClassExport], [
                        relocatedExtraMarkerExport,
                        relocatedMarkerExport,
                    ]);
        var relocatedProviderInput
            = new TrRmStandardArtifactInput(schemaIdentity,
                relocatedProviderArtifact);
        var relocatedLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", [relocatedProviderInput]);
        Assert.That(relocatedLoaded.NativePointerTypes,
            Has.Length.EqualTo(2));
        var relocatedMarkerIndex = Enumerable.Range(0,
                relocatedLoaded.EnumDefinitions.Length)
            .Single(index =>
                relocatedLoaded.EnumDefinitions[index].Name
                    == "Demo.Marker");
        var relocatedBox = relocatedLoaded.StandardClassDeclarations
            .Single(declaration =>
                declaration.Declaration.Namespace == "Demo"
                    && declaration.Declaration.Name == "Box")
            .Declaration;
        var relocatedEnumPointerType = relocatedBox.Fields
            .Single(field => field.Name == "EnumAddress").Type;
        Assert.That(NativePointerTypes.IsType(relocatedEnumPointerType),
            Is.True);
        Assert.That(relocatedLoaded.NativePointerTypes[
            NativePointerTypes.Index(relocatedEnumPointerType)].PointeeType,
            Is.EqualTo(BoundEnumTypes.Type(relocatedMarkerIndex)));
        var relocatedReturn = (BoundReturn)relocatedLoaded.Functions
            .Single().Body.Statements.Single();
        var relocatedQuery = (BoundLayoutQuery)((BoundUnsafeExpression)
            relocatedReturn.Value!).Expression;
        var relocatedPointerType
            = relocatedQuery.Subject.FieldPath!.Steps.Single().FieldType;
        var relocatedPointeeType = relocatedLoaded.NativePointerTypes[
            NativePointerTypes.Index(relocatedPointerType)].PointeeType;
        var relocatedPayloadIndex = Enumerable.Range(0,
                relocatedLoaded.Structures.Length)
            .Single(index =>
                relocatedLoaded.Structures[index].Name == "Demo.Payload");
        Assert.That(relocatedPointeeType, Is.EqualTo(
            (PrimitiveType)(100 + relocatedPayloadIndex)));
        var relocatedMain = relocatedLoaded.Functions.Single() with {
            Name = "Main",
            ReturnType = PrimitiveType.Int32,
            Body = new BoundBlock([
                new BoundReturn(new BoundCast(relocatedReturn.Value!,
                    PrimitiveType.Int32, default), default),
            ], default),
        };
        var relocatedExecutable = relocatedLoaded with {
            Functions = [relocatedMain],
            EntryPoint = 0,
        };
        Assert.That(NativeJit.Execute(relocatedExecutable,
            NativeRuntimePolicy.FailFast), Is.EqualTo(8));

        var relocatedReemittedArtifact
            = TrRmExecutableCompiler.EmitStandard(relocatedLoaded,
                "ClassLayoutConsumerReemitted", schemaIdentity, 1);
        var relocatedReloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                relocatedReemittedArtifact),
            "ClassLayoutConsumerReemitted", [relocatedProviderInput]);
        Assert.That(relocatedReloaded.NativePointerTypes,
            Has.Length.EqualTo(1));
    }

    [Test]
    public void LowersEN1NativeFunctionPointerClassFieldStorage()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        var providerOptions
            = Terrum.CodeAnalysis.Tests.Semantics
                .NativeCoreRegistrationTests
                .CreateRegisteredCompilationOptions() with {
                    LayoutCoreRegistration
                        = LayoutCoreRegistrationTestData.Create(),
                };
        var providerTree = SyntaxTree.Parse("""
            using Terrum.Interop;
            namespace Demo {
                public class Box {
                    public uint8 Prefix;
                    public [NativeCallConv(NativeCallingConvention.Cdecl)]
                        fnptr<int32> Callback;
                    public Box(uint8 prefix,
                        [NativeCallConv(NativeCallingConvention.Cdecl)]
                        fnptr<int32> callback) {
                        Prefix = prefix;
                        Callback = callback;
                    }
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree,
            providerOptions);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var classDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var classFields = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.FieldDeclaration)
            .ToArray();
        var classIndex = providerProgram.SourceClassProgramAssociation!
            .Declarations.Single(declaration =>
                declaration.Span == classDeclaration.Span).ClassIndex;
        var classExport = new TrRmSourceClassExport(providerProgram,
            classIndex, classDeclaration.Span, "type.box", [
                new TrRmSourceClassMemberExport(0,
                    classFields[0].Span, "field.box.prefix"),
                new TrRmSourceClassMemberExport(0,
                    classFields[1].Span, "field.box.callback"),
                CreateSourceClassConstructorExport(classDeclaration,
                    "constructor.box"),
            ]);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ClassLayoutProvider", schemaIdentity, 1, [],
            [], [classExport], []);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    providerArtifact, SchemaFor(schemaIdentity), null,
                    usesNativePointerLoads: false,
                    standardSchemaIdentity: schemaIdentity));
        var providerClassRow = Enumerable.Range(1,
                providerTables.TypeDefs.Count)
            .Single(row =>
                providerTables.TypeDefs.GetName(row) == "Box");
        var (providerFieldStart, _) = providerTables.TypeDefs
            .GetFieldRange(providerClassRow);
        var callbackType = providerTables.TypeSpecs.GetTypeSpecRef(
            providerTables.Fields.GetTypeSpecRef(providerFieldStart + 1));
        Assert.That(callbackType.Form, Is.EqualTo((byte)0x0D));
        Assert.That(providerTables.NativeFunctionPointerAnchors
            .HasAnchorForTypeSpec(callbackType.Row), Is.True);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "ClassLayoutProvider", [], ["type.box"], [
                "field.box.prefix",
                "field.box.callback",
            ]);
        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Callback));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, queryCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassLayoutConsumer",
            schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", [providerInput]);
        var returned = (BoundReturn)loaded.Functions.Single().Body
            .Statements.Single();
        var query = (BoundLayoutQuery)((BoundUnsafeExpression)
            returned.Value!).Expression;
        var fieldType = query.Subject.FieldPath!.Steps.Single().FieldType;
        Assert.That(NativeFunctionPointerTypes.IsType(fieldType), Is.True);
        var signature = loaded.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(fieldType)];
        Assert.That(signature.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        Assert.That(signature.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(signature.ParameterTypes, Is.Empty);
        var main = loaded.Functions.Single() with {
            Name = "Main",
            ReturnType = PrimitiveType.Int32,
            Body = new BoundBlock([
                new BoundReturn(new BoundCast(returned.Value!,
                    PrimitiveType.Int32, default), default),
            ], default),
        };
        var executable = loaded with {
            Functions = [main],
            EntryPoint = 0,
        };
        Assert.That(NativeJit.Execute(executable,
            NativeRuntimePolicy.FailFast), Is.EqualTo(8));

        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "ClassLayoutConsumerReemitted", schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "ClassLayoutConsumerReemitted",
            [providerInput]);
        var reloadedReturn = (BoundReturn)reloaded.Functions.Single().Body
            .Statements.Single();
        var reloadedQuery = (BoundLayoutQuery)((BoundUnsafeExpression)
            reloadedReturn.Value!).Expression;
        var reloadedFieldType
            = reloadedQuery.Subject.FieldPath!.Steps.Single().FieldType;
        Assert.That(NativeFunctionPointerTypes.IsType(reloadedFieldType),
            Is.True);
        Assert.That(NativeModuleCompiler.EmitStandardLibrary(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassLayoutConsumer", [providerInput], ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void LowersEN1PointerToClassFieldStorageAndRemapsPointees()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var schemaIdentity
            = $"{s_schemaIdentities[0]}-class-instance-layout-1";
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Target {
                    public int32 Value;
                    public Target(int32 value) { Value = value; }
                }
                public class Box {
                    public uint8 Prefix;
                    public ptr<Target> Address;
                    public Box(uint8 prefix, ptr<Target> address) {
                        Prefix = prefix;
                        Address = address;
                    }
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ClassPointerProvider", schemaIdentity, 1, [], [],
            [
                CreateSourceClassExport(providerProgram, providerTree,
                    "Target", "type.target", "constructor.target"),
                CreateSourceClassExport(providerProgram, providerTree,
                    "Box", "type.box", "constructor.box",
                    (0, "field.box.prefix"),
                    (1, "field.box.address")),
            ], []);
        var providerTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    providerArtifact, SchemaFor(schemaIdentity)));
        var targetDefinition
            = providerTables.Exports.GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "type.target").Definition;
        var boxDefinition = providerTables.Exports.GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "type.box").Definition;
        var boxTypeDefRow = checked((int)(boxDefinition & 0x00FF_FFFF));
        var (boxFieldStart, boxFieldEnd) = providerTables.TypeDefs
            .GetFieldRange(boxTypeDefRow);
        var addressFieldRow = Enumerable.Range(boxFieldStart,
                boxFieldEnd - boxFieldStart)
            .Single(row =>
                providerTables.Fields.GetName(row) == "Address");
        var addressTypeSpec = providerTables.TypeSpecs.GetTypeSpecRef(
            providerTables.Fields.GetTypeSpecRef(addressFieldRow));
        Assert.That(addressTypeSpec.Form, Is.EqualTo((byte)0x0C));
        Assert.That(providerTables.TypeSpecs
            .TryGetNativePointerPointeeTypeSpecRef(
                providerTables.Fields.GetTypeSpecRef(addressFieldRow),
                out var addressPointeeTypeSpec), Is.True);
        var targetTypeSpec
            = providerTables.TypeSpecs.GetTypeSpecRef(
                addressPointeeTypeSpec);
        Assert.That(targetTypeSpec.Form, Is.EqualTo((byte)0x02));
        Assert.That(targetTypeSpec.Row,
            Is.EqualTo(targetDefinition & 0x00FF_FFFF));
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "ClassPointerProvider", [], ["type.box"], [
                "field.box.prefix",
                "field.box.address",
            ]);
        var targetReference = reference.StandardClassDeclarations.Single(
            static declaration =>
                declaration.Declaration.Name == "Target");
        Assert.That(targetReference.SelectedExportKeys, Is.Empty);

        var pointerTraversalCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Address.Value));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(pointerTraversalCompilation.HasErrors, Is.True);

        var queryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Box, Address));
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(queryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                queryCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            queryCompilation.Program!, "ClassPointerConsumer",
            schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassPointerConsumer", [providerInput]);
        var loadedReturn = (BoundReturn)loaded.Functions.Single().Body
            .Statements.Single();
        var loadedQuery = (BoundLayoutQuery)((BoundUnsafeExpression)
            loadedReturn.Value!).Expression;
        var loadedPointerType = loadedQuery.Subject.FieldPath!
            .Steps.Single().FieldType;
        Assert.That(NativePointerTypes.IsType(loadedPointerType), Is.True);
        var loadedPointeeType = loaded.NativePointerTypes[
            NativePointerTypes.Index(loadedPointerType)].PointeeType;
        Assert.That(BoundGenericNominalTypeTypes.IsType(loadedPointeeType),
            Is.True);
        var targetIdentity = loaded.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(loadedPointeeType)]
            .Definition;
        Assert.That(targetIdentity.DefinitionId,
            Is.EqualTo(targetDefinition));

        var main = loaded.Functions.Single() with {
            Name = "Main",
            ReturnType = PrimitiveType.Int32,
            Body = new BoundBlock([
                new BoundReturn(new BoundCast(loadedReturn.Value!,
                    PrimitiveType.Int32, default), default),
            ], default),
        };
        var executable = loaded with {
            Functions = [main],
            EntryPoint = 0,
        };
        Assert.That(NativeJit.Execute(executable,
            NativeRuntimePolicy.FailFast), Is.EqualTo(8));
        Assert.That(NativeEmitter.EmitLibrary(loaded, ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "ClassPointerConsumerReemitted", schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "ClassPointerConsumerReemitted", [providerInput]);
        var reloadedReturn = (BoundReturn)reloaded.Functions.Single().Body
            .Statements.Single();
        var reloadedQuery = (BoundLayoutQuery)((BoundUnsafeExpression)
            reloadedReturn.Value!).Expression;
        var reloadedPointerType = reloadedQuery.Subject.FieldPath!
            .Steps.Single().FieldType;
        var reloadedPointeeType = reloaded.NativePointerTypes[
            NativePointerTypes.Index(reloadedPointerType)].PointeeType;
        Assert.That(BoundGenericNominalTypeTypes.IsType(reloadedPointeeType),
            Is.True);
        Assert.That(reloaded.GenericNominalTypeInstances[
                BoundGenericNominalTypeTypes.Index(reloadedPointeeType)]
            .Definition, Is.EqualTo(targetIdentity));

        var relocatedProviderTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Padding {
                    public int32 Value;
                    public Padding(int32 value) { Value = value; }
                }
                public class Target {
                    public int32 Value;
                    public Target(int32 value) { Value = value; }
                }
                public class Box {
                    public uint8 Prefix;
                    public ptr<Target> Address;
                    public Box(uint8 prefix, ptr<Target> address) {
                        Prefix = prefix;
                        Address = address;
                    }
                }
            }
            """);
        var relocatedProviderCompilation
            = Compilation.CreateLibrary(relocatedProviderTree);
        Assert.That(relocatedProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                relocatedProviderCompilation.Diagnostics));
        var relocatedProviderProgram
            = (BoundProgram)relocatedProviderCompilation.Program!;
        var relocatedProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            relocatedProviderProgram, "ClassPointerProvider",
            schemaIdentity, 1, [], [], [
                CreateSourceClassExport(relocatedProviderProgram,
                    relocatedProviderTree, "Padding", "type.padding",
                    "constructor.padding"),
                CreateSourceClassExport(relocatedProviderProgram,
                    relocatedProviderTree, "Target", "type.target",
                    "constructor.target"),
                CreateSourceClassExport(relocatedProviderProgram,
                    relocatedProviderTree, "Box", "type.box",
                    "constructor.box",
                    (0, "field.box.prefix"),
                    (1, "field.box.address")),
            ], []);
        var relocatedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, relocatedProviderArtifact);
        var relocatedTargetDefinition
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(relocatedProviderArtifact,
                        SchemaFor(schemaIdentity)))
                .Exports.GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "type.target").Definition;
        Assert.That(relocatedTargetDefinition,
            Is.Not.EqualTo(targetDefinition));

        var relocatedLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassPointerConsumer", [relocatedProviderInput]);
        var relocatedReturn = (BoundReturn)relocatedLoaded.Functions
            .Single().Body.Statements.Single();
        var relocatedMain = relocatedLoaded.Functions.Single() with {
            Name = "Main",
            ReturnType = PrimitiveType.Int32,
            Body = new BoundBlock([
                new BoundReturn(new BoundCast(relocatedReturn.Value!,
                    PrimitiveType.Int32, default), default),
            ], default),
        };
        Assert.That(NativeJit.Execute(relocatedLoaded with {
                Functions = [relocatedMain],
                EntryPoint = 0,
            }, NativeRuntimePolicy.FailFast), Is.EqualTo(8));
        var relocatedQuery = (BoundLayoutQuery)((BoundUnsafeExpression)
            relocatedReturn.Value!).Expression;
        var relocatedPointerType = relocatedQuery.Subject.FieldPath!
            .Steps.Single().FieldType;
        Assert.That(NativePointerTypes.IsType(relocatedPointerType), Is.True);
        var relocatedPointeeType = relocatedLoaded.NativePointerTypes[
            NativePointerTypes.Index(relocatedPointerType)].PointeeType;
        var relocatedTargetIdentity
            = relocatedLoaded.GenericNominalTypeInstances[
                BoundGenericNominalTypeTypes.Index(relocatedPointeeType)]
                .Definition;
        Assert.That(relocatedTargetIdentity.DefinitionId,
            Is.EqualTo(relocatedTargetDefinition));

        var relocatedReemitted = TrRmExecutableCompiler.EmitStandard(
            relocatedLoaded, "ClassPointerConsumerReemitted",
            schemaIdentity, 1);
        var relocatedReloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                relocatedReemitted), "ClassPointerConsumerReemitted",
            [relocatedProviderInput]);
        Assert.That(NativeEmitter.EmitLibrary(relocatedReloaded, ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
        Assert.That(NativeModuleCompiler.EmitStandardLibrary(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassPointerConsumer", [relocatedProviderInput], ["Offset"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void ReemitsEN1ClassTypeDefAndFieldImportsAndTypeSpecs()
    {
        var schemaIdentity = s_schemaIdentities[0];
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Marker : int32 { Value = 1 }
                public class Shelf {
                    public int32 Id;
                    public Shelf(int32 id) { Id = id; }
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerClassDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.ClassDeclaration);
        var providerField = providerClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.FieldDeclaration);
        var providerClassIndex = providerProgram
            .SourceClassProgramAssociation!.Declarations.Single(
                declaration => declaration.Span
                    == providerClassDeclaration.Span).ClassIndex;
        var providerClassExport = new TrRmSourceClassExport(
            providerProgram, providerClassIndex,
            providerClassDeclaration.Span, "type.shelf", [
                new TrRmSourceClassMemberExport(0, providerField.Span,
                    "field.shelf.id"),
                CreateSourceClassConstructorExport(
                    providerClassDeclaration, "constructor.shelf"),
            ]);
        var providerEnumExport = CreateExport(providerProgram,
            providerTree, 0, "Marker", ["type.marker"], "marker");
        var providerArtifactWithoutFieldAlias
            = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ShelfProvider", schemaIdentity, 1,
            [], [], [providerClassExport], [providerEnumExport]);
        var providerArtifact = AddExportAlias(
            providerArtifactWithoutFieldAlias, schemaIdentity,
            "field.shelf.id", "field.shelf.id.alias");
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                providerArtifact, SchemaFor(schemaIdentity)));
        Assert.That(providerTables.Exports.TryGetDefinitionForKey(1,
            "type.shelf", out var providerTypeDefinition), Is.True);
        var providerTypeDefRow = checked((int)(providerTypeDefinition
            & 0x00FF_FFFF));

        var consumerTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 { Dark = -1 }
                public class Palette {
                    public Shelf ShelfValue;
                    public Shade ShadeValue;
                    public Palette(Shade shade) {
                        ShadeValue = shade;
                    }
                }
                public class Shelf {
                    public int32 Id;
                    public Shelf(int32 id) { Id = id; }
                }
            }
            """);
        var consumerCompilation = Compilation.CreateLibrary(consumerTree);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = (BoundProgram)consumerCompilation.Program!;
        var consumerClassDeclaration = consumerTree.Root.Children
            .OfType<SyntaxNode>()
            .First(static node => node.Kind == SyntaxKind.ClassDeclaration);
        var consumerFields = consumerClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.FieldDeclaration)
            .ToArray();
        var consumerClassIndex = consumerProgram
            .SourceClassProgramAssociation!.Declarations.Single(
                declaration => declaration.Span
                    == consumerClassDeclaration.Span).ClassIndex;
        var consumerClassExport = new TrRmSourceClassExport(
            consumerProgram, consumerClassIndex,
            consumerClassDeclaration.Span, "type.palette", [
                new TrRmSourceClassMemberExport(0,
                    consumerFields[0].Span, "field.palette.shelf"),
                new TrRmSourceClassMemberExport(0,
                    consumerFields[1].Span, "field.palette.shade"),
                CreateSourceClassConstructorExport(
                    consumerClassDeclaration, "constructor.palette"),
            ]);
        var consumerEnumExport = CreateExport(consumerProgram,
            consumerTree, 0, "Shade", ["type.shade"], "shade");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "PaletteConsumer", schemaIdentity, 1,
            [], [], [consumerClassExport], [consumerEnumExport]);
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, SchemaFor(schemaIdentity)));
        var expectedShelfTypeDefRow = Enumerable.Range(1,
                consumerTables.TypeDefs.Count)
            .Single(row => consumerTables.TypeDefs.GetName(row) == "Shelf");
        var (shelfFieldStart, _) = consumerTables.TypeDefs
            .GetFieldRange(expectedShelfTypeDefRow);
        var shelfFieldRow = Enumerable.Range(1, consumerTables.Fields.Count)
            .Single(row => consumerTables.Fields.GetName(row)
                == "ShelfValue");
        consumerArtifact = AddTypeDefImport(consumerArtifact,
            schemaIdentity, "ShelfProvider", "type.shelf",
            0x1300_0000u | checked((uint)expectedShelfTypeDefRow),
            "field.shelf.id",
            0x1400_0000u | checked((uint)shelfFieldStart),
            "field.shelf.id.alias");
        consumerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, SchemaFor(schemaIdentity)));
        Assert.That(consumerTables.Imports.Count, Is.EqualTo(3));
        consumerArtifact = RewriteFieldTypeSpecAsImport(consumerArtifact,
            schemaIdentity, shelfFieldRow, 1);

        var providers = new[] {
            new TrRmStandardArtifactInput(schemaIdentity, providerArtifact),
        };
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "PaletteConsumer", providers, ["type.palette"]);
        Assert.That(reference.StandardClassDeclarations, Has.Length.EqualTo(2));
        var unselectedPalette = reference.StandardClassDeclarations.Single(
            static declaration =>
                !declaration.SelectedExportKeys.IsEmpty);
        Assert.That(unselectedPalette.Declaration.Fields.Single(
            static field => field.Name == "ShelfValue").SelectedExportKeys,
            Is.Empty);
        var importedShelf = reference.StandardClassDeclarations.Single(
            declaration => declaration.Declaration.Name == "Shelf"
                && !declaration.Expectations.IsEmpty);
        Assert.That(importedShelf.Expectations, Has.Length.EqualTo(1));
        Assert.That(importedShelf.Expectations[0].FieldExpectations,
            Has.Length.EqualTo(2));
        Assert.That(importedShelf.Expectations[0].FieldExpectations[0]
            .ExportKey, Is.EqualTo("field.shelf.id"));
        Assert.That(importedShelf.Expectations[0].FieldExpectations.Select(
                static field => field.ExportKey),
            Is.EquivalentTo([
                "field.shelf.id", "field.shelf.id.alias",
            ]));
        Assert.That(importedShelf.Expectations[0].FieldExpectations.Select(
                static field => field.ProviderDefinitionId).Distinct()
                .Count(), Is.EqualTo(1));

        var splitAliasProvider = AddExportAlias(
            providerArtifactWithoutFieldAlias, schemaIdentity,
            "type.shelf", "field.shelf.id.alias");
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact),
                "PaletteConsumer", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        splitAliasProvider),
                ], ["type.palette"]));

        var fieldSelectedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact),
                "PaletteConsumer", providers, ["type.palette"],
                ["field.palette.shelf"]);
        var selectedPalette = fieldSelectedReference
            .StandardClassDeclarations.Single(
                static declaration =>
                    !declaration.SelectedExportKeys.IsEmpty);
        Assert.That(selectedPalette.Declaration.Fields.Single(
            static field => field.Name == "ShelfValue").SelectedExportKeys,
            Is.EqualTo(["field.palette.shelf"]));
        var classHandleQuery = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Demo;
                nint Offset() {
                    return unsafe(offsetof(Palette, ShelfValue));
                }
                """), fieldSelectedReference, new CompilationOptions {
                LayoutCoreRegistration
                    = LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(classHandleQuery.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                classHandleQuery.Diagnostics));
        BoundLayoutQuery? classHandleOffset = null;
        var classHandleFunction = classHandleQuery.Program!.Functions
            .Single();
        Assert.That(BoundTreeTraversal.VisitExpressions(
            classHandleFunction.Body,
            expression => classHandleOffset ??=
                expression as BoundLayoutQuery), Is.True);
        var classHandleFieldType
            = classHandleOffset!.Subject.FieldPath!.Steps.Single()
                .FieldType;
        Assert.That(BoundGenericNominalTypeTypes.IsType(
            classHandleFieldType), Is.True);
        var classHandleTypeInstance = fieldSelectedReference
            .GenericNominalTypeInstances[
                BoundGenericNominalTypeTypes.Index(classHandleFieldType)];
        Assert.That(classHandleTypeInstance.Definition.ProviderIdentity,
            Is.EqualTo("ShelfProvider"));
        Assert.That(classHandleTypeInstance.TypeSpecProviderIdentity,
            Is.EqualTo("PaletteConsumer"));
        Assert.That(classHandleTypeInstance.TypeSpecReference, Is.GreaterThan(0));
        Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact),
                "PaletteConsumer", providers, ["type.palette"],
                ["field.shelf.id"]));

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            sourceFreeCompilation.Program!, "PaletteConsumer",
            schemaIdentity, 1);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                reemitted, SchemaFor(schemaIdentity)));
        Assert.That(reemittedTables.Imports.Count, Is.EqualTo(2));
        var reemittedShelfTypeDefRow = Enumerable.Range(1,
                reemittedTables.TypeDefs.Count)
            .Single(row => reemittedTables.TypeDefs.GetName(row) == "Shelf");
        Assert.That(reemittedTables.Imports
            .GetExpectedDefinitionForImportRow(1),
            Is.EqualTo(0x1300_0000u
                | checked((uint)reemittedShelfTypeDefRow)));
        var (reemittedShelfFieldStart, _)
            = reemittedTables.TypeDefs.GetFieldRange(
                reemittedShelfTypeDefRow);
        Assert.That(reemittedTables.Imports
            .GetExpectedDefinitionForImportRow(2),
            Is.EqualTo(0x1400_0000u
                | checked((uint)reemittedShelfFieldStart)));
        var reemittedShelfFieldRow = Enumerable.Range(1,
                reemittedTables.Fields.Count)
            .Single(row => reemittedTables.Fields.GetName(row)
                == "ShelfValue");
        var shelfType = reemittedTables.TypeSpecs.GetTypeSpecRef(
            reemittedTables.Fields.GetTypeSpecRef(
                reemittedShelfFieldRow));
        Assert.That(shelfType.Form, Is.EqualTo((byte)0x03));
        Assert.That(shelfType.Qualifiers, Is.Zero);
        Assert.That(shelfType.Row, Is.EqualTo(1));

        var reloadedReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "PaletteConsumer", providers, ["type.palette"]);
        Assert.That(reloadedReference.StandardClassDeclarations,
            Has.Length.EqualTo(2));
        Assert.That(reloadedReference.StandardClassDeclarations.Any(
            declaration => declaration.Declaration.Identity.DefinitionId
                == (0x1300_0000u
                    | checked((uint)providerTypeDefRow))), Is.True);

        var incompatibleProviderTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Shelf {
                    public int32 Id;
                    public Shelf(int32 id) { Id = id; }
                }
            }
            """);
        var incompatibleProviderCompilation
            = Compilation.CreateLibrary(incompatibleProviderTree);
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderProgram
            = (BoundProgram)incompatibleProviderCompilation.Program!;
        var incompatibleClassDeclaration
            = incompatibleProviderTree.Root.Children
                .OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.ClassDeclaration);
        var incompatibleField = incompatibleClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var incompatibleClassIndex = incompatibleProviderProgram
            .SourceClassProgramAssociation!.Declarations.Single(
                declaration => declaration.Span
                    == incompatibleClassDeclaration.Span).ClassIndex;
        var incompatibleClassExport = new TrRmSourceClassExport(
            incompatibleProviderProgram, incompatibleClassIndex,
            incompatibleClassDeclaration.Span, "type.shelf", [
                new TrRmSourceClassMemberExport(0,
                    incompatibleField.Span, "field.shelf.other"),
                CreateSourceClassConstructorExport(
                    incompatibleClassDeclaration, "constructor.shelf"),
            ]);
        var incompatibleProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleProviderProgram, "ShelfProvider",
                schemaIdentity, 1, [], [], [incompatibleClassExport], []);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact),
                "PaletteConsumer", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProviderArtifact),
                ], ["type.palette"]));
    }

    [Test]
    public void ReemitsImportedClassHandleStructFieldsWithoutCopyOwnership()
    {
        var schemaIdentity = s_schemaIdentities[0];
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Shelf {
                    public int32 Id;
                    public Shelf(int32 id) { Id = id; }
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerClassDeclaration = providerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.ClassDeclaration);
        var providerField = providerClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.FieldDeclaration);
        var providerClassIndex = providerProgram
            .SourceClassProgramAssociation!.Declarations.Single(
                declaration => declaration.Span
                    == providerClassDeclaration.Span).ClassIndex;
        var providerClassExport = new TrRmSourceClassExport(
            providerProgram, providerClassIndex,
            providerClassDeclaration.Span, "type.shelf", [
                new TrRmSourceClassMemberExport(0, providerField.Span,
                    "field.shelf.id"),
                CreateSourceClassConstructorExport(
                    providerClassDeclaration, "constructor.shelf"),
            ]);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ShelfProvider", schemaIdentity, 1,
            [], [], [providerClassExport], []);
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                providerArtifact, SchemaFor(schemaIdentity)));
        Assert.That(providerTables.Exports.TryGetDefinitionForKey(1,
            "type.shelf", out var providerTypeDefinition), Is.True);
        var providerTypeDefRow = checked((int)(providerTypeDefinition
            & 0x00FF_FFFF));

        var consumerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Palette {
                    public Shelf ShelfValue;
                    public Palette(Shelf shelf) { }
                }
                public class Shelf {
                    public int32 Id;
                    public Shelf(int32 id) { Id = id; }
                }
                public class LocalShelf {
                    public int32 Value;
                    public LocalShelf(int32 value) { Value = value; }
                }
                public struct Payload {
                    public int64 LocalValue;
                    public uint64 ImportedValue;
                }
                public struct Packet { public Payload PayloadValue; }
            }
            """);
        var consumerCompilation = Compilation.CreateLibrary(consumerTree);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = (BoundProgram)consumerCompilation.Program!;
        var consumerClassDeclaration = consumerTree.Root.Children
            .OfType<SyntaxNode>()
            .First(static node => node.Kind == SyntaxKind.ClassDeclaration);
        var consumerClassField = consumerClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.FieldDeclaration);
        var consumerClassIndex = consumerProgram
            .SourceClassProgramAssociation!.Declarations.Single(
                declaration => declaration.Span
                    == consumerClassDeclaration.Span).ClassIndex;
        var consumerClassExport = new TrRmSourceClassExport(
            consumerProgram, consumerClassIndex,
            consumerClassDeclaration.Span, "type.palette", [
                new TrRmSourceClassMemberExport(0,
                    consumerClassField.Span, "field.palette.shelf"),
                CreateSourceClassConstructorExport(
                    consumerClassDeclaration, "constructor.palette"),
            ]);
        var localClassDeclaration = consumerTree.Root.Children
            .OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.ClassDeclaration)
            .Last();
        var localClassField = localClassDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.FieldDeclaration);
        var localClassIndex = consumerProgram
            .SourceClassProgramAssociation!.Declarations.Single(
                declaration => declaration.Span
                    == localClassDeclaration.Span).ClassIndex;
        var localClassExport = new TrRmSourceClassExport(
            consumerProgram, localClassIndex,
            localClassDeclaration.Span, "type.local-shelf", [
                new TrRmSourceClassMemberExport(0, localClassField.Span,
                    "field.local-shelf.value"),
                CreateSourceClassConstructorExport(
                    localClassDeclaration, "constructor.local-shelf"),
            ]);
        var packetExport = new TrRmSourceTypeExport("Demo", "Packet",
            "type.packet") {
            FieldExports = [
                new TrRmSourceFieldExport("PayloadValue",
                    "field.packet.payload"),
            ],
        };
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "ClassHandleConsumer", schemaIdentity, 1,
            [packetExport], [], [consumerClassExport, localClassExport], []);
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, SchemaFor(schemaIdentity)));
        var consumerShelfTypeDefRow = Enumerable.Range(1,
                consumerTables.TypeDefs.Count)
            .Single(row => consumerTables.TypeDefs.GetName(row) == "Shelf");
        var localShelfTypeDefRow = Enumerable.Range(1,
                consumerTables.TypeDefs.Count)
            .Single(row => consumerTables.TypeDefs.GetName(row)
                == "LocalShelf");
        var paletteFieldRow = Enumerable.Range(1,
                consumerTables.Fields.Count)
            .Single(row => consumerTables.Fields.GetName(row)
                == "ShelfValue");
        var localPayloadFieldRow = Enumerable.Range(1,
                consumerTables.Fields.Count)
            .Single(row => consumerTables.Fields.GetName(row)
                == "LocalValue");
        var importedPayloadFieldRow = Enumerable.Range(1,
                consumerTables.Fields.Count)
            .Single(row => consumerTables.Fields.GetName(row)
                == "ImportedValue");
        consumerArtifact = AddTypeDefImport(consumerArtifact,
            schemaIdentity, "ShelfProvider", "type.shelf",
            0x1300_0000u | checked((uint)consumerShelfTypeDefRow));
        consumerArtifact = RewriteFieldTypeSpecAsImport(consumerArtifact,
            schemaIdentity, paletteFieldRow, 1);
        consumerArtifact = RewriteFieldTypeSpecAsImport(consumerArtifact,
            schemaIdentity, importedPayloadFieldRow, 1);
        consumerArtifact = RewriteFieldTypeSpecAsTypeDef(consumerArtifact,
            schemaIdentity, localPayloadFieldRow, localShelfTypeDefRow);
        consumerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, SchemaFor(schemaIdentity)));

        var providers = new[] {
            new TrRmStandardArtifactInput(schemaIdentity, providerArtifact),
        };
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "ClassHandleConsumer", providers, ["type.packet"]);
        Assert.That(reference.Structures, Has.Length.EqualTo(2));
        var packet = reference.Structures.Single(
            static structure => structure.Name == "Demo.Packet");
        var payload = reference.Structures.Single(
            static structure => structure.Name == "Demo.Payload");
        Assert.That(BoundGenericNominalTypeTypes.IsType(
            payload.Fields[0].Type), Is.True);
        Assert.That(BoundGenericNominalTypeTypes.IsType(
            payload.Fields[1].Type), Is.True);
        var classHandle = reference.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(payload.Fields[0].Type)];
        var importedClassHandle = reference.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(payload.Fields[1].Type)];
        Assert.That(classHandle.TypeSpecForm, Is.EqualTo((byte)0x02));
        Assert.That(importedClassHandle.TypeSpecForm,
            Is.EqualTo((byte)0x03));
        Assert.That(classHandle.TypeSpecProviderIdentity,
            Is.EqualTo("ClassHandleConsumer"));
        Assert.That(classHandle.TypeSpecReference,
            Is.EqualTo(consumerTables.Fields.GetTypeSpecRef(
                localPayloadFieldRow)));
        Assert.That(classHandle.Definition.DefinitionId,
            Is.EqualTo(0x1300_0000u
                | checked((uint)localShelfTypeDefRow)));
        Assert.That(importedClassHandle.TypeSpecReference,
            Is.EqualTo(consumerTables.Fields.GetTypeSpecRef(
                importedPayloadFieldRow)));
        Assert.That(importedClassHandle.Definition.DefinitionId,
            Is.EqualTo(0x1300_0000u | checked((uint)providerTypeDefRow)));
        Assert.That((int)packet.Fields.Single().Type, Is.GreaterThanOrEqualTo(100));
        Assert.That((int)packet.Fields.Single().Type, Is.LessThan(ManagedClassTypes.First));

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            sourceFreeCompilation.Program!, "ClassHandleConsumer",
            schemaIdentity, 1);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                reemitted, SchemaFor(schemaIdentity)));
        Assert.That(reemittedTables.Imports.Count, Is.EqualTo(1));
        var reemittedPayloadRow = Enumerable.Range(1,
                reemittedTables.TypeDefs.Count)
            .Single(row => reemittedTables.TypeDefs.GetName(row) == "Payload");
        var (reemittedPayloadFieldStart, reemittedPayloadFieldEnd)
            = reemittedTables.TypeDefs
            .GetFieldRange(reemittedPayloadRow);
        var reemittedLocalFieldRow = Enumerable.Range(
                reemittedPayloadFieldStart, reemittedPayloadFieldEnd
                    - reemittedPayloadFieldStart)
            .Single(row => reemittedTables.Fields.GetName(row)
                == "LocalValue");
        var reemittedImportedFieldRow = Enumerable.Range(
                reemittedPayloadFieldStart, reemittedPayloadFieldEnd
                    - reemittedPayloadFieldStart)
            .Single(row => reemittedTables.Fields.GetName(row)
                == "ImportedValue");
        var reemittedLocalFieldType
            = reemittedTables.TypeSpecs.GetTypeSpecRef(
            reemittedTables.Fields.GetTypeSpecRef(
                reemittedLocalFieldRow));
        var reemittedImportedFieldType
            = reemittedTables.TypeSpecs.GetTypeSpecRef(
                reemittedTables.Fields.GetTypeSpecRef(
                    reemittedImportedFieldRow));
        Assert.That(reemittedLocalFieldType.Form, Is.EqualTo((byte)0x02));
        Assert.That(reemittedLocalFieldType.Row,
            Is.EqualTo(checked((uint)Enumerable.Range(1,
                    reemittedTables.TypeDefs.Count)
                .Single(row => reemittedTables.TypeDefs.GetName(row)
                    == "LocalShelf"))));
        Assert.That(reemittedImportedFieldType.Form,
            Is.EqualTo((byte)0x03));
        var reemittedShelfRow = Enumerable.Range(1,
                reemittedTables.TypeDefs.Count)
            .Single(row => reemittedTables.TypeDefs.GetName(row) == "Shelf");
        Assert.That(reemittedTables.Imports
            .GetExpectedDefinitionForImportRow(
                checked((int)reemittedImportedFieldType.Row)),
            Is.EqualTo(0x1300_0000u | checked((uint)reemittedShelfRow)));

        var reloadedReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "ClassHandleConsumer", providers, ["type.packet"]);
        Assert.That(reloadedReference.StandardClassDeclarations.Any(
            declaration => declaration.Declaration.Identity.DefinitionId
                == (0x1300_0000u | checked((uint)providerTypeDefRow))),
            Is.True);
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reloadedReference);
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));

        var ownershipSensitiveUse = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                namespace Demo {
                    public class Receiver {
                        public Receiver(Packet value) { }
                    }
                }
                """), reloadedReference);
        Assert.That(ownershipSensitiveUse.HasErrors,
            Is.EqualTo(!SchemaFor(schemaIdentity)
                .UsesConstructionInitializerCopyEvidence()));
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
        consumerArtifact = AddTypeDefImport(consumerArtifact,
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
            AddTypeDefImport(proxyConsumerArtifact, schemaIdentity,
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

    private static TrRmSourceClassMemberExport
        CreateSourceClassConstructorExport(SyntaxNode declaration,
            string exportKey)
    {
        var constructor = declaration.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ConstructorDeclaration);
        return new TrRmSourceClassMemberExport(0, constructor.Span,
            exportKey);
    }

    private static TrRmSourceClassExport CreateSourceClassExport(
        BoundProgram program, SyntaxTree tree, string name, string exportKey,
        string constructorExportKey,
        params (int FieldOrdinal, string ExportKey)[] fieldExports)
    {
        var declaration = tree.Root.DescendantNodes().Single(node =>
            node.Kind == SyntaxKind.ClassDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                token.Token.Kind == TokenKind.Identifier
                && token.Token.Name == name));
        var classIndex = program.SourceClassProgramAssociation!
            .Declarations.Single(item => item.Span == declaration.Span)
            .ClassIndex;
        var fields = declaration.Children.OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.FieldDeclaration)
            .ToArray();
        var memberExports = fieldExports.Select(fieldExport => {
            if ((uint)fieldExport.FieldOrdinal >= (uint)fields.Length)
            {
                throw new InvalidDataException(
                    "A class-export fixture field ordinal is invalid.");
            }

            return new TrRmSourceClassMemberExport(0,
                fields[fieldExport.FieldOrdinal].Span,
                fieldExport.ExportKey);
        }).ToImmutableArray()
            .Add(CreateSourceClassConstructorExport(declaration,
                constructorExportKey));
        return new TrRmSourceClassExport(program, classIndex,
            declaration.Span, exportKey, memberExports);
    }

    private static TrRmSourceClassExport
        CreateSourceClassExportWithProjection(
            BoundProgram program, SyntaxTree tree, string name,
            string exportKey, string constructorExportKey,
            string projectionExportKey,
            params (int FieldOrdinal, string ExportKey)[] fieldExports)
    {
        var export = CreateSourceClassExport(program, tree, name, exportKey,
            constructorExportKey, fieldExports);
        var constructorExportIndex = export.MemberExports.Length - 1;
        var constructorExport = export.MemberExports[
            constructorExportIndex] with {
            ConstructionProjectionExportKey = projectionExportKey,
        };
        return export with {
            MemberExports = export.MemberExports.SetItem(
                constructorExportIndex, constructorExport),
        };
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

    private static byte[] AddTypeDefImport(byte[] artifact,
        string schemaIdentity, string providerIdentity, string exportKey,
        uint expectedDefinitionId, string? fieldImportExportKey = null,
        uint? expectedFieldDefinitionId = null,
        string? fieldImportExportAliasKey = null)
    {
        if ((fieldImportExportKey is null)
            != (expectedFieldDefinitionId is null)
            || (fieldImportExportAliasKey is not null
                && expectedFieldDefinitionId is null)
            || (expectedFieldDefinitionId is uint fieldDefinition
                && ((fieldDefinition >> 24) != 0x14
                    || (fieldDefinition & 0x00FF_FFFF) == 0)))
        {
            throw new ArgumentException(
                "The optional FieldDef Import must identify one expected field.");
        }

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
                    "The source artifact has no String table.");
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
        var fieldImportExportKeyRow = fieldImportExportKey is null
            ? 0 : AddString(fieldImportExportKey);
        var fieldImportExportAliasRow = fieldImportExportAliasKey is null
            ? 0 : AddString(fieldImportExportAliasKey);
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(textRows);
        var strings = new TrRmStringTableReader(stringBytes,
            textRows.Count);
        if (!index.TryGetSection(0x0001, out var moduleBytes,
                out var moduleCount))
        {
            throw new InvalidDataException(
                "The source artifact has no Module table.");
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
                "The source artifact has no TypeDef table.");
        }

        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            typeDefCount, strings, modules, tables.Fields.Count, 0,
            tables.Layouts.Count, 0, schema);
        if (!index.TryGetSection(0x0004, out var featureBytes,
                out var featureCount))
        {
            throw new InvalidDataException(
                "The source artifact has no Feature table.");
        }

        var features = new TrRmFeatureTableReader(featureBytes,
            featureCount, schema);
        var importRows = new List<TrRmImportRow> {
            new(1, exportKeyRow, expectedDefinitionId),
        };
        if (expectedFieldDefinitionId is uint fieldDefinitionId)
        {
            importRows.Add(new TrRmImportRow(1,
                fieldImportExportKeyRow, fieldDefinitionId));
            if (fieldImportExportAliasKey is not null)
            {
                importRows.Add(new TrRmImportRow(1,
                    fieldImportExportAliasRow, fieldDefinitionId));
            }
        }
        var importBytes = TrRmImportTableWriter.Encode(importRows, strings,
            moduleRefs, typeDefs, default, default, default, default, default,
            features, modules, schema);

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
        sections.Add(new TrRmSection(0x001C, 0,
            checked((uint)importRows.Count), importBytes));
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

    private static byte[] AssignTrailingClassTypeDefsToModules(
        byte[] artifact, string schemaIdentity,
        IReadOnlyList<(string ModuleIdentity, string TypeDefName)>
            dependencies, uint? dependencyTargetRequirement = null)
    {
        var schema = SchemaFor(schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        var sourceIdentity = tables.Modules.GetIdentity(1);
        var seenModuleIdentities = new HashSet<string>(
            StringComparer.Ordinal) {
            sourceIdentity,
        };
        if (tables.Modules.Count != 1 || dependencies.Count == 0
            || dependencies.Any(dependency =>
                string.IsNullOrEmpty(dependency.ModuleIdentity)
                || !seenModuleIdentities.Add(dependency.ModuleIdentity)))
        {
            throw new InvalidDataException(
                "The packed class fixture requires distinct dependency Modules.");
        }

        var typeDefRange = tables.Modules.GetTypeDefRange(1);
        var firstDependencyTypeDefRow
            = typeDefRange.End - dependencies.Count;
        if (typeDefRange.Start != 1
            || typeDefRange.End != tables.TypeDefs.Count + 1
            || firstDependencyTypeDefRow <= typeDefRange.Start)
        {
            throw new InvalidDataException(
                "The packed class fixture requires trailing dependency TypeDefs.");
        }

        for (var index = 0; index < dependencies.Count; index++)
        {
            var typeDefRow = firstDependencyTypeDefRow + index;
            if (!StringComparer.Ordinal.Equals(
                    tables.TypeDefs.GetName(typeDefRow),
                    dependencies[index].TypeDefName))
            {
                throw new InvalidDataException(
                    "The packed class fixture dependency TypeDefs are not in the expected trailing row order.");
            }
        }

        if (!index.TryGetSection(0x0002, out var originalStringBytes,
                out var originalStringCount))
        {
            throw new InvalidDataException(
                "The packed class fixture has no String table.");
        }

        var textRows = new List<(string Text, int Encoding)>(
            originalStringCount + dependencies.Count);
        var stringRowsByText = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var originalStrings = new TrRmStringTableReader(
            originalStringBytes, originalStringCount);
        for (var row = 1; row <= originalStringCount; row++)
        {
            var text = originalStrings.DecodeNamespace(row);
            textRows.Add((text, GetTextEncoding(text)));
            _ = stringRowsByText.TryAdd(text, row);
        }

        int AddString(string text)
        {
            if (!stringRowsByText.TryGetValue(text, out var row))
            {
                row = textRows.Count + 1;
                textRows.Add((text, GetTextEncoding(text)));
                stringRowsByText.Add(text, row);
            }

            return row;
        }

        var sourceIdentityRow = AddString(sourceIdentity);
        var dependencyIdentityRows = dependencies
            .Select(dependency => AddString(dependency.ModuleIdentity))
            .ToArray();
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(textRows);
        var strings = new TrRmStringTableReader(stringBytes,
            textRows.Count);
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(1);
        var (featureFirst, featureCount)
            = tables.Modules.GetFeatureRange(1);
        if ((featureCount == 0 && featureFirst != 0)
            || (featureCount != 0 && featureFirst != 1)
            || featureCount != tables.Features.Count)
        {
            throw new InvalidDataException(
                "The packed class fixture requires one complete source Feature range.");
        }

        var sourceFeatures = Enumerable.Range(1, tables.Features.Count)
            .Select(tables.Features.GetFeature).ToArray();
        var packedFeatures = new List<TrRmFeatureRow>(
            checked(sourceFeatures.Length * (dependencies.Count + 1)));
        for (var moduleIndex = 0;
            moduleIndex <= dependencies.Count; moduleIndex++)
        {
            packedFeatures.AddRange(sourceFeatures);
        }

        var featureBytes = TrRmFeatureTableWriter.EncodeModuleRanges(
            packedFeatures, schema);
        var moduleRefRange = tables.Modules.GetModuleRefRange(1);
        var methodDefRange = tables.Modules.GetMethodDefRange(1);
        var sourceFileRange = tables.Modules.GetSourceFileRange(1);
        var governingRuleEdition
            = tables.Modules.GetGoverningRuleEdition(1);
        var moduleRows = new List<TrRmModuleRow>(
            dependencies.Count + 1) {
            new(sourceIdentityRow, targetRequirement,
                runtimePolicy, tables.Modules.GetEntryMethod(1),
                moduleRefRange.Start, typeDefRange.Start,
                methodDefRange.Start, sourceFileRange.Start,
                featureCount == 0 ? 0 : 1, sourceFeatures.Length,
                governingRuleEdition),
        };
        for (var dependencyIndex = 0;
            dependencyIndex < dependencies.Count; dependencyIndex++)
        {
            var featureFirst = featureCount == 0
                ? 0
                : ((dependencyIndex + 1) * sourceFeatures.Length) + 1;
            moduleRows.Add(new TrRmModuleRow(
                dependencyIdentityRows[dependencyIndex],
                dependencyTargetRequirement ?? targetRequirement,
                runtimePolicy, 0, moduleRefRange.End,
                firstDependencyTypeDefRow + dependencyIndex,
                methodDefRange.End, sourceFileRange.End, featureFirst,
                sourceFeatures.Length, governingRuleEdition));
        }

        var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows, strings,
            tables.Modules.ModuleRefCount, tables.TypeDefs.Count,
            tables.Methods.Count, tables.Modules.SourceFileCount,
            packedFeatures.Count);

        var sections = new List<TrRmSection>(index.SectionCount);
        var replacedModule = false;
        var replacedStrings = false;
        var replacedFeatures = false;
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags,
                out var section, out var recordCount);
            switch (kind)
            {
                case 0x0001:
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)moduleRows.Count),
                        moduleBytes));
                    replacedModule = true;
                    break;
                case 0x0002:
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)textRows.Count), stringBytes));
                    replacedStrings = true;
                    break;
                case 0x0004:
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)packedFeatures.Count), featureBytes));
                    replacedFeatures = true;
                    break;
                default:
                    sections.Add(new TrRmSection(kind, flags, recordCount,
                        section.ToArray()));
                    break;
            }
        }

        if (!replacedModule || !replacedStrings || !replacedFeatures)
        {
            throw new InvalidDataException(
                "The packed class fixture is missing a required metadata section.");
        }

        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(result, schema));
        return result;

        static int GetTextEncoding(string text)
            => text.Any(static character => character > 0x7F) ? 1 : 0;
    }

    private static byte[] RewriteFieldTypeSpecAsImport(byte[] artifact,
        string schemaIdentity, int fieldRow, int importRow)
        => RewriteFieldTypeSpec(artifact, schemaIdentity, fieldRow, 0x03,
            importRow);

    private static byte[] RewriteFieldTypeSpecAsTypeDef(byte[] artifact,
        string schemaIdentity, int fieldRow, int typeDefRow)
        => RewriteFieldTypeSpec(artifact, schemaIdentity, fieldRow, 0x02,
            typeDefRow);

    private static byte[] RewriteFieldTypeSpec(byte[] artifact,
        string schemaIdentity, int fieldRow, byte form, int referenceRow)
    {
        var schema = SchemaFor(schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(index);
        var typeSpecReference = tables.Fields.GetTypeSpecRef(fieldRow);
        if (typeSpecReference is 0 or > 0x00FF_FFFF
            || form is not (0x02 or 0x03)
            || referenceRow <= 0
            || referenceRow > (form == 0x02
                ? tables.TypeDefs.Count : tables.Imports.Count))
        {
            throw new InvalidDataException(
                "The C1 field fixture has no valid row-based field type reference.");
        }
        var typeSpec = tables.TypeSpecs.GetTypeSpecRef(typeSpecReference);

        var typeSpecRows = new ReadOnlyMemory<byte>[tables.TypeSpecs.Count];
        for (var row = 1; row <= typeSpecRows.Length; row++)
        {
            typeSpecRows[row - 1] = row == typeSpecReference
                ? TrRmTypeSpecTableWriter.Reference(form,
                    checked((uint)referenceRow), typeSpec.Qualifiers)
                : tables.TypeSpecs.GetRow(row).ToArray();
        }

        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            tables.TypeDefs.Count, tables.Imports.Count, 0, 0, schema);
        var sections = new List<TrRmSection>(index.SectionCount);
        var replaced = false;
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind == 0x0011)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)typeSpecRows.Length), typeSpecBytes));
                replaced = true;
            }
            else
            {
                sections.Add(new TrRmSection(kind, flags, recordCount,
                    section.ToArray()));
            }
        }
        if (!replaced)
        {
            throw new InvalidDataException(
                "The C1 field fixture has no TypeSpec section.");
        }

        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(result, schema));
        return result;
    }

    private static byte[] AddExactClassLayoutVariant(byte[] artifact,
        TrRmArtifactSchema schema, byte layoutKind, uint pack,
        string typeName = "Box")
    {
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        var typeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == typeName);
        var (layoutFirst, layoutCount)
            = tables.TypeDefs.GetLayoutRange(typeDefRow);
        var (fieldStart, fieldEnd)
            = tables.TypeDefs.GetFieldRange(typeDefRow);
        if (layoutKind is not (1 or 2) || pack is not (1 or 2 or 4 or 8 or 16)
            || layoutCount != 1
            || layoutFirst + layoutCount != tables.Layouts.Count + 1
            || tables.LayoutFields.Count == 0)
        {
            throw new InvalidDataException(
                "The class layout variant fixture requires one supported pack and the final TypeDef's complete default Layout.");
        }

        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ]);
        var targetProfileRow = 1;
        var layoutRow = checked(tables.Layouts.Count + 1);
        var layoutFieldFirst = checked(tables.LayoutFields.Count + 1);
        var instanceFields = Enumerable.Range(fieldStart,
                fieldEnd - fieldStart)
            .Where(fieldRow =>
                (tables.Fields.GetFlags(fieldRow) & 8) == 0)
            .ToArray();
        if (instanceFields.Length == 0)
        {
            throw new InvalidDataException(
                "The sequential class fixture requires instance fields.");
        }

        var additionalLayout = new byte[28];
        System.Buffers.Binary.BinaryPrimitives
            .WriteUInt32LittleEndian(additionalLayout,
                checked((uint)typeDefRow));
        System.Buffers.Binary.BinaryPrimitives
            .WriteUInt32LittleEndian(additionalLayout.AsSpan(4),
                checked((uint)targetProfileRow));
        additionalLayout[8] = layoutKind;
        System.Buffers.Binary.BinaryPrimitives
            .WriteUInt32LittleEndian(additionalLayout.AsSpan(16), pack);
        System.Buffers.Binary.BinaryPrimitives
            .WriteUInt32LittleEndian(additionalLayout.AsSpan(20),
                checked((uint)layoutFieldFirst));
        System.Buffers.Binary.BinaryPrimitives
            .WriteUInt32LittleEndian(additionalLayout.AsSpan(24),
                checked((uint)instanceFields.Length));

        var additionalLayoutFields
            = new byte[checked(instanceFields.Length * 12)];
        for (var ordinal = 0; ordinal < instanceFields.Length; ordinal++)
        {
            var offset = ordinal * 12;
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt32LittleEndian(
                    additionalLayoutFields.AsSpan(offset),
                    checked((uint)layoutRow));
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt32LittleEndian(
                    additionalLayoutFields.AsSpan(offset + 4),
                    checked((uint)ordinal));
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt32LittleEndian(
                    additionalLayoutFields.AsSpan(offset + 8),
                    checked((uint)instanceFields[ordinal]));
        }

        var sections = new List<TrRmSection>(index.SectionCount);
        var hasTargetProfiles = false;
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind == 0x0013)
            {
                var typeDefBytes = section.ToArray();
                if (typeDefBytes.Length != recordCount * 44)
                {
                    throw new InvalidDataException(
                        "The sequential class fixture has an invalid TypeDef table.");
                }

                var layoutCountOffset = checked((typeDefRow - 1) * 44 + 32);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt32LittleEndian(
                        typeDefBytes.AsSpan(layoutCountOffset),
                        checked((uint)(layoutCount + 1)));
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), typeDefBytes));
            }
            else if (kind == 0x0015)
            {
                var layoutBytes = new byte[checked(section.Length + 28)];
                section.CopyTo(layoutBytes);
                additionalLayout.AsSpan().CopyTo(
                    layoutBytes.AsSpan(section.Length));
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount + 1u), layoutBytes));
            }
            else if (kind == 0x0023)
            {
                var layoutFieldBytes = new byte[checked(
                    section.Length + additionalLayoutFields.Length)];
                section.CopyTo(layoutFieldBytes);
                additionalLayoutFields.AsSpan().CopyTo(
                    layoutFieldBytes.AsSpan(section.Length));
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount
                        + (uint)instanceFields.Length),
                    layoutFieldBytes));
            }
            else if (kind == 0x0048)
            {
                if (recordCount != 0)
                {
                    throw new InvalidDataException(
                        "The sequential class fixture expects no existing target-profile rows.");
                }

                hasTargetProfiles = true;
                sections.Add(new TrRmSection(kind, flags, 1,
                    targetProfileBytes));
            }
            else
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
            }
        }
        if (!hasTargetProfiles)
        {
            sections.Add(new TrRmSection(0x0048, 0, 1,
                targetProfileBytes));
        }
        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));

        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(result, schema));
        return result;
    }

    private static byte[] AddExportAlias(byte[] artifact,
        string schemaIdentity, string existingKey, string aliasKey)
    {
        var schema = SchemaFor(schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        var definition = tables.Exports.GetExportsForModule(1)
            .Single(export => StringComparer.Ordinal.Equals(
                export.ExportKey, existingKey)).Definition;
        if (tables.Exports.GetExportsForModule(1).Any(export =>
                StringComparer.Ordinal.Equals(export.ExportKey, aliasKey)))
        {
            throw new ArgumentException(
                "The ExportKey alias already exists in the Module.",
                nameof(aliasKey));
        }

        if (!index.TryGetSection(0x0002, out var originalStringBytes,
                out var originalStringCount)
            || !index.TryGetSection(0x001D, out var originalExportBytes,
                out var originalExportCount))
        {
            throw new InvalidDataException(
                "The alias fixture requires String and Export tables.");
        }

        var originalStrings = new TrRmStringTableReader(
            originalStringBytes, originalStringCount);
        var stringRows = new List<(string Text, int Encoding)>(
            originalStrings.Count + 1);
        var stringRowsByText = new Dictionary<string, int>(
            StringComparer.Ordinal);
        for (var row = 1; row <= originalStrings.Count; row++)
        {
            var text = originalStrings.DecodeNamespace(row);
            stringRows.Add((text, GetTextEncoding(text)));
            _ = stringRowsByText.TryAdd(text, row);
        }

        if (stringRowsByText.ContainsKey(aliasKey))
        {
            throw new ArgumentException(
                "The ExportKey alias is already present in the String table.",
                nameof(aliasKey));
        }

        var aliasStringRow = stringRows.Count + 1;
        stringRows.Add((aliasKey, GetTextEncoding(aliasKey)));
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var exportBytes = new byte[checked(originalExportBytes.Length + 8)];
        originalExportBytes.CopyTo(exportBytes);
        System.Buffers.Binary.BinaryPrimitives
            .WriteUInt32LittleEndian(
                exportBytes.AsSpan(originalExportBytes.Length), definition);
        System.Buffers.Binary.BinaryPrimitives
            .WriteUInt32LittleEndian(
                exportBytes.AsSpan(originalExportBytes.Length + 4),
                checked((uint)aliasStringRow));

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind == 0x0002)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)stringRows.Count), stringBytes));
            }
            else if (kind == 0x001D)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)(originalExportCount + 1)),
                    exportBytes));
            }
            else
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
            }
        }

        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(result, schema));
        return result;

        static int GetTextEncoding(string text)
            => text.Any(static character => character > 0x7F) ? 1 : 0;
    }

    private static byte[] RewriteFieldImportExportKey(byte[] artifact,
        string schemaIdentity, string currentKey, string replacementKey)
    {
        var schema = SchemaFor(schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                artifact, schema, null, usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        var importRow = Enumerable.Range(1, tables.Imports.Count)
            .Single(row =>
                (tables.Imports.GetExpectedDefinitionForImportRow(row) >> 24)
                    == 0x14
                && StringComparer.Ordinal.Equals(
                    tables.Imports.GetBindingForImportRow(row).ExportKey,
                    currentKey));
        if (!index.TryGetSection(0x0002, out var originalStringBytes,
                out var originalStringCount)
            || !index.TryGetSection(0x001C, out var originalImportBytes,
                out var originalImportCount))
        {
            throw new InvalidDataException(
                "The FieldDef Import fixture requires String and Import tables.");
        }

        var originalStrings = new TrRmStringTableReader(
            originalStringBytes, originalStringCount);
        var stringRows = new List<(string Text, int Encoding)>(
            originalStrings.Count + 1);
        var stringRowsByText = new Dictionary<string, int>(
            StringComparer.Ordinal);
        for (var row = 1; row <= originalStrings.Count; row++)
        {
            var text = originalStrings.DecodeNamespace(row);
            stringRows.Add((text, GetTextEncoding(text)));
            _ = stringRowsByText.TryAdd(text, row);
        }

        if (!stringRowsByText.TryGetValue(replacementKey,
                out var replacementStringRow))
        {
            replacementStringRow = stringRows.Count + 1;
            stringRows.Add((replacementKey, GetTextEncoding(replacementKey)));
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var importBytes = originalImportBytes.ToArray();
        System.Buffers.Binary.BinaryPrimitives
            .WriteUInt32LittleEndian(importBytes.AsSpan(
                checked((importRow - 1) * 12 + 4)),
                checked((uint)replacementStringRow));
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind == 0x0002)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)stringRows.Count), stringBytes));
            }
            else if (kind == 0x001C)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)originalImportCount), importBytes));
            }
            else
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
            }
        }

        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                result, schema, null, usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        return result;

        static int GetTextEncoding(string text)
            => text.Any(static character => character > 0x7F) ? 1 : 0;
    }

    private static byte[] RewriteFieldImportProvider(byte[] artifact,
        string schemaIdentity, string fieldExportKey,
        byte[] replacementProviderArtifact)
    {
        var schema = SchemaFor(schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        var fieldImportRow = Enumerable.Range(1, tables.Imports.Count)
            .First(row =>
                (tables.Imports.GetExpectedDefinitionForImportRow(row) >> 24)
                    == 0x14
                && StringComparer.Ordinal.Equals(
                    tables.Imports.GetBindingForImportRow(row).ExportKey,
                    fieldExportKey));
        var replacementIndex = new TrRmArtifactIndex(
            replacementProviderArtifact, schema);
        var replacementTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(replacementIndex);
        if (replacementTables.Modules.Count != 1
            || tables.Modules.Count != 1
            || !index.TryGetSection(0x0001, out var moduleBytes,
                out var moduleCount)
            || !index.TryGetSection(0x0002, out var originalStringBytes,
                out var originalStringCount)
            || !index.TryGetSection(0x0010, out _,
                out var originalModuleRefCount)
            || !index.TryGetSection(0x001C, out var originalImportBytes,
                out var originalImportCount)
            || fieldImportRow > originalImportCount)
        {
            throw new InvalidDataException(
                "The FieldDef Import provider fixture has an unexpected table shape.");
        }

        var originalStrings = new TrRmStringTableReader(
            originalStringBytes, originalStringCount);
        var stringRows = new List<(string Text, int Encoding)>(
            originalStringCount + 1);
        var stringRowsByText = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var providerIdentityStringRow = 0;
        for (var row = 1; row <= originalStrings.Count; row++)
        {
            var text = originalStrings.DecodeNamespace(row);
            stringRows.Add((text, GetTextEncoding(text)));
            _ = stringRowsByText.TryAdd(text, row);
            if (StringComparer.Ordinal.Equals(text,
                    replacementTables.Modules.GetIdentity(1)))
            {
                providerIdentityStringRow = row;
            }
        }
        if (providerIdentityStringRow == 0)
        {
            providerIdentityStringRow = stringRows.Count + 1;
            stringRows.Add((replacementTables.Modules.GetIdentity(1),
                GetTextEncoding(replacementTables.Modules.GetIdentity(1))));
        }

        var rewrittenStrings = TrRmStringTableWriter.EncodeTextRows(
            stringRows);
        var stringTable = new TrRmStringTableReader(rewrittenStrings,
            stringRows.Count);
        var moduleRefRows = new List<TrRmModuleRefRow>(
            originalModuleRefCount + 1);
        for (var row = 1; row <= tables.ModuleRefs.Count; row++)
        {
            var (targetRequirement, runtimePolicy)
                = tables.ModuleRefs.GetRequirements(row);
            moduleRefRows.Add(new TrRmModuleRefRow(
                stringRowsByText[
                    tables.ModuleRefs.GetProducerIdentity(row)],
                targetRequirement, runtimePolicy));
        }
        var (replacementTarget, replacementPolicy)
            = replacementTables.Modules.GetRequirements(1);
        moduleRefRows.Add(new TrRmModuleRefRow(
            providerIdentityStringRow, replacementTarget, replacementPolicy));
        var rewrittenModules = new TrRmModuleTableReader(moduleBytes,
            checked((int)moduleCount), stringTable, moduleRefRows.Count,
            tables.TypeDefs.Count, tables.Methods.Count,
            tables.Modules.SourceFileCount, tables.Features.Count);
        var rewrittenModuleRefs = TrRmModuleRefTableWriter.Encode(
            moduleRefRows, stringTable, rewrittenModules);
        var rewrittenImports = originalImportBytes.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            rewrittenImports.AsSpan(checked((fieldImportRow - 1) * 12)),
            checked((uint)moduleRefRows.Count));
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var sectionBytes, out var recordCount);
            if (kind == 0x0002)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)stringRows.Count), rewrittenStrings));
            }
            else if (kind == 0x0010)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)moduleRefRows.Count),
                    rewrittenModuleRefs));
            }
            else if (kind == 0x001C)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)originalImportCount), rewrittenImports));
            }
            else
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), sectionBytes.ToArray()));
            }
        }

        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                result, schema));
        return result;

        static int GetTextEncoding(string text)
            => text.Any(static character => character > 0x7F) ? 1 : 0;
    }

    private static byte[] AsStaticClassField(byte[] artifact,
        TrRmArtifactSchema schema)
    {
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        if (tables.Fields.Count != 1 || tables.Layouts.Count != 1
            || tables.LayoutFields.Count != 1)
        {
            throw new InvalidDataException(
                "The static FieldDef fixture requires one class field and layout row.");
        }

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind == 0x0014)
            {
                var fieldBytes = section.ToArray();
                var fieldFlags
                    = System.Buffers.Binary.BinaryPrimitives
                        .ReadUInt32LittleEndian(fieldBytes.AsSpan(12));
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt32LittleEndian(fieldBytes.AsSpan(12),
                        fieldFlags | 8);
                sections.Add(new TrRmSection(kind, flags, recordCount,
                    fieldBytes));
            }
            else if (kind == 0x0015)
            {
                var layoutBytes = section.ToArray();
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt32LittleEndian(layoutBytes.AsSpan(20), 0);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt32LittleEndian(layoutBytes.AsSpan(24), 0);
                sections.Add(new TrRmSection(kind, flags, recordCount,
                    layoutBytes));
            }
            else if (kind != 0x0023)
            {
                sections.Add(new TrRmSection(kind, flags, recordCount,
                    section.ToArray()));
            }
        }

        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(result, schema));
        return result;
    }

    private static byte[] SwapTargetLayoutDependencyRows(byte[] artifact,
        string schemaIdentity, int firstRow, int secondRow)
    {
        var schema = SchemaFor(schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        if (firstRow <= 0 || firstRow > tables.TargetLayoutDependencies.Count
            || secondRow <= 0
            || secondRow > tables.TargetLayoutDependencies.Count
            || !index.TryGetSection(0x0045, out _, out _))
        {
            throw new ArgumentOutOfRangeException(nameof(firstRow));
        }

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind == 0x0045)
            {
                var bytes = section.ToArray();
                var firstOffset = (firstRow - 1) * 20;
                var secondOffset = (secondRow - 1) * 20;
                var first = bytes.AsSpan(firstOffset, 20).ToArray();
                var second = bytes.AsSpan(secondOffset, 20).ToArray();
                Array.Copy(second, 0, bytes, firstOffset, 20);
                Array.Copy(first, 0, bytes, secondOffset, 20);
                sections.Add(new TrRmSection(kind, flags, recordCount,
                    bytes));
            }
            else
            {
                sections.Add(new TrRmSection(kind, flags, recordCount,
                    section.ToArray()));
            }
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] RewriteEN1ClassQueryFieldReference(byte[] artifact,
        string schemaIdentity, int fieldPathRow,
        int targetLayoutDependencyRow, int ordinal, uint replacement)
    {
        var schema = SchemaFor(schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                artifact, schema, null, usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        if (fieldPathRow <= 0 || fieldPathRow > tables.FieldPaths.Count
            || targetLayoutDependencyRow <= 0
            || targetLayoutDependencyRow
                > tables.TargetLayoutDependencies.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldPathRow));
        }

        var pathFieldCount = tables.FieldPaths.GetFieldCount(fieldPathRow);
        var dependencyFieldRange = tables.TargetLayoutDependencies
            .GetFieldRange(targetLayoutDependencyRow);
        if (ordinal < 0 || ordinal >= pathFieldCount
            || ordinal >= dependencyFieldRange.Count
            || pathFieldCount != dependencyFieldRange.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        var originalFieldReference = tables.FieldPaths
            .GetFieldReference(fieldPathRow, ordinal);
        var dependencyFieldRow = dependencyFieldRange.First + ordinal;
        if (tables.TargetLayoutDependencyFields.GetFieldReference(
                dependencyFieldRow) != originalFieldReference)
        {
            throw new InvalidDataException(
                "The EN1 FieldPath and dependency fixtures disagree.");
        }

        var fieldPathRows = new TrRmFieldPathRow[tables.FieldPaths.Count];
        for (var row = 1; row <= fieldPathRows.Length; row++)
        {
            var fieldCount = tables.FieldPaths.GetFieldCount(row);
            var fieldReferences = new uint[fieldCount];
            for (var field = 0; field < fieldCount; field++)
            {
                fieldReferences[field] = tables.FieldPaths
                    .GetFieldReference(row, field);
            }

            if (row == fieldPathRow)
            {
                fieldReferences[ordinal] = replacement;
            }

            fieldPathRows[row - 1] = new TrRmFieldPathRow(
                tables.FieldPaths.GetRootTypeSpecRef(row),
                fieldReferences);
        }

        var targetLayoutDependencyFieldReferences
            = new uint[tables.TargetLayoutDependencyFields.Count];
        for (var row = 1;
            row <= targetLayoutDependencyFieldReferences.Length; row++)
        {
            targetLayoutDependencyFieldReferences[row - 1]
                = tables.TargetLayoutDependencyFields
                    .GetFieldReference(row);
        }

        targetLayoutDependencyFieldReferences[dependencyFieldRow - 1]
            = replacement;
        var rewrittenFieldPaths = TrRmFieldPathTableWriter.Encode(
            fieldPathRows, tables.TypeSpecs, tables.Fields, tables.Imports);
        var rewrittenDependencyFields
            = TrRmTargetLayoutDependencyFieldTableWriter.Encode(
                targetLayoutDependencyFieldReferences, tables.Fields,
                tables.Imports);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags,
                out var section, out var recordCount);
            sections.Add(kind switch {
                0x004A => new TrRmSection(kind, flags, recordCount,
                    rewrittenDependencyFields),
                0x004B => new TrRmSection(kind, flags, recordCount,
                    rewrittenFieldPaths),
                _ => new TrRmSection(kind, flags, recordCount,
                    section.ToArray()),
            });
        }

        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                result, schema, null, usesNativePointerLoads: false,
                standardSchemaIdentity: schemaIdentity));
        return result;
    }

    private static byte[] ReplaceFirstBodyFieldMapToken(byte[] artifact,
        string schemaIdentity, int bodyRow, uint replacement)
    {
        var schema = SchemaFor(schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var fieldMap = body.FieldMap.ToArray();
        if (fieldMap.Length < sizeof(uint))
        {
            throw new InvalidOperationException(
                "The selected constructor Body has no FieldMap token.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(fieldMap, replacement);
        var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            blocks[blockIndex] = new TrRmRegisterBodyBlock(
                BinaryPrimitives.ReadUInt32LittleEndian(block),
                BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(block[8..]));
        }

        var records = new ReadOnlyMemory<byte>[body.RecordCount];
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            records[recordIndex] = body.GetRecord(recordIndex).ToArray();
        }

        var replacementBody = TrRmRegisterBodyWriter.Encode(
            body.HomeMap.ToArray(), body.TypeMap.ToArray(),
            body.MethodMap.ToArray(), fieldMap,
            body.ConstantMap.ToArray(), blocks, records,
            body.Code.ToArray(), tables.Contracts.Count, schema);
        return ReplaceBodyRow(artifact, schema, bodyRow, replacementBody);
    }

    private static byte[] ReplaceBodyRow(byte[] artifact,
        TrRmArtifactSchema schema, int bodyRow, byte[] replacementBody)
    {
        var index = new TrRmArtifactIndex(artifact, schema);
        if (!index.TryGetSection(0x0020, out var bodySection,
                out var bodyCount)
            || bodyRow <= 0 || bodyRow > bodyCount)
        {
            throw new InvalidOperationException(
                "The test artifact has no selected Body row.");
        }

        var offsetBytes = checked((bodyCount + 1) * sizeof(uint));
        var bodyRows = new byte[bodyCount][];
        var bodyDataLength = 0;
        for (var row = 1; row <= bodyCount; row++)
        {
            var start = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(bodySection[((row - 1)
                    * sizeof(uint))..]));
            var end = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(bodySection[(row
                    * sizeof(uint))..]));
            var rowBytes = row == bodyRow
                ? (byte[])replacementBody.Clone()
                : bodySection.Slice(offsetBytes + start,
                    end - start).ToArray();
            bodyRows[row - 1] = rowBytes;
            bodyDataLength = checked(bodyDataLength + rowBytes.Length);
        }

        var newBodySection = new byte[checked(offsetBytes
            + bodyDataLength)];
        var nextOffset = 0;
        for (var rowIndex = 0; rowIndex < bodyRows.Length; rowIndex++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                newBodySection.AsSpan(rowIndex * sizeof(uint)),
                checked((uint)nextOffset));
            var rowBytes = bodyRows[rowIndex];
            rowBytes.CopyTo(newBodySection, offsetBytes + nextOffset);
            nextOffset = checked(nextOffset + rowBytes.Length);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(
            newBodySection.AsSpan(bodyCount * sizeof(uint)),
            checked((uint)nextOffset));
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                kind == 0x0020 ? newBodySection : section.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static void SetTypeDefFlags(byte[] artifact, int typeDefRow,
        uint flags)
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
            BinaryPrimitives.WriteUInt32LittleEndian(
                artifact.AsSpan(sectionOffset
                    + ((typeDefRow - 1) * 44) + 8), flags);
            return;
        }

        throw new InvalidDataException("The TypeDef section is missing.");
    }

    private static byte[] EmitClassArtifact(BoundProgram program,
        string moduleIdentity, string schemaIdentity,
        IReadOnlyList<TrRmSourceClassExport> classExports)
        => SchemaFor(schemaIdentity).UsesC1EnumClosure()
            ? TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
                schemaIdentity, 1, [], [], classExports, [])
            : TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
                schemaIdentity, 1, [], [], classExports);

    private static TrRmArtifactSchema SchemaFor(string schemaIdentity)
        => schemaIdentity switch {
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1"
                => TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1"
                => TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-initializer-copy-evidence-1"
                => TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureInitializerCopyEvidenceDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-initializer-copy-evidence-1"
                => TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureInitializerCopyEvidenceDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1"
                => TrRmArtifactSchema.C1M1ConstructionL1R1PropertyInitializedStateEnumClosureDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1"
                => TrRmArtifactSchema.C1M1Construction2L1R1PropertyInitializedStateEnumClosureDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1"
                => TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1"
                => TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1-initializer-copy-evidence-1"
                => TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureClassInstanceLayoutInitializerCopyEvidenceDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1-initializer-copy-evidence-1"
                => TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureClassInstanceLayoutInitializerCopyEvidenceDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1"
                => TrRmArtifactSchema.C1M1Construction2InitializerCopyEvidenceDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1"
                => TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1-initializer-copy-evidence-1"
                => TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1"
                => TrRmArtifactSchema.C1M1ConstructionL1R1PropertyInitializedStateEnumClosureDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1"
                => TrRmArtifactSchema.C1M1Construction2L1R1PropertyInitializedStateEnumClosureDraft,
            _ => throw new ArgumentOutOfRangeException(nameof(schemaIdentity)),
        };
}
