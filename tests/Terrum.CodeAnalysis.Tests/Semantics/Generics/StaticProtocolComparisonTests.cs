// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics.Generics;

public sealed class StaticProtocolComparisonTests
{
    private const string LessSource = """
        interface Less<A, B, C> { static C operator <(A left, B right); }
        interface OtherLess<A, B, C> { static C operator <(A left, B right); }
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
    [TestCase("==","op_Equality")]
    [TestCase("!=","op_Inequality")]
    [TestCase("<","op_LessThan")]
    [TestCase("<=","op_LessThanOrEqual")]
    [TestCase(">","op_GreaterThan")]
    [TestCase(">=","op_GreaterThanOrEqual")]
    public void ComparisonOperatorsHaveBoolResultsAndIndependentNames(string operation, string name)
    {
        var source = $$"""
            interface Comparison<A, B, C> { static C operator {{operation}}(A left, B right); }
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
        Assert.That(syntax.HasErrors, Is.False, string.Join(Environment.NewLine, syntax.Diagnostics));
        var compilation = Compilation.Create(syntax);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        SemanticVerifier.Verify(program);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function =>
            function.Name == "Value." + name && function.ReturnType == PrimitiveType.Bool),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function =>
            function.Name == "Compare<Value>" && function.ReturnType == PrimitiveType.Bool),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal)),Is.False);
    }
    [TestCase("struct Value : Less<Value, Value, bool>","struct Value : Less<Value, Value, Value>","TRM3007")]
    [TestCase("struct Value : Less<Value, Value, bool>","struct Value : Less<Value, Value, int32>","TRM3007")]
    [TestCase("struct Value : Less<Value, Value, bool>","struct Value : Less<Value, Other, bool>","TRM3007")]
    [TestCase("public static bool operator <(Value left, Value right)",        "public static int32 operator <(Value left, Value right)","TRM3007")]
    [TestCase("public static bool operator <(Value left, Value right)",        "public static bool operator <(Value left, Other right)","TRM3007")]
    [TestCase("static C operator <(A left, B right)",        "static B operator <(A left, B right)","TRM3001")]
    [TestCase("where T : Less<T, T, bool>, [Copy]","where T : Less<T, T, T>, [Copy]","TRM3007")]
    [TestCase("where T : Less<T, T, bool>, [Copy]","where T : OtherLess<T, T, bool>, [Copy]","TRM3007")]
    [TestCase("where T : Less<T, T, bool>, [Copy]","where T : [Copy]","TRM3007")]
    [TestCase("return left < right;","return left == right;","TRM3007")]
    [TestCase("return left < right;","return left >= right;","TRM3007")]
    public void InvalidComparisonShapesAndMissingWitnessesAreRejected(
        string before, string after, string diagnostic)
    {
        Assert.That(LessSource, Does.Contain(before));
        var source = LessSource.Replace(before, after, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == diagnostic),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void NoThrowComparisonRequiresDeclaredAndVerifiedEffect(bool witnessNoThrow)
    {
        var source = LessSource.Replace("static C operator <",
            (witnessNoThrow ? "[NoThrow] " : "") + "static C operator <", StringComparison.Ordinal)
            .Replace("bool Compare<T>", "[NoThrow] bool Compare<T>", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void DistinctSymbolicTypesCannotBorrowComparisonFromEqualActuals()
    {
        var source = LessSource.Replace("bool Compare<T>(T left, T right) where T : Less<T, T, bool>, [Copy] {",
            "bool Compare<T, U>(T left, U right) where T : Less<T, T, bool>, [Copy] where U : [Copy] {",
            StringComparison.Ordinal)
            .Replace("Compare(new Value(20), new Value(22))",
                "Compare<Value, Value>(new Value(20), new Value(22))", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
    }

    [Test]
    public void UninstantiatedComparisonBodyStillNeedsItsOwnWitness()
    {
        var source = LessSource[..LessSource.IndexOf("int32 Main()", StringComparison.Ordinal)]
            .Replace("where T : Less<T, T, bool>, [Copy]", "where T : [Copy]", StringComparison.Ordinal)
            + "int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void ConcreteStructComparisonDoesNotRequireAProtocolWitness()
    {
        var source = LessSource.Replace("struct Value : Less<Value, Value, bool>", "struct Value",
            StringComparison.Ordinal).Replace(
                "bool Compare<T>(T left, T right) where T : Less<T, T, bool>, [Copy] {\n    return left < right;\n}",
                "", StringComparison.Ordinal)
            .Replace("Compare(new Value(20), new Value(22))",
                "new Value(20) < new Value(22)", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))());
    }

    [Test]
    public void PrimitiveOperandsDoNotAcquireNominalWitnesses()
    {
        var source = LessSource.Replace("Compare(new Value(20), new Value(22))",
            "Compare<int32>(20, 22)", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("""
        struct Plain {
            public int32 Number;
            public Plain(int32 value) { Number = value; }
        }
        int32 Main() {
            if (new Plain(1) == new Plain(1)) { return 42; }
            return 0;
        }
        """)]
    [TestCase("""
        int32 Main() {
            require ManagedNode.TryCreate(1, out var node);
            if (node == node) { return 42; }
            return 0;
        }
        """)]
    public void ComparisonDoesNotIntroduceUniversalOrManagedEquality(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void ComparisonEffectsComeFromTheDeclaredProtocolAndBody(bool valid)
    {
        var source = LessSource.Replace("static C operator <",
            "[NoThrow] static C operator <", StringComparison.Ordinal)
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
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            SemanticVerifier.Verify(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))());
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3017"),Is.True);
        }
    }

    [Test]
    public void IndependentComparisonConstraintsDriveConditionalCopyOutput()
    {
        var source = """
            interface Less<A, B, C> { static C operator <(A left, B right); }
            interface Greater<A, B, C> { static C operator >(A left, B right); }
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        SemanticVerifier.Verify(program);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.Name == "TryChoose<First, Second>"),Is.True);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3016"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void VerifierRejectsForgedComparisonReturnType()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(LessSource));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        var index = Array.FindIndex(program.Functions.ToArray(), static function =>
            function.Name == "Value.op_LessThan");
        Assert.That(index >= 0, Is.True);
        var forged = program with {
            Functions = program.Functions.SetItem(index, program.Functions[index] with {
                ReturnType = PrimitiveType.Int32,
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(forged, NativeRuntimePolicy.FailFast))!;
    }
}
