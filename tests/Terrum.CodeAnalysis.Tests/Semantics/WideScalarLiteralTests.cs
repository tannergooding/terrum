// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class WideScalarLiteralTests
{
    [TestCase("int128", PrimitiveType.Int128,
        "-170141183460469231731687303715884105728", 0UL, 0x8000_0000_0000_0000UL)]
    [TestCase("int128", PrimitiveType.Int128,
        "0x80000000000000000000000000000000", 0UL, 0x8000_0000_0000_0000UL)]
    [TestCase("int128", PrimitiveType.Int128,
        "0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", ulong.MaxValue, ulong.MaxValue)]
    [TestCase("int128", PrimitiveType.Int128,
        "170141183460469231731687303715884105727", ulong.MaxValue, 0x7FFF_FFFF_FFFF_FFFFUL)]
    [TestCase("uint128", PrimitiveType.UInt128,
        "340282366920938463463374607431768211455", ulong.MaxValue, ulong.MaxValue)]
    [TestCase("uint128", PrimitiveType.UInt128,
        "0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", ulong.MaxValue, ulong.MaxValue)]
    public void BindsWideIntegerLiteralsInTargetFormat(
        string typeName, PrimitiveType type, string literal, ulong low, ulong high)
    {
        var constant = BindReturnConstant(typeName, literal);

        Assert.That(constant.Type, Is.EqualTo(type));
        Assert.That(constant.Bits.Low, Is.EqualTo(low));
        Assert.That(constant.Bits.High, Is.EqualTo(high));
    }

    [Test]
    public void BindsWideBinaryIntegerLiteralsInTargetFormat()
    {
        var literal = "0b" + new string('1', 128);
        foreach (var typeName in new[] { "int128", "uint128" })
        {
            var constant = BindReturnConstant(typeName, literal);

            Assert.That(constant.Bits.Low, Is.EqualTo(ulong.MaxValue));
            Assert.That(constant.Bits.High, Is.EqualTo(ulong.MaxValue));
        }
    }

    [TestCase("float16", PrimitiveType.Float16, "1.5", 0x3E00UL, 0UL)]
    [TestCase("float16", PrimitiveType.Float16, "0x1p-24", 1UL, 0UL)]
    [TestCase("float16", PrimitiveType.Float16, "0x1p-25", 0UL, 0UL)]
    [TestCase("float16", PrimitiveType.Float16, "0x3p-25", 2UL, 0UL)]
    [TestCase("float16", PrimitiveType.Float16, "0x5p-25", 2UL, 0UL)]
    [TestCase("float16", PrimitiveType.Float16, "-0.0", 0x8000UL, 0UL)]
    [TestCase("float16", PrimitiveType.Float16, "0x3", 0x4200UL, 0UL)]
    [TestCase("bfloat16", PrimitiveType.BFloat16, "1.5", 0x3FC0UL, 0UL)]
    [TestCase("bfloat16", PrimitiveType.BFloat16, "0x1p-133", 1UL, 0UL)]
    [TestCase("bfloat16", PrimitiveType.BFloat16, "0x1p-134", 0UL, 0UL)]
    [TestCase("bfloat16", PrimitiveType.BFloat16, "0x3p-134", 2UL, 0UL)]
    [TestCase("bfloat16", PrimitiveType.BFloat16, "0x5p-134", 2UL, 0UL)]
    [TestCase("float128", PrimitiveType.Float128, "1.5", 0UL, 0x3FFF_8000_0000_0000UL)]
    [TestCase("float128", PrimitiveType.Float128, "0x1p-16494", 1UL, 0UL)]
    [TestCase("float128", PrimitiveType.Float128, "0x1p-16495", 0UL, 0UL)]
    [TestCase("float128", PrimitiveType.Float128, "0x3p-16495", 2UL, 0UL)]
    [TestCase("float128", PrimitiveType.Float128, "0x5p-16495", 2UL, 0UL)]
    [TestCase("float128", PrimitiveType.Float128, "0x1p+16384", 0UL, 0x7FFF_0000_0000_0000UL)]
    [TestCase("decimal32", PrimitiveType.Decimal32, "1.20", 0x3180_0078UL, 0UL)]
    [TestCase("decimal32", PrimitiveType.Decimal32, "1.2", 0x3200_000CUL, 0UL)]
    [TestCase("decimal32", PrimitiveType.Decimal32, "-0.00", 0xB180_0000UL, 0UL)]
    [TestCase("decimal32", PrimitiveType.Decimal32, "1.2345665", 0x2F92_D686UL, 0UL)]
    [TestCase("decimal32", PrimitiveType.Decimal32, "1.2345675", 0x2F92_D688UL, 0UL)]
    [TestCase("decimal32", PrimitiveType.Decimal32, "1.23456785", 0x2F92_D688UL, 0UL)]
    [TestCase("decimal32", PrimitiveType.Decimal32, "1e96", 0x5F8F_4240UL, 0UL)]
    [TestCase("decimal32", PrimitiveType.Decimal32, "1e-101", 1UL, 0UL)]
    [TestCase("decimal32", PrimitiveType.Decimal32, "1e97", 0x7800_0000UL, 0UL)]
    [TestCase("decimal32", PrimitiveType.Decimal32, "0x1p+2", 0x3280_0004UL, 0UL)]
    [TestCase("decimal32", PrimitiveType.Decimal32, "0x1.8p+1", 0x3280_0003UL, 0UL)]
    [TestCase("decimal32", PrimitiveType.Decimal32, "0xFF", 0x3280_00FFUL, 0UL)]
    [TestCase("decimal32", PrimitiveType.Decimal32, "9000000", 0x6CA9_5440UL, 0UL)]
    [TestCase("decimal64", PrimitiveType.Decimal64,
        "1.20", 0x3180_0000_0000_0078UL, 0UL)]
    [TestCase("decimal64", PrimitiveType.Decimal64,
        "9007199254740993", 0x6C70_0000_0000_0001UL, 0UL)]
    [TestCase("decimal128", PrimitiveType.Decimal128,
        "1.20", 0x78UL, 0x303C_0000_0000_0000UL)]
    public void BindsWideFloatingLiteralsInTargetFormat(
        string typeName, PrimitiveType type, string literal, ulong low, ulong high)
    {
        var constant = BindReturnConstant(typeName, literal);

        Assert.That(constant.Type, Is.EqualTo(type));
        Assert.That(constant.Bits.Low, Is.EqualTo(low));
        Assert.That(constant.Bits.High, Is.EqualTo(high));
    }

    [TestCase("int128", "170141183460469231731687303715884105728")]
    [TestCase("int128", "-170141183460469231731687303715884105729")]
    [TestCase("int128", "+0x80000000000000000000000000000000")]
    [TestCase("int128", "0x100000000000000000000000000000000")]
    [TestCase("uint128", "340282366920938463463374607431768211456")]
    public void RejectsOutOfRangeWideIntegerLiterals(string typeName, string literal)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public {typeName} Read() {{ return {literal}; }}"));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3013"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    private static BoundConstant BindReturnConstant(string typeName, string literal)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public {typeName} Read() {{ return {literal}; }}"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = compilation.Program!.Functions.Single();
        var result = (BoundReturn)function.Body.Statements.Single();
        return (BoundConstant)result.Value!;
    }
}
