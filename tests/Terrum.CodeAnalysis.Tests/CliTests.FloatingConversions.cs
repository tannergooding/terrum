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
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task FloatingConversionsMatchCompiledScannerAsync(string policy)
    {
        const string Source = """
            [NoThrow] float64 Convert(uint64 value) { return float64(value); }
            int32 ToInteger(float64 value) { return int32(value); }
            int32 Main() { _ = Convert(uint64(0x8000000000000401)); return ToInteger(41.9) + 1; }
            """;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(root, "artifacts", $"integer-floating-conversion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var hosted = Path.Combine(directory, "hosted.exe");
            var scanned = Path.Combine(directory, "scanned.exe");
            var input = Encoding.UTF8.GetBytes(Source);
            var (result, output, error) = await RunAsync(input, "build", "-", "-o", hosted,
                "--runtime", policy, "--json");
            Assert.True(result == 0, output + error);
            (result, output, error) = await RunAsync(input, "build-component",
                Path.Combine(root, "self-host", "Scanner.terrum"), "-", "-o", scanned,
                "--runtime", policy, "--json");
            Assert.True(result == 0, output + error);
            Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(scanned));
            using var process = Process.Start(new ProcessStartInfo(scanned) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start floating-conversion executable.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(42, process.ExitCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
