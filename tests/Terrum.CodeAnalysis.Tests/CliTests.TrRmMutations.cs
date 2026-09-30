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
    private delegate int MutationExport(int input);

    [Theory]
    [InlineData("int32")]
    [InlineData("uint32")]
    [InlineData("int64")]
    [InlineData("uint64")]
    public async Task IndexedMutationArtifactRunsAndBuildsWithoutSourceAsync(string type)
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-mutation-{Guid.NewGuid():N}");
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
            Assert.True(compiled == 0, $"Exit {compiled}: {output}{Environment.NewLine}{error}");
            Assert.Empty(error);
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == 42, $"Exit {ran}: {runOutput}{Environment.NewLine}{runError}");
            Assert.Empty(runOutput);
            Assert.Empty(runError);
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"Exit {built}: {buildOutput}{Environment.NewLine}{buildError}");
            Assert.Empty(buildError);
            Assert.Equal(NativeEmitter.Emit(TrRmExecutableCompiler.Load(
                await File.ReadAllBytesAsync(artifact)), NativeRuntimePolicy.FailFast),
                await File.ReadAllBytesAsync(executable));
            Assert.Equal(42, await ExecuteTrRmImageAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }

    [Theory]
    [InlineData("int32")]
    [InlineData("uint32")]
    [InlineData("int64")]
    [InlineData("uint64")]
    public async Task IndexedMutationLibraryExportExecutesFromSourceFreeDllAsync(string type)
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-mutation-lib-{Guid.NewGuid():N}");
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
            Assert.True(compiled == 0, $"Exit {compiled}: {output}{Environment.NewLine}{error}");
            Assert.Empty(error);
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm-library",
                artifact, "--export", "Mutate", "-o", library, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"Exit {built}: {buildOutput}{Environment.NewLine}{buildError}");
            Assert.Empty(buildError);
            var loaded = TrRmExecutableCompiler.Load(await File.ReadAllBytesAsync(artifact));
            Assert.Equal(NativeEmitter.EmitLibrary(loaded, ["Mutate"], NativeRuntimePolicy.FailFast),
                await File.ReadAllBytesAsync(library));
            var handle = NativeLibrary.Load(library);
            try
            {
                var mutate = Marshal.GetDelegateForFunctionPointer<MutationExport>(
                    NativeLibrary.GetExport(handle, "Mutate"));
                Assert.Equal(41, mutate(20));
                Assert.Equal(1, mutate(0));
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
    [InlineData("int32", "int32.MaxValue", "+=", "1", unchecked((int)0xE0540001))]
    [InlineData("int32", "1", "<<=", "-1", unchecked((int)0xE0540001))]
    [InlineData("uint32", "uint32(1)", ">>=", "32", unchecked((int)0xE0540001))]
    [InlineData("int64", "int64(1)", ">>>=", "64", unchecked((int)0xE0540001))]
    [InlineData("uint64", "uint64(1)", "/=", "uint64(0)", unchecked((int)0xE0540002))]
    [InlineData("int32", "20", "+=", "x++ / 0", unchecked((int)0xE0540002))]
    [InlineData("int32", "int32.MaxValue", "+=", "x++ / 0", unchecked((int)0xE0540001))]
    [InlineData("uint32", "uint32.MaxValue", "+=", "++x", unchecked((int)0xE0540001))]
    [InlineData("int64", "int64.MaxValue", "+=", "x++", unchecked((int)0xE0540001))]
    [InlineData("uint64", "uint64.MaxValue", "+=", "++x", unchecked((int)0xE0540001))]
    [InlineData("int32", "int32.MinValue", "-=", "--x", unchecked((int)0xE0540001))]
    [InlineData("uint32", "uint32(0)", "-=", "x--", unchecked((int)0xE0540001))]
    [InlineData("int64", "int64.MinValue", "/=", "int64(-1)", unchecked((int)0xE0540001))]
    [InlineData("uint64", "uint64(0)", "-=", "--x", unchecked((int)0xE0540001))]
    public async Task FailedMutationHasOrderedFailFastStatusInJitAndExeAsync(string type,
        string initial, string op, string right, int failure)
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-mutation-fail-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        var source = $"int32 Main() {{ mut {type} x = {initial}; x {op} {right}; return 42; }}";
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "mutation-fail", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(compiled == 0, $"Exit {compiled}: {output}{Environment.NewLine}{error}");
            Assert.Empty(error);
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == failure, $"Expected {failure:X8}, got {ran:X8}: {runOutput}{Environment.NewLine}{runError}");
            Assert.Empty(runOutput);
            Assert.Empty(runError);
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"Exit {built}: {buildOutput}{Environment.NewLine}{buildError}");
            Assert.Empty(buildError);
            Assert.Equal(failure, await ExecuteTrRmImageAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }

    [Theory]
    [InlineData(32u)]
    [InlineData(0x8000_0000u)]
    [InlineData(uint.MaxValue)]
    public async Task IndependentRawUInt32CountFailsInSourceFreeJitAndExeAsync(uint count)
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-raw-shift-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        const int failure = unchecked((int)0xE0540001);
        try
        {
            await File.WriteAllBytesAsync(artifact, TrRmExecutionAuditTests.Artifact(
                TrRmIndexedMutationTests.RawCountShiftBody(count)));
            var (ran, output, error) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == failure, $"Count {count}: {ran:X8}: {output}{Environment.NewLine}{error}");
            Assert.Empty(output);
            Assert.Empty(error);
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"Exit {built}: {buildOutput}{Environment.NewLine}{buildError}");
            Assert.Empty(buildError);
            Assert.Equal(failure, await ExecuteTrRmImageAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }

    [Fact]
    public async Task HandwrittenIndexedShiftRunsInIndependentJitAndExeAsync()
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-indexed-shift-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            await File.WriteAllBytesAsync(artifact,
                TrRmExecutionAuditTests.Artifact(TrRmMutationAuditTests.ShiftBody()));
            var (ran, output, error) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == int.MaxValue, $"Exit {ran}: {output}{Environment.NewLine}{error}");
            Assert.Empty(output);
            Assert.Empty(error);
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"Exit {built}: {buildOutput}{Environment.NewLine}{buildError}");
            Assert.Empty(buildError);
            Assert.Equal(int.MaxValue, await ExecuteTrRmImageAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }
}
