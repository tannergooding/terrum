using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private sealed record C1EnumClosureEmissionMember(
        string DeclarationKey, string Name, BigInteger Value);

    private sealed record C1EnumClosureImport(
        string ProducerIdentity, string ExportKey, uint TargetRequirement,
        uint RuntimePolicy);

    private sealed record C1EnumClosureEmissionLayout(
        uint TargetProfileId, uint TargetProfileRuleRevision,
        byte Kind, byte Flags, uint Alignment, uint Pack);

    private sealed record C1EnumClosureEmission(
        string Namespace, string Name, uint TypeDefFlags,
        PrimitiveType UnderlyingType, uint EnumFlags,
        ImmutableArray<C1EnumClosureEmissionLayout> Layouts,
        ImmutableArray<C1EnumClosureEmissionMember> Members,
        ImmutableArray<string> ExportKeys,
        C1EnumClosureImport? Import);

    private static byte[] EmitC1EnumClosureDeclarationLibrary(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement, TrRmArtifactSchema schema,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports,
        SourceGenericMethodExports? sourceGenericMethodExports,
        SourceClassExports? sourceClassExports,
        C1EnumClosureSourceEnumExports? sourceEnumExports)
    {
        if ((sourceEnumExports is not null
                && !ReferenceEquals(sourceEnumExports.Program, program))
            || sourceGenericMethodExports?.Exports.IsEmpty == false
            || sourceClassExports?.Exports.IsEmpty == false
            || sourceTypeExports is { Count: > 0 }
            || program.EntryPoint != -1 || !program.Functions.IsEmpty
            || !program.Structures.IsEmpty || !program.Classes.IsEmpty
            || !program.Interfaces.IsEmpty
            || !program.InterfaceWitnesses.IsEmpty
            || !program.Buffers.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || !program.NativePointerTypes.IsEmpty
            || !program.NativeFixedArrayTypes.IsEmpty
            || !program.CheckedReferenceTypes.IsEmpty
            || !program.PrivateNominalTypeOrigins.IsEmpty
            || !program.PrivateClassReferences.IsEmpty
            || !program.StaticFields.IsEmpty
            || !program.StaticInitializationGroups.IsEmpty
            || !program.NativeUtf16ZLiterals.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty
            || program.StandardMethodProviderClosure is not null
            || program.StandardFieldProviderClosure is not null
            || !program.StandardClassDeclarations.IsEmpty
            || program.StandardQuerySourceAssociation is not null
            || program.NativeCoreRegistration is not null
            || program.NativeOutputCoreRegistration is not null
            || program.LayoutCoreRegistration is not null
            || program.StaticInitializationCoreRegistration is not null
            || program.LazyInitializationCoreRegistration is not null
            || program.CoreTaskType is not null
            || !program.CoreExceptionIdentitySelections.IsEmpty
            || !program.CoreContractFailureIdentitySelections.IsEmpty
            || program.CoreContractFailureIdentitySelection is not null
            || program.CoreEvaluationFormIdentitySelection is not null
            || !program.CoreEvaluationFormIdentitySelections.IsEmpty
            || !program.CoreEvaluationFormSchemaProviders.IsEmpty
            || program.RequiresSourceGrant
            || program.InvocationProfileId is not null
            || program.NativeLoweringAgreement is not null
            || !program.SourceTypeDeclarations.IsEmpty
            || !program.SourceGenericTypeDefinitions.IsEmpty
            || !program.SourceGenericTypeInstances.IsEmpty
            || !program.GenericNominalTypeDefinitions.IsEmpty
            || !program.GenericNominalTypeInstances.IsEmpty
            || !program.GenericMethodDefinitions.IsEmpty
            || !program.GenericMethodSignatures.IsEmpty
            || !program.GenericMethodOperatorProtocols.IsEmpty
            || !schema.UsesC1EnumClosure()
            || !BoundEnumTypes.IsValidTable(program.EnumDefinitions)
            || program.EnumDefinitions.Any(static definition =>
                definition.Identity is not
                    (BoundEnumDeclarationIdentity.Source
                    or BoundEnumDeclarationIdentity.Standard))
            || (sourceEnumExports is not null
                && program.EnumDefinitions.Any(static definition =>
                    definition.Identity
                        is not BoundEnumDeclarationIdentity.Source))
            || (sourceEnumExports is null
                && program.EnumDefinitions.Any(static definition =>
                    definition.Identity
                        is not BoundEnumDeclarationIdentity.Standard)))
        {
            throw Unsupported(
                "The bounded EN1 declaration route accepts mapped source enums or retained Standard enum views and direct expectations; mixed class and executable closure remains outside this writer.");
        }

        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            moduleTargetRequirement,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);

        var emittedEnums = new List<C1EnumClosureEmission>(
            program.EnumDefinitions.Length);
        var emittedMemberCount = 0L;
        void EnsureEmissionCapacity(int memberCount)
        {
            if (emittedEnums.Count >= SemanticVerifier.MaximumAnalysisVisits
                || memberCount < 0
                || emittedMemberCount + memberCount
                    > SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Unsupported(
                    "The EN1 enum closure exceeds the checked declaration/member work limits.");
            }

            emittedMemberCount += memberCount;
        }

        if (sourceEnumExports is not null)
        {
            var sourceExportsByIndex = sourceEnumExports.Exports
                .ToDictionary(static export => export.EnumIndex);
            if (sourceExportsByIndex.Count != program.EnumDefinitions.Length)
            {
                throw new InvalidDataException(
                    "The EN1 source enum map does not cover the complete declaration table.");
            }

            for (var enumIndex = 0;
                enumIndex < program.EnumDefinitions.Length; enumIndex++)
            {
                var definition = program.EnumDefinitions[enumIndex];
                var sourceExport = sourceExportsByIndex[enumIndex];
                if (!ReferenceEquals(sourceExport.Definition, definition)
                    || sourceExport.MemberDeclarationKeys.IsDefault
                    || sourceExport.MemberDeclarationKeys.Length
                        != definition.Members.Length)
                {
                    throw new InvalidDataException(
                        "The EN1 source enum assignment no longer matches its checked definition.");
                }

                var separator = definition.Name.LastIndexOf(
                    '.', StringComparison.Ordinal);
                var namespaceName = separator < 0
                    ? "" : definition.Name[..separator];
                var typeName = separator < 0
                    ? definition.Name : definition.Name[(separator + 1)..];
                if (string.IsNullOrWhiteSpace(typeName) || separator == 0)
                {
                    throw Unsupported(
                        "EN1 enum names require valid namespace and type-name components.");
                }

                EnsureEmissionCapacity(definition.Members.Length);
                var members
                    = ImmutableArray.CreateBuilder<
                        C1EnumClosureEmissionMember>(
                        definition.Members.Length);
                for (var memberIndex = 0;
                    memberIndex < definition.Members.Length; memberIndex++)
                {
                    var member = definition.Members[memberIndex];
                    members.Add(new C1EnumClosureEmissionMember(
                        sourceExport.MemberDeclarationKeys[memberIndex],
                        member.Name, member.Value));
                }

                if (!TryGetEnumTypeCode(definition.UnderlyingType,
                    out var underlyingTypeCode))
                {
                    throw new InvalidDataException(
                        "The EN1 source enum has no admitted integer base.");
                }

                var needsWindowsProfile
                    = definition.BlittableTargetRequirement != 0
                        || underlyingTypeCode is 0x10 or 0x11;
                var layouts
                    = ImmutableArray.CreateBuilder<
                        C1EnumClosureEmissionLayout>(
                        needsWindowsProfile ? 2 : 1);
                layouts.Add(new C1EnumClosureEmissionLayout(0, 0, 0, 0,
                    0, 0));
                if (needsWindowsProfile)
                {
                    layouts.Add(new C1EnumClosureEmissionLayout(1, 1, 0,
                        definition.BlittableTargetRequirement == 0
                            ? (byte)0 : (byte)1, 0, 0));
                }

                var visibility = definition.IsPublic ? 2u
                    : sourceExport.Declaration.IsFileLocal ? 0u : 1u;
                emittedEnums.Add(new C1EnumClosureEmission(namespaceName,
                    typeName, 0x10u | visibility,
                    definition.UnderlyingType,
                    definition.IsFlags ? 1u : 0u,
                    layouts.MoveToImmutable(), members.MoveToImmutable(),
                    sourceExport.ExportKeys, null));
            }
        }
        else
        {
            BoundSourceTypeProviderSnapshot? rootSnapshot = null;
            foreach (var definition in program.EnumDefinitions)
            {
                var occurrence
                    = ((BoundEnumDeclarationIdentity.Standard)
                        definition.Identity).Value;
                var snapshot = occurrence.Declaration.Site.Snapshot;
                if (!IsSelectedC1EnumClosureSchemaIdentity(
                        snapshot.SchemaIdentity)
                    || GetForeignInvocationSchema(snapshot.SchemaIdentity)
                        != schema)
                {
                    throw Unsupported(
                        "EN1 source-free enum emission requires declarations bound to the exact selected schema.");
                }

                if (!occurrence.SelectedExportKeys.IsEmpty)
                {
                    if (rootSnapshot is not null
                        && !BoundStandardEnumDeclarationContract
                            .SnapshotsMatch(rootSnapshot, snapshot))
                    {
                        throw Unsupported(
                            "EN1 source-free enum emission does not combine multiple root Modules.");
                    }

                    rootSnapshot = snapshot;
                }
            }

            if (rootSnapshot is null
                || rootSnapshot.TargetRequirement
                    != moduleTargetRequirement
                || rootSnapshot.RuntimePolicy != (uint)program.RuntimePolicy)
            {
                throw Unsupported(
                    "EN1 source-free enum emission requires a selected root Module with the exact target/runtime binding.");
            }

            C1EnumClosureEmission CreateEmission(
                BoundStandardEnumView view,
                ImmutableArray<string> exportKeys,
                C1EnumClosureImport? import)
            {
                EnsureEmissionCapacity(view.Members.Length);
                var layouts
                    = ImmutableArray.CreateBuilder<
                        C1EnumClosureEmissionLayout>(view.Layouts.Length);
                foreach (var layout in view.Layouts)
                {
                    layouts.Add(new C1EnumClosureEmissionLayout(
                        layout.TargetProfileId,
                        layout.TargetProfileRuleRevision, layout.Kind,
                        layout.Flags, layout.Alignment, layout.Pack));
                }

                var members
                    = ImmutableArray.CreateBuilder<
                        C1EnumClosureEmissionMember>(view.Members.Length);
                foreach (var member in view.Members)
                {
                    members.Add(new C1EnumClosureEmissionMember(
                        member.DeclarationKey, member.Name, member.Value));
                }

                return new C1EnumClosureEmission(view.Namespace, view.Name,
                    view.TypeDefFlags, view.UnderlyingType, view.EnumFlags,
                    layouts.MoveToImmutable(), members.MoveToImmutable(),
                    exportKeys, import);
            }

            foreach (var definition in program.EnumDefinitions)
            {
                var occurrence
                    = ((BoundEnumDeclarationIdentity.Standard)
                        definition.Identity).Value;
                if (occurrence.Expectations.IsEmpty)
                {
                    if (!BoundStandardEnumDeclarationContract.SnapshotsMatch(
                            occurrence.Declaration.Site.Snapshot,
                            rootSnapshot))
                    {
                        throw Unsupported(
                            "EN1 source-free enum emission cannot re-own a declaration from another Module.");
                    }

                    emittedEnums.Add(CreateEmission(
                        occurrence.Declaration, occurrence.ExportKeys, null));
                    continue;
                }

                foreach (var expectation in occurrence.Expectations)
                {
                    var binding = expectation.Binding;
                    if (!BoundStandardEnumDeclarationContract.SnapshotsMatch(
                            binding.UsingModule, rootSnapshot))
                    {
                        continue;
                    }

                    emittedEnums.Add(CreateEmission(
                        expectation.ExpectedDeclaration, [],
                        new C1EnumClosureImport(
                            binding.ProducerIdentity, binding.ExportKey,
                            binding.TargetRequirement,
                            binding.RuntimePolicy)));
                }
            }
        }

        if (emittedEnums.Count == 0)
        {
            throw Unsupported(
                "EN1 declaration emission requires retained enum declarations.");
        }

        var stringRows = new List<(string Text, int Encoding)> {
            ("", 0), (moduleIdentity, GetStringEncoding(moduleIdentity)),
        };
        var stringRowsByText = new Dictionary<string, int>(
            StringComparer.Ordinal) {
            [""] = 1,
            [moduleIdentity] = 2,
        };
        var typeDefRows = new List<TrRmTypeDefRow>(
            emittedEnums.Count);
        var typeDefNames = new List<(string Namespace, string Name)>(
            emittedEnums.Count);
        var layoutRows = new List<TrRmLayoutRow>(
            checked(emittedEnums.Count * 2));
        var enumRows = new List<TrRmEnumDefRow>(
            emittedEnums.Count);
        var enumMemberRows = new List<TrRmTypedEnumMemberDefRow>();
        var typeSpecRows = new List<ReadOnlyMemory<byte>>();
        var typeSpecByEnumIndex = new Dictionary<int, uint>();
        var exportRows = new List<TrRmExportRow>();
        var moduleRefRows = new List<TrRmModuleRefRow>();
        var moduleRefRowsByProvider = new Dictionary<
            (string ProducerIdentity, uint TargetRequirement,
                uint RuntimePolicy), int>();
        var importRows = new List<TrRmImportRow>();
        var valueBuilder = new TrRmFrozenScalarValueBuilder();
        var hasBlittableEnum = false;
        var hasTargetProfile = false;

        int AddString(string text)
        {
            if (!stringRowsByText.TryGetValue(text, out var row))
            {
                row = stringRows.Count + 1;
                stringRows.Add((text, GetStringEncoding(text)));
                stringRowsByText.Add(text, row);
            }

            return row;
        }

        for (var enumIndex = 0; enumIndex < emittedEnums.Count; enumIndex++)
        {
            var emittedEnum = emittedEnums[enumIndex];
            if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                    emittedEnum.Name)
                || !TryGetEnumTypeCode(emittedEnum.UnderlyingType,
                    out var underlyingTypeCode))
            {
                throw new InvalidDataException(
                    "The EN1 enum emission no longer matches its checked declaration.");
            }

            if (string.IsNullOrWhiteSpace(emittedEnum.Name)
                || (emittedEnum.TypeDefFlags & 0x1C) != 0x10
                || (emittedEnum.TypeDefFlags & 3) == 3
                || (emittedEnum.TypeDefFlags & ~0x1Fu) != 0
                || emittedEnum.EnumFlags > 1
                || emittedEnum.Layouts.IsDefaultOrEmpty
                || emittedEnum.Members.IsDefault
                || emittedEnum.ExportKeys.IsDefault
                || (emittedEnum.Import is not null
                    && !emittedEnum.ExportKeys.IsEmpty))
            {
                throw new InvalidDataException(
                    "An EN1 enum emission has invalid declaration flags or mappings.");
            }

            var typeDefRow = enumIndex + 1;
            var namespaceName = emittedEnum.Namespace;
            var typeName = emittedEnum.Name;
            var layoutFirst = layoutRows.Count + 1;
            var hasWindowsProfileForEnum = false;
            for (var layoutIndex = 0;
                layoutIndex < emittedEnum.Layouts.Length; layoutIndex++)
            {
                var layout = emittedEnum.Layouts[layoutIndex];
                if (layout.Kind != 0 || (layout.Flags & ~1) != 0
                    || layout.Alignment != 0 || layout.Pack != 0)
                {
                    throw new InvalidDataException(
                        "An EN1 enum layout has unsupported fields or flags.");
                }

                var targetProfileReference = 0u;
                if (layout.TargetProfileId != 0
                    || layout.TargetProfileRuleRevision != 0)
                {
                    if (layout.TargetProfileId
                            != TrRmTargetProfileTableReader
                                .WindowsX64NativeProfileId
                        || layout.TargetProfileRuleRevision
                            != TrRmTargetProfileTableReader
                                .WindowsX64NativeProfileRuleRevision)
                    {
                        throw Unsupported(
                            "EN1 enum layouts require the selected Windows x64 target profile.");
                    }

                    targetProfileReference = 1;
                    hasWindowsProfileForEnum = true;
                    hasTargetProfile = true;
                }

                if ((layout.Flags & 1) != 0
                    && targetProfileReference == 0)
                {
                    throw new InvalidDataException(
                        "An EN1 Blittable enum layout requires a target profile.");
                }

                hasBlittableEnum |= (layout.Flags & 1) != 0;
                layoutRows.Add(new TrRmLayoutRow(typeDefRow,
                    targetProfileReference, layout.Kind, layout.Flags,
                    layout.Alignment, layout.Pack, 0, 0));
            }

            var hasNativeWidth = underlyingTypeCode is 0x10 or 0x11;
            if ((hasNativeWidth || hasWindowsProfileForEnum)
                && moduleTargetRequirement
                    != TrRmModuleTableReader
                        .WindowsX64NativeTargetRequirement)
            {
                throw Unsupported(
                    "EN1 native-width and profile-specific enum layouts require the selected Windows x64 target.");
            }
            if (hasNativeWidth && !hasWindowsProfileForEnum)
            {
                throw new InvalidDataException(
                    "An EN1 native-width enum has no Windows x64 layout variant.");
            }

            typeDefRows.Add(new TrRmTypeDefRow(0, 0,
                emittedEnum.TypeDefFlags, 0, 0, 0, 0,
                checked((uint)layoutFirst),
                checked((uint)emittedEnum.Layouts.Length),
                0, 0));
            typeDefNames.Add((namespaceName, typeName));

            var enumImportRow = 0;
            if (emittedEnum.Import is { } enumImport)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                        enumImport.ProducerIdentity)
                    || !BoundEnumTypes.IsValidModuleDeclarationKey(
                        enumImport.ExportKey))
                {
                    throw new InvalidDataException(
                        "An EN1 enum Import has an invalid producer identity or ExportKey.");
                }

                var providerKey = (enumImport.ProducerIdentity,
                    enumImport.TargetRequirement, enumImport.RuntimePolicy);
                if (!moduleRefRowsByProvider.TryGetValue(providerKey,
                        out var moduleRefRow))
                {
                    moduleRefRow = moduleRefRows.Count + 1;
                    moduleRefRows.Add(new TrRmModuleRefRow(
                        AddString(enumImport.ProducerIdentity),
                        enumImport.TargetRequirement,
                        enumImport.RuntimePolicy));
                    moduleRefRowsByProvider.Add(providerKey, moduleRefRow);
                }

                importRows.Add(new TrRmImportRow(moduleRefRow,
                    AddString(enumImport.ExportKey),
                    0x1300_0000u | checked((uint)typeDefRow)));
                enumImportRow = importRows.Count;
            }

            if (!emittedEnum.Members.IsEmpty)
            {
                var typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                    enumImportRow == 0 ? (byte)0x02 : (byte)0x03,
                    checked((uint)(enumImportRow == 0
                        ? typeDefRow : enumImportRow))));
                typeSpecByEnumIndex.Add(enumIndex, typeSpecRow);
            }

            var memberFirst = emittedEnum.Members.IsEmpty
                ? 0 : checked(enumMemberRows.Count + 1);
            for (var memberIndex = 0;
                memberIndex < emittedEnum.Members.Length; memberIndex++)
            {
                var member = emittedEnum.Members[memberIndex];
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                        member.DeclarationKey)
                    || !BoundEnumTypes.IsValidModuleDeclarationKey(
                        member.Name)
                    || !ScalarBits.TryFromIntegerValue(member.Value,
                        emittedEnum.UnderlyingType, out var bits))
                {
                    throw Unsupported(
                        "An EN1 enum member value is outside its exact underlying integer type.");
                }

                var constant = new BoundConstant(bits,
                    BoundEnumTypes.Type(enumIndex),
                    emittedEnum.UnderlyingType, default);
                var frozenValue = valueBuilder.AddC1EnumClosureMember(
                    constant, typeSpecByEnumIndex[enumIndex],
                    emittedEnum.UnderlyingType, schema);
                enumMemberRows.Add(new TrRmTypedEnumMemberDefRow(
                    typeDefRow, enumMemberRows.Count - memberFirst + 1,
                    AddString(member.DeclarationKey),
                    AddString(member.Name),
                    frozenValue.ConstantSourceRow));
            }

            enumRows.Add(new TrRmEnumDefRow(typeDefRow,
                0xF200_0000u | underlyingTypeCode,
                emittedEnum.EnumFlags, memberFirst,
                emittedEnum.Members.Length));
            foreach (var exportKey in emittedEnum.ExportKeys)
            {
                exportRows.Add(new TrRmExportRow(
                    0x1300_0000u | checked((uint)typeDefRow),
                    AddString(exportKey)));
            }
        }

        foreach (var typeName in typeDefNames)
        {
            _ = AddString(typeName.Namespace);
            _ = AddString(typeName.Name);
        }

        var textBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(textBytes, stringRows.Count);
        for (var typeDef = 0; typeDef < typeDefRows.Count; typeDef++)
        {
            typeDefRows[typeDef] = typeDefRows[typeDef] with {
                NamespaceStringRow
                    = stringRowsByText[typeDefNames[typeDef].Namespace],
                NameStringRow = stringRowsByText[
                    typeDefNames[typeDef].Name],
            };
        }

        var featureRows = new List<TrRmFeatureRow>();
        if (enumRows.Count != 0)
        {
            featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
            featureRows.Add(new TrRmFeatureRow(18, 1));
        }
        if (hasBlittableEnum)
        {
            featureRows.Add(TrRmFeatureRow.NativeBoundaryContracts);
        }
        featureRows.Sort(static (left, right)
            => left.FeatureId.CompareTo(right.FeatureId));
        var targetProfileCount = hasTargetProfile ? 1 : 0;
        var totalRows = 1L + stringRows.Count + typeDefRows.Count
            + moduleRefRows.Count + importRows.Count + layoutRows.Count
            + enumRows.Count + enumMemberRows.Count + typeSpecRows.Count
            + exportRows.Count + featureRows.Count + targetProfileCount
            + valueBuilder.BlobCount + valueBuilder.ConstantSourceCount
            + valueBuilder.FrozenValueNodeCount;
        if (stringRows.Count > 0xFF_FFFF
            || typeDefRows.Count > 0xFF_FFFF
            || moduleRefRows.Count > 0xFF_FFFF
            || importRows.Count > 0xFF_FFFF
            || layoutRows.Count > 0xFF_FFFF
            || enumRows.Count > 0xFF_FFFF
            || enumMemberRows.Count > 0xFF_FFFF
            || typeSpecRows.Count > 0xFF_FFFF
            || exportRows.Count > 0xFF_FFFF
            || featureRows.Count > 0xFF_FFFF
            || totalRows > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "The EN1 enum closure exceeds the checked metadata row limits.");
        }

        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, moduleTargetRequirement,
                (uint)program.RuntimePolicy, 0, 1, 1, 1, 1,
                featureRows.Count == 0 ? 0u : 1u,
                checked((uint)featureRows.Count)),
        ], strings, moduleRefRows.Count, typeDefRows.Count, 0, 0,
            featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefRows.Count, typeDefRows.Count, 0, 0,
            featureRows.Count);
        var features = featureRows.Count == 0
            ? default
            : new TrRmFeatureTableReader(featureBytes, featureRows.Count,
                schema);
        var moduleRefBytes = moduleRefRows.Count == 0 ? []
            : TrRmModuleRefTableWriter.Encode(moduleRefRows, strings,
                modules);
        var moduleRefs = moduleRefRows.Count == 0 ? default
            : new TrRmModuleRefTableReader(moduleRefBytes,
                moduleRefRows.Count, strings, modules);
        var typeDefBytes = typeDefRows.Count == 0 ? []
            : TrRmTypeDefTableWriter.Encode(typeDefRows, strings, modules,
                0, 0, layoutRows.Count, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            typeDefRows.Count, strings, modules, 0, 0, layoutRows.Count,
            0, schema);
        var targetProfileBytes = hasTargetProfile
            ? TrRmTargetProfileTableWriter.Encode([
                new TrRmTargetProfileRow(
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader
                        .WindowsX64NativeProfileRuleRevision),
            ])
            : [];
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, targetProfileCount);
        var layoutBytes = layoutRows.Count == 0 ? []
            : TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
                targetProfiles, layoutFieldCount: 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes,
            layoutRows.Count, typeDefs, targetProfiles,
            layoutFieldCount: 0);
        var typeSpecBytes = typeSpecRows.Count == 0 ? []
            : TrRmTypeSpecTableWriter.Encode(typeSpecRows,
                typeDefRows.Count, importRows.Count, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, typeDefRows.Count, importRows.Count, 0, 0,
            schema);
        var importBytes = importRows.Count == 0 ? []
            : TrRmImportTableWriter.Encode(importRows, strings, moduleRefs,
                typeDefs, default, default, default, default, default,
                features, modules, schema);
        var imports = new TrRmImportTableReader(importBytes,
            importRows.Count, strings,
            moduleRefs, typeDefs, default, default, default, default, default,
            features, modules, schema);
        var enumMemberBytes = enumMemberRows.Count == 0 ? []
            : TrRmEnumTablesWriter.EncodeTypedEnumMemberValues(
                enumMemberRows, strings, typeDefs,
                valueBuilder.ConstantSourceCount);
        var enumMembers = new TrRmEnumMemberDefTableReader(enumMemberBytes,
            enumMemberRows.Count, strings, typeDefs,
            valueBuilder.ConstantSourceCount);
        var enumDefBytes = enumRows.Count == 0 ? []
            : TrRmEnumTablesWriter.Encode(enumRows, typeDefs, typeSpecs,
                layouts, targetProfiles, enumMembers);
        var enumDefs = new TrRmEnumDefTableReader(enumDefBytes,
            enumRows.Count, typeDefs, typeSpecs, layouts,
            targetProfiles, enumMembers);
        var values = valueBuilder.EncodeC1EnumClosure(typeSpecs, typeDefs,
            enumDefs, imports);
        var exportBytes = exportRows.Count == 0 ? []
            : TrRmExportTableWriter.Encode(exportRows, strings, typeDefs,
                default, default, imports, default, default, default,
                features, modules, schema);

        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, checked((uint)stringRows.Count), textBytes),
        };
        if (featureRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                checked((uint)featureRows.Count), featureBytes));
        }
        if (values.BlobCount != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                checked((uint)values.BlobCount), values.BlobSection));
        }
        if (typeSpecRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0011, 0,
                checked((uint)typeSpecRows.Count), typeSpecBytes));
        }
        if (moduleRefRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0010, 0,
                checked((uint)moduleRefRows.Count), moduleRefBytes));
        }
        if (typeDefRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0013, 0,
                checked((uint)typeDefRows.Count), typeDefBytes));
        }
        if (layoutRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0015, 0,
                checked((uint)layoutRows.Count), layoutBytes));
        }
        if (exportRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                checked((uint)exportRows.Count), exportBytes));
        }
        if (importRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                checked((uint)importRows.Count), importBytes));
        }
        if (values.ConstantSourceCount != 0)
        {
            sections.Add(new TrRmSection(0x0030, 0,
                checked((uint)values.ConstantSourceCount),
                values.ConstantSourceSection));
            sections.Add(new TrRmSection(0x003F, 0,
                checked((uint)values.FrozenValueNodeCount),
                values.FrozenValueNodeSection));
        }
        if (enumRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0053, 0,
                checked((uint)enumRows.Count), enumDefBytes));
        }
        if (enumMemberRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0054, 0,
                checked((uint)enumMemberRows.Count), enumMemberBytes));
        }
        if (hasTargetProfile)
        {
            sections.Add(new TrRmSection(0x0048, 0, 1,
                targetProfileBytes));
        }

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(artifact, schema));
        return artifact;
    }

    private static byte GetStringEncoding(string text)
    {
        foreach (var character in text)
        {
            if (character > 0x7F)
            {
                return 1;
            }
        }

        return 0;
    }

    private static bool TryGetEnumTypeCode(PrimitiveType type,
        out byte typeCode)
        => TrRmConstantSourceValueValidator.TryGetIntegerTypeCode(type,
            out typeCode);
}
