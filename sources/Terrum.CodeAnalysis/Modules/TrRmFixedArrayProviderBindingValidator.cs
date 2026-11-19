// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmStandardTypeProviderBinding(
    int ConsumerArtifactIndex, int ConsumerModuleRow, int ImportRow,
    int ModuleRefRow,
    int ExpectedTypeDefRow, int ProviderArtifactIndex, int ProviderModuleRow,
    int ProviderTypeDefRow, int ExportRow, uint ActualDefinitionId,
    string ProducerIdentity, string ExportKey, uint TargetRequirement,
    uint RuntimePolicy);

internal readonly record struct TrRmStandardFieldProviderBinding(
    int ConsumerArtifactIndex, int ConsumerModuleRow, int ImportRow,
    int ModuleRefRow, int ExpectedFieldDefRow, int ExpectedOwnerTypeDefRow,
    int OwnerImportRow, int ProviderArtifactIndex, int ProviderModuleRow,
    int ProviderTypeDefRow, int ProviderFieldDefRow, int ExportRow,
    string ProducerIdentity, string ExportKey, uint TargetRequirement,
    uint RuntimePolicy);

internal readonly record struct TrRmStandardMethodProviderBinding(
    int ConsumerArtifactIndex, int ConsumerModuleRow, int ImportRow,
    int ModuleRefRow, int ExpectedMethodDefRow, int ExpectedOwnerTypeDefRow,
    int OwnerImportRow, int ProviderArtifactIndex, int ProviderModuleRow,
    int ProviderTypeDefRow, int ProviderMethodDefRow, int ExportRow,
    string ProducerIdentity, string ExportKey, uint TargetRequirement,
    uint RuntimePolicy);

internal readonly record struct TrRmStandardFieldProviderValidation(
    ImmutableArray<TrRmStandardTypeProviderBinding> TypeBindings,
    ImmutableArray<TrRmStandardFieldProviderBinding> FieldBindings,
    ImmutableArray<TrRmStandardMethodProviderBinding> MethodBindings);

internal readonly record struct TrRmStandardClassProviderValidation(
    ImmutableArray<TrRmStandardTypeProviderBinding> TypeBindings,
    ImmutableArray<TrRmStandardFieldProviderBinding> FieldBindings,
    ImmutableArray<TrRmStandardMethodProviderBinding> MethodBindings);

internal readonly record struct TrRmStandardMethodTypeSpecPair(
    int ExpectedArtifactIndex, int ExpectedModuleRow,
    uint ExpectedTypeSpecRef, int ActualArtifactIndex, int ActualModuleRow,
    uint ActualTypeSpecRef);

internal static class TrRmFixedArrayProviderBindingValidator
{
    private const string LayoutQueriesSchemaIdentity
        = "trrm/1-draft-layout-queries-1";
    private const string C1M1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";
    private const string CM1SchemaIdentity
        = StandardGenericMethodSchema
            .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaults;
    private const string FixedArraySchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
    private const string FixedArrayEmptyInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1";
    private const string FixedArrayEmptyUnitInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
    private const string FixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
    private const string FixedArrayCopyReturnCoverageSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-1";
    private const string FixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-repeat-initialization-1";
    private const string LocalAggregateLayoutSchemaIdentity
        = "trrm/1-draft-local-aggregate-layout-execution-1";
    private const string InitializationReferenceLocalFieldLayoutSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-local-field-layout-1";
    private const string InitializationReferenceNestedFieldDestinationsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-local-field-layout-2";
    private const string GenericFixedArraySchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";

    private static bool IsC1M1SchemaIdentity(string schemaIdentity)
        => StringComparer.Ordinal.Equals(schemaIdentity,
                C1M1SchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                CM1SchemaIdentity);

    private static bool IsInitializationReferenceLocalFieldLayoutSchemaIdentity(
        string schemaIdentity)
        => StringComparer.Ordinal.Equals(schemaIdentity,
                InitializationReferenceLocalFieldLayoutSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                InitializationReferenceNestedFieldDestinationsSchemaIdentity);

    internal static ImmutableArray<TrRmStandardTypeProviderBinding> Validate(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
        byte[][] artifacts, string[] schemaIdentities,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        int rootModuleRow)
    {
        var a3ResourceLedger = consumer.Schema
            == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? consumer.A3LayoutResourceLedger
                    ?? throw new InvalidOperationException(
                        "A3 provider validation requires the root request resource ledger.")
                : null;
        var context = new ValidationContext(consumer, artifacts,
            schemaIdentities, modules, a3ResourceLedger);
        try
        {
            return context.Validate(rootModuleRow);
        }
        catch (TrRmResourceLimitException)
        {
            throw;
        }
        catch (OverflowException exception)
            when (a3ResourceLedger is not null)
        {
            if (TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
                    out var kind)
                && kind
                    == TrRmA3AdmissionFailureKind.TargetLayoutOverflow)
            {
                throw;
            }

            var invalid = new InvalidDataException(
                "Invalid TRRM fixed-array TypeDef provider binding.",
                exception);
            a3ResourceLedger.AdmissionScope.Mark(invalid,
                TrRmA3AdmissionFailureKind.InvalidArtifact);
            throw invalid;
        }
        catch (InvalidDataException exception)
            when (a3ResourceLedger is not null)
        {
            if (!TrRmA3AdmissionFailureMarkers.TryGet(exception, out _))
            {
                a3ResourceLedger.AdmissionScope.Mark(exception,
                    TrRmA3AdmissionFailureKind.InvalidArtifact);
            }

            throw;
        }
        catch (NotSupportedException exception)
            when (a3ResourceLedger is not null)
        {
            if (!TrRmA3AdmissionFailureMarkers.TryGet(exception, out _))
            {
                a3ResourceLedger.AdmissionScope.Mark(exception,
                    TrRmA3AdmissionFailureKind.UnsupportedAdmission);
            }

            throw;
        }
    }

    internal static TrRmStandardFieldProviderValidation
        ValidateFieldDefImports(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
        byte[][] artifacts, string[] schemaIdentities,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        int rootModuleRow)
    {
        var isEN1ProviderSchema = schemaIdentities.Length != 0
            && TrRmExecutableCompiler
                .IsSelectedC1EnumClosureSchemaIdentity(
                    schemaIdentities[0])
            && schemaIdentities.All(schemaIdentity =>
                StringComparer.Ordinal.Equals(schemaIdentity,
                    schemaIdentities[0]));
        var isC1M1ProviderSchema = schemaIdentities.Length != 0
            && IsC1M1SchemaIdentity(schemaIdentities[0])
            && schemaIdentities.All(schemaIdentity =>
                StringComparer.Ordinal.Equals(schemaIdentity,
                    schemaIdentities[0]));
        if (consumer.Schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
                or TrRmArtifactSchema.C1Draft
                or TrRmArtifactSchema.C1M1Draft
                or TrRmArtifactSchema
                    .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft)
            && !(consumer.Schema.UsesC1EnumClosure()
                && isEN1ProviderSchema)
            || artifacts.Length != schemaIdentities.Length
            || artifacts.Length == 0)
        {
            throw new InvalidDataException(
                "Invalid TRRM Standard FieldDef provider binding.");
        }

        var nestedFieldDestinations = schemaIdentities.Length != 0
            && schemaIdentities.All(static schemaIdentity =>
                StringComparer.Ordinal.Equals(schemaIdentity,
                    InitializationReferenceNestedFieldDestinationsSchemaIdentity));
        var layoutQueries = schemaIdentities.Length != 0
            && schemaIdentities.All(static schemaIdentity =>
                StringComparer.Ordinal.Equals(schemaIdentity,
                    LayoutQueriesSchemaIdentity));
        if (!layoutQueries && !nestedFieldDestinations
            && !isEN1ProviderSchema && !isC1M1ProviderSchema)
        {
            throw new InvalidDataException(
                "Invalid TRRM Standard FieldDef provider binding.");
        }

        foreach (var schemaIdentity in schemaIdentities)
        {
            if (!StringComparer.Ordinal.Equals(schemaIdentity,
                    LayoutQueriesSchemaIdentity)
                && !(nestedFieldDestinations
                    && StringComparer.Ordinal.Equals(schemaIdentity,
                        InitializationReferenceNestedFieldDestinationsSchemaIdentity))
                && !(isC1M1ProviderSchema
                    && IsC1M1SchemaIdentity(schemaIdentity))
                && !(isEN1ProviderSchema
                    && TrRmExecutableCompiler
                        .IsSelectedC1EnumClosureSchemaIdentity(
                            schemaIdentity)))
            {
                throw new InvalidDataException(
                    "Invalid TRRM Standard FieldDef provider binding.");
            }
        }

        var context = new ValidationContext(consumer, artifacts,
            schemaIdentities, modules, a3ResourceLedger: null);
        return context.ValidateFieldDefImports(rootModuleRow,
            nestedFieldDestinations, isC1M1ProviderSchema);
    }

    internal static ImmutableArray<TrRmStandardTypeProviderBinding>
        ValidateC1M1TypeDefImports(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
        byte[][] artifacts, string[] schemaIdentities,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        int rootModuleRow)
        => ValidateC1M1ClassImports(consumer, artifacts, schemaIdentities,
            modules, rootModuleRow).TypeBindings;

    internal static TrRmStandardClassProviderValidation
        ValidateC1M1ClassImports(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
        byte[][] artifacts, string[] schemaIdentities,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        int rootModuleRow)
    {
        var isC1M1ProviderSchema = schemaIdentities.Length != 0
            && IsC1M1SchemaIdentity(schemaIdentities[0]);
        var isEN1ProviderSchema = schemaIdentities.Length != 0
            && TrRmExecutableCompiler
                .IsSelectedC1EnumClosureSchemaIdentity(
                    schemaIdentities[0]);
        var isC1M1ConsumerSchema = consumer.Schema is
            TrRmArtifactSchema.C1M1Draft
                or TrRmArtifactSchema
                    .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft;
        if ((!isC1M1ConsumerSchema
                && (!consumer.Schema.UsesC1EnumClosure()
                    || !isEN1ProviderSchema))
            || artifacts.Length != schemaIdentities.Length
            || artifacts.Length == 0
            || (!isC1M1ProviderSchema && !isEN1ProviderSchema))
        {
            throw new InvalidDataException(
                "Invalid TRRM C1M1 class member provider binding.");
        }

        for (var artifactIndex = 1;
            artifactIndex < schemaIdentities.Length; artifactIndex++)
        {
            if (!StringComparer.Ordinal.Equals(schemaIdentities[0],
                    schemaIdentities[artifactIndex]))
            {
                throw new InvalidDataException(
                    "Invalid TRRM C1M1 class member provider binding.");
            }
        }

        var context = new ValidationContext(consumer, artifacts,
            schemaIdentities, modules, a3ResourceLedger: null);
        var typeBindings
            = context.ValidateC1M1TypeDefImports(rootModuleRow);
        var fieldValidation = context.ValidateFieldDefImports(rootModuleRow,
            nestedFieldDestinations: false,
            allowC1M1MethodImports: true);
        return new TrRmStandardClassProviderValidation(typeBindings,
            fieldValidation.FieldBindings, context.MethodProviderBindings);
    }

    internal static TrRmStandardTypeProviderBinding
        ValidateGenericMethodTypeDefImport(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
        byte[][] artifacts, string[] schemaIdentities,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        int artifactIndex, int moduleRow, int importRow)
    {
        if (artifactIndex < 0 || artifactIndex >= artifacts.Length
            || artifactIndex >= schemaIdentities.Length
            || !StandardGenericMethodSchema.IsGenericMethodCalls(
                schemaIdentities[artifactIndex]))
        {
            throw InvalidBinding();
        }

        var context = new ValidationContext(consumer, artifacts,
            schemaIdentities, modules, a3ResourceLedger: null,
            allowGenericMethodTypeProviders: true);
        return context.ValidateGenericMethodTypeDefImport(artifactIndex,
            moduleRow, importRow);
    }

    internal static void ValidateMethodTypeSpecPairs(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
        byte[][] artifacts, string[] schemaIdentities,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        int rootModuleRow,
        ImmutableArray<TrRmStandardMethodTypeSpecPair> typeSpecPairs)
    {
        var context = new ValidationContext(consumer, artifacts,
            schemaIdentities, modules, a3ResourceLedger: null);
        _ = context.Validate(rootModuleRow);
        context.ValidateMethodTypeSpecPairs(typeSpecPairs);
    }

    private readonly record struct TypeOrigin(
        int ArtifactIndex, int ModuleRow, int TypeDefRow);

    private readonly record struct TypeOriginPair(
        TypeOrigin Expected, TypeOrigin Actual, bool IsInlineStorage,
        ulong Depth = 1);

    private readonly record struct TypeSpecLocation(
        int ArtifactIndex, int ModuleRow, uint TypeSpecRef,
        bool IsInlineStorage, ulong Depth = 1);

    private readonly record struct ImportResolution(
        TypeOrigin Expected, TypeOrigin Actual,
        TrRmStandardTypeProviderBinding Binding);

    private readonly record struct LayoutIdentity(
        uint ProfileId, uint RuleRevision, byte Kind, byte Flags,
        uint Alignment, uint Pack);

    private readonly record struct TypeSpecComparison(
        int LeftArtifactIndex, int LeftModuleRow, uint LeftTypeSpecRef,
        int RightArtifactIndex, int RightModuleRow, uint RightTypeSpecRef,
        bool IsInlineStorage, TypeOrigin? LeftFormalOwner = null,
        TypeOrigin? RightFormalOwner = null,
        bool AllowInitQualifier = false, ulong Depth = 1);

    private sealed record TypeFormalSubstitution(
        TypeOrigin ExpectedOwner, TypeOrigin ActualOwner,
        ImmutableArray<uint> Arguments,
        TypeFormalSubstitution? Parent);

    private readonly record struct ResolvedFieldPathOwner(
        TypeOrigin Expected, TypeOrigin Actual,
        TypeFormalSubstitution? Substitutions);

    private ref struct ValidationContext
    {
        private readonly TrRmStructuralMetadataValidator.ValidatedExecutionTables
            _consumer;
        private readonly byte[][] _artifacts;
        private readonly string[] _schemaIdentities;
        private readonly ImmutableArray<TrRmStandardModuleSelection> _modules;
        private readonly TrRmA3LayoutResourceLedger? _a3ResourceLedger;
        private readonly bool _allowGenericMethodTypeProviders;
        private readonly Dictionary<(int ArtifactIndex, int ImportRow),
            ImportResolution> _imports;
        private readonly HashSet<TypeOrigin> _visitedLocalTypes;
        private readonly HashSet<(TypeOrigin Expected, TypeOrigin Actual,
            bool IsInlineStorage)> _visitedTypePairs;
        private readonly HashSet<(int LeftArtifact, int LeftMethod,
            int RightArtifact, int RightMethod)> _activeAnchorPairs;
        internal ImmutableArray<TrRmStandardMethodProviderBinding>
            MethodProviderBindings { get; private set; }
        private TrRmStructuralMetadataValidator.ValidatedExecutionTables
            _cachedProviderTables;
        private int _cachedProviderArtifactIndex;
        private bool _hasCachedProviderTables;

        internal ValidationContext(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
            byte[][] artifacts, string[] schemaIdentities,
            ImmutableArray<TrRmStandardModuleSelection> modules,
            TrRmA3LayoutResourceLedger? a3ResourceLedger,
            bool allowGenericMethodTypeProviders = false)
        {
            _consumer = consumer;
            _artifacts = artifacts;
            _schemaIdentities = schemaIdentities;
            _modules = modules;
            _a3ResourceLedger = a3ResourceLedger;
            _allowGenericMethodTypeProviders
                = allowGenericMethodTypeProviders;
            _a3ResourceLedger?.ConsumeWork(4);
            _imports = [];
            _visitedLocalTypes = [];
            _visitedTypePairs = [];
            _activeAnchorPairs = [];
            MethodProviderBindings = [];
            _cachedProviderTables = default;
            _cachedProviderArtifactIndex = -1;
            _hasCachedProviderTables = false;
        }

        internal ImmutableArray<TrRmStandardTypeProviderBinding> Validate(
            int rootModuleRow)
        {
            if (_artifacts.Length != _schemaIdentities.Length
                || _artifacts.Length == 0 || rootModuleRow <= 0
                || rootModuleRow > _consumer.Modules.Count)
            {
                throw InvalidBinding();
            }

            _a3ResourceLedger?.ConsumeWork();
            var pendingTypes = new Queue<TypeOriginPair>();
            for (var moduleIndex = 0; moduleIndex < _modules.Length;
                moduleIndex++)
            {
                var module = _modules[moduleIndex];
                var schemaIdentity
                    = _schemaIdentities[module.ArtifactIndex];
                var isInitializationReferenceLocalFieldLayout
                    = IsInitializationReferenceLocalFieldLayoutSchemaIdentity(
                        schemaIdentity);
                if (!IsFixedArraySchemaIdentity(schemaIdentity)
                    && !isInitializationReferenceLocalFieldLayout)
                {
                    continue;
                }

                var moduleTables = GetTables(module.ArtifactIndex);
                for (var importRow = 1;
                    importRow <= moduleTables.Imports.Count; importRow++)
                {
                    ConsumeWork();
                    var moduleRefRow = moduleTables.Imports
                        .GetModuleRefForImportRow(importRow);
                    if (moduleTables.ModuleRefs.GetOwnerModule(moduleRefRow)
                        != module.ModuleRow)
                    {
                        continue;
                    }

                    if ((moduleTables.Imports
                            .GetExpectedDefinitionForImportRow(importRow)
                            >> 24) != 0x13)
                    {
                        continue;
                    }

                    var resolution = ResolveImport(module.ArtifactIndex,
                        module.ModuleRow, moduleTables, importRow);
                    AddTypePair(resolution.Expected, resolution.Actual,
                        isInlineStorage: false, pendingTypes);
                }

                var (typeDefStart, typeDefEnd)
                    = moduleTables.Modules.GetTypeDefRange(module.ModuleRow);
                for (var typeDefRow = typeDefStart;
                    typeDefRow < typeDefEnd; typeDefRow++)
                {
                    if (isInitializationReferenceLocalFieldLayout
                        && !moduleTables.TypeDefs.HasDeclaredCopy(typeDefRow))
                    {
                        continue;
                    }

                    if (isInitializationReferenceLocalFieldLayout)
                    {
                        if (!moduleTables.TargetProfiles.TryGetProfile(
                                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                                TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision,
                                out var profileRow))
                        {
                            throw InvalidBinding();
                        }

                        var layoutRow = moduleTables.Layouts
                            .GetLayoutForProfile(typeDefRow,
                                checked((uint)profileRow));
                        if (moduleTables.Layouts.GetLayoutKind(layoutRow)
                            is not (1 or 3))
                        {
                            throw InvalidBinding();
                        }
                    }

                    var (fieldStart, fieldEnd)
                        = moduleTables.TypeDefs.GetFieldRange(typeDefRow);
                    for (var fieldRow = fieldStart; fieldRow < fieldEnd;
                        fieldRow++)
                    {
                        ConsumeWork();
                        CollectStorageType(module.ArtifactIndex,
                            module.ModuleRow,
                            moduleTables.Fields.GetTypeSpecRef(fieldRow),
                            pendingTypes);
                    }
                }
            }

            for (var layoutRow = 1;
                layoutRow <= _consumer.FixedArrayLayouts.Count; layoutRow++)
            {
                ConsumeWork();
                var layout = _consumer.FixedArrayLayouts.GetRow(layoutRow);
                if (layout.OwnerModuleRow != rootModuleRow)
                {
                    continue;
                }

                _ = _consumer.TypeSpecs.TryGetFixedInlineArray(
                    (uint)layout.ArrayTypeSpecRow,
                    out var elementTypeSpecRef, out _);
                CollectStorageType(0, rootModuleRow,
                    elementTypeSpecRef, pendingTypes);
            }

            while (pendingTypes.TryDequeue(out var pair))
            {
                ConsumeWork();
                _a3ResourceLedger?.CheckDepth(pair.Depth);
                ValidateTypePair(pair, pendingTypes);
            }

            ConsumeWork(checked((ulong)_imports.Count + 1UL));
            var orderedBindings
                = new List<TrRmStandardTypeProviderBinding>(_imports.Count);
            foreach (var resolution in _imports.Values)
            {
                orderedBindings.Add(resolution.Binding);
            }

            var resourceLedger = _a3ResourceLedger;
            orderedBindings.Sort((left, right) =>
            {
                resourceLedger?.ConsumeWork();
                var result = left.ConsumerArtifactIndex
                    .CompareTo(right.ConsumerArtifactIndex);
                if (result != 0)
                {
                    return result;
                }

                resourceLedger?.ConsumeWork();
                return left.ImportRow.CompareTo(right.ImportRow);
            });

            ConsumeWork(checked((ulong)orderedBindings.Count + 1UL));
            return [.. orderedBindings];
        }

        internal TrRmStandardFieldProviderValidation
            ValidateFieldDefImports(int rootModuleRow,
                bool nestedFieldDestinations,
                bool allowC1M1MethodImports = false)
        {
            if (_artifacts.Length != _schemaIdentities.Length
                || _artifacts.Length == 0 || rootModuleRow <= 0
                || rootModuleRow > _consumer.Modules.Count)
            {
                throw InvalidFieldBinding();
            }

            var pendingTypes = new Queue<TypeOriginPair>();
            var fieldBindings
                = new List<TrRmStandardFieldProviderBinding>();
            var ownerImports
                = new Dictionary<(int ArtifactIndex, int ModuleRow,
                    int TypeDefRow), int>();
            foreach (var module in _modules)
            {
                var moduleTables = GetTables(module.ArtifactIndex);
                if (TrRmExecutableCompiler
                    .IsSelectedC1EnumClosureSchemaIdentity(
                        _schemaIdentities[module.ArtifactIndex]))
                {
                    var (typeDefStart, typeDefEnd)
                        = moduleTables.Modules.GetTypeDefRange(
                            module.ModuleRow);
                    for (var typeDefRow = typeDefStart;
                        typeDefRow < typeDefEnd; typeDefRow++)
                    {
                        var (fieldStart, fieldEnd)
                            = moduleTables.TypeDefs.GetFieldRange(typeDefRow);
                        for (var fieldRow = fieldStart;
                            fieldRow < fieldEnd; fieldRow++)
                        {
                            ValidateC1TypeSpecImportUses(moduleTables,
                                moduleTables.Fields.GetTypeSpecRef(fieldRow));
                        }
                    }
                }

                for (var importRow = 1;
                    importRow <= moduleTables.Imports.Count; importRow++)
                {
                    var moduleRefRow = moduleTables.Imports
                        .GetModuleRefForImportRow(importRow);
                    if (moduleTables.ModuleRefs.GetOwnerModule(moduleRefRow)
                            != module.ModuleRow
                        || (moduleTables.Imports
                            .GetExpectedDefinitionForImportRow(importRow)
                            >> 24) != 0x13)
                    {
                        continue;
                    }

                    var typeDefRow = checked((int)(
                        moduleTables.Imports
                            .GetExpectedDefinitionForImportRow(importRow)
                        & 0x00FF_FFFF));
                    if (!ownerImports.TryAdd(
                            (module.ArtifactIndex, module.ModuleRow,
                                typeDefRow), importRow))
                    {
                        throw InvalidFieldBinding();
                    }
                }
            }

            for (var moduleIndex = 0; moduleIndex < _modules.Length;
                moduleIndex++)
            {
                var module = _modules[moduleIndex];
                var moduleTables = GetTables(module.ArtifactIndex);
                var usedModuleRefs
                    = new bool[moduleTables.ModuleRefs.Count + 1];
                var isRootModule = module.ArtifactIndex == 0
                    && module.ModuleRow == rootModuleRow;
                var isEN1 = TrRmExecutableCompiler
                    .IsSelectedC1EnumClosureSchemaIdentity(
                        _schemaIdentities[module.ArtifactIndex]);
                for (var importRow = 1;
                    importRow <= moduleTables.Imports.Count; importRow++)
                {
                    ConsumeWork();
                    var moduleRefRow = moduleTables.Imports
                        .GetModuleRefForImportRow(importRow);
                    if (moduleTables.ModuleRefs.GetOwnerModule(moduleRefRow)
                        != module.ModuleRow)
                    {
                        continue;
                    }

                    var expectedDefinition = moduleTables.Imports
                        .GetExpectedDefinitionForImportRow(importRow);
                    switch (expectedDefinition >> 24)
                    {
                        case 0x13:
                        {
                            var resolution = ResolveImport(
                                module.ArtifactIndex, module.ModuleRow,
                                moduleTables, importRow);
                            AddTypePair(resolution.Expected,
                                resolution.Actual, isInlineStorage: false,
                                pendingTypes);
                            usedModuleRefs[moduleRefRow] = true;
                            break;
                        }

                        case 0x14:
                        {
                            var expectedFieldRow = checked((int)(
                                expectedDefinition & 0x00FF_FFFF));
                            var expectedOwnerTypeDefRow
                                = moduleTables.Fields.GetOwnerTypeDef(
                                    expectedFieldRow);
                            if (!ownerImports.TryGetValue(
                                    (module.ArtifactIndex,
                                        module.ModuleRow,
                                        expectedOwnerTypeDefRow),
                                    out var ownerImportRow))
                            {
                                throw InvalidFieldBinding();
                            }

                            fieldBindings.Add(ResolveFieldImport(
                                module.ArtifactIndex, module.ModuleRow,
                                moduleTables, importRow, expectedDefinition,
                                ownerImportRow, pendingTypes));
                            usedModuleRefs[moduleRefRow] = true;
                            break;
                        }

                        default:
                            if (isEN1)
                            {
                                throw InvalidFieldBinding();
                            }
                            if (!isRootModule
                                && (nestedFieldDestinations
                                    || allowC1M1MethodImports)
                                && (expectedDefinition >> 24) == 0x16)
                            {
                                usedModuleRefs[moduleRefRow] = true;
                                break;
                            }
                            if (!isRootModule || isEN1)
                            {
                                throw new NotSupportedException(
                                    "A Standard FieldDef provider Module may retain only the selected profile's provider Imports.");
                            }
                            break;
                    }
                }

                if (!isRootModule)
                {
                    var (moduleRefStart, moduleRefEnd)
                        = moduleTables.Modules.GetModuleRefRange(
                            module.ModuleRow);
                    for (var moduleRefRow = moduleRefStart;
                        moduleRefRow < moduleRefEnd; moduleRefRow++)
                    {
                        if (!usedModuleRefs[moduleRefRow])
                        {
                            throw InvalidFieldBinding();
                        }
                    }
                }
            }

            while (pendingTypes.TryDequeue(out var pair))
            {
                ConsumeWork();
                ValidateTypePair(pair, pendingTypes);
            }

            ValidateRetainedFieldReferenceUses(ownerImports, fieldBindings);

            var typeBindings
                = new List<TrRmStandardTypeProviderBinding>(_imports.Count);
            foreach (var resolution in _imports.Values)
            {
                typeBindings.Add(resolution.Binding);
            }

            typeBindings.Sort(static (left, right) =>
            {
                var result = left.ConsumerArtifactIndex.CompareTo(
                    right.ConsumerArtifactIndex);
                return result != 0
                    ? result
                    : left.ImportRow.CompareTo(right.ImportRow);
            });
            fieldBindings.Sort(static (left, right) =>
            {
                var result = left.ConsumerArtifactIndex.CompareTo(
                    right.ConsumerArtifactIndex);
                if (result != 0)
                {
                    return result;
                }

                result = left.ConsumerModuleRow.CompareTo(
                    right.ConsumerModuleRow);
                return result != 0
                    ? result
                    : left.ImportRow.CompareTo(right.ImportRow);
            });
            return new TrRmStandardFieldProviderValidation(
                [.. typeBindings], [.. fieldBindings],
                MethodProviderBindings);
        }

        internal ImmutableArray<TrRmStandardTypeProviderBinding>
            ValidateC1M1TypeDefImports(int rootModuleRow)
        {
            if (_artifacts.Length != _schemaIdentities.Length
                || _artifacts.Length == 0 || rootModuleRow <= 0
                || rootModuleRow > _consumer.Modules.Count)
            {
                throw InvalidBinding();
            }

            var pendingTypes = new Queue<TypeOriginPair>();
            foreach (var module in _modules)
            {
                var tables = GetTables(module.ArtifactIndex);
                var usedModuleRefs = new bool[tables.ModuleRefs.Count + 1];
                var (typeDefStart, typeDefEnd)
                    = tables.Modules.GetTypeDefRange(module.ModuleRow);
                for (var typeDefRow = typeDefStart;
                    typeDefRow < typeDefEnd; typeDefRow++)
                {
                    var (fieldStart, fieldEnd)
                        = tables.TypeDefs.GetFieldRange(typeDefRow);
                    for (var fieldRow = fieldStart; fieldRow < fieldEnd;
                        fieldRow++)
                    {
                        ValidateC1TypeSpecImportUses(tables,
                            tables.Fields.GetTypeSpecRef(fieldRow));
                    }
                }

                for (var importRow = 1;
                    importRow <= tables.Imports.Count; importRow++)
                {
                    ConsumeWork();
                    var moduleRefRow = tables.Imports
                        .GetModuleRefForImportRow(importRow);
                    if (tables.ModuleRefs.GetOwnerModule(moduleRefRow)
                        != module.ModuleRow)
                    {
                        continue;
                    }

                    var expectedDefinition = tables.Imports
                        .GetExpectedDefinitionForImportRow(importRow);
                    var expectedKind = expectedDefinition >> 24;
                    if (expectedKind == 0x16)
                    {
                        usedModuleRefs[moduleRefRow] = true;
                        continue;
                    }
                    if (expectedKind == 0x14)
                    {
                        usedModuleRefs[moduleRefRow] = true;
                        continue;
                    }
                    if (expectedKind != 0x13)
                    {
                        throw new NotSupportedException(
                            "A selected C1 class type-reference Module may retain only TypeDef, FieldDef and MethodDef Imports.");
                    }

                    var resolution = ResolveImport(module.ArtifactIndex,
                        module.ModuleRow, tables, importRow);
                    AddTypePair(resolution.Expected, resolution.Actual,
                        isInlineStorage: false, pendingTypes);
                    usedModuleRefs[moduleRefRow] = true;
                }

                var (moduleRefStart, moduleRefEnd)
                    = tables.Modules.GetModuleRefRange(module.ModuleRow);
                for (var moduleRefRow = moduleRefStart;
                    moduleRefRow < moduleRefEnd; moduleRefRow++)
                {
                    if (!usedModuleRefs[moduleRefRow])
                    {
                        throw InvalidBinding();
                    }
                }
            }

            while (pendingTypes.TryDequeue(out var pair))
            {
                ConsumeWork();
                ValidateTypePair(pair, pendingTypes);
            }

            var orderedBindings
                = new List<TrRmStandardTypeProviderBinding>(_imports.Count);
            foreach (var resolution in _imports.Values)
            {
                orderedBindings.Add(resolution.Binding);
            }

            orderedBindings.Sort(static (left, right) =>
            {
                var result = left.ConsumerArtifactIndex.CompareTo(
                    right.ConsumerArtifactIndex);
                if (result != 0)
                {
                    return result;
                }

                result = left.ConsumerModuleRow.CompareTo(
                    right.ConsumerModuleRow);
                return result != 0
                    ? result
                    : left.ImportRow.CompareTo(right.ImportRow);
            });
            MethodProviderBindings = ValidateC1M1MethodDefImports(
                rootModuleRow);
            return [.. orderedBindings];
        }

        private ImmutableArray<TrRmStandardMethodProviderBinding>
            ValidateC1M1MethodDefImports(int rootModuleRow)
        {
            if (rootModuleRow <= 0
                || rootModuleRow > _consumer.Modules.Count)
            {
                throw InvalidBinding();
            }

            var bindings = new List<TrRmStandardMethodProviderBinding>();
            var supportsGenericClassMethods
                = _schemaIdentities.Length != 0
                    && _schemaIdentities.All(
                        StandardGenericMethodSchema.IsGenericMethodCalls);
            var pendingTypes = new Queue<TypeOriginPair>();
            var methodRows = new Dictionary<(
                int ArtifactIndex, int OwnerImportRow,
                int ExpectedMethodDefRow), int>();
            foreach (var ownerResolution in _imports.Values)
            {
                var expectedTables = GetTables(
                    ownerResolution.Expected.ArtifactIndex);
                var providerTables = GetTables(
                    ownerResolution.Actual.ArtifactIndex);
                var expectedOwnerTypeDefRow
                    = ownerResolution.Expected.TypeDefRow;
                var providerOwnerTypeDefRow
                    = ownerResolution.Actual.TypeDefRow;
                if (expectedTables.TypeDefs.GetKind(
                        expectedOwnerTypeDefRow) != 2)
                {
                    continue;
                }

                var (expectedMethodFirst, expectedMethodEnd)
                    = expectedTables.TypeDefs.GetMethodRange(
                        expectedOwnerTypeDefRow);
                var (providerMethodFirst, providerMethodEnd)
                    = providerTables.TypeDefs.GetMethodRange(
                        providerOwnerTypeDefRow);
                if (expectedMethodEnd - expectedMethodFirst
                    != providerMethodEnd - providerMethodFirst)
                {
                    throw InvalidBinding();
                }

                for (var ordinal = 0;
                    ordinal < expectedMethodEnd - expectedMethodFirst;
                    ordinal++)
                {
                    var expectedMethodRow
                        = expectedMethodFirst + ordinal;
                    var providerMethodRow
                        = providerMethodFirst + ordinal;
                    methodRows.Add((
                        ownerResolution.Binding.ConsumerArtifactIndex,
                        ownerResolution.Binding.ImportRow,
                        expectedMethodRow), providerMethodRow);
                    var methodContext = this;
                    var methodTypesMatch = MethodDeclarationTypesMatch(
                        ref methodContext, expectedTables,
                            ownerResolution.Expected.ArtifactIndex,
                            ownerResolution.Expected.ModuleRow,
                            expectedOwnerTypeDefRow,
                            expectedMethodRow, providerTables,
                            ownerResolution.Actual.ArtifactIndex,
                            ownerResolution.Actual.ModuleRow,
                            providerOwnerTypeDefRow,
                            providerMethodRow, pendingTypes);
                    _cachedProviderTables
                        = methodContext._cachedProviderTables;
                    _cachedProviderArtifactIndex
                        = methodContext._cachedProviderArtifactIndex;
                    _hasCachedProviderTables
                        = methodContext._hasCachedProviderTables;
                    if (!methodTypesMatch)
                    {
                        throw InvalidBinding();
                    }
                }
            }

            foreach (var module in _modules)
            {
                var consumer = GetTables(module.ArtifactIndex);
                for (var importRow = 1;
                    importRow <= consumer.Imports.Count; importRow++)
                {
                    ConsumeWork();
                    var moduleRefRow = consumer.Imports
                        .GetModuleRefForImportRow(importRow);
                    if (consumer.ModuleRefs.GetOwnerModule(moduleRefRow)
                        != module.ModuleRow)
                    {
                        continue;
                    }

                    var expectedDefinition = consumer.Imports
                        .GetExpectedDefinitionForImportRow(importRow);
                    if ((expectedDefinition >> 24) != 0x16)
                    {
                        continue;
                    }

                    var expectedMethodRow = checked((int)(
                        expectedDefinition & 0x00FF_FFFF));
                    var expectedOwnerTypeDefRow
                        = consumer.Methods.GetOwnerType(
                            expectedMethodRow);
                    if (expectedOwnerTypeDefRow == 0
                        || consumer.TypeDefs.GetKind(
                            expectedOwnerTypeDefRow) != 2)
                    {
                        continue;
                    }
                    var expectedGenericCount = consumer.Signatures
                        .GetGenericCount(consumer.Methods.GetSignature(
                            expectedMethodRow));
                    if (expectedGenericCount != 0
                        && !supportsGenericClassMethods)
                    {
                        throw new NotSupportedException(
                            "Generic class MethodDef Imports require the exact CM1 GenericMethodCalls profile.");
                    }

                    var importBinding = consumer.Imports
                        .GetBindingForImportRow(importRow);
                    var ownerImportRows = new List<int>();
                    ImportResolution ownerResolution = default;
                    for (var ownerImportRow = 1;
                        ownerImportRow <= consumer.Imports.Count;
                        ownerImportRow++)
                    {
                        ConsumeWork();
                        if (consumer.Imports
                                .GetExpectedDefinitionForImportRow(
                                    ownerImportRow)
                                != (0x1300_0000u
                                    | checked((uint)expectedOwnerTypeDefRow))
                            || !_imports.TryGetValue((
                                    module.ArtifactIndex, ownerImportRow),
                                out var candidateOwnerResolution))
                        {
                            continue;
                        }

                        var ownerBinding = consumer.Imports
                            .GetBindingForImportRow(ownerImportRow);
                        if (!StringComparer.Ordinal.Equals(
                                ownerBinding.ProducerIdentity,
                                importBinding.ProducerIdentity)
                            || ownerBinding.TargetRequirement
                                != importBinding.TargetRequirement
                            || ownerBinding.RuntimePolicy
                                != importBinding.RuntimePolicy)
                        {
                            continue;
                        }

                        ownerImportRows.Add(ownerImportRow);
                        ownerResolution = candidateOwnerResolution;
                    }

                    if (ownerImportRows.Count != 1
                        || ownerResolution.Expected.TypeDefRow
                            != expectedOwnerTypeDefRow
                        || !methodRows.TryGetValue((
                                module.ArtifactIndex,
                                ownerImportRows[0], expectedMethodRow),
                            out var mappedProviderMethodRow))
                    {
                        throw InvalidBinding();
                    }

                    var selected = FindModule(
                        importBinding.ProducerIdentity);
                    if (selected.ArtifactIndex == module.ArtifactIndex
                        && selected.ModuleRow == module.ModuleRow)
                    {
                        throw InvalidBinding();
                    }

                    var provider = GetTables(selected.ArtifactIndex);
                    if (provider.Modules.GetRequirements(
                            selected.ModuleRow)
                        != (importBinding.TargetRequirement,
                            importBinding.RuntimePolicy)
                        || !provider.Exports.TryGetDefinitionForKey(
                            selected.ModuleRow, importBinding.ExportKey,
                            out var providerDefinition)
                        || (providerDefinition >> 24) != 0x16
                        || !provider.Exports.TryGetExportRowForKey(
                            selected.ModuleRow, importBinding.ExportKey,
                            out var exportRow))
                    {
                        throw InvalidBinding();
                    }

                    var providerMethodRow = checked((int)(
                        providerDefinition & 0x00FF_FFFF));
                    var providerOwnerTypeDefRow = provider.Methods
                        .GetOwnerType(providerMethodRow);
                    if (providerMethodRow != mappedProviderMethodRow
                        || providerOwnerTypeDefRow
                            != ownerResolution.Actual.TypeDefRow
                        || provider.TypeDefs.GetKind(
                            providerOwnerTypeDefRow) != 2
                        || provider.TypeDefs.GetOwnerModule(
                            providerOwnerTypeDefRow)
                            != selected.ModuleRow
                        || !StringComparer.Ordinal.Equals(
                            consumer.Methods.GetName(expectedMethodRow),
                            provider.Methods.GetName(providerMethodRow)))
                    {
                        throw InvalidBinding();
                    }

                    var expectedRole = 0;
                    var providerRole = 0;
                    var expectedHasConstruction
                        = consumer.Contracts.HasConstructionClause(
                            expectedMethodRow);
                    var providerHasConstruction
                        = provider.Contracts.HasConstructionClause(
                            providerMethodRow);
                    if (expectedHasConstruction != providerHasConstruction)
                    {
                        throw InvalidBinding();
                    }
                    if (expectedHasConstruction)
                    {
                        var expectedConstruction
                            = consumer.Contracts.GetConstructionClause(
                                expectedMethodRow);
                        var providerConstruction
                            = provider.Contracts.GetConstructionClause(
                                providerMethodRow);
                        expectedRole = expectedConstruction.RoleTag;
                        providerRole = providerConstruction.RoleTag;
                        if (expectedRole != providerRole
                            || expectedRole == 3
                            || ((expectedConstruction.PropertyDefRow == 0)
                                != (providerConstruction.PropertyDefRow == 0)))
                        {
                            throw InvalidBinding();
                        }

                        if (expectedRole == 1
                            && !InitializedMembersMatch(consumer,
                                expectedOwnerTypeDefRow,
                                expectedConstruction.InitializedMemberDefIds,
                                provider, providerOwnerTypeDefRow,
                                providerConstruction.InitializedMemberDefIds))
                        {
                            throw InvalidBinding();
                        }

                        if (expectedRole == 2
                            && MethodOrdinal(consumer,
                                expectedOwnerTypeDefRow,
                                checked((int)expectedConstruction
                                    .InitializerMethodDefRow))
                                != MethodOrdinal(provider,
                                    providerOwnerTypeDefRow,
                                    checked((int)providerConstruction
                                        .InitializerMethodDefRow)))
                        {
                            throw InvalidBinding();
                        }
                    }

                    bindings.Add(new TrRmStandardMethodProviderBinding(
                        module.ArtifactIndex, module.ModuleRow, importRow,
                        moduleRefRow, expectedMethodRow,
                        expectedOwnerTypeDefRow, ownerImportRows[0],
                        selected.ArtifactIndex, selected.ModuleRow,
                        providerOwnerTypeDefRow, providerMethodRow,
                        exportRow, importBinding.ProducerIdentity,
                        importBinding.ExportKey,
                        importBinding.TargetRequirement,
                        importBinding.RuntimePolicy));
                }
            }

            while (pendingTypes.TryDequeue(out var pair))
            {
                ConsumeWork();
                ValidateTypePair(pair, pendingTypes);
            }

            bindings.Sort(static (left, right) =>
            {
                var result = left.ConsumerArtifactIndex.CompareTo(
                    right.ConsumerArtifactIndex);
                if (result != 0)
                {
                    return result;
                }

                result = left.ConsumerModuleRow.CompareTo(
                    right.ConsumerModuleRow);
                if (result != 0)
                {
                    return result;
                }

                result = left.OwnerImportRow.CompareTo(
                    right.OwnerImportRow);
                return result != 0
                    ? result : left.ImportRow.CompareTo(right.ImportRow);
            });
            return [.. bindings];

            static bool MethodDeclarationTypesMatch(
                ref ValidationContext context,
                TrRmStructuralMetadataValidator.ValidatedExecutionTables
                    expectedTables,
                int expectedArtifactIndex, int expectedModuleRow,
                int expectedOwnerTypeDefRow, int expectedMethodRow,
                TrRmStructuralMetadataValidator.ValidatedExecutionTables
                    providerTables,
                int providerArtifactIndex, int providerModuleRow,
                int providerOwnerTypeDefRow, int providerMethodRow,
                Queue<TypeOriginPair> typePairs)
            {
                var expectedFlags = expectedTables.Methods.GetFlags(
                    expectedMethodRow);
                var providerFlags = providerTables.Methods.GetFlags(
                    providerMethodRow);
                var expectedSignature = expectedTables.Methods.GetSignature(
                    expectedMethodRow);
                var providerSignature = providerTables.Methods.GetSignature(
                    providerMethodRow);
                var expectedGenericCount = expectedTables.Signatures
                    .GetGenericCount(expectedSignature);
                var providerGenericCount = providerTables.Signatures
                    .GetGenericCount(providerSignature);
                if (!StringComparer.Ordinal.Equals(
                        expectedTables.Methods.GetName(expectedMethodRow),
                        providerTables.Methods.GetName(providerMethodRow))
                    || expectedFlags != providerFlags
                    || expectedTables.Contracts.GetEffectsMask(
                        expectedMethodRow)
                        != providerTables.Contracts.GetEffectsMask(
                            providerMethodRow)
                    || expectedGenericCount != providerGenericCount)
                {
                    return false;
                }

                var expectedReceiver = expectedTables.Signatures
                    .GetReceiverTypeSpec(expectedSignature);
                var providerReceiver = providerTables.Signatures
                    .GetReceiverTypeSpec(providerSignature);
                if ((expectedReceiver == 0) != (providerReceiver == 0))
                {
                    return false;
                }

                var expectedHasConstruction
                    = expectedTables.Contracts.HasConstructionClause(
                        expectedMethodRow);
                var providerHasConstruction
                    = providerTables.Contracts.HasConstructionClause(
                        providerMethodRow);
                if (expectedHasConstruction != providerHasConstruction)
                {
                    return false;
                }
                if (expectedHasConstruction)
                {
                    var expectedConstruction
                        = expectedTables.Contracts.GetConstructionClause(
                            expectedMethodRow);
                    var providerConstruction
                        = providerTables.Contracts.GetConstructionClause(
                            providerMethodRow);
                    if (expectedConstruction.RoleTag
                        != providerConstruction.RoleTag
                        || ((expectedConstruction.PropertyDefRow == 0)
                            != (providerConstruction.PropertyDefRow == 0)))
                    {
                        return false;
                    }

                    if (expectedConstruction.RoleTag == 1
                        && !InitializedMembersMatch(expectedTables,
                            expectedOwnerTypeDefRow,
                            expectedConstruction.InitializedMemberDefIds,
                            providerTables, providerOwnerTypeDefRow,
                            providerConstruction
                                .InitializedMemberDefIds))
                    {
                        return false;
                    }

                    if (expectedConstruction.RoleTag == 2
                        && MethodOrdinal(expectedTables,
                            expectedOwnerTypeDefRow,
                            checked((int)expectedConstruction
                                .InitializerMethodDefRow))
                            != MethodOrdinal(providerTables,
                                providerOwnerTypeDefRow,
                                checked((int)providerConstruction
                                    .InitializerMethodDefRow)))
                    {
                        return false;
                    }
                }

                var (expectedFirstParameter, expectedParameterCount)
                    = expectedTables.Signatures.GetParamRange(
                        expectedSignature);
                var (providerFirstParameter, providerParameterCount)
                    = providerTables.Signatures.GetParamRange(
                        providerSignature);
                if (expectedParameterCount != providerParameterCount)
                {
                    return false;
                }

                var expectedDependencies
                    = TrRmExecutableCompiler
                        .DecodeC1M1ClassMethodDependencyContracts(
                            expectedTables.Contracts
                                .GetDependencyContracts(expectedMethodRow),
                            expectedParameterCount,
                            expectedReceiver != 0);
                var providerDependencies
                    = TrRmExecutableCompiler
                        .DecodeC1M1ClassMethodDependencyContracts(
                            providerTables.Contracts
                                .GetDependencyContracts(providerMethodRow),
                            providerParameterCount,
                            providerReceiver != 0);
                if (!TrRmExecutableCompiler
                        .GenericMethodDependencyContractsSatisfy(
                            expectedDependencies, providerDependencies))
                {
                    return false;
                }

                if (expectedGenericCount != 0)
                {
                    return true;
                }

                if (!MethodTypesMatch(ref context, expectedTables,
                        expectedArtifactIndex, expectedModuleRow,
                        expectedTables.Signatures.GetResultTypeSpec(
                            expectedSignature),
                        providerTables, providerArtifactIndex,
                        providerModuleRow,
                        providerTables.Signatures.GetResultTypeSpec(
                            providerSignature),
                        typePairs)
                    || (expectedReceiver != 0
                        && !MethodTypesMatch(ref context, expectedTables,
                            expectedArtifactIndex, expectedModuleRow,
                            expectedReceiver, providerTables,
                            providerArtifactIndex, providerModuleRow,
                            providerReceiver, typePairs)))
                {
                    return false;
                }

                for (var ordinal = 0;
                    ordinal < expectedParameterCount; ordinal++)
                {
                    var expectedParameter
                        = expectedFirstParameter + ordinal;
                    var providerParameter
                        = providerFirstParameter + ordinal;
                    if (expectedTables.Parameters.GetMode(
                            expectedParameter)
                            != providerTables.Parameters.GetMode(
                                providerParameter)
                        || expectedTables.Parameters.GetFlags(
                            expectedParameter)
                            != providerTables.Parameters.GetFlags(
                                providerParameter)
                        || !MethodTypesMatch(ref context, expectedTables,
                            expectedArtifactIndex, expectedModuleRow,
                            expectedTables.Parameters.GetTypeSpec(
                                expectedParameter),
                            providerTables, providerArtifactIndex,
                            providerModuleRow,
                            providerTables.Parameters.GetTypeSpec(
                                providerParameter), typePairs))
                    {
                        return false;
                    }
                }

                return true;
            }

            static bool MethodTypesMatch(ref ValidationContext context,
                TrRmStructuralMetadataValidator.ValidatedExecutionTables
                    expectedTables,
                int expectedArtifactIndex, int expectedModuleRow,
                uint expectedTypeSpecRef,
                TrRmStructuralMetadataValidator.ValidatedExecutionTables
                    providerTables,
                int providerArtifactIndex, int providerModuleRow,
                uint providerTypeSpecRef,
                Queue<TypeOriginPair> typePairs)
            {
                var reference = expectedTables.TypeSpecs
                    .GetTypeSpecRef(expectedTypeSpecRef);
                var isInlineStorage = false;
                if (reference.Form is 0x02 or 0x03)
                {
                    var typeDefRow = expectedTables.TypeSpecs
                        .GetNominalTypeDefRow(expectedTypeSpecRef,
                            expectedTables.TypeDefs,
                            expectedTables.Imports);
                    isInlineStorage = expectedTables.TypeDefs
                        .GetKind(typeDefRow) == 1;
                }

                return context.TypeSpecsMatch(expectedTables,
                    expectedArtifactIndex, expectedModuleRow,
                    expectedTypeSpecRef, providerTables,
                    providerArtifactIndex, providerModuleRow,
                    providerTypeSpecRef, isInlineStorage, typePairs);
            }

            static int MethodOrdinal(
                TrRmStructuralMetadataValidator.ValidatedExecutionTables
                    tables, int ownerTypeDefRow, int methodDefRow)
            {
                var (first, end)
                    = tables.TypeDefs.GetMethodRange(ownerTypeDefRow);
                return methodDefRow >= first && methodDefRow < end
                    && tables.Methods.GetOwnerType(methodDefRow)
                        == ownerTypeDefRow
                    ? methodDefRow - first : -1;
            }

            static bool InitializedMembersMatch(
                TrRmStructuralMetadataValidator.ValidatedExecutionTables
                    expectedTables, int expectedOwnerTypeDefRow,
                ImmutableArray<uint> expectedMembers,
                TrRmStructuralMetadataValidator.ValidatedExecutionTables
                    providerTables, int providerOwnerTypeDefRow,
                ImmutableArray<uint> providerMembers)
            {
                if (expectedMembers.IsDefault || providerMembers.IsDefault
                    || expectedMembers.Length != providerMembers.Length)
                {
                    return false;
                }

                var (expectedFirst, expectedEnd)
                    = expectedTables.TypeDefs.GetFieldRange(
                        expectedOwnerTypeDefRow);
                var (providerFirst, providerEnd)
                    = providerTables.TypeDefs.GetFieldRange(
                        providerOwnerTypeDefRow);
                for (var ordinal = 0;
                    ordinal < expectedMembers.Length; ordinal++)
                {
                    var expectedField = expectedMembers[ordinal];
                    var providerField = providerMembers[ordinal];
                    if ((expectedField >> 24) != 0x14
                        || (providerField >> 24) != 0x14)
                    {
                        return false;
                    }

                    var expectedFieldRow
                        = checked((int)(expectedField & 0x00FF_FFFF));
                    var providerFieldRow
                        = checked((int)(providerField & 0x00FF_FFFF));
                    if (expectedFieldRow < expectedFirst
                        || expectedFieldRow >= expectedEnd
                        || providerFieldRow < providerFirst
                        || providerFieldRow >= providerEnd
                        || expectedFieldRow - expectedFirst
                            != providerFieldRow - providerFirst
                        || expectedTables.Fields.GetOwnerTypeDef(
                            expectedFieldRow) != expectedOwnerTypeDefRow
                        || providerTables.Fields.GetOwnerTypeDef(
                            providerFieldRow) != providerOwnerTypeDefRow)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        internal TrRmStandardTypeProviderBinding
            ValidateGenericMethodTypeDefImport(int artifactIndex,
                int moduleRow, int importRow)
        {
            if (artifactIndex < 0 || artifactIndex >= _artifacts.Length
                || !StandardGenericMethodSchema.IsGenericMethodCalls(
                    _schemaIdentities[artifactIndex]))
            {
                throw InvalidBinding();
            }

            var tables = GetTables(artifactIndex);
            if ((uint)moduleRow > (uint)tables.Modules.Count
                || moduleRow <= 0
                || (uint)importRow > (uint)tables.Imports.Count
                || importRow <= 0
                || tables.ModuleRefs.GetOwnerModule(
                    tables.Imports.GetModuleRefForImportRow(importRow))
                    != moduleRow
                || (tables.Imports.GetExpectedDefinitionForImportRow(
                        importRow) >> 24) != 0x13)
            {
                throw InvalidBinding();
            }

            var resolution = ResolveImport(artifactIndex, moduleRow,
                tables, importRow);
            var pendingTypes = new Queue<TypeOriginPair>();
            AddTypePair(resolution.Expected, resolution.Actual,
                isInlineStorage: false, pendingTypes);
            while (pendingTypes.TryDequeue(out var pair))
            {
                ValidateTypePair(pair, pendingTypes);
            }

            return resolution.Binding;
        }

        private void ValidateC1TypeSpecImportUses(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            uint typeSpecRef)
        {
            var pending = new Stack<uint>();
            var visitedRows = new HashSet<int>();
            pending.Push(typeSpecRef);
            while (pending.TryPop(out var currentTypeSpecRef))
            {
                ConsumeWork();
                var reference = tables.TypeSpecs.GetTypeSpecRef(
                    currentTypeSpecRef);
                if (!reference.IsRow || !visitedRows.Add(reference.Row))
                {
                    continue;
                }

                var row = tables.TypeSpecs.GetRow(reference.Row);
                switch (reference.Form)
                {
                    case 0x02:
                    {
                        var typeDefRow = checked((int)Read(row, 2));
                        if (tables.Imports.IsExpectedDefinition(
                                0x1300_0000u
                                | checked((uint)typeDefRow)))
                        {
                            throw InvalidBinding();
                        }
                        break;
                    }

                    case 0x03:
                        break;

                    case >= 0x06 and <= 0x0B:
                    case 0x0E:
                        pending.Push(Read(row, 2));
                        break;

                    case 0x0C:
                        pending.Push(Read(row, 3));
                        break;

                    case 0x05:
                        throw new NotSupportedException(
                            "The C1M1 class type-reference projection does not yet support constructed generic field types.");

                    case 0x04:
                    case 0x0D:
                    case 0x0F:
                    case 0x10:
                    case 0x11:
                        throw new NotSupportedException(
                            "The C1M1 class type-reference projection does not support formal, function-pointer, existential, captured or fixed-array field types.");
                }
            }
        }

        private TrRmStandardFieldProviderBinding ResolveFieldImport(
            int artifactIndex, int moduleRow,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
            int importRow, uint expectedDefinition, int ownerImportRow,
            Queue<TypeOriginPair> pendingTypes)
        {
            var expectedFieldRow = checked((int)(
                expectedDefinition & 0x00FF_FFFF));
            var expectedOwnerTypeDefRow
                = consumer.Fields.GetOwnerTypeDef(expectedFieldRow);
            var (expectedFirst, expectedEnd)
                = consumer.TypeDefs.GetFieldRange(expectedOwnerTypeDefRow);
            var fieldOrdinal = expectedFieldRow - expectedFirst;
            if (fieldOrdinal < 0 || expectedFieldRow >= expectedEnd
                || consumer.TypeDefs.GetOwnerModule(
                    expectedOwnerTypeDefRow) != moduleRow)
            {
                throw InvalidFieldBinding();
            }

            var moduleRefRow = consumer.Imports
                .GetModuleRefForImportRow(importRow);
            var fieldBinding = consumer.Imports
                .GetBindingForImportRow(importRow);
            var fieldProducer = consumer.ModuleRefs
                .GetProducerIdentity(moduleRefRow);
            if (!StringComparer.Ordinal.Equals(fieldProducer,
                    fieldBinding.ProducerIdentity)
                || consumer.ModuleRefs.GetRequirements(moduleRefRow)
                    != (fieldBinding.TargetRequirement,
                        fieldBinding.RuntimePolicy)
                || consumer.ModuleRefs.GetOwnerModule(moduleRefRow)
                    != moduleRow)
            {
                throw InvalidFieldBinding();
            }

            var ownerResolution = ResolveImport(artifactIndex, moduleRow,
                consumer, ownerImportRow);
            var ownerBinding = ownerResolution.Binding;
            if (!StringComparer.Ordinal.Equals(
                    ownerBinding.ProducerIdentity,
                    fieldBinding.ProducerIdentity)
                || ownerBinding.TargetRequirement
                    != fieldBinding.TargetRequirement
                || ownerBinding.RuntimePolicy != fieldBinding.RuntimePolicy)
            {
                throw InvalidFieldBinding();
            }

            AddTypePair(ownerResolution.Expected, ownerResolution.Actual,
                isInlineStorage: false, pendingTypes);

            var selected = FindModule(fieldBinding.ProducerIdentity);
            var provider = GetTables(selected.ArtifactIndex);
            if ((selected.ArtifactIndex == artifactIndex
                    && selected.ModuleRow == moduleRow)
                || provider.Modules.GetRequirements(selected.ModuleRow)
                    != (fieldBinding.TargetRequirement,
                        fieldBinding.RuntimePolicy)
                || !provider.Exports.TryGetDefinitionForKey(
                    selected.ModuleRow, fieldBinding.ExportKey,
                    out var actualDefinition)
                || (actualDefinition >> 24) != 0x14)
            {
                throw InvalidFieldBinding();
            }

            var providerFieldRow = checked((int)(
                actualDefinition & 0x00FF_FFFF));
            var providerTypeDefRow = ownerResolution.Actual.TypeDefRow;
            var (providerFirst, providerEnd)
                = provider.TypeDefs.GetFieldRange(providerTypeDefRow);
            var expectedProviderFieldRow = checked(providerFirst + fieldOrdinal);
            if (providerFieldRow != expectedProviderFieldRow
                || providerFieldRow >= providerEnd
                || provider.Fields.GetOwnerTypeDef(providerFieldRow)
                    != providerTypeDefRow
                || !provider.Exports.TryGetExportRowForKey(
                    selected.ModuleRow, fieldBinding.ExportKey,
                    out var exportRow))
            {
                throw InvalidFieldBinding();
            }

            return new TrRmStandardFieldProviderBinding(
                artifactIndex, moduleRow, importRow, moduleRefRow,
                expectedFieldRow, expectedOwnerTypeDefRow, ownerImportRow,
                selected.ArtifactIndex, selected.ModuleRow,
                providerTypeDefRow, providerFieldRow, exportRow,
                fieldBinding.ProducerIdentity, fieldBinding.ExportKey,
                fieldBinding.TargetRequirement, fieldBinding.RuntimePolicy);
        }

        private void ValidateRetainedFieldReferenceUses(
            Dictionary<(int ArtifactIndex, int ModuleRow, int TypeDefRow),
                int> ownerImports,
            List<TrRmStandardFieldProviderBinding> fieldBindings)
        {
            var importedFieldRows
                = new HashSet<(int ArtifactIndex, int ModuleRow, int FieldRow)>();
            var boundImportRows
                = new HashSet<(int ArtifactIndex, int ImportRow)>();
            var fieldBindingsByImport
                = new Dictionary<(int ArtifactIndex, int ImportRow),
                    TrRmStandardFieldProviderBinding>(fieldBindings.Count);
            var fieldBindingsByExpectedField
                = new Dictionary<(int ArtifactIndex, int ModuleRow,
                    int FieldRow), TrRmStandardFieldProviderBinding>(
                    fieldBindings.Count);
            foreach (var binding in fieldBindings)
            {
                ConsumeWork();
                _ = importedFieldRows.Add((binding.ConsumerArtifactIndex,
                    binding.ConsumerModuleRow, binding.ExpectedFieldDefRow));
                _ = boundImportRows.Add((binding.ConsumerArtifactIndex,
                    binding.ImportRow));
                if (!fieldBindingsByImport.TryAdd(
                    (binding.ConsumerArtifactIndex, binding.ImportRow),
                    binding))
                {
                    throw InvalidFieldBinding();
                }
                var expectedFieldKey = (
                    binding.ConsumerArtifactIndex,
                    binding.ConsumerModuleRow,
                    binding.ExpectedFieldDefRow);
                if (!fieldBindingsByExpectedField.TryAdd(expectedFieldKey,
                        binding)
                    && fieldBindingsByExpectedField.TryGetValue(
                        expectedFieldKey, out var existingFieldBinding)
                    && (existingFieldBinding.ExpectedOwnerTypeDefRow
                            != binding.ExpectedOwnerTypeDefRow
                        || existingFieldBinding.OwnerImportRow
                            != binding.OwnerImportRow
                        || existingFieldBinding.ProviderArtifactIndex
                            != binding.ProviderArtifactIndex
                        || existingFieldBinding.ProviderModuleRow
                            != binding.ProviderModuleRow
                        || existingFieldBinding.ProviderTypeDefRow
                            != binding.ProviderTypeDefRow
                        || existingFieldBinding.ProviderFieldDefRow
                            != binding.ProviderFieldDefRow
                        || !StringComparer.Ordinal.Equals(
                            existingFieldBinding.ProducerIdentity,
                            binding.ProducerIdentity)
                        || existingFieldBinding.TargetRequirement
                            != binding.TargetRequirement
                        || existingFieldBinding.RuntimePolicy
                            != binding.RuntimePolicy))
                {
                    throw InvalidFieldBinding();
                }
            }

            var validatedGlobalReferences = new HashSet<int>();
            for (var moduleIndex = 0; moduleIndex < _modules.Length;
                moduleIndex++)
            {
                var module = _modules[moduleIndex];
                var tables = GetTables(module.ArtifactIndex);
                var (methodStart, methodEnd)
                    = tables.Modules.GetMethodDefRange(module.ModuleRow);
                for (var methodRow = methodStart;
                    methodRow < methodEnd; methodRow++)
                {
                    var bodyRow = tables.Methods.GetBodyRow(methodRow);
                    if (bodyRow == 0)
                    {
                        continue;
                    }

                    var bodyBytes = tables.Bodies.GetRow(bodyRow);
                    if (bodyBytes.IsEmpty)
                    {
                        throw InvalidFieldBinding();
                    }

                    var bodyKind = bodyBytes[0];
                    if (bodyKind != 0x01)
                    {
                        tables.Bodies.ValidateSelectedImplementation(
                            bodyRow, tables.Contracts.Count);
                    }

                    switch (bodyKind)
                    {
                        case 0x01:
                        {
                            var body = tables.Bodies.GetRegisterBody(bodyRow,
                                tables.Contracts.Count);
                            for (var mapIndex = 0;
                                mapIndex < body.FieldCount; mapIndex++)
                            {
                                var token = BinaryPrimitives
                                    .ReadUInt32LittleEndian(body.FieldMap[
                                        (mapIndex * sizeof(uint))..]);
                                if ((token >> 24) == 0x1D)
                                {
                                    continue;
                                }

                                ValidateRetainedFieldReference(
                                    module.ArtifactIndex, tables, token,
                                    ownerImports, importedFieldRows,
                                    boundImportRows);
                            }
                            for (var recordIndex = 0;
                                recordIndex < body.RecordCount;
                                recordIndex++)
                            {
                                ConsumeWork();
                                var record = body.GetRecord(recordIndex);
                                if (record[0] != 0x04
                                    || record[4] != 2 || record[13] != 1)
                                {
                                    continue;
                                }

                                var fieldRow = checked((int)
                                    BinaryPrimitives.ReadUInt32LittleEndian(
                                        record.Slice(16, sizeof(uint))));
                                ValidatePlaceSelectionFieldStubReference(
                                    module.ArtifactIndex, module.ModuleRow,
                                    tables, fieldRow, ownerImports,
                                    importedFieldRows, boundImportRows,
                                    fieldBindingsByExpectedField);
                            }
                            break;
                        }

                        case 0x02 or 0x03 or 0x04:
                            ValidateRetainedFieldReference(
                                module.ArtifactIndex, tables,
                                BinaryPrimitives.ReadUInt32LittleEndian(
                                    bodyBytes[4..]),
                                ownerImports, importedFieldRows,
                                boundImportRows);
                            break;

                        case 0x05:
                            break;

                        default:
                            throw InvalidFieldBinding();
                    }
                }

                if (!validatedGlobalReferences.Add(module.ArtifactIndex))
                {
                    continue;
                }

                for (var fieldPathRow = 1;
                    fieldPathRow <= tables.FieldPaths.Count; fieldPathRow++)
                {
                    var fieldCount
                        = tables.FieldPaths.GetFieldCount(fieldPathRow);
                    var fieldReferences = new uint[fieldCount];
                    for (var fieldOrdinal = 0;
                        fieldOrdinal < fieldCount; fieldOrdinal++)
                    {
                        var fieldReference
                            = tables.FieldPaths.GetFieldReference(
                                fieldPathRow, fieldOrdinal);
                        fieldReferences[fieldOrdinal] = fieldReference;
                        ValidateRetainedFieldReference(
                            module.ArtifactIndex, tables,
                            fieldReference,
                            ownerImports, importedFieldRows,
                            boundImportRows);
                    }

                    var usingModuleRow = GetFieldReferenceOwnerModule(
                        module.ArtifactIndex, tables, fieldReferences[0]);
                    ValidateFieldPathOwnerChain(module.ArtifactIndex,
                        usingModuleRow,
                        tables.FieldPaths.GetRootTypeSpecRef(fieldPathRow),
                        fieldReferences, fieldBindingsByImport);
                }

                for (var dependencyFieldRow = 1;
                    dependencyFieldRow
                        <= tables.TargetLayoutDependencyFields.Count;
                    dependencyFieldRow++)
                {
                    ValidateRetainedFieldReference(
                        module.ArtifactIndex, tables,
                        tables.TargetLayoutDependencyFields
                            .GetFieldReference(dependencyFieldRow),
                        ownerImports, importedFieldRows, boundImportRows);
                }

                for (var dependencyRow = 1;
                    dependencyRow <= tables.TargetLayoutDependencies.Count;
                    dependencyRow++)
                {
                    if (tables.TargetLayoutDependencies.GetQueryKind(
                            dependencyRow) is not (3 or 4))
                    {
                        continue;
                    }

                    var (fieldFirst, fieldCount)
                        = tables.TargetLayoutDependencies.GetFieldRange(
                            dependencyRow);
                    var fieldReferences = new uint[fieldCount];
                    for (var fieldOrdinal = 0;
                        fieldOrdinal < fieldCount; fieldOrdinal++)
                    {
                        fieldReferences[fieldOrdinal]
                            = tables.TargetLayoutDependencyFields
                                .GetFieldReference(fieldFirst + fieldOrdinal);
                    }

                    var usingModuleRow = GetFieldReferenceOwnerModule(
                        module.ArtifactIndex, tables, fieldReferences[0]);
                    ValidateFieldPathOwnerChain(module.ArtifactIndex,
                        usingModuleRow,
                        tables.TargetLayoutDependencies
                            .GetSubjectTypeSpecRef(dependencyRow),
                        fieldReferences, fieldBindingsByImport);
                }
            }
        }

        private readonly int GetFieldReferenceOwnerModule(
            int artifactIndex,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            uint fieldReference)
        {
            ConsumeWork();
            var row = checked((int)(fieldReference & 0x00FF_FFFF));
            var moduleRow = (fieldReference >> 24) switch {
                0x14 => tables.TypeDefs.GetOwnerModule(
                    tables.Fields.GetOwnerTypeDef(row)),
                0x1C => tables.ModuleRefs.GetOwnerModule(
                    tables.Imports.GetModuleRefForImportRow(row)),
                _ => throw InvalidFieldBinding(),
            };
            var moduleIsSelected = false;
            foreach (var module in _modules)
            {
                if (module.ArtifactIndex == artifactIndex
                    && module.ModuleRow == moduleRow)
                {
                    moduleIsSelected = true;
                    break;
                }
            }
            if (!moduleIsSelected)
            {
                throw InvalidFieldBinding();
            }
            return moduleRow;
        }

        private void ValidateFieldPathOwnerChain(
            int artifactIndex, int moduleRow, uint rootTypeSpecRef,
            ReadOnlySpan<uint> fieldReferences,
            Dictionary<(int ArtifactIndex, int ImportRow),
                TrRmStandardFieldProviderBinding> fieldBindings)
        {
            if (fieldReferences.IsEmpty
                    || !TryResolveFieldPathRoot(artifactIndex,
                    moduleRow, rootTypeSpecRef, null, out var current))
            {
                throw InvalidFieldBinding();
            }

            for (var ordinal = 0; ordinal < fieldReferences.Length;
                ordinal++)
            {
                ConsumeWork();
                var fieldReference = fieldReferences[ordinal];
                var fieldRow = checked((int)(
                    fieldReference & 0x00FF_FFFF));

                int expectedFieldRow;
                switch (fieldReference >> 24)
                {
                    case 0x14:
                        expectedFieldRow = fieldRow;
                        if (current.Expected.ArtifactIndex != artifactIndex
                            || current.Expected.ModuleRow != moduleRow
                            || current.Expected.TypeDefRow
                                != GetTables(artifactIndex).Fields
                                    .GetOwnerTypeDef(expectedFieldRow)
                            || current.Actual != current.Expected)
                        {
                            throw InvalidFieldBinding();
                        }
                        break;

                    case 0x1C:
                    {
                        if (!fieldBindings.TryGetValue(
                                (artifactIndex, fieldRow),
                                out var binding)
                            || binding.ConsumerModuleRow != moduleRow
                            || binding.ExpectedOwnerTypeDefRow
                                != current.Expected.TypeDefRow
                            || binding.ProviderArtifactIndex
                                != current.Actual.ArtifactIndex
                            || binding.ProviderModuleRow
                                != current.Actual.ModuleRow
                            || binding.ProviderTypeDefRow
                                != current.Actual.TypeDefRow)
                        {
                            throw InvalidFieldBinding();
                        }
                        expectedFieldRow
                            = binding.ExpectedFieldDefRow;
                        break;
                    }

                    default:
                        throw InvalidFieldBinding();
                }

                if (ordinal + 1 == fieldReferences.Length)
                {
                    continue;
                }

                var fieldTypeSpecRef = GetTables(artifactIndex).Fields
                    .GetTypeSpecRef(expectedFieldRow);
                if (!TryResolveInlineFieldPathOwner(artifactIndex,
                    moduleRow, fieldTypeSpecRef,
                    current.Substitutions, out current))
                {
                    throw InvalidFieldBinding();
                }
            }
        }

        private bool TryResolveFieldPathRoot(
            int artifactIndex, int moduleRow, uint typeSpecRef,
            TypeFormalSubstitution? substitutions,
            out ResolvedFieldPathOwner owner)
        {
            if (TryResolveInlineFieldPathOwner(artifactIndex, moduleRow,
                    typeSpecRef, substitutions, out owner))
            {
                return true;
            }

            owner = default;
            if (!TrRmExecutableCompiler
                    .IsSelectedC1ClassInstanceLayoutSchemaIdentity(
                        _schemaIdentities[artifactIndex]))
            {
                return false;
            }

            var tables = GetTables(artifactIndex);
            var reference = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
            if (reference.Qualifiers != 0 || !reference.IsRow
                || reference.Form is not (0x02 or 0x03))
            {
                return false;
            }

            var row = tables.TypeSpecs.GetRow(reference.Row);
            var typeOrigin = ResolveNominalType(tables, artifactIndex,
                moduleRow, reference, row);
            if (!IsClassType(typeOrigin.Expected)
                || !IsClassType(typeOrigin.Actual))
            {
                return false;
            }

            var expectedTables = GetTables(
                typeOrigin.Expected.ArtifactIndex);
            var actualTables = GetTables(typeOrigin.Actual.ArtifactIndex);
            if (expectedTables.TypeDefs.GetGenericRange(
                    typeOrigin.Expected.TypeDefRow).Count != 0
                || actualTables.TypeDefs.GetGenericRange(
                    typeOrigin.Actual.TypeDefRow).Count != 0)
            {
                return false;
            }

            owner = new ResolvedFieldPathOwner(typeOrigin.Expected,
                typeOrigin.Actual, null);
            return true;
        }

        private bool TryResolveInlineFieldPathOwner(
            int artifactIndex, int moduleRow, uint typeSpecRef,
            TypeFormalSubstitution? substitutions,
            out ResolvedFieldPathOwner owner)
        {
            owner = default;
            ConsumeWork();
            var tables = GetTables(artifactIndex);
            var reference = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
            if (reference.Qualifiers != 0 || !reference.IsRow)
            {
                return false;
            }

            if (reference.Form == 0x04)
            {
                var formalRow = checked((int)Read(
                    tables.TypeSpecs.GetRow(reference.Row), 2));
                var ownerDefinition
                    = tables.GenericParams.GetOwnerDefId(formalRow);
                if ((ownerDefinition >> 24) != 0x13
                    || tables.GenericParams.GetFormalKind(formalRow) != 1)
                {
                    return false;
                }

                var ownerTypeDefRow = checked((int)(
                    ownerDefinition & 0x00FF_FFFF));
                var formalOrdinal = checked((int)
                    tables.GenericParams.GetOrdinal(formalRow));
                for (var frame = substitutions; frame is not null;
                    frame = frame.Parent)
                {
                    if (frame.ExpectedOwner.ArtifactIndex != artifactIndex
                        || frame.ExpectedOwner.ModuleRow != moduleRow
                        || frame.ExpectedOwner.TypeDefRow
                            != ownerTypeDefRow)
                    {
                        continue;
                    }

                    var ownerTables = GetTables(
                        frame.ExpectedOwner.ArtifactIndex);
                    var (formalFirst, formalCount)
                        = ownerTables.TypeDefs.GetGenericRange(
                            frame.ExpectedOwner.TypeDefRow);
                    if ((uint)formalOrdinal >= (uint)formalCount
                        || formalRow != formalFirst + formalOrdinal
                        || (uint)formalOrdinal
                            >= (uint)frame.Arguments.Length
                        || ownerTables.GenericParams.GetFormalKind(
                            formalFirst + formalOrdinal) != 1)
                    {
                        return false;
                    }

                    return TryResolveInlineFieldPathOwner(artifactIndex,
                        moduleRow, frame.Arguments[formalOrdinal],
                        frame.Parent, out owner);
                }

                return false;
            }

            if (reference.Form is not (0x02 or 0x03 or 0x05))
            {
                return false;
            }

            var tablesForOwner = GetTables(artifactIndex);
            var typeDefReference = reference;
            var typeDefRowBytes = tablesForOwner.TypeSpecs.GetRow(
                reference.Row);
            var typeArgumentCount = 0;
            if (reference.Form == 0x05)
            {
                typeDefReference = tablesForOwner.TypeSpecs.GetTypeSpecRef(
                    Read(typeDefRowBytes, 2));
                typeArgumentCount
                    = tablesForOwner.TypeSpecs
                        .GetConstructedArgumentCount(reference.Row);
            }

            if (typeDefReference.Form is not (0x02 or 0x03)
                || !typeDefReference.IsRow)
            {
                return false;
            }
            var typeDefRowData = tablesForOwner.TypeSpecs.GetRow(
                typeDefReference.Row);
            var typeOrigin = ResolveNominalType(tablesForOwner,
                artifactIndex, moduleRow, typeDefReference, typeDefRowData);
            if (!IsInlineStructType(typeOrigin.Expected)
                || !IsInlineStructType(typeOrigin.Actual))
            {
                return false;
            }

            var expectedTables = GetTables(
                typeOrigin.Expected.ArtifactIndex);
            var actualTables = GetTables(typeOrigin.Actual.ArtifactIndex);
            var (expectedFormalFirst, expectedFormalCount)
                = expectedTables.TypeDefs.GetGenericRange(
                    typeOrigin.Expected.TypeDefRow);
            var (actualFormalFirst, actualFormalCount)
                = actualTables.TypeDefs.GetGenericRange(
                    typeOrigin.Actual.TypeDefRow);
            if (expectedFormalCount != actualFormalCount
                || expectedFormalCount != typeArgumentCount)
            {
                return false;
            }

            var arguments = ImmutableArray.CreateBuilder<uint>(
                typeArgumentCount);
            for (var ordinal = 0; ordinal < typeArgumentCount; ordinal++)
            {
                ConsumeWork();
                var expectedFormalKind
                    = expectedTables.GenericParams.GetFormalKind(
                        expectedFormalFirst + ordinal);
                var actualFormalKind
                    = actualTables.GenericParams.GetFormalKind(
                        actualFormalFirst + ordinal);
                if (expectedFormalKind != actualFormalKind)
                {
                    return false;
                }

                var argument = tablesForOwner.TypeSpecs
                    .GetConstructedArgument(reference.Row, ordinal);
                if (expectedFormalKind == 1)
                {
                    _ = tablesForOwner.TypeSpecs.GetTypeSpecRef(argument);
                }
                else if (expectedFormalKind != 2)
                {
                    return false;
                }
                arguments.Add(argument);
            }

            owner = new ResolvedFieldPathOwner(
                typeOrigin.Expected, typeOrigin.Actual,
                new TypeFormalSubstitution(typeOrigin.Expected,
                    typeOrigin.Actual, arguments.MoveToImmutable(),
                    substitutions));
            return true;
        }

        private bool IsInlineStructType(TypeOrigin type)
        {
            var tables = GetTables(type.ArtifactIndex);
            return ((tables.TypeDefs.GetFlags(type.TypeDefRow) >> 2) & 7) == 1;
        }

        private bool IsClassType(TypeOrigin type)
        {
            var tables = GetTables(type.ArtifactIndex);
            return ((tables.TypeDefs.GetFlags(type.TypeDefRow) >> 2) & 7) == 2;
        }

        private static void ValidateRetainedFieldReference(
            int artifactIndex,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            uint fieldReference,
            Dictionary<(int ArtifactIndex, int ModuleRow, int TypeDefRow),
                int> ownerImports,
            HashSet<(int ArtifactIndex, int ModuleRow, int FieldRow)>
                importedFieldRows,
            HashSet<(int ArtifactIndex, int ImportRow)> boundImportRows)
        {
            var row = checked((int)(fieldReference & 0x00FF_FFFF));
            switch (fieldReference >> 24)
            {
                case 0x14:
                {
                    var ownerTypeDefRow
                        = tables.Fields.GetOwnerTypeDef(row);
                    var ownerModuleRow = tables.TypeDefs
                        .GetOwnerModule(ownerTypeDefRow);
                    if (ownerImports.ContainsKey((artifactIndex,
                            ownerModuleRow, ownerTypeDefRow))
                        && (!importedFieldRows.Contains((artifactIndex,
                                ownerModuleRow, row))
                            || fieldReference >> 24 == 0x14))
                    {
                        throw InvalidFieldBinding();
                    }

                    break;
                }

                case 0x1C:
                    if (!boundImportRows.Contains((artifactIndex, row)))
                    {
                        throw InvalidFieldBinding();
                    }
                    break;

                default:
                    throw InvalidFieldBinding();
            }
        }

        private static void ValidatePlaceSelectionFieldStubReference(
            int artifactIndex, int moduleRow,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int fieldRow,
            Dictionary<(int ArtifactIndex, int ModuleRow, int TypeDefRow),
                int> ownerImports,
            HashSet<(int ArtifactIndex, int ModuleRow, int FieldRow)>
                importedFieldRows,
            HashSet<(int ArtifactIndex, int ImportRow)> boundImportRows,
            Dictionary<(int ArtifactIndex, int ModuleRow, int FieldRow),
                TrRmStandardFieldProviderBinding>
                fieldBindingsByExpectedField)
        {
            if (fieldRow <= 0 || fieldRow > tables.Fields.Count)
            {
                throw InvalidFieldBinding();
            }

            var ownerTypeDefRow
                = tables.Fields.GetOwnerTypeDef(fieldRow);
            if (tables.TypeDefs.GetOwnerModule(ownerTypeDefRow)
                != moduleRow)
            {
                throw InvalidFieldBinding();
            }

            if (!ownerImports.TryGetValue((artifactIndex, moduleRow,
                    ownerTypeDefRow), out var ownerImportRow))
            {
                return;
            }

            var fieldKey = (artifactIndex, moduleRow, fieldRow);
            if (!importedFieldRows.Contains(fieldKey)
                || !fieldBindingsByExpectedField.TryGetValue(fieldKey,
                    out var fieldBinding)
                || fieldBinding.OwnerImportRow != ownerImportRow
                || !boundImportRows.Contains((artifactIndex,
                    fieldBinding.ImportRow)))
            {
                throw InvalidFieldBinding();
            }
        }

        internal void ValidateMethodTypeSpecPairs(
            ImmutableArray<TrRmStandardMethodTypeSpecPair> typeSpecPairs)
        {
            var pendingTypes = new Queue<TypeOriginPair>();
            foreach (var pair in typeSpecPairs)
            {
                ConsumeWork();
                var expectedTables = GetTables(pair.ExpectedArtifactIndex);
                var actualTables = GetTables(pair.ActualArtifactIndex);
                var expectedTypeSpec = expectedTables.TypeSpecs
                    .GetTypeSpecRef(pair.ExpectedTypeSpecRef);
                var actualTypeSpec = actualTables.TypeSpecs
                    .GetTypeSpecRef(pair.ActualTypeSpecRef);
                if (expectedTypeSpec.Form == 0x0F
                    || actualTypeSpec.Form == 0x0F)
                {
                    if (pair.ExpectedTypeSpecRef
                        != pair.ActualTypeSpecRef)
                    {
                        throw InvalidBinding();
                    }

                    continue;
                }

                if (!TypeSpecsMatch(expectedTables,
                        pair.ExpectedArtifactIndex, pair.ExpectedModuleRow,
                        pair.ExpectedTypeSpecRef, actualTables,
                        pair.ActualArtifactIndex, pair.ActualModuleRow,
                        pair.ActualTypeSpecRef, isInlineStorage: true,
                        pendingTypes: pendingTypes))
                {
                    throw InvalidBinding();
                }
            }

            while (pendingTypes.TryDequeue(out var pair))
            {
                ConsumeWork();
                ValidateTypePair(pair, pendingTypes);
            }
        }

        private void CollectStorageType(int artifactIndex, int moduleRow,
            uint typeSpecRef, Queue<TypeOriginPair> pendingTypes)
        {
            ConsumeWork();
            var pending = new Stack<TypeSpecLocation>();
            PushLocation(pending, new TypeSpecLocation(artifactIndex,
                moduleRow, typeSpecRef, IsInlineStorage: true));
            while (pending.TryPop(out var location))
            {
                ConsumeWork();
                _a3ResourceLedger?.CheckDepth(location.Depth);
                var tables = GetTables(location.ArtifactIndex);
                var reference = tables.TypeSpecs.GetTypeSpecRef(
                    location.TypeSpecRef);
                if (!reference.IsRow)
                {
                    if (reference.TypeCode == 0x8C)
                    {
                        continue;
                    }

                    continue;
                }

                var row = tables.TypeSpecs.GetRow(reference.Row);
                switch (reference.Form)
                {
                    case 0x01:
                        break;

                    case 0x02:
                        if (location.IsInlineStorage)
                        {
                            var typeDefRow = checked((int)Read(row, 2));
                            AddTypePair(new TypeOrigin(
                                    location.ArtifactIndex,
                                    tables.TypeDefs.GetOwnerModule(typeDefRow),
                                    typeDefRow),
                                new TypeOrigin(location.ArtifactIndex,
                                    tables.TypeDefs.GetOwnerModule(typeDefRow),
                                    typeDefRow),
                                isInlineStorage: true, pendingTypes,
                                location.Depth);
                            CollectLocalType(location.ArtifactIndex,
                                typeDefRow, pending, location.Depth);
                        }
                        break;

                    case 0x03:
                    {
                        var importRow = checked((int)Read(row, 2));
                        var resolution = ResolveImport(location.ArtifactIndex,
                            location.ModuleRow, tables, importRow);
                        AddTypePair(resolution.Expected, resolution.Actual,
                            location.IsInlineStorage, pendingTypes,
                            location.Depth);
                        break;
                    }

                    // Resolve a formal only in its constructed owner/actual context.
                    case 0x04:
                        break;

                    case 0x05:
                    {
                        var definitionRef = Read(row, 2);
                        var definition = tables.TypeSpecs.GetTypeSpecRef(
                            definitionRef);
                        var definitionRow = tables.TypeSpecs.GetRow(
                            definition.Row);
                        var origin = ResolveNominalType(tables,
                            location.ArtifactIndex, location.ModuleRow,
                            definition, definitionRow);
                        AddTypePair(origin.Expected, origin.Actual,
                            location.IsInlineStorage, pendingTypes,
                            location.Depth);
                        var argumentCount
                            = tables.TypeSpecs.GetConstructedArgumentCount(
                                reference.Row);
                        for (var ordinal = argumentCount - 1;
                            ordinal >= 0; ordinal--)
                        {
                            PushLocation(pending, new TypeSpecLocation(
                                location.ArtifactIndex, location.ModuleRow,
                                tables.TypeSpecs.GetConstructedArgument(
                                    reference.Row, ordinal),
                                IsInlineStorage: false,
                                Depth: checked(location.Depth + 1)));
                        }
                        break;
                    }

                    case >= 0x06 and <= 0x0B:
                    case 0x0E:
                        PushLocation(pending, new TypeSpecLocation(
                            location.ArtifactIndex, location.ModuleRow,
                            Read(row, 2), IsInlineStorage: false,
                            Depth: checked(location.Depth + 1)));
                        break;

                    case 0x0C:
                        if (location.IsInlineStorage
                            && reference.TypeCode != 0x8C)
                        {
                            throw InvalidBinding();
                        }

                        PushLocation(pending, new TypeSpecLocation(
                            location.ArtifactIndex, location.ModuleRow,
                            Read(row, 3), IsInlineStorage: false,
                            Depth: checked(location.Depth + 1)));
                        break;

                    case 0x0D:
                        break;

                    case 0x11:
                        if (!tables.TargetProfiles.TryGetProfile(
                                TrRmTargetProfileTableReader
                                    .WindowsX64NativeProfileId,
                                TrRmTargetProfileTableReader
                                    .WindowsX64NativeProfileRuleRevision,
                                out var profileRow)
                            || tables.FixedArrayLayouts.FindRow(
                                location.ModuleRow, location.TypeSpecRef,
                                (uint)profileRow) == 0)
                        {
                            throw InvalidBinding();
                        }

                        PushLocation(pending, new TypeSpecLocation(
                            location.ArtifactIndex, location.ModuleRow,
                            Read(row, 2), IsInlineStorage: true,
                            Depth: checked(location.Depth + 1)));
                        break;

                    default:
                        throw InvalidBinding();
                }
            }
        }

        private void CollectLocalType(int artifactIndex, int typeDefRow,
            Stack<TypeSpecLocation> pending, ulong depth)
        {
            ConsumeWork();
            _a3ResourceLedger?.CheckDepth(depth);
            var origin = new TypeOrigin(artifactIndex,
                GetTables(artifactIndex).TypeDefs.GetOwnerModule(typeDefRow),
                typeDefRow);
            ConsumeWork(2);
            if (!_visitedLocalTypes.Add(origin))
            {
                return;
            }

            var tables = GetTables(artifactIndex);
            if (_a3ResourceLedger is null)
            {
                _ = TrRmFixedArrayLayoutValidator.CalculateTypeDefLayout(
                    tables, typeDefRow);
            }
            else
            {
                using var depthContext
                    = _a3ResourceLedger.EnterDepthContext(depth);
                _ = TrRmFixedArrayLayoutValidator.CalculateTypeDefLayout(
                    tables, typeDefRow);
            }
            var layout = GetWindowsLayout(tables, typeDefRow);
            var (fieldFirst, fieldCount)
                = tables.Layouts.GetLayoutFieldRange(layout);
            for (var ordinal = fieldCount - 1; ordinal >= 0; ordinal--)
            {
                ConsumeWork();
                var field = tables.LayoutFields.GetFieldDef(
                    fieldFirst + ordinal);
                PushLocation(pending, new TypeSpecLocation(artifactIndex,
                    origin.ModuleRow,
                    tables.Fields.GetTypeSpecRef(field),
                    IsInlineStorage: true,
                    Depth: checked(depth + 1)));
            }
        }

        private void ValidateTypePair(TypeOriginPair pair,
            Queue<TypeOriginPair> pendingTypes)
        {
            ConsumeWork();
            _a3ResourceLedger?.CheckDepth(pair.Depth);
            ConsumeWork(2);
            if (!_visitedTypePairs.Add((pair.Expected, pair.Actual,
                    pair.IsInlineStorage)))
            {
                return;
            }

            var expected = GetTables(pair.Expected.ArtifactIndex);
            var actual = GetTables(pair.Actual.ArtifactIndex);
            var expectedType = pair.Expected.TypeDefRow;
            var actualType = pair.Actual.TypeDefRow;
            var expectedKind = expected.TypeDefs.GetKind(expectedType);
            var actualKind = actual.TypeDefs.GetKind(actualType);
            var (expectedFormalFirst, expectedFormalCount)
                = expected.TypeDefs.GetGenericRange(expectedType);
            var (actualFormalFirst, actualFormalCount)
                = actual.TypeDefs.GetGenericRange(actualType);
            var isLayoutQueriesPair
                = expected.Schema == TrRmArtifactSchema.Standard
                && actual.Schema == TrRmArtifactSchema.Standard;
            ConsumeWork(4);
            if (expectedKind == 4 || actualKind == 4)
            {
                if (expectedKind != 4 || actualKind != 4
                    || expected.TypeDefs.GetOwnerModule(expectedType)
                        != pair.Expected.ModuleRow
                    || actual.TypeDefs.GetOwnerModule(actualType)
                        != pair.Actual.ModuleRow
                    || !expected.Schema.UsesC1EnumClosure()
                    || !actual.Schema.UsesC1EnumClosure())
                {
                    throw InvalidBinding();
                }

                TrRmEnumProviderBindingValidator.ValidateC1EnumSnapshot(
                    expected, expectedType, actual, actualType);
                return;
            }

            if (expected.TypeDefs.GetOwnerModule(expectedType)
                    != pair.Expected.ModuleRow
                || actual.TypeDefs.GetOwnerModule(actualType)
                    != pair.Actual.ModuleRow
                || expectedKind is not (1 or 2)
                || expectedKind != actualKind
                || expected.TypeDefs.GetFlags(expectedType)
                    != actual.TypeDefs.GetFlags(actualType)
                || !TypeNamesMatch(expected, expectedType,
                    actual, actualType)
                || expectedFormalCount != actualFormalCount
                || (pair.IsInlineStorage
                    && (expectedKind != 1
                        || (!isLayoutQueriesPair
                            && (!expected.TypeDefs.HasDeclaredCopy(expectedType)
                                || !actual.TypeDefs.HasDeclaredCopy(actualType))))))
            {
                throw InvalidBinding();
            }

            if (expectedFormalCount != 0
                && (!IsGenericTypeProviderSchemaIdentity(
                        _schemaIdentities[pair.Expected.ArtifactIndex])
                    || !IsGenericTypeProviderSchemaIdentity(
                        _schemaIdentities[pair.Actual.ArtifactIndex])))
            {
                throw InvalidBinding();
            }

            if (pair.IsInlineStorage)
            {
                if (isLayoutQueriesPair)
                {
                    if (expectedFormalCount != 0
                        || !expected.Layouts.HasBlittableLayout(
                            expectedType, 1, 1)
                        || !actual.Layouts.HasBlittableLayout(actualType,
                            1, 1))
                    {
                        throw InvalidBinding();
                    }
                }
                else if (expectedFormalCount == 0)
                {
                    using var depthContext = _a3ResourceLedger is null
                        ? default
                        : _a3ResourceLedger.EnterDepthContext(pair.Depth);
                    var expectedLayout
                        = TrRmFixedArrayLayoutValidator
                            .CalculateTypeDefLayout(expected, expectedType);
                    var actualLayout
                        = TrRmFixedArrayLayoutValidator
                            .CalculateTypeDefLayout(actual, actualType);
                    if (expectedLayout != actualLayout)
                    {
                        throw InvalidBinding();
                    }
                }
                else if (!expected.Layouts.HasBlittableLayout(
                            expectedType, 1, 1)
                    || !actual.Layouts.HasBlittableLayout(actualType, 1, 1))
                {
                    throw InvalidBinding();
                }
            }

            if (expectedFormalCount != 0
                && !GenericFormalsMatch(pair.Expected, pair.Actual,
                    expected, actual, expectedFormalFirst,
                    actualFormalFirst, expectedFormalCount, pendingTypes,
                    checked(pair.Depth + 1)))
            {
                throw InvalidBinding();
            }

            var (expectedFirst, expectedEnd)
                = expected.TypeDefs.GetFieldRange(expectedType);
            var (actualFirst, actualEnd)
                = actual.TypeDefs.GetFieldRange(actualType);
            if (expectedEnd - expectedFirst != actualEnd - actualFirst)
            {
                throw InvalidBinding();
            }

            for (var ordinal = 0; ordinal < expectedEnd - expectedFirst;
                ordinal++)
            {
                ConsumeWork();
                var expectedField = expectedFirst + ordinal;
                var actualField = actualFirst + ordinal;
                var compareAsInlineStorage
                    = !IsC1M1SchemaIdentity(
                        _schemaIdentities[pair.Expected.ArtifactIndex])
                        && (expected.Fields.GetFlags(expectedField) & 8) == 0;
                if (expected.Fields.GetFlags(expectedField)
                        != actual.Fields.GetFlags(actualField)
                    || !FieldNamesMatch(expected, expectedField,
                        actual, actualField)
                    || !TypeSpecsMatch(expected, pair.Expected.ArtifactIndex,
                        pair.Expected.ModuleRow,
                        expected.Fields.GetTypeSpecRef(expectedField),
                        actual, pair.Actual.ArtifactIndex, pair.Actual.ModuleRow,
                        actual.Fields.GetTypeSpecRef(actualField),
                        isInlineStorage: compareAsInlineStorage,
                        pendingTypes,
                        leftFormalOwner: expectedFormalCount == 0
                            ? null : pair.Expected,
                        rightFormalOwner: actualFormalCount == 0
                            ? null : pair.Actual,
                        depth: checked(pair.Depth + 1)))
                {
                    throw InvalidBinding();
                }
            }

            if (!LayoutsMatch(pair.Expected, pair.Actual, expected, actual,
                    pendingTypes,
                    expectedFormalCount == 0 ? null : pair.Expected,
                    actualFormalCount == 0 ? null : pair.Actual,
                    checked(pair.Depth + 1),
                    compareLayoutFieldTypesAsInlineStorage:
                        !IsC1M1SchemaIdentity(
                            _schemaIdentities[pair.Expected.ArtifactIndex])))
            {
                throw InvalidBinding();
            }
        }

        private readonly bool TypeNamesMatch(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables expected,
            int expectedType,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
            int actualType)
        {
            var expectedNamespace = expected.TypeDefs.GetNamespace(expectedType);
            var actualNamespace = actual.TypeDefs.GetNamespace(actualType);
            ConsumeWork(GetStringComparisonWork(
                expectedNamespace, actualNamespace));
            if (!StringComparer.Ordinal.Equals(
                    expectedNamespace, actualNamespace))
            {
                return false;
            }

            var expectedName = expected.TypeDefs.GetName(expectedType);
            var actualName = actual.TypeDefs.GetName(actualType);
            ConsumeWork(GetStringComparisonWork(expectedName, actualName));
            return StringComparer.Ordinal.Equals(expectedName, actualName);
        }

        private readonly bool FieldNamesMatch(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables expected,
            int expectedField,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
            int actualField)
        {
            var expectedName = expected.Fields.GetName(expectedField);
            var actualName = actual.Fields.GetName(actualField);
            ConsumeWork(GetStringComparisonWork(expectedName, actualName));
            return StringComparer.Ordinal.Equals(expectedName, actualName);
        }

        private bool GenericFormalsMatch(TypeOrigin expectedOrigin,
            TypeOrigin actualOrigin,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables expected,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
            int expectedFormalFirst, int actualFormalFirst, int formalCount,
            Queue<TypeOriginPair> pendingTypes, ulong depth)
        {
            for (var ordinal = 0; ordinal < formalCount; ordinal++)
            {
                ConsumeWork();
                var expectedFormal = expectedFormalFirst + ordinal;
                var actualFormal = actualFormalFirst + ordinal;
                if (expected.GenericParams.GetOwnerDefId(expectedFormal)
                        != (0x1300_0000u | (uint)expectedOrigin.TypeDefRow)
                    || actual.GenericParams.GetOwnerDefId(actualFormal)
                        != (0x1300_0000u | (uint)actualOrigin.TypeDefRow)
                    || expected.GenericParams.GetOrdinal(expectedFormal)
                        != actual.GenericParams.GetOrdinal(actualFormal)
                    || expected.GenericParams.GetFormalKind(expectedFormal)
                        != actual.GenericParams.GetFormalKind(actualFormal)
                    || expected.GenericParams.GetOwnershipDomain(
                        expectedFormal)
                        != actual.GenericParams.GetOwnershipDomain(actualFormal)
                    || expected.GenericParams.GetNullabilityDomain(
                        expectedFormal)
                        != actual.GenericParams.GetNullabilityDomain(
                            actualFormal))
                {
                    return false;
                }

                var formalKind
                    = expected.GenericParams.GetFormalKind(expectedFormal);
                var expectedConstantTypeSpec
                    = expected.GenericParams.GetConstantTypeSpecRef(
                        expectedFormal);
                var actualConstantTypeSpec
                    = actual.GenericParams.GetConstantTypeSpecRef(
                        actualFormal);
                if ((expectedConstantTypeSpec == 0)
                        != (actualConstantTypeSpec == 0)
                    || (expectedConstantTypeSpec != 0
                        && !TypeSpecsMatch(expected,
                            expectedOrigin.ArtifactIndex,
                            expectedOrigin.ModuleRow,
                            expectedConstantTypeSpec, actual,
                            actualOrigin.ArtifactIndex,
                            actualOrigin.ModuleRow, actualConstantTypeSpec,
                            isInlineStorage: false, pendingTypes,
                            expectedOrigin, actualOrigin,
                            checked(depth + 1))))
                {
                    return false;
                }

                var expectedDefault
                    = expected.GenericParams.GetDefaultArgumentRef(
                        expectedFormal);
                var actualDefault
                    = actual.GenericParams.GetDefaultArgumentRef(actualFormal);
                var expectedHasDefault = expectedDefault != 0;
                var actualHasDefault = actualDefault != 0;
                if (expectedHasDefault != actualHasDefault
                    || (expectedDefault != 0
                        && (formalKind == 1
                            ? !TypeSpecsMatch(expected,
                                expectedOrigin.ArtifactIndex,
                                expectedOrigin.ModuleRow, expectedDefault,
                                actual, actualOrigin.ArtifactIndex,
                                actualOrigin.ModuleRow, actualDefault,
                                isInlineStorage: false, pendingTypes,
                                expectedOrigin, actualOrigin,
                                checked(depth + 1))
                            : !GenericConstantDefaultsMatch(
                                expectedOrigin, actualOrigin, expected,
                                actual, expectedFormal, actualFormal,
                                expectedDefault, actualDefault))))
                {
                    return false;
                }

                var (expectedConstraintFirst, expectedConstraintCount)
                    = expected.GenericParams.GetConstraintRange(expectedFormal);
                var (actualConstraintFirst, actualConstraintCount)
                    = actual.GenericParams.GetConstraintRange(actualFormal);
                if (expectedConstraintCount != actualConstraintCount)
                {
                    return false;
                }

                for (var constraintOrdinal = 0;
                    constraintOrdinal < expectedConstraintCount;
                    constraintOrdinal++)
                {
                    ConsumeWork();
                    var expectedConstraint
                        = expectedConstraintFirst + constraintOrdinal;
                    var actualConstraint
                        = actualConstraintFirst + constraintOrdinal;
                    var constraintKind
                        = expected.Constraints.GetKind(expectedConstraint);
                    var expectedPayload
                        = expected.Constraints.GetPayload(expectedConstraint);
                    var actualPayload
                        = actual.Constraints.GetPayload(actualConstraint);
                    if (constraintKind
                            != actual.Constraints.GetKind(actualConstraint)
                        || (constraintKind == 1
                            ? !TypeSpecsMatch(expected,
                                expectedOrigin.ArtifactIndex,
                                expectedOrigin.ModuleRow, expectedPayload,
                                actual, actualOrigin.ArtifactIndex,
                                actualOrigin.ModuleRow, actualPayload,
                                isInlineStorage: false, pendingTypes,
                                expectedOrigin, actualOrigin,
                                checked(depth + 1))
                            : constraintKind == 2
                                ? expectedPayload != actualPayload
                                : true))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool GenericConstantDefaultsMatch(
            TypeOrigin expectedOrigin, TypeOrigin actualOrigin,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables expected,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
            int expectedFormal, int actualFormal,
            uint expectedDefault, uint actualDefault)
        {
            var expectedKind = expected.ConstantSources.GetKind(
                checked((int)expectedDefault));
            var actualKind = actual.ConstantSources.GetKind(
                checked((int)actualDefault));
            if (expectedKind != actualKind)
            {
                return false;
            }

            var expectedDomain = expected.GenericParams
                .GetConstantTypeSpecRef(expectedFormal);
            var actualDomain = actual.GenericParams
                .GetConstantTypeSpecRef(actualFormal);
            if (expectedKind == 1)
            {
                var expectedPayload
                    = TrRmConstantSourceValueValidator.GetFrozenScalarPayload(
                        checked((int)expectedDefault), expectedDomain,
                        expectedOrigin.ModuleRow,
                        expected.ConstantSources,
                        expected.FrozenValueNodes, expected.Blobs,
                        expected.TypeSpecs, expected.Features,
                        expected.Modules, expected.TypeDefs,
                        expected.EnumDefs, expected.Imports,
                        _a3ResourceLedger, expected.Fields,
                        expected.TransparentConstantDomains);
                var actualPayload
                    = TrRmConstantSourceValueValidator.GetFrozenScalarPayload(
                        checked((int)actualDefault), actualDomain,
                        actualOrigin.ModuleRow, actual.ConstantSources,
                        actual.FrozenValueNodes, actual.Blobs,
                        actual.TypeSpecs, actual.Features, actual.Modules,
                        actual.TypeDefs, actual.EnumDefs, actual.Imports,
                        _a3ResourceLedger, actual.Fields,
                        actual.TransparentConstantDomains);
                return expectedPayload.SequenceEqual(actualPayload);
            }

            if (expectedKind == 4)
            {
                var expectedFormalRow = checked((int)
                    expected.ConstantSources.GetPayloadReference(
                        checked((int)expectedDefault)));
                var actualFormalRow = checked((int)
                    actual.ConstantSources.GetPayloadReference(
                        checked((int)actualDefault)));
                var expectedOwner = expected.GenericParams
                    .GetOwnerDefId(expectedFormalRow);
                var actualOwner = actual.GenericParams
                    .GetOwnerDefId(actualFormalRow);
                return expectedOwner
                        == (0x1300_0000u
                            | checked((uint)expectedOrigin.TypeDefRow))
                    && actualOwner
                        == (0x1300_0000u
                            | checked((uint)actualOrigin.TypeDefRow))
                    && expected.GenericParams.GetFormalKind(
                        expectedFormalRow) == 2
                    && actual.GenericParams.GetFormalKind(
                        actualFormalRow) == 2
                    && expected.GenericParams.GetOrdinal(expectedFormalRow)
                        == actual.GenericParams.GetOrdinal(actualFormalRow)
                    && expected.GenericParams.GetOrdinal(expectedFormalRow)
                        < expected.GenericParams.GetOrdinal(expectedFormal)
                    && actual.GenericParams.GetOrdinal(actualFormalRow)
                        < actual.GenericParams.GetOrdinal(actualFormal);
            }

            return false;
        }

        private bool LayoutsMatch(TypeOrigin expectedOrigin,
            TypeOrigin actualOrigin,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables expected,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
            Queue<TypeOriginPair> pendingTypes,
            TypeOrigin? leftFormalOwner = null,
            TypeOrigin? rightFormalOwner = null, ulong depth = 1,
            bool compareLayoutFieldTypesAsInlineStorage = true)
        {
            var (expectedFirst, expectedCount)
                = expected.TypeDefs.GetLayoutRange(expectedOrigin.TypeDefRow);
            var (actualFirst, actualCount)
                = actual.TypeDefs.GetLayoutRange(actualOrigin.TypeDefRow);
            if (expectedCount != actualCount)
            {
                return false;
            }

            ConsumeWork(checked((ulong)actualCount + 1UL));
            var actualRows = new Dictionary<LayoutIdentity, int>(actualCount);
            for (var ordinal = 0; ordinal < actualCount; ordinal++)
            {
                ConsumeWork();
                var row = actualFirst + ordinal;
                ConsumeWork(2);
                if (!actualRows.TryAdd(GetLayoutIdentity(actual, row), row))
                {
                    return false;
                }
            }

            for (var ordinal = 0; ordinal < expectedCount; ordinal++)
            {
                ConsumeWork();
                var expectedLayout = expectedFirst + ordinal;
                var identity = GetLayoutIdentity(expected, expectedLayout);
                ConsumeWork();
                if (!actualRows.Remove(identity, out var actualLayout))
                {
                    return false;
                }

                var (expectedFields, expectedFieldCount)
                    = expected.Layouts.GetLayoutFieldRange(expectedLayout);
                var (actualFields, actualFieldCount)
                    = actual.Layouts.GetLayoutFieldRange(actualLayout);
                if (expectedFieldCount != actualFieldCount)
                {
                    return false;
                }

                var (expectedTypeFields, expectedTypeFieldEnd)
                    = expected.TypeDefs.GetFieldRange(
                        expectedOrigin.TypeDefRow);
                var (actualTypeFields, actualTypeFieldEnd)
                    = actual.TypeDefs.GetFieldRange(actualOrigin.TypeDefRow);
                for (var fieldOrdinal = 0;
                    fieldOrdinal < expectedFieldCount; fieldOrdinal++)
                {
                    ConsumeWork();
                    var expectedRow = expectedFields + fieldOrdinal;
                    var actualRow = actualFields + fieldOrdinal;
                    var expectedField = expected.LayoutFields.GetFieldDef(
                        expectedRow);
                    var actualField = actual.LayoutFields.GetFieldDef(
                        actualRow);
                    if (expected.LayoutFields.GetOrdinal(expectedRow)
                            != actual.LayoutFields.GetOrdinal(actualRow)
                        || expectedField < expectedTypeFields
                        || expectedField >= expectedTypeFieldEnd
                        || actualField < actualTypeFields
                        || actualField >= actualTypeFieldEnd
                        || expectedField - expectedTypeFields
                            != actualField - actualTypeFields
                        || !TypeSpecsMatch(expected,
                            expectedOrigin.ArtifactIndex,
                            expectedOrigin.ModuleRow,
                            expected.Fields.GetTypeSpecRef(expectedField),
                            actual, actualOrigin.ArtifactIndex,
                            actualOrigin.ModuleRow,
                            actual.Fields.GetTypeSpecRef(actualField),
                            isInlineStorage:
                                compareLayoutFieldTypesAsInlineStorage,
                            pendingTypes,
                            leftFormalOwner, rightFormalOwner,
                            depth: checked(depth + 1)))
                    {
                        return false;
                    }
                }
            }

            return actualRows.Count == 0;
        }

        private bool TypeSpecsMatch(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables leftTables,
            int leftArtifactIndex, int leftModuleRow, uint leftTypeSpecRef,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables rightTables,
            int rightArtifactIndex, int rightModuleRow, uint rightTypeSpecRef,
            bool isInlineStorage,
            Queue<TypeOriginPair> pendingTypes,
            TypeOrigin? leftFormalOwner = null,
            TypeOrigin? rightFormalOwner = null, ulong depth = 1)
        {
            ConsumeWork();
            var pending = new Stack<TypeSpecComparison>();
            PushComparison(pending, new TypeSpecComparison(leftArtifactIndex,
                leftModuleRow, leftTypeSpecRef, rightArtifactIndex,
                rightModuleRow, rightTypeSpecRef, isInlineStorage,
                leftFormalOwner, rightFormalOwner, Depth: depth));
            while (pending.TryPop(out var comparison))
            {
                ConsumeWork();
                _a3ResourceLedger?.CheckDepth(comparison.Depth);
                var left = leftTables.TypeSpecs.GetTypeSpecRef(
                    comparison.LeftTypeSpecRef,
                    allowInitQualifier: comparison.AllowInitQualifier);
                var right = rightTables.TypeSpecs.GetTypeSpecRef(
                    comparison.RightTypeSpecRef,
                    allowInitQualifier: comparison.AllowInitQualifier);
                var leftIsNominalType = left.Form is 0x02 or 0x03;
                var rightIsNominalType = right.Form is 0x02 or 0x03;
                if (left.Qualifiers != right.Qualifiers
                    || (left.Form != right.Form
                        && !(leftIsNominalType && rightIsNominalType)))
                {
                    return false;
                }

                if (!left.IsRow || !right.IsRow)
                {
                    if (left.Form == 0x01
                        && left.TypeCode == right.TypeCode
                        && left.ArgumentCode == right.ArgumentCode)
                    {
                        continue;
                    }

                    if (left.Form == 0x0F)
                    {
                        continue;
                    }

                    if (left.IsRow || right.IsRow)
                    {
                        return false;
                    }

                    return false;
                }

                var leftRow = leftTables.TypeSpecs.GetRow(left.Row);
                var rightRow = rightTables.TypeSpecs.GetRow(right.Row);
                switch (left.Form)
                {
                    case 0x01:
                        if (left.TypeCode != right.TypeCode
                            || left.ArgumentCode != right.ArgumentCode)
                        {
                            return false;
                        }
                        break;

                    case 0x02:
                    case 0x03:
                    {
                        var leftOrigin = ResolveNominalType(
                            leftTables,
                            comparison.LeftArtifactIndex,
                            comparison.LeftModuleRow, left, leftRow);
                        var rightOrigin = ResolveNominalType(
                            rightTables,
                            comparison.RightArtifactIndex,
                            comparison.RightModuleRow, right, rightRow);
                        if (leftOrigin.Actual != rightOrigin.Actual)
                        {
                            return false;
                        }

                        AddTypePair(leftOrigin.Expected, rightOrigin.Actual,
                            comparison.IsInlineStorage, pendingTypes,
                            comparison.Depth);
                        AddTypePair(rightOrigin.Expected, rightOrigin.Actual,
                            comparison.IsInlineStorage, pendingTypes,
                            comparison.Depth);
                        break;
                    }

                    case 0x04:
                    {
                        if (comparison.LeftFormalOwner is not TypeOrigin
                                leftFormalTypeOwner
                            || comparison.RightFormalOwner is not TypeOrigin
                                rightFormalTypeOwner)
                        {
                            return false;
                        }

                        var leftFormal = checked((int)Read(leftRow, 2));
                        var rightFormal = checked((int)Read(rightRow, 2));
                        var leftOwner = leftTables.GenericParams
                            .GetOwnerDefId(leftFormal);
                        var rightOwner = rightTables.GenericParams
                            .GetOwnerDefId(rightFormal);
                        if (leftOwner != (0x1300_0000u
                                | (uint)leftFormalTypeOwner.TypeDefRow)
                            || rightOwner != (0x1300_0000u
                                | (uint)rightFormalTypeOwner.TypeDefRow)
                            || leftTables.TypeDefs.GetOwnerModule(
                                leftFormalTypeOwner.TypeDefRow)
                                != leftFormalTypeOwner.ModuleRow
                            || rightTables.TypeDefs.GetOwnerModule(
                                rightFormalTypeOwner.TypeDefRow)
                                != rightFormalTypeOwner.ModuleRow
                            || leftTables.GenericParams.GetOrdinal(leftFormal)
                                != rightTables.GenericParams.GetOrdinal(
                                    rightFormal)
                            || leftTables.GenericParams.GetFormalKind(
                                leftFormal)
                                != rightTables.GenericParams.GetFormalKind(
                                    rightFormal)
                            || leftTables.GenericParams.GetOwnershipDomain(
                                leftFormal)
                                != rightTables.GenericParams.GetOwnershipDomain(
                                    rightFormal)
                            || leftTables.GenericParams.GetNullabilityDomain(
                                leftFormal)
                                != rightTables.GenericParams.GetNullabilityDomain(
                                    rightFormal))
                        {
                            return false;
                        }
                        break;
                    }

                    case >= 0x06 and <= 0x0B:
                    case 0x0E:
                        PushComparison(pending, new TypeSpecComparison(
                            comparison.LeftArtifactIndex,
                            comparison.LeftModuleRow, Read(leftRow, 2),
                            comparison.RightArtifactIndex,
                            comparison.RightModuleRow, Read(rightRow, 2),
                            IsInlineStorage: false,
                            comparison.LeftFormalOwner,
                            comparison.RightFormalOwner,
                            Depth: checked(comparison.Depth + 1)));
                        break;

                    case 0x0C:
                        ConsumeWork(5);
                        if (leftRow[2] != rightRow[2])
                        {
                            return false;
                        }

                        PushComparison(pending, new TypeSpecComparison(
                            comparison.LeftArtifactIndex,
                            comparison.LeftModuleRow, Read(leftRow, 3),
                            comparison.RightArtifactIndex,
                            comparison.RightModuleRow, Read(rightRow, 3),
                            IsInlineStorage: false,
                            comparison.LeftFormalOwner,
                            comparison.RightFormalOwner,
                            AllowInitQualifier: left.TypeCode == 0x8A,
                            Depth: checked(comparison.Depth + 1)));
                        break;

                    case 0x0D:
                        if (!FunctionPointerAnchorsMatch(comparison,
                                leftTables, rightTables, pendingTypes))
                        {
                            return false;
                        }
                        break;
                    case 0x10:
                        if (leftRow.Length != rightRow.Length)
                        {
                            return false;
                        }

                        ConsumeWork(checked((ulong)leftRow.Length * 3));
                        if (!leftRow.SequenceEqual(rightRow))
                        {
                            return false;
                        }
                        break;

                    case 0x05:
                    {
                        var leftDefinitionRef = Read(leftRow, 2);
                        var rightDefinitionRef = Read(rightRow, 2);
                        var leftDefinition
                            = leftTables.TypeSpecs.GetTypeSpecRef(
                                leftDefinitionRef);
                        var rightDefinition
                            = rightTables.TypeSpecs.GetTypeSpecRef(
                                rightDefinitionRef);
                        var leftDefinitionRow
                            = leftTables.TypeSpecs.GetRow(leftDefinition.Row);
                        var rightDefinitionRow
                            = rightTables.TypeSpecs.GetRow(rightDefinition.Row);
                        var leftOrigin = ResolveNominalType(leftTables,
                            comparison.LeftArtifactIndex,
                            comparison.LeftModuleRow, leftDefinition,
                            leftDefinitionRow);
                        var rightOrigin = ResolveNominalType(rightTables,
                            comparison.RightArtifactIndex,
                            comparison.RightModuleRow, rightDefinition,
                            rightDefinitionRow);
                        var leftActualTables
                            = GetTables(leftOrigin.Actual.ArtifactIndex);
                        var rightActualTables
                            = GetTables(rightOrigin.Actual.ArtifactIndex);
                        var leftFormalCount
                            = leftActualTables.TypeDefs.GetGenericRange(
                                leftOrigin.Actual.TypeDefRow).Count;
                        var rightFormalCount
                            = rightActualTables.TypeDefs.GetGenericRange(
                                rightOrigin.Actual.TypeDefRow).Count;
                        var leftArgumentCount
                            = leftTables.TypeSpecs.GetConstructedArgumentCount(
                                left.Row);
                        var rightArgumentCount
                            = rightTables.TypeSpecs.GetConstructedArgumentCount(
                                right.Row);
                        if (leftOrigin.Actual != rightOrigin.Actual
                            || leftFormalCount != rightFormalCount
                            || leftArgumentCount != leftFormalCount
                            || rightArgumentCount != rightFormalCount)
                        {
                            return false;
                        }

                        AddTypePair(leftOrigin.Expected,
                            rightOrigin.Actual, comparison.IsInlineStorage,
                            pendingTypes, comparison.Depth);
                        AddTypePair(rightOrigin.Expected,
                            rightOrigin.Actual, comparison.IsInlineStorage,
                            pendingTypes, comparison.Depth);
                        for (var ordinal = leftArgumentCount - 1;
                            ordinal >= 0; ordinal--)
                        {
                            ConsumeWork();
                            PushComparison(pending, new TypeSpecComparison(
                                comparison.LeftArtifactIndex,
                                comparison.LeftModuleRow,
                                leftTables.TypeSpecs.GetConstructedArgument(
                                    left.Row, ordinal),
                                comparison.RightArtifactIndex,
                                comparison.RightModuleRow,
                                rightTables.TypeSpecs.GetConstructedArgument(
                                    right.Row, ordinal),
                                IsInlineStorage: false,
                                comparison.LeftFormalOwner,
                                comparison.RightFormalOwner,
                                Depth: checked(comparison.Depth + 1)));
                        }
                        break;
                    }

                    case 0x0F:
                        break;

                    case 0x11:
                        ConsumeWork(19);
                        if (BinaryPrimitives.ReadUInt64LittleEndian(
                                leftRow[6..])
                            != BinaryPrimitives.ReadUInt64LittleEndian(
                                rightRow[6..]))
                        {
                            return false;
                        }

                        PushComparison(pending, new TypeSpecComparison(
                            comparison.LeftArtifactIndex,
                            comparison.LeftModuleRow, Read(leftRow, 2),
                            comparison.RightArtifactIndex,
                            comparison.RightModuleRow, Read(rightRow, 2),
                            comparison.IsInlineStorage,
                            comparison.LeftFormalOwner,
                            comparison.RightFormalOwner,
                            Depth: checked(comparison.Depth + 1)));
                        break;

                    default:
                        return false;
                }
            }

            return true;
        }

        private bool FunctionPointerAnchorsMatch(
            TypeSpecComparison comparison,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables left,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables right,
            Queue<TypeOriginPair> pendingTypes)
        {
            var leftTypeSpecRow = left.TypeSpecs.GetTypeSpecRef(
                comparison.LeftTypeSpecRef).Row;
            var rightTypeSpecRow = right.TypeSpecs.GetTypeSpecRef(
                comparison.RightTypeSpecRef).Row;
            var leftAnchors = left.NativeFunctionPointerAnchors;
            var rightAnchors = right.NativeFunctionPointerAnchors;
            if (!leftAnchors.HasAnchorForTypeSpec(leftTypeSpecRow)
                || !rightAnchors.HasAnchorForTypeSpec(rightTypeSpecRow))
            {
                return false;
            }

            var leftMethod = leftAnchors.GetAnchorMethodDefRow(
                leftTypeSpecRow);
            var rightMethod = rightAnchors.GetAnchorMethodDefRow(
                rightTypeSpecRow);
            var anchorPair = (comparison.LeftArtifactIndex, leftMethod,
                comparison.RightArtifactIndex, rightMethod);
            ConsumeWork(2);
            if (leftAnchors.GetCallingConvention(leftTypeSpecRow)
                    != rightAnchors.GetCallingConvention(rightTypeSpecRow)
                || !_activeAnchorPairs.Add(anchorPair))
            {
                return false;
            }

            try
            {
                var leftSignature = left.Methods.GetSignature(leftMethod);
                var rightSignature = right.Methods.GetSignature(rightMethod);
                if (left.Signatures.GetReceiverTypeSpec(leftSignature) != 0
                    || right.Signatures.GetReceiverTypeSpec(rightSignature) != 0
                    || left.Signatures.GetGenericCount(leftSignature) != 0
                    || right.Signatures.GetGenericCount(rightSignature) != 0
                    || !TypeSpecsMatch(left, comparison.LeftArtifactIndex,
                        left.Methods.GetOwnerModule(leftMethod),
                        left.Signatures.GetResultTypeSpec(leftSignature),
                        right, comparison.RightArtifactIndex,
                        right.Methods.GetOwnerModule(rightMethod),
                        right.Signatures.GetResultTypeSpec(rightSignature),
                        isInlineStorage: false, pendingTypes,
                        depth: checked(comparison.Depth + 1)))
                {
                    return false;
                }

                var (leftFirst, leftCount)
                    = left.Signatures.GetParamRange(leftSignature);
                var (rightFirst, rightCount)
                    = right.Signatures.GetParamRange(rightSignature);
                if (leftCount != rightCount)
                {
                    return false;
                }

                for (var ordinal = 0; ordinal < leftCount; ordinal++)
                {
                    ConsumeWork();
                    var leftParameter = leftFirst + ordinal;
                    var rightParameter = rightFirst + ordinal;
                    if (left.Parameters.GetMode(leftParameter)
                            != right.Parameters.GetMode(rightParameter)
                        || left.Parameters.GetFlags(leftParameter)
                            != right.Parameters.GetFlags(rightParameter)
                        || !TypeSpecsMatch(left,
                            comparison.LeftArtifactIndex,
                            left.Methods.GetOwnerModule(leftMethod),
                            left.Parameters.GetTypeSpec(leftParameter),
                            right, comparison.RightArtifactIndex,
                            right.Methods.GetOwnerModule(rightMethod),
                            right.Parameters.GetTypeSpec(rightParameter),
                            isInlineStorage: false, pendingTypes,
                            depth: checked(comparison.Depth + 1)))
                    {
                        return false;
                    }
                }

                return leftAnchors.AreEquivalentAnchorContracts(leftMethod,
                    rightAnchors, rightMethod);
            }
            finally
            {
                ConsumeWork();
                _ = _activeAnchorPairs.Remove(anchorPair);
            }
        }

        private TypeOriginPair ResolveNominalType(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int artifactIndex, int moduleRow, TrRmTypeSpecRefInfo reference,
            ReadOnlySpan<byte> row)
        {
            if (reference.Form == 0x02)
            {
                var typeDefRow = checked((int)Read(row, 2));
                if (tables.Imports.IsExpectedDefinition(
                        0x1300_0000u | checked((uint)typeDefRow)))
                {
                    throw InvalidBinding();
                }

                return new TypeOriginPair(
                    new TypeOrigin(artifactIndex,
                        tables.TypeDefs.GetOwnerModule(typeDefRow),
                        typeDefRow),
                    new TypeOrigin(artifactIndex,
                        tables.TypeDefs.GetOwnerModule(typeDefRow),
                        typeDefRow), false);
            }

            if (reference.Form == 0x03)
            {
                var resolution = ResolveImport(artifactIndex, moduleRow,
                    tables,
                    checked((int)Read(row, 2)));
                return new TypeOriginPair(resolution.Expected,
                    resolution.Actual, false);
            }

            throw InvalidBinding();
        }

        private ImportResolution ResolveImport(int artifactIndex,
            int moduleRow,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
            int importRow)
        {
            ConsumeWork();
            var key = (artifactIndex, importRow);
            ConsumeWork(2);
            if (_imports.TryGetValue(key, out var existing))
            {
                if (existing.Expected.ModuleRow != moduleRow)
                {
                    throw InvalidBinding();
                }

                return existing;
            }

            var expectedDefinition
                = consumer.Imports.GetExpectedDefinitionForImportRow(importRow);
            if ((expectedDefinition >> 24) != 0x13)
            {
                throw InvalidBinding();
            }

            var expectedTypeDefRow = checked((int)(
                expectedDefinition & 0x00FF_FFFF));
            var expectedGenericCount
                = consumer.TypeDefs.GetGenericRange(expectedTypeDefRow).Count;
            var expectedRequiresGenericSchema = expectedGenericCount != 0
                || consumer.TypeDefs.HasDeclaredUnique(expectedTypeDefRow);
            var expectedModule
                = consumer.TypeDefs.GetOwnerModule(expectedTypeDefRow);
            var moduleRefRow
                = consumer.Imports.GetModuleRefForImportRow(importRow);
            var binding = consumer.Imports.GetBindingForImportRow(importRow);
            if (expectedModule != moduleRow
                || (expectedRequiresGenericSchema
                    && !IsGenericTypeProviderSchemaIdentity(
                        _schemaIdentities[artifactIndex]))
                || consumer.ModuleRefs.GetOwnerModule(moduleRefRow) != moduleRow)
            {
                throw InvalidBinding();
            }

            var producerIdentity
                = consumer.ModuleRefs.GetProducerIdentity(moduleRefRow);
            ConsumeWork(GetStringComparisonWork(producerIdentity,
                binding.ProducerIdentity));
            if (!StringComparer.Ordinal.Equals(producerIdentity,
                    binding.ProducerIdentity)
                || consumer.ModuleRefs.GetRequirements(moduleRefRow)
                    != (binding.TargetRequirement, binding.RuntimePolicy))
            {
                throw InvalidBinding();
            }

            var selected = FindModule(binding.ProducerIdentity);
            if ((selected.ArtifactIndex == artifactIndex
                    && selected.ModuleRow == moduleRow)
                || !IsTypeProviderSchemaIdentity(
                    _schemaIdentities[selected.ArtifactIndex]))
            {
                throw InvalidBinding();
            }

            var provider = GetTables(selected.ArtifactIndex);
            if (provider.Modules.GetRequirements(selected.ModuleRow)
                    != (binding.TargetRequirement, binding.RuntimePolicy)
                || !provider.Exports.TryGetDefinitionForKey(
                    selected.ModuleRow, binding.ExportKey,
                    out var providerDefinition)
                || (providerDefinition >> 24) != 0x13
                || !provider.Exports.TryGetExportRowForKey(
                    selected.ModuleRow, binding.ExportKey,
                    out var providerExportRow))
            {
                throw InvalidBinding();
            }

            var providerTypeDefRow = checked((int)(
                providerDefinition & 0x00FF_FFFF));
            if (providerTypeDefRow <= 0
                || providerTypeDefRow > provider.TypeDefs.Count
                || provider.TypeDefs.GetOwnerModule(providerTypeDefRow)
                    != selected.ModuleRow
                || ((expectedGenericCount != 0
                    || provider.TypeDefs.GetGenericRange(
                        providerTypeDefRow).Count != 0
                    || consumer.TypeDefs.HasDeclaredUnique(expectedTypeDefRow)
                    || provider.TypeDefs.HasDeclaredUnique(providerTypeDefRow))
                    && (!IsGenericTypeProviderSchemaIdentity(
                            _schemaIdentities[artifactIndex])
                        || !IsGenericTypeProviderSchemaIdentity(
                            _schemaIdentities[selected.ArtifactIndex]))))
            {
                throw InvalidBinding();
            }

            var providerBinding = new TrRmStandardTypeProviderBinding(
                artifactIndex, moduleRow, importRow, moduleRefRow,
                expectedTypeDefRow,
                selected.ArtifactIndex, selected.ModuleRow,
                providerTypeDefRow, providerExportRow, providerDefinition,
                binding.ProducerIdentity,
                binding.ExportKey, binding.TargetRequirement,
                binding.RuntimePolicy);
            var result = new ImportResolution(
                new TypeOrigin(artifactIndex, expectedModule,
                    expectedTypeDefRow),
                new TypeOrigin(selected.ArtifactIndex, selected.ModuleRow,
                    providerTypeDefRow),
                providerBinding);
            ConsumeWork(2);
            _imports.Add(key, result);
            return result;
        }

        private readonly TrRmStandardModuleSelection FindModule(
            string identity)
        {
            TrRmStandardModuleSelection? selected = null;
            foreach (var module in _modules)
            {
                ConsumeWork();
                ConsumeWork(GetStringComparisonWork(module.Identity,
                    identity));
                if (!StringComparer.Ordinal.Equals(module.Identity, identity))
                {
                    continue;
                }

                if (selected is not null)
                {
                    throw InvalidBinding();
                }

                selected = module;
            }

            return selected ?? throw InvalidBinding();
        }

        private readonly void AddTypePair(TypeOrigin expected,
            TypeOrigin actual,
            bool isInlineStorage, Queue<TypeOriginPair> pendingTypes,
            ulong depth = 1)
        {
            var pair = (expected, actual, isInlineStorage);
            ConsumeWork(2);
            if (!_visitedTypePairs.Contains(pair))
            {
                _a3ResourceLedger?.ReserveWorkAtDepth(1, depth);
                pendingTypes.Enqueue(new TypeOriginPair(expected, actual,
                    isInlineStorage, depth));
            }
        }

        private readonly void PushLocation(Stack<TypeSpecLocation> pending,
            TypeSpecLocation location)
        {
            _a3ResourceLedger?.ReserveWorkAtDepth(1, location.Depth);
            pending.Push(location);
        }

        private readonly void PushComparison(Stack<TypeSpecComparison> pending,
            TypeSpecComparison comparison)
        {
            _a3ResourceLedger?.ReserveWorkAtDepth(1, comparison.Depth);
            pending.Push(comparison);
        }

        private readonly LayoutIdentity GetLayoutIdentity(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int layoutRow)
        {
            ConsumeWork(sizeof(uint) + 1);
            var profileRow = tables.Layouts.GetTargetProfileRef(layoutRow);
            ConsumeWork();
            var profile = profileRow == 0
                ? default
                : tables.TargetProfiles.GetProfile(checked((int)profileRow));
            ConsumeWork(2);
            var kind = tables.Layouts.GetLayoutKind(layoutRow);
            ConsumeWork(2);
            var flags = tables.Layouts.GetFlags(layoutRow);
            ConsumeWork(sizeof(uint) + 1);
            var alignment = tables.Layouts.GetAlignment(layoutRow);
            ConsumeWork(sizeof(uint) + 1);
            var pack = tables.Layouts.GetPack(layoutRow);
            return new LayoutIdentity(profile.ProfileId,
                profile.RuleRevision, kind, flags, alignment, pack);
        }

        private readonly int GetWindowsLayout(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int typeDefRow)
        {
            ConsumeWork();
            if (!tables.TargetProfiles.TryGetProfile(
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader
                        .WindowsX64NativeProfileRuleRevision,
                    out var profileRow))
            {
                throw InvalidBinding();
            }

            ConsumeWork();
            var layoutRow = tables.Layouts.GetLayoutForProfile(typeDefRow,
                (uint)profileRow);
            ConsumeWork(sizeof(uint) + 1);
            if (tables.Layouts.GetTargetProfileRef(layoutRow)
                != (uint)profileRow)
            {
                throw InvalidBinding();
            }

            return layoutRow;
        }

        private TrRmStructuralMetadataValidator.ValidatedExecutionTables
            GetTables(int artifactIndex)
        {
            ConsumeWork();
            if (artifactIndex == 0)
            {
                return _consumer;
            }

            if (artifactIndex <= 0 || artifactIndex >= _artifacts.Length
                || !(StringComparer.Ordinal.Equals(
                        _schemaIdentities[artifactIndex],
                        LayoutQueriesSchemaIdentity)
                    || (_allowGenericMethodTypeProviders
                        && StandardGenericMethodSchema.IsGenericMethodCalls(
                        _schemaIdentities[artifactIndex])
                    )
                    || IsC1M1SchemaIdentity(
                        _schemaIdentities[artifactIndex])
                    || IsInitializationReferenceLocalFieldLayoutSchemaIdentity(
                        _schemaIdentities[artifactIndex])
                    || IsFixedArraySchemaIdentity(
                        _schemaIdentities[artifactIndex])))
            {
                throw InvalidBinding();
            }

            if (_hasCachedProviderTables
                && _cachedProviderArtifactIndex == artifactIndex)
            {
                return _cachedProviderTables;
            }

            // Nested substitutions revisit immutable provider snapshots.
            if (StringComparer.Ordinal.Equals(
                    _schemaIdentities[artifactIndex],
                    LayoutQueriesSchemaIdentity))
            {
                var standardTables
                    = TrRmStructuralMetadataValidator
                        .ValidateAndGetExecutionTables(
                            new TrRmArtifactIndex(
                                _artifacts[artifactIndex]));
                _cachedProviderTables = standardTables;
                _cachedProviderArtifactIndex = artifactIndex;
                _hasCachedProviderTables = true;
                return standardTables;
            }

            if (IsInitializationReferenceLocalFieldLayoutSchemaIdentity(
                    _schemaIdentities[artifactIndex]))
            {
                var initializationReferenceIndex
                    = StringComparer.Ordinal.Equals(
                        _schemaIdentities[artifactIndex],
                        InitializationReferenceNestedFieldDestinationsSchemaIdentity)
                    ? TrRmArtifactIndex
                        .ForInitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft(
                            _artifacts[artifactIndex])
                    : TrRmArtifactIndex
                        .ForInitializationReferenceLocalFieldLayoutDraft(
                            _artifacts[artifactIndex]);
                var initializationReferenceTables
                    = TrRmStructuralMetadataValidator
                        .ValidateAndGetExecutionTables(
                            initializationReferenceIndex);
                _cachedProviderTables = initializationReferenceTables;
                _cachedProviderArtifactIndex = artifactIndex;
                _hasCachedProviderTables = true;
                return initializationReferenceTables;
            }

            if (_allowGenericMethodTypeProviders
                && StandardGenericMethodSchema.IsGenericMethodCalls(
                    _schemaIdentities[artifactIndex]))
            {
                var schema
                    = StringComparer.Ordinal.Equals(
                        _schemaIdentities[artifactIndex],
                        StandardGenericMethodSchema.GenericMethodCalls)
                    ? TrRmArtifactSchema.GenericMethodCallsDraft
                    : TrRmArtifactSchema
                        .GenericMethodCallsParameterLabelsDraft;
                var genericMethodTables
                    = TrRmStructuralMetadataValidator
                        .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                            new TrRmArtifactIndex(
                                _artifacts[artifactIndex], schema));
                _cachedProviderTables = genericMethodTables;
                _cachedProviderArtifactIndex = artifactIndex;
                _hasCachedProviderTables = true;
                return genericMethodTables;
            }

            if (IsC1M1SchemaIdentity(
                    _schemaIdentities[artifactIndex]))
            {
                var c1m1Index = StringComparer.Ordinal.Equals(
                        _schemaIdentities[artifactIndex], CM1SchemaIdentity)
                    ? TrRmArtifactIndex
                        .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                            _artifacts[artifactIndex])
                    : TrRmArtifactIndex.ForC1M1Draft(
                        _artifacts[artifactIndex]);
                var c1m1Tables = TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(c1m1Index);
                _cachedProviderTables = c1m1Tables;
                _cachedProviderArtifactIndex = artifactIndex;
                _hasCachedProviderTables = true;
                return c1m1Tables;
            }

            var schema = GetFixedArraySchema(_schemaIdentities[artifactIndex]);
            var index = _a3ResourceLedger is not null
                ? new TrRmArtifactIndex(_artifacts[artifactIndex], schema,
                    _a3ResourceLedger)
                : schema == TrRmArtifactSchema.LocalAggregateLayoutDraft
                ? TrRmArtifactIndex.ForLocalAggregateLayoutDraft(
                    _artifacts[artifactIndex])
                : schema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        _artifacts[artifactIndex])
                : schema
                    .UsesForeignInvocationStorageFixedArrayRepeatInitialization()
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                        _artifacts[artifactIndex])
                : schema.UsesRawUnionValueCoverage()
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyReturnCoverageDraft(
                        _artifacts[artifactIndex])
                : schema.UsesScalarFrozenValues()
                    ? TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                            _artifacts[artifactIndex])
                : schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
                    ? TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                            _artifacts[artifactIndex])
                : schema.UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                    ? TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                            _artifacts[artifactIndex])
                : TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        _artifacts[artifactIndex]);
            var tables = schema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    index, _a3ResourceLedger,
                    createA3ResourceLedger: _consumer.Schema
                        == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft)
                : schema
                .UsesForeignInvocationStorageFixedArrayRepeatInitialization()
                ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index)
                : schema.UsesRawUnionValueCoverage()
                ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageDraftAndGetExecutionTables(
                        index)
                : schema.UsesScalarFrozenValues()
                    ? TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                            index)
                : schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
                    ? TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                            index)
                : schema.UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                    ? TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyEmptyInitializationDraftAndGetExecutionTables(
                            index)
                : TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                        index, _a3ResourceLedger);
            _cachedProviderTables = tables;
            _cachedProviderArtifactIndex = artifactIndex;
            _hasCachedProviderTables = true;
            return tables;
        }

        private readonly bool IsFixedArraySchemaIdentity(
            string schemaIdentity)
        {
            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                FixedArraySchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArraySchemaIdentity))
            {
                return true;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                FixedArrayEmptyInitializationSchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayEmptyInitializationSchemaIdentity))
            {
                return true;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                FixedArrayEmptyUnitInitializationSchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayEmptyUnitInitializationSchemaIdentity))
            {
                return true;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                FixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity))
            {
                return true;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                FixedArrayCopyReturnCoverageSchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayCopyReturnCoverageSchemaIdentity))
            {
                return true;
            }

            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity))
            {
                return true;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                LocalAggregateLayoutSchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                LocalAggregateLayoutSchemaIdentity))
            {
                return true;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                GenericFixedArraySchemaIdentity));
            return StringComparer.Ordinal.Equals(schemaIdentity,
                GenericFixedArraySchemaIdentity);
        }

        private readonly bool IsGenericFixedArraySchemaIdentity(
            string schemaIdentity)
        {
            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                GenericFixedArraySchemaIdentity));
            return StringComparer.Ordinal.Equals(schemaIdentity,
                GenericFixedArraySchemaIdentity);
        }

        private readonly bool IsTypeProviderSchemaIdentity(
            string schemaIdentity)
            => StringComparer.Ordinal.Equals(schemaIdentity,
                LayoutQueriesSchemaIdentity)
                || (_allowGenericMethodTypeProviders
                    && StandardGenericMethodSchema.IsGenericMethodCalls(
                        schemaIdentity))
                || IsInitializationReferenceLocalFieldLayoutSchemaIdentity(
                    schemaIdentity)
                || IsC1M1SchemaIdentity(schemaIdentity)
                || IsFixedArraySchemaIdentity(schemaIdentity);

        private readonly bool IsGenericTypeProviderSchemaIdentity(
            string schemaIdentity)
            => StringComparer.Ordinal.Equals(schemaIdentity,
                LayoutQueriesSchemaIdentity)
                || (_allowGenericMethodTypeProviders
                    && StandardGenericMethodSchema.IsGenericMethodCalls(
                        schemaIdentity))
                || IsGenericFixedArraySchemaIdentity(schemaIdentity);

        private readonly bool IsC1M1SchemaIdentity(string schemaIdentity)
            => TrRmFixedArrayProviderBindingValidator
                .IsC1M1SchemaIdentity(schemaIdentity)
                || TrRmExecutableCompiler
                    .IsSelectedC1EnumClosureSchemaIdentity(schemaIdentity);

        private readonly TrRmArtifactSchema GetFixedArraySchema(
            string schemaIdentity)
        {
            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                GenericFixedArraySchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                GenericFixedArraySchemaIdentity))
            {
                return TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;
            }

            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayEmptyInitializationSchemaIdentity))
            {
                return TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft;
            }

            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayEmptyUnitInitializationSchemaIdentity))
            {
                return TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
            }

            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity))
            {
                return TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft;
            }

            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayCopyReturnCoverageSchemaIdentity))
            {
                return TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft;
            }

            if (StringComparer.Ordinal.Equals(schemaIdentity,
                FixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity))
            {
                return TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                LocalAggregateLayoutSchemaIdentity));
            if (StringComparer.Ordinal.Equals(schemaIdentity,
                LocalAggregateLayoutSchemaIdentity))
            {
                return TrRmArtifactSchema.LocalAggregateLayoutDraft;
            }

            ConsumeWork(GetStringComparisonWork(schemaIdentity,
                FixedArraySchemaIdentity));
            return StringComparer.Ordinal.Equals(schemaIdentity,
                    FixedArraySchemaIdentity)
                ? TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                : throw InvalidBinding();
        }

        private readonly uint Read(ReadOnlySpan<byte> row, int offset)
        {
            ConsumeWork(sizeof(uint) + 1);
            return BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(offset,
                sizeof(uint)));
        }

        private readonly void ConsumeWork(ulong units = 1)
            => _a3ResourceLedger?.ConsumeWork(units);

        private static ulong GetStringComparisonWork(string left,
            string right)
        {
            var characterCount
                = checked((ulong)left.Length + (ulong)right.Length);
            var payloadBytes = checked(characterCount * sizeof(char));
            return checked(payloadBytes + 1UL);
        }

        private static InvalidDataException InvalidBinding()
            => new("Invalid TRRM fixed-array TypeDef provider binding.");

        private static InvalidDataException InvalidFieldBinding()
            => new("Invalid TRRM Standard FieldDef provider binding.");
    }
}
