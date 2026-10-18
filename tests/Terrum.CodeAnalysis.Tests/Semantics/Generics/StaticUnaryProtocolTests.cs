// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics.Generics;

public sealed class StaticUnaryProtocolTests
{
    internal const string Source = """
        interface UnaryPlus<T, R> { [NoThrow] static R operator +(T value); }
        interface UnaryMinus<T, R> { [NoThrow] static R operator -(T value); }
        interface UnaryNot<T, R> { [NoThrow] static R operator !(T value); }
        interface UnaryComplement<T, R> { [NoThrow] static R operator ~(T value); }
        interface BinaryPlus<L, R, O> { [NoThrow] static O operator +(L left, R right); }
        interface BinaryMinus<L, R, O> { [NoThrow] static O operator -(L left, R right); }
        struct Value : UnaryPlus<Value, Value>, UnaryMinus<Value, Value>,
            UnaryNot<Value, int32>, UnaryComplement<Value, bool>,
            BinaryPlus<Value, Value, Value>, BinaryMinus<Value, Value, Value> {
            public int32 Number;
            [NoThrow] public Value(int32 number) { Number = number; }
            [NoThrow] public static Value operator +(Value value) { return value; }
            [NoThrow] public static Value operator -(Value value) { return value; }
            [NoThrow] public static int32 operator !(Value value) { return value.Number; }
            [NoThrow] public static bool operator ~(Value value) { return value.Number == 42; }
            [NoThrow] public static Value operator +(Value left, Value right) {
                return new Value(left.Number);
            }
            [NoThrow] public static Value operator -(Value left, Value right) {
                return new Value(left.Number);
            }
        }
        R Negate<T, R>(T value) where T : UnaryNot<T, R>, [Copy] where R : [Copy] {
            return !value;
        }
        [NoThrow] T Plus<T>(T value) where T : UnaryPlus<T, T>, [Copy] {
            return +value;
        }
        T Compose<T>(T value) where T : UnaryPlus<T, T>, [Copy] {
            return Plus<T>(value);
        }
        """;

    [Test]
    public void UnsafeProtocolOperatorRemainsUnsafeInsideGenericProofBodies()
    {
        const string protocol = """
            interface UnaryPlus<T, R> { static unsafe R operator +(T value); }
            struct Value : UnaryPlus<Value, Value> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator +(Value value) { return value; }
            }
            """;

        var unauthorized = Compilation.CreateLibrary(SyntaxTree.Parse(protocol + """
            T Plus<T>(T value) where T : UnaryPlus<T, T>, [Copy] { return +value; }
            """));
        Assert.That(unauthorized.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(unauthorized.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var authorized = Compilation.Create(SyntaxTree.Parse(protocol + """
            T Plus<T>(T value) where T : UnaryPlus<T, T>, [Copy] { return unsafe(+value); }
            int32 Main() { return Plus<Value>(new Value(42)).Number; }
            """));
        Assert.That(authorized.HasErrors, Is.False, string.Join(Environment.NewLine, authorized.Diagnostics));
    }

    [Test]
    public void DistinctArityAndIndependentResultsBindDirectAndClosedCalls()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Source + """
            int32 Main() {
                Value value = new Value(42);
                Value plus = +value;
                Value minus = -value;
                Value combined = plus + minus - value;
                if (!value != 42 || ~value != true) { return 1; }
                if (Negate<Value, int32>(value) != 42) { return 2; }
                return Compose(combined).Number;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.Name == "Negate<Value, int32>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.Name == "Plus<Value>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.Name == "Compose<Value>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal)),Is.False);
    }
    [TestCase("operator !(Value value)","operator !(Value first, Value second)","TRM3007")]
    [TestCase("operator !(Value value)","operator !(int32 value)","TRM3007")]
    [TestCase("static R operator !(T value)","static R operator !(T first, T second)","TRM3001")]
    [TestCase("UnaryNot<Value, int32>","UnaryNot<Value, bool>","TRM3007")]
    [TestCase("UnaryNot<Value, int32>","UnaryNot<Value, int32>, UnaryNot<Value, bool>","TRM3007")]
    [TestCase("interface UnaryNot<T, R>","interface UnaryNot<T, R, Extra>","TRM3001")]
    public void SignatureAndResultMismatchesReject(string before, string after, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Source.Replace(before, after, StringComparison.Ordinal)
            + "int32 Main() { return 42; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == diagnostic),Is.True);
    }

    [Test]
    public void ResultOnlyOverloadIsRejectedRatherThanSelectedByContext()
    {
        var source = Source.Replace(
            "[NoThrow] public static int32 operator !(Value value) { return value.Number; }",
            """
            [NoThrow] public static int32 operator !(Value value) { return value.Number; }
            [NoThrow] public static bool operator !(Value value) { return true; }
            """, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source + "int32 Main() { return 42; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3003"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("where T : UnaryPlus<T, T>, [Copy]","where T : UnaryMinus<T, T>, [Copy]")]
    [TestCase("where T : UnaryPlus<T, T>, [Copy]","where T : [Copy]")]
    public void DefinitionSiteProofCannotBorrowConcreteOrOtherWitnesses(string before, string after)
    {
        var source = Source.Replace(before, after, StringComparison.Ordinal) + "int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("UnaryPlus<Value, Value>","UnaryMinus<Value, Value>")]
    [TestCase("public static Value operator +(Value value)","private static Value operator +(Value value)")]
    public void MissingOrInaccessibleUnaryWitnessRejects(string before, string after)
    {
        var source = Source.Replace(before, after, StringComparison.Ordinal)
            + "int32 Main() { return Compose(new Value(42)).Number; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NoThrowUnaryProtocolRequiresNoThrowImplementationAndPreservesEffects()
    {
        var missing = Source.Replace("[NoThrow] public static int32 operator !(Value value)",
            "public static int32 operator !(Value value)", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(missing + "int32 Main() { return 42; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == "TRM3007"),Is.True);
        var nonNoThrow = Source.Replace("[NoThrow] static R operator !(T value)",
            "static R operator !(T value)", StringComparison.Ordinal);
        nonNoThrow += "[NoThrow] int32 Check(Value value) { return Negate<Value, int32>(value); } int32 Main() { return 42; }";
        var checkedCompilation = Compilation.Create(SyntaxTree.Parse(nonNoThrow));
        Assert.That(global::System.Linq.Enumerable.Any(checkedCompilation.Diagnostics,item => item.Code == "TRM3017"),Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void ClosedUnaryCallsUseExistingPrivateConsumerEncoding(NativeRuntimePolicy policy)
    {
        var sourcePolicy = policy == NativeRuntimePolicy.FailFast
            ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(Source + """
            public int32 Read(int32 number) {
                return Negate<Value, int32>(new Value(number));
            }
            """), runtimePolicy: sourcePolicy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = library.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), ["Read"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(42); }"),
            reference, runtimePolicy: sourcePolicy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,function =>
            function.Name.EndsWith(".Negate<Value, int32>", StringComparison.Ordinal)),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,function => function.Name == "Main"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,function =>
            function.Name.Contains("$T", StringComparison.Ordinal)),Is.False);
        Assert.That(NativeEmitter.Emit(linked, policy), Is.Not.Empty);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task UnarySpecializationServesNativeGraphConsumerAsync(NativeRuntimePolicy policy)
    {
        var sourcePolicy = policy == NativeRuntimePolicy.FailFast
            ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(Source + """
            public int32 Read(int32 number) {
                return Negate<Value, int32>(new Value(number));
            }
            """), runtimePolicy: sourcePolicy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = library.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), ["Read"], "NodeB", policy);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Read(42); }"),
            NativeModuleCompiler.CreateGraphReference([producer]), runtimePolicy: sourcePolicy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = consumerCompilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(), [producer], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer, [producer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(false,NativeRuntimePolicy.FailFast)]
    [TestCase(false,NativeRuntimePolicy.CheckedExceptions)]
    [TestCase(true,NativeRuntimePolicy.FailFast)]
    [TestCase(true,NativeRuntimePolicy.CheckedExceptions)]
    public void FixedCopyStructResultUsesDistinctProofTypeAndInference(bool self, NativeRuntimePolicy policy)
    {
        var source = """
            interface Wrap<T, R> { static R operator ~(T value); }
            struct Result { public int32 Number; public Result(int32 n) { Number = n; } }
            struct Input : Wrap<Input, Result> {
                public int32 Number;
                public Input(int32 n) { Number = n; }
                public static Result operator ~(Input value) { return new Result(value.Number); }
            }
            R Apply<T, R>(T value) where T : Wrap<T, R>, [Copy] where R : [Copy] {
                return ~value;
            }
            T Fixed<T>(T value) where T : Wrap<T, Result>, [Copy] {
                Result result = ~value;
                if (result.Number != 42) { return value; }
                return value;
            }
            int32 Main() {
                Result result = Apply(new Input(42));
                return Fixed(new Input(result.Number)).Number;
            }
            """;
        if (self)
        {
            source = source.Replace("interface Wrap<T, R>", "interface Wrap<this T, R>", StringComparison.Ordinal)
                .Replace("Wrap<Input, Result>", "Wrap<Result>", StringComparison.Ordinal)
                .Replace("Wrap<T, R>", "Wrap<R>", StringComparison.Ordinal)
                .Replace("Wrap<T, Result>", "Wrap<Result>", StringComparison.Ordinal);
        }
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(global::System.Linq.Enumerable.Any(((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))().Functions,function => function.Name == "Apply<Input, Result>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))().Functions,function => function.Name == "Fixed<Input>"),Is.True);
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), policy), Is.EqualTo(42));
    }
}
