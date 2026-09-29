// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.Tools;

internal static partial class Program
{
    private const string Usage = "Usage: terrumc <check|tokens|tree> <path|-> [--json]\n"
        + "       terrumc analyze <path|-> [--source <path>] [--runtime <fail-fast|checked-exceptions>] [--corelib] [--json]\n"
        + "       terrumc build <path|-> [--source <path>] -o <output.exe> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc build-library <path|-> [--source <path>] --export <name> [--export <name>] -o <output.dll> --runtime <fail-fast|checked-exceptions> [--corelib] [--json]\n"
        + "       terrumc jit <path|-> [--source <path>] --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc scan-component <scanner.terrum> <input|-> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc build-component <scanner.terrum> <input|-> -o <output.exe> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc frontend-component <self-host-directory> <input|-> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc frontend-build-library <self-host-directory> <input|-> -o <output.dll> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc frontend-build <self-host-directory> <input|-> -o <output.exe> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc frontend-run <self-host-directory> <input|-> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc validate-trrm-structure <artifact> [--json]\n"
        + "       terrumc validate-trrm-compact-linear <artifact> [--json]\n"
        + "       terrumc validate-trrm-compact-indexed <artifact> [--json]\n"
        + "       terrumc validate-trrm-compact <artifact> [--json]\n"
        + "       terrumc compile-module producer <source> <export> [--source <source>] [--export <export>] -o <output.trm> [--runtime <fail-fast|checked-exceptions>] [--json]\n"
        + "       terrumc compile-module consumer <source> <producer.trm> [--source <source>] -o <output.trm> [--runtime <fail-fast|checked-exceptions>] [--json]\n"
        + "       terrumc link-module <consumer.trm> <producer.trm> -o <output.exe> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc run-module <consumer.trm> <producer.trm> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc compile-native producer <source> <identity> --export <name> [--export <name>] [--source <source>] -o <producer.trmn> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc compile-native consumer <source> <producer.trmn> [--source <source>] -o <consumer.trmn> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc validate-native <consumer.trmn> <producer.trmn> [--json]\n"
        + "       terrumc run-native <consumer.trmn> <producer.trmn> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc compile-native-set <source> --provider <producer.trmn> --provider <producer.trmn> -o <consumer.tnset> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc validate-native-set <consumer.tnset> --provider <producer.trmn> --provider <producer.trmn> [--json]\n"
        + "       terrumc run-native-set <consumer.tnset> --provider <producer.trmn> --provider <producer.trmn> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc compile-native-graph producer <source> <identity> --provider <dependency.trmn|dependency.trng> --export <name> -o <producer.trng> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc compile-native-graph consumer <source> --provider <dependency.trmn|dependency.trng> -o <consumer.trng> --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc validate-native-graph <consumer.trng> --provider <selected.trmn|selected.trng> [--provider <selected>] [--json]\n"
        + "       terrumc resolve-native-graph <consumer.trng> --candidate <local.trmn|local.trng> [--candidate <local>] [--json]\n"
        + "       terrumc run-native-graph <consumer.trng> --provider <selected.trmn|selected.trng> [--provider <selected>] --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "       terrumc run-native-graph-jit <consumer.trng> --provider <selected.trmn|selected.trng> [--provider <selected>] --runtime <fail-fast|checked-exceptions> [--json]\n"
        + "check/tokens/tree inspect syntax; analyze binds the native profile; build emits Windows x64 PE.\n"
        + "fail-fast explicitly selects process termination for runtime failures, not catchable exceptions.\n"
        + "checked-exceptions enables recovery and unwinding for four compiler-generated failure reasons.\n"
        + "jit/run-module execute in this process and return the program's status; supervise them with a deadline.\n"
        + "scan-component/build-component/frontend-component/frontend-build-library/frontend-build/frontend-run execute native components in this process; supervise them with a deadline.\n"
        + "frontend-run returns Main's raw exit status without a success envelope; --json applies to diagnostics only.\n"
        + "validate-trrm-structure checks provisional (1, 0) framing and structural metadata only, not Body instructions or executable contracts.\n"
        + "validate-trrm-compact-linear additionally checks the bounded scalar compact-linear Body shape, not effects or execution.\n"
        + "validate-trrm-compact-indexed checks the bounded forward-branching scalar indexed Body shape, not effects or execution.\n"
        + "validate-trrm-compact checks both bounded compact forms per Body row, not general Bodies, effects or execution.\n"
        + "'-' reads strict UTF-8 from stdin. Exit: 0 success, 1 source/compile diagnostics, 2 usage/input/output error.";

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            Console.Out.WriteLine(Usage);

            return 0;
        }

        var json = args.Contains("--json", StringComparer.Ordinal);

        if (args.Length != 0 && args[0] is "scan-component" or "build-component")
        {
            return RunScannerComponent(args, json);
        }

        if (args.Length != 0 && args[0] is "frontend-component" or "frontend-build-library" or "frontend-build" or "frontend-run")
        {
            return RunFrontendComponent(args, json);
        }

        if (args.Length != 0 && args[0] is "validate-trrm-structure"
            or "validate-trrm-compact-linear" or "validate-trrm-compact-indexed"
            or "validate-trrm-compact")
        {
            return RunTrRmValidationCommand(args, json);
        }

        if (args.Length != 0 && args[0] is "compile-module" or "link-module" or "run-module")
        {
            return RunModuleCommand(args);
        }

        if (args.Length != 0 && args[0] is "compile-native" or "validate-native" or "run-native")
        {
            return RunNativeModuleCommand(args, json);
        }

        if (args.Length != 0 && args[0] is "compile-native-set" or "validate-native-set" or "run-native-set")
        {
            return RunNativeSetCommand(args, json);
        }

        if (args.Length != 0 && args[0] is "compile-native-graph" or "validate-native-graph" or "run-native-graph")
        {
            return RunNativeGraphCommand(args, json);
        }

        if (args.Length != 0 && args[0] == "resolve-native-graph")
        {
            return RunNativeGraphCatalogCommand(args, json);
        }

        if (args.Length != 0 && args[0] is "run-native-graph-jit" or "--native-graph-jit-worker")
        {
            return RunNativeGraphJitCommand(args, json);
        }

        if (args.Length == 1 && args[0] == "--native-jit-registrar")
        {
            return NativeJitContinuationRegistrar.Serve();
        }

        if (args.Length != 0 && args[0] is "analyze" or "build" or "build-library" or "jit")
        {
            return RunCompilation(args, json);
        }

        if (args.Length is not (2 or 3) || args[0] is not ("check" or "tokens" or "tree")
            || (args.Length == 3 && args[2] != "--json") || args[1] == "--json")
        {
            WriteInputError(json, "TRM2000", Usage);

            return 2;
        }

        string text;

        try
        {
            text = ReadSource(args[1]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            WriteInputError(json, "TRM2001", exception.Message);

            return 2;
        }

        var source = SourceText.From(text, args[1] == "-" ? "<stdin>" : args[1]);
        var tree = args[0] == "tokens" ? null : SyntaxTree.Parse(source);
        ImmutableArray<SyntaxToken> tokens;
        ImmutableArray<Diagnostic> diagnostics;

        if (tree is null)
        {
            var lex = Lexer.Lex(source);
            tokens = lex.Tokens;
            diagnostics = lex.Diagnostics;
        }
        else
        {
            tokens = tree.Tokens;
            diagnostics = tree.Diagnostics;
        }
        var hasErrors = diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        if (json)
        {
            WriteJson(args[0], source, tree, tokens, diagnostics, hasErrors);
        }
        else
        {
            if (args[0] == "tokens")
            {
                foreach (var token in tokens)
                {
                    Console.Out.WriteLine(FormattableString.Invariant($"{token.Kind} [{token.Span.Start}..{token.Span.End}) \"{JsonEncodedText.Encode(token.Text)}\""));
                }
            }
            else if (args[0] == "tree" && tree is not null)
            {
                WriteTree(tree.Root);
            }

            foreach (var diagnostic in diagnostics)
            {
                var position = source.GetLinePosition(diagnostic.Span.Start);
                Console.Error.WriteLine(FormattableString.Invariant($"{source.Path}({position.Line + 1},{position.Character + 1}): {GetSeverityText(diagnostic.Severity)} {diagnostic.Code}: {diagnostic.Message}"));
            }

            if (args[0] == "check" && !hasErrors)
            {
                Console.Out.WriteLine($"{source.Path}: syntax OK (no semantic analysis)");
            }
        }

        return hasErrors ? 1 : 0;
    }

    private static string ReadSource(string path)
    {
        using var input = path == "-" ? Console.OpenStandardInput() : File.OpenRead(path);

        return ReadUtf8(input);
    }

    private static string ReadUtf8(Stream input)
    {
        // Do not let StreamReader's BOM detection silently switch to UTF-16 or replace invalid bytes.
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var text = reader.ReadToEnd();

        return text.StartsWith('\uFEFF', StringComparison.Ordinal) ? text[1..] : text;
    }

    private static void WriteTree(SyntaxNode root)
    {
        var pending = new Stack<(SyntaxElement Element, int Depth)>();
        pending.Push((root, 0));

        while (pending.TryPop(out var item))
        {
            string indentation = new(' ', Math.Min(item.Depth, 40) * 2);

            if (item.Element is SyntaxNode node)
            {
                Console.Out.WriteLine(FormattableString.Invariant($"{indentation}{node.Kind} [{node.Span.Start}..{node.Span.End})"));

                for (var index = node.Children.Length - 1; index >= 0; index--)
                {
                    pending.Push((node.Children[index], item.Depth + 1));
                }
            }
            else if (item.Element is SyntaxTokenElement leaf)
            {
                Console.Out.WriteLine($"{indentation}{leaf.Token.Kind}{(leaf.Token.IsMissing ? " missing" : "")} \"{JsonEncodedText.Encode(leaf.Token.Text)}\"");
            }
        }
    }

    private static void WriteJson(string command, SourceText source, SyntaxTree? tree,
        ImmutableArray<SyntaxToken> lexicalTokens, ImmutableArray<Diagnostic> diagnostics, bool hasErrors)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("languageVersion", SyntaxTree.LanguageVersion);
        writer.WriteString("command", command);
        writer.WriteString("path", source.Path);
        writer.WriteString("coordinateSystem", "utf-16");
        writer.WriteNumber("sourceLength", source.Length);
        writer.WriteBoolean("success", !hasErrors);

        if (tree is not null)
        {
            writer.WriteString("contentId", tree.ContentId);
        }

        WriteDiagnostics(writer, source, diagnostics);

        if (command != "check")
        {
            var tokens = command == "tree" && tree is not null ? tree.Root.DescendantTokens().ToArray() : [.. lexicalTokens];
            writer.WriteStartArray("tokens");

            foreach (var token in tokens)
            {
                WriteToken(writer, token);
            }

            writer.WriteEndArray();

            if (command == "tree" && tree is not null)
            {
                var nodes = tree.Root.DescendantNodes(includeSelf: true).ToArray();
                var nodeIds = new Dictionary<SyntaxNode, int>();
                var tokenIds = new Dictionary<SyntaxToken, int>();

                for (var index = 0; index < nodes.Length; index++)
                {
                    nodeIds.Add(nodes[index], index);
                }

                for (var index = 0; index < tokens.Length; index++)
                {
                    tokenIds.Add(tokens[index], index);
                }

                writer.WriteNumber("root", 0);
                writer.WriteStartArray("nodes");

                foreach (var node in nodes)
                {
                    writer.WriteStartObject();
                    writer.WriteString("kind", node.Kind.ToString());
                    WriteSpan(writer, "span", node.Span);
                    WriteSpan(writer, "fullSpan", node.FullSpan);
                    writer.WriteStartArray("children");

                    foreach (var child in node.Children)
                    {
                        writer.WriteStartObject();

                        if (child is SyntaxNode childNode)
                        {
                            writer.WriteNumber("node", nodeIds[childNode]);
                        }
                        else if (child is SyntaxTokenElement leaf)
                        {
                            writer.WriteNumber("token", tokenIds[leaf.Token]);
                        }

                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }
        }

        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteToken(Utf8JsonWriter writer, SyntaxToken token)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", token.Kind.ToString());
        writer.WriteString("text", token.Text);
        writer.WriteBoolean("isMissing", token.IsMissing);
        WriteSpan(writer, "span", token.Span);
        WriteSpan(writer, "fullSpan", token.FullSpan);
        writer.WriteStartArray("leadingTrivia");

        foreach (var trivia in token.LeadingTrivia)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", trivia.Kind.ToString());
            writer.WriteString("text", trivia.Text);
            WriteSpan(writer, "span", trivia.Span);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string GetSeverityText(DiagnosticSeverity severity) => severity switch {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        DiagnosticSeverity.Info => "info",
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static void WriteDiagnostics(Utf8JsonWriter writer, SourceText source, ImmutableArray<Diagnostic> diagnostics)
    {
        writer.WriteStartArray("diagnostics");

        foreach (var diagnostic in diagnostics)
        {
            writer.WriteStartObject();
            WriteDiagnosticProperties(writer, source, diagnostic);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteDiagnosticProperties(Utf8JsonWriter writer, SourceText source, Diagnostic diagnostic)
    {
        var start = source.GetLinePosition(diagnostic.Span.Start);
        var end = source.GetLinePosition(diagnostic.Span.End);
        writer.WriteString("code", diagnostic.Code);
        writer.WriteString("severity", GetSeverityText(diagnostic.Severity));
        writer.WriteString("message", diagnostic.Message);
        WriteSpan(writer, "span", diagnostic.Span);
        writer.WriteStartObject("range");
        WritePosition(writer, "start", start);
        WritePosition(writer, "end", end);
        writer.WriteEndObject();
    }

    private static void WriteSpan(Utf8JsonWriter writer, string name, TextSpan span)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("start", span.Start);
        writer.WriteNumber("length", span.Length);
        writer.WriteEndObject();
    }

    private static void WritePosition(Utf8JsonWriter writer, string name, LinePosition position)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("line", position.Line);
        writer.WriteNumber("character", position.Character);
        writer.WriteEndObject();
    }

    private static void WriteInputError(bool json, string code, string message)
    {
        if (json)
        {
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteBoolean("success", false);
            writer.WriteStartArray("diagnostics");
            writer.WriteStartObject();
            writer.WriteString("code", code);
            writer.WriteString("severity", "error");
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        else
        {
            Console.Error.WriteLine($"{code}: {message}");
        }
    }
}
