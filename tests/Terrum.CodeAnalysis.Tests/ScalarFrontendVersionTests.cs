// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ScalarFrontendVersionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void SingleFunctionLayoutsAcceptOnlyCurrentTag(bool tree)
    {
        const string text = "int32 Value() { return 42; }";
        var source = SourceText.From(text);
        var words = Function(text, "Value", tree);
        Assert.That(ScalarFrontendHost.ReadResult(source, Bytes(words)).IsValid, Is.True);
        Assert.That(ScalarFrontendHost.BindLibrary(source, Bytes(words), BoundRuntimePolicy.FailFast).Functions, Has.Exactly(1).Items);
        foreach (var version in RejectedVersions())
        {
            words[0] = version;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)));
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void StandaloneV87RemainsBoundedToThreeParameters(bool tree)
    {
        var text = tree
            ? "int32 Sum(int32 first, int32 second, int32 third) => first;"
            : "int32 Sum(int32 first, int32 second, int32 third) { return first; }";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[5] = words[7] = words[8] = words[34] = 1;
        words[6] = 3;
        words[9] = text.IndexOf("Sum", StringComparison.Ordinal);
        words[10] = 3;
        words[11] = text.IndexOf("first", StringComparison.Ordinal);
        words[12] = 5;
        words[13] = text.IndexOf("second", StringComparison.Ordinal);
        words[14] = 6;
        words[35] = text.IndexOf("third", StringComparison.Ordinal);
        words[36] = 5;
        var firstReference = text.LastIndexOf("first", StringComparison.Ordinal);
        if (tree)
        {
            words[37] = 1;
            words[38] = words[40] = -1;
            words[39] = 0;
            words[41] = 1;
            words[42] = 0;
            words[43] = 1;
            words[44] = firstReference;
            words[45] = 5;
            words[46] = 1;
            words[47] = -1;
        }
        else
        {
            words[22] = 1;
            words[24] = 1;
            words[26] = firstReference;
            words[27] = 5;
        }

        var result = ScalarFrontendHost.ReadResult(source, Bytes(words));
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.ParameterCount, Is.EqualTo(3));
        Assert.That(result.FourthType, Is.EqualTo(0));
        Assert.That(result.FourthName, Is.EqualTo(default(TextSpan)));
        if (tree)
        {
            foreach (var maskWord in new[] { 16, 17 })
            {
                var fourthMask = words.ToArray();
                fourthMask[maskWord] = 1 << 3;
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
                    source, Bytes(fourthMask)));
            }
        }

        var fourParameterText = tree
            ? "int32 Sum(int32 first, int32 second, int32 third, int32 fourth) => first;"
            : "int32 Sum(int32 first, int32 second, int32 third, int32 fourth) { return first; }";
        var fourParameter = words.ToArray();
        fourParameter[6] = 4;
        fourParameter[18] = 1;
        fourParameter[19] = fourParameterText.IndexOf("fourth", StringComparison.Ordinal);
        fourParameter[20] = "fourth".Length;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(fourParameterText), Bytes(fourParameter)));
    }

    [Test]
    public void SingleFunctionValidationPreservesUtf16Spans()
    {
        const string prefix = "//\U0001D11E\n";
        var text = $"{prefix}int32 Value() {{ return 42; }}";
        var source = SourceText.From(text);
        Assert.That(Lexer.Lex(source).Tokens.Length, Is.EqualTo(10));
        Assert.That(SyntaxTree.Parse(source).HasErrors, Is.False);

        var words = Function(text, "Value", tree: true);
        var result = ScalarFrontendHost.ReadResult(source, Bytes(words));

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Name, Is.EqualTo(new TextSpan(text.IndexOf("Value", StringComparison.Ordinal), "Value".Length)));
        Assert.That(global::System.Linq.Enumerable.Single(result.Nodes).Span, Is.EqualTo(new TextSpan(text.IndexOf("42", StringComparison.Ordinal), 2)));
    }

    [Test]
    public void SingleFunctionValidationRejectsMalformedSyntaxLexicalErrorsAndAliasFailures()
    {
        const string function = "int32 Value() { return 42; }";
        var malformed = SourceText.From(function[..^1]);
        Assert.That(SyntaxTree.Parse(malformed).HasErrors, Is.True);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(malformed,
            Bytes(Function(malformed.Text, "Value", tree: true))));

        var lexicalErrorText = string.Concat("//", (char)0xD800, "\n", function);
        var lexicalError = SourceText.From(lexicalErrorText);
        Assert.That(Lexer.Lex(lexicalError).Diagnostics, Is.Not.Empty);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(lexicalError,
            Bytes(Function(lexicalErrorText, "Value", tree: true))));

        const string invalidAliasText = "alias Scalar = Missing; int32 Value() { return 42; }";
        var invalidAlias = SourceText.From(invalidAliasText);
        Assert.That(SyntaxTree.Parse(invalidAlias).HasErrors, Is.False);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(invalidAlias,
            Bytes(Function(invalidAliasText, "Value", tree: true))));
    }

    [Test]
    public void DiagnosticProjectionAcceptsOnlyCurrentTag()
    {
        var source = SourceText.From("not a scalar function");
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 2;
        words[2] = 1;
        words[4] = source.Length;
        Assert.That(ScalarFrontendHost.ReadResult(source, Bytes(words)).IsValid, Is.False);
        foreach (var version in RejectedVersions())
        {
            words[0] = version;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)));
        }
    }

    [Test]
    public void CompleteProgramAndEveryFrameAcceptOnlyCurrentTag()
    {
        const string text = "int32 First() { return 42; } int32 Second() { return 42; }";
        var source = SourceText.From(text);
        var bytes = Bytes(new int[ScalarFrontendHost.WordCount]);
        var words = new int[ScalarFrontendHost.ProgramWordCount];
        words[0] = 88;
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
            body[0] = 88;
            body.AsSpan(0, ScalarFrontendHost.ProgramBodyWordCount).CopyTo(words.AsSpan(frame + 4));
            words.AsSpan(frame + 4 + ScalarFrontendHost.ProgramBodyWordCount, 64).Fill(-1);
            start = end;
        }
        Assert.That(ScalarFrontendHost.ReadProgram(source, bytes, Bytes(words),
            BoundRuntimePolicy.FailFast).Projection.Functions.Length, Is.EqualTo(2));
        foreach (var offset in new[] { 0, 8, 8 + ScalarFrontendHost.ProgramFrameWordCount })
        {
            foreach (var version in RejectedProgramVersions())
            {
                words[offset] = version;
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source, bytes,
                    Bytes(words), BoundRuntimePolicy.FailFast));
            }
            words[offset] = 88;
        }
        Assert.That(ScalarFrontendHost.BindLibrary(source, bytes, Bytes(words),
            BoundRuntimePolicy.FailFast).Functions.Length, Is.EqualTo(2));
    }

    [Test]
    public void CompleteProgramCarriesAndValidatesFileScopedNamespaceIdentity()
    {
        const string text = "namespace Demo.Core; int32 Main() { return 42; }";
        var source = SourceText.From(text);
        var tokens = Lexer.Lex(source).Tokens;
        var start = Array.FindIndex(tokens.ToArray(), static token =>
            token.Kind == TokenKind.Identifier && token.Name == "int32");
        var end = start;
        while (tokens[end++].Kind != TokenKind.CloseBrace)
        {
        }
        var declaration = new TextSpan(tokens[start].Span.Start,
            tokens[end - 1].Span.End - tokens[start].Span.Start);
        var namespaceName = new TextSpan(text.IndexOf("Demo.Core", StringComparison.Ordinal),
            "Demo.Core".Length);
        var words = new int[ScalarFrontendHost.ProgramWordCount];
        words[0] = 88;
        words[1] = 1;
        words[2] = 1;
        var frame = 4;
        words[frame] = start;
        words[frame + 1] = end;
        words[frame + 2] = declaration.Start;
        words[frame + 3] = declaration.Length;
        var payload = Function(text, "Main", tree: true);
        payload[0] = 88;
        payload.AsSpan(0, ScalarFrontendHost.ProgramBodyWordCount).CopyTo(words.AsSpan(frame + 4));
        words[frame + 4 + 21] = namespaceName.Start;
        words[frame + 4 + 22] = namespaceName.Length;
        words.AsSpan(frame + 4 + ScalarFrontendHost.ProgramBodyWordCount, 64).Fill(-1);

        var bytes = Bytes(new int[ScalarFrontendHost.WordCount]);
        var programBytes = Bytes(words);
        var read = ScalarFrontendHost.ReadProgram(source, bytes, programBytes,
            BoundRuntimePolicy.FailFast);
        var function = global::System.Linq.Enumerable.Single(read.Projection.Functions);
        Assert.That(function.NamespaceNameSpan, Is.EqualTo(namespaceName));
        Assert.That(function.QualifiedName, Is.EqualTo("Demo.Core.Main"));
        Assert.That(global::System.Linq.Enumerable.Single(read.Program!.Functions).Name, Is.EqualTo("Demo.Core.Main"));

        var forgedNamespace = words.ToArray();
        forgedNamespace[frame + 4 + 21]++;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source, bytes,
            Bytes(forgedNamespace), BoundRuntimePolicy.FailFast));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
            Bytes(Function(text, "Main", tree: true))));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void CompleteProgramCarriesAndValidatesNamespaceBlockIdentity(BoundRuntimePolicy policy)
    {
        const string text = "namespace Demo.Core { int32 Main() { return 42; } }";
        var source = SourceText.From(text);
        var tokens = Lexer.Lex(source).Tokens;
        var start = Array.FindIndex(tokens.ToArray(), static token =>
            token.Kind == TokenKind.Identifier && token.Name == "int32");
        var end = start;
        while (tokens[end++].Kind != TokenKind.CloseBrace)
        {
        }
        var declaration = new TextSpan(tokens[start].Span.Start,
            tokens[end - 1].Span.End - tokens[start].Span.Start);
        var namespaceName = new TextSpan(text.IndexOf("Demo.Core", StringComparison.Ordinal),
            "Demo.Core".Length);
        var words = new int[ScalarFrontendHost.ProgramWordCount];
        words[0] = 88;
        words[1] = 1;
        words[2] = 1;
        var frame = 4;
        words[frame] = start;
        words[frame + 1] = end;
        words[frame + 2] = declaration.Start;
        words[frame + 3] = declaration.Length;
        var payload = Function(text, "Main", tree: true);
        payload[0] = 88;
        payload.AsSpan(0, ScalarFrontendHost.ProgramBodyWordCount).CopyTo(words.AsSpan(frame + 4));
        words[frame + 4 + 21] = namespaceName.Start;
        words[frame + 4 + 22] = namespaceName.Length;
        words.AsSpan(frame + 4 + ScalarFrontendHost.ProgramBodyWordCount, 64).Fill(-1);

        var bytes = Bytes(new int[ScalarFrontendHost.WordCount]);
        var read = ScalarFrontendHost.ReadProgram(source, bytes, Bytes(words),
            policy);
        var function = global::System.Linq.Enumerable.Single(read.Projection.Functions);
        Assert.That(function.NamespaceNameSpan, Is.EqualTo(namespaceName));
        Assert.That(function.QualifiedName, Is.EqualTo("Demo.Core.Main"));
        Assert.That(global::System.Linq.Enumerable.Single(read.Program!.Functions).Name, Is.EqualTo("Demo.Core.Main"));

        var forgedNamespace = words.ToArray();
        forgedNamespace[frame + 4 + 21]++;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source, bytes,
            Bytes(forgedNamespace), policy));
        var forgedDeclarationName = words.ToArray();
        forgedDeclarationName[frame + 4 + 9]++;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source, bytes,
            Bytes(forgedDeclarationName), policy));
    }
    [TestCase(9,64)]
    [TestCase(10,71)]
    [TestCase(32,225)]
    public void CompleteProgramInventoryAllowsMoreThan64AggregateTokens(int functionCount,
        int expectedTokenCount)
    {
        var text = string.Join(' ', Enumerable.Range(0, functionCount)
            .Select(index => $"int32 F{index}() => 0;"));
        var source = SourceText.From(text);
        var tokens = Lexer.Lex(source).Tokens;
        Assert.That(tokens.Length, Is.EqualTo(expectedTokenCount));

        var program = new int[ScalarFrontendHost.ProgramWordCount];
        program[0] = 88;
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

            var payload = new int[ScalarFrontendHost.ProgramBodyWordCount];
            payload[0] = 88;
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
            program.AsSpan(frame + 4 + ScalarFrontendHost.ProgramBodyWordCount, 64).Fill(-1);
            start = end;
        }

        var bytes = Bytes(new int[ScalarFrontendHost.WordCount]);
        var result = ScalarFrontendHost.ReadProgram(source, bytes, Bytes(program),
            BoundRuntimePolicy.FailFast).Projection;
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Functions.Length, Is.EqualTo(functionCount));
        Assert.That(result.Functions[0].Name, Is.EqualTo(new TextSpan(text.IndexOf("F0", StringComparison.Ordinal), 2)));
        var lastFunction = $"F{functionCount - 1}";
        Assert.That(result.Functions[^1].Name, Is.EqualTo(new TextSpan(text.LastIndexOf(lastFunction, StringComparison.Ordinal),
            lastFunction.Length)));
    }

    [Test]
    public void CompleteProgramInventoryRejectsMoreThanThirtyTwoFunctions()
    {
        var words = new int[ScalarFrontendHost.ProgramWordCount];
        words[0] = 88;
        words[1] = 1;
        words[2] = ScalarFrontendHost.MaximumFunctions + 1;
        var source = SourceText.From("void Main() { }");

        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source,
            Bytes(new int[ScalarFrontendHost.WordCount]), Bytes(words), BoundRuntimePolicy.FailFast));
    }

    private static int[] Function(string text, string name, bool tree)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
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
        for (var version = 0; version < 87; version++)
        {
            yield return version;
        }
        yield return 88;
        yield return int.MaxValue;
    }

    private static IEnumerable<int> RejectedProgramVersions()
    {
        yield return -1;
        for (var version = 0; version < 88; version++)
        {
            yield return version;
        }
        yield return 89;
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
