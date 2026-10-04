// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [MemberData(nameof(LoopLabelAttachmentTests.Boundaries), MemberType = typeof(LoopLabelAttachmentTests))]
    public async Task NativeLoopLabelsEnforceEveryPhysicalLineBoundaryAsync(string loop, string gap, bool invalid)
    {
        var input = Encoding.UTF8.GetBytes(LoopLabelAttachmentTests.Source(loop, gap));
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                Path.Combine(RepositoryRoot(), "self-host"), "-", "--runtime", policy, "--json");
            Assert.True(exit == (invalid ? 1 : 0), $"{output}{error}");
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            Assert.Equal(!invalid, result.GetProperty("valid").GetBoolean());
            if (!invalid)
            {
                Assert.Equal(85, result.GetProperty("projectionVersion").GetInt32());
                Assert.True(result.GetProperty("noThrow").GetBoolean());
            }
        }
    }

    [Theory]
    [InlineData("a: while (true) { break a; }", "a:\nwhile (true) { break a; }")]
    [InlineData("a: for (;;) { break a; }", "a: /*\r\n*/ for (;;) { break a; }")]
    [InlineData("a: do { break a; } while (false);", "a: b: do { break b; } while (false);")]
    public async Task LoopLabelRejectionPreservesPeAcrossHostedScannerAndNativeAsync(string valid, string invalid)
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, $"loop-label-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                foreach (var mode in new[] { "build", "build-component", "frontend-build" })
                {
                    var args = mode == "build" ? new[] { "-", "-o", path, "--runtime", policy, "--json" }
                        : new[] { Path.Combine(root, "self-host"),
                            "-", "-o", path, "--runtime", policy, "--json" };
                    var source = Encoding.UTF8.GetBytes($"int32 Main() {{ {valid} return 42; }}");
                    var (exit, output, error) = await RunAsync(source, [mode, .. args]);
                    Assert.True(exit == 0, $"{output}{error}");
                    var bytes = await File.ReadAllBytesAsync(path);
                    Assert.Equal(42, await ExecuteNativeImageAsync(path));
                    source = Encoding.UTF8.GetBytes($"int32 Main() {{ {invalid} return 42; }}");
                    (exit, output, error) = await RunAsync(source, [mode, .. args]);
                    Assert.True(exit == 1, $"{output}{error}");
                    Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
