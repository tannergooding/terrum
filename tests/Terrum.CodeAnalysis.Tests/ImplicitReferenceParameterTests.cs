// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ImplicitReferenceParameterTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void ReadsAndForwardsTheCallerPlace(NativeRuntimePolicy policy)
    {
        var source = """
            int32 Read(implicit ref<int32> value) { return value; }
            int32 Forward(implicit ref<int32> value) { return Read(value); }
            int32 Main() {
                mut int32 value = 41;
                return Forward(value);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.True(program.Functions[0].Variables[0].IsImplicitRef);
        Assert.Equal(41, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "implicit-ref-test"));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Read"]));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void EarlierLoanObservesMutationFromLaterArgument(NativeRuntimePolicy policy)
    {
        var source = """
            int32 Replace(out int32 value) {
                value = 42;
                return 0;
            }
            int32 Observe(implicit ref<int32> value, int32 ignored) {
                return value;
            }
            int32 Main() {
                mut int32 value = 7;
                return Observe(value, Replace(out value));
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void ReorderedNamedCallPassesTheReferenceAddressOnTheStack(NativeRuntimePolicy policy)
    {
        var source = """
            int32 ReadSix(int32 first, int32 second, int32 third, int32 fourth,
                implicit ref<int32> value, implicit ref<int32> materialized) {
                return value + materialized + first + second + third + fourth;
            }
            int32 Main() {
                mut int32 value = 37;
                return ReadSix(value: value, materialized: 5 + 6, fourth: 4,
                    third: 3, second: 2, first: 1);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(58, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void MaterializesValueExpressionsForReadonlyReferences(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Read(implicit ref<int32> value) { return value; }
            int32 Main() { return Read(40 + 2); }
            """), (BoundRuntimePolicy)policy);

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var main = Assert.Single(program.Functions, static function => function.Name == "Main");
        Assert.Contains(main.Variables, static variable => variable.IsImplicitReferenceTemporary);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "implicit-ref-temp-test"));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Read"]));

        var mainIndex = program.Functions.IndexOf(main);
        var call = Assert.IsType<BoundCall>(Assert.IsType<BoundReturn>(main.Body.Statements[0]).Value);
        var temporary = call.ImplicitReferenceTemporary(0);
        var forgedMain = main with {
            Variables = main.Variables.SetItem(temporary,
                main.Variables[temporary] with { Type = PrimitiveType.UInt32 }),
        };
        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(mainIndex, forgedMain),
        }));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void MaterializedExpressionIsEvaluatedOnceBeforeLaterArguments(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Bump(int32 previous, out int32 value) {
                value = previous + 1;
                return value;
            }
            int32 Observe(implicit ref<int32> value, int32 later) {
                return value + later;
            }
            int32 Main() {
                mut int32 value = 7;
                return Observe(Bump(value, out value), Bump(value, out value));
            }
            """), (BoundRuntimePolicy)policy);

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(17, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void ReorderedNamedArgumentsPreserveImplicitReferenceEvaluationOrder(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Replace(out int32 value) {
                value = 42;
                return 7;
            }
            int32 Observe(int32 current, implicit ref<int32> snapshot) {
                return current * 100 + snapshot;
            }
            int32 Main() {
                mut int32 value = 1;
                return Observe(snapshot: value + 1, current: Replace(out value));
            }
            """), (BoundRuntimePolicy)policy);

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(702, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void ReusesMaterializationSlotAcrossLoopIterations(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Read(implicit ref<int32> value) { return value; }
            int32 Main() {
                mut int32 index = 0;
                mut int32 sum = 0;
                while (index < 3) {
                    sum = sum + Read(index + 10);
                    index = index + 1;
                }
                return sum;
            }
            """), (BoundRuntimePolicy)policy);

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(33, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Theory]
    [InlineData("bool", "true", "value ? 42 : 0", NativeRuntimePolicy.FailFast)]
    [InlineData("char16", "char16(42)", "int32(value)", NativeRuntimePolicy.FailFast)]
    [InlineData("int32", "40 + 2", "value", NativeRuntimePolicy.FailFast)]
    [InlineData("uint32", "uint32(42)", "int32(value)", NativeRuntimePolicy.FailFast)]
    [InlineData("int64", "int64(42)", "int32(value)", NativeRuntimePolicy.FailFast)]
    [InlineData("uint64", "uint64(42)", "int32(value)", NativeRuntimePolicy.FailFast)]
    [InlineData("float32", "float32(42)", "value == float32(42) ? 42 : 0", NativeRuntimePolicy.FailFast)]
    [InlineData("float64", "float64(42)", "value == float64(42) ? 42 : 0", NativeRuntimePolicy.FailFast)]
    [InlineData("bool", "true", "value ? 42 : 0", NativeRuntimePolicy.CheckedExceptions)]
    [InlineData("char16", "char16(42)", "int32(value)", NativeRuntimePolicy.CheckedExceptions)]
    [InlineData("int32", "40 + 2", "value", NativeRuntimePolicy.CheckedExceptions)]
    [InlineData("uint32", "uint32(42)", "int32(value)", NativeRuntimePolicy.CheckedExceptions)]
    [InlineData("int64", "int64(42)", "int32(value)", NativeRuntimePolicy.CheckedExceptions)]
    [InlineData("uint64", "uint64(42)", "int32(value)", NativeRuntimePolicy.CheckedExceptions)]
    [InlineData("float32", "float32(42)", "value == float32(42) ? 42 : 0", NativeRuntimePolicy.CheckedExceptions)]
    [InlineData("float64", "float64(42)", "value == float64(42) ? 42 : 0", NativeRuntimePolicy.CheckedExceptions)]
    public void BorrowsAndMaterializesSupportedScalarReferences(string type, string expression, string read,
        NativeRuntimePolicy policy)
    {
        var source = $$"""
            int32 Read(implicit ref<{{type}}> value) { return {{read}}; }
            int32 Main() {
                mut {{type}} local = {{expression}};
                return Read(local) + Read({{expression}});
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(84, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Fact]
    public void ImplicitReferenceParameterIsReadonly()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Assign(implicit ref<int32> value) {
                value = 42;
                return value;
            }
            int32 Main() {
                mut int32 value = 7;
                return Assign(value);
            }
            """));

        Assert.True(compilation.HasErrors);
    }

    [Fact]
    public void ImplicitReferenceCannotBorrowAnUninitializedLocal()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Read(implicit ref<int32> value) { return value; }
            int32 Main() {
                init int32 value;
                return Read(value);
            }
            """));

        Assert.Contains(compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3016");
    }

    [Fact]
    public void AggregateImplicitReferenceTypesAreRejectedByTheProvisionalGate()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Pair {
                public int32 Value;
                public Pair(int32 value) { Value = value; }
            }
            int32 Read(implicit ref<Pair> value) { return value.Value; }
            int32 Main() { mut Pair value = new Pair(42); return Read(value); }
            """));

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3011"
                && diagnostic.Message.Contains("requires a supported scalar",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void FieldPlacesAreRejectedByTheProvisionalScalarGate()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Pair {
                public int32 Value;
                public Pair(int32 value) { Value = value; }
            }
            int32 Read(implicit ref<int32> value) { return value; }
            int32 Main() {
                Pair pair = new Pair(42);
                return Read(pair.Value);
            }
            """));

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3011"
                && diagnostic.Message.Contains("cannot borrow field or indexed places",
                    StringComparison.Ordinal));
    }
}
