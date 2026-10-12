// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ConversionFrontendProjectionTests
{
    [Theory]
    [InlineData(7, "0", 0)]
    [InlineData(7, "55296", 55296)]
    [InlineData(7, "0xFFFF", 65535)]
    [InlineData(8, "0", 0)]
    [InlineData(8, "55296", 55296)]
    [InlineData(8, "0xFFFF", 65535)]
    public void ReaderValidatesChar16FloatLiteralSourceAndPayload(
        int type, string literal, int codeUnit)
    {
        var typeName = type == 7 ? "float32" : "float64";
        var expression = $"{typeName}(char16({literal}))";
        var text = $"public {typeName} Value() => {expression};";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = type;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = 1;
        words[38] = -1;
        words[39] = 0;
        words[40] = -1;
        words[41] = 1;
        var bits = type == 7
            ? BitConverter.SingleToUInt32Bits((float)codeUnit)
            : BitConverter.DoubleToUInt64Bits((double)codeUnit);
        var node = 42;
        words[node] = 3;
        words[node + 1] = type;
        words[node + 2] = text.IndexOf(expression, StringComparison.Ordinal);
        words[node + 3] = expression.Length;
        words[node + 4] = -1;
        words[node + 5] = -1;
        words[node + 6] = unchecked((int)(uint)bits);
        words[node + 7] = type == 8 ? unchecked((int)(uint)(bits >> 32)) : 0;

        Assert.True(ScalarFrontendHost.ReadResult(source, Encode(words)).IsValid);

        words[node + 1] = type == 7 ? 8 : 7;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Encode(words)));
        words[node + 1] = type;

        words[node + 6] ^= 1;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Encode(words)));
        words[node + 6] ^= 1;

        var changedLiteral = codeUnit switch {
            0 => "1",
            55296 => "55297",
            _ => "0xFFFE",
        };
        var changedSource = SourceText.From(text.Replace($"char16({literal})",
            $"char16({changedLiteral})", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() =>
            ScalarFrontendHost.ReadResult(changedSource, Encode(words)));

        var changedType = SourceText.From(text.Replace("char16(", "uint32(",
            StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() =>
            ScalarFrontendHost.ReadResult(changedType, Encode(words)));
    }

    [Theory]
    [InlineData(7, "0", 0)]
    [InlineData(7, "55296", 55296)]
    [InlineData(7, "0xFFFF", 65535)]
    [InlineData(8, "0", 0)]
    [InlineData(8, "55296", 55296)]
    [InlineData(8, "0xFFFF", 65535)]
    public void CompleteProgramReaderValidatesChar16FloatLiteralSourceAndPayload(
        int type, string literal, int codeUnit)
    {
        var typeName = type == 7 ? "float32" : "float64";
        var expression = $"{typeName}(char16({literal}))";
        var text = $"public {typeName} Value() => {expression};";
        var source = SourceText.From(text);
        var tokens = Lexer.Lex(source).Tokens;
        var program = new int[ScalarFrontendHost.ProgramWordCount];
        program[0] = 88;
        program[1] = 1;
        program[2] = 1;
        var tokenEnd = 0;
        while (tokens[tokenEnd++].Kind != TokenKind.Semicolon)
        {
        }
        var span = new TextSpan(tokens[0].Span.Start,
            tokens[tokenEnd - 1].Span.End - tokens[0].Span.Start);
        const int frame = 4;
        program[frame] = 0;
        program[frame + 1] = tokenEnd;
        program[frame + 2] = span.Start;
        program[frame + 3] = span.Length;

        var payload = new int[ScalarFrontendHost.ProgramBodyWordCount];
        payload[0] = 88;
        payload[1] = 1;
        payload[2] = 1;
        payload[5] = type;
        payload[9] = text.IndexOf("Value", StringComparison.Ordinal);
        payload[10] = 5;
        payload[37] = 1;
        payload[38] = -1;
        payload[39] = 0;
        payload[40] = -1;
        payload[41] = 1;
        var bits = type == 7
            ? BitConverter.SingleToUInt32Bits((float)codeUnit)
            : BitConverter.DoubleToUInt64Bits((double)codeUnit);
        const int node = 42;
        payload[node] = 3;
        payload[node + 1] = type;
        payload[node + 2] = text.IndexOf(expression, StringComparison.Ordinal);
        payload[node + 3] = expression.Length;
        payload[node + 4] = -1;
        payload[node + 5] = -1;
        payload[node + 6] = unchecked((int)(uint)bits);
        payload[node + 7] = type == 8 ? unchecked((int)(uint)(bits >> 32)) : 0;
        payload.CopyTo(program, frame + 4);
        program.AsSpan(frame + 4 + ScalarFrontendHost.ProgramBodyWordCount, 64).Fill(-1);

        var bytes = Encode(new int[ScalarFrontendHost.WordCount]);
        Assert.True(ScalarFrontendHost.ReadProgram(source, bytes, Encode(program),
            BoundRuntimePolicy.FailFast).Projection.IsValid);

        program[frame + 4 + node + 1] = type == 7 ? 8 : 7;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source, bytes,
            Encode(program), BoundRuntimePolicy.FailFast));
        program[frame + 4 + node + 1] = type;

        program[frame + 4 + node + 6] ^= 1;
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source, bytes,
            Encode(program), BoundRuntimePolicy.FailFast));
        program[frame + 4 + node + 6] ^= 1;

        if (type == 8)
        {
            program[frame + 4 + node + 7] ^= 1;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source, bytes,
                Encode(program), BoundRuntimePolicy.FailFast));
            program[frame + 4 + node + 7] ^= 1;
        }
        else
        {
            program[frame + 4 + node + 7] = 1;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source, bytes,
                Encode(program), BoundRuntimePolicy.FailFast));
            program[frame + 4 + node + 7] = 0;
        }

        var changedLiteral = codeUnit switch {
            0 => "1",
            55296 => "55297",
            _ => "0xFFFE",
        };
        var changedLiteralSource = SourceText.From(text.Replace($"char16({literal})",
            $"char16({changedLiteral})", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(changedLiteralSource,
            bytes, Encode(program), BoundRuntimePolicy.FailFast));

        var changedType = SourceText.From(text.Replace("char16(", "uint32(",
            StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(changedType,
            bytes, Encode(program), BoundRuntimePolicy.FailFast));
    }

    [Fact]
    public void ReaderChecksConversionVersionsTypesAndSourceAnchors()
    {
        const string text = "public int32 Main() => int32(uint64(42));";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[9] = text.IndexOf("Main", StringComparison.Ordinal);
        words[10] = 4;
        words[37] = 3;
        words[38] = -1;
        words[39] = 2;
        words[40] = -1;
        words[41] = 1;
        var outer = text.IndexOf("int32(", StringComparison.Ordinal);
        var inner = text.IndexOf("uint64(", StringComparison.Ordinal);
        var literal = text.IndexOf("42", StringComparison.Ordinal);
        Node(0, 3, 4, literal, literal + 2, -1, -1, 42);
        Node(1, 83, 4, inner, literal + 3, 0, -1);
        Node(2, 83, 1, outer, literal + 4, 1, -1);

        Assert.Equal(3, ScalarFrontendHost.ReadResult(source, Bytes()).Nodes.Length);
        Reject(0, 20);
        Reject(0, 21);
        Reject(0, 23);
        Reject(43, 1);
        Reject(42 + 8, 5);
        Reject(42 + 8 + 1, 5);
        Reject(42 + 8 + 1, 1);
        Reject(42 + 8 + 2, inner + 1);
        Reject(42 + 8 + 4, 1);
        Reject(42 + 16 + 1, 4);
        Reject(42 + 16 + 5, 0);
        Reject(42 + 16 + 6, 1);
        Reject(42 + 3 * 8, 1);
        Reject(298, 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("uint64(", "int64(", StringComparison.Ordinal)), Bytes()));

        void Reject(int index, int value)
        {
            var original = words[index];
            words[index] = value;
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
    public void ReaderChecksConversionInOrderedLocalStatements()
    {
        const string text = "public int32 Main() { mut uint64 x = uint64(42); x = uint64(x); return int32(x); }";
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[9] = text.IndexOf("Main", StringComparison.Ordinal);
        words[10] = 4;
        words[37] = 6;
        words[38] = -1;
        words[39] = 5;
        words[40] = -1;
        words[298] = 2;
        words[299] = 1;
        words[300] = 4;
        words[301] = 1;
        words[302] = text.IndexOf("x =", StringComparison.Ordinal);
        words[303] = 1;
        var literal = text.IndexOf("42", StringComparison.Ordinal);
        var firstCast = text.IndexOf("uint64(42)", StringComparison.Ordinal);
        var secondCast = text.IndexOf("uint64(x)", StringComparison.Ordinal);
        var lastCast = text.IndexOf("int32(x)", StringComparison.Ordinal);
        Node(0, 3, 4, literal, literal + 2, -1, -1, 42);
        Node(1, 83, 4, firstCast, literal + 3, 0, -1);
        Node(2, 0, 4, secondCast + 7, secondCast + 8, 1, -1);
        Node(3, 83, 4, secondCast, secondCast + 9, 2, -1);
        Node(4, 0, 4, lastCast + 6, lastCast + 7, 1, -1);
        Node(5, 83, 1, lastCast, lastCast + 8, 4, -1);
        var declaration = text.IndexOf("mut uint64", StringComparison.Ordinal);
        Statement(0, 1, 1, 1, declaration, firstCast + 11);
        var assignment = text.LastIndexOf("x = uint64", StringComparison.Ordinal);
        Statement(1, 2, 1, 3, assignment, secondCast + 10);

        var source = SourceText.From(text);
        Assert.Equal(2, ScalarFrontendHost.ReadResult(source, Bytes()).Statements.Length);
        Reject(0, 21);
        Reject(0, 22);
        Reject(301, 0);
        Reject(42 + 3 * 8 + 2, secondCast + 1);
        Reject(337 + 2, 5);
        Reject(342, 1);

        void Reject(int index, int value)
        {
            var original = words[index];
            words[index] = value;
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

    [Fact]
    public void ReaderRejectsTypeCallWhenParameterShadowsItsSpelling()
    {
        const string text = "public int32 Value(int32 int32) => int32(int32);";
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[6] = 1;
        words[7] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[11] = text.IndexOf("int32)", StringComparison.Ordinal);
        words[12] = 5;
        words[37] = 2;
        words[38] = -1;
        words[39] = 1;
        words[40] = -1;
        words[41] = 1;
        var call = text.IndexOf("int32(", words[11] + 5, StringComparison.Ordinal);
        var argument = call + 6;
        words[42] = 0;
        words[43] = 1;
        words[44] = argument;
        words[45] = 5;
        words[46] = 1;
        words[47] = -1;
        words[50] = 83;
        words[51] = 1;
        words[52] = call;
        words[53] = 12;
        words[54] = 0;
        words[55] = -1;
        var bytes = new byte[words.Length * sizeof(int)];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4, 4), words[index]);
        }
        Assert.Throws<ArgumentException>(() =>
            ScalarFrontendHost.ReadResult(SourceText.From(text), bytes));
    }

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
