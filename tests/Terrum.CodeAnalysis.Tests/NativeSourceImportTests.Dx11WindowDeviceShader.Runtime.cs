// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [TestCase("missing")]
    [TestCase("invalid")]
    [TestCase("missing-pixel")]
    [TestCase("input-signature")]
    public Task DirectWindowDeviceShaderStageRejectsInvalidShaderInputsOnRealWarpAsync(string asset) =>
        RunDirectWindowDeviceShaderStageOnRealWarpAsync(asset);

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public Task DirectWindowDeviceShaderStageCreatesShadersWithVisibleWindowOnRealWarpAsync() =>
        RunDirectWindowDeviceShaderStageOnRealWarpAsync("valid");

    private async Task RunDirectWindowDeviceShaderStageOnRealWarpAsync(string asset)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var program = CompileDirectWindowDeviceShader();
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);
        var directory = Directory.CreateTempSubdirectory("terrum-window-device-shader-").FullName;
        try
        {
            var shaderPath = Path.Combine(directory, "triangle.hlsl");
            if (asset == "valid")
            {
                File.Copy(Path.Combine(FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle",
                    "triangle.hlsl"), shaderPath);
            }
            else if (asset == "invalid")
            {
                await File.WriteAllTextAsync(shaderPath, "This is deliberately invalid HLSL.").ConfigureAwait(false);
            }
            else if (asset == "missing-pixel")
            {
                await File.WriteAllTextAsync(shaderPath,
                    "float4 VertexMain(float3 position : POSITION) : SV_POSITION { return float4(position, 1); }")
                    .ConfigureAwait(false);
            }
            else if (asset == "input-signature")
            {
                await File.WriteAllTextAsync(shaderPath,
                    "float4 VertexMain(float3 position : COLOR) : SV_POSITION { return float4(position, 1); }"
                    + "float4 PixelMain() : SV_TARGET { return float4(0, 1, 1, 1); }").ConfigureAwait(false);
            }
            var executable = Path.Combine(directory, "window-device-shader.exe");
            await File.WriteAllBytesAsync(executable, image).ConfigureAwait(false);
            using var process = Process.Start(new ProcessStartInfo(executable) {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = false,
            }) ?? throw new InvalidOperationException("Direct shader stage did not start.");
            try
            {
                if (asset != "valid")
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                    Assert.That(process.ExitCode, Is.EqualTo(1));
                    return;
                }
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                var window = IntPtr.Zero;
                while (DateTime.UtcNow < deadline && !process.HasExited)
                {
                    process.Refresh();
                    window = process.MainWindowHandle;
                    if (window != IntPtr.Zero && process.MainWindowTitle == "Terrum DX11 Triangle" && IsWindowVisible(window))
                    {
                        break;
                    }
                    await Task.Delay(50).ConfigureAwait(false);
                }
                Assert.That(process.HasExited, Is.False, "Real HLSL compilation, shader/input-layout creation, or presentation failed.");
                Assert.That(window, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(process.MainWindowTitle, Is.EqualTo("Terrum DX11 Triangle"));
                Assert.That(IsWindowVisible(window), Is.True, "The stage shows its window only after creating real shaders and their input layout.");
                Assert.That(PostMessageW(window, WindowCloseMessage, IntPtr.Zero, IntPtr.Zero), Is.True);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(process.ExitCode, Is.EqualTo(0));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
