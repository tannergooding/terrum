// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class FloatingArithmeticModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task FloatingAddSubtractMatchesHostedIeeeOperatorsAsync(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var arithmeticPath = Path.Combine(root, "self-host", "FloatingArithmetic.terrum");
        var componentTestsPath = Path.Combine(root, "self-host", "FloatingArithmeticTests.terrum");
        var arithmetic = SyntaxTree.Parse(
            await File.ReadAllTextAsync(arithmeticPath).ConfigureAwait(true), arithmeticPath);
        var componentTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(componentTestsPath).ConfigureAwait(true), componentTestsPath);
        Assert.False(arithmetic.HasErrors, string.Join(Environment.NewLine, arithmetic.Diagnostics));
        Assert.False(componentTests.HasErrors, string.Join(Environment.NewLine, componentTests.Diagnostics));

        var verification = SyntaxTree.Parse(CreateVerificationSource(), "floating-arithmetic-oracle.terrum");
        Assert.False(verification.HasErrors, string.Join(Environment.NewLine, verification.Diagnostics));
        var compilation = Compilation.Create([arithmetic, componentTests, verification], policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(0, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
    }

    private static string CreateVerificationSource()
    {
        var verifiers = new List<string>();
        var definitions = new StringBuilder();
        AppendFloat32Pairs(definitions, verifiers);
        AppendFloat64Pairs(definitions, verifiers);
        var source = new StringBuilder("""
            namespace Terrum.CodeAnalysis.Tests;
            int32 Main() {
            """);
        foreach (var verifier in verifiers)
        {
            source.Append("if (!").Append(verifier).AppendLine("()) { return 1; }");
        }
        source.AppendLine("return 0;");
        source.AppendLine("}");
        source.Append(definitions);
        return source.ToString();
    }

    private static void AppendFloat32Pairs(StringBuilder definitions, List<string> verifiers)
    {
        uint[] boundaries =
        [
            0x00000000, 0x80000000, 0x00000001, 0x80000001, 0x007FFFFF, 0x807FFFFF,
            0x00800000, 0x80800000, 0x3F800000, 0x3F800001, 0xBF800000, 0xBF800001,
            0x33800000, 0xB3800000, 0x7F7FFFFF, 0xFF7FFFFF, 0x7F800000, 0xFF800000,
            0x7F800001, 0xFF800001, 0x7FC12345, 0xFFCABCDE,
        ];
        var pairs = new List<(uint Left, uint Right)>();
        foreach (var left in boundaries)
        {
            foreach (var right in boundaries)
            {
                pairs.Add((left, right));
            }
        }

        AddFloat32PairAndReverse(pairs, 0x7F7FFFFF, 0x72FFFFFF);
        AddFloat32PairAndReverse(pairs, 0x7F7FFFFF, 0x73000000);
        AddFloat32PairAndReverse(pairs, 0x7F7FFFFF, 0x73000001);
        AddFloat32PairAndReverse(pairs, 0x5E800000, 0x3F800000);
        AddFloat32PairAndReverse(pairs, 0x5F000000, 0x3F800000);
        AddFloat32PairAndReverse(pairs, 0x5F800000, 0x3F800000);
        AddFloat32PairAndReverse(pairs, 0x60000000, 0x3F800000);
        AddFloat32PairAndReverse(pairs, 0xDF000000, 0x3F800000);

        var random = new Random(0xF1032);
        for (var index = 0; index < 256; index++)
        {
            pairs.Add((NextUInt32(random), NextUInt32(random)));
        }

        AppendFloat32Verifiers(definitions, verifiers, pairs);
    }

    private static void AppendFloat64Pairs(StringBuilder definitions, List<string> verifiers)
    {
        ulong[] boundaries =
        [
            0x0000000000000000, 0x8000000000000000, 0x0000000000000001, 0x8000000000000001,
            0x000FFFFFFFFFFFFF, 0x800FFFFFFFFFFFFF, 0x0010000000000000, 0x8010000000000000,
            0x3FF0000000000000, 0x3FF0000000000001, 0xBFF0000000000000, 0xBFF0000000000001,
            0x3CA0000000000000, 0xBCA0000000000000, 0x7FEFFFFFFFFFFFFF, 0xFFEFFFFFFFFFFFFF,
            0x7FF0000000000000, 0xFFF0000000000000, 0x7FF0000000000001, 0xFFF0000000000001,
            0x7FF8000000001234, 0xFFFABCDE00000000,
        ];
        var pairs = new List<(ulong Left, ulong Right)>();
        foreach (var left in boundaries)
        {
            foreach (var right in boundaries)
            {
                pairs.Add((left, right));
            }
        }

        AddFloat64PairAndReverse(pairs, 0x7FEFFFFFFFFFFFFF, 0x7C8FFFFFFFFFFFFF);
        AddFloat64PairAndReverse(pairs, 0x7FEFFFFFFFFFFFFF, 0x7C90000000000000);
        AddFloat64PairAndReverse(pairs, 0x7FEFFFFFFFFFFFFF, 0x7C90000000000001);
        AddFloat64PairAndReverse(pairs, 0x43D0000000000000, 0x3FF0000000000000);
        AddFloat64PairAndReverse(pairs, 0x43E0000000000000, 0x3FF0000000000000);
        AddFloat64PairAndReverse(pairs, 0x43F0000000000000, 0x3FF0000000000000);
        AddFloat64PairAndReverse(pairs, 0x4400000000000000, 0x3FF0000000000000);
        AddFloat64PairAndReverse(pairs, 0xC3E0000000000000, 0x3FF0000000000000);

        var random = new Random(0xF1064);
        for (var index = 0; index < 256; index++)
        {
            pairs.Add((NextUInt64(random), NextUInt64(random)));
        }

        AppendFloat64Verifiers(definitions, verifiers, pairs);
    }

    private static void AddFloat32PairAndReverse(List<(uint Left, uint Right)> pairs, uint left, uint right)
    {
        pairs.Add((left, right));
        pairs.Add((right, left));
    }

    private static void AddFloat64PairAndReverse(List<(ulong Left, ulong Right)> pairs, ulong left, ulong right)
    {
        pairs.Add((left, right));
        pairs.Add((right, left));
    }

    private static void AppendFloat32Verifiers(
        StringBuilder definitions,
        List<string> verifiers,
        List<(uint Left, uint Right)> pairs)
    {
        for (var start = 0; start < pairs.Count; start += 16)
        {
            var verifier = $"VerifyFloat32Batch{verifiers.Count}";
            verifiers.Add(verifier);
            definitions.Append("bool ").Append(verifier).AppendLine("() {");
            var end = Math.Min(start + 16, pairs.Count);
            for (var index = start; index < end; index++)
            {
                var pair = pairs[index];
                var left = BitConverter.Int32BitsToSingle(unchecked((int)pair.Left));
                var right = BitConverter.Int32BitsToSingle(unchecked((int)pair.Right));
                AppendFloat32Check(definitions, "Float32AddBits", pair.Left, pair.Right,
                    CanonicalizeNaN(left + right));
                AppendFloat32Check(definitions, "Float32SubtractBits", pair.Left, pair.Right,
                    CanonicalizeNaN(left - right));
            }
            definitions.AppendLine("return true;");
            definitions.AppendLine("}");
        }
    }

    private static void AppendFloat64Verifiers(
        StringBuilder definitions,
        List<string> verifiers,
        List<(ulong Left, ulong Right)> pairs)
    {
        for (var start = 0; start < pairs.Count; start += 16)
        {
            var verifier = $"VerifyFloat64Batch{verifiers.Count}";
            verifiers.Add(verifier);
            definitions.Append("bool ").Append(verifier).AppendLine("() {");
            var end = Math.Min(start + 16, pairs.Count);
            for (var index = start; index < end; index++)
            {
                var pair = pairs[index];
                var left = BitConverter.Int64BitsToDouble(unchecked((long)pair.Left));
                var right = BitConverter.Int64BitsToDouble(unchecked((long)pair.Right));
                AppendFloat64Check(definitions, "Float64AddBits", pair.Left, pair.Right,
                    CanonicalizeNaN(left + right));
                AppendFloat64Check(definitions, "Float64SubtractBits", pair.Left, pair.Right,
                    CanonicalizeNaN(left - right));
            }
            definitions.AppendLine("return true;");
            definitions.AppendLine("}");
        }
    }

    private static void AppendFloat32Check(StringBuilder source, string function, uint left, uint right, uint expected)
    {
        source.Append("if (").Append(function).Append("(uint32(0x").Append(left.ToString("X8"))
            .Append("), uint32(0x").Append(right.ToString("X8")).Append(")) != uint32(0x")
            .Append(expected.ToString("X8")).AppendLine(")) { return false; }");
    }

    private static void AppendFloat64Check(
        StringBuilder source,
        string function,
        ulong left,
        ulong right,
        ulong expected)
    {
        source.Append("if (").Append(function).Append("(uint64(0x").Append(left.ToString("X16"))
            .Append("), uint64(0x").Append(right.ToString("X16")).Append(")) != uint64(0x")
            .Append(expected.ToString("X16")).AppendLine(")) { return false; }");
    }

    private static uint CanonicalizeNaN(float value)
    {
        return float.IsNaN(value)
            ? 0x7FC00000
            : unchecked((uint)BitConverter.SingleToInt32Bits(value));
    }

    private static ulong CanonicalizeNaN(double value)
    {
        return double.IsNaN(value)
            ? 0x7FF8000000000000
            : unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
    }

    private static uint NextUInt32(Random random)
    {
        return (uint)random.NextInt64(0, 1L << 32);
    }

    private static ulong NextUInt64(Random random)
    {
        return ((ulong)NextUInt32(random) << 32) | NextUInt32(random);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Terrum.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate Terrum.slnx.");
    }
}
