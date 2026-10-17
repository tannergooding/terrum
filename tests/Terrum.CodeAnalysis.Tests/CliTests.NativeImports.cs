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
    public async Task BuildNativeImportCompilesDirectMessageBoxWithoutExecutingItAsync(string runtimePolicy)
    {
        var sample = Path.Combine(RepositoryRoot(), "samples", "native", "message-box.terrum");
        var standardOutput = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"terrum-message-box-standard-{runtimePolicy}-{Guid.NewGuid():N}.exe");
        var nativeImportOutput = Path.Combine(TestArtifactPaths.DirectoryPath,
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
            await TestFileCleanup.DeleteWithRetryAsync(standardOutput).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(nativeImportOutput).ConfigureAwait(true);
        }
    }

    [TestCase("main.terrum")]
    [TestCase("main-hardware.terrum")]
    public async Task BuildNativeImportCompilesGenericDx11EntryPointAsync(string sourceName)
    {
        var sample = Path.Combine(RepositoryRoot(), "samples", "design", "win32-dx11-triangle", sourceName);
        var interop = Path.Combine(Path.GetDirectoryName(sample)!, "dx11-api.terrum");
        var windowsInterop = Path.Combine(Path.GetDirectoryName(sample)!, "win32-api.terrum");
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-dx11-{Guid.NewGuid():N}.exe");
        try
        {
            var (exitCode, result, error) = await RunAsync([], "build-native-import", sample, "--source", interop,
                "--source", windowsInterop, "-o", output, "--runtime", "checked-exceptions", "--json");
            Assert.That(exitCode, Is.EqualTo(0), result + error);
            Assert.That(error, Is.Empty);
            using var json = JsonDocument.Parse(result);
            Assert.That(json.RootElement.GetProperty("success").GetBoolean(), Is.True);

            var image = await File.ReadAllBytesAsync(output);
            Assert.That(image.AsSpan().IndexOf("D3D11CreateDeviceAndSwapChain\0"u8) >= 0, Is.True);
            Assert.That(image.AsSpan().IndexOf("D3DCompileFromFile\0"u8) >= 0, Is.True);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(output).ConfigureAwait(true);
        }
    }
}
