// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Fact]
    public async Task SaturatingIntegerConversionsClampAcrossAotAndJitAsync()
    {
        var checks = new[] {
            "SaturateUInt32ToInt32(0) != 0",
            "SaturateUInt32ToInt32(42) != 42",
            "SaturateUInt32ToInt32(uint32(int32.MaxValue)) != int32.MaxValue",
            "SaturateUInt32ToInt32(uint32(int32.MaxValue) + 1) != int32.MaxValue",
            "SaturateUInt32ToInt32(uint32.MaxValue) != int32.MaxValue",
            "SaturateInt32ToUInt32(int32.MinValue) != 0",
            "SaturateInt32ToUInt32(-1) != 0",
            "SaturateInt32ToUInt32(0) != 0",
            "SaturateInt32ToUInt32(42) != 42",
            "SaturateInt32ToUInt32(int32.MaxValue) != uint32(int32.MaxValue)",
            "SaturateInt64ToInt32(int64.MinValue) != int32.MinValue",
            "SaturateInt64ToInt32(int64(int32.MinValue) - 1) != int32.MinValue",
            "SaturateInt64ToInt32(int64(int32.MinValue)) != int32.MinValue",
            "SaturateInt64ToInt32(int64(int32.MinValue) + 1) != int32.MinValue + 1",
            "SaturateInt64ToInt32(-42) != -42",
            "SaturateInt64ToInt32(int64(int32.MaxValue) + 1) != int32.MaxValue",
            "SaturateInt64ToInt32(int64(int32.MaxValue)) != int32.MaxValue",
            "SaturateInt64ToInt32(int64.MaxValue) != int32.MaxValue",
            "SaturateInt64ToUInt32(int64.MinValue) != 0",
            "SaturateInt64ToUInt32(-1) != 0",
            "SaturateInt64ToUInt32(0) != 0",
            "SaturateInt64ToUInt32(42) != 42",
            "SaturateInt64ToUInt32(int64(uint32.MaxValue)) != uint32.MaxValue",
            "SaturateInt64ToUInt32(int64(uint32.MaxValue) + 1) != uint32.MaxValue",
            "SaturateUInt64ToInt32(0) != 0",
            "SaturateUInt64ToInt32(42) != 42",
            "SaturateUInt64ToInt32(uint64(int32.MaxValue)) != int32.MaxValue",
            "SaturateUInt64ToInt32(uint64(int32.MaxValue) + 1) != int32.MaxValue",
            "SaturateUInt64ToInt32(uint64.MaxValue) != int32.MaxValue",
            "SaturateUInt64ToUInt32(0) != 0",
            "SaturateUInt64ToUInt32(42) != 42",
            "SaturateUInt64ToUInt32(uint64(uint32.MaxValue)) != uint32.MaxValue",
            "SaturateUInt64ToUInt32(uint64(uint32.MaxValue) + 1) != uint32.MaxValue",
            "SaturateUInt64ToUInt32(uint64.MaxValue) != uint32.MaxValue",
            "SaturateUInt64ToInt64(0) != 0",
            "SaturateUInt64ToInt64(42) != 42",
            "SaturateUInt64ToInt64(uint64(int64.MaxValue)) != int64.MaxValue",
            "SaturateUInt64ToInt64(uint64(int64.MaxValue) + 1) != int64.MaxValue",
            "SaturateUInt64ToInt64(uint64.MaxValue) != int64.MaxValue",
            "SaturateInt64ToUInt64(int64.MinValue) != 0",
            "SaturateInt64ToUInt64(-1) != 0",
            "SaturateInt64ToUInt64(0) != 0",
            "SaturateInt64ToUInt64(42) != 42",
            "SaturateInt64ToUInt64(int64.MaxValue) != uint64(int64.MaxValue)",
        };
        var assertions = string.Join(Environment.NewLine, checks.Select((check, index) =>
            $"if ({check}) {{ return {index + 1}; }}"));
        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerSaturation.terrum")).ConfigureAwait(true);
        var source = component + "\nint32 Main() {\n" + assertions + "\nreturn 42;\n}";

        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
}
