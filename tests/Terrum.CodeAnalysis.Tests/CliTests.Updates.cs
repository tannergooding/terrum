// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("int32")]
    [InlineData("uint32")]
    [InlineData("int64")]
    [InlineData("uint64")]
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
        var directory = Path.Combine(root, "artifacts", $"updates-{Guid.NewGuid():N}");
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
                Assert.True(exit == 0, $"build {policy} exited {exit}: {output}{error}");
                (exit, output, error) = await RunAsync(input, "build-component",
                    Path.Combine(root, "self-host", "Scanner.terrum"), "-", "-o", scanned,
                    "--runtime", policy, "--json");
                Assert.True(exit == 0, $"build-component {policy} exited {exit}: {output}{error}");
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(scanned));
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
                Assert.Equal(42, process.ExitCode);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
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
        var directory = Path.Combine(root, "artifacts", $"nominal-updates-{Guid.NewGuid():N}");
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
                Assert.True(exit == 0, $"build {policy} exited {exit}: {output}{error}");
                (exit, output, error) = await RunAsync(input, "build-component",
                    Path.Combine(root, "self-host", "Scanner.terrum"), "-", "-o", scanned,
                    "--runtime", policy, "--json");
                Assert.True(exit == 0, $"build-component {policy} exited {exit}: {output}{error}");
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(scanned));
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
                Assert.Equal(unchecked((int)0xE0540001), process.ExitCode);
                Assert.Empty(await stdout);
                Assert.Empty(await stderr);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
