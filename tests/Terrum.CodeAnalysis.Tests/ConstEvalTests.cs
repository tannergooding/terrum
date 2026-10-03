// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ConstEvalTests
{
    [Fact]
    public void NamedConstEvalArgumentsBindToFormalSlots()
    {
        const string source = """
            int32 Main() { const int32 answer = Combine(second: 2, first: 4); return answer; }
            [ConstEval] int32 Combine(int32 first, int32 second) { return first * 10 + second; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "Main");
        Assert.Equal(42, Assert.IsType<BoundConstant>(
            Assert.IsType<BoundLocal>(main.Body.Statements[0]).Initializer).Value);
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "Main");
        Assert.Equal(42, Assert.IsType<BoundConstant>(
            Assert.IsType<BoundLocal>(main.Body.Statements[0]).Initializer).Value);
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

    [Fact]
    public void ForwardConstantCallIsReplacedBeforeVerificationButRuntimeCallsRemain()
    {
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(Source, tree.Root.ToFullString());
        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var main = Assert.Single(program.Functions, function => function.Name == "Main");
        var local = Assert.IsType<BoundLocal>(main.Body.Statements[0]);
        Assert.Equal(10, Assert.IsType<BoundConstant>(local.Initializer).Value);
        Assert.Contains(program.Functions, function => function.Name == "Sum");
        Assert.Contains(main.Body.Statements.OfType<BoundReturn>(),
            statement => statement.Value is BoundBinary { Right: BoundCall });
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "Main");
        Assert.Equal([0L, -1L, 42L], main.Body.Statements.OfType<BoundLocal>()
            .Select(local => Assert.IsType<BoundConstant>(local.Initializer).Value));
    }

    [Theory]
    [InlineData("const int32 value = Divide(1, 0);", "ConstEval checked arithmetic")]
    [InlineData("const int32 value = Add(2147483647, 1);", "ConstEval checked arithmetic")]
    [InlineData("const int32 value = Identity(arg);", "required constant")]
    [InlineData("const int32 value = true ? arg : 42;", "required constant needs scalar values")]
    [InlineData("const int32 value = Identity(Loop(1));", "ConstEval work limit")]
    [InlineData("const int32 value = Recurse(100);", "ConstEval call depth")]
    [InlineData("const int32 value = Divide(int32(0x80000000), -1);", "ConstEval checked arithmetic")]
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
        Assert.Contains(compilation.Diagnostics,
            diagnostic => diagnostic.Code == "TRM3024" && diagnostic.Message.Contains(message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(63, true)]
    [InlineData(64, false)]
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
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions,
                function => function.Name == "Main");
            Assert.Equal(count, Assert.IsType<BoundConstant>(
                Assert.IsType<BoundLocal>(main.Body.Statements[0]).Initializer).Value);
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3024"
                && diagnostic.Message.Contains("call depth limit", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(510, true)]
    [InlineData(511, false)]
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
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3024"
                && diagnostic.Message.Contains("work limit", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("[ConstEval] public int32 Bad(int32 value) { return value; }")]
    [InlineData("[ConstEval] int32 Bad(int32 value) { if (value == 0) { return Other(); } return value; } int32 Other() { return 1; }")]
    [InlineData("[ConstEval] int32 Bad(out int32 value) { value = 1; return 1; }")]
    [InlineData("[ConstEval] void Bad() { return; }")]
    public void UnsupportedContractsAndUnreachableEffectsAreRejected(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declaration + " int32 Main() { return 42; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3024");
    }
}
