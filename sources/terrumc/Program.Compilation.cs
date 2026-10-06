// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
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
        List<string> exports;
        string? outputPath;
        string? abiOutputPath;
        NativeRuntimePolicy runtimePolicy;
        bool coreLibrary;
        bool sourceGrant;
        try
        {
            if (!TryReadCompilationOptions(args, out var paths, out exports, out outputPath,
                out runtimePolicy, out coreLibrary, out sourceGrant, out abiOutputPath))
            {
                WriteInputError(json, "TRM2000", Usage);

                return 2;
            }

            var extension = args[0] == "build-library" ? ".dll" : ".exe";
            if (outputPath is not null
                && (!string.Equals(Path.GetExtension(outputPath), extension, StringComparison.OrdinalIgnoreCase)
                    || paths.Any(path => path != "-" && string.Equals(outputPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))))
            {
                WriteInputError(json, "TRM2002", $"Output must be a distinct {extension} path, not any input source.");

                return 2;
            }
            if (abiOutputPath is not null
                && (string.Equals(abiOutputPath, outputPath, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetExtension(abiOutputPath), ".json", StringComparison.OrdinalIgnoreCase)
                    || paths.Any(path => path != "-"
                        && string.Equals(abiOutputPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))))
            {
                WriteInputError(json, "TRM2002", "The ABI manifest must be a distinct JSON path, not an input source or the library output.");

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

        return CompileTrees(args[0], snapshots, outputPath, json, [.. exports],
            runtimePolicy, coreLibrary, sourceGrant, abiOutputPath);
    }

    private static bool TryReadCompilationOptions(string[] args, out List<string> paths, out List<string> exports,
        out string? outputPath, out NativeRuntimePolicy runtimePolicy, out bool coreLibrary,
        out bool sourceGrant, out string? abiOutputPath)
    {
        paths = [];
        exports = [];
        outputPath = null;
        abiOutputPath = null;
        runtimePolicy = NativeRuntimePolicy.FailFast;
        coreLibrary = false;
        sourceGrant = false;
        var count = args.Length - (args.Length != 0 && args[^1] == "--json" ? 1 : 0);
        if (count < 2 || args[1] == "--json")
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
        var exportNames = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 2; index < count;)
        {
            if (args[index] == "--corelib")
            {
                if (coreLibrary || args[0] is not ("analyze" or "build-library"))
                {
                    return false;
                }
                coreLibrary = true;
                index++;
                continue;
            }
            if (args[index] == "--source-grant" && args[0] == "build-library" && !sourceGrant)
            {
                sourceGrant = true;
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
                case "--source" when value != "-" && paths.Count < 256 && knownPaths.Add(Path.GetFullPath(value)):
                    paths.Add(value);
                    break;
                case "--export" when args[0] == "build-library" && exports.Count < 1024 && exportNames.Add(value):
                    exports.Add(value);
                    break;
                case "--abi-output" when args[0] == "build-library" && abiOutputPath is null:
                    abiOutputPath = Path.GetFullPath(value);
                    break;
                case "-o" when args[0] is "build" or "build-library" && outputPath is null:
                    outputPath = Path.GetFullPath(value);
                    break;
                case "--runtime" when args[0] is "analyze" or "build" or "build-library" or "jit"
                    && !hasRuntime && value is "fail-fast" or "checked-exceptions":
                    hasRuntime = true;
                    runtimePolicy = value == "fail-fast"
                        ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
                    break;
                default:
                    return false;
            }
            index += 2;
        }

        return (sourceGrant == (abiOutputPath is not null))
            && (!sourceGrant || args[0] == "build-library" && !coreLibrary)
            && (args[0] == "analyze"
            || (hasRuntime && (args[0] == "jit" || outputPath is not null)
                && (args[0] != "build-library" || exports.Count != 0)));
    }

    private static int CompileTrees(string command, ImmutableArray<SyntaxTree> trees, string? outputPath, bool json,
        ImmutableArray<string> exports = default, NativeRuntimePolicy runtimePolicy = NativeRuntimePolicy.FailFast,
        bool coreLibrary = false, bool sourceGrant = false, string? abiOutputPath = null)
    {
        var build = outputPath is not null;
        var library = command == "build-library" || coreLibrary;
        var buffers = sourceGrant
            ? ImmutableArray.Create(new HostBuffer("Source", PrimitiveType.Char16, [], false)
                { IsInvocationScoped = true })
            : ImmutableArray<HostBuffer>.Empty;
        var compilation = coreLibrary
            ? Compilation.CreateCoreLibrary(trees, (BoundRuntimePolicy)runtimePolicy)
            : library
            ? sourceGrant
                ? Compilation.CreateLibrary(trees, buffers, (BoundRuntimePolicy)runtimePolicy)
                : trees.Length == 1 ? Compilation.CreateLibrary(trees[0], (BoundRuntimePolicy)runtimePolicy)
                    : Compilation.CreateLibrary(trees, (BoundRuntimePolicy)runtimePolicy)
            : trees.Length == 1 ? Compilation.Create(trees[0], (BoundRuntimePolicy)runtimePolicy)
                : Compilation.Create(trees, (BoundRuntimePolicy)runtimePolicy);
        var diagnostics = compilation.SourceDiagnostics;
        byte[]? image = null;
        byte[]? abiManifest = null;

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
                    return NativeJit.Execute(program, runtimePolicy, GraphJitRegistrarAssemblyPath());
                }

                if (library && sourceGrant)
                {
                    var artifact = NativeEmitter.EmitLibraryWithSourceGrant(program, exports, runtimePolicy);
                    image = artifact.Image;
                    abiManifest = CreateSourceGrantManifest(artifact, runtimePolicy);
                }
                else
                {
                    image = library ? NativeEmitter.EmitLibrary(program, exports, runtimePolicy)
                        : NativeEmitter.Emit(program, runtimePolicy);
                }
            }
            catch (NotSupportedException exception)
            {
                diagnostics = diagnostics.Add(new SourceDiagnostic(trees[0],
                    new Diagnostic("TRM4001", DiagnosticSeverity.Error, exception.Message, trees[0].Root.Span)));
            }
            catch (Exception exception) when (command == "jit" && exception is IOException
                or Win32Exception or InvalidOperationException or TimeoutException)
            {
                diagnostics = diagnostics.Add(new SourceDiagnostic(trees[0],
                    new Diagnostic("TRM4001", DiagnosticSeverity.Error, exception.Message, trees[0].Root.Span)));
            }
            catch (ArgumentException exception) when (library)
            {
                diagnostics = diagnostics.Add(new SourceDiagnostic(trees[0],
                    new Diagnostic("TRM4002", DiagnosticSeverity.Error, exception.Message, trees[0].Root.Span)));
            }
        }

        var success = !diagnostics.Any(static diagnostic => diagnostic.Diagnostic.Severity == DiagnosticSeverity.Error);

        if (success && image is not null && outputPath is not null)
        {
            try
            {
                if (abiManifest is not null)
                {
                    WriteArtifactPair(outputPath, image,
                        abiOutputPath ?? throw new InvalidOperationException("A source-grant manifest path is required."),
                        abiManifest);
                }
                else
                {
                    WriteArtifact(outputPath, image);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                WriteInputError(json, "TRM2002", exception.Message);

                return 2;
            }
        }

        WriteCompilationResult(command, compilation, diagnostics, success, outputPath, image, json, runtimePolicy);

        return success ? 0 : 1;
    }

    private static byte[] CreateSourceGrantManifest(NativeSourceGrantLibrary artifact,
        NativeRuntimePolicy runtimePolicy)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteNumber("sourceGrantAbiVersion", 1);
            writer.WriteNumber("maximumSourceCodeUnits", 16_384);
            writer.WriteString("runtimePolicy", PolicyName(runtimePolicy));
            writer.WriteString("imageSha256", Convert.ToHexString(SHA256.HashData(artifact.Image)));
            writer.WriteStartArray("exports");
            foreach (var export in artifact.Exports)
            {
                writer.WriteStartObject();
                writer.WriteString("name", export.Name);
                writer.WriteBoolean("sourceDependent", export.SourceDependent);
                writer.WriteString("returnType", export.ReturnType.ToString());
                writer.WriteStartArray("parameterTypes");
                foreach (var parameterType in export.ParameterTypes)
                {
                    writer.WriteStringValue(parameterType.ToString());
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteCompilationResult(string command, Compilation compilation,
        ImmutableArray<SourceDiagnostic> diagnostics, bool success, string? outputPath, byte[]? image, bool json,
        NativeRuntimePolicy runtimePolicy = NativeRuntimePolicy.FailFast)
    {
        var profile = compilation.NativeProfileVersion ?? Profile(runtimePolicy);
        if (json)
        {
            if (compilation.SyntaxTrees.Length == 1)
            {
                WriteCompilationJson(command, compilation.SyntaxTree, [.. diagnostics.Select(static item => item.Diagnostic)],
                    success, outputPath, image, runtimePolicy, profile);
            }
            else
            {
                WriteCompilationJson(command, compilation, diagnostics, success, outputPath, image, runtimePolicy, profile);
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
                    ? $"{outputPath}: Windows x64 {profile} ({PolicyName(runtimePolicy)} runtime)"
                    : $"{sourceDescription}: {profile} semantic analysis OK");
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

    private static void WriteArtifactPair(string imagePath, byte[] image, string manifestPath, byte[] manifest)
    {
        string? imageTemporary = null;
        string? manifestTemporary = null;
        var imageBackup = SiblingPath(imagePath, ".bak");
        var manifestBackup = SiblingPath(manifestPath, ".bak");
        var preserveManifestBackup = false;

        try
        {
            imageTemporary = StageArtifact(imagePath, image);
            manifestTemporary = StageArtifact(manifestPath, manifest);
            var imageExisted = File.Exists(imagePath);
            var manifestExisted = File.Exists(manifestPath);

            // Publish the manifest first so a blocked manifest destination cannot replace the image alone.
            PublishStagedArtifact(manifestTemporary, manifestPath, manifestBackup, manifestExisted);
            try
            {
                PublishStagedArtifact(imageTemporary, imagePath, imageBackup, imageExisted);
            }
            catch (Exception publishException)
            {
                try
                {
                    if (manifestExisted)
                    {
                        File.Replace(manifestBackup, manifestPath, null);
                    }
                    else
                    {
                        File.Delete(manifestPath);
                    }
                }
                catch (Exception rollbackException)
                {
                    preserveManifestBackup = true;
                    throw new IOException(
                        $"Failed to publish the artifact pair and restore the manifest backup at '{manifestBackup}'.",
                        new AggregateException(publishException, rollbackException));
                }

                throw;
            }
        }
        finally
        {
            DeleteIfPresent(imageTemporary);
            DeleteIfPresent(manifestTemporary);
            if (!preserveManifestBackup)
            {
                DeleteIfPresent(imageBackup);
                DeleteIfPresent(manifestBackup);
            }
        }
    }

    private static string StageArtifact(string outputPath, byte[] image)
    {
        var directory = Path.GetDirectoryName(outputPath)
            ?? throw new ArgumentException("Output requires a parent directory.", nameof(outputPath));
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        var completed = false;

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(image);
                stream.Flush(flushToDisk: true);
            }
            completed = true;
            return temporaryPath;
        }
        finally
        {
            if (!completed)
            {
                DeleteIfPresent(temporaryPath);
            }
        }
    }

    private static void PublishStagedArtifact(string temporaryPath, string outputPath, string backupPath,
        bool outputExisted)
    {
        if (outputExisted)
        {
            File.Replace(temporaryPath, outputPath, backupPath);
        }
        else
        {
            File.Move(temporaryPath, outputPath);
        }
    }

    private static string SiblingPath(string path, string extension)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("Output requires a parent directory.", nameof(path));
        return Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}{extension}");
    }

    private static void DeleteIfPresent(string? path)
    {
        if (path is not null && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void WriteCompilationJson(string command, SyntaxTree tree, ImmutableArray<Diagnostic> diagnostics,
        bool success, string? outputPath, byte[]? image,
        NativeRuntimePolicy runtimePolicy, string profile)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("languageVersion", SyntaxTree.LanguageVersion);
        writer.WriteString("profile", profile);
        writer.WriteString("runtimePolicy", PolicyName(runtimePolicy));
        writer.WriteString("command", command);
        writer.WriteString("path", tree.Source.Path);
        writer.WriteString("coordinateSystem", "utf-16");
        writer.WriteNumber("sourceLength", tree.Source.Length);
        writer.WriteString("contentId", tree.ContentId);
        writer.WriteBoolean("success", success);
        WriteDiagnostics(writer, tree.Source, diagnostics);

        if (success && image is not null)
        {
            WriteNativeArtifactJson(writer, outputPath, image, command == "build-library", runtimePolicy);
        }

        writer.WriteEndObject();
    }

    private static void WriteCompilationJson(string command, Compilation compilation,
        ImmutableArray<SourceDiagnostic> diagnostics, bool success, string? outputPath, byte[]? image,
        NativeRuntimePolicy runtimePolicy, string profile)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 2);
        writer.WriteString("languageVersion", SyntaxTree.LanguageVersion);
        writer.WriteString("profile", profile);
        writer.WriteString("runtimePolicy", PolicyName(runtimePolicy));
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
        Dictionary<SyntaxTree, int>? sourceIndices = null;
        if (diagnostics.Length > 1)
        {
            sourceIndices = new Dictionary<SyntaxTree, int>(compilation.SyntaxTrees.Length);
            for (var index = 0; index < compilation.SyntaxTrees.Length; index++)
            {
                _ = sourceIndices.TryAdd(compilation.SyntaxTrees[index], index);
            }
        }
        foreach (var located in diagnostics)
        {
            writer.WriteStartObject();
            var sourceIndex = sourceIndices is null
                ? compilation.SyntaxTrees.IndexOf(located.SyntaxTree)
                : sourceIndices[located.SyntaxTree];
            writer.WriteNumber("sourceIndex", sourceIndex);
            WriteDiagnosticProperties(writer, located.SyntaxTree.Source, located.Diagnostic);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        if (success && image is not null)
        {
            WriteNativeArtifactJson(writer, outputPath, image, command == "build-library", runtimePolicy);
        }

        writer.WriteEndObject();
    }

    private static void WriteNativeArtifactJson(Utf8JsonWriter writer, string? outputPath, byte[] image, bool library,
        NativeRuntimePolicy runtimePolicy)
    {
        writer.WriteStartObject("artifact");
        writer.WriteString("path", outputPath);
        writer.WriteString("target", "windows-x64");
        if (library)
        {
            writer.WriteString("kind", "dynamic-library");
        }
        writer.WriteString("runtimePolicy", PolicyName(runtimePolicy));
        writer.WriteNumber("length", image.Length);
        writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(image)));
        writer.WriteEndObject();
    }

    private static string Profile(NativeRuntimePolicy policy) =>
        Compilation.GetNativeProfileVersion((BoundRuntimePolicy)policy, uniqueValues: false);

    private static string PolicyName(NativeRuntimePolicy policy) =>
        policy == NativeRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions";
}
