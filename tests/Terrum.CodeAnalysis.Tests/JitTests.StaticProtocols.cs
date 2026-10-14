// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ClosedStaticProtocolSpecializationsAgreeWithNativeExecutableAsync(bool checkedFailures)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "static-protocol-quadratic.terrum")).ConfigureAwait(true);
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(source, 46).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(source, 46).ConfigureAwait(true);
        }
    }
}
