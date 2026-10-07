// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class MutationFrontendProjectionTests
{
    [Fact]
    public void ReaderRequiresInt32ShiftCountAndExactExpressionBodySource()
    {
        const string text = "public int64 Shift(int64 value, int32 count) => value >>> count;";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 85;
        words[1] = 1;
        words[2] = 1;
        words[5] = 3;
        words[6] = 2;
        words[7] = 3;
        words[8] = 1;
        words[9] = text.IndexOf("Shift", StringComparison.Ordinal);
        words[10] = 5;
        words[11] = text.IndexOf("value", StringComparison.Ordinal);
        words[12] = 5;
        words[13] = text.IndexOf("count", StringComparison.Ordinal);
        words[14] = 5;
        words[37] = 3;
        words[38] = -1;
        words[39] = 2;
        words[40] = -1;
        words[41] = 1;
        var value = text.LastIndexOf("value", StringComparison.Ordinal);
        var count = text.LastIndexOf("count", StringComparison.Ordinal);
        Node(0, 0, 3, value, 5, 1, -1);
        Node(1, 0, 1, count, 5, 2, -1);
        Node(2, 90, 3, value, count + 5 - value, 0, 1);
        Assert.Equal(3, ScalarFrontendHost.ReadResult(source, Bytes()).Nodes.Length);
        Reject(0, 48);
        Reject(37, 33);
        Reject(42 + 1 * 8 + 1, 3);
        Reject(42 + 2 * 8 + 1, 1);
        Reject(42 + 2 * 8, 91);
        Reject(42 + 2 * 8 + 5, 0);
        Reject(41, 0);
        Reject(299, 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace(">>>", ">>", StringComparison.Ordinal)), Bytes()));

        void Reject(int index, int replacement)
        {
            var original = words[index];
            words[index] = replacement;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
            words[index] = original;
        }

        void Node(int index, int operation, int type, int start, int length, int left, int right)
        {
            var offset = 42 + index * 8;
            words[offset] = operation;
            words[offset + 1] = type;
            words[offset + 2] = start;
            words[offset + 3] = length;
            words[offset + 4] = left;
            words[offset + 5] = right;
        }

        byte[] Bytes()
        {
            var bytes = new byte[words.Length * sizeof(int)];
            for (var index = 0; index < words.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    bytes.AsSpan(index * sizeof(int), sizeof(int)), words[index]);
            }
            return bytes;
        }
    }

    [Fact]
    public void ReaderRejectsForgedMutationSourceAndOlderProfiles()
    {
        const string text = "public int32 Value() { mut int32 x = 4; x += 2; return x++; }";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 85;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = 6;
        words[38] = -1;
        words[39] = -1;
        words[40] = -1;
        words[298] = 3;
        words[299] = 1;
        words[300] = 1;
        words[301] = 1;
        words[302] = text.IndexOf("x =", StringComparison.Ordinal);
        words[303] = 1;
        var declaration = text.IndexOf("mut int32", StringComparison.Ordinal);
        var compound = text.IndexOf("x +=", StringComparison.Ordinal);
        var result = text.IndexOf("return", StringComparison.Ordinal);
        Node(0, 3, declaration + 14, 1, -1, -1, 4);
        Node(1, 3, compound + 5, 1, -1, -1, 2);
        Node(2, 0, compound, 1, 1, -1, 0);
        Node(3, 91, compound, 6, 2, 1, 0);
        Node(4, 0, result + 7, 1, 1, -1, 0);
        Node(5, 86, result + 7, 3, 4, -1, 0);
        Statement(0, 1, 1, 0, declaration, compound - 1);
        Statement(1, 9, 1, 3, compound, result - 1);
        Statement(2, 8, 0, 5, result, text.LastIndexOf(';') + 1);

        var accepted = ScalarFrontendHost.ReadResult(source, Bytes());
        Assert.Equal(3, accepted.Statements.Length);
        var program = ScalarFrontendHost.BindLibrary(source, Bytes(), BoundRuntimePolicy.FailFast);
        var statements = program.Functions[0].Body.Statements;
        Assert.IsType<BoundCompoundAssignment>(
            Assert.IsType<BoundExpressionStatement>(statements[1]).Expression);
        var update = Assert.IsType<BoundUpdate>(
            Assert.IsType<BoundReturn>(statements[2]).Value);
        Assert.False(update.IsPrefix);
        var producer = ModuleCompiler.CreateProducer(program, ["Value"]);
        Assert.Equal(73,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var client = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Value(); }"),
            ModuleCompiler.CreateReference(producer));
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program),
            producer);
        Assert.Equal(6, NativeJit.Execute(ModuleCompiler.Link(consumer, producer),
            NativeRuntimePolicy.FailFast));

        Reject(0, 48);
        Reject(0, 47);
        Reject(37, 5);
        Reject(37, 33);
        Reject(298, 13);
        Reject(299, 9);
        Reject(301, 0);
        Reject(300, 5);
        Reject(332 + 5 + 1, 2);
        Reject(332 + 5 + 2, 1);
        Reject(42 + 3 * 8, 92);
        Reject(42 + 3 * 8 + 1, 2);
        Reject(42 + 3 * 8 + 4, 3);
        Reject(42 + 3 * 8 + 5, 4);
        Reject(42 + 3 * 8 + 2, compound + 1);
        Reject(42 + 5 * 8, 87);
        Reject(42 + 5 * 8 + 4, 5);
        Reject(42 + 5 * 8 + 6, 2);
        Reject(42 + 5 * 8 + 7, 1);
        Reject(41, 1);
        Reject(38, 0);
        Reject(300 + 4, 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("x += 2", "x -= 2", StringComparison.Ordinal)), Bytes()));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()[..^1]));

        void Reject(int index, int replacement)
        {
            var original = words[index];
            words[index] = replacement;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
            words[index] = original;
        }

        void Node(int index, int operation, int start, int length, int left, int right, int low)
        {
            var offset = 42 + index * 8;
            words[offset] = operation;
            words[offset + 1] = 1;
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
                BinaryPrimitives.WriteInt32LittleEndian(
                    bytes.AsSpan(index * sizeof(int), sizeof(int)), words[index]);
            }
            return bytes;
        }
    }
}
