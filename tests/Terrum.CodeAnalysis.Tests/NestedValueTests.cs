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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NestedValueTests
{
    private const string Values = """
        public struct Pair {
            public mut Unit First;
            public mut Unit Second;
            public Pair(int32 first, int32 second) {
                this.First = new Unit(first);
                this.Second = new Unit(second);
            }
        }
        public struct Unit {
            public mut int32 Value;
            public Unit(int32 value) { this.Value = value; }
        }
        """;
    [TestCase("Pair pair = new Pair(17, 25); return pair.First.Value + pair.Second.Value;",42)]
    [TestCase("mut Pair pair = new Pair(1, 2); pair.First = new Unit(40); return pair.First.Value + pair.Second.Value;",42)]
    [TestCase("Pair pair = new Pair(-2147483648, 2147483647); return pair.First.Value + pair.Second.Value;",-1)]
    [TestCase("Pair pair = new Pair(1, 77); Pair copy = pair; return copy.Second.Value;",77)]
    [TestCase("mut Pair pair = new Pair(1, 2); pair.First.Value = 40; return pair.First.Value + pair.Second.Value;",42)]
    [TestCase("mut Pair pair = new Pair(1, 2); (pair.Second).Value = 41; return pair.First.Value + pair.Second.Value;",42)]
    [TestCase("Pair pair = new Pair(1, 2); mut Pair copy = pair; copy.Second.Value = 39; return pair.Second.Value + copy.First.Value + copy.Second.Value;",42)]
    public async Task NestedValuesExecuteThroughAotAndJitAsync(string body, int expected)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + "int32 Main() { " + body + " }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteBothAsync(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), expected).ConfigureAwait(true);
    }

    [Test]
    public async Task ConstructorsAndOutputsMutateOnlyInitializedNestedValuesAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + """
            public struct Box {
                public mut Pair Value;
                public int32 Tail;
                public Box() {
                    Value = new Pair(1, 2);
                    Value.First.Value = 19;
                    this.Value.Second.Value = 21;
                    Tail = 2;
                }
            }
            bool Try(bool success, out Box box) {
                if (!success) { return false; }
                box = new Box();
                box.Value.First.Value = box.Value.First.Value + 1;
                box.Value.Second.Value = 20;
                return true;
            }
            bool Forward(bool success, out Box box) { return Try(success, out box); }
            int32 Main() {
                require Forward(true, out var value);
                mut Box copy = value;
                copy.Value.Second = new Unit(0);
                require value.Value.Second.Value == 20;
                if (Forward(false, out var invalid)) { return 99; }
                return value.Value.First.Value + value.Value.Second.Value + value.Tail;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteBothAsync(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), 42).ConfigureAwait(true);
    }

    [Test]
    public async Task NestedCodeUnitAndBooleanWritesPreserveAdjacentFieldsAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Bits {
                public mut bool First; public mut char16 Middle; public mut bool Last;
                public Bits() { First = true; Middle = char16(1); Last = false; }
            }
            struct Box {
                public bool Head; public mut Bits Value; public char16 Tail;
                public Box() { Head = true; Value = new Bits(); Tail = char16(2); }
            }
            int32 Main() {
                mut Box box = new Box();
                box.Value.Middle = char16(40);
                box.Value.Last = true;
                box.Value.First = false;
                require box.Head && !box.Value.First && box.Value.Last;
                return int32(box.Value.Middle) + int32(box.Tail);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteBothAsync(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), 42).ConfigureAwait(true);
    }
    [TestCase("Pair value = new Pair(1, 2); value.First.Value = 3;")]
    [TestCase("new Pair(1, 2).First.Value = 3;")]
    [TestCase("mut Pair value = new Pair(1, 2); value.Missing.Value = 3;")]
    [TestCase("mut Pair value = new Pair(1, 2); value.First.Value.Missing = 3;")]
    public void InvalidNestedPlacesAreRejected(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + "int32 Main() { " + body + " return 0; }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("Value.First.Value = 1; Value.Second.Value = 2;")]
    [TestCase("Value.First = new Unit(1); Value.Second = new Unit(2);")]
    public void NestedWritesCannotPublishIncompleteValues(string constructor)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + """
            struct Box { public mut Pair Value; public Box() {
            """ + constructor + """
            } }
            int32 Main() { Box value = new Box(); return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void RightHandSideCannotVacateTheContainingValue()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Inner { public mut bool Flag; public int32 Other;
                public Inner() { Flag = true; Other = 42; } }
            struct Outer { public mut Inner Value; public Outer() { Value = new Inner(); } }
            bool Clear(out Outer value) { return false; }
            int32 Main() {
                mut Outer value = new Outer();
                value.Value.Flag = Clear(out value);
                return 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("Value = 1; return 0;")]
    [TestCase("return Value;")]
    public void StaticMembersCannotUseAnImplicitInstanceField(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "struct S { public int32 Value; public static int32 Read() { " + body
            + " } } int32 Main() { return S.Read(); }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void ForgedNestedFieldPathsCannotReachNativeCode()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values
            + "int32 Main() { mut Pair value = new Pair(1, 2); value.First.Value = 40; return value.First.Value; }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        var function = program.Functions[program.EntryPoint];
        var assignment = ((global::System.Func<BoundFieldAssignment>)(() => { var __nunitActual4 = function.Body.Statements[1]; Assert.That(__nunitActual4, Is.TypeOf<BoundFieldAssignment>()); return (BoundFieldAssignment)__nunitActual4!; }))();
        foreach (var path in new ImmutableArray<int>[] { default, [-1], [int.MaxValue], [0, 0] })
        {
            var malformed = function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(1, assignment with { NestedFields = path }),
                },
            };
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(
                program with { Functions = program.Functions.SetItem(program.EntryPoint, malformed) },
                NativeRuntimePolicy.FailFast))!;
        }
    }

    [Test]
    public async Task NestedEightByteFieldsAndConditionalOutputsPreserveBothHalvesAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + """
            struct Box {
                public mut Pair Value;
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteBothAsync(((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(), 42).ConfigureAwait(true);
    }

    [Test]
    public async Task NestedAlignmentIsNotAggregateSizeAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Outer {
                public char16 Prefix;
                public Inner Value;
                public char16 Suffix;
                public Outer() {
                    this.Prefix = char16(1);
                    this.Value = new Inner();
                    this.Suffix = char16(2);
                }
            }
            struct Inner {
                public char16 Unit;
                public bool Valid;
                public Inner() { this.Unit = char16(39); this.Valid = true; }
            }
            int32 Main() {
                Outer value = new Outer();
                require value.Value.Valid;
                return int32(value.Prefix) + int32(value.Value.Unit) + int32(value.Suffix);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
        Assert.That(program.Structures[0].Size, Is.EqualTo(8));
        Assert.That(program.Structures[0].Fields[1].Offset, Is.EqualTo(2));
        Assert.That(program.Structures[0].Fields[2].Offset, Is.EqualTo(6));
        await ExecuteBothAsync(program, 42).ConfigureAwait(true);
    }
    [TestCase("struct A { public A Value; }")]
    [TestCase("struct A { public B Value; } struct B { public A Value; }")]
    [TestCase("struct A { public Missing Value; }")]
    [TestCase("struct A { public int32 X; public int32 Y; } struct B { public A First; public A Second; public bool TooLarge; }")]
    [TestCase("struct A { public B Value; public A() { } } struct B { public int32 X; }")]
    [TestCase("struct A { public B Value; public A() { Value = Value; } } struct B { public int32 X; }")]
    public void InvalidNestedLayoutsAndInitializationAreSourceErrors(string declarations)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declarations + " int32 Main() { return 0; }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void ForgedRecursiveAndMisalignedLayoutsCannotReachEmission()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + "int32 Main() { return 0; }"));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))();
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
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(malformed, NativeRuntimePolicy.FailFast))!;
        }
    }

    [Test]
    public async Task NestedDefinitionsResolveAcrossSourceSnapshotsAsync()
    {
        var outer = SyntaxTree.Parse("using Model; struct Wrapper { public Unit Value; "
            + "public Wrapper() { Value = new Unit(42); } } "
            + "int32 Main() { Wrapper value = new Wrapper(); return value.Value.Value; }", "outer.terrum");
        var inner = SyntaxTree.Parse("namespace Model; public struct Unit { public int32 Value; "
            + "public Unit(int32 value) { Value = value; } }", "inner.terrum");
        var compilation = Compilation.Create([outer, inner]);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteBothAsync(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = compilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(), 42).ConfigureAwait(true);
    }
    [TestCase("Outer value = new Outer(); return value.Value.Secret;")]
    [TestCase("Outer value = new Outer(); value.Value = new Inner(); return 0;")]
    [TestCase("mut Outer value = new Outer(); value.Value.Secret = 1; return 0;")]
    public void NestingDoesNotBypassVisibilityOrMutation(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Inner { private mut int32 Secret; public Inner() { Secret = 42; } }
            struct Outer { public mut Inner Value; public Outer() { Value = new Inner(); } }
            """ + "int32 Main() { " + body + " }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void ModuleExportsRequireTransitivePublicValueLayouts()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public struct Outer { public Hidden Value; public Outer() { Value = new Hidden(); } }
            struct Hidden { public int32 Value; public Hidden() { Value = 42; } }
            public Outer Make() { return new Outer(); }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(), ["Make"]))!;
    }
    [TestCase(false)]
    [TestCase(true)]
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
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code is "TRM3005" or "TRM3016"),Is.True);
    }

    [Test]
    public async Task NestedModuleLayoutsRemapTransitivelyAndRejectReplacementChangesAsync()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(Values + "public Pair Make() { return new Pair(19, 23); }"));
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = library.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))();
        var producer = ModuleCompiler.CreateProducer(program, ["Make"]);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(reference.Structures.Length, Is.EqualTo(2));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Pair value = Make(); return value.First.Value + value.Second.Value; }"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = consumerCompilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))(), producer);
        var reordered = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Hidden { public int32 X; public Hidden() { X = 0; } }
            public struct Unit { public mut int32 Value; public Unit(int32 value) { Value = value; } }
            public struct Pair { public mut Unit First; public mut Unit Second;
                public Pair(int32 a, int32 b) { First = new Unit(a); Second = new Unit(b); } }
            public Pair Make() { Hidden hidden = new Hidden(); return new Pair(20, 22); }
            """));
        Assert.That(reordered.HasErrors, Is.False, string.Join(Environment.NewLine, reordered.Diagnostics));
        var replacement = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = reordered.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))(), ["Make"]);
        await ExecuteBothAsync(ModuleCompiler.Link(consumer, replacement), 42).ConfigureAwait(true);
        var changed = Compilation.CreateLibrary(SyntaxTree.Parse(Values.Replace("public mut int32 Value;",
            "public mut uint32 Value;", StringComparison.Ordinal).Replace("this.Value = value;",
            "this.Value = uint32(value);", StringComparison.Ordinal) + "public Pair Make() { return new Pair(1, 2); }"));
        Assert.That(changed.HasErrors, Is.False, string.Join(Environment.NewLine, changed.Diagnostics));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer,
            ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = changed.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))(), ["Make"])))!;
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
            var producerBytes = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = unused.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))(), ["Unused"]);
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
