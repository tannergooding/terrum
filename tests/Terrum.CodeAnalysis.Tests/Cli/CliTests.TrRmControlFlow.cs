// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Modules.TrRm;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IndexedChoose(int input);

    [Test]
    public async Task PersistedIndexedControlFlowRunsAndBuildsWithoutProducerSourceAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-indexed-cli-{Guid.NewGuid():N}");
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
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(File.Exists(artifact), Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 42, Is.True, $"{runOutput}{Environment.NewLine}{runError}");

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(await File.ReadAllBytesAsync(executable), Is.EqualTo(NativeEmitter.Emit(TrRmExecutableCompiler.Load(await File.ReadAllBytesAsync(artifact)),
                NativeRuntimePolicy.FailFast)));
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(42));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedIndexedLibraryPreservesBranchExportAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-indexed-export-{Guid.NewGuid():N}");
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
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm-library", artifact, "--export", "Choose", "-o", library,
                "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(await File.ReadAllBytesAsync(library), Is.EqualTo(NativeEmitter.EmitLibrary(
                TrRmExecutableCompiler.Load(await File.ReadAllBytesAsync(artifact)),
                ["Choose"], NativeRuntimePolicy.FailFast)));
            var handle = NativeLibrary.Load(library);
            try
            {
                var choose = Marshal.GetDelegateForFunctionPointer<IndexedChoose>(
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
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 1) { x = x / 0; } return x; }",        unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { mut int32 x = 0; if (x == 0) { x = int32.MaxValue + 1; } return x; }",        unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { int32 x = 0; return (x == 0 ? 1 / x : 0) + (int32.MaxValue + 1); }",        unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { int32 x = 0; return (int32.MaxValue + 1) + (x == 0 ? 1 / x : 0); }",        unchecked((int)0xE0540001))]
    public async Task IndexedCheckedFailureReturnsExactFailFastStatusAsync(string source, int status)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-indexed-failure-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity", "indexed-failure",
                "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == status, Is.True, $"Expected {status:X8}, got {ran:X8}: {runOutput}{Environment.NewLine}{runError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(status));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }
    [TestCase(false,false)]
    [TestCase(true,false)]
    [TestCase(false,true)]
    [TestCase(true,true)]
    public async Task IndependentIndexedConsumerAuditsBeforeReplacingOutputAsync(bool library, bool invalidEffect)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-indexed-independent-{Guid.NewGuid():N}");
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
            Assert.That(exit == (invalidEffect ? 1 : 0), Is.True, $"Exit {exit}: {stdout}{Environment.NewLine}{stderr}");
            if (invalidEffect)
            {
                Assert.That(stdout, Does.Contain("TRM5001"));
                Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(new byte[] { 0xAA, 0x55 }));
                return;
            }
            var loaded = TrRmExecutableCompiler.Load(await File.ReadAllBytesAsync(artifact));
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(library
                ? NativeEmitter.EmitLibrary(loaded, ["Main"], NativeRuntimePolicy.FailFast)
                : NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)));
            if (!library)
            {
                Assert.That(await ExecuteTrRmImageAsync(output), Is.EqualTo(2));
                var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                    "--runtime", "fail-fast", "--json");
                Assert.That(ran == 2, Is.True, $"Exit {ran}: {runOutput}{Environment.NewLine}{runError}");
            }
            else
            {
                var handle = NativeLibrary.Load(output);
                try
                {
                    var main = Marshal.GetDelegateForFunctionPointer<IndexedMain>(
                        NativeLibrary.GetExport(handle, "Main"));
                    Assert.That(main(), Is.EqualTo(2));
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
            await TestFileCleanup.DeleteWithRetryAsync(output).ConfigureAwait(true);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IndexedMain();
}
