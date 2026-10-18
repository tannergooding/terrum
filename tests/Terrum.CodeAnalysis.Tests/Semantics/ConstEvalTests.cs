// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class ConstEvalTests
{
    [Test]
    public void CallerUnsafeConstEvalUsesExpressionScope()
    {
        const string source = """
            [ConstEval] unsafe int32 Value() { return 42; }
            int32 Main() { return const (unsafe(Value())); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))().Functions, function => function.Name == "Main");
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual2 = ((global::System.Func<BoundReturn>)(() => { var __nunitActual1 = main.Body.Statements[0]; Assert.That(__nunitActual1, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual1!; }))().Value; Assert.That(__nunitActual2, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual2!; }))().Value, Is.EqualTo(42));
    }

    [Test]
    public void NamedConstEvalArgumentsBindToFormalSlots()
    {
        const string source = """
            int32 Main() { const int32 answer = Combine(second: 2, first: 4); return answer; }
            [ConstEval] int32 Combine(int32 first, int32 second) { return first * 10 + second; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))().Functions, function => function.Name == "Main");
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual5 = ((global::System.Func<BoundLocal>)(() => { var __nunitActual4 = main.Body.Statements[0]; Assert.That(__nunitActual4, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual4!; }))().Initializer; Assert.That(__nunitActual5, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual5!; }))().Value, Is.EqualTo(42));
    }

    [Test]
    public void PostTestedConstEvalTransfersToNamedOuterLoop()
    {
        const string source = """
            [ConstEval] int32 Value() {
                mut int32 count = 0;
                outer: do {
                    count = count + 1;
                    do {
                        if (count < 3) { continue outer; }
                        break outer;
                    } while (false);
                } while (true);
                return count + 39;
            }
            int32 Main() { const int32 answer = Value(); return answer; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))().Functions, function => function.Name == "Main");
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual8 = ((global::System.Func<BoundLocal>)(() => { var __nunitActual7 = main.Body.Statements[0]; Assert.That(__nunitActual7, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual7!; }))().Initializer; Assert.That(__nunitActual8, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual8!; }))().Value, Is.EqualTo(42));
    }

    private const string Source = """
        int32 Main() {
            const int32 total = Sum(4);
            return total + Sum(3);
        }
        [ConstEval] int32 Sum(int32 count) {
            mut int32 current = count;
            mut int32 total = 0;
            while (current > 0) {
                total = total + current;
                current = current - 1;
            }
            return total;
        }
        """;

    [Test]
    public void ForwardConstantCallIsReplacedBeforeVerificationButRuntimeCallsRemain()
    {
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(Source));
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))();
        var main = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Main");
        var local = ((global::System.Func<BoundLocal>)(() => { var __nunitActual10 = main.Body.Statements[0]; Assert.That(__nunitActual10, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual10!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual11 = local.Initializer; Assert.That(__nunitActual11, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual11!; }))().Value, Is.EqualTo(10));
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.Name == "Sum"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(main.Body.Statements.OfType<BoundReturn>(),statement => statement.Value is BoundBinary { Right: BoundCall }),Is.True);
    }

    [Test]
    public void RequiredScalarArithmeticHandlesUnsignedBitsAndSignedMinimumRemainder()
    {
        const string Program = """
            [ConstEval] int64 Remainder(int64 left, int64 right) { return left % right; }
            [ConstEval] uint64 Largest(uint64 value) { return value; }
            [ConstEval] char16 Letter(char16 value) { return value; }
            int32 Main() {
                const int64 rem = Remainder(int64(0x8000000000000000), int64(-1));
                const uint64 bits = Largest(uint64(0xFFFFFFFFFFFFFFFF));
                const char16 letter = Letter(char16(42));
                if (rem == int64(0) && bits == uint64(0xFFFFFFFFFFFFFFFF)) {
                    return int32(letter);
                }
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Program));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = compilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))().Functions, function => function.Name == "Main");
        Assert.That(main.Body.Statements.OfType<BoundLocal>()
            .Select(local => ((global::System.Func<BoundConstant>)(() => { var __nunitActual13 = local.Initializer; Assert.That(__nunitActual13, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual13!; }))().Value), Is.EqualTo([0L, -1L, 42L]));
    }
    [TestCase("const int32 value = Divide(1, 0);","ConstEval checked arithmetic")]
    [TestCase("const int32 value = Add(2147483647, 1);","ConstEval checked arithmetic")]
    [TestCase("const int32 value = Identity(arg);","required constant")]
    [TestCase("const int32 value = true ? arg : 42;","required constant needs scalar values")]
    [TestCase("const int32 value = Identity(Loop(1));","ConstEval work limit")]
    [TestCase("const int32 value = Recurse(100);","ConstEval call depth")]
    [TestCase("const int32 value = Divide(int32(0x80000000), -1);","ConstEval checked arithmetic")]
    public void RequiredConstantFailuresDiagnoseWithoutRuntimeFallback(string statement, string message)
    {
        var source = """
            [ConstEval] int32 Identity(int32 value) { return value; }
            [ConstEval] int32 Add(int32 left, int32 right) { return left + right; }
            [ConstEval] int32 Divide(int32 left, int32 right) { return left / right; }
            [ConstEval] int32 Loop(int32 input) { while (true) { } return input; }
            [ConstEval] int32 Recurse(int32 input) {
                if (input == 0) { return 0; }
                return Recurse(input - 1);
            }
            int32 Caller(int32 arg) { STATEMENT return 42; }
            int32 Main() { return Caller(1); }
            """.Replace("STATEMENT", statement, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3024" && diagnostic.Message.Contains(message, StringComparison.Ordinal)),Is.True);
    }
    [TestCase(63,true)]
    [TestCase(64,false)]
    public void CallDepthLimitAdmitsExactlySixtyFourNestedCalls(int count, bool succeeds)
    {
        var source = """
            [ConstEval] int32 Recurse(int32 remaining) {
                if (remaining == 0) { return 0; }
                return 1 + Recurse(remaining - 1);
            }
            int32 Main() { const int32 value = Recurse(COUNT); return value; }
            """.Replace("COUNT", count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        if (succeeds)
        {
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var main = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = compilation.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))().Functions, function => function.Name == "Main");
            Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual16 = ((global::System.Func<BoundLocal>)(() => { var __nunitActual15 = main.Body.Statements[0]; Assert.That(__nunitActual15, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual15!; }))().Initializer; Assert.That(__nunitActual16, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual16!; }))().Value, Is.EqualTo(count));
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3024"
                && diagnostic.Message.Contains("call depth limit", StringComparison.Ordinal)),Is.True);
        }
    }
    [TestCase(510,true)]
    [TestCase(511,false)]
    public void WorkLimitBoundsLongRunningScalarLoops(int count, bool succeeds)
    {
        var source = """
            [ConstEval] int32 Count(int32 limit) {
                mut int32 result = 0;
                while (result < limit) { result = result + 1; }
                return result;
            }
            int32 Main() { const int32 value = Count(COUNT); return value; }
            """.Replace("COUNT", count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        if (succeeds)
        {
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3024"
                && diagnostic.Message.Contains("work limit", StringComparison.Ordinal)),Is.True);
        }
    }
    [TestCase("[ConstEval] public int32 Bad(int32 value) { return value; }")]
    [TestCase("[ConstEval] int32 Bad(int32 value) { if (value == 0) { return Other(); } return value; } int32 Other() { return 1; }")]
    [TestCase("[ConstEval] int32 Bad(out int32 value) { value = 1; return 1; }")]
    [TestCase("[ConstEval] void Bad() { return; }")]
    public void UnsupportedContractsAndUnreachableEffectsAreRejected(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declaration + " int32 Main() { return 42; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3024"),Is.True);
    }
}
