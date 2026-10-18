// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class GetterPropertyTests
{
    internal const string ValueSource = """
        struct Counter {
            int32 _value;
            public Counter(int32 value) { _value = value; }
            public int32 Value { [NoThrow] get { return _value; } }
            public int32 get_Value() { return 1; }
        }
        class Box {
            int32 _value;
            public Box(int32 value) { _value = value; }
            public int32 Value { get { require _value > 0; return _value; } }
        }
        int32 Main() {
            Counter value = new Counter(40);
            require Box.TryCreate(value.Value, out var box);
            return box.Value + value.get_Value() + 1;
        }
        """;

    [Test]
    public void GetterHasDistinctMemberIdentityAndAccessorEffects()
    {
        var tree = SyntaxTree.Parse(ValueSource);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(ValueSource));
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var getter = global::System.Linq.Enumerable.Single(program.Functions, function => function.PropertyName == "Value"
            && function.DeclaringStruct == "Counter");
        Assert.That(getter.Name, Is.EqualTo("Counter.#get:Value"));
        Assert.That(getter.IsNoThrow, Is.True);
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions, function => function.PropertyName == "Value"
            && function.DeclaringStruct == "Box").IsNoThrow, Is.False);
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Counter.get_Value").PropertyName, Is.Null);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        var index = program.Functions.IndexOf(getter);
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(index, getter with { PropertyName = "Other" }),
        }))!;
    }

    [Test]
    public void StaticComputedGetterHasNoReceiverAndUsesTypeQualifiedAndUnqualifiedLookup()
    {
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Counter {
                public int32 Placeholder;
                public static int32 Base { [NoThrow] get { return 40; } }
                public static int32 LocalValue {
                    get {
                        int32 local = 42;
                        return local;
                    }
                }
                public static int32 Direct => 2;
                public static int32 Value {
                    [NoThrow] get { return Base; }
                }
                public static int32 UnsafeValue { unsafe get { return 1; } }
                public static int32 Read() { return Value + Direct; }
                public int32 ReadStaticValue() { return Counter.Value; }
            }
            int32 Main() { return Counter.Read() + unsafe(Counter.UnsafeValue); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        foreach (var propertyName in new[] { "Base", "Value" })
        {
            var getter = global::System.Linq.Enumerable.Single(program.Functions, function => function.DeclaringStruct == "Counter" && function.PropertyName == propertyName);
            Assert.That(getter.Name, Is.EqualTo("Counter.#get:" + propertyName));
            Assert.That(getter.IsStatic, Is.True);
            Assert.That(getter.ParameterCount, Is.EqualTo(0));
            Assert.That(getter.IsNoThrow, Is.True);
        }
        var localGetter = global::System.Linq.Enumerable.Single(program.Functions, static function => function.DeclaringStruct == "Counter" && function.PropertyName == "LocalValue");
        Assert.That(localGetter.ParameterCount, Is.EqualTo(0));
        Assert.That(localGetter.Variables[0].Name, Is.EqualTo("local"));
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions, static function => function.DeclaringStruct == "Counter"
                && function.PropertyName == "UnsafeValue").RequiresUnsafeCaller, Is.True);
        var staticGetterIndex = program.Functions.IndexOf(global::System.Linq.Enumerable.Single(program.Functions, static function => function.DeclaringStruct == "Counter" && function.PropertyName == "Value"));
        var staticGetter = program.Functions[staticGetterIndex];
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(staticGetterIndex, staticGetter with { IsStatic = false }),
        }))!;
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(staticGetterIndex, staticGetter with {
                Name = "Counter.#get:Other",
            }),
        }))!;
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(43));
    }
    [TestCase("","A.Counter.Value")]
    [TestCase("A.Counter value","value.InstanceValue")]
    public void GetterLookupDoesNotFallBackToAnotherOwner(string parameters, string access)
    {
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse("namespace A; [Blittable] [Layout(LayoutKind.Sequential)] public struct Counter { public int32 Placeholder; }"),
            SyntaxTree.Parse("""
                namespace N.A;
                public struct Counter {
                    public static int32 Value { get { return 42; } }
                    public int32 InstanceValue { get { return 43; } }
                }
                """),
            SyntaxTree.Parse($"namespace N; int32 Probe({parameters}) {{ return {access}; }}"),
        ]);

        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3006"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("int32 result = Counter.Value;")]
    [TestCase("var result = Counter.Value;")]
    public void StaticGetterDoesNotTreatAnUnresolvedLocalAsAType(string declaration)
    {
        var source = "public struct Counter { public static int32 Value { get { return 42; } } } "
            + "int32 Main() { init var Counter; " + declaration
            + " Counter = int32(0); return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3006"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StaticAndReadonlyInstanceGettersReturnCopyValuesAsync(BoundRuntimePolicy policy)
    {
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair {
                public mut int32 First;
                public mut int32 Second;
                public Pair(int32 first, int32 second) { First = first; Second = second; }
            }
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Odd {
                public char16 First;
                public char16 Second;
                public bool Third;
                public Odd(char16 first, char16 second, bool third) {
                    First = first;
                    Second = second;
                    Third = third;
                }
            }
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Envelope {
                public Pair Payload;
                public Odd Tail;
                public Envelope(Pair payload, Odd tail) { Payload = payload; Tail = tail; }
            }
            [Blittable] [Layout(LayoutKind.Sequential)] public unique struct UniqueValue {
                public int32 Number;
                public UniqueValue(int32 number) { Number = number; }
            }
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Counter {
                public mut int32 Number;
                public Counter(int32 number) { Number = number; }
                public static Pair StaticPair {
                    get {
                        Pair local = new Pair(40, 2);
                        return local;
                    }
                }
                public static Odd StaticOdd => new Odd(char16(3), char16(4), true);
                public static Envelope StaticNested {
                    get { return new Envelope(StaticPair, StaticOdd); }
                }
                public static float64 Floating => 1.5;
                public Pair PairValue { get { return new Pair(Number, Number + 1); } }
                public Odd OddValue => new Odd(char16(Number), char16(Number + 1), true);
                public Envelope NestedValue { get { return new Envelope(PairValue, OddValue); } }
                public mut Counter Next() {
                    Number = Number + 1;
                    return new Counter(Number);
                }
            }
            int32 Main() {
                Pair staticPair = Counter.StaticPair;
                Odd staticOdd = Counter.StaticOdd;
                Envelope staticNested = Counter.StaticNested;
                mut Counter owner = new Counter(10);
                Pair instancePair = owner.PairValue;
                Odd instanceOdd = owner.OddValue;
                Envelope instanceNested = owner.NestedValue;
                mut Pair changed = owner.PairValue;
                changed.First = 0;
                Pair sideEffect = owner.Next().PairValue;
                require staticOdd.Third && instanceOdd.Third && staticNested.Tail.Third
                    && instanceNested.Tail.Third && staticPair.First == 40 && staticPair.Second == 2
                    && instancePair.First == 10 && instancePair.Second == 11;
                require staticNested.Payload.First == 40 && staticNested.Payload.Second == 2
                    && staticNested.Tail.First == char16(3) && staticNested.Tail.Second == char16(4);
                require instanceNested.Payload.First == 10 && instanceNested.Payload.Second == 11
                    && instanceNested.Tail.First == char16(10) && instanceNested.Tail.Second == char16(11);
                require changed.First == 0 && owner.PairValue.First == 11;
                require sideEffect.First == 11 && owner.Number == 11;
                return 91;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();

        var staticPairGetter = global::System.Linq.Enumerable.Single(program.Functions, static function => function.PropertyName == "StaticPair");
        Assert.That(staticPairGetter.IsStatic, Is.True);
        Assert.That(staticPairGetter.ParameterCount, Is.EqualTo(0));
        Assert.That(staticPairGetter.Variables[0].Name, Is.EqualTo("local"));
        var instanceOddGetter = global::System.Linq.Enumerable.Single(program.Functions, static function => function.PropertyName == "OddValue");
        Assert.That(instanceOddGetter.IsStatic, Is.False);
        Assert.That(instanceOddGetter.ParameterCount, Is.EqualTo(1));
        Assert.That((int)instanceOddGetter.Variables[0].Type, Is.EqualTo(program.Structures.IndexOf(
            global::System.Linq.Enumerable.Single(program.Structures, static structure => structure.Name == "Counter")) + 100));
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions, static function => function.PropertyName == "Floating").ReturnType, Is.EqualTo(PrimitiveType.Float64));

        var uniqueValueIndex = program.Structures.IndexOf(
            global::System.Linq.Enumerable.Single(program.Structures, static structure => structure.Name == "UniqueValue"));
        var staticPairIndex = program.Functions.IndexOf(staticPairGetter);
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(staticPairIndex,
                staticPairGetter with { ReturnType = (PrimitiveType)(100 + uniqueValueIndex) }),
        }))!;

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        await RunNativeImageAsync(NativeEmitter.Emit(program, runtimePolicy), 91).ConfigureAwait(true);
    }
    [TestCase("""
        [Blittable] [Layout(LayoutKind.Sequential)] public unique struct UniqueValue {
            public int32 Number;
            public UniqueValue(int32 number) { Number = number; }
        }
        [Blittable] [Layout(LayoutKind.Sequential)] public struct Counter {
            public int32 Placeholder;
            public static UniqueValue Value { get { return new UniqueValue(42); } }
        }
        """)]
    [TestCase("""
        class Box {
            public int32 Number;
            public Box(int32 number) { Number = number; }
        }
        [Blittable] [Layout(LayoutKind.Sequential)] public struct Counter {
            public int32 Placeholder;
            public static Box Value { get { return new Box(42); } }
        }
        """)]
    public void StaticComputedGettersRejectUniqueAndManagedResults(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void StaticComputedGetterDoesNotAddClassPropertySupport()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Box {
                public int32 Placeholder;
                public static int32 Value { get { return 42; } }
            }
            int32 Main() { return 0; }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3001"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void GetterVerifierRejectsFieldNameCollisionsForStructsAndClasses()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ValueSource));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        foreach (var owner in new[] { "Counter", "Box" })
        {
            var index = program.Functions.IndexOf(global::System.Linq.Enumerable.Single(program.Functions, function => function.PropertyName == "Value" && function.DeclaringStruct == owner));
            var getter = program.Functions[index];
            var otherGetter = getter with {
                Name = owner + ".#get:Other",
                PropertyName = "Other",
            };
            var collidingGetter = getter with {
                Name = owner + ".#get:_value",
                PropertyName = "_value",
            };
            _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
                Functions = program.Functions.SetItem(index, otherGetter).Add(collidingGetter),
            }))!;
        }
    }
    [TestCase("public int32 Value { get; }","TRM1002")]
    [TestCase("public int32 Value { get { return _value; } set { _value = 1; } }","TRM1002")]
    [TestCase("public int32 Value { [DoesNotReturn] get { return _value; } }","TRM3001")]
    [TestCase("public mut int32 Value { get { return _value; } }","TRM3001")]
    [TestCase("public static UniqueCounter Value { get { return new UniqueCounter(1); } }","TRM3007")]
    [TestCase("public void Value { get { return; } }","TRM3007")]
    [TestCase("public int32 _value { get { return _value; } }","TRM3001")]
    [TestCase("[NoThrow] public int32 Value { get { return _value; } }","TRM3001")]
    public void UnsupportedGetterShapesDiagnose(string declaration, string code)
    {
        var source = "unique struct UniqueCounter { public int32 Number; "
            + "public UniqueCounter(int32 number) { Number = number; } } "
            + "struct Counter { int32 _value; public Counter(int32 value) { _value = value; } "
            + declaration + " } int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
    }

    [Test]
    public void PropertyReadIsNotAnAssignableField()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ValueSource.Replace(
            "return box.Value + value.get_Value() + 1;",
            "value.Value = 1; return box.Value + value.get_Value() + 1;",
            StringComparison.Ordinal)));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void GenericCopyConstraintDoesNotInventInstanceGetterWitnesses()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Counter {
                int32 _value;
                public Counter(int32 value) { _value = value; }
                public int32 Value { get { return _value; } }
            }
            int32 Read<T>(T input) where T : [Copy] { return input.Value; }
            int32 Main() { return Read(new Counter(42)); }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void GetterEffectsAreCheckedAtTheAccessSite()
    {
        var source = """
            class Box {
                int32 _value;
                public Box(int32 value) { _value = value; }
                public int32 Value { get { require _value > 0; return _value; } }
                [NoThrow] public int32 Read() { return this.Value; }
            }
            int32 Main() { return 42; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3017"),Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ExportedGetterServicesSemanticAndNativeConsumersAsync(NativeRuntimePolicy policy)
    {
        const string Library = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Counter {
                public int32 _value;
                public Counter(int32 value) { _value = value; }
                public static Counter Create() { return new Counter(42); }
                public int32 Value { [NoThrow] get { return _value; } }
                public int32 get_Value() { return 1; }
            }
            """;
        const string Consumer = "int32 Main() { Counter value = Counter.Create(); return value.Value; }";
        string[] exports = ["Counter.Create", "Counter.#get:Value"];
        var boundPolicy = (BoundRuntimePolicy)policy;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(Library), boundPolicy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        var producer = ModuleCompiler.CreateProducer(program, exports);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(78));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            ModuleCompiler.CreateReference(producer), boundPolicy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = consumerCompilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(global::System.Linq.Enumerable.Single(linked.Functions, function => function.Name == "Counter.#get:Value").PropertyName, Is.EqualTo("Value"));
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(linked, policy), Is.Not.Empty);

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, exports, "NodeB", policy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), boundPolicy);
        Assert.That(nativeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = nativeCompilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), [nativeProducer], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateClassGetterSurvivesManagedGraphIntermediateAsync(NativeRuntimePolicy policy)
    {
        const string Leaf = "namespace Source; public int32 Bias() { return 42; }";
        const string Middle = """
            using Source;
            class Box {
                int32 _value;
                public Box(int32 value) { _value = value; }
                public int32 Value { [NoThrow] get { return _value; } }
            }
            public int32 Read() {
                require Box.TryCreate(Bias(), out var box);
                return box.Value;
            }
            """;
        var leaf = NativeModuleCompiler.CreateProducer(
            CompileLibrary(Leaf, policy), ["Source.Bias"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(CompileLibrary(Middle, policy,
            NativeModuleCompiler.CreateGraphReference([leaf])), ["Read"], "NodeA", [leaf], policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]), (BoundRuntimePolicy)policy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = rootCompilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), [middle], policy);
        NativeModuleCompiler.ValidateGraph(root, [middle, leaf]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [leaf, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public void GetterVersionAlsoPreservesPrivateInitMode()
    {
        var program = CompileLibrary("""
            struct Counter {
                int32 _value;
                public Counter(int32 value) { _value = value; }
                public int32 Value { [NoThrow] get { return _value; } }
            }
            bool Fill(init ref<int32> value) { value = 42; return true; }
            public int32 Read() {
                require Fill(init var result);
                return new Counter(result).Value;
            }
            """, NativeRuntimePolicy.FailFast);
        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            ModuleCompiler.CreateReference(producer));
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = consumerCompilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(global::System.Linq.Enumerable.Single(linked.Functions, function => function.Name == "$producer.Fill")
            .Variables[0].IsInit, Is.True);
        Assert.That(NativeJit.Execute(linked, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void PrivateGetterOnSharedPublicStructSurvivesSemanticLinking()
    {
        var program = CompileLibrary("""
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Counter {
                public int32 ValueField;
                public Counter(int32 value) { ValueField = value; }
                int32 Value { [NoThrow] get { return ValueField; } }
                public static Counter Create() { return new Counter(42); }
                public int32 Read() { return Value; }
            }
            """, NativeRuntimePolicy.FailFast);
        var producer = ModuleCompiler.CreateProducer(program, ["Counter.Create", "Counter.Read"]);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { Counter value = Counter.Create(); return value.Read(); }
            """), ModuleCompiler.CreateReference(producer));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    private static BoundProgram CompileLibrary(string source, NativeRuntimePolicy policy,
        CompilationReference? reference = null)
    {
        var tree = SyntaxTree.Parse(source);
        var compilation = reference is null
            ? Compilation.CreateLibrary(tree, (BoundRuntimePolicy)policy)
            : Compilation.CreateLibrary(tree, reference, (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))();
    }

    private static async Task RunNativeImageAsync(byte[] image, int expected)
    {
        var executable = Path.Combine(TestArtifactPaths.DirectoryPath,
            "terrum-getter-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            await File.WriteAllBytesAsync(executable, image).ConfigureAwait(true);
            var start = new ProcessStartInfo(executable) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Failed to start emitted getter test image.");
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
        finally
        {
            if (File.Exists(executable))
            {
                await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
            }
        }
    }
}
