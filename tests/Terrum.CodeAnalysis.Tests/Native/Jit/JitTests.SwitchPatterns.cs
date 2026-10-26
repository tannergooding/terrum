// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [Test]
    public async Task InferredScalarSwitchPatternsBindBeforeGuardsAndBodiesAsync()
    {
        const string source = """
            int32 MatchOnly() {
                switch (int32(42)) {
                    var item => { return item; },
                }
            }
            int32 Main() {
                if (MatchOnly() != 42) {
                    return 0;
                }
                int32 selector = 42;
                switch (selector) {
                    var value when value == 41 => { return 0; },
                    mut var matched when matched == 42 => {
                        matched += 1;
                        return matched;
                    },
                    _ => { return 0; },
                }
            }
            """;
        await AssertFailFastExecutionAsync(source, 43).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 43).ConfigureAwait(true);
    }
}
