// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProtocolExtendedTests
{
    private const string Source = """
        interface BitMask<A, B, C> { static C operator &(A left, B right); }
        interface OtherBitMask<A, B, C> { static C operator &(A left, B right); }
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
    [TestCase("struct Value : BitMask<Value, Value, Value>","struct Value","TRM3007")]
    [TestCase("struct Value : BitMask<Value, Value, Value>","struct Value : BitMask<Value, Other, Value>","TRM3007")]
    [TestCase("public static Value operator &(Value left, Value right)","public static Value operator &(Value left, Other right)","TRM3007")]
    [TestCase("public static Value operator &(Value left, Value right)","private static Value operator &(Value left, Value right)","TRM3007")]
    [TestCase("static C operator &(A left, B right)","static B operator &(A left, B right)","TRM3001")]
    [TestCase("static C operator &(A left, B right)","[NoThrow] static C operator &(A left, B right)","TRM3007")]
    [TestCase("where T : BitMask<T, T, T>, [Copy]","where T : [Copy]","TRM3007")]
    [TestCase("where T : BitMask<T, T, T>, [Copy]","where T : BitMask<T, T, T>, OtherBitMask<T, T, T>, [Copy]","TRM3007")]
    [TestCase("return left & right;","return left ^ right;","TRM3007")]
    [TestCase("return Apply(new Value(40), new Value(2)).Number;","Apply(new Value(40), new Other(2)); return 0;","TRM3007")]
    [TestCase("return Apply(new Value(40), new Value(2)).Number;","Apply<int32>(40, 2); return 0;","TRM3007")]
    [TestCase("T Apply<T>","[NoThrow] T Apply<T>","TRM3007")]
    public void FalseExtendedOperatorContractsDoNotBorrowConcreteCapabilities(
        string before, string after, string expected)
    {
        Assert.That(Source, Does.Contain(before));
        var compilation = Compilation.Create(SyntaxTree.Parse(Source.Replace(before, after, StringComparison.Ordinal)));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == expected),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(false)]
    [TestCase(true)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(program.Functions.Count(function =>
            function.Name.Contains('<', StringComparison.Ordinal)), Is.EqualTo(instantiate ? 2 : 0));
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal)),Is.False);
    }
    [TestCase("where U : [Copy]")]
    [TestCase("where U : OtherBitMask<U, U, U>, [Copy]")]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NoThrowWitnessCannotClaimCheckedDivisionIsInfallible()
    {
        var source = """
            interface Divide<A, B, C> {
                [NoThrow] static C operator /(A left, B right);
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3017"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
}
