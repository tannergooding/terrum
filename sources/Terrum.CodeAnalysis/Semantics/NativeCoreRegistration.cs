// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Identifies the semantic declaration family selected by a Core provider.</summary>
public enum CoreDeclarationKind
{
    /// <summary>No declaration kind; invalid in a selected identity.</summary>
    None = 0,
    /// <summary>A nominal enum type declaration.</summary>
    EnumType = 1,
    /// <summary>A member owned by a nominal enum type.</summary>
    EnumMember = 2,
    /// <summary>A registered semantic attribute schema.</summary>
    AttributeSchema = 3,
}

/// <summary>An exact provider, export-key and declaration-kind identity supplied by the host adapter.</summary>
/// <param name="ProviderIdentity">The selected provider's semantic identity.</param>
/// <param name="ExportKey">The declaration's exact export key.</param>
/// <param name="Kind">The selected declaration family.</param>
public readonly record struct CoreDeclarationIdentity(
    string ProviderIdentity, string ExportKey, CoreDeclarationKind Kind)
{
    /// <summary>Whether the identity has all required components and a supported declaration kind.</summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(ProviderIdentity)
        && !string.IsNullOrWhiteSpace(ExportKey)
        && Kind is CoreDeclarationKind.EnumType
            or CoreDeclarationKind.EnumMember
            or CoreDeclarationKind.AttributeSchema;
}

/// <summary>A checked public member definition in a registered Core enum.</summary>
/// <param name="Identity">The exact member declaration identity.</param>
/// <param name="DeclaringEnum">The exact enum that owns this member.</param>
/// <param name="Name">The source member name.</param>
/// <param name="Value">The member's checked underlying value.</param>
/// <param name="IsPublic">Whether the member is publicly accessible.</param>
public sealed record CoreEnumMemberDefinition(
    CoreDeclarationIdentity Identity,
    CoreDeclarationIdentity DeclaringEnum,
    string Name,
    int Value,
    bool IsPublic);

/// <summary>A complete checked public enum declaration selected by a Core provider.</summary>
/// <param name="Identity">The exact enum declaration identity.</param>
/// <param name="QualifiedName">The source-qualified enum name.</param>
/// <param name="UnderlyingType">The exact underlying semantic type.</param>
/// <param name="Members">The complete member definition set.</param>
/// <param name="GenericArity">The declaration's generic arity.</param>
/// <param name="IsPublic">Whether the enum is publicly accessible.</param>
/// <param name="IsPrimary">Whether this is the primary declaration.</param>
/// <param name="IsFlags">Whether the enum carries the flags role.</param>
public sealed record CoreEnumDefinition(
    CoreDeclarationIdentity Identity,
    string QualifiedName,
    PrimitiveType UnderlyingType,
    ImmutableArray<CoreEnumMemberDefinition> Members,
    int GenericArity,
    bool IsPublic,
    bool IsPrimary,
    bool IsFlags);

/// <summary>The native semantic role owned by a registered Core attribute schema.</summary>
public enum CoreNativeAttributeRole
{
    /// <summary>No native role; invalid in a selected schema.</summary>
    None = 0,
    /// <summary>A direct foreign function import.</summary>
    NativeImport = 1,
    /// <summary>A function callable through a native ABI.</summary>
    NativeCallable = 2,
    /// <summary>A function-pointer type-use convention selector.</summary>
    NativeCallConv = 3,
    /// <summary>A native export declaration.</summary>
    NativeExport = 4,
}

/// <summary>A complete checked attribute schema selected by a Core provider.</summary>
/// <param name="Identity">The exact semantic schema identity.</param>
/// <param name="QualifiedName">The source-qualified schema name.</param>
/// <param name="Role">The native declaration role owned by this schema.</param>
/// <param name="CallingConventionEnum">The exact convention enum identity, when applicable.</param>
/// <param name="ResolutionModeEnum">The exact resolution-mode enum identity, when applicable.</param>
/// <param name="ResolutionPolicyEnum">The exact resolution-policy enum identity, when applicable.</param>
/// <param name="GenericArity">The schema's generic arity.</param>
/// <param name="IsPublic">Whether the schema is publicly accessible.</param>
/// <param name="IsPrimary">Whether this is the primary schema declaration.</param>
/// <param name="IsRepeatable">Whether the schema permits repeated placement.</param>
public sealed record CoreNativeAttributeSchema(
    CoreDeclarationIdentity Identity,
    string QualifiedName,
    CoreNativeAttributeRole Role,
    CoreDeclarationIdentity? CallingConventionEnum,
    CoreDeclarationIdentity? ResolutionModeEnum,
    CoreDeclarationIdentity? ResolutionPolicyEnum,
    int GenericArity,
    bool IsPublic,
    bool IsPrimary,
    bool IsRepeatable);

/// <summary>
/// Immutable host-selected Core declarations for one using Module.
/// </summary>
/// <remarks>
/// The embedding host supplies identities from its authorized provider adapter.
/// This descriptor is not serialized and does not authenticate a provider.
/// </remarks>
public sealed class NativeCoreRegistrationSet
{
    private NativeCoreRegistrationSet(string usingModuleIdentity,
        CoreEnumDefinition callingConventions,
        CoreEnumDefinition resolutionModes,
        CoreEnumDefinition resolutionPolicies,
        CoreNativeAttributeSchema nativeImport,
        CoreNativeAttributeSchema nativeCallable,
        CoreNativeAttributeSchema nativeCallConv,
        CoreNativeAttributeSchema nativeExport)
    {
        UsingModuleIdentity = usingModuleIdentity;
        CallingConventions = callingConventions;
        ResolutionModes = resolutionModes;
        ResolutionPolicies = resolutionPolicies;
        NativeImport = nativeImport;
        NativeCallable = nativeCallable;
        NativeCallConv = nativeCallConv;
        NativeExport = nativeExport;
    }

    /// <summary>The using logical Module identity for this registration.</summary>
    public string UsingModuleIdentity { get; }

    /// <summary>The selected convention enum declaration.</summary>
    public CoreEnumDefinition CallingConventions { get; }

    /// <summary>The selected import resolution-mode enum declaration.</summary>
    public CoreEnumDefinition ResolutionModes { get; }

    /// <summary>The selected import resolution-policy enum declaration.</summary>
    public CoreEnumDefinition ResolutionPolicies { get; }

    /// <summary>The complete selected enum declarations in this registration.</summary>
    public ImmutableArray<CoreEnumDefinition> EnumDefinitions =>
        [CallingConventions, ResolutionModes, ResolutionPolicies];

    /// <summary>The selected direct-import schema.</summary>
    public CoreNativeAttributeSchema NativeImport { get; }

    /// <summary>The selected native-callable schema.</summary>
    public CoreNativeAttributeSchema NativeCallable { get; }

    /// <summary>The selected function-pointer convention schema.</summary>
    public CoreNativeAttributeSchema NativeCallConv { get; }

    /// <summary>The selected native-export schema.</summary>
    public CoreNativeAttributeSchema NativeExport { get; }

    internal static bool NameResolvesTo(string sourceName, string qualifiedName,
        string currentNamespace, IEnumerable<string> usings)
    {
        if (sourceName == qualifiedName)
        {
            return true;
        }

        var separator = qualifiedName.LastIndexOf('.', StringComparison.Ordinal);
        if (separator < 0 || sourceName != qualifiedName[(separator + 1)..])
        {
            return false;
        }

        var declaringNamespace = qualifiedName[..separator];
        return currentNamespace == declaringNamespace
            || usings.Contains(declaringNamespace, StringComparer.Ordinal);
    }

    /// <summary>Creates a registration after validating its complete enum and schema shapes.</summary>
    /// <exception cref="ArgumentException">A declaration, mapping or owner relationship is invalid.</exception>
    public static NativeCoreRegistrationSet Create(string usingModuleIdentity,
        CoreEnumDefinition callingConventions,
        CoreEnumDefinition resolutionModes,
        CoreEnumDefinition resolutionPolicies,
        CoreNativeAttributeSchema nativeImport,
        CoreNativeAttributeSchema nativeCallable,
        CoreNativeAttributeSchema nativeCallConv,
        CoreNativeAttributeSchema nativeExport)
    {
        var registration = new NativeCoreRegistrationSet(usingModuleIdentity,
            callingConventions, resolutionModes, resolutionPolicies, nativeImport,
            nativeCallable, nativeCallConv, nativeExport);
        if (registration.GetInvalidReason() is string reason)
        {
            throw new ArgumentException($"The selected Core native registration is invalid: {reason}.",
                nameof(usingModuleIdentity));
        }

        return registration;
    }

    /// <summary>Whether the complete registration has the exact selected Core shape.</summary>
    public bool IsValid() => GetInvalidReason() is null;

    internal string? GetInvalidReason()
    {
        if (string.IsNullOrWhiteSpace(UsingModuleIdentity))
        {
            return "the using Module identity is missing";
        }
        if (!IsEnum(CallingConventions, "Terrum.Interop.NativeCallingConvention",
                ("Unmanaged", 1), ("Cdecl", 2), ("Stdcall", 3), ("Vectorcall", 4)))
        {
            return "the NativeCallingConvention enum does not match its exact registered shape";
        }
        if (!IsEnum(ResolutionModes, "Terrum.Interop.NativeImportResolutionMode",
                ("Link", 1), ("Dynamic", 2)))
        {
            return "the NativeImportResolutionMode enum does not match its exact registered shape";
        }
        if (!IsEnum(ResolutionPolicies, "Terrum.Interop.NativeImportResolutionPolicy",
                ("Default", 1), ("CompatibilitySearch", 2)))
        {
            return "the NativeImportResolutionPolicy enum does not match its exact registered shape";
        }
        if (!IsSchema(NativeImport, "Terrum.Interop.NativeImport",
                CoreNativeAttributeRole.NativeImport))
        {
            return "the NativeImport schema identity or owner is invalid";
        }
        if (!IsSchema(NativeCallable, "Terrum.Interop.NativeCallable",
                CoreNativeAttributeRole.NativeCallable))
        {
            return "the NativeCallable schema identity or owner is invalid";
        }
        if (!IsSchema(NativeCallConv, "Terrum.Interop.NativeCallConv",
                CoreNativeAttributeRole.NativeCallConv))
        {
            return "the NativeCallConv schema identity or owner is invalid";
        }
        if (!IsSchema(NativeExport, "Terrum.Interop.NativeExport",
                CoreNativeAttributeRole.NativeExport))
        {
            return "the NativeExport schema identity or owner is invalid";
        }

        var conventionIdentity = CallingConventions.Identity;
        var resolutionModeIdentity = ResolutionModes.Identity;
        var resolutionPolicyIdentity = ResolutionPolicies.Identity;
        if (NativeImport.CallingConventionEnum != conventionIdentity
            || NativeImport.ResolutionModeEnum != resolutionModeIdentity
            || NativeImport.ResolutionPolicyEnum != resolutionPolicyIdentity
            || NativeCallable.CallingConventionEnum != conventionIdentity
            || NativeCallable.ResolutionModeEnum is not null
            || NativeCallable.ResolutionPolicyEnum is not null
            || NativeCallConv.CallingConventionEnum != conventionIdentity
            || NativeCallConv.ResolutionModeEnum is not null
            || NativeCallConv.ResolutionPolicyEnum is not null
            || NativeExport.CallingConventionEnum is not null
            || NativeExport.ResolutionModeEnum is not null
            || NativeExport.ResolutionPolicyEnum is not null)
        {
            return "native schema argument types do not refer to the selected enum identities";
        }

        var identities = new List<CoreDeclarationIdentity>
        {
            CallingConventions.Identity,
            ResolutionModes.Identity,
            ResolutionPolicies.Identity,
            NativeImport.Identity,
            NativeCallable.Identity,
            NativeCallConv.Identity,
            NativeExport.Identity,
        };
        identities.AddRange(CallingConventions.Members.Select(static member => member.Identity));
        identities.AddRange(ResolutionModes.Members.Select(static member => member.Identity));
        identities.AddRange(ResolutionPolicies.Members.Select(static member => member.Identity));
        var duplicateIdentity = identities.GroupBy(static identity => identity)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateIdentity is not null)
        {
            return $"registered declaration identity '{duplicateIdentity.Key.ProviderIdentity}:"
                + $"{duplicateIdentity.Key.ExportKey}' is used more than once";
        }

        return null;
    }

    private static bool IsEnum(CoreEnumDefinition definition, string qualifiedName,
        params (string Name, int Value)[] members)
    {
        if (definition is null || !definition.Identity.IsValid
            || definition.Identity.Kind != CoreDeclarationKind.EnumType
            || definition.QualifiedName != qualifiedName
            || definition.UnderlyingType != PrimitiveType.Int32
            || definition.GenericArity != 0 || !definition.IsPublic
            || !definition.IsPrimary || definition.IsFlags
            || definition.Members.IsDefault
            || definition.Members.Length != members.Length)
        {
            return false;
        }

        var seenIdentities = new System.Collections.Generic.HashSet<CoreDeclarationIdentity>();
        var seenNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var (expectedName, expectedValue) in members)
        {
            var matches = definition.Members.Where(member => member is not null
                && member.Name == expectedName && member.Value == expectedValue).ToArray();
            if (matches.Length != 1)
            {
                return false;
            }

            var member = matches[0];
            if (!member.Identity.IsValid
                || member.Identity.Kind != CoreDeclarationKind.EnumMember
                || member.Identity.ProviderIdentity != definition.Identity.ProviderIdentity
                || member.DeclaringEnum != definition.Identity
                || !member.IsPublic
                || !seenIdentities.Add(member.Identity)
                || !seenNames.Add(member.Name))
            {
                return false;
            }
        }

        return seenIdentities.Count == definition.Members.Length
            && definition.Members.All(member => member is not null
                && member.DeclaringEnum == definition.Identity);
    }

    private static bool IsSchema(CoreNativeAttributeSchema schema, string qualifiedName,
        CoreNativeAttributeRole role) =>
        schema is not null && schema.Identity.IsValid
        && schema.Identity.Kind == CoreDeclarationKind.AttributeSchema
        && schema.QualifiedName == qualifiedName
        && schema.Role == role
        && schema.GenericArity == 0
        && schema.IsPublic && schema.IsPrimary && !schema.IsRepeatable;
}
