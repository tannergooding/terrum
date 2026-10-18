// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [TestCase("int32","42","43","44")]
    [TestCase("uint32","4294967295","43","44")]
    [TestCase("int64","9223372036854775807","43","44")]
    [TestCase("uint64","18446744073709551615","43","44")]
    [TestCase("bool","true","false","false")]
    [TestCase("char16","65","66","67")]
    public async Task FrontendStructuredReturnsMatchHostedExportAcrossScalarContextsAsync(
        string type, string selected, string alternate, string fallback)
    {
        var source = $"public {type} Value(bool flag) {{ if (flag) {{ if (true) {{ return {selected}; }}"
            + $" else {{ return {alternate}; }} }} return {fallback}; }}";
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var nativePath = CreateTestArtifactPath($"return-{Guid.NewGuid():N}.native.dll");
        var hostedPath = CreateTestArtifactPath($"return-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (projectionExit, projectionOutput, projectionError) = await RunAsync(input,
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.That(projectionExit == 0, Is.True, $"frontend-component {policy} exited {projectionExit}: {projectionOutput}{projectionError}");
                Assert.That(projectionError, Is.Empty);
                using var document = JsonDocument.Parse(projectionOutput);
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(result.GetProperty("statements").EnumerateArray()
                    .Count(statement => statement.GetProperty("kind").GetInt32() == 8), Is.EqualTo(3));
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build-library", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build-library", "-", "--export", "Value", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"frontend-build-library {policy} exited {nativeExit}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"build-library {policy} exited {hostedExit}: {hostedOutput}{hostedError}");
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 3) { if (x == 2) { return 42; } x = x + 1; continue; } return 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; if (true) { while (x < 2) { x = x + 1; if (x == 1) { continue; } if (x == 2) { return 42; } } } return 0; }")]
    [TestCase("int32 Main() { while (true) { if (true) { return 42; } else { break; } } return 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { while (true) { break; } x = x + 1; if (x == 1) { continue; } return 42; } return 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 1) { x = x + 1; if (true ? true : false) { return int32(uint64(42)); } } return 0; }")]
    [TestCase("int32 Main() { while (true) { if (true) { return 42; } break; } }")]
    [TestCase("int32 Main() { while (true) { while (true) { return 42; } break; } }")]
    [TestCase("int32 Main() { bool flag = false; if (flag || true) { return 42; } }")]
    [TestCase("int32 Main() { bool flag = false; if (!(flag && false)) { return 42; } }")]
    [TestCase("int32 Main() { bool flag = false; while (flag ? true : true) { return 42; } }")]
    public async Task FrontendNestedReturnsMatchHostedExecutableAndJitAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var nativePath = CreateTestArtifactPath($"return-{Guid.NewGuid():N}.native.exe");
        var hostedPath = CreateTestArtifactPath($"return-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"frontend-build {policy} exited {nativeExit}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"build {policy} exited {hostedExit}: {hostedOutput}{hostedError}");
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(42));
                var (nativeRun, nativeRunOutput, nativeRunError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                var (hostedRun, hostedRunOutput, hostedRunError) = await RunAsync(input,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.That(nativeRun == 42, Is.True, $"frontend-run {policy} exited {nativeRun}: {nativeRunOutput}{nativeRunError}");
                Assert.That(hostedRun == 42, Is.True, $"jit {policy} exited {hostedRun}: {hostedRunOutput}{hostedRunError}");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            File.Delete(hostedPath);
        }
    }
    [TestCase("int32 Main() { int32 x = 0; while (false) { if (true) { return 1 / x; } } return 42; }",42)]
    [TestCase("int32 Main() { int32 x = 0; while (true) { if (true) { return 1 / x; } } }",unchecked((int)0xE0540002))]
    public async Task FrontendStructuredReturnsPreserveLazyFailureAsync(string source, int expected)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var path = CreateTestArtifactPath($"return-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"frontend-build {policy} exited {exit}: {output}{error}");
                Assert.That(error, Is.Empty);
                Assert.That(await ExecuteNativeImageAsync(path), Is.EqualTo(expected));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Main() { bool flag = true; if (flag) { return 42; } }")]
    [TestCase("int32 Main() { while (true) { break; return 42; } }")]
    [TestCase("int32 Main() { while (true) { if (true) { break; } return 42; } }")]
    [TestCase("int32 Main() { while (false) { return 42; } }")]
    [TestCase("int32 Main() { if (true) { return missing; } return 42; }")]
    [TestCase("int32 Main() { return 42; if (false) { return unknown; } }")]
    [TestCase("int32 Main() { while (true) { if (true) { return false; } } }")]
    [TestCase("int32 Main() { if (true) { int32 x = x; return x; } }")]
    public async Task FrontendInvalidStructuredReturnsRetainBuiltOutputAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var path = CreateTestArtifactPath($"return-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { while (true) { return 42; } }"),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(initial == 0, Is.True, $"frontend-build {policy} exited {initial}: {initialOutput}{initialError}");
                Assert.That(initialError, Is.Empty);
                var original = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == 1, Is.True, $"frontend-build {policy} exited {exit}: {output}{error}");
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task FrontendStructuredReturnsRespectStatementAndNodeBoundsAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var source = "int32 Main() { " + string.Concat(Enumerable.Repeat("return 42; ", 12)) + "}";
            var (accepted, acceptedOutput, acceptedError) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.That(accepted == 0, Is.True, $"frontend-component {policy} exited {accepted}: {acceptedOutput}{acceptedError}");
            Assert.That(acceptedError, Is.Empty);
            using var document = JsonDocument.Parse(acceptedOutput);
            Assert.That(document.RootElement.GetProperty("result")
                .GetProperty("statements").GetArrayLength(), Is.EqualTo(12));
            var excess = "int32 Main() { " + string.Concat(Enumerable.Repeat("return 42; ", 13)) + "}";
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(excess),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.That(exit == 1, Is.True, $"frontend-component {policy} exited {exit}: {output}{error}");
            Assert.That(error, Is.Empty);
            using var failure = JsonDocument.Parse(output);
            Assert.That(failure.RootElement.GetProperty("success").GetBoolean(), Is.False);

            var maximumNodes = "int32 Main() { while (false) { return 0; } return +1"
                + string.Concat(Enumerable.Repeat(" + 1", 30)) + "; }";
            var (nodesExit, nodesOutput, nodesError) = await RunAsync(Encoding.UTF8.GetBytes(maximumNodes),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.That(nodesExit == 0, Is.True, $"frontend-component {policy} exited {nodesExit}: {nodesOutput}{nodesError}");
            Assert.That(nodesError, Is.Empty);
            using var nodesDocument = JsonDocument.Parse(nodesOutput);
            Assert.That(nodesDocument.RootElement.GetProperty("result")
                .GetProperty("nodes").GetArrayLength(), Is.EqualTo(64));
            var tooManyNodes = "int32 Main() { while (false) { return 0; } return +1"
                + string.Concat(Enumerable.Repeat(" + 1", 31)) + "; }";
            var (extraExit, extraOutput, extraError) = await RunAsync(Encoding.UTF8.GetBytes(tooManyNodes),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.That(extraExit == 1, Is.True, $"frontend-component {policy} exited {extraExit}: {extraOutput}{extraError}");
            Assert.That(extraError, Is.Empty);
            using var extraDocument = JsonDocument.Parse(extraOutput);
            Assert.That(extraDocument.RootElement.GetProperty("success").GetBoolean(), Is.False);

            var maximumDepth = "int32 Main() { " + string.Concat(Enumerable.Repeat("if (true) { ", 6))
                + "return 42; " + string.Concat(Enumerable.Repeat("} ", 6)) + "}";
            var (depthExit, depthOutput, depthError) = await RunAsync(Encoding.UTF8.GetBytes(maximumDepth),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.That(depthExit == 0, Is.True, $"frontend-component {policy} exited {depthExit}: {depthOutput}{depthError}");
            Assert.That(depthError, Is.Empty);
            using var depthDocument = JsonDocument.Parse(depthOutput);
            Assert.That(depthDocument.RootElement.GetProperty("result")
                .GetProperty("statements").GetArrayLength(), Is.EqualTo(7));
            var excessDepth = "int32 Main() { " + string.Concat(Enumerable.Repeat("if (true) { ", 7))
                + "return 42; " + string.Concat(Enumerable.Repeat("} ", 7)) + "}";
            var (deepExit, deepOutput, deepError) = await RunAsync(Encoding.UTF8.GetBytes(excessDepth),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.That(deepExit == 1, Is.True, $"frontend-component {policy} exited {deepExit}: {deepOutput}{deepError}");
            Assert.That(deepError, Is.Empty);
            using var deepDocument = JsonDocument.Parse(deepOutput);
            Assert.That(deepDocument.RootElement.GetProperty("success").GetBoolean(), Is.False);

            string Locals(int count) => "int32 Main() { "
                + string.Concat(Enumerable.Range(1, count).Select(index => $"int32 x{index} = {index}; "))
                + "if (true) { return x1; } }";
            var (localExit, localOutput, localError) = await RunAsync(Encoding.UTF8.GetBytes(Locals(8)),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.That(localExit == 0, Is.True, $"frontend-component {policy} exited {localExit}: {localOutput}{localError}");
            Assert.That(localError, Is.Empty);
            using var localDocument = JsonDocument.Parse(localOutput);
            Assert.That(localDocument.RootElement.GetProperty("result")
                .GetProperty("locals").GetArrayLength(), Is.EqualTo(8));
            var (overflowExit, overflowOutput, overflowError) = await RunAsync(Encoding.UTF8.GetBytes(Locals(9)),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.That(overflowExit == 1, Is.True, $"frontend-component {policy} exited {overflowExit}: {overflowOutput}{overflowError}");
            Assert.That(overflowError, Is.Empty);
            using var overflowDocument = JsonDocument.Parse(overflowOutput);
            Assert.That(overflowDocument.RootElement.GetProperty("success").GetBoolean(), Is.False);
        }
    }
}
