// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task CopyStructAndClassGettersAgreeAcrossAotAndJitAsync(bool checkedFailures) =>
        checkedFailures ? AssertCheckedExecutionAsync(GetterPropertyTests.ValueSource, 42)
            : AssertFailFastExecutionAsync(GetterPropertyTests.ValueSource, 42);

    [Test]
    public Task CheckedGetterFailureRemainsCatchableAsync()
    {
        const string Source = """
            class Box {
                int32 _value;
                public Box(int32 value) { _value = value; }
                public int32 Value { get { require _value > 0; return _value; } }
            }
            int32 Main() {
                require Box.TryCreate(0, out var box);
                unsafe {
                    try { return box.Value; }
                    catch { return 42; }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
}
