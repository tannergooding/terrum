// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("/**/",new[] { "TRM0003" },new[] { 0 },new[] { 4 })]
    [TestCase("/** ***/",new[] { "TRM0010", "TRM0003" },new[] { 4, 0 },new[] { 4, 8 })]
    [TestCase("//* */",new[] { "TRM0003" },new[] { 0 },new[] { 6 })]
    [TestCase("//* **// *//",new[] { "TRM0010" },new[] { 4 },new[] { 4 })]
    [TestCase("//* **/ *//",new string[] { },new int[] { },new int[] { })]
    [TestCase("/*** **/ x ***/",new string[] { },new int[] { },new int[] { })]
    [TestCase("/** /**/ **/",new[] { "TRM0011" },new[] { 9 },new[] { 3 })]
    [TestCase("//** /* \" \r\n **//",new string[] { },new int[] { },new int[] { })]
    public async Task CompiledFenceAuditHasExactDiagnosticsUnderBothPoliciesAsync(
        string source, string[] codes, int[] starts, int[] lengths)
    {
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "scan-component", SelfHostPaths.SourceDirectory(RepositoryRoot()),
                "-", "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(codes.Length == 0 ? 0 : 1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            var diagnostics = document.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
            Assert.That(diagnostics.Select(value => value.GetProperty("code").GetString()), Is.EqualTo(codes));
            Assert.That(diagnostics.Select(value => value.GetProperty("start").GetInt32()), Is.EqualTo(starts));
            Assert.That(diagnostics.Select(value => value.GetProperty("length").GetInt32()), Is.EqualTo(lengths));
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task InvalidFenceRetainsNativeFrontendOutputAsync(string policy)
    {
        var image = Path.Combine(TestArtifactPaths.DirectoryPath, $"comment-audit-{Guid.NewGuid():N}.exe");
        try
        {
            var (built, buildOutput, buildError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { /** /* */ text **/ return 42; }"),
                "frontend-build", SelfHostPaths.SourceDirectory(RepositoryRoot()), "-", "-o", image,
                "--runtime", policy, "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{buildError}");
            Assert.That(await ExecuteNativeImageAsync(image), Is.EqualTo(42));
            var original = await File.ReadAllBytesAsync(image);
            var (rejected, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { /** ***/ return 42; }"),
                "frontend-build", SelfHostPaths.SourceDirectory(RepositoryRoot()), "-", "-o", image,
                "--runtime", policy, "--json");
            Assert.That(rejected == 1, Is.True, $"{output}{error}");
            Assert.That(await File.ReadAllBytesAsync(image), Is.EqualTo(original));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(image).ConfigureAwait(true);
        }
    }
}
