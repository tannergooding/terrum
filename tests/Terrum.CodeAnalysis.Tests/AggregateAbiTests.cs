// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class AggregateAbiTests
{
    private const string Declarations = """
        [Blittable] [Layout(LayoutKind.Sequential)]
        struct Three {
            public bool A; public bool B; public bool C;
            public Three(bool a, bool b, bool c) { A = a; B = b; C = c; }
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
        struct Six {
            public char16 A; public char16 B; public char16 C;
            public Six(char16 a, char16 b, char16 c) { A = a; B = b; C = c; }
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
        struct Twelve {
            public int32 A; public int32 B; public int32 C;
            public Twelve(int32 a, int32 b, int32 c) { A = a; B = b; C = c; }
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
        struct Wide {
            public mut Twelve Value; public mut int32 Tail;
            public Wide(int32 tail) { Value = new Twelve(10, 11, 12); Tail = tail; }
        }
        Wide Make(int32 tail) { return new Wide(tail); }
        Wide Echo(Wide a, Wide b, Wide c, Wide d, Wide e) { return e; }
        Wide Recurse(int32 n) {
            if (n == 0) { return Make(9); }
            return Recurse(n - 1);
        }
        int32 Mix(Twelve a, Three b, Six c, Wide d, int32 e, Twelve f) {
            if (!b.A || b.B || !b.C) { return -1; }
            return a.A + a.B + int32(c.C) + d.Value.C + e + f.C;
        }
        bool Try(bool success, out Wide result) {
            if (!success) { return false; }
            result = Make(1);
            result.Value = new Twelve(10, 11, 12);
            result.Tail = 9;
            return true;
        }
        bool Forward(bool success, out Wide result) { return Try(success, out result); }
        """;
    [TestCase("return Mix(new Twelve(1, 2, 7), new Three(true, false, true), new Six(char16(4), char16(5), char16(6)), Make(9), 14, new Twelve(1, 2, 7));",42)]
    [TestCase("Wide w = Echo(Make(1), Make(2), Make(3), Make(4), Recurse(5)); return w.Value.A + w.Value.B + w.Value.C + w.Tail;",42)]
    [TestCase("mut Wide w = Make(1); Wide copy = w; w.Value = new Twelve(1, 2, 3); w.Tail = 40; return copy.Value.A + copy.Value.B + copy.Value.C + copy.Tail + w.Value.A + w.Value.B + w.Value.C;",40)]
    [TestCase("require Forward(true, out var w); if (Forward(false, out var ignored)) { return -1; } return w.Value.A + w.Value.B + w.Value.C + w.Tail;",42)]
    [TestCase("Three t = new Three(true, false, true); Six s = new Six(char16(7), char16(8), char16(9)); if (t.A && !t.B && t.C) { return int32(s.A) + int32(s.B) + int32(s.C); } return -1;",24)]
    [TestCase("Wide w = Echo(Make(1 / 0), Make(2147483647 + 1), Make(3), Make(4), Make(5)); return w.Tail;",unchecked((int)0xE0540002))]
    public async Task IndirectAggregatesExecuteThroughAotAndCliJitAsync(string body, int expected)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Declarations + " int32 Main() { " + body + " }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(program.Structures[0].Size, Is.EqualTo(3));
        Assert.That(program.Structures[1].Size, Is.EqualTo(6));
        Assert.That(program.Structures[2].Size, Is.EqualTo(12));
        Assert.That(program.Structures[3].Size, Is.EqualTo(16));
        await ExecuteBothAsync(program, expected).ConfigureAwait(true);
    }
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(10)]
    [TestCase(11)]
    [TestCase(12)]
    [TestCase(13)]
    [TestCase(14)]
    [TestCase(15)]
    [TestCase(16)]
    public async Task EveryNaturalAggregateSizeCopiesReturnsAndPublishesExactlyAsync(int size)
    {
        var source = new StringBuilder("[Blittable] [Layout(LayoutKind.Sequential)] struct Bytes {");
        for (var index = 0; index < size; index++)
        {
            _ = source.Append(index == 0 ? "public mut bool F" : "public bool F").Append(index).Append(';');
        }
        _ = source.Append("public Bytes() {");
        for (var index = 0; index < size; index++)
        {
            _ = source.Append('F').Append(index).Append(index == size - 1 ? " = true;" : " = false;");
        }
        _ = source.Append("} } Bytes Produce() { return new Bytes(); }")
            .Append("Bytes Echo(Bytes value) { return value; }")
            .Append("bool Try(bool success, out Bytes value) { if (!success) { return false; } value = Echo(Produce()); return true; }")
            .Append("bool Forward(bool success, out Bytes value) { return Try(success, out value); }")
            .Append("int32 Main() { Bytes copy = Echo(Produce()); require Forward(true, out var value);")
            .Append("if (Forward(false, out var ignored)) { return -1; }")
            .Append("if (!copy.F").Append(size - 1).Append(" || !value.F").Append(size - 1).Append(") { return -2; }");
        if (size != 1)
        {
            _ = source.Append("mut Bytes replacement = copy; replacement.F0 = true;")
                .Append("if (copy.F0 || value.F0 || !replacement.F0) { return -3; }");
        }
        _ = source.Append("return 42; }");
        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(program.Structures[0].Size, Is.EqualTo(size));
        await ExecuteBothAsync(program, 42).ConfigureAwait(true);
    }

    [TestCase(24)]
    [TestCase(32)]
    [TestCase(64)]
    [TestCase(128)]
    public async Task LargeAggregateScratchSupportsOutputsAndCleanupReturnsAsync(int size)
    {
        var fieldCount = size / sizeof(long);
        var lastField = fieldCount - 1;
        var source = new StringBuilder("[Blittable] [Layout(LayoutKind.Sequential)] struct Bytes {");
        for (var index = 0; index < fieldCount; index++)
        {
            _ = source.Append("public int64 F").Append(index).Append(';');
        }
        _ = source.Append("public Bytes() {");
        for (var index = 0; index < fieldCount; index++)
        {
            _ = source.Append('F').Append(index).Append(index == lastField ? " = 42;" : " = 0;");
        }
        _ = source.Append("} } [NoThrow] void Touch() {}")
            .Append("Bytes ReturnThroughFinally() { try { return new Bytes(); } finally { Touch(); } }")
            .Append("Bytes Fill(out Bytes output) { output = new Bytes(); return ReturnThroughFinally(); }")
            .Append("int32 Main() { Bytes result = Fill(out var output);")
            .Append("if (result.F").Append(lastField).Append(" != 42 || output.F").Append(lastField)
            .Append(" != 42) { return -1; } return 42; }");
        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        Assert.That(program.Structures[0].Size, Is.EqualTo(size));
        await ExecuteBothAsync(program, 42).ConfigureAwait(true);
    }

    [Test]
    public async Task LargeUniqueReplacementAndCleanupUseFullAggregateScratchAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            unique struct Owner {
                public int64 First;
                public int64 Second;
                public int64 Third;
                public int64 Fourth;
                public Owner(int64 value) {
                    First = value;
                    Second = value + 1;
                    Third = value + 2;
                    Fourth = value + 3;
                }
                [NoThrow] [Cleanup] public void Close() {
                    if (Fourth == 4) { Host.Trace[0] = Host.Trace[0] ^ 4; }
                    else if (Fourth == 43) { Host.Trace[0] = Host.Trace[0] ^ 43; }
                    else { Host.Trace[0] = -100; }
                }
                [NoThrow] public int64 Read() { return Fourth; }
            }
            int32 Main() {
                {
                    mut Owner owner = new Owner(1);
                    owner = new Owner(40);
                    if (owner.Read() != 43) { return -1; }
                }
                return Host.Trace[0];
            }
            """), [new HostBuffer("Trace", PrimitiveType.Int32, [0], true)]);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        Assert.That(program.Structures.Single(static structure => structure.Name == "Owner").Size, Is.EqualTo(32));
        await ExecuteAotAsync(program, 47).ConfigureAwait(true);
    }

    private static async Task ExecuteAotAsync(BoundProgram program, int expected)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, $"aggregate-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(executable,
                NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
            await RunAsync(executable, [], expected).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    private static async Task ExecuteBothAsync(BoundProgram program, int expected)
    {
        var prefix = Path.Combine(AppContext.BaseDirectory, $"aggregate-{Guid.NewGuid():N}");
        var executable = prefix + ".exe";
        var consumer = prefix + ".consumer.trm";
        var producer = prefix + ".producer.trm";
        try
        {
            await File.WriteAllBytesAsync(executable, NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
            await RunAsync(executable, [], expected).ConfigureAwait(true);
            var unused = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Unused() { return 0; }"));
            var producerBytes = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = unused.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), ["Unused"]);
            await File.WriteAllBytesAsync(producer, producerBytes).ConfigureAwait(true);
            await File.WriteAllBytesAsync(consumer, ModuleCompiler.CreateConsumer(program, producerBytes)).ConfigureAwait(true);
            var framework = new DirectoryInfo(AppContext.BaseDirectory);
            var configuration = framework.Parent?.Name ?? throw new InvalidOperationException("Missing configuration.");
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
            var cli = Path.Combine(root, "artifacts", "bin", "sources", "terrumc", configuration, framework.Name, "terrumc.dll");
            await RunAsync("dotnet", [cli, "run-module", consumer, producer, "--runtime", "fail-fast"], expected).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
            File.Delete(consumer);
            File.Delete(producer);
        }
    }

    private static async Task RunAsync(string executable, ImmutableArray<string> arguments, int expected)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start native worker.");
        var error = process.StandardError.ReadToEndAsync();
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
        Assert.That(process.ExitCode, Is.EqualTo(expected));
        Assert.That(await error.ConfigureAwait(true), Is.Empty);
    }
}
