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
        int DefinitionIndex,
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
        var hasSourceTypeExports = sourceTypeExports is { Count: > 0 };
        var hasStandardStructViews = !program.Structures.IsEmpty
            && program.Structures.All(static structure =>
                structure.StandardLayoutOnly
                && structure.StandardTypeOrigin is
                    BoundStandardTypeOrigin {
                        Provider.SchemaIdentity: var schemaIdentity,
                    }
                    && IsSelectedC1EnumClosureSchemaIdentity(
                        schemaIdentity));
        BoundSourceTypeProviderSnapshot? rootSnapshot = null;
        if ((sourceEnumExports is not null
                && !ReferenceEquals(sourceEnumExports.Program, program))
            || sourceGenericMethodExports?.Exports.IsEmpty == false
            || sourceClassExports?.Exports.IsEmpty == false
            || (hasSourceTypeExports && sourceEnumExports is null)
            || (sourceEnumExports is not null && hasStandardStructViews)
            || program.EntryPoint != -1 || !program.Functions.IsEmpty
            || (!program.Structures.IsEmpty && !hasSourceTypeExports
                && !hasStandardStructViews)
            || !program.Classes.IsEmpty
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
            || (!program.SourceTypeDeclarations.IsEmpty
                && !hasSourceTypeExports)
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
            || (sourceEnumExports is null
                && program.EnumDefinitions.Any(static definition =>
                    definition.Identity
                        is not BoundEnumDeclarationIdentity.Standard)))
        {
            throw Unsupported(
                "The bounded EN1 declaration route accepts mapped source enums or retained Standard enum views, plus mapped local non-generic struct closure; class and executable closure remain outside this writer.");
        }

        if (hasSourceTypeExports)
        {
            if (sourceEnumExports is null
                || program.SourceTypeDeclarations.IsDefault
                || program.SourceTypeDeclarations.Select(
                    static declaration => declaration.StructureIndex)
                    .Distinct().Count()
                    != program.SourceTypeDeclarations.Length)
            {
                throw new ArgumentException(
                    "EN1 struct exports require exact local source declaration identities.",
                    nameof(sourceTypeExports));
            }

            SemanticVerifier.VerifyLibraryReferences(program);
        }
        else if (hasStandardStructViews)
        {
            SemanticVerifier.VerifyLibraryReferences(program);
            foreach (var structure in program.Structures)
            {
                var origin = structure.StandardTypeOrigin
                    ?? throw Invalid(
                        "A mixed EN1 struct re-emission lost its TypeDef origin.");
                var provider = origin.Provider;
                var referenceRoot = origin.ReferenceRoot ?? provider;
                var hasImports = !origin.Expectations.IsDefaultOrEmpty;
                var sourceSchema = provider.SchemaIdentity;
                if (!StringComparer.Ordinal.Equals(
                        referenceRoot.ModuleIdentity, moduleIdentity)
                    || !IsSelectedC1EnumClosureSchemaIdentity(sourceSchema)
                    || !IsSelectedC1EnumClosureSchemaIdentity(
                        referenceRoot.SchemaIdentity)
                    || origin.DefinitionId >> 24 != 0x13
                    || (origin.DefinitionId & 0x00FF_FFFF) == 0
                    || origin.ExportKeys.IsDefault
                    || origin.SelectedExportKeys.IsDefault
                    || origin.ExpectedDeclaration is null
                    || (hasImports
                        && (origin.IsLookupRoot
                            || !origin.SelectedExportKeys.IsEmpty))
                    || (!hasImports && origin.IsLookupRoot
                        && origin.SelectedExportKeys.IsEmpty)
                    || (!hasImports && !origin.IsLookupRoot
                        && !origin.SelectedExportKeys.IsEmpty)
                    || origin.SelectedExportKeys.Any(key =>
                        !origin.ExportKeys.Contains(key,
                            StringComparer.Ordinal))
                    || (!hasImports
                        && !SameA2SourceTypeProviderSnapshot(provider,
                            referenceRoot))
                    || !structure.StandardLayoutOnly)
                {
                    throw Unsupported(
                        "Mixed EN1 struct re-emission requires exact local or directly imported TypeDef snapshots.");
                }

                if (rootSnapshot is not null
                    && !SameA2SourceTypeProviderSnapshot(rootSnapshot,
                        referenceRoot))
                {
                    throw Unsupported(
                        "Mixed EN1 struct re-emission cannot combine root Modules.");
                }

                if (origin.IsLookupRoot)
                {
                    rootSnapshot = referenceRoot;
                }

                var expected = origin.ExpectedDeclaration;
                var actual = structure with {
                    Fields = [.. structure.Fields.Select(
                        static field => field with {
                            StandardFieldOrigin = null,
                        })],
                    StandardTypeOrigin = null,
                    StandardLayoutOnly = false,
                };
                if (actual != expected)
                {
                    throw Unsupported(
                        "Mixed EN1 struct re-emission cannot weaken its retained declaration snapshot.");
                }
            }
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

        C1EnumClosureEmission CreateStandardEmission(
            int definitionIndex, BoundStandardEnumView view,
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

            return new C1EnumClosureEmission(definitionIndex,
                view.Namespace, view.Name, view.TypeDefFlags,
                view.UnderlyingType, view.EnumFlags,
                layouts.MoveToImmutable(), members.MoveToImmutable(),
                exportKeys, import);
        }

        if (sourceEnumExports is not null)
        {
            var sourceExportsByIndex = sourceEnumExports.Exports
                .ToDictionary(static export => export.EnumIndex);
            var sourceDefinitionCount = program.EnumDefinitions.Count(
                static definition => definition.Identity
                    is BoundEnumDeclarationIdentity.Source);
            if (sourceExportsByIndex.Count != sourceDefinitionCount)
            {
                throw new InvalidDataException(
                    "The EN1 source enum map does not cover the complete source declaration table.");
            }

            void AddFreshStandardEnumImports(int definitionIndex,
                BoundStandardEnumOccurrence occurrence)
            {
                var view = occurrence.Declaration;
                var provider = view.Site.Snapshot;
                if (!IsSelectedC1EnumClosureSchemaIdentity(
                        provider.SchemaIdentity)
                    || GetForeignInvocationSchema(provider.SchemaIdentity)
                        != schema
                    || provider.RuntimePolicy != (uint)program.RuntimePolicy)
                {
                    throw Unsupported(
                        "A fresh EN1 source declaration requires a selected Standard enum provider with the exact schema and runtime.");
                }

                var selectedEdges = new HashSet<(
                    string ProducerIdentity, string ExportKey,
                    uint TargetRequirement, uint RuntimePolicy)>();
                foreach (var expectation in occurrence.Expectations)
                {
                    var binding = expectation.Binding;
                    var expected = expectation.ExpectedDeclaration;
                    if (!BoundStandardEnumDeclarationContract
                            .SnapshotsMatch(binding.UsingModule,
                                expected.Site.Snapshot)
                        || binding.ExpectedDefinitionId
                            != expected.Site.DefinitionId
                        || !BoundStandardEnumDeclarationContract
                            .SnapshotsMatch(binding.Provider, provider)
                        || binding.ProviderDefinitionId
                            != view.Site.DefinitionId
                        || !BoundStandardEnumDeclarationContract
                            .ContractMatches(expected, view)
                        || !StringComparer.Ordinal.Equals(
                            binding.ProducerIdentity,
                            provider.ModuleIdentity)
                        || binding.TargetRequirement
                            != provider.TargetRequirement
                        || binding.RuntimePolicy != provider.RuntimePolicy
                        || !BoundEnumTypes.IsValidModuleDeclarationKey(
                            binding.ExportKey)
                        || !occurrence.ExportKeys.Contains(
                            binding.ExportKey, StringComparer.Ordinal))
                    {
                        throw Invalid(
                            "A fresh EN1 source enum Import lost its checked provider expectation.");
                    }

                    _ = selectedEdges.Add((binding.ProducerIdentity,
                        binding.ExportKey, binding.TargetRequirement,
                        binding.RuntimePolicy));
                    emittedEnums.Add(CreateStandardEmission(
                        definitionIndex, expected, [],
                        new C1EnumClosureImport(
                            binding.ProducerIdentity, binding.ExportKey,
                            binding.TargetRequirement,
                            binding.RuntimePolicy)));
                }

                foreach (var exportKey in occurrence.SelectedExportKeys)
                {
                    var edge = (provider.ModuleIdentity, exportKey,
                        provider.TargetRequirement, provider.RuntimePolicy);
                    if (!selectedEdges.Add(edge))
                    {
                        continue;
                    }

                    emittedEnums.Add(CreateStandardEmission(
                        definitionIndex, view, [],
                        new C1EnumClosureImport(provider.ModuleIdentity,
                            exportKey, provider.TargetRequirement,
                            provider.RuntimePolicy)));
                }

                if (selectedEdges.Count == 0)
                {
                    throw Unsupported(
                        "Fresh EN1 source emission cannot publish an unselected closure-only Standard enum without a retained Import edge.");
                }
            }

            for (var enumIndex = 0;
                enumIndex < program.EnumDefinitions.Length; enumIndex++)
            {
                var definition = program.EnumDefinitions[enumIndex];
                if (definition.Identity
                    is BoundEnumDeclarationIdentity.Standard standard)
                {
                    AddFreshStandardEnumImports(enumIndex,
                        standard.Value);
                    continue;
                }

                if (definition.Identity
                        is not BoundEnumDeclarationIdentity.Source
                    || !sourceExportsByIndex.TryGetValue(enumIndex,
                        out var sourceExport))
                {
                    throw Invalid(
                        "The EN1 source enum map no longer matches the checked declaration table.");
                }

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
                emittedEnums.Add(new C1EnumClosureEmission(enumIndex,
                    namespaceName,
                    typeName, 0x10u | visibility,
                    definition.UnderlyingType,
                    definition.IsFlags ? 1u : 0u,
                    layouts.MoveToImmutable(), members.MoveToImmutable(),
                    sourceExport.ExportKeys, null));
            }
        }
        else
        {
            if (hasStandardStructViews)
            {
                foreach (var structure in program.Structures)
                {
                    var origin = structure.StandardTypeOrigin!;
                    if (rootSnapshot is not null
                        && !SameA2SourceTypeProviderSnapshot(rootSnapshot,
                            origin.Provider))
                    {
                        throw Unsupported(
                            "Mixed EN1 struct and enum re-emission cannot combine root Modules.");
                    }
                    if (origin.IsLookupRoot)
                    {
                        rootSnapshot = origin.Provider;
                    }
                }
            }

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

            for (var definitionIndex = 0;
                definitionIndex < program.EnumDefinitions.Length;
                definitionIndex++)
            {
                var definition = program.EnumDefinitions[definitionIndex];
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

                    emittedEnums.Add(CreateStandardEmission(
                        definitionIndex, occurrence.Declaration,
                        occurrence.ExportKeys, null));
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

                    emittedEnums.Add(CreateStandardEmission(
                        definitionIndex, expectation.ExpectedDeclaration, [],
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

        var sourceStructExportsByIndex
            = new SortedDictionary<int, List<string>>();
        var sourceStructImportsByIndex
            = new Dictionary<int, List<C1EnumClosureImport>>();
        var sourceStructFieldExportsByIndex = new Dictionary<
            (int StructureIndex, int FieldIndex), List<string>>();
        var sourceStructDeclarationsByIndex
            = program.SourceTypeDeclarations.IsDefault
                ? new Dictionary<int, BoundSourceTypeDeclaration>()
                : program.SourceTypeDeclarations.ToDictionary(
                    static declaration => declaration.StructureIndex);
        bool IsSupportedSourceStruct(BoundStruct structure)
            => hasStandardStructViews
                ? structure.StandardLayoutOnly
                    && structure.StandardTypeOrigin is not null
                    && !structure.IsUnique
                    && structure.CleanupMethod is null
                    && structure.SourceGenericTypeDefinitionIndex < 0
                    && structure.LayoutKind == 0
                    && structure.BlittableTargetRequirement == 0
                    && structure.Packing == 0
                    && structure.DeclaredAlignment is null
                : IsC1M1SourceStructLayoutSupported(structure);

        bool TryGetEN1SourceStructFieldType(PrimitiveType type,
            out int structureIndex)
        {
            if (TryGetC1M1SourceStructFieldType(type,
                    sourceStructDeclarationsByIndex,
                    out structureIndex))
            {
                return true;
            }

            var candidate = (int)type - 100;
            if (hasStandardStructViews && candidate >= 0
                && candidate < program.Structures.Length
                && candidate < ManagedClassTypes.First - 100
                && !BoundEnumTypes.IsType(type))
            {
                structureIndex = candidate;
                return true;
            }

            structureIndex = -1;
            return false;
        }

        var usedExportKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var emittedEnum in emittedEnums)
        {
            foreach (var exportKey in emittedEnum.ExportKeys)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(exportKey)
                    || !usedExportKeys.Add(exportKey))
                {
                    throw new ArgumentException(
                        "EN1 TypeDef ExportKeys must be valid and Module-unique.",
                        nameof(sourceEnumExports));
                }
            }
        }

        if (hasSourceTypeExports)
        {
            foreach (var typeExport in sourceTypeExports!)
            {
                if (typeExport is null
                    || string.IsNullOrWhiteSpace(typeExport.Name)
                    || string.IsNullOrEmpty(typeExport.ExportKey)
                    || !BoundEnumTypes.IsValidModuleDeclarationKey(
                        typeExport.ExportKey)
                    || typeExport.GenericArity < -1
                    || typeExport.GenericArity > 0
                    || !typeExport.EnumMemberDeclarationKeys.IsDefaultOrEmpty
                    || !typeExport.InterfaceMethodExports.IsDefaultOrEmpty)
                {
                    throw new ArgumentException(
                        "EN1 source type exports select non-generic structs and cannot assign enum members or interface methods.",
                        nameof(sourceTypeExports));
                }

                var matchingDeclarations = program.SourceTypeDeclarations
                    .Where(declaration =>
                        StringComparer.Ordinal.Equals(declaration.Namespace,
                            typeExport.Namespace)
                        && StringComparer.Ordinal.Equals(declaration.Name,
                            typeExport.Name))
                    .ToArray();
                if (matchingDeclarations.Length != 1)
                {
                    throw new ArgumentException(
                        "An EN1 source TypeDef mapping must select one exact local struct declaration.",
                        nameof(sourceTypeExports));
                }

                var declaration = matchingDeclarations[0];
                if ((uint)declaration.StructureIndex
                        >= (uint)program.Structures.Length
                    || !ReferenceEquals(program.Structures[
                        declaration.StructureIndex],
                        declaration.DeclarationSnapshot)
                    || declaration.DeclarationSnapshot.IsPrivate
                    || declaration.DeclarationSnapshot.IsInternal
                    || !IsC1M1SourceStructLayoutSupported(
                        declaration.DeclarationSnapshot))
                {
                    throw new ArgumentException(
                        "EN1 source TypeDef mappings can select public, supported local structs only.",
                        nameof(sourceTypeExports));
                }

                if (!usedExportKeys.Add(typeExport.ExportKey))
                {
                    throw new ArgumentException(
                        "EN1 TypeDef and FieldDef ExportKeys must be Module-unique.",
                        nameof(sourceTypeExports));
                }

                if (!sourceStructExportsByIndex.TryGetValue(
                        declaration.StructureIndex, out var typeKeys))
                {
                    typeKeys = [];
                    sourceStructExportsByIndex.Add(
                        declaration.StructureIndex, typeKeys);
                }
                typeKeys.Add(typeExport.ExportKey);

                if (typeExport.FieldExports.IsDefault)
                {
                    continue;
                }

                var structure = declaration.DeclarationSnapshot;
                foreach (var fieldExport in typeExport.FieldExports)
                {
                    if (fieldExport is null
                        || string.IsNullOrEmpty(fieldExport.ExportKey)
                        || !BoundEnumTypes.IsValidModuleDeclarationKey(
                            fieldExport.ExportKey)
                        || !usedExportKeys.Add(fieldExport.ExportKey))
                    {
                        throw new ArgumentException(
                            "EN1 FieldDef ExportKeys must be valid and Module-unique.",
                            nameof(sourceTypeExports));
                    }

                    var fieldIndex = -1;
                    for (var index = 0;
                        index < structure.Fields.Length; index++)
                    {
                        if (StringComparer.Ordinal.Equals(
                                structure.Fields[index].Name,
                                fieldExport.Name))
                        {
                            if (fieldIndex >= 0)
                            {
                                throw new InvalidDataException(
                                    "An EN1 source struct has duplicate field names.");
                            }
                            fieldIndex = index;
                        }
                    }

                    if (fieldIndex < 0
                        || structure.Fields[fieldIndex].IsPrivate
                        || structure.Fields[fieldIndex].IsInternal)
                    {
                        throw new ArgumentException(
                            "An EN1 FieldDef mapping must select one public direct field of its mapped struct.",
                            nameof(sourceTypeExports));
                    }

                    if (!sourceStructFieldExportsByIndex.TryGetValue(
                            (declaration.StructureIndex, fieldIndex),
                            out var fieldKeys))
                    {
                        fieldKeys = [];
                        sourceStructFieldExportsByIndex.Add(
                            (declaration.StructureIndex, fieldIndex),
                            fieldKeys);
                    }
                    fieldKeys.Add(fieldExport.ExportKey);
                }
            }
        }

        if (hasStandardStructViews)
        {
            for (var structureIndex = 0;
                structureIndex < program.Structures.Length;
                structureIndex++)
            {
                var structure = program.Structures[structureIndex];
                var origin = structure.StandardTypeOrigin
                    ?? throw Invalid(
                        "A mixed EN1 struct re-emission lost its TypeDef origin.");
                if (rootSnapshot is null
                    || origin.ExportKeys.IsDefault
                    || origin.SelectedExportKeys.IsDefault
                    || origin.ExpectedDeclaration is null)
                {
                    throw Invalid(
                        "A mixed EN1 struct re-emission changed its provider or ExportKey snapshot.");
                }

                if (!origin.Expectations.IsDefaultOrEmpty)
                {
                    if (origin.IsLookupRoot
                        || !origin.SelectedExportKeys.IsEmpty)
                    {
                        throw Invalid(
                            "An EN1 imported struct TypeDef cannot become a source lookup root.");
                    }

                    var typeImports = new List<C1EnumClosureImport>();
                    uint expectedDefinitionId = 0;
                    foreach (var expectation in origin.Expectations)
                    {
                        var binding = expectation;
                        if (!BoundStandardEnumDeclarationContract
                                .SnapshotsMatch(binding.UsingModule,
                                    rootSnapshot))
                        {
                            continue;
                        }

                        if (!BoundStandardEnumDeclarationContract
                                .SnapshotsMatch(binding.Provider,
                                    origin.Provider)
                            || binding.ImportRow <= 0
                            || (binding.ExpectedDefinitionId >> 24) != 0x13
                            || (binding.ExpectedDefinitionId
                                & 0x00FF_FFFF) == 0
                            || binding.ProviderDefinitionId
                                != origin.DefinitionId
                            || !StringComparer.Ordinal.Equals(
                                binding.ProducerIdentity,
                                origin.Provider.ModuleIdentity)
                            || binding.TargetRequirement
                                != origin.Provider.TargetRequirement
                            || binding.RuntimePolicy
                                != origin.Provider.RuntimePolicy
                            || !BoundEnumTypes.IsValidModuleDeclarationKey(
                                binding.ExportKey)
                            || !origin.ExportKeys.Contains(
                                binding.ExportKey, StringComparer.Ordinal))
                        {
                            throw Invalid(
                                "An EN1 imported struct lost its root-owned TypeDef Import edge.");
                        }

                        if (expectedDefinitionId != 0
                            && expectedDefinitionId
                                != binding.ExpectedDefinitionId)
                        {
                            throw Invalid(
                                "One EN1 imported struct view combines different expected TypeDef rows.");
                        }
                        expectedDefinitionId
                            = binding.ExpectedDefinitionId;
                        typeImports.Add(new C1EnumClosureImport(
                            binding.ProducerIdentity, binding.ExportKey,
                            binding.TargetRequirement,
                            binding.RuntimePolicy));
                    }

                    if (typeImports.Count == 0)
                    {
                        throw Unsupported(
                            "Mixed EN1 struct re-emission requires at least one root-owned TypeDef Import edge.");
                    }

                    sourceStructImportsByIndex.Add(structureIndex,
                        typeImports);
                    sourceStructExportsByIndex.Add(structureIndex, []);
                }
                else
                {
                    if (!SameA2SourceTypeProviderSnapshot(
                            rootSnapshot, origin.Provider)
                        || (origin.DefinitionId & 0x00FF_FFFF) == 0)
                    {
                        throw Invalid(
                            "A local EN1 struct declaration changed its provider identity.");
                    }

                    var typeKeys = origin.ExportKeys.ToList();
                    foreach (var exportKey in typeKeys)
                    {
                        if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                                exportKey)
                            || !usedExportKeys.Add(exportKey))
                        {
                            throw Invalid(
                                "Mixed EN1 struct TypeDef ExportKeys are invalid or duplicated.");
                        }
                    }
                    sourceStructExportsByIndex.Add(structureIndex,
                        typeKeys);
                }

                for (var fieldIndex = 0;
                    fieldIndex < structure.Fields.Length; fieldIndex++)
                {
                    if (structure.Fields[fieldIndex].StandardFieldOrigin
                        is not BoundStandardFieldOrigin fieldOrigin)
                    {
                        continue;
                    }
                    if (!SameA2SourceTypeProviderSnapshot(rootSnapshot,
                            fieldOrigin.Provider)
                        || !origin.Expectations.IsDefaultOrEmpty
                        || (fieldOrigin.DefinitionId >> 24) != 0x14
                        || (fieldOrigin.DefinitionId & 0x00FF_FFFF) == 0
                        || fieldOrigin.IsLookupRoot
                        || fieldOrigin.ExportKeys.IsDefault
                        || fieldOrigin.ExportKeys.IsEmpty
                        || fieldOrigin.ExpectedDeclaration is null)
                    {
                        throw Invalid(
                            "A mixed EN1 struct FieldDef origin changed its provider or declaration identity.");
                    }

                    var actualField = structure.Fields[fieldIndex] with {
                            StandardFieldOrigin = null,
                        };
                    if (actualField != fieldOrigin.ExpectedDeclaration)
                    {
                        throw Unsupported(
                            "Mixed EN1 struct re-emission cannot weaken its retained FieldDef snapshot.");
                    }

                    foreach (var exportKey in fieldOrigin.ExportKeys)
                    {
                        if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                                exportKey)
                            || !usedExportKeys.Add(exportKey))
                        {
                            throw Invalid(
                                "Mixed EN1 struct FieldDef ExportKeys are invalid or duplicated.");
                        }
                    }
                    sourceStructFieldExportsByIndex.Add(
                        (structureIndex, fieldIndex),
                        [.. fieldOrigin.ExportKeys]);
                }
            }
        }

        var retainedStructureIndexes = new SortedSet<int>();
        var pendingStructureIndexes = new Queue<int>(
            sourceStructExportsByIndex.Keys);
        while (pendingStructureIndexes.TryDequeue(
            out var structureIndex))
        {
            if (!retainedStructureIndexes.Add(structureIndex))
            {
                continue;
            }

            var structure = program.Structures[structureIndex];
            if (!IsSupportedSourceStruct(structure))
            {
                throw Unsupported(
                    "EN1 source struct closure supports only non-generic local structs with auto layout and no ownership or cleanup contract.");
            }

            foreach (var field in structure.Fields)
            {
                if (TryGetEN1SourceStructFieldType(field.Type,
                        out var dependencyStructureIndex))
                {
                    if ((uint)dependencyStructureIndex
                            >= (uint)program.Structures.Length
                        || !IsSupportedSourceStruct(
                            program.Structures[dependencyStructureIndex]))
                    {
                        throw Unsupported(
                            "EN1 source struct fields require retained local non-generic struct declarations.");
                    }

                    if (!retainedStructureIndexes.Contains(
                            dependencyStructureIndex))
                    {
                        pendingStructureIndexes.Enqueue(
                            dependencyStructureIndex);
                    }
                    continue;
                }

                if ((field.Type is PrimitiveType.NativeInt
                        or PrimitiveType.NativeUInt)
                    && moduleTargetRequirement
                        != TrRmModuleTableReader
                            .WindowsX64NativeTargetRequirement)
                {
                    throw Unsupported(
                        "EN1 native-width struct fields require the selected Windows x64 target.");
                }

                if (BoundEnumTypes.IsType(field.Type))
                {
                    if ((uint)BoundEnumTypes.Index(field.Type)
                        >= (uint)program.EnumDefinitions.Length)
                    {
                        throw new InvalidDataException(
                            "An EN1 source struct field refers to an invalid enum identity.");
                    }
                    continue;
                }

                _ = GetC1M1SourceScalarTypeSpecReference(field.Type);
            }
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
            emittedEnums.Count + retainedStructureIndexes.Count);
        var typeDefNames = new List<(string Namespace, string Name)>(
            emittedEnums.Count + retainedStructureIndexes.Count);
        var layoutRows = new List<TrRmLayoutRow>(
            checked(emittedEnums.Count * 2
                + retainedStructureIndexes.Count));
        var fieldRows = new List<TrRmFieldDefRow>();
        var fieldNames = new List<string>();
        var layoutFieldRows = new List<TrRmLayoutFieldRow>();
        var enumRows = new List<TrRmEnumDefRow>(
            emittedEnums.Count);
        var enumMemberRows = new List<TrRmTypedEnumMemberDefRow>();
        var typeSpecRows = new List<ReadOnlyMemory<byte>>();
        var typeSpecByEmissionEnumIndex = new Dictionary<int, uint>();
        var typeSpecByBoundEnumIndex = new Dictionary<int, uint>();
        var typeDefRowByEnumIndex = new Dictionary<int, int>();
        var importRowByEnumIndex = new Dictionary<int, int>();
        var importRowByStructureIndex = new Dictionary<int, int>();
        var typeDefRowByStructure = retainedStructureIndexes
            .Select((structureIndex, ordinal) => (structureIndex,
                typeDefRow: emittedEnums.Count + ordinal + 1))
            .ToDictionary(static item => item.structureIndex,
                static item => item.typeDefRow);
        var typeSpecByStructure = new Dictionary<int, uint>();
        var exportRows = new List<TrRmExportRow>();
        var fieldExportRows = new List<TrRmExportRow>();
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

        uint FieldTypeSpecReference(PrimitiveType type)
        {
            if (TryGetEN1SourceStructFieldType(type,
                    out var structureIndex))
            {
                if (!typeDefRowByStructure.TryGetValue(structureIndex,
                        out var typeDefRow))
                {
                    throw new InvalidDataException(
                        "An EN1 source struct field refers to an unretained local struct.");
                }

                if (!typeSpecByStructure.TryGetValue(structureIndex,
                        out var typeSpecRow))
                {
                    typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                    var importRow = importRowByStructureIndex.TryGetValue(
                        structureIndex, out var selectedImportRow)
                        ? selectedImportRow : 0;
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                        importRow == 0 ? (byte)0x02 : (byte)0x03,
                        checked((uint)(importRow == 0
                            ? typeDefRow : importRow))));
                    typeSpecByStructure.Add(structureIndex, typeSpecRow);
                }

                return typeSpecRow;
            }

            if (BoundEnumTypes.IsType(type))
            {
                var enumIndex = BoundEnumTypes.Index(type);
                if (!typeDefRowByEnumIndex.TryGetValue(enumIndex,
                        out var typeDefRow))
                {
                    throw new InvalidDataException(
                        "An EN1 source struct field refers to an unretained local enum.");
                }

                if (!typeSpecByBoundEnumIndex.TryGetValue(enumIndex,
                        out var typeSpecRow))
                {
                    typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                    var importRow = importRowByEnumIndex.TryGetValue(
                        enumIndex, out var selectedImportRow)
                        ? selectedImportRow : 0;
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                        importRow == 0 ? (byte)0x02 : (byte)0x03,
                        checked((uint)(importRow == 0
                            ? typeDefRow : importRow))));
                    typeSpecByBoundEnumIndex.Add(enumIndex, typeSpecRow);
                }

                return typeSpecRow;
            }

            return GetC1M1SourceScalarTypeSpecReference(type);
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
            if ((uint)emittedEnum.DefinitionIndex
                    >= (uint)program.EnumDefinitions.Length)
            {
                throw Invalid(
                    "An EN1 enum emission lost its checked declaration index.");
            }
            _ = typeDefRowByEnumIndex.TryAdd(
                emittedEnum.DefinitionIndex, typeDefRow);
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
                _ = importRowByEnumIndex.TryAdd(
                    emittedEnum.DefinitionIndex, enumImportRow);
            }

            if (!emittedEnum.Members.IsEmpty)
            {
                var typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                    enumImportRow == 0 ? (byte)0x02 : (byte)0x03,
                    checked((uint)(enumImportRow == 0
                        ? typeDefRow : enumImportRow))));
                typeSpecByEmissionEnumIndex.Add(enumIndex, typeSpecRow);
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
                    constant, typeSpecByEmissionEnumIndex[enumIndex],
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

        foreach (var (structureIndex, importsForType) in
            sourceStructImportsByIndex.OrderBy(
                static item => item.Key))
        {
            var typeDefRow = typeDefRowByStructure[structureIndex];
            foreach (var typeImport in importsForType)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                        typeImport.ProducerIdentity)
                    || !BoundEnumTypes.IsValidModuleDeclarationKey(
                        typeImport.ExportKey))
                {
                    throw Invalid(
                        "A mixed EN1 struct Import has an invalid provider identity or ExportKey.");
                }

                var providerKey = (typeImport.ProducerIdentity,
                    typeImport.TargetRequirement, typeImport.RuntimePolicy);
                if (!moduleRefRowsByProvider.TryGetValue(providerKey,
                        out var moduleRefRow))
                {
                    moduleRefRow = moduleRefRows.Count + 1;
                    moduleRefRows.Add(new TrRmModuleRefRow(
                        AddString(typeImport.ProducerIdentity),
                        typeImport.TargetRequirement,
                        typeImport.RuntimePolicy));
                    moduleRefRowsByProvider.Add(providerKey, moduleRefRow);
                }

                importRows.Add(new TrRmImportRow(moduleRefRow,
                    AddString(typeImport.ExportKey),
                    0x1300_0000u | checked((uint)typeDefRow)));
                _ = importRowByStructureIndex.TryAdd(structureIndex,
                    importRows.Count);
            }
        }

        foreach (var structureIndex in retainedStructureIndexes)
        {
            var structure = program.Structures[structureIndex];
            string namespaceName;
            string typeName;
            if (sourceStructDeclarationsByIndex.TryGetValue(structureIndex,
                    out var declaration))
            {
                namespaceName = declaration.Namespace;
                typeName = declaration.Name;
            }
            else
            {
                var separator = structure.Name.LastIndexOf(
                    '.', StringComparison.Ordinal);
                namespaceName = separator < 0 ? "" : structure.Name[..separator];
                typeName = separator < 0 ? structure.Name
                    : structure.Name[(separator + 1)..];
            }

            var typeDefRow = typeDefRowByStructure[structureIndex];
            var typeDefFlags = (structure.IsPrivate ? 0u
                : structure.IsInternal ? 1u : 2u) | 0x04u;
            var fieldFirst = structure.Fields.IsEmpty
                ? 0 : fieldRows.Count + 1;
            var layoutFieldFirst = structure.Fields.IsEmpty
                ? 0 : layoutFieldRows.Count + 1;
            var fieldNamesForType = new HashSet<string>(
                StringComparer.Ordinal);
            for (var fieldOrdinal = 0;
                fieldOrdinal < structure.Fields.Length; fieldOrdinal++)
            {
                var field = structure.Fields[fieldOrdinal];
                if (field is null || (field.IsPrivate && field.IsInternal)
                    || field.RequiresUnsafeCaller
                    || field.BackingPropertyName is not null
                    || !fieldNamesForType.Add(field.Name))
                {
                    throw Unsupported(
                        "EN1 source struct FieldDefs cannot discard conflicting access, unsafe-caller or backing-property facts.");
                }

                var visibility = field.IsPrivate ? 0u
                    : field.IsInternal ? 1u : 2u;
                var fieldFlags = visibility
                    | (field.IsMutable ? 0x04u : 0u);
                var fieldDefRow = fieldRows.Count + 1;
                fieldRows.Add(new TrRmFieldDefRow(typeDefRow, 0,
                    FieldTypeSpecReference(field.Type), fieldFlags));
                fieldNames.Add(field.Name);
                layoutFieldRows.Add(new TrRmLayoutFieldRow(
                    layoutRows.Count + 1, fieldOrdinal, fieldDefRow));

                if (sourceStructFieldExportsByIndex.TryGetValue(
                        (structureIndex, fieldOrdinal),
                        out var fieldExportKeys))
                {
                    foreach (var exportKey in fieldExportKeys)
                    {
                        fieldExportRows.Add(new TrRmExportRow(
                            0x1400_0000u | checked((uint)fieldDefRow),
                            AddString(exportKey)));
                    }
                }
            }

            var layoutRow = layoutRows.Count + 1;
            typeDefRows.Add(new TrRmTypeDefRow(0, 0, typeDefFlags,
                fieldFirst, structure.Fields.Length, 0, 0,
                checked((uint)layoutRow), 1, 0, 0));
            typeDefNames.Add((namespaceName, typeName));
            layoutRows.Add(new TrRmLayoutRow(typeDefRow, 0, 0, 0, 0, 0,
                layoutFieldFirst, structure.Fields.Length));

            if (sourceStructExportsByIndex.TryGetValue(structureIndex,
                    out var exportKeys))
            {
                foreach (var exportKey in exportKeys)
                {
                    exportRows.Add(new TrRmExportRow(
                        0x1300_0000u | checked((uint)typeDefRow),
                        AddString(exportKey)));
                }
            }
        }
        exportRows.AddRange(fieldExportRows);

        foreach (var typeName in typeDefNames)
        {
            _ = AddString(typeName.Namespace);
            _ = AddString(typeName.Name);
        }
        foreach (var fieldName in fieldNames)
        {
            _ = AddString(fieldName);
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
        for (var fieldIndex = 0; fieldIndex < fieldRows.Count; fieldIndex++)
        {
            fieldRows[fieldIndex] = fieldRows[fieldIndex] with {
                NameStringRow = stringRowsByText[fieldNames[fieldIndex]],
            };
        }

        var featureRows = new List<TrRmFeatureRow>();
        if (enumRows.Count != 0)
        {
            featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
            featureRows.Add(new TrRmFeatureRow(18, 1));
        }
        if (sourceStructFieldExportsByIndex.Count != 0)
        {
            featureRows.Add(TrRmFeatureRow.LayoutQueries);
        }
        if (hasBlittableEnum)
        {
            featureRows.Add(TrRmFeatureRow.NativeBoundaryContracts);
        }
        featureRows.Sort(static (left, right)
            => left.FeatureId.CompareTo(right.FeatureId));
        var targetProfileCount = hasTargetProfile ? 1 : 0;
        var totalRows = 1L + stringRows.Count + typeDefRows.Count
            + moduleRefRows.Count + importRows.Count + fieldRows.Count
            + layoutRows.Count + layoutFieldRows.Count + enumRows.Count
            + enumMemberRows.Count + typeSpecRows.Count + exportRows.Count
            + featureRows.Count + targetProfileCount + valueBuilder.BlobCount
            + valueBuilder.ConstantSourceCount
            + valueBuilder.FrozenValueNodeCount;
        if (stringRows.Count > 0xFF_FFFF
            || typeDefRows.Count > 0xFF_FFFF
            || moduleRefRows.Count > 0xFF_FFFF
            || importRows.Count > 0xFF_FFFF
            || fieldRows.Count > 0xFF_FFFF
            || layoutRows.Count > 0xFF_FFFF
            || layoutFieldRows.Count > 0xFF_FFFF
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
                fieldRows.Count, 0, layoutRows.Count, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            typeDefRows.Count, strings, modules, fieldRows.Count, 0,
            layoutRows.Count, 0, schema);
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
                targetProfiles, layoutFieldRows.Count);
        var layouts = new TrRmLayoutTableReader(layoutBytes,
            layoutRows.Count, typeDefs, targetProfiles,
            layoutFieldRows.Count);
        var typeSpecBytes = typeSpecRows.Count == 0 ? []
            : TrRmTypeSpecTableWriter.Encode(typeSpecRows,
                typeDefRows.Count, importRows.Count, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, typeDefRows.Count, importRows.Count, 0, 0,
            schema);
        var fieldBytes = fieldRows.Count == 0 ? []
            : TrRmFieldDefTableWriter.Encode(fieldRows, strings, typeDefs,
                typeSpecs, schema);
        var fields = fieldRows.Count == 0 ? default
            : new TrRmFieldDefTableReader(fieldBytes, fieldRows.Count,
                strings, typeDefs, typeSpecs, schema);
        var layoutFieldBytes = layoutFieldRows.Count == 0 ? []
            : TrRmLayoutFieldTableWriter.Encode(layoutFieldRows, layouts,
                typeDefs, fields);
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
                default, default, imports, default, default, fields,
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
        if (fieldRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0014, 0,
                checked((uint)fieldRows.Count), fieldBytes));
        }
        if (layoutRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0015, 0,
                checked((uint)layoutRows.Count), layoutBytes));
        }
        if (layoutFieldRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0023, 0,
                checked((uint)layoutFieldRows.Count), layoutFieldBytes));
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
