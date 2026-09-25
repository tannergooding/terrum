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
    public async Task NativePackagesRunAnUnchangedConsumerWithAValidatedReplacementAsync()
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-native-package-cli-{Guid.NewGuid():N}");
        var source = prefix + ".producer.terrum";
        var helper = prefix + ".helper.terrum";
        var entry = prefix + ".consumer.terrum";
        var producer = prefix + ".producer.trmn";
        var replacement = prefix + ".replacement.trmn";
        var consumer = prefix + ".consumer.trmn";
        try
        {
            await File.WriteAllTextAsync(source, "public int32 Value() { return Helper(); }");
            await File.WriteAllTextAsync(helper, "int32 Helper() { return 42; }");
            await File.WriteAllTextAsync(entry, "int32 Main() { return Value(); }");
            var (producerExit, producerOutput, producerError) = await RunAsync([], "compile-native", "producer",
                source, "Example", "--source", helper, "--export", "Value", "-o", producer, "--runtime", "fail-fast", "--json");
            Assert.Equal(0, producerExit);
            Assert.Empty(producerError);
            using var producerDocument = JsonDocument.Parse(producerOutput);
            Assert.Equal("native-producer", producerDocument.RootElement.GetProperty("artifact").GetProperty("kind").GetString());
            Assert.Equal("Example", producerDocument.RootElement.GetProperty("artifact").GetProperty("identity").GetString());
            Assert.Equal(64, producerDocument.RootElement.GetProperty("artifact").GetProperty("imageSha256").GetString()?.Length);
            var originalProducer = await File.ReadAllBytesAsync(producer);
            Assert.Equal(originalProducer.Length, producerDocument.RootElement.GetProperty("artifact").GetProperty("length").GetInt32());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(originalProducer)),
                producerDocument.RootElement.GetProperty("artifact").GetProperty("sha256").GetString());

            var (consumerExit, _, consumerError) = await RunAsync([], "compile-native", "consumer", entry, producer,
                "-o", consumer, "--runtime", "fail-fast", "--json");
            Assert.Equal(0, consumerExit);
            Assert.Empty(consumerError);
            var originalConsumer = await File.ReadAllBytesAsync(consumer);
            var (overwrite, overwriteOutput, _) = await RunAsync([], "compile-native", "consumer", entry, producer,
                "-o", producer, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, overwrite);
            using var overwriteDocument = JsonDocument.Parse(overwriteOutput);
            Assert.Equal("TRM5001", overwriteDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal(originalProducer, await File.ReadAllBytesAsync(producer));
            var (first, firstOutput, firstError) = await RunAsync([], "run-native", consumer, producer, "--runtime", "fail-fast", "--json");
            Assert.Equal(42, first);
            Assert.Empty(firstOutput);
            Assert.Empty(firstError);

            await File.WriteAllTextAsync(helper, "int32 Helper() { return 43; }");
            var (replacementExit, _, _) = await RunAsync([], "compile-native", "producer", source, "Example",
                "--source", helper, "--export", "Value", "-o", replacement, "--runtime", "fail-fast", "--json");
            Assert.Equal(0, replacementExit);
            var (validation, validationOutput, _) = await RunAsync([], "validate-native", consumer, replacement, "--json");
            Assert.Equal(0, validation);
            using var validationDocument = JsonDocument.Parse(validationOutput);
            Assert.True(validationDocument.RootElement.GetProperty("success").GetBoolean());
            File.Delete(source);
            File.Delete(helper);
            File.Delete(entry);
            var (second, secondOutput, secondError) = await RunAsync([], "run-native", consumer, replacement, "--runtime", "fail-fast", "--json");
            Assert.Equal(43, second);
            Assert.Empty(secondOutput);
            Assert.Empty(secondError);
            Assert.Equal(originalConsumer, await File.ReadAllBytesAsync(consumer));

            await File.WriteAllTextAsync(source, "public bool Value() { require false; return true; }");
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer", source, "Example", "--export", "Value",
                "-o", replacement, "--runtime", "fail-fast", "--json")).ExitCode);
            var (rejected, rejectedOutput, _) = await RunAsync([], "run-native", consumer, replacement, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, rejected);
            using var rejectedDocument = JsonDocument.Parse(rejectedOutput);
            Assert.Equal("TRM5002", rejectedDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal(originalConsumer, await File.ReadAllBytesAsync(consumer));

            var damaged = await File.ReadAllBytesAsync(producer);
            damaged[^1] ^= 1;
            await File.WriteAllBytesAsync(producer, damaged);
            await File.WriteAllTextAsync(entry, "int32 Main() { return Value(); }");
            var (corrupt, corruptOutput, _) = await RunAsync([], "compile-native", "consumer", entry, producer,
                "-o", consumer, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, corrupt);
            using var corruptDocument = JsonDocument.Parse(corruptOutput);
            Assert.Equal("TRM5001", corruptDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal(originalConsumer, await File.ReadAllBytesAsync(consumer));
        }
        finally
        {
            File.Delete(source);
            File.Delete(helper);
            File.Delete(entry);
            File.Delete(producer);
            File.Delete(replacement);
            File.Delete(consumer);
        }
    }

    [Fact]
    public async Task NativePackageInputsAreBoundedAndCannotBeOverwrittenAsync()
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-native-input-cli-{Guid.NewGuid():N}");
        var source = prefix + ".source.trmn";
        var oversized = prefix + ".oversized.trmn";
        var output = prefix + ".output.trmn";
        const string Text = "public int32 Value() { return 42; }";
        byte[] original = [1, 2, 3];
        try
        {
            await File.WriteAllTextAsync(source, Text);
            await File.WriteAllBytesAsync(output, original);
            using (var stream = File.Create(oversized))
            {
                stream.SetLength((16 * 1024 * 1024) + 1);
            }
            var (overwrite, overwriteOutput, _) = await RunAsync([], "compile-native", "producer", source, "Example",
                "--export", "Value", "-o", source, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, overwrite);
            using var overwriteDocument = JsonDocument.Parse(overwriteOutput);
            Assert.Equal("TRM5001", overwriteDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal(Text, await File.ReadAllTextAsync(source));

            var (tooLarge, tooLargeOutput, _) = await RunAsync([], "compile-native", "consumer", source, oversized,
                "-o", output, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, tooLarge);
            using var tooLargeDocument = JsonDocument.Parse(tooLargeOutput);
            var diagnostic = tooLargeDocument.RootElement.GetProperty("diagnostics")[0];
            Assert.Equal("TRM5001", diagnostic.GetProperty("code").GetString());
            Assert.Contains("16 MiB", diagnostic.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Equal(original, await File.ReadAllBytesAsync(output));
        }
        finally
        {
            File.Delete(source);
            File.Delete(oversized);
            File.Delete(output);
        }
    }

    [Fact]
    public async Task NativePackageSourceFailuresRemainLocatedAndPreserveArtifactsAsync()
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-native-source-cli-{Guid.NewGuid():N}");
        var source = prefix + ".terrum";
        var helper = prefix + ".helper.terrum";
        var producer = prefix + ".trmn";
        byte[] original = [1, 2, 3];
        try
        {
            await File.WriteAllTextAsync(source, "public int32 Value() { return Helper(); }");
            await File.WriteAllTextAsync(helper, "int32 Helper() { return missing; }");
            await File.WriteAllBytesAsync(producer, original);
            var (exit, output, error) = await RunAsync([], "compile-native", "producer", source, "Example", "--source", helper,
                "--export", "Value", "-o", producer, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("compile-native", document.RootElement.GetProperty("command").GetString());
            var diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal(1, diagnostic.GetProperty("sourceIndex").GetInt32());
            Assert.Equal(original, await File.ReadAllBytesAsync(producer));

            var (duplicate, duplicateOutput, _) = await RunAsync([], "compile-native", "producer", source, "Example",
                "--source", source, "--export", "Value", "-o", producer, "--runtime", "fail-fast", "--json");
            Assert.Equal(2, duplicate);
            using var duplicateDocument = JsonDocument.Parse(duplicateOutput);
            Assert.Equal("TRM2000", duplicateDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal(original, await File.ReadAllBytesAsync(producer));
        }
        finally
        {
            File.Delete(source);
            File.Delete(helper);
            File.Delete(producer);
        }
    }

    [Theory]
    [InlineData("compile-native producer missing.terrum Example -o producer.trmn --runtime fail-fast --json")]
    [InlineData("compile-native producer missing.terrum Example --export Value -o producer.trmn --json")]
    [InlineData("compile-native consumer missing.terrum producer.trmn -o consumer.trmn --json")]
    [InlineData("compile-native consumer missing.terrum producer.trmn --export Value -o consumer.trmn --runtime fail-fast --json")]
    [InlineData("run-native consumer.trmn producer.trmn --json")]
    [InlineData("validate-native consumer.trmn producer.trmn --runtime fail-fast --json")]
    public async Task NativePackageCommandsRequireExplicitValidRolesAndRuntimeAsync(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var (exit, output, error) = await RunAsync([], arguments.Split(' '));
        Assert.Equal(2, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("TRM2000", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }
}
