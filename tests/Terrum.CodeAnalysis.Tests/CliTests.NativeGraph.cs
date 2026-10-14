// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Test]
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
            Assert.That((await RunAsync([], "compile-native", "producer", leafSource, "NodeB",
                "--export", "Leaf", "-o", leaf, "--runtime", "fail-fast")).ExitCode, Is.EqualTo(0));
            var (middleExit, middleJson, _) = await RunAsync([], "compile-native-graph", "producer", middleSource,
                "NodeA", "--provider", leaf, "--export", "Middle", "-o", middle, "--runtime", "fail-fast", "--json");
            Assert.That(middleExit, Is.EqualTo(0));
            var middleBytes = await File.ReadAllBytesAsync(middle);
            using var middleArtifact = JsonDocument.Parse(middleJson);
            Assert.That(middleArtifact.RootElement.GetProperty("artifact").GetProperty("kind").GetString(), Is.EqualTo("native-graph-producer"));
            var (rootExit, rootJson, _) = await RunAsync([], "compile-native-graph", "consumer", rootSource,
                "--provider", middle, "-o", root, "--runtime", "fail-fast", "--json");
            Assert.That(rootExit, Is.EqualTo(0));
            var rootBytes = await File.ReadAllBytesAsync(root);
            using var rootArtifact = JsonDocument.Parse(rootJson);
            var metadata = rootArtifact.RootElement.GetProperty("artifact");
            Assert.That(metadata.GetProperty("kind").GetString(), Is.EqualTo("native-graph-consumer"));
            Assert.That(metadata.GetProperty("length").GetInt32(), Is.EqualTo(rootBytes.Length));
            Assert.That(metadata.GetProperty("sha256").GetString(), Is.EqualTo(Convert.ToHexString(SHA256.HashData(rootBytes))));
            Assert.That((await RunAsync([], "validate-native-graph", root,
                "--provider", middle, "--provider", leaf)).ExitCode, Is.EqualTo(0));
            Assert.That((await RunAsync([], "run-native-graph", root,
                "--provider", leaf, "--provider", middle, "--runtime", "fail-fast")).ExitCode, Is.EqualTo(42));

            var badSource = Path.Combine(directory, "bad.terrum");
            await File.WriteAllTextAsync(badSource, "int32 Invalid() { return Missing; }");
            var (failed, failedJson, _) = await RunAsync([], "compile-native-graph", "consumer", rootSource,
                "--source", badSource, "--provider", middle, "-o", root, "--runtime", "fail-fast", "--json");
            Assert.That(failed, Is.EqualTo(1));
            using var errors = JsonDocument.Parse(failedJson);
            Assert.That(errors.RootElement.GetProperty("command").GetString(), Is.EqualTo("compile-native-graph"));
            Assert.That(global::System.Linq.Enumerable.Any(errors.RootElement.GetProperty("diagnostics").EnumerateArray(),item => item.GetProperty("sourceIndex").GetInt32() == 1),Is.True);
            Assert.That(await File.ReadAllBytesAsync(root), Is.EqualTo(rootBytes));

            var (collision, collisionJson, _) = await RunAsync([], "compile-native-graph", "producer",
                middleSource, "NodeB", "--provider", leaf, "--export", "Middle", "-o", middle,
                "--runtime", "fail-fast", "--json");
            Assert.That(collision, Is.EqualTo(1));
            using var collisionError = JsonDocument.Parse(collisionJson);
            Assert.That(collisionError.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5002"));
            Assert.That(await File.ReadAllBytesAsync(middle), Is.EqualTo(middleBytes));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
