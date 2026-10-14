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

public sealed class VoidFrontendProjectionTests
{
    [TestCase("")]
    [TestCase("return;")]
    [TestCase("return ();")]
    [TestCase("return ((()));")]
    [TestCase("/*\U0001F600*/ return ( /*group*/ () );")]
    public void IndependentVoidReturnsMatchHostedNullValuesImplicitCompletionAndSpans(string body)
    {
        var text = $"public void @await() {{ {body} }} /*trailing*/";
        var words = Projection(text);
        var result = ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words));
        Assert.That(result.Nodes, Is.Empty);
        Assert.That(result.Locals, Is.Empty);
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var actual = ScalarFrontendHost.BindLibrary(SourceText.From(text), Bytes(words), policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            Assert.That(actual.Functions[0].ReturnType, Is.EqualTo(PrimitiveType.Void));
            Assert.That(actual.Functions[0].Name, Is.EqualTo("await"));
            Assert.That(actual.Functions[0].Body.Statements.Length, Is.EqualTo(hosted.Functions[0].Body.Statements.Length));
            for (var i = 0; i < actual.Functions[0].Body.Statements.Length; i++)
            {
                var expected = ((global::System.Func<BoundReturn>)(() => { var __nunitActual1 = hosted.Functions[0].Body.Statements[i]; Assert.That(__nunitActual1, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual1!; }))();
                var returned = ((global::System.Func<BoundReturn>)(() => { var __nunitActual2 = actual.Functions[0].Body.Statements[i]; Assert.That(__nunitActual2, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual2!; }))();
                Assert.That(returned.Value, Is.Null);
                Assert.That(returned.Span, Is.EqualTo(expected.Span));
            }
            Assert.That(NativeEmitter.EmitLibrary(actual, ["await"], (NativeRuntimePolicy)policy), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["await"], (NativeRuntimePolicy)policy)));
        }
    }

    [Test]
    public void EveryHistoricalVersionRejectsVoidHeaderAndNullReturnKind()
    {
        const string text = "public void @await() { return (); }";
        var words = Projection(text);
        Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid, Is.True);
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
    [TestCase(0,57)]
    [TestCase(5,1)]
    [TestCase(5,-1)]
    [TestCase(5,7)]
    [TestCase(37,1)]
    [TestCase(38,0)]
    [TestCase(39,0)]
    [TestCase(40,0)]
    [TestCase(41,1)]
    [TestCase(298,0)]
    [TestCase(298,13)]
    [TestCase(299,1)]
    [TestCase(299,9)]
    [TestCase(332,8)]
    [TestCase(332,21)]
    [TestCase(332,23)]
    [TestCase(333,1)]
    [TestCase(334,0)]
    [TestCase(335,0)]
    [TestCase(336,1)]
    [TestCase(391,1)]
    public void IndependentVoidRejectsForgedHeaderRootSpanKindAndCoverage(int offset, int value)
    {
        const string text = "public void @await() { return (); }";
        var words = Projection(text);
        words[offset] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }
    [TestCase("void","@void")]
    [TestCase("void","int32")]
    [TestCase("return ();","return 1;")]
    [TestCase("return ();","return true;")]
    [TestCase("return ();","return Missing();")]
    [TestCase("return ();","return () + ();")]
    [TestCase("return ();","return; Missing();")]
    [TestCase("return ();","return ()")]
    public void IndependentVoidRejectsContradictingSource(string before, string after)
    {
        const string text = "public void @await() { return (); }";
        var words = Projection(text);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace(before, after, StringComparison.Ordinal)), Bytes(words)));
    }
    [TestCase(16,true)]
    [TestCase(17,false)]
    public void IndependentVoidReturnUsesExistingExpressionDepth(int groups, bool valid)
    {
        var text = "public void @await() { return " + new string('(', groups) + new string(')', groups) + "; }";
        var words = Projection(text);
        if (valid)
        {
            Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid, Is.True);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        }
    }
    [TestCase(true)]
    [TestCase(false)]
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
            Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid, Is.True);
            words[300] = 0;
        }
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Test]
    public void IndependentVoidHeaderDoesNotAdmitVoidParameters()
    {
        const string text = "public void @await(bool a) {}";
        var words = Projection(text);
        words[6] = 1;
        words[7] = 5;
        words[11] = text.IndexOf("a)", StringComparison.Ordinal);
        words[12] = 1;
        Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid, Is.True);
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
