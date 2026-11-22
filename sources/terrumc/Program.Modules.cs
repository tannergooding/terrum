// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.Tools;

internal static partial class Program
{
    private const string ModuleUsage = "Usage: terrumc compile-module producer <source> <export> [--source <source>] [--export <export>] -o <artifact.trm> [--runtime <fail-fast|checked-exceptions>] [--arithmetic-profile <development|release>] [--layout-core-manifest <path>] [--native-core-manifest <path>] [--native-output-core-manifest <path>] [--private-identity-manifest <path>] [--private-schema <exact-schema-identity>] [--corelib] [--json]; "
        + "terrumc compile-module consumer <source> <producer.trm> [--source <source>] -o <artifact.trm> [--runtime <fail-fast|checked-exceptions>] [--arithmetic-profile <development|release>] [--layout-core-manifest <path>] [--native-core-manifest <path>] [--native-output-core-manifest <path>] [--private-identity-manifest <path>] [--private-schema <exact-schema-identity>] [--json]; "
        + "terrumc link-module <consumer.trm> <producer.trm> -o <output.exe> --runtime <fail-fast|checked-exceptions> [--json]; "
        + "terrumc run-module <consumer.trm> <producer.trm> --runtime <fail-fast|checked-exceptions> [--json]";

    // The normal source binder runs only while creating an artifact. Linking takes module bytes only.
    private static int RunModuleCommand(string[] args)
    {
        var json = Array.IndexOf(args, "--json") >= 0;
        var trailingJson = args.Length != 0 && args[^1] == "--json";
        var count = args.Length - (trailingJson ? 1 : 0);
        if (json && Array.IndexOf(args, "--json") != args.Length - 1)
        {
            WriteInputError(json, "TRM2000", ModuleUsage);
            return 2;
        }

        try
        {
            byte[] result;
            string output;
            string profile;
            var selectedPolicy = NativeRuntimePolicy.FailFast;
            var sourceArithmeticProfile = SourceArithmeticProfile.Development;
            LayoutCoreRegistrationSet? layoutCoreRegistration = null;
            NativeCoreRegistrationSet? nativeCoreRegistration = null;
            NativeOutputCoreRegistrationSet? nativeOutputCoreRegistration
                = null;
            string? privateSchemaIdentity = null;
            if (count >= 6 && args[0] == "compile-module"
                && (args[1] is "producer" or "consumer"))
            {
                if (!TryReadModuleOptions(args, count, out var paths, out var exports, out output,
                    out selectedPolicy, out sourceArithmeticProfile, out var coreLibrary,
                    out var layoutCoreManifestPath,
                    out var nativeCoreManifestPath,
                    out var nativeOutputCoreManifestPath,
                    out var privateIdentityManifestPath,
                    out privateSchemaIdentity,
                    out var coreManifestOrder))
                {
                    WriteInputError(json, "TRM2000", ModuleUsage);
                    return 2;
                }
                var pathAliasError = GetModuleManifestPathAliasError(paths,
                    args[1] == "consumer" ? args[3] : null, output,
                    layoutCoreManifestPath, nativeCoreManifestPath,
                    nativeOutputCoreManifestPath,
                    privateIdentityManifestPath);
                if (pathAliasError is not null)
                {
                    WriteInputError(json, "TRM2002", pathAliasError);
                    return 2;
                }
                if (privateSchemaIdentity is not null)
                {
                    try
                    {
                        _ = ModuleCompiler.GetPrivateSchemaFormatVersion(
                            privateSchemaIdentity);
                    }
                    catch (ModuleContractException exception)
                    {
                        WriteInputError(json, "TRM2000",
                            exception.Message);
                        return 2;
                    }
                }

                if (paths.Any(path => path != "-" && PathsAlias(output, path))
                    || ((args[1] == "consumer") && PathsAlias(output, args[3]))
                    || !Path.GetExtension(output).Equals(".trm", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ModuleFormatException("Module output must be a distinct .trm artifact.");
                }

                PrivateModuleIdentityManifest? privateIdentityManifest = null;
                if (privateIdentityManifestPath is not null)
                {
                    try
                    {
                        privateIdentityManifest
                            = PrivateModuleIdentityManifestReader.Read(
                                privateIdentityManifestPath,
                                producer: args[1] == "producer",
                                sourceCount: paths.Count);
                    }
                    catch (Exception exception) when (exception is IOException
                        or UnauthorizedAccessException
                        or System.Text.DecoderFallbackException
                        or ArgumentException or NotSupportedException
                        or FormatException or JsonException)
                    {
                        WriteInputError(json, "TRM2001", exception.Message);
                        return 2;
                    }
                }

                if (privateIdentityManifest is
                        { SchemaVersion: 2 }
                    && privateSchemaIdentity
                        != ModuleCompiler.ForeignInterfacePrivateSchemaIdentity)
                {
                    WriteInputError(json, "TRM2001",
                        "Private identity manifest version 2 requires the exact PF111 private-schema selection.");
                    return 2;
                }

                CoreManifestDefinitions coreDefinitions;
                CoreManifestRegistrationComposition coreRegistrationComposition;
                try
                {
                    coreDefinitions = ReadPrivateCompilationCoreDefinitions(
                        layoutCoreManifestPath, nativeCoreManifestPath,
                        nativeOutputCoreManifestPath, coreManifestOrder,
                        privateIdentityManifest);
                    coreRegistrationComposition
                        = CoreManifestRegistrationComposer.Create(
                            coreDefinitions.Layout, coreDefinitions.Native,
                            coreDefinitions.NativeOutput,
                            layoutCoreManifestPath, nativeCoreManifestPath,
                            nativeOutputCoreManifestPath);
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException
                    or System.Text.DecoderFallbackException
                    or ArgumentException or NotSupportedException
                    or FormatException)
                {
                    WriteInputError(json, "TRM2001", exception.Message);
                    return 2;
                }
                layoutCoreRegistration = coreRegistrationComposition.Layout;
                nativeCoreRegistration = coreRegistrationComposition.Native;
                nativeOutputCoreRegistration
                    = coreRegistrationComposition.NativeOutput;
                if (args[1] == "consumer"
                    && privateIdentityManifest is not null
                    && SelectsConfiguredCoreEnum(privateIdentityManifest,
                        coreDefinitions))
                {
                    WriteInputError(json, "TRM2001",
                        "A private consumer cannot select a Core enum identity directly from a selected Core registration; imports must resolve through its positional producer.");
                    return 2;
                }
                var snapshots = ReadModuleSources(paths,
                    out var sourceSha256);
                if (privateIdentityManifest is not null
                    && !sourceSha256.SequenceEqual(
                        privateIdentityManifest.SourceSha256,
                        StringComparer.Ordinal))
                {
                    WriteInputError(json, "TRM2001",
                        "A source file does not match the captured private identity manifest snapshot.");
                    return 2;
                }
                var reference = args[1] == "consumer" ? ReadModuleFile(args[3]) : null;
                if (privateIdentityManifest is not null
                    && reference is not null
                    && Convert.ToHexString(SHA256.HashData(reference))
                        != privateIdentityManifest.ReferenceProducerSha256)
                {
                    WriteInputError(json, "TRM2001",
                        "The producer artifact does not match the captured private identity manifest snapshot.");
                    return 2;
                }
                var declarations = reference is null ? null
                    : privateSchemaIdentity is null
                        ? ModuleCompiler.CreateReference(reference)
                        : ModuleCompiler.CreateReferenceWithSchema(reference,
                            privateSchemaIdentity);
                var options = new CompilationOptions {
                    SourceArithmeticProfile = sourceArithmeticProfile,
                    RuntimePolicy = (BoundRuntimePolicy)selectedPolicy,
                    LayoutCoreRegistration = layoutCoreRegistration,
                    NativeCoreRegistration = nativeCoreRegistration,
                    NativeOutputCoreRegistration =
                        nativeOutputCoreRegistration,
                };
                var compilation = coreLibrary ? Compilation.CreateCoreLibrary(snapshots, options)
                    : snapshots.Length == 1
                    ? declarations is null ? Compilation.CreateLibrary(snapshots[0], options)
                        : Compilation.Create(snapshots[0], declarations, options)
                    : declarations is null ? Compilation.CreateLibrary(snapshots, options)
                        : Compilation.Create(snapshots, declarations, options);
                if (compilation.HasErrors)
                {
                    WriteModuleSourceDiagnostics(compilation, json, selectedPolicy);
                    return 1;
                }
                if (compilation.Program is not BoundProgram program)
                {
                    throw new InvalidOperationException("A successful compilation must contain a bound program.");
                }
                PrivateModuleIdentityInputs? identityInputs = null;
                if (privateIdentityManifest is not null)
                {
                    try
                    {
                        identityInputs
                            = PrivateModuleIdentityInputs.CreateFromSelectors(
                                program,
                                privateIdentityManifest.UsingModuleIdentity,
                                privateIdentityManifest.Definitions,
                                privateIdentityManifest.Imports,
                                privateIdentityManifest.EnumMembers,
                                privateIdentityManifest.SourceEnumDefinitions,
                                privateIdentityManifest
                                    .ForeignInterfaceDefinitions,
                                privateIdentityManifest
                                    .ForeignInterfaceImports);
                    }
                    catch (Exception exception) when (exception is
                        ArgumentException or ModuleContractException)
                    {
                        WriteInputError(json, "TRM2001", exception.Message);
                        return 2;
                    }
                }
                result = args[1] switch {
                    "producer" when privateSchemaIdentity is not null
                        => ModuleCompiler.CreateProducerWithSchema(program,
                            exports, privateSchemaIdentity, identityInputs),
                    "producer" when identityInputs is null
                        => ModuleCompiler.CreateProducer(program, exports),
                    "producer" => ModuleCompiler.CreateProducer(program,
                        exports, identityInputs
                            ?? throw new InvalidOperationException(
                                "Producer identity inputs are missing.")),
                    "consumer" when privateSchemaIdentity is not null
                        => ModuleCompiler.CreateConsumerWithSchema(program,
                            reference
                                ?? throw new InvalidOperationException(
                                    "Consumer reference is missing."),
                            privateSchemaIdentity, identityInputs),
                    "consumer" when identityInputs is null
                        => ModuleCompiler.CreateConsumer(program,
                            reference ?? throw new InvalidOperationException("Consumer reference is missing.")),
                    "consumer" => ModuleCompiler.CreateConsumer(program,
                        reference ?? throw new InvalidOperationException("Consumer reference is missing."),
                        identityInputs
                            ?? throw new InvalidOperationException(
                                "Consumer identity inputs are missing.")),
                    _ => throw new InvalidOperationException("Unknown module kind."),
                };
                profile = ModuleCompiler.GetNativeProfileVersion(result, args[1] == "consumer");
            }
            else if (count == 7 && args[0] == "link-module" && args[3] == "-o"
                && args[5] == "--runtime" && args[6] is "fail-fast" or "checked-exceptions")
            {
                if (Path.GetFullPath(args[4]).Equals(Path.GetFullPath(args[1]), StringComparison.OrdinalIgnoreCase)
                    || Path.GetFullPath(args[4]).Equals(Path.GetFullPath(args[2]), StringComparison.OrdinalIgnoreCase)
                    || !Path.GetExtension(args[4]).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ModuleFormatException("Linked output must be a distinct .exe path.");
                }
                var program = ModuleCompiler.Link(ReadModuleFile(args[1]), ReadModuleFile(args[2]));
                var runtimePolicy = args[6] == "fail-fast"
                    ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
                selectedPolicy = runtimePolicy;
                result = NativeEmitter.Emit(program, runtimePolicy);
                profile = Compilation.GetNativeProfileVersion(program);
                output = args[4];
            }
            else if (count == 5 && args[0] == "run-module"
                && args[3] == "--runtime" && args[4] is "fail-fast" or "checked-exceptions")
            {
                var program = ModuleCompiler.Link(ReadModuleFile(args[1]), ReadModuleFile(args[2]));
                var runtimePolicy = args[4] == "fail-fast"
                    ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
                return NativeJit.Execute(program, runtimePolicy, GraphJitRegistrarAssemblyPath());
            }
            else
            {
                WriteInputError(json, "TRM2000", ModuleUsage);
                return 2;
            }
            var outputPath = Path.GetFullPath(output);
            WriteArtifact(outputPath, result);
            if (json)
            {
                WriteModuleArtifactJson(args[0], outputPath, result, selectedPolicy, profile);
            }
            return 0;
        }
        catch (ModuleContractException exception)
        {
            WriteInputError(json, "TRM5002", exception.Message);
            return 1;
        }
        catch (ModuleFormatException exception)
        {
            WriteInputError(json, "TRM5001", exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            WriteInputError(json, "TRM5003", exception.Message);
            return 2;
        }
    }

    private static bool TryReadModuleOptions(string[] args, int count, out List<string> sources,
        out List<string> exports, out string output, out NativeRuntimePolicy runtimePolicy,
        out SourceArithmeticProfile sourceArithmeticProfile, out bool coreLibrary,
        out string? layoutCoreManifestPath,
        out string? nativeCoreManifestPath,
        out string? nativeOutputCoreManifestPath,
        out string? privateIdentityManifestPath,
        out string? privateSchemaIdentity,
        out List<CoreManifestKind> coreManifestOrder)
    {
        sources = [args[2]];
        var native = args[0] == "compile-native";
        exports = args[1] == "producer" && !native ? [args[3]] : [];
        output = "";
        runtimePolicy = NativeRuntimePolicy.FailFast;
        sourceArithmeticProfile = SourceArithmeticProfile.Development;
        coreLibrary = false;
        layoutCoreManifestPath = null;
        nativeCoreManifestPath = null;
        nativeOutputCoreManifestPath = null;
        privateIdentityManifestPath = null;
        privateSchemaIdentity = null;
        coreManifestOrder = [];
        var hasOutput = false;
        var hasRuntime = false;
        var hasArithmeticProfile = false;
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(args[2]) };
        var exportNames = new HashSet<string>(exports, StringComparer.Ordinal);
        for (var index = 4; index < count;)
        {
            if (args[index] == "--corelib")
            {
                if (args[1] != "producer" || coreLibrary)
                {
                    return false;
                }
                coreLibrary = true;
                index++;
                continue;
            }
            if (index + 1 >= count)
            {
                return false;
            }
            var value = args[index + 1];
            switch (args[index])
            {
                case "--source" when sources.Count < 256 && sourcePaths.Add(Path.GetFullPath(value)):
                    sources.Add(value);
                    break;
                case "--export" when args[1] == "producer" && exports.Count < 1024 && exportNames.Add(value):
                    exports.Add(value);
                    break;
                case "-o" when !hasOutput:
                    output = value;
                    hasOutput = true;
                    break;
                case "--runtime" when !hasRuntime && value is "fail-fast" or "checked-exceptions":
                    hasRuntime = true;
                    runtimePolicy = value == "fail-fast"
                        ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
                    break;
                case "--arithmetic-profile" when !hasArithmeticProfile
                    && TryParseSourceArithmeticProfile(value, out sourceArithmeticProfile):
                    hasArithmeticProfile = true;
                    break;
                case "--layout-core-manifest" when args[0] == "compile-module"
                    && layoutCoreManifestPath is null
                    && value.Length != 0 && value != "-"
                    && !value.StartsWith('-', StringComparison.Ordinal):
                    if (!TryGetFullPath(value, out layoutCoreManifestPath))
                    {
                        return false;
                    }
                    coreManifestOrder.Add(CoreManifestKind.Layout);
                    break;
                case "--native-core-manifest" when
                    (args[0] is "compile-module" or "compile-native")
                    && nativeCoreManifestPath is null
                    && value.Length != 0 && value != "-"
                    && !value.StartsWith('-', StringComparison.Ordinal):
                    if (!TryGetFullPath(value, out nativeCoreManifestPath))
                    {
                        return false;
                    }
                    coreManifestOrder.Add(CoreManifestKind.Native);
                    break;
                case "--native-output-core-manifest" when
                    (args[0] is "compile-module" or "compile-native")
                    && nativeOutputCoreManifestPath is null
                    && value.Length != 0 && value != "-"
                    && !value.StartsWith('-', StringComparison.Ordinal):
                    if (!TryGetFullPath(value,
                        out nativeOutputCoreManifestPath))
                    {
                        return false;
                    }
                    coreManifestOrder.Add(CoreManifestKind.NativeOutput);
                    break;
                case "--private-identity-manifest" when args[0]
                        is ("compile-module" or "compile-native")
                    && privateIdentityManifestPath is null
                    && value.Length != 0 && value != "-"
                    && !value.StartsWith('-', StringComparison.Ordinal):
                    if (!TryGetFullPath(value,
                        out privateIdentityManifestPath))
                    {
                        return false;
                    }
                    break;
                case "--private-schema" when (args[0] is "compile-module"
                        or "compile-native")
                    && privateSchemaIdentity is null
                    && value.Length != 0 && value != "-"
                    && !value.StartsWith('-', StringComparison.Ordinal):
                    privateSchemaIdentity = value;
                    break;
                default:
                    return false;
            }
            index += 2;
        }
        return hasOutput && (!native || (hasRuntime && (args[1] != "producer" || exports.Count != 0)));
    }

    private static string? GetModuleManifestPathAliasError(
        List<string> sourcePaths, string? producerPath, string outputPath,
        params string?[] manifestPaths)
    {
        var selectedManifests = manifestPaths
            .Where(static path => path is not null)
            .Select(static path => path!)
            .ToArray();
        try
        {
            for (var index = 0; index < selectedManifests.Length; index++)
            {
                var path = selectedManifests[index];
                if (PathsAlias(path, outputPath)
                    || sourcePaths.Any(source => source != "-"
                        && PathsAlias(path, source))
                    || (producerPath is not null
                        && PathsAlias(path, producerPath))
                    || selectedManifests.Take(index).Any(previous =>
                        PathsAlias(path, previous)))
                {
                    return "Selected manifests must be distinct from every source, producer artifact, output and other manifest.";
                }
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return $"Could not validate manifest path identity: {exception.Message}";
        }

        return null;
    }

    private static CoreManifestDefinitions ReadPrivateCompilationCoreDefinitions(
        string? layoutPath, string? nativePath,
        string? nativeOutputPath, List<CoreManifestKind> order,
        PrivateModuleIdentityManifest? identityManifest)
    {
        var definitions = ReadCoreManifestDefinitions(layoutPath, nativePath,
            nativeOutputPath, evaluationPath: null, order: order);
        var usingModuleIdentity = definitions.Layout?.UsingModuleIdentity
            ?? definitions.Native?.UsingModuleIdentity
            ?? definitions.NativeOutput?.UsingModuleIdentity;
        if (identityManifest is not null
            && usingModuleIdentity is string configuredIdentity
            && configuredIdentity != identityManifest.UsingModuleIdentity)
        {
            throw new FormatException(
                "The private identity and selected Core manifests must use the same Module identity.");
        }
        return definitions;
    }

    private static bool SelectsConfiguredCoreEnum(
        PrivateModuleIdentityManifest manifest,
        CoreManifestDefinitions definitions)
    {
        var identities = (definitions.Layout is null
                ? Enumerable.Empty<CoreDeclarationIdentity>()
                : new[] { definitions.Layout.LayoutKinds.Identity })
            .Concat(definitions.Native?.EnumDefinitions.Select(
                static definition => definition.Identity)
                ?? Enumerable.Empty<CoreDeclarationIdentity>())
            .Concat(definitions.NativeOutput is null
                ? Enumerable.Empty<CoreDeclarationIdentity>()
                : new[] {
                    definitions.NativeOutput.OutputConditions.Identity,
                });
        var selected = identities.ToHashSet();
        return manifest.Imports.Any(selection =>
            selection.Declaration.Kind
                == PrivateNominalDeclarationKind.Enum
            && selected.Contains(new CoreDeclarationIdentity(
                selection.SelectedModuleIdentity,
                selection.SelectedExportKey,
                CoreDeclarationKind.EnumType)));
    }

    private static void WriteModuleSourceDiagnostics(Compilation compilation, bool json, NativeRuntimePolicy policy)
        => WriteCompilationResult("compile-module", compilation, compilation.SourceDiagnostics, false, null, null, json, policy);

    private static void WriteModuleArtifactJson(string command, string outputPath, byte[] result,
        NativeRuntimePolicy policy, string profile)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("profile", profile);
        writer.WriteString("runtimePolicy", PolicyName(policy));
        writer.WriteString("command", command);
        writer.WriteBoolean("success", true);
        writer.WriteStartArray("diagnostics");
        writer.WriteEndArray();
        writer.WriteStartObject("artifact");
        writer.WriteString("path", outputPath);
        writer.WriteString("kind", command == "link-module" ? "windows-x64-executable" : "semantic-module");
        writer.WriteString("runtimePolicy", PolicyName(policy));
        writer.WriteNumber("length", result.Length);
        writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(result)));
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static byte[] ReadModuleFile(string path, int maximumBytes = 4 * 1024 * 1024)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > maximumBytes)
        {
            throw new ModuleFormatException(FormattableString.Invariant($"Module file exceeds the {maximumBytes / (1024 * 1024)} MiB limit."));
        }
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            throw new ModuleFormatException("Module file changed while reading.");
        }
        return bytes;
    }

    private static ImmutableArray<SyntaxTree> ReadModuleSources(
        List<string> paths)
        => ReadModuleSources(paths, out _);

    private static ImmutableArray<SyntaxTree> ReadModuleSources(
        List<string> paths, out ImmutableArray<string> sourceSha256)
    {
        var trees = ImmutableArray.CreateBuilder<SyntaxTree>(paths.Count);
        var sourceHashes = ImmutableArray.CreateBuilder<string>(paths.Count);
        var sourceBytes = 0;
        foreach (var path in paths)
        {
            var (source, sha256) = ReadModuleSource(path);
            sourceHashes.Add(sha256);
            sourceBytes += Encoding.UTF8.GetByteCount(source);
            if (sourceBytes > 4 * 1024 * 1024)
            {
                throw new ModuleFormatException("Combined module source exceeds the 4 MiB limit.");
            }
            trees.Add(SyntaxTree.Parse(source, path));
        }

        sourceSha256 = sourceHashes.MoveToImmutable();
        return trees.MoveToImmutable();
    }

    private static (string Source, string Sha256) ReadModuleSource(string path)
    {
        if (path == "-")
        {
            throw new ArgumentException("Module source must be a bounded file path, not stdin.");
        }
        var bytes = ReadModuleFile(path);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        var text = new UTF8Encoding(false, true).GetString(bytes);
        var source = text.StartsWith('\uFEFF', StringComparison.Ordinal)
            ? text[1..] : text;
        return (source, sha256);
    }
}
