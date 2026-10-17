// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeProviderOutputFailureTests
{
    [Test]
    [Explicit("Requires Windows x64, Visual Studio x64 C++ tools, D3DCompiler, and D3D11 WARP.")]
    public async Task Dx11ProviderFailureOutputStatesAreObservedForSelectedPathsAsync()
    {
        Assert.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);

        var root = FindRepositoryRoot();
        var directory = Directory.CreateTempSubdirectory("terrum-dx11-failure-outputs-").FullName;
        try
        {
            var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer", "vswhere.exe");
            var discover = new ProcessStartInfo(vswhere);
            foreach (var argument in new[] {
                "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
                "-property", "installationPath",
            })
            {
                discover.ArgumentList.Add(argument);
            }

            var discovery = await RunProcessAsync(discover, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(discovery.ExitCode, Is.EqualTo(0), discovery.Error);
            var installation = discovery.Output.Trim();
            var environment = Path.Combine(installation, "VC", "Auxiliary", "Build", "vcvars64.bat");
            Assert.That(File.Exists(environment), Is.True, environment);

            var fixture = Path.Combine(root, "tests", "native", "dx11-native-failure-output-probe.cpp");
            var shader = Path.Combine(root, "samples", "design", "win32-dx11-triangle", "triangle.hlsl");
            var executable = Path.Combine(directory, "dx11-native-failure-output-probe.exe");
            File.Copy(shader, Path.Combine(directory, "triangle.hlsl"));
            var compile = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
                WorkingDirectory = directory,
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /EHsc /std:c++20 /Fe\"{executable}\" \"{fixture}\" /link /incremental:no d3d11.lib d3dcompiler.lib user32.lib\"",
            };
            TestContext.Out.WriteLine($"Compiler environment: {environment}");
            var build = await RunProcessAsync(compile, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            TestContext.Out.WriteLine($"Build exit: {build.ExitCode}\n{build.Output}{build.Error}");
            Assert.That(build.ExitCode, Is.EqualTo(0), build.Output + build.Error);

            var start = new ProcessStartInfo(executable) { WorkingDirectory = directory };
            var result = await RunProcessAsync(start, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            TestContext.Out.WriteLine($"Probe exit: {result.ExitCode}\n{result.Output}{result.Error}");
            Assert.That(result.ExitCode, Is.EqualTo(0), result.Output + result.Error);
            foreach (var operation in new[] {
                "D3DCompileFromFile-code",
                "D3DCompileFromFile-diagnostics",
                "D3D11CreateDeviceAndSwapChain-invalid-window",
                "IDXGISwapChain::GetBuffer-invalid-index",
                "ID3D11Device::CreateVertexShader-invalid-bytecode",
                "ID3D11Device::CreatePixelShader-invalid-bytecode",
                "ID3D11Device::CreateRenderTargetView-null-resource",
            })
            {
                Assert.That(result.Output.Contains(operation, StringComparison.Ordinal), Is.True,
                    $"The native probe did not report {operation}.");
            }
            Assert.That(result.Output,
                Does.Contain("D3DCompileFromFile-diagnostics: output=non-null"));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory).ConfigureAwait(false);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "Could not find global.json above the test output directory.");
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        ProcessStartInfo start, TimeSpan timeout)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {start.FileName}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }

            throw;
        }

        return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }
}
