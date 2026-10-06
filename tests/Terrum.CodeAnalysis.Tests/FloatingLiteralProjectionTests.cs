// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class FloatingLiteralProjectionTests
{
    [Theory]
    [InlineData(false, 7, unchecked((int)0x3DCCCCCD), 0)]
    [InlineData(true, 8, unchecked((int)0x9999999A), 0x3FB99999)]
    public void ReaderRejectsMismatchedFloatingLiteralPayloads(bool tree, int type,
        int low, int high)
    {
        var text = tree
            ? $"public {TypeName(type)} Value() => 0.1;"
            : $"public {TypeName(type)} Value() {{ return 0.1; }}";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 85;
        words[1] = 1;
        words[2] = 1;
        words[5] = type;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        var literal = text.IndexOf("0.1", StringComparison.Ordinal);
        if (tree)
        {
            words[37] = 1;
            words[38] = -1;
            words[39] = 0;
            words[40] = -1;
            words[41] = 1;
            Node(42, 3, type, literal, 3, -1, -1, low, high);
        }
        else
        {
            words[22] = type;
            words[23] = 3;
            words[24] = low;
            words[25] = high;
            words[26] = literal;
            words[27] = 3;
        }

        Assert.True(ScalarFrontendHost.ReadResult(source, Encode(words)).IsValid);
        var lowIndex = tree ? 48 : 24;
        words[lowIndex] ^= 1;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Encode(words)));
        words[lowIndex] ^= 1;

        var highIndex = tree ? 49 : 25;
        words[highIndex] = type == 7 ? 1 : high ^ 1;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Encode(words)));

        void Node(int offset, int operation, int nodeType, int start, int length,
            int left, int right, int nodeLow, int nodeHigh)
        {
            words[offset] = operation;
            words[offset + 1] = nodeType;
            words[offset + 2] = start;
            words[offset + 3] = length;
            words[offset + 4] = left;
            words[offset + 5] = right;
            words[offset + 6] = nodeLow;
            words[offset + 7] = nodeHigh;
        }
    }

    private static string TypeName(int type) => type == 7 ? "float32" : "float64";

    private static byte[] Encode(int[] words)
    {
        var bytes = new byte[words.Length * sizeof(int)];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int), sizeof(int)),
                words[index]);
        }
        return bytes;
    }
}
