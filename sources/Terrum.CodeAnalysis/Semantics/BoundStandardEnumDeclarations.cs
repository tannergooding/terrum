using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed record BoundStandardEnumTypeSite(
    BoundSourceTypeProviderSnapshot Snapshot, uint DefinitionId);

internal readonly record struct BoundStandardEnumSiteIdentity(
    string SchemaIdentity, ImmutableArray<byte> ArtifactSnapshot,
    int ModuleRow, string ModuleIdentity, uint DefinitionId);

internal sealed record BoundStandardEnumMemberView(
    string DeclarationKey, string Name, BigInteger Value);

internal sealed record BoundStandardEnumLayoutVariant(
    uint Row, uint OwnerDefinitionId, uint TargetProfileReference,
    uint TargetProfileId, uint TargetProfileRuleRevision, byte Kind,
    byte Flags, uint Alignment, uint Pack,
    ImmutableArray<uint> FieldDefinitionIds);

internal sealed record BoundStandardEnumView(
    BoundStandardEnumTypeSite Site, string Namespace, string Name,
    uint TypeDefFlags, PrimitiveType UnderlyingType, uint EnumFlags,
    ImmutableArray<BoundStandardEnumLayoutVariant> Layouts,
    ImmutableArray<BoundStandardEnumMemberView> Members);

internal sealed record BoundStandardEnumExpectation(
    BoundSourceTypeExpectation Binding,
    BoundStandardEnumView ExpectedDeclaration);

internal sealed class BoundStandardEnumOccurrence(
    BoundStandardEnumView declaration,
    ImmutableArray<BoundStandardEnumView> originalDeclarations,
    ImmutableArray<BoundStandardEnumExpectation> expectations,
    ImmutableArray<string> exportKeys,
    ImmutableArray<string> selectedExportKeys)
{
    private readonly bool _isValid
        = BoundStandardEnumDeclarationContract.ValidateOccurrenceData(
            declaration, originalDeclarations, expectations, exportKeys,
            selectedExportKeys);

    internal BoundStandardEnumView Declaration { get; } = declaration;
    internal ImmutableArray<BoundStandardEnumView> OriginalDeclarations
        { get; } = originalDeclarations;
    internal ImmutableArray<BoundStandardEnumExpectation> Expectations
        { get; } = expectations;
    internal ImmutableArray<string> ExportKeys { get; } = exportKeys;
    internal ImmutableArray<string> SelectedExportKeys
        { get; } = selectedExportKeys;
    internal bool IsLookupRoot => !SelectedExportKeys.IsEmpty;

    internal bool IsValid => _isValid;
}

internal sealed record BoundCoreEnumStandardProviderBinding(
    CoreProviderModuleBindings ModuleBindings,
    CoreProviderModuleOrigin ProviderOrigin,
    CoreEnumDefinition CoreDeclaration,
    BoundStandardEnumOccurrence StandardOccurrence);

internal static class BoundCoreEnumStandardProviderBindingContract
{
    internal const string SchemaIdentity
        = "trrm/1-draft-foreign-native-interface-parameter-surface-1-enum-closure-1";

    internal static bool TryCreate(CoreEnumDefinition coreDeclaration,
        BoundStandardEnumOccurrence standardOccurrence,
        CoreProviderModuleBindings moduleBindings,
        out BoundCoreEnumStandardProviderBinding? binding)
    {
        ArgumentNullException.ThrowIfNull(coreDeclaration);
        ArgumentNullException.ThrowIfNull(standardOccurrence);
        ArgumentNullException.ThrowIfNull(moduleBindings);
        binding = null;
        if (!moduleBindings.IsValid() || !standardOccurrence.IsValid)
        {
            return false;
        }

        var providers = moduleBindings.Providers.Where(provider =>
            StringComparer.Ordinal.Equals(provider.ModuleIdentity,
                coreDeclaration.Identity.ProviderIdentity)).ToArray();
        if (providers.Length != 1)
        {
            return false;
        }

        var candidate = new BoundCoreEnumStandardProviderBinding(
            moduleBindings, providers[0], coreDeclaration,
            standardOccurrence);
        var definition = BoundEnumDefinition.FromCore(coreDeclaration)
            with { CoreStandardProviderBinding = candidate };
        if (!IsValid(definition, candidate))
        {
            return false;
        }

        binding = candidate;
        return true;
    }

    internal static bool IsValid(BoundEnumDefinition definition)
        => definition.CoreStandardProviderBinding is
            BoundCoreEnumStandardProviderBinding binding
            && IsValid(definition, binding);

    internal static bool IsValid(BoundEnumDefinition definition,
        BoundCoreEnumStandardProviderBinding binding)
    {
        if (binding.ModuleBindings is not CoreProviderModuleBindings
                moduleBindings
            || binding.CoreDeclaration is not CoreEnumDefinition core
            || binding.StandardOccurrence is not {
                IsValid: true,
                Declaration: { Site: { Snapshot: not null } }
            } occurrence)
        {
            return false;
        }

        var view = occurrence.Declaration;
        var snapshot = view.Site.Snapshot;
        var origin = binding.ProviderOrigin;
        if (definition.Identity
                is not BoundEnumDeclarationIdentity.Core coreIdentity
            || coreIdentity.Value != core.Identity
            || core.Identity.Kind != CoreDeclarationKind.EnumType
            || core.GenericArity != 0 || !core.IsPublic || !core.IsPrimary
            || !core.Identity.IsValid
            || !moduleBindings.IsValid()
            || !IsSelectedProviderOrigin(moduleBindings, origin,
                core.Identity.ProviderIdentity)
            || !moduleBindings.ContainsDeclaration(core)
            || !occurrence.ExportKeys.Contains(core.Identity.ExportKey,
                StringComparer.Ordinal)
            || !StringComparer.Ordinal.Equals(
                origin.ModuleIdentity, core.Identity.ProviderIdentity)
            || !StringComparer.Ordinal.Equals(origin.Schema.Identity,
                SchemaIdentity)
            || origin.Schema.Revision != 1
            || !StringComparer.Ordinal.Equals(snapshot.SchemaIdentity,
                SchemaIdentity)
            || !StringComparer.Ordinal.Equals(snapshot.ModuleIdentity,
                origin.ModuleIdentity)
            || origin.GoverningSemanticEdition
                != snapshot.GoverningRuleEdition
            || origin.TargetRequirement != snapshot.TargetRequirement
            || origin.RuntimePolicy != snapshot.RuntimePolicy
            || origin.ModuleIdentity
                == moduleBindings.UsingModuleIdentity
            || !BoundEnumTypes.AreEquivalent(definition,
                BoundEnumDefinition.FromCore(core))
            || !MatchesStandardDeclaration(core, view))
        {
            return false;
        }

        return true;
    }

    internal static bool AreSameProvider(
        BoundCoreEnumStandardProviderBinding left,
        BoundCoreEnumStandardProviderBinding right)
    {
        var leftDefinition = BoundEnumDefinition.FromCore(
            left.CoreDeclaration) with {
                CoreStandardProviderBinding = left,
            };
        var rightDefinition = BoundEnumDefinition.FromCore(
            right.CoreDeclaration) with {
                CoreStandardProviderBinding = right,
            };
        var leftOccurrence = left.StandardOccurrence;
        var rightOccurrence = right.StandardOccurrence;
        return IsValid(leftDefinition, left)
            && IsValid(rightDefinition, right)
            && left.CoreDeclaration.Identity
                == right.CoreDeclaration.Identity
            && BoundEnumTypes.AreEquivalent(
                BoundEnumDefinition.FromCore(left.CoreDeclaration),
                BoundEnumDefinition.FromCore(right.CoreDeclaration))
            && left.ProviderOrigin == right.ProviderOrigin
            && BoundStandardEnumDeclarationContract.GetSiteIdentity(
                leftOccurrence.Declaration.Site)
                == BoundStandardEnumDeclarationContract.GetSiteIdentity(
                    rightOccurrence.Declaration.Site)
            && BoundStandardEnumDeclarationContract.ContractMatches(
                leftOccurrence.Declaration,
                rightOccurrence.Declaration)
            && left.ModuleBindings.ContainsDeclaration(
                left.CoreDeclaration)
            && right.ModuleBindings.ContainsDeclaration(
                right.CoreDeclaration);
    }

    private static bool IsSelectedProviderOrigin(
        CoreProviderModuleBindings moduleBindings,
        CoreProviderModuleOrigin providerOrigin, string moduleIdentity)
    {
        var providers = moduleBindings.Providers.Where(provider =>
            StringComparer.Ordinal.Equals(provider.ModuleIdentity,
                moduleIdentity)).ToArray();
        return providers.Length == 1 && providers[0] == providerOrigin;
    }

    private static bool MatchesStandardDeclaration(
        CoreEnumDefinition core, BoundStandardEnumView view)
    {
        if (!core.IsPublic || !core.IsPrimary || core.GenericArity != 0
            || !BoundStandardEnumDeclarationContract.IsPublic(
                view.TypeDefFlags)
            || !StringComparer.Ordinal.Equals(core.QualifiedName,
                BoundStandardEnumDeclarationContract.GetQualifiedName(view))
            || core.UnderlyingType != view.UnderlyingType
            || core.IsFlags != (view.EnumFlags == 1)
            || core.BlittableTargetRequirement
                != BoundStandardEnumDeclarationContract
                    .GetBlittableTargetRequirement(view.Layouts)
            || core.Members.IsDefault
            || core.Members.Length != view.Members.Length)
        {
            return false;
        }

        for (var index = 0; index < core.Members.Length; index++)
        {
            var coreMember = core.Members[index];
            var standardMember = view.Members[index];
            if (coreMember is null
                || !coreMember.Identity.IsValid
                || coreMember.Identity.Kind
                    != CoreDeclarationKind.EnumMember
                || coreMember.Identity.ProviderIdentity
                    != core.Identity.ProviderIdentity
                || coreMember.DeclaringEnum != core.Identity
                || !coreMember.IsPublic
                || !BoundEnumTypes.IsValidModuleDeclarationKey(
                    coreMember.DeclarationKey)
                || !StringComparer.Ordinal.Equals(coreMember.Name,
                    standardMember.Name)
                || coreMember.Value != standardMember.Value
                || !StringComparer.Ordinal.Equals(
                    coreMember.DeclarationKey,
                    standardMember.DeclarationKey))
            {
                return false;
            }
        }

        return true;
    }
}

internal static class BoundStandardEnumDeclarationContract
{
    private const uint WindowsX64NativeProfileId = 1;
    private const uint WindowsX64NativeProfileRuleRevision = 1;
    private const uint WindowsX64TargetRequirement = 1;

    private static readonly string[] s_schemaIdentities = [
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-initializer-copy-evidence-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-initializer-copy-evidence-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1-initializer-copy-evidence-1",
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1-initializer-copy-evidence-1",
        BoundCoreEnumStandardProviderBindingContract.SchemaIdentity,
    ];

    internal static bool IsSelectedSchemaIdentity(string? schemaIdentity)
        => schemaIdentity is not null
            && s_schemaIdentities.Contains(schemaIdentity,
                StringComparer.Ordinal);

    internal static bool IsSelectedClassInstanceLayoutSchemaIdentity(
        string? schemaIdentity)
        => schemaIdentity is
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1-initializer-copy-evidence-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1-initializer-copy-evidence-1";

    internal static bool IsValidDefinition(
        BoundStandardEnumOccurrence occurrence,
        BoundEnumDefinition definition)
        => occurrence.IsValid
            && definition.Identity
                is BoundEnumDeclarationIdentity.Standard standard
            && ReferenceEquals(standard.Value, occurrence)
            && definition.ModuleImport is null
            && definition.IsPrimary
            && definition.IsPublic == IsPublic(
                occurrence.Declaration.TypeDefFlags)
            && definition.IsFlags
                == (occurrence.Declaration.EnumFlags == 1)
            && definition.UnderlyingType
                == occurrence.Declaration.UnderlyingType
            && definition.BlittableTargetRequirement
                == GetBlittableTargetRequirement(
                    occurrence.Declaration.Layouts)
            && StringComparer.Ordinal.Equals(definition.Name,
                GetQualifiedName(occurrence.Declaration))
            && definition.Members.Length
                == occurrence.Declaration.Members.Length
            && MembersMatch(definition, occurrence.Declaration);

    internal static BoundStandardEnumSiteIdentity GetSiteIdentity(
        BoundStandardEnumTypeSite site)
        => new(site.Snapshot.SchemaIdentity,
            site.Snapshot.ArtifactSnapshot, site.Snapshot.ModuleRow,
            site.Snapshot.ModuleIdentity, site.DefinitionId);

    internal static bool ValidateOccurrenceData(
        BoundStandardEnumView declaration,
        ImmutableArray<BoundStandardEnumView> originalDeclarations,
        ImmutableArray<BoundStandardEnumExpectation> expectations,
        ImmutableArray<string> exportKeys,
        ImmutableArray<string> selectedExportKeys)
    {
        if (!IsValidView(declaration)
            || originalDeclarations.IsDefaultOrEmpty
            || expectations.IsDefault
            || exportKeys.IsDefault
            || selectedExportKeys.IsDefault
            || !AreUniqueKeys(exportKeys)
            || !AreUniqueKeys(selectedExportKeys)
            || selectedExportKeys.Any(key =>
                !exportKeys.Contains(key, StringComparer.Ordinal))
            || (!IsPublic(declaration.TypeDefFlags)
                && (!exportKeys.IsEmpty || !selectedExportKeys.IsEmpty)))
        {
            return false;
        }

        var originalSites
            = new List<BoundStandardEnumTypeSite>();
        var containsDeclaration = false;
        foreach (var original in originalDeclarations)
        {
            if (!IsValidView(original)
                || !StringComparer.Ordinal.Equals(
                    original.Site.Snapshot.SchemaIdentity,
                    declaration.Site.Snapshot.SchemaIdentity)
                || !ContractMatches(original, declaration)
                || originalSites.Any(site =>
                    SameSite(site, original.Site)))
            {
                return false;
            }

            originalSites.Add(original.Site);
            containsDeclaration |= SameSite(original.Site,
                declaration.Site);
        }

        if (!containsDeclaration)
        {
            return false;
        }

        var expectationKeys = new HashSet<(
            string SchemaIdentity, ImmutableArray<byte> ArtifactSnapshot,
            int ModuleRow, string ModuleIdentity, int ImportRow)>();
        foreach (var expectation in expectations)
        {
            if (expectation is null
                || !IsValidView(expectation.ExpectedDeclaration)
                || !ContractMatches(expectation.ExpectedDeclaration,
                    declaration)
                || !originalDeclarations.Any(original =>
                    SameSite(original.Site,
                        expectation.ExpectedDeclaration.Site))
                || !IsValidBinding(expectation, declaration)
                || !exportKeys.Contains(
                    expectation.Binding.ExportKey,
                    StringComparer.Ordinal)
                || !expectationKeys.Add((
                    expectation.Binding.UsingModule.SchemaIdentity,
                    expectation.Binding.UsingModule.ArtifactSnapshot,
                    expectation.Binding.UsingModule.ModuleRow,
                    expectation.Binding.UsingModule.ModuleIdentity,
                    expectation.Binding.ImportRow)))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool ContractMatches(
        BoundStandardEnumView expected, BoundStandardEnumView actual)
    {
        if (!StringComparer.Ordinal.Equals(expected.Namespace,
                actual.Namespace)
            || !StringComparer.Ordinal.Equals(expected.Name, actual.Name)
            || expected.TypeDefFlags != actual.TypeDefFlags
            || expected.UnderlyingType != actual.UnderlyingType
            || expected.EnumFlags != actual.EnumFlags
            || expected.Members.Length != actual.Members.Length
            || expected.Layouts.Length != actual.Layouts.Length)
        {
            return false;
        }

        var actualLayouts = new HashSet<LayoutContract>(
            actual.Layouts.Select(static layout =>
                LayoutContract.From(layout)));
        foreach (var layout in expected.Layouts)
        {
            if (!actualLayouts.Remove(LayoutContract.From(layout)))
            {
                return false;
            }
        }

        if (actualLayouts.Count != 0)
        {
            return false;
        }

        for (var index = 0; index < expected.Members.Length; index++)
        {
            var left = expected.Members[index];
            var right = actual.Members[index];
            if (!StringComparer.Ordinal.Equals(left.DeclarationKey,
                    right.DeclarationKey)
                || !StringComparer.Ordinal.Equals(left.Name, right.Name)
                || left.Value != right.Value)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool SnapshotsMatch(
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
            && left.ArtifactSnapshot.Equals(right.ArtifactSnapshot);

    private static bool IsValidView(BoundStandardEnumView view)
    {
        if (view is null || view.Site is null
            || !IsValidSnapshot(view.Site.Snapshot)
            || !IsSelectedSchemaIdentity(
                view.Site.Snapshot.SchemaIdentity)
            || (view.Site.DefinitionId >> 24) != 0x13
            || (view.Site.DefinitionId & 0x00FF_FFFF) == 0
            || !IsValidText(view.Namespace, allowEmpty: true)
            || !IsValidText(view.Name)
            || (view.TypeDefFlags & ~0x1Fu) != 0
            || ((view.TypeDefFlags >> 2) & 7) != 4
            || (view.TypeDefFlags & 3) == 3
            || view.EnumFlags > 1
            || !PrimitiveTypeFacts.IsIntegerOrWide(view.UnderlyingType)
            || !BoundEnumTypes.IsValidModuleDeclarationKey(
                GetQualifiedName(view))
            || view.Layouts.IsDefaultOrEmpty
            || view.Members.IsDefault
            || !IsValueInRange(view.Members, view.UnderlyingType))
        {
            return false;
        }

        var profileReferences = new HashSet<uint>();
        var hasDefault = false;
        var hasWindowsProfile = false;
        foreach (var layout in view.Layouts)
        {
            if (layout is null || layout.Row == 0
                || layout.OwnerDefinitionId != view.Site.DefinitionId
                || layout.FieldDefinitionIds.IsDefault
                || !layout.FieldDefinitionIds.IsEmpty
                || layout.TargetProfileReference
                    > (uint)view.Site.Snapshot.TargetProfiles.Length
                || layout.Kind != 0 || (layout.Flags & ~1) != 0
                || layout.Alignment != 0 || layout.Pack != 0
                || !profileReferences.Add(
                    layout.TargetProfileReference))
            {
                return false;
            }

            if (layout.TargetProfileReference == 0)
            {
                if (layout.TargetProfileId != 0
                    || layout.TargetProfileRuleRevision != 0)
                {
                    return false;
                }

                hasDefault = true;
                continue;
            }

            var profile = view.Site.Snapshot.TargetProfiles[
                checked((int)layout.TargetProfileReference - 1)];
            if (layout.TargetProfileId != profile.ProfileId
                || layout.TargetProfileRuleRevision
                    != profile.RuleRevision
                || profile.ProfileId != WindowsX64NativeProfileId
                || profile.RuleRevision
                    != WindowsX64NativeProfileRuleRevision)
            {
                return false;
            }

            hasWindowsProfile = true;
        }

        if (!hasDefault
            || (view.UnderlyingType
                    is PrimitiveType.NativeInt or PrimitiveType.NativeUInt
                && (!hasWindowsProfile
                    || view.Site.Snapshot.TargetRequirement
                        != WindowsX64TargetRequirement)))
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in view.Members)
        {
            if (member is null
                || !BoundEnumTypes.IsValidModuleDeclarationKey(
                    member.DeclarationKey)
                || !BoundEnumTypes.IsValidModuleDeclarationKey(member.Name)
                || !keys.Add(member.DeclarationKey)
                || !names.Add(member.Name))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidBinding(
        BoundStandardEnumExpectation expectation,
        BoundStandardEnumView declaration)
    {
        var binding = expectation.Binding;
        var expected = expectation.ExpectedDeclaration;
        if (binding is null
            || !IsValidSnapshot(binding.UsingModule)
            || !IsValidSnapshot(binding.Provider)
            || !SnapshotsMatch(binding.UsingModule,
                expected.Site.Snapshot)
            || !SnapshotsMatch(binding.Provider,
                declaration.Site.Snapshot)
            || binding.ImportRow <= 0
            || binding.ExpectedDefinitionId
                != expected.Site.DefinitionId
            || binding.ProviderDefinitionId
                != declaration.Site.DefinitionId
            || !StringComparer.Ordinal.Equals(binding.ProducerIdentity,
                declaration.Site.Snapshot.ModuleIdentity)
            || !StringComparer.Ordinal.Equals(
                declaration.Site.Snapshot.ModuleIdentity,
                binding.Provider.ModuleIdentity)
            || !StringComparer.Ordinal.Equals(
                declaration.Site.Snapshot.SchemaIdentity,
                expected.Site.Snapshot.SchemaIdentity)
            || !BoundEnumTypes.IsValidModuleDeclarationKey(
                binding.ExportKey)
            || !StringComparer.Ordinal.Equals(
                declaration.Site.Snapshot.ModuleIdentity,
                binding.ProducerIdentity)
            || binding.TargetRequirement
                != declaration.Site.Snapshot.TargetRequirement
            || binding.RuntimePolicy
                != declaration.Site.Snapshot.RuntimePolicy)
        {
            return false;
        }

        return true;
    }

    private static bool IsValidSnapshot(
        BoundSourceTypeProviderSnapshot snapshot)
        => snapshot is not null
            && snapshot.ArtifactSnapshot is { IsDefault: false, Length: > 0 }
            && snapshot.ModuleRow > 0
            && BoundEnumTypes.IsValidModuleDeclarationKey(
                snapshot.SchemaIdentity)
            && BoundEnumTypes.IsValidModuleDeclarationKey(
                snapshot.ModuleIdentity)
            && snapshot.TargetRequirement <= WindowsX64TargetRequirement
            && snapshot.RuntimePolicy <= 1
            && !snapshot.Features.IsDefault
            && !snapshot.TargetProfiles.IsDefault;

    private static bool MembersMatch(BoundEnumDefinition definition,
        BoundStandardEnumView view)
    {
        for (var index = 0; index < definition.Members.Length; index++)
        {
            var member = definition.Members[index];
            var snapshot = view.Members[index];
            if (!StringComparer.Ordinal.Equals(member.Name, snapshot.Name)
                || !StringComparer.Ordinal.Equals(member.DeclarationKey,
                    snapshot.DeclarationKey)
                || member.Value != snapshot.Value
                || member.IsPublic != definition.IsPublic)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValueInRange(
        ImmutableArray<BoundStandardEnumMemberView> members,
        PrimitiveType underlyingType)
    {
        var width = PrimitiveTypeFacts.IntegerWidthOrWide(underlyingType);
        if (width <= 0)
        {
            return false;
        }

        var isSigned = PrimitiveTypeFacts.IsSignedIntegerOrWide(
            underlyingType);
        var minimum = isSigned
            ? -(BigInteger.One << (width - 1)) : BigInteger.Zero;
        var maximum = isSigned
            ? (BigInteger.One << (width - 1)) - BigInteger.One
            : (BigInteger.One << width) - BigInteger.One;
        return members.All(member => member is not null
            && member.Value >= minimum && member.Value <= maximum);
    }

    private static bool AreUniqueKeys(ImmutableArray<string> keys)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        return keys.All(key =>
            BoundEnumTypes.IsValidModuleDeclarationKey(key)
            && unique.Add(key));
    }

    internal static bool IsPublic(uint typeDefFlags)
        => (typeDefFlags & 3) == 2;

    internal static string GetQualifiedName(BoundStandardEnumView view)
        => view.Namespace.Length == 0
            ? view.Name : $"{view.Namespace}.{view.Name}";

    internal static int GetBlittableTargetRequirement(
        ImmutableArray<BoundStandardEnumLayoutVariant> layouts)
        => layouts.Any(static layout => (layout.Flags & 1) != 0)
            ? checked((int)WindowsX64TargetRequirement) : 0;

    private static bool IsValidText(string? value,
        bool allowEmpty = false)
        => value is not null
            && (allowEmpty || value.Length != 0)
            && (value.Length == 0 ? allowEmpty
                : BoundEnumTypes.IsValidModuleDeclarationKey(value));

    private static bool SameSite(BoundStandardEnumTypeSite left,
        BoundStandardEnumTypeSite right)
        => left.DefinitionId == right.DefinitionId
            && SnapshotsMatch(left.Snapshot, right.Snapshot);

    private static readonly record struct LayoutContract(
        uint TargetProfileId, uint TargetProfileRuleRevision,
        byte Kind, byte Flags, uint Alignment, uint Pack)
    {
        internal static LayoutContract From(
            BoundStandardEnumLayoutVariant layout)
            => new(layout.TargetProfileId,
                layout.TargetProfileRuleRevision, layout.Kind,
                layout.Flags, layout.Alignment, layout.Pack);
    }
}
