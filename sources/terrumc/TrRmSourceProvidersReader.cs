// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Terrum.Tools;

internal sealed record TrRmSourceProvidersFile(
    int SchemaVersion,
    ImmutableArray<TrRmSourceProviderArtifact> Providers,
    ImmutableArray<TrRmSourceProviderReference> References);

internal sealed record TrRmSourceProviderArtifact(
    string SchemaIdentity, string Path);

internal sealed record TrRmSourceProviderReference(
    int ProviderInputOrdinal, string ModuleIdentity,
    ImmutableArray<string> TypeExportKeys,
    ImmutableArray<string> FieldExportKeys);

internal static class TrRmSourceProvidersReader
{
    private static readonly byte[] s_utf8Signature = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    internal static TrRmSourceProvidersFile Read(string path)
    {
        var contents = File.ReadAllBytes(path);
        var json = contents.AsSpan();
        if (json.StartsWith(s_utf8Signature))
        {
            json = json[3..];
            if (json.StartsWith(s_utf8Signature))
            {
                throw Invalid(path, "$",
                    "only one leading UTF-8 signature is permitted");
            }
        }

        string jsonText;
        try
        {
            jsonText = s_strictUtf8.GetString(json);
        }
        catch (DecoderFallbackException)
        {
            throw Invalid(path, "$", "input is not valid UTF-8");
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
                "schemaVersion", "providers", "references");
            var version = ReadVersion(Property(root, "schemaVersion",
                path, "$"), path, "$.schemaVersion");

            var providerArray = Property(root, "providers", path, "$");
            if (providerArray.ValueKind != JsonValueKind.Array
                || providerArray.GetArrayLength() == 0)
            {
                throw Invalid(path, "$.providers",
                    "expected a nonempty array");
            }

            var providers = ImmutableArray.CreateBuilder<
                TrRmSourceProviderArtifact>(
                    providerArray.GetArrayLength());
            var providerIndex = 0;
            foreach (var providerElement in providerArray.EnumerateArray())
            {
                var providerPath = $"$.providers[{providerIndex}]";
                var provider = RequireObject(providerElement, path,
                    providerPath, "schemaIdentity", "path");
                var schemaIdentity = ReadText(Property(provider,
                    "schemaIdentity", path, providerPath), path,
                    providerPath + ".schemaIdentity");
                var artifactPath = ReadText(Property(provider, "path",
                    path, providerPath), path, providerPath + ".path");
                if (artifactPath[0] == '-')
                {
                    throw Invalid(path, providerPath + ".path",
                        "dash-leading paths are not permitted");
                }

                providers.Add(new TrRmSourceProviderArtifact(
                    schemaIdentity, artifactPath));
                providerIndex++;
            }

            var referenceArray = Property(root, "references", path, "$");
            if (referenceArray.ValueKind != JsonValueKind.Array
                || referenceArray.GetArrayLength() == 0)
            {
                throw Invalid(path, "$.references",
                    "expected a nonempty array");
            }

            var references = ImmutableArray.CreateBuilder<
                TrRmSourceProviderReference>(
                    referenceArray.GetArrayLength());
            var selectors = new HashSet<(int Provider, string Module)>();
            var referenceIndex = 0;
            foreach (var referenceElement in referenceArray.EnumerateArray())
            {
                var referencePath = $"$.references[{referenceIndex}]";
                var reference = RequireObject(referenceElement, path,
                    referencePath, "providerInputOrdinal", "moduleIdentity",
                    "typeExportKeys", "fieldExportKeys");
                var providerOrdinal = ReadOrdinal(Property(reference,
                    "providerInputOrdinal", path, referencePath), path,
                    referencePath + ".providerInputOrdinal");
                if (providerOrdinal >= providers.Count)
                {
                    throw Invalid(path,
                        referencePath + ".providerInputOrdinal",
                        "ordinal is outside the provider array");
                }

                var moduleIdentity = ReadText(Property(reference,
                    "moduleIdentity", path, referencePath), path,
                    referencePath + ".moduleIdentity");
                if (!selectors.Add((providerOrdinal, moduleIdentity)))
                {
                    throw Invalid(path, referencePath,
                        "duplicate provider Module selection");
                }

                var keys = new HashSet<string>(StringComparer.Ordinal);
                var typeKeys = ReadKeys(Property(reference, "typeExportKeys",
                    path, referencePath), path,
                    referencePath + ".typeExportKeys", allowEmpty: false,
                    seenKeys: keys);
                var fieldKeys = ReadKeys(Property(reference,
                    "fieldExportKeys", path, referencePath), path,
                    referencePath + ".fieldExportKeys", allowEmpty: true,
                    seenKeys: keys);
                references.Add(new TrRmSourceProviderReference(
                    providerOrdinal, moduleIdentity, typeKeys, fieldKeys));
                referenceIndex++;
            }

            return new TrRmSourceProvidersFile(version,
                providers.MoveToImmutable(), references.MoveToImmutable());
        }
        catch (JsonException exception)
        {
            throw Invalid(path, exception.Path ?? "$", exception.Message);
        }
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

    private static ImmutableArray<string> ReadKeys(JsonElement element,
        string path, string jsonPath, bool allowEmpty,
        HashSet<string> seenKeys)
    {
        if (element.ValueKind != JsonValueKind.Array
            || (!allowEmpty && element.GetArrayLength() == 0))
        {
            throw Invalid(path, jsonPath,
                allowEmpty ? "expected an array" : "expected a nonempty array");
        }

        var keys = ImmutableArray.CreateBuilder<string>(
            element.GetArrayLength());
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var keyPath = jsonPath + "["
                + index.ToString(CultureInfo.InvariantCulture) + "]";
            var key = ReadText(item, path, keyPath, maximumUtf8Bytes: 0xFF_FFFF);
            if (!seenKeys.Add(key))
            {
                throw Invalid(path, keyPath,
                    "duplicate ExportKey in one reference");
            }

            keys.Add(key);
            index++;
        }
        return keys.MoveToImmutable();
    }

    private static string ReadText(JsonElement element, string path,
        string jsonPath, int maximumUtf8Bytes = int.MaxValue)
    {
        if (element.ValueKind != JsonValueKind.String
            || element.GetString() is not string value
            || value.Length == 0
            || value.Contains('\0', StringComparison.Ordinal))
        {
            throw Invalid(path, jsonPath,
                "expected nonempty text without NUL");
        }

        ValidateUnicode(value, path, jsonPath);
        try
        {
            if (s_strictUtf8.GetByteCount(value) > maximumUtf8Bytes)
            {
                throw Invalid(path, jsonPath,
                    "UTF-8 text exceeds the existing String capacity");
            }
        }
        catch (EncoderFallbackException)
        {
            throw Invalid(path, jsonPath,
                "text contains an unpaired surrogate");
        }
        return value;
    }

    private static int ReadOrdinal(JsonElement element, string path,
        string jsonPath)
    {
        var token = element.ValueKind == JsonValueKind.Number
            ? element.GetRawText() : "";
        if (token.Length == 0
            || !int.TryParse(token, NumberStyles.None,
                CultureInfo.InvariantCulture, out var value)
            || value < 0)
        {
            throw Invalid(path, jsonPath,
                "expected an unsigned decimal integer token");
        }
        return value;
    }

    private static int ReadVersion(JsonElement element, string path,
        string jsonPath)
    {
        var token = element.ValueKind == JsonValueKind.Number
            ? element.GetRawText() : "";
        if (token is not ("1" or "2"))
        {
            throw Invalid(path, jsonPath,
                "only exact version tokens 1 and 2 are supported");
        }
        return token == "1" ? 1 : 2;
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
                        throw Invalid(path, jsonPath + "." + property.Name,
                            "duplicate decoded property name");
                    }
                    ValidateJsonText(property.Value, path,
                        jsonPath + "." + property.Name);
                }
                break;
            }
            case JsonValueKind.Array:
            {
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    ValidateJsonText(item, path,
                        jsonPath + "["
                        + index.ToString(CultureInfo.InvariantCulture) + "]");
                    index++;
                }
                break;
            }
            case JsonValueKind.String:
            {
                string value;
                try
                {
                    value = element.GetString() ?? "";
                }
                catch (InvalidOperationException)
                {
                    throw Invalid(path, jsonPath,
                        "text contains an unpaired surrogate");
                }
                ValidateUnicode(value, path, jsonPath);
                break;
            }
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
        string message)
        => new($"Invalid source providers '{path}' at {jsonPath}: {message}.");
}
