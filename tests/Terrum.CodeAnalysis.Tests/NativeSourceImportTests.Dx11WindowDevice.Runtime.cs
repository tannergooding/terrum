// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
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
    public async Task DirectWindowDeviceRuntimeProbeCleansDx11BeforeItsWindow()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(false);
        var directory = Directory.CreateTempSubdirectory("terrum-window-device-runtime-").FullName;
        try
        {
            var fixture = Path.Combine(root, "tests", "native", "native-dx11-window-device-probe.c");
            var dll = Path.Combine(directory, "native-dx11-window-device-probe.dll");
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
                WorkingDirectory = directory,
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /Od /TC /LD /Fo\"{directory}\\fixture.obj\" /Fe\"{dll}\" \"{fixture}\" /link /incremental:no\"",
            };
            build.Environment["PATH"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer") + ";" + build.Environment["PATH"];
            _ = await RunAsync(build, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            Assert.That(File.Exists(Path.Combine(directory, "fixture.obj")), Is.True);

            var program = CompileDirectWindowDevice();
            Assert.That(NativeDx11WindowAcquisitionContract.MatchesProgram(program), Is.True);
            SemanticVerifier.VerifyReferences(program);
            var bindings = program.Functions.Select((function, index) => (function, index))
                .Where(static item => item.function.IsExternal)
                .ToDictionary(static item => item.index, item => {
                    Assert.That(NativeImportCatalog.WindowsX64.TryGetImport(
                        item.function.NativeImportKey ?? "", out var import), Is.True);
                    Assert.That(import, Is.Not.Null);
                    return new NativeImportBinding(item.function.Name, import.DllName, import.ExportName);
                });
            var code = new PeCompiler(program, default, managedNodes: false, imports: bindings).Compile();
            var fixtureName = Path.GetFileName(dll);
            var injected = code with {
                NativeImports = [.. code.NativeImports.Select(import => import.ExportName switch {
                    "RegisterClassExW" => import with {
                        DllName = fixtureName, ExportName = "NativeFfiWindowDeviceRegisterClassExW",
                    },
                    "UnregisterClassW" => import with {
                        DllName = fixtureName, ExportName = "NativeFfiWindowDeviceUnregisterClassW",
                    },
                    "CreateWindowExW" => import with {
                        DllName = fixtureName, ExportName = "NativeFfiWindowDeviceCreateWindowExW",
                    },
                    "DestroyWindow" => import with {
                        DllName = fixtureName, ExportName = "NativeFfiWindowDeviceDestroyWindow",
                    },
                    "ShowWindow" => import with {
                        DllName = fixtureName, ExportName = "NativeFfiWindowDeviceShowWindow",
                    },
                    "GetMessageW" => import with {
                        DllName = fixtureName, ExportName = "NativeFfiWindowDeviceGetMessageW",
                    },
                    "TranslateMessage" => import with {
                        DllName = fixtureName, ExportName = "NativeFfiWindowDeviceTranslateMessage",
                    },
                    "DispatchMessageW" => import with {
                        DllName = fixtureName, ExportName = "NativeFfiWindowDeviceDispatchMessageW",
                    },
                    "DefWindowProcW" => import with {
                        DllName = fixtureName, ExportName = "NativeFfiWindowDeviceDefWindowProcW",
                    },
                    "PostQuitMessage" => import with {
                        DllName = fixtureName, ExportName = "NativeFfiWindowDevicePostQuitMessage",
                    },
                    "D3D11CreateDeviceAndSwapChain" => import with {
                        DllName = fixtureName, ExportName = "NativeFfiWindowDeviceCreateDeviceAndSwapChain",
                    },
                    _ => import,
                })],
            };
            foreach (var export in new[] {
                "RegisterClassExW", "UnregisterClassW", "CreateWindowExW", "DestroyWindow", "ShowWindow",
                "GetMessageW", "TranslateMessage", "DispatchMessageW", "DefWindowProcW", "PostQuitMessage",
                "D3D11CreateDeviceAndSwapChain",
            })
            {
                Assert.That(code.NativeImports, Has.Some.Matches(import => import.ExportName == export));
            }

            var executable = Path.Combine(directory, "window-device-runtime.exe");
            await File.WriteAllBytesAsync(executable, PeImage.Build(injected)).ConfigureAwait(false);
            foreach (var (mode, expectedExit, expectedTrace) in new (int Mode, int? ExpectedExit, string ExpectedTrace)[] {
                (1, null, "rZ"),
                (2, null, "RwUZ"),
                (3, 1, "RWaXUZ"),
                (4, 1, "RWAH!csdXUZ"),
                (0, 0, "RWAHMTPQecsdXUZ"),
            })
            {
                var report = Path.Combine(directory, $"trace-{mode}.txt");
                var start = new ProcessStartInfo(executable) { WorkingDirectory = directory };
                start.Environment["TERRUM_WINDOW_DEVICE_MODE"] =
                    mode.ToString(System.Globalization.CultureInfo.InvariantCulture);
                start.Environment["TERRUM_WINDOW_DEVICE_REPORT"] = report;
                using var process = Process.Start(start)
                    ?? throw new InvalidOperationException("Injected window-device probe did not start.");
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    Assert.That(await File.ReadAllTextAsync(report).ConfigureAwait(false), Is.EqualTo(expectedTrace));
                    if (expectedExit is int exitCode)
                    {
                        Assert.That(process.ExitCode, Is.EqualTo(exitCode));
                    }
                    else
                    {
                        Assert.That(process.ExitCode, Is.Not.EqualTo(0));
                    }
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
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
