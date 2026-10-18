// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class NoThrowTests
{
    [Test]
    public void ParameterlessAttributesRetainLosslessSyntaxAndTraversal()
    {
        const string Source = "[ /* contract */ NoThrow ] public int32 Value() { return 42; }";
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(Source));
        var attribute = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), static node => node.Kind == SyntaxKind.Attribute);
        Assert.That(attribute.Children.Length, Is.EqualTo(3));
        Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual0 = attribute.Children[1]; Assert.That(__nunitActual0, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual0!; }))().Token.Text, Is.EqualTo("NoThrow"));
    }
    [TestCase("[NoThrow")]
    [TestCase("[NoThrow(1)] void F() { }")]
    [TestCase("[] void F() { }")]
    [TestCase("[[NoThrow]] void F() { }")]
    public void MalformedAttributesRemainLossless(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
    }
    [TestCase("[NoThrow] [NoThrow] int32 F() { return 0; }")]
    [TestCase("[Unknown] int32 F() { return 0; }")]
    [TestCase("[NoThrow] struct S { public int32 Value; }")]
    [TestCase("struct S { [NoThrow] public int32 Value; }")]
    public void UnknownDuplicateAndMisplacedContractsAreRejected(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3001"),Is.True);
    }
    [TestCase("[NoThrow] void F() { require true || false; }")]
    [TestCase("[NoThrow] int32 F() { return -1; }")]
    [TestCase("[NoThrow] int32 F() { return -2147483648; }")]
    [TestCase("[NoThrow] int64 F() { return -9223372036854775808; }")]
    [TestCase("[NoThrow] bool F(int32 a, int32 b) { return a < b || a == b; }")]
    [TestCase("[NoThrow] int32 F(int32 a, int32 b) { return +(a & b) ^ ~a; }")]
    [TestCase("[NoThrow] int64 F(int32 value) { return int64(value); }")]
    [TestCase("[NoThrow] int64 F(uint32 value) { return int64(value); }")]
    [TestCase("[NoThrow] uint64 F(uint32 value) { return uint64(value); }")]
    [TestCase("[NoThrow] uint32 F(char16 value) { return uint32(value); }")]
    [TestCase("[NoThrow] int32 F(char16 value) { return int32(value); }")]
    [TestCase("[NoThrow] int64 F(char16 value) { return int64(value); }")]
    [TestCase("[NoThrow] uint64 F(char16 value) { return uint64(value); }")]
    [TestCase("[NoThrow] char16 F(char16 value) { return char16(value); }")]
    [TestCase("[NoThrow] bool F(bool success, out int32 value) { if (!success) { return false; } value = 42; return true; }")]
    [TestCase("[NoThrow] int32 F(bool choose) { if (choose) { return G(false); } return 42; } [NoThrow] int32 G(bool choose) { return F(choose); }")]
    public void InfallibleOperationsSatisfyDeclaredContracts(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        {var __nunitItems0 = program.Functions;        Assert.Multiple(() => { foreach (var __nunitItem2 in __nunitItems0) { Assert.That(__nunitItem2.IsNoThrow, Is.True); } });}
        SemanticVerifier.VerifyLibrary(program);
    }
    [TestCase("int32","1 + 2",PrimitiveType.Int32,3L)]
    [TestCase("int32","(1 + 2) * 14",PrimitiveType.Int32,42L)]
    [TestCase("int32","-(-42)",PrimitiveType.Int32,42L)]
    [TestCase("int32","~(1 & 2)",PrimitiveType.Int32,-1L)]
    [TestCase("int32","int32.MinValue % -1",PrimitiveType.Int32,0L)]
    [TestCase("int64","int64.MinValue % -1",PrimitiveType.Int64,0L)]
    [TestCase("uint32","uint32.MaxValue / 4294967295",PrimitiveType.UInt32,1L)]
    [TestCase("uint32","uint32.MaxValue & uint32.MaxValue",PrimitiveType.UInt32,-1L)]
    [TestCase("uint64","uint64.MaxValue / uint64.MaxValue",PrimitiveType.UInt64,1L)]
    [TestCase("uint64","uint64.MaxValue - 1",PrimitiveType.UInt64,-2L)]
    [TestCase("uint64","uint64(uint32.MaxValue) + 1",PrimitiveType.UInt64,4294967296L)]
    [TestCase("char16","char16(int32(65535))",PrimitiveType.Char16,65535L)]
    [TestCase("int32","int32(uint32(42))",PrimitiveType.Int32,42L)]
    [TestCase("int32","+int32(42)",PrimitiveType.Int32,42L)]
    [TestCase("uint32","+uint32.MaxValue",PrimitiveType.UInt32,-1L)]
    [TestCase("uint64","+uint64.MaxValue",PrimitiveType.UInt64,-1L)]
    [TestCase("int64","-int64(42)",PrimitiveType.Int64,-42L)]
    [TestCase("int64","~int64(0)",PrimitiveType.Int64,-1L)]
    [TestCase("uint32","~uint32(0)",PrimitiveType.UInt32,-1L)]
    [TestCase("uint32","~uint32.MaxValue",PrimitiveType.UInt32,0L)]
    [TestCase("uint64","~uint64(0)",PrimitiveType.UInt64,-1L)]
    [TestCase("uint64","~uint64.MaxValue",PrimitiveType.UInt64,0L)]
    [TestCase("int32","int32.MinValue - 0",PrimitiveType.Int32,int.MinValue)]
    [TestCase("int64","int64.MaxValue * 1",PrimitiveType.Int64,long.MaxValue)]
    [TestCase("uint32","uint32.MaxValue % 4294967295",PrimitiveType.UInt32,0L)]
    [TestCase("uint64","uint64.MaxValue % uint64(100)",PrimitiveType.UInt64,15L)]
    [TestCase("int32","1 | 2",PrimitiveType.Int32,3L)]
    [TestCase("int32","1 ^ 3",PrimitiveType.Int32,2L)]
    [TestCase("uint32","uint32.MaxValue ^ uint32(1)",PrimitiveType.UInt32,-2L)]
    [TestCase("uint64","uint64.MaxValue | uint64(0)",PrimitiveType.UInt64,-1L)]
    [TestCase("uint64","uint64.MaxValue ^ uint64(1)",PrimitiveType.UInt64,-2L)]
    [TestCase("uint32","uint32(int32.MaxValue) + 1",PrimitiveType.UInt32,int.MinValue)]
    [TestCase("int64","int64(uint32.MaxValue)",PrimitiveType.Int64,4294967295L)]
    [TestCase("uint64","uint64(int64.MaxValue)",PrimitiveType.UInt64,long.MaxValue)]
    [TestCase("char16","char16(uint64(65535))",PrimitiveType.Char16,65535L)]
    public void SafeScalarExpressionsPersistAsTypedConstants(
        string resultType, string expression, PrimitiveType type, long value)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"[NoThrow] public {resultType} Fold() {{ return {expression}; }}"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))().Functions);
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual4 = global::System.Linq.Enumerable.Single(function.Body.Statements); Assert.That(__nunitActual4, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual4!; }))();
        var constant = ((global::System.Func<BoundConstant>)(() => { var __nunitActual5 = result.Value; Assert.That(__nunitActual5, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual5!; }))();
        Assert.That(constant.Type, Is.EqualTo(type));
        Assert.That(constant.Value, Is.EqualTo(value));
        Assert.That(constant.Span.Length, Is.EqualTo(expression.Length));
    }
    [TestCase("int32","int32.MaxValue + 1")]
    [TestCase("int32","int32.MinValue - 1")]
    [TestCase("int64","int64.MaxValue * 2")]
    [TestCase("uint32","uint32.MaxValue + 1")]
    [TestCase("uint64","uint64.MaxValue + 1")]
    [TestCase("int32","1 / 0")]
    [TestCase("int32","int32.MinValue / -1")]
    [TestCase("int64","int64.MinValue / -1")]
    [TestCase("char16","char16(int32(-1))")]
    [TestCase("int32","int32(uint32.MaxValue)")]
    [TestCase("int32","(int32.MaxValue + 1) - 1")]
    [TestCase("uint32","(uint32.MaxValue + 1) - 1")]
    [TestCase("int32","0 * Read()")]
    [TestCase("int32","1 + value")]
    [TestCase("uint32","uint32(int32(-1))")]
    [TestCase("int64","int64(uint64.MaxValue)")]
    [TestCase("char16","char16(uint64(65536))")]
    public void UnsafeOrUnknownScalarExpressionsKeepTheirEffects(string resultType, string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var parameter = expression.Contains("value", StringComparison.Ordinal) ? "int32 value" : "";
        var callee = expression.Contains("Read()", StringComparison.Ordinal)
            ? "[NoThrow] int32 Read() { return 42; }" : "";
        var source = $"{resultType} Fold({parameter}) {{ return {expression}; }} {callee}";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("[NoThrow] " + source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3017"),Is.True);
        Assert.That(compilation.Program, Is.Null);
        var baseline = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(baseline.HasErrors, Is.False, string.Join(Environment.NewLine, baseline.Diagnostics));
        var function = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = baseline.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))().Functions, static function => function.Name == "Fold");
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual7 = global::System.Linq.Enumerable.Single(function.Body.Statements); Assert.That(__nunitActual7, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual7!; }))();
        Assert.That(result.Value, Is.Not.TypeOf<BoundConstant>());
    }
    [TestCase("int32 Main() { var value = 1 + 2; return value; }","TRM3014")]
    [TestCase("int32 Main() { return int32(1) + uint32(2); }","TRM3007")]
    [TestCase("char16 Main() { return char16(1) + char16(2); }","TRM3007")]
    public void FoldingDoesNotRepairInvalidOperandContexts(string source, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
    }

    [Test]
    public void FoldingCalleeBodyDoesNotStrengthenItsDeclaredEffect()
    {
        const string Callee = "int32 Callee() { return 1 + 2; }";
        var baseline = Compilation.CreateLibrary(SyntaxTree.Parse(Callee));
        Assert.That(baseline.HasErrors, Is.False, string.Join(Environment.NewLine, baseline.Diagnostics));
        var callee = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = baseline.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))().Functions);
        var statement = ((global::System.Func<BoundReturn>)(() => { var __nunitActual9 = global::System.Linq.Enumerable.Single(callee.Body.Statements); Assert.That(__nunitActual9, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual9!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual10 = statement.Value; Assert.That(__nunitActual10, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual10!; }))().Value, Is.EqualTo(3L));
        Assert.That(callee.IsNoThrow, Is.False);

        var caller = Compilation.CreateLibrary(SyntaxTree.Parse(
            Callee + " [NoThrow] int32 Caller() { return Callee(); }"));
        Assert.That(caller.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(caller.Diagnostics,static diagnostic => diagnostic.Code == "TRM3017"),Is.True);
    }

    [Test]
    public void ConstantTrueComparisonSatisfiesNoThrowRequire()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[NoThrow] void Check() { require int32(1) == int32(1); }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.VerifyLibrary(((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))());
    }
    [TestCase("(true && true) == true")]
    [TestCase("(!false) == true")]
    public void PureBooleanFactsSatisfyNoThrowRequire(string expression)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"[NoThrow] void Check() {{ require {expression}; }}"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.VerifyLibrary(((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = compilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))());
    }
    [TestCase("!false",true)]
    [TestCase("!true",false)]
    [TestCase("!(!true)",true)]
    [TestCase("true && true",true)]
    [TestCase("true && false",false)]
    [TestCase("false && true",false)]
    [TestCase("false && false",false)]
    [TestCase("true || false",true)]
    [TestCase("false || true",true)]
    [TestCase("false || false",false)]
    [TestCase("true & true",true)]
    [TestCase("true & false",false)]
    [TestCase("false & false",false)]
    [TestCase("true | false",true)]
    [TestCase("false | false",false)]
    [TestCase("true ^ false",true)]
    [TestCase("true ^ true",false)]
    [TestCase("false ^ false",false)]
    [TestCase("((true && true) == (!false)) ^ false",true)]
    public void PureBooleanOperatorsBecomeTypedConstants(string expression, bool expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"[NoThrow] bool Fold() {{ return {expression}; }}"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = compilation.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))().Functions);
        var returned = ((global::System.Func<BoundReturn>)(() => { var __nunitActual14 = global::System.Linq.Enumerable.Single(function.Body.Statements); Assert.That(__nunitActual14, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual14!; }))();
        var constant = ((global::System.Func<BoundConstant>)(() => { var __nunitActual15 = returned.Value; Assert.That(__nunitActual15, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual15!; }))();
        Assert.That(constant.Type, Is.EqualTo(PrimitiveType.Bool));
        Assert.That(constant.Value, Is.EqualTo(expected ? 1L : 0L));
        Assert.That(constant.Span.Length, Is.EqualTo(expression.Length));
    }
    [TestCase("(!true) == false",true)]
    [TestCase("(true && false) == true",false)]
    [TestCase("(false | true) != true",false)]
    public void NestedBooleanRequirementsKeepLiteralFailurePolicy(string expression, bool expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"[NoThrow] void Check() {{ require {expression}; }}"));
        Assert.That(compilation.HasErrors, Is.EqualTo(!expected));
        if (expected)
        {
            var function = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual16 = compilation.Program; Assert.That(__nunitActual16, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual16!; }))().Functions);
            var requirement = ((global::System.Func<BoundRequire>)(() => { var __nunitActual17 = function.Body.Statements[0]; Assert.That(__nunitActual17, Is.TypeOf<BoundRequire>()); return (BoundRequire)__nunitActual17!; }))();
            Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual18 = requirement.Condition; Assert.That(__nunitActual18, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual18!; }))().Value, Is.EqualTo(1L));
        }
        else
        {
            Assert.That(compilation.Program, Is.Null);
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3017"),Is.True);
        }
    }
    [TestCase("bool Fold(bool value) { return false && value; }",typeof(BoundRead))]
    [TestCase("bool Fold() { return true || Fail(); } bool Fail() { require false; return false; }",typeof(BoundCall))]
    [TestCase("bool Fold() { return false & Fail(); } bool Fail() { require false; return false; }",typeof(BoundCall))]
    [TestCase("bool Fold() { return false && int32.MaxValue + 1 > int32(0); }",typeof(BoundBinary))]
    public void UnknownAndFailingBooleanOperandsRemainInIr(string source, Type rightType)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual19 = compilation.Program; Assert.That(__nunitActual19, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual19!; }))().Functions, static function => function.Name == "Fold");
        var returned = ((global::System.Func<BoundReturn>)(() => { var __nunitActual20 = global::System.Linq.Enumerable.Single(function.Body.Statements); Assert.That(__nunitActual20, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual20!; }))();
        var binary = ((global::System.Func<BoundBinary>)(() => { var __nunitActual21 = returned.Value; Assert.That(__nunitActual21, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual21!; }))();
        Assert.That(binary.Right.GetType(), Is.EqualTo(rightType));
        if (binary.Right is BoundBinary comparison)
        {
            _ = ((global::System.Func<BoundBinary>)(() => { var __nunitActual22 = comparison.Left; Assert.That(__nunitActual22, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual22!; }))();
        }
    }
    [TestCase("[NoThrow] bool Fold() { return true || Fail(); } bool Fail() { return true; }","TRM3017")]
    [TestCase("[NoThrow] bool Fold() { return false && int32.MaxValue + 1 > int32(0); }","TRM3017")]
    [TestCase("bool Fold() { return false && Missing(); }","TRM3011")]
    [TestCase("bool Fold() { return true || int32(1) == uint32(1); }","TRM3007")]
    public void ConstantBooleanSidesDoNotHideInvalidOrEffectfulOperands(string source, string code)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
    }
    [TestCase("int32.MinValue == int32.MinValue",true)]
    [TestCase("int32.MinValue != int32.MinValue",false)]
    [TestCase("int32.MinValue < int32.MaxValue",true)]
    [TestCase("int32.MaxValue < int32.MinValue",false)]
    [TestCase("int32.MinValue <= int32.MinValue",true)]
    [TestCase("int32.MaxValue <= int32.MinValue",false)]
    [TestCase("int32.MaxValue > int32.MinValue",true)]
    [TestCase("int32.MinValue > int32.MaxValue",false)]
    [TestCase("int32.MaxValue >= int32.MaxValue",true)]
    [TestCase("int32.MinValue >= int32.MaxValue",false)]
    [TestCase("int64.MinValue < int64.MaxValue",true)]
    [TestCase("int64.MinValue >= int64.MaxValue",false)]
    [TestCase("uint32.MaxValue > uint32(0)",true)]
    [TestCase("uint32(0) > uint32.MaxValue",false)]
    [TestCase("uint32.MaxValue <= uint32.MaxValue",true)]
    [TestCase("uint32.MaxValue < uint32(0)",false)]
    [TestCase("uint64.MaxValue > uint64(int64.MaxValue)",true)]
    [TestCase("uint64.MaxValue < uint64(int64.MaxValue)",false)]
    [TestCase("uint64.MaxValue != uint64(0)",true)]
    [TestCase("uint64.MaxValue == uint64(0)",false)]
    [TestCase("char16(65535) >= char16(65535)",true)]
    [TestCase("char16(0) > char16(65535)",false)]
    [TestCase("char16(1) != char16(0)",true)]
    [TestCase("char16(1) == char16(0)",false)]
    [TestCase("true == true",true)]
    [TestCase("true != true",false)]
    [TestCase("true != false",true)]
    [TestCase("false == true",false)]
    [TestCase("(1 + 2) == int32(3)",true)]
    public void MatchingScalarConstantComparisonsBecomeBooleanConstants(string expression, bool expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"[NoThrow] bool Compare() {{ return {expression}; }}"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual23 = compilation.Program; Assert.That(__nunitActual23, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual23!; }))().Functions);
        var returned = ((global::System.Func<BoundReturn>)(() => { var __nunitActual24 = global::System.Linq.Enumerable.Single(function.Body.Statements); Assert.That(__nunitActual24, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual24!; }))();
        var constant = ((global::System.Func<BoundConstant>)(() => { var __nunitActual25 = returned.Value; Assert.That(__nunitActual25, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual25!; }))();
        Assert.That(constant.Type, Is.EqualTo(PrimitiveType.Bool));
        Assert.That(constant.Value, Is.EqualTo(expected ? 1L : 0L));
        Assert.That(constant.Span.Length, Is.EqualTo(expression.Length));
    }
    [TestCase("int32(1) == int32(1)",true)]
    [TestCase("uint64.MaxValue >= uint64(0)",true)]
    [TestCase("char16(1) < char16(0)",false)]
    [TestCase("true != true",false)]
    public void NoThrowRequireKeepsBooleanLiteralFailurePolicy(string comparison, bool expected)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"[NoThrow] void Check() {{ require {comparison}; }}"));
        Assert.That(compilation.HasErrors, Is.EqualTo(!expected));
        if (expected)
        {
            var function = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual26 = compilation.Program; Assert.That(__nunitActual26, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual26!; }))().Functions);
            var requirement = ((global::System.Func<BoundRequire>)(() => { var __nunitActual27 = function.Body.Statements[0]; Assert.That(__nunitActual27, Is.TypeOf<BoundRequire>()); return (BoundRequire)__nunitActual27!; }))();
            Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual28 = requirement.Condition; Assert.That(__nunitActual28, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual28!; }))().Value, Is.EqualTo(1L));
        }
        else
        {
            Assert.That(compilation.Program, Is.Null);
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3017"),Is.True);
        }
    }
    [TestCase("bool Compare(int32 value) { return value == 42; }")]
    [TestCase("[NoThrow] bool Compare() { return Read() == 42; } [NoThrow] int32 Read() { return 42; }")]
    [TestCase("bool Compare() { return int32.MaxValue + 1 == int32(0); }")]
    public void UnknownOrFailingComparisonsRetainExecutableOperands(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual29 = compilation.Program; Assert.That(__nunitActual29, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual29!; }))();
        var function = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Compare");
        var returned = ((global::System.Func<BoundReturn>)(() => { var __nunitActual30 = global::System.Linq.Enumerable.Single(function.Body.Statements); Assert.That(__nunitActual30, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual30!; }))();
        var comparison = ((global::System.Func<BoundBinary>)(() => { var __nunitActual31 = returned.Value; Assert.That(__nunitActual31, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual31!; }))();
        Assert.That(comparison.Left, Is.Not.TypeOf<BoundConstant>());
    }
    [TestCase("bool Compare() { return int32(1) == uint32(1); }","TRM3007")]
    [TestCase("bool Compare() { return char16(1) < int32(2); }","TRM3007")]
    [TestCase("bool Compare() { return true < false; }","TRM3007")]
    [TestCase("bool Compare() { var value = 1 == 2; return value; }","TRM3007")]
    public void FoldingDoesNotRepairInvalidComparisons(string source, string code)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
    }
    [TestCase("[NoThrow] int32 Check() { if (int32(1) == int32(1)) { return 42; } return 1 / 0; }","TRM3017")]
    [TestCase("int32 Check() { if (int32(1) != int32(1)) { return Missing(); } return 42; }","TRM3011")]
    public void UnreachableComparisonBranchesRetainNormalChecks(string source, string code)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
    }
    [TestCase("false")]
    [TestCase("int32(1) != int32(1)")]
    public void FoldedFalseConditionDoesNotInitializeUnreachedOutputs(string condition)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"bool Try(out int32 value) {{ return false; }} "
            + $"int32 Main() {{ if ({condition} && Try(out var output)) {{ }} return output; }}"));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
    }
    [TestCase("int32 F(int32 value) { return value + 1; }")]
    [TestCase("int32 F(int32 value) { return value - 1; }")]
    [TestCase("int64 F(int64 value) { return value * 2; }")]
    [TestCase("uint64 F(uint64 value) { return value / 2; }")]
    [TestCase("int32 F(int32 value) { return value % 2; }")]
    [TestCase("int32 F(int32 value) { return -value; }")]
    [TestCase("int32 F() { return -int32.MinValue; }")]
    [TestCase("int64 F() { return -int64.MinValue; }")]
    [TestCase("char16 F(int32 value) { return char16(value); }")]
    [TestCase("uint32 F(int32 value) { return uint32(value); }")]
    [TestCase("int32 F(uint32 value) { return int32(value); }")]
    [TestCase("uint64 F(int64 value) { return uint64(value); }")]
    [TestCase("int64 F(uint64 value) { return int64(value); }")]
    [TestCase("int32 F(int64 value) { return int32(value); }")]
    [TestCase("void F(bool success) { require success; }")]
    [TestCase("int32 F() { return G(); } int32 G() { return 42; }")]
    public void SourceAndPublicIrCannotAssertUnprovedNoThrowContracts(string source)
    {
        var syntax = SyntaxTree.Parse("[NoThrow] " + source + " int32 Main() { return 0; }");
        Assert.That(syntax.HasErrors, Is.False);
        var invalid = Compilation.Create(syntax);
        Assert.That(invalid.HasErrors, Is.True);
        Assert.That(invalid.Program, Is.Null);
        var diagnostic = global::System.Linq.Enumerable.Single(invalid.Diagnostics);
        Assert.That(diagnostic.Code, Is.EqualTo("TRM3017"));
        Assert.That(diagnostic.Span.Length > 0, Is.True);

        var baseline = Compilation.Create(SyntaxTree.Parse(source + " int32 Main() { return 0; }"));
        Assert.That(baseline.HasErrors, Is.False, string.Join(Environment.NewLine, baseline.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual32 = baseline.Program; Assert.That(__nunitActual32, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual32!; }))();
        var index = program.Functions.IndexOf(program.Functions.Single(static function => function.Name == "F"));
        var forged = program with { Functions = program.Functions.SetItem(index, program.Functions[index] with { IsNoThrow = true }) };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast))!;
    }
    [TestCase("return Host.Data[1];")]
    [TestCase("Host.Data[1] = 1; return 0;")]
    [TestCase("int32 index = 0; return Host.Data[index];")]
    public void CheckedBufferAccessIsNotAnImplicitNoThrowCapability(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("[NoThrow] int32 Main() { " + body + " }"),
            [new HostBuffer("Data", PrimitiveType.Int32, [42], true)]);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3017"),Is.True);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
    }
}
