// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PrivateSequentialCopiesExecuteThroughAotAndCliJitAsync(bool checkedFailures)
    {
        var source = SequentialLayoutTests.Library + """
            int32 Main() {
                return Compute(37);
            }
            """;
        return checkedFailures
            ? AssertCheckedExecutionAsync(source, 42)
            : AssertFailFastExecutionAsync(source, 42);
    }
}
