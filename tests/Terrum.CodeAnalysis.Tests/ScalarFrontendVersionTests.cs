// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ScalarFrontendVersionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleFunctionLayoutsAcceptOnlyCurrentTag(bool tree)
    {
        const string text = "int32 Value() { return 42; }";
        var source = SourceText.From(text);
        var words = Function(text, "Value", tree);
        Assert.True(ScalarFrontendHost.ReadResult(source, Bytes(words)).IsValid);
        Assert.Single(ScalarFrontendHost.BindLibrary(source, Bytes(words), BoundRuntimePolicy.FailFast).Functions);
        foreach (var version in RejectedVersions())
        {
            words[0] = version;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)));
        }
    }

    [Fact]
    public void SingleFunctionValidationPreservesUtf16Spans()
    {
        const string prefix = "//\U0001D11E\n";
        var text = $"{prefix}int32 Value() {{ return 42; }}";
        var source = SourceText.From(text);
        Assert.Equal(10, Lexer.Lex(source).Tokens.Length);
        Assert.False(SyntaxTree.Parse(source).HasErrors);

        var words = Function(text, "Value", tree: true);
        var result = ScalarFrontendHost.ReadResult(source, Bytes(words));

        Assert.True(result.IsValid);
        Assert.Equal(new TextSpan(text.IndexOf("Value", StringComparison.Ordinal), "Value".Length),
            result.Name);
        Assert.Equal(new TextSpan(text.IndexOf("42", StringComparison.Ordinal), 2),
            Assert.Single(result.Nodes).Span);
    }

    [Fact]
    public void SingleFunctionValidationRejectsMalformedSyntaxLexicalErrorsAndAliasFailures()
    {
        const string function = "int32 Value() { return 42; }";
        var malformed = SourceText.From(function[..^1]);
        Assert.True(SyntaxTree.Parse(malformed).HasErrors);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(malformed,
            Bytes(Function(malformed.Text, "Value", tree: true))));

        var lexicalErrorText = string.Concat("//", (char)0xD800, "\n", function);
        var lexicalError = SourceText.From(lexicalErrorText);
        Assert.NotEmpty(Lexer.Lex(lexicalError).Diagnostics);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(lexicalError,
            Bytes(Function(lexicalErrorText, "Value", tree: true))));

        const string invalidAliasText = "alias Scalar = Missing; int32 Value() { return 42; }";
        var invalidAlias = SourceText.From(invalidAliasText);
        Assert.False(SyntaxTree.Parse(invalidAlias).HasErrors);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(invalidAlias,
            Bytes(Function(invalidAliasText, "Value", tree: true))));
    }

    [Fact]
    public void DiagnosticProjectionAcceptsOnlyCurrentTag()
    {
        var source = SourceText.From("not a scalar function");
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 85;
        words[1] = 2;
        words[2] = 1;
        words[4] = source.Length;
        Assert.False(ScalarFrontendHost.ReadResult(source, Bytes(words)).IsValid);
        foreach (var version in RejectedVersions())
        {
            words[0] = version;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)));
        }
    }

    [Fact]
    public void CompleteProgramAndEveryFrameAcceptOnlyCurrentTag()
    {
        const string text = "int32 First() { return 42; } int32 Second() { return 42; }";
        var source = SourceText.From(text);
        var bytes = Bytes(new int[ScalarFrontendHost.WordCount]);
        var words = new int[ScalarFrontendHost.ProgramWordCount];
        words[0] = 85;
        words[1] = 1;
        words[2] = 2;
        var tokens = Lexer.Lex(source).Tokens;
        var start = 0;
        for (var function = 0; function < 2; function++)
        {
            var end = start;
            while (tokens[end++].Kind != TokenKind.CloseBrace)
            {
            }
            var span = new TextSpan(tokens[start].Span.Start,
                tokens[end - 1].Span.End - tokens[start].Span.Start);
            var frame = 4 + function * ScalarFrontendHost.ProgramFrameWordCount;
            words[frame] = start;
            words[frame + 1] = end;
            words[frame + 2] = span.Start;
            words[frame + 3] = span.Length;
            var name = function == 0 ? "First" : "Second";
            var body = Function(new string(' ', span.Start) + text.Substring(span.Start, span.Length), name, true);
            body.CopyTo(words, frame + 4);
            words.AsSpan(frame + 396, 32).Fill(-1);
            start = end;
        }
        Assert.Equal(2, ScalarFrontendHost.ReadProgram(source, bytes, Bytes(words),
            BoundRuntimePolicy.FailFast).Projection.Functions.Length);
        foreach (var offset in new[] { 0, 8, 8 + ScalarFrontendHost.ProgramFrameWordCount })
        {
            foreach (var version in RejectedVersions())
            {
                words[offset] = version;
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source, bytes,
                    Bytes(words), BoundRuntimePolicy.FailFast));
            }
            words[offset] = 85;
        }
        Assert.Equal(2, ScalarFrontendHost.BindLibrary(source, bytes, Bytes(words),
            BoundRuntimePolicy.FailFast).Functions.Length);
    }

    [Theory]
    [InlineData(9, 64)]
    [InlineData(10, 71)]
    public void CompleteProgramInventoryPreservesThe64TokenBoundary(int functionCount,
        int expectedTokenCount)
    {
        var text = string.Join(' ', Enumerable.Range(0, functionCount)
            .Select(index => $"int32 F{index}() => 0;"));
        var source = SourceText.From(text);
        var tokens = Lexer.Lex(source).Tokens;
        Assert.Equal(expectedTokenCount, tokens.Length);

        var program = new int[ScalarFrontendHost.ProgramWordCount];
        program[0] = 85;
        program[1] = 1;
        program[2] = functionCount;
        var start = 0;
        for (var index = 0; index < functionCount; index++)
        {
            var end = start;
            while (tokens[end++].Kind != TokenKind.Semicolon)
            {
            }
            var span = new TextSpan(tokens[start].Span.Start,
                tokens[end - 1].Span.End - tokens[start].Span.Start);
            var frame = 4 + index * ScalarFrontendHost.ProgramFrameWordCount;
            program[frame] = start;
            program[frame + 1] = end;
            program[frame + 2] = span.Start;
            program[frame + 3] = span.Length;

            var payload = new int[ScalarFrontendHost.WordCount];
            payload[0] = 85;
            payload[1] = 1;
            payload[5] = 1;
            payload[9] = tokens[start + 1].Span.Start;
            payload[10] = tokens[start + 1].Span.Length;
            payload[37] = 1;
            payload[38] = -1;
            payload[39] = 0;
            payload[40] = -1;
            payload[41] = 1;
            payload[42] = 3;
            payload[43] = 1;
            payload[44] = tokens[end - 2].Span.Start;
            payload[45] = tokens[end - 2].Span.Length;
            payload[46] = payload[47] = -1;
            payload.CopyTo(program, frame + 4);
            program.AsSpan(frame + 396, 32).Fill(-1);
            start = end;
        }

        var bytes = Bytes(new int[ScalarFrontendHost.WordCount]);
        if (expectedTokenCount > 64)
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source,
                bytes, Bytes(program), BoundRuntimePolicy.FailFast));
            return;
        }

        var result = ScalarFrontendHost.ReadProgram(source, bytes, Bytes(program),
            BoundRuntimePolicy.FailFast).Projection;
        Assert.True(result.IsValid);
        Assert.Equal(functionCount, result.Functions.Length);
        Assert.Equal(new TextSpan(text.IndexOf("F0", StringComparison.Ordinal), 2),
            result.Functions[0].Name);
        Assert.Equal(new TextSpan(text.LastIndexOf("F8", StringComparison.Ordinal), 2),
            result.Functions[^1].Name);
    }

    private static int[] Function(string text, string name, bool tree)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 85;
        words[1] = words[5] = 1;
        words[9] = text.IndexOf(name, StringComparison.Ordinal);
        words[10] = name.Length;
        var literal = text.IndexOf("42", StringComparison.Ordinal);
        if (tree)
        {
            words[37] = 1;
            words[38] = words[40] = -1;
            words[42] = 3;
            words[43] = 1;
            words[44] = literal;
            words[45] = 2;
            words[46] = words[47] = -1;
            words[48] = 42;
        }
        else
        {
            words[22] = 1;
            words[23] = 3;
            words[24] = 42;
            words[26] = literal;
            words[27] = 2;
        }
        return words;
    }

    private static IEnumerable<int> RejectedVersions()
    {
        yield return -1;
        for (var version = 0; version < 85; version++)
        {
            yield return version;
        }
        yield return 86;
        yield return int.MaxValue;
    }

    private static byte[] Bytes(int[] words)
    {
        var bytes = new byte[words.Length * sizeof(int)];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), words[index]);
        }
        return bytes;
    }
}
