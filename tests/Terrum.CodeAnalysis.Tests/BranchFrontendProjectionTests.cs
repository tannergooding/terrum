// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class BranchFrontendProjectionTests
{
    [TestCase("if",32,4)]
    [TestCase("while",36,3)]
    public void NestedBodyCannotConsumeAnEnclosingElse(string keyword, int version, int kind)
    {
        var text = $"public int32 Value() {{ if (true) {{ {keyword} (false) {{ }} }} else {{ }} return 42; }}";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        var outer = text.IndexOf("if (true)", StringComparison.Ordinal);
        var inner = text.IndexOf(keyword + " (false)", StringComparison.Ordinal);
        var otherwise = text.IndexOf("else", StringComparison.Ordinal);
        var end = text.IndexOf("} return", StringComparison.Ordinal) + 1;
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = 3;
        words[38] = -1;
        words[39] = 2;
        words[40] = -1;
        words[298] = 3;
        int[] condition = [20, 5, text.IndexOf("true", StringComparison.Ordinal), 4, -1, -1, 0, 0];
        int[] innerCondition = [21, 5, text.IndexOf("false", StringComparison.Ordinal), 5, -1, -1, 0, 0];
        int[] result = [3, 1, text.IndexOf("42", StringComparison.Ordinal), 2, -1, -1, 42, 0];
        condition.CopyTo(words, 42);
        innerCondition.CopyTo(words, 50);
        result.CopyTo(words, 58);
        int[] branch = [4, 2, 0, outer, end - outer];
        int[] nested = [kind, 2, 1, inner, text.IndexOf('}', inner) + 1 - inner];
        int[] marker = [5, 3, -1, otherwise, end - otherwise];
        branch.CopyTo(words, 332);
        nested.CopyTo(words, 337);
        marker.CopyTo(words, 342);
        var parsed = ScalarFrontendHost.ReadResult(source, Encode(words));
        Assert.That(parsed.Statements.Length, Is.EqualTo(3));
        Assert.That(text.Substring(parsed.Statements[1].BodySpan.Start,
            parsed.Statements[1].BodySpan.Length), Is.EqualTo("{ }"));

        Reject(333, 1);
        Reject(338, 3);
        Reject(343, 2);
        Reject(339, 0);
        Reject(341, end - inner);
        Reject(345, otherwise + 1);
        Reject(0, version == 32 ? 36 : 32);

        void Reject(int index, int changed)
        {
            var original = words[index];
            words[index] = changed;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Encode(words)));
            words[index] = original;
        }
    }

    [Test]
    public void ReaderRequiresSourceBoundBranchArmsAndSelectedFeatures()
    {
        const string text = "public int32 Value() { if (true) { } else { } return 42; }";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        var branch = text.IndexOf("if (", StringComparison.Ordinal);
        var otherwise = text.IndexOf("else", StringComparison.Ordinal);
        var number = text.IndexOf("42", StringComparison.Ordinal);
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = 2;
        words[38] = -1;
        words[39] = 1;
        words[40] = -1;
        words[298] = 2;
        Node(0, 20, 5, text.IndexOf("true", StringComparison.Ordinal), 4, -1, -1);
        Node(1, 3, 1, number, 2, -1, -1, 42);
        Statement(0, 4, 1, 0, branch, text.IndexOf("} return", StringComparison.Ordinal) + 1);
        Statement(1, 5, 2, -1, otherwise, text.IndexOf("} return", StringComparison.Ordinal) + 1);

        var result = ScalarFrontendHost.ReadResult(source, Bytes());
        Assert.That(result.Statements.Length, Is.EqualTo(2));
        Assert.That(result.Statements[0].Kind, Is.EqualTo(4));
        Assert.That(result.Statements[1].Kind, Is.EqualTo(5));
        Assert.That(text.Substring(result.Statements[0].BodySpan.Start,
            result.Statements[0].BodySpan.Length), Is.EqualTo("{ }"));
        Assert.That(text.Substring(result.Statements[1].BodySpan.Start,
            result.Statements[1].BodySpan.Length), Is.EqualTo("{ }"));

        Reject(0, 28);
        Reject(0, 33);
        Reject(0, 34);
        Reject(0, 36);
        Reject(298, 1);
        Reject(333, 2);
        Reject(338, 1);
        Reject(339, 0);
        Reject(339, 1);
        Reject(334, 1);
        Reject(334, -1);
        Reject(337, 4);
        Reject(340, otherwise + 1);
        Reject(341, words[341] - 1);
        Reject(43, 1);
        Reject(39, 0);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("else", "xxxx", StringComparison.Ordinal)), Bytes()));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("true", "false", StringComparison.Ordinal)), Bytes()));

        void Reject(int index, int changed)
        {
            var original = words[index];
            words[index] = changed;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
            words[index] = original;
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
            return Encode(words);
        }
    }

    private static byte[] Encode(int[] words)
    {
        var bytes = new byte[words.Length * sizeof(int)];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4, 4), words[index]);
        }

        return bytes;
    }
}
