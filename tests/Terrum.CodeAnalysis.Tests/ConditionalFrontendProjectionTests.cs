// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ConditionalFrontendProjectionTests
{
    [Fact]
    public void ReaderChecksThreeConditionalRootsAndIndependentThirdChildren()
    {
        const string text = "public int32 Value(bool b) { if (b ? true : false) { return b ? 1 : 2; } return b ? 3 : 4; }";
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 85;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[6] = 1;
        words[7] = 5;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[11] = text.IndexOf("b)", StringComparison.Ordinal);
        words[12] = 1;
        words[15] = 2;
        words[37] = 12;
        words[38] = 3;
        words[39] = 7;
        words[40] = 11;
        var first = text.IndexOf("b ?", StringComparison.Ordinal);
        var second = text.IndexOf("b ?", first + 1, StringComparison.Ordinal);
        var third = text.IndexOf("b ?", second + 1, StringComparison.Ordinal);
        Node(0, 0, 5, first, first + 1, 1, -1);
        Node(1, 20, 5, text.IndexOf("true", StringComparison.Ordinal),
            text.IndexOf("true", StringComparison.Ordinal) + 4, -1, -1);
        Node(2, 21, 5, text.IndexOf("false", StringComparison.Ordinal),
            text.IndexOf("false", StringComparison.Ordinal) + 5, -1, -1);
        Node(3, 84, 5, first, text.IndexOf("false", StringComparison.Ordinal) + 5, 0, 1, 2);
        Node(4, 0, 5, second, second + 1, 1, -1);
        var one = text.IndexOf("1 :", StringComparison.Ordinal);
        var two = text.IndexOf("2;", StringComparison.Ordinal);
        Node(5, 3, 1, one, one + 1, -1, -1, 1);
        Node(6, 3, 1, two, two + 1, -1, -1, 2);
        Node(7, 84, 1, second, two + 1, 4, 5, 6);
        Node(8, 0, 5, third, third + 1, 1, -1);
        var three = text.IndexOf("3 :", StringComparison.Ordinal);
        var four = text.IndexOf("4;", StringComparison.Ordinal);
        Node(9, 3, 1, three, three + 1, -1, -1, 3);
        Node(10, 3, 1, four, four + 1, -1, -1, 4);
        Node(11, 84, 1, third, four + 1, 8, 9, 10);

        var source = SourceText.From(text);
        Assert.Equal(12, ScalarFrontendHost.ReadResult(source, Bytes()).Nodes.Length);
        foreach (var version in new[] { 20, 21, 22, 23, 25, 26, 27 })
        {
            Reject(0, version);
        }
        Reject(38, 7);
        Reject(39, 11);
        Reject(40, 7);
        Reject(43 + 3 * 8, 1);
        Reject(43 + 7 * 8, 5);
        Reject(42 + 7 * 8 + 4, 5);
        Reject(42 + 7 * 8 + 5, 7);
        Reject(42 + 7 * 8 + 6, 7);
        Reject(42 + 7 * 8 + 6, 5);
        Reject(42 + 7 * 8 + 7, 1);
        Reject(42 + 11 * 8 + 6, 6);
        Reject(42 + 3 * 8 + 2, first + 1);
        Reject(42 + 7 * 8 + 3, two - second);
        Reject(42 + 12 * 8, 1);
        Reject(298, 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("b ? 3", "b : 3", StringComparison.Ordinal)), Bytes()));

        void Reject(int index, int replacement)
        {
            var original = words[index];
            words[index] = replacement;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
            words[index] = original;
        }

        void Node(int index, int operation, int type, int start, int end,
            int left, int right, int low = 0)
        {
            var offset = 42 + index * 8;
            words[offset] = operation;
            words[offset + 1] = type;
            words[offset + 2] = start;
            words[offset + 3] = end - start;
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

    [Fact]
    public void LiteralOnlyConditionalCannotSupplyConversionInputContext()
    {
        const string text = "public uint64 Value(bool b) => uint64(b ? 1 : 2);";
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 85;
        words[1] = 1;
        words[2] = 1;
        words[5] = 4;
        words[6] = 1;
        words[7] = 5;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[11] = text.IndexOf("b)", StringComparison.Ordinal);
        words[12] = 1;
        words[37] = 5;
        words[38] = -1;
        words[39] = 4;
        words[40] = -1;
        words[41] = 1;
        var call = text.LastIndexOf("uint64(", StringComparison.Ordinal);
        var b = text.IndexOf("b ?", StringComparison.Ordinal);
        var one = text.IndexOf("1 :", StringComparison.Ordinal);
        var two = text.IndexOf("2)", StringComparison.Ordinal);
        Node(0, 0, 5, b, b + 1, 1, -1);
        Node(1, 3, 1, one, one + 1, -1, -1, 1);
        Node(2, 3, 1, two, two + 1, -1, -1, 2);
        Node(3, 84, 1, b, two + 1, 0, 1, 2);
        Node(4, 83, 4, call, two + 2, 3, -1);
        Assert.Throws<ArgumentException>(() =>
            ScalarFrontendHost.ReadResult(SourceText.From(text), Bytes()));

        void Node(int index, int operation, int type, int start, int end,
            int left, int right, int low = 0)
        {
            var offset = 42 + index * 8;
            words[offset] = operation;
            words[offset + 1] = type;
            words[offset + 2] = start;
            words[offset + 3] = end - start;
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
