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
    [Test]
    public async Task NoThrowValueAndOutputContractsExecuteThroughJitAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllBytesAsync(Path.Combine(root, "samples", "LanguageFeatures", "Contracts", "no-throw.terrum"));
        var (exit, output, error) = await RunAsync(source, "jit", "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(42));
        Assert.That(output, Is.Empty);
        Assert.That(error, Is.Empty);
    }

    [Test]
    public async Task NoThrowViolationHasSourceCoordinatesAndPreservesNativeOutputAsync()
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-no-throw-{Guid.NewGuid():N}.exe");
        byte[] original = [1, 2, 3];
        try
        {
            await File.WriteAllBytesAsync(path, original);
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(
                "[NoThrow] int32 Main() { require false; return 0; }"),
                "build", "-", "-o", path, "--runtime", "fail-fast", "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            var diagnostic = global::System.Linq.Enumerable.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.That(diagnostic.GetProperty("code").GetString(), Is.EqualTo("TRM3017"));
            Assert.That(diagnostic.GetProperty("span").GetProperty("length").GetInt32() > 0, Is.True);
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
