// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
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
            ? AssertCheckedExecutionAsync(Source, 100)
            : AssertFailFastExecutionAsync(Source, 100);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ShortCircuitAndRecursiveConstantCallsPreserveScalarSemanticsAsync(bool checkedFailures)
    {
        const string Source = """
            [ConstEval] int32 Factorial(int32 value) {
                if (value <= 1) { return 1; }
                return value * Factorial(value - 1);
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
            ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RequiredForLoopAndChainedConstantsExecuteAsync(bool checkedFailures)
    {
        const string Source = """
            [ConstEval] int32 Triangle(int32 count) {
                mut int32 result = 0;
                for (mut int32 index = 1; index <= count; index = index + 1) {
                    result = result + index;
                }
                return result;
            }
            int32 Main() {
                const int32 first = Triangle(6);
                const int32 second = Triangle(first / 7);
                return first + second + Triangle(6) - 6;
            }
            """;
        return checkedFailures
            ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
}
