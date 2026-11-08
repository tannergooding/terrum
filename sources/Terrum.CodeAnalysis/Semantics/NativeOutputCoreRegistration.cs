// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>A complete registered NativeOutput source attribute schema.</summary>
/// <param name="Identity">The exact selected attribute declaration identity.</param>
/// <param name="QualifiedName">The checked source-qualified attribute name.</param>
/// <param name="Arguments">The complete ordered argument schema.</param>
/// <param name="Placement">The exact registered source placement.</param>
/// <param name="Targets">The complete set of permitted source target contexts.</param>
/// <param name="GenericArity">The schema's generic arity.</param>
/// <param name="IsPublic">Whether the schema is public.</param>
/// <param name="IsPrimary">Whether this is the primary schema declaration.</param>
/// <param name="IsRepeatable">Whether the schema permits repeated placement.</param>
public sealed record CoreNativeOutputAttributeSchema(
    CoreDeclarationIdentity Identity,
    string QualifiedName,
    ImmutableArray<CoreNativeAttributeArgument> Arguments,
    string Placement,
    ImmutableHashSet<string> Targets,
    int GenericArity,
    bool IsPublic,
    bool IsPrimary,
    bool IsRepeatable)
{
    internal bool IsStructurallyValid
    {
        get
        {
            if (!IsValidDeclarationIdentity(Identity)
                || Identity.Kind != CoreDeclarationKind.AttributeSchema
                || !IsValidText(QualifiedName)
                || Arguments.IsDefault
                || !IsValidText(Placement)
                || Targets is null || Targets.Count == 0
                || Targets.Any(static target => !IsValidText(target))
                || GenericArity < 0
                || Arguments.Any(static argument => !argument.IsValid))
            {
                return false;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            return Arguments.All(argument => argument.Name is not string name
                || names.Add(name));
        }
    }

    internal static bool AreEquivalent(
        CoreNativeOutputAttributeSchema left,
        CoreNativeOutputAttributeSchema right)
    {
        if (left.Identity != right.Identity
            || left.QualifiedName != right.QualifiedName
            || left.Arguments.IsDefault || right.Arguments.IsDefault
            || left.Arguments.Length != right.Arguments.Length
            || !string.Equals(left.Placement, right.Placement,
                StringComparison.Ordinal)
            || !HaveSameOrdinalValues(left.Targets, right.Targets)
            || left.GenericArity != right.GenericArity
            || left.IsPublic != right.IsPublic
            || left.IsPrimary != right.IsPrimary
            || left.IsRepeatable != right.IsRepeatable)
        {
            return false;
        }

        for (var index = 0; index < left.Arguments.Length; index++)
        {
            var leftArgument = left.Arguments[index];
            var rightArgument = right.Arguments[index];
            if (leftArgument.Name != rightArgument.Name
                || leftArgument.Kind != rightArgument.Kind
                || leftArgument.EnumType != rightArgument.EnumType
                || leftArgument.IsRequired != rightArgument.IsRequired
                || leftArgument.AllowsPositional
                    != rightArgument.AllowsPositional
                || leftArgument.AllowsNamed != rightArgument.AllowsNamed
                || !HaveSameOrdinalValues(leftArgument.Contexts,
                    rightArgument.Contexts)
                || leftArgument.Default != rightArgument.Default)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidText(string value)
        => !string.IsNullOrWhiteSpace(value)
            && !value.Contains('\0', StringComparison.Ordinal);

    private static bool IsValidDeclarationIdentity(
        CoreDeclarationIdentity identity)
        => identity.IsValid && IsValidText(identity.ProviderIdentity)
            && IsValidText(identity.ExportKey);

    private static bool HaveSameOrdinalValues(
        ImmutableHashSet<string> left,
        ImmutableHashSet<string> right)
        => left is not null && right is not null
            && left.Count == right.Count
            && left.ToHashSet(StringComparer.Ordinal).SetEquals(
                right.ToHashSet(StringComparer.Ordinal));
}

/// <summary>
/// Immutable host-selected NativeOutput source declarations for one using
/// Module. The role is independent of NativeCore manifest roles.
/// </summary>
public sealed class NativeOutputCoreRegistrationSet
{
    private static readonly ImmutableHashSet<string> s_outputContexts
        = ImmutableHashSet.Create(StringComparer.Ordinal,
            "direct-import-parameter",
            "foreign-interface-method-parameter");

    private NativeOutputCoreRegistrationSet(
        CoreProviderModuleBindings moduleBindings,
        CoreEnumDefinition outputConditions,
        CoreNativeOutputAttributeSchema nativeOutput)
    {
        ModuleBindings = moduleBindings;
        OutputConditions = outputConditions;
        NativeOutput = nativeOutput;
    }

    /// <summary>The exact using logical Module identity.</summary>
    public string UsingModuleIdentity => ModuleBindings.UsingModuleIdentity;

    /// <summary>The immutable selected provider context for this registration.</summary>
    public CoreProviderModuleBindings ModuleBindings { get; }

    /// <summary>The selected nominal NativeOutputCondition enum declaration.</summary>
    public CoreEnumDefinition OutputConditions { get; }

    /// <summary>The selected NativeOutput parameter attribute schema.</summary>
    public CoreNativeOutputAttributeSchema NativeOutput { get; }

    /// <summary>
    /// Creates a registration after validating its complete provider, enum and
    /// schema shapes.
    /// </summary>
    /// <exception cref="ArgumentNullException">A required registration component is null.</exception>
    /// <exception cref="ArgumentException">A declaration, mapping or owner relationship is invalid.</exception>
    public static NativeOutputCoreRegistrationSet Create(
        CoreProviderModuleBindings moduleBindings,
        CoreEnumDefinition outputConditions,
        CoreNativeOutputAttributeSchema nativeOutput)
    {
        ArgumentNullException.ThrowIfNull(moduleBindings);
        ArgumentNullException.ThrowIfNull(outputConditions);
        ArgumentNullException.ThrowIfNull(nativeOutput);

        var registration = new NativeOutputCoreRegistrationSet(
            moduleBindings, outputConditions, nativeOutput);
        if (registration.GetInvalidReason() is string reason)
        {
            throw new ArgumentException(
                $"The selected NativeOutput Core registration is invalid: {reason}.",
                nameof(moduleBindings));
        }

        return registration;
    }

    /// <summary>Whether the complete NativeOutput registration has its exact selected shape.</summary>
    public bool IsValid() => GetInvalidReason() is null;

    internal string? GetInvalidReason()
    {
        if (!ModuleBindings.IsValid())
        {
            return "the provider Module binding context is invalid";
        }
        if (!IsOutputConditionsEnum(OutputConditions))
        {
            return "the NativeOutputCondition enum does not match its exact registered shape";
        }
        if (!IsNativeOutputSchema(NativeOutput, OutputConditions.Identity))
        {
            return "the NativeOutput schema identity, targets or arguments are invalid";
        }
        if (!ModuleBindings.ContainsProvider(
                OutputConditions.Identity.ProviderIdentity)
            || !ModuleBindings.ContainsDeclaration(OutputConditions)
            || !ModuleBindings.ContainsProvider(
                NativeOutput.Identity.ProviderIdentity)
            || !ModuleBindings.ContainsDeclaration(NativeOutput))
        {
            return "a registered declaration has no matching selected provider export snapshot";
        }

        var identities = new HashSet<CoreDeclarationIdentity>();
        var exportKeys = new HashSet<(string Provider, string Key)>();
        if (!AddDeclaration(OutputConditions.Identity)
            || !AddExport(OutputConditions.Identity)
            || !AddDeclaration(NativeOutput.Identity)
            || !AddExport(NativeOutput.Identity))
        {
            return "the selected enum or schema identities are ambiguous";
        }
        foreach (var member in OutputConditions.Members)
        {
            if (!AddDeclaration(member.Identity))
            {
                return "a NativeOutputCondition member identity is missing or duplicated";
            }
        }

        return null;

        bool AddDeclaration(CoreDeclarationIdentity identity)
        {
            return identity.IsValid && identities.Add(identity);
        }

        bool AddExport(CoreDeclarationIdentity identity)
        {
            return exportKeys.Add((identity.ProviderIdentity, identity.ExportKey));
        }
    }

    private static bool IsOutputConditionsEnum(CoreEnumDefinition definition)
    {
        if (definition is null
            || !IsValidDeclarationIdentity(definition.Identity)
            || definition.Identity.Kind != CoreDeclarationKind.EnumType
            || definition.QualifiedName != "Terrum.Interop.NativeOutputCondition"
            || definition.UnderlyingType != PrimitiveType.Int32
            || definition.GenericArity != 0
            || !definition.IsPublic || !definition.IsPrimary || definition.IsFlags
            || definition.BlittableTargetRequirement is not (0 or 1)
            || definition.Members.IsDefault || definition.Members.Length != 2)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<CoreDeclarationIdentity>();
        var declarationKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, value) in new[] {
            (Name: "NonZero", Value: 0),
            (Name: "NonNegative", Value: 1),
        })
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
                if (!IsValidDeclarationIdentity(member.Identity)
                    || member.Identity.Kind != CoreDeclarationKind.EnumMember
                    || member.Identity.ProviderIdentity
                        != definition.Identity.ProviderIdentity
                    || member.DeclaringEnum != definition.Identity
                    || !member.IsPublic
                    || string.IsNullOrWhiteSpace(member.DeclarationKey)
                    || member.DeclarationKey.Contains('\0',
                        StringComparison.Ordinal)
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

        return true;
    }

    private static bool IsNativeOutputSchema(
        CoreNativeOutputAttributeSchema schema,
        CoreDeclarationIdentity outputConditions)
    {
        if (schema is null || !schema.IsStructurallyValid
            || schema.Identity.Kind != CoreDeclarationKind.AttributeSchema
            || schema.QualifiedName != "Terrum.Interop.NativeOutput"
            || schema.Placement != "primary-callable-contract"
            || !HaveSameOrdinalValues(schema.Targets, s_outputContexts)
            || schema.GenericArity != 0 || !schema.IsPublic
            || !schema.IsPrimary || schema.IsRepeatable
            || schema.Arguments.Length != 1)
        {
            return false;
        }

        var argument = schema.Arguments[0];
        return argument.Name == "When"
            && argument.Kind == CoreNativeAttributeArgumentKind.Enum
            && argument.EnumType == outputConditions
            && argument.IsRequired
            && !argument.AllowsPositional
            && argument.AllowsNamed
            && HaveSameOrdinalValues(argument.Contexts, s_outputContexts)
            && argument.Default is null;
    }

    private static bool IsValidDeclarationIdentity(
        CoreDeclarationIdentity identity)
        => identity.IsValid
            && IsValidText(identity.ProviderIdentity)
            && IsValidText(identity.ExportKey);

    private static bool IsValidText(string value)
        => !string.IsNullOrWhiteSpace(value)
            && !value.Contains('\0', StringComparison.Ordinal);

    private static bool HaveSameOrdinalValues(
        ImmutableHashSet<string> left,
        ImmutableHashSet<string> right)
        => left is not null && right is not null
            && left.Count == right.Count
            && left.ToHashSet(StringComparer.Ordinal).SetEquals(
                right.ToHashSet(StringComparer.Ordinal));
}
