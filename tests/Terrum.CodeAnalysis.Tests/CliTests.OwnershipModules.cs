// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("fail-fast", true, false, "native-12")]
    [InlineData("checked-exceptions", true, false, "native-12")]
    [InlineData("fail-fast", false, false, "native-10")]
    [InlineData("checked-exceptions", false, false, "native-11")]
    [InlineData("fail-fast", true, true, "native-13")]
    [InlineData("checked-exceptions", true, true, "native-13")]
    public async Task ModuleCommandsReportThePersistedOwnershipProfileAsync(
        string policy, bool exportOwner, bool transferOwner, string profile)
    {
        var directory = Directory.CreateTempSubdirectory("terrum-owner-profile-").FullName;
        var source = Path.Combine(directory, "provider.terrum");
        var consumerSource = Path.Combine(directory, "consumer.terrum");
        var provider = Path.Combine(directory, "provider.trm");
        var consumer = Path.Combine(directory, "consumer.trm");
        var linked = Path.Combine(directory, "linked.exe");
        var nativeProvider = Path.Combine(directory, "provider.trmn");
        var nativeConsumer = Path.Combine(directory, "consumer.trmn");
        try
        {
            await File.WriteAllTextAsync(source, """
                public unique struct Token {
                    public int32 Id;
                    public Token() { Id = 42; }
                    [NoThrow] [Cleanup] public void Close() {}
                }
                public Token Make() { return new Token(); }
                public int32 Take(Token token) { return token.Id; }
                public int32 Value() { return 42; }
                """);
            await File.WriteAllTextAsync(consumerSource, transferOwner
                ? "int32 Main() { Token token = Make(); return Take(move token); }"
                : exportOwner ? "int32 Main() { Token token = Make(); return token.Id; }"
                    : "int32 Main() { return Value(); }");
            string[] exports = transferOwner ? ["--export", "Token.Close", "--export", "Take"]
                : exportOwner ? ["--export", "Token.Close"] : [];
            var name = exportOwner ? "Make" : "Value";
            await AssertModuleProfileAsync(profile, policy,
                ["compile-module", "producer", source, name, .. exports, "-o", provider, "--runtime", policy, "--json"]);
            await AssertModuleProfileAsync(profile, policy,
                ["compile-module", "consumer", consumerSource, provider, "-o", consumer, "--runtime", policy, "--json"]);
            await AssertModuleProfileAsync(profile, policy,
                ["link-module", consumer, provider, "-o", linked, "--runtime", policy, "--json"]);
            await AssertModuleProfileAsync(profile, policy,
                ["compile-native", "producer", source, "OwnershipProfile", "--export", name, .. exports,
                    "-o", nativeProvider, "--runtime", policy, "--json"]);
            await AssertModuleProfileAsync(profile, policy,
                ["compile-native", "consumer", consumerSource, nativeProvider, "-o", nativeConsumer,
                    "--runtime", policy, "--json"]);
            await AssertModuleProfileAsync(profile, policy, ["validate-native", nativeConsumer, nativeProvider, "--json"]);
            var bytes = await File.ReadAllBytesAsync(nativeConsumer);
            Assert.Equal(profile, NativeModuleCompiler.Inspect(bytes).NativeProfileVersion);
            AssertMismatchedNativePolicyRejected(bytes);
        }
        finally
        {
            foreach (var path in new[] { source, consumerSource, provider, consumer, linked, nativeProvider, nativeConsumer })
            {
                File.Delete(path);
            }
            Directory.Delete(directory);
        }
    }

    private static void AssertMismatchedNativePolicyRejected(byte[] bytes)
    {
        var contract = 18 + bytes[7] + bytes[8];
        if (bytes[4] == 2)
        {
            var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(contract, 2));
            contract += 2;
            for (var index = 0; index < count; index++)
            {
                var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(contract, 2));
                contract += 2 + length;
            }
        }
        Assert.InRange(contract, 18, bytes.Length - 32 - 1);
        var profileLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(contract + 8, 4));
        var policy = contract + 12 + profileLength + 1;
        Assert.InRange(policy, contract, contract
            + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10, 4)) - 1);
        bytes[policy] ^= 1;
        SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(bytes));
    }

    private static async Task AssertModuleProfileAsync(string expected, string policy, string[] arguments)
    {
        var (exit, output, error) = await RunAsync([], arguments).ConfigureAwait(true);
        Assert.True(exit == 0, output + Environment.NewLine + error);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(expected, document.RootElement.GetProperty("profile").GetString());
        Assert.Equal(policy, document.RootElement.GetProperty("runtimePolicy").GetString());
    }
}
