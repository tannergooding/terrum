// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>One complete TRRM artifact paired with its exact out-of-band schema identity.</summary>
/// <param name="SchemaIdentity">The case-sensitive schema identity selected for the artifact.</param>
/// <param name="Artifact">The complete artifact bytes.</param>
public sealed record TrRmStandardArtifactInput(
    string SchemaIdentity, ReadOnlyMemory<byte> Artifact);

public static partial class TrRmExecutableCompiler
{
    /// <summary>Loads one explicitly selected Standard Module and its supplied provider artifacts.</summary>
    /// <param name="input">The complete root artifact and its exact schema selection.</param>
    /// <param name="moduleIdentity">The exact identity of the root Module to load.</param>
    /// <param name="providers">The complete explicitly supplied provider artifact set.</param>
    /// <returns>The checked root program.</returns>
    /// <exception cref="ArgumentNullException">An input, provider list or artifact is null.</exception>
    /// <exception cref="ArgumentException">A selection or Module identity is empty or unrecognized.</exception>
    /// <exception cref="NotSupportedException">The selected schema is not supported by this operation.</exception>
    /// <exception cref="InvalidDataException">An artifact or selected Module is invalid.</exception>
    /// <exception cref="OverflowException">A selected A3 target-layout calculation exceeds its representable range.</exception>
    /// <exception cref="TrRmResourceLimitException">The selected A3 request exceeds deterministic layout capacity.</exception>
    public static BoundProgram LoadStandard(TrRmStandardArtifactInput input,
        string moduleIdentity, IReadOnlyList<TrRmStandardArtifactInput> providers)
    {
        return LoadStandard(input, moduleIdentity, providers,
            []);
    }

    /// <summary>Loads one explicitly selected Standard Module with host-selected Core exception anchors.</summary>
    /// <param name="input">The complete root artifact and its exact schema selection.</param>
    /// <param name="moduleIdentity">The exact identity of the root Module to load.</param>
    /// <param name="providers">The complete explicitly supplied provider artifact set.</param>
    /// <param name="coreExceptionIdentitySelections">The exact Core exception anchors selected for using Modules.</param>
    /// <returns>The checked root program.</returns>
    /// <exception cref="ArgumentNullException">An input, provider list or artifact is null.</exception>
    /// <exception cref="ArgumentException">A selection or Module identity is empty, duplicate or unrecognized.</exception>
    /// <exception cref="NotSupportedException">The selected schema is not supported by this operation.</exception>
    /// <exception cref="InvalidDataException">An artifact or selected Module is invalid.</exception>
    /// <exception cref="OverflowException">A selected A3 target-layout calculation exceeds its representable range.</exception>
    /// <exception cref="TrRmResourceLimitException">The selected A3 request exceeds deterministic layout capacity.</exception>
    public static BoundProgram LoadStandard(TrRmStandardArtifactInput input,
        string moduleIdentity, IReadOnlyList<TrRmStandardArtifactInput> providers,
        ImmutableArray<CoreExceptionIdentitySelection>
            coreExceptionIdentitySelections)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providers);
        var (artifacts, schemaIdentities, a3LayoutResourceLedger)
            = SnapshotStandardInputsForOperation(input, moduleIdentity,
                providers);
        return LoadStandardProgram(artifacts, schemaIdentities,
            moduleIdentity, true, a3LayoutResourceLedger,
            out _, out _, out _, out _, out _, coreExceptionIdentitySelections);
    }

    internal static TrRmStandardNativeBindingPlan
        LoadStandardNativeBindingPlan(TrRmStandardArtifactInput input,
            string moduleIdentity,
            IReadOnlyList<TrRmStandardArtifactInput> providers,
            TrRmStandardNativeOperation operation,
            NativeRuntimePolicy runtimePolicy)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        if (runtimePolicy != NativeRuntimePolicy.FailFast)
        {
            throw Unsupported(
                "The bounded F native binding plan supports fail-fast only.");
        }

        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providers);
        var (artifacts, schemaIdentities, selectedA3LayoutResourceLedger)
            = SnapshotStandardInputsForOperation(input, moduleIdentity,
                providers);

        var program = LoadStandardProgram(artifacts, schemaIdentities,
            moduleIdentity, true, selectedA3LayoutResourceLedger,
            out var providerBindings,
            out var providerDependencyBindings, out var typeProviderBindings,
            out var moduleValidation,
            out var a3LayoutResourceLedger);
        return new TrRmStandardNativeBindingPlan(program, moduleIdentity,
            artifacts, schemaIdentities,
            moduleValidation.Modules, moduleValidation.RootModuleRow,
            operation, runtimePolicy, providerBindings,
            providerDependencyBindings,
            typeProviderBindings, a3LayoutResourceLedger);
    }

    /// <summary>Creates a source reference from one explicitly selected Standard Module.</summary>
    /// <param name="input">The complete provider artifact and its exact schema selection.</param>
    /// <param name="moduleIdentity">The exact identity of the Module to expose.</param>
    /// <param name="providers">The complete explicitly supplied provider artifact set.</param>
    /// <returns>The selected Module's exported declarations.</returns>
    /// <exception cref="ArgumentNullException">An input, provider list or artifact is null.</exception>
    /// <exception cref="ArgumentException">A selection or Module identity is empty or unrecognized.</exception>
    /// <exception cref="NotSupportedException">The selected schema is not supported by this operation.</exception>
    /// <exception cref="InvalidDataException">An artifact or selected Module is invalid.</exception>
    /// <exception cref="OverflowException">A selected A3 target-layout calculation exceeds its representable range.</exception>
    /// <exception cref="TrRmResourceLimitException">The selected A3 request exceeds deterministic layout capacity.</exception>
    public static CompilationReference CreateStandardReference(
        TrRmStandardArtifactInput input, string moduleIdentity,
        IReadOnlyList<TrRmStandardArtifactInput> providers)
        => CreateStandardReferenceWithCoreExceptionIdentitySelections(
            input, moduleIdentity, providers, []);

    /// <summary>Creates a source reference from one explicitly selected Standard Module with Core exception anchors.</summary>
    /// <param name="input">The complete provider artifact and its exact schema selection.</param>
    /// <param name="moduleIdentity">The exact identity of the Module to expose.</param>
    /// <param name="providers">The complete explicitly supplied provider artifact set.</param>
    /// <param name="coreExceptionIdentitySelections">The exact Core exception anchors selected for using Modules.</param>
    /// <returns>The selected Module's exported declarations.</returns>
    /// <exception cref="ArgumentNullException">An input, provider list or artifact is null.</exception>
    /// <exception cref="ArgumentException">A selection or Module identity is empty, duplicate or unrecognized.</exception>
    /// <exception cref="NotSupportedException">The selected schema is not supported by this operation.</exception>
    /// <exception cref="InvalidDataException">An artifact or selected Module is invalid.</exception>
    /// <exception cref="OverflowException">A selected A3 target-layout calculation exceeds its representable range.</exception>
    /// <exception cref="TrRmResourceLimitException">The selected A3 request exceeds deterministic layout capacity.</exception>
    public static CompilationReference
        CreateStandardReferenceWithCoreExceptionIdentitySelections(
        TrRmStandardArtifactInput input, string moduleIdentity,
        IReadOnlyList<TrRmStandardArtifactInput> providers,
        ImmutableArray<CoreExceptionIdentitySelection>
            coreExceptionIdentitySelections)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providers);
        var (artifacts, schemaIdentities, selectedA3LayoutResourceLedger)
            = SnapshotStandardInputsForOperation(input, moduleIdentity,
                providers);
        var program = LoadStandardProgram(artifacts, schemaIdentities,
            moduleIdentity, false, selectedA3LayoutResourceLedger,
            out _, out _, out _, out _,
            out var a3LayoutResourceLedger,
            coreExceptionIdentitySelections);
        var index = CreateForeignInvocationIndex(artifacts[0],
            input.SchemaIdentity, a3LayoutResourceLedger);
        var tables = ValidateForeignInvocationArtifact(index,
            input.SchemaIdentity, a3LayoutResourceLedger);
        var moduleRow = FindModuleRow(tables.Modules, moduleIdentity);
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(moduleRow);
        var (methodStart, methodEnd)
            = tables.Modules.GetMethodDefRange(moduleRow);
        var functionByName = program.Functions.ToDictionary(
            static function => function.Name, StringComparer.Ordinal);
        var functions = ImmutableArray.CreateBuilder<BoundFunction>();
        for (var row = methodStart; row < methodEnd; row++)
        {
            if (tables.NativeFunctionPointerAnchors.IsAnchorMethodDef(row))
            {
                continue;
            }

            var functionName = tables.Methods.GetName(row);
            if (!functionByName.TryGetValue(functionName, out var function))
            {
                throw Invalid("The selected F Module has an unrepresented MethodDef.");
            }

            if (!tables.Exports.TryGetExportKeyForDefinition(moduleRow,
                    0x1600_0000u | (uint)row, out var exportKey))
            {
                continue;
            }

            functions.Add(function with
            {
                Name = exportKey,
                Body = new BoundBlock([], default),
                IndexedBody = null,
                IsExternal = true,
                IsModuleReference = true,
                IsPrivate = false,
                IsInternal = false,
                ModuleImport = new BoundModuleImport(moduleIdentity,
                    exportKey, targetRequirement, runtimePolicy),
            });
        }

        if (functions.Count == 0)
        {
            throw Unsupported("The selected F Module has no supported exported declarations.");
        }

        return new CompilationReference([], functions.ToImmutable(),
            (BoundRuntimePolicy)runtimePolicy,
            nativeFunctionPointerSignatures: program.NativeFunctionPointerSignatures,
            nativePointerTypes: program.NativePointerTypes,
            coreExceptionIdentitySelections:
                coreExceptionIdentitySelections);
    }

    private static BoundProgram LoadStandardProgram(byte[][] artifacts,
        string[] schemaIdentities, string moduleIdentity,
        bool verifySemantics,
        TrRmA3LayoutResourceLedger? selectedA3LayoutResourceLedger,
        out ImmutableArray<TrRmStandardNativeProviderBinding>
            providerBindings,
        out ImmutableArray<TrRmStandardNativeDependencyBinding>
            providerDependencyBindings,
        out ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings,
        out StandardModuleValidation moduleValidation,
        out TrRmA3LayoutResourceLedger? a3LayoutResourceLedger,
        ImmutableArray<CoreExceptionIdentitySelection>
            coreExceptionIdentitySelections = default)
    {
        var usesA3Schema = schemaIdentities.Length != 0
            && StringComparer.Ordinal.Equals(schemaIdentities[0],
                StandardForeignInvocationStorageGenericFixedArraySchemaIdentity);
        if (usesA3Schema != (selectedA3LayoutResourceLedger is not null))
        {
            throw new InvalidOperationException(
                "The selected A3 Standard operation must retain its preflight ledger.");
        }

        a3LayoutResourceLedger = selectedA3LayoutResourceLedger;
        try
        {
            return LoadStandardProgramCore(artifacts, schemaIdentities,
                moduleIdentity, verifySemantics, a3LayoutResourceLedger,
                out providerBindings, out providerDependencyBindings,
                out typeProviderBindings,
                out moduleValidation, coreExceptionIdentitySelections);
        }
        catch (Exception exception)
        {
            a3LayoutResourceLedger?.AdmissionScope.Mark(exception,
                GetA3AdmissionFailureKind(exception));
            throw;
        }
    }

    private static BoundProgram LoadStandardProgramCore(byte[][] artifacts,
        string[] schemaIdentities, string moduleIdentity,
        bool verifySemantics,
        TrRmA3LayoutResourceLedger? a3LayoutResourceLedger,
        out ImmutableArray<TrRmStandardNativeProviderBinding>
            providerBindings,
        out ImmutableArray<TrRmStandardNativeDependencyBinding>
            providerDependencyBindings,
        out ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings,
        out StandardModuleValidation moduleValidation,
        ImmutableArray<CoreExceptionIdentitySelection>
            coreExceptionIdentitySelections = default)
    {
        providerBindings = [];
        providerDependencyBindings = [];
        typeProviderBindings = [];
        var includesRegisterExceptionRegions
            = schemaIdentities.Contains(
                StandardRegisterExceptionRegionsSchemaIdentity,
                StringComparer.Ordinal);
        if (!CoreExceptionIdentitySelectionContract.IsValidTable(
            coreExceptionIdentitySelections))
        {
            throw new ArgumentException(
                "The Core exception identity selections are invalid.",
                nameof(coreExceptionIdentitySelections));
        }
        if (!includesRegisterExceptionRegions
            && !coreExceptionIdentitySelections.IsDefaultOrEmpty)
        {
            throw Unsupported(
                "Core exception identity selections require the exact B+E1 schema.");
        }

        if (includesRegisterExceptionRegions
            && (schemaIdentities.Any(identity =>
                    !StringComparer.Ordinal.Equals(identity,
                        StandardRegisterExceptionRegionsSchemaIdentity))
                || artifacts.Length != 1))
        {
            throw Unsupported(
                "The current B+E1 Standard consumer requires one explicitly selected artifact.");
        }

        if (StringComparer.Ordinal.Equals(schemaIdentities[0],
                StandardLocalAggregateLayoutSchemaIdentity)
            && schemaIdentities.Any(static schemaIdentity =>
                !StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardLocalAggregateLayoutSchemaIdentity)))
        {
            throw Unsupported(
                "An L1 Standard operation requires exact L1 selections for every supplied artifact.");
        }

        moduleValidation = ValidateStandardInputModules(artifacts,
            schemaIdentities, moduleIdentity, a3LayoutResourceLedger);
        var rootModuleRow = moduleValidation.RootModuleRow;
        if (IsExactForeignInvocationSchemaSet(schemaIdentities)
            || moduleValidation.HasNativeOutputProviderImports)
        {
            var closure = ValidateStandardProviderClosureCore(artifacts,
                schemaIdentities, moduleValidation, verifySemantics);
            providerBindings = closure.RootProviderBindings;
            providerDependencyBindings = closure.DependencyBindings;
            return closure.RootProgram;
        }

        var index = CreateForeignInvocationIndex(artifacts[0],
            schemaIdentities[0], a3LayoutResourceLedger);
        var tables = ValidateForeignInvocationArtifact(index,
            schemaIdentities[0], a3LayoutResourceLedger,
            createA3ResourceLedger: false);
        if (StringComparer.Ordinal.Equals(schemaIdentities[0],
                StandardRegisterExceptionRegionsSchemaIdentity))
        {
            if (artifacts.Length != 1 || moduleValidation.Modules.Length != 1
                || rootModuleRow != 1 || tables.Imports.Count != 0
                || tables.ModuleRefs.Count != 0)
            {
                throw Unsupported(
                    "The current B+E1 Standard loader requires one local Module without provider Imports.");
            }

            var requiresCoreExceptionIdentitySelection = false;
            for (var bodyRow = 1; bodyRow <= tables.Bodies.Count; bodyRow++)
            {
                if (tables.Bodies.GetRow(bodyRow)[0] != 0x01)
                {
                    continue;
                }

                if (tables.Bodies.GetRegisterBody(bodyRow,
                        tables.Contracts.Count)
                    .HasCoreExceptionIdentityUse)
                {
                    requiresCoreExceptionIdentitySelection = true;
                    break;
                }
            }

            var selectedCoreExceptionIdentitySelections
                = ValidateStandardCoreExceptionIdentitySelections(
                    coreExceptionIdentitySelections, artifacts,
                    schemaIdentities, moduleValidation.Modules,
                    moduleIdentity,
                    requiresCoreExceptionIdentitySelection,
                    nameof(coreExceptionIdentitySelections));
            return LoadRegisterExceptionRegionsDraft(artifacts[0],
                selectedCoreExceptionIdentitySelections);
        }

        if (schemaIdentities[0] is
            StandardForeignInvocationStorageFixedArraySchemaIdentity
            or StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity
            or StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity
            or StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
            or StandardLocalAggregateLayoutSchemaIdentity
            or StandardForeignInvocationStorageGenericFixedArraySchemaIdentity)
        {
            typeProviderBindings = TrRmFixedArrayProviderBindingValidator
                .Validate(tables, artifacts, schemaIdentities,
                    moduleValidation.Modules, rootModuleRow);
        }

        if (StringComparer.Ordinal.Equals(schemaIdentities[0],
                StandardLocalAggregateLayoutSchemaIdentity)
            && HasLocalAggregateMethodClosureWork(artifacts,
                schemaIdentities, moduleValidation))
        {
            return LoadLocalAggregateLayoutProviderClosure(artifacts,
                schemaIdentities, moduleIdentity, moduleValidation,
                typeProviderBindings);
        }

        if (tables.Imports.Count == 0 && tables.ModuleRefs.Count == 0
            && !StringComparer.Ordinal.Equals(schemaIdentities[0],
                StandardLocalAggregateLayoutSchemaIdentity))
        {
            var loadedProgram = LoadForeignInvocationDraftNativeAddressLibrary(
                artifacts[0], rootModuleRow, null, verifySemantics,
                schema: GetForeignInvocationSchema(schemaIdentities[0]),
                a3LayoutResourceLedger: a3LayoutResourceLedger);
            return RetainLoadedA2SourceTypeFacts(loadedProgram, artifacts,
                schemaIdentities, tables, rootModuleRow,
                typeProviderBindings,
                retainExecutableTypes: !loadedProgram.Functions.IsEmpty);
        }

        var boundTypeImports = typeProviderBindings
            .Where(static binding => binding.ConsumerArtifactIndex == 0)
            .Select(static binding => binding.ImportRow)
            .ToHashSet();
        var methodImportRows = new List<int>();
        var importedMethodRows = new HashSet<int>();
        var moduleRefsUsed = new bool[tables.ModuleRefs.Count + 1];
        for (var importRow = 1; importRow <= tables.Imports.Count;
            importRow++)
        {
            var importedDefinition = tables.Imports
                .GetExpectedDefinitionForImportRow(importRow);
            var definitionKind = importedDefinition >> 24;
            if (definitionKind == 0x13
                && boundTypeImports.Contains(importRow))
            {
                var importModuleRefRow = tables.Imports
                    .GetModuleRefForImportRow(importRow);
                moduleRefsUsed[importModuleRefRow] = true;
                continue;
            }

            if (definitionKind == 0x16)
            {
                var targetMethodRow = checked((int)
                    (importedDefinition & 0x00FF_FFFF));
                if (StringComparer.Ordinal.Equals(schemaIdentities[0],
                        StandardLocalAggregateLayoutSchemaIdentity))
                {
                    throw Invalid(
                        "An L1 MethodDef Import escaped the checked semantic provider-closure path.");
                }

                if (!tables.StandardNativeImports.HasImportForMethod(
                        targetMethodRow)
                    || !importedMethodRows.Add(targetMethodRow))
                {
                    throw Invalid(
                        "A retained MethodDef Import has no unique NativeImport expectation.");
                }

                methodImportRows.Add(importRow);
                var importModuleRefRow = tables.Imports
                    .GetModuleRefForImportRow(importRow);
                if (tables.ModuleRefs.GetOwnerModule(importModuleRefRow)
                    != rootModuleRow)
                {
                    throw Invalid(
                        "A retained MethodDef Import is not owned by the selected root Module.");
                }

                moduleRefsUsed[importModuleRefRow] = true;
                continue;
            }

            throw Unsupported(
                "The bounded F consumer supports A2 array TypeDef Imports and NativeImport MethodDef targets only.");
        }

        if (methodImportRows.Count > 1
            && !StringComparer.Ordinal.Equals(schemaIdentities[0],
                StandardForeignInvocationSchemaIdentity))
        {
            throw Unsupported(
                "Multiple root MethodDef Imports are supported only by the exact F schema.");
        }

        var (rootModuleRefStart, rootModuleRefEnd)
            = tables.Modules.GetModuleRefRange(rootModuleRow);
        for (var rootModuleRefRow = rootModuleRefStart;
            rootModuleRefRow < rootModuleRefEnd; rootModuleRefRow++)
        {
            if (!moduleRefsUsed[rootModuleRefRow])
            {
                throw Invalid(
                    "The selected root Module has an unconsumed ModuleRef.");
            }
        }

        if (methodImportRows.Count == 0)
        {
            var isLocalAggregateLayout
                = StringComparer.Ordinal.Equals(schemaIdentities[0],
                    StandardLocalAggregateLayoutSchemaIdentity);
            var (rootMethodStart, rootMethodEnd)
                = tables.Modules.GetMethodDefRange(rootModuleRow);
            var loadedProgram = isLocalAggregateLayout
                && rootMethodStart != rootMethodEnd
                ? LoadLocalAggregateLayoutExecution(index, tables,
                    rootModuleRow).Program
                : LoadForeignInvocationDraftNativeAddressLibrary(
                    artifacts[0], rootModuleRow, null, verifySemantics,
                    schema: GetForeignInvocationSchema(schemaIdentities[0]),
                    a3LayoutResourceLedger: a3LayoutResourceLedger,
                    typeProviderBindings: typeProviderBindings);
            return RetainLoadedA2SourceTypeFacts(loadedProgram, artifacts,
                schemaIdentities, tables, rootModuleRow,
                typeProviderBindings,
                retainExecutableTypes: !loadedProgram.Functions.IsEmpty);
        }

        var selectedModuleImports = new Dictionary<int, BoundModuleImport>(
            methodImportRows.Count);
        var providerBindingsBuilder
            = ImmutableArray.CreateBuilder<TrRmStandardNativeProviderBinding>(
                methodImportRows.Count);
        foreach (var importRow in methodImportRows)
        {
            var expectedDefinition = tables.Imports
                .GetExpectedDefinitionForImportRow(importRow);
            var targetMethodRow = checked((int)
                (expectedDefinition & 0x00FF_FFFF));
            if (!tables.Imports.TryGetExpectedBinding(expectedDefinition,
                    out var expectedBinding))
            {
                throw Invalid(
                    "A retained NativeImport has no original provider expectation.");
            }

            var moduleRefRow
                = tables.Imports.GetModuleRefForImportRow(importRow);
            var moduleRefIdentity
                = tables.ModuleRefs.GetProducerIdentity(moduleRefRow);
            var (moduleRefTargetRequirement, moduleRefRuntimePolicy)
                = tables.ModuleRefs.GetRequirements(moduleRefRow);
            if (!StringComparer.Ordinal.Equals(moduleRefIdentity,
                    expectedBinding.ProducerIdentity)
                || moduleRefTargetRequirement
                    != expectedBinding.TargetRequirement
                || moduleRefRuntimePolicy != expectedBinding.RuntimePolicy)
            {
                throw Invalid(
                    "A retained ModuleRef and Import context differ.");
            }

            if (StringComparer.Ordinal.Equals(
                    tables.Modules.GetIdentity(rootModuleRow),
                    expectedBinding.ProducerIdentity))
            {
                throw Unsupported(
                    "The bounded F consumer does not permit the selected root Module to satisfy its own Import.");
            }

            var (providerArtifactIndex, providerArtifact,
                providerModuleRow)
                = FindSelectedProviderModule(artifacts,
                    moduleValidation.Modules, rootModuleRow,
                    expectedBinding.ProducerIdentity);
            var (providerMethodRow, providerLibraryName,
                providerExportName)
                = ValidateNativeImportProvider(tables, targetMethodRow,
                    expectedBinding, providerArtifact, providerModuleRow,
                    schemaIdentities[providerArtifactIndex],
                    a3LayoutResourceLedger);
            if (!selectedModuleImports.TryAdd(targetMethodRow,
                    new BoundModuleImport(expectedBinding.ProducerIdentity,
                        expectedBinding.ExportKey,
                        expectedBinding.TargetRequirement,
                        expectedBinding.RuntimePolicy)))
            {
                throw Invalid(
                    "A root NativeImport has more than one selected provider binding.");
            }

            providerBindingsBuilder.Add(
                new TrRmStandardNativeProviderBinding(
                    tables.Methods.GetName(targetMethodRow),
                    expectedBinding.ProducerIdentity, expectedBinding.ExportKey,
                    expectedBinding.TargetRequirement,
                    expectedBinding.RuntimePolicy, rootModuleRow,
                    importRow, moduleRefRow, targetMethodRow,
                    providerArtifactIndex,
                    providerModuleRow, providerMethodRow,
                    providerLibraryName, providerExportName));
        }

        var program = LoadForeignInvocationDraftNativeAddressLibrary(
            artifacts[0], rootModuleRow, selectedModuleImports,
            verifySemantics,
            schema: GetForeignInvocationSchema(schemaIdentities[0]),
            a3LayoutResourceLedger: a3LayoutResourceLedger);
        providerBindings = providerBindingsBuilder.MoveToImmutable();
        return RetainLoadedA2SourceTypeFacts(program, artifacts,
            schemaIdentities, tables, rootModuleRow,
            typeProviderBindings,
            retainExecutableTypes: !program.Functions.IsEmpty);
    }

    private static TrRmA3AdmissionFailureKind GetA3AdmissionFailureKind(
        Exception exception)
        => exception switch
        {
            TrRmResourceLimitException
                => TrRmA3AdmissionFailureKind.ResourceLimit,
            NotSupportedException
                => TrRmA3AdmissionFailureKind.UnsupportedAdmission,
            InvalidDataException
                => TrRmA3AdmissionFailureKind.InvalidArtifact,
            _ => TrRmA3AdmissionFailureKind.None
        };

    private static (int ArtifactIndex, byte[] Artifact, int ModuleRow)
        FindSelectedProviderModule(
        byte[][] artifacts,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        int rootModuleRow,
        string providerIdentity)
    {
        foreach (var module in modules)
        {
            if (module.ArtifactIndex == 0
                && module.ModuleRow == rootModuleRow)
            {
                continue;
            }

            if (StringComparer.Ordinal.Equals(module.Identity,
                    providerIdentity))
            {
                return (module.ArtifactIndex,
                    artifacts[module.ArtifactIndex], module.ModuleRow);
            }
        }

        throw Invalid($"The selected provider Module '{providerIdentity}' was not supplied.");
    }

    private static (int MethodRow, string LibraryName, string ExportName)
        ValidateNativeImportProvider(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
        int expectedMethodRow, TrRmImportBinding expectedBinding,
        byte[] providerArtifact, int providerModuleRow,
        string schemaIdentity,
        TrRmA3LayoutResourceLedger? a3LayoutResourceLedger)
    {
        var providerIndex = CreateForeignInvocationIndex(providerArtifact,
            schemaIdentity, a3LayoutResourceLedger);
        var provider = ValidateForeignInvocationArtifact(providerIndex,
            schemaIdentity, a3LayoutResourceLedger,
            createA3ResourceLedger: false);
        var (providerTargetRequirement, providerRuntimePolicy)
            = provider.Modules.GetRequirements(providerModuleRow);
        var (methodStart, methodEnd)
            = provider.Modules.GetMethodDefRange(providerModuleRow);
        if (!StringComparer.Ordinal.Equals(
                provider.Modules.GetIdentity(providerModuleRow),
                expectedBinding.ProducerIdentity)
            || providerTargetRequirement != expectedBinding.TargetRequirement
            || providerRuntimePolicy != expectedBinding.RuntimePolicy
            || methodEnd - methodStart != 3)
        {
            throw Invalid("The selected provider Module does not match the retained ModuleRef.");
        }

        if (!provider.Exports.TryGetDefinitionForKey(providerModuleRow,
                expectedBinding.ExportKey, out var providerDefinition)
            || (providerDefinition >> 24) != 0x16)
        {
            throw Invalid("The selected provider ExportKey does not name a MethodDef.");
        }

        var providerMethodRow = checked((int)
            (providerDefinition & 0x00FF_FFFF));
        _ = LoadForeignInvocationDraftNativeAddressLibrary(providerArtifact,
            providerModuleRow, null, verifySemantics: false,
            schema: GetForeignInvocationSchema(schemaIdentity),
            a3LayoutResourceLedger: a3LayoutResourceLedger);
        return ValidateNativeImportProvider(consumer, expectedMethodRow,
            expectedBinding, provider, providerModuleRow, providerMethodRow);
    }

    private static (int MethodRow, string LibraryName, string ExportName)
        ValidateNativeImportProvider(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
        int expectedMethodRow, TrRmImportBinding expectedBinding,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables provider,
        int providerModuleRow, int providerMethodRow)
    {
        var (providerTargetRequirement, providerRuntimePolicy)
            = provider.Modules.GetRequirements(providerModuleRow);
        var (methodStart, methodEnd)
            = provider.Modules.GetMethodDefRange(providerModuleRow);
        var providerDefinition = 0x1600_0000u | (uint)providerMethodRow;
        if (!StringComparer.Ordinal.Equals(
                provider.Modules.GetIdentity(providerModuleRow),
                expectedBinding.ProducerIdentity)
            || providerTargetRequirement != expectedBinding.TargetRequirement
            || providerRuntimePolicy != expectedBinding.RuntimePolicy
            || providerMethodRow < methodStart || providerMethodRow >= methodEnd
            || !provider.Exports.TryGetDefinitionForKey(providerModuleRow,
                expectedBinding.ExportKey, out var exportedDefinition)
            || exportedDefinition != providerDefinition
            || provider.Imports.IsExpectedDefinition(providerDefinition)
            || provider.Methods.GetOwnerType(providerMethodRow) != 0
            || provider.Methods.GetOwnerModule(providerMethodRow)
                != providerModuleRow
            || provider.Methods.GetFlags(providerMethodRow) != 2
            || provider.Methods.GetBodyRow(providerMethodRow) != 0
            || !provider.StandardNativeImports.HasImportForMethod(providerMethodRow))
        {
            throw Invalid("The selected export is not a provider-owned NativeImport.");
        }

        var expectedSignatureRow = consumer.Methods.GetSignature(expectedMethodRow);
        var providerSignatureRow = provider.Methods.GetSignature(providerMethodRow);
        var (expectedParameterFirst, expectedParameterCount)
            = consumer.Signatures.GetParamRange(expectedSignatureRow);
        var (providerParameterFirst, providerParameterCount)
            = provider.Signatures.GetParamRange(providerSignatureRow);
        var consumerOutputStates
            = consumer.Contracts.GetOutputStateContracts(expectedMethodRow);
        var providerOutputStates
            = provider.Contracts.GetOutputStateContracts(providerMethodRow);
        var hasNativeOutputContracts
            = !consumerOutputStates.IsEmpty || !providerOutputStates.IsEmpty;
        if (hasNativeOutputContracts
            && (consumerOutputStates.IsEmpty || providerOutputStates.IsEmpty))
        {
            throw Invalid(
                "The selected provider does not preserve the original NativeOutput states.");
        }

        if (hasNativeOutputContracts
            && (!consumer.Contracts.GetNativeParameters(expectedMethodRow).IsEmpty
                || !provider.Contracts.GetNativeParameters(providerMethodRow).IsEmpty))
        {
            throw Unsupported(
                "Standard NativeOutput provider binding does not select NativeParameter contracts.");
        }

        var consumerContractCount = consumer.Contracts
            .GetNativeParameters(expectedMethodRow).Length + 1;
        var providerContractCount = provider.Contracts
            .GetNativeParameters(providerMethodRow).Length + 1;
        if (hasNativeOutputContracts)
        {
            consumerContractCount = checked(consumerContractCount
                + consumerOutputStates.Length
                + consumer.Contracts.GetPredicateGraphContracts(
                    expectedMethodRow).Length
                + consumer.Contracts.GetDependencyContracts(
                    expectedMethodRow).Length);
            providerContractCount = checked(providerContractCount
                + providerOutputStates.Length
                + provider.Contracts.GetPredicateGraphContracts(
                    providerMethodRow).Length
                + provider.Contracts.GetDependencyContracts(
                    providerMethodRow).Length);
        }

        EnsureNativeAddressContracts(provider.Methods, provider.Contracts,
            providerMethodRow, providerContractCount, allowEffects: true,
            allowNativeOutputContracts: hasNativeOutputContracts);
        EnsureNativeAddressContracts(consumer.Methods, consumer.Contracts,
            expectedMethodRow, consumerContractCount, allowEffects: true,
            allowNativeOutputContracts: hasNativeOutputContracts);

        var expectedResultTypeSpec
            = consumer.Signatures.GetResultTypeSpec(expectedSignatureRow);
        var providerResultTypeSpec
            = provider.Signatures.GetResultTypeSpec(providerSignatureRow);
        if (hasNativeOutputContracts
            && (!IsSupportedNativeOutputProviderType(consumer.TypeSpecs,
                    expectedResultTypeSpec)
                || !IsSupportedNativeOutputProviderType(provider.TypeSpecs,
                    providerResultTypeSpec)))
        {
            throw Unsupported(
                "The Standard NativeOutput provider signature contains a type form without an admitted F identity mapping.");
        }

        if (hasNativeOutputContracts)
        {
            for (var ordinal = 0; ordinal < expectedParameterCount; ordinal++)
            {
                if (!IsSupportedNativeOutputProviderType(consumer.TypeSpecs,
                        consumer.Parameters.GetTypeSpec(
                            expectedParameterFirst + ordinal))
                    || !IsSupportedNativeOutputProviderType(provider.TypeSpecs,
                        provider.Parameters.GetTypeSpec(
                            providerParameterFirst + ordinal)))
                {
                    throw Unsupported(
                        "The Standard NativeOutput provider signature contains a type form without an admitted F identity mapping.");
                }
            }
        }

        var resultTypesEquivalent = hasNativeOutputContracts
            ? AreEquivalentNativeOutputProviderTypeSpecs(consumer.TypeSpecs,
                expectedResultTypeSpec, provider.TypeSpecs,
                providerResultTypeSpec)
            : AreEquivalentProviderTypeSpecs(consumer.TypeSpecs,
                expectedResultTypeSpec, provider.TypeSpecs,
                providerResultTypeSpec);
        var outputContractsEquivalent = hasNativeOutputContracts
            ? consumer.NativeFunctionPointerAnchors
                .AreEquivalentAnchorContracts(expectedMethodRow,
                    provider.NativeFunctionPointerAnchors, providerMethodRow)
            : consumer.Contracts.GetNativeParameters(expectedMethodRow)
                .SequenceEqual(
                    provider.Contracts.GetNativeParameters(providerMethodRow));
        if (consumer.Signatures.GetReceiverTypeSpec(expectedSignatureRow) != 0
            || provider.Signatures.GetReceiverTypeSpec(providerSignatureRow) != 0
            || consumer.Signatures.GetGenericCount(expectedSignatureRow) != 0
            || provider.Signatures.GetGenericCount(providerSignatureRow) != 0
            || expectedParameterCount != providerParameterCount
            || !resultTypesEquivalent
            || consumer.Contracts.GetEffectsMask(expectedMethodRow) != 4
            || provider.Contracts.GetEffectsMask(providerMethodRow) != 4
            || !consumer.Contracts.GetNativeOutputs(expectedMethodRow).IsEmpty
            || !provider.Contracts.GetNativeOutputs(providerMethodRow).IsEmpty
            || !outputContractsEquivalent)
        {
            throw Invalid("The selected provider does not satisfy the original NativeImport signature.");
        }

        for (var ordinal = 0; ordinal < expectedParameterCount; ordinal++)
        {
            var expectedParameterRow = expectedParameterFirst + ordinal;
            var providerParameterRow = providerParameterFirst + ordinal;
            if (consumer.Parameters.GetMode(expectedParameterRow)
                    != provider.Parameters.GetMode(providerParameterRow)
                || !(hasNativeOutputContracts
                    ? AreEquivalentNativeOutputProviderTypeSpecs(
                        consumer.TypeSpecs,
                        consumer.Parameters.GetTypeSpec(expectedParameterRow),
                        provider.TypeSpecs,
                        provider.Parameters.GetTypeSpec(providerParameterRow))
                    : AreEquivalentProviderTypeSpecs(consumer.TypeSpecs,
                        consumer.Parameters.GetTypeSpec(expectedParameterRow),
                        provider.TypeSpecs,
                        provider.Parameters.GetTypeSpec(providerParameterRow))))
            {
                throw Invalid("The selected provider has an incompatible NativeImport formal.");
            }
        }

        var expectedImport
            = consumer.StandardNativeImports.GetImportForMethod(expectedMethodRow);
        var providerImport
            = provider.StandardNativeImports.GetImportForMethod(providerMethodRow);
        var providerLibraryName
            = provider.StandardNativeImports.GetLibraryName(providerImport);
        var providerExportName
            = provider.StandardNativeImports.GetEntryName(providerImport);
        if (expectedImport.OwnerKind != providerImport.OwnerKind
            || expectedImport.ResolutionMode != providerImport.ResolutionMode
            || expectedImport.ResolutionPolicy != providerImport.ResolutionPolicy
            || expectedImport.CallConv != providerImport.CallConv
            || !StringComparer.Ordinal.Equals(
                consumer.StandardNativeImports.GetLibraryName(expectedImport),
                providerLibraryName)
            || !StringComparer.Ordinal.Equals(
                consumer.StandardNativeImports.GetEntryName(expectedImport),
                providerExportName))
        {
            throw Invalid("The selected provider does not satisfy the original NativeImport descriptor.");
        }

        return (providerMethodRow, providerLibraryName, providerExportName);
    }

    private static bool IsSupportedNativeOutputProviderType(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef)
    {
        var maximumDepth = typeSpecs.Count + 1;
        var allowVoid = false;
        for (var depth = 0; depth < maximumDepth; depth++)
        {
            var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
            if (type.Form == 0x01 && type.TypeCode < 0x80)
            {
                return type.Qualifiers == 0
                    && IsNativeOutputProviderAtomic(type.TypeCode, allowVoid);
            }

            if (type.TypeCode != 0x8C || type.Qualifiers is not (0 or 1))
            {
                return false;
            }

            if (type.Form == 0x01)
            {
                return IsNativeOutputProviderAtomic(type.ArgumentCode,
                    allowVoid: true);
            }

            if (type.Form != 0x0C || !type.IsRow)
            {
                return false;
            }

            typeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(
                typeSpecs.GetRow(type.Row)[3..]);
            allowVoid = true;
        }

        return false;
    }

    private static bool IsNativeOutputProviderAtomic(byte typeCode,
        bool allowVoid)
    {
        var typeIndex = Array.IndexOf(s_codes, typeCode);
        if (typeIndex < 0)
        {
            return false;
        }

        var type = s_types[typeIndex];
        return type == PrimitiveType.Void
            ? allowVoid : DirectNativeImportContract.IsScalar(type);
    }

    private static bool AreEquivalentNativeOutputProviderTypeSpecs(
        TrRmTypeSpecTableReader leftTypeSpecs, uint leftTypeSpecRef,
        TrRmTypeSpecTableReader rightTypeSpecs, uint rightTypeSpecRef)
    {
        var maximumDepth = Math.Max(leftTypeSpecs.Count,
            rightTypeSpecs.Count) + 1;
        for (var depth = 0; depth < maximumDepth; depth++)
        {
            var left = leftTypeSpecs.GetTypeSpecRef(leftTypeSpecRef);
            var right = rightTypeSpecs.GetTypeSpecRef(rightTypeSpecRef);
            if (left.Qualifiers != right.Qualifiers)
            {
                return false;
            }

            if (left.TypeCode == 0x8C || right.TypeCode == 0x8C)
            {
                if (left.TypeCode != 0x8C || right.TypeCode != 0x8C
                    || !TryGetNativeOutputProviderPointee(leftTypeSpecs,
                        left, out leftTypeSpecRef)
                    || !TryGetNativeOutputProviderPointee(rightTypeSpecs,
                        right, out rightTypeSpecRef))
                {
                    return false;
                }

                continue;
            }

            return left.Form == 0x01 && right.Form == 0x01
                && left.TypeCode == right.TypeCode
                && left.TypeCode < 0x80
                && left.Qualifiers == 0;
        }

        return false;
    }

    private static bool TryGetNativeOutputProviderPointee(
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeSpecRefInfo type,
        out uint pointeeTypeSpecRef)
    {
        if (type.Form == 0x01 && type.TypeCode == 0x8C)
        {
            pointeeTypeSpecRef = 0xF200_0000u | type.ArgumentCode;
            return true;
        }

        if (type.Form == 0x0C && type.TypeCode == 0x8C && type.IsRow)
        {
            pointeeTypeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(
                typeSpecs.GetRow(type.Row)[3..]);
            return true;
        }

        pointeeTypeSpecRef = 0;
        return false;
    }

    private static bool AreEquivalentProviderTypeSpecs(
        TrRmTypeSpecTableReader leftTypeSpecs, uint leftTypeSpecRef,
        TrRmTypeSpecTableReader rightTypeSpecs, uint rightTypeSpecRef)
    {
        var maximumDepth = Math.Max(leftTypeSpecs.Count,
            rightTypeSpecs.Count) + 1;
        for (var depth = 0; depth < maximumDepth; depth++)
        {
            var left = leftTypeSpecs.GetTypeSpecRef(leftTypeSpecRef);
            var right = rightTypeSpecs.GetTypeSpecRef(rightTypeSpecRef);
            if (left.Form != right.Form
                || left.Qualifiers != right.Qualifiers
                || left.TypeCode != right.TypeCode)
            {
                return false;
            }

            if (left.IsRow && right.IsRow
                && left.Form == 0x0C && left.TypeCode == 0x8C)
            {
                var leftRow = leftTypeSpecs.GetRow(left.Row);
                var rightRow = rightTypeSpecs.GetRow(right.Row);
                leftTypeSpecRef
                    = BinaryPrimitives.ReadUInt32LittleEndian(
                        leftRow[3..]);
                rightTypeSpecRef
                    = BinaryPrimitives.ReadUInt32LittleEndian(
                        rightRow[3..]);
                continue;
            }

            return leftTypeSpecs.GetTypeSpecBytes(leftTypeSpecRef)
                .SequenceEqual(rightTypeSpecs.GetTypeSpecBytes(
                    rightTypeSpecRef));
        }

        return false;
    }

    private readonly record struct StandardProviderImportEdge(
        int ConsumerArtifactIndex,
        int ConsumerModuleRow,
        string ConsumerModuleIdentity,
        int ImportRow,
        int ModuleRefRow,
        int ExpectedMethodRow,
        string ExpectedMethodName,
        TrRmImportBinding ExpectedBinding,
        int ProviderArtifactIndex,
        int ProviderModuleRow);

    internal static TrRmStandardNativeProviderClosureValidation
        ValidateStandardProviderClosure(byte[][] artifacts,
                string[] schemaIdentities, string moduleIdentity,
                bool verifyRootSemantics)
        {
            ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
            var moduleValidation = ValidateStandardInputModules(artifacts,
                schemaIdentities, moduleIdentity, a3LayoutResourceLedger: null);
        return ValidateStandardProviderClosureCore(artifacts,
            schemaIdentities, moduleValidation, verifyRootSemantics);
    }

    internal static bool IsExactForeignInvocationSchemaSet(
        IReadOnlyList<string> schemaIdentities)
        => schemaIdentities.Count != 0
            && schemaIdentities.All(static schemaIdentity =>
                StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardForeignInvocationSchemaIdentity));

    internal static bool IsNativeOutputProviderSchemaSet(
        string[] schemaIdentities)
        => schemaIdentities.Length != 0
            && schemaIdentities.All(static schemaIdentity =>
                StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardForeignInvocationSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardForeignInvocationStorageAddressSchemaIdentity));

    internal static bool IsNativeOutputProviderSchemaSet(
        ImmutableArray<string> schemaIdentities)
    {
        if (schemaIdentities.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var schemaIdentity in schemaIdentities)
        {
            if (!StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardForeignInvocationSchemaIdentity)
                && !StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardForeignInvocationStorageAddressSchemaIdentity))
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableHashSet<string>
        GetNativeOutputProviderImportIdentities(byte[] artifact,
            string schemaIdentity, string rootModuleIdentity)
    {
        var index = CreateForeignInvocationIndex(artifact, schemaIdentity);
        var tables = ValidateForeignInvocationArtifact(index, schemaIdentity,
            createA3ResourceLedger: false);
        var rootModuleRow = 0;
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            if (StringComparer.Ordinal.Equals(
                    tables.Modules.GetIdentity(moduleRow),
                    rootModuleIdentity))
            {
                rootModuleRow = moduleRow;
                break;
            }
        }

        if (rootModuleRow == 0)
        {
            return ImmutableHashSet<string>.Empty.WithComparer(
                StringComparer.Ordinal);
        }

        var identities = ImmutableHashSet.CreateBuilder<string>(
            StringComparer.Ordinal);
        for (var importRow = 1; importRow <= tables.Imports.Count;
            importRow++)
        {
            var moduleRefRow
                = tables.Imports.GetModuleRefForImportRow(importRow);
            if (tables.ModuleRefs.GetOwnerModule(moduleRefRow)
                    != rootModuleRow
                || (tables.Imports.GetExpectedDefinitionForImportRow(
                        importRow) >> 24) != 0x16)
            {
                continue;
            }

            var methodRow = checked((int)
                (tables.Imports.GetExpectedDefinitionForImportRow(importRow)
                    & 0x00FF_FFFF));
            if (!tables.Contracts.GetOutputStateContracts(methodRow).IsEmpty)
            {
                _ = identities.Add(
                    tables.ModuleRefs.GetProducerIdentity(moduleRefRow));
            }
        }

        return identities.ToImmutable();
    }

    private static void ValidateNativeOutputProviderTopology(
        string[] schemaIdentities,
        StandardModuleValidation moduleValidation,
        Dictionary<(int ConsumerArtifactIndex, int ProviderArtifactIndex),
            List<StandardProviderImportEdge>> edgePairs,
        TrRmStandardModuleSelection root)
    {
        if (!StringComparer.Ordinal.Equals(
                schemaIdentities[root.ArtifactIndex],
                StandardForeignInvocationStorageAddressSchemaIdentity))
        {
            throw Unsupported(
                "A mixed F/F+S provider selection requires an F+S consumer and an F provider.");
        }

        var providers = new HashSet<(int ArtifactIndex, int ModuleRow)>();
        foreach (var pair in edgePairs)
        {
            foreach (var edge in pair.Value)
            {
                if (edge.ConsumerArtifactIndex != root.ArtifactIndex
                    || edge.ConsumerModuleRow != root.ModuleRow)
                {
                    throw Unsupported(
                        "F+S NativeOutput binding does not admit provider dependency graphs.");
                }

                _ = providers.Add((edge.ProviderArtifactIndex,
                    edge.ProviderModuleRow));
            }
        }

        if (providers.Count != 1)
        {
            throw Unsupported(
                "F+S NativeOutput binding admits one selected provider Module.");
        }

        var selectedProvider = providers.Single();
        foreach (var module in moduleValidation.Modules)
        {
            if (module.ArtifactIndex == root.ArtifactIndex
                && module.ModuleRow == root.ModuleRow)
            {
                continue;
            }

            if ((module.ArtifactIndex, module.ModuleRow) != selectedProvider)
            {
                throw Unsupported(
                    "F+S NativeOutput binding does not admit unrelated selected Modules.");
            }

            var providerSchema = schemaIdentities[module.ArtifactIndex];
            if (!StringComparer.Ordinal.Equals(providerSchema,
                    schemaIdentities[root.ArtifactIndex])
                && !StringComparer.Ordinal.Equals(providerSchema,
                    StandardForeignInvocationSchemaIdentity))
            {
                throw Unsupported(
                    "The mixed F/F+S selection does not use an F provider.");
            }
        }
    }

    private static TrRmStandardNativeProviderClosureValidation
        ValidateStandardProviderClosureCore(byte[][] artifacts,
            string[] schemaIdentities,
            StandardModuleValidation moduleValidation,
            bool verifyRootSemantics)
    {
        if ((!IsExactForeignInvocationSchemaSet(schemaIdentities)
                && !moduleValidation.HasNativeOutputProviderImports)
            || !IsNativeOutputProviderSchemaSet(schemaIdentities)
            || artifacts.Length != schemaIdentities.Length)
        {
            throw Unsupported(
                "The provider closure requires exact F selections or a selected F/F+S NativeOutput edge.");
        }

        var modulesByIdentity
            = new Dictionary<string, TrRmStandardModuleSelection>(
                moduleValidation.Modules.Length, StringComparer.Ordinal);
        var modulesByArtifact
            = new List<TrRmStandardModuleSelection>[artifacts.Length];
        for (var artifactIndex = 0; artifactIndex < modulesByArtifact.Length;
            artifactIndex++)
        {
            modulesByArtifact[artifactIndex] = [];
        }

        TrRmStandardModuleSelection? rootSelection = null;
        foreach (var module in moduleValidation.Modules)
        {
            if ((uint)module.ArtifactIndex >= (uint)artifacts.Length
                || !modulesByIdentity.TryAdd(module.Identity, module))
            {
                throw Invalid(
                    "The provider closure has a duplicate or invalid Module selection.");
            }

            modulesByArtifact[module.ArtifactIndex].Add(module);
            if (module.ArtifactIndex == 0
                && module.ModuleRow == moduleValidation.RootModuleRow)
            {
                rootSelection = module;
            }
        }

        if (rootSelection is not TrRmStandardModuleSelection selectedRoot)
        {
            throw Invalid("The provider closure lost its selected root Module.");
        }

        var edgePairs
            = new Dictionary<(int ConsumerArtifactIndex,
                int ProviderArtifactIndex), List<StandardProviderImportEdge>>();
        var ownedNativeImportsByModule
            = new Dictionary<(int ArtifactIndex, int ModuleRow), int>();
        var rootModuleImports = (Dictionary<int, BoundModuleImport>?)null;
        BoundProgram? rootProgram = null;

        for (var artifactIndex = 0; artifactIndex < artifacts.Length;
            artifactIndex++)
        {
            var index = CreateForeignInvocationIndex(artifacts[artifactIndex],
                schemaIdentities[artifactIndex]);
            var tables = ValidateForeignInvocationArtifact(index,
                schemaIdentities[artifactIndex],
                createA3ResourceLedger: false);
            var hasNativeOutputBody = false;
            foreach (var module in modulesByArtifact[artifactIndex])
            {
                hasNativeOutputBody |= HasNativeOutputReservationBody(tables,
                    module.ModuleRow);
            }
            var artifactSchema = GetForeignInvocationSchema(
                schemaIdentities[artifactIndex]);
            if (!hasNativeOutputBody)
            {
                ValidateNativeAddressArtifactSections(index, tables,
                    artifactSchema);
                ValidateNativeAddressArtifactTables(tables);
            }
            var moduleRefsUsed = new bool[tables.ModuleRefs.Count + 1];
            var edgesByModule
                = new Dictionary<int, List<StandardProviderImportEdge>>();
            foreach (var module in modulesByArtifact[artifactIndex])
            {
                edgesByModule.Add(module.ModuleRow, []);
            }

            for (var importRow = 1; importRow <= tables.Imports.Count;
                importRow++)
            {
                var moduleRefRow
                    = tables.Imports.GetModuleRefForImportRow(importRow);
                var expectedDefinition
                    = tables.Imports.GetExpectedDefinitionForImportRow(
                        importRow);
                if ((expectedDefinition >> 24) != 0x16)
                {
                    throw Unsupported(
                        "The exact-F provider closure supports MethodDef Imports only.");
                }

                var expectedMethodRow = checked((int)
                    (expectedDefinition & 0x00FF_FFFF));
                var consumerModuleRow
                    = tables.ModuleRefs.GetOwnerModule(moduleRefRow);
                if (!edgesByModule.TryGetValue(consumerModuleRow,
                        out var moduleEdges)
                    || !tables.StandardNativeImports.HasImportForMethod(
                        expectedMethodRow)
                    || !tables.Imports.TryGetExpectedBinding(
                        expectedDefinition, out var expectedBinding))
                {
                    throw Invalid(
                        "A provider closure Import has no owned NativeImport expectation.");
                }

                var moduleRefIdentity
                    = tables.ModuleRefs.GetProducerIdentity(moduleRefRow);
                var (moduleRefTargetRequirement, moduleRefRuntimePolicy)
                    = tables.ModuleRefs.GetRequirements(moduleRefRow);
                if (!StringComparer.Ordinal.Equals(moduleRefIdentity,
                        expectedBinding.ProducerIdentity)
                    || moduleRefTargetRequirement
                        != expectedBinding.TargetRequirement
                    || moduleRefRuntimePolicy != expectedBinding.RuntimePolicy
                    || !modulesByIdentity.TryGetValue(
                        expectedBinding.ProducerIdentity,
                        out var providerModule)
                    || providerModule.TargetRequirement
                        != expectedBinding.TargetRequirement
                    || providerModule.RuntimePolicy
                        != expectedBinding.RuntimePolicy)
                {
                    throw Invalid(
                        "A provider closure ModuleRef does not select its retained producer requirements.");
                }

                moduleRefsUsed[moduleRefRow] = true;
                moduleEdges.Add(new StandardProviderImportEdge(
                    artifactIndex, consumerModuleRow, moduleRefIdentity,
                    importRow, moduleRefRow, expectedMethodRow,
                    tables.Methods.GetName(expectedMethodRow),
                    expectedBinding, providerModule.ArtifactIndex,
                    providerModule.ModuleRow));
            }

            foreach (var module in modulesByArtifact[artifactIndex])
            {
                var (moduleRefStart, moduleRefEnd)
                    = tables.Modules.GetModuleRefRange(module.ModuleRow);
                var (typeStart, typeEnd)
                    = tables.Modules.GetTypeDefRange(module.ModuleRow);
                var (sourceFileStart, sourceFileEnd)
                    = tables.Modules.GetSourceFileRange(module.ModuleRow);
                var moduleEdges = edgesByModule[module.ModuleRow];
                var moduleRefCount = moduleRefEnd - moduleRefStart;
                if ((moduleEdges.Count == 0 && moduleRefCount != 0)
                    || (moduleEdges.Count != 0
                        && (moduleRefCount == 0
                            || moduleRefCount > moduleEdges.Count)))
                {
                    throw Invalid(
                        "A provider Module has an unconsumed or inconsistent ModuleRef set.");
                }

                var moduleHasNativeOutputBody
                    = HasNativeOutputReservationBody(
                    tables, module.ModuleRow);
                if ((typeStart != typeEnd && !moduleHasNativeOutputBody)
                    || sourceFileStart != sourceFileEnd)
                {
                    throw Unsupported(
                        "The exact-F provider closure does not admit these TypeDef or SourceFile declarations.");
                }

                for (var moduleRefRow = moduleRefStart;
                    moduleRefRow < moduleRefEnd; moduleRefRow++)
                {
                    if (!moduleRefsUsed[moduleRefRow])
                    {
                        throw Invalid(
                            "A provider Module has an unconsumed ModuleRef.");
                    }
                }

                Dictionary<int, BoundModuleImport>? selectedModuleImports
                    = null;
                var importedMethodRows
                    = new HashSet<int>(moduleEdges.Select(static edge =>
                        edge.ExpectedMethodRow));
                var ownedNativeImportCount = 0;
                for (var methodRow = module.MethodDefStart;
                    methodRow < module.MethodDefEnd; methodRow++)
                {
                    if (tables.StandardNativeImports.HasImportForMethod(
                            methodRow)
                        && !importedMethodRows.Contains(methodRow))
                    {
                        ownedNativeImportCount++;
                    }
                }

                ownedNativeImportsByModule.Add(
                    (artifactIndex, module.ModuleRow),
                    ownedNativeImportCount);
                if (moduleEdges.Count != 0)
                {
                    selectedModuleImports
                        = new Dictionary<int, BoundModuleImport>(
                            moduleEdges.Count);
                    foreach (var edge in moduleEdges)
                    {
                        if (!selectedModuleImports.TryAdd(
                                edge.ExpectedMethodRow,
                                new BoundModuleImport(
                                    edge.ExpectedBinding.ProducerIdentity,
                                    edge.ExpectedBinding.ExportKey,
                                    edge.ExpectedBinding.TargetRequirement,
                                    edge.ExpectedBinding.RuntimePolicy)))
                        {
                            throw Invalid(
                                "A provider Module has more than one Import for a MethodDef.");
                        }
                    }
                }

                foreach (var edge in moduleEdges)
                {
                    var key = (edge.ConsumerArtifactIndex,
                        edge.ProviderArtifactIndex);
                    if (!edgePairs.TryGetValue(key, out var pairEdges))
                    {
                        pairEdges = [];
                        edgePairs.Add(key, pairEdges);
                    }

                    pairEdges.Add(edge);
                }

                var isRoot = artifactIndex == selectedRoot.ArtifactIndex
                    && module.ModuleRow == selectedRoot.ModuleRow;
                if (!isRoot
                    && IsStandaloneNativeOutputProviderModule(tables, module))
                {
                    continue;
                }

                if (module.MethodDefStart == module.MethodDefEnd)
                {
                    if (isRoot)
                    {
                        throw Unsupported(
                            "The selected exact-F root Module has no bounded NativeAddress caller.");
                    }

                    continue;
                }

                var program = LoadForeignInvocationDraftNativeAddressLibrary(
                    index, tables, module.ModuleRow,
                    selectedModuleImports,
                    verifySemantics: isRoot && verifyRootSemantics,
                    schema: artifactSchema,
                    a3LayoutResourceLedger: null,
                    typeProviderBindings: default);
                if (isRoot)
                {
                    rootProgram = program;
                    rootModuleImports = selectedModuleImports;
                }
            }
        }

        if (schemaIdentities.Any(static schemaIdentity =>
                StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardForeignInvocationStorageAddressSchemaIdentity)))
        {
            ValidateNativeOutputProviderTopology(schemaIdentities,
                moduleValidation, edgePairs,
                selectedRoot);
        }

        var dependencyBindings
            = ImmutableArray.CreateBuilder<
                TrRmStandardNativeDependencyBinding>();
        var rootProviderBindings
            = ImmutableArray.CreateBuilder<
                TrRmStandardNativeProviderBinding>();
        foreach (var pair in edgePairs.OrderBy(static pair =>
                pair.Key.ConsumerArtifactIndex).ThenBy(static pair =>
                pair.Key.ProviderArtifactIndex))
        {
            var consumerIndex = CreateForeignInvocationIndex(
                artifacts[pair.Key.ConsumerArtifactIndex],
                schemaIdentities[pair.Key.ConsumerArtifactIndex]);
            var consumer = ValidateForeignInvocationArtifact(consumerIndex,
                schemaIdentities[pair.Key.ConsumerArtifactIndex],
                createA3ResourceLedger: false);
            var provider = pair.Key.ConsumerArtifactIndex
                    == pair.Key.ProviderArtifactIndex
                ? consumer
                : ValidateStandardProviderArtifact(
                    artifacts[pair.Key.ProviderArtifactIndex],
                    schemaIdentities[pair.Key.ProviderArtifactIndex]);

            foreach (var edge in pair.Value)
            {
                if (edge.ConsumerArtifactIndex
                        == edge.ProviderArtifactIndex
                    && edge.ConsumerModuleRow == edge.ProviderModuleRow
                    && ownedNativeImportsByModule[(
                        edge.ProviderArtifactIndex, edge.ProviderModuleRow)] == 0)
                {
                    throw Unsupported(
                        "An imported-only Module cannot satisfy its own Import.");
                }

                if (!provider.Exports.TryGetDefinitionForKey(
                        edge.ProviderModuleRow, edge.ExpectedBinding.ExportKey,
                        out var providerDefinition)
                    || (providerDefinition >> 24) != 0x16)
                {
                    throw Invalid(
                        "A provider closure ExportKey does not select a public local MethodDef.");
                }

                var providerMethodRow = checked((int)
                    (providerDefinition & 0x00FF_FFFF));
                var (actualMethodRow, libraryName, exportName)
                    = ValidateNativeImportProvider(consumer,
                        edge.ExpectedMethodRow, edge.ExpectedBinding, provider,
                        edge.ProviderModuleRow, providerMethodRow);
                dependencyBindings.Add(
                    new TrRmStandardNativeDependencyBinding(
                        edge.ConsumerArtifactIndex, edge.ConsumerModuleRow,
                        edge.ConsumerModuleIdentity, edge.ImportRow,
                        edge.ModuleRefRow, edge.ExpectedMethodRow,
                        edge.ExpectedBinding.ProducerIdentity,
                        edge.ExpectedBinding.ExportKey,
                        edge.ExpectedBinding.TargetRequirement,
                        edge.ExpectedBinding.RuntimePolicy,
                        edge.ProviderArtifactIndex, edge.ProviderModuleRow,
                        actualMethodRow, libraryName, exportName));
                if (edge.ConsumerArtifactIndex == selectedRoot.ArtifactIndex
                    && edge.ConsumerModuleRow == selectedRoot.ModuleRow)
                {
                    rootProviderBindings.Add(
                        new TrRmStandardNativeProviderBinding(
                            edge.ExpectedMethodName,
                            edge.ExpectedBinding.ProducerIdentity,
                            edge.ExpectedBinding.ExportKey,
                            edge.ExpectedBinding.TargetRequirement,
                            edge.ExpectedBinding.RuntimePolicy,
                            edge.ConsumerModuleRow, edge.ImportRow,
                            edge.ModuleRefRow, edge.ExpectedMethodRow,
                            edge.ProviderArtifactIndex, edge.ProviderModuleRow,
                            actualMethodRow, libraryName, exportName));
                }
            }
        }

        if (rootProgram is null)
        {
            throw Invalid("The provider closure did not reconstruct its selected root.");
        }

        var orderedDependencies = dependencyBindings
            .OrderBy(static binding => binding.ConsumerArtifactIndex)
            .ThenBy(static binding => binding.ConsumerModuleRow)
            .ThenBy(static binding => binding.ConsumerImportRow)
            .ToImmutableArray();
        var orderedRootBindings = rootProviderBindings
            .OrderBy(static binding => binding.RootImportRow)
            .ToImmutableArray();
        return new TrRmStandardNativeProviderClosureValidation(rootProgram,
            moduleValidation.RootModuleRow, moduleValidation.Modules,
            orderedRootBindings, orderedDependencies);
    }

    private static bool IsStandaloneNativeOutputProviderModule(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        TrRmStandardModuleSelection module)
    {
        if (module.MethodDefEnd == module.MethodDefStart
            || tables.Modules.GetEntryMethod(module.ModuleRow) != 0)
        {
            return false;
        }

        var hasExportedNativeOutput = false;
        for (var methodRow = module.MethodDefStart;
            methodRow < module.MethodDefEnd; methodRow++)
        {
            var visibility = tables.Methods.GetFlags(methodRow);
            if (tables.Methods.GetOwnerType(methodRow) != 0
                || tables.Methods.GetOwnerModule(methodRow) != module.ModuleRow
                || visibility is not (1 or 2)
                || tables.Methods.GetBodyRow(methodRow) != 0
                || !tables.StandardNativeImports.HasImportForMethod(methodRow)
                || !tables.Contracts.GetNativeParameters(methodRow).IsEmpty
                || !tables.Contracts.GetNativeOutputs(methodRow).IsEmpty)
            {
                return false;
            }

            if (visibility == 2
                && tables.Exports.HasExportForMethod(methodRow)
                && !tables.Contracts.GetOutputStateContracts(methodRow).IsEmpty)
            {
                hasExportedNativeOutput = true;
            }
        }

        return hasExportedNativeOutput;
    }

    private static TrRmStructuralMetadataValidator.ValidatedExecutionTables
        ValidateStandardProviderArtifact(byte[] artifact,
            string schemaIdentity)
    {
        var index = CreateForeignInvocationIndex(artifact, schemaIdentity);
        return ValidateForeignInvocationArtifact(index, schemaIdentity,
            createA3ResourceLedger: false);
    }

    private static ImmutableArray<CoreExceptionIdentitySelection>
        ValidateStandardCoreExceptionIdentitySelections(
            ImmutableArray<CoreExceptionIdentitySelection> selections,
            byte[][] artifacts, string[] schemaIdentities,
            ImmutableArray<TrRmStandardModuleSelection> modules,
            string rootModuleIdentity, bool requiresSelection,
            string parameterName)
    {
        if (!CoreExceptionIdentitySelectionContract.IsValidTable(selections))
        {
            throw new ArgumentException(
                "The Core exception identity selections are invalid.",
                parameterName);
        }

        if (requiresSelection
            && !selections.Any(selection =>
                StringComparer.Ordinal.Equals(
                    selection.UsingModuleIdentity, rootModuleIdentity)))
        {
            throw new ArgumentException(
                "The selected B+E1 Module uses exception identity records but has no Core exception selection.",
                parameterName);
        }

        if (selections.IsDefaultOrEmpty)
        {
            return [];
        }

        foreach (var selection in selections)
        {
            var usingModule = FindStandardModuleSelection(modules,
                selection.UsingModuleIdentity, parameterName);
            EnsureRegisterExceptionRegionsSchema(schemaIdentities,
                usingModule.ArtifactIndex, nameof(selections));

            var exceptionInterface = ResolveStandardCoreExceptionAnchor(
                selection.ExceptionInterfaceProducerIdentity,
                selection.ExceptionInterfaceExportKey, modules, artifacts,
                schemaIdentities, "exception-interface",
                parameterName);
            var unreachablePayload = ResolveStandardCoreExceptionAnchor(
                selection.UnreachableProducerIdentity,
                selection.UnreachableExportKey, modules, artifacts,
                schemaIdentities, "unreachable-payload",
                parameterName);
            if (exceptionInterface == unreachablePayload)
            {
                throw new ArgumentException(
                    "The exception-interface and Unreachable anchors must select distinct TypeDefs.",
                    parameterName);
            }
        }

        return selections.IsDefault ? [] : selections;
    }

    private static TrRmStandardModuleSelection FindStandardModuleSelection(
        ImmutableArray<TrRmStandardModuleSelection> modules,
        string moduleIdentity, string parameterName)
    {
        foreach (var module in modules)
        {
            if (StringComparer.Ordinal.Equals(module.Identity, moduleIdentity))
            {
                return module;
            }
        }

        throw new ArgumentException(
            $"The selected Module '{moduleIdentity}' is not present in the supplied Standard artifacts.",
            parameterName);
    }

    private static void EnsureRegisterExceptionRegionsSchema(
        string[] schemaIdentities, int artifactIndex, string parameterName)
    {
        if ((uint)artifactIndex >= (uint)schemaIdentities.Length
            || !StringComparer.Ordinal.Equals(schemaIdentities[artifactIndex],
                StandardRegisterExceptionRegionsSchemaIdentity))
        {
            throw new ArgumentException(
                "Core exception anchors require exact B+E1 artifacts for the using and provider Modules.",
                parameterName);
        }
    }

    private static (int ArtifactIndex, int ModuleRow, int TypeDefRow)
        ResolveStandardCoreExceptionAnchor(string producerIdentity,
            string exportKey,
            ImmutableArray<TrRmStandardModuleSelection> modules,
            byte[][] artifacts, string[] schemaIdentities, string role,
            string parameterName)
    {
        var producer = FindStandardModuleSelection(modules,
            producerIdentity, parameterName);
        EnsureRegisterExceptionRegionsSchema(schemaIdentities,
            producer.ArtifactIndex, parameterName);
        var index = CreateForeignInvocationIndex(
            artifacts[producer.ArtifactIndex],
            schemaIdentities[producer.ArtifactIndex]);
        var tables = ValidateForeignInvocationArtifact(index,
            schemaIdentities[producer.ArtifactIndex]);
        if (!tables.Exports.TryGetDefinitionForKey(producer.ModuleRow,
                exportKey, out var definition)
            || definition >> 24 != 0x13)
        {
            throw new ArgumentException(
                $"The selected {role} ExportKey is not a TypeDef exported by its selected producer Module.",
                parameterName);
        }

        var typeDefRow = checked((int)(definition & 0x00FF_FFFF));
        if (typeDefRow is <= 0
            || typeDefRow > tables.TypeDefs.Count
            || tables.TypeDefs.GetOwnerModule(typeDefRow)
                != producer.ModuleRow)
        {
            throw new ArgumentException(
                $"The selected {role} ExportKey is not owned by its selected producer Module.",
                parameterName);
        }

        return (producer.ArtifactIndex, producer.ModuleRow, typeDefRow);
    }

    private static (byte[][] Artifacts, string[] SchemaIdentities,
        TrRmA3LayoutResourceLedger? ResourceLedger)
        SnapshotStandardInputsForOperation(
            TrRmStandardArtifactInput input, string moduleIdentity,
            IReadOnlyList<TrRmStandardArtifactInput> providers)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providers);
        if (!StringComparer.Ordinal.Equals(input.SchemaIdentity,
                StandardForeignInvocationStorageGenericFixedArraySchemaIdentity))
        {
            return (SnapshotStandardInputs(input, providers),
                GetStandardSchemaIdentities(input, providers), null);
        }

        var resourceLedger
            = new TrRmA3LayoutResourceLedger(moduleIdentity);
        try
        {
            resourceLedger.ConsumeWork();
            resourceLedger.ConsumeWork(
                TrRmStandardNativeBindingPlan.GetStringComparisonWork(
                    input.SchemaIdentity,
                    StandardForeignInvocationStorageGenericFixedArraySchemaIdentity));
            resourceLedger.ConsumeWork(
                TrRmStandardNativeBindingPlan.GetStringComparisonWork(
                    moduleIdentity, moduleIdentity));
            var (artifacts, schemaIdentities) = SnapshotA3StandardInputs(
                input, providers, resourceLedger);
            return (artifacts, schemaIdentities, resourceLedger);
        }
        catch (Exception exception)
        {
            resourceLedger.AdmissionScope.Mark(exception,
                GetA3AdmissionFailureKind(exception));
            throw;
        }
    }

    private static (byte[][] Artifacts, string[] SchemaIdentities)
        SnapshotA3StandardInputs(TrRmStandardArtifactInput input,
            IReadOnlyList<TrRmStandardArtifactInput> providers,
            TrRmA3LayoutResourceLedger resourceLedger)
    {
        resourceLedger.ConsumeWork();
        var providerCount = providers.Count;
        if (providerCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(providers));
        }

        resourceLedger.ConsumeWork();
        var inputCountValue = checked((ulong)providerCount + 1UL);
        resourceLedger.ConsumeWork(checked(inputCountValue + 1UL));
        var inputCount = checked((int)inputCountValue);
        var selectedInputs = new TrRmStandardArtifactInput[inputCount];
        selectedInputs[0] = input;
        for (var index = 0; index < providerCount; index++)
        {
            resourceLedger.ConsumeWork();
            selectedInputs[index + 1] = providers[index]
                ?? throw new ArgumentNullException(
                    $"{nameof(providers)}[{index}]");
        }

        resourceLedger.ConsumeWork(checked((ulong)inputCount + 1UL));
        var schemaIdentities = new string[inputCount];
        for (var index = 0; index < inputCount; index++)
        {
            resourceLedger.ConsumeWork();
            var schemaIdentity = selectedInputs[index].SchemaIdentity;
            var parameterName = index == 0
                ? nameof(input) : $"{nameof(providers)}[{index - 1}]";
            ArgumentException.ThrowIfNullOrEmpty(schemaIdentity,
                parameterName);
            if (!IsSelectedStandardSchemaIdentity(schemaIdentity,
                    resourceLedger))
            {
                throw new ArgumentException(
                    "The exact Standard schema identity is not selected by this consumer.",
                    parameterName);
            }

            schemaIdentities[index] = schemaIdentity;
        }

        resourceLedger.ConsumeWork(checked((ulong)inputCount + 1UL));
        var artifacts = new byte[inputCount][];
        for (var index = 0; index < inputCount; index++)
        {
            resourceLedger.ConsumeWork();
            var source = selectedInputs[index].Artifact;
            resourceLedger.ConsumeWork();
            var length = source.Length;
            resourceLedger.ConsumeWork();
            resourceLedger.ConsumeWork(checked((ulong)length));
            artifacts[index] = source.ToArray();
        }

        return (artifacts, schemaIdentities);
    }

    private static bool IsSelectedStandardSchemaIdentity(string identity,
        TrRmA3LayoutResourceLedger resourceLedger)
        => IsSelectedStandardSchemaIdentity(identity,
                StandardForeignInvocationSchemaIdentity, resourceLedger)
            || IsSelectedStandardSchemaIdentity(identity,
                StandardForeignInvocationStorageAddressSchemaIdentity,
                resourceLedger)
            || IsSelectedStandardSchemaIdentity(identity,
                StandardForeignInvocationStorageFixedArraySchemaIdentity,
                resourceLedger)
            || IsSelectedStandardSchemaIdentity(identity,
                StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity,
                resourceLedger)
            || IsSelectedStandardSchemaIdentity(identity,
                StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity,
                resourceLedger)
            || IsSelectedStandardSchemaIdentity(identity,
                StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity,
                resourceLedger)
            || IsSelectedStandardSchemaIdentity(identity,
                StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity,
                resourceLedger)
            || IsSelectedStandardSchemaIdentity(identity,
                StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity,
                resourceLedger)
            || IsSelectedStandardSchemaIdentity(identity,
                StandardForeignInvocationStorageGenericFixedArraySchemaIdentity,
                resourceLedger)
            || IsSelectedStandardSchemaIdentity(identity,
                StandardRegisterExceptionRegionsSchemaIdentity,
                resourceLedger);

    private static bool IsSelectedStandardSchemaIdentity(string identity,
        string selectedIdentity,
        TrRmA3LayoutResourceLedger resourceLedger)
    {
        resourceLedger.ConsumeWork(
            TrRmStandardNativeBindingPlan.GetStringComparisonWork(identity,
                selectedIdentity));
        return StringComparer.Ordinal.Equals(identity, selectedIdentity);
    }

    private static byte[][] SnapshotStandardInputs(
        TrRmStandardArtifactInput input,
        IReadOnlyList<TrRmStandardArtifactInput> providers)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providers);
        var artifacts = new byte[providers.Count + 1][];
        artifacts[0] = SnapshotStandardInput(input, nameof(input));
        for (var index = 0; index < providers.Count; index++)
        {
            var provider = providers[index]
                ?? throw new ArgumentNullException(
                    $"{nameof(providers)}[{index}]");
            artifacts[index + 1] = SnapshotStandardInput(provider,
                $"{nameof(providers)}[{index}]");
        }

        return artifacts;
    }

    private static byte[] SnapshotStandardInput(
        TrRmStandardArtifactInput input, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(input.SchemaIdentity,
            parameterName);
        if (input.SchemaIdentity is not (StandardForeignInvocationSchemaIdentity
            or StandardForeignInvocationStorageAddressSchemaIdentity
            or StandardForeignInvocationStorageFixedArraySchemaIdentity
            or StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity
            or StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity
            or StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
            or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity
            or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
            or StandardLocalAggregateLayoutSchemaIdentity
            or StandardForeignInvocationStorageGenericFixedArraySchemaIdentity
            or StandardRegisterExceptionRegionsSchemaIdentity))
        {
            throw new ArgumentException(
                "The exact Standard schema identity is not selected by this consumer.",
                parameterName);
        }

        return [.. input.Artifact.Span];
    }

    private static string[] GetStandardSchemaIdentities(
        TrRmStandardArtifactInput input,
        IReadOnlyList<TrRmStandardArtifactInput> providers)
    {
        var result = new string[providers.Count + 1];
        result[0] = input.SchemaIdentity;
        for (var index = 0; index < providers.Count; index++)
        {
            result[index + 1] = providers[index].SchemaIdentity;
        }

        if (result.Contains(StandardLocalAggregateLayoutSchemaIdentity,
                StringComparer.Ordinal)
            && result.Any(schemaIdentity =>
                !StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardLocalAggregateLayoutSchemaIdentity)))
        {
            throw Unsupported(
                "The L1 operation requires its exact schema selection for the root and every provider artifact.");
        }

        return result;
    }

    private static TrRmArtifactSchema GetForeignInvocationSchema(
        string schemaIdentity)
        => schemaIdentity switch
        {
            StandardForeignInvocationSchemaIdentity
                => TrRmArtifactSchema.ForeignInvocationDraft,
            StandardForeignInvocationStorageAddressSchemaIdentity
                => TrRmArtifactSchema.ForeignInvocationStorageAddressDraft,
            StandardForeignInvocationStorageFixedArraySchemaIdentity
                => TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft,
            StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity
                => TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft,
            StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity
                => TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft,
            StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
                => TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft,
            StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity
                => TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft,
            StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
                => TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft,
            StandardLocalAggregateLayoutSchemaIdentity
                => TrRmArtifactSchema.LocalAggregateLayoutDraft,
            StandardForeignInvocationStorageGenericFixedArraySchemaIdentity
                => TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft,
            StandardRegisterExceptionRegionsSchemaIdentity
                => TrRmArtifactSchema.RegisterExceptionRegionsDraft,
            _ => throw new ArgumentException(
                "The exact Standard schema identity is not selected by this consumer.",
                nameof(schemaIdentity)),
        };

    private static TrRmArtifactIndex CreateForeignInvocationIndex(
        byte[] artifact, string schemaIdentity,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        var schema = GetForeignInvocationSchema(schemaIdentity);
        if (resourceLedger is not null)
        {
            return new TrRmArtifactIndex(artifact, schema, resourceLedger);
        }

        if (schema.UsesRegisterExceptionRegions())
        {
            return TrRmArtifactIndex.ForRegisterExceptionRegionsDraft(artifact);
        }

        if (schema == TrRmArtifactSchema.LocalAggregateLayoutDraft)
        {
            return TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        }

        if (schema
            .UsesForeignInvocationStorageFixedArrayRepeatInitialization())
        {
            return TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                    artifact);
        }

        if (schema.UsesRawUnionValueCoverage())
        {
            return TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyReturnCoverageDraft(
                    artifact);
        }

        return schema.UsesScalarFrozenValues()
            && schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                    artifact)
            : schema.UsesForeignInvocationStorageFixedArrays()
            ? schema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(artifact)
                : schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
                    ? TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                            artifact)
                : schema.UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                    ? TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                            artifact)
                    : TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyDraft(artifact)
            : schema.UsesForeignInvocationStorageAddresses()
                ? TrRmArtifactIndex.ForForeignInvocationStorageAddressDraft(artifact)
                : TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
    }

    private static TrRmStructuralMetadataValidator.ValidatedExecutionTables
        ValidateForeignInvocationArtifact(TrRmArtifactIndex index,
            string schemaIdentity,
            TrRmA3LayoutResourceLedger? a3LayoutResourceLedger = null,
            bool createA3ResourceLedger = true)
        => GetForeignInvocationSchema(schemaIdentity)
            .UsesForeignInvocationStorageFixedArrayRepeatInitialization()
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index)
            : GetForeignInvocationSchema(schemaIdentity)
                .UsesRawUnionValueCoverage()
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageDraftAndGetExecutionTables(
                    index)
            : GetForeignInvocationSchema(schemaIdentity)
            .UsesRegisterExceptionRegions()
            ? TrRmStructuralMetadataValidator
                .ValidateRegisterExceptionRegionsDraftAndGetExecutionTables(index)
            : GetForeignInvocationSchema(schemaIdentity)
            .UsesForeignInvocationStorageFixedArrays()
            ? GetForeignInvocationSchema(schemaIdentity)
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                        index, a3LayoutResourceLedger, createA3ResourceLedger)
                : GetForeignInvocationSchema(schemaIdentity).UsesScalarFrozenValues()
                    ? TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                            index)
                : GetForeignInvocationSchema(schemaIdentity)
                    .UsesForeignInvocationStorageFixedArrayUnitInitialization()
                    ? TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                            index)
                : GetForeignInvocationSchema(schemaIdentity)
                    .UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                    ? TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyEmptyInitializationDraftAndGetExecutionTables(
                            index)
                    : TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                            index, a3LayoutResourceLedger)
            : GetForeignInvocationSchema(schemaIdentity)
                .UsesForeignInvocationStorageAddresses()
                ? TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(index)
                : TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationDraftAndGetExecutionTables(index);

    private readonly record struct StandardModuleValidation(
        int RootModuleRow,
        ImmutableArray<TrRmStandardModuleSelection> Modules,
        bool HasNativeOutputProviderImports);

    private static StandardModuleValidation ValidateStandardInputModules(
        byte[][] artifacts, string[] schemaIdentities,
        string rootModuleIdentity,
        TrRmA3LayoutResourceLedger? a3LayoutResourceLedger)
    {
        var exactFProviderClosure
            = IsExactForeignInvocationSchemaSet(schemaIdentities);
        var hasNativeOutputProviderImports
            = !exactFProviderClosure
                && IsNativeOutputProviderSchemaSet(schemaIdentities)
                && !GetNativeOutputProviderImportIdentities(artifacts[0],
                    schemaIdentities[0], rootModuleIdentity).IsEmpty;
        var includesFProviderClosure = exactFProviderClosure
            || hasNativeOutputProviderImports;
        var includesL1MethodProviderClosure
            = schemaIdentities.Length != 0
                && schemaIdentities.All(static schemaIdentity =>
                    StringComparer.Ordinal.Equals(schemaIdentity,
                        StandardLocalAggregateLayoutSchemaIdentity));
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var rootModuleRow = 0;
        var moduleSelections
            = ImmutableArray.CreateBuilder<TrRmStandardModuleSelection>();
        for (var index = 0; index < artifacts.Length; index++)
        {
            var artifactIndex = CreateForeignInvocationIndex(artifacts[index],
                schemaIdentities[index], a3LayoutResourceLedger);
            var tables = ValidateForeignInvocationArtifact(artifactIndex,
                schemaIdentities[index], a3LayoutResourceLedger,
                createA3ResourceLedger: false);
            var populatedModuleCount = 0;
            for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
                moduleRow++)
            {
                var identity = tables.Modules.GetIdentity(moduleRow);
                if (!identities.Add(identity))
                {
                    throw Invalid("A selected Module identity appears in multiple artifacts.");
                }

                if (index == 0
                    && StringComparer.Ordinal.Equals(identity,
                        rootModuleIdentity))
                {
                    rootModuleRow = moduleRow;
                }

                var (targetRequirement, runtimePolicy)
                    = tables.Modules.GetRequirements(moduleRow);
                if ((targetRequirement, runtimePolicy) != (1u, 0u))
                {
                    throw Unsupported(
                        "The bounded F consumer supports only Windows x64 fail-fast Modules.");
                }

                var (featureStart, featureCount)
                    = tables.Modules.GetFeatureRange(moduleRow);
                var supportsA2Features = schemaIdentities[index] is
                    StandardForeignInvocationStorageFixedArraySchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
                    or StandardLocalAggregateLayoutSchemaIdentity;
                var supportsNativeOutputCells
                    = StringComparer.Ordinal.Equals(schemaIdentities[index],
                        StandardForeignInvocationSchemaIdentity)
                    || StringComparer.Ordinal.Equals(schemaIdentities[index],
                        StandardForeignInvocationStorageAddressSchemaIdentity)
                    || supportsA2Features
                    || StringComparer.Ordinal.Equals(schemaIdentities[index],
                        StandardForeignInvocationStorageGenericFixedArraySchemaIdentity);
                var supportsScalarFrozenValues
                    = StringComparer.Ordinal.Equals(schemaIdentities[index],
                        StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity)
                    || StringComparer.Ordinal.Equals(schemaIdentities[index],
                        StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity)
                    || StringComparer.Ordinal.Equals(schemaIdentities[index],
                        StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity)
                    || StringComparer.Ordinal.Equals(schemaIdentities[index],
                        StandardLocalAggregateLayoutSchemaIdentity);
                var supportsCopyOriginAndUnionCoverage
                    = StringComparer.Ordinal.Equals(schemaIdentities[index],
                        StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity)
                    || StringComparer.Ordinal.Equals(schemaIdentities[index],
                        StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity)
                    || StringComparer.Ordinal.Equals(schemaIdentities[index],
                        StandardLocalAggregateLayoutSchemaIdentity);
                var supportsRegisterExceptionRegions
                    = StringComparer.Ordinal.Equals(schemaIdentities[index],
                        StandardRegisterExceptionRegionsSchemaIdentity);
                for (var offset = 0; offset < featureCount; offset++)
                {
                    var feature = tables.Features.GetFeature(
                        featureStart + offset);
                    if (feature.FeatureId
                        is not (TrRmFeatureTableReader.NativeExportEntriesFeatureId
                            or TrRmFeatureTableReader.RootlessNativeValuesFeatureId
                            or TrRmFeatureTableReader.ForeignInvocationContractsCandidateFeatureId)
                        && !(supportsNativeOutputCells
                            && feature.FeatureId
                                == TrRmFeatureTableReader.DirectNativeCallsFeatureId)
                        && !(supportsNativeOutputCells
                            && feature.FeatureId
                                == TrRmFeatureTableReader.NativeBoundaryContractsFeatureId)
                        && !(supportsCopyOriginAndUnionCoverage
                            && feature.FeatureId is
                                TrRmFeatureTableReader.CopyOriginContractsCandidateFeatureId
                                or TrRmFeatureTableReader.RawUnionValueCoverageFeatureId)
                        && !(supportsA2Features
                            && feature.FeatureId
                                == TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId)
                        && !(supportsScalarFrozenValues
                            && feature.FeatureId
                                == TrRmFeatureTableReader.TypedFrozenValuesFeatureId)
                        && !(supportsRegisterExceptionRegions
                            && feature.FeatureId
                                == TrRmFeatureTableReader.RegisterExceptionRegionsFeatureId)
                        && !(supportsNativeOutputCells
                            && feature.FeatureId is
                                TrRmFeatureTableReader.NativeOutputCellsCandidateFeatureId
                                or TrRmFeatureTableReader.PublishedPredicatesFeatureId))
                    {
                        throw Unsupported(
                            "The bounded F consumer does not support a required Module feature.");
                    }
                }

                var (methodStart, methodEnd)
                    = tables.Modules.GetMethodDefRange(moduleRow);
                var (typeStart, typeEnd)
                    = tables.Modules.GetTypeDefRange(moduleRow);
                moduleSelections.Add(new TrRmStandardModuleSelection(index,
                    moduleRow, identity, targetRequirement, runtimePolicy,
                    tables.Modules.GetEntryMethod(moduleRow), methodStart,
                    methodEnd));
                if (methodEnd == methodStart && typeEnd == typeStart)
                {
                    continue;
                }

                populatedModuleCount++;
                var (moduleRefStart, moduleRefEnd)
                    = tables.Modules.GetModuleRefRange(moduleRow);
                var isSelectedRoot = index == 0
                    && moduleRow == rootModuleRow;
                var isA2TypeOnlyModule
                    = (schemaIdentities[index] is
                        StandardForeignInvocationStorageFixedArraySchemaIdentity
                        or StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity
                        or StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity
                        or StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
                        or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity
                        or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
                        or StandardLocalAggregateLayoutSchemaIdentity)
                    && methodEnd == methodStart && typeEnd > typeStart;
                if (!isSelectedRoot && moduleRefEnd != moduleRefStart
                    && !isA2TypeOnlyModule && !includesFProviderClosure
                    && !includesL1MethodProviderClosure)
                {
                    throw Unsupported(
                        "Provider Imports require an A2 type-only Module, an exact F closure, or an exact L1 semantic closure.");
                }

                if (isA2TypeOnlyModule)
                {
                    var usedModuleRefs
                        = new bool[tables.ModuleRefs.Count + 1];
                    for (var importRow = 1;
                        importRow <= tables.Imports.Count; importRow++)
                    {
                        var importModuleRef
                            = tables.Imports.GetModuleRefForImportRow(
                                importRow);
                        if (tables.ModuleRefs.GetOwnerModule(importModuleRef)
                            != moduleRow)
                        {
                            continue;
                        }

                        if ((tables.Imports
                                .GetExpectedDefinitionForImportRow(importRow)
                                >> 24) != 0x13)
                        {
                            throw Unsupported(
                                "An A2 type-only provider may retain only TypeDef Imports.");
                        }

                        usedModuleRefs[importModuleRef] = true;
                    }

                    for (var moduleRefRow = moduleRefStart;
                        moduleRefRow < moduleRefEnd; moduleRefRow++)
                    {
                        if (!usedModuleRefs[moduleRefRow])
                        {
                            throw Invalid(
                                "An A2 type-only provider has an unconsumed ModuleRef.");
                        }
                    }
                }

                if (!isSelectedRoot && methodEnd != methodStart
                    && !includesFProviderClosure
                    && !includesL1MethodProviderClosure)
                {
                    _ = LoadForeignInvocationDraftNativeAddressLibrary(
                        artifacts[index], moduleRow, null,
                        verifySemantics: false,
                        schema: GetForeignInvocationSchema(schemaIdentities[index]));
                }
            }

            if (index == 0 && rootModuleRow == 0)
            {
                throw new ArgumentException(
                    "The selected root Module identity is not present in the artifact.",
                    nameof(rootModuleIdentity));
            }

            var rootMethodStart = 0;
            var rootMethodEnd = 0;
            var rootTypeStart = 0;
            var rootTypeEnd = 0;
            if (index == 0)
            {
                (rootMethodStart, rootMethodEnd)
                    = tables.Modules.GetMethodDefRange(rootModuleRow);
                (rootTypeStart, rootTypeEnd)
                    = tables.Modules.GetTypeDefRange(rootModuleRow);
            }

            var rootIsA2TypeLibrary = index == 0
                && (schemaIdentities[0] is
                    StandardForeignInvocationStorageFixedArraySchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
                    or StandardLocalAggregateLayoutSchemaIdentity)
                && rootMethodStart == rootMethodEnd
                && rootTypeStart < rootTypeEnd
                && tables.Modules.GetEntryMethod(rootModuleRow) == 0;
            if ((populatedModuleCount == 0
                    && !includesFProviderClosure)
                || (index == 0 && rootMethodEnd == rootMethodStart
                    && !rootIsA2TypeLibrary))
            {
                throw Unsupported(
                    "The selected F artifact must contain a supported populated Module.");
            }
        }

        return new StandardModuleValidation(rootModuleRow,
            moduleSelections.ToImmutable(), hasNativeOutputProviderImports);
    }

    private static int FindModuleRow(TrRmModuleTableReader modules,
        string moduleIdentity)
    {
        for (var moduleRow = 1; moduleRow <= modules.Count; moduleRow++)
        {
            if (StringComparer.Ordinal.Equals(
                    modules.GetIdentity(moduleRow), moduleIdentity))
            {
                return moduleRow;
            }
        }

        throw new ArgumentException(
            "The selected Module identity is not present in the artifact.",
            nameof(moduleIdentity));
    }
}
