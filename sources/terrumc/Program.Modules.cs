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
    private const string ModuleUsage = "Usage: terrumc compile-module producer <source> <export> [--source <source>] [--export <export>] -o <artifact.trm> [--json]; "
        + "terrumc compile-module consumer <source> <producer.trm> [--source <source>] -o <artifact.trm> [--json]; "
        + "terrumc link-module <consumer.trm> <producer.trm> -o <output.exe> --runtime fail-fast [--json]; "
        + "terrumc run-module <consumer.trm> <producer.trm> --runtime fail-fast [--json]";

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
            if (count >= 6 && args[0] == "compile-module"
                && (args[1] is "producer" or "consumer"))
            {
                if (!TryReadModuleOptions(args, count, out var paths, out var exports, out output))
                {
                    WriteInputError(json, "TRM2000", ModuleUsage);
                    return 2;
                }
                var requestedPath = Path.GetFullPath(output);
                if (paths.Any(path => Path.GetFullPath(path).Equals(requestedPath, StringComparison.OrdinalIgnoreCase))
                    || ((args[1] == "consumer") && Path.GetFullPath(args[3]).Equals(requestedPath, StringComparison.OrdinalIgnoreCase))
                    || !Path.GetExtension(output).Equals(".trm", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ModuleFormatException("Module output must be a distinct .trm artifact.");
                }
                var snapshots = ReadModuleSources(paths);
                var reference = args[1] == "consumer" ? ReadModuleFile(args[3]) : null;
                var declarations = reference is null ? null : ModuleCompiler.CreateReference(reference);
                var compilation = snapshots.Length == 1
                    ? declarations is null ? Compilation.CreateLibrary(snapshots[0]) : Compilation.Create(snapshots[0], declarations)
                    : declarations is null ? Compilation.CreateLibrary(snapshots) : Compilation.Create(snapshots, declarations);
                if (compilation.HasErrors)
                {
                    WriteModuleSourceDiagnostics(compilation, json);
                    return 1;
                }
                if (compilation.Program is not BoundProgram program)
                {
                    throw new InvalidOperationException("A successful compilation must contain a bound program.");
                }
                result = args[1] switch {
                    "producer" => ModuleCompiler.CreateProducer(program, exports),
                    "consumer" => ModuleCompiler.CreateConsumer(program,
                        reference ?? throw new InvalidOperationException("Consumer reference is missing.")),
                    _ => throw new InvalidOperationException("Unknown module kind."),
                };
            }
            else if (count == 7 && args[0] == "link-module" && args[3] == "-o"
                && args[5] == "--runtime" && args[6] == "fail-fast")
            {
                if (Path.GetFullPath(args[4]).Equals(Path.GetFullPath(args[1]), StringComparison.OrdinalIgnoreCase)
                    || Path.GetFullPath(args[4]).Equals(Path.GetFullPath(args[2]), StringComparison.OrdinalIgnoreCase)
                    || !Path.GetExtension(args[4]).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ModuleFormatException("Linked output must be a distinct .exe path.");
                }
                var program = ModuleCompiler.Link(ReadModuleFile(args[1]), ReadModuleFile(args[2]));
                result = NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast);
                output = args[4];
            }
            else if (count == 5 && args[0] == "run-module"
                && args[3] == "--runtime" && args[4] == "fail-fast")
            {
                var program = ModuleCompiler.Link(ReadModuleFile(args[1]), ReadModuleFile(args[2]));
                return NativeJit.Execute(program, NativeRuntimePolicy.FailFast);
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
                WriteModuleArtifactJson(args[0], outputPath, result);
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
        out List<string> exports, out string output)
    {
        sources = [args[2]];
        var native = args[0] == "compile-native";
        exports = args[1] == "producer" && !native ? [args[3]] : [];
        output = "";
        var hasOutput = false;
        var hasRuntime = false;
        if ((count & 1) != 0)
        {
            return false;
        }
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(args[2]) };
        var exportNames = new HashSet<string>(exports, StringComparer.Ordinal);
        for (var index = 4; index < count; index += 2)
        {
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
                case "--runtime" when native && !hasRuntime && value == "fail-fast":
                    hasRuntime = true;
                    break;
                default:
                    return false;
            }
        }
        return hasOutput && (!native || (hasRuntime && (args[1] != "producer" || exports.Count != 0)));
    }

    private static void WriteModuleSourceDiagnostics(Compilation compilation, bool json)
        => WriteCompilationResult("compile-module", compilation, compilation.SourceDiagnostics, false, null, null, json);

    private static void WriteModuleArtifactJson(string command, string outputPath, byte[] result)
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
        writer.WriteString("path", outputPath);
        writer.WriteString("kind", command == "link-module" ? "windows-x64-executable" : "semantic-module");
        if (command == "link-module")
        {
            writer.WriteString("runtimePolicy", "fail-fast");
        }
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

    private static ImmutableArray<SyntaxTree> ReadModuleSources(List<string> paths)
    {
        var trees = ImmutableArray.CreateBuilder<SyntaxTree>(paths.Count);
        var sourceBytes = 0;
        foreach (var path in paths)
        {
            var source = ReadModuleSource(path);
            sourceBytes += Encoding.UTF8.GetByteCount(source);
            if (sourceBytes > 4 * 1024 * 1024)
            {
                throw new ModuleFormatException("Combined module source exceeds the 4 MiB limit.");
            }
            trees.Add(SyntaxTree.Parse(source, path));
        }

        return trees.MoveToImmutable();
    }

    private static string ReadModuleSource(string path)
    {
        if (path == "-")
        {
            throw new ArgumentException("Module source must be a bounded file path, not stdin.");
        }
        var bytes = ReadModuleFile(path);
        var text = new UTF8Encoding(false, true).GetString(bytes);
        return text.StartsWith('\uFEFF', StringComparison.Ordinal) ? text[1..] : text;
    }
}
