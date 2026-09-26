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
    public async Task NativeGraphCliPreservesIntermediateAndRootArtifactsOnFailuresAsync()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts",
            "native-graph-cli-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        var leafSource = Path.Combine(directory, "leaf.terrum");
        var middleSource = Path.Combine(directory, "middle.terrum");
        var rootSource = Path.Combine(directory, "root.terrum");
        var leaf = Path.Combine(directory, "leaf.trmn");
        var middle = Path.Combine(directory, "middle.trng");
        var root = Path.Combine(directory, "root.trng");
        try
        {
            await File.WriteAllTextAsync(leafSource, "[NoThrow] public int32 Leaf() { return 40; }");
            await File.WriteAllTextAsync(middleSource, "public int32 Middle() { return Leaf() + 1; }");
            await File.WriteAllTextAsync(rootSource, "int32 Main() { return Middle() + 1; }");
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer", leafSource, "NodeB",
                "--export", "Leaf", "-o", leaf, "--runtime", "fail-fast")).ExitCode);
            var (middleExit, middleJson, _) = await RunAsync([], "compile-native-graph", "producer", middleSource,
                "NodeA", "--provider", leaf, "--export", "Middle", "-o", middle, "--runtime", "fail-fast", "--json");
            Assert.Equal(0, middleExit);
            var middleBytes = await File.ReadAllBytesAsync(middle);
            using var middleArtifact = JsonDocument.Parse(middleJson);
            Assert.Equal("native-graph-producer",
                middleArtifact.RootElement.GetProperty("artifact").GetProperty("kind").GetString());
            var (rootExit, rootJson, _) = await RunAsync([], "compile-native-graph", "consumer", rootSource,
                "--provider", middle, "-o", root, "--runtime", "fail-fast", "--json");
            Assert.Equal(0, rootExit);
            var rootBytes = await File.ReadAllBytesAsync(root);
            using var rootArtifact = JsonDocument.Parse(rootJson);
            var metadata = rootArtifact.RootElement.GetProperty("artifact");
            Assert.Equal("native-graph-consumer", metadata.GetProperty("kind").GetString());
            Assert.Equal(rootBytes.Length, metadata.GetProperty("length").GetInt32());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(rootBytes)), metadata.GetProperty("sha256").GetString());
            Assert.Equal(0, (await RunAsync([], "validate-native-graph", root,
                "--provider", middle, "--provider", leaf)).ExitCode);
            Assert.Equal(42, (await RunAsync([], "run-native-graph", root,
                "--provider", leaf, "--provider", middle, "--runtime", "fail-fast")).ExitCode);

            var badSource = Path.Combine(directory, "bad.terrum");
            await File.WriteAllTextAsync(badSource, "int32 Invalid() { return Missing; }");
            var (failed, failedJson, _) = await RunAsync([], "compile-native-graph", "consumer", rootSource,
                "--source", badSource, "--provider", middle, "-o", root, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, failed);
            using var errors = JsonDocument.Parse(failedJson);
            Assert.Equal("compile-native-graph", errors.RootElement.GetProperty("command").GetString());
            Assert.Contains(errors.RootElement.GetProperty("diagnostics").EnumerateArray(),
                item => item.GetProperty("sourceIndex").GetInt32() == 1);
            Assert.Equal(rootBytes, await File.ReadAllBytesAsync(root));

            var (collision, collisionJson, _) = await RunAsync([], "compile-native-graph", "producer",
                middleSource, "NodeB", "--provider", leaf, "--export", "Middle", "-o", middle,
                "--runtime", "fail-fast", "--json");
            Assert.Equal(1, collision);
            using var collisionError = JsonDocument.Parse(collisionJson);
            Assert.Equal("TRM5002",
                collisionError.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal(middleBytes, await File.ReadAllBytesAsync(middle));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
