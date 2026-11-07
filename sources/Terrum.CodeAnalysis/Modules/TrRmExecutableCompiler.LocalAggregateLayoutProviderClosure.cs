// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private sealed record LocalAggregateMethodProviderEdge(
        int ConsumerArtifactIndex, int ConsumerModuleRow,
        string ConsumerModuleIdentity, int ImportRow, int ModuleRefRow,
        int ExpectedMethodRow, string ProducerIdentity, string ExportKey,
        uint TargetRequirement, uint RuntimePolicy,
        int ProviderArtifactIndex, int ProviderModuleRow,
        string ProviderModuleIdentity, int ExportRow, int ActualMethodRow);

    private sealed record LocalAggregateMethodProviderImport(
        int ConsumerArtifactIndex, int ConsumerModuleRow,
        string ConsumerModuleIdentity, int ImportRow, int ModuleRefRow,
        int ExpectedMethodRow, string ProducerIdentity, string ExportKey,
        uint TargetRequirement, uint RuntimePolicy,
        int ProviderArtifactIndex, int ProviderModuleRow,
        string ProviderModuleIdentity, ImmutableArray<uint> ExpectedTypeSpecs,
        ImmutableArray<byte> ExpectedParameterFlags);

    private sealed record LocalAggregateTypeProviderImport(
        int ConsumerArtifactIndex, int ConsumerModuleRow,
        string ConsumerModuleIdentity, int ImportRow, int ModuleRefRow,
        uint ExpectedDefinitionId, string ProducerIdentity,
        string ExportKey, uint TargetRequirement, uint RuntimePolicy,
        int ProviderArtifactIndex, int ProviderModuleRow,
        string ProviderModuleIdentity, int ProviderTypeDefRow);

    private static bool HasLocalAggregateMethodClosureWork(
        byte[][] artifacts, string[] schemaIdentities,
        StandardModuleValidation moduleValidation)
    {
        for (var artifactIndex = 0; artifactIndex < artifacts.Length;
            artifactIndex++)
        {
            var index = CreateForeignInvocationIndex(
                artifacts[artifactIndex], schemaIdentities[artifactIndex]);
            var tables = ValidateForeignInvocationArtifact(index,
                schemaIdentities[artifactIndex],
                createA3ResourceLedger: false);
            foreach (var module in moduleValidation.Modules)
            {
                if (module.ArtifactIndex != artifactIndex)
                {
                    continue;
                }

                for (var methodRow = module.MethodDefStart;
                    methodRow < module.MethodDefEnd; methodRow++)
                {
                    if (tables.Methods.GetBodyRow(methodRow) != 0)
                    {
                        return true;
                    }
                }
            }

            for (var importRow = 1; importRow <= tables.Imports.Count;
                importRow++)
            {
                if ((tables.Imports
                        .GetExpectedDefinitionForImportRow(importRow)
                    >> 24) == 0x16)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static BoundProgram LoadLocalAggregateLayoutProviderClosure(
        byte[][] artifacts, string[] schemaIdentities,
        string rootModuleIdentity,
        StandardModuleValidation moduleValidation,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings)
    {
        if (artifacts.Length != schemaIdentities.Length
            || schemaIdentities.Any(static schemaIdentity =>
                !StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardLocalAggregateLayoutSchemaIdentity)))
        {
            throw Unsupported(
                "The L1 semantic MethodDef closure requires exact L1 selections for every supplied artifact.");
        }

        var modulesByIdentity
            = new Dictionary<string, TrRmStandardModuleSelection>(
                moduleValidation.Modules.Length, StringComparer.Ordinal);
        var modulesByLocation
            = new Dictionary<(int ArtifactIndex, int ModuleRow),
                TrRmStandardModuleSelection>(
                moduleValidation.Modules.Length);
        foreach (var module in moduleValidation.Modules)
        {
            if ((uint)module.ArtifactIndex >= (uint)artifacts.Length
                || !modulesByIdentity.TryAdd(module.Identity, module)
                || !modulesByLocation.TryAdd(
                    (module.ArtifactIndex, module.ModuleRow), module))
            {
                throw Invalid(
                    "The L1 semantic MethodDef closure has duplicate or invalid Module selections.");
            }
        }

        if (!modulesByLocation.TryGetValue((0,
                moduleValidation.RootModuleRow), out var rootModule)
            || !StringComparer.Ordinal.Equals(rootModule.Identity,
                rootModuleIdentity))
        {
            throw Invalid(
                "The L1 semantic MethodDef closure lost its exact root Module selection.");
        }

        var typeBindingsByImport = new Dictionary<
            (int ArtifactIndex, int ModuleRow, int ImportRow),
            TrRmStandardTypeProviderBinding>(
            typeProviderBindings.Length);
        foreach (var binding in typeProviderBindings)
        {
            if (!typeBindingsByImport.TryAdd(
                    (binding.ConsumerArtifactIndex,
                        binding.ConsumerModuleRow, binding.ImportRow),
                    binding))
            {
                throw Invalid(
                    "The L1 TypeDef provider closure contains a duplicate Import binding.");
            }
        }

        var moduleImports
            = new Dictionary<(int ArtifactIndex, int ModuleRow),
                Dictionary<int, BoundModuleImport>>(
                moduleValidation.Modules.Length);
        var modulesByArtifact
            = new List<TrRmStandardModuleSelection>[artifacts.Length];
        for (var artifactIndex = 0; artifactIndex < artifacts.Length;
            artifactIndex++)
        {
            modulesByArtifact[artifactIndex] = [];
        }
        foreach (var module in moduleValidation.Modules)
        {
            modulesByArtifact[module.ArtifactIndex].Add(module);
            moduleImports.Add(
                (module.ArtifactIndex, module.ModuleRow), []);
        }

        var methodImports
            = new List<LocalAggregateMethodProviderImport>();
        var typeImports
            = new List<LocalAggregateTypeProviderImport>();
        var methodEdges
            = new List<LocalAggregateMethodProviderEdge>();
        var typeEdges
            = ImmutableArray.CreateBuilder<BoundStandardTypeProviderEdge>(
                typeProviderBindings.Length);
        var methodTypeSpecPairs
            = ImmutableArray.CreateBuilder<
                TrRmStandardMethodTypeSpecPair>();
        for (var artifactIndex = 0; artifactIndex < artifacts.Length;
            artifactIndex++)
        {
            var index = CreateForeignInvocationIndex(
                artifacts[artifactIndex], schemaIdentities[artifactIndex]);
            var tables = ValidateForeignInvocationArtifact(index,
                schemaIdentities[artifactIndex],
                createA3ResourceLedger: false);
            var modulesByRow = modulesByArtifact[artifactIndex]
                .ToDictionary(static module => module.ModuleRow);
            var moduleRefRowsUsed
                = new bool[tables.ModuleRefs.Count + 1];
            for (var importRow = 1; importRow <= tables.Imports.Count;
                importRow++)
            {
                var moduleRefRow
                    = tables.Imports.GetModuleRefForImportRow(importRow);
                var consumerModuleRow
                    = tables.ModuleRefs.GetOwnerModule(moduleRefRow);
                if (!modulesByRow.TryGetValue(consumerModuleRow,
                        out var module))
                {
                    throw Invalid(
                        "An L1 Import is owned by an unselected Module.");
                }

                var (moduleRefStart, moduleRefEnd)
                    = tables.Modules.GetModuleRefRange(
                        consumerModuleRow);
                if (moduleRefRow < moduleRefStart
                    || moduleRefRow >= moduleRefEnd)
                {
                    throw Invalid(
                        "An L1 Import selects a ModuleRef outside its owning Module range.");
                }

                moduleRefRowsUsed[moduleRefRow] = true;
                var expectedDefinition = tables.Imports
                    .GetExpectedDefinitionForImportRow(importRow);
                var definitionKind = expectedDefinition >> 24;
                var binding = tables.Imports.GetBindingForImportRow(importRow);
                var moduleRefIdentity = tables.ModuleRefs
                    .GetProducerIdentity(moduleRefRow);
                var (targetRequirement, runtimePolicy)
                    = tables.ModuleRefs.GetRequirements(moduleRefRow);
                if (!StringComparer.Ordinal.Equals(moduleRefIdentity,
                        binding.ProducerIdentity)
                    || targetRequirement != binding.TargetRequirement
                    || runtimePolicy != binding.RuntimePolicy)
                {
                    throw Invalid(
                        "An L1 Import and its exact ModuleRef binding differ.");
                }

                if (!modulesByIdentity.TryGetValue(
                        binding.ProducerIdentity, out var providerModule)
                    || providerModule.TargetRequirement
                        != binding.TargetRequirement
                    || providerModule.RuntimePolicy != binding.RuntimePolicy)
                {
                    throw Invalid(
                        "An L1 Import does not select one supplied Module with matching requirements.");
                }

                if (definitionKind == 0x13)
                {
                    if (!typeBindingsByImport.TryGetValue(
                            (artifactIndex, module.ModuleRow, importRow),
                            out var typeBinding)
                        || typeBinding.ExpectedTypeDefRow
                            != (int)(expectedDefinition
                                & 0x00FF_FFFF)
                        || typeBinding.ProviderArtifactIndex
                            != providerModule.ArtifactIndex
                        || typeBinding.ProviderModuleRow
                            != providerModule.ModuleRow
                        || !StringComparer.Ordinal.Equals(
                            typeBinding.ProducerIdentity,
                            binding.ProducerIdentity)
                        || !StringComparer.Ordinal.Equals(
                            typeBinding.ExportKey, binding.ExportKey)
                        || typeBinding.TargetRequirement
                            != binding.TargetRequirement
                        || typeBinding.RuntimePolicy
                            != binding.RuntimePolicy)
                    {
                        throw Invalid(
                            "An L1 TypeDef Import has no matching checked provider origin.");
                    }

                    typeImports.Add(new LocalAggregateTypeProviderImport(
                        artifactIndex, module.ModuleRow, module.Identity,
                        importRow, moduleRefRow, expectedDefinition,
                        binding.ProducerIdentity, binding.ExportKey,
                        binding.TargetRequirement, binding.RuntimePolicy,
                        providerModule.ArtifactIndex,
                        providerModule.ModuleRow, providerModule.Identity,
                        typeBinding.ProviderTypeDefRow));
                    continue;
                }

                if (definitionKind != 0x16)
                {
                    throw Unsupported(
                        "The L1 semantic MethodDef closure admits only TypeDef and MethodDef Imports.");
                }

                var expectedMethodRow = checked((int)(expectedDefinition
                    & 0x00FF_FFFF));
                ValidateExpectedMethod(tables, module, expectedMethodRow);
                var (expectedTypeSpecs, expectedParameterFlags)
                    = CaptureExpectedMethodSignature(tables,
                        expectedMethodRow);
                methodImports.Add(new LocalAggregateMethodProviderImport(
                    artifactIndex, module.ModuleRow, module.Identity,
                    importRow, moduleRefRow, expectedMethodRow,
                    binding.ProducerIdentity, binding.ExportKey,
                    binding.TargetRequirement, binding.RuntimePolicy,
                    providerModule.ArtifactIndex,
                    providerModule.ModuleRow, providerModule.Identity,
                    expectedTypeSpecs, expectedParameterFlags));
                moduleImports[(module.ArtifactIndex, module.ModuleRow)]
                    .Add(importRow, new BoundModuleImport(
                        binding.ProducerIdentity, binding.ExportKey,
                        binding.TargetRequirement, binding.RuntimePolicy));
            }

            foreach (var module in modulesByArtifact[artifactIndex])
            {
                var (moduleRefStart, moduleRefEnd)
                    = tables.Modules.GetModuleRefRange(module.ModuleRow);
                for (var moduleRefRow = moduleRefStart;
                    moduleRefRow < moduleRefEnd; moduleRefRow++)
                {
                    if (!moduleRefRowsUsed[moduleRefRow])
                    {
                        throw Invalid(
                            "An L1 Module has an unconsumed ModuleRef.");
                    }
                }
            }
        }

        if (typeImports.Count != typeProviderBindings.Length
            || methodImports.Count != moduleImports.Values.Sum(
                static imports => imports.Count))
        {
            throw Invalid(
                "The L1 semantic Module closure has an unbound Import edge.");
        }

        for (var providerArtifactIndex = 0;
            providerArtifactIndex < artifacts.Length;
            providerArtifactIndex++)
        {
            var providerTypeImports = typeImports.Where(edge =>
                edge.ProviderArtifactIndex == providerArtifactIndex)
                .ToArray();
            var providerMethodImports = methodImports.Where(edge =>
                edge.ProviderArtifactIndex == providerArtifactIndex)
                .ToArray();
            if (providerTypeImports.Length == 0
                && providerMethodImports.Length == 0)
            {
                continue;
            }

            var providerIndex = CreateForeignInvocationIndex(
                artifacts[providerArtifactIndex],
                schemaIdentities[providerArtifactIndex]);
            var providerTables
                = ValidateForeignInvocationArtifact(providerIndex,
                    schemaIdentities[providerArtifactIndex],
                    createA3ResourceLedger: false);
            foreach (var import in providerTypeImports)
            {
                var actualTypeDefinition = 0x1300_0000u
                    | checked((uint)import.ProviderTypeDefRow);
                if (!providerTables.Exports.TryGetDefinitionForKey(
                        import.ProviderModuleRow, import.ExportKey,
                        out var exportedDefinition)
                    || exportedDefinition != actualTypeDefinition
                    || !providerTables.Exports.TryGetExportRowForKey(
                        import.ProviderModuleRow, import.ExportKey,
                        out var exportRow))
                {
                    throw Invalid(
                        "An L1 TypeDef Export no longer matches its checked provider origin.");
                }

                typeEdges.Add(new BoundStandardTypeProviderEdge(
                    import.ConsumerArtifactIndex, import.ConsumerModuleRow,
                    import.ImportRow, import.ModuleRefRow,
                    import.ExpectedDefinitionId, import.ProducerIdentity,
                    import.ExportKey, import.TargetRequirement,
                    import.RuntimePolicy, import.ProviderArtifactIndex,
                    import.ProviderModuleRow, exportRow,
                    actualTypeDefinition));
            }

            foreach (var import in providerMethodImports)
            {
                if (!providerTables.Exports.TryGetDefinitionForKey(
                        import.ProviderModuleRow, import.ExportKey,
                        out var actualDefinition)
                    || (actualDefinition >> 24) != 0x16
                    || !providerTables.Exports.TryGetExportRowForKey(
                        import.ProviderModuleRow, import.ExportKey,
                        out var exportRow))
                {
                    throw Invalid(
                        "An L1 MethodDef ExportKey does not select one provider-local MethodDef.");
                }

                var actualMethodRow = checked((int)(actualDefinition
                    & 0x00FF_FFFF));
                var providerModule = modulesByLocation[
                    (import.ProviderArtifactIndex,
                        import.ProviderModuleRow)];
                ValidateActualMethod(providerTables, providerModule,
                    actualMethodRow);
                AppendMethodSignatureTypePairs(import, providerTables,
                    actualMethodRow, methodTypeSpecPairs);
                methodEdges.Add(new LocalAggregateMethodProviderEdge(
                    import.ConsumerArtifactIndex, import.ConsumerModuleRow,
                    import.ConsumerModuleIdentity, import.ImportRow,
                    import.ModuleRefRow, import.ExpectedMethodRow,
                    import.ProducerIdentity, import.ExportKey,
                    import.TargetRequirement, import.RuntimePolicy,
                    import.ProviderArtifactIndex, import.ProviderModuleRow,
                    import.ProviderModuleIdentity, exportRow,
                    actualMethodRow));
            }
        }

        if (methodEdges.Count != methodImports.Count)
        {
            throw Invalid(
                "The L1 semantic MethodDef closure omitted a selected Import edge.");
        }

        var moduleEdges = new Dictionary<
            (int ArtifactIndex, int ModuleRow),
            List<(int ArtifactIndex, int ModuleRow)>>(
            moduleValidation.Modules.Length);
        foreach (var module in moduleValidation.Modules)
        {
            moduleEdges.Add((module.ArtifactIndex, module.ModuleRow), []);
        }

        foreach (var import in methodImports)
        {
            moduleEdges[(import.ConsumerArtifactIndex,
                import.ConsumerModuleRow)].Add((
                    import.ProviderArtifactIndex, import.ProviderModuleRow));
        }

        foreach (var import in typeImports)
        {
            moduleEdges[(import.ConsumerArtifactIndex,
                import.ConsumerModuleRow)].Add((
                    import.ProviderArtifactIndex, import.ProviderModuleRow));
        }

        var reachableModules = new HashSet<(int ArtifactIndex, int ModuleRow)>();
        var pendingModules = new Queue<(int ArtifactIndex, int ModuleRow)>();
        var rootLocation = (ArtifactIndex: 0,
            ModuleRow: moduleValidation.RootModuleRow);
        _ = reachableModules.Add(rootLocation);
        pendingModules.Enqueue(rootLocation);
        while (pendingModules.TryDequeue(out var current))
        {
            foreach (var provider in moduleEdges[current])
            {
                if (reachableModules.Add(provider))
                {
                    pendingModules.Enqueue(provider);
                }
            }
        }

        if (reachableModules.Count != moduleValidation.Modules.Length)
        {
            throw Invalid(
                "The L1 semantic provider closure contains a disconnected supplied Module.");
        }

        if (methodTypeSpecPairs.Count != 0)
        {
            var rootIndexForMethodTypes
                = CreateForeignInvocationIndex(artifacts[0],
                    schemaIdentities[0]);
            var rootTablesForMethodTypes
                = ValidateForeignInvocationArtifact(
                    rootIndexForMethodTypes, schemaIdentities[0],
                    createA3ResourceLedger: false);
            TrRmFixedArrayProviderBindingValidator
                .ValidateMethodTypeSpecPairs(rootTablesForMethodTypes,
                    artifacts, schemaIdentities,
                    moduleValidation.Modules,
                    moduleValidation.RootModuleRow,
                    methodTypeSpecPairs.ToImmutable());
        }

        ValidateNoConflictingMethodProviderOrigins(methodEdges);

        var modulePrograms = new Dictionary<
            (int ArtifactIndex, int ModuleRow),
            LocalAggregateLayoutLoadedModule>(
            moduleValidation.Modules.Length);
        var governingRuleEditions = new Dictionary<
            (int ArtifactIndex, int ModuleRow), uint>(
            moduleValidation.Modules.Length);
        for (var artifactIndex = 0; artifactIndex < artifacts.Length;
            artifactIndex++)
        {
            var moduleIndex = CreateForeignInvocationIndex(
                artifacts[artifactIndex],
                schemaIdentities[artifactIndex]);
            var moduleTables = ValidateForeignInvocationArtifact(
                moduleIndex, schemaIdentities[artifactIndex],
                createA3ResourceLedger: false);
            foreach (var module in modulesByArtifact[artifactIndex])
            {
                var selectedImports
                    = moduleImports[(module.ArtifactIndex,
                        module.ModuleRow)];
                var loadedModule = LoadLocalAggregateLayoutExecution(
                    moduleIndex, moduleTables, module.ModuleRow,
                    selectedImports, isRootModule:
                        module.ArtifactIndex == 0
                            && module.ModuleRow
                                == moduleValidation.RootModuleRow);
                var moduleProgram = RetainLoadedA2SourceTypeFacts(
                    loadedModule.Program, artifacts, schemaIdentities,
                    moduleTables, module.ModuleRow, typeProviderBindings,
                    retainExecutableTypes:
                        !loadedModule.Program.Functions.IsEmpty,
                    consumerArtifactIndex: module.ArtifactIndex);
                modulePrograms.Add(
                    (module.ArtifactIndex, module.ModuleRow),
                    loadedModule with { Program = moduleProgram });
                governingRuleEditions.Add(
                    (module.ArtifactIndex, module.ModuleRow),
                    moduleTables.Modules.GetGoverningRuleEdition(
                        module.ModuleRow));
            }
        }

        if (!modulePrograms.TryGetValue((0,
                moduleValidation.RootModuleRow), out var rootProgramData))
        {
            throw Invalid(
                "The L1 semantic MethodDef closure omitted its root Program.");
        }

        var artifactsForCarrier
            = ImmutableArray.CreateBuilder<
                BoundStandardMethodProviderArtifact>(artifacts.Length);
        for (var artifactIndex = 0; artifactIndex < artifacts.Length;
            artifactIndex++)
        {
            artifactsForCarrier.Add(
                new BoundStandardMethodProviderArtifact(artifactIndex,
                    schemaIdentities[artifactIndex],
                    [.. artifacts[artifactIndex]]));
        }

        var modulesForCarrier
            = ImmutableArray.CreateBuilder<
                BoundStandardMethodProviderModule>(
                moduleValidation.Modules.Length);
        foreach (var module in moduleValidation.Modules)
        {
            var loadedModule = modulePrograms[
                (module.ArtifactIndex, module.ModuleRow)];
            modulesForCarrier.Add(
                new BoundStandardMethodProviderModule(
                    module.ArtifactIndex, module.ModuleRow, module.Identity,
                    governingRuleEditions[
                        (module.ArtifactIndex, module.ModuleRow)],
                    module.TargetRequirement, module.RuntimePolicy,
                    module.EntryMethodDefRow, module.MethodDefStart,
                    module.MethodDefEnd, loadedModule.Program,
                    [.. loadedModule.FunctionIndexByMethodRow],
                    [.. loadedModule.FunctionIndexByImportRow]));
        }

        var methodEdgesForCarrier
            = ImmutableArray.CreateBuilder<
                BoundStandardMethodProviderEdge>(methodEdges.Count);
        foreach (var edge in methodEdges)
        {
            var consumerModule = modulePrograms[
                (edge.ConsumerArtifactIndex, edge.ConsumerModuleRow)];
            var providerModule = modulePrograms[
                (edge.ProviderArtifactIndex, edge.ProviderModuleRow)];
            var importFunctionIndex
                = consumerModule.FunctionIndexByImportRow[edge.ImportRow];
            var actualFunctionIndex
                = providerModule.FunctionIndexByMethodRow[
                    edge.ActualMethodRow];
            if (importFunctionIndex < 0 || actualFunctionIndex < 0)
            {
                throw Invalid(
                    "An L1 MethodDef provider edge has no reconstructed function origin.");
            }

            methodEdgesForCarrier.Add(
                new BoundStandardMethodProviderEdge(
                    edge.ConsumerArtifactIndex, edge.ConsumerModuleRow,
                    edge.ConsumerModuleIdentity, edge.ImportRow,
                    edge.ModuleRefRow,
                    new BoundStandardMethodProviderMethodOrigin(
                        edge.ConsumerArtifactIndex, edge.ConsumerModuleRow,
                        edge.ExpectedMethodRow,
                        0x1600_0000u
                            | checked((uint)edge.ExpectedMethodRow),
                        importFunctionIndex),
                    edge.ProducerIdentity, edge.ExportKey,
                    edge.TargetRequirement, edge.RuntimePolicy,
                    edge.ProviderArtifactIndex, edge.ProviderModuleRow,
                    edge.ProviderModuleIdentity, edge.ExportRow,
                    new BoundStandardMethodProviderMethodOrigin(
                        edge.ProviderArtifactIndex, edge.ProviderModuleRow,
                        edge.ActualMethodRow,
                        0x1600_0000u
                            | checked((uint)edge.ActualMethodRow),
                        actualFunctionIndex)));
        }

        var closure = new BoundStandardMethodProviderClosure(
            0, moduleValidation.RootModuleRow, rootModuleIdentity,
            artifactsForCarrier.MoveToImmutable(),
            modulesForCarrier.MoveToImmutable(),
            methodEdgesForCarrier.MoveToImmutable(),
            typeEdges.MoveToImmutable());
        return rootProgramData.Program with {
            StandardMethodProviderClosure = closure,
        };
    }

    private static void ValidateExpectedMethod(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        TrRmStandardModuleSelection module, int methodRow)
    {
        var methodStart = module.MethodDefStart;
        var methodEnd = module.MethodDefEnd;
        if (methodRow < methodStart || methodRow >= methodEnd
            || tables.Methods.GetOwnerModule(methodRow) != module.ModuleRow
            || tables.Methods.GetOwnerType(methodRow) != 0
            || tables.Methods.GetBodyRow(methodRow) != 0
            || tables.Methods.GetFlags(methodRow) > 2
            || !tables.Imports.IsExpectedDefinition(
                0x1600_0000u | checked((uint)methodRow)))
        {
            throw Invalid(
                "An L1 imported MethodDef expectation is not a bodyless local top-level declaration.");
        }

        if (tables.Methods.GetContractRange(methodRow).Count != 0)
        {
            throw Unsupported(
                "L1 MethodDef provider binding does not admit MethodDef contract rows.");
        }
    }

    private static void ValidateActualMethod(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        TrRmStandardModuleSelection module, int methodRow)
    {
        if (methodRow < module.MethodDefStart
            || methodRow >= module.MethodDefEnd
            || tables.Methods.GetOwnerModule(methodRow) != module.ModuleRow
            || tables.Methods.GetOwnerType(methodRow) != 0
            || tables.Methods.GetFlags(methodRow) != 2
            || tables.Methods.GetBodyRow(methodRow) == 0
            || tables.Imports.IsExpectedDefinition(
                0x1600_0000u | checked((uint)methodRow)))
        {
            throw Invalid(
                "An L1 MethodDef Export must select a public provider-owned local MethodDef with one Body.");
        }

        if (tables.Methods.GetContractRange(methodRow).Count != 0)
        {
            throw Unsupported(
                "L1 MethodDef provider binding does not admit MethodDef contract rows.");
        }
    }

    private static (ImmutableArray<uint> TypeSpecs,
        ImmutableArray<byte> ParameterFlags) CaptureExpectedMethodSignature(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int methodRow)
    {
        var signature = tables.Methods.GetSignature(methodRow);
        var (Start, Count) = tables.Signatures.GetParamRange(signature);
        if (tables.Signatures.GetReceiverTypeSpec(signature) != 0
            || tables.Signatures.GetGenericCount(signature) != 0)
        {
            throw Unsupported(
                "L1 MethodDef providers initially require nongeneric module-level Methods without receivers.");
        }

        if (Count > SemanticVerifier.MaximumArguments)
        {
            throw Unsupported(
                "An L1 MethodDef signature exceeds verifier argument bounds.");
        }

        var typeSpecs = ImmutableArray.CreateBuilder<uint>(
            Count + 1);
        var parameterFlags = ImmutableArray.CreateBuilder<byte>(Count);
        typeSpecs.Add(tables.Signatures.GetResultTypeSpec(signature));
        for (var ordinal = 0; ordinal < Count; ordinal++)
        {
            var parameterRow = Start + ordinal;
            var flags = tables.Parameters.GetFlags(parameterRow);
            if (tables.Parameters.GetMode(parameterRow) != 0
                || flags is not (0 or 1))
            {
                throw Unsupported(
                    "L1 MethodDef providers require ordinary parameters or the assigned implicit-readonly-reference adaptation.");
            }

            parameterFlags.Add(flags);
            typeSpecs.Add(tables.Parameters.GetTypeSpec(parameterRow));
        }

        return (typeSpecs.MoveToImmutable(),
            parameterFlags.MoveToImmutable());
    }

    private static void AppendMethodSignatureTypePairs(
        LocalAggregateMethodProviderImport expected,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
        int actualMethodRow,
        ImmutableArray<TrRmStandardMethodTypeSpecPair>.Builder pairs)
    {
        var signature = actual.Methods.GetSignature(actualMethodRow);
        var (Start, Count) = actual.Signatures.GetParamRange(signature);
        if (actual.Signatures.GetReceiverTypeSpec(signature) != 0
            || actual.Signatures.GetGenericCount(signature) != 0)
        {
            throw Unsupported(
                "L1 MethodDef providers initially require nongeneric module-level Methods without receivers.");
        }

        if (expected.ExpectedTypeSpecs.Length != Count + 1)
        {
            throw Invalid(
                "An L1 provider MethodSig changes the ordered logical parameter count.");
        }

        pairs.Add(new TrRmStandardMethodTypeSpecPair(
            expected.ConsumerArtifactIndex, expected.ConsumerModuleRow,
            expected.ExpectedTypeSpecs[0], expected.ProviderArtifactIndex,
            expected.ProviderModuleRow,
            actual.Signatures.GetResultTypeSpec(signature)));
        for (var ordinal = 0; ordinal < Count; ordinal++)
        {
            var parameterRow = Start + ordinal;
            var actualFlags = actual.Parameters.GetFlags(parameterRow);
            if (actual.Parameters.GetMode(parameterRow) != 0
                || actualFlags is not (0 or 1))
            {
                throw Unsupported(
                    "L1 MethodDef providers must preserve each ordinary or implicit-readonly-reference parameter contract.");
            }
            if (actualFlags != expected.ExpectedParameterFlags[ordinal])
            {
                throw Invalid(
                    "An L1 provider MethodSig changes a parameter's implicit-reference adaptation contract.");
            }

            pairs.Add(new TrRmStandardMethodTypeSpecPair(
                expected.ConsumerArtifactIndex, expected.ConsumerModuleRow,
                expected.ExpectedTypeSpecs[ordinal + 1],
                expected.ProviderArtifactIndex, expected.ProviderModuleRow,
                actual.Parameters.GetTypeSpec(parameterRow)));
        }
    }

    private static void ValidateNoConflictingMethodProviderOrigins(
        List<LocalAggregateMethodProviderEdge> edges)
    {
        var actualByExpected = new Dictionary<
            (int ConsumerArtifactIndex, int ConsumerModuleRow,
                int ExpectedMethodRow),
            (int ProviderArtifactIndex, int ProviderModuleRow,
                int ActualMethodRow)>();
        foreach (var edge in edges)
        {
            var expected = (edge.ConsumerArtifactIndex,
                edge.ConsumerModuleRow, edge.ExpectedMethodRow);
            var actual = (edge.ProviderArtifactIndex,
                edge.ProviderModuleRow, edge.ActualMethodRow);
            if (actualByExpected.TryGetValue(expected, out var existing)
                && existing != actual)
            {
                throw Invalid(
                    "One L1 expected MethodDef selects conflicting actual provider Methods.");
            }

            actualByExpected[expected] = actual;
        }
    }
}
