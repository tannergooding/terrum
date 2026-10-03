// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TryFinallyTests
{
    [Theory]
    [InlineData("int32 Main() { try { return 42; } finally { return 0; } }", "TRM3019")]
    [InlineData("int32 Main() { try { return 42; } finally { while (true) { } } }", "TRM3020")]
    [InlineData("int32 Main() { try { return 42; } finally { if (Maybe()) { Stop(); } } } [NoThrow] bool Maybe() { return true; } [NoThrow] [DoesNotReturn] void Stop() { while (true) { } }", "TRM3020")]
    [InlineData("int32 Main() { try { return 42; } finally { while (Maybe()) { } } } [NoThrow] bool Maybe() { return false; }", "TRM3020")]
    [InlineData("int32 Main() { try { return 42; } finally { require false; } }", "TRM3017")]
    [InlineData("int32 Main() { try { return 42; } finally { Unsafe(); } } void Unsafe() { }", "TRM3017")]
    [InlineData("int32 Main() { try { return 42; } finally { mut int32 x = 1 / 0; } }", "TRM3017")]
    [InlineData("int32 Main() { try { return 42; } catch { return 0; } }", "TRM")]
    [InlineData("int32 Main() { try { return 42; } }", "TRM1001")]
    [InlineData("bool Take(out int32 x) { x = 42; try { return true; } finally { Clear(out x); } } [NoThrow] bool Clear(out int32 x) { return false; } int32 Main() { return 0; }", "TRM3016")]
    [InlineData("bool Fill(bool publish, init ref<int32> value) { if (publish) { value = 42; return true; } return false; } bool ReadAfterCall(bool publish) { init int32 value; try { return Fill(publish, init value); } finally { int32 observed = value; } } int32 Main() { return 0; }", "TRM3016")]
    [InlineData("int32 Main() { try { return 42; } finally { use = 0; } int32 use = 1; }", "TRM3006")]
    [InlineData("int32 Main() { try { int32 hidden = 42; } finally { int32 copy = hidden; } return 0; }", "TRM3006")]
    [InlineData("bool Take(bool choose, out int32 x) { try { if (choose) { x = 42; return true; } } finally { int32 copy = x; } return false; } int32 Main() { return 0; }", "TRM3016")]
    [InlineData("bool Take(out int32 x) { try { return true; } finally { int32 copy = x; } } int32 Main() { return 0; }", "TRM3016")]
    [InlineData("struct V { public int32 Value; public V() { Value = 1; } } int32 Main() { mut V value = new V(); try { return 42; } finally { value.Value = 0; } }", "TRM3008")]
    public void InvalidCleanupIsRejected(string source, string diagnostic)
    {
        var result = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, item => item.Code.StartsWith(diagnostic, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("return")]
    [InlineData("null-body")]
    [InlineData("null-finally")]
    [InlineData("throwing-cleanup")]
    public void MutableIrCannotForgeUnsafeCleanup(string kind)
    {
        var valid = Compile("int32 Main() { try { return 42; } finally { } }");
        var function = valid.Functions[valid.EntryPoint];
        var region = Assert.IsType<BoundTryFinally>(function.Body.Statements[0]);
        var forged = kind switch {
            "return" => region with {
                Finally = new BoundBlock([new BoundReturn(new BoundConstant(0,
                    PrimitiveType.Int32, default), default)], default),
            },
            "null-body" => region with { Body = null! },
            "null-finally" => region with { Finally = null! },
            "throwing-cleanup" => region with {
                Finally = new BoundBlock([new BoundRequire(new BoundConstant(0, PrimitiveType.Bool, default),
                    default)], default),
            },
            _ => throw new InvalidOperationException("Unknown forged region."),
        };
        var invalid = valid with {
            Functions = valid.Functions.SetItem(valid.EntryPoint,
            function with { Body = function.Body with { Statements = [forged] } })
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalid));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(invalid, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(invalid, ["Main"]));
    }

    [Fact]
    public async Task StructuredRegionsSurviveSourceIndependentModuleRoundTripAsync()
    {
        var producer = CompileLibrary("""
            [NoThrow] public void Touch() { }
            public int32 Compute(int32 x) {
                try { return x; } finally { Touch(); }
            }
            """);
        var bytes = ModuleCompiler.CreateProducer(producer, ["Compute"]);
        var reference = ModuleCompiler.CreateReference(bytes);
        Assert.Contains(reference.Functions, function => function.Name == "Compute");
        var consumer = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Compute(42); }"), reference);
        Assert.False(consumer.HasErrors, string.Join(Environment.NewLine, consumer.Diagnostics));
        var image = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumer.Program), bytes);
        var linked = ModuleCompiler.Link(image, bytes);
        Assert.Contains(linked.Functions, function => function.Name == "Compute"
            && function.Body.Statements[0] is BoundTryFinally);
        var executable = Path.Combine(AppContext.BaseDirectory, $"try-finally-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(executable, NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast));
            using var process = Process.Start(new ProcessStartInfo(executable) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start native worker.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            Assert.Equal(42, process.ExitCode);
        }
        finally
        {
            File.Delete(executable);
        }
        bytes[4] = 9;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(bytes));
    }

    private static BoundProgram Compile(string source)
    {
        var result = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        return Assert.IsType<BoundProgram>(result.Program);
    }

    private static BoundProgram CompileLibrary(string source)
    {
        var result = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        return Assert.IsType<BoundProgram>(result.Program);
    }
}
