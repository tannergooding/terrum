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

namespace Terrum.CodeAnalysis.Tests;

public sealed class WideIntegerFactsModuleTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CheckedWideArithmeticPersistsAndMatchesBigIntegerBoundariesAsync(
        BoundRuntimePolicy policy)
    {
        var path = SelfHostPaths.ResolveFile(RepositoryRoot(), "WideIntegerFacts.terrum");
        var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));

        var producerCompilation = Compilation.CreateLibrary(tree, policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
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
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = producerCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
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
        Assert.That(consumerTree.HasErrors, Is.False, string.Join(Environment.NewLine, consumerTree.Diagnostics));
        var consumerCompilation = Compilation.Create(consumerTree, reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);
        var actual = NativeJit.Execute(ModuleCompiler.Link(consumer, producer), (NativeRuntimePolicy)policy);
        Assert.That(actual == 0, Is.True, actual > 0 && actual <= checks.Count
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
            Assert.That(global::System.Linq.Enumerable.Any(invalidCompilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
            Assert.That(invalidCompilation.Program, Is.Null);
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task WideAccumulatorsPersistAndMatchBigIntegerBoundariesAsync(
        BoundRuntimePolicy policy)
    {
        var path = SelfHostPaths.ResolveFile(RepositoryRoot(), "WideIntegerFacts.terrum");
        var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));

        var producerCompilation = Compilation.CreateLibrary(tree, policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = producerCompilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(),
            [
                "Terrum.CodeAnalysis.Numerics64.TryAccumulateDecimal",
                "Terrum.CodeAnalysis.Numerics64.TryAccumulateBase",
            ]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerSource = new StringBuilder("using Terrum.CodeAnalysis.Numerics64;\nint32 Main() {\n");
        var helpers = new StringBuilder();
        var checks = new List<string>();
        var failureCode = 1;
        ulong[] maxima = [0, 1, (ulong)long.MaxValue, ulong.MaxValue];

        AppendAccumulatorHelpers(helpers);
        foreach (var maximum in maxima)
        {
            for (uint digit = 0; digit <= 10; digit++)
            {
                foreach (var value in AccumulationInputs(maximum, digit, 10))
                {
                    var succeeds = TryExpectedAccumulateDecimal(value, digit, maximum, out var result);
                    AppendAccumulationCheck(consumerSource, checks, ref failureCode,
                        "CheckTryAccumulateDecimal",
                        $"{UInt64Literal(value)}, {digit}, {UInt64Literal(maximum)}",
                        UInt64Literal(succeeds ? result : BigInteger.Zero), succeeds,
                        $"TryAccumulateDecimal({value}, {digit}, {maximum})");
                }
            }
        }

        for (uint radix = 2; radix <= 36; radix++)
        {
            foreach (var maximum in maxima)
            {
                uint[] digits = radix == 2 ? [0, 1, radix] : [0, 1, radix - 1, radix];
                foreach (var digit in digits)
                {
                    foreach (var value in AccumulationInputs(maximum, digit, radix))
                    {
                        var succeeds = TryExpectedAccumulateBase(value, digit, radix, maximum, out var result);
                        AppendAccumulationCheck(consumerSource, checks, ref failureCode,
                            "CheckTryAccumulateBase",
                            $"{UInt64Literal(value)}, {digit}, {radix}, {UInt64Literal(maximum)}",
                            UInt64Literal(succeeds ? result : BigInteger.Zero), succeeds,
                            $"TryAccumulateBase({value}, {digit}, {radix}, {maximum})");
                    }
                }
            }
        }

        foreach (var radix in new uint[] { 0, 1, 37 })
        {
            foreach (var maximum in maxima)
            {
                var values = new List<BigInteger> { BigInteger.Zero };
                if (maximum != 0)
                {
                    values.Add(maximum);
                }
                foreach (var digit in new uint[] { 0, 1 })
                {
                    foreach (var value in values)
                    {
                        AppendAccumulationCheck(consumerSource, checks, ref failureCode,
                            "CheckTryAccumulateBase",
                            $"{UInt64Literal(value)}, {digit}, {radix}, {UInt64Literal(maximum)}",
                            "0", false,
                            $"TryAccumulateBase({value}, {digit}, {radix}, {maximum})");
                    }
                }
            }
        }

        Assert.That(checks.Count, Is.EqualTo(2442));
        consumerSource.AppendLine("return 0;");
        consumerSource.AppendLine("}");
        consumerSource.Append(helpers);
        var consumerTree = SyntaxTree.Parse(consumerSource.ToString(), "wide-accumulation-consumer.terrum");
        Assert.That(consumerTree.HasErrors, Is.False, string.Join(Environment.NewLine, consumerTree.Diagnostics));
        var consumerCompilation = Compilation.Create(consumerTree, reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = consumerCompilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), producer);
        var actual = NativeJit.Execute(ModuleCompiler.Link(consumer, producer), (NativeRuntimePolicy)policy);
        Assert.That(actual == 0, Is.True, actual > 0 && actual <= checks.Count
                ? $"Wide accumulation boundary {actual} failed: {checks[actual - 1]}."
                : $"Expected all wide accumulation checks to pass, got consumer result {actual}.");

        foreach (var (function, arguments) in new[] {
            ("TryAccumulateDecimal", "0, 10, uint64.MaxValue"),
            ("TryAccumulateBase", "0, 1, 1, uint64.MaxValue"),
        })
        {
            var invalidConsumer = SyntaxTree.Parse(
                "using Terrum.CodeAnalysis.Numerics64;\n"
                + "int32 Main() {\n"
                + $"    if (!{function}({arguments}, init var result)) {{ return int32(result); }}\n"
                + "    return 0;\n"
                + "}",
                "wide-accumulation-unpublished-" + function + ".terrum");
            var invalidCompilation = Compilation.Create(invalidConsumer, reference, policy);
            Assert.That(global::System.Linq.Enumerable.Any(invalidCompilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
            Assert.That(invalidCompilation.Program, Is.Null);
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

    private static void AppendAccumulatorHelpers(StringBuilder source)
    {
        source.AppendLine("""
            bool CheckTryAccumulateDecimal(
                uint64 value, uint32 digit, uint64 maximum, uint64 expected, bool shouldSucceed) {
                if (shouldSucceed) {
                    if (!TryAccumulateDecimal(value, digit, maximum, init var actual)) { return false; }
                    return actual == expected;
                }
                return !TryAccumulateDecimal(value, digit, maximum, init var unpublished);
            }
            bool CheckTryAccumulateBase(
                uint64 value, uint32 digit, uint32 radix, uint64 maximum,
                uint64 expected, bool shouldSucceed) {
                if (shouldSucceed) {
                    if (!TryAccumulateBase(value, digit, radix, maximum, init var actual)) { return false; }
                    return actual == expected;
                }
                return !TryAccumulateBase(value, digit, radix, maximum, init var unpublished);
            }
            """);
    }

    private static void AppendAccumulationCheck(StringBuilder source, List<string> checks,
        ref int failureCode, string function, string arguments, string expected, bool succeeds,
        string description)
    {
        source.Append("if (!").Append(function).Append('(').Append(arguments).Append(", ")
            .Append(expected).Append(", ").Append(succeeds ? "true" : "false")
            .Append(")) { return ")
            .Append(failureCode.ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
        checks.Add(description);
        failureCode++;
    }

    private static List<BigInteger> AccumulationInputs(ulong maximum, uint digit, uint radix)
    {
        var values = new List<BigInteger>();
        var seen = new HashSet<BigInteger>();
        var upperBound = new BigInteger(ulong.MaxValue);
        var maximumValue = new BigInteger(maximum);

        void Add(BigInteger value)
        {
            if (value >= BigInteger.Zero && value <= upperBound && seen.Add(value))
            {
                values.Add(value);
            }
        }

        Add(BigInteger.Zero);
        Add(maximumValue);
        Add(maximumValue + 1);
        if (maximumValue >= digit)
        {
            var threshold = (maximumValue - digit) / radix;
            Add(threshold - 1);
            Add(threshold);
            Add(threshold + 1);
        }
        return values;
    }

    private static bool TryExpectedAccumulateDecimal(BigInteger value, uint digit, ulong maximum,
        out BigInteger result)
    {
        result = value * 10 + digit;
        return digit <= 9 && result <= maximum;
    }

    private static bool TryExpectedAccumulateBase(BigInteger value, uint digit, uint radix, ulong maximum,
        out BigInteger result)
    {
        result = value * radix + digit;
        return radix >= 2 && radix <= 36 && digit < radix && result <= maximum;
    }

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

    private static string UInt64Literal(ulong value) =>
        value == ulong.MaxValue ? "uint64.MaxValue"
            : value.ToString(CultureInfo.InvariantCulture);

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record BinaryOperation(string Name, string Type, bool IsUnsigned);
}
