// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>The registered source-argument kind for a Core layout attribute.</summary>
public enum CoreLayoutAttributeArgumentKind
{
    /// <summary>No valid argument kind.</summary>
    None = 0,
    /// <summary>A member of the selected LayoutKind enum.</summary>
    LayoutKind = 1,
    /// <summary>A checked uint32 constant.</summary>
    Unsigned32 = 2,
}

/// <summary>The source declaration families that may carry a Core layout attribute.</summary>
[Flags]
public enum CoreLayoutAttributeTargets
{
    /// <summary>No valid declaration target.</summary>
    None = 0,
    /// <summary>An ordinary inline struct or unique struct.</summary>
    InlineStruct = 1,
    /// <summary>An integer-backed enum contract.</summary>
    IntegerBackedEnum = 2,
}

/// <summary>A complete named or positional argument slot in a registered layout schema.</summary>
/// <param name="Name">The exact ordinal source label.</param>
/// <param name="Kind">The registered source value kind.</param>
/// <param name="EnumType">The exact selected enum identity for an enum-valued argument.</param>
/// <param name="IsRequired">Whether the source schema requires the argument.</param>
/// <param name="AllowsPositional">Whether the argument may be supplied positionally.</param>
/// <param name="DefaultUInt32">The registered uint32 default, if any.</param>
public readonly record struct CoreLayoutAttributeArgument(
    string Name,
    CoreLayoutAttributeArgumentKind Kind,
    CoreDeclarationIdentity? EnumType,
    bool IsRequired,
    bool AllowsPositional,
    uint? DefaultUInt32);

/// <summary>A selected, structurally checked Core source layout attribute schema.</summary>
/// <param name="Identity">The exact selected attribute declaration identity.</param>
/// <param name="QualifiedName">The checked source-qualified attribute name.</param>
/// <param name="Arguments">The complete ordered argument schema.</param>
/// <param name="Targets">The complete set of permitted source declaration families.</param>
/// <param name="IsPrimaryTargetOnly">Whether placement is restricted to a primary declaration.</param>
/// <param name="GenericArity">The schema's generic arity.</param>
/// <param name="IsPublic">Whether the schema is public.</param>
/// <param name="IsPrimary">Whether this is the primary schema declaration.</param>
/// <param name="IsRepeatable">Whether the schema permits repeated placement.</param>
public sealed record CoreLayoutAttributeSchema(
    CoreDeclarationIdentity Identity,
    string QualifiedName,
    ImmutableArray<CoreLayoutAttributeArgument> Arguments,
    CoreLayoutAttributeTargets Targets,
    bool IsPrimaryTargetOnly,
    int GenericArity,
    bool IsPublic,
    bool IsPrimary,
    bool IsRepeatable);

/// <summary>
/// Immutable host-selected Core identities for the registered LayoutKind,
/// Layout and Blittable source contracts in one using Module.
/// </summary>
/// <remarks>
/// The descriptor is a binding input, not a serialized record or authentication claim.
/// </remarks>
public sealed class LayoutCoreRegistrationSet
{
    private LayoutCoreRegistrationSet(CoreProviderModuleBindings moduleBindings,
        CoreEnumDefinition layoutKinds, CoreLayoutAttributeSchema layout,
        CoreLayoutAttributeSchema blittable)
    {
        ModuleBindings = moduleBindings;
        LayoutKinds = layoutKinds;
        Layout = layout;
        Blittable = blittable;
    }

    /// <summary>Gets the exact using logical Module identity.</summary>
    public string UsingModuleIdentity => ModuleBindings.UsingModuleIdentity;

    /// <summary>Gets the immutable selected provider context for this registration.</summary>
    public CoreProviderModuleBindings ModuleBindings { get; }

    /// <summary>Gets the selected nominal LayoutKind enum declaration.</summary>
    public CoreEnumDefinition LayoutKinds { get; }

    /// <summary>Gets the selected Layout attribute schema.</summary>
    public CoreLayoutAttributeSchema Layout { get; }

    /// <summary>Gets the selected Blittable attribute schema.</summary>
    public CoreLayoutAttributeSchema Blittable { get; }

    /// <summary>
    /// Creates a registration after validating its complete provider, enum and schema shapes.
    /// </summary>
    /// <exception cref="ArgumentNullException">A required registration component is null.</exception>
    /// <exception cref="ArgumentException">A declaration, mapping or owner relationship is invalid.</exception>
    public static LayoutCoreRegistrationSet Create(
        CoreProviderModuleBindings moduleBindings,
        CoreEnumDefinition layoutKinds,
        CoreLayoutAttributeSchema layout,
        CoreLayoutAttributeSchema blittable)
    {
        ArgumentNullException.ThrowIfNull(moduleBindings);
        ArgumentNullException.ThrowIfNull(layoutKinds);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(blittable);

        var registration = new LayoutCoreRegistrationSet(moduleBindings,
            layoutKinds, layout, blittable);
        if (registration.GetInvalidReason() is string reason)
        {
            throw new ArgumentException(
                $"The selected Core layout registration is invalid: {reason}.",
                nameof(moduleBindings));
        }

        return registration;
    }

    /// <summary>Whether the complete layout registration has its exact selected shape.</summary>
    public bool IsValid() => GetInvalidReason() is null;

    internal string? GetInvalidReason()
    {
        if (!ModuleBindings.IsValid())
        {
            return "the provider Module binding context is invalid";
        }
        if (!IsLayoutKindsEnum(LayoutKinds))
        {
            return "the LayoutKind enum does not match its exact registered shape";
        }
        if (!IsSchema(Layout, CoreLayoutAttributeTargets.InlineStruct,
                ExpectedLayoutArguments(LayoutKinds.Identity)))
        {
            return "the Layout schema identity, targets or arguments are invalid";
        }
        if (!IsSchema(Blittable,
                CoreLayoutAttributeTargets.InlineStruct
                    | CoreLayoutAttributeTargets.IntegerBackedEnum, []))
        {
            return "the Blittable schema identity, targets or arguments are invalid";
        }
        if (LayoutKinds.QualifiedName == Layout.QualifiedName
            || LayoutKinds.QualifiedName == Blittable.QualifiedName
            || Layout.QualifiedName == Blittable.QualifiedName)
        {
            return "the selected source declaration names are ambiguous";
        }

        var identities = new HashSet<CoreDeclarationIdentity>();
        var exportKeys = new HashSet<(string Provider, string Key)>();
        if (!AddDeclarationIdentity(LayoutKinds.Identity)
            || !AddExportIdentity(LayoutKinds.Identity)
            || !AddProvider(LayoutKinds.Identity.ProviderIdentity)
            || !ModuleBindings.ContainsDeclaration(LayoutKinds))
        {
            return "the LayoutKind identity is missing or duplicated";
        }

        foreach (var member in LayoutKinds.Members)
        {
            if (member is null || !AddDeclarationIdentity(member.Identity)
                || !AddProvider(member.Identity.ProviderIdentity))
            {
                return "a LayoutKind member identity is missing or duplicated";
            }
        }

        if (!AddDeclarationIdentity(Layout.Identity)
            || !AddExportIdentity(Layout.Identity)
            || !AddDeclarationIdentity(Blittable.Identity)
            || !AddExportIdentity(Blittable.Identity)
            || !AddProvider(Layout.Identity.ProviderIdentity)
            || !AddProvider(Blittable.Identity.ProviderIdentity)
            || !ModuleBindings.ContainsDeclaration(Layout)
            || !ModuleBindings.ContainsDeclaration(Blittable))
        {
            return "a layout attribute identity is missing or duplicated";
        }

        return null;

        bool AddDeclarationIdentity(CoreDeclarationIdentity identity)
        {
            return identity.IsValid && identities.Add(identity);
        }

        bool AddExportIdentity(CoreDeclarationIdentity identity)
        {
            return exportKeys.Add((identity.ProviderIdentity, identity.ExportKey));
        }

        bool AddProvider(string providerIdentity)
        {
            return ModuleBindings.ContainsProvider(providerIdentity);
        }
    }

    private static bool IsLayoutKindsEnum(CoreEnumDefinition definition)
    {
        if (definition is null || !definition.Identity.IsValid
            || definition.Identity.Kind != CoreDeclarationKind.EnumType
            || !IsQualifiedName(definition.QualifiedName)
            || definition.UnderlyingType != PrimitiveType.Int32
            || definition.GenericArity != 0
            || !definition.IsPublic || !definition.IsPrimary || definition.IsFlags
            || definition.BlittableTargetRequirement is not (0
                or FixedLayoutStorage.WindowsX64TargetRequirement)
            || definition.Members.IsDefault
            || definition.Members.Length != 4)
        {
            return false;
        }

        var expectedMembers = new (string Name, int Value)[] {
            ("Auto", 0),
            ("Sequential", 1),
            ("Union", 2),
            ("Transparent", 3),
        };
        var identities = new HashSet<CoreDeclarationIdentity>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var declarationKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, value) in expectedMembers)
        {
            var matches = 0;
            foreach (var member in definition.Members)
            {
                if (member is null || member.Name != name
                    || member.Value != value)
                {
                    continue;
                }

                matches++;
                if (!member.Identity.IsValid
                    || member.Identity.Kind != CoreDeclarationKind.EnumMember
                    || member.Identity.ProviderIdentity
                        != definition.Identity.ProviderIdentity
                    || member.DeclaringEnum != definition.Identity
                    || !member.IsPublic
                    || string.IsNullOrWhiteSpace(member.DeclarationKey)
                    || member.DeclarationKey.Contains('\0', StringComparison.Ordinal)
                    || !identities.Add(member.Identity)
                    || !names.Add(member.Name)
                    || !declarationKeys.Add(member.DeclarationKey))
                {
                    return false;
                }
            }

            if (matches != 1)
            {
                return false;
            }
        }

        return identities.Count == definition.Members.Length
            && definition.Members.All(member => member is not null
                && member.DeclaringEnum == definition.Identity);
    }

    private static bool IsSchema(CoreLayoutAttributeSchema schema,
        CoreLayoutAttributeTargets targets,
        ImmutableArray<CoreLayoutAttributeArgument> expectedArguments)
        => schema is not null && schema.Identity.IsValid
            && schema.Identity.Kind == CoreDeclarationKind.AttributeSchema
            && IsQualifiedName(schema.QualifiedName)
            && !schema.Arguments.IsDefault
            && schema.Arguments.SequenceEqual(expectedArguments)
            && schema.Targets == targets
            && schema.IsPrimaryTargetOnly
            && schema.GenericArity == 0
            && schema.IsPublic && schema.IsPrimary && !schema.IsRepeatable;

    private static ImmutableArray<CoreLayoutAttributeArgument>
        ExpectedLayoutArguments(CoreDeclarationIdentity layoutKinds)
        => [
            new("Kind", CoreLayoutAttributeArgumentKind.LayoutKind,
                layoutKinds, true, true, null),
            new("Packing", CoreLayoutAttributeArgumentKind.Unsigned32,
                null, false, false, 16),
            new("Alignment", CoreLayoutAttributeArgumentKind.Unsigned32,
                null, false, false, null),
        ];

    private static bool IsQualifiedName(string name)
        => !string.IsNullOrWhiteSpace(name)
            && !name.Contains('\0', StringComparison.Ordinal)
            && !name.StartsWith('.', StringComparison.Ordinal)
            && !name.EndsWith('.', StringComparison.Ordinal)
            && !name.Contains("..", StringComparison.Ordinal);
}
