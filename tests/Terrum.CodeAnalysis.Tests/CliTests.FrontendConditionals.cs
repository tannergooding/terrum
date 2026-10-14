// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("public int32 Value(bool b, int32 x) => b ? x : 42;",24)]
    [TestCase("public uint32 Value(bool b, uint32 x) => b ? 42 : x;",24)]
    [TestCase("public int64 Value(bool b, int64 x) => b ? x : 42;",24)]
    [TestCase("public uint64 Value(bool b, uint64 x) => b ? 42 : x;",24)]
    [TestCase("public char16 Value(bool b, char16 x) => b ? x : 42;",24)]
    [TestCase("public bool Value(bool b, bool x) => b ? x : false;",24)]
    [TestCase("public bool Value(bool b, int32 x) => (b ? 1 : 2) == x;",24)]
    [TestCase("public int32 Value(bool b) => b ? 1 : 42;",24)]
    [TestCase("public int32 Value(bool b, int32 y) { var x = b ? 1 : y; return x; }",25)]
    [TestCase("public int32 Value(bool b) => int32(b ? uint64(42) : uint64(3));",26)]
    [TestCase("public uint64 Value(bool b) => uint64(b ? 42 : 3);",26)]
    [TestCase("public uint64 Value(int32 x) => uint64(x > 0 ? 42 : 3);",26)]
    [TestCase("public int64 Value(int32 x) => int64((x > 0) ? 1 : 2);",26)]
    [TestCase("public int64 Value(int64 x) => int64((int32(x) > 0) ? 1 : 2);",26)]
    [TestCase("public int64 Value(int32 x) => int64((x > 0) ? (1 + 2) : (3 * 4));",26)]
    [TestCase("public int32 Value(bool b) { var x = b ? uint64(42) : uint64(3); return int32(x); }",27)]
    [TestCase("public int32 Value(bool b, int32 x) => b ? (x + 1) : x * 2;",24)]
    public async Task FrontendConditionalLibraryMatchesHostedBytesAsync(string source, int version)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"conditional-{Guid.NewGuid():N}.native.dll");
        var hostedPath = Path.Combine(root, $"conditional-{Guid.NewGuid():N}.hosted.dll");
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

                var (nativeExit, _, nativeError) = await RunAsync(input,
                    "frontend-build-library", frontend, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedExit, _, hostedError) = await RunAsync(input,
                    "build-library", "-", "--export", "Value", "-o", hostedPath,
                    "--runtime", policy, "--json");
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
    [TestCase("int32 Main() { int32 x = 0; return true ? 42 : 1 / x; }")]
    [TestCase("int32 Main() { int32 x = 0; return false ? 1 / x : 42; }")]
    [TestCase("int32 Main() { int32 x = 2147483647; return false ? x + 1 : 42; }")]
    [TestCase("int32 Main() { uint64 x = 18446744073709551615; return true ? 42 : int32(x); }")]
    [TestCase("int32 Main() => true ? 42 : false ? 1 : 3;")]
    [TestCase("int32 Main() => false ? 1 : true ? 42 : 2;")]
    [TestCase("int32 Main() => (true ? 20 : 1) + (false ? 1 : 22);")]
    [TestCase("int32 Main() { mut int32 x = true ? 1 : 2; x = false ? 1 : 42; return x; }")]
    [TestCase("int32 Main() { if (true ? false : true) { return 1; } return true ? 42 : 1; }")]
    public async Task FrontendConditionalEntryExecutesSelectedArmAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"conditional-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(root, $"conditional-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, _, nativeError) = await RunAsync(input,
                    "frontend-build", frontend, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
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
                }) ?? throw new InvalidOperationException("Could not start native frontend executable.");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
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
    [TestCase("int32 Main() => 1 ? 42 : 0;")]
    [TestCase("int32 Main() => true ? 42 : false;")]
    [TestCase("int32 Main() => true ? 42 : missing;")]
    [TestCase("int32 Main() => true ? 42 : 1 + unknown;")]
    [TestCase("int32 Main() => true ? 42 : int32(uint64(1)) + false;")]
    [TestCase("int32 Main() => true ? int32(42) : uint64(3);")]
    [TestCase("int32 Main() => int32((1 < 2) ? 42 : 0);")]
    [TestCase("int32 Main() { var x = true ? 1 : 2; return x; }")]
    [TestCase("int32 Main() { int32 x = true ? x : 42; return x; }")]
    public async Task FrontendConditionalInvalidSourceRetainsExecutableAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var path = Path.Combine(root, $"conditional-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, _, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() => true ? 42 : 0;"),
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
}
