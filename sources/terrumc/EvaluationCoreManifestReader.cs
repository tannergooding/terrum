// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Semantics;
using static Terrum.Tools.NativeCoreManifestReader;

namespace Terrum.Tools;

internal sealed record EvaluationCoreManifestDefinition(
    string CoreAuthorityIdentity,
    CoreEvaluationFormIdentitySelection Selection)
{
    internal string UsingModuleIdentity => Selection.UsingModuleIdentity;

    internal void ValidateRuntimePolicy(BoundRuntimePolicy runtimePolicy,
        string path)
    {
        if (Selection.LocalSchemaDeclarations!.Origin.RuntimePolicy
            != (uint)runtimePolicy)
        {
            throw Invalid(path,
                "$.localSchemaDeclarations.origin.runtimePolicy",
                "the local catalog runtime policy must match its using Module compilation");
        }
    }
}

internal static class EvaluationCoreManifestReader
{
    private const string SchemaIdentity = "terrumc.evaluation-core-manifest/1";

    internal static EvaluationCoreManifestDefinition ReadDefinition(string path)
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
                "pureSchema", "constEvalSchema", "localSchemaDeclarations");
            if (ReadText(Property(root, "schema", path, "$"), path,
                    "$.schema") != SchemaIdentity)
            {
                throw Invalid(path, "$.schema",
                    $"expected exactly '{SchemaIdentity}'");
            }

            var authority = ReadText(Property(root, "coreAuthorityIdentity",
                path, "$"), path, "$.coreAuthorityIdentity");
            var usingModule = ReadText(Property(root, "usingModuleIdentity",
                path, "$"), path, "$.usingModuleIdentity");
            var pure = ReadSelector(Property(root, "pureSchema", path, "$"),
                path, "$.pureSchema");
            var constEval = ReadSelector(Property(root, "constEvalSchema",
                path, "$"), path, "$.constEvalSchema");
            var localElement = Property(root, "localSchemaDeclarations",
                path, "$");
            var local = localElement.ValueKind == JsonValueKind.Null
                ? null : ReadCatalog(localElement, path,
                    "$.localSchemaDeclarations", usingModule);
            if (pure is null && constEval is null)
            {
                throw Invalid(path, "$",
                    "at least one evaluation-form schema must be selected");
            }

            var selected = new HashSet<CoreEvaluationFormSchemaDeclaration>(
                ReferenceEqualityComparer.Instance);
            ValidateSelector(pure, "$.pureSchema");
            ValidateSelector(constEval, "$.constEvalSchema");
            if (local is not null
                && selected.Count != local.Declarations.Length)
            {
                throw Invalid(path, "$.localSchemaDeclarations",
                    "each local declaration must be selected by exactly one role");
            }

            return new EvaluationCoreManifestDefinition(authority,
                new CoreEvaluationFormIdentitySelection(usingModule, pure,
                    constEval, local));

            void ValidateSelector(CoreEvaluationFormSchemaSelector? selector,
                string selectorPath)
            {
                if (selector is not CoreEvaluationFormSchemaSelector value)
                {
                    return;
                }
                if (!StringComparer.Ordinal.Equals(value.ProviderIdentity,
                    usingModule))
                {
                    throw Invalid(path, selectorPath,
                        "external evaluation schemas require an actual checked ordinary reference export; this CLI route supplies none");
                }
                if (local is null
                    || !local.TryGetDeclaration(value.ExportKey,
                        out var declaration))
                {
                    throw Invalid(path, selectorPath,
                        "the selected local schema requires its actual catalog export");
                }
                if (!selected.Add(declaration))
                {
                    throw Invalid(path, selectorPath,
                        "Pure and ConstEval must select distinct actual declarations");
                }
            }
        }
        catch (JsonException exception)
        {
            throw Invalid(path, exception.Path ?? "$", exception.Message);
        }
        catch (ArgumentException exception)
        {
            throw Invalid(path, "$", exception.Message);
        }
    }

    private static CoreEvaluationFormSchemaSelector? ReadSelector(
        JsonElement element, string manifestPath, string path)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var value = RequireObject(element, manifestPath, path,
            "providerIdentity", "exportKey");
        return new CoreEvaluationFormSchemaSelector(
            ReadText(Property(value, "providerIdentity", manifestPath,
                path), manifestPath, path + ".providerIdentity"),
            ReadText(Property(value, "exportKey", manifestPath, path),
                manifestPath, path + ".exportKey"));
    }

    private static CoreEvaluationFormSchemaDeclarations ReadCatalog(
        JsonElement element, string manifestPath, string path,
        string usingModule)
    {
        var value = RequireObject(element, manifestPath, path, "origin",
            "declarations", "exports");
        var origin = ReadOrigin(Property(value, "origin", manifestPath,
            path), manifestPath, path + ".origin");
        if (!StringComparer.Ordinal.Equals(origin.ModuleIdentity,
            usingModule))
        {
            throw Invalid(manifestPath, path + ".origin.moduleIdentity",
                "the local catalog must belong to the exact using Module");
        }

        var declarationElements = Property(value, "declarations",
            manifestPath, path);
        if (declarationElements.ValueKind != JsonValueKind.Array
            || declarationElements.GetArrayLength() == 0)
        {
            throw Invalid(manifestPath, path + ".declarations",
                "expected a nonempty declaration array");
        }

        var declarations = ImmutableArray.CreateBuilder<
            CoreEvaluationFormSchemaDeclaration>(
                declarationElements.GetArrayLength());
        foreach (var item in declarationElements.EnumerateArray())
        {
            declarations.Add(ReadDeclaration(item, manifestPath,
                $"{path}.declarations[{declarations.Count}]"));
        }

        var exportElements = Property(value, "exports", manifestPath, path);
        if (exportElements.ValueKind != JsonValueKind.Array
            || exportElements.GetArrayLength() == 0)
        {
            throw Invalid(manifestPath, path + ".exports",
                "expected a nonempty export array");
        }
        var exports = ImmutableArray.CreateBuilder<
            KeyValuePair<string, CoreEvaluationFormSchemaDeclaration>>(
                exportElements.GetArrayLength());
        foreach (var item in exportElements.EnumerateArray())
        {
            var exportPath = $"{path}.exports[{exports.Count}]";
            var export = RequireObject(item, manifestPath, exportPath,
                "exportKey", "declarationIndex");
            var key = ReadText(Property(export, "exportKey", manifestPath,
                exportPath), manifestPath, exportPath + ".exportKey");
            var index = ReadUInt32(Property(export, "declarationIndex",
                manifestPath, exportPath), manifestPath,
                exportPath + ".declarationIndex");
            if (index >= (uint)declarations.Count)
            {
                throw Invalid(manifestPath,
                    exportPath + ".declarationIndex",
                    "declaration index is outside the local catalog");
            }
            exports.Add(new(key, declarations[(int)index]));
        }

        try
        {
            return CoreEvaluationFormSchemaDeclarations.Create(origin,
                declarations.MoveToImmutable(), exports.MoveToImmutable());
        }
        catch (ArgumentException exception)
        {
            throw Invalid(manifestPath, path, exception.Message);
        }
    }

    private static CoreProviderModuleOrigin ReadOrigin(JsonElement element,
        string manifestPath, string path)
    {
        var value = RequireObject(element, manifestPath, path,
            "moduleIdentity", "revisionIdentity", "schema",
            "governingSemanticEdition", "targetRequirement", "runtimePolicy");
        var schema = RequireObject(Property(value, "schema", manifestPath,
            path), manifestPath, path + ".schema", "identity", "revision");
        var origin = new CoreProviderModuleOrigin(
            ReadText(Property(value, "moduleIdentity", manifestPath, path),
                manifestPath, path + ".moduleIdentity"),
            ReadText(Property(value, "revisionIdentity", manifestPath, path),
                manifestPath, path + ".revisionIdentity"),
            new CoreProviderSchemaSelection(
                ReadText(Property(schema, "identity", manifestPath,
                    path + ".schema"), manifestPath,
                    path + ".schema.identity"),
                ReadUInt32(Property(schema, "revision", manifestPath,
                    path + ".schema"), manifestPath,
                    path + ".schema.revision")),
            ReadUInt32(Property(value, "governingSemanticEdition",
                manifestPath, path), manifestPath,
                path + ".governingSemanticEdition"),
            ReadUInt32(Property(value, "targetRequirement", manifestPath,
                path), manifestPath, path + ".targetRequirement"),
            ReadUInt32(Property(value, "runtimePolicy", manifestPath, path),
                manifestPath, path + ".runtimePolicy"));
        if (origin.Schema.Revision == 0
            || origin.GoverningSemanticEdition != 1
            || origin.TargetRequirement > 1 || origin.RuntimePolicy > 1)
        {
            throw Invalid(manifestPath, path,
                "catalog schema revision, semantic edition, target or runtime policy is unsupported");
        }
        return origin;
    }

    private static CoreEvaluationFormSchemaDeclaration ReadDeclaration(
        JsonElement element, string manifestPath, string path)
    {
        var value = RequireObject(element, manifestPath, path,
            "qualifiedName", "genericArity", "argumentCount", "targets",
            "isPublic", "isPrimary", "isPrimaryTargetOnly", "isRepeatable");
        var name = ReadQualifiedName(Property(value, "qualifiedName",
            manifestPath, path), manifestPath, path + ".qualifiedName");
        var arity = ReadUInt32(Property(value, "genericArity", manifestPath,
            path), manifestPath, path + ".genericArity");
        var arguments = ReadUInt32(Property(value, "argumentCount",
            manifestPath, path), manifestPath, path + ".argumentCount");
        var isPublic = ReadBoolean(Property(value, "isPublic", manifestPath,
            path), manifestPath, path + ".isPublic");
        var isPrimary = ReadBoolean(Property(value, "isPrimary", manifestPath,
            path), manifestPath, path + ".isPrimary");
        var primaryTarget = ReadBoolean(Property(value, "isPrimaryTargetOnly",
            manifestPath, path), manifestPath, path + ".isPrimaryTargetOnly");
        var repeatable = ReadBoolean(Property(value, "isRepeatable",
            manifestPath, path), manifestPath, path + ".isRepeatable");
        if (arity != 0 || arguments != 0 || !isPublic || !isPrimary
            || !primaryTarget || repeatable)
        {
            throw Invalid(manifestPath, path,
                "expected the complete public primary zero-argument nonrepeatable evaluation schema");
        }

        var targetElements = Property(value, "targets", manifestPath, path);
        if (targetElements.ValueKind != JsonValueKind.Array
            || targetElements.GetArrayLength() != 3)
        {
            throw Invalid(manifestPath, path + ".targets",
                "expected exactly the three evaluation schema target categories");
        }
        var targets = ImmutableArray.CreateBuilder<
            CoreEvaluationFormTargetKind>(3);
        var observed = new HashSet<CoreEvaluationFormTargetKind>();
        foreach (var item in targetElements.EnumerateArray())
        {
            var targetPath = $"{path}.targets[{targets.Count}]";
            var target = ReadText(item, manifestPath, targetPath) switch {
                "ordinary-named-callable"
                    => CoreEvaluationFormTargetKind.OrdinaryNamedCallable,
                "individual-accessor"
                    => CoreEvaluationFormTargetKind.IndividualAccessor,
                "eligible-initialization-constructor"
                    => CoreEvaluationFormTargetKind
                        .EligibleInitializationConstructor,
                _ => throw Invalid(manifestPath, targetPath,
                    "unknown evaluation schema target category"),
            };
            if (!observed.Add(target))
            {
                throw Invalid(manifestPath, targetPath,
                    "duplicate evaluation schema target category");
            }
            targets.Add(target);
        }

        return new CoreEvaluationFormSchemaDeclaration(name, 0, 0,
            targets.MoveToImmutable(), isPublic, isPrimary,
            primaryTarget, repeatable);
    }

    private static string ReadText(JsonElement element, string manifestPath,
        string path)
    {
        if (element.ValueKind != JsonValueKind.String
            || element.GetString() is not string value
            || value.Length == 0
            || value.Contains('\0', StringComparison.Ordinal))
        {
            throw Invalid(manifestPath, path,
                "expected nonempty text without NUL");
        }
        return value;
    }
}
