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

public sealed class VoidExpressionFrontendProjectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentVoidArrowCertifiesExactHostedFunctionBodyStatementCompletionAndHiddenLocal(bool discard)
    {
        var text = "/*\U0001F600*/ public void @await() => " + (discard ? "_ = true;" : "require true;") + " //trailing";
        var words = Projection(text, discard);
        var result = ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words));
        Assert.True(result.IsExpressionBody);
        Assert.Single(result.Statements);
        Assert.Single(result.Nodes);
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
            Assert.Equal(2, function.Body.Statements.Length);
            Assert.Equal(expected.Body.Statements[0].Span, function.Body.Statements[0].Span);
            Assert.Equal(expected.Body.Statements[1], function.Body.Statements[1]);
            if (discard)
            {
                Assert.Equal(expected.Variables[0], Assert.Single(function.Variables));
                Assert.Equal("<discard>", function.Variables[0].Name);
                var expectedBlock = Assert.IsType<BoundBlock>(expected.Body.Statements[0]);
                var block = Assert.IsType<BoundBlock>(function.Body.Statements[0]);
                Assert.True(block.IsManagedLexicalScope);
                Assert.Equal(expectedBlock.Statements[0], Assert.Single(block.Statements));
            }
            else
            {
                Assert.Empty(function.Variables);
                Assert.Equal(expected.Body.Statements[0], function.Body.Statements[0]);
            }
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["await"], (NativeRuntimePolicy)policy),
                NativeEmitter.EmitLibrary(actual, ["await"], (NativeRuntimePolicy)policy));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
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

    [Theory]
    [InlineData(0, 58)]
    [InlineData(5, 1)]
    [InlineData(5, -1)]
    [InlineData(37, 0)]
    [InlineData(37, 2)]
    [InlineData(38, 0)]
    [InlineData(39, 0)]
    [InlineData(40, 0)]
    [InlineData(41, 0)]
    [InlineData(41, 2)]
    [InlineData(43, 1)]
    [InlineData(298, 0)]
    [InlineData(298, 2)]
    [InlineData(299, 1)]
    [InlineData(332, 8)]
    [InlineData(332, 10)]
    [InlineData(332, 22)]
    [InlineData(333, 1)]
    [InlineData(334, -1)]
    [InlineData(334, 1)]
    [InlineData(335, 0)]
    [InlineData(336, 1)]
    [InlineData(391, 1)]
    public void IndependentVoidArrowRejectsForgedFeatureHeaderRootSlotTypeSpanAndCoverage(int offset, int value)
    {
        const string text = "public void @await() => require true;";
        var words = Projection(text, false);
        words[offset] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Theory]
    [InlineData(299, 0)]
    [InlineData(299, 2)]
    [InlineData(300, 0)]
    [InlineData(300, 1)]
    [InlineData(301, 1)]
    [InlineData(302, 0)]
    [InlineData(303, 2)]
    [InlineData(333, 0)]
    [InlineData(333, 2)]
    public void IndependentVoidArrowDiscardRequiresExactHiddenIdentity(int offset, int value)
    {
        const string text = "public void @await() => _ = true;";
        var words = Projection(text, true);
        words[offset] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Theory]
    [InlineData("=>", "{")]
    [InlineData("void", "int32")]
    [InlineData("require", "@require")]
    [InlineData("require true;", "require true; require true;")]
    [InlineData("require true;", "require true; void Other() {}")]
    [InlineData("require true;", "();")]
    [InlineData("require true;", "return;")]
    [InlineData("require true;", "Missing();")]
    [InlineData("require true;", "require 1;")]
    [InlineData("require true;", "require true")]
    public void IndependentVoidArrowRejectsContradictingWholeSource(string before, string after)
    {
        const string text = "public void @await() => require true;";
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace(before, after, StringComparison.Ordinal)), Bytes(Projection(text, false))));
    }

    [Fact]
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
        Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid);
        words[46] = 2;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        words[46] = 1;
        words[7] = 1;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    private static int[] Projection(string text, bool discard)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 57;
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
