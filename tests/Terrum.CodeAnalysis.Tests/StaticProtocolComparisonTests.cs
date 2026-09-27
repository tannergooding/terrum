// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProtocolComparisonTests
{
    private const string LessSource = """
        interface Less<A, B, C> { static abstract C operator <(A left, B right); }
        interface OtherLess<A, B, C> { static abstract C operator <(A left, B right); }
        struct Value : Less<Value, Value, bool> {
            public int32 Number;
            public Value(int32 number) { Number = number; }
            public static bool operator <(Value left, Value right) {
                return left.Number < right.Number;
            }
        }
        struct Other {
            public int32 Number;
            public Other(int32 number) { Number = number; }
        }
        bool Compare<T>(T left, T right) where T : Less<T, T, bool>, [Copy] {
            return left < right;
        }
        int32 Main() {
            if (Compare(new Value(20), new Value(22))) { return 42; }
            return 0;
        }
        """;

    [Theory]
    [InlineData("==", "op_Equality")]
    [InlineData("!=", "op_Inequality")]
    [InlineData("<", "op_LessThan")]
    [InlineData("<=", "op_LessThanOrEqual")]
    [InlineData(">", "op_GreaterThan")]
    [InlineData(">=", "op_GreaterThanOrEqual")]
    public void ComparisonOperatorsHaveBoolResultsAndIndependentNames(string operation, string name)
    {
        var source = $$"""
            interface Comparison<A, B, C> { static abstract C operator {{operation}}(A left, B right); }
            struct Value : Comparison<Value, Value, bool> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static bool operator {{operation}}(Value left, Value right) {
                    return left.Number {{operation}} right.Number;
                }
            }
            bool Compare<T>(T left, T right) where T : Comparison<T, T, bool>, [Copy] {
                return left {{operation}} right;
            }
            int32 Main() {
                if (Compare(new Value(20), new Value(22))) { return 42; }
                return 0;
            }
            """;
        var syntax = SyntaxTree.Parse(source);
        Assert.False(syntax.HasErrors, string.Join(Environment.NewLine, syntax.Diagnostics));
        var compilation = Compilation.Create(syntax);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        SemanticVerifier.Verify(program);
        Assert.Contains(program.Functions, function =>
            function.Name == "Value." + name && function.ReturnType == PrimitiveType.Bool);
        Assert.Contains(program.Functions, function =>
            function.Name == "Compare<Value>" && function.ReturnType == PrimitiveType.Bool);
        Assert.DoesNotContain(program.Functions, function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("struct Value : Less<Value, Value, bool>", "struct Value : Less<Value, Value, Value>", "TRM3007")]
    [InlineData("struct Value : Less<Value, Value, bool>", "struct Value : Less<Value, Value, int32>", "TRM3007")]
    [InlineData("struct Value : Less<Value, Value, bool>", "struct Value : Less<Value, Other, bool>", "TRM3007")]
    [InlineData("public static bool operator <(Value left, Value right)",
        "public static int32 operator <(Value left, Value right)", "TRM3007")]
    [InlineData("public static bool operator <(Value left, Value right)",
        "public static bool operator <(Value left, Other right)", "TRM3007")]
    [InlineData("static abstract C operator <(A left, B right)",
        "static abstract B operator <(A left, B right)", "TRM3001")]
    [InlineData("where T : Less<T, T, bool>, [Copy]", "where T : Less<T, T, T>, [Copy]", "TRM3007")]
    [InlineData("where T : Less<T, T, bool>, [Copy]", "where T : OtherLess<T, T, bool>, [Copy]", "TRM3007")]
    [InlineData("where T : Less<T, T, bool>, [Copy]", "where T : [Copy]", "TRM3007")]
    [InlineData("return left < right;", "return left == right;", "TRM3007")]
    [InlineData("return left < right;", "return left >= right;", "TRM3007")]
    public void InvalidComparisonShapesAndMissingWitnessesAreRejected(
        string before, string after, string diagnostic)
    {
        Assert.Contains(before, LessSource, StringComparison.Ordinal);
        var source = LessSource.Replace(before, after, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, item => item.Code == diagnostic);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoThrowComparisonRequiresDeclaredAndVerifiedEffect(bool witnessNoThrow)
    {
        var source = LessSource.Replace("static abstract C operator <",
            (witnessNoThrow ? "[NoThrow] " : "") + "static abstract C operator <", StringComparison.Ordinal)
            .Replace("bool Compare<T>", "[NoThrow] bool Compare<T>", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void DistinctSymbolicTypesCannotBorrowComparisonFromEqualActuals()
    {
        var source = LessSource.Replace("bool Compare<T>(T left, T right) where T : Less<T, T, bool>, [Copy] {",
            "bool Compare<T, U>(T left, U right) where T : Less<T, T, bool>, [Copy] where U : [Copy] {",
            StringComparison.Ordinal)
            .Replace("Compare(new Value(20), new Value(22))",
                "Compare<Value, Value>(new Value(20), new Value(22))", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
    }

    [Fact]
    public void UninstantiatedComparisonBodyStillNeedsItsOwnWitness()
    {
        var source = LessSource[..LessSource.IndexOf("int32 Main()", StringComparison.Ordinal)]
            .Replace("where T : Less<T, T, bool>, [Copy]", "where T : [Copy]", StringComparison.Ordinal)
            + "int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void ConcreteStructComparisonDoesNotRequireAProtocolWitness()
    {
        var source = LessSource.Replace("struct Value : Less<Value, Value, bool>", "struct Value",
            StringComparison.Ordinal).Replace(
                "bool Compare<T>(T left, T right) where T : Less<T, T, bool>, [Copy] {\n    return left < right;\n}",
                "", StringComparison.Ordinal)
            .Replace("Compare(new Value(20), new Value(22))",
                "new Value(20) < new Value(22)", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(Assert.IsType<BoundProgram>(compilation.Program));
    }

    [Fact]
    public void PrimitiveOperandsDoNotAcquireNominalWitnesses()
    {
        var source = LessSource.Replace("Compare(new Value(20), new Value(22))",
            "Compare<int32>(20, 22)", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("""
        struct Plain {
            public int32 Number;
            public Plain(int32 value) { Number = value; }
        }
        int32 Main() {
            if (new Plain(1) == new Plain(1)) { return 42; }
            return 0;
        }
        """)]
    [InlineData("""
        int32 Main() {
            require ManagedNode.TryCreate(1, out var node);
            if (node == node) { return 42; }
            return 0;
        }
        """)]
    public void ComparisonDoesNotIntroduceUniversalOrManagedEquality(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComparisonEffectsComeFromTheDeclaredProtocolAndBody(bool valid)
    {
        var source = LessSource.Replace("static abstract C operator <",
            "[NoThrow] static abstract C operator <", StringComparison.Ordinal)
            .Replace("public static bool operator <",
                "[NoThrow] public static bool operator <", StringComparison.Ordinal)
            .Replace("bool Compare<T>", "[NoThrow] bool Compare<T>", StringComparison.Ordinal);
        if (!valid)
        {
            source = source.Replace("return left.Number < right.Number;",
                "require false; return left.Number < right.Number;", StringComparison.Ordinal);
        }
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        if (valid)
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            SemanticVerifier.Verify(Assert.IsType<BoundProgram>(compilation.Program));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3017");
        }
    }

    [Fact]
    public void IndependentComparisonConstraintsDriveConditionalCopyOutput()
    {
        var source = """
            interface Less<A, B, C> { static abstract C operator <(A left, B right); }
            interface Greater<A, B, C> { static abstract C operator >(A left, B right); }
            struct First : Less<First, First, bool> {
                public int32 Value;
                public First(int32 value) { Value = value; }
                public static bool operator <(First a, First b) { return a.Value < b.Value; }
            }
            struct Second : Greater<Second, Second, bool> {
                public int32 Value;
                public Second(int32 value) { Value = value; }
                public static bool operator >(Second a, Second b) { return a.Value > b.Value; }
            }
            bool TryChoose<T, U>(T first, T second, U left, U right, out T result)
                where T : Less<T, T, bool>, [Copy] where U : Greater<U, U, bool>, [Copy] {
                if (first < second && left > right) {
                    result = first;
                    return true;
                }
                return false;
            }
            int32 Main() {
                require TryChoose(new First(20), new First(22), new Second(4), new Second(2),
                    out var selected);
                require !TryChoose(new First(22), new First(20), new Second(4), new Second(2),
                    out var vacant);
                return selected.Value + 22;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        SemanticVerifier.Verify(program);
        Assert.Contains(program.Functions, function => function.Name == "TryChoose<First, Second>");
    }

    [Fact]
    public void ComparisonDrivenFalseOutputRemainsVacant()
    {
        var source = LessSource[..LessSource.IndexOf("int32 Main()", StringComparison.Ordinal)]
            + """
            bool TryChoose<T>(T left, T right, out T result) where T : Less<T, T, bool>, [Copy] {
                if (left < right) { result = left; return true; }
                return false;
            }
            int32 Main() {
                if (TryChoose(new Value(22), new Value(20), out var absent)) { return 0; }
                return absent.Number;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3016");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void VerifierRejectsForgedComparisonReturnType()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(LessSource));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var index = Array.FindIndex(program.Functions.ToArray(), static function =>
            function.Name == "Value.op_LessThan");
        Assert.True(index >= 0);
        var forged = program with {
            Functions = program.Functions.SetItem(index, program.Functions[index] with {
                ReturnType = PrimitiveType.Int32,
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(forged, NativeRuntimePolicy.FailFast));
    }
}
