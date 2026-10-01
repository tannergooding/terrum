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

public sealed class ElseIfFrontendProjectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentElseIfPreservesDirectNestedConditionalsSpansAndScopes(bool isVoid)
    {
        var text = Text(isVoid);
        var words = Projection(text, isVoid);
        var result = ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words));
        Assert.Equal(23, result.Statements[2].Kind);
        Assert.Equal(text.IndexOf("else if", StringComparison.Ordinal), result.Statements[2].Span.Start);
        Assert.Equal(result.Statements[3].Span.End, result.Statements[2].Span.End);
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            var actual = ScalarFrontendHost.BindLibrary(SourceText.From(text), Bytes(words), policy);
            var expected = hosted.Functions[0];
            var function = actual.Functions[0];
            Assert.Equal(expected.Span, function.Span);
            Assert.Equal(expected.Body.Span, function.Body.Span);
            Assert.Equal(expected.Body.IsManagedLexicalScope, function.Body.IsManagedLexicalScope);
            Check(Assert.IsType<BoundIf>(expected.Body.Statements[0]), Assert.IsType<BoundIf>(function.Body.Statements[0]));
            if (isVoid) { Assert.Equal(expected.Body.Statements[1], function.Body.Statements[1]); }
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["await"], (NativeRuntimePolicy)policy),
                NativeEmitter.EmitLibrary(actual, ["await"], (NativeRuntimePolicy)policy));
        }

        static void Check(BoundIf expected, BoundIf actual)
        {
            Assert.Equal(expected.Span, actual.Span);
            Assert.Equal(expected.Condition, actual.Condition);
            var trueBlock = Assert.IsType<BoundBlock>(actual.Then);
            var expectedTrue = Assert.IsType<BoundBlock>(expected.Then);
            Assert.Equal(expectedTrue.Span, trueBlock.Span);
            Assert.Equal(expectedTrue.IsManagedLexicalScope, trueBlock.IsManagedLexicalScope);
            Assert.Equal(expectedTrue.Statements[0], trueBlock.Statements[0]);
            if (expected.Else is BoundIf nested)
            {
                Check(nested, Assert.IsType<BoundIf>(actual.Else));
            }
            else
            {
                var falseBlock = Assert.IsType<BoundBlock>(actual.Else);
                var expectedFalse = Assert.IsType<BoundBlock>(expected.Else);
                Assert.Equal(expectedFalse.Span, falseBlock.Span);
                Assert.Equal(expectedFalse.IsManagedLexicalScope, falseBlock.IsManagedLexicalScope);
                Assert.Equal(expectedFalse.Statements[0], falseBlock.Statements[0]);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllHistoricalVersionsRejectElseIfMarker(bool isVoid)
    {
        var text = Text(isVoid);
        var words = Projection(text, isVoid);
        for (var version = 1; version <= 57; version++)
        {
            words[0] = version;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        }
    }

    [Theory]
    [InlineData(0, 59)]
    [InlineData(5, 0)]
    [InlineData(5, 5)]
    [InlineData(37, 4)]
    [InlineData(37, 6)]
    [InlineData(38, 0)]
    [InlineData(39, 1)]
    [InlineData(41, 1)]
    [InlineData(298, 6)]
    [InlineData(298, 13)]
    [InlineData(299, 1)]
    [InlineData(333, 3)]
    [InlineData(334, 1)]
    [InlineData(336, 1)]
    [InlineData(342, 5)]
    [InlineData(342, 4)]
    [InlineData(342, 24)]
    [InlineData(343, 3)]
    [InlineData(343, 6)]
    [InlineData(343, 8)]
    [InlineData(344, 0)]
    [InlineData(345, 0)]
    [InlineData(346, 1)]
    [InlineData(347, 3)]
    [InlineData(347, 23)]
    [InlineData(348, 6)]
    [InlineData(349, 0)]
    [InlineData(350, 0)]
    [InlineData(351, 1)]
    [InlineData(357, 23)]
    [InlineData(358, 6)]
    [InlineData(391, 1)]
    public void IndependentElseIfRejectsForgedHeaderMarkerEndChildOrderAssociationAndCoverage(int index, int value)
    {
        var text = Text(false);
        var words = Projection(text, false);
        words[index] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Theory]
    [InlineData("else if", "else { if")]
    [InlineData("else if", "else @if")]
    [InlineData("return 42;", "return 42; Missing();")]
    [InlineData("return 42;", "break;")]
    [InlineData("return 42;", "continue missing;")]
    [InlineData("else { return 0; }", "")]
    [InlineData("else { return 0; }", "else if (false) { return 0; }")]
    [InlineData("else { return 0; }", "else { return 0; } require true;")]
    public void IndependentElseIfRejectsContradictingSource(string before, string after)
    {
        var text = Text(false);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace(before, after, StringComparison.Ordinal)), Bytes(Projection(text, false))));
    }

    [Fact]
    public void NewVersionRequiresAnActualElseIfAndRejectsMixedArrowOrScalarNullReturns()
    {
        const string scalar = "public int32 @await() { return 42; }";
        var words = new int[ScalarFrontendHost.WordCount];
        Header(words, scalar, 1);
        words[37] = words[298] = 1;
        Node(words, 0, 3, 1, scalar.IndexOf("42", StringComparison.Ordinal), 2, 42);
        Statement(words, 0, 8, 0, 0, scalar.IndexOf("return", StringComparison.Ordinal), scalar.IndexOf(';') + 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(scalar), Bytes(words)));
        var text = Text(false);
        words = Projection(text, false);
        words[337] = 22;
        words[339] = -1;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        words = Projection(text, false);
        words[41] = 1;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Theory]
    [InlineData(363, 1)]
    [InlineData(358, 1)]
    [InlineData(307, 0)]
    [InlineData(347, 2)]
    [InlineData(348, 1)]
    public void IndependentElseIfRejectsWrongLoopTargetsOrLocalMembership(int index, int value)
    {
        const string text = "public void @await() { while (false) { if (false) { int32 x = 1; } else if (true) { int32 x = 2; break; } } }";
        var words = new int[ScalarFrontendHost.WordCount];
        Header(words, text, 0);
        words[37] = 5;
        words[298] = 7;
        words[299] = 2;
        var loop = text.IndexOf("while", StringComparison.Ordinal);
        var firstIf = text.IndexOf("if (", StringComparison.Ordinal);
        var marker = text.IndexOf("else if", StringComparison.Ordinal);
        var secondIf = marker + 5;
        var firstLocal = text.IndexOf("int32 x", StringComparison.Ordinal);
        var secondLocal = text.IndexOf("int32 x", firstLocal + 1, StringComparison.Ordinal);
        var transfer = text.IndexOf("break", StringComparison.Ordinal);
        var chainEnd = text.IndexOf('}', transfer) + 1;
        var loopEnd = text.IndexOf('}', chainEnd) + 1;
        words[300] = words[304] = 1;
        words[302] = firstLocal + 6;
        words[306] = secondLocal + 6;
        words[303] = words[307] = 1;
        Node(words, 0, 21, 5, text.IndexOf("false", StringComparison.Ordinal), 5, 0);
        Node(words, 1, 21, 5, text.IndexOf("false", firstIf, StringComparison.Ordinal), 5, 0);
        Node(words, 2, 3, 1, firstLocal + 10, 1, 1);
        Node(words, 3, 20, 5, text.IndexOf("true", StringComparison.Ordinal), 4, 0);
        Node(words, 4, 3, 1, secondLocal + 10, 1, 2);
        Statement(words, 0, 3, 7, 0, loop, loopEnd);
        Statement(words, 1, 4, 3, 1, firstIf, chainEnd);
        Statement(words, 2, 1, 1, 2, firstLocal, text.IndexOf(';', firstLocal) + 1);
        Statement(words, 3, 23, 7, -1, marker, chainEnd);
        Statement(words, 4, 4, 7, 3, secondIf, chainEnd);
        Statement(words, 5, 1, 2, 4, secondLocal, text.IndexOf(';', secondLocal) + 1);
        Statement(words, 6, 6, 0, -1, transfer, text.IndexOf(';', transfer) + 1);
        var source = SourceText.From(text);
        var actual = ScalarFrontendHost.BindLibrary(source, Bytes(words), BoundRuntimePolicy.FailFast);
        var hosted = Compilation.CreateLibrary(SyntaxTree.Parse(text)).Program;
        Assert.NotNull(hosted);
        Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["await"], NativeRuntimePolicy.FailFast),
            NativeEmitter.EmitLibrary(actual, ["await"], NativeRuntimePolicy.FailFast));
        var loopBody = Assert.IsType<BoundBlock>(Assert.IsType<BoundWhile>(actual.Functions[0].Body.Statements[0]).Body);
        Assert.True(loopBody.IsManagedLexicalScope);
        var conditional = Assert.IsType<BoundIf>(loopBody.Statements[0]);
        var nested = Assert.IsType<BoundIf>(conditional.Else);
        var nestedBody = Assert.IsType<BoundBlock>(nested.Then);
        Assert.Equal(1, Assert.IsType<BoundBreak>(nestedBody.Statements[1]).TargetDepth);
        words[index] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)));
    }

    private static string Text(bool isVoid) => isVoid
        ? "/*\U0001F600*/ public void @await() { if (false) { return; } else if (true) { return (); } else { return; } } //tail"
        : "/*\U0001F600*/ public int32 @await() { if (false) { return 1; } else if (true) { return 42; } else { return 0; } } //tail";

    [Fact]
    public void IndependentElseIfMarkerCannotSwallowASiblingStatement()
    {
        var text = Text(false).Replace("} } //tail", "} return 2; } //tail", StringComparison.Ordinal);
        var words = Projection(text, false);
        var returned = text.IndexOf("return 2", StringComparison.Ordinal);
        words[37] = 6;
        words[298] = 8;
        Node(words, 5, 3, 1, returned + 7, 1, 2);
        Statement(words, 7, 8, 0, 5, returned, text.IndexOf(';', returned) + 1);
        Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid);
        words[343] = 8;
        words[346] = text.IndexOf(';', returned) + 1 - words[345];
        words[336] = text.IndexOf(';', returned) + 1 - words[335];
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    private static int[] Projection(string text, bool isVoid)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        Header(words, text, isVoid ? 0 : 1);
        words[37] = isVoid ? 2 : 5;
        words[298] = 7;
        var firstIf = text.IndexOf("if (", StringComparison.Ordinal);
        var elseIf = text.IndexOf("else if", StringComparison.Ordinal);
        var secondIf = elseIf + 5;
        var lastElse = text.IndexOf("else {", StringComparison.Ordinal);
        var end = text.IndexOf('}', lastElse) + 1;
        Node(words, 0, 21, 5, text.IndexOf("false", StringComparison.Ordinal), 5, 0);
        Node(words, isVoid ? 1 : 2, 20, 5, text.IndexOf("true", StringComparison.Ordinal), 4, 0);
        var firstReturn = text.IndexOf("return", StringComparison.Ordinal);
        var secondReturn = text.IndexOf("return", firstReturn + 1, StringComparison.Ordinal);
        var lastReturn = text.IndexOf("return", secondReturn + 1, StringComparison.Ordinal);
        if (!isVoid)
        {
            Node(words, 1, 3, 1, firstReturn + 7, 1, 1);
            Node(words, 3, 3, 1, secondReturn + 7, 2, 42);
            Node(words, 4, 3, 1, lastReturn + 7, 1, 0);
        }
        Statement(words, 0, 4, 2, 0, firstIf, end);
        Statement(words, 1, isVoid ? 22 : 8, 0, isVoid ? -1 : 1, firstReturn, text.IndexOf(';', firstReturn) + 1);
        Statement(words, 2, 23, 7, -1, elseIf, end);
        Statement(words, 3, 4, 5, isVoid ? 1 : 2, secondIf, end);
        Statement(words, 4, isVoid ? 22 : 8, 0, isVoid ? -1 : 3, secondReturn, text.IndexOf(';', secondReturn) + 1);
        Statement(words, 5, 5, 7, -1, lastElse, end);
        Statement(words, 6, isVoid ? 22 : 8, 0, isVoid ? -1 : 4, lastReturn, text.IndexOf(';', lastReturn) + 1);
        return words;
    }

    private static void Header(int[] words, string text, int type)
    {
        words[0] = 58;
        words[1] = words[2] = 1;
        words[5] = type;
        words[9] = text.IndexOf("@await", StringComparison.Ordinal);
        words[10] = 6;
        words[38] = words[39] = words[40] = -1;
    }

    private static void Node(int[] words, int index, int operation, int type, int start, int length, int low)
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
        for (var i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * sizeof(int)), words[i]);
        }
        return bytes;
    }
}
