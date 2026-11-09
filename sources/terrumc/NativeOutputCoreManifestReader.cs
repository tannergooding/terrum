// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.Tools;

internal sealed record NativeOutputCoreManifestDefinition(
    string CoreAuthorityIdentity,
    string UsingModuleIdentity,
    ImmutableArray<CoreProviderModuleOrigin> Origins,
    CoreEnumDefinition OutputConditions,
    CoreNativeOutputAttributeSchema NativeOutput)
{
    internal ImmutableArray<CoreProviderDeclarationSnapshot> Declarations =>
    [
        CoreProviderDeclarationSnapshot.From(OutputConditions),
        CoreProviderDeclarationSnapshot.From(NativeOutput),
    ];

    internal NativeOutputCoreRegistrationSet CreateRegistration(
        CoreProviderModuleBindings bindings)
        => NativeOutputCoreRegistrationSet.Create(bindings,
            OutputConditions, NativeOutput);
}

internal static class NativeOutputCoreManifestReader
{
    private const string SchemaIdentity
        = "terrumc.native-output-core-manifest/1";
    private const string OutputConditionsName
        = "Terrum.Interop.NativeOutputCondition";
    private const string NativeOutputName
        = "Terrum.Interop.NativeOutput";
    private const string OutputPlacement
        = "primary-callable-contract";

    private static readonly ImmutableHashSet<string> s_outputContexts
        = ImmutableHashSet.Create(StringComparer.Ordinal,
            "direct-import-parameter",
            "foreign-interface-method-parameter");

    internal static NativeOutputCoreManifestDefinition ReadDefinition(
        string path)
    {
        var contents = File.ReadAllBytes(path);
        var json = contents.AsMemory();
        if (json.Span.StartsWith(NativeCoreManifestReader.s_utf8Signature))
        {
            json = json[3..];
            if (json.Span.StartsWith(
                NativeCoreManifestReader.s_utf8Signature))
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
                jsonText = NativeCoreManifestReader.s_strictUtf8.GetString(
                    json.Span);
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
            NativeCoreManifestReader.ValidateJsonText(document.RootElement,
                path, "$");
            var root = NativeCoreManifestReader.RequireObject(
                document.RootElement, path, "$", "schema",
                "coreAuthorityIdentity", "usingModuleIdentity", "providers",
                "outputConditions", "nativeOutput");
            if (NativeCoreManifestReader.ReadText(
                    NativeCoreManifestReader.Property(root, "schema", path,
                        "$"), path, "$.schema") != SchemaIdentity)
            {
                throw Invalid(path, "$.schema",
                    $"expected exactly '{SchemaIdentity}'");
            }

            var authorityIdentity = NativeCoreManifestReader.ReadText(
                NativeCoreManifestReader.Property(root,
                    "coreAuthorityIdentity", path, "$"), path,
                "$.coreAuthorityIdentity");
            var usingModuleIdentity = NativeCoreManifestReader.ReadText(
                NativeCoreManifestReader.Property(root,
                    "usingModuleIdentity", path, "$"), path,
                "$.usingModuleIdentity");
            var origins = NativeCoreManifestReader.ReadOrigins(
                NativeCoreManifestReader.Property(root, "providers", path,
                    "$"), path);
            if (origins.Length is < 1 or > 2)
            {
                throw Invalid(path, "$.providers",
                    "expected exactly the distinct providers of the enum and attribute schema");
            }

            var outputConditions = NativeCoreManifestReader.ReadEnum(
                NativeCoreManifestReader.Property(root, "outputConditions",
                    path, "$"), path, "$.outputConditions",
                OutputConditionsName, [("NonZero", 0), ("NonNegative", 1)]);
            if (outputConditions.GenericArity != 0
                || !outputConditions.IsPublic
                || !outputConditions.IsPrimary
                || outputConditions.IsFlags
                || outputConditions.BlittableTargetRequirement is not (0 or 1))
            {
                throw Invalid(path, "$.outputConditions",
                    "enum visibility, flags, arity or representation commitment is invalid");
            }

            var nativeOutput = ReadNativeOutputSchema(
                NativeCoreManifestReader.Property(root, "nativeOutput",
                    path, "$"), outputConditions, path,
                "$.nativeOutput");
            ValidateDeclarations(outputConditions, nativeOutput, path);
            ValidateUsedProviders(origins, outputConditions, nativeOutput,
                path);
            return new NativeOutputCoreManifestDefinition(
                authorityIdentity, usingModuleIdentity, origins,
                outputConditions, nativeOutput);
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

    private static CoreNativeOutputAttributeSchema ReadNativeOutputSchema(
        JsonElement element, CoreEnumDefinition outputConditions,
        string manifestPath, string path)
    {
        var value = NativeCoreManifestReader.RequireObject(element,
            manifestPath, path, "identity", "qualifiedName", "arguments",
            "placement", "targets", "genericArity", "isPublic",
            "isPrimary", "isRepeatable");
        var identity = NativeCoreManifestReader.ReadIdentity(
            NativeCoreManifestReader.Property(value, "identity",
                manifestPath, path), manifestPath, path + ".identity");
        if (identity.Kind != CoreDeclarationKind.AttributeSchema)
        {
            throw Invalid(manifestPath, path + ".identity.kind",
                "expected an attribute-schema identity");
        }

        var qualifiedName = NativeCoreManifestReader.ReadQualifiedName(
            NativeCoreManifestReader.Property(value, "qualifiedName",
                manifestPath, path), manifestPath,
            path + ".qualifiedName");
        if (qualifiedName != NativeOutputName)
        {
            throw Invalid(manifestPath, path + ".qualifiedName",
                $"expected exactly '{NativeOutputName}'");
        }
        var placement = NativeCoreManifestReader.ReadText(
            NativeCoreManifestReader.Property(value, "placement",
                manifestPath, path), manifestPath, path + ".placement");
        if (placement != OutputPlacement)
        {
            throw Invalid(manifestPath, path + ".placement",
                $"expected exactly '{OutputPlacement}'");
        }

        var targets = NativeCoreManifestReader.ReadStringSet(
            NativeCoreManifestReader.Property(value, "targets",
                manifestPath, path), manifestPath, path + ".targets");
        if (!targets.SetEquals(s_outputContexts))
        {
            throw Invalid(manifestPath, path + ".targets",
                "target set differs from the selected NativeOutput source contract");
        }

        var argumentsElement = NativeCoreManifestReader.Property(value,
            "arguments", manifestPath, path);
        if (argumentsElement.ValueKind != JsonValueKind.Array
            || argumentsElement.GetArrayLength() != 1)
        {
            throw Invalid(manifestPath, path + ".arguments",
                "exactly one argument record is required");
        }
        var argumentPath = path + ".arguments[0]";
        var argument = NativeCoreManifestReader.ReadArgument(
            argumentsElement[0], manifestPath, argumentPath);
        if (argument.Name != "When"
            || argument.Kind != "enum"
            || argument.EnumType != outputConditions.Identity
            || !argument.IsRequired
            || argument.AllowsPositional
            || !argument.AllowsNamed
            || !argument.Contexts.SetEquals(s_outputContexts)
            || argument.Default is not null)
        {
            throw Invalid(manifestPath, argumentPath,
                "argument differs from the complete selected NativeOutput schema");
        }

        var genericArity = NativeCoreManifestReader.ReadUInt32(
            NativeCoreManifestReader.Property(value, "genericArity",
                manifestPath, path), manifestPath, path + ".genericArity");
        var isPublic = NativeCoreManifestReader.ReadBoolean(
            NativeCoreManifestReader.Property(value, "isPublic",
                manifestPath, path), manifestPath, path + ".isPublic");
        var isPrimary = NativeCoreManifestReader.ReadBoolean(
            NativeCoreManifestReader.Property(value, "isPrimary",
                manifestPath, path), manifestPath, path + ".isPrimary");
        var isRepeatable = NativeCoreManifestReader.ReadBoolean(
            NativeCoreManifestReader.Property(value, "isRepeatable",
                manifestPath, path), manifestPath, path + ".isRepeatable");
        if (genericArity != 0 || !isPublic || !isPrimary || isRepeatable)
        {
            throw Invalid(manifestPath, path,
                "schema visibility, primary status, repeatability or generic arity is invalid");
        }

        return new CoreNativeOutputAttributeSchema(identity, qualifiedName,
            [NativeCoreManifestReader.ToCoreArgument(argument)], placement,
            targets, 0, isPublic, isPrimary, isRepeatable);
    }

    private static void ValidateDeclarations(
        CoreEnumDefinition outputConditions,
        CoreNativeOutputAttributeSchema nativeOutput,
        string manifestPath)
    {
        var identities = new HashSet<CoreDeclarationIdentity> {
            outputConditions.Identity,
            nativeOutput.Identity,
        };
        if (identities.Count != 2)
        {
            throw Invalid(manifestPath, "$",
                "enum and schema declaration identities must be distinct");
        }
        foreach (var member in outputConditions.Members)
        {
            if (!identities.Add(member.Identity))
            {
                throw Invalid(manifestPath, "$.outputConditions.members",
                    "declaration identities must be distinct");
            }
        }
    }

    private static void ValidateUsedProviders(
        ImmutableArray<CoreProviderModuleOrigin> origins,
        CoreEnumDefinition outputConditions,
        CoreNativeOutputAttributeSchema nativeOutput,
        string manifestPath)
    {
        var used = new HashSet<string>(StringComparer.Ordinal) {
            outputConditions.Identity.ProviderIdentity,
            nativeOutput.Identity.ProviderIdentity,
        };
        if (used.Count != origins.Length
            || origins.Any(origin => !used.Remove(origin.ModuleIdentity))
            || used.Count != 0)
        {
            throw Invalid(manifestPath, "$.providers",
                "origins must name each used provider exactly once and contain no unused provider");
        }
    }

    private static FormatException Invalid(string manifestPath, string path,
        string message)
        => NativeCoreManifestReader.Invalid(manifestPath, path, message);
}
