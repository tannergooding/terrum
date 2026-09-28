// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ExpressionRequirementPreservesRuntimeCallsAndConditionalOrderAsync(bool checkedFailures)
    {
        const string Source = """
            [ConstEval] int32 Twice(int32 value) { return value + value; }
            [ConstEval] bool Truth() { return true; }
            int32 Runtime(int32 value) { require value != 0; return value + 1; }
            bool Explode() { require false; return false; }
            int32 Add(int32 first, int32 second) { return first + second; }
            int32 Main() {
                mut int32 value = const Twice(20);
                value = Add(Runtime(1), const Twice(20));
                if (const Truth() || Explode()) { return value + Twice(0); }
                return 0;
            }
            """;
        return checkedFailures
            ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ParenthesizedRequirementAndNestedCallsExecuteAsync(bool checkedFailures)
    {
        const string Source = """
            [ConstEval] int32 Next(int32 value) { return value + 1; }
            int32 Main() {
                int32 before = Next(const (Next(39) + 1));
                if (const (Next(0) == 1)) {
                    return before;
                }
                return 0;
            }
            """;
        return checkedFailures
            ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
}
