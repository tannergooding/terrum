// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class DoForFrontendProjectionTests
{
    [Fact]
    public void VersionFiftyOneRequiresAnActualLoopHeader()
    {
        const string text = "public int32 Value() { return 42; }";
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 48;
        words[1] = words[2] = words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = words[298] = 1;
        words[38] = words[39] = words[40] = -1;
        words[42] = 3;
        words[43] = 1;
        words[44] = text.IndexOf("42", StringComparison.Ordinal);
        words[45] = 2;
        words[46] = words[47] = -1;
        words[48] = 42;
        words[332] = 8;
        words[335] = text.IndexOf("return", StringComparison.Ordinal);
        words[336] = 10;
        var bytes = new byte[words.Length * sizeof(int)];
        void Encode()
        {
            for (var i = 0; i < words.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * sizeof(int)), words[i]);
            }
        }
        var source = SourceText.From(text);
        Encode();
        Assert.True(ScalarFrontendHost.ReadResult(source, bytes).IsValid);
        words[0] = 51;
        Encode();
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentLoopProjectionRejectsForgedRolesAndOlderVersions(bool isFor)
    {
        var text = isFor
            ? "public int32 Value() { for (;;) { return 42; } }"
            : "public int32 Value() { do { return 42; } while (false); }";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 51;
        words[1] = words[2] = words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = isFor ? 1 : 2;
        words[38] = words[39] = words[40] = -1;
        words[298] = isFor ? 4 : 2;
        var keyword = text.IndexOf(isFor ? "for (" : "do {", StringComparison.Ordinal);
        var end = isFor
            ? text.IndexOf("}", text.IndexOf("return", StringComparison.Ordinal),
                StringComparison.Ordinal) + 1
            : text.IndexOf(");", keyword, StringComparison.Ordinal) + 2;
        var returnAt = text.IndexOf("return", StringComparison.Ordinal);
        var literalAt = text.IndexOf("42", StringComparison.Ordinal);
        Node(0, 3, 1, literalAt, 2, -1, 42);
        if (isFor)
        {
            var firstDelimiter = text.IndexOf(';', keyword);
            var secondDelimiter = text.IndexOf(';', firstDelimiter + 1);
            var close = text.IndexOf(')', secondDelimiter);
            Statement(0, 12, 4, -1, keyword, end);
            Statement(1, 13, 0, -1, firstDelimiter, firstDelimiter + 1);
            Statement(2, 13, 0, -1, close, close + 1);
            Statement(3, 8, 0, 0, returnAt, returnAt + 10);
        }
        else
        {
            var predicate = text.IndexOf("false", StringComparison.Ordinal);
            Node(1, 21, 5, predicate, 5, -1, 0);
            Statement(0, 11, 2, 1, keyword, end);
            Statement(1, 8, 0, 0, returnAt, returnAt + 10);
        }

        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            Assert.Single(ScalarFrontendHost.BindLibrary(source, Bytes(), policy).Functions);
        }
        Reject(0, 50);
        Reject(335, keyword + 1);
        Reject(333, isFor ? 3 : 1);
        Reject(334, isFor ? 0 : -1);
        if (isFor)
        {
            Reject(337, 8);
            Reject(339, 0);
            Reject(342, 1);
            Reject(344, 0);
            Reject(347, 1);
        }
        else
        {
            Reject(50, 1);
            Reject(336, end - keyword - 1);
        }

        void Reject(int index, int value)
        {
            var previous = words[index];
            words[index] = value;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
            words[index] = previous;
        }

        void Node(int index, int operation, int type, int start, int length, int left, int low)
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

        void Statement(int index, int kind, int slot, int root, int start, int finish)
        {
            var offset = 332 + index * 5;
            words[offset] = kind;
            words[offset + 1] = slot;
            words[offset + 2] = root;
            words[offset + 3] = start;
            words[offset + 4] = finish - start;
        }

        byte[] Bytes()
        {
            var bytes = new byte[words.Length * sizeof(int)];
            for (var index = 0; index < words.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), words[index]);
            }
            return bytes;
        }
    }
}
