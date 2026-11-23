// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private readonly record struct ForeignNativeInterfaceBodyTarget(
        BoundInterfaceDefinition Definition,
        BoundInterfaceMemberDefinition Member, int MemberOrdinal);

    private static Dictionary<int, ForeignNativeInterfaceBodyTarget>
        CreateForeignNativeInterfaceBodyTargets(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int selectedModuleRow,
        ImmutableArray<BoundInterfaceDefinition> definitions)
    {
        var result = new Dictionary<int, ForeignNativeInterfaceBodyTarget>();
        for (var foreignInterfaceRow = 1;
            foreignInterfaceRow <= tables.ForeignNativeInterfaces.Count;
            foreignInterfaceRow++)
        {
            var interfaceDef = checked((int)
                tables.ForeignNativeInterfaces.GetOwnerInterfaceDef(
                    foreignInterfaceRow));
            var typeDef = tables.Interfaces.GetOwnerTypeDef(interfaceDef);
            if (tables.TypeDefs.GetOwnerModule(typeDef)
                != selectedModuleRow)
            {
                continue;
            }

            var typeDefId = 0x1300_0000u | checked((uint)typeDef);
            BoundModuleImport ownerImport;
            var typeImportRows = Enumerable.Range(1, tables.Imports.Count)
                .Where(importRow =>
                    tables.Imports.GetExpectedDefinitionForImportRow(
                        importRow) == typeDefId
                    && tables.ModuleRefs.GetOwnerModule(
                        tables.Imports.GetModuleRefForImportRow(importRow))
                        == selectedModuleRow)
                .ToArray();
            if (typeImportRows.Length == 1)
            {
                var binding = tables.Imports.GetBindingForImportRow(
                    typeImportRows[0]);
                ownerImport = new BoundModuleImport(
                    binding.ProducerIdentity, binding.ExportKey,
                    binding.TargetRequirement, binding.RuntimePolicy);
            }
            else if (typeImportRows.Length == 0)
            {
                var typeExports = tables.Exports.GetExportsForModule(
                        selectedModuleRow)
                    .Where(export => export.Definition == typeDefId)
                    .ToArray();
                if (typeExports.Length != 1)
                {
                    throw Invalid(
                        "A ForeignNativeInterface Body target has no unique TypeDef ExportKey or Import.");
                }

                var (targetRequirement, runtimePolicy)
                    = tables.Modules.GetRequirements(selectedModuleRow);
                ownerImport = new BoundModuleImport(
                    tables.Modules.GetIdentity(selectedModuleRow),
                    typeExports[0].ExportKey, targetRequirement,
                    runtimePolicy);
            }
            else
            {
                throw Invalid(
                    "A ForeignNativeInterface Body target has multiple TypeDef Imports.");
            }

            var typeName = tables.TypeDefs.GetName(typeDef);
            var typeNamespace = tables.TypeDefs.GetNamespace(typeDef);
            var qualifiedName = typeNamespace.Length == 0
                ? typeName : string.Concat(typeNamespace, ".", typeName);
            var owners = definitions.Where(definition =>
                    StringComparer.Ordinal.Equals(
                        definition.QualifiedName, qualifiedName)
                    && definition.ModuleImport == ownerImport
                    && definition.ForeignNativeInterface is not null)
                .ToArray();
            if (owners is not [{ } owner])
            {
                throw Invalid(
                    "A ForeignNativeInterface Body target has no exact bound owner.");
            }

            var (firstMethod, methodCount)
                = tables.Interfaces.GetMethodRange(interfaceDef);
            if (owner.Members.Length != methodCount)
            {
                throw Invalid(
                    "A ForeignNativeInterface Body target has an incomplete member view.");
            }

            for (var ordinal = 0; ordinal < methodCount; ordinal++)
            {
                var interfaceMethod = firstMethod + ordinal;
                var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                    interfaceMethod);
                var member = owner.Members[ordinal];
                if (tables.InterfaceMethods.GetOwnerInterfaceDef(
                        interfaceMethod) != interfaceDef
                    || tables.InterfaceMethods.GetOrdinal(interfaceMethod)
                        != (uint)ordinal
                    || !result.TryAdd(methodRow,
                        new ForeignNativeInterfaceBodyTarget(owner,
                            member, ordinal)))
                {
                    throw Invalid(
                        "A ForeignNativeInterface MethodDef has an ambiguous Body target identity.");
                }
            }
        }

        return result;
    }

    private static ImmutableArray<BoundInterfaceDefinition>
        CreateForeignNativeInterfaceDefinitions(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow)
    {
        var importRows = CreateGenericInterfaceImportRows(tables, moduleRow);
        var usedImportRows = new HashSet<int>();
        var typeExportKeys = new Dictionary<uint, string>();
        var methodExportKeys = new Dictionary<uint, List<string>>();
        foreach (var (definition, exportKey)
            in tables.Exports.GetExportsForModule(moduleRow))
        {
            if ((definition >> 24) == 0x13)
            {
                if (!typeExportKeys.TryAdd(definition, exportKey))
                {
                    throw Invalid(
                        "A ForeignNativeInterface TypeDef has multiple ExportKeys.");
                }
            }
            else if ((definition >> 24) == 0x16)
            {
                if (!methodExportKeys.TryGetValue(definition,
                        out var keys))
                {
                    keys = [];
                    methodExportKeys.Add(definition, keys);
                }

                keys.Add(exportKey);
            }
        }

        var pointerTypeIndexes = context.ForeignNativeInterfacePointerTypes;
        var signatureIndexByAnchorMethod
            = new int[tables.Methods.Count + 1];
        Array.Fill(signatureIndexByAnchorMethod, -1);
        var signatureIndexByAnchorTypeSpec
            = new int[tables.TypeSpecs.Count + 1];
        Array.Fill(signatureIndexByAnchorTypeSpec, -1);
        var includedTypeDefRows
            = GetForeignNativeInterfaceStructureTypeDefClosure(tables,
                moduleRow, rootTypeDefRows: null,
                out var structureImportRows);
        usedImportRows.UnionWith(structureImportRows);

        _ = LoadForeignNativeInterfaceStructures(tables, context,
            artifactIndex, moduleRow, includedTypeDefRows,
            out var structureTypeByTypeDef,
            signatureIndexByAnchorMethod,
            signatureIndexByAnchorTypeSpec);
        BindForeignNativeInterfaceStructureImports(tables, context,
            artifactIndex, moduleRow, structureImportRows,
            structureTypeByTypeDef, usedImportRows);
        ReadForeignNativeInterfaceFunctionPointerAnchors(tables, context,
            moduleRow, pointerTypeIndexes, signatureIndexByAnchorMethod,
            signatureIndexByAnchorTypeSpec, structureTypeByTypeDef,
            includedTypeDefRows);
        var foreignInterfaceRowsByOwner
            = new Dictionary<int, int>(tables.ForeignNativeInterfaces.Count);
        for (var foreignInterfaceRow = 1;
            foreignInterfaceRow <= tables.ForeignNativeInterfaces.Count;
            foreignInterfaceRow++)
        {
            var interfaceDef = checked((int)
                tables.ForeignNativeInterfaces.GetOwnerInterfaceDef(
                    foreignInterfaceRow));
            if (!foreignInterfaceRowsByOwner.TryAdd(interfaceDef,
                    foreignInterfaceRow))
            {
                throw Invalid(
                    "A ForeignNativeInterface InterfaceDef has duplicate layout rows.");
            }
        }

        var (firstTypeDef, endTypeDef)
            = tables.Modules.GetTypeDefRange(moduleRow);
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(moduleRow);
        var exportedDefinitions = ImmutableArray.CreateBuilder<
            BoundInterfaceDefinition>();
        var exposedDefinitions = ImmutableArray.CreateBuilder<
            BoundInterfaceDefinition>();
        var exposedProviderViews = new Dictionary<(
            string ProducerIdentity, string ExportKey), int>();
        var expectedViewCount = 0;
        for (var typeDef = firstTypeDef; typeDef < endTypeDef; typeDef++)
        {
            var interfaceDef
                = tables.Interfaces.GetInterfaceDefForTypeDef(typeDef);
            if (interfaceDef == 0)
            {
                continue;
            }

            var typeDefId = 0x1300_0000u | checked((uint)typeDef);
            var hasTypeExport = typeExportKeys.TryGetValue(typeDefId,
                out var typeExportKey);
            var hasTypeImport = importRows.DefinitionRows.TryGetValue(
                typeDefId, out var typeImportRow);
            if (hasTypeExport && hasTypeImport)
            {
                throw Invalid(
                    "A ForeignNativeInterface TypeDef is both exported and imported.");
            }

            if (!hasTypeExport && !hasTypeImport)
            {
                var (unexportedFirstMethod, unexportedMethodCount)
                    = tables.Interfaces.GetMethodRange(interfaceDef);
                for (var methodOrdinal = 0;
                    methodOrdinal < unexportedMethodCount;
                    methodOrdinal++)
                {
                    var methodRow = tables.InterfaceMethods
                        .GetMethodDefRow(unexportedFirstMethod
                            + methodOrdinal);
                    var methodId = 0x1600_0000u
                        | checked((uint)methodRow);
                    if (methodExportKeys.ContainsKey(methodId))
                    {
                        throw Invalid(
                            "A ForeignNativeInterface MethodDef is exported without its owner TypeDef.");
                    }
                    if (importRows.MethodRows.ContainsKey(methodId))
                    {
                        throw Invalid(
                            "A ForeignNativeInterface MethodDef is imported without its owner TypeDef.");
                    }
                }

                continue;
            }

            if (exposedDefinitions.Count
                >= SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Unsupported(
                    "The F+I source view exceeds the supported declaration limit.");
            }

            if (!hasTypeExport)
            {
                var (firstMethod, methodCount)
                    = tables.Interfaces.GetMethodRange(interfaceDef);
                if (!foreignInterfaceRowsByOwner.TryGetValue(interfaceDef,
                        out var foreignInterfaceRow))
                {
                    throw Invalid(
                        "A ForeignNativeInterface InterfaceDef has no layout row.");
                }

                var providerView
                    = BindExpectedForeignNativeInterfaceDefinition(tables,
                        context, artifactIndex, moduleRow, interfaceDef,
                        typeDef, typeImportRow, importRows,
                        usedImportRows, foreignInterfaceRow,
                        context.ForeignNativeInterfacePointerTypes,
                        signatureIndexByAnchorMethod,
                        signatureIndexByAnchorTypeSpec,
                        structureTypeByTypeDef, firstMethod, methodCount);
                if (!context.InterfaceDefinitionsByTypeDef.TryAdd(
                        (artifactIndex, moduleRow, typeDef), providerView))
                {
                    throw Invalid(
                        "A ForeignNativeInterface TypeDef has multiple declaration views.");
                }

                var providerImport = providerView.ModuleImport
                    ?? throw Invalid(
                        "A selected ForeignNativeInterface provider has no module identity.");
                var providerViewKey = (providerImport.ProducerIdentity,
                    providerImport.ExportKey);
                var expectedViews
                    = providerView.ForeignNativeInterfaceExpectedViews;
                if (expectedViews.Length != 1
                    || expectedViews[0].SourceTypeDefRow != typeDef)
                {
                    throw Invalid(
                        "A selected ForeignNativeInterface provider view has no exact expected TypeDef surface.");
                }
                if (expectedViewCount
                    >= SemanticVerifier.MaximumAnalysisVisits)
                {
                    throw Unsupported(
                        "The retained FNI expected-view count exceeds the supported declaration limit.");
                }
                expectedViewCount++;

                if (exposedProviderViews.TryGetValue(providerViewKey,
                        out var exposedIndex))
                {
                    var exposedProviderView
                        = exposedDefinitions[exposedIndex];
                    var retainedExpectedViews
                        = exposedProviderView
                            .ForeignNativeInterfaceExpectedViews;
                    if (retainedExpectedViews.Any(expectedView =>
                            expectedView.SourceTypeDefRow == typeDef))
                    {
                        throw Invalid(
                            "A ForeignNativeInterface TypeDef expectation is retained more than once.");
                    }

                    exposedDefinitions[exposedIndex]
                        = exposedProviderView with {
                            ForeignNativeInterfaceExpectedViews = [
                                .. retainedExpectedViews, expectedViews[0],
                            ],
                        };
                }
                else
                {
                    exposedProviderViews.Add(providerViewKey,
                        exposedDefinitions.Count);
                    exposedDefinitions.Add(providerView);
                }

                continue;
            }

            var (firstMethod, methodCount)
                = tables.Interfaces.GetMethodRange(interfaceDef);
            var methodKeys = new ImmutableArray<string>[methodCount];
            for (var methodOrdinal = 0; methodOrdinal < methodCount;
                methodOrdinal++)
            {
                var interfaceMethod = firstMethod + methodOrdinal;
                if (tables.InterfaceMethods.GetOwnerInterfaceDef(
                        interfaceMethod) != interfaceDef
                    || tables.InterfaceMethods.GetOrdinal(interfaceMethod)
                        != methodOrdinal)
                {
                    throw Invalid(
                        "A ForeignNativeInterface InterfaceMethod range is not declaration-ordered.");
                }

                var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                    interfaceMethod);
                var methodId = 0x1600_0000u
                    | checked((uint)methodRow);
                if (!methodExportKeys.TryGetValue(methodId,
                        out var exportsForMethod)
                    || exportsForMethod.Count == 0)
                {
                    throw Invalid(
                        "A ForeignNativeInterface MethodDef has no ExportKey.");
                }

                methodKeys[methodOrdinal] = [.. exportsForMethod
                    .OrderBy(static key => key, StringComparer.Ordinal)];
            }

            var interfaceName = tables.TypeDefs.GetName(typeDef);
            var interfaceNamespace = tables.TypeDefs.GetNamespace(typeDef);
            var qualifiedName = interfaceNamespace.Length == 0
                ? interfaceName
                : string.Concat(interfaceNamespace, ".", interfaceName);
            if (!foreignInterfaceRowsByOwner.TryGetValue(interfaceDef,
                    out var foreignInterfaceRow))
            {
                throw Invalid(
                    "A ForeignNativeInterface InterfaceDef has no layout row.");
            }

            var definition = ReadForeignNativeInterfaceDefinition(tables,
                context, interfaceDef, typeDef, foreignInterfaceRow,
                qualifiedName, typeExportKey, targetRequirement,
                runtimePolicy, methodKeys, pointerTypeIndexes,
                signatureIndexByAnchorMethod,
                signatureIndexByAnchorTypeSpec, structureTypeByTypeDef,
                tables.Modules.GetIdentity(moduleRow));
            if (!context.InterfaceDefinitionsByTypeDef.TryAdd(
                    (artifactIndex, moduleRow, typeDef), definition))
            {
                throw Invalid(
                    "A ForeignNativeInterface TypeDef has multiple declaration views.");
            }

            if (!context.ExportedInterfaceDefinitionsByKey.TryAdd(
                    (artifactIndex, moduleRow, typeExportKey), definition))
            {
                throw Invalid(
                    "A ForeignNativeInterface TypeDef ExportKey selects multiple declarations.");
            }

            exportedDefinitions.Add(definition);
            exposedDefinitions.Add(definition);
        }

        foreach (var importRow in importRows.DefinitionRows.Values)
        {
            if (!usedImportRows.Contains(importRow))
            {
                throw Invalid(
                    "A ForeignNativeInterface TypeDef Import is unused by a retained declaration.");
            }
        }
        foreach (var importRowsForMethod in importRows.MethodRows.Values)
        {
            foreach (var importRow in importRowsForMethod)
            {
                if (!usedImportRows.Contains(importRow))
                {
                    throw Invalid(
                        "A ForeignNativeInterface MethodDef Import is unused by a retained declaration.");
                }
            }
        }
        if (importRows.AssociatedTypeRows.Count != 0)
        {
            throw Invalid(
                "A ForeignNativeInterface declaration contains an associated-type Import.");
        }
        var usedModuleRefs = new HashSet<int>();
        for (var importRow = 1; importRow <= tables.Imports.Count;
            importRow++)
        {
            var moduleRefRow
                = tables.Imports.GetModuleRefForImportRow(importRow);
            if (tables.ModuleRefs.GetOwnerModule(moduleRefRow) != moduleRow)
            {
                continue;
            }

            if (!usedImportRows.Contains(importRow))
            {
                throw Unsupported(
                    "The F+I source reference has an unrelated unbound Import.");
            }

            _ = usedModuleRefs.Add(moduleRefRow);
        }

        var (moduleRefStart, moduleRefEnd)
            = tables.Modules.GetModuleRefRange(moduleRow);
        for (var moduleRefRow = moduleRefStart;
            moduleRefRow < moduleRefEnd; moduleRefRow++)
        {
            if (!usedModuleRefs.Contains(moduleRefRow))
            {
                throw Unsupported(
                    "The F+I source reference has an unused ModuleRef.");
            }
        }

        var result = exportedDefinitions.ToImmutable();
        context.ExportedInterfaceDefinitions.Add(
            (artifactIndex, moduleRow), result);
        return exposedDefinitions.ToImmutable();
    }

    private static ImmutableArray<BoundStruct>
        LoadForeignNativeInterfaceStructures(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow,
        HashSet<int> includedTypeDefRows,
        out int[] structureTypeByTypeDef,
        int[]? signatureIndexByAnchorMethod = null,
        int[]? signatureIndexByAnchorTypeSpec = null)
    {
        var moduleKey = (artifactIndex, moduleRow);
        structureTypeByTypeDef = new int[tables.TypeDefs.Count + 1];
        Array.Fill(structureTypeByTypeDef, -1);
        var existingStructureIndexesByTypeDef
            = new Dictionary<int, int>();
        if (context.ForeignNativeInterfaceStructureModulesLoaded.Contains(
                moduleKey))
        {
            foreach (var typeDefRow in includedTypeDefRows)
            {
                if (context.ForeignNativeInterfaceStructureIndexesByTypeDef
                    .TryGetValue((artifactIndex, moduleRow, typeDefRow),
                        out var structureIndex))
                {
                    existingStructureIndexesByTypeDef.Add(typeDefRow,
                        structureIndex);
                    structureTypeByTypeDef[typeDefRow] = structureIndex;
                }
            }

            if (existingStructureIndexesByTypeDef.Count
                == includedTypeDefRows.Count)
            {
                return [.. context.ForeignNativeInterfaceStructures];
            }
        }

        static int[] CreateUnmappedSignatureIndexes(int count)
        {
            var indexes = new int[count + 1];
            Array.Fill(indexes, -1);
            return indexes;
        }

        var anchorMethodMap = signatureIndexByAnchorMethod
            ?? CreateUnmappedSignatureIndexes(tables.Methods.Count);
        var anchorTypeSpecMap = signatureIndexByAnchorTypeSpec
            ?? CreateUnmappedSignatureIndexes(tables.TypeSpecs.Count);
        if (anchorMethodMap.Length != tables.Methods.Count + 1
            || anchorTypeSpecMap.Length != tables.TypeSpecs.Count + 1)
        {
            throw Invalid(
                "A ForeignNativeInterface function-pointer anchor map has an invalid extent.");
        }

        var structures = LoadBlittableStructures(tables.TypeDefs,
            tables.Fields, tables.Layouts, tables.LayoutFields,
            tables.TargetProfiles, tables.TypeSpecs,
            [.. context.ForeignNativeInterfaceStructures],
            context.ForeignNativeInterfacePointerTypes,
            out structureTypeByTypeDef, imports: tables.Imports,
            includedTypeDefRows: includedTypeDefRows,
            allowDuplicateImportedViews: true,
            existingStructureTypeIndexesByTypeDef:
                existingStructureIndexesByTypeDef,
            functionPointerSignatureIndexByTypeSpecRow:
                anchorTypeSpecMap,
            beforeFieldLoad: structureMap =>
                ReadForeignNativeInterfaceFunctionPointerAnchors(
                    tables, context, moduleRow,
                    context.ForeignNativeInterfacePointerTypes,
                    anchorMethodMap, anchorTypeSpecMap, structureMap,
                    includedTypeDefRows,
                    validateNativePointerTypes: false));
        var structuresWithOrigins = structures.ToBuilder();
        foreach (var typeDefRow in includedTypeDefRows)
        {
            var structureIndex = structureTypeByTypeDef[typeDefRow];
            if ((uint)structureIndex >= (uint)structuresWithOrigins.Count)
            {
                throw Invalid(
                    "A ForeignNativeInterface aggregate TypeDef has no loaded structure identity.");
            }

            structuresWithOrigins[structureIndex]
                = structuresWithOrigins[structureIndex] with {
                    DeclaredCopyEvidence
                        = tables.Schema.SupportsDeclaredCopyEvidence()
                            ? tables.TypeDefs.HasDeclaredCopy(typeDefRow)
                            : null,
                };
        }

        var structureExportKeys = tables.Exports
            .GetExportsForModule(moduleRow)
            .Where(static export => (export.Definition >> 24) == 0x13)
            .GroupBy(static export => export.Definition)
            .ToDictionary(static group => group.Key,
                static group => group.Select(
                        static export => export.ExportKey)
                    .OrderBy(static key => key, StringComparer.Ordinal)
                    .ToImmutableArray());
        var fieldExportKeys = tables.Exports
            .GetExportsForModule(moduleRow)
            .Where(static export => (export.Definition >> 24) == 0x14)
            .GroupBy(static export => export.Definition)
            .ToDictionary(static group => group.Key,
                static group => group.Select(
                        static export => export.ExportKey)
                    .OrderBy(static key => key, StringComparer.Ordinal)
                    .ToImmutableArray());
        BoundSourceTypeProviderSnapshot? structureProviderSnapshot = null;
        foreach (var typeDefRow in includedTypeDefRows)
        {
            var definitionId = 0x1300_0000u
                | checked((uint)typeDefRow);
            if (!structureExportKeys.TryGetValue(definitionId,
                    out var exportKeys)
                || exportKeys.IsEmpty)
            {
                continue;
            }

            var structureIndex = structureTypeByTypeDef[typeDefRow];
            var structure = structuresWithOrigins[structureIndex];
            var providerSnapshot = structureProviderSnapshot
                ??= CreateA2SourceTypeProviderSnapshot(tables,
                    context.Artifacts[artifactIndex],
                    context.SchemaIdentities[artifactIndex], moduleRow);
            var fields = structure.Fields.ToBuilder();
            var (fieldStart, fieldEnd)
                = tables.TypeDefs.GetFieldRange(typeDefRow);
            var layoutRow = tables.Layouts.GetLayoutForProfile(typeDefRow,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision);
            var (firstLayoutField, layoutFieldCount)
                = tables.Layouts.GetLayoutFieldRange(layoutRow);
            if (layoutFieldCount != fields.Count
                || fieldEnd - fieldStart != fields.Count)
            {
                throw Invalid(
                    "A ForeignNativeInterface aggregate layout has inconsistent field ranges.");
            }

            for (var fieldOrdinal = 0; fieldOrdinal < fields.Count;
                fieldOrdinal++)
            {
                var fieldRow = tables.LayoutFields.GetFieldDef(
                    firstLayoutField + fieldOrdinal);
                var fieldDefinitionId = 0x1400_0000u
                    | checked((uint)fieldRow);
                if (!fieldExportKeys.TryGetValue(fieldDefinitionId,
                        out var fieldKeys)
                    || fieldKeys.IsEmpty)
                {
                    continue;
                }

                var field = fields[fieldOrdinal];
                var expectedField = field with {
                    StandardFieldOrigin = null,
                };
                fields[fieldOrdinal] = field with {
                    StandardFieldOrigin = new BoundStandardFieldOrigin(
                        providerSnapshot, fieldDefinitionId, fieldKeys, true,
                        expectedField),
                };
            }

            var expectedDeclaration = structure with {
                Fields = [.. fields.Select(static field => field with {
                    StandardFieldOrigin = null,
                })],
                StandardLayoutOnly = false,
                StandardTypeOrigin = null,
            };
            structuresWithOrigins[structureIndex] = structure with {
                Fields = fields.ToImmutable(),
                StandardTypeOrigin = new BoundStandardTypeOrigin(
                    providerSnapshot, definitionId, exportKeys, true,
                    ReferenceRoot: providerSnapshot,
                    ExpectedDeclaration: expectedDeclaration),
                StandardLayoutOnly = false,
            };
        }

        context.ForeignNativeInterfaceStructures.Clear();
        context.ForeignNativeInterfaceStructures.AddRange(
            structuresWithOrigins);
        _ = context.ForeignNativeInterfaceStructureModulesLoaded.Add(
            moduleKey);
        foreach (var typeDefRow in includedTypeDefRows)
        {
            context.ForeignNativeInterfaceStructureIndexesByTypeDef[
                (artifactIndex, moduleRow, typeDefRow)]
                = structureTypeByTypeDef[typeDefRow];
        }

        if (!NativePointerTypeContract.IsValidTable(
                [.. context.ForeignNativeInterfacePointerTypes],
                [.. context.ForeignNativeInterfaceStructures],
                [.. context.ForeignNativeInterfaceFunctionPointerSignatures]))
        {
            throw Invalid(
                "The selected FNI aggregate contains an invalid pointer or function-pointer type closure.");
        }

        return structuresWithOrigins.ToImmutable();
    }

    private static void BindForeignNativeInterfaceStructureImports(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow,
        HashSet<int> structureImportRows, int[] structureTypeByTypeDef,
        HashSet<int> usedImportRows)
    {
        var localStructureTypeByTypeDef
            = (int[])structureTypeByTypeDef.Clone();
        foreach (var importRow in structureImportRows.Order())
        {
            _ = usedImportRows.Add(importRow);
            var importKey = (artifactIndex, importRow);
            if (!context.ForeignNativeInterfaceStructureImportsBeingBound
                    .Add(importKey))
            {
                continue;
            }

            try
            {
                var definitionId = tables.Imports
                    .GetExpectedDefinitionForImportRow(importRow);
                var expectedTypeDefRow = checked((int)(
                    definitionId & 0x00FF_FFFF));
                var moduleRefRow = tables.Imports
                    .GetModuleRefForImportRow(importRow);
                var selected = tables.Imports.GetBindingForImportRow(
                    importRow);
                if ((definitionId >> 24) != 0x13
                    || tables.ModuleRefs.GetOwnerModule(moduleRefRow)
                        != moduleRow
                    || tables.ModuleRefs.GetRequirements(moduleRefRow)
                        != (selected.TargetRequirement,
                            selected.RuntimePolicy)
                    || selected.TargetRequirement
                        != TrRmModuleTableReader
                            .WindowsX64NativeTargetRequirement
                    || selected.RuntimePolicy != 0
                    || !context.Modules.TryGetValue(
                        selected.ProducerIdentity,
                        out var providerSelection))
                {
                    throw Invalid(
                        "An FNI aggregate TypeDef Import has no exact selected provider.");
                }

                var providerTables
                    = context.GetValidatedForeignNativeInterfaceArtifact(
                        providerSelection.ArtifactIndex);
                var providerModuleRow = providerSelection.ModuleRow;
                if (providerTables.Modules.GetRequirements(
                        providerModuleRow)
                        != (selected.TargetRequirement,
                            selected.RuntimePolicy)
                    || !providerTables.Exports.TryGetDefinitionForKey(
                        providerModuleRow, selected.ExportKey,
                        out var providerDefinitionId)
                    || (providerDefinitionId >> 24) != 0x13)
                {
                    throw Invalid(
                        "The selected FNI aggregate provider requirements or ExportKey changed.");
                }

                var providerTypeDefRow = checked((int)(
                    providerDefinitionId & 0x00FF_FFFF));
                if (providerTables.TypeDefs.GetKind(providerTypeDefRow) != 1
                    || providerTables.TypeDefs.GetOwnerModule(
                        providerTypeDefRow) != providerModuleRow)
                {
                    throw Invalid(
                        "The selected FNI aggregate provider ExportKey does not select a local structure TypeDef.");
                }
                var providerIncludedTypeDefRows
                    = GetForeignNativeInterfaceStructureTypeDefClosure(
                        providerTables, providerModuleRow,
                        [providerTypeDefRow],
                        out var providerStructureImportRows);
                _ = LoadForeignNativeInterfaceStructures(providerTables,
                    context, providerSelection.ArtifactIndex,
                    providerModuleRow, providerIncludedTypeDefRows,
                    out var providerStructureTypeByTypeDef);
                var providerStructureIndex
                    = providerStructureTypeByTypeDef[providerTypeDefRow];
                var expectedStructureIndex
                    = structureTypeByTypeDef[expectedTypeDefRow];
                if (expectedStructureIndex < 0
                    || providerStructureIndex < 0)
                {
                    throw Invalid(
                        "An FNI aggregate Import does not map to a selected Blittable layout.");
                }

                structureTypeByTypeDef[expectedTypeDefRow]
                    = providerStructureIndex;
                context.ForeignNativeInterfaceStructureIndexesByTypeDef[
                    (artifactIndex, moduleRow, expectedTypeDefRow)]
                    = providerStructureIndex;
                BindForeignNativeInterfaceStructureImports(providerTables,
                    context, providerSelection.ArtifactIndex,
                    providerModuleRow, providerStructureImportRows,
                    providerStructureTypeByTypeDef, []);
                if (!ForeignNativeInterfaceStructuresEqual(
                        expectedStructureIndex, providerStructureIndex,
                        context))
                {
                    throw Invalid(
                        "The selected FNI aggregate provider changes the retained layout or field contract.");
                }

                if (context.Schema
                    .UsesForeignNativeInterfaceAggregateOutputConsumption())
                {
                    if (!TrRmFeatureManifestValidator.Requires(
                            providerTables.Features, providerTables.Modules,
                            providerModuleRow,
                            TrRmFeatureTableReader.LayoutQueriesFeatureId,
                            providerTables.Schema))
                    {
                        throw Invalid(
                            "An AC1 aggregate provider Module omits LayoutQueries.");
                    }

                    foreach (var closureTypeDef in providerIncludedTypeDefRows)
                    {
                        var closureModule = providerTables.TypeDefs
                            .GetOwnerModule(closureTypeDef);
                        if (!TrRmFeatureManifestValidator.Requires(
                                providerTables.Features,
                                providerTables.Modules, closureModule,
                                TrRmFeatureTableReader.LayoutQueriesFeatureId,
                                providerTables.Schema))
                        {
                            throw Invalid(
                                "An AC1 aggregate provider closure omits LayoutQueries.");
                        }
                    }

                    if (tables.TypeDefs.HasDeclaredCopy(expectedTypeDefRow))
                    {
                        var providerLayout
                            = TrRmFixedArrayLayoutValidator
                                .CalculateTypeDefLayoutAndTypeDefRows(
                                    providerTables, providerTypeDefRow,
                                    rejectRawUnion: true);
                        foreach (var closureTypeDef in
                            providerLayout.TypeDefRows)
                        {
                            var closureModule = providerTables.TypeDefs
                                .GetOwnerModule(closureTypeDef);
                            if (!TrRmFeatureManifestValidator.Requires(
                                    providerTables.Features,
                                    providerTables.Modules, closureModule,
                                    TrRmFeatureTableReader
                                        .RootlessNativeValuesFeatureId,
                                    providerTables.Schema)
                                || !TrRmFeatureManifestValidator.Requires(
                                    providerTables.Features,
                                    providerTables.Modules, closureModule,
                                    TrRmFeatureTableReader
                                        .LayoutQueriesFeatureId,
                                    providerTables.Schema))
                            {
                                throw Invalid(
                                    "An AC1 Copy-positive provider closure omits its required Features.");
                            }
                        }
                    }
                }

                if (expectedStructureIndex != providerStructureIndex)
                {
                    var expectedStructure
                        = context.ForeignNativeInterfaceStructures[
                            expectedStructureIndex];
                    var providerOrigin
                        = context.ForeignNativeInterfaceStructures[
                            providerStructureIndex].StandardTypeOrigin
                        ?? throw Invalid(
                            "The selected FNI aggregate provider has no retained TypeDef origin.");
                    var expectedDeclaration = expectedStructure with {
                        Fields = [.. expectedStructure.Fields.Select(
                            static field => field with {
                                StandardFieldOrigin = null,
                            })],
                        StandardTypeOrigin = null,
                        StandardLayoutOnly = false,
                    };
                    context.ForeignNativeInterfaceStructures[
                        expectedStructureIndex] = expectedStructure with {
                        StandardTypeOrigin = providerOrigin with {
                            IsLookupRoot = false,
                            ExpectedDeclaration = expectedDeclaration,
                            SelectedExportKeys = [selected.ExportKey],
                        },
                        StandardLayoutOnly = true,
                    };
                }

            }
            finally
            {
                _ = context.ForeignNativeInterfaceStructureImportsBeingBound
                    .Remove(importKey);
            }
        }

        var structureIndexMap = new Dictionary<int, int>();
        for (var typeDefRow = 1;
            typeDefRow < structureTypeByTypeDef.Length; typeDefRow++)
        {
            var localStructureIndex
                = localStructureTypeByTypeDef[typeDefRow];
            var selectedStructureIndex
                = structureTypeByTypeDef[typeDefRow];
            if (localStructureIndex >= 0
                && selectedStructureIndex >= 0)
            {
                structureIndexMap[localStructureIndex]
                    = selectedStructureIndex;
            }
        }

        foreach (var selectedStructureIndex
            in structureTypeByTypeDef.Where(static index => index >= 0)
                .Distinct())
        {
            var structure = context.ForeignNativeInterfaceStructures[
                selectedStructureIndex];
            var fields = structure.Fields.ToBuilder();
            var changed = false;
            for (var fieldOrdinal = 0; fieldOrdinal < fields.Count;
                fieldOrdinal++)
            {
                var fieldType = fields[fieldOrdinal].Type;
                var typeValue = (int)fieldType;
                if (typeValue is >= 100 and < ManagedClassTypes.First
                    && structureIndexMap.TryGetValue(typeValue - 100,
                        out var mappedStructureIndex)
                    && mappedStructureIndex != typeValue - 100)
                {
                    fields[fieldOrdinal] = fields[fieldOrdinal] with {
                        Type = (PrimitiveType)(100 + mappedStructureIndex),
                    };
                    changed = true;
                }
            }

            if (changed)
            {
                context.ForeignNativeInterfaceStructures[
                    selectedStructureIndex] = structure with {
                    Fields = fields.ToImmutable(),
                };
            }
        }
    }

    private static bool ForeignNativeInterfaceStructuresEqual(
        int expectedStructureIndex, int providerStructureIndex,
        GenericMethodReadContext context)
    {
        var structures = context.ForeignNativeInterfaceStructures;
        var pointerTypes = context.ForeignNativeInterfacePointerTypes;
        var functionPointerSignatures
            = context.ForeignNativeInterfaceFunctionPointerSignatures;
        var pendingStructures = new Queue<(int Expected, int Provider)>();
        var comparedStructures = new HashSet<(int Expected, int Provider)>();
        var comparedTypes = new HashSet<(PrimitiveType Expected,
            PrimitiveType Provider)>();
        pendingStructures.Enqueue((expectedStructureIndex,
            providerStructureIndex));

        bool FunctionPointerSignaturesEqual(int expectedIndex,
            int providerIndex)
        {
            if ((uint)expectedIndex >= (uint)functionPointerSignatures.Count
                || (uint)providerIndex
                    >= (uint)functionPointerSignatures.Count)
            {
                return false;
            }

            var expectedSignature
                = functionPointerSignatures[expectedIndex];
            var providerSignature
                = functionPointerSignatures[providerIndex];
            if (expectedSignature.ParameterTypes.IsDefault
                || providerSignature.ParameterTypes.IsDefault
                || expectedSignature.ParameterTypes.Length
                    != providerSignature.ParameterTypes.Length
                || !TypesEqual(expectedSignature.ReturnType,
                    providerSignature.ReturnType))
            {
                return false;
            }

            for (var ordinal = 0;
                ordinal < expectedSignature.ParameterTypes.Length; ordinal++)
            {
                if (!TypesEqual(
                        expectedSignature.ParameterTypes[ordinal],
                        providerSignature.ParameterTypes[ordinal]))
                {
                    return false;
                }
            }

            return NativeFunctionPointerContract.AreEquivalent(
                expectedSignature with {
                    ReturnType = providerSignature.ReturnType,
                    ParameterTypes = providerSignature.ParameterTypes,
                }, providerSignature);
        }

        bool TypesEqual(PrimitiveType expected, PrimitiveType provider)
        {
            if (expected == provider)
            {
                return true;
            }
            if (!comparedTypes.Add((expected, provider)))
            {
                return true;
            }

            if (NativePointerTypes.IsType(expected)
                || NativePointerTypes.IsType(provider))
            {
                if (!NativePointerTypes.IsType(expected)
                    || !NativePointerTypes.IsType(provider))
                {
                    return false;
                }

                var expectedPointerIndex
                    = NativePointerTypes.Index(expected);
                var providerPointerIndex
                    = NativePointerTypes.Index(provider);
                if ((uint)expectedPointerIndex >= (uint)pointerTypes.Count
                    || (uint)providerPointerIndex
                        >= (uint)pointerTypes.Count)
                {
                    return false;
                }

                var expectedPointer = pointerTypes[expectedPointerIndex];
                var providerPointer = pointerTypes[providerPointerIndex];
                return expectedPointer.IsWritable
                        == providerPointer.IsWritable
                    && TypesEqual(expectedPointer.PointeeType,
                        providerPointer.PointeeType);
            }

            var expectedTypeValue = (int)expected;
            var providerTypeValue = (int)provider;
            if (expectedTypeValue is >= 100
                    and < ManagedClassTypes.First
                || providerTypeValue is >= 100
                    and < ManagedClassTypes.First)
            {
                if (expectedTypeValue is not (>= 100
                        and < ManagedClassTypes.First)
                    || providerTypeValue is not (>= 100
                        and < ManagedClassTypes.First))
                {
                    return false;
                }

                pendingStructures.Enqueue((expectedTypeValue - 100,
                    providerTypeValue - 100));
                return true;
            }

            if (NativeFunctionPointerTypes.IsType(expected)
                || NativeFunctionPointerTypes.IsType(provider))
            {
                return NativeFunctionPointerTypes.IsType(expected)
                    && NativeFunctionPointerTypes.IsType(provider)
                    && FunctionPointerSignaturesEqual(
                        NativeFunctionPointerTypes.Index(expected),
                        NativeFunctionPointerTypes.Index(provider));
            }

            return false;
        }

        while (pendingStructures.TryDequeue(out var pair))
        {
            if (!comparedStructures.Add(pair))
            {
                continue;
            }
            if ((uint)pair.Expected >= (uint)structures.Count
                || (uint)pair.Provider >= (uint)structures.Count)
            {
                return false;
            }

            var expected = structures[pair.Expected];
            var provider = structures[pair.Provider];
            if (expected.Size != provider.Size
                || expected.Fields.Length != provider.Fields.Length
                || expected.IsPrivate != provider.IsPrivate
                || expected.IsInternal != provider.IsInternal
                || (expected.DeclaredCopyEvidence == true
                    && provider.DeclaredCopyEvidence != true)
                || expected.IsUnique != provider.IsUnique
                || expected.CleanupMethod != provider.CleanupMethod
                || expected.IsSequential != provider.IsSequential
                || expected.IsTransparent != provider.IsTransparent
                || expected.Packing != provider.Packing
                || expected.DeclaredAlignment != provider.DeclaredAlignment
                || expected.Alignment != provider.Alignment
                || expected.RequiredAlignment
                    != provider.RequiredAlignment
                || expected.BlittableTargetRequirement
                    != provider.BlittableTargetRequirement)
            {
                return false;
            }

            for (var fieldOrdinal = 0;
                fieldOrdinal < expected.Fields.Length; fieldOrdinal++)
            {
                var expectedField = expected.Fields[fieldOrdinal];
                var providerField = provider.Fields[fieldOrdinal];
                if (expectedField.Name != providerField.Name
                    || expectedField.Offset != providerField.Offset
                    || expectedField.Size != providerField.Size
                    || expectedField.IsMutable != providerField.IsMutable
                    || expectedField.IsPrivate
                        != providerField.IsPrivate
                    || expectedField.IsInternal
                        != providerField.IsInternal
                    || expectedField.RequiresUnsafeCaller
                        != providerField.RequiresUnsafeCaller
                    || !TypesEqual(expectedField.Type,
                        providerField.Type))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static BoundInterfaceDefinition
        BindExpectedForeignNativeInterfaceDefinition(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow,
        int interfaceDef, int typeDef, int typeImportRow,
        GenericInterfaceImportRows importRows,
        HashSet<int> usedImportRows, int foreignInterfaceRow,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] signatureIndexByAnchorMethod,
        int[] signatureIndexByAnchorTypeSpec,
        int[] structureTypeByTypeDef, int firstMethod, int methodCount)
    {
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
                "A ForeignNativeInterface TypeDef expectation has no selected provider.");
        }

        var providerModuleRow = providerSelection.ModuleRow;
        var providerArtifactIndex = providerSelection.ArtifactIndex;
        var providerRequirements = context.ModuleRequirements[
            (providerArtifactIndex, providerModuleRow)];
        if (providerRequirements.TargetRequirement
                != selectedType.TargetRequirement
            || providerRequirements.RuntimePolicy
                != selectedType.RuntimePolicy)
        {
            throw Invalid(
                "The selected F+I TypeDef provider does not match the retained Import.");
        }

        var providerKey = (providerSelection.ArtifactIndex,
            providerModuleRow, selectedType.ExportKey);
        if (!context.ExportedInterfaceDefinitionsByKey.TryGetValue(
                providerKey, out var providerInterface))
        {
            var providerTables = TrRmStructuralMetadataValidator
                .GetValidatedForeignNativeInterfaceArtifact(
                    providerArtifactIndex);
            _ = CreateGenericInterfaceDefinitions(providerTables, context,
                providerArtifactIndex, providerModuleRow);
        }

        if (!context.ExportedInterfaceDefinitionsByKey.TryGetValue(
                providerKey, out providerInterface))
        {
            throw Invalid(
                "The selected F+I TypeDef provider has no exported interface declaration.");
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
                "The selected F+I interface provider identity changed.");
        }

        if (!context.ForeignNativeInterfaceMembersByExportKey.TryGetValue(
                providerKey, out var providerMembersByKey))
        {
            providerMembersByKey = new Dictionary<string,
                BoundInterfaceMemberDefinition>(
                    providerInterface.Members.Length,
                    StringComparer.Ordinal);
            foreach (var member in providerInterface.Members)
            {
                if (!providerMembersByKey.TryAdd(member.DeclarationKey,
                        member))
                {
                    throw Invalid(
                        "The selected F+I provider interface has duplicate method ExportKeys.");
                }

                foreach (var alias in member.AdditionalDeclarationKeys)
                {
                    if (!providerMembersByKey.TryAdd(alias, member))
                    {
                        throw Invalid(
                            "The selected F+I provider interface has conflicting method aliases.");
                    }
                }
            }

            context.ForeignNativeInterfaceMembersByExportKey.Add(
                providerKey, providerMembersByKey);
        }

        var methodExportKeys
            = new ImmutableArray<string>[methodCount];
        var selectedMethodKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var methodOrdinal = 0; methodOrdinal < methodCount;
            methodOrdinal++)
        {
            var interfaceMethod = firstMethod + methodOrdinal;
            if (tables.InterfaceMethods.GetOwnerInterfaceDef(interfaceMethod)
                    != interfaceDef
                || tables.InterfaceMethods.GetOrdinal(interfaceMethod)
                    != methodOrdinal)
            {
                throw Invalid(
                    "A ForeignNativeInterface InterfaceMethod expectation is not declaration-ordered.");
            }

            var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                interfaceMethod);
            var methodId = 0x1600_0000u | checked((uint)methodRow);
            if (!importRows.MethodRows.TryGetValue(methodId,
                    out var methodImportRows)
                || methodImportRows.Count == 0)
            {
                throw Invalid(
                    "A ForeignNativeInterface InterfaceMethod has no MethodDef Import row.");
            }

            var exportsForMethod = ImmutableArray.CreateBuilder<string>(
                methodImportRows.Count);
            BoundInterfaceMemberDefinition? selectedMember = null;
            foreach (var methodImportRow in methodImportRows)
            {
                if (!usedImportRows.Add(methodImportRow))
                {
                    throw Invalid(
                        "A ForeignNativeInterface MethodDef Import is shared by multiple members.");
                }

                var methodModuleRef
                    = tables.Imports.GetModuleRefForImportRow(
                        methodImportRow);
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
                    || !selectedMethodKeys.Add(selectedMethod.ExportKey)
                    || !providerMembersByKey.TryGetValue(
                        selectedMethod.ExportKey, out var providerMember)
                    || providerMember.Kind
                        != BoundInterfaceMemberKind.Callable
                    || providerMember.Ordinal != methodOrdinal
                    || (selectedMember is not null
                        && selectedMember.Ordinal != providerMember.Ordinal))
                {
                    throw Invalid(
                        "A ForeignNativeInterface MethodDef Import selects a different or ambiguous provider member.");
                }

                selectedMember = providerMember;
                exportsForMethod.Add(selectedMethod.ExportKey);
            }

            methodExportKeys[methodOrdinal] = [.. exportsForMethod
                .OrderBy(static key => key, StringComparer.Ordinal)];
        }

        var interfaceNamespace = tables.TypeDefs.GetNamespace(typeDef);
        var interfaceName = tables.TypeDefs.GetName(typeDef);
        var qualifiedName = interfaceNamespace.Length == 0
            ? interfaceName
            : string.Concat(interfaceNamespace, ".", interfaceName);
        var expected = ReadForeignNativeInterfaceDefinition(tables,
            context, interfaceDef, typeDef, foreignInterfaceRow,
            qualifiedName, selectedType.ExportKey,
            selectedType.TargetRequirement, selectedType.RuntimePolicy,
            methodExportKeys, nativePointerTypeDefinitions,
            signatureIndexByAnchorMethod,
            signatureIndexByAnchorTypeSpec, structureTypeByTypeDef,
            selectedType.ProducerIdentity);
        ValidateForeignNativeInterfaceProviderContract(expected,
            providerInterface, providerMembersByKey);
        return providerInterface with {
            ForeignNativeInterfaceExpectedViews = [
                new BoundForeignNativeInterfaceExpectedView(typeDef,
                    expected),
            ],
        };
    }

    private static void ValidateForeignNativeInterfaceProviderContract(
        BoundInterfaceDefinition expected, BoundInterfaceDefinition provider,
        Dictionary<string, BoundInterfaceMemberDefinition>
            providerMembersByKey)
    {
        if (expected.ModuleImport != provider.ModuleImport
            || expected.ForeignNativeInterface
                != provider.ForeignNativeInterface
            || expected.IsPublic != provider.IsPublic
            || expected.Accessibility != provider.Accessibility
            || !expected.Bases.IsEmpty || !provider.Bases.IsEmpty
            || !expected.Formals.IsEmpty || !provider.Formals.IsEmpty
            || expected.Members.Length != provider.Members.Length)
        {
            throw Invalid(
                "The selected F+I provider changes the retained owner contract.");
        }

        foreach (var expectedMember in expected.Members)
        {
            if (!providerMembersByKey.TryGetValue(expectedMember.DeclarationKey,
                    out var providerMember)
                || expectedMember.Ordinal != providerMember.Ordinal
                || expectedMember.Kind != providerMember.Kind
                || expectedMember.IsStatic != providerMember.IsStatic
                || expectedMember.IsExternal != providerMember.IsExternal
                || expectedMember.RequiresUnsafeCaller
                    != providerMember.RequiresUnsafeCaller
                || expectedMember.DeclaredRequiresUnsafeCaller
                    != providerMember.DeclaredRequiresUnsafeCaller
                || expectedMember.DoesNotReturn
                    != providerMember.DoesNotReturn
                || expectedMember.RequiresMutableReceiver
                    != providerMember.RequiresMutableReceiver
                || expectedMember.NativeCallableConvention
                    != providerMember.NativeCallableConvention
                || !AreEquivalentG1InterfaceTypes(expectedMember.ResultType,
                    providerMember.ResultType)
                || expectedMember.Parameters.Length
                    != providerMember.Parameters.Length
                || !ForeignNativeInterfaceDependenciesEqual(
                    expectedMember.Dependencies,
                    providerMember.Dependencies)
                || !ForeignNativeInterfaceOutputsEqual(
                    expectedMember.NativeOutputContracts,
                    providerMember.NativeOutputContracts))
            {
                throw Invalid(
                    "The selected F+I provider changes a retained InterfaceMethod contract.");
            }

            for (var ordinal = 0; ordinal < expectedMember.Parameters.Length;
                ordinal++)
            {
                var expectedParameter = expectedMember.Parameters[ordinal];
                var providerParameter = providerMember.Parameters[ordinal];
                if (expectedParameter.Mode != providerParameter.Mode
                    || !AreEquivalentG1InterfaceTypes(
                        expectedParameter.Type, providerParameter.Type))
                {
                    throw Invalid(
                        "The selected F+I provider changes an ordered parameter ABI contract.");
                }
            }
        }
    }

    private static bool ForeignNativeInterfaceDependenciesEqual(
        ImmutableArray<BoundDependencyContract> expected,
        ImmutableArray<BoundDependencyContract> provider)
    {
        if (expected.IsDefault || provider.IsDefault
            || expected.Length != provider.Length)
        {
            return false;
        }

        var expectedContracts = expected.OrderBy(
                static contract => contract.Destination)
            .ThenBy(static contract => contract.DestinationOrdinal ?? -1)
            .ThenBy(static contract => contract.Relation)
            .ToArray();
        var providerContracts = provider.OrderBy(
                static contract => contract.Destination)
            .ThenBy(static contract => contract.DestinationOrdinal ?? -1)
            .ThenBy(static contract => contract.Relation)
            .ToArray();
        for (var index = 0; index < expectedContracts.Length; index++)
        {
            var left = expectedContracts[index];
            var right = providerContracts[index];
            if (left.Relation != right.Relation
                || left.Destination != right.Destination
                || left.DestinationOrdinal != right.DestinationOrdinal
                || left.Sources.IsDefault || right.Sources.IsDefault
                || !left.Sources.OrderBy(static source => source.Kind)
                    .ThenBy(static source => source.FormalOrdinal ?? -1)
                    .SequenceEqual(right.Sources.OrderBy(
                        static source => source.Kind).ThenBy(
                        static source => source.FormalOrdinal ?? -1)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ForeignNativeInterfaceOutputsEqual(
        ImmutableArray<NativeOutputContract> expected,
        ImmutableArray<NativeOutputContract> provider)
    {
        if (expected.IsDefault || provider.IsDefault
            || expected.Length != provider.Length)
        {
            return false;
        }

        var expectedOutputs = expected.OrderBy(
            static output => output.FormalOrdinal).ToArray();
        var providerOutputs = provider.OrderBy(
            static output => output.FormalOrdinal).ToArray();
        for (var index = 0; index < expectedOutputs.Length; index++)
        {
            var left = expectedOutputs[index];
            var right = providerOutputs[index];
            if (left.FormalOrdinal != right.FormalOrdinal
                || left.PointeeType != right.PointeeType
                || left.Formation != right.Formation
                || left.PublicationPhase != right.PublicationPhase
                || left.SuccessSelector != right.SuccessSelector
                || left.SuccessState != right.SuccessState
                || left.ComplementSelector != right.ComplementSelector
                || left.ComplementState != right.ComplementState
                || left.Dependencies.IsDefault || right.Dependencies.IsDefault
                || !left.Dependencies.SequenceEqual(right.Dependencies))
            {
                return false;
            }
        }

        return true;
    }

    private static void ReadForeignNativeInterfaceFunctionPointerAnchors(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int moduleRow,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] signatureIndexByAnchorMethod,
        int[] signatureIndexByAnchorTypeSpec,
        int[] structureTypeByTypeDef,
        HashSet<int> includedTypeDefRows,
        bool validateNativePointerTypes = true)
    {
        var typeStates = new byte[tables.TypeSpecs.Count + 1];
        var anchorStates = new byte[tables.TypeSpecs.Count + 1];

        void EnsureTypeSpec(uint typeSpecRef, int depth)
        {
            if (depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "The ForeignNativeInterface function-pointer type closure exceeds the supported depth.");
            }

            var reference = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
            if (reference.IsRow && reference.Form == 0x0D)
            {
                EnsureAnchor(reference.Row, depth + 1);
                return;
            }

            if (!tables.TypeSpecs.TryGetNativePointerPointeeTypeSpecRef(
                    typeSpecRef, out var pointeeTypeSpecRef)
                || !tables.TypeSpecs.GetTypeSpecRef(typeSpecRef).IsRow)
            {
                return;
            }

            var row = reference.Row;
            if (typeStates[row] == 1)
            {
                throw Invalid(
                    "A ForeignNativeInterface function-pointer TypeSpec is cyclic.");
            }
            if (typeStates[row] == 2)
            {
                return;
            }

            typeStates[row] = 1;
            EnsureTypeSpec(pointeeTypeSpecRef, depth + 1);
            typeStates[row] = 2;
        }

        void EnsureAnchor(int typeSpecRow, int depth)
        {
            if (depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "The ForeignNativeInterface function-pointer anchor graph exceeds the supported depth.");
            }

            if (anchorStates[typeSpecRow] == 1)
            {
                throw Invalid(
                    "A ForeignNativeInterface function-pointer anchor is cyclic.");
            }
            if (anchorStates[typeSpecRow] == 2)
            {
                return;
            }

            anchorStates[typeSpecRow] = 1;
            var methodRow = tables.NativeFunctionPointerAnchors
                .GetAnchorMethodDefRow(typeSpecRow);
            var signatureRow = tables.Methods.GetSignature(methodRow);
            var (firstParameter, parameterCount)
                = tables.Signatures.GetParamRange(signatureRow);
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                EnsureTypeSpec(tables.Parameters.GetTypeSpec(
                    firstParameter + ordinal), depth + 1);
            }
            EnsureTypeSpec(tables.Signatures.GetResultTypeSpec(
                signatureRow), depth + 1);

            var parameterTypes = ReadForeignInvocationParameterTypes(
                methodRow, tables.Methods, tables.Signatures,
                tables.Parameters, tables.TypeSpecs,
                nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports: tables.Imports,
                signatureIndexByAnchorTypeSpec:
                    signatureIndexByAnchorTypeSpec);
            var resultType = ReadForeignInvocationType(tables.TypeSpecs,
                tables.Signatures.GetResultTypeSpec(signatureRow),
                nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports: tables.Imports,
                signatureIndexByAnchorTypeSpec:
                    signatureIndexByAnchorTypeSpec);
            var predicateContracts
                = tables.Contracts.GetPredicateGraphContracts(methodRow);
            var selectors
                = ImmutableArray.CreateBuilder<
                    NativeFunctionPointerSelector>(
                    predicateContracts.Length);
            var selectorOrdinalByGraphRow
                = new Dictionary<int, uint>(predicateContracts.Length);
            foreach (var predicate in predicateContracts)
            {
                if (predicate.PhaseTag != 3)
                {
                    throw Invalid(
                        "A ForeignNativeInterface function-pointer anchor contains an unsupported predicate phase.");
                }

                var rootNode = tables.PredicateGraphs.GetRootNodeRow(
                    predicate.PredicateGraphRow);
                var nodeKind = tables.PredicateNodes.GetNodeKind(rootNode);
                if (nodeKind > byte.MaxValue
                    || !selectorOrdinalByGraphRow.TryAdd(
                        predicate.PredicateGraphRow,
                        predicate.PhaseLocalOrdinal))
                {
                    throw Invalid(
                        "A ForeignNativeInterface function-pointer selector is malformed.");
                }

                selectors.Add(new NativeFunctionPointerSelector(
                    predicate.PhaseTag, predicate.PhaseLocalOrdinal,
                    checked((byte)nodeKind)));
            }

            var outputStates
                = ImmutableArray.CreateBuilder<
                    NativeFunctionPointerOutputState>();
            foreach (var outputState in tables.Contracts
                         .GetOutputStateContracts(methodRow))
            {
                if (!selectorOrdinalByGraphRow.TryGetValue(
                        outputState.PredicateGraphRow,
                        out var selectorOrdinal))
                {
                    throw Invalid(
                        "A ForeignNativeInterface function-pointer output state has no selector.");
                }

                outputStates.Add(new NativeFunctionPointerOutputState(
                    checked((uint)outputState.ParameterOrdinal),
                    selectorOrdinal, outputState.State));
            }

            var nativeParameters = tables.Contracts.GetNativeParameters(
                methodRow);
            if (!nativeParameters.IsEmpty)
            {
                throw Unsupported(
                    "Standard FNI function-pointer anchors do not admit NativeParameter.");
            }

            var signature = new NativeFunctionPointerSignatureDefinition(
                (NativeCallingConvention)tables.NativeFunctionPointerAnchors
                    .GetCallingConvention(typeSpecRow),
                resultType, parameterTypes) {
                EffectsMask = tables.Contracts.GetEffectsMask(methodRow),
                Selectors = selectors.MoveToImmutable(),
                OutputStates = outputStates.MoveToImmutable(),
                Dependencies = [.. tables.Contracts
                    .GetDependencyContracts(methodRow)
                    .Select(static dependency =>
                        new NativeFunctionPointerDependency(
                            dependency.DestinationKind,
                            dependency.SourceKind,
                            dependency.Relationship,
                            dependency.Phase,
                            dependency.DestinationOrdinal,
                            dependency.SourceOrdinal))],
            };

            var signatureIndex = -1;
            for (var index = 0;
                index < context
                    .ForeignNativeInterfaceFunctionPointerSignatures.Count;
                index++)
            {
                if (NativeFunctionPointerContract.AreEquivalent(
                        context
                            .ForeignNativeInterfaceFunctionPointerSignatures[
                                index], signature))
                {
                    signatureIndex = index;
                    break;
                }
            }

            if (signatureIndex < 0)
            {
                var candidateSignatures = context
                    .ForeignNativeInterfaceFunctionPointerSignatures
                    .Append(signature).ToImmutableArray();
                if (!NativeFunctionPointerContract.IsValidTable(
                        candidateSignatures)
                    || (validateNativePointerTypes
                        && !NativePointerTypeContract.IsValidTable(
                            [.. nativePointerTypeDefinitions],
                            [.. context
                                .ForeignNativeInterfaceStructures],
                            candidateSignatures)))
                {
                    throw Invalid(
                        "A ForeignNativeInterface function-pointer anchor has an invalid signature.");
                }

                signatureIndex = context
                    .ForeignNativeInterfaceFunctionPointerSignatures.Count;
                context.ForeignNativeInterfaceFunctionPointerSignatures.Add(
                    signature);
            }

            signatureIndexByAnchorTypeSpec[typeSpecRow] = signatureIndex;
            if (signatureIndexByAnchorMethod[methodRow] == -1)
            {
                signatureIndexByAnchorMethod[methodRow] = signatureIndex;
            }
            else if (signatureIndexByAnchorMethod[methodRow] != signatureIndex)
            {
                signatureIndexByAnchorMethod[methodRow] = -2;
            }
            anchorStates[typeSpecRow] = 2;
        }

        var foreignInterfaceOwners = new HashSet<int>(
            tables.ForeignNativeInterfaces.Count);
        for (var foreignInterfaceRow = 1;
            foreignInterfaceRow <= tables.ForeignNativeInterfaces.Count;
            foreignInterfaceRow++)
        {
            var interfaceDef = checked((int)tables.ForeignNativeInterfaces
                .GetOwnerInterfaceDef(foreignInterfaceRow));
            var typeDef = tables.Interfaces.GetOwnerTypeDef(interfaceDef);
            if (tables.TypeDefs.GetOwnerModule(typeDef) == moduleRow)
            {
                _ = foreignInterfaceOwners.Add(interfaceDef);
            }
        }

        var (typeDefStart, typeDefEnd)
            = tables.Modules.GetTypeDefRange(moduleRow);
        for (var typeDef = typeDefStart; typeDef < typeDefEnd; typeDef++)
        {
            var interfaceDef
                = tables.Interfaces.GetInterfaceDefForTypeDef(typeDef);
            if (interfaceDef == 0
                || !foreignInterfaceOwners.Contains(interfaceDef))
            {
                continue;
            }

            var (firstMethod, methodCount)
                = tables.Interfaces.GetMethodRange(interfaceDef);
            for (var methodOrdinal = 0; methodOrdinal < methodCount;
                methodOrdinal++)
            {
                var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                    firstMethod + methodOrdinal);
                var signatureRow = tables.Methods.GetSignature(methodRow);
                var (firstParameter, parameterCount)
                    = tables.Signatures.GetParamRange(signatureRow);
                for (var ordinal = 0; ordinal < parameterCount; ordinal++)
                {
                    EnsureTypeSpec(tables.Parameters.GetTypeSpec(
                        firstParameter + ordinal), 0);
                }
                EnsureTypeSpec(tables.Signatures.GetResultTypeSpec(
                    signatureRow), 0);
            }
        }

        foreach (var typeDef in includedTypeDefRows)
        {
            var (firstField, endField)
                = tables.TypeDefs.GetFieldRange(typeDef);
            for (var fieldRow = firstField; fieldRow < endField; fieldRow++)
            {
                EnsureTypeSpec(tables.Fields.GetTypeSpecRef(fieldRow), 0);
            }
        }

        if (context.ForeignNativeInterfaceFunctionPointerSignatures.Count
            != 0
            && !NativeFunctionPointerContract.IsValidTable(context
                .ForeignNativeInterfaceFunctionPointerSignatures.ToImmutableArray()))
        {
            throw Invalid(
                "The selected FNI modules contain an invalid function-pointer signature table.");
        }
    }

    internal static HashSet<int>
        GetForeignNativeInterfaceStructureTypeDefClosure(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int moduleRow, IReadOnlyList<int>? rootTypeDefRows,
        out HashSet<int> structureImportRows,
        IReadOnlyList<int>? selectedInterfaceDefs = null)
    {
        var typeDefStates = new Dictionary<int, byte>();
        var typeSpecStates = new Dictionary<int, byte>();
        var typeSpecExportStates = new Dictionary<int, byte>();
        var methodStates = new Dictionary<int, byte>();
        var includedTypeDefRows = new HashSet<int>();
        structureImportRows = [];
        var exportedTypeDefs = tables.Exports.GetExportsForModule(moduleRow)
            .Where(static export => (export.Definition >> 24) == 0x13)
            .Select(static export => export.Definition)
            .ToHashSet();

        void VisitMethodSignature(int methodRow, int depth)
        {
            if (depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "The F+I aggregate TypeSpec closure exceeds the supported depth.");
            }
            if (methodStates.TryGetValue(methodRow, out var methodState)
                && methodState == 1)
            {
                throw Invalid(
                    "The F+I aggregate and function-pointer signature closure is cyclic.");
            }
            if (methodState == 2)
            {
                return;
            }

            methodStates[methodRow] = 1;
            var signatureRow = tables.Methods.GetSignature(methodRow);
            var (firstParameter, parameterCount)
                = tables.Signatures.GetParamRange(signatureRow);
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                VisitTypeSpec(tables.Parameters.GetTypeSpec(
                    firstParameter + ordinal), false, true, depth + 1);
            }
            VisitTypeSpec(tables.Signatures.GetResultTypeSpec(signatureRow),
                false, true, depth + 1);
            methodStates[methodRow] = 2;
        }

        void VisitTypeDef(int typeDefRow, bool throughPointer,
            bool requireExport, int depth)
        {
            if (depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "The F+I aggregate TypeDef closure exceeds the supported depth.");
            }

            if (tables.TypeDefs.GetKind(typeDefRow) != 1)
            {
                return;
            }

            if (tables.TypeDefs.GetOwnerModule(typeDefRow) != moduleRow)
            {
                throw Invalid(
                    "An F+I aggregate TypeDef is referenced without a consumer-local Import.");
            }

            var definitionId = 0x1300_0000u
                | checked((uint)typeDefRow);
            var isExported = exportedTypeDefs.Contains(definitionId);
            var structureImportRow = 0;
            for (var importRow = 1; importRow <= tables.Imports.Count;
                importRow++)
            {
                if (tables.Imports.GetExpectedDefinitionForImportRow(
                        importRow) != definitionId)
                {
                    continue;
                }

                var moduleRefRow = tables.Imports
                    .GetModuleRefForImportRow(importRow);
                if (tables.ModuleRefs.GetOwnerModule(moduleRefRow)
                    != moduleRow)
                {
                    continue;
                }
                if (structureImportRow != 0)
                {
                    throw Invalid(
                        "An F+I aggregate TypeDef has duplicate Import expectations.");
                }

                structureImportRow = importRow;
            }

            if (requireExport)
            {
                if ((tables.TypeDefs.GetFlags(typeDefRow) & 3) != 2)
                {
                    throw Invalid(
                        "A ForeignNativeInterface aggregate pointee is not public.");
                }
                if (!isExported && structureImportRow == 0)
                {
                    throw Invalid(
                        "A ForeignNativeInterface aggregate pointee has no local ExportKey or expected Import.");
                }
            }
            if (structureImportRow != 0)
            {
                _ = structureImportRows.Add(structureImportRow);
            }

            if (typeDefStates.TryGetValue(typeDefRow,
                    out var typeDefState)
                && typeDefState == 1)
            {
                if (throughPointer)
                {
                    return;
                }
                throw Invalid(
                    "An F+I inline aggregate TypeDef is recursively defined.");
            }
            if (typeDefState == 2)
            {
                return;
            }
            if (!tables.Layouts.HasBlittableLayout(typeDefRow, 1, 1))
            {
                throw Unsupported(
                    "An F+I aggregate pointee has no selected Windows x64 Blittable Layout.");
            }
            var (_, genericCount)
                = tables.TypeDefs.GetGenericRange(typeDefRow);
            if (genericCount != 0)
            {
                throw Unsupported(
                    "F+I aggregate pointee TypeDefs must be nongeneric.");
            }

            typeDefStates[typeDefRow] = 1;
            _ = includedTypeDefRows.Add(typeDefRow);
            var (firstField, endField)
                = tables.TypeDefs.GetFieldRange(typeDefRow);
            for (var fieldRow = firstField; fieldRow < endField;
                fieldRow++)
            {
                VisitTypeSpec(tables.Fields.GetTypeSpecRef(fieldRow),
                    false, false, depth + 1);
            }
            typeDefStates[typeDefRow] = 2;
        }

        void VisitTypeSpec(uint typeSpecRef, bool throughPointer,
            bool requireExport, int depth)
        {
            if (depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "The F+I aggregate TypeSpec closure exceeds the supported depth.");
            }

            var reference = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
            if (reference.IsRow && reference.Form == 0x0D)
            {
                VisitMethodSignature(tables.NativeFunctionPointerAnchors
                    .GetAnchorMethodDefRow(reference.Row), depth + 1);
                return;
            }

            if (tables.TypeSpecs.TryGetNativePointerPointeeTypeSpecRef(
                    typeSpecRef, out var pointeeTypeSpecRef))
            {
                if (reference.IsRow)
                {
                    if (typeSpecStates.TryGetValue(reference.Row,
                            out var typeSpecState)
                        && typeSpecState == 1)
                    {
                        throw Invalid(
                            "An F+I aggregate pointer TypeSpec is cyclic.");
                    }
                    if (typeSpecState == 2
                        && (!requireExport
                            || typeSpecExportStates.TryGetValue(
                                reference.Row, out var typeSpecExportState)
                                && typeSpecExportState == 2))
                    {
                        return;
                    }
                    typeSpecStates[reference.Row] = 1;
                    VisitTypeSpec(pointeeTypeSpecRef, true, requireExport,
                        depth + 1);
                    typeSpecStates[reference.Row] = 2;
                    if (requireExport)
                    {
                        typeSpecExportStates[reference.Row] = 2;
                    }
                }
                else
                {
                    VisitTypeSpec(pointeeTypeSpecRef, true, requireExport,
                        depth + 1);
                }

                return;
            }

            if (reference.Form is not (0x02 or 0x03 or 0x05))
            {
                return;
            }

            var typeDefRow = tables.TypeSpecs.GetNominalTypeDefRow(
                typeSpecRef, tables.TypeDefs, tables.Imports);
            VisitTypeDef(typeDefRow, throughPointer, requireExport,
                depth + 1);
        }

        void VisitInterface(int interfaceDef)
        {
            var (firstMethod, methodCount)
                = tables.Interfaces.GetMethodRange(interfaceDef);
            for (var methodOrdinal = 0; methodOrdinal < methodCount;
                methodOrdinal++)
            {
                VisitMethodSignature(tables.InterfaceMethods
                    .GetMethodDefRow(firstMethod + methodOrdinal), 0);
            }
        }

        if (selectedInterfaceDefs is not null)
        {
            foreach (var interfaceDef in selectedInterfaceDefs)
            {
                var typeDef = tables.Interfaces.GetOwnerTypeDef(interfaceDef);
                if (tables.TypeDefs.GetOwnerModule(typeDef) != moduleRow)
                {
                    throw Invalid(
                        "A selected ForeignNativeInterface definition belongs to a different Module.");
                }

                VisitInterface(interfaceDef);
            }
        }
        else
        {
            for (var foreignInterfaceRow = 1;
                foreignInterfaceRow <= tables.ForeignNativeInterfaces.Count;
                foreignInterfaceRow++)
            {
                var interfaceDef = checked((int)
                    tables.ForeignNativeInterfaces.GetOwnerInterfaceDef(
                        foreignInterfaceRow));
                var typeDef = tables.Interfaces.GetOwnerTypeDef(interfaceDef);
                if (tables.TypeDefs.GetOwnerModule(typeDef) == moduleRow)
                {
                    VisitInterface(interfaceDef);
                }
            }
        }

        if (rootTypeDefRows is not null)
        {
            foreach (var typeDefRow in rootTypeDefRows)
            {
                VisitTypeDef(typeDefRow, false, true, 0);
            }
        }

        return includedTypeDefRows;
    }

    private static BoundInterfaceDefinition
        ReadForeignNativeInterfaceDefinition(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int interfaceDef, int typeDef,
        int foreignInterfaceRow, string qualifiedName, string typeExportKey,
        uint targetRequirement, uint runtimePolicy,
        ImmutableArray<string>[] methodExportKeys,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] signatureIndexByAnchorMethod,
        int[] signatureIndexByAnchorTypeSpec,
        int[] structureTypeByTypeDef, string moduleIdentity)
    {
        var (fieldStart, fieldEnd)
            = tables.TypeDefs.GetFieldRange(typeDef);
        var (_, genericCount) = tables.TypeDefs.GetGenericRange(typeDef);
        var (_, typeContractCount)
            = tables.TypeDefs.GetContractRange(typeDef);
        if (tables.TypeDefs.GetKind(typeDef) != 3
            || fieldStart != fieldEnd || genericCount != 0
            || typeContractCount != 0
            || tables.Interfaces.GetMethodRange(interfaceDef).Count
                != methodExportKeys.Length
            || tables.ForeignNativeInterfaces.GetTargetProfile(
                foreignInterfaceRow) == 0)
        {
            throw Invalid(
                "A ForeignNativeInterface declaration has an unsupported TypeDef or owner shape.");
        }

        var (firstMethod, methodCount)
            = tables.Interfaces.GetMethodRange(interfaceDef);
        var members = ImmutableArray.CreateBuilder<
            BoundInterfaceMemberDefinition>(methodCount);
        for (var methodOrdinal = 0; methodOrdinal < methodCount;
            methodOrdinal++)
        {
            var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                firstMethod + methodOrdinal);
            var signatureRow = tables.Methods.GetSignature(methodRow);
            var parameterTypes = ReadForeignInvocationParameterTypes(
                methodRow, tables.Methods, tables.Signatures,
                tables.Parameters, tables.TypeSpecs,
                nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports: tables.Imports,
                signatureIndexByAnchorTypeSpec:
                    signatureIndexByAnchorTypeSpec);
            var (firstParameter, parameterCount)
                = tables.Signatures.GetParamRange(signatureRow);
            var parameters = ImmutableArray.CreateBuilder<
                BoundInterfaceParameter>(parameterCount);
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var parameterRow = firstParameter + ordinal;
                var typeSpec = tables.Parameters.GetTypeSpec(parameterRow);
                var parameterType = CreateForeignNativeInterfaceType(
                    tables.TypeSpecs, typeSpec, parameterTypes[ordinal],
                    nativePointerTypeDefinitions);
                BoundConstant? defaultValue = null;
                string parameterName;
                if (tables.Schema
                    .UsesForeignNativeInterfaceParameterSurface())
                {
                    if (!tables.MethodParameterLabels.TryGetNameStringRow(
                            methodRow, checked((uint)ordinal),
                            out var nameStringRow)
                        || !tables.MethodParameterLabels
                            .TryGetDefaultConstantSourceRow(methodRow,
                                checked((uint)ordinal),
                                out var constantSourceRow))
                    {
                        throw Invalid(
                            "A PS1 ForeignNativeInterface parameter has no complete surface row.");
                    }

                    parameterName = tables.Strings.DecodeName(nameStringRow);
                    if (constantSourceRow != 0)
                    {
                        var ownerModule
                            = tables.Methods.GetOwnerModule(methodRow);
                        var (ownerTarget, _)
                            = tables.Modules.GetRequirements(ownerModule);
                        defaultValue
                            = TrRmConstantSourceValueValidator
                                .MaterializeFrozenScalar(
                                    checked((int)constantSourceRow), typeSpec,
                                    ownerModule, ownerTarget,
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
                                "A PS1 ForeignNativeInterface default does not match its parameter type.");
                        }
                    }
                }
                else
                {
                    throw Unsupported(
                        "F+I source-free references cannot expose explicit parameters.");
                }

                parameters.Add(new BoundInterfaceParameter(parameterName,
                    parameterType, BoundInterfaceParameterMode.Value, [], []) {
                    DefaultValue = defaultValue,
                });
            }

            var resultTypeSpec = tables.Signatures.GetResultTypeSpec(
                signatureRow);
            var resultType = ReadForeignInvocationType(tables.TypeSpecs,
                resultTypeSpec, nativePointerTypeDefinitions,
                signatureIndexByAnchorMethod, structureTypeByTypeDef,
                imports: tables.Imports,
                signatureIndexByAnchorTypeSpec:
                    signatureIndexByAnchorTypeSpec);
            var boundResultType = CreateForeignNativeInterfaceType(
                tables.TypeSpecs, resultTypeSpec, resultType,
                nativePointerTypeDefinitions);
            var nativeCallableConvention
                = (NativeCallingConvention)tables.Contracts
                    .GetNativeCallableConvention(methodRow);
            var effects = tables.Contracts.GetEffectsMask(methodRow);
            var nativeOutputs = ReadForeignInvocationNativeOutputs(methodRow,
                parameterTypes, nativePointerTypeDefinitions, tables,
                out var dependencies, allowGeneralDependencies: true,
                methodResultType: resultType);
            if (!nativeOutputs.IsEmpty && (effects & 2) != 0)
            {
                throw Invalid(
                    "A ForeignNativeInterface NativeOutput method does not have a normal return.");
            }

            var keys = methodExportKeys[methodOrdinal];
            members.Add(new BoundInterfaceMemberDefinition(methodOrdinal,
                tables.Methods.GetName(methodRow), keys[0],
                BoundInterfaceMemberKind.Callable,
                BoundInterfaceAccessibility.Public, false, false, false,
                (effects & 4) != 0, (effects & 2) != 0, false, false,
                [], [], [], default, boundResultType, [], [],
                parameters.MoveToImmutable(), dependencies, [], null, null,
                null) {
                RequiresMutableReceiver
                    = (tables.Methods.GetFlags(methodRow) & 4) != 0,
                DeclaredRequiresUnsafeCaller = (effects & 4) != 0,
                NativeCallableConvention = nativeCallableConvention,
                NativeOutputContracts = nativeOutputs,
                AdditionalDeclarationKeys = keys.RemoveAt(0),
            });
        }

        return new BoundInterfaceDefinition(qualifiedName, true, false,
            [], [], [], members.MoveToImmutable()) {
            Accessibility = BoundInterfaceAccessibility.Public,
            ModuleImport = new BoundModuleImport(moduleIdentity,
                typeExportKey, targetRequirement, runtimePolicy),
            ForeignNativeInterface = new BoundForeignNativeInterfaceLayout(
                tables.ForeignNativeInterfaces.GetVTableByteOffset(
                    foreignInterfaceRow)),
        };
    }

    private static BoundInterfaceType CreateForeignNativeInterfaceType(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpec,
        PrimitiveType primitiveType,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions)
    {
        var isWritable = NativePointerTypes.IsType(primitiveType)
            ? nativePointerTypeDefinitions[
                NativePointerTypes.Index(primitiveType)].IsWritable
            : primitiveType == PrimitiveType.NativeRawPointer
                && typeSpecs.GetTypeSpecRef(typeSpec).Qualifiers != 0;
        return new BoundInterfaceType(BoundInterfaceTypeKind.Primitive,
            primitiveType, null, -1, [], isWritable, false, false, false);
    }
}
