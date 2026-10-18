// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ScopedChoose(int input);

    [Test]
    public async Task PersistedScopedLocalsRunAndBuildFromSourceFreeArtifactAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-scoped-cli-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        const string source = """
            int32 Main() {
                mut int32 count = 0;
                mut int32 sum = 0;
                while (count < 4) {
                    int32 before = count++;
                    { mut int32 local = before + 1; local++; sum += local; }
                    if (before == 1) { continue; }
                    if (before == 3) { break; }
                }
                return sum;
            }
            """;
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "scoped-cli", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            var (oldProfile, _, _) = await RunAsync([], "validate-trrm-compact-linear",
                artifact, "--json");
            Assert.That(oldProfile, Is.EqualTo(1));
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 14, Is.True, $"{runOutput}{Environment.NewLine}{runError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(await File.ReadAllBytesAsync(executable), Is.EqualTo(NativeEmitter.Emit(TrRmExecutableCompiler.Load(await File.ReadAllBytesAsync(artifact)),
                NativeRuntimePolicy.FailFast)));
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(14));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedScopedLibraryBuildsAndExportsAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-scoped-export-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var library = prefix + ".dll";
        const string source = """
            public int32 Choose(int32 input) {
                mut int32 result = 0;
                if (input > 0) { int32 local = input + 2; result = local; }
                else { int32 local = 42; result = local; }
                return result;
            }
            """;
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "scoped-export", "-o", artifact, "--runtime", "fail-fast", "--library", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm-library",
                artifact, "--export", "Choose", "-o", library, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            var handle = NativeLibrary.Load(library);
            try
            {
                var choose = Marshal.GetDelegateForFunctionPointer<ScopedChoose>(
                    NativeLibrary.GetExport(handle, "Choose"));
                Assert.That(choose(0), Is.EqualTo(42));
                Assert.That(choose(7), Is.EqualTo(9));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(library);
        }
    }
    [TestCase("int32 Main() { if (true) { int32 local = 1 / 0; return local; } return 0; }")]
    [TestCase("int32 Main() { if (true) { int32 local = 42; Fail(local); return local; } return 0; } void Fail(int32 value) { int32 fault = value / 0; }")]
    public async Task ScopedCheckedFailureIsObservedOnlyInSubprocessAsync(string source)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-scoped-failure-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        const int status = unchecked((int)0xE0540002);
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "scoped-failure", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == status, Is.True, $"{runOutput}{Environment.NewLine}{runError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm",
                artifact, "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(status));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }
}
