using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
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
        var missingExportDirectory = Path.Combine(directory, "missing-export");
        Directory.CreateDirectory(missingExportDirectory);
        var missingExportProviderPath = Path.Combine(missingExportDirectory,
            "terrum-root-pin-runtime.dll");
        var missingExportProviderObjectPath = Path.Combine(missingExportDirectory,
            "root-pin-missing-export.obj");
        var concurrentProviderDirectory = Path.Combine(directory, "concurrent");
        Directory.CreateDirectory(concurrentProviderDirectory);
        var concurrentProviderPath = Path.Combine(concurrentProviderDirectory,
            "terrum-root-pin-runtime.dll");
        var concurrentProviderObjectPath = Path.Combine(concurrentProviderDirectory,
            "root-pin-concurrent.obj");
        var runtimeProviderDirectory = Path.Combine(AppContext.BaseDirectory, "RootPinRuntimeProvider");
        Directory.CreateDirectory(runtimeProviderDirectory);
        var productionProviderPath = Path.Combine(runtimeProviderDirectory, "terrum-root-pin-runtime.dll");
        var productionProviderObjectPath = Path.Combine(runtimeProviderDirectory, "root-pin-loaded-types-production.obj");
        var probePath = Path.Combine(directory, "root-pin-loaded-types-probe.exe");
        var probeObjectPath = Path.Combine(directory, "root-pin-loaded-types-probe.obj");
        var runtimeAbiProbePath = Path.Combine(directory, "root-pin-runtime-abi-probe.exe");
        var runtimeAbiProbeObjectPath = Path.Combine(directory, "root-pin-runtime-abi-probe.obj");
        var providerSource = Path.Combine(root, "sources", "Terrum.RootPin.Runtime", "RootPinLoadedTypesV2.cpp");
        var providerHeader = Path.Combine(root, "sources", "Terrum.RootPin.Runtime", "RootPinLoadedTypesV2.h");
        var probeSource = Path.Combine(root, "tests", "Terrum.CodeAnalysis.Tests",
            "Native", "Storage", "RootPinLoadedTypesProbe.c");
        var runtimeAbiProbeSource = Path.Combine(root, "tests", "Terrum.CodeAnalysis.Tests",
            "Native", "Storage", "RootPinRuntimeAbiProbe.c");
        var testSource = Path.Combine(root, "tests", "Terrum.CodeAnalysis.Tests",
            "Native", "Storage", "NativeRootPinLoadedTypesTests.cs");
        var testProjectPath = Path.Combine(root, "tests",
            "Terrum.CodeAnalysis.Tests", "Terrum.CodeAnalysis.Tests.csproj");
        var includePath = Path.Combine(root, "sources", "Terrum.RootPin.Runtime");
        Assume.That(Path.IsPathFullyQualified(productionProviderPath)
            && productionProviderPath.All(static character => character is >= ' ' and <= '~'),
            "The selected RootPin provider path must be absolute ASCII.");
        try
        {
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is unset.")) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /Bv /nologo /W4 /WX /O2 /MT /EHsc /std:c++20 /D TERRUM_ROOT_PIN_TESTS /D TERRUM_ROOT_PIN_EXPORTS /LD /Fo\"{providerObjectPath}\" /Fe\"{providerPath}\" \"{providerSource}\" /link /incremental:no && cl /Bv /nologo /W4 /WX /O2 /MT /EHsc /std:c++20 /D TERRUM_ROOT_PIN_EXPORTS /LD /Fo\"{productionProviderObjectPath}\" /Fe\"{productionProviderPath}\" \"{providerSource}\" /link /incremental:no && cl /Bv /nologo /W4 /WX /O2 /MT /EHsc /std:c++20 /D TERRUM_ROOT_PIN_EXPORTS /D TERRUM_ROOT_PIN_TEST_OMIT_METHOD_ADMISSION_EXPORT /LD /Fo\"{missingExportProviderObjectPath}\" /Fe\"{missingExportProviderPath}\" \"{providerSource}\" /link /incremental:no && cl /Bv /nologo /W4 /WX /O2 /MT /EHsc /std:c++20 /D TERRUM_ROOT_PIN_EXPORTS /D TERRUM_ROOT_PIN_TEST_DELAY_RUNTIME_GETTER /LD /Fo\"{concurrentProviderObjectPath}\" /Fe\"{concurrentProviderPath}\" \"{providerSource}\" /link /incremental:no && cl /Bv /nologo /W4 /WX /O2 /MT /D TERRUM_ROOT_PIN_TESTS /TC /I\"{includePath}\" /Fo\"{probeObjectPath}\" /Fe\"{probePath}\" \"{probeSource}\" /link /incremental:no && cl /Bv /nologo /W4 /WX /O2 /MT /TC /I\"{includePath}\" /Fo\"{runtimeAbiProbeObjectPath}\" /Fe\"{runtimeAbiProbePath}\" \"{runtimeAbiProbeSource}\" /link /incremental:no\"",
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

            var runtimeAbiProbe = new ProcessStartInfo(runtimeAbiProbePath) { WorkingDirectory = root };
            runtimeAbiProbe.ArgumentList.Add(productionProviderPath);
            var runtimeAbiOutput = await RunProcessAsync(runtimeAbiProbe, TimeSpan.FromSeconds(10))
                .ConfigureAwait(true);
            Assert.That(runtimeAbiOutput, Does.Contain("rootpin-runtime-abi=pass"));
            TestContext.WriteLine(runtimeAbiOutput);
            Assert.That(NativeJit.ExecuteImportedCode(
                CreateRootPinRuntimeGetterCode(productionProviderPath), [], []), Is.Zero);
            TestContext.WriteLine("rootpin-jit-provider=pass");
            var alternateProviderDirectory = Path.Combine(directory, "alternate-provider");
            Directory.CreateDirectory(alternateProviderDirectory);
            var alternateProviderPath = Path.Combine(alternateProviderDirectory,
                Path.GetFileName(productionProviderPath));
            File.Copy(productionProviderPath, alternateProviderPath);
            var conflictingProvider = Assert.Throws<NotSupportedException>(() =>
                NativeJit.ExecuteImportedCode(
                    CreateRootPinRuntimeGetterCode(alternateProviderPath), [], []));
            Assert.That(conflictingProvider!.Message,
                Does.Contain("A process cannot select a second RootPin provider path."));
            TestContext.WriteLine("rootpin-jit-conflicting-provider-path=reject");
            var missingExportBootstrap = await RunRootPinProviderBootstrapChildAsync(
                root, testProjectPath, missingExportProviderPath, "missing-export")
                .ConfigureAwait(true);
            Assert.That(missingExportBootstrap, Does.Contain("Passed!"));
            TestContext.WriteLine("rootpin-jit-missing-export-and-failure-state=reject");
            var concurrentBootstrap = await RunRootPinProviderBootstrapChildAsync(
                root, testProjectPath, concurrentProviderPath, "concurrent")
                .ConfigureAwait(true);
            Assert.That(concurrentBootstrap, Does.Contain("Passed!"));
            TestContext.WriteLine("rootpin-jit-concurrent-bootstrap=pass");
            TestContext.WriteLine($"test-assembly-sha256={GetSha256(typeof(NativeRootPinLoadedTypesTests).Assembly.Location)}");
            TestContext.WriteLine($"test-source-sha256={GetSha256(testSource)}");
            TestContext.WriteLine($"provider-source-sha256={GetSha256(providerSource)}");
            TestContext.WriteLine($"provider-header-sha256={GetSha256(providerHeader)}");
            TestContext.WriteLine($"probe-source-sha256={GetSha256(probeSource)}");
            TestContext.WriteLine($"runtime-abi-probe-source-sha256={GetSha256(runtimeAbiProbeSource)}");
            TestContext.WriteLine($"provider-dll-sha256={GetSha256(providerPath)}");
            TestContext.WriteLine($"probe-exe-sha256={GetSha256(probePath)}");
            TestContext.WriteLine($"production-provider-dll-sha256={GetSha256(productionProviderPath)}");
            TestContext.WriteLine($"runtime-abi-probe-exe-sha256={GetSha256(runtimeAbiProbePath)}");
            TestContext.WriteLine($"missing-export-provider-dll-sha256={GetSha256(missingExportProviderPath)}");
            TestContext.WriteLine($"concurrent-provider-dll-sha256={GetSha256(concurrentProviderPath)}");

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

    [Test]
    public async Task RootPinProviderBootstrapChildAsync()
    {
        var childMarker = Environment.GetEnvironmentVariable(
            "TERRUM_ROOT_PIN_BOOTSTRAP_CHILD");
        var providerPath = Environment.GetEnvironmentVariable(
            "TERRUM_ROOT_PIN_BOOTSTRAP_PROVIDER");
        var mode = Environment.GetEnvironmentVariable(
            "TERRUM_ROOT_PIN_BOOTSTRAP_MODE");
        if (childMarker is null)
        {
            return;
        }
        if (childMarker != "1" || providerPath is null || mode is null)
        {
            throw new InvalidOperationException(
                "The isolated RootPin bootstrap child lacks its provider selection.");
        }

        var code = CreateRootPinRuntimeGetterCode(providerPath);
        if (mode == "missing-export")
        {
            var missingExport = Assert.Throws<NotSupportedException>(() =>
                NativeJit.ExecuteImportedCode(code, [], []));
            Assert.That(missingExport!.Message,
                Does.Contain("lacks same-image runtime or admission exports"));
            var failedState = Assert.Throws<NotSupportedException>(() =>
                NativeJit.ExecuteImportedCode(code, [], []));
            Assert.That(failedState!.Message,
                Does.Contain("provider selection has already failed"));
            return;
        }
        if (mode == "concurrent")
        {
            using var start = new ManualResetEventSlim();
            var calls = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                start.Wait();
                return NativeJit.ExecuteImportedCode(code, [], []);
            })).ToArray();
            start.Set();
            var results = await Task.WhenAll(calls)
                .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            foreach (var result in results)
            {
                Assert.That(result, Is.Zero);
            }
            return;
        }

        Assert.Fail($"Unknown RootPin provider bootstrap mode '{mode}'.");
    }

    private static string GetSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static Task<string> RunRootPinProviderBootstrapChildAsync(
        string root, string projectPath, string providerPath, string mode)
    {
        var child = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
        };
        child.ArgumentList.Add("test");
        child.ArgumentList.Add(projectPath);
        child.ArgumentList.Add("--no-build");
        child.ArgumentList.Add("--no-restore");
        child.ArgumentList.Add("--filter");
        child.ArgumentList.Add(
            "FullyQualifiedName=Terrum.CodeAnalysis.Tests.Native.Storage.NativeRootPinLoadedTypesTests.RootPinProviderBootstrapChildAsync");
        child.Environment["TERRUM_ROOT_PIN_BOOTSTRAP_CHILD"] = "1";
        child.Environment["TERRUM_ROOT_PIN_BOOTSTRAP_PROVIDER"] = providerPath;
        child.Environment["TERRUM_ROOT_PIN_BOOTSTRAP_MODE"] = mode;
        return RunProcessAsync(child, TimeSpan.FromSeconds(60));
    }

    private static NativeCode CreateRootPinRuntimeGetterCode(string providerPath)
    {
        byte[] code = [
            0x48, 0x81, 0xEC, 0x28, 0, 0, 0,
            0xFF, 0x15, 0, 0, 0, 0,
            0x31, 0xC0,
            0x48, 0x81, 0xC4, 0x28, 0, 0, 0,
            0xC3,
        ];
        return new NativeCode(code, 0, [new NativeFunctionRange(0, code.Length, 40)], []) {
            NativeImports = [
                new NativeImportFixup(9, providerPath, "TerrumRootPinGetRuntimeV1"),
            ],
        };
    }

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
