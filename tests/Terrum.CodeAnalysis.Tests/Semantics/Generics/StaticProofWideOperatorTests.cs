// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics.Generics;

public sealed class StaticProofWideOperatorTests
{
    [Test]
    public void StaticProofPreservesExactWideIntegerBoundsAndPayloads()
    {
        const string source = """
            int32 VerifySigned(int128 input) {
                mut int128 value = input;
                if (value == int128(-170141183460469231731687303715884105728)) {
                    static require value < int128(-170141183460469231731687303715884105727);
                    static require value % int128(-1) == int128(0);
                }
                if (value == int128(170141183460469231731687303715884105727)) {
                    static require value > int128(170141183460469231731687303715884105726);
                }
                mut bool atMaximum = value == int128(170141183460469231731687303715884105727);
                if (atMaximum) {
                    static require value == int128(170141183460469231731687303715884105727);
                }
                if (value >= int128(-170141183460469231731687303715884105727)
                    && value <= int128(-1)) {
                    int128 quotient = value / int128(-1);
                    static require quotient > int128(0);
                }
                if (value == int128(-2)) {
                    static require value + int128(1) == int128(-1);
                    static require value - int128(1) == int128(-3);
                    static require value * int128(2) == int128(-4);
                    static require value / int128(-1) == int128(2);
                    static require value % int128(-1) == int128(0);
                    static require -value == int128(2);
                    static require (value << int8(1)) == int128(-4);
                    static require (value >> int8(1)) == int128(-1);
                    static require (value >>> int8(127)) == int128(1);
                    static require (value & int128(-1)) == value;
                    static require (value | int128(0)) == value;
                    static require (value ^ int128(-1)) == int128(1);
                    static require ~value == int128(1);
                }
                if (value < int128(170141183460469231731687303715884105700)) {
                } else if (value > int128(170141183460469231731687303715884105700)) {
                } else {
                    return 0;
                }
                static require value != int128(170141183460469231731687303715884105700);
                return 0;
            }
            int32 VerifyUnsigned(uint128 input) {
                mut uint128 value = input;
                if (value == uint128(340282366920938463463374607431768211455)) {
                    static require value > uint128(340282366920938463463374607431768211454);
                    static require value >> int8(127) == uint128(1);
                    static require ~value == uint128(0);
                }
                if (value < uint128(340282366920938463463374607431768211455)) {
                    uint128 incremented = value + uint128(1);
                    static require incremented > uint128(0);
                }
                if (value == uint128(1)) {
                    static require (value << int8(127))
                        == uint128(170141183460469231731687303715884105728);
                }
                return 0;
            }
            int32 Main() { return VerifySigned(int128(0)) + VerifyUnsigned(uint128(0)); }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var impossible = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int128 value) {
                if (value == int128(170141183460469231731687303715884105727)) {
                    static require value < int128(170141183460469231731687303715884105727);
                }
                return 0;
            }
            int32 Main() { return Verify(int128(0)); }
            """));
        Assert.That(impossible.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3025"),
            Is.True, string.Join(Environment.NewLine, impossible.Diagnostics));

        var incompleteBound = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(uint128 value) {
                if (value >= uint128(340282366920938463463374607431768211454)) {
                    static require value == uint128(340282366920938463463374607431768211455);
                }
                return 0;
            }
            int32 Main() { return Verify(uint128(0)); }
            """));
        Assert.That(incompleteBound.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3026"),
            Is.True, string.Join(Environment.NewLine, incompleteBound.Diagnostics));
    }

    [TestCase("float16")]
    [TestCase("bfloat16")]
    [TestCase("float32")]
    [TestCase("float64")]
    [TestCase("float128")]
    [TestCase("decimal32")]
    [TestCase("decimal64")]
    [TestCase("decimal128")]
    public void StaticProofCarriesFloatingFactsAcrossCopiesJoinsAndConstEval(string type)
    {
        var source = $$"""
            int32 Verify({{type}} input, bool choose) {
                mut {{type}} value = input;
                mut bool saved = value == {{type}}(1);
                if (saved) {
                    static require value == {{type}}(1);
                } else {
                    static require value != {{type}}(1);
                }
                if (choose) {
                    value = {{type}}(-0);
                } else {
                    value = {{type}}(0);
                }
                static require value == {{type}}(-0);
                static require value == {{type}}(0);
                mut {{type}} sum = {{type}}(1) + {{type}}(2);
                static require sum == {{type}}(3);
                mut {{type}} infinity = {{type}}(1) / {{type}}(0);
                static require infinity > {{type}}(0);
                mut {{type}} nanValue = {{type}}(0) / {{type}}(0);
                static require nanValue != {{type}}(0);
                static require !(nanValue == {{type}}(0));
                static require !(nanValue < {{type}}(0));
                static require !(nanValue >= {{type}}(0));
                return 0;
            }
            [ConstEval] {{type}} ConstantRemainder({{type}} left, {{type}} right) {
                return left % right;
            }
            [ConstEval] {{type}} ConstantUpdate() {
                mut {{type}} current = {{type}}(1);
                {{type}} previous = current++;
                current += {{type}}(1);
                return previous + current;
            }
            int32 Main() {
                const {{type}} result = ConstantRemainder({{type}}(5), {{type}}(2));
                const {{type}} updated = ConstantUpdate();
                return Verify({{type}}(1), true);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var main = compilation.Program!.Functions.Single(static function => function.Name == "Main");
        var constants = main.Body.Statements.OfType<BoundLocal>()
            .Select(static local => (BoundConstant)local.Initializer).ToArray();
        Assert.That(constants, Has.Length.EqualTo(2));
        Assert.That(constants[0].Bits, Is.EqualTo(ParseFloating(type, "1")));
        Assert.That(constants[1].Bits, Is.EqualTo(ParseFloating(type, "4")));
        Assert.That(ParseFloating(type, "-0"), Is.Not.EqualTo(ParseFloating(type, "0")));
    }

    [TestCase("float16")]
    [TestCase("bfloat16")]
    [TestCase("float32")]
    [TestCase("float64")]
    [TestCase("float128")]
    [TestCase("decimal32")]
    [TestCase("decimal64")]
    [TestCase("decimal128")]
    public void StaticProofKeepsNaNOrderingAndSignedZeroFailuresDistinct(string type)
    {
        var source = $$"""
            int32 Verify({{type}} input) {
                if (input > {{type}}(0)) {
                } else {
                    static require input >= {{type}}(0);
                }
                return 0;
            }
            int32 Main() { return Verify({{type}}(0)); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3026"),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));

        var knownNaN = Compilation.Create(SyntaxTree.Parse($$"""
            int32 Verify({{type}} input) {
                mut {{type}} nanValue = {{type}}(0) / {{type}}(0);
                static require nanValue == nanValue;
                return 0;
            }
            int32 Main() { return Verify({{type}}(0)); }
            """));
        Assert.That(knownNaN.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3025"),
            Is.True, string.Join(Environment.NewLine, knownNaN.Diagnostics));
    }

    [TestCase("decimal32")]
    [TestCase("decimal64")]
    [TestCase("decimal128")]
    public void StaticProofJoinsNumericEqualityWithoutMergingDecimalCohorts(string type)
    {
        var source = $$"""
            int32 Verify(bool choose) {
                mut {{type}} value = {{type}}(1.0);
                if (choose) {
                    value = {{type}}(1.0);
                } else {
                    value = {{type}}(1.00);
                }
                static require value == {{type}}(1.000);
                return 0;
            }
            int32 Main() { return Verify(true); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    private static ScalarBits ParseFloating(string type, string text)
    {
        var primitiveType = type switch {
            "float16" => PrimitiveType.Float16,
            "bfloat16" => PrimitiveType.BFloat16,
            "float32" => PrimitiveType.Float32,
            "float64" => PrimitiveType.Float64,
            "float128" => PrimitiveType.Float128,
            "decimal32" => PrimitiveType.Decimal32,
            "decimal64" => PrimitiveType.Decimal64,
            "decimal128" => PrimitiveType.Decimal128,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        ScalarBits bits;
        bool parsed;
        if (primitiveType is PrimitiveType.Float32 or PrimitiveType.Float64)
        {
            parsed = FloatingLiteralParser.TryParse(text.AsSpan(), primitiveType, out var legacyBits);
            bits = ScalarBits.FromLegacyValue(legacyBits, primitiveType);
        }
        else
        {
            parsed = FloatingLiteralParser.TryParseWide(text.AsSpan(), primitiveType, out bits);
        }
        Assert.That(parsed, Is.True);
        return bits;
    }
}
