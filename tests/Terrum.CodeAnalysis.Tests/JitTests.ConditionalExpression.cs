// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false, false, 42)]
    [InlineData(false, true, unchecked((int)0xE0540003))]
    [InlineData(true, false, 42)]
    [InlineData(true, true, unchecked((int)0xE0540003))]
    public Task OnlySelectedArmCanFailAsync(bool checkedFailures, bool chooseFailure, int expected)
    {
        var source = $$"""
            int32 Fail() { require false; return 0; }
            int32 Main() { return {{(chooseFailure ? "true" : "false")}} ? Fail() : 42; }
            """;
        return checkedFailures
            ? AssertCheckedExecutionAsync(source, expected)
            : AssertFailFastExecutionAsync(source, expected);
    }
}
