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
    [InlineData("int32", "42", "43", "44")]
    [InlineData("uint32", "4294967295", "43", "44")]
    [InlineData("int64", "9223372036854775807", "43", "44")]
    [InlineData("uint64", "18446744073709551615", "43", "44")]
    [InlineData("bool", "true", "false", "false")]
    [InlineData("char16", "65", "66", "67")]
    public async Task FrontendStructuredReturnsMatchHostedExportAcrossScalarContextsAsync(
        string type, string selected, string alternate, string fallback)
    {
        var source = $"public {type} Value(bool flag) {{ if (flag) {{ if (true) {{ return {selected}; }}"
            + $" else {{ return {alternate}; }} }} return {fallback}; }}";
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"return-{Guid.NewGuid():N}.native.dll");
        var hostedPath = Path.Combine(root, $"return-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (projectionExit, projectionOutput, projectionError) = await RunAsync(input,
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.True(projectionExit == 0,
                    $"frontend-component {policy} exited {projectionExit}: {projectionOutput}{projectionError}");
                Assert.Empty(projectionError);
                using var document = JsonDocument.Parse(projectionOutput);
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(48, result.GetProperty("projectionVersion").GetInt32());
                Assert.Equal(3, result.GetProperty("statements").EnumerateArray()
                    .Count(statement => statement.GetProperty("kind").GetInt32() == 8));
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
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath), await File.ReadAllBytesAsync(nativePath));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 3) { if (x == 2) { return 42; } x = x + 1; continue; } return 0; }")]
    [InlineData("int32 Main() { mut int32 x = 0; if (true) { while (x < 2) { x = x + 1; if (x == 1) { continue; } if (x == 2) { return 42; } } } return 0; }")]
    [InlineData("int32 Main() { while (true) { if (true) { return 42; } else { break; } } return 0; }")]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 2) { while (true) { break; } x = x + 1; if (x == 1) { continue; } return 42; } return 0; }")]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 1) { x = x + 1; if (true ? true : false) { return int32(uint64(42)); } } return 0; }")]
    [InlineData("int32 Main() { while (true) { if (true) { return 42; } break; } }")]
    [InlineData("int32 Main() { while (true) { while (true) { return 42; } break; } }")]
    [InlineData("int32 Main() { bool flag = false; if (flag || true) { return 42; } }")]
    [InlineData("int32 Main() { bool flag = false; if (!(flag && false)) { return 42; } }")]
    [InlineData("int32 Main() { bool flag = false; while (flag ? true : true) { return 42; } }")]
    public async Task FrontendNestedReturnsMatchHostedExecutableAndJitAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"return-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(root, $"return-{Guid.NewGuid():N}.hosted.exe");
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
                Assert.Equal(42, await ExecuteNativeImageAsync(nativePath));
                var (nativeRun, nativeRunOutput, nativeRunError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                var (hostedRun, hostedRunOutput, hostedRunError) = await RunAsync(input,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.True(nativeRun == 42,
                    $"frontend-run {policy} exited {nativeRun}: {nativeRunOutput}{nativeRunError}");
                Assert.True(hostedRun == 42,
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
    [InlineData("int32 Main() { int32 x = 0; while (false) { if (true) { return 1 / x; } } return 42; }", 42)]
    [InlineData("int32 Main() { int32 x = 0; while (true) { if (true) { return 1 / x; } } }", unchecked((int)0xE0540002))]
    public async Task FrontendStructuredReturnsPreserveLazyFailureAsync(string source, int expected)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var path = Path.Combine(root, $"return-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"frontend-build {policy} exited {exit}: {output}{error}");
                Assert.Empty(error);
                Assert.Equal(expected, await ExecuteNativeImageAsync(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("int32 Main() { bool flag = true; if (flag) { return 42; } }")]
    [InlineData("int32 Main() { while (true) { break; return 42; } }")]
    [InlineData("int32 Main() { while (true) { if (true) { break; } return 42; } }")]
    [InlineData("int32 Main() { while (false) { return 42; } }")]
    [InlineData("int32 Main() { if (true) { return missing; } return 42; }")]
    [InlineData("int32 Main() { return 42; if (false) { return unknown; } }")]
    [InlineData("int32 Main() { while (true) { if (true) { return false; } } }")]
    [InlineData("int32 Main() { if (true) { int32 x = x; return x; } }")]
    public async Task FrontendInvalidStructuredReturnsRetainBuiltOutputAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var path = Path.Combine(root, $"return-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { while (true) { return 42; } }"),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.True(initial == 0,
                    $"frontend-build {policy} exited {initial}: {initialOutput}{initialError}");
                Assert.Empty(initialError);
                var original = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.True(exit == 1, $"frontend-build {policy} exited {exit}: {output}{error}");
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
    public async Task FrontendStructuredReturnsRespectStatementAndNodeBoundsAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var source = "int32 Main() { " + string.Concat(Enumerable.Repeat("return 42; ", 12)) + "}";
            var (accepted, acceptedOutput, acceptedError) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(accepted == 0,
                $"frontend-component {policy} exited {accepted}: {acceptedOutput}{acceptedError}");
            Assert.Empty(acceptedError);
            using var document = JsonDocument.Parse(acceptedOutput);
            Assert.Equal(12, document.RootElement.GetProperty("result")
                .GetProperty("statements").GetArrayLength());
            var excess = "int32 Main() { " + string.Concat(Enumerable.Repeat("return 42; ", 13)) + "}";
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(excess),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(exit == 1, $"frontend-component {policy} exited {exit}: {output}{error}");
            Assert.Empty(error);
            using var failure = JsonDocument.Parse(output);
            Assert.False(failure.RootElement.GetProperty("success").GetBoolean());

            var maximumNodes = "int32 Main() { while (false) { return 0; } return +1"
                + string.Concat(Enumerable.Repeat(" + 1", 14)) + "; }";
            var (nodesExit, nodesOutput, nodesError) = await RunAsync(Encoding.UTF8.GetBytes(maximumNodes),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(nodesExit == 0,
                $"frontend-component {policy} exited {nodesExit}: {nodesOutput}{nodesError}");
            Assert.Empty(nodesError);
            using var nodesDocument = JsonDocument.Parse(nodesOutput);
            Assert.Equal(32, nodesDocument.RootElement.GetProperty("result")
                .GetProperty("nodes").GetArrayLength());
            var tooManyNodes = "int32 Main() { while (false) { return 0; } return +1"
                + string.Concat(Enumerable.Repeat(" + 1", 15)) + "; }";
            var (extraExit, extraOutput, extraError) = await RunAsync(Encoding.UTF8.GetBytes(tooManyNodes),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(extraExit == 1,
                $"frontend-component {policy} exited {extraExit}: {extraOutput}{extraError}");
            Assert.Empty(extraError);
            using var extraDocument = JsonDocument.Parse(extraOutput);
            Assert.False(extraDocument.RootElement.GetProperty("success").GetBoolean());

            var maximumDepth = "int32 Main() { " + string.Concat(Enumerable.Repeat("if (true) { ", 6))
                + "return 42; " + string.Concat(Enumerable.Repeat("} ", 6)) + "}";
            var (depthExit, depthOutput, depthError) = await RunAsync(Encoding.UTF8.GetBytes(maximumDepth),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(depthExit == 0,
                $"frontend-component {policy} exited {depthExit}: {depthOutput}{depthError}");
            Assert.Empty(depthError);
            using var depthDocument = JsonDocument.Parse(depthOutput);
            Assert.Equal(7, depthDocument.RootElement.GetProperty("result")
                .GetProperty("statements").GetArrayLength());
            var excessDepth = "int32 Main() { " + string.Concat(Enumerable.Repeat("if (true) { ", 7))
                + "return 42; " + string.Concat(Enumerable.Repeat("} ", 7)) + "}";
            var (deepExit, deepOutput, deepError) = await RunAsync(Encoding.UTF8.GetBytes(excessDepth),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(deepExit == 1,
                $"frontend-component {policy} exited {deepExit}: {deepOutput}{deepError}");
            Assert.Empty(deepError);
            using var deepDocument = JsonDocument.Parse(deepOutput);
            Assert.False(deepDocument.RootElement.GetProperty("success").GetBoolean());

            string Locals(int count) => "int32 Main() { "
                + string.Concat(Enumerable.Range(1, count).Select(index => $"int32 x{index} = {index}; "))
                + "if (true) { return x1; } }";
            var (localExit, localOutput, localError) = await RunAsync(Encoding.UTF8.GetBytes(Locals(8)),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(localExit == 0,
                $"frontend-component {policy} exited {localExit}: {localOutput}{localError}");
            Assert.Empty(localError);
            using var localDocument = JsonDocument.Parse(localOutput);
            Assert.Equal(8, localDocument.RootElement.GetProperty("result")
                .GetProperty("locals").GetArrayLength());
            var (overflowExit, overflowOutput, overflowError) = await RunAsync(Encoding.UTF8.GetBytes(Locals(9)),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(overflowExit == 1,
                $"frontend-component {policy} exited {overflowExit}: {overflowOutput}{overflowError}");
            Assert.Empty(overflowError);
            using var overflowDocument = JsonDocument.Parse(overflowOutput);
            Assert.False(overflowDocument.RootElement.GetProperty("success").GetBoolean());
        }
    }
}
