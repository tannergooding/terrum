// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Fact]
    public async Task BodylessStaticRequirementAcceptsTheFreshCliReproductionAsync()
    {
        const string Source = "interface Addition<TLeft, TRight, TResult> { static TResult operator +(TLeft left, TRight right); } int32 Main(){return 0;}";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "analyze", "-", "--json").ConfigureAwait(true);
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var result = JsonDocument.Parse(output);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task FreshSelfBoundSourceExecutesThroughCliBuildAndJitAsync(string policy)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"terrum-self-{Guid.NewGuid():N}.exe");
        try
        {
            var input = Encoding.UTF8.GetBytes(StaticProtocolSelfTests.Source);
            var (build, output, error) = await RunAsync(input,
                "build", "-", "-o", path, "--runtime", policy, "--json").ConfigureAwait(true);
            Assert.Equal(0, build);
            Assert.Empty(error);
            using var result = JsonDocument.Parse(output);
            Assert.True(result.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(42, await ExecuteNativeImageAsync(path).ConfigureAwait(true));

            var (jit, jitOutput, jitError) = await RunAsync(input,
                "jit", "-", "--runtime", policy, "--json").ConfigureAwait(true);
            Assert.Equal(42, jit);
            Assert.Empty(jitOutput);
            Assert.Empty(jitError);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
