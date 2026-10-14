// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("public int32 Value() { if (true) { } else { } return 42; }",32)]
    [TestCase("public int32 Value() { if (true) { } return 42; }",32)]
    [TestCase("public int32 Value() { mut int32 x = 1; if (int32(uint64(x)) == 1) { x = 2; } return x; }",33)]
    [TestCase("public int32 Value() { mut int32 x = 1; if (true ? false : true) { x = 2; } else { x = 3; } return x; }",34)]
    [TestCase("public int32 Value() { mut int32 x = 1; if (int32(uint64(x)) == 1 ? true : false) { x = 2; } return x; }",35)]
    [TestCase("public int32 Value() { while (false) { if (true) { } } return 42; }",36)]
    [TestCase("public int32 Value() { mut int32 x = 0; while (x < 1) { if (true) { x = int32(uint64(x + 1)); } } return x; }",37)]
    [TestCase("public int32 Value() { mut int32 x = 0; if (true) { while (x < 1) { x = true ? 1 : 2; } } return x; }",38)]
    [TestCase("public int32 Value() { mut int32 x = 0; while (x < 1) { if (true ? true : false) { x = int32(uint64(x + 1)); } } return x; }",39)]
    [TestCase("public int32 Value() { mut int32 x = 1; if (true) { x = 2; } else { x = 3; } var y = x; return y; }",32)]
    [TestCase("public int32 Value() { mut int32 x = 1; if (true) { if (false) { x = 2; } } else { x = 3; } return x; }",32)]
    [TestCase("public int32 Value() { if (true) { return 42; } else { return 0; } }",0)]
    [TestCase("public int32 Value() { if (true) { return 42; } return 0; }",0)]
    [TestCase("public int32 Value() { if (true && false || true) { return 42; } return 0; }",20)]
    [TestCase("public uint32 Value() { mut uint32 x = 1; if (true) { x = 42; } return x; }",32)]
    [TestCase("public int64 Value() { mut int64 x = 1; if (true) { x = -42; } return x; }",32)]
    [TestCase("public uint64 Value() { mut uint64 x = 1; if (true) { x = 42; } return x; }",32)]
    [TestCase("public char16 Value() { mut char16 x = 1; if (true) { x = 42; } return x; }",32)]
    [TestCase("public bool Value() { mut bool x = false; if (true) { x = true; } return x; }",32)]
    public async Task FrontendBranchLibraryMatchesHostedBytesAsync(string source, int version)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"branch-{Guid.NewGuid():N}.native.dll");
        var hostedPath = Path.Combine(root, $"branch-{Guid.NewGuid():N}.hosted.dll");
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
                var projected = document.RootElement.GetProperty("result");
                if (version == 0)
                {
                    Assert.That(projected.TryGetProperty("projectionVersion", out _), Is.False);
                }
                else
                {
                    Assert.That(projected.GetProperty("projectionVersion").GetInt32(), Is.Not.EqualTo(version));
                    Assert.That(projected.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                }
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
    [TestCase("int32 Main() { if (true) { } else { } return 42; }",42)]
    [TestCase("int32 Main() { mut int32 x = 0; if (false) { x = 1; } return x + 42; }",42)]
    [TestCase("int32 Main() { mut int32 x = 0; if (true) { if (false) { x = 1; } else { x = 42; } } return x; }",42)]
    [TestCase("int32 Main() { mut int32 x = 42; if (true) { if (false) { x = 1; } } else { x = 2; } return x; }",42)]
    [TestCase("int32 Main() { mut int32 x = 0; if (false) { if (true) { x = 1; } } else { if (true) { x = 42; } } return x; }",42)]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 3) { if (x == 2) { x = 42; } else { x = x + 1; } } return x; }",42)]
    [TestCase("int32 Main() { mut int32 x = 0; if (true) { while (x < 2) { x = x + 1; } } else { x = 1; } var y = x; return y + 40; }",42)]
    [TestCase("int32 Main() { mut int32 x = 0; if (true) { x = 42; } else { x = x / 0; } return x; }",42)]
    [TestCase("int32 Main() { mut int32 x = 0; if (false) { x = x / 0; } else { x = 42; } return x; }",42)]
    [TestCase("int32 Main() { mut int32 x = 0; if (true ? true : false) { x = int32(uint64(42)); } return x; }",42)]
    [TestCase("int32 Main() { if (true) { return 42; } return 0; }",42)]
    [TestCase("int32 Main() { if (true) { if (true) { if (true) { if (true) { if (true) { if (true) { } } } } } } return 42; }",42)]
    public async Task FrontendBranchEntryMatchesHostedAndExecutesAsync(string source, int expected)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"branch-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(root, $"branch-{Guid.NewGuid():N}.hosted.exe");
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
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(expected));
                var (nativeRun, _, nativeRunError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                var (hostedRun, _, hostedRunError) = await RunAsync(input,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.That(nativeRun, Is.EqualTo(expected));
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
    [TestCase("int32 Main() { mut int32 x = 2147483647; if (true) { x = x + 1; } return x; }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { mut int32 x = 0; if (true) { x = 1 / x; } return x; }",unchecked((int)0xE0540002))]
    public async Task FrontendBranchSelectedCheckedFailureExitsExactlyAsync(string source, int expectedExit)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var path = Path.Combine(root, $"branch-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"frontend-build failed under {policy} with exit {exit}.{Environment.NewLine}{output}{error}");
                Assert.That(error, Is.Empty);
                Assert.That(await ExecuteNativeImageAsync(path), Is.EqualTo(expectedExit));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Main() { if (1) { } return 42; }")]
    [TestCase("int32 Main() { if (true) { mut int32 x = 1; mut int32 x = 2; } return 42; }")]
    [TestCase("int32 Main() { if (true) { return missing; } else { } return 42; }")]
    [TestCase("int32 Main() { if (true) { break; } return 42; }")]
    [TestCase("int32 Main() { if (true) { continue; } return 42; }")]
    [TestCase("int32 Main() { if (true) { } else { return missing; } return 42; }")]
    [TestCase("int32 Main() { mut int32 x = 0; if (true) { x = false; } return x; }")]
    [TestCase("int32 Main() { int32 x = 0; if (true) { x = 1; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; if (true) { x = y; } return x; }")]
    [TestCase("int32 Main() { if (true) { if (true) { if (true) { if (true) { if (true) { if (true) { if (true) { } } } } } } } return 42; }")]
    public async Task FrontendBranchInvalidSourceRetainsOutputAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var path = Path.Combine(root, $"branch-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, _, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { if (true) { } return 42; }"),
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
    public async Task FrontendBranchRespectsStatementAndNodeLimitsAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var sources = new[] {
            "int32 Main() { mut int32 x = 0; if (true) { "
                + string.Concat(Enumerable.Repeat("x = x + 1; ", 12)) + "} return x; }",
            "int32 Main() { mut int32 x = 0; if (true) { x = x"
                + string.Concat(Enumerable.Repeat(" + 1", 32)) + "; } return x; }",
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
