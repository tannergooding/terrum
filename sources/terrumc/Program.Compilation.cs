// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
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
        var build = args[0] == "build";
        var validArguments = build
            ? args.Length is 6 or 7 && args[2] == "-o" && args[4] == "--runtime" && args[5] == "fail-fast"
                && (args.Length == 6 || args[6] == "--json")
            : args.Length is 2 or 3 && (args.Length == 2 || args[2] == "--json");

        if (!validArguments || args[1] == "--json")
        {
            WriteInputError(json, "TRM2000", Usage);

            return 2;
        }

        string text;
        string? outputPath = null;

        try
        {
            if (build)
            {
                outputPath = Path.GetFullPath(args[3]);

                if (!string.Equals(Path.GetExtension(outputPath), ".exe", StringComparison.OrdinalIgnoreCase)
                    || (args[1] != "-" && string.Equals(outputPath, Path.GetFullPath(args[1]), StringComparison.OrdinalIgnoreCase)))
                {
                    WriteInputError(json, "TRM2002", "Output must be a distinct .exe path, not the input source.");

                    return 2;
                }
            }

            text = ReadSource(args[1]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            WriteInputError(json, "TRM2001", exception.Message);

            return 2;
        }

        var tree = SyntaxTree.Parse(text, args[1] == "-" ? "<stdin>" : args[1]);

        return CompileTree(args[0], tree, outputPath, json);
    }

    private static int CompileTree(string command, SyntaxTree tree, string? outputPath, bool json)
    {
        var build = outputPath is not null;
        var compilation = Compilation.Create(tree);
        var diagnostics = compilation.Diagnostics;
        byte[]? image = null;

        if (!compilation.HasErrors && build)
        {
            if (compilation.Program is not BoundProgram program)
            {
                throw new InvalidOperationException("A successful compilation must contain a bound program.");
            }

            try
            {
                image = NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast);
            }
            catch (NotSupportedException exception)
            {
                diagnostics = diagnostics.Add(new Diagnostic("TRM4001", DiagnosticSeverity.Error, exception.Message, tree.Root.Span));
            }
        }

        var success = !diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

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

        if (json)
        {
            WriteCompilationJson(command, tree, diagnostics, success, outputPath, image);
        }
        else
        {
            foreach (var diagnostic in diagnostics)
            {
                var position = tree.Source.GetLinePosition(diagnostic.Span.Start);
                Console.Error.WriteLine(FormattableString.Invariant($"{tree.Source.Path}({position.Line + 1},{position.Character + 1}): {GetSeverityText(diagnostic.Severity)} {diagnostic.Code}: {diagnostic.Message}"));
            }

            if (success)
            {
                Console.Out.WriteLine(build
                    ? $"{outputPath}: Windows x64 {Compilation.ProfileVersion} (fail-fast runtime)"
                    : $"{tree.Source.Path}: {Compilation.ProfileVersion} semantic analysis OK");
            }
        }

        return success ? 0 : 1;
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
            writer.WriteStartObject("artifact");
            writer.WriteString("path", outputPath);
            writer.WriteString("target", "windows-x64");
            writer.WriteString("runtimePolicy", "fail-fast");
            writer.WriteNumber("length", image.Length);
            writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(image)));
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }
}
