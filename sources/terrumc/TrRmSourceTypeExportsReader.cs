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
using Terrum.CodeAnalysis.Text;

namespace Terrum.Tools;

internal sealed record TrRmSourceTypeExportsFile(
    int SchemaVersion,
    ImmutableArray<TrRmSourceSnapshotAssertion> Sources,
    ImmutableArray<TrRmSourceTypeExport> Exports);

internal static partial class Program
{
    private static readonly byte[] s_sourceTypeExportsUtf8Signature
        = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding s_sourceTypeExportsStrictUtf8
        = new(false, true);

    private static TrRmSourceTypeExportsFile
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
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw InvalidSourceTypeExports(path, "$",
                    "expected an object");
            }
            var version = ReadSourceTypeExportsVersion(
                PropertySourceTypeExports(document.RootElement,
                    "schemaVersion", path, "$"), path,
                "$.schemaVersion");
            var root = version switch {
                1 => RequireSourceTypeExportsObject(
                    document.RootElement, path, "$",
                    "schemaVersion", "exports"),
                2 => RequireSourceTypeExportsObject(
                    document.RootElement, path, "$", "schemaVersion",
                    "sources", "exports"),
                _ => throw InvalidSourceTypeExports(path,
                    "$.schemaVersion",
                    "only schema versions 1 and 2 are supported"),
            };
            var sources = version == 2
                ? ReadSourceTypeExportsSources(root, path)
                : ImmutableArray<TrRmSourceSnapshotAssertion>.Empty;

            var exportArray = PropertySourceTypeExports(root, "exports",
                path, "$");
            if (exportArray.ValueKind != JsonValueKind.Array)
            {
                throw InvalidSourceTypeExports(path, "$.exports",
                    "expected an array");
            }
            if (version == 2
                && exportArray.GetArrayLength() > 0xFF_FFFF)
            {
                throw InvalidSourceTypeExports(path, "$.exports",
                    "export count exceeds the TRRM table limit");
            }

            var exports = ImmutableArray.CreateBuilder<
                TrRmSourceTypeExport>(exportArray.GetArrayLength());
            var selectors = new HashSet<(string Namespace, string Name)>();
            var exportKeys = new HashSet<string>(StringComparer.Ordinal);
            var methodSpans = new HashSet<(int Source, int Start, int Length)>();
            var exportIndex = 0;
            foreach (var exportElement in exportArray.EnumerateArray())
            {
                var exportPath = $"$.exports[{exportIndex}]";
                var export = version == 1
                    ? ReadVersion1SourceTypeExport(exportElement, path,
                        exportPath, exportKeys)
                    : ReadVersion2SourceTypeExport(exportElement, path,
                        exportPath, sources.Length, exportKeys,
                        methodSpans);
                if (!selectors.Add((export.Namespace, export.Name)))
                {
                    throw InvalidSourceTypeExports(path, exportPath,
                        "duplicate declaration selector");
                }

                exports.Add(export);
                exportIndex++;
            }

            return new TrRmSourceTypeExportsFile(version, sources,
                exports.MoveToImmutable());
        }
        catch (JsonException exception)
        {
            throw InvalidSourceTypeExports(path,
                exception.Path ?? "$", exception.Message);
        }
    }

    private static ImmutableArray<TrRmSourceSnapshotAssertion>
        ReadSourceTypeExportsSources(JsonElement root, string path)
    {
        var sourceArray = PropertySourceTypeExports(root, "sources", path,
            "$");
        if (sourceArray.ValueKind != JsonValueKind.Array
            || sourceArray.GetArrayLength() is < 1 or > 256)
        {
            throw InvalidSourceTypeExports(path, "$.sources",
                "expected one through 256 source assertions");
        }

        var sources = ImmutableArray.CreateBuilder<
            TrRmSourceSnapshotAssertion>(sourceArray.GetArrayLength());
        var sourceIndex = 0;
        foreach (var sourceElement in sourceArray.EnumerateArray())
        {
            var sourcePath = $"$.sources[{sourceIndex}]";
            var sourceObject = RequireSourceTypeExportsObject(
                sourceElement, path, sourcePath, "path", "snapshotSha256");
            var operand = ReadSourceTypeExportsText(
                PropertySourceTypeExports(sourceObject, "path", path,
                    sourcePath), path, sourcePath + ".path");
            var digest = ReadSourceTypeExportsText(
                PropertySourceTypeExports(sourceObject, "snapshotSha256",
                    path, sourcePath), path,
                sourcePath + ".snapshotSha256");
            if (digest.Length != 64
                || digest.Any(static character =>
                    character is not (>= '0' and <= '9'
                        or >= 'a' and <= 'f')))
            {
                throw InvalidSourceTypeExports(path,
                    sourcePath + ".snapshotSha256",
                    "expected 64 lowercase hexadecimal digits");
            }

            sources.Add(new TrRmSourceSnapshotAssertion(operand, digest));
            sourceIndex++;
        }
        return sources.MoveToImmutable();
    }

    private static TrRmSourceTypeExport ReadVersion1SourceTypeExport(
        JsonElement element, string path, string jsonPath,
        HashSet<string> exportKeys)
    {
        var exportObject = RequireSourceTypeExportsObject(element, path,
            jsonPath, "namespace", "name", "exportKey", "fieldExports");
        var @namespace = ReadSourceTypeExportsText(
            PropertySourceTypeExports(exportObject, "namespace", path,
                jsonPath), path, jsonPath + ".namespace", allowEmpty: true);
        var name = ReadSourceTypeExportsTypeName(exportObject, path,
            jsonPath);
        var exportKey = ReadSourceTypeExportsText(
            PropertySourceTypeExports(exportObject, "exportKey", path,
                jsonPath), path, jsonPath + ".exportKey");
        if (!exportKeys.Add(exportKey))
        {
            throw InvalidSourceTypeExports(path, jsonPath + ".exportKey",
                "duplicate ExportKey");
        }

        return new TrRmSourceTypeExport(@namespace, name, exportKey) {
            FieldExports = ReadSourceTypeFieldExports(exportObject, path,
                jsonPath, exportKeys, validateExportKey: false),
        };
    }

    private static TrRmSourceTypeExport ReadVersion2SourceTypeExport(
        JsonElement element, string path, string jsonPath, int sourceCount,
        HashSet<string> exportKeys,
        HashSet<(int Source, int Start, int Length)> methodSpans)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw InvalidSourceTypeExports(path, jsonPath,
                "expected an object");
        }
        var kind = ReadSourceTypeExportsText(
            PropertySourceTypeExports(element, "kind", path, jsonPath),
            path, jsonPath + ".kind");
        var expectedProperties = kind switch {
            "struct" => new[] {
                "kind", "namespace", "name", "exportKey", "fieldExports",
            },
            "foreign-native-interface" => new[] {
                "kind", "namespace", "name", "exportKey",
                "interfaceMethodExports",
            },
            _ => throw InvalidSourceTypeExports(path,
                jsonPath + ".kind", "unknown declaration kind"),
        };
        var exportObject = RequireSourceTypeExportsObject(element, path,
            jsonPath, expectedProperties);
        var @namespace = ReadSourceTypeExportsText(
            PropertySourceTypeExports(exportObject, "namespace", path,
                jsonPath), path, jsonPath + ".namespace", allowEmpty: true);
        var name = ReadSourceTypeExportsTypeName(exportObject, path,
            jsonPath);
        var exportKey = ReadSourceTypeExportKey(
            PropertySourceTypeExports(exportObject, "exportKey", path,
                jsonPath), path, jsonPath + ".exportKey");
        if (!exportKeys.Add(exportKey))
        {
            throw InvalidSourceTypeExports(path, jsonPath + ".exportKey",
                "duplicate ExportKey");
        }

        if (kind == "struct")
        {
            return new TrRmSourceTypeExport(@namespace, name, exportKey) {
                FieldExports = ReadSourceTypeFieldExports(exportObject,
                    path, jsonPath, exportKeys, validateExportKey: true),
            };
        }

        return new TrRmSourceTypeExport(@namespace, name, exportKey) {
            GenericArity = 0,
            InterfaceMethodExports =
                ReadSourceInterfaceMethodExports(exportObject, path,
                    jsonPath, sourceCount, exportKeys, methodSpans),
        };
    }

    private static string ReadSourceTypeExportsTypeName(
        JsonElement exportObject, string path, string jsonPath)
    {
        var name = ReadSourceTypeExportsText(
            PropertySourceTypeExports(exportObject, "name", path,
                jsonPath), path, jsonPath + ".name");
        if (name.Contains('.', StringComparison.Ordinal))
        {
            throw InvalidSourceTypeExports(path, jsonPath + ".name",
                "expected a simple declaration name");
        }
        return name;
    }

    private static ImmutableArray<TrRmSourceFieldExport>
        ReadSourceTypeFieldExports(JsonElement exportObject, string path,
            string jsonPath, HashSet<string> exportKeys,
            bool validateExportKey)
    {
        var fieldArray = PropertySourceTypeExports(exportObject,
            "fieldExports", path, jsonPath);
        if (fieldArray.ValueKind != JsonValueKind.Array
            || (validateExportKey
                && fieldArray.GetArrayLength() > 0xFF_FFFF))
        {
            throw InvalidSourceTypeExports(path,
                jsonPath + ".fieldExports",
                validateExportKey
                    ? "expected an array within the TRRM FieldDef limit"
                    : "expected an array");
        }

        var fieldExports = ImmutableArray.CreateBuilder<
            TrRmSourceFieldExport>(fieldArray.GetArrayLength());
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        var fieldIndex = 0;
        foreach (var fieldElement in fieldArray.EnumerateArray())
        {
            var fieldPath = jsonPath + ".fieldExports["
                + fieldIndex.ToString(CultureInfo.InvariantCulture) + "]";
            var fieldObject = RequireSourceTypeExportsObject(
                fieldElement, path, fieldPath, "name", "exportKey");
            var fieldName = ReadSourceTypeExportsText(
                PropertySourceTypeExports(fieldObject, "name", path,
                    fieldPath), path, fieldPath + ".name");
            if (fieldName.Contains('.', StringComparison.Ordinal)
                || !fieldNames.Add(fieldName))
            {
                throw InvalidSourceTypeExports(path,
                    fieldPath + ".name",
                    "expected a unique direct field name");
            }

            var fieldExportKey = validateExportKey
                ? ReadSourceTypeExportKey(
                    PropertySourceTypeExports(fieldObject, "exportKey",
                        path, fieldPath), path,
                    fieldPath + ".exportKey")
                : ReadSourceTypeExportsText(
                    PropertySourceTypeExports(fieldObject, "exportKey",
                        path, fieldPath), path,
                    fieldPath + ".exportKey");
            if (!exportKeys.Add(fieldExportKey))
            {
                throw InvalidSourceTypeExports(path,
                    fieldPath + ".exportKey", "duplicate ExportKey");
            }

            fieldExports.Add(new TrRmSourceFieldExport(fieldName,
                fieldExportKey));
            fieldIndex++;
        }
        return fieldExports.MoveToImmutable();
    }

    private static ImmutableArray<TrRmSourceInterfaceMethodExport>
        ReadSourceInterfaceMethodExports(JsonElement exportObject,
            string path, string jsonPath, int sourceCount,
            HashSet<string> exportKeys,
            HashSet<(int Source, int Start, int Length)> methodSpans)
    {
        var methodArray = PropertySourceTypeExports(exportObject,
            "interfaceMethodExports", path, jsonPath);
        if (methodArray.ValueKind != JsonValueKind.Array
            || methodArray.GetArrayLength() is < 1 or > 0xFF_FFFF)
        {
            throw InvalidSourceTypeExports(path,
                jsonPath + ".interfaceMethodExports",
                "expected a nonempty array within the InterfaceMethod limit");
        }

        var methods = ImmutableArray.CreateBuilder<
            TrRmSourceInterfaceMethodExport>(
                methodArray.GetArrayLength());
        var methodIndex = 0;
        foreach (var methodElement in methodArray.EnumerateArray())
        {
            var methodPath = jsonPath + ".interfaceMethodExports["
                + methodIndex.ToString(CultureInfo.InvariantCulture) + "]";
            var methodObject = RequireSourceTypeExportsObject(methodElement,
                path, methodPath, "sourceInputOrdinal", "declarationStart",
                "declarationLength", "exportKey");
            var sourceInputOrdinal = ReadSourceTypeExportsInteger(
                PropertySourceTypeExports(methodObject,
                    "sourceInputOrdinal", path, methodPath), path,
                methodPath + ".sourceInputOrdinal", allowZero: true);
            var declarationStart = ReadSourceTypeExportsInteger(
                PropertySourceTypeExports(methodObject,
                    "declarationStart", path, methodPath), path,
                methodPath + ".declarationStart", allowZero: true);
            var declarationLength = ReadSourceTypeExportsInteger(
                PropertySourceTypeExports(methodObject,
                    "declarationLength", path, methodPath), path,
                methodPath + ".declarationLength", allowZero: false);
            if (sourceInputOrdinal >= sourceCount)
            {
                throw InvalidSourceTypeExports(path,
                    methodPath + ".sourceInputOrdinal",
                    "source ordinal is outside the asserted source list");
            }
            if ((long)declarationStart + declarationLength > int.MaxValue)
            {
                throw InvalidSourceTypeExports(path,
                    methodPath + ".declarationLength",
                    "declaration span end exceeds Int32.MaxValue");
            }
            if (!methodSpans.Add((sourceInputOrdinal, declarationStart,
                    declarationLength)))
            {
                throw InvalidSourceTypeExports(path, methodPath,
                    "duplicate interface member span");
            }

            var exportKey = ReadSourceTypeExportKey(
                PropertySourceTypeExports(methodObject, "exportKey", path,
                    methodPath), path, methodPath + ".exportKey");
            if (!exportKeys.Add(exportKey))
            {
                throw InvalidSourceTypeExports(path,
                    methodPath + ".exportKey", "duplicate ExportKey");
            }

            methods.Add(new TrRmSourceInterfaceMethodExport(
                sourceInputOrdinal, new TextSpan(declarationStart,
                    declarationLength), exportKey));
            methodIndex++;
        }
        return methods.MoveToImmutable();
    }

    private static string ReadSourceTypeExportKey(JsonElement element,
        string path, string jsonPath)
    {
        var key = ReadSourceTypeExportsText(element, path, jsonPath);
        try
        {
            if (s_sourceTypeExportsStrictUtf8.GetByteCount(key) > 0xFF_FFFF)
            {
                throw InvalidSourceTypeExports(path, jsonPath,
                    "ExportKey exceeds the Standard String limit");
            }
        }
        catch (EncoderFallbackException)
        {
            throw InvalidSourceTypeExports(path, jsonPath,
                "ExportKey contains malformed Unicode");
        }
        return key;
    }

    private static int ReadSourceTypeExportsInteger(JsonElement element,
        string path, string jsonPath, bool allowZero)
    {
        var token = element.ValueKind == JsonValueKind.Number
            ? element.GetRawText() : "";
        if (token.Length == 0
            || !int.TryParse(token, NumberStyles.None,
                CultureInfo.InvariantCulture, out var value)
            || value < (allowZero ? 0 : 1))
        {
            throw InvalidSourceTypeExports(path, jsonPath,
                allowZero
                    ? "expected an unsigned Int32 integer token"
                    : "expected a positive unsigned Int32 integer token");
        }
        return value;
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

    private static int ReadSourceTypeExportsVersion(JsonElement element,
        string path, string jsonPath)
    {
        var token = element.ValueKind == JsonValueKind.Number
            ? element.GetRawText() : "";
        if (token is not ("1" or "2"))
        {
            throw InvalidSourceTypeExports(path, jsonPath,
                "only exact version tokens 1 and 2 are supported");
        }
        return token == "1" ? 1 : 2;
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
