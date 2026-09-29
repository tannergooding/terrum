// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Fact]
    public async Task CoreLibrarySwitchGatesPrimitiveDeclarationAndEmitsApiAsync()
    {
        const string Source = """
            public struct int32 {
                private int32 value;
                public static int32 AddOne(int32 input) { return input + 1; }
            }
            """;
        var path = Path.Combine(Path.GetTempPath(), $"terrum-corelib-{Guid.NewGuid():N}.dll");
        try
        {
            var (rejected, rejectedOutput, _) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "analyze", "-", "--json");
            Assert.Equal(1, rejected);
            using var rejectedDocument = JsonDocument.Parse(rejectedOutput);
            Assert.Equal("TRM3003",
                rejectedDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());

            var (analyzed, _, _) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "analyze", "-", "--corelib", "--json");
            Assert.Equal(0, analyzed);

            var (built, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "build-library", "-", "--export", "int32.AddOne", "-o", path,
                "--runtime", "fail-fast", "--corelib", "--json");
            Assert.True(built == 0, output + error);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("dynamic-library",
                document.RootElement.GetProperty("artifact").GetProperty("kind").GetString());
            Assert.True(File.Exists(path));
            Assert.NotEmpty(await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CoreLibraryModuleSwitchPersistsPrimitiveApiAcrossSeparateCompilationAsync()
    {
        var directory = Directory.CreateTempSubdirectory("terrum-core-module-").FullName;
        var source = Path.Combine(directory, "core.terrum");
        var caller = Path.Combine(directory, "caller.terrum");
        var provider = Path.Combine(directory, "core.trm");
        var consumer = Path.Combine(directory, "caller.trm");
        try
        {
            await File.WriteAllTextAsync(source, """
                public struct int32 {
                    private int32 value;
                    public static int32 AddOne(int32 input) { return input + 1; }
                    public int32 Read() { return value; }
                }
                """);
            await File.WriteAllTextAsync(caller,
                "int32 Main() { return int32.AddOne(5).Read(); }");

            var (withoutSwitch, _, _) = await RunAsync([],
                "compile-module", "producer", source, "int32.AddOne",
                "--export", "int32.Read", "-o", provider, "--json");
            Assert.Equal(1, withoutSwitch);

            var (produced, producerOutput, producerError) = await RunAsync([],
                "compile-module", "producer", source, "int32.AddOne",
                "--export", "int32.Read", "-o", provider, "--corelib", "--json");
            Assert.True(produced == 0, producerOutput + producerError);

            var (consumed, consumerOutput, consumerError) = await RunAsync([],
                "compile-module", "consumer", caller, provider, "-o", consumer, "--json");
            Assert.True(consumed == 0, consumerOutput + consumerError);

            var (executed, _, _) = await RunAsync([],
                "run-module", consumer, provider, "--runtime", "fail-fast");
            Assert.Equal(6, executed);

            var (consumerSwitch, _, _) = await RunAsync([],
                "compile-module", "consumer", caller, provider, "-o", consumer, "--corelib", "--json");
            Assert.Equal(2, consumerSwitch);
        }
        finally
        {
            File.Delete(source);
            File.Delete(caller);
            File.Delete(provider);
            File.Delete(consumer);
            Directory.Delete(directory);
        }
    }
}
