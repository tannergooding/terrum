// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
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

public sealed class UpdateExpressionTests
{
    [Test]
    public void ScannerAndParserKeepDistinctUpdateTokensAndPrecedence()
    {
        const string text = "++value; value--; value++ + ++value;";
        var lexed = Lexer.Lex(SourceText.From(text));
        Assert.That((int)TokenKind.PlusPlus, Is.EqualTo(86));
        Assert.That((int)TokenKind.MinusMinus, Is.EqualTo(87));
        Assert.That(lexed.Tokens.Select(token => token.Kind), Is.EqualTo([TokenKind.PlusPlus, TokenKind.Identifier, TokenKind.Semicolon, TokenKind.Identifier,
            TokenKind.MinusMinus, TokenKind.Semicolon, TokenKind.Identifier, TokenKind.PlusPlus,
            TokenKind.Plus, TokenKind.PlusPlus, TokenKind.Identifier, TokenKind.Semicolon,
            TokenKind.EndOfFile]));
        var operators = ScannerHost.CreateBuffers(SourceText.From(text))
            .Single(buffer => buffer.Name == "Operators").Values;
        Assert.That(operators, Does.Contain(86));
        Assert.That(operators, Does.Contain(87));
        var tree = SyntaxTree.Parse("int32 Main() { mut int32 value = 1; " + text + " return value; }");
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.UpdateExpression), Is.EqualTo(4));
        Assert.That(global::System.Linq.Enumerable.Any(SyntaxTree.Parse("int32 Main() { mut int32 value = 1; value+++value; return 0; }").Diagnostics,diagnostic => diagnostic.Code == "TRM0011"),Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void PrefixAndPostfixReturnSelectedValuesAndCommitInOrder(NativeRuntimePolicy policy)
    {
        var source = """
            int32 Main() {
                mut int32 value = 40;
                int32 first = value++;
                int32 second = ++value;
                int32 third = value--;
                int32 fourth = --value;
                if (first != 40 || second != 42 || third != 42 || fourth != 40 || value != 40) {
                    return 1;
                }
                return 42;
            }
            """;
        var program = Compile(source, policy);
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
    }
    [TestCase("int32","2147483647","int32.MinValue","int32.MaxValue")]
    [TestCase("uint32","0xFFFFFFFF","uint32.MinValue","uint32.MaxValue")]
    [TestCase("int64","0x7FFFFFFFFFFFFFFF","int64.MinValue","int64.MaxValue")]
    [TestCase("uint64","0xFFFFFFFFFFFFFFFF","uint64.MinValue","uint64.MaxValue")]
    public void EveryIntegerWidthRejectsOverflowWithoutCommitting(string type, string max,
        string minExpression, string maxExpression)
    {
        var source = $$"""
            int32 Main() {
                mut {{type}} number = {{type}}({{max}});
                unsafe {
                    try { number++; return 1; }
                    catch { if (number != {{maxExpression}}) { return 2; } }
                }
                number = {{minExpression}};
                unsafe {
                    try { --number; return 3; }
                    catch { if (number != {{minExpression}}) { return 4; } }
                }
                return 42;
            }
            """;
        var program = Compile(source, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void CompactFieldsComputedClassReceiversAndHostBufferPlacesAreWritable(NativeRuntimePolicy policy)
    {
        var source = """
            struct Inner {
                public mut int32 Value;
                public Inner(int32 value) { Value = value; }
            }
            struct Outer {
                public mut Inner Child;
                public Outer(int32 value) { Child = new Inner(value); }
            }
            mut class Box {
                public mut Outer Data;
                public Box(int32 value) { Data = new Outer(value); }
            }
            mut class Counter {
                public mut int32 Visits;
                public Counter() { Visits = 0; }
            }
            write Box Pick(write Counter count, write Box box) {
                count.Visits = count.Visits + 1;
                return box;
            }
            int32 Main() {
                mut Outer local = new Outer(39);
                require Box.TryCreate(40, out var box);
                require Counter.TryCreate(out var count);
                int32 old = local.Child.Value++;
                int32 next = ++Pick(count, box).Data.Child.Value;
                if (old != 39 || local.Child.Value != 40 || next != 41
                    || box.Data.Child.Value != 41 || count.Visits != 1) { return 1; }
                return 42;
            }
            """;
        Assert.That(NativeJit.Execute(Compile(source, policy), policy), Is.EqualTo(42));
    }

    [Test]
    public void CheckedFieldOverflowPreservesExistingValueAndPriorReceiverEffects()
    {
        const string source = """
            mut class Box {
                public mut int32 Value;
                public Box(int32 value) { Value = value; }
            }
            mut class Counter {
                public mut int32 Visits;
                public Counter() { Visits = 0; }
            }
            write Box Pick(write Counter count, write Box box) {
                count.Visits = count.Visits + 1;
                return box;
            }
            int32 Main() {
                require Box.TryCreate(int32.MaxValue, out var box);
                require Counter.TryCreate(out var count);
                unsafe {
                    try { Pick(count, box).Value++; return 1; }
                    catch {
                        if (box.Value != int32.MaxValue || count.Visits != 1) { return 2; }
                        return 42;
                    }
                }
            }
            """;
        Assert.That(NativeJit.Execute(Compile(source, NativeRuntimePolicy.CheckedExceptions),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void WritableHostBufferIndexesEvaluateOnce(NativeRuntimePolicy policy)
    {
        const string source = """
            mut class Counter {
                public mut int32 Visits;
                public Counter() { Visits = 0; }
            }
            int32 NextIndex(write Counter counter) {
                counter.Visits = counter.Visits + 1;
                return 0;
            }
            int32 Main() {
                require Counter.TryCreate(out var counter);
                int32 before = Host.Trace[NextIndex(counter)]++;
                if (before != 40 || counter.Visits != 1 || Host.Trace[0] != 41) { return 1; }
                int32 after = ++Host.Trace[NextIndex(counter)];
                if (after != 42 || counter.Visits != 2) { return 2; }
                return Host.Trace[0];
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [40], true)], (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), policy), Is.EqualTo(42));
    }

    [Test]
    public void FailedComputedReceiverUpdateRetiresItsRoot()
    {
        const string source = """
            mut class Box {
                public mut int32 Value;
                public Box() { Value = int32.MaxValue; }
            }
            write Box Create() {
                require Box.TryCreate(out var box);
                return box;
            }
            int32 Main() {
                unsafe {
                    try { Create().Value++; return 1; }
                    catch {
                        require ManagedHeap.Collect() == 1;
                        return 42;
                    }
                }
            }
            """;
        Assert.That(NativeJit.Execute(Compile(source, NativeRuntimePolicy.CheckedExceptions),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void OwningRootScalarUpdateDoesNotLoseTheOwnerOnCheckedFailure()
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
                    try { owner.Value++; return 1; }
                    catch {
                        if (owner.Value != int32.MaxValue || Host.Trace[0] != 0) { return 2; }
                    }
                }
                owner.Value--;
                return owner.Value == int32.MaxValue - 1 ? 42 : 3;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0], true)],
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }
    [TestCase("int32 value = 1; value++;","TRM3008")]
    [TestCase("init int32 value; ++value;","TRM")]
    [TestCase("int32 value = 1; ++(value + 1);","TRM3008")]
    [TestCase("mut char16 value = char16(1); value++;","TRM3007")]
    public void UnsupportedPlacesAreDiagnosed(string statements, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { "
            + statements + " return 42; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code.StartsWith(code, StringComparison.Ordinal)),Is.True);
    }

    [Test]
    public void NoThrowCannotPromiseAnUnprovenUpdate()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] int32 Next(int32 initial) {
                mut int32 value = initial;
                return value++;
            }
            int32 Main() { return 42; }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == "TRM3017"),Is.True);
    }
    [TestCase("holder.Value++;")]
    [TestCase("holder.Item++;")]
    [TestCase("holder.Computed++;")]
    public void ReadonlyClassFieldsAndGettersAreNotPlaces(string statement)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Holder {
                public int32 Value;
                public mut int32 Item;
                public Holder(int32 value) { Value = value; Item = value; }
                public int32 Computed { get { return Item; } }
            }
            int32 Main() {
                require Holder.TryCreate(1, out var holder);
            """
            + statement + """

                return 42;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == "TRM3008"),Is.True);
    }

    [Test]
    public void WritableClassDoesNotOverrideImmutableFieldAuthority()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Holder {
                public int32 Value;
                public Holder(int32 value) { Value = value; }
            }
            int32 Main() {
                require Holder.TryCreate(1, out var holder);
                holder.Value++;
                return 42;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == "TRM3008"),Is.True);
    }

    [Test]
    public void ConstEvalUpdatesOnlyScalarSlotsAndChecksOverflow()
    {
        var valid = Compile("""
            [ConstEval] int32 Calculate() {
                mut int32 value = 40;
                int32 old = value++;
                return old + ++value - 40;
            }
            int32 Main() { const int32 result = Calculate(); return result; }
            """, NativeRuntimePolicy.FailFast);
        var entry = valid.Functions[valid.EntryPoint];
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual3 = ((global::System.Func<BoundLocal>)(() => { var __nunitActual2 = entry.Body.Statements[0]; Assert.That(__nunitActual2, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual2!; }))().Initializer; Assert.That(__nunitActual3, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual3!; }))().Value, Is.EqualTo(42));
        var overflow = Compilation.Create(SyntaxTree.Parse("""
            [ConstEval] int32 Calculate() { mut int32 value = int32.MaxValue; return value++; }
            int32 Main() { const int32 result = Calculate(); return result; }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(overflow.Diagnostics,item => item.Code == "TRM3024"),Is.True);
    }

    [Test]
    public void InvalidIrAndHistoricalHeaderRejectUpdates()
    {
        var source = "int32 Main() { mut int32 value = 41; return ++value; }";
        var program = Compile(source, NativeRuntimePolicy.FailFast);
        var entry = program.Functions[program.EntryPoint];
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual4 = entry.Body.Statements[1]; Assert.That(__nunitActual4, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual4!; }))();
        var update = ((global::System.Func<BoundUpdate>)(() => { var __nunitActual5 = result.Value; Assert.That(__nunitActual5, Is.TypeOf<BoundUpdate>()); return (BoundUpdate)__nunitActual5!; }))();
        foreach (var forged in new[] {
            update with { Operator = TokenKind.Plus },
            update with { Place = new BoundConstant(41, PrimitiveType.Int32, update.Span) },
            update with { Type = PrimitiveType.UInt32 },
        })
        {
            var function = entry with {
                Body = entry.Body with {
                    Statements = entry.Body.Statements.SetItem(1, result with { Value = forged }),
                }
            };
            Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
                Functions = program.Functions.SetItem(program.EntryPoint, function),
            }));
        }
        var immutable = Compile("int32 Main() { int32 value = 41; return value; }",
            NativeRuntimePolicy.FailFast);
        var immutableEntry = immutable.Functions[immutable.EntryPoint];
        var forgedRead = new BoundUpdate(TokenKind.PlusPlus,
            new BoundRead(0, PrimitiveType.Int32, immutableEntry.Span), true,
            PrimitiveType.Int32, immutableEntry.Span);
        var immutableReturn = ((global::System.Func<BoundReturn>)(() => { var __nunitActual6 = immutableEntry.Body.Statements[1]; Assert.That(__nunitActual6, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual6!; }))();
        var forgedFunction = immutableEntry with {
            Body = immutableEntry.Body with {
                Statements = immutableEntry.Body.Statements.SetItem(1,
                immutableReturn with { Value = forgedRead }),
            }
        };
        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(immutable with {
            Functions = immutable.Functions.SetItem(immutable.EntryPoint, forgedFunction),
        }));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Next(int32 input) { mut int32 value = input; return value++; }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), ["Next"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        var old = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(old.AsSpan(4, 4), 42);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(old));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void SemanticConsumerSurvivesCompatibleUpdateBodyReplacement(NativeRuntimePolicy policy)
    {
        const string library = """
            public int32 Next() { mut int32 value = 41; return ++value; }
            """;
        var original = ModuleCompiler.CreateProducer(CompileLibrary(library, policy), ["Next"]);
        var client = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Next(); }"),
            ModuleCompiler.CreateReference(original), (BoundRuntimePolicy)policy);
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = client.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(), original);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, original), policy), Is.EqualTo(42));
        var replacement = ModuleCompiler.CreateProducer(CompileLibrary(
            library.Replace("= 41;", "= 42;", StringComparison.Ordinal), policy), ["Next"]);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), policy), Is.EqualTo(43));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task SourceIndependentConsumersPermitCompatibleUpdateReplacement(NativeRuntimePolicy policy)
    {
        const string library = """
            mut class Counter {
                public mut int32 Value;
                public Counter(int32 initial) { Value = initial; }
            }
            public int32 Next() {
                require Counter.TryCreate(41, out var counter);
                return ++counter.Value;
            }
            """;
        var producer = NativeModuleCompiler.CreateProducer(CompileLibrary(library, policy),
            ["Next"], "Updates", policy);
        var client = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Next(); }"),
            NativeModuleCompiler.CreateGraphReference([producer]), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = client.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(), [producer], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer, [producer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        var replacement = NativeModuleCompiler.CreateProducer(CompileLibrary(
            library.Replace("TryCreate(41", "TryCreate(42", StringComparison.Ordinal), policy), ["Next"], "Updates", policy);
        NativeModuleCompiler.ValidateGraph(consumer, [replacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer, [replacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(43));
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
