// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
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
                OwnedInt32Buffer digits = FormatUInt64(uint64.MaxValue, move workspace);
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

    [Theory]
    [InlineData("0", false, 1)]
    [InlineData("42", false, 2)]
    [InlineData("18446744073709551615", false, 20)]
    [InlineData("0", true, 1)]
    [InlineData("-1", true, 2)]
    [InlineData("-9223372036854775808", true, 20)]
    [InlineData("4294967295", false, 10, true)]
    [InlineData("2147483647", true, 10, true)]
    [InlineData("-2147483648", true, 11, true)]
    [InlineData("0", false, 1, true)]
    public async Task DecimalFormattingAcceptsExactOutputCapacityAsync(
        string value, bool isSigned, int capacity, bool isNarrow = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        var format = FormatterName(isSigned, isNarrow, false);
        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerFormatting.terrum")).ConfigureAwait(true);
        var checks = string.Join(Environment.NewLine, value.Select((digit, index) =>
            FormattableString.Invariant($"if (digits.Read({index}) != {(int)digit}) {{ return 2; }}")));
        var source = component + $$"""

            int32 Main() {
                require OwnedInt32Buffer.TryCreate({{capacity}}, out var workspace);
                OwnedInt32Buffer digits = {{format}}({{value}}, move workspace);
                if (digits.Count != {{value.Length}} || digits.Capacity != {{capacity}}) { return 1; }
                {{checks}}
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("0", false, 0)]
    [InlineData("-1", true, 1)]
    [InlineData("-9223372036854775808", true, 19)]
    [InlineData("4294967295", false, 9, true)]
    [InlineData("-2147483648", true, 10, true)]
    public async Task DecimalFormattingRejectsUndersizedOutputAsync(
        string value, bool isSigned, int capacity, bool isNarrow = false)
    {
        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerFormatting.terrum")).ConfigureAwait(true);
        var format = FormatterName(isSigned, isNarrow, false);
        var attempt = $$"""

            int32 Attempt() {
                require OwnedInt32Buffer.TryCreate({{capacity}}, out var workspace);
                OwnedInt32Buffer digits = {{format}}({{value}}, move workspace);
                return digits.Count;
            }
            """;
        await AssertFailFastExecutionAsync(component + attempt + "int32 Main() { return Attempt(); }",
            unchecked((int)0xE0540003)).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(component + attempt + """
            int32 Main() {
                unsafe {
                    try { return Attempt(); }
                    catch { return 42; }
                }
            }
            """, 42).ConfigureAwait(true);
    }

    [Theory]
    [MemberData(nameof(EveryRadix))]
    [InlineData("0", 2, false)]
    [InlineData("0", 10, false)]
    [InlineData("12345678901234567890", 3, false)]
    [InlineData("12345678901234567890", 17, false)]
    [InlineData("0", 36, true)]
    [InlineData("9223372036854775807", 16, true)]
    [InlineData("-9223372036854775808", 2, true)]
    [InlineData("-9223372036854775808", 10, true)]
    [InlineData("-9223372036854775808", 16, true)]
    [InlineData("-1", 36, true)]
    [InlineData("-1234567890123456789", 35, true)]
    public async Task IntegerBaseFormattingUsesExactWorkspaceAcrossAotAndJitAsync(
        string value, int radix, bool signed, bool isNarrow = false)
    {
        var number = signed ? new BigInteger(long.Parse(value, CultureInfo.InvariantCulture))
            : new BigInteger(ulong.Parse(value, CultureInfo.InvariantCulture));
        var magnitude = BigInteger.Abs(number);
        var expectedDigits = new char[65];
        var start = expectedDigits.Length;
        do
        {
            magnitude = BigInteger.DivRem(magnitude, radix, out var remainder);
            expectedDigits[--start] = "0123456789abcdefghijklmnopqrstuvwxyz"[(int)remainder];
        }
        while (magnitude > 0);
        if (number.Sign < 0)
        {
            expectedDigits[--start] = '-';
        }
        var expected = new string(expectedDigits, start, expectedDigits.Length - start);
        var checks = string.Join(Environment.NewLine, expected.Select((digit, index) =>
            FormattableString.Invariant($"if (digits.Read({index}) != {(int)digit}) {{ return 2; }}")));
        var format = FormatterName(signed, isNarrow, true);
        var measure = (signed, isNarrow) switch {
            (false, false) => "TryGetUInt64BaseLength",
            (true, false) => "TryGetInt64BaseLength",
            (false, true) => "TryGetUInt32BaseLength",
            (true, true) => "TryGetInt32BaseLength",
        };
        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerFormatting.terrum")).ConfigureAwait(true);
        var source = component + $$"""

            int32 Main() {
                require {{measure}}({{value}}, {{radix}}, init var capacity);
                if (capacity != {{expected.Length}}) { return 4; }
                require OwnedInt32Buffer.TryCreate(capacity, out var workspace);
                OwnedInt32Buffer digits = {{format}}({{value}}, {{radix}}, move workspace);
                if (digits.Count != capacity || digits.Capacity != capacity) { return 1; }
                {{checks}}
                OwnedInt32Buffer zero = {{format}}(0, {{radix}}, move digits);
                if (zero.Count != 1 || zero.Read(0) != 48 || zero.Capacity != capacity) { return 3; }
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    public static IEnumerable<object[]> EveryRadix()
    {
        for (var radix = 2; radix <= 36; radix++)
        {
            yield return [ulong.MaxValue.ToString(CultureInfo.InvariantCulture), radix, false];
            yield return [uint.MaxValue.ToString(CultureInfo.InvariantCulture), radix, false, true];
            yield return [int.MinValue.ToString(CultureInfo.InvariantCulture), radix, true, true];
        }
    }

    [Theory]
    [MemberData(nameof(EveryChar16Radix))]
    [InlineData(0, 10)]
    [InlineData(55296, 16)]
    [InlineData(65535, 2)]
    public async Task Char16FormattingMeasuresExactCapacityAcrossAotAndJitAsync(int value, int radix)
    {
        var remaining = new BigInteger(value);
        var digits = new char[16];
        var start = digits.Length;
        do
        {
            remaining = BigInteger.DivRem(remaining, radix, out var digit);
            digits[--start] = "0123456789abcdefghijklmnopqrstuvwxyz"[(int)digit];
        }
        while (remaining > 0);
        var expected = new string(digits, start, digits.Length - start);
        var checks = string.Join(Environment.NewLine, expected.Select((digit, index) =>
            FormattableString.Invariant($"if (formatted.Read({index}) != {(int)digit}) {{ return 2; }}")));
        var decimalText = value.ToString(CultureInfo.InvariantCulture);
        var decimalChecks = string.Join(Environment.NewLine, decimalText.Select((digit, index) =>
            FormattableString.Invariant($"if (decimalDigits.Read({index}) != {(int)digit}) {{ return 5; }}")));
        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerFormatting.terrum")).ConfigureAwait(true);
        var source = component + $$"""

            int32 Main() {
                if (TryGetChar16BaseLength(char16({{value}}), 1, init var bad)) { return 6; }
                require TryGetChar16BaseLength(char16({{value}}), {{radix}}, init var capacity);
                if (capacity != {{expected.Length}}) { return 1; }
                require OwnedInt32Buffer.TryCreate(capacity, out var workspace);
                OwnedInt32Buffer formatted = FormatChar16Base(char16({{value}}), {{radix}}, move workspace);
                if (formatted.Count != capacity || formatted.Capacity != capacity) { return 3; }
                {{checks}}
                require TryGetChar16BaseLength(char16({{value}}), 10, init var decimalLength);
                require OwnedInt32Buffer.TryCreate(decimalLength, out var decimalWorkspace);
                OwnedInt32Buffer decimalDigits = FormatChar16(char16({{value}}), move decimalWorkspace);
                if (decimalLength != {{decimalText.Length}} || decimalDigits.Count != decimalLength
                    || decimalDigits.Capacity != decimalLength) { return 4; }
                {{decimalChecks}}
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    public static IEnumerable<object[]> EveryChar16Radix()
    {
        for (var radix = 2; radix <= 36; radix++)
        {
            yield return [65535, radix];
        }
    }

    [Theory]
    [InlineData(1, 16)]
    [InlineData(37, 16)]
    [InlineData(2, 15)]
    public async Task Char16FormattingRejectsInvalidBaseAndInsufficientStorageAsync(int radix, int capacity)
    {
        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerFormatting.terrum")).ConfigureAwait(true);
        var attempt = $$"""

            int32 Attempt() {
                require OwnedInt32Buffer.TryCreate({{capacity}}, out var workspace);
                OwnedInt32Buffer digits = FormatChar16Base(char16.MaxValue, {{radix}}, move workspace);
                return digits.Count;
            }
            """;
        await AssertFailFastExecutionAsync(component + attempt + "int32 Main() { return Attempt(); }",
            unchecked((int)0xE0540003)).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(component + attempt + """
            int32 Main() {
                unsafe {
                    try { return Attempt(); }
                    catch { return 42; }
                }
            }
            """, 42).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(1, 16, false)]
    [InlineData(37, 16, false)]
    [InlineData(16, 15, false)]
    [InlineData(2, 63, false)]
    [InlineData(2, 64, true)]
    [InlineData(2, 31, false, true)]
    [InlineData(2, 32, true, true)]
    public async Task IntegerBaseFormattingRejectsInvalidRadixAndInsufficientStorageAsync(
        int radix, int capacity, bool signed, bool isNarrow = false)
    {
        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerFormatting.terrum")).ConfigureAwait(true);
        var format = FormatterName(signed, isNarrow, true);
        var value = signed
            ? isNarrow ? "int32.MinValue" : "int64.MinValue"
            : isNarrow ? "uint32.MaxValue" : "uint64.MaxValue";
        var attempt = $$"""

            int32 Attempt() {
                require OwnedInt32Buffer.TryCreate({{capacity}}, out var workspace);
                OwnedInt32Buffer digits = {{format}}({{value}}, {{radix}}, move workspace);
                return digits.Count;
            }
            """;
        await AssertFailFastExecutionAsync(component + attempt + "int32 Main() { return Attempt(); }",
            unchecked((int)0xE0540003)).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(component + attempt + """
            int32 Main() {
                unsafe {
                    try { return Attempt(); }
                    catch { return 42; }
                }
            }
            """, 42).ConfigureAwait(true);
    }

    private static string FormatterName(bool signed, bool isNarrow, bool isBase) =>
        (signed, isNarrow, isBase) switch {
            (false, false, false) => "FormatUInt64",
            (true, false, false) => "FormatInt64",
            (false, true, false) => "FormatUInt32",
            (true, true, false) => "FormatInt32",
            (false, false, true) => "FormatUInt64Base",
            (true, false, true) => "FormatInt64Base",
            (false, true, true) => "FormatUInt32Base",
            (true, true, true) => "FormatInt32Base",
        };
}
