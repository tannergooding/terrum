// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal sealed partial class TrRmStandardNativeBindingPlan
{
    internal static BoundProgram BuildConstruction2MethodProviderClosure(
        BoundStandardMethodProviderClosure closure)
        => new LocalAggregateLayoutNativeProgramBuilder(closure,
            initializationReferenceLocalFieldLayout: false,
            nestedInitializationReferenceFieldDestinations: false,
            generalPredicateGraphsInitializationReferenceBodies: false,
            standardNativePointerLoads: false,
            nativeParameterContracts: false,
            construction2MethodLinking: true).Build();

    private BoundProgram CreateLocalAggregateLayoutLoweringProgram(
        bool initializationReferenceLocalFieldLayout = false,
        bool nestedInitializationReferenceFieldDestinations = false,
        bool generalPredicateGraphsInitializationReferenceBodies = false,
        bool standardNativePointerLoads = false,
        bool nativeParameterContracts = false)
    {
        if (initializationReferenceLocalFieldLayout
            && (generalPredicateGraphsInitializationReferenceBodies
                || standardNativePointerLoads))
        {
            throw new ArgumentException(
                "The initialization-reference native schema selections are mutually exclusive.");
        }
        if (nestedInitializationReferenceFieldDestinations
            && !initializationReferenceLocalFieldLayout)
        {
            throw new ArgumentException(
                "The nested FieldPath successor requires the local-field initialization profile.",
                nameof(nestedInitializationReferenceFieldDestinations));
        }
        if (nativeParameterContracts && !standardNativePointerLoads)
        {
            throw new ArgumentException(
                "The NativeParameter schema selection requires pointer-load Bodies.",
                nameof(nativeParameterContracts));
        }

        // Reload the owned snapshots, including uncalled Imports, before creating
        // any backend input. The retained semantic Programs are never linked in place.
        var providers = new TrRmStandardArtifactInput[_artifacts.Length - 1];
        for (var index = 0; index < providers.Length; index++)
        {
            providers[index] = new TrRmStandardArtifactInput(
                _schemaIdentities[index + 1], _artifacts[index + 1]);
        }

        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(_schemaIdentities[0],
                _artifacts[0]), ModuleIdentity, providers);
        if (nestedInitializationReferenceFieldDestinations)
        {
            ValidateNestedFieldProviderClosure(
                RootProgram.StandardFieldProviderClosure,
                reloaded.StandardFieldProviderClosure);
        }
        var original = RootProgram.StandardMethodProviderClosure;
        var closure = reloaded.StandardMethodProviderClosure;
        if (original is null || closure is null)
        {
            if (original is not null || closure is not null
                || _modules.Length != 1
                || RootProgram.Functions.Length != 0
                || reloaded.Functions.Length != 0)
            {
                throw new InvalidDataException(
                    "The L1 native plan lost its semantic provider closure.");
            }

            SemanticVerifier.VerifyLibrary(reloaded);
            return reloaded;
        }

        ValidateLocalAggregateLayoutClosure(original, closure);
        return new LocalAggregateLayoutNativeProgramBuilder(closure,
            initializationReferenceLocalFieldLayout,
            nestedInitializationReferenceFieldDestinations,
            generalPredicateGraphsInitializationReferenceBodies,
            standardNativePointerLoads, nativeParameterContracts).Build();
    }

    private void ValidateNestedFieldProviderClosure(
        BoundStandardFieldProviderClosure? expected,
        BoundStandardFieldProviderClosure? actual)
    {
        if (expected is null || actual is null)
        {
            if (expected is not null || actual is not null)
            {
                throw new InvalidDataException(
                    "The nested FieldPath native plan lost its FieldDef provider closure.");
            }
            return;
        }

        if (!StringComparer.Ordinal.Equals(expected.RootModuleIdentity,
                ModuleIdentity)
            || !StringComparer.Ordinal.Equals(actual.RootModuleIdentity,
                ModuleIdentity)
            || expected.RootModuleRow != RootModuleRow
            || actual.RootModuleRow != RootModuleRow
            || !StringComparer.Ordinal.Equals(expected.SchemaIdentity,
                InitializationReferenceNestedFieldDestinationsSchemaIdentity)
            || !StringComparer.Ordinal.Equals(actual.SchemaIdentity,
                InitializationReferenceNestedFieldDestinationsSchemaIdentity)
            || expected.TargetRequirement != actual.TargetRequirement
            || expected.RuntimePolicy != actual.RuntimePolicy
            || expected.Artifacts.Length != _artifacts.Length
            || actual.Artifacts.Length != _artifacts.Length
            || expected.Modules.Length != _modules.Length
            || actual.Modules.Length != _modules.Length
            || !expected.Modules.SequenceEqual(actual.Modules)
            || !expected.TypeEdges.SequenceEqual(actual.TypeEdges)
            || !expected.FieldEdges.SequenceEqual(actual.FieldEdges))
        {
            throw new InvalidDataException(
                "The nested FieldPath native plan changed its exact FieldDef provider closure.");
        }

        for (var artifactIndex = 0; artifactIndex < _artifacts.Length;
            artifactIndex++)
        {
            var expectedArtifact = expected.Artifacts[artifactIndex];
            var actualArtifact = actual.Artifacts[artifactIndex];
            if (expectedArtifact.ArtifactIndex != artifactIndex
                || actualArtifact.ArtifactIndex != artifactIndex
                || !StringComparer.Ordinal.Equals(
                    expectedArtifact.SchemaIdentity,
                    InitializationReferenceNestedFieldDestinationsSchemaIdentity)
                || !StringComparer.Ordinal.Equals(
                    actualArtifact.SchemaIdentity,
                    InitializationReferenceNestedFieldDestinationsSchemaIdentity)
                || !expectedArtifact.ArtifactSnapshot.AsSpan()
                    .SequenceEqual(_artifacts[artifactIndex])
                || !actualArtifact.ArtifactSnapshot.AsSpan()
                    .SequenceEqual(_artifacts[artifactIndex]))
            {
                throw new InvalidDataException(
                    "The nested FieldPath native plan changed an exact provider artifact.");
            }
        }
    }

    private void ValidateLocalAggregateLayoutClosure(
        BoundStandardMethodProviderClosure original,
        BoundStandardMethodProviderClosure actual)
    {
        if (original.RootArtifactIndex != 0
            || actual.RootArtifactIndex != 0
            || original.RootModuleRow != RootModuleRow
            || actual.RootModuleRow != RootModuleRow
            || !StringComparer.Ordinal.Equals(
                original.RootModuleIdentity, ModuleIdentity)
            || !StringComparer.Ordinal.Equals(
                actual.RootModuleIdentity, ModuleIdentity)
            || original.Artifacts.Length != _artifacts.Length
            || actual.Artifacts.Length != _artifacts.Length
            || original.Modules.Length != _modules.Length
            || actual.Modules.Length != _modules.Length
            || !original.MethodEdges.SequenceEqual(actual.MethodEdges)
            || !original.TypeEdges.SequenceEqual(actual.TypeEdges))
        {
            throw new InvalidDataException(
                "The L1 native plan changed its original MethodDef or TypeDef provider closure.");
        }

        for (var index = 0; index < _artifacts.Length; index++)
        {
            var expectedArtifact = original.Artifacts[index];
            var actualArtifact = actual.Artifacts[index];
            if (expectedArtifact.ArtifactIndex != index
                || actualArtifact.ArtifactIndex != index
                || !StringComparer.Ordinal.Equals(
                    expectedArtifact.SchemaIdentity, _schemaIdentities[index])
                || !StringComparer.Ordinal.Equals(
                    actualArtifact.SchemaIdentity, _schemaIdentities[index])
                || !expectedArtifact.ArtifactSnapshot.AsSpan()
                    .SequenceEqual(_artifacts[index])
                || !actualArtifact.ArtifactSnapshot.AsSpan()
                    .SequenceEqual(_artifacts[index]))
            {
                throw new InvalidDataException(
                    "The L1 native plan changed an exact selected artifact.");
            }
        }

        for (var index = 0; index < _modules.Length; index++)
        {
            var expectedModule = original.Modules[index];
            var actualModule = actual.Modules[index];
            var selection = _modules[index];
            var actualSelection = new TrRmStandardModuleSelection(
                    actualModule.ArtifactIndex, actualModule.ModuleRow,
                    actualModule.Identity, actualModule.TargetRequirement,
                    actualModule.RuntimePolicy, actualModule.EntryMethodDefRow,
                    actualModule.MethodDefStart, actualModule.MethodDefEnd) {
                GoverningRuleEdition = actualModule.GoverningRuleEdition,
            };
            if (actualSelection != selection
                || (expectedModule with { Program = actualModule.Program,
                    FunctionIndexByMethodRow = actualModule.FunctionIndexByMethodRow,
                    FunctionIndexByImportRow = actualModule.FunctionIndexByImportRow,
                }) != actualModule
                || !expectedModule.FunctionIndexByMethodRow.SequenceEqual(
                    actualModule.FunctionIndexByMethodRow)
                || !expectedModule.FunctionIndexByImportRow.SequenceEqual(
                    actualModule.FunctionIndexByImportRow))
            {
                throw new InvalidDataException(
                    "The L1 native plan changed an exact selected Module or MethodDef origin.");
            }
        }
    }

    private sealed class LocalAggregateLayoutNativeProgramBuilder
    {
        private readonly record struct ModuleOrigin(int ArtifactIndex, int ModuleRow);
        private readonly record struct MethodOrigin(ModuleOrigin Module, int MethodDefRow);
        private readonly record struct FunctionOrigin(ModuleOrigin Module, int FunctionIndex);
        private readonly record struct TypeOrigin(ModuleOrigin Module, uint DefinitionId);
        private readonly record struct LocalTypeOrigin(ModuleOrigin Module, PrimitiveType Type);
        private readonly record struct LocalStructureTypeOrigin(
            LocalTypeOrigin LocalType, TypeOrigin ProviderType);

        private readonly BoundStandardMethodProviderClosure _closure;
        private readonly bool _initializationReferenceLocalFieldLayout;
        private readonly bool _nestedInitializationReferenceFieldDestinations;
        private readonly bool
            _generalPredicateGraphsInitializationReferenceBodies;
        private readonly bool _standardNativePointerLoads;
        private readonly bool _nativeParameterContracts;
        private readonly bool _construction2MethodLinking;
        private readonly ModuleOrigin _root;
        private readonly Dictionary<ModuleOrigin, BoundStandardMethodProviderModule> _modules = [];
        private readonly Dictionary<MethodOrigin, int> _methods = [];
        private readonly Dictionary<FunctionOrigin, int> _functions = [];
        private readonly List<(BoundStandardMethodProviderModule Module, int FunctionIndex)> _implementations = [];
        private readonly Dictionary<TypeOrigin, TypeOrigin> _typeProviders = [];
        private readonly Dictionary<TypeOrigin, (BoundStandardMethodProviderModule Module, int Index)> _declarations = [];
        private readonly Dictionary<TypeOrigin, PrimitiveType> _nominalTypes = [];
        private readonly Dictionary<LocalTypeOrigin, PrimitiveType> _types = [];
        private readonly HashSet<LocalTypeOrigin> _pendingTypes = [];
        private readonly List<LocalStructureTypeOrigin>
            _localStructureTypeOrigins = [];
        private readonly Dictionary<LocalTypeOrigin, BoundStruct>
            _construction2LocalStructureDeclarations = [];
        private readonly List<(BoundStandardMethodProviderModule Module, int Index)> _structures = [];
        private readonly List<BoundNativeFixedArrayType> _arrays = [];
        private readonly Dictionary<(PrimitiveType ElementType, ulong ElementCount), int> _arrayIndexes = [];
        private readonly List<NativePointerTypeDefinition> _pointers = [];
        private readonly Dictionary<NativePointerTypeDefinition, int> _pointerIndexes = [];
        private readonly List<CheckedReferenceTypeDefinition>
            _checkedReferenceTypes = [];
        private readonly Dictionary<CheckedReferenceTypeDefinition, int>
            _checkedReferenceTypeIndexes = [];

        internal LocalAggregateLayoutNativeProgramBuilder(
            BoundStandardMethodProviderClosure closure,
            bool initializationReferenceLocalFieldLayout,
            bool nestedInitializationReferenceFieldDestinations,
            bool generalPredicateGraphsInitializationReferenceBodies,
            bool standardNativePointerLoads,
            bool nativeParameterContracts,
            bool construction2MethodLinking = false)
        {
            _closure = closure;
            _initializationReferenceLocalFieldLayout
                = initializationReferenceLocalFieldLayout;
            _nestedInitializationReferenceFieldDestinations
                = nestedInitializationReferenceFieldDestinations;
            _generalPredicateGraphsInitializationReferenceBodies
                = generalPredicateGraphsInitializationReferenceBodies;
            _standardNativePointerLoads = standardNativePointerLoads;
            _nativeParameterContracts = nativeParameterContracts;
            _construction2MethodLinking = construction2MethodLinking;
            _root = new ModuleOrigin(closure.RootArtifactIndex,
                closure.RootModuleRow);
        }

        internal BoundProgram Build()
        {
            var modulesByArtifact = _closure.Modules.ToLookup(
                static module => module.ArtifactIndex);
            var modulesByIdentityAndRow = _closure.Modules.ToDictionary(
                static module => (module.Identity, module.ModuleRow));
            var typeProviderTablesByArtifact = new Dictionary<int,
                TrRmStructuralMetadataValidator.ValidatedExecutionTables>();
            var providerSnapshotsByModule
                = new Dictionary<ModuleOrigin, ImmutableArray<byte>>();
            BoundStandardMethodProviderModule GetTypeProviderModule(
                BoundSourceTypeProviderSnapshot provider)
            {
                if (!modulesByIdentityAndRow.TryGetValue((
                        provider.ModuleIdentity, provider.ModuleRow),
                        out var selectedModule)
                    || selectedModule.TargetRequirement
                        != provider.TargetRequirement
                    || selectedModule.RuntimePolicy != provider.RuntimePolicy
                    || (uint)selectedModule.ArtifactIndex
                        >= (uint)_closure.Artifacts.Length)
                {
                    throw new InvalidDataException(
                        "A Construction-2 struct provider snapshot is outside the checked Module closure.");
                }

                var location = Location(selectedModule);
                var artifact = _closure.Artifacts[
                    selectedModule.ArtifactIndex];
                if (!StringComparer.Ordinal.Equals(
                        artifact.SchemaIdentity, provider.SchemaIdentity))
                {
                    throw new InvalidDataException(
                        "A Construction-2 struct provider snapshot changed its schema identity.");
                }

                if (providerSnapshotsByModule.TryGetValue(location,
                        out var previousSnapshot))
                {
                    if (previousSnapshot != provider.ArtifactSnapshot
                        && !previousSnapshot.AsSpan().SequenceEqual(
                            provider.ArtifactSnapshot.AsSpan()))
                    {
                        throw new InvalidDataException(
                            "A Construction-2 struct provider Module has conflicting artifact snapshots.");
                    }
                }
                else
                {
                    if (!artifact.ArtifactSnapshot.AsSpan().SequenceEqual(
                            provider.ArtifactSnapshot.AsSpan()))
                    {
                        throw new InvalidDataException(
                            "A Construction-2 struct provider snapshot changed its selected artifact.");
                    }

                    providerSnapshotsByModule.Add(location,
                        provider.ArtifactSnapshot);
                }

                return selectedModule;
            }

            TrRmStructuralMetadataValidator.ValidatedExecutionTables
                GetTypeProviderTables(
                    int artifactIndex)
            {
                if (typeProviderTablesByArtifact.TryGetValue(
                        artifactIndex, out var existingTables))
                {
                    return existingTables;
                }

                if ((uint)artifactIndex
                    >= (uint)_closure.Artifacts.Length)
                {
                    throw new InvalidDataException(
                        "A Construction-2 struct provider has an invalid artifact origin.");
                }

                var artifact = _closure.Artifacts[artifactIndex];
                var index = TrRmExecutableCompiler
                    .CreateForeignInvocationIndex(
                        artifact.ArtifactSnapshot.ToArray(),
                        artifact.SchemaIdentity);
                var tables = TrRmExecutableCompiler
                    .ValidateForeignInvocationArtifact(index,
                        artifact.SchemaIdentity,
                        createA3ResourceLedger: false);
                typeProviderTablesByArtifact.Add(artifactIndex, tables);
                return tables;
            }

            foreach (var artifact in _closure.Artifacts)
            {
                TrRmStructuralMetadataValidator.ValidatedExecutionTables tables;
                if (_construction2MethodLinking)
                {
                    tables = GetTypeProviderTables(artifact.ArtifactIndex);
                }
                else
                {
                    var artifactIndex = _nativeParameterContracts
                        ? _generalPredicateGraphsInitializationReferenceBodies
                            ? TrRmArtifactIndex
                                .ForInitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft(
                                    artifact.ArtifactSnapshot.AsSpan())
                            : TrRmArtifactIndex
                                .ForInitializationReferencePointerLoadNativeParameterDraft(
                                    artifact.ArtifactSnapshot.AsSpan())
                        : _standardNativePointerLoads
                            ? _generalPredicateGraphsInitializationReferenceBodies
                                ? TrRmArtifactIndex
                                    .ForGeneralPredicateGraphsNativePointerLoadsDraft(
                                        artifact.ArtifactSnapshot.AsSpan())
                                : TrRmArtifactIndex.ForNativePointerLoadsDraft(
                                    artifact.ArtifactSnapshot.AsSpan())
                            : _generalPredicateGraphsInitializationReferenceBodies
                                ? TrRmArtifactIndex
                                    .ForGeneralPredicateGraphsDraft(
                                        artifact.ArtifactSnapshot.AsSpan())
                                : _initializationReferenceLocalFieldLayout
                                    ? _nestedInitializationReferenceFieldDestinations
                                        ? TrRmArtifactIndex
                                            .ForInitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft(
                                                artifact.ArtifactSnapshot.AsSpan())
                                        : TrRmArtifactIndex
                                            .ForInitializationReferenceLocalFieldLayoutDraft(
                                                artifact.ArtifactSnapshot.AsSpan())
                                    : TrRmArtifactIndex
                                        .ForLocalAggregateLayoutDraft(
                                            artifact.ArtifactSnapshot.AsSpan());
                    tables = _nativeParameterContracts
                        ? _generalPredicateGraphsInitializationReferenceBodies
                            ? TrRmStructuralMetadataValidator
                                .ValidateInitializationReferenceGeneralPredicatePointerLoadNativeParameterDraftAndGetExecutionTables(
                                    artifactIndex)
                            : TrRmStructuralMetadataValidator
                                .ValidateInitializationReferencePointerLoadNativeParameterDraftAndGetExecutionTables(
                                    artifactIndex)
                        : _generalPredicateGraphsInitializationReferenceBodies
                            ? TrRmStructuralMetadataValidator
                                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                                    artifactIndex)
                            : _initializationReferenceLocalFieldLayout
                                || _standardNativePointerLoads
                                ? TrRmStructuralMetadataValidator
                                    .ValidateAndGetExecutionTables(
                                        artifactIndex)
                                : TrRmStructuralMetadataValidator
                                    .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                                        artifactIndex);
                }
                foreach (var module in modulesByArtifact[artifact.ArtifactIndex])
                {
                    var location = Location(module);
                    _modules.Add(location, module);
                    ValidateProgram(module.Program,
                        _construction2MethodLinking
                            || _initializationReferenceLocalFieldLayout
                            || _generalPredicateGraphsInitializationReferenceBodies
                            || _standardNativePointerLoads,
                        _standardNativePointerLoads,
                        _nativeParameterContracts,
                        _construction2MethodLinking);
                    var (typeStart, typeEnd) = tables.Modules.GetTypeDefRange(
                        module.ModuleRow);
                    if (!_construction2MethodLinking)
                    {
                        if (typeEnd - typeStart
                            != module.Program.Structures.Length)
                        {
                            throw new InvalidDataException(
                                "The L1 native type map lost its Module-owned TypeDef range.");
                        }

                        for (var index = 0;
                            index < module.Program.Structures.Length; index++)
                        {
                            _declarations.Add(new TypeOrigin(location,
                                0x1300_0000u
                                    | checked((uint)(typeStart + index))),
                                (module, index));
                        }
                    }
                    else
                    {
                        if (module.Program.Structures.Any(
                                static structure =>
                                    structure.StandardTypeOrigin is null)
                            && (typeEnd - typeStart
                                    != module.Program.Structures.Length
                                || Enumerable.Range(typeStart,
                                    typeEnd - typeStart).Any(typeDefRow =>
                                    tables.TypeDefs.GetKind(typeDefRow) != 1
                                    || !tables.Layouts
                                        .HasBlittableLayout(typeDefRow, 1, 1))))
                        {
                            throw new NotSupportedException(
                                "Construction-2 local value types require an exact Blittable struct Module range.");
                        }

                        for (var index = 0;
                            index < module.Program.Structures.Length; index++)
                        {
                            var structure = module.Program.Structures[index];
                            var origin = structure.StandardTypeOrigin;
                            if (origin is null)
                            {
                                var typeDefRow = checked(typeStart + index);
                                var typeDefId = 0x1300_0000u
                                    | checked((uint)typeDefRow);
                                var (methodStart, methodEnd)
                                    = tables.TypeDefs.GetMethodRange(
                                        typeDefRow);
                                if (tables.TypeDefs.GetGenericRange(
                                        typeDefRow).Count != 0
                                    || tables.TypeDefs.HasDeclaredUnique(
                                        typeDefRow)
                                    || methodStart != methodEnd)
                                {
                                    throw new NotSupportedException(
                                        "Construction-2 value-type linking requires non-generic, non-unique structs without owned Methods.");
                                }

                                var artifactSnapshot = _closure.Artifacts[
                                    module.ArtifactIndex].ArtifactSnapshot
                                    .ToArray();
                                var providerSnapshot
                                    = TrRmExecutableCompiler
                                        .CreateA2SourceTypeProviderSnapshot(
                                            tables, artifactSnapshot,
                                            _closure.Artifacts[
                                                module.ArtifactIndex]
                                                .SchemaIdentity,
                                            module.ModuleRow);
                                var typeExportKeys
                                    = ImmutableArray.CreateBuilder<string>();
                                foreach (var (definitionId, exportKey) in
                                    tables.Exports.GetExportsForModule(
                                        module.ModuleRow))
                                {
                                    if (definitionId == typeDefId)
                                    {
                                        typeExportKeys.Add(exportKey);
                                    }
                                }

                                var expectedDeclaration = structure with {
                                    Fields = [.. structure.Fields.Select(
                                        static field => field with {
                                            StandardFieldOrigin = null,
                                        })],
                                    StandardTypeOrigin = null,
                                    StandardLayoutOnly = false,
                                };
                                var localType = new LocalTypeOrigin(location,
                                    (PrimitiveType)(100 + index));
                                var structureWithOrigin = structure with {
                                    StandardTypeOrigin =
                                        new BoundStandardTypeOrigin(
                                            providerSnapshot, typeDefId,
                                            typeExportKeys.ToImmutable(),
                                            false, [], providerSnapshot,
                                            expectedDeclaration,
                                            module.Program
                                                .NativeFixedArrayTypes,
                                            module.Program.NativePointerTypes) {
                                            SelectedExportKeys = [],
                                        },
                                    StandardLayoutOnly = false,
                                    DeclaredCopyEvidence
                                        = tables.Schema
                                            .SupportsDeclaredCopyEvidence()
                                            ? tables.TypeDefs.HasDeclaredCopy(
                                                typeDefRow)
                                            : structure.DeclaredCopyEvidence,
                                };
                                _construction2LocalStructureDeclarations
                                    .Add(localType, structureWithOrigin);
                                var providerTypeOrigin = new TypeOrigin(
                                    location, 0x1300_0000u
                                        | checked((uint)typeDefRow));
                                if (_declarations.TryGetValue(
                                        providerTypeOrigin,
                                        out var existingDeclaration))
                                {
                                    if (Location(existingDeclaration.Module)
                                        == location)
                                    {
                                        throw new InvalidDataException(
                                            "A Construction-2 Module repeats one local struct TypeDef origin.");
                                    }

                                    _declarations[providerTypeOrigin]
                                        = (module, index);
                                }
                                else
                                {
                                    _declarations.Add(providerTypeOrigin,
                                        (module, index));
                                }

                                _localStructureTypeOrigins.Add(
                                    new LocalStructureTypeOrigin(
                                        localType,
                                        providerTypeOrigin));
                                continue;
                            }

                            if (origin.ExpectedDeclaration is null
                                || (origin.DefinitionId >> 24) != 0x13
                                || (origin.DefinitionId & 0x00FF_FFFF) == 0)
                            {
                                throw new NotSupportedException(
                                    "Construction-2 value-type linking requires exact standard struct TypeDef origins.");
                            }

                            var providerModule
                                = GetTypeProviderModule(origin.Provider);
                            var providerTypeDefRow = checked((int)(
                                origin.DefinitionId & 0x00FF_FFFF));
                            var providerTables = GetTypeProviderTables(
                                providerModule.ArtifactIndex);
                            if (providerTypeDefRow
                                    > providerTables.TypeDefs.Count
                                || providerTables.TypeDefs.GetKind(
                                    providerTypeDefRow) != 1
                                || providerTables.TypeDefs.GetOwnerModule(
                                    providerTypeDefRow)
                                    != providerModule.ModuleRow)
                            {
                                throw new InvalidDataException(
                                    "A Construction-2 struct origin does not select its provider-owned TypeDef.");
                            }

                            var providerTypeOrigin = new TypeOrigin(
                                Location(providerModule),
                                origin.DefinitionId);
                            if (!_declarations.TryGetValue(
                                    providerTypeOrigin, out _)
                                || Location(module)
                                    == Location(providerModule))
                            {
                                _declarations[providerTypeOrigin]
                                    = (module, index);
                            }
                            _localStructureTypeOrigins.Add(
                                new LocalStructureTypeOrigin(
                                    new LocalTypeOrigin(location,
                                        (PrimitiveType)(100 + index)),
                                    providerTypeOrigin));
                        }
                    }

                    for (var methodRow = module.MethodDefStart;
                        methodRow < module.MethodDefEnd; methodRow++)
                    {
                        var index = module.FunctionIndexByMethodRow[methodRow];
                        if (_construction2MethodLinking
                            && (index < 0
                                || tables.Methods.GetOwnerType(methodRow)
                                    != 0))
                        {
                            continue;
                        }

                        var function = module.Program.Functions[index];
                        if (!function.IsExternal)
                        {
                            if (_implementations.Count >= SemanticVerifier.MaximumFunctions)
                            {
                                throw new NotSupportedException(
                                    "The joint L1 native program exceeds the function limit.");
                            }
                            _methods.Add(new MethodOrigin(location, methodRow),
                                _implementations.Count);
                            _functions.Add(new FunctionOrigin(location, index),
                                _implementations.Count);
                            _implementations.Add((module, index));
                        }
                        else if (_standardNativePointerLoads
                            && !function.IsModuleReference
                            && function.ModuleImport is null
                            && function.DirectNativeImport is not null)
                        {
                            if (_implementations.Count >= SemanticVerifier.MaximumFunctions)
                            {
                                throw new NotSupportedException(
                                    "The joint L1 native program exceeds the function limit.");
                            }
                            _methods.Add(new MethodOrigin(location, methodRow),
                                _implementations.Count);
                            _functions.Add(new FunctionOrigin(location, index),
                                _implementations.Count);
                            _implementations.Add((module, index));
                        }
                    }
                }
            }

            foreach (var edge in _closure.TypeEdges)
            {
                var expected = new TypeOrigin(new ModuleOrigin(
                    edge.ConsumerArtifactIndex, edge.ConsumerModuleRow),
                    edge.ExpectedDefinitionId);
                var actual = new TypeOrigin(new ModuleOrigin(
                    edge.ProviderArtifactIndex, edge.ProviderModuleRow),
                    edge.ActualDefinitionId);
                if (_construction2MethodLinking)
                {
                    if (_declarations.ContainsKey(actual))
                    {
                        if (_typeProviders.TryGetValue(expected,
                                out var previousProvider)
                            && previousProvider != actual)
                        {
                            throw new InvalidDataException(
                                "A Construction-2 struct TypeDef Import has conflicting provider origins.");
                        }

                        _typeProviders[expected] = actual;
                        continue;
                    }

                    var actualModule = _modules[actual.Module];
                    var actualIdentity
                        = new BoundGenericNominalTypeIdentity(
                            actualModule.Identity, actualModule.Identity,
                            edge.ActualDefinitionId);
                    var expectedModule = _modules[expected.Module];
                    var expectedIdentity
                        = new BoundGenericNominalTypeIdentity(
                            expectedModule.Identity,
                            expectedModule.Identity,
                            edge.ExpectedDefinitionId);
                    var classDeclaration
                        = actualModule.Program.StandardClassDeclarations
                            .SingleOrDefault(declaration =>
                                declaration.Declaration.Identity
                                    == actualIdentity);
                    if (classDeclaration is null
                        || !classDeclaration.Expectations.Any(
                            expectation => expectation.ExpectedDeclaration
                                .Identity == expectedIdentity))
                    {
                        throw new InvalidDataException(
                            "A Construction-2 class TypeDef Import has no matching checked nominal provider view.");
                    }

                    continue;
                }

                if (_typeProviders.TryGetValue(expected, out var previous)
                    && previous != actual)
                {
                    throw new InvalidDataException(
                        "The L1 native type map has conflicting provider origins.");
                }
                _typeProviders[expected] = actual;
            }

            PrimitiveType GetOrAddStructureType(TypeOrigin origin)
            {
                var actual = ResolveTypeOrigin(origin);
                if (!_nominalTypes.TryGetValue(actual, out var type))
                {
                    if (_structures.Count >= SemanticVerifier.MaximumVariables)
                    {
                        throw new NotSupportedException(
                            "The joint L1 native program exceeds the nominal type limit.");
                    }
                    type = (PrimitiveType)(100 + _structures.Count);
                    _nominalTypes.Add(actual, type);
                    _structures.Add(_declarations[actual]);
                }

                return type;
            }

            if (_construction2MethodLinking)
            {
                foreach (var localType in _localStructureTypeOrigins)
                {
                    var type = GetOrAddStructureType(
                        localType.ProviderType);
                    if (_types.TryGetValue(localType.LocalType,
                            out var previousType)
                        && previousType != type)
                    {
                        throw new InvalidDataException(
                            "A Construction-2 local struct TypeSpec has conflicting provider origins.");
                    }

                    _types[localType.LocalType] = type;
                }
            }
            else
            {
                foreach (var (origin, declaration) in _declarations)
                {
                    var type = GetOrAddStructureType(origin);
                    _types.Add(new LocalTypeOrigin(origin.Module,
                        (PrimitiveType)(100 + declaration.Index)), type);
                }
            }

            foreach (var edge in _closure.MethodEdges)
            {
                var consumer = new ModuleOrigin(edge.ConsumerArtifactIndex,
                    edge.ConsumerModuleRow);
                var actual = new MethodOrigin(new ModuleOrigin(
                    edge.Actual.ArtifactIndex, edge.Actual.ModuleRow),
                    edge.Actual.MethodDefRow);
                if (!_methods.TryGetValue(actual, out var target)
                    || MapFunction(_modules[actual.Module],
                        edge.Actual.FunctionIndex) != target)
                {
                    throw new InvalidDataException(
                        "An L1 MethodDef Import has no provider-owned implementation.");
                }
                AddImport(new FunctionOrigin(consumer,
                    edge.Expected.FunctionIndex), target);
                AddImport(new FunctionOrigin(consumer,
                    _modules[consumer].FunctionIndexByMethodRow[
                        edge.Expected.MethodDefRow]), target);
            }
            foreach (var module in _closure.Modules)
            {
                for (var index = 0; index < module.Program.Functions.Length; index++)
                {
                    _ = MapFunction(module, index);
                }
            }

            var structures = ImmutableArray.CreateBuilder<BoundStruct>(_structures.Count);
            for (var index = 0; index < _structures.Count; index++)
            {
                var (module, sourceIndex) = _structures[index];
                var sourceStructure = module.Program.Structures[
                    sourceIndex];
                if (_construction2MethodLinking
                    && _construction2LocalStructureDeclarations.TryGetValue(
                        new LocalTypeOrigin(Location(module),
                            (PrimitiveType)(100 + sourceIndex)),
                        out var localStructure)
                    )
                {
                    sourceStructure = localStructure;
                }

                var structure = BoundTreeTraversal.RewriteTypes(
                    sourceStructure,
                    type => MapType(module, type));
                if (_construction2MethodLinking)
                {
                    structure = structure with {
                        StandardLayoutOnly = false,
                    };
                }

                structures.Add(_construction2MethodLinking
                    ? structure
                    : structure with {
                        Name = $"__trrm_l1_type_{index}",
                    });
            }

            // Reserve root names before allocating backend-only provider names.
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (module, index) in _implementations)
            {
                if (Location(module) == _root
                    && !names.Add(module.Program.Functions[index].Name))
                {
                    throw new NotSupportedException(
                        "The L1 native root contains ambiguous backend function names.");
                }
            }
            var functions = ImmutableArray.CreateBuilder<BoundFunction>(_implementations.Count);
            foreach (var (module, index) in _implementations)
            {
                var function = BoundTreeTraversal.RewriteTypes(
                    module.Program.Functions[index],
                    type => MapType(module, type));
                function = BoundTreeTraversal.RewriteCalls(function,
                    call => call with {
                        Function = MapFunction(module, call.Function),
                    });
                if (Location(module) != _root)
                {
                    var name = $"__trrm_l1_method_{module.ArtifactIndex}_{module.ModuleRow}_{index}";
                    while (!names.Add(name))
                    {
                        name += "_";
                    }
                    function = function with {
                        Name = name,
                        IsInternal = !function.IsPrivate,
                    };
                }
                functions.Add(function);
            }

            var rootProgram = _modules[_root].Program;
            var program = rootProgram with {
                Functions = functions.MoveToImmutable(),
                EntryPoint = rootProgram.EntryPoint < 0 ? -1
                    : MapFunction(_modules[_root], rootProgram.EntryPoint),
                Structures = structures.MoveToImmutable(),
                NativeFixedArrayTypes = [.. _arrays],
                NativePointerTypes = [.. _pointers],
                CheckedReferenceTypes = [.. _checkedReferenceTypes],
                SourceTypeDeclarations = [],
                SourceTypePublications = default,
                StandardMethodProviderClosure = _closure,
            };
            if (_standardNativePointerLoads && program.EntryPoint < 0)
            {
                SemanticVerifier.VerifyLibraryWithDirectNativeImports(program);
            }
            else if (_standardNativePointerLoads)
            {
                SemanticVerifier.VerifyWithDirectNativeImports(program);
            }
            else if (program.EntryPoint < 0)
            {
                SemanticVerifier.VerifyLibrary(program);
            }
            else
            {
                SemanticVerifier.Verify(program);
            }
            return program;
        }

        private static ModuleOrigin Location(
            BoundStandardMethodProviderModule module)
            => new(module.ArtifactIndex, module.ModuleRow);

        private static void ValidateProgram(BoundProgram program,
            bool supportsCheckedReferenceTypes,
            bool standardNativePointerLoads,
            bool nativeParameterContracts,
            bool construction2MethodLinking)
        {
            if (program.RuntimePolicy != BoundRuntimePolicy.FailFast
                || !program.Classes.IsEmpty || !program.Buffers.IsEmpty
                || !program.NativeFunctionPointerSignatures.IsEmpty
                || (!supportsCheckedReferenceTypes
                    && !program.CheckedReferenceTypes.IsEmpty)
                || (!construction2MethodLinking
                    && !program.GenericNominalTypeDefinitions.IsEmpty)
                || (!construction2MethodLinking
                    && !program.GenericNominalTypeInstances.IsEmpty)
                || (!construction2MethodLinking
                    && !program.StandardClassDeclarations.IsEmpty)
                || !program.Interfaces.IsEmpty || !program.EnumDefinitions.IsEmpty
                || program.RequiresSourceGrant
                || program.Functions.Any(static function =>
                    function.NativeVTableMethod is not null
                    || function.NativeFunctionPointerType is not null
                    || function.NativeCallableEntryName is not null
                    || function.DeclaringStruct is not null
                    || function.IsClassConstructor
                    || function.ClassConstructor >= 0)
                || program.Functions.Any(function =>
                    function.DirectNativeImport is not null
                    && !(standardNativePointerLoads
                        && function.IsExternal
                        && ((!function.IsModuleReference
                                && function.ModuleImport is null)
                            || (nativeParameterContracts
                                && function.IsModuleReference
                                && function.ModuleImport is not null)))))
            {
                throw new NotSupportedException(
                    "The L1 native operation requires the selected ordinary MethodDef and Copy-layout profile.");
            }
        }

        private TypeOrigin ResolveTypeOrigin(TypeOrigin origin)
        {
            var visited = new HashSet<TypeOrigin>();
            while (_typeProviders.TryGetValue(origin, out var provider))
            {
                if (!visited.Add(origin))
                {
                    throw new InvalidDataException(
                        "The L1 native type map contains a provider identity cycle.");
                }
                origin = provider;
            }
            if (!_declarations.ContainsKey(origin))
            {
                throw new InvalidDataException(
                    "The L1 native type map lost an exact selected TypeDef.");
            }
            return origin;
        }

        private void AddImport(FunctionOrigin origin, int target)
        {
            if (_functions.TryGetValue(origin, out var previous)
                && previous != target)
            {
                throw new InvalidDataException(
                    "The L1 native method map has conflicting provider origins.");
            }
            var function = _modules[origin.Module].Program.Functions[
                origin.FunctionIndex];
            if (!function.IsExternal || !function.IsModuleReference
                || function.ModuleImport is null)
            {
                throw new InvalidDataException(
                    "The L1 native method map lost its original Import expectation.");
            }
            _functions[origin] = target;
        }

        private int MapFunction(BoundStandardMethodProviderModule module,
            int index)
            => _functions.TryGetValue(new FunctionOrigin(Location(module), index),
                out var mapped) ? mapped : throw new InvalidDataException(
                    "The L1 native method map contains an unresolved call target.");

        private PrimitiveType MapType(
            BoundStandardMethodProviderModule module, PrimitiveType type,
            int depth = 0)
        {
            if (_construction2MethodLinking
                && BoundGenericNominalTypeTypes.IsType(type))
            {
                return type;
            }

            var origin = new LocalTypeOrigin(Location(module), type);
            if (_types.TryGetValue(origin, out var mapped))
            {
                return mapped;
            }
            var isCheckedReferenceType = CheckedReferenceTypes.IsType(type);
            if (!isCheckedReferenceType
                && !NativePointerTypes.IsType(type)
                && !NativeFixedArrayTypes.IsType(type))
            {
                if ((int)type >= 100)
                {
                    throw new InvalidDataException(
                        "The L1 native type map contains an unresolved nominal identity.");
                }
                return type;
            }
            if (depth > SemanticVerifier.MaximumDepth
                || !_pendingTypes.Add(origin))
            {
                throw new InvalidDataException(
                    "The L1 native type map contains an invalid structural type recipe.");
            }

            if (isCheckedReferenceType)
            {
                var referenceIndex = CheckedReferenceTypes.Index(type);
                if ((uint)referenceIndex
                    >= (uint)module.Program.CheckedReferenceTypes.Length)
                {
                    throw new InvalidDataException(
                        "The B+I native type map contains an unknown checked-reference type.");
                }

                var definition
                    = module.Program.CheckedReferenceTypes[referenceIndex] with {
                        ReferentType = MapType(module,
                            module.Program.CheckedReferenceTypes[
                                referenceIndex].ReferentType, depth + 1),
                    };
                if (!_checkedReferenceTypeIndexes.TryGetValue(definition,
                        out var mappedReferenceIndex))
                {
                    if (_checkedReferenceTypes.Count
                        >= SemanticVerifier.MaximumAnalysisVisits)
                    {
                        throw new NotSupportedException(
                            "The joint B+I reference type table exceeds its limit.");
                    }
                    mappedReferenceIndex = _checkedReferenceTypes.Count;
                    _checkedReferenceTypeIndexes.Add(definition,
                        mappedReferenceIndex);
                    _checkedReferenceTypes.Add(definition);
                }
                mapped = CheckedReferenceTypes.Type(
                    mappedReferenceIndex);
            }
            else if (NativeFixedArrayTypes.IsType(type))
            {
                var source = module.Program.NativeFixedArrayTypes[
                    NativeFixedArrayTypes.Index(type)];
                var array = source with {
                    ElementType = MapType(module, source.ElementType, depth + 1),
                };
                var identity = (array.ElementType, array.ElementCount);
                if (!_arrayIndexes.TryGetValue(identity, out var index))
                {
                    if (_arrays.Count >= SemanticVerifier.MaximumAnalysisVisits)
                    {
                        throw new NotSupportedException(
                            "The joint L1 native array table exceeds its limit.");
                    }
                    index = _arrays.Count;
                    _arrayIndexes.Add(identity, index);
                    _arrays.Add(array);
                }
                else if (_arrays[index] != array)
                {
                    throw new InvalidDataException(
                        "One L1 array identity has conflicting selected layout facts.");
                }
                mapped = NativeFixedArrayTypes.Type(index);
            }
            else
            {
                var source = module.Program.NativePointerTypes[
                    NativePointerTypes.Index(type)];
                var pointer = source with {
                    PointeeType = MapType(module, source.PointeeType, depth + 1),
                };
                if (!_pointerIndexes.TryGetValue(pointer, out var index))
                {
                    if (_pointers.Count >= SemanticVerifier.MaximumAnalysisVisits)
                    {
                        throw new NotSupportedException(
                            "The joint L1 native pointer table exceeds its limit.");
                    }
                    index = _pointers.Count;
                    _pointerIndexes.Add(pointer, index);
                    _pointers.Add(pointer);
                }
                mapped = NativePointerTypes.Type(index);
            }

            _ = _pendingTypes.Remove(origin);
            _types.Add(origin, mapped);
            return mapped;
        }
    }
}
