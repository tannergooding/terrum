// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.Tools;

internal static partial class Program
{
    private const string A3FixedArraySchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
    private const string A2EUScalarFrozenValuesSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
    private const string A2R1ReturnCoverageSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-1";
    private const string A2R1RepeatInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-repeat-initialization-1";
    private const string LocalAggregateLayoutSchemaIdentity
        = "trrm/1-draft-local-aggregate-layout-execution-1";
    private const string RegisterExceptionRegionsSchemaIdentity
        = "trrm/1-draft-register-exception-regions-1";

    private static int RunTrRmExecutionCommand(string[] args, bool json)
    {
        var command = args[0];
        var count = args.Length - (json && args[^1] == "--json" ? 1 : 0);
        if (count < 2 || args[1] == "--json"
            || (json && args[^1] != "--json"))
        {
            WriteInputError(json, "TRM2000", Usage);
            return 2;
        }
        string? output = null;
        string? identity = null;
        string? schemaIdentity = null;
        string? moduleIdentity = null;
        string? layoutCoreManifestPath = null;
        string? nativeCoreManifestPath = null;
        var nativeCoreManifestFirst = false;
        LayoutCoreRegistrationSet? layoutCoreRegistration = null;
        NativeCoreRegistrationSet? nativeCoreRegistration = null;
        var library = false;
        var runtime = false;
        var hasArithmeticProfile = false;
        var sourceArithmeticProfile = SourceArithmeticProfile.Development;
        var paths = new List<string> { args[1] };
        var exports = new List<string>();
        var providers = new List<(string SchemaIdentity, string Path)>();
        var coreExceptionAnchors = new Dictionary<
            (string UsingModuleIdentity, string Role),
            (string ProducerModuleIdentity, string ExportKey)>();
        var uniqueExports = new HashSet<string>(StringComparer.Ordinal);
        var a3AdmissionCallFailed = false;
        var isArtifactCommand = command is "run-trrm" or "build-trrm"
            or "build-trrm-library";
        try
        {
            for (var i = 2; i < count;)
            {
                if (args[i] == "--library" && command == "compile-trrm" && !library)
                {
                    library = true;
                    i++;
                    continue;
                }
                if (args[i] == "--trrm-provider")
                {
                    if (!isArtifactCommand || i + 2 >= count
                        || args[i + 1].Length == 0
                        || args[i + 2].Length == 0 || args[i + 2] == "-")
                    {
                        WriteInputError(json, "TRM2000", Usage);
                        return 2;
                    }

                    providers.Add((args[i + 1], args[i + 2]));
                    i += 3;
                    continue;
                }
                if (args[i] == "--trrm-core-exception-anchor")
                {
                    if (!isArtifactCommand || i + 4 >= count)
                    {
                        WriteInputError(json, "TRM2000", Usage);
                        return 2;
                    }

                    var usingModuleIdentity = args[i + 1];
                    var role = args[i + 2];
                    var producerModuleIdentity = args[i + 3];
                    var exportKey = args[i + 4];
                    if (usingModuleIdentity.Length == 0
                        || role is not ("exception-interface"
                            or "unreachable-payload")
                        || producerModuleIdentity.Length == 0
                        || exportKey.Length == 0
                        || !coreExceptionAnchors.TryAdd(
                            (usingModuleIdentity, role),
                            (producerModuleIdentity, exportKey)))
                    {
                        WriteInputError(json, "TRM2000", Usage);
                        return 2;
                    }

                    i += 5;
                    continue;
                }
                if (i + 1 == count
                    || ((args[i] is "--trrm-schema" or "--trrm-module")
                        && args[i + 1].StartsWith('-',
                            StringComparison.Ordinal)))
                {
                    WriteInputError(json, "TRM2000", Usage);
                    return 2;
                }
                var value = args[i + 1];
                switch (args[i])
                {
                    case "-o" when command != "run-trrm" && output is null:
                        output = Path.GetFullPath(value);
                        break;
                    case "--identity" when command == "compile-trrm" && identity is null && value.Length > 0:
                        identity = value;
                        break;
                    case "--trrm-schema" when (command == "compile-trrm"
                        || isArtifactCommand)
                        && schemaIdentity is null && value.Length > 0:
                        schemaIdentity = value;
                        break;
                    case "--trrm-module" when isArtifactCommand
                        && moduleIdentity is null && value.Length > 0:
                        moduleIdentity = value;
                        break;
                    case "--source" when command == "compile-trrm" && paths.Count < 256
                        && !paths.Contains(value, StringComparer.OrdinalIgnoreCase):
                        paths.Add(value);
                        break;
                    case "--layout-core-manifest" when command == "compile-trrm"
                        && layoutCoreManifestPath is null
                        && value.Length != 0 && value != "-"
                        && !value.StartsWith('-',
                            StringComparison.Ordinal)
                        && TryGetFullPath(value, out var manifestFullPath):
                        layoutCoreManifestPath = manifestFullPath;
                        if (nativeCoreManifestPath is null)
                        {
                            nativeCoreManifestFirst = false;
                        }
                        break;
                    case "--native-core-manifest" when command == "compile-trrm"
                        && nativeCoreManifestPath is null
                        && value.Length != 0 && value != "-"
                        && !value.StartsWith('-',
                            StringComparison.Ordinal)
                        && TryGetFullPath(value, out var nativeManifestFullPath):
                        nativeCoreManifestPath = nativeManifestFullPath;
                        nativeCoreManifestFirst = layoutCoreManifestPath is null;
                        break;
                    case "--export" when command == "build-trrm-library" && uniqueExports.Add(value):
                        exports.Add(value);
                        break;
                    case "--runtime" when !runtime && value == "fail-fast":
                        runtime = true;
                        break;
                    case "--runtime" when !runtime && value == "checked-exceptions":
                        WriteInputError(json, "TRM5001",
                            "TRRM (1, 0) execution supports only fail-fast; checked-exceptions requires an exceptional-edge contract.");
                        return 1;
                    case "--arithmetic-profile" when command == "compile-trrm"
                        && !hasArithmeticProfile
                        && TryParseSourceArithmeticProfile(value, out sourceArithmeticProfile):
                        hasArithmeticProfile = true;
                        break;
                    default:
                        WriteInputError(json, "TRM2000", Usage);
                        return 2;
                }
                i += 2;
            }
            var coreExceptionIdentitySelections
                = ImmutableArray.CreateBuilder<CoreExceptionIdentitySelection>();
            var selectedUsingModules = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (var (usingModuleIdentity, _) in coreExceptionAnchors.Keys)
            {
                _ = selectedUsingModules.Add(usingModuleIdentity);
            }
            foreach (var usingModuleIdentity in selectedUsingModules)
            {
                if (!coreExceptionAnchors.TryGetValue(
                        (usingModuleIdentity, "exception-interface"),
                        out var exceptionInterface)
                    || !coreExceptionAnchors.TryGetValue(
                        (usingModuleIdentity, "unreachable-payload"),
                        out var unreachablePayload))
                {
                    WriteInputError(json, "TRM2000", Usage);
                    return 2;
                }

                coreExceptionIdentitySelections.Add(
                    new CoreExceptionIdentitySelection(
                        usingModuleIdentity,
                        exceptionInterface.ProducerModuleIdentity,
                        exceptionInterface.ExportKey,
                        unreachablePayload.ProducerModuleIdentity,
                        unreachablePayload.ExportKey));
            }
            var selectedCoreExceptionIdentitySelections
                = coreExceptionIdentitySelections.ToImmutable();
            if (!selectedCoreExceptionIdentitySelections.IsEmpty
                && !StringComparer.Ordinal.Equals(schemaIdentity,
                    RegisterExceptionRegionsSchemaIdentity))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }

            if ((command == "compile-trrm"
                && (moduleIdentity is not null || providers.Count != 0))
                || (isArtifactCommand
                    && ((schemaIdentity is null) != (moduleIdentity is null)
                        || (schemaIdentity is null && providers.Count != 0))))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            if (isArtifactCommand && schemaIdentity is not null
                && (schemaIdentity is not (
                        "trrm/1-draft-foreign-invocation-contracts-1"
                        or "trrm/1-draft-foreign-invocation-storage-addresses-1"
                        or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2"
                        or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1"
                        or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1"
                        or A2EUScalarFrozenValuesSchemaIdentity
                        or A2R1ReturnCoverageSchemaIdentity
                        or A2R1RepeatInitializationSchemaIdentity
                        or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1"
                        or LocalAggregateLayoutSchemaIdentity
                        or RegisterExceptionRegionsSchemaIdentity)
                    || providers.Exists(static provider =>
                        provider.SchemaIdentity is not (
                            "trrm/1-draft-foreign-invocation-contracts-1"
                            or "trrm/1-draft-foreign-invocation-storage-addresses-1"
                            or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2"
                            or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1"
                            or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1"
                            or A2EUScalarFrozenValuesSchemaIdentity
                            or A2R1ReturnCoverageSchemaIdentity
                            or A2R1RepeatInitializationSchemaIdentity
                            or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1"
                            or LocalAggregateLayoutSchemaIdentity
                            or RegisterExceptionRegionsSchemaIdentity))))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            if (isArtifactCommand && schemaIdentity is not null
                && (StringComparer.Ordinal.Equals(schemaIdentity,
                        RegisterExceptionRegionsSchemaIdentity)
                    || providers.Exists(static provider =>
                        StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                            RegisterExceptionRegionsSchemaIdentity)))
                && (!StringComparer.Ordinal.Equals(schemaIdentity,
                        RegisterExceptionRegionsSchemaIdentity)
                    || providers.Exists(static provider =>
                        !StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                            RegisterExceptionRegionsSchemaIdentity))))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            if (!runtime || (command == "compile-trrm" && identity is null)
                || (command is "build-trrm" or "build-trrm-library" && args[1] == "-")
                || (command == "run-trrm" && (output is not null || args[1] == "-"))
                || (command != "run-trrm" && output is null)
                || (command == "build-trrm-library" && exports.Count == 0)
                || (output is not null && (!Path.GetExtension(output).Equals(
                    command switch { "compile-trrm" => ".trrm", "build-trrm" => ".exe", _ => ".dll" },
                    StringComparison.OrdinalIgnoreCase)
                    || paths.Any(path => path != "-" && PathsAlias(output,
                        path)))))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            if (output is not null && providers.Any(provider =>
                    PathsAlias(output, provider.Path)))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            if (layoutCoreManifestPath is not null
                || nativeCoreManifestPath is not null)
            {
                if ((layoutCoreManifestPath is not null
                        && ((output is not null
                                && PathsAlias(layoutCoreManifestPath, output))
                            || paths.Any(path => path != "-"
                                && PathsAlias(layoutCoreManifestPath, path))))
                    || (nativeCoreManifestPath is not null
                        && ((output is not null
                                && PathsAlias(nativeCoreManifestPath, output))
                            || paths.Any(path => path != "-"
                                && PathsAlias(nativeCoreManifestPath, path))))
                    || (layoutCoreManifestPath is not null
                        && nativeCoreManifestPath is not null
                        && PathsAlias(layoutCoreManifestPath,
                            nativeCoreManifestPath)))
                {
                    WriteInputError(json, "TRM2002",
                        "Core manifests must be distinct from one another, the source and artifact output.");
                    return 2;
                }

                var usingModuleIdentity = identity
                    ?? throw new InvalidOperationException(
                        "Missing compile-trrm identity.");
                var definitions = ReadCoreManifestDefinitions(
                    layoutCoreManifestPath, nativeCoreManifestPath,
                    nativeCoreManifestFirst);
                if ((definitions.Layout is LayoutCoreManifestDefinition
                        layoutDefinition
                        && !StringComparer.Ordinal.Equals(
                            layoutDefinition.UsingModuleIdentity,
                            usingModuleIdentity))
                    || (definitions.Native is
                        NativeCoreManifestDefinition nativeDefinition
                        && !StringComparer.Ordinal.Equals(
                            nativeDefinition.UsingModuleIdentity,
                            usingModuleIdentity)))
                {
                    WriteInputError(json, "TRM2000",
                        "Each Core manifest usingModuleIdentity must exactly match --identity.");
                    return 2;
                }
                var registrations = CoreManifestRegistrationComposer.Create(
                    definitions.Layout, definitions.Native,
                    layoutCoreManifestPath, nativeCoreManifestPath);
                layoutCoreRegistration = registrations.Layout;
                nativeCoreRegistration = registrations.Native;
            }

            byte[] artifact;
            if (command == "compile-trrm")
            {
                var trees = ImmutableArray.CreateBuilder<SyntaxTree>(paths.Count);
                foreach (var path in paths)
                {
                    trees.Add(SyntaxTree.Parse(ReadSource(path), path == "-" ? "<stdin>" : path));
                }
                var snapshots = trees.MoveToImmutable();
                var options = new CompilationOptions {
                    SourceArithmeticProfile = sourceArithmeticProfile,
                    RuntimePolicy = BoundRuntimePolicy.FailFast,
                    LayoutCoreRegistration = layoutCoreRegistration,
                    NativeCoreRegistration = nativeCoreRegistration,
                };
                var compilation = library
                    ? snapshots.Length == 1 ? Compilation.CreateLibrary(snapshots[0], options)
                        : Compilation.CreateLibrary(snapshots, options)
                    : snapshots.Length == 1 ? Compilation.Create(snapshots[0], options)
                        : Compilation.Create(snapshots, options);
                if (compilation.HasErrors)
                {
                    WriteCompilationResult(command, compilation, compilation.SourceDiagnostics,
                        false, null, null, json);
                    return 1;
                }
                var program = compilation.Program
                    ?? throw new InvalidOperationException("A successful compilation must contain a bound program.");
                var sourceModuleIdentity = identity
                    ?? throw new InvalidOperationException("Missing module identity.");
                if (schemaIdentity is null)
                {
                    artifact = TrRmExecutableCompiler.Emit(program,
                        sourceModuleIdentity);
                }
                else
                {
                    try
                    {
                        artifact = TrRmExecutableCompiler.EmitStandard(program,
                            sourceModuleIdentity, schemaIdentity, 1);
                    }
                    catch (ArgumentException exception)
                        when (exception.ParamName == "schemaIdentity")
                    {
                        WriteInputError(json, "TRM2000", exception.Message);
                        return 2;
                    }
                }
                var artifactPath = output ?? throw new InvalidOperationException("Missing artifact output path.");
                WriteArtifact(artifactPath, artifact);
                WriteTrRmSuccess(command, artifactPath, artifact.Length, json);
                return 0;
            }

            artifact = File.ReadAllBytes(args[1]);
            byte[] image;
            if (schemaIdentity is null)
            {
                var program = TrRmExecutableCompiler.Load(artifact);
                if (command == "run-trrm")
                {
                    if (program.EntryPoint < 0)
                    {
                        throw new InvalidDataException("A library TRRM artifact has no executable entry.");
                    }
                    return NativeJit.Execute(program, NativeRuntimePolicy.FailFast,
                        GraphJitRegistrarAssemblyPath());
                }

                if (command == "build-trrm-library")
                {
                    if (program.EntryPoint >= 0)
                    {
                        throw new InvalidDataException("Library emission requires a no-entry TRRM module.");
                    }
                    image = NativeEmitter.EmitLibrary(program, exports,
                        NativeRuntimePolicy.FailFast);
                }
                else
                {
                    if (program.EntryPoint < 0)
                    {
                        throw new InvalidDataException("An executable TRRM artifact requires an entry.");
                    }
                    image = NativeEmitter.Emit(program,
                        NativeRuntimePolicy.FailFast);
                }
            }
            else
            {
                var providerInputs
                    = new List<TrRmStandardArtifactInput>(providers.Count);
                foreach (var provider in providers)
                {
                    providerInputs.Add(new TrRmStandardArtifactInput(
                        provider.SchemaIdentity,
                        File.ReadAllBytes(provider.Path)));
                }

                var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
                    artifact);
                if (StringComparer.Ordinal.Equals(schemaIdentity,
                        RegisterExceptionRegionsSchemaIdentity))
                {
                    var program = TrRmExecutableCompiler.LoadStandard(rootInput,
                        moduleIdentity!, providerInputs,
                        selectedCoreExceptionIdentitySelections);
                    if (command == "run-trrm")
                    {
                        if (program.EntryPoint < 0)
                        {
                            throw new InvalidDataException(
                                "A library TRRM artifact has no executable entry.");
                        }

                        return NativeJit.Execute(program,
                            NativeRuntimePolicy.FailFast,
                            GraphJitRegistrarAssemblyPath());
                    }
                    else if (command == "build-trrm-library")
                    {
                        if (program.EntryPoint >= 0)
                        {
                            throw new InvalidDataException(
                                "Library emission requires a no-entry TRRM module.");
                        }

                        image = NativeEmitter.EmitLibrary(program, exports,
                            NativeRuntimePolicy.FailFast);
                    }
                    else
                    {
                        if (program.EntryPoint < 0)
                        {
                            throw new InvalidDataException(
                                "An executable TRRM artifact requires an entry.");
                        }

                        image = NativeEmitter.Emit(program,
                            NativeRuntimePolicy.FailFast);
                    }
                }
                else if (command == "run-trrm")
                {
                    try
                    {
                        return NativeModuleCompiler.ExecuteStandard(rootInput,
                            moduleIdentity!, providerInputs,
                            NativeRuntimePolicy.FailFast,
                            GraphJitRegistrarAssemblyPath());
                    }
                    catch (Exception exception)
                        when (StringComparer.Ordinal.Equals(schemaIdentity,
                                A3FixedArraySchemaIdentity)
                            && exception is InvalidDataException
                                or OverflowException or NotSupportedException)
                    {
                        a3AdmissionCallFailed = true;
                        throw;
                    }
                }
                else if (command == "build-trrm-library")
                {
                    try
                    {
                        image = NativeModuleCompiler.EmitStandardLibrary(
                            rootInput, moduleIdentity!, providerInputs, exports,
                            NativeRuntimePolicy.FailFast);
                    }
                    catch (Exception exception)
                        when (StringComparer.Ordinal.Equals(schemaIdentity,
                                A3FixedArraySchemaIdentity)
                            && exception is InvalidDataException
                                or OverflowException or NotSupportedException)
                    {
                        a3AdmissionCallFailed = true;
                        throw;
                    }
                }
                else
                {
                    try
                    {
                        image = NativeModuleCompiler.EmitStandardExecutable(
                            rootInput, moduleIdentity!, providerInputs,
                            NativeRuntimePolicy.FailFast);
                    }
                    catch (Exception exception)
                        when (StringComparer.Ordinal.Equals(schemaIdentity,
                                A3FixedArraySchemaIdentity)
                            && exception is InvalidDataException
                                or OverflowException or NotSupportedException)
                    {
                        a3AdmissionCallFailed = true;
                        throw;
                    }
                }
            }
            var imagePath = output ?? throw new InvalidOperationException("Missing native output path.");
            WriteArtifact(imagePath, image);
            WriteTrRmSuccess(command, imagePath, image.Length, json);
            return 0;
        }
        catch (Exception exception)
            when (TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
                out _))
        {
            _ = TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
                out var failureKind);
            var (code, status) = failureKind switch
            {
                TrRmA3AdmissionFailureKind.InvalidArtifact
                    => ("TRM5001", 1),
                TrRmA3AdmissionFailureKind.TargetLayoutOverflow
                    => ("TRM5005", 1),
                TrRmA3AdmissionFailureKind.UnsupportedAdmission
                    => ("TRM5006", 1),
                TrRmA3AdmissionFailureKind.ResourceLimit
                    => ("TRM5007", 2),
                _ => throw new InvalidOperationException(
                    "The A3 admission failure kind is not supported.")
            };
            WriteInputError(json, code, exception.Message);
            return status;
        }
        catch (Exception) when (a3AdmissionCallFailed)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or (ArgumentException and not DecoderFallbackException)
            or NotSupportedException)
        {
            WriteInputError(json, "TRM5001", exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or DecoderFallbackException or FormatException)
        {
            WriteInputError(json, "TRM2001", exception.Message);
            return 2;
        }
    }

    private static void WriteTrRmSuccess(string command, string path, int bytes, bool json)
    {
        if (!json)
        {
            Console.Out.WriteLine($"{path}: {bytes} bytes ({command}, Windows x64, fail-fast)");
            return;
        }
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(),
            new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("command", command);
        writer.WriteString("runtimePolicy", "fail-fast");
        writer.WriteBoolean("success", true);
        writer.WriteStartArray("diagnostics");
        writer.WriteEndArray();
        writer.WriteStartObject("artifact");
        writer.WriteString("path", path);
        writer.WriteNumber("length", bytes);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
