// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>The kind of an imported generic nominal formal or substitution.</summary>
public enum BoundGenericArgumentKind
{
    /// <summary>No valid generic argument kind.</summary>
    None = 0,
    /// <summary>A complete type argument.</summary>
    Type = 1,
    /// <summary>A constant-source argument.</summary>
    Constant = 2,
}

/// <summary>Exact provider, logical module and TypeDef identity for a nominal declaration.</summary>
/// <param name="ProviderIdentity">The checked provider identity owning the declaration.</param>
/// <param name="ModuleIdentity">The exact logical Module identity within the provider.</param>
/// <param name="DefinitionId">The one-based TypeDef DefId.</param>
public readonly record struct BoundGenericNominalTypeIdentity(
    string ProviderIdentity, string ModuleIdentity, uint DefinitionId)
{
    /// <summary>Whether this identity names a TypeDef row in a selected provider module.</summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(ProviderIdentity)
        && !string.IsNullOrWhiteSpace(ModuleIdentity)
        && (DefinitionId >> 24) == 0x13
        && (DefinitionId & 0x00FF_FFFF) != 0;
}

/// <summary>An exact host-selected Core Task TypeDef identity.</summary>
/// <param name="Definition">The exact checked generic nominal TypeDef selected as Core Task.</param>
public readonly record struct CoreTaskTypeIdentity(BoundGenericNominalTypeIdentity Definition);

/// <summary>A generic formal's retained TypeSpec or ConstantSource default.</summary>
/// <param name="Kind">Whether the default is a type or constant source.</param>
/// <param name="Reference">The original TypeSpecRef or ConstantSource row.</param>
/// <param name="ProviderIdentity">The checked provider that owns the reference.</param>
/// <param name="DecodedType">A decoded type default when it is a supported primitive.</param>
/// <param name="TypeQualifiers">The retained qualifier bits of a decoded TypeSpec.</param>
public sealed record BoundGenericNominalTypeDefault(
    BoundGenericArgumentKind Kind, uint Reference, string ProviderIdentity,
    PrimitiveType? DecodedType = null, byte TypeQualifiers = 0)
{
    internal BoundGenericNominalTypeArgument? DecodedConstantArgument
        { get; init; }
    internal int? EarlierFormalOrdinal { get; init; }
}

/// <summary>A retained generic constraint row, with its exact assigned kind and payload.</summary>
/// <param name="Kind">The assigned Constraint kind.</param>
/// <param name="Payload">The kind-specific row or value payload.</param>
public readonly record struct BoundGenericNominalTypeConstraint(ushort Kind, uint Payload);

/// <summary>A generic nominal formal with its owner, ordinal, domains, defaults and constraints.</summary>
/// <param name="OwnerDefinitionId">The exact TypeDef DefId that owns this formal.</param>
/// <param name="Ordinal">The zero-based ordinal in the TypeDef formal range.</param>
/// <param name="Name">The declared formal name.</param>
/// <param name="Kind">Whether the formal accepts a type or constant argument.</param>
/// <param name="OwnershipDomain">The assigned type-formal ownership domain.</param>
/// <param name="NullabilityDomain">The assigned type-formal nullability domain.</param>
/// <param name="ConstantTypeSpecRef">The constant formal's value-domain TypeSpecRef.</param>
/// <param name="Default">The retained default reference, if present.</param>
/// <param name="Constraints">The ordered checked Constraint rows owned by the formal.</param>
public sealed record BoundGenericNominalTypeFormal(
    uint OwnerDefinitionId, uint Ordinal, string Name, BoundGenericArgumentKind Kind,
    byte OwnershipDomain, byte NullabilityDomain, uint ConstantTypeSpecRef,
    BoundGenericNominalTypeDefault? Default,
    ImmutableArray<BoundGenericNominalTypeConstraint> Constraints)
{
    internal PrimitiveType? ConstantDomainType { get; init; }
    internal PrimitiveType? ConstantCarrierType { get; init; }
    internal BoundGenericMethodTypeTerm? ConstantDomainTypeTerm
        { get; init; }
}

/// <summary>A nominal TypeDef imported from a structurally checked provider.</summary>
/// <param name="Identity">The exact provider, module and TypeDef identity.</param>
/// <param name="Namespace">The declaration namespace.</param>
/// <param name="Name">The declaration name.</param>
/// <param name="NominalKind">The assigned TypeDef nominal kind.</param>
/// <param name="Formals">The ordered TypeDef generic formals; empty for a nongeneric TypeDef.</param>
/// <param name="TypeSpecCount">The checked provider TypeSpec row count.</param>
/// <param name="ConstantSourceCount">The checked provider ConstantSource row count.</param>
/// <param name="ConstraintCount">The checked provider Constraint row count.</param>
/// <param name="AssociatedRelationCount">The checked provider AssociatedRelation row count.</param>
public sealed record BoundGenericNominalTypeDefinition(
    BoundGenericNominalTypeIdentity Identity, string Namespace, string Name,
    byte NominalKind, ImmutableArray<BoundGenericNominalTypeFormal> Formals,
    int TypeSpecCount, int ConstantSourceCount, int ConstraintCount,
    int AssociatedRelationCount)
{
    internal PrimitiveType? TransparentConstantCarrierType { get; init; }
    internal ImmutableArray<BoundSourceTypeExpectation> ProviderExpectations
        { get; init; } = [];
    internal BoundSourceTypeProviderSnapshot? Provider { get; init; }
    internal ImmutableArray<string> ExportKeys { get; init; } = [];
}

/// <summary>One ordered type or constant substitution for a generic nominal formal.</summary>
/// <param name="Kind">Whether this substitution is a type or constant.</param>
/// <param name="Type">The exact bound type when this is a type substitution.</param>
/// <param name="Qualifiers">The TypeSpec qualifier bits retained for this type node.</param>
/// <param name="ConstantLow">The low target-width constant bits.</param>
/// <param name="ConstantHigh">The high target-width constant bits.</param>
/// <param name="ConstantProviderIdentity">The provider owning a ConstantSource, if applicable.</param>
/// <param name="ConstantSourceRow">The provider-owned ConstantSource row, if applicable.</param>
/// <param name="TypeSpecForm">The assigned TypeSpec form for metadata-derived type arguments.</param>
/// <param name="TypeCode">The built-in type or family code, when retained.</param>
/// <param name="ArgumentCode">The built-in family argument code, when retained.</param>
/// <param name="NominalTypeIdentity">The exact identity of a nongeneric nominal type argument.</param>
/// <param name="TypeSpecReference">The original TypeSpecRef from checked metadata, if applicable.</param>
/// <param name="TypeSpecProviderIdentity">The provider owning the TypeSpecRef, if applicable.</param>
public sealed record BoundGenericNominalTypeArgument(
    BoundGenericArgumentKind Kind, PrimitiveType Type, byte Qualifiers,
    ulong ConstantLow, ulong ConstantHigh, string? ConstantProviderIdentity,
    uint ConstantSourceRow, byte TypeSpecForm = 0, byte TypeCode = 0,
    byte ArgumentCode = 0, BoundGenericNominalTypeIdentity? NominalTypeIdentity = null,
    uint TypeSpecReference = 0, string? TypeSpecProviderIdentity = null)
{
    internal PrimitiveType? ConstantDomainType { get; init; }
    internal PrimitiveType? ConstantCarrierType { get; init; }
    internal bool HasDecodedConstant { get; init; }
    internal int TypeFormalDefinitionOrdinal { get; init; } = -1;
    internal int TypeFormalOrdinal { get; init; } = -1;
    internal BoundGenericTypeFormalOwnerKind TypeFormalOwnerKind
        { get; init; } = BoundGenericTypeFormalOwnerKind.GenericMethod;
    internal BoundGenericMethodTypeTerm? TypeArgumentTerm
        { get; init; }
    internal bool HasTypeFormalOwnerIdentity =>
        TypeFormalDefinitionOrdinal >= 0
        && TypeFormalOwnerKind
            == BoundGenericTypeFormalOwnerKind.GenericMethod;
    internal bool HasTypeFormalIdentity =>
        HasTypeFormalOwnerIdentity && TypeFormalOrdinal >= 0;
    internal int ConstantFormalDefinitionOrdinal { get; init; } = -1;
    internal int ConstantFormalOrdinal { get; init; } = -1;
    internal BoundGenericTypeFormalOwnerKind ConstantFormalOwnerKind
        { get; init; } = BoundGenericTypeFormalOwnerKind.GenericMethod;
    internal BoundGenericMethodTypeTerm? ConstantFormalDomain
        { get; init; }
    internal bool HasConstantFormalIdentity =>
        ConstantFormalDefinitionOrdinal >= 0
        && ConstantFormalOrdinal >= 0
        && (ConstantFormalOwnerKind is
            BoundGenericTypeFormalOwnerKind.GenericMethod
                or BoundGenericTypeFormalOwnerKind.SourceGenericType)
        && ConstantFormalDomain is not null;

    /// <summary>Creates a type substitution without normalizing its bound type identity.</summary>
    /// <param name="type">The bound type identity to retain.</param>
    /// <param name="qualifiers">The qualifier bits attached to this type node.</param>
    public static BoundGenericNominalTypeArgument ForType(
        PrimitiveType type, byte qualifiers = 0) =>
        new(BoundGenericArgumentKind.Type, type, qualifiers, 0, 0, null, 0);

    /// <summary>Creates a nominal TypeDef argument whose exact provider identity is retained.</summary>
    /// <param name="identity">The exact nominal TypeDef identity.</param>
    /// <param name="typeSpecForm">The source TypeSpec form for this node.</param>
    /// <param name="qualifiers">The TypeSpec qualifier bits for this node.</param>
    /// <param name="typeSpecReference">The original metadata TypeSpecRef, if present.</param>
    /// <param name="typeSpecProviderIdentity">The provider owning the TypeSpecRef.</param>
    public static BoundGenericNominalTypeArgument ForNominalType(
        BoundGenericNominalTypeIdentity identity, byte typeSpecForm = 0x02,
        byte qualifiers = 0, uint typeSpecReference = 0,
        string? typeSpecProviderIdentity = null) =>
        new(BoundGenericArgumentKind.Type, PrimitiveType.Void, qualifiers, 0, 0,
            null, 0, typeSpecForm, 0, 0, identity, typeSpecReference,
            typeSpecProviderIdentity);

    /// <summary>Creates a type substitution decoded from a checked TypeSpecRef.</summary>
    /// <param name="type">The decoded bound type identity.</param>
    /// <param name="qualifiers">The TypeSpec qualifier bits.</param>
    /// <param name="typeSpecReference">The checked source TypeSpecRef.</param>
    /// <param name="providerIdentity">The provider owning the TypeSpecRef.</param>
    /// <param name="typeSpecForm">The TypeSpec form, if it remains semantically relevant.</param>
    /// <param name="typeCode">The built-in type or family code, when retained.</param>
    /// <param name="argumentCode">The built-in family argument code, when retained.</param>
    public static BoundGenericNominalTypeArgument ForMetadataType(
        PrimitiveType type, byte qualifiers, uint typeSpecReference,
        string providerIdentity, byte typeSpecForm = 0, byte typeCode = 0,
        byte argumentCode = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerIdentity);
        if (!BoundGenericNominalTypeContract.IsTypeSpecReference(typeSpecReference))
        {
            throw new ArgumentOutOfRangeException(nameof(typeSpecReference));
        }

        return new BoundGenericNominalTypeArgument(BoundGenericArgumentKind.Type,
            type, qualifiers, 0, 0, null, 0, typeSpecForm, typeCode,
            argumentCode, null, typeSpecReference, providerIdentity);
    }

    /// <summary>Creates an inline constant substitution with its complete target-width bits.</summary>
    /// <param name="low">The low 64 bits of the constant representation.</param>
    /// <param name="high">The high 64 bits of the constant representation.</param>
    public static BoundGenericNominalTypeArgument ForConstant(
        ulong low, ulong high = 0) =>
        new(BoundGenericArgumentKind.Constant, PrimitiveType.Void, 0, low, high, null, 0);

    internal static BoundGenericNominalTypeArgument ForConstant(
        PrimitiveType domainType, PrimitiveType carrierType,
        ulong low, ulong high = 0) =>
        new(BoundGenericArgumentKind.Constant, PrimitiveType.Void, 0,
            low, high, null, 0) {
            ConstantDomainType = domainType,
            ConstantCarrierType = carrierType,
            HasDecodedConstant = true,
        };

    internal static BoundGenericNominalTypeArgument ForConstantFormal(
        int definitionOrdinal, int constantOrdinal,
        BoundGenericMethodTypeTerm domain,
        BoundGenericTypeFormalOwnerKind ownerKind
            = BoundGenericTypeFormalOwnerKind.GenericMethod)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (definitionOrdinal < 0 || constantOrdinal < 0
            || ownerKind is not (
                BoundGenericTypeFormalOwnerKind.GenericMethod
                or BoundGenericTypeFormalOwnerKind.SourceGenericType))
        {
            throw new ArgumentOutOfRangeException(nameof(definitionOrdinal));
        }

        return new BoundGenericNominalTypeArgument(
            BoundGenericArgumentKind.Constant, PrimitiveType.Void, 0,
            0, 0, null, 0) {
            ConstantFormalDefinitionOrdinal = definitionOrdinal,
            ConstantFormalOrdinal = constantOrdinal,
            ConstantFormalOwnerKind = ownerKind,
            ConstantFormalDomain = domain,
        };
    }

    /// <summary>Creates a provider-owned ConstantSource substitution without replacing its identity.</summary>
    /// <param name="providerIdentity">The checked provider identity owning the row.</param>
    /// <param name="sourceRow">The one-based ConstantSource row.</param>
    public static BoundGenericNominalTypeArgument ForConstantSource(
        string providerIdentity, uint sourceRow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerIdentity);
        if (sourceRow is 0 or > 0x00FF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRow));
        }

        return new BoundGenericNominalTypeArgument(
            BoundGenericArgumentKind.Constant, PrimitiveType.Void, 0, 0, 0,
            providerIdentity, sourceRow);
    }

    internal static BoundGenericNominalTypeArgument ForConstantSource(
        string providerIdentity, uint sourceRow,
        PrimitiveType domainType, PrimitiveType carrierType,
        ulong low, ulong high = 0) =>
        ForConstantSource(providerIdentity, sourceRow) with {
            ConstantDomainType = domainType,
            ConstantCarrierType = carrierType,
            ConstantLow = low,
            ConstantHigh = high,
            HasDecodedConstant = true,
        };
}

/// <summary>A direct or constructed nominal TypeDef use retained in a bound program.</summary>
/// <param name="Definition">The exact nominal TypeDef identity being instantiated.</param>
/// <param name="Arguments">The ordered type or constant substitutions.</param>
/// <param name="Qualifiers">The outer TypeSpec qualifier bits.</param>
/// <param name="TypeSpecReference">The original constructed TypeSpecRef, if metadata-derived.</param>
/// <param name="TypeSpecProviderIdentity">The provider owning the TypeSpecRef, if metadata-derived.</param>
/// <param name="TypeSpecForm">The outer TypeSpec form when not a bare construction.</param>
public sealed record BoundGenericNominalTypeInstance(
    BoundGenericNominalTypeIdentity Definition,
    ImmutableArray<BoundGenericNominalTypeArgument> Arguments,
    byte Qualifiers, uint TypeSpecReference = 0,
    string? TypeSpecProviderIdentity = null, byte TypeSpecForm = 0);

/// <summary>Maps compilation-local nominal TypeDef identities carried by <see cref="PrimitiveType"/>.</summary>
public static class BoundGenericNominalTypeTypes
{
    private const int First = -2_000_000;

    /// <summary>The largest generic nominal definition/instance table accepted by the verifier.</summary>
    public const int MaximumCount = SemanticVerifier.MaximumAnalysisVisits;

    /// <summary>Whether a primitive type value encodes a generic nominal instance.</summary>
    /// <param name="type">The type value to inspect.</param>
    public static bool IsType(PrimitiveType type) =>
        (int)type is <= First and > First - MaximumCount;

    /// <summary>Gets the instance-table index encoded by a generic nominal type value.</summary>
    /// <param name="type">The generic nominal type value.</param>
    public static int Index(PrimitiveType type)
    {
        if (!IsType(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        return First - (int)type;
    }

    internal static PrimitiveType Type(int index)
    {
        if ((uint)index >= MaximumCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return (PrimitiveType)(First - index);
    }
}

internal static class BoundGenericNominalTypeContract
{
    internal static bool AreEquivalentDefinitions(
        BoundGenericNominalTypeDefinition left,
        BoundGenericNominalTypeDefinition right)
    {
        if (left.Identity != right.Identity
            || left.Namespace != right.Namespace
            || left.Name != right.Name
            || left.NominalKind != right.NominalKind
            || left.TypeSpecCount != right.TypeSpecCount
            || left.ConstantSourceCount != right.ConstantSourceCount
            || left.ConstraintCount != right.ConstraintCount
            || left.AssociatedRelationCount != right.AssociatedRelationCount
            || left.TransparentConstantCarrierType
                != right.TransparentConstantCarrierType
            || (!left.ExportKeys.IsDefaultOrEmpty
                && !right.ExportKeys.IsDefaultOrEmpty
                && !left.ExportKeys.AsSpan().SequenceEqual(
                    right.ExportKeys.AsSpan()))
            || (left.Provider is BoundSourceTypeProviderSnapshot
                    leftProvider
                && right.Provider is BoundSourceTypeProviderSnapshot
                    rightProvider
                && !AreEquivalentProviderSnapshots(leftProvider,
                    rightProvider))
            || left.Formals.Length != right.Formals.Length)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < left.Formals.Length; ordinal++)
        {
            var leftFormal = left.Formals[ordinal];
            var rightFormal = right.Formals[ordinal];
            if (leftFormal.OwnerDefinitionId
                    != rightFormal.OwnerDefinitionId
                || leftFormal.Ordinal != rightFormal.Ordinal
                || leftFormal.Name != rightFormal.Name
                || leftFormal.Kind != rightFormal.Kind
                || leftFormal.OwnershipDomain
                    != rightFormal.OwnershipDomain
                || leftFormal.NullabilityDomain
                    != rightFormal.NullabilityDomain
                || leftFormal.ConstantTypeSpecRef
                    != rightFormal.ConstantTypeSpecRef
                || leftFormal.Default != rightFormal.Default
                || leftFormal.ConstantDomainType
                    != rightFormal.ConstantDomainType
                || leftFormal.ConstantCarrierType
                    != rightFormal.ConstantCarrierType
                || leftFormal.ConstantDomainTypeTerm
                    != rightFormal.ConstantDomainTypeTerm
                || !leftFormal.Constraints.SequenceEqual(
                    rightFormal.Constraints))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool AreEquivalentProviderExpectations(
        BoundSourceTypeExpectation left,
        BoundSourceTypeExpectation right) =>
        left.ImportRow == right.ImportRow
        && left.ExpectedDefinitionId == right.ExpectedDefinitionId
        && StringComparer.Ordinal.Equals(left.ProducerIdentity,
            right.ProducerIdentity)
        && StringComparer.Ordinal.Equals(left.ExportKey, right.ExportKey)
        && left.TargetRequirement == right.TargetRequirement
        && left.RuntimePolicy == right.RuntimePolicy
        && left.ProviderDefinitionId == right.ProviderDefinitionId
        && AreEquivalentProviderSnapshots(left.UsingModule,
            right.UsingModule)
        && AreEquivalentProviderSnapshots(left.Provider, right.Provider);

    private static bool AreEquivalentProviderSnapshots(
        BoundSourceTypeProviderSnapshot left,
        BoundSourceTypeProviderSnapshot right) =>
        StringComparer.Ordinal.Equals(left.SchemaIdentity,
            right.SchemaIdentity)
        && left.ArtifactSnapshot.AsSpan().SequenceEqual(
            right.ArtifactSnapshot.AsSpan())
        && left.ModuleRow == right.ModuleRow
        && StringComparer.Ordinal.Equals(left.ModuleIdentity,
            right.ModuleIdentity)
        && left.GoverningRuleEdition == right.GoverningRuleEdition
        && left.TargetRequirement == right.TargetRequirement
        && left.RuntimePolicy == right.RuntimePolicy
        && left.Features.AsSpan().SequenceEqual(
            right.Features.AsSpan())
        && left.TargetProfiles.AsSpan().SequenceEqual(
            right.TargetProfiles.AsSpan());

    internal static bool IsValidDefinitionTable(
        ImmutableArray<BoundGenericNominalTypeDefinition> definitions)
    {
        if (definitions.IsDefault || definitions.Length > BoundGenericNominalTypeTypes.MaximumCount)
        {
            return false;
        }

        var identities = new HashSet<BoundGenericNominalTypeIdentity>();
        var providerRows = new Dictionary<string,
            (int TypeSpecs, int ConstantSources, int Constraints, int Relations)>(
                StringComparer.Ordinal);
        long analysisRows = 0;
        foreach (var definition in definitions)
        {
            if (definition is null || !definition.Identity.IsValid
                || !IsValidNamespace(definition.Namespace)
                || !IsValidText(definition.Name)
                || definition.NominalKind is < 1 or > 3
                || definition.Formals.IsDefault
                || definition.Formals.Length > SemanticVerifier.MaximumAnalysisVisits
                || !ValidCount(definition.TypeSpecCount)
                || !ValidCount(definition.ConstantSourceCount)
                || !ValidCount(definition.ConstraintCount)
                || !ValidCount(definition.AssociatedRelationCount)
                || (definition.TransparentConstantCarrierType
                    is PrimitiveType transparentCarrier
                    && (definition.NominalKind != 1
                        || (!PrimitiveTypeFacts.IsIntegerOrWide(
                                transparentCarrier)
                            && transparentCarrier
                                != PrimitiveType.Bool)))
                || definition.ProviderExpectations.IsDefault
                || definition.ExportKeys.IsDefault
                || definition.ExportKeys.Any(static key => !IsValidText(key))
                || definition.ExportKeys.Distinct(StringComparer.Ordinal)
                    .Count() != definition.ExportKeys.Length
                || (definition.Provider is
                        BoundSourceTypeProviderSnapshot provider
                    && (!IsValidText(provider.SchemaIdentity)
                        || provider.ModuleRow <= 0
                        || !IsValidText(provider.ModuleIdentity)
                        || provider.ModuleIdentity
                            != definition.Identity.ModuleIdentity
                        || provider.ArtifactSnapshot.IsDefaultOrEmpty
                        || definition.ExportKeys.IsEmpty
                        || definition.ProviderExpectations.Any(
                            expectation =>
                                !AreEquivalentProviderSnapshots(provider,
                                    expectation.Provider))))
                || definition.ProviderExpectations.Any(expectation =>
                    !IsValidProviderExpectation(definition, expectation))
                || !identities.Add(definition.Identity))
            {
                return false;
            }
            if (providerRows.TryGetValue(definition.Identity.ProviderIdentity,
                out var existingCounts))
            {
                if (existingCounts != (definition.TypeSpecCount,
                    definition.ConstantSourceCount, definition.ConstraintCount,
                    definition.AssociatedRelationCount))
                {
                    return false;
                }
            }
            else
            {
                providerRows.Add(definition.Identity.ProviderIdentity,
                    (definition.TypeSpecCount, definition.ConstantSourceCount,
                        definition.ConstraintCount, definition.AssociatedRelationCount));
            }
            analysisRows += definition.Formals.Length;
            if (analysisRows > SemanticVerifier.MaximumAnalysisVisits)
            {
                return false;
            }

            var formalNames = new HashSet<string>(StringComparer.Ordinal);
            for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
            {
                var formal = definition.Formals[ordinal];
                if (formal is null || formal.OwnerDefinitionId != definition.Identity.DefinitionId
                    || formal.Ordinal != (uint)ordinal || !IsValidText(formal.Name)
                    || !formalNames.Add(formal.Name) || formal.Constraints.IsDefault
                    || !IsValidFormal(formal, definition, definitions)
                    || formal.Constraints.Any(constraint =>
                        !IsValidConstraint(constraint, definition)))
                {
                    return false;
                }
                analysisRows += formal.Constraints.Length;
                if (analysisRows > SemanticVerifier.MaximumAnalysisVisits)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsValidProviderExpectation(
        BoundGenericNominalTypeDefinition definition,
        BoundSourceTypeExpectation expectation)
    {
        return expectation is not null
            && expectation.UsingModule is BoundSourceTypeProviderSnapshot
                usingModule
            && expectation.Provider is BoundSourceTypeProviderSnapshot
                provider
            && expectation.ImportRow > 0
            && (expectation.ExpectedDefinitionId >> 24) == 0x13
            && (expectation.ExpectedDefinitionId & 0x00FF_FFFF) != 0
            && IsValidText(expectation.ProducerIdentity)
            && expectation.ProducerIdentity
                == definition.Identity.ProviderIdentity
            && IsValidText(expectation.ExportKey)
            && expectation.TargetRequirement
                == provider.TargetRequirement
            && expectation.RuntimePolicy == provider.RuntimePolicy
            && expectation.TargetRequirement
                == usingModule.TargetRequirement
            && expectation.RuntimePolicy == usingModule.RuntimePolicy
            && expectation.ProviderDefinitionId
                == definition.Identity.DefinitionId
            && provider.ModuleRow > 0
            && IsValidText(provider.SchemaIdentity)
            && IsValidText(provider.ModuleIdentity)
            && provider.ModuleIdentity
                == definition.Identity.ModuleIdentity
            && !provider.ArtifactSnapshot.IsDefaultOrEmpty
            && usingModule.ModuleRow > 0
            && IsValidText(usingModule.SchemaIdentity)
            && IsValidText(usingModule.ModuleIdentity)
            && !usingModule.ArtifactSnapshot.IsDefaultOrEmpty;
    }

    internal static bool IsValidCoreTaskSelection(
        CoreTaskTypeIdentity? selection,
        ImmutableArray<BoundGenericNominalTypeDefinition> definitions)
    {
        if (selection is not CoreTaskTypeIdentity selected)
        {
            return true;
        }

        if (!selected.Definition.IsValid)
        {
            return false;
        }

        var matches = definitions.Where(definition =>
            definition.Identity == selected.Definition).ToArray();
        return matches.Length == 1
            && matches[0].Namespace == "Terrum.Threading.Tasks"
            && matches[0].Name == "Task"
            && matches[0].NominalKind == 2
            && matches[0].Formals.Length == 1
            && matches[0].Formals[0].Kind == BoundGenericArgumentKind.Type
            && matches[0].Formals[0].Name == "T"
            && matches[0].Formals[0].Ordinal == 0
            && matches[0].Formals[0].Default is
            {
                Kind: BoundGenericArgumentKind.Type,
                Reference: not 0,
                ProviderIdentity: var providerIdentity,
                DecodedType: PrimitiveType.Void,
                TypeQualifiers: 0,
            }
            && providerIdentity == selected.Definition.ProviderIdentity;
    }

    internal static bool IsValidInstanceTable(
        ImmutableArray<BoundGenericNominalTypeInstance> instances,
        ImmutableArray<BoundGenericNominalTypeDefinition> definitions)
    {
        if (instances.IsDefault || instances.Length > BoundGenericNominalTypeTypes.MaximumCount
            || !IsValidDefinitionTable(definitions))
        {
            return false;
        }

        var byIdentity = definitions.ToDictionary(static definition => definition.Identity);
        var providerRows =
            new Dictionary<string, (int TypeSpecs, int ConstantSources,
                int Constraints, int Relations)>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            _ = providerRows.TryAdd(definition.Identity.ProviderIdentity,
                (definition.TypeSpecCount, definition.ConstantSourceCount,
                    definition.ConstraintCount, definition.AssociatedRelationCount));
        }
        long argumentCount = 0;
        foreach (var instance in instances)
        {
            if (instance is null || instance.Arguments.IsDefault)
            {
                return false;
            }
            argumentCount += instance.Arguments.Length;
            if (argumentCount > SemanticVerifier.MaximumAnalysisVisits)
            {
                return false;
            }
        }
        var states = new byte[instances.Length];
        for (var index = 0; index < instances.Length; index++)
        {
            if (!IsValidInstance(index, instances, byIdentity, providerRows, states, 0))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidType(
        PrimitiveType type, BoundProgram program) =>
        BoundGenericNominalTypeTypes.IsType(type)
        && (uint)BoundGenericNominalTypeTypes.Index(type)
            < (uint)program.GenericNominalTypeInstances.Length;

    internal static bool TryGetDefinition(
        PrimitiveType type, BoundProgram program,
        out BoundGenericNominalTypeDefinition? definition)
    {
        if (IsValidType(type, program))
        {
            var instance = program.GenericNominalTypeInstances[
                BoundGenericNominalTypeTypes.Index(type)];
            definition = program.GenericNominalTypeDefinitions.FirstOrDefault(candidate =>
                candidate.Identity == instance.Definition);
            return definition is not null;
        }

        definition = null;
        return false;
    }

    internal static bool IsValidArgumentTypes(BoundProgram program)
    {
        var validTypeArguments = true;
        if (!BoundTreeTraversal.VisitGenericNominalTypeArguments(program, type =>
        {
            if (!SemanticVerifier.IsValidType(type, true, program))
            {
                validTypeArguments = false;
            }
        })
            || !validTypeArguments)
        {
            return false;
        }

        var typeFormalsByOwner = new Dictionary<int,
            ImmutableArray<BoundGenericMethodFormal>>();
        foreach (var definition in program.GenericMethodDefinitions)
        {
            if (definition.FormalTypeOwnerOrdinal >= 0
                && !AddFormalOwner(definition.FormalTypeOwnerOrdinal,
                    definition.Formals))
            {
                return false;
            }
        }
        foreach (var function in program.Functions)
        {
            if (function.ImportedGenericMethod is
                BoundGenericMethodImport import
                && import.FormalTypeOwnerOrdinal >= 0
                && !AddFormalOwner(import.FormalTypeOwnerOrdinal,
                    import.Formals))
            {
                return false;
            }
        }
        foreach (var owner in program.GenericMethodTypeFormalOwners)
        {
            if (owner.Ordinal < 0
                || typeFormalsByOwner.ContainsKey(owner.Ordinal))
            {
                continue;
            }

            typeFormalsByOwner.Add(owner.Ordinal, [
                .. owner.Formals.Select(static formal =>
                    new BoundGenericMethodFormal(formal.Ordinal,
                        string.Empty, formal.PlaceholderType,
                        formal.OwnershipDomain,
                        formal.NullabilityDomain, []) {
                        RequiresWritable = formal.RequiresWritable,
                        RequiresBlittable = formal.RequiresBlittable,
                    }),
            ]);
        }
        var typeOwnerFunctions = new Dictionary<int, BoundFunction>(
            typeFormalsByOwner.Count);
        // The verifier's term builder consumes formal identity through a function context.
        foreach (var (ownerOrdinal, formals) in typeFormalsByOwner)
        {
            typeOwnerFunctions.Add(ownerOrdinal, new BoundFunction(
                "$generic-nominal-type-argument", PrimitiveType.Void, [], 0,
                new BoundBlock([], default), default) {
                GenericMethodProofTypeFormalOwnerOrdinal = ownerOrdinal,
                GenericMethodProofTypeParameters = [.. formals.Select(
                    static formal => formal.PlaceholderType)],
            });
        }

        foreach (var instance in program.GenericNominalTypeInstances)
        {
            var definition = program.GenericNominalTypeDefinitions.First(candidate =>
                candidate.Identity == instance.Definition);
            for (var ordinal = 0; ordinal < instance.Arguments.Length; ordinal++)
            {
                var formal = definition.Formals[ordinal];
                var argument = instance.Arguments[ordinal];
                if ((formal.Kind == BoundGenericArgumentKind.Type
                    && (argument.Kind != BoundGenericArgumentKind.Type
                        || argument.ConstantFormalDefinitionOrdinal != -1
                        || argument.ConstantFormalOrdinal != -1
                        || argument.ConstantFormalOwnerKind
                            != BoundGenericTypeFormalOwnerKind.GenericMethod
                        || argument.ConstantFormalDomain is not null
                        || !IsAdmittedByDomains(formal, argument, program,
                            typeFormalsByOwner, typeOwnerFunctions)))
                    || (formal.Kind == BoundGenericArgumentKind.Constant
                        && (argument.Kind
                                != BoundGenericArgumentKind.Constant
                            || (argument.HasConstantFormalIdentity
                                ? !IsValidConstantFormalArgument(formal,
                                    argument, program)
                                : argument.HasDecodedConstant
                                ? !IsValidDecodedConstantArgument(formal,
                                    argument, program)
                                : argument.ConstantFormalDefinitionOrdinal
                                        != -1
                                    || argument.ConstantFormalOrdinal != -1
                                    || argument.ConstantFormalOwnerKind
                                        != BoundGenericTypeFormalOwnerKind
                                            .GenericMethod
                                    || argument.ConstantFormalDomain
                                        is not null
                                    || argument.ConstantDomainType is not null
                                    || argument.ConstantCarrierType
                                        is not null))))
                {
                    return false;
                }
            }
        }
        return true;

        bool AddFormalOwner(int ownerOrdinal,
            ImmutableArray<BoundGenericMethodFormal> formals)
        {
            if (formals.IsDefault)
            {
                return false;
            }
            if (typeFormalsByOwner.TryGetValue(ownerOrdinal,
                    out var existingFormals))
            {
                return existingFormals.AsSpan().SequenceEqual(
                    formals.AsSpan());
            }

            typeFormalsByOwner.Add(ownerOrdinal, formals);
            return true;
        }
    }

    private static bool IsValidConstantFormalArgument(
        BoundGenericNominalTypeFormal targetFormal,
        BoundGenericNominalTypeArgument argument, BoundProgram program)
    {
        if (argument.ConstantFormalDefinitionOrdinal < 0
            || argument.ConstantFormalOrdinal < 0
            || argument.ConstantFormalDomain is not
                BoundGenericMethodTypeTerm domain
            || argument.ConstantDomainType is not null
            || argument.ConstantCarrierType is not null
            || argument.HasDecodedConstant
            || argument.ConstantLow != 0 || argument.ConstantHigh != 0
            || argument.ConstantProviderIdentity is not null
            || argument.ConstantSourceRow != 0
            || argument.ConstantFormalOwnerKind is not (
                BoundGenericTypeFormalOwnerKind.GenericMethod
                or BoundGenericTypeFormalOwnerKind.SourceGenericType))
        {
            return false;
        }

        if (argument.ConstantFormalOwnerKind
            == BoundGenericTypeFormalOwnerKind.SourceGenericType)
        {
            if ((uint)argument.ConstantFormalDefinitionOrdinal
                    >= (uint)program.SourceGenericTypeDefinitions.Length)
            {
                return false;
            }

            var sourceDefinition = program.SourceGenericTypeDefinitions[
                argument.ConstantFormalDefinitionOrdinal];
            var sourceFormal = sourceDefinition.ConstantFormals
                .FirstOrDefault(candidate => candidate.ConstantOrdinal
                    == argument.ConstantFormalOrdinal);
            return sourceFormal is not null
                && sourceFormal.Domain is not null
                && (targetFormal.ConstantDomainType
                        is not PrimitiveType targetDomain
                    || (domain.Kind
                            == BoundGenericMethodTypeTermKind.Concrete
                        && domain.ConcreteType == targetDomain));
        }

        if ((uint)argument.ConstantFormalDefinitionOrdinal
            >= (uint)program.GenericMethodSignatures.Length)
        {
            return false;
        }

        var signature = program.GenericMethodSignatures[
            argument.ConstantFormalDefinitionOrdinal];
        if (signature is null || signature.ConstantFormals.IsDefault
            || (uint)argument.ConstantFormalOrdinal
                >= (uint)signature.ConstantFormals.Length)
        {
            return false;
        }

        var callerFormal = signature.ConstantFormals[
            argument.ConstantFormalOrdinal];
        return callerFormal is not null
            && callerFormal.ConstantOrdinal
                == argument.ConstantFormalOrdinal
            && SemanticVerifier.AreGenericMethodTypeTermsEqual(
                domain, callerFormal.Domain, 0)
            && (targetFormal.ConstantDomainType
                    is not PrimitiveType targetDomain
                || (domain.Kind
                    == BoundGenericMethodTypeTermKind.Concrete
                    && domain.ConcreteType == targetDomain));
    }

    internal static bool IsValidConstantFormalUse(PrimitiveType type,
        BoundFunction? callerFunction, BoundProgram program)
    {
        var visited = new HashSet<int>();
        return IsValidConstantFormalUse(type, callerFunction, program,
            visited);
    }

    private static bool IsValidConstantFormalUse(PrimitiveType type,
        BoundFunction? callerFunction, BoundProgram program,
        HashSet<int> visited)
    {
        if (!BoundGenericNominalTypeTypes.IsType(type))
        {
            return true;
        }

        var index = BoundGenericNominalTypeTypes.Index(type);
        if ((uint)index >= (uint)program.GenericNominalTypeInstances.Length
            || !visited.Add(index))
        {
            return (uint)index
                < (uint)program.GenericNominalTypeInstances.Length;
        }

        var instance = program.GenericNominalTypeInstances[index];
        foreach (var argument in instance.Arguments)
        {
            if (argument.HasConstantFormalIdentity
                && !IsValidConstantFormalUse(argument, callerFunction,
                    program))
            {
                return false;
            }

            if (argument.Kind == BoundGenericArgumentKind.Type
                && !IsValidConstantFormalUse(argument.Type,
                    callerFunction, program, visited))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidConstantFormalUse(
        BoundGenericNominalTypeArgument argument,
        BoundFunction? callerFunction, BoundProgram program)
    {
        if (!argument.HasConstantFormalIdentity)
        {
            return false;
        }

        if (argument.ConstantFormalOwnerKind
            == BoundGenericTypeFormalOwnerKind.GenericMethod)
        {
            if (callerFunction?.GenericMethodProofDefinitionOrdinal
                    is not int callerDefinitionOrdinal
                || (uint)callerDefinitionOrdinal
                    >= (uint)program.GenericMethodSignatures.Length)
            {
                return false;
            }

            var callerSignature = program.GenericMethodSignatures[
                callerDefinitionOrdinal];
            return callerSignature is not null
                && !callerSignature.ConstantFormals.IsDefault
                && argument.ConstantFormalDefinitionOrdinal
                    == callerDefinitionOrdinal
                && argument.ConstantFormalOrdinal
                    < callerSignature.ConstantFormals.Length
                && SemanticVerifier.AreGenericMethodTypeTermsEqual(
                    argument.ConstantFormalDomain,
                    callerSignature.ConstantFormals[
                        argument.ConstantFormalOrdinal].Domain, 0);
        }

        if (argument.ConstantFormalOwnerKind
                != BoundGenericTypeFormalOwnerKind.SourceGenericType
            || (uint)argument.ConstantFormalDefinitionOrdinal
                >= (uint)program.SourceGenericTypeDefinitions.Length)
        {
            return false;
        }

        var sourceDefinition = program.SourceGenericTypeDefinitions[
            argument.ConstantFormalDefinitionOrdinal];
        if (sourceDefinition.ConstantFormals.IsDefault)
        {
            return false;
        }
        var sourceFormal = sourceDefinition.ConstantFormals.FirstOrDefault(
            candidate => candidate.ConstantOrdinal
                == argument.ConstantFormalOrdinal);
        if (sourceFormal is null)
        {
            return false;
        }

        if (callerFunction is null)
        {
            return SemanticVerifier.AreGenericMethodTypeTermsEqual(
                sourceFormal.Domain, argument.ConstantFormalDomain, 0);
        }

        if (callerFunction.GenericMethodProofDefinitionOrdinal
                is not int callerDefinitionOrdinal
            || (uint)callerDefinitionOrdinal
                >= (uint)program.GenericMethodSignatures.Length)
        {
            return false;
        }

        var callerSignature = program.GenericMethodSignatures[
            callerDefinitionOrdinal];
        if (callerSignature is null
            || callerSignature.Owner
                is not BoundGenericMethodOwner owner
            || owner.SourceGenericTypeDefinitionIndex
                != argument.ConstantFormalDefinitionOrdinal
            || owner.TypeArguments.IsDefault
            || owner.ConstantArguments.IsDefault
            || owner.ConstantArguments.Length
                != sourceDefinition.ConstantFormals.Length
            || (uint)argument.ConstantFormalOrdinal
                >= (uint)owner.ConstantArguments.Length)
        {
            return false;
        }

        var selectedOwnerArgument = owner.ConstantArguments[
            argument.ConstantFormalOrdinal];
        if (selectedOwnerArgument.Kind
                != BoundGenericMethodConstantArgumentKind.Formal
            || !SemanticVerifier.IsValidGenericMethodConstantFormalIdentity(
                selectedOwnerArgument, program))
        {
            return false;
        }

        var ownerTypeArguments = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(owner.TypeArguments.Length);
        foreach (var typeArgument in owner.TypeArguments)
        {
            if (!SemanticVerifier.TryGetGenericMethodActualTypeTerm(
                typeArgument, callerFunction, program, 0,
                out var typeTerm))
            {
                return false;
            }
            ownerTypeArguments.Add(typeTerm);
        }

        var ownerConstantArguments = owner.ConstantArguments
            .Select(static actual =>
                (BoundGenericMethodConstantArgument?)actual)
            .ToImmutableArray();
        return BoundGenericMethodTypeTerms
                .TrySubstituteSourceGenericTypeFormals(
                    sourceFormal.Domain, ownerTypeArguments.MoveToImmutable(),
                    sourceDefinition.DefinitionIndex,
                    ownerConstantArguments, out var expectedDomain)
            && SemanticVerifier.AreGenericMethodTypeTermsEqual(
                expectedDomain, argument.ConstantFormalDomain, 0);
    }

    internal static bool UsesAny(BoundProgram program) =>
        program.GenericNominalTypeInstances.Length != 0
        || program.Functions.Any(static function =>
            BoundGenericNominalTypeTypes.IsType(function.ReturnType)
            || function.Variables.Any(static variable =>
                BoundGenericNominalTypeTypes.IsType(variable.Type)));

    internal static bool IsCoreTaskType(PrimitiveType type, BoundProgram program) =>
        IsCoreTaskType(type, (IReadOnlyList<BoundGenericNominalTypeInstance>)
            program.GenericNominalTypeInstances, program.CoreTaskType);

    internal static bool IsCoreTaskType(PrimitiveType type,
        IReadOnlyList<BoundGenericNominalTypeInstance> instances,
        CoreTaskTypeIdentity? selection) =>
        selection is CoreTaskTypeIdentity coreTask
        && BoundGenericNominalTypeTypes.IsType(type)
        && (uint)BoundGenericNominalTypeTypes.Index(type) < (uint)instances.Count
        && instances[BoundGenericNominalTypeTypes.Index(type)]
            .Definition == coreTask.Definition;

    internal static bool IsNominalClassType(PrimitiveType type,
        IReadOnlyList<BoundGenericNominalTypeInstance> instances,
        IReadOnlyList<BoundGenericNominalTypeDefinition> definitions)
        => BoundGenericNominalTypeTypes.IsType(type)
            && (uint)BoundGenericNominalTypeTypes.Index(type) < (uint)instances.Count
            && definitions.Any(definition =>
                definition.Identity == instances[BoundGenericNominalTypeTypes.Index(type)]
                    .Definition
                && definition.NominalKind == 2);

    internal static bool IsGenericNominalTypeName(string qualifiedName) =>
        qualifiedName == "Terrum.Threading.Tasks.Task";

    internal static string CreateUseKey(
        BoundGenericNominalTypeIdentity identity,
        ImmutableArray<BoundGenericNominalTypeArgument> arguments,
        byte qualifiers, byte typeSpecForm = 0)
    {
        var key = new StringBuilder()
            .Append(identity.ProviderIdentity.Length).Append(':').Append(identity.ProviderIdentity)
            .Append(identity.ModuleIdentity.Length).Append(':').Append(identity.ModuleIdentity)
            .Append(':').Append(identity.DefinitionId).Append(':').Append(qualifiers);
        foreach (var argument in arguments)
        {
            _ = key.Append(';').Append((byte)argument.Kind).Append(':')
                .Append((int)argument.Type).Append(':')
                .Append(argument.Qualifiers);
            if (argument.HasConstantFormalIdentity)
            {
                _ = key.Append(":F:")
                    .Append((byte)argument.ConstantFormalOwnerKind)
                    .Append(':')
                    .Append(argument.ConstantFormalDefinitionOrdinal)
                    .Append(':').Append(argument.ConstantFormalOrdinal);
            }
            else if (argument.HasTypeFormalOwnerIdentity)
            {
                _ = key.Append(":TF:")
                    .Append((byte)argument.TypeFormalOwnerKind)
                    .Append(':')
                    .Append(argument.TypeFormalDefinitionOrdinal)
                    .Append(':').Append(argument.TypeFormalOrdinal);
            }
            else if (argument.Kind == BoundGenericArgumentKind.Constant
                && argument.HasDecodedConstant)
            {
                _ = key.Append(":K:")
                    .Append((int)argument.ConstantDomainType.GetValueOrDefault())
                    .Append(':')
                    .Append((int)argument.ConstantCarrierType.GetValueOrDefault())
                    .Append(':').Append(argument.ConstantLow)
                    .Append(':').Append(argument.ConstantHigh);
            }
            else
            {
                _ = key.Append(':').Append(argument.ConstantLow)
                    .Append(':').Append(argument.ConstantHigh)
                    .Append(':').Append(argument.ConstantProviderIdentity)
                    .Append(':').Append(argument.ConstantSourceRow)
                    .Append(':').Append(argument.TypeSpecForm)
                    .Append(':').Append(argument.TypeCode)
                    .Append(':').Append(argument.ArgumentCode)
                    .Append(':').Append(argument.NominalTypeIdentity);
            }
            if (argument.TypeArgumentTerm is
                BoundGenericMethodTypeTerm typeArgumentTerm)
            {
                _ = key.Append(":TT:")
                    .Append(BoundGenericMethodTypeTerms.CreateKey(
                        typeArgumentTerm));
            }
        }

        return key.Append(':').Append(typeSpecForm).ToString();
    }

    private static bool IsValidInstance(int index,
        ImmutableArray<BoundGenericNominalTypeInstance> instances,
        IReadOnlyDictionary<BoundGenericNominalTypeIdentity, BoundGenericNominalTypeDefinition> definitions,
        IReadOnlyDictionary<string, (int TypeSpecs, int ConstantSources,
            int Constraints, int Relations)> providerRows,
        byte[] states, int depth)
    {
        if ((uint)index >= (uint)instances.Length
            || depth > SemanticVerifier.MaximumDepth || states[index] == 1)
        {
            return false;
        }
        if (states[index] == 2)
        {
            return true;
        }

        var instance = instances[index];
        if (instance is null || !definitions.TryGetValue(instance.Definition, out var definition)
            || instance.Arguments.IsDefault || instance.Arguments.Length != definition.Formals.Length
            || (instance.Qualifiers & ~0x03) != 0
            || (instance.TypeSpecForm != 0
                && instance.TypeSpecForm is not (0x02 or 0x03 or 0x06 or 0x07 or 0x08 or 0x09
                    or 0x0A or 0x0B or 0x0C or 0x0E))
            || (instance.TypeSpecReference == 0
                ? instance.TypeSpecProviderIdentity is not null
                : instance.TypeSpecProviderIdentity is not string providerIdentity
                    || !IsValidText(providerIdentity)
                    || !providerRows.TryGetValue(providerIdentity, out var typeSpecRows)
                    || !IsTypeSpecReference(instance.TypeSpecReference,
                        typeSpecRows.TypeSpecs)))
        {
            return false;
        }

        states[index] = 1;
        for (var ordinal = 0; ordinal < instance.Arguments.Length;
            ordinal++)
        {
            var argument = instance.Arguments[ordinal];
            var formal = definition.Formals[ordinal];
            if (argument is null || argument.Kind is not (
                    BoundGenericArgumentKind.Type
                    or BoundGenericArgumentKind.Constant)
                || argument.Kind != formal.Kind)
            {
                return false;
            }
            if (argument.Kind == BoundGenericArgumentKind.Type)
            {
                var hasTermFormalDependencies = false;
                if (argument.TypeArgumentTerm is
                    BoundGenericMethodTypeTerm typeArgumentTerm)
                {
                    if (!BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                            typeArgumentTerm, out var typeFormalDependencies)
                        || !BoundGenericMethodTypeTerms
                            .TryGetConstantFormalDependencies(
                                typeArgumentTerm, out _))
                    {
                        return false;
                    }
                    hasTermFormalDependencies =
                        !typeFormalDependencies.IsEmpty;
                }

                if ((argument.Qualifiers & ~0x03) != 0
                    || argument.ConstantFormalDefinitionOrdinal != -1
                    || argument.ConstantFormalOrdinal != -1
                    || argument.ConstantFormalOwnerKind
                        != BoundGenericTypeFormalOwnerKind.GenericMethod
                    || argument.ConstantFormalDomain is not null
                    || (argument.HasTypeFormalOwnerIdentity
                        ? argument.TypeFormalOwnerKind
                                != BoundGenericTypeFormalOwnerKind.GenericMethod
                            || argument.TypeFormalDefinitionOrdinal < 0
                            || argument.TypeFormalDefinitionOrdinal
                                >= BoundSpanTypeTypes.MaximumCount
                            || argument.TypeFormalOrdinal < -1
                            || argument.TypeFormalOrdinal
                                >= SemanticVerifier.MaximumArguments
                            || (argument.TypeFormalOrdinal >= 0
                                ? !((argument.TypeSpecForm == 0x04
                                    && argument.TypeSpecReference != 0
                                    && argument.TypeSpecProviderIdentity
                                        is not null)
                                || (argument.TypeSpecForm == 0
                                    && argument.TypeSpecReference == 0
                                    && argument.TypeSpecProviderIdentity
                                        is null))
                                    || (argument.TypeArgumentTerm is
                                            BoundGenericMethodTypeTerm directTerm
                                        && (directTerm.Kind
                                                != BoundGenericMethodTypeTermKind
                                                    .Formal
                                            || !BoundGenericMethodTypeTerms
                                                .TryGetFormalDependencies(
                                                    directTerm,
                                                    out var directDependencies)
                                            || directDependencies.Length != 1
                                            || directDependencies[0]
                                                != argument.TypeFormalOrdinal))
                                : argument.TypeArgumentTerm is null
                                    || !hasTermFormalDependencies)
                        : argument.TypeFormalDefinitionOrdinal != -1
                            || argument.TypeFormalOrdinal != -1
                            || argument.TypeFormalOwnerKind
                                != BoundGenericTypeFormalOwnerKind.GenericMethod
                            || hasTermFormalDependencies)
                    || (argument.NominalTypeIdentity is BoundGenericNominalTypeIdentity nominalType
                        && (!nominalType.IsValid
                            || !definitions.TryGetValue(nominalType,
                                out var nominalDefinition)
                            || !nominalDefinition.Formals.IsEmpty
                            || argument.TypeSpecForm is not (0x02 or 0x03 or 0x05)))
                    || (argument.NominalTypeIdentity is null
                        && argument.TypeSpecForm != 0
                        && argument.TypeSpecForm != 0x01
                        && !(argument.TypeSpecForm == 0x04
                            && argument.HasTypeFormalIdentity)
                        && argument.TypeSpecForm is not (>= 0x05 and <= 0x0F))
                    || (argument.TypeSpecReference == 0
                        ? argument.TypeSpecProviderIdentity is not null
                        : argument.TypeSpecProviderIdentity is not string argumentProvider
                            || !IsValidText(argumentProvider)
                            || !providerRows.TryGetValue(argumentProvider,
                                out var argumentTypeSpecRows)
                            || !IsTypeSpecReference(argument.TypeSpecReference,
                                argumentTypeSpecRows.TypeSpecs))
                    || (argument.NominalTypeIdentity is null
                        && BoundGenericNominalTypeTypes.IsType(argument.Type)
                        && !IsValidInstance(BoundGenericNominalTypeTypes.Index(argument.Type),
                            instances, definitions, providerRows, states, depth + 1)))
                {
                    return false;
                }
            }
            else if (argument.Qualifiers != 0
                || argument.TypeSpecForm != 0 || argument.TypeCode != 0
                || argument.ArgumentCode != 0 || argument.NominalTypeIdentity is not null
                || argument.TypeSpecReference != 0
                || argument.TypeSpecProviderIdentity is not null
                || argument.TypeFormalDefinitionOrdinal != -1
                || argument.TypeFormalOrdinal != -1
                || argument.TypeFormalOwnerKind
                    != BoundGenericTypeFormalOwnerKind.GenericMethod
                || argument.TypeArgumentTerm is not null
                || (argument.HasConstantFormalIdentity
                    ? argument.ConstantLow != 0
                        || argument.ConstantHigh != 0
                        || argument.ConstantDomainType is not null
                        || argument.ConstantCarrierType is not null
                        || argument.HasDecodedConstant
                        || argument.ConstantProviderIdentity is not null
                        || argument.ConstantSourceRow != 0
                    : argument.ConstantFormalDefinitionOrdinal != -1
                        || argument.ConstantFormalOrdinal != -1
                        || argument.ConstantFormalOwnerKind
                            != BoundGenericTypeFormalOwnerKind.GenericMethod
                        || argument.ConstantFormalDomain is not null
                        || (argument.HasDecodedConstant
                    ? argument.ConstantDomainType is not PrimitiveType
                            domainType
                        || argument.ConstantCarrierType is not PrimitiveType
                            carrierType
                        || (formal.ConstantDomainType is PrimitiveType
                                formalDomain
                            && domainType != formalDomain)
                        || (formal.ConstantCarrierType is PrimitiveType
                                formalCarrier
                            && carrierType != formalCarrier)
                        || !new ScalarBits(argument.ConstantLow,
                            argument.ConstantHigh)
                            .IsCanonicalFor(carrierType)
                    : argument.ConstantDomainType is not null
                        || argument.ConstantCarrierType is not null))
                || (argument.ConstantProviderIdentity is null
                    ? argument.ConstantSourceRow != 0
                    : !IsValidText(argument.ConstantProviderIdentity)
                        || argument.ConstantSourceRow == 0
                        || !providerRows.TryGetValue(argument.ConstantProviderIdentity,
                            out var constantSourceRows)
                        || argument.ConstantSourceRow
                            > (uint)constantSourceRows.ConstantSources))
            {
                return false;
            }
        }

        states[index] = 2;
        return true;
    }

    private static bool IsValidFormal(BoundGenericNominalTypeFormal formal,
        BoundGenericNominalTypeDefinition definition,
        ImmutableArray<BoundGenericNominalTypeDefinition> definitions)
    {
        if (formal.Kind == BoundGenericArgumentKind.Type)
        {
            return formal.OwnershipDomain is >= 1 and <= 3
                && formal.NullabilityDomain is >= 1 and <= 2
                && formal.ConstantTypeSpecRef == 0
                && formal.ConstantDomainType is null
                && formal.ConstantCarrierType is null
                && formal.ConstantDomainTypeTerm is null
                && (formal.Default is null
                    || (formal.Default.Kind == BoundGenericArgumentKind.Type
                        && IsTypeSpecReference(formal.Default.Reference,
                            definition.TypeSpecCount)
                        && IsValidText(formal.Default.ProviderIdentity)
                        && formal.Default.ProviderIdentity
                            == definition.Identity.ProviderIdentity
                        && (formal.Default.TypeQualifiers & ~0x03) == 0));
        }

        return formal.Kind == BoundGenericArgumentKind.Constant
            && formal.OwnershipDomain == 0 && formal.NullabilityDomain == 0
            && IsTypeSpecReference(formal.ConstantTypeSpecRef,
                definition.TypeSpecCount)
            && (formal.ConstantDomainType is PrimitiveType domainType
                ? domainType != PrimitiveType.Void
                    && formal.ConstantCarrierType
                        is PrimitiveType carrierType
                    && (PrimitiveTypeFacts.IsIntegerOrWide(carrierType)
                        || carrierType == PrimitiveType.Bool)
                    && (formal.ConstantDomainTypeTerm is null
                        || (formal.ConstantDomainTypeTerm.Kind
                                == BoundGenericMethodTypeTermKind.Concrete
                            && formal.ConstantDomainTypeTerm.ConcreteType
                                == domainType))
                : formal.ConstantDomainTypeTerm is
                        BoundGenericMethodTypeTerm domainTerm
                    ? formal.ConstantCarrierType
                            is PrimitiveType termCarrierType
                        && (PrimitiveTypeFacts.IsIntegerOrWide(
                                termCarrierType)
                            || termCarrierType == PrimitiveType.Bool)
                        && IsValidConstantDomainTerm(domainTerm,
                            definition, definitions)
                    : formal.ConstantCarrierType is null)
            && (formal.Default is null
                || IsValidConstantDefault(formal, definition,
                    formal.Default));
    }

    private static bool IsValidConstantDomainTerm(
        BoundGenericMethodTypeTerm term,
        BoundGenericNominalTypeDefinition owner,
        ImmutableArray<BoundGenericNominalTypeDefinition> definitions)
    {
        var typeFormalCount = owner.Formals.Count(static formal =>
            formal.Kind == BoundGenericArgumentKind.Type);
        if (term.Kind != BoundGenericMethodTypeTermKind.ImportedGeneric
            || term.NominalTypeIdentity is not
                BoundGenericNominalTypeIdentity identity
            || !identity.IsValid
            || term.ConstantArguments.IsDefault
            || term.TypeArguments.IsDefault)
        {
            return false;
        }

        var domainDefinition = definitions.FirstOrDefault(candidate =>
            candidate.Identity == identity);
        if (domainDefinition is null
            || domainDefinition.NominalKind != 1
            || domainDefinition.TransparentConstantCarrierType is null
            || domainDefinition.Formals.Count(static formal =>
                formal.Kind == BoundGenericArgumentKind.Type)
                != term.TypeArguments.Length
            || domainDefinition.Formals.Any(static formal =>
                formal.Kind != BoundGenericArgumentKind.Type)
            || !BoundGenericMethodTypeTerms.TryGetFormalDependencies(term,
                out var typeFormalDependencies)
            || typeFormalDependencies.Any(dependency =>
                (uint)dependency >= (uint)typeFormalCount)
            || !BoundGenericMethodTypeTerms.TryGetConstantFormalDependencies(
                term, out var constantFormalDependencies)
            || !constantFormalDependencies.IsEmpty)
        {
            return false;
        }

        return term.ConstantArguments.IsEmpty
            && term.TypeArguments.All(argument =>
                IsValidConstantDomainTypeArgument(argument, owner,
                    definitions, 0));
    }

    private static bool IsValidConstantDomainTypeArgument(
        BoundGenericMethodTypeTerm term,
        BoundGenericNominalTypeDefinition owner,
        ImmutableArray<BoundGenericNominalTypeDefinition> definitions,
        int depth)
    {
        if (term is null || depth > SemanticVerifier.MaximumDepth
            || term.TypeArguments.IsDefault
            || term.ConstantArguments.IsDefault
            || !BoundGenericMethodTypeTerms.TryGetFormalDependencies(term,
                out var dependencies)
            || dependencies.Any(dependency =>
                (uint)dependency >= (uint)owner.Formals.Count(static formal =>
                    formal.Kind == BoundGenericArgumentKind.Type))
            || !BoundGenericMethodTypeTerms.TryGetConstantFormalDependencies(
                term, out var constantDependencies)
            || !constantDependencies.IsEmpty)
        {
            return false;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.ImportedGeneric)
        {
            if (term.NominalTypeIdentity
                    is not BoundGenericNominalTypeIdentity identity
                || !identity.IsValid
                || !term.ConstantArguments.IsEmpty
                || definitions.FirstOrDefault(candidate =>
                    candidate.Identity == identity) is not
                    BoundGenericNominalTypeDefinition definition
                || definition.Formals.Any(static formal =>
                    formal.Kind != BoundGenericArgumentKind.Type)
                || definition.Formals.Length != term.TypeArguments.Length)
            {
                return false;
            }
        }
        else if (term.Kind is BoundGenericMethodTypeTermKind.Concrete
            or BoundGenericMethodTypeTermKind.Formal)
        {
            if (!term.TypeArguments.IsEmpty
                || !term.ConstantArguments.IsEmpty
                || (term.Kind == BoundGenericMethodTypeTermKind.Concrete
                    && term.ConcreteType == PrimitiveType.Void)
                || (term.Kind == BoundGenericMethodTypeTermKind.Formal
                    && (uint)term.FormalOrdinal
                        >= (uint)owner.Formals.Count(static formal =>
                            formal.Kind == BoundGenericArgumentKind.Type)))
            {
                return false;
            }
        }
        else if (term.Kind == BoundGenericMethodTypeTermKind.SourceGeneric)
        {
            return false;
        }

        return term.TypeArguments.All(argument =>
            IsValidConstantDomainTypeArgument(argument, owner,
                definitions, depth + 1));
    }

    private static bool IsValidConstantDefault(
        BoundGenericNominalTypeFormal formal,
        BoundGenericNominalTypeDefinition definition,
        BoundGenericNominalTypeDefault defaultValue)
    {
        if (defaultValue.Kind != BoundGenericArgumentKind.Constant
            || defaultValue.Reference == 0
            || defaultValue.Reference
                > (uint)definition.ConstantSourceCount
            || !IsValidText(defaultValue.ProviderIdentity)
            || defaultValue.ProviderIdentity
                != definition.Identity.ProviderIdentity)
        {
            return false;
        }

        if (defaultValue.EarlierFormalOrdinal
            is int sourceFormalOrdinal)
        {
            if (defaultValue.DecodedConstantArgument is not null
                || sourceFormalOrdinal < 0
                || (uint)sourceFormalOrdinal >= formal.Ordinal
                || (uint)sourceFormalOrdinal
                    >= (uint)definition.Formals.Length)
            {
                return false;
            }

            var sourceFormal = definition.Formals[sourceFormalOrdinal];
            return sourceFormal.Ordinal == (uint)sourceFormalOrdinal
                && sourceFormal.Kind == BoundGenericArgumentKind.Constant
                && sourceFormal.ConstantDomainType
                    == formal.ConstantDomainType
                && sourceFormal.ConstantCarrierType
                    == formal.ConstantCarrierType
                && (sourceFormal.ConstantDomainTypeTerm is null
                    ? formal.ConstantDomainTypeTerm is null
                    : formal.ConstantDomainTypeTerm is not null
                        && SemanticVerifier.AreGenericMethodTypeTermsEqual(
                            sourceFormal.ConstantDomainTypeTerm,
                            formal.ConstantDomainTypeTerm, 0));
        }

        if (defaultValue.DecodedConstantArgument is not
            BoundGenericNominalTypeArgument decoded)
        {
            return true;
        }

        if (formal.ConstantDomainTypeTerm is not null)
        {
            return false;
        }

        return decoded.Kind == BoundGenericArgumentKind.Constant
            && decoded.HasDecodedConstant
            && decoded.ConstantProviderIdentity
                == defaultValue.ProviderIdentity
            && decoded.ConstantSourceRow == defaultValue.Reference
            && (formal.ConstantDomainType is not PrimitiveType domainType
                || decoded.ConstantDomainType == domainType)
            && (formal.ConstantCarrierType is not PrimitiveType carrierType
                || decoded.ConstantCarrierType == carrierType);
    }

    private static bool IsValidDecodedConstantArgument(
        BoundGenericNominalTypeFormal formal,
        BoundGenericNominalTypeArgument argument, BoundProgram program)
    {
        if (argument.ConstantDomainType is not PrimitiveType domainType
            || argument.ConstantCarrierType is not PrimitiveType carrierType
            || (formal.ConstantDomainType is PrimitiveType formalDomain
                && domainType != formalDomain)
            || (formal.ConstantCarrierType is PrimitiveType formalCarrier
                && carrierType != formalCarrier)
            || !SemanticVerifier.IsValidType(domainType, false, program)
            || !(PrimitiveTypeFacts.IsIntegerOrWide(carrierType)
                || carrierType == PrimitiveType.Bool)
            || !new ScalarBits(argument.ConstantLow, argument.ConstantHigh)
                .IsCanonicalFor(carrierType))
        {
            return false;
        }

        return formal.ConstantDomainType is not null
            || formal.ConstantDomainTypeTerm is not null;
    }

    private static bool IsValidConstraint(
        BoundGenericNominalTypeConstraint constraint,
        BoundGenericNominalTypeDefinition definition) =>
        constraint.Kind switch
        {
            1 => IsTypeSpecReference(constraint.Payload, definition.TypeSpecCount),
            2 => constraint.Payload is >= 1 and <= 3,
            3 => constraint.Payload <= definition.AssociatedRelationCount,
            _ => false,
        };

    private static bool IsAdmittedByDomains(
        BoundGenericNominalTypeFormal formal,
        BoundGenericNominalTypeArgument argument,
        BoundProgram program,
        IReadOnlyDictionary<int,
            ImmutableArray<BoundGenericMethodFormal>> typeFormalsByOwner,
        IReadOnlyDictionary<int, BoundFunction> typeOwnerFunctions)
    {
        if (argument.HasTypeFormalOwnerIdentity
            && argument.TypeFormalOrdinal == -1
            && argument.TypeArgumentTerm is
                BoundGenericMethodTypeTerm typeArgumentTerm)
        {
            if (!typeFormalsByOwner.TryGetValue(
                    argument.TypeFormalDefinitionOrdinal,
                    out var sourceFormals))
            {
                return false;
            }
            if (!typeOwnerFunctions.TryGetValue(
                    argument.TypeFormalDefinitionOrdinal,
                    out var typeOwner))
            {
                return false;
            }

            return SemanticVerifier.TryGetGenericMethodActualTypeTerm(
                    argument.Type, typeOwner, program, 0,
                    out var actualTypeTerm)
                && SemanticVerifier.AreGenericMethodTypeTermsEqual(
                    actualTypeTerm, typeArgumentTerm, 0)
                && (typeArgumentTerm.Kind
                        != BoundGenericMethodTypeTermKind.CheckedReference
                    || CheckedReferenceTypeContract
                        .IsSupportedReferenceStoreValueType(argument.Type,
                            program.Structures, program.Classes,
                            program.EnumDefinitions.Length,
                            program.CheckedReferenceTypes,
                            program.NativePointerTypes))
                && SemanticVerifier
                    .IsGenericNominalTypeArgumentTermAdmitted(
                        typeArgumentTerm, sourceFormals,
                        formal.OwnershipDomain,
                        formal.NullabilityDomain, program)
                && (argument.Qualifiers == 0
                    || (argument.Qualifiers & 0x01) != 0);
        }

        if (argument.HasTypeFormalIdentity)
        {
            var owners = program.GenericMethodTypeFormalOwners
                .Where(owner => owner.Ordinal
                    == argument.TypeFormalDefinitionOrdinal)
                .ToArray();
            if (owners.Length == 0
                || (uint)argument.TypeFormalOrdinal
                    >= (uint)owners[0].Formals.Length)
            {
                return false;
            }

            var sourceFormal = owners[0].Formals[
                argument.TypeFormalOrdinal];
            if (sourceFormal.PlaceholderType != argument.Type
                || (formal.NullabilityDomain == 1
                    && sourceFormal.NullabilityDomain != 1)
                || (formal.OwnershipDomain == 1
                    && sourceFormal.OwnershipDomain != 1)
                || (formal.OwnershipDomain == 2
                    && sourceFormal.OwnershipDomain != 2))
            {
                return false;
            }

            return argument.Qualifiers == 0
                || (argument.Qualifiers & 0x01) != 0;
        }

        var type = argument.Type;
        var nullable = ManagedClassTypes.IsOptional(type)
            || argument.TypeSpecForm == 0x06;
        var sourceClassOwnership = (bool?)null;
        if (ManagedClassTypes.IsManaged(type)
            && (int)type >= ManagedClassTypes.First)
        {
            var classIndex = ManagedClassTypes.ClassIndex(type);
            if ((uint)classIndex >= (uint)program.Classes.Length)
            {
                return false;
            }

            var isUniqueClass = program.Classes[classIndex].IsUnique;
            if (isUniqueClass is null)
            {
                return false;
            }

            sourceClassOwnership = isUniqueClass;
        }
        var unique = type == PrimitiveType.OwnedInt32Buffer
            || argument.TypeSpecForm == 0x0B
            || ((int)type >= 100 && (int)type - 100 < program.Structures.Length
                && program.Structures[(int)type - 100].IsUnique)
            || sourceClassOwnership == true
            || IsCoreTaskType(type, program);
        var nominalDefinition = argument.NominalTypeIdentity is
            BoundGenericNominalTypeIdentity nominalIdentity
                ? program.GenericNominalTypeDefinitions.FirstOrDefault(definition =>
                    definition.Identity == nominalIdentity)
                : TryGetDefinition(type, program, out var typeDefinition)
                    ? typeDefinition : null;
        if (nominalDefinition?.NominalKind == 2
            && !IsCoreTaskType(type, program))
        {
            return false;
        }

        var copyCapable = PrimitiveTypeFacts.ScalarWidth(type) > 0
            || NativePointerTypes.IsType(type)
            || NativeFunctionPointerTypes.IsType(type)
            || BoundEnumTypes.IsValidType(type, program.EnumDefinitions)
            || CheckedReferenceTypeContract
                .IsSupportedReferenceStoreValueType(type,
                    program.Structures, program.Classes,
                    program.EnumDefinitions.Length,
                    program.CheckedReferenceTypes,
                    program.NativePointerTypes)
            || (BoundSpanTypeTypes.IsValidType(type, program.SpanTypes)
                && !program.SpanTypes[BoundSpanTypeTypes.Index(type)]
                    .IsInitialization)
            || (ManagedClassTypes.IsManaged(type)
                && (sourceClassOwnership is null
                    ? (int)type < ManagedClassTypes.First
                    : sourceClassOwnership == false))
            || ((int)type >= 100 && (int)type - 100 < program.Structures.Length
                && !program.Structures[(int)type - 100].IsUnique);
        if ((formal.NullabilityDomain == 1 && nullable)
            || (formal.OwnershipDomain == 1 && (unique || !copyCapable))
            || (formal.OwnershipDomain == 2 && !unique))
        {
            return false;
        }

        return argument.Qualifiers == 0
            || (argument.Qualifiers & 0x01) != 0;
    }

    private static bool IsValidText(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains('\0', StringComparison.Ordinal);

    private static bool IsValidNamespace(string value) =>
        value is not null
        && !value.Contains('\0', StringComparison.Ordinal)
        && (value.Length == 0 || !string.IsNullOrWhiteSpace(value));

    internal static bool IsTypeSpecReference(uint reference)
    {
        if (reference is > 0 and <= 0x00FF_FFFF)
        {
            return true;
        }
        if ((reference & 0xFF00_0000) == 0xF200_0000)
        {
            return (reference & 0x00FF_FF00) == 0
                && (reference & 0xFF) <= 0x1A;
        }
        if ((reference & 0xFF00_0000) != 0xF300_0000)
        {
            return false;
        }

        var qualifiers = (byte)((reference >> 16) & 0xFF);
        var family = (byte)((reference >> 8) & 0xFF);
        var argument = (byte)(reference & 0xFF);
        return ((family >= 0x80 && family <= 0x84)
            || (family >= 0x8A && family <= 0x8F))
            && argument <= 0x19 && (qualifiers & 0xF8) == 0
            && ((qualifiers & 0x02) == 0 || family is 0x8B or 0x8D);
    }

    private static bool IsTypeSpecReference(uint reference, int typeSpecCount) =>
        ValidCount(typeSpecCount)
        && (reference is > 0 and <= 0x00FF_FFFF
            ? reference <= (uint)typeSpecCount
            : IsTypeSpecReference(reference));

    private static bool ValidCount(int count) => count is >= 0 and <= 0x00FF_FFFF;
}
