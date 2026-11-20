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
            if (!canonicalStructureByOrigin.TryAdd(originKey,
                    structureIndex))
            {
                throw Invalid(
                    "The combined method/type reference has duplicate canonical struct origins.");
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
            if (!selectedTypeKeysByOrigin.ContainsKey(originKey)
                && !typeExpectationsByOrigin.ContainsKey(originKey))
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
            var projectionSource = original;
            BoundStandardMethodProviderModule? projectionModule = null;
            var typeExpectations
                = ImmutableArray<BoundSourceTypeExpectation>.Empty;
            var referenceRoot = providerSnapshot;
            var originExportKeys = GetExportKeys(providerModule,
                originalTypeOrigin.DefinitionId);
            if (selectedTypeKeys.IsEmpty)
            {
                var expectationsByImport = new Dictionary<(
                    string ModuleIdentity, int ModuleRow, int ImportRow,
                    uint ExpectedDefinitionId), BoundSourceTypeExpectation>();
                if (!originalTypeOrigin.Expectations.IsDefaultOrEmpty)
                {
                    foreach (var expectation in
                        originalTypeOrigin.Expectations)
                    {
                        AddTypeExpectation(expectationsByImport,
                            expectation);
                    }
                }

                if (typeExpectationsByOrigin.TryGetValue(typeOriginKey,
                        out var retainedExpectations))
                {
                    foreach (var expectation in retainedExpectations)
                    {
                        AddTypeExpectation(expectationsByImport,
                            expectation);
                    }
                }

                typeExpectations
                    = [.. expectationsByImport.Values];
                if (typeExpectations.IsEmpty)
                {
                    throw new NotSupportedException(
                        "A closure-only imported struct requires retained TypeDef Import expectations.");
                }

                var firstExpectation = typeExpectations[0];
                if (typeExpectations.Any(expectation =>
                        !SameA2SourceTypeProviderSnapshot(
                            expectation.UsingModule,
                            firstExpectation.UsingModule)
                        || expectation.ExpectedDefinitionId
                            != firstExpectation.ExpectedDefinitionId
                        || !SameA2SourceTypeProviderSnapshot(
                            expectation.Provider, providerSnapshot)
                        || expectation.ProviderDefinitionId
                            != originalTypeOrigin.DefinitionId))
                {
                    throw new NotSupportedException(
                        "A closure-only struct with multiple using-Module or expected-TypeDef contexts requires contextual TypeDef cloning.");
                }

                if (!moduleByIdentity.TryGetValue(
                        firstExpectation.UsingModule.ModuleIdentity,
                        out var expectationModule)
                    || expectationModule.ModuleRow
                        != firstExpectation.UsingModule.ModuleRow
                    || !typeProgramByLocation.TryGetValue((
                        expectationModule.ArtifactIndex,
                        expectationModule.ModuleRow),
                        out projectionModule))
                {
                    throw Invalid(
                        "A retained struct TypeDef expectation has no loaded using Module.");
                }

                var expectedTypeDefRow = checked((int)(
                    firstExpectation.ExpectedDefinitionId & 0x00FF_FFFF));
                var expectationTables = GetTables(
                    expectationModule.ArtifactIndex);
                var (expectedTypeDefStart, expectedTypeDefEnd)
                    = expectationTables.Modules.GetTypeDefRange(
                        expectationModule.ModuleRow);
                if ((firstExpectation.ExpectedDefinitionId >> 24) != 0x13
                    || expectedTypeDefRow < expectedTypeDefStart
                    || expectedTypeDefRow >= expectedTypeDefEnd
                    || expectedTypeDefEnd - expectedTypeDefStart
                        != projectionModule.Program.Structures.Length)
                {
                    throw Invalid(
                        "A retained struct TypeDef expectation is outside its using Module.");
                }

                var expectedStructure = projectionModule.Program.Structures[
                    expectedTypeDefRow - expectedTypeDefStart];
                var expectedOrigin = expectedStructure.StandardTypeOrigin;
                if (expectedOrigin is null
                    || expectedOrigin.DefinitionId
                        != originalTypeOrigin.DefinitionId
                    || !SameA2SourceTypeProviderSnapshot(
                        expectedOrigin.Provider, providerSnapshot))
                {
                    throw Invalid(
                        "A retained struct TypeDef expectation does not map to the canonical provider identity.");
                }

                projectionSource
                    = expectedOrigin.ExpectedDeclaration
                        ?? expectedStructure;
                referenceRoot = firstExpectation.UsingModule;
                originExportKeys = [
                    .. typeExpectations.Select(static expectation =>
                            expectation.ExportKey)
                        .Distinct(StringComparer.Ordinal),
                ];
            }

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
                => projectionModule is null
                    ? RemapCanonicalType(type)
                    : RemapModuleType(projectionModule, type);

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
                DeclaredCopyEvidence = declaredCopyEvidence,
            };
            projectedStructures.Add(projectionSource with {
                Fields = remappedFields.MoveToImmutable(),
                StandardTypeOrigin = originalTypeOrigin with {
                    Expectations = typeExpectations,
                    ExportKeys = originExportKeys,
                    IsLookupRoot = !selectedTypeKeys.IsEmpty,
                    ReferenceRoot = referenceRoot,
                    ExpectedDeclaration = expectedDeclaration,
                    SelectedExportKeys = selectedTypeKeys,
                    AdditionalExpectedDeclarations
                        = originalTypeOrigin.AdditionalExpectedDeclarations,
                },
                StandardLayoutOnly = true,
                DeclaredCopyEvidence = declaredCopyEvidence,
            });
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
                ? null
                : new BoundStandardQuerySourceAssociation(
                    sourceProviderClosures.ToImmutable());
        return new CompilationReference(structures, functions,
            (BoundRuntimePolicy)runtimePolicy,
            standardQuerySourceAssociation:
                standardQuerySourceAssociation);
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
            or StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceSchemaIdentity;
}
