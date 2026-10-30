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
    PrimitiveType? DecodedType = null, byte TypeQualifiers = 0);

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
    ImmutableArray<BoundGenericNominalTypeConstraint> Constraints);

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
    int AssociatedRelationCount);

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
                || !IsValidText(definition.Namespace) || !IsValidText(definition.Name)
                || definition.NominalKind is < 1 or > 3
                || definition.Formals.IsDefault
                || definition.Formals.Length > SemanticVerifier.MaximumAnalysisVisits
                || !ValidCount(definition.TypeSpecCount)
                || !ValidCount(definition.ConstantSourceCount)
                || !ValidCount(definition.ConstraintCount)
                || !ValidCount(definition.AssociatedRelationCount)
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
                    || !IsValidFormal(formal, definition)
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
                        || !IsAdmittedByDomains(formal, argument, program)))
                    || (formal.Kind == BoundGenericArgumentKind.Constant
                        && argument.Kind != BoundGenericArgumentKind.Constant))
                {
                    return false;
                }
            }
        }

        return true;
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
                .Append((int)argument.Type).Append(':').Append(argument.Qualifiers)
                .Append(':').Append(argument.ConstantLow).Append(':').Append(argument.ConstantHigh)
                .Append(':').Append(argument.ConstantProviderIdentity)
                .Append(':').Append(argument.ConstantSourceRow)
                .Append(':').Append(argument.TypeSpecForm)
                .Append(':').Append(argument.TypeCode)
                .Append(':').Append(argument.ArgumentCode)
                .Append(':').Append(argument.NominalTypeIdentity);
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
        foreach (var argument in instance.Arguments)
        {
            if (argument is null || argument.Kind is not (
                    BoundGenericArgumentKind.Type or BoundGenericArgumentKind.Constant))
            {
                return false;
            }
            if (argument.Kind == BoundGenericArgumentKind.Type)
            {
                if ((argument.Qualifiers & ~0x03) != 0
                    || (argument.NominalTypeIdentity is BoundGenericNominalTypeIdentity nominalType
                        && (!nominalType.IsValid
                            || !definitions.TryGetValue(nominalType,
                                out var nominalDefinition)
                            || !nominalDefinition.Formals.IsEmpty
                            || argument.TypeSpecForm is not (0x02 or 0x03 or 0x05)))
                    || (argument.NominalTypeIdentity is null
                        && argument.TypeSpecForm != 0
                        && argument.TypeSpecForm != 0x01
                        && argument.TypeSpecForm is not (>= 0x06 and <= 0x0F))
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
        BoundGenericNominalTypeDefinition definition)
    {
        if (formal.Kind == BoundGenericArgumentKind.Type)
        {
            return formal.OwnershipDomain is >= 1 and <= 3
                && formal.NullabilityDomain is >= 1 and <= 2
                && formal.ConstantTypeSpecRef == 0
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
            && (formal.Default is null
                || (formal.Default.Kind == BoundGenericArgumentKind.Constant
                    && formal.Default.Reference != 0
                    && formal.Default.Reference
                        <= (uint)definition.ConstantSourceCount
                    && IsValidText(formal.Default.ProviderIdentity)
                    && formal.Default.ProviderIdentity
                        == definition.Identity.ProviderIdentity));
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
        BoundProgram program)
    {
        var type = argument.Type;
        var nullable = ManagedClassTypes.IsOptional(type)
            || argument.TypeSpecForm == 0x06;
        var unique = type == PrimitiveType.OwnedInt32Buffer
            || argument.TypeSpecForm == 0x0B
            || ((int)type >= 100 && (int)type - 100 < program.Structures.Length
                && program.Structures[(int)type - 100].IsUnique)
            || IsCoreTaskType(type, program);
        var nominalDefinition = argument.NominalTypeIdentity is
            BoundGenericNominalTypeIdentity nominalIdentity
                ? program.GenericNominalTypeDefinitions.FirstOrDefault(definition =>
                    definition.Identity == nominalIdentity)
                : TryGetDefinition(type, program, out var typeDefinition)
                    ? typeDefinition : null;
        var copyCapable = PrimitiveTypeFacts.ScalarWidth(type) > 0
            || NativePointerTypes.IsType(type)
            || NativeFunctionPointerTypes.IsType(type)
            || BoundEnumTypes.IsValidType(type, program.EnumDefinitions)
            || CheckedReferenceTypeContract.IsValidType(
                type, program.CheckedReferenceTypes)
            || ManagedClassTypes.IsManaged(type)
            || ((int)type >= 100 && (int)type - 100 < program.Structures.Length
                && !program.Structures[(int)type - 100].IsUnique)
            || (nominalDefinition is not null
                && nominalDefinition.NominalKind == 2 && !unique);
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
