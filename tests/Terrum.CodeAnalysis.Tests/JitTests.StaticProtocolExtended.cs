// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
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
    [TestCase("-",38,"op_Subtraction")]
    [TestCase("/",20,"op_Division")]
    [TestCase("%",0,"op_Modulus")]
    [TestCase("&",0,"op_BitwiseAnd")]
    [TestCase("|",42,"op_BitwiseOr")]
    [TestCase("^",42,"op_ExclusiveOr")]
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
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var functions = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))().Functions;
        Assert.That(global::System.Linq.Enumerable.Any(functions,function => function.Name == "Value." + operatorName),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(functions,function => function.Name == "Apply<Value>"),Is.True);
        await AssertFailFastExecutionAsync(source, result).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, result).ConfigureAwait(true);
    }
    [TestCase("/","0")]
    [TestCase("%","0")]
    [TestCase("/","-1")]
    [TestCase("%","-1")]
    [TestCase("-","1")]
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
    [TestCase(false)]
    [TestCase(true)]
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
