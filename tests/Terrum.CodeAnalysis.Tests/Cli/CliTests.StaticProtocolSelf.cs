// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

using Terrum.CodeAnalysis.Tests.Semantics.Generics;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [Test]
    public async Task BodylessStaticRequirementAcceptsTheFreshCliReproductionAsync()
    {
        const string Source = "interface Addition<TLeft, TRight, TResult> { static TResult operator +(TLeft left, TRight right); } int32 Main(){return 0;}";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "analyze", "-", "--json").ConfigureAwait(true);
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using var result = JsonDocument.Parse(output);
        Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.True);
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task FreshSelfBoundSourceExecutesThroughCliBuildAndJitAsync(string policy)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-self-{Guid.NewGuid():N}.exe");
        try
        {
            var input = Encoding.UTF8.GetBytes(StaticProtocolSelfTests.Source);
            var (build, output, error) = await RunAsync(input,
                "build", "-", "-o", path, "--runtime", policy, "--json").ConfigureAwait(true);
            Assert.That(build, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            using var result = JsonDocument.Parse(output);
            Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(await ExecuteNativeImageAsync(path).ConfigureAwait(true), Is.EqualTo(42));

            var (jit, jitOutput, jitError) = await RunAsync(input,
                "jit", "-", "--runtime", policy, "--json").ConfigureAwait(true);
            Assert.That(jit, Is.EqualTo(42));
            Assert.That(jitOutput, Is.Empty);
            Assert.That(jitError, Is.Empty);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
