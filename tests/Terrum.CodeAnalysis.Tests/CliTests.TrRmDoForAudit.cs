// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandwrittenDoForExecutesInFreshConsumersAndRejectsTamperingAsync(bool iterator)
    {
        var prefix = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-do-for-raw-{Guid.NewGuid():N}");
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
            Assert.True(ran == 42, $"Exit {ran}: {output}{Environment.NewLine}{error}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(built == 0, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.Equal(42, await ExecuteTrRmImageAsync(executable));
            var retained = await File.ReadAllBytesAsync(executable);

            body[15] = iterator ? (byte)4 : (byte)3;
            await File.WriteAllBytesAsync(artifact, TrRmExecutionAuditTests.Artifact(body));
            var (rejected, rejectOutput, rejectError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.True(rejected == 1, $"{rejectOutput}{Environment.NewLine}{rejectError}");
            Assert.Contains("TRM5001", rejectOutput);
            Assert.Equal(retained, await File.ReadAllBytesAsync(executable));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(executable);
        }
    }
}
