// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
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
            Assert.Equal(1, standardExit);
            Assert.Empty(standardError);
            Assert.False(File.Exists(standardOutput));
            using (var standardJson = JsonDocument.Parse(standardResult))
            {
                Assert.Equal("build", standardJson.RootElement.GetProperty("command").GetString());
                Assert.False(standardJson.RootElement.GetProperty("success").GetBoolean());
            }

            var (nativeExit, nativeResult, nativeError) = await RunAsync([], "build-native-import", sample,
                "-o", nativeImportOutput, "--runtime", runtimePolicy, "--json");
            Assert.Equal(0, nativeExit);
            Assert.Empty(nativeError);
            using var nativeJson = JsonDocument.Parse(nativeResult);
            Assert.Equal("build-native-import", nativeJson.RootElement.GetProperty("command").GetString());
            Assert.True(nativeJson.RootElement.GetProperty("success").GetBoolean());

            var image = await File.ReadAllBytesAsync(nativeImportOutput);
            Assert.True(image.AsSpan().IndexOf("user32.dll\0"u8) >= 0);
            Assert.True(image.AsSpan().IndexOf("MessageBoxW\0"u8) >= 0);
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
