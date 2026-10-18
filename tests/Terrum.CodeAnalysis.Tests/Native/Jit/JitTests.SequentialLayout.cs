// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
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
