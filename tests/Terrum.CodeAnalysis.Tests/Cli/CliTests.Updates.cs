// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public async Task UpdatesMatchCompiledScannerAndExecuteAllIntegerWidthsAsync(string type)
    {
        var source = $$"""
            [ConstEval] int32 Offset() {
                mut int32 count = 0;
                for (mut int32 i = 0; i < 2; i++) { count++; }
                return count;
            }
            int32 Main() {
                mut {{type}} value = {{type}}(40);
                {{type}} before = value++;
                {{type}} after = ++value;
                if (before != 40 || after != 42 || value != 42) { return 1; }
                if (value-- != 42 || --value != 40) { return 2; }
                const int32 offset = Offset();
                return int32(value) + offset;
            }
            """;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(TestArtifactPaths.DirectoryPath, $"updates-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var hosted = Path.Combine(directory, "hosted.exe");
                var scanned = Path.Combine(directory, "scanned.exe");
                var input = Encoding.UTF8.GetBytes(source);
                var (exit, output, error) = await RunAsync(input, "build", "-", "-o", hosted,
                    "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"build {policy} exited {exit}: {output}{error}");
                (exit, output, error) = await RunAsync(input, "build-component",
                    TerrumProjectPaths.SourceDirectory(root), "-", "-o", scanned,
                    "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"build-component {policy} exited {exit}: {output}{error}");
                Assert.That(await File.ReadAllBytesAsync(scanned), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                using var process = Process.Start(new ProcessStartInfo(scanned) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("Could not start update executable.");
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
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task NominalUpdatesMatchCompiledScannerAndPreserveFailureExitAsync()
    {
        const string source = """
            struct Value {
                public mut int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator ++(Value value) {
                    return new Value(value.Number + 1);
                }
            }
            int32 Main() {
                mut Value value = new Value(int32.MaxValue);
                value++;
                return value.Number;
            }
            """;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(TestArtifactPaths.DirectoryPath, $"nominal-updates-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var hosted = Path.Combine(directory, "hosted.exe");
                var scanned = Path.Combine(directory, "scanned.exe");
                var input = Encoding.UTF8.GetBytes(source);
                var (exit, output, error) = await RunAsync(input, "build", "-", "-o", hosted,
                    "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"build {policy} exited {exit}: {output}{error}");
                (exit, output, error) = await RunAsync(input, "build-component",
                    TerrumProjectPaths.SourceDirectory(root), "-", "-o", scanned,
                    "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"build-component {policy} exited {exit}: {output}{error}");
                Assert.That(await File.ReadAllBytesAsync(scanned), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                using var process = Process.Start(new ProcessStartInfo(scanned) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }) ?? throw new InvalidOperationException("Could not start nominal update executable.");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
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
                Assert.That(await stdout, Is.Empty);
                Assert.That(await stderr, Is.Empty);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
