// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeTests
{
    [Test]
    public async Task IntegerFactsPredicatesExecuteNativelyAndMatchBigIntegerBoundariesAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var componentPath = SelfHostPaths.ResolveFile(root, "IntegerFacts.terrum");
        var componentText = await File.ReadAllTextAsync(componentPath).ConfigureAwait(true);
        var assertions = new StringBuilder();
        var failureCases = new List<string>();
        AddPairwiseChecks(assertions, failureCases, "IntegerFactsInt32Mask", "int32",
            [int.MinValue, int.MinValue + 1, -46341, -1, 0, 1, 46340, 46341, int.MaxValue - 1, int.MaxValue],
            Int32Literal, Int32ExpectedMask);
        AddPairwiseChecks(assertions, failureCases, "IntegerFactsUInt32Mask", "uint32",
            [0U, 1U, 2U, 65535U, 65536U, 2147483647U, 2147483648U, uint.MaxValue - 1, uint.MaxValue],
            static value => value.ToString(CultureInfo.InvariantCulture), UInt32ExpectedMask);
        AddPairwiseChecks(assertions, failureCases, "IntegerFactsInt64Mask", "int64",
            [long.MinValue, long.MinValue + 1, -3037000500L, -1L, 0L, 1L, 3037000499L,
                3037000500L, long.MaxValue - 1, long.MaxValue],
            Int64Literal, Int64ExpectedMask);
        AddPairwiseChecks(assertions, failureCases, "IntegerFactsUInt64Mask", "uint64",
            [0UL, 1UL, 2UL, 4294967295UL, 4294967296UL, 9223372036854775807UL,
                9223372036854775808UL, ulong.MaxValue - 1, ulong.MaxValue],
            UInt64Literal, UInt64ExpectedMask);

        var source = new StringBuilder(componentText)
            .AppendLine()
            .Append("""

                int32 IntegerFactsInt32Mask(int32 left, int32 right) {
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
                int32 IntegerFactsUInt32Mask(uint32 left, uint32 right) {
                    mut int32 mask = 0;
                    if (CanAddUInt32(left, right)) { mask = mask + 1; }
                    if (CanSubtractUInt32(left, right)) { mask = mask + 2; }
                    if (CanMultiplyUInt32(left, right)) { mask = mask + 4; }
                    if (CanDivideUInt32(left, right)) { mask = mask + 8; }
                    if (CanRemainderUInt32(left, right)) { mask = mask + 16; }
                    return mask;
                }
                int32 IntegerFactsInt64Mask(int64 left, int64 right) {
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
                int32 IntegerFactsUInt64Mask(uint64 left, uint64 right) {
                    mut int32 mask = 0;
                    if (CanAddUInt64(left, right)) { mask = mask + 1; }
                    if (CanSubtractUInt64(left, right)) { mask = mask + 2; }
                    if (CanMultiplyUInt64(left, right)) { mask = mask + 4; }
                    if (CanDivideUInt64(left, right)) { mask = mask + 8; }
                    if (CanRemainderUInt64(left, right)) { mask = mask + 16; }
                    return mask;
                }
                int32 Main() {
                """)
            .Append(assertions)
            .AppendLine("return 0; }");

        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var actual = await ExecuteAsync(Emit(source.ToString(), policy)).ConfigureAwait(true);
            Assert.That(actual == 0, Is.True, actual > 0 && actual <= failureCases.Count
                    ? $"{policy} IntegerFacts boundary case {actual} failed: {failureCases[actual - 1]}."
                    : $"Expected all {policy} IntegerFacts native checks to pass, got exit code {actual}.");
        }
    }

    private static void AddPairwiseChecks<T>(
        StringBuilder assertions,
        List<string> failureCases,
        string functionName,
        string typeName,
        T[] values,
        Func<T, string> format,
        Func<T, T, int> expectedMask)
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
        SignedExpectedMask(new BigInteger(left), new BigInteger(right), int.MinValue, int.MaxValue);

    private static int Int64ExpectedMask(long left, long right) =>
        SignedExpectedMask(new BigInteger(left), new BigInteger(right), long.MinValue, long.MaxValue);

    private static int SignedExpectedMask(BigInteger left, BigInteger right, BigInteger minimum, BigInteger maximum)
    {
        var mask = 0;
        if (IsInRange(left + right, minimum, maximum))
        { mask += 1; }
        if (IsInRange(left - right, minimum, maximum))
        { mask += 2; }
        if (IsInRange(left * right, minimum, maximum))
        { mask += 4; }
        if (!right.IsZero && !(left == minimum && right == -BigInteger.One))
        { mask += 8; }
        if (!right.IsZero)
        { mask += 16; }
        if (left != minimum)
        { mask += 32; }
        if (left != minimum)
        { mask += 64; }
        return mask;
    }

    private static int UInt32ExpectedMask(uint left, uint right) =>
        UnsignedExpectedMask(new BigInteger(left), new BigInteger(right), uint.MaxValue);

    private static int UInt64ExpectedMask(ulong left, ulong right) =>
        UnsignedExpectedMask(new BigInteger(left), new BigInteger(right), ulong.MaxValue);

    private static int UnsignedExpectedMask(BigInteger left, BigInteger right, BigInteger maximum)
    {
        var mask = 0;
        if (left + right <= maximum)
        { mask += 1; }
        if (left >= right)
        { mask += 2; }
        if (left * right <= maximum)
        { mask += 4; }
        if (!right.IsZero)
        { mask += 8; }
        if (!right.IsZero)
        { mask += 16; }
        return mask;
    }

    private static bool IsInRange(BigInteger value, BigInteger minimum, BigInteger maximum) =>
        value >= minimum && value <= maximum;

    private static string Int32Literal(int value) =>
        value == int.MinValue ? "int32.MinValue" : value.ToString(CultureInfo.InvariantCulture);

    private static string Int64Literal(long value) =>
        value == long.MinValue ? "int64.MinValue" : value.ToString(CultureInfo.InvariantCulture);

    private static string UInt64Literal(ulong value) =>
        value == ulong.MaxValue ? "uint64.MaxValue" : value.ToString(CultureInfo.InvariantCulture);

    private static byte[] Emit(string source, BoundRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeEmitter.Emit(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), (NativeRuntimePolicy)policy);
    }
}
