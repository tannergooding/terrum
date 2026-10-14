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

public sealed class NamedTransferFrontendProjectionTests
{
    private const string Source = "public int32 Value() { @const: while (true) { do { break const; } while (false); } return 42; }";
    [TestCase(false)]
    [TestCase(true)]
    public void IndependentNamedTransferCertifiesAncestryDepthAndHostedLowering(bool continuing)
    {
        var text = continuing ? Source.Replace("break", "continue", StringComparison.Ordinal) : Source;
        var words = Projection(text, continuing);
        var bytes = Bytes(words);
        var result = ScalarFrontendHost.ReadResult(SourceText.From(text), bytes);
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Statements[0].Kind, Is.EqualTo(16));
        Assert.That(result.Statements[0].LoopSpan.Start, Is.EqualTo(text.IndexOf("while", StringComparison.Ordinal)));
        Assert.That(result.Statements[0].Span.Start, Is.EqualTo(text.IndexOf("@const", StringComparison.Ordinal)));
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var actual = ScalarFrontendHost.BindLibrary(SourceText.From(text), bytes, policy);
            var hosted = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
            var outer = ((global::System.Func<BoundWhile>)(() => { var __nunitActual0 = actual.Functions[0].Body.Statements[0]; Assert.That(__nunitActual0, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual0!; }))();
            var inner = ((global::System.Func<BoundWhile>)(() => { var __nunitActual2 = global::System.Linq.Enumerable.Single(((global::System.Func<BoundBlock>)(() => { var __nunitActual1 = outer.Body; Assert.That(__nunitActual1, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual1!; }))().Statements); Assert.That(__nunitActual2, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual2!; }))();
            var transfer = global::System.Linq.Enumerable.Single(((global::System.Func<BoundBlock>)(() => { var __nunitActual3 = inner.Body; Assert.That(__nunitActual3, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual3!; }))().Statements);
            Assert.That(continuing ? ((global::System.Func<BoundContinue>)(() => { var __nunitActual4 = transfer; Assert.That(__nunitActual4, Is.TypeOf<BoundContinue>()); return (BoundContinue)__nunitActual4!; }))().TargetDepth
                : ((global::System.Func<BoundBreak>)(() => { var __nunitActual5 = transfer; Assert.That(__nunitActual5, Is.TypeOf<BoundBreak>()); return (BoundBreak)__nunitActual5!; }))().TargetDepth, Is.EqualTo(2));
            // The continuing case is deliberately compile-only.
            Assert.That(NativeEmitter.EmitLibrary(actual, ["Value"], (NativeRuntimePolicy)policy), Is.EqualTo(NativeEmitter.EmitLibrary(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = hosted.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), ["Value"],
                (NativeRuntimePolicy)policy)));
        }
    }
    [TestCase(0,53)]
    [TestCase(0,52)]
    [TestCase(0,51)]
    [TestCase(0,48)]
    [TestCase(0,39)]
    [TestCase(332,3)]
    [TestCase(332,17)]
    [TestCase(332,18)]
    [TestCase(333,2)]
    [TestCase(333,5)]
    [TestCase(334,1)]
    [TestCase(335,0)]
    [TestCase(336,1)]
    [TestCase(337,16)]
    [TestCase(337,14)]
    [TestCase(338,2)]
    [TestCase(339,0)]
    [TestCase(342,6)]
    [TestCase(342,20)]
    [TestCase(343,-1)]
    [TestCase(343,1)]
    [TestCase(343,2)]
    [TestCase(343,3)]
    [TestCase(344,0)]
    [TestCase(345,0)]
    [TestCase(346,1)]
    [TestCase(298,13)]
    [TestCase(391,1)]
    public void IndependentNamedTransferRejectsForgedVersionKindTargetRootAndSpan(int index, int value)
    {
        var words = Projection(Source, false);
        words[index] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(Source), Bytes(words)));
    }
    [TestCase("break const;","break Const;")]
    [TestCase("break const;","break other;")]
    [TestCase("@const:","@const :")]
    [TestCase("do {","if (true) {")]
    [TestCase("break const;","break;")]
    [TestCase("break const;","continue const;")]
    [TestCase("return 42;","return 43;")]
    public void IndependentNamedTransferRejectsContradictingSource(string before, string after)
    {
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(
            Source.Replace(before, after, StringComparison.Ordinal)), Bytes(Projection(Source, false))));
    }
    [TestCase("",false)]
    [TestCase(" ",false)]
    [TestCase("\t",false)]
    [TestCase("/* gap */",false)]
    [TestCase("//** gap **//",false)]
    [TestCase("\r",true)]
    [TestCase("\n",true)]
    [TestCase("\r\n",true)]
    [TestCase("\u0085",true)]
    [TestCase("\u2028",true)]
    [TestCase("\u2029",true)]
    [TestCase("/*\n*/",true)]
    [TestCase("/*\r\n*/",true)]
    [TestCase("/*\u2028*/",true)]
    [TestCase("// gap\n",true)]
    [TestCase("/// gap\n",true)]
    public void IndependentHistoricalAndEffectHeadersEnforcePhysicalLineAttachment(string gap, bool invalid)
    {
        foreach (var effects in new[] { false, true })
        {
            var text = (effects ? "[NoThrow] " : "")
                + Source.Replace("@const: ", "@const:" + gap, StringComparison.Ordinal);
            var words = Projection(text, false);
            if (effects)
            {
                words[0] = 87;
                words[2] = 3;
            }
            if (invalid)
            {
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
            }
            else
            {
                Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid, Is.True);
            }
        }
    }
    [TestCase(16384,true)]
    [TestCase(16385,false)]
    public void IndependentNamedTransferEnforcesUtf16SourceBudget(int length, bool valid)
    {
        var source = Source + " /*" + new string('x', length - Source.Length - 5) + "*/";
        Assert.That(source.Length, Is.EqualTo(length));
        if (valid)
        {
            Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(source), Bytes(Projection(Source, false))).IsValid, Is.True);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
                SourceText.From(source), Bytes(Projection(Source, false))));
        }
    }

    [Test]
    public void IndependentNamedHeadersPreserveUnnamedNearestTransferRecords()
    {
        var text = Source.Replace("break const;", "break;", StringComparison.Ordinal);
        var words = Projection(text, false);
        words[342] = 6;
        words[343] = 1;
        words[346] = 6;
        Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid, Is.True);
        words[343] = 0;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Test]
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
        words[0] = 87;
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
