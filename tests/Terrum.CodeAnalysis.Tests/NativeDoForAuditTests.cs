// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeDoForAuditTests
{
    [Theory]
    [InlineData("")]
    [InlineData("true")]
    [InlineData("false")]
    public void IndependentForWithoutReturnRequiresProvenNonFallthrough(string condition)
    {
        var text = $"public int32 Value() {{ for (;{condition};) {{ }} }}";
        var fixture = new Projection(text, condition.Length == 0 ? 0 : 1, 3);
        var loop = text.IndexOf("for", StringComparison.Ordinal);
        var first = text.IndexOf(';', loop);
        var close = text.IndexOf(')', first);
        fixture.Statement(0, 12, 3, condition.Length == 0 ? -1 : 0, loop, text.IndexOf('}', close) + 1);
        fixture.Statement(1, 13, 0, -1, first, first + 1);
        fixture.Statement(2, 13, 0, -1, close, close + 1);
        if (condition.Length != 0)
        {
            fixture.Node(0, condition == "true" ? 20 : 21, 5, first + 1, condition.Length, -1, 0);
        }
        if (condition == "false")
        {
            Assert.Throws<ArgumentException>(() => fixture.Bind());
            return;
        }
        fixture.Bind();
        fixture.Reject(0, 50);
        fixture.Reject(39, 0);
        fixture.Reject(333, 2);
        fixture.Reject(338, 1);
        fixture.Reject(340, first + 1);
        fixture.Reject(344, 0);
        fixture.Reject(345, close - 1);
        fixture.Reject(391, 1);
    }

    [Fact]
    public void IndependentInitializedForClauseHasExactScopeAndRole()
    {
        const string text = "public int32 Value() { for (int32 x = 42; true;) { return x; } }";
        var fixture = new Projection(text, 3, 4);
        var loop = text.IndexOf("for", StringComparison.Ordinal);
        var declaration = text.IndexOf("int32 x", StringComparison.Ordinal);
        var delimiter = text.IndexOf(';', declaration);
        var condition = text.IndexOf("true", StringComparison.Ordinal);
        var close = text.IndexOf(')', condition);
        var result = text.IndexOf("return", StringComparison.Ordinal);
        fixture.Words[299] = 1;
        fixture.Words[300] = 1;
        fixture.Words[302] = declaration + 6;
        fixture.Words[303] = 1;
        fixture.Node(0, 3, 1, delimiter - 2, 2, -1, 42);
        fixture.Node(1, 20, 5, condition, 4, -1, 0);
        fixture.Node(2, 0, 1, result + 7, 1, 1, 0);
        fixture.Statement(0, 12, 4, 1, loop, text.IndexOf('}', result) + 1);
        fixture.Statement(1, 1, 1, 0, declaration, delimiter);
        fixture.Statement(2, 13, 0, -1, close, close + 1);
        fixture.Statement(3, 8, 0, 2, result, result + 9);
        fixture.Bind();
        fixture.Reject(337, 2);
        fixture.Reject(338, 2);
        fixture.Reject(339, 1);
        fixture.Reject(341, delimiter - declaration + 1);
        fixture.Reject(342, 1);
        fixture.Reject(301, 1);
        fixture.Reject(62, 2);
        fixture.Reject(0, 50);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentDoConditionCannotReadBodyLocal(bool escapedRead)
    {
        var condition = escapedRead ? "x" : "false";
        var text = $"public int32 Value() {{ do {{ bool x = false; }} while ({condition}); return 42; }}";
        var fixture = new Projection(text, 3, 3);
        var loop = text.IndexOf("do", StringComparison.Ordinal);
        var declaration = text.IndexOf("bool x", StringComparison.Ordinal);
        var initial = text.IndexOf("false", declaration, StringComparison.Ordinal);
        var predicate = text.IndexOf("while", StringComparison.Ordinal) + 7;
        var result = text.IndexOf("return", StringComparison.Ordinal);
        fixture.Words[299] = 1;
        fixture.Words[300] = 5;
        fixture.Words[302] = declaration + 5;
        fixture.Words[303] = 1;
        fixture.Node(0, 21, 5, initial, 5, -1, 0);
        fixture.Node(1, escapedRead ? 0 : 21, 5, predicate, condition.Length, escapedRead ? 1 : -1, 0);
        fixture.Node(2, 3, 1, result + 7, 2, -1, 42);
        fixture.Statement(0, 11, 2, 1, loop, result - 1);
        fixture.Statement(1, 1, 1, 0, declaration, initial + 6);
        fixture.Statement(2, 8, 0, 2, result, result + 10);
        if (escapedRead)
        {
            Assert.Throws<ArgumentException>(() => fixture.Bind());
            return;
        }
        fixture.Bind();
        fixture.Reject(334, -1);
        fixture.Reject(0, 50);
    }

    [Fact]
    public void IndependentForTransferCannotTargetAClauseRecord()
    {
        const string text = "public int32 Value() { for (; true;) { break; } return 42; }";
        var fixture = new Projection(text, 2, 5);
        var loop = text.IndexOf("for", StringComparison.Ordinal);
        var first = text.IndexOf(';', loop);
        var condition = text.IndexOf("true", StringComparison.Ordinal);
        var close = text.IndexOf(')', condition);
        var transfer = text.IndexOf("break", StringComparison.Ordinal);
        var result = text.IndexOf("return", StringComparison.Ordinal);
        fixture.Node(0, 20, 5, condition, 4, -1, 0);
        fixture.Node(1, 3, 1, result + 7, 2, -1, 42);
        fixture.Statement(0, 12, 4, 0, loop, text.IndexOf('}', transfer) + 1);
        fixture.Statement(1, 13, 0, -1, first, first + 1);
        fixture.Statement(2, 13, 0, -1, close, close + 1);
        fixture.Statement(3, 6, 0, -1, transfer, transfer + 6);
        fixture.Statement(4, 8, 0, 1, result, result + 10);
        fixture.Bind();
        fixture.Reject(348, 1);
        fixture.Reject(348, 2);
        fixture.Reject(349, 0);
    }

    private sealed class Projection
    {
        private readonly SourceText _source;
        internal readonly int[] Words = new int[ScalarFrontendHost.WordCount];

        internal Projection(string text, int nodes, int statements)
        {
            _source = SourceText.From(text);
            Words[0] = 85;
            Words[1] = Words[2] = Words[5] = 1;
            Words[9] = text.IndexOf("Value", StringComparison.Ordinal);
            Words[10] = 5;
            Words[37] = nodes;
            Words[38] = Words[39] = Words[40] = -1;
            Words[298] = statements;
        }

        internal void Bind()
        {
            foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
            {
                Assert.Single(ScalarFrontendHost.BindLibrary(_source, Bytes(), policy).Functions);
            }
        }

        internal void Reject(int index, int value)
        {
            var previous = Words[index];
            Words[index] = value;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(_source, Bytes()));
            Words[index] = previous;
        }

        internal void Node(int index, int operation, int type, int start, int length, int left, int low)
        {
            var offset = 42 + index * 8;
            Words[offset] = operation;
            Words[offset + 1] = type;
            Words[offset + 2] = start;
            Words[offset + 3] = length;
            Words[offset + 4] = left;
            Words[offset + 5] = -1;
            Words[offset + 6] = low;
        }

        internal void Statement(int index, int kind, int slot, int root, int start, int end)
        {
            var offset = 332 + index * 5;
            Words[offset] = kind;
            Words[offset + 1] = slot;
            Words[offset + 2] = root;
            Words[offset + 3] = start;
            Words[offset + 4] = end - start;
        }

        private byte[] Bytes()
        {
            var bytes = new byte[Words.Length * sizeof(int)];
            for (var index = 0; index < Words.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), Words[index]);
            }
            return bytes;
        }
    }
}
