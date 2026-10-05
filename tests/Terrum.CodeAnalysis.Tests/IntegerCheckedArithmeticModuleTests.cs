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

public sealed class IntegerCheckedArithmeticModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task Checked32BitArithmeticPersistsAndMatchesBigIntegerBoundariesAsync(
        BoundRuntimePolicy policy)
    {
        var path = Path.Combine(RepositoryRoot(), "self-host", "IntegerCheckedArithmetic.terrum");
        var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));

        var producerCompilation = Compilation.CreateLibrary(tree, policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var exports = new[] {
            "TryAddInt32",
            "TrySubtractInt32",
            "TryMultiplyInt32",
            "TryDivideInt32",
            "TryRemainderInt32",
            "TryNegateInt32",
            "TryAbsoluteInt32",
            "MagnitudeInt32",
            "TryAddUInt32",
            "TrySubtractUInt32",
            "TryMultiplyUInt32",
            "TryDivideUInt32",
            "TryRemainderUInt32",
        };
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            Array.ConvertAll(exports, static name => "Terrum.CodeAnalysis.Numerics64." + name));
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerSource = new StringBuilder("using Terrum.CodeAnalysis.Numerics64;\nint32 Main() {\n");
        var helpers = new StringBuilder();
        var checks = new List<string>();
        var failureCode = 1;
        int[] signedValues = [
            int.MinValue, int.MinValue + 1, -46341, -1, 0, 1,
            46340, 46341, int.MaxValue - 1, int.MaxValue,
        ];
        uint[] unsignedValues = [
            0, 1, 2, 65535, 65536, 2147483647, 2147483648,
            uint.MaxValue - 1, uint.MaxValue,
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
                            "Check" + operation.Name, UInt32Literal(left), UInt32Literal(right),
                            UInt32Literal(succeeds ? result : BigInteger.Zero), succeeds,
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
                            "Check" + operation.Name, Int32Literal(left), Int32Literal(right),
                            Int32Literal(succeeds ? result : BigInteger.Zero), succeeds,
                            $"{operation.Name}({left}, {right})");
                    }
                }
            }
        }

        AppendUnaryHelpers(helpers);
        foreach (var value in signedValues)
        {
            foreach (var function in new[] { "TryNegateInt32", "TryAbsoluteInt32" })
            {
                var succeeds = value != int.MinValue;
                var result = function == "TryNegateInt32" ? -new BigInteger(value) : BigInteger.Abs(value);
                AppendCheck(consumerSource, checks, ref failureCode, "Check" + function,
                    Int32Literal(value), Int32Literal(succeeds ? result : BigInteger.Zero), succeeds,
                    $"{function}({value})");
            }

            AppendCheck(consumerSource, checks, ref failureCode, "CheckMagnitudeInt32",
                Int32Literal(value), UInt32Literal(BigInteger.Abs(value)),
                $"MagnitudeInt32({value})");
        }

        consumerSource.AppendLine("return 0;");
        consumerSource.AppendLine("}");
        consumerSource.Append(helpers);
        var consumerTree = SyntaxTree.Parse(consumerSource.ToString(), "integer-checked-arithmetic-consumer.terrum");
        Assert.False(consumerTree.HasErrors, string.Join(Environment.NewLine, consumerTree.Diagnostics));
        var consumerCompilation = Compilation.Create(consumerTree, reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var actual = NativeJit.Execute(ModuleCompiler.Link(consumer, producer), (NativeRuntimePolicy)policy);
        Assert.True(actual == 0,
            actual > 0 && actual <= checks.Count
                ? $"32-bit arithmetic boundary {actual} failed: {checks[actual - 1]}."
                : $"Expected all 32-bit arithmetic checks to pass, got consumer result {actual}.");

        foreach (var (function, arguments) in RejectedCalls())
        {
            var invalidConsumer = SyntaxTree.Parse(
                "using Terrum.CodeAnalysis.Numerics64;\n"
                + "int32 Main() {\n"
                + $"    if (!{function}({arguments}, init var result)) {{ return int32(result); }}\n"
                + "    return 0;\n"
                + "}",
                "integer-checked-arithmetic-unpublished-" + function + ".terrum");
            var invalidCompilation = Compilation.Create(invalidConsumer, reference, policy);
            Assert.Contains(invalidCompilation.Diagnostics,
                static diagnostic => diagnostic.Code == "TRM3016");
            Assert.Null(invalidCompilation.Program);
        }
    }

    private static (string Function, string Arguments)[] RejectedCalls() =>
    [
        ("TryAddInt32", "int32.MaxValue, 1"),
        ("TrySubtractInt32", "int32.MinValue, 1"),
        ("TryMultiplyInt32", "int32.MinValue, -1"),
        ("TryDivideInt32", "int32.MinValue, -1"),
        ("TryRemainderInt32", "1, 0"),
        ("TryNegateInt32", "int32.MinValue"),
        ("TryAbsoluteInt32", "int32.MinValue"),
        ("TryAddUInt32", "uint32.MaxValue, 1"),
        ("TrySubtractUInt32", "0, 1"),
        ("TryMultiplyUInt32", "uint32.MaxValue, 2"),
        ("TryDivideUInt32", "1, 0"),
        ("TryRemainderUInt32", "1, 0"),
    ];

    private static List<BinaryOperation> BinaryOperations() =>
    [
        new("TryAddInt32", "int32", false),
        new("TrySubtractInt32", "int32", false),
        new("TryMultiplyInt32", "int32", false),
        new("TryDivideInt32", "int32", false),
        new("TryRemainderInt32", "int32", false),
        new("TryAddUInt32", "uint32", true),
        new("TrySubtractUInt32", "uint32", true),
        new("TryMultiplyUInt32", "uint32", true),
        new("TryDivideUInt32", "uint32", true),
        new("TryRemainderUInt32", "uint32", true),
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
        foreach (var function in new[] { "TryNegateInt32", "TryAbsoluteInt32" })
        {
            source.Append("bool Check").Append(function)
                .AppendLine("(int32 value, int32 expected, bool shouldSucceed) {")
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
            bool CheckMagnitudeInt32(int32 value, uint32 expected) {
                return MagnitudeInt32(value) == expected;
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
        if (function == "TryDivideInt32" && left == int.MinValue && right == -BigInteger.One)
        {
            result = BigInteger.Zero;
            return false;
        }
        if ((function is "TryDivideInt32" or "TryRemainderInt32"
            or "TryDivideUInt32" or "TryRemainderUInt32") && right.IsZero)
        {
            result = BigInteger.Zero;
            return false;
        }

        result = function switch {
            "TryAddInt32" or "TryAddUInt32" => left + right,
            "TrySubtractInt32" or "TrySubtractUInt32" => left - right,
            "TryMultiplyInt32" or "TryMultiplyUInt32" => left * right,
            "TryDivideInt32" or "TryDivideUInt32" => left / right,
            "TryRemainderInt32" or "TryRemainderUInt32" => left % right,
            _ => throw new ArgumentOutOfRangeException(nameof(function)),
        };
        var minimum = function.EndsWith("UInt32", StringComparison.Ordinal)
            ? BigInteger.Zero : new BigInteger(int.MinValue);
        var maximum = function.EndsWith("UInt32", StringComparison.Ordinal)
            ? new BigInteger(uint.MaxValue) : new BigInteger(int.MaxValue);
        return result >= minimum && result <= maximum;
    }

    private static string Int32Literal(BigInteger value) =>
        value == int.MinValue ? "int32.MinValue"
            : value == int.MaxValue ? "int32.MaxValue"
                : value.ToString(CultureInfo.InvariantCulture);

    private static string UInt32Literal(BigInteger value) =>
        value.IsZero ? "0"
            : value == uint.MaxValue ? "uint32.MaxValue"
                : value.ToString(CultureInfo.InvariantCulture);

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record BinaryOperation(string Name, string Type, bool IsUnsigned);
}
