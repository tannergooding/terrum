// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Fact]
    public async Task GraphCatalogCliReportsOnlyUniqueSnapshotSelectionAsync()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts",
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
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer", source, "NodeB",
                "--export", "Leaf", "-o", oldLeaf, "--runtime", "fail-fast")).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native-graph", "producer", middleSource, "NodeA",
                "--provider", oldLeaf, "--export", "Middle", "-o", middle, "--runtime", "fail-fast")).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native-graph", "consumer", rootSource,
                "--provider", middle, "-o", root, "--runtime", "fail-fast")).ExitCode);
            await File.WriteAllTextAsync(source, "public int32 Leaf() { return 42; }");
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer", source, "NodeB",
                "--export", "Leaf", "-o", replacement, "--runtime", "fail-fast")).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer", source, "Other",
                "--export", "Leaf", "-o", unrelated, "--runtime", "fail-fast")).ExitCode);
            var savedRoot = await File.ReadAllBytesAsync(root);
            var savedMiddle = await File.ReadAllBytesAsync(middle);

            var (exit, output, _) = await RunAsync([], "resolve-native-graph", root,
                "--candidate", unrelated, "--candidate", replacement, "--candidate", middle, "--json");
            Assert.Equal(0, exit);
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement;
            Assert.Equal(1, result.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("resolve-native-graph", result.GetProperty("command").GetString());
            Assert.True(result.GetProperty("success").GetBoolean());
            Assert.Equal(0, result.GetProperty("diagnostics").GetArrayLength());
            var artifact = result.GetProperty("artifact");
            Assert.Equal("native-graph-consumer", artifact.GetProperty("kind").GetString());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(savedRoot)), artifact.GetProperty("sha256").GetString());
            var selected = artifact.GetProperty("selectedProviders");
            Assert.Equal(2, selected.GetArrayLength());
            Assert.Equal("NodeA", selected[0].GetProperty("identity").GetString());
            Assert.Equal(Path.GetFullPath(middle), selected[0].GetProperty("path").GetString());
            Assert.Equal("NodeB", selected[1].GetProperty("identity").GetString());
            Assert.Equal(Path.GetFullPath(replacement), selected[1].GetProperty("path").GetString());

            var (ambiguous, ambiguousJson, _) = await RunAsync([], "resolve-native-graph", root,
                "--candidate", oldLeaf, "--candidate", replacement, "--candidate", middle, "--json");
            Assert.Equal(1, ambiguous);
            using var error = JsonDocument.Parse(ambiguousJson);
            Assert.Equal("TRM5002", error.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString());
            Assert.Equal(savedRoot, await File.ReadAllBytesAsync(root));
            Assert.Equal(savedMiddle, await File.ReadAllBytesAsync(middle));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
