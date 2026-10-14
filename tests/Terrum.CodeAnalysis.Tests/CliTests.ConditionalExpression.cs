// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ConditionalExpressionsMatchCompiledScannerAsync(string policy)
    {
        const string Source = """
            bool Fill(init ref<int32> value) { value = 42; return true; }
            int32 Main() {
                init var value;
                return Fill(init value) ? value : 0;
            }
            """;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(root, "artifacts", $"conditional-expression-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var hosted = Path.Combine(directory, "hosted.exe");
            var scanned = Path.Combine(directory, "scanned.exe");
            var input = Encoding.UTF8.GetBytes(Source);
            var (result, output, error) = await RunAsync(input, "build", "-", "-o", hosted,
                "--runtime", policy, "--json");
            Assert.That(result == 0, Is.True, output + error);
            (result, output, error) = await RunAsync(input, "build-component",
                Path.Combine(root, "self-host"), "-", "-o", scanned,
                "--runtime", policy, "--json");
            Assert.That(result == 0, Is.True, output + error);
            Assert.That(await File.ReadAllBytesAsync(scanned), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
            using var process = Process.Start(new ProcessStartInfo(scanned) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start conditional expression executable.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.That(process.ExitCode, Is.EqualTo(42));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
