// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("public int32 Value() { while (true) { break; } return 42; }", 40)]
    [InlineData("public int32 Value() { while (int32(uint64(0)) < 1) { break; } return 42; }", 41)]
    [InlineData("public int32 Value() { while (true ? true : false) { break; } return 42; }", 42)]
    [InlineData("public int32 Value() { while (int32(uint64(0)) < 1 ? true : false) { break; } return 42; }", 43)]
    [InlineData("public int32 Value() { while (true) { if (true) { break; } } return 42; }", 44)]
    [InlineData("public int32 Value() { if (true) { while (int32(uint64(0)) < 1) { break; } } return 42; }", 45)]
    [InlineData("public int32 Value() { while (true) { if (true ? true : false) { break; } } return 42; }", 46)]
    [InlineData("public int32 Value() { while (true) { if (int32(uint64(0)) < 1 ? true : false) { break; } } return 42; }", 47)]
    public async Task FrontendTransferVersionsMatchHostedDllBytesAsync(string source, int version)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"transfer-{Guid.NewGuid():N}.native.dll");
        var hostedPath = Path.Combine(root, $"transfer-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (projectionExit, projectionOutput, projectionError) = await RunAsync(
                    input, "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.True(projectionExit == 0,
                    $"frontend-component {policy} exited {projectionExit}: {projectionOutput}{projectionError}");
                Assert.Empty(projectionError);
                using var document = JsonDocument.Parse(projectionOutput);
                var projected = document.RootElement.GetProperty("result");
                Assert.NotEqual(version, projected.GetProperty("projectionVersion").GetInt32());
                Assert.Equal(87, projected.GetProperty("projectionVersion").GetInt32());
                var statements = projected.GetProperty("statements");
                Assert.Contains(statements.EnumerateArray(), entry => entry.GetProperty("kind").GetInt32() == 3);
                Assert.Contains(statements.EnumerateArray(), entry => entry.GetProperty("kind").GetInt32() is 6 or 7);
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build-library", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build-library", "-", "--export", "Value", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.True(nativeExit == 0,
                    $"frontend-build-library {policy} exited {nativeExit}: {nativeOutput}{nativeError}");
                Assert.True(hostedExit == 0,
                    $"build-library {policy} exited {hostedExit}: {hostedOutput}{hostedError}");
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
    [InlineData("int32 Main() { while (false) { break; } return 42; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 3) { x = x + 1; continue; x = x / 0; } return x + 39; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 3) { x = x + 1; if (x < 3) { continue; } } return x + 39; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; while (true) { break; x = x / 0; } return 42; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 2147483647; while (true) { break; x = x + 1; } return 42; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 3) { while (true) { break; } x = x + 1; if (x == 2) { continue; } } return x + 39; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 3) { x = x + 1; while (x < 2) { x = x + 1; continue; } if (x == 2) { break; } } return x + 40; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 2) { if (x == 0) { if (false) { break; } else { x = x + 1; continue; } } else { break; } } return x + 41; }", 42)]
    [InlineData("int32 Main() { if (true) { while (true) { break; } } return 42; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; if (false) { while (true) { x = x / 0; break; } } return x + 42; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; while (true ? true : false) { x = false ? x / 0 : x + 42; break; } return x; }", 42)]
    public async Task FrontendTransfersMatchHostedImageAndExecuteAsync(string source, int expected)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"transfer-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(root, $"transfer-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.True(nativeExit == 0,
                    $"frontend-build {policy} exited {nativeExit}: {nativeOutput}{nativeError}");
                Assert.True(hostedExit == 0,
                    $"build {policy} exited {hostedExit}: {hostedOutput}{hostedError}");
                Assert.Empty(nativeError);
                Assert.Empty(hostedError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath), await File.ReadAllBytesAsync(nativePath));
                Assert.Equal(expected, await ExecuteNativeImageAsync(nativePath));
                var (nativeRun, nativeRunOutput, nativeRunError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                var (hostedRun, hostedRunOutput, hostedRunError) = await RunAsync(input,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.True(nativeRun == expected,
                    $"frontend-run {policy} exited {nativeRun}: {nativeRunOutput}{nativeRunError}");
                Assert.True(hostedRun == expected,
                    $"jit {policy} exited {hostedRun}: {hostedRunOutput}{hostedRunError}");
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("int32 Main() { mut int32 x = 2147483647; while (x > 0) { x = x + 1; continue; } return x; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { mut int32 x = 2147483646; while (x + 1 > 0) { x = 2147483647; continue; } return x; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { mut int32 x = 0; while (true) { if (true) { x = 1 / x; } break; } return x; }", unchecked((int)0xE0540002))]
    public async Task FrontendTransfersPreserveSelectedCheckedFailuresAsync(string source, int expectedExit)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var path = Path.Combine(root, $"transfer-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.True(exit == 0,
                    $"frontend-build {policy} exited {exit}: {output}{error}");
                Assert.Empty(error);
                Assert.Equal(expectedExit, await ExecuteNativeImageAsync(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("int32 Main() { break; while (true) { } return 42; }")]
    [InlineData("int32 Main() { continue; while (true) { } return 42; }")]
    [InlineData("int32 Main() { while (true) { break outer; } return 42; }")]
    [InlineData("int32 Main() { while (true) { continue outer; } return 42; }")]
    [InlineData("int32 Main() { while (true) { return missing; } return 0; }")]
    [InlineData("int32 Main() { while (true) { break; } continue; return 42; }")]
    public async Task FrontendInvalidTransfersRetainBuiltOutputAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var path = Path.Combine(root, $"transfer-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { while (true) { break; } return 42; }"),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.True(initial == 0,
                    $"frontend-build {policy} exited {initial}: {initialOutput}{initialError}");
                Assert.Empty(initialError);
                var original = await File.ReadAllBytesAsync(path);
                var (failure, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.True(failure == 1,
                    $"frontend-build {policy} exited {failure}: {output}{error}");
                Assert.Empty(error);
                using var document = JsonDocument.Parse(output);
                Assert.False(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal(original, await File.ReadAllBytesAsync(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FrontendTransfersCountTowardExistingStatementLimitAsync()
    {
        var source = "int32 Main() { while (false) { "
            + string.Concat(Enumerable.Repeat("break; ", 12)) + "} return 42; }";
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var maximum = "int32 Main() { while (false) { "
                + string.Concat(Enumerable.Repeat("break; ", 11)) + "} return 42; }";
            var (accepted, acceptedOutput, acceptedError) = await RunAsync(Encoding.UTF8.GetBytes(maximum),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(accepted == 0,
                $"frontend-component {policy} exited {accepted}: {acceptedOutput}{acceptedError}");
            Assert.Empty(acceptedError);
            using var acceptedDocument = JsonDocument.Parse(acceptedOutput);
            Assert.Equal(12, acceptedDocument.RootElement.GetProperty("result")
                .GetProperty("statements").GetArrayLength());
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(exit == 1,
                $"frontend-component {policy} exited {exit}: {output}{error}");
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        }
    }
}
