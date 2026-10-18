// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics.Frontend;

public sealed class VoidExpressionFrontendProjectionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void IndependentVoidArrowCertifiesExactHostedFunctionBodyStatementCompletionAndHiddenLocal(bool discard)
    {
        var text = "/*\U0001F600*/ public void @await() => " + (discard ? "_ = true;" : "require true;") + " //trailing";
        var words = Projection(text, discard);
        var result = ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words));
        Assert.That(result.IsExpressionBody, Is.True);
        Assert.That(result.Statements, Has.Exactly(1).Items);
        Assert.That(result.Nodes, Has.Exactly(1).Items);
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
            Assert.That(function.Body.Statements.Length, Is.EqualTo(2));
            Assert.That(function.Body.Statements[0].Span, Is.EqualTo(expected.Body.Statements[0].Span));
            Assert.That(function.Body.Statements[1], Is.EqualTo(expected.Body.Statements[1]));
            if (discard)
            {
                Assert.That(global::System.Linq.Enumerable.Single(function.Variables), Is.EqualTo(expected.Variables[0]));
                Assert.That(function.Variables[0].Name, Is.EqualTo("<discard>"));
                var expectedBlock = ((global::System.Func<BoundBlock>)(() => { var __nunitActual1 = expected.Body.Statements[0]; Assert.That(__nunitActual1, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual1!; }))();
                var block = ((global::System.Func<BoundBlock>)(() => { var __nunitActual2 = function.Body.Statements[0]; Assert.That(__nunitActual2, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual2!; }))();
                Assert.That(block.IsManagedLexicalScope, Is.True);
                Assert.That(global::System.Linq.Enumerable.Single(block.Statements), Is.EqualTo(expectedBlock.Statements[0]));
            }
            else
            {
                Assert.That(function.Variables, Is.Empty);
                Assert.That(function.Body.Statements[0], Is.EqualTo(expected.Body.Statements[0]));
            }
            Assert.That(NativeEmitter.EmitLibrary(actual, ["await"], (NativeRuntimePolicy)policy), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["await"], (NativeRuntimePolicy)policy)));
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void HistoricalVersionsNeverAdmitVoidArrowStatementPayloads(bool discard)
    {
        var text = "public void @await() => " + (discard ? "_ = true;" : "require true;");
        var words = Projection(text, discard);
        for (var version = 1; version <= 56; version++)
        {
            words[0] = version;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        }
    }
    [TestCase(0,58)]
    [TestCase(5,1)]
    [TestCase(5,-1)]
    [TestCase(37,0)]
    [TestCase(37,2)]
    [TestCase(38,0)]
    [TestCase(39,0)]
    [TestCase(40,0)]
    [TestCase(41,0)]
    [TestCase(41,2)]
    [TestCase(43,1)]
    [TestCase(298,0)]
    [TestCase(298,2)]
    [TestCase(299,1)]
    [TestCase(332,8)]
    [TestCase(332,10)]
    [TestCase(332,22)]
    [TestCase(333,1)]
    [TestCase(334,-1)]
    [TestCase(334,1)]
    [TestCase(335,0)]
    [TestCase(336,1)]
    [TestCase(391,1)]
    public void IndependentVoidArrowRejectsForgedFeatureHeaderRootSlotTypeSpanAndCoverage(int offset, int value)
    {
        const string text = "public void @await() => require true;";
        var words = Projection(text, false);
        words[offset] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }
    [TestCase(299,0)]
    [TestCase(299,2)]
    [TestCase(300,0)]
    [TestCase(300,1)]
    [TestCase(301,1)]
    [TestCase(302,0)]
    [TestCase(303,2)]
    [TestCase(333,0)]
    [TestCase(333,2)]
    public void IndependentVoidArrowDiscardRequiresExactHiddenIdentity(int offset, int value)
    {
        const string text = "public void @await() => _ = true;";
        var words = Projection(text, true);
        words[offset] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }
    [TestCase("=>","{")]
    [TestCase("void","int32")]
    [TestCase("require","@require")]
    [TestCase("require true;","require true; require true;")]
    [TestCase("require true;","require true; void Other() {}")]
    [TestCase("require true;","();")]
    [TestCase("require true;","return;")]
    [TestCase("require true;","Missing();")]
    [TestCase("require true;","require 1;")]
    [TestCase("require true;","require true")]
    public void IndependentVoidArrowRejectsContradictingWholeSource(string before, string after)
    {
        const string text = "public void @await() => require true;";
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace(before, after, StringComparison.Ordinal)), Bytes(Projection(text, false))));
    }

    [Test]
    public void IndependentVoidArrowCannotChangeReadonlyParameterBindingOrReadHiddenStorage()
    {
        const string text = "public void @await(bool x) => _ = x;";
        var words = Projection(text, true);
        words[6] = 1;
        words[7] = 5;
        words[11] = text.IndexOf("x)", StringComparison.Ordinal);
        words[12] = 1;
        words[42] = 0;
        words[44] = text.LastIndexOf("x;", StringComparison.Ordinal);
        words[45] = 1;
        words[46] = 1;
        words[333] = 2;
        Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid, Is.True);
        words[46] = 2;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        words[46] = 1;
        words[7] = 1;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    private static int[] Projection(string text, bool discard)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = words[2] = 1;
        words[9] = text.IndexOf("@await", StringComparison.Ordinal);
        words[10] = 6;
        words[37] = 1;
        words[38] = words[39] = words[40] = -1;
        words[41] = 1;
        words[42] = 20;
        words[43] = 5;
        words[44] = text.IndexOf("true", StringComparison.Ordinal);
        words[45] = 4;
        words[46] = words[47] = -1;
        words[298] = 1;
        words[332] = discard ? 15 : 21;
        words[335] = text.IndexOf(discard ? "_" : "require", StringComparison.Ordinal);
        words[336] = text.IndexOf(';') + 1 - words[335];
        if (discard)
        {
            words[299] = 1;
            words[300] = 5;
            words[302] = words[335];
            words[303] = 1;
            words[333] = 1;
        }
        return words;
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
