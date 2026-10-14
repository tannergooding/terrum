// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task CorePrimitiveGetterSurvivesPersistedModuleAndNativeReplacementAsync(string policy)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"core-getter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "core.terrum");
        var caller = Path.Combine(directory, "caller.terrum");
        var provider = Path.Combine(directory, "core.trm");
        var consumer = Path.Combine(directory, "caller.trm");
        var nativeProvider = Path.Combine(directory, "core.trmn");
        var nativeConsumer = Path.Combine(directory, "caller.trmn");
        const string Original = """
            public struct int32 {
                private int32 value;
                public static int32 Identity(int32 input) { return input.value; }
                public int32 Value { [NoThrow] get { return value; } }
            }
            """;
        try
        {
            await File.WriteAllTextAsync(source, Original);
            await File.WriteAllTextAsync(caller,
                "int32 Main() { return int32.Identity(7).Value; }");
            var (produced, output, error) = await RunAsync([],
                "compile-module", "producer", source, "int32.Identity",
                "--export", "int32.#get:Value", "-o", provider,
                "--runtime", policy, "--corelib", "--json");
            Assert.That(produced == 0, Is.True, output + error);
            var (consumed, consumerOutput, consumerError) = await RunAsync([],
                "compile-module", "consumer", caller, provider, "-o", consumer,
                "--runtime", policy, "--json");
            Assert.That(consumed == 0, Is.True, consumerOutput + consumerError);
            var (executed, _, _) = await RunAsync([],
                "run-module", consumer, provider, "--runtime", policy);
            Assert.That(executed, Is.EqualTo(7));

            var (nativeProduced, nativeOutput, nativeError) = await RunAsync([],
                "compile-native", "producer", source, "CoreGetter",
                "--export", "int32.Identity", "--export", "int32.#get:Value",
                "-o", nativeProvider, "--runtime", policy, "--corelib", "--json");
            Assert.That(nativeProduced == 0, Is.True, nativeOutput + nativeError);
            var (nativeConsumed, nativeConsumerOutput, nativeConsumerError) = await RunAsync([],
                "compile-native", "consumer", caller, nativeProvider, "-o", nativeConsumer,
                "--runtime", policy, "--json");
            Assert.That(nativeConsumed == 0, Is.True, nativeConsumerOutput + nativeConsumerError);
            var (nativeExecuted, _, _) = await RunAsync([],
                "run-native", nativeConsumer, nativeProvider, "--runtime", policy);
            Assert.That(nativeExecuted, Is.EqualTo(7));

            await File.WriteAllTextAsync(source,
                Original.Replace("get { return value; }", "get { return 8; }",
                    StringComparison.Ordinal));
            (produced, output, error) = await RunAsync([],
                "compile-module", "producer", source, "int32.Identity",
                "--export", "int32.#get:Value", "-o", provider,
                "--runtime", policy, "--corelib", "--json");
            Assert.That(produced == 0, Is.True, output + error);
            (executed, _, _) = await RunAsync([],
                "run-module", consumer, provider, "--runtime", policy);
            Assert.That(executed, Is.EqualTo(8));

            (nativeProduced, nativeOutput, nativeError) = await RunAsync([],
                "compile-native", "producer", source, "CoreGetter",
                "--export", "int32.Identity", "--export", "int32.#get:Value",
                "-o", nativeProvider, "--runtime", policy, "--corelib", "--json");
            Assert.That(nativeProduced == 0, Is.True, nativeOutput + nativeError);
            (nativeExecuted, _, _) = await RunAsync([],
                "run-native", nativeConsumer, nativeProvider, "--runtime", policy);
            Assert.That(nativeExecuted, Is.EqualTo(8));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
