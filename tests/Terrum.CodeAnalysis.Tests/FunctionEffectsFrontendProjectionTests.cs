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

public sealed class FunctionEffectsFrontendProjectionTests
{
    [Theory]
    [InlineData("[NoThrow]", "void", "{}", false, 2)]
    [InlineData("[NoThrow]", "void", "{ require true; }", false, 2)]
    [InlineData("[NoThrow]", "void", "=> require true;", true, 2)]
    [InlineData("[DoesNotReturn]", "void", "{ require false; }", false, 4)]
    [InlineData("[DoesNotReturn]", "void", "=> require false;", true, 4)]
    [InlineData("[NoThrow]", "int32", "{ return 42; }", false, 2)]
    [InlineData("[NoThrow]", "int32", "=> 42;", true, 2)]
    public void IndependentFunctionEffectsPreserveHostedFlagsCompletionSpansAndPe(
        string attribute, string type, string body, bool arrow, int effects)
    {
        var text = $"/*\U0001F600*/ {attribute} public {type} @await() {body} //tail";
        var words = Projection(text, type == "void", arrow, effects);
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var expected = Assert.IsType<BoundProgram>(compilation.Program);
            var actual = ScalarFrontendHost.BindLibrary(SourceText.From(text), Bytes(words), policy);
            var first = expected.Functions[0];
            var second = actual.Functions[0];
            Assert.Equal(first.IsNoThrow, second.IsNoThrow);
            Assert.Equal(first.DoesNotReturn, second.DoesNotReturn);
            Assert.Equal(first.Span, second.Span);
            Assert.Equal(first.Body.Span, second.Body.Span);
            Assert.Equal(first.Body.IsManagedLexicalScope, second.Body.IsManagedLexicalScope);
            Assert.Equal<BoundStatement>(first.Body.Statements, second.Body.Statements);
            Assert.Equal(NativeEmitter.EmitLibrary(expected, ["await"], (NativeRuntimePolicy)policy),
                NativeEmitter.EmitLibrary(actual, ["await"], (NativeRuntimePolicy)policy));
        }
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 4)]
    [InlineData(2, 7)]
    [InlineData(2, 10)]
    [InlineData(5, 1)]
    [InlineData(37, 0)]
    [InlineData(38, 0)]
    [InlineData(39, 0)]
    [InlineData(41, 1)]
    [InlineData(43, 1)]
    [InlineData(45, 1)]
    [InlineData(298, 0)]
    [InlineData(332, 8)]
    [InlineData(333, 1)]
    [InlineData(334, -1)]
    [InlineData(335, 0)]
    [InlineData(336, 1)]
    public void IndependentFunctionEffectsRejectContradictingFlagsHeadersRootsAndCoverage(int index, int value)
    {
        const string text = "[NoThrow] public void @await() { require true; }";
        var words = Projection(text, true, false, 2);
        words[index] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Theory]
    [InlineData("[nothrow]")]
    [InlineData("[NoThrow][NoThrow]")]
    [InlineData("[NoThrow(value)]")]
    [InlineData("[NoThrow()]")]
    [InlineData("[DoesNotReturn]")]
    [InlineData("[Cleanup]")]
    [InlineData("[ConstEval]")]
    [InlineData("[@NoThrow]")]
    [InlineData("")]
    [InlineData("public [NoThrow]")]
    public void IndependentFunctionEffectsRejectSpellingDuplicateArgumentUnknownAndAttachmentClaims(string prefix)
    {
        var text = $"{prefix} public void @await() {{ require true; }}";
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text), Bytes(Projection(text, true, false, 2))));
    }

    [Fact]
    public void AllHistoricalVersionsRejectEffectBitsEvenForOtherwiseCompatibleBodies()
    {
        const string text = "[NoThrow] public void @await() {}";
        var words = Projection(text, true, false, 2);
        for (var version = 1; version <= 58; version++)
        {
            words[0] = version;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        }
    }

    [Fact]
    public void EffectsBracedBodiesCannotClaimHistoricalScalarReturnEnvelopes()
    {
        const string text = "[NoThrow] public int32 @await() { return 42; }";
        var words = Projection(text, false, false, 2);
        Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid);
        words[298] = 0;
        Array.Clear(words, 332, 5);
        words[39] = 0;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Theory]
    [InlineData("[DoesNotReturn] public void @await() {}", true, 4)]
    [InlineData("[DoesNotReturn] public void @await() { return; }", true, 4)]
    [InlineData("[DoesNotReturn] public int32 @await() { return 42; }", false, 4)]
    [InlineData("[NoThrow] public void @await() { require false; }", true, 2)]
    public void IndependentEffectsUseSharedCompletionAndFailureProof(string text, bool isVoid, int effects)
    {
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text), Bytes(Projection(text, isVoid, false, effects))));
    }

    [Theory]
    [InlineData("[NoThrow][DoesNotReturn]")]
    [InlineData("[DoesNotReturn][NoThrow]")]
    public void IndependentCombinedEffectsPreserveUnreachableReturnAndNoImplicitCompletion(string attributes)
    {
        var text = $"{attributes} public void @await() {{ while (true) {{}} return; }}";
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 7;
        words[9] = text.IndexOf("@await", StringComparison.Ordinal);
        words[10] = 6;
        words[37] = 1;
        words[38] = words[39] = words[40] = -1;
        words[42] = 20;
        words[43] = 5;
        words[44] = text.IndexOf("true", StringComparison.Ordinal);
        words[45] = 4;
        words[46] = words[47] = -1;
        words[298] = 2;
        words[332] = 3;
        words[333] = 1;
        words[335] = text.IndexOf("while", StringComparison.Ordinal);
        words[336] = text.IndexOf('}', words[335]) + 1 - words[335];
        words[337] = 22;
        words[339] = -1;
        words[340] = text.IndexOf("return", StringComparison.Ordinal);
        words[341] = 7;
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            var actual = ScalarFrontendHost.BindLibrary(SourceText.From(text), Bytes(words), policy);
            var function = actual.Functions[0];
            Assert.True(function.IsNoThrow);
            Assert.True(function.DoesNotReturn);
            Assert.Equal(2, function.Body.Statements.Length);
            Assert.Equal(hosted.Functions[0].Body.Statements[1], function.Body.Statements[1]);
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["await"], (NativeRuntimePolicy)policy),
                NativeEmitter.EmitLibrary(actual, ["await"], (NativeRuntimePolicy)policy));
        }
        for (var version = 1; version <= 58; version++)
        {
            words[0] = version;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        }
    }

    private static int[] Projection(string text, bool isVoid, bool arrow, int effects)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = effects | 1;
        words[5] = isVoid ? 0 : 1;
        words[9] = text.IndexOf("@await", StringComparison.Ordinal);
        words[10] = 6;
        words[38] = words[39] = words[40] = -1;
        words[41] = arrow ? 1 : 0;
        var requirement = text.IndexOf("require", StringComparison.Ordinal);
        var returned = text.IndexOf("return", StringComparison.Ordinal);
        if (requirement >= 0 || !isVoid)
        {
            words[37] = 1;
            var start = requirement >= 0 ? requirement + 8 : text.IndexOf("42", StringComparison.Ordinal);
            var falseCondition = text.AsSpan(start).StartsWith("false", StringComparison.Ordinal);
            words[42] = requirement >= 0 ? falseCondition ? 21 : 20 : 3;
            words[43] = requirement >= 0 ? 5 : 1;
            words[44] = start;
            words[45] = requirement >= 0 ? falseCondition ? 5 : 4 : 2;
            words[46] = words[47] = -1;
            words[48] = requirement >= 0 ? 0 : 42;
        }
        if (requirement >= 0 || returned >= 0)
        {
            words[298] = 1;
            words[332] = requirement >= 0 ? 21 : isVoid ? 22 : 8;
            words[334] = isVoid && returned >= 0 ? -1 : 0;
            words[335] = requirement >= 0 ? requirement : returned;
            words[336] = text.IndexOf(';', words[335]) + 1 - words[335];
        }
        else if (!isVoid)
        {
            words[39] = 0;
        }
        return words;
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
