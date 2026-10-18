// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [TestCase("0",false)]
    [TestCase("1",false)]
    [TestCase("9",false)]
    [TestCase("10",false)]
    [TestCase("99",false)]
    [TestCase("100",false)]
    [TestCase("1000",false)]
    [TestCase("4294967296",false)]
    [TestCase("9223372036854775808",false)]
    [TestCase("18446744073709551615",false)]
    [TestCase("0",true)]
    [TestCase("42",true)]
    [TestCase("-1",true)]
    [TestCase("-10",true)]
    [TestCase("-1000",true)]
    [TestCase("-4294967296",true)]
    [TestCase("9223372036854775807",true)]
    [TestCase("-9223372036854775808",true)]
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
            TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerFormatting.terrum")).ConfigureAwait(true);
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
        await AssertIntegerFormattingFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertIntegerFormattingCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Test]
    public async Task IntegerFormattingRejectsInsufficientWorkspaceAcrossAotAndJitAsync()
    {
        var component = await File.ReadAllTextAsync(
            TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerFormatting.terrum")).ConfigureAwait(true);
        const string Failure = """

            int32 Attempt() {
                require OwnedInt32Buffer.TryCreate(19, out var workspace);
                OwnedInt32Buffer digits = FormatUInt64(uint64.MaxValue, move workspace);
                return digits.Count;
            }
            """;
        await AssertIntegerFormattingFailFastExecutionAsync(
            component + Failure + "int32 Main() { return Attempt(); }",
            unchecked((int)0xE0540003)).ConfigureAwait(true);
        await AssertIntegerFormattingCheckedExecutionAsync(
            component + Failure + """
                int32 Main() {
                    unsafe {
                        try { return Attempt(); }
                        catch { return 42; }
                    }
                }
                """, 42).ConfigureAwait(true);
    }
    [TestCase("0",false,1)]
    [TestCase("42",false,2)]
    [TestCase("18446744073709551615",false,20)]
    [TestCase("0",true,1)]
    [TestCase("-1",true,2)]
    [TestCase("-9223372036854775808",true,20)]
    [TestCase("4294967295",false,10,true)]
    [TestCase("2147483647",true,10,true)]
    [TestCase("-2147483648",true,11,true)]
    [TestCase("0",false,1,true)]
    public async Task DecimalFormattingAcceptsExactOutputCapacityAsync(
        string value, bool isSigned, int capacity, bool isNarrow = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        var format = FormatterName(isSigned, isNarrow, false);
        var component = await File.ReadAllTextAsync(
            TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerFormatting.terrum")).ConfigureAwait(true);
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
        await AssertIntegerFormattingFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertIntegerFormattingCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
    [TestCase("0",false,0)]
    [TestCase("-1",true,1)]
    [TestCase("-9223372036854775808",true,19)]
    [TestCase("4294967295",false,9,true)]
    [TestCase("-2147483648",true,10,true)]
    public async Task DecimalFormattingRejectsUndersizedOutputAsync(
        string value, bool isSigned, int capacity, bool isNarrow = false)
    {
        var component = await File.ReadAllTextAsync(
            TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerFormatting.terrum")).ConfigureAwait(true);
        var format = FormatterName(isSigned, isNarrow, false);
        var attempt = $$"""

            int32 Attempt() {
                require OwnedInt32Buffer.TryCreate({{capacity}}, out var workspace);
                OwnedInt32Buffer digits = {{format}}({{value}}, move workspace);
                return digits.Count;
            }
            """;
        await AssertIntegerFormattingFailFastExecutionAsync(component + attempt + "int32 Main() { return Attempt(); }",
            unchecked((int)0xE0540003)).ConfigureAwait(true);
        await AssertIntegerFormattingCheckedExecutionAsync(component + attempt + """
            int32 Main() {
                unsafe {
                    try { return Attempt(); }
                    catch { return 42; }
                }
            }
            """, 42).ConfigureAwait(true);
    }
    [TestCaseSource(nameof(EveryRadix))]
    [TestCase("0",2,false)]
    [TestCase("0",10,false)]
    [TestCase("12345678901234567890",3,false)]
    [TestCase("12345678901234567890",17,false)]
    [TestCase("0",36,true)]
    [TestCase("9223372036854775807",16,true)]
    [TestCase("-9223372036854775808",2,true)]
    [TestCase("-9223372036854775808",10,true)]
    [TestCase("-9223372036854775808",16,true)]
    [TestCase("-10",10,true)]
    [TestCase("-35",36,true,false,true)]
    [TestCase("-1",36,true)]
    [TestCase("-1234567890123456789",35,true)]
    [TestCase("4294967295",16,false,true,true)]
    public async Task IntegerBaseFormattingUsesExactWorkspaceAcrossAotAndJitAsync(
        string value, int radix, bool signed, bool isNarrow = false, bool uppercase = false)
    {
        var number = signed ? new BigInteger(long.Parse(value, CultureInfo.InvariantCulture))
            : new BigInteger(ulong.Parse(value, CultureInfo.InvariantCulture));
        var magnitude = BigInteger.Abs(number);
        var expectedDigits = new char[65];
        var start = expectedDigits.Length;
        do
        {
            magnitude = BigInteger.DivRem(magnitude, radix, out var remainder);
            expectedDigits[--start] = (uppercase
                ? "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"
                : "0123456789abcdefghijklmnopqrstuvwxyz")[(int)remainder];
        }
        while (magnitude > 0);
        if (number.Sign < 0)
        {
            expectedDigits[--start] = '-';
        }
        var expected = new string(expectedDigits, start, expectedDigits.Length - start);
        var checks = string.Join(Environment.NewLine, expected.Select((digit, index) =>
            FormattableString.Invariant($"if (digits.Read({index}) != {(int)digit}) {{ return 2; }}")));
        var format = FormatterName(signed, isNarrow, true) + (uppercase ? "Upper" : "");
        var measure = (signed, isNarrow) switch {
            (false, false) => "TryGetUInt64BaseLength",
            (true, false) => "TryGetInt64BaseLength",
            (false, true) => "TryGetUInt32BaseLength",
            (true, true) => "TryGetInt32BaseLength",
        };
        var component = await File.ReadAllTextAsync(
            TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerFormatting.terrum")).ConfigureAwait(true);
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
        await AssertIntegerFormattingFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertIntegerFormattingCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    public static IEnumerable<object[]> EveryRadix()
    {
        for (var radix = 2; radix <= 36; radix++)
        {
            yield return [ulong.MaxValue.ToString(CultureInfo.InvariantCulture), radix, false];
            yield return [uint.MaxValue.ToString(CultureInfo.InvariantCulture), radix, false, true];
            yield return [int.MinValue.ToString(CultureInfo.InvariantCulture), radix, true, true];
            yield return [ulong.MaxValue.ToString(CultureInfo.InvariantCulture), radix, false, false, true];
            yield return [int.MinValue.ToString(CultureInfo.InvariantCulture), radix, true, true, true];
        }
    }
    [TestCaseSource(nameof(EveryChar16Radix))]
    [TestCase(0,10)]
    [TestCase(55296,16)]
    [TestCase(65535,2)]
    [TestCase(65535,16,true)]
    [TestCase(65535,36,true)]
    public async Task Char16FormattingMeasuresExactCapacityAcrossAotAndJitAsync(
        int value, int radix, bool uppercase = false)
    {
        var remaining = new BigInteger(value);
        var digits = new char[16];
        var start = digits.Length;
        do
        {
            remaining = BigInteger.DivRem(remaining, radix, out var digit);
            digits[--start] = (uppercase
                ? "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"
                : "0123456789abcdefghijklmnopqrstuvwxyz")[(int)digit];
        }
        while (remaining > 0);
        var expected = new string(digits, start, digits.Length - start);
        var checks = string.Join(Environment.NewLine, expected.Select((digit, index) =>
            FormattableString.Invariant($"if (formatted.Read({index}) != {(int)digit}) {{ return 2; }}")));
        var decimalText = value.ToString(CultureInfo.InvariantCulture);
        var decimalChecks = string.Join(Environment.NewLine, decimalText.Select((digit, index) =>
            FormattableString.Invariant($"if (decimalDigits.Read({index}) != {(int)digit}) {{ return 5; }}")));
        var component = await File.ReadAllTextAsync(
            TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerFormatting.terrum")).ConfigureAwait(true);
        var format = uppercase ? "FormatChar16BaseUpper" : "FormatChar16Base";
        var source = component + $$"""

            int32 Main() {
                if (TryGetChar16BaseLength(char16({{value}}), 1, init var bad)) { return 6; }
                require TryGetChar16BaseLength(char16({{value}}), {{radix}}, init var capacity);
                if (capacity != {{expected.Length}}) { return 1; }
                require OwnedInt32Buffer.TryCreate(capacity, out var workspace);
                OwnedInt32Buffer formatted = {{format}}(char16({{value}}), {{radix}}, move workspace);
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
        await AssertIntegerFormattingFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertIntegerFormattingCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    public static IEnumerable<object[]> EveryChar16Radix()
    {
        for (var radix = 2; radix <= 36; radix++)
        {
            yield return [65535, radix];
            yield return [65535, radix, true];
        }
    }
    [TestCase(1,16)]
    [TestCase(1,16,true)]
    [TestCase(37,16)]
    [TestCase(37,16,true)]
    [TestCase(2,15)]
    public async Task Char16FormattingRejectsInvalidBaseAndInsufficientStorageAsync(
        int radix, int capacity, bool uppercase = false)
    {
        var component = await File.ReadAllTextAsync(
            TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerFormatting.terrum")).ConfigureAwait(true);
        var format = uppercase ? "FormatChar16BaseUpper" : "FormatChar16Base";
        var attempt = $$"""

            int32 Attempt() {
                require OwnedInt32Buffer.TryCreate({{capacity}}, out var workspace);
                OwnedInt32Buffer digits = {{format}}(char16.MaxValue, {{radix}}, move workspace);
                return digits.Count;
            }
            """;
        await AssertIntegerFormattingFailFastExecutionAsync(component + attempt + "int32 Main() { return Attempt(); }",
            unchecked((int)0xE0540003)).ConfigureAwait(true);
        await AssertIntegerFormattingCheckedExecutionAsync(component + attempt + """
            int32 Main() {
                unsafe {
                    try { return Attempt(); }
                    catch { return 42; }
                }
            }
            """, 42).ConfigureAwait(true);
    }
    [TestCase(1,16,false)]
    [TestCase(1,16,false,false,true)]
    [TestCase(37,16,false)]
    [TestCase(37,16,false,false,true)]
    [TestCase(16,15,false)]
    [TestCase(2,63,false)]
    [TestCase(2,64,true)]
    [TestCase(2,31,false,true)]
    [TestCase(2,32,true,true)]
    public async Task IntegerBaseFormattingRejectsInvalidRadixAndInsufficientStorageAsync(
        int radix, int capacity, bool signed, bool isNarrow = false, bool uppercase = false)
    {
        var component = await File.ReadAllTextAsync(
            TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerFormatting.terrum")).ConfigureAwait(true);
        var format = FormatterName(signed, isNarrow, true) + (uppercase ? "Upper" : "");
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
        await AssertIntegerFormattingFailFastExecutionAsync(component + attempt + "int32 Main() { return Attempt(); }",
            unchecked((int)0xE0540003)).ConfigureAwait(true);
        await AssertIntegerFormattingCheckedExecutionAsync(component + attempt + """
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

    private static Task AssertIntegerFormattingFailFastExecutionAsync(string source, int expected) =>
        AssertFailFastExecutionAsync(source, expected,
            TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/WideIntegerFacts.terrum"));

    private static Task AssertIntegerFormattingCheckedExecutionAsync(string source, int expected) =>
        AssertCheckedExecutionAsync(source, expected,
            TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/WideIntegerFacts.terrum"));
}
