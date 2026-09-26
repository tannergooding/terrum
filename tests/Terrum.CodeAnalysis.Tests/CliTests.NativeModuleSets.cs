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
    public async Task NativeSetCliCompilesValidatesAndPreservesOutputOnFailureAsync()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "native-set-cli-" + Guid.NewGuid().ToString("N"));
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
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer", firstSource, "First",
                "--export", "Alpha", "-o", first, "--runtime", "fail-fast")).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer", secondSource, "Second",
                "--export", "Beta", "-o", second, "--runtime", "fail-fast")).ExitCode);
            var (compiled, json, _) = await RunAsync([], "compile-native-set", source,
                "--provider", second, "--provider", first, "-o", relativeConsumer, "--runtime", "fail-fast", "--json");
            Assert.Equal(0, compiled);
            using var artifact = JsonDocument.Parse(json);
            var metadata = artifact.RootElement.GetProperty("artifact");
            Assert.Equal(2, metadata.GetProperty("dependencies").GetArrayLength());
            var bytes = await File.ReadAllBytesAsync(consumer);
            Assert.Equal("native-consumer-set", metadata.GetProperty("kind").GetString());
            Assert.Equal(Path.GetFullPath(consumer), metadata.GetProperty("path").GetString());
            Assert.Equal(bytes.Length, metadata.GetProperty("length").GetInt32());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), metadata.GetProperty("sha256").GetString());
            var (validation, validationJson, _) = await RunAsync([], "validate-native-set", relativeConsumer,
                "--provider", first, "--provider", second, "--json");
            Assert.Equal(0, validation);
            using var checkedArtifact = JsonDocument.Parse(validationJson);
            var checkedMetadata = checkedArtifact.RootElement.GetProperty("artifact");
            Assert.Equal(Path.GetFullPath(consumer), checkedMetadata.GetProperty("path").GetString());
            Assert.Equal(bytes.Length, checkedMetadata.GetProperty("length").GetInt32());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),
                checkedMetadata.GetProperty("sha256").GetString());
            Assert.Equal(42, (await RunAsync([], "run-native-set", consumer,
                "--provider", second, "--provider", first, "--runtime", "fail-fast")).ExitCode);
            var (rejected, output, _) = await RunAsync([], "compile-native-set", source,
                "--provider", first, "--provider", first, "-o", consumer, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, rejected);
            using var error = JsonDocument.Parse(output);
            Assert.Equal("TRM5002", error.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal(bytes, await File.ReadAllBytesAsync(consumer));

            var secondary = Path.Combine(directory, "broken.terrum");
            await File.WriteAllTextAsync(secondary, "int32 Broken() { return Missing; }");
            var (badSource, badJson, _) = await RunAsync([], "compile-native-set", source,
                "--source", secondary, "--provider", first, "--provider", second,
                "-o", consumer, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, badSource);
            using var diagnostics = JsonDocument.Parse(badJson);
            var root = diagnostics.RootElement;
            Assert.Equal("compile-native-set", root.GetProperty("command").GetString());
            Assert.Equal(2, root.GetProperty("sources").GetArrayLength());
            Assert.Equal(secondary, root.GetProperty("sources")[1].GetProperty("path").GetString());
            Assert.Contains(root.GetProperty("diagnostics").EnumerateArray(),
                item => item.GetProperty("sourceIndex").GetInt32() == 1);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(consumer));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
