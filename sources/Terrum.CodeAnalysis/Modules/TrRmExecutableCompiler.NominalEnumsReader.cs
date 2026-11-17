// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Numerics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private const string GenericConstantMethodsDraftSchemaIdentity
        = "trrm/1-draft-constant-generic-methods-1";

    private readonly record struct GenericNominalWrapperDomain(
        PrimitiveType? Type, PrimitiveType CarrierType,
        BoundGenericNominalTypeIdentity? Identity = null);

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
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables) =>
        LoadNominalEnumsDraftDefinitions(tables,
            requireEnumOnlyTypeDefs: true);

    private static ImmutableArray<BoundEnumDefinition>
        LoadGenericNominalEnumDefinitions(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables) =>
        LoadNominalEnumsDraftDefinitions(tables,
            requireEnumOnlyTypeDefs: false);

    private static ImmutableArray<BoundEnumDefinition>
        LoadNominalEnumsDraftDefinitions(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            bool requireEnumOnlyTypeDefs)
    {
        var enumTypeDefRows = ImmutableArray.CreateBuilder<int>();
        for (var typeDefRow = 1; typeDefRow <= tables.TypeDefs.Count;
            typeDefRow++)
        {
            if (tables.TypeDefs.GetKind(typeDefRow) == 4)
            {
                enumTypeDefRows.Add(typeDefRow);
            }
        }

        if (enumTypeDefRows.Count != tables.EnumDefs.Count
            || (requireEnumOnlyTypeDefs
                && tables.TypeDefs.Count != enumTypeDefRows.Count)
            || enumTypeDefRows.Count > BoundEnumTypes.MaximumCount)
        {
            throw Unsupported(
                "NominalEnumsDraft scalar returns require a bounded enum-only TypeDef table.");
        }

        var definitions = ImmutableArray.CreateBuilder<BoundEnumDefinition>(
            enumTypeDefRows.Count);
        foreach (var typeDefRow in enumTypeDefRows)
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
            var enumIdentity
                = new BoundEnumDeclarationIdentity.Core(identity);
            var name = tables.TypeDefs.GetName(typeDefRow);
            var namespaceName = tables.TypeDefs.GetNamespace(typeDefRow);
            var qualifiedName = namespaceName.Length == 0
                ? name : $"{namespaceName}.{name}";
            var enumRow = tables.EnumDefs.GetRowForTypeDef(typeDefRow);
            var underlyingType = ResolveEnumUnderlyingType(tables, enumRow);
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
                    new BoundEnumMemberIdentity.OwnerKey(enumIdentity,
                        declarationKey),
                    enumIdentity,
                    tables.EnumMembers.GetName(memberRow),
                    tables.Schema.UsesTypedEnumMemberValues()
                        ? ReadGenericEnumMemberValue(tables, typeDefRow,
                            memberRow, underlyingType)
                        : tables.EnumMembers.GetValue(memberRow),
                    IsPublic: true,
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

            definitions.Add(new BoundEnumDefinition(
                enumIdentity,
                qualifiedName,
                underlyingType, members.MoveToImmutable(),
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

    private static PrimitiveType ResolveEnumUnderlyingType(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int enumRow)
    {
        if (tables.Schema.SupportsNominalEnums())
        {
            return PrimitiveType.Int32;
        }

        var type = tables.TypeSpecs.GetTypeSpecRef(
            tables.EnumDefs.GetUnderlyingTypeSpecRef(enumRow));
        if (!tables.Schema.UsesTypedEnumMemberValues()
            || type.Form != 0x01 || type.Qualifiers != 0
            || type.ArgumentCode != 0
            || !tables.Schema.SupportsEnumUnderlyingTypeCode(
                type.TypeCode)
            || !TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                type.TypeCode, out var primitiveType)
            || !PrimitiveTypeFacts.IsIntegerOrWide(primitiveType))
        {
            throw Unsupported(
                "The selected enum has no admitted integer carrier.");
        }

        return primitiveType;
    }

    private static BigInteger ReadGenericEnumMemberValue(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int ownerTypeDef, int memberRow, PrimitiveType underlyingType)
    {
        var sourceRow = checked((int)
            tables.EnumMembers.GetValueSourceRef(memberRow));
        var nodeRow = checked((int)
            tables.ConstantSources.GetPayloadReference(sourceRow));
        var valueTypeSpecRef = tables.FrozenValueNodes.GetTypeSpecRef(nodeRow);
        var ownerModule = tables.TypeDefs.GetOwnerModule(ownerTypeDef);
        var payload = TrRmConstantSourceValueValidator.GetFrozenScalarPayload(
            sourceRow, valueTypeSpecRef, ownerModule, tables.ConstantSources,
            tables.FrozenValueNodes, tables.Blobs, tables.TypeSpecs,
            tables.Features, tables.Modules, tables.TypeDefs, tables.EnumDefs,
            tables.Imports);
        return new BigInteger(payload,
            isUnsigned: !PrimitiveTypeFacts.IsSignedIntegerOrWide(
                underlyingType),
            isBigEndian: false);
    }

    private readonly record struct GenericNominalWrapperLayoutIdentity(
        uint ProfileId, uint RuleRevision, byte Flags, uint Alignment,
        uint Pack);

    private static BoundSourceTypeProviderSnapshot
        CreateGenericConstantMethodsDraftProviderSnapshot(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            byte[] artifact, int moduleRow)
    {
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(moduleRow);
        var (featureFirst, featureCount)
            = tables.Modules.GetFeatureRange(moduleRow);
        var features
            = ImmutableArray.CreateBuilder<BoundSourceTypeFeature>(
                featureCount);
        for (var offset = 0; offset < featureCount; offset++)
        {
            var feature = tables.Features.GetFeature(featureFirst + offset);
            features.Add(new BoundSourceTypeFeature(feature.FeatureId,
                feature.RuleRevision));
        }

        var profiles
            = ImmutableArray.CreateBuilder<BoundSourceTypeTargetProfile>(
                tables.TargetProfiles.Count);
        for (var row = 1; row <= tables.TargetProfiles.Count; row++)
        {
            var profile = tables.TargetProfiles.GetProfile(row);
            profiles.Add(new BoundSourceTypeTargetProfile(
                profile.ProfileId, profile.RuleRevision));
        }

        return new BoundSourceTypeProviderSnapshot(
            GenericConstantMethodsDraftSchemaIdentity, [.. artifact],
            moduleRow, tables.Modules.GetIdentity(moduleRow),
            tables.Modules.GetGoverningRuleEdition(moduleRow),
            targetRequirement, runtimePolicy, features.MoveToImmutable(),
            profiles.MoveToImmutable());
    }

    private static bool TryCreateGenericNominalTransparentWrapperShape(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int typeDefRow, out BoundStruct structure,
        out PrimitiveType carrierPrimitiveType)
    {
        structure = null!;
        carrierPrimitiveType = PrimitiveType.Void;
        if (tables.TypeDefs.GetKind(typeDefRow) != 1
            || !tables.TransparentConstantDomains.TryGetForOwner(typeDefRow,
                out var carrierTypeSpecRef, out var carrierFieldDefRow))
        {
            return false;
        }

        var carrierType = tables.TypeSpecs.GetTypeSpecRef(carrierTypeSpecRef);
        if (carrierType.Form != 0x01 || carrierType.Qualifiers != 0
            || carrierType.ArgumentCode != 0
            || !TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                carrierType.TypeCode, out carrierPrimitiveType)
            || !(PrimitiveTypeFacts.IsIntegerOrWide(carrierPrimitiveType)
                || carrierPrimitiveType == PrimitiveType.Bool))
        {
            throw new InvalidDataException(
                "A transparent wrapper has no admitted primitive carrier.");
        }

        if (!tables.Layouts.HasBlittableLayout(typeDefRow,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision))
        {
            return false;
        }

        var size = ValueLayout.Size(carrierPrimitiveType, []);
        var fieldFlags = tables.Fields.GetFlags(carrierFieldDefRow);
        var field = new BoundStructField(
            tables.Fields.GetName(carrierFieldDefRow), carrierPrimitiveType,
            0, size)
        {
            IsMutable = (fieldFlags & 0x04) != 0,
            IsPrivate = (fieldFlags & 3) == 0,
            IsInternal = (fieldFlags & 3) == 1,
        };
        var namespaceName = tables.TypeDefs.GetNamespace(typeDefRow);
        var name = tables.TypeDefs.GetName(typeDefRow);
        var qualifiedName = namespaceName.Length == 0
            ? name : $"{namespaceName}.{name}";
        structure = new BoundStruct(qualifiedName, [field], size)
        {
            IsTransparent = true,
            Alignment = ValueLayout.Alignment(carrierPrimitiveType, []),
            RequiredAlignment = ValueLayout.RequiredAlignment(
                carrierPrimitiveType, []),
            BlittableTargetRequirement
                = FixedLayoutStorage.WindowsX64TargetRequirement,
        };
        return true;
    }

    private static BoundStruct AddGenericNominalTransparentWrapperOrigin(
        BoundStruct structure, BoundSourceTypeProviderSnapshot provider,
        uint definitionId, ImmutableArray<string> exportKeys,
        bool isLookupRoot,
        ImmutableArray<BoundSourceTypeExpectation> expectations,
        BoundSourceTypeProviderSnapshot referenceRoot,
        BoundStruct? expectedDeclaration)
    {
        var origin = new BoundStandardTypeOrigin(provider, definitionId,
            exportKeys, isLookupRoot, expectations, referenceRoot,
            expectedDeclaration ?? structure);
        return structure with {
            StandardTypeOrigin = origin with {
                ExpectedDeclaration = (expectedDeclaration ?? structure) with {
                    StandardTypeOrigin = null,
                },
            },
        };
    }

    private static void ValidateGenericNominalTransparentWrapperBinding(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables expected,
        int expectedTypeDef,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
        int actualTypeDef)
    {
        if (expected.TypeDefs.GetKind(expectedTypeDef) != 1
            || actual.TypeDefs.GetKind(actualTypeDef) != 1
            || expected.TypeDefs.GetFlags(expectedTypeDef)
                != actual.TypeDefs.GetFlags(actualTypeDef)
            || !StringComparer.Ordinal.Equals(
                expected.TypeDefs.GetNamespace(expectedTypeDef),
                actual.TypeDefs.GetNamespace(actualTypeDef))
            || !StringComparer.Ordinal.Equals(
                expected.TypeDefs.GetName(expectedTypeDef),
                actual.TypeDefs.GetName(actualTypeDef))
            || !GenericWrapperFormalsMatch(expected, expectedTypeDef,
                actual, actualTypeDef)
            || !expected.TransparentConstantDomains.TryGetForOwner(
                expectedTypeDef, out var expectedCarrierTypeSpec,
                out var expectedCarrierField)
            || !actual.TransparentConstantDomains.TryGetForOwner(
                actualTypeDef, out var actualCarrierTypeSpec,
                out var actualCarrierField))
        {
            throw InvalidWrapperBinding();
        }

        if (!SamePrimitiveType(expected.TypeSpecs.GetTypeSpecRef(
                    expectedCarrierTypeSpec),
                actual.TypeSpecs.GetTypeSpecRef(actualCarrierTypeSpec))
            || !StringComparer.Ordinal.Equals(
                expected.Fields.GetName(expectedCarrierField),
                actual.Fields.GetName(actualCarrierField))
            || expected.Fields.GetFlags(expectedCarrierField)
                != actual.Fields.GetFlags(actualCarrierField)
            || !SamePrimitiveType(expected.TypeSpecs.GetTypeSpecRef(
                    expected.Fields.GetTypeSpecRef(expectedCarrierField)),
                actual.TypeSpecs.GetTypeSpecRef(
                    actual.Fields.GetTypeSpecRef(actualCarrierField)))
            || !WrapperLayoutsMatch(expected, expectedTypeDef,
                expectedCarrierField, actual, actualTypeDef,
                actualCarrierField))
        {
            throw InvalidWrapperBinding();
        }
    }

    private static bool GenericWrapperFormalsMatch(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables expected,
        int expectedTypeDef,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
        int actualTypeDef)
    {
        var (expectedFirst, expectedCount)
            = expected.TypeDefs.GetGenericRange(expectedTypeDef);
        var (actualFirst, actualCount)
            = actual.TypeDefs.GetGenericRange(actualTypeDef);
        if (expectedCount != actualCount)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < expectedCount; ordinal++)
        {
            var expectedRow = expectedFirst + ordinal;
            var actualRow = actualFirst + ordinal;
            if (expected.GenericParams.GetFormalKind(expectedRow) != 1
                || actual.GenericParams.GetFormalKind(actualRow) != 1
                || expected.GenericParams.GetOrdinal(expectedRow)
                    != (uint)ordinal
                || actual.GenericParams.GetOrdinal(actualRow)
                    != (uint)ordinal
                || expected.GenericParams.GetOwnershipDomain(expectedRow)
                    != actual.GenericParams.GetOwnershipDomain(actualRow)
                || expected.GenericParams.GetNullabilityDomain(expectedRow)
                    != actual.GenericParams.GetNullabilityDomain(actualRow)
                || expected.GenericParams.GetConstantTypeSpecRef(expectedRow)
                    != 0
                || actual.GenericParams.GetConstantTypeSpecRef(actualRow) != 0
                || expected.GenericParams.GetDefaultArgumentRef(expectedRow)
                    != 0
                || actual.GenericParams.GetDefaultArgumentRef(actualRow) != 0
                || expected.GenericParams.GetConstraintRange(expectedRow)
                    .Count != 0
                || actual.GenericParams.GetConstraintRange(actualRow)
                    .Count != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool WrapperLayoutsMatch(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables expected,
        int expectedTypeDef, int expectedCarrierField,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
        int actualTypeDef, int actualCarrierField)
    {
        var expectedLayouts = GetWrapperLayoutIdentities(expected,
            expectedTypeDef, expectedCarrierField);
        var actualLayouts = GetWrapperLayoutIdentities(actual,
            actualTypeDef, actualCarrierField);
        return expectedLayouts.SetEquals(actualLayouts);
    }

    private static HashSet<GenericNominalWrapperLayoutIdentity>
        GetWrapperLayoutIdentities(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int typeDef, int carrierField)
    {
        var result = new HashSet<GenericNominalWrapperLayoutIdentity>();
        var (first, count) = tables.TypeDefs.GetLayoutRange(typeDef);
        for (var row = first; row < first + count; row++)
        {
            var profileRef = tables.Layouts.GetTargetProfileRef(row);
            var profile = profileRef == 0 ? default
                : tables.TargetProfiles.GetProfile(
                    checked((int)profileRef));
            var (fieldFirst, fieldCount)
                = tables.Layouts.GetLayoutFieldRange(row);
            if (tables.Layouts.GetLayoutKind(row) != 3 || fieldCount != 1
                || tables.LayoutFields.GetFieldDef(fieldFirst)
                    != carrierField)
            {
                throw InvalidWrapperBinding();
            }

            _ = result.Add(new GenericNominalWrapperLayoutIdentity(
                profile.ProfileId, profile.RuleRevision,
                tables.Layouts.GetFlags(row),
                tables.Layouts.GetAlignment(row),
                tables.Layouts.GetPack(row)));
        }

        return result;
    }

    private static bool SamePrimitiveType(TrRmTypeSpecRefInfo left,
        TrRmTypeSpecRefInfo right)
        => left.Form == 0x01 && right.Form == 0x01
            && left.Qualifiers == 0 && right.Qualifiers == 0
            && left.TypeCode == right.TypeCode
            && left.ArgumentCode == right.ArgumentCode;

    private static InvalidDataException InvalidWrapperBinding()
        => new("A CG1 wrapper import does not match its provider contract.");

    private static ImmutableArray<BoundStruct>
        LoadGenericNominalTransparentWrappers(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            byte[] providerArtifact, IReadOnlyList<byte[]> providerArtifacts,
            out Dictionary<int, GenericNominalWrapperDomain> domainsByTypeDef,
            out ImmutableArray<BoundGenericNominalTypeDefinition>
                importedGenericDefinitions)
    {
        var wrapperDomains = new Dictionary<int, GenericNominalWrapperDomain>();
        domainsByTypeDef = wrapperDomains;
        var importedDefinitions = ImmutableArray.CreateBuilder<
            BoundGenericNominalTypeDefinition>();
        importedGenericDefinitions = [];
        if (!tables.Schema.UsesGenericConstantMethods())
        {
            return [];
        }

        var consumerSnapshot
            = CreateGenericConstantMethodsDraftProviderSnapshot(tables,
                providerArtifact, 1);
        var structures = ImmutableArray.CreateBuilder<BoundStruct>();
        for (var typeDefRow = 1; typeDefRow <= tables.TypeDefs.Count;
            typeDefRow++)
        {
            if (!tables.TransparentConstantDomains.TryGetForOwner(typeDefRow,
                    out _, out _)
                || tables.TypeDefs.GetOwnerModule(typeDefRow) != 1)
            {
                continue;
            }

            var expectedDefinitionId = 0x1300_0000u
                | (uint)typeDefRow;
            if (tables.Imports.TryGetExpectedBinding(
                    expectedDefinitionId, out var binding))
            {
                var isGenericWrapper
                    = tables.TypeDefs.GetGenericRange(typeDefRow).Count != 0;

                var importRow = 0;
                for (var import = 1; import <= tables.Imports.Count; import++)
                {
                    if (tables.Imports.GetExpectedDefinitionForImportRow(
                            import) != expectedDefinitionId)
                    {
                        continue;
                    }

                    if (importRow != 0)
                    {
                        throw InvalidWrapperBinding();
                    }
                    importRow = import;
                }

                if (importRow == 0)
                {
                    throw InvalidWrapperBinding();
                }

                var boundStructure = (BoundStruct?)null;
                var boundGenericDefinition
                    = (BoundGenericNominalTypeDefinition?)null;
                var boundCarrier = PrimitiveType.Void;
                var matchingModules = 0;
                foreach (var candidateArtifact in providerArtifacts)
                {
                    ArgumentNullException.ThrowIfNull(candidateArtifact);
                    var providerTables
                        = TrRmStructuralMetadataValidator
                            .ValidateAndGetExecutionTables(
                                TrRmArtifactIndex.ForGenericConstantMethodsDraft(
                                    candidateArtifact));
                    if (!providerTables.Modules.TryFindModule(
                            binding.ProducerIdentity, out var providerModule))
                    {
                        continue;
                    }

                    matchingModules++;
                    if (matchingModules > 1
                        || providerTables.Modules.GetRequirements(
                            providerModule)
                            != (binding.TargetRequirement,
                                binding.RuntimePolicy)
                        || !providerTables.Exports
                            .TryGetDefinitionForKey(providerModule,
                                binding.ExportKey, out var providerDefinition)
                        || (providerDefinition >> 24) != 0x13)
                    {
                        throw InvalidWrapperBinding();
                    }

                    var providerTypeDef = checked((int)(
                        providerDefinition & 0x00FF_FFFF));
                    if (providerTypeDef <= 0
                        || providerTypeDef > providerTables.TypeDefs.Count
                        || providerTables.TypeDefs.GetOwnerModule(
                            providerTypeDef) != providerModule
                        || !TryCreateGenericNominalTransparentWrapperShape(
                            providerTables, providerTypeDef,
                            out var providerShape, out var providerCarrier))
                    {
                        throw InvalidWrapperBinding();
                    }

                    ValidateGenericNominalTransparentWrapperBinding(
                        tables, typeDefRow, providerTables,
                        providerTypeDef);
                    var providerSnapshot
                        = CreateGenericConstantMethodsDraftProviderSnapshot(
                            providerTables, candidateArtifact, providerModule);
                    var consumerShape
                        = TryCreateGenericNominalTransparentWrapperShape(
                            tables, typeDefRow, out var expectedShape,
                            out _)
                            ? expectedShape
                            : throw InvalidWrapperBinding();
                    var providerModuleRef = tables.Imports
                        .GetModuleRefForImportRow(importRow);
                    if (tables.ModuleRefs.GetProducerIdentity(
                            providerModuleRef) != binding.ProducerIdentity)
                    {
                        throw InvalidWrapperBinding();
                    }

                    var expectation = new BoundSourceTypeExpectation(
                        consumerSnapshot, importRow, expectedDefinitionId,
                        binding.ProducerIdentity, binding.ExportKey,
                        binding.TargetRequirement, binding.RuntimePolicy,
                        providerSnapshot, providerDefinition);
                    boundCarrier = providerCarrier;
                    if (isGenericWrapper)
                    {
                        var moduleIdentity
                            = providerTables.Modules.GetIdentity(
                                providerModule);
                        var identity = new BoundGenericNominalTypeIdentity(
                            binding.ProducerIdentity, moduleIdentity,
                            providerDefinition);
                        var providerWrapperDomains = new Dictionary<int,
                            GenericNominalWrapperDomain> {
                            [providerTypeDef] = new GenericNominalWrapperDomain(
                                null, providerCarrier, identity),
                        };
                        var providerDefinitions
                            = TrRmExecutableCompiler
                                .CreateGenericNominalTypeDefinitions(
                                    providerTables, binding.ProducerIdentity,
                                    LoadGenericNominalEnumDefinitions(
                                        providerTables),
                                    providerWrapperDomains, providerModule,
                                    providerTypeDef);
                        BoundGenericNominalTypeDefinition?
                            providerNominalDefinition = null;
                        foreach (var candidate in providerDefinitions)
                        {
                            if (candidate.Identity != identity)
                            {
                                continue;
                            }
                            if (providerNominalDefinition is not null)
                            {
                                throw InvalidWrapperBinding();
                            }
                            providerNominalDefinition = candidate;
                        }

                        boundGenericDefinition
                            = (providerNominalDefinition
                                ?? throw InvalidWrapperBinding()) with {
                                ProviderExpectations = [expectation],
                            };
                    }
                    else
                    {
                        var providerExportKeys = ImmutableArray.Create(
                            binding.ExportKey);
                        boundStructure
                            = AddGenericNominalTransparentWrapperOrigin(
                                providerShape, providerSnapshot,
                                providerDefinition, providerExportKeys,
                                isLookupRoot: true, [expectation],
                                consumerSnapshot, consumerShape);
                    }
                }

                if (isGenericWrapper)
                {
                    var hasDuplicateDefinition = false;
                    foreach (var definition in importedDefinitions)
                    {
                        hasDuplicateDefinition |= definition.Identity
                            == boundGenericDefinition?.Identity;
                    }
                    if (boundGenericDefinition is null
                        || !wrapperDomains.TryAdd(typeDefRow,
                            new GenericNominalWrapperDomain(null,
                                boundCarrier,
                                boundGenericDefinition.Identity))
                        || hasDuplicateDefinition)
                    {
                        throw InvalidWrapperBinding();
                    }
                    importedDefinitions.Add(boundGenericDefinition);
                    continue;
                }

                if (boundStructure is null)
                {
                    continue;
                }

                AddWrapperStructure(typeDefRow, boundStructure,
                    boundCarrier);
                continue;
            }

            if (!TryCreateGenericNominalTransparentWrapperShape(tables,
                    typeDefRow, out var structure,
                    out var carrierPrimitiveType))
            {
                continue;
            }

            var definitionId = expectedDefinitionId;
            if (tables.TypeDefs.GetGenericRange(typeDefRow).Count != 0)
            {
                if (!wrapperDomains.TryAdd(typeDefRow,
                        new GenericNominalWrapperDomain(null,
                            carrierPrimitiveType)))
                {
                    throw InvalidWrapperBinding();
                }
                continue;
            }

            var exportKeys = tables.Exports.TryGetExportKeyForDefinition(
                    1, definitionId, out var exportKey)
                ? ImmutableArray.Create(exportKey) : [];
            var lookupRoot = !exportKeys.IsEmpty;
            var rootedStructure = AddGenericNominalTransparentWrapperOrigin(
                structure, consumerSnapshot, definitionId, exportKeys,
                lookupRoot, [], consumerSnapshot, null);
            AddWrapperStructure(typeDefRow, rootedStructure,
                carrierPrimitiveType);
        }
        importedGenericDefinitions = importedDefinitions.ToImmutable();
        return structures.ToImmutable();

        void AddWrapperStructure(int typeDefRow, BoundStruct structure,
            PrimitiveType carrierType)
        {
            if (structures.Count >= ManagedClassTypes.First - 100)
            {
                throw Unsupported(
                    "The selected wrapper structure table exceeds its type identity range.");
            }

            var wrapperType = (PrimitiveType)(100 + structures.Count);
            structures.Add(structure);
            if (!wrapperDomains.TryAdd(typeDefRow,
                    new GenericNominalWrapperDomain(wrapperType,
                        carrierType)))
            {
                throw InvalidWrapperBinding();
            }
        }
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
