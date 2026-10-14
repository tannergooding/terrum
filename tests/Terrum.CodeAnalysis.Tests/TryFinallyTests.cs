// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TryFinallyTests
{
    [TestCase("int32 Main() { try { return 42; } finally { return 0; } }","TRM3019")]
    [TestCase("int32 Main() { try { return 42; } finally { while (true) { } } }","TRM3020")]
    [TestCase("int32 Main() { try { return 42; } finally { if (Maybe()) { Stop(); } } } [NoThrow] bool Maybe() { return true; } [NoThrow] [DoesNotReturn] void Stop() { while (true) { } }","TRM3020")]
    [TestCase("int32 Main() { try { return 42; } finally { while (Maybe()) { } } } [NoThrow] bool Maybe() { return false; }","TRM3020")]
    [TestCase("int32 Main() { try { return 42; } finally { require false; } }","TRM3017")]
    [TestCase("int32 Main() { try { return 42; } finally { Unsafe(); } } void Unsafe() { }","TRM3017")]
    [TestCase("int32 Main() { try { return 42; } finally { mut int32 x = 1 / 0; } }","TRM3017")]
    [TestCase("int32 Main() { try { return 42; } catch { return 0; } }","TRM")]
    [TestCase("int32 Main() { try { return 42; } }","TRM1001")]
    [TestCase("bool Take(out int32 x) { x = 42; try { return true; } finally { Clear(out x); } } [NoThrow] bool Clear(out int32 x) { return false; } int32 Main() { return 0; }","TRM3016")]
    [TestCase("bool Fill(bool publish, init ref<int32> value) { if (publish) { value = 42; return true; } return false; } bool ReadAfterCall(bool publish) { init int32 value; try { return Fill(publish, init value); } finally { int32 observed = value; } } int32 Main() { return 0; }","TRM3016")]
    [TestCase("int32 Main() { try { return 42; } finally { use = 0; } int32 use = 1; }","TRM3006")]
    [TestCase("int32 Main() { try { int32 hidden = 42; } finally { int32 copy = hidden; } return 0; }","TRM3006")]
    [TestCase("bool Take(bool choose, out int32 x) { try { if (choose) { x = 42; return true; } } finally { int32 copy = x; } return false; } int32 Main() { return 0; }","TRM3016")]
    [TestCase("bool Take(out int32 x) { try { return true; } finally { int32 copy = x; } } int32 Main() { return 0; }","TRM3016")]
    [TestCase("struct V { public int32 Value; public V() { Value = 1; } } int32 Main() { mut V value = new V(); try { return 42; } finally { value.Value = 0; } }","TRM3008")]
    public void InvalidCleanupIsRejected(string source, string diagnostic)
    {
        var result = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(result.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(result.Diagnostics,item => item.Code.StartsWith(diagnostic, StringComparison.Ordinal)),Is.True);
    }
    [TestCase("return")]
    [TestCase("null-body")]
    [TestCase("null-finally")]
    [TestCase("throwing-cleanup")]
    public void MutableIrCannotForgeUnsafeCleanup(string kind)
    {
        var valid = Compile("int32 Main() { try { return 42; } finally { } }");
        var function = valid.Functions[valid.EntryPoint];
        var region = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual0 = function.Body.Statements[0]; Assert.That(__nunitActual0, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual0!; }))();
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
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalid))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(invalid, NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(invalid, ["Main"]))!;
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(reference.Functions,function => function.Name == "Compute"),Is.True);
        var consumer = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Compute(42); }"), reference);
        Assert.That(consumer.HasErrors, Is.False, string.Join(Environment.NewLine, consumer.Diagnostics));
        var image = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumer.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), bytes);
        var linked = ModuleCompiler.Link(image, bytes);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,function => function.Name == "Compute"
            && function.Body.Statements[0] is BoundTryFinally),Is.True);
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
            Assert.That(process.ExitCode, Is.EqualTo(42));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
        bytes[4] = 9;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(bytes))!;
    }

    private static BoundProgram Compile(string source)
    {
        var result = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = result.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
    }

    private static BoundProgram CompileLibrary(string source)
    {
        var result = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = result.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
    }
}
