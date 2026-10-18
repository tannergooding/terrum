// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.Interop;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeProviderOutputFailureTests
{
    [Test]
    public void Dx11ProviderFailureObservationsEmitWithoutExecutingWarp()
    {
        Assert.That(EmitProviderObservations(), Is.Not.Empty);
    }

    [Test]
    [Explicit("Requires Windows x64, D3DCompiler, and D3D11 WARP; creates one hidden window.")]
    public async Task Dx11ProviderFailureOutputStatesAreObservedForSelectedPathsAsync()
    {
        Assert.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var directory = TestArtifactPaths.CreateDirectory("terrum-dx11-failure-outputs-");
        try
        {
            var executable = Path.Combine(directory, "dx11-failure-outputs.exe");
            await File.WriteAllBytesAsync(executable, EmitProviderObservations()).ConfigureAwait(false);
            File.Copy(Path.Combine(RepositoryRoot(),
                "samples", "Win32Dx11Triangle", "triangle.hlsl"), Path.Combine(directory, "triangle.hlsl"));
            using var process = Process.Start(new ProcessStartInfo(executable) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Could not start the provider observation program.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(process.ExitCode, Is.Zero,
                    "The Terrum fixture returns distinct nonzero statuses for shader outputs, "
                    + "invalid-window acquisition, setup, GetBuffer, vertex/pixel shader, and RTV failures.");
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

    private static byte[] EmitProviderObservations()
    {
        var root = RepositoryRoot();
        var sample = Path.Combine(root, "samples", "Win32Dx11Triangle");
        var compilation = Compilation.Create([
            SyntaxTree.Parse(File.ReadAllText(Path.Combine(root,
                "tests", "Terrum.CodeAnalysis.Tests", "Native", "Interop", "Fixtures", "dx11-provider-failure-outputs.terrum"))),
            SyntaxTree.Parse(File.ReadAllText(Path.Combine(sample, "dx11-api.terrum"))),
            SyntaxTree.Parse(File.ReadAllText(Path.Combine(sample, "win32-api.terrum"))),
        ], BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeEmitter.EmitWithNativeImports(
            compilation.Program!, NativeRuntimePolicy.CheckedExceptions);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Could not find the repository above the test output directory.");
    }
}
