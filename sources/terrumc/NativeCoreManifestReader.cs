// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.Tools;

internal sealed record NativeCoreManifestDefinition(
    string CoreAuthorityIdentity,
    string UsingModuleIdentity,
    ImmutableArray<CoreProviderModuleOrigin> Origins,
    CoreEnumDefinition CallingConventions,
    CoreEnumDefinition ResolutionModes,
    CoreEnumDefinition ResolutionPolicies,
    CoreNativeAttributeSchema NativeImport,
    CoreNativeAttributeSchema NativeCallable,
    CoreNativeAttributeSchema NativeCallConv)
{
    internal ImmutableArray<CoreProviderDeclarationSnapshot> Declarations =>
    [
        CoreProviderDeclarationSnapshot.From(CallingConventions),
        CoreProviderDeclarationSnapshot.From(ResolutionModes),
        CoreProviderDeclarationSnapshot.From(ResolutionPolicies),
        CoreProviderDeclarationSnapshot.From(NativeImport),
        CoreProviderDeclarationSnapshot.From(NativeCallable),
        CoreProviderDeclarationSnapshot.From(NativeCallConv),
    ];

    internal NativeCoreRegistrationSet CreateRegistration(
        CoreProviderModuleBindings bindings)
        => NativeCoreRegistrationSet.Create(bindings, CallingConventions,
            ResolutionModes, ResolutionPolicies, NativeImport,
            NativeCallable, NativeCallConv, nativeExport: null);
}

internal static class NativeCoreManifestReader
{
    private const string SchemaIdentity = "terrumc.native-core-manifest/1";
    private const string CallingConventionsName
        = "Terrum.Interop.NativeCallingConvention";
    private const string ResolutionModesName
        = "Terrum.Interop.NativeImportResolutionMode";
    private const string ResolutionPoliciesName
        = "Terrum.Interop.NativeImportResolutionPolicy";
    private const string NativeImportName = "Terrum.Interop.NativeImport";
    private const string NativeCallableName = "Terrum.Interop.NativeCallable";
    private const string NativeCallConvName = "Terrum.Interop.NativeCallConv";

    private static readonly byte[] s_utf8Signature = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    private readonly record struct ParsedDefault(
        string Kind, CoreDeclarationIdentity? Member);

    private readonly record struct ParsedArgument(
        string? Name, string Kind, CoreDeclarationIdentity? EnumType,
        bool IsRequired, bool AllowsPositional, bool AllowsNamed,
        ImmutableHashSet<string> Contexts, ParsedDefault? Default);

    private sealed record ParsedSchema(
        CoreNativeAttributeSchema Schema, string Placement,
        ImmutableHashSet<string> Targets,
        ImmutableArray<ParsedArgument> Arguments);

    internal static NativeCoreManifestDefinition ReadDefinition(string path)
    {
        var contents = File.ReadAllBytes(path);
        var json = contents.AsMemory();
        if (json.Span.StartsWith(s_utf8Signature))
        {
            json = json[3..];
            if (json.Span.StartsWith(s_utf8Signature))
            {
                throw Invalid(path, "$",
                    "only one leading UTF-8 signature is permitted");
            }
        }

        try
        {
            string jsonText;
            try
            {
                jsonText = s_strictUtf8.GetString(json.Span);
            }
            catch (DecoderFallbackException)
            {
                throw Invalid(path, "$", "manifest is not valid UTF-8");
            }

            using var document = JsonDocument.Parse(jsonText,
                new JsonDocumentOptions {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 128,
                });
            ValidateJsonText(document.RootElement, path, "$");
            var root = RequireObject(document.RootElement, path, "$",
                "schema", "coreAuthorityIdentity", "usingModuleIdentity",
                "providers", "callingConventions", "resolutionModes",
                "resolutionPolicies", "nativeImport", "nativeCallable",
                "nativeCallConv");
            if (ReadText(Property(root, "schema", path, "$"), path,
                    "$.schema") != SchemaIdentity)
            {
                throw Invalid(path, "$.schema",
                    $"expected exactly '{SchemaIdentity}'");
            }

            var authorityIdentity = ReadText(Property(root,
                "coreAuthorityIdentity", path, "$"), path,
                "$.coreAuthorityIdentity");
            var usingModuleIdentity = ReadText(Property(root,
                "usingModuleIdentity", path, "$"), path,
                "$.usingModuleIdentity");
            var origins = ReadOrigins(Property(root, "providers", path, "$"),
                path);
            var callingConventions = ReadEnum(Property(root,
                "callingConventions", path, "$"), path,
                "$.callingConventions", CallingConventionsName,
                [("Unmanaged", 1), ("Cdecl", 2), ("Stdcall", 3),
                    ("Vectorcall", 4)]);
            var resolutionModes = ReadEnum(Property(root,
                "resolutionModes", path, "$"), path,
                "$.resolutionModes", ResolutionModesName,
                [("Link", 1), ("Dynamic", 2)]);
            var resolutionPolicies = ReadEnum(Property(root,
                "resolutionPolicies", path, "$"), path,
                "$.resolutionPolicies", ResolutionPoliciesName,
                [("Default", 1), ("CompatibilitySearch", 2)]);
            var nativeImport = ReadSchema(Property(root, "nativeImport",
                path, "$"), path, "$.nativeImport", NativeImportName,
                "native-import", "primary-declaration",
                ["static-extern-callable", "static-extern-data"],
                ReadNativeImportArguments(callingConventions, resolutionModes,
                    resolutionPolicies), callingConventions, resolutionModes,
                resolutionPolicies);
            var nativeCallable = ReadSchema(Property(root, "nativeCallable",
                path, "$"), path, "$.nativeCallable", NativeCallableName,
                "native-callable", "primary-declaration",
                ["ordinary-method", "interface"],
                ReadNativeCallableArguments(callingConventions),
                callingConventions, resolutionModes, resolutionPolicies);
            var nativeCallConv = ReadSchema(Property(root, "nativeCallConv",
                path, "$"), path, "$.nativeCallConv", NativeCallConvName,
                "native-callconv", "immediate-fnptr-type-use",
                ["fnptr-type-use"],
                ReadNativeCallConvArguments(callingConventions),
                callingConventions, resolutionModes, resolutionPolicies);

            ValidateUsedProviders(origins, callingConventions,
                resolutionModes, resolutionPolicies, nativeImport.Schema,
                nativeCallable.Schema, nativeCallConv.Schema, path);
            return new NativeCoreManifestDefinition(authorityIdentity,
                usingModuleIdentity, origins, callingConventions,
                resolutionModes, resolutionPolicies, nativeImport.Schema,
                nativeCallable.Schema, nativeCallConv.Schema);
        }
        catch (JsonException exception)
        {
            throw Invalid(path, exception.Path ?? "$", exception.Message);
        }
        catch (ArgumentException exception)
        {
            throw Invalid(path, "$", exception.Message);
        }
        catch (OverflowException exception)
        {
            throw Invalid(path, "$", exception.Message);
        }
    }

    private static CoreEnumDefinition ReadEnum(JsonElement element,
        string manifestPath, string path, string expectedName,
        (string Name, int Value)[] expectedMembers)
    {
        var value = RequireObject(element, manifestPath, path, "identity",
            "qualifiedName", "underlyingType", "members", "genericArity",
            "isPublic", "isPrimary", "isFlags",
            "blittableTargetRequirement");
        var identity = ReadIdentity(Property(value, "identity", manifestPath,
            path), manifestPath, path + ".identity");
        if (identity.Kind != CoreDeclarationKind.EnumType)
        {
            throw Invalid(manifestPath, path + ".identity.kind",
                "expected an enum-type identity");
        }
        var qualifiedName = ReadQualifiedName(Property(value, "qualifiedName",
            manifestPath, path), manifestPath, path + ".qualifiedName");
        if (qualifiedName != expectedName)
        {
            throw Invalid(manifestPath, path + ".qualifiedName",
                $"expected exactly '{expectedName}'");
        }
        if (ReadText(Property(value, "underlyingType", manifestPath, path),
                manifestPath, path + ".underlyingType") != "int32")
        {
            throw Invalid(manifestPath, path + ".underlyingType",
                "expected int32");
        }

        var membersElement = Property(value, "members", manifestPath, path);
        if (membersElement.ValueKind != JsonValueKind.Array
            || membersElement.GetArrayLength() != expectedMembers.Length)
        {
            throw Invalid(manifestPath, path + ".members",
                $"expected exactly {expectedMembers.Length} members");
        }

        var members = ImmutableArray.CreateBuilder<CoreEnumMemberDefinition>(
            expectedMembers.Length);
        var identities = new HashSet<CoreDeclarationIdentity>();
        var declarationKeys = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var values = new HashSet<int>();
        var ordinal = 0;
        foreach (var item in membersElement.EnumerateArray())
        {
            var memberPath = $"{path}.members[{ordinal}]";
            var member = RequireObject(item, manifestPath, memberPath,
                "identity", "declaringEnum", "declarationKey", "name",
                "value", "isPublic");
            var memberIdentity = ReadIdentity(Property(member, "identity",
                manifestPath, memberPath), manifestPath,
                memberPath + ".identity");
            var declaringEnum = ReadIdentity(Property(member, "declaringEnum",
                manifestPath, memberPath), manifestPath,
                memberPath + ".declaringEnum");
            var name = ReadIdentifier(Property(member, "name", manifestPath,
                memberPath), manifestPath, memberPath + ".name");
            var memberValue = ReadInt32(Property(member, "value",
                manifestPath, memberPath), manifestPath,
                memberPath + ".value");
            var declarationKey = ReadText(Property(member,
                "declarationKey", manifestPath, memberPath), manifestPath,
                memberPath + ".declarationKey");
            if (memberIdentity.Kind != CoreDeclarationKind.EnumMember
                || memberIdentity.ProviderIdentity != identity.ProviderIdentity
                || declaringEnum != identity
                || !identities.Add(memberIdentity)
                || !declarationKeys.Add(declarationKey)
                || !names.Add(name) || !values.Add(memberValue)
                || ReadBoolean(Property(member, "isPublic", manifestPath,
                    memberPath), manifestPath, memberPath + ".isPublic")
                    is false)
            {
                throw Invalid(manifestPath, memberPath,
                    "member identity, owner, key, values and visibility must be exact and unique");
            }
            if (!expectedMembers.Contains((name, memberValue)))
            {
                throw Invalid(manifestPath, memberPath,
                    "member name/value is outside the selected enum domain");
            }

            members.Add(new CoreEnumMemberDefinition(memberIdentity,
                declaringEnum, name, memberValue, IsPublic: true) {
                DeclarationKey = declarationKey,
            });
            ordinal++;
        }

        var genericArity = ReadUInt32(Property(value, "genericArity",
            manifestPath, path), manifestPath, path + ".genericArity");
        var blittableTargetRequirement = ReadUInt32(Property(value,
            "blittableTargetRequirement", manifestPath, path),
            manifestPath, path + ".blittableTargetRequirement");
        if (genericArity > int.MaxValue
            || blittableTargetRequirement > int.MaxValue)
        {
            throw Invalid(manifestPath, path,
                "enum arity or representation requirement exceeds the supported range");
        }

        return new CoreEnumDefinition(identity, qualifiedName,
            PrimitiveType.Int32, members.MoveToImmutable(),
            (int)genericArity,
            ReadBoolean(Property(value, "isPublic", manifestPath, path),
                manifestPath, path + ".isPublic"),
            ReadBoolean(Property(value, "isPrimary", manifestPath, path),
                manifestPath, path + ".isPrimary"),
            ReadBoolean(Property(value, "isFlags", manifestPath, path),
                manifestPath, path + ".isFlags"))
        {
            BlittableTargetRequirement = (int)blittableTargetRequirement,
        };
    }

    private static ParsedSchema ReadSchema(JsonElement element,
        string manifestPath, string path, string expectedQualifiedName,
        string expectedRole, string expectedPlacement,
        string[] expectedTargets, ParsedArgument[] expectedArguments,
        CoreEnumDefinition callingConventions,
        CoreEnumDefinition resolutionModes,
        CoreEnumDefinition resolutionPolicies)
    {
        var value = RequireObject(element, manifestPath, path, "identity",
            "qualifiedName", "role", "arguments", "placement", "targets",
            "genericArity", "isPublic", "isPrimary", "isRepeatable");
        var identity = ReadIdentity(Property(value, "identity", manifestPath,
            path), manifestPath, path + ".identity");
        if (identity.Kind != CoreDeclarationKind.AttributeSchema)
        {
            throw Invalid(manifestPath, path + ".identity.kind",
                "expected an attribute-schema identity");
        }
        var qualifiedName = ReadQualifiedName(Property(value, "qualifiedName",
            manifestPath, path), manifestPath, path + ".qualifiedName");
        var role = ReadText(Property(value, "role", manifestPath, path),
            manifestPath, path + ".role");
        var placement = ReadText(Property(value, "placement", manifestPath,
            path), manifestPath, path + ".placement");
        if (qualifiedName != expectedQualifiedName || role != expectedRole
            || placement != expectedPlacement)
        {
            throw Invalid(manifestPath, path,
                "schema qualified name, role and placement must match the selected source contract");
        }

        var targets = ReadStringSet(Property(value, "targets", manifestPath,
            path), manifestPath, path + ".targets");
        if (!targets.SetEquals(expectedTargets))
        {
            throw Invalid(manifestPath, path + ".targets",
                "target set differs from the selected source contract");
        }

        var argumentsElement = Property(value, "arguments", manifestPath,
            path);
        if (argumentsElement.ValueKind != JsonValueKind.Array
            || argumentsElement.GetArrayLength() != expectedArguments.Length)
        {
            throw Invalid(manifestPath, path + ".arguments",
                "argument records are incomplete or have extra records");
        }
        var arguments = ImmutableArray.CreateBuilder<ParsedArgument>(
            expectedArguments.Length);
        var ordinal = 0;
        foreach (var argumentElement in argumentsElement.EnumerateArray())
        {
            var argumentPath = $"{path}.arguments[{ordinal}]";
            var argument = ReadArgument(argumentElement, manifestPath,
                argumentPath);
            if (!Matches(argument, expectedArguments[ordinal]))
            {
                throw Invalid(manifestPath, argumentPath,
                    "argument record differs from the complete selected source schema");
            }
            arguments.Add(argument);
            ordinal++;
        }

        var genericArity = ReadUInt32(Property(value, "genericArity",
            manifestPath, path), manifestPath, path + ".genericArity");
        if (genericArity > int.MaxValue)
        {
            throw Invalid(manifestPath, path + ".genericArity",
                "value exceeds the supported range");
        }
        var isPublic = ReadBoolean(Property(value, "isPublic", manifestPath,
            path), manifestPath, path + ".isPublic");
        var isPrimary = ReadBoolean(Property(value, "isPrimary", manifestPath,
            path), manifestPath, path + ".isPrimary");
        var isRepeatable = ReadBoolean(Property(value, "isRepeatable",
            manifestPath, path), manifestPath, path + ".isRepeatable");
        if (!isPublic || !isPrimary || isRepeatable || genericArity != 0)
        {
            throw Invalid(manifestPath, path,
                "schema visibility, primary status, repeatability or generic arity is invalid");
        }

        var roleValue = expectedRole switch {
            "native-import" => CoreNativeAttributeRole.NativeImport,
            "native-callable" => CoreNativeAttributeRole.NativeCallable,
            "native-callconv" => CoreNativeAttributeRole.NativeCallConv,
            _ => throw new InvalidOperationException(),
        };
        var conventionIdentity = callingConventions.Identity;
        var resolutionModeIdentity = expectedRole == "native-import"
            ? resolutionModes.Identity : (CoreDeclarationIdentity?)null;
        var resolutionPolicyIdentity = expectedRole == "native-import"
            ? resolutionPolicies.Identity : (CoreDeclarationIdentity?)null;
        var parsedArguments = arguments.MoveToImmutable();
        var schemaArguments = ImmutableArray.CreateBuilder<
            CoreNativeAttributeArgument>(parsedArguments.Length);
        foreach (var argument in parsedArguments)
        {
            schemaArguments.Add(ToCoreArgument(argument));
        }
        return new ParsedSchema(new CoreNativeAttributeSchema(identity,
            qualifiedName, roleValue, schemaArguments.MoveToImmutable(),
            placement, targets, conventionIdentity,
            resolutionModeIdentity, resolutionPolicyIdentity,
            (int)genericArity, isPublic, isPrimary, isRepeatable),
            placement, targets, parsedArguments);
    }

    private static bool Matches(ParsedArgument actual,
        ParsedArgument expected)
        => actual.Name == expected.Name
            && actual.Kind == expected.Kind
            && actual.EnumType == expected.EnumType
            && actual.IsRequired == expected.IsRequired
            && actual.AllowsPositional == expected.AllowsPositional
            && actual.AllowsNamed == expected.AllowsNamed
            && actual.Contexts.SetEquals(expected.Contexts)
            && actual.Default == expected.Default;

    private static CoreNativeAttributeArgument ToCoreArgument(
        ParsedArgument argument)
    {
        var kind = argument.Kind switch {
            "library-name-literal"
                => CoreNativeAttributeArgumentKind.LibraryNameLiteral,
            "entry-name-literal"
                => CoreNativeAttributeArgumentKind.EntryNameLiteral,
            "enum" => CoreNativeAttributeArgumentKind.Enum,
            _ => throw new InvalidOperationException(),
        };
        CoreNativeAttributeDefault? defaultValue = argument.Default switch {
            ParsedDefault { Kind: "declaration-name", Member: null }
                => new CoreNativeAttributeDefault(
                    CoreNativeAttributeDefaultKind.DeclarationName, null),
            ParsedDefault { Kind: "enum-member", Member: CoreDeclarationIdentity member }
                => new CoreNativeAttributeDefault(
                    CoreNativeAttributeDefaultKind.EnumMember, member),
            null => null,
            _ => throw new InvalidOperationException(),
        };
        return new CoreNativeAttributeArgument(argument.Name, kind,
            argument.EnumType, argument.IsRequired, argument.AllowsPositional,
            argument.AllowsNamed, argument.Contexts, defaultValue);
    }

    private static ParsedArgument[] ReadNativeImportArguments(
        CoreEnumDefinition conventions, CoreEnumDefinition modes,
        CoreEnumDefinition policies)
    {
        var unmanaged = FindMember(conventions, "Unmanaged").Identity;
        var link = FindMember(modes, "Link").Identity;
        var defaultPolicy = FindMember(policies, "Default").Identity;
        return [
            new("LibraryName", "library-name-literal", null, true, true,
                true, Set("callable-link", "callable-dynamic", "data-link",
                    "data-dynamic"), null),
            new("EntryName", "entry-name-literal", null, false, true,
                true, Set("callable-link", "callable-dynamic", "data-link",
                    "data-dynamic"), new ParsedDefault("declaration-name", null)),
            new("ResolutionMode", "enum", modes.Identity, false, false,
                true, Set("callable-link", "callable-dynamic", "data-link",
                    "data-dynamic"), new ParsedDefault("enum-member", link)),
            new("ResolutionPolicy", "enum", policies.Identity, false, false,
                true, Set("callable-dynamic", "data-dynamic"),
                new ParsedDefault("enum-member", defaultPolicy)),
            new("CallConv", "enum", conventions.Identity, false, false,
                true, Set("callable-link", "callable-dynamic"),
                new ParsedDefault("enum-member", unmanaged)),
        ];
    }

    private static ParsedArgument[] ReadNativeCallableArguments(
        CoreEnumDefinition conventions)
        =>
        [
            new("CallConv", "enum", conventions.Identity, false, false,
                true, Set("method", "interface"),
                new ParsedDefault("enum-member",
                    FindMember(conventions, "Unmanaged").Identity)),
        ];

    private static ParsedArgument[] ReadNativeCallConvArguments(
        CoreEnumDefinition conventions)
        =>
        [
            new(null, "enum", conventions.Identity, true, true, false,
                Set("fnptr-type-use"), null),
        ];

    private static ParsedArgument ReadArgument(JsonElement element,
        string manifestPath, string path)
    {
        var value = RequireObject(element, manifestPath, path, "name", "kind",
            "enumType", "isRequired", "allowsPositional", "allowsNamed",
            "contexts", "default");
        var nameElement = Property(value, "name", manifestPath, path);
        var name = nameElement.ValueKind == JsonValueKind.Null
            ? null : ReadText(nameElement, manifestPath, path + ".name");
        var kind = ReadText(Property(value, "kind", manifestPath, path),
            manifestPath, path + ".kind");
        if (kind is not ("library-name-literal" or "entry-name-literal"
            or "enum"))
        {
            throw Invalid(manifestPath, path + ".kind",
                "unknown native schema argument kind");
        }
        var enumType = ReadNullableIdentity(Property(value, "enumType",
            manifestPath, path), manifestPath, path + ".enumType");
        if (kind == "enum" != (enumType is not null))
        {
            throw Invalid(manifestPath, path + ".enumType",
                "enum identity is required exactly for enum arguments");
        }
        return new ParsedArgument(name, kind, enumType,
            ReadBoolean(Property(value, "isRequired", manifestPath, path),
                manifestPath, path + ".isRequired"),
            ReadBoolean(Property(value, "allowsPositional", manifestPath,
                path), manifestPath, path + ".allowsPositional"),
            ReadBoolean(Property(value, "allowsNamed", manifestPath, path),
                manifestPath, path + ".allowsNamed"),
            ReadStringSet(Property(value, "contexts", manifestPath, path),
                manifestPath, path + ".contexts"),
            ReadDefault(Property(value, "default", manifestPath, path),
                manifestPath, path + ".default"));
    }

    private static ParsedDefault? ReadDefault(JsonElement element,
        string manifestPath, string path)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(manifestPath, path,
                "expected null or a complete default object");
        }
        if (!element.TryGetProperty("kind", out var kindElement))
        {
            throw Invalid(manifestPath, path + ".kind",
                "missing required property");
        }
        var kind = ReadText(kindElement, manifestPath, path + ".kind");
        if (kind == "declaration-name")
        {
            _ = RequireObject(element, manifestPath, path, "kind");
            return new ParsedDefault(kind, null);
        }
        if (kind == "enum-member")
        {
            var value = RequireObject(element, manifestPath, path,
                "kind", "member");
            return new ParsedDefault(kind, ReadIdentity(Property(value,
                "member", manifestPath, path), manifestPath,
                path + ".member"));
        }
        throw Invalid(manifestPath, path + ".kind",
            "unknown native schema default kind");
    }

    private static CoreEnumMemberDefinition FindMember(
        CoreEnumDefinition definition, string name)
        => definition.Members.Single(member => member.Name == name);

    private static ImmutableHashSet<string> Set(params string[] values)
        => values.ToImmutableHashSet(StringComparer.Ordinal);

    private static ImmutableArray<CoreProviderModuleOrigin> ReadOrigins(
        JsonElement element, string manifestPath)
    {
        if (element.ValueKind != JsonValueKind.Array
            || element.GetArrayLength() is < 1 or > 6)
        {
            throw Invalid(manifestPath, "$.providers",
                "expected an array containing one through six provider origins");
        }

        var origins = ImmutableArray.CreateBuilder<CoreProviderModuleOrigin>(
            element.GetArrayLength());
        var ordinal = 0;
        foreach (var item in element.EnumerateArray())
        {
            var path = $"$.providers[{ordinal}]";
            var origin = RequireObject(item, manifestPath, path,
                "moduleIdentity", "revisionIdentity", "schema",
                "governingSemanticEdition", "targetRequirement",
                "runtimePolicy");
            var schemaElement = RequireObject(Property(origin, "schema",
                manifestPath, path), manifestPath, path + ".schema",
                "identity", "revision");
            origins.Add(new CoreProviderModuleOrigin(
                ReadText(Property(origin, "moduleIdentity", manifestPath,
                    path), manifestPath, path + ".moduleIdentity"),
                ReadText(Property(origin, "revisionIdentity", manifestPath,
                    path), manifestPath, path + ".revisionIdentity"),
                new CoreProviderSchemaSelection(
                    ReadText(Property(schemaElement, "identity",
                        manifestPath, path + ".schema"),
                        manifestPath, path + ".schema.identity"),
                    ReadUInt32(Property(schemaElement, "revision",
                        manifestPath, path + ".schema"),
                        manifestPath, path + ".schema.revision")),
                ReadUInt32(Property(origin, "governingSemanticEdition",
                    manifestPath, path), manifestPath,
                    path + ".governingSemanticEdition"),
                ReadUInt32(Property(origin, "targetRequirement",
                    manifestPath, path), manifestPath,
                    path + ".targetRequirement"),
                ReadUInt32(Property(origin, "runtimePolicy", manifestPath,
                    path), manifestPath, path + ".runtimePolicy")));
            ordinal++;
        }
        return origins.MoveToImmutable();
    }

    private static CoreDeclarationIdentity ReadIdentity(JsonElement element,
        string manifestPath, string path)
    {
        var value = RequireObject(element, manifestPath, path,
            "providerIdentity", "exportKey", "kind");
        var kind = ReadText(Property(value, "kind", manifestPath, path),
            manifestPath, path + ".kind") switch {
            "enum-type" => CoreDeclarationKind.EnumType,
            "enum-member" => CoreDeclarationKind.EnumMember,
            "attribute-schema" => CoreDeclarationKind.AttributeSchema,
            _ => throw Invalid(manifestPath, path + ".kind",
                "unknown declaration kind"),
        };
        return new CoreDeclarationIdentity(
            ReadText(Property(value, "providerIdentity", manifestPath,
                path), manifestPath, path + ".providerIdentity"),
            ReadText(Property(value, "exportKey", manifestPath, path),
                manifestPath, path + ".exportKey"), kind);
    }

    private static CoreDeclarationIdentity? ReadNullableIdentity(
        JsonElement element, string manifestPath, string path)
        => element.ValueKind == JsonValueKind.Null
            ? null : ReadIdentity(element, manifestPath, path);

    private static ImmutableHashSet<string> ReadStringSet(
        JsonElement element, string manifestPath, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(manifestPath, path, "expected a string array");
        }
        var result = ImmutableHashSet.CreateBuilder<string>(
            StringComparer.Ordinal);
        var ordinal = 0;
        foreach (var item in element.EnumerateArray())
        {
            var value = ReadText(item, manifestPath, $"{path}[{ordinal}]");
            if (!result.Add(value))
            {
                throw Invalid(manifestPath, $"{path}[{ordinal}]",
                    "set contains a duplicate");
            }
            ordinal++;
        }
        return result.ToImmutable();
    }

    private static void ValidateUsedProviders(
        ImmutableArray<CoreProviderModuleOrigin> origins,
        CoreEnumDefinition callingConventions,
        CoreEnumDefinition resolutionModes,
        CoreEnumDefinition resolutionPolicies,
        CoreNativeAttributeSchema nativeImport,
        CoreNativeAttributeSchema nativeCallable,
        CoreNativeAttributeSchema nativeCallConv,
        string manifestPath)
    {
        var used = new HashSet<string>(StringComparer.Ordinal) {
            callingConventions.Identity.ProviderIdentity,
            resolutionModes.Identity.ProviderIdentity,
            resolutionPolicies.Identity.ProviderIdentity,
            nativeImport.Identity.ProviderIdentity,
            nativeCallable.Identity.ProviderIdentity,
            nativeCallConv.Identity.ProviderIdentity,
        };
        foreach (var enumDefinition in new[] {
            callingConventions, resolutionModes, resolutionPolicies,
        })
        {
            foreach (var member in enumDefinition.Members)
            {
                _ = used.Add(member.Identity.ProviderIdentity);
            }
        }
        if (used.Count != origins.Length
            || origins.Any(origin => !used.Remove(origin.ModuleIdentity))
            || used.Count != 0)
        {
            throw Invalid(manifestPath, "$.providers",
                "origins must name each used provider exactly once and contain no unused provider");
        }
    }

    private static JsonElement RequireObject(JsonElement element,
        string manifestPath, string path, params string[] expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(manifestPath, path, "expected an object");
        }
        var expected = new HashSet<string>(expectedProperties,
            StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Remove(property.Name))
            {
                throw Invalid(manifestPath, path + "." + property.Name,
                    "unknown property");
            }
        }
        if (expected.Count != 0)
        {
            throw Invalid(manifestPath, path,
                $"missing required property '{expected.First()}'");
        }
        return element;
    }

    private static JsonElement Property(JsonElement element, string name,
        string manifestPath, string path)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            throw Invalid(manifestPath, path + "." + name,
                "missing required property");
        }
        return property;
    }

    private static string ReadText(JsonElement element, string manifestPath,
        string path)
    {
        if (element.ValueKind != JsonValueKind.String
            || element.GetString() is not string value
            || string.IsNullOrWhiteSpace(value)
            || value.Contains('\0', StringComparison.Ordinal))
        {
            throw Invalid(manifestPath, path,
                "expected nonempty text without NUL");
        }
        return value;
    }

    private static string ReadQualifiedName(JsonElement element,
        string manifestPath, string path)
    {
        var value = ReadText(element, manifestPath, path);
        var tokens = Lexer.Lex(SourceText.From(value));
        if (!tokens.Diagnostics.IsEmpty)
        {
            throw Invalid(manifestPath, path,
                "name is not a valid source-qualified name");
        }
        var parts = new List<string>();
        var expectsIdentifier = true;
        for (var index = 0; index < tokens.Tokens.Length - 1; index++)
        {
            var token = tokens.Tokens[index];
            if (!token.LeadingTrivia.IsDefaultOrEmpty
                || (expectsIdentifier
                    ? token.Kind != TokenKind.Identifier
                    : token.Kind != TokenKind.Dot))
            {
                throw Invalid(manifestPath, path,
                    "name is not a valid source-qualified name");
            }
            if (expectsIdentifier)
            {
                parts.Add(token.Name);
            }
            expectsIdentifier = !expectsIdentifier;
        }
        var endOfFile = tokens.Tokens[^1];
        if (parts.Count == 0 || expectsIdentifier
            || endOfFile.Kind != TokenKind.EndOfFile
            || !endOfFile.LeadingTrivia.IsDefaultOrEmpty)
        {
            throw Invalid(manifestPath, path,
                "name is not a valid source-qualified name");
        }
        return value;
    }

    private static string ReadIdentifier(JsonElement element,
        string manifestPath, string path)
    {
        var value = ReadText(element, manifestPath, path);
        var tokens = Lexer.Lex(SourceText.From(value));
        if (!tokens.Diagnostics.IsEmpty || tokens.Tokens.Length != 2
            || tokens.Tokens[0].Kind != TokenKind.Identifier
            || !tokens.Tokens[0].LeadingTrivia.IsDefaultOrEmpty
            || tokens.Tokens[1].Kind != TokenKind.EndOfFile
            || !tokens.Tokens[1].LeadingTrivia.IsDefaultOrEmpty)
        {
            throw Invalid(manifestPath, path,
                "name is not a valid source identifier");
        }
        return value;
    }

    private static uint ReadUInt32(JsonElement element, string manifestPath,
        string path)
    {
        var raw = element.ValueKind == JsonValueKind.Number
            ? element.GetRawText() : "";
        if (raw.Length == 0
            || !uint.TryParse(raw, NumberStyles.None,
                CultureInfo.InvariantCulture, out var value))
        {
            throw Invalid(manifestPath, path,
                "expected an exact unsigned 32-bit integer token");
        }
        return value;
    }

    private static int ReadInt32(JsonElement element, string manifestPath,
        string path)
    {
        var raw = element.ValueKind == JsonValueKind.Number
            ? element.GetRawText() : "";
        if (raw.Length == 0
            || !int.TryParse(raw, NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var value))
        {
            throw Invalid(manifestPath, path,
                "expected an exact signed 32-bit integer token");
        }
        return value;
    }

    private static bool ReadBoolean(JsonElement element, string manifestPath,
        string path)
    {
        if (element.ValueKind is not (JsonValueKind.True
            or JsonValueKind.False))
        {
            throw Invalid(manifestPath, path, "expected a Boolean");
        }
        return element.GetBoolean();
    }

    private static void ValidateJsonText(JsonElement element,
        string manifestPath, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    ValidateUnicode(property.Name, manifestPath, path);
                    if (!names.Add(property.Name))
                    {
                        throw Invalid(manifestPath, path + "."
                            + property.Name,
                            "duplicate decoded property name");
                    }
                    ValidateJsonText(property.Value, manifestPath,
                        path + "." + property.Name);
                }
                break;
            }
            case JsonValueKind.Array:
            {
                var ordinal = 0;
                foreach (var item in element.EnumerateArray())
                {
                    ValidateJsonText(item, manifestPath,
                        $"{path}[{ordinal}]");
                    ordinal++;
                }
                break;
            }
            case JsonValueKind.String:
                string value;
                try
                {
                    value = element.GetString() ?? "";
                }
                catch (InvalidOperationException)
                {
                    throw Invalid(manifestPath, path,
                        "text contains an unpaired surrogate");
                }
                ValidateUnicode(value, manifestPath, path);
                break;
        }
    }

    private static void ValidateUnicode(string value, string manifestPath,
        string path)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length
                    || !char.IsLowSurrogate(value[index + 1]))
                {
                    throw Invalid(manifestPath, path,
                        "text contains an unpaired surrogate");
                }
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                throw Invalid(manifestPath, path,
                    "text contains an unpaired surrogate");
            }
        }
    }

    private static FormatException Invalid(string manifestPath, string path,
        string message)
        => new($"Invalid native Core manifest '{manifestPath}' at {path}: {message}.");
}
