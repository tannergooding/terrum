// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task BuildNativeImportOptsIntoCataloguedMessageBoxWithoutExecutingItAsync(string runtimePolicy)
    {
        var sample = Path.Combine(RepositoryRoot(), "samples", "native", "message-box.terrum");
        var standardOutput = Path.Combine(Path.GetTempPath(),
            $"terrum-message-box-standard-{runtimePolicy}-{Guid.NewGuid():N}.exe");
        var nativeImportOutput = Path.Combine(Path.GetTempPath(),
            $"terrum-message-box-native-{runtimePolicy}-{Guid.NewGuid():N}.exe");
        try
        {
            var (standardExit, standardResult, standardError) = await RunAsync([], "build", sample,
                "-o", standardOutput, "--runtime", runtimePolicy, "--json");
            Assert.That(standardExit, Is.EqualTo(1));
            Assert.That(standardError, Is.Empty);
            Assert.That(File.Exists(standardOutput), Is.False);
            using (var standardJson = JsonDocument.Parse(standardResult))
            {
                Assert.That(standardJson.RootElement.GetProperty("command").GetString(), Is.EqualTo("build"));
                Assert.That(standardJson.RootElement.GetProperty("success").GetBoolean(), Is.False);
            }

            var (nativeExit, nativeResult, nativeError) = await RunAsync([], "build-native-import", sample,
                "-o", nativeImportOutput, "--runtime", runtimePolicy, "--json");
            Assert.That(nativeExit, Is.EqualTo(0));
            Assert.That(nativeError, Is.Empty);
            using var nativeJson = JsonDocument.Parse(nativeResult);
            Assert.That(nativeJson.RootElement.GetProperty("command").GetString(), Is.EqualTo("build-native-import"));
            Assert.That(nativeJson.RootElement.GetProperty("success").GetBoolean(), Is.True);

            var image = await File.ReadAllBytesAsync(nativeImportOutput);
            Assert.That(image.AsSpan().IndexOf("user32.dll\0"u8) >= 0, Is.True);
            Assert.That(image.AsSpan().IndexOf("MessageBoxW\0"u8) >= 0, Is.True);
        }
        finally
        {
            if (File.Exists(standardOutput))
            {
                File.Delete(standardOutput);
            }
            if (File.Exists(nativeImportOutput))
            {
                File.Delete(nativeImportOutput);
            }
        }
    }
}
