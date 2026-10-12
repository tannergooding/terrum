// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class VoidFrontendProjectionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("return;")]
    [InlineData("return ();")]
    [InlineData("return ((()));")]
    [InlineData("/*\U0001F600*/ return ( /*group*/ () );")]
    public void IndependentVoidReturnsMatchHostedNullValuesImplicitCompletionAndSpans(string body)
    {
        var text = $"public void @await() {{ {body} }} /*trailing*/";
        var words = Projection(text);
        var result = ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words));
        Assert.Empty(result.Nodes);
        Assert.Empty(result.Locals);
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var actual = ScalarFrontendHost.BindLibrary(SourceText.From(text), Bytes(words), policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(PrimitiveType.Void, actual.Functions[0].ReturnType);
            Assert.Equal("await", actual.Functions[0].Name);
            Assert.Equal(hosted.Functions[0].Body.Statements.Length, actual.Functions[0].Body.Statements.Length);
            for (var i = 0; i < actual.Functions[0].Body.Statements.Length; i++)
            {
                var expected = Assert.IsType<BoundReturn>(hosted.Functions[0].Body.Statements[i]);
                var returned = Assert.IsType<BoundReturn>(actual.Functions[0].Body.Statements[i]);
                Assert.Null(returned.Value);
                Assert.Equal(expected.Span, returned.Span);
            }
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["await"], (NativeRuntimePolicy)policy),
                NativeEmitter.EmitLibrary(actual, ["await"], (NativeRuntimePolicy)policy));
        }
    }

    [Fact]
    public void EveryHistoricalVersionRejectsVoidHeaderAndNullReturnKind()
    {
        const string text = "public void @await() { return (); }";
        var words = Projection(text);
        Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid);
        for (var version = 1; version <= 55; version++)
        {
            words[0] = version;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        }
        const string scalar = "public int32 @await() { return 42; }";
        words = Projection(scalar);
        words[5] = 1;
        for (var version = 1; version <= 56; version++)
        {
            words[0] = version;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(scalar), Bytes(words)));
        }
    }

    [Theory]
    [InlineData(0, 57)]
    [InlineData(5, 1)]
    [InlineData(5, -1)]
    [InlineData(5, 7)]
    [InlineData(37, 1)]
    [InlineData(38, 0)]
    [InlineData(39, 0)]
    [InlineData(40, 0)]
    [InlineData(41, 1)]
    [InlineData(298, 0)]
    [InlineData(298, 13)]
    [InlineData(299, 1)]
    [InlineData(299, 9)]
    [InlineData(332, 8)]
    [InlineData(332, 21)]
    [InlineData(332, 23)]
    [InlineData(333, 1)]
    [InlineData(334, 0)]
    [InlineData(335, 0)]
    [InlineData(336, 1)]
    [InlineData(391, 1)]
    public void IndependentVoidRejectsForgedHeaderRootSpanKindAndCoverage(int offset, int value)
    {
        const string text = "public void @await() { return (); }";
        var words = Projection(text);
        words[offset] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Theory]
    [InlineData("void", "@void")]
    [InlineData("void", "int32")]
    [InlineData("return ();", "return 1;")]
    [InlineData("return ();", "return true;")]
    [InlineData("return ();", "return Missing();")]
    [InlineData("return ();", "return () + ();")]
    [InlineData("return ();", "return; Missing();")]
    [InlineData("return ();", "return ()")]
    public void IndependentVoidRejectsContradictingSource(string before, string after)
    {
        const string text = "public void @await() { return (); }";
        var words = Projection(text);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace(before, after, StringComparison.Ordinal)), Bytes(words)));
    }

    [Theory]
    [InlineData(16, true)]
    [InlineData(17, false)]
    public void IndependentVoidReturnUsesExistingExpressionDepth(int groups, bool valid)
    {
        var text = "public void @await() { return " + new string('(', groups) + new string(')', groups) + "; }";
        var words = Projection(text);
        if (valid)
        {
            Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IndependentVoidStillAuditsLexicalLocalVisibility(bool visible)
    {
        var text = visible
            ? "public void @await() { { bool x = true; require x; } }"
            : "public void @await() { { bool x = true; } require x; }";
        var words = Projection(text);
        words[37] = 2;
        words[298] = 3;
        words[299] = 1;
        words[300] = 5;
        words[302] = text.IndexOf("x =", StringComparison.Ordinal);
        words[303] = 1;
        Node(words, 0, 20, text.IndexOf("true", StringComparison.Ordinal), 4, -1);
        Node(words, 1, 0, text.IndexOf("x;", StringComparison.Ordinal), 1, 1);
        var block = text.IndexOf('{', text.IndexOf('{') + 1);
        Statement(words, 0, 14, visible ? 3 : 2, -1, block, text.IndexOf('}', block) + 1);
        Statement(words, 1, 1, 1, 0, text.IndexOf("bool x", StringComparison.Ordinal), text.IndexOf(';') + 1);
        var require = text.IndexOf("require", StringComparison.Ordinal);
        Statement(words, 2, 21, 0, 1, require, text.IndexOf(';', require) + 1);
        if (visible)
        {
            Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid);
            words[300] = 0;
        }
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Fact]
    public void IndependentVoidHeaderDoesNotAdmitVoidParameters()
    {
        const string text = "public void @await(bool a) {}";
        var words = Projection(text);
        words[6] = 1;
        words[7] = 5;
        words[11] = text.IndexOf("a)", StringComparison.Ordinal);
        words[12] = 1;
        Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid);
        words[7] = 0;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        words[7] = 5;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("bool a", "void a", StringComparison.Ordinal)), Bytes(words)));
    }

    private static int[] Projection(string text)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = words[2] = 1;
        words[9] = text.IndexOf("@await", StringComparison.Ordinal);
        words[10] = 6;
        words[38] = words[39] = words[40] = -1;
        var returned = text.IndexOf("return", StringComparison.Ordinal);
        if (returned >= 0)
        {
            words[298] = 1;
            Statement(words, 0, 22, 0, -1, returned, text.IndexOf(';', returned) + 1);
        }
        return words;
    }

    private static void Statement(int[] words, int index, int kind, int slot, int root, int start, int end)
    {
        var offset = 332 + index * 5;
        words[offset] = kind;
        words[offset + 1] = slot;
        words[offset + 2] = root;
        words[offset + 3] = start;
        words[offset + 4] = end - start;
    }

    private static void Node(int[] words, int index, int operation, int start, int length, int slot)
    {
        var offset = 42 + index * 8;
        words[offset] = operation;
        words[offset + 1] = 5;
        words[offset + 2] = start;
        words[offset + 3] = length;
        words[offset + 4] = slot;
        words[offset + 5] = -1;
    }

    private static byte[] Bytes(int[] words)
    {
        var bytes = new byte[words.Length * sizeof(int)];
        for (var i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * sizeof(int)), words[i]);
        }
        return bytes;
    }
}
