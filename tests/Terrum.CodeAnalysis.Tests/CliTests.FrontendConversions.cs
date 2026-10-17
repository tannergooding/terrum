// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("int32 Main() => int32(uint64(42));",22)]
    [TestCase("int32 Main() { var value = uint64(42); return int32(value); }",23)]
    public async Task FrontendConversionProjectionVersionMatchesItsRecordsAsync(string source, int version)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", SelfHostPaths.SourceDirectory(root), "-",
            "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("result")
            .GetProperty("projectionVersion").GetInt32(), Is.Not.EqualTo(version));
        Assert.That(document.RootElement.GetProperty("result")
            .GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
    }

    [Test]
    public async Task FrontendConversionsMatchHostedLibrariesForEveryNumericPairAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = SelfHostPaths.SourceDirectory(root);
        var outputDirectory = TestArtifactPaths.CreateDirectory("conversion-artifacts");
        try
        {
            var types = new[] { "int32", "uint32", "int64", "uint64", "char16" };
            var conversions = new List<(string SourceType, string TargetType, string Name)>();
            foreach (var sourceType in types)
            {
                foreach (var targetType in types)
                {
                    conversions.Add((sourceType, targetType,
                        $"Value_{sourceType}_to_{targetType}"));
                }
            }

            var functions = new string[conversions.Count];
            for (var index = 0; index < conversions.Count; index++)
            {
                var conversion = conversions[index];
                functions[index] =
                    $"public {conversion.TargetType} {conversion.Name}({conversion.SourceType} x) "
                    + $"=> {conversion.TargetType}(x);";
            }

            var source = Encoding.UTF8.GetBytes(string.Join(" ", functions));
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var nativePath = Path.Combine(outputDirectory, "native.dll");
                var hostedPath = Path.Combine(outputDirectory, "hosted.dll");
                var (nativeExit, _, nativeError) = await RunAsync(source,
                    "frontend-build-library", directory, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var hostedArguments = new List<string> { "build-library", "-" };
                foreach (var conversion in conversions)
                {
                    hostedArguments.Add("--export");
                    hostedArguments.Add(conversion.Name);
                }
                hostedArguments.Add("-o");
                hostedArguments.Add(hostedPath);
                hostedArguments.Add("--runtime");
                hostedArguments.Add(policy);
                hostedArguments.Add("--json");
                var (hostedExit, _, hostedError) = await RunAsync(source, [.. hostedArguments]);
                Assert.That(nativeExit, Is.EqualTo(0));
                Assert.That(hostedExit, Is.EqualTo(0));
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(outputDirectory).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Main() => int32(uint64(42));")]
    [TestCase("int32 Main() => -(int32(0xFFFFFFD6));")]
    [TestCase("int32 Main() => int32(-2147483648 + int32(2147483647)) + 43;")]
    [TestCase("int32 Main() { mut var x = uint64(40); x = uint64(int32(x) + 2); return int32(x); }")]
    [TestCase("int32 Main() { if (int32(uint64(42)) == 42) { return int32(char16(42)); } return 0; }")]
    [TestCase("int32 Main() => int32(uint32(char16(int64(42))));")]
    [TestCase("int32 Main() => int32(char16(0xD800)) - 55254;")]
    [TestCase("int32 Main() => int32(int64(int32(0x80000000)) + 2147483690);")]
    [TestCase("int32 Main() => int32(1 + uint64(41));")]
    [TestCase("int32 Main() { var int32 = int32(42); return int32; }")]
    public async Task FrontendConversionExecutablesAndJitMatchHostedAsync(string text)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = SelfHostPaths.SourceDirectory(root);
        var outputDirectory = TestArtifactPaths.CreateDirectory("conversion-artifacts");
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

                var (nativeRun, nativeOutput, nativeRunError) = await RunAsync(source,
                    "frontend-run", directory, "-", "--runtime", policy, "--json");
                var (hostedRun, hostedOutput, hostedRunError) = await RunAsync(source,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.That(hostedRun, Is.EqualTo(42));
                Assert.That(nativeRun, Is.EqualTo(hostedRun));
                Assert.That(nativeOutput, Is.Empty);
                Assert.That(hostedOutput, Is.Empty);
                Assert.That(nativeRunError, Is.Empty);
                Assert.That(hostedRunError, Is.Empty);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(outputDirectory).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Main() => int32(uint64(18446744073709551615));")]
    [TestCase("int32 Main() => int32(char16(int32(-1)));")]
    [TestCase("int32 Main() => int32(uint32(int32(-1)));")]
    [TestCase("int32 Main() => int32(int64(uint64(9223372036854775808)));")]
    public async Task FrontendCheckedConversionFailuresMatchHostedJitAsync(string text)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = SelfHostPaths.SourceDirectory(root);
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var source = Encoding.UTF8.GetBytes(text);
            var (nativeExit, _, _) = await RunAsync(source,
                "frontend-run", directory, "-", "--runtime", policy, "--json");
            var (hostedExit, _, _) = await RunAsync(source,
                "jit", "-", "--runtime", policy, "--json");
            Assert.That(hostedExit, Is.Not.EqualTo(0));
            Assert.That(nativeExit, Is.EqualTo(hostedExit));
        }
    }
    [TestCase("int32 Main() => int32(2147483648);")]
    [TestCase("int32 Main() => char16(65536);")]
    [TestCase("int32 Main() => int32(true);")]
    [TestCase("int32 Main() => int32(1, 2);")]
    [TestCase("int32 Main() { mut int32 int32 = 0; return int32(1); }")]
    public async Task FrontendConversionInvalidSourceRetainsExecutableAsync(string text)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = SelfHostPaths.SourceDirectory(root);
        var outputDirectory = TestArtifactPaths.CreateDirectory("conversion-artifacts");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var path = Path.Combine(outputDirectory, "native.exe");
                var (initial, _, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() => int32(uint64(42));"),
                    "frontend-build", directory, "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.That(initial, Is.EqualTo(0));
                Assert.That(initialError, Is.Empty);
                var image = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(text),
                    "frontend-build", directory, "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit, Is.EqualTo(1));
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(image));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(outputDirectory).ConfigureAwait(true);
        }
    }
}
