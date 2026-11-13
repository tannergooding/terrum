using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.Tools;

internal sealed record PrivateModuleIdentityManifest(
    string UsingModuleIdentity,
    ImmutableArray<string> SourceSha256,
    string? ReferenceProducerSha256,
    ImmutableArray<PrivateNominalDefinitionSelection> Definitions,
    ImmutableArray<PrivateNominalImportSelection> Imports,
    ImmutableArray<PrivateEnumMemberSelection> EnumMembers,
    ImmutableArray<PrivateSourceEnumDefinitionAssignment>
        SourceEnumDefinitions);

internal static class PrivateModuleIdentityManifestReader
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly byte[] s_utf8Signature = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    internal static PrivateModuleIdentityManifest Read(string path,
        bool producer, int sourceCount)
    {
        var bytes = ReadFile(path);
        var json = bytes.AsMemory();
        if (json.Span.StartsWith(s_utf8Signature))
        {
            json = json[3..];
            if (json.Span.StartsWith(s_utf8Signature))
            {
                throw Invalid(path, "$",
                    "only one leading UTF-8 signature is permitted");
            }
        }

        string jsonText;
        try
        {
            jsonText = s_strictUtf8.GetString(json.Span);
        }
        catch (DecoderFallbackException exception)
        {
            throw Invalid(path, "$", "manifest is not valid UTF-8",
                exception);
        }

        try
        {
            using var document = JsonDocument.Parse(jsonText,
                new JsonDocumentOptions {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 128,
                });
            ValidateJsonText(document.RootElement, path, "$");
            var root = RequireObject(document.RootElement, path, "$",
                "schemaVersion", "usingModuleIdentity", "sourceSha256",
                "referenceProducerSha256", "definitions", "imports",
                "enumMembers", "sourceEnumDefinitions");
            if (ReadUInt32(Property(root, "schemaVersion", path, "$"),
                    path, "$.schemaVersion") != 1)
            {
                throw Invalid(path, "$.schemaVersion",
                    "only schema version 1 is supported");
            }

            var usingModuleIdentity = ReadString(Property(root,
                "usingModuleIdentity", path, "$"), path,
                "$.usingModuleIdentity");
            if (!IsValidModuleIdentity(usingModuleIdentity))
            {
                throw Invalid(path, "$.usingModuleIdentity",
                    "logical identity is outside the private nominal domain");
            }
            var sourceSha256 = ReadDigests(Property(root, "sourceSha256",
                path, "$"), path, "$.sourceSha256");
            if (sourceSha256.Length != sourceCount)
            {
                throw Invalid(path, "$.sourceSha256",
                    "digest count must match the ordered source operands");
            }

            var referenceProducerSha256Element = Property(root,
                "referenceProducerSha256", path, "$");
            string? referenceProducerSha256;
            if (producer)
            {
                if (referenceProducerSha256Element.ValueKind
                    != JsonValueKind.Null)
                {
                    throw Invalid(path, "$.referenceProducerSha256",
                        "producer manifests require null");
                }
                referenceProducerSha256 = null;
            }
            else
            {
                referenceProducerSha256 = ReadDigest(
                    referenceProducerSha256Element, path,
                    "$.referenceProducerSha256");
            }

            var definitions = ReadDefinitions(Property(root, "definitions",
                path, "$"), path);
            var imports = ReadImports(Property(root, "imports", path, "$"),
                path);

            return new PrivateModuleIdentityManifest(
                usingModuleIdentity, sourceSha256,
                referenceProducerSha256, definitions, imports,
                ReadEnumMembers(Property(root, "enumMembers", path, "$"),
                    path),
                ReadSourceEnumDefinitions(Property(root,
                    "sourceEnumDefinitions", path, "$"), path));
        }
        catch (JsonException exception)
        {
            throw Invalid(path, exception.Path ?? "$", exception.Message,
                exception);
        }
    }

    private static byte[] ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumBytes)
        {
            throw Invalid(path, "$",
                "manifest exceeds the 4 MiB input limit");
        }

        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            throw Invalid(path, "$", "manifest changed while reading");
        }
        return bytes;
    }

    private static ImmutableArray<string> ReadDigests(JsonElement element,
        string path, string jsonPath)
    {
        var items = ReadArray(element, path, jsonPath);
        var digests = ImmutableArray.CreateBuilder<string>(items.Length);
        var ordinal = 0;
        foreach (var item in items)
        {
            digests.Add(ReadDigest(item, path,
                $"{jsonPath}[{ordinal}]"));
            ordinal++;
        }
        return digests.MoveToImmutable();
    }

    private static bool IsValidModuleIdentity(string identity)
    {
        if (identity is not { Length: >= 1 and <= 64 }
            || !IsLetter(identity[0]))
        {
            return false;
        }
        for (var index = 1; index < identity.Length; index++)
        {
            var character = identity[index];
            if (!IsLetter(character)
                && character is not (>= '0' and <= '9')
                && character != '_')
            {
                return false;
            }
        }
        return true;

        static bool IsLetter(char character)
            => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
    }

    private static string ReadDigest(JsonElement element, string path,
        string jsonPath)
    {
        var value = ReadString(element, path, jsonPath);
        if (value.Length != 64 || value.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'A' and <= 'F')))
        {
            throw Invalid(path, jsonPath,
                "expected 64 uppercase hexadecimal SHA-256 characters");
        }
        return value;
    }

    private static ImmutableArray<PrivateNominalDefinitionSelection>
        ReadDefinitions(JsonElement element, string path)
    {
        var items = ReadArray(element, path, "$.definitions");
        var result = ImmutableArray.CreateBuilder<
            PrivateNominalDefinitionSelection>(items.Length);
        for (var index = 0; index < items.Length; index++)
        {
            var itemPath = $"$.definitions[{index}]";
            var item = RequireObject(items[index], path, itemPath, "kind",
                "qualifiedName", "localDeclarationKey", "isExported");
            result.Add(new PrivateNominalDefinitionSelection(
                new PrivateNominalDeclarationSelector(
                    ReadDeclarationKind(Property(item, "kind", path,
                        itemPath), path, itemPath + ".kind"),
                    ReadString(Property(item, "qualifiedName", path,
                        itemPath), path, itemPath + ".qualifiedName")),
                ReadString(Property(item, "localDeclarationKey", path,
                    itemPath), path, itemPath + ".localDeclarationKey"),
                ReadBoolean(Property(item, "isExported", path, itemPath),
                    path, itemPath + ".isExported")));
        }
        return result.MoveToImmutable();
    }

    private static ImmutableArray<PrivateNominalImportSelection>
        ReadImports(JsonElement element, string path)
    {
        var items = ReadArray(element, path, "$.imports");
        var result = ImmutableArray.CreateBuilder<
            PrivateNominalImportSelection>(items.Length);
        for (var index = 0; index < items.Length; index++)
        {
            var itemPath = $"$.imports[{index}]";
            var item = RequireObject(items[index], path, itemPath, "kind",
                "qualifiedName", "localDeclarationKey",
                "selectedModuleIdentity", "selectedExportKey");
            var selectedModuleIdentity = ReadString(Property(item,
                "selectedModuleIdentity", path, itemPath), path,
                itemPath + ".selectedModuleIdentity");
            if (!IsValidModuleIdentity(selectedModuleIdentity))
            {
                throw Invalid(path, itemPath + ".selectedModuleIdentity",
                    "provider identity is outside the private nominal domain");
            }
            result.Add(new PrivateNominalImportSelection(
                new PrivateNominalDeclarationSelector(
                    ReadDeclarationKind(Property(item, "kind", path,
                        itemPath), path, itemPath + ".kind"),
                    ReadString(Property(item, "qualifiedName", path,
                        itemPath), path, itemPath + ".qualifiedName")),
                ReadString(Property(item, "localDeclarationKey", path,
                    itemPath), path, itemPath + ".localDeclarationKey"),
                selectedModuleIdentity,
                ReadString(Property(item, "selectedExportKey", path,
                    itemPath), path, itemPath + ".selectedExportKey")));
        }
        return result.MoveToImmutable();
    }

    private static ImmutableArray<PrivateEnumMemberSelection>
        ReadEnumMembers(JsonElement element, string path)
    {
        var items = ReadArray(element, path, "$.enumMembers");
        var result = ImmutableArray.CreateBuilder<
            PrivateEnumMemberSelection>(items.Length);
        for (var index = 0; index < items.Length; index++)
        {
            var itemPath = $"$.enumMembers[{index}]";
            var item = RequireObject(items[index], path, itemPath, "owner",
                "enumIdentity", "memberOrdinal", "memberIdentity",
                "memberDeclarationKey");
            var ownerPath = itemPath + ".owner";
            var owner = RequireObject(Property(item, "owner", path,
                itemPath), path, ownerPath, "kind", "qualifiedName");
            if (ReadString(Property(owner, "kind", path, ownerPath), path,
                    ownerPath + ".kind") != "enum")
            {
                throw Invalid(path, ownerPath + ".kind",
                    "Core enum-member owners must use the enum kind");
            }

            result.Add(new PrivateEnumMemberSelection(
                new PrivateNominalDeclarationSelector(
                    PrivateNominalDeclarationKind.Enum,
                    ReadString(Property(owner, "qualifiedName", path,
                        ownerPath), path, ownerPath + ".qualifiedName")),
                ReadIdentity(Property(item, "enumIdentity", path,
                    itemPath), CoreDeclarationKind.EnumType, path,
                    itemPath + ".enumIdentity"),
                ReadInt32(Property(item, "memberOrdinal", path, itemPath),
                    path, itemPath + ".memberOrdinal"),
                ReadIdentity(Property(item, "memberIdentity", path,
                    itemPath), CoreDeclarationKind.EnumMember, path,
                    itemPath + ".memberIdentity"),
                ReadString(Property(item, "memberDeclarationKey", path,
                    itemPath), path, itemPath + ".memberDeclarationKey")));
        }
        return result.MoveToImmutable();
    }

    private static ImmutableArray<PrivateSourceEnumDefinitionAssignment>
        ReadSourceEnumDefinitions(JsonElement element, string path)
    {
        var items = ReadArray(element, path, "$.sourceEnumDefinitions");
        var result = ImmutableArray.CreateBuilder<
            PrivateSourceEnumDefinitionAssignment>(items.Length);
        for (var index = 0; index < items.Length; index++)
        {
            var itemPath = $"$.sourceEnumDefinitions[{index}]";
            var item = RequireObject(items[index], path, itemPath,
                "qualifiedName", "localDeclarationKey", "isExported",
                "memberDeclarationKeys");
            var memberElements = ReadArray(Property(item,
                "memberDeclarationKeys", path, itemPath), path,
                itemPath + ".memberDeclarationKeys");
            var memberKeys = ImmutableArray.CreateBuilder<string>(
                memberElements.Length);
            for (var memberIndex = 0;
                memberIndex < memberElements.Length;
                memberIndex++)
            {
                memberKeys.Add(ReadString(memberElements[memberIndex], path,
                    $"{itemPath}.memberDeclarationKeys[{memberIndex}]"));
            }

            result.Add(new PrivateSourceEnumDefinitionAssignment(
                ReadString(Property(item, "qualifiedName", path,
                    itemPath), path, itemPath + ".qualifiedName"),
                ReadString(Property(item, "localDeclarationKey", path,
                    itemPath), path, itemPath + ".localDeclarationKey"),
                ReadBoolean(Property(item, "isExported", path, itemPath),
                    path, itemPath + ".isExported"),
                memberKeys.MoveToImmutable()));
        }
        return result.MoveToImmutable();
    }

    private static CoreDeclarationIdentity ReadIdentity(JsonElement element,
        CoreDeclarationKind expectedKind, string path, string jsonPath)
    {
        var identity = RequireObject(element, path, jsonPath,
            "providerIdentity", "exportKey", "kind");
        var kind = ReadString(Property(identity, "kind", path, jsonPath),
            path, jsonPath + ".kind") switch {
            "enum-type" => CoreDeclarationKind.EnumType,
            "enum-member" => CoreDeclarationKind.EnumMember,
            _ => throw Invalid(path, jsonPath + ".kind",
                "unknown Core declaration kind"),
        };
        var result = new CoreDeclarationIdentity(
            ReadString(Property(identity, "providerIdentity", path,
                jsonPath), path, jsonPath + ".providerIdentity"),
            ReadString(Property(identity, "exportKey", path, jsonPath),
                path, jsonPath + ".exportKey"), kind);
        if (!result.IsValid || result.Kind != expectedKind)
        {
            throw Invalid(path, jsonPath,
                "Core identity has the wrong kind or invalid components");
        }
        return result;
    }

    private static PrivateNominalDeclarationKind ReadDeclarationKind(
        JsonElement element, string path, string jsonPath)
        => ReadString(element, path, jsonPath) switch {
            "struct" => PrivateNominalDeclarationKind.Struct,
            "class" => PrivateNominalDeclarationKind.Class,
            "enum" => PrivateNominalDeclarationKind.Enum,
            _ => throw Invalid(path, jsonPath,
                "unknown nominal declaration kind"),
        };

    private static ImmutableArray<JsonElement> ReadArray(JsonElement element,
        string path, string jsonPath)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(path, jsonPath, "expected an array");
        }
        return [.. element.EnumerateArray()];
    }

    private static JsonElement RequireObject(JsonElement element,
        string path, string jsonPath, params string[] expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(path, jsonPath, "expected an object");
        }

        var expected = new HashSet<string>(expectedProperties,
            StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Remove(property.Name))
            {
                throw Invalid(path, jsonPath + "." + property.Name,
                    "unknown property");
            }
        }
        if (expected.Count != 0)
        {
            throw Invalid(path, jsonPath,
                $"missing required property '{expected.First()}'");
        }
        return element;
    }

    private static JsonElement Property(JsonElement element, string name,
        string path, string jsonPath)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            throw Invalid(path, jsonPath + "." + name,
                "missing required property");
        }
        return property;
    }

    private static string ReadString(JsonElement element, string path,
        string jsonPath)
    {
        if (element.ValueKind != JsonValueKind.String
            || element.GetString() is not string value)
        {
            throw Invalid(path, jsonPath, "expected a string");
        }
        return value;
    }

    private static bool ReadBoolean(JsonElement element, string path,
        string jsonPath)
    {
        if (element.ValueKind is not (JsonValueKind.True
            or JsonValueKind.False))
        {
            throw Invalid(path, jsonPath, "expected a Boolean");
        }
        return element.GetBoolean();
    }

    private static uint ReadUInt32(JsonElement element, string path,
        string jsonPath)
    {
        var raw = element.ValueKind == JsonValueKind.Number
            ? element.GetRawText() : "";
        if (raw.Length == 0
            || !uint.TryParse(raw, NumberStyles.None,
                CultureInfo.InvariantCulture, out var value))
        {
            throw Invalid(path, jsonPath,
                "expected an exact unsigned 32-bit integer token");
        }
        return value;
    }

    private static int ReadInt32(JsonElement element, string path,
        string jsonPath)
    {
        var raw = element.ValueKind == JsonValueKind.Number
            ? element.GetRawText() : "";
        if (raw.Length == 0
            || !uint.TryParse(raw, NumberStyles.None,
                CultureInfo.InvariantCulture, out var value)
            || value > int.MaxValue)
        {
            throw Invalid(path, jsonPath,
                "expected an unsigned 32-bit integer token in range");
        }
        return (int)value;
    }

    private static void ValidateJsonText(JsonElement element, string path,
        string jsonPath)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    ValidateUnicode(property.Name, path, jsonPath);
                    if (!names.Add(property.Name))
                    {
                        throw Invalid(path, jsonPath + "."
                            + property.Name,
                            "duplicate decoded property name");
                    }
                    ValidateJsonText(property.Value, path,
                        jsonPath + "." + property.Name);
                }
                break;
            }
            case JsonValueKind.Array:
            {
                var ordinal = 0;
                foreach (var item in element.EnumerateArray())
                {
                    ValidateJsonText(item, path,
                        $"{jsonPath}[{ordinal}]");
                    ordinal++;
                }
                break;
            }
            case JsonValueKind.String:
                ValidateUnicode(element.GetString() ?? "", path,
                    jsonPath);
                break;
        }
    }

    private static void ValidateUnicode(string value, string path,
        string jsonPath)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length
                    || !char.IsLowSurrogate(value[index + 1]))
                {
                    throw Invalid(path, jsonPath,
                        "text contains an unpaired surrogate");
                }
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                throw Invalid(path, jsonPath,
                    "text contains an unpaired surrogate");
            }
        }
    }

    private static FormatException Invalid(string path, string jsonPath,
        string message, Exception? innerException = null)
        => new($"Invalid private identity manifest '{path}' at {jsonPath}: {message}.",
            innerException);
}
