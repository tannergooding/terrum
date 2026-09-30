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
        var library = false;
        var runtime = false;
        var paths = new List<string> { args[1] };
        var exports = new List<string>();
        var uniqueExports = new HashSet<string>(StringComparer.Ordinal);
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
                if (i + 1 == count)
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
                    case "--source" when command == "compile-trrm" && paths.Count < 256
                        && !paths.Contains(value, StringComparer.OrdinalIgnoreCase):
                        paths.Add(value);
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
                    default:
                        WriteInputError(json, "TRM2000", Usage);
                        return 2;
                }
                i += 2;
            }
            if (!runtime || command == "compile-trrm" && identity is null
                || command is "build-trrm" or "build-trrm-library" && args[1] == "-"
                || command == "run-trrm" && (output is not null || args[1] == "-")
                || command != "run-trrm" && output is null
                || command == "build-trrm-library" && exports.Count == 0
                || output is not null && (!Path.GetExtension(output).Equals(
                    command switch { "compile-trrm" => ".trrm", "build-trrm" => ".exe", _ => ".dll" },
                    StringComparison.OrdinalIgnoreCase)
                    || paths.Any(path => path != "-" && Path.GetFullPath(path).Equals(output, StringComparison.OrdinalIgnoreCase))))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }

            byte[] artifact;
            BoundProgram program;
            if (command == "compile-trrm")
            {
                var trees = ImmutableArray.CreateBuilder<SyntaxTree>(paths.Count);
                foreach (var path in paths)
                {
                    trees.Add(SyntaxTree.Parse(ReadSource(path), path == "-" ? "<stdin>" : path));
                }
                var snapshots = trees.MoveToImmutable();
                var compilation = library
                    ? snapshots.Length == 1 ? Compilation.CreateLibrary(snapshots[0])
                        : Compilation.CreateLibrary(snapshots)
                    : snapshots.Length == 1 ? Compilation.Create(snapshots[0])
                        : Compilation.Create(snapshots);
                if (compilation.HasErrors)
                {
                    WriteCompilationResult(command, compilation, compilation.SourceDiagnostics,
                        false, null, null, json);
                    return 1;
                }
                program = compilation.Program
                    ?? throw new InvalidOperationException("A successful compilation must contain a bound program.");
                artifact = TrRmExecutableCompiler.Emit(program,
                    identity ?? throw new InvalidOperationException("Missing module identity."));
                var artifactPath = output ?? throw new InvalidOperationException("Missing artifact output path.");
                WriteArtifact(artifactPath, artifact);
                WriteTrRmSuccess(command, artifactPath, artifact.Length, json);
                return 0;
            }

            artifact = File.ReadAllBytes(args[1]);
            program = TrRmExecutableCompiler.Load(artifact);
            if (command == "run-trrm")
            {
                if (program.EntryPoint < 0)
                {
                    throw new InvalidDataException("A library TRRM artifact has no executable entry.");
                }
                return NativeJit.Execute(program, NativeRuntimePolicy.FailFast, GraphJitRegistrarAssemblyPath());
            }
            byte[] image;
            if (command == "build-trrm-library")
            {
                if (program.EntryPoint >= 0)
                {
                    throw new InvalidDataException("Library emission requires a no-entry TRRM module.");
                }
                image = NativeEmitter.EmitLibrary(program, exports, NativeRuntimePolicy.FailFast);
            }
            else
            {
                if (program.EntryPoint < 0)
                {
                    throw new InvalidDataException("An executable TRRM artifact requires an entry.");
                }
                image = NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast);
            }
            var imagePath = output ?? throw new InvalidOperationException("Missing native output path.");
            WriteArtifact(imagePath, image);
            WriteTrRmSuccess(command, imagePath, image.Length, json);
            return 0;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException and not DecoderFallbackException
            or NotSupportedException)
        {
            WriteInputError(json, "TRM5001", exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or DecoderFallbackException)
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
