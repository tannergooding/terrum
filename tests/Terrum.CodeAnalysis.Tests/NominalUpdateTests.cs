// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NominalUpdateTests
{
    private const string Operators = """
        struct Value {
            public mut int32 Number;
            public int32 Extra;
            public Value(int32 number) { Number = number; Extra = 0; }
            public static Value operator ++(Value value) { return new Value(value.Number + 1); }
            public static Value operator --(Value value) { return new Value(value.Number - 1); }
        }
        """;
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void DirectPrefixPostfixAndCompactFieldPaths(NativeRuntimePolicy policy)
    {
        var source = Operators + """
            struct Pair {
                public mut Value First;
                public mut Value Second;
                public Pair(int32 number) {
                    First = new Value(number);
                    Second = new Value(number + 1);
                }
            }
            mut class Box {
                public mut Pair Data;
                public Box(int32 number) { Data = new Pair(number); }
            }
            mut class Counter {
                public mut int32 Visits;
                public Counter() { Visits = 0; }
            }
            write Box Pick(write Counter counter, write Box box) {
                counter.Visits = counter.Visits + 1;
                return box;
            }
            int32 Main() {
                mut Value local = new Value(40);
                Value old = local++;
                Value next = ++local;
                mut Pair pair = new Pair(40);
                Value fieldOld = pair.First--;
                Value fieldNew = ++pair.Second;
                require Box.TryCreate(40, out var box);
                require Counter.TryCreate(out var counter);
                Value boxOld = Pick(counter, box).Data.First++;
                Value boxNew = --Pick(counter, box).Data.Second;
                if (old.Number != 40 || next.Number != 42 || local.Number != 42
                    || fieldOld.Number != 40 || pair.First.Number != 39
                    || fieldNew.Number != 42 || pair.Second.Number != 42
                    || boxOld.Number != 40 || boxNew.Number != 40
                    || box.Data.First.Number != 41 || box.Data.Second.Number != 40
                    || counter.Visits != 2) { return 1; }
                return 42;
            }
            """;
        var program = Compile(source, policy);
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task SixteenByteOperandReturnsSavedPostfixAndReplacementPrefix(NativeRuntimePolicy policy)
    {
        const string source = """
            struct Wide {
                public mut int64 Low;
                public mut int64 High;
                public Wide(int64 low, int64 high) { Low = low; High = high; }
                public static Wide operator ++(Wide value) {
                    return new Wide(value.Low + 1, value.High + 2);
                }
                public static Wide operator --(Wide value) {
                    return new Wide(value.Low - 1, value.High - 2);
                }
            }
            int32 Main() {
                mut Wide value = new Wide(40, 80);
                Wide old = value++;
                Wide next = ++value;
                Wide previous = value--;
                if (old.Low != 40 || old.High != 80 || next.Low != 42 || next.High != 84
                    || previous.Low != 42 || previous.High != 84
                    || value.Low != 41 || value.High != 82) { return 1; }
                return 42;
            }
            """;
        var program = Compile(source, policy);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(await StaticProtocolTests.RunAsync(NativeEmitter.Emit(program, policy))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public void ThrowingOperatorDoesNotCommitAndComputedReceiverEffectsRemain()
    {
        var source = Operators + """
            mut class Box {
                public mut Value Item;
                public Box() { Item = new Value(int32.MaxValue); }
            }
            mut class Counter {
                public mut int32 Visits;
                public Counter() { Visits = 0; }
            }
            write Box Pick(write Counter counter, write Box box) {
                counter.Visits = counter.Visits + 1;
                return box;
            }
            write Box Create(write Counter counter) {
                counter.Visits = counter.Visits + 1;
                require Box.TryCreate(out var box);
                return box;
            }
            int32 Main() {
                require Box.TryCreate(out var box);
                require Counter.TryCreate(out var counter);
                unsafe {
                    try { Pick(counter, box).Item++; return 1; }
                    catch {
                        if (counter.Visits != 1 || box.Item.Number != int32.MaxValue) { return 2; }
                    }
                    try { ++Create(counter).Item; return 3; }
                    catch {
                        if (counter.Visits != 2 || ManagedHeap.Collect() != 1) { return 4; }
                    }
                }
                return 42;
            }
            """;
        Assert.That(NativeJit.Execute(Compile(source, NativeRuntimePolicy.CheckedExceptions),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void ThrowingOperatorRetainsItsOwnSideEffectsWithoutCommitting()
    {
        const string source = """
            struct Value {
                public mut int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator ++(Value value) {
                    Host.Trace[0] = Host.Trace[0] + 1;
                    return new Value(value.Number + 1);
                }
            }
            int32 Main() {
                mut Value value = new Value(int32.MaxValue);
                unsafe {
                    try { value++; return 1; }
                    catch {
                        if (value.Number != int32.MaxValue || Host.Trace[0] != 1) { return 2; }
                    }
                }
                return 42;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0], true)],
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }
    [TestCase(false,NativeRuntimePolicy.FailFast)]
    [TestCase(false,NativeRuntimePolicy.CheckedExceptions)]
    [TestCase(true,NativeRuntimePolicy.FailFast)]
    [TestCase(true,NativeRuntimePolicy.CheckedExceptions)]
    public void ConstrainedGenericUpdateInfersExactWitness(bool self, NativeRuntimePolicy policy)
    {
        var protocol = self ? "interface Increment<this T, R>" : "interface Increment<T, R>";
        var conformance = self ? "Increment<Value>" : "Increment<Value, Value>";
        var constraint = self ? "Increment<T>" : "Increment<T, T>";
        var source = $$"""
            {{protocol}} { static R operator ++(T value); }
            struct Value : {{conformance}} {
                public mut int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator ++(Value value) { return new Value(value.Number + 1); }
            }
            T Next<T>(T value) where T : {{constraint}}, [Copy] {
                mut T result = value;
                result++;
                return ++result;
            }
            int32 Main() { return Next(new Value(40)).Number; }
            """;
        Assert.That(NativeJit.Execute(Compile(source, policy), policy), Is.EqualTo(42));
    }
    [TestCase("struct Value : Increment<Value, Value>","struct Value","TRM3007")]
    [TestCase("struct Value : Increment<Value, Value>","struct Value : Increment<Value, int32>","TRM3007")]
    [TestCase("where T : Increment<T, T>, [Copy]","where T : [Copy]","TRM3007")]
    [TestCase("where T : Increment<T, T>, [Copy]","where T : Increment<T, int32>, [Copy]","TRM3007")]
    [TestCase("public static Value operator ++(Value value)","public static int32 operator ++(Value value)","TRM3007")]
    [TestCase("public static Value operator ++(Value value)","private static Value operator ++(Value value)","TRM3007")]
    public void IncorrectWitnessesAndSignaturesAreRejected(string oldText, string newText, string code)
    {
        const string source = """
            interface Increment<T, R> { static R operator ++(T value); }
            struct Value : Increment<Value, Value> {
                public mut int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator ++(Value value) { return new Value(value.Number + 1); }
            }
            T Next<T>(T value) where T : Increment<T, T>, [Copy] {
                mut T result = value;
                return ++result;
            }
            int32 Main() { return Next(new Value(41)).Number; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(
            source.Replace(oldText, newText, StringComparison.Ordinal)));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
    }
    [TestCase("Value value = new Value(41); value++;")]
    [TestCase("init Value value; ++value;")]
    public void InvalidPlaceContractsAreChecked(string statements)
    {
        var source = Operators + "int32 Main() { " + statements + " return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void ParenthesizedPlaceRetainsWritableIdentity()
    {
        var source = Operators + """
            int32 Main() {
                mut Value value = new Value(41);
                ++(value);
                return value.Number;
            }
            """;
        Assert.That(NativeJit.Execute(Compile(source, NativeRuntimePolicy.FailFast),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void NoThrowSelfWitnessSupportsNoThrowGenericUpdates()
    {
        const string source = """
            interface Increment<this T, R> { [NoThrow] static R operator ++(T value); }
            struct Value : Increment<Value> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                [NoThrow] public static Value operator ++(Value value) { return value; }
            }
            [NoThrow] T Next<T>(T value) where T : Increment<T>, [Copy] {
                mut T result = value;
                return ++result;
            }
            int32 Main() { return Next(new Value(42)).Number; }
            """;
        Assert.That(NativeJit.Execute(Compile(source, NativeRuntimePolicy.CheckedExceptions),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void NoThrowRequiresProvenOperatorEffect()
    {
        var source = Operators + """
            [NoThrow] Value Next(Value value) {
                mut Value result = value;
                return ++result;
            }
            int32 Main() { return 42; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3017"),Is.True);
    }

    [Test]
    public void NoThrowProtocolCannotClaimThrowingOperator()
    {
        const string source = """
            interface Increment<T, R> { [NoThrow] static R operator ++(T value); }
            struct Value : Increment<Value, Value> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator ++(Value value) { return new Value(value.Number + 1); }
            }
            [NoThrow] T Next<T>(T value) where T : Increment<T, T>, [Copy] {
                mut T result = value;
                return ++result;
            }
            int32 Main() { return 42; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
    }

    [Test]
    public void ConstEvalReportsUnsupportedNominalOperatorInsteadOfFolding()
    {
        var source = Operators + """
            [ConstEval] int32 Calculate() {
                mut Value value = new Value(41);
                return (++value).Number;
            }
            int32 Main() { const int32 answer = Calculate(); return answer; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3024"),Is.True);
    }

    [Test]
    public void RequiredConstantCannotExecuteNominalUpdateOnAnExistingPlace()
    {
        var source = Operators + """
            int32 Main() {
                mut Value value = new Value(41);
                return const (++value).Number;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3024"),Is.True);
    }
    [TestCase("holder.Readonly++;")]
    [TestCase("holder.Computed++;")]
    [TestCase("++owner;")]
    public void ReadonlyAccessorsAndOwningValuesCannotBeUpdated(string statement)
    {
        var source = Operators + """
            mut class Holder {
                public Value Readonly;
                public mut Value Item;
                public Holder() {
                    Readonly = new Value(1);
                    Item = new Value(1);
                }
                public Value Computed { get { return Item; } }
            }
            unique struct Owner {
                public mut Value Item;
                public Owner() { Item = new Value(1); }
                [NoThrow] [Cleanup] public void Close() { }
            }
            int32 Main() {
                require Holder.TryCreate(out var holder);
                mut Owner owner = new Owner();
            """
            + statement + """

                return 42;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3008"
            || diagnostic.Code == "TRM3007"),Is.True);
    }

    [Test]
    public void UniqueRootCopyFieldCanCommitWithoutMovingOwner()
    {
        var source = Operators + """
            unique struct Owner {
                public mut Value Item;
                public Owner() { Item = new Value(40); }
                [NoThrow] [Cleanup] public void Close() { }
            }
            int32 Main() {
                mut Owner owner = new Owner();
                Value old = owner.Item++;
                return old.Number == 40 && owner.Item.Number == 41 ? 42 : 1;
            }
            """;
        Assert.That(NativeJit.Execute(Compile(source, NativeRuntimePolicy.CheckedExceptions),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void ForgedOperatorIdentityAndResultAreRejected()
    {
        var program = Compile(Operators + """
            Value Next(Value initial) { mut Value value = initial; return ++value; }
            int32 Main() { return Next(new Value(41)).Number; }
            """, NativeRuntimePolicy.FailFast);
        var index = Enumerable.Range(0, program.Functions.Length)
            .Single(i => program.Functions[i].Name == "Next");
        var function = program.Functions[index];
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual1 = function.Body.Statements[^1]; Assert.That(__nunitActual1, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual1!; }))();
        var update = ((global::System.Func<BoundNominalUpdate>)(() => { var __nunitActual2 = result.Value; Assert.That(__nunitActual2, Is.TypeOf<BoundNominalUpdate>()); return (BoundNominalUpdate)__nunitActual2!; }))();
        foreach (var forged in new[] {
            update with { Function = program.EntryPoint },
            update with { Operator = TokenKind.MinusMinus },
            update with { Type = PrimitiveType.Int32 },
            update with { Place = new BoundConstant(41, PrimitiveType.Int32, update.Span) },
        })
        {
            var changed = function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(function.Body.Statements.Length - 1,
                    result with { Value = forged }),
                }
            };
            Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
                Functions = program.Functions.SetItem(index, changed),
            }));
        }
        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(index, function with { IsNoThrow = true }),
        }));
        var operatorIndex = Enumerable.Range(0, program.Functions.Length)
            .Single(i => program.Functions[i].Name == "Value.op_Increment");
        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(operatorIndex,
                program.Functions[operatorIndex] with { IsOperator = false }),
        }));

        var ordinary = Compile("""
            struct Value {
                public int32 Number;
                public static int32 op_Increment(Value value) { return 1; }
            }
            int32 Main() { return 1; }
            """, NativeRuntimePolicy.FailFast);
        var ordinaryOperatorIndex = Enumerable.Range(0, ordinary.Functions.Length)
            .Single(i => ordinary.Functions[i].Name == "Value.op_Increment");
        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(ordinary with {
            Functions = ordinary.Functions.SetItem(ordinaryOperatorIndex,
                ordinary.Functions[ordinaryOperatorIndex] with { IsOperator = true }),
        }));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void ConcreteNominalUpdatesInGenericDefinitionsRetainOperatorIdentity(NativeRuntimePolicy policy)
    {
        var source = Operators + """
            T Forward<T>(T value) where T : [Copy] {
                mut Value fixedValue = new Value(1);
                fixedValue++;
                return value;
            }
            int32 Main() { return Forward(new Value(42)).Number; }
            """;
        Assert.That(NativeJit.Execute(Compile(source, policy), policy), Is.EqualTo(42));
    }

    [Test]
    public void BodylessIncrementImportContractRequiresOwnerResult()
    {
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Value {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator ++(Value value) {
                    return new Value(value.Number + 1);
                }
            }
            """;
        var producer = ModuleCompiler.CreateProducer(CompileLibrary(source, NativeRuntimePolicy.FailFast),
            ["Value.#ctor", "Value.op_Increment"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var client = CompileClient("""
            int32 Main() {
                mut Value value = new Value(40);
                return (++value).Number;
            }
            """, reference, NativeRuntimePolicy.FailFast);
        var consumer = ModuleCompiler.CreateConsumer(client, producer);
        var contractPrefix = Encoding.UTF8.GetBytes("Value.op_Increment|Value:E|");
        var contractOffset = consumer.AsSpan().IndexOf(contractPrefix);
        Assert.That(contractOffset >= 0, Is.True);
        var lengthOffset = contractOffset - sizeof(int);
        var originalLength = BinaryPrimitives.ReadInt32LittleEndian(
            consumer.AsSpan(lengthOffset, sizeof(int)));
        var originalContract = Encoding.UTF8.GetString(consumer, contractOffset, originalLength);
        var scalarReturnType = ((int)PrimitiveType.Int32)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var malformedContract = originalContract.Replace("Value.op_Increment|Value:E|",
            "Value.op_Increment|" + scalarReturnType + "|", StringComparison.Ordinal);
        Assert.That(malformedContract, Is.Not.EqualTo(originalContract));
        var malformedContractBytes = Encoding.UTF8.GetBytes(malformedContract);
        var malformed = new byte[consumer.Length + malformedContractBytes.Length - originalLength];
        consumer.AsSpan(0, lengthOffset).CopyTo(malformed);
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(lengthOffset, sizeof(int)),
            malformedContractBytes.Length);
        malformedContractBytes.CopyTo(malformed.AsSpan(lengthOffset + sizeof(int)));
        consumer.AsSpan(contractOffset + originalLength).CopyTo(
            malformed.AsSpan(lengthOffset + sizeof(int) + malformedContractBytes.Length));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(malformed, producer));
    }
    [TestCase(false,false,NativeRuntimePolicy.FailFast)]
    [TestCase(false,false,NativeRuntimePolicy.CheckedExceptions)]
    [TestCase(true,false,NativeRuntimePolicy.FailFast)]
    [TestCase(true,false,NativeRuntimePolicy.CheckedExceptions)]
    [TestCase(false,true,NativeRuntimePolicy.FailFast)]
    [TestCase(false,true,NativeRuntimePolicy.CheckedExceptions)]
    public async Task NominalUpdateBodiesSurviveSemanticAndNativeReplacement(
        bool generic, bool exportOperator, NativeRuntimePolicy policy)
    {
        var library = Operators + """
            public int32 Next() { mut Value value = new Value(41); return (++value).Number; }
            """;
        if (generic)
        {
            library = """
                interface Increment<this T, R> { static R operator ++(T value); }
                """ + Operators.Replace("struct Value {", "struct Value : Increment<Value> {",
                    StringComparison.Ordinal) + """
                T IncrementValue<T>(T initial) where T : Increment<T>, [Copy] {
                    mut T value = initial;
                    return ++value;
                }
                public int32 Next() { return IncrementValue(new Value(41)).Number; }
                """;
        }
        if (exportOperator)
        {
            library = library.Replace("struct Value {",
                "[Blittable] [Layout(LayoutKind.Sequential)] public struct Value {", StringComparison.Ordinal);
        }
        string[] exports = exportOperator ? ["Next", "Value.op_Increment"] : ["Next"];
        var original = ModuleCompiler.CreateProducer(CompileLibrary(library, policy), exports);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(4, 4)),
            Is.EqualTo(exportOperator ? 78 : 76));
        var stale = (byte[])original.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(stale.AsSpan(4, 4), 63);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(stale));
        var client = CompileClient("int32 Main() { return Next(); }",
            ModuleCompiler.CreateReference(original), policy);
        var consumer = ModuleCompiler.CreateConsumer(client, original);
        var consumerSnapshot = (byte[])consumer.Clone();
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, original), policy), Is.EqualTo(42));
        var replacement = ModuleCompiler.CreateProducer(CompileLibrary(
            library.Replace("new Value(41)", "new Value(42)", StringComparison.Ordinal), policy), exports);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), policy), Is.EqualTo(43));
        Assert.That(consumer, Is.EqualTo(consumerSnapshot));

        var native = NativeModuleCompiler.CreateProducer(CompileLibrary(library, policy),
            exports, "NominalUpdates", policy);
        var graphClient = CompileClient("int32 Main() { return Next(); }",
            NativeModuleCompiler.CreateGraphReference([native]), policy);
        var graph = NativeModuleCompiler.CreateGraphConsumer(graphClient, [native], policy);
        var graphSnapshot = (byte[])graph.Clone();
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graph, [native],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        var nativeReplacement = NativeModuleCompiler.CreateProducer(CompileLibrary(
            library.Replace("new Value(41)", "new Value(42)", StringComparison.Ordinal), policy),
            exports, "NominalUpdates", policy);
        NativeModuleCompiler.ValidateGraph(graph, [nativeReplacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graph, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(43));
        Assert.That(graph, Is.EqualTo(graphSnapshot));
    }

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
    }

    private static BoundProgram CompileLibrary(string source, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
    }

    private static BoundProgram CompileClient(string source, CompilationReference reference, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), reference, (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
    }
}
