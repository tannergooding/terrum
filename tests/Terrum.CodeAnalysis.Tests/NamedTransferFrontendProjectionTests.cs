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

public sealed class NamedTransferFrontendProjectionTests
{
    private const string Source = "public int32 Value() { @const: while (true) { do { break const; } while (false); } return 42; }";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentNamedTransferCertifiesAncestryDepthAndHostedLowering(bool continuing)
    {
        var text = continuing ? Source.Replace("break", "continue", StringComparison.Ordinal) : Source;
        var words = Projection(text, continuing);
        var bytes = Bytes(words);
        var result = ScalarFrontendHost.ReadResult(SourceText.From(text), bytes);
        Assert.True(result.IsValid);
        Assert.Equal(16, result.Statements[0].Kind);
        Assert.Equal(text.IndexOf("while", StringComparison.Ordinal), result.Statements[0].LoopSpan.Start);
        Assert.Equal(text.IndexOf("@const", StringComparison.Ordinal), result.Statements[0].Span.Start);
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var actual = ScalarFrontendHost.BindLibrary(SourceText.From(text), bytes, policy);
            var hosted = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
            var outer = Assert.IsType<BoundWhile>(actual.Functions[0].Body.Statements[0]);
            var inner = Assert.IsType<BoundWhile>(Assert.Single(Assert.IsType<BoundBlock>(outer.Body).Statements));
            var transfer = Assert.Single(Assert.IsType<BoundBlock>(inner.Body).Statements);
            Assert.Equal(2, continuing ? Assert.IsType<BoundContinue>(transfer).TargetDepth
                : Assert.IsType<BoundBreak>(transfer).TargetDepth);
            // The continuing case is deliberately compile-only.
            Assert.Equal(NativeEmitter.EmitLibrary(Assert.IsType<BoundProgram>(hosted.Program), ["Value"],
                (NativeRuntimePolicy)policy), NativeEmitter.EmitLibrary(actual, ["Value"], (NativeRuntimePolicy)policy));
        }
    }

    [Theory]
    [InlineData(0, 53)]
    [InlineData(0, 52)]
    [InlineData(0, 51)]
    [InlineData(0, 48)]
    [InlineData(0, 39)]
    [InlineData(332, 3)]
    [InlineData(332, 17)]
    [InlineData(332, 18)]
    [InlineData(333, 2)]
    [InlineData(333, 5)]
    [InlineData(334, 1)]
    [InlineData(335, 0)]
    [InlineData(336, 1)]
    [InlineData(337, 16)]
    [InlineData(337, 14)]
    [InlineData(338, 2)]
    [InlineData(339, 0)]
    [InlineData(342, 6)]
    [InlineData(342, 20)]
    [InlineData(343, -1)]
    [InlineData(343, 1)]
    [InlineData(343, 2)]
    [InlineData(343, 3)]
    [InlineData(344, 0)]
    [InlineData(345, 0)]
    [InlineData(346, 1)]
    [InlineData(298, 13)]
    [InlineData(391, 1)]
    public void IndependentNamedTransferRejectsForgedVersionKindTargetRootAndSpan(int index, int value)
    {
        var words = Projection(Source, false);
        words[index] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(Source), Bytes(words)));
    }

    [Theory]
    [InlineData("break const;", "break Const;")]
    [InlineData("break const;", "break other;")]
    [InlineData("@const:", "@const :")]
    [InlineData("do {", "if (true) {")]
    [InlineData("break const;", "break;")]
    [InlineData("break const;", "continue const;")]
    [InlineData("return 42;", "return 43;")]
    public void IndependentNamedTransferRejectsContradictingSource(string before, string after)
    {
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(
            Source.Replace(before, after, StringComparison.Ordinal)), Bytes(Projection(Source, false))));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("\t", false)]
    [InlineData("/* gap */", false)]
    [InlineData("//** gap **//", false)]
    [InlineData("\r", true)]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    [InlineData("\u0085", true)]
    [InlineData("\u2028", true)]
    [InlineData("\u2029", true)]
    [InlineData("/*\n*/", true)]
    [InlineData("/*\r\n*/", true)]
    [InlineData("/*\u2028*/", true)]
    [InlineData("// gap\n", true)]
    [InlineData("/// gap\n", true)]
    public void IndependentHistoricalAndEffectHeadersEnforcePhysicalLineAttachment(string gap, bool invalid)
    {
        foreach (var effects in new[] { false, true })
        {
            var text = (effects ? "[NoThrow] " : "")
                + Source.Replace("@const: ", "@const:" + gap, StringComparison.Ordinal);
            var words = Projection(text, false);
            if (effects)
            {
                words[0] = 59;
                words[2] = 3;
            }
            if (invalid)
            {
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
            }
            else
            {
                Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid);
            }
        }
    }

    [Theory]
    [InlineData(16384, true)]
    [InlineData(16385, false)]
    public void IndependentNamedTransferEnforcesUtf16SourceBudget(int length, bool valid)
    {
        var source = Source + " /*" + new string('x', length - Source.Length - 5) + "*/";
        Assert.Equal(length, source.Length);
        if (valid)
        {
            Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(source), Bytes(Projection(Source, false))).IsValid);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
                SourceText.From(source), Bytes(Projection(Source, false))));
        }
    }

    [Fact]
    public void IndependentNamedHeadersPreserveUnnamedNearestTransferRecords()
    {
        var text = Source.Replace("break const;", "break;", StringComparison.Ordinal);
        var words = Projection(text, false);
        words[342] = 6;
        words[343] = 1;
        words[346] = 6;
        Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid);
        words[343] = 0;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Fact]
    public void IndependentNamedTransferRejectsDuplicateActiveNormalizedLabelsAndInactiveTargets()
    {
        var duplicate = Source.Replace("do {", "const: do {", StringComparison.Ordinal);
        var words = Projection(duplicate, false);
        words[337] = 17;
        words[340] = duplicate.IndexOf("const: do", StringComparison.Ordinal);
        words[341] = duplicate.IndexOf(" } return", StringComparison.Ordinal) - words[340];
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(duplicate), Bytes(words)));

        const string inactive = "public int32 Value() { a: do {} while (false); while (true) { break a; } return 42; }";
        words = Projection(inactive, false);
        words[332] = 17;
        words[333] = 1;
        words[334] = 0;
        words[335] = inactive.IndexOf("a:", StringComparison.Ordinal);
        words[336] = inactive.IndexOf(';') + 1 - words[335];
        words[337] = 3;
        words[338] = 3;
        words[339] = 1;
        words[340] = inactive.IndexOf("while (true)", StringComparison.Ordinal);
        words[341] = inactive.IndexOf(" return", StringComparison.Ordinal) - words[340];
        Node(words, 0, 21, inactive.IndexOf("false", StringComparison.Ordinal), 5, 5, 0);
        Node(words, 1, 20, inactive.IndexOf("true", StringComparison.Ordinal), 4, 5, 0);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(inactive), Bytes(words)));
    }

    private static int[] Projection(string text, bool continuing)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 54;
        words[1] = words[2] = words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = 3;
        words[38] = words[39] = words[40] = -1;
        words[298] = 4;
        Node(words, 0, 20, text.IndexOf("true", StringComparison.Ordinal), 4, 5, 0);
        Node(words, 1, 21, text.IndexOf("false", StringComparison.Ordinal), 5, 5, 0);
        Node(words, 2, 3, text.IndexOf("42", StringComparison.Ordinal), 2, 1, 42);
        var outer = text.IndexOf("@const", StringComparison.Ordinal);
        var outerEnd = text.IndexOf(" return", StringComparison.Ordinal);
        Statement(words, 0, 16, 3, 0, outer, outerEnd);
        var inner = text.IndexOf("do {", StringComparison.Ordinal);
        Statement(words, 1, 11, 3, 1, inner, outerEnd - 2);
        var transfer = text.IndexOf(continuing ? "continue" : "break", StringComparison.Ordinal);
        Statement(words, 2, continuing ? 20 : 19, 0, -1, transfer, text.IndexOf(';') + 1);
        var returned = text.IndexOf("return", StringComparison.Ordinal);
        Statement(words, 3, 8, 0, 2, returned, text.LastIndexOf(';') + 1);
        return words;
    }

    private static void Node(int[] words, int index, int operation, int start, int length, int type, int low)
    {
        var offset = 42 + index * 8;
        words[offset] = operation;
        words[offset + 1] = type;
        words[offset + 2] = start;
        words[offset + 3] = length;
        words[offset + 4] = words[offset + 5] = -1;
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
