// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmEnumProviderBindingValidator
{
    internal static void Validate(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
        IReadOnlyList<byte[]> providerArtifacts)
    {
        var boundImports = new byte[consumer.Imports.Count + 1];
        foreach (var providerArtifact in providerArtifacts)
        {
            ArgumentNullException.ThrowIfNull(providerArtifact);
            var provider = TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForNominalEnumsDraft(providerArtifact));
            for (var providerModule = 1;
                providerModule <= provider.Modules.Count; providerModule++)
            {
                var producerIdentity = provider.Modules.GetIdentity(providerModule);
                for (var import = 1; import <= consumer.Imports.Count; import++)
                {
                    var expectedDefinition
                        = consumer.Imports.GetExpectedDefinitionForImportRow(import);
                    if ((expectedDefinition >> 24) != 0x13)
                    {
                        continue;
                    }

                    var expectedRow = checked((int)(
                        expectedDefinition & 0x00FF_FFFF));
                    if (consumer.TypeDefs.GetKind(expectedRow) != 4
                        || !consumer.Imports.TryGetExpectedBinding(
                            expectedDefinition, out var binding)
                        || !StringComparer.Ordinal.Equals(
                            producerIdentity, binding.ProducerIdentity))
                    {
                        continue;
                    }

                    if (boundImports[import] != 0
                        || provider.Modules.GetRequirements(providerModule)
                            != (binding.TargetRequirement, binding.RuntimePolicy)
                        || !provider.Exports.TryGetDefinitionForKey(providerModule,
                            binding.ExportKey, out var providerDefinition)
                        || (providerDefinition >> 24) != 0x13)
                    {
                        throw InvalidBinding();
                    }

                    var providerType = checked((int)(
                        providerDefinition & 0x00FF_FFFF));
                    if (providerType <= 0
                        || providerType > provider.TypeDefs.Count
                        || provider.TypeDefs.GetKind(providerType) != 4
                        || provider.TypeDefs.GetOwnerModule(providerType)
                            != providerModule)
                    {
                        throw InvalidBinding();
                    }

                    ValidateEnumSnapshot(consumer, expectedRow,
                        provider, providerType);
                    boundImports[import] = 1;
                }
            }
        }

        for (var import = 1; import < boundImports.Length; import++)
        {
            var expectedDefinition
                = consumer.Imports.GetExpectedDefinitionForImportRow(import);
            var expectedRow = checked((int)(expectedDefinition & 0x00FF_FFFF));
            if ((expectedDefinition >> 24) == 0x13
                && consumer.TypeDefs.GetKind(expectedRow) == 4
                && boundImports[import] == 0)
            {
                throw InvalidBinding();
            }
        }
    }

    private static void ValidateEnumSnapshot(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables expected,
        int expectedType,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables actual,
        int actualType)
    {
        if (expected.TypeDefs.GetFlags(expectedType)
                != actual.TypeDefs.GetFlags(actualType)
            || !StringComparer.Ordinal.Equals(
                expected.TypeDefs.GetNamespace(expectedType),
                actual.TypeDefs.GetNamespace(actualType))
            || !StringComparer.Ordinal.Equals(
                expected.TypeDefs.GetName(expectedType),
                actual.TypeDefs.GetName(actualType)))
        {
            throw InvalidBinding();
        }

        var expectedEnum = expected.EnumDefs.GetRowForTypeDef(expectedType);
        var actualEnum = actual.EnumDefs.GetRowForTypeDef(actualType);
        if (expected.EnumDefs.GetFlags(expectedEnum)
                != actual.EnumDefs.GetFlags(actualEnum))
        {
            throw InvalidBinding();
        }

        var expectedUnderlying = expected.TypeSpecs.GetTypeSpecRef(
            expected.EnumDefs.GetUnderlyingTypeSpecRef(expectedEnum));
        var actualUnderlying = actual.TypeSpecs.GetTypeSpecRef(
            actual.EnumDefs.GetUnderlyingTypeSpecRef(actualEnum));
        if (expectedUnderlying.Form != actualUnderlying.Form
            || expectedUnderlying.Qualifiers != actualUnderlying.Qualifiers
            || expectedUnderlying.TypeCode != actualUnderlying.TypeCode
            || expectedUnderlying.ArgumentCode != actualUnderlying.ArgumentCode)
        {
            throw InvalidBinding();
        }

        var (expectedFirst, expectedCount)
            = expected.EnumDefs.GetMemberRange(expectedEnum);
        var (actualFirst, actualCount) = actual.EnumDefs.GetMemberRange(actualEnum);
        if (expectedCount != actualCount)
        {
            throw InvalidBinding();
        }

        for (var ordinal = 0; ordinal < expectedCount; ordinal++)
        {
            var expectedMember = expectedFirst + ordinal;
            var actualMember = actualFirst + ordinal;
            if (expected.EnumMembers.GetOrdinal(expectedMember)
                    != actual.EnumMembers.GetOrdinal(actualMember)
                || !StringComparer.Ordinal.Equals(
                    expected.EnumMembers.GetDeclarationKey(expectedMember),
                    actual.EnumMembers.GetDeclarationKey(actualMember))
                || !StringComparer.Ordinal.Equals(
                    expected.EnumMembers.GetName(expectedMember),
                    actual.EnumMembers.GetName(actualMember))
                || expected.EnumMembers.GetValue(expectedMember)
                    != actual.EnumMembers.GetValue(actualMember))
            {
                throw InvalidBinding();
            }
        }

        if (!LayoutsMatch(expected, expectedType, actual, actualType))
        {
            throw InvalidBinding();
        }
    }

    private static bool LayoutsMatch(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables left,
        int leftType,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables right,
        int rightType)
    {
        var (leftFirst, leftCount) = left.TypeDefs.GetLayoutRange(leftType);
        var (rightFirst, rightCount) = right.TypeDefs.GetLayoutRange(rightType);
        if (leftCount != rightCount)
        {
            return false;
        }

        var rightLayouts = new HashSet<LayoutIdentity>();
        for (var ordinal = 0; ordinal < rightCount; ordinal++)
        {
            _ = rightLayouts.Add(GetLayoutIdentity(
                right, rightFirst + ordinal));
        }

        for (var ordinal = 0; ordinal < leftCount; ordinal++)
        {
            if (!rightLayouts.Remove(GetLayoutIdentity(
                    left, leftFirst + ordinal)))
            {
                return false;
            }
        }

        return rightLayouts.Count == 0;
    }

    private static LayoutIdentity GetLayoutIdentity(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int layoutRow)
    {
        var target = tables.Layouts.GetTargetProfileRef(layoutRow);
        var profile = target == 0
            ? default
            : tables.TargetProfiles.GetProfile(checked((int)target));
        var (fieldFirst, fieldCount) = tables.Layouts.GetLayoutFieldRange(layoutRow);
        if (fieldFirst != 0 || fieldCount != 0)
        {
            throw InvalidBinding();
        }

        return new LayoutIdentity(profile.ProfileId, profile.RuleRevision,
            tables.Layouts.GetLayoutKind(layoutRow),
            tables.Layouts.GetFlags(layoutRow),
            tables.Layouts.GetAlignment(layoutRow),
            tables.Layouts.GetPack(layoutRow));
    }

    private readonly record struct LayoutIdentity(
        uint ProfileId, uint RuleRevision, byte LayoutKind, byte Flags,
        uint Alignment, uint Pack);

    private static InvalidDataException InvalidBinding()
        => new("Invalid TRRM nominal enum provider binding.");
}
