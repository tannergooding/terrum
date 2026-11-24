// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    /// <summary>
    /// Creates one source reference from top-level methods in one classless
    /// Construction-2 Module and explicitly selected Module-qualified struct
    /// and field roots across its exact provider set.
    /// </summary>
    /// <param name="input">The complete provider artifact and its exact schema selection.</param>
    /// <param name="moduleIdentity">The exact identity of the Module that publishes the methods.</param>
    /// <param name="providers">The complete explicitly supplied provider artifact set.</param>
    /// <param name="methodExportKeys">The exact MethodDef ExportKeys to expose as source callables.</param>
    /// <param name="typeExportKeys">The Module-qualified TypeDef ExportKeys to expose as source types.</param>
    /// <param name="fieldExportKeys">The Module-qualified FieldDef ExportKeys to expose within selected type roots.</param>
    /// <returns>One checked reference containing the selected callables and type closure.</returns>
    /// <exception cref="ArgumentNullException">An input, list, selection, or selected string is null.</exception>
    /// <exception cref="ArgumentException">A selected key is malformed, duplicated, missing, or has the wrong kind.</exception>
    /// <exception cref="NotSupportedException">The selected schema or provider closure is outside the bounded projection.</exception>
    /// <exception cref="InvalidDataException">The selected artifact or retained provider identity is invalid.</exception>
    public static CompilationReference CreateStandardReference(
        TrRmStandardArtifactInput input, string moduleIdentity,
        IReadOnlyList<TrRmStandardArtifactInput> providers,
        IReadOnlyList<string> methodExportKeys,
        IReadOnlyList<TrRmStandardExportKeySelection> typeExportKeys,
        IReadOnlyList<TrRmStandardExportKeySelection> fieldExportKeys)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providers);
        ValidateSelectionText(moduleIdentity, nameof(moduleIdentity));
        var capturedMethodKeys = CaptureLayoutQueryExportKeys(
            methodExportKeys, nameof(methodExportKeys), requireOne: true);
        var capturedTypeKeys = CaptureExportKeySelections(typeExportKeys,
            nameof(typeExportKeys));
        var capturedFieldKeys = CaptureExportKeySelections(fieldExportKeys,
            nameof(fieldExportKeys));
        var selectedKeys = new HashSet<(string ModuleIdentity,
            string ExportKey)>();
        foreach (var selection in capturedTypeKeys)
        {
            if (!selectedKeys.Add((selection.ModuleIdentity,
                    selection.ExportKey)))
            {
                throw new ArgumentException(
                    "TypeDef and FieldDef selections cannot repeat a Module/ExportKey pair.",
                    nameof(typeExportKeys));
            }
        }
        foreach (var selection in capturedFieldKeys)
        {
            if (!selectedKeys.Add((selection.ModuleIdentity,
                    selection.ExportKey)))
            {
                throw new ArgumentException(
                    "TypeDef and FieldDef selections cannot repeat a Module/ExportKey pair.",
                    nameof(fieldExportKeys));
            }
        }

        var capturedProviders = new TrRmStandardArtifactInput[
            providers.Count];
        for (var index = 0; index < capturedProviders.Length; index++)
        {
            capturedProviders[index] = providers[index]
                ?? throw new ArgumentNullException(
                    $"{nameof(providers)}[{index}]");
        }

        var schemaIdentities = GetStandardSchemaIdentities(input,
            capturedProviders);
        if (schemaIdentities.Length == 0
            || schemaIdentities.Any(schemaIdentity =>
                !IsC1M1MethodTypeSchemaIdentity(schemaIdentity))
            || schemaIdentities.Any(schemaIdentity =>
                !StringComparer.Ordinal.Equals(schemaIdentity,
                    schemaIdentities[0])))
        {
            throw Unsupported(
                "The combined method/type reference requires one exact Construction-2 Copy-evidence selection for every artifact.");
        }

        RejectMaplessStandardDeclarationFailureReferences(input,
            moduleIdentity, capturedProviders);
        var artifacts = SnapshotStandardInputs(input, capturedProviders);
        return CreateC1M1MethodTypeSourceReference(artifacts,
            schemaIdentities, moduleIdentity, capturedMethodKeys,
            capturedTypeKeys, capturedFieldKeys);
    }

    private static TrRmStandardExportKeySelection[]
        CaptureExportKeySelections(
            IReadOnlyList<TrRmStandardExportKeySelection> selections,
            string parameterName)
    {
        ArgumentNullException.ThrowIfNull(selections, parameterName);
        var captured = new TrRmStandardExportKeySelection[
            selections.Count];
        for (var index = 0; index < captured.Length; index++)
        {
            var selection = selections[index]
                ?? throw new ArgumentNullException(
                    $"{parameterName}[{index}]");
            ValidateSelectionText(selection.ModuleIdentity,
                parameterName);
            ValidateSelectionText(selection.ExportKey, parameterName);
            captured[index] = new TrRmStandardExportKeySelection(
                selection.ModuleIdentity, selection.ExportKey);
        }

        return captured;
    }

    private static void ValidateSelectionText(string value,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length == 0
            || value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Module identities and ExportKeys must be nonempty NUL-free text.",
                parameterName);
        }

        try
        {
            _ = s_sourceTypeExportKeyEncoding.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Module identities and ExportKeys must be well-formed scalar text.",
                parameterName, exception);
        }
    }

    private static CompilationReference
        CreateC1M1MethodTypeSourceReference(byte[][] artifacts,
            string[] schemaIdentities, string moduleIdentity,
            string[] methodExportKeys,
            TrRmStandardExportKeySelection[] typeExportKeys,
            TrRmStandardExportKeySelection[] fieldExportKeys)
    {
        if (artifacts.Length == 0
            || artifacts.Length != schemaIdentities.Length
            || schemaIdentities.Any(schemaIdentity =>
                !IsC1M1MethodTypeSchemaIdentity(schemaIdentity))
            || schemaIdentities.Any(schemaIdentity =>
                !StringComparer.Ordinal.Equals(schemaIdentity,
                    schemaIdentities[0])))
        {
            throw Unsupported(
                "The combined method/type reference requires exact matching Construction-2 Copy-evidence selections for every artifact.");
        }

        var moduleValidation = ValidateStandardInputModules(artifacts,
            schemaIdentities, moduleIdentity, a3LayoutResourceLedger: null);
        var tablesByArtifact = new Dictionary<int,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables>();
        TrRmStructuralMetadataValidator.ValidatedExecutionTables GetTables(
            int artifactIndex)
        {
            if (tablesByArtifact.TryGetValue(artifactIndex,
                    out var existingTables))
            {
                return existingTables;
            }

            var index = CreateForeignInvocationIndex(
                artifacts[artifactIndex], schemaIdentities[artifactIndex]);
            var result = ValidateForeignInvocationArtifact(index,
                schemaIdentities[artifactIndex],
                createA3ResourceLedger: false);
            tablesByArtifact.Add(artifactIndex, result);
            return result;
        }

        var tables = GetTables(0);
        var moduleRow = moduleValidation.RootModuleRow;
        var rootModule = moduleValidation.Modules.SingleOrDefault(module =>
            module.ArtifactIndex == 0 && module.ModuleRow == moduleRow)
            ?? throw Invalid(
                "The combined method/type reference lost its selected root Module.");
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(moduleRow);
        if (targetRequirement
                != TrRmModuleTableReader.WindowsX64NativeTargetRequirement
            || runtimePolicy != 0)
        {
            throw Unsupported(
                "The combined method/type reference requires a Windows x64 fail-fast root Module.");
        }

        var moduleByIdentity = moduleValidation.Modules.ToDictionary(
            static module => module.Identity, StringComparer.Ordinal);
        var moduleByLocation = moduleValidation.Modules.ToDictionary(
            static module => (module.ArtifactIndex, module.ModuleRow));
        var typeProgramByLocation
            = new Dictionary<(int ArtifactIndex, int ModuleRow),
                BoundStandardMethodProviderModule>();
        foreach (var module in moduleValidation.Modules)
        {
            var moduleTables = GetTables(module.ArtifactIndex);
            var (moduleTargetRequirement, moduleRuntimePolicy)
                = moduleTables.Modules.GetRequirements(module.ModuleRow);
            var (typeDefStart, typeDefEnd)
                = moduleTables.Modules.GetTypeDefRange(module.ModuleRow);
            if (moduleTargetRequirement
                    != TrRmModuleTableReader.WindowsX64NativeTargetRequirement
                || moduleRuntimePolicy != 0
                || (typeDefStart == 0) != (typeDefEnd == 0)
                || typeDefEnd < typeDefStart
                || Enumerable.Range(typeDefStart,
                    typeDefEnd - typeDefStart).Any(typeDefRow =>
                    moduleTables.TypeDefs.GetKind(typeDefRow) != 1))
            {
                throw Unsupported(
                    "The initial combined method/type reference requires classless Windows x64 Modules.");
            }
        }

        var classProviderValidation
            = TrRmFixedArrayProviderBindingValidator
                .ValidateC1M1ClassImports(tables, artifacts,
                    schemaIdentities, moduleValidation.Modules, moduleRow);
        var linkedProgram = LoadLocalAggregateLayoutProviderClosure(
            artifacts, schemaIdentities, moduleIdentity, moduleValidation,
            classProviderValidation.TypeBindings,
            classProviderValidation.FieldBindings,
            construction2MethodLinking: true);
        var fieldProviderValidation
            = new TrRmStandardFieldProviderValidation(
                classProviderValidation.TypeBindings,
                classProviderValidation.FieldBindings,
                classProviderValidation.MethodBindings);
        var program = RetainStandardFieldProviderClosure(linkedProgram,
            fieldProviderValidation, artifacts, schemaIdentities,
            moduleValidation.Modules, moduleRow, moduleIdentity);
        var methodProviderClosure
            = program.StandardMethodProviderClosure
                ?? throw Invalid(
                    "The selected Module has no retained MethodDef origin map.");
        foreach (var module in methodProviderClosure.Modules)
        {
            typeProgramByLocation.Add((module.ArtifactIndex,
                module.ModuleRow), module);
            var moduleTables = GetTables(module.ArtifactIndex);
            var (typeDefStart, typeDefEnd)
                = moduleTables.Modules.GetTypeDefRange(module.ModuleRow);
            if ((typeDefStart == 0) != (typeDefEnd == 0)
                || typeDefEnd < typeDefStart
                || typeDefEnd - typeDefStart
                    != module.Program.Structures.Length)
            {
                throw Unsupported(
                    "The combined method/type reference requires TypeDef-aligned Blittable struct Modules.");
            }
        }

        if (!typeProgramByLocation.TryGetValue((0, moduleRow),
                out var rootProviderModule))
        {
            throw Invalid(
                "The selected MethodDef origin map lost its root Module.");
        }

        var typeBindingByExpectedType = new Dictionary<(
            int ArtifactIndex, int ModuleRow, int TypeDefRow),
            TrRmStandardTypeProviderBinding>();
        foreach (var binding in classProviderValidation.TypeBindings)
        {
            if (!typeBindingByExpectedType.TryAdd((
                    binding.ConsumerArtifactIndex,
                    binding.ConsumerModuleRow,
                    binding.ExpectedTypeDefRow), binding))
            {
                throw Invalid(
                    "A contextual TypeDef endpoint has multiple Import bindings.");
            }
        }

        var (methodStart, methodEnd)
            = tables.Modules.GetMethodDefRange(moduleRow);
        var providerSnapshots = new Dictionary<
            (int ArtifactIndex, int ModuleRow),
            BoundSourceTypeProviderSnapshot>();
        BoundSourceTypeProviderSnapshot GetProviderSnapshot(
            TrRmStandardModuleSelection module)
        {
            var key = (module.ArtifactIndex, module.ModuleRow);
            if (providerSnapshots.TryGetValue(key, out var existingSnapshot))
            {
                return existingSnapshot;
            }

            var snapshot = CreateA2SourceTypeProviderSnapshot(
                GetTables(module.ArtifactIndex),
                artifacts[module.ArtifactIndex],
                schemaIdentities[module.ArtifactIndex], module.ModuleRow);
            if (!StringComparer.Ordinal.Equals(snapshot.ModuleIdentity,
                    module.Identity))
            {
                throw Invalid(
                    "A selected type provider snapshot has a different Module identity.");
            }

            providerSnapshots.Add(key, snapshot);
            return snapshot;
        }

        var exportsByDefinition = new Dictionary<
            (int ArtifactIndex, int ModuleRow, uint DefinitionId),
            List<string>>();
        foreach (var module in moduleValidation.Modules)
        {
            foreach (var (definition, exportKey) in
                GetTables(module.ArtifactIndex).Exports.GetExportsForModule(
                    module.ModuleRow))
            {
                var key = (module.ArtifactIndex, module.ModuleRow,
                    definition);
                if (!exportsByDefinition.TryGetValue(key,
                        out var keys))
                {
                    keys = [];
                    exportsByDefinition.Add(key, keys);
                }

                keys.Add(exportKey);
            }
        }

        ImmutableArray<string> GetExportKeys(
            TrRmStandardModuleSelection module, uint definition)
            => exportsByDefinition.TryGetValue(
                (module.ArtifactIndex, module.ModuleRow, definition),
                out var keys)
                ? [.. keys] : [];

        var canonicalStructureByOrigin = new Dictionary<
            (string ModuleIdentity, uint DefinitionId), int>();
        for (var structureIndex = 0;
            structureIndex < program.Structures.Length; structureIndex++)
        {
            var origin = program.Structures[structureIndex]
                .StandardTypeOrigin;
            if (origin is null
                || (origin.DefinitionId >> 24) != 0x13
                || !moduleByIdentity.TryGetValue(
                    origin.Provider.ModuleIdentity,
                    out var providerModule)
                || providerModule.ModuleRow != origin.Provider.ModuleRow
                || providerModule.TargetRequirement
                    != origin.Provider.TargetRequirement
                || providerModule.RuntimePolicy
                    != origin.Provider.RuntimePolicy
                || !StringComparer.Ordinal.Equals(
                    schemaIdentities[providerModule.ArtifactIndex],
                    origin.Provider.SchemaIdentity)
                || !origin.Provider.ArtifactSnapshot.AsSpan()
                    .SequenceEqual(artifacts[providerModule.ArtifactIndex]))
            {
                throw Invalid(
                    "A canonical Construction-2 struct has no exact provider TypeDef origin.");
            }

            var originKey = (origin.Provider.ModuleIdentity,
                origin.DefinitionId);
            if (canonicalStructureByOrigin.TryGetValue(originKey,
                    out var existingStructureIndex))
            {
                var existingOrigin = program.Structures[
                    existingStructureIndex].StandardTypeOrigin
                    ?? throw Invalid(
                        "A canonical struct origin disappeared during projection.");
                var existingIsActual
                    = existingOrigin.Expectations.IsDefaultOrEmpty
                        && existingOrigin.ReferenceRoot is
                            BoundSourceTypeProviderSnapshot existingRoot
                        && SameA2SourceTypeProviderSnapshot(existingRoot,
                            existingOrigin.Provider);
                var currentIsActual = origin.Expectations.IsDefaultOrEmpty
                    && origin.ReferenceRoot is
                        BoundSourceTypeProviderSnapshot currentRoot
                    && SameA2SourceTypeProviderSnapshot(currentRoot,
                        origin.Provider);
                if (!SameA2SourceTypeProviderSnapshot(existingOrigin.Provider,
                        origin.Provider)
                    || existingOrigin.DefinitionId != origin.DefinitionId
                    || (existingIsActual && currentIsActual))
                {
                    throw Invalid(
                        "A provider TypeDef origin has no unique canonical local declaration.");
                }

                if (currentIsActual && !existingIsActual)
                {
                    canonicalStructureByOrigin[originKey] = structureIndex;
                }
            }
            else
            {
                canonicalStructureByOrigin.Add(originKey, structureIndex);
            }
        }

        var selectedTypeKeysByOrigin = new Dictionary<
            (string ModuleIdentity, uint DefinitionId), List<string>>();
        foreach (var selection in typeExportKeys)
        {
            if (!moduleByIdentity.TryGetValue(selection.ModuleIdentity,
                    out var selectedModule))
            {
                throw new ArgumentException(
                    "A selected type Module identity is not present in the supplied provider set.",
                    nameof(typeExportKeys));
            }

            var selectedTables = GetTables(selectedModule.ArtifactIndex);
            if (!selectedTables.Exports.TryGetDefinitionForKey(
                    selectedModule.ModuleRow, selection.ExportKey,
                    out var definition)
                || (definition >> 24) != 0x13)
            {
                throw new ArgumentException(
                    "A selected ExportKey does not identify a TypeDef in its selected Module.",
                    nameof(typeExportKeys));
            }

            var typeDefRow = checked((int)(definition & 0x00FF_FFFF));
            var (fieldStart, fieldEnd)
                = selectedTables.TypeDefs.GetFieldRange(typeDefRow);
            var (methodRangeStart, methodRangeEnd)
                = selectedTables.TypeDefs.GetMethodRange(typeDefRow);
            var (typeDefStart, typeDefEnd)
                = selectedTables.Modules.GetTypeDefRange(
                    selectedModule.ModuleRow);
            if (typeDefRow < typeDefStart || typeDefRow >= typeDefEnd
                || selectedTables.TypeDefs.GetOwnerModule(typeDefRow)
                    != selectedModule.ModuleRow
                || selectedTables.Imports.IsExpectedDefinition(definition)
                || selectedTables.TypeDefs.GetKind(typeDefRow) != 1
                || (selectedTables.TypeDefs.GetFlags(typeDefRow) & 3) != 2
                || selectedTables.TypeDefs.GetGenericRange(typeDefRow)
                    .Count != 0
                || selectedTables.TypeDefs.HasDeclaredUnique(typeDefRow)
                || methodRangeStart != methodRangeEnd
                || !selectedTables.TypeDefs.HasDeclaredCopy(typeDefRow)
                || !selectedTables.Layouts.HasBlittableLayout(typeDefRow,
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader
                        .WindowsX64NativeProfileRuleRevision))
            {
                throw Invalid(
                    "A selected TypeDef Export does not identify a local public nongeneric Copy struct with the required Blittable layout.");
            }

            var originKey = (selectedModule.Identity, definition);
            if (!canonicalStructureByOrigin.TryGetValue(originKey,
                    out var structureIndex))
            {
                throw Invalid(
                    "A selected TypeDef Export is outside the checked struct closure.");
            }

            var structure = program.Structures[structureIndex];
            var providerSnapshot = GetProviderSnapshot(selectedModule);
            var typeOrigin = structure.StandardTypeOrigin!;
            if (structure.IsPrivate || structure.IsInternal
                || structure.IsUnique
                || structure.CleanupMethod is not null
                || structure.SourceGenericTypeDefinitionIndex >= 0
                || !structure.IsSequential && !structure.IsTransparent
                || structure.BlittableTargetRequirement
                    != FixedLayoutStorage.WindowsX64TargetRequirement
                || structure.DeclaredCopyEvidence != true
                || !StringComparer.Ordinal.Equals(
                    typeOrigin.Provider.ModuleIdentity,
                    selectedModule.Identity)
                || typeOrigin.DefinitionId != definition
                || !SameA2SourceTypeProviderSnapshot(
                    typeOrigin.Provider, providerSnapshot)
                || fieldEnd - fieldStart != structure.Fields.Length)
            {
                throw Invalid(
                    "A selected struct lost its exact provider origin, Copy evidence, or Blittable field layout.");
            }

            if (!selectedTypeKeysByOrigin.TryGetValue(originKey,
                    out var selectedKeys))
            {
                selectedKeys = [];
                selectedTypeKeysByOrigin.Add(originKey, selectedKeys);
            }
            selectedKeys.Add(selection.ExportKey);
        }

        var selectedFieldKeysByOwner = new Dictionary<
            (int StructureIndex, int FieldOrdinal), List<string>>();
        foreach (var selection in fieldExportKeys)
        {
            if (!moduleByIdentity.TryGetValue(selection.ModuleIdentity,
                    out var selectedModule))
            {
                throw new ArgumentException(
                    "A selected field Module identity is not present in the supplied provider set.",
                    nameof(fieldExportKeys));
            }

            var selectedTables = GetTables(selectedModule.ArtifactIndex);
            if (!selectedTables.Exports.TryGetDefinitionForKey(
                    selectedModule.ModuleRow, selection.ExportKey,
                    out var definition)
                || (definition >> 24) != 0x14)
            {
                throw new ArgumentException(
                    "A selected ExportKey does not identify a FieldDef in its selected Module.",
                    nameof(fieldExportKeys));
            }

            var fieldRow = checked((int)(definition & 0x00FF_FFFF));
            var ownerTypeDefRow = selectedTables.Fields.GetOwnerTypeDef(
                fieldRow);
            var ownerDefinitionId = 0x1300_0000u
                | checked((uint)ownerTypeDefRow);
            var ownerOrigin = (selectedModule.Identity, ownerDefinitionId);
            var (typeDefStart, typeDefEnd)
                = selectedTables.Modules.GetTypeDefRange(
                    selectedModule.ModuleRow);
            var (fieldStart, fieldEnd)
                = selectedTables.TypeDefs.GetFieldRange(ownerTypeDefRow);
            var fieldFlags = selectedTables.Fields.GetFlags(fieldRow);
            if (!selectedTypeKeysByOrigin.ContainsKey(ownerOrigin)
                || ownerTypeDefRow < typeDefStart
                || ownerTypeDefRow >= typeDefEnd
                || selectedTables.TypeDefs.GetOwnerModule(ownerTypeDefRow)
                    != selectedModule.ModuleRow
                || selectedTables.TypeDefs.GetKind(ownerTypeDefRow) != 1
                || selectedTables.Imports.IsExpectedDefinition(
                    ownerDefinitionId)
                || (fieldFlags & 3) != 2
                || (fieldFlags & 8) != 0
                || fieldRow < fieldStart || fieldRow >= fieldEnd
                || selectedTables.Fields.GetOwnerTypeDef(fieldRow)
                    != ownerTypeDefRow)
            {
                throw new ArgumentException(
                    "A selected FieldDef must be a public instance field of an explicitly selected local TypeDef.",
                    nameof(fieldExportKeys));
            }

            var fieldOrdinal = fieldRow - fieldStart;
            if (!canonicalStructureByOrigin.TryGetValue(ownerOrigin,
                    out var ownerIndex)
                || (uint)fieldOrdinal
                    >= (uint)program.Structures[ownerIndex].Fields.Length)
            {
                throw Invalid(
                    "A selected FieldDef is outside its loaded owner.");
            }

            var key = (ownerIndex, fieldOrdinal);
            if (!selectedFieldKeysByOwner.TryGetValue(key,
                    out var selectedKeys))
            {
                selectedKeys = [];
                selectedFieldKeysByOwner.Add(key, selectedKeys);
            }
            selectedKeys.Add(selection.ExportKey);
        }

        var typeExpectationsByOrigin = new Dictionary<
            (string ModuleIdentity, uint DefinitionId),
            List<BoundSourceTypeExpectation>>();
        foreach (var edge in methodProviderClosure.TypeEdges)
        {
            if (!moduleByLocation.TryGetValue(
                    (edge.ConsumerArtifactIndex, edge.ConsumerModuleRow),
                    out var usingModule)
                || !moduleByLocation.TryGetValue(
                    (edge.ProviderArtifactIndex, edge.ProviderModuleRow),
                    out var providerModule))
            {
                throw Invalid(
                    "A retained TypeDef provider edge is outside the checked Module directory.");
            }

            if ((edge.ExpectedDefinitionId >> 24) != 0x13
                || (edge.ActualDefinitionId >> 24) != 0x13
                || !StringComparer.Ordinal.Equals(edge.ProducerIdentity,
                    providerModule.Identity)
                || edge.TargetRequirement
                    != providerModule.TargetRequirement
                || edge.RuntimePolicy != providerModule.RuntimePolicy)
            {
                throw Invalid(
                    "A retained TypeDef provider edge has an invalid identity or requirement.");
            }

            var originKey = (providerModule.Identity,
                edge.ActualDefinitionId);
            if (!typeExpectationsByOrigin.TryGetValue(originKey,
                    out var expectations))
            {
                expectations = [];
                typeExpectationsByOrigin.Add(originKey, expectations);
            }

            expectations.Add(new BoundSourceTypeExpectation(
                GetProviderSnapshot(usingModule), edge.ImportRow,
                edge.ExpectedDefinitionId, edge.ProducerIdentity,
                edge.ExportKey, edge.TargetRequirement, edge.RuntimePolicy,
                GetProviderSnapshot(providerModule),
                edge.ActualDefinitionId));
        }

        PrimitiveType RemapModuleType(
            BoundStandardMethodProviderModule module, PrimitiveType type)
        {
            if (IsCombinedMethodScalarOrUnit(type))
            {
                return type;
            }

            var localStructureIndex = (int)type - 100;
            if ((uint)localStructureIndex
                >= (uint)module.Program.Structures.Length
                || !moduleByLocation.TryGetValue(
                    (module.ArtifactIndex, module.ModuleRow),
                    out var selectedModule))
            {
                throw Unsupported(
                    "A selected method signature uses an unsupported type family.");
            }

            var localStructure
                = module.Program.Structures[localStructureIndex];
            var localOrigin = localStructure.StandardTypeOrigin;
            (string ModuleIdentity, uint DefinitionId) origin;
            if (localOrigin is not null)
            {
                origin = (localOrigin.Provider.ModuleIdentity,
                    localOrigin.DefinitionId);
            }
            else
            {
                var moduleTables = GetTables(module.ArtifactIndex);
                var (typeDefStart, typeDefEnd)
                    = moduleTables.Modules.GetTypeDefRange(
                        module.ModuleRow);
                if (typeDefEnd - typeDefStart
                    != module.Program.Structures.Length)
                {
                    throw Invalid(
                        "A local method TypeSpec does not map to its Module-owned TypeDef range.");
                }

                var typeDefRow = typeDefStart + localStructureIndex;
                origin = (selectedModule.Identity,
                    0x1300_0000u | checked((uint)typeDefRow));
            }

            return canonicalStructureByOrigin.TryGetValue(origin,
                    out var structureIndex)
                ? (PrimitiveType)(100 + structureIndex)
                : throw Invalid(
                    "A method struct TypeSpec is outside the checked provider closure.");
        }

        var typeImportContextsByOrigin = new Dictionary<
            (string ModuleIdentity, uint DefinitionId),
            List<BoundSourceTypeProviderSnapshot>>();
        void AddTypeImportContext(int structureIndex,
            BoundSourceTypeProviderSnapshot usingModule)
        {
            if ((uint)structureIndex >= (uint)program.Structures.Length
                || program.Structures[structureIndex].StandardTypeOrigin
                    is not BoundStandardTypeOrigin typeOrigin)
            {
                throw Invalid(
                    "A required struct TypeDef context has no canonical provider origin.");
            }

            var origin = (typeOrigin.Provider.ModuleIdentity,
                typeOrigin.DefinitionId);
            if (!typeImportContextsByOrigin.TryGetValue(origin,
                    out var contexts))
            {
                contexts = [];
                typeImportContextsByOrigin.Add(origin, contexts);
            }

            if (!contexts.Any(context =>
                    SameA2SourceTypeProviderSnapshot(context, usingModule)))
            {
                contexts.Add(usingModule);
            }
        }

        var selectedStructures = new HashSet<int>();
        var pendingStructures = new Stack<int>();
        foreach (var origin in selectedTypeKeysByOrigin.Keys)
        {
            var structureIndex = canonicalStructureByOrigin[origin];
            _ = selectedStructures.Add(structureIndex);
            pendingStructures.Push(structureIndex);
        }

        var selectedMethods = new List<(BoundFunction Function, string Key)>(
            methodExportKeys.Length);
        BoundSourceTypeProviderSnapshot? rootProviderSnapshot = null;
        foreach (var exportKey in methodExportKeys)
        {
            if (!tables.Exports.TryGetDefinitionForKey(moduleRow, exportKey,
                    out var definition)
                || (definition >> 24) != 0x16)
            {
                throw new ArgumentException(
                    "A selected ExportKey does not identify a MethodDef in the selected Module.",
                    nameof(methodExportKeys));
            }

            var methodRow = checked((int)(definition & 0x00FF_FFFF));
            var signature = tables.Methods.GetSignature(methodRow);
            if ((uint)methodRow
                >= (uint)rootProviderModule.FunctionIndexByMethodRow.Length)
            {
                throw Invalid(
                    "A selected MethodDef is outside its retained function origin map.");
            }

            var functionIndex
                = rootProviderModule.FunctionIndexByMethodRow[methodRow];
            if ((uint)functionIndex
                >= (uint)rootProviderModule.Program.Functions.Length)
            {
                throw Invalid(
                    "A selected MethodDef has no reconstructed source function.");
            }

            var function
                = rootProviderModule.Program.Functions[functionIndex];
            if (methodRow < methodStart || methodRow >= methodEnd
                || tables.Methods.GetOwnerType(methodRow) != 0
                || (tables.Methods.GetFlags(methodRow) & 3) != 2
                || tables.Signatures.GetGenericCount(signature) != 0
                || tables.Methods.GetBodyRow(methodRow) == 0
                || tables.NativeFunctionPointerAnchors
                    .IsAnchorMethodDef(methodRow)
                || tables.StandardNativeImports
                    .HasImportForMethod(methodRow)
                || function.IsExternal || function.IsModuleReference
                || function.DirectNativeImport is not null
                || function.NativeCallableConvention is not null
                || function.NativeOutputRole is not null
                || function.NativeVTableMethod is not null
                || function.PropertyName is not null
                || function.IsPartialConstructor || function.IsCleanup)
            {
                throw Invalid(
                    "A selected MethodDef is not a public local ordinary top-level function with a supported Body.");
            }

            if (!IsCombinedMethodScalarOrUnit(function.ReturnType)
                || function.ParameterCount > function.Variables.Length
                || function.Variables.Take(function.ParameterCount)
                    .Any(static variable =>
                    variable.IsOut || variable.IsInit
                    || variable.IsImplicitRef
                    || variable.IsCanonicalImplicitReference))
            {
                throw Unsupported(
                    "A selected method requires a supported scalar/unit result and ordinary by-value scalar/unit or struct parameters.");
            }

            for (var ordinal = 0;
                ordinal < function.ParameterCount; ordinal++)
            {
                var variable = function.Variables[ordinal];
                if (IsCombinedMethodScalarOrUnit(variable.Type))
                {
                    continue;
                }

                var parameterType = RemapModuleType(rootProviderModule,
                    variable.Type);
                var structureIndex = (int)parameterType - 100;
                if (program.Structures[structureIndex].StandardTypeOrigin
                    is null)
                {
                    throw Invalid(
                        "A selected method parameter lost its canonical struct origin.");
                }
                var methodProviderSnapshot
                    = rootProviderSnapshot ??= GetProviderSnapshot(rootModule);
                AddTypeImportContext(structureIndex,
                    methodProviderSnapshot);
                _ = selectedStructures.Add(structureIndex);
                pendingStructures.Push(structureIndex);
            }

            if (function.OrdinaryParameterDefaults.Any(defaultValue =>
                    defaultValue.ParameterOrdinal >= 0
                    && defaultValue.ParameterOrdinal
                        < function.ParameterCount
                    && (int)RemapModuleType(rootProviderModule,
                        function.Variables[
                            defaultValue.ParameterOrdinal].Type) >= 100))
            {
                throw Unsupported(
                    "The combined method/type reference does not admit struct-valued parameter defaults.");
            }

            selectedMethods.Add((function, exportKey));
        }

        while (pendingStructures.TryPop(out var structureIndex))
        {
            if ((uint)structureIndex
                >= (uint)program.Structures.Length)
            {
                throw Invalid(
                    "A selected method/type closure refers to an unknown canonical struct.");
            }

            var structure = program.Structures[structureIndex];
            var origin = structure.StandardTypeOrigin
                ?? throw Invalid(
                    "A selected struct has no retained provider TypeDef origin.");
            var originKey = (origin.Provider.ModuleIdentity,
                origin.DefinitionId);
            var hasSelectedTypeKey
                = selectedTypeKeysByOrigin.ContainsKey(originKey);
            var hasRetainedExpectationForContext = false;
            if (!hasSelectedTypeKey
                && typeExpectationsByOrigin.TryGetValue(originKey,
                    out var retainedTypeExpectations)
                && typeImportContextsByOrigin.TryGetValue(originKey,
                    out var requiredContexts))
            {
                hasRetainedExpectationForContext
                    = retainedTypeExpectations.Any(expectation =>
                        requiredContexts.Any(context =>
                            SameA2SourceTypeProviderSnapshot(
                                expectation.UsingModule, context)));
            }

            if (!hasSelectedTypeKey
                && !hasRetainedExpectationForContext)
            {
                throw new ArgumentException(
                    "Every provider-local method and nested struct dependency that requires a new Import needs an explicit TypeDef ExportKey selection.",
                    nameof(typeExportKeys));
            }

            foreach (var field in structure.Fields)
            {
                if (IsCombinedMethodScalarOrUnit(field.Type))
                {
                    continue;
                }

                var nestedStructureIndex = (int)field.Type - 100;
                if ((uint)nestedStructureIndex
                    >= (uint)program.Structures.Length)
                {
                    throw Unsupported(
                        "The selected struct closure contains an unsupported field type family.");
                }

                if (selectedStructures.Add(nestedStructureIndex))
                {
                    pendingStructures.Push(nestedStructureIndex);
                }
                if (!moduleByIdentity.TryGetValue(
                        origin.Provider.ModuleIdentity,
                        out var ownerProviderModule))
                {
                    throw Invalid(
                        "A selected struct field owner is outside the checked Module directory.");
                }

                AddTypeImportContext(nestedStructureIndex,
                    GetProviderSnapshot(ownerProviderModule));
            }
        }

        var structureOrder = selectedStructures.ToArray();
        Array.Sort(structureOrder);
        var projectedIndexByStructure = new Dictionary<int, int>(
            structureOrder.Length);
        for (var projectedIndex = 0;
            projectedIndex < structureOrder.Length; projectedIndex++)
        {
            projectedIndexByStructure.Add(structureOrder[projectedIndex],
                projectedIndex);
        }

        PrimitiveType RemapCanonicalType(PrimitiveType type)
        {
            if (IsCombinedMethodScalarOrUnit(type))
            {
                return type;
            }

            var structureIndex = (int)type - 100;
            if ((uint)structureIndex >= (uint)program.Structures.Length
                || !projectedIndexByStructure.TryGetValue(structureIndex,
                    out var projectedIndex))
            {
                throw Invalid(
                    "A selected method/type signature is missing its struct identity mapping.");
            }

            return (PrimitiveType)(100 + projectedIndex);
        }

        PrimitiveType RemapMethodType(PrimitiveType type)
            => RemapCanonicalType(
                RemapModuleType(rootProviderModule, type));

        void AddTypeExpectation(
            Dictionary<(string ModuleIdentity, int ModuleRow, int ImportRow,
                uint ExpectedDefinitionId), BoundSourceTypeExpectation>
                expectationsByImport,
            BoundSourceTypeExpectation expectation)
        {
            var key = (expectation.UsingModule.ModuleIdentity,
                expectation.UsingModule.ModuleRow, expectation.ImportRow,
                expectation.ExpectedDefinitionId);
            if (expectationsByImport.TryGetValue(key,
                    out var existingExpectation))
            {
                if (!StringComparer.Ordinal.Equals(
                        existingExpectation.ProducerIdentity,
                        expectation.ProducerIdentity)
                    || !StringComparer.Ordinal.Equals(
                        existingExpectation.ExportKey,
                        expectation.ExportKey)
                    || existingExpectation.TargetRequirement
                        != expectation.TargetRequirement
                    || existingExpectation.RuntimePolicy
                        != expectation.RuntimePolicy
                    || existingExpectation.ProviderDefinitionId
                        != expectation.ProviderDefinitionId
                    || !SameA2SourceTypeProviderSnapshot(
                        existingExpectation.UsingModule,
                        expectation.UsingModule)
                    || !SameA2SourceTypeProviderSnapshot(
                        existingExpectation.Provider,
                        expectation.Provider))
                {
                    throw Invalid(
                        "A retained TypeDef Import row has conflicting provider expectations.");
                }

                return;
            }

            expectationsByImport.Add(key, expectation);
        }

        var retainedFieldProviderEdges
            = ImmutableArray.CreateBuilder<BoundStandardFieldProviderEdge>();
        if (program.StandardQuerySourceAssociation is
            BoundStandardQuerySourceAssociation fieldAssociation)
        {
            foreach (var closure in fieldAssociation.ProviderClosures)
            {
                retainedFieldProviderEdges.AddRange(closure.FieldEdges);
            }
        }
        if (program.StandardFieldProviderClosure is
                BoundStandardFieldProviderClosure retainedFieldClosure
            && (program.StandardQuerySourceAssociation is not
                    BoundStandardQuerySourceAssociation existingAssociation
                || !existingAssociation.ProviderClosures.Any(closure =>
                    ReferenceEquals(closure, retainedFieldClosure))))
        {
            retainedFieldProviderEdges.AddRange(
                retainedFieldClosure.FieldEdges);
        }
        var retainedFieldEdges = retainedFieldProviderEdges.ToImmutable();

        var contextualTypeViews = new List<BoundStandardTypeView>();
        var contextualViewIndexByModuleType = new Dictionary<(
            int ArtifactIndex, int ModuleRow, uint ExpectedDefinitionId,
            int ImportRow), int>();
        var contextualViewIndexesByStructure = new Dictionary<int, List<int>>();
        var contextualViewsInProgress = new HashSet<(
            int ArtifactIndex, int ModuleRow, uint ExpectedDefinitionId,
            int ImportRow)>();
        var methodTypeViews
            = ImmutableArray.CreateBuilder<BoundStandardMethodTypeView>();

        int GetOrAddTypeView(BoundStandardMethodProviderModule viewModule,
            int localStructureIndex)
        {
            if (!moduleByLocation.TryGetValue((
                    viewModule.ArtifactIndex, viewModule.ModuleRow),
                    out var usingModule))
            {
                throw Invalid(
                    "A contextual TypeDef view is outside the checked Module directory.");
            }

            var viewTables = GetTables(viewModule.ArtifactIndex);
            var (viewTypeDefStart, viewTypeDefEnd)
                = viewTables.Modules.GetTypeDefRange(viewModule.ModuleRow);
            if (viewTypeDefEnd - viewTypeDefStart
                    != viewModule.Program.Structures.Length
                || (uint)localStructureIndex
                    >= (uint)viewModule.Program.Structures.Length)
            {
                throw Invalid(
                    "A contextual TypeDef view is outside its using Module.");
            }

            var localTypeDefRow = viewTypeDefStart + localStructureIndex;
            var expectedDefinitionId = 0x1300_0000u
                | checked((uint)localTypeDefRow);
            var localStructure
                = viewModule.Program.Structures[localStructureIndex];
            var localOrigin = localStructure.StandardTypeOrigin;
            var actualOrigin = localOrigin is null
                ? (viewModule.Identity, expectedDefinitionId)
                : (localOrigin.Provider.ModuleIdentity,
                    localOrigin.DefinitionId);
            if (!canonicalStructureByOrigin.TryGetValue(actualOrigin,
                    out var canonicalStructureIndex)
                || (uint)canonicalStructureIndex
                    >= (uint)program.Structures.Length
                || program.Structures[canonicalStructureIndex]
                    .StandardTypeOrigin is not
                        BoundStandardTypeOrigin actualOriginData
                || !StringComparer.Ordinal.Equals(
                    actualOriginData.Provider.ModuleIdentity,
                    actualOrigin.Item1)
                || actualOriginData.DefinitionId != actualOrigin.Item2)
            {
                throw Invalid(
                    "A contextual TypeDef view has no canonical actual identity.");
            }

            var expectationsByImport = new Dictionary<(
                string ModuleIdentity, int ModuleRow, int ImportRow,
                uint ExpectedDefinitionId), BoundSourceTypeExpectation>();
            if (typeExpectationsByOrigin.TryGetValue(actualOrigin,
                    out var retainedExpectations))
            {
                foreach (var retainedExpectation in retainedExpectations)
                {
                    AddTypeExpectation(expectationsByImport,
                        retainedExpectation);
                }
            }
            if (localOrigin is not null
                && !localOrigin.Expectations.IsDefaultOrEmpty)
            {
                foreach (var retainedExpectation in
                    localOrigin.Expectations)
                {
                    AddTypeExpectation(expectationsByImport,
                        retainedExpectation);
                }
            }

            var expectationCandidates = expectationsByImport.Values
                .Where(expectation =>
                    expectation.ExpectedDefinitionId
                        == expectedDefinitionId
                    && SameA2SourceTypeProviderSnapshot(
                        expectation.UsingModule,
                        GetProviderSnapshot(usingModule)))
                .ToArray();
            if (expectationCandidates.Length > 1)
            {
                throw Invalid(
                    "A contextual TypeDef view has multiple Imports for one expected TypeDef row.");
            }

            var expectation = expectationCandidates.Length == 0
                ? null : expectationCandidates[0];
            var isLocalActual
                = StringComparer.Ordinal.Equals(
                    actualOriginData.Provider.ModuleIdentity,
                    viewModule.Identity)
                    && actualOriginData.DefinitionId
                        == expectedDefinitionId;
            if (expectation is null && !isLocalActual)
            {
                throw Invalid(
                    "A contextual TypeDef proxy lost its exact provider Import.");
            }

            var isLookupRoot = false;
            string? exportKey = null;
            if (expectation is not null)
            {
                if (!SameA2SourceTypeProviderSnapshot(
                        expectation.Provider, actualOriginData.Provider)
                    || expectation.ProviderDefinitionId
                        != actualOriginData.DefinitionId)
                {
                    throw Invalid(
                        "A contextual TypeDef Import targets a different actual declaration.");
                }

                exportKey = expectation.ExportKey;
            }
            else if (selectedTypeKeysByOrigin.TryGetValue(actualOrigin,
                         out var selectedKeys)
                     && selectedKeys.Count != 0)
            {
                isLookupRoot = true;
                exportKey = selectedKeys[0];
            }
            else
            {
                throw new ArgumentException(
                    "A closure-only local struct needs an explicit TypeDef ExportKey selection.",
                    nameof(typeExportKeys));
            }

            var importRow = expectation?.ImportRow ?? 0;
            var viewKey = (viewModule.ArtifactIndex, viewModule.ModuleRow,
                expectedDefinitionId, importRow);
            if (contextualViewIndexByModuleType.TryGetValue(viewKey,
                    out var existingViewIndex))
            {
                if (contextualViewsInProgress.Contains(viewKey))
                {
                    throw Invalid(
                        "The contextual TypeDef view graph contains an inline cycle.");
                }

                return existingViewIndex;
            }

            var viewIndex = contextualTypeViews.Count;
            contextualViewIndexByModuleType.Add(viewKey, viewIndex);
            contextualViewsInProgress.Add(viewKey);
            if (!contextualViewIndexesByStructure.TryGetValue(
                    canonicalStructureIndex, out var viewIndexes))
            {
                viewIndexes = [];
                contextualViewIndexesByStructure.Add(
                    canonicalStructureIndex, viewIndexes);
            }
            viewIndexes.Add(viewIndex);

            if (!moduleByIdentity.TryGetValue(
                    actualOriginData.Provider.ModuleIdentity,
                    out var providerSelection))
            {
                throw Invalid(
                    "A contextual TypeDef view references an unloaded provider Module.");
            }
            var providerTables = GetTables(providerSelection.ArtifactIndex);
            var actualTypeDefRow = checked((int)(
                actualOriginData.DefinitionId & 0x00FF_FFFF));
            if (providerTables.TypeDefs.GetOwnerModule(actualTypeDefRow)
                    != providerSelection.ModuleRow
                || providerTables.TypeDefs.GetKind(actualTypeDefRow) != 1)
            {
                throw Invalid(
                    "A contextual TypeDef view does not resolve to its provider-owned struct.");
            }

            PrimitiveType RemapViewType(PrimitiveType type)
                => RemapCanonicalType(
                    RemapModuleType(viewModule, type));
            var expectedSource = localOrigin?.ExpectedDeclaration
                ?? localStructure;
            var sourceViewDeclaration = BoundTreeTraversal.RewriteTypes(
                expectedSource, RemapViewType) with {
                StandardTypeOrigin = null,
                StandardLayoutOnly = false,
            };
            var (localFieldStart, localFieldEnd)
                = viewTables.TypeDefs.GetFieldRange(localTypeDefRow);
            if (localFieldEnd - localFieldStart
                != sourceViewDeclaration.Fields.Length)
            {
                throw Invalid(
                    "A contextual TypeDef view has an incomplete FieldDef range.");
            }
            var viewFields
                = ImmutableArray.CreateBuilder<BoundStructField>(
                    sourceViewDeclaration.Fields.Length);
            var fieldEdges
                = ImmutableArray.CreateBuilder<
                    BoundStandardFieldProviderEdge?>(
                        sourceViewDeclaration.Fields.Length);
            for (var fieldOrdinal = 0;
                fieldOrdinal < sourceViewDeclaration.Fields.Length;
                fieldOrdinal++)
            {
                var field = sourceViewDeclaration.Fields[fieldOrdinal];
                var expectedFieldDefinitionId = 0x1400_0000u
                    | checked((uint)(localFieldStart + fieldOrdinal));
                var matchingFieldEdges = retainedFieldEdges.Where(edge =>
                        edge.ConsumerArtifactIndex
                            == viewModule.ArtifactIndex
                        && edge.ConsumerModuleRow == viewModule.ModuleRow
                        && edge.ExpectedDefinitionId
                            == expectedFieldDefinitionId
                        && edge.ExpectedOwnerTypeDefRow == localTypeDefRow)
                    .ToArray();
                if (matchingFieldEdges.Length > 1)
                {
                    throw Invalid(
                        "A contextual FieldDef view has multiple provider Imports.");
                }

                var fieldEdge = matchingFieldEdges.Length == 0
                    ? null : matchingFieldEdges[0];
                if (fieldEdge is not null)
                {
                    if ((fieldEdge.ActualDefinitionId >> 24) != 0x14
                        || !moduleByLocation.TryGetValue((
                            fieldEdge.ProviderArtifactIndex,
                            fieldEdge.ProviderModuleRow),
                            out var fieldProviderModule)
                        || !StringComparer.Ordinal.Equals(
                            fieldProviderModule.Identity,
                            fieldEdge.ProducerIdentity))
                    {
                        throw Invalid(
                            "A contextual FieldDef Import has an invalid provider identity.");
                    }

                    var fieldProviderSnapshot
                        = GetProviderSnapshot(fieldProviderModule);
                    var fieldProviderExports = GetExportKeys(
                        fieldProviderModule, fieldEdge.ActualDefinitionId);
                    if (!fieldProviderExports.Contains(fieldEdge.ExportKey,
                            StringComparer.Ordinal))
                    {
                        throw Invalid(
                            "A contextual FieldDef Import lost its selected ExportKey.");
                    }
                    var expectedField = field with {
                        StandardFieldOrigin = null,
                    };
                    var fieldOrigin = field.StandardFieldOrigin
                        ?? new BoundStandardFieldOrigin(
                            fieldProviderSnapshot,
                            fieldEdge.ActualDefinitionId,
                            fieldProviderExports, false, expectedField);
                    if (fieldOrigin.DefinitionId
                            != fieldEdge.ActualDefinitionId
                        || !SameA2SourceTypeProviderSnapshot(
                            fieldOrigin.Provider,
                            fieldProviderSnapshot))
                    {
                        throw Invalid(
                            "A contextual FieldDef view conflicts with its retained Import edge.");
                    }

                    field = field with {
                        StandardFieldOrigin = fieldOrigin with {
                            ExportKeys = fieldProviderExports,
                            IsLookupRoot = false,
                            ExpectedDeclaration = expectedField,
                            SelectedExportKeys = [],
                        },
                    };
                }

                viewFields.Add(field);
                fieldEdges.Add(fieldEdge);
            }

            var viewDeclaration = sourceViewDeclaration with {
                Fields = viewFields.MoveToImmutable(),
            };
            var remappedExpectedSource = BoundTreeTraversal.RewriteTypes(
                expectedSource, RemapViewType);
            var remappedExpected = remappedExpectedSource with {
                Fields = [.. remappedExpectedSource.Fields.Select(
                        static field => field with {
                            StandardFieldOrigin = null,
                        })],
                StandardTypeOrigin = null,
                StandardLayoutOnly = false,
            };
            contextualTypeViews.Add(new BoundStandardTypeView(
                projectedIndexByStructure[canonicalStructureIndex],
                GetProviderSnapshot(usingModule), expectation,
                actualOriginData.Provider, actualOriginData.DefinitionId,
                expectedDefinitionId, exportKey, isLookupRoot,
                viewDeclaration, remappedExpected, [], []));

            var fieldTypeViewIndexes = ImmutableArray.CreateBuilder<int>(
                localStructure.Fields.Length);
            foreach (var field in localStructure.Fields)
            {
                if (IsCombinedMethodScalarOrUnit(field.Type))
                {
                    fieldTypeViewIndexes.Add(-1);
                    continue;
                }

                var childStructureIndex = (int)field.Type - 100;
                if ((uint)childStructureIndex
                    >= (uint)viewModule.Program.Structures.Length)
                {
                    throw Unsupported(
                        "A contextual struct field uses an unsupported type family.");
                }

                fieldTypeViewIndexes.Add(GetOrAddTypeView(viewModule,
                    childStructureIndex));
            }

            contextualTypeViews[viewIndex] = contextualTypeViews[viewIndex]
                with {
                    FieldTypeViewIndexes
                        = fieldTypeViewIndexes.MoveToImmutable(),
                    FieldEdges = fieldEdges.MoveToImmutable(),
                };
            _ = contextualViewsInProgress.Remove(viewKey);
            return viewIndex;
        }

        foreach (var (origin, exportKeys) in selectedTypeKeysByOrigin)
        {
            if (!moduleByIdentity.TryGetValue(origin.ModuleIdentity,
                    out var selectedModule)
                || !typeProgramByLocation.TryGetValue((
                    selectedModule.ArtifactIndex,
                    selectedModule.ModuleRow), out var selectedProgram))
            {
                throw Invalid(
                    "A selected TypeDef view has no loaded provider Module.");
            }

            var typeDefRow = checked((int)(origin.DefinitionId
                & 0x00FF_FFFF));
            var (typeDefStart, typeDefEnd)
                = GetTables(selectedModule.ArtifactIndex).Modules
                    .GetTypeDefRange(selectedModule.ModuleRow);
            if (typeDefRow < typeDefStart || typeDefRow >= typeDefEnd)
            {
                throw Invalid(
                    "A selected TypeDef view is outside its provider Module.");
            }

            if (exportKeys.Count == 0)
            {
                throw Invalid(
                    "A selected TypeDef view has no retained ExportKey.");
            }
            _ = GetOrAddTypeView(selectedProgram,
                typeDefRow - typeDefStart);
        }

        foreach (var (function, exportKey) in selectedMethods)
        {
            for (var ordinal = 0; ordinal < function.ParameterCount;
                ordinal++)
            {
                var type = function.Variables[ordinal].Type;
                if (IsCombinedMethodScalarOrUnit(type))
                {
                    continue;
                }

                var viewIndex = GetOrAddTypeView(rootProviderModule,
                    (int)type - 100);
                methodTypeViews.Add(new BoundStandardMethodTypeView(
                    exportKey, ordinal, viewIndex));
            }
        }

        var typeDefEqualityEdgesByOwner = new Dictionary<(
            int ArtifactIndex, int OwnerModule),
            List<(int LeftTypeDef, int RightTypeDef)>>();
        for (var artifactIndex = 0;
            artifactIndex < artifacts.Length; artifactIndex++)
        {
            var equalityTables = GetTables(artifactIndex);
            var equalities = equalityTables.TypeDefBindingEqualities;
            for (var row = 1; row <= equalities.Count; row++)
            {
                var ownerModule = checked((int)
                    equalities.GetOwnerModule(row));
                var key = (artifactIndex, ownerModule);
                if (!typeDefEqualityEdgesByOwner.TryGetValue(key,
                        out var edges))
                {
                    edges = [];
                    typeDefEqualityEdgesByOwner.Add(key, edges);
                }

                edges.Add((
                    checked((int)equalities.GetLeftTypeDef(row)),
                    checked((int)equalities.GetRightTypeDef(row))));
            }
        }

        foreach (var (owner, edges) in typeDefEqualityEdgesByOwner)
        {
            var adjacent = new Dictionary<int, HashSet<int>>();
            foreach (var (left, right) in edges)
            {
                if (!adjacent.TryGetValue(left, out var leftNeighbors))
                {
                    leftNeighbors = [];
                    adjacent.Add(left, leftNeighbors);
                }
                if (!adjacent.TryGetValue(right,
                        out var rightNeighbors))
                {
                    rightNeighbors = [];
                    adjacent.Add(right, rightNeighbors);
                }

                _ = leftNeighbors.Add(right);
                _ = rightNeighbors.Add(left);
            }

            var visited = new HashSet<int>();
            foreach (var start in adjacent.Keys)
            {
                if (!visited.Add(start))
                {
                    continue;
                }

                var component = new List<int>();
                var pending = new Stack<int>();
                pending.Push(start);
                while (pending.TryPop(out var typeDefRow))
                {
                    component.Add(typeDefRow);
                    foreach (var neighbor in adjacent[typeDefRow])
                    {
                        if (visited.Add(neighbor))
                        {
                            pending.Push(neighbor);
                        }
                    }
                }

                if (component.Count < 2)
                {
                    continue;
                }

                var componentHasRetainedView = false;
                foreach (var typeDefRow in component)
                {
                    var typeTables = GetTables(owner.ArtifactIndex);
                    var usingModule = typeTables.TypeDefs
                        .GetOwnerModule(typeDefRow);
                    var importRow
                        = typeBindingByExpectedType.TryGetValue((
                            owner.ArtifactIndex, usingModule, typeDefRow),
                            out var binding)
                            ? binding.ImportRow : 0;
                    if (contextualViewIndexByModuleType.ContainsKey((
                            owner.ArtifactIndex, usingModule,
                            0x1300_0000u
                                | checked((uint)typeDefRow), importRow))
                    {
                        componentHasRetainedView = true;
                        break;
                    }
                }

                if (!componentHasRetainedView)
                {
                    continue;
                }

                foreach (var typeDefRow in component)
                {
                    var typeTables = GetTables(owner.ArtifactIndex);
                    var usingModule = typeTables.TypeDefs
                        .GetOwnerModule(typeDefRow);
                    if (!typeProgramByLocation.TryGetValue((
                            owner.ArtifactIndex, usingModule),
                            out var viewModule))
                    {
                        throw Invalid(
                            "A retained TypeDef equality endpoint has no loaded Module view.");
                    }

                    var (typeDefStart, typeDefEnd)
                        = typeTables.Modules.GetTypeDefRange(usingModule);
                    if (typeDefRow < typeDefStart
                        || typeDefRow >= typeDefEnd)
                    {
                        throw Invalid(
                            "A retained TypeDef equality endpoint is outside its owning Module.");
                    }

                    var viewIndex = GetOrAddTypeView(viewModule,
                        typeDefRow - typeDefStart);
                    if (!selectedStructures.Contains(
                            contextualTypeViews[viewIndex]
                                .StructureIndex))
                    {
                        throw Invalid(
                            "A retained TypeDef equality endpoint has no selected canonical actual type.");
                    }
                }
            }
        }

        var projectedStructures
            = ImmutableArray.CreateBuilder<BoundStruct>(
                structureOrder.Length);
        foreach (var structureIndex in structureOrder)
        {
            var original = program.Structures[structureIndex];
            var originalTypeOrigin = original.StandardTypeOrigin
                ?? throw Invalid(
                    "A selected struct lost its provider TypeDef origin.");
            var typeOriginKey = (originalTypeOrigin.Provider.ModuleIdentity,
                originalTypeOrigin.DefinitionId);
            if (!moduleByIdentity.TryGetValue(
                    originalTypeOrigin.Provider.ModuleIdentity,
                    out var providerModule))
            {
                throw Invalid(
                    "A selected struct provider is outside the checked Module directory.");
            }

            var providerTables = GetTables(providerModule.ArtifactIndex);
            var typeDefRow = checked((int)(
                originalTypeOrigin.DefinitionId & 0x00FF_FFFF));
            var (typeDefStart, typeDefEnd)
                = providerTables.Modules.GetTypeDefRange(
                    providerModule.ModuleRow);
            var (fieldStart, fieldEnd)
                = providerTables.TypeDefs.GetFieldRange(typeDefRow);
            var providerSnapshot = GetProviderSnapshot(providerModule);
            var selectedTypeKeys
                = selectedTypeKeysByOrigin.TryGetValue(typeOriginKey,
                    out var selectedKeys)
                    ? ImmutableArray.Create(selectedKeys[0])
                    : ImmutableArray<string>.Empty;
            if (!contextualViewIndexesByStructure.TryGetValue(
                    structureIndex, out var structureViewIndexes)
                || structureViewIndexes.Count == 0)
            {
                throw Invalid(
                    "A selected struct has no retained contextual TypeDef view.");
            }

            var contextualViewIndexes = structureViewIndexes.ToImmutableArray();
            var contextualViews = contextualViewIndexes.Select(
                viewIndex => contextualTypeViews[viewIndex]).ToArray();
            var primaryViewIndex = contextualViewIndexes[0];
            if (!selectedTypeKeys.IsEmpty)
            {
                var selectedViewIndexes = contextualViewIndexes.Where(
                    viewIndex =>
                    contextualTypeViews[viewIndex].IsLookupRoot
                    && StringComparer.Ordinal.Equals(
                        contextualTypeViews[viewIndex].ExportKey,
                        selectedTypeKeys[0])).ToArray();
                if (selectedViewIndexes.Length == 0)
                {
                    throw Invalid(
                        "A selected TypeDef root has no matching contextual view.");
                }

                primaryViewIndex = selectedViewIndexes[0];
            }
            var primaryView = contextualTypeViews[primaryViewIndex];
            var projectionSource = primaryView.ViewDeclaration;
            ImmutableArray<BoundSourceTypeExpectation> typeExpectations
                = [.. contextualViews
                    .Where(static view => view.Expectation is not null)
                    .Select(static view => view.Expectation!)];
            var referenceRoot = primaryView.UsingModule;
            var originExportKeys = selectedTypeKeys.IsEmpty
                ? [.. contextualViews.Select(static view => view.ExportKey)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)]
                : GetExportKeys(providerModule,
                    originalTypeOrigin.DefinitionId);

            if ((originalTypeOrigin.DefinitionId >> 24) != 0x13
                || typeDefRow < typeDefStart || typeDefRow >= typeDefEnd
                || providerTables.TypeDefs.GetOwnerModule(typeDefRow)
                    != providerModule.ModuleRow
                || providerTables.TypeDefs.GetKind(typeDefRow) != 1
                || !SameA2SourceTypeProviderSnapshot(
                    originalTypeOrigin.Provider, providerSnapshot)
                || fieldEnd - fieldStart != original.Fields.Length
                || projectionSource.Fields.Length != original.Fields.Length)
            {
                throw Invalid(
                    "A canonical struct origin does not match its provider-owned TypeDef and fields.");
            }

            PrimitiveType RemapProjectionType(PrimitiveType type)
                => type;

            var remappedFields = ImmutableArray.CreateBuilder<
                BoundStructField>(projectionSource.Fields.Length);
            for (var fieldOrdinal = 0;
                fieldOrdinal < projectionSource.Fields.Length;
                fieldOrdinal++)
            {
                var originalField = projectionSource.Fields[fieldOrdinal];
                var field = originalField with {
                    Type = RemapProjectionType(originalField.Type),
                    StandardFieldOrigin = null,
                };
                if (selectedFieldKeysByOwner.TryGetValue(
                        (structureIndex, fieldOrdinal),
                        out var selectedFieldKeys))
                {
                    var fieldRow = fieldStart + fieldOrdinal;
                    var fieldDefinitionId = 0x1400_0000u
                        | checked((uint)fieldRow);
                    var expectedField = field with {
                        StandardFieldOrigin = null,
                    };
                    var existingOrigin
                        = originalField.StandardFieldOrigin;
                    if (existingOrigin is not null
                        && (existingOrigin.DefinitionId
                                != fieldDefinitionId
                            || !SameA2SourceTypeProviderSnapshot(
                                existingOrigin.Provider,
                                providerSnapshot)))
                    {
                        throw Invalid(
                            "A selected FieldDef conflicts with its retained provider origin.");
                    }

                    var selectedKeys
                        = ImmutableArray.Create(selectedFieldKeys[0]);
                    var allFieldExportKeys = GetExportKeys(
                        providerModule, fieldDefinitionId);
                    BoundStandardFieldOrigin fieldOrigin;
                    if (existingOrigin is null)
                    {
                        fieldOrigin = new BoundStandardFieldOrigin(
                            providerSnapshot, fieldDefinitionId,
                            allFieldExportKeys, true, expectedField) {
                            SelectedExportKeys = selectedKeys,
                            AdditionalExpectedDeclarations = [],
                        };
                    }
                    else
                    {
                        var additionalExpected
                            = existingOrigin.AdditionalExpectedDeclarations;
                        fieldOrigin = existingOrigin with {
                            ExportKeys = allFieldExportKeys,
                            IsLookupRoot = true,
                            ExpectedDeclaration = expectedField,
                            SelectedExportKeys = selectedKeys,
                            AdditionalExpectedDeclarations
                                = additionalExpected.IsDefaultOrEmpty
                                    ? []
                                    : [.. additionalExpected.Select(
                                        expected => expected with {
                                            Type = RemapProjectionType(
                                                expected.Type),
                                            StandardFieldOrigin = null,
                                        })],
                        };
                    }
                    field = field with {
                        StandardFieldOrigin = fieldOrigin,
                    };
                }
                else if (originalField.StandardFieldOrigin is
                         BoundStandardFieldOrigin existingOrigin)
                {
                    BoundStructField? expectedField
                        = existingOrigin.ExpectedDeclaration is
                            BoundStructField priorExpected
                            ? priorExpected with {
                                Type = RemapProjectionType(
                                    priorExpected.Type),
                                StandardFieldOrigin = null,
                            }
                            : null;
                    var additionalExpected
                        = existingOrigin.AdditionalExpectedDeclarations;
                    field = field with {
                        StandardFieldOrigin = existingOrigin with {
                            IsLookupRoot = false,
                            ExpectedDeclaration = expectedField,
                            SelectedExportKeys = [],
                            AdditionalExpectedDeclarations
                                = additionalExpected.IsDefaultOrEmpty
                                    ? []
                                    : [.. additionalExpected.Select(
                                        expected => expected with {
                                            Type = RemapProjectionType(
                                                expected.Type),
                                            StandardFieldOrigin = null,
                                        })],
                        },
                    };
                }

                remappedFields.Add(field);
            }

            var declaredCopyEvidence
                = providerTables.TypeDefs.HasDeclaredCopy(typeDefRow);
            var expectedDeclaration = projectionSource with {
                Fields = [.. remappedFields.Select(static field => field with {
                    StandardFieldOrigin = null,
                })],
                StandardTypeOrigin = null,
                StandardLayoutOnly = false,
            };
            var orderedContextualViewIndexes
                = ImmutableArray.Create(primaryViewIndex).AddRange(
                    contextualViewIndexes.Where(viewIndex =>
                        viewIndex != primaryViewIndex));
            var additionalExpectedDeclarations
                = orderedContextualViewIndexes
                    .Where(viewIndex => viewIndex != primaryViewIndex)
                    .Select(viewIndex =>
                        contextualTypeViews[viewIndex].ExpectedDeclaration);
            if (!originalTypeOrigin.AdditionalExpectedDeclarations.IsDefaultOrEmpty)
            {
                additionalExpectedDeclarations
                    = originalTypeOrigin.AdditionalExpectedDeclarations
                        .Concat(additionalExpectedDeclarations);
            }
            var projectedStructure = projectionSource with {
                Fields = remappedFields.MoveToImmutable(),
                StandardTypeOrigin = originalTypeOrigin with {
                    Expectations = typeExpectations,
                    ExportKeys = originExportKeys,
                    IsLookupRoot = !selectedTypeKeys.IsEmpty,
                    ReferenceRoot = referenceRoot,
                    ExpectedDeclaration = expectedDeclaration,
                    SelectedExportKeys = selectedTypeKeys,
                    AdditionalExpectedDeclarations
                        = [.. additionalExpectedDeclarations],
                    ContextualViewIndexes = orderedContextualViewIndexes,
                },
                StandardLayoutOnly = true,
                DeclaredCopyEvidence = declaredCopyEvidence,
            };
            contextualTypeViews[primaryViewIndex]
                = contextualTypeViews[primaryViewIndex] with {
                    ViewDeclaration = projectedStructure with {
                        StandardTypeOrigin = null,
                        StandardLayoutOnly = false,
                    },
                    ExpectedDeclaration = expectedDeclaration,
                };
            projectedStructures.Add(projectedStructure);
        }

        var structures = projectedStructures.MoveToImmutable();
        var projectedFunctions
            = ImmutableArray.CreateBuilder<BoundFunction>(
                selectedMethods.Count);
        foreach (var (function, exportKey) in selectedMethods)
        {
            var signature = function with {
                Variables = function.Variables.Take(
                    function.ParameterCount).ToImmutableArray(),
                Body = new BoundBlock([], default),
                IndexedBody = null,
            };
            signature = BoundTreeTraversal.RewriteTypes(signature,
                RemapMethodType);
            projectedFunctions.Add(signature with {
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

        var functions = projectedFunctions.MoveToImmutable();
        var projectedProgram = new BoundProgram(functions, -1) {
            RuntimePolicy = (BoundRuntimePolicy)runtimePolicy,
            Structures = structures,
        };
        SemanticVerifier.VerifyLibraryReferences(projectedProgram);
        var sourceProviderClosures
            = ImmutableArray.CreateBuilder<
                BoundStandardFieldProviderClosure>();
        if (program.StandardQuerySourceAssociation is
            BoundStandardQuerySourceAssociation existingAssociation)
        {
            sourceProviderClosures.AddRange(
                existingAssociation.ProviderClosures);
        }
        if (program.StandardFieldProviderClosure is
                BoundStandardFieldProviderClosure fieldProviderClosure
            && !sourceProviderClosures.Any(closure =>
                ReferenceEquals(closure, fieldProviderClosure)))
        {
            sourceProviderClosures.Add(fieldProviderClosure);
        }

        var standardQuerySourceAssociation
            = sourceProviderClosures.Count == 0
                && contextualTypeViews.Count == 0
                ? null
                : new BoundStandardQuerySourceAssociation(
                    sourceProviderClosures.ToImmutable(),
                    [.. contextualTypeViews],
                    methodTypeViews.ToImmutable());
        return new CompilationReference(structures, functions,
            (BoundRuntimePolicy)runtimePolicy,
            standardQuerySourceAssociation:
                standardQuerySourceAssociation);
    }

    private static List<StandardSourceImportMapping>
        CreateC1M1MethodTypeSourceImportMappings(BoundProgram program,
            int[] typeDefStructureIndexes,
            int[] typeDefStructureViewIndexes,
            Dictionary<int, int> typeDefRowByContextualView,
            int[] structureFieldFirst,
            List<(string Text, int Encoding)> names)
    {
        if (program.StandardQuerySourceAssociation
            is not BoundStandardQuerySourceAssociation association
            || association.TypeViews.IsDefaultOrEmpty
            || typeDefStructureIndexes.Length
                != typeDefStructureViewIndexes.Length)
        {
            throw Invalid(
                "The contextual TypeDef Import writer has no complete view association.");
        }

        var mappings = new List<StandardSourceImportMapping>();
        var seen = new HashSet<(uint DefinitionId, string ProducerIdentity,
            string ExportKey)>();
        for (var typeDefIndex = 0;
            typeDefIndex < typeDefStructureIndexes.Length; typeDefIndex++)
        {
            var structureIndex = typeDefStructureIndexes[typeDefIndex];
            var typeViewIndex = typeDefStructureViewIndexes[typeDefIndex];
            if (typeViewIndex < 0
                || (uint)typeViewIndex >= (uint)association.TypeViews.Length
                || !typeDefRowByContextualView.TryGetValue(typeViewIndex,
                    out var typeDefRow)
                || typeDefRow != typeDefIndex + 1
                || (uint)structureIndex >= (uint)program.Structures.Length)
            {
                throw Invalid(
                    "A contextual TypeDef Import has no exact destination view.");
            }

            var view = association.TypeViews[typeViewIndex];
            var origin = program.Structures[structureIndex]
                .StandardTypeOrigin;
            var key = view.Expectation?.ExportKey ?? view.ExportKey;
            if (view.StructureIndex != structureIndex
                || origin is null
                || !SameA2SourceTypeProviderSnapshot(origin.Provider,
                    view.Provider)
                || origin.DefinitionId != view.ProviderDefinitionId
                || (view.ProviderDefinitionId >> 24) != 0x13
                || (view.ExpectedDefinitionId >> 24) != 0x13
                || string.IsNullOrEmpty(key)
                || !BoundEnumTypes.IsValidModuleDeclarationKey(key)
                || !origin.ExportKeys.Contains(key,
                    StringComparer.Ordinal))
            {
                throw Invalid(
                    "A contextual TypeDef Import lost its complete provider and ExportKey identity.");
            }

            if (view.Expectation is BoundSourceTypeExpectation expectation
                && (expectation.ExpectedDefinitionId
                        != view.ExpectedDefinitionId
                    || expectation.ProviderDefinitionId
                        != view.ProviderDefinitionId
                    || !StringComparer.Ordinal.Equals(
                        expectation.ProducerIdentity,
                        view.Provider.ModuleIdentity)
                    || !StringComparer.Ordinal.Equals(
                        expectation.ExportKey, key)
                    || !SameA2SourceTypeProviderSnapshot(
                        expectation.Provider, view.Provider)))
            {
                throw Invalid(
                    "A contextual TypeDef Import no longer matches its original expectation.");
            }
            if (view.Expectation is null && !view.IsLookupRoot)
            {
                throw Invalid(
                    "A contextual TypeDef without an original Import is not an explicitly selected lookup root.");
            }

            var expectedTypeDefId = 0x1300_0000u
                | checked((uint)typeDefRow);
            Add(view.Provider.ModuleIdentity, key, expectedTypeDefId,
                view.Provider);

            if (view.FieldTypeViewIndexes.Length
                    != view.ViewDeclaration.Fields.Length
                || view.FieldEdges.Length
                    != view.ViewDeclaration.Fields.Length)
            {
                throw Invalid(
                    "A contextual TypeDef view has incomplete field edge maps.");
            }

            for (var fieldOrdinal = 0;
                fieldOrdinal < view.FieldEdges.Length; fieldOrdinal++)
            {
                var fieldEdge = view.FieldEdges[fieldOrdinal];
                if (fieldEdge is null)
                {
                    continue;
                }

                var field = view.ViewDeclaration.Fields[fieldOrdinal];
                var fieldOrigin = field.StandardFieldOrigin;
                var fieldRow = structureFieldFirst[typeDefIndex] == 0
                    ? 0
                    : checked(structureFieldFirst[typeDefIndex]
                        + fieldOrdinal);
                var producer = fieldOrigin?.Provider;
                if (fieldRow == 0
                    || fieldOrigin is null
                    || producer is null
                    || (fieldEdge.ExpectedDefinitionId >> 24) != 0x14
                    || fieldEdge.ExpectedOwnerTypeDefRow
                        != checked((int)(view.ExpectedDefinitionId
                            & 0x00FF_FFFF))
                    || fieldEdge.ActualDefinitionId
                        != fieldOrigin.DefinitionId
                    || !StringComparer.Ordinal.Equals(
                        fieldEdge.ProducerIdentity,
                        producer.ModuleIdentity)
                    || !SameA2SourceTypeProviderSnapshot(producer,
                        view.Provider)
                    || fieldEdge.ProviderTypeDefRow
                        != checked((int)(view.ProviderDefinitionId
                            & 0x00FF_FFFF))
                    || !fieldOrigin.ExportKeys.Contains(
                        fieldEdge.ExportKey, StringComparer.Ordinal))
                {
                    throw Invalid(
                        "A contextual FieldDef Import lost its exact owning TypeDef provider edge.");
                }

                var expectedFieldDefId = 0x1400_0000u
                    | checked((uint)fieldRow);
                Add(fieldEdge.ProducerIdentity, fieldEdge.ExportKey,
                    expectedFieldDefId, producer);
            }
        }

        return mappings;

        void Add(string producerIdentity, string exportKey,
            uint expectedDefinitionId,
            BoundSourceTypeProviderSnapshot provider)
        {
            if (!StringComparer.Ordinal.Equals(provider.ModuleIdentity,
                    producerIdentity)
                || !seen.Add((expectedDefinitionId, producerIdentity,
                    exportKey)))
            {
                throw Invalid(
                    "A contextual Import has a conflicting provider or duplicate expected definition.");
            }

            var exportKeyStringRow = names.Count + 1;
            names.Add((exportKey, 1));
            mappings.Add(new StandardSourceImportMapping(
                producerIdentity, exportKey, expectedDefinitionId,
                exportKeyStringRow, provider));
        }
    }

    private static bool IsCombinedMethodScalarOrUnit(PrimitiveType type)
        => type == PrimitiveType.Void
            || DirectNativeImportContract.IsScalar(type)
                && !BoundEnumTypes.IsType(type);

    private static bool IsC1M1MethodTypeSchemaIdentity(
        string schemaIdentity)
        => schemaIdentity is
            StandardC1M1Construction2InitializerCopyEvidenceSchemaIdentity
            or StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceSchemaIdentity
            or StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceSchemaIdentity
            || StandardGenericMethodSchema.IsTypeBindingViews(schemaIdentity);
}
