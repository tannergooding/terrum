// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("int32 Main() => int32(uint64(42));", 22)]
    [InlineData("int32 Main() { var value = uint64(42); return int32(value); }", 23)]
    public async Task FrontendConversionProjectionVersionMatchesItsRecordsAsync(string source, int version)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(root, "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.NotEqual(version, document.RootElement.GetProperty("result")
            .GetProperty("projectionVersion").GetInt32());
        Assert.Equal(85, document.RootElement.GetProperty("result")
            .GetProperty("projectionVersion").GetInt32());
    }

    [Fact]
    public async Task FrontendConversionsMatchHostedLibrariesForEveryNumericPairAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(root, "self-host");
        var outputDirectory = Path.Combine(root, "tests", "Terrum.CodeAnalysis.Tests",
            $"conversion-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                foreach (var sourceType in new[] { "int32", "uint32", "int64", "uint64", "char16" })
                {
                    foreach (var targetType in new[] { "int32", "uint32", "int64", "uint64", "char16" })
                    {
                        var source = Encoding.UTF8.GetBytes(
                            $"public {targetType} Value({sourceType} x) => {targetType}(x);");
                        var nativePath = Path.Combine(outputDirectory, "native.dll");
                        var hostedPath = Path.Combine(outputDirectory, "hosted.dll");
                        var (nativeExit, _, nativeError) = await RunAsync(source,
                            "frontend-build-library", directory, "-", "-o", nativePath,
                            "--runtime", policy, "--json");
                        var (hostedExit, _, hostedError) = await RunAsync(source,
                            "build-library", "-", "--export", "Value", "-o", hostedPath,
                            "--runtime", policy, "--json");
                        Assert.Equal(0, nativeExit);
                        Assert.Equal(0, hostedExit);
                        Assert.Empty(nativeError);
                        Assert.Empty(hostedError);
                        Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                            await File.ReadAllBytesAsync(nativePath));
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("int32 Main() => int32(uint64(42));")]
    [InlineData("int32 Main() => -(int32(0xFFFFFFD6));")]
    [InlineData("int32 Main() => int32(-2147483648 + int32(2147483647)) + 43;")]
    [InlineData("int32 Main() { mut var x = uint64(40); x = uint64(int32(x) + 2); return int32(x); }")]
    [InlineData("int32 Main() { if (int32(uint64(42)) == 42) { return int32(char16(42)); } return 0; }")]
    [InlineData("int32 Main() => int32(uint32(char16(int64(42))));")]
    [InlineData("int32 Main() => int32(char16(0xD800)) - 55254;")]
    [InlineData("int32 Main() => int32(int64(int32(0x80000000)) + 2147483690);")]
    [InlineData("int32 Main() => int32(1 + uint64(41));")]
    [InlineData("int32 Main() { var int32 = int32(42); return int32; }")]
    public async Task FrontendConversionExecutablesAndJitMatchHostedAsync(string text)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(root, "self-host");
        var outputDirectory = Path.Combine(root, "tests", "Terrum.CodeAnalysis.Tests",
            $"conversion-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var source = Encoding.UTF8.GetBytes(text);
                var nativePath = Path.Combine(outputDirectory, "native.exe");
                var hostedPath = Path.Combine(outputDirectory, "hosted.exe");
                var (nativeExit, _, nativeError) = await RunAsync(source, "frontend-build",
                    directory, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, _, hostedError) = await RunAsync(source, "build",
                    "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.Equal(0, nativeExit);
                Assert.Equal(0, hostedExit);
                Assert.Empty(nativeError);
                Assert.Empty(hostedError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                    await File.ReadAllBytesAsync(nativePath));
                using var process = Process.Start(new ProcessStartInfo(nativePath) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("Could not start native frontend executable.");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Assert.Equal(42, process.ExitCode);

                var (nativeRun, nativeOutput, nativeRunError) = await RunAsync(source,
                    "frontend-run", directory, "-", "--runtime", policy, "--json");
                var (hostedRun, hostedOutput, hostedRunError) = await RunAsync(source,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.Equal(42, hostedRun);
                Assert.Equal(hostedRun, nativeRun);
                Assert.Empty(nativeOutput);
                Assert.Empty(hostedOutput);
                Assert.Empty(nativeRunError);
                Assert.Empty(hostedRunError);
            }
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("int32 Main() => int32(uint64(18446744073709551615));")]
    [InlineData("int32 Main() => int32(char16(int32(-1)));")]
    [InlineData("int32 Main() => int32(uint32(int32(-1)));")]
    [InlineData("int32 Main() => int32(int64(uint64(9223372036854775808)));")]
    public async Task FrontendCheckedConversionFailuresMatchHostedJitAsync(string text)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(root, "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var source = Encoding.UTF8.GetBytes(text);
            var (nativeExit, _, _) = await RunAsync(source,
                "frontend-run", directory, "-", "--runtime", policy, "--json");
            var (hostedExit, _, _) = await RunAsync(source,
                "jit", "-", "--runtime", policy, "--json");
            Assert.NotEqual(0, hostedExit);
            Assert.Equal(hostedExit, nativeExit);
        }
    }

    [Theory]
    [InlineData("int32 Main() => int32(2147483648);")]
    [InlineData("int32 Main() => char16(65536);")]
    [InlineData("int32 Main() => int32(true);")]
    [InlineData("int32 Main() => int32(1, 2);")]
    [InlineData("int32 Main() { mut int32 int32 = 0; return int32(1); }")]
    public async Task FrontendConversionInvalidSourceRetainsExecutableAsync(string text)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(root, "self-host");
        var outputDirectory = Path.Combine(root, "tests", "Terrum.CodeAnalysis.Tests",
            $"conversion-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var path = Path.Combine(outputDirectory, "native.exe");
                var (initial, _, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() => int32(uint64(42));"),
                    "frontend-build", directory, "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.Equal(0, initial);
                Assert.Empty(initialError);
                var image = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(text),
                    "frontend-build", directory, "-", "-o", path, "--runtime", policy, "--json");
                Assert.Equal(1, exit);
                Assert.Empty(error);
                using var document = JsonDocument.Parse(output);
                Assert.False(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal(image, await File.ReadAllBytesAsync(path));
            }
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }
}
