// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.Tools;

internal static class Program
{
    private const string Usage = "Usage: terrumc <check|tokens|tree> <path|-> [--json]\nChecks syntax only; '-' reads strict UTF-8 from stdin. Exit: 0 clean, 1 diagnostics, 2 usage/input error.";

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            Console.Out.WriteLine(Usage);

            return 0;
        }

        bool json = args.Contains("--json", StringComparer.Ordinal);

        if (args.Length is not (2 or 3) || args[0] is not ("check" or "tokens" or "tree")
            || (args.Length == 3 && args[2] != "--json") || args[1] == "--json")
        {
            WriteInputError(json, "TRM2000", Usage);

            return 2;
        }

        string text;

        try
        {
            if (args[1] == "-")
            {
                using Stream input = Console.OpenStandardInput();
                text = ReadUtf8(input);
            }
            else
            {
                using FileStream input = File.OpenRead(args[1]);
                text = ReadUtf8(input);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            WriteInputError(json, "TRM2001", exception.Message);

            return 2;
        }

        SourceText source = SourceText.From(text, args[1] == "-" ? "<stdin>" : args[1]);
        SyntaxTree? tree = args[0] == "tokens" ? null : SyntaxTree.Parse(source);
        ImmutableArray<SyntaxToken> tokens;
        ImmutableArray<Diagnostic> diagnostics;

        if (tree is null)
        {
            LexResult lex = Lexer.Lex(source);
            tokens = lex.Tokens;
            diagnostics = lex.Diagnostics;
        }
        else
        {
            tokens = tree.Tokens;
            diagnostics = tree.Diagnostics;
        }
        bool hasErrors = diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        if (json)
        {
            WriteJson(args[0], source, tree, tokens, diagnostics, hasErrors);
        }
        else
        {
            if (args[0] == "tokens")
            {
                foreach (SyntaxToken token in tokens)
                {
                    Console.Out.WriteLine(FormattableString.Invariant($"{token.Kind} [{token.Span.Start}..{token.Span.End}) {JsonSerializer.Serialize(token.Text)}"));
                }
            }
            else if (args[0] == "tree" && tree is not null)
            {
                WriteTree(tree.Root);
            }

            foreach (Diagnostic diagnostic in diagnostics)
            {
                LinePosition position = source.GetLinePosition(diagnostic.Span.Start);
                Console.Error.WriteLine(FormattableString.Invariant($"{source.Path}({position.Line + 1},{position.Character + 1}): {diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Code}: {diagnostic.Message}"));
            }

            if (args[0] == "check" && !hasErrors)
            {
                Console.Out.WriteLine($"{source.Path}: syntax OK (no semantic analysis)");
            }
        }

        return hasErrors ? 1 : 0;
    }

    private static string ReadUtf8(Stream input)
    {
        // Do not let StreamReader's BOM detection silently switch to UTF-16 or replace invalid bytes.
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string text = reader.ReadToEnd();

        return text.StartsWith('\uFEFF') ? text[1..] : text;
    }

    private static void WriteTree(SyntaxNode root)
    {
        var pending = new Stack<(SyntaxElement Element, int Depth)>();
        pending.Push((root, 0));

        while (pending.TryPop(out (SyntaxElement Element, int Depth) item))
        {
            string indentation = new(' ', Math.Min(item.Depth, 40) * 2);

            if (item.Element is SyntaxNode node)
            {
                Console.Out.WriteLine(FormattableString.Invariant($"{indentation}{node.Kind} [{node.Span.Start}..{node.Span.End})"));

                for (int index = node.Children.Length - 1; index >= 0; index--)
                {
                    pending.Push((node.Children[index], item.Depth + 1));
                }
            }
            else if (item.Element is SyntaxTokenElement leaf)
            {
                Console.Out.WriteLine($"{indentation}{leaf.Token.Kind}{(leaf.Token.IsMissing ? " missing" : "")} {JsonSerializer.Serialize(leaf.Token.Text)}");
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
            SyntaxToken[] tokens = command == "tree" && tree is not null ? tree.Root.DescendantTokens().ToArray() : lexicalTokens.ToArray();
            writer.WriteStartArray("tokens");

            foreach (SyntaxToken token in tokens)
            {
                WriteToken(writer, token);
            }

            writer.WriteEndArray();

            if (command == "tree" && tree is not null)
            {
                SyntaxNode[] nodes = tree.Root.DescendantNodes(includeSelf: true).ToArray();
                var nodeIds = new Dictionary<SyntaxNode, int>();
                var tokenIds = new Dictionary<SyntaxToken, int>();

                for (int index = 0; index < nodes.Length; index++)
                {
                    nodeIds.Add(nodes[index], index);
                }

                for (int index = 0; index < tokens.Length; index++)
                {
                    tokenIds.Add(tokens[index], index);
                }

                writer.WriteNumber("root", 0);
                writer.WriteStartArray("nodes");

                foreach (SyntaxNode node in nodes)
                {
                    writer.WriteStartObject();
                    writer.WriteString("kind", node.Kind.ToString());
                    WriteSpan(writer, "span", node.Span);
                    WriteSpan(writer, "fullSpan", node.FullSpan);
                    writer.WriteStartArray("children");

                    foreach (SyntaxElement child in node.Children)
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

        foreach (SyntaxTrivia trivia in token.LeadingTrivia)
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

    private static void WriteDiagnostics(Utf8JsonWriter writer, SourceText source, ImmutableArray<Diagnostic> diagnostics)
    {
        writer.WriteStartArray("diagnostics");

        foreach (Diagnostic diagnostic in diagnostics)
        {
            LinePosition start = source.GetLinePosition(diagnostic.Span.Start);
            LinePosition end = source.GetLinePosition(diagnostic.Span.End);
            writer.WriteStartObject();
            writer.WriteString("code", diagnostic.Code);
            writer.WriteString("severity", diagnostic.Severity.ToString().ToLowerInvariant());
            writer.WriteString("message", diagnostic.Message);
            WriteSpan(writer, "span", diagnostic.Span);
            writer.WriteStartObject("range");
            WritePosition(writer, "start", start);
            WritePosition(writer, "end", end);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
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
