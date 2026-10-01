// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("/**/", new[] { "TRM0003" }, new[] { 0 }, new[] { 4 })]
    [InlineData("/** ***/", new[] { "TRM0010", "TRM0003" }, new[] { 4, 0 }, new[] { 4, 8 })]
    [InlineData("//* */", new[] { "TRM0003" }, new[] { 0 }, new[] { 6 })]
    [InlineData("//* **// *//", new[] { "TRM0010" }, new[] { 4 }, new[] { 4 })]
    [InlineData("//* **/ *//", new string[] { }, new int[] { }, new int[] { })]
    [InlineData("/*** **/ x ***/", new string[] { }, new int[] { }, new int[] { })]
    [InlineData("/** /**/ **/", new[] { "TRM0011" }, new[] { 9 }, new[] { 3 })]
    [InlineData("//** /* \" \r\n **//", new string[] { }, new int[] { }, new int[] { })]
    public async Task CompiledFenceAuditHasExactDiagnosticsUnderBothPoliciesAsync(
        string source, string[] codes, int[] starts, int[] lengths)
    {
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "scan-component", Path.Combine(RepositoryRoot(), "self-host", "Scanner.terrum"),
                "-", "--runtime", policy, "--json");
            Assert.Equal(codes.Length == 0 ? 0 : 1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            var diagnostics = document.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
            Assert.Equal(codes, diagnostics.Select(value => value.GetProperty("code").GetString()));
            Assert.Equal(starts, diagnostics.Select(value => value.GetProperty("start").GetInt32()));
            Assert.Equal(lengths, diagnostics.Select(value => value.GetProperty("length").GetInt32()));
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task InvalidFenceRetainsNativeFrontendOutputAsync(string policy)
    {
        var image = Path.Combine(RepositoryRoot(), "artifacts", $"comment-audit-{Guid.NewGuid():N}.exe");
        try
        {
            var (built, buildOutput, buildError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { /** /* */ text **/ return 42; }"),
                "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", image,
                "--runtime", policy, "--json");
            Assert.True(built == 0, $"{buildOutput}{buildError}");
            Assert.Equal(42, await ExecuteNativeImageAsync(image));
            var original = await File.ReadAllBytesAsync(image);
            var (rejected, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { /** ***/ return 42; }"),
                "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", image,
                "--runtime", policy, "--json");
            Assert.True(rejected == 1, $"{output}{error}");
            Assert.Equal(original, await File.ReadAllBytesAsync(image));
        }
        finally
        {
            File.Delete(image);
        }
    }
}
