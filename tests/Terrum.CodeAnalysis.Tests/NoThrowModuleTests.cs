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
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NoThrowModuleTests
{
    private static BoundProgram CompileLibrary(string source)
    {
        var result = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        return Assert.IsType<BoundProgram>(result.Program);
    }

    private static byte[] Producer(string source) =>
        ModuleCompiler.CreateProducer(CompileLibrary(source), ["Compute"]);

    private static BoundProgram CompileConsumer(string source, byte[] producer)
    {
        var result = Compilation.Create(SyntaxTree.Parse(source), ModuleCompiler.CreateReference(producer));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        return Assert.IsType<BoundProgram>(result.Program);
    }

    [Fact]
    public async Task NoThrowConstructorAndImportedFunctionRemainDeclaredThroughRemappingAsync()
    {
        const string Source = """
            public struct Value {
                public int32 Number;
                [NoThrow] public Value(int32 number) { Number = number; }
                [NoThrow] public int32 Read() { return Number; }
            }
            """;
        var original = CompileLibrary(Source);
        var producer = ModuleCompiler.CreateProducer(original, ["Value.#ctor", "Value.Read"]);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.All(reference.Functions, static function => {
            Assert.True(function.IsExternal);
            Assert.True(function.IsNoThrow);
        });
        var consumerSource = """
            [NoThrow] int32 Main() {
                Value value = new Value(42);
                return value.Read();
            }
            """;
        var consumer = CompileConsumer(consumerSource, producer);
        Assert.True(consumer.Functions[consumer.EntryPoint].IsNoThrow);
        var artifact = ModuleCompiler.CreateConsumer(consumer, producer);
        var linked = ModuleCompiler.Link(artifact, producer);
        Assert.All(linked.Functions.Where(static function => function.Name is "Value.#ctor" or "Value.Read"),
            static function => Assert.True(function.IsNoThrow));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public async Task StrengtheningPromiseAllowsSameSemanticAndNativeConsumerWithNewBodyAsync()
    {
        var mayThrow = Producer("public int32 Compute() { return 42; }");
        var noThrow = Producer("[NoThrow] public int32 Compute() { return 43; }");
        var reference = ModuleCompiler.CreateReference(mayThrow);
        Assert.False(Assert.Single(reference.Functions).IsNoThrow);
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
            Assert.Equal(result, await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(semantic, module),
                NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
            Assert.Equal(originalSemantic, semantic);
            NativeModuleCompiler.Validate(nativeConsumer, package);
            Assert.Equal(result, await NativeModuleCompiler.RunAsync(nativeConsumer, package,
                TimeSpan.FromSeconds(10)).ConfigureAwait(true));
            Assert.Equal(unchanged, nativeConsumer);
            Assert.Equal(imageHash, NativeModuleCompiler.Inspect(nativeConsumer).ImageSha256);
        }
    }

    [Fact]
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
        Assert.True(ModuleCompiler.Link(normalized, noThrow).Functions[index].IsNoThrow);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateConsumer(consumer, mayThrow));
    }

    [Fact]
    public void WeakeningNoThrowPromiseRejectsSafeUnmarkedBodyBeforeExecution()
    {
        var noThrow = Producer("[NoThrow] public int32 Compute() { return 42; }");
        var mayThrow = Producer("public int32 Compute() { return 43; }");
        var consumer = CompileConsumer("[NoThrow] int32 Forward() { return Compute(); } "
            + "int32 Main() { return Forward(); }", noThrow);
        var semantic = ModuleCompiler.CreateConsumer(consumer, noThrow);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(semantic, mayThrow));
        var nativeProducer = NativeModuleCompiler.CreateProducer(CompileLibrary("[NoThrow] public int32 Compute() { return 42; }"),
            ["Compute"], "Effects", NativeRuntimePolicy.FailFast);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(CompileLibrary("public int32 Compute() { return 43; }"),
            ["Compute"], "Effects", NativeRuntimePolicy.FailFast);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumer, nativeProducer, NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(nativeConsumer, nativeReplacement));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.RunAsync(nativeConsumer, nativeReplacement,
            TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
    }

    [Fact]
    public void ProducerBodyCannotForgeNoThrowOrOldModuleVersion()
    {
        var unsafeProgram = CompileLibrary("public int32 Compute(int32 value) { return value + 1; }");
        var forged = unsafeProgram with {
            Functions = unsafeProgram.Functions.SetItem(0, unsafeProgram.Functions[0] with { IsNoThrow = true }),
        };
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(forged, ["Compute"]));
        _ = Assert.Throws<ArgumentException>(() => NativeModuleCompiler.CreateProducer(forged,
            ["Compute"], "Effects", NativeRuntimePolicy.FailFast));
        var artifact = Producer("[NoThrow] public int32 Compute() { return 42; }");
        artifact[4] = 7;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(artifact));
    }

    [Fact]
    public void PureUnfoldedCheckedArithmeticCannotForgeNoThrowInPersistedProducer()
    {
        var folded = CompileLibrary("[NoThrow] public int32 Compute() { return 1 + 2; }");
        var function = Assert.Single(folded.Functions);
        var statement = Assert.IsType<BoundReturn>(Assert.Single(function.Body.Statements));
        var constant = Assert.IsType<BoundConstant>(statement.Value);
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
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(forged));
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(forged, ["Compute"]));
        _ = Assert.Throws<ArgumentException>(() => NativeModuleCompiler.CreateProducer(forged,
            ["Compute"], "Effects", NativeRuntimePolicy.FailFast));
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
            File.Delete(path);
        }
    }
}
