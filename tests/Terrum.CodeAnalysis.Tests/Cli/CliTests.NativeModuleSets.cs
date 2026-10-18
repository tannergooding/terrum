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
    public async Task NativeSetCliCompilesValidatesAndPreservesOutputOnFailureAsync()
    {
        var directory = Path.Combine(TestArtifactPaths.DirectoryPath, "native-set-cli-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        var first = Path.Combine(directory, "first.trmn");
        var second = Path.Combine(directory, "second.trmn");
        var consumer = Path.Combine(directory, "consumer.tnset");
        var relativeConsumer = Path.GetRelativePath(Environment.CurrentDirectory, consumer);
        var source = Path.Combine(directory, "consumer.terrum");
        var firstSource = Path.Combine(directory, "first.terrum");
        var secondSource = Path.Combine(directory, "second.terrum");
        try
        {
            await File.WriteAllTextAsync(firstSource, "public int32 Alpha() { return 20; }");
            await File.WriteAllTextAsync(secondSource, "public int32 Beta() { return 22; }");
            await File.WriteAllTextAsync(source, "int32 Main() { return Alpha() + Beta(); }");
            Assert.That((await RunAsync([], "compile-native", "producer", firstSource, "First",
                "--export", "Alpha", "-o", first, "--runtime", "fail-fast")).ExitCode, Is.EqualTo(0));
            Assert.That((await RunAsync([], "compile-native", "producer", secondSource, "Second",
                "--export", "Beta", "-o", second, "--runtime", "fail-fast")).ExitCode, Is.EqualTo(0));
            var (compiled, json, _) = await RunAsync([], "compile-native-set", source,
                "--provider", second, "--provider", first, "-o", relativeConsumer, "--runtime", "fail-fast", "--json");
            Assert.That(compiled, Is.EqualTo(0));
            using var artifact = JsonDocument.Parse(json);
            var metadata = artifact.RootElement.GetProperty("artifact");
            Assert.That(metadata.GetProperty("dependencies").GetArrayLength(), Is.EqualTo(2));
            var bytes = await File.ReadAllBytesAsync(consumer);
            Assert.That(metadata.GetProperty("kind").GetString(), Is.EqualTo("native-consumer-set"));
            Assert.That(metadata.GetProperty("path").GetString(), Is.EqualTo(Path.GetFullPath(consumer)));
            Assert.That(metadata.GetProperty("length").GetInt32(), Is.EqualTo(bytes.Length));
            Assert.That(metadata.GetProperty("sha256").GetString(), Is.EqualTo(Convert.ToHexString(SHA256.HashData(bytes))));
            var (validation, validationJson, _) = await RunAsync([], "validate-native-set", relativeConsumer,
                "--provider", first, "--provider", second, "--json");
            Assert.That(validation, Is.EqualTo(0));
            using var checkedArtifact = JsonDocument.Parse(validationJson);
            var checkedMetadata = checkedArtifact.RootElement.GetProperty("artifact");
            Assert.That(checkedMetadata.GetProperty("path").GetString(), Is.EqualTo(Path.GetFullPath(consumer)));
            Assert.That(checkedMetadata.GetProperty("length").GetInt32(), Is.EqualTo(bytes.Length));
            Assert.That(checkedMetadata.GetProperty("sha256").GetString(), Is.EqualTo(Convert.ToHexString(SHA256.HashData(bytes))));
            Assert.That((await RunAsync([], "run-native-set", consumer,
                "--provider", second, "--provider", first, "--runtime", "fail-fast")).ExitCode, Is.EqualTo(42));
            var (rejected, output, _) = await RunAsync([], "compile-native-set", source,
                "--provider", first, "--provider", first, "-o", consumer, "--runtime", "fail-fast", "--json");
            Assert.That(rejected, Is.EqualTo(1));
            using var error = JsonDocument.Parse(output);
            Assert.That(error.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5002"));
            Assert.That(await File.ReadAllBytesAsync(consumer), Is.EqualTo(bytes));

            var secondary = Path.Combine(directory, "broken.terrum");
            await File.WriteAllTextAsync(secondary, "int32 Broken() { return Missing; }");
            var (badSource, badJson, _) = await RunAsync([], "compile-native-set", source,
                "--source", secondary, "--provider", first, "--provider", second,
                "-o", consumer, "--runtime", "fail-fast", "--json");
            Assert.That(badSource, Is.EqualTo(1));
            using var diagnostics = JsonDocument.Parse(badJson);
            var root = diagnostics.RootElement;
            Assert.That(root.GetProperty("command").GetString(), Is.EqualTo("compile-native-set"));
            Assert.That(root.GetProperty("sources").GetArrayLength(), Is.EqualTo(2));
            Assert.That(root.GetProperty("sources")[1].GetProperty("path").GetString(), Is.EqualTo(secondary));
            Assert.That(global::System.Linq.Enumerable.Any(root.GetProperty("diagnostics").EnumerateArray(),item => item.GetProperty("sourceIndex").GetInt32() == 1),Is.True);
            Assert.That(await File.ReadAllBytesAsync(consumer), Is.EqualTo(bytes));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
