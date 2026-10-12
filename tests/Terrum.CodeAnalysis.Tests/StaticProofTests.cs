// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProofTests
{
    [Fact]
    public void ProvenBodyChecksEraseWhileOrdinaryRequireRemainsExecutable()
    {
        const string Source = """
            [ConstEval] bool True() { return true; }
            int32 Main() {
                const bool known = True();
                static require known;
                static ensure True();
                require known;
                return 42;
            }
            """;
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(Source, tree.Root.ToFullString());
        _ = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.StaticRequireStatement);
        _ = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.StaticEnsureStatement);
        var version = Assert.IsAssignableFrom<System.Reflection.FieldInfo>(
            typeof(SyntaxTree).GetField(nameof(SyntaxTree.LanguageVersion)));
        Assert.Equal("syntax-46", version.GetRawConstantValue());

        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "Main");
        Assert.Collection(main.Body.Statements,
            statement => Assert.IsType<BoundLocal>(statement),
            statement => Assert.IsType<BoundRequire>(statement),
            statement => Assert.IsType<BoundReturn>(statement));
    }

    [Fact]
    public void OuterEnsureIsIndependentOfEarlierNormalReturnPaths()
    {
        const string Source = """
            int32 Main() {
                if (true) { return 42; }
                static ensure true;
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions);
        Assert.DoesNotContain(main.Body.Statements,
            statement => statement.GetType().Name.Contains("Static", StringComparison.Ordinal));
    }

    [Fact]
    public void StaticRequireUsesExactLocalPredicatesOnConditionalEdges()
    {
        const string Source = """
            int32 Verify(int32 input) {
                mut int32 value = input;
                mut bool enabled = true;
                if (value > 0) {
                    static require value > 0;
                } else {
                    static require value <= 0;
                }
                if (value > 0) {
                    static require value > 0;
                } else if (value == 0) {
                    static require value == 0;
                } else if (value < 0) {
                    static require value < 0;
                }
                if (0 < value) {
                    static require value > 0;
                }
                if (value != 1) {
                    static require value != 1;
                } else {
                    static require value == 1;
                }
                if (value > 0 && (value < 10 && enabled)) {
                    static require value > 0;
                    static require value < 10;
                    static require enabled;
                    static require value > 0 && (value < 10 && enabled);
                }
                if (value <= 0 || value >= 10) {
                    int32 ignored = 0;
                } else {
                    static require value > 0;
                    static require value < 10;
                }
                if (enabled) {
                    static require enabled;
                } else {
                    static require !enabled;
                }
                return 42;
            }
            int32 Main() { return Verify(1); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public void StaticRequireUsesKnownOperandsToDisambiguateShortCircuitEdges()
    {
        const string Source = """
            int32 LeftKnown(int32 input) {
                mut int32 value = input;
                mut bool enabled = false;
                if (value > 0) {
                    if (value > 0 && enabled) { } else {
                        static require !enabled;
                    }
                }
                return 42;
            }
            int32 RightKnown(int32 input) {
                mut int32 value = input;
                mut bool enabled = false;
                if (enabled) {
                    if (value > 0 && enabled) { } else {
                        static require value <= 0;
                    }
                }
                return 42;
            }
            int32 OrLeftKnown(int32 input) {
                mut int32 value = input;
                mut bool enabled = false;
                if (enabled) {
                    if (!enabled || value > 0) {
                        static require value > 0;
                    }
                }
                return 42;
            }
            int32 OrRightKnown(int32 input) {
                mut int32 value = input;
                mut bool enabled = false;
                if (value <= 0) {
                    if (enabled || value > 0) {
                        static require enabled;
                    }
                }
                return 42;
            }
            int32 Main() {
                return LeftKnown(1) + RightKnown(1) + OrLeftKnown(1) + OrRightKnown(1);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void StaticRequireRefinesRecursiveShortCircuitOperands(NativeRuntimePolicy policy)
    {
        const string Source = """
            int32 AndCompoundLeft(int32 input) {
                mut int32 value = input;
                mut bool enabled = true;
                if (value > 0 && enabled) {
                    if ((value > 0 && enabled) && value < 10) { }
                    else { static require value >= 10; }
                }
                return 42;
            }
            int32 AndCompoundRight(int32 input) {
                mut int32 value = input;
                mut bool enabled = true;
                if (value > 0 && enabled) {
                    if (value < 10 && (value > 0 && enabled)) { }
                    else { static require value >= 10; }
                }
                return 42;
            }
            int32 OrCompoundLeft(int32 input) {
                mut int32 value = input;
                mut bool enabled = false;
                if (value <= 0 && !enabled) {
                    if ((value > 0 || enabled) || value < 10) {
                        static require value < 10;
                    }
                }
                return 42;
            }
            int32 OrCompoundRight(int32 input) {
                mut int32 value = input;
                mut bool enabled = false;
                if (value <= 0 && !enabled) {
                    if (value < 10 || (value > 0 || enabled)) {
                        static require value < 10;
                    }
                }
                return 42;
            }
            int32 NestedComparisons(int32 input) {
                mut int32 value = input;
                if (value > 0 && value < 10) {
                    if ((value > 0 && value < 10) && value > 5) { }
                    else { static require value <= 5; }
                }
                return 42;
            }
            int32 Main() {
                return AndCompoundLeft(10) + AndCompoundRight(10)
                    + OrCompoundLeft(-1) + OrCompoundRight(-1) + NestedComparisons(3);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(210, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void StaticRequireUsesExactFloatingPredicatesWithoutOrderingComplements(NativeRuntimePolicy policy)
    {
        const string Source = """
            int32 Verify32(float32 input) {
                mut float32 value = input;
                if (value == float32(0)) {
                    static require value == float32(0);
                } else {
                    static require !(value == float32(0));
                }
                if (value != float32(0)) {
                    static require value != float32(0);
                } else {
                    static require !(value != float32(0));
                }
                if (value < float32(0)) {
                    static require value < float32(0);
                } else {
                    static require !(value < float32(0));
                }
                if (value <= float32(0)) {
                    static require value <= float32(0);
                } else {
                    static require !(value <= float32(0));
                }
                if (value > float32(0)) {
                    static require value > float32(0);
                } else {
                    static require !(value > float32(0));
                }
                if (value >= float32(0)) {
                    static require value >= float32(0);
                } else {
                    static require !(value >= float32(0));
                }
                if (float32(0) < value) {
                    static require value > float32(0);
                } else {
                    static require !(float32(0) < value);
                }
                return 42;
            }
            int32 Verify64(float64 input) {
                mut float64 value = input;
                if (value == float64(0)) {
                    static require value == float64(0);
                } else {
                    static require !(value == float64(0));
                }
                if (value != float64(0)) {
                    static require value != float64(0);
                } else {
                    static require !(value != float64(0));
                }
                if (value < float64(0)) {
                    static require value < float64(0);
                } else {
                    static require !(value < float64(0));
                }
                if (value <= float64(0)) {
                    static require value <= float64(0);
                } else {
                    static require !(value <= float64(0));
                }
                if (value > float64(0)) {
                    static require value > float64(0);
                } else {
                    static require !(value > float64(0));
                }
                if (value >= float64(0)) {
                    static require value >= float64(0);
                } else {
                    static require !(value >= float64(0));
                }
                if (float64(0) < value) {
                    static require value > float64(0);
                } else {
                    static require !(float64(0) < value);
                }
                return 42;
            }
            int32 Main() {
                return Verify32(float32(0) / float32(0))
                    + Verify32(float32(0)) + Verify32(float32(1)) + Verify32(float32(-1))
                    + Verify64(float64(0) / float64(0))
                    + Verify64(float64(0)) + Verify64(float64(1)) + Verify64(float64(-1));
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(336, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Fact]
    public void StaticRequireDoesNotInferFloatOrderingComplements()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(float32 input) {
                mut float32 value = input;
                if (value > float32(0)) { }
                else { static require value <= float32(0); }
                return 42;
            }
            int32 Main() { return Verify(float32(0) / float32(0)); }
            """));

        Assert.Contains(compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3026"
                && diagnostic.Message.Contains("not established", StringComparison.Ordinal));
    }

    [Fact]
    public void StaticRequireNormalizesReversedScalarComparisons()
    {
        const string Source = """
            int32 Int32Cases(int32 input) {
                mut int32 value = input;
                if (1 < value) {
                    static require value > 1;
                } else {
                    static require value <= 1;
                }
                if (1 <= value) {
                    static require value >= 1;
                } else {
                    static require value < 1;
                }
                if (1 > value) {
                    static require value < 1;
                } else {
                    static require value >= 1;
                }
                if (1 >= value) {
                    static require value <= 1;
                } else {
                    static require value > 1;
                }
                return 0;
            }
            int32 Int64Cases(int64 input) {
                mut int64 value = input;
                if (1 < value) {
                    static require value > 1;
                } else {
                    static require value <= 1;
                }
                return 0;
            }
            int32 UInt32Cases(uint32 input) {
                mut uint32 value = input;
                if (1 <= value) {
                    static require value >= 1;
                } else {
                    static require value < 1;
                }
                return 0;
            }
            int32 UInt64Cases(uint64 input) {
                mut uint64 value = input;
                if (1 > value) {
                    static require value < 1;
                } else {
                    static require value >= 1;
                }
                return 0;
            }
            int32 Char16Cases(char16 input) {
                mut char16 value = input;
                if (char16(1) >= value) {
                    static require value <= char16(1);
                } else {
                    static require value > char16(1);
                }
                return 0;
            }
            int32 Main() { return 42; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public void StaticRequireRejectsFloatingPointOrderingComplements()
    {
        const string Source = """
            int32 Verify(float32 input) {
                mut float32 value = input;
                if (value > float32(0)) {
                    static require value > float32(0);
                } else {
                    static require value <= float32(0);
                }
                return 0;
            }
            int32 Main() { return Verify(float32(0)); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.Equal(1, compilation.Diagnostics.Length);
        Assert.All(compilation.Diagnostics, diagnostic => Assert.Equal("TRM3026", diagnostic.Code));
    }

    [Theory]
    [InlineData("if (value > 0) { static require value <= 0; }", "TRM3025")]
    [InlineData("if (value > 0) {} static require value > 0;", "TRM3026")]
    [InlineData("if (value > 0) { value = 0; static require value > 0; }", "TRM3026")]
    [InlineData("if (input > 0) { static require input > 0; }", "TRM3026")]
    [InlineData("Runtime(); if (value > 0) { static require value > 0; }", "TRM3026")]
    [InlineData("if (value > 0) { Runtime(); static require value > 0; }", "TRM3026")]
    [InlineData("if (NoThrowPredicate(value)) { static require value > 0; }", "TRM3026")]
    [InlineData("if (value > 0 || value < 10) { static require value > 0; }", "TRM3026")]
    [InlineData("if (value > 0 && value < 10) {} else { static require value <= 0; }", "TRM3026")]
    [InlineData("if (value > 0 && value <= 0) { static require value > 0; }", "TRM3026")]
    [InlineData("mut bool enabled = true; if (value > 0 && enabled) {} else { static require !enabled; }", "TRM3026")]
    [InlineData("mut bool enabled = true; if (enabled || value > 0) { static require value > 0; }", "TRM3026")]
    public void StaticRequireDoesNotUseContradictedOrUnstableFacts(string statements, string code)
    {
        var source = """
            bool Runtime() { return true; }
            [NoThrow] bool NoThrowPredicate(implicit ref<int32> value) { return value > 0; }
            int32 Verify(int32 input) {
                mut int32 value = input;
                STATEMENTS
                return 42;
            }
            int32 Main() { return Verify(1); }
            """.Replace("STATEMENTS", statements, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("static require false;", "TRM3025", "evaluates to false")]
    [InlineData("static ensure false;", "TRM3025", "evaluates to false")]
    [InlineData("static require input == 42;", "TRM3026", "not established")]
    [InlineData("static ensure input == 42;", "TRM3026", "not established")]
    [InlineData("static require int32(1) / int32(0) == 0;", "TRM3026", "checked arithmetic")]
    [InlineData("static require Recurse(100) == 0;", "TRM3026", "call depth")]
    [InlineData("static require Runtime();", "TRM3027", "unsupported effects")]
    [InlineData("static ensure Runtime();", "TRM3027", "unsupported effects")]
    [InlineData("const bool known = true; static ensure known;", "TRM3026", "not established")]
    public void FalseUnknownAndUnsupportedProofsHaveDistinctDiagnostics(string statement, string code, string text)
    {
        var source = """
            [ConstEval] int32 Recurse(int32 remaining) {
                if (remaining == 0) { return 0; }
                return 1 + Recurse(remaining - 1);
            }
            bool Runtime() { return true; }
            int32 Verify(int32 input) { STATEMENT return 42; }
            int32 Main() { return Verify(42); }
            """.Replace("STATEMENT", statement, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code
            && diagnostic.Message.Contains(text, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("if (true) { static ensure true; }", "TRM3026")]
    [InlineData("static ensure true;", "TRM3026")]
    public void NestedEnsureAndConstructorEnsureAreNotPointChecks(string statement, string code)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var source = statement.StartsWith("if", StringComparison.Ordinal)
            ? "int32 Main() { " + statement + " return 42; }"
            : "struct Box { public int32 Value; public Box(int32 value) { "
                + statement + " Value = value; } } int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void StaticProofsCannotBePublishedAsTopLevelClauses()
    {
        var tree = SyntaxTree.Parse("static require true; int32 Main() { return 42; }");
        Assert.True(tree.HasErrors);
    }

    [Fact]
    public void RuntimeParametersInGenericTemplatesDoNotBecomeProofs()
    {
        const string Source = """
            T Forward<T>(T value) where T : [Copy] {
                static require true;
                return value;
            }
            int32 Main() { return Forward(int32(42)); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3026");
    }
}
