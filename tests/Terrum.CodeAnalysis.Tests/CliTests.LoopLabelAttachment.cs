// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCaseSource(typeof(LoopLabelAttachmentTests), nameof(LoopLabelAttachmentTests.Boundaries))]
    public async Task NativeLoopLabelsEnforceEveryPhysicalLineBoundaryAsync(string loop, string gap, bool invalid)
    {
        var input = Encoding.UTF8.GetBytes(LoopLabelAttachmentTests.Source(loop, gap));
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                SelfHostPaths.SourceDirectory(RepositoryRoot()), "-", "--runtime", policy, "--json");
            Assert.That(exit == (invalid ? 1 : 0), Is.True, $"{output}{error}");
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            Assert.That(result.GetProperty("valid").GetBoolean(), Is.EqualTo(!invalid));
            if (!invalid)
            {
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(result.GetProperty("noThrow").GetBoolean(), Is.True);
            }
        }
    }
    [TestCase("a: while (true) { break a; }","a:\nwhile (true) { break a; }")]
    [TestCase("a: for (;;) { break a; }","a: /*\r\n*/ for (;;) { break a; }")]
    [TestCase("a: do { break a; } while (false);","a: b: do { break b; } while (false);")]
    public async Task LoopLabelRejectionPreservesPeAcrossHostedScannerAndNativeAsync(string valid, string invalid)
    {
        var root = RepositoryRoot();
        var path = CreateTestArtifactPath($"loop-label-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                foreach (var mode in new[] { "build", "build-component", "frontend-build" })
                {
                    var args = mode == "build" ? new[] { "-", "-o", path, "--runtime", policy, "--json" }
                        : new[] { SelfHostPaths.SourceDirectory(root),
                            "-", "-o", path, "--runtime", policy, "--json" };
                    var source = Encoding.UTF8.GetBytes($"int32 Main() {{ {valid} return 42; }}");
                    var (exit, output, error) = await RunAsync(source, [mode, .. args]);
                    Assert.That(exit == 0, Is.True, $"{output}{error}");
                    var bytes = await File.ReadAllBytesAsync(path);
                    Assert.That(await ExecuteNativeImageAsync(path), Is.EqualTo(42));
                    source = Encoding.UTF8.GetBytes($"int32 Main() {{ {invalid} return 42; }}");
                    (exit, output, error) = await RunAsync(source, [mode, .. args]);
                    Assert.That(exit == 1, Is.True, $"{output}{error}");
                    Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes));
                }
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
