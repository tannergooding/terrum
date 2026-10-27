// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal enum CoreDeclarationKind : byte
{
    EnumType = 1,
    EnumMember = 2,
    AttributeSchema = 3,
}

internal readonly record struct CoreDeclarationIdentity(
    string ProviderIdentity, string ExportKey, CoreDeclarationKind Kind)
{
    internal bool IsValid =>
        !string.IsNullOrWhiteSpace(ProviderIdentity)
        && !string.IsNullOrWhiteSpace(ExportKey)
        && Kind is CoreDeclarationKind.EnumType
            or CoreDeclarationKind.EnumMember
            or CoreDeclarationKind.AttributeSchema;
}

internal sealed record CoreEnumMemberDefinition(
    CoreDeclarationIdentity Identity,
    CoreDeclarationIdentity DeclaringEnum,
    string Name,
    int Value,
    bool IsPublic);

internal sealed record CoreEnumDefinition(
    CoreDeclarationIdentity Identity,
    string QualifiedName,
    PrimitiveType UnderlyingType,
    ImmutableArray<CoreEnumMemberDefinition> Members,
    int GenericArity,
    bool IsPublic,
    bool IsPrimary,
    bool IsFlags);

internal enum CoreNativeAttributeRole : byte
{
    NativeImport = 1,
    NativeCallable = 2,
    NativeCallConv = 3,
    NativeExport = 4,
}

internal sealed record CoreNativeAttributeSchema(
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

// A trusted in-process adapter supplies these exact handles; the set is not reconstructed from names or serialized.
internal sealed class NativeCoreRegistrationSet
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

    internal string UsingModuleIdentity { get; }
    internal CoreEnumDefinition CallingConventions { get; }
    internal CoreEnumDefinition ResolutionModes { get; }
    internal CoreEnumDefinition ResolutionPolicies { get; }
    internal CoreNativeAttributeSchema NativeImport { get; }
    internal CoreNativeAttributeSchema NativeCallable { get; }
    internal CoreNativeAttributeSchema NativeCallConv { get; }
    internal CoreNativeAttributeSchema NativeExport { get; }

    internal static NativeCoreRegistrationSet Create(string usingModuleIdentity,
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

    internal bool IsValid() => GetInvalidReason() is null;

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

        var identities = new[]
        {
            CallingConventions.Identity,
            ResolutionModes.Identity,
            ResolutionPolicies.Identity,
            NativeImport.Identity,
            NativeCallable.Identity,
            NativeCallConv.Identity,
            NativeExport.Identity,
        };
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
        foreach (var expected in members)
        {
            var matches = definition.Members.Where(member => member is not null
                && member.Name == expected.Name && member.Value == expected.Value).ToArray();
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
