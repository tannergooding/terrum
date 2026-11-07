// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class WideFloatingOperatorSemanticsTests
{
    [TestCase("float16", 0x3E00UL, 0UL)]
    [TestCase("bfloat16", 0x3FC0UL, 0UL)]
    [TestCase("float32", 0x3FC0_0000UL, 0UL)]
    [TestCase("float64", 0x3FF8_0000_0000_0000UL, 0UL)]
    [TestCase("float128", 0UL, 0x3FFF_8000_0000_0000UL)]
    public void FoldsBinaryRemainderInTheSelectedFormat(string type, ulong low, ulong high)
    {
        var result = BindReturnConstant(type, $"{type}(5.5) % {type}(2)");

        AssertWideBits(result, low, high);
    }

    [TestCase("float16", "1.5", "0.5", "2", "1", "0.75", "3")]
    [TestCase("bfloat16", "1.5", "0.5", "2", "1", "0.75", "3")]
    [TestCase("float32", "1.5", "0.5", "2", "1", "0.75", "3")]
    [TestCase("float64", "1.5", "0.5", "2", "1", "0.75", "3")]
    [TestCase("float128", "1.5", "0.5", "2", "1", "0.75", "3")]
    [TestCase("decimal32", "1.50", "0.50", "2.00", "1.00", "0.7500", "3")]
    [TestCase("decimal64", "1.50", "0.50", "2.00", "1.00", "0.7500", "3")]
    [TestCase("decimal128", "1.50", "0.50", "2.00", "1.00", "0.7500", "3")]
    public void FoldsSameFormatFloatingArithmetic(string type, string left, string right,
        string sum, string difference, string product, string quotient)
    {
        AssertFoldedEquals(type, $"{type}({left}) + {type}({right})", sum);
        AssertFoldedEquals(type, $"{type}({left}) - {type}({right})", difference);
        AssertFoldedEquals(type, $"{type}({left}) * {type}({right})", product);
        AssertFoldedEquals(type, $"{type}({left}) / {type}({right})", quotient);
    }

    [TestCase("float16", "1.5")]
    [TestCase("bfloat16", "1.5")]
    [TestCase("float32", "1.5")]
    [TestCase("float64", "1.5")]
    [TestCase("float128", "1.5")]
    [TestCase("decimal32", "1.50")]
    [TestCase("decimal64", "1.50")]
    [TestCase("decimal128", "1.50")]
    public void UnarySignOperationsPreserveTheCompleteFormatBits(string type, string positive)
    {
        var negative = BindReturnConstant(type, $"-{type}({positive})");
        var negativeLiteral = BindReturnConstant(type, $"{type}(-{positive})");
        var unaryPlus = BindReturnConstant(type, $"+{type}(-{positive})");

        Assert.That(negative.Bits, Is.EqualTo(negativeLiteral.Bits));
        Assert.That(unaryPlus.Bits, Is.EqualTo(negativeLiteral.Bits));
    }

    [TestCase("decimal32")]
    [TestCase("decimal64")]
    [TestCase("decimal128")]
    public void PreservesThePreferredDecimalRemainderCohort(string type)
    {
        var result = BindReturnConstant(type, $"{type}(5.50) % {type}(2.0)");
        var expected = BindReturnConstant(type, $"{type}(1.50)");

        Assert.That(result.Bits, Is.EqualTo(expected.Bits));
    }

    [TestCase("decimal32")]
    [TestCase("decimal64")]
    [TestCase("decimal128")]
    public void PreservesPreferredDecimalArithmeticCohorts(string type)
    {
        AssertFoldedEquals(type, $"{type}(1.20) + {type}(2.3)", $"{type}(3.50)");
        AssertFoldedEquals(type, $"{type}(1.20) * {type}(2.0)", $"{type}(2.400)");
        AssertFoldedEquals(type, $"{type}(1.20) / {type}(2.0)", $"{type}(0.6)");
    }

    [TestCase("float16", 0x3555UL, 0UL)]
    [TestCase("bfloat16", 0x3EABUL, 0UL)]
    [TestCase("float32", 0x3EAA_AAABUL, 0UL)]
    [TestCase("float64", 0x3FD5_5555_5555_5555UL, 0UL)]
    [TestCase("float128", 0x5555_5555_5555_5555UL, 0x3FFD_5555_5555_5555UL)]
    public void RoundsBinaryDivisionDirectlyToItsTargetFormat(string type, ulong low, ulong high)
    {
        var result = BindReturnConstant(type, $"{type}(1) / {type}(3)");

        AssertWideBits(result, low, high);
    }

    [TestCase("decimal32", "0.5")]
    [TestCase("decimal64", "0.5")]
    [TestCase("decimal128", "0.5")]
    [TestCase("decimal32", "0.3333333")]
    [TestCase("decimal64", "0.3333333333333333")]
    [TestCase("decimal128", "0.3333333333333333333333333333333333")]
    public void FoldsDecimalDivisionWithItsPreferredExponent(string type, string expectedText)
    {
        var divisor = expectedText == "0.5" ? $"{type}(2.00)" : $"{type}(3)";
        var numerator = expectedText == "0.5" ? $"{type}(1.00)" : $"{type}(1)";
        var result = BindReturnConstant(type, $"{numerator} / {divisor}");
        var expected = BindReturnConstant(type, $"{type}({expectedText})");

        Assert.That(result.Bits, Is.EqualTo(expected.Bits));
    }

    [Test]
    public void FoldsBinaryTiesSignedZeroAndNaNComparisons()
    {
        AssertWideBits(BindReturnConstant("float16", "float16(2048) + float16(1)"),
            0x6800, 0);
        AssertWideBits(BindReturnConstant("bfloat16", "bfloat16(256) + bfloat16(1)"),
            0x4380, 0);
        AssertWideBits(BindReturnConstant("float128", "float128(1.5) + float128(0.25)"),
            0, 0x3FFF_C000_0000_0000UL);
        AssertWideBits(BindReturnConstant("float32", "float32(0.0) + float32(-0.0)"),
            0, 0);
        AssertWideBits(BindReturnConstant("float32", "float32(-0.0) + float32(-0.0)"),
            0x8000_0000, 0);

        AssertWideBits(BindReturnConstant("float16", "float16(0) / float16(0)"),
            0x7E00, 0);
        AssertWideBits(BindReturnConstant("float32", "float32(0) / float32(0)"),
            0x7FC0_0000, 0);
        AssertWideBits(BindReturnConstant("float128", "float128(0) / float128(0)"),
            0, 0x7FFF_8000_0000_0000UL);
        AssertWideBits(BindReturnConstant("decimal32", "decimal32(0) / decimal32(0)"),
            0x7C00_0000, 0);
        AssertWideBits(BindReturnConstant("decimal128", "decimal128(0) / decimal128(0)"),
            0, 0x7C00_0000_0000_0000UL);
        AssertWideBits(BindReturnConstant("float16", "float16(65504) + float16(65504)"),
            0x7C00, 0);
        AssertWideBits(BindReturnConstant("decimal32", "decimal32(1) / decimal32(0)"),
            0x7800_0000, 0);
        AssertWideBits(BindReturnConstant("decimal32",
                "decimal32(0) * (decimal32(1) / decimal32(0))"),
            0x7C00_0000, 0);

        Assert.That(BindBooleanReturnConstant("float32(0) / float32(0) == float32(0)").Value,
            Is.EqualTo(0));
        Assert.That(BindBooleanReturnConstant("float32(0) / float32(0) != float32(0)").Value,
            Is.EqualTo(1));
        Assert.That(BindBooleanReturnConstant("decimal32(1.0) == decimal32(1.00)").Value,
            Is.EqualTo(1));
    }

    [Test]
    public void FoldsDecimalPrecisionAndExponentBoundaries()
    {
        Assert.That(BindReturnConstant("decimal32",
            "decimal32(1.2345665) + decimal32(0)").Bits,
            Is.EqualTo(BindReturnConstant("decimal32", "decimal32(1.234566)").Bits));
        AssertWideBits(BindReturnConstant("decimal32", "decimal32(1e96) * decimal32(10)"),
            0x7800_0000, 0);
        AssertWideBits(BindReturnConstant("decimal32", "decimal32(1e-101) / decimal32(2)"),
            0, 0);
        AssertWideBits(BindReturnConstant("decimal32", "decimal32(1e-101) / decimal32(1)"),
            1, 0);
    }

    [TestCase("decimal32", 0x8000_0000UL, 0UL)]
    [TestCase("decimal64", 0x8000_0000_0000_0000UL, 0UL)]
    [TestCase("decimal128", 0UL, 0x8000_0000_0000_0000UL)]
    public void DividesFiniteDecimalByInfinityToMinimumQuantumZero(
        string type, ulong signLow, ulong signHigh)
    {
        for (var dividendNegative = 0; dividendNegative <= 1; dividendNegative++)
        {
            for (var divisorNegative = 0; divisorNegative <= 1; divisorNegative++)
            {
                var dividendSign = dividendNegative == 0 ? "" : "-";
                var divisorSign = divisorNegative == 0 ? "" : "-";
                var expression = $"{type}({dividendSign}1.20) / "
                    + $"({type}(1) / {type}({divisorSign}0))";
                var result = BindReturnConstant(type, expression);
                var negativeResult = dividendNegative != divisorNegative;
                AssertWideBits(result, negativeResult ? signLow : 0,
                    negativeResult ? signHigh : 0);
            }
        }

        var negativeZeroDividend = BindReturnConstant(type,
            $"{type}(-0.00) / ({type}(1) / {type}(0))");
        AssertWideBits(negativeZeroDividend, signLow, signHigh);
    }

    [TestCase("decimal32", PrimitiveType.Decimal32, 0x780A_BCDEUL, 0UL)]
    [TestCase("decimal64", PrimitiveType.Decimal64, 0x7800_0000_0000_1234UL, 0UL)]
    [TestCase("decimal128", PrimitiveType.Decimal128, 0x5678_9ABC_DEF0_1234UL, 0x7800_0000_0000_1234UL)]
    public void IgnoresDecimalInfinityTrailingBitsForDivisionCohort(
        string typeName, PrimitiveType type, ulong infinityLow, ulong infinityHigh)
    {
        var finite = BindReturnConstant(typeName, $"{typeName}(1.20)").Bits;
        var infinity = new ScalarBits(infinityLow, infinityHigh);

        Assert.That(FloatingLiteralParser.TryFoldBinary(TokenKind.Slash,
            type, finite, infinity, out var quotient), Is.True);
        Assert.That(quotient.Low, Is.Zero);
        Assert.That(quotient.High, Is.Zero);
    }

    [Test]
    public void DecodesNoncanonicalDecimalCoefficientsAsSignedZero()
    {
        var noncanonicalPositiveZero = new ScalarBits(0x601F_FFFF, 0);
        var noncanonicalNegativeZero = new ScalarBits(0xE01F_FFFF, 0);
        var positiveZero = BindReturnConstant("decimal32", "decimal32(0)").Bits;
        var negativeZero = BindReturnConstant("decimal32", "decimal32(-0)").Bits;

        Assert.That(FloatingLiteralParser.TryFoldBinary(TokenKind.EqualsEquals,
            PrimitiveType.Decimal32, noncanonicalPositiveZero, positiveZero, out var equal),
            Is.True);
        Assert.That(equal.Low, Is.EqualTo(1));
        Assert.That(FloatingLiteralParser.TryFoldBinary(TokenKind.EqualsEquals,
            PrimitiveType.Decimal32, noncanonicalNegativeZero, negativeZero, out equal),
            Is.True);
        Assert.That(equal.Low, Is.EqualTo(1));

        Assert.That(FloatingLiteralParser.TryFoldBinary(TokenKind.Plus,
            PrimitiveType.Decimal32, noncanonicalPositiveZero, positiveZero, out var sum),
            Is.True);
        Assert.That(sum.Low, Is.Zero);
        Assert.That(sum.High, Is.Zero);
    }

    [Test]
    public void ConstEvalFoldsBinaryAndDecimalFloatingOperators()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [ConstEval] float16 AddHalf(float16 left, float16 right) { return left + right; }
            [ConstEval] decimal32 Remainder(decimal32 left, decimal32 right) { return left % right; }
            [ConstEval] float16 UpdateHalf() {
                mut float16 current = float16(1);
                float16 previous = current++;
                current += float16(0.5);
                return previous + current;
            }
            [ConstEval] decimal32 UpdateDecimal() {
                mut decimal32 current = decimal32(1.00);
                decimal32 previous = current++;
                current += decimal32(0.50);
                return previous + current;
            }
            int32 Main() {
                const float16 half = AddHalf(float16(1.5), float16(2.5));
                const decimal32 decimalValue = Remainder(decimal32(5.50), decimal32(2.0));
                const float16 updatedHalf = UpdateHalf();
                const decimal32 updatedDecimal = UpdateDecimal();
                return 42;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = compilation.Program!.Functions.Single(static function => function.Name == "Main");
        var constants = main.Body.Statements.OfType<BoundLocal>()
            .Select(static local => (BoundConstant)local.Initializer).ToArray();
        Assert.That(constants, Has.Length.EqualTo(4));
        AssertWideBits(constants[0], 0x4400, 0);
        Assert.That(constants[1].Bits,
            Is.EqualTo(BindReturnConstant("decimal32", "decimal32(1.50)").Bits));
        Assert.That(constants[2].Bits, Is.EqualTo(BindReturnConstant("float16", "float16(3.5)").Bits));
        Assert.That(constants[3].Bits,
            Is.EqualTo(BindReturnConstant("decimal32", "decimal32(3.50)").Bits));
    }

    private static BoundConstant BindReturnConstant(string type, string expression)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public {type} Value() {{ return {expression}; }}"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = compilation.Program!.Functions.Single();
        return (BoundConstant)((BoundReturn)function.Body.Statements.Single()).Value!;
    }

    private static BoundConstant BindBooleanReturnConstant(string expression)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public bool Value() {{ return {expression}; }}"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = compilation.Program!.Functions.Single();
        return (BoundConstant)((BoundReturn)function.Body.Statements.Single()).Value!;
    }

    private static void AssertFoldedEquals(string type, string expression, string expected)
    {
        var actualConstant = BindReturnConstant(type, expression);
        var expectedConstant = BindReturnConstant(type, $"{type}({expected})");

        Assert.That(actualConstant.Bits, Is.EqualTo(expectedConstant.Bits));
    }

    private static void AssertWideBits(BoundConstant constant, ulong low, ulong high)
    {
        Assert.That(constant.Bits.Low, Is.EqualTo(low));
        Assert.That(constant.Bits.High, Is.EqualTo(high));
    }
}
