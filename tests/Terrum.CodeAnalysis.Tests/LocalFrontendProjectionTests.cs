// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class LocalFrontendProjectionTests
{
    [Fact]
    public void ReadResultRejectsForgedLocalDeclarationsAndAnchors()
    {
        const string text = "public int32 Value(int32 x) { mut var y = x; return y; }";
        var words = Projection(text, assignment: false);
        var source = SourceText.From(text);
        Assert.True(ScalarFrontendHost.ReadResult(source, Bytes(words)).IsValid);
        Assert.Single(ScalarFrontendHost.ReadResult(source, Bytes(words)).Locals);

        Reject(0, 20);
        Reject(298, 13);
        Reject(299, 9);
        Reject(300, 3);
        Reject(301, 0);
        Reject(302, words[302] + 1);
        Reject(332, 2);
        Reject(333, 1);
        Reject(334, 1);
        Reject(335, words[335] + 1);
        Reject(42 + 8, 2);
        Reject(42 + 8 + 4, 3);
        Reject(391, 1);
        var altered = text.Replace("mut var y", "mut var z", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() =>
            ScalarFrontendHost.ReadResult(SourceText.From(altered), Bytes(words)));

        void Reject(int index, int value)
        {
            var old = words[index];
            words[index] = value;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)));
            words[index] = old;
        }
    }

    [Fact]
    public void ReadResultRejectsForgedAssignmentOrderAndMutability()
    {
        const string text = "public int32 Value(int32 x) { mut var y = x; y = y; return y; }";
        var words = Projection(text, assignment: true);
        var source = SourceText.From(text);
        Assert.Equal(2, ScalarFrontendHost.ReadResult(source, Bytes(words)).Statements.Length);

        Reject(301, 0);
        Reject(337, 1);
        Reject(338, 3);
        Reject(339, 0);
        Reject(340, words[335]);
        Reject(42 + 8 + 4, 3);
        Reject(42 + 16 + 1, 5);
        Reject(299, 2);

        void Reject(int index, int value)
        {
            var old = words[index];
            words[index] = value;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)));
            words[index] = old;
        }
    }

    [Fact]
    public void ReadResultAuditsTypedVacantLocalInitializationAuthority()
    {
        const string text = "public int32 Value(int32 x) { init int32 y; y = x; return y; }";
        var words = InitializationProjection(text);
        var source = SourceText.From(text);
        var result = ScalarFrontendHost.ReadResult(source, Bytes(words));
        Assert.True(result.IsValid);
        Assert.True(Assert.Single(result.Locals).IsInitialization);

        Reject(0, 60);
        Reject(301, 0);
        Reject(301, 3);
        Reject(334, 0);
        var altered = text.Replace("init int32 y", "init int32 z", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() =>
            ScalarFrontendHost.ReadResult(SourceText.From(altered), Bytes(words)));

        void Reject(int index, int value)
        {
            var old = words[index];
            words[index] = value;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)));
            words[index] = old;
        }
    }

    private static int[] Projection(string text, bool assignment)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 21;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[6] = 1;
        words[7] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[11] = text.IndexOf("x)", StringComparison.Ordinal);
        words[12] = 1;
        words[37] = assignment ? 3 : 2;
        words[38] = -1;
        words[39] = assignment ? 2 : 1;
        words[40] = -1;
        words[298] = assignment ? 2 : 1;
        words[299] = 1;
        words[300] = 1;
        words[301] = 1;
        words[302] = text.IndexOf("y = x", StringComparison.Ordinal);
        words[303] = 1;
        words[332] = 1;
        words[333] = 2;
        words[334] = 0;
        words[335] = text.IndexOf("mut var", StringComparison.Ordinal);
        words[336] = text.IndexOf("x;", words[335], StringComparison.Ordinal) + 2 - words[335];
        var firstRead = text.IndexOf("x;", words[335], StringComparison.Ordinal);
        Node(0, firstRead, 1);
        if (assignment)
        {
            var assignmentStart = text.IndexOf("y = y;", StringComparison.Ordinal);
            words[337] = 2;
            words[338] = 2;
            words[339] = 1;
            words[340] = assignmentStart;
            words[341] = 6;
            Node(1, assignmentStart + 4, 2);
            Node(2, text.LastIndexOf("y;", StringComparison.Ordinal), 2);
        }
        else
        {
            Node(1, text.LastIndexOf("y;", StringComparison.Ordinal), 2);
        }
        return words;

        void Node(int index, int start, int slot)
        {
            var offset = 42 + index * 8;
            words[offset] = 0;
            words[offset + 1] = 1;
            words[offset + 2] = start;
            words[offset + 3] = 1;
            words[offset + 4] = slot;
            words[offset + 5] = -1;
        }
    }

    private static int[] InitializationProjection(string text)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 61;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[6] = 1;
        words[7] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[11] = text.IndexOf("x)", StringComparison.Ordinal);
        words[12] = 1;
        words[37] = 2;
        words[38] = -1;
        words[39] = -1;
        words[40] = -1;
        words[300] = 1;
        words[301] = 2;
        words[302] = text.IndexOf("y;", StringComparison.Ordinal);
        words[303] = 1;
        words[298] = 3;
        words[299] = 1;
        var vacantStart = text.IndexOf("init int32 y;", StringComparison.Ordinal);
        words[332] = 25;
        words[333] = 2;
        words[334] = -1;
        words[335] = vacantStart;
        words[336] = "init int32 y;".Length;
        var assignmentStart = text.IndexOf("y = x;", StringComparison.Ordinal);
        words[337] = 2;
        words[338] = 2;
        words[339] = 0;
        words[340] = assignmentStart;
        words[341] = "y = x;".Length;
        var returnStart = text.IndexOf("return y;", StringComparison.Ordinal);
        words[342] = 8;
        words[343] = 0;
        words[344] = 1;
        words[345] = returnStart;
        words[346] = "return y;".Length;
        Node(0, text.IndexOf("x;", assignmentStart, StringComparison.Ordinal), 1);
        Node(1, text.LastIndexOf("y;", StringComparison.Ordinal), 2);
        return words;

        void Node(int index, int start, int slot)
        {
            var offset = 42 + index * 8;
            words[offset] = 0;
            words[offset + 1] = 1;
            words[offset + 2] = start;
            words[offset + 3] = 1;
            words[offset + 4] = slot;
            words[offset + 5] = -1;
        }
    }

    private static byte[] Bytes(int[] words)
    {
        var bytes = new byte[words.Length * sizeof(int)];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4, 4), words[index]);
        }
        return bytes;
    }
}
