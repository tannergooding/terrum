// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class RequireFrontendProjectionTests
{
    private const string Source = "public int32 Value() { require true; return 42; }";

    [Test]
    public void IndependentRequireCertifiesExactStatementAndHostedLoweringWithoutHiddenIdentity()
    {
        var bytes = Bytes(Projection(Source));
        var result = ScalarFrontendHost.ReadResult(SourceText.From(Source), bytes);
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Locals, Is.Empty);
        Assert.That(result.Statements[0].Kind, Is.EqualTo(21));
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var actual = ScalarFrontendHost.BindLibrary(SourceText.From(Source), bytes, policy);
            var hosted = Compilation.CreateLibrary(SyntaxTree.Parse(Source), policy);
            Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
            Assert.That(actual.Functions[0].Variables, Is.Empty);
            var requirement = ((global::System.Func<BoundRequire>)(() => { var __nunitActual0 = actual.Functions[0].Body.Statements[0]; Assert.That(__nunitActual0, Is.TypeOf<BoundRequire>()); return (BoundRequire)__nunitActual0!; }))();
            Assert.That(requirement.Span, Is.EqualTo(result.Statements[0].Span));
            Assert.That(requirement.Condition.Type, Is.EqualTo(PrimitiveType.Bool));
            Assert.That(NativeEmitter.EmitLibrary(actual, ["Value"], (NativeRuntimePolicy)policy), Is.EqualTo(NativeEmitter.EmitLibrary(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = hosted.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), ["Value"],
                (NativeRuntimePolicy)policy)));
        }
    }
    [TestCase(0,54)]
    [TestCase(0,53)]
    [TestCase(0,52)]
    [TestCase(0,51)]
    [TestCase(0,48)]
    [TestCase(0,21)]
    [TestCase(0,56)]
    [TestCase(43,1)]
    [TestCase(42,3)]
    [TestCase(37,3)]
    [TestCase(298,13)]
    [TestCase(299,9)]
    [TestCase(332,8)]
    [TestCase(332,10)]
    [TestCase(332,15)]
    [TestCase(332,22)]
    [TestCase(333,-1)]
    [TestCase(333,1)]
    [TestCase(334,-1)]
    [TestCase(334,1)]
    [TestCase(335,0)]
    [TestCase(336,1)]
    [TestCase(339,0)]
    [TestCase(391,1)]
    public void IndependentRequireRejectsForgedHistoricalKindTypeSlotRootSpanAndCoverage(int index, int value)
    {
        var words = Projection(Source);
        words[index] = value;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(Source), Bytes(words)));
    }
    [TestCase("require","@require")]
    [TestCase("require true","require 1")]
    [TestCase("require true","require false")]
    [TestCase("require true;","require true")]
    [TestCase("require true","static require true")]
    [TestCase("require true","ensure true")]
    public void IndependentRequireRejectsContradictingKeywordExpressionAndExtent(string before, string after)
    {
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(Source.Replace(before, after, StringComparison.Ordinal)), Bytes(Projection(Source))));
    }

    [Test]
    public void IndependentRequireCannotClaimALocalOrReuseAConditionRoot()
    {
        var words = Projection(Source);
        words[299] = 1;
        words[300] = 5;
        words[302] = Source.IndexOf("true", StringComparison.Ordinal);
        words[303] = 4;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(Source), Bytes(words)));

        const string text = "public int32 Value() { require true; require false; return 42; }";
        words = Projection(text);
        words[37] = words[298] = 3;
        Node(words, 1, 21, 5, text.IndexOf("false", StringComparison.Ordinal), 5, -1, 0);
        Node(words, 2, 3, 1, text.IndexOf("42", StringComparison.Ordinal), 2, -1, 42);
        Statement(words, 1, 21, 0, 1, text.IndexOf("require false", StringComparison.Ordinal),
            text.IndexOf("false;", StringComparison.Ordinal) + 6);
        Statement(words, 2, 8, 0, 2, text.IndexOf("return", StringComparison.Ordinal), text.LastIndexOf(';') + 1);
        Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid, Is.True);
        words[339] = 0;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }
    [TestCase(16384,true)]
    [TestCase(16385,false)]
    public void IndependentRequireEnforcesUtf16SourceLimit(int length, bool valid)
    {
        var source = Source + " /*" + new string('x', length - Source.Length - 5) + "*/";
        Assert.That(source.Length, Is.EqualTo(length));
        if (valid)
        {
            Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(source), Bytes(Projection(Source))).IsValid, Is.True);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(source), Bytes(Projection(Source))));
        }
    }
    [TestCase(true)]
    [TestCase(false)]
    public void IndependentRequireAuditsActualLocalMembership(bool visible)
    {
        var text = visible
            ? "public int32 Value() { { bool x = true; require x; } return 42; }"
            : "public int32 Value() { { bool x = true; } require x; return 42; }";
        var words = Projection(text);
        Array.Clear(words, 42, 256);
        Array.Clear(words, 332, 60);
        words[37] = 3;
        words[298] = 4;
        words[299] = 1;
        words[300] = 5;
        words[302] = text.IndexOf("x =", StringComparison.Ordinal);
        words[303] = 1;
        Node(words, 0, 20, 5, text.IndexOf("true", StringComparison.Ordinal), 4, -1, 0);
        Node(words, 1, 0, 5, text.IndexOf("x;", StringComparison.Ordinal), 1, 1, 0);
        Node(words, 2, 3, 1, text.IndexOf("42", StringComparison.Ordinal), 2, -1, 42);
        var blockStart = text.IndexOf('{', text.IndexOf('{') + 1);
        Statement(words, 0, 14, visible ? 3 : 2, -1, blockStart, text.IndexOf('}', blockStart) + 1);
        Statement(words, 1, 1, 1, 0, text.IndexOf("bool x", StringComparison.Ordinal), text.IndexOf(';') + 1);
        var require = text.IndexOf("require", StringComparison.Ordinal);
        Statement(words, 2, 21, 0, 1, require, text.IndexOf(';', require) + 1);
        Statement(words, 3, 8, 0, 2, text.IndexOf("return", StringComparison.Ordinal), text.LastIndexOf(';') + 1);
        if (visible)
        {
            Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid, Is.True);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        }
    }
    [TestCase(1)]
    [TestCase(2)]
    public void IndependentRequireCannotOccupyEitherForHeaderRecord(int clause)
    {
        const string text = "public int32 Value() { for (;;) { require true; return 42; } }";
        var words = Projection(text);
        Array.Clear(words, 332, 60);
        words[298] = 5;
        var start = text.IndexOf("for", StringComparison.Ordinal);
        var close = text.IndexOf(')', start);
        Statement(words, 0, 12, 5, -1, start, text.IndexOf('}', close) + 1);
        Statement(words, 1, 13, 0, -1, text.IndexOf(';', start), text.IndexOf(';', start) + 1);
        Statement(words, 2, 13, 0, -1, close, close + 1);
        var require = text.IndexOf("require", StringComparison.Ordinal);
        Statement(words, 3, 21, 0, 0, require, text.IndexOf(';', require) + 1);
        Statement(words, 4, 8, 0, 1, text.IndexOf("return", StringComparison.Ordinal), text.LastIndexOf(';') + 1);
        Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid, Is.True);
        words[332 + clause * 5] = 21;
        words[334 + clause * 5] = 0;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }
    [TestCase(15,true)]
    [TestCase(16,false)]
    public void IndependentRequireEnforcesNativeExpressionRecursion(int groups, bool valid)
    {
        var text = "public int32 Value() { require " + new string('(', groups)
            + "true" + new string(')', groups) + "; return 42; }";
        var words = Projection(text);
        Array.Clear(words, 42, 256);
        words[37] = groups + 2;
        var literal = text.IndexOf("true", StringComparison.Ordinal);
        Node(words, 0, 20, 5, literal, 4, -1, 0);
        for (var index = 1; index <= groups; index++)
        {
            Node(words, index, 46, 5, literal - index, 4 + index * 2, index - 1, 0);
        }
        Node(words, groups + 1, 3, 1, text.IndexOf("42", StringComparison.Ordinal), 2, -1, 42);
        words[334] = groups;
        words[339] = groups + 1;
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
    public void RequireUsesExistingNoThrowVerification(bool accepted)
    {
        var text = accepted ? Source : Source.Replace("true", "false", StringComparison.Ordinal);
        var program = ScalarFrontendHost.BindLibrary(SourceText.From(text), Bytes(Projection(text)),
            BoundRuntimePolicy.CheckedExceptions);
        program = program with { Functions = ImmutableArray.Create(program.Functions[0] with { IsNoThrow = true }) };
        if (accepted)
        {
            SemanticVerifier.VerifyLibrary(program);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program));
        }
    }

    private static int[] Projection(string text)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = words[2] = words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = words[298] = 2;
        words[38] = words[39] = words[40] = -1;
        var literal = text.IndexOf("true", StringComparison.Ordinal);
        var isTrue = literal >= 0;
        if (!isTrue)
        { literal = text.IndexOf("false", StringComparison.Ordinal); }
        Node(words, 0, isTrue ? 20 : 21, 5, literal, isTrue ? 4 : 5, -1, 0);
        Node(words, 1, 3, 1, text.IndexOf("42", StringComparison.Ordinal), 2, -1, 42);
        Statement(words, 0, 21, 0, 0, text.IndexOf("require", StringComparison.Ordinal), text.IndexOf(';') + 1);
        Statement(words, 1, 8, 0, 1, text.IndexOf("return", StringComparison.Ordinal), text.LastIndexOf(';') + 1);
        return words;
    }

    private static void Node(int[] words, int index, int operation, int type, int start, int length, int left, int low)
    {
        var offset = 42 + index * 8;
        words[offset] = operation;
        words[offset + 1] = type;
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
