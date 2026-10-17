// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public Task Dx11SampleMainReadsBackExpectedPixelsFromRealWarpAsync() =>
        RunDx11SamplePixelReadbackAsync("main.terrum", hardware: false);

    [HardwareIntegrationTest]
    [Category("HardwareIntegration")]
    public Task Dx11HardwareSampleMainReadsBackExpectedPixelsFromRealHardwareAsync() =>
        RunDx11SamplePixelReadbackAsync("main-hardware.terrum", hardware: true);

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public Task Dx11SampleProbeReportsScaledTriangleGeometryMismatchFromRealWarpAsync() =>
        RunDx11SamplePixelReadbackAsync("main.terrum", hardware: false,
            expectGeometryMismatch: true);

    private async Task RunDx11SamplePixelReadbackAsync(string sourceName, bool hardware,
        bool expectGeometryMismatch = false)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(true);
        var driver = hardware ? "hardware" : "warp";
        var driverLabel = hardware ? "hardware" : "WARP";
        var directory = TestArtifactPaths.CreateDirectory(hardware
            ? "terrum-dx11-sample-pixels-hardware-" : "terrum-dx11-sample-pixels-");
        try
        {
            var fixture = Path.Combine(root, "tests", "native", "native-dx11-sample-pixels-probe.c");
            var dllName = "native-dx11-sample-pixels-probe.dll";
            var dll = Path.Combine(directory, dllName);
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
                WorkingDirectory = directory,
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /Od /TC /LD /Fofixture.obj \"{fixture}\" /link /OUT:\"{dll}\" user32.lib d3d11.lib dxguid.lib\"",
            };
            build.Environment["PATH"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer") + ";" + build.Environment["PATH"];
            _ = await RunAsync(build, TimeSpan.FromSeconds(60)).ConfigureAwait(true);
            Assert.That(File.Exists(dll), Is.True);

            var program = AssertDx11SampleEntryPointUsesExactResizeRenderSeals(sourceName, hardware);
            var code = DirectWindowDeviceShaderCode(program);
            var injected = code with {
                NativeImports = [.. code.NativeImports.Select(import => import.ExportName == "GetProcAddress"
                    ? import with {
                        DllName = dllName, ExportName = "NativeFfiSamplePixelsGetProcAddress",
                    }
                    : import)],
            };
            var executable = Path.Combine(directory, "triangle.exe");
            await File.WriteAllBytesAsync(executable, PeImage.Build(injected)).ConfigureAwait(true);
            var shaderPath = Path.Combine(root, "samples", "design",
                "win32-dx11-triangle", "triangle.hlsl");
            var shader = await File.ReadAllTextAsync(shaderPath).ConfigureAwait(true);
            if (expectGeometryMismatch)
            {
                const string originalVertexPosition = "return float4(position, 1.0f);";
                Assert.That(shader.Contains(originalVertexPosition, StringComparison.Ordinal), Is.True);
                shader = shader.Replace(originalVertexPosition,
                    "return float4(position * 0.2f, 1.0f);", StringComparison.Ordinal);
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "triangle.hlsl"), shader)
                .ConfigureAwait(true);

            var report = Path.Combine(directory, "pixels.txt");
            var start = new ProcessStartInfo(executable) {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            start.Environment["TERRUM_DX11_SAMPLE_PIXELS_REPORT"] = report;
            start.Environment["TERRUM_DX11_SAMPLE_PIXELS_DRIVER"] = driver;
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("The instrumented Terrum DX11 sample did not start.");
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                var window = IntPtr.Zero;
                while (DateTime.UtcNow < deadline && !process.HasExited)
                {
                    process.Refresh();
                    window = process.MainWindowHandle;
                    if (window != IntPtr.Zero
                        && process.MainWindowTitle == "Terrum DX11 Triangle"
                        && IsWindowVisible(window))
                    {
                        break;
                    }
                    await Task.Delay(50).ConfigureAwait(true);
                }
                Assert.That(process.HasExited, Is.False, $"The {driverLabel} sample exited before presenting its window.");
                Assert.That(window, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(process.MainWindowTitle, Is.EqualTo("Terrum DX11 Triangle"));

                var initialPixelDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                var trace = string.Empty;
                while (DateTime.UtcNow < initialPixelDeadline && !process.HasExited)
                {
                    if (File.Exists(report))
                    {
                        trace = await File.ReadAllTextAsync(report).ConfigureAwait(true);
                        if (trace.Contains('I'))
                        {
                            break;
                        }
                    }
                    await Task.Delay(50).ConfigureAwait(true);
                }
                Assert.That(trace, Does.Contain('I'));
                Assert.That(GetClientRect(window, out var initialClient), Is.True, $"Could not query the initial client rectangle (Win32 {Marshal.GetLastPInvokeError()}).");
                var initialWidth = initialClient.Right - initialClient.Left;
                var initialHeight = initialClient.Bottom - initialClient.Top;
                Assert.That(initialWidth > 0 && initialHeight > 0, Is.True);
                Assert.That(SetWindowPos(window, IntPtr.Zero, 0, 0, 1024, 768, ResizeWindowFlags), Is.True, $"Could not resize the {driverLabel} sample window (Win32 {Marshal.GetLastPInvokeError()}).");
                Assert.That(GetClientRect(window, out var resizedClient), Is.True, $"Could not query the resized client rectangle (Win32 {Marshal.GetLastPInvokeError()}).");
                Assert.That(resizedClient.Right - resizedClient.Left > initialWidth, Is.True);
                Assert.That(resizedClient.Bottom - resizedClient.Top > initialHeight, Is.True);

                var resizedPixelDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                while (DateTime.UtcNow < resizedPixelDeadline && !process.HasExited)
                {
                    if (File.Exists(report))
                    {
                        trace = await File.ReadAllTextAsync(report).ConfigureAwait(true);
                        if (trace.Contains('R'))
                        {
                            break;
                        }
                    }
                    await Task.Delay(50).ConfigureAwait(true);
                }
                Assert.That(trace, Does.Contain('R'));
                Assert.That(PostMessageW(window, WindowCloseMessage, IntPtr.Zero, IntPtr.Zero), Is.True);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                var errors = await stderr.ConfigureAwait(true);
                trace = await File.ReadAllTextAsync(report).ConfigureAwait(true);
                Assert.That(process.ExitCode == 0, Is.True, $"Exit code {process.ExitCode}; events={trace}: {errors}");
                Assert.That(errors, Is.Empty);
                Assert.That(trace.Count(static character => character == 'I'), Is.EqualTo(1));
                Assert.That(trace.Count(static character => character == 'R'), Is.EqualTo(1));
                Assert.That(trace.IndexOf('I') < trace.IndexOf('R'), Is.True);
                Assert.That(trace, Does.Contain('Z'));
                if (expectGeometryMismatch)
                {
                    Assert.That(trace.Count(static character => character == 'G'), Is.EqualTo(2));
                }
                else
                {
                    Assert.That(trace, Does.Not.Contain('G'));
                }
                Assert.That(HardwareIsWindow(window), Is.False);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
