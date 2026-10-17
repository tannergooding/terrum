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
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScannerPublishesDocumentationKindsAndCurrentProfileAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        const string Source = "/// line\n//* block *// /** ordinary **/ int32 Main() { return 42; }";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), "scan-component",
            SelfHostPaths.SourceDirectory(root), "-", "--runtime", policy, "--json");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(10));
        Assert.That(document.RootElement.GetProperty("component").GetString(), Is.EqualTo("scanner-10"));
        Assert.That(document.RootElement.GetProperty("tokens")[0].GetProperty("leadingTrivia")
                .EnumerateArray().Select(item => item.GetProperty("kind").GetString()), Is.EqualTo(["DocumentationLineComment", "NewLine", "DocumentationBlockComment", "Whitespace",
                "MultiLineComment", "Whitespace"]));
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task FrontendExecutesThroughCurrentCommentFencesAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        const string Source = "/// heading\nint32 Main() { //** doc **// /** ordinary **/ return 42; }";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "frontend-run", SelfHostPaths.SourceDirectory(root), "-", "--runtime", policy, "--json");
        Assert.That(exit, Is.EqualTo(42));
        Assert.That(output, Is.Empty);
        Assert.That(error, Is.Empty);
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task CompiledComponentExecutesWithDocumentationFencesAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var image = Path.Combine(TestArtifactPaths.DirectoryPath, $"comment-fences-worker-valid-{Guid.NewGuid():N}.exe");
        try
        {
            var (exit, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("/// heading\nint32 Main() { //** block **// return 42; }"),
                "build-component", SelfHostPaths.SourceDirectory(root), "-",
                "-o", image, "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(await ExecuteNativeImageAsync(image).ConfigureAwait(true), Is.EqualTo(42));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(image).ConfigureAwait(true);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task OverlongFenceRejectsBuildComponentWithoutPublishingArtifactAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var image = Path.Combine(TestArtifactPaths.DirectoryPath, $"comment-fences-worker-invalid-{Guid.NewGuid():N}.exe");
        try
        {
            var (exit, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { /* **/ */ return 42; }"),
                "build-component", SelfHostPaths.SourceDirectory(root), "-",
                "-o", image, "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(global::System.Linq.Enumerable.Any(document.RootElement.GetProperty("diagnostics").EnumerateArray(),diagnostic => diagnostic.GetProperty("code").GetString() == "TRM0010"),Is.True);
            Assert.That(File.Exists(image), Is.False);
        }
        finally
        {
            File.Delete(image);
        }
    }
}
