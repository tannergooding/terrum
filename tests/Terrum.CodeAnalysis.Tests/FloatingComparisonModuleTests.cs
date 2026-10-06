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

public sealed class FloatingComparisonModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task FloatingComparisonMatchesHostedIeeeOperatorsAsync(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var comparisonPath = Path.Combine(root, "self-host", "FloatingComparison.terrum");
        var componentTestsPath = Path.Combine(root, "self-host", "FloatingComparisonTests.terrum");
        var comparison = SyntaxTree.Parse(
            await File.ReadAllTextAsync(comparisonPath).ConfigureAwait(true), comparisonPath);
        var componentTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(componentTestsPath).ConfigureAwait(true), componentTestsPath);
        Assert.False(comparison.HasErrors, string.Join(Environment.NewLine, comparison.Diagnostics));
        Assert.False(componentTests.HasErrors, string.Join(Environment.NewLine, componentTests.Diagnostics));

        var verification = SyntaxTree.Parse(CreateVerificationSource(), "floating-comparison-oracle.terrum");
        Assert.False(verification.HasErrors, string.Join(Environment.NewLine, verification.Diagnostics));
        var compilation = Compilation.Create([comparison, componentTests, verification], policy);
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
            0x00800000, 0x80800000, 0x3F800000, 0xBF800000, 0x40000000, 0xC0000000,
            0x7F7FFFFF, 0xFF7FFFFF, 0x7F800000, 0xFF800000, 0x7F800001, 0xFF800001,
            0x7FC12345, 0xFFCABCDE,
        ];
        var pairs = new List<(uint Left, uint Right)>();
        foreach (var left in boundaries)
        {
            foreach (var right in boundaries)
            {
                pairs.Add((left, right));
            }
        }

        var random = new Random(0xF1032);
        for (var index = 0; index < 128; index++)
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
            0x3FF0000000000000, 0xBFF0000000000000, 0x4000000000000000, 0xC000000000000000,
            0x7FEFFFFFFFFFFFFF, 0xFFEFFFFFFFFFFFFF, 0x7FF0000000000000, 0xFFF0000000000000,
            0x7FF0000000000001, 0xFFF0000000000001, 0x7FF8000000001234, 0xFFFABCDE00000000,
        ];
        var pairs = new List<(ulong Left, ulong Right)>();
        foreach (var left in boundaries)
        {
            foreach (var right in boundaries)
            {
                pairs.Add((left, right));
            }
        }

        var random = new Random(0xF1064);
        for (var index = 0; index < 128; index++)
        {
            pairs.Add((NextUInt64(random), NextUInt64(random)));
        }

        AppendFloat64Verifiers(definitions, verifiers, pairs);
    }

    private static void AppendFloat32Verifiers(
        StringBuilder definitions,
        List<string> verifiers,
        List<(uint Left, uint Right)> pairs)
    {
        for (var start = 0; start < pairs.Count; start += 32)
        {
            var verifier = $"VerifyFloat32Batch{verifiers.Count}";
            verifiers.Add(verifier);
            definitions.Append("bool ").Append(verifier).AppendLine("() {");
            var end = Math.Min(start + 32, pairs.Count);
            for (var index = start; index < end; index++)
            {
                var pair = pairs[index];
                var left = BitConverter.Int32BitsToSingle(unchecked((int)pair.Left));
                var right = BitConverter.Int32BitsToSingle(unchecked((int)pair.Right));
                AppendCheck(definitions, "CompareFloat32Bits", "uint32", pair.Left.ToString("X8"),
                    pair.Right.ToString("X8"), GetComparisonResult(left, right));
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
        for (var start = 0; start < pairs.Count; start += 32)
        {
            var verifier = $"VerifyFloat64Batch{verifiers.Count}";
            verifiers.Add(verifier);
            definitions.Append("bool ").Append(verifier).AppendLine("() {");
            var end = Math.Min(start + 32, pairs.Count);
            for (var index = start; index < end; index++)
            {
                var pair = pairs[index];
                var left = BitConverter.Int64BitsToDouble(unchecked((long)pair.Left));
                var right = BitConverter.Int64BitsToDouble(unchecked((long)pair.Right));
                AppendCheck(definitions, "CompareFloat64Bits", "uint64", pair.Left.ToString("X16"),
                    pair.Right.ToString("X16"), GetComparisonResult(left, right));
            }
            definitions.AppendLine("return true;");
            definitions.AppendLine("}");
        }
    }

    private static void AppendCheck(
        StringBuilder source,
        string function,
        string integerType,
        string leftBits,
        string rightBits,
        int expected)
    {
        source.Append("if (").Append(function).Append('(').Append(integerType).Append("(0x").Append(leftBits)
            .Append("), ").Append(integerType).Append("(0x").Append(rightBits).Append(")) != ")
            .Append(expected).AppendLine(") { return false; }");
    }

    private static int GetComparisonResult(float left, float right)
    {
        if (left < right)
        {
            return -1;
        }
        if (left > right)
        {
            return 1;
        }
        return left == right ? 0 : 2;
    }

    private static int GetComparisonResult(double left, double right)
    {
        if (left < right)
        {
            return -1;
        }
        if (left > right)
        {
            return 1;
        }
        return left == right ? 0 : 2;
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
