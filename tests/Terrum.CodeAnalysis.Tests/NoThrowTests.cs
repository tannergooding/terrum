// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NoThrowTests
{
    [Fact]
    public void ParameterlessAttributesRetainLosslessSyntaxAndTraversal()
    {
        const string Source = "[ /* contract */ NoThrow ] public int32 Value() { return 42; }";
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors);
        Assert.Equal(Source, tree.Root.ToFullString());
        var attribute = Assert.Single(tree.Root.DescendantNodes(), static node => node.Kind == SyntaxKind.Attribute);
        Assert.Equal(3, attribute.Children.Length);
        Assert.Equal("NoThrow", Assert.IsType<SyntaxTokenElement>(attribute.Children[1]).Token.Text);
    }

    [Theory]
    [InlineData("[NoThrow")]
    [InlineData("[NoThrow(1)] void F() { }")]
    [InlineData("[] void F() { }")]
    [InlineData("[[NoThrow]] void F() { }")]
    public void MalformedAttributesRemainLossless(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.True(tree.HasErrors);
        Assert.Equal(source, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("[NoThrow] [NoThrow] int32 F() { return 0; }")]
    [InlineData("[Unknown] int32 F() { return 0; }")]
    [InlineData("[NoThrow] struct S { public int32 Value; }")]
    [InlineData("struct S { [NoThrow] public int32 Value; }")]
    public void UnknownDuplicateAndMisplacedContractsAreRejected(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3001");
    }

    [Theory]
    [InlineData("[NoThrow] void F() { require true || false; }")]
    [InlineData("[NoThrow] int32 F() { return -1; }")]
    [InlineData("[NoThrow] int32 F() { return -2147483648; }")]
    [InlineData("[NoThrow] int64 F() { return -9223372036854775808; }")]
    [InlineData("[NoThrow] bool F(int32 a, int32 b) { return a < b || a == b; }")]
    [InlineData("[NoThrow] int32 F(int32 a, int32 b) { return +(a & b) ^ ~a; }")]
    [InlineData("[NoThrow] int64 F(int32 value) { return int64(value); }")]
    [InlineData("[NoThrow] int64 F(uint32 value) { return int64(value); }")]
    [InlineData("[NoThrow] uint64 F(uint32 value) { return uint64(value); }")]
    [InlineData("[NoThrow] uint32 F(char16 value) { return uint32(value); }")]
    [InlineData("[NoThrow] int32 F(char16 value) { return int32(value); }")]
    [InlineData("[NoThrow] int64 F(char16 value) { return int64(value); }")]
    [InlineData("[NoThrow] uint64 F(char16 value) { return uint64(value); }")]
    [InlineData("[NoThrow] char16 F(char16 value) { return char16(value); }")]
    [InlineData("[NoThrow] bool F(bool success, out int32 value) { if (!success) { return false; } value = 42; return true; }")]
    [InlineData("[NoThrow] int32 F(bool choose) { if (choose) { return G(false); } return 42; } [NoThrow] int32 G(bool choose) { return F(choose); }")]
    public void InfallibleOperationsSatisfyDeclaredContracts(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.All(program.Functions, static function => Assert.True(function.IsNoThrow));
        SemanticVerifier.VerifyLibrary(program);
    }

    [Theory]
    [InlineData("int32", "1 + 2", PrimitiveType.Int32, 3L)]
    [InlineData("int32", "(1 + 2) * 14", PrimitiveType.Int32, 42L)]
    [InlineData("int32", "-(-42)", PrimitiveType.Int32, 42L)]
    [InlineData("int32", "~(1 & 2)", PrimitiveType.Int32, -1L)]
    [InlineData("int32", "int32.MinValue % -1", PrimitiveType.Int32, 0L)]
    [InlineData("int64", "int64.MinValue % -1", PrimitiveType.Int64, 0L)]
    [InlineData("uint32", "uint32.MaxValue / 4294967295", PrimitiveType.UInt32, 1L)]
    [InlineData("uint32", "uint32.MaxValue & uint32.MaxValue", PrimitiveType.UInt32, -1L)]
    [InlineData("uint64", "uint64.MaxValue / uint64.MaxValue", PrimitiveType.UInt64, 1L)]
    [InlineData("uint64", "uint64.MaxValue - 1", PrimitiveType.UInt64, -2L)]
    [InlineData("uint64", "uint64(uint32.MaxValue) + 1", PrimitiveType.UInt64, 4294967296L)]
    [InlineData("char16", "char16(int32(65535))", PrimitiveType.Char16, 65535L)]
    [InlineData("int32", "int32(uint32(42))", PrimitiveType.Int32, 42L)]
    [InlineData("int32", "+int32(42)", PrimitiveType.Int32, 42L)]
    [InlineData("uint32", "+uint32.MaxValue", PrimitiveType.UInt32, -1L)]
    [InlineData("uint64", "+uint64.MaxValue", PrimitiveType.UInt64, -1L)]
    [InlineData("int64", "-int64(42)", PrimitiveType.Int64, -42L)]
    [InlineData("int64", "~int64(0)", PrimitiveType.Int64, -1L)]
    [InlineData("uint32", "~uint32(0)", PrimitiveType.UInt32, -1L)]
    [InlineData("uint32", "~uint32.MaxValue", PrimitiveType.UInt32, 0L)]
    [InlineData("uint64", "~uint64(0)", PrimitiveType.UInt64, -1L)]
    [InlineData("uint64", "~uint64.MaxValue", PrimitiveType.UInt64, 0L)]
    [InlineData("int32", "int32.MinValue - 0", PrimitiveType.Int32, int.MinValue)]
    [InlineData("int64", "int64.MaxValue * 1", PrimitiveType.Int64, long.MaxValue)]
    [InlineData("uint32", "uint32.MaxValue % 4294967295", PrimitiveType.UInt32, 0L)]
    [InlineData("uint64", "uint64.MaxValue % uint64(100)", PrimitiveType.UInt64, 15L)]
    [InlineData("int32", "1 | 2", PrimitiveType.Int32, 3L)]
    [InlineData("int32", "1 ^ 3", PrimitiveType.Int32, 2L)]
    [InlineData("uint32", "uint32.MaxValue ^ uint32(1)", PrimitiveType.UInt32, -2L)]
    [InlineData("uint64", "uint64.MaxValue | uint64(0)", PrimitiveType.UInt64, -1L)]
    [InlineData("uint64", "uint64.MaxValue ^ uint64(1)", PrimitiveType.UInt64, -2L)]
    [InlineData("uint32", "uint32(int32.MaxValue) + 1", PrimitiveType.UInt32, int.MinValue)]
    [InlineData("int64", "int64(uint32.MaxValue)", PrimitiveType.Int64, 4294967295L)]
    [InlineData("uint64", "uint64(int64.MaxValue)", PrimitiveType.UInt64, long.MaxValue)]
    [InlineData("char16", "char16(uint64(65535))", PrimitiveType.Char16, 65535L)]
    public void SafeScalarExpressionsPersistAsTypedConstants(
        string resultType, string expression, PrimitiveType type, long value)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"[NoThrow] public {resultType} Fold() {{ return {expression}; }}"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions);
        var result = Assert.IsType<BoundReturn>(Assert.Single(function.Body.Statements));
        var constant = Assert.IsType<BoundConstant>(result.Value);
        Assert.Equal(type, constant.Type);
        Assert.Equal(value, constant.Value);
        Assert.Equal(expression.Length, constant.Span.Length);
    }

    [Theory]
    [InlineData("int32", "int32.MaxValue + 1")]
    [InlineData("int32", "int32.MinValue - 1")]
    [InlineData("int64", "int64.MaxValue * 2")]
    [InlineData("uint32", "uint32.MaxValue + 1")]
    [InlineData("uint64", "uint64.MaxValue + 1")]
    [InlineData("int32", "1 / 0")]
    [InlineData("int32", "int32.MinValue / -1")]
    [InlineData("int64", "int64.MinValue / -1")]
    [InlineData("char16", "char16(int32(-1))")]
    [InlineData("int32", "int32(uint32.MaxValue)")]
    [InlineData("int32", "(int32.MaxValue + 1) - 1")]
    [InlineData("uint32", "(uint32.MaxValue + 1) - 1")]
    [InlineData("int32", "0 * Read()")]
    [InlineData("int32", "1 + value")]
    [InlineData("uint32", "uint32(int32(-1))")]
    [InlineData("int64", "int64(uint64.MaxValue)")]
    [InlineData("char16", "char16(uint64(65536))")]
    public void UnsafeOrUnknownScalarExpressionsKeepTheirEffects(string resultType, string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var parameter = expression.Contains("value", StringComparison.Ordinal) ? "int32 value" : "";
        var callee = expression.Contains("Read()", StringComparison.Ordinal)
            ? "[NoThrow] int32 Read() { return 42; }" : "";
        var source = $"{resultType} Fold({parameter}) {{ return {expression}; }} {callee}";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("[NoThrow] " + source));
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3017");
        Assert.Null(compilation.Program);
        var baseline = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(baseline.HasErrors, string.Join(Environment.NewLine, baseline.Diagnostics));
        var function = Assert.Single(Assert.IsType<BoundProgram>(baseline.Program).Functions,
            static function => function.Name == "Fold");
        var result = Assert.IsType<BoundReturn>(Assert.Single(function.Body.Statements));
        Assert.IsNotType<BoundConstant>(result.Value);
    }

    [Theory]
    [InlineData("int32 Main() { var value = 1 + 2; return value; }", "TRM3014")]
    [InlineData("int32 Main() { return int32(1) + uint32(2); }", "TRM3007")]
    [InlineData("char16 Main() { return char16(1) + char16(2); }", "TRM3007")]
    public void FoldingDoesNotRepairInvalidOperandContexts(string source, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void FoldingCalleeBodyDoesNotStrengthenItsDeclaredEffect()
    {
        const string Callee = "int32 Callee() { return 1 + 2; }";
        var baseline = Compilation.CreateLibrary(SyntaxTree.Parse(Callee));
        Assert.False(baseline.HasErrors, string.Join(Environment.NewLine, baseline.Diagnostics));
        var callee = Assert.Single(Assert.IsType<BoundProgram>(baseline.Program).Functions);
        var statement = Assert.IsType<BoundReturn>(Assert.Single(callee.Body.Statements));
        Assert.Equal(3L, Assert.IsType<BoundConstant>(statement.Value).Value);
        Assert.False(callee.IsNoThrow);

        var caller = Compilation.CreateLibrary(SyntaxTree.Parse(
            Callee + " [NoThrow] int32 Caller() { return Callee(); }"));
        Assert.Null(caller.Program);
        Assert.Contains(caller.Diagnostics, static diagnostic => diagnostic.Code == "TRM3017");
    }

    [Fact]
    public void ConstantTrueComparisonSatisfiesNoThrowRequire()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[NoThrow] void Check() { require int32(1) == int32(1); }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.VerifyLibrary(Assert.IsType<BoundProgram>(compilation.Program));
    }

    [Theory]
    [InlineData("(true && true) == true")]
    [InlineData("(!false) == true")]
    public void PureBooleanFactsSatisfyNoThrowRequire(string expression)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"[NoThrow] void Check() {{ require {expression}; }}"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.VerifyLibrary(Assert.IsType<BoundProgram>(compilation.Program));
    }

    [Theory]
    [InlineData("!false", true)]
    [InlineData("!true", false)]
    [InlineData("!!true", true)]
    [InlineData("true && true", true)]
    [InlineData("true && false", false)]
    [InlineData("false && true", false)]
    [InlineData("false && false", false)]
    [InlineData("true || false", true)]
    [InlineData("false || true", true)]
    [InlineData("false || false", false)]
    [InlineData("true & true", true)]
    [InlineData("true & false", false)]
    [InlineData("false & false", false)]
    [InlineData("true | false", true)]
    [InlineData("false | false", false)]
    [InlineData("true ^ false", true)]
    [InlineData("true ^ true", false)]
    [InlineData("false ^ false", false)]
    [InlineData("((true && true) == (!false)) ^ false", true)]
    public void PureBooleanOperatorsBecomeTypedConstants(string expression, bool expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"[NoThrow] bool Fold() {{ return {expression}; }}"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions);
        var returned = Assert.IsType<BoundReturn>(Assert.Single(function.Body.Statements));
        var constant = Assert.IsType<BoundConstant>(returned.Value);
        Assert.Equal(PrimitiveType.Bool, constant.Type);
        Assert.Equal(expected ? 1L : 0L, constant.Value);
        Assert.Equal(expression.Length, constant.Span.Length);
    }

    [Theory]
    [InlineData("(!true) == false", true)]
    [InlineData("(true && false) == true", false)]
    [InlineData("(false | true) != true", false)]
    public void NestedBooleanRequirementsKeepLiteralFailurePolicy(string expression, bool expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"[NoThrow] void Check() {{ require {expression}; }}"));
        Assert.Equal(!expected, compilation.HasErrors);
        if (expected)
        {
            var function = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions);
            var requirement = Assert.IsType<BoundRequire>(function.Body.Statements[0]);
            Assert.Equal(1L, Assert.IsType<BoundConstant>(requirement.Condition).Value);
        }
        else
        {
            Assert.Null(compilation.Program);
            Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3017");
        }
    }

    [Theory]
    [InlineData("bool Fold(bool value) { return false && value; }", typeof(BoundRead))]
    [InlineData("bool Fold() { return true || Fail(); } bool Fail() { require false; return false; }", typeof(BoundCall))]
    [InlineData("bool Fold() { return false & Fail(); } bool Fail() { require false; return false; }", typeof(BoundCall))]
    [InlineData("bool Fold() { return false && int32.MaxValue + 1 > int32(0); }", typeof(BoundBinary))]
    public void UnknownAndFailingBooleanOperandsRemainInIr(string source, Type rightType)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            static function => function.Name == "Fold");
        var returned = Assert.IsType<BoundReturn>(Assert.Single(function.Body.Statements));
        var binary = Assert.IsType<BoundBinary>(returned.Value);
        Assert.Equal(rightType, binary.Right.GetType());
        if (binary.Right is BoundBinary comparison)
        {
            _ = Assert.IsType<BoundBinary>(comparison.Left);
        }
    }

    [Theory]
    [InlineData("[NoThrow] bool Fold() { return true || Fail(); } bool Fail() { return true; }", "TRM3017")]
    [InlineData("[NoThrow] bool Fold() { return false && int32.MaxValue + 1 > int32(0); }", "TRM3017")]
    [InlineData("bool Fold() { return false && Missing(); }", "TRM3011")]
    [InlineData("bool Fold() { return true || int32(1) == uint32(1); }", "TRM3007")]
    public void ConstantBooleanSidesDoNotHideInvalidOrEffectfulOperands(string source, string code)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("int32.MinValue == int32.MinValue", true)]
    [InlineData("int32.MinValue != int32.MinValue", false)]
    [InlineData("int32.MinValue < int32.MaxValue", true)]
    [InlineData("int32.MaxValue < int32.MinValue", false)]
    [InlineData("int32.MinValue <= int32.MinValue", true)]
    [InlineData("int32.MaxValue <= int32.MinValue", false)]
    [InlineData("int32.MaxValue > int32.MinValue", true)]
    [InlineData("int32.MinValue > int32.MaxValue", false)]
    [InlineData("int32.MaxValue >= int32.MaxValue", true)]
    [InlineData("int32.MinValue >= int32.MaxValue", false)]
    [InlineData("int64.MinValue < int64.MaxValue", true)]
    [InlineData("int64.MinValue >= int64.MaxValue", false)]
    [InlineData("uint32.MaxValue > uint32(0)", true)]
    [InlineData("uint32(0) > uint32.MaxValue", false)]
    [InlineData("uint32.MaxValue <= uint32.MaxValue", true)]
    [InlineData("uint32.MaxValue < uint32(0)", false)]
    [InlineData("uint64.MaxValue > uint64(int64.MaxValue)", true)]
    [InlineData("uint64.MaxValue < uint64(int64.MaxValue)", false)]
    [InlineData("uint64.MaxValue != uint64(0)", true)]
    [InlineData("uint64.MaxValue == uint64(0)", false)]
    [InlineData("char16(65535) >= char16(65535)", true)]
    [InlineData("char16(0) > char16(65535)", false)]
    [InlineData("char16(1) != char16(0)", true)]
    [InlineData("char16(1) == char16(0)", false)]
    [InlineData("true == true", true)]
    [InlineData("true != true", false)]
    [InlineData("true != false", true)]
    [InlineData("false == true", false)]
    [InlineData("(1 + 2) == int32(3)", true)]
    public void MatchingScalarConstantComparisonsBecomeBooleanConstants(string expression, bool expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"[NoThrow] bool Compare() {{ return {expression}; }}"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions);
        var returned = Assert.IsType<BoundReturn>(Assert.Single(function.Body.Statements));
        var constant = Assert.IsType<BoundConstant>(returned.Value);
        Assert.Equal(PrimitiveType.Bool, constant.Type);
        Assert.Equal(expected ? 1L : 0L, constant.Value);
        Assert.Equal(expression.Length, constant.Span.Length);
    }

    [Theory]
    [InlineData("int32(1) == int32(1)", true)]
    [InlineData("uint64.MaxValue >= uint64(0)", true)]
    [InlineData("char16(1) < char16(0)", false)]
    [InlineData("true != true", false)]
    public void NoThrowRequireKeepsBooleanLiteralFailurePolicy(string comparison, bool expected)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"[NoThrow] void Check() {{ require {comparison}; }}"));
        Assert.Equal(!expected, compilation.HasErrors);
        if (expected)
        {
            var function = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions);
            var requirement = Assert.IsType<BoundRequire>(function.Body.Statements[0]);
            Assert.Equal(1L, Assert.IsType<BoundConstant>(requirement.Condition).Value);
        }
        else
        {
            Assert.Null(compilation.Program);
            Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3017");
        }
    }

    [Theory]
    [InlineData("bool Compare(int32 value) { return value == 42; }")]
    [InlineData("[NoThrow] bool Compare() { return Read() == 42; } [NoThrow] int32 Read() { return 42; }")]
    [InlineData("bool Compare() { return int32.MaxValue + 1 == int32(0); }")]
    public void UnknownOrFailingComparisonsRetainExecutableOperands(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var function = Assert.Single(program.Functions, static function => function.Name == "Compare");
        var returned = Assert.IsType<BoundReturn>(Assert.Single(function.Body.Statements));
        var comparison = Assert.IsType<BoundBinary>(returned.Value);
        Assert.IsNotType<BoundConstant>(comparison.Left);
    }

    [Theory]
    [InlineData("bool Compare() { return int32(1) == uint32(1); }", "TRM3007")]
    [InlineData("bool Compare() { return char16(1) < int32(2); }", "TRM3007")]
    [InlineData("bool Compare() { return true < false; }", "TRM3007")]
    [InlineData("bool Compare() { var value = 1 == 2; return value; }", "TRM3007")]
    public void FoldingDoesNotRepairInvalidComparisons(string source, string code)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("[NoThrow] int32 Check() { if (int32(1) == int32(1)) { return 42; } return 1 / 0; }", "TRM3017")]
    [InlineData("int32 Check() { if (int32(1) != int32(1)) { return Missing(); } return 42; }", "TRM3011")]
    public void UnreachableComparisonBranchesRetainNormalChecks(string source, string code)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("int32(1) != int32(1)")]
    public void FoldedFalseConditionDoesNotInitializeUnreachedOutputs(string condition)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"bool Try(out int32 value) {{ return false; }} "
            + $"int32 Main() {{ if ({condition} && Try(out var output)) {{ }} return output; }}"));
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3016");
    }

    [Theory]
    [InlineData("int32 F(int32 value) { return value + 1; }")]
    [InlineData("int32 F(int32 value) { return value - 1; }")]
    [InlineData("int64 F(int64 value) { return value * 2; }")]
    [InlineData("uint64 F(uint64 value) { return value / 2; }")]
    [InlineData("int32 F(int32 value) { return value % 2; }")]
    [InlineData("int32 F(int32 value) { return -value; }")]
    [InlineData("int32 F() { return -int32.MinValue; }")]
    [InlineData("int64 F() { return -int64.MinValue; }")]
    [InlineData("char16 F(int32 value) { return char16(value); }")]
    [InlineData("uint32 F(int32 value) { return uint32(value); }")]
    [InlineData("int32 F(uint32 value) { return int32(value); }")]
    [InlineData("uint64 F(int64 value) { return uint64(value); }")]
    [InlineData("int64 F(uint64 value) { return int64(value); }")]
    [InlineData("int32 F(int64 value) { return int32(value); }")]
    [InlineData("void F(bool success) { require success; }")]
    [InlineData("int32 F() { return G(); } int32 G() { return 42; }")]
    public void SourceAndPublicIrCannotAssertUnprovedNoThrowContracts(string source)
    {
        var syntax = SyntaxTree.Parse("[NoThrow] " + source + " int32 Main() { return 0; }");
        Assert.False(syntax.HasErrors);
        var invalid = Compilation.Create(syntax);
        Assert.True(invalid.HasErrors);
        Assert.Null(invalid.Program);
        var diagnostic = Assert.Single(invalid.Diagnostics);
        Assert.Equal("TRM3017", diagnostic.Code);
        Assert.True(diagnostic.Span.Length > 0);

        var baseline = Compilation.Create(SyntaxTree.Parse(source + " int32 Main() { return 0; }"));
        Assert.False(baseline.HasErrors, string.Join(Environment.NewLine, baseline.Diagnostics));
        var program = Assert.IsType<BoundProgram>(baseline.Program);
        var index = program.Functions.IndexOf(program.Functions.Single(static function => function.Name == "F"));
        var forged = program with { Functions = program.Functions.SetItem(index, program.Functions[index] with { IsNoThrow = true }) };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("return Host.Data[1];")]
    [InlineData("Host.Data[1] = 1; return 0;")]
    [InlineData("int32 index = 0; return Host.Data[index];")]
    public void CheckedBufferAccessIsNotAnImplicitNoThrowCapability(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("[NoThrow] int32 Main() { " + body + " }"),
            [new HostBuffer("Data", PrimitiveType.Int32, [42], true)]);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3017");
    }

    [Fact]
    public void NoThrowDoesNotWaiveConditionalOutputAssignment()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] bool Try(bool success, out int32 value) {
                if (!success) { return false; }
                value = 42; return true;
            }
            [NoThrow] int32 Main() {
                if (Try(false, out var value)) { }
                return value;
            }
            """));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3016");
    }
}
