// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task CompileModuleTransfersInitializationReferenceAsync(
        string policy)
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-init-reference-module-");
        var providerSource = Path.Combine(directory, "provider.terrum");
        var consumerSource = Path.Combine(directory, "consumer.terrum");
        var providerArtifact = Path.Combine(directory, "provider.trm");
        var consumerArtifact = Path.Combine(directory, "consumer.trm");
        try
        {
            await File.WriteAllTextAsync(providerSource, """
                public int32 Initialize(ref<init int32> result) {
                    *result = 42;
                    return 0;
                }
                """).ConfigureAwait(false);
            await File.WriteAllTextAsync(consumerSource, """
                public int32 Main() {
                    init int32 result;
                    _ = Initialize(ref init result);
                    return result;
                }
                """).ConfigureAwait(false);

            var (providerExit, providerOutput, providerError)
                = await RunAsync([], [
                    "compile-module", "producer", providerSource,
                    "Initialize", "-o", providerArtifact, "--runtime", policy,
                    "--json",
                ]).ConfigureAwait(false);
            Assert.That(providerExit, Is.EqualTo(0),
                providerOutput + providerError);
            Assert.That(providerError, Is.Empty);

            var (consumerExit, consumerOutput, consumerError)
                = await RunAsync([], [
                    "compile-module", "consumer", consumerSource,
                    providerArtifact, "-o", consumerArtifact, "--runtime",
                    policy, "--json",
                ]).ConfigureAwait(false);
            Assert.That(consumerExit, Is.EqualTo(0),
                consumerOutput + consumerError);
            Assert.That(consumerError, Is.Empty);

            var (runExit, _, runError) = await RunAsync([], [
                "run-module", consumerArtifact, providerArtifact,
                "--runtime", policy,
            ]).ConfigureAwait(false);
            Assert.That(runExit, Is.EqualTo(42), runError);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(false);
        }
    }
}
