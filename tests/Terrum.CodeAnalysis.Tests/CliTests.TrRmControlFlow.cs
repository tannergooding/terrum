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
    private delegate int IndexedChoose(int input);

    [Fact]
    public async Task PersistedIndexedControlFlowRunsAndBuildsWithoutProducerSourceAsync()
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-indexed-cli-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        const string source = """
            int32 Main() {
                mut int32 x = 0;
                while (x < 6) {
                    x = x + 1;
                    if (x == 2) { continue; }
                    if (x == 4) { break; }
                }
                return x + 38;
            }
            """;
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "indexed-cli", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.True(compiled == 0, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.True(File.Exists(artifact), $"{compileOutput}{Environment.NewLine}{compileError}");

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == 42, $"{runOutput}{Environment.NewLine}{runError}");

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.Equal(NativeEmitter.Emit(TrRmExecutableCompiler.Load(await File.ReadAllBytesAsync(artifact)),
                NativeRuntimePolicy.FailFast), await File.ReadAllBytesAsync(executable));
            Assert.Equal(42, await ExecuteTrRmImageAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }

    [Fact]
    public async Task PersistedIndexedLibraryPreservesBranchExportAsync()
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-indexed-export-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var library = prefix + ".dll";
        const string source = """
            public int32 Choose(int32 input) {
                mut int32 result = input;
                if (input > 0) { result = input + 2; }
                else { result = 42; }
                return result;
            }
            """;
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "indexed-export", "-o", artifact, "--runtime", "fail-fast",
                "--library", "--json");
            Assert.True(compiled == 0, $"{compileOutput}{Environment.NewLine}{compileError}");
            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm-library", artifact, "--export", "Choose", "-o", library,
                "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.Equal(NativeEmitter.EmitLibrary(
                TrRmExecutableCompiler.Load(await File.ReadAllBytesAsync(artifact)),
                ["Choose"], NativeRuntimePolicy.FailFast), await File.ReadAllBytesAsync(library));
            var handle = NativeLibrary.Load(library);
            try
            {
                var choose = Marshal.GetDelegateForFunctionPointer<IndexedChoose>(
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
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 1) { x = x / 0; } return x; }",
        unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { mut int32 x = 0; if (x == 0) { x = int32.MaxValue + 1; } return x; }",
        unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int32 x = 0; return (x == 0 ? 1 / x : 0) + (int32.MaxValue + 1); }",
        unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { int32 x = 0; return (int32.MaxValue + 1) + (x == 0 ? 1 / x : 0); }",
        unchecked((int)0xE0540001))]
    public async Task IndexedCheckedFailureReturnsExactFailFastStatusAsync(string source, int status)
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-indexed-failure-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity", "indexed-failure",
                "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.True(compiled == 0, $"{compileOutput}{Environment.NewLine}{compileError}");
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == status, $"Expected {status:X8}, got {ran:X8}: {runOutput}{Environment.NewLine}{runError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.Equal(status, await ExecuteTrRmImageAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task IndependentIndexedConsumerAuditsBeforeReplacingOutputAsync(bool library, bool invalidEffect)
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-indexed-independent-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var output = prefix + (library ? ".dll" : ".exe");
        try
        {
            await File.WriteAllBytesAsync(artifact, TrRmExecutionAuditTests.Artifact(
                TrRmIndexedExecutionAuditTests.LoopBody(3), entry: !library,
                effect: invalidEffect ? 1u : null, exported: library));
            await File.WriteAllBytesAsync(output, [0xAA, 0x55]);
            var arguments = library
                ? new[] { "build-trrm-library", artifact, "--export", "Main", "-o", output, "--runtime", "fail-fast", "--json" }
                : ["build-trrm", artifact, "-o", output, "--runtime", "fail-fast", "--json"];
            var (exit, stdout, stderr) = await RunAsync([], arguments);
            Assert.True(exit == (invalidEffect ? 1 : 0),
                $"Exit {exit}: {stdout}{Environment.NewLine}{stderr}");
            if (invalidEffect)
            {
                Assert.Contains("TRM5001", stdout);
                Assert.Equal(new byte[] { 0xAA, 0x55 }, await File.ReadAllBytesAsync(output));
                return;
            }
            var loaded = TrRmExecutableCompiler.Load(await File.ReadAllBytesAsync(artifact));
            Assert.Equal(library
                ? NativeEmitter.EmitLibrary(loaded, ["Main"], NativeRuntimePolicy.FailFast)
                : NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast),
                await File.ReadAllBytesAsync(output));
            if (!library)
            {
                Assert.Equal(2, await ExecuteTrRmImageAsync(output));
                var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                    "--runtime", "fail-fast", "--json");
                Assert.True(ran == 2, $"Exit {ran}: {runOutput}{Environment.NewLine}{runError}");
            }
            else
            {
                var handle = NativeLibrary.Load(output);
                try
                {
                    var main = Marshal.GetDelegateForFunctionPointer<IndexedMain>(
                        NativeLibrary.GetExport(handle, "Main"));
                    Assert.Equal(2, main());
                }
                finally
                {
                    NativeLibrary.Free(handle);
                }
            }
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(output);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IndexedMain();
}
