// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics;

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
    private static BoundProgram CompileLibrary(string source)
    {
        var result = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = result.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
    }

    private static byte[] Producer(string source) =>
        ModuleCompiler.CreateProducer(CompileLibrary(source), ["Stop"]);

    private static byte[] NativeProducer(string source) =>
        NativeModuleCompiler.CreateProducer(CompileLibrary(source), ["Stop"],
            "Termination", NativeRuntimePolicy.FailFast);

    private static BoundProgram CompileConsumer(string source, byte[] producer,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast)
    {
        var result = Compilation.Create(SyntaxTree.Parse(source), ModuleCompiler.CreateReference(producer), policy);
        Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = result.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
    }

    [Test]
    public async Task IndependentConsumerPreservesImportedNonReturnPromiseAsync()
    {
        var producer = Producer(Terminating);
        var reference = ModuleCompiler.CreateReference(producer);
        var declaration = global::System.Linq.Enumerable.Single(reference.Functions);
        Assert.That(declaration.IsExternal, Is.True);
        Assert.That(declaration.DoesNotReturn, Is.True);
        Assert.That(declaration.NoFailureProof, Is.False);
        var consumer = CompileConsumer("int32 Main() { Stop(); }", producer);
        var artifact = ModuleCompiler.CreateConsumer(consumer, producer);
        var linked = ModuleCompiler.Link(artifact, producer);
        Assert.That(linked.Functions.Single(static function => function.Name == "Stop").DoesNotReturn, Is.True);

        var nativeProducer = NativeProducer(Terminating);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumer, nativeProducer, NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, nativeProducer,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true), Is.EqualTo(unchecked((int)0xE0540003)));
    }

    [Test]
    public async Task IndependentConsumerPreservesImportedNonUnitNoReturnPromiseAsync()
    {
        const string source = "[DoesNotReturn] public int32 Stop() { require false; }";
        var producer = Producer(source);
        var declaration = global::System.Linq.Enumerable.Single(ModuleCompiler.CreateReference(producer).Functions);
        Assert.That(declaration.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(declaration.DoesNotReturn, Is.True);
        var consumer = CompileConsumer("int32 Main() { return Stop(); }", producer);
        var artifact = ModuleCompiler.CreateConsumer(consumer, producer);
        Assert.That(ModuleCompiler.Link(artifact, producer).Functions
            .Single(static function => function.Name == "Stop").DoesNotReturn, Is.True);

        var nativeProducer = NativeProducer(source);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumer, nativeProducer, NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, nativeProducer,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true), Is.EqualTo(unchecked((int)0xE0540003)));
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ManagedClassConstructorNoReturnSurvivesPrivateModulesAsync(BoundRuntimePolicy policy)
    {
        const string classSource = """
            class Leaf {
                public int32 Value;
                [DoesNotReturn] public Leaf() { require false; }
            }
            """;
        var source = policy == BoundRuntimePolicy.CheckedExceptions ? classSource + """
            public int32 Exercise() {
                unsafe {
                    try {
                        if (Leaf.TryCreate(out var leaf)) { return 1; }
                        return 2;
                    } catch {
                        return ManagedHeap.Collect() == 0 ? 42 : 3;
                    }
                }
                return 0;
            }
            """ : classSource + """
            public int32 Exercise() {
                if (Leaf.TryCreate(out var leaf)) { return 1; }
                return 2;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        var semanticProducer = ModuleCompiler.CreateProducer(program, ["Exercise"]);
        var semanticConsumer = CompileConsumer("int32 Main() { return Exercise(); }", semanticProducer, policy);
        var linked = ModuleCompiler.Link(ModuleCompiler.CreateConsumer(semanticConsumer, semanticProducer),
            semanticProducer);
        Assert.That(global::System.Linq.Enumerable.Single(linked.Functions,
            static function => function.IsClassConstructor).DoesNotReturn, Is.True);

        var nativePolicy = (NativeRuntimePolicy)policy;
        var nativeProducer = NativeModuleCompiler.CreateProducer(program, ["Exercise"], "Constructor", nativePolicy);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            NativeModuleCompiler.CreateReference(nativeProducer), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual3 = consumerCompilation.Program;
            Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual3!;
        }))();
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumerProgram, nativeProducer, nativePolicy);
        var expected = policy == BoundRuntimePolicy.CheckedExceptions
            ? 42 : unchecked((int)0xE0540003);
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, nativeProducer,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true), Is.EqualTo(expected));
    }

    [Test]
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
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, first,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true), Is.EqualTo(unchecked((int)0xE0540003)));
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, second,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true), Is.EqualTo(unchecked((int)0xE0540001)));
        Assert.That(nativeConsumer, Is.EqualTo(original));
        Assert.That(NativeModuleCompiler.Inspect(nativeConsumer).ImageSha256, Is.EqualTo(imageHash));
    }

    [Test]
    public void WeakeningNonReturnPromiseRejectsReplacementBeforeLaunch()
    {
        var promised = Producer(Terminating);
        var consumer = CompileConsumer("int32 Main() { Stop(); }", promised);
        var semantic = ModuleCompiler.CreateConsumer(consumer, promised);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(semantic, Producer(Returning)))!;
        var original = NativeProducer(Terminating);
        var replacement = NativeProducer(Returning);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumer, original, NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(nativeConsumer, replacement))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.RunAsync(nativeConsumer, replacement,
            TimeSpan.FromSeconds(10)).GetAwaiter().GetResult())!;
    }

    [Test]
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
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, original,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, replacement,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true), Is.EqualTo(unchecked((int)0xE0540003)));
        Assert.That(NativeModuleCompiler.Inspect(nativeConsumer).ImageSha256, Is.EqualTo(imageHash));
    }

    [Test]
    public void NonReturnPromiseStrengthensButNeverWeakens()
    {
        var sources = new[] { Returning, Terminating };
        var modules = sources.Select(Producer).ToArray();
        var packages = sources.Select(NativeProducer).ToArray();
        for (var expected = 0; expected < sources.Length; expected++)
        {
            var reference = ModuleCompiler.CreateReference(modules[expected]);
            var promise = global::System.Linq.Enumerable.Single(reference.Functions);
            Assert.That(promise.NoFailureProof, Is.False);
            Assert.That(promise.DoesNotReturn, Is.EqualTo(expected != 0));
            var consumerSource = expected == 0
                ? "int32 Main() { Stop(); return 42; }"
                : "int32 Main() { Stop(); }";
            var consumer = CompileConsumer(consumerSource, modules[expected]);
            var semantic = ModuleCompiler.CreateConsumer(consumer, modules[expected]);
            var native = NativeModuleCompiler.CreateConsumer(consumer, packages[expected], NativeRuntimePolicy.FailFast);
            for (var actual = 0; actual < sources.Length; actual++)
            {
                if (expected == 0 || actual == 1)
                {
                    _ = ModuleCompiler.Link(semantic, modules[actual]);
                    NativeModuleCompiler.Validate(native, packages[actual]);
                }
                else
                {
                    _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(semantic, modules[actual]))!;
                    _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(native, packages[actual]))!;
                }
            }
        }
    }

    [Test]
    public void ConsumerDeclarationNormalizationChecksEachPromise()
    {
        var terminating = Producer(Terminating);
        var consumer = CompileConsumer("int32 Main() { Stop(); return 42; }", terminating);
        var index = consumer.Functions.IndexOf(consumer.Functions.Single(static function => function.IsExternal));
        var weaker = consumer with {
            Functions = consumer.Functions.SetItem(index, consumer.Functions[index] with {
                DoesNotReturn = false,
            }),
        };
        var normalized = ModuleCompiler.CreateConsumer(weaker, terminating);
        var imported = ModuleCompiler.Link(normalized, terminating).Functions[index];
        Assert.That(imported.NoFailureProof, Is.False);
        Assert.That(imported.DoesNotReturn, Is.True);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateConsumer(consumer, Producer(Returning)))!;
    }

    [Test]
    public void ProducerBodiesCannotForgeNonReturnAndUnknownContractSuffixesAreRejected()
    {
        var program = CompileLibrary(Returning);
        var forged = program with {
            Functions = program.Functions.SetItem(0, program.Functions[0] with { DoesNotReturn = true }),
        };
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(forged, ["Stop"]))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateProducer(forged,
            ["Stop"], "Termination", NativeRuntimePolicy.FailFast))!;
        var nonVoid = CompileLibrary("public int32 Value() { return 42; }");
        var invalidReturn = nonVoid with {
            Functions = nonVoid.Functions.SetItem(0, nonVoid.Functions[0] with { DoesNotReturn = true }),
        };
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(invalidReturn, ["Value"]))!;

        var producer = Producer(Returning);
        var consumer = ModuleCompiler.CreateConsumer(CompileConsumer("int32 Main() { Stop(); return 0; }", producer), producer);
        var malformed = (byte[])consumer.Clone();
        var suffix = Encoding.UTF8.GetBytes("|MayReturn");
        var offset = malformed.AsSpan().IndexOf(suffix);
        Assert.That(offset >= 0, Is.True);
        malformed[offset + 1] = (byte)'X';
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(malformed, producer))!;
        producer[4] = 8;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(producer))!;
    }
}
