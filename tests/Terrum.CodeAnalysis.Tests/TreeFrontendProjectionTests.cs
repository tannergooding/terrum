// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TreeFrontendProjectionTests
{
    [Fact]
    public void ReadResultRejectsInventedIntegerComparisonContext()
    {
        const string text = "public bool Value() => 1 < 2;";
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 5;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = 3;
        words[38] = -1;
        words[39] = 2;
        words[40] = -1;
        words[41] = 1;
        var left = text.IndexOf('1', StringComparison.Ordinal);
        var right = text.IndexOf('2', StringComparison.Ordinal);
        int[] nodes = [
            3, 1, left, 1, -1, -1, 1, 0,
            3, 1, right, 1, -1, -1, 2, 0,
            67, 5, left, right - left + 1, 0, 1, 0, 0,
        ];
        nodes.CopyTo(words, 42);
        var bytes = new byte[words.Length * sizeof(int)];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4, 4), words[index]);
        }
        Assert.Throws<ArgumentException>(() =>
            ScalarFrontendHost.ReadResult(SourceText.From(text), bytes));
    }

    [Fact]
    public void ReadResultRejectsInvalidTreeReferencesTypesAndSourceAnchors()
    {
        const string text = "public int32 Value(int32 x) => x + (x * 2);";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
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

    [Theory]
    [InlineData(false, 64, true)]
    [InlineData(false, 65, false)]
    [InlineData(true, 64, true)]
    [InlineData(true, 65, false)]
    public void ReadersEnforceThe64NodeLimitForStandaloneAndCompleteProgramLayouts(
        bool completeProgram, int nodeCount, bool valid)
    {
        var source = SourceText.From(ExpressionFunctionText());
        var words = ExpressionFunction(source.Text, completeProgram ? 88 : 87);
        words[37] = nodeCount;

        if (!valid)
        {
            _ = Assert.Throws<ArgumentException>(() => ReadProjection(source, words, completeProgram));
            return;
        }

        var result = ReadProjection(source, words, completeProgram);
        Assert.True(result.IsValid);
        Assert.Equal(64, result.Nodes.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadersRejectOutOfRangeNodeReferencesWithoutReturningAProjection(bool completeProgram)
    {
        var source = SourceText.From(ExpressionFunctionText());
        var words = ExpressionFunction(source.Text, completeProgram ? 88 : 87);
        words[NodeWordOffset(63) + 5] = 64;

        _ = Assert.Throws<ArgumentException>(() => ReadProjection(source, words, completeProgram));
    }

    private static ScalarFrontendResult ReadProjection(SourceText source, int[] words,
        bool completeProgram)
    {
        if (!completeProgram)
        {
            return ScalarFrontendHost.ReadResult(source, Bytes(words));
        }

        var program = new int[ScalarFrontendHost.ProgramWordCount];
        program[0] = 88;
        program[1] = 1;
        program[2] = 1;
        var tokens = Lexer.Lex(source).Tokens;
        var end = 0;
        while (tokens[end++].Kind != TokenKind.Semicolon)
        {
        }
        var declaration = new TextSpan(tokens[0].Span.Start,
            tokens[end - 1].Span.End - tokens[0].Span.Start);
        program[4] = 0;
        program[5] = end;
        program[6] = declaration.Start;
        program[7] = declaration.Length;
        words.AsSpan(0, ScalarFrontendHost.ProgramBodyWordCount).CopyTo(program.AsSpan(8));
        program.AsSpan(8 + ScalarFrontendHost.ProgramBodyWordCount, 64).Fill(-1);
        return ScalarFrontendHost.ReadProgram(source, Bytes(new int[ScalarFrontendHost.WordCount]),
            Bytes(program), BoundRuntimePolicy.FailFast).Projection;
    }

    private static string ExpressionFunctionText()
    {
        var terms = new string[32];
        Array.Fill(terms, "x");
        terms[0] = "(x)";
        return $"int32 Value(int32 x) => {string.Join(" + ", terms)};";
    }

    private static int[] ExpressionFunction(string text, int version)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = version;
        words[1] = 1;
        words[5] = 1;
        words[6] = 1;
        words[7] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = "Value".Length;
        words[11] = text.IndexOf('x', StringComparison.Ordinal);
        words[12] = 1;
        words[37] = 64;
        words[38] = -1;
        words[39] = 63;
        words[40] = -1;
        words[41] = 1;

        var expressionStart = text.IndexOf("(x)", StringComparison.Ordinal);
        var node = 0;
        WriteNode(0, expressionStart + 1, 1, 1, -1);
        var root = node - 1;
        WriteNode(46, expressionStart, 3, root, -1);
        root = node - 1;
        var next = expressionStart + 3;
        for (var term = 1; term < 32; term++)
        {
            var x = text.IndexOf('x', next, StringComparison.Ordinal);
            WriteNode(0, x, 1, 1, -1);
            var leaf = node - 1;
            WriteNode(57, expressionStart, x + 1 - expressionStart, root, leaf);
            root = node - 1;
            next = x + 1;
        }
        Assert.Equal(64, node);
        Assert.Equal(63, root);
        return words;

        void WriteNode(int operation, int start, int length, int left, int right)
        {
            var offset = NodeWordOffset(node++);
            words[offset] = operation;
            words[offset + 1] = 1;
            words[offset + 2] = start;
            words[offset + 3] = length;
            words[offset + 4] = left;
            words[offset + 5] = right;
        }
    }

    private static int NodeWordOffset(int node) => node < 32
        ? 42 + (node * 8)
        : 392 + ((node - 32) * 8);

    private static byte[] Bytes(int[] words)
    {
        var bytes = new byte[words.Length * sizeof(int)];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), words[index]);
        }
        return bytes;
    }
}
