// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [Test]
    public async Task DirectHardwareAcquisitionMatchesSdkAndCleansEveryInjectedOutputAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory("terrum-direct-hardware-abi-");
        try
        {
            var dll = await BuildHardwareFixtureAsync(directory, "direct-acquisition", "user32.lib").ConfigureAwait(true);
            var code = DirectWindowDeviceShaderCode(CompileDirectHardwarePipeline());
            var injected = code with {
                NativeImports = [.. code.NativeImports.Select(import =>
                    import.ExportName == "GetProcAddress" ? import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectWarpGetProcAddress",
                    } : import)],
            };
            var executable = Path.Combine(directory, "injected-hardware-acquisition.exe");
            await File.WriteAllBytesAsync(executable, PeImage.Build(injected)).ConfigureAwait(true);
            for (var mode = 0; mode <= 20; mode++)
            {
                var mask = mode == 1 ? 0 : mode is >= 2 and <= 8 ? mode - 1
                    : mode is >= 10 and <= 16 ? mode - 10 : 7;
                var success = mode is 0 or 9 or 17 or 19;
                var alias = mode is 17 or 18;
                // Successful fake acquisition stops at CreateBuffer, before any pipeline submission.
                var trace = "A" + (success ? "B" : "") + (alias ? "RRR"
                    : ((mask & 4) != 0 ? "C" : "") + ((mask & 2) != 0 ? "S" : "")
                        + ((mask & 1) != 0 ? "D" : "")) + "Z";
                var exit = mode is >= 10 and <= 16 ? NativeDx11WarpAcquisitionContract.NullSuccessError : 1;
                for (var iteration = 0; iteration < 4; iteration++)
                {
                    await RunHardwareFixtureAsync(executable, directory, "TERRUM_DX11", mode, iteration, exit, trace)
                        .ConfigureAwait(true);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task DirectHardwarePipelineInterceptionPreservesDrawPumpAndFailureCleanupAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory("terrum-hardware-pipeline-abi-");
        try
        {
            var dll = await BuildHardwareFixtureAsync(directory, "direct-pipeline", "user32.lib d3d11.lib")
                .ConfigureAwait(true);
            File.Copy(Path.Combine(FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "triangle.hlsl"),
                Path.Combine(directory, "triangle.hlsl"));
            var code = DirectWindowDeviceShaderCode(CompileDirectHardwarePipeline());
            var exports = new[] {
                ("GetProcAddress", "GetProcAddress"),
                ("GetMessageW", "GetMessageW"),
                ("ShowWindow", "ShowWindow"),
                ("TranslateMessage", "TranslateMessage"),
                ("DispatchMessageW", "DispatchMessageW"),
                ("PostQuitMessage", "PostQuitMessage"),
                ("DestroyWindow", "DestroyWindow"),
                ("UnregisterClassW", "UnregisterClassW"),
            }.ToDictionary(static pair => pair.Item1, static pair => "NativeFfiDirectPipeline" + pair.Item2);
            // This fixture explicitly supplies real WARP receivers as deterministic test doubles
            // after verifying hardware driver 1. The unmodified hardware PE never uses this DLL.
            var injected = code with {
                NativeImports = [.. code.NativeImports.Select(import =>
                    exports.TryGetValue(import.ExportName, out var export) ? import with {
                        DllName = Path.GetFileName(dll), ExportName = export,
                    } : import)],
            };
            var executable = Path.Combine(directory, "injected-hardware-pipeline.exe");
            await File.WriteAllBytesAsync(executable, PeImage.Build(injected)).ConfigureAwait(true);
            string[] traces = [
                "ABLVPGRcovlbtxydOpmTFQeKrgYXIBCSDWUZ",
                "ABLVPGRcovlbtxydOpKrgYXIBCSDWUZ",
                "ABLVPGRcovlbtxydOp!KrgYXIBCSDWUZ",
                "ABLVIBCSDWUZ",
                "ABLVPXIBCSDWUZ",
                "ABLBCSDWUZ",
                "ABLVPGRgYXIBCSDWUZ",
                "ABCSDWUZ",
                "ABLVPGYXIBCSDWUZ",
                "ABLVPGRrgYXIBCSDWUZ",
                "ABLVXIBCSDWUZ",
                "ABLIBCSDWUZ",
                "ABLVPYXIBCSDWUZ",
            ];
            for (var mode = 0; mode < traces.Length; mode++)
            {
                await RunHardwareFixtureAsync(executable, directory, "TERRUM_DX11_PIPELINE",
                    mode, 0, mode == 0 ? 0 : 1, traces[mode]).ConfigureAwait(true);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string> BuildHardwareFixtureAsync(string directory, string probe, string libraries)
    {
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(true);
        var sdk = Path.Combine(root, "tests", "native", $"dx11-{probe}-sdk-probe.c");
        var fixture = Path.Combine(root, "tests", "native", $"native-dx11-{probe}-probe.c");
        var dll = Path.Combine(directory, $"native-dx11-{probe}-probe.dll");
        var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
            WorkingDirectory = directory,
            Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /Od /TC /c /Fosdk.obj \"{sdk}\" && cl /nologo /W4 /WX /wd4201 /Od /TC /LD /Fofixture.obj \"{fixture}\" /link /OUT:\"{dll}\" {libraries}\"",
        };
        build.Environment["PATH"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer") + ";" + build.Environment["PATH"];
        _ = await RunAsync(build, TimeSpan.FromSeconds(60)).ConfigureAwait(true);
        Assert.That(File.Exists(Path.Combine(directory, "sdk.obj")), Is.True);
        Assert.That(File.Exists(dll), Is.True);
        return dll;
    }

    private static async Task RunHardwareFixtureAsync(string executable, string directory, string prefix,
        int mode, int iteration, int expectedExit, string expectedTrace)
    {
        var report = Path.Combine(directory, $"trace-{mode}-{iteration}.txt");
        var start = new ProcessStartInfo(executable) {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        start.Environment["TERRUM_DX11_HARDWARE"] = "1";
        start.Environment[prefix + "_MODE"] = mode.ToString(CultureInfo.InvariantCulture);
        start.Environment[prefix + "_REPORT"] = report;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Hardware fixture did not start.");
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
            var errors = await stderr.ConfigureAwait(true);
            var trace = await File.ReadAllTextAsync(report).ConfigureAwait(true);
            Assert.That(process.ExitCode == expectedExit, Is.True, $"Mode {mode}, iteration {iteration}, exit {process.ExitCode}, trace {trace}: {errors}");
            Assert.That(trace, Is.EqualTo(expectedTrace));
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

    [HardwareIntegrationTest]
    [Category("HardwareIntegration")]
    public async Task DirectHardwarePipelineRunsUnmodifiedSystemOnlyPeAsync()
    {
        var program = CompileDirectHardwarePipeline();
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);
        var directory = TestArtifactPaths.CreateDirectory("terrum-real-hardware-pipeline-");
        try
        {
            File.Copy(Path.Combine(FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "triangle.hlsl"),
                Path.Combine(directory, "triangle.hlsl"));
            var executable = Path.Combine(directory, "hardware-pipeline.exe");
            await File.WriteAllBytesAsync(executable, image).ConfigureAwait(true);
            Assert.That(Directory.EnumerateFiles(directory, "*.dll"), Is.Empty);
            using var process = Process.Start(new ProcessStartInfo(executable) {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = false,
            }) ?? throw new InvalidOperationException("Unmodified hardware pipeline did not start.");
            try
            {
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
                    await Task.Delay(50).ConfigureAwait(true);
                }
                Assert.That(process.HasExited, Is.False, "Hardware availability was established; application failure cannot be skipped.");
                Assert.That(window, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(process.MainWindowTitle, Is.EqualTo("Terrum DX11 Triangle"));
                Assert.That(IsWindowVisible(window), Is.True);
                Assert.That(PostMessageW(window, WindowCloseMessage, IntPtr.Zero, IntPtr.Zero), Is.True);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                Assert.That(process.ExitCode, Is.EqualTo(0));
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

    private sealed class HardwareIntegrationTestAttribute : ConditionalSkipTestAttribute
    {
        protected override string? GetSkipReason()
        {
            if (Environment.GetEnvironmentVariable("TERRUM_RUN_DX11_HARDWARE") != "1")
            {
                return "Not exercised: opt in with TERRUM_RUN_DX11_HARDWARE=1.";
            }
            if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                return "Not exercised: requires Windows x64.";
            }
            if (!Environment.UserInteractive)
            {
                return "Not exercised: no interactive desktop.";
            }
            var desktop = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS
            if (desktop == IntPtr.Zero)
            {
                return $"Not exercised: input desktop unavailable (Win32 {Marshal.GetLastPInvokeError()}).";
            }
            Assert.That(CloseDesktop(desktop), Is.True);
            var device = IntPtr.Zero;
            var context = IntPtr.Zero;
            try
            {
                var result = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0, [0xB000], 1, 7,
                    out device, out var selected, out context);
                if (result == unchecked((int)0x887A0004)) // DXGI_ERROR_UNSUPPORTED
                {
                    return "Not exercised: hardware feature level 11_0 unavailable (DXGI_ERROR_UNSUPPORTED).";
                }
                Assert.That(result >= 0, Is.True, $"Hardware preflight failed: 0x{result:X8}.");
                Assert.That(device, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(context, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(selected, Is.EqualTo(0xB000U));
                return null;
            }
            finally
            {
                if (context != IntPtr.Zero)
                {
                    _ = Marshal.Release(context);
                }
                if (device != IntPtr.Zero)
                {
                    _ = Marshal.Release(device);
                }
            }
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driver, IntPtr software, uint flags,
        [In] uint[] levels, uint levelCount, uint sdk, out IntPtr device, out uint selected, out IntPtr context);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll", EntryPoint = "IsWindow", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HardwareIsWindow(IntPtr window);
}
