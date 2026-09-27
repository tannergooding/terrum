// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProtocolExtendedTests
{
    private const string Source = """
        interface BitMask<A, B, C> { static abstract C operator &(A left, B right); }
        interface OtherBitMask<A, B, C> { static abstract C operator &(A left, B right); }
        struct Value : BitMask<Value, Value, Value> {
            public int32 Number;
            public Value(int32 number) { Number = number; }
            public static Value operator &(Value left, Value right) {
                return new Value(left.Number & right.Number);
            }
        }
        struct Other {
            public int32 Number;
            public Other(int32 number) { Number = number; }
        }
        T Apply<T>(T left, T right) where T : BitMask<T, T, T>, [Copy] {
            return left & right;
        }
        int32 Main() { return Apply(new Value(40), new Value(2)).Number; }
        """;

    [Theory]
    [InlineData("struct Value : BitMask<Value, Value, Value>", "struct Value", "TRM3007")]
    [InlineData("struct Value : BitMask<Value, Value, Value>", "struct Value : BitMask<Value, Other, Value>", "TRM3007")]
    [InlineData("public static Value operator &(Value left, Value right)", "public static Value operator &(Value left, Other right)", "TRM3007")]
    [InlineData("public static Value operator &(Value left, Value right)", "private static Value operator &(Value left, Value right)", "TRM3007")]
    [InlineData("static abstract C operator &(A left, B right)", "static abstract B operator &(A left, B right)", "TRM3001")]
    [InlineData("static abstract C operator &(A left, B right)", "[NoThrow] static abstract C operator &(A left, B right)", "TRM3007")]
    [InlineData("where T : BitMask<T, T, T>, [Copy]", "where T : [Copy]", "TRM3007")]
    [InlineData("where T : BitMask<T, T, T>, [Copy]", "where T : BitMask<T, T, T>, OtherBitMask<T, T, T>, [Copy]", "TRM3007")]
    [InlineData("return left & right;", "return left ^ right;", "TRM3007")]
    [InlineData("return Apply(new Value(40), new Value(2)).Number;", "Apply(new Value(40), new Other(2)); return 0;", "TRM3007")]
    [InlineData("return Apply(new Value(40), new Value(2)).Number;", "Apply<int32>(40, 2); return 0;", "TRM3007")]
    [InlineData("T Apply<T>", "[NoThrow] T Apply<T>", "TRM3007")]
    public void FalseExtendedOperatorContractsDoNotBorrowConcreteCapabilities(
        string before, string after, string expected)
    {
        Assert.Contains(before, Source, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(Source.Replace(before, after, StringComparison.Ordinal)));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == expected);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferredNestedHelpersUseOnlyDeclaredMatchingWitnesses(bool instantiate)
    {
        var source = Source.Replace(
            "int32 Main() { return Apply(new Value(40), new Value(2)).Number; }",
            """
            U Forward<U>(U left, U right) where U : BitMask<U, U, U>, [Copy] {
                return Apply(left, right);
            }
            int32 Main() { return Forward(new Value(40), new Value(2)).Number; }
            """, StringComparison.Ordinal);
        if (!instantiate)
        {
            source = source.Replace("int32 Main() { return Forward(new Value(40), new Value(2)).Number; }",
                "int32 Main() { return 0; }", StringComparison.Ordinal);
        }
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(instantiate ? 2 : 0, program.Functions.Count(function =>
            function.Name.Contains('<', StringComparison.Ordinal)));
        Assert.DoesNotContain(program.Functions, function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("where U : [Copy]")]
    [InlineData("where U : OtherBitMask<U, U, U>, [Copy]")]
    public void CallerCannotBorrowMissingHelperWitnessFromConcreteStruct(string constraint)
    {
        var source = Source.Replace(
            "int32 Main() { return Apply(new Value(40), new Value(2)).Number; }",
            "U Forward<U>(U left, U right) " + constraint
                + " { return Apply(left, right); }"
                + " int32 Main() { Apply(new Value(40), new Value(2));"
                + " return Forward(new Value(40), new Value(2)).Number; }",
            StringComparison.Ordinal).Replace("struct Value : BitMask<Value, Value, Value>",
                "struct Value : BitMask<Value, Value, Value>, OtherBitMask<Value, Value, Value>",
                StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void NoThrowWitnessCannotClaimCheckedDivisionIsInfallible()
    {
        var source = """
            interface Divide<A, B, C> {
                [NoThrow] static abstract C operator /(A left, B right);
            }
            struct Value : Divide<Value, Value, Value> {
                public int32 Number;
                [NoThrow] public Value(int32 number) { Number = number; }
                [NoThrow] public static Value operator /(Value left, Value right) {
                    return new Value(left.Number / right.Number);
                }
            }
            [NoThrow] T Apply<T>(T left, T right) where T : Divide<T, T, T>, [Copy] {
                return left / right;
            }
            int32 Main() { return Apply(new Value(40), new Value(2)).Number; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3017");
        Assert.Null(compilation.Program);
    }
}
