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

internal sealed record LoadedLayoutCoreManifest(
    string CoreAuthorityIdentity,
    string UsingModuleIdentity,
    LayoutCoreRegistrationSet Registration);

internal sealed record LayoutCoreManifestDefinition(
    string CoreAuthorityIdentity,
    string UsingModuleIdentity,
    ImmutableArray<CoreProviderModuleOrigin> Origins,
    CoreEnumDefinition LayoutKinds,
    CoreLayoutAttributeSchema Layout,
    CoreLayoutAttributeSchema Blittable)
{
    internal ImmutableArray<CoreProviderDeclarationSnapshot> Declarations =>
    [
        CoreProviderDeclarationSnapshot.From(LayoutKinds),
        CoreProviderDeclarationSnapshot.From(Layout),
        CoreProviderDeclarationSnapshot.From(Blittable),
    ];

    internal LayoutCoreRegistrationSet CreateRegistration(
        CoreProviderModuleBindings bindings)
        => LayoutCoreRegistrationSet.Create(bindings, LayoutKinds, Layout,
            Blittable);
}

internal static class LayoutCoreManifestReader
{
    private static readonly byte[] s_utf8Signature = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    internal static LoadedLayoutCoreManifest Read(string path)
    {
        var definition = ReadDefinition(path);
        try
        {
            var bindings = CoreProviderModuleBindings.Create(
                definition.UsingModuleIdentity, definition.Origins,
                definition.Declarations);
            return new LoadedLayoutCoreManifest(
                definition.CoreAuthorityIdentity,
                definition.UsingModuleIdentity,
                definition.CreateRegistration(bindings));
        }
        catch (ArgumentException exception)
        {
            throw Invalid(path, "$", exception.Message);
        }
    }

    internal static LayoutCoreManifestDefinition ReadDefinition(string path)
    {
        var contents = File.ReadAllBytes(path);
        var json = contents.AsMemory();
        if (json.Span.StartsWith(s_utf8Signature))
        {
            json = json[3..];
            if (json.Span.StartsWith(s_utf8Signature))
            {
                throw Invalid(path, "$", "only one leading UTF-8 signature is permitted");
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
                "schemaVersion", "coreAuthorityIdentity",
                "usingModuleIdentity", "providers", "layoutKinds",
                "layout", "blittable");
            if (ReadUInt32(Property(root, "schemaVersion", path, "$"),
                    path, "$.schemaVersion") != 1)
            {
                throw Invalid(path, "$.schemaVersion",
                    "only schema version 1 is supported");
            }

            var authorityIdentity = ReadText(Property(root,
                "coreAuthorityIdentity", path, "$"), path,
                "$.coreAuthorityIdentity");
            var usingModuleIdentity = ReadText(Property(root,
                "usingModuleIdentity", path, "$"), path,
                "$.usingModuleIdentity");
            var origins = ReadOrigins(Property(root, "providers", path, "$"),
                path);
            var layoutKinds = ReadEnumDefinition(Property(root, "layoutKinds",
                path, "$"), path, "$.layoutKinds");
            var layout = ReadSchema(Property(root, "layout", path, "$"), path,
                "$.layout");
            var blittable = ReadSchema(Property(root, "blittable", path, "$"),
                path, "$.blittable");
            ValidateUsedProviders(origins, layoutKinds, layout, blittable,
                path);

            return new LayoutCoreManifestDefinition(authorityIdentity,
                usingModuleIdentity, origins, layoutKinds, layout, blittable);
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

    private static ImmutableArray<CoreProviderModuleOrigin> ReadOrigins(
        JsonElement element, string manifestPath)
    {
        if (element.ValueKind != JsonValueKind.Array
            || element.GetArrayLength() is < 1 or > 3)
        {
            throw Invalid(manifestPath, "$.providers",
                "expected an array containing one through three provider origins");
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

    private static CoreEnumDefinition ReadEnumDefinition(JsonElement element,
        string manifestPath, string path)
    {
        var value = RequireObject(element, manifestPath, path, "identity",
            "qualifiedName", "underlyingType", "members", "genericArity",
            "isPublic", "isPrimary", "isFlags",
            "blittableTargetRequirement");
        var identity = ReadIdentity(Property(value, "identity", manifestPath,
            path), manifestPath, path + ".identity");
        if (ReadText(Property(value, "underlyingType", manifestPath, path),
                manifestPath, path + ".underlyingType") != "int32")
        {
            throw Invalid(manifestPath, path + ".underlyingType",
                "expected int32");
        }

        var qualifiedName = ReadQualifiedName(Property(value, "qualifiedName",
            manifestPath, path), manifestPath, path + ".qualifiedName");
        var membersElement = Property(value, "members", manifestPath, path);
        if (membersElement.ValueKind != JsonValueKind.Array
            || membersElement.GetArrayLength() != 4)
        {
            throw Invalid(manifestPath, path + ".members",
                "expected exactly four ordered members");
        }

        var members = ImmutableArray.CreateBuilder<CoreEnumMemberDefinition>(
            membersElement.GetArrayLength());
        var ordinal = 0;
        foreach (var item in membersElement.EnumerateArray())
        {
            var memberPath = $"{path}.members[{ordinal}]";
            var member = RequireObject(item, manifestPath, memberPath,
                "identity", "declaringEnum", "declarationKey", "name",
                "value", "isPublic");
            var memberName = ReadIdentifier(Property(member, "name",
                manifestPath, memberPath), manifestPath,
                memberPath + ".name");
            var memberDefinition = new CoreEnumMemberDefinition(
                ReadIdentity(Property(member, "identity", manifestPath,
                    memberPath), manifestPath, memberPath + ".identity"),
                ReadIdentity(Property(member, "declaringEnum",
                    manifestPath, memberPath), manifestPath,
                    memberPath + ".declaringEnum"),
                memberName,
                ReadInt32(Property(member, "value", manifestPath,
                    memberPath), manifestPath, memberPath + ".value"),
                ReadBoolean(Property(member, "isPublic", manifestPath,
                    memberPath), manifestPath, memberPath + ".isPublic"))
            {
                DeclarationKey = ReadText(Property(member,
                    "declarationKey", manifestPath, memberPath),
                    manifestPath, memberPath + ".declarationKey"),
            };
            members.Add(memberDefinition);
            ordinal++;
        }

        var blittableTargetRequirement = ReadUInt32(Property(value,
            "blittableTargetRequirement", manifestPath, path),
            manifestPath, path + ".blittableTargetRequirement");
        if (blittableTargetRequirement > int.MaxValue)
        {
            throw Invalid(manifestPath, path + ".blittableTargetRequirement",
                "value exceeds the supported enum commitment range");
        }

        return new CoreEnumDefinition(identity, qualifiedName,
            PrimitiveType.Int32, members.MoveToImmutable(),
            checked((int)ReadUInt32(Property(value, "genericArity",
                manifestPath, path), manifestPath, path + ".genericArity")),
            ReadBoolean(Property(value, "isPublic", manifestPath, path),
                manifestPath, path + ".isPublic"),
            ReadBoolean(Property(value, "isPrimary", manifestPath, path),
                manifestPath, path + ".isPrimary"),
            ReadBoolean(Property(value, "isFlags", manifestPath, path),
                manifestPath, path + ".isFlags"))
        {
            BlittableTargetRequirement =
                (int)blittableTargetRequirement,
        };
    }

    private static CoreLayoutAttributeSchema ReadSchema(JsonElement element,
        string manifestPath, string path)
    {
        var value = RequireObject(element, manifestPath, path, "identity",
            "qualifiedName", "arguments", "targets", "isPrimaryTargetOnly",
            "genericArity", "isPublic", "isPrimary", "isRepeatable");
        var identity = ReadIdentity(Property(value, "identity", manifestPath,
            path), manifestPath, path + ".identity");
        var qualifiedName = ReadQualifiedName(Property(value, "qualifiedName",
            manifestPath, path), manifestPath, path + ".qualifiedName");
        var argumentsElement = Property(value, "arguments", manifestPath,
            path);
        if (argumentsElement.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(manifestPath, path + ".arguments",
                "expected an initialized array");
        }
        var arguments = ImmutableArray.CreateBuilder<
            CoreLayoutAttributeArgument>(argumentsElement.GetArrayLength());
        var ordinal = 0;
        foreach (var item in argumentsElement.EnumerateArray())
        {
            var argumentPath = $"{path}.arguments[{ordinal}]";
            var argument = RequireObject(item, manifestPath, argumentPath,
                "name", "kind", "enumType", "isRequired",
                "allowsPositional", "defaultUInt32");
            var name = ReadText(Property(argument, "name", manifestPath,
                argumentPath), manifestPath, argumentPath + ".name");
            var kindText = ReadText(Property(argument, "kind", manifestPath,
                argumentPath), manifestPath, argumentPath + ".kind");
            var kind = kindText switch {
                "layout-kind" => CoreLayoutAttributeArgumentKind.LayoutKind,
                "uint32" => CoreLayoutAttributeArgumentKind.Unsigned32,
                _ => throw Invalid(manifestPath, argumentPath + ".kind",
                    "unknown layout argument kind"),
            };
            var enumType = ReadNullableIdentity(Property(argument,
                "enumType", manifestPath, argumentPath), manifestPath,
                argumentPath + ".enumType");
            var defaultUInt32 = ReadNullableUInt32(Property(argument,
                "defaultUInt32", manifestPath, argumentPath), manifestPath,
                argumentPath + ".defaultUInt32");
            arguments.Add(new CoreLayoutAttributeArgument(name, kind,
                enumType,
                ReadBoolean(Property(argument, "isRequired", manifestPath,
                    argumentPath), manifestPath,
                    argumentPath + ".isRequired"),
                ReadBoolean(Property(argument, "allowsPositional",
                    manifestPath, argumentPath), manifestPath,
                    argumentPath + ".allowsPositional"),
                defaultUInt32));
            ordinal++;
        }

        var targets = ReadTargets(Property(value, "targets", manifestPath,
            path), manifestPath, path + ".targets");
        var genericArity = ReadUInt32(Property(value, "genericArity",
            manifestPath, path), manifestPath, path + ".genericArity");
        if (genericArity > int.MaxValue)
        {
            throw Invalid(manifestPath, path + ".genericArity",
                "value exceeds the supported range");
        }

        return new CoreLayoutAttributeSchema(identity, qualifiedName,
            arguments.MoveToImmutable(), targets,
            ReadBoolean(Property(value, "isPrimaryTargetOnly",
                manifestPath, path), manifestPath,
                path + ".isPrimaryTargetOnly"),
            (int)genericArity,
            ReadBoolean(Property(value, "isPublic", manifestPath, path),
                manifestPath, path + ".isPublic"),
            ReadBoolean(Property(value, "isPrimary", manifestPath, path),
                manifestPath, path + ".isPrimary"),
            ReadBoolean(Property(value, "isRepeatable", manifestPath, path),
                manifestPath, path + ".isRepeatable"));
    }

    private static CoreLayoutAttributeTargets ReadTargets(JsonElement element,
        string manifestPath, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(manifestPath, path, "expected an array");
        }

        var targets = CoreLayoutAttributeTargets.None;
        foreach (var item in element.EnumerateArray())
        {
            var target = ReadText(item, manifestPath, path);
            var flag = target switch {
                "inline-struct" => CoreLayoutAttributeTargets.InlineStruct,
                "integer-backed-enum"
                    => CoreLayoutAttributeTargets.IntegerBackedEnum,
                _ => throw Invalid(manifestPath, path,
                    "unknown layout attribute target"),
            };
            if ((targets & flag) != 0)
            {
                throw Invalid(manifestPath, path,
                    "target set contains a duplicate");
            }
            targets |= flag;
        }
        return targets;
    }

    private static void ValidateUsedProviders(
        ImmutableArray<CoreProviderModuleOrigin> origins,
        CoreEnumDefinition layoutKinds, CoreLayoutAttributeSchema layout,
        CoreLayoutAttributeSchema blittable, string manifestPath)
    {
        var used = new HashSet<string>(StringComparer.Ordinal) {
            layoutKinds.Identity.ProviderIdentity,
            layout.Identity.ProviderIdentity,
            blittable.Identity.ProviderIdentity,
        };
        foreach (var member in layoutKinds.Members)
        {
            _ = used.Add(member.Identity.ProviderIdentity);
        }
        if (used.Count != origins.Length
            || origins.Any(origin => !used.Remove(origin.ModuleIdentity))
            || used.Count != 0)
        {
            throw Invalid(manifestPath, "$.providers",
                "origins must name each used provider exactly once and contain no unused provider");
        }
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
                manifestPath, path + ".exportKey"),
            kind);
    }

    private static CoreDeclarationIdentity? ReadNullableIdentity(
        JsonElement element, string manifestPath, string path)
        => element.ValueKind == JsonValueKind.Null
            ? null : ReadIdentity(element, manifestPath, path);

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

        return string.Join(".", parts);
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
        return tokens.Tokens[0].Name;
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

    private static uint? ReadNullableUInt32(JsonElement element,
        string manifestPath, string path)
        => element.ValueKind == JsonValueKind.Null
            ? null : ReadUInt32(element, manifestPath, path);

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
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
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
                            + property.Name, "duplicate decoded property name");
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
                ValidateUnicode(element.GetString() ?? "", manifestPath,
                    path);
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
        => new($"Invalid layout Core manifest '{manifestPath}' at {path}: {message}.");
}
