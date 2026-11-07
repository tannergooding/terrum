// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal enum BoundStaticInitializationTrigger
{
    GroupedStaticFieldAccess,
    RuntimeStaticMemberAccess,
}

internal sealed record BoundStaticConstructorIdentity(
    string ModuleIdentity,
    string OwnerName);

/// <summary>The source declaration target for the registered static-initializer attribute.</summary>
public enum CoreStaticInitializerAttributeTarget
{
    /// <summary>No source target is selected.</summary>
    None = 0,
    /// <summary>The primary static-constructor declaration.</summary>
    PrimaryStaticConstructor = 1,
}

/// <summary>The complete source argument slot for a static-initializer attribute schema.</summary>
/// <param name="Name">The exact source argument label.</param>
/// <param name="EnumType">The exact selected enum identity.</param>
/// <param name="IsRequired">Whether the argument is required.</param>
/// <param name="AllowsPositional">Whether the argument may be supplied positionally.</param>
/// <param name="AllowsNamed">Whether the argument may be supplied by name.</param>
/// <param name="HasDefault">Whether omission has a default value.</param>
public readonly record struct CoreStaticInitializerAttributeArgument(
    string Name,
    CoreDeclarationIdentity EnumType,
    bool IsRequired,
    bool AllowsPositional,
    bool AllowsNamed,
    bool HasDefault)
{
    internal bool IsStructurallyValid
        => IsValidText(Name)
            && EnumType.IsValid
            && EnumType.Kind == CoreDeclarationKind.EnumType
            && (AllowsPositional || AllowsNamed);

    private static bool IsValidText(string value)
        => !string.IsNullOrWhiteSpace(value)
            && !value.Contains('\0', StringComparison.Ordinal);
}

/// <summary>The complete checked source schema selected for StaticInitializer.</summary>
/// <param name="Identity">The exact selected schema declaration identity.</param>
/// <param name="QualifiedName">The checked source-qualified schema name.</param>
/// <param name="Arguments">The complete ordered argument schema.</param>
/// <param name="Target">The permitted source declaration target.</param>
/// <param name="IsPrimaryTargetOnly">Whether placement is restricted to the primary declaration.</param>
/// <param name="GenericArity">The schema's generic arity.</param>
/// <param name="IsPublic">Whether the schema is public.</param>
/// <param name="IsPrimary">Whether this is the primary schema declaration.</param>
/// <param name="IsRepeatable">Whether the schema permits repeated placement.</param>
public sealed record CoreStaticInitializerAttributeSchema(
    CoreDeclarationIdentity Identity,
    string QualifiedName,
    ImmutableArray<CoreStaticInitializerAttributeArgument> Arguments,
    CoreStaticInitializerAttributeTarget Target,
    bool IsPrimaryTargetOnly,
    int GenericArity,
    bool IsPublic,
    bool IsPrimary,
    bool IsRepeatable)
{
    internal bool IsStructurallyValid
        => Identity.IsValid
            && Identity.Kind == CoreDeclarationKind.AttributeSchema
            && IsValidText(QualifiedName)
            && !Arguments.IsDefault
            && Arguments.Length == 1
            && Arguments[0].IsStructurallyValid
            && Target == CoreStaticInitializerAttributeTarget.PrimaryStaticConstructor
            && GenericArity >= 0;

    internal static bool AreEquivalent(
        CoreStaticInitializerAttributeSchema left,
        CoreStaticInitializerAttributeSchema right)
        => left.Identity == right.Identity
            && left.QualifiedName == right.QualifiedName
            && !left.Arguments.IsDefault && !right.Arguments.IsDefault
            && left.Arguments.SequenceEqual(right.Arguments)
            && left.Target == right.Target
            && left.IsPrimaryTargetOnly == right.IsPrimaryTargetOnly
            && left.GenericArity == right.GenericArity
            && left.IsPublic == right.IsPublic
            && left.IsPrimary == right.IsPrimary
            && left.IsRepeatable == right.IsRepeatable;

    private static bool IsValidText(string value)
        => !string.IsNullOrWhiteSpace(value)
            && !value.Contains('\0', StringComparison.Ordinal);
}

/// <summary>The source declaration target for the LazyInitialization schema.</summary>
public enum CoreLazyInitializationAttributeTarget
{
    /// <summary>No source target is selected.</summary>
    None = 0,
    /// <summary>An authored static field declaration.</summary>
    PrimaryStaticField = 1,
}

/// <summary>The complete checked source schema selected for LazyInitialization.</summary>
/// <param name="Identity">The exact selected schema declaration identity.</param>
/// <param name="QualifiedName">The checked source-qualified schema name.</param>
/// <param name="Target">The permitted source declaration target.</param>
/// <param name="IsPrimaryTargetOnly">Whether placement is restricted to the primary declaration.</param>
/// <param name="GenericArity">The schema's generic arity.</param>
/// <param name="IsPublic">Whether the schema is public.</param>
/// <param name="IsPrimary">Whether this is the primary schema declaration.</param>
/// <param name="IsRepeatable">Whether the schema permits repeated placement.</param>
public sealed record CoreLazyInitializationAttributeSchema(
    CoreDeclarationIdentity Identity,
    string QualifiedName,
    CoreLazyInitializationAttributeTarget Target,
    bool IsPrimaryTargetOnly,
    int GenericArity,
    bool IsPublic,
    bool IsPrimary,
    bool IsRepeatable)
{
    internal bool IsStructurallyValid
        => Identity.IsValid
            && Identity.Kind == CoreDeclarationKind.AttributeSchema
            && !string.IsNullOrWhiteSpace(QualifiedName)
            && !QualifiedName.Contains('\0', StringComparison.Ordinal)
            && Target
                == CoreLazyInitializationAttributeTarget.PrimaryStaticField
            && IsPrimaryTargetOnly
            && GenericArity == 0
            && IsPublic
            && IsPrimary
            && !IsRepeatable;

    internal static bool AreEquivalent(
        CoreLazyInitializationAttributeSchema left,
        CoreLazyInitializationAttributeSchema right)
        => left.Identity == right.Identity
            && left.QualifiedName == right.QualifiedName
            && left.Target == right.Target
            && left.IsPrimaryTargetOnly == right.IsPrimaryTargetOnly
            && left.GenericArity == right.GenericArity
            && left.IsPublic == right.IsPublic
            && left.IsPrimary == right.IsPrimary
            && left.IsRepeatable == right.IsRepeatable;
}

/// <summary>
/// Immutable host-selected Core identities for the StaticInitializer source contract
/// in one using Module.
/// </summary>
/// <remarks>The descriptor is a binding input, not a serialized record or authentication claim.</remarks>
public sealed class StaticInitializationCoreRegistrationSet
{
    private StaticInitializationCoreRegistrationSet(
        CoreProviderModuleBindings moduleBindings,
        CoreEnumDefinition triggerEnum,
        CoreStaticInitializerAttributeSchema staticInitializer)
    {
        ModuleBindings = moduleBindings;
        TriggerEnum = triggerEnum;
        StaticInitializer = staticInitializer;
    }

    /// <summary>Gets the exact using logical Module identity.</summary>
    public string UsingModuleIdentity => ModuleBindings.UsingModuleIdentity;

    /// <summary>Gets the immutable selected provider context for this registration.</summary>
    public CoreProviderModuleBindings ModuleBindings { get; }

    /// <summary>Gets the selected nominal enum declaration for static-initialization triggers.</summary>
    public CoreEnumDefinition TriggerEnum { get; }

    /// <summary>Gets the selected StaticInitializer attribute schema.</summary>
    public CoreStaticInitializerAttributeSchema StaticInitializer { get; }

    /// <summary>Gets the exact grouped-static-field trigger member.</summary>
    public CoreEnumMemberDefinition GroupedStaticFieldAccess
        => TriggerEnum.Members.Single(static member =>
            member.Name == "GroupedStaticFieldAccess");

    /// <summary>Gets the exact runtime-static-member trigger member.</summary>
    public CoreEnumMemberDefinition RuntimeStaticMemberAccess
        => TriggerEnum.Members.Single(static member =>
            member.Name == "RuntimeStaticMemberAccess");

    /// <summary>
    /// Creates a registration after validating its complete provider, enum and schema shapes.
    /// </summary>
    /// <exception cref="ArgumentNullException">A required registration component is null.</exception>
    /// <exception cref="ArgumentException">A declaration, mapping or owner relationship is invalid.</exception>
    public static StaticInitializationCoreRegistrationSet Create(
        CoreProviderModuleBindings moduleBindings,
        CoreEnumDefinition triggerEnum,
        CoreStaticInitializerAttributeSchema staticInitializer)
    {
        ArgumentNullException.ThrowIfNull(moduleBindings);
        ArgumentNullException.ThrowIfNull(triggerEnum);
        ArgumentNullException.ThrowIfNull(staticInitializer);

        var registration = new StaticInitializationCoreRegistrationSet(
            moduleBindings, triggerEnum, staticInitializer);
        if (registration.GetInvalidReason() is string reason)
        {
            throw new ArgumentException(
                $"The selected static-initialization Core registration is invalid: {reason}.",
                nameof(moduleBindings));
        }

        return registration;
    }

    /// <summary>Whether the complete static-initialization registration has its exact selected shape.</summary>
    public bool IsValid() => GetInvalidReason() is null;

    internal string? GetInvalidReason()
    {
        if (!ModuleBindings.IsValid())
        {
            return "the provider Module binding context is invalid";
        }
        if (!IsTriggerEnum(TriggerEnum))
        {
            return "the trigger enum does not match its exact registered shape";
        }
        if (!IsStaticInitializerSchema(StaticInitializer,
                TriggerEnum.Identity))
        {
            return "the StaticInitializer schema identity, target or argument is invalid";
        }
        if (TriggerEnum.QualifiedName == StaticInitializer.QualifiedName)
        {
            return "the selected source declaration names are ambiguous";
        }

        var identities = new HashSet<CoreDeclarationIdentity>();
        var exportKeys = new HashSet<(string Provider, string Key)>();
        if (!AddDeclarationIdentity(TriggerEnum.Identity)
            || !AddExportIdentity(TriggerEnum.Identity)
            || !AddProvider(TriggerEnum.Identity.ProviderIdentity)
            || !ModuleBindings.ContainsDeclaration(TriggerEnum))
        {
            return "the trigger enum identity is missing or duplicated";
        }

        foreach (var member in TriggerEnum.Members)
        {
            if (member is null || !AddDeclarationIdentity(member.Identity)
                || !AddProvider(member.Identity.ProviderIdentity))
            {
                return "a trigger member identity is missing or duplicated";
            }
        }

        if (!AddDeclarationIdentity(StaticInitializer.Identity)
            || !AddExportIdentity(StaticInitializer.Identity)
            || !AddProvider(StaticInitializer.Identity.ProviderIdentity)
            || !ModuleBindings.ContainsDeclaration(StaticInitializer))
        {
            return "the StaticInitializer schema identity is missing or duplicated";
        }

        return null;

        bool AddDeclarationIdentity(CoreDeclarationIdentity identity)
        {
            return identity.IsValid && identities.Add(identity);
        }

        bool AddExportIdentity(CoreDeclarationIdentity identity)
        {
            return exportKeys.Add((identity.ProviderIdentity,
                identity.ExportKey));
        }

        bool AddProvider(string providerIdentity)
        {
            return ModuleBindings.ContainsProvider(providerIdentity);
        }
    }

    private static bool IsTriggerEnum(CoreEnumDefinition definition)
    {
        if (definition is null || !definition.Identity.IsValid
            || definition.Identity.Kind != CoreDeclarationKind.EnumType
            || definition.QualifiedName != "Terrum.StaticInitializationTrigger"
            || definition.UnderlyingType != PrimitiveType.Int32
            || definition.GenericArity != 0
            || !definition.IsPublic || !definition.IsPrimary || definition.IsFlags
            || definition.BlittableTargetRequirement != 0
            || definition.Members.IsDefault
            || definition.Members.Length != 2)
        {
            return false;
        }

        var expectedMembers = new (string Name, int Value)[] {
            ("GroupedStaticFieldAccess", 0),
            ("RuntimeStaticMemberAccess", 1),
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

    private static bool IsStaticInitializerSchema(
        CoreStaticInitializerAttributeSchema schema,
        CoreDeclarationIdentity triggerEnum)
        => schema is not null && schema.Identity.IsValid
            && schema.Identity.Kind == CoreDeclarationKind.AttributeSchema
            && schema.QualifiedName == "Terrum.StaticInitializer"
            && !schema.Arguments.IsDefault
            && schema.Arguments is [
                {
                    Name: "Trigger",
                    EnumType: var enumType,
                    IsRequired: true,
                    AllowsPositional: true,
                    AllowsNamed: false,
                    HasDefault: false,
                }]
            && enumType == triggerEnum
            && schema.Target
                == CoreStaticInitializerAttributeTarget.PrimaryStaticConstructor
            && schema.IsPrimaryTargetOnly
            && schema.GenericArity == 0
            && schema.IsPublic && schema.IsPrimary && !schema.IsRepeatable;
}

/// <summary>
/// Immutable host-selected Core identity for LazyInitialization in one using
/// Module, independent of static-constructor trigger selection.
/// </summary>
public sealed class LazyInitializationCoreRegistrationSet
{
            private LazyInitializationCoreRegistrationSet(
                CoreProviderModuleBindings moduleBindings,
                CoreLazyInitializationAttributeSchema lazyInitialization)
            {
                ModuleBindings = moduleBindings;
                LazyInitialization = lazyInitialization;
            }

            /// <summary>Gets the exact using logical Module identity.</summary>
            public string UsingModuleIdentity => ModuleBindings.UsingModuleIdentity;

            /// <summary>Gets the immutable selected provider context for this registration.</summary>
            public CoreProviderModuleBindings ModuleBindings { get; }

            /// <summary>Gets the exact selected LazyInitialization schema.</summary>
            public CoreLazyInitializationAttributeSchema LazyInitialization { get; }

            /// <summary>Creates a registration after validating its complete provider and source schema.</summary>
            /// <exception cref="ArgumentNullException">A required registration component is null.</exception>
            /// <exception cref="ArgumentException">A declaration, mapping or owner relationship is invalid.</exception>
            public static LazyInitializationCoreRegistrationSet Create(
                CoreProviderModuleBindings moduleBindings,
                CoreLazyInitializationAttributeSchema lazyInitialization)
            {
                ArgumentNullException.ThrowIfNull(moduleBindings);
                ArgumentNullException.ThrowIfNull(lazyInitialization);

                var registration = new LazyInitializationCoreRegistrationSet(
                    moduleBindings, lazyInitialization);
                if (registration.GetInvalidReason() is string reason)
                {
                    throw new ArgumentException(
                        $"The selected LazyInitialization Core registration is invalid: {reason}.",
                        nameof(moduleBindings));
                }

                return registration;
            }

            /// <summary>Whether the complete LazyInitialization registration has its exact selected shape.</summary>
            public bool IsValid() => GetInvalidReason() is null;

            internal string? GetInvalidReason()
            {
                if (!ModuleBindings.IsValid())
                {
                    return "the provider Module binding context is invalid";
                }
                if (!LazyInitialization.IsStructurallyValid
                    || !ModuleBindings.ContainsProvider(
                        LazyInitialization.Identity.ProviderIdentity)
                    || !ModuleBindings.ContainsDeclaration(LazyInitialization))
                {
                    return "the selected LazyInitialization schema or provider declaration is invalid";
                }

                return null;
            }
}
