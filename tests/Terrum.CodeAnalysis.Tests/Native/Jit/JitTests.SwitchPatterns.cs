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
            int32 MatchTyped(int32 selector) {
                switch (selector) {
                    mut int32 item when item == 42 => {
                        item += 1;
                        return item;
                    },
                    _ => { return 0; },
                }
            }
            int32 MatchComposite(int32 selector) {
                switch (selector) {
                    1 or 2 => { return 11; },
                    not 3 => { return selector; },
                    _ => { return 0; },
                }
            }
            int32 MatchAnd(int32 selector) {
                switch (selector) {
                    1 and 1 => { return 1; },
                    _ => { return 0; },
                }
            }
            int32 MatchAndBinding(int32 selector) {
                switch (selector) {
                    var item and 1 when item == selector => { return item; },
                    _ => { return 0; },
                }
            }
            int32 Main() {
                if (MatchOnly() != 42 || MatchTyped(42) != 43
                    || MatchComposite(1) != 11 || MatchComposite(2) != 11
                    || MatchComposite(3) != 0 || MatchComposite(42) != 42
                    || MatchAnd(1) != 1 || MatchAnd(2) != 0
                    || MatchAndBinding(1) != 1 || MatchAndBinding(2) != 0) {
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
