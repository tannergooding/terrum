// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtendedStaticProtocolSampleAgreesBetweenAotAndJitAsync(bool checkedFailures)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "static-protocol-binary.terrum")).ConfigureAwait(true);
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("-", 38, "op_Subtraction")]
    [InlineData("/", 20, "op_Division")]
    [InlineData("%", 0, "op_Modulus")]
    [InlineData("&", 0, "op_BitwiseAnd")]
    [InlineData("|", 42, "op_BitwiseOr")]
    [InlineData("^", 42, "op_ExclusiveOr")]
    public async Task ExtendedStaticOperatorsSpecializeCopyStructsAsync(string op, int result, string operatorName)
    {
        var source = $$"""
            interface Operation<A, B, C> { static C operator {{op}}(A left, B right); }
            struct Value : Operation<Value, Value, Value> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator {{op}}(Value left, Value right) {
                    return new Value(left.Number {{op}} right.Number);
                }
            }
            T Apply<T>(T left, T right) where T : Operation<T, T, T>, [Copy] {
                return left {{op}} right;
            }
            int32 Main() { return Apply(new Value(40), new Value(2)).Number; }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var functions = Assert.IsType<BoundProgram>(compilation.Program).Functions;
        Assert.Contains(functions, function => function.Name == "Value." + operatorName);
        Assert.Contains(functions,
            function => function.Name == "Apply<Value>");
        await AssertFailFastExecutionAsync(source, result).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, result).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("/", "0")]
    [InlineData("%", "0")]
    [InlineData("/", "-1")]
    [InlineData("%", "-1")]
    [InlineData("-", "1")]
    public Task ExtendedArithmeticWitnessPropagatesCheckedFailureAsync(string op, string divisor)
    {
        var dividend = divisor == "-1" || op == "-" ? "-2147483648" : "40";
        var source = $$"""
            interface Operation<A, B, C> { static C operator {{op}}(A left, B right); }
            struct Value : Operation<Value, Value, Value> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator {{op}}(Value left, Value right) {
                    return new Value(left.Number {{op}} right.Number);
                }
            }
            T Apply<T>(T left, T right) where T : Operation<T, T, T>, [Copy] {
                return left {{op}} right;
            }
            int32 Main() {
                unsafe {
                    try { return Apply<Value>(new Value({{dividend}}), new Value({{divisor}})).Number; }
                    catch { return 42; }
                }
            }
            """;
        return AssertCheckedExecutionAsync(source, op == "%" && divisor == "-1" ? 0 : 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DeclaredNoThrowBitwiseWitnessProvesNoThrowGenericAsync(bool checkedFailures)
    {
        const string Source = """
            interface Mask<A, B, C> {
                [NoThrow] static C operator &(A left, B right);
            }
            struct Value : Mask<Value, Value, Value> {
                public int32 Number;
                [NoThrow] public Value(int32 number) { Number = number; }
                [NoThrow] public static Value operator &(Value left, Value right) {
                    return new Value(left.Number & right.Number);
                }
            }
            [NoThrow] T Apply<T>(T left, T right) where T : Mask<T, T, T>, [Copy] {
                return left & right;
            }
            int32 Main() { return Apply(new Value(42), new Value(63)).Number; }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
}
