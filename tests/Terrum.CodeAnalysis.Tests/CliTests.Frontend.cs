// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ScalarBinary(int left, int right);

    [Theory]
    [InlineData("int32 Main() { return 42; }", 42)]
    [InlineData("int32 Main() { return -0xFFFFFFD6; }", 42)]
    [InlineData("int32 Main() { if (true) { return 42; } else { return 0; } }", 42)]
    [InlineData("int32 Main() { return ((42)); }", 42)]
    [InlineData("int32 Main() { if (!false) { return 42; } else { return 0; } }", 42)]
    [InlineData("int32 Main() { return 0; }", 0)]
    [InlineData("int32 Main() { return 20 + 22; }", 42)]
    [InlineData("int32 Main() { return 84 / 2; }", 42)]
    [InlineData("int32 Main() { return 0x2A & 0x3F; }", 42)]
    [InlineData("int32 Main() { return 40 ^ 2; }", 42)]
    [InlineData("int32 Main() { return +42; }", 42)]
    [InlineData("int32 Main() { if (true) { return 42; } return 0; }", 42)]
    [InlineData("int32 Main() { if (false) { return 0; } return 42; }", 42)]
    public async Task FrontendRunMatchesHostedJitAsync(string source, int expected)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                "frontend-run", directory, "-", "--runtime", policy, "--json");
            var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                "jit", "-", "--runtime", policy, "--json");
            Assert.Equal(expected, hostedExit);
            Assert.Equal(hostedExit, nativeExit);
            Assert.Empty(nativeOutput);
            Assert.Empty(hostedOutput);
            Assert.Empty(nativeError);
            Assert.Empty(hostedError);
        }
    }

    [Fact]
    public async Task FrontendRunRejectsUnsupportedEntryAsync()
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(
            Encoding.UTF8.GetBytes("int32 Other() { return 42; }"),
            "frontend-run", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("TRM4002", result.RootElement.GetProperty("diagnostics")[0]
            .GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("int32 Main() { return 42; }")]
    [InlineData("public int32 Main() { return 0x2A; }")]
    [InlineData("int32 Main() { if (true) { return 42; } else { return 0; } }")]
    [InlineData("int32 Main() { return -0xFFFFFFD6; }")]
    [InlineData("int32 Main() { return ((42)); }")]
    [InlineData("int32 Main() { if (!false) { return 42; } else { return 0; } }")]
    [InlineData("int32 Main() { return 20 + 22; }")]
    [InlineData("int32 Main() { return 6 * 7; }")]
    [InlineData("int32 Main() { return 84 / 2; }")]
    [InlineData("int32 Main() { return 85 % 43; }")]
    [InlineData("int32 Main() { return 0x2A & 0x3F; }")]
    [InlineData("int32 Main() { return 0 | 42; }")]
    [InlineData("int32 Main() { return 40 ^ 2; }")]
    [InlineData("int32 Main() { return 50 - 8; }")]
    [InlineData("int32 Main() { return +0x2A; }")]
    [InlineData("int32 Main() { if (true) { return 42; } return 0; }")]
    [InlineData("int32 Main() { if (false) { return 0; } return 42; }")]
    public async Task FrontendBuiltExecutableMatchesHostedAndRunsAsync(string source)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-native-executable-{Guid.NewGuid():N}");
        var nativePath = prefix + ".native.exe";
        var hostedPath = prefix + ".hosted.exe";
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input, "frontend-build",
                    directory, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, _, hostedError) = await RunAsync(input, "build",
                    "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.Equal(0, nativeExit);
                Assert.Equal(0, hostedExit);
                Assert.Empty(nativeError);
                Assert.Empty(hostedError);
                using (var document = JsonDocument.Parse(nativeOutput))
                {
                    Assert.Equal("frontend-build", document.RootElement.GetProperty("command").GetString());
                    Assert.Equal(nativePath, document.RootElement.GetProperty("output").GetString());
                }
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                    await File.ReadAllBytesAsync(nativePath));
                using var process = Process.Start(new ProcessStartInfo(nativePath) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("Could not start native frontend executable.");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Assert.Equal(42, process.ExitCode);
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("int32 Other() { return 42; }")]
    [InlineData("int32 Main(int32 x) { return x; }")]
    [InlineData("uint32 Main() { return 42; }")]
    public async Task FrontendExecutableRejectsUnsupportedEntryWithoutReplacingOutputAsync(
        string source)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var outputPath = Path.Combine(Path.GetTempPath(), $"terrum-frontend-entry-{Guid.NewGuid():N}.exe");
        try
        {
            var (initial, _, initialError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                "frontend-build", directory, "-", "-o", outputPath, "--runtime", "fail-fast", "--json");
            Assert.Equal(0, initial);
            Assert.Empty(initialError);
            var image = await File.ReadAllBytesAsync(outputPath);

            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-build", directory, "-", "-o", outputPath, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("TRM4002", document.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString());
            Assert.Equal(image, await File.ReadAllBytesAsync(outputPath));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Theory]
    [InlineData("public int32 Value(int32 x, int32 y) { return x + y; }")]
    [InlineData("public int32 Value(int32 x, int32 y) { if (x < y) { return x + y; } else { return x - y; } }")]
    [InlineData("public bool Value(int32 x, int32 y) { return x >= y; }")]
    [InlineData("public char16 Value(char16 x, char16 y) { if (x != y) { return y; } else { return x; } }")]
    [InlineData("public bool Value() { return true; }")]
    [InlineData("public bool Value() { return false; }")]
    [InlineData("public bool Value() { if (true) { return false; } else { return true; } }")]
    [InlineData("public int32 Value() { return 42; }")]
    [InlineData("public int32 Value() { return 0xFFFFFFFF; }")]
    [InlineData("public uint32 Value() { return 4294967295; }")]
    [InlineData("public int64 Value() { return 9223372036854775807; }")]
    [InlineData("public uint64 Value() { return 18446744073709551615; }")]
    [InlineData("public char16 Value() { return 0xFFFF; }")]
    [InlineData("public int32 Value() { if (true) { return 0b101010; } else { return 0; } }")]
    [InlineData("public int32 Value() { return -42; }")]
    [InlineData("public int32 Value() { return - /* whitespace */ 42; }")]
    [InlineData("public int32 Value() { return -2147483648; }")]
    [InlineData("public int32 Value() { return -0x80000000; }")]
    [InlineData("public int64 Value() { return -9223372036854775808; }")]
    [InlineData("public int64 Value() { return -0xFFFFFFFFFFFFFFFF; }")]
    [InlineData("public int32 Value(int32 x) { return x + 5; }")]
    [InlineData("public uint32 Value(uint32 x) { return x - 0xFFFFFFFF; }")]
    [InlineData("public int64 Value(int64 x) { return x + 9223372036854775807; }")]
    [InlineData("public uint64 Value(uint64 x) { return x + 18446744073709551615; }")]
    [InlineData("public int32 Value(int32 x, int32 y) { return y - /* gap */ 0b101; }")]
    [InlineData("public int32 Value(int32 x) { if (x < x) { return x + 42; } else { return x - 5; } }")]
    [InlineData("public int32 Value(int32 x) { return 5 + x; }")]
    [InlineData("public uint32 Value(uint32 x) { return 0xFFFFFFFF - x; }")]
    [InlineData("public int64 Value(int64 x) { return 9223372036854775807 - x; }")]
    [InlineData("public uint64 Value(uint64 x) { return 18446744073709551615 - x; }")]
    [InlineData("public int32 Value(int32 x, int32 y) { return 47 - /* gap */ y; }")]
    [InlineData("public int32 Value(int32 x) { return ((x + 5)); }")]
    [InlineData("public bool Value() { if (((true))) { return (false); } else { return true; } }")]
    [InlineData("public bool Value() { return !false; }")]
    [InlineData("public bool Value(bool x) { return !x; }")]
    [InlineData("public bool Value(bool x, bool y) { return !y; }")]
    [InlineData("public int32 Value(bool x) { if (!x) { return 42; } else { return 0; } }")]
    [InlineData("public bool Value(int32 x) { return x >= 42; }")]
    [InlineData("public bool Value(uint64 x) { return x == 18446744073709551615; }")]
    [InlineData("public bool Value(char16 x) { return x != 0xFFFF; }")]
    [InlineData("public bool Value(int32 x) { return 42 < x; }")]
    [InlineData("public bool Value(char16 x) { return 0xFFFF != x; }")]
    [InlineData("public int32 Value(int32 x) { if (x < 42) { return x; } else { return 42; } }")]
    [InlineData("public int32 Value(int32 x, int32 y) { return x * y; }")]
    [InlineData("public uint64 Value(uint64 x, uint64 y) { return x / y; }")]
    [InlineData("public int32 Value(int32 x, int32 y) { return x % y; }")]
    [InlineData("public int32 Value(int32 x) { return x * 7; }")]
    [InlineData("public uint32 Value(uint32 x) { return x / 0xFFFF; }")]
    [InlineData("public int64 Value(int64 x) { return x % 9223372036854775807; }")]
    [InlineData("public uint64 Value(uint64 x) { return 7 * x; }")]
    [InlineData("public int32 Value(int32 x) { return 42 / x; }")]
    [InlineData("public uint64 Value(uint64 x) { return 18446744073709551615 % x; }")]
    [InlineData("public int32 Value(int32 x, int32 y) { return x & y; }")]
    [InlineData("public uint32 Value(uint32 x, uint32 y) { return x | y; }")]
    [InlineData("public int64 Value(int64 x, int64 y) { return x ^ y; }")]
    [InlineData("public uint64 Value(uint64 x) { return x & 0xFF; }")]
    [InlineData("public int32 Value(int32 x) { return x | 0xFFFFFFFF; }")]
    [InlineData("public uint64 Value(uint64 x) { return x ^ 0xFFFFFFFFFFFFFFFF; }")]
    [InlineData("public int32 Value(int32 x) { return 0xFF & x; }")]
    [InlineData("public uint64 Value(uint64 x) { return 18446744073709551615 | x; }")]
    [InlineData("public uint32 Value(uint32 x) { return 0b101010 ^ x; }")]
    [InlineData("public int32 Value(int32 x) { return ~x; }")]
    [InlineData("public uint32 Value(uint32 x, uint32 y) { return ~y; }")]
    [InlineData("public int64 Value(int64 x) { return ~x; }")]
    [InlineData("public uint64 Value(uint64 x) { return ~x; }")]
    [InlineData("public int32 Value() { return ~0xFFFFFFD5; }")]
    [InlineData("public uint32 Value() { return ~4294967295; }")]
    [InlineData("public int64 Value() { return ~0xFFFFFFFFFFFFFFFF; }")]
    [InlineData("public uint64 Value() { return ~0xFFFFFFFFFFFFFFFF; }")]
    [InlineData("public bool Value(bool x, bool y) { return x && y; }")]
    [InlineData("public bool Value(bool x, bool y) { return x || y; }")]
    [InlineData("public bool Value(bool x, bool y) { return x & y; }")]
    [InlineData("public bool Value(bool x, bool y) { return x | y; }")]
    [InlineData("public bool Value(bool x, bool y) { return x ^ y; }")]
    [InlineData("public int32 Value(bool x, bool y) { if (x && y) { return 42; } else { return 0; } }")]
    [InlineData("public int32 Value(int32 x) { return -x; }")]
    [InlineData("public int64 Value(int64 x, int64 y) { return -y; }")]
    [InlineData("public int32 Value(int32 x) { return +x; }")]
    [InlineData("public uint32 Value(uint32 x) { return +x; }")]
    [InlineData("public int64 Value(int64 x) { return +x; }")]
    [InlineData("public uint64 Value(uint64 x) { return +x; }")]
    [InlineData("public int32 Value() { return 20 + 22; }")]
    [InlineData("public uint32 Value() { return 0xFFFFFFFF & 42; }")]
    [InlineData("public int32 Value() { return 84 / 2; }")]
    [InlineData("public uint32 Value() { return 0b101010 ^ 0; }")]
    [InlineData("public int32 Value() { return +42; }")]
    [InlineData("public int32 Value() { return +0xFFFFFFFF; }")]
    [InlineData("public uint32 Value() { return +4294967295; }")]
    [InlineData("public int64 Value() { return +9223372036854775807; }")]
    [InlineData("public uint64 Value() { return +18446744073709551615; }")]
    [InlineData("public int32 Value(bool x) { if (x) { return 42; } return 0; }")]
    [InlineData("public int32 Value(int32 x) { if (x < 42) { return x; } return 42; }")]
    public async Task FrontendBuiltLibraryMatchesHostedNativeBytesAsync(string source)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-native-parity-{Guid.NewGuid():N}");
        var nativePath = prefix + ".native.dll";
        var hostedPath = prefix + ".hosted.dll";
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, _, nativeError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, _, hostedError) = await RunAsync(input, "build-library",
                    "-", "--export", "Value", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.Equal(0, nativeExit);
                Assert.Equal(0, hostedExit);
                Assert.Empty(nativeError);
                Assert.Empty(hostedError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                    await File.ReadAllBytesAsync(nativePath));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task FrontendBuiltLibraryExportsPublicScalarAndPreservesOutputOnFailureAsync(
        string policy)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var outputPath = Path.Combine(Path.GetTempPath(), $"terrum-native-frontend-{Guid.NewGuid():N}.dll");
        try
        {
            var (exit, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("public int32 Value(int32 x, int32 y) { return x + y; }"),
                "frontend-build-library", directory, "-", "-o", outputPath, "--runtime", policy, "--json");
            Assert.Equal(0, exit);
            Assert.Empty(error);
            using (var document = JsonDocument.Parse(output))
            {
                Assert.True(document.RootElement.GetProperty("success").GetBoolean());
                Assert.True(document.RootElement.GetProperty("result").GetProperty("public").GetBoolean());
                Assert.Equal(outputPath, document.RootElement.GetProperty("output").GetString());
            }
            var image = await File.ReadAllBytesAsync(outputPath);
            var module = NativeLibrary.Load(outputPath);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<ScalarBinary>(
                    NativeLibrary.GetExport(module, "Value"));
                Assert.Equal(42, function(20, 22));
            }
            finally
            {
                NativeLibrary.Free(module);
            }

            var (privateExit, privateOutput, privateError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Value(int32 x, int32 y) { return x + y; }"),
                "frontend-build-library", directory, "-", "-o", outputPath, "--runtime", policy, "--json");
            Assert.Equal(1, privateExit);
            Assert.Empty(privateError);
            using (var document = JsonDocument.Parse(privateOutput))
            {
                Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            }
            Assert.Equal(image, await File.ReadAllBytesAsync(outputPath));

            var (lexExit, lexOutput, lexError) = await RunAsync(
                Encoding.UTF8.GetBytes("public int32 Value(int32 x) { return @; }"),
                "frontend-build-library", directory, "-", "-o", outputPath, "--runtime", policy, "--json");
            Assert.Equal(1, lexExit);
            Assert.Empty(lexError);
            using (var document = JsonDocument.Parse(lexOutput))
            {
                Assert.False(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("TRM0001", document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString());
            }
            Assert.Equal(image, await File.ReadAllBytesAsync(outputPath));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task FrontendComponentPublishesTypedBranchesAndRejectsInvalidShapesAsync(string policy)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        const string Conditional =
            "int32 Value(int32 x, int32 y) { if (x < y) { return x + y; } else { return x - y; } }";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Conditional),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using (var document = JsonDocument.Parse(output))
        {
            var result = document.RootElement;
            Assert.Equal(1, result.GetProperty("schema").GetInt32());
            Assert.Equal("frontend-component", result.GetProperty("command").GetString());
            Assert.True(result.GetProperty("success").GetBoolean());
            Assert.Equal(2, result.GetProperty("result").GetProperty("parameterCount").GetInt32());
            Assert.False(result.GetProperty("result").TryGetProperty("hasFallthrough", out _));
            Assert.Equal(67, result.GetProperty("result").GetProperty("condition")
                .GetProperty("operation").GetInt32());
            Assert.Equal(57, result.GetProperty("result").GetProperty("whenTrue")
                .GetProperty("operation").GetInt32());
            Assert.Equal(58, result.GetProperty("result").GetProperty("whenFalse")
                .GetProperty("operation").GetInt32());
            Assert.Empty(result.GetProperty("diagnostics").EnumerateArray());
        }

        var (invalidExit, invalidOutput, invalidError) = await RunAsync(
            Encoding.UTF8.GetBytes("int32 Value(int32 x) { return y; }"),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.Equal(1, invalidExit);
        Assert.Empty(invalidError);
        using (var document = JsonDocument.Parse(invalidOutput))
        {
            var result = document.RootElement;
            Assert.False(result.GetProperty("success").GetBoolean());
            Assert.Equal(3, result.GetProperty("result").GetProperty("errorCode").GetInt32());
            Assert.Equal(30, result.GetProperty("result").GetProperty("errorSpan")
                .GetProperty("start").GetInt32());
            Assert.Empty(result.GetProperty("diagnostics").EnumerateArray());
        }

        var (lexExit, lexOutput, lexError) = await RunAsync(Encoding.UTF8.GetBytes("@"),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.Equal(1, lexExit);
        Assert.Empty(lexError);
        using (var document = JsonDocument.Parse(lexOutput))
        {
            var result = document.RootElement;
            Assert.False(result.GetProperty("success").GetBoolean());
            Assert.False(result.TryGetProperty("result", out _));
            Assert.Equal("TRM0001", result.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task FrontendComponentPublishesTrailingReturnShapeAsync(string policy)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(
            Encoding.UTF8.GetBytes("int32 Value(bool x) { if (x) { return 42; } return 0; }"),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("hasCondition").GetBoolean());
        Assert.True(result.GetProperty("hasFallthrough").GetBoolean());
        Assert.Equal(0, result.GetProperty("condition").GetProperty("operation").GetInt32());
        Assert.Equal(3, result.GetProperty("whenTrue").GetProperty("operation").GetInt32());
        Assert.Equal(3, result.GetProperty("whenFalse").GetProperty("operation").GetInt32());
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task FrontendComponentRejectsConditionalWithoutFallbackAsync(string policy)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(
            Encoding.UTF8.GetBytes("int32 Value(bool x) { if (x) { return 42; } }"),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("valid").GetBoolean());
        Assert.Equal(1, result.GetProperty("errorCode").GetInt32());
    }

    [Fact]
    public async Task FrontendComponentRejectsMissingSourcesWithoutPublishingAResultAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"terrum-missing-frontend-{Guid.NewGuid():N}");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Value(int32 x) { return x; }"),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(2, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task FrontendComponentRejectsBooleanLiteralWithIncompatibleReturnTypeAsync()
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(
            Encoding.UTF8.GetBytes("public int32 Value() { return true; }"),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("valid").GetBoolean());
        Assert.False(result.GetProperty("public").GetBoolean());
        Assert.Equal(2, result.GetProperty("errorCode").GetInt32());
        Assert.Equal(30, result.GetProperty("errorSpan").GetProperty("start").GetInt32());
    }

    [Theory]
    [InlineData("int32", "2147483648")]
    [InlineData("int32", "0x100000000")]
    [InlineData("uint32", "4294967296")]
    [InlineData("int64", "9223372036854775808")]
    [InlineData("uint64", "18446744073709551616")]
    [InlineData("char16", "65536")]
    [InlineData("bool", "1")]
    [InlineData("int32", "-2147483649")]
    [InlineData("int64", "-9223372036854775809")]
    [InlineData("uint32", "-1")]
    public async Task FrontendComponentRejectsOutOfRangeIntegerAsync(string type, string literal)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var source = $"public {type} Value() {{ return {literal}; }}";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("valid").GetBoolean());
        Assert.Equal(2, result.GetProperty("errorCode").GetInt32());
        Assert.Equal(source.IndexOf(literal, StringComparison.Ordinal),
            result.GetProperty("errorSpan").GetProperty("start").GetInt32());
    }

    [Theory]
    [InlineData("int32 Value(int32 x) { return x + 2147483648; }", "2147483648")]
    [InlineData("int32 Value(int32 x) { return 2147483648 + x; }", "2147483648")]
    [InlineData("bool Value(int32 x) { return x < 2147483648; }", "2147483648")]
    [InlineData("bool Value(int32 x) { return 2147483648 < x; }", "2147483648")]
    [InlineData("int32 Value(int32 x) { return x * 2147483648; }", "2147483648")]
    [InlineData("int32 Value(int32 x) { return 2147483648 / x; }", "2147483648")]
    [InlineData("uint32 Value(uint32 x) { return x & 4294967296; }", "4294967296")]
    [InlineData("uint32 Value(uint32 x) { return 4294967296 | x; }", "4294967296")]
    [InlineData("int32 Value() { return ~2147483648; }", "2147483648")]
    [InlineData("int32 Value() { return 1 + 2147483648; }", "2147483648")]
    [InlineData("int32 Value() { return +2147483648; }", "2147483648")]
    public async Task FrontendComponentRejectsOutOfRangeBinaryLiteralAsync(string source,
        string invalidLiteral)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("valid").GetBoolean());
        Assert.Equal(2, result.GetProperty("errorCode").GetInt32());
        Assert.Equal(source.IndexOf(invalidLiteral, StringComparison.Ordinal),
            result.GetProperty("errorSpan").GetProperty("start").GetInt32());
    }

    [Theory]
    [InlineData("char16 Value(char16 x) { return x & 42; }")]
    [InlineData("char16 Value(char16 x) { return 42 | x; }")]
    [InlineData("char16 Value(char16 x) { return x + 42; }")]
    [InlineData("char16 Value(char16 x) { return ~x; }")]
    [InlineData("char16 Value(char16 x, char16 y) { return x & y; }")]
    [InlineData("int32 Value(int32 x, int32 y) { return x && y; }")]
    [InlineData("uint32 Value(uint32 x) { return -x; }")]
    [InlineData("bool Value(bool x) { return +x; }")]
    [InlineData("char16 Value() { return +42; }")]
    public async Task FrontendComponentRejectsUnsupportedScalarLiteralOperatorAsync(string source)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("valid").GetBoolean());
        Assert.Equal(2, result.GetProperty("errorCode").GetInt32());
    }

    [Theory]
    [InlineData("int32 Main() { return (42; }", ";")]
    [InlineData("int32 Main() { return ((42); }", ";")]
    [InlineData("int32 Main() { return (42)); }", ")")]
    public async Task FrontendComponentRejectsUnbalancedExpressionGroupAsync(
        string source, string offending)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("valid").GetBoolean());
        Assert.Equal(1, result.GetProperty("errorCode").GetInt32());
        Assert.Equal(source.LastIndexOf(offending, StringComparison.Ordinal),
            result.GetProperty("errorSpan").GetProperty("start").GetInt32());
    }

    [Theory]
    [InlineData("bool Value(int32 x) { return !x; }", 2)]
    [InlineData("bool Value(bool x) { return !missing; }", 3)]
    public async Task FrontendComponentRejectsInvalidBooleanNegationAsync(
        string source, int expectedError)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("valid").GetBoolean());
        Assert.Equal(expectedError, result.GetProperty("errorCode").GetInt32());
    }
}
