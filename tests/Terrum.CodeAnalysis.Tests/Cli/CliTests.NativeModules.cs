// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task BuildCompilesAndRunsFlatArraySourceAsync(
        string runtimePolicy)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"terrum-fixed-array-build-{Guid.NewGuid():N}");
        var source = prefix + ".terrum";
        var output = prefix + ".exe";
        try
        {
            await File.WriteAllTextAsync(source,
                "int32 Main() { mut [uint32; 3] values = [10, 20, 30]; "
                + "values[1] = values[0] + 2; return int32(values[1]); }").ConfigureAwait(false);
            var (exitCode, json, error) = await RunAsync([], "build", source,
                "-o", output, "--runtime", runtimePolicy, "--json").ConfigureAwait(false);
            Assert.That(exitCode, Is.Zero, json + error);
            Assert.That(error, Is.Empty);
            using var result = JsonDocument.Parse(json);
            Assert.That(result.RootElement.GetProperty("success").GetBoolean(),
                Is.True);
            Assert.That(await ExecuteNativeImageAsync(output).ConfigureAwait(true),
                Is.EqualTo(12));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(source).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(output).ConfigureAwait(true);
        }
    }

    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task BuildLibraryEmitsFlatArrayExportAsync(
        string runtimePolicy)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"terrum-fixed-array-library-{Guid.NewGuid():N}");
        var source = prefix + ".terrum";
        var output = prefix + ".dll";
        try
        {
            await File.WriteAllTextAsync(source,
                "public int32 ReadArray() { [int32; 2] values = [20, 22]; "
                + "return values[1]; }").ConfigureAwait(false);
            var (exitCode, json, error) = await RunAsync([], "build-library",
                source, "--export", "ReadArray", "-o", output,
                "--runtime", runtimePolicy, "--json").ConfigureAwait(false);
            Assert.That(exitCode, Is.Zero, json + error);
            Assert.That(error, Is.Empty);
            using var result = JsonDocument.Parse(json);
            Assert.That(result.RootElement.GetProperty("success").GetBoolean(),
                Is.True);

            var library = NativeLibrary.Load(output);
            try
            {
                Assert.That(NativeLibrary.TryGetExport(library, "ReadArray",
                    out var entryPoint), Is.True);
                Assert.That(entryPoint, Is.Not.EqualTo(IntPtr.Zero));
            }
            finally
            {
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(source).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(output).ConfigureAwait(true);
        }
    }

    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task BuildNativeImportUsesBoundedFixedArrayElementAsync(
        string runtimePolicy)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"terrum-fixed-array-native-import-{Guid.NewGuid():N}");
        var source = prefix + ".terrum";
        var output = prefix + ".exe";
        var nativeManifest = prefix + ".native-core.json";
        try
        {
            await File.WriteAllTextAsync(nativeManifest,
                CreateNativeCoreManifest()).ConfigureAwait(false);
            var sourceText =
                "[Terrum.Interop.NativeImport(\"kernel32.dll\", \"lstrlenW\")]\n"
                + "[Terrum.Interop.NativeParameter(1, Read, NoEscape, 3)]\n"
                + "extern unsafe int32 Length(ptr<char16> value);\n"
                + "int32 Main() { [char16; 3] text = [char16(65), char16(66), char16(0)]; "
                + "unsafe { return Length(&text[0]); } }";
            await File.WriteAllTextAsync(source, sourceText).ConfigureAwait(false);
            var (exitCode, json, error) = await RunAsync([], "build-native-import",
                source, "--native-core-manifest", nativeManifest,
                "-o", output, "--runtime", runtimePolicy, "--json").ConfigureAwait(false);
            Assert.That(exitCode, Is.Zero, json + error);
            Assert.That(error, Is.Empty);
            using var result = JsonDocument.Parse(json);
            Assert.That(result.RootElement.GetProperty("success").GetBoolean(),
                Is.True);
            Assert.That(await ExecuteNativeImageAsync(output).ConfigureAwait(true),
                Is.EqualTo(2));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(source).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(output).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(nativeManifest)
                .ConfigureAwait(true);
        }
    }

    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task NativePackagesCompileAndRunFlatArrayProgramsAsync(
        string policy)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"terrum-native-array-cli-{Guid.NewGuid():N}");
        var producerSource = prefix + ".producer.terrum";
        var consumerSource = prefix + ".consumer.terrum";
        var producer = prefix + ".producer.trmn";
        var consumer = prefix + ".consumer.trmn";
        try
        {
            await File.WriteAllTextAsync(producerSource, """
                public int32 ReadArray() {
                    mut [uint32; 2] values = [10, 20];
                    values[1] = values[0] + 1;
                    unsafe {
                        ptr<uint32> selected = &values[1];
                        return int32(*selected);
                    }
                }
                """).ConfigureAwait(false);
            await File.WriteAllTextAsync(consumerSource, """
                int32 Main() {
                    mut [uint16; 1] local = [1];
                    return ReadArray() + int32(local[0]) - 12;
                }
                """).ConfigureAwait(false);

            var (producerExit, producerOutput, producerError) =
                await RunAsync([], "compile-native", "producer",
                    producerSource, "ArrayProvider", "--export",
                    "ReadArray", "-o", producer, "--runtime", policy,
                    "--json").ConfigureAwait(false);
            Assert.That(producerExit, Is.Zero, producerOutput);
            Assert.That(producerError, Is.Empty);

            var (consumerExit, consumerOutput, consumerError) =
                await RunAsync([], "compile-native", "consumer",
                    consumerSource, producer, "-o", consumer, "--runtime",
                    policy, "--json").ConfigureAwait(false);
            Assert.That(consumerExit, Is.Zero, consumerOutput);
            Assert.That(consumerError, Is.Empty);

            var (validationExit, validationOutput, validationError) =
                await RunAsync([], "validate-native", consumer, producer, "--json").ConfigureAwait(false);
            Assert.That(validationExit, Is.Zero, validationOutput + validationError);
            Assert.That(validationError, Is.Empty);
            using (var validation = JsonDocument.Parse(validationOutput))
            {
                Assert.That(validation.RootElement.GetProperty("success").GetBoolean(),
                    Is.True);
            }

            var (runExit, runOutput, runError) =
                await RunAsync([], "run-native", consumer, producer,
                    "--runtime", policy, "--json").ConfigureAwait(false);
            Assert.That(runExit, Is.Zero, runOutput + runError);
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
        finally
        {
            File.Delete(producerSource);
            File.Delete(consumerSource);
            File.Delete(producer);
            File.Delete(consumer);
        }
    }

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
            await File.WriteAllTextAsync(source, "public int32 Value() { return Helper(); }").ConfigureAwait(false);
            await File.WriteAllTextAsync(helper, "int32 Helper() { return 42; }").ConfigureAwait(false);
            await File.WriteAllTextAsync(entry, "int32 Main() { return Value(); }").ConfigureAwait(false);
            var (producerExit, producerOutput, producerError) = await RunAsync([], "compile-native", "producer",
                source, "Example", "--source", helper, "--export", "Value", "-o", producer, "--runtime", "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(producerExit, Is.EqualTo(0));
            Assert.That(producerError, Is.Empty);
            using var producerDocument = JsonDocument.Parse(producerOutput);
            Assert.That(producerDocument.RootElement.GetProperty("artifact").GetProperty("kind").GetString(), Is.EqualTo("native-producer"));
            Assert.That(producerDocument.RootElement.GetProperty("artifact").GetProperty("identity").GetString(), Is.EqualTo("Example"));
            Assert.That(producerDocument.RootElement.GetProperty("artifact").GetProperty("imageSha256").GetString()?.Length, Is.EqualTo(64));
            var originalProducer = await File.ReadAllBytesAsync(producer).ConfigureAwait(false);
            Assert.That(producerDocument.RootElement.GetProperty("artifact").GetProperty("length").GetInt32(), Is.EqualTo(originalProducer.Length));
            Assert.That(producerDocument.RootElement.GetProperty("artifact").GetProperty("sha256").GetString(), Is.EqualTo(Convert.ToHexString(SHA256.HashData(originalProducer))));

            var (consumerExit, _, consumerError) = await RunAsync([], "compile-native", "consumer", entry, producer,
                "-o", consumer, "--runtime", "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(consumerExit, Is.EqualTo(0));
            Assert.That(consumerError, Is.Empty);
            var originalConsumer = await File.ReadAllBytesAsync(consumer).ConfigureAwait(false);
            var (overwrite, overwriteOutput, _) = await RunAsync([], "compile-native", "consumer", entry, producer,
                "-o", producer, "--runtime", "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(overwrite, Is.EqualTo(1));
            using var overwriteDocument = JsonDocument.Parse(overwriteOutput);
            Assert.That(overwriteDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(producer).ConfigureAwait(false), Is.EqualTo(originalProducer));
            var (first, firstOutput, firstError) = await RunAsync([], "run-native", consumer, producer, "--runtime", "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(first, Is.EqualTo(42));
            Assert.That(firstOutput, Is.Empty);
            Assert.That(firstError, Is.Empty);

            await File.WriteAllTextAsync(helper, "int32 Helper() { return 43; }").ConfigureAwait(false);
            var (replacementExit, _, _) = await RunAsync([], "compile-native", "producer", source, "Example",
                "--source", helper, "--export", "Value", "-o", replacement, "--runtime", "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(replacementExit, Is.EqualTo(0));
            var (validation, validationOutput, _) = await RunAsync([], "validate-native", consumer, replacement, "--json").ConfigureAwait(false);
            Assert.That(validation, Is.EqualTo(0));
            using var validationDocument = JsonDocument.Parse(validationOutput);
            Assert.That(validationDocument.RootElement.GetProperty("success").GetBoolean(), Is.True);
            File.Delete(source);
            File.Delete(helper);
            File.Delete(entry);
            var (second, secondOutput, secondError) = await RunAsync([], "run-native", consumer, replacement, "--runtime", "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(second, Is.EqualTo(43));
            Assert.That(secondOutput, Is.Empty);
            Assert.That(secondError, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(consumer).ConfigureAwait(false), Is.EqualTo(originalConsumer));

            await File.WriteAllTextAsync(source, "public bool Value() { require false; return true; }").ConfigureAwait(false);
            Assert.That((await RunAsync([], "compile-native", "producer", source, "Example", "--export", "Value",
                "-o", replacement, "--runtime", "fail-fast", "--json").ConfigureAwait(false)).ExitCode, Is.EqualTo(0));
            var (rejected, rejectedOutput, _) = await RunAsync([], "run-native", consumer, replacement, "--runtime", "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(rejected, Is.EqualTo(1));
            using var rejectedDocument = JsonDocument.Parse(rejectedOutput);
            Assert.That(rejectedDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5002"));
            Assert.That(await File.ReadAllBytesAsync(consumer).ConfigureAwait(false), Is.EqualTo(originalConsumer));

            var damaged = await File.ReadAllBytesAsync(producer).ConfigureAwait(false);
            damaged[^1] ^= 1;
            await File.WriteAllBytesAsync(producer, damaged).ConfigureAwait(false);
            await File.WriteAllTextAsync(entry, "int32 Main() { return Value(); }").ConfigureAwait(false);
            var (corrupt, corruptOutput, _) = await RunAsync([], "compile-native", "consumer", entry, producer,
                "-o", consumer, "--runtime", "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(corrupt, Is.EqualTo(1));
            using var corruptDocument = JsonDocument.Parse(corruptOutput);
            Assert.That(corruptDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(consumer).ConfigureAwait(false), Is.EqualTo(originalConsumer));
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
            await File.WriteAllTextAsync(source, Text).ConfigureAwait(false);
            await File.WriteAllBytesAsync(output, original).ConfigureAwait(false);
            using (var stream = File.Create(oversized))
            {
                stream.SetLength((16 * 1024 * 1024) + 1);
            }
            var (overwrite, overwriteOutput, _) = await RunAsync([], "compile-native", "producer", source, "Example",
                "--export", "Value", "-o", source, "--runtime", "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(overwrite, Is.EqualTo(1));
            using var overwriteDocument = JsonDocument.Parse(overwriteOutput);
            Assert.That(overwriteDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            Assert.That(await File.ReadAllTextAsync(source).ConfigureAwait(false), Is.EqualTo(Text));

            var (tooLarge, tooLargeOutput, _) = await RunAsync([], "compile-native", "consumer", source, oversized,
                "-o", output, "--runtime", "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(tooLarge, Is.EqualTo(1));
            using var tooLargeDocument = JsonDocument.Parse(tooLargeOutput);
            var diagnostic = tooLargeDocument.RootElement.GetProperty("diagnostics")[0];
            Assert.That(diagnostic.GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            Assert.That(diagnostic.GetProperty("message").GetString(), Does.Contain("16 MiB"));
            Assert.That(await File.ReadAllBytesAsync(output).ConfigureAwait(false), Is.EqualTo(original));
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
            await File.WriteAllTextAsync(source, "public int32 Value() { return Helper(); }").ConfigureAwait(false);
            await File.WriteAllTextAsync(helper, "int32 Helper() { return missing; }").ConfigureAwait(false);
            await File.WriteAllBytesAsync(producer, original).ConfigureAwait(false);
            var (exit, output, error) = await RunAsync([], "compile-native", "producer", source, "Example", "--source", helper,
                "--export", "Value", "-o", producer, "--runtime", "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(document.RootElement.GetProperty("command").GetString(), Is.EqualTo("compile-native"));
            var diagnostic = global::System.Linq.Enumerable.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.That(diagnostic.GetProperty("sourceIndex").GetInt32(), Is.EqualTo(1));
            Assert.That(await File.ReadAllBytesAsync(producer).ConfigureAwait(false), Is.EqualTo(original));

            var (duplicate, duplicateOutput, _) = await RunAsync([], "compile-native", "producer", source, "Example",
                "--source", source, "--export", "Value", "-o", producer, "--runtime", "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(duplicate, Is.EqualTo(2));
            using var duplicateDocument = JsonDocument.Parse(duplicateOutput);
            Assert.That(duplicateDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
            Assert.That(await File.ReadAllBytesAsync(producer).ConfigureAwait(false), Is.EqualTo(original));
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
        var (exit, output, error) = await RunAsync([], arguments.Split(' ')).ConfigureAwait(false);
        Assert.That(exit, Is.EqualTo(2));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
    }
}
