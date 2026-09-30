// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ScopedChoose(int input);

    [Fact]
    public async Task PersistedScopedLocalsRunAndBuildFromSourceFreeArtifactAsync()
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-scoped-cli-{Guid.NewGuid():N}");
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
            Assert.True(compiled == 0, $"{compileOutput}{Environment.NewLine}{compileError}");
            var (oldProfile, _, _) = await RunAsync([], "validate-trrm-compact-linear",
                artifact, "--json");
            Assert.Equal(1, oldProfile);
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == 14, $"{runOutput}{Environment.NewLine}{runError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.Equal(NativeEmitter.Emit(TrRmExecutableCompiler.Load(await File.ReadAllBytesAsync(artifact)),
                NativeRuntimePolicy.FailFast), await File.ReadAllBytesAsync(executable));
            Assert.Equal(14, await ExecuteTrRmImageAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }

    [Fact]
    public async Task PersistedScopedLibraryBuildsAndExportsAsync()
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-scoped-export-{Guid.NewGuid():N}");
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
            Assert.True(compiled == 0, $"{compileOutput}{Environment.NewLine}{compileError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm-library",
                artifact, "--export", "Choose", "-o", library, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"{buildOutput}{Environment.NewLine}{buildError}");
            var handle = NativeLibrary.Load(library);
            try
            {
                var choose = Marshal.GetDelegateForFunctionPointer<ScopedChoose>(
                    NativeLibrary.GetExport(handle, "Choose"));
                Assert.Equal(42, choose(0));
                Assert.Equal(9, choose(7));
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

    [Theory]
    [InlineData("int32 Main() { if (true) { int32 local = 1 / 0; return local; } return 0; }")]
    [InlineData("int32 Main() { if (true) { int32 local = 42; Fail(local); return local; } return 0; } void Fail(int32 value) { int32 fault = value / 0; }")]
    public async Task ScopedCheckedFailureIsObservedOnlyInSubprocessAsync(string source)
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-scoped-failure-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        const int status = unchecked((int)0xE0540002);
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "scoped-failure", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.True(compiled == 0, $"{compileOutput}{Environment.NewLine}{compileError}");
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == status, $"{runOutput}{Environment.NewLine}{runError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm",
                artifact, "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.Equal(status, await ExecuteTrRmImageAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }
}
