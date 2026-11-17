// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    /// <summary>Creates a source-free reference to a G1L1 Module's exported generic MethodDefs and exported interfaces.</summary>
    /// <param name="providerArtifact">The exact G1L1 provider artifact.</param>
    /// <param name="moduleIdentity">The exact identity of the provider Module.</param>
    /// <returns>A source reference retaining selected open signatures, Bodies, and exported interface declarations.</returns>
    public static CompilationReference CreateGenericMethodCallsDraftReference(
        byte[] providerArtifact, string moduleIdentity)
    {
        ArgumentNullException.ThrowIfNull(providerArtifact);
        return CreateGenericMethodCallsDraftReference(
            new TrRmStandardArtifactInput(
                StandardGenericMethodCallsParameterLabelsSchemaIdentity,
                providerArtifact),
            moduleIdentity, [], exportKey: null);
    }

    /// <summary>Creates a source-free reference to one exported G1L1 generic MethodDef and the Module's exported interface declarations.</summary>
    /// <param name="providerArtifact">The exact G1L1 provider artifact.</param>
    /// <param name="moduleIdentity">The exact identity of the provider Module.</param>
    /// <param name="exportKey">The exact generic MethodDef ExportKey.</param>
    /// <returns>A source reference retaining the open signature, checked Body, and exported interface declarations.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">An identity or ExportKey is empty.</exception>
    /// <exception cref="NotSupportedException">The selected G1L1 declaration is outside the implemented import subset.</exception>
    /// <exception cref="InvalidDataException">The artifact or selected declaration is invalid.</exception>
    public static CompilationReference CreateGenericMethodCallsDraftReference(
        byte[] providerArtifact, string moduleIdentity, string exportKey)
    {
        ArgumentNullException.ThrowIfNull(providerArtifact);
        ArgumentException.ThrowIfNullOrEmpty(exportKey);
        return CreateGenericMethodCallsDraftReference(
            new TrRmStandardArtifactInput(
                StandardGenericMethodCallsParameterLabelsSchemaIdentity,
                providerArtifact),
            moduleIdentity, [], exportKey);
    }

    private static CompilationReference CreateGenericMethodCallsDraftReference(
        TrRmStandardArtifactInput input, string moduleIdentity,
        IReadOnlyList<TrRmStandardArtifactInput> providers,
        string? exportKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        var context = new GenericMethodReadContext(input, providers);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                new TrRmArtifactIndex(context.Artifacts[0],
                    context.Schema));
        var moduleRow = FindGenericMethodModule(tables.Modules,
            moduleIdentity);
        ValidateGenericMethodModuleImports(tables, context, 0, moduleRow);
        var imports = ImmutableArray.CreateBuilder<
            BoundGenericMethodImport>();
        if (exportKey is not null)
        {
            if (!tables.Exports.TryGetDefinitionForKey(moduleRow, exportKey,
                    out var definitionId)
                || (definitionId >> 24) != 0x16)
            {
                throw new InvalidDataException(
                    "The selected G1 ExportKey does not name a MethodDef.");
            }

            imports.Add(CreateGenericMethodImport(tables, context, 0,
                moduleRow, definitionId, exportKey,
                isMethodDefTarget: false));
        }
        else
        {
            foreach (var (definition, key)
                in tables.Exports.GetExportsForModule(moduleRow))
            {
                if ((definition >> 24) != 0x16)
                {
                    continue;
                }

                var methodRow = checked((int)(definition & 0x00FF_FFFF));
                var signatureRow = tables.Methods.GetSignature(methodRow);
                if (tables.Signatures.GetGenericCount(signatureRow) == 0)
                {
                    continue;
                }

                imports.Add(CreateGenericMethodImport(tables, context, 0,
                    moduleRow, definition, key,
                    isMethodDefTarget: false));
            }
        }

        var interfaces = CreateGenericInterfaceDefinitions(tables,
            context, 0, moduleRow);
        context.CompleteBindings();
        var (_, runtimePolicy) = tables.Modules.GetRequirements(moduleRow);
        return new CompilationReference([], [],
            (BoundRuntimePolicy)runtimePolicy,
            interfaces: interfaces,
            genericMethodImports: imports.ToImmutable(),
            spanTypes: [.. context.SpanTypes],
            checkedReferenceTypes:
                [.. context.CheckedReferenceTypeDefinitions],
            genericNominalTypeDefinitions:
                context.GenericNominalTypeDefinitions.ToImmutable(),
            genericNominalTypeInstances:
                context.GenericNominalTypeInstances.ToImmutable(),
            nativePointerTypes:
                context.GenericNominalPointerTypes.ToImmutable());
    }

    private static ImmutableArray<BoundInterfaceDefinition>
        CreateGenericInterfaceDefinitions(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            GenericMethodReadContext context, int artifactIndex,
            int moduleRow)
    {
        var cacheKey = (artifactIndex, moduleRow);
        if (context.InterfaceDefinitions.TryGetValue(cacheKey,
                out var cachedDefinitions))
        {
            return cachedDefinitions;
        }

        if (!context.Schema.UsesMethodParameterLabels())
        {
            var (legacyTypeStart, legacyTypeEnd)
                = tables.Modules.GetTypeDefRange(moduleRow);
            for (var typeDef = legacyTypeStart;
                typeDef < legacyTypeEnd; typeDef++)
            {
                var interfaceDef
                    = tables.Interfaces.GetInterfaceDefForTypeDef(typeDef);
                if (interfaceDef == 0)
                {
                    continue;
                }

                if (tables.Exports.HasExportForTypeDef(typeDef)
                    || tables.Imports.IsExpectedDefinition(
                        0x1300_0000u | checked((uint)typeDef))
                    || tables.Imports.IsExpectedDefinition(
                        0x2000_0000u | checked((uint)interfaceDef)))
                {
                    throw Unsupported(
                        "A G1 interface source view requires exact G1L1 label metadata.");
                }
            }

            context.InterfaceDefinitions.Add(cacheKey, []);
            return [];
        }

        var exportedDefinitions
            = CreateExportedGenericInterfaceDefinitions(tables, context,
                artifactIndex, moduleRow);
        if (exportedDefinitions.Length
            > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "The G1L1 interface source view exceeds the supported declaration limit.");
        }

        var definitions = ImmutableArray.CreateBuilder<
            BoundInterfaceDefinition>(exportedDefinitions.Length);
        definitions.AddRange(exportedDefinitions);
        var importRows = CreateGenericInterfaceImportRows(tables, moduleRow);
        var usedInterfaceImportRows = new HashSet<int>();
        var (firstTypeDef, endTypeDef)
            = tables.Modules.GetTypeDefRange(moduleRow);
        for (var typeDef = firstTypeDef; typeDef < endTypeDef; typeDef++)
        {
            var interfaceDef
                = tables.Interfaces.GetInterfaceDefForTypeDef(typeDef);
            if (interfaceDef == 0)
            {
                continue;
            }

            if (tables.Exports.HasExportForTypeDef(typeDef))
            {
                if (tables.Imports.IsExpectedDefinition(
                        0x2000_0000u | checked((uint)interfaceDef)))
                {
                    throw Invalid(
                        "A G1L1 exported interface cannot also have an InterfaceDef Import expectation.");
                }

                continue;
            }

            var typeDefId = 0x1300_0000u | checked((uint)typeDef);
            if (!tables.Imports.IsExpectedDefinition(typeDefId))
            {
                continue;
            }

            if (definitions.Count >= SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Unsupported(
                    "The G1L1 interface source view exceeds the supported declaration limit.");
            }

            var definition = BindExpectedGenericInterfaceDefinition(tables,
                context, moduleRow, interfaceDef, importRows,
                usedInterfaceImportRows);
            if (!context.InterfaceDefinitionsByTypeDef.TryAdd(
                    (artifactIndex, moduleRow, typeDef), definition))
            {
                throw Invalid(
                    "A G1L1 InterfaceDef has multiple imported declaration views.");
            }

            definitions.Add(definition);
        }

        foreach (var (definitionId, importRow) in importRows)
        {
            var definitionKind = definitionId >> 24;
            var definitionRow = checked((int)(
                definitionId & 0x00FF_FFFF));
            var belongsToInterface = definitionKind switch {
                0x13 => tables.Interfaces
                    .GetInterfaceDefForTypeDef(definitionRow) != 0,
                0x16 => tables.InterfaceMethods
                    .GetOwnerInterfaceDefForMethod(definitionRow) != 0,
                0x20 => true,
                _ => false,
            };
            if (belongsToInterface
                && !usedInterfaceImportRows.Contains(importRow))
            {
                throw Invalid(
                    "A G1L1 interface Import expectation is unused by a retained declaration.");
            }
        }

        var result = definitions.ToImmutable();
        context.InterfaceDefinitions.Add(cacheKey, result);
        return result;
    }

    private static ImmutableArray<BoundInterfaceDefinition>
        CreateExportedGenericInterfaceDefinitions(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            GenericMethodReadContext context, int artifactIndex,
            int moduleRow)
    {
        var cacheKey = (artifactIndex, moduleRow);
        if (context.ExportedInterfaceDefinitions.TryGetValue(cacheKey,
                out var cachedDefinitions))
        {
            return cachedDefinitions;
        }

        ValidateG1InterfaceMethodClosure(tables, context, artifactIndex,
            moduleRow);
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(moduleRow);
        var moduleIdentity = tables.Modules.GetIdentity(moduleRow);
        var exportedInterfaceDefinitions = new HashSet<uint>();
        var exportedInterfaceCount = 0;
        var (firstTypeDef, endTypeDef)
            = tables.Modules.GetTypeDefRange(moduleRow);
        for (var typeDef = firstTypeDef; typeDef < endTypeDef; typeDef++)
        {
            var interfaceDef
                = tables.Interfaces.GetInterfaceDefForTypeDef(typeDef);
            if (interfaceDef == 0)
            {
                continue;
            }

            if (tables.Exports.HasExportForTypeDef(typeDef))
            {
                exportedInterfaceCount = checked(
                    exportedInterfaceCount + 1);
                if (exportedInterfaceCount
                    > SemanticVerifier.MaximumAnalysisVisits)
                {
                    throw Unsupported(
                        "The G1L1 interface source view exceeds the supported declaration limit.");
                }

                _ = exportedInterfaceDefinitions.Add(
                    0x1300_0000u | checked((uint)typeDef));
            }

            var (firstMethod, methodCount)
                = tables.Interfaces.GetMethodRange(interfaceDef);
            for (var methodOrdinal = 0;
                methodOrdinal < methodCount; methodOrdinal++)
            {
                var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                    firstMethod + methodOrdinal);
                if (tables.Exports.HasExportForMethod(methodRow))
                {
                    _ = exportedInterfaceDefinitions.Add(
                        0x1600_0000u | checked((uint)methodRow));
                }
            }
        }

        var exportKeys = new Dictionary<uint, string>(
            exportedInterfaceDefinitions.Count);
        foreach (var (definition, key)
            in tables.Exports.GetExportsForModule(moduleRow))
        {
            if (exportedInterfaceDefinitions.Contains(definition)
                && !exportKeys.TryAdd(definition, key))
            {
                throw Invalid(
                    "A G1L1 interface declaration has an ambiguous ExportKey.");
            }
        }

        var definitions = ImmutableArray.CreateBuilder<
            BoundInterfaceDefinition>(exportedInterfaceCount);
        for (var typeDef = firstTypeDef; typeDef < endTypeDef; typeDef++)
        {
            var interfaceDef
                = tables.Interfaces.GetInterfaceDefForTypeDef(typeDef);
            if (interfaceDef == 0)
            {
                continue;
            }

            var typeDefId = 0x1300_0000u | checked((uint)typeDef);
            if (!tables.Exports.HasExportForTypeDef(typeDef))
            {
                continue;
            }

            if (!exportKeys.TryGetValue(typeDefId, out var typeExportKey))
            {
                throw Invalid(
                    "A G1L1 InterfaceDef TypeDef has an ambiguous ExportKey.");
            }

            var (firstMethod, methodCount)
                = tables.Interfaces.GetMethodRange(interfaceDef);
            var methodExportKeys
                = ImmutableArray.CreateBuilder<string>(methodCount);
            for (var methodOrdinal = 0;
                methodOrdinal < methodCount; methodOrdinal++)
            {
                var interfaceMethodRow = firstMethod + methodOrdinal;
                if (tables.InterfaceMethods.GetOwnerInterfaceDef(
                        interfaceMethodRow) != interfaceDef
                    || tables.InterfaceMethods.GetOrdinal(
                        interfaceMethodRow) != methodOrdinal)
                {
                    throw Invalid(
                        "A G1L1 InterfaceMethod range is not declaration-ordered.");
                }

                var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                    interfaceMethodRow);
                var methodId = 0x1600_0000u
                    | checked((uint)methodRow);
                if (!exportKeys.TryGetValue(methodId, out var methodExportKey))
                {
                    throw Invalid(
                        "A G1L1 InterfaceMethod has no unique MethodDef ExportKey.");
                }

                methodExportKeys.Add(methodExportKey);
            }

            var definition = ReadG1InterfaceDefinition(tables, interfaceDef,
                moduleIdentity, typeExportKey, targetRequirement,
                runtimePolicy, methodExportKeys.MoveToImmutable());
            if (!context.InterfaceDefinitionsByTypeDef.TryAdd(
                    (artifactIndex, moduleRow, typeDef), definition))
            {
                throw Invalid(
                    "A G1L1 TypeDef has multiple exported interface declaration views.");
            }

            definitions.Add(definition);
        }

        var result = definitions.ToImmutable();
        foreach (var definition in result)
        {
            var exportKey = definition.ModuleImport?.ExportKey
                ?? throw Invalid(
                    "A G1L1 exported interface has no TypeDef ExportKey.");
            if (!context.ExportedInterfaceDefinitionsByKey.TryAdd(
                    (artifactIndex, moduleRow, exportKey), definition))
            {
                throw Invalid(
                    "A G1L1 interface ExportKey selects multiple declarations.");
            }
        }

        context.ExportedInterfaceDefinitions.Add(cacheKey, result);
        return result;
    }

    private static void ValidateG1InterfaceMethodClosure(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow)
    {
        if (!context.ModulesWithValidatedG1InterfaceMethodClosure.Add(
                (artifactIndex, moduleRow)))
        {
            return;
        }

        var supportedInterfaces = new HashSet<int>();
        var analysisVisits = 0;
        var (firstTypeDef, endTypeDef)
            = tables.Modules.GetTypeDefRange(moduleRow);
        for (var typeDef = firstTypeDef; typeDef < endTypeDef; typeDef++)
        {
            var interfaceDef
                = tables.Interfaces.GetInterfaceDefForTypeDef(typeDef);
            if (interfaceDef == 0)
            {
                continue;
            }

            var typeDefId = 0x1300_0000u | checked((uint)typeDef);
            var interfaceDefId = 0x2000_0000u
                | checked((uint)interfaceDef);
            if (!tables.Exports.HasExportForTypeDef(typeDef)
                && !tables.Imports.IsExpectedDefinition(typeDefId)
                && !tables.Imports.IsExpectedDefinition(interfaceDefId))
            {
                continue;
            }

            if (!supportedInterfaces.Add(interfaceDef)
                || ++analysisVisits
                    > SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Unsupported(
                    "The G1L1 interface source view exceeds the supported declaration limit.");
            }
            var (_, formalCount)
                = tables.TypeDefs.GetGenericRange(typeDef);
            if (formalCount > SemanticVerifier.MaximumArguments
                || formalCount > SemanticVerifier.MaximumAnalysisVisits
                    - analysisVisits)
            {
                throw Unsupported(
                    "The G1L1 interface formals exceed the supported analysis limit.");
            }
            analysisVisits = checked(analysisVisits + formalCount);

            var (firstMethod, methodCount)
                = tables.Interfaces.GetMethodRange(interfaceDef);
            if (methodCount > SemanticVerifier.MaximumAnalysisVisits
                - analysisVisits)
            {
                throw Unsupported(
                    "The G1L1 interface requirements exceed the supported analysis limit.");
            }
            analysisVisits = checked(analysisVisits + methodCount);
            for (var ordinal = 0; ordinal < methodCount; ordinal++)
            {
                var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                    firstMethod + ordinal);
                var signatureRow = tables.Methods.GetSignature(methodRow);
                var (_, parameterCount)
                    = tables.Signatures.GetParamRange(signatureRow);
                if (parameterCount > SemanticVerifier.MaximumArguments
                    || parameterCount
                        > SemanticVerifier.MaximumAnalysisVisits
                            - analysisVisits)
                {
                    throw Unsupported(
                        "The G1L1 interface parameters exceed the supported analysis limit.");
                }
                analysisVisits = checked(analysisVisits + parameterCount);
            }
        }

        var (firstModuleMethod, endModuleMethod)
            = tables.Modules.GetMethodDefRange(moduleRow);
        for (var methodRow = firstModuleMethod;
            methodRow < endModuleMethod; methodRow++)
        {
            var ownerType = tables.Methods.GetOwnerType(methodRow);
            if (ownerType == 0
                || tables.TypeDefs.GetOwnerModule(ownerType) != moduleRow)
            {
                continue;
            }

            var interfaceDef
                = tables.Interfaces.GetInterfaceDefForTypeDef(ownerType);
            if (interfaceDef != 0
                && supportedInterfaces.Contains(interfaceDef)
                && tables.InterfaceMethods.GetOwnerInterfaceDefForMethod(
                    methodRow) != interfaceDef)
            {
                throw Unsupported(
                    "A G1L1 interface source view has an unrepresented MethodDef.");
            }
        }
    }

    private static Dictionary<uint, int> CreateGenericInterfaceImportRows(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int moduleRow)
    {
        var interfaceImportDefinitions = new HashSet<uint>();
        var (firstTypeDef, endTypeDef)
            = tables.Modules.GetTypeDefRange(moduleRow);
        for (var typeDef = firstTypeDef; typeDef < endTypeDef; typeDef++)
        {
            var interfaceDef
                = tables.Interfaces.GetInterfaceDefForTypeDef(typeDef);
            if (interfaceDef == 0)
            {
                continue;
            }

            var typeDefId = 0x1300_0000u | checked((uint)typeDef);
            var interfaceDefId = 0x2000_0000u
                | checked((uint)interfaceDef);
            if (!tables.Exports.HasExportForTypeDef(typeDef)
                && !tables.Imports.IsExpectedDefinition(typeDefId)
                && !tables.Imports.IsExpectedDefinition(interfaceDefId))
            {
                continue;
            }

            if (tables.Imports.IsExpectedDefinition(typeDefId))
            {
                _ = interfaceImportDefinitions.Add(typeDefId);
                if (interfaceImportDefinitions.Count
                    > SemanticVerifier.MaximumAnalysisVisits)
                {
                    throw Unsupported(
                        "The G1L1 interface Import map exceeds the supported analysis limit.");
                }
            }
            if (tables.Imports.IsExpectedDefinition(interfaceDefId))
            {
                _ = interfaceImportDefinitions.Add(interfaceDefId);
                if (interfaceImportDefinitions.Count
                    > SemanticVerifier.MaximumAnalysisVisits)
                {
                    throw Unsupported(
                        "The G1L1 interface Import map exceeds the supported analysis limit.");
                }
            }

            var (firstMethod, methodCount)
                = tables.Interfaces.GetMethodRange(interfaceDef);
            for (var ordinal = 0; ordinal < methodCount; ordinal++)
            {
                var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                    firstMethod + ordinal);
                var methodId = 0x1600_0000u
                    | checked((uint)methodRow);
                if (tables.Imports.IsExpectedDefinition(methodId))
                {
                    _ = interfaceImportDefinitions.Add(methodId);
                    if (interfaceImportDefinitions.Count
                        > SemanticVerifier.MaximumAnalysisVisits)
                    {
                        throw Unsupported(
                            "The G1L1 interface Import map exceeds the supported analysis limit.");
                    }
                }
            }
        }

        var importRows = new Dictionary<uint, int>();
        for (var importRow = 1; importRow <= tables.Imports.Count;
            importRow++)
        {
            var moduleRefRow = tables.Imports.GetModuleRefForImportRow(
                importRow);
            if (tables.ModuleRefs.GetOwnerModule(moduleRefRow) != moduleRow)
            {
                continue;
            }

            var definitionId = tables.Imports
                .GetExpectedDefinitionForImportRow(importRow);
            if (interfaceImportDefinitions.Contains(definitionId))
            {
                if (!importRows.TryAdd(definitionId, importRow))
                {
                    throw Invalid(
                        "A G1L1 interface Import expectation is ambiguous.");
                }
            }
        }

        return importRows;
    }

    private static BoundInterfaceDefinition
        BindExpectedGenericInterfaceDefinition(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            GenericMethodReadContext context, int moduleRow, int interfaceDef,
            Dictionary<uint, int> importRows, HashSet<int> usedImportRows)
    {
        var typeDef = tables.Interfaces.GetOwnerTypeDef(interfaceDef);
        var typeDefId = 0x1300_0000u | checked((uint)typeDef);
        if (!importRows.TryGetValue(typeDefId, out var typeImportRow))
        {
            throw Invalid(
                "A G1L1 expected interface has no TypeDef Import row.");
        }
        _ = usedImportRows.Add(typeImportRow);

        var typeModuleRef = tables.Imports.GetModuleRefForImportRow(
            typeImportRow);
        var selectedType = tables.Imports.GetBindingForImportRow(
            typeImportRow);
        if (tables.ModuleRefs.GetOwnerModule(typeModuleRef) != moduleRow
            || !context.Modules.TryGetValue(selectedType.ProducerIdentity,
                out var providerSelection))
        {
            throw Invalid(
                "A G1L1 expected interface has no exact selected TypeDef provider.");
        }

        var interfaceDefId = 0x2000_0000u
            | checked((uint)interfaceDef);
        if (importRows.TryGetValue(interfaceDefId,
                out var interfaceImportRow))
        {
            _ = usedImportRows.Add(interfaceImportRow);
            var interfaceModuleRef = tables.Imports
                .GetModuleRefForImportRow(interfaceImportRow);
            var selectedInterface = tables.Imports
                .GetBindingForImportRow(interfaceImportRow);
            if (tables.ModuleRefs.GetOwnerModule(interfaceModuleRef)
                    != moduleRow
                || !StringComparer.Ordinal.Equals(
                    selectedInterface.ProducerIdentity,
                    selectedType.ProducerIdentity)
                || !StringComparer.Ordinal.Equals(
                    selectedInterface.ExportKey, selectedType.ExportKey)
                || selectedInterface.TargetRequirement
                    != selectedType.TargetRequirement
                || selectedInterface.RuntimePolicy
                    != selectedType.RuntimePolicy)
            {
                throw Invalid(
                    "The G1L1 InterfaceDef Import does not retain the selected TypeDef provider identity.");
            }
        }

        var providerModuleRow = providerSelection.ModuleRow;
        var providerInterfaceKey = (providerSelection.ArtifactIndex,
            providerModuleRow, selectedType.ExportKey);
        if (!context.ExportedInterfaceDefinitionsByKey.TryGetValue(
                providerInterfaceKey, out var providerInterface))
        {
            var provider = TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                    new TrRmArtifactIndex(
                        context.Artifacts[providerSelection.ArtifactIndex],
                        context.Schema));
            var (providerTargetRequirement, providerRuntimePolicy)
                = provider.Modules.GetRequirements(providerModuleRow);
            if (providerTargetRequirement != selectedType.TargetRequirement
                || providerRuntimePolicy != selectedType.RuntimePolicy
                || !provider.Exports.TryGetDefinitionForKey(providerModuleRow,
                    selectedType.ExportKey, out var providerTypeDefId)
                || (providerTypeDefId >> 24) != 0x13
                || provider.Interfaces.GetInterfaceDefForTypeDef(
                    checked((int)(providerTypeDefId & 0x00FF_FFFF))) == 0)
            {
                throw Invalid(
                    "The selected G1L1 TypeDef provider does not match the retained Import.");
            }

            _ = CreateExportedGenericInterfaceDefinitions(provider, context,
                providerSelection.ArtifactIndex, providerModuleRow);
            if (!context.ExportedInterfaceDefinitionsByKey.TryGetValue(
                    providerInterfaceKey, out providerInterface))
            {
                throw Invalid(
                    "The selected G1L1 TypeDef provider has no exported interface declaration.");
            }
        }

        var providerImport = providerInterface.ModuleImport;
        if (providerImport is null
            || !StringComparer.Ordinal.Equals(providerImport.ProducerIdentity,
                selectedType.ProducerIdentity)
            || !StringComparer.Ordinal.Equals(providerImport.ExportKey,
                selectedType.ExportKey)
            || providerImport.TargetRequirement
                != selectedType.TargetRequirement
            || providerImport.RuntimePolicy != selectedType.RuntimePolicy)
        {
            throw Invalid(
                "The selected G1L1 interface provider identity changed.");
        }

        var providerMembersByKey
            = new Dictionary<string, BoundInterfaceMemberDefinition>(
                providerInterface.Members.Length, StringComparer.Ordinal);
        foreach (var member in providerInterface.Members)
        {
            if (!providerMembersByKey.TryAdd(member.DeclarationKey, member))
            {
                throw Invalid(
                    "The selected G1L1 provider interface has duplicate requirement keys.");
            }
        }

        var (firstMethod, methodCount)
            = tables.Interfaces.GetMethodRange(interfaceDef);
        var methodExportKeys
            = ImmutableArray.CreateBuilder<string>(methodCount);
        var methodKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var methodOrdinal = 0; methodOrdinal < methodCount;
            methodOrdinal++)
        {
            var interfaceMethodRow = firstMethod + methodOrdinal;
            var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                interfaceMethodRow);
            var methodId = 0x1600_0000u | checked((uint)methodRow);
            if (!importRows.TryGetValue(methodId, out var methodImportRow))
            {
                throw Invalid(
                    "A G1L1 expected InterfaceMethod has no MethodDef Import row.");
            }
            _ = usedImportRows.Add(methodImportRow);

            var methodModuleRef = tables.Imports.GetModuleRefForImportRow(
                methodImportRow);
            var selectedMethod = tables.Imports.GetBindingForImportRow(
                methodImportRow);
            if (tables.ModuleRefs.GetOwnerModule(methodModuleRef) != moduleRow
                || !StringComparer.Ordinal.Equals(
                    selectedMethod.ProducerIdentity,
                    selectedType.ProducerIdentity)
                || selectedMethod.TargetRequirement
                    != selectedType.TargetRequirement
                || selectedMethod.RuntimePolicy != selectedType.RuntimePolicy
                || !methodKeys.Add(selectedMethod.ExportKey)
                || !providerMembersByKey.ContainsKey(
                    selectedMethod.ExportKey))
            {
                throw Invalid(
                    "A G1L1 requirement Import selects a different or ambiguous provider.");
            }

            methodExportKeys.Add(selectedMethod.ExportKey);
        }

        var expected = ReadG1InterfaceDefinition(tables, interfaceDef,
            selectedType.ProducerIdentity, selectedType.ExportKey,
            selectedType.TargetRequirement, selectedType.RuntimePolicy,
            methodExportKeys.MoveToImmutable());
        ValidateG1InterfaceProviderContract(expected, providerInterface,
            providerMembersByKey);
        return expected;
    }

    private static BoundInterfaceDefinition ReadG1InterfaceDefinition(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int interfaceDef, string moduleIdentity, string typeExportKey,
        uint targetRequirement, uint runtimePolicy,
        ImmutableArray<string> methodExportKeys)
    {
        var typeDef = tables.Interfaces.GetOwnerTypeDef(interfaceDef);
        var typeFlags = tables.TypeDefs.GetFlags(typeDef);
        var (firstField, endField) = tables.TypeDefs.GetFieldRange(typeDef);
        var (_, typeContractCount)
            = tables.TypeDefs.GetContractRange(typeDef);
        var (_, baseCount) = tables.Interfaces.GetBaseRange(interfaceDef);
        var (_, associatedTypeCount)
            = tables.Interfaces.GetAssociatedTypeRange(interfaceDef);
        var (_, associatedConstCount)
            = tables.Interfaces.GetAssociatedConstRange(interfaceDef);
        var (_, interfaceConstCount)
            = tables.Interfaces.GetInterfaceConstRange(interfaceDef);
        if ((typeFlags & 3) != 2)
        {
            throw Invalid(
                "A G1L1 exported interface TypeDef is not public.");
        }
        if (tables.TypeDefs.GetKind(typeDef) != 3
            || firstField != endField || typeContractCount != 0
            || baseCount != 0 || associatedTypeCount != 0
            || associatedConstCount != 0 || interfaceConstCount != 0)
        {
            throw Unsupported(
                "A G1L1 interface declaration has unsupported fields, bases, associated members, or type contracts.");
        }

        var (firstLayout, layoutCount)
            = tables.TypeDefs.GetLayoutRange(typeDef);
        if (layoutCount != 1
            || tables.Layouts.GetTargetProfileRef(firstLayout) != 0
            || tables.Layouts.GetLayoutKind(firstLayout) != 0
            || tables.Layouts.GetFlags(firstLayout) != 0
            || tables.Layouts.GetAlignment(firstLayout) != 0
            || tables.Layouts.GetPack(firstLayout) != 0
            || (tables.Layouts.GetLayoutFieldRange(firstLayout)
                is not (0, 0)))
        {
            throw Unsupported(
                "A G1L1 interface declaration requires one empty default Layout.");
        }

        var (firstFormal, formalCount)
            = tables.TypeDefs.GetGenericRange(typeDef);
        var selfFormal = tables.Interfaces.GetSelfGenericParam(interfaceDef);
        var formals = ImmutableArray.CreateBuilder<BoundInterfaceFormal>(
            formalCount);
        var constraints
            = ImmutableArray.CreateBuilder<BoundInterfaceConstraint>();
        for (var ordinal = 0; ordinal < formalCount; ordinal++)
        {
            var formalRow = firstFormal + ordinal;
            var (firstConstraint, constraintCount)
                = tables.GenericParams.GetConstraintRange(formalRow);
            if (tables.GenericParams.GetFormalKind(formalRow) != 1
                || tables.GenericParams.GetOrdinal(formalRow)
                    != (uint)ordinal
                || tables.GenericParams.GetOwnershipDomain(formalRow) != 1
                || tables.GenericParams.GetNullabilityDomain(formalRow) != 1
                || tables.GenericParams.GetDefaultArgumentRef(formalRow)
                    != 0)
            {
                throw Unsupported(
                    "A G1L1 InterfaceDef has an unsupported owner formal contract.");
            }

            var formalName = tables.Strings.DecodeName(
                tables.GenericParams.GetNameStringRow(formalRow));
            var requiresWritable = false;
            for (var constraintOffset = 0;
                constraintOffset < constraintCount; constraintOffset++)
            {
                var constraintRow = firstConstraint + constraintOffset;
                if (tables.Constraints.GetKind(constraintRow) != 2
                    || tables.Constraints.GetPayload(constraintRow) != 2)
                {
                    throw Unsupported(
                        "A G1L1 InterfaceDef has an unsupported generic constraint.");
                }

                requiresWritable = true;
                constraints.Add(new BoundInterfaceConstraint(ordinal,
                    BoundInterfaceConstraintKind.Write, null, null));
            }
            formals.Add(new BoundInterfaceFormal(ordinal, formalName,
                BoundInterfaceFormalKind.Type, selfFormal == formalRow,
                [], [], null, null, null)
            {
                RequiresWritable = requiresWritable,
            });
        }

        var (firstMethod, methodCount)
            = tables.Interfaces.GetMethodRange(interfaceDef);
        if (methodExportKeys.Length != methodCount)
        {
            throw Invalid(
                "A G1L1 InterfaceDef does not have one selected key per direct requirement.");
        }

        var members = ImmutableArray.CreateBuilder<
            BoundInterfaceMemberDefinition>(methodCount);
        for (var methodOrdinal = 0; methodOrdinal < methodCount;
            methodOrdinal++)
        {
            var interfaceMethodRow = firstMethod + methodOrdinal;
            if (tables.InterfaceMethods.GetOwnerInterfaceDef(
                    interfaceMethodRow) != interfaceDef
                || tables.InterfaceMethods.GetOrdinal(interfaceMethodRow)
                    != methodOrdinal)
            {
                throw Invalid(
                    "A G1L1 InterfaceMethod range is not declaration-ordered.");
            }

            var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                interfaceMethodRow);
            var (_, methodContractCount)
                = tables.Methods.GetContractRange(methodRow);
            var methodFlags = tables.Methods.GetFlags(methodRow);
            if ((methodFlags & 3) != 2
                || tables.Methods.GetBodyRow(methodRow) != 0
                || methodContractCount != 0)
            {
                throw Unsupported(
                    "A G1L1 InterfaceMethod has an unsupported visibility, body, or contract.");
            }

            var signatureRow = tables.Methods.GetSignature(methodRow);
            if (tables.Signatures.GetGenericCount(signatureRow) != 0)
            {
                throw Unsupported(
                    "G1L1 interface requirements do not support method formals.");
            }

            ValidateG1InterfaceReceiver(tables, typeDef, firstFormal,
                formalCount, signatureRow);
            var (firstParameter, parameterCount)
                = tables.Signatures.GetParamRange(signatureRow);
            var parameters = ImmutableArray.CreateBuilder<
                BoundInterfaceParameter>(parameterCount);
            for (var parameterOrdinal = 0;
                parameterOrdinal < parameterCount; parameterOrdinal++)
            {
                var parameterRow = firstParameter + parameterOrdinal;
                if (tables.Parameters.GetMode(parameterRow) != 0
                    || tables.Parameters.GetFlags(parameterRow) != 0
                    || !tables.MethodParameterLabels.TryGetNameStringRow(
                        methodRow, checked((uint)parameterOrdinal),
                        out var labelStringRow))
                {
                    throw Invalid(
                        "A G1L1 InterfaceMethod has an invalid value parameter view.");
                }

                var parameterType = DecodeG1InterfaceTypeSpec(tables,
                    typeDef, formalCount,
                    tables.Parameters.GetTypeSpec(parameterRow),
                    isReturn: false);
                parameters.Add(new BoundInterfaceParameter(
                    tables.Strings.DecodeName(labelStringRow), parameterType,
                    BoundInterfaceParameterMode.Value, [], []));
            }

            var resultType = DecodeG1InterfaceTypeSpec(tables, typeDef,
                formalCount,
                tables.Signatures.GetResultTypeSpec(signatureRow),
                isReturn: true);
            members.Add(new BoundInterfaceMemberDefinition(methodOrdinal,
                tables.Methods.GetName(methodRow),
                methodExportKeys[methodOrdinal],
                BoundInterfaceMemberKind.Callable,
                BoundInterfaceAccessibility.Public, false, false, false,
                false, false, false, false, [], [], [], default, resultType,
                [], [], parameters.MoveToImmutable(), [], [], null, null,
                null) {
                RequiresMutableReceiver = (methodFlags & 4) != 0,
            });
        }

        var interfaceNamespace = tables.TypeDefs.GetNamespace(typeDef);
        var interfaceName = tables.TypeDefs.GetName(typeDef);
        var qualifiedName = interfaceNamespace.Length == 0
            ? interfaceName
            : string.Concat(interfaceNamespace, ".", interfaceName);
        return new BoundInterfaceDefinition(qualifiedName, true, false,
            formals.MoveToImmutable(), [], constraints.ToImmutable(),
            members.MoveToImmutable()) {
            Accessibility = BoundInterfaceAccessibility.Public,
            ModuleImport = new BoundModuleImport(moduleIdentity,
                typeExportKey, targetRequirement, runtimePolicy),
        };
    }

    private static void ValidateG1InterfaceProviderContract(
        BoundInterfaceDefinition expected, BoundInterfaceDefinition provider,
        Dictionary<string, BoundInterfaceMemberDefinition>
            providerMembersByKey)
    {
        if (expected.ModuleImport != provider.ModuleImport
            || expected.IsPublic != provider.IsPublic
            || expected.Accessibility != provider.Accessibility
            || expected.Formals.Length != provider.Formals.Length
            || expected.Members.Length != provider.Members.Length
            || !expected.Bases.IsEmpty || !provider.Bases.IsEmpty
            || !HasOnlyG1InterfaceWriteConstraints(expected)
            || !HasOnlyG1InterfaceWriteConstraints(provider)
            || expected.ForeignNativeInterface is not null
            || provider.ForeignNativeInterface is not null)
        {
            throw Invalid(
                "The selected G1L1 interface provider changes the retained owner contract.");
        }

        for (var ordinal = 0; ordinal < expected.Formals.Length; ordinal++)
        {
            var expectedFormal = expected.Formals[ordinal];
            var providerFormal = provider.Formals[ordinal];
            if (expectedFormal.Ordinal != providerFormal.Ordinal
                || expectedFormal.Kind != providerFormal.Kind
                || expectedFormal.IsSelf != providerFormal.IsSelf
                || !expectedFormal.Attributes.SequenceEqual(
                    providerFormal.Attributes, StringComparer.Ordinal)
                || !expectedFormal.AttributeTokens.SequenceEqual(
                    providerFormal.AttributeTokens, StringComparer.Ordinal)
                || !AreEquivalentG1InterfaceTypes(
                    expectedFormal.ConstantType,
                    providerFormal.ConstantType)
                || !AreEquivalentG1InterfaceTypes(
                    expectedFormal.TypeDefault,
                    providerFormal.TypeDefault)
                || (providerFormal.RequiresWritable
                    && !expectedFormal.RequiresWritable)
                || expectedFormal.ConstantDefault
                    != providerFormal.ConstantDefault)
            {
                throw Invalid(
                    "The selected G1L1 interface provider changes an owner formal.");
            }
        }

        foreach (var expectedMember in expected.Members)
        {
            if (!providerMembersByKey.TryGetValue(
                    expectedMember.DeclarationKey, out var providerMember)
                || expectedMember.Kind != providerMember.Kind
                || expectedMember.Accessibility
                    != providerMember.Accessibility
                || expectedMember.IsStatic != providerMember.IsStatic
                || (providerMember.RequiresMutableReceiver
                    && !expectedMember.RequiresMutableReceiver)
                || expectedMember.IsExternal != providerMember.IsExternal
                || expectedMember.IsPartial != providerMember.IsPartial
                || expectedMember.RequiresUnsafeCaller
                    != providerMember.RequiresUnsafeCaller
                || expectedMember.DeclaredRequiresUnsafeCaller
                    != providerMember.DeclaredRequiresUnsafeCaller
                || expectedMember.DoesNotReturn
                    != providerMember.DoesNotReturn
                || expectedMember.IsAsync != providerMember.IsAsync
                || expectedMember.HasBody != providerMember.HasBody
                || expectedMember.NativeCallableConvention
                    != providerMember.NativeCallableConvention
                || expectedMember.Operator != providerMember.Operator
                || !expectedMember.Attributes.SequenceEqual(
                    providerMember.Attributes, StringComparer.Ordinal)
                || !expectedMember.AttributeTokens.SequenceEqual(
                    providerMember.AttributeTokens, StringComparer.Ordinal)
                || !expectedMember.ContractTokens.SequenceEqual(
                    providerMember.ContractTokens, StringComparer.Ordinal)
                || !expectedMember.TypeParameters.IsEmpty
                || !providerMember.TypeParameters.IsEmpty
                || !expectedMember.Constraints.IsEmpty
                || !providerMember.Constraints.IsEmpty
                || !expectedMember.Dependencies.IsEmpty
                || !providerMember.Dependencies.IsEmpty
                || !expectedMember.Accessors.IsEmpty
                || !providerMember.Accessors.IsEmpty
                || expectedMember.AssociatedDefaultType is not null
                || providerMember.AssociatedDefaultType is not null
                || expectedMember.AssociatedConstantType is not null
                || providerMember.AssociatedConstantType is not null
                || expectedMember.AssociatedConstantDefault is not null
                || providerMember.AssociatedConstantDefault is not null
                || !AreEquivalentG1InterfaceTypes(expectedMember.ResultType,
                    providerMember.ResultType)
                || expectedMember.Parameters.Length
                    != providerMember.Parameters.Length)
            {
                throw Invalid(
                    "The selected G1L1 interface provider changes a retained requirement.");
            }

            for (var ordinal = 0;
                ordinal < expectedMember.Parameters.Length; ordinal++)
            {
                var expectedParameter = expectedMember.Parameters[ordinal];
                var providerParameter = providerMember.Parameters[ordinal];
                if (expectedParameter.Mode != providerParameter.Mode
                    || !expectedParameter.Attributes.SequenceEqual(
                        providerParameter.Attributes,
                        StringComparer.Ordinal)
                    || !expectedParameter.AttributeTokens.SequenceEqual(
                        providerParameter.AttributeTokens,
                        StringComparer.Ordinal)
                    || !AreEquivalentG1InterfaceTypes(
                        expectedParameter.Type, providerParameter.Type))
                {
                    throw Invalid(
                        "The selected G1L1 interface provider changes an ordered parameter contract.");
                }
            }
        }
    }

    private static bool HasOnlyG1InterfaceWriteConstraints(
        BoundInterfaceDefinition definition)
    {
        if (definition.Constraints.IsDefault)
        {
            return false;
        }

        var requiresWritable = new bool[definition.Formals.Length];
        foreach (var constraint in definition.Constraints)
        {
            if (constraint is null
                || constraint.Kind != BoundInterfaceConstraintKind.Write
                || constraint.Type is not null
                || constraint.AttributeName is not null
                || constraint.FormalScope
                    != BoundInterfaceTypeParameterScope.Interface
                || (uint)constraint.FormalOrdinal
                    >= (uint)definition.Formals.Length
                || definition.Formals[constraint.FormalOrdinal].Kind
                    != BoundInterfaceFormalKind.Type)
            {
                return false;
            }

            requiresWritable[constraint.FormalOrdinal] = true;
        }

        for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
        {
            if (definition.Formals[ordinal].RequiresWritable
                != requiresWritable[ordinal])
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreEquivalentG1InterfaceTypes(
        BoundInterfaceType? left, BoundInterfaceType? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Kind == right.Kind
            && left.PrimitiveType == right.PrimitiveType
            && left.NominalName == right.NominalName
            && left.TypeParameterOrdinal == right.TypeParameterOrdinal
            && left.TypeParameterScope == right.TypeParameterScope
            && left.IsWritable == right.IsWritable
            && left.IsOptional == right.IsOptional
            && left.IsGarbageCollected == right.IsGarbageCollected
            && left.IsInitialization == right.IsInitialization
            && left.TypeArguments.IsDefaultOrEmpty
            && right.TypeArguments.IsDefaultOrEmpty
            && left.ModuleImport is null && right.ModuleImport is null
            && left.GenericNominalTypeIdentity is null
            && right.GenericNominalTypeIdentity is null
            && left.SourceGenericTypeDefinitionIndex == -1
            && right.SourceGenericTypeDefinitionIndex == -1
            && !left.IsGenericNominalConstantDomain
            && !right.IsGenericNominalConstantDomain
            && (left.Kind is BoundInterfaceTypeKind.Primitive
                or BoundInterfaceTypeKind.TypeParameter);
    }

    private static void ValidateG1InterfaceReceiver(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int typeDefRow, int firstFormal, int formalCount, int signatureRow)
    {
        var receiverTypeSpec = tables.Signatures
            .GetReceiverTypeSpec(signatureRow);
        var receiver = tables.TypeSpecs.GetTypeSpecRef(receiverTypeSpec);
        if (!receiver.IsRow || receiver.Qualifiers != 0)
        {
            throw Invalid(
                "A G1L1 InterfaceMethod has an invalid receiver TypeSpec.");
        }

        var receiverBytes = tables.TypeSpecs.GetRow(receiver.Row);
        if (formalCount == 0)
        {
            if (receiver.Form != 0x02 || receiverBytes.Length != 6
                || ReadGenericInterfaceTypeSpecWord(receiverBytes, 2)
                    != (uint)typeDefRow)
            {
                throw Invalid(
                    "A G1L1 InterfaceMethod receiver does not match its declaring TypeDef.");
            }

            return;
        }

        var expectedReceiverLength
            = checked(6L + ((long)formalCount * sizeof(uint)));
        if (receiver.Form != 0x05
            || receiverBytes.Length != expectedReceiverLength)
        {
            throw Invalid(
                "A G1L1 InterfaceMethod receiver is not the complete owner-formal instance.");
        }

        var genericTypeSpec = ReadGenericInterfaceTypeSpecWord(
            receiverBytes, 2);
        var genericType = tables.TypeSpecs.GetTypeSpecRef(genericTypeSpec);
        if (!genericType.IsRow || genericType.Form != 0x02
            || genericType.Qualifiers != 0
            || ReadGenericInterfaceTypeSpecWord(
                tables.TypeSpecs.GetRow(genericType.Row), 2)
                != (uint)typeDefRow)
        {
            throw Invalid(
                "A G1L1 InterfaceMethod receiver has the wrong generic owner.");
        }

        for (var ordinal = 0; ordinal < formalCount; ordinal++)
        {
            var argument = tables.TypeSpecs.GetTypeSpecRef(
                ReadGenericInterfaceTypeSpecWord(receiverBytes,
                    6 + (ordinal * sizeof(uint))));
            if (!argument.IsRow || argument.Form != 0x04
                || argument.Qualifiers != 0)
            {
                throw Invalid(
                    "A G1L1 InterfaceMethod receiver has an unsupported owner argument.");
            }

            var formalRow = ReadGenericInterfaceTypeSpecWord(
                tables.TypeSpecs.GetRow(argument.Row), 2);
            var expectedFormalRow = checked((uint)(firstFormal + ordinal));
            if (formalRow != expectedFormalRow
                || tables.GenericParams.GetOwnerDefId(
                    checked((int)formalRow))
                    != (0x1300_0000u | checked((uint)typeDefRow))
                || tables.GenericParams.GetFormalKind(
                    checked((int)formalRow)) != 1
                || tables.GenericParams.GetOrdinal(
                    checked((int)formalRow)) != (uint)ordinal)
            {
                throw Invalid(
                    "A G1L1 InterfaceMethod receiver does not forward owner formals in order.");
            }
        }
    }

    private static BoundInterfaceType DecodeG1InterfaceTypeSpec(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int ownerTypeDef, int formalCount, uint typeSpecRef, bool isReturn)
    {
        var type = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
        if (!type.IsRow || type.Qualifiers != 0)
        {
            throw Unsupported(
                "A G1L1 interface signature requires unqualified TypeSpec rows.");
        }

        var row = tables.TypeSpecs.GetRow(type.Row);
        if (type.Form == 0x01 && row.Length == 3)
        {
            var typeIndex = Array.IndexOf(s_codes, type.TypeCode);
            if (typeIndex < 0)
            {
                throw Unsupported(
                    "A G1L1 interface signature uses a non-scalar type.");
            }

            var primitive = s_types[typeIndex];
            if (primitive == PrimitiveType.Void)
            {
                if (!isReturn)
                {
                    throw Unsupported(
                        "A G1L1 interface parameter cannot have unit type.");
                }

                return new BoundInterfaceType(
                    BoundInterfaceTypeKind.Primitive, PrimitiveType.Void,
                    null, -1, [], false, false, false, false);
            }

            return new BoundInterfaceType(BoundInterfaceTypeKind.Primitive,
                primitive, null, -1, [], false, false, false, false);
        }

        if (type.Form == 0x04 && row.Length == 6)
        {
            var formalRow = checked((int)
                ReadGenericInterfaceTypeSpecWord(row, 2));
            if (tables.GenericParams.GetOwnerDefId(formalRow)
                    != (0x1300_0000u | checked((uint)ownerTypeDef))
                || tables.GenericParams.GetFormalKind(formalRow) != 1)
            {
                throw Invalid(
                    "A G1L1 interface signature references a foreign owner formal.");
            }

            var formalOrdinal = checked((int)
                tables.GenericParams.GetOrdinal(formalRow));
            if ((uint)formalOrdinal >= (uint)formalCount)
            {
                throw Invalid(
                    "A G1L1 interface signature references an unknown owner formal.");
            }

            return new BoundInterfaceType(
                BoundInterfaceTypeKind.TypeParameter, PrimitiveType.Void,
                null, formalOrdinal, [], false, false, false, false) {
                TypeParameterScope
                    = BoundInterfaceTypeParameterScope.Interface,
            };
        }

        throw Unsupported(
            "A G1L1 interface signature is outside the scalar/owner-formal subset.");
    }

    private static uint ReadGenericInterfaceTypeSpecWord(
        ReadOnlySpan<byte> row, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(
            row.Slice(offset, sizeof(uint)));

    private static void ValidateGenericMethodCallBodies(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
    {
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            if (bodyRow != 0 && tables.Bodies.GetRow(bodyRow)[0] == 0x01)
            {
                tables.ValidateRegisterBodyMapsForMethod(methodRow);
            }
        }
    }

    private static ImmutableArray<BoundDependencyContract>
        DecodeG1GenericMethodDependencyContracts(
            ImmutableArray<TrRmDependencyContract> dependencies,
            int parameterCount)
    {
        if (dependencies.IsDefault)
        {
            throw Invalid(
                "A G1 Generic MethodDef has an invalid Dependency clause view.");
        }
        if (dependencies.IsEmpty)
        {
            return [];
        }

        var emptySources = dependencies.Where(
            static dependency => dependency.SourceKind == 0).ToArray();
        if (emptySources.Length != 0)
        {
            if (dependencies.Length != 1
                || emptySources[0] is not {
                    DestinationKind: 1,
                    SourceKind: 0,
                    Relationship: 0,
                    Phase: 1,
                    DestinationOrdinal: 0,
                    SourceOrdinal: 0,
                })
            {
                throw Unsupported(
                    "G1 generic MethodDefs support only an explicit empty ordinary-result DependsOn set.");
            }

            return [
                new BoundDependencyContract(
                    BoundDependencyRelation.DependsOn,
                    BoundDependencyDestination.Result, null, []),
            ];
        }

        var sources = ImmutableArray.CreateBuilder<BoundDependencySource>(
            dependencies.Length);
        var forwardedSources
            = ImmutableArray.CreateBuilder<BoundDependencySource>();
        var storageSources
            = ImmutableArray.CreateBuilder<BoundDependencySource>();
        foreach (var dependency in dependencies)
        {
            if (dependency.DestinationKind != 1
                || dependency.DestinationOrdinal != 0
                || dependency.Phase != 1
                || dependency.SourceKind != 1
                || dependency.SourceOrdinal >= (uint)parameterCount
                || dependency.Relationship is not (1 or 2 or 3))
            {
                throw Unsupported(
                    "G1 generic MethodDefs support only ordinary-result formal dependency sources.");
            }

            var source = new BoundDependencySource(
                BoundDependencySourceKind.Formal,
                checked((int)dependency.SourceOrdinal));
            sources.Add(source);
            if (dependency.Relationship == 2)
            {
                forwardedSources.Add(source);
            }
            else if (dependency.Relationship == 1)
            {
                storageSources.Add(source);
            }
        }

        var sourceSet = sources.ToImmutable()
            .OrderBy(static source => source.FormalOrdinal)
            .ToImmutableArray();
        var forwardedSourceSet = forwardedSources.ToImmutable()
            .OrderBy(static source => source.FormalOrdinal)
            .ToImmutableArray();
        var storageSourceSet = storageSources.ToImmutable()
            .OrderBy(static source => source.FormalOrdinal)
            .ToImmutableArray();
        var contracts = ImmutableArray.CreateBuilder<
            BoundDependencyContract>(1
                + (forwardedSourceSet.IsEmpty ? 0 : 1)
                + (storageSourceSet.IsEmpty ? 0 : 1));
        contracts.Add(new BoundDependencyContract(
            BoundDependencyRelation.DependsOn,
            BoundDependencyDestination.Result, null, sourceSet));
        if (!storageSourceSet.IsEmpty)
        {
            contracts.Add(new BoundDependencyContract(
                BoundDependencyRelation.BorrowSourceStorage,
                BoundDependencyDestination.Result, null,
                storageSourceSet));
        }
        if (!forwardedSourceSet.IsEmpty)
        {
            contracts.Add(new BoundDependencyContract(
                BoundDependencyRelation.ForwardsBacking,
                BoundDependencyDestination.Result, null,
                forwardedSourceSet));
        }
        return contracts.MoveToImmutable();
    }

    private static BoundGenericMethodImport CreateGenericMethodImport(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow,
        uint definitionId, string exportKey,
        bool isMethodDefTarget)
    {
        var methodRow = checked((int)(definitionId & 0x00FF_FFFF));
        var methodKey = (artifactIndex, methodRow);
        if (isMethodDefTarget
            && context.MethodTargets.TryGetValue(methodKey,
                out var cachedImport))
        {
            return cachedImport;
        }
        if (!isMethodDefTarget
            && context.ExportedMethods.TryGetValue(
                (artifactIndex, methodRow, exportKey), out cachedImport))
        {
            return cachedImport;
        }

        if (tables.Imports.IsExpectedDefinition(definitionId))
        {
            if (!isMethodDefTarget)
            {
                throw Invalid(
                    "A G1 Export cannot expose an imported expectation.");
            }
            var selectedImportRow = 0;
            for (var importRow = 1; importRow <= tables.Imports.Count;
                importRow++)
            {
                if (tables.Imports.GetExpectedDefinitionForImportRow(importRow)
                    != definitionId)
                {
                    continue;
                }
                if (selectedImportRow != 0)
                {
                    throw Unsupported(
                        "A G1 expected MethodDef with multiple Import rows requires an exact MethodMap Import token.");
                }

                selectedImportRow = importRow;
            }

            return CreateExpectedGenericMethodImport(tables, context,
                artifactIndex, moduleRow, selectedImportRow);
        }

        var stub = CreateGenericMethodImportCore(tables,
            context, artifactIndex, moduleRow, definitionId, exportKey,
            isMethodDefTarget, decodeBody: false);
        if (isMethodDefTarget)
        {
            context.MethodTargets.Add(methodKey, stub);
        }
        else
        {
            context.ExportedMethods.Add(
                (artifactIndex, methodRow, exportKey), stub);
        }
        context.MethodViews.Add((methodKey, stub));
        if (context.DecodedMethods.TryGetValue(methodKey,
                out var proofFunction))
        {
            if (!context.DecodedMethodSupportFunctions.TryGetValue(
                    methodKey, out var supportFunctions))
            {
                throw Invalid(
                    "A G1 decoded MethodDef has no proof support functions.");
            }

            stub.CompleteProofFunction(proofFunction, supportFunctions);
            return stub;
        }
        if (context.MethodsBeingDecoded.Count
                + context.ImportsBeingDecoded.Count
            >= SemanticVerifier.MaximumDepth)
        {
            throw Unsupported(
                "The G1 generic-method provider closure exceeds the supported nesting limit.");
        }
        if (!context.MethodsBeingDecoded.Add(methodKey))
        {
            return stub;
        }

        try
        {
            var imported = CreateGenericMethodImportCore(tables,
                context, artifactIndex, moduleRow, definitionId, exportKey,
                isMethodDefTarget, decodeBody: true);
            context.DecodedMethods.Add(methodKey, imported.ProofFunction);
            context.DecodedMethodSupportFunctions.Add(methodKey,
                imported.ProofSupportFunctions);
            stub.CompleteProofFunction(imported.ProofFunction,
                imported.ProofSupportFunctions);
            return stub;
        }
        finally
        {
            _ = context.MethodsBeingDecoded.Remove(methodKey);
        }
    }

    private static BoundGenericMethodImport CreateGenericMethodImportCore(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow,
        uint definitionId, string exportKey,
        bool isMethodDefTarget, bool decodeBody)
    {
        var methodRow = checked((int)(definitionId & 0x00FF_FFFF));
        var formalTypeOwnerOrdinal
            = context.GetFormalTypeOwnerOrdinal(artifactIndex, methodRow);
        if (tables.Methods.GetOwnerModule(methodRow) != moduleRow
            || tables.Methods.GetOwnerType(methodRow) != 0
            || (isMethodDefTarget
                ? tables.Methods.GetFlags(methodRow) is not (0 or 2)
                : tables.Methods.GetFlags(methodRow) != 2))
        {
            throw Unsupported(
                isMethodDefTarget
                    ? "G1 GenericCall targets currently require module-level MethodDefs."
                    : "G1 generic MethodDef imports currently require a public module-level declaration.");
        }

        var signatureRow = tables.Methods.GetSignature(methodRow);
        var (formalFirst, formalCount)
            = tables.Signatures.GetGenericRange(signatureRow);
        if (formalCount is < 1 or > 8)
        {
            throw Invalid("The selected G1 MethodDef has no supported type-formal range.");
        }

        var placeholderTypes = ImmutableArray.CreateBuilder<PrimitiveType>(
            formalCount);
        var formalRecords = new TrRmGenericMethodFormalImport[formalCount];
        var interfaceConstraintsLoaded = false;
        var interfaceConstraintVisits
            = SemanticVerifier.MaximumAnalysisVisits;
        for (var ordinal = 0; ordinal < formalCount; ordinal++)
        {
            var genericParamRow = checked(formalFirst + ordinal);
            if (tables.GenericParams.GetFormalKind(genericParamRow) != 1
                || tables.GenericParams.GetOwnerDefId(genericParamRow)
                    != definitionId
                || tables.GenericParams.GetOrdinal(genericParamRow)
                    != (uint)ordinal)
            {
                throw Unsupported(
                    "G1 generic MethodDef import currently supports type formals only.");
            }

            var placeholder = (PrimitiveType)(100 + ordinal);
            var name = tables.Strings.DecodeName(
                tables.GenericParams.GetNameStringRow(genericParamRow));
            var ownershipDomain = tables.GenericParams
                .GetOwnershipDomain(genericParamRow);
            var nullabilityDomain = tables.GenericParams
                .GetNullabilityDomain(genericParamRow);
            var requiresWritable = false;
            var requiresBlittable = false;
            var interfaceConstraints
                = ImmutableArray.CreateBuilder<BoundInterfaceType>();
            var (constraintFirst, constraintCount)
                = tables.GenericParams.GetConstraintRange(genericParamRow);
            for (var constraintOffset = 0;
                constraintOffset < constraintCount; constraintOffset++)
            {
                var constraintRow = constraintFirst + constraintOffset;
                if (tables.Constraints.GetOwnerGenericParam(constraintRow)
                    != genericParamRow)
                {
                    throw Invalid(
                        "A G1 GenericParam Constraint has the wrong owner.");
                }

                var constraintKind = tables.Constraints.GetKind(
                    constraintRow);
                var constraintPayload = tables.Constraints.GetPayload(
                    constraintRow);
                switch (constraintKind)
                {
                    case 1:
                        if (!context.Schema.UsesMethodParameterLabels())
                        {
                            throw Unsupported(
                                "G1 interface constraints require the exact G1L1 schema.");
                        }
                        if (!interfaceConstraintsLoaded)
                        {
                            _ = CreateGenericInterfaceDefinitions(tables,
                                context, artifactIndex, moduleRow);
                            interfaceConstraintsLoaded = true;
                        }

                        interfaceConstraints.Add(
                            DecodeG1MethodInterfaceConstraint(tables,
                                context, artifactIndex, moduleRow, methodRow,
                                formalCount, constraintPayload,
                                ref interfaceConstraintVisits));
                        break;

                    case 2 when constraintPayload == 2 && !requiresWritable:
                        requiresWritable = true;
                        break;

                    case 2 when constraintPayload == 3 && !requiresBlittable:
                        requiresBlittable = true;
                        break;

                    case 2:
                        throw Invalid(
                            "The selected generic MethodDef has duplicate or unsupported property constraints.");

                    case 3:
                        throw Unsupported(
                            "G1 generic MethodDef imports do not support associated-relation constraints.");

                    default:
                        throw Invalid(
                            "The selected generic MethodDef has an unsupported Constraint kind.");
                }
            }

            BoundGenericMethodTypeDefault? typeDefault = null;
            var defaultReference = tables.GenericParams
                .GetDefaultArgumentRef(genericParamRow);
            if (defaultReference != 0)
            {
                var defaultType = ResolveGenericMethodTypeSpec(
                    context, artifactIndex, tables, moduleRow,
                    tables.TypeSpecs,
                    tables.GenericParams,
                    formalRecords, methodRow, formalTypeOwnerOrdinal,
                    defaultReference, placeholderTypes.ToImmutable(),
                    allowFormalForwarding: true, ordinal,
                    out var defaultFormalOrdinal);
                if (!TryCreateG1GenericMethodTypeTerm(defaultType,
                        placeholderTypes.ToImmutable(), formalRecords,
                        formalTypeOwnerOrdinal, context.SpanTypes,
                        context.CheckedReferenceTypeDefinitions,
                        context.GenericNominalTypeDefinitions.ToImmutable(),
                        context.GenericNominalTypeInstances, 0,
                        defaultFormalOrdinal, out var term)
                    || !BoundGenericMethodTypeTerms
                        .TryGetFormalDependencies(term,
                            out var defaultTypeDependencies))
                {
                    throw Unsupported(
                        "A G1 GenericParam type default has an unsupported type term.");
                }
                typeDefault = new BoundGenericMethodTypeDefault(term,
                    defaultTypeDependencies);
            }

            placeholderTypes.Add(placeholder);
            formalRecords[ordinal] = new TrRmGenericMethodFormalImport(
                placeholder, name, ownershipDomain, nullabilityDomain,
                requiresWritable, requiresBlittable,
                interfaceConstraints.ToImmutable(), typeDefault);
        }

        var formalTypes = placeholderTypes.MoveToImmutable();
        var formalSignatures = ImmutableArray.CreateBuilder<
            BoundGenericMethodFormalSignature>(formalCount);
        var formals = ImmutableArray.CreateBuilder<
            BoundGenericMethodFormal>(formalCount);
        var formalOrder = ImmutableArray.CreateBuilder<
            BoundGenericMethodFormalSlot>(formalCount);
        for (var ordinal = 0; ordinal < formalCount; ordinal++)
        {
            var formal = formalRecords[ordinal];
            var dependencies = formal.TypeDefault?.FormalDependencies ?? [];
            formalSignatures.Add(
                new BoundGenericMethodFormalSignature(ordinal,
                    formal.Name, formal.OwnershipDomain,
                    formal.NullabilityDomain, []) {
                    HasTypeDefault = formal.TypeDefault is not null,
                    RequiresWritable = formal.RequiresWritable,
                    RequiresBlittable = formal.RequiresBlittable,
                    InterfaceConstraints = formal.InterfaceConstraints,
                    DefaultTypeParameterDependencies = dependencies,
                });
            formals.Add(new BoundGenericMethodFormal(ordinal,
                formal.Name, formal.PlaceholderType,
                formal.OwnershipDomain, formal.NullabilityDomain, []) {
                TypeDefault = formal.TypeDefault,
                RequiresWritable = formal.RequiresWritable,
                RequiresBlittable = formal.RequiresBlittable,
                InterfaceConstraints = formal.InterfaceConstraints,
            });
            formalOrder.Add(new BoundGenericMethodFormalSlot(ordinal,
                BoundGenericMethodFormalKind.Type, ordinal, -1));
        }

        var (parameterFirst, parameterCount)
            = tables.Signatures.GetParamRange(signatureRow);
        if (parameterCount > 8
            || tables.Signatures.GetReceiverTypeSpec(signatureRow) != 0)
        {
            throw Unsupported(
                "G1 generic MethodDef imports currently require supported module-level value parameters.");
        }
        var variables = ImmutableArray.CreateBuilder<BoundVariable>(
            parameterCount);
        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            var paramRow = parameterFirst + ordinal;
            if (tables.Parameters.GetMode(paramRow) != 0
                || tables.Parameters.GetFlags(paramRow) != 0)
            {
                throw Unsupported(
                    "G1 generic MethodDef imports currently require ordinary value parameters.");
            }

            var parameterType = ResolveGenericMethodTypeSpec(
                context, artifactIndex, tables, moduleRow,
                tables.TypeSpecs,
                tables.GenericParams,
                formalRecords, methodRow, formalTypeOwnerOrdinal,
                tables.Parameters.GetTypeSpec(paramRow), formalTypes,
                allowFormalForwarding: false, ordinal: formalCount, out _);
            var retainsLabel = context.Schema.UsesMethodParameterLabels()
                && (tables.Exports.HasExportForMethod(methodRow)
                    || tables.Imports.IsExpectedDefinition(definitionId));
            var parameterName = "arg" + ordinal.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            if (retainsLabel)
            {
                if (!tables.MethodParameterLabels.TryGetNameStringRow(
                        methodRow, checked((uint)ordinal),
                        out var nameStringRow))
                {
                    throw Invalid(
                        "A validated G1L1 MethodDef has no parameter label.");
                }

                parameterName = tables.Strings.DecodeName(nameStringRow);
            }

            variables.Add(new BoundVariable(parameterName, parameterType,
                false, default));
        }

        var returnType = ResolveGenericMethodTypeSpec(context,
            artifactIndex, tables, moduleRow, tables.TypeSpecs,
            tables.GenericParams,
            formalRecords,
            methodRow, formalTypeOwnerOrdinal,
            tables.Signatures.GetResultTypeSpec(signatureRow),
            formalTypes, allowFormalForwarding: false,
            ordinal: formalCount, out _);
        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        if (bodyRow == 0 && (decodeBody
                || !tables.Imports.IsExpectedDefinition(definitionId)))
        {
            throw Unsupported(
                "G1 generic MethodDef imports currently require an executable open Body.");
        }
        var (contractFirst, contractCount)
            = tables.Methods.GetContractRange(methodRow);
        for (var index = 0; index < contractCount; index++)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(
                    tables.Contracts.GetRow(contractFirst + index)[4..])
                is not (1 or 4))
            {
                throw Unsupported(
                    "The G1 generic-method source reference currently supports Effects and ordinary-result Dependency contracts only.");
            }
        }
        var dependencyContracts
            = DecodeG1GenericMethodDependencyContracts(
                tables.Contracts.GetDependencyContracts(methodRow),
                parameterCount);

        var proofVariables = variables.ToImmutable().ToBuilder();
        var bodyBytes = bodyRow == 0 ? default : tables.Bodies.GetRow(bodyRow);
        var proofSupportFunctions = ImmutableArray<BoundFunction>.Empty;
        var functionIndexByMethodRow = decodeBody && bodyBytes[0] != 0x01
            ? new int[tables.Methods.Count + 1] : [];
        Array.Fill(functionIndexByMethodRow, -1);
        var statements = decodeBody
            ? bodyBytes[0] == 0x01
                ? DecodeGenericRegisterBody(tables, context,
                    artifactIndex, moduleRow, methodRow, formalTypes,
                    formalRecords, returnType, proofVariables, bodyRow,
                    out proofSupportFunctions)
                : DecodeBody(bodyBytes, parameterCount, proofVariables,
                    tables.TypeSpecs,
                    ImmutableArray.CreateBuilder<BoundFunction>(),
                    functionIndexByMethodRow)
            : [];
        var effects = tables.Contracts.GetEffectsMask(methodRow);
        if ((effects & ~6u) != 0)
        {
            throw Invalid(
                "The selected generic MethodDef has unassigned effect bits.");
        }
        var methodName = tables.Methods.GetName(methodRow);
        var isPublic = (tables.Methods.GetFlags(methodRow) & 3) == 2;
        var proofFunction = new BoundFunction(methodName, returnType,
            proofVariables.ToImmutable(), parameterCount,
            new BoundBlock(statements, default), default) {
            IsExternal = !decodeBody,
            IsStatic = true,
            RequiresUnsafeCaller = (effects & 4) != 0,
            DoesNotReturn = (effects & 2) != 0,
            DependencyContracts = dependencyContracts,
        };
        var methodSignature = new BoundGenericMethodSignature(0,
            methodName, isPublic, proofFunction.RequiresUnsafeCaller,
            formalSignatures.MoveToImmutable()) {
            IsStatic = true,
            FormalOrder = formalOrder.MoveToImmutable(),
        };
        var signature = new BoundGenericMethodSignature(0,
            isMethodDefTarget ? methodName : exportKey, isPublic,
            proofFunction.RequiresUnsafeCaller,
            methodSignature.Formals) {
            IsStatic = true,
            FormalOrder = methodSignature.FormalOrder,
        };
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(moduleRow);
        var origin = new BoundGenericMethodImportOrigin(
            tables.Modules.GetIdentity(moduleRow), exportKey,
            context.SchemaIdentity,
            targetRequirement,
            runtimePolicy, context.Digests[artifactIndex], definitionId,
            isMethodDefTarget);
        var import = new BoundGenericMethodImport(origin, signature,
            formals.MoveToImmutable(), proofFunction,
            formalTypeOwnerOrdinal, context.SpanTypes);
        import.CompleteProofFunction(proofFunction,
            proofSupportFunctions);
        return import;
    }

    private static ImmutableArray<BoundStatement> DecodeGenericRegisterBody(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow,
        int callerMethodRow, ImmutableArray<PrimitiveType> callerFormals,
        TrRmGenericMethodFormalImport[] callerFormalRecords,
        PrimitiveType callerReturnType,
        ImmutableArray<BoundVariable>.Builder callerVariables,
        int bodyRow, out ImmutableArray<BoundFunction> proofSupportFunctions)
    {
        proofSupportFunctions = [];
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var code = body.GetBlockCode(0);
        if (body.BlockCount == 1 && body.RecordCount == 1
            && code.Length == 2 && code[0] == 0xB8)
        {
            var homeIndex = code[1];
            if ((uint)homeIndex >= (uint)body.HomeCount)
            {
                throw Invalid(
                    "The open move-return Body references an invalid Home.");
            }

            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            var parameterOrdinal = BinaryPrimitives
                .ReadUInt16LittleEndian(home.Slice(4, 2));
            if (home[0] != 2
                || parameterOrdinal >= callerVariables.Count
                || callerVariables[parameterOrdinal].Type
                    != callerReturnType)
            {
                throw Invalid(
                    "The open move-return Body does not select its exact result parameter.");
            }

            var signature = tables.Methods.GetSignature(callerMethodRow);
            var (firstParameter, callerParameterCount)
                = tables.Signatures.GetParamRange(signature);
            if (parameterOrdinal >= callerParameterCount)
            {
                throw Invalid(
                    "The open ReturnHome does not select a MethodSig parameter.");
            }

            var parameterTypeSpec = tables.Parameters.GetTypeSpec(
                firstParameter + parameterOrdinal);
            var parameterType = tables.TypeSpecs
                .GetTypeSpecRef(parameterTypeSpec);
            var sourceUse = (byte)1;
            if (parameterType.Form == 0x04)
            {
                var genericParamRow = checked((int)BinaryPrimitives
                    .ReadUInt32LittleEndian(tables.TypeSpecs
                        .GetRow(parameterType.Row).Slice(2, sizeof(uint))));
                sourceUse = tables.GenericParams
                    .GetOwnershipDomain(genericParamRow) switch {
                        1 => (byte)1,
                        2 => (byte)2,
                        _ => throw new NotSupportedException(
                            "The G1 consumer currently supports Copy or unique ReturnHome formals only."),
                    };
            }

            BoundExpression returned = sourceUse == 2
                ? new BoundMove(parameterOrdinal, callerReturnType, default)
                : new BoundRead(parameterOrdinal, callerReturnType, default);
            return [new BoundReturn(returned, default)];
        }

        if (body.BlockCount != 1 || body.RecordCount != 3)
        {
            throw Unsupported(
                "The G1 consumer currently supports one-block direct GenericCall return Bodies.");
        }

        var callSiteInstructionLength
            = TrRmRegisterBodyReader.InstructionLength(code[0], body.Schema);
        var callSiteRecordIndex = code[0] switch
        {
            0x90 when code.Length >= 2 => code[1],
            0xE0 when code.Length >= 4 && code[1] == 0
                => BinaryPrimitives.ReadUInt16LittleEndian(code.Slice(2, 2)),
            _ => throw Unsupported(
                "The G1 consumer requires a short or long GenericCall instruction."),
        };
        var hasReturnHome = code.Length == callSiteInstructionLength + 2
            && code[callSiteInstructionLength] == 0xB8;
        if (code.Length != callSiteInstructionLength
            + (hasReturnHome ? 2 : 0))
        {
            throw Unsupported(
                "The G1 consumer supports only a terminal GenericCall or one followed by ReturnHome.");
        }

        var callSite = body.GetRecord(callSiteRecordIndex);
        if (callSite[0] != 0x02 || callSite[4] != 4
            || callSite[5] is not (0 or 2))
        {
            throw Unsupported(
                "The G1 consumer supports direct GenericCall targets with None or Home results.");
        }

        var callSitePayload = callSite[4..];
        var targetRecordIndex = BinaryPrimitives
            .ReadUInt16LittleEndian(callSitePayload.Slice(4, 2));
        var targetRecord = body.GetRecord(targetRecordIndex);
        if (targetRecord[0] != 0x10)
        {
            throw Invalid(
                "The GenericCall target record has an invalid kind.");
        }

        if (targetRecord[4] == 2)
        {
            var interfaceCall = DecodeG1InterfaceRequirementCall(tables,
                context,
                artifactIndex, moduleRow, callerMethodRow, callerFormals,
                callerFormalRecords, callerReturnType, callerVariables,
                body, code, callSiteInstructionLength, hasReturnHome,
                callSite, callSitePayload, targetRecord,
                out var supportFunction);
            proofSupportFunctions = [supportFunction];
            return [new BoundReturn(interfaceCall, default)];
        }

        if (targetRecord[4] != 1
            || targetRecord[5] != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(
                targetRecord.Slice(8, 4)) != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(
                targetRecord.Slice(12, 4)) != 0)
        {
            throw Unsupported(
                "The G1 consumer currently supports direct module-level GenericCall targets without dispatch evidence.");
        }

        var targetPayload = targetRecord[4..];
        var methodMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            targetPayload.Slice(2, 2));
        if ((uint)methodMapIndex >= (uint)body.MethodCount)
        {
            throw Invalid(
                "The selected GenericCall target has an invalid MethodMap index.");
        }

        var methodToken = BinaryPrimitives.ReadUInt32LittleEndian(
            body.MethodMap.Slice(methodMapIndex * sizeof(uint),
                sizeof(uint)));
        var importRow = (methodToken >> 24) == 0x1C
            ? checked((int)(methodToken & 0x00FF_FFFF)) : 0;
        var targetDefinition = (methodToken >> 24) switch
        {
            0x16 => methodToken,
            0x1C => tables.Imports.GetExpectedDefinitionForImportRow(
                importRow),
            _ => throw Invalid(
                "The selected GenericCall target is not a MethodDef or MethodDef Import."),
        };
        if ((targetDefinition >> 24) != 0x16)
        {
            throw Invalid(
                "The selected GenericCall Import does not expect a MethodDef.");
        }

        var targetMethodRow = checked((int)(targetDefinition & 0x00FF_FFFF));
        var targetImport = importRow != 0
            ? CreateExpectedGenericMethodImport(tables, context,
                artifactIndex, moduleRow, importRow)
            : CreateGenericMethodImport(tables, context,
                artifactIndex, moduleRow, targetDefinition, "",
                isMethodDefTarget: true);
        var targetSignature = tables.Methods.GetSignature(targetMethodRow);
        var (_, targetFormalCount)
            = tables.Signatures.GetGenericRange(targetSignature);
        var targetArgumentCount = BinaryPrimitives.ReadUInt32LittleEndian(
            targetPayload.Slice(12, sizeof(uint)));
        if (targetArgumentCount != (uint)targetFormalCount
            || targetImport.Formals.Length != targetFormalCount)
        {
            throw Invalid(
                "The GenericCall actual and target formal counts differ.");
        }

        var typeArguments = ImmutableArray.CreateBuilder<PrimitiveType>(
            targetFormalCount);
        var typeArgumentMetadata
            = ImmutableArray.CreateBuilder<BoundGenericNominalTypeArgument>(
                targetFormalCount);
        for (var ordinal = 0; ordinal < targetFormalCount; ordinal++)
        {
            var argumentOffset = checked(16 + (ordinal * 8));
            if (targetPayload[argumentOffset] != 1)
            {
                throw Unsupported(
                    "The G1 consumer currently supports TypeActual GenericCall arguments only.");
            }

            var typeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(
                targetPayload.Slice(argumentOffset + 4, sizeof(uint)));
            var actual = ResolveGenericMethodTypeSpec(context,
                artifactIndex, tables, moduleRow, tables.TypeSpecs,
                tables.GenericParams,
                callerFormalRecords,
                callerMethodRow,
                context.GetFormalTypeOwnerOrdinal(artifactIndex,
                    callerMethodRow), typeSpecRef,
                callerFormals, allowFormalForwarding: false,
                ordinal: callerFormals.Length, out _);
            typeArguments.Add(actual);
            typeArgumentMetadata.Add(context.ReadGenericNominalTypeArgument(
                tables, artifactIndex, moduleRow, typeSpecRef,
                callerMethodRow, callerFormals, callerFormalRecords,
                actual));
        }
        var actualTypes = typeArguments.MoveToImmutable();
        var actualTypeMetadata = typeArgumentMetadata.MoveToImmutable();
        var targetFormalTypes = targetImport.Formals.Select(
            static formal => formal.PlaceholderType).ToImmutableArray();
        var callerFormalTypes = callerFormals;
        var callerFormalTypeOwnerOrdinal
            = context.GetFormalTypeOwnerOrdinal(artifactIndex,
                callerMethodRow);

        var (_, parameterCount)
            = tables.Signatures.GetParamRange(targetSignature);
        var inputCount = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(callSitePayload.Slice(8, 4)));
        if (parameterCount != inputCount
            || targetImport.ProofFunction.ParameterCount != parameterCount)
        {
            throw Invalid(
                "The GenericCall input count differs from its selected MethodSig.");
        }

        var arguments = ImmutableArray.CreateBuilder<BoundExpression>(
            parameterCount);
        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            var inputOffset = 16 + (ordinal * 4);
            var sourceUse = callSitePayload[inputOffset + 1];
            if (callSitePayload[inputOffset] != 2
                || sourceUse is not (1 or 2 or 5))
            {
                throw Unsupported(
                    "The G1 consumer currently supports copy or ownership-transfer parameter-Home GenericCall inputs only.");
            }

            var homeIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                callSitePayload.Slice(inputOffset + 2, 2));
            if ((uint)homeIndex >= (uint)body.HomeCount)
            {
                throw Invalid(
                    "A GenericCall input references an invalid Home.");
            }

            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            var parameterOrdinal = BinaryPrimitives
                .ReadUInt16LittleEndian(home.Slice(4, 2));
            if (home[0] != 2 || parameterOrdinal >= callerVariables.Count)
            {
                throw Invalid(
                    "A GenericCall input does not select a caller parameter Home.");
            }

            var targetParameterType = targetImport.ProofFunction
                .Variables[ordinal].Type;
            if (!TrySubstituteGenericMethodType(
                    targetParameterType, targetFormalTypes, actualTypes,
                    actualTypeMetadata, targetImport, context,
                    artifactIndex, callerMethodRow,
                    callerFormalTypes, callerFormalTypeOwnerOrdinal, 0,
                    out targetParameterType))
            {
                throw Unsupported(
                    "The G1 GenericCall parameter type uses an unsupported substitution term.");
            }

            var descriptorRead = new BoundRead(parameterOrdinal,
                callerVariables[parameterOrdinal].Type, default);
            if (sourceUse == 5
                && descriptorRead.Type != targetParameterType)
            {
                throw Invalid(
                    "A GenericCall reborrow changes the initialization-reference type.");
            }
            BoundExpression argument = sourceUse switch {
                1 => descriptorRead,
                2 => new BoundMove(parameterOrdinal,
                    callerVariables[parameterOrdinal].Type, default),
                5 => new BoundReferenceReborrow(descriptorRead,
                    targetParameterType, default),
                _ => throw Invalid(
                    "A GenericCall input uses an unsupported source-use kind."),
            };

            var argumentIsInitializationReference
                = IsGenericMethodInitializationReferenceType(argument.Type,
                    context.CheckedReferenceTypeDefinitions);
            var targetIsInitializationReference
                = IsGenericMethodInitializationReferenceType(
                    targetParameterType,
                    context.CheckedReferenceTypeDefinitions);
            if ((sourceUse == 5)
                != (argumentIsInitializationReference
                    && targetIsInitializationReference))
            {
                throw Invalid(
                    "A GenericCall initialization-reference input does not preserve its scoped reservation.");
            }

            if (argument.Type != targetParameterType)
            {
                throw Invalid(
                    "A GenericCall input type differs from its substituted target formal.");
            }
            if (sourceUse == 2
                && !IsDirectGenericMethodMoveReturn(
                    targetImport.ProofFunction, ordinal))
            {
                throw new NotSupportedException(
                    "The G1 consumer requires a direct move-return target for ownership-transfer GenericCall inputs.");
            }

            arguments.Add(argument);
        }

        var resultType = targetImport.ProofFunction.ReturnType;
        if (!TrySubstituteGenericMethodType(
                resultType, targetFormalTypes, actualTypes,
                actualTypeMetadata, targetImport,
                context, artifactIndex, callerMethodRow, callerFormalTypes,
                callerFormalTypeOwnerOrdinal, 0, out resultType))
        {
            throw Unsupported(
                "The G1 GenericCall result type uses an unsupported substitution term.");
        }

        var resultHome = BinaryPrimitives.ReadUInt16LittleEndian(
            callSitePayload.Slice(6, 2));
        var targetDoesNotReturn
            = targetImport.ProofFunction.DoesNotReturn;
        if (resultType != callerReturnType
            || (targetDoesNotReturn
                ? callSite[5] != 0 || resultHome != 0 || hasReturnHome
                : callSite[5] != 2 || !hasReturnHome
                    || resultHome
                        != code[callSiteInstructionLength + 1]))
        {
            throw Invalid(
                "The GenericCall result differs from the open MethodSig return type.");
        }

        var call = new BoundCall(0, arguments.MoveToImmutable(),
            resultType, default) {
            ImportedGenericMethodCall = targetImport,
            ImportedGenericMethodTypeArguments = actualTypes,
            IsUnsafeAuthorized = targetImport.Signature.RequiresUnsafeCaller,
        };
        // Authorization is a published Body fact, not an unsafe declaration scope.
        BoundExpression value = targetImport.Signature.RequiresUnsafeCaller
            ? new BoundUnsafeExpression(call, default) : call;
        return [new BoundReturn(value, default)];
    }

    private static bool TrySubstituteGenericMethodType(
        PrimitiveType type, ImmutableArray<PrimitiveType> targetFormalTypes,
        ImmutableArray<PrimitiveType> typeArguments,
        ImmutableArray<BoundGenericNominalTypeArgument>
            typeArgumentMetadata,
        BoundGenericMethodImport targetImport,
        GenericMethodReadContext context, int callerArtifactIndex,
        int callerMethodRow,
        ImmutableArray<PrimitiveType> callerFormalTypes,
        int callerFormalTypeOwnerOrdinal, int depth,
        out PrimitiveType substitutedType)
    {
        substitutedType = default;
        if (depth > SemanticVerifier.MaximumDepth
            || targetFormalTypes.Length != typeArguments.Length)
        {
            return false;
        }

        var targetFormalOrdinal = targetFormalTypes.IndexOf(type);
        if (targetFormalOrdinal >= 0)
        {
            substitutedType = typeArguments[targetFormalOrdinal];
            return true;
        }

        if (CheckedReferenceTypeContract.IsValidCompleteType(type,
                context.CheckedReferenceTypeDefinitions))
        {
            var reference = context.CheckedReferenceTypeDefinitions[
                CheckedReferenceTypes.Index(type)];
            if (reference.IsOptional || reference.IsOptionalValue
                || !TrySubstituteGenericMethodType(reference.ReferentType,
                    targetFormalTypes, typeArguments, typeArgumentMetadata,
                    targetImport,
                    context, callerArtifactIndex, callerMethodRow,
                    callerFormalTypes, callerFormalTypeOwnerOrdinal,
                    depth + 1, out var referentType))
            {
                return false;
            }

            substitutedType = context.GetCheckedReferenceType(
                callerArtifactIndex, callerMethodRow, referentType,
                reference.IsWritable, reference.IsGarbageCollected,
                reference.IsInitialization,
                reference.IsReferentWritable);
            return true;
        }

        if (BoundSpanTypeTypes.IsValidType(type, context.SpanTypes))
        {
            var span = context.SpanTypes[BoundSpanTypeTypes.Index(type)];
            PrimitiveType elementType;
            var targetFormalOwnerOrdinal
                = BoundSpanTypeContract.ImportedFormalDefinitionOrdinal
                    - targetImport.FormalTypeOwnerOrdinal;
            if (span.ElementFormalDefinitionOrdinal
                == targetFormalOwnerOrdinal)
            {
                if ((uint)span.ElementFormalOrdinal
                    >= (uint)targetFormalTypes.Length
                    || targetFormalTypes[span.ElementFormalOrdinal]
                        != span.ElementType)
                {
                    return false;
                }
                elementType = typeArguments[span.ElementFormalOrdinal];
            }
            else if (span.ElementFormalDefinitionOrdinal == -1)
            {
                if (!TrySubstituteGenericMethodType(span.ElementType,
                        targetFormalTypes, typeArguments,
                        typeArgumentMetadata, targetImport,
                        context, callerArtifactIndex, callerMethodRow,
                        callerFormalTypes, callerFormalTypeOwnerOrdinal,
                        depth + 1, out elementType))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            var elementFormalOrdinal = callerFormalTypes.IndexOf(elementType);
            if (span.IsElementWritable && elementFormalOrdinal < 0)
            {
                return false;
            }
            substitutedType = context.GetSpanType(elementType,
                callerFormalTypeOwnerOrdinal, elementFormalOrdinal,
                span.IsElementWritable, span.IsWritable,
                span.IsInitialization);
            return true;
        }

        if (BoundGenericNominalTypeTypes.IsType(type))
        {
            var instanceIndex = BoundGenericNominalTypeTypes.Index(type);
            if ((uint)instanceIndex
                    >= (uint)context.GenericNominalTypeInstances.Count)
            {
                return false;
            }

            var instance = context.GenericNominalTypeInstances[
                instanceIndex];
            var arguments = ImmutableArray.CreateBuilder<
                BoundGenericNominalTypeArgument>(
                    instance.Arguments.Length);
            var changed = false;
            var hasOpenTypeArgumentTerm = false;
            foreach (var argument in instance.Arguments)
            {
                if (argument.TypeArgumentTerm is
                    BoundGenericMethodTypeTerm typeArgumentTerm)
                {
                    if (!BoundGenericMethodTypeTerms
                            .TryGetFormalDependencies(typeArgumentTerm,
                                out var dependencies))
                    {
                        return false;
                    }
                    hasOpenTypeArgumentTerm |= !dependencies.IsEmpty;
                }
            }

            ImmutableArray<BoundGenericMethodTypeTerm> callerTypeArguments = [];
            if (hasOpenTypeArgumentTerm)
            {
                if (typeArgumentMetadata.IsDefault
                    || typeArgumentMetadata.Length != typeArguments.Length)
                {
                    return false;
                }

                var callerTypeArgumentBuilder = ImmutableArray.CreateBuilder<
                    BoundGenericMethodTypeTerm>(typeArgumentMetadata.Length);
                foreach (var actual in typeArgumentMetadata)
                {
                    if (!TryGetGenericNominalTypeArgumentTerm(actual,
                            context.GenericNominalTypeDefinitions.ToImmutable(),
                            context.GenericNominalTypeInstances,
                            out var actualTerm, formalTypes: callerFormalTypes,
                            formalTypeOwnerOrdinal:
                                callerFormalTypeOwnerOrdinal))
                    {
                        return false;
                    }
                    callerTypeArgumentBuilder.Add(actualTerm);
                }
                callerTypeArguments =
                    callerTypeArgumentBuilder.MoveToImmutable();
            }

            foreach (var argument in instance.Arguments)
            {
                if (argument.Kind != BoundGenericArgumentKind.Type
                    || argument.NominalTypeIdentity is not null)
                {
                    arguments.Add(argument);
                    continue;
                }

                if (argument.HasTypeFormalIdentity)
                {
                    if (argument.TypeFormalDefinitionOrdinal
                            != targetImport.FormalTypeOwnerOrdinal
                        || (uint)argument.TypeFormalOrdinal
                            >= (uint)typeArgumentMetadata.Length
                        || argument.Qualifiers != 0)
                    {
                        return false;
                    }

                    var actual = typeArgumentMetadata[
                        argument.TypeFormalOrdinal];
                    if (actual.Kind != BoundGenericArgumentKind.Type)
                    {
                        return false;
                    }
                    arguments.Add(actual);
                    changed = true;
                    continue;
                }

                if (argument.HasTypeFormalOwnerIdentity)
                {
                    if (argument.TypeFormalDefinitionOrdinal
                            != targetImport.FormalTypeOwnerOrdinal
                        || argument.TypeArgumentTerm is not
                            BoundGenericMethodTypeTerm targetTypeTerm
                        || callerTypeArguments.IsDefault
                        || !BoundGenericMethodTypeTerms.TrySubstitute(
                            targetTypeTerm, callerTypeArguments,
                            out var callerTypeTerm)
                        || !BoundGenericMethodTypeTerms
                            .TryGetFormalDependencies(callerTypeTerm,
                                out var callerFormalDependencies)
                        || !TrySubstituteGenericMethodType(argument.Type,
                            targetFormalTypes, typeArguments,
                            typeArgumentMetadata, targetImport, context,
                            callerArtifactIndex, callerMethodRow,
                            callerFormalTypes,
                            callerFormalTypeOwnerOrdinal, depth + 1,
                            out var callerArgumentType))
                    {
                        return false;
                    }

                    var callerFormalOrdinal =
                        callerTypeTerm.Kind
                            == BoundGenericMethodTypeTermKind.Formal
                            && callerFormalDependencies.Length == 1
                            ? callerFormalDependencies[0] : -1;
                    var rewrittenArgument = argument with {
                        Type = callerArgumentType,
                        TypeFormalDefinitionOrdinal =
                            callerFormalDependencies.IsEmpty
                                ? -1 : callerFormalTypeOwnerOrdinal,
                        TypeFormalOrdinal = callerFormalOrdinal,
                        TypeFormalOwnerKind =
                            BoundGenericTypeFormalOwnerKind.GenericMethod,
                        TypeArgumentTerm = callerTypeTerm,
                        TypeSpecForm = 0,
                        TypeCode = 0,
                        ArgumentCode = 0,
                        TypeSpecReference = 0,
                        TypeSpecProviderIdentity = null,
                    };
                    changed |= callerArgumentType != argument.Type
                        || callerFormalTypeOwnerOrdinal
                            != argument.TypeFormalDefinitionOrdinal
                        || !BoundGenericMethodTypeTerms.AreEquivalent(
                            targetTypeTerm, callerTypeTerm, depth + 1);
                    arguments.Add(rewrittenArgument);
                    continue;
                }

                if (argument.Qualifiers != 0
                    && targetFormalTypes.Contains(argument.Type))
                {
                    return false;
                }
                if (!TrySubstituteGenericMethodType(argument.Type,
                        targetFormalTypes, typeArguments,
                        typeArgumentMetadata, targetImport,
                        context, callerArtifactIndex, callerMethodRow,
                        callerFormalTypes, callerFormalTypeOwnerOrdinal,
                        depth + 1, out var argumentType))
                {
                    return false;
                }
                if (argumentType != argument.Type)
                {
                    changed = true;
                    arguments.Add(argument with {
                        Type = argumentType,
                        TypeSpecForm = 0,
                        TypeCode = 0,
                        ArgumentCode = 0,
                        TypeSpecReference = 0,
                        TypeSpecProviderIdentity = null,
                    });
                }
                else
                {
                    arguments.Add(argument);
                }
            }

            if (!changed)
            {
                substitutedType = type;
                return true;
            }

            var substitutedArguments = arguments.MoveToImmutable();
            substitutedType = context.GetGenericNominalTypeInstance(
                instance.Definition, substitutedArguments,
                instance.Qualifiers, instance.TypeSpecForm);
            return true;
        }

        substitutedType = type;
        return true;
    }

    private static BoundCall DecodeG1InterfaceRequirementCall(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow,
        int callerMethodRow, ImmutableArray<PrimitiveType> callerFormals,
        TrRmGenericMethodFormalImport[] callerFormalRecords,
        PrimitiveType callerReturnType,
        ImmutableArray<BoundVariable>.Builder callerVariables,
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> code,
        int callSiteInstructionLength, bool hasReturnHome,
        ReadOnlySpan<byte> callSite, ReadOnlySpan<byte> callSitePayload,
        ReadOnlySpan<byte> targetRecord, out BoundFunction supportFunction)
    {
        supportFunction = null!;
        if (!context.Schema.UsesMethodParameterLabels())
        {
            throw Unsupported(
                "The G1 consumer requires an exact label-bearing open interface-requirement schema.");
        }
        if (targetRecord.Length != 20)
        {
            throw Invalid(
                "The interface-requirement GenericCallTarget has an invalid extent.");
        }

        var targetPayload = targetRecord[4..];
        var targetTypeSpec = BinaryPrimitives.ReadUInt32LittleEndian(
            targetPayload.Slice(4, sizeof(uint)));
        var evidenceRow = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(targetPayload.Slice(8, sizeof(uint))));
        var argumentCount = BinaryPrimitives.ReadUInt32LittleEndian(
            targetPayload.Slice(12, sizeof(uint)));
        if (targetPayload[0] != 2 || targetPayload[1] != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(
                targetPayload.Slice(2, sizeof(ushort))) == ushort.MaxValue
            || targetTypeSpec == 0 || evidenceRow <= 0
            || argumentCount != 0
            || callSite[5] != 2 || !hasReturnHome
            || BinaryPrimitives.ReadUInt32LittleEndian(
                callSitePayload.Slice(12, sizeof(uint))) != 0)
        {
            throw Unsupported(
                "The G1 consumer requires a complete G1L1 open interface-requirement GenericCall.");
        }

        var methodMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            targetPayload.Slice(2, sizeof(ushort)));
        if ((uint)methodMapIndex >= (uint)body.MethodCount)
        {
            throw Invalid(
                "The interface-requirement GenericCall has an invalid MethodMap index.");
        }

        var methodToken = BinaryPrimitives.ReadUInt32LittleEndian(
            body.MethodMap.Slice(methodMapIndex * sizeof(uint),
                sizeof(uint)));
        var importRow = (methodToken >> 24) == 0x1C
            ? checked((int)(methodToken & 0x00FF_FFFF)) : 0;
        var targetDefinition = (methodToken >> 24) switch
        {
            0x16 => methodToken,
            0x1C => tables.Imports.GetExpectedDefinitionForImportRow(
                importRow),
            _ => throw Invalid(
                "The interface-requirement GenericCall target is not a MethodDef or MethodDef Import."),
        };
        if ((targetDefinition >> 24) != 0x16)
        {
            throw Invalid(
                "The selected interface MethodDef Import has the wrong target kind.");
        }
        if (importRow == 0
            && tables.Imports.IsExpectedDefinition(targetDefinition))
        {
            throw Invalid(
                "An expected interface MethodDef must be selected through its exact Import row.");
        }

        var targetMethodRow = checked((int)(
            targetDefinition & 0x00FF_FFFF));
        var ownerTypeDef = tables.Methods.GetOwnerType(targetMethodRow);
        var interfaceDef
            = tables.Interfaces.GetInterfaceDefForTypeDef(ownerTypeDef);
        if (ownerTypeDef == 0 || interfaceDef == 0
            || tables.InterfaceMethods.GetOwnerInterfaceDefForMethod(
                targetMethodRow) != interfaceDef)
        {
            throw Invalid(
                "The interface-requirement MethodDef is not a direct InterfaceMethod.");
        }

        var interfaceMethodRow = 0;
        var (firstInterfaceMethod, interfaceMethodCount)
            = tables.Interfaces.GetMethodRange(interfaceDef);
        for (var ordinal = 0; ordinal < interfaceMethodCount; ordinal++)
        {
            var candidate = firstInterfaceMethod + ordinal;
            if (tables.InterfaceMethods.GetMethodDefRow(candidate)
                == targetMethodRow)
            {
                interfaceMethodRow = candidate;
                break;
            }
        }
        if (interfaceMethodRow == 0)
        {
            throw Invalid(
                "The interface-requirement MethodDef has no InterfaceMethod row.");
        }

        if (!context.InterfaceDefinitionsByTypeDef.TryGetValue(
                (artifactIndex, moduleRow, ownerTypeDef),
                out var interfaceDefinition))
        {
            _ = CreateGenericInterfaceDefinitions(tables, context,
                artifactIndex, moduleRow);
            if (!context.InterfaceDefinitionsByTypeDef.TryGetValue(
                    (artifactIndex, moduleRow, ownerTypeDef),
                    out interfaceDefinition))
            {
                throw Unsupported(
                    "The interface-requirement GenericCall has no retained provider declaration closure.");
            }
        }

        var memberOrdinal = tables.InterfaceMethods.GetOrdinal(
            interfaceMethodRow);
        if ((uint)memberOrdinal >= (uint)interfaceDefinition.Members.Length)
        {
            throw Invalid(
                "The interface-requirement MethodDef has no retained member view.");
        }

        var callerDefinitionId = 0x1600_0000u
            | checked((uint)callerMethodRow);
        var evidenceOwner = tables.Constraints
            .GetOwnerGenericParam(evidenceRow);
        var evidenceFormalOrdinal = tables.GenericParams
            .GetOrdinal(evidenceOwner);
        var (_, callerFormalCount)
            = tables.Signatures.GetGenericRange(
                tables.Methods.GetSignature(callerMethodRow));
        var (firstEvidenceConstraint, evidenceConstraintCount)
            = tables.GenericParams.GetConstraintRange(evidenceOwner);
        if (tables.GenericParams.GetOwnerDefId(evidenceOwner)
                != callerDefinitionId
            || tables.GenericParams.GetFormalKind(evidenceOwner) != 1
            || evidenceFormalOrdinal >= (uint)callerFormalCount
            || evidenceFormalOrdinal >= (uint)callerFormals.Length
            || evidenceFormalOrdinal >= (uint)callerFormalRecords.Length
            || evidenceRow < firstEvidenceConstraint
            || evidenceRow >= firstEvidenceConstraint
                + evidenceConstraintCount
            || tables.Constraints.GetKind(evidenceRow) != 1)
        {
            throw Invalid(
                "The interface-requirement GenericCall evidence is not an in-scope interface Constraint.");
        }

        var formalOrdinal = checked((int)evidenceFormalOrdinal);
        var remainingVisits = SemanticVerifier.MaximumAnalysisVisits;
        var evidenceInterface = DecodeG1MethodInterfaceConstraint(tables,
            context, artifactIndex, moduleRow, callerMethodRow,
            callerFormalCount, tables.Constraints.GetPayload(evidenceRow),
            ref remainingVisits);
        var ownerInterface = DecodeG1MethodInterfaceTypeSpec(tables,
            context, artifactIndex, moduleRow, callerMethodRow,
            callerFormalCount, targetTypeSpec, 0,
            ref remainingVisits);
        if (!BoundInterfaceContract.AreEquivalent(ownerInterface,
                evidenceInterface)
            || !callerFormalRecords[formalOrdinal].InterfaceConstraints
                .Any(constraint =>
                    BoundInterfaceContract.AreEquivalent(constraint,
                        evidenceInterface)))
        {
            throw Invalid(
                "The interface-requirement GenericCall owner differs from its retained Constraint evidence.");
        }

        var expectedTypeDef = tables.TypeSpecs.GetNominalTypeDefRow(
            targetTypeSpec, tables.TypeDefs, tables.Imports);
        var targetFlags = tables.Methods.GetFlags(targetMethodRow);
        if (expectedTypeDef != ownerTypeDef
            || tables.Methods.GetBodyRow(targetMethodRow) != 0
            || (targetFlags & 3) != 2
            || ((targetFlags & 4) != 0)
                != interfaceDefinition.Members[memberOrdinal]
                    .RequiresMutableReceiver
            || tables.Methods.GetContractRange(targetMethodRow).Count != 0)
        {
            throw Unsupported(
                "The G1 interface-requirement target is outside the bodyless public requirement subset.");
        }

        var targetSignature = tables.Methods.GetSignature(targetMethodRow);
        var (_, targetFormalCount)
            = tables.Signatures.GetGenericRange(targetSignature);
        var (_, parameterCount)
            = tables.Signatures.GetParamRange(targetSignature);
        if (targetFormalCount != 0
            || parameterCount
                != interfaceDefinition.Members[memberOrdinal].Parameters.Length
            || tables.Signatures.GetReceiverTypeSpec(targetSignature) == 0)
        {
            throw Unsupported(
                "The G1 interface-requirement MethodSig has unsupported generic or receiver facts.");
        }

        var member = interfaceDefinition.Members[memberOrdinal];
        if (member.Kind != BoundInterfaceMemberKind.Callable
            || member.IsStatic || member.HasBody
            || !member.TypeParameters.IsEmpty
            || !member.Constraints.IsEmpty)
        {
            throw Unsupported(
                "The selected G1 InterfaceMethod is outside the callable requirement subset.");
        }
        if (member.RequiresMutableReceiver
            && !callerFormalRecords[formalOrdinal].RequiresWritable)
        {
            throw Invalid(
                "A mutable interface requirement lacks writable formal evidence.");
        }

        PrimitiveType ResolveRequirementType(BoundInterfaceType type,
            bool allowVoid)
        {
            var resolved = BoundInterfaceContract.SubstituteTypeParameters(
                type, ownerInterface.TypeArguments);
            if (resolved.Kind == BoundInterfaceTypeKind.Primitive
                && Array.IndexOf(s_types, resolved.PrimitiveType) >= 0
                && (allowVoid
                    || resolved.PrimitiveType != PrimitiveType.Void)
                && resolved.TypeArguments.IsDefaultOrEmpty
                && resolved.ModuleImport is null
                && !resolved.IsWritable && !resolved.IsOptional
                && !resolved.IsGarbageCollected
                && !resolved.IsInitialization)
            {
                return resolved.PrimitiveType;
            }
            if (resolved.Kind == BoundInterfaceTypeKind.TypeParameter
                && resolved.TypeParameterScope
                    == BoundInterfaceTypeParameterScope.Member
                && (uint)resolved.TypeParameterOrdinal
                    < (uint)callerFormals.Length
                && resolved.TypeArguments.IsDefaultOrEmpty
                && !resolved.IsWritable && !resolved.IsOptional
                && !resolved.IsGarbageCollected
                && !resolved.IsInitialization)
            {
                return callerFormals[resolved.TypeParameterOrdinal];
            }

            throw Unsupported(
                "A G1 InterfaceMethod signature requires an unsupported owner substitution.");
        }

        var targetReturnType = member.ResultType is BoundInterfaceType
            resultType
            ? ResolveRequirementType(resultType, allowVoid: true)
            : PrimitiveType.Void;
        if (targetReturnType == PrimitiveType.Void
            || targetReturnType != callerReturnType)
        {
            throw Unsupported(
                "The G1 interface-requirement result is outside the direct-return subset.");
        }

        var targetVariables
            = ImmutableArray.CreateBuilder<BoundVariable>(
                parameterCount + 1);
        var implementingType = callerFormals[formalOrdinal];
        targetVariables.Add(new BoundVariable("this", implementingType,
            member.RequiresMutableReceiver, default));
        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            var parameter = member.Parameters[ordinal];
            if (parameter.Mode != BoundInterfaceParameterMode.Value)
            {
                throw Unsupported(
                    "The G1 InterfaceMethod has an unsupported parameter mode.");
            }
            targetVariables.Add(new BoundVariable(parameter.Name,
                ResolveRequirementType(parameter.Type, allowVoid: false),
                false, default));
        }

        var requirement = new BoundInterfaceMemberIdentity(ownerInterface,
            memberOrdinal, member.DeclarationKey);
        supportFunction = new BoundFunction(
            "$g1-interface-requirement."
                + memberOrdinal.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            targetReturnType, targetVariables.MoveToImmutable(),
            parameterCount + 1, new BoundBlock([], default), default) {
            IsExternal = true,
            IsPrivate = true,
            IsStatic = false,
            RequiresUnsafeCaller = member.RequiresUnsafeCaller,
            DoesNotReturn = member.DoesNotReturn,
            InterfaceRequirement = requirement,
        };

        var inputCount = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(callSitePayload.Slice(8, sizeof(uint))));
        if (inputCount != supportFunction.ParameterCount
            || !hasReturnHome
            || callSite[5] != 2
            || targetReturnType == PrimitiveType.Void)
        {
            throw Invalid(
                "The interface-requirement CallSite input or result shape is invalid.");
        }

        var arguments
            = ImmutableArray.CreateBuilder<BoundExpression>(inputCount);
        for (var ordinal = 0; ordinal < inputCount; ordinal++)
        {
            var inputOffset = 16 + (ordinal * 4);
            var expectedSourceUse = (byte)1;
            if (ordinal == 0 && context.Schema.UsesMethodReceiverAccess())
            {
                expectedSourceUse = member.RequiresMutableReceiver
                    ? (byte)4 : (byte)3;
            }
            if (callSitePayload[inputOffset] != 2
                || callSitePayload[inputOffset + 1] != expectedSourceUse)
            {
                throw Unsupported(
                    "The G1 interface-requirement CallSite input mode does not match its selected MethodDef.");
            }

            var homeIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                callSitePayload.Slice(inputOffset + 2, sizeof(ushort)));
            if ((uint)homeIndex >= (uint)body.HomeCount)
            {
                throw Invalid(
                    "The interface-requirement CallSite references an invalid Home.");
            }
            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            var parameterOrdinal = BinaryPrimitives
                .ReadUInt16LittleEndian(home.Slice(4, sizeof(ushort)));
            if (home[0] != 2
                || parameterOrdinal >= callerVariables.Count
                || callerVariables[parameterOrdinal].Type
                    != supportFunction.Variables[ordinal].Type
                || callerVariables[parameterOrdinal].IsMutable
                || callerVariables[parameterOrdinal].IsOut
                || callerVariables[parameterOrdinal].IsInit
                || callerVariables[parameterOrdinal].IsImplicitRef)
            {
                throw Invalid(
                    "The interface-requirement CallSite input does not match its parameter Home.");
            }

            var inputFormalOrdinal = callerFormals.IndexOf(
                callerVariables[parameterOrdinal].Type);
            if (inputFormalOrdinal >= 0
                && callerFormalRecords[inputFormalOrdinal]
                    .OwnershipDomain != 1)
            {
                throw Unsupported(
                    "The G1 interface-requirement CallSite input lacks a Copy domain.");
            }
            var argumentType = callerVariables[parameterOrdinal].Type;
            arguments.Add(ordinal == 0
                && member.RequiresMutableReceiver
                    ? new BoundMutableReceiverLoan(parameterOrdinal,
                        argumentType, default)
                    : new BoundRead(parameterOrdinal, argumentType, default));
        }

        var resultHome = BinaryPrimitives.ReadUInt16LittleEndian(
            callSitePayload.Slice(6, sizeof(ushort)));
        if (resultHome != code[callSiteInstructionLength + 1])
        {
            throw Invalid(
                "The interface-requirement ReturnHome differs from its CallSite.");
        }

        return new BoundCall(0, arguments.MoveToImmutable(),
            targetReturnType, default) {
            InterfaceCallIdentity = new BoundInterfaceCallIdentity(
                implementingType, ownerInterface, ownerInterface,
                requirement, IsConstraintCall: true),
        };
    }

    private static bool IsDirectGenericMethodMoveReturn(
        BoundFunction function, int parameterOrdinal)
        => function.Body.RetiredManagedLocals.IsEmpty
            && function.Body.Statements.Length == 1
            && function.Body.Statements[0] is BoundReturn {
                Value: BoundMove move,
            }
            && move.Variable == parameterOrdinal
            && move.Type == function.ReturnType;

    private static int FindGenericMethodModule(
        TrRmModuleTableReader modules, string moduleIdentity)
    {
        for (var candidate = 1; candidate <= modules.Count; candidate++)
        {
            if (StringComparer.Ordinal.Equals(
                modules.GetIdentity(candidate), moduleIdentity))
            {
                return candidate;
            }
        }
        throw new ArgumentException(
            "The selected G1 Module identity is not present.",
            nameof(moduleIdentity));
    }

    private sealed record TrRmGenericMethodFormalImport(
        PrimitiveType PlaceholderType, string Name,
        byte OwnershipDomain, byte NullabilityDomain,
        bool RequiresWritable, bool RequiresBlittable,
        ImmutableArray<BoundInterfaceType> InterfaceConstraints,
        BoundGenericMethodTypeDefault? TypeDefault);

    private static BoundInterfaceType DecodeG1MethodInterfaceConstraint(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow,
        int methodRow, int formalCount, uint typeSpecRef,
        ref int remainingVisits)
        => DecodeG1MethodInterfaceTypeSpec(tables, context, artifactIndex,
            moduleRow, methodRow, formalCount, typeSpecRef, 0,
            ref remainingVisits);

    private static BoundInterfaceType DecodeG1MethodInterfaceTypeSpec(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow,
        int methodRow, int formalCount, uint typeSpecRef, int depth,
        ref int remainingVisits)
    {
        if (depth > SemanticVerifier.MaximumDepth
            || --remainingVisits < 0)
        {
            throw Unsupported(
                "The G1 interface constraint exceeds the supported analysis limit.");
        }

        var reference = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
        var writableFormal = reference.Form == 0x04
            && reference.IsRow && reference.Qualifiers == 0x01;
        if (reference.Qualifiers != 0 && !writableFormal)
        {
            throw Unsupported(
                "A G1 interface constraint uses an unsupported TypeSpec qualifier.");
        }

        if (!reference.IsRow)
        {
            var inlineTypeIndex = Array.IndexOf(s_codes,
                reference.TypeCode);
            if ((typeSpecRef & 0xFF00_0000) == 0xF200_0000
                && reference.Form == 0x01 && inlineTypeIndex >= 0)
            {
                var inlineType = s_types[inlineTypeIndex];
                if (inlineType != PrimitiveType.Void)
                {
                    return new BoundInterfaceType(
                        BoundInterfaceTypeKind.Primitive, inlineType, null,
                        -1, [], false, false, false, false);
                }
            }

            throw Unsupported(
                "A G1 interface constraint uses an unsupported inline type argument.");
        }

        var row = tables.TypeSpecs.GetRow(reference.Row);
        if (reference.Form == 0x01 && row.Length == 3)
        {
            var typeIndex = Array.IndexOf(s_codes, reference.TypeCode);
            if (typeIndex >= 0 && s_types[typeIndex] != PrimitiveType.Void)
            {
                return new BoundInterfaceType(
                    BoundInterfaceTypeKind.Primitive, s_types[typeIndex],
                    null, -1, [], false, false, false, false);
            }
        }
        else if (reference.Form == 0x04 && row.Length == 6)
        {
            var formalRow = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(row.Slice(2, sizeof(uint))));
            var expectedOwner = 0x1600_0000u
                | checked((uint)methodRow);
            var formalOrdinal = tables.GenericParams
                .GetOrdinal(formalRow);
            if (tables.GenericParams.GetFormalKind(formalRow) == 1
                && tables.GenericParams.GetOwnerDefId(formalRow)
                    == expectedOwner
                && formalOrdinal < (uint)formalCount)
            {
                return new BoundInterfaceType(
                    BoundInterfaceTypeKind.TypeParameter,
                    PrimitiveType.Void, null,
                    checked((int)formalOrdinal), [], writableFormal, false,
                    false, false) {
                    TypeParameterScope
                        = BoundInterfaceTypeParameterScope.Member,
                };
            }

            throw Invalid(
                "A G1 interface constraint references a foreign or unsupported formal.");
        }
        else if (reference.Form is 0x02 or 0x03 or 0x05)
        {
            var typeDef = tables.TypeSpecs.GetNominalTypeDefRow(
                typeSpecRef, tables.TypeDefs, tables.Imports);
            var interfaceDef
                = tables.Interfaces.GetInterfaceDefForTypeDef(typeDef);
            if (tables.TypeDefs.GetKind(typeDef) != 3 || interfaceDef == 0)
            {
                throw Unsupported(
                    "A G1 interface constraint must name a declared interface.");
            }

            if (!context.InterfaceDefinitionsByTypeDef.TryGetValue(
                    (artifactIndex, moduleRow, typeDef),
                    out var definition))
            {
                throw Unsupported(
                    "A G1 interface constraint has no retained provider declaration closure.");
            }

            var actualCount = 0;
            var argumentOffset = 0;
            if (reference.Form == 0x05)
            {
                actualCount = (row.Length - 6) / sizeof(uint);
                argumentOffset = 6;
            }
            if (definition.Formals.Length != actualCount
                || definition.Formals.Any(static formal =>
                    formal.Kind != BoundInterfaceFormalKind.Type))
            {
                throw Invalid(
                    "A G1 interface constraint has an incomplete or unsupported interface instance.");
            }

            var arguments
                = ImmutableArray.CreateBuilder<BoundInterfaceTypeArgument>(
                    actualCount);
            for (var ordinal = 0; ordinal < actualCount; ordinal++)
            {
                var argumentTypeSpec = BinaryPrimitives
                    .ReadUInt32LittleEndian(row.Slice(
                        argumentOffset + (ordinal * sizeof(uint)),
                        sizeof(uint)));
                var argumentType = DecodeG1MethodInterfaceTypeSpec(tables,
                    context, artifactIndex, moduleRow, methodRow,
                    formalCount, argumentTypeSpec, depth + 1,
                    ref remainingVisits);
                arguments.Add(BoundInterfaceTypeArgument.ForType(
                    argumentType));
            }

            return new BoundInterfaceType(BoundInterfaceTypeKind.Nominal,
                PrimitiveType.Void, definition.QualifiedName, -1,
                arguments.MoveToImmutable(), false, false, false, false) {
                ModuleImport = definition.ModuleImport,
            };
        }

        throw Unsupported(
            "A G1 interface constraint uses an unsupported TypeSpec form.");
    }

    private static bool IsGenericMethodInitializationReferenceType(
        PrimitiveType type,
        IReadOnlyList<CheckedReferenceTypeDefinition>
            checkedReferenceTypes)
    {
        if (!CheckedReferenceTypeContract.IsValidType(type,
                checkedReferenceTypes))
        {
            return false;
        }

        var reference = checkedReferenceTypes[
            CheckedReferenceTypes.Index(type)];
        return reference.IsInitialization
            && !reference.IsGarbageCollected
            && !reference.IsOptional
            && !reference.IsOptionalValue;
    }

    private static bool TryCreateG1GenericMethodTypeTerm(
        PrimitiveType type, ImmutableArray<PrimitiveType> formalTypes,
        TrRmGenericMethodFormalImport[] formalRecords,
        int formalTypeOwnerOrdinal,
        IReadOnlyList<BoundSpanTypeDefinition> spanTypes,
        IReadOnlyList<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions,
        ImmutableArray<BoundGenericNominalTypeInstance>.Builder
            genericNominalTypeInstances,
        int depth, int formalOrdinal,
        out BoundGenericMethodTypeTerm term)
    {
        term = null!;
        if (depth > SemanticVerifier.MaximumDepth
            || formalTypes.IsDefault)
        {
            return false;
        }

        if (formalOrdinal >= 0)
        {
            if ((uint)formalOrdinal >= (uint)formalTypes.Length
                || formalTypes[formalOrdinal] != type)
            {
                return false;
            }

            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Formal,
                PrimitiveType.Void, formalOrdinal, -1, [],
                false, false, false, false);
            return true;
        }

        if (CheckedReferenceTypeContract.IsValidType(type,
                checkedReferenceTypes))
        {
            var reference = checkedReferenceTypes[
                CheckedReferenceTypes.Index(type)];
            if (reference.IsGarbageCollected
                || reference.IsOptional
                || reference.IsOptionalValue)
            {
                return false;
            }

            var referentFormalOrdinal = formalTypes.IndexOf(
                reference.ReferentType);
            if (!TryCreateG1GenericMethodTypeTerm(
                    reference.ReferentType, formalTypes, formalRecords,
                    formalTypeOwnerOrdinal, spanTypes,
                    checkedReferenceTypes, genericNominalTypeDefinitions,
                    genericNominalTypeInstances, depth + 1,
                    referentFormalOrdinal, out var referentTerm))
            {
                return false;
            }
            if (reference.IsReferentWritable)
            {
                if (referentTerm.Kind
                        != BoundGenericMethodTypeTermKind.Formal
                    || (uint)referentTerm.FormalOrdinal
                        >= (uint)formalRecords.Length
                    || formalRecords[referentTerm.FormalOrdinal] is not
                        TrRmGenericMethodFormalImport formal
                    || !formal.RequiresWritable)
                {
                    return false;
                }
            }

            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.CheckedReference,
                PrimitiveType.Void, -1, -1, [referentTerm],
                reference.IsWritable, reference.IsGarbageCollected,
                reference.IsInitialization, reference.IsOptional);
            return true;
        }

        if (BoundGenericNominalTypeTypes.IsType(type))
        {
            return TryGetGenericNominalTypeArgumentTerm(
                BoundGenericNominalTypeArgument.ForType(type),
                genericNominalTypeDefinitions,
                genericNominalTypeInstances, out term, depth + 1,
                formalTypes, formalTypeOwnerOrdinal);
        }

        if (!BoundSpanTypeTypes.IsValidType(type, spanTypes))
        {
            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Concrete,
                type, -1, -1, [], false, false, false, false) {
                AllowsVoid = type == PrimitiveType.Void,
            };
            return true;
        }

        var span = spanTypes[BoundSpanTypeTypes.Index(type)];
        BoundGenericMethodTypeTerm elementTerm;
        if (span.ElementFormalDefinitionOrdinal
            == BoundSpanTypeContract.ImportedFormalDefinitionOrdinal
                - formalTypeOwnerOrdinal)
        {
            var formalOrdinal = span.ElementFormalOrdinal;
            if ((uint)formalOrdinal >= (uint)formalTypes.Length
                || formalTypes[formalOrdinal] != span.ElementType
                || formalRecords[formalOrdinal]
                    is not TrRmGenericMethodFormalImport formal
                || (span.IsElementWritable
                    && !formal.RequiresWritable))
            {
                return false;
            }
            elementTerm = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Formal,
                PrimitiveType.Void, formalOrdinal, -1, [],
                false, false, false, false);
        }
        else if (span.ElementFormalDefinitionOrdinal == -1)
        {
            if (!TryCreateG1GenericMethodTypeTerm(span.ElementType,
                    formalTypes, formalRecords, formalTypeOwnerOrdinal,
                    spanTypes, checkedReferenceTypes,
                    genericNominalTypeDefinitions,
                    genericNominalTypeInstances, depth + 1, -1,
                    out elementTerm))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        if (span.IsElementWritable
            && elementTerm.Kind
                != BoundGenericMethodTypeTermKind.Formal)
        {
            return false;
        }

        term = new BoundGenericMethodTypeTerm(
            BoundGenericMethodTypeTermKind.Span,
            PrimitiveType.Void, -1, -1, [elementTerm],
            span.IsWritable, false, span.IsInitialization, false) {
            IsElementWritable = span.IsElementWritable,
        };
        return true;
    }

    private readonly record struct G1GenericTypeSpecResolution(
        PrimitiveType Type, int FormalOrdinal,
        bool IsWritableQualifier, bool IsInitializationQualifier);

    private static PrimitiveType ResolveGenericMethodTypeSpec(
        GenericMethodReadContext context,
        int artifactIndex,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int moduleRow,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmGenericMethodFormalImport[] formalRecords, int methodRow,
        int formalTypeOwnerOrdinal, uint typeSpecRef,
        ImmutableArray<PrimitiveType> formalTypes,
        bool allowFormalForwarding, int ordinal,
        out int formalOrdinal)
    {
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        var resolution = ResolveGenericMethodTypeSpecCore(context,
            artifactIndex, tables, moduleRow, typeSpecs, genericParams,
            formalRecords, methodRow,
            formalTypeOwnerOrdinal, typeSpecRef, formalTypes,
            allowFormalForwarding, ordinal, false, false,
            ref remaining, 0);
        if (resolution.IsWritableQualifier
            || resolution.IsInitializationQualifier)
        {
            throw Unsupported(
                "G1 generic MethodDef import supports qualified types only within the selected Span family.");
        }

        formalOrdinal = resolution.FormalOrdinal;
        return resolution.Type;
    }

    private static G1GenericTypeSpecResolution
        ResolveGenericMethodTypeSpecCore(
            GenericMethodReadContext context,
            int artifactIndex,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int moduleRow,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmGenericParamTableReader genericParams,
            TrRmGenericMethodFormalImport[] formalRecords, int methodRow,
            int formalTypeOwnerOrdinal, uint typeSpecRef,
            ImmutableArray<PrimitiveType> formalTypes,
            bool allowFormalForwarding, int ordinal,
            bool allowInitQualifier, bool isSpanElement,
            ref int remaining, int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth || --remaining < 0)
        {
            throw Unsupported(
                "The G1 generic TypeSpec exceeds the supported analysis limit.");
        }

        var reference = typeSpecs.GetTypeSpecRef(typeSpecRef,
            allowInitQualifier: allowInitQualifier);
        if ((reference.Qualifiers & ~0x07) != 0)
        {
            throw Unsupported(
                "G1 generic MethodDef import uses unsupported TypeSpec qualifiers.");
        }

        var isWritableQualifier = (reference.Qualifiers & 0x01) != 0;
        var isGarbageCollectedQualifier
            = (reference.Qualifiers & 0x02) != 0;
        var isInitializationQualifier
            = (reference.Qualifiers & 0x04) != 0;
        if (!reference.IsRow)
        {
            var inlineTypeIndex = Array.IndexOf(s_codes,
                reference.TypeCode);
            if (reference.Form == 0x01 && inlineTypeIndex >= 0)
            {
                if (isWritableQualifier
                    || isGarbageCollectedQualifier)
                {
                    throw Unsupported(
                        "A G1 scalar TypeSpec cannot carry write or GC qualifiers.");
                }

                return new G1GenericTypeSpecResolution(
                    s_types[inlineTypeIndex], -1, false,
                    isInitializationQualifier);
            }

            throw Unsupported(
                "G1 generic MethodDef import uses an unsupported inline TypeSpec.");
        }

        if (reference.Form == 0x05)
        {
            if (reference.Qualifiers != 0)
            {
                throw Unsupported(
                    "G1 generic MethodDef imports do not yet support qualified nominal TypeSpecs.");
            }

            var nominalType = context.ReadGenericNominalTypeSpec(
                tables, artifactIndex, moduleRow,
                typeSpecRef, methodRow, formalTypes, formalRecords);
            return new G1GenericTypeSpecResolution(nominalType, -1,
                false, false);
        }

        var row = typeSpecs.GetRow(reference.Row);
        if (reference.Form == 0x0C && row.Length == 7
            && reference.TypeCode == 0x8A)
        {
            if (isGarbageCollectedQualifier)
            {
                throw Unsupported(
                    "A G1 Span TypeSpec cannot carry GC backing.");
            }
            if (isInitializationQualifier && !allowInitQualifier)
            {
                throw Unsupported(
                    "A Span initialization qualifier is admitted only as a Span element type.");
            }

            var elementReference = BinaryPrimitives
                .ReadUInt32LittleEndian(row.Slice(3, sizeof(uint)));
            var element = ResolveGenericMethodTypeSpecCore(context,
                artifactIndex, tables, moduleRow, typeSpecs,
                genericParams, formalRecords, methodRow,
                formalTypeOwnerOrdinal, elementReference, formalTypes,
                allowFormalForwarding, ordinal, true, true,
                ref remaining, depth + 1);
            if (element.Type == PrimitiveType.Void)
            {
                throw Unsupported("A G1 Span element cannot be void.");
            }

            var isElementWritable = false;
            var elementFormalOrdinal = -1;
            if (element.FormalOrdinal >= 0)
            {
                if (element.IsWritableQualifier)
                {
                    var elementFormal = formalRecords[
                        element.FormalOrdinal];
                    if (elementFormal is null
                        || !elementFormal.RequiresWritable)
                    {
                        throw Unsupported(
                            "A G1 Span write element requires a writable generic formal.");
                    }
                    isElementWritable = true;
                }
                elementFormalOrdinal = element.FormalOrdinal;
            }
            else if (element.IsWritableQualifier)
            {
                throw Unsupported(
                    "A G1 Span write element must be a writable formal or a writable Span value.");
            }

            var spanType = context.GetSpanType(element.Type,
                formalTypeOwnerOrdinal, elementFormalOrdinal,
                isElementWritable, isWritableQualifier,
                element.IsInitializationQualifier);
            return new G1GenericTypeSpecResolution(spanType, -1, false,
                isInitializationQualifier);
        }

        if (reference.Form == 0x01 && row.Length == 3)
        {
            var typeIndex = Array.IndexOf(s_codes, reference.TypeCode);
            if (typeIndex >= 0)
            {
                if (isWritableQualifier
                    || isGarbageCollectedQualifier)
                {
                    throw Unsupported(
                        "A G1 scalar TypeSpec cannot carry write or GC qualifiers.");
                }

                return new G1GenericTypeSpecResolution(
                    s_types[typeIndex], -1, false,
                    isInitializationQualifier);
            }
        }
        else if ((reference.Form is 0x07 or 0x0E)
            && row.Length == 6)
        {
            var isInitializationReference = reference.Form == 0x0E;
            if (isInitializationQualifier)
            {
                throw Invalid(
                    "A G1 checked-reference TypeSpec has an invalid qualifier.");
            }
            if (isGarbageCollectedQualifier)
            {
                throw Unsupported(
                    "G1 generic MethodDef imports do not support GC-backed reference types.");
            }

            var referentTypeSpecRef = BinaryPrimitives
                .ReadUInt32LittleEndian(row.Slice(2, sizeof(uint)));
            var referent = ResolveGenericMethodTypeSpecCore(context,
                artifactIndex, tables, moduleRow, typeSpecs,
                genericParams, formalRecords, methodRow,
                formalTypeOwnerOrdinal, referentTypeSpecRef,
                formalTypes, allowFormalForwarding, ordinal, false, false,
                ref remaining, depth + 1);
            if (referent.IsInitializationQualifier)
            {
                throw Unsupported(
                    "A G1 checked-reference referent cannot carry an initialization qualifier.");
            }

            var referenceType = context.GetCheckedReferenceType(
                artifactIndex, methodRow, referent.Type,
                isWritable: isWritableQualifier,
                isGarbageCollected: false,
                isInitialization: isInitializationReference,
                isReferentWritable: referent.IsWritableQualifier);
            return new G1GenericTypeSpecResolution(referenceType, -1,
                false, false);
        }
        else if (reference.Form == 0x04 && row.Length == 6)
        {
            var formalRow = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(row.Slice(2, sizeof(uint))));
            var owner = genericParams.GetOwnerDefId(formalRow);
            var formal = checked((int)genericParams.GetOrdinal(formalRow));
            if ((owner >> 24) != 0x16
                || (owner & 0x00FF_FFFF) != (uint)methodRow
                || genericParams.GetFormalKind(formalRow) != 1
                || !(allowFormalForwarding
                    ? formal < ordinal
                    : formal < formalTypes.Length)
                || (uint)formal >= (uint)formalTypes.Length)
            {
                throw Invalid(
                    "A G1 generic MethodDef TypeSpec refers to a foreign or out-of-scope formal.");
            }

            if (isGarbageCollectedQualifier)
            {
                throw Unsupported(
                    "A G1 type formal cannot carry a GC qualifier.");
            }
            if (isWritableQualifier
                && ((uint)formal >= (uint)formalRecords.Length
                    || formalRecords[formal] is not
                        TrRmGenericMethodFormalImport formalRecord
                    || !formalRecord.RequiresWritable))
            {
                throw Unsupported(
                    "A G1 write-qualified formal requires its declared write property.");
            }

            if (!isSpanElement)
            {
                isWritableQualifier = false;
            }
            return new G1GenericTypeSpecResolution(
                formalTypes[formal], formal, isWritableQualifier,
                isInitializationQualifier);
        }

        throw Unsupported(
            "The selected generic MethodDef uses an unsupported TypeSpec form.");
    }
}
