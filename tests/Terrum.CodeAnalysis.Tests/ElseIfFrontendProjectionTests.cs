// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ElseIfFrontendProjectionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void IndependentElseIfPreservesDirectNestedConditionalsSpansAndScopes(bool isVoid)
    {
        var text = Text(isVoid);
        var words = Projection(text, isVoid);
        var result = ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words));
        Assert.That(result.Statements[2].Kind, Is.EqualTo(23));
        Assert.That(result.Statements[2].Span.Start, Is.EqualTo(text.IndexOf("else if", StringComparison.Ordinal)));
        Assert.That(result.Statements[2].Span.End, Is.EqualTo(result.Statements[3].Span.End));
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            var actual = ScalarFrontendHost.BindLibrary(SourceText.From(text), Bytes(words), policy);
            var expected = hosted.Functions[0];
            var function = actual.Functions[0];
            Assert.That(function.Span, Is.EqualTo(expected.Span));
            Assert.That(function.Body.Span, Is.EqualTo(expected.Body.Span));
            Assert.That(function.Body.IsManagedLexicalScope, Is.EqualTo(expected.Body.IsManagedLexicalScope));
            Check(((global::System.Func<BoundIf>)(() => { var __nunitActual1 = expected.Body.Statements[0]; Assert.That(__nunitActual1, Is.TypeOf<BoundIf>()); return (BoundIf)__nunitActual1!; }))(), ((global::System.Func<BoundIf>)(() => { var __nunitActual2 = function.Body.Statements[0]; Assert.That(__nunitActual2, Is.TypeOf<BoundIf>()); return (BoundIf)__nunitActual2!; }))());
            if (isVoid)
            { Assert.That(function.Body.Statements[1], Is.EqualTo(expected.Body.Statements[1])); }
            Assert.That(NativeEmitter.EmitLibrary(actual, ["await"], (NativeRuntimePolicy)policy), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["await"], (NativeRuntimePolicy)policy)));
        }

        static void Check(BoundIf expected, BoundIf actual)
        {
            Assert.That(actual.Span, Is.EqualTo(expected.Span));
            Assert.That(actual.Condition, Is.EqualTo(expected.Condition));
            var trueBlock = ((global::System.Func<BoundBlock>)(() => { var __nunitActual3 = actual.Then; Assert.That(__nunitActual3, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual3!; }))();
            var expectedTrue = ((global::System.Func<BoundBlock>)(() => { var __nunitActual4 = expected.Then; Assert.That(__nunitActual4, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual4!; }))();
            Assert.That(trueBlock.Span, Is.EqualTo(expectedTrue.Span));
            Assert.That(trueBlock.IsManagedLexicalScope, Is.EqualTo(expectedTrue.IsManagedLexicalScope));
            Assert.That(trueBlock.Statements[0], Is.EqualTo(expectedTrue.Statements[0]));
            if (expected.Else is BoundIf nested)
            {
                Check(nested, ((global::System.Func<BoundIf>)(() => { var __nunitActual5 = actual.Else; Assert.That(__nunitActual5, Is.TypeOf<BoundIf>()); return (BoundIf)__nunitActual5!; }))());
            }
            else
            {
                var falseBlock = ((global::System.Func<BoundBlock>)(() => { var __nunitActual6 = actual.Else; Assert.That(__nunitActual6, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual6!; }))();
                var expectedFalse = ((global::System.Func<BoundBlock>)(() => { var __nunitActual7 = expected.Else; Assert.That(__nunitActual7, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual7!; }))();
                Assert.That(falseBlock.Span, Is.EqualTo(expectedFalse.Span));
                Assert.That(falseBlock.IsManagedLexicalScope, Is.EqualTo(expectedFalse.IsManagedLexicalScope));
                Assert.That(falseBlock.Statements[0], Is.EqualTo(expectedFalse.Statements[0]));
            }
        }
    }
    [TestCase(false)]
    [TestCase(true)]
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
    [TestCase(0,59)]
    [TestCase(5,0)]
    [TestCase(5,5)]
    [TestCase(37,4)]
    [TestCase(37,6)]
    [TestCase(38,0)]
    [TestCase(39,1)]
    [TestCase(41,1)]
    [TestCase(298,6)]
    [TestCase(298,13)]
    [TestCase(299,1)]
    [TestCase(333,3)]
    [TestCase(334,1)]
    [TestCase(336,1)]
    [TestCase(342,5)]
    [TestCase(342,4)]
    [TestCase(342,24)]
    [TestCase(343,3)]
    [TestCase(343,6)]
    [TestCase(343,8)]
    [TestCase(344,0)]
    [TestCase(345,0)]
    [TestCase(346,1)]
    [TestCase(347,3)]
    [TestCase(347,23)]
    [TestCase(348,6)]
    [TestCase(349,0)]
    [TestCase(350,0)]
    [TestCase(351,1)]
    [TestCase(357,23)]
    [TestCase(358,6)]
    [TestCase(391,1)]
    public void IndependentElseIfRejectsForgedHeaderMarkerEndChildOrderAssociationAndCoverage(int index, int value)
    {
        var text = Text(false);
        var words = Projection(text, false);
        words[index] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }
    [TestCase("else if","else { if")]
    [TestCase("else if","else @if")]
    [TestCase("return 42;","return 42; Missing();")]
    [TestCase("return 42;","break;")]
    [TestCase("return 42;","continue missing;")]
    [TestCase("else { return 0; }","")]
    [TestCase("else { return 0; }","else if (false) { return 0; }")]
    [TestCase("else { return 0; }","else { return 0; } require true;")]
    public void IndependentElseIfRejectsContradictingSource(string before, string after)
    {
        var text = Text(false);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace(before, after, StringComparison.Ordinal)), Bytes(Projection(text, false))));
    }

    [Test]
    public void CurrentVersionCoversOrdinaryReturnsAndRejectsMixedArrowOrScalarNullReturns()
    {
        const string scalar = "public int32 @await() { return 42; }";
        var words = new int[ScalarFrontendHost.WordCount];
        Header(words, scalar, 1);
        words[37] = words[298] = 1;
        Node(words, 0, 3, 1, scalar.IndexOf("42", StringComparison.Ordinal), 2, 42);
        Statement(words, 0, 8, 0, 0, scalar.IndexOf("return", StringComparison.Ordinal), scalar.IndexOf(';') + 1);
        Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(scalar), Bytes(words)).IsValid, Is.True);
        words[0] = 58;
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
    [TestCase(363,1)]
    [TestCase(358,1)]
    [TestCase(307,0)]
    [TestCase(347,2)]
    [TestCase(348,1)]
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
        TestAssertions.NotNull(hosted);
        Assert.That(NativeEmitter.EmitLibrary(actual, ["await"], NativeRuntimePolicy.FailFast), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["await"], NativeRuntimePolicy.FailFast)));
        var loopBody = ((global::System.Func<BoundBlock>)(() => { var __nunitActual9 = ((global::System.Func<BoundWhile>)(() => { var __nunitActual8 = actual.Functions[0].Body.Statements[0]; Assert.That(__nunitActual8, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual8!; }))().Body; Assert.That(__nunitActual9, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual9!; }))();
        Assert.That(loopBody.IsManagedLexicalScope, Is.True);
        var conditional = ((global::System.Func<BoundIf>)(() => { var __nunitActual10 = loopBody.Statements[0]; Assert.That(__nunitActual10, Is.TypeOf<BoundIf>()); return (BoundIf)__nunitActual10!; }))();
        var nested = ((global::System.Func<BoundIf>)(() => { var __nunitActual11 = conditional.Else; Assert.That(__nunitActual11, Is.TypeOf<BoundIf>()); return (BoundIf)__nunitActual11!; }))();
        var nestedBody = ((global::System.Func<BoundBlock>)(() => { var __nunitActual12 = nested.Then; Assert.That(__nunitActual12, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual12!; }))();
        Assert.That(((global::System.Func<BoundBreak>)(() => { var __nunitActual13 = nestedBody.Statements[1]; Assert.That(__nunitActual13, Is.TypeOf<BoundBreak>()); return (BoundBreak)__nunitActual13!; }))().TargetDepth, Is.EqualTo(1));
        words[index] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)));
    }

    private static string Text(bool isVoid) => isVoid
        ? "/*\U0001F600*/ public void @await() { if (false) { return; } else if (true) { return (); } else { return; } } //tail"
        : "/*\U0001F600*/ public int32 @await() { if (false) { return 1; } else if (true) { return 42; } else { return 0; } } //tail";

    [Test]
    public void IndependentElseIfMarkerCannotSwallowASiblingStatement()
    {
        var text = Text(false).Replace("} } //tail", "} return 2; } //tail", StringComparison.Ordinal);
        var words = Projection(text, false);
        var returned = text.IndexOf("return 2", StringComparison.Ordinal);
        words[37] = 6;
        words[298] = 8;
        Node(words, 5, 3, 1, returned + 7, 1, 2);
        Statement(words, 7, 8, 0, 5, returned, text.IndexOf(';', returned) + 1);
        Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid, Is.True);
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
        words[0] = 87;
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
