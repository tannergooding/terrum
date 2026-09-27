// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeOwnedBufferTests
{
    private const string BufferBuilder = """
        public bool Make(int32 capacity, out OwnedInt32Buffer result) {
            return OwnedInt32Buffer.TryCreate(capacity, out result);
        }
        public int32 ReadFirst(OwnedInt32Buffer value) {
            return value.Read(0);
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "native-10")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "native-11")]
    public void ScalarProgramsAndRetainedModulesDoNotAcquireBufferProfile(
        BoundRuntimePolicy policy, string expectedProfile)
    {
        var scalar = Compilation.Create(SyntaxTree.Parse("int32 Main() { return 42; }"), policy);
        var program = Assert.IsType<BoundProgram>(scalar.Program);
        Assert.DoesNotContain(program.Functions, static function => function.Intrinsic != BoundIntrinsic.None);
        Assert.Equal(expectedProfile, Compilation.GetNativeProfileVersion(program));

        var library = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Value() { return 42; }"), policy);
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Value"]);
        Assert.Equal(expectedProfile, ModuleCompiler.GetNativeProfileVersion(producer, false));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void CompilerOwnedIntrinsicsCannotBeExportedOrSelectedAsEntry(BoundRuntimePolicy policy)
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(BufferBuilder), policy);
        var program = Assert.IsType<BoundProgram>(library.Program);
        var operation = Assert.Single(program.Functions,
            static function => function.Intrinsic == BoundIntrinsic.BufferCount);
        Assert.True(operation.IsInternal);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program,
            [operation.Name], (NativeRuntimePolicy)policy));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, [operation.Name]));
        var forged = program with { EntryPoint = program.Functions.IndexOf(operation) };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(forged, (NativeRuntimePolicy)policy));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void EveryNativeBufferIntrinsicEndsInsideItsOwnFunction(BoundRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var created);
                mut OwnedInt32Buffer value = move created;
                require value.TryAppend(42);
                return value.Read(0);
            }
            """), policy);
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var code = NativeEmitter.EmitCode(program, (NativeRuntimePolicy)policy);
        var intrinsicCount = 0;
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (program.Functions[index].Intrinsic == BoundIntrinsic.None)
            {
                continue;
            }
            intrinsicCount++;
            var emitted = code.Functions[6 + index];
            Assert.InRange(emitted.End, emitted.Start + 1, code.Code.Length);
            Assert.Equal(0x5D, code.Code[emitted.End - 2]);
            Assert.Equal(0xC3, code.Code[emitted.End - 1]);
        }
        Assert.Equal(7, intrinsicCount);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task BufferPrefixAndTransfersExecuteAsync(BoundRuntimePolicy policy)
    {
        const string Source = BufferBuilder + """
            bool Forward(out OwnedInt32Buffer result) {
                if (!Make(2, out var raw)) { return false; }
                mut OwnedInt32Buffer first = move raw;
                if (!first.TryAppend(40) || !first.TryAppend(42) || first.TryAppend(99)) { return false; }
                result = move first;
                return true;
            }
            int32 Main() {
                if (!Forward(out var raw)) { return 0; }
                mut OwnedInt32Buffer value = move raw;
                if (value.Capacity != 2 || value.Count != 2 || value.Read(0) != 40) { return 1; }
                value.Set(1, value.Read(1));
                if (value.Read(1) != ReadFirst(move value) + 2) { return 2; }
                return 42;
            }
            """;
        Assert.Equal(42, await RunAsync(Source, policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ExistingOwnerCanBeReplacedByConditionalOutputAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(2, out var created);
                mut OwnedInt32Buffer buffer = move created;
                require buffer.TryAppend(1);
                require OwnedInt32Buffer.TryCreate(1, out buffer);
                require buffer.TryAppend(42);
                return buffer.Read(0);
            }
            """;
        Assert.Equal(42, await RunAsync(Source, policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ScannerWritesInitializedWordsThroughOwnedBufferAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            int32 Main() {
                if (!OwnedInt32Buffer.TryCreate(3, out var raw)) { return 1; }
                mut OwnedInt32Buffer words = move raw;
                for (mut int32 i = 0; i < 3; i = i + 1) {
                    char16 character = Host.Source[i];
                    if (character < char16(48) || character > char16(57)) { return 2; }
                    if (!words.TryAppend(int32(character) - 48)) { return 3; }
                }
                if (words.Count != 3 || words.Capacity != 3) { return 4; }
                return words.Read(0) * 10 + words.Read(1) + words.Read(2);
            }
            """;
        Assert.Equal(42, await RunAsync(Source, policy, [51, 52, 56]).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("OwnedInt32Buffer copy = value;", "TRM3023")]
    [InlineData("OwnedInt32Buffer copy = move value; value.Count();", "TRM3023")]
    [InlineData("value.TryAppend(Steal(move value));", "TRM3023")]
    [InlineData("value.TryAppend(Observe(Replace(out value)));", "TRM3023")]
    [InlineData("value.Set(0, Steal(move value));", "TRM3023")]
    [InlineData("raw.TryAppend(1);", "TRM3023")]
    public void OwnershipAndAccessViolationsAreDiagnosed(string body, string code)
    {
        var source = """
            int32 Steal(OwnedInt32Buffer item) { return item.Count; }
            bool Replace(out OwnedInt32Buffer item) {
                return OwnedInt32Buffer.TryCreate(1, out item);
            }
            int32 Observe(bool result) {
                if (result) { return 1; }
                return 0;
            }
            int32 Main() {
                if (!OwnedInt32Buffer.TryCreate(1, out var raw)) { return 0; }
                mut OwnedInt32Buffer value = move raw;
            """ + body + """
                return 0;
            }
            """;
        var result = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Null(result.Program);
    }

    [Theory]
    [InlineData("value.Read(Observe(Replace(out value)))")]
    [InlineData("value.Read(Observe(ReplaceMoved(move value, out value)))")]
    [InlineData("value.Read(Observe(value.TryAppend(42)))")]
    public void ReadonlyBufferReceiverReservesOriginalOwnerAcrossScalarArguments(string expression)
    {
        var source = $$"""
            bool Replace(out OwnedInt32Buffer item) {
                return OwnedInt32Buffer.TryCreate(1, out item);
            }
            bool ReplaceMoved(OwnedInt32Buffer previous, out OwnedInt32Buffer item) {
                return OwnedInt32Buffer.TryCreate(1, out item);
            }
            int32 Observe(bool result) { if (result) { return 0; } return 0; }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(2, out var created);
                mut OwnedInt32Buffer value = move created;
                require value.TryAppend(40);
                return {{expression}};
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023"
            && diagnostic.Span.Start == source.LastIndexOf("Observe(", StringComparison.Ordinal));
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ReadonlyBufferReceiverAllowsCompletedQueryAndScalarSideEffectAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            int32 Index(int32 count) {
                Host.Trace[0] = Host.Trace[0] + 1;
                return count - 1;
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var created);
                mut OwnedInt32Buffer value = move created;
                require value.TryAppend(41);
                int32 result = value.Read(Index(value.Count));
                return result + Host.Trace[0];
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0], true)], policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(42, await RunAsync(Assert.IsType<BoundProgram>(compilation.Program), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "value.Read(Observe(other.TryAppend(7)))")]
    [InlineData(BoundRuntimePolicy.FailFast, "value.Read(Observe(Consume(move other)))")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "value.Read(Observe(other.TryAppend(7)))")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "value.Read(Observe(Consume(move other)))")]
    public async Task ReadonlyBufferReceiverAllowsDistinctOwnerEffectsAsync(BoundRuntimePolicy policy, string expression)
    {
        var source = $$"""
            bool Consume(OwnedInt32Buffer item) { return item.Count == 0; }
            int32 Observe(bool result) { if (result) { return 0; } return 0; }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var first);
                mut OwnedInt32Buffer value = move first;
                require value.TryAppend(42);
                require OwnedInt32Buffer.TryCreate(1, out var second);
                mut OwnedInt32Buffer other = move second;
                return {{expression}};
            }
            """;
        Assert.Equal(42, await RunAsync(source, policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ConditionalOwnerMoveTracksExecutedAndSkippedOperandsAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            bool Consume(OwnedInt32Buffer item) { return item.Read(0) == 42; }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer value = move raw;
                require value.TryAppend(42);
                if (false && Consume(move value)) { return 1; }
                if (value.Read(0) != 42) { return 2; }
                if (!Consume(move value)) { return 3; }
                return 42;
            }
            """;
        Assert.Equal(42, await RunAsync(Source, policy).ConfigureAwait(true));
    }

    [Fact]
    public void ConditionalOwnerMoveCannotLeaveOwnerReadable()
    {
        const string Source = """
            bool Consume(OwnedInt32Buffer item) { return item.Count == 0; }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer value = move raw;
                if (Consume(move value)) { return value.Read(0); }
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023"
            && diagnostic.Span.Start == Source.IndexOf("value.Read(0)", StringComparison.Ordinal));
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void NativeBufferIsNotForgeableAsSourceStructure()
    {
        var result = Compilation.Create(SyntaxTree.Parse("""
            struct OwnedInt32Buffer { public int64 Handle; }
            int32 Main() { return 0; }
            """));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TRM3003");
    }

    [Theory]
    [InlineData("struct Copy { public OwnedInt32Buffer Value; }", "TRM3023")]
    [InlineData("[NoThrow] int32 Get(OwnedInt32Buffer value) { return value.Read(0); }", "TRM3017")]
    [InlineData("[NoThrow] bool Make(out OwnedInt32Buffer value) { return OwnedInt32Buffer.TryCreate(1, out value); }", "TRM3017")]
    public void NativeBufferOwnershipAndEffectsAreChecked(string declaration, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { return 0; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompletedCompositeBufferCanBeBorrowedDuringConstructionAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            unique struct Record {
                public OwnedInt32Buffer Words;
                public int32 Tail;
                public Record() {
                    require OwnedInt32Buffer.TryCreate(1, out var created);
                    mut OwnedInt32Buffer words = move created;
                    require words.TryAppend(40);
                    Words = move words;
                    require Words.Count == 1;
                    Tail = Words.Read(0) + 2;
                }
                public int32 Read() { return Words.Read(0) + Tail - 40; }
            }
            int32 Main() {
                Record record = new Record();
                return record.Read();
            }
            """;
        Assert.Equal(42, await RunAsync(Source, policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ReadingOutsideInitializedPrefixIsCheckedAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            int32 Main() {
                if (!OwnedInt32Buffer.TryCreate(1, out var value)) { return 0; }
                return value.Read(0);
            }
            """;
        Assert.Equal(unchecked((int)0xE0540003),
            await RunAsync(Source, policy).ConfigureAwait(true));
    }

    [Fact]
    public void ForgedIntrinsicOrMutableLoanCannotBypassVerifier()
    {
        var result = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                if (!OwnedInt32Buffer.TryCreate(1, out var raw)) { return 0; }
                mut OwnedInt32Buffer value = move raw;
                value.TryAppend(42);
                return value.Read(0);
            }
            """));
        var program = Assert.IsType<BoundProgram>(result.Program);
        var intrinsic = Assert.Single(program.Functions, function => function.Intrinsic == BoundIntrinsic.BufferRead);
        var index = program.Functions.IndexOf(intrinsic);
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(index, intrinsic with { IsNoThrow = true }),
        }));
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(index, intrinsic with { Intrinsic = BoundIntrinsic.BufferSet }),
        }));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SeparateModulesRetainNativeBufferContractAsync(BoundRuntimePolicy policy)
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse(BufferBuilder), policy);
        Assert.False(provider.HasErrors, string.Join(Environment.NewLine, provider.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(provider.Program), ["Make", "ReadFirst"]);
        Assert.Equal("native-15", ModuleCompiler.GetNativeProfileVersion(producer, false));
        var client = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                if (!Make(1, out var raw)) { return 0; }
                mut OwnedInt32Buffer value = move raw;
                if (!value.TryAppend(42)) { return 1; }
                return ReadFirst(move value);
            }
            """), ModuleCompiler.CreateReference(producer), runtimePolicy: policy);
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program), producer);
        Assert.Equal("native-15", ModuleCompiler.GetNativeProfileVersion(consumer, true));
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(42, await RunAsync(linked, policy).ConfigureAwait(true));

        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var nativeProducer = NativeModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(provider.Program), ["Make", "ReadFirst"], "buffer_leaf", nativePolicy);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(client.Program), nativeProducer, nativePolicy);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(nativeConsumer, nativeProducer,
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true));
        var replacement = Compilation.CreateLibrary(SyntaxTree.Parse(BufferBuilder.Replace(
            "TryCreate(capacity, out result)", "TryCreate(capacity + 1, out result)", StringComparison.Ordinal)), policy);
        Assert.False(replacement.HasErrors, string.Join(Environment.NewLine, replacement.Diagnostics));
        var nativeReplacement = NativeModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(replacement.Program), ["Make", "ReadFirst"], "buffer_leaf", nativePolicy);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(nativeConsumer, nativeReplacement,
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true));
    }

    private static async Task<int> RunAsync(string source, BoundRuntimePolicy policy, int[]? characters = null)
    {
        System.Collections.Immutable.ImmutableArray<HostBuffer> buffers = characters is null ? []
            : [new HostBuffer("Source", PrimitiveType.Char16, [.. characters], false)];
        var compilation = Compilation.Create(SyntaxTree.Parse(source), buffers, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return await RunAsync(Assert.IsType<BoundProgram>(compilation.Program), policy).ConfigureAwait(true);
    }

    private static async Task<int> RunAsync(BoundProgram program, BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var image = NativeEmitter.Emit(program, nativePolicy);
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"terrum-buffer-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Native process did not start.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
                throw;
            }
            return process.ExitCode;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
