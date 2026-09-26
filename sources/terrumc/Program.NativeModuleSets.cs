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
    private const string NativeSetUsage = "Usage: terrumc compile-native-set <source> --provider <producer.trmn> --provider <producer.trmn> -o <consumer.tnset> --runtime <fail-fast|checked-exceptions> [--source <source>] [--json]; "
        + "terrumc validate-native-set <consumer.tnset> --provider <producer.trmn> --provider <producer.trmn> [--json]; "
        + "terrumc run-native-set <consumer.tnset> --provider <producer.trmn> --provider <producer.trmn> --runtime <fail-fast|checked-exceptions> [--json]";

    private static int RunNativeSetCommand(string[] args, bool json)
    {
        var count = args.Length - (args.Length > 0 && args[^1] == "--json" ? 1 : 0);
        if (count < 6 || (json && (count == args.Length || Array.IndexOf(args, "--json") != args.Length - 1)))
        {
            WriteInputError(json, "TRM2000", NativeSetUsage);
            return 2;
        }
        var compile = args[0] == "compile-native-set";
        var run = args[0] == "run-native-set";
        var sources = new List<string> { args[1] };
        var providers = new List<string>();
        string? output = null;
        NativeRuntimePolicy? policy = null;
        for (var i = 2; i < count - 1; i += 2)
        {
            switch (args[i])
            {
                case "--provider" when providers.Count < 16:
                    providers.Add(args[i + 1]);
                    break;
                case "--source" when compile && sources.Count < 256:
                    sources.Add(args[i + 1]);
                    break;
                case "-o" when compile && output is null:
                    output = args[i + 1];
                    break;
                case "--runtime" when (compile || run) && policy is null
                    && args[i + 1] is "fail-fast" or "checked-exceptions":
                    policy = args[i + 1] == "fail-fast"
                        ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
                    break;
                default:
                    WriteInputError(json, "TRM2000", NativeSetUsage);
                    return 2;
            }
        }
        if (((count - 2) & 1) != 0 || providers.Count < 2 || (compile && (output is null || policy is null))
            || (run && policy is null) || (!compile && output is not null))
        {
            WriteInputError(json, "TRM2000", NativeSetUsage);
            return 2;
        }
        try
        {
            if (compile && (!Path.GetExtension(output!).Equals(".tnset", StringComparison.OrdinalIgnoreCase)
                || sources.Concat(providers).Any(path => Path.GetFullPath(path).Equals(Path.GetFullPath(output!),
                    StringComparison.OrdinalIgnoreCase))))
            {
                throw new ModuleFormatException("Native set output must be a distinct .tnset artifact.");
            }
            var providerBytes = providers.Select(path => ReadModuleFile(path, 16 * 1024 * 1024)).ToArray();
            if (compile)
            {
                var snapshots = ReadModuleSources(sources);
                var compilation = Compilation.Create(snapshots, NativeModuleCompiler.CreateReferenceSet(providerBytes),
                    (BoundRuntimePolicy)policy!.Value);
                if (compilation.HasErrors)
                {
                    WriteCompilationResult(args[0], compilation, compilation.SourceDiagnostics, false, null, null,
                        json, policy.Value);
                    return 1;
                }
                var program = compilation.Program ?? throw new InvalidOperationException("Successful compilation has no program.");
                var artifact = NativeModuleCompiler.CreateConsumerSet(program, providerBytes, policy.Value);
                var outputPath = Path.GetFullPath(output!);
                WriteArtifact(outputPath, artifact);
                if (json)
                {
                    WriteNativeSetJson(args[0], outputPath, artifact,
                        NativeModuleCompiler.InspectConsumerSet(artifact));
                }
                else
                {
                    Console.WriteLine($"{outputPath}: native consumer set ({providerBytes.Length} providers).");
                }
                return 0;
            }
            var consumer = ReadModuleFile(args[1], 16 * 1024 * 1024);
            if (run)
            {
                return NativeModuleCompiler.RunConsumerSetAsync(consumer, providerBytes, TimeSpan.FromSeconds(30),
                    policy!.Value).GetAwaiter().GetResult();
            }
            NativeModuleCompiler.ValidateConsumerSet(consumer, providerBytes);
            if (json)
            {
                WriteNativeSetJson(args[0], Path.GetFullPath(args[1]), consumer,
                    NativeModuleCompiler.InspectConsumerSet(consumer));
            }
            else
            {
                Console.WriteLine($"{args[1]}: compatible with {providerBytes.Length} producers.");
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

    private static void WriteNativeSetJson(string command, string path, byte[] bytes, NativeModuleSetInfo info)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("command", command);
        writer.WriteBoolean("success", true);
        writer.WriteString("profile", info.NativeProfileVersion);
        writer.WriteString("runtimePolicy", PolicyName(info.RuntimePolicy));
        writer.WriteStartArray("diagnostics");
        writer.WriteEndArray();
        writer.WriteStartObject("artifact");
        writer.WriteString("path", path);
        writer.WriteString("kind", "native-consumer-set");
        writer.WriteString("runtimePolicy", PolicyName(info.RuntimePolicy));
        writer.WriteNumber("length", bytes.Length);
        writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(bytes)));
        writer.WriteString("imageSha256", info.ImageSha256);
        writer.WriteStartArray("dependencies");
        foreach (var dependency in info.Dependencies)
        {
            writer.WriteStringValue(dependency);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
