// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task ProvenStaticChecksDoNotAlterRuntimeCallsAsync(bool checkedFailures)
    {
        const string Source = """
            [ConstEval] int32 FortyTwo() {
                static require true;
                static ensure true;
                return 42;
            }
            int32 Main() {
                static ensure FortyTwo() == 42;
                if (false) { require false; }
                static require FortyTwo() == 42;
                require FortyTwo() == 42;
                return FortyTwo();
            }
            """;
        return checkedFailures
            ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Test]
    public Task StaticChecksDoNotInterceptExecutableRequirementFailureAsync()
    {
        const string Source = """
            int32 Main() {
                static require true;
                static ensure true;
                unsafe {
                    try {
                        require false;
                        return 0;
                    } catch {
                        return 42;
                    }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
}
