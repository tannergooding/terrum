// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public Task Dx11WarpSampleRendersResizesAndClosesNormallyAsync() =>
        ObserveDx11SampleAsync("main.terrum", scaledTriangle: false);

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public Task Dx11WarpSampleObservesScaledTriangleGeometryAsync() =>
        ObserveDx11SampleAsync("main.terrum", scaledTriangle: true);

    [HardwareIntegrationTest]
    [Category("HardwareIntegration")]
    public Task Dx11HardwareSampleRendersResizesAndClosesNormallyAsync() =>
        ObserveDx11SampleAsync("main-hardware.terrum", scaledTriangle: false);

    private static async Task ObserveDx11SampleAsync(string sourceName, bool scaledTriangle)
    {
        var source = Path.Combine(RepositoryRoot(), "samples", "Win32Dx11Triangle");
        var directory = TestArtifactPaths.CreateDirectory("terrum-dx11-sample-");
        try
        {
            var path = Path.Combine(directory, "triangle.exe");
            await BuildDx11PipelineAsync(sourceName, path).ConfigureAwait(false);
            var shader = await File.ReadAllTextAsync(Path.Combine(source, "triangle.hlsl")).ConfigureAwait(false);
            if (scaledTriangle)
            {
                const string Position = "return float4(position, 0.5f, 1.0f);";
                Assert.That(shader, Does.Contain(Position));
                shader = shader.Replace(Position, "return float4(position * 0.2f, 0.5f, 1.0f);",
                    StringComparison.Ordinal);
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "triangle.hlsl"), shader).ConfigureAwait(false);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                WorkingDirectory = directory,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException("Could not start the triangle sample.");
            var errors = process.StandardError.ReadToEndAsync();
            try
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                var window = IntPtr.Zero;
                while (DateTime.UtcNow < deadline && !process.HasExited)
                {
                    process.Refresh();
                    window = process.MainWindowHandle;
                    if (window != IntPtr.Zero && process.MainWindowTitle == "Terrum DX11 Triangle")
                    {
                        break;
                    }
                    await Task.Delay(50).ConfigureAwait(false);
                }
                Assert.That(process.HasExited, Is.False, "The sample exited before opening its window.");
                Assert.That(window, Is.Not.EqualTo(IntPtr.Zero));
                foreach (var (width, height) in new[] { (640, 480), (704, 528) })
                {
                    Assert.That(Dx11SetWindowPos(window, new IntPtr(-1), 32, 32, width, height, 0x10),
                        Is.True, $"Could not resize the sample (Win32 {Marshal.GetLastPInvokeError()}).");
                    Assert.That(Dx11GetClientRect(window, out var rectangle), Is.True);
                    Assert.That(rectangle.Right > 0 && rectangle.Bottom > 0, Is.True);
                    await AssertDx11VisiblePixelsAsync(process, window, rectangle, scaledTriangle)
                        .ConfigureAwait(false);
                }
                Assert.That(Dx11PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero), Is.True);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(process.ExitCode, Is.Zero);
                Assert.That(await errors.ConfigureAwait(false), Is.Empty);
                Assert.That(Dx11IsWindow(window), Is.False);
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
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory).ConfigureAwait(false);
        }
    }

    private static async Task AssertDx11VisiblePixelsAsync(
        Process process, IntPtr window, Dx11Rectangle rectangle, bool scaledTriangle)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var background = uint.MaxValue;
        var triangle = uint.MaxValue;
        do
        {
            Assert.That(process.HasExited, Is.False, "The sample exited during rendering.");
            var dc = Dx11GetDc(window);
            Assert.That(dc, Is.Not.EqualTo(IntPtr.Zero));
            try
            {
                background = Dx11GetPixel(dc, rectangle.Right / 8, rectangle.Bottom / 8);
                triangle = Dx11GetPixel(dc, rectangle.Right / 2, rectangle.Bottom * 3 / 8);
            }
            finally
            {
                Assert.That(Dx11ReleaseDc(window, dc), Is.EqualTo(1));
            }
            if (MatchesColor(background, 20, 31, 46)
                && (scaledTriangle ? MatchesColor(triangle, 20, 31, 46)
                    : MatchesColor(triangle, 26, 204, 230)))
            {
                return;
            }
            await Task.Delay(50).ConfigureAwait(false);
        } while (DateTime.UtcNow < deadline);
        Assert.Fail($"Unexpected visible pixels: background=0x{background:X8}, triangle=0x{triangle:X8}.");

        static bool MatchesColor(uint color, int red, int green, int blue)
        {
            return color != uint.MaxValue && Math.Abs((int)(color & 0xFF) - red) <= 12
                && Math.Abs((int)((color >> 8) & 0xFF) - green) <= 12
                && Math.Abs((int)((color >> 16) & 0xFF) - blue) <= 12;
        }
    }

    internal static async Task BuildDx11PipelineAsync(string sourceName, string path)
    {
        var directory = Path.Combine(RepositoryRoot(), "samples", "Win32Dx11Triangle");
        var (exit, output, error) = await RunAsync([], "build-native-import",
            Path.Combine(directory, sourceName),
            "--source", Path.Combine(directory, "dx11-api.terrum"),
            "--source", Path.Combine(directory, "win32-api.terrum"),
            "-o", path, "--runtime", "checked-exceptions", "--json").ConfigureAwait(false);
        Assert.That(exit, Is.Zero, output + error);
        Assert.That(error, Is.Empty);
        using var result = JsonDocument.Parse(output);
        Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.True);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dx11Rectangle { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Dx11SetWindowPos(IntPtr window, IntPtr after, int x, int y,
        int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetClientRect", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Dx11GetClientRect(IntPtr window, out Dx11Rectangle rectangle);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Dx11PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);

    [DllImport("user32.dll", EntryPoint = "IsWindow", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Dx11IsWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetDC", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr Dx11GetDc(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "ReleaseDC", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int Dx11ReleaseDc(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll", EntryPoint = "GetPixel", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint Dx11GetPixel(IntPtr dc, int x, int y);
}
