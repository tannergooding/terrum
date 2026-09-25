// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
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
        if (args.Length is not (5 or 6) || args[1] == "-" || args[3] != "--runtime" || args[4] != "fail-fast"
            || (args.Length == 6 && args[5] != "--json"))
        {
            WriteInputError(json, "TRM2000", Usage);
            return 2;
        }

        string scanner;
        SourceText source;
        try
        {
            scanner = ReadSource(args[1]);
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

        var tree = SyntaxTree.Parse(scanner + "\nint32 Main() { return Scan(); }\n", args[1]);
        var compilation = Compilation.Create(tree, ScannerHost.CreateBuffers(source));
        if (compilation.HasErrors)
        {
            if (json)
            {
                WriteCompilationJson("scan-component", tree, compilation.Diagnostics, false, null, null);
            }
            else
            {
                foreach (var diagnostic in compilation.Diagnostics)
                {
                    Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
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
            result = NativeJit.ExecuteWithData(program, NativeRuntimePolicy.FailFast);
        }
        catch (NotSupportedException exception)
        {
            WriteInputError(json, "TRM4001", exception.Message);
            return 1;
        }

        ImmutableArray<ScannerToken> tokens;
        ImmutableArray<ScannerDiagnostic> diagnostics;
        try
        {
            var words = result.Data.AsSpan(result.BufferOffsets[3], program.Buffers[3].Values.Length * 4);
            tokens = ScannerHost.ReadTokens(source, result.ExitCode, words);
            var diagnosticWords = result.Data.AsSpan(result.BufferOffsets[4], program.Buffers[4].Values.Length * 4);
            diagnostics = ScannerHost.ReadDiagnostics(source, diagnosticWords);
        }
        catch (ArgumentException exception)
        {
            WriteInputError(json, "TRM5001", exception.Message);
            return 1;
        }
        if (json)
        {
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 2);
            writer.WriteString("component", "scanner-2");
            writer.WriteBoolean("success", diagnostics.IsEmpty);
            writer.WriteString("unicodeVersion", UnicodeIdentifierProfile.Version);
            writer.WriteString("coordinateSystem", "utf-16");
            writer.WriteNumber("sourceLength", source.Length);
            writer.WriteStartArray("tokens");
            foreach (var token in tokens)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", token.Kind.ToString());
                writer.WriteNumber("start", token.Span.Start);
                writer.WriteNumber("length", token.Span.Length);
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
                Console.WriteLine(FormattableString.Invariant($"{token.Kind} [{token.Span.Start}..{token.Span.End})"));
            }
            foreach (var diagnostic in diagnostics)
            {
                Console.Error.WriteLine(FormattableString.Invariant(
                    $"{diagnostic.Code} [{diagnostic.Span.Start}..{diagnostic.Span.End})"));
            }
        }

        return diagnostics.IsEmpty ? 0 : 1;
    }
}
