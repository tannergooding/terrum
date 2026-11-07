// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    [Test]
    public async Task CoreLibrarySwitchGatesPrimitiveDeclarationAndEmitsApiAsync()
    {
        const string Source = """
            public struct int32 {
                private int32 value;
                public static int32 AddOne(int32 input) { return input + 1; }
            }
            """;
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-corelib-{Guid.NewGuid():N}.dll");
        try
        {
            var (rejected, rejectedOutput, _) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "analyze", "-", "--json").ConfigureAwait(false);
            Assert.That(rejected, Is.EqualTo(1));
            using var rejectedDocument = JsonDocument.Parse(rejectedOutput);
            Assert.That(rejectedDocument.RootElement.GetProperty("diagnostics").EnumerateArray()
                .Any(static diagnostic => diagnostic.GetProperty("code").GetString() == "TRM3003"), Is.True);

            var (analyzed, _, _) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "analyze", "-", "--corelib", "--json").ConfigureAwait(false);
            Assert.That(analyzed, Is.EqualTo(0));

            var (built, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "build-library", "-", "--export", "int32.AddOne", "-o", path,
                "--runtime", "fail-fast", "--corelib", "--json").ConfigureAwait(false);
            Assert.That(built == 0, Is.True, output + error);
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("artifact").GetProperty("kind").GetString(), Is.EqualTo("dynamic-library"));
            Assert.That(File.Exists(path), Is.True);
            Assert.That(await File.ReadAllBytesAsync(path).ConfigureAwait(false), Is.Not.Empty);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task CoreLibraryModuleSwitchPersistsPrimitiveApiAcrossSeparateCompilationAsync(string policy)
    {
        var directory = TestArtifactPaths.CreateDirectory("terrum-core-module-");
        var source = Path.Combine(directory, "core.terrum");
        var additionalSource = Path.Combine(directory, "unsigned.terrum");
        var caller = Path.Combine(directory, "caller.terrum");
        var provider = Path.Combine(directory, "core.trm");
        var consumer = Path.Combine(directory, "caller.trm");
        var nativeProvider = Path.Combine(directory, "core.trmn");
        var nativeConsumer = Path.Combine(directory, "caller.trmn");
        try
        {
            await File.WriteAllTextAsync(source, """
                public struct int32 {
                    private int32 value;
                    public static int32 AddOne(int32 input) { return input + 1; }
                    public int32 Read() { return value; }
                }
                """).ConfigureAwait(false);
            await File.WriteAllTextAsync(additionalSource, """
                public struct uint32 {
                    private uint32 value;
                    public static uint32 Identity(uint32 input) { return input.value; }
                }
                """).ConfigureAwait(false);
            await File.WriteAllTextAsync(caller,
                "int32 Main() { return int32.AddOne(5).Read() + int32(uint32.Identity(uint32(1))); }").ConfigureAwait(false);

            var (withoutSwitch, _, _) = await RunAsync([],
                "compile-module", "producer", source, "int32.AddOne",
                "--export", "int32.Read", "-o", provider, "--json").ConfigureAwait(false);
            Assert.That(withoutSwitch, Is.EqualTo(1));

            var (produced, producerOutput, producerError) = await RunAsync([],
                "compile-module", "producer", source, "int32.AddOne",
                "--source", additionalSource, "--export", "int32.Read",
                "--export", "uint32.Identity", "-o", provider, "--runtime", policy,
                "--corelib", "--json").ConfigureAwait(false);
            Assert.That(produced == 0, Is.True, producerOutput + producerError);

            var (consumed, consumerOutput, consumerError) = await RunAsync([],
                "compile-module", "consumer", caller, provider, "-o", consumer,
                "--runtime", policy, "--json").ConfigureAwait(false);
            Assert.That(consumed == 0, Is.True, consumerOutput + consumerError);

            var (executed, _, _) = await RunAsync([],
                "run-module", consumer, provider, "--runtime", policy).ConfigureAwait(false);
            Assert.That(executed, Is.EqualTo(7));

            var (consumerSwitch, _, _) = await RunAsync([],
                "compile-module", "consumer", caller, provider, "-o", consumer, "--corelib", "--json").ConfigureAwait(false);
            Assert.That(consumerSwitch, Is.EqualTo(2));

            var (nativeProduced, nativeOutput, nativeError) = await RunAsync([],
                "compile-native", "producer", source, "CorePrimitive",
                "--source", additionalSource, "--export", "int32.AddOne",
                "--export", "int32.Read", "--export", "uint32.Identity",
                "-o", nativeProvider, "--runtime", policy, "--corelib", "--json").ConfigureAwait(false);
            Assert.That(nativeProduced == 0, Is.True, nativeOutput + nativeError);

            var (nativeConsumed, nativeConsumerOutput, nativeConsumerError) = await RunAsync([],
                "compile-native", "consumer", caller, nativeProvider, "-o", nativeConsumer,
                "--runtime", policy, "--json").ConfigureAwait(false);
            Assert.That(nativeConsumed == 0, Is.True, nativeConsumerOutput + nativeConsumerError);

            var (validated, validationOutput, validationError) = await RunAsync([],
                "validate-native", nativeConsumer, nativeProvider, "--json").ConfigureAwait(false);
            Assert.That(validated == 0, Is.True, validationOutput + validationError);

            var (nativeExecuted, _, _) = await RunAsync([],
                "run-native", nativeConsumer, nativeProvider, "--runtime", policy).ConfigureAwait(false);
            Assert.That(nativeExecuted, Is.EqualTo(7));

            if (policy == "checked-exceptions")
            {
                await File.WriteAllTextAsync(caller, """
                    int32 Main() {
                        unsafe {
                            try { return int32.AddOne(2147483647); }
                            catch { return 42; }
                        }
                    }
                    """).ConfigureAwait(false);
                var (checkedConsumer, checkedOutput, checkedError) = await RunAsync([],
                    "compile-module", "consumer", caller, provider, "-o", consumer,
                    "--runtime", policy, "--json").ConfigureAwait(false);
                Assert.That(checkedConsumer == 0, Is.True, checkedOutput + checkedError);
                var (checkedResult, _, _) = await RunAsync([],
                    "run-module", consumer, provider, "--runtime", policy).ConfigureAwait(false);
                Assert.That(checkedResult, Is.EqualTo(42));

                var (checkedNativeConsumer, nativeCheckedOutput, nativeCheckedError) =
                    await RunAsync([], "compile-native", "consumer", caller, nativeProvider,
                        "-o", nativeConsumer, "--runtime", policy, "--json").ConfigureAwait(false);
                Assert.That(checkedNativeConsumer == 0, Is.True, nativeCheckedOutput + nativeCheckedError);
                var (nativeCheckedResult, _, _) = await RunAsync([],
                    "run-native", nativeConsumer, nativeProvider, "--runtime", policy).ConfigureAwait(false);
                Assert.That(nativeCheckedResult, Is.EqualTo(42));
            }

            var (nativeConsumerSwitch, _, _) = await RunAsync([],
                "compile-native", "consumer", caller, nativeProvider, "-o", nativeConsumer,
                "--runtime", policy, "--corelib", "--json").ConfigureAwait(false);
            Assert.That(nativeConsumerSwitch, Is.EqualTo(2));
        }
        finally
        {
            File.Delete(source);
            File.Delete(additionalSource);
            File.Delete(caller);
            File.Delete(provider);
            File.Delete(consumer);
            File.Delete(nativeProvider);
            File.Delete(nativeConsumer);
            Directory.Delete(directory);
        }
    }
}
