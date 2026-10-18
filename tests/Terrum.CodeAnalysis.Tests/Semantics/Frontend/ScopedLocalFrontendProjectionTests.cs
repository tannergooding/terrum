// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics.Frontend;

public sealed class ScopedLocalFrontendProjectionTests
{
    [Test]
    public void IndependentExpiredIdentityCannotAuthorizeReadsOrUpdatesOfReusedSpelling()
    {
        const string text = "public int32 Value() { if (false) { mut int32 x = 1; } mut int32 x = 41; x++; return x; }";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = words[2] = words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = 6;
        words[38] = words[39] = words[40] = -1;
        words[298] = 5;
        words[299] = 2;
        var branch = text.IndexOf("if (", StringComparison.Ordinal);
        var first = text.IndexOf("mut int32", StringComparison.Ordinal);
        var second = text.IndexOf("mut int32", first + 1, StringComparison.Ordinal);
        var update = text.IndexOf("x++", StringComparison.Ordinal);
        var result = text.IndexOf("return", StringComparison.Ordinal);
        words[300] = words[301] = words[303] = 1;
        words[304] = words[305] = words[307] = 1;
        words[302] = first + 10;
        words[306] = second + 10;
        Node(0, 21, 5, branch + 4, 5, -1, 0);
        Node(1, 3, 1, first + 14, 1, -1, 1);
        Node(2, 3, 1, second + 14, 2, -1, 41);
        Node(3, 0, 1, update, 1, 2, 0);
        Node(4, 86, 1, update, 3, 3, 0);
        Node(5, 0, 1, result + 7, 1, 2, 0);
        Statement(0, 4, 2, 0, branch, second - 1);
        Statement(1, 1, 1, 1, first, first + 16);
        Statement(2, 1, 2, 2, second, second + 17);
        Statement(3, 10, 0, 4, update, update + 4);
        Statement(4, 8, 0, 5, result, result + 9);

        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            Assert.That(ScalarFrontendHost.BindLibrary(source, Bytes(), policy).Functions, Has.Exactly(1).Items);
        }
        Reject(70, 1);
        Reject(86, 1);
        Reject(343, 1);
        Reject(305, 0);
        Reject(306, first + 10);
        Reject(299, 9);
        Reject(0, 49);

        void Reject(int index, int value)
        {
            var saved = words[index];
            words[index] = value;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
            words[index] = saved;
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
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), words[index]);
            }
            return bytes;
        }
    }

    [Test]
    public void ReaderRejectsObsoleteScopedLocalTagForOrdinaryReturn()
    {
        const string text = "public int32 Value() { return 42; }";
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = 1;
        words[38] = words[39] = words[40] = -1;
        words[42] = 3;
        words[43] = 1;
        words[44] = text.IndexOf("42", StringComparison.Ordinal);
        words[45] = 2;
        words[46] = words[47] = -1;
        words[48] = 42;
        words[298] = 1;
        words[332] = 8;
        words[334] = 0;
        words[335] = text.IndexOf("return", StringComparison.Ordinal);
        words[336] = 10;
        var bytes = new byte[words.Length * sizeof(int)];
        void Encode()
        {
            for (var i = 0; i < words.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * sizeof(int), sizeof(int)), words[i]);
            }
        }
        var source = SourceText.From(text);
        Encode();
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).IsValid, Is.True);
        words[0] = 50;
        Encode();
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
    }

    [Test]
    public void IndependentSiblingSlotsExpireAcrossArms()
    {
        const string text = "public int32 Value(bool flag) { if (flag) { int32 x = 1; } else { int32 x = 2; return x; } return 0; }";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[6] = 1;
        words[7] = 5;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[11] = text.IndexOf("flag", StringComparison.Ordinal);
        words[12] = 4;
        words[37] = 5;
        words[38] = -1;
        words[39] = -1;
        words[40] = -1;
        words[298] = 6;
        words[299] = 2;
        var branch = text.IndexOf("if (", StringComparison.Ordinal);
        var first = text.IndexOf("int32 x = 1", StringComparison.Ordinal);
        var otherwise = text.IndexOf("else", StringComparison.Ordinal);
        var second = text.IndexOf("int32 x = 2", StringComparison.Ordinal);
        var innerReturn = text.IndexOf("return x", StringComparison.Ordinal);
        var outerReturn = text.IndexOf("return 0", StringComparison.Ordinal);
        words[300] = words[304] = 1;
        words[302] = first + 6;
        words[306] = second + 6;
        words[303] = words[307] = 1;
        Node(0, 0, 5, text.IndexOf("flag", branch, StringComparison.Ordinal), 4, 1, 0);
        Node(1, 3, 1, first + 10, 1, -1, 1);
        Node(2, 3, 1, second + 10, 1, -1, 2);
        Node(3, 0, 1, innerReturn + 7, 1, 3, 0);
        Node(4, 3, 1, outerReturn + 7, 1, -1, 0);
        var branchEnd = innerReturn + 11;
        Statement(0, 4, 2, 0, branch, branchEnd);
        Statement(1, 1, 2, 1, first, first + 12);
        Statement(2, 5, 5, -1, otherwise, branchEnd);
        Statement(3, 1, 3, 2, second, second + 12);
        Statement(4, 8, 0, 3, innerReturn, innerReturn + 9);
        Statement(5, 8, 0, 4, outerReturn, outerReturn + 9);

        Assert.That(ScalarFrontendHost.ReadResult(source, Bytes()).Locals.Length, Is.EqualTo(2));
        Reject(0, 49);
        Reject(70, 2);
        Reject(341, 1);
        Reject(344, otherwise + 1);
        Reject(349, 1);
        Reject(306, first + 6);
        Reject(355, 2);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("return 0;", "return x;", StringComparison.Ordinal)), Bytes()));

        void Reject(int index, int replacement)
        {
            var old = words[index];
            words[index] = replacement;
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
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int), sizeof(int)),
                    words[index]);
            }
            return bytes;
        }
    }

    [Test]
    public void IndependentScopedDeclarationRequiresVersionAndExactSourceAuthority()
    {
        const string text = "public int32 Value() { if (true) { int32 x = 42; return x; } return 0; }";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = 4;
        words[38] = -1;
        words[39] = -1;
        words[40] = -1;
        words[298] = 4;
        words[299] = 1;
        var declaration = text.IndexOf("int32 x", StringComparison.Ordinal);
        var firstReturn = text.IndexOf("return x", StringComparison.Ordinal);
        var lastReturn = text.IndexOf("return 0", StringComparison.Ordinal);
        words[300] = 1;
        words[302] = declaration + 6;
        words[303] = 1;
        Node(0, 20, 5, text.IndexOf("true", StringComparison.Ordinal), 4, -1, 0);
        Node(1, 3, 1, declaration + 10, 2, -1, 42);
        Node(2, 0, 1, firstReturn + 7, 1, 1, 0);
        Node(3, 3, 1, lastReturn + 7, 1, -1, 0);
        var branch = text.IndexOf("if (", StringComparison.Ordinal);
        Statement(0, 4, 3, 0, branch, firstReturn + 11);
        Statement(1, 1, 1, 1, declaration, declaration + 13);
        Statement(2, 8, 0, 2, firstReturn, firstReturn + 9);
        Statement(3, 8, 0, 3, lastReturn, lastReturn + 9);

        var result = ScalarFrontendHost.ReadResult(source, Bytes());
        Assert.That(result.Locals, Has.Exactly(1).Items);
        Assert.That(result.Statements.Length, Is.EqualTo(4));
        Assert.That(ScalarFrontendHost.BindLibrary(source, Bytes(),
            BoundRuntimePolicy.CheckedExceptions).Functions, Has.Exactly(1).Items);
        Reject(0, 49);
        Reject(0, 48);
        Reject(300, 2);
        Reject(301, 1);
        Reject(302, declaration + 1);
        Reject(303, 2);
        Reject(337, 2);
        Reject(338, 2);
        Reject(339, 3);
        Reject(341, 10);
        Reject(342, 2);
        Reject(50, 2);
        Reject(359, lastReturn + 1);
        Reject(391, 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("return x;", "return 42;", StringComparison.Ordinal)), Bytes()));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()[..^1]));

        void Reject(int index, int replacement)
        {
            var old = words[index];
            words[index] = replacement;
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
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int), sizeof(int)),
                    words[index]);
            }
            return bytes;
        }
    }
}
