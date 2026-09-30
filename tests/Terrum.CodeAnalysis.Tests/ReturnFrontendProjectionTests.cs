// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ReturnFrontendProjectionTests
{
    [Fact]
    public void ReaderConsumesEveryReturnRootAndRejectsForgedSourceOrControl()
    {
        const string text = "public int32 Value(bool flag) { if (flag) { return 42; } else { return 43; } }";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 48;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[6] = 1;
        words[7] = 5;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[11] = text.IndexOf("flag", StringComparison.Ordinal);
        words[12] = 4;
        words[37] = 3;
        words[38] = -1;
        words[39] = -1;
        words[40] = -1;
        words[298] = 4;
        var branch = text.IndexOf("if (", StringComparison.Ordinal);
        var firstReturn = text.IndexOf("return 42;", StringComparison.Ordinal);
        var otherwise = text.IndexOf("else", StringComparison.Ordinal);
        var secondReturn = text.IndexOf("return 43;", StringComparison.Ordinal);
        Node(0, 0, 5, text.IndexOf("flag", branch, StringComparison.Ordinal), 4, 1, 0);
        Node(1, 3, 1, text.IndexOf("42", StringComparison.Ordinal), 2, -1, 42);
        Node(2, 3, 1, text.IndexOf("43", StringComparison.Ordinal), 2, -1, 43);
        var branchEnd = text.LastIndexOf('}') - 1;
        Statement(0, 4, 2, 0, branch, branchEnd);
        Statement(1, 8, 0, 1, firstReturn, firstReturn + 10);
        Statement(2, 5, 4, -1, otherwise, branchEnd);
        Statement(3, 8, 0, 2, secondReturn, secondReturn + 10);

        var result = ScalarFrontendHost.ReadResult(source, Bytes());
        Assert.Equal(4, result.Statements.Length);
        Assert.Equal(8, result.Statements[1].Kind);
        Assert.Equal(8, result.Statements[3].Kind);
        Reject(0, 47);
        Reject(15, 1);
        Reject(39, 1);
        Reject(37, 2);
        Reject(298, 3);
        Reject(339, 0);
        Reject(340, firstReturn + 1);
        Reject(341, 9);
        Reject(343, 9);
        Reject(349, 1);
        Reject(64, 44);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("return 43;", "return 44;", StringComparison.Ordinal)), Bytes()));
        const string incomplete = "public int32 Value(bool flag) { if (flag) { return 42; } }";
        words[37] = 2;
        words[298] = 2;
        words[333] = 2;
        words[336] = incomplete.LastIndexOf('}') - 1 - branch;
        Array.Clear(words, 58, 8);
        Array.Clear(words, 342, 10);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(incomplete), Bytes()));

        void Reject(int index, int value)
        {
            var old = words[index];
            words[index] = value;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
            words[index] = old;
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
            for (var index = 0; index < words.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int), sizeof(int)), words[index]);
            }
            return bytes;
        }
    }
}
