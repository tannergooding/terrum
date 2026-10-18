// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics.Frontend;

public sealed class TransferFrontendProjectionTests
{
    [Test]
    public void ReaderRequiresNearestLoopAndSourceBoundTransfersAcrossBranches()
    {
        const string text = "public int32 Value() { while (true) { if (true) { break; } else { continue; } } return 42; }";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        var loop = text.IndexOf("while", StringComparison.Ordinal);
        var branch = text.IndexOf("if (true)", StringComparison.Ordinal);
        var exit = text.IndexOf("break;", StringComparison.Ordinal);
        var next = text.IndexOf("continue;", StringComparison.Ordinal);
        var otherwise = text.IndexOf("else", StringComparison.Ordinal);
        var branchEnd = text.IndexOf("} } return", StringComparison.Ordinal) + 1;
        var loopEnd = text.IndexOf("} return", StringComparison.Ordinal) + 1;
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
        words[298] = 5;
        Node(0, 20, 5, text.IndexOf("true", loop, StringComparison.Ordinal), 4, -1, -1);
        Node(1, 20, 5, text.IndexOf("true", branch, StringComparison.Ordinal), 4, -1, -1);
        Node(2, 3, 1, text.IndexOf("42", StringComparison.Ordinal), 2, -1, -1, 42);
        Statement(0, 3, 5, 0, loop, loopEnd);
        Statement(1, 4, 3, 1, branch, branchEnd);
        Statement(2, 6, 0, -1, exit, exit + 6);
        Statement(3, 5, 5, -1, otherwise, branchEnd);
        Statement(4, 7, 0, -1, next, next + 9);

        var result = ScalarFrontendHost.ReadResult(source, Bytes());
        Assert.That(result.Statements.Length, Is.EqualTo(5));
        Assert.That(result.Statements[2].Slot, Is.EqualTo(0));
        Assert.That(result.Statements[4].Slot, Is.EqualTo(0));
        Reject(0, 36);
        Reject(0, 40);
        Reject(0, 45);
        Reject(0, 46);
        Reject(0, 48);
        Reject(298, 4);
        Reject(333, 4);
        Reject(338, 5);
        Reject(343, 4);
        Reject(343, 1);
        Reject(343, -1);
        Reject(344, 0);
        Reject(345, exit + 1);
        Reject(346, 5);
        Reject(353, 1);
        Reject(354, 0);
        Reject(355, next + 1);
        Reject(356, 8);
        Reject(352, 6);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("continue;", "break;   ", StringComparison.Ordinal)), Bytes()));

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
            var bytes = new byte[words.Length * sizeof(int)];
            for (var index = 0; index < words.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4, 4), words[index]);
            }
            return bytes;
        }
    }

    [Test]
    public void ReaderRejectsTransferToOuterLoopAndOutsideLoop()
    {
        const string text = "public int32 Value() { while (true) { while (true) { break; } continue; } return 42; }";
        var words = new int[ScalarFrontendHost.WordCount];
        var outer = text.IndexOf("while", StringComparison.Ordinal);
        var inner = text.IndexOf("while", outer + 1, StringComparison.Ordinal);
        var exit = text.IndexOf("break;", StringComparison.Ordinal);
        var next = text.IndexOf("continue;", StringComparison.Ordinal);
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
        words[298] = 4;
        Node(0, 20, outer);
        Node(1, 20, inner);
        Node(2, 3, text.IndexOf("42", StringComparison.Ordinal));
        Statement(0, 3, 4, 0, outer, text.IndexOf("} return", StringComparison.Ordinal) + 1);
        Statement(1, 3, 3, 1, inner, text.IndexOf("} continue", StringComparison.Ordinal) + 1);
        Statement(2, 6, 1, -1, exit, exit + 6);
        Statement(3, 7, 0, -1, next, next + 9);
        var source = SourceText.From(text);
        Assert.That(ScalarFrontendHost.ReadResult(source, Bytes()).Statements.Length, Is.EqualTo(4));
        Reject(343, 0);
        Reject(348, 1);
        Reject(343, 2);
        Reject(333, 3);
        Reject(0, 28);
        Reject(0, 44);

        const string outside = "public int32 Value() { break; while (true) { break; } return 42; }";
        Array.Clear(words, 0, words.Length);
        var outsideLoop = outside.IndexOf("while", StringComparison.Ordinal);
        var outsideBreak = outside.IndexOf("break;", StringComparison.Ordinal);
        var insideBreak = outside.IndexOf("break;", outsideBreak + 1, StringComparison.Ordinal);
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[9] = outside.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = 2;
        words[38] = -1;
        words[39] = 1;
        words[40] = -1;
        words[298] = 3;
        Node(0, 20, outsideLoop);
        Node(1, 3, outside.IndexOf("42", StringComparison.Ordinal));
        Statement(0, 6, 0, -1, outsideBreak, outsideBreak + 6);
        Statement(1, 3, 3, 0, outsideLoop, outside.IndexOf("} return", StringComparison.Ordinal) + 1);
        Statement(2, 6, 1, -1, insideBreak, insideBreak + 6);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(outside), Bytes()));

        void Reject(int index, int changed)
        {
            var original = words[index];
            words[index] = changed;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
            words[index] = original;
        }

        void Node(int index, int operation, int start)
        {
            var offset = 42 + index * 8;
            words[offset] = operation;
            words[offset + 1] = operation == 20 ? 5 : 1;
            words[offset + 2] = start + (operation == 20 ? 7 : 0);
            words[offset + 3] = operation == 20 ? 4 : 2;
            words[offset + 4] = -1;
            words[offset + 5] = -1;
            if (operation == 3)
            { words[offset + 6] = 42; }
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
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4, 4), words[index]);
            }
            return bytes;
        }
    }
}
