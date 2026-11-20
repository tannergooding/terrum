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
    /// Creates one source reference from explicitly selected top-level methods,
    /// struct types, and fields in one classless Construction-2 Module.
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
        if (artifacts.Length != 1 || schemaIdentities.Length != 1
            || !IsC1M1MethodTypeSchemaIdentity(schemaIdentities[0]))
        {
            throw Unsupported(
                "The combined method/type reference requires one exact Construction-2 Copy-evidence artifact.");
        }

        if (typeExportKeys.Any(selection =>
                !StringComparer.Ordinal.Equals(selection.ModuleIdentity,
                    moduleIdentity))
            || fieldExportKeys.Any(selection =>
                !StringComparer.Ordinal.Equals(selection.ModuleIdentity,
                    moduleIdentity)))
        {
            throw Unsupported(
                "The initial combined method/type reference requires type and field selections from the method Module.");
        }

        var moduleValidation = ValidateStandardInputModules(artifacts,
            schemaIdentities, moduleIdentity, a3LayoutResourceLedger: null);
        if (moduleValidation.Modules.Length != 1
            || moduleValidation.Modules[0].ArtifactIndex != 0
            || moduleValidation.Modules[0].ModuleRow
                != moduleValidation.RootModuleRow)
        {
            throw Unsupported(
                "The initial combined method/type reference requires one selected Module.");
        }

        var tables = ValidateForeignInvocationArtifact(
            CreateForeignInvocationIndex(artifacts[0],
                schemaIdentities[0]), schemaIdentities[0], null,
            createA3ResourceLedger: false);
        var moduleRow = moduleValidation.RootModuleRow;
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(moduleRow);
        var (typeDefStart, typeDefEnd)
            = tables.Modules.GetTypeDefRange(moduleRow);
        if (targetRequirement
                != TrRmModuleTableReader.WindowsX64NativeTargetRequirement
            || runtimePolicy != 0
            || tables.Imports.Count != 0
            || tables.ModuleRefs.Count != 0
            || tables.Modules.Count != 1
            || moduleRow != 1
            || typeDefStart <= 0 || typeDefEnd < typeDefStart
            || Enumerable.Range(typeDefStart,
                typeDefEnd - typeDefStart)
                .Any(typeDefRow =>
                    tables.TypeDefs.GetKind(typeDefRow) != 1))
        {
            throw Unsupported(
                "The initial combined method/type reference requires a classless Windows x64 Module without Imports.");
        }

        var program = LoadLocalAggregateLayoutProviderClosure(artifacts,
            schemaIdentities, moduleIdentity, moduleValidation, [],
            construction2MethodLinking: true);
        var methodProviderClosure
            = program.StandardMethodProviderClosure
                ?? throw Invalid(
                    "The selected Module has no retained MethodDef origin map.");
        var rootProviderModule = methodProviderClosure.Modules.SingleOrDefault(
            module => module.ArtifactIndex == 0
                && module.ModuleRow == moduleValidation.RootModuleRow)
            ?? throw Invalid(
                "The selected MethodDef origin map lost its root Module.");
        var (methodStart, methodEnd)
            = tables.Modules.GetMethodDefRange(moduleRow);
        if (typeDefEnd - typeDefStart
                != rootProviderModule.Program.Structures.Length
            || Enumerable.Range(typeDefStart, typeDefEnd - typeDefStart)
                .Any(typeDefRow =>
                    tables.TypeDefs.GetKind(typeDefRow) != 1))
        {
            throw Unsupported(
                "The initial combined method/type reference requires a TypeDef-aligned struct Module.");
        }

        var providerSnapshot = CreateA2SourceTypeProviderSnapshot(tables,
            artifacts[0], schemaIdentities[0], moduleRow);
        var allTypeExports = new Dictionary<int, List<string>>();
        var allFieldExports = new Dictionary<int, List<string>>();
        foreach (var (definition, exportKey) in
            tables.Exports.GetExportsForModule(moduleRow))
        {
            var kind = definition >> 24;
            var row = checked((int)(definition & 0x00FF_FFFF));
            var exports = kind switch {
                0x13 => allTypeExports,
                0x14 => allFieldExports,
                _ => null,
            };
            if (exports is null)
            {
                continue;
            }

            if (!exports.TryGetValue(row, out var keys))
            {
                keys = [];
                exports.Add(row, keys);
            }
            keys.Add(exportKey);
        }

        var selectedTypeKeysByRow = new Dictionary<int, List<string>>();
        foreach (var selection in typeExportKeys)
        {
            if (!tables.Exports.TryGetDefinitionForKey(moduleRow,
                    selection.ExportKey, out var definition)
                || (definition >> 24) != 0x13)
            {
                throw new ArgumentException(
                    "A selected ExportKey does not identify a TypeDef in the selected Module.",
                    nameof(typeExportKeys));
            }

            var typeDefRow = checked((int)(definition & 0x00FF_FFFF));
            var (fieldStart, fieldEnd) = tables.TypeDefs.GetFieldRange(
                typeDefRow);
            var (methodRangeStart, methodRangeEnd)
                = tables.TypeDefs.GetMethodRange(typeDefRow);
            if (typeDefRow < typeDefStart || typeDefRow >= typeDefEnd
                || tables.TypeDefs.GetOwnerModule(typeDefRow) != moduleRow
                || tables.Imports.IsExpectedDefinition(definition)
                || tables.TypeDefs.GetKind(typeDefRow) != 1
                || (tables.TypeDefs.GetFlags(typeDefRow) & 3) != 2
                || tables.TypeDefs.GetGenericRange(typeDefRow).Count != 0
                || tables.TypeDefs.HasDeclaredUnique(typeDefRow)
                || methodRangeStart != methodRangeEnd
                || !tables.TypeDefs.HasDeclaredCopy(typeDefRow)
                || !tables.Layouts.HasBlittableLayout(typeDefRow,
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader
                        .WindowsX64NativeProfileRuleRevision))
            {
                throw Invalid(
                    "A selected TypeDef Export does not identify a local public nongeneric Copy struct with the required Blittable layout.");
            }

            var structureIndex = typeDefRow - typeDefStart;
            var structure
                = rootProviderModule.Program.Structures[structureIndex];
            if (structure.IsPrivate || structure.IsInternal
                || structure.IsUnique
                || structure.CleanupMethod is not null
                || structure.SourceGenericTypeDefinitionIndex >= 0
                || !structure.IsSequential && !structure.IsTransparent
                || structure.BlittableTargetRequirement
                    != FixedLayoutStorage.WindowsX64TargetRequirement
                || structure.DeclaredCopyEvidence != true
                || fieldEnd - fieldStart != structure.Fields.Length)
            {
                throw Invalid(
                    "A selected struct lost its exact provider origin, Copy evidence, or Blittable field layout.");
            }

            if (!selectedTypeKeysByRow.TryGetValue(typeDefRow,
                    out var selectedKeys))
            {
                selectedKeys = [];
                selectedTypeKeysByRow.Add(typeDefRow, selectedKeys);
            }
            selectedKeys.Add(selection.ExportKey);
        }

        var selectedFieldKeysByOwner = new Dictionary<
            (int TypeDefRow, int FieldOrdinal), List<string>>();
        foreach (var selection in fieldExportKeys)
        {
            if (!tables.Exports.TryGetDefinitionForKey(moduleRow,
                    selection.ExportKey, out var definition)
                || (definition >> 24) != 0x14)
            {
                throw new ArgumentException(
                    "A selected ExportKey does not identify a FieldDef in the selected Module.",
                    nameof(fieldExportKeys));
            }

            var fieldRow = checked((int)(definition & 0x00FF_FFFF));
            var ownerTypeDefRow = tables.Fields.GetOwnerTypeDef(fieldRow);
            var (fieldStart, fieldEnd)
                = tables.TypeDefs.GetFieldRange(ownerTypeDefRow);
            var fieldFlags = tables.Fields.GetFlags(fieldRow);
            if (!selectedTypeKeysByRow.ContainsKey(ownerTypeDefRow)
                || tables.TypeDefs.GetOwnerModule(ownerTypeDefRow)
                    != moduleRow
                || tables.TypeDefs.GetKind(ownerTypeDefRow) != 1
                || tables.Imports.IsExpectedDefinition(
                    0x1300_0000u | checked((uint)ownerTypeDefRow))
                || (fieldFlags & 3) != 2
                || (fieldFlags & 8) != 0
                || fieldRow < fieldStart || fieldRow >= fieldEnd
                || tables.Fields.GetOwnerTypeDef(fieldRow)
                    != ownerTypeDefRow)
            {
                throw new ArgumentException(
                    "A selected FieldDef must be a public instance field of an explicitly selected local TypeDef.",
                    nameof(fieldExportKeys));
            }

            var fieldOrdinal = fieldRow - fieldStart;
            var ownerIndex = ownerTypeDefRow - typeDefStart;
            if ((uint)ownerIndex >= (uint)rootProviderModule
                    .Program.Structures.Length
                || (uint)fieldOrdinal
                    >= (uint)rootProviderModule
                        .Program.Structures[ownerIndex].Fields.Length)
            {
                throw Invalid(
                    "A selected FieldDef is outside its loaded owner.");
            }

            var key = (ownerTypeDefRow, fieldOrdinal);
            if (!selectedFieldKeysByOwner.TryGetValue(key,
                    out var selectedKeys))
            {
                selectedKeys = [];
                selectedFieldKeysByOwner.Add(key, selectedKeys);
            }
            selectedKeys.Add(selection.ExportKey);
        }

        var selectedStructures = new HashSet<int>();
        var pendingStructures = new Stack<int>();
        foreach (var typeDefRow in selectedTypeKeysByRow.Keys)
        {
            var structureIndex = typeDefRow - typeDefStart;
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

                var structureIndex = (int)variable.Type - 100;
                if ((uint)structureIndex
                    >= (uint)rootProviderModule.Program.Structures.Length)
                {
                    throw Unsupported(
                        "A selected method parameter uses an unsupported type family.");
                }

                _ = selectedStructures.Add(structureIndex);
                pendingStructures.Push(structureIndex);
            }

            if (function.OrdinaryParameterDefaults.Any(defaultValue =>
                    defaultValue.ParameterOrdinal >= 0
                    && defaultValue.ParameterOrdinal
                        < function.ParameterCount
                    && (int)function.Variables[
                        defaultValue.ParameterOrdinal].Type >= 100))
            {
                throw Unsupported(
                    "The combined method/type reference does not admit struct-valued parameter defaults.");
            }

            selectedMethods.Add((function, exportKey));
        }

        while (pendingStructures.TryPop(out var structureIndex))
        {
            var typeDefRow = typeDefStart + structureIndex;
            if (!selectedTypeKeysByRow.ContainsKey(typeDefRow))
            {
                throw new ArgumentException(
                    "Every selected method and nested struct dependency requires an explicit TypeDef ExportKey selection.",
                    nameof(typeExportKeys));
            }

            var structure
                = rootProviderModule.Program.Structures[structureIndex];
            foreach (var field in structure.Fields)
            {
                if (IsCombinedMethodScalarOrUnit(field.Type))
                {
                    continue;
                }

                var nestedStructureIndex = (int)field.Type - 100;
                if ((uint)nestedStructureIndex
                    >= (uint)rootProviderModule.Program.Structures.Length)
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

        PrimitiveType RemapType(PrimitiveType type)
        {
            if (IsCombinedMethodScalarOrUnit(type))
            {
                return type;
            }

            var structureIndex = (int)type - 100;
            if ((uint)structureIndex >= (uint)rootProviderModule
                    .Program.Structures.Length
                || !projectedIndexByStructure.TryGetValue(structureIndex,
                    out var projectedIndex))
            {
                throw Invalid(
                    "A selected method/type signature is missing its struct identity mapping.");
            }

            return (PrimitiveType)(100 + projectedIndex);
        }

        var projectedStructures
            = ImmutableArray.CreateBuilder<BoundStruct>(
                structureOrder.Length);
        foreach (var structureIndex in structureOrder)
        {
            var typeDefRow = typeDefStart + structureIndex;
            var original
                = rootProviderModule.Program.Structures[structureIndex];
            var (fieldStart, _) = tables.TypeDefs.GetFieldRange(typeDefRow);
            var remappedFields = ImmutableArray.CreateBuilder<
                BoundStructField>(original.Fields.Length);
            for (var fieldOrdinal = 0;
                fieldOrdinal < original.Fields.Length; fieldOrdinal++)
            {
                var originalField = original.Fields[fieldOrdinal];
                var field = originalField with {
                    Type = RemapType(originalField.Type),
                    StandardFieldOrigin = null,
                };
                if (selectedFieldKeysByOwner.TryGetValue(
                        (typeDefRow, fieldOrdinal),
                        out var selectedKeys))
                {
                    var fieldRow = fieldStart + fieldOrdinal;
                    var fieldDefinitionId = 0x1400_0000u
                        | checked((uint)fieldRow);
                    var expectedField = field with {
                        StandardFieldOrigin = null,
                    };
                    field = field with {
                        StandardFieldOrigin =
                            new BoundStandardFieldOrigin(
                                providerSnapshot, fieldDefinitionId,
                                [.. allFieldExports[fieldRow]], true,
                                expectedField) {
                                SelectedExportKeys = [.. selectedKeys],
                                AdditionalExpectedDeclarations = [],
                            },
                    };
                }

                remappedFields.Add(field);
            }

            var expectedDeclaration = original with {
                Fields = [.. remappedFields.Select(static field => field with {
                    StandardFieldOrigin = null,
                })],
                StandardTypeOrigin = null,
                StandardLayoutOnly = false,
                DeclaredCopyEvidence
                    = tables.TypeDefs.HasDeclaredCopy(typeDefRow),
            };
            var typeDefinitionId = 0x1300_0000u
                | checked((uint)typeDefRow);
            var selectedTypeKeys = selectedTypeKeysByRow.TryGetValue(
                typeDefRow, out var selectedKeys)
                ? ImmutableArray.CreateRange(selectedKeys)
                : ImmutableArray<string>.Empty;
            projectedStructures.Add(original with {
                Fields = remappedFields.MoveToImmutable(),
                StandardTypeOrigin = new BoundStandardTypeOrigin(
                    providerSnapshot, typeDefinitionId,
                    [.. allTypeExports[typeDefRow]],
                    selectedTypeKeysByRow.ContainsKey(typeDefRow),
                    [],
                    ReferenceRoot: providerSnapshot,
                    ExpectedDeclaration: expectedDeclaration) {
                    SelectedExportKeys = selectedTypeKeys,
                    AdditionalExpectedDeclarations = [],
                },
                StandardLayoutOnly = true,
                DeclaredCopyEvidence
                    = tables.TypeDefs.HasDeclaredCopy(typeDefRow),
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
                RemapType);
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
        return new CompilationReference(structures, functions,
            (BoundRuntimePolicy)runtimePolicy);
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
