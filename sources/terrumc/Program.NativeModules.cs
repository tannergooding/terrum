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
    private const string NativeModuleUsage = "Usage: terrumc compile-native producer <source> <identity> --export <name> [--export <name>] [--source <source>] -o <producer.trmn> --runtime fail-fast [--json]; "
        + "terrumc compile-native consumer <source> <producer.trmn> [--source <source>] -o <consumer.trmn> --runtime fail-fast [--json]; "
        + "terrumc validate-native <consumer.trmn> <producer.trmn> [--json]; "
        + "terrumc run-native <consumer.trmn> <producer.trmn> --runtime fail-fast [--json]";

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
                if (!TryReadModuleOptions(args, count, out var paths, out var exports, out var output))
                {
                    WriteInputError(json, "TRM2000", NativeModuleUsage);
                    return 2;
                }
                var producer = args[1] == "producer";
                var outputPath = Path.GetFullPath(output);
                if (!Path.GetExtension(outputPath).Equals(".trmn", StringComparison.OrdinalIgnoreCase)
                    || paths.Any(path => Path.GetFullPath(path).Equals(outputPath, StringComparison.OrdinalIgnoreCase))
                    || (!producer && Path.GetFullPath(args[3]).Equals(outputPath, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ModuleFormatException("Native module output must be a distinct .trmn path, not any source or producer input.");
                }
                var snapshots = ReadModuleSources(paths);
                var reference = producer ? null : ReadModuleFile(args[3], 16 * 1024 * 1024);
                var compilation = reference is null ? Compilation.CreateLibrary(snapshots)
                    : Compilation.Create(snapshots, NativeModuleCompiler.CreateReference(reference));
                if (compilation.HasErrors)
                {
                    WriteCompilationResult(args[0], compilation, compilation.SourceDiagnostics, false, null, null, json);
                    return 1;
                }
                if (compilation.Program is not BoundProgram program)
                {
                    throw new InvalidOperationException("A successful compilation must contain a bound program.");
                }
                var bytes = reference is null
                    ? NativeModuleCompiler.CreateProducer(program, exports, args[3], NativeRuntimePolicy.FailFast)
                    : NativeModuleCompiler.CreateConsumer(program, reference, NativeRuntimePolicy.FailFast);
                var info = NativeModuleCompiler.Inspect(bytes);
                WriteArtifact(outputPath, bytes);
                if (json)
                {
                    WriteNativeModuleArtifactJson(args[0], outputPath, bytes, info);
                }
                else
                {
                    Console.WriteLine($"{outputPath}: {info.Identity} native {(info.IsConsumer ? "consumer" : "producer")} (fail-fast runtime)");
                }
                return 0;
            }
            if (count == 3 && args[0] == "validate-native")
            {
                NativeModuleCompiler.Validate(ReadModuleFile(args[1], 16 * 1024 * 1024),
                    ReadModuleFile(args[2], 16 * 1024 * 1024));
                if (json)
                {
                    using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
                    writer.WriteStartObject();
                    writer.WriteNumber("schemaVersion", 1);
                    writer.WriteString("profile", Compilation.ProfileVersion);
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
            if (count == 5 && args[0] == "run-native" && args[3] == "--runtime" && args[4] == "fail-fast")
            {
                return NativeModuleCompiler.RunAsync(ReadModuleFile(args[1], 16 * 1024 * 1024),
                    ReadModuleFile(args[2], 16 * 1024 * 1024), TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
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

    private static void WriteNativeModuleArtifactJson(string command, string path, byte[] bytes, NativeModuleInfo info)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("profile", Compilation.ProfileVersion);
        writer.WriteString("command", command);
        writer.WriteBoolean("success", true);
        writer.WriteStartArray("diagnostics");
        writer.WriteEndArray();
        writer.WriteStartObject("artifact");
        writer.WriteString("path", path);
        writer.WriteString("kind", info.IsConsumer ? "native-consumer" : "native-producer");
        writer.WriteString("identity", info.Identity);
        writer.WriteString("runtimePolicy", "fail-fast");
        writer.WriteNumber("length", bytes.Length);
        writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(bytes)));
        writer.WriteNumber("imageLength", info.ImageLength);
        writer.WriteString("imageSha256", info.ImageSha256);
        writer.WriteNumber("contractLength", info.ContractLength);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
