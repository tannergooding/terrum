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

public sealed class DiscardFrontendProjectionTests
{
    [Fact]
    public void IndependentDiscardCertifiesHiddenIdentityAndExactHostedLowering()
    {
        const string text = "public int32 Value() { _ = int32(7); return 42; }";
        var source = SourceText.From(text);
        var words = Projection(text);
        var bytes = Bytes(words);
        Assert.True(ScalarFrontendHost.ReadResult(source, bytes).IsValid);
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var actual = ScalarFrontendHost.BindLibrary(source, bytes, policy);
            var hosted = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
            var function = Assert.Single(actual.Functions);
            var variable = Assert.Single(function.Variables);
            Assert.Equal("<discard>", variable.Name);
            Assert.False(variable.IsMutable);
            var block = Assert.IsType<BoundBlock>(function.Body.Statements[0]);
            Assert.True(block.IsManagedLexicalScope);
            var local = Assert.IsType<BoundLocal>(Assert.Single(block.Statements));
            Assert.Equal(0, local.Variable);
            Assert.Equal(block.Span, local.Span);
            Assert.Equal(local.Span, variable.Span);
            Assert.Equal(NativeEmitter.EmitLibrary(Assert.IsType<BoundProgram>(hosted.Program), ["Value"],
                (NativeRuntimePolicy)policy), NativeEmitter.EmitLibrary(actual, ["Value"], (NativeRuntimePolicy)policy));
        }
    }

    [Theory]
    [InlineData(0, 52)]
    [InlineData(0, 49)]
    [InlineData(0, 48)]
    [InlineData(0, 21)]
    [InlineData(0, 54)]
    [InlineData(299, 0)]
    [InlineData(299, 9)]
    [InlineData(298, 13)]
    [InlineData(300, 2)]
    [InlineData(301, 1)]
    [InlineData(303, 2)]
    [InlineData(332, 10)]
    [InlineData(332, 1)]
    [InlineData(333, 0)]
    [InlineData(333, 2)]
    [InlineData(334, 0)]
    [InlineData(334, 2)]
    [InlineData(335, 0)]
    [InlineData(336, 1)]
    [InlineData(391, 1)]
    public void IndependentDiscardRejectsForgedVersionTypeIdentityRootAndExtent(int index, int value)
    {
        const string text = "public int32 Value() { _ = int32(7); return 42; }";
        var words = Projection(text);
        words[index] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Theory]
    [InlineData("_= int32(7);")]
    [InlineData("_ =int32(7);")]
    [InlineData("_ += int32(7);")]
    [InlineData("_ = int32(8);")]
    [InlineData("_ = _;")]
    public void IndependentDiscardRejectsContradictingSource(string statement)
    {
        const string text = "public int32 Value() { _ = int32(7); return 42; }";
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("_ = int32(7);", statement, StringComparison.Ordinal)),
            Bytes(Projection(text))));
    }

    [Fact]
    public void IndependentDiscardCannotClaimUntypedLiteralConstruction()
    {
        const string text = "public int32 Value() { _ = 7; return 42; }";
        var words = Projection(text);
        words[37] = 2;
        Array.Clear(words, 58, 16);
        Node(words, 0, 3, text.IndexOf('7'), 1, -1, 7);
        Node(words, 1, 3, text.IndexOf("42", StringComparison.Ordinal), 2, -1, 42);
        words[334] = 0;
        words[339] = 1;
        words[336] = text.IndexOf(';') + 1 - words[335];
        words[340] = text.IndexOf("return", StringComparison.Ordinal);
        words[341] = text.LastIndexOf(';') + 1 - words[340];
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Fact]
    public void IndependentDiscardRejectsSharedHiddenAnchorsAndReusedExpressionRoots()
    {
        const string text = "public int32 Value() { _ = int32(7); _ = true; return 42; }";
        var words = Projection(text);
        words[37] = 4;
        words[298] = 3;
        words[299] = 2;
        var second = text.IndexOf("_ = true", StringComparison.Ordinal);
        words[304] = 5;
        words[306] = second;
        words[307] = 1;
        Node(words, 2, 20, second + 4, 4, -1, 0);
        words[59] = 5;
        Node(words, 3, 3, text.IndexOf("42", StringComparison.Ordinal), 2, -1, 42);
        Statement(words, 1, 15, 2, 2, second, second + 9);
        Statement(words, 2, 8, 0, 3, text.IndexOf("return", StringComparison.Ordinal), text.LastIndexOf(';') + 1);
        Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid);
        words[306] = words[302];
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        words[306] = second;
        words[338] = 1;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        words[338] = 2;
        words[339] = 1;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    private static int[] Projection(string text)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 53;
        words[1] = words[2] = words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = 3;
        words[38] = words[39] = words[40] = -1;
        words[298] = 2;
        words[299] = 1;
        words[300] = 1;
        words[302] = text.IndexOf('_');
        words[303] = 1;
        var literal = text.IndexOf('7');
        Node(words, 0, 3, literal, 1, -1, 7);
        Node(words, 1, 83, text.IndexOf("int32(7)", StringComparison.Ordinal), 8, 0, 0);
        Node(words, 2, 3, text.IndexOf("42", StringComparison.Ordinal), 2, -1, 42);
        Statement(words, 0, 15, 1, 1, text.IndexOf('_'), text.IndexOf(';') + 1);
        Statement(words, 1, 8, 0, 2, text.IndexOf("return", StringComparison.Ordinal), text.LastIndexOf(';') + 1);
        return words;
    }

    [Theory]
    [InlineData(15, true)]
    [InlineData(16, false)]
    public void IndependentDiscardEnforcesNativeExpressionRecursionNotTreeHeight(int groups, bool valid)
    {
        var text = "public int32 Value() { _ = " + new string('(', groups)
            + "true" + new string(')', groups) + "; return 42; }";
        var words = Projection(text);
        Array.Clear(words, 42, 256);
        words[37] = groups + 2;
        words[300] = 5;
        var literal = text.IndexOf("true", StringComparison.Ordinal);
        Node(words, 0, 20, literal, 4, -1, 0);
        words[43] = 5;
        for (var index = 1; index <= groups; index++)
        {
            Node(words, index, 46, literal - index, 4 + 2 * index, index - 1, 0);
            words[43 + index * 8] = 5;
        }
        Node(words, groups + 1, 3, text.IndexOf("42", StringComparison.Ordinal), 2, -1, 42);
        words[334] = groups;
        words[339] = groups + 1;
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
    [InlineData(16384, true)]
    [InlineData(16385, false)]
    public void IndependentDiscardEnforcesSourceLimitIncludingTrailingTrivia(int length, bool valid)
    {
        const string text = "public int32 Value() { _ = int32(7); return 42; }";
        var padded = text + " /*" + new string('x', length - text.Length - 5) + "*/";
        Assert.Equal(length, padded.Length);
        if (valid)
        {
            Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(padded), Bytes(Projection(text))).IsValid);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
                SourceText.From(padded), Bytes(Projection(text))));
        }
    }

    private static void Node(int[] words, int index, int operation, int start, int length, int left, int low)
    {
        var offset = 42 + index * 8;
        words[offset] = operation;
        words[offset + 1] = 1;
        words[offset + 2] = start;
        words[offset + 3] = length;
        words[offset + 4] = left;
        words[offset + 5] = -1;
        words[offset + 6] = low;
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
