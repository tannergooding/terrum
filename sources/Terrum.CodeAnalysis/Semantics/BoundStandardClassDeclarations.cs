// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed record BoundStandardClassFieldDeclaration(
    uint DefinitionId, uint OwnerDefinitionId, string Name, uint Flags,
    uint TypeSpecReference);

internal sealed record BoundStandardClassLayoutVariant(
    uint Row, uint OwnerDefinitionId, uint TargetProfileReference,
    uint TargetProfileId, uint TargetProfileRuleRevision, byte Kind,
    byte Flags, uint Alignment, uint Pack,
    ImmutableArray<uint> FieldDefinitionIds);

internal sealed record BoundStandardClassTypeView(
    BoundGenericNominalTypeIdentity Identity, string Namespace, string Name,
    uint TypeDefFlags,
    ImmutableArray<BoundStandardClassFieldDeclaration> Fields,
    ImmutableArray<BoundStandardClassLayoutVariant> Layouts,
    BoundSourceTypeProviderSnapshot Provider)
{
    internal bool TryGetDeclaredMutability(out bool isMutable)
    {
        if (Provider is not null
            && (StringComparer.Ordinal.Equals(Provider.SchemaIdentity,
                    "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1")
                || BoundStandardEnumDeclarationContract
                    .IsSelectedSchemaIdentity(Provider.SchemaIdentity)))
        {
            isMutable = (TypeDefFlags & 0x80) != 0;
            return true;
        }

        isMutable = false;
        return false;
    }
}

internal sealed record BoundStandardClassExpectation(
    BoundSourceTypeExpectation Binding,
    BoundStandardClassTypeView ExpectedDeclaration);

internal sealed record BoundStandardClassDeclaration(
    BoundStandardClassTypeView Declaration,
    ImmutableArray<string> ExportKeys,
    ImmutableArray<BoundStandardClassExpectation> Expectations);

internal static class BoundStandardClassDeclarationContract
{
    private const string C1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1";
    private const string C1M1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";

    internal static bool IsValidTable(
        ImmutableArray<BoundStandardClassDeclaration> declarations,
        ImmutableArray<BoundGenericNominalTypeDefinition> nominalTypes)
    {
        if (declarations.IsDefault
            || declarations.Length > BoundGenericNominalTypeTypes.MaximumCount)
        {
            return false;
        }
        if (declarations.IsEmpty)
        {
            return true;
        }
        if (!BoundGenericNominalTypeContract.IsValidDefinitionTable(
                nominalTypes))
        {
            return false;
        }

        var definitions = nominalTypes.ToDictionary(
            static definition => definition.Identity);
        var identities = new HashSet<BoundGenericNominalTypeIdentity>();
        var totalRows = 0L;
        foreach (var declaration in declarations)
        {
            if (declaration is null
                || !IsValidView(declaration.Declaration)
                || declaration.ExportKeys.IsDefault
                || declaration.Expectations.IsDefault
                || !identities.Add(declaration.Declaration.Identity)
                || !definitions.TryGetValue(declaration.Declaration.Identity,
                    out var definition)
                || definition.NominalKind != 2
                || !definition.Formals.IsEmpty
                || !StringComparer.Ordinal.Equals(definition.Namespace,
                    declaration.Declaration.Namespace)
                || !StringComparer.Ordinal.Equals(definition.Name,
                    declaration.Declaration.Name)
                || (!declaration.ExportKeys.IsEmpty
                    && (declaration.Declaration.TypeDefFlags & 3) != 2)
                || !IsUniqueText(declaration.ExportKeys))
            {
                return false;
            }
            totalRows += declaration.Declaration.Fields.Length
                + declaration.Declaration.Layouts.Length
                + declaration.Expectations.Length;
            if (totalRows > SemanticVerifier.MaximumAnalysisVisits)
            {
                return false;
            }

            var expectationKeys = new HashSet<(
                string SchemaIdentity, string ModuleIdentity, int ImportRow)>();
            foreach (var expectation in declaration.Expectations)
            {
                if (expectation is null
                    || !IsValidView(expectation.ExpectedDeclaration)
                    || expectation.ExpectedDeclaration.Identity
                        == declaration.Declaration.Identity
                    || expectation.Binding is not BoundSourceTypeExpectation
                        binding
                    || binding.ImportRow <= 0
                    || binding.ExpectedDefinitionId
                        != expectation.ExpectedDeclaration.Identity.DefinitionId
                    || binding.ProviderDefinitionId
                        != declaration.Declaration.Identity.DefinitionId
                    || !expectationKeys.Add((
                        binding.UsingModule.SchemaIdentity,
                        binding.UsingModule.ModuleIdentity,
                        binding.ImportRow))
                    || (expectation.ExpectedDeclaration.TypeDefFlags & 3)
                        != 2
                    || (declaration.Declaration.TypeDefFlags & 3) != 2
                    || !StringComparer.Ordinal.Equals(
                        binding.ProducerIdentity,
                        declaration.Declaration.Identity.ProviderIdentity)
                    || !declaration.ExportKeys.Contains(binding.ExportKey,
                        StringComparer.Ordinal)
                    || !SnapshotsMatch(binding.UsingModule,
                        expectation.ExpectedDeclaration.Provider)
                    || !SnapshotsMatch(binding.Provider,
                        declaration.Declaration.Provider)
                    || !AreEquivalentDeclarationContracts(
                        expectation.ExpectedDeclaration,
                        declaration.Declaration)
                    || binding.TargetRequirement
                        != declaration.Declaration.Provider.TargetRequirement
                    || binding.RuntimePolicy
                        != declaration.Declaration.Provider.RuntimePolicy)
                {
                    return false;
                }

                totalRows += expectation.ExpectedDeclaration.Fields.Length
                    + expectation.ExpectedDeclaration.Layouts.Length;
                if (totalRows > SemanticVerifier.MaximumAnalysisVisits)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsValidView(BoundStandardClassTypeView view)
    {
        if (view is null || !view.Identity.IsValid
            || (view.Identity.DefinitionId >> 24) != 0x13
            || !IsValidNamespace(view.Namespace) || !IsValidText(view.Name)
            || view.Fields.IsDefault || view.Layouts.IsDefault
            || !IsValidSnapshot(view.Provider)
            || !StringComparer.Ordinal.Equals(view.Identity.ModuleIdentity,
                view.Provider.ModuleIdentity)
            || !StringComparer.Ordinal.Equals(view.Provider.SchemaIdentity,
                C1SchemaIdentity)
                && !StringComparer.Ordinal.Equals(view.Provider.SchemaIdentity,
                    C1M1SchemaIdentity)
                && !BoundStandardEnumDeclarationContract
                    .IsSelectedSchemaIdentity(
                        view.Provider.SchemaIdentity))
        {
            return false;
        }

        var kind = (view.TypeDefFlags >> 2) & 7;
        var schemaAllowsMutability
            = StringComparer.Ordinal.Equals(view.Provider.SchemaIdentity,
                C1M1SchemaIdentity)
                || BoundStandardEnumDeclarationContract
                    .IsSelectedSchemaIdentity(
                        view.Provider.SchemaIdentity);
        uint allowedFlags = schemaAllowsMutability ? 0x8Fu : 0x0Fu;
        if (kind != 2 || (view.TypeDefFlags & ~allowedFlags) != 0
            || (view.TypeDefFlags & 3) == 3)
        {
            return false;
        }

        var fields = new Dictionary<uint, BoundStandardClassFieldDeclaration>();
        foreach (var field in view.Fields)
        {
            if (field is null || (field.DefinitionId >> 24) != 0x14
                || (field.DefinitionId & 0x00FF_FFFF) == 0
                || field.OwnerDefinitionId != view.Identity.DefinitionId
                || !IsValidText(field.Name)
                || !BoundGenericNominalTypeContract.IsTypeSpecReference(
                    field.TypeSpecReference)
                || !fields.TryAdd(field.DefinitionId, field))
            {
                return false;
            }
        }

        var layoutRows = new HashSet<uint>();
        var targetProfiles = new HashSet<uint>();
        var hasDefault = false;
        foreach (var layout in view.Layouts)
        {
            if (layout is null || layout.Row == 0
                || layout.OwnerDefinitionId != view.Identity.DefinitionId
                || !layoutRows.Add(layout.Row)
                || layout.TargetProfileReference
                    > (uint)view.Provider.TargetProfiles.Length
                || !MatchesTargetProfile(layout, view.Provider)
                || layout.Kind > 4 || (layout.Flags & ~1) != 0
                || layout.Alignment > 8192
                || (layout.Alignment != 0
                    && (layout.Alignment & (layout.Alignment - 1)) != 0)
                || (layout.Kind is 1 or 2
                    ? layout.Pack is not (1 or 2 or 4 or 8 or 16)
                    : layout.Pack is not (0 or 1 or 2 or 4 or 8 or 16))
                || ((layout.Flags & 1) != 0
                    && layout.TargetProfileReference == 0)
                || (layout.Kind is 0 or 3
                    && (layout.Alignment != 0 || layout.Pack != 0))
                || layout.FieldDefinitionIds.IsDefault
                || !targetProfiles.Add(layout.TargetProfileReference))
            {
                return false;
            }

            hasDefault |= layout.TargetProfileReference == 0;
            var listedFields = new HashSet<uint>();
            foreach (var fieldId in layout.FieldDefinitionIds)
            {
                if (!fields.TryGetValue(fieldId, out var field)
                    || (field.Flags & 8) != 0
                    || !listedFields.Add(fieldId))
                {
                    return false;
                }
            }

            if (listedFields.Count != fields.Values.Count(
                    static field => (field.Flags & 8) == 0))
            {
                return false;
            }
        }

        return hasDefault;
    }

    private static bool AreEquivalentDeclarationContracts(
        BoundStandardClassTypeView expected,
        BoundStandardClassTypeView actual)
    {
        if (!StringComparer.Ordinal.Equals(expected.Namespace,
                actual.Namespace)
            || !StringComparer.Ordinal.Equals(expected.Name, actual.Name)
            || expected.TypeDefFlags != actual.TypeDefFlags
            || expected.Fields.Length != actual.Fields.Length
            || expected.Layouts.Length != actual.Layouts.Length)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < expected.Fields.Length; ordinal++)
        {
            var expectedField = expected.Fields[ordinal];
            var actualField = actual.Fields[ordinal];
            if (!StringComparer.Ordinal.Equals(expectedField.Name,
                    actualField.Name)
                || expectedField.Flags != actualField.Flags)
            {
                return false;
            }
        }

        foreach (var expectedLayout in expected.Layouts)
        {
            var actualLayout = actual.Layouts.SingleOrDefault(layout =>
                layout.TargetProfileId == expectedLayout.TargetProfileId
                && layout.TargetProfileRuleRevision
                    == expectedLayout.TargetProfileRuleRevision);
            if (actualLayout is null
                || expectedLayout.Kind != actualLayout.Kind
                || expectedLayout.Flags != actualLayout.Flags
                || expectedLayout.Alignment != actualLayout.Alignment
                || expectedLayout.Pack != actualLayout.Pack
                || !LayoutFieldsMatch(expectedLayout, expected, actualLayout,
                    actual))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LayoutFieldsMatch(
        BoundStandardClassLayoutVariant expectedLayout,
        BoundStandardClassTypeView expected,
        BoundStandardClassLayoutVariant actualLayout,
        BoundStandardClassTypeView actual)
    {
        if (expectedLayout.FieldDefinitionIds.Length
            != actualLayout.FieldDefinitionIds.Length)
        {
            return false;
        }

        for (var ordinal = 0;
            ordinal < expectedLayout.FieldDefinitionIds.Length; ordinal++)
        {
            var expectedFieldOrdinal = FindFieldOrdinal(expected,
                expectedLayout.FieldDefinitionIds[ordinal]);
            var actualFieldOrdinal = FindFieldOrdinal(actual,
                actualLayout.FieldDefinitionIds[ordinal]);
            if (expectedFieldOrdinal != actualFieldOrdinal)
            {
                return false;
            }
        }

        return true;
    }

    private static int FindFieldOrdinal(BoundStandardClassTypeView view,
        uint fieldDefinitionId)
    {
        for (var ordinal = 0; ordinal < view.Fields.Length; ordinal++)
        {
            if (view.Fields[ordinal].DefinitionId == fieldDefinitionId)
            {
                return ordinal;
            }
        }

        return -1;
    }

    private static bool MatchesTargetProfile(
        BoundStandardClassLayoutVariant layout,
        BoundSourceTypeProviderSnapshot provider)
    {
        if (layout.TargetProfileReference == 0)
        {
            return layout.TargetProfileId == 0
                && layout.TargetProfileRuleRevision == 0;
        }

        var profile = provider.TargetProfiles[
            checked((int)layout.TargetProfileReference - 1)];
        return layout.TargetProfileId == profile.ProfileId
            && layout.TargetProfileRuleRevision == profile.RuleRevision;
    }

    private static bool IsValidSnapshot(
        BoundSourceTypeProviderSnapshot snapshot)
        => snapshot is not null
            && snapshot.ArtifactSnapshot is { IsDefault: false, Length: > 0 }
            && snapshot.ModuleRow > 0
            && IsValidText(snapshot.SchemaIdentity)
            && IsValidText(snapshot.ModuleIdentity)
            && snapshot.Features is { IsDefault: false }
            && snapshot.TargetProfiles is { IsDefault: false };

    private static bool SnapshotsMatch(
        BoundSourceTypeProviderSnapshot left,
        BoundSourceTypeProviderSnapshot right)
        => left is not null && right is not null
            && StringComparer.Ordinal.Equals(left.SchemaIdentity,
                right.SchemaIdentity)
            && left.ModuleRow == right.ModuleRow
            && StringComparer.Ordinal.Equals(left.ModuleIdentity,
                right.ModuleIdentity)
            && left.GoverningRuleEdition == right.GoverningRuleEdition
            && left.TargetRequirement == right.TargetRequirement
            && left.RuntimePolicy == right.RuntimePolicy
            && left.Features.AsSpan().SequenceEqual(right.Features.AsSpan())
            && left.TargetProfiles.AsSpan().SequenceEqual(
                right.TargetProfiles.AsSpan())
            && left.ArtifactSnapshot.AsSpan().SequenceEqual(
                right.ArtifactSnapshot.AsSpan());

    private static bool IsUniqueText(ImmutableArray<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return values.All(value => IsValidText(value) && seen.Add(value));
    }

    private static bool IsValidText(string value)
        => value is { Length: > 0 and <= 4096 }
            && value.IndexOf('\0') < 0;

    private static bool IsValidNamespace(string value)
        => value is { Length: <= 4096 }
            && value.IndexOf('\0') < 0;
}
