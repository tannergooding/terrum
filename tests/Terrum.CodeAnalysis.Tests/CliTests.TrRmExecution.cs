// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int TrRmAdd(int left, int right);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate long TrRmIdentity(long value);

    [Fact]
    public async Task PersistedTrRmRunsAndBuildsWithoutProducerSourceAsync()
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-consumer-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { mut int32 x = 20; x = x + 22; return x; }"),
                "compile-trrm", "-", "--identity", "consumer", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(compiled == 0, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.Empty(compileError);
            Assert.True(File.Exists(artifact));

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == 42, $"{runOutput}{Environment.NewLine}{runError}");
            Assert.Empty(runOutput);
            Assert.Empty(runError);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.Empty(buildError);
            Assert.Equal(42, await ExecuteTrRmImageAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }

    [Fact]
    public async Task PersistedNestedAcyclicBreaksRunAndBuildWithoutProducerSourceAsync()
    {
        const string source = """
            int32 Main() {
                mut int32 result = 0;
                do {
                    do {
                        result = 40;
                        break;
                    } while (Fail());
                    result += 2;
                    break;
                } while (Fail());
                return result;
            }
            bool Fail() { require false; return false; }
            """;
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-nested-acyclic-breaks-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity", "nested-acyclic-breaks",
                "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.True(compiled == 0, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.Empty(compileError);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == 42, $"{runOutput}{Environment.NewLine}{runError}");
            Assert.Empty(runOutput);
            Assert.Empty(runError);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.Empty(buildError);
            Assert.Equal(42, await ExecuteTrRmImageAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }

    [Fact]
    public async Task PersistedDirectCallsAgreeAcrossJitAndExecutableAsync()
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-calls-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            const string source = """
                int32 Main() { return Recursive(5); }
                int32 Recursive(int32 n) {
                    if (n == 0) { return 1; }
                    else { return n * Recursive(n - 1); }
                }
                """;
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "recursive-calls", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(compiled == 0, $"{output}{Environment.NewLine}{error}");
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == 120, $"{runOutput}{Environment.NewLine}{runError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.Equal(120, await ExecuteTrRmImageAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }

    [Fact]
    public async Task PersistedNoThrowAndDoesNotReturnEffectsAreAuditedAsync()
    {
        const string source = """
            [NoThrow] int32 Identity(int32 value) { return value; }
            [NoThrow][DoesNotReturn] void Never() { while (true) {} }
            [NoThrow][DoesNotReturn] void Stop() { Never(); }
            int32 Main() { return Identity(42); }
            """;
        var artifact = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-effects-{Guid.NewGuid():N}.trrm");
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "effects", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(compiled == 0, $"{output}{Environment.NewLine}{error}");
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == 42, $"{runOutput}{Environment.NewLine}{runError}");
            Assert.Empty(runOutput);
            Assert.Empty(runError);
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Fact]
    public async Task PersistedNonReturningCallKeepsRequirementFailureStatusAsync()
    {
        const string source = "[DoesNotReturn] void Stop() { require false; } int32 Main() { Stop(); }";
        var artifact = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-no-return-{Guid.NewGuid():N}.trrm");
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "no-return", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(compiled == 0, $"{output}{Environment.NewLine}{error}");
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.Equal(unchecked((int)0xE0540003), ran);
            Assert.Empty(runOutput);
            Assert.Empty(runError);
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Fact]
    public async Task IndexedFailureReasonsMapToTheExistingNativeStatusesAsync()
    {
        var artifact = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-failure-reasons-{Guid.NewGuid():N}.trrm");
        try
        {
            var (compiled, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { require false; return 0; }"),
                "compile-trrm", "-", "--identity", "failure-reasons", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(compiled == 0, $"{output}{Environment.NewLine}{error}");
            var artifactBytes = await File.ReadAllBytesAsync(artifact);
            var reasonOffset = IndexedFailureReasonOffset(artifactBytes);
            foreach (var (reason, expected) in new[]
            {
                (1, unchecked((int)0xE0540003)),
                (2, unchecked((int)0xE0540001)),
                (3, unchecked((int)0xE0540002)),
                (4, unchecked((int)0xE0540001)),
            })
            {
                artifactBytes[reasonOffset] = (byte)reason;
                await File.WriteAllBytesAsync(artifact, artifactBytes);
                var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                    "--runtime", "fail-fast", "--json");
                Assert.Equal(expected, ran);
                Assert.Empty(runOutput);
                Assert.Empty(runError);
            }
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    private static int IndexedFailureReasonOffset(byte[] artifact)
    {
        var sectionCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(12));
        for (var section = 0; section < sectionCount; section++)
        {
            var entry = artifact.AsSpan(32 + section * 16);
            if (BinaryPrimitives.ReadUInt16LittleEndian(entry) != 0x20)
            {
                continue;
            }
            var rowCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var rowStart = offset + (rowCount + 1) * sizeof(uint);
            var rowLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(offset + rowCount * sizeof(uint)));
            var rowEnd = rowStart + rowLength;
            var marker = Array.LastIndexOf(artifact, (byte)0x87, rowEnd - 1, rowEnd - rowStart);
            if (marker < rowStart || artifact[marker + 1] != 1)
            {
                throw new InvalidOperationException("Indexed failure terminator was not found.");
            }
            return marker + 1;
        }
        throw new InvalidOperationException("TRRM Body section was not found.");
    }

    [Fact]
    public async Task PersistedLibraryExportCanCallPrivateSixArgumentHelperAsync()
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-calls-library-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var library = prefix + ".dll";
        try
        {
            const string source = """
                public int32 Add(int32 first, int32 second) {
                    return Helper(first, second, 3, 4, 5, 6);
                }
                int32 Helper(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) {
                    return a + b + c + d + e + f;
                }
                """;
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "export-calls", "-o", artifact,
                "--runtime", "fail-fast", "--library", "--json");
            Assert.True(compiled == 0, $"{output}{Environment.NewLine}{error}");
            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm-library", artifact, "--export", "Add", "-o", library,
                "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"{buildOutput}{Environment.NewLine}{buildError}");
            var handle = NativeLibrary.Load(library);
            try
            {
                var add = Marshal.GetDelegateForFunctionPointer<TrRmAdd>(
                    NativeLibrary.GetExport(handle, "Add"));
                Assert.Equal(41, add(10, 13));
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
    [Fact]
    public async Task PersistedTrRmLibraryPublishesTwoIndependentNativeExportsAsync()
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-library-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var library = prefix + ".dll";
        try
        {
            const string source = """
                public int32 Add(int32 left, int32 right) { return left + right; }
                public int64 Identity(int64 value) { return value; }
                """;
            var (compiled, stdout, stderr) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "two-exports", "-o", artifact,
                "--runtime", "fail-fast", "--library", "--json");
            Assert.True(compiled == 0, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.Empty(stderr);
            var (built, output, error) = await RunAsync([], "build-trrm-library", artifact,
                "--export", "Add", "--export", "Identity", "-o", library,
                "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"{output}{Environment.NewLine}{error}");
            Assert.Empty(error);
            var handle = NativeLibrary.Load(library);
            try
            {
                var add = Marshal.GetDelegateForFunctionPointer<TrRmAdd>(
                    NativeLibrary.GetExport(handle, "Add"));
                var identity = Marshal.GetDelegateForFunctionPointer<TrRmIdentity>(
                    NativeLibrary.GetExport(handle, "Identity"));
                Assert.Equal(42, add(20, 22));
                Assert.Equal(long.MinValue, identity(long.MinValue));
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
    [InlineData("int32 Main() { return 2147483647 + 1; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return 1 / 0; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { return int32(uint32.MaxValue); }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { _ = int32(1) / 0; return 42; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { int32 x = 1 / 0; return 42; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { mut int32 x = int32.MaxValue; do { x += 1; break; } while (false); return x; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { mut int32 value = 42; mut int32 divisor = 0; do { value /= divisor; break; } while (false); return value; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { do { int32 value = Fail(); break; } while (false); return 42; } int32 Fail() { require false; return 0; }", unchecked((int)0xE0540003))]
    [InlineData("int32 Main() { do { require false; return 42; } while (false); }", unchecked((int)0xE0540003))]
    [InlineData("int32 Main() { for (mut int32 i = 0; true; i = 1 / 0) { i = int32.MaxValue + 1; break; } return 42; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return (1 / 0) + (int32.MaxValue + 1); }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { return (int32.MaxValue + 1) + (1 / 0); }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { Fail(); return 42; } void Fail() { int32 x = 1 / 0; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { _ = Fail(); return 42; } int32 Fail() { return 1 / 0; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { return F(1 / 0, int32.MaxValue + 1); } int32 F(int32 a, int32 b) { return 42; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { return F(int32.MaxValue + 1, 1 / 0); } int32 F(int32 a, int32 b) { return 42; }", unchecked((int)0xE0540001))]
    public async Task TrRmCheckedFailureKeepsFailFastPolicyAfterPersistenceAsync(string source, int failure)
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-failure-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, stdout, stderr) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "failure", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.True(compiled == 0, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.Empty(stderr);
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == failure, $"Expected {failure:X8}, received {ran:X8}: {runOutput}{Environment.NewLine}{runError}");
            var (built, output, error) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"{output}{Environment.NewLine}{error}");
            Assert.Equal(failure, await ExecuteTrRmImageAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }

    private static async Task<int> ExecuteTrRmImageAsync(string executable)
    {
        using var process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Executable was not started.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(await stdout);
        Assert.Empty(await stderr);
        return process.ExitCode;
    }

    [Theory]
    [InlineData("compile-trrm")]
    [InlineData("run-trrm")]
    [InlineData("build-trrm")]
    [InlineData("build-trrm-library")]
    public async Task TrRmRejectsCheckedExceptionsAtEveryExecutionBoundaryAsync(string command)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-policy-{Guid.NewGuid():N}.trrm");
        var arguments = command switch
        {
            "compile-trrm" => new[] { command, "-", "--identity", "policy", "-o", path, "--runtime", "checked-exceptions", "--json" },
            "run-trrm" => [command, path, "--runtime", "checked-exceptions", "--json"],
            "build-trrm" => [command, path, "-o", Path.ChangeExtension(path, ".exe"), "--runtime", "checked-exceptions", "--json"],
            _ => [command, path, "--export", "Value", "-o", Path.ChangeExtension(path, ".dll"), "--runtime", "checked-exceptions", "--json"],
        };
        var (exit, stdout, stderr) = await RunAsync(
            Encoding.UTF8.GetBytes("int32 Main() { return 42; }"), arguments);
        Assert.True(exit == 1, $"{stdout}{Environment.NewLine}{stderr}");
        Assert.Empty(stderr);
        using var result = JsonDocument.Parse(stdout);
        Assert.Equal("TRM5001", result.RootElement.GetProperty("diagnostics")[0]
            .GetProperty("code").GetString());
        Assert.Contains("fail-fast", result.RootElement.GetProperty("diagnostics")[0]
            .GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 2) { int32 inner = x; init int32 vacant; x = inner + 1; } return x; }")]
    [InlineData("int32 Main() { mut int32 x = 0; if (x == 0) { int32 inner = x; init int32 vacant; x = inner + 1; } return x; }")]
    public async Task TrRmSourceRejectionDoesNotOverwritePreviousArtifactAsync(string source)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-reject-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(path, [0xAA, 0x55]);
            var (exit, stdout, stderr) = await RunAsync(
                Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "reject", "-o", path,
                "--runtime", "fail-fast", "--json");
            Assert.True(exit == 1, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.Empty(stderr);
            Assert.Contains("TRM5001", stdout);
            Assert.Equal(new byte[] { 0xAA, 0x55 }, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TrRmIndependentBytesExecuteInFreshProcessAsync()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-independent-{Guid.NewGuid():N}.trrm");
        try
        {
            byte[] body = [4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1];
            await File.WriteAllBytesAsync(path, TrRmExecutionAuditTests.Artifact(body));
            var (exit, stdout, stderr) = await RunAsync([], "run-trrm", path, "--runtime", "fail-fast", "--json");
            Assert.True(exit == 42, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.Empty(stdout);
            Assert.Empty(stderr);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrRmInvalidEffectsPreserveNativeOutputAndRejectExecutionAsync(bool library)
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-invalid-effects-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var output = prefix + (library ? ".dll" : ".exe");
        try
        {
            byte[] body = [4, 1, 1, 0, 0, 0, 3,
                1, 1, 1, 20, 1, 2, 1, 22, 0x15, 3, 1, 1, 2, 0x83, 3];
            await File.WriteAllBytesAsync(artifact,
                TrRmExecutionAuditTests.Artifact(body, entry: !library, effect: 1, exported: library));
            await File.WriteAllBytesAsync(output, [0xAA, 0x55]);
            var arguments = library
                ? new[] { "build-trrm-library", artifact, "--export", "Main", "-o", output, "--runtime", "fail-fast", "--json" }
                : ["build-trrm", artifact, "-o", output, "--runtime", "fail-fast", "--json"];
            var (exit, stdout, stderr) = await RunAsync([], arguments);
            Assert.True(exit == 1, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.Empty(stderr);
            Assert.Contains("TRM5001", stdout);
            Assert.Equal(new byte[] { 0xAA, 0x55 }, await File.ReadAllBytesAsync(output));
            var (run, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(run == 1, $"{runOutput}{Environment.NewLine}{runError}");
            Assert.Contains("TRM5001", runOutput);
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(output);
        }
    }

    [Fact]
    public async Task TrRmMissingInputHasInputErrorStatusAsync()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-absent-{Guid.NewGuid():N}.trrm");
        var (exit, stdout, stderr) = await RunAsync([], "run-trrm", path, "--runtime", "fail-fast", "--json");
        Assert.True(exit == 2, $"{stdout}{Environment.NewLine}{stderr}");
        Assert.Empty(stderr);
        Assert.Contains("TRM2001", stdout);
    }

    [Fact]
    public async Task TrRmInvalidUtf8PreservesOutputAndReportsInputFailureAsync()
    {
        var output = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-invalid-utf8-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(output, [0xAA, 0x55]);
            var (exit, stdout, stderr) = await RunAsync([0xC3, 0x28],
                "compile-trrm", "-", "--identity", "invalid-utf8", "-o", output,
                "--runtime", "fail-fast", "--json");
            Assert.True(exit == 2, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.Empty(stderr);
            Assert.Contains("TRM2001", stdout);
            Assert.Equal(new byte[] { 0xAA, 0x55 }, await File.ReadAllBytesAsync(output));
        }
        finally
        {
            File.Delete(output);
        }
    }
}
