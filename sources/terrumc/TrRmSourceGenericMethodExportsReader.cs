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

internal sealed record TrRmSourceSnapshotAssertion(
    string Path, string SnapshotSha256);

internal sealed record TrRmSourceGenericMethodSelector(
    int SourceInputOrdinal, int DeclarationStart, int DeclarationLength,
    string ExportKey);

internal sealed record TrRmSourceInterfaceMethodSelector(
    int SourceInputOrdinal, int DeclarationStart, int DeclarationLength,
    string ExportKey);

internal sealed record TrRmSourceInterfaceTypeSelector(
    string Namespace, string Name, int GenericArity, string ExportKey,
    ImmutableArray<TrRmSourceInterfaceMethodSelector> InterfaceMethodExports);

internal sealed record TrRmSourceGenericMethodExportsFile(
    int SchemaVersion,
    ImmutableArray<TrRmSourceSnapshotAssertion> Sources,
    ImmutableArray<TrRmSourceGenericMethodSelector> Exports,
    ImmutableArray<TrRmSourceInterfaceTypeSelector> TypeExports);

internal static class TrRmSourceGenericMethodExportsReader
{
    private static readonly byte[] s_utf8Signature = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);
    private static readonly UTF8Encoding s_exportKeyUtf8
        = new(false, true);

    internal static TrRmSourceGenericMethodExportsFile Read(string path)
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
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(path, "$", "expected an object");
            }
            var schemaVersion = ReadInteger(Property(
                document.RootElement, "schemaVersion", path, "$"),
                path, "$.schemaVersion", allowZero: false);
            var root = schemaVersion switch {
                1 => RequireObject(document.RootElement, path, "$",
                    "schemaVersion", "sources", "exports"),
                3 => RequireObject(document.RootElement, path, "$",
                    "schemaVersion", "sources", "typeExports",
                    "genericMethodExports"),
                _ => throw Invalid(path, "$.schemaVersion",
                    "only schema versions 1 and 3 are supported"),
            };
            if (schemaVersion == 3)
            {
                return ReadVersion3(root, path);
            }

            var sourceArray = Property(root, "sources", path, "$");
            if (sourceArray.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(path, "$.sources", "expected an array");
            }
            if (sourceArray.GetArrayLength() > 256)
            {
                throw Invalid(path, "$.sources",
                    "source count exceeds the compile-trrm limit");
            }

            var sources = ImmutableArray.CreateBuilder<
                TrRmSourceSnapshotAssertion>(sourceArray.GetArrayLength());
            var sourceIndex = 0;
            foreach (var sourceElement in sourceArray.EnumerateArray())
            {
                var sourcePath = $"$.sources[{sourceIndex}]";
                var sourceObject = RequireObject(sourceElement, path,
                    sourcePath, "path", "snapshotSha256");
                var sourceOperand = ReadText(Property(sourceObject, "path",
                    path, sourcePath), path, sourcePath + ".path");
                var digest = ReadDigest(Property(sourceObject,
                    "snapshotSha256", path, sourcePath), path,
                    sourcePath + ".snapshotSha256");
                sources.Add(new TrRmSourceSnapshotAssertion(sourceOperand,
                    digest));
                sourceIndex++;
            }

            var exportArray = Property(root, "exports", path, "$");
            if (exportArray.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(path, "$.exports", "expected an array");
            }
            if (exportArray.GetArrayLength() > 0xFF_FFFF)
            {
                throw Invalid(path, "$.exports",
                    "export count exceeds the TRRM table limit");
            }

            var exports = ImmutableArray.CreateBuilder<
                TrRmSourceGenericMethodSelector>(
                    exportArray.GetArrayLength());
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var exportIndex = 0;
            foreach (var exportElement in exportArray.EnumerateArray())
            {
                var exportPath = $"$.exports[{exportIndex}]";
                var exportObject = RequireObject(exportElement, path,
                    exportPath, "sourceInputOrdinal", "declarationStart",
                    "declarationLength", "exportKey");
                var sourceInputOrdinal = ReadInteger(Property(exportObject,
                    "sourceInputOrdinal", path, exportPath), path,
                    exportPath + ".sourceInputOrdinal", allowZero: true);
                if (sourceInputOrdinal >= sourceArray.GetArrayLength())
                {
                    throw Invalid(path, exportPath + ".sourceInputOrdinal",
                        "source ordinal is outside the asserted source list");
                }
                var declarationStart = ReadInteger(Property(exportObject,
                    "declarationStart", path, exportPath), path,
                    exportPath + ".declarationStart", allowZero: true);
                var declarationLength = ReadInteger(Property(exportObject,
                    "declarationLength", path, exportPath), path,
                    exportPath + ".declarationLength", allowZero: false);
                if ((long)declarationStart + declarationLength
                    > int.MaxValue)
                {
                    throw Invalid(path, exportPath + ".declarationLength",
                        "declaration span end exceeds Int32.MaxValue");
                }

                var exportKey = ReadText(Property(exportObject, "exportKey",
                    path, exportPath), path, exportPath + ".exportKey");
                try
                {
                    if (s_exportKeyUtf8.GetByteCount(exportKey) > 0xFF_FFFF)
                    {
                        throw Invalid(path, exportPath + ".exportKey",
                            "export key exceeds the Standard String limit");
                    }
                }
                catch (EncoderFallbackException)
                {
                    throw Invalid(path, exportPath + ".exportKey",
                        "export key contains malformed Unicode");
                }
                if (!keys.Add(exportKey))
                {
                    throw Invalid(path, exportPath + ".exportKey",
                        "duplicate export key");
                }

                exports.Add(new TrRmSourceGenericMethodSelector(
                    sourceInputOrdinal, declarationStart,
                    declarationLength, exportKey));
                exportIndex++;
            }

            return new TrRmSourceGenericMethodExportsFile(1,
                sources.MoveToImmutable(), exports.MoveToImmutable(), []);
        }
        catch (JsonException exception)
        {
            throw Invalid(path, exception.Path ?? "$", exception.Message);
        }
    }

    private static TrRmSourceGenericMethodExportsFile ReadVersion3(
        JsonElement root, string path)
    {
        var sourceArray = Property(root, "sources", path, "$");
        if (sourceArray.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(path, "$.sources", "expected an array");
        }
        if (sourceArray.GetArrayLength() > 256)
        {
            throw Invalid(path, "$.sources",
                "source count exceeds the compile-trrm limit");
        }

        var sources = ImmutableArray.CreateBuilder<
            TrRmSourceSnapshotAssertion>(sourceArray.GetArrayLength());
        var sourceIndex = 0;
        foreach (var sourceElement in sourceArray.EnumerateArray())
        {
            var sourcePath = $"$.sources[{sourceIndex}]";
            var sourceObject = RequireObject(sourceElement, path,
                sourcePath, "path", "snapshotSha256");
            var sourceOperand = ReadText(Property(sourceObject, "path",
                path, sourcePath), path, sourcePath + ".path");
            var digest = ReadDigest(Property(sourceObject,
                "snapshotSha256", path, sourcePath), path,
                sourcePath + ".snapshotSha256");
            sources.Add(new TrRmSourceSnapshotAssertion(sourceOperand,
                digest));
            sourceIndex++;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var typeArray = Property(root, "typeExports", path, "$");
        if (typeArray.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(path, "$.typeExports", "expected an array");
        }
        if (typeArray.GetArrayLength() > 0xFF_FFFF)
        {
            throw Invalid(path, "$.typeExports",
                "type export count exceeds the TRRM table limit");
        }

        var typeExports = ImmutableArray.CreateBuilder<
            TrRmSourceInterfaceTypeSelector>(typeArray.GetArrayLength());
        var typeKeys = new HashSet<(string Namespace, string Name, int Arity)>();
        var typeIndex = 0;
        foreach (var typeElement in typeArray.EnumerateArray())
        {
            var typePath = $"$.typeExports[{typeIndex}]";
            var typeObject = RequireObject(typeElement, path, typePath,
                "namespace", "name", "genericArity", "exportKey",
                "interfaceMethodExports");
            var typeNamespace = ReadNamespace(Property(typeObject,
                "namespace", path, typePath), path,
                typePath + ".namespace");
            var typeName = ReadText(Property(typeObject, "name", path,
                typePath), path, typePath + ".name");
            var genericArity = ReadInteger(Property(typeObject,
                "genericArity", path, typePath), path,
                typePath + ".genericArity", allowZero: true);
            var typeExportKey = ReadText(Property(typeObject, "exportKey",
                path, typePath), path, typePath + ".exportKey");
            AddExportKey(keys, typeExportKey, path,
                typePath + ".exportKey");
            if (!typeKeys.Add((typeNamespace, typeName, genericArity)))
            {
                throw Invalid(path, typePath,
                    "duplicate source interface type selector");
            }

            var memberArray = Property(typeObject,
                "interfaceMethodExports", path, typePath);
            if (memberArray.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(path,
                    typePath + ".interfaceMethodExports",
                    "expected an array");
            }
            if (memberArray.GetArrayLength() > 0xFF_FFFF)
            {
                throw Invalid(path,
                    typePath + ".interfaceMethodExports",
                    "interface method export count exceeds the TRRM table limit");
            }

            var methodExports = ImmutableArray.CreateBuilder<
                TrRmSourceInterfaceMethodSelector>(
                    memberArray.GetArrayLength());
            var methodIndex = 0;
            foreach (var methodElement in memberArray.EnumerateArray())
            {
                var methodPath = typePath + ".interfaceMethodExports["
                    + methodIndex.ToString(CultureInfo.InvariantCulture)
                    + "]";
                var methodObject = RequireObject(methodElement, path,
                    methodPath, "sourceInputOrdinal", "declarationStart",
                    "declarationLength", "exportKey");
                var sourceInputOrdinal = ReadInteger(Property(methodObject,
                    "sourceInputOrdinal", path, methodPath), path,
                    methodPath + ".sourceInputOrdinal", allowZero: true);
                if (sourceInputOrdinal >= sourceArray.GetArrayLength())
                {
                    throw Invalid(path,
                        methodPath + ".sourceInputOrdinal",
                        "source ordinal is outside the asserted source list");
                }
                var declarationStart = ReadInteger(Property(methodObject,
                    "declarationStart", path, methodPath), path,
                    methodPath + ".declarationStart", allowZero: true);
                var declarationLength = ReadInteger(Property(methodObject,
                    "declarationLength", path, methodPath), path,
                    methodPath + ".declarationLength", allowZero: false);
                if ((long)declarationStart + declarationLength
                    > int.MaxValue)
                {
                    throw Invalid(path,
                        methodPath + ".declarationLength",
                        "declaration span end exceeds Int32.MaxValue");
                }
                var methodExportKey = ReadText(Property(methodObject,
                    "exportKey", path, methodPath), path,
                    methodPath + ".exportKey");
                AddExportKey(keys, methodExportKey, path,
                    methodPath + ".exportKey");
                methodExports.Add(new TrRmSourceInterfaceMethodSelector(
                    sourceInputOrdinal, declarationStart,
                    declarationLength, methodExportKey));
                methodIndex++;
            }

            typeExports.Add(new TrRmSourceInterfaceTypeSelector(
                typeNamespace, typeName, genericArity, typeExportKey,
                methodExports.MoveToImmutable()));
            typeIndex++;
        }

        var exportArray = Property(root, "genericMethodExports", path, "$");
        if (exportArray.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(path, "$.genericMethodExports",
                "expected an array");
        }
        if (exportArray.GetArrayLength() > 0xFF_FFFF)
        {
            throw Invalid(path, "$.genericMethodExports",
                "export count exceeds the TRRM table limit");
        }

        var exports = ImmutableArray.CreateBuilder<
            TrRmSourceGenericMethodSelector>(
                exportArray.GetArrayLength());
        var exportIndex = 0;
        foreach (var exportElement in exportArray.EnumerateArray())
        {
            var exportPath = $"$.genericMethodExports[{exportIndex}]";
            var exportObject = RequireObject(exportElement, path,
                exportPath, "sourceInputOrdinal", "declarationStart",
                "declarationLength", "exportKey");
            var sourceInputOrdinal = ReadInteger(Property(exportObject,
                "sourceInputOrdinal", path, exportPath), path,
                exportPath + ".sourceInputOrdinal", allowZero: true);
            if (sourceInputOrdinal >= sourceArray.GetArrayLength())
            {
                throw Invalid(path, exportPath + ".sourceInputOrdinal",
                    "source ordinal is outside the asserted source list");
            }
            var declarationStart = ReadInteger(Property(exportObject,
                "declarationStart", path, exportPath), path,
                exportPath + ".declarationStart", allowZero: true);
            var declarationLength = ReadInteger(Property(exportObject,
                "declarationLength", path, exportPath), path,
                exportPath + ".declarationLength", allowZero: false);
            if ((long)declarationStart + declarationLength > int.MaxValue)
            {
                throw Invalid(path, exportPath + ".declarationLength",
                    "declaration span end exceeds Int32.MaxValue");
            }

            var exportKey = ReadText(Property(exportObject, "exportKey",
                path, exportPath), path, exportPath + ".exportKey");
            AddExportKey(keys, exportKey, path,
                exportPath + ".exportKey");
            exports.Add(new TrRmSourceGenericMethodSelector(
                sourceInputOrdinal, declarationStart, declarationLength,
                exportKey));
            exportIndex++;
        }

        return new TrRmSourceGenericMethodExportsFile(3,
            sources.MoveToImmutable(), exports.MoveToImmutable(),
            typeExports.MoveToImmutable());
    }

    private static void AddExportKey(HashSet<string> keys, string key,
        string path, string jsonPath)
    {
        try
        {
            if (s_exportKeyUtf8.GetByteCount(key) > 0xFF_FFFF)
            {
                throw Invalid(path, jsonPath,
                    "export key exceeds the Standard String limit");
            }
        }
        catch (EncoderFallbackException)
        {
            throw Invalid(path, jsonPath,
                "export key contains malformed Unicode");
        }

        if (!keys.Add(key))
        {
            throw Invalid(path, jsonPath, "duplicate export key");
        }
    }

    private static string ReadNamespace(JsonElement element, string path,
        string jsonPath)
    {
        if (element.ValueKind != JsonValueKind.String
            || element.GetString() is not string value
            || value.Contains('\0', StringComparison.Ordinal))
        {
            throw Invalid(path, jsonPath,
                "expected text without NUL");
        }
        return value;
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

    private static int ReadInteger(JsonElement element, string path,
        string jsonPath, bool allowZero)
    {
        var token = element.ValueKind == JsonValueKind.Number
            ? element.GetRawText() : "";
        if (token.Length == 0
            || !int.TryParse(token, NumberStyles.None,
                CultureInfo.InvariantCulture, out var value)
            || value < (allowZero ? 0 : 1))
        {
            throw Invalid(path, jsonPath,
                allowZero
                    ? "expected an exact unsigned Int32 integer token"
                    : "expected a positive exact Int32 integer token");
        }
        return value;
    }

    private static string ReadText(JsonElement element, string path,
        string jsonPath)
    {
        if (element.ValueKind != JsonValueKind.String
            || element.GetString() is not string value
            || value.Length == 0
            || value.Contains('\0', StringComparison.Ordinal))
        {
            throw Invalid(path, jsonPath,
                "expected nonempty text without NUL");
        }
        return value;
    }

    private static string ReadDigest(JsonElement element, string path,
        string jsonPath)
    {
        var digest = ReadText(element, path, jsonPath);
        if (digest.Length != 64 || digest.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw Invalid(path, jsonPath,
                "expected 64 lowercase ASCII hexadecimal digits");
        }
        return digest;
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
                        throw Invalid(path,
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
        => new($"Invalid source generic method exports '{path}' at {jsonPath}: {message}.");
}
