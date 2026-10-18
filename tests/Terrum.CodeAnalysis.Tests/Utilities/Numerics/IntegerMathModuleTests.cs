using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Utilities.Numerics;

public sealed class IntegerMathModuleTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task IntegerMathCandidatesPersistAndMatchBoundaryOraclesAsync(
        BoundRuntimePolicy policy)
    {
        var path = TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerMath.terrum");
        var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));

        var producerCompilation = Compilation.CreateLibrary(tree, policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var exports = new[] {
            "AbsInt32",
            "TryAbsInt32",
            "SignInt32",
            "MinInt32",
            "MaxInt32",
            "ClampInt32",
            "BigMultiplyInt32",
            "GreatestCommonDivisorInt32",
            "AbsInt64",
            "TryAbsInt64",
            "SignInt64",
            "MinInt64",
            "MaxInt64",
            "ClampInt64",
            "GreatestCommonDivisorInt64",
        };
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = producerCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            Array.ConvertAll(exports, static name => "Terrum.CodeAnalysis.Numerics64." + name));
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerSource = new StringBuilder("using Terrum.CodeAnalysis.Numerics64;\nint32 Main() {\n");
        var helpers = new StringBuilder();
        var checks = new List<string>();
        var failureCode = 1;
        int[] values32 = [
            int.MinValue, int.MinValue + 1, -46341, -1, 0, 1, 46340, 46341,
            int.MaxValue - 1, int.MaxValue,
        ];
        long[] values64 = [
            long.MinValue, long.MinValue + 1, -3037000500, -1, 0, 1, 3037000499,
            3037000500, long.MaxValue - 1, long.MaxValue,
        ];
        (BigInteger Minimum, BigInteger Maximum)[] bounds32 = [
            (int.MinValue, int.MinValue),
            (int.MinValue, 0),
            (-5, 5),
            (0, int.MaxValue),
            (int.MaxValue, int.MaxValue),
        ];
        (BigInteger Minimum, BigInteger Maximum)[] bounds64 = [
            (long.MinValue, long.MinValue),
            (long.MinValue, 0),
            (-5, 5),
            (0, long.MaxValue),
            (long.MaxValue, long.MaxValue),
        ];

        AppendTryAbsHelpers(helpers);
        foreach (var value in values32)
        {
            var input = Int32Literal(value);
            var succeeds = value != int.MinValue;
            var expected = succeeds ? BigInteger.Abs(value) : BigInteger.Zero;
            if (succeeds)
            {
                AppendCheck(consumerSource, checks, ref failureCode,
                    $"AbsInt32({input}) == {Int32Literal(expected)}", $"AbsInt32({value})");
            }
            AppendCheck(consumerSource, checks, ref failureCode,
                $"CheckTryAbsInt32({input}, {Int32Literal(expected)}, {(succeeds ? "true" : "false")})",
                $"TryAbsInt32({value})");
            AppendCheck(consumerSource, checks, ref failureCode,
                $"SignInt32({input}) == {Math.Sign(value).ToString(CultureInfo.InvariantCulture)}",
                $"SignInt32({value})");
        }

        foreach (var left in values32)
        {
            foreach (var right in values32)
            {
                var leftLiteral = Int32Literal(left);
                var rightLiteral = Int32Literal(right);
                AppendCheck(consumerSource, checks, ref failureCode,
                    $"MinInt32({leftLiteral}, {rightLiteral}) == "
                        + Int32Literal(BigInteger.Min(left, right)),
                    $"MinInt32({left}, {right})");
                AppendCheck(consumerSource, checks, ref failureCode,
                    $"MaxInt32({leftLiteral}, {rightLiteral}) == "
                        + Int32Literal(BigInteger.Max(left, right)),
                    $"MaxInt32({left}, {right})");
                AppendCheck(consumerSource, checks, ref failureCode,
                    $"BigMultiplyInt32({leftLiteral}, {rightLiteral}) == "
                        + Int64Literal(new BigInteger(left) * right),
                    $"BigMultiplyInt32({left}, {right})");
                AppendCheck(consumerSource, checks, ref failureCode,
                    $"GreatestCommonDivisorInt32({leftLiteral}, {rightLiteral}) == "
                        + UInt32Literal(BigInteger.GreatestCommonDivisor(
                            BigInteger.Abs(left), BigInteger.Abs(right))),
                    $"GreatestCommonDivisorInt32({left}, {right})");
            }
        }
        foreach (var value in values32)
        {
            foreach (var (minimum, maximum) in bounds32)
            {
                var expected = Clamp(value, minimum, maximum);
                AppendCheck(consumerSource, checks, ref failureCode,
                    $"ClampInt32({Int32Literal(value)}, {Int32Literal(minimum)}, "
                        + $"{Int32Literal(maximum)}) == {Int32Literal(expected)}",
                    $"ClampInt32({value}, {minimum}, {maximum})");
            }
        }

        foreach (var value in values64)
        {
            var input = Int64Literal(value);
            var succeeds = value != long.MinValue;
            var expected = succeeds ? BigInteger.Abs(value) : BigInteger.Zero;
            if (succeeds)
            {
                AppendCheck(consumerSource, checks, ref failureCode,
                    $"AbsInt64({input}) == {Int64Literal(expected)}", $"AbsInt64({value})");
            }
            AppendCheck(consumerSource, checks, ref failureCode,
                $"CheckTryAbsInt64({input}, {Int64Literal(expected)}, {(succeeds ? "true" : "false")})",
                $"TryAbsInt64({value})");
            AppendCheck(consumerSource, checks, ref failureCode,
                $"SignInt64({input}) == {Math.Sign(value).ToString(CultureInfo.InvariantCulture)}",
                $"SignInt64({value})");
        }

        foreach (var left in values64)
        {
            foreach (var right in values64)
            {
                var leftLiteral = Int64Literal(left);
                var rightLiteral = Int64Literal(right);
                AppendCheck(consumerSource, checks, ref failureCode,
                    $"MinInt64({leftLiteral}, {rightLiteral}) == "
                        + Int64Literal(BigInteger.Min(left, right)),
                    $"MinInt64({left}, {right})");
                AppendCheck(consumerSource, checks, ref failureCode,
                    $"MaxInt64({leftLiteral}, {rightLiteral}) == "
                        + Int64Literal(BigInteger.Max(left, right)),
                    $"MaxInt64({left}, {right})");
                AppendCheck(consumerSource, checks, ref failureCode,
                    $"GreatestCommonDivisorInt64({leftLiteral}, {rightLiteral}) == "
                        + UInt64Literal(BigInteger.GreatestCommonDivisor(
                            BigInteger.Abs(left), BigInteger.Abs(right))),
                    $"GreatestCommonDivisorInt64({left}, {right})");
            }
        }
        foreach (var value in values64)
        {
            foreach (var (minimum, maximum) in bounds64)
            {
                var expected = Clamp(value, minimum, maximum);
                AppendCheck(consumerSource, checks, ref failureCode,
                    $"ClampInt64({Int64Literal(value)}, {Int64Literal(minimum)}, "
                        + $"{Int64Literal(maximum)}) == {Int64Literal(expected)}",
                    $"ClampInt64({value}, {minimum}, {maximum})");
            }
        }

        Assert.That(checks.Count, Is.EqualTo(858));
        consumerSource.AppendLine("return 0;");
        consumerSource.AppendLine("}");
        consumerSource.Append(helpers);
        var consumerTree = SyntaxTree.Parse(consumerSource.ToString(), "integer-math-consumer.terrum");
        Assert.That(consumerTree.HasErrors, Is.False, string.Join(Environment.NewLine, consumerTree.Diagnostics));
        var consumerCompilation = Compilation.Create(consumerTree, reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);
        var actual = NativeJit.Execute(ModuleCompiler.Link(consumer, producer), (NativeRuntimePolicy)policy);
        Assert.That(actual == 0, Is.True, actual > 0 && actual <= checks.Count
                ? $"IntegerMath boundary {actual} failed: {checks[actual - 1]}."
                : $"Expected all IntegerMath checks to pass, got consumer result {actual}.");

        foreach (var (function, argument) in new[] {
            ("TryAbsInt32", "int32.MinValue"),
            ("TryAbsInt64", "int64.MinValue"),
        })
        {
            var invalidConsumer = SyntaxTree.Parse(
                "using Terrum.CodeAnalysis.Numerics64;\n"
                + "int32 Main() {\n"
                + $"    if (!{function}({argument}, init var result)) {{ return int32(result); }}\n"
                + "    return 0;\n"
                + "}",
                "integer-math-unpublished-" + function + ".terrum");
            var invalidCompilation = Compilation.Create(invalidConsumer, reference, policy);
            Assert.That(global::System.Linq.Enumerable.Any(invalidCompilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
            Assert.That(invalidCompilation.Program, Is.Null);
        }
    }

    private static void AppendTryAbsHelpers(StringBuilder source)
    {
        foreach (var (function, type) in new[] {
            ("TryAbsInt32", "int32"),
            ("TryAbsInt64", "int64"),
        })
        {
            source.Append("bool Check").Append(function).Append('(').Append(type)
                .Append(" value, ").Append(type).AppendLine(" expected, bool shouldSucceed) {")
                .AppendLine("    if (shouldSucceed) {")
                .Append("        if (!").Append(function)
                .AppendLine("(value, init var actual)) { return false; }")
                .AppendLine("        return actual == expected;")
                .AppendLine("    }")
                .Append("    return !").Append(function)
                .AppendLine("(value, init var unpublished);")
                .AppendLine("}");
        }
    }

    private static void AppendCheck(StringBuilder source, List<string> checks, ref int failureCode,
        string expression, string description)
    {
        source.Append("if (!(").Append(expression).Append(")) { return ")
            .Append(failureCode.ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
        checks.Add(description);
        failureCode++;
    }

    private static BigInteger Clamp(BigInteger value, BigInteger minimum, BigInteger maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;

    private static string Int32Literal(BigInteger value) =>
        value == int.MinValue ? "int32.MinValue"
            : value == int.MaxValue ? "int32.MaxValue"
                : value.ToString(CultureInfo.InvariantCulture);

    private static string Int64Literal(BigInteger value) =>
        value == long.MinValue ? "int64.MinValue"
            : value == long.MaxValue ? "int64.MaxValue"
                : value.ToString(CultureInfo.InvariantCulture);

    private static string UInt32Literal(BigInteger value) =>
        value.IsZero ? "0"
            : value == uint.MaxValue ? "uint32.MaxValue"
                : value.ToString(CultureInfo.InvariantCulture);

    private static string UInt64Literal(BigInteger value) =>
        value.IsZero ? "0"
            : value == ulong.MaxValue ? "uint64.MaxValue"
                : value.ToString(CultureInfo.InvariantCulture);

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
