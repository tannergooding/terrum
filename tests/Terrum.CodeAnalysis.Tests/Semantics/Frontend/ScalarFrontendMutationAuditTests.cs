// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics.Frontend;

public sealed class ScalarFrontendMutationAuditTests
{
    [TestCase(false,"<<",88)]
    [TestCase(true,"<<",88)]
    [TestCase(false,">>",89)]
    [TestCase(true,">>",89)]
    [TestCase(false,">>>",90)]
    [TestCase(true,">>>",90)]
    public void ShiftCountCannotProvideTheShiftedValuesInferredType(bool inferred, string operation, int opcode)
    {
        var (source, bytes) = ShiftFixture(inferred, operation, opcode);
        var hosted = Compilation.CreateLibrary(SyntaxTree.Parse(source.Text));
        Assert.That(hosted.HasErrors, Is.EqualTo(inferred));
        if (inferred)
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        }
        else
        {
            Assert.That(ScalarFrontendHost.ReadResult(source, bytes).Nodes.Length, Is.EqualTo(4));
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task IndependentlyProjectedShiftsServiceUnchangedSemanticAndNativeConsumers(NativeRuntimePolicy policy)
    {
        var (source, bytes) = ShiftFixture(false);
        var program = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var producer = ModuleCompiler.CreateProducer(program, ["Value"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4)), Is.EqualTo(76));
        var obsolete = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(obsolete.AsSpan(4), 44);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(obsolete));
        const string client = "int32 Main() { return Value(5) + 10; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(client),
            ModuleCompiler.CreateReference(producer), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), producer);

        var (replacementSource, replacementBytes) = ShiftFixture(false, initial: 2);
        var replacementProgram = ScalarFrontendHost.BindLibrary(
            replacementSource, replacementBytes, (BoundRuntimePolicy)policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["Value"]);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), policy), Is.EqualTo(74));

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, ["Value"], "FrontendShifts", policy);
        var nativeClient = Compilation.Create(SyntaxTree.Parse(client),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), (BoundRuntimePolicy)policy);
        Assert.That(nativeClient.HasErrors, Is.False, string.Join(Environment.NewLine, nativeClient.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = nativeClient.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), [nativeProducer], policy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(
            replacementProgram, ["Value"], "FrontendShifts", policy);
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var apphost = Path.Combine(root, "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        foreach (var (dependency, expected) in new[] { (nativeProducer, 42), (nativeReplacement, 74) })
        {
            NativeModuleCompiler.ValidateGraph(nativeConsumer, [dependency]);
            Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer,
                [dependency], TimeSpan.FromSeconds(10), policy), Is.EqualTo(expected));
            Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer,
                [dependency], apphost, TimeSpan.FromSeconds(30), policy), Is.EqualTo(expected));
        }
    }

    [Test]
    public void IndependentlyGroupedCompoundRetainsItsPlaceAndSourceOrder()
    {
        const string text = "public int32 Value() { mut int32 x = 4; ((x)) += x++; return x; }";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[37] = 8;
        words[38] = -1;
        words[39] = -1;
        words[40] = -1;
        words[298] = 3;
        words[299] = 1;
        words[300] = 1;
        words[301] = 1;
        words[302] = text.IndexOf("x =", StringComparison.Ordinal);
        words[303] = 1;
        var compound = text.IndexOf("((x))", StringComparison.Ordinal);
        var update = text.IndexOf("x++", StringComparison.Ordinal);
        var result = text.IndexOf("return", StringComparison.Ordinal);
        Node(0, 3, text.IndexOf('4'), 1, -1, -1, 4);
        Node(1, 0, compound + 2, 1, 1, -1);
        Node(2, 46, compound + 1, 3, 1, -1);
        Node(3, 46, compound, 5, 2, -1);
        Node(4, 0, update, 1, 1, -1);
        Node(5, 86, update, 3, 4, -1);
        Node(6, 91, compound, update + 3 - compound, 3, 5);
        Node(7, 0, text.LastIndexOf('x'), 1, 1, -1);
        Statement(0, 1, 1, 0, text.IndexOf("mut", StringComparison.Ordinal), text.IndexOf(';') + 1);
        Statement(1, 9, 1, 6, compound, update + 4);
        Statement(2, 8, 0, 7, result, text.LastIndexOf(';') + 1);
        var program = ScalarFrontendHost.BindLibrary(source, Bytes(), BoundRuntimePolicy.FailFast);
        var operation = ((global::System.Func<BoundCompoundAssignment>)(() => { var __nunitActual3 = ((global::System.Func<BoundExpressionStatement>)(() => { var __nunitActual2 = program.Functions[0].Body.Statements[1]; Assert.That(__nunitActual2, Is.TypeOf<BoundExpressionStatement>()); return (BoundExpressionStatement)__nunitActual2!; }))().Expression; Assert.That(__nunitActual3, Is.TypeOf<BoundCompoundAssignment>()); return (BoundCompoundAssignment)__nunitActual3!; }))();
        Assert.That(operation.Place, Is.TypeOf<BoundRead>());
        Assert.That(operation.Right, Is.TypeOf<BoundUpdate>());
        for (var version = 1; version <= 84; version++)
        {
            Reject(0, version);
        }
        Reject(301, 0);
        Reject(338, 2);
        Reject(339, 0);
        Reject(337, 10);
        Reject(42 + 3 * 8, 57);
        Reject(42 + 3 * 8 + 3, 4);
        Reject(42 + 5 * 8 + 6, 1);
        Reject(42 + 6 * 8 + 5, 3);
        Reject(42 + 6 * 8 + 5, 7);

        void Reject(int index, int replacement)
        {
            var original = words[index];
            words[index] = replacement;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes()));
            words[index] = original;
        }

        void Node(int index, int operation, int start, int length, int left, int right, int low = 0)
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

        void Statement(int index, int kind, int slot, int expression, int start, int end)
        {
            var offset = 332 + index * 5;
            words[offset] = kind;
            words[offset + 1] = slot;
            words[offset + 2] = expression;
            words[offset + 3] = start;
            words[offset + 4] = end - start;
        }

        byte[] Bytes()
        {
            var bytes = new byte[words.Length * sizeof(int)];
            for (var i = 0; i < words.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * sizeof(int)), words[i]);
            }
            return bytes;
        }
    }

    private static (SourceText Source, byte[] Bytes) ShiftFixture(
        bool inferred, string operation = "<<", int opcode = 88, int initial = 1)
    {
        var localType = inferred ? "var" : "int32";
        var text = $"public int32 Value(int32 count) {{ {localType} x = {initial} {operation} count; return x; }}";
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[6] = 1;
        words[7] = 1;
        words[9] = text.IndexOf("Value", StringComparison.Ordinal);
        words[10] = 5;
        words[11] = text.IndexOf("count", StringComparison.Ordinal);
        words[12] = 5;
        words[37] = 4;
        words[38] = -1;
        words[39] = 3;
        words[40] = -1;
        words[298] = 1;
        words[299] = 1;
        words[300] = 1;
        words[302] = text.IndexOf("x =", StringComparison.Ordinal);
        words[303] = 1;
        var literal = text.IndexOf(initial + " " + operation, StringComparison.Ordinal);
        var count = text.LastIndexOf("count", StringComparison.Ordinal);
        Node(0, 3, literal, 1, -1, -1, initial);
        Node(1, 0, count, 5, 1, -1, 0);
        Node(2, opcode, literal, count + 5 - literal, 0, 1, 0);
        Node(3, 0, text.LastIndexOf('x'), 1, 2, -1, 0);
        words[332] = 1;
        words[333] = 2;
        words[334] = 2;
        words[335] = text.IndexOf(localType + " x", StringComparison.Ordinal);
        words[336] = text.IndexOf(';') + 1 - words[335];
        var bytes = new byte[words.Length * sizeof(int)];
        for (var i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * sizeof(int)), words[i]);
        }
        return (SourceText.From(text), bytes);

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
    }
}
