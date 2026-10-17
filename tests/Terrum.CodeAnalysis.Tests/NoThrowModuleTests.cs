// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NoThrowModuleTests
{
    private static BoundProgram CompileLibrary(string source)
    {
        var result = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = result.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
    }

    private static byte[] Producer(string source) =>
        ModuleCompiler.CreateProducer(CompileLibrary(source), ["Compute"]);

    private static BoundProgram CompileConsumer(string source, byte[] producer)
    {
        var result = Compilation.Create(SyntaxTree.Parse(source), ModuleCompiler.CreateReference(producer));
        Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = result.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
    }

    [Test]
    public async Task NoThrowConstructorAndImportedFunctionRemainDeclaredThroughRemappingAsync()
    {
        const string Source = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Value {
                public int32 Number;
                [NoThrow] public Value(int32 number) { Number = number; }
                [NoThrow] public int32 Read() { return Number; }
            }
            """;
        var original = CompileLibrary(Source);
        var producer = ModuleCompiler.CreateProducer(original, ["Value.#ctor", "Value.Read"]);
        var reference = ModuleCompiler.CreateReference(producer);
        {var __nunitItems0 = reference.Functions;        Assert.Multiple(() => {
            foreach (var __nunitItem2 in __nunitItems0)
            {
                {
                    Assert.That(__nunitItem2.IsExternal, Is.True);
                    Assert.That(__nunitItem2.IsNoThrow, Is.True);
                }
            }
        });}
        var consumerSource = """
            [NoThrow] int32 Main() {
                Value value = new Value(42);
                return value.Read();
            }
            """;
        var consumer = CompileConsumer(consumerSource, producer);
        Assert.That(consumer.Functions[consumer.EntryPoint].IsNoThrow, Is.True);
        var artifact = ModuleCompiler.CreateConsumer(consumer, producer);
        var linked = ModuleCompiler.Link(artifact, producer);
        {var __nunitItems1 = linked.Functions.Where(static function => function.Name is "Value.#ctor" or "Value.Read");        Assert.Multiple(() => { foreach (var __nunitItem3 in __nunitItems1) { Assert.That(__nunitItem3.IsNoThrow, Is.True); } });}
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task StrengtheningPromiseAllowsSameSemanticAndNativeConsumerWithNewBodyAsync()
    {
        var mayThrow = Producer("public int32 Compute() { return 42; }");
        var noThrow = Producer("[NoThrow] public int32 Compute() { return 43; }");
        var reference = ModuleCompiler.CreateReference(mayThrow);
        Assert.That(global::System.Linq.Enumerable.Single(reference.Functions).IsNoThrow, Is.False);
        var consumer = CompileConsumer("int32 Main() { return Compute(); }", mayThrow);
        var semantic = ModuleCompiler.CreateConsumer(consumer, mayThrow);
        var originalSemantic = (byte[])semantic.Clone();
        var nativeProducer = NativeModuleCompiler.CreateProducer(CompileLibrary("public int32 Compute() { return 42; }"),
            ["Compute"], "Effects", NativeRuntimePolicy.FailFast);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(CompileLibrary("[NoThrow] public int32 Compute() { return 43; }"),
            ["Compute"], "Effects", NativeRuntimePolicy.FailFast);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumer, nativeProducer, NativeRuntimePolicy.FailFast);
        var unchanged = (byte[])nativeConsumer.Clone();
        var imageHash = NativeModuleCompiler.Inspect(nativeConsumer).ImageSha256;
        foreach (var (module, package, result) in new[] { (mayThrow, nativeProducer, 42), (noThrow, nativeReplacement, 43) })
        {
            Assert.That(await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(semantic, module),
                NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(result));
            Assert.That(semantic, Is.EqualTo(originalSemantic));
            NativeModuleCompiler.Validate(nativeConsumer, package);
            Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, package,
                TimeSpan.FromSeconds(10)).ConfigureAwait(true), Is.EqualTo(result));
            Assert.That(nativeConsumer, Is.EqualTo(unchanged));
            Assert.That(NativeModuleCompiler.Inspect(nativeConsumer).ImageSha256, Is.EqualTo(imageHash));
        }
    }

    [Test]
    public void ConsumerCreationChecksDeclaredPromiseBeforeNormalizingImports()
    {
        var noThrow = Producer("[NoThrow] public int32 Compute() { return 42; }");
        var mayThrow = Producer("public int32 Compute() { return 42; }");
        var consumer = CompileConsumer("int32 Main() { return Compute(); }", noThrow);
        var index = consumer.Functions.IndexOf(consumer.Functions.Single(static function => function.IsExternal));
        var weakenedDeclaration = consumer with {
            Functions = consumer.Functions.SetItem(index, consumer.Functions[index] with { IsNoThrow = false }),
        };
        var normalized = ModuleCompiler.CreateConsumer(weakenedDeclaration, noThrow);
        Assert.That(ModuleCompiler.Link(normalized, noThrow).Functions[index].IsNoThrow, Is.True);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateConsumer(consumer, mayThrow))!;
    }

    [Test]
    public void WeakeningNoThrowPromiseRejectsSafeUnmarkedBodyBeforeExecution()
    {
        var noThrow = Producer("[NoThrow] public int32 Compute() { return 42; }");
        var mayThrow = Producer("public int32 Compute() { return 43; }");
        var consumer = CompileConsumer("[NoThrow] int32 Forward() { return Compute(); } "
            + "int32 Main() { return Forward(); }", noThrow);
        var semantic = ModuleCompiler.CreateConsumer(consumer, noThrow);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(semantic, mayThrow))!;
        var nativeProducer = NativeModuleCompiler.CreateProducer(CompileLibrary("[NoThrow] public int32 Compute() { return 42; }"),
            ["Compute"], "Effects", NativeRuntimePolicy.FailFast);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(CompileLibrary("public int32 Compute() { return 43; }"),
            ["Compute"], "Effects", NativeRuntimePolicy.FailFast);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumer, nativeProducer, NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(nativeConsumer, nativeReplacement))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.RunAsync(nativeConsumer, nativeReplacement,
            TimeSpan.FromSeconds(10)).GetAwaiter().GetResult())!;
    }

    [Test]
    public void ProducerBodyCannotForgeNoThrowOrOldModuleVersion()
    {
        var unsafeProgram = CompileLibrary("public int32 Compute(int32 value) { return value + 1; }");
        var forged = unsafeProgram with {
            Functions = unsafeProgram.Functions.SetItem(0, unsafeProgram.Functions[0] with { IsNoThrow = true }),
        };
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(forged, ["Compute"]))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateProducer(forged,
            ["Compute"], "Effects", NativeRuntimePolicy.FailFast))!;
        var artifact = Producer("[NoThrow] public int32 Compute() { return 42; }");
        artifact[4] = 7;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(artifact))!;
    }

    [Test]
    public void PureUnfoldedCheckedArithmeticCannotForgeNoThrowInPersistedProducer()
    {
        var folded = CompileLibrary("[NoThrow] public int32 Compute() { return 1 + 2; }");
        var function = global::System.Linq.Enumerable.Single(folded.Functions);
        var statement = ((global::System.Func<BoundReturn>)(() => { var __nunitActual4 = global::System.Linq.Enumerable.Single(function.Body.Statements); Assert.That(__nunitActual4, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual4!; }))();
        var constant = ((global::System.Func<BoundConstant>)(() => { var __nunitActual5 = statement.Value; Assert.That(__nunitActual5, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual5!; }))();
        var arithmetic = new BoundBinary(TokenKind.Plus,
            new BoundConstant(1, PrimitiveType.Int32, constant.Span),
            new BoundConstant(2, PrimitiveType.Int32, constant.Span),
            PrimitiveType.Int32, constant.Span);
        var forged = folded with {
            Functions = folded.Functions.SetItem(0, function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(0, statement with { Value = arithmetic }),
                },
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(forged))!;
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(forged, ["Compute"]))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateProducer(forged,
            ["Compute"], "Effects", NativeRuntimePolicy.FailFast))!;
    }

    private static async Task<int> ExecuteAsync(byte[] image)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"nothrow-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start native worker.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(true);
                throw;
            }
            return process.ExitCode;
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
