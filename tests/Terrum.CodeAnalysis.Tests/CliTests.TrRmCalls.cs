// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task IndependentCallBytesRunAndBuildWithoutSourceAsync(bool indexed)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-raw-call-{Guid.NewGuid():N}");
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
            Assert.That(ran == 41, Is.True, $"Exit {ran}: {output}{Environment.NewLine}{error}");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"Exit {built}: {buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(executable), Is.EqualTo(NativeEmitter.Emit(TrRmExecutableCompiler.Load(bytes), NativeRuntimePolicy.FailFast)));
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(41));
            Assert.That(await File.ReadAllBytesAsync(artifact), Is.EqualTo(bytes));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task InvalidPersistedCallCannotReplaceAnExistingNativeImageAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-invalid-call-{Guid.NewGuid():N}");
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
            Assert.That(built == 1, Is.True, $"Exit {built}: {output}{Environment.NewLine}{error}");
            Assert.That(output.Length != 0 || error.Length != 0, Is.True);
            Assert.That(await File.ReadAllBytesAsync(executable), Is.EqualTo(retained));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }
}
