// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NoReturnModuleTests
{
    private const string Returning = "public void Stop() { }";
    private const string Terminating = "[DoesNotReturn] public void Stop() { require false; }";
    private const string Overflowing = """
        [DoesNotReturn] public void Stop() {
            mut int32 value = 2147483647;
            value = value + 1;
            require false;
        }
        """;
    private const string Both = "[NoThrow] [DoesNotReturn] public void Stop() { while (true) { } }";

    private static BoundProgram CompileLibrary(string source)
    {
        var result = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        return Assert.IsType<BoundProgram>(result.Program);
    }

    private static byte[] Producer(string source) =>
        ModuleCompiler.CreateProducer(CompileLibrary(source), ["Stop"]);

    private static byte[] NativeProducer(string source) =>
        NativeModuleCompiler.CreateProducer(CompileLibrary(source), ["Stop"], "Termination", NativeRuntimePolicy.FailFast);

    private static BoundProgram CompileConsumer(string source, byte[] producer)
    {
        var result = Compilation.Create(SyntaxTree.Parse(source), ModuleCompiler.CreateReference(producer));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        return Assert.IsType<BoundProgram>(result.Program);
    }

    [Fact]
    public async Task IndependentConsumerPreservesImportedNonReturnPromiseAsync()
    {
        var producer = Producer(Terminating);
        var reference = ModuleCompiler.CreateReference(producer);
        var declaration = Assert.Single(reference.Functions);
        Assert.True(declaration.IsExternal);
        Assert.True(declaration.DoesNotReturn);
        Assert.False(declaration.IsNoThrow);
        var consumer = CompileConsumer("int32 Main() { Stop(); }", producer);
        var artifact = ModuleCompiler.CreateConsumer(consumer, producer);
        var linked = ModuleCompiler.Link(artifact, producer);
        Assert.True(linked.Functions.Single(static function => function.Name == "Stop").DoesNotReturn);

        var nativeProducer = NativeProducer(Terminating);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumer, nativeProducer, NativeRuntimePolicy.FailFast);
        Assert.Equal(unchecked((int)0xE0540003), await NativeModuleCompiler.RunAsync(nativeConsumer, nativeProducer,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true));
    }

    [Fact]
    public async Task SameNativeConsumerCallsDifferentVerifiedNonReturningBodiesAsync()
    {
        var first = NativeProducer(Terminating);
        var second = NativeProducer(Overflowing);
        var semantic = Producer(Terminating);
        var consumer = CompileConsumer("int32 Main() { Stop(); }", semantic);
        var artifact = ModuleCompiler.CreateConsumer(consumer, semantic);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumer, first, NativeRuntimePolicy.FailFast);
        var original = (byte[])nativeConsumer.Clone();
        var imageHash = NativeModuleCompiler.Inspect(nativeConsumer).ImageSha256;
        _ = ModuleCompiler.Link(artifact, Producer(Overflowing));
        Assert.Equal(unchecked((int)0xE0540003), await NativeModuleCompiler.RunAsync(nativeConsumer, first,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true));
        Assert.Equal(unchecked((int)0xE0540001), await NativeModuleCompiler.RunAsync(nativeConsumer, second,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true));
        Assert.Equal(original, nativeConsumer);
        Assert.Equal(imageHash, NativeModuleCompiler.Inspect(nativeConsumer).ImageSha256);
    }

    [Fact]
    public void WeakeningNonReturnPromiseRejectsReplacementBeforeLaunch()
    {
        var promised = Producer(Terminating);
        var consumer = CompileConsumer("int32 Main() { Stop(); }", promised);
        var semantic = ModuleCompiler.CreateConsumer(consumer, promised);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(semantic, Producer(Returning)));
        var original = NativeProducer(Terminating);
        var replacement = NativeProducer(Returning);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumer, original, NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(nativeConsumer, replacement));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.RunAsync(nativeConsumer, replacement,
            TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task ReturningConsumerAcceptsStrongerNonReturnPromiseWithoutReemissionAsync()
    {
        var original = NativeProducer(Returning);
        var replacement = NativeProducer(Terminating);
        var semantic = Producer(Returning);
        var consumer = CompileConsumer("int32 Main() { Stop(); return 42; }", semantic);
        var artifact = ModuleCompiler.CreateConsumer(consumer, semantic);
        _ = ModuleCompiler.Link(artifact, Producer(Terminating));
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumer, original, NativeRuntimePolicy.FailFast);
        var imageHash = NativeModuleCompiler.Inspect(nativeConsumer).ImageSha256;
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(nativeConsumer, original,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true));
        Assert.Equal(unchecked((int)0xE0540003), await NativeModuleCompiler.RunAsync(nativeConsumer, replacement,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true));
        Assert.Equal(imageHash, NativeModuleCompiler.Inspect(nativeConsumer).ImageSha256);
    }

    [Fact]
    public void BothPromisesStrengthenIndependentlyButNeverWeaken()
    {
        var sources = new[] { Returning, "[NoThrow] " + Returning, Terminating, Both };
        var modules = sources.Select(Producer).ToArray();
        var packages = sources.Select(NativeProducer).ToArray();
        for (var expected = 0; expected < sources.Length; expected++)
        {
            var reference = ModuleCompiler.CreateReference(modules[expected]);
            var promise = Assert.Single(reference.Functions);
            Assert.Equal((expected & 1) != 0, promise.IsNoThrow);
            Assert.Equal((expected & 2) != 0, promise.DoesNotReturn);
            var consumer = CompileConsumer("int32 Main() { Stop(); return 42; }", modules[expected]);
            var semantic = ModuleCompiler.CreateConsumer(consumer, modules[expected]);
            var native = NativeModuleCompiler.CreateConsumer(consumer, packages[expected], NativeRuntimePolicy.FailFast);
            for (var actual = 0; actual < sources.Length; actual++)
            {
                if ((expected & actual) == expected)
                {
                    _ = ModuleCompiler.Link(semantic, modules[actual]);
                    NativeModuleCompiler.Validate(native, packages[actual]);
                }
                else
                {
                    _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(semantic, modules[actual]));
                    _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(native, packages[actual]));
                }
            }
        }
    }

    [Fact]
    public void ConsumerDeclarationNormalizationChecksEachPromise()
    {
        var both = Producer(Both);
        var consumer = CompileConsumer("int32 Main() { Stop(); return 42; }", both);
        var index = consumer.Functions.IndexOf(consumer.Functions.Single(static function => function.IsExternal));
        var weaker = consumer with {
            Functions = consumer.Functions.SetItem(index, consumer.Functions[index] with {
                IsNoThrow = false,
                DoesNotReturn = false,
            }),
        };
        var normalized = ModuleCompiler.CreateConsumer(weaker, both);
        var imported = ModuleCompiler.Link(normalized, both).Functions[index];
        Assert.True(imported.IsNoThrow);
        Assert.True(imported.DoesNotReturn);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateConsumer(consumer, Producer("[NoThrow] " + Returning)));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateConsumer(consumer, Producer(Terminating)));
    }

    [Fact]
    public void ProducerBodiesCannotForgeNonReturnAndUnknownContractSuffixesAreRejected()
    {
        var program = CompileLibrary(Returning);
        var forged = program with {
            Functions = program.Functions.SetItem(0, program.Functions[0] with { DoesNotReturn = true }),
        };
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(forged, ["Stop"]));
        _ = Assert.Throws<ArgumentException>(() => NativeModuleCompiler.CreateProducer(forged,
            ["Stop"], "Termination", NativeRuntimePolicy.FailFast));
        var nonVoid = CompileLibrary("public int32 Value() { return 42; }");
        var invalidReturn = nonVoid with {
            Functions = nonVoid.Functions.SetItem(0, nonVoid.Functions[0] with { DoesNotReturn = true }),
        };
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(invalidReturn, ["Value"]));

        var producer = Producer(Returning);
        var consumer = ModuleCompiler.CreateConsumer(CompileConsumer("int32 Main() { Stop(); return 0; }", producer), producer);
        var malformed = (byte[])consumer.Clone();
        var suffix = Encoding.UTF8.GetBytes("|MayThrow|MayReturn");
        var offset = malformed.AsSpan().IndexOf(suffix);
        Assert.True(offset >= 0);
        malformed[offset + "|MayThrow|".Length] = (byte)'X';
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(malformed, producer));
        producer[4] = 8;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(producer));
    }
}
