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
    private delegate int DoForExport(int input);

    [Test]
    public async Task PersistedDoAndForExecuteWithoutSourceAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-do-for-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        const string source = """
            int32 Main() {
                mut int32 total = 0;
                for (mut int32 i = Start(); i < 3; i = Next(i)) {
                    do {
                        total += i;
                        if (i == 1) { continue; }
                    } while (false);
                    if (i == 2) { break; }
                }
                return total == 3 ? 42 : 0;
            }
            int32 Start() { return 0; }
            int32 Next(int32 value) { return value + 1; }
            """;
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "do-for-cli", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            var bytes = await File.ReadAllBytesAsync(artifact);
            TrRmStructuralMetadataValidator.ValidateCompact(bytes);
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 42, Is.True, $"Exit {ran}: {runOutput}{Environment.NewLine}{runError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(await File.ReadAllBytesAsync(executable), Is.EqualTo(NativeEmitter.Emit(TrRmExecutableCompiler.Load(bytes), NativeRuntimePolicy.FailFast)));
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(42));
            Assert.That(await File.ReadAllBytesAsync(artifact), Is.EqualTo(bytes));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedDoForLibraryExecutesIndependentExportAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-do-for-export-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var library = prefix + ".dll";
        const string source = """
            public int32 Count(int32 limit) {
                mut int32 total = 0;
                for (mut int32 i = 0; i < limit; i++) {
                    do { total += i; } while (false);
                }
                return total;
            }
            """;
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "do-for-export", "-o", artifact,
                "--runtime", "fail-fast", "--library", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm-library", artifact, "--export", "Count", "-o", library,
                "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            var loaded = TrRmExecutableCompiler.Load(await File.ReadAllBytesAsync(artifact));
            Assert.That(await File.ReadAllBytesAsync(library), Is.EqualTo(NativeEmitter.EmitLibrary(loaded, ["Count"], NativeRuntimePolicy.FailFast)));
            var handle = NativeLibrary.Load(library);
            try
            {
                var count = Marshal.GetDelegateForFunctionPointer<DoForExport>(
                    NativeLibrary.GetExport(handle, "Count"));
                Assert.That(count(0), Is.EqualTo(0));
                Assert.That(count(4), Is.EqualTo(6));
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
    [TestCase("int32 Main() { mut int32 x = 0; do { x = 1 / x; } while (false); return x; }",        unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { mut int32 x = 0; for (x = 0; x < 1; x = x + 1) { x = int32.MaxValue + 1; } return x; }",        unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { mut int32 x = 0; for (x = 0; x < 2; x = x / 0) { continue; } return x; }",        unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { do { Touch(0); } while (Overflow() == 0); return 42; } void Touch(int32 x) { _ = 1 / x; } int32 Overflow() { return int32.MaxValue + 1; }",        unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { for (int32 x = Divide(0); Overflow() == 0;) { } return 42; } int32 Divide(int32 x) { return 1 / x; } int32 Overflow() { return int32.MaxValue + 1; }",        unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { mut int32 x = 0; for (; Divide(x) == 0; x = int32.MaxValue + 1) { continue; } return 42; } int32 Divide(int32 x) { return 1 / x; }",        unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { mut int32 x = 0; for (; x++ < 2; Touch(0)) { continue; } return 42; } void Touch(int32 x) { _ = 1 / x; }",        unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { mut int32 x = 0; do { x++; continue; } while (Divide(0) == 0); return x; } int32 Divide(int32 x) { return 1 / x; }",        unchecked((int)0xE0540002))]
    public async Task PersistedLoopFailureHasExactStatusAsync(string source, int expected)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-do-for-failure-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "do-for-failure", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            var bytes = await File.ReadAllBytesAsync(artifact);
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == expected, Is.True, $"Expected {expected:X8}, got {ran:X8}: {runOutput}{Environment.NewLine}{runError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(expected));
            Assert.That(await File.ReadAllBytesAsync(artifact), Is.EqualTo(bytes));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task UnsupportedTransferDoesNotReplaceCompiledArtifactAsync()
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-do-for-retained-{Guid.NewGuid():N}.trrm");
        byte[] retained = [0xAB, 0xCD, 0xEF];
        const string source = """
            int32 Main() {
                mut int32 x = 0;
                outer: for (x = 0; x < 2; x++) {
                    do { break outer; } while (false);
                }
                return x;
            }
            """;
        try
        {
            await File.WriteAllBytesAsync(path, retained);
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "do-for-retained", "-o", path,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 1, Is.True, $"Exit {compiled}: {output}{Environment.NewLine}{error}");
            Assert.That(output, Does.Contain("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(retained));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
