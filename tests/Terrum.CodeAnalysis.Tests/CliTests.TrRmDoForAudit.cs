// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task HandwrittenDoForExecutesInFreshConsumersAndRejectsTamperingAsync(bool iterator)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-do-for-raw-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var body = iterator
                ? TrRmDoForAuditTests.IteratorBody(42)
                : TrRmDoForAuditTests.PostTestBody(42);
            await File.WriteAllBytesAsync(artifact, TrRmExecutionAuditTests.Artifact(body));
            var (ran, output, error) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 42, Is.True, $"Exit {ran}: {output}{Environment.NewLine}{error}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(42));
            var retained = await File.ReadAllBytesAsync(executable);

            body[15] = iterator ? (byte)4 : (byte)3;
            await File.WriteAllBytesAsync(artifact, TrRmExecutionAuditTests.Artifact(body));
            var (rejected, rejectOutput, rejectError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(rejected == 1, Is.True, $"{rejectOutput}{Environment.NewLine}{rejectError}");
            Assert.That(rejectOutput, Does.Contain("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(executable), Is.EqualTo(retained));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }
}
