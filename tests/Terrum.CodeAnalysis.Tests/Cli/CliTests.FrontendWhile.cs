// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
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
    [TestCase("public int32 Value() { while (false) { } return 42; }",28)]
    [TestCase("public int32 Value(int32 n) { mut int32 x = 0; while (x < n) { x = x + 1; x = x + 1; } return x; }",28)]
    [TestCase("public int32 Value(int32 n) { mut int32 x = 0; while (x < n) { x = int32(uint64(x)); x = x + 1; } return x; }",29)]
    [TestCase("public int32 Value(int32 n) { mut int32 x = 0; while (x < n) { x = n > 2 ? x + 1 : x + 2; x = x + 1; } return x; }",30)]
    [TestCase("public int32 Value(int32 n) { mut int32 x = 0; while (x < n ? true : false) { x = int32(uint64(x + 1)); x = x + 1; } return x; }",31)]
    [TestCase("public int32 Value(int32 n) { mut int32 x = 0; while (x < n) { while (x < 1) { x = x + 1; } x = x + 1; } return x; }",28)]
    [TestCase("public int32 Value(int32 n) { mut var x = n; while (x < 2) { x = x + 1; } return x; }",28)]
    public async Task FrontendWhileLibraryMatchesHostedBytesAsync(string source, int version)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var nativePath = CreateTestArtifactPath($"while-{Guid.NewGuid():N}.native.dll");
        var hostedPath = CreateTestArtifactPath($"while-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (projectionExit, projectionOutput, projectionError) = await RunAsync(
                    input, "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.That(projectionExit, Is.EqualTo(0));
                Assert.That(projectionError, Is.Empty);
                using var document = JsonDocument.Parse(projectionOutput);
                Assert.That(document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32(), Is.Not.EqualTo(version));
                Assert.That(document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                var statements = document.RootElement.GetProperty("result").GetProperty("statements");
                Assert.That(global::System.Linq.Enumerable.Any(statements.EnumerateArray(),entry => entry.GetProperty("kind").GetInt32() == 3),Is.True);

                var (nativeExit, _, nativeError) = await RunAsync(input,
                    "frontend-build-library", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, _, hostedError) = await RunAsync(input,
                    "build-library", "-", "--export", "Value", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit, Is.EqualTo(0));
                Assert.That(hostedExit, Is.EqualTo(0));
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
    [TestCase("int32 Main() { while (false) { } return 42; }")]
    [TestCase("int32 Main() { mut int32 x = 42; while (x < 0) { x = x + 1; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 42) { x = x + 1; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; mut int32 y = 0; while (x < 6) { y = y + x; x = x + 1; } return y + 27; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 42) { while (x < 2) { x = x + 1; } x = x + 1; x = x + 1; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 42 ? true : false) { x = int32(uint64(x + 1)); x = x + 1; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 42) { x = x + 1; } if (x == 42) { return x; } return 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 42) { x = x + 1; } var y = x; return y; }")]
    [TestCase("int32 Main() { mut int32 x = 42; while (false) { while (false) { while (false) { while (false) { while (false) { while (false) { } } } } } } return x; }")]
    public async Task FrontendWhileEntryMatchesHostedAndExecutesAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var nativePath = CreateTestArtifactPath($"while-{Guid.NewGuid():N}.native.exe");
        var hostedPath = CreateTestArtifactPath($"while-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, _, nativeError) = await RunAsync(input,
                    "frontend-build", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, _, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit, Is.EqualTo(0));
                Assert.That(hostedExit, Is.EqualTo(0));
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                using var process = Process.Start(new ProcessStartInfo(nativePath) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("Could not start frontend executable.");
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                }
                catch (TimeoutException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                    throw;
                }
                Assert.That(process.ExitCode, Is.EqualTo(42));
                var (nativeRun, _, nativeRunError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                var (hostedRun, _, hostedRunError) = await RunAsync(input,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.That(nativeRun, Is.EqualTo(42));
                Assert.That(nativeRun, Is.EqualTo(hostedRun));
                Assert.That(nativeRunError, Is.Empty);
                Assert.That(hostedRunError, Is.Empty);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            File.Delete(hostedPath);
        }
    }

    [Test]
    public async Task FrontendWhileCheckedOverflowFailsAtRuntimeAsync()
    {
        const string source = "int32 Main() { mut int32 x = 2147483647; while (x > 0) { x = x + 1; } return x; }";
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var path = CreateTestArtifactPath($"while-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (buildExit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(buildExit, Is.EqualTo(0));
                Assert.That(error, Is.Empty);
                using var process = Process.Start(new ProcessStartInfo(path) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("Could not start frontend executable.");
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                }
                catch (TimeoutException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                    throw;
                }
                Assert.That(process.ExitCode, Is.EqualTo(unchecked((int)0xE0540001)));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Main() { int32 x = 0; while (x < 2) { x = x + 1; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { int32 y = y; x = y + 1; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = x; while (x < 2) { x = x + 1; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { x = y; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x + 1) { x = x + 1; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { x = true; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { return missing; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { break outer; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { continue outer; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { if (true) { mut int32 x = 1; } } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { x = x + 1; } mut var x = 3; return x; }")]
    public async Task FrontendWhileInvalidSourceRetainsOutputAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var path = CreateTestArtifactPath($"while-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, _, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { mut int32 x = 0; while (x < 42) { x = x + 1; } return x; }"),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(initial, Is.EqualTo(0));
                Assert.That(initialError, Is.Empty);
                var original = await File.ReadAllBytesAsync(path);
                var (failure, output, failureError) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(failure, Is.EqualTo(1));
                Assert.That(failureError, Is.Empty);
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
    public async Task FrontendWhileRespectsStatementAndTreeLimitsAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var sources = new[] {
            "int32 Main() { mut int32 x = 0; while (x < 1) { "
                + string.Concat(Enumerable.Repeat("x = x + 1; ", 12)) + "} return x; }",
            "int32 Main() { mut int32 x = 0; while (x < 1) { x = x"
                + string.Concat(Enumerable.Repeat(" + 1", 32)) + "; } return x; }",
            "int32 Main() { mut int32 x = 0; "
                + string.Concat(Enumerable.Repeat("while (false) { ", 7))
                + string.Concat(Enumerable.Repeat(" }", 7)) + " return x; }",
        };
        foreach (var source in sources)
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", frontend, "-", "--runtime", "fail-fast", "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
        }
    }
}
