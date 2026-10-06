// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class FloatingArithmeticModuleTests
{
    private delegate int Float32Binary(int left, int right);
    private delegate long Float64Binary(long left, long right);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task FloatingBinaryArithmeticMatchesHostedEmitterAsync(BoundRuntimePolicy policy)
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

        var oracleSyntaxTree = SyntaxTree.Parse("""
            [NoThrow] public float32 Float32Add(float32 left, float32 right) { return left + right; }
            [NoThrow] public float32 Float32Subtract(float32 left, float32 right) { return left - right; }
            [NoThrow] public float32 Float32Multiply(float32 left, float32 right) { return left * right; }
            [NoThrow] public float32 Float32Divide(float32 left, float32 right) { return left / right; }
            [NoThrow] public float64 Float64Add(float64 left, float64 right) { return left + right; }
            [NoThrow] public float64 Float64Subtract(float64 left, float64 right) { return left - right; }
            [NoThrow] public float64 Float64Multiply(float64 left, float64 right) { return left * right; }
            [NoThrow] public float64 Float64Divide(float64 left, float64 right) { return left / right; }
            """, "floating-arithmetic-hosted-oracle.terrum");
        Assert.False(oracleSyntaxTree.HasErrors, string.Join(Environment.NewLine, oracleSyntaxTree.Diagnostics));
        var oracleCompilation = Compilation.CreateLibrary(oracleSyntaxTree);
        Assert.False(oracleCompilation.HasErrors, string.Join(Environment.NewLine, oracleCompilation.Diagnostics));
        var oracleProgram = Assert.IsType<BoundProgram>(oracleCompilation.Program);
        var oraclePath = Path.Combine(Path.GetTempPath(), $"terrum-floating-arithmetic-{Guid.NewGuid():N}.dll");
        var oracleLibrary = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(oraclePath, NativeEmitter.EmitLibrary(oracleProgram, [
                "Float32Add", "Float32Subtract", "Float32Multiply",
                "Float32Divide", "Float64Add", "Float64Subtract", "Float64Multiply", "Float64Divide",
            ], NativeRuntimePolicy.FailFast));
            oracleLibrary = NativeLibrary.Load(oraclePath);
            var float32Add = Export<Float32Binary>(oracleLibrary, "Float32Add");
            var float32Subtract = Export<Float32Binary>(oracleLibrary, "Float32Subtract");
            var float32Multiply = Export<Float32Binary>(oracleLibrary, "Float32Multiply");
            var float32Divide = Export<Float32Binary>(oracleLibrary, "Float32Divide");
            var float64Add = Export<Float64Binary>(oracleLibrary, "Float64Add");
            var float64Subtract = Export<Float64Binary>(oracleLibrary, "Float64Subtract");
            var float64Multiply = Export<Float64Binary>(oracleLibrary, "Float64Multiply");
            var float64Divide = Export<Float64Binary>(oracleLibrary, "Float64Divide");
            (string Format, string Operation)[] cases =
            [
                ("Float32", "Add"),
                ("Float32", "Subtract"),
                ("Float32", "Multiply"),
                ("Float32", "Divide"),
                ("Float64", "Add"),
                ("Float64", "Subtract"),
                ("Float64", "Multiply"),
                ("Float64", "Divide"),
            ];
            foreach (var (format, operation) in cases)
            {
                string verificationSource;
                if (format == "Float32")
                {
                    var oracle = operation switch
                    {
                        "Add" => float32Add,
                        "Subtract" => float32Subtract,
                        "Multiply" => float32Multiply,
                        "Divide" => float32Divide,
                        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
                    };
                    verificationSource = CreateFloat32VerificationSource(operation, oracle);
                }
                else
                {
                    var oracle = operation switch
                    {
                        "Add" => float64Add,
                        "Subtract" => float64Subtract,
                        "Multiply" => float64Multiply,
                        "Divide" => float64Divide,
                        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
                    };
                    verificationSource = CreateFloat64VerificationSource(operation, oracle);
                }

                var verification = SyntaxTree.Parse(
                    verificationSource,
                    $"floating-{format.ToLowerInvariant()}-{operation.ToLowerInvariant()}-oracle.terrum");
                Assert.False(verification.HasErrors, string.Join(Environment.NewLine, verification.Diagnostics));
                var compilation = Compilation.Create([arithmetic, componentTests, verification], policy);
                Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

                var program = Assert.IsType<BoundProgram>(compilation.Program);
                var result = NativeJit.Execute(program, (NativeRuntimePolicy)policy);
                Assert.True(result == 0, $"{format} {operation} batch {result} did not match the hosted emitter.");
            }
        }
        finally
        {
            if (oracleLibrary != IntPtr.Zero)
            {
                NativeLibrary.Free(oracleLibrary);
            }

            File.Delete(oraclePath);
        }
    }

    private static string CreateFloat32VerificationSource(string operation, Float32Binary oracle)
    {
        var verifiers = new List<string>();
        var definitions = new StringBuilder();
        AppendFloat32Pairs(definitions, verifiers, operation, oracle);
        return BuildVerificationSource(definitions, verifiers);
    }

    private static string CreateFloat64VerificationSource(string operation, Float64Binary oracle)
    {
        var verifiers = new List<string>();
        var definitions = new StringBuilder();
        AppendFloat64Pairs(definitions, verifiers, operation, oracle);
        return BuildVerificationSource(definitions, verifiers);
    }

    private static string BuildVerificationSource(StringBuilder definitions, List<string> verifiers)
    {
        var groups = new List<string>();
        for (var start = 0; start < verifiers.Count; start += 8)
        {
            var group = $"VerifyFloatingArithmeticGroup{groups.Count}";
            groups.Add(group);
            definitions.Append("bool ").Append(group).AppendLine("() {");
            var end = Math.Min(start + 8, verifiers.Count);
            for (var index = start; index < end; index++)
            {
                definitions.Append("if (!").Append(verifiers[index]).AppendLine("()) { return false; }");
            }
            definitions.AppendLine("return true;");
            definitions.AppendLine("}");
        }

        var roots = new List<string>();
        for (var start = 0; start < groups.Count; start += 8)
        {
            var root = $"VerifyFloatingArithmeticRoot{roots.Count}";
            roots.Add(root);
            definitions.Append("bool ").Append(root).AppendLine("() {");
            var end = Math.Min(start + 8, groups.Count);
            for (var index = start; index < end; index++)
            {
                definitions.Append("if (!").Append(groups[index]).AppendLine("()) { return false; }");
            }
            definitions.AppendLine("return true;");
            definitions.AppendLine("}");
        }

        var source = new StringBuilder("""
            namespace Terrum.CodeAnalysis.Tests;
            int32 Main() {
            """);
        for (var index = 0; index < roots.Count; index++)
        {
            source.Append("if (!").Append(roots[index]).Append("()) { return ")
                .Append(index + 1).AppendLine("; }");
        }
        source.AppendLine("if (!CheckFloat32Division()) { return 900; }");
        source.AppendLine("if (!CheckFloat64DivisionA()) { return 901; }");
        source.AppendLine("if (!CheckFloat64DivisionA2()) { return 902; }");
        source.AppendLine("if (!CheckFloat64DivisionB()) { return 903; }");
        source.AppendLine("return 0;");
        source.AppendLine("}");
        source.Append(definitions);
        return source.ToString();
    }

    private static void AppendFloat32Pairs(
        StringBuilder definitions,
        List<string> verifiers,
        string operation,
        Float32Binary oracle)
    {
        uint[] boundaries =
        [
            0x00000000, 0x80000000, 0x00000001, 0x80000001, 0x007FFFFF, 0x807FFFFF,
            0x00800000, 0x80800000, 0x3F800000, 0x3F800001, 0xBF800000, 0xBF800001,
            0x3F000000, 0x3F7FFFFE, 0x3F7FFFFF, 0x3FC00000, 0x3FFFFFFE, 0x40000000,
            0x00FFFFFF, 0x7F000000, 0x33800000, 0xB3800000,
            0x3EAAAAAB, 0x40400000, 0x41200000,
            0x00000002, 0x00000003, 0x00000004, 0x00000005, 0x7F7FFFFF, 0xFF7FFFFF,
            0x34800000,
            0x7F800000, 0xFF800000, 0x7F800001, 0xFF800001, 0x7FC12345, 0xFFCABCDE,
        ];
        var pairs = new List<(uint Left, uint Right)>();
        foreach (var left in boundaries)
        {
            foreach (var right in boundaries)
            {
                pairs.Add((left, right));
            }
        }

        AddFloat32PairAndReverse(pairs, 0x00000001, 0x7F000000);
        AddFloat32PairAndReverse(pairs, 0x3FCDA5E7, 0x3FFA0242);
        AddFloat32PairAndReverse(pairs, 0x7F7FFFFF, 0x72FFFFFF);
        AddFloat32PairAndReverse(pairs, 0x7F7FFFFF, 0x73000000);
        AddFloat32PairAndReverse(pairs, 0x7F7FFFFF, 0x73000001);
        AddFloat32PairAndReverse(pairs, 0x5E800000, 0x3F800000);
        AddFloat32PairAndReverse(pairs, 0x5F000000, 0x3F800000);
        AddFloat32PairAndReverse(pairs, 0x5F800000, 0x3F800000);
        AddFloat32PairAndReverse(pairs, 0x60000000, 0x3F800000);
        AddFloat32PairAndReverse(pairs, 0xDF000000, 0x3F800000);

        var random = new Random(0xF1032);
        for (var index = 0; index < 1024; index++)
        {
            pairs.Add((NextUInt32(random), NextUInt32(random)));
        }

        AppendFloat32Verifiers(definitions, verifiers, pairs, operation, oracle);
    }

    private static void AppendFloat64Pairs(
        StringBuilder definitions,
        List<string> verifiers,
        string operation,
        Float64Binary oracle)
    {
        ulong[] boundaries =
        [
            0x0000000000000000, 0x8000000000000000, 0x0000000000000001, 0x8000000000000001,
            0x000FFFFFFFFFFFFF, 0x800FFFFFFFFFFFFF, 0x0010000000000000, 0x8010000000000000,
            0x3FF0000000000000, 0x3FF0000000000001, 0xBFF0000000000000, 0xBFF0000000000001,
            0x3FE0000000000000, 0x3FEFFFFFFFFFFFFE, 0x3FEFFFFFFFFFFFFF, 0x3FF8000000000000,
            0x3FFFFFFFFFFFFFFE, 0x4000000000000000, 0x001FFFFFFFFFFFFF,
            0x7FE0000000000000, 0x3CC0000000000000,
            0x3FD5555555555555, 0x4008000000000000, 0x4024000000000000,
            0x3CA0000000000000, 0xBCA0000000000000,
            0x0000000000000002, 0x0000000000000003, 0x0000000000000004, 0x0000000000000005,
            0x7FEFFFFFFFFFFFFF, 0xFFEFFFFFFFFFFFFF,
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

        AddFloat64PairAndReverse(pairs, 0x0000000000000001, 0x7FE0000000000000);
        AddFloat64PairAndReverse(pairs, 0x3FF63A160589F877, 0x3FFF55E11FB797FA);
        AddFloat64PairAndReverse(pairs, 0x7FEFFFFFFFFFFFFF, 0x7C8FFFFFFFFFFFFF);
        AddFloat64PairAndReverse(pairs, 0x7FEFFFFFFFFFFFFF, 0x7C90000000000000);
        AddFloat64PairAndReverse(pairs, 0x7FEFFFFFFFFFFFFF, 0x7C90000000000001);
        AddFloat64PairAndReverse(pairs, 0x43D0000000000000, 0x3FF0000000000000);
        AddFloat64PairAndReverse(pairs, 0x43E0000000000000, 0x3FF0000000000000);
        AddFloat64PairAndReverse(pairs, 0x43F0000000000000, 0x3FF0000000000000);
        AddFloat64PairAndReverse(pairs, 0x4400000000000000, 0x3FF0000000000000);
        AddFloat64PairAndReverse(pairs, 0xC3E0000000000000, 0x3FF0000000000000);

        var random = new Random(0xF1064);
        for (var index = 0; index < 1024; index++)
        {
            pairs.Add((NextUInt64(random), NextUInt64(random)));
        }

        AppendFloat64Verifiers(definitions, verifiers, pairs, operation, oracle);
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
        List<(uint Left, uint Right)> pairs,
        string operation,
        Float32Binary oracle)
    {
        for (var start = 0; start < pairs.Count; start += 8)
        {
            var end = Math.Min(start + 8, pairs.Count);
            AppendFloat32Verifier(definitions, verifiers, pairs, start, end, operation, oracle);
        }
    }

    private static void AppendFloat32Verifier(
        StringBuilder definitions,
        List<string> verifiers,
        List<(uint Left, uint Right)> pairs,
        int start,
        int end,
        string operation,
        Float32Binary oracle)
    {
        var verifier = $"VerifyFloat32{operation}Batch{verifiers.Count}";
        verifiers.Add(verifier);
        definitions.Append("bool ").Append(verifier).AppendLine("() {");
        for (var index = start; index < end; index++)
        {
            var pair = pairs[index];
            uint expected = unchecked((uint)oracle(unchecked((int)pair.Left), unchecked((int)pair.Right)));
            AppendFloat32Check(definitions, $"Float32{operation}Bits", pair.Left, pair.Right, expected);
        }
        definitions.AppendLine("return true;");
        definitions.AppendLine("}");
    }

    private static void AppendFloat64Verifiers(
        StringBuilder definitions,
        List<string> verifiers,
        List<(ulong Left, ulong Right)> pairs,
        string operation,
        Float64Binary oracle)
    {
        for (var start = 0; start < pairs.Count; start += 8)
        {
            var end = Math.Min(start + 8, pairs.Count);
            AppendFloat64Verifier(definitions, verifiers, pairs, start, end, operation, oracle);
        }
    }

    private static void AppendFloat64Verifier(
        StringBuilder definitions,
        List<string> verifiers,
        List<(ulong Left, ulong Right)> pairs,
        int start,
        int end,
        string operation,
        Float64Binary oracle)
    {
        var verifier = $"VerifyFloat64{operation}Batch{verifiers.Count}";
        verifiers.Add(verifier);
        definitions.Append("bool ").Append(verifier).AppendLine("() {");
        for (var index = start; index < end; index++)
        {
            var pair = pairs[index];
            ulong expected = unchecked((ulong)oracle(unchecked((long)pair.Left), unchecked((long)pair.Right)));
            AppendFloat64Check(definitions, $"Float64{operation}Bits", pair.Left, pair.Right, expected);
        }
        definitions.AppendLine("return true;");
        definitions.AppendLine("}");
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

    private static T Export<T>(IntPtr library, string name) where T : Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    }
}
