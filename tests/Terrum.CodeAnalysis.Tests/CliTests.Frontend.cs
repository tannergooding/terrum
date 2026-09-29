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
    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task FrontendComponentPublishesTypedBranchesAndRejectsInvalidShapesAsync(string policy)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        const string Conditional =
            "int32 Value(int32 x, int32 y) { if (x < y) { return x + y; } else { return x - y; } }";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Conditional),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using (var document = JsonDocument.Parse(output))
        {
            var result = document.RootElement;
            Assert.Equal(1, result.GetProperty("schema").GetInt32());
            Assert.Equal("frontend-component", result.GetProperty("command").GetString());
            Assert.True(result.GetProperty("success").GetBoolean());
            Assert.Equal(2, result.GetProperty("result").GetProperty("parameterCount").GetInt32());
            Assert.Equal(67, result.GetProperty("result").GetProperty("condition")
                .GetProperty("operation").GetInt32());
            Assert.Equal(57, result.GetProperty("result").GetProperty("whenTrue")
                .GetProperty("operation").GetInt32());
            Assert.Equal(58, result.GetProperty("result").GetProperty("whenFalse")
                .GetProperty("operation").GetInt32());
            Assert.Empty(result.GetProperty("diagnostics").EnumerateArray());
        }

        var (invalidExit, invalidOutput, invalidError) = await RunAsync(
            Encoding.UTF8.GetBytes("int32 Value(int32 x) { return y; }"),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.Equal(1, invalidExit);
        Assert.Empty(invalidError);
        using (var document = JsonDocument.Parse(invalidOutput))
        {
            var result = document.RootElement;
            Assert.False(result.GetProperty("success").GetBoolean());
            Assert.Equal(3, result.GetProperty("result").GetProperty("errorCode").GetInt32());
            Assert.Equal(30, result.GetProperty("result").GetProperty("errorSpan")
                .GetProperty("start").GetInt32());
            Assert.Empty(result.GetProperty("diagnostics").EnumerateArray());
        }

        var (lexExit, lexOutput, lexError) = await RunAsync(Encoding.UTF8.GetBytes("@"),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.Equal(1, lexExit);
        Assert.Empty(lexError);
        using (var document = JsonDocument.Parse(lexOutput))
        {
            var result = document.RootElement;
            Assert.False(result.GetProperty("success").GetBoolean());
            Assert.False(result.TryGetProperty("result", out _));
            Assert.Equal("TRM0001", result.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task FrontendComponentRejectsMissingSourcesWithoutPublishingAResultAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"terrum-missing-frontend-{Guid.NewGuid():N}");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Value(int32 x) { return x; }"),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(2, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }
}
