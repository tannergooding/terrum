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
    private readonly record struct GenericMethodCallSiteResult(
        int ResultHome, PrimitiveType ResultType, BoundExpression Value);

    private readonly record struct GenericInterfaceImportRows(
        Dictionary<uint, int> DefinitionRows,
        Dictionary<uint, List<int>> MethodRows,
        Dictionary<uint, List<int>> AssociatedTypeRows);

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
        var inputIndex = new TrRmArtifactIndex(context.Artifacts[0],
            context.Schema);
        var tables = context.Schema.UsesForeignNativeInterface()
            ? TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(inputIndex)
            : TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                    inputIndex);
        var moduleRow = FindGenericMethodModule(tables.Modules,
            moduleIdentity);
        context.ForeignNativeInterfaceRootModule = (0, moduleRow);
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
        var genericMethodImports = imports.ToImmutable();
        var foreignNativeStructures = context.Schema
            .UsesForeignNativeInterface()
                ? context.ForeignNativeInterfaceStructures.ToImmutableArray()
                : ImmutableArray<BoundStruct>.Empty;
        return new CompilationReference(foreignNativeStructures, [],
            (BoundRuntimePolicy)runtimePolicy,
            interfaces: interfaces,
            enumDefinitions:
                [.. context.ForeignNativeInterfaceEnumDefinitions],
            genericMethodImports: genericMethodImports,
            retainedGenericMethodRoots: genericMethodImports,
            spanTypes: [.. context.SpanTypes],
            checkedReferenceTypes:
                [.. context.CheckedReferenceTypeDefinitions],
            nativePointerTypes: context.Schema
                .UsesForeignNativeInterface()
                    ? [.. context.ForeignNativeInterfacePointerTypes]
                    : [.. context.GenericNominalPointerTypes],
            nativeFunctionPointerSignatures: context.Schema
                .UsesForeignNativeInterface()
                    ? [.. context
                        .ForeignNativeInterfaceFunctionPointerSignatures]
                    : [],
            genericNominalTypeDefinitions:
                context.GenericNominalTypeDefinitions.ToImmutable(),
            genericNominalTypeInstances:
                context.GenericNominalTypeInstances.ToImmutable(),
            foreignNativeInterfaceSchemaIdentity:
                context.Schema.UsesForeignNativeInterface()
                    ? context.SchemaIdentity : null);
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

        if (context.Schemas[artifactIndex].UsesForeignNativeInterface())
        {
            if (!context.ForeignNativeInterfaceModulesBeingLoaded.Add(
                    cacheKey))
            {
                throw Invalid(
                    "The selected F+I provider graph contains a cycle.");
            }

            try
            {
                var foreignDefinitions
                    = CreateForeignNativeInterfaceDefinitions(tables,
                        context, artifactIndex, moduleRow);
                context.InterfaceDefinitions.Add(cacheKey,
                    foreignDefinitions);
                return foreignDefinitions;
            }
            finally
            {
                _ = context.ForeignNativeInterfaceModulesBeingLoaded.Remove(
                    cacheKey);
            }
        }

        if (!context.Schema.UsesGeneralMethodParameterLabels())
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

                var (firstMethod, methodCount)
                    = tables.Interfaces.GetMethodRange(interfaceDef);
                var (_, associatedTypeCount)
                    = tables.Interfaces.GetAssociatedTypeRange(interfaceDef);
                if (methodCount != 0 || associatedTypeCount != 0)
                {
                    throw Unsupported(
                        "A class generic MethodDef without parameter labels can retain only methodless, non-associated InterfaceDef constraints.");
                }
            }
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

        foreach (var (definitionId, importRow)
            in importRows.DefinitionRows)
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
        foreach (var (definitionId, importRowsForAssociatedType)
            in importRows.AssociatedTypeRows)
        {
            if ((definitionId >> 24) != 0x20)
            {
                throw Invalid(
                    "A G1L1 associated-type Import map has an invalid definition kind.");
            }

            foreach (var importRow in importRowsForAssociatedType)
            {
                if (!usedInterfaceImportRows.Contains(importRow))
                {
                    throw Invalid(
                        "A G1L1 associated-type Import expectation is unused by a retained declaration.");
                }
            }
        }
        foreach (var (definitionId, importRowsForMethod)
            in importRows.MethodRows)
        {
            if ((definitionId >> 24) != 0x16)
            {
                throw Invalid(
                    "A G1L1 InterfaceMethod Import map has an invalid definition kind.");
            }

            foreach (var importRow in importRowsForMethod)
            {
                if (!usedInterfaceImportRows.Contains(importRow))
                {
                    throw Invalid(
                        "A G1L1 InterfaceMethod Import expectation is unused by a retained declaration.");
                }
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
                var (firstAssociatedType, associatedTypeCount)
                    = tables.Interfaces.GetAssociatedTypeRange(interfaceDef);
                for (var associatedOrdinal = 0;
                    associatedOrdinal < associatedTypeCount;
                    associatedOrdinal++)
                {
                    _ = exportedInterfaceDefinitions.Add(
                        0x2000_0000u | checked((uint)(
                            firstAssociatedType + associatedOrdinal)));
                }
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
        var methodExportKeysByDef
            = new Dictionary<uint, List<string>>();
        var associatedTypeExportKeysByDef
            = new Dictionary<uint, List<string>>();
        foreach (var (definition, key)
            in tables.Exports.GetExportsForModule(moduleRow))
        {
            if (!exportedInterfaceDefinitions.Contains(definition))
            {
                continue;
            }

            if ((definition >> 24) == 0x20)
            {
                if (!associatedTypeExportKeysByDef.TryGetValue(definition,
                        out var keys))
                {
                    keys = [];
                    associatedTypeExportKeysByDef.Add(definition, keys);
                }

                keys.Add(key);
            }
            else if ((definition >> 24) == 0x16)
            {
                if (!methodExportKeysByDef.TryGetValue(definition,
                        out var keys))
                {
                    keys = [];
                    methodExportKeysByDef.Add(definition, keys);
                }

                keys.Add(key);
            }
            else if (!exportKeys.TryAdd(definition, key))
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
                = ImmutableArray.CreateBuilder<ImmutableArray<string>>(
                    methodCount);
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
                if (!methodExportKeysByDef.TryGetValue(methodId,
                        out var methodExportKeysForMember)
                    || methodExportKeysForMember.Count == 0)
                {
                    throw Invalid(
                        "A G1L1 InterfaceMethod has no MethodDef ExportKey.");
                }

                methodExportKeys.Add(
                    methodExportKeysForMember.ToImmutableArray());
            }

            var (firstAssociatedType, associatedTypeCount)
                = tables.Interfaces.GetAssociatedTypeRange(interfaceDef);
            var associatedTypeExportKeys
                = ImmutableArray.CreateBuilder<ImmutableArray<string>>(
                    associatedTypeCount);
            for (var associatedOrdinal = 0;
                associatedOrdinal < associatedTypeCount;
                associatedOrdinal++)
            {
                var associatedTypeDefId = 0x2000_0000u
                    | checked((uint)(firstAssociatedType
                        + associatedOrdinal));
                if (!associatedTypeExportKeysByDef.TryGetValue(
                        associatedTypeDefId, out var associatedTypeExportKeysForMember)
                    || associatedTypeExportKeysForMember.Count == 0)
                {
                    throw Invalid(
                        "A G1L1 AssociatedTypeDef has no ExportKey.");
                }

                associatedTypeExportKeys.Add(
                    associatedTypeExportKeysForMember.ToImmutableArray());
            }

            var definition = ReadG1InterfaceDefinition(tables, interfaceDef,
                moduleIdentity, typeExportKey, targetRequirement,
                runtimePolicy, methodExportKeys.MoveToImmutable(),
                associatedTypeExportKeys.MoveToImmutable(),
                context.Schema.UsesAssociatedRelationConstraints());
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

    private static GenericInterfaceImportRows
        CreateGenericInterfaceImportRows(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int moduleRow)
    {
        var interfaceImportDefinitions = new HashSet<uint>();
        var methodImportDefinitions = new HashSet<uint>();
        var associatedTypeImportDefinitions = new HashSet<uint>();
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
            if (!tables.Exports.HasExportForTypeDef(typeDef)
                && !tables.Imports.IsExpectedDefinition(typeDefId)
                && tables.Interfaces.GetMethodRange(interfaceDef).Count == 0
                && tables.Interfaces.GetAssociatedTypeRange(interfaceDef)
                    .Count == 0)
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
                    _ = methodImportDefinitions.Add(methodId);
                    if (methodImportDefinitions.Count
                        > SemanticVerifier.MaximumAnalysisVisits)
                    {
                        throw Unsupported(
                            "The G1L1 interface Import map exceeds the supported analysis limit.");
                    }
                }
            }

            var (firstAssociatedType, associatedTypeCount)
                = tables.Interfaces.GetAssociatedTypeRange(interfaceDef);
            for (var ordinal = 0; ordinal < associatedTypeCount; ordinal++)
            {
                var associatedTypeDefId = 0x2000_0000u
                    | checked((uint)(firstAssociatedType + ordinal));
                if (tables.Imports.IsExpectedDefinition(
                        associatedTypeDefId))
                {
                    _ = associatedTypeImportDefinitions.Add(
                        associatedTypeDefId);
                    if (associatedTypeImportDefinitions.Count
                        > SemanticVerifier.MaximumAnalysisVisits)
                    {
                        throw Unsupported(
                            "The G1L1 interface Import map exceeds the supported analysis limit.");
                    }
                }
            }
        }

        var definitionRows = new Dictionary<uint, int>();
        var methodRows = new Dictionary<uint, List<int>>();
        var associatedTypeRows = new Dictionary<uint, List<int>>();
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
            if (associatedTypeImportDefinitions.Contains(definitionId))
            {
                if (!associatedTypeRows.TryGetValue(definitionId,
                        out var rows))
                {
                    rows = [];
                    associatedTypeRows.Add(definitionId, rows);
                }

                rows.Add(importRow);
                continue;
            }

            if (methodImportDefinitions.Contains(definitionId))
            {
                if (!methodRows.TryGetValue(definitionId,
                        out var rows))
                {
                    rows = [];
                    methodRows.Add(definitionId, rows);
                }

                rows.Add(importRow);
                continue;
            }

            if (interfaceImportDefinitions.Contains(definitionId))
            {
                if (!definitionRows.TryAdd(definitionId, importRow))
                {
                    throw Invalid(
                        "A G1L1 interface Import expectation is ambiguous.");
                }
            }
        }

        return new GenericInterfaceImportRows(definitionRows,
            methodRows, associatedTypeRows);
    }

    private static BoundInterfaceDefinition
        BindExpectedGenericInterfaceDefinition(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            GenericMethodReadContext context, int moduleRow, int interfaceDef,
            GenericInterfaceImportRows importRows,
            HashSet<int> usedImportRows)
    {
        var typeDef = tables.Interfaces.GetOwnerTypeDef(interfaceDef);
        var typeDefId = 0x1300_0000u | checked((uint)typeDef);
        if (!importRows.DefinitionRows.TryGetValue(typeDefId,
                out var typeImportRow))
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
            foreach (var alias in member.AdditionalDeclarationKeys)
            {
                if (!providerMembersByKey.TryAdd(alias, member))
                {
                    throw Invalid(
                        "The selected G1L1 provider interface has conflicting member aliases.");
                }
            }
        }

        var (firstMethod, methodCount)
            = tables.Interfaces.GetMethodRange(interfaceDef);
        var methodExportKeys
            = ImmutableArray.CreateBuilder<ImmutableArray<string>>(
                methodCount);
        var methodKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var methodOrdinal = 0; methodOrdinal < methodCount;
            methodOrdinal++)
        {
            var interfaceMethodRow = firstMethod + methodOrdinal;
            var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                interfaceMethodRow);
            var methodId = 0x1600_0000u | checked((uint)methodRow);
            if (!importRows.MethodRows.TryGetValue(methodId,
                    out var methodImportRows)
                || methodImportRows.Count == 0)
            {
                throw Invalid(
                    "A G1L1 expected InterfaceMethod has no MethodDef Import row.");
            }
            var methodMemberExportKeys
                = ImmutableArray.CreateBuilder<string>(
                    methodImportRows.Count);
            BoundInterfaceMemberDefinition? selectedMethodMember = null;
            foreach (var methodImportRow in methodImportRows)
            {
                _ = usedImportRows.Add(methodImportRow);
                var methodModuleRef = tables.Imports
                    .GetModuleRefForImportRow(methodImportRow);
                var selectedMethod = tables.Imports
                    .GetBindingForImportRow(methodImportRow);
                if (tables.ModuleRefs.GetOwnerModule(methodModuleRef)
                        != moduleRow
                    || !StringComparer.Ordinal.Equals(
                        selectedMethod.ProducerIdentity,
                        selectedType.ProducerIdentity)
                    || selectedMethod.TargetRequirement
                        != selectedType.TargetRequirement
                    || selectedMethod.RuntimePolicy
                        != selectedType.RuntimePolicy
                    || !methodKeys.Add(selectedMethod.ExportKey)
                    || !providerMembersByKey.TryGetValue(
                        selectedMethod.ExportKey, out var providerMember)
                    || providerMember.Kind
                        != BoundInterfaceMemberKind.Callable
                    || providerMember.Ordinal != methodOrdinal
                    || (selectedMethodMember is not null
                        && selectedMethodMember.Ordinal
                            != providerMember.Ordinal))
                {
                    throw Invalid(
                        "A G1L1 requirement Import selects a different or ambiguous provider.");
                }

                selectedMethodMember = providerMember;
                methodMemberExportKeys.Add(selectedMethod.ExportKey);
            }

            methodExportKeys.Add(methodMemberExportKeys.MoveToImmutable());
        }

        var (firstAssociatedType, associatedTypeCount)
            = tables.Interfaces.GetAssociatedTypeRange(interfaceDef);
        var associatedTypeExportKeys
            = ImmutableArray.CreateBuilder<ImmutableArray<string>>(
                associatedTypeCount);
        for (var associatedOrdinal = 0;
            associatedOrdinal < associatedTypeCount; associatedOrdinal++)
        {
            var associatedTypeDefRow
                = firstAssociatedType + associatedOrdinal;
            var associatedTypeDefId = 0x2000_0000u
                | checked((uint)associatedTypeDefRow);
            if (!importRows.AssociatedTypeRows.TryGetValue(
                    associatedTypeDefId, out var associatedImportRows)
                || associatedImportRows.Count == 0
                || tables.AssociatedTypeDefs.GetOwnerInterfaceDef(
                    associatedTypeDefRow) != interfaceDef)
            {
                throw Invalid(
                    "A G1L1 expected interface associated type has no exact owner-scoped Import.");
            }

            var associatedExportKeys
                = ImmutableArray.CreateBuilder<string>(
                    associatedImportRows.Count);
            BoundInterfaceMemberDefinition? selectedAssociatedType = null;
            foreach (var associatedImportRow in associatedImportRows)
            {
                _ = usedImportRows.Add(associatedImportRow);
                var associatedModuleRef = tables.Imports
                    .GetModuleRefForImportRow(associatedImportRow);
                var selectedAssociatedExport = tables.Imports
                    .GetBindingForImportRow(associatedImportRow);
                if (tables.ModuleRefs.GetOwnerModule(associatedModuleRef)
                        != moduleRow
                    || !StringComparer.Ordinal.Equals(
                        selectedAssociatedExport.ProducerIdentity,
                        selectedType.ProducerIdentity)
                    || selectedAssociatedExport.TargetRequirement
                        != selectedType.TargetRequirement
                    || selectedAssociatedExport.RuntimePolicy
                        != selectedType.RuntimePolicy
                    || !providerMembersByKey.TryGetValue(
                        selectedAssociatedExport.ExportKey,
                        out var providerAssociatedType)
                    || providerAssociatedType.Kind
                        != BoundInterfaceMemberKind.AssociatedType
                    || providerAssociatedType.Ordinal
                        != methodCount + associatedOrdinal
                    || (selectedAssociatedType is not null
                        && selectedAssociatedType.Ordinal
                            != providerAssociatedType.Ordinal))
                {
                    throw Invalid(
                        "A G1L1 associated-type Import does not bind through its selected interface provider.");
                }

                selectedAssociatedType = providerAssociatedType;
                associatedExportKeys.Add(
                    selectedAssociatedExport.ExportKey);
            }

            associatedTypeExportKeys.Add(
                associatedExportKeys.MoveToImmutable());
        }

        var expected = ReadG1InterfaceDefinition(tables, interfaceDef,
            selectedType.ProducerIdentity, selectedType.ExportKey,
            selectedType.TargetRequirement, selectedType.RuntimePolicy,
            methodExportKeys.MoveToImmutable(),
            associatedTypeExportKeys.MoveToImmutable(),
            context.Schema.UsesAssociatedRelationConstraints());
        ValidateG1InterfaceProviderContract(expected, providerInterface,
            providerMembersByKey, context.Schema);
        return expected;
    }

    private static BoundInterfaceDefinition ReadG1InterfaceDefinition(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int interfaceDef, string moduleIdentity, string typeExportKey,
        uint targetRequirement, uint runtimePolicy,
        ImmutableArray<ImmutableArray<string>> methodExportKeys,
        ImmutableArray<ImmutableArray<string>> associatedTypeExportKeys,
        bool supportsAssociatedTypes)
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
            || baseCount != 0
            || (associatedTypeCount != 0 && !supportsAssociatedTypes)
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
                    || tables.Constraints.GetPayload(constraintRow) != 2
                    || !context.Schema.UsesGenericWriteRequirements())
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
        if (associatedTypeExportKeys.Length != associatedTypeCount)
        {
            throw Invalid(
                "A G1L1 InterfaceDef does not have one selected key per direct associated type.");
        }

        var members = ImmutableArray.CreateBuilder<
            BoundInterfaceMemberDefinition>(
                methodCount + associatedTypeCount);
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
                    tables.Parameters.GetTypeSpec(parameterRow));
                BoundConstant? defaultValue = null;
                if (context.Schema.UsesClassParameterDefaults())
                {
                    if (!tables.MethodParameterLabels
                        .TryGetDefaultConstantSourceRow(methodRow,
                            checked((uint)parameterOrdinal),
                            out var constantSourceRow))
                    {
                        throw Invalid(
                            "A validated CM1 InterfaceMethod has no default surface.");
                    }

                    if (constantSourceRow != 0)
                    {
                        var ownerModule
                            = tables.Methods.GetOwnerModule(methodRow);
                        var (targetRequirement, _)
                            = tables.Modules.GetRequirements(ownerModule);
                        defaultValue
                            = TrRmConstantSourceValueValidator
                                .MaterializeFrozenScalar(
                                    checked((int)constantSourceRow),
                                    tables.Parameters.GetTypeSpec(
                                        parameterRow),
                                    ownerModule, targetRequirement,
                                    tables.ConstantSources,
                                    tables.FrozenValueNodes, tables.Blobs,
                                    tables.TypeSpecs, tables.Features,
                                    tables.Modules,
                                    tables.A3LayoutResourceLedger);
                        if (parameterType.Kind
                                != BoundInterfaceTypeKind.Primitive
                            || defaultValue.Type
                                != parameterType.PrimitiveType)
                        {
                            throw Invalid(
                                "A CM1 interface default does not match its checked parameter type.");
                        }
                    }
                }

                parameters.Add(new BoundInterfaceParameter(
                    tables.Strings.DecodeName(labelStringRow), parameterType,
                    BoundInterfaceParameterMode.Value, [], []) {
                    DefaultValue = defaultValue,
                });
            }

            var resultType = DecodeG1InterfaceTypeSpec(tables, typeDef,
                formalCount,
                tables.Signatures.GetResultTypeSpec(signatureRow));
            var methodKeys = methodExportKeys[methodOrdinal];
            if (methodKeys.IsDefaultOrEmpty)
            {
                throw Invalid(
                    "A G1L1 InterfaceMethod has no retained ExportKey.");
            }
            members.Add(new BoundInterfaceMemberDefinition(methodOrdinal,
                tables.Methods.GetName(methodRow),
                methodKeys[0],
                BoundInterfaceMemberKind.Callable,
                BoundInterfaceAccessibility.Public, false, false, false,
                false, false, false, false, [], [], [], default, resultType,
                [], [], parameters.MoveToImmutable(), [], [], null, null,
                null) {
                RequiresMutableReceiver = (methodFlags & 4) != 0,
                AdditionalDeclarationKeys = methodKeys.RemoveAt(0),
            });
        }

        for (var associatedOrdinal = 0;
            associatedOrdinal < associatedTypeCount;
            associatedOrdinal++)
        {
            var associatedTypeDefRow
                = firstAssociatedType + associatedOrdinal;
            if (tables.AssociatedTypeDefs.GetOwnerInterfaceDef(
                    associatedTypeDefRow) != interfaceDef)
            {
                throw Invalid(
                    "A G1L1 AssociatedTypeDef range has an invalid owner.");
            }

            var defaultTypeSpecRef = tables.AssociatedTypeDefs
                .GetDefaultTypeSpecRef(associatedTypeDefRow);
            var defaultType = defaultTypeSpecRef == 0 ? null
                : DecodeG1InterfaceTypeSpec(tables, typeDef,
                    formalCount, defaultTypeSpecRef);
            var associatedTypeKeys
                = associatedTypeExportKeys[associatedOrdinal];
            if (associatedTypeKeys.IsDefaultOrEmpty)
            {
                throw Invalid(
                    "A G1L1 AssociatedTypeDef has no retained ExportKey.");
            }
            members.Add(new BoundInterfaceMemberDefinition(
                members.Count,
                tables.Strings.DecodeName(
                    tables.AssociatedTypeDefs.GetNameStringRow(
                        associatedTypeDefRow)),
                associatedTypeKeys[0],
                BoundInterfaceMemberKind.AssociatedType,
                BoundInterfaceAccessibility.Public, false, false, false,
                false, false, false, false, [], [], [], default, null,
                [], [], [], [], [], defaultType, null, null) {
                AdditionalDeclarationKeys =
                    associatedTypeKeys.RemoveAt(0),
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
            providerMembersByKey, TrRmArtifactSchema schema)
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
                || (expectedMember.Kind
                        == BoundInterfaceMemberKind.AssociatedType
                    && !StringComparer.Ordinal.Equals(
                        expectedMember.Name, providerMember.Name))
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
                || (expectedMember.Kind == BoundInterfaceMemberKind.AssociatedType
                    && (!schema.UsesAssociatedRelationConstraints()
                        || !AreEquivalentG1InterfaceTypes(
                            expectedMember.AssociatedDefaultType,
                            providerMember.AssociatedDefaultType)))
                || (expectedMember.Kind
                        != BoundInterfaceMemberKind.AssociatedType
                    && (expectedMember.AssociatedDefaultType is not null
                        || providerMember.AssociatedDefaultType is not null))
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

            if (expectedMember.AdditionalDeclarationKeys.Any(
                    aliasKey =>
                        !providerMembersByKey.TryGetValue(aliasKey,
                            out var aliasMember)
                        || aliasMember.Ordinal != providerMember.Ordinal
                        || aliasMember.Kind != providerMember.Kind))
            {
                throw Invalid(
                    "The selected G1L1 interface provider retargets a retained member alias.");
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
        int ownerTypeDef, int formalCount, uint typeSpecRef)
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
            int parameterCount, bool hasReceiver)
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
                || dependency.Relationship is not (1 or 2 or 3))
            {
                throw Unsupported(
                    "G1 generic MethodDefs support only ordinary-result formal dependency sources.");
            }

            var source = dependency.SourceKind switch {
                1 when dependency.SourceOrdinal < (uint)parameterCount
                    => new BoundDependencySource(
                        BoundDependencySourceKind.Formal,
                        checked((int)dependency.SourceOrdinal)),
                2 when hasReceiver && dependency.SourceOrdinal == 0
                    => new BoundDependencySource(
                        BoundDependencySourceKind.Receiver, null),
                _ => throw Invalid(
                    "A G1 generic MethodDef has an invalid result Dependency source."),
            };
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
        var ownerTypeDefRow = tables.Methods.GetOwnerType(methodRow);
        var signatureRow = tables.Methods.GetSignature(methodRow);
        var receiverTypeSpec
            = tables.Signatures.GetReceiverTypeSpec(signatureRow);
        var methodFlags = tables.Methods.GetFlags(methodRow);
        var isSupportedClassReceiver = receiverTypeSpec == 0;
        if (ownerTypeDefRow != 0 && receiverTypeSpec != 0)
        {
            var receiver = tables.TypeSpecs.GetTypeSpecRef(
                receiverTypeSpec);
            isSupportedClassReceiver = receiver.IsRow
                && receiver.Form is (0x02 or 0x03)
                && receiver.Qualifiers == 0
                && tables.TypeSpecs.GetNominalTypeDefRow(
                    receiverTypeSpec, tables.TypeDefs, tables.Imports)
                    == ownerTypeDefRow;
        }
        var isSupportedClassMethod
            = ownerTypeDefRow != 0
                && (StandardGenericMethodSchema
                        .IsCM1ClassParameterDefaults(
                            context.SchemaIdentity)
                    || StandardGenericMethodSchema.IsClassTypeBindingViews(
                        context.SchemaIdentity))
                && tables.TypeDefs.GetKind(ownerTypeDefRow) == 2
                && (tables.TypeDefs.GetFlags(ownerTypeDefRow) & 3) == 2
                && tables.TypeDefs.GetGenericRange(ownerTypeDefRow).Count == 0
                && tables.TypeDefs.GetContractRange(ownerTypeDefRow).Count
                    == 0
                && (methodFlags & 3) == 2
                && (methodFlags & 4) == 0
                && isSupportedClassReceiver;
        var formalTypeOwnerOrdinal
            = context.GetFormalTypeOwnerOrdinal(artifactIndex, methodRow);
        var supportedMethodVisibility
            = ownerTypeDefRow != 0
                ? isSupportedClassMethod
                : isMethodDefTarget
                    ? methodFlags is 0 or 2
                    : methodFlags == 2;
        if (tables.Methods.GetOwnerModule(methodRow) != moduleRow
            || !supportedMethodVisibility)
        {
            throw Unsupported(
                isMethodDefTarget
                    ? "GenericCall targets currently require module-level or supported readonly class MethodDefs."
                    : "Generic MethodDef imports currently require a public module-level or supported readonly class declaration.");
        }

        var (formalFirst, formalCount)
            = tables.Signatures.GetGenericRange(signatureRow);
        if (formalCount is < 1 or > 8)
        {
            throw Invalid("The selected G1 MethodDef has no supported type-formal range.");
        }

        var placeholderTypes = ImmutableArray.CreateBuilder<PrimitiveType>(
            formalCount);
        var formalRecords = new TrRmGenericMethodFormalImport[formalCount];
        var pendingAssociatedRelations
            = new List<(int FormalOrdinal, int ConstraintRow)>();
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
            var requiresUnique = false;
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
                        if (!context.Schema.UsesGeneralMethodParameterLabels()
                            && !(ownerTypeDefRow != 0
                                && StandardGenericMethodSchema
                                    .IsClassTypeBindingViews(
                                        context.SchemaIdentity)))
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
                        if (constraintPayload == 1 && !requiresUnique)
                        {
                            requiresUnique = true;
                            break;
                        }
                        throw Invalid(
                            "The selected generic MethodDef has duplicate or unsupported property constraints.");

                    case 3:
                        if (!context.Schema.UsesAssociatedRelationConstraints()
                            || !isSupportedClassMethod)
                        {
                            throw Unsupported(
                                "Associated-relation constraints require the exact CM1AR1 class MethodDef profile.");
                        }

                        pendingAssociatedRelations.Add((ordinal,
                            constraintRow));
                        break;

                    default:
                        throw Invalid(
                            "The selected generic MethodDef has an unsupported Constraint kind.");
                }
            }

            if (requiresUnique)
            {
                if (ownershipDomain == 1)
                {
                    throw Invalid(
                        "A Unique constraint conflicts with the formal's non-unique-only domain.");
                }

                ownershipDomain = 2;
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
        if (pendingAssociatedRelations.Count != 0)
        {
            var associatedRelations
                = Enumerable.Range(0, formalCount)
                    .Select(static _ => ImmutableArray.CreateBuilder<
                        BoundGenericMethodAssociatedRelation>())
                    .ToArray();
            var relationVisits = SemanticVerifier.MaximumAnalysisVisits;
            foreach (var (formalOrdinal, constraintRow)
                in pendingAssociatedRelations)
            {
                var relation = DecodeG1MethodAssociatedRelation(tables,
                    context, artifactIndex, moduleRow, methodRow,
                    formalCount, constraintRow, formalTypes, formalRecords,
                    formalTypeOwnerOrdinal, ref relationVisits);
                if (associatedRelations[formalOrdinal].Any(existing =>
                        BoundInterfaceContract
                            .AreEquivalentAssociatedTypeIdentities(
                                existing.AssociatedType,
                                relation.AssociatedType)
                        && BoundGenericMethodTypeTerms.AreEquivalent(
                            existing.RequiredType, relation.RequiredType)))
                {
                    throw Invalid(
                        "A G1 GenericParam repeats an associated identity Constraint.");
                }
                associatedRelations[formalOrdinal].Add(relation);
            }

            for (var ordinal = 0; ordinal < formalCount; ordinal++)
            {
                formalRecords[ordinal] = formalRecords[ordinal] with {
                    AssociatedRelations =
                        associatedRelations[ordinal].ToImmutable(),
                };
            }
        }

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
                    AssociatedRelations = formal.AssociatedRelations,
                    DefaultTypeParameterDependencies = dependencies,
                });
            formals.Add(new BoundGenericMethodFormal(ordinal,
                formal.Name, formal.PlaceholderType,
                formal.OwnershipDomain, formal.NullabilityDomain, []) {
                TypeDefault = formal.TypeDefault,
                RequiresWritable = formal.RequiresWritable,
                RequiresBlittable = formal.RequiresBlittable,
                InterfaceConstraints = formal.InterfaceConstraints,
                AssociatedRelations = formal.AssociatedRelations,
            });
            formalOrder.Add(new BoundGenericMethodFormalSlot(ordinal,
                BoundGenericMethodFormalKind.Type, ordinal, -1));
        }

        var (parameterFirst, parameterCount)
            = tables.Signatures.GetParamRange(signatureRow);
        if (parameterCount > 8)
        {
            throw Unsupported(
                "G1 generic MethodDef imports currently support at most eight value parameters.");
        }
        var hasReceiver = receiverTypeSpec != 0;
        var receiverCount = hasReceiver ? 1 : 0;
        var variables = ImmutableArray.CreateBuilder<BoundVariable>(
            parameterCount + receiverCount);
        if (hasReceiver)
        {
            var receiverType = ResolveGenericMethodTypeSpec(
                context, artifactIndex, tables, moduleRow,
                tables.TypeSpecs, tables.GenericParams,
                formalRecords, methodRow, formalTypeOwnerOrdinal,
                receiverTypeSpec, formalTypes,
                allowFormalForwarding: false, ordinal: formalCount, out _);
            variables.Add(new BoundVariable("this", receiverType, false,
                default));
        }
        var ordinaryParameterDefaults = ImmutableArray.CreateBuilder<
            BoundGenericMethodParameterDefault?>(parameterCount);
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
            var retainsLabel = context.Schema.UsesGeneralMethodParameterLabels()
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
            BoundGenericMethodParameterDefault? parameterDefault = null;
            if (context.Schema.UsesClassParameterDefaults()
                && retainsLabel)
            {
                if (!tables.MethodParameterLabels
                    .TryGetDefaultConstantSourceRow(methodRow,
                        checked((uint)ordinal),
                        out var constantSourceRow))
                {
                    throw Invalid(
                        "A validated CM1 MethodDef has no complete default vector.");
                }

                if (constantSourceRow != 0)
                {
                    var ownerModule
                        = tables.Methods.GetOwnerModule(methodRow);
                    var (targetRequirement, _)
                        = tables.Modules.GetRequirements(ownerModule);
                    var value
                        = TrRmConstantSourceValueValidator
                            .MaterializeFrozenScalar(
                                checked((int)constantSourceRow),
                                tables.Parameters.GetTypeSpec(paramRow),
                                ownerModule, targetRequirement,
                                tables.ConstantSources,
                                tables.FrozenValueNodes, tables.Blobs,
                                tables.TypeSpecs, tables.Features,
                                tables.Modules,
                                tables.A3LayoutResourceLedger);
                    if (value.Type != parameterType)
                    {
                        throw Invalid(
                            "A CM1 generic default does not match its checked parameter type.");
                    }

                    parameterDefault
                        = new BoundGenericMethodParameterDefault(
                            ordinal, parameterName, parameterType, value,
                            []);
                }
            }

            ordinaryParameterDefaults.Add(parameterDefault);
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
                parameterCount, hasReceiver);

        var proofVariables = variables.ToImmutable().ToBuilder();
        var bodyBytes = bodyRow == 0 ? default : tables.Bodies.GetRow(bodyRow);
        if (hasReceiver && decodeBody
            && bodyBytes[0] != 0x01)
        {
            throw Unsupported(
                "The bounded class generic MethodDef reader requires a register Body.");
        }
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
        var typeNamespace = ownerTypeDefRow == 0 ? string.Empty
            : tables.TypeDefs.GetNamespace(ownerTypeDefRow);
        var declaringTypeName = ownerTypeDefRow == 0 ? string.Empty
            : tables.TypeDefs.GetName(ownerTypeDefRow);
        var declaringStruct = ownerTypeDefRow == 0 ? null
            : typeNamespace.Length == 0 ? declaringTypeName
                : typeNamespace + "." + declaringTypeName;
        var proofFunction = new BoundFunction(methodName, returnType,
            proofVariables.ToImmutable(), parameterCount + receiverCount,
            new BoundBlock(statements, default), default) {
            IsExternal = !decodeBody,
            IsStatic = !hasReceiver,
            IsMutable = (methodFlags & 4) != 0,
            DeclaringStruct = declaringStruct,
            RequiresUnsafeCaller = (effects & 4) != 0,
            DoesNotReturn = (effects & 2) != 0,
            DependencyContracts = dependencyContracts,
        };
        var methodSignature = new BoundGenericMethodSignature(0,
            methodName, isPublic, proofFunction.RequiresUnsafeCaller,
            formalSignatures.MoveToImmutable()) {
            IsStatic = !hasReceiver,
            IsMutable = (methodFlags & 4) != 0,
            FormalOrder = formalOrder.MoveToImmutable(),
        };
        var signature = new BoundGenericMethodSignature(0,
            isMethodDefTarget ? methodName : exportKey, isPublic,
            proofFunction.RequiresUnsafeCaller,
            methodSignature.Formals) {
            IsStatic = !hasReceiver,
            IsMutable = (methodFlags & 4) != 0,
            FormalOrder = methodSignature.FormalOrder,
        };
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(moduleRow);
        var origin = new BoundGenericMethodImportOrigin(
            tables.Modules.GetIdentity(moduleRow), exportKey,
            context.SchemaIdentity,
            targetRequirement,
            runtimePolicy, context.Digests[artifactIndex], definitionId,
            isMethodDefTarget) {
            OwnerModule
                = TrRmExecutableCompiler.CreateA2SourceTypeProviderSnapshot(
                    tables, context.Artifacts[artifactIndex],
                    context.SchemaIdentity, moduleRow),
        };
        var import = new BoundGenericMethodImport(origin, signature,
            formals.MoveToImmutable(), proofFunction,
            formalTypeOwnerOrdinal, context.SpanTypes,
            ordinaryParameterDefaults.MoveToImmutable());
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
        var callerSignature = tables.Methods.GetSignature(callerMethodRow);
        var receiverOffset
            = tables.Signatures.GetReceiverTypeSpec(callerSignature) == 0
                ? 0 : 1;
        var (firstParameter, callerParameterCount)
            = tables.Signatures.GetParamRange(callerSignature);
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
            var variableOrdinal = checked(parameterOrdinal
                + receiverOffset);
            if (home[0] != 2
                || parameterOrdinal >= callerParameterCount
                || (uint)variableOrdinal >= (uint)callerVariables.Count
                || callerVariables[variableOrdinal].Type
                    != callerReturnType)
            {
                throw Invalid(
                    "The open move-return Body does not select its exact result parameter.");
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
                ? new BoundMove(variableOrdinal, callerReturnType, default)
                : new BoundRead(variableOrdinal, callerReturnType, default);
            return [new BoundReturn(returned, default)];
        }

        if (body.BlockCount == 4 && body.RecordCount == 4)
        {
            return DecodeGenericMethodConditionalUnitCallBody(tables,
                context, artifactIndex, moduleRow, callerMethodRow,
                callerFormals, callerFormalRecords, callerReturnType,
                callerVariables, body);
        }

        if (body.BlockCount == 1 && body.RecordCount > 3)
        {
            return DecodeGenericMethodCallChainBody(tables, context,
                artifactIndex, moduleRow, callerMethodRow, callerFormals,
                callerFormalRecords, callerReturnType, callerVariables,
                body, code);
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
                "The G1 consumer supports only a no-return GenericCall, a normal unit GenericCall, or a non-unit GenericCall followed by ReturnHome.");
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

        var (_, methodParameterCount)
            = tables.Signatures.GetParamRange(targetSignature);
        var targetReceiverCount
            = targetImport.Signature.IsStatic ? 0 : 1;
        var targetParameterCount
            = methodParameterCount + targetReceiverCount;
        var inputCount = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(callSitePayload.Slice(8, 4)));
        if (targetParameterCount != inputCount
            || targetImport.ProofFunction.ParameterCount
                != targetParameterCount)
        {
            throw Invalid(
                "The GenericCall input count differs from its selected MethodSig.");
        }

        var arguments = ImmutableArray.CreateBuilder<BoundExpression>(
            targetParameterCount);
        for (var ordinal = 0; ordinal < targetParameterCount; ordinal++)
        {
            var inputOffset = 16 + (ordinal * 4);
            var selectorKind = callSitePayload[inputOffset];
            var sourceUse = callSitePayload[inputOffset + 1];
            var selectorIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                callSitePayload.Slice(inputOffset + 2, 2));
            var targetParameterType = targetImport.ProofFunction
                .Variables[ordinal].Type;
            if (!TrySubstituteGenericMethodType(
                    targetParameterType, targetFormalTypes, actualTypes,
                    actualTypeMetadata, targetImport, context,
                    artifactIndex, callerMethodRow, callerFormalTypes,
                    callerFormalTypeOwnerOrdinal, 0,
                    out targetParameterType))
            {
                throw Unsupported(
                    "The G1 GenericCall parameter type uses an unsupported substitution term.");
            }

            if (targetParameterType == PrimitiveType.Void)
            {
                if (selectorKind != 0 || sourceUse != 0 || selectorIndex != 0)
                {
                    throw Invalid(
                        "A unit GenericCall input must use the exact None selector.");
                }

                arguments.Add(new BoundConstant(0, PrimitiveType.Void,
                    default));
                continue;
            }

            if (selectorKind != 2 || sourceUse is not (1 or 2 or 5))
            {
                throw Unsupported(
                    "The G1 consumer currently supports copy or ownership-transfer parameter-Home GenericCall inputs only.");
            }

            var homeIndex = selectorIndex;
            if ((uint)homeIndex >= (uint)body.HomeCount)
            {
                throw Invalid(
                    "A GenericCall input references an invalid Home.");
            }

            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            var parameterOrdinal = BinaryPrimitives
                .ReadUInt16LittleEndian(home.Slice(4, 2));
            var variableOrdinal = checked(parameterOrdinal
                + receiverOffset);
            if (home[0] != 2
                || parameterOrdinal >= callerParameterCount
                || (uint)variableOrdinal >= (uint)callerVariables.Count
                || (ordinal == 0 && targetReceiverCount != 0
                    && sourceUse != 1))
            {
                throw Invalid(
                    "A GenericCall input does not select a caller parameter Home.");
            }

            var descriptorRead = new BoundRead(variableOrdinal,
                callerVariables[variableOrdinal].Type, default);
            if (sourceUse == 5
                && descriptorRead.Type != targetParameterType)
            {
                throw Invalid(
                    "A GenericCall reborrow changes the initialization-reference type.");
            }
            BoundExpression argument = sourceUse switch {
                1 => descriptorRead,
                2 => new BoundMove(variableOrdinal,
                    callerVariables[variableOrdinal].Type, default),
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
                    targetImport.ProofFunction,
                    ordinal - targetReceiverCount))
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
        var returnsUnit = !targetDoesNotReturn
            && resultType == PrimitiveType.Void;
        if (resultType != callerReturnType
            || (targetDoesNotReturn
                ? callSite[5] != 0 || resultHome != 0 || hasReturnHome
                : returnsUnit
                    ? callSite[5] != 0 || resultHome != 0 || hasReturnHome
                    : callSite[5] != 2 || !hasReturnHome
                        || resultHome
                            != code[callSiteInstructionLength + 1])
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
        return returnsUnit
            ? [
                new BoundExpressionStatement(value, default),
                new BoundReturn(null, default),
            ]
            : [new BoundReturn(value, default)];
    }

    private static ImmutableArray<BoundStatement>
        DecodeGenericMethodConditionalUnitCallBody(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            GenericMethodReadContext context, int artifactIndex, int moduleRow,
            int callerMethodRow, ImmutableArray<PrimitiveType> callerFormals,
            TrRmGenericMethodFormalImport[] callerFormalRecords,
            PrimitiveType callerReturnType,
            ImmutableArray<BoundVariable>.Builder callerVariables,
            TrRmRegisterBodyReader body)
    {
        var conditionCode = body.GetBlockCode(0);
        var callCode = body.GetBlockCode(1);
        var falseCode = body.GetBlockCode(2);
        var returnCode = body.GetBlockCode(3);
        if (callerReturnType != PrimitiveType.Void
            || body.RecordCount != 4
            || conditionCode.Length != 8 || conditionCode[0] != 0xD5
            || conditionCode[1] != 0 || conditionCode[4] != 0xE4
            || conditionCode[5] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(
                conditionCode.Slice(6, 2)) != 3
            || callCode.Length != 6 || callCode[0] != 0x90
            || callCode[1] != 2 || callCode[2] != 0xE3
            || callCode[3] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(
                callCode.Slice(4, 2)) != 3
            || !falseCode.SequenceEqual(
                new byte[] { 0xE3, 0, 3, 0 })
            || !returnCode.SequenceEqual(new byte[] { 0x18, 0 })
            || body.GetRecord(0)[0] != 0x01
            || body.GetRecord(1)[0] != 0x10)
        {
            throw Unsupported(
                "The G1 consumer requires a one-branch, unit-result GenericCall Body.");
        }

        var edge = body.GetRecord(3);
        if (edge.Length != 8 || edge[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(edge.Slice(4, 2))
                != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(edge.Slice(6, 2))
                != 2)
        {
            throw Invalid(
                "The conditional GenericCall Body has an invalid true/false edge.");
        }

        var conditionHome = BinaryPrimitives.ReadUInt16LittleEndian(
            conditionCode.Slice(2, 2));
        if (conditionHome >= body.HomeCount)
        {
            throw Invalid(
                "The conditional GenericCall Body references an invalid predicate Home.");
        }

        var conditionHomeEntry = body.HomeMap.Slice(conditionHome * 8, 8);
        var conditionVariable = BinaryPrimitives
            .ReadUInt16LittleEndian(conditionHomeEntry.Slice(4, 2));
        var callerSignature = tables.Methods.GetSignature(callerMethodRow);
        var (firstParameter, parameterCount)
            = tables.Signatures.GetParamRange(callerSignature);
        if (parameterCount != callerVariables.Count
            || conditionHomeEntry[0] != 2 || conditionHomeEntry[1] != 0
            || conditionHomeEntry[6] != 0 || conditionHomeEntry[7] != 0
            || conditionVariable >= parameterCount
            || conditionVariable >= callerVariables.Count
            || callerVariables[conditionVariable].Type
                != PrimitiveType.Bool)
        {
            throw Invalid(
                "The conditional GenericCall Home does not select a Boolean MethodSig parameter.");
        }

        var expectedHome = 0;
        var expectedConditionHome = -1;
        for (var parameterOrdinal = 0;
            parameterOrdinal < parameterCount; parameterOrdinal++)
        {
            if (callerVariables[parameterOrdinal].Type
                == PrimitiveType.Void)
            {
                continue;
            }

            if (expectedHome >= body.HomeCount)
            {
                throw Invalid(
                    "The conditional GenericCall Body omits a MethodSig parameter Home.");
            }

            var homeEntry = body.HomeMap.Slice(expectedHome * 8, 8);
            var typeMapIndex = BinaryPrimitives
                .ReadUInt16LittleEndian(homeEntry.Slice(2, 2));
            if (homeEntry[0] != 2 || homeEntry[1] != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    homeEntry.Slice(4, 2)) != parameterOrdinal
                || homeEntry[6] != 0 || homeEntry[7] != 0
                || typeMapIndex >= body.TypeCount)
            {
                throw Invalid(
                    "A conditional GenericCall Home does not select its MethodSig parameter.");
            }

            var parameterTypeSpec = tables.Parameters.GetTypeSpec(
                firstParameter + parameterOrdinal);
            var parameterTypeSpecRef = tables.TypeSpecs
                .GetTypeSpecRef(parameterTypeSpec);
            var typeMapToken = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap.Slice(typeMapIndex * sizeof(uint),
                    sizeof(uint)));
            if (typeMapToken
                != CreateStandardBodyTypeMapToken(parameterTypeSpecRef))
            {
                throw Invalid(
                    "A conditional GenericCall Home type differs from its MethodSig parameter.");
            }

            if (parameterOrdinal == conditionVariable)
            {
                expectedConditionHome = expectedHome;
            }
            expectedHome++;
        }

        if (expectedHome != body.HomeCount
            || expectedConditionHome != conditionHome)
        {
            throw Invalid(
                "The conditional GenericCall Body has an incomplete or mismatched HomeMap.");
        }

        var callSite = body.GetRecord(2);
        if (callSite[0] != 0x02 || callSite[4] != 4 || callSite[5] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(
                callSite.Slice(8, 2)) != 1)
        {
            throw Invalid(
                "The conditional GenericCall Body has an invalid unit CallSite.");
        }

        var decoded = DecodeDirectGenericCallForChain(tables, context,
            artifactIndex, moduleRow, callerMethodRow, callerFormals,
            callerFormalRecords, callerVariables.Count, callerVariables,
            body, callSite[4..], new Dictionary<int, int>(),
            allowUnitResult: true);
        if (decoded.ResultType != PrimitiveType.Void
            || decoded.ResultHome != -1)
        {
            throw Invalid(
                "The conditional GenericCall Body does not have a unit result.");
        }

        var thenBody = new BoundBlock([
            new BoundExpressionStatement(decoded.Value, default),
        ], default);
        return [
            new BoundIf(new BoundRead(conditionVariable,
                PrimitiveType.Bool, default), thenBody, null, default),
            new BoundReturn(null, default),
        ];
    }

    private static ImmutableArray<BoundStatement>
        DecodeGenericMethodCallChainBody(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            GenericMethodReadContext context, int artifactIndex, int moduleRow,
            int callerMethodRow, ImmutableArray<PrimitiveType> callerFormals,
            TrRmGenericMethodFormalImport[] callerFormalRecords,
            PrimitiveType callerReturnType,
            ImmutableArray<BoundVariable>.Builder callerVariables,
            TrRmRegisterBodyReader body, ReadOnlySpan<byte> code)
    {
        if (code.Length < 4
            || body.RecordCount < 4
            || body.GetRecord(0)[0] != 1)
        {
            throw Unsupported(
                "The G1 consumer requires a nonempty linear GenericCall chain.");
        }

        var callerParameterCount = callerVariables.Count;
        var callerSignature = tables.Methods.GetSignature(callerMethodRow);
        if (tables.Methods.GetOwnerType(callerMethodRow) != 0
            || tables.Signatures.GetReceiverTypeSpec(callerSignature) != 0
            || tables.Signatures.GetParamRange(callerSignature).Count
                != callerParameterCount)
        {
            throw Unsupported(
                "The G1 consumer requires a module-level ownerless GenericCall chain.");
        }

        var callSiteIndexes = new List<int>();
        var returnHome = -1;
        var hasReturnHome = false;
        var position = 0;
        while (position < code.Length)
        {
            var opcode = code[position];
            var instructionLength
                = TrRmRegisterBodyReader.InstructionLength(opcode,
                    body.Schema);
            if (instructionLength <= 0
                || position + instructionLength > code.Length)
            {
                throw Invalid(
                    "The G1 GenericCall chain has an invalid instruction extent.");
            }
            if ((opcode is 0x90 or 0xE0)
                && callerVariables.Count + callSiteIndexes.Count
                    >= SemanticVerifier.MaximumVariables)
            {
                throw Unsupported(
                    "The G1 GenericCall chain exceeds the supported local-result count.");
            }

            if (opcode == 0x90 && instructionLength == 2)
            {
                if (callerVariables.Count + callSiteIndexes.Count
                    >= SemanticVerifier.MaximumVariables)
                {
                    throw Unsupported(
                        "The G1 GenericCall chain exceeds the supported local-result count.");
                }
                callSiteIndexes.Add(code[position + 1]);
            }
            else if (opcode == 0xE0 && instructionLength == 4
                && code[position + 1] == 0)
            {
                if (callerVariables.Count + callSiteIndexes.Count
                    >= SemanticVerifier.MaximumVariables)
                {
                    throw Unsupported(
                        "The G1 GenericCall chain exceeds the supported local-result count.");
                }
                callSiteIndexes.Add(BinaryPrimitives
                    .ReadUInt16LittleEndian(code.Slice(position + 2, 2)));
            }
            else if (opcode == 0xB8 && instructionLength == 2
                && position + instructionLength == code.Length)
            {
                returnHome = code[position + 1];
                hasReturnHome = true;
                position += instructionLength;
                break;
            }
            else
            {
                throw Unsupported(
                    "The G1 consumer supports only sequential GenericCall instructions with a value ReturnHome or terminal unit call.");
            }

            position += instructionLength;
        }

        if (position != code.Length
            || callSiteIndexes.Count < 2
            || body.RecordCount < callSiteIndexes.Count + 2
            || (callerReturnType == PrimitiveType.Void
                ? hasReturnHome : !hasReturnHome))
        {
            throw Unsupported(
                "The G1 consumer requires a multi-call sequence ending in a value ReturnHome or normal unit call.");
        }

        var statements = ImmutableArray.CreateBuilder<BoundStatement>(
            callSiteIndexes.Count + 1);
        var resultVariablesByHome = new Dictionary<int, int>();
        var referencedRecords = new HashSet<int> { 0 };
        var hasUnitTerminalCall = false;
        for (var callOrdinal = 0;
            callOrdinal < callSiteIndexes.Count; callOrdinal++)
        {
            var callSiteIndex = callSiteIndexes[callOrdinal];
            if ((uint)callSiteIndex >= (uint)body.RecordCount
                || !referencedRecords.Add(callSiteIndex))
            {
                throw Invalid(
                    "The G1 GenericCall chain reuses or misses a CallSite record.");
            }

            var callSite = body.GetRecord(callSiteIndex);
            if (callSite[0] != 0x02 || callSite[4] != 4
                || (callSite[5] != 2
                    && !(callOrdinal == callSiteIndexes.Count - 1
                        && callerReturnType == PrimitiveType.Void
                        && callSite[5] == 0)))
            {
                throw Unsupported(
                    "The G1 consumer requires value results except for a terminal normal unit GenericCall.");
            }

            var callSitePayload = callSite[4..];
            var targetRecordIndex = BinaryPrimitives
                .ReadUInt16LittleEndian(callSitePayload.Slice(4, 2));
            if ((uint)targetRecordIndex >= (uint)body.RecordCount)
            {
                throw Invalid(
                    "A G1 GenericCall chain CallSite has an invalid target record.");
            }
            _ = referencedRecords.Add(targetRecordIndex);

            var decoded = DecodeDirectGenericCallForChain(tables, context,
                artifactIndex, moduleRow, callerMethodRow, callerFormals,
                callerFormalRecords, callerParameterCount, callerVariables,
                body, callSitePayload, resultVariablesByHome,
                callOrdinal == callSiteIndexes.Count - 1
                    && callerReturnType == PrimitiveType.Void);
            if (decoded.ResultType == PrimitiveType.Void)
            {
                if (callOrdinal != callSiteIndexes.Count - 1
                    || hasReturnHome || decoded.ResultHome != -1)
                {
                    throw Invalid(
                        "A unit GenericCall must be the terminal operation of a unit-returning chain.");
                }

                hasUnitTerminalCall = true;
                statements.Add(new BoundExpressionStatement(decoded.Value,
                    default));
                continue;
            }

            if ((uint)decoded.ResultHome >= (uint)body.HomeCount
                || resultVariablesByHome.ContainsKey(decoded.ResultHome))
            {
                throw Invalid(
                    "A G1 GenericCall chain reuses an invalid result Home.");
            }

            var resultHome = body.HomeMap.Slice(
                decoded.ResultHome * 8, 8);
            if (resultHome[0] != 3 || resultHome[1] != 1)
            {
                throw Invalid(
                    "A G1 GenericCall chain result does not select an initialized-value Home.");
            }

            var localVariable = callerVariables.Count;
            callerVariables.Add(new BoundVariable(
                "<trrm-generic-call-result-" + callOrdinal.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + ">",
                decoded.ResultType, false, default));
            resultVariablesByHome.Add(decoded.ResultHome, localVariable);
            statements.Add(new BoundLocal(localVariable, decoded.Value,
                default));
        }

        for (var recordIndex = 1; recordIndex < body.RecordCount;
            recordIndex++)
        {
            if (!referencedRecords.Contains(recordIndex))
            {
                throw Unsupported(
                    "The G1 consumer does not retain an unreferenced GenericCall Body record.");
            }
        }

        if (hasReturnHome)
        {
            if (!resultVariablesByHome.TryGetValue(returnHome,
                    out var returnedVariable)
                || callerVariables[returnedVariable].Type
                    != callerReturnType)
            {
                throw Invalid(
                    "The G1 GenericCall chain ReturnHome does not match the open MethodSig result.");
            }

            statements.Add(new BoundReturn(new BoundRead(returnedVariable,
                callerReturnType, default), default));
        }
        else if (callerReturnType != PrimitiveType.Void
            || !hasUnitTerminalCall)
        {
            throw Invalid(
                "The GenericCall chain has no valid unit completion.");
        }
        else
        {
            statements.Add(new BoundReturn(null, default));
        }
        return statements.MoveToImmutable();
    }

    private static GenericMethodCallSiteResult
        DecodeDirectGenericCallForChain(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            GenericMethodReadContext context, int artifactIndex, int moduleRow,
            int callerMethodRow, ImmutableArray<PrimitiveType> callerFormals,
            TrRmGenericMethodFormalImport[] callerFormalRecords,
            int callerParameterCount,
            ImmutableArray<BoundVariable>.Builder callerVariables,
            TrRmRegisterBodyReader body, ReadOnlySpan<byte> callSitePayload,
            IReadOnlyDictionary<int, int> resultVariablesByHome,
            bool allowUnitResult)
    {
        var targetRecordIndex = BinaryPrimitives
            .ReadUInt16LittleEndian(callSitePayload.Slice(4, 2));
        var targetRecord = body.GetRecord(targetRecordIndex);
        if (targetRecord[0] != 0x10 || targetRecord[4] != 1
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

        var targetMethodRow = checked((int)(targetDefinition
            & 0x00FF_FFFF));
        var targetImport = importRow != 0
            ? CreateExpectedGenericMethodImport(tables, context,
                artifactIndex, moduleRow, importRow)
            : CreateGenericMethodImport(tables, context, artifactIndex,
                moduleRow, targetDefinition, "", isMethodDefTarget: true);
        var targetSignature = tables.Methods.GetSignature(targetMethodRow);
        if (tables.Methods.GetOwnerType(targetMethodRow) != 0
            || tables.Signatures.GetReceiverTypeSpec(targetSignature) != 0)
        {
            throw Unsupported(
                "The G1 multi-call consumer requires module-level GenericCall targets.");
        }
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
        var typeArgumentMetadata = ImmutableArray.CreateBuilder<
            BoundGenericNominalTypeArgument>(targetFormalCount);
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
            var actual = ResolveGenericMethodTypeSpec(context, artifactIndex,
                tables, moduleRow, tables.TypeSpecs, tables.GenericParams,
                callerFormalRecords, callerMethodRow,
                context.GetFormalTypeOwnerOrdinal(artifactIndex,
                    callerMethodRow), typeSpecRef, callerFormals,
                allowFormalForwarding: false,
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
        var callerFormalTypeOwnerOrdinal
            = context.GetFormalTypeOwnerOrdinal(artifactIndex,
                callerMethodRow);
        var (_, parameterCount)
            = tables.Signatures.GetParamRange(targetSignature);
        var inputCount = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(callSitePayload.Slice(8, 4)));
        if (parameterCount != inputCount
            || targetImport.ProofFunction.ParameterCount != parameterCount
            || tables.Signatures.GetReceiverTypeSpec(targetSignature) != 0)
        {
            throw Invalid(
                "The GenericCall input count differs from its selected MethodSig.");
        }

        var arguments = ImmutableArray.CreateBuilder<BoundExpression>(
            parameterCount);
        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            var inputOffset = 16 + (ordinal * 4);
            var selectorKind = callSitePayload[inputOffset];
            var sourceUse = callSitePayload[inputOffset + 1];
            var homeIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                callSitePayload.Slice(inputOffset + 2, 2));
            var targetParameterType = targetImport.ProofFunction
                .Variables[ordinal].Type;
            if (!TrySubstituteGenericMethodType(targetParameterType,
                    targetFormalTypes, actualTypes, actualTypeMetadata,
                    targetImport, context, artifactIndex, callerMethodRow,
                    callerFormals, callerFormalTypeOwnerOrdinal, 0,
                    out targetParameterType))
            {
                throw Unsupported(
                    "The G1 GenericCall parameter type uses an unsupported substitution term.");
            }

            if (targetParameterType == PrimitiveType.Void)
            {
                if (selectorKind != 0 || sourceUse != 0 || homeIndex != 0)
                {
                    throw Invalid(
                        "A unit GenericCall input must use the exact None selector.");
                }

                arguments.Add(new BoundConstant(0, PrimitiveType.Void,
                    default));
                continue;
            }

            if (selectorKind != 2 || sourceUse != 1)
            {
                throw Unsupported(
                    "The G1 multi-call consumer supports Copy parameter-Home inputs only.");
            }

            if ((uint)homeIndex >= (uint)body.HomeCount)
            {
                throw Invalid(
                    "A GenericCall input references an invalid Home.");
            }

            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            var variable = home[0] switch {
                2 => checked((int)BinaryPrimitives
                    .ReadUInt16LittleEndian(home.Slice(4, 2))),
                3 when resultVariablesByHome.TryGetValue(homeIndex,
                    out var resultVariable) => resultVariable,
                _ => -1,
            };
            if (variable < 0 || variable >= callerVariables.Count
                || (home[0] == 2 && variable >= callerParameterCount))
            {
                throw Invalid(
                    "A GenericCall input Home is not initialized before this call.");
            }

            var argumentType = callerVariables[variable].Type;
            if (argumentType != targetParameterType)
            {
                throw Invalid(
                    "A GenericCall input type differs from its substituted target formal.");
            }

            arguments.Add(new BoundRead(variable, argumentType, default));
        }

        var resultType = targetImport.ProofFunction.ReturnType;
        if (!TrySubstituteGenericMethodType(resultType, targetFormalTypes,
                actualTypes, actualTypeMetadata, targetImport, context,
                artifactIndex, callerMethodRow, callerFormals,
                callerFormalTypeOwnerOrdinal, 0, out resultType)
            || targetImport.ProofFunction.DoesNotReturn)
        {
            throw Unsupported(
                "The G1 GenericCall result type or termination contract is outside the multi-call subset.");
        }

        var resultHome = BinaryPrimitives.ReadUInt16LittleEndian(
            callSitePayload.Slice(6, 2));
        if (resultType == PrimitiveType.Void)
        {
            if (!allowUnitResult || callSitePayload[1] != 0
                || resultHome != 0)
            {
                throw Invalid(
                    "A unit GenericCall must be the terminal None-result call of a unit-returning chain.");
            }
        }
        else
        {
            if ((uint)resultHome >= (uint)body.HomeCount)
            {
                throw Invalid(
                    "A GenericCall chain references an invalid result Home.");
            }
            var resultHomeEntry = body.HomeMap.Slice(resultHome * 8, 8);
            if (callSitePayload[1] != 2 || resultHomeEntry[0] != 3
                || resultHomeEntry[1] != 1)
            {
                throw Invalid(
                    "The GenericCall result does not select a value Home.");
            }
        }

        var call = new BoundCall(0, arguments.MoveToImmutable(),
            resultType, default) {
            ImportedGenericMethodCall = targetImport,
            ImportedGenericMethodTypeArguments = actualTypes,
            IsUnsafeAuthorized = targetImport.Signature.RequiresUnsafeCaller,
        };
        BoundExpression value = targetImport.Signature.RequiresUnsafeCaller
            ? new BoundUnsafeExpression(call, default) : call;
        return new GenericMethodCallSiteResult(
            resultType == PrimitiveType.Void ? -1 : resultHome,
            resultType, value);
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
        if (!context.Schema.UsesGeneralMethodParameterLabels())
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
                ResolveRequirementType(parameter.Type, allowVoid: true),
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
            && move.Variable
                == parameterOrdinal + (function.IsStatic ? 0 : 1)
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
        BoundGenericMethodTypeDefault? TypeDefault)
    {
        internal ImmutableArray<BoundGenericMethodAssociatedRelation>
            AssociatedRelations { get; init; } = [];
    }

    private static BoundGenericMethodAssociatedRelation
        DecodeG1MethodAssociatedRelation(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            GenericMethodReadContext context, int artifactIndex, int moduleRow,
            int methodRow, int formalCount, int constraintRow,
            ImmutableArray<PrimitiveType> formalTypes,
            TrRmGenericMethodFormalImport[] formalRecords,
            int formalTypeOwnerOrdinal, ref int remainingVisits)
    {
        if (--remainingVisits < 0)
        {
            throw Unsupported(
                "The G1 associated-relation closure exceeds the supported analysis limit.");
        }

        var formalRow = tables.Constraints
            .GetOwnerGenericParam(constraintRow);
        var formalOrdinal = checked((int)tables.GenericParams
            .GetOrdinal(formalRow));
        if (tables.GenericParams.GetFormalKind(formalRow) != 1
            || tables.GenericParams.GetOwnerDefId(formalRow)
                != (0x1600_0000u | checked((uint)methodRow))
            || (uint)formalOrdinal >= (uint)formalCount)
        {
            throw Invalid(
                "A G1 associated-relation Constraint has an invalid method formal owner.");
        }

        var relationRow = checked((int)tables.Constraints
            .GetPayload(constraintRow));
        var declaringInterfaceTypeSpecRef = tables.AssociatedRelations
            .GetDeclaringInterfaceTypeSpecRef(relationRow);
        var associatedTypeDefReference = tables.AssociatedRelations
            .GetAssociatedTypeDefReference(relationRow);
        var requiredTypeSpecRef = tables.AssociatedRelations
            .GetRequiredTypeSpecRef(relationRow);
        _ = CreateGenericInterfaceDefinitions(tables, context,
            artifactIndex, moduleRow);
        var interfaceConstraintVisits
            = Math.Min(remainingVisits,
                SemanticVerifier.MaximumAnalysisVisits);
        var declaringInterface = DecodeG1MethodInterfaceConstraint(tables,
            context, artifactIndex, moduleRow, methodRow, formalCount,
            declaringInterfaceTypeSpecRef,
            ref interfaceConstraintVisits);
        remainingVisits = Math.Min(remainingVisits, interfaceConstraintVisits);
        if (declaringInterface.Kind != BoundInterfaceTypeKind.Nominal
            || declaringInterface.IsWritable
            || declaringInterface.IsOptional
            || declaringInterface.IsGarbageCollected
            || declaringInterface.IsInitialization)
        {
            throw Unsupported(
                "A G1 associated relation must name one unqualified interface instance.");
        }

        var typeDef = tables.TypeSpecs.GetNominalTypeDefRow(
            declaringInterfaceTypeSpecRef, tables.TypeDefs, tables.Imports);
        var interfaceDef
            = tables.Interfaces.GetInterfaceDefForTypeDef(typeDef);
        if (interfaceDef == 0
            || !context.InterfaceDefinitionsByTypeDef.TryGetValue(
                (artifactIndex, moduleRow, typeDef),
                out var interfaceDefinition))
        {
            throw Unsupported(
                "A G1 associated relation has no retained declaring-interface closure.");
        }

        if (!TrRmAssociatedTypeReference.TryGetLocalRow(
                associatedTypeDefReference,
                tables.AssociatedTypeDefs.Count, tables.Imports,
                out var associatedTypeDefRow)
            || tables.AssociatedTypeDefs.GetOwnerInterfaceDef(
                associatedTypeDefRow) != interfaceDef)
        {
            throw Invalid(
                "A G1 associated-relation reference has the wrong namespace or declaring interface.");
        }

        var (firstAssociatedType, associatedTypeCount)
            = tables.Interfaces.GetAssociatedTypeRange(interfaceDef);
        if (associatedTypeDefRow < firstAssociatedType
            || associatedTypeDefRow
                >= firstAssociatedType + associatedTypeCount)
        {
            throw Invalid(
                "A G1 associated-relation reference is outside its interface member range.");
        }

        var memberOrdinal = tables.Interfaces.GetMethodRange(interfaceDef)
            .Count + associatedTypeDefRow - firstAssociatedType;
        if ((uint)memberOrdinal >= (uint)interfaceDefinition.Members.Length
            || interfaceDefinition.Members[memberOrdinal]
                is not BoundInterfaceMemberDefinition {
                    Kind: BoundInterfaceMemberKind.AssociatedType,
                } associatedMember
            || !StringComparer.Ordinal.Equals(associatedMember.Name,
                tables.Strings.DecodeName(
                    tables.AssociatedTypeDefs.GetNameStringRow(
                        associatedTypeDefRow))))
        {
            throw Invalid(
                "A G1 associated-relation reference does not select its exact retained member.");
        }

        var associatedDeclarationKey
            = ValidateG1AssociatedTypeReferenceBinding(tables, moduleRow,
            associatedTypeDefReference, associatedTypeDefRow,
            interfaceDefinition, associatedMember);

        var requiredType = ResolveGenericMethodTypeSpec(context,
            artifactIndex, tables, moduleRow, tables.TypeSpecs,
            tables.GenericParams, formalRecords, methodRow,
            formalTypeOwnerOrdinal, requiredTypeSpecRef, formalTypes,
            allowFormalForwarding: true, ordinal: formalCount,
            out var requiredFormalOrdinal);
        if (!TryCreateG1GenericMethodTypeTerm(requiredType, formalTypes,
                formalRecords, formalTypeOwnerOrdinal, context.SpanTypes,
                context.CheckedReferenceTypeDefinitions,
                context.GenericNominalTypeDefinitions.ToImmutable(),
                context.GenericNominalTypeInstances, 0,
                requiredFormalOrdinal, out var requiredTypeTerm))
        {
            throw Unsupported(
                "A G1 associated relation uses an unsupported required type.");
        }

        if (!formalRecords[formalOrdinal].InterfaceConstraints.Any(
                constraint =>
                    BoundInterfaceContract
                        .AreEquivalentProviderInterfaceViews(
                            constraint, declaringInterface)))
        {
            throw Invalid(
                "A G1 associated-relation Constraint lacks its separate matching interface conformance Constraint.");
        }

        var identity = new BoundInterfaceMemberIdentity(
            declaringInterface, associatedMember.Ordinal,
            associatedDeclarationKey);
        return new BoundGenericMethodAssociatedRelation(formalOrdinal,
            identity, requiredTypeTerm, relationRow,
            declaringInterfaceTypeSpecRef, associatedTypeDefReference,
            requiredTypeSpecRef);
    }

    private static string ValidateG1AssociatedTypeReferenceBinding(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int moduleRow, uint associatedTypeDefReference,
        int associatedTypeDefRow,
        BoundInterfaceDefinition interfaceDefinition,
        BoundInterfaceMemberDefinition associatedMember)
    {
        var definitionId = 0x2000_0000u
            | checked((uint)associatedTypeDefRow);
        var referenceKind = associatedTypeDefReference >> 24;
        string? selectedExportKey = null;
        string? selectedProducer = null;
        uint selectedTargetRequirement = 0;
        uint selectedRuntimePolicy = 0;
        var ownerImport = interfaceDefinition.ModuleImport;
        if (referenceKind == 0x1C)
        {
            var importRow = checked((int)(associatedTypeDefReference
                & 0x00FF_FFFF));
            if (importRow == 0)
            {
                throw Invalid(
                    "A G1 AssociatedTypeDef reference has no exact Import binding.");
            }

            var moduleRefRow = tables.Imports
                .GetModuleRefForImportRow(importRow);
            var selected = tables.Imports
                .GetBindingForImportRow(importRow);
            if (tables.Imports.GetExpectedDefinitionForImportRow(importRow)
                    != definitionId
                || tables.ModuleRefs.GetOwnerModule(moduleRefRow) != moduleRow)
            {
                throw Invalid(
                    "A G1 AssociatedTypeDef Import has an invalid expected owner or ModuleRef.");
            }

            selectedExportKey = selected.ExportKey;
            selectedProducer = selected.ProducerIdentity;
            selectedTargetRequirement = selected.TargetRequirement;
            selectedRuntimePolicy = selected.RuntimePolicy;
        }
        else if (referenceKind == 0x20)
        {
            var isExpected = tables.Imports
                .IsExpectedDefinition(definitionId);
            var foundPrimaryKey = false;
            if (isExpected)
            {
                for (var importRow = 1;
                    importRow <= tables.Imports.Count; importRow++)
                {
                    if (tables.Imports
                            .GetExpectedDefinitionForImportRow(importRow)
                        != definitionId)
                    {
                        continue;
                    }

                    var moduleRefRow = tables.Imports
                        .GetModuleRefForImportRow(importRow);
                    var selected = tables.Imports
                        .GetBindingForImportRow(importRow);
                    if (tables.ModuleRefs.GetOwnerModule(moduleRefRow)
                            != moduleRow
                        || ownerImport is null
                        || !StringComparer.Ordinal.Equals(
                            selected.ProducerIdentity,
                            ownerImport.ProducerIdentity)
                        || selected.TargetRequirement
                            != ownerImport.TargetRequirement
                        || selected.RuntimePolicy
                            != ownerImport.RuntimePolicy
                        || !IsAssociatedTypeExportKey(
                            associatedMember, selected.ExportKey))
                    {
                        throw Invalid(
                            "A direct G1 AssociatedTypeDef expectation has a conflicting Import alias.");
                    }

                    if (StringComparer.Ordinal.Equals(
                        associatedMember.DeclarationKey,
                        selected.ExportKey))
                    {
                        foundPrimaryKey = true;
                    }
                }

                if (!foundPrimaryKey)
                {
                    throw Invalid(
                        "A direct G1 AssociatedTypeDef expectation does not retain its primary Import key.");
                }
                selectedExportKey = associatedMember.DeclarationKey;
                selectedProducer = ownerImport.ProducerIdentity;
                selectedTargetRequirement = ownerImport.TargetRequirement;
                selectedRuntimePolicy = ownerImport.RuntimePolicy;
            }
            else
            {
                var foundPrimaryKey = false;
                foreach (var (definition, exportKey)
                    in tables.Exports.GetExportsForModule(moduleRow))
                {
                    if (definition != definitionId)
                    {
                        continue;
                    }
                    if (!IsAssociatedTypeExportKey(associatedMember,
                            exportKey))
                    {
                        throw Invalid(
                            "A direct G1 AssociatedTypeDef Export is not retained by its interface member view.");
                    }
                    foundPrimaryKey |= StringComparer.Ordinal.Equals(
                        associatedMember.DeclarationKey, exportKey);
                }

                if (!foundPrimaryKey || ownerImport is null)
                {
                    throw Invalid(
                        "A direct G1 AssociatedTypeDef has no exact ExportKey.");
                }
                selectedExportKey = associatedMember.DeclarationKey;
                selectedProducer = tables.Modules.GetIdentity(moduleRow);
                (selectedTargetRequirement, selectedRuntimePolicy)
                    = tables.Modules.GetRequirements(moduleRow);
            }
        }

        if (ownerImport is null
            || selectedExportKey is null
            || selectedProducer is null
            || !StringComparer.Ordinal.Equals(selectedProducer,
                ownerImport.ProducerIdentity)
            || selectedTargetRequirement != ownerImport.TargetRequirement
            || selectedRuntimePolicy != ownerImport.RuntimePolicy
            || !IsAssociatedTypeExportKey(associatedMember,
                selectedExportKey))
        {
            throw Invalid(
                "A G1 AssociatedTypeDef reference does not bind through its exact declaring-interface provider.");
        }

        return selectedExportKey;

        static bool IsAssociatedTypeExportKey(
            BoundInterfaceMemberDefinition member, string exportKey)
            => StringComparer.Ordinal.Equals(member.DeclarationKey,
                    exportKey)
                || member.AdditionalDeclarationKeys.Contains(exportKey,
                    StringComparer.Ordinal);
    }

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
            if (reference.IsOptionalValue)
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
        if (reference.Form == 0x06 && row.Length == 6)
        {
            if (reference.Qualifiers != 0)
            {
                throw Unsupported(
                    "G1 optional wrappers do not admit qualifiers on the optional node.");
            }

            var innerTypeSpecRef = BinaryPrimitives
                .ReadUInt32LittleEndian(row.Slice(2, sizeof(uint)));
            var innerReference
                = typeSpecs.GetTypeSpecRef(innerTypeSpecRef);
            var innerForm = innerReference.Form;
            if (innerForm is 0x02 or 0x03 or 0x05)
            {
                if (innerReference.Qualifiers != 0)
                {
                    throw Unsupported(
                        "G1 optional class handles do not preserve inner TypeSpec qualifiers.");
                }

                var optionalType = context.ReadGenericNominalTypeSpec(
                    tables, artifactIndex, moduleRow, typeSpecRef,
                    methodRow, formalTypes, formalRecords);
                var optionalInstance
                    = context.GenericNominalTypeInstances[
                        BoundGenericNominalTypeTypes.Index(optionalType)];
                if (!context.GenericNominalTypeDefinitions.Any(definition =>
                        definition.Identity
                            == optionalInstance.Definition
                        && definition.NominalKind == 2))
                {
                    throw Unsupported(
                        "G1 optional nominal wrappers are supported only for class TypeDefs.");
                }

                return new G1GenericTypeSpecResolution(optionalType, -1,
                    false, false);
            }

            if (reference.Qualifiers != 0 || innerForm != 0x07)
            {
                throw Unsupported(
                    "G1 generic MethodDef imports support optional class handles and checked-reference wrappers only.");
            }

            var inner = ResolveGenericMethodTypeSpecCore(context,
                artifactIndex, tables, moduleRow, typeSpecs,
                genericParams, formalRecords, methodRow,
                formalTypeOwnerOrdinal, innerTypeSpecRef, formalTypes,
                allowFormalForwarding, ordinal, false, false,
                ref remaining, depth + 1);
            if (!CheckedReferenceTypeContract.IsValidType(inner.Type,
                    context.CheckedReferenceTypeDefinitions))
            {
                throw Invalid(
                    "A G1 optional TypeSpec does not wrap a checked-reference type.");
            }

            var innerReference = context
                .CheckedReferenceTypeDefinitions[
                    CheckedReferenceTypes.Index(inner.Type)];
            if (innerReference.IsInitialization
                || innerReference.IsOptional)
            {
                throw Unsupported(
                    "G1 does not support optional initialization or nested optional checked references.");
            }

            var optionalType = context.GetCheckedReferenceType(
                artifactIndex, methodRow, innerReference.ReferentType,
                innerReference.IsWritable,
                innerReference.IsGarbageCollected,
                isInitialization: false,
                isReferentWritable: innerReference.IsReferentWritable,
                isOptional: true);
            return new G1GenericTypeSpecResolution(optionalType, -1,
                false, false);
        }

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
            if (isGarbageCollectedQualifier
                && isInitializationReference)
            {
                throw Unsupported(
                    "G1 initialization-reference TypeSpecs cannot be GC-backed.");
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
                isGarbageCollected: isGarbageCollectedQualifier,
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
