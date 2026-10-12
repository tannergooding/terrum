// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class BlockFrontendProjectionTests
{
    [Fact]
    public void IndependentSiblingBlocksRequireExactScopeAndDeclarationIdentity()
    {
        const string text = "public int32 Value() { { mut int32 x = 1; x++; } { int32 x = 42; return x; } }";
        var source = SourceText.From(text);
        var words = Header(text, 5, 6, 2);
        var firstBlock = text.IndexOf("{ mut", StringComparison.Ordinal);
        var secondBlock = text.IndexOf("{ int32", StringComparison.Ordinal);
        var firstDeclaration = firstBlock + 2;
        var secondDeclaration = secondBlock + 2;
        var update = text.IndexOf("x++", StringComparison.Ordinal);
        var returnAt = text.IndexOf("return", StringComparison.Ordinal);
        Local(words, 0, true, firstDeclaration + 10);
        Local(words, 1, false, secondDeclaration + 6);
        Node(words, 0, 3, firstDeclaration + 14, 1, -1, 1);
        Node(words, 1, 0, update, 1, 1, 0);
        Node(words, 2, 86, update, 3, 1, 0);
        Node(words, 3, 3, secondDeclaration + 10, 2, -1, 42);
        Node(words, 4, 0, returnAt + 7, 1, 2, 0);
        Statement(words, 0, 14, 3, -1, firstBlock, update + 6);
        Statement(words, 1, 1, 1, 0, firstDeclaration, firstDeclaration + 16);
        Statement(words, 2, 10, 0, 2, update, update + 4);
        Statement(words, 3, 14, 6, -1, secondBlock, returnAt + 11);
        Statement(words, 4, 1, 2, 3, secondDeclaration, secondDeclaration + 13);
        Statement(words, 5, 8, 0, 4, returnAt, returnAt + 9);

        var result = ScalarFrontendHost.ReadResult(source, Bytes(words));
        var maximumLengthSource = SourceText.From(text + new string(' ',
            ScannerHost.MaximumSourceLength - text.Length));
        Assert.True(ScalarFrontendHost.ReadResult(maximumLengthSource, Bytes(words)).IsValid);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(maximumLengthSource.Text + " "), Bytes(words)));
        Assert.Equal(result.Statements[0].Span, result.Statements[0].BodySpan);
        Assert.Equal(result.Statements[3].Span, result.Statements[3].BodySpan);
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var function = Assert.Single(ScalarFrontendHost.BindLibrary(source, Bytes(words), policy).Functions);
            Assert.True(Assert.IsType<BoundBlock>(function.Body.Statements[0]).IsManagedLexicalScope);
        }
        foreach (var version in new[] { 1, 20, 48, 49, 50, 51, 53 })
        {
            Reject(0, version);
        }
        Reject(332, 4);
        Reject(333, 2);
        Reject(333, 6);
        Reject(334, 0);
        Reject(335, firstBlock + 1);
        Reject(336, update + 5 - firstBlock);
        Reject(347, 5);
        Reject(348, 5);
        Reject(349, 4);
        Reject(350, firstBlock);
        Reject(354, 1);
        Reject(378, 1);
        Reject(78, 1);
        Reject(74, 3);
        Reject(306, firstDeclaration + 10);
        Reject(299, 9);
        Reject(298, 13);
        Reject(391, 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)[..^1]));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("x++;", "++x;", StringComparison.Ordinal)), Bytes(words)));

        void Reject(int index, int value)
        {
            var previous = words[index];
            words[index] = value;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)));
            words[index] = previous;
        }
    }

    [Fact]
    public void IndependentExpiredBlockIdentityCannotAuthorizeAnOuterReturn()
    {
        const string text = "public int32 Value() { { int32 x = 42; } return x; }";
        var source = SourceText.From(text);
        var words = Header(text, 2, 3, 1);
        var block = text.IndexOf("{ int32", StringComparison.Ordinal);
        var declaration = block + 2;
        var returnAt = text.IndexOf("return", StringComparison.Ordinal);
        Local(words, 0, false, declaration + 6);
        Node(words, 0, 3, declaration + 10, 2, -1, 42);
        Node(words, 1, 0, returnAt + 7, 1, 1, 0);
        Statement(words, 0, 14, 2, -1, block, declaration + 15);
        Statement(words, 1, 1, 1, 0, declaration, declaration + 13);
        Statement(words, 2, 8, 0, 1, returnAt, returnAt + 9);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)));
        words[333] = 3;
        words[336] = returnAt + 9 - block;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)));
    }

    [Fact]
    public void ReaderRejectsObsoleteBlockTagForOrdinaryReturn()
    {
        const string text = "public int32 Value() { return 42; }";
        var words = Header(text, 1, 1, 0);
        var returnAt = text.IndexOf("return", StringComparison.Ordinal);
        Node(words, 0, 3, returnAt + 7, 2, -1, 42);
        Statement(words, 0, 8, 0, 0, returnAt, returnAt + 10);
        words[0] = 87;
        Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid);
        words[0] = 52;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public void IndependentBlockDepthSharesTheStructuralLimit(int depth, bool valid)
    {
        var text = "public int32 Value() { " + new string('{', depth)
            + " return 42; " + new string('}', depth) + " }";
        var words = Header(text, 1, depth + 1, 0);
        var block = text.IndexOf('{') + 2;
        var returnAt = text.IndexOf("return", StringComparison.Ordinal);
        var closing = text.IndexOf('}', returnAt);
        Node(words, 0, 3, returnAt + 7, 2, -1, 42);
        for (var index = 0; index < depth; index++)
        {
            Statement(words, index, 14, depth + 1, -1, block + index, closing + depth - index);
        }
        Statement(words, depth, 8, 0, 0, returnAt, returnAt + 10);
        if (valid)
        {
            Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
        }
    }

    [Fact]
    public void IndependentBlockDoesNotBecomeANearestLoopTransferTarget()
    {
        const string text = "public int32 Value() { while (true) { { break; } } return 42; }";
        var words = Header(text, 2, 4, 0);
        var loop = text.IndexOf("while", StringComparison.Ordinal);
        var block = text.IndexOf("{ break", StringComparison.Ordinal);
        var transfer = text.IndexOf("break", StringComparison.Ordinal);
        var returnAt = text.IndexOf("return", StringComparison.Ordinal);
        Node(words, 0, 20, loop + 7, 4, -1, 0);
        words[43] = 5;
        Node(words, 1, 3, returnAt + 7, 2, -1, 42);
        Statement(words, 0, 3, 3, 0, loop, transfer + 10);
        Statement(words, 1, 14, 3, -1, block, transfer + 8);
        Statement(words, 2, 6, 0, -1, transfer, transfer + 6);
        Statement(words, 3, 8, 0, 1, returnAt, returnAt + 10);
        Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)).IsValid);
        words[343] = 1;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    [Fact]
    public void IndependentEmptyBlockCannotForgeAValueReturningSuccess()
    {
        const string text = "public int32 Value() { {} }";
        var words = Header(text, 0, 1, 0);
        var block = text.IndexOf("{}", StringComparison.Ordinal);
        Statement(words, 0, 14, 1, -1, block, block + 2);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes(words)));
    }

    private static int[] Header(string text, int nodes, int statements, int locals)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = words[2] = words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = nodes;
        words[38] = words[39] = words[40] = -1;
        words[298] = statements;
        words[299] = locals;
        return words;
    }

    private static void Local(int[] words, int index, bool mutable, int start)
    {
        var offset = 300 + index * 4;
        words[offset] = 1;
        words[offset + 1] = mutable ? 1 : 0;
        words[offset + 2] = start;
        words[offset + 3] = 1;
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
