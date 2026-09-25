// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class AggregateAbiTests
{
    private const string Declarations = """
        struct Three {
            public bool A; public bool B; public bool C;
            public Three(bool a, bool b, bool c) { A = a; B = b; C = c; }
        }
        struct Six {
            public char16 A; public char16 B; public char16 C;
            public Six(char16 a, char16 b, char16 c) { A = a; B = b; C = c; }
        }
        struct Twelve {
            public int32 A; public int32 B; public int32 C;
            public Twelve(int32 a, int32 b, int32 c) { A = a; B = b; C = c; }
        }
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
            return a.A + a.B + (int32)c.C + d.Value.C + e + f.C;
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

    [Theory]
    [InlineData("return Mix(new Twelve(1, 2, 7), new Three(true, false, true), new Six((char16)4, (char16)5, (char16)6), Make(9), 14, new Twelve(1, 2, 7));", 42)]
    [InlineData("Wide w = Echo(Make(1), Make(2), Make(3), Make(4), Recurse(5)); return w.Value.A + w.Value.B + w.Value.C + w.Tail;", 42)]
    [InlineData("mut Wide w = Make(1); Wide copy = w; w.Value = new Twelve(1, 2, 3); w.Tail = 40; return copy.Value.A + copy.Value.B + copy.Value.C + copy.Tail + w.Value.A + w.Value.B + w.Value.C;", 40)]
    [InlineData("require Forward(true, out var w); if (Forward(false, out var ignored)) { return -1; } return w.Value.A + w.Value.B + w.Value.C + w.Tail;", 42)]
    [InlineData("Three t = new Three(true, false, true); Six s = new Six((char16)7, (char16)8, (char16)9); if (t.A && !t.B && t.C) { return (int32)s.A + (int32)s.B + (int32)s.C; } return -1;", 24)]
    [InlineData("Wide w = Echo(Make(1 / 0), Make(2147483647 + 1), Make(3), Make(4), Make(5)); return w.Tail;", unchecked((int)0xE0540002))]
    public async Task IndirectAggregatesExecuteThroughAotAndCliJitAsync(string body, int expected)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Declarations + " int32 Main() { " + body + " }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(3, program.Structures[0].Size);
        Assert.Equal(6, program.Structures[1].Size);
        Assert.Equal(12, program.Structures[2].Size);
        Assert.Equal(16, program.Structures[3].Size);
        await ExecuteBothAsync(program, expected).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    public async Task EveryNaturalAggregateSizeCopiesReturnsAndPublishesExactlyAsync(int size)
    {
        var source = new StringBuilder("struct Bytes {");
        for (var index = 0; index < size; index++)
        {
            _ = source.Append(index == 0 ? "public mut bool F" : "public bool F").Append(index).Append(';');
        }
        _ = source.Append("public Bytes() {");
        for (var index = 0; index < size; index++)
        {
            _ = source.Append('F').Append(index).Append(index == size - 1 ? "=true;" : "=false;");
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(size, program.Structures[0].Size);
        await ExecuteBothAsync(program, 42).ConfigureAwait(true);
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
            var producerBytes = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(unused.Program), ["Unused"]);
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
            File.Delete(executable);
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
        Assert.Equal(expected, process.ExitCode);
        Assert.Empty(await error.ConfigureAwait(true));
    }
}
