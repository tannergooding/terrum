using System;
using System.Buffers.Binary;
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
        uint RuntimePolicy, int RootImportRow = 0);

    private sealed record C1EnumClosureStandardClassEmission(
        BoundStandardClassTypeView View, ImmutableArray<string> ExportKeys,
        C1EnumClosureImport? Import, int RootTypeDefRow,
        ImmutableArray<BoundStandardClassFieldExpectation>
            FieldExpectations);

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
        C1EnumClosureImport? Import, int RootImportRow = 0);

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
        var standardClassDeclarations = program.StandardClassDeclarations;
        var hasStandardClassDeclarations
            = !standardClassDeclarations.IsEmpty;
        var standardClassIdentities = standardClassDeclarations.Select(
                static declaration => declaration.Declaration.Identity)
            .ToHashSet();
        var standardClassExpectedIdentities = standardClassDeclarations
            .SelectMany(static declaration => declaration.Expectations)
            .Select(static expectation =>
                expectation.ExpectedDeclaration.Identity)
            .ToHashSet();
        var standardStructureIdentities
            = new HashSet<BoundGenericNominalTypeIdentity>();
        foreach (var structure in program.Structures)
        {
            if (structure.StandardTypeOrigin is not
                BoundStandardTypeOrigin origin)
            {
                continue;
            }

            _ = standardStructureIdentities.Add(
                new BoundGenericNominalTypeIdentity(
                    origin.Provider.ModuleIdentity,
                    origin.Provider.ModuleIdentity, origin.DefinitionId));
            foreach (var expectation in origin.Expectations)
            {
                _ = standardStructureIdentities.Add(
                    new BoundGenericNominalTypeIdentity(
                        expectation.UsingModule.ModuleIdentity,
                        expectation.UsingModule.ModuleIdentity,
                        expectation.ExpectedDefinitionId));
            }
        }
        var standardClassClosureIsValid
            = standardClassDeclarations.IsEmpty
                ? program.GenericNominalTypeDefinitions.IsEmpty
                : BoundStandardClassDeclarationContract.IsValidTable(
                        standardClassDeclarations,
                        program.GenericNominalTypeDefinitions)
                    && program.GenericNominalTypeDefinitions.All(
                        definition =>
                            definition.Formals.IsEmpty
                            && (definition.NominalKind == 2
                                ? standardClassIdentities.Contains(
                                    definition.Identity)
                                    || standardClassExpectedIdentities
                                        .Contains(definition.Identity)
                                : definition.NominalKind == 1
                                    && standardStructureIdentities.Contains(
                                        definition.Identity)));
        var standardClassTypeInstancesAreValid
            = program.GenericNominalTypeInstances.IsEmpty
                || (hasStandardClassDeclarations
                    && program.GenericNominalTypeInstances.All(instance =>
                        instance.Arguments.IsEmpty
                        && instance.TypeSpecReference != 0
                        && StringComparer.Ordinal.Equals(
                            instance.TypeSpecProviderIdentity,
                            moduleIdentity)
                        && instance.TypeSpecForm
                            is 0x02 or 0x03 or 0x06
                        && standardClassIdentities.Contains(
                            instance.Definition)));
        BoundSourceTypeProviderSnapshot? rootSnapshot = null;
        if ((sourceEnumExports is not null
                && !ReferenceEquals(sourceEnumExports.Program, program))
            || (sourceClassExports is not null
                && !ReferenceEquals(sourceClassExports.Program, program))
            || sourceGenericMethodExports?.Exports.IsEmpty == false
            || (hasSourceTypeExports && sourceEnumExports is null)
            || (sourceEnumExports is not null && hasStandardStructViews)
            || (hasStandardClassDeclarations
                && (sourceClassExports is not null
                    || sourceEnumExports is not null
                    || hasSourceTypeExports))
            || program.EntryPoint != -1 || !program.Functions.IsEmpty
            || (!program.Structures.IsEmpty && !hasSourceTypeExports
                && !hasStandardStructViews
                && sourceClassExports?.Exports.IsEmpty != false)
            || (!program.Classes.IsEmpty && sourceClassExports is null)
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
            || (program.StandardFieldProviderClosure is not null
                && !hasStandardStructViews)
            || (program.StandardQuerySourceAssociation is not null
                && !hasStandardStructViews)
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
                && !hasSourceTypeExports
                && sourceClassExports?.Exports.IsEmpty != false)
            || !program.SourceGenericTypeDefinitions.IsEmpty
            || !program.SourceGenericTypeInstances.IsEmpty
            || !standardClassTypeInstancesAreValid
            || !program.GenericMethodDefinitions.IsEmpty
            || !program.GenericMethodSignatures.IsEmpty
            || !program.GenericMethodOperatorProtocols.IsEmpty
            || !standardClassClosureIsValid
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
                "The bounded EN1 declaration route accepts mapped source enums or retained Standard enum views, with local class and non-generic struct declaration closure; executable class members and Bodies remain outside this writer.");
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
        else if (sourceClassExports is not null)
        {
            SemanticVerifier.VerifyLibraryReferences(program);
        }
        else if (hasStandardClassDeclarations)
        {
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
            C1EnumClosureImport? import, int rootImportRow = 0)
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
                exportKeys, import, rootImportRow);
        }

        var standardClassEmissions
            = new List<C1EnumClosureStandardClassEmission>();
        if (hasStandardClassDeclarations)
        {
            if (schema.UsesPropertyInitializedState())
            {
                throw Unsupported(
                    "EN1 class re-emission does not retain PI1 property and initialized-state declarations.");
            }

            var classRootProviders
                = new List<BoundSourceTypeProviderSnapshot>();
            foreach (var declaration in standardClassDeclarations)
            {
                if (declaration.Expectations.IsEmpty
                    && StringComparer.Ordinal.Equals(
                        declaration.Declaration.Provider.ModuleIdentity,
                        moduleIdentity))
                {
                    classRootProviders.Add(
                        declaration.Declaration.Provider);
                }

                foreach (var expectation in declaration.Expectations)
                {
                    if (StringComparer.Ordinal.Equals(
                            expectation.Binding.UsingModule.ModuleIdentity,
                            moduleIdentity))
                    {
                        classRootProviders.Add(
                            expectation.Binding.UsingModule);
                    }
                }
            }

            foreach (var provider in classRootProviders)
            {
                if (!IsSelectedC1EnumClosureSchemaIdentity(
                        provider.SchemaIdentity)
                    || GetForeignInvocationSchema(provider.SchemaIdentity)
                        != schema
                    || !StringComparer.Ordinal.Equals(
                        provider.ModuleIdentity, moduleIdentity)
                    || provider.TargetRequirement
                        != moduleTargetRequirement
                    || provider.RuntimePolicy != (uint)program.RuntimePolicy
                    || (rootSnapshot is not null
                        && !SameA2SourceTypeProviderSnapshot(rootSnapshot,
                            provider)))
                {
                    throw Unsupported(
                        "EN1 class re-emission requires one exact selected root Module with matching target and runtime.");
                }

                rootSnapshot = provider;
            }

            foreach (var declaration in standardClassDeclarations)
            {
                var classView = declaration.Declaration;
                if (declaration.Expectations.IsEmpty)
                {
                    if (rootSnapshot is null
                        || !SameA2SourceTypeProviderSnapshot(rootSnapshot,
                            classView.Provider))
                    {
                        continue;
                    }

                    if ((classView.Identity.DefinitionId >> 24) != 0x13
                        || (classView.Identity.DefinitionId
                            & 0x00FF_FFFF) == 0)
                    {
                        throw Invalid(
                            "An EN1 class root has an invalid TypeDef identity.");
                    }

                    standardClassEmissions.Add(
                        new C1EnumClosureStandardClassEmission(
                            classView, declaration.ExportKeys, null,
                            checked((int)(classView.Identity.DefinitionId
                                & 0x00FF_FFFF)), []));
                    continue;
                }

                foreach (var expectation in declaration.Expectations)
                {
                    var binding = expectation.Binding;
                    if (rootSnapshot is null)
                    {
                        continue;
                    }
                    if (!SameA2SourceTypeProviderSnapshot(
                            rootSnapshot, binding.UsingModule))
                    {
                        continue;
                    }

                    var expectedView = expectation.ExpectedDeclaration;
                    if ((binding.ExpectedDefinitionId >> 24) != 0x13
                        || (binding.ExpectedDefinitionId
                            & 0x00FF_FFFF) == 0
                        || binding.ImportRow <= 0
                        || !SameA2SourceTypeProviderSnapshot(rootSnapshot!,
                            expectedView.Provider)
                        || !SameA2SourceTypeProviderSnapshot(
                            classView.Provider, binding.Provider)
                        || binding.ProviderDefinitionId
                            != classView.Identity.DefinitionId)
                    {
                        throw Invalid(
                            "An EN1 class TypeDef Import lost its expected or provider identity.");
                    }

                    standardClassEmissions.Add(
                        new C1EnumClosureStandardClassEmission(
                            expectedView, [], new C1EnumClosureImport(
                                binding.ProducerIdentity, binding.ExportKey,
                                binding.TargetRequirement,
                                binding.RuntimePolicy,
                                binding.ImportRow),
                            checked((int)(binding.ExpectedDefinitionId
                                & 0x00FF_FFFF),
                            expectation.FieldExpectations));
                }
            }

            standardClassEmissions.Sort(static (left, right)
                => left.RootTypeDefRow.CompareTo(right.RootTypeDefRow));
            for (var index = 1;
                index < standardClassEmissions.Count; index++)
            {
                if (standardClassEmissions[index - 1].RootTypeDefRow
                    == standardClassEmissions[index].RootTypeDefRow)
                {
                    throw Invalid(
                        "EN1 class re-emission contains duplicate root TypeDef views.");
                }
            }
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
                            binding.RuntimePolicy),
                        binding.ImportRow));
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
        var rootTypeDefRowByStructureIndex
            = new Dictionary<int, int>();
        var sourceStructFieldExportsByIndex = new Dictionary<
            (int StructureIndex, int FieldIndex), List<string>>();
        var sourceStructFieldImportsByIndex = new Dictionary<
            (int StructureIndex, int FieldIndex),
            List<C1EnumClosureImport>>();
        var sourceStructDeclarationsByIndex
            = program.SourceTypeDeclarations.IsDefault
                ? new Dictionary<int, BoundSourceTypeDeclaration>()
                : program.SourceTypeDeclarations.ToDictionary(
                    static declaration => declaration.StructureIndex);
        var usedExportKeys = new HashSet<string>(StringComparer.Ordinal);
        var sourceClassExportsByIndex
            = new SortedDictionary<int, List<string>>();
        var sourceClassFieldExportsByIndex = new Dictionary<
            (int ClassIndex, int FieldIndex), List<string>>();
        var retainedClassIndexes = new SortedSet<int>();
        var sourceClassAssociation = program.SourceClassProgramAssociation;
        var localClassIndexes = sourceClassAssociation is null
            || sourceClassAssociation.Declarations.IsDefault
                ? new HashSet<int>()
                : sourceClassAssociation.Declarations.Select(
                    static declaration => declaration.ClassIndex)
                    .ToHashSet();
        if (sourceClassExports is not null)
        {
            if (sourceClassAssociation is null
                || !ReferenceEquals(sourceClassAssociation.Program, program)
                || sourceClassAssociation.Declarations.IsDefault)
            {
                throw new ArgumentException(
                    "EN1 class exports require exact compiler-owned source class declarations.",
                    nameof(sourceClassExports));
            }

            foreach (var classExport in sourceClassExports.Exports)
            {
                var classIndex = classExport.Declaration.ClassIndex;
                if ((uint)classIndex >= (uint)program.Classes.Length
                    || !localClassIndexes.Contains(classIndex)
                    || !ReferenceEquals(program.Classes[classIndex],
                        classExport.Declaration.DeclarationSnapshot)
                    || !BoundEnumTypes.IsValidModuleDeclarationKey(
                        classExport.ExportKey)
                    || !usedExportKeys.Add(classExport.ExportKey))
                {
                    throw new ArgumentException(
                        "An EN1 class mapping does not identify one exact local declaration with a unique ExportKey.",
                        nameof(sourceClassExports));
                }

                if (!sourceClassExportsByIndex.TryGetValue(classIndex,
                        out var classExportKeys))
                {
                    classExportKeys = [];
                    sourceClassExportsByIndex.Add(classIndex,
                        classExportKeys);
                }
                classExportKeys.Add(classExport.ExportKey);

                foreach (var memberExport in classExport.MemberExports)
                {
                    if (memberExport.Kind != SourceClassMemberKind.Field)
                    {
                        throw Unsupported(
                            "The bounded EN1 class route emits FieldDefs only; constructors, methods and properties require their complete Body closure.");
                    }

                    var fields = program.Classes[classIndex].Fields;
                    if ((uint)memberExport.FieldIndex >= (uint)fields.Length)
                    {
                        throw new ArgumentException(
                            "An EN1 class FieldDef ExportKey must select one public direct field and be Module-unique.",
                            nameof(sourceClassExports));
                    }

                    var field = fields[memberExport.FieldIndex];
                    if (field is null
                        || field.IsPrivate || field.IsInternal
                        || !BoundEnumTypes.IsValidModuleDeclarationKey(
                            memberExport.ExportKey)
                        || !usedExportKeys.Add(memberExport.ExportKey))
                    {
                        throw new ArgumentException(
                            "An EN1 class FieldDef ExportKey must select one public direct field and be Module-unique.",
                            nameof(sourceClassExports));
                    }

                    if (!sourceClassFieldExportsByIndex.TryGetValue(
                            (classIndex, memberExport.FieldIndex),
                            out var fieldKeys))
                    {
                        fieldKeys = [];
                        sourceClassFieldExportsByIndex.Add(
                            (classIndex, memberExport.FieldIndex), fieldKeys);
                    }
                    fieldKeys.Add(memberExport.ExportKey);
                }
            }
        }

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
                            binding.RuntimePolicy, binding.ImportRow));
                    }

                    if (typeImports.Count == 0)
                    {
                        throw Unsupported(
                            "Mixed EN1 struct re-emission requires at least one root-owned TypeDef Import edge.");
                    }

                    rootTypeDefRowByStructureIndex.Add(structureIndex,
                        checked((int)(expectedDefinitionId
                            & 0x00FF_FFFF)));
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
                    rootTypeDefRowByStructureIndex.Add(structureIndex,
                        checked((int)(origin.DefinitionId
                            & 0x00FF_FFFF)));
                }

                for (var fieldIndex = 0;
                    fieldIndex < structure.Fields.Length; fieldIndex++)
                {
                    if (structure.Fields[fieldIndex].StandardFieldOrigin
                        is not BoundStandardFieldOrigin fieldOrigin)
                    {
                        continue;
                    }
                    if ((fieldOrigin.DefinitionId >> 24) != 0x14
                        || (fieldOrigin.DefinitionId & 0x00FF_FFFF) == 0
                        || fieldOrigin.IsLookupRoot
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

                    if (SameA2SourceTypeProviderSnapshot(rootSnapshot,
                            fieldOrigin.Provider))
                    {
                        if (!origin.Expectations.IsDefaultOrEmpty
                            || fieldOrigin.ExportKeys.IsDefault
                            || fieldOrigin.ExportKeys.IsEmpty)
                        {
                            throw Invalid(
                                "A root-owned FieldDef Export has an invalid local owner or key set.");
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
                    else if (!origin.Expectations.IsDefaultOrEmpty
                        && SameA2SourceTypeProviderSnapshot(
                            origin.Provider, fieldOrigin.Provider)
                        && !fieldOrigin.ExportKeys.IsDefaultOrEmpty
                        && fieldOrigin.SelectedExportKeys.IsDefaultOrEmpty)
                    {
                        continue;
                    }
                    else
                    {
                        throw Invalid(
                            "An EN1 FieldDef origin is neither a local Export nor a provider Import.");
                    }
                }
            }
        }

        BoundStandardFieldProviderClosure? fieldProviderClosure
            = program.StandardFieldProviderClosure;
        if (program.StandardQuerySourceAssociation is
            BoundStandardQuerySourceAssociation fieldAssociation)
        {
            if (fieldAssociation.ProviderClosures.IsDefaultOrEmpty
                || fieldAssociation.ProviderClosures.Length != 1
                || (fieldProviderClosure is not null
                    && !ReferenceEquals(fieldProviderClosure,
                        fieldAssociation.ProviderClosures[0])))
            {
                throw Unsupported(
                    "Mixed EN1 struct re-emission requires one exact FieldDef provider closure.");
            }

            fieldProviderClosure ??= fieldAssociation.ProviderClosures[0];
        }

        if (fieldProviderClosure is not null)
        {
            var selectedRootSnapshot = rootSnapshot
                ?? throw Invalid(
                    "An EN1 FieldDef provider closure has no selected root Module.");
            if (!hasStandardStructViews
                || !IsSelectedC1EnumClosureSchemaIdentity(
                    fieldProviderClosure.SchemaIdentity)
                || !StringComparer.Ordinal.Equals(
                    fieldProviderClosure.SchemaIdentity,
                    selectedRootSnapshot.SchemaIdentity)
                || !StringComparer.Ordinal.Equals(
                    fieldProviderClosure.RootModuleIdentity,
                    moduleIdentity)
                || fieldProviderClosure.RootModuleRow
                    != selectedRootSnapshot.ModuleRow
                || fieldProviderClosure.TargetRequirement
                    != moduleTargetRequirement
                || fieldProviderClosure.RuntimePolicy
                    != (uint)program.RuntimePolicy
                || fieldProviderClosure.Artifacts.IsDefaultOrEmpty
                || fieldProviderClosure.Modules.IsDefaultOrEmpty
                || fieldProviderClosure.FieldEdges.IsDefault
                || fieldProviderClosure.Artifacts[0].ArtifactIndex != 0
                || !StringComparer.Ordinal.Equals(
                    fieldProviderClosure.Artifacts[0].SchemaIdentity,
                    selectedRootSnapshot.SchemaIdentity)
                || !fieldProviderClosure.Artifacts[0].ArtifactSnapshot
                    .AsSpan().SequenceEqual(
                        selectedRootSnapshot.ArtifactSnapshot.AsSpan()))
            {
                throw Unsupported(
                    "Mixed EN1 struct re-emission cannot change its FieldDef provider root or artifact snapshot.");
            }

            var rootTables = ValidateForeignInvocationArtifact(
                CreateForeignInvocationIndex(
                    fieldProviderClosure.Artifacts[0].ArtifactSnapshot
                        .ToArray(),
                    fieldProviderClosure.SchemaIdentity),
                fieldProviderClosure.SchemaIdentity, null,
                createA3ResourceLedger: false);
            var structureIndexByExpectedTypeDef
                = new Dictionary<int, int>(program.Structures.Length);
            for (var structureIndex = 0;
                structureIndex < program.Structures.Length;
                structureIndex++)
            {
                var origin = program.Structures[structureIndex]
                    .StandardTypeOrigin
                    ?? throw Invalid(
                        "An EN1 FieldDef provider closure has an unoriginated struct.");
                var rootDefinitionIds = origin.Expectations
                    .Where(expectation =>
                        BoundStandardEnumDeclarationContract.SnapshotsMatch(
                            expectation.UsingModule,
                            selectedRootSnapshot))
                    .Select(static expectation =>
                        expectation.ExpectedDefinitionId)
                    .Distinct()
                    .ToArray();
                uint rootDefinitionId;
                if (rootDefinitionIds.Length != 0)
                {
                    if (rootDefinitionIds.Length != 1)
                    {
                        throw Invalid(
                            "An EN1 struct origin has multiple root TypeDef stubs.");
                    }
                    rootDefinitionId = rootDefinitionIds[0];
                }
                else if (SameA2SourceTypeProviderSnapshot(origin.Provider,
                             selectedRootSnapshot))
                {
                    rootDefinitionId = origin.DefinitionId;
                }
                else
                {
                    throw Invalid(
                        "An EN1 struct origin lost its root TypeDef identity.");
                }

                if ((rootDefinitionId >> 24) != 0x13
                    || (rootDefinitionId & 0x00FF_FFFF) == 0
                    || !structureIndexByExpectedTypeDef.TryAdd(
                        checked((int)(rootDefinitionId & 0x00FF_FFFF)),
                        structureIndex))
                {
                    throw Invalid(
                        "An EN1 struct origin has an invalid or duplicate root TypeDef identity.");
                }
            }

            foreach (var edge in fieldProviderClosure.FieldEdges)
            {
                if (edge.ConsumerArtifactIndex != 0
                    || edge.ConsumerModuleRow
                        != fieldProviderClosure.RootModuleRow)
                {
                    continue;
                }

                if ((edge.ExpectedDefinitionId >> 24) != 0x14
                    || (edge.ActualDefinitionId >> 24) != 0x14
                    || edge.ImportRow <= 0
                    || edge.ModuleRefRow <= 0
                    || edge.OwnerImportRow <= 0
                    || !structureIndexByExpectedTypeDef.TryGetValue(
                        edge.ExpectedOwnerTypeDefRow,
                        out var structureIndex))
                {
                    throw Invalid(
                        "An EN1 FieldDef provider edge has an invalid root or definition identity.");
                }

                var expectedFieldRow = checked((int)(
                    edge.ExpectedDefinitionId & 0x00FF_FFFF));
                var (fieldStart, fieldEnd) = rootTables.TypeDefs
                    .GetFieldRange(edge.ExpectedOwnerTypeDefRow);
                if (expectedFieldRow < fieldStart
                    || expectedFieldRow >= fieldEnd
                    || rootTables.Fields.GetOwnerTypeDef(expectedFieldRow)
                        != edge.ExpectedOwnerTypeDefRow
                    || rootTables.Imports
                        .GetExpectedDefinitionForImportRow(edge.OwnerImportRow)
                        != (0x1300_0000u
                            | checked((uint)edge.ExpectedOwnerTypeDefRow))
                    || rootTables.Imports
                        .GetExpectedDefinitionForImportRow(edge.ImportRow)
                        != edge.ExpectedDefinitionId)
                {
                    throw Invalid(
                        "An EN1 FieldDef provider edge lost its expected owner or stub.");
                }

                var structure = program.Structures[structureIndex];
                var fieldOrdinal = expectedFieldRow - fieldStart;
                if ((uint)fieldOrdinal >= (uint)structure.Fields.Length
                    || structure.StandardTypeOrigin
                        is not BoundStandardTypeOrigin typeOrigin
                    || (typeOrigin.DefinitionId >> 24) != 0x13
                    || typeOrigin.DefinitionId
                        != (0x1300_0000u
                            | checked((uint)edge.ProviderTypeDefRow))
                    || !StringComparer.Ordinal.Equals(
                        typeOrigin.Provider.ModuleIdentity,
                        edge.ProducerIdentity)
                    || typeOrigin.Provider.TargetRequirement
                        != edge.TargetRequirement
                    || typeOrigin.Provider.RuntimePolicy
                        != edge.RuntimePolicy
                    || !typeOrigin.Expectations.Any(expectation =>
                        expectation.ImportRow == edge.OwnerImportRow
                        && expectation.ExpectedDefinitionId
                            == (0x1300_0000u
                                | checked((uint)edge.ExpectedOwnerTypeDefRow))
                        && expectation.ProviderDefinitionId
                            == (0x1300_0000u
                                | checked((uint)edge.ProviderTypeDefRow))
                        && BoundStandardEnumDeclarationContract
                            .SnapshotsMatch(expectation.UsingModule,
                                selectedRootSnapshot)))
                {
                    throw Invalid(
                        "An EN1 FieldDef provider edge does not match its imported owner.");
                }

                var field = structure.Fields[fieldOrdinal];
                var providerArtifact
                    = fieldProviderClosure.Artifacts.FirstOrDefault(
                        artifact => artifact.ArtifactIndex
                            == edge.ProviderArtifactIndex);
                var providerModule
                    = fieldProviderClosure.Modules.FirstOrDefault(
                        module => module.ArtifactIndex
                                == edge.ProviderArtifactIndex
                            && module.ModuleRow == edge.ProviderModuleRow);
                var fieldBinding = rootTables.Imports
                    .GetBindingForImportRow(edge.ImportRow);
                if (field.StandardFieldOrigin
                    is not BoundStandardFieldOrigin fieldOrigin
                    || providerArtifact is null
                    || providerModule is null
                    || !StringComparer.Ordinal.Equals(
                        providerArtifact.SchemaIdentity,
                        fieldOrigin.Provider.SchemaIdentity)
                    || !providerArtifact.ArtifactSnapshot.AsSpan()
                        .SequenceEqual(
                            fieldOrigin.Provider.ArtifactSnapshot.AsSpan())
                    || providerModule.Identity
                        != fieldOrigin.Provider.ModuleIdentity
                    || providerModule.ModuleRow
                        != fieldOrigin.Provider.ModuleRow
                    || providerModule.TargetRequirement
                        != fieldOrigin.Provider.TargetRequirement
                    || providerModule.RuntimePolicy
                        != fieldOrigin.Provider.RuntimePolicy
                    || fieldOrigin.DefinitionId != edge.ActualDefinitionId
                    || fieldOrigin.Provider.ModuleRow
                        != edge.ProviderModuleRow
                    || fieldOrigin.Provider.TargetRequirement
                        != edge.TargetRequirement
                    || fieldOrigin.Provider.RuntimePolicy
                        != edge.RuntimePolicy
                    || !StringComparer.Ordinal.Equals(
                        fieldOrigin.Provider.ModuleIdentity,
                        edge.ProducerIdentity)
                    || fieldBinding.ExportKey != edge.ExportKey
                    || fieldBinding.ProducerIdentity
                        != edge.ProducerIdentity
                    || fieldBinding.TargetRequirement
                        != edge.TargetRequirement
                    || fieldBinding.RuntimePolicy != edge.RuntimePolicy
                    || rootTables.Imports.GetModuleRefForImportRow(
                        edge.ImportRow) != edge.ModuleRefRow
                    || !fieldOrigin.ExportKeys.Contains(edge.ExportKey,
                        StringComparer.Ordinal))
                {
                    throw Invalid(
                        "An EN1 FieldDef provider edge lost its checked field origin.");
                }

                var fieldImports = sourceStructFieldImportsByIndex.GetValueOrDefault(
                    (structureIndex, fieldOrdinal));
                if (fieldImports is null)
                {
                    fieldImports = [];
                    sourceStructFieldImportsByIndex.Add(
                        (structureIndex, fieldOrdinal), fieldImports);
                }
                fieldImports.Add(new C1EnumClosureImport(
                    edge.ProducerIdentity, edge.ExportKey,
                    edge.TargetRequirement, edge.RuntimePolicy));
            }

            for (var structureIndex = 0;
                structureIndex < program.Structures.Length;
                structureIndex++)
            {
                var structure = program.Structures[structureIndex];
                for (var fieldIndex = 0;
                    fieldIndex < structure.Fields.Length; fieldIndex++)
                {
                    if (structure.Fields[fieldIndex].StandardFieldOrigin
                            is not BoundStandardFieldOrigin fieldOrigin
                        || SameA2SourceTypeProviderSnapshot(
                            fieldOrigin.Provider, selectedRootSnapshot))
                    {
                        continue;
                    }

                    if (!sourceStructFieldImportsByIndex.TryGetValue(
                            (structureIndex, fieldIndex),
                            out var fieldImports)
                        || fieldImports.Any(fieldImport =>
                            !fieldOrigin.ExportKeys.Contains(
                                fieldImport.ExportKey,
                                StringComparer.Ordinal))
                        || fieldOrigin.ExportKeys.Any(exportKey =>
                            !fieldImports.Any(fieldImport =>
                                StringComparer.Ordinal.Equals(
                                    fieldImport.ExportKey, exportKey))))
                    {
                        throw Invalid(
                            "An EN1 imported FieldDef origin has no complete retained provider edge set.");
                    }
                }
            }
        }
        else if (hasStandardStructViews
            && program.Structures.Any(structure =>
                structure.Fields.Any(field =>
                    field.StandardFieldOrigin is
                        BoundStandardFieldOrigin fieldOrigin
                    && rootSnapshot is not null
                    && !SameA2SourceTypeProviderSnapshot(rootSnapshot,
                        fieldOrigin.Provider))))
        {
            throw Invalid(
                "A mixed EN1 imported FieldDef lost its provider closure.");
        }

        var retainedStructureIndexes = new SortedSet<int>();
        var pendingClassIndexes = new Queue<int>(
            sourceClassExportsByIndex.Keys);
        var pendingStructureIndexes = new Queue<int>(
            sourceStructExportsByIndex.Keys);
        while (pendingClassIndexes.Count != 0
            || pendingStructureIndexes.Count != 0)
        {
            while (pendingClassIndexes.TryDequeue(out var classIndex))
            {
                if (!retainedClassIndexes.Add(classIndex))
                {
                    continue;
                }

                if ((uint)classIndex >= (uint)program.Classes.Length
                    || !localClassIndexes.Contains(classIndex)
                    || retainedClassIndexes.Count
                        > ManagedClassTypes.OptionalFirst
                            - ManagedClassTypes.First)
                {
                    throw Unsupported(
                        "EN1 class closure supports only checked local classes within the registered limit.");
                }

                var @class = program.Classes[classIndex];
                if (@class.Fields.IsDefaultOrEmpty
                    || @class.IsUnique is not false
                    || @class.HasCleanup is not false
                    || @class.CleanupMethod is not null)
                {
                    throw Unsupported(
                        "The bounded EN1 class TypeDef route requires nonempty fields, known aliasable ownership and no Cleanup.");
                }

                foreach (var field in @class.Fields)
                {
                    if (field is null || (field.IsPrivate && field.IsInternal)
                        || field.RequiresUnsafeCaller
                        || field.BackingPropertyName is not null)
                    {
                        throw Unsupported(
                            "EN1 class FieldDefs cannot discard conflicting access, unsafe-caller or backing-property facts.");
                    }

                    if (TryGetC1M1SourceClassFieldType(field.Type,
                            out var dependencyClassIndex, out _, out _))
                    {
                        if ((uint)dependencyClassIndex
                                >= (uint)program.Classes.Length
                            || !localClassIndexes.Contains(
                                dependencyClassIndex))
                        {
                            throw Unsupported(
                                "The bounded EN1 class closure does not admit imported or private class TypeDef dependencies.");
                        }
                        if (!sourceClassExportsByIndex.ContainsKey(
                                dependencyClassIndex))
                        {
                            sourceClassExportsByIndex.Add(
                                dependencyClassIndex, []);
                        }
                        pendingClassIndexes.Enqueue(dependencyClassIndex);
                        continue;
                    }

                    if (TryGetC1M1SourceStructFieldType(field.Type,
                            sourceStructDeclarationsByIndex,
                            out var dependencyStructureIndex))
                    {
                        pendingStructureIndexes.Enqueue(
                            dependencyStructureIndex);
                        continue;
                    }

                    if (BoundEnumTypes.IsType(field.Type))
                    {
                        if ((uint)BoundEnumTypes.Index(field.Type)
                            >= (uint)program.EnumDefinitions.Length)
                        {
                            throw Invalid(
                                "An EN1 class field refers to an invalid enum identity.");
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
                            "EN1 native-width class fields require the selected Windows x64 target.");
                    }

                    _ = GetC1M1SourceScalarTypeSpecReference(field.Type);
                }
            }

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
                    if (TryGetC1M1SourceClassFieldType(field.Type,
                            out var dependencyClassIndex, out _, out _))
                    {
                        if ((uint)dependencyClassIndex
                                >= (uint)program.Classes.Length
                            || !localClassIndexes.Contains(
                                dependencyClassIndex))
                        {
                            throw Unsupported(
                                "EN1 struct fields require retained local class declarations.");
                        }
                        if (!sourceClassExportsByIndex.ContainsKey(
                                dependencyClassIndex))
                        {
                            sourceClassExportsByIndex.Add(
                                dependencyClassIndex, []);
                        }
                        pendingClassIndexes.Enqueue(dependencyClassIndex);
                        continue;
                    }

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

                        pendingStructureIndexes.Enqueue(
                            dependencyStructureIndex);
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
        }

        var structureIndexByRootTypeDefRow
            = new Dictionary<int, int>(rootTypeDefRowByStructureIndex.Count);
        foreach (var (structureIndex, typeDefRow)
            in rootTypeDefRowByStructureIndex)
        {
            if (typeDefRow <= 0
                || !structureIndexByRootTypeDefRow.TryAdd(typeDefRow,
                    structureIndex))
            {
                throw Invalid(
                    "EN1 class re-emission found an invalid or duplicate root struct TypeDef identity.");
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
        var standardClassDeclarationsByTypeDef
            = standardClassEmissions.ToArray();
        var typeDefRows = new List<TrRmTypeDefRow>(
            emittedEnums.Count + retainedClassIndexes.Count
                + standardClassDeclarationsByTypeDef.Length
                + retainedStructureIndexes.Count);
        var typeDefNames = new List<(string Namespace, string Name)>(
            emittedEnums.Count + retainedClassIndexes.Count
                + standardClassDeclarationsByTypeDef.Length
                + retainedStructureIndexes.Count);
        var layoutRows = new List<TrRmLayoutRow>(
            checked(emittedEnums.Count * 2
                + retainedClassIndexes.Count
                + standardClassDeclarationsByTypeDef.Length
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
        var importRowByRootStructureImport = new Dictionary<int, int>();
        var importRowByRootClassImport = new Dictionary<int, int>();
        var typeDefRowByClass = retainedClassIndexes
            .Select((classIndex, ordinal) => (classIndex,
                typeDefRow: emittedEnums.Count + ordinal + 1))
            .ToDictionary(static item => item.classIndex,
                static item => item.typeDefRow);
        var typeDefRowByRootClassTypeDef
            = standardClassDeclarationsByTypeDef
                .Select((emission, ordinal) => (emission,
                    typeDefRow: emittedEnums.Count
                        + retainedClassIndexes.Count + ordinal + 1))
                .ToDictionary(static item =>
                        item.emission.RootTypeDefRow,
                    static item => item.typeDefRow);
        var typeDefRowByStructure = retainedStructureIndexes
            .Select((structureIndex, ordinal) => (structureIndex,
                typeDefRow: emittedEnums.Count + retainedClassIndexes.Count
                    + standardClassDeclarationsByTypeDef.Length
                    + ordinal + 1))
            .ToDictionary(static item => item.structureIndex,
                static item => item.typeDefRow);
        var typeSpecByClassType = new Dictionary<
            (int ClassIndex, bool IsWritable, bool IsOptional), uint>();
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
        var standardClassRootTables
            = standardClassDeclarationsByTypeDef.Length == 0
                ? null
                : ValidateForeignInvocationArtifact(
                    CreateForeignInvocationIndex(
                        rootSnapshot!.ArtifactSnapshot.ToArray(),
                        rootSnapshot.SchemaIdentity),
                    rootSnapshot.SchemaIdentity, null,
                    createA3ResourceLedger: false);
        var standardClassFieldExportKeysByDefinition
            = new Dictionary<uint, List<string>>();
        if (standardClassRootTables is not null)
        {
            foreach (var (definition, exportKey) in standardClassRootTables
                .Exports.GetExportsForModule(rootSnapshot!.ModuleRow))
            {
                if ((definition >> 24) != 0x14)
                {
                    continue;
                }

                if (!standardClassFieldExportKeysByDefinition.TryGetValue(
                        definition, out var exportKeys))
                {
                    exportKeys = [];
                    standardClassFieldExportKeysByDefinition.Add(
                        definition, exportKeys);
                }
                exportKeys.Add(exportKey);
            }
        }
        var enumIndexByRootTypeDefRow = new Dictionary<int, int>();
        var importRowByRootEnumImport = new Dictionary<int, int>();
        if (standardClassRootTables is not null)
        {
            var selectedRootSnapshot = rootSnapshot!;
            for (var enumIndex = 0;
                enumIndex < program.EnumDefinitions.Length; enumIndex++)
            {
                var definition = program.EnumDefinitions[enumIndex];
                if (definition.Identity
                    is not BoundEnumDeclarationIdentity.Standard standard)
                {
                    throw Invalid(
                        "EN1 class re-emission requires retained Standard enum identities.");
                }

                var occurrence = standard.Value;
                var view = occurrence.Declaration;
                if (BoundStandardEnumDeclarationContract.SnapshotsMatch(
                        view.Site.Snapshot, selectedRootSnapshot))
                {
                    if ((view.Site.DefinitionId >> 24) != 0x13
                        || !enumIndexByRootTypeDefRow.TryAdd(
                            checked((int)(view.Site.DefinitionId
                                & 0x00FF_FFFF)), enumIndex))
                    {
                        throw Invalid(
                            "EN1 class re-emission found an invalid or duplicate root enum TypeDef identity.");
                    }
                }
            }
        }

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

        var standardClassTypeSpecRows = new Dictionary<uint, uint>();
        var activeStandardClassTypeSpecs = new HashSet<uint>();
        uint StandardClassFieldTypeSpecReference(
            BoundStandardClassFieldDeclaration field)
            => RemapStandardClassTypeSpecReference(
                field.TypeSpecReference, 0);

        uint RemapStandardClassTypeSpecReference(uint sourceTypeSpec,
            int depth)
        {
            if (standardClassRootTables is null)
            {
                throw new InvalidDataException(
                    "An EN1 Standard class FieldDef has no validated root artifact.");
            }

            var type = standardClassRootTables.TypeSpecs.GetTypeSpecRef(
                sourceTypeSpec);
            if (type.Form == 0xF2 && type.Qualifiers == 0)
            {
                return sourceTypeSpec;
            }
            if (depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "EN1 class TypeSpec remapping exceeds the checked nesting limit.");
            }
            if (standardClassTypeSpecRows.TryGetValue(sourceTypeSpec,
                    out var existingTypeSpec))
            {
                return existingTypeSpec;
            }
            if (!type.IsRow || !activeStandardClassTypeSpecs.Add(
                    sourceTypeSpec))
            {
                throw Invalid(
                    "An EN1 class FieldDef contains a cyclic or non-row TypeSpec reference.");
            }

            try
            {
                uint encodedTypeSpec;
                if (type.Form == 0x06 && type.Qualifiers == 0)
                {
                    var row = standardClassRootTables.TypeSpecs.GetRow(
                        type.Row);
                    var innerTypeSpec = BinaryPrimitives
                        .ReadUInt32LittleEndian(row[2..]);
                    encodedTypeSpec = checked((uint)typeSpecRows.Count + 1);
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Wrapped(
                        type.Form, RemapStandardClassTypeSpecReference(
                            innerTypeSpec, depth + 1), type.Qualifiers));
                }
                else if (type.Form is 0x02 or 0x03)
                {
                    var oldReferenceRow = checked((int)type.Row);
                    uint newReferenceRow;
                    if (type.Form == 0x02
                        && typeDefRowByRootClassTypeDef.TryGetValue(
                            oldReferenceRow, out var classTypeDefRow))
                    {
                        newReferenceRow = checked((uint)classTypeDefRow);
                    }
                    else if (type.Form == 0x03
                        && importRowByRootClassImport.TryGetValue(
                            oldReferenceRow, out var classImportRow))
                    {
                        newReferenceRow = checked((uint)classImportRow);
                    }
                    else if (type.Form == 0x02 && type.Qualifiers == 0
                        && enumIndexByRootTypeDefRow.TryGetValue(
                            oldReferenceRow, out var enumIndex))
                    {
                        encodedTypeSpec = FieldTypeSpecReference(
                            BoundEnumTypes.Type(enumIndex));
                        standardClassTypeSpecRows.Add(sourceTypeSpec,
                            encodedTypeSpec);
                        return encodedTypeSpec;
                    }
                    else if (type.Form == 0x03 && type.Qualifiers == 0
                        && importRowByRootEnumImport.TryGetValue(
                            oldReferenceRow, out var enumImportRow))
                    {
                        newReferenceRow = checked((uint)enumImportRow);
                    }
                    else if (type.Form == 0x02 && type.Qualifiers == 0
                        && structureIndexByRootTypeDefRow.TryGetValue(
                            oldReferenceRow, out var structureIndex))
                    {
                        encodedTypeSpec = FieldTypeSpecReference(
                            (PrimitiveType)(100 + structureIndex));
                        standardClassTypeSpecRows.Add(sourceTypeSpec,
                            encodedTypeSpec);
                        return encodedTypeSpec;
                    }
                    else if (type.Form == 0x03 && type.Qualifiers == 0
                        && importRowByRootStructureImport.TryGetValue(
                            oldReferenceRow,
                            out var structureImportRow))
                    {
                        newReferenceRow
                            = checked((uint)structureImportRow);
                    }
                    else
                    {
                        throw Unsupported(
                            "An EN1 class FieldDef TypeSpec refers to an unretained class, struct or enum identity.");
                    }

                    encodedTypeSpec = checked((uint)typeSpecRows.Count + 1);
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                        type.Form, newReferenceRow, type.Qualifiers));
                }
                else
                {
                    throw Unsupported(
                        "The bounded EN1 class re-emission supports scalar, wrapper and class/struct/enum TypeSpec forms only.");
                }

                standardClassTypeSpecRows.Add(sourceTypeSpec,
                    encodedTypeSpec);
                return encodedTypeSpec;
            }
            finally
            {
                _ = activeStandardClassTypeSpecs.Remove(sourceTypeSpec);
            }
        }

        uint FieldTypeSpecReference(PrimitiveType type)
        {
            if (BoundGenericNominalTypeTypes.IsType(type))
            {
                var instanceIndex
                    = BoundGenericNominalTypeTypes.Index(type);
                if ((uint)instanceIndex
                        >= (uint)program.GenericNominalTypeInstances.Length
                    || !BoundGenericNominalTypeContract.IsNominalClassType(
                        type, program.GenericNominalTypeInstances,
                        program.GenericNominalTypeDefinitions))
                {
                    throw Unsupported(
                        "EN1 FieldDefs support metadata-only nongeneric class handles only.");
                }

                var instance = program.GenericNominalTypeInstances[
                    instanceIndex];
                if (instance.TypeSpecReference == 0
                    || rootSnapshot is null
                    || !StringComparer.Ordinal.Equals(
                        instance.TypeSpecProviderIdentity,
                        rootSnapshot.ModuleIdentity))
                {
                    throw Invalid(
                        "An EN1 class FieldDef lost its root TypeSpec origin.");
                }

                return RemapStandardClassTypeSpecReference(
                    instance.TypeSpecReference, 0);
            }

            if (TryGetC1M1SourceClassFieldType(type,
                    out var classIndex, out var isWritable,
                    out var isOptional))
            {
                if (!typeDefRowByClass.TryGetValue(classIndex,
                        out var typeDefRow))
                {
                    throw new InvalidDataException(
                        "A captured EN1 class FieldDef refers to an unretained local class.");
                }

                var key = (classIndex, isWritable, isOptional);
                if (!typeSpecByClassType.TryGetValue(key,
                        out var typeSpecRow))
                {
                    var classTypeSpecRow
                        = checked((uint)typeSpecRows.Count + 1);
                    var qualifiers = (byte)(0x02
                        | (isWritable ? 0x01 : 0));
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                        0x02, checked((uint)typeDefRow), qualifiers));
                    typeSpecRow = classTypeSpecRow;
                    if (isOptional)
                    {
                        typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                        typeSpecRows.Add(TrRmTypeSpecTableWriter.Wrapped(
                            0x06, classTypeSpecRow));
                    }

                    typeSpecByClassType.Add(key, typeSpecRow);
                }

                return typeSpecRow;
            }

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
                if (emittedEnum.RootImportRow != 0)
                {
                    if (standardClassRootTables is null
                        || emittedEnum.RootImportRow < 0
                        || !importRowByRootEnumImport.TryAdd(
                            emittedEnum.RootImportRow, enumImportRow))
                    {
                        throw Invalid(
                            "An EN1 class field Import lost its exact root enum Import mapping.");
                    }
                }
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

        foreach (var emission in standardClassDeclarationsByTypeDef)
        {
            if (emission.Import is not C1EnumClosureImport typeImport)
            {
                continue;
            }

            if (!BoundEnumTypes.IsValidModuleDeclarationKey(
                    typeImport.ProducerIdentity)
                || !BoundEnumTypes.IsValidModuleDeclarationKey(
                    typeImport.ExportKey)
                || typeImport.RootImportRow <= 0
                || !typeDefRowByRootClassTypeDef.TryGetValue(
                    emission.RootTypeDefRow, out var typeDefRow))
            {
                throw Invalid(
                    "An EN1 class TypeDef Import has an invalid provider or root identity.");
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
            if (!importRowByRootClassImport.TryAdd(
                    typeImport.RootImportRow, importRows.Count))
            {
                throw Invalid(
                    "An EN1 class TypeDef Import row was emitted more than once.");
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
                if (typeImport.RootImportRow != 0
                    && (typeImport.RootImportRow < 0
                        || !importRowByRootStructureImport.TryAdd(
                            typeImport.RootImportRow, importRows.Count)))
                {
                    throw Invalid(
                        "An EN1 struct TypeDef Import row was emitted more than once.");
                }
            }
        }

        foreach (var classIndex in retainedClassIndexes)
        {
            var @class = program.Classes[classIndex];
            var className = @class.Name;
            var separator = className.LastIndexOf('.',
                StringComparison.Ordinal);
            var namespaceName = separator < 0
                ? "" : className[..separator];
            var typeName = separator < 0
                ? className : className[(separator + 1)..];
            if (string.IsNullOrWhiteSpace(typeName) || separator == 0)
            {
                throw Unsupported(
                    "EN1 class names require valid namespace and type-name components.");
            }

            var typeDefRow = typeDefRowByClass[classIndex];
            var classExportKeys = sourceClassExportsByIndex[classIndex];
            var typeDefFlags = (classExportKeys.Count != 0 ? 2u
                : @class.IsInternal ? 1u : 2u) | 0x08u
                    | (@class.IsMutable ? 0x80u : 0u);
            var fieldFirst = fieldRows.Count + 1;
            var layoutFieldFirst = layoutFieldRows.Count + 1;
            var fieldNamesForClass = new HashSet<string>(
                StringComparer.Ordinal);
            for (var fieldOrdinal = 0;
                fieldOrdinal < @class.Fields.Length; fieldOrdinal++)
            {
                var field = @class.Fields[fieldOrdinal];
                if (field is null || (field.IsPrivate && field.IsInternal)
                    || field.RequiresUnsafeCaller
                    || field.BackingPropertyName is not null
                    || !fieldNamesForClass.Add(field.Name))
                {
                    throw Unsupported(
                        "EN1 class FieldDefs cannot discard conflicting access, unsafe-caller or backing-property facts.");
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
                if (sourceClassFieldExportsByIndex.TryGetValue(
                        (classIndex, fieldOrdinal),
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

            typeDefRows.Add(new TrRmTypeDefRow(0, 0, typeDefFlags,
                fieldFirst, @class.Fields.Length, 0, 0,
                checked((uint)(layoutRows.Count + 1)), 1, 0, 0));
            typeDefNames.Add((namespaceName, typeName));
            layoutRows.Add(new TrRmLayoutRow(typeDefRow, 0, 0, 0, 0, 0,
                layoutFieldFirst, @class.Fields.Length));
            foreach (var exportKey in classExportKeys)
            {
                exportRows.Add(new TrRmExportRow(
                    0x1300_0000u | checked((uint)typeDefRow),
                    AddString(exportKey)));
            }
        }

        foreach (var emission in standardClassDeclarationsByTypeDef)
        {
            var classView = emission.View;
            if (standardClassRootTables is null
                || !SameA2SourceTypeProviderSnapshot(rootSnapshot!,
                    classView.Provider)
                || classView.Provider.ModuleRow != rootSnapshot!.ModuleRow
                || classView.Layouts.Length != 1)
            {
                throw Unsupported(
                    "EN1 class re-emission requires one exact local class declaration with a single default layout.");
            }

            var layout = classView.Layouts[0];
            if (layout.TargetProfileReference != 0
                || layout.Kind != 0 || layout.Flags != 0
                || layout.Alignment != 0 || layout.Pack != 0
                || layout.FieldDefinitionIds.Length
                    != classView.Fields.Length)
            {
                throw Unsupported(
                    "EN1 class re-emission supports the default Auto Layout variant only.");
            }

            var typeDefRow = typeDefRowByRootClassTypeDef[
                emission.RootTypeDefRow];
            var fieldFirst = classView.Fields.IsEmpty
                ? 0 : checked(fieldRows.Count + 1);
            var layoutFieldFirst = classView.Fields.IsEmpty
                ? 0 : checked(layoutFieldRows.Count + 1);
            var fieldNamesForClass = new HashSet<string>(
                StringComparer.Ordinal);
            for (var fieldOrdinal = 0;
                fieldOrdinal < classView.Fields.Length; fieldOrdinal++)
            {
                var field = classView.Fields[fieldOrdinal];
                if (field.OwnerDefinitionId
                        != classView.Identity.DefinitionId
                    || (field.DefinitionId >> 24) != 0x14
                    || !fieldNamesForClass.Add(field.Name)
                    || layout.FieldDefinitionIds[fieldOrdinal]
                        != field.DefinitionId)
                {
                    throw Invalid(
                        "An EN1 class FieldDef or Layout no longer matches its retained declaration snapshot.");
                }

                var fieldDefRow = checked(fieldRows.Count + 1);
                fieldRows.Add(new TrRmFieldDefRow(typeDefRow, 0,
                    StandardClassFieldTypeSpecReference(field),
                    field.Flags));
                fieldNames.Add(field.Name);
                layoutFieldRows.Add(new TrRmLayoutFieldRow(
                    checked(layoutRows.Count + 1), fieldOrdinal,
                    fieldDefRow));
                if (standardClassFieldExportKeysByDefinition.TryGetValue(
                        field.DefinitionId, out var fieldExportKeys))
                {
                    if (emission.Import is not null)
                    {
                        throw Invalid(
                            "An EN1 imported class TypeDef cannot re-export its expected FieldDefs.");
                    }

                    foreach (var exportKey in fieldExportKeys)
                    {
                        fieldExportRows.Add(new TrRmExportRow(
                            0x1400_0000u | checked((uint)fieldDefRow),
                            AddString(exportKey)));
                    }
                }

                foreach (var fieldExpectation in
                    emission.FieldExpectations)
                {
                    if (fieldExpectation.ExpectedDefinitionId
                        != field.DefinitionId)
                    {
                        continue;
                    }

                    if (emission.Import is not C1EnumClosureImport typeImport
                        || fieldExpectation.ImportRow <= 0
                        || fieldExpectation.OwnerImportRow
                            != typeImport.RootImportRow
                        || fieldExpectation.ExpectedOwnerDefinitionId
                            != classView.Identity.DefinitionId
                        || !StringComparer.Ordinal.Equals(
                            fieldExpectation.Provider.ModuleIdentity,
                            typeImport.ProducerIdentity)
                        || fieldExpectation.Provider.TargetRequirement
                            != typeImport.TargetRequirement
                        || fieldExpectation.Provider.RuntimePolicy
                            != typeImport.RuntimePolicy)
                    {
                        throw Invalid(
                            "An EN1 class FieldDef Import lost its retained owner or provider identity.");
                    }

                    var fieldProviderKey = (
                        fieldExpectation.Provider.ModuleIdentity,
                        fieldExpectation.Provider.TargetRequirement,
                        fieldExpectation.Provider.RuntimePolicy);
                    if (!moduleRefRowsByProvider.TryGetValue(
                            fieldProviderKey, out var fieldModuleRefRow))
                    {
                        fieldModuleRefRow = moduleRefRows.Count + 1;
                        moduleRefRows.Add(new TrRmModuleRefRow(
                            AddString(fieldExpectation.Provider
                                .ModuleIdentity),
                            fieldExpectation.Provider.TargetRequirement,
                            fieldExpectation.Provider.RuntimePolicy));
                        moduleRefRowsByProvider.Add(fieldProviderKey,
                            fieldModuleRefRow);
                    }

                    importRows.Add(new TrRmImportRow(fieldModuleRefRow,
                        AddString(fieldExpectation.ExportKey),
                        0x1400_0000u | checked((uint)fieldDefRow)));
                }
            }

            var layoutFirst = checked(layoutRows.Count + 1);
            layoutRows.Add(new TrRmLayoutRow(typeDefRow, 0, layout.Kind,
                layout.Flags, layout.Alignment, layout.Pack,
                layoutFieldFirst, classView.Fields.Length));
            typeDefRows.Add(new TrRmTypeDefRow(0, 0,
                classView.TypeDefFlags, fieldFirst,
                classView.Fields.Length, 0, 0,
                checked((uint)layoutFirst), 1, 0, 0));
            typeDefNames.Add((classView.Namespace, classView.Name));
            foreach (var exportKey in emission.ExportKeys)
            {
                exportRows.Add(new TrRmExportRow(
                    0x1300_0000u | checked((uint)typeDefRow),
                    AddString(exportKey)));
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

                if (sourceStructFieldImportsByIndex.TryGetValue(
                        (structureIndex, fieldOrdinal),
                        out var fieldImports))
                {
                    if (sourceStructFieldExportsByIndex.ContainsKey(
                            (structureIndex, fieldOrdinal))
                        || !sourceStructImportsByIndex.TryGetValue(
                            structureIndex, out var ownerImports)
                        || ownerImports.Count != 1
                        || fieldImports.Any(fieldImport =>
                            !ownerImports.Any(ownerImport =>
                                StringComparer.Ordinal.Equals(
                                    ownerImport.ProducerIdentity,
                                    fieldImport.ProducerIdentity)
                                && ownerImport.TargetRequirement
                                    == fieldImport.TargetRequirement
                                && ownerImport.RuntimePolicy
                                    == fieldImport.RuntimePolicy)))
                    {
                        throw Invalid(
                            "An EN1 FieldDef Import does not retain one matching owner TypeDef Import.");
                    }

                    foreach (var fieldImport in fieldImports)
                    {
                        var providerKey = (fieldImport.ProducerIdentity,
                            fieldImport.TargetRequirement,
                            fieldImport.RuntimePolicy);
                        if (!moduleRefRowsByProvider.TryGetValue(
                                providerKey, out var moduleRefRow))
                        {
                            moduleRefRow = moduleRefRows.Count + 1;
                            moduleRefRows.Add(new TrRmModuleRefRow(
                                AddString(fieldImport.ProducerIdentity),
                                fieldImport.TargetRequirement,
                                fieldImport.RuntimePolicy));
                            moduleRefRowsByProvider.Add(providerKey,
                                moduleRefRow);
                        }

                        importRows.Add(new TrRmImportRow(moduleRefRow,
                            AddString(fieldImport.ExportKey),
                            0x1400_0000u
                                | checked((uint)fieldDefRow)));
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
        if (sourceClassFieldExportsByIndex.Count != 0
            || standardClassFieldExportKeysByDefinition.Count != 0
            || sourceStructFieldExportsByIndex.Count != 0
            || sourceStructFieldImportsByIndex.Count != 0)
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
