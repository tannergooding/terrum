// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

internal sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task RequiredConstantAndRuntimeCallExecuteIdenticallyAsync(bool checkedFailures)
    {
        const string Source = """
            int32 Main() {
                const int32 answer = Choose(1, 41);
                return answer + Choose(0, 99);
            }
            [ConstEval] int32 Choose(int32 value, int32 fallback) {
                if (value != 0) { return value; }
                return fallback;
            }
            """;
        return checkedFailures
                ? AssertCheckedExecutionAsync(Source, 100,
                    ConstEvalSelection(checkedFailures))
                : AssertFailFastExecutionAsync(Source, 100,
                    ConstEvalSelection(checkedFailures));
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task ShortCircuitAndRecursiveConstantCallsPreserveScalarSemanticsAsync(bool checkedFailures)
    {
        const string Source = """
            [ConstEval] int32 Factorial(int32 value) {
                if (value <= 1) { return 1; }
                if (value > 4) { return 0; }
                int32 previous = Factorial(value - 1);
                if (previous == 0) { return 0; }
                if (value == 2) { return 2; }
                if (value == 3) { return 6; }
                return 24;
            }
            [ConstEval] bool SkipFailure() {
                return false && (int32(1) / int32(0) == int32(0));
            }
            int32 Main() {
                const int32 result = Factorial(4);
                const bool skipped = SkipFailure();
                if (skipped) { return 0; }
                return result + Factorial(3) + 12;
            }
            """;
        return checkedFailures
                ? AssertCheckedExecutionAsync(Source, 42,
                    ConstEvalSelection(checkedFailures))
                : AssertFailFastExecutionAsync(Source, 42,
                    ConstEvalSelection(checkedFailures));
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task RequiredForLoopAndChainedConstantsExecuteAsync(bool checkedFailures)
    {
        const string Source = """
            [ConstEval] int32 Counted(int32 count) {
                if (count < 0) { return 0; }
                if (count > 6) { return 0; }
                mut bool entered = false;
                for (mut int32 index = 0; index < count; index = index + 1) {
                    entered = true;
                }
                return entered ? count : 0;
            }
            int32 Main() {
                const int32 first = Counted(6);
                const int32 second = Counted(first / 2);
                return first + second + Counted(6) + 27;
            }
            """;
        return checkedFailures
                ? AssertCheckedExecutionAsync(Source, 42,
                    ConstEvalSelection(checkedFailures))
                : AssertFailFastExecutionAsync(Source, 42,
                    ConstEvalSelection(checkedFailures));
    }
}
