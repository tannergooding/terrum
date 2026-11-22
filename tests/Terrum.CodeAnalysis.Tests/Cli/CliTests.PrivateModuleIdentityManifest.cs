// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task CompileModuleIdentityManifestBindsOrderedFileInputsAsync(
        string policy)
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-private-identity-manifest-");
        var primarySource = Path.Combine(directory, "primary.terrum");
        var additionalSource = Path.Combine(directory, "additional.terrum");
        var consumerSource = Path.Combine(directory, "consumer.terrum");
        var producerManifest = Path.Combine(directory, "producer.json");
        var consumerManifest = Path.Combine(directory, "consumer.json");
        var producerArtifact = Path.Combine(directory, "producer.trm");
        var consumerArtifact = Path.Combine(directory, "consumer.trm");
        var mismatchedOutput = Path.Combine(directory, "mismatched.trm");
        var primaryBytes = Encoding.UTF8.GetBytes(
            "public struct Cell { private int32 Value; } "
            + "public enum Shade : int32 { Red = 0, Blue = 1 } "
            + "public int32 AddOne(int32 value) { return value + 1; }");
        var additionalBytes = Encoding.UTF8.GetBytes(
            "public int32 Twice(int32 value) { return value * 2; }");
        var consumerBytes = Encoding.UTF8.GetBytes(
            "public int32 ReadCell(Cell value) { return 0; } "
            + "public Shade CopyShade(Shade value) { return value; } "
            + "public int32 Main() { return AddOne(Twice(20)); }");
        try
        {
            await File.WriteAllBytesAsync(primarySource, primaryBytes)
                .ConfigureAwait(false);
            await File.WriteAllBytesAsync(additionalSource,
                additionalBytes).ConfigureAwait(false);
            await File.WriteAllBytesAsync(consumerSource, consumerBytes)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(producerManifest,
                CreateManifest("ManifestProvider", [
                    Digest(primaryBytes), Digest(additionalBytes),
                ], null, [
                    new {
                        kind = "struct",
                        qualifiedName = "Cell",
                        localDeclarationKey = "types.cell",
                        isExported = true,
                    },
                ], [], [
                    new {
                        qualifiedName = "Shade",
                        localDeclarationKey = "types.shade",
                        isExported = true,
                        memberDeclarationKeys = new[] {
                            "members.shade.red", "members.shade.blue",
                        },
                    },
                ])).ConfigureAwait(false);

            var (producerExit, producerOutput, producerError)
                = await RunAsync([], [
                    "compile-module", "producer", primarySource, "AddOne",
                    "--source", additionalSource, "--export", "Twice",
                    "-o", producerArtifact, "--runtime", policy,
                    "--private-identity-manifest", producerManifest,
                    "--json",
                ]).ConfigureAwait(false);
            Assert.That(producerExit, Is.EqualTo(0),
                producerOutput + producerError);
            Assert.That(producerError, Is.Empty);
            using (var document = JsonDocument.Parse(producerOutput))
            {
                Assert.That(document.RootElement.GetProperty("success")
                    .GetBoolean(), Is.True);
                Assert.That(document.RootElement.GetProperty("runtimePolicy")
                    .GetString(), Is.EqualTo(policy));
            }

            var producerBytes = await File.ReadAllBytesAsync(producerArtifact)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(consumerManifest,
                CreateManifest("ManifestConsumer", [Digest(consumerBytes)],
                    Digest(producerBytes), [], [
                        new {
                            kind = "struct",
                            qualifiedName = "Cell",
                            localDeclarationKey = "types.cell.consumer",
                            selectedModuleIdentity = "ManifestProvider",
                            selectedExportKey = "types.cell",
                        },
                        new {
                            kind = "enum",
                            qualifiedName = "Shade",
                            localDeclarationKey = "types.shade.consumer",
                            selectedModuleIdentity = "ManifestProvider",
                            selectedExportKey = "types.shade",
                        },
                    ], [])).ConfigureAwait(false);
            var (consumerExit, consumerOutput, consumerError)
                = await RunAsync([], [
                    "compile-module", "consumer", consumerSource,
                    producerArtifact, "-o", consumerArtifact, "--runtime",
                    policy, "--private-identity-manifest", consumerManifest,
                    "--json",
                ]).ConfigureAwait(false);
            Assert.That(consumerExit, Is.EqualTo(0),
                consumerOutput + consumerError);
            Assert.That(consumerError, Is.Empty);
            using (var document = JsonDocument.Parse(consumerOutput))
            {
                Assert.That(document.RootElement.GetProperty("success")
                    .GetBoolean(), Is.True);
                Assert.That(document.RootElement.GetProperty("runtimePolicy")
                    .GetString(), Is.EqualTo(policy));
            }

            var (runExit, _, runError) = await RunAsync([], [
                "run-module", consumerArtifact, producerArtifact,
                "--runtime", policy,
            ]).ConfigureAwait(false);
            Assert.That(runExit, Is.EqualTo(41), runError);

            var sentinel = "preserve existing artifact"u8.ToArray();
            await File.WriteAllBytesAsync(mismatchedOutput, sentinel)
                .ConfigureAwait(false);
            var (mismatchExit, mismatchOutput, mismatchError)
                = await RunAsync([], [
                    "compile-module", "producer", additionalSource, "AddOne",
                    "--source", primarySource, "--export", "Twice",
                    "-o", mismatchedOutput, "--runtime", policy,
                    "--private-identity-manifest", producerManifest,
                    "--json",
                ]).ConfigureAwait(false);
            Assert.That(mismatchExit, Is.EqualTo(2));
            Assert.That(mismatchError, Is.Empty);
            using (var document = JsonDocument.Parse(mismatchOutput))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2001"));
            }
            Assert.That(await File.ReadAllBytesAsync(mismatchedOutput)
                .ConfigureAwait(false), Is.EqualTo(sentinel));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PrivateIdentityManifestV2CarriesFniThroughModuleAndNativeCliAsync()
    {
        const string privateSchema
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string providerIdentity = "FniProvider";
        const string consumerIdentity = "FniConsumer";
        const string ownerKey = "contract.iprobe";
        var memberKeys = new[] {
            "contract.iprobe.get",
            "contract.iprobe.try-read",
        };
        var source = Encoding.UTF8.GetBytes("""
            using Terrum.Interop;
            public int32 Add(int32 value) { return value + 1; }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Get(int32 value);
                [param(value): NativeOutput(
                    When: NativeOutputCondition.NonZero)]
                public unsafe int32 TryRead(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<int32> value);
            }
            """);
        var consumerSource = Encoding.UTF8.GetBytes("""
            public int32 Main() { return Add(41); }
            public unsafe int32 Invoke(
                ptr<void> receiver, int32 value) {
                unsafe { return IProbe.Get(receiver, value); }
            }
            """);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-private-identity-v2-fni-");
        var providerSourcePath = Path.Combine(directory, "provider.terrum");
        var consumerSourcePath = Path.Combine(directory, "consumer.terrum");
        var replacementSourcePath = Path.Combine(directory,
            "replacement.terrum");
        var nativeManifestPath = Path.Combine(directory, "native.json");
        var nativeOutputManifestPath = Path.Combine(directory,
            "native-output.json");
        var moduleProviderManifest = Path.Combine(directory,
            "module-provider.json");
        var moduleConsumerManifest = Path.Combine(directory,
            "module-consumer.json");
        var nativeProviderManifest = Path.Combine(directory,
            "native-provider.json");
        var nativeConsumerManifest = Path.Combine(directory,
            "native-consumer.json");
        var moduleProvider = Path.Combine(directory, "provider.trm");
        var moduleConsumer = Path.Combine(directory, "consumer.trm");
        var replacementModuleProvider = Path.Combine(directory,
            "replacement.trm");
        var nativeProvider = Path.Combine(directory, "provider.trmn");
        var nativeConsumer = Path.Combine(directory, "consumer.trmn");
        var preservedOutput = Path.Combine(directory, "preserved.trm");
        var preservedNativeOutput = Path.Combine(directory, "preserved.trmn");

        try
        {
            await File.WriteAllBytesAsync(providerSourcePath, source)
                .ConfigureAwait(false);
            await File.WriteAllBytesAsync(consumerSourcePath, consumerSource)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(nativeManifestPath,
                CreateNativeCoreManifest(usingModuleIdentity: providerIdentity,
                    includeForeignNativeInterface: true))
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(nativeOutputManifestPath,
                CreateNativeOutputCoreManifest(
                    usingModuleIdentity: providerIdentity))
                .ConfigureAwait(false);

            var foreignDefinition = new {
                interfaceOrdinal = 0,
                localDeclarationKey = ownerKey,
                isExported = true,
                members = new[] {
                    new {
                        memberOrdinal = 0,
                        localDeclarationKey = memberKeys[0],
                    },
                    new {
                        memberOrdinal = 1,
                        localDeclarationKey = memberKeys[1],
                    },
                },
            };
            await File.WriteAllTextAsync(moduleProviderManifest,
                CreateManifestV2(providerIdentity, [Digest(source)], null,
                    [foreignDefinition], [])).ConfigureAwait(false);
            var sentinel = new byte[] { 0xA5, 0x5A };
            await File.WriteAllBytesAsync(preservedOutput, sentinel)
                .ConfigureAwait(false);
            var (missingSchemaExit, missingSchemaOutput,
                missingSchemaError) = await RunAsync([], [
                    "compile-module", "producer", providerSourcePath, "Add",
                    "-o", preservedOutput, "--runtime", "fail-fast",
                    "--private-identity-manifest", moduleProviderManifest,
                    "--json",
                ]).ConfigureAwait(false);
            Assert.That(missingSchemaExit, Is.EqualTo(2),
                missingSchemaOutput + missingSchemaError);
            Assert.That(missingSchemaError, Is.Empty);
            using (var document = JsonDocument.Parse(missingSchemaOutput))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2001"));
            }
            Assert.That(await File.ReadAllBytesAsync(preservedOutput)
                .ConfigureAwait(false), Is.EqualTo(sentinel));
            await File.WriteAllBytesAsync(preservedNativeOutput, sentinel)
                .ConfigureAwait(false);
            var (aliasExit, aliasOutput, aliasError) = await RunAsync([], [
                "compile-module", "producer", providerSourcePath, "Add", "-o",
                preservedOutput, "--runtime", "fail-fast",
                "--native-core-manifest", moduleProviderManifest,
                "--private-identity-manifest", moduleProviderManifest,
                "--private-schema", privateSchema, "--json",
            ]).ConfigureAwait(false);
            Assert.That(aliasExit, Is.EqualTo(2), aliasOutput + aliasError);
            Assert.That(aliasError, Is.Empty);
            using (var document = JsonDocument.Parse(aliasOutput))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2002"));
            }
            Assert.That(await File.ReadAllBytesAsync(preservedOutput)
                .ConfigureAwait(false), Is.EqualTo(sentinel));

            var malformedDefinition = new {
                interfaceOrdinal = 0,
                localDeclarationKey = ownerKey,
                isExported = true,
                members = new object[] {
                    new {
                        memberOrdinal = 0,
                        localDeclarationKey = memberKeys[0],
                    },
                    new {
                        memberOrdinal = 0,
                        localDeclarationKey = memberKeys[1],
                    },
                },
            };
            await File.WriteAllTextAsync(moduleProviderManifest,
                CreateManifestV2(providerIdentity, [Digest(source)], null,
                    [malformedDefinition], [])).ConfigureAwait(false);
            var (malformedExit, malformedOutput, malformedError)
                = await RunAsync([], [
                    "compile-module", "producer", providerSourcePath, "Add",
                    "-o", preservedOutput, "--runtime", "fail-fast",
                    "--native-core-manifest", nativeManifestPath,
                    "--native-output-core-manifest",
                    nativeOutputManifestPath,
                    "--private-identity-manifest", moduleProviderManifest,
                    "--private-schema", privateSchema, "--json",
                ]).ConfigureAwait(false);
            Assert.That(malformedExit, Is.EqualTo(2),
                malformedOutput + malformedError);
            Assert.That(malformedError, Is.Empty);
            using (var document = JsonDocument.Parse(malformedOutput))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2001"));
            }
            Assert.That(await File.ReadAllBytesAsync(preservedOutput)
                .ConfigureAwait(false), Is.EqualTo(sentinel));

            await File.WriteAllTextAsync(moduleProviderManifest,
                CreateManifestV2(providerIdentity, [Digest(source)], null,
                    [foreignDefinition], [])).ConfigureAwait(false);
            var (moduleProviderExit, moduleProviderOutput,
                moduleProviderError) = await RunAsync([], [
                    "compile-module", "producer", providerSourcePath, "Add",
                    "-o", moduleProvider, "--runtime", "fail-fast",
                    "--native-core-manifest", nativeManifestPath,
                    "--native-output-core-manifest",
                    nativeOutputManifestPath,
                    "--private-identity-manifest", moduleProviderManifest,
                    "--private-schema", privateSchema, "--json",
                ]).ConfigureAwait(false);
            Assert.That(moduleProviderExit, Is.Zero,
                moduleProviderOutput + moduleProviderError);
            Assert.That(moduleProviderError, Is.Empty);

            var moduleProviderBytes = await File.ReadAllBytesAsync(
                moduleProvider).ConfigureAwait(false);
            var foreignImport = new {
                interfaceOrdinal = 0,
                localDeclarationKey = "consumer.iprobe",
                selectedModuleIdentity = providerIdentity,
                selectedExportKey = ownerKey,
                members = new[] {
                    new {
                        memberOrdinal = 0,
                        localDeclarationKey = "consumer.iprobe.get",
                        selectedExportKey = memberKeys[0],
                    },
                    new {
                        memberOrdinal = 1,
                        localDeclarationKey = "consumer.iprobe.try-read",
                        selectedExportKey = memberKeys[1],
                    },
                },
            };
            await File.WriteAllTextAsync(moduleConsumerManifest,
                CreateManifestV2(consumerIdentity, [Digest(consumerSource)],
                    Digest(moduleProviderBytes), [], [foreignImport]))
                .ConfigureAwait(false);
            var (moduleConsumerExit, moduleConsumerOutput,
                moduleConsumerError) = await RunAsync([], [
                    "compile-module", "consumer", consumerSourcePath,
                    moduleProvider, "-o", moduleConsumer, "--runtime",
                    "fail-fast", "--private-identity-manifest",
                    moduleConsumerManifest, "--private-schema", privateSchema,
                    "--json",
                ]).ConfigureAwait(false);
            Assert.That(moduleConsumerExit, Is.Zero,
                moduleConsumerOutput + moduleConsumerError);
            Assert.That(moduleConsumerError, Is.Empty);
            var (moduleRunExit, _, moduleRunError) = await RunAsync([], [
                "run-module", moduleConsumer, moduleProvider, "--runtime",
                "fail-fast",
            ]).ConfigureAwait(false);
            Assert.That(moduleRunExit, Is.EqualTo(42), moduleRunError);

            var replacementSource = Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(source).Replace(
                    "VTableByteOffset: uint32(0)",
                    "VTableByteOffset: uint32(8)",
                    StringComparison.Ordinal));
            await File.WriteAllBytesAsync(replacementSourcePath,
                replacementSource).ConfigureAwait(false);
            var replacementManifest = Path.Combine(directory,
                "replacement-provider.json");
            await File.WriteAllTextAsync(replacementManifest,
                CreateManifestV2(providerIdentity,
                    [Digest(replacementSource)], null,
                    [foreignDefinition], [])).ConfigureAwait(false);
            var (replacementExit, replacementOutput, replacementError)
                = await RunAsync([], [
                    "compile-module", "producer", replacementSourcePath,
                    "Add", "-o", replacementModuleProvider, "--runtime",
                    "fail-fast", "--native-core-manifest",
                    nativeManifestPath, "--native-output-core-manifest",
                    nativeOutputManifestPath, "--private-identity-manifest",
                    replacementManifest, "--private-schema", privateSchema,
                    "--json",
                ]).ConfigureAwait(false);
            Assert.That(replacementExit, Is.Zero,
                replacementOutput + replacementError);
            Assert.That(replacementError, Is.Empty);
            var (replacementValidationExit, replacementValidationOutput,
                replacementValidationError) = await RunAsync([], [
                    "run-module", moduleConsumer, replacementModuleProvider,
                    "--runtime", "fail-fast", "--json",
                ]).ConfigureAwait(false);
            Assert.That(replacementValidationExit, Is.EqualTo(1),
                replacementValidationOutput + replacementValidationError);
            Assert.That(replacementValidationError, Is.Empty);
            using (var document = JsonDocument.Parse(
                replacementValidationOutput))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM5002"));
            }

            await File.WriteAllTextAsync(nativeProviderManifest,
                CreateManifestV2(providerIdentity, [Digest(source)], null,
                    [foreignDefinition], [])).ConfigureAwait(false);
            var (wrongIdentityExit, wrongIdentityOutput, wrongIdentityError)
                = await RunAsync([], [
                    "compile-native", "producer", providerSourcePath,
                    "DifferentIdentity", "--export", "Add", "-o",
                    preservedNativeOutput, "--runtime", "fail-fast",
                    "--native-core-manifest", nativeManifestPath,
                    "--native-output-core-manifest",
                    nativeOutputManifestPath,
                    "--private-identity-manifest", nativeProviderManifest,
                    "--private-schema", privateSchema, "--json",
                ]).ConfigureAwait(false);
            Assert.That(wrongIdentityExit, Is.EqualTo(2),
                wrongIdentityOutput + wrongIdentityError);
            Assert.That(wrongIdentityError, Is.Empty);
            using (var document = JsonDocument.Parse(wrongIdentityOutput))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
            }
            Assert.That(await File.ReadAllBytesAsync(preservedNativeOutput)
                .ConfigureAwait(false), Is.EqualTo(sentinel));
            var (nativeProviderExit, nativeProviderOutput,
                nativeProviderError) = await RunAsync([], [
                    "compile-native", "producer", providerSourcePath,
                    providerIdentity, "--export", "Add", "-o",
                    nativeProvider, "--runtime", "fail-fast",
                    "--native-core-manifest", nativeManifestPath,
                    "--native-output-core-manifest",
                    nativeOutputManifestPath,
                    "--private-identity-manifest", nativeProviderManifest,
                    "--private-schema", privateSchema, "--json",
                ]).ConfigureAwait(false);
            Assert.That(nativeProviderExit, Is.Zero,
                nativeProviderOutput + nativeProviderError);
            Assert.That(nativeProviderError, Is.Empty);

            var nativeProviderBytes = await File.ReadAllBytesAsync(
                nativeProvider).ConfigureAwait(false);
            var incorrectProducerDigest = Digest(nativeProviderBytes);
            incorrectProducerDigest = (incorrectProducerDigest[0] == 'A'
                ? "B" : "A") + incorrectProducerDigest[1..];
            await File.WriteAllTextAsync(nativeConsumerManifest,
                CreateManifestV2(consumerIdentity,
                    [Digest(consumerSource)],
                    incorrectProducerDigest, [], [foreignImport]))
                .ConfigureAwait(false);
            var (digestExit, digestOutput, digestError) = await RunAsync(
                [], [
                    "compile-native", "consumer", consumerSourcePath,
                    nativeProvider, "-o", preservedNativeOutput,
                    "--runtime", "fail-fast",
                    "--private-identity-manifest", nativeConsumerManifest,
                    "--private-schema", privateSchema, "--json",
                ]).ConfigureAwait(false);
            Assert.That(digestExit, Is.EqualTo(2), digestOutput + digestError);
            Assert.That(digestError, Is.Empty);
            using (var document = JsonDocument.Parse(digestOutput))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2001"));
            }
            Assert.That(await File.ReadAllBytesAsync(preservedNativeOutput)
                .ConfigureAwait(false), Is.EqualTo(sentinel));
            await File.WriteAllTextAsync(nativeConsumerManifest,
                CreateManifestV2(consumerIdentity,
                    [Digest(consumerSource)],
                    Digest(nativeProviderBytes), [], [foreignImport]))
                .ConfigureAwait(false);
            var (nativeConsumerExit, nativeConsumerOutput,
                nativeConsumerError) = await RunAsync([], [
                    "compile-native", "consumer", consumerSourcePath,
                    nativeProvider, "-o", nativeConsumer, "--runtime",
                    "fail-fast", "--private-identity-manifest",
                    nativeConsumerManifest, "--private-schema", privateSchema,
                    "--json",
                ]).ConfigureAwait(false);
            Assert.That(nativeConsumerExit, Is.Zero,
                nativeConsumerOutput + nativeConsumerError);
            Assert.That(nativeConsumerError, Is.Empty);
            var (nativeRunExit, _, nativeRunError) = await RunAsync([], [
                "run-native", nativeConsumer, nativeProvider, "--runtime",
                "fail-fast",
            ]).ConfigureAwait(false);
            Assert.That(nativeRunExit, Is.EqualTo(42), nativeRunError);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PrivateFniAggregateOutputPublishesSourceCopyEvidenceAsync()
    {
        const string privateSchema
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string providerIdentity = "FniAggregateProvider";
        const string source = """
            using Terrum.Interop;
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Payload { public int32 Value; }
            public int32 Add(int32 value) { return value + 1; }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                [param(value): NativeOutput(
                    When: NativeOutputCondition.NonZero)]
                public unsafe int32 TryRead(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<Payload> value);
            }
            """;
        var sourceBytes = Encoding.UTF8.GetBytes(source);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-private-fni-copy-evidence-");
        var sourcePath = Path.Combine(directory, "provider.terrum");
        var layoutManifest = Path.Combine(directory, "layout.json");
        var nativeManifest = Path.Combine(directory, "native.json");
        var nativeOutputManifest = Path.Combine(directory,
            "native-output.json");
        var identityManifest = Path.Combine(directory, "identity.json");
        var artifact = Path.Combine(directory, "provider.trm");
        try
        {
            await File.WriteAllBytesAsync(sourcePath, sourceBytes)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(layoutManifest,
                CreateLayoutCoreManifest(providerIdentity))
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(nativeManifest,
                CreateNativeCoreManifest(usingModuleIdentity:
                    providerIdentity, includeForeignNativeInterface: true))
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(nativeOutputManifest,
                CreateNativeOutputCoreManifest(
                    usingModuleIdentity: providerIdentity))
                .ConfigureAwait(false);

            var owner = new {
                interfaceOrdinal = 0,
                localDeclarationKey = "contract.iprobe",
                isExported = true,
                members = new[] {
                    new {
                        memberOrdinal = 0,
                        localDeclarationKey = "contract.iprobe.try-read",
                    },
                },
            };
            await File.WriteAllTextAsync(identityManifest,
                CreateManifestV2(providerIdentity, [Digest(sourceBytes)],
                    null, [owner], [],
                    definitions: [
                        new {
                            kind = "struct",
                            qualifiedName = "Payload",
                            localDeclarationKey = "types.payload",
                            isExported = true,
                        },
                    ])).ConfigureAwait(false);

            var (exit, output, error) = await RunAsync([], [
                "compile-module", "producer", sourcePath, "Add", "-o",
                artifact, "--runtime", "fail-fast",
                "--layout-core-manifest", layoutManifest,
                "--native-core-manifest", nativeManifest,
                "--native-output-core-manifest", nativeOutputManifest,
                "--private-identity-manifest", identityManifest,
                "--private-schema", privateSchema, "--json",
            ]).ConfigureAwait(false);
            Assert.That(exit, Is.Zero, output + error);
            Assert.That(error, Is.Empty);
            var reference = ModuleCompiler.CreateReferenceWithSchema(
                await File.ReadAllBytesAsync(artifact).ConfigureAwait(false),
                privateSchema);
            Assert.That(reference.Structures.Single(static structure =>
                structure.Name == "Payload").DeclaredCopyEvidence,
                Is.True);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(false);
        }
    }

    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task CompileModulePrivateSchemaSelectionPreservesRevisionBoundariesAsync(
        string policy)
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string providerSource
            = "public int32 Read(int32 value) { return value + 1; }";
        const string consumerSource
            = "public int32 Main() { return Read(41); }";
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-private-schema-selection-");
        var providerSourcePath = Path.Combine(directory, "provider.terrum");
        var consumerSourcePath = Path.Combine(directory, "consumer.terrum");
        var selectedProducerPath = Path.Combine(directory,
            "selected-producer.trm");
        var defaultProducerPath = Path.Combine(directory,
            "default-producer.trm");
        var selectedConsumerPath = Path.Combine(directory,
            "selected-consumer.trm");
        var rejectedConsumerPath = Path.Combine(directory,
            "rejected-consumer.trm");
        var invalidSchemaOutputPath = Path.Combine(directory,
            "invalid-schema.trm");
        byte[] retained = [0xA5, 0x5A];
        try
        {
            await File.WriteAllTextAsync(providerSourcePath, providerSource,
                new UTF8Encoding(false)).ConfigureAwait(true);
            await File.WriteAllTextAsync(consumerSourcePath, consumerSource,
                new UTF8Encoding(false)).ConfigureAwait(true);

            var (selectedExit, selectedOutput, selectedError)
                = await RunAsync([], "compile-module", "producer",
                    providerSourcePath, "Read", "-o", selectedProducerPath,
                    "--runtime", policy, "--private-schema", schemaIdentity,
                    "--json").ConfigureAwait(true);
            Assert.That(selectedExit, Is.Zero,
                selectedOutput + selectedError);
            Assert.That(selectedError, Is.Empty);
            var selectedProducer
                = await File.ReadAllBytesAsync(selectedProducerPath)
                    .ConfigureAwait(true);
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
                selectedProducer.AsSpan(sizeof(int), sizeof(int))),
                Is.EqualTo(111));

            var (defaultExit, defaultOutput, defaultError) = await RunAsync(
                [], "compile-module", "producer", providerSourcePath, "Read",
                "-o", defaultProducerPath, "--runtime", policy, "--json")
                .ConfigureAwait(true);
            Assert.That(defaultExit, Is.Zero,
                defaultOutput + defaultError);
            var defaultProducer
                = await File.ReadAllBytesAsync(defaultProducerPath)
                    .ConfigureAwait(true);
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
                defaultProducer.AsSpan(sizeof(int), sizeof(int))),
                Is.AnyOf(104, 106));

            var (consumerExit, consumerOutput, consumerError)
                = await RunAsync([], "compile-module", "consumer",
                    consumerSourcePath, selectedProducerPath, "-o",
                    selectedConsumerPath, "--runtime", policy,
                    "--private-schema", schemaIdentity, "--json")
                    .ConfigureAwait(true);
            Assert.That(consumerExit, Is.Zero,
                consumerOutput + consumerError);
            Assert.That(consumerError, Is.Empty);
            var selectedConsumer
                = await File.ReadAllBytesAsync(selectedConsumerPath)
                    .ConfigureAwait(true);
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
                selectedConsumer.AsSpan(sizeof(int), sizeof(int))),
                Is.EqualTo(111));

            var (runExit, _, runError) = await RunAsync([],
                "run-module", selectedConsumerPath, selectedProducerPath,
                "--runtime", policy).ConfigureAwait(true);
            Assert.That(runExit, Is.EqualTo(42), runError);
            Assert.That(runError, Is.Empty);

            await File.WriteAllBytesAsync(rejectedConsumerPath, retained)
                .ConfigureAwait(true);
            var (implicitExit, implicitOutput, implicitError)
                = await RunAsync([], "compile-module", "consumer",
                    consumerSourcePath, selectedProducerPath, "-o",
                    rejectedConsumerPath, "--runtime", policy, "--json")
                    .ConfigureAwait(true);
            Assert.That(implicitExit, Is.EqualTo(1),
                $"{implicitOutput}{Environment.NewLine}{implicitError}");
            Assert.That(implicitError, Is.Empty);
            using (var document = JsonDocument.Parse(implicitOutput))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM5002"));
            }
            Assert.That(await File.ReadAllBytesAsync(rejectedConsumerPath)
                .ConfigureAwait(true), Is.EqualTo(retained));

            var (mismatchExit, mismatchOutput, mismatchError)
                = await RunAsync([], "compile-module", "consumer",
                    consumerSourcePath, defaultProducerPath, "-o",
                    rejectedConsumerPath, "--runtime", policy,
                    "--private-schema", schemaIdentity, "--json")
                    .ConfigureAwait(true);
            Assert.That(mismatchExit, Is.EqualTo(1),
                $"{mismatchOutput}{Environment.NewLine}{mismatchError}");
            Assert.That(mismatchError, Is.Empty);
            using (var document = JsonDocument.Parse(mismatchOutput))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM5002"));
            }
            Assert.That(await File.ReadAllBytesAsync(rejectedConsumerPath)
                .ConfigureAwait(true), Is.EqualTo(retained));

            await File.WriteAllBytesAsync(invalidSchemaOutputPath, retained)
                .ConfigureAwait(true);
            var (invalidExit, invalidOutput, invalidError) = await RunAsync(
                [], "compile-module", "producer", providerSourcePath, "Read",
                "-o", invalidSchemaOutputPath, "--runtime", policy,
                "--private-schema",
                "terrum/private-semantic-111-foreign-native-interfaces",
                "--json").ConfigureAwait(true);
            Assert.That(invalidExit, Is.EqualTo(2),
                $"{invalidOutput}{Environment.NewLine}{invalidError}");
            Assert.That(invalidError, Is.Empty);
            using (var document = JsonDocument.Parse(invalidOutput))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
            }
            Assert.That(await File.ReadAllBytesAsync(invalidSchemaOutputPath)
                .ConfigureAwait(true), Is.EqualTo(retained));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task CompileModuleIdentityManifestCarriesRawUnionReturnCoverageAsync(
        string policy)
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-private-identity-raw-union-layout-");
        var providerSource = Path.Combine(directory, "provider.terrum");
        var providerManifest = Path.Combine(directory, "provider.json");
        var layoutManifest = Path.Combine(directory, "layout.json");
        var providerArtifact = Path.Combine(directory, "provider.trm");
        var consumerSource = Path.Combine(directory, "consumer.terrum");
        var consumerManifest = Path.Combine(directory, "consumer.json");
        var consumerArtifact = Path.Combine(directory, "consumer.trm");
        var replacementSource = Path.Combine(directory,
            "replacement-provider.terrum");
        var replacementManifest = Path.Combine(directory,
            "replacement-provider.json");
        var replacementArtifact = Path.Combine(directory,
            "replacement-provider.trm");
        var providerBytes = Encoding.UTF8.GetBytes("""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint32 Narrow;
                public mut uint64 Wide;
            }

            public RawWord MakeWide() {
                init mut RawWord value;
                unsafe { value.Wide = uint64(0x1122334455667788); }
                return value;
            }
            """);
        var consumerBytes = Encoding.UTF8.GetBytes("""
            int32 Main() {
                mut RawWord value = MakeWide();
                unsafe {
                    return value.Wide == uint64(0x1122334455667788)
                        ? 0 : 1;
                }
            }
            """);
        object LayoutKindMember(string name, int ordinal)
            => new {
                owner = new { kind = "enum", qualifiedName = "LayoutKind" },
                enumIdentity = new {
                    providerIdentity = "LayoutCore",
                    exportKey = "LayoutKind",
                    kind = "enum-type",
                },
                memberOrdinal = ordinal,
                memberIdentity = new {
                    providerIdentity = "LayoutCore",
                    exportKey = $"LayoutKind.{name}",
                    kind = "enum-member",
                },
                memberDeclarationKey = $"layout-kind/member/{name}",
            };
        var layoutKindMembers = new[] {
            LayoutKindMember("Auto", 0),
            LayoutKindMember("Sequential", 1),
            LayoutKindMember("Union", 2),
            LayoutKindMember("Transparent", 3),
        };
        try
        {
            await File.WriteAllBytesAsync(providerSource, providerBytes)
                .ConfigureAwait(false);
            await File.WriteAllBytesAsync(consumerSource, consumerBytes)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(layoutManifest,
                CreateLayoutCoreManifest("RawUnionProvider")
                    .Replace("sample.layout-core", "LayoutCore",
                        StringComparison.Ordinal))
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(providerManifest,
                CreateManifest("RawUnionProvider", [Digest(providerBytes)],
                    null, [
                        new {
                            kind = "struct",
                            qualifiedName = "RawWord",
                            localDeclarationKey = "types.rawword",
                            isExported = true,
                        },
                    ], [
                        new {
                            kind = "enum",
                            qualifiedName = "LayoutKind",
                            localDeclarationKey = "types.layout-kind",
                            selectedModuleIdentity = "LayoutCore",
                            selectedExportKey = "LayoutKind",
                        },
                    ], [], layoutKindMembers)).ConfigureAwait(false);

            var (producerExit, producerOutput, producerError)
                = await RunAsync([], [
                    "compile-module", "producer", providerSource, "MakeWide",
                    "-o", providerArtifact, "--runtime", policy,
                    "--layout-core-manifest", layoutManifest,
                    "--private-identity-manifest", providerManifest, "--json",
                ]).ConfigureAwait(false);
            Assert.That(producerExit, Is.Zero,
                producerOutput + producerError);
            Assert.That(producerError, Is.Empty);

            var providerArtifactBytes
                = await File.ReadAllBytesAsync(providerArtifact)
                    .ConfigureAwait(false);
            await File.WriteAllTextAsync(consumerManifest,
                CreateManifest("RawUnionConsumer", [Digest(consumerBytes)],
                    Digest(providerArtifactBytes), [], [
                        new {
                            kind = "struct",
                            qualifiedName = "RawWord",
                            localDeclarationKey = "types.rawword.consumer",
                            selectedModuleIdentity = "RawUnionProvider",
                            selectedExportKey = "types.rawword",
                        },
                    ], [])).ConfigureAwait(false);
            var (consumerExit, consumerOutput, consumerError)
                = await RunAsync([], [
                    "compile-module", "consumer", consumerSource,
                    providerArtifact, "-o", consumerArtifact, "--runtime",
                    policy, "--private-identity-manifest", consumerManifest,
                    "--json",
                ]).ConfigureAwait(false);
            Assert.That(consumerExit, Is.Zero,
                consumerOutput + consumerError);
            Assert.That(consumerError, Is.Empty);

            var (runExit, _, runError) = await RunAsync([], [
                "run-module", consumerArtifact, providerArtifact,
                "--runtime", policy,
            ]).ConfigureAwait(false);
            Assert.That(runExit, Is.Zero, runError);

            var replacementBytes = Encoding.UTF8.GetBytes("""
                [Blittable]
                [Layout(LayoutKind.Union)]
                public struct RawWord {
                    public mut uint32 Narrow;
                    public mut uint64 Wide;
                }

                public RawWord MakeWide() {
                    init mut RawWord value;
                    unsafe { value.Narrow = uint32(0x55667788); }
                    return value;
                }
                """);
            await File.WriteAllBytesAsync(replacementSource,
                replacementBytes).ConfigureAwait(false);
            await File.WriteAllTextAsync(replacementManifest,
                CreateManifest("RawUnionProvider",
                    [Digest(replacementBytes)], null, [
                        new {
                            kind = "struct",
                            qualifiedName = "RawWord",
                            localDeclarationKey = "types.rawword",
                            isExported = true,
                        },
                    ], [
                        new {
                            kind = "enum",
                            qualifiedName = "LayoutKind",
                            localDeclarationKey = "types.layout-kind",
                            selectedModuleIdentity = "LayoutCore",
                            selectedExportKey = "LayoutKind",
                        },
                    ], [], layoutKindMembers)).ConfigureAwait(false);
            var (replacementExit, replacementOutput, replacementError)
                = await RunAsync([], [
                    "compile-module", "producer", replacementSource,
                    "MakeWide", "-o", replacementArtifact, "--runtime",
                    policy, "--layout-core-manifest", layoutManifest,
                    "--private-identity-manifest", replacementManifest,
                    "--json",
                ]).ConfigureAwait(false);
            Assert.That(replacementExit, Is.Zero,
                replacementOutput + replacementError);
            Assert.That(replacementError, Is.Empty);

            var (replacementRunExit, _, replacementRunError)
                = await RunAsync([], [
                    "run-module", consumerArtifact, replacementArtifact,
                    "--runtime", policy,
                ]).ConfigureAwait(false);
            Assert.That(replacementRunExit, Is.Not.Zero,
                replacementRunError);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(false);
        }
    }

    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task CompileModuleIdentityManifestBindsCoreEnumMembersAsync(
        string policy)
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-private-identity-core-enum-");
        var sourcePath = Path.Combine(directory, "source.terrum");
        var layoutManifest = Path.Combine(directory, "layout.json");
        var identityManifest = Path.Combine(directory, "identity.json");
        var artifact = Path.Combine(directory, "module.trm");
        var sourceBytes = Encoding.UTF8.GetBytes("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            public int32 Read(Cell value) { return value.Value; }
            public LayoutKind LayoutOf() { return LayoutKind.Sequential; }
            """);
        var layoutCoreManifest = CreateLayoutCoreManifest(
                "CoreEnumMemberProvider")
            .Replace("sample.layout-core", "LayoutCore",
                StringComparison.Ordinal);
        object EnumMember(string name, int ordinal)
            => new {
                owner = new { kind = "enum", qualifiedName = "LayoutKind" },
                enumIdentity = new {
                    providerIdentity = "LayoutCore",
                    exportKey = "LayoutKind",
                    kind = "enum-type",
                },
                memberOrdinal = ordinal,
                memberIdentity = new {
                    providerIdentity = "LayoutCore",
                    exportKey = $"LayoutKind.{name}",
                    kind = "enum-member",
                },
                memberDeclarationKey = $"layout-kind/member/{name}",
            };
        var enumMembers = new[] {
            EnumMember("Auto", 0),
            EnumMember("Sequential", 1),
            EnumMember("Union", 2),
            EnumMember("Transparent", 3),
        };
        try
        {
            await File.WriteAllBytesAsync(sourcePath, sourceBytes)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(layoutManifest,
                layoutCoreManifest).ConfigureAwait(false);
            await File.WriteAllTextAsync(identityManifest,
                CreateManifest("CoreEnumMemberProvider",
                    [Digest(sourceBytes)], null, [
                        new {
                            kind = "struct",
                            qualifiedName = "Cell",
                            localDeclarationKey = "types.cell",
                            isExported = true,
                        },
                    ], [
                        new {
                            kind = "enum",
                            qualifiedName = "LayoutKind",
                            localDeclarationKey = "types.layout-kind",
                            selectedModuleIdentity = "LayoutCore",
                            selectedExportKey = "LayoutKind",
                        },
                    ], [], enumMembers)).ConfigureAwait(false);

            var (exit, output, error) = await RunAsync([], [
                "compile-module", "producer", sourcePath, "Read",
                "--export", "LayoutOf", "-o", artifact, "--runtime", policy,
                "--layout-core-manifest", layoutManifest,
                "--private-identity-manifest", identityManifest, "--json",
            ]).ConfigureAwait(false);
            Assert.That(exit, Is.Zero, output + error);
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success")
                .GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("runtimePolicy")
                .GetString(), Is.EqualTo(policy));
            Assert.That(File.Exists(artifact), Is.True);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(false);
        }
    }

    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task CompileModuleConsumerCannotSelectCoreEnumMembersAsync(
        string policy)
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-private-identity-core-enum-consumer-");
        var producerSource = Path.Combine(directory, "producer.terrum");
        var consumerSource = Path.Combine(directory, "consumer.terrum");
        var producerManifest = Path.Combine(directory, "producer.json");
        var layoutManifest = Path.Combine(directory, "layout.json");
        var identityManifest = Path.Combine(directory, "consumer-identity.json");
        var producerArtifact = Path.Combine(directory, "producer.trm");
        var consumerArtifact = Path.Combine(directory, "consumer.trm");
        var producerBytes = Encoding.UTF8.GetBytes(
            "public int32 Value() { return 7; }");
        var consumerBytes = Encoding.UTF8.GetBytes(
            "public LayoutKind GetLayoutKind() { return LayoutKind.Sequential; }");
        var preservedOutput = "preserve existing artifact"u8.ToArray();
        try
        {
            await File.WriteAllBytesAsync(producerSource, producerBytes)
                .ConfigureAwait(false);
            await File.WriteAllBytesAsync(consumerSource, consumerBytes)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(producerManifest,
                CreateManifest("ManifestProducer", [Digest(producerBytes)],
                    null, [], [], [])).ConfigureAwait(false);
            var (producerExit, producerOutput, producerError)
                = await RunAsync([], [
                    "compile-module", "producer", producerSource, "Value",
                    "-o", producerArtifact, "--runtime", policy,
                    "--private-identity-manifest", producerManifest,
                    "--json",
                ]).ConfigureAwait(false);
            Assert.That(producerExit, Is.Zero,
                producerOutput + producerError);

            var layoutCoreManifest = CreateLayoutCoreManifest(
                    "ManifestConsumer")
                .Replace("sample.layout-core", "LayoutCore",
                    StringComparison.Ordinal);
            await File.WriteAllTextAsync(layoutManifest,
                layoutCoreManifest).ConfigureAwait(false);
            var producerArtifactBytes = await File.ReadAllBytesAsync(
                producerArtifact).ConfigureAwait(false);
            var enumMembers = new object[] {
                new {
                    owner = new {
                        kind = "enum",
                        qualifiedName = "LayoutKind",
                    },
                    enumIdentity = new {
                        providerIdentity = "LayoutCore",
                        exportKey = "LayoutKind",
                        kind = "enum-type",
                    },
                    memberOrdinal = 0,
                    memberIdentity = new {
                        providerIdentity = "LayoutCore",
                        exportKey = "LayoutKind.Auto",
                        kind = "enum-member",
                    },
                    memberDeclarationKey = "layout-kind/member/Auto",
                },
                new {
                    owner = new {
                        kind = "enum",
                        qualifiedName = "LayoutKind",
                    },
                    enumIdentity = new {
                        providerIdentity = "LayoutCore",
                        exportKey = "LayoutKind",
                        kind = "enum-type",
                    },
                    memberOrdinal = 1,
                    memberIdentity = new {
                        providerIdentity = "LayoutCore",
                        exportKey = "LayoutKind.Sequential",
                        kind = "enum-member",
                    },
                    memberDeclarationKey = "layout-kind/member/Sequential",
                },
                new {
                    owner = new {
                        kind = "enum",
                        qualifiedName = "LayoutKind",
                    },
                    enumIdentity = new {
                        providerIdentity = "LayoutCore",
                        exportKey = "LayoutKind",
                        kind = "enum-type",
                    },
                    memberOrdinal = 2,
                    memberIdentity = new {
                        providerIdentity = "LayoutCore",
                        exportKey = "LayoutKind.Union",
                        kind = "enum-member",
                    },
                    memberDeclarationKey = "layout-kind/member/Union",
                },
                new {
                    owner = new {
                        kind = "enum",
                        qualifiedName = "LayoutKind",
                    },
                    enumIdentity = new {
                        providerIdentity = "LayoutCore",
                        exportKey = "LayoutKind",
                        kind = "enum-type",
                    },
                    memberOrdinal = 3,
                    memberIdentity = new {
                        providerIdentity = "LayoutCore",
                        exportKey = "LayoutKind.Transparent",
                        kind = "enum-member",
                    },
                    memberDeclarationKey = "layout-kind/member/Transparent",
                },
            };
            await File.WriteAllTextAsync(identityManifest,
                CreateManifest("ManifestConsumer", [Digest(consumerBytes)],
                    Digest(producerArtifactBytes), [], [
                        new {
                            kind = "enum",
                            qualifiedName = "LayoutKind",
                            localDeclarationKey = "types.layout-kind",
                            selectedModuleIdentity = "LayoutCore",
                            selectedExportKey = "LayoutKind",
                        },
                    ], [])).ConfigureAwait(false);
            await File.WriteAllBytesAsync(consumerArtifact, preservedOutput)
                .ConfigureAwait(false);

            var (consumerExit, consumerOutput, consumerError)
                = await RunAsync([], [
                    "compile-module", "consumer", consumerSource,
                    producerArtifact, "-o", consumerArtifact, "--runtime",
                    policy, "--layout-core-manifest", layoutManifest,
                    "--private-identity-manifest", identityManifest,
                    "--json",
                ]).ConfigureAwait(false);
            Assert.That(consumerExit, Is.EqualTo(2),
                consumerOutput + consumerError);
            Assert.That(consumerError, Is.Empty);
            using var document = JsonDocument.Parse(consumerOutput);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM2001"));
            Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("message").GetString(),
                Does.Contain("select a Core enum identity directly"));
            Assert.That(await File.ReadAllBytesAsync(consumerArtifact)
                .ConfigureAwait(false), Is.EqualTo(preservedOutput));

            await File.WriteAllTextAsync(identityManifest,
                CreateManifest("ManifestConsumer", [Digest(consumerBytes)],
                    Digest(producerArtifactBytes), [], [
                        new {
                            kind = "enum",
                            qualifiedName = "LayoutKind",
                            localDeclarationKey = "types.layout-kind",
                            selectedModuleIdentity = "LayoutCore",
                            selectedExportKey = "LayoutKind",
                        },
                    ], [], enumMembers)).ConfigureAwait(false);
            var (assignmentExit, assignmentOutput, assignmentError)
                = await RunAsync([], [
                    "compile-module", "consumer", consumerSource,
                    producerArtifact, "-o", consumerArtifact, "--runtime",
                    policy, "--private-identity-manifest", identityManifest,
                    "--json",
                ]).ConfigureAwait(false);
            Assert.That(assignmentExit, Is.EqualTo(2),
                assignmentOutput + assignmentError);
            Assert.That(assignmentError, Is.Empty);
            using var assignmentDocument
                = JsonDocument.Parse(assignmentOutput);
            Assert.That(assignmentDocument.RootElement
                .GetProperty("diagnostics")[0].GetProperty("code")
                .GetString(), Is.EqualTo("TRM2001"));
            Assert.That(assignmentDocument.RootElement
                .GetProperty("diagnostics")[0].GetProperty("message")
                .GetString(), Does.Contain("cannot assign Core enum-member"));
            Assert.That(await File.ReadAllBytesAsync(consumerArtifact)
                .ConfigureAwait(false), Is.EqualTo(preservedOutput));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(false);
        }
    }

    private static string CreateManifest(string usingModuleIdentity,
        string[] sourceSha256, string? referenceProducerSha256,
        object[] definitions, object[] imports,
        object[] sourceEnumDefinitions, object[]? enumMembers = null)
        => JsonSerializer.Serialize(new {
            schemaVersion = 1,
            usingModuleIdentity,
            sourceSha256,
            referenceProducerSha256,
            definitions,
            imports,
            enumMembers = enumMembers ?? Array.Empty<object>(),
            sourceEnumDefinitions,
        });

    private static string CreateManifestV2(string usingModuleIdentity,
        string[] sourceSha256, string? referenceProducerSha256,
        object[] foreignInterfaceDefinitions,
        object[] foreignInterfaceImports, object[]? definitions = null,
        object[]? imports = null)
        => JsonSerializer.Serialize(new {
            schemaVersion = 2,
            usingModuleIdentity,
            sourceSha256,
            referenceProducerSha256,
            definitions = definitions ?? Array.Empty<object>(),
            imports = imports ?? Array.Empty<object>(),
            enumMembers = Array.Empty<object>(),
            sourceEnumDefinitions = Array.Empty<object>(),
            foreignInterfaceDefinitions,
            foreignInterfaceImports,
        });

    private static string Digest(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));
}
