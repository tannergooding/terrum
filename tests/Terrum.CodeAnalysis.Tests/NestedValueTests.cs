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

public sealed class NestedValueTests
{
    private const string Values = """
        public struct Pair {
            public Unit First;
            public Unit Second;
            public Pair(int32 first, int32 second) {
                this.First = new Unit(first);
                this.Second = new Unit(second);
            }
        }
        public struct Unit {
            public int32 Value;
            public Unit(int32 value) { this.Value = value; }
        }
        """;

    [Theory]
    [InlineData("Pair pair = new Pair(17, 25); return pair.First.Value + pair.Second.Value;", 42)]
    [InlineData("mut Pair pair = new Pair(1, 2); pair.First = new Unit(40); return pair.First.Value + pair.Second.Value;", 42)]
    [InlineData("Pair pair = new Pair(-2147483648, 2147483647); return pair.First.Value + pair.Second.Value;", -1)]
    [InlineData("Pair pair = new Pair(1, 77); Pair copy = pair; return copy.Second.Value;", 77)]
    public async Task NestedValuesExecuteThroughAotAndJitAsync(string body, int expected)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + "int32 Main() { " + body + " }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteBothAsync(Assert.IsType<BoundProgram>(compilation.Program), expected).ConfigureAwait(true);
    }

    [Fact]
    public async Task NestedEightByteFieldsAndConditionalOutputsPreserveBothHalvesAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + """
            struct Box {
                public Pair Value;
                public Box(int32 first, int32 second) { this.Value = new Pair(first, second); }
            }
            bool Try(bool success, out Box result) {
                if (!success) { return false; }
                result = new Box(19, 23);
                result.Value = new Pair(20, 22);
                return true;
            }
            bool Forward(bool success, out Box result) { return Try(success, out result); }
            Box Echo(Box a, Box b, Box c, Box d, Box e) { return e; }
            int32 Main() {
                require Forward(true, out var value);
                if (Forward(false, out var invalid)) { return 99; }
                Box copy = Echo(value, value, value, value, value);
                return copy.Value.First.Value + copy.Value.Second.Value;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteBothAsync(Assert.IsType<BoundProgram>(compilation.Program), 42).ConfigureAwait(true);
    }

    [Fact]
    public async Task NestedAlignmentIsNotAggregateSizeAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Outer {
                public char16 Prefix;
                public Inner Value;
                public char16 Suffix;
                public Outer() {
                    this.Prefix = (char16)1;
                    this.Value = new Inner();
                    this.Suffix = (char16)2;
                }
            }
            struct Inner {
                public char16 Unit;
                public bool Valid;
                public Inner() { this.Unit = (char16)39; this.Valid = true; }
            }
            int32 Main() {
                Outer value = new Outer();
                require value.Value.Valid;
                return (int32)value.Prefix + (int32)value.Value.Unit + (int32)value.Suffix;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(8, program.Structures[0].Size);
        Assert.Equal(2, program.Structures[0].Fields[1].Offset);
        Assert.Equal(6, program.Structures[0].Fields[2].Offset);
        await ExecuteBothAsync(program, 42).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("struct A { public A Value; }")]
    [InlineData("struct A { public B Value; } struct B { public A Value; }")]
    [InlineData("struct A { public Missing Value; }")]
    [InlineData("struct A { public int32 X; public int32 Y; } struct B { public A First; public A Second; public bool TooLarge; }")]
    [InlineData("struct A { public B Value; public A() { } } struct B { public int32 X; }")]
    [InlineData("struct A { public B Value; public A() { Value = Value; } } struct B { public int32 X; }")]
    public void InvalidNestedLayoutsAndInitializationAreSourceErrors(string declarations)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declarations + " int32 Main() { return 0; }"));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void ForgedRecursiveAndMisalignedLayoutsCannotReachEmission()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + "int32 Main() { return 0; }"));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var pair = program.Structures[0];
        foreach (var field in new[] {
            pair.Fields[0] with { Type = (PrimitiveType)100 },
            pair.Fields[0] with { Type = (PrimitiveType)int.MinValue },
            pair.Fields[0] with { Type = (PrimitiveType)int.MaxValue },
            pair.Fields[0] with { Offset = 1 },
            pair.Fields[0] with { Size = 8 },
        })
        {
            var malformed = program with {
                Structures = program.Structures.SetItem(0, pair with { Fields = pair.Fields.SetItem(0, field) }),
            };
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(malformed, NativeRuntimePolicy.FailFast));
        }
    }

    [Fact]
    public async Task NestedDefinitionsResolveAcrossSourceSnapshotsAsync()
    {
        var outer = SyntaxTree.Parse("using Model; struct Wrapper { public Unit Value; "
            + "public Wrapper() { Value = new Unit(42); } } "
            + "int32 Main() { Wrapper value = new Wrapper(); return value.Value.Value; }", "outer.terrum");
        var inner = SyntaxTree.Parse("namespace Model; public struct Unit { public int32 Value; "
            + "public Unit(int32 value) { Value = value; } }", "inner.terrum");
        var compilation = Compilation.Create([outer, inner]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteBothAsync(Assert.IsType<BoundProgram>(compilation.Program), 42).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("Outer value = new Outer(); return value.Value.Secret;")]
    [InlineData("Outer value = new Outer(); value.Value = new Inner(); return 0;")]
    [InlineData("mut Outer value = new Outer(); value.Value.Secret = 1; return 0;")]
    public void NestingDoesNotBypassVisibilityOrMutation(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Inner { private int32 Secret; public Inner() { Secret = 42; } }
            struct Outer { public Inner Value; public Outer() { Value = new Inner(); } }
            """ + "int32 Main() { " + body + " }"));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void ModuleExportsRequireTransitivePublicValueLayouts()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public struct Outer { public Hidden Value; public Outer() { Value = new Hidden(); } }
            struct Hidden { public int32 Value; public Hidden() { Value = 42; } }
            public Outer Make() { return new Outer(); }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program), ["Make"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcessiveValueContainmentDepthProducesDiagnostics(bool reverse)
    {
        var source = new StringBuilder();
        for (var index = 0; index < 130; index++)
        {
            var number = reverse ? 129 - index : index;
            _ = source.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"struct S{number} {{ public S{number + 1} Value; }} ");
        }
        _ = source.Append("struct S130 { public int32 Value; } int32 Main() { return 0; }");
        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code is "TRM3005" or "TRM3016");
    }

    [Fact]
    public async Task NestedModuleLayoutsRemapTransitivelyAndRejectReplacementChangesAsync()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(Values + "public Pair Make() { return new Pair(19, 23); }"));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var program = Assert.IsType<BoundProgram>(library.Program);
        var producer = ModuleCompiler.CreateProducer(program, ["Make"]);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Equal(2, reference.Structures.Length);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Pair value = Make(); return value.First.Value + value.Second.Value; }"), reference);
        Assert.False(consumerCompilation.HasErrors, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var reordered = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Hidden { public int32 X; public Hidden() { X = 0; } }
            public struct Unit { public int32 Value; public Unit(int32 value) { Value = value; } }
            public struct Pair { public Unit First; public Unit Second;
                public Pair(int32 a, int32 b) { First = new Unit(a); Second = new Unit(b); } }
            public Pair Make() { Hidden hidden = new Hidden(); return new Pair(20, 22); }
            """));
        Assert.False(reordered.HasErrors, string.Join(Environment.NewLine, reordered.Diagnostics));
        var replacement = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(reordered.Program), ["Make"]);
        await ExecuteBothAsync(ModuleCompiler.Link(consumer, replacement), 42).ConfigureAwait(true);
        var changed = Compilation.CreateLibrary(SyntaxTree.Parse(Values.Replace("public int32 Value;",
            "public uint32 Value;", StringComparison.Ordinal).Replace("this.Value = value;",
            "this.Value = (uint32)value;", StringComparison.Ordinal) + "public Pair Make() { return new Pair(1, 2); }"));
        Assert.False(changed.HasErrors, string.Join(Environment.NewLine, changed.Diagnostics));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer,
            ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(changed.Program), ["Make"])));
    }

    private static async Task ExecuteBothAsync(BoundProgram program, int expected)
    {
        var prefix = Path.Combine(AppContext.BaseDirectory, $"nested-{Guid.NewGuid():N}");
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
