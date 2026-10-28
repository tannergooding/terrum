using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.Storage;

public sealed class NativeRootPinLoadedTypesTests
{
    [Test]
    public async Task LoadedTypeAdmissionIsExactAndTransactionalAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        Assume.That(File.Exists(vswhere), "Visual Studio discovery is required to build the RootPin provider probe.");

        var discover = new ProcessStartInfo(vswhere) { WorkingDirectory = root };
        foreach (var argument in new[] {
            "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "-property", "installationPath",
        })
        {
            discover.ArgumentList.Add(argument);
        }
        var installation = (await RunProcessAsync(discover, TimeSpan.FromSeconds(10))
            .ConfigureAwait(true)).Trim();
        var environment = Path.Combine(installation, "VC", "Auxiliary", "Build", "vcvars64.bat");
        Assume.That(File.Exists(environment), "Visual Studio x64 C++ build tools are required for the RootPin provider probe.");

        var directory = TestArtifactPaths.CreateDirectory("terrum-rootpin-loaded-types-");
        var providerPath = Path.Combine(directory, "terrum-root-pin-runtime.dll");
        var providerObjectPath = Path.Combine(directory, "root-pin-loaded-types.obj");
        var probePath = Path.Combine(directory, "root-pin-loaded-types-probe.exe");
        var probeObjectPath = Path.Combine(directory, "root-pin-loaded-types-probe.obj");
        var providerSource = Path.Combine(root, "sources", "Terrum.RootPin.Runtime", "RootPinLoadedTypesV2.cpp");
        var providerHeader = Path.Combine(root, "sources", "Terrum.RootPin.Runtime", "RootPinLoadedTypesV2.h");
        var probeSource = Path.Combine(root, "tests", "Terrum.CodeAnalysis.Tests",
            "Native", "Storage", "RootPinLoadedTypesProbe.c");
        var testSource = Path.Combine(root, "tests", "Terrum.CodeAnalysis.Tests",
            "Native", "Storage", "NativeRootPinLoadedTypesTests.cs");
        var includePath = Path.Combine(root, "sources", "Terrum.RootPin.Runtime");
        try
        {
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is unset.")) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /Bv /nologo /W4 /WX /O2 /MT /EHsc /std:c++20 /D TERRUM_ROOT_PIN_TESTS /D TERRUM_ROOT_PIN_EXPORTS /LD /Fo\"{providerObjectPath}\" /Fe\"{providerPath}\" \"{providerSource}\" /link /incremental:no && cl /Bv /nologo /W4 /WX /O2 /MT /D TERRUM_ROOT_PIN_TESTS /TC /I\"{includePath}\" /Fo\"{probeObjectPath}\" /Fe\"{probePath}\" \"{probeSource}\" /link /incremental:no\"",
                WorkingDirectory = root,
            };
            var buildOutput = await RunProcessAsync(build, TimeSpan.FromMinutes(2)).ConfigureAwait(true);
            TestContext.WriteLine($"native-build-command={build.Arguments}");
            TestContext.WriteLine(buildOutput);

            var probe = new ProcessStartInfo(probePath) { WorkingDirectory = root };
            probe.ArgumentList.Add(providerPath);
            var output = await RunProcessAsync(probe, TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            Assert.That(output, Does.Contain("rootpin-loaded-types=pass"));
            TestContext.WriteLine(output);
            TestContext.WriteLine($"test-assembly-sha256={GetSha256(typeof(NativeRootPinLoadedTypesTests).Assembly.Location)}");
            TestContext.WriteLine($"test-source-sha256={GetSha256(testSource)}");
            TestContext.WriteLine($"provider-source-sha256={GetSha256(providerSource)}");
            TestContext.WriteLine($"provider-header-sha256={GetSha256(providerHeader)}");
            TestContext.WriteLine($"probe-source-sha256={GetSha256(probeSource)}");
            TestContext.WriteLine($"provider-dll-sha256={GetSha256(providerPath)}");
            TestContext.WriteLine($"probe-exe-sha256={GetSha256(probePath)}");

            var invalidSupportProbe = new ProcessStartInfo(probePath) { WorkingDirectory = root };
            invalidSupportProbe.ArgumentList.Add(providerPath);
            invalidSupportProbe.ArgumentList.Add("invalid-support");
            await RunProcessAsync(invalidSupportProbe, TimeSpan.FromSeconds(10),
                unchecked((int)0xC0000409)).ConfigureAwait(true);
            TestContext.WriteLine("invalid-support-contract-violation=expected-fast-fail");
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory).ConfigureAwait(true);
        }
    }

    private static string GetSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task<string> RunProcessAsync(
        ProcessStartInfo start,
        TimeSpan timeout,
        int expectedExitCode = 0)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start the RootPin native probe process.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(true);
            }
            throw;
        }

        var standardOutput = await output.ConfigureAwait(true);
        var standardError = await error.ConfigureAwait(true);
        Assert.That(process.ExitCode, Is.EqualTo(expectedExitCode),
            $"RootPin native probe exited with {process.ExitCode}, expected {expectedExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        return standardOutput + standardError;
    }
}
