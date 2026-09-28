// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticUnaryProtocolTests
{
    internal const string Source = """
        interface UnaryPlus<T, R> { [NoThrow] static abstract R operator +(T value); }
        interface UnaryMinus<T, R> { [NoThrow] static abstract R operator -(T value); }
        interface UnaryNot<T, R> { [NoThrow] static abstract R operator !(T value); }
        interface UnaryComplement<T, R> { [NoThrow] static abstract R operator ~(T value); }
        interface BinaryPlus<L, R, O> { [NoThrow] static abstract O operator +(L left, R right); }
        interface BinaryMinus<L, R, O> { [NoThrow] static abstract O operator -(L left, R right); }
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

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Contains(program.Functions, function => function.Name == "Negate<Value, int32>");
        Assert.Contains(program.Functions, function => function.Name == "Plus<Value>");
        Assert.Contains(program.Functions, function => function.Name == "Compose<Value>");
        Assert.DoesNotContain(program.Functions, function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("operator !(Value value)", "operator !(Value first, Value second)", "TRM3007")]
    [InlineData("operator !(Value value)", "operator !(int32 value)", "TRM3007")]
    [InlineData("static abstract R operator !(T value)", "static abstract R operator !(T first, T second)", "TRM3001")]
    [InlineData("UnaryNot<Value, int32>", "UnaryNot<Value, bool>", "TRM3007")]
    [InlineData("UnaryNot<Value, int32>", "UnaryNot<Value, int32>, UnaryNot<Value, bool>", "TRM3007")]
    [InlineData("interface UnaryNot<T, R>", "interface UnaryNot<T, R, Extra>", "TRM3001")]
    public void SignatureAndResultMismatchesReject(string before, string after, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Source.Replace(before, after, StringComparison.Ordinal)
            + "int32 Main() { return 42; }"));
        Assert.Contains(compilation.Diagnostics, item => item.Code == diagnostic);
    }

    [Fact]
    public void ResultOnlyOverloadIsRejectedRatherThanSelectedByContext()
    {
        var source = Source.Replace(
            "[NoThrow] public static int32 operator !(Value value) { return value.Number; }",
            """
            [NoThrow] public static int32 operator !(Value value) { return value.Number; }
            [NoThrow] public static bool operator !(Value value) { return true; }
            """, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source + "int32 Main() { return 42; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3003");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("where T : UnaryPlus<T, T>, [Copy]", "where T : UnaryMinus<T, T>, [Copy]")]
    [InlineData("where T : UnaryPlus<T, T>, [Copy]", "where T : [Copy]")]
    public void DefinitionSiteProofCannotBorrowConcreteOrOtherWitnesses(string before, string after)
    {
        var source = Source.Replace(before, after, StringComparison.Ordinal) + "int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("UnaryPlus<Value, Value>", "UnaryMinus<Value, Value>")]
    [InlineData("public static Value operator +(Value value)", "private static Value operator +(Value value)")]
    public void MissingOrInaccessibleUnaryWitnessRejects(string before, string after)
    {
        var source = Source.Replace(before, after, StringComparison.Ordinal)
            + "int32 Main() { return Compose(new Value(42)).Number; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void NoThrowUnaryProtocolRequiresNoThrowImplementationAndPreservesEffects()
    {
        var missing = Source.Replace("[NoThrow] public static int32 operator !(Value value)",
            "public static int32 operator !(Value value)", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(missing + "int32 Main() { return 42; }"));
        Assert.Contains(compilation.Diagnostics, item => item.Code == "TRM3007");
        var nonNoThrow = Source.Replace("[NoThrow] static abstract R operator !(T value)",
            "static abstract R operator !(T value)", StringComparison.Ordinal);
        nonNoThrow += "[NoThrow] int32 Check(Value value) { return Negate<Value, int32>(value); } int32 Main() { return 42; }";
        var checkedCompilation = Compilation.Create(SyntaxTree.Parse(nonNoThrow));
        Assert.Contains(checkedCompilation.Diagnostics, item => item.Code == "TRM3017");
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void ClosedUnaryCallsUseExistingPrivateConsumerEncoding(NativeRuntimePolicy policy)
    {
        var sourcePolicy = policy == NativeRuntimePolicy.FailFast
            ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(Source + """
            public int32 Read(int32 number) {
                return Negate<Value, int32>(new Value(number));
            }
            """), runtimePolicy: sourcePolicy);
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Read"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(42); }"),
            reference, runtimePolicy: sourcePolicy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Contains(linked.Functions, function =>
            function.Name.EndsWith(".Negate<Value, int32>", StringComparison.Ordinal));
        Assert.Contains(linked.Functions, function => function.Name == "Main");
        Assert.DoesNotContain(linked.Functions, function =>
            function.Name.Contains("$T", StringComparison.Ordinal));
        Assert.NotEmpty(NativeEmitter.Emit(linked, policy));
        Assert.Equal(42, NativeJit.Execute(linked, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task UnarySpecializationServesNativeGraphConsumerAsync(NativeRuntimePolicy policy)
    {
        var sourcePolicy = policy == NativeRuntimePolicy.FailFast
            ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(Source + """
            public int32 Read(int32 number) {
                return Negate<Value, int32>(new Value(number));
            }
            """), runtimePolicy: sourcePolicy);
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(library.Program), ["Read"], "NodeB", policy);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Read(42); }"),
            NativeModuleCompiler.CreateGraphReference([producer]), runtimePolicy: sourcePolicy);
        Assert.False(consumerCompilation.HasErrors, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), [producer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(consumer, [producer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Fact]
    public void FixedCopyStructResultUsesDistinctProofTypeAndInference()
    {
        var source = """
            interface Wrap<T, R> { static abstract R operator ~(T value); }
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
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "Apply<Input, Result>");
        Assert.Contains(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "Fixed<Input>");
    }
}
