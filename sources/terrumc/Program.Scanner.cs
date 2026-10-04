// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
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
        var count = build ? 7 : 5;
        var runtime = build ? 5 : 3;
        if ((args.Length != count && args.Length != count + 1) || args[1] == "-"
            || (build && args[3] != "-o") || args[runtime] != "--runtime"
            || args[runtime + 1] is not ("fail-fast" or "checked-exceptions")
            || (args.Length == count + 1 && args[count] != "--json"))
        {
            WriteInputError(json, "TRM2000", Usage);
            return 2;
        }

        ImmutableArray<SyntaxTree> scannerTrees;
        string scannerPath;
        SourceText source;
        string? outputPath = null;
        try
        {
            scannerPath = Path.GetFullPath(args[1]);
            if (build)
            {
                outputPath = Path.GetFullPath(args[4]);
                if (!string.Equals(Path.GetExtension(outputPath), ".exe", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(outputPath, scannerPath, StringComparison.OrdinalIgnoreCase)
                    || (args[2] != "-" && string.Equals(outputPath, Path.GetFullPath(args[2]), StringComparison.OrdinalIgnoreCase)))
                {
                    WriteInputError(json, "TRM2002", "Output must be a distinct .exe path, not scanner or input source.");
                    return 2;
                }
            }
            scannerTrees = ReadScannerTrees(args[1]);
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

        var policy = args[runtime + 1] == "fail-fast"
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var compilation = Compilation.Create(scannerTrees, ScannerHost.CreateBuffers(source), (BoundRuntimePolicy)policy);
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
                runtimePolicy: policy);
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

    private static ImmutableArray<SyntaxTree> ReadScannerTrees(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            return [SyntaxTree.Parse(ReadSource(fullPath) + "\nint32 Main() { return Scan(); }\n", path)];
        }

        string[] componentNames = ["SourceRange.terrum", "Utf16Scalar.terrum", "CharacterLiteral.terrum",
            "StringEscape.terrum", "Scanner.terrum"];
        var trees = ImmutableArray.CreateBuilder<SyntaxTree>(componentNames.Length);
        const string NamespaceDeclaration = "namespace Terrum.CodeAnalysis;";
        foreach (var componentName in componentNames)
        {
            var componentPath = Path.Combine(fullPath, componentName);
            var componentSource = ReadSource(componentPath);
            if (componentName != "SourceRange.terrum"
                && !componentSource.StartsWith(NamespaceDeclaration, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Scanner bundle components must use the Terrum.CodeAnalysis namespace.", nameof(path));
            }
            if (componentName == "Scanner.terrum")
            {
                componentSource += "\nint32 Main() { return Scan(); }\n";
            }
            trees.Add(SyntaxTree.Parse(componentSource, componentPath));
        }

        return trees.MoveToImmutable();
    }
}
