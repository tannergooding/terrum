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
    [TestCase("mut int32 x = 0; outer: while (x < 3) { mut int32 y = 0; while (y++ < 2) { x++; if (x == 2) { break outer; } } } return x == 2 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; outer: for (; x < 3; x++) { mut int32 y = 0; while (y++ < 2) { if (x == 1) { continue outer; } } } return x == 3 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; mut int32 effects = 0; outer: do { mut int32 y = 0; while (y++ < 2) { if (x++ == 0) { continue outer; } } } while (++effects < 2); return effects == 2 && x == 3 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; outer: while (x < 2) { mut int32 y = 0; while (y++ < 2) { mut int32 z = 0; do { x++; if (x == 2) { break outer; } } while (z++ < 1); } } return x == 2 ? 42 : 0;")]
    public async Task PersistedNamedTransfersRunAndBuildWithoutSourceAsync(string body)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-named-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { " + body + " }"),
                "compile-trrm", "-", "--identity", "named-cli", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{error}");
            var bytes = await File.ReadAllBytesAsync(artifact);
            TrRmStructuralMetadataValidator.ValidateCompact(bytes);
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 42, Is.True, $"{runOutput}{runError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{buildError}");
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
    public async Task PersistedNamedTransfersRetainNormalizedLibraryExportsAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-named-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var library = prefix + ".dll";
        const string source = "public int32 @await(int32 limit) { mut int32 x = 0; @const: for (; x < limit; x++) { mut int32 y = 0; while (y++ < 2) { if (x == 1) { continue const; } if (x == 2) { break const; } } } return x; }";
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "named-export", "-o", artifact,
                "--runtime", "fail-fast", "--library", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{error}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm-library", artifact,
                "--export", "await", "-o", library, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{buildError}");
            var loaded = TrRmExecutableCompiler.Load(await File.ReadAllBytesAsync(artifact));
            Assert.That(await File.ReadAllBytesAsync(library), Is.EqualTo(NativeEmitter.EmitLibrary(loaded, ["await"], NativeRuntimePolicy.FailFast)));
            var handle = NativeLibrary.Load(library);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<DoForExport>(NativeLibrary.GetExport(handle, "await"));
                Assert.That(invoke(0), Is.EqualTo(0));
                Assert.That(invoke(1), Is.EqualTo(1));
                Assert.That(invoke(4), Is.EqualTo(2));
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
}
