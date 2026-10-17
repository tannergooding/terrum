// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int MutationExport(int input);
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public async Task IndexedMutationArtifactRunsAndBuildsWithoutSourceAsync(string type)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-mutation-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        var source = $$"""
            int32 Main() {
                mut {{type}} x = {{type}}(20);
                x += x++;
                if (x == {{type}}(40)) { x >>>= 1; }
                return int32(x) + 22;
            }
            """;
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "mutation", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"Exit {compiled}: {output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 42, Is.True, $"Exit {ran}: {runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"Exit {built}: {buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(executable), Is.EqualTo(NativeEmitter.Emit(TrRmExecutableCompiler.Load(
                await File.ReadAllBytesAsync(artifact)), NativeRuntimePolicy.FailFast)));
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(42));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public async Task IndexedMutationLibraryExportExecutesFromSourceFreeDllAsync(string type)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-mutation-lib-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var library = prefix + ".dll";
        try
        {
            var source = $$"""
                public int32 Mutate(int32 input) {
                    mut {{type}} x = {{type}}(input);
                    x += x++;
                    x >>>= 0;
                    return int32(++x);
                }
                """;
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "mutation-lib", "-o", artifact,
                "--runtime", "fail-fast", "--library", "--json");
            Assert.That(compiled == 0, Is.True, $"Exit {compiled}: {output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm-library",
                artifact, "--export", "Mutate", "-o", library, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"Exit {built}: {buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            var loaded = TrRmExecutableCompiler.Load(await File.ReadAllBytesAsync(artifact));
            Assert.That(await File.ReadAllBytesAsync(library), Is.EqualTo(NativeEmitter.EmitLibrary(loaded, ["Mutate"], NativeRuntimePolicy.FailFast)));
            var handle = NativeLibrary.Load(library);
            try
            {
                var mutate = Marshal.GetDelegateForFunctionPointer<MutationExport>(
                    NativeLibrary.GetExport(handle, "Mutate"));
                Assert.That(mutate(20), Is.EqualTo(41));
                Assert.That(mutate(0), Is.EqualTo(1));
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
    [TestCase("int32","int32.MaxValue","+=","1",unchecked((int)0xE0540001))]
    [TestCase("int32","1","<<=","-1",unchecked((int)0xE0540001))]
    [TestCase("uint32","uint32(1)",">>=","32",unchecked((int)0xE0540001))]
    [TestCase("int64","int64(1)",">>>=","64",unchecked((int)0xE0540001))]
    [TestCase("uint64","uint64(1)","/=","uint64(0)",unchecked((int)0xE0540002))]
    [TestCase("int32","20","+=","x++ / 0",unchecked((int)0xE0540002))]
    [TestCase("int32","int32.MaxValue","+=","x++ / 0",unchecked((int)0xE0540001))]
    [TestCase("uint32","uint32.MaxValue","+=","++x",unchecked((int)0xE0540001))]
    [TestCase("int64","int64.MaxValue","+=","x++",unchecked((int)0xE0540001))]
    [TestCase("uint64","uint64.MaxValue","+=","++x",unchecked((int)0xE0540001))]
    [TestCase("int32","int32.MinValue","-=","--x",unchecked((int)0xE0540001))]
    [TestCase("uint32","uint32(0)","-=","x--",unchecked((int)0xE0540001))]
    [TestCase("int64","int64.MinValue","/=","int64(-1)",unchecked((int)0xE0540001))]
    [TestCase("uint64","uint64(0)","-=","--x",unchecked((int)0xE0540001))]
    public async Task FailedMutationHasOrderedFailFastStatusInJitAndExeAsync(string type,
        string initial, string op, string right, int failure)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-mutation-fail-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        var source = $"int32 Main() {{ mut {type} x = {initial}; x {op} {right}; return 42; }}";
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "mutation-fail", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"Exit {compiled}: {output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == failure, Is.True, $"Expected {failure:X8}, got {ran:X8}: {runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"Exit {built}: {buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(failure));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }
    [TestCase(32u)]
    [TestCase(0x8000_0000u)]
    [TestCase(uint.MaxValue)]
    public async Task IndependentRawUInt32CountFailsInSourceFreeJitAndExeAsync(uint count)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-raw-shift-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        const int failure = unchecked((int)0xE0540001);
        try
        {
            await File.WriteAllBytesAsync(artifact, TrRmExecutionAuditTests.Artifact(
                TrRmIndexedMutationTests.RawCountShiftBody(count)));
            var (ran, output, error) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == failure, Is.True, $"Count {count}: {ran:X8}: {output}{Environment.NewLine}{error}");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"Exit {built}: {buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(failure));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task HandwrittenIndexedShiftRunsInIndependentJitAndExeAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-indexed-shift-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            await File.WriteAllBytesAsync(artifact,
                TrRmExecutionAuditTests.Artifact(TrRmMutationAuditTests.ShiftBody()));
            var (ran, output, error) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == int.MaxValue, Is.True, $"Exit {ran}: {output}{Environment.NewLine}{error}");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"Exit {built}: {buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(int.MaxValue));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }
}
