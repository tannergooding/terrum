// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Fact]
    public async Task CompoundAssignmentGenericClosersRetainLexicalAndTreeStreamsAsync()
    {
        var input = Encoding.UTF8.GetBytes("void F(Box<Box<int32 /* close */ >> value) {}");
        foreach (var command in new[] { "tokens", "tree" })
        {
            var (exit, output, error) = await RunAsync(input, command, "-", "--json");
            Assert.True(exit == 0, $"{command} exited {exit}: {output}{error}");
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            var tokens = document.RootElement.GetProperty("tokens").EnumerateArray().ToArray();
            Assert.Equal(command == "tokens" ? 1 : 0,
                tokens.Count(token => token.GetProperty("kind").GetString() == "ShiftRight"));
            Assert.Equal(command == "tree" ? 2 : 0,
                tokens.Count(token => token.GetProperty("kind").GetString() == "Greater"));
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task CompoundAssignmentsMatchCompiledScannerAndExecuteAsync(string policy)
    {
        const string source = """
            int32 Main() {
                mut int32 value = 40;
                value += 4;
                value -= 2;
                value *= 2;
                value /= 2;
                value %= 41;
                value |= 32;
                value &= 63;
                value ^= 32;
                value <<= 1;
                value >>= 1;
                value >>>= 1;
                for (mut int32 i = 0; i < 2; i += 1) { value += 20; }
                return value;
            }
            """;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(root, "artifacts", $"compound-assignments-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
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
            }) ?? throw new InvalidOperationException("Could not start compound assignment executable.");
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
            Assert.Equal(40, process.ExitCode);
            Assert.Empty(await stdout);
            Assert.Empty(await stderr);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
