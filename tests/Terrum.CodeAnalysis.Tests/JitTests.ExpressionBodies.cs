// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ExpressionBodiedCallablesAgreeAcrossAotAndJitAsync(bool checkedFailures) =>
        checkedFailures ? AssertCheckedExecutionAsync(ExpressionBodyTests.Source, 42)
            : AssertFailFastExecutionAsync(ExpressionBodyTests.Source, 42);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ExpressionBodyReturnCleansTransferredOwnerAsync(bool checkedFailures)
    {
        const string Source = """
            int32 Read(OwnedInt32Buffer buffer) => buffer.Read(0);
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer buffer = move raw;
                require buffer.TryAppend(42);
                return Read(move buffer);
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Fact]
    public Task ExpressionBodyFailureCleansTransferredOwnerAsync()
    {
        const string Source = """
            int32 Read(OwnedInt32Buffer buffer) => buffer.Read(1);
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer buffer = move raw;
                require buffer.TryAppend(10);
                unsafe {
                    try { return Read(move buffer); }
                    catch { return 42; }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MutableClassExpressionBodyUpdatesOriginalReceiverAsync(bool checkedFailures)
    {
        const string Source = """
            mut class Box {
                public mut int32 Value;
                public Box(int32 value) => Value = value;
                public mut void Set(int32 value) => Value = value;
                public int32 Read() => Value;
            }
            int32 Main() {
                require Box.TryCreate(1, out var created);
                write Box box = created;
                box.Set(42);
                return box.Read();
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
}
