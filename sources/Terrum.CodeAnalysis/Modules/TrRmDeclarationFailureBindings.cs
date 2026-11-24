// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Classifies the source of one declaration precondition failure.</summary>
public enum TrRmPreconditionFailureSource
{
    /// <summary>The source clause explicitly selected its exception class.</summary>
    ExplicitType,

    /// <summary>The source clause uses its originating Module's Core default.</summary>
    CoreDefault,
}

/// <summary>Identifies one assigned ConstructorBinding record in a captured MethodDef Body.</summary>
/// <param name="Artifact">The exact captured artifact input containing the plan.</param>
/// <param name="MethodDefRow">The one-based MethodDef row whose Body owns the record.</param>
/// <param name="ConstructorBindingRecordIndex">The zero-based record index in that Body.</param>
public sealed record TrRmConstructorBindingLocation(
    TrRmStandardArtifactInput Artifact, uint MethodDefRow,
    ushort ConstructorBindingRecordIndex);

/// <summary>Associates one retained phase-1 declaration clause with its selected failure plan.</summary>
/// <param name="ClauseArtifact">The captured artifact containing the retained clause.</param>
/// <param name="MethodDefRow">The one-based MethodDef row owning the clause.</param>
/// <param name="PhaseLocalOrdinal">The full-width phase-1 ordinal of the clause.</param>
/// <param name="OriginArtifact">The captured artifact containing the original declaration occurrence.</param>
/// <param name="OriginMethodDefRow">The one-based MethodDef row owning the original occurrence.</param>
/// <param name="OriginPhaseLocalOrdinal">The full-width phase-1 ordinal of the original occurrence.</param>
/// <param name="FailureSource">Whether the original clause selected an explicit type or Core default.</param>
/// <param name="ConstructorPlan">The exact already-selected exception construction plan.</param>
public sealed record TrRmPreconditionFailureBinding(
    TrRmStandardArtifactInput ClauseArtifact, uint MethodDefRow,
    uint PhaseLocalOrdinal, TrRmStandardArtifactInput OriginArtifact,
    uint OriginMethodDefRow, uint OriginPhaseLocalOrdinal,
    TrRmPreconditionFailureSource FailureSource,
    TrRmConstructorBindingLocation ConstructorPlan);

/// <summary>Associates one retained phase-2 declaration clause with its selected exception construction plan.</summary>
/// <param name="ClauseArtifact">The captured artifact containing the retained clause.</param>
/// <param name="MethodDefRow">The one-based MethodDef row owning the clause.</param>
/// <param name="PhaseLocalOrdinal">The full-width phase-2 ordinal of the clause.</param>
/// <param name="OriginArtifact">The captured artifact containing the original declaration occurrence.</param>
/// <param name="OriginMethodDefRow">The one-based MethodDef row owning the original occurrence.</param>
/// <param name="OriginPhaseLocalOrdinal">The full-width phase-2 ordinal of the original occurrence.</param>
/// <param name="ConstructorPlan">The exact already-selected exception construction plan.</param>
public sealed record TrRmPostconditionConstructionBinding(
    TrRmStandardArtifactInput ClauseArtifact, uint MethodDefRow,
    uint PhaseLocalOrdinal, TrRmStandardArtifactInput OriginArtifact,
    uint OriginMethodDefRow, uint OriginPhaseLocalOrdinal,
    TrRmConstructorBindingLocation ConstructorPlan);

internal static class TrRmDeclarationFailureBindingContract
{
    private readonly record struct ClauseOccurrence(
        int MethodDefRow, byte Phase, uint Ordinal, int OwnerModuleRow);

    private readonly record struct ImportEdge(
        int OwnerModuleRow, string ProviderIdentity);

    private readonly record struct MethodImport(
        int OwnerModuleRow, string ProviderIdentity, string ExportKey);

    private sealed record InputFacts(
        int InputIndex, string[] ModuleIdentities,
        int[] MethodOwnerModuleRows,
        ImmutableArray<ClauseOccurrence> Clauses,
        ImmutableArray<ImportEdge> ImportEdges,
        Dictionary<int, ImmutableArray<MethodImport>> MethodImports,
        Dictionary<(int ModuleRow, string ExportKey), uint> ExportsByKey);

    internal static bool IsValidShape(
        ImmutableArray<TrRmPreconditionFailureBinding> preconditions,
        ImmutableArray<TrRmPostconditionConstructionBinding>
            postconditions)
    {
        if (preconditions.IsDefault || postconditions.IsDefault)
        {
            return false;
        }

        foreach (var binding in preconditions)
        {
            if (binding is null
                || !IsValidClause(binding.ClauseArtifact,
                    binding.MethodDefRow)
                || !IsValidClause(binding.OriginArtifact,
                    binding.OriginMethodDefRow)
                || !IsValidConstructorPlan(binding.ConstructorPlan)
                || binding.FailureSource is not (
                    TrRmPreconditionFailureSource.ExplicitType
                    or TrRmPreconditionFailureSource.CoreDefault))
            {
                return false;
            }
        }

        foreach (var binding in postconditions)
        {
            if (binding is null
                || !IsValidClause(binding.ClauseArtifact,
                    binding.MethodDefRow)
                || !IsValidClause(binding.OriginArtifact,
                    binding.OriginMethodDefRow)
                || !IsValidConstructorPlan(binding.ConstructorPlan))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsZeroFormalConstructorBindingRecord(
        ReadOnlySpan<byte> record)
        => record.Length == 20 && record[0] == 0x0B
            && BinaryPrimitives.ReadUInt32LittleEndian(record[8..]) == 0
            && BinaryPrimitives.ReadUInt32LittleEndian(record[12..]) == 0
            && BinaryPrimitives.ReadUInt32LittleEndian(record[16..]) == 0;

    internal static (
        ImmutableArray<TrRmStandardArtifactInput> Inputs,
        ImmutableArray<TrRmPreconditionFailureBinding> Preconditions,
        ImmutableArray<TrRmPostconditionConstructionBinding> Postconditions)
        SnapshotCapturedInputs(
            TrRmStandardArtifactInput input,
            IReadOnlyList<TrRmStandardArtifactInput> providers,
            ImmutableArray<TrRmPreconditionFailureBinding> preconditions,
            ImmutableArray<TrRmPostconditionConstructionBinding>
                postconditions)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providers);
        var capturedInputs = new TrRmStandardArtifactInput[
            providers.Count + 1];
        capturedInputs[0] = input;
        for (var index = 0; index < providers.Count; index++)
        {
            capturedInputs[index + 1] = providers[index]
                ?? throw new ArgumentNullException(
                    $"{nameof(providers)}[{index}]");
        }

        if (!AreCapturedInputs(capturedInputs, preconditions,
                postconditions))
        {
            throw new ArgumentException(
                "The declaration failure-binding maps must reference unique captured inputs.");
        }

        var snapshots = new TrRmStandardArtifactInput[capturedInputs.Length];
        for (var index = 0; index < snapshots.Length; index++)
        {
            snapshots[index] = new TrRmStandardArtifactInput(
                capturedInputs[index].SchemaIdentity,
                capturedInputs[index].Artifact.ToArray());
        }

        var preconditionSnapshots
            = ImmutableArray.CreateBuilder<
                TrRmPreconditionFailureBinding>(preconditions.Length);
        foreach (var binding in preconditions)
        {
            preconditionSnapshots.Add(binding with {
                ClauseArtifact = snapshots[FindReference(capturedInputs,
                    binding.ClauseArtifact)],
                OriginArtifact = snapshots[FindReference(capturedInputs,
                    binding.OriginArtifact)],
                ConstructorPlan = binding.ConstructorPlan with {
                    Artifact = snapshots[FindReference(capturedInputs,
                        binding.ConstructorPlan.Artifact)],
                },
            });
        }

        var postconditionSnapshots
            = ImmutableArray.CreateBuilder<
                TrRmPostconditionConstructionBinding>(
                postconditions.Length);
        foreach (var binding in postconditions)
        {
            postconditionSnapshots.Add(binding with {
                ClauseArtifact = snapshots[FindReference(capturedInputs,
                    binding.ClauseArtifact)],
                OriginArtifact = snapshots[FindReference(capturedInputs,
                    binding.OriginArtifact)],
                ConstructorPlan = binding.ConstructorPlan with {
                    Artifact = snapshots[FindReference(capturedInputs,
                        binding.ConstructorPlan.Artifact)],
                },
            });
        }

        return (snapshots.ToImmutableArray(),
            preconditionSnapshots.MoveToImmutable(),
            postconditionSnapshots.MoveToImmutable());
    }

    internal static void ValidateForStandardInputs(
        TrRmStandardArtifactInput input,
        IReadOnlyList<TrRmStandardArtifactInput> providers,
        string moduleIdentity,
        ImmutableArray<CoreContractFailureIdentitySelection>
            coreSelections,
        ImmutableArray<TrRmExceptionInterfaceBinding>
            exceptionInterfaceBindings,
        ImmutableArray<TrRmPreconditionFailureBinding> preconditions,
        ImmutableArray<TrRmPostconditionConstructionBinding>
            postconditions)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        var inputs = new TrRmStandardArtifactInput[providers.Count + 1];
        inputs[0] = input;
        for (var index = 0; index < providers.Count; index++)
        {
            inputs[index + 1] = providers[index]
                ?? throw new ArgumentNullException(
                    $"{nameof(providers)}[{index}]");
        }

        if (!CoreContractFailureIdentitySelectionContract.IsValidTable(
                coreSelections)
            || !TrRmExceptionInterfaceBindingContract.IsValidTable(
                exceptionInterfaceBindings)
            || !AreCapturedInputs(inputs, preconditions, postconditions))
        {
            throw new ArgumentException(
                "The declaration failure-binding contexts are invalid.");
        }

        var schemaIdentity = input.SchemaIdentity;
        if (schemaIdentity is not (
                "trrm/1-draft-init-reference-bodies-general-predicate-graphs-1"
                or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2"))
        {
            throw new NotSupportedException(
                "Declaration failure-binding maps require exact B+I+G or no-label Construction-2 selection.");
        }

        for (var index = 1; index < inputs.Length; index++)
        {
            if (!StringComparer.Ordinal.Equals(inputs[index].SchemaIdentity,
                    schemaIdentity))
            {
                throw new NotSupportedException(
                    "Declaration failure-binding maps require one exact schema identity for every supplied input.");
            }
        }

        var facts = new InputFacts[inputs.Length];
        for (var inputIndex = 0; inputIndex < inputs.Length; inputIndex++)
        {
            if (!StringComparer.Ordinal.Equals(
                    inputs[inputIndex].SchemaIdentity, schemaIdentity))
            {
                throw new NotSupportedException(
                    "Declaration failure-binding maps require one exact schema identity for every supplied input.");
            }

            facts[inputIndex] = ReadInputFacts(inputIndex,
                inputs[inputIndex]);
        }

        var moduleLocations = new Dictionary<string, (int InputIndex,
            int ModuleRow)>(StringComparer.Ordinal);
        foreach (var inputFacts in facts)
        {
            for (var moduleRow = 1;
                moduleRow < inputFacts.ModuleIdentities.Length;
                moduleRow++)
            {
                if (!moduleLocations.TryAdd(
                        inputFacts.ModuleIdentities[moduleRow],
                        (inputFacts.InputIndex, moduleRow)))
                {
                    throw new InvalidDataException(
                        "A declaration failure-binding input set has an ambiguous Module identity.");
                }
            }
        }

        if (!moduleLocations.TryGetValue(moduleIdentity,
                out var rootModule)
            || rootModule.InputIndex != 0)
        {
            throw new InvalidDataException(
                "The selected root Module is absent from the failure-binding input set.");
        }

        var retainedModules = new HashSet<(int InputIndex, int ModuleRow)> {
            rootModule,
        };
        var pendingModules = new Queue<(int InputIndex, int ModuleRow)>();
        pendingModules.Enqueue(rootModule);
        while (pendingModules.TryDequeue(out var currentModule))
        {
            var currentFacts = facts[currentModule.InputIndex];
            foreach (var edge in currentFacts.ImportEdges)
            {
                if (edge.OwnerModuleRow != currentModule.ModuleRow)
                {
                    continue;
                }

                if (!moduleLocations.TryGetValue(edge.ProviderIdentity,
                        out var providerModule))
                {
                    throw new InvalidDataException(
                        "A retained declaration failure-binding Module imports an absent provider.");
                }

                if (retainedModules.Add(providerModule))
                {
                    pendingModules.Enqueue(providerModule);
                }
            }
        }

        foreach (var binding in exceptionInterfaceBindings)
        {
            ValidateExceptionInterfaceBinding(inputs, moduleLocations,
                binding);
        }

        var preconditionClauseKeys = new HashSet<(int InputIndex,
            int MethodDefRow, uint Ordinal)>();
        foreach (var binding in preconditions)
        {
            var clauseInputIndex = FindReference(inputs,
                binding.ClauseArtifact);
            var originInputIndex = FindReference(inputs,
                binding.OriginArtifact);
            if (clauseInputIndex < 0 || originInputIndex < 0)
            {
                throw new InvalidDataException(
                    "A precondition failure binding refers to an uncaptured artifact input.");
            }

            var clauseOwner = GetMethodOwner(facts[clauseInputIndex],
                binding.MethodDefRow);
            var originOwner = GetMethodOwner(facts[originInputIndex],
                binding.OriginMethodDefRow);
            var planInputIndex = FindReference(inputs,
                binding.ConstructorPlan.Artifact);
            if (planInputIndex < 0)
            {
                throw new InvalidDataException(
                    "A precondition construction plan is not a captured input.");
            }

            var planOwner = GetMethodOwner(facts[planInputIndex],
                binding.ConstructorPlan.MethodDefRow);
            if (!retainedModules.Contains((clauseInputIndex, clauseOwner))
                || !retainedModules.Contains((originInputIndex, originOwner))
                || !retainedModules.Contains((planInputIndex, planOwner)))
            {
                throw new InvalidDataException(
                    "A declaration failure binding refers to a Module outside the selected root's provider closure.");
            }
            if (!preconditionClauseKeys.Add((clauseInputIndex,
                    checked((int)binding.MethodDefRow),
                    binding.PhaseLocalOrdinal)))
            {
                throw new InvalidDataException(
                    "A phase-1 declaration occurrence has duplicate failure bindings.");
            }
        }

        var postconditionClauseKeys = new HashSet<(int InputIndex,
            int MethodDefRow, uint Ordinal)>();
        foreach (var binding in postconditions)
        {
            var clauseInputIndex = FindReference(inputs,
                binding.ClauseArtifact);
            var originInputIndex = FindReference(inputs,
                binding.OriginArtifact);
            if (clauseInputIndex < 0 || originInputIndex < 0)
            {
                throw new InvalidDataException(
                    "A postcondition construction binding refers to an uncaptured artifact input.");
            }

            var clauseOwner = GetMethodOwner(facts[clauseInputIndex],
                binding.MethodDefRow);
            var originOwner = GetMethodOwner(facts[originInputIndex],
                binding.OriginMethodDefRow);
            var planInputIndex = FindReference(inputs,
                binding.ConstructorPlan.Artifact);
            if (planInputIndex < 0)
            {
                throw new InvalidDataException(
                    "A postcondition construction plan is not a captured input.");
            }

            var planOwner = GetMethodOwner(facts[planInputIndex],
                binding.ConstructorPlan.MethodDefRow);
            if (!retainedModules.Contains((clauseInputIndex, clauseOwner))
                || !retainedModules.Contains((originInputIndex, originOwner))
                || !retainedModules.Contains((planInputIndex, planOwner)))
            {
                throw new InvalidDataException(
                    "A declaration failure binding refers to a Module outside the selected root's provider closure.");
            }
            if (!postconditionClauseKeys.Add((clauseInputIndex,
                    checked((int)binding.MethodDefRow),
                    binding.PhaseLocalOrdinal)))
            {
                throw new InvalidDataException(
                    "A phase-2 declaration occurrence has duplicate construction bindings.");
            }
        }

        var expectedPreconditionKeys = new HashSet<(int InputIndex,
            int MethodDefRow, uint Ordinal)>();
        var expectedPostconditionKeys = new HashSet<(int InputIndex,
            int MethodDefRow, uint Ordinal)>();
        foreach (var inputFacts in facts)
        {
            foreach (var clause in inputFacts.Clauses)
            {
                if (!retainedModules.Contains((inputFacts.InputIndex,
                        clause.OwnerModuleRow)))
                {
                    continue;
                }

                var key = (inputFacts.InputIndex, clause.MethodDefRow,
                    clause.Ordinal);
                if (clause.Phase == 1
                    && !expectedPreconditionKeys.Add(key))
                {
                    throw new InvalidDataException(
                        "Retained phase-1 declaration attachments are ambiguous.");
                }
                if (clause.Phase == 2
                    && !expectedPostconditionKeys.Add(key))
                {
                    throw new InvalidDataException(
                        "Retained phase-2 declaration attachments are ambiguous.");
                }
            }
        }

        if (!expectedPreconditionKeys.SetEquals(preconditionClauseKeys)
            || !expectedPostconditionKeys.SetEquals(
                postconditionClauseKeys))
        {
            throw new InvalidDataException(
                "Declaration failure-binding maps must cover every retained phase-1 and phase-2 occurrence exactly once.");
        }

        foreach (var binding in preconditions)
        {
            ValidateRequiredExceptionInterfaceAnchor(facts, inputs,
                moduleLocations, retainedModules, exceptionInterfaceBindings,
                binding.OriginArtifact, binding.OriginMethodDefRow);
            ValidateOriginAndPlan(facts, inputs, coreSelections,
                exceptionInterfaceBindings,
                binding.ClauseArtifact, binding.MethodDefRow,
                binding.PhaseLocalOrdinal, binding.OriginArtifact,
                binding.OriginMethodDefRow,
                binding.OriginPhaseLocalOrdinal,
                binding.ConstructorPlan,
                binding.FailureSource
                    == TrRmPreconditionFailureSource.CoreDefault);
        }

        foreach (var binding in postconditions)
        {
            ValidateRequiredExceptionInterfaceAnchor(facts, inputs,
                moduleLocations, retainedModules, exceptionInterfaceBindings,
                binding.OriginArtifact, binding.OriginMethodDefRow);
            ValidateOriginAndPlan(facts, inputs, coreSelections,
                exceptionInterfaceBindings,
                binding.ClauseArtifact, binding.MethodDefRow,
                binding.PhaseLocalOrdinal, binding.OriginArtifact,
                binding.OriginMethodDefRow,
                binding.OriginPhaseLocalOrdinal,
                binding.ConstructorPlan, requirePostcondition: true);
        }
    }

    private static void ValidateExceptionInterfaceBinding(
        IReadOnlyList<TrRmStandardArtifactInput> inputs,
        IReadOnlyDictionary<string, (int InputIndex, int ModuleRow)>
            moduleLocations,
        TrRmExceptionInterfaceBinding binding)
    {
        if (!moduleLocations.ContainsKey(binding.UsingModuleIdentity)
            || !moduleLocations.TryGetValue(binding.ProviderIdentity,
                out var provider))
        {
            throw new InvalidDataException(
                "A Core exception-interface binding names a Module absent from the captured input set.");
        }

        var artifact = inputs[provider.InputIndex];
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                artifact.Artifact.ToArray(),
                TrRmExecutableCompiler.GetForeignInvocationSchema(
                    artifact.SchemaIdentity),
                standardSchemaIdentity: artifact.SchemaIdentity));
        if (!tables.Exports.TryGetDefinitionForKey(provider.ModuleRow,
                binding.ExportKey, out var interfaceDefinition)
            || (interfaceDefinition >> 24) != 0x13)
        {
            throw new InvalidDataException(
                "A Core exception-interface ExportKey does not select a public TypeDef.");
        }

        var typeDefRow = GetTypeDefRow(interfaceDefinition);
        if (tables.TypeDefs.GetKind(typeDefRow) != 3)
        {
            throw new InvalidDataException(
                "A Core exception-interface anchor must select an interface TypeDef.");
        }

        var interfaceDefRow
            = tables.Interfaces.GetInterfaceDefForTypeDef(typeDefRow);
        var (firstFormal, formalCount)
            = tables.TypeDefs.GetGenericRange(typeDefRow);
        var selfFormal
            = tables.Interfaces.GetSelfGenericParam(interfaceDefRow);
        if (interfaceDefRow == 0 || formalCount != 1
            || selfFormal != firstFormal
            || tables.GenericParams.GetOwnerDefId(selfFormal)
                != interfaceDefinition
            || tables.GenericParams.GetOrdinal(selfFormal) != 0
            || tables.GenericParams.GetFormalKind(selfFormal) != 1)
        {
            throw new InvalidDataException(
                "The Core exception-interface anchor must have exactly one owned self Type formal.");
        }
    }

    private static void ValidateRequiredExceptionInterfaceAnchor(
        InputFacts[] facts,
        IReadOnlyList<TrRmStandardArtifactInput> inputs,
        IReadOnlyDictionary<string, (int InputIndex, int ModuleRow)>
            moduleLocations,
        HashSet<(int InputIndex, int ModuleRow)> retainedModules,
        ImmutableArray<TrRmExceptionInterfaceBinding> bindings,
        TrRmStandardArtifactInput originArtifact, uint originMethodDefRow)
    {
        var originInputIndex = FindReference(inputs, originArtifact);
        var originModuleRow = GetMethodOwner(facts[originInputIndex],
            originMethodDefRow);
        var usingModuleIdentity
            = facts[originInputIndex].ModuleIdentities[originModuleRow];
        var binding = bindings.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.UsingModuleIdentity,
                usingModuleIdentity));
        if (binding is null
            || !moduleLocations.TryGetValue(binding.ProviderIdentity,
                out var provider)
            || !retainedModules.Contains(provider))
        {
            throw new InvalidDataException(
                "A retained declaration failure clause requires its originating Module's selected Core exception-interface provider in the retained closure.");
        }
    }

    internal static bool HasRetainedPhaseFailureAttachments(
        IReadOnlyList<TrRmStandardArtifactInput> inputs,
        string moduleIdentity)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        if (inputs.Count == 0 || inputs[0] is null)
        {
            throw new ArgumentException(
                "The declaration predicate input set is empty.",
                nameof(inputs));
        }

        var facts = new InputFacts[inputs.Count];
        var moduleLocations = new Dictionary<string, (int InputIndex,
            int ModuleRow)>(StringComparer.Ordinal);
        for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
        {
            var input = inputs[inputIndex]
                ?? throw new ArgumentNullException(
                    $"{nameof(inputs)}[{inputIndex}]");
            facts[inputIndex] = ReadInputFacts(inputIndex, input);
            for (var moduleRow = 1;
                moduleRow < facts[inputIndex].ModuleIdentities.Length;
                moduleRow++)
            {
                if (!moduleLocations.TryAdd(
                        facts[inputIndex].ModuleIdentities[moduleRow],
                        (inputIndex, moduleRow)))
                {
                    throw new InvalidDataException(
                        "A declaration predicate input set has an ambiguous Module identity.");
                }
            }
        }

        if (!moduleLocations.TryGetValue(moduleIdentity,
                out var rootModule)
            || rootModule.InputIndex != 0)
        {
            throw new InvalidDataException(
                "The selected root Module is absent from the declaration predicate input set.");
        }

        var retainedModules = new HashSet<(int InputIndex, int ModuleRow)> {
            rootModule,
        };
        var pendingModules = new Queue<(int InputIndex, int ModuleRow)>();
        pendingModules.Enqueue(rootModule);
        while (pendingModules.TryDequeue(out var currentModule))
        {
            foreach (var edge in facts[currentModule.InputIndex].ImportEdges)
            {
                if (edge.OwnerModuleRow != currentModule.ModuleRow)
                {
                    continue;
                }

                if (!moduleLocations.TryGetValue(edge.ProviderIdentity,
                        out var providerModule))
                {
                    throw new InvalidDataException(
                        "A retained declaration predicate Module imports an absent provider.");
                }

                if (retainedModules.Add(providerModule))
                {
                    pendingModules.Enqueue(providerModule);
                }
            }
        }

        return facts.Any(inputFacts =>
            inputFacts.Clauses.Any(clause =>
                clause.Phase is 1 or 2
                && retainedModules.Contains((inputFacts.InputIndex,
                    clause.OwnerModuleRow))));
    }

    private static InputFacts ReadInputFacts(int inputIndex,
        TrRmStandardArtifactInput input)
    {
        var bytes = input.Artifact.ToArray();
        var schema = TrRmExecutableCompiler.GetForeignInvocationSchema(
            input.SchemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(bytes,
                schema, standardSchemaIdentity: input.SchemaIdentity));
        var moduleIdentities = new string[tables.Modules.Count + 1];
        for (var moduleRow = 1;
            moduleRow < moduleIdentities.Length; moduleRow++)
        {
            moduleIdentities[moduleRow]
                = tables.Modules.GetIdentity(moduleRow);
        }

        var methodOwnerModuleRows = new int[tables.Methods.Count + 1];
        var clauses = ImmutableArray.CreateBuilder<ClauseOccurrence>();
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            var ownerModuleRow
                = tables.Methods.GetOwnerModule(methodRow);
            methodOwnerModuleRows[methodRow] = ownerModuleRow;
            foreach (var clause in tables.Contracts
                .GetPredicateGraphContracts(methodRow))
            {
                if (clause.PhaseTag is 1 or 2)
                {
                    clauses.Add(new ClauseOccurrence(methodRow,
                        clause.PhaseTag,
                        clause.PhaseLocalOrdinal, ownerModuleRow));
                }
            }
        }

        var importEdges = ImmutableArray.CreateBuilder<ImportEdge>();
        var methodImports
            = new Dictionary<int, ImmutableArray<MethodImport>.Builder>();
        for (var importRow = 1; importRow <= tables.Imports.Count;
            importRow++)
        {
            var moduleRefRow
                = tables.Imports.GetModuleRefForImportRow(importRow);
            var expectedDefinition
                = tables.Imports.GetExpectedDefinitionForImportRow(importRow);
            var ownerModuleRow
                = tables.ModuleRefs.GetOwnerModule(moduleRefRow);
            importEdges.Add(new ImportEdge(ownerModuleRow,
                tables.ModuleRefs.GetProducerIdentity(moduleRefRow)));
            if ((expectedDefinition >> 24) == 0x16)
            {
                var methodDefRow = checked((int)(
                    expectedDefinition & 0x00FF_FFFF));
                if (!methodImports.TryGetValue(methodDefRow,
                        out var bindings))
                {
                    bindings = ImmutableArray.CreateBuilder<MethodImport>();
                    methodImports.Add(methodDefRow, bindings);
                }

                var binding
                    = tables.Imports.GetBindingForImportRow(importRow);
                bindings.Add(new MethodImport(ownerModuleRow,
                    binding.ProducerIdentity, binding.ExportKey));
            }
        }

        var exportKeys = new Dictionary<(int ModuleRow, string ExportKey),
            uint>();
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            foreach (var export in tables.Exports
                .GetExportsForModule(moduleRow))
            {
                exportKeys.Add((moduleRow, export.ExportKey),
                    export.Definition);
            }
        }

        var immutableMethodImports
            = new Dictionary<int, ImmutableArray<MethodImport>>();
        foreach (var (methodDefRow, bindings) in methodImports)
        {
            immutableMethodImports.Add(methodDefRow, bindings.ToImmutable());
        }

        return new InputFacts(inputIndex, moduleIdentities,
            methodOwnerModuleRows, clauses.ToImmutable(),
            importEdges.ToImmutable(), immutableMethodImports, exportKeys);
    }

    private static int GetMethodOwner(InputFacts facts,
        uint methodDefRow)
    {
        if (methodDefRow == 0
            || methodDefRow >= (uint)facts.MethodOwnerModuleRows.Length)
        {
            throw new InvalidDataException(
                "A declaration failure-binding MethodDef row is out of range.");
        }

        return facts.MethodOwnerModuleRows[checked((int)methodDefRow)];
    }

    private static void ValidateOriginAndPlan(
        InputFacts[] facts,
        IReadOnlyList<TrRmStandardArtifactInput> inputs,
        ImmutableArray<CoreContractFailureIdentitySelection>
            coreSelections,
        ImmutableArray<TrRmExceptionInterfaceBinding>
            exceptionInterfaceBindings,
        TrRmStandardArtifactInput clauseArtifact, uint clauseMethodDefRow,
        uint clauseOrdinal, TrRmStandardArtifactInput originArtifact,
        uint originMethodDefRow, uint originOrdinal,
        TrRmConstructorBindingLocation plan,
        bool requireRequirementException = false,
        bool requirePostcondition = false)
    {
        var clauseInputIndex = FindReference(inputs, clauseArtifact);
        var originInputIndex = FindReference(inputs, originArtifact);
        var planInputIndex = FindReference(inputs, plan.Artifact);
        var clause = FindClause(facts[clauseInputIndex],
            checked((int)clauseMethodDefRow), clauseOrdinal,
            requirePostcondition ? (byte)2 : (byte)1);
        var origin = FindClause(facts[originInputIndex],
            checked((int)originMethodDefRow), originOrdinal,
            requirePostcondition ? (byte)2 : (byte)1);
        if (clause.Phase != origin.Phase)
        {
            throw new InvalidDataException(
                "A declaration failure-binding origin must use the same phase as its clause.");
        }

        ValidateOriginCorrespondence(facts, clauseInputIndex,
            checked((int)clauseMethodDefRow), clauseOrdinal,
            originInputIndex, checked((int)originMethodDefRow),
            originOrdinal, origin.OwnerModuleRow);

        var originModuleIdentity
            = facts[originInputIndex].ModuleIdentities[
                origin.OwnerModuleRow];
        if (!exceptionInterfaceBindings.Any(binding =>
                StringComparer.Ordinal.Equals(
                    binding.UsingModuleIdentity, originModuleIdentity)))
        {
            throw new InvalidDataException(
                "A retained declaration failure clause has no exact originating-Module IException interface anchor.");
        }

        var role = coreSelections.FirstOrDefault(selection =>
            StringComparer.Ordinal.Equals(
                selection.UsingModuleIdentity, originModuleIdentity));
        var selectedRole = requireRequirementException
            ? role?.RequirementExceptionClass
            : requirePostcondition
                ? role?.PostconditionExceptionClass : null;
        if ((requireRequirementException || requirePostcondition)
            && (role is null || selectedRole is null))
        {
            throw new InvalidDataException(
                "A Core-default or postcondition binding has no exact originating-Module role selection.");
        }

        var planInput = inputs[planInputIndex];
        var planBytes = planInput.Artifact.ToArray();
        var schema = TrRmExecutableCompiler.GetForeignInvocationSchema(
            planInput.SchemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                planBytes, schema,
                standardSchemaIdentity: planInput.SchemaIdentity));
        var methodRow = checked((int)plan.MethodDefRow);
        if (methodRow <= 0 || methodRow > tables.Methods.Count)
        {
            throw new InvalidDataException(
                "A declaration failure-binding plan MethodDef is out of range.");
        }

        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        if (bodyRow == 0)
        {
            throw new InvalidDataException(
                "A declaration failure-binding plan MethodDef has no Body.");
        }

        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count,
            generalPredicateGraphsInitializationReferenceBodies:
                tables.Schema.UsesGeneralPredicateGraphs());
        var recordIndex = (int)plan.ConstructorBindingRecordIndex;
        if (recordIndex >= body.RecordCount)
        {
            throw new InvalidDataException(
                "A declaration failure-binding ConstructorBinding index is out of range.");
        }

        var record = body.GetRecord(recordIndex);
        if (!IsZeroFormalConstructorBindingRecord(record))
        {
            throw new InvalidDataException(
                "A declaration failure-binding plan requires the assigned zero-formal ConstructorBinding record 0B.");
        }

        var methodMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            record[4..]);
        var exceptionTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        if (methodMapIndex >= body.MethodCount
            || exceptionTypeMapIndex >= body.TypeCount)
        {
            throw new InvalidDataException(
                "A declaration failure-binding ConstructorBinding has an out-of-range target or exception TypeMap.");
        }

        var exceptionTypeMapToken
            = BinaryPrimitives.ReadUInt32LittleEndian(body.TypeMap.Slice(
                exceptionTypeMapIndex * sizeof(uint), sizeof(uint)));
        if ((exceptionTypeMapToken >> 24) != 0x11)
        {
            throw new InvalidDataException(
                "A declaration failure-binding exception TypeMap must be a typed TypeSpec reference.");
        }

        var exceptionTypeSpecRef = exceptionTypeMapToken & 0x00FF_FFFF;
        var exceptionType = tables.TypeSpecs.GetTypeSpecRef(
            exceptionTypeSpecRef);
        if (!exceptionType.IsRow
            || exceptionType.Form is not (0x02 or 0x03)
            || exceptionType.Qualifiers != 0)
        {
            throw new InvalidDataException(
                "A declaration failure-binding exception TypeSpec must be a concrete readonly class.");
        }

        var exceptionTypeDefRow = tables.TypeSpecs
            .GetNominalTypeDefRow(exceptionTypeSpecRef, tables.TypeDefs,
                tables.Imports);
        if (tables.TypeDefs.GetKind(exceptionTypeDefRow) != 2
            || tables.TypeDefs.GetGenericRange(exceptionTypeDefRow).Count
                != 0)
        {
            throw new InvalidDataException(
                "A declaration failure-binding exception TypeSpec is not a nongeneric class.");
        }

        var methodMapToken
            = BinaryPrimitives.ReadUInt32LittleEndian(body.MethodMap.Slice(
                methodMapIndex * sizeof(uint), sizeof(uint)));
        var targetDefinition = (methodMapToken >> 24) switch {
            0x16 => methodMapToken,
            0x1C => tables.Imports.GetExpectedDefinitionForImportRow(
                checked((int)(methodMapToken & 0x00FF_FFFF))),
            _ => 0u,
        };
        if ((targetDefinition >> 24) != 0x16
            || (targetDefinition & 0x00FF_FFFF) == 0)
        {
            throw new InvalidDataException(
                "A declaration failure-binding ConstructorBinding target is not a MethodDef.");
        }

        var targetMethodRow = checked((int)(
            targetDefinition & 0x00FF_FFFF));
        var targetSignature = tables.Methods.GetSignature(targetMethodRow);
        var (_, targetFormalCount)
            = tables.Signatures.GetParamRange(targetSignature);
        var targetOwnerTypeDefRow
            = tables.Methods.GetOwnerType(targetMethodRow);
        var targetConstruction
            = tables.Contracts.GetConstructionClause(targetMethodRow);
        if (targetFormalCount != 0 || targetConstruction.RoleTag != 2
            || targetOwnerTypeDefRow != exceptionTypeDefRow
            || !tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                tables.Signatures.GetResultTypeSpec(targetSignature),
                exceptionTypeSpecRef))
        {
            throw new InvalidDataException(
                "A declaration failure-binding ConstructorBinding does not select the exception class's exact projection.");
        }

        if (selectedRole is CoreContractFailureDeclarationIdentity identity
            && !MatchesSelectedClass(tables, exceptionType,
                exceptionTypeDefRow, identity))
        {
            throw new InvalidDataException(
                "The ConstructorBinding exception class differs from the originating Module's selected Core role.");
        }

        ValidateExceptionClassWitness(facts, inputs,
            exceptionInterfaceBindings, originModuleIdentity,
            planInputIndex, tables, exceptionType,
            exceptionTypeDefRow);
    }

    private static void ValidateExceptionClassWitness(
        InputFacts[] facts,
        IReadOnlyList<TrRmStandardArtifactInput> inputs,
        ImmutableArray<TrRmExceptionInterfaceBinding> exceptionBindings,
        string originModuleIdentity, int planInputIndex,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables
            planTables,
        TrRmTypeSpecRefInfo exceptionType, int exceptionTypeDefRow)
    {
        var exceptionBinding = exceptionBindings.FirstOrDefault(binding =>
            StringComparer.Ordinal.Equals(binding.UsingModuleIdentity,
                originModuleIdentity));
        if (exceptionBinding is null)
        {
            throw new InvalidDataException(
                "The originating Module has no selected IException interface anchor.");
        }

        var classProviderIdentity = exceptionType.Form == 0x03
            ? planTables.Imports.GetBindingForImportRow(
                planTables.TypeSpecs.GetImportRow(exceptionType.Value))
                .ProducerIdentity
            : planTables.Modules.GetIdentity(
                planTables.TypeDefs.GetOwnerModule(exceptionTypeDefRow));
        var classProvider = FindModule(facts, classProviderIdentity);
        var classTables = ReadExecutionTables(inputs[classProvider.InputIndex]);
        var classTypeDefRow = exceptionType.Form == 0x02
            ? exceptionTypeDefRow
            : ResolveClassTypeDef(classTables, classProvider.ModuleRow,
                planTables.Imports.GetBindingForImportRow(
                    planTables.TypeSpecs.GetImportRow(exceptionType.Value))
                    .ExportKey);
        if (classTables.TypeDefs.GetKind(classTypeDefRow) != 2
            || classTables.TypeDefs.GetGenericRange(classTypeDefRow).Count
                != 0)
        {
            throw new InvalidDataException(
                "The selected exception provider does not publish the expected concrete nongeneric class TypeDef.");
        }

        var witnessCount = 0;
        for (var witnessRow = 1;
            witnessRow <= classTables.InterfaceWitnesses.Count;
            witnessRow++)
        {
            var implementingTypeSpecRef
                = classTables.InterfaceWitnesses
                    .GetImplementingTypeSpecRef(witnessRow);
            var implementingTypeDefRow
                = classTables.TypeSpecs.GetNominalTypeDefRow(
                    implementingTypeSpecRef, classTables.TypeDefs,
                    classTables.Imports);
            if (implementingTypeDefRow != classTypeDefRow)
            {
                continue;
            }

            var interfaceTypeSpecRef
                = classTables.InterfaceWitnesses
                    .GetInterfaceTypeSpecRef(witnessRow);
            var interfaceType = classTables.TypeSpecs.GetTypeSpecRef(
                interfaceTypeSpecRef);
            if (!interfaceType.IsRow || interfaceType.Form != 0x05
                || interfaceType.Qualifiers != 0
                || classTables.TypeSpecs.GetConstructedArgumentCount(
                    interfaceType.Row) != 1)
            {
                continue;
            }

            var interfaceRow
                = classTables.TypeSpecs.GetRow(interfaceType.Row);
            var nominalInterfaceTypeSpecRef
                = BinaryPrimitives.ReadUInt32LittleEndian(
                    interfaceRow[2..]);
            var selfTypeSpecRef
                = classTables.TypeSpecs.GetConstructedArgument(
                    interfaceType.Row, 0);
            var selfType = classTables.TypeSpecs.GetTypeSpecRef(
                selfTypeSpecRef);
            if (!selfType.IsRow
                || selfType.Form is not (0x02 or 0x03)
                || selfType.Qualifiers != 0
                || classTables.TypeSpecs.GetNominalTypeDefRow(
                    selfTypeSpecRef, classTables.TypeDefs,
                    classTables.Imports) != classTypeDefRow
                || !MatchesSelectedInterface(classTables,
                    nominalInterfaceTypeSpecRef, exceptionBinding))
            {
                continue;
            }

            var interfaceTypeDefRow
                = classTables.TypeSpecs.GetNominalTypeDefRow(
                    nominalInterfaceTypeSpecRef, classTables.TypeDefs,
                    classTables.Imports);
            var interfaceDefRow
                = classTables.Interfaces.GetInterfaceDefForTypeDef(
                    interfaceTypeDefRow);
            var (_, baseCount)
                = classTables.Interfaces.GetBaseRange(interfaceDefRow);
            if (baseCount != 0)
            {
                throw new NotSupportedException(
                    "The selected Core exception-interface witness has inherited requirements outside the current validator subset.");
            }

            var (methodFirst, methodCount)
                = classTables.Interfaces.GetMethodRange(interfaceDefRow);
            var (memberFirst, memberCount)
                = classTables.InterfaceWitnesses.GetMemberRange(witnessRow);
            if (memberCount != methodCount)
            {
                throw new InvalidDataException(
                    "The selected exception-class witness does not cover every direct IException requirement.");
            }

            for (var methodOrdinal = 0; methodOrdinal < methodCount;
                methodOrdinal++)
            {
                var requiredMethod = classTables.InterfaceMethods
                    .GetMethodDefRow(methodFirst + methodOrdinal);
                var matchingBindings = 0;
                var matchingMemberRow = 0;
                for (var memberOrdinal = 0; memberOrdinal < memberCount;
                    memberOrdinal++)
                {
                    var memberRow = memberFirst + memberOrdinal;
                    if (classTables.WitnessMembers
                            .GetRequirementMethodDef(memberRow)
                            == requiredMethod
                        && classTables.TypeSpecs.AreEquivalentTypeSpecRefs(
                            classTables.WitnessMembers
                                .GetDeclaringInterfaceTypeSpecRef(memberRow),
                            interfaceTypeSpecRef))
                    {
                        matchingBindings++;
                        matchingMemberRow = memberRow;
                    }
                }

                if (matchingBindings != 1)
                {
                    throw new InvalidDataException(
                        "The selected exception-class witness has a missing or ambiguous direct IException requirement binding.");
                }

                var selectedMethod = classTables.WitnessMembers
                    .GetSelectedMethodDef(matchingMemberRow);
                var selectedOwnerRef = classTables.WitnessMembers
                    .GetSelectedOwnerTypeSpecRef(matchingMemberRow);
                var selectedOwnerTypeDef = selectedOwnerRef == 0
                    ? classTypeDefRow
                    : classTables.TypeSpecs.GetNominalTypeDefRow(
                        selectedOwnerRef, classTables.TypeDefs,
                        classTables.Imports);
                var requirementSignature = classTables.Methods.GetSignature(
                    requiredMethod);
                var selectedSignature = classTables.Methods.GetSignature(
                    selectedMethod);
                var (_, requirementParameterCount)
                    = classTables.Signatures.GetParamRange(
                        requirementSignature);
                var (_, selectedParameterCount)
                    = classTables.Signatures.GetParamRange(
                        selectedSignature);
                if (classTables.Methods.GetOwnerType(selectedMethod)
                        != selectedOwnerTypeDef
                    || (selectedOwnerRef == 0
                        && selectedOwnerTypeDef != classTypeDefRow)
                    || (selectedOwnerRef != 0
                        && classTables.TypeDefs.GetKind(
                            selectedOwnerTypeDef) is not (2 or 3))
                    || (selectedOwnerTypeDef != classTypeDefRow
                        && selectedOwnerTypeDef != interfaceTypeDefRow)
                    || requirementParameterCount != selectedParameterCount
                    || classTables.Signatures.GetGenericCount(
                        requirementSignature)
                        != classTables.Signatures.GetGenericCount(
                            selectedSignature))
                {
                    throw new InvalidDataException(
                        "A selected exception witness MethodDef has an incompatible owner or callable arity.");
                }
            }

            witnessCount++;
        }

        if (witnessCount != 1)
        {
            throw new InvalidDataException(
                "The selected exception class does not have exactly one self-typed Core IException witness.");
        }
    }

    private static (int InputIndex, int ModuleRow) FindModule(
        InputFacts[] facts, string moduleIdentity)
    {
        for (var inputIndex = 0; inputIndex < facts.Length; inputIndex++)
        {
            for (var moduleRow = 1;
                moduleRow < facts[inputIndex].ModuleIdentities.Length;
                moduleRow++)
            {
                if (StringComparer.Ordinal.Equals(
                        facts[inputIndex].ModuleIdentities[moduleRow],
                        moduleIdentity))
                {
                    return (inputIndex, moduleRow);
                }
            }
        }

        throw new InvalidDataException(
            "A selected exception provider Module is absent from the captured input set.");
    }

    private static int ResolveClassTypeDef(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int providerModuleRow, string exportKey)
    {
        if (!tables.Exports.TryGetDefinitionForKey(providerModuleRow,
                exportKey, out var definitionId)
            || (definitionId >> 24) != 0x13)
        {
            throw new InvalidDataException(
                "The selected exception class ExportKey is not a public TypeDef.");
        }

        return GetTypeDefRow(definitionId);
    }

    private static bool MatchesSelectedInterface(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        uint interfaceTypeSpecRef,
        TrRmExceptionInterfaceBinding binding)
    {
        var interfaceType = tables.TypeSpecs.GetTypeSpecRef(
            interfaceTypeSpecRef);
        if (!interfaceType.IsRow
            || interfaceType.Form is not (0x02 or 0x03)
            || interfaceType.Qualifiers != 0)
        {
            return false;
        }

        var typeDefRow = tables.TypeSpecs.GetNominalTypeDefRow(
            interfaceTypeSpecRef, tables.TypeDefs, tables.Imports);
        if (tables.TypeDefs.GetKind(typeDefRow) != 3)
        {
            return false;
        }

        if (interfaceType.Form == 0x03)
        {
            var import = tables.Imports.GetBindingForImportRow(
                tables.TypeSpecs.GetImportRow(interfaceTypeSpecRef));
            return StringComparer.Ordinal.Equals(
                    import.ProducerIdentity, binding.ProviderIdentity)
                && StringComparer.Ordinal.Equals(import.ExportKey,
                    binding.ExportKey);
        }

        var ownerModule = tables.TypeDefs.GetOwnerModule(typeDefRow);
        var expectedDefinitionId = 0x1300_0000u
            | checked((uint)typeDefRow);
        return StringComparer.Ordinal.Equals(
                tables.Modules.GetIdentity(ownerModule),
                binding.ProviderIdentity)
            && tables.Exports.TryGetDefinitionForKey(ownerModule,
                binding.ExportKey, out var definitionId)
            && definitionId == expectedDefinitionId;
    }

    private static TrRmStructuralMetadataValidator.ValidatedExecutionTables
        ReadExecutionTables(TrRmStandardArtifactInput input)
    {
        var schema = TrRmExecutableCompiler.GetForeignInvocationSchema(
            input.SchemaIdentity);
        return TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                input.Artifact.ToArray(), schema,
                standardSchemaIdentity: input.SchemaIdentity));
    }

    private static bool MatchesSelectedClass(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        TrRmTypeSpecRefInfo exceptionType, int typeDefRow,
        CoreContractFailureDeclarationIdentity identity)
    {
        if (exceptionType.Form == 0x02)
        {
            var ownerModule = tables.TypeDefs.GetOwnerModule(typeDefRow);
            var expectedDefinitionId = 0x1300_0000u
                | checked((uint)typeDefRow);
            return StringComparer.Ordinal.Equals(
                    tables.Modules.GetIdentity(ownerModule),
                    identity.ProviderIdentity)
                && tables.Exports.TryGetDefinitionForKey(ownerModule,
                    identity.ExportKey, out var definitionId)
                && definitionId == expectedDefinitionId;
        }

        if (exceptionType.Form != 0x03)
        {
            return false;
        }

        private static void ValidateOriginCorrespondence(InputFacts[] facts,
            int clauseInputIndex, int clauseMethodDefRow, uint clauseOrdinal,
            int originInputIndex, int originMethodDefRow, uint originOrdinal,
            int originModuleRow)
        {
            if (clauseInputIndex == originInputIndex
                && clauseMethodDefRow == originMethodDefRow
                && clauseOrdinal == originOrdinal)
            {
                return;
            }

            if (!facts[clauseInputIndex].MethodImports.TryGetValue(
                    clauseMethodDefRow, out var methodImports))
            {
                throw new InvalidDataException(
                    "A non-local declaration failure-binding origin has no exact imported MethodDef correspondence.");
            }

            var originFacts = facts[originInputIndex];
            var originModuleIdentity
                = originFacts.ModuleIdentities[originModuleRow];
            var clauseOwnerModuleRow
                = facts[clauseInputIndex]
                    .MethodOwnerModuleRows[clauseMethodDefRow];
            var expectedDefinition = 0x1600_0000u
                | checked((uint)originMethodDefRow);
            var clauseImports = methodImports
                .Where(import => import.OwnerModuleRow == clauseOwnerModuleRow)
                .Distinct()
                .ToArray();
            if (clauseImports.Length != 1
                || !StringComparer.Ordinal.Equals(
                    clauseImports[0].ProviderIdentity, originModuleIdentity)
                || !originFacts.ExportsByKey.TryGetValue(
                    (originModuleRow, clauseImports[0].ExportKey),
                    out var exportedDefinition)
                || exportedDefinition != expectedDefinition)
            {
                throw new InvalidDataException(
                    "A declaration failure-binding origin does not match the clause's unique exact public MethodDef import.");
            }
        }

        var binding = tables.Imports.GetBindingForImportRow(
            tables.TypeSpecs.GetImportRow(exceptionType.Value));
        return StringComparer.Ordinal.Equals(binding.ProducerIdentity,
                identity.ProviderIdentity)
            && StringComparer.Ordinal.Equals(binding.ExportKey,
                identity.ExportKey);
    }

    private static ClauseOccurrence FindClause(InputFacts facts,
        int methodDefRow, uint ordinal, byte phase)
    {
        foreach (var clause in facts.Clauses)
        {
            if (clause.MethodDefRow == methodDefRow
                && clause.Ordinal == ordinal && clause.Phase == phase)
            {
                return clause;
            }
        }

        throw new InvalidDataException(
            "A declaration failure-binding map refers to a missing clause attachment.");
    }

    internal static bool AreCapturedInputs(
        IReadOnlyList<TrRmStandardArtifactInput> inputs,
        ImmutableArray<TrRmPreconditionFailureBinding> preconditions,
        ImmutableArray<TrRmPostconditionConstructionBinding>
            postconditions)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0
            || !IsValidShape(preconditions, postconditions))
        {
            return false;
        }

        for (var index = 0; index < inputs.Count; index++)
        {
            if (inputs[index] is null)
            {
                return false;
            }

            for (var previous = 0; previous < index; previous++)
            {
                if (ReferenceEquals(inputs[index], inputs[previous]))
                {
                    return false;
                }
            }
        }

        var preconditionKeys = new HashSet<(int ArtifactIndex,
            uint MethodDefRow, uint Ordinal)>();
        foreach (var binding in preconditions)
        {
            var clauseIndex = FindReference(inputs,
                binding.ClauseArtifact);
            if (clauseIndex < 0
                || FindReference(inputs, binding.OriginArtifact) < 0
                || FindReference(inputs,
                    binding.ConstructorPlan.Artifact) < 0
                || !preconditionKeys.Add((clauseIndex,
                    binding.MethodDefRow, binding.PhaseLocalOrdinal)))
            {
                return false;
            }
        }

        var postconditionKeys = new HashSet<(int ArtifactIndex,
            uint MethodDefRow, uint Ordinal)>();
        foreach (var binding in postconditions)
        {
            var clauseIndex = FindReference(inputs,
                binding.ClauseArtifact);
            if (clauseIndex < 0
                || FindReference(inputs, binding.OriginArtifact) < 0
                || FindReference(inputs,
                    binding.ConstructorPlan.Artifact) < 0
                || !postconditionKeys.Add((clauseIndex,
                    binding.MethodDefRow, binding.PhaseLocalOrdinal)))
            {
                return false;
            }
        }

        return true;
    }

    private static int FindReference(
        IReadOnlyList<TrRmStandardArtifactInput> inputs,
        TrRmStandardArtifactInput candidate)
    {
        for (var index = 0; index < inputs.Count; index++)
        {
            if (ReferenceEquals(inputs[index], candidate))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsValidClause(
        TrRmStandardArtifactInput? artifact, uint methodDefRow)
        => artifact is not null
            && !string.IsNullOrEmpty(artifact.SchemaIdentity)
            && methodDefRow is > 0 and <= 0x00FF_FFFF;

    private static bool IsValidConstructorPlan(
        TrRmConstructorBindingLocation? plan)
        => plan is not null
            && IsValidClause(plan.Artifact, plan.MethodDefRow);
}
