// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData("0", false)]
    [InlineData("1", false)]
    [InlineData("9", false)]
    [InlineData("10", false)]
    [InlineData("99", false)]
    [InlineData("100", false)]
    [InlineData("1000", false)]
    [InlineData("4294967296", false)]
    [InlineData("9223372036854775808", false)]
    [InlineData("18446744073709551615", false)]
    [InlineData("0", true)]
    [InlineData("42", true)]
    [InlineData("-1", true)]
    [InlineData("-10", true)]
    [InlineData("-1000", true)]
    [InlineData("-4294967296", true)]
    [InlineData("9223372036854775807", true)]
    [InlineData("-9223372036854775808", true)]
    public async Task IntegerFormattingReusesWorkspaceAcrossAotAndJitAsync(string value, bool allowNegative)
    {
        var expected = allowNegative
            ? long.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture)
            : ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture);
        var format = allowNegative ? "FormatInt64" : "FormatUInt64";
        var maximum = allowNegative ? "int64.MinValue" : "uint64.MaxValue";
        var checks = string.Join(Environment.NewLine, expected.Select((digit, index) =>
            FormattableString.Invariant($"if (digits.Read({index}) != {(int)digit}) {{ return 2; }}")));
        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerFormatting.terrum")).ConfigureAwait(true);
        var source = component + $$"""

            int32 Main() {
                require OwnedInt32Buffer.TryCreate(20, out var workspace);
                OwnedInt32Buffer maximum = {{format}}({{maximum}}, move workspace);
                OwnedInt32Buffer digits = {{format}}({{value}}, move maximum);
                if (digits.Count != {{expected.Length}} || digits.Capacity != 20) { return 1; }
                {{checks}}
                OwnedInt32Buffer zero = {{format}}(0, move digits);
                if (zero.Count != 1 || zero.Read(0) != 48 || zero.Capacity != 20) { return 3; }
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Fact]
    public async Task IntegerFormattingRejectsInsufficientWorkspaceAcrossAotAndJitAsync()
    {
        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerFormatting.terrum")).ConfigureAwait(true);
        const string Failure = """

            int32 Attempt() {
                require OwnedInt32Buffer.TryCreate(19, out var workspace);
                OwnedInt32Buffer digits = FormatUInt64(0, move workspace);
                return digits.Count;
            }
            """;
        await AssertFailFastExecutionAsync(
            component + Failure + "int32 Main() { return Attempt(); }",
            unchecked((int)0xE0540003)).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(
            component + Failure + """
                int32 Main() {
                    unsafe {
                        try { return Attempt(); }
                        catch { return 42; }
                    }
                }
                """, 42).ConfigureAwait(true);
    }
}
