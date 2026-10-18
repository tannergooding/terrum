// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [Test]
    public async Task GraphCatalogCliReportsOnlyUniqueSnapshotSelectionAsync()
    {
        var directory = Path.Combine(TestArtifactPaths.DirectoryPath,
            "native-graph-catalog-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "leaf.terrum");
        var middleSource = Path.Combine(directory, "middle.terrum");
        var rootSource = Path.Combine(directory, "root.terrum");
        var oldLeaf = Path.Combine(directory, "old.trmn");
        var replacement = Path.Combine(directory, "replacement.trmn");
        var unrelated = Path.Combine(directory, "unrelated.trmn");
        var middle = Path.Combine(directory, "middle.trng");
        var root = Path.Combine(directory, "root.trng");
        try
        {
            await File.WriteAllTextAsync(source, "public int32 Leaf() { return 40; }");
            await File.WriteAllTextAsync(middleSource, "public int32 Middle() { return Leaf() + 1; }");
            await File.WriteAllTextAsync(rootSource, "int32 Main() { return Middle() + 1; }");
            Assert.That((await RunAsync([], "compile-native", "producer", source, "NodeB",
                "--export", "Leaf", "-o", oldLeaf, "--runtime", "fail-fast")).ExitCode, Is.EqualTo(0));
            Assert.That((await RunAsync([], "compile-native-graph", "producer", middleSource, "NodeA",
                "--provider", oldLeaf, "--export", "Middle", "-o", middle, "--runtime", "fail-fast")).ExitCode, Is.EqualTo(0));
            Assert.That((await RunAsync([], "compile-native-graph", "consumer", rootSource,
                "--provider", middle, "-o", root, "--runtime", "fail-fast")).ExitCode, Is.EqualTo(0));
            await File.WriteAllTextAsync(source, "public int32 Leaf() { return 42; }");
            Assert.That((await RunAsync([], "compile-native", "producer", source, "NodeB",
                "--export", "Leaf", "-o", replacement, "--runtime", "fail-fast")).ExitCode, Is.EqualTo(0));
            Assert.That((await RunAsync([], "compile-native", "producer", source, "Other",
                "--export", "Leaf", "-o", unrelated, "--runtime", "fail-fast")).ExitCode, Is.EqualTo(0));
            var savedRoot = await File.ReadAllBytesAsync(root);
            var savedMiddle = await File.ReadAllBytesAsync(middle);

            var (exit, output, _) = await RunAsync([], "resolve-native-graph", root,
                "--candidate", unrelated, "--candidate", replacement, "--candidate", middle, "--json");
            Assert.That(exit, Is.EqualTo(0));
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement;
            Assert.That(result.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(result.GetProperty("command").GetString(), Is.EqualTo("resolve-native-graph"));
            Assert.That(result.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("diagnostics").GetArrayLength(), Is.EqualTo(0));
            var artifact = result.GetProperty("artifact");
            Assert.That(artifact.GetProperty("kind").GetString(), Is.EqualTo("native-graph-consumer"));
            Assert.That(artifact.GetProperty("sha256").GetString(), Is.EqualTo(Convert.ToHexString(SHA256.HashData(savedRoot))));
            var selected = artifact.GetProperty("selectedProviders");
            Assert.That(selected.GetArrayLength(), Is.EqualTo(2));
            Assert.That(selected[0].GetProperty("identity").GetString(), Is.EqualTo("NodeA"));
            Assert.That(selected[0].GetProperty("path").GetString(), Is.EqualTo(Path.GetFullPath(middle)));
            Assert.That(selected[1].GetProperty("identity").GetString(), Is.EqualTo("NodeB"));
            Assert.That(selected[1].GetProperty("path").GetString(), Is.EqualTo(Path.GetFullPath(replacement)));

            var (ambiguous, ambiguousJson, _) = await RunAsync([], "resolve-native-graph", root,
                "--candidate", oldLeaf, "--candidate", replacement, "--candidate", middle, "--json");
            Assert.That(ambiguous, Is.EqualTo(1));
            using var error = JsonDocument.Parse(ambiguousJson);
            Assert.That(error.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM5002"));
            Assert.That(await File.ReadAllBytesAsync(root), Is.EqualTo(savedRoot));
            Assert.That(await File.ReadAllBytesAsync(middle), Is.EqualTo(savedMiddle));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
