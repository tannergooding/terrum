// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class WhileFrontendProjectionTests
{
    [Fact]
    public void ReaderChecksNestedBodyExtentsConditionsAndSourceAnchors()
    {
        const string text = "public int32 Value() { mut int32 x = 0; while (x < 2) { while (x < 1) { x = x + 1; } x = x + 1; } return x; }";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = 14;
        words[38] = -1;
        words[39] = 13;
        words[40] = -1;
        words[298] = 5;
        words[299] = 1;
        words[300] = 1;
        words[301] = 1;
        words[302] = text.IndexOf("x = 0", StringComparison.Ordinal);
        words[303] = 1;

        var declaration = text.IndexOf("mut int32", StringComparison.Ordinal);
        var outer = text.IndexOf("while", StringComparison.Ordinal);
        var inner = text.IndexOf("while", outer + 1, StringComparison.Ordinal);
        var firstAssignment = text.IndexOf("x = x + 1;", inner, StringComparison.Ordinal);
        var secondAssignment = text.IndexOf("x = x + 1;", firstAssignment + 1, StringComparison.Ordinal);
        var firstRead = text.IndexOf("x < 2", StringComparison.Ordinal);
        var secondRead = text.IndexOf("x < 1", StringComparison.Ordinal);
        var returnRead = text.LastIndexOf("x;", StringComparison.Ordinal);
        Node(0, 3, 1, declaration + 14, 1, -1, -1, 0);
        Node(1, 0, 1, firstRead, 1, 1, -1);
        Node(2, 3, 1, firstRead + 4, 1, -1, -1, 2);
        Node(3, 67, 5, firstRead, 5, 1, 2);
        Node(4, 0, 1, secondRead, 1, 1, -1);
        Node(5, 3, 1, secondRead + 4, 1, -1, -1, 1);
        Node(6, 67, 5, secondRead, 5, 4, 5);
        Node(7, 0, 1, firstAssignment + 4, 1, 1, -1);
        Node(8, 3, 1, firstAssignment + 8, 1, -1, -1, 1);
        Node(9, 57, 1, firstAssignment + 4, 5, 7, 8);
        Node(10, 0, 1, secondAssignment + 4, 1, 1, -1);
        Node(11, 3, 1, secondAssignment + 8, 1, -1, -1, 1);
        Node(12, 57, 1, secondAssignment + 4, 5, 10, 11);
        Node(13, 0, 1, returnRead, 1, 1, -1);
        Statement(0, 1, 1, 0, declaration, text.IndexOf("0;", declaration, StringComparison.Ordinal) + 2);
        Statement(1, 3, 5, 3, outer, text.IndexOf("} return", StringComparison.Ordinal) + 1);
        Statement(2, 3, 4, 6, inner, text.IndexOf("} x =", inner, StringComparison.Ordinal) + 1);
        Statement(3, 2, 1, 9, firstAssignment, firstAssignment + 10);
        Statement(4, 2, 1, 12, secondAssignment, secondAssignment + 10);

        Assert.Equal(5, ScalarFrontendHost.ReadResult(source, Bytes()).Statements.Length);
        Reject(0, 21);
        Reject(0, 29);
        Reject(333 + 5, 4);
        Reject(333 + 5, 6);
        Reject(333 + 10, 5);
        Reject(334 + 5, 6);
        Reject(334 + 10, 3);
        Reject(335 + 10, outer);
        Reject(336 + 10, words[336 + 10] + 1);
        Reject(337 + 15, 1);
        Reject(301, 0);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("while (x < 1)", "while (x + 1)", StringComparison.Ordinal)), Bytes()));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("x = x + 1;", "x = x - 1;", StringComparison.Ordinal)), Bytes()));

        void Reject(int index, int value)
        {
            var old = words[index];
            words[index] = value;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
            words[index] = old;
        }

        void Node(int index, int operation, int type, int start, int length,
            int left, int right, int low = 0)
        {
            var offset = 42 + index * 8;
            words[offset] = operation;
            words[offset + 1] = type;
            words[offset + 2] = start;
            words[offset + 3] = length;
            words[offset + 4] = left;
            words[offset + 5] = right;
            words[offset + 6] = low;
        }

        void Statement(int index, int kind, int slot, int root, int start, int end)
        {
            var offset = 332 + index * 5;
            words[offset] = kind;
            words[offset + 1] = slot;
            words[offset + 2] = root;
            words[offset + 3] = start;
            words[offset + 4] = end - start;
        }

        byte[] Bytes()
        {
            var bytes = new byte[words.Length * sizeof(int)];
            for (var i = 0; i < words.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4, 4), words[i]);
            }
            return bytes;
        }
    }
}
