// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static bool HasNominalEnumsDraftFeatureSet(
        TrRmFeatureTableReader features, int featureCount)
    {
        if (featureCount is < 1 or > 3)
        {
            return false;
        }

        var hasNativeBoundary = false;
        var hasTypedFrozenValues = false;
        var hasNominalEnums = false;
        for (var row = 1; row <= featureCount; row++)
        {
            var feature = features.GetFeature(row);
            if (feature == TrRmFeatureRow.NativeBoundaryContracts)
            {
                hasNativeBoundary = true;
            }
            else if (feature == TrRmFeatureRow.TypedFrozenValues)
            {
                hasTypedFrozenValues = true;
            }
            else if (feature.FeatureId == 18 && feature.RuleRevision == 1)
            {
                hasNominalEnums = true;
            }
            else
            {
                return false;
            }
        }

        return hasTypedFrozenValues
            && featureCount
                == 1 + (hasNativeBoundary ? 1 : 0)
                    + (hasNominalEnums ? 1 : 0);
    }

    private static bool HasNominalEnumsDraftIdentityFeatureSet(
        TrRmFeatureTableReader features, int featureCount)
    {
        if (featureCount is < 1 or > 2)
        {
            return false;
        }

        var hasNativeBoundary = false;
        var hasNominalEnums = false;
        for (var row = 1; row <= featureCount; row++)
        {
            var feature = features.GetFeature(row);
            if (feature == TrRmFeatureRow.NativeBoundaryContracts)
            {
                hasNativeBoundary = true;
            }
            else if (feature.FeatureId == 18 && feature.RuleRevision == 1)
            {
                hasNominalEnums = true;
            }
            else
            {
                return false;
            }
        }

        return hasNominalEnums
            && featureCount == 1 + (hasNativeBoundary ? 1 : 0);
    }

    private static PrimitiveType ResolveScalarReturnType(uint typeSpecRef,
        TrRmTypeSpecRefInfo typeReference,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        bool nominalEnumsDraft)
    {
        if (typeReference.Form == 0x01 && typeReference.Qualifiers == 0
            && TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                typeReference.TypeCode, out var scalarType))
        {
            return scalarType;
        }

        if (nominalEnumsDraft && typeReference.IsRow
            && typeReference.Form is 0x02 or 0x03
            && typeReference.Qualifiers == 0)
        {
            var typeDefRow = tables.TypeSpecs.GetNominalTypeDefRow(
                typeSpecRef, tables.TypeDefs, tables.Imports);
            return GetNominalEnumType(typeDefRow, tables.TypeDefs);
        }

        throw Unsupported(
            "Scalar-return execution requires a materializable scalar or nominal enum result.");
    }

    private static PrimitiveType GetNominalEnumType(int typeDefRow,
        TrRmTypeDefTableReader typeDefs)
    {
        var ordinal = 0;
        for (var row = 1; row <= typeDefs.Count; row++)
        {
            if (typeDefs.GetKind(row) != 4)
            {
                continue;
            }

            if (row == typeDefRow)
            {
                if (ordinal >= BoundEnumTypes.MaximumCount)
                {
                    throw Unsupported(
                        "The selected enum table exceeds the bound enum type domain.");
                }

                return BoundEnumTypes.Type(ordinal);
            }

            ordinal++;
        }

        throw Unsupported(
            "The selected TypeSpec does not identify a nominal enum TypeDef.");
    }

    private static ImmutableArray<BoundEnumDefinition>
        LoadNominalEnumsDraftDefinitions(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
    {
        if (tables.TypeDefs.Count != tables.EnumDefs.Count
            || tables.TypeDefs.Count > BoundEnumTypes.MaximumCount)
        {
            throw Unsupported(
                "NominalEnumsDraft scalar returns require a bounded enum-only TypeDef table.");
        }

        var definitions = ImmutableArray.CreateBuilder<BoundEnumDefinition>(
            tables.TypeDefs.Count);
        for (var typeDefRow = 1; typeDefRow <= tables.TypeDefs.Count;
            typeDefRow++)
        {
            var expectedDefId = 0x1300_0000u | (uint)typeDefRow;
            string providerIdentity;
            string exportKey;
            BoundModuleImport? moduleImport = null;
            if (tables.Imports.TryGetExpectedBinding(expectedDefId,
                    out var importBinding))
            {
                providerIdentity = importBinding.ProducerIdentity;
                exportKey = importBinding.ExportKey;
                moduleImport = new BoundModuleImport(providerIdentity,
                    exportKey, importBinding.TargetRequirement,
                    importBinding.RuntimePolicy);
            }
            else
            {
                var ownerModule = tables.TypeDefs.GetOwnerModule(typeDefRow);
                if (!tables.Exports.TryGetExportKeyForDefinition(ownerModule,
                    expectedDefId, out exportKey))
                {
                    throw Unsupported(
                        "NominalEnumsDraft loaded enums require one exact Export or Import identity.");
                }

                providerIdentity = tables.Modules.GetIdentity(ownerModule);
            }

            var identity = new CoreDeclarationIdentity(providerIdentity,
                exportKey, CoreDeclarationKind.EnumType);
            var name = tables.TypeDefs.GetName(typeDefRow);
            var namespaceName = tables.TypeDefs.GetNamespace(typeDefRow);
            var qualifiedName = namespaceName.Length == 0
                ? name : $"{namespaceName}.{name}";
            var enumRow = tables.EnumDefs.GetRowForTypeDef(typeDefRow);
            var (memberFirst, memberCount)
                = tables.EnumDefs.GetMemberRange(enumRow);
            var members
                = ImmutableArray.CreateBuilder<BoundEnumMemberDefinition>(
                    memberCount);
            for (var ordinal = 0; ordinal < memberCount; ordinal++)
            {
                var memberRow = memberFirst + ordinal;
                var declarationKey
                    = tables.EnumMembers.GetDeclarationKey(memberRow);
                members.Add(new BoundEnumMemberDefinition(
                    new CoreDeclarationIdentity(providerIdentity,
                        declarationKey, CoreDeclarationKind.EnumMember),
                    identity, tables.EnumMembers.GetName(memberRow),
                    tables.EnumMembers.GetValue(memberRow), IsPublic: true,
                    declarationKey));
            }

            var (layoutFirst, layoutCount)
                = tables.TypeDefs.GetLayoutRange(typeDefRow);
            var isBlittable = false;
            for (var layoutRow = layoutFirst;
                layoutRow < layoutFirst + layoutCount; layoutRow++)
            {
                isBlittable |= (tables.Layouts.GetFlags(layoutRow) & 1) != 0;
            }

            definitions.Add(new BoundEnumDefinition(identity, qualifiedName,
                PrimitiveType.Int32, members.MoveToImmutable(),
                IsPublic: true, IsPrimary: true,
                IsFlags: tables.EnumDefs.GetFlags(enumRow) == 1)
            {
                BlittableTargetRequirement = isBlittable
                    ? FixedLayoutStorage.WindowsX64TargetRequirement : 0,
                ModuleImport = moduleImport,
            });
        }

        var result = definitions.MoveToImmutable();
        if (!BoundEnumTypes.IsValidTable(result))
        {
            throw Unsupported(
                "NominalEnumsDraft enum identities cannot be reconstructed as a checked type table.");
        }

        return result;
    }

    private static void ValidateNominalEnumsDraftScalarReturnReferences(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        TrRmArtifactIndex index, int methodCount)
    {
        var importedProviders = new HashSet<string>(StringComparer.Ordinal);
        var expectedExportCount = 0;
        for (var typeDefRow = 1; typeDefRow <= tables.TypeDefs.Count;
            typeDefRow++)
        {
            var expectedDefId = 0x1300_0000u | (uint)typeDefRow;
            if (tables.Imports.TryGetExpectedBinding(expectedDefId,
                    out var binding))
            {
                _ = importedProviders.Add(binding.ProducerIdentity);
            }
            else
            {
                var ownerModule = tables.TypeDefs.GetOwnerModule(typeDefRow);
                if (!tables.Exports.TryGetExportKeyForDefinition(ownerModule,
                    expectedDefId, out _))
                {
                    throw Unsupported(
                        "A local NominalEnumsDraft TypeDef must retain its exact Export key.");
                }

                expectedExportCount++;
            }
        }

        if (tables.Imports.Count
                != tables.TypeDefs.Count - expectedExportCount
            || tables.ModuleRefs.Count != importedProviders.Count)
        {
            throw Unsupported(
                "NominalEnumsDraft scalar returns require exactly the selected enum TypeDef imports.");
        }

        for (var methodRow = 1; methodRow <= methodCount; methodRow++)
        {
            var definitionId = 0x1600_0000u | (uint)methodRow;
            var isPublic = tables.Methods.GetFlags(methodRow) == 2;
            var hasExactExport = tables.Exports.TryGetDefinitionForKey(1,
                tables.Methods.GetName(methodRow), out var exportedDefinition)
                && exportedDefinition == definitionId;
            if (isPublic != hasExactExport)
            {
                throw Unsupported(
                    "NominalEnumsDraft scalar-return methods must preserve their exact public Export state.");
            }

            if (isPublic)
            {
                expectedExportCount++;
            }
        }

        var hasExports = index.TryGetSection(0x001D, out _, out var exportCount);
        if (!hasExports && expectedExportCount != 0)
        {
            throw Unsupported(
                "NominalEnumsDraft scalar returns require their retained Export rows.");
        }
        if (hasExports && exportCount != expectedExportCount)
        {
            throw Unsupported(
                "NominalEnumsDraft scalar returns contain unconsumed Export rows.");
        }
    }
}
