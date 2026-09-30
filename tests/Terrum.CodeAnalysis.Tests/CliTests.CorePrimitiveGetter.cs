// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
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
            Assert.True(produced == 0, output + error);
            var (consumed, consumerOutput, consumerError) = await RunAsync([],
                "compile-module", "consumer", caller, provider, "-o", consumer,
                "--runtime", policy, "--json");
            Assert.True(consumed == 0, consumerOutput + consumerError);
            var (executed, _, _) = await RunAsync([],
                "run-module", consumer, provider, "--runtime", policy);
            Assert.Equal(7, executed);

            var (nativeProduced, nativeOutput, nativeError) = await RunAsync([],
                "compile-native", "producer", source, "CoreGetter",
                "--export", "int32.Identity", "--export", "int32.#get:Value",
                "-o", nativeProvider, "--runtime", policy, "--corelib", "--json");
            Assert.True(nativeProduced == 0, nativeOutput + nativeError);
            var (nativeConsumed, nativeConsumerOutput, nativeConsumerError) = await RunAsync([],
                "compile-native", "consumer", caller, nativeProvider, "-o", nativeConsumer,
                "--runtime", policy, "--json");
            Assert.True(nativeConsumed == 0, nativeConsumerOutput + nativeConsumerError);
            var (nativeExecuted, _, _) = await RunAsync([],
                "run-native", nativeConsumer, nativeProvider, "--runtime", policy);
            Assert.Equal(7, nativeExecuted);

            await File.WriteAllTextAsync(source,
                Original.Replace("get { return value; }", "get { return 8; }",
                    StringComparison.Ordinal));
            (produced, output, error) = await RunAsync([],
                "compile-module", "producer", source, "int32.Identity",
                "--export", "int32.#get:Value", "-o", provider,
                "--runtime", policy, "--corelib", "--json");
            Assert.True(produced == 0, output + error);
            (executed, _, _) = await RunAsync([],
                "run-module", consumer, provider, "--runtime", policy);
            Assert.Equal(8, executed);

            (nativeProduced, nativeOutput, nativeError) = await RunAsync([],
                "compile-native", "producer", source, "CoreGetter",
                "--export", "int32.Identity", "--export", "int32.#get:Value",
                "-o", nativeProvider, "--runtime", policy, "--corelib", "--json");
            Assert.True(nativeProduced == 0, nativeOutput + nativeError);
            (nativeExecuted, _, _) = await RunAsync([],
                "run-native", nativeConsumer, nativeProvider, "--runtime", policy);
            Assert.Equal(8, nativeExecuted);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
