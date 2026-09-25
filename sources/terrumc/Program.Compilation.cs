// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.Tools;

internal static partial class Program
{
    private static int RunCompilation(string[] args, bool json)
    {
        ImmutableArray<SyntaxTree> snapshots;
        string? outputPath;
        try
        {
            if (!TryReadCompilationOptions(args, out var paths, out outputPath))
            {
                WriteInputError(json, "TRM2000", Usage);

                return 2;
            }

            if (outputPath is not null
                && (!string.Equals(Path.GetExtension(outputPath), ".exe", StringComparison.OrdinalIgnoreCase)
                    || paths.Any(path => path != "-" && string.Equals(outputPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))))
            {
                WriteInputError(json, "TRM2002", "Output must be a distinct .exe path, not any input source.");

                return 2;
            }

            var trees = ImmutableArray.CreateBuilder<SyntaxTree>(paths.Count);
            foreach (var path in paths)
            {
                trees.Add(SyntaxTree.Parse(ReadSource(path), path == "-" ? "<stdin>" : path));
            }

            snapshots = trees.MoveToImmutable();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            WriteInputError(json, "TRM2001", exception.Message);

            return 2;
        }

        return CompileTrees(args[0], snapshots, outputPath, json);
    }

    private static bool TryReadCompilationOptions(string[] args, out List<string> paths, out string? outputPath)
    {
        paths = [];
        outputPath = null;
        var count = args.Length - (args.Length != 0 && args[^1] == "--json" ? 1 : 0);
        if (count < 2 || args[1] == "--json" || (count & 1) != 0)
        {
            return false;
        }

        paths.Add(args[1]);
        var knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (args[1] != "-")
        {
            _ = knownPaths.Add(Path.GetFullPath(args[1]));
        }
        var hasRuntime = false;
        for (var index = 2; index < count; index += 2)
        {
            var value = args[index + 1];
            switch (args[index])
            {
                case "--source" when value != "-" && paths.Count < 256 && knownPaths.Add(Path.GetFullPath(value)):
                    paths.Add(value);
                    break;
                case "-o" when args[0] == "build" && outputPath is null:
                    outputPath = Path.GetFullPath(value);
                    break;
                case "--runtime" when args[0] is "build" or "jit" && !hasRuntime && value == "fail-fast":
                    hasRuntime = true;
                    break;
                default:
                    return false;
            }
        }

        return args[0] == "analyze" || (hasRuntime && (args[0] != "build" || outputPath is not null));
    }

    private static int CompileTree(string command, SyntaxTree tree, string? outputPath, bool json) =>
        CompileTrees(command, [tree], outputPath, json);

    private static int CompileTrees(string command, ImmutableArray<SyntaxTree> trees, string? outputPath, bool json)
    {
        var build = outputPath is not null;
        var compilation = trees.Length == 1 ? Compilation.Create(trees[0]) : Compilation.Create(trees);
        var diagnostics = compilation.SourceDiagnostics;
        byte[]? image = null;

        if (!compilation.HasErrors && (build || command == "jit"))
        {
            if (compilation.Program is not BoundProgram program)
            {
                throw new InvalidOperationException("A successful compilation must contain a bound program.");
            }

            try
            {
                if (command == "jit")
                {
                    // This worker returns Main's raw exit status, not a JSON success envelope.
                    // Its caller must enforce a deadline; native fail-fast can terminate it.
                    return NativeJit.Execute(program, NativeRuntimePolicy.FailFast);
                }

                image = NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast);
            }
            catch (NotSupportedException exception)
            {
                diagnostics = diagnostics.Add(new SourceDiagnostic(trees[0],
                    new Diagnostic("TRM4001", DiagnosticSeverity.Error, exception.Message, trees[0].Root.Span)));
            }
        }

        var success = !diagnostics.Any(static diagnostic => diagnostic.Diagnostic.Severity == DiagnosticSeverity.Error);

        if (success && image is not null && outputPath is not null)
        {
            try
            {
                WriteArtifact(outputPath, image);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                WriteInputError(json, "TRM2002", exception.Message);

                return 2;
            }
        }

        WriteCompilationResult(command, compilation, diagnostics, success, outputPath, image, json);

        return success ? 0 : 1;
    }

    private static void WriteCompilationResult(string command, Compilation compilation,
        ImmutableArray<SourceDiagnostic> diagnostics, bool success, string? outputPath, byte[]? image, bool json)
    {
        if (json)
        {
            if (compilation.SyntaxTrees.Length == 1)
            {
                WriteCompilationJson(command, compilation.SyntaxTree, [.. diagnostics.Select(static item => item.Diagnostic)],
                    success, outputPath, image);
            }
            else
            {
                WriteCompilationJson(command, compilation, diagnostics, success, outputPath, image);
            }
        }
        else
        {
            foreach (var located in diagnostics)
            {
                var tree = located.SyntaxTree;
                var diagnostic = located.Diagnostic;
                var position = tree.Source.GetLinePosition(diagnostic.Span.Start);
                Console.Error.WriteLine(FormattableString.Invariant($"{tree.Source.Path}({position.Line + 1},{position.Character + 1}): {GetSeverityText(diagnostic.Severity)} {diagnostic.Code}: {diagnostic.Message}"));
            }

            if (success)
            {
                var sourceDescription = compilation.SyntaxTrees.Length == 1
                    ? compilation.SyntaxTree.Source.Path
                    : FormattableString.Invariant($"{compilation.SyntaxTrees.Length} source files");
                Console.Out.WriteLine(outputPath is not null
                    ? $"{outputPath}: Windows x64 {Compilation.ProfileVersion} (fail-fast runtime)"
                    : $"{sourceDescription}: {Compilation.ProfileVersion} semantic analysis OK");
            }
        }
    }

    private static void WriteArtifact(string outputPath, byte[] image)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? throw new ArgumentException("Output requires a parent directory.", nameof(outputPath));
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        var ownsTemporary = false;

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                stream.Write(image);
                stream.Flush(flushToDisk: true);
            }

            // Only replace the destination after the complete image has been written.
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (ownsTemporary)
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void WriteCompilationJson(string command, SyntaxTree tree, ImmutableArray<Diagnostic> diagnostics,
        bool success, string? outputPath, byte[]? image)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("languageVersion", SyntaxTree.LanguageVersion);
        writer.WriteString("profile", Compilation.ProfileVersion);
        writer.WriteString("command", command);
        writer.WriteString("path", tree.Source.Path);
        writer.WriteString("coordinateSystem", "utf-16");
        writer.WriteNumber("sourceLength", tree.Source.Length);
        writer.WriteString("contentId", tree.ContentId);
        writer.WriteBoolean("success", success);
        WriteDiagnostics(writer, tree.Source, diagnostics);

        if (success && image is not null)
        {
            WriteNativeArtifactJson(writer, outputPath, image);
        }

        writer.WriteEndObject();
    }

    private static void WriteCompilationJson(string command, Compilation compilation,
        ImmutableArray<SourceDiagnostic> diagnostics, bool success, string? outputPath, byte[]? image)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 2);
        writer.WriteString("languageVersion", SyntaxTree.LanguageVersion);
        writer.WriteString("profile", Compilation.ProfileVersion);
        writer.WriteString("command", command);
        writer.WriteString("coordinateSystem", "utf-16");
        writer.WriteBoolean("success", success);
        writer.WriteStartArray("sources");
        foreach (var tree in compilation.SyntaxTrees)
        {
            writer.WriteStartObject();
            writer.WriteString("path", tree.Source.Path);
            writer.WriteString("contentId", tree.ContentId);
            writer.WriteNumber("sourceLength", tree.Source.Length);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("diagnostics");
        foreach (var located in diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteNumber("sourceIndex", compilation.SyntaxTrees.IndexOf(located.SyntaxTree));
            WriteDiagnosticProperties(writer, located.SyntaxTree.Source, located.Diagnostic);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        if (success && image is not null)
        {
            WriteNativeArtifactJson(writer, outputPath, image);
        }

        writer.WriteEndObject();
    }

    private static void WriteNativeArtifactJson(Utf8JsonWriter writer, string? outputPath, byte[] image)
    {
        writer.WriteStartObject("artifact");
        writer.WriteString("path", outputPath);
        writer.WriteString("target", "windows-x64");
        writer.WriteString("runtimePolicy", "fail-fast");
        writer.WriteNumber("length", image.Length);
        writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(image)));
        writer.WriteEndObject();
    }
}
