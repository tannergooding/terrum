// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class PublicInitReferenceTests
{
    private const string ScalarLibrary = """
        public bool TryFill(bool available, init ref<int32> result) {
            if (!available) { return false; }
            result = 42;
            return true;
        }
        """;

    private const string ScalarConsumer = """
        int32 Main() {
            if (TryFill(false, init var result)) { return 1; }
            require TryFill(true, init result);
            return result;
        }
        """;

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ExportedScalarInitServicesUnchangedSemanticAndNativeConsumersAsync(NativeRuntimePolicy policy)
    {
        var boundPolicy = (BoundRuntimePolicy)policy;
        var program = CompileLibrary(ScalarLibrary, policy);
        var producer = ModuleCompiler.CreateProducer(program, ["TryFill"]);
        Assert.Equal(64,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.True(Assert.Single(reference.Functions).Variables[1].IsInit);
        var consumerProgram = CompileConsumer(ScalarConsumer, reference, policy);
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));

        var replacementProgram = CompileLibrary(ScalarLibrary.Replace("result = 42", "result = 43",
            StringComparison.Ordinal), policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["TryFill"]);
        Assert.Equal(43, NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), policy));

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, ["TryFill"], "NodeB", policy);
        var nativeConsumerProgram = CompileConsumer(ScalarConsumer,
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(nativeConsumerProgram, [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var nativeReplacement = NativeModuleCompiler.CreateProducer(
            replacementProgram, ["TryFill"], "NodeB", policy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [nativeReplacement]);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(43, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));

        var outLibrary = ScalarLibrary.Replace("init ref<int32>", "out int32", StringComparison.Ordinal);
        var outProgram = CompileLibrary(outLibrary, policy);
        var outProducer = ModuleCompiler.CreateProducer(outProgram, ["TryFill"]);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, outProducer));
        var nativeOut = NativeModuleCompiler.CreateProducer(outProgram, ["TryFill"], "NodeB", policy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(nativeConsumer, [nativeOut]));
        var badCall = Compilation.Create(SyntaxTree.Parse(ScalarConsumer.Replace("init var result", "out var result",
            StringComparison.Ordinal)), reference, boundPolicy);
        Assert.Contains(badCall.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");
        var mismatchedInit = Compilation.Create(SyntaxTree.Parse(ScalarConsumer),
            ModuleCompiler.CreateReference(outProducer), boundPolicy);
        Assert.Contains(mismatchedInit.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ExportedNonBooleanInitPublishesOnNormalReturnAsync(NativeRuntimePolicy policy)
    {
        const string Library = """
            public struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) { First = first; Second = second; }
            }
            public unique struct Ticket {
                public int32 Value;
                public Ticket(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public int32 Make(init ref<int32> result) { result = 40; return 2; }
            public void Fill(init ref<int32> result) { result = 41; }
            public int32 MakePair(init ref<Pair> result) { result = new Pair(40, 2); return 0; }
            public int32 MakeTicket(init ref<Ticket> result) { result = new Ticket(3); return 0; }
            public int32 Consume(Ticket ticket) { return ticket.Value; }
            """;
        const string Consumer = """
            int32 Main() {
                init int32 first;
                init int32 second;
                init Pair pair;
                init Ticket ticket;
                int32 status = Make(init first);
                Fill(init second);
                int32 pairStatus = MakePair(init pair);
                int32 ticketStatus = MakeTicket(init ticket);
                int32 ticketValue = Consume(move ticket);
                return first + status + second + pair.First + pair.Second + pairStatus
                    + ticketStatus + ticketValue;
            }
            """;
        var program = CompileLibrary(Library, policy);
        var exports = new[] { "Make", "Fill", "MakePair", "MakeTicket", "Consume", "Ticket.Close" };
        var producer = ModuleCompiler.CreateProducer(program, exports);
        var reference = ModuleCompiler.CreateReference(producer);
        var checkedInitFunctions = 0;
        foreach (var function in reference.Functions)
        {
            if (function.Name is "Make" or "Fill" or "MakePair" or "MakeTicket")
            {
                Assert.True(function.Variables[0].IsInit);
                checkedInitFunctions++;
            }
        }
        Assert.Equal(4, checkedInitFunctions);
        var consumerProgram = CompileConsumer(Consumer, reference, policy);
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        Assert.Equal(128, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, exports, "NodeB", policy);
        var nativeConsumerProgram = CompileConsumer(Consumer,
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(nativeConsumerProgram,
            [nativeProducer], policy);
        Assert.Equal(128, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Fact]
    public void CorruptPublicInitModeCannotMasqueradeAsOut()
    {
        var producer = ModuleCompiler.CreateProducer(
            CompileLibrary(ScalarLibrary, NativeRuntimePolicy.FailFast), ["TryFill"]);
        var mode = new byte[] { 6, 0, 0, 0, (byte)'r', (byte)'e', (byte)'s', (byte)'u',
            (byte)'l', (byte)'t', (byte)PrimitiveType.Int32, 0, 0, 0, 1, 1, 1 };
        var location = producer.AsSpan().IndexOf(mode);
        Assert.True(location >= 0);
        Assert.Equal(-1, producer.AsSpan(location + 1).IndexOf(mode));
        producer[location + mode.Length - 1] = 0;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(producer));
    }

    [Fact]
    public void OlderModuleVersionRejectsPublicInitImport()
    {
        var producer = ModuleCompiler.CreateProducer(
            CompileLibrary(ScalarLibrary, NativeRuntimePolicy.FailFast), ["TryFill"]);
        var consumer = ModuleCompiler.CreateConsumer(CompileConsumer(ScalarConsumer,
            ModuleCompiler.CreateReference(producer), NativeRuntimePolicy.FailFast), producer);
        Assert.Equal(64,BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)));
        BinaryPrimitives.WriteInt32LittleEndian(consumer.AsSpan(4, 4), 32);
        var error = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(consumer, producer));
        Assert.Equal("Unknown module format or version.", error.Message);
    }

    [Fact]
    public void ForgedPublicInitWithoutAnOutputSlotIsRejected()
    {
        var program = CompileLibrary(ScalarLibrary, NativeRuntimePolicy.FailFast);
        var function = Assert.Single(program.Functions);
        var forged = function with {
            Variables = function.Variables.SetItem(1, function.Variables[1] with { IsOut = false }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program with {
            Functions = program.Functions.SetItem(0, forged),
        }));
    }

    [Fact]
    public void PublicInitDoesNotAdmitManagedHandleOutputs()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public bool Leak(init ref<ManagedNode> result) { return false; }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedGraphIntermediateExportsCopyInitWithoutLeakingHandlesAsync(NativeRuntimePolicy policy)
    {
        const string Leaf = "namespace Source; public int32 Bias() { return 40; }";
        const string Middle = """
            using Source;
            public struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            class Box {
                public Pair Data;
                public Box(Pair value) { Data = value; }
            }
            public bool TryRead(bool available, init ref<Pair> result) {
                if (!available) { return false; }
                require Box.TryCreate(new Pair(int64(Bias()), 2), out var box);
                result = box.Data;
                return true;
            }
            public int32 Read(init ref<Pair> result) {
                require Box.TryCreate(new Pair(int64(Bias()), 2), out var box);
                result = box.Data;
                return 0;
            }
            """;
        const string Root = """
            int32 Main() {
                if (TryRead(false, init var value)) { return 1; }
                require TryRead(true, init value);
                int32 status = Read(init var copy);
                return int32(value.First + value.Second + copy.First + copy.Second) + status;
            }
            """;
        var leaf = NativeModuleCompiler.CreateProducer(CompileLibrary(Leaf, policy),
            ["Source.Bias"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(CompileLibrary(Middle, policy,
            NativeModuleCompiler.CreateGraphReference([leaf])), ["TryRead", "Read"], "NodeA", [leaf], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(CompileConsumer(Root,
            NativeModuleCompiler.CreateGraphReference([middle, leaf]), policy),
            [middle, leaf], policy);
        var originalRootHash = SHA256.HashData(root);
        var originalMiddleHash = SHA256.HashData(middle);
        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        Assert.Equal(84, await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var replacement = NativeModuleCompiler.CreateProducer(CompileLibrary(
            Leaf.Replace("return 40", "return 41", StringComparison.Ordinal), policy),
            ["Source.Bias"], "NodeB", policy);
        NativeModuleCompiler.ValidateGraph(root, [middle, replacement]);
        Assert.Equal(86, await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(originalRootHash, SHA256.HashData(root));
        Assert.Equal(originalMiddleHash, SHA256.HashData(middle));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PublicUniqueInitPublishesOnlyACompleteOwnerAsync(NativeRuntimePolicy policy)
    {
        const string Library = """
            public unique struct Ticket {
                public int32 Value;
                public Ticket(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public bool TryTicket(bool available, init ref<Ticket> ticket) {
                ticket = new Ticket(42);
                return available;
            }
            public int32 Consume(Ticket ticket) { return ticket.Value; }
            """;
        const string Consumer = """
            int32 Main() {
                if (TryTicket(false, init var ticket)) { return 1; }
                require TryTicket(true, init ticket);
                return Consume(move ticket);
            }
            """;
        string[] exports = ["TryTicket", "Consume", "Ticket.Close"];
        var program = CompileLibrary(Library, policy);
        var producer = ModuleCompiler.CreateProducer(program, exports);
        var consumer = ModuleCompiler.CreateConsumer(CompileConsumer(Consumer,
            ModuleCompiler.CreateReference(producer), policy), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        var nativeProducer = NativeModuleCompiler.CreateProducer(program, exports, "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(CompileConsumer(Consumer,
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), policy), [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Fact]
    public void ManagedGraphCannotExportUniqueInitOutputs()
    {
        const string Library = """
            public unique struct Ticket {
                public int32 Value;
                public Ticket(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            public bool TryTicket(init ref<Ticket> ticket) {
                require Cell.TryCreate(42, out var cell);
                ticket = new Ticket(cell.Value);
                return true;
            }
            """;
        var program = CompileLibrary(Library, NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(program, ["TryTicket", "Ticket.Close"]));
    }

    private static BoundProgram CompileLibrary(string source, NativeRuntimePolicy policy,
        CompilationReference? reference = null)
    {
        var tree = SyntaxTree.Parse(source);
        var compilation = reference is null
            ? Compilation.CreateLibrary(tree, (BoundRuntimePolicy)policy)
            : Compilation.CreateLibrary(tree, reference, (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static BoundProgram CompileConsumer(string source, CompilationReference reference,
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), reference, (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", ".."));
}
