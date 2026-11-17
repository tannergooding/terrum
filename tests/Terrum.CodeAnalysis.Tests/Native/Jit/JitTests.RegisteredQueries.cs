// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

internal sealed partial class JitTests
{
    [Test]
    public async Task RegisteredLayoutAndTypeIdentityQueriesExecuteOnWindowsX64()
    {
        var source = SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            struct Pair {
                public uint8 Tag;
                public uint32 Value;
            }

            struct AutoPair {
                int32 First;
                int32 Second;

                public static nint OffsetOfSecond() {
                    return unsafe(offsetof(AutoPair, Second));
                }
            }

            bool SelectIdentity(bool choose) {
                return (choose ? typeof(nint) : typeof(int64))
                    == typeof(int64);
            }

            int32 Main() {
                if (sizeof(nint) != nint(8)) { return -1; }
                if (alignof(void) != 0) { return -2; }
                if (offsetof(Pair, Value) != nint(1)) { return -3; }
                if (typeof(nint) == typeof(int64)) { return -4; }
                if (typeof(Pair) != typeof(Pair)) { return -5; }
                if (int32(AutoPair.OffsetOfSecond()) != 4) { return -6; }
                if ((typeof(nint) != typeof(int64)
                    ? typeof(int64) : typeof(nint)) != typeof(int64)) {
                    return -7;
                }
                if (SelectIdentity(true)) { return -8; }
                if (!SelectIdentity(false)) { return -9; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(source, new CompilationOptions {
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException("The query program did not bind.");
        var main = program.Functions.Single(
            static function => function.Name == "Main");
        var hasLayoutQuery = false;
        var hasTypeIdentityQuery = false;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body, expression =>
        {
            hasLayoutQuery |= expression is BoundLayoutQuery;
            hasTypeIdentityQuery |= expression is BoundTypeIdentityQuery;
        }), Is.True);
        Assert.That(hasLayoutQuery, Is.True);
        Assert.That(hasTypeIdentityQuery, Is.True);

        await AssertProgramExecutionAsync(program, 42,
            NativeRuntimePolicy.FailFast).ConfigureAwait(true);
    }
}
