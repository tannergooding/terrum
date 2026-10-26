// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class WideIntegerOperatorSemanticsTests
{
    [Test]
    public void FoldsWideIntegerOperatorsWithoutTruncatingBits()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int128 Add() { return int128(2) + int128(3); }
            public uint128 Product() {
                return uint128(340282366920938463463374607431768211455) * uint128(2);
            }
            public uint128 Shift() { return uint128(1) << int8(127); }
            public int128 LogicalShift() { return int128(-1) >>> int8(1); }
            public int128 MinimumRemainder() {
                return int128(-170141183460469231731687303715884105728) % int128(-1);
            }
            public uint128 Complement() { return ~uint128(0); }
            public bool SignedComparison() { return int128(-1) < int128(0); }
            public bool UnsignedComparison() {
                return uint128(340282366920938463463374607431768211455) > uint128(1);
            }
            """), new CompilationOptions { SourceArithmeticProfile = SourceArithmeticProfile.Release });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var functions = compilation.Program!.Functions.ToDictionary(static function => function.Name);

        AssertWideConstant(functions["Add"], 5, 0);
        AssertWideConstant(functions["Product"], ulong.MaxValue - 1, ulong.MaxValue);
        AssertWideConstant(functions["Shift"], 0, 0x8000_0000_0000_0000UL);
        AssertWideConstant(functions["LogicalShift"], ulong.MaxValue, 0x7FFF_FFFF_FFFF_FFFFUL);
        AssertWideConstant(functions["MinimumRemainder"], 0, 0);
        AssertWideConstant(functions["Complement"], ulong.MaxValue, ulong.MaxValue);
        var comparison = GetReturnConstant(functions["SignedComparison"]);
        Assert.That(comparison.Type, Is.EqualTo(PrimitiveType.Bool));
        Assert.That(comparison.Value, Is.EqualTo(1));
        Assert.That(GetReturnConstant(functions["UnsignedComparison"]).Value, Is.EqualTo(1));
    }

    [TestCase(SourceArithmeticProfile.Development, false)]
    [TestCase(SourceArithmeticProfile.Release, true)]
    public void ResolvesWideIntegerOverflowPolicy(
        SourceArithmeticProfile profile, bool expectedWrapping)
    {
        var expected = expectedWrapping
            ? BoundIntegerOverflowBehavior.Wrapping : BoundIntegerOverflowBehavior.Checked;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int128 Add(int128 left, int128 right) { return left + right; }
            public int128 Negate(int128 value) { return -value; }
            public uint128 NegateUnsigned(uint128 value) { return -value; }
            """), new CompilationOptions { SourceArithmeticProfile = profile });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        foreach (var function in compilation.Program!.Functions)
        {
            var result = (BoundReturn)function.Body.Statements.Single();
            Assert.That(result.Value, Is.TypeOf<BoundBinary>()
                .Or.TypeOf<BoundUnary>());
            if (result.Value is BoundBinary binary)
            {
                Assert.That(binary.OverflowBehavior, Is.EqualTo(expected));
            }
            else
            {
                Assert.That(((BoundUnary)result.Value!).OverflowBehavior, Is.EqualTo(expected));
            }
        }
    }

    [Test]
    public void ConstEvalSupportsWideIntegerUpdatesAndCompoundAssignments()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [ConstEval] int128 UpdateAndCompound() {
                mut int128 current = int128(10);
                int128 previous = current++;
                current += int128(2);
                return previous + current;
            }
            int32 Main() {
                const int128 result = UpdateAndCompound();
                return 42;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = compilation.Program!.Functions.Single(static function => function.Name == "Main");
        var result = (BoundConstant)main.Body.Statements.OfType<BoundLocal>().Single().Initializer;
        AssertWideBits(result, 23, 0);
    }

    [Test]
    public void ConstEvalPreservesWideArithmeticAndRemainderBoundaries()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [ConstEval] int128 Add(int128 left, int128 right) { return left + right; }
            [ConstEval] int128 Remainder(int128 left, int128 right) { return left % right; }
            int32 Main() {
                const int128 sum = Add(
                    int128(170141183460469231731687303715884105727), int128(1));
                const int128 remainder = Remainder(
                    int128(-170141183460469231731687303715884105728), int128(-1));
                return 42;
            }
            """), new CompilationOptions { SourceArithmeticProfile = SourceArithmeticProfile.Release });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = compilation.Program!.Functions.Single(static function => function.Name == "Main");
        var constants = main.Body.Statements.OfType<BoundLocal>()
            .Select(static local => (BoundConstant)local.Initializer).ToArray();
        Assert.That(constants, Has.Length.EqualTo(2));
        AssertWideBits(constants[0], 0, 0x8000_0000_0000_0000UL);
        AssertWideBits(constants[1], 0, 0);
    }

    [TestCase("""
        [ConstEval] int128 Add(int128 left, int128 right) { return left + right; }
        int32 Main() {
            const int128 value = Add(
                int128(170141183460469231731687303715884105727), int128(1));
            return 42;
        }
        """)]
    [TestCase("""
        [ConstEval] int128 Divide(int128 left, int128 right) { return left / right; }
        int32 Main() {
            const int128 value = Divide(
                int128(-170141183460469231731687303715884105728), int128(-1));
            return 42;
        }
        """)]
    [TestCase("""
        [ConstEval] uint128 Shift(uint128 value, int16 count) { return value << count; }
        int32 Main() {
            const uint128 value = Shift(uint128(1), int16(128));
            return 42;
        }
        """)]
    public void RequiredConstEvalReportsWideArithmeticFailures(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3024" && diagnostic.Message.Contains("ConstEval", StringComparison.Ordinal)),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsMixedWideIntegerOperands()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int128 Add(int128 left, uint128 right) { return left + right; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3007"),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    private static BoundConstant GetReturnConstant(BoundFunction function)
    {
        var result = function.Body.Statements.OfType<BoundReturn>().Single();
        return (BoundConstant)result.Value!;
    }

    private static void AssertWideConstant(BoundFunction function, ulong low, ulong high)
    {
        var constant = GetReturnConstant(function);
        AssertWideBits(constant, low, high);
    }

    private static void AssertWideBits(BoundConstant constant, ulong low, ulong high)
    {
        Assert.That(constant.Bits.Low, Is.EqualTo(low));
        Assert.That(constant.Bits.High, Is.EqualTo(high));
    }
}
