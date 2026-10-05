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
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class WideIntegerFactsModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CheckedWideArithmeticPersistsAndMatchesBigIntegerBoundariesAsync(
        BoundRuntimePolicy policy)
    {
        var path = Path.Combine(RepositoryRoot(), "self-host", "WideIntegerFacts.terrum");
        var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));

        var producerCompilation = Compilation.CreateLibrary(tree, policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var exports = new[] {
            "TryAddInt64",
            "TrySubtractInt64",
            "TryMultiplyInt64",
            "TryDivideInt64",
            "TryRemainderInt64",
            "TryNegateInt64",
            "TryAbsoluteInt64",
            "MagnitudeInt64",
            "TryAddUInt64",
            "TrySubtractUInt64",
            "TryMultiplyUInt64",
            "TryDivideUInt64",
            "TryRemainderUInt64",
        };
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            Array.ConvertAll(exports, static name => "Terrum.CodeAnalysis.Numerics64." + name));
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerSource = new StringBuilder("using Terrum.CodeAnalysis.Numerics64;\nint32 Main() {\n");
        var helpers = new StringBuilder();
        var checks = new List<string>();
        var failureCode = 1;
        long[] signedValues = [
            long.MinValue, long.MinValue + 1, -3037000500, -1, 0, 1,
            3037000499, 3037000500, long.MaxValue - 1, long.MaxValue,
        ];
        ulong[] unsignedValues = [
            0, 1, 2, 4294967295, 4294967296, 9223372036854775807,
            9223372036854775808, ulong.MaxValue - 1, ulong.MaxValue,
        ];

        foreach (var operation in BinaryOperations())
        {
            AppendBinaryHelper(helpers, operation);
            if (operation.IsUnsigned)
            {
                foreach (var left in unsignedValues)
                {
                    foreach (var right in unsignedValues)
                    {
                        var succeeds = TryExpectedBinary(operation.Name, left, right, out var result);
                        AppendCheck(consumerSource, checks, ref failureCode,
                            "Check" + operation.Name, UInt64Literal(left), UInt64Literal(right),
                            UInt64Literal(succeeds ? result : BigInteger.Zero), succeeds,
                            $"{operation.Name}({left}, {right})");
                    }
                }
            }
            else
            {
                foreach (var left in signedValues)
                {
                    foreach (var right in signedValues)
                    {
                        var succeeds = TryExpectedBinary(operation.Name, left, right, out var result);
                        AppendCheck(consumerSource, checks, ref failureCode,
                            "Check" + operation.Name, Int64Literal(left), Int64Literal(right),
                            Int64Literal(succeeds ? result : BigInteger.Zero), succeeds,
                            $"{operation.Name}({left}, {right})");
                    }
                }
            }
        }

        AppendUnaryHelpers(helpers);
        foreach (var value in signedValues)
        {
            foreach (var function in new[] { "TryNegateInt64", "TryAbsoluteInt64" })
            {
                var succeeds = value != long.MinValue;
                var result = function == "TryNegateInt64" ? -new BigInteger(value) : BigInteger.Abs(value);
                AppendCheck(consumerSource, checks, ref failureCode, "Check" + function,
                    Int64Literal(value), Int64Literal(succeeds ? result : BigInteger.Zero), succeeds,
                    $"{function}({value})");
            }

            AppendCheck(consumerSource, checks, ref failureCode, "CheckMagnitudeInt64",
                Int64Literal(value), UInt64Literal(BigInteger.Abs(value)),
                $"MagnitudeInt64({value})");
        }

        consumerSource.AppendLine("return 0;");
        consumerSource.AppendLine("}");
        consumerSource.Append(helpers);
        var consumerTree = SyntaxTree.Parse(consumerSource.ToString(), "wide-integer-facts-consumer.terrum");
        Assert.False(consumerTree.HasErrors, string.Join(Environment.NewLine, consumerTree.Diagnostics));
        var consumerCompilation = Compilation.Create(consumerTree, reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var actual = NativeJit.Execute(ModuleCompiler.Link(consumer, producer), (NativeRuntimePolicy)policy);
        Assert.True(actual == 0,
            actual > 0 && actual <= checks.Count
                ? $"Wide integer boundary {actual} failed: {checks[actual - 1]}."
                : $"Expected all wide integer checks to pass, got consumer result {actual}.");

        foreach (var (function, arguments) in RejectedCalls())
        {
            var invalidConsumer = SyntaxTree.Parse(
                "using Terrum.CodeAnalysis.Numerics64;\n"
                + "int32 Main() {\n"
                + $"    if (!{function}({arguments}, init var result)) {{ return int32(result); }}\n"
                + "    return 0;\n"
                + "}",
                "wide-integer-unpublished-" + function + ".terrum");
            var invalidCompilation = Compilation.Create(invalidConsumer, reference, policy);
            Assert.Contains(invalidCompilation.Diagnostics,
                static diagnostic => diagnostic.Code == "TRM3016");
            Assert.Null(invalidCompilation.Program);
        }
    }

    private static (string Function, string Arguments)[] RejectedCalls() =>
    [
        ("TryAddInt64", "int64.MaxValue, 1"),
        ("TrySubtractInt64", "int64.MinValue, 1"),
        ("TryMultiplyInt64", "int64.MinValue, -1"),
        ("TryDivideInt64", "int64.MinValue, -1"),
        ("TryRemainderInt64", "1, 0"),
        ("TryNegateInt64", "int64.MinValue"),
        ("TryAbsoluteInt64", "int64.MinValue"),
        ("TryAddUInt64", "uint64.MaxValue, 1"),
        ("TrySubtractUInt64", "0, 1"),
        ("TryMultiplyUInt64", "uint64.MaxValue, 2"),
        ("TryDivideUInt64", "1, 0"),
        ("TryRemainderUInt64", "1, 0"),
    ];

    private static List<BinaryOperation> BinaryOperations() =>
    [
        new("TryAddInt64", "int64", false),
        new("TrySubtractInt64", "int64", false),
        new("TryMultiplyInt64", "int64", false),
        new("TryDivideInt64", "int64", false),
        new("TryRemainderInt64", "int64", false),
        new("TryAddUInt64", "uint64", true),
        new("TrySubtractUInt64", "uint64", true),
        new("TryMultiplyUInt64", "uint64", true),
        new("TryDivideUInt64", "uint64", true),
        new("TryRemainderUInt64", "uint64", true),
    ];

    private static void AppendBinaryHelper(StringBuilder source, BinaryOperation operation)
    {
        source.Append("bool Check").Append(operation.Name).Append('(')
            .Append(operation.Type).Append(" left, ").Append(operation.Type).Append(' ')
            .AppendLine("right, " + operation.Type + " expected, bool shouldSucceed) {")
            .AppendLine("    if (shouldSucceed) {")
            .Append("        if (!").Append(operation.Name)
            .AppendLine("(left, right, init var actual)) { return false; }")
            .AppendLine("        return actual == expected;")
            .AppendLine("    }")
            .Append("    return !").Append(operation.Name)
            .AppendLine("(left, right, init var unpublished);")
            .AppendLine("}");
    }

    private static void AppendUnaryHelpers(StringBuilder source)
    {
        foreach (var function in new[] { "TryNegateInt64", "TryAbsoluteInt64" })
        {
            source.Append("bool Check").Append(function)
                .AppendLine("(int64 value, int64 expected, bool shouldSucceed) {")
                .AppendLine("    if (shouldSucceed) {")
                .Append("        if (!").Append(function)
                .AppendLine("(value, init var actual)) { return false; }")
                .AppendLine("        return actual == expected;")
                .AppendLine("    }")
                .Append("    return !").Append(function)
                .AppendLine("(value, init var unpublished);")
                .AppendLine("}");
        }
        source.AppendLine("""
            bool CheckMagnitudeInt64(int64 value, uint64 expected) {
                return MagnitudeInt64(value) == expected;
            }
            """);
    }

    private static void AppendCheck(StringBuilder source, List<string> checks, ref int failureCode,
        string function, string first, string second, string expected, bool succeeds, string description)
    {
        source.Append("if (!").Append(function).Append('(').Append(first).Append(", ")
            .Append(second).Append(", ").Append(expected).Append(", ")
            .Append(succeeds ? "true" : "false").Append(")) { return ")
            .Append(failureCode.ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
        checks.Add(description);
        failureCode++;
    }

    private static void AppendCheck(StringBuilder source, List<string> checks, ref int failureCode,
        string function, string input, string expected, bool succeeds, string description)
    {
        source.Append("if (!").Append(function).Append('(').Append(input).Append(", ")
            .Append(expected).Append(", ").Append(succeeds ? "true" : "false")
            .Append(")) { return ")
            .Append(failureCode.ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
        checks.Add(description);
        failureCode++;
    }

    private static void AppendCheck(StringBuilder source, List<string> checks, ref int failureCode,
        string function, string input, string expected, string description)
    {
        source.Append("if (!").Append(function).Append('(').Append(input).Append(", ")
            .Append(expected).Append(")) { return ")
            .Append(failureCode.ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
        checks.Add(description);
        failureCode++;
    }

    private static bool TryExpectedBinary(string function, BigInteger left, BigInteger right,
        out BigInteger result)
    {
        if (function == "TryDivideInt64" && left == long.MinValue && right == -BigInteger.One)
        {
            result = BigInteger.Zero;
            return false;
        }
        if ((function is "TryDivideInt64" or "TryRemainderInt64"
            or "TryDivideUInt64" or "TryRemainderUInt64") && right.IsZero)
        {
            result = BigInteger.Zero;
            return false;
        }

        result = function switch {
            "TryAddInt64" or "TryAddUInt64" => left + right,
            "TrySubtractInt64" or "TrySubtractUInt64" => left - right,
            "TryMultiplyInt64" or "TryMultiplyUInt64" => left * right,
            "TryDivideInt64" or "TryDivideUInt64" => left / right,
            "TryRemainderInt64" or "TryRemainderUInt64" => left % right,
            _ => throw new ArgumentOutOfRangeException(nameof(function)),
        };
        var minimum = function.EndsWith("UInt64", StringComparison.Ordinal)
            ? BigInteger.Zero : new BigInteger(long.MinValue);
        var maximum = function.EndsWith("UInt64", StringComparison.Ordinal)
            ? new BigInteger(ulong.MaxValue) : new BigInteger(long.MaxValue);
        return result >= minimum && result <= maximum;
    }

    private static string Int64Literal(BigInteger value) =>
        value == long.MinValue ? "int64.MinValue"
            : value == long.MaxValue ? "int64.MaxValue"
                : value.ToString(CultureInfo.InvariantCulture);

    private static string UInt64Literal(BigInteger value) =>
        value.IsZero ? "0"
            : value == ulong.MaxValue ? "uint64.MaxValue"
                : value.ToString(CultureInfo.InvariantCulture);

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record BinaryOperation(string Name, string Type, bool IsUnsigned);
}
