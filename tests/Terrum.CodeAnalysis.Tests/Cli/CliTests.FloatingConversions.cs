// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task FloatingConversionsMatchCompiledScannerAsync(string policy)
    {
        const string Source = """
             float64 Convert(uint64 value) { return float64(value); }
            int32 ToInteger(float64 value) { return int32(value); }
            int32 Main() { _ = Convert(uint64(0x8000000000000401)); return ToInteger(41.9) + 1; }
            """;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(TestArtifactPaths.DirectoryPath, $"integer-floating-conversion-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(directory);
        try
        {
            var hosted = Path.Combine(directory, "hosted.exe");
            var scanned = Path.Combine(directory, "scanned.exe");
            var input = Encoding.UTF8.GetBytes(Source);
            var (result, output, error) = await RunAsync(input, "build", "-", "-o", hosted,
                "--runtime", policy, "--json").ConfigureAwait(false);
            Assert.That(result == 0, Is.True, output + error);
            (result, output, error) = await RunScannerCommandWithLayoutAsync(input, "build-component",
                TerrumProjectPaths.SourceDirectory(root), "-", "-o", scanned,
                "--runtime", policy, "--json").ConfigureAwait(false);
            Assert.That(result == 0, Is.True, output + error);
            Assert.That(await File.ReadAllBytesAsync(scanned).ConfigureAwait(false), Is.EqualTo(await File.ReadAllBytesAsync(hosted).ConfigureAwait(false)));
            using var process = Process.Start(new ProcessStartInfo(scanned) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start floating-conversion executable.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.EqualTo(42));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
