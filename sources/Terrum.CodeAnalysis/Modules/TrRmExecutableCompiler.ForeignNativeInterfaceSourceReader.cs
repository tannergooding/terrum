// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
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
        var structureTypeByTypeDef
            = new int[tables.TypeDefs.Count + 1];
        Array.Fill(structureTypeByTypeDef, -1);
        ReadForeignNativeInterfaceFunctionPointerAnchors(tables, context,
            moduleRow, pointerTypeIndexes, signatureIndexByAnchorMethod,
            signatureIndexByAnchorTypeSpec, structureTypeByTypeDef);
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
        var exposedProviderViews = new HashSet<(
            string ProducerIdentity, string ExportKey)>();
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
                if (exposedProviderViews.Add((
                        providerImport.ProducerIdentity,
                        providerImport.ExportKey)))
                {
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
                .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                    context.Artifacts[providerArtifactIndex],
                    context.Schema));
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
        return providerInterface;
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
        int[] structureTypeByTypeDef)
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
                    || !NativePointerTypeContract.IsValidTable(
                        [.. nativePointerTypeDefinitions], [],
                        candidateSignatures))
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

        if (context.ForeignNativeInterfaceFunctionPointerSignatures.Count
            != 0
            && !NativeFunctionPointerContract.IsValidTable(context
                .ForeignNativeInterfaceFunctionPointerSignatures.ToImmutableArray()))
        {
            throw Invalid(
                "The selected FNI modules contain an invalid function-pointer signature table.");
        }

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
                if (context.Schema
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
