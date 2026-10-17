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
    public async Task NativePackagesRunAnUnchangedConsumerWithAValidatedReplacementAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-native-package-cli-{Guid.NewGuid():N}");
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
            Assert.That(producerExit, Is.EqualTo(0));
            Assert.That(producerError, Is.Empty);
            using var producerDocument = JsonDocument.Parse(producerOutput);
            Assert.That(producerDocument.RootElement.GetProperty("artifact").GetProperty("kind").GetString(), Is.EqualTo("native-producer"));
            Assert.That(producerDocument.RootElement.GetProperty("artifact").GetProperty("identity").GetString(), Is.EqualTo("Example"));
            Assert.That(producerDocument.RootElement.GetProperty("artifact").GetProperty("imageSha256").GetString()?.Length, Is.EqualTo(64));
            var originalProducer = await File.ReadAllBytesAsync(producer);
            Assert.That(producerDocument.RootElement.GetProperty("artifact").GetProperty("length").GetInt32(), Is.EqualTo(originalProducer.Length));
            Assert.That(producerDocument.RootElement.GetProperty("artifact").GetProperty("sha256").GetString(), Is.EqualTo(Convert.ToHexString(SHA256.HashData(originalProducer))));

            var (consumerExit, _, consumerError) = await RunAsync([], "compile-native", "consumer", entry, producer,
                "-o", consumer, "--runtime", "fail-fast", "--json");
            Assert.That(consumerExit, Is.EqualTo(0));
            Assert.That(consumerError, Is.Empty);
            var originalConsumer = await File.ReadAllBytesAsync(consumer);
            var (overwrite, overwriteOutput, _) = await RunAsync([], "compile-native", "consumer", entry, producer,
                "-o", producer, "--runtime", "fail-fast", "--json");
            Assert.That(overwrite, Is.EqualTo(1));
            using var overwriteDocument = JsonDocument.Parse(overwriteOutput);
            Assert.That(overwriteDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(producer), Is.EqualTo(originalProducer));
            var (first, firstOutput, firstError) = await RunAsync([], "run-native", consumer, producer, "--runtime", "fail-fast", "--json");
            Assert.That(first, Is.EqualTo(42));
            Assert.That(firstOutput, Is.Empty);
            Assert.That(firstError, Is.Empty);

            await File.WriteAllTextAsync(helper, "int32 Helper() { return 43; }");
            var (replacementExit, _, _) = await RunAsync([], "compile-native", "producer", source, "Example",
                "--source", helper, "--export", "Value", "-o", replacement, "--runtime", "fail-fast", "--json");
            Assert.That(replacementExit, Is.EqualTo(0));
            var (validation, validationOutput, _) = await RunAsync([], "validate-native", consumer, replacement, "--json");
            Assert.That(validation, Is.EqualTo(0));
            using var validationDocument = JsonDocument.Parse(validationOutput);
            Assert.That(validationDocument.RootElement.GetProperty("success").GetBoolean(), Is.True);
            File.Delete(source);
            File.Delete(helper);
            File.Delete(entry);
            var (second, secondOutput, secondError) = await RunAsync([], "run-native", consumer, replacement, "--runtime", "fail-fast", "--json");
            Assert.That(second, Is.EqualTo(43));
            Assert.That(secondOutput, Is.Empty);
            Assert.That(secondError, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(consumer), Is.EqualTo(originalConsumer));

            await File.WriteAllTextAsync(source, "public bool Value() { require false; return true; }");
            Assert.That((await RunAsync([], "compile-native", "producer", source, "Example", "--export", "Value",
                "-o", replacement, "--runtime", "fail-fast", "--json")).ExitCode, Is.EqualTo(0));
            var (rejected, rejectedOutput, _) = await RunAsync([], "run-native", consumer, replacement, "--runtime", "fail-fast", "--json");
            Assert.That(rejected, Is.EqualTo(1));
            using var rejectedDocument = JsonDocument.Parse(rejectedOutput);
            Assert.That(rejectedDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5002"));
            Assert.That(await File.ReadAllBytesAsync(consumer), Is.EqualTo(originalConsumer));

            var damaged = await File.ReadAllBytesAsync(producer);
            damaged[^1] ^= 1;
            await File.WriteAllBytesAsync(producer, damaged);
            await File.WriteAllTextAsync(entry, "int32 Main() { return Value(); }");
            var (corrupt, corruptOutput, _) = await RunAsync([], "compile-native", "consumer", entry, producer,
                "-o", consumer, "--runtime", "fail-fast", "--json");
            Assert.That(corrupt, Is.EqualTo(1));
            using var corruptDocument = JsonDocument.Parse(corruptOutput);
            Assert.That(corruptDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(consumer), Is.EqualTo(originalConsumer));
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

    [Test]
    public async Task NativePackageInputsAreBoundedAndCannotBeOverwrittenAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-native-input-cli-{Guid.NewGuid():N}");
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
            Assert.That(overwrite, Is.EqualTo(1));
            using var overwriteDocument = JsonDocument.Parse(overwriteOutput);
            Assert.That(overwriteDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            Assert.That(await File.ReadAllTextAsync(source), Is.EqualTo(Text));

            var (tooLarge, tooLargeOutput, _) = await RunAsync([], "compile-native", "consumer", source, oversized,
                "-o", output, "--runtime", "fail-fast", "--json");
            Assert.That(tooLarge, Is.EqualTo(1));
            using var tooLargeDocument = JsonDocument.Parse(tooLargeOutput);
            var diagnostic = tooLargeDocument.RootElement.GetProperty("diagnostics")[0];
            Assert.That(diagnostic.GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            Assert.That(diagnostic.GetProperty("message").GetString(), Does.Contain("16 MiB"));
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(original));
        }
        finally
        {
            File.Delete(source);
            File.Delete(oversized);
            File.Delete(output);
        }
    }

    [Test]
    public async Task NativePackageSourceFailuresRemainLocatedAndPreserveArtifactsAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-native-source-cli-{Guid.NewGuid():N}");
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
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(document.RootElement.GetProperty("command").GetString(), Is.EqualTo("compile-native"));
            var diagnostic = global::System.Linq.Enumerable.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.That(diagnostic.GetProperty("sourceIndex").GetInt32(), Is.EqualTo(1));
            Assert.That(await File.ReadAllBytesAsync(producer), Is.EqualTo(original));

            var (duplicate, duplicateOutput, _) = await RunAsync([], "compile-native", "producer", source, "Example",
                "--source", source, "--export", "Value", "-o", producer, "--runtime", "fail-fast", "--json");
            Assert.That(duplicate, Is.EqualTo(2));
            using var duplicateDocument = JsonDocument.Parse(duplicateOutput);
            Assert.That(duplicateDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
            Assert.That(await File.ReadAllBytesAsync(producer), Is.EqualTo(original));
        }
        finally
        {
            File.Delete(source);
            File.Delete(helper);
            File.Delete(producer);
        }
    }
    [TestCase("compile-native producer missing.terrum Example -o producer.trmn --runtime fail-fast --json")]
    [TestCase("compile-native producer missing.terrum Example --export Value -o producer.trmn --json")]
    [TestCase("compile-native consumer missing.terrum producer.trmn -o consumer.trmn --json")]
    [TestCase("compile-native consumer missing.terrum producer.trmn --export Value -o consumer.trmn --runtime fail-fast --json")]
    [TestCase("run-native consumer.trmn producer.trmn --json")]
    [TestCase("validate-native consumer.trmn producer.trmn --runtime fail-fast --json")]
    public async Task NativePackageCommandsRequireExplicitValidRolesAndRuntimeAsync(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var (exit, output, error) = await RunAsync([], arguments.Split(' '));
        Assert.That(exit, Is.EqualTo(2));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
    }
}
