// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentCallBytesRunAndBuildWithoutSourceAsync(bool indexed)
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-raw-call-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            byte[] body = indexed ? TrRmCallAuditTests.IndexedCallBody()
                : [4, 1, 1, 0, 0, 0, 2, 1, 1, 1, 41, 0x12, 2, 2, 1, 1, 0x83, 2];
            var bytes = TrRmExecutionAuditTests.LocalCallArtifact(body, TrRmCallAuditTests.IdentityBody());
            await File.WriteAllBytesAsync(artifact, bytes);
            var (ran, output, error) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(ran == 41, $"Exit {ran}: {output}{Environment.NewLine}{error}");
            Assert.Empty(output);
            Assert.Empty(error);
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"Exit {built}: {buildOutput}{Environment.NewLine}{buildError}");
            Assert.Empty(buildError);
            Assert.Equal(NativeEmitter.Emit(TrRmExecutableCompiler.Load(bytes), NativeRuntimePolicy.FailFast),
                await File.ReadAllBytesAsync(executable));
            Assert.Equal(41, await ExecuteTrRmImageAsync(executable));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(artifact));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }

    [Fact]
    public async Task InvalidPersistedCallCannotReplaceAnExistingNativeImageAsync()
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-invalid-call-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var body = TrRmCallAuditTests.IndexedCallBody();
            body[33] = 3;
            await File.WriteAllBytesAsync(artifact, TrRmExecutionAuditTests.LocalCallArtifact(
                body, TrRmCallAuditTests.IdentityBody()));
            byte[] retained = [0xDE, 0xAD, 0xBE, 0xEF];
            await File.WriteAllBytesAsync(executable, retained);
            var (built, output, error) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 1, $"Exit {built}: {output}{Environment.NewLine}{error}");
            Assert.True(output.Length != 0 || error.Length != 0);
            Assert.Equal(retained, await File.ReadAllBytesAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }
}
