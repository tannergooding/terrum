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

/// <summary>An exact host-selected provider schema identity and revision.</summary>
/// <param name="Identity">The nonempty, ordinal schema identity token.</param>
/// <param name="Revision">The nonzero exact schema revision.</param>
public readonly record struct CoreProviderSchemaSelection(string Identity, uint Revision);

/// <summary>The selected logical Module and requirements for one Core provider snapshot.</summary>
/// <param name="ModuleIdentity">The exact logical Module identity.</param>
/// <param name="RevisionIdentity">The host-selected producer revision token.</param>
/// <param name="Schema">The exact whole schema/catalog selection.</param>
/// <param name="GoverningSemanticEdition">The selected semantic edition.</param>
/// <param name="TargetRequirement">The provider Module target requirement.</param>
/// <param name="RuntimePolicy">The provider Module runtime policy.</param>
public readonly record struct CoreProviderModuleOrigin(
    string ModuleIdentity,
    string RevisionIdentity,
    CoreProviderSchemaSelection Schema,
    uint GoverningSemanticEdition,
    uint TargetRequirement,
    uint RuntimePolicy);

/// <summary>Immutable host-selected Module origins for one using Module's Core registration.</summary>
/// <remarks>The object identity is the binding snapshot token; it is not serialized or an authentication claim.</remarks>
public sealed class CoreProviderModuleBindings
{
    private CoreProviderModuleBindings(string usingModuleIdentity,
        ImmutableArray<CoreProviderModuleOrigin> providers,
        ImmutableArray<CoreProviderDeclarationSnapshot> declarations)
    {
        UsingModuleIdentity = usingModuleIdentity;
        Providers = providers;
        Declarations = declarations;
    }

    /// <summary>The exact using logical Module identity.</summary>
    public string UsingModuleIdentity { get; }

    /// <summary>The exact selected producer Module snapshots used by this registration.</summary>
    public ImmutableArray<CoreProviderModuleOrigin> Providers { get; }

    /// <summary>The exact checked declarations and export keys captured from the selected providers.</summary>
    public ImmutableArray<CoreProviderDeclarationSnapshot> Declarations { get; }

    /// <summary>Creates a binding context after validating all provider origins.</summary>
    /// <exception cref="ArgumentException">The using Module or a selected provider origin is invalid or ambiguous.</exception>
    public static CoreProviderModuleBindings Create(string usingModuleIdentity,
        ImmutableArray<CoreProviderModuleOrigin> providers)
        => Create(usingModuleIdentity, providers, []);

    /// <summary>Creates a binding context after validating origins and their complete selected declarations.</summary>
    /// <exception cref="ArgumentException">An origin, declaration export or owner relationship is invalid or ambiguous.</exception>
    public static CoreProviderModuleBindings Create(string usingModuleIdentity,
        ImmutableArray<CoreProviderModuleOrigin> providers,
        ImmutableArray<CoreProviderDeclarationSnapshot> declarations)
    {
        var bindings = new CoreProviderModuleBindings(usingModuleIdentity, providers,
            declarations);
        if (bindings.GetInvalidReason() is string reason)
        {
            throw new ArgumentException($"The selected Core provider Module bindings are invalid: {reason}.",
                nameof(providers));
        }

        return bindings;
    }

    /// <summary>Whether the complete provider binding context is structurally valid.</summary>
    public bool IsValid() => GetInvalidReason() is null;

    internal bool ContainsProvider(string moduleIdentity)
        => Providers.Any(origin => origin.ModuleIdentity == moduleIdentity);

    internal bool ContainsDeclaration(CoreEnumDefinition definition)
        => Declarations.Any(declaration => declaration.EnumDefinition is CoreEnumDefinition
                exported && AreEquivalent(exported, definition));

    internal bool ContainsDeclaration(CoreNativeAttributeSchema schema)
        => Declarations.Any(declaration =>
            declaration.NativeAttributeSchema is CoreNativeAttributeSchema exported
            && CoreNativeAttributeSchema.AreEquivalent(exported, schema));

    internal bool ContainsDeclaration(CoreLayoutAttributeSchema schema)
        => Declarations.Any(declaration => declaration.LayoutAttributeSchema is
                CoreLayoutAttributeSchema exported
            && exported.Identity == schema.Identity
            && exported.QualifiedName == schema.QualifiedName
            && exported.Arguments.SequenceEqual(schema.Arguments)
            && exported.Targets == schema.Targets
            && exported.IsPrimaryTargetOnly == schema.IsPrimaryTargetOnly
            && exported.GenericArity == schema.GenericArity
            && exported.IsPublic == schema.IsPublic
            && exported.IsPrimary == schema.IsPrimary
            && exported.IsRepeatable == schema.IsRepeatable);

    internal bool ContainsDeclaration(
        CoreStaticInitializerAttributeSchema schema)
        => Declarations.Any(declaration =>
            declaration.StaticInitializerAttributeSchema is
                CoreStaticInitializerAttributeSchema exported
            && CoreStaticInitializerAttributeSchema.AreEquivalent(
                exported, schema));

    internal bool ContainsDeclaration(
        CoreLazyInitializationAttributeSchema schema)
        => Declarations.Any(declaration =>
            declaration.LazyInitializationAttributeSchema is
                CoreLazyInitializationAttributeSchema exported
            && CoreLazyInitializationAttributeSchema.AreEquivalent(
                exported, schema));

    internal string? GetInvalidReason()
    {
        if (!IsValidIdentity(UsingModuleIdentity))
        {
            return "the using Module identity is missing or malformed";
        }
        if (Providers.IsDefaultOrEmpty || Declarations.IsDefault)
        {
            return "selected provider Module origins or declaration snapshots are uninitialized";
        }

        var modules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in Providers)
        {
            if (!IsValidIdentity(provider.ModuleIdentity)
                || !IsValidIdentity(provider.RevisionIdentity)
                || !IsValidIdentity(provider.Schema.Identity)
                || provider.Schema.Revision == 0
                || provider.GoverningSemanticEdition != 1
                || provider.TargetRequirement > 1
                || provider.RuntimePolicy > 1
                || !modules.Add(provider.ModuleIdentity))
            {
                return "a provider Module origin is malformed or has an ambiguous selection";
            }
            if (provider.ModuleIdentity == UsingModuleIdentity)
            {
                return "the using Module cannot also be a selected provider";
            }
        }

        var exportKeys = new HashSet<(string Provider, string Key)>();
        foreach (var declaration in Declarations)
        {
            if (declaration is null || !declaration.IsValid
                || !modules.Contains(declaration.Identity.ProviderIdentity)
                || !exportKeys.Add((declaration.Identity.ProviderIdentity,
                    declaration.Identity.ExportKey)))
            {
                return "a selected declaration export is invalid, duplicated or has no provider origin";
            }
        }

        return null;
    }

    private static bool AreEquivalent(CoreEnumDefinition left,
        CoreEnumDefinition right)
        => left.Identity == right.Identity
            && left.QualifiedName == right.QualifiedName
            && left.UnderlyingType == right.UnderlyingType
            && left.Members.SequenceEqual(right.Members)
            && left.GenericArity == right.GenericArity
            && left.IsPublic == right.IsPublic
            && left.IsPrimary == right.IsPrimary
            && left.IsFlags == right.IsFlags
            && left.BlittableTargetRequirement
                == right.BlittableTargetRequirement;

    private static bool IsValidIdentity(string value)
        => !string.IsNullOrWhiteSpace(value)
            && !value.Contains('\0', StringComparison.Ordinal);
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
    bool IsPublic)
{
    /// <summary>The producer-assigned key that preserves this member declaration in metadata.</summary>
    public string DeclarationKey { get; init; } = string.Empty;
}

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
    bool IsFlags)
{
    /// <summary>The selected target requirement for the enum's Blittable representation, or zero when absent.</summary>
    public int BlittableTargetRequirement { get; init; }
}

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

/// <summary>The registered source-value kind for one native attribute argument.</summary>
public enum CoreNativeAttributeArgumentKind
{
    /// <summary>No valid source argument kind.</summary>
    None = 0,
    /// <summary>A compile-time native library-name literal.</summary>
    LibraryNameLiteral = 1,
    /// <summary>A compile-time native entry-name literal.</summary>
    EntryNameLiteral = 2,
    /// <summary>A value of the exact selected enum type.</summary>
    Enum = 3,
}

/// <summary>The registered default form for a native attribute argument.</summary>
public enum CoreNativeAttributeDefaultKind
{
    /// <summary>No valid default form.</summary>
    None = 0,
    /// <summary>The exact name of the target declaration.</summary>
    DeclarationName = 1,
    /// <summary>A selected member of the argument's exact enum type.</summary>
    EnumMember = 2,
}

/// <summary>A complete registered default for one native attribute argument.</summary>
/// <param name="Kind">The selected default form.</param>
/// <param name="Member">The exact enum member for an EnumMember default.</param>
public readonly record struct CoreNativeAttributeDefault(
    CoreNativeAttributeDefaultKind Kind, CoreDeclarationIdentity? Member)
{
    internal bool IsValid
        => Kind switch {
            CoreNativeAttributeDefaultKind.DeclarationName => Member is null,
            CoreNativeAttributeDefaultKind.EnumMember => Member is
                CoreDeclarationIdentity member
                && member.IsValid
                && member.Kind == CoreDeclarationKind.EnumMember,
            _ => false,
        };
}

/// <summary>A complete ordered source argument slot in a registered native schema.</summary>
/// <param name="Name">The exact source label, or null for an unnamed argument.</param>
/// <param name="Kind">The exact source-value domain.</param>
/// <param name="EnumType">The exact selected enum identity for an enum-valued argument.</param>
/// <param name="IsRequired">Whether the source schema requires the argument.</param>
/// <param name="AllowsPositional">Whether the argument may be supplied positionally.</param>
/// <param name="AllowsNamed">Whether the argument may be supplied by its source label.</param>
/// <param name="Contexts">The complete set of source applicability contexts.</param>
/// <param name="Default">The registered omission rule, when any.</param>
public readonly record struct CoreNativeAttributeArgument(
    string? Name,
    CoreNativeAttributeArgumentKind Kind,
    CoreDeclarationIdentity? EnumType,
    bool IsRequired,
    bool AllowsPositional,
    bool AllowsNamed,
    ImmutableHashSet<string> Contexts,
    CoreNativeAttributeDefault? Default)
{
    internal bool IsValid
    {
        get
        {
            if ((Name is string name && !IsValidText(name))
                || (Name is null && AllowsNamed)
                || Kind is not (CoreNativeAttributeArgumentKind.LibraryNameLiteral
                    or CoreNativeAttributeArgumentKind.EntryNameLiteral
                    or CoreNativeAttributeArgumentKind.Enum)
                || (Kind == CoreNativeAttributeArgumentKind.Enum
                    != (EnumType is CoreDeclarationIdentity enumType
                        && enumType.IsValid
                        && enumType.Kind == CoreDeclarationKind.EnumType))
                || (!AllowsPositional && !AllowsNamed)
                || Contexts is null || Contexts.Count == 0
                || Contexts.Any(static context => !IsValidText(context))
                || (Default is CoreNativeAttributeDefault defaultValue
                    && !defaultValue.IsValid))
            {
                return false;
            }

            return true;
        }
    }

    internal bool IsEquivalent(CoreNativeAttributeArgument other)
        => Name == other.Name
            && Kind == other.Kind
            && EnumType == other.EnumType
            && IsRequired == other.IsRequired
            && AllowsPositional == other.AllowsPositional
            && AllowsNamed == other.AllowsNamed
            && Contexts is not null && other.Contexts is not null
            && Contexts.SetEquals(other.Contexts)
            && Default == other.Default;

    private static bool IsValidText(string value)
        => !string.IsNullOrWhiteSpace(value)
            && !value.Contains('\0', StringComparison.Ordinal);
}

/// <summary>A complete checked attribute schema selected by a Core provider.</summary>
/// <param name="Identity">The exact semantic schema identity.</param>
/// <param name="QualifiedName">The source-qualified schema name.</param>
/// <param name="Role">The native declaration role owned by this schema.</param>
/// <param name="Arguments">The complete ordered source argument schema.</param>
/// <param name="Placement">The exact registered source placement.</param>
/// <param name="Targets">The complete set of permitted source target contexts.</param>
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
    ImmutableArray<CoreNativeAttributeArgument> Arguments,
    string Placement,
    ImmutableHashSet<string> Targets,
    CoreDeclarationIdentity? CallingConventionEnum,
    CoreDeclarationIdentity? ResolutionModeEnum,
    CoreDeclarationIdentity? ResolutionPolicyEnum,
    int GenericArity,
    bool IsPublic,
    bool IsPrimary,
    bool IsRepeatable)
{
    internal bool IsStructurallyValid
    {
        get
        {
            if (!Identity.IsValid
                || Identity.Kind != CoreDeclarationKind.AttributeSchema
                || !IsValidText(QualifiedName)
                || Role is not (CoreNativeAttributeRole.NativeImport
                    or CoreNativeAttributeRole.NativeCallable
                    or CoreNativeAttributeRole.NativeCallConv
                    or CoreNativeAttributeRole.NativeExport)
                || Arguments.IsDefault
                || !IsValidText(Placement)
                || Targets is null || Targets.Count == 0
                || Targets.Any(static target => target is null
                    || !IsValidText(target))
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

    internal static bool AreEquivalent(CoreNativeAttributeSchema left,
        CoreNativeAttributeSchema right)
    {
        if (left.Identity != right.Identity
            || left.QualifiedName != right.QualifiedName
            || left.Role != right.Role
            || left.Arguments.IsDefault || right.Arguments.IsDefault
            || left.Arguments.Length != right.Arguments.Length
            || !string.Equals(left.Placement, right.Placement,
                StringComparison.Ordinal)
            || left.Targets is null || right.Targets is null
            || !left.Targets.SetEquals(right.Targets)
            || left.CallingConventionEnum != right.CallingConventionEnum
            || left.ResolutionModeEnum != right.ResolutionModeEnum
            || left.ResolutionPolicyEnum != right.ResolutionPolicyEnum
            || left.GenericArity != right.GenericArity
            || left.IsPublic != right.IsPublic
            || left.IsPrimary != right.IsPrimary
            || left.IsRepeatable != right.IsRepeatable)
        {
            return false;
        }

        for (var index = 0; index < left.Arguments.Length; index++)
        {
            if (!left.Arguments[index].IsEquivalent(right.Arguments[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidText(string value)
        => !string.IsNullOrWhiteSpace(value)
            && !value.Contains('\0', StringComparison.Ordinal);
}

/// <summary>A complete selected top-level declaration captured from one provider export map.</summary>
/// <param name="Identity">The exact exported declaration identity.</param>
/// <param name="EnumDefinition">The complete enum declaration, for an enum-type export.</param>
/// <param name="NativeAttributeSchema">The complete native attribute schema, when selected.</param>
/// <param name="LayoutAttributeSchema">The complete layout attribute schema, when selected.</param>
/// <param name="StaticInitializerAttributeSchema">The complete static-initializer attribute schema, when selected.</param>
/// <param name="LazyInitializationAttributeSchema">The complete LazyInitialization attribute schema, when selected.</param>
public sealed record CoreProviderDeclarationSnapshot(
    CoreDeclarationIdentity Identity,
    CoreEnumDefinition? EnumDefinition,
    CoreNativeAttributeSchema? NativeAttributeSchema,
    CoreLayoutAttributeSchema? LayoutAttributeSchema,
    CoreStaticInitializerAttributeSchema? StaticInitializerAttributeSchema = null,
    CoreLazyInitializationAttributeSchema? LazyInitializationAttributeSchema = null)
{
    /// <summary>Creates a snapshot for a complete enum declaration.</summary>
    public static CoreProviderDeclarationSnapshot From(CoreEnumDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new(definition.Identity, definition, null, null);
    }

    /// <summary>Creates a snapshot for a complete native attribute schema.</summary>
    public static CoreProviderDeclarationSnapshot From(
        CoreNativeAttributeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new(schema.Identity, null, schema, null);
    }

    /// <summary>Creates a snapshot for a complete layout attribute schema.</summary>
    public static CoreProviderDeclarationSnapshot From(
        CoreLayoutAttributeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new(schema.Identity, null, null, schema);
    }

    /// <summary>Creates a snapshot for the complete StaticInitializer schema.</summary>
    public static CoreProviderDeclarationSnapshot From(
        CoreStaticInitializerAttributeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new(schema.Identity, null, null, null, schema);
    }

    /// <summary>Creates a snapshot for the complete LazyInitialization schema.</summary>
    public static CoreProviderDeclarationSnapshot From(
        CoreLazyInitializationAttributeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new(schema.Identity, null, null, null, null, schema);
    }

    internal bool IsValid
    {
        get
        {
            var hasEnum = EnumDefinition is not null;
            var hasNativeSchema = NativeAttributeSchema is not null;
            var hasLayoutSchema = LayoutAttributeSchema is not null;
            var hasStaticInitializerSchema
                = StaticInitializerAttributeSchema is not null;
            var hasLazyInitializationSchema
                = LazyInitializationAttributeSchema is not null;
            if (!Identity.IsValid
                || (hasEnum ? 1 : 0) + (hasNativeSchema ? 1 : 0)
                    + (hasLayoutSchema ? 1 : 0)
                    + (hasStaticInitializerSchema ? 1 : 0)
                    + (hasLazyInitializationSchema ? 1 : 0) != 1)
            {
                return false;
            }

            return hasEnum
                ? Identity.Kind == CoreDeclarationKind.EnumType
                    && EnumDefinition!.Identity == Identity
                : hasNativeSchema
                    ? Identity.Kind == CoreDeclarationKind.AttributeSchema
                        && NativeAttributeSchema!.Identity == Identity
                        && NativeAttributeSchema.IsStructurallyValid
                    : hasLayoutSchema
                        ? Identity.Kind == CoreDeclarationKind.AttributeSchema
                            && LayoutAttributeSchema!.Identity == Identity
                        : hasStaticInitializerSchema
                            ? Identity.Kind == CoreDeclarationKind.AttributeSchema
                                && StaticInitializerAttributeSchema!.Identity
                                    == Identity
                                && StaticInitializerAttributeSchema.IsStructurallyValid
                            : Identity.Kind == CoreDeclarationKind.AttributeSchema
                            && LazyInitializationAttributeSchema!.Identity
                                == Identity
                            && LazyInitializationAttributeSchema.IsStructurallyValid;
        }
    }
}

/// <summary>
/// Immutable host-selected Core declarations for one using Module.
/// </summary>
/// <remarks>
/// The embedding host supplies identities from its authorized provider adapter.
/// This descriptor is not serialized and does not authenticate a provider.
/// </remarks>
public sealed class NativeCoreRegistrationSet
{
    private NativeCoreRegistrationSet(CoreProviderModuleBindings moduleBindings,
        CoreEnumDefinition callingConventions,
        CoreEnumDefinition resolutionModes,
        CoreEnumDefinition resolutionPolicies,
        CoreNativeAttributeSchema nativeImport,
        CoreNativeAttributeSchema nativeCallable,
        CoreNativeAttributeSchema nativeCallConv,
        CoreNativeAttributeSchema? nativeExport)
    {
        ModuleBindings = moduleBindings;
        CallingConventions = callingConventions;
        ResolutionModes = resolutionModes;
        ResolutionPolicies = resolutionPolicies;
        NativeImport = nativeImport;
        NativeCallable = nativeCallable;
        NativeCallConv = nativeCallConv;
        NativeExport = nativeExport;
    }

    /// <summary>The using logical Module identity for this registration.</summary>
    public string UsingModuleIdentity => ModuleBindings.UsingModuleIdentity;

    /// <summary>The immutable selected provider Module context for this registration.</summary>
    public CoreProviderModuleBindings ModuleBindings { get; }

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

    /// <summary>The selected native-export schema, when that source role is admitted.</summary>
    public CoreNativeAttributeSchema? NativeExport { get; }

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
    public static NativeCoreRegistrationSet Create(CoreProviderModuleBindings moduleBindings,
        CoreEnumDefinition callingConventions,
        CoreEnumDefinition resolutionModes,
        CoreEnumDefinition resolutionPolicies,
        CoreNativeAttributeSchema nativeImport,
        CoreNativeAttributeSchema nativeCallable,
        CoreNativeAttributeSchema nativeCallConv,
        CoreNativeAttributeSchema? nativeExport)
    {
        ArgumentNullException.ThrowIfNull(moduleBindings);
        var registration = new NativeCoreRegistrationSet(moduleBindings,
            callingConventions, resolutionModes, resolutionPolicies, nativeImport,
            nativeCallable, nativeCallConv, nativeExport);
        if (registration.GetInvalidReason() is string reason)
        {
            throw new ArgumentException($"The selected Core native registration is invalid: {reason}.",
                nameof(moduleBindings));
        }

        return registration;
    }

    /// <summary>Whether the complete registration has the exact selected Core shape.</summary>
    public bool IsValid() => GetInvalidReason() is null;

    internal string? GetInvalidReason()
    {
        if (!ModuleBindings.IsValid())
        {
            return "the provider Module binding context is invalid";
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
        if (NativeExport is CoreNativeAttributeSchema nativeExport
            && !IsSchema(nativeExport, "Terrum.Interop.NativeExport",
                CoreNativeAttributeRole.NativeExport))
        {
            return "the NativeExport schema identity or owner is invalid";
        }
        if (EnumDefinitions.Any(definition =>
                    !ModuleBindings.ContainsProvider(definition.Identity.ProviderIdentity)
                    || !ModuleBindings.ContainsDeclaration(definition)
                    || definition.Members.Any(member =>
                        !ModuleBindings.ContainsProvider(member.Identity.ProviderIdentity)))
                || !ModuleBindings.ContainsProvider(NativeImport.Identity.ProviderIdentity)
                || !ModuleBindings.ContainsDeclaration(NativeImport)
                || !ModuleBindings.ContainsProvider(NativeCallable.Identity.ProviderIdentity)
                || !ModuleBindings.ContainsDeclaration(NativeCallable)
                || !ModuleBindings.ContainsProvider(NativeCallConv.Identity.ProviderIdentity)
                || !ModuleBindings.ContainsDeclaration(NativeCallConv)
                || (NativeExport is CoreNativeAttributeSchema selectedNativeExport
                    && (!ModuleBindings.ContainsProvider(
                            selectedNativeExport.Identity.ProviderIdentity)
                        || !ModuleBindings.ContainsDeclaration(
                            selectedNativeExport))))
        {
            return "a registered declaration has no matching selected provider export snapshot";
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
            || (NativeExport is CoreNativeAttributeSchema exportArguments
                && (exportArguments.CallingConventionEnum is not null
                    || exportArguments.ResolutionModeEnum is not null
                    || exportArguments.ResolutionPolicyEnum is not null)))
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
        };
        if (NativeExport is CoreNativeAttributeSchema selectedExport)
        {
            identities.Add(selectedExport.Identity);
        }
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
            || definition.BlittableTargetRequirement is not (0
                or FixedLayoutStorage.WindowsX64TargetRequirement)
            || definition.Members.IsDefault
            || definition.Members.Length != members.Length)
        {
            return false;
        }

        var seenIdentities = new HashSet<CoreDeclarationIdentity>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var seenDeclarationKeys = new HashSet<string>(StringComparer.Ordinal);
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
                || string.IsNullOrWhiteSpace(member.DeclarationKey)
                || member.DeclarationKey.Contains('\0', StringComparison.Ordinal)
                || !seenIdentities.Add(member.Identity)
                || !seenNames.Add(member.Name)
                || !seenDeclarationKeys.Add(member.DeclarationKey))
            {
                return false;
            }
        }

        return seenIdentities.Count == definition.Members.Length
            && definition.Members.All(member => member is not null
                && member.DeclaringEnum == definition.Identity);
    }

    private bool IsSchema(CoreNativeAttributeSchema schema,
        string qualifiedName, CoreNativeAttributeRole role)
    {
        if (schema is null || !schema.IsStructurallyValid
            || schema.Identity.Kind != CoreDeclarationKind.AttributeSchema
            || schema.QualifiedName != qualifiedName
            || schema.Role != role
            || schema.GenericArity != 0
            || !schema.IsPublic || !schema.IsPrimary || schema.IsRepeatable)
        {
            return false;
        }

        var conventionMember = FindMember(CallingConventions,
            "Unmanaged").Identity;
        var (placement, targets, arguments) = role switch {
            CoreNativeAttributeRole.NativeImport =>
                (Placement: "primary-declaration",
                    Targets: Set("static-extern-callable",
                        "static-extern-data"),
                    Arguments: ExpectedNativeImportArguments(conventionMember)),
            CoreNativeAttributeRole.NativeCallable =>
                (Placement: "primary-declaration",
                    Targets: Set("ordinary-method", "interface"),
                    Arguments: ExpectedNativeCallableArguments(
                        conventionMember)),
            CoreNativeAttributeRole.NativeCallConv =>
                (Placement: "immediate-fnptr-type-use",
                    Targets: Set("fnptr-type-use"),
                    Arguments: ExpectedNativeCallConvArguments()),
            CoreNativeAttributeRole.NativeExport =>
                (Placement: "primary-declaration",
                    Targets: Set("static-defined-callable",
                        "static-defined-data"),
                    Arguments: ExpectedNativeExportArguments()),
            _ => default,
        };
        return schema.Placement == placement
            && schema.Targets.SetEquals(targets)
            && AreEquivalent(schema.Arguments, arguments);
    }

    private ImmutableArray<CoreNativeAttributeArgument>
        ExpectedNativeImportArguments(CoreDeclarationIdentity conventionMember)
    {
        var all = Set("callable-link", "callable-dynamic", "data-link",
            "data-dynamic");
        return [
            new("LibraryName",
                CoreNativeAttributeArgumentKind.LibraryNameLiteral, null,
                true, true, true, all, null),
            new("EntryName",
                CoreNativeAttributeArgumentKind.EntryNameLiteral, null,
                false, true, true, all,
                new CoreNativeAttributeDefault(
                    CoreNativeAttributeDefaultKind.DeclarationName, null)),
            new("ResolutionMode", CoreNativeAttributeArgumentKind.Enum,
                ResolutionModes.Identity, false, false, true, all,
                EnumMemberDefault(FindMember(ResolutionModes, "Link").Identity)),
            new("ResolutionPolicy", CoreNativeAttributeArgumentKind.Enum,
                ResolutionPolicies.Identity, false, false, true,
                Set("callable-dynamic", "data-dynamic"),
                EnumMemberDefault(FindMember(ResolutionPolicies,
                    "Default").Identity)),
            new("CallConv", CoreNativeAttributeArgumentKind.Enum,
                CallingConventions.Identity, false, false, true,
                Set("callable-link", "callable-dynamic"),
                EnumMemberDefault(conventionMember)),
        ];
    }

    private ImmutableArray<CoreNativeAttributeArgument>
        ExpectedNativeCallableArguments(
            CoreDeclarationIdentity conventionMember)
        =>
        [
            new("CallConv", CoreNativeAttributeArgumentKind.Enum,
                CallingConventions.Identity, false, false, true,
                Set("method", "interface"),
                EnumMemberDefault(conventionMember)),
        ];

    private ImmutableArray<CoreNativeAttributeArgument>
        ExpectedNativeCallConvArguments()
        =>
        [
            new(null, CoreNativeAttributeArgumentKind.Enum,
                CallingConventions.Identity, true, true, false,
                Set("fnptr-type-use"), null),
        ];

    private static ImmutableArray<CoreNativeAttributeArgument>
        ExpectedNativeExportArguments()
        =>
        [
            new("EntryName",
                CoreNativeAttributeArgumentKind.EntryNameLiteral, null,
                false, true, true, Set("method", "data"),
                new CoreNativeAttributeDefault(
                    CoreNativeAttributeDefaultKind.DeclarationName, null)),
        ];

    private static CoreNativeAttributeDefault EnumMemberDefault(
        CoreDeclarationIdentity member)
        => new(CoreNativeAttributeDefaultKind.EnumMember, member);

    private static CoreEnumMemberDefinition FindMember(
        CoreEnumDefinition definition, string name)
        => definition.Members.Single(member => member.Name == name);

    private static bool AreEquivalent(
        ImmutableArray<CoreNativeAttributeArgument> left,
        ImmutableArray<CoreNativeAttributeArgument> right)
    {
        if (left.IsDefault || right.IsDefault || left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!left[index].IsEquivalent(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableHashSet<string> Set(params string[] values)
        => values.ToImmutableHashSet(StringComparer.Ordinal);
}
