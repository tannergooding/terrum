// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.Tools;

internal static partial class Program
{
    private static readonly byte[] s_sourceEnumExportsUtf8Signature
        = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding s_sourceEnumExportsStrictUtf8
        = new(false, true);

    private static ImmutableArray<TrRmSourceTypeExport>
        ReadTrRmSourceEnumExports(string path)
    {
        var contents = File.ReadAllBytes(path);
        var json = contents.AsSpan();
        if (json.StartsWith(s_sourceEnumExportsUtf8Signature))
        {
            json = json[3..];
            if (json.StartsWith(s_sourceEnumExportsUtf8Signature))
            {
                throw InvalidSourceEnumExports(path, "$",
                    "only one leading UTF-8 signature is permitted");
            }
        }

        string jsonText;
        try
        {
            jsonText = s_sourceEnumExportsStrictUtf8.GetString(json);
        }
        catch (DecoderFallbackException)
        {
            throw InvalidSourceEnumExports(path, "$",
                "input is not valid UTF-8");
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
                "schemaVersion", "exports");
            if (ReadVersion(Property(root, "schemaVersion", path, "$"),
                    path, "$.schemaVersion") != 1)
            {
                throw InvalidSourceEnumExports(path, "$.schemaVersion",
                    "only schema version 1 is supported");
            }

            var exportArray = Property(root, "exports", path, "$");
            if (exportArray.ValueKind != JsonValueKind.Array)
            {
                throw InvalidSourceEnumExports(path, "$.exports",
                    "expected an array");
            }

            var exports = ImmutableArray.CreateBuilder<
                TrRmSourceTypeExport>(exportArray.GetArrayLength());
            var selectors = new HashSet<(string Namespace, string Name)>();
            var exportKeys = new HashSet<string>(StringComparer.Ordinal);
            var exportIndex = 0;
            foreach (var exportElement in exportArray.EnumerateArray())
            {
                var exportPath = $"$.exports[{exportIndex}]";
                var exportObject = RequireObject(exportElement, path,
                    exportPath, "namespace", "name", "exportKey",
                    "enumMemberDeclarationKeys");
                var @namespace = ReadText(Property(exportObject,
                    "namespace", path, exportPath), path,
                    exportPath + ".namespace", allowEmpty: true);
                var name = ReadText(Property(exportObject, "name", path,
                    exportPath), path, exportPath + ".name");
                if (name.Contains('.', StringComparison.Ordinal))
                {
                    throw InvalidSourceEnumExports(path,
                        exportPath + ".name",
                        "expected a simple declaration name");
                }
                if (!selectors.Add((@namespace, name)))
                {
                    throw InvalidSourceEnumExports(path, exportPath,
                        "duplicate declaration selector");
                }

                var exportKey = ReadText(Property(exportObject,
                    "exportKey", path, exportPath), path,
                    exportPath + ".exportKey");
                if (!exportKeys.Add(exportKey))
                {
                    throw InvalidSourceEnumExports(path,
                        exportPath + ".exportKey",
                        "duplicate export key");
                }

                var memberKeysElement = Property(exportObject,
                    "enumMemberDeclarationKeys", path, exportPath);
                if (memberKeysElement.ValueKind != JsonValueKind.Array)
                {
                    throw InvalidSourceEnumExports(path,
                        exportPath + ".enumMemberDeclarationKeys",
                        "expected an array");
                }

                var memberKeys = ImmutableArray.CreateBuilder<string>(
                    memberKeysElement.GetArrayLength());
                var seenMemberKeys = new HashSet<string>(
                    StringComparer.Ordinal);
                var memberIndex = 0;
                foreach (var memberKeyElement
                    in memberKeysElement.EnumerateArray())
                {
                    var memberPath = exportPath
                        + ".enumMemberDeclarationKeys["
                        + memberIndex.ToString(
                            CultureInfo.InvariantCulture) + "]";
                    var memberKey = ReadText(memberKeyElement, path,
                        memberPath);
                    if (!seenMemberKeys.Add(memberKey))
                    {
                        throw InvalidSourceEnumExports(path, memberPath,
                            "duplicate member key within one enum");
                    }

                    memberKeys.Add(memberKey);
                    memberIndex++;
                }

                exports.Add(new TrRmSourceTypeExport(@namespace, name,
                    exportKey) {
                    EnumMemberDeclarationKeys = memberKeys.MoveToImmutable(),
                });
                exportIndex++;
            }

            return exports.MoveToImmutable();
        }
        catch (JsonException exception)
        {
            throw InvalidSourceEnumExports(path,
                exception.Path ?? "$", exception.Message);
        }
    }

    private static JsonElement RequireObject(JsonElement element,
        string path, string jsonPath, params string[] expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw InvalidSourceEnumExports(path, jsonPath,
                "expected an object");
        }

        var expected = new HashSet<string>(expectedProperties,
            StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Remove(property.Name))
            {
                throw InvalidSourceEnumExports(path,
                    jsonPath + "." + property.Name,
                    "unknown property");
            }
        }
        if (expected.Count != 0)
        {
            throw InvalidSourceEnumExports(path, jsonPath,
                $"missing required property '{expected.First()}'");
        }

        return element;
    }

    private static JsonElement Property(JsonElement element, string name,
        string path, string jsonPath)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            throw InvalidSourceEnumExports(path,
                jsonPath + "." + name, "missing required property");
        }
        return property;
    }

    private static string ReadText(JsonElement element, string path,
        string jsonPath, bool allowEmpty = false)
    {
        if (element.ValueKind != JsonValueKind.String
            || element.GetString() is not string value
            || (!allowEmpty && value.Length == 0)
            || value.Contains('\0', StringComparison.Ordinal))
        {
            throw InvalidSourceEnumExports(path, jsonPath,
                allowEmpty
                    ? "expected text without NUL"
                    : "expected nonempty text without NUL");
        }
        return value;
    }

    private static uint ReadVersion(JsonElement element, string path,
        string jsonPath)
    {
        var token = element.ValueKind == JsonValueKind.Number
            ? element.GetRawText() : "";
        if (token.Length == 0
            || !uint.TryParse(token, NumberStyles.None,
                CultureInfo.InvariantCulture, out var value))
        {
            throw InvalidSourceEnumExports(path, jsonPath,
                "expected an exact unsigned integer token");
        }
        return value;
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
                        throw InvalidSourceEnumExports(path,
                            jsonPath + "." + property.Name,
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
                    throw InvalidSourceEnumExports(path, jsonPath,
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
                    throw InvalidSourceEnumExports(path, jsonPath,
                        "text contains an unpaired surrogate");
                }
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                throw InvalidSourceEnumExports(path, jsonPath,
                    "text contains an unpaired surrogate");
            }
        }
    }

    private static FormatException InvalidSourceEnumExports(string path,
        string jsonPath, string message) =>
        new($"Invalid source enum exports '{path}' at {jsonPath}: {message}.");
}
