// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TreeFrontendProjectionTests
{
    [Fact]
    public void ReadResultRejectsInvalidTreeReferencesTypesAndSourceAnchors()
    {
        const string text = "public int32 Value(int32 x) => x + (x * 2);";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 20;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[6] = 1;
        words[7] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[11] = text.IndexOf("x)", StringComparison.Ordinal);
        words[12] = 1;
        words[37] = 6;
        words[38] = -1;
        words[39] = 5;
        words[40] = -1;
        words[41] = 1;
        var first = text.IndexOf("x +", StringComparison.Ordinal);
        var second = text.IndexOf("x *", StringComparison.Ordinal);
        var digit = text.IndexOf("2)", StringComparison.Ordinal);
        var open = text.IndexOf("(x *", StringComparison.Ordinal);
        var end = text.IndexOf(");", StringComparison.Ordinal) + 1;
        Node(0, 0, first, first + 1, 1, -1);
        Node(1, 0, second, second + 1, 1, -1);
        Node(2, 3, digit, digit + 1, -1, -1, 2);
        Node(3, 59, second, digit + 1, 1, 2);
        Node(4, 46, open, end, 3, -1);
        Node(5, 57, first, end, 0, 4);

        Assert.True(ScalarFrontendHost.ReadResult(source, Bytes()).IsValid);
        Assert.Equal(6, ScalarFrontendHost.ReadResult(source, Bytes()).Nodes.Length);

        var child = 42 + 5 * 8 + 5;
        words[child] = 5;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
        words[child] = 4;

        words[42 + 3 * 8 + 1] = 5;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
        words[42 + 3 * 8 + 1] = 1;

        words[42 + 4 * 8 + 2] = open + 1;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
        words[42 + 4 * 8 + 2] = open;

        words[39] = 4;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
        words[39] = 5;

        words[42 + 2 * 8 + 6] = 3;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));

        void Node(int index, int operation, int start, int stop, int left, int right, int low = 0)
        {
            var offset = 42 + index * 8;
            words[offset] = operation;
            words[offset + 1] = 1;
            words[offset + 2] = start;
            words[offset + 3] = stop - start;
            words[offset + 4] = left;
            words[offset + 5] = right;
            words[offset + 6] = low;
        }

        byte[] Bytes()
        {
            var bytes = new byte[words.Length * sizeof(int)];
            for (var index = 0; index < words.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4, 4), words[index]);
            }
            return bytes;
        }
    }
}
