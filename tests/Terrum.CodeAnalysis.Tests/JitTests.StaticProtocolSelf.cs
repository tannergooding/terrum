// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SelfBoundAndOrdinaryProtocolsExecuteUnderBothPoliciesAsync(bool checkedFailures) =>
        checkedFailures
            ? AssertCheckedExecutionAsync(StaticProtocolSelfTests.Source, 42)
            : AssertFailFastExecutionAsync(StaticProtocolSelfTests.Source, 42);
}
