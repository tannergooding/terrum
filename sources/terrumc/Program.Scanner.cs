// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.Tools;

internal static partial class Program
{
    private static int RunScannerComponent(string[] args, bool json)
    {
        var build = args[0] == "build-component";
        var trailingJson = args.Length != 0 && args[^1] == "--json";
        var count = args.Length - (trailingJson ? 1 : 0);
        if (count < 3 || args[1] == "-" || (json && !trailingJson))
        {
            WriteInputError(json, "TRM2000", Usage);
            return 2;
        }

        string? outputArgument = null;
        NativeRuntimePolicy? selectedPolicy = null;
        var sourceArithmeticProfile = SourceArithmeticProfile.Development;
        var hasArithmeticProfile = false;
        string? layoutCoreManifestPath = null;
        for (var index = 3; index < count;)
        {
            if (index + 1 >= count)
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            var value = args[index + 1];
            switch (args[index])
            {
                case "-o" when build && outputArgument is null:
                    outputArgument = value;
                    break;
                case "--runtime" when selectedPolicy is null
                    && value is "fail-fast" or "checked-exceptions":
                    selectedPolicy = value == "fail-fast"
                        ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
                    break;
                case "--arithmetic-profile" when build && !hasArithmeticProfile
                    && TryParseSourceArithmeticProfile(value, out sourceArithmeticProfile):
                    hasArithmeticProfile = true;
                    break;
                case "--layout-core-manifest" when layoutCoreManifestPath is null
                    && value.Length != 0 && value != "-"
                    && !value.StartsWith('-', StringComparison.Ordinal):
                    if (!TryGetFullPath(value, out layoutCoreManifestPath))
                    {
                        WriteInputError(json, "TRM2000", Usage);
                        return 2;
                    }
                    break;
                default:
                    WriteInputError(json, "TRM2000", Usage);
                    return 2;
            }
            index += 2;
        }
        if (selectedPolicy is not NativeRuntimePolicy policy || (build && outputArgument is null))
        {
            WriteInputError(json, "TRM2000", Usage);
            return 2;
        }

        ImmutableArray<SyntaxTree> scannerTrees;
        ScannerSourceSelection scannerSelection;
        SourceText source;
        string? outputPath = null;
        LayoutCoreRegistrationSet? layoutCoreRegistration = null;
        try
        {
            scannerSelection = SelectScannerSourceFiles(args[1]);
            var subjectPath = args[2] == "-"
                ? null : Path.GetFullPath(args[2]);
            if (build)
            {
                outputPath = Path.GetFullPath(outputArgument!);
                if (!string.Equals(Path.GetExtension(outputPath), ".exe",
                        StringComparison.OrdinalIgnoreCase)
                    || Directory.Exists(outputPath))
                {
                    WriteInputError(json, "TRM2002",
                        "Output must be a distinct .exe file, not scanner or input source.");
                    return 2;
                }
            }

            try
            {
                if (layoutCoreManifestPath is not null
                    && (scannerSelection.Files.Any(path =>
                            PathsAlias(layoutCoreManifestPath, path))
                        || (subjectPath is not null
                            && PathsAlias(layoutCoreManifestPath, subjectPath))
                        || (build && PathsAlias(layoutCoreManifestPath,
                            outputPath!))))
                {
                    WriteInputError(json, "TRM2002",
                        "The layout Core manifest must be distinct from every scanner source, subject input and output.");
                    return 2;
                }
                if (build && (scannerSelection.Files.Any(path =>
                        PathsAlias(outputPath!, path))
                    || (subjectPath is not null
                        && PathsAlias(outputPath!, subjectPath))))
                {
                    WriteInputError(json, "TRM2002",
                        "Output must be distinct from every scanner and subject source.");
                    return 2;
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException or ArgumentException
                or NotSupportedException)
            {
                WriteInputError(json, "TRM2002",
                    $"Could not validate scanner component path identity: {exception.Message}");
                return 2;
            }

            if (layoutCoreManifestPath is not null)
            {
                try
                {
                    layoutCoreRegistration
                        = LayoutCoreManifestReader.Read(
                            layoutCoreManifestPath).Registration;
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException
                    or DecoderFallbackException or ArgumentException
                    or NotSupportedException or FormatException)
                {
                    WriteInputError(json, "TRM2001", exception.Message);
                    return 2;
                }
            }

            scannerTrees = ReadScannerTrees(scannerSelection);
            source = SourceText.From(ReadSource(args[2]), args[2] == "-" ? "<stdin>" : args[2]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            WriteInputError(json, "TRM2001", exception.Message);
            return 2;
        }

        if (source.Length > ScannerHost.MaximumSourceLength)
        {
            WriteInputError(json, "TRM2001", "The scanner component input exceeds its bounded source limit.");
            return 2;
        }

        var compilation = Compilation.Create(scannerTrees,
            ScannerHost.CreateBuffers(source), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                LayoutCoreRegistration = layoutCoreRegistration,
            });
        if (compilation.HasErrors)
        {
            if (json)
            {
                if (scannerTrees.Length == 1)
                {
                    WriteCompilationJson(args[0], scannerTrees[0], compilation.Diagnostics, false, null, null,
                        policy, compilation.NativeProfileVersion ?? Profile(policy));
                }
                else
                {
                    WriteCompilationJson(args[0], compilation, compilation.SourceDiagnostics, false, null, null,
                        policy, compilation.NativeProfileVersion ?? Profile(policy));
                }
            }
            else
            {
                foreach (var located in compilation.SourceDiagnostics)
                {
                    var diagnostic = located.Diagnostic;
                    var tree = located.SyntaxTree;
                    var position = tree.Source.GetLinePosition(diagnostic.Span.Start);
                    Console.Error.WriteLine(FormattableString.Invariant(
                        $"{tree.Source.Path}({position.Line + 1},{position.Character + 1}): {GetSeverityText(diagnostic.Severity)} {diagnostic.Code}: {diagnostic.Message}"));
                }
            }
            return 1;
        }

        if (compilation.Program is not BoundProgram program)
        {
            throw new InvalidOperationException("A successful compilation must contain a bound program.");
        }

        NativeExecutionResult result;
        try
        {
            result = NativeJit.ExecuteWithData(program, policy, GraphJitRegistrarAssemblyPath());
        }
        catch (NotSupportedException exception)
        {
            WriteInputError(json, "TRM4001", exception.Message);
            return 1;
        }

        LexResult lexResult;
        ImmutableArray<ScannerIntegerValue> integers;
        SyntaxTree? parsedTree = null;
        try
        {
            var words = result.Data.AsSpan(result.BufferOffsets[3], program.Buffers[3].Values.Length * 4);
            var triviaWords = result.Data.AsSpan(result.BufferOffsets[5], program.Buffers[5].Values.Length * 4);
            var diagnosticWords = result.Data.AsSpan(result.BufferOffsets[4], program.Buffers[4].Values.Length * 4);
            lexResult = ScannerHost.ReadLexResult(source, result.ExitCode, words, triviaWords, diagnosticWords);
            var integerWords = result.Data.AsSpan(result.BufferOffsets[8], program.Buffers[8].Values.Length * 4);
            integers = ScannerHost.ReadIntegerValues(source, lexResult.Tokens, integerWords);
            if (build)
            {
                parsedTree = SyntaxTree.Parse(lexResult, integers);
            }
        }
        catch (ArgumentException exception)
        {
            WriteInputError(json, "TRM5001", exception.Message);
            return 1;
        }
        if (build)
        {
            if (parsedTree is not SyntaxTree scannedTree)
            {
                throw new InvalidOperationException("A successful component build must contain its parsed source.");
            }
            return CompileTrees(args[0], [scannedTree], outputPath, json,
                runtimePolicy: policy, sourceArithmeticProfile: sourceArithmeticProfile);
        }
        var tokens = lexResult.Tokens;
        var diagnostics = lexResult.Diagnostics;
        if (json)
        {
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", ScannerHost.SchemaVersion);
            writer.WriteString("component", ScannerHost.ComponentName);
            writer.WriteBoolean("success", diagnostics.IsEmpty);
            writer.WriteString("unicodeVersion", UnicodeIdentifierProfile.Version);
            writer.WriteString("coordinateSystem", "utf-16");
            writer.WriteNumber("sourceLength", source.Length);
            writer.WriteStartArray("integerValues");
            foreach (var integer in integers)
            {
                writer.WriteStartObject();
                writer.WriteNumber("tokenIndex", integer.TokenIndex);
                writer.WriteString("tag", integer.Tag);
                writer.WriteString("status", integer.IsOverflow ? "overflow" : "exact");
                if (integer.Magnitude is ulong magnitude)
                {
                    writer.WriteString("magnitude", magnitude.ToString(CultureInfo.InvariantCulture));
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("tokens");
            foreach (var token in tokens)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", token.Kind.ToString());
                writer.WriteNumber("start", token.Span.Start);
                writer.WriteNumber("length", token.Span.Length);
                writer.WriteStartArray("leadingTrivia");
                foreach (var item in token.LeadingTrivia)
                {
                    writer.WriteStartObject();
                    writer.WriteString("kind", item.Kind.ToString());
                    writer.WriteNumber("start", item.Span.Start);
                    writer.WriteNumber("length", item.Span.Length);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("diagnostics");
            foreach (var diagnostic in diagnostics)
            {
                writer.WriteStartObject();
                writer.WriteString("code", diagnostic.Code);
                writer.WriteNumber("start", diagnostic.Span.Start);
                writer.WriteNumber("length", diagnostic.Span.Length);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        else
        {
            foreach (var token in tokens)
            {
                foreach (var item in token.LeadingTrivia)
                {
                    Console.WriteLine(FormattableString.Invariant($"  {item.Kind} [{item.Span.Start}..{item.Span.End})"));
                }
                Console.WriteLine(FormattableString.Invariant($"{token.Kind} [{token.Span.Start}..{token.Span.End})"));
            }
            foreach (var integer in integers)
            {
                var magnitude = integer.Magnitude is ulong value
                    ? value.ToString(CultureInfo.InvariantCulture) : "overflow";
                Console.WriteLine(FormattableString.Invariant(
                    $"  integer token {integer.TokenIndex}: {magnitude} (tag: {integer.Tag})"));
            }
            foreach (var diagnostic in diagnostics)
            {
                Console.Error.WriteLine(FormattableString.Invariant(
                    $"{diagnostic.Code} [{diagnostic.Span.Start}..{diagnostic.Span.End})"));
            }
        }

        return diagnostics.IsEmpty ? 0 : 1;
    }

    private sealed record ScannerSourceSelection(
        string OriginalPath,
        bool IsDirectory,
        ImmutableArray<string> Files);

    private static ScannerSourceSelection SelectScannerSourceFiles(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            return new ScannerSourceSelection(path, false, [fullPath]);
        }

        string[] componentPaths = [
            Path.Combine("Text", "SourceRange.terrum"),
            Path.Combine("Lexing", "CharacterFacts.terrum"),
            Path.Combine("Text", "Utf16CodeUnits.terrum"),
            Path.Combine("Text", "Utf16Scalar.terrum"),
            Path.Combine("Lexing", "CharacterLiteral.terrum"),
            Path.Combine("Lexing", "StringEscape.terrum"),
            Path.Combine("Lexing", "Scanner.terrum"),
        ];
        var files = ImmutableArray.CreateBuilder<string>(componentPaths.Length);
        foreach (var componentPath in componentPaths)
        {
            files.Add(Path.GetFullPath(Path.Combine(fullPath, componentPath)));
        }

        return new ScannerSourceSelection(path, true, files.MoveToImmutable());
    }

    private static ImmutableArray<SyntaxTree> ReadScannerTrees(
        ScannerSourceSelection selection)
    {
        if (!selection.IsDirectory)
        {
            return [
                SyntaxTree.Parse(ReadSource(selection.OriginalPath)
                    + "\nint32 Main() { return Scan(); }\n",
                    selection.OriginalPath),
            ];
        }

        var trees = ImmutableArray.CreateBuilder<SyntaxTree>(
            selection.Files.Length);
        const string NamespaceDeclaration = "namespace Terrum.CodeAnalysis;";
        foreach (var fullComponentPath in selection.Files)
        {
            var componentName = Path.GetFileName(fullComponentPath);
            var componentSource = ReadSource(fullComponentPath);
            if (componentName != "SourceRange.terrum"
                && !componentSource.StartsWith(NamespaceDeclaration, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Scanner bundle components must use the Terrum.CodeAnalysis namespace.",
                    nameof(selection));
            }
            if (componentName == "Scanner.terrum")
            {
                componentSource += "\nint32 Main() { return Scan(); }\n";
            }
            trees.Add(SyntaxTree.Parse(componentSource, fullComponentPath));
        }

        return trees.MoveToImmutable();
    }
}
