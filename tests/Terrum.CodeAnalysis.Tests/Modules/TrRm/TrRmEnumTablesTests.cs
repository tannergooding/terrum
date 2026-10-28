using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmEnumTablesTests
{
    [Test]
    public void RoundTripsNominalEnumAndDuplicateValuedMembers()
    {
        var fixture = CreateFixture();
        var result = Validate(fixture);

        Assert.That(result.Flags, Is.EqualTo(1));
        Assert.That(result.MemberCount, Is.EqualTo(2));
        Assert.That(result.Values, Is.EqualTo(new[] { 7, 7 }));
    }

    [Test]
    public void AcceptsAnEmptyEnumWithoutAnEnumMemberSection()
    {
        var result = Validate(CreateFixture(includeMembers: false, blittable: false));

        Assert.That(result.Flags, Is.EqualTo(1));
        Assert.That(result.MemberCount, Is.Zero);
        Assert.That(result.Values, Is.Empty);
    }

    [Test]
    public void RejectsWrongUnderlyingTypeAndReservedEnumFlags()
    {
        var fixture = CreateFixture();
        Assert.Throws<InvalidDataException>(() => Validate(fixture with {
            EnumBytes = WriteUInt32(fixture.EnumBytes, 4, 0xF200_0007),
        }));
        Assert.Throws<InvalidDataException>(() => Validate(fixture with {
            EnumBytes = WriteUInt32(fixture.EnumBytes, 8, 2),
        }));
    }

    [Test]
    public void RejectsDuplicateMemberKeysAndOrphanMemberRows()
    {
        var fixture = CreateFixture();
        Assert.Throws<InvalidDataException>(() => Validate(fixture with {
            MemberBytes = WriteUInt32(fixture.MemberBytes, 20 + 8, 6),
        }));
        Assert.Throws<InvalidDataException>(() => Validate(fixture with {
            EnumBytes = WriteUInt32(fixture.EnumBytes, 16, 1),
        }));
    }

    [Test]
    public void RejectsBlittableLayoutVariantsForAnotherTargetProfile()
    {
        var fixture = CreateFixture();
        var otherProfile = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(otherProfile, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(otherProfile.AsSpan(4), 1);
        Assert.Throws<InvalidDataException>(() => Validate(fixture with {
            TargetProfileBytes = otherProfile,
        }));
    }

    [Test]
    public void StructuralProfileRequiresEnumFeatureAndIsSelectedOutOfBand()
    {
        var artifact = CreateArtifact([
            TrRmFeatureRow.NativeBoundaryContracts,
            new TrRmFeatureRow(18, 1),
        ]);
        var closure = ValidateCandidateArtifact(artifact);
        Assert.That(closure.HasTypedFrozenValues, Is.True);
        Assert.That(closure.HasNominalUnionMetadata, Is.False);
        Assert.Throws<InvalidDataException>(() => ValidateDefaultArtifact(artifact));
        Assert.Throws<InvalidDataException>(() => ValidateCandidateArtifact(
            CreateArtifact([
                TrRmFeatureRow.NativeBoundaryContracts,
                TrRmFeatureRow.TypedFrozenValues,
            ])));
    }

    [Test]
    public void BindsImportedEnumToExactProviderSnapshot()
    {
        var consumer = CreateBoundEnumArtifact(isConsumer: true);
        var provider = CreateBoundEnumArtifact(isConsumer: false);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [provider]));

        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, memberValue: 8)]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, declarationKey: "different-key")]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, providerExportKey: "other")]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, providerModuleIdentity: "other")]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, providerTargetRequirement: 1)]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, flags: 0)]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, blittable: false)]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, []));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [provider, provider]));
    }

    [Test]
    public void EmitsOwnedNominalEnumDeclarationFromBoundProgram()
    {
        var enumIdentity = new CoreDeclarationIdentity(
            "enum-module", "Color", CoreDeclarationKind.EnumType);
        var member = new BoundEnumMemberDefinition(
            new CoreDeclarationIdentity("enum-module", "color-red",
                CoreDeclarationKind.EnumMember),
            enumIdentity, "Red", 7, true, "color-red");
        var definition = new BoundEnumDefinition(enumIdentity, "Example.Color",
            PrimitiveType.Int32, [member], true, true, false)
        {
            BlittableTargetRequirement
                = FixedLayoutStorage.WindowsX64TargetRequirement,
        };
        var program = new BoundProgram([], -1)
        {
            EnumDefinitions = [definition],
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };

        var artifact = TrRmExecutableCompiler
            .EmitNominalEnumsDraftDeclarationLibrary(
                program, "enum-module", moduleTargetRequirement: 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));
        var providerDefinitionExported
            = tables.Exports.TryGetDefinitionForKey(1, "Color", out var localDefId);

        Assert.That(tables.TypeDefs.Count, Is.EqualTo(1));
        Assert.That(tables.EnumDefs.Count, Is.EqualTo(1));
        Assert.That(tables.EnumMembers.Count, Is.EqualTo(1));
        Assert.That(tables.Modules.GetRequirements(1).TargetRequirement,
            Is.EqualTo(1u));
        Assert.That(providerDefinitionExported, Is.True);
        Assert.That(localDefId, Is.EqualTo(0x1300_0001u));
        Assert.That(tables.EnumMembers.GetDeclarationKey(1), Is.EqualTo("color-red"));
        Assert.That(tables.EnumMembers.GetValue(1), Is.EqualTo(7));
    }

    [Test]
    public void RejectsExternalEnumEmissionWithoutSelectedProviderOrigin()
    {
        var enumIdentity = new CoreDeclarationIdentity(
            "provider-module", "Color", CoreDeclarationKind.EnumType);
        var member = new BoundEnumMemberDefinition(
            new CoreDeclarationIdentity("provider-module", "color-red",
                CoreDeclarationKind.EnumMember),
            enumIdentity, "Red", 7, true, "color-red");
        var program = new BoundProgram([], -1)
        {
            EnumDefinitions = [
                new BoundEnumDefinition(enumIdentity, "Example.Color",
                    PrimitiveType.Int32, [member], true, true, false),
            ],
        };

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitNominalEnumsDraftDeclarationLibrary(
                program, "consumer-module", moduleTargetRequirement: 0));
    }

    [Test]
    public void EmitsExternalNominalEnumWithSelectedProviderModuleOrigin()
    {
        var registration
            = Terrum.CodeAnalysis.Tests.Semantics.NativeCoreRegistrationTests.CreateRegistration();
        var enumIdentity = new CoreDeclarationIdentity(
            "core.native", "Color", CoreDeclarationKind.EnumType);
        var member = new BoundEnumMemberDefinition(
            new CoreDeclarationIdentity("core.native", "color-red",
                CoreDeclarationKind.EnumMember),
            enumIdentity, "Red", 7, true, "color-red");
        var importedDefinition = new BoundEnumDefinition(enumIdentity,
            "Example.Color", PrimitiveType.Int32, [member], true, true, false);
        var definitions = registration.EnumDefinitions
            .Select(BoundEnumDefinition.FromCore)
            .Append(importedDefinition)
            .ToArray();
        var program = new BoundProgram([], -1)
        {
            EnumDefinitions = [.. definitions],
            NativeCoreRegistration = registration,
        };

        var artifact = TrRmExecutableCompiler
            .EmitNominalEnumsDraftDeclarationLibrary(
                program, registration.UsingModuleIdentity,
                moduleTargetRequirement: 0);
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));

        Assert.That(tables.EnumDefs.Count, Is.EqualTo(4));
        Assert.That(tables.Imports.Count, Is.EqualTo(4));
        Assert.That(tables.ModuleRefs.GetProducerIdentity(1),
            Is.EqualTo("core.native"));
        Assert.That(tables.ModuleRefs.GetRequirements(1).TargetRequirement,
            Is.EqualTo(0u));
    }

    private static EnumFixture CreateFixture(bool includeMembers = true, bool blittable = true)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("enum-module", 0), ("Color", 0), ("Red", 0),
            ("AlsoRed", 0), ("color-red", 0), ("color-also-red", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 7);
        var modules = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 0, 0);
        var moduleReader = new TrRmModuleTableReader(modules, 1, strings,
            0, 1, 0, 0);
        var layoutCount = blittable ? 2 : 1;
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x12, 0, 0, 0, 0, 1, layoutCount, 0, 0),
        ], strings, moduleReader, 0, 0, layoutCount, 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings,
            moduleReader, 0, 0, layoutCount, 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var targetProfileBytes = blittable
            ? TrRmTargetProfileTableWriter.Encode([new TrRmTargetProfileRow(1, 1)])
            : Array.Empty<byte>();
        var targetProfiles = new TrRmTargetProfileTableReader(targetProfileBytes,
            blittable ? 1 : 0);
        var layoutRows = blittable
            ? new[] {
                new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
                new TrRmLayoutRow(1, 1, 0, 1, 0, 0, 0, 0),
            }
            : new[] { new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0) };
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows, types,
            targetProfiles, 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutCount,
            types, targetProfiles, 0);
        var typeSpecs = default(TrRmTypeSpecTableReader);
        var memberRows = includeMembers
            ? new[] {
                new TrRmEnumMemberDefRow(1, 0, 6, 4, 7),
                new TrRmEnumMemberDefRow(1, 1, 7, 5, 7),
            }
            : Array.Empty<TrRmEnumMemberDefRow>();
        var memberBytes = TrRmEnumTablesWriter.EncodeMembers(memberRows,
            strings, types);
        var members = new TrRmEnumMemberDefTableReader(memberBytes,
            memberRows.Length, strings, types);
        var enumRows = new[] {
            new TrRmEnumDefRow(1, 0xF200_0008, 1,
                includeMembers ? 1 : 0, includeMembers ? 2 : 0),
        };
        var enumBytes = TrRmEnumTablesWriter.Encode(enumRows, types, typeSpecs,
            layouts, targetProfiles, members);
        return new EnumFixture(stringBytes, modules, typeBytes, targetProfileBytes,
            layoutBytes, memberBytes, enumBytes, layoutCount, memberRows.Length);
    }

    private static (uint Flags, int MemberCount, int[] Values) Validate(EnumFixture fixture)
    {
        var strings = new TrRmStringTableReader(fixture.StringBytes, 7);
        var modules = new TrRmModuleTableReader(fixture.ModuleBytes, 1, strings,
            0, 1, 0, 0);
        var types = new TrRmTypeDefTableReader(fixture.TypeBytes, 1, strings,
            modules, 0, 0, fixture.LayoutCount, 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var targetProfiles = new TrRmTargetProfileTableReader(
            fixture.TargetProfileBytes, fixture.TargetProfileBytes.Length / 8);
        var layouts = new TrRmLayoutTableReader(fixture.LayoutBytes,
            fixture.LayoutCount, types, targetProfiles, 0);
        var typeSpecs = default(TrRmTypeSpecTableReader);
        var members = new TrRmEnumMemberDefTableReader(fixture.MemberBytes,
            fixture.MemberCount, strings, types);
        var enums = new TrRmEnumDefTableReader(fixture.EnumBytes, 1, types,
            typeSpecs, layouts, targetProfiles, members);
        var range = enums.GetMemberRange(1);
        var values = new int[range.Count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = members.GetValue(range.First + index);
        }

        return (enums.GetFlags(1), range.Count, values);
    }

    private static byte[] CreateArtifact(TrRmFeatureRow[] features)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("enum-module", 0), ("Color", 0), ("Red", 0),
            ("AlsoRed", 0), ("color-red", 0), ("color-also-red", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 7);
        var featureBytes = TrRmFeatureTableWriter.Encode(features,
            TrRmArtifactSchema.NominalEnumsDraft);
        var modules = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1, features.Length),
        ], strings, 0, 1, 0, 0, features.Length);
        var moduleReader = new TrRmModuleTableReader(modules, 1, strings,
            0, 1, 0, 0, features.Length);
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ]);
        var targetProfiles = new TrRmTargetProfileTableReader(targetProfileBytes, 1);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x12, 0, 0, 0, 0, 1, 2, 0, 0),
        ], strings, moduleReader, 0, 0, 2, 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings,
            moduleReader, 0, 0, 2, 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(1, 1, 0, 1, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 2, types,
            targetProfiles, 0);
        var members = TrRmEnumTablesWriter.EncodeMembers([
            new TrRmEnumMemberDefRow(1, 0, 6, 4, 7),
            new TrRmEnumMemberDefRow(1, 1, 7, 5, 7),
        ], strings, types);
        var memberReader = new TrRmEnumMemberDefTableReader(members, 2,
            strings, types);
        var enumDefinitions = TrRmEnumTablesWriter.Encode([
            new TrRmEnumDefRow(1, 0xF200_0008, 1, 1, 2),
        ], types, default, layouts, targetProfiles, memberReader);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modules),
            new TrRmSection(0x0002, 0, 7, stringBytes),
            new TrRmSection(0x0004, 0, (uint)features.Length, featureBytes),
            new TrRmSection(0x0013, 0, 1, typeBytes),
            new TrRmSection(0x0015, 0, 2, layoutBytes),
            new TrRmSection(0x0048, 0, 1, targetProfileBytes),
            new TrRmSection(0x0053, 0, 1, enumDefinitions),
            new TrRmSection(0x0054, 0, 2, members),
        ], TrRmArtifactSchema.NominalEnumsDraft);
    }

    private static byte[] CreateBoundEnumArtifact(bool isConsumer,
        int memberValue = 7, string declarationKey = "color-red",
        bool blittable = true, uint flags = 1,
        string providerModuleIdentity = "provider",
        uint providerTargetRequirement = 0, uint providerRuntimePolicy = 0,
        string providerExportKey = "color")
    {
        const string providerIdentity = "provider";
        const string exportKey = "color";
        var moduleIdentity = isConsumer ? "consumer" : providerModuleIdentity;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), (moduleIdentity, 0), (providerIdentity, 0),
            ("Color", 0), ("Red", 0), ("AlsoRed", 0),
            (declarationKey, 0), ("color-also-red", 0),
            (isConsumer ? exportKey : providerExportKey, 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 9);
        var featureRows = new[]
        {
            TrRmFeatureRow.NativeBoundaryContracts,
            new TrRmFeatureRow(18, 1),
        };
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows,
            TrRmArtifactSchema.NominalEnumsDraft);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2,
                isConsumer ? 0 : providerTargetRequirement,
                isConsumer ? 0 : providerRuntimePolicy,
                0, 1, 1, 1, 1, 1, featureRows.Length),
        ], strings, moduleRefCount: isConsumer ? 1 : 0, typeDefCount: 1,
            methodDefCount: 0, sourceFileCount: 0,
            featureCount: featureRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount: isConsumer ? 1 : 0, typeDefCount: 1,
            methodDefCount: 0, sourceFileCount: 0,
            featureCount: featureRows.Length);
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ]);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, 1);
        var layoutCount = blittable ? 2 : 1;
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 4, 0x12, 0, 0, 0, 0,
                1, layoutCount, 0, 0),
        ], strings, modules, fieldCount: 0, genericParamCount: 0,
            layoutCount, contractCount: 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings,
            modules, 0, 0, layoutCount, 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var layoutRows = blittable
            ? new[] {
                new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
                new TrRmLayoutRow(1, 1, 0, 1, 0, 0, 0, 0),
            }
            : new[] { new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0) };
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows, types,
            targetProfiles, layoutFieldCount: 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutCount,
            types, targetProfiles, layoutFieldCount: 0);
        var membersBytes = TrRmEnumTablesWriter.EncodeMembers([
            new TrRmEnumMemberDefRow(1, 0, 7, 5, memberValue),
            new TrRmEnumMemberDefRow(1, 1, 8, 6, 7),
        ], strings, types);
        var members = new TrRmEnumMemberDefTableReader(
            membersBytes, 2, strings, types);
        var enumBytes = TrRmEnumTablesWriter.Encode([
            new TrRmEnumDefRow(1, 0xF200_0008, flags, 1, 2),
        ], types, default, layouts, targetProfiles, members);
        var moduleRefsBytes = isConsumer
            ? TrRmModuleRefTableWriter.Encode([
                new TrRmModuleRefRow(3, 0, 0),
            ], strings, modules)
            : Array.Empty<byte>();
        var moduleRefs = new TrRmModuleRefTableReader(
            moduleRefsBytes, isConsumer ? 1 : 0, strings, modules);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Length, TrRmArtifactSchema.NominalEnumsDraft);
        var importsBytes = isConsumer
            ? TrRmImportTableWriter.Encode([
                new TrRmImportRow(1, 9, 0x1300_0001),
            ], strings, moduleRefs, types, default, default, default,
                default, default, features, modules,
                TrRmArtifactSchema.NominalEnumsDraft)
            : Array.Empty<byte>();
        var imports = new TrRmImportTableReader(importsBytes,
            isConsumer ? 1 : 0, strings, moduleRefs, types, default, default,
            default, default, default, features, modules,
            TrRmArtifactSchema.NominalEnumsDraft);
        var exportsBytes = isConsumer
            ? Array.Empty<byte>()
            : TrRmExportTableWriter.Encode([
                new TrRmExportRow(0x1300_0001, 9),
            ], strings, types, default, default, imports,
                default, default, default, features, modules,
                TrRmArtifactSchema.NominalEnumsDraft);

        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 9, stringBytes),
            new(0x0004, 0, (uint)featureRows.Length, featureBytes),
            new(0x0013, 0, 1, typeBytes),
            new(0x0015, 0, (uint)layoutCount, layoutBytes),
            new(0x0048, 0, 1, targetProfileBytes),
            new(0x0053, 0, 1, enumBytes),
            new(0x0054, 0, 2, membersBytes),
        };
        if (isConsumer)
        {
            sections.Add(new TrRmSection(0x0010, 0, 1, moduleRefsBytes));
            sections.Add(new TrRmSection(0x001C, 0, 1, importsBytes));
        }
        else
        {
            sections.Add(new TrRmSection(0x001D, 0, 1, exportsBytes));
        }

        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.NominalEnumsDraft);
    }

    private static (bool HasTypedFrozenValues, bool HasNominalUnionMetadata)
        ValidateCandidateArtifact(byte[] artifact)
    {
        var index = TrRmArtifactIndex.ForNominalEnumsDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(index);
        return (
            TrRmFeatureManifestValidator.Requires(tables.Features, tables.Modules, 1,
                TrRmFeatureTableReader.TypedFrozenValuesFeatureId, index.Schema),
            TrRmFeatureManifestValidator.Requires(tables.Features, tables.Modules, 1,
                TrRmFeatureTableReader.NominalUnionMetadataFeatureId, index.Schema));
    }

    private static void ValidateDefaultArtifact(byte[] artifact)
        => TrRmStructuralMetadataValidator.Validate(artifact);

    private static byte[] WriteUInt32(byte[] source, int offset, uint value)
    {
        var result = (byte[])source.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), value);
        return result;
    }

    private sealed record EnumFixture(
        byte[] StringBytes, byte[] ModuleBytes, byte[] TypeBytes,
        byte[] TargetProfileBytes, byte[] LayoutBytes, byte[] MemberBytes,
        byte[] EnumBytes, int LayoutCount, int MemberCount);
}
