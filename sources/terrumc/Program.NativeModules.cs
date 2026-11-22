// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.Tools;

internal static partial class Program
{
    private const string NativeModuleUsage = "Usage: terrumc compile-native producer <source> <identity> --export <name> [--export <name>] [--source <source>] -o <producer.trmn> --runtime <fail-fast|checked-exceptions> [--arithmetic-profile <development|release>] [--native-core-manifest <path>] [--native-output-core-manifest <path>] [--private-identity-manifest <path>] [--private-schema <exact-schema-identity>] [--corelib] [--json]; "
        + "terrumc compile-native consumer <source> <producer.trmn> [--source <source>] -o <consumer.trmn> --runtime <fail-fast|checked-exceptions> [--arithmetic-profile <development|release>] [--native-core-manifest <path>] [--native-output-core-manifest <path>] [--private-identity-manifest <path>] [--private-schema <exact-schema-identity>] [--json]; "
        + "terrumc validate-native <consumer.trmn> <producer.trmn> [--json]; "
        + "terrumc run-native <consumer.trmn> <producer.trmn> --runtime <fail-fast|checked-exceptions> [--json]";

    private static int RunNativeModuleCommand(string[] args, bool json)
    {
        var count = args.Length - (args.Length != 0 && args[^1] == "--json" ? 1 : 0);
        if (json && Array.IndexOf(args, "--json") != args.Length - 1)
        {
            WriteInputError(json, "TRM2000", NativeModuleUsage);
            return 2;
        }

        try
        {
            if (count >= 8 && args[0] == "compile-native" && args[1] is "producer" or "consumer")
            {
                if (!TryReadModuleOptions(args, count, out var paths, out var exports, out var output,
                    out var runtimePolicy, out var sourceArithmeticProfile, out var coreLibrary,
                    out _, out var nativeCoreManifestPath,
                    out var nativeOutputCoreManifestPath,
                    out var privateIdentityManifestPath,
                    out var privateSchemaIdentity,
                    out var coreManifestOrder))
                {
                    WriteInputError(json, "TRM2000", NativeModuleUsage);
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
                var producer = args[1] == "producer";
                var outputPath = Path.GetFullPath(output);
                var producerPath = producer ? null : args[3];
                var pathAliasError = GetModuleManifestPathAliasError(paths,
                    producerPath, outputPath, nativeCoreManifestPath,
                    nativeOutputCoreManifestPath,
                    privateIdentityManifestPath);
                if (pathAliasError is not null)
                {
                    WriteInputError(json, "TRM2002", pathAliasError);
                    return 2;
                }
                if (!Path.GetExtension(outputPath).Equals(".trmn", StringComparison.OrdinalIgnoreCase)
                    || paths.Any(path => path != "-"
                        && PathsAlias(outputPath, path))
                    || (!producer && PathsAlias(args[3], outputPath)))
                {
                    throw new ModuleFormatException("Native module output must be a distinct .trmn path, not any source or producer input.");
                }
                PrivateModuleIdentityManifest? privateIdentityManifest = null;
                if (privateIdentityManifestPath is not null)
                {
                    try
                    {
                        privateIdentityManifest
                            = PrivateModuleIdentityManifestReader.Read(
                                privateIdentityManifestPath, producer,
                                paths.Count);
                    }
                    catch (Exception exception) when (exception is IOException
                        or UnauthorizedAccessException
                        or DecoderFallbackException or ArgumentException
                        or NotSupportedException or FormatException
                        or JsonException)
                    {
                        WriteInputError(json, "TRM2001", exception.Message);
                        return 2;
                    }
                    if (privateIdentityManifest.SchemaVersion != 2)
                    {
                        WriteInputError(json, "TRM2000",
                            "compile-native requires private identity manifest version 2.");
                        return 2;
                    }
                    if (privateSchemaIdentity
                        != ModuleCompiler.ForeignInterfacePrivateSchemaIdentity)
                    {
                        WriteInputError(json, "TRM2001",
                            "Private identity manifest version 2 requires the exact PF111 private-schema selection.");
                        return 2;
                    }
                    if (producer
                        && privateIdentityManifest.UsingModuleIdentity
                            != args[3])
                    {
                        WriteInputError(json, "TRM2000",
                            "The native producer identity must equal the v2 private identity manifest using Module identity.");
                        return 2;
                    }
                }

                CoreManifestDefinitions coreDefinitions;
                CoreManifestRegistrationComposition coreRegistrationComposition;
                try
                {
                    coreDefinitions = ReadPrivateCompilationCoreDefinitions(
                        layoutPath: null,
                        nativePath: nativeCoreManifestPath,
                        nativeOutputPath: nativeOutputCoreManifestPath,
                        order: coreManifestOrder,
                        identityManifest: privateIdentityManifest);
                    coreRegistrationComposition
                        = CoreManifestRegistrationComposer.Create(
                            coreDefinitions.Layout, coreDefinitions.Native,
                            coreDefinitions.NativeOutput,
                            layoutPath: null,
                            nativePath: nativeCoreManifestPath,
                            nativeOutputPath:
                                nativeOutputCoreManifestPath);
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException or DecoderFallbackException
                    or ArgumentException or NotSupportedException
                    or FormatException)
                {
                    WriteInputError(json, "TRM2001", exception.Message);
                    return 2;
                }
                var selectedUsingModuleIdentity
                    = coreDefinitions.Layout?.UsingModuleIdentity
                        ?? coreDefinitions.Native?.UsingModuleIdentity
                        ?? coreDefinitions.NativeOutput?.UsingModuleIdentity;
                if (producer
                    && selectedUsingModuleIdentity is string configuredUsing
                    && configuredUsing != args[3])
                {
                    WriteInputError(json, "TRM2000",
                        "A native producer identity must equal every selected Core manifest using Module identity.");
                    return 2;
                }
                if (!producer && privateIdentityManifest is not null
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
                var reference = producer ? null : ReadModuleFile(args[3], 16 * 1024 * 1024);
                if (privateIdentityManifest is not null
                    && reference is not null
                    && Convert.ToHexString(SHA256.HashData(reference))
                        != privateIdentityManifest.ReferenceProducerSha256)
                {
                    WriteInputError(json, "TRM2001",
                        "The native producer package does not match the captured private identity manifest snapshot.");
                    return 2;
                }
                var declarations = reference is null ? null
                    : privateSchemaIdentity is null
                        ? NativeModuleCompiler.CreateReference(reference)
                        : NativeModuleCompiler.CreateReferenceWithSchema(
                            reference, privateSchemaIdentity);
                var options = new CompilationOptions {
                    SourceArithmeticProfile = sourceArithmeticProfile,
                    RuntimePolicy = (BoundRuntimePolicy)runtimePolicy,
                    NativeCoreRegistration =
                        coreRegistrationComposition.Native,
                    NativeOutputCoreRegistration =
                        coreRegistrationComposition.NativeOutput,
                };
                var compilation = coreLibrary
                    ? Compilation.CreateCoreLibrary(snapshots, options)
                    : declarations is null
                        ? Compilation.CreateLibrary(snapshots, options)
                        : Compilation.Create(snapshots, declarations, options);
                if (compilation.HasErrors)
                {
                    WriteCompilationResult(args[0], compilation, compilation.SourceDiagnostics, false, null, null, json,
                        runtimePolicy);
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
                var bytes = reference is null
                    ? privateSchemaIdentity is null
                        ? NativeModuleCompiler.CreateProducer(program, exports,
                            args[3], runtimePolicy)
                        : NativeModuleCompiler.CreateProducerWithSchema(
                            program, exports, args[3], privateSchemaIdentity,
                            runtimePolicy, identityInputs)
                    : privateSchemaIdentity is null
                        ? NativeModuleCompiler.CreateConsumer(program,
                            reference, runtimePolicy)
                        : NativeModuleCompiler.CreateConsumerWithSchema(
                            program, reference, privateSchemaIdentity,
                            runtimePolicy, identityInputs);
                var info = NativeModuleCompiler.Inspect(bytes);
                WriteArtifact(outputPath, bytes);
                if (json)
                {
                    WriteNativeModuleArtifactJson(args[0], outputPath, bytes, info, runtimePolicy);
                }
                else
                {
                    Console.WriteLine($"{outputPath}: {info.Identity} native {(info.IsConsumer ? "consumer" : "producer")} ({PolicyName(runtimePolicy)} runtime)");
                }
                return 0;
            }
            if (count == 3 && args[0] == "validate-native")
            {
                var consumer = ReadModuleFile(args[1], 16 * 1024 * 1024);
                NativeModuleCompiler.Validate(consumer,
                    ReadModuleFile(args[2], 16 * 1024 * 1024));
                if (json)
                {
                    var info = NativeModuleCompiler.Inspect(consumer);
                    using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
                    writer.WriteStartObject();
                    writer.WriteNumber("schemaVersion", 1);
                    writer.WriteString("profile", info.NativeProfileVersion);
                    writer.WriteString("runtimePolicy", PolicyName(info.RuntimePolicy));
                    writer.WriteString("command", args[0]);
                    writer.WriteBoolean("success", true);
                    writer.WriteStartArray("diagnostics");
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                else
                {
                    Console.WriteLine($"{args[1]}: compatible with {args[2]}.");
                }
                return 0;
            }
            if (count == 5 && args[0] == "run-native" && args[3] == "--runtime"
                && args[4] is "fail-fast" or "checked-exceptions")
            {
                return NativeModuleCompiler.RunAsync(ReadModuleFile(args[1], 16 * 1024 * 1024),
                    ReadModuleFile(args[2], 16 * 1024 * 1024), TimeSpan.FromSeconds(30),
                    args[4] == "fail-fast" ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions)
                    .GetAwaiter().GetResult();
            }

            WriteInputError(json, "TRM2000", NativeModuleUsage);
            return 2;
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
        catch (TimeoutException exception)
        {
            WriteInputError(json, "TRM5004", exception.Message);
            return 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            WriteInputError(json, "TRM5003", exception.Message);
            return 2;
        }
    }

    private static void WriteNativeModuleArtifactJson(string command, string path, byte[] bytes, NativeModuleInfo info,
        NativeRuntimePolicy policy)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("profile", info.NativeProfileVersion);
        writer.WriteString("runtimePolicy", PolicyName(policy));
        writer.WriteString("command", command);
        writer.WriteBoolean("success", true);
        writer.WriteStartArray("diagnostics");
        writer.WriteEndArray();
        writer.WriteStartObject("artifact");
        writer.WriteString("path", path);
        writer.WriteString("kind", info.IsConsumer ? "native-consumer" : "native-producer");
        writer.WriteString("identity", info.Identity);
        writer.WriteString("runtimePolicy", PolicyName(policy));
        writer.WriteNumber("length", bytes.Length);
        writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(bytes)));
        writer.WriteNumber("imageLength", info.ImageLength);
        writer.WriteString("imageSha256", info.ImageSha256);
        writer.WriteNumber("contractLength", info.ContractLength);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
