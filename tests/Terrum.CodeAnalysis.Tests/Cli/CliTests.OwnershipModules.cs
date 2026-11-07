// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    [TestCase("fail-fast",true,false,"native-12")]
    [TestCase("checked-exceptions",true,false,"native-12")]
    [TestCase("fail-fast",false,false,"native-10")]
    [TestCase("checked-exceptions",false,false,"native-11")]
    [TestCase("fail-fast",true,true,"native-13")]
    [TestCase("checked-exceptions",true,true,"native-13")]
    public async Task ModuleCommandsReportPersistedProfileAndRequireNativeLayoutSelectionAsync(
        string policy, bool exportOwner, bool transferOwner, string profile)
    {
        var directory = TestArtifactPaths.CreateDirectory("terrum-owner-profile-");
        var layoutManifest = Path.Combine(RepositoryRoot(), "tests", "Fixtures",
            "LayoutCore", "build-layout-core-manifest.json");
        var source = Path.Combine(directory, "provider.terrum");
        var consumerSource = Path.Combine(directory, "consumer.terrum");
        var provider = Path.Combine(directory, "provider.trm");
        var consumer = Path.Combine(directory, "consumer.trm");
        var linked = Path.Combine(directory, "linked.exe");
        var nativeProvider = Path.Combine(directory, "provider.trmn");
        try
        {
            await File.WriteAllTextAsync(source, """
                [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Token {
                    public int32 Id;
                    public Token() { Id = 42; }
                     [Cleanup] public void Close() {}
                }
                public Token Make() { return new Token(); }
                public int32 Take(Token token) { return token.Id; }
                public int32 Value() { return 42; }
                """).ConfigureAwait(false);
            await File.WriteAllTextAsync(consumerSource, transferOwner
                ? "int32 Main() { Token token = Make(); return Take(move token); }"
                : exportOwner ? "int32 Main() { Token token = Make(); return token.Id; }"
                    : "int32 Main() { return Value(); }").ConfigureAwait(false);
            string[] exports = transferOwner ? ["--export", "Token.Close", "--export", "Take"]
                : exportOwner ? ["--export", "Token.Close"] : [];
            var name = exportOwner ? "Make" : "Value";
            await AssertModuleProfileAsync(profile, policy,
                ["compile-module", "producer", source, name, .. exports, "-o",
                    provider, "--runtime", policy, "--layout-core-manifest",
                    layoutManifest, "--json"]).ConfigureAwait(false);
            await AssertModuleProfileAsync(profile, policy,
                ["compile-module", "consumer", consumerSource, provider, "-o",
                    consumer, "--runtime", policy, "--layout-core-manifest",
                    layoutManifest, "--json"]).ConfigureAwait(false);
            await AssertModuleProfileAsync(profile, policy,
                ["link-module", consumer, provider, "-o", linked, "--runtime", policy, "--json"]).ConfigureAwait(false);
            var nativeSentinel = "existing native package"u8.ToArray();
            await File.WriteAllBytesAsync(nativeProvider, nativeSentinel)
                .ConfigureAwait(false);
            var (nativeExit, nativeOutput, nativeError) = await RunAsync([], [
                "compile-native", "producer", source, "OwnershipProfile",
                "--export", name, .. exports, "-o", nativeProvider,
                "--runtime", policy, "--json",
            ]).ConfigureAwait(false);
            Assert.That(nativeExit, Is.EqualTo(1));
            Assert.That(nativeError, Is.Empty);
            using (var document = JsonDocument.Parse(nativeOutput))
            {
                var diagnostics = document.RootElement.GetProperty("diagnostics");
                Assert.That(document.RootElement.GetProperty("success")
                    .GetBoolean(), Is.False);
                Assert.That(diagnostics[0].GetProperty("code").GetString(),
                    Is.EqualTo("TRM3005"));
            }
            Assert.That(await File.ReadAllBytesAsync(nativeProvider)
                    .ConfigureAwait(false),
                Is.EqualTo(nativeSentinel));

            var (unselectedExit, unselectedOutput, unselectedError)
                = await RunAsync([], [
                    "compile-native", "producer", source,
                    "OwnershipProfile", "--export", name, .. exports, "-o",
                    nativeProvider, "--runtime", policy,
                    "--layout-core-manifest", layoutManifest, "--json",
                ]).ConfigureAwait(false);
            Assert.That(unselectedExit, Is.EqualTo(2));
            Assert.That(unselectedError, Is.Empty);
            using (var document = JsonDocument.Parse(unselectedOutput))
            {
                Assert.That(document.RootElement.GetProperty("success")
                    .GetBoolean(), Is.False);
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
            }
            Assert.That(await File.ReadAllBytesAsync(nativeProvider)
                    .ConfigureAwait(false),
                Is.EqualTo(nativeSentinel));
        }
        finally
        {
            foreach (var path in new[] { source, consumerSource, provider, consumer, linked, nativeProvider })
            {
                File.Delete(path);
            }
            Directory.Delete(directory);
        }
    }

    private static async Task AssertModuleProfileAsync(string expected, string policy, string[] arguments)
    {
        var (exit, output, error) = await RunAsync([], arguments).ConfigureAwait(true);
        Assert.That(exit == 0, Is.True, output + Environment.NewLine + error);
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("profile").GetString(), Is.EqualTo(expected));
        Assert.That(document.RootElement.GetProperty("runtimePolicy").GetString(), Is.EqualTo(policy));
    }
}
