// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScannerApphostRegistersCleanupContinuationsAsync(string policy)
    {
        var scanner = Path.Combine(TestArtifactPaths.DirectoryPath, $"registrar-scanner-{Guid.NewGuid():N}.terrum");
        try
        {
            await File.WriteAllTextAsync(scanner, """
                int32 Scan() {
                    require OwnedInt32Buffer.TryCreate(1, out var buffer);
                    require buffer.Count == 0;
                    Host.Tokens[0] = 0;
                    Host.Tokens[1] = 0;
                    Host.Tokens[2] = 0;
                    return 1;
                }
                """).ConfigureAwait(true);
            var start = CliStart("scan-component", scanner, "-", "--runtime", policy, "--json");
            start.FileName = Path.ChangeExtension(start.ArgumentList[0], ".exe");
            start.ArgumentList.RemoveAt(0);
            Assert.That(File.Exists(start.FileName), Is.True);
            var (exit, output, error) = await RunAsync(start, []).ConfigureAwait(true);
            Assert.That(exit == 0, Is.True, $"Scanner exit {exit}: {error}{output}");
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("sourceLength").GetInt32(), Is.EqualTo(0));
            Assert.That(document.RootElement.GetProperty("tokens").GetArrayLength(), Is.EqualTo(1));
            Assert.That(document.RootElement.GetProperty("diagnostics").GetArrayLength(), Is.EqualTo(0));
        }
        finally
        {
            File.Delete(scanner);
        }
    }
}
