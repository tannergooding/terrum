// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed partial class JitTests
{
    private static readonly (string Name, PrimitiveType Type, int Width, int Precision,
        int CoefficientBits, int ExponentBits, int Bias, int MinimumQuantum,
        int MaximumQuantum, int MaximumAdjusted)[] s_bidDecimalFormats = [
        ("decimal32", PrimitiveType.Decimal32, 32, 7, 23, 8, 101, -101, 90, 96),
        ("decimal64", PrimitiveType.Decimal64, 64, 16, 53, 10, 398, -398, 369, 384),
        ("decimal128", PrimitiveType.Decimal128, 128, 34, 113, 14, 6176, -6176, 6111, 6144),
    ];

    [Test]
    public async Task BidDecimalOperatorsExecuteAcrossAotAndJitUnderBothRuntimePoliciesAsync()
    {
        var operations = new (TokenKind Operator, string Name, string Symbol, bool Comparison)[] {
            (TokenKind.Plus, "Add", "+", false),
            (TokenKind.Minus, "Subtract", "-", false),
            (TokenKind.Star, "Multiply", "*", false),
            (TokenKind.Slash, "Divide", "/", false),
            (TokenKind.Percent, "Remainder", "%", false),
            (TokenKind.EqualsEquals, "Equal", "==", true),
            (TokenKind.BangEquals, "NotEqual", "!=", true),
            (TokenKind.Less, "Less", "<", true),
            (TokenKind.LessEquals, "LessOrEqual", "<=", true),
            (TokenKind.Greater, "Greater", ">", true),
            (TokenKind.GreaterEquals, "GreaterOrEqual", ">=", true),
        };
        var random = new Random(0xB1D128);
        var source = new StringBuilder();
        foreach (var format in s_bidDecimalFormats)
        {
            foreach (var operation in operations)
            {
                _ = source.Append(operation.Comparison ? "bool " : format.Name + " ")
                    .Append(format.Name).Append(operation.Name)
                    .Append('(').Append(format.Name).Append(" left, ")
                    .Append(format.Name).AppendLine(" right) {")
                    .Append("    return left ").Append(operation.Symbol).AppendLine(" right;")
                    .AppendLine("}");
            }
            AppendUpdateFunction(source, format.Name);
        }
        _ = source.AppendLine("int32 Main() {");
        var check = 0;
        foreach (var format in s_bidDecimalFormats)
        {
            foreach (var operation in operations)
            {
                for (var index = 0; index < 3; index++)
                {
                    var left = RandomBidDecimal(random, format);
                    var right = RandomBidDecimal(random, format);
                    Assert.That(FloatingLiteralParser.TryFoldBinary(operation.Operator,
                        format.Type, left, right, out var expected), Is.True);
                    var expectedText = operation.Comparison
                        ? expected.Low != 0 ? "true" : "false"
                        : DecimalLiteral(format, expected);
                    check++;
                    _ = source.Append("    if (").Append(format.Name).Append(operation.Name)
                        .Append('(').Append(DecimalLiteral(format, left)).Append(", ")
                        .Append(DecimalLiteral(format, right)).Append(") != ")
                        .Append(expectedText).Append(") { return ").Append(check).AppendLine("; }");
                }
            }
            check++;
            _ = source.Append("    if (!Update").Append(format.Name)
                .Append("()) { return ").Append(check).AppendLine("; }");
            _ = source.Append("    ").Append(format.Name).Append(" infinity")
                .Append(format.Name).Append("Value = ").Append(format.Name)
                .Append("(1) / ").Append(format.Name).AppendLine("(0);");
            _ = source.Append("    ").Append(format.Name).Append(" nan")
                .Append(format.Name).Append("Value = ").Append(format.Name)
                .Append("(0) / ").Append(format.Name).AppendLine("(0);");
            check++;
            _ = source.Append("    if (infinity").Append(format.Name)
                .Append("Value <= ").Append(format.Name).Append("(1) || !(infinity")
                .Append(format.Name).Append("Value > ").Append(format.Name)
                .AppendLine("(1))) { return " + check.ToString(CultureInfo.InvariantCulture) + "; }");
            check++;
            _ = source.Append("    if (nan").Append(format.Name).Append("Value == nan")
                .Append(format.Name).Append("Value || !(nan").Append(format.Name)
                .Append("Value != nan").Append(format.Name).Append("Value) || nan")
                .Append(format.Name).Append("Value < ").Append(format.Name)
                .Append("(1) || nan").Append(format.Name).Append("Value <= ")
                .Append(format.Name).Append("(1) || nan").Append(format.Name)
                .Append("Value > ").Append(format.Name).Append("(1) || nan")
                .Append(format.Name).Append("Value >= ").Append(format.Name)
                .AppendLine("(1)) { return " + check.ToString(CultureInfo.InvariantCulture) + "; }");
            check++;
            _ = source.Append("    ").Append(format.Name).Append(" negativeZero")
                .Append(format.Name).Append(" = -").Append(format.Name)
                .AppendLine("(0.00);");
            _ = source.Append("    if (!(").Append(format.Name).Append("(1) / negativeZero")
                .Append(format.Name).Append(" < ").Append(format.Name)
                .Append("(0))) { return ").Append(check).AppendLine("; }");
        }
        _ = source.AppendLine("    if (decimal32(1e96) * decimal32(10) != decimal32(1) / decimal32(0)")
            .AppendLine("        || decimal32(1e-101) / decimal32(2) != decimal32(0)")
            .AppendLine("        || decimal32(1e-101) / decimal32(1) != decimal32(1e-101)")
            .AppendLine("        || decimal64(1e384) * decimal64(10) != decimal64(1) / decimal64(0)")
            .AppendLine("        || decimal64(1e-398) / decimal64(2) != decimal64(0)")
            .AppendLine("        || decimal64(1e-398) / decimal64(1) != decimal64(1e-398)")
            .AppendLine("        || decimal128(1e6144) * decimal128(10) != decimal128(1) / decimal128(0)")
            .AppendLine("        || decimal128(1e-6176) / decimal128(2) != decimal128(0)")
            .AppendLine("        || decimal128(1e-6176) / decimal128(1) != decimal128(1e-6176)")
            .AppendLine("        || decimal128(1e1000) % decimal128(3) != decimal128(1)) { return 200; }");
        _ = source.AppendLine("    return 42;")
            .AppendLine("}");
        var program = source.ToString();
        await AssertFailFastExecutionAsync(program, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(program, 42).ConfigureAwait(true);
    }

    private static void AppendUpdateFunction(StringBuilder source, string type)
    {
        _ = source.Append("bool Update").Append(type).AppendLine("() {")
            .Append("    mut ").Append(type).Append(" value = ").Append(type).AppendLine("(1.20);")
            .Append("    ").Append(type).Append(" previous = value++;").AppendLine()
            .Append("    ").Append(type).Append(" prefix = ++value;").AppendLine()
            .Append("    value += ").Append(type).AppendLine("(0.50);")
            .Append("    value -= ").Append(type).AppendLine("(0.50);")
            .Append("    value *= ").Append(type).AppendLine("(2);")
            .Append("    value /= ").Append(type).AppendLine("(2);")
            .Append("    value %= ").Append(type).AppendLine("(1.0);")
            .Append("    return previous == ").Append(type).Append("(1.20) && prefix == ")
            .Append(type).Append("(3.20) && value == ").Append(type).AppendLine("(0.20);")
            .AppendLine("}");
    }

    private static ScalarBits RandomBidDecimal(Random random,
        (string Name, PrimitiveType Type, int Width, int Precision, int CoefficientBits,
            int ExponentBits, int Bias, int MinimumQuantum, int MaximumQuantum,
            int MaximumAdjusted) format)
    {
        var bytes = new byte[16];
        random.NextBytes(bytes);
        var coefficient = new BigInteger(bytes, isUnsigned: true, isBigEndian: false)
            % BigInteger.Pow(10, format.Precision);
        if (coefficient.IsZero)
        {
            coefficient = BigInteger.One;
        }
        var exponent = random.Next(-20, 21);
        var negative = random.Next(2) != 0;
        return EncodeBidDecimal(format, coefficient, exponent, negative);
    }

    private static ScalarBits EncodeBidDecimal(
        (string Name, PrimitiveType Type, int Width, int Precision, int CoefficientBits,
            int ExponentBits, int Bias, int MinimumQuantum, int MaximumQuantum,
            int MaximumAdjusted) format, BigInteger coefficient, int exponent, bool negative)
    {
        var raw = negative ? BigInteger.One << (format.Width - 1) : BigInteger.Zero;
        var encodedExponent = new BigInteger(exponent + format.Bias);
        var shortLimit = BigInteger.One << format.CoefficientBits;
        raw |= coefficient < shortLimit
            ? (encodedExponent << format.CoefficientBits) | coefficient
            : (new BigInteger(3) << (format.Width - 3))
                | (encodedExponent << (format.CoefficientBits - 2))
                | (coefficient - shortLimit);
        return new ScalarBits(
            (ulong)(raw & ulong.MaxValue),
            (ulong)(raw >> 64));
    }

    private static string DecimalLiteral(
        (string Name, PrimitiveType Type, int Width, int Precision, int CoefficientBits,
            int ExponentBits, int Bias, int MinimumQuantum, int MaximumQuantum,
            int MaximumAdjusted) format, ScalarBits bits)
    {
        var raw = (new BigInteger(bits.High) << 64) | bits.Low;
        var negative = ((raw >> (format.Width - 1)) & BigInteger.One) != 0;
        var steering = ((raw >> (format.Width - 3)) & 3) == 3;
        var coefficient = steering
            ? (BigInteger.One << format.CoefficientBits)
                | (raw & ((BigInteger.One << (format.CoefficientBits - 2)) - 1))
            : raw & ((BigInteger.One << format.CoefficientBits) - 1);
        var exponentField = steering
            ? (raw >> (format.CoefficientBits - 2))
                & ((BigInteger.One << format.ExponentBits) - 1)
            : (raw >> format.CoefficientBits)
                & ((BigInteger.One << format.ExponentBits) - 1);
        if (coefficient >= BigInteger.Pow(10, format.Precision))
        {
            coefficient = BigInteger.Zero;
        }
        var exponent = (int)exponentField - format.Bias;
        var exponentText = exponent >= 0
            ? $"+{exponent.ToString(CultureInfo.InvariantCulture)}"
            : exponent.ToString(CultureInfo.InvariantCulture);
        return $"{format.Name}({(negative ? "-" : "")}{coefficient.ToString(CultureInfo.InvariantCulture)}e{exponentText})";
    }
}
