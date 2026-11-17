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
    private static readonly byte[] s_sourceTypeExportsUtf8Signature
        = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding s_sourceTypeExportsStrictUtf8
        = new(false, true);

    private static ImmutableArray<TrRmSourceTypeExport>
        ReadTrRmSourceTypeExports(string path)
    {
        var contents = File.ReadAllBytes(path);
        var json = contents.AsSpan();
        if (json.StartsWith(s_sourceTypeExportsUtf8Signature))
        {
            json = json[3..];
            if (json.StartsWith(s_sourceTypeExportsUtf8Signature))
            {
                throw InvalidSourceTypeExports(path, "$",
                    "only one leading UTF-8 signature is permitted");
            }
        }

        string jsonText;
        try
        {
            jsonText = s_sourceTypeExportsStrictUtf8.GetString(json);
        }
        catch (DecoderFallbackException)
        {
            throw InvalidSourceTypeExports(path, "$",
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
            ValidateSourceTypeExportsJsonText(document.RootElement, path,
                "$");
            var root = RequireSourceTypeExportsObject(
                document.RootElement, path, "$",
                "schemaVersion", "exports");
            if (ReadSourceTypeExportsVersion(PropertySourceTypeExports(
                    root, "schemaVersion", path, "$"), path,
                    "$.schemaVersion") != 1)
            {
                throw InvalidSourceTypeExports(path, "$.schemaVersion",
                    "only schema version 1 is supported");
            }

            var exportArray = PropertySourceTypeExports(root, "exports",
                path, "$");
            if (exportArray.ValueKind != JsonValueKind.Array)
            {
                throw InvalidSourceTypeExports(path, "$.exports",
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
                var exportObject = RequireSourceTypeExportsObject(
                    exportElement, path, exportPath, "namespace", "name",
                    "exportKey", "fieldExports");
                var @namespace = ReadSourceTypeExportsText(
                    PropertySourceTypeExports(exportObject, "namespace",
                        path, exportPath), path,
                    exportPath + ".namespace", allowEmpty: true);
                var name = ReadSourceTypeExportsText(
                    PropertySourceTypeExports(exportObject, "name", path,
                        exportPath), path, exportPath + ".name");
                if (name.Contains('.', StringComparison.Ordinal))
                {
                    throw InvalidSourceTypeExports(path,
                        exportPath + ".name",
                        "expected a simple declaration name");
                }
                if (!selectors.Add((@namespace, name)))
                {
                    throw InvalidSourceTypeExports(path, exportPath,
                        "duplicate declaration selector");
                }

                var exportKey = ReadSourceTypeExportsText(
                    PropertySourceTypeExports(exportObject, "exportKey",
                        path, exportPath), path,
                    exportPath + ".exportKey");
                if (!exportKeys.Add(exportKey))
                {
                    throw InvalidSourceTypeExports(path,
                        exportPath + ".exportKey",
                        "duplicate ExportKey");
                }

                var fieldArray = PropertySourceTypeExports(exportObject,
                    "fieldExports", path, exportPath);
                if (fieldArray.ValueKind != JsonValueKind.Array)
                {
                    throw InvalidSourceTypeExports(path,
                        exportPath + ".fieldExports",
                        "expected an array");
                }

                var fieldExports = ImmutableArray.CreateBuilder<
                    TrRmSourceFieldExport>(fieldArray.GetArrayLength());
                var fieldNames = new HashSet<string>(StringComparer.Ordinal);
                var fieldIndex = 0;
                foreach (var fieldElement in fieldArray.EnumerateArray())
                {
                    var fieldPath = exportPath + ".fieldExports["
                        + fieldIndex.ToString(CultureInfo.InvariantCulture)
                        + "]";
                    var fieldObject = RequireSourceTypeExportsObject(
                        fieldElement, path, fieldPath, "name",
                        "exportKey");
                    var fieldName = ReadSourceTypeExportsText(
                        PropertySourceTypeExports(fieldObject, "name",
                            path, fieldPath), path, fieldPath + ".name");
                    if (fieldName.Contains('.', StringComparison.Ordinal))
                    {
                        throw InvalidSourceTypeExports(path,
                            fieldPath + ".name",
                            "expected a direct field name");
                    }
                    if (!fieldNames.Add(fieldName))
                    {
                        throw InvalidSourceTypeExports(path,
                            fieldPath + ".name",
                            "duplicate field selector");
                    }

                    var fieldExportKey = ReadSourceTypeExportsText(
                        PropertySourceTypeExports(fieldObject,
                            "exportKey", path, fieldPath), path,
                        fieldPath + ".exportKey");
                    if (!exportKeys.Add(fieldExportKey))
                    {
                        throw InvalidSourceTypeExports(path,
                            fieldPath + ".exportKey",
                            "duplicate ExportKey");
                    }

                    fieldExports.Add(new TrRmSourceFieldExport(fieldName,
                        fieldExportKey));
                    fieldIndex++;
                }

                exports.Add(new TrRmSourceTypeExport(@namespace, name,
                    exportKey) {
                    FieldExports = fieldExports.MoveToImmutable(),
                });
                exportIndex++;
            }

            return exports.MoveToImmutable();
        }
        catch (JsonException exception)
        {
            throw InvalidSourceTypeExports(path,
                exception.Path ?? "$", exception.Message);
        }
    }

    private static JsonElement RequireSourceTypeExportsObject(
        JsonElement element,
        string path, string jsonPath, params string[] expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw InvalidSourceTypeExports(path, jsonPath,
                "expected an object");
        }

        var expected = new HashSet<string>(expectedProperties,
            StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Remove(property.Name))
            {
                throw InvalidSourceTypeExports(path,
                    jsonPath + "." + property.Name,
                    "unknown property");
            }
        }
        if (expected.Count != 0)
        {
            throw InvalidSourceTypeExports(path, jsonPath,
                $"missing required property '{expected.First()}'");
        }

        return element;
    }

    private static JsonElement PropertySourceTypeExports(
        JsonElement element, string name, string path, string jsonPath)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            throw InvalidSourceTypeExports(path,
                jsonPath + "." + name, "missing required property");
        }
        return property;
    }

    private static string ReadSourceTypeExportsText(JsonElement element,
        string path, string jsonPath, bool allowEmpty = false)
    {
        if (element.ValueKind != JsonValueKind.String
            || element.GetString() is not string value
            || (!allowEmpty && value.Length == 0)
            || value.Contains('\0', StringComparison.Ordinal))
        {
            throw InvalidSourceTypeExports(path, jsonPath,
                allowEmpty
                    ? "expected text without NUL"
                    : "expected nonempty text without NUL");
        }
        return value;
    }

    private static uint ReadSourceTypeExportsVersion(JsonElement element,
        string path, string jsonPath)
    {
        var token = element.ValueKind == JsonValueKind.Number
            ? element.GetRawText() : "";
        if (token.Length == 0
            || !uint.TryParse(token, NumberStyles.None,
                CultureInfo.InvariantCulture, out var value))
        {
            throw InvalidSourceTypeExports(path, jsonPath,
                "expected an exact unsigned integer token");
        }
        return value;
    }

    private static void ValidateSourceTypeExportsJsonText(
        JsonElement element, string path, string jsonPath)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    ValidateSourceTypeExportsUnicode(property.Name, path,
                        jsonPath);
                    if (!names.Add(property.Name))
                    {
                        throw InvalidSourceTypeExports(path,
                            jsonPath + "." + property.Name,
                            "duplicate decoded property name");
                    }
                    ValidateSourceTypeExportsJsonText(property.Value, path,
                        jsonPath + "." + property.Name);
                }
                break;
            }
            case JsonValueKind.Array:
            {
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    ValidateSourceTypeExportsJsonText(item, path,
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
                    throw InvalidSourceTypeExports(path, jsonPath,
                        "text contains an unpaired surrogate");
                }
                ValidateSourceTypeExportsUnicode(value, path, jsonPath);
                break;
            }
        }
    }

    private static void ValidateSourceTypeExportsUnicode(string value,
        string path, string jsonPath)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length
                    || !char.IsLowSurrogate(value[index + 1]))
                {
                    throw InvalidSourceTypeExports(path, jsonPath,
                        "text contains an unpaired surrogate");
                }
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                throw InvalidSourceTypeExports(path, jsonPath,
                    "text contains an unpaired surrogate");
            }
        }
    }

    private static FormatException InvalidSourceTypeExports(string path,
        string jsonPath, string message) =>
        new($"Invalid source type exports '{path}' at {jsonPath}: {message}.");
}
