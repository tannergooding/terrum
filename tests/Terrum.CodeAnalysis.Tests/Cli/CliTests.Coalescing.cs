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
    public async Task CoalescingMatchesCompiledScannerAndRunsAotAsync(string policy)
    {
        const string source = """
            mut class Leaf { public int32 Value; public Leaf(int32 value) { Value = value; } }
            int32 Main() {
                require Leaf.TryCreate(42, out var leaf);
                mut Leaf? optional = null;
                optional ??= leaf;
                return (optional ?? leaf).Value;
            }
            """;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(TestArtifactPaths.DirectoryPath, $"coalescing-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(directory);
        try
        {
            var hosted = Path.Combine(directory, "hosted.exe");
            var scanned = Path.Combine(directory, "scanned.exe");
            var input = Encoding.UTF8.GetBytes(source);
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
            }) ?? throw new InvalidOperationException("Could not start coalescing executable.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.EqualTo(42));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
