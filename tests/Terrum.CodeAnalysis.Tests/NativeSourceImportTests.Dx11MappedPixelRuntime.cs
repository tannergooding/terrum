// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public Task Dx11SourceAuthoredMappedPixelEvidenceReadsBothRealWarpFramesAsync() =>
        RunSourceAuthoredMappedPixelEvidenceAsync(hardware: false, wrongTriangle: false, expectedExit: 0);

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public Task Dx11SourceAuthoredMappedPixelEvidenceRejectsWrongTriangleOnRealWarpAsync() =>
        RunSourceAuthoredMappedPixelEvidenceAsync(hardware: false, wrongTriangle: true, expectedExit: 1);

    [HardwareIntegrationTest]
    [Category("HardwareIntegration")]
    public Task Dx11SourceAuthoredMappedPixelEvidenceReadsBothRealHardwareFramesAsync() =>
        RunSourceAuthoredMappedPixelEvidenceAsync(hardware: true, wrongTriangle: false, expectedExit: 0);

    [HardwareIntegrationTest]
    [Category("HardwareIntegration")]
    public Task Dx11SourceAuthoredMappedPixelEvidenceRejectsWrongTriangleOnRealHardwareAsync() =>
        RunSourceAuthoredMappedPixelEvidenceAsync(hardware: true, wrongTriangle: true, expectedExit: 1);

    private async Task RunSourceAuthoredMappedPixelEvidenceAsync(
        bool hardware, bool wrongTriangle, int expectedExit)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var source = hardware ? HardwareMappedPixelEvidenceSource : MappedPixelEvidenceSource;
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        SemanticVerifier.VerifyReferences(program);
        Assert.That(NativeDx11DirectPipelineContract.MatchesCompatibleProgram(program), Is.True);
        AssertWindowsSystemImportsOnly(DirectWindowDeviceShaderCode(program));

        var directory = Directory.CreateTempSubdirectory(hardware
            ? "terrum-dx11-mapped-pixels-hardware-" : "terrum-dx11-mapped-pixels-warp-").FullName;
        try
        {
            var executable = Path.Combine(directory, "mapped-pixel-evidence.exe");
            var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);
            Assert.That(image.AsSpan().IndexOf("native-dx11"u8) < 0, Is.True);
            await File.WriteAllBytesAsync(executable, image).ConfigureAwait(true);

            var shader = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "samples", "design",
                "win32-dx11-triangle", "triangle.hlsl")).ConfigureAwait(true);
            Assert.That(shader.Contains("float4(0.1f, 0.8f, 0.9f, 1.0f)", StringComparison.Ordinal), Is.True);
            if (wrongTriangle)
            {
                shader = shader.Replace("float4(0.1f, 0.8f, 0.9f, 1.0f)",
                    "float4(1.0f, 0.0f, 0.0f, 1.0f)", StringComparison.Ordinal);
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "triangle.hlsl"), shader)
                .ConfigureAwait(true);

            var (exitCode, output, error) = await RunNativeExecutableAsync(
                new ProcessStartInfo(executable) { WorkingDirectory = directory }).ConfigureAwait(true);
            Assert.That(exitCode == expectedExit, Is.True, $"Source-authored {(hardware ? "hardware" : "WARP")} mapped pixel evidence exited "
                    + $"{exitCode}, expected {expectedExit}: {error}");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertWindowsSystemImportsOnly(NativeCode code)
    {
        string[] allowed = ["kernel32.dll", "user32.dll", "ntdll.dll",
            Path.Combine(Environment.SystemDirectory, "user32.dll")];
        Assert.Multiple(() => {
            foreach (var import in code.NativeImports)
                Assert.That(allowed.Contains(import.DllName, StringComparer.OrdinalIgnoreCase), Is.True, import.DllName);
        });
    }

    private static async Task<(int Exit, string Output, string Error)> RunNativeExecutableAsync(
        ProcessStartInfo start)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The native test executable did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
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
