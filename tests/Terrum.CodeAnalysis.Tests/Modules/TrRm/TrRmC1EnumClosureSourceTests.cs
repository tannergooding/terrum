using System;
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
    public void ReemitsTransitiveImportedStructClosureAcrossProviders()
    {
        var schemaIdentity = s_schemaIdentities[0];
        var leafTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Leaf { public int32 Id; }
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
            ],
        };
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafProgram, "StructLeafProvider", schemaIdentity, 1,
            [leafExport], [], [], []);
        var leafInput = new TrRmStandardArtifactInput(schemaIdentity,
            leafArtifact);
        var leafReference = TrRmExecutableCompiler.CreateStandardReference(
            leafInput, "StructLeafProvider", [], ["type.leaf"]);

        var middleTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Wrapper { public Leaf Child; }
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
        var middleArtifact = TrRmExecutableCompiler.EmitStandard(
            middleProgram, "StructMiddleProvider", schemaIdentity, 1,
            [wrapperExport], [], [], []);
        var middleInput = new TrRmStandardArtifactInput(schemaIdentity,
            middleArtifact);
        var middleReference = TrRmExecutableCompiler.CreateStandardReference(
            middleInput, "StructMiddleProvider", [leafInput],
            ["type.wrapper"]);
        Assert.That(middleReference.Structures.Any(
            static structure => structure.Name == "Demo.Leaf"), Is.True);

        var consumerTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Packet { public Wrapper Value; }
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
                new TrRmSourceFieldExport("Value", "field.packet.value"),
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
        Assert.That(reference.Structures, Has.Length.EqualTo(3));
        Assert.That(reference.Structures.Any(
            static structure => structure.Name == "Demo.Leaf"
                && structure.StandardTypeOrigin is not null), Is.True);
        Assert.That(reference.Structures.Any(
            static structure => structure.Name == "Demo.Wrapper"
                && structure.StandardTypeOrigin is not null), Is.True);

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            sourceFreeCompilation.Program!, "StructConsumer",
            schemaIdentity, 1);
        var reloadedReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "StructConsumer", providers, ["type.packet"]);
        Assert.That(reloadedReference.Structures, Has.Length.EqualTo(3));
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reloadedReference);
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));

        var incompatibleLeafTree = SyntaxTree.Parse("""
            namespace Demo {
                public struct Leaf { public int64 Id; }
            }
            """);
        var incompatibleLeafCompilation
            = Compilation.CreateLibrary(incompatibleLeafTree);
        Assert.That(incompatibleLeafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleLeafCompilation.Diagnostics));
        var incompatibleLeafArtifact
            = TrRmExecutableCompiler.EmitStandard(
                (BoundProgram)incompatibleLeafCompilation.Program!,
                "StructLeafProvider", schemaIdentity, 1, [
                    new TrRmSourceTypeExport("Demo", "Leaf",
                        "type.leaf") {
                        FieldExports = [
                            new TrRmSourceFieldExport("Id",
                                "field.leaf.id"),
                        ],
                    },
                ], [], [], []);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                consumerInput, "StructConsumer", [
                    middleInput,
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleLeafArtifact),
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
                }
                public class Shelf { public Shade ShelfShade; }
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
    public void BindsEN1ClassFieldOffsetOnlyWithExactFieldSelection()
    {
        var schemaIdentity = s_schemaIdentities[0];
        var tree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box { public int32 Value; }
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
                public class Box { public int32 Value; }
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
            ]);
        var changedProviderTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box { public int64 Value; }
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
            ]);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ClassLayoutProvider", schemaIdentity, 1,
            [], [], [classExport], []);
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
            NativeRuntimePolicy.FailFast), Is.EqualTo(8));
        Assert.That(NativeEmitter.EmitLibrary(loaded, ["Offset"],
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
                [Blittable]
                [Layout(LayoutKind.Sequential, Packing: 4)]
                public struct Payload {
                    public uint8 Prefix;
                    public int32 Value;
                }
                public class Box {
                    public uint8 Prefix;
                    public Payload Data;
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
            NativeRuntimePolicy.FailFast), Is.EqualTo(8));

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
        Assert.That(reloadedQuery.Subject.FieldPath.Steps[1]
            .StandardFieldOrigin!.SelectedExportKeys,
            Is.EqualTo(["field.payload.value"]));
    }

    [Test]
    public void ReemitsEN1ClassTypeDefAndFieldImportsAndTypeSpecs()
    {
        var schemaIdentity = s_schemaIdentities[0];
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Marker : int32 { Value = 1 }
                public class Shelf { public int32 Id; }
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
            ]);
        var providerEnumExport = CreateExport(providerProgram,
            providerTree, 0, "Marker", ["type.marker"], "marker");
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ShelfProvider", schemaIdentity, 1,
            [], [], [providerClassExport], [providerEnumExport]);
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
                }
                public class Shelf { public int32 Id; }
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
            0x1400_0000u | checked((uint)shelfFieldStart));
        consumerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, SchemaFor(schemaIdentity)));
        Assert.That(consumerTables.Imports.Count, Is.EqualTo(2));
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
            Has.Length.EqualTo(1));
        Assert.That(importedShelf.Expectations[0].FieldExpectations[0]
            .ExportKey, Is.EqualTo("field.shelf.id"));

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
        Assert.That(shelfType.Qualifiers, Is.EqualTo((byte)0x02));
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
                public class Shelf { public int32 Id; }
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
                public class Shelf { public int32 Id; }
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
                public class Palette { public Shelf ShelfValue; }
                public class Shelf { public int32 Id; }
                public class LocalShelf { public int32 Value; }
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
        Assert.That(ownershipSensitiveUse.HasErrors, Is.True);
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
        uint? expectedFieldDefinitionId = null)
    {
        if ((fieldImportExportKey is null)
            != (expectedFieldDefinitionId is null)
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
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1"
                => TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1"
                => TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1"
                => TrRmArtifactSchema.C1M1ConstructionL1R1PropertyInitializedStateEnumClosureDraft,
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1"
                => TrRmArtifactSchema.C1M1Construction2L1R1PropertyInitializedStateEnumClosureDraft,
            _ => throw new ArgumentOutOfRangeException(nameof(schemaIdentity)),
        };
}
