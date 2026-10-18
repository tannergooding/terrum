// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task MultipleTypeParameterSampleRunsGenericBodiesInAotAndJitAsync(NativeRuntimePolicy policy)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(),
            "samples", "LanguageFeatures", "Generics", "multi-type-generics.terrum")).ConfigureAwait(true);
        if (policy == NativeRuntimePolicy.FailFast)
        {
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }
}
