// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [TestCase(true,42)]
    [TestCase(false,unchecked((int)0xE0540003))]
    public async Task NoReturnOutputBranchesExecuteWithoutManufacturedValuesAsync(bool success, int expected)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "LanguageFeatures", "Contracts", "no-return.terrum"));
        if (!success)
        {
            source = source.Replace("return Select(true);", "return Select(false);", StringComparison.Ordinal);
        }
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "jit", "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(expected));
        Assert.That(output, Is.Empty);
        Assert.That(error, Is.Empty);
    }

    [Test]
    public async Task NonReturningTailCallNeedsNoInventedReturnValueAsync()
    {
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(
            "[DoesNotReturn] void Stop() { require false; } int32 Main() { Stop(); }"),
            "jit", "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(unchecked((int)0xE0540003)));
        Assert.That(output, Is.Empty);
        Assert.That(error, Is.Empty);
    }

    [Test]
    public async Task UnprovedNoReturnDeclarationPreservesPriorArtifactAsync()
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-no-return-{Guid.NewGuid():N}.exe");
        byte[] original = [1, 2, 3];
        try
        {
            await File.WriteAllBytesAsync(path, original);
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(
                "[DoesNotReturn] void Stop() { return; } int32 Main() { Stop(); }"),
                "build", "-", "-o", path, "--runtime", "fail-fast", "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM3018"));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
