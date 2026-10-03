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
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ScannerPublishesDocumentationKindsAndCurrentProfileAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        const string Source = "/// line\n//* block *// /** ordinary **/ int32 Main() { return 42; }";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), "scan-component",
            Path.Combine(root, "self-host", "Scanner.terrum"), "-", "--runtime", policy, "--json");
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(9, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("scanner-9", document.RootElement.GetProperty("component").GetString());
        Assert.Equal(["DocumentationLineComment", "NewLine", "DocumentationBlockComment", "Whitespace",
                "MultiLineComment", "Whitespace"],
            document.RootElement.GetProperty("tokens")[0].GetProperty("leadingTrivia")
                .EnumerateArray().Select(item => item.GetProperty("kind").GetString()));
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task FrontendExecutesThroughCurrentCommentFencesAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        const string Source = "/// heading\nint32 Main() { //** doc **// /** ordinary **/ return 42; }";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "frontend-run", Path.Combine(root, "self-host"), "-", "--runtime", policy, "--json");
        Assert.Equal(42, exit);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task CompiledComponentExecutesWithDocumentationFencesAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var image = Path.Combine(root, "artifacts", $"comment-fences-worker-valid-{Guid.NewGuid():N}.exe");
        try
        {
            var (exit, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("/// heading\nint32 Main() { //** block **// return 42; }"),
                "build-component", Path.Combine(root, "self-host", "Scanner.terrum"), "-",
                "-o", image, "--runtime", policy, "--json");
            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(42, await ExecuteNativeImageAsync(image).ConfigureAwait(true));
        }
        finally
        {
            File.Delete(image);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task OverlongFenceRejectsBuildComponentWithoutPublishingArtifactAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var image = Path.Combine(root, "artifacts", $"comment-fences-worker-invalid-{Guid.NewGuid():N}.exe");
        try
        {
            var (exit, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { /* **/ */ return 42; }"),
                "build-component", Path.Combine(root, "self-host", "Scanner.terrum"), "-",
                "-o", image, "--runtime", policy, "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("code").GetString() == "TRM0010");
            Assert.False(File.Exists(image));
        }
        finally
        {
            File.Delete(image);
        }
    }
}
