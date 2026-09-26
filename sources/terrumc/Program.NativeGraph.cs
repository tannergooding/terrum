// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
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
    private const string NativeGraphUsage = "Usage: terrumc compile-native-graph producer <source> <identity> --provider <dependency.trmn|dependency.trng> --export <name> -o <producer.trng> --runtime <fail-fast|checked-exceptions> [--source <source>] [--json]; "
        + "terrumc compile-native-graph consumer <source> --provider <dependency.trmn|dependency.trng> -o <consumer.trng> --runtime <fail-fast|checked-exceptions> [--source <source>] [--json]; "
        + "terrumc validate-native-graph <consumer.trng> --provider <selected.trmn|selected.trng> [--provider <selected>] [--json]; "
        + "terrumc run-native-graph <consumer.trng> --provider <selected.trmn|selected.trng> [--provider <selected>] --runtime <fail-fast|checked-exceptions> [--json]";

    private static int RunNativeGraphCommand(string[] args, bool json)
    {
        var count = args.Length - (args.Length != 0 && args[^1] == "--json" ? 1 : 0);
        var compile = args.Length != 0 && args[0] == "compile-native-graph";
        var producer = compile && args.Length > 1 && args[1] == "producer";
        var consumer = compile && args.Length > 1 && args[1] == "consumer";
        var run = args.Length != 0 && args[0] == "run-native-graph";
        if (count < (producer ? 5 : compile ? 4 : 3) || (compile && !producer && !consumer)
            || (json && (count == args.Length || Array.IndexOf(args, "--json") != args.Length - 1)))
        {
            WriteInputError(json, "TRM2000", NativeGraphUsage);
            return 2;
        }
        var input = args[compile ? 2 : 1];
        var identity = producer ? args[3] : null;
        var sources = new List<string> { input };
        var providers = new List<string>();
        var exports = new List<string>();
        string? output = null;
        NativeRuntimePolicy? policy = null;
        var firstOption = producer ? 4 : compile ? 3 : 2;
        if (((count - firstOption) & 1) != 0)
        {
            WriteInputError(json, "TRM2000", NativeGraphUsage);
            return 2;
        }
        for (var index = firstOption; index < count; index += 2)
        {
            var value = args[index + 1];
            switch (args[index])
            {
                case "--provider" when providers.Count < 16:
                    providers.Add(value);
                    break;
                case "--source" when compile && sources.Count < 256:
                    sources.Add(value);
                    break;
                case "--export" when producer && exports.Count < 1024:
                    exports.Add(value);
                    break;
                case "-o" when compile && output is null:
                    output = value;
                    break;
                case "--runtime" when (compile || run) && policy is null
                    && value is "fail-fast" or "checked-exceptions":
                    policy = value == "fail-fast"
                        ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
                    break;
                default:
                    WriteInputError(json, "TRM2000", NativeGraphUsage);
                    return 2;
            }
        }
        if (providers.Count == 0 || (compile && (output is null || policy is null))
            || (producer && exports.Count == 0) || (!producer && exports.Count != 0)
            || (run && policy is null) || (!compile && output is not null))
        {
            WriteInputError(json, "TRM2000", NativeGraphUsage);
            return 2;
        }
        try
        {
            if (compile && (!Path.GetExtension(output!).Equals(".trng", StringComparison.OrdinalIgnoreCase)
                || sources.Concat(providers).Any(path => Path.GetFullPath(path).Equals(Path.GetFullPath(output!),
                    StringComparison.OrdinalIgnoreCase))))
            {
                throw new ModuleFormatException("Graph output must be a distinct .trng artifact.");
            }
            var packages = providers.Select(path => ReadModuleFile(path, 16 * 1024 * 1024)).ToArray();
            if (compile)
            {
                var reference = NativeModuleCompiler.CreateGraphReference(packages);
                var snapshots = ReadModuleSources(sources);
                var compilation = producer
                    ? Compilation.CreateLibrary(snapshots, reference, (BoundRuntimePolicy)policy!.Value)
                    : Compilation.Create(snapshots, reference, (BoundRuntimePolicy)policy!.Value);
                if (compilation.HasErrors)
                {
                    WriteCompilationResult(args[0], compilation, compilation.SourceDiagnostics, false, null, null,
                        json, policy!.Value);
                    return 1;
                }
                var program = compilation.Program
                    ?? throw new InvalidOperationException("Successful graph compilation has no bound program.");
                var artifact = producer
                    ? NativeModuleCompiler.CreateGraphProducer(program, exports, identity!, packages, policy!.Value)
                    : NativeModuleCompiler.CreateGraphConsumer(program, packages, policy!.Value);
                var info = NativeModuleCompiler.InspectGraphArtifact(artifact);
                var path = Path.GetFullPath(output!);
                WriteArtifact(path, artifact);
                WriteGraphSuccess(args[0], path, artifact, info, json);
                return 0;
            }
            var root = ReadModuleFile(input, 16 * 1024 * 1024);
            if (run)
            {
                return NativeModuleCompiler.RunGraphAsync(root, packages, TimeSpan.FromSeconds(30), policy!.Value)
                    .GetAwaiter().GetResult();
            }
            NativeModuleCompiler.ValidateGraph(root, packages);
            WriteGraphSuccess(args[0], Path.GetFullPath(input), root,
                NativeModuleCompiler.InspectGraphArtifact(root), json);
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

    private static void WriteGraphSuccess(string command, string path, byte[] bytes, NativeGraphInfo info, bool json)
    {
        if (!json)
        {
            Console.WriteLine($"{path}: native graph {(info.IsConsumer ? "consumer" : "producer")} checked.");
            return;
        }
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("command", command);
        writer.WriteString("profile", info.NativeProfileVersion);
        writer.WriteString("runtimePolicy", PolicyName(info.RuntimePolicy));
        writer.WriteBoolean("success", true);
        writer.WriteStartArray("diagnostics");
        writer.WriteEndArray();
        writer.WriteStartObject("artifact");
        writer.WriteString("path", path);
        writer.WriteString("kind", info.IsConsumer ? "native-graph-consumer" : "native-graph-producer");
        if (info.Identity is string identity)
        {
            writer.WriteString("identity", identity);
        }
        writer.WriteNumber("length", bytes.Length);
        writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(bytes)));
        writer.WriteString("imageSha256", info.ImageSha256);
        writer.WriteStartArray("dependencies");
        foreach (var dependency in info.Dependencies)
        {
            writer.WriteStringValue(dependency);
        }
        writer.WriteEndArray();
        writer.WriteStartArray("exports");
        foreach (var exported in info.Exports)
        {
            writer.WriteStringValue(exported);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
