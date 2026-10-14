// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CompoundAssignmentTests
{
    [Test]
    public void SpellingsAreAtomicAndNestedGenericClosersRemainUsable()
    {
        const string spellings = "+= -= *= /= %= &= |= ^= <<= >>= >>>= << >> >>>";
        var kinds = Lexer.Lex(SourceText.From(spellings)).Tokens.Select(token => token.Kind).ToArray();
        Assert.That(kinds, Is.EqualTo([
            TokenKind.PlusEquals, TokenKind.MinusEquals, TokenKind.StarEquals,
            TokenKind.SlashEquals, TokenKind.PercentEquals, TokenKind.AmpersandEquals,
            TokenKind.PipeEquals, TokenKind.CaretEquals, TokenKind.ShiftLeftEquals,
            TokenKind.ShiftRightEquals, TokenKind.ShiftRightUnsignedEquals,
            TokenKind.ShiftLeft, TokenKind.ShiftRight, TokenKind.ShiftRightUnsigned,
            TokenKind.EndOfFile,
        ]));
        var operators = ScannerHost.CreateBuffers(SourceText.From(spellings))
            .Single(buffer => buffer.Name == "Operators").Values;
        foreach (var kind in kinds[..^1])
        {
            Assert.That(operators, Does.Contain((int)kind));
        }
        Assert.That(SyntaxTree.Parse("""
            interface I<T> { }
            void F(Box<Box<int32>> value) { }
            void G(Box<Box<Box<int32>>> value) { }
            int32 Main() { mut int32 value = 1; value <<= 2; return value; }
            """).HasErrors, Is.False);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void OperatorsAndForIteratorExecuteInOrder(NativeRuntimePolicy policy)
    {
        var program = Compile("""
            int32 Main() {
                mut int32 value = 40;
                value += 4; value -= 2; value *= 3; value /= 3; value %= 40;
                value &= 63; value |= 32; value ^= 32;
                value <<= 1; value >>= 1; value >>>= 1;
                for (mut int32 i = 0; i < 2; i += 1) { value += 20; }
                return value;
            }
            """, policy);
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(41));
    }
    [TestCase("int32",NativeRuntimePolicy.FailFast)]
    [TestCase("uint32",NativeRuntimePolicy.FailFast)]
    [TestCase("int64",NativeRuntimePolicy.FailFast)]
    [TestCase("uint64",NativeRuntimePolicy.FailFast)]
    [TestCase("int32",NativeRuntimePolicy.CheckedExceptions)]
    [TestCase("uint32",NativeRuntimePolicy.CheckedExceptions)]
    [TestCase("int64",NativeRuntimePolicy.CheckedExceptions)]
    [TestCase("uint64",NativeRuntimePolicy.CheckedExceptions)]
    public async Task EveryOperatorExecutesForEveryIntegerWidth(string type, NativeRuntimePolicy policy)
    {
        var program = Compile($$"""
            struct Pair {
                public mut {{type}} Value;
                public {{type}} Guard;
                public Pair() { Value = 40; Guard = 17; }
            }
            int32 Main() {
                mut Pair pair = new Pair();
                pair.Value += 4; pair.Value -= 2; pair.Value *= 3;
                pair.Value /= 3; pair.Value %= 40;
                pair.Value &= 63; pair.Value |= 32; pair.Value ^= 32;
                pair.Value <<= 1; pair.Value >>= 1; pair.Value >>>= 1;
                return pair.Value == 1 && pair.Guard == 17 ? 42 : 1;
            }
            """, policy);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(await StaticProtocolTests.RunAsync(NativeEmitter.Emit(program, policy))
            .ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase("<<",21)]
    [TestCase(">>",84)]
    [TestCase(">>>",84)]
    public void OrdinaryShiftSelectsNewSemanticVersion(string operation, int operand)
    {
        var library = CompileLibrary(
            $"public int64 Shift(int64 value, int32 count) {{ return value {operation} count; }}",
            NativeRuntimePolicy.FailFast);
        var producer = ModuleCompiler.CreateProducer(library, ["Shift"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        var stale = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(stale.AsSpan(4, 4), 63);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(stale));
        var client = Compilation.Create(SyntaxTree.Parse(
            $"int32 Main() {{ return int32(Shift(int64({operand}), 1)); }}"),
            ModuleCompiler.CreateReference(producer));
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = client.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase("var value = 1 << 2;")]
    [TestCase("mut int64 value = 1; value <<= int64(1);")]
    [TestCase("mut int32 value = 1; value += uint32(1);")]
    [TestCase("mut char16 value = 1; value += 1;")]
    [TestCase("mut bool value = true; value &= false;")]
    public void CompoundsAndShiftsDoNotPromoteOrDefaultTypes(string statement)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { " + statement + " return 42; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
    }

    [Test]
    public void GenericDefinitionCannotAcquireCompoundCapabilityFromASpecialization()
    {
        var invalid = Compilation.Create(SyntaxTree.Parse("""
            T Next<T>(T initial) where T : [Copy] {
                mut T value = initial;
                value += initial;
                return value;
            }
            int32 Main() { return Next(int32(21)); }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(invalid.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        var program = Compile("""
            T Next<T>(T initial) where T : [Copy] {
                mut int32 count = 0;
                count += 1;
                return initial;
            }
            [NoThrow] int32 Bits(int32 initial) {
                mut int32 value = initial;
                value &= 63; value |= 32; value ^= 0;
                return value;
            }
            int32 Main() { return Next(Bits(42)); }
            """, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }
    [TestCase("int32","int32.MaxValue","int32.MinValue")]
    [TestCase("uint32","uint32.MaxValue","uint32.MinValue")]
    [TestCase("int64","int64.MaxValue","int64.MinValue")]
    [TestCase("uint64","uint64.MaxValue","uint64.MinValue")]
    public void OverflowAndShiftCountsDoNotCommit(string type, string max, string min)
    {
        var source = $$"""
            int32 Main() {
                mut {{type}} value = {{max}};
                unsafe {
                    try { value += {{type}}(1); return 1; }
                    catch { if (value != {{max}}) { return 2; } }
                }
                value = {{min}};
                unsafe {
                    try { value -= {{type}}(1); return 3; }
                    catch { if (value != {{min}}) { return 4; } }
                }
                value = {{type}}(1);
                unsafe {
                    try { value <<= -1; return 5; }
                    catch { if (value != {{type}}(1)) { return 6; } }
                }
                unsafe {
                    try { value >>= {{(type is "int32" or "uint32" ? 32 : 64)}}; return 7; }
                    catch { if (value != {{type}}(1)) { return 8; } }
                }
                return 42;
            }
            """;
        var program = Compile(source, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void SignedShiftAndRemainderEdges()
    {
        var program = Compile("""
            int32 Main() {
                mut int32 left = 1;
                left <<= 31;
                if (left != int32.MinValue || left >> 31 != -1 || left >>> 31 != 1) { return 1; }
                mut int64 wide = int64.MinValue;
                wide %= int64(-1);
                if (wide != 0) { return 2; }
                wide = int64(-1);
                wide >>>= 63;
                if (wide != 1) { return 3; }
                return 42;
            }
            """, NativeRuntimePolicy.FailFast);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void ConstEvalUsesCheckedCompoundAndShiftOperations()
    {
        var program = Compile("""
            [ConstEval] int32 Calculate() {
                mut int32 value = 1;
                value <<= 5;
                value += 10;
                return value;
            }
            int32 Main() { const int32 value = Calculate(); return value; }
            """, NativeRuntimePolicy.FailFast);
        var entry = program.Functions[program.EntryPoint];
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual2 = ((global::System.Func<BoundLocal>)(() => { var __nunitActual1 = entry.Body.Statements[0]; Assert.That(__nunitActual1, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual1!; }))().Initializer; Assert.That(__nunitActual2, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual2!; }))().Value, Is.EqualTo(42));
        var invalid = Compilation.Create(SyntaxTree.Parse("""
            [ConstEval] int32 Calculate() {
                mut int32 value = 1;
                value <<= 32;
                return value;
            }
            int32 Main() { const int32 value = Calculate(); return value; }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(invalid.Diagnostics,diagnostic => diagnostic.Code == "TRM3024"),Is.True);
    }

    [Test]
    public void SignedMinimumDivisionFailsButRemainderSucceeds()
    {
        var program = Compile("""
            int32 Main() {
                mut int32 value = int32.MinValue;
                unsafe {
                    try { value /= -1; return 1; }
                    catch { if (value != int32.MinValue) { return 2; } }
                }
                value %= -1;
                return value == 0 ? 42 : 3;
            }
            """, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void CheckedFailurePreservesPriorRhsEffectsAndSnapshotsOldValue()
    {
        var program = Compile("""
            mut class Counter {
                public mut int32 Visits;
                public Counter() { Visits = 0; }
            }
            int32 Change(write Counter counter) { counter.Visits++; return 1; }
            int32 Main() {
                require Counter.TryCreate(out var counter);
                mut int32 value = int32.MaxValue;
                unsafe {
                    try { value += Change(counter); return 1; }
                    catch { if (value != int32.MaxValue || counter.Visits != 1) { return 2; } }
                }
                value = 1;
                value += ++value;
                return value + counter.Visits + 38;
            }
            """, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void ComputedClassReceiverIsEvaluatedOnceAndRetiredOnRhsFailure()
    {
        var program = Compile("""
            mut class Box {
                public mut int32 Value;
                public Box(int32 value) { Value = value; }
            }
            mut class Counter {
                public mut int32 Visits;
                public Counter() { Visits = 0; }
            }
            write Box Pick(write Counter counter, write Box box) {
                counter.Visits++;
                return box;
            }
            int32 Fail() { return 1 / 0; }
            int32 Main() {
                require Box.TryCreate(40, out var box);
                require Counter.TryCreate(out var counter);
                unsafe {
                    try { Pick(counter, box).Value += Fail(); return 1; }
                    catch { if (box.Value != 40 || counter.Visits != 1) { return 2; } }
                }
                Pick(counter, box).Value += 2;
                if (counter.Visits != 2) { return 3; }
                return box.Value;
            }
            """, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void HostBufferIndexIsEvaluatedOnce(NativeRuntimePolicy policy)
    {
        const string source = """
            mut class Counter {
                public mut int32 Visits;
                public Counter() { Visits = 0; }
            }
            int32 Index(write Counter counter) {
                counter.Visits++;
                return 0;
            }
            int32 Main() {
                require Counter.TryCreate(out var counter);
                Host.Trace[Index(counter)] += 2;
                if (counter.Visits != 1) { return 1; }
                return Host.Trace[0];
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [40], true)], (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), policy), Is.EqualTo(42));
    }

    [Test]
    public void TemporaryManagedReceiverRootIsReleasedAfterFailure()
    {
        var program = Compile("""
            mut class Box {
                public mut int32 Value;
                public Box() { Value = 1; }
            }
            write Box Create() {
                require Box.TryCreate(out var box);
                return box;
            }
            int32 Main() {
                unsafe {
                    try { Create().Value /= 0; return 1; }
                    catch {
                        require ManagedHeap.Collect() == 1;
                        return 42;
                    }
                }
            }
            """, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void OwningRootSurvivesCheckedCompoundFailureAndCleansOnExit()
    {
        const string source = """
            unique struct Owner {
                public mut int32 Value;
                public Owner(int32 initial) { Value = initial; }
                [NoThrow] [Cleanup] public void Close() { Host.Trace[0] = 1; }
            }
            int32 Main() {
                mut Owner owner = new Owner(int32.MaxValue);
                unsafe {
                    try { owner.Value += 1; return 1; }
                    catch {
                        if (owner.Value != int32.MaxValue || Host.Trace[0] != 0) { return 2; }
                    }
                }
                owner.Value -= 1;
                return owner.Value == int32.MaxValue - 1 ? 42 : 3;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0], true)],
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void UnsupportedNominalAndImmutableOrVacantPlacesDiagnose()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Number {
                public mut int32 Value;
                public Number(int32 value) { Value = value; }
                public static Number operator +(Number left, Number right) {
                    return new Number(left.Value + right.Value);
                }
            }
            int32 Main() {
                mut Number nominal = new Number(1);
                nominal += new Number(2);
                int32 immutable = 1;
                immutable += 1;
                init int32 vacant;
                vacant += 1;
                return 0;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3008"),Is.True);
        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void ThrowingCompoundOperationIsNotAdmittedInNoThrowBody()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] int32 Main() {
                mut int32 value = 1;
                value /= 1;
                return value;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3017"),Is.True);
    }

    [Test]
    public void WritableHandleCannotUpgradeReadonlyFieldAuthority()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Holder {
                public int32 Value;
                public Holder(int32 value) { Value = value; }
            }
            int32 Main() {
                require Holder.TryCreate(1, out var holder);
                holder.Value += 1;
                return holder.Value;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3008"),Is.True);
    }

    [Test]
    public void IrAndOldVersionRejectCompoundAssignment()
    {
        var library = CompileLibrary("""
            public int32 Next() { mut int32 value = 40; value += 2; return value; }
            """, NativeRuntimePolicy.FailFast);
        var producer = ModuleCompiler.CreateProducer(library, ["Next"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        var old = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(old.AsSpan(4, 4), 44);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(old));
        var function = library.Functions[0];
        var statement = ((global::System.Func<BoundExpressionStatement>)(() => { var __nunitActual5 = function.Body.Statements[1]; Assert.That(__nunitActual5, Is.TypeOf<BoundExpressionStatement>()); return (BoundExpressionStatement)__nunitActual5!; }))();
        var compound = ((global::System.Func<BoundCompoundAssignment>)(() => { var __nunitActual6 = statement.Expression; Assert.That(__nunitActual6, Is.TypeOf<BoundCompoundAssignment>()); return (BoundCompoundAssignment)__nunitActual6!; }))();
        foreach (var invalid in new[] {
            compound with { Operator = TokenKind.Plus },
            compound with { Place = new BoundConstant(40, PrimitiveType.Int32, compound.Span) },
            compound with { Right = new BoundConstant(2, PrimitiveType.UInt32, compound.Span) },
        })
        {
            var forged = library with {
                Functions = library.Functions.SetItem(0,
                function with {
                    Body = function.Body with {
                        Statements = function.Body.Statements.SetItem(1,
                    statement with { Expression = invalid })
                    }
                })
            };
            Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        }
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual7 = function.Body.Statements[2]; Assert.That(__nunitActual7, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual7!; }))();
        var nested = library with {
            Functions = library.Functions.SetItem(0,
            function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(2,
                result with {
                    Value = new BoundBinary(TokenKind.Plus, compound,
                    new BoundConstant(0, PrimitiveType.Int32, compound.Span),
                    PrimitiveType.Int32, compound.Span)
                })
                }
            })
        };
        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(nested));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void RetainedConsumerUsesCompatibleReplacement(NativeRuntimePolicy policy)
    {
        const string source = "public int32 Next() { mut int32 value = 40; value += 2; return value; }";
        var original = ModuleCompiler.CreateProducer(CompileLibrary(source, policy), ["Next"]);
        var client = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Next(); }"),
            ModuleCompiler.CreateReference(original), (BoundRuntimePolicy)policy);
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = client.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(), original);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, original), policy), Is.EqualTo(42));
        var replacement = ModuleCompiler.CreateProducer(CompileLibrary(
            source.Replace("= 40;", "= 41;", StringComparison.Ordinal), policy), ["Next"]);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), policy), Is.EqualTo(43));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NativeGraphRetainsAndReplacesCompoundBody(NativeRuntimePolicy policy)
    {
        const string source = "public int32 Next() { mut int32 value = 40; value += 2; return value; }";
        var producer = NativeModuleCompiler.CreateProducer(CompileLibrary(source, policy),
            ["Next"], "Compounds", policy);
        var client = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { mut int32 value = 0; value += Next(); return value; }
            """),
            NativeModuleCompiler.CreateGraphReference([producer]), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = client.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(), [producer], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer, [producer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var apphost = Path.Combine(root, "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(consumer, [producer],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
        var replacement = NativeModuleCompiler.CreateProducer(CompileLibrary(
            source.Replace("= 40;", "= 41;", StringComparison.Ordinal), policy), ["Next"], "Compounds", policy);
        NativeModuleCompiler.ValidateGraph(consumer, [replacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer, [replacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(43));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(consumer, [replacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(43));
    }

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))();
    }

    private static BoundProgram CompileLibrary(string source, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))();
    }
}
