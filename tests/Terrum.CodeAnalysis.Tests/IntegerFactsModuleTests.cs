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

public sealed class IntegerFactsModuleTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task IntegerFactsPersistedConsumerMatchesBoundaryMasks(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentPath = SelfHostPaths.ResolveFile(root, "IntegerFacts.terrum");
        var componentText = await File.ReadAllTextAsync(componentPath).ConfigureAwait(true);
        var componentTree = SyntaxTree.Parse(componentText, componentPath);
        Assert.That(componentTree.HasErrors, Is.False, string.Join(Environment.NewLine, componentTree.Diagnostics));

        var producerSource = componentText + """

            public int32 IntegerFactsInt32Mask(int32 left, int32 right) {
                mut int32 mask = 0;
                if (CanAddInt32(left, right)) { mask = mask + 1; }
                if (CanSubtractInt32(left, right)) { mask = mask + 2; }
                if (CanMultiplyInt32(left, right)) { mask = mask + 4; }
                if (CanDivideInt32(left, right)) { mask = mask + 8; }
                if (CanRemainderInt32(left, right)) { mask = mask + 16; }
                if (CanNegateInt32(left)) { mask = mask + 32; }
                if (CanAbsoluteInt32(left)) { mask = mask + 64; }
                return mask;
            }
            public int32 IntegerFactsUInt32Mask(uint32 left, uint32 right) {
                mut int32 mask = 0;
                if (CanAddUInt32(left, right)) { mask = mask + 1; }
                if (CanSubtractUInt32(left, right)) { mask = mask + 2; }
                if (CanMultiplyUInt32(left, right)) { mask = mask + 4; }
                if (CanDivideUInt32(left, right)) { mask = mask + 8; }
                if (CanRemainderUInt32(left, right)) { mask = mask + 16; }
                return mask;
            }
            public int32 IntegerFactsInt64Mask(int64 left, int64 right) {
                mut int32 mask = 0;
                if (CanAddInt64(left, right)) { mask = mask + 1; }
                if (CanSubtractInt64(left, right)) { mask = mask + 2; }
                if (CanMultiplyInt64(left, right)) { mask = mask + 4; }
                if (CanDivideInt64(left, right)) { mask = mask + 8; }
                if (CanRemainderInt64(left, right)) { mask = mask + 16; }
                if (CanNegateInt64(left)) { mask = mask + 32; }
                if (CanAbsoluteInt64(left)) { mask = mask + 64; }
                return mask;
            }
            public int32 IntegerFactsUInt64Mask(uint64 left, uint64 right) {
                mut int32 mask = 0;
                if (CanAddUInt64(left, right)) { mask = mask + 1; }
                if (CanSubtractUInt64(left, right)) { mask = mask + 2; }
                if (CanMultiplyUInt64(left, right)) { mask = mask + 4; }
                if (CanDivideUInt64(left, right)) { mask = mask + 8; }
                if (CanRemainderUInt64(left, right)) { mask = mask + 16; }
                return mask;
            }
            int32 Main() { return 0; }
            """;
        var producerTree = SyntaxTree.Parse(producerSource, componentPath);
        Assert.That(producerTree.HasErrors, Is.False, string.Join(Environment.NewLine, producerTree.Diagnostics));
        var producerCompilation = Compilation.Create(producerTree, policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));

        var exports = new[] {
            "IntegerFactsInt32Mask",
            "IntegerFactsUInt32Mask",
            "IntegerFactsInt64Mask",
            "IntegerFactsUInt64Mask",
        };
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = producerCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            exports);
        var assertions = new StringBuilder();
        var failureCases = new List<string>();
        AddPairwiseChecks(assertions, failureCases, "IntegerFactsInt32Mask", "int32",
            [int.MinValue, int.MinValue + 1, -46341, -1, 0, 1, 46340, 46341, int.MaxValue - 1, int.MaxValue],
            Int32Literal, Int32ExpectedMask);
        AddPairwiseChecks(assertions, failureCases, "IntegerFactsUInt32Mask", "uint32",
            [0U, 1U, 2U, 65535U, 65536U, 2147483647U, 2147483648U, uint.MaxValue - 1, uint.MaxValue],
            static value => value.ToString(CultureInfo.InvariantCulture), UInt32ExpectedMask);
        AddPairwiseChecks(assertions, failureCases, "IntegerFactsInt64Mask", "int64",
            [long.MinValue, long.MinValue + 1, -3037000500L, -1L, 0L, 1L,
                3037000499L, 3037000500L, long.MaxValue - 1, long.MaxValue],
            Int64Literal, Int64ExpectedMask);
        AddPairwiseChecks(assertions, failureCases, "IntegerFactsUInt64Mask", "uint64",
            [0UL, 1UL, 2UL, 4294967295UL, 4294967296UL, 9223372036854775807UL,
                9223372036854775808UL, ulong.MaxValue - 1, ulong.MaxValue],
            UInt64Literal, UInt64ExpectedMask);

        var consumerSource = "int32 Main() {\n" + assertions + "\nreturn 0;\n}";
        var consumerTree = SyntaxTree.Parse(consumerSource, "integer-facts-consumer.terrum");
        Assert.That(consumerTree.HasErrors, Is.False, string.Join(Environment.NewLine, consumerTree.Diagnostics));
        var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        var actual = NativeJit.Execute(linked, (NativeRuntimePolicy)policy);
        Assert.That(actual == 0, Is.True, actual > 0 && actual <= failureCases.Count
                ? $"Boundary case {actual} failed: {failureCases[actual - 1]}."
                : $"Expected no failed boundary checks, got consumer result {actual}.");

        Assert.That(Int32ExpectedMask(int.MinValue, -1) & 8, Is.EqualTo(0));
        Assert.That(Int32ExpectedMask(int.MinValue, -1) & 16, Is.Not.EqualTo(0));
        Assert.That(Int32ExpectedMask(int.MinValue, -1) & 32, Is.EqualTo(0));
        Assert.That(Int32ExpectedMask(int.MinValue, -1) & 64, Is.EqualTo(0));
        Assert.That(Int64ExpectedMask(long.MinValue, -1) & 8, Is.EqualTo(0));
        Assert.That(Int64ExpectedMask(long.MinValue, -1) & 16, Is.Not.EqualTo(0));
        Assert.That(Int64ExpectedMask(long.MinValue, -1) & 32, Is.EqualTo(0));
        Assert.That(Int64ExpectedMask(long.MinValue, -1) & 64, Is.EqualTo(0));
    }

    private static void AddPairwiseChecks<T>(StringBuilder assertions, List<string> failureCases,
        string functionName, string typeName, T[] values, Func<T, string> format, Func<T, T, int> expectedMask)
    {
        foreach (var left in values)
        {
            foreach (var right in values)
            {
                var expected = expectedMask(left, right);
                var caseNumber = failureCases.Count + 1;
                assertions.Append("if (").Append(functionName).Append('(').Append(format(left)).Append(", ")
                    .Append(format(right)).Append(") != ").Append(expected.ToString(CultureInfo.InvariantCulture))
                    .Append(") { return ").Append(caseNumber.ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
                failureCases.Add($"{typeName} ({format(left)}, {format(right)}) expected mask {expected}");
            }
        }
    }

    private static int Int32ExpectedMask(int left, int right) =>
        SignedExpectedMask(left, right, int.MinValue, int.MaxValue);

    private static int Int64ExpectedMask(long left, long right) =>
        SignedExpectedMask(left, right, long.MinValue, long.MaxValue);

    private static int SignedExpectedMask<T>(T left, T right, T minimum, T maximum)
        where T : struct, IComparable<T>, IConvertible
    {
        var leftValue = new BigInteger(left.ToDecimal(CultureInfo.InvariantCulture));
        var rightValue = new BigInteger(right.ToDecimal(CultureInfo.InvariantCulture));
        var minimumValue = new BigInteger(minimum.ToDecimal(CultureInfo.InvariantCulture));
        var maximumValue = new BigInteger(maximum.ToDecimal(CultureInfo.InvariantCulture));
        var mask = 0;
        if (leftValue + rightValue >= minimumValue && leftValue + rightValue <= maximumValue)
        { mask += 1; }
        if (leftValue - rightValue >= minimumValue && leftValue - rightValue <= maximumValue)
        { mask += 2; }
        if (leftValue * rightValue >= minimumValue && leftValue * rightValue <= maximumValue)
        { mask += 4; }
        if (!rightValue.IsZero && !(leftValue == minimumValue && rightValue == -BigInteger.One))
        { mask += 8; }
        if (!rightValue.IsZero)
        { mask += 16; }
        if (leftValue != minimumValue)
        { mask += 32; }
        if (leftValue != minimumValue)
        { mask += 64; }
        return mask;
    }

    private static int UInt32ExpectedMask(uint left, uint right) => UnsignedExpectedMask(left, right, uint.MaxValue);

    private static int UInt64ExpectedMask(ulong left, ulong right) =>
        UnsignedExpectedMask(left, right, ulong.MaxValue);

    private static int UnsignedExpectedMask<T>(T left, T right, T maximum)
        where T : struct, IConvertible
    {
        var leftValue = new BigInteger(left.ToDecimal(CultureInfo.InvariantCulture));
        var rightValue = new BigInteger(right.ToDecimal(CultureInfo.InvariantCulture));
        var maximumValue = new BigInteger(maximum.ToDecimal(CultureInfo.InvariantCulture));
        var mask = 0;
        if (leftValue + rightValue <= maximumValue)
        { mask += 1; }
        if (leftValue >= rightValue)
        { mask += 2; }
        if (leftValue * rightValue <= maximumValue)
        { mask += 4; }
        if (!rightValue.IsZero)
        { mask += 8; }
        if (!rightValue.IsZero)
        { mask += 16; }
        return mask;
    }

    private static string Int32Literal(int value) =>
        value == int.MinValue ? "int32.MinValue" : value.ToString(CultureInfo.InvariantCulture);

    private static string Int64Literal(long value) =>
        value == long.MinValue ? "int64.MinValue" : value.ToString(CultureInfo.InvariantCulture);

    private static string UInt64Literal(ulong value) =>
        value == ulong.MaxValue ? "uint64.MaxValue" : value.ToString(CultureInfo.InvariantCulture);

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
