// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [Test]
    public async Task DirectWindowDevicePipelineStageMatchesSdkRealReceiverAbiPixelsAndFailureCleanupAsync()
    {
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(true);
        var directory = Directory.CreateTempSubdirectory("terrum-direct-pipeline-abi-").FullName;
        try
        {
            var sdk = Path.Combine(root, "tests", "native", "dx11-direct-pipeline-sdk-probe.c");
            var fixture = Path.Combine(root, "tests", "native", "native-dx11-direct-pipeline-probe.c");
            var dll = Path.Combine(directory, "native-dx11-direct-pipeline-probe.dll");
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
                WorkingDirectory = directory,
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /Od /TC /c /Fosdk.obj \"{sdk}\" && cl /nologo /W4 /WX /wd4201 /Od /TC /LD /Fofixture.obj \"{fixture}\" /link /OUT:\"{dll}\" user32.lib d3d11.lib\"",
            };
            build.Environment["PATH"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer") + ";" + build.Environment["PATH"];
            _ = await RunAsync(build, TimeSpan.FromSeconds(60)).ConfigureAwait(true);
            Assert.That(File.Exists(Path.Combine(directory, "sdk.obj")), Is.True);
            File.Copy(Path.Combine(root, "samples", "design", "win32-dx11-triangle", "triangle.hlsl"),
                Path.Combine(directory, "triangle.hlsl"));
            var code = DirectWindowDeviceShaderCode(CompileDirectWindowDevicePipeline());
            // Only this emitted test image substitutes imports; the source, bound seal and production image do not.
            var injected = code with {
                NativeImports = [.. code.NativeImports.Select(import => import.ExportName switch {
                    "GetProcAddress" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectPipelineGetProcAddress",
                    },
                    "GetMessageW" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectPipelineGetMessageW",
                    },
                    "GetClientRect" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectPipelineGetClientRect",
                    },
                    "ShowWindow" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectPipelineShowWindow",
                    },
                    "TranslateMessage" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectPipelineTranslateMessage",
                    },
                    "DispatchMessageW" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectPipelineDispatchMessageW",
                    },
                    "PostQuitMessage" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectPipelinePostQuitMessage",
                    },
                    "DestroyWindow" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectPipelineDestroyWindow",
                    },
                    "UnregisterClassW" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectPipelineUnregisterClassW",
                    },
                    _ => import,
                })],
            };
            var executable = Path.Combine(directory, "injected-pipeline.exe");
            await File.WriteAllBytesAsync(executable, PeImage.Build(injected)).ConfigureAwait(true);
            string[] traces = [
                "ABLVPKGRKcovlbtxydOpKrgmTFQeYXIBCSDWUZ",
                "ABLVPKGRKcovlbtxydOpKrgYXIBCSDWUZ",
                "ABLVPKGRKcovlbtxydOpKrg!YXIBCSDWUZ",
                "ABLVIBCSDWUZ",
                "ABLVPXIBCSDWUZ",
                "ABLBCSDWUZ",
                "ABLVPKGRgYXIBCSDWUZ",
                "ABCSDWUZ",
                "ABLVPKGYXIBCSDWUZ",
                "ABLVPKGRrgYXIBCSDWUZ",
                "ABLVXIBCSDWUZ",
                "ABLIBCSDWUZ",
                "ABLVPYXIBCSDWUZ",
                "ABLVPYXIBCSDWUZ",
                "ABLVPKGRKcovlbtxydOpKrgmTFQeYXIBCSDWUZ",
                "ABLVPYXIBCSDWUZ",
                "ABLVPYXIBCSDWUZ",
                "ABLVPmTFQeYXIBCSDWUZ",
                "ABLVPYXIBCSDWUZ",
                "WUZ",
            ];
            for (var mode = 0; mode < traces.Length; mode++)
            {
                var report = Path.Combine(directory, $"trace-{mode}.txt");
                var start = new ProcessStartInfo(executable) {
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                };
                start.Environment["TERRUM_DX11_PIPELINE_MODE"] = mode.ToString(CultureInfo.InvariantCulture);
                start.Environment["TERRUM_DX11_PIPELINE_REPORT"] = report;
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Injected pipeline stage did not start.");
                var stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
                    var errors = await stderr.ConfigureAwait(true);
                    Assert.That(File.Exists(report), Is.True, $"Mode {mode} produced no trace, exit {process.ExitCode}: {errors}");
                    var trace = await File.ReadAllTextAsync(report).ConfigureAwait(true);
                    Assert.That(process.ExitCode == (mode is 0 or 14 or 17 ? 0 : 1), Is.True, $"Mode {mode}, exit {process.ExitCode}, trace {trace}: {errors}");
                    Assert.That(trace, Is.EqualTo(traces[mode]));
                    if (mode <= 2 || mode == 14)
                    {
                        Assert.That(trace.Count(static character => character == 'd'), Is.EqualTo(1));
                        Assert.That(trace.Count(static character => character == 'O'), Is.EqualTo(1));
                        Assert.That(trace.Count(static character => character == 'K'), Is.EqualTo(3));
                        Assert.That(trace.IndexOf('K', StringComparison.Ordinal) < trace.IndexOf('r', StringComparison.Ordinal), Is.True, "ClearState must precede even the first bound resource release.");
                    }
                    else if (mode is 6 or 8 or 9)
                    {
                        Assert.That(trace.Contains("d", StringComparison.Ordinal), Is.False);
                        Assert.That(trace.Count(static character => character == 'K'), Is.EqualTo(1));
                        var firstRelease = trace.IndexOf(mode switch {
                            6 => 'g',
                            8 => 'Y',
                            _ => 'r',
                        });
                        Assert.That(trace.IndexOf('K', StringComparison.Ordinal) < firstRelease, Is.True, $"Resize setup must clear context state before releasing resources in mode {mode}: {trace}");
                    }
                    else
                    {
                        Assert.That(trace.Contains("d", StringComparison.Ordinal), Is.False);
                        Assert.That(trace.Contains("K", StringComparison.Ordinal), Is.False);
                    }
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
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase("missing")]
    [TestCase("invalid")]
    [TestCase("missing-pixel")]
    [TestCase("input-signature")]
    public Task DirectWindowDevicePipelineStageRejectsInvalidShaderInputsOnRealWarpAsync(string asset) =>
        RunDirectWindowDevicePipelineStageOnRealWarpAsync(asset);

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public Task DirectWindowDevicePipelineStageRunsUnmodifiedSampleOnRealWarpAsync() =>
        RunDirectWindowDevicePipelineStageOnRealWarpAsync("valid");

    [Test]
    public async Task DirectWindowDevicePipelinePathScanHandlesSyntheticWindowsPathBoundariesAsync()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(true);
        var directory = Directory.CreateTempSubdirectory("terrum-dx11-shader-path-").FullName;
        var install = Path.Combine(directory, "install");
        var working = Path.Combine(directory, "working");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(working);
        try
        {
            var source = Path.Combine(root, "tests", "native", "native-dx11-shader-path-probe.c");
            var dll = Path.Combine(install, "native-dx11-shader-path-probe.dll");
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
                WorkingDirectory = directory,
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /TC /LD /Foshader-path.obj \"{source}\" /link /OUT:\"{dll}\"\"",
            };
            build.Environment["PATH"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer") + ";" + build.Environment["PATH"];
            _ = await RunAsync(build, TimeSpan.FromSeconds(60)).ConfigureAwait(true);
            Assert.That(File.Exists(dll), Is.True);

            var code = DirectWindowDeviceShaderCode(CompileDirectWindowDevicePipeline());
            var injected = code with {
                NativeImports = [.. code.NativeImports.Select(import => import.ExportName switch {
                    "GetModuleFileNameW" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiShaderPathGetModuleFileNameW",
                    },
                    "GetLastError" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiShaderPathGetLastError",
                    },
                    "GetProcAddress" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiShaderPathGetProcAddress",
                    },
                    _ => import,
                })],
            };
            var executable = Path.Combine(install, "pipeline.exe");
            await File.WriteAllBytesAsync(executable, PeImage.Build(injected)).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(working, "triangle.hlsl"),
                "Valid CWD decoy that the synthetic module-path query must ignore.").ConfigureAwait(true);

            var prefix246 = "C:\\" + new string('a', 242) + "\\";
            var prefix247 = "C:\\" + new string('a', 243) + "\\";
            (string Mode, string? Fixture, string Trace, string? Result)[] cases = [
                ("zero", null, "QE", null),
                ("truncated", null, "Q", null),
                ("fixture", "C:\\pipeline.exe", "QC", "C:\\triangle.hlsl"),
                ("fixture", "\\\\server\\share\\pipeline.exe", "QC", "\\\\server\\share\\triangle.hlsl"),
                ("fixture", "\\\\?\\C:\\root\\pipeline.exe", "QC", "\\\\?\\C:\\root\\triangle.hlsl"),
                ("fixture", "C:\\tools\\\ud83d\ude80\\pipeline.exe", "QC", "C:\\tools\\\ud83d\ude80\\triangle.hlsl"),
                ("fixture", prefix246 + "pipeline.exe", "QC", prefix246 + "triangle.hlsl"),
                ("fixture", prefix247 + "pipeline.exe", "Q", null),
                ("fixture", "pipeline.exe", "Q", null),
                ("fixture", "C:\\directory\\", "Q", null),
            ];
            for (var index = 0; index < cases.Length; index++)
            {
                var (mode, fixture, expectedTrace, expectedResult) = cases[index];
                var tracePath = Path.Combine(directory, $"trace-{index}.txt");
                var resultPath = Path.Combine(directory, $"result-{index}.bin");
                var start = new ProcessStartInfo(executable) {
                    WorkingDirectory = working,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                start.Environment["TERRUM_DX11_PATH_MODE"] = mode;
                start.Environment["TERRUM_DX11_PATH_TRACE"] = tracePath;
                start.Environment["TERRUM_DX11_PATH_RESULT"] = resultPath;
                if (fixture is not null)
                {
                    start.Environment["TERRUM_DX11_PATH_FIXTURE"] = fixture;
                }
                using var process = Process.Start(start)
                    ?? throw new InvalidOperationException("Synthetic path probe did not start.");
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                    Assert.That(process.ExitCode, Is.EqualTo(1));
                    Assert.That(await File.ReadAllTextAsync(tracePath).ConfigureAwait(true), Is.EqualTo(expectedTrace));
                    if (expectedResult is null)
                    {
                        Assert.That(File.Exists(resultPath), Is.False);
                    }
                    else
                    {
                        var result = Encoding.Unicode.GetString(await File.ReadAllBytesAsync(resultPath)
                            .ConfigureAwait(true)).TrimEnd('\0');
                        Assert.That(result, Is.EqualTo(expectedResult));
                    }
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
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task RunDirectWindowDevicePipelineStageOnRealWarpAsync(string asset)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var image = NativeEmitter.EmitWithNativeImports(CompileDirectWindowDevicePipeline(), NativeRuntimePolicy.CheckedExceptions);
        var directory = Directory.CreateTempSubdirectory("terrum-window-device-pipeline-").FullName;
        var install = Path.Combine(directory, "install");
        var working = Path.Combine(directory, "working");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(working);
        try
        {
            var shader = Path.Combine(install, "triangle.hlsl");
            var decoy = asset == "valid" ? "Deliberately invalid CWD decoy."
                : await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "samples",
                    "design", "win32-dx11-triangle", "triangle.hlsl")).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(working, "triangle.hlsl"), decoy).ConfigureAwait(true);
            if (asset == "valid")
            {
                File.Copy(Path.Combine(FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "triangle.hlsl"), shader);
            }
            else if (asset == "invalid")
            {
                await File.WriteAllTextAsync(shader, "Deliberately invalid HLSL.").ConfigureAwait(true);
            }
            else if (asset == "missing-pixel")
            {
                await File.WriteAllTextAsync(shader,
                    "float4 VertexMain(float3 position : POSITION) : SV_POSITION { return float4(position, 1); }")
                    .ConfigureAwait(true);
            }
            else if (asset == "input-signature")
            {
                await File.WriteAllTextAsync(shader,
                    "float4 VertexMain(float3 position : COLOR) : SV_POSITION { return float4(position, 1); }"
                    + "float4 PixelMain() : SV_TARGET { return float4(0, 1, 1, 1); }").ConfigureAwait(true);
            }
            var executable = Path.Combine(install, "pipeline.exe");
            await File.WriteAllBytesAsync(executable, image).ConfigureAwait(true);
            Assert.That(Directory.EnumerateFiles(install, "*.dll"), Is.Empty);
            if (asset == "valid")
            {
                var relocated = Path.Combine(directory, "relocated");
                Directory.Move(install, relocated);
                executable = Path.Combine(relocated, "pipeline.exe");
            }
            using var process = Process.Start(new ProcessStartInfo(executable) {
                WorkingDirectory = working,
                UseShellExecute = false,
                CreateNoWindow = false,
            }) ?? throw new InvalidOperationException("Direct pipeline stage did not start.");
            try
            {
                if (asset != "valid")
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
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
                    await Task.Delay(50).ConfigureAwait(true);
                }
                Assert.That(process.HasExited, Is.False, "The no-application-library pipeline executable failed.");
                Assert.That(window, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(process.MainWindowTitle, Is.EqualTo("Terrum DX11 Triangle"));
                Assert.That(IsWindowVisible(window), Is.True);
                Assert.That(PostMessageW(window, WindowCloseMessage, IntPtr.Zero, IntPtr.Zero), Is.True);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                Assert.That(process.ExitCode, Is.EqualTo(0));
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
