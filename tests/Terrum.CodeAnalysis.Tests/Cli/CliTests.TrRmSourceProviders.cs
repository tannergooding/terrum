// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    [Test]
    public async Task CompileTrRmBindsSelectedSourceProviderModulesAsync()
    {
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-local-field-layout-2";
        const string providerIdentity = "source-provider-cli-provider";
        const string secondProviderIdentity
            = "source-provider-cli-second-provider";
        const string consumerIdentity = "source-provider-cli-consumer";
        const string providerSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Cell {
                public int32 Value;
            }
            """;
        const string secondProviderSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Other {
                private int32 Value;
            }
            """;
        const string consumerSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Wrapper {
                public Cell Inner;
                public Other Extra;
            }

            public void Fill(ref<init int32> result, int32 value) {
                *result = value;
            }

            public int32 Main() {
                init Wrapper wrapper;
                Fill(ref init wrapper.Inner.Value, 42);
                return wrapper.Inner.Value;
            }
            """;
        var providerExports = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-exports-{Guid.NewGuid():N}.json");
        var providerLayout = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-layout-{Guid.NewGuid():N}.json");
        var providerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-provider-{Guid.NewGuid():N}.trrm");
        var secondProviderExports = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-second-exports-{Guid.NewGuid():N}.json");
        var secondProviderLayout = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-second-layout-{Guid.NewGuid():N}.json");
        var secondProviderArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-second-provider-{Guid.NewGuid():N}.trrm");
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-inputs-{Guid.NewGuid():N}.json");
        var consumerLayout = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-consumer-layout-{Guid.NewGuid():N}.json");
        var consumerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-consumer-{Guid.NewGuid():N}.trrm");
        var consumerImage = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-consumer-{Guid.NewGuid():N}.exe");
        byte[] retained = [0xA5, 0x5A];
        try
        {
            var exportConfiguration = JsonSerializer.Serialize(new {
                schemaVersion = 1,
                exports = new[] {
                    new {
                        @namespace = "",
                        name = "Cell",
                        exportKey = "type.cell",
                        fieldExports = new[] {
                            new {
                                name = "Value",
                                exportKey = "field.cell.value",
                            },
                        },
                    },
                },
            });
            await File.WriteAllTextAsync(providerExports,
                exportConfiguration, new UTF8Encoding(false))
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(providerLayout,
                CreateLayoutCoreManifest(providerIdentity),
                new UTF8Encoding(false)).ConfigureAwait(true);
            var (providerExit, providerOutput, providerError)
                = await RunAsync(Encoding.UTF8.GetBytes(providerSource),
                    "compile-trrm", "-", "--identity", providerIdentity,
                    "--trrm-schema", schemaIdentity,
                    "--trrm-source-type-exports", providerExports,
                    "--layout-core-manifest", providerLayout, "--library",
                    "-o", providerArtifact, "--runtime", "fail-fast",
                    "--json").ConfigureAwait(false);
            Assert.That(providerExit, Is.Zero,
                $"{providerOutput}{Environment.NewLine}{providerError}");
            Assert.That(providerError, Is.Empty);

            var secondExportConfiguration = JsonSerializer.Serialize(new {
                schemaVersion = 1,
                exports = new[] {
                    new {
                        @namespace = "",
                        name = "Other",
                        exportKey = "type.other",
                        fieldExports = Array.Empty<object>(),
                    },
                },
            });
            await File.WriteAllTextAsync(secondProviderExports,
                secondExportConfiguration, new UTF8Encoding(false))
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(secondProviderLayout,
                CreateLayoutCoreManifest(secondProviderIdentity),
                new UTF8Encoding(false)).ConfigureAwait(true);
            var (secondProviderExit, secondProviderOutput, secondProviderError)
                = await RunAsync(
                    Encoding.UTF8.GetBytes(secondProviderSource),
                    "compile-trrm", "-", "--identity", secondProviderIdentity,
                    "--trrm-schema", schemaIdentity,
                    "--trrm-source-type-exports", secondProviderExports,
                    "--layout-core-manifest", secondProviderLayout,
                    "--library", "-o", secondProviderArtifact, "--runtime",
                    "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(secondProviderExit, Is.Zero,
                $"{secondProviderOutput}{Environment.NewLine}{secondProviderError}");
            Assert.That(secondProviderError, Is.Empty);

            var providerConfiguration = JsonSerializer.Serialize(new {
                schemaVersion = 1,
                providers = new[] {
                    new { schemaIdentity, path = providerArtifact },
                    new { schemaIdentity, path = secondProviderArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = providerIdentity,
                        typeExportKeys = new[] { "type.cell" },
                        fieldExportKeys = new[] { "field.cell.value" },
                    },
                    new {
                        providerInputOrdinal = 1,
                        moduleIdentity = secondProviderIdentity,
                        typeExportKeys = new[] { "type.other" },
                        fieldExportKeys = Array.Empty<string>(),
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections,
                providerConfiguration, new UTF8Encoding(false))
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(consumerLayout,
                CreateLayoutCoreManifest(consumerIdentity),
                new UTF8Encoding(false)).ConfigureAwait(true);
            var (compileExit, compileOutput, compileError)
                = await RunAsync(Encoding.UTF8.GetBytes(consumerSource),
                    "compile-trrm", "-", "--identity", consumerIdentity,
                    "--trrm-schema", schemaIdentity,
                    "--trrm-source-providers", providerSelections,
                    "--layout-core-manifest", consumerLayout,
                    "-o", consumerArtifact, "--runtime", "fail-fast",
                    "--json").ConfigureAwait(false);
            Assert.That(compileExit, Is.Zero,
                $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (runExit, runOutput, runError) = await RunAsync([],
                "run-trrm", consumerArtifact, "--runtime", "fail-fast",
                "--trrm-schema", schemaIdentity,
                "--trrm-module", consumerIdentity, "--trrm-provider",
                schemaIdentity, providerArtifact, "--trrm-provider",
                schemaIdentity, secondProviderArtifact, "--json")
                .ConfigureAwait(false);
            Assert.That(runExit, Is.EqualTo(42),
                $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (buildExit, buildOutput, buildError) = await RunAsync([],
                "build-trrm", consumerArtifact, "-o", consumerImage,
                "--runtime", "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", consumerIdentity, "--trrm-provider",
                schemaIdentity, providerArtifact, "--trrm-provider",
                schemaIdentity, secondProviderArtifact, "--json")
                .ConfigureAwait(false);
            Assert.That(buildExit, Is.Zero,
                $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(consumerImage)
                .ConfigureAwait(true), Is.EqualTo(42));

            await File.WriteAllBytesAsync(consumerArtifact, retained)
                .ConfigureAwait(true);
            var invalidSelection = JsonSerializer.Serialize(new {
                schemaVersion = 1,
                providers = new[] {
                    new { schemaIdentity, path = providerArtifact },
                    new { schemaIdentity, path = secondProviderArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = providerIdentity,
                        typeExportKeys = new[] { "type.missing" },
                        fieldExportKeys = Array.Empty<string>(),
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections, invalidSelection,
                new UTF8Encoding(false)).ConfigureAwait(true);
            var (invalidExit, invalidOutput, invalidError) = await RunAsync(
                Encoding.UTF8.GetBytes(consumerSource), "compile-trrm", "-",
                "--identity", consumerIdentity, "--trrm-schema", schemaIdentity,
                "--trrm-source-providers", providerSelections,
                "--layout-core-manifest", consumerLayout,
                "-o", consumerArtifact, "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(invalidExit, Is.EqualTo(2),
                $"{invalidOutput}{Environment.NewLine}{invalidError}");
            Assert.That(invalidError, Is.Empty);
            using (var document = JsonDocument.Parse(invalidOutput))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2001"));
            }
            Assert.That(await File.ReadAllBytesAsync(consumerArtifact)
                    .ConfigureAwait(true),
                Is.EqualTo(retained));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(providerExports)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(providerLayout)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(providerArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(secondProviderExports)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(secondProviderLayout)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(secondProviderArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerLayout)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerImage)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task CompileTrRmRejectsWrongSourceProviderSchemaBeforeSourceReadAsync()
    {
        const string identity = "source-provider-cli-schema";
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-local-field-layout-2";
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-wrong-schema-{Guid.NewGuid():N}.json");
        var output = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-wrong-schema-{Guid.NewGuid():N}.trrm");
        var aliasPath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-alias-{Guid.NewGuid():N}.trrm");
        byte[] retained = [0xA5, 0x5A];
        try
        {
            var providerConfiguration = JsonSerializer.Serialize(new {
                schemaVersion = 1,
                providers = new[] {
                    new {
                        schemaIdentity = "trrm/1-unselected-provider-schema",
                        path = "missing-provider.trrm",
                    },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = "missing-provider",
                        typeExportKeys = new[] { "type.missing" },
                        fieldExportKeys = Array.Empty<string>(),
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections,
                providerConfiguration, new UTF8Encoding(false))
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(output, retained)
                .ConfigureAwait(true);

            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes("this is not valid Terrum"),
                "compile-trrm", "-", "--identity", identity,
                "--trrm-schema", schemaIdentity,
                "--trrm-source-providers", providerSelections,
                "-o", output, "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(exit, Is.EqualTo(2),
                $"{result}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            using (var document = JsonDocument.Parse(result))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
            }
            Assert.That(await File.ReadAllBytesAsync(output)
                    .ConfigureAwait(true),
                Is.EqualTo(retained));

            var invalidOrdinalConfiguration = JsonSerializer.Serialize(new {
                schemaVersion = 1,
                providers = new[] {
                    new { schemaIdentity, path = "missing-provider.trrm" },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 1,
                        moduleIdentity = "missing-provider",
                        typeExportKeys = new[] { "type.missing" },
                        fieldExportKeys = Array.Empty<string>(),
                    },
                },
            });
            var duplicatePropertyConfiguration = """
                {"schemaVersion":1,"schemaVersion":1,"providers":[{"schemaIdentity":"trrm/1-draft-init-reference-bodies-local-field-layout-2","path":"missing-provider.trrm"}],"references":[{"providerInputOrdinal":0,"moduleIdentity":"missing-provider","typeExportKeys":["type.missing"],"fieldExportKeys":[]}]}
                """;
            foreach (var invalidConfiguration in new[] {
                invalidOrdinalConfiguration,
                duplicatePropertyConfiguration,
            })
            {
                await File.WriteAllTextAsync(providerSelections,
                    invalidConfiguration, new UTF8Encoding(false))
                    .ConfigureAwait(true);
                var (invalidExit, invalidResult, invalidError)
                    = await RunAsync(
                        Encoding.UTF8.GetBytes("this is not valid Terrum"),
                        "compile-trrm", "-", "--identity", identity,
                        "--trrm-schema", schemaIdentity,
                        "--trrm-source-providers", providerSelections,
                        "-o", output, "--runtime", "fail-fast", "--json")
                        .ConfigureAwait(false);
                Assert.That(invalidExit, Is.EqualTo(2),
                    $"{invalidResult}{Environment.NewLine}{invalidError}");
                Assert.That(invalidError, Is.Empty);
                using (var document = JsonDocument.Parse(invalidResult))
                {
                    Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                        .GetProperty("code").GetString(),
                        Is.EqualTo("TRM2001"));
                }
                Assert.That(await File.ReadAllBytesAsync(output)
                        .ConfigureAwait(true),
                    Is.EqualTo(retained));
            }

            var providerAliasConfiguration = JsonSerializer.Serialize(new {
                schemaVersion = 1,
                providers = new[] {
                    new { schemaIdentity, path = output },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = "missing-provider",
                        typeExportKeys = new[] { "type.missing" },
                        fieldExportKeys = Array.Empty<string>(),
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections,
                providerAliasConfiguration, new UTF8Encoding(false))
                .ConfigureAwait(true);
            var (providerAliasExit, providerAliasResult, providerAliasError)
                = await RunAsync(
                    Encoding.UTF8.GetBytes("this is not valid Terrum"),
                    "compile-trrm", "-", "--identity", identity,
                    "--trrm-schema", schemaIdentity,
                    "--trrm-source-providers", providerSelections,
                    "-o", output, "--runtime", "fail-fast", "--json")
                    .ConfigureAwait(false);
            Assert.That(providerAliasExit, Is.EqualTo(2),
                $"{providerAliasResult}{Environment.NewLine}{providerAliasError}");
            Assert.That(providerAliasError, Is.Empty);
            using (var document = JsonDocument.Parse(providerAliasResult))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2002"));
            }
            Assert.That(await File.ReadAllBytesAsync(output)
                    .ConfigureAwait(true),
                Is.EqualTo(retained));

            var aliasContents = Encoding.UTF8.GetBytes(providerConfiguration);
            await File.WriteAllBytesAsync(aliasPath, aliasContents)
                .ConfigureAwait(true);
            var (aliasExit, aliasResult, aliasError) = await RunAsync(
                Encoding.UTF8.GetBytes("this is not valid Terrum"),
                "compile-trrm", "-", "--identity", identity,
                "--trrm-schema", schemaIdentity,
                "--trrm-source-providers", aliasPath, "-o", aliasPath,
                "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(aliasExit, Is.EqualTo(2),
                $"{aliasResult}{Environment.NewLine}{aliasError}");
            Assert.That(aliasError, Is.Empty);
            using (var document = JsonDocument.Parse(aliasResult))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2002"));
            }
            Assert.That(await File.ReadAllBytesAsync(aliasPath)
                    .ConfigureAwait(true),
                Is.EqualTo(aliasContents));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(aliasPath)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(output)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task CompileTrRmReadsVersionTwoClassProviderPairingBeforeProviderReadAsync()
    {
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";
        const string nestedSchemaIdentity
            = "trrm/1-draft-init-reference-bodies-local-field-layout-2";
        const string providerPath = "missing-class-provider.trrm";
        const string moduleIdentity = "source-provider-cli-class-provider";
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-v2-{Guid.NewGuid():N}.json");
        var output = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-v2-{Guid.NewGuid():N}.trrm");
        byte[] retained = [0xA5, 0x5A];
        try
        {
            await File.WriteAllBytesAsync(output, retained)
                .ConfigureAwait(true);

            var configuration = JsonSerializer.Serialize(new {
                schemaVersion = 2,
                providers = new[] {
                    new { schemaIdentity, path = providerPath },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity,
                        typeExportKeys = new[] { "type.box" },
                        fieldExportKeys = Array.Empty<string>(),
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections, configuration,
                new UTF8Encoding(false)).ConfigureAwait(true);
            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes("this is not valid Terrum"),
                "compile-trrm", "-", "--identity", "source-provider-cli-v2",
                "--trrm-schema", schemaIdentity,
                "--trrm-source-providers", providerSelections,
                "-o", output, "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(exit, Is.EqualTo(2),
                $"{result}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            using (var document = JsonDocument.Parse(result))
            {
                var diagnostic
                    = document.RootElement.GetProperty("diagnostics")[0];
                Assert.That(diagnostic.GetProperty("code").GetString(),
                    Is.EqualTo("TRM2001"));
                Assert.That(diagnostic.GetProperty("message").GetString(),
                    Does.Contain("not a regular file"));
            }
            Assert.That(await File.ReadAllBytesAsync(output)
                    .ConfigureAwait(true),
                Is.EqualTo(retained));

            var mismatchedConfiguration = JsonSerializer.Serialize(new {
                schemaVersion = 2,
                providers = new[] {
                    new { schemaIdentity, path = providerPath },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity,
                        typeExportKeys = new[] { "type.box" },
                        fieldExportKeys = Array.Empty<string>(),
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections,
                mismatchedConfiguration, new UTF8Encoding(false))
                .ConfigureAwait(true);
            var (mismatchExit, mismatchResult, mismatchError)
                = await RunAsync(
                    Encoding.UTF8.GetBytes("this is not valid Terrum"),
                    "compile-trrm", "-", "--identity",
                    "source-provider-cli-v2", "--trrm-schema",
                    nestedSchemaIdentity, "--trrm-source-providers",
                    providerSelections, "-o", output, "--runtime",
                    "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(mismatchExit, Is.EqualTo(2),
                $"{mismatchResult}{Environment.NewLine}{mismatchError}");
            Assert.That(mismatchError, Is.Empty);
            using (var document = JsonDocument.Parse(mismatchResult))
            {
                var diagnostic
                    = document.RootElement.GetProperty("diagnostics")[0];
                Assert.That(diagnostic.GetProperty("code").GetString(),
                    Is.EqualTo("TRM2000"));
                Assert.That(diagnostic.GetProperty("message").GetString(),
                    Does.Contain("schema version"));
            }
            Assert.That(await File.ReadAllBytesAsync(output)
                    .ConfigureAwait(true),
                Is.EqualTo(retained));

            var noncanonicalVersion = $$"""
                {"schemaVersion":2.0,"providers":[{"schemaIdentity":"{{schemaIdentity}}","path":"{{providerPath}}"}],"references":[{"providerInputOrdinal":0,"moduleIdentity":"{{moduleIdentity}}","typeExportKeys":["type.box"],"fieldExportKeys":[]}]}
                """;
            await File.WriteAllTextAsync(providerSelections,
                noncanonicalVersion, new UTF8Encoding(false))
                .ConfigureAwait(true);
            var (versionExit, versionResult, versionError)
                = await RunAsync(
                    Encoding.UTF8.GetBytes("this is not valid Terrum"),
                    "compile-trrm", "-", "--identity",
                    "source-provider-cli-v2", "--trrm-schema",
                    schemaIdentity, "--trrm-source-providers",
                    providerSelections, "-o", output, "--runtime",
                    "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(versionExit, Is.EqualTo(2),
                $"{versionResult}{Environment.NewLine}{versionError}");
            Assert.That(versionError, Is.Empty);
            using (var document = JsonDocument.Parse(versionResult))
            {
                var diagnostic
                    = document.RootElement.GetProperty("diagnostics")[0];
                Assert.That(diagnostic.GetProperty("code").GetString(),
                    Is.EqualTo("TRM2001"));
                Assert.That(diagnostic.GetProperty("message").GetString(),
                    Does.Contain("exact version tokens"));
            }
            Assert.That(await File.ReadAllBytesAsync(output)
                    .ConfigureAwait(true),
                Is.EqualTo(retained));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(output)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task CompileTrRmBindsVersionTwoC1ClassSourceProviderAsync()
    {
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";
        const string providerIdentity = "source-provider-cli-c1-provider";
        const string consumerIdentity = "source-provider-cli-c1-consumer";
        var providerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-c1-{Guid.NewGuid():N}.trrm");
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-c1-{Guid.NewGuid():N}.json");
        var consumerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-c1-{Guid.NewGuid():N}.trrm");
        try
        {
            var provider = CreateC1M1ClassProvider(providerIdentity, "Box",
                "type.box", schemaIdentity);
            await File.WriteAllBytesAsync(providerArtifact, provider)
                .ConfigureAwait(true);

            var configuration = JsonSerializer.Serialize(new {
                schemaVersion = 2,
                providers = new[] {
                    new { schemaIdentity, path = providerArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = providerIdentity,
                        typeExportKeys = new[] { "type.box" },
                        fieldExportKeys = Array.Empty<string>(),
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections, configuration,
                new UTF8Encoding(false)).ConfigureAwait(true);

            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes("void Consume(Box value) {}"),
                "compile-trrm", "-", "--identity", consumerIdentity,
                "--trrm-schema", schemaIdentity, "--trrm-source-providers",
                providerSelections, "--library", "-o", consumerArtifact,
                "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(exit, Is.Zero,
                $"{result}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            Assert.That(new FileInfo(consumerArtifact).Length, Is.GreaterThan(0));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(providerArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
        }
    }

    [Test]
    [TestCase(
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1")]
    [TestCase(
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1")]
    [TestCase(
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1-initializer-copy-evidence-1")]
    public async Task CompileTrRmBindsVersionThreeCombinedMethodTypeSourceProviderAsync(
        string schemaIdentity)
    {
        const string methodProviderIdentity
            = "source-provider-cli-method-type-methods";
        const string typeProviderIdentity
            = "source-provider-cli-method-type-types";
        const string consumerIdentity = "source-provider-cli-method-type-consumer";
        const string consumerSource = """
            public int32 Forward(Record value) { return Read(value); }
            public int32 ReadData(Record value) { return value.Data; }
            """;
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-method-type-{Guid.NewGuid():N}.json");
        var methodProviderArtifact = Path.Combine(
            TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-method-type-methods-{Guid.NewGuid():N}.trrm");
        var typeProviderArtifact = Path.Combine(
            TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-method-type-types-{Guid.NewGuid():N}.trrm");
        var consumerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-method-type-{Guid.NewGuid():N}.trrm");
        byte[] retained = [0xA5, 0x5A];
        try
        {
            var typeProviderTree = SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Record {
                    public int32 Data;
                }
                """);
            var typeProviderCompilation = Compilation.CreateLibrary(
                typeProviderTree,
                new CompilationOptions {
                    LayoutCoreRegistration
                        = LayoutCoreRegistrationTestData.Create(),
                });
            Assert.That(typeProviderCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    typeProviderCompilation.Diagnostics));
            var typeProviderProgram
                = (BoundProgram)typeProviderCompilation.Program!;
            var typeProviderArtifactBytes
                = TrRmExecutableCompiler.EmitStandard(
                typeProviderProgram, typeProviderIdentity, schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement, [
                    new TrRmSourceTypeExport("", "Record", "type.record") {
                        FieldExports = [
                            new TrRmSourceFieldExport("Data",
                                "field.record.data"),
                        ],
                    },
                ], [], []);
            await File.WriteAllBytesAsync(typeProviderArtifact,
                typeProviderArtifactBytes).ConfigureAwait(true);
            var typeProviderInput = new TrRmStandardArtifactInput(
                schemaIdentity, typeProviderArtifactBytes);
            var typeProviderReference
                = TrRmExecutableCompiler.CreateStandardReference(
                    typeProviderInput, typeProviderIdentity, [],
                    ["type.record"], ["field.record.data"]);

            var methodProviderTree = SyntaxTree.Parse("""
                public int32 Read(Record value) { return value.Data; }
                """);
            var methodProviderCompilation = Compilation.CreateLibrary(
                methodProviderTree, typeProviderReference);
            Assert.That(methodProviderCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    methodProviderCompilation.Diagnostics));
            var methodProviderProgram
                = (BoundProgram)methodProviderCompilation.Program!;
            var methodProviderArtifactBytes
                = TrRmExecutableCompiler.EmitStandard(
                    methodProviderProgram, methodProviderIdentity,
                    schemaIdentity,
                    TrRmModuleTableReader
                        .WindowsX64NativeTargetRequirement,
                    [], [], []);
            await File.WriteAllBytesAsync(methodProviderArtifact,
                methodProviderArtifactBytes).ConfigureAwait(true);

            var providerConfiguration = JsonSerializer.Serialize(new {
                schemaVersion = 3,
                providers = new[] {
                    new { schemaIdentity, path = typeProviderArtifact },
                    new { schemaIdentity, path = methodProviderArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 1,
                        moduleIdentity = methodProviderIdentity,
                        methodExportKeys = new[] { "Read" },
                        typeExportKeys = new[] {
                            new {
                                moduleIdentity = typeProviderIdentity,
                                exportKey = "type.record",
                            },
                        },
                        fieldExportKeys = new[] {
                            new {
                                moduleIdentity = typeProviderIdentity,
                                exportKey = "field.record.data",
                            },
                        },
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections,
                providerConfiguration, new UTF8Encoding(false))
                .ConfigureAwait(true);

            var (compileExit, compileOutput, compileError)
                = await RunAsync(Encoding.UTF8.GetBytes(consumerSource),
                    "compile-trrm", "-", "--identity", consumerIdentity,
                    "--trrm-schema", schemaIdentity,
                    "--trrm-source-providers", providerSelections,
                    "--library", "-o", consumerArtifact, "--runtime",
                    "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(compileExit, Is.Zero,
                $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var consumerTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(
                        File.ReadAllBytes(consumerArtifact),
                        TrRmArtifactSchema
                            .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft));
            var importRows = Enumerable.Range(1,
                    consumerTables.Imports.Count)
                .Select(row => (
                    Kind: consumerTables.Imports
                        .GetExpectedDefinitionForImportRow(row) >> 24,
                    ExportKey: consumerTables.Imports
                        .GetBindingForImportRow(row).ExportKey,
                    ProducerIdentity:
                        consumerTables.ModuleRefs.GetProducerIdentity(
                            consumerTables.Imports
                                .GetModuleRefForImportRow(row))))
                .ToArray();
            Assert.That(importRows.Where(static import =>
                    import.Kind == 0x13)
                .Select(static import => import.ExportKey),
                Is.EqualTo(["type.record"]));
            Assert.That(importRows.Where(static import =>
                    import.Kind == 0x14)
                .Select(static import => import.ExportKey),
                Is.EqualTo(["field.record.data"]));
            Assert.That(importRows.Where(static import =>
                    import.Kind == 0x16)
                .Select(static import => import.ExportKey),
                Is.EqualTo(["Read"]));
            Assert.That(importRows.Where(static import =>
                    import.Kind == 0x13 || import.Kind == 0x14)
                .Select(static import => import.ProducerIdentity).Distinct(),
                Is.EquivalentTo([typeProviderIdentity]));
            Assert.That(importRows.Where(static import => import.Kind == 0x16)
                .Select(static import => import.ProducerIdentity).Distinct(),
                Is.EquivalentTo([methodProviderIdentity]));
            var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
                File.ReadAllBytes(consumerArtifact));
            var linkedProgram = TrRmExecutableCompiler.LoadStandard(
                consumerInput, consumerIdentity, [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        methodProviderArtifactBytes),
                    typeProviderInput,
                ]);
            var sourceFreeArtifact = TrRmExecutableCompiler.EmitStandard(
                linkedProgram, consumerIdentity, schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
            Assert.That(sourceFreeArtifact,
                Is.EqualTo(consumerInput.Artifact.ToArray()));
            var reloadedProgram = TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    sourceFreeArtifact),
                consumerIdentity, [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        methodProviderArtifactBytes),
                    typeProviderInput,
                ]);
            Assert.That(reloadedProgram.Functions.Single(
                    static function => function.Name == "ReadData")
                    .ReturnType,
                Is.EqualTo(PrimitiveType.Int32));

            await File.WriteAllBytesAsync(consumerArtifact, retained)
                .ConfigureAwait(true);
            var mismatchedProviderConfiguration
                = JsonSerializer.Serialize(new {
                    schemaVersion = 3,
                    providers = new[] {
                        new {
                            schemaIdentity =
                                "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1",
                            path = typeProviderArtifact,
                        },
                        new {
                            schemaIdentity,
                            path = methodProviderArtifact,
                        },
                    },
                    references = new[] {
                        new {
                            providerInputOrdinal = 1,
                            moduleIdentity = methodProviderIdentity,
                            methodExportKeys = new[] { "Read" },
                            typeExportKeys = Array.Empty<object>(),
                            fieldExportKeys = Array.Empty<object>(),
                        },
                    },
                });
            await File.WriteAllTextAsync(providerSelections,
                mismatchedProviderConfiguration, new UTF8Encoding(false))
                .ConfigureAwait(true);
            var (mismatchExit, mismatchOutput, mismatchError)
                = await RunAsync(
                    Encoding.UTF8.GetBytes(consumerSource), "compile-trrm",
                    "-", "--identity", consumerIdentity, "--trrm-schema",
                    schemaIdentity, "--trrm-source-providers",
                    providerSelections, "--library", "-o", consumerArtifact,
                    "--runtime", "fail-fast", "--json")
                    .ConfigureAwait(false);
            Assert.That(mismatchExit, Is.EqualTo(2),
                $"{mismatchOutput}{Environment.NewLine}{mismatchError}");
            Assert.That(mismatchError, Is.Empty);
            using (var document = JsonDocument.Parse(mismatchOutput))
            {
                Assert.That(document.RootElement
                        .GetProperty("diagnostic").GetProperty("code")
                        .GetString(),
                    Is.EqualTo("TRM2000"));
            }
            Assert.That(await File.ReadAllBytesAsync(consumerArtifact)
                .ConfigureAwait(true), Is.EqualTo(retained));
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
            var (absentExit, absentOutput, absentError)
                = await RunAsync(
                    Encoding.UTF8.GetBytes("public int32 Invalid("),
                    "compile-trrm", "-", "--identity", consumerIdentity,
                    "--trrm-schema", schemaIdentity,
                    "--trrm-source-providers", providerSelections,
                    "--library", "-o", consumerArtifact, "--runtime",
                    "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(absentExit, Is.EqualTo(2),
                $"{absentOutput}{Environment.NewLine}{absentError}");
            Assert.That(absentError, Is.Empty);
            using (var document = JsonDocument.Parse(absentOutput))
            {
                Assert.That(document.RootElement
                        .GetProperty("diagnostic").GetProperty("code")
                        .GetString(),
                    Is.EqualTo("TRM2000"));
            }
            Assert.That(File.Exists(consumerArtifact), Is.False);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(methodProviderArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(typeProviderArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task CompileTrRmBindsVersionThreeScalarOnlySourceProviderAsync()
    {
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1";
        const string providerIdentity = "source-provider-cli-scalar-method";
        const string consumerIdentity = "source-provider-cli-scalar-consumer";
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-scalar-method-{Guid.NewGuid():N}.json");
        var providerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-scalar-method-{Guid.NewGuid():N}.trrm");
        var duplicateProviderArtifact = Path.Combine(
            TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-scalar-method-copy-{Guid.NewGuid():N}.trrm");
        var consumerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-scalar-method-{Guid.NewGuid():N}.trrm");
        var manifestOutputAlias = Path.Combine(
            TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-scalar-manifest-{Guid.NewGuid():N}.trrm");
        string? providerHardLink = null;
        try
        {
            var providerCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse("public void Ping() {}"));
            Assert.That(providerCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    providerCompilation.Diagnostics));
            var providerArtifactBytes = TrRmExecutableCompiler.EmitStandard(
                (BoundProgram)providerCompilation.Program!, providerIdentity,
                schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
            await File.WriteAllBytesAsync(providerArtifact,
                providerArtifactBytes).ConfigureAwait(true);
            var providerConfiguration = JsonSerializer.Serialize(new {
                schemaVersion = 3,
                providers = new[] {
                    new { schemaIdentity, path = providerArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = providerIdentity,
                        methodExportKeys = new[] { "Ping" },
                        typeExportKeys = Array.Empty<object>(),
                        fieldExportKeys = Array.Empty<object>(),
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections,
                providerConfiguration, new UTF8Encoding(false))
                .ConfigureAwait(true);

            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes("public void Forward() { Ping(); }"),
                "compile-trrm", "-", "--identity", consumerIdentity,
                "--trrm-schema", schemaIdentity, "--trrm-source-providers",
                providerSelections, "--library", "-o", consumerArtifact,
                "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(exit, Is.Zero,
                $"{result}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);

            var tables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    File.ReadAllBytes(consumerArtifact),
                    TrRmArtifactSchema
                        .C1M1Construction2InitializerCopyEvidenceDraft));
            Assert.That(tables.Imports.Count, Is.EqualTo(1));
            Assert.That(tables.Imports.GetExpectedDefinitionForImportRow(1)
                    >> 24,
                Is.EqualTo(0x16u));
            Assert.That(tables.Imports.GetBindingForImportRow(1).ExportKey,
                Is.EqualTo("Ping"));
            Assert.That(tables.ModuleRefs.GetProducerIdentity(
                    tables.Imports.GetModuleRefForImportRow(1)),
                Is.EqualTo(providerIdentity));

            await File.WriteAllBytesAsync(consumerArtifact, retained)
                .ConfigureAwait(true);
            var (wrongOutputSchemaExit, wrongOutputSchemaOutput,
                wrongOutputSchemaError) = await RunAsync(
                    Encoding.UTF8.GetBytes("invalid Terrum source"),
                    "compile-trrm", "-", "--identity", consumerIdentity,
                    "--trrm-schema",
                    "trrm/1-draft-init-reference-bodies-local-field-layout-2",
                    "--trrm-source-providers", providerSelections,
                    "--library", "-o", consumerArtifact, "--runtime",
                    "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(wrongOutputSchemaExit, Is.EqualTo(2),
                $"{wrongOutputSchemaOutput}{Environment.NewLine}{wrongOutputSchemaError}");
            Assert.That(wrongOutputSchemaError, Is.Empty);
            using (var document = JsonDocument.Parse(wrongOutputSchemaOutput))
            {
                Assert.That(document.RootElement
                        .GetProperty("diagnostic").GetProperty("code")
                        .GetString(),
                    Is.EqualTo("TRM2000"));
            }
            Assert.That(await File.ReadAllBytesAsync(consumerArtifact)
                .ConfigureAwait(true), Is.EqualTo(retained));

            await File.WriteAllBytesAsync(manifestOutputAlias,
                await File.ReadAllBytesAsync(providerSelections)
                    .ConfigureAwait(true)).ConfigureAwait(true);
            await File.WriteAllBytesAsync(consumerArtifact, retained)
                .ConfigureAwait(true);

            async Task AssertAliasFailureAsync(string sourcePath,
                string configurationPath, string outputPath,
                string preservedPath)
            {
                var before = await File.ReadAllBytesAsync(preservedPath)
                    .ConfigureAwait(true);
                var (aliasExit, aliasOutput, aliasError) = await RunAsync([],
                    "compile-trrm", sourcePath, "--identity", consumerIdentity,
                    "--trrm-schema", schemaIdentity, "--trrm-source-providers",
                    configurationPath, "--library", "-o", outputPath,
                    "--runtime", "fail-fast", "--json")
                    .ConfigureAwait(false);
                Assert.That(aliasExit, Is.EqualTo(2),
                    $"{aliasOutput}{Environment.NewLine}{aliasError}");
                Assert.That(aliasError, Is.Empty);
                using (var document = JsonDocument.Parse(aliasOutput))
                {
                    Assert.That(document.RootElement
                            .GetProperty("diagnostic").GetProperty("code")
                            .GetString(),
                        Is.EqualTo("TRM2002"));
                }
                Assert.That(await File.ReadAllBytesAsync(preservedPath)
                    .ConfigureAwait(true), Is.EqualTo(before));
            }

            await File.WriteAllBytesAsync(duplicateProviderArtifact,
                await File.ReadAllBytesAsync(providerArtifact)
                    .ConfigureAwait(true)).ConfigureAwait(true);
            await File.WriteAllBytesAsync(consumerArtifact, retained)
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(providerSelections,
                JsonSerializer.Serialize(new {
                    schemaVersion = 3,
                    providers = new[] {
                        new { schemaIdentity, path = providerArtifact },
                        new {
                            schemaIdentity,
                            path = duplicateProviderArtifact,
                        },
                    },
                    references = new[] {
                        new {
                            providerInputOrdinal = 0,
                            moduleIdentity = providerIdentity,
                            methodExportKeys = new[] { "Ping" },
                            typeExportKeys = Array.Empty<object>(),
                            fieldExportKeys = Array.Empty<object>(),
                        },
                    },
                }), new UTF8Encoding(false)).ConfigureAwait(true);
            var (duplicateModuleExit, duplicateModuleOutput,
                duplicateModuleError) = await RunAsync(
                    Encoding.UTF8.GetBytes("invalid Terrum source"),
                    "compile-trrm", "-", "--identity", consumerIdentity,
                    "--trrm-schema", schemaIdentity,
                    "--trrm-source-providers", providerSelections,
                    "--library", "-o", consumerArtifact, "--runtime",
                    "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(duplicateModuleExit, Is.EqualTo(1),
                $"{duplicateModuleOutput}{Environment.NewLine}{duplicateModuleError}");
            Assert.That(duplicateModuleError, Is.Empty);
            using (var document = JsonDocument.Parse(duplicateModuleOutput))
            {
                Assert.That(document.RootElement
                        .GetProperty("diagnostic").GetProperty("code")
                        .GetString(),
                    Is.EqualTo("TRM5001"));
            }
            Assert.That(await File.ReadAllBytesAsync(consumerArtifact)
                .ConfigureAwait(true), Is.EqualTo(retained));
            await File.WriteAllTextAsync(providerSelections,
                providerConfiguration, new UTF8Encoding(false))
                .ConfigureAwait(true);

            await AssertAliasFailureAsync("-", manifestOutputAlias,
                manifestOutputAlias, manifestOutputAlias)
                .ConfigureAwait(true);
            await AssertAliasFailureAsync(providerSelections,
                providerSelections, consumerArtifact, consumerArtifact)
                .ConfigureAwait(true);
            await AssertAliasFailureAsync("-", providerSelections,
                providerArtifact, providerArtifact).ConfigureAwait(true);
            await File.WriteAllTextAsync(providerSelections,
                JsonSerializer.Serialize(new {
                    schemaVersion = 3,
                    providers = new[] {
                        new { schemaIdentity, path = providerArtifact },
                        new { schemaIdentity, path = providerArtifact },
                    },
                    references = new[] {
                        new {
                            providerInputOrdinal = 0,
                            moduleIdentity = providerIdentity,
                            methodExportKeys = new[] { "Ping" },
                            typeExportKeys = Array.Empty<object>(),
                            fieldExportKeys = Array.Empty<object>(),
                        },
                    },
                }), new UTF8Encoding(false)).ConfigureAwait(true);
            await AssertAliasFailureAsync("-", providerSelections,
                consumerArtifact, consumerArtifact).ConfigureAwait(true);
            await File.WriteAllTextAsync(providerSelections,
                providerConfiguration, new UTF8Encoding(false))
                .ConfigureAwait(true);
            await AssertAliasFailureAsync(providerArtifact, providerSelections,
                consumerArtifact, consumerArtifact).ConfigureAwait(true);

            if (OperatingSystem.IsWindows())
            {
                var caseVariantConfiguration = JsonSerializer.Serialize(new {
                    schemaVersion = 3,
                    providers = new[] {
                        new {
                            schemaIdentity,
                            path = Path.GetFullPath(providerArtifact)
                                .ToUpperInvariant(),
                        },
                    },
                    references = new[] {
                        new {
                            providerInputOrdinal = 0,
                            moduleIdentity = providerIdentity,
                            methodExportKeys = new[] { "Ping" },
                            typeExportKeys = Array.Empty<object>(),
                            fieldExportKeys = Array.Empty<object>(),
                        },
                    },
                });
                await File.WriteAllTextAsync(providerSelections,
                    caseVariantConfiguration, new UTF8Encoding(false))
                    .ConfigureAwait(true);
                await AssertAliasFailureAsync("-", providerSelections,
                    providerArtifact, providerArtifact).ConfigureAwait(true);

                await File.WriteAllTextAsync(providerSelections,
                    providerConfiguration, new UTF8Encoding(false))
                    .ConfigureAwait(true);
                providerHardLink = Path.Combine(
                    TestArtifactPaths.DirectoryPath,
                    $"source-provider-cli-scalar-hardlink-{Guid.NewGuid():N}.trrm");
                if (!CreateHardLink(providerHardLink, providerArtifact,
                        IntPtr.Zero))
                {
                    throw new IOException(
                        "Could not create the source provider test hard link.",
                        new System.ComponentModel.Win32Exception(
                            System.Runtime.InteropServices.Marshal
                                .GetLastWin32Error()));
                }

                await AssertAliasFailureAsync("-", providerSelections,
                    providerHardLink, providerArtifact).ConfigureAwait(true);
            }

            var invalidConfigurations = new[] {
                JsonSerializer.Serialize(new {
                    schemaVersion = 3,
                    providers = new[] {
                        new { schemaIdentity, path = providerArtifact },
                    },
                    references = new[] {
                        new {
                            providerInputOrdinal = 0,
                            moduleIdentity = providerIdentity,
                            typeExportKeys = Array.Empty<object>(),
                            fieldExportKeys = Array.Empty<object>(),
                        },
                    },
                }),
                JsonSerializer.Serialize(new {
                    schemaVersion = 3,
                    providers = new[] {
                        new { schemaIdentity, path = providerArtifact },
                    },
                    references = new[] {
                        new {
                            providerInputOrdinal = 0,
                            moduleIdentity = providerIdentity,
                            methodExportKeys = new[] { "Ping" },
                            typeExportKeys = Array.Empty<object>(),
                            fieldExportKeys = Array.Empty<object>(),
                        },
                        new {
                            providerInputOrdinal = 0,
                            moduleIdentity = providerIdentity,
                            methodExportKeys = new[] { "Ping" },
                            typeExportKeys = Array.Empty<object>(),
                            fieldExportKeys = Array.Empty<object>(),
                        },
                    },
                }),
                JsonSerializer.Serialize(new {
                    schemaVersion = 3,
                    providers = new[] {
                        new { schemaIdentity, path = providerArtifact },
                    },
                    references = new[] {
                        new {
                            providerInputOrdinal = 0,
                            moduleIdentity = providerIdentity,
                            methodExportKeys = new[] { "Ping" },
                            typeExportKeys = new[] {
                                new {
                                    moduleIdentity = providerIdentity,
                                    exportKey = "same.key",
                                },
                            },
                            fieldExportKeys = new[] {
                                new {
                                    moduleIdentity = providerIdentity,
                                    exportKey = "same.key",
                                },
                            },
                        },
                    },
                }),
                $$"""
                    {
                      "schemaVersion": 3.0,
                      "providers": [{
                        "schemaIdentity": {{JsonSerializer.Serialize(schemaIdentity)}},
                        "path": {{JsonSerializer.Serialize(providerArtifact)}}
                      }],
                      "references": [{
                        "providerInputOrdinal": 0,
                        "moduleIdentity": {{JsonSerializer.Serialize(providerIdentity)}},
                        "methodExportKeys": ["Ping"],
                        "typeExportKeys": [],
                        "fieldExportKeys": []
                      }]
                    }
                    """,
            };
            foreach (var invalidConfiguration in invalidConfigurations)
            {
                await File.WriteAllBytesAsync(consumerArtifact, retained)
                    .ConfigureAwait(true);
                await File.WriteAllTextAsync(providerSelections,
                    invalidConfiguration, new UTF8Encoding(false))
                    .ConfigureAwait(true);
                var (invalidExit, invalidResult, invalidError)
                    = await RunAsync(
                        Encoding.UTF8.GetBytes(
                            "public void Forward() { Ping(); }"),
                        "compile-trrm", "-", "--identity", consumerIdentity,
                        "--trrm-schema", schemaIdentity,
                        "--trrm-source-providers", providerSelections,
                        "--library", "-o", consumerArtifact, "--runtime",
                        "fail-fast", "--json").ConfigureAwait(false);
                Assert.That(invalidExit, Is.EqualTo(2),
                    $"{invalidResult}{Environment.NewLine}{invalidError}");
                Assert.That(invalidError, Is.Empty);
                using (var document = JsonDocument.Parse(invalidResult))
                {
                    Assert.That(document.RootElement
                            .GetProperty("diagnostic").GetProperty("code")
                            .GetString(),
                        Is.EqualTo("TRM2001"));
                }
                Assert.That(await File.ReadAllBytesAsync(consumerArtifact)
                    .ConfigureAwait(true), Is.EqualTo(retained));
            }

            await File.WriteAllTextAsync(providerSelections,
                invalidConfigurations[0], new UTF8Encoding(false))
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
            var (absentInvalidExit, absentInvalidResult,
                absentInvalidError) = await RunAsync(
                    Encoding.UTF8.GetBytes(
                        "public void Forward() { Ping(); }"),
                    "compile-trrm", "-", "--identity", consumerIdentity,
                    "--trrm-schema", schemaIdentity,
                    "--trrm-source-providers", providerSelections,
                    "--library", "-o", consumerArtifact, "--runtime",
                    "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(absentInvalidExit, Is.EqualTo(2),
                $"{absentInvalidResult}{Environment.NewLine}{absentInvalidError}");
            Assert.That(absentInvalidError, Is.Empty);
            using (var document = JsonDocument.Parse(absentInvalidResult))
            {
                Assert.That(document.RootElement
                        .GetProperty("diagnostic").GetProperty("code")
                        .GetString(),
                    Is.EqualTo("TRM2001"));
            }
            Assert.That(File.Exists(consumerArtifact), Is.False);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(manifestOutputAlias)
                .ConfigureAwait(true);
            if (providerHardLink is not null)
            {
                await TestFileCleanup.DeleteWithRetryAsync(providerHardLink)
                    .ConfigureAwait(true);
            }
            await TestFileCleanup.DeleteWithRetryAsync(providerArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(
                duplicateProviderArtifact).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task CompileTrRmSelectsNonFirstCoLocatedMethodProviderModuleAsync()
    {
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1";
        const string typeModuleIdentity = "source-provider-cli-colocated-types";
        const string methodModuleIdentity = "source-provider-cli-colocated-methods";
        const string consumerIdentity = "source-provider-cli-colocated-consumer";
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-colocated-{Guid.NewGuid():N}.json");
        var providerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-colocated-{Guid.NewGuid():N}.trrm");
        var consumerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-colocated-{Guid.NewGuid():N}.trrm");
        try
        {
            var providerTree = SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Record {
                    public int32 Data;
                }
                public void Ping() {}
                """);
            var providerCompilation = Compilation.CreateLibrary(providerTree,
                new CompilationOptions {
                    LayoutCoreRegistration
                        = LayoutCoreRegistrationTestData.Create(),
                });
            Assert.That(providerCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    providerCompilation.Diagnostics));
            var providerProgram = (BoundProgram)providerCompilation.Program!;
            var providerArtifactBytes = TrRmExecutableCompiler.EmitStandard(
                providerProgram, typeModuleIdentity, schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement, [
                    new TrRmSourceTypeExport("", "Record", "type.record") {
                        FieldExports = [
                            new TrRmSourceFieldExport("Data",
                                "field.record.data"),
                        ],
                    },
                ], [], []);
            providerArtifactBytes = MoveMethodsToSecondModule(
                providerArtifactBytes, typeModuleIdentity,
                methodModuleIdentity);
            await File.WriteAllBytesAsync(providerArtifact,
                providerArtifactBytes).ConfigureAwait(true);

            var providerConfiguration = JsonSerializer.Serialize(new {
                schemaVersion = 3,
                providers = new[] {
                    new { schemaIdentity, path = providerArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = methodModuleIdentity,
                        methodExportKeys = new[] { "Ping" },
                        typeExportKeys = new[] {
                            new {
                                moduleIdentity = typeModuleIdentity,
                                exportKey = "type.record",
                            },
                        },
                        fieldExportKeys = new[] {
                            new {
                                moduleIdentity = typeModuleIdentity,
                                exportKey = "field.record.data",
                            },
                        },
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections,
                providerConfiguration, new UTF8Encoding(false))
                .ConfigureAwait(true);

            var consumerSource = """
                public void Forward() { Ping(); }
                public int32 ReadData(Record value) { return value.Data; }
                """;
            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes(consumerSource), "compile-trrm", "-",
                "--identity", consumerIdentity, "--trrm-schema", schemaIdentity,
                "--trrm-source-providers", providerSelections, "--library",
                "-o", consumerArtifact, "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(exit, Is.Zero,
                $"{result}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);

            var tables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                    File.ReadAllBytes(consumerArtifact),
                    TrRmArtifactSchema
                        .C1M1Construction2InitializerCopyEvidenceDraft));
            Assert.That(tables.Modules.Count, Is.EqualTo(1));
            foreach (var (kind, exportKey, expectedModuleIdentity) in new[] {
                (0x13u, "type.record", typeModuleIdentity),
                (0x14u, "field.record.data", typeModuleIdentity),
                (0x16u, "Ping", methodModuleIdentity),
            })
            {
                var importRow = Enumerable.Range(1, tables.Imports.Count)
                    .Single(row =>
                        tables.Imports.GetExpectedDefinitionForImportRow(row)
                            >> 24 == kind
                        && StringComparer.Ordinal.Equals(
                            tables.Imports.GetBindingForImportRow(row)
                                .ExportKey,
                            exportKey));
                Assert.That(tables.ModuleRefs.GetProducerIdentity(
                        tables.Imports.GetModuleRefForImportRow(importRow)),
                    Is.EqualTo(expectedModuleIdentity));
            }

            var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
                File.ReadAllBytes(consumerArtifact));
            var providers = new[] {
                new TrRmStandardArtifactInput(schemaIdentity,
                    providerArtifactBytes),
            };
            var linkedProgram = TrRmExecutableCompiler.LoadStandard(
                consumerInput, consumerIdentity, providers);
            var sourceFreeArtifact = TrRmExecutableCompiler.EmitStandard(
                linkedProgram, consumerIdentity, schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
            Assert.That(sourceFreeArtifact,
                Is.EqualTo(consumerInput.Artifact.ToArray()));
            var reloadedProgram = TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    sourceFreeArtifact),
                consumerIdentity, providers);
            Assert.That(reloadedProgram.Functions.Single(
                    static function => function.Name == "ReadData")
                    .ReturnType,
                Is.EqualTo(PrimitiveType.Int32));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(providerArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
        }
    }

    [TestCase(
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1")]
    [TestCase(
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1")]
    public async Task CompileTrRmBindsVersionTwoCM1ClassSourceProviderAsync(
        string schemaIdentity)
    {
        var providerIdentity
            = $"source-provider-cli-cm1-{Guid.NewGuid():N}";
        var providerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-cm1-{Guid.NewGuid():N}.trrm");
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-cm1-{Guid.NewGuid():N}.json");
        var consumerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-cm1-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(providerArtifact,
                CreateC1M1ClassProviderWithField(providerIdentity, "Box",
                    "type.box", "field.box.value", schemaIdentity))
                .ConfigureAwait(true);
            var configuration = JsonSerializer.Serialize(new {
                schemaVersion = 2,
                providers = new[] {
                    new { schemaIdentity, path = providerArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = providerIdentity,
                        typeExportKeys = new[] { "type.box" },
                        fieldExportKeys = new[] { "field.box.value" },
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections, configuration,
                new UTF8Encoding(false)).ConfigureAwait(true);

            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes("void Consume(Box value) {}"),
                "compile-trrm", "-", "--identity",
                $"source-provider-cli-cm1-consumer-{Guid.NewGuid():N}",
                "--trrm-schema", schemaIdentity, "--trrm-source-providers",
                providerSelections, "--library", "-o", consumerArtifact,
                "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(exit, Is.Zero,
                $"{result}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            Assert.That(new FileInfo(consumerArtifact).Length, Is.GreaterThan(0));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(providerArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task CompileTrRmBindsVersionTwoEN1ClassFieldSelectionAsync()
    {
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1";
        var providerIdentity
            = $"source-provider-cli-en1-{Guid.NewGuid():N}";
        var providerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-en1-{Guid.NewGuid():N}.trrm");
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-en1-{Guid.NewGuid():N}.json");
        var consumerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-en1-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(providerArtifact,
                CreateC1M1ClassProviderWithField(providerIdentity, "Box",
                    "type.box", "field.box.value", schemaIdentity))
                .ConfigureAwait(true);
            var configuration = JsonSerializer.Serialize(new {
                schemaVersion = 2,
                providers = new[] {
                    new { schemaIdentity, path = providerArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = providerIdentity,
                        typeExportKeys = new[] { "type.box" },
                        fieldExportKeys = new[] { "field.box.value" },
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections, configuration,
                new UTF8Encoding(false)).ConfigureAwait(true);

            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes("void Consume(Box value) {}"),
                "compile-trrm", "-", "--identity",
                $"source-provider-cli-en1-consumer-{Guid.NewGuid():N}",
                "--trrm-schema", schemaIdentity, "--trrm-source-providers",
                providerSelections, "--library", "-o", consumerArtifact,
                "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(exit, Is.Zero,
                $"{result}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            Assert.That(new FileInfo(consumerArtifact).Length, Is.GreaterThan(0));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(providerArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
        }
    }

    [TestCase(
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1")]
    [TestCase(
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1")]
    public async Task CompileTrRmBindsVersionTwoEN1ClassLayoutProviderAsync(
        string schemaIdentity)
    {
        var providerIdentity
            = $"source-provider-cli-en1-layout-{Guid.NewGuid():N}";
        var providerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-en1-layout-{Guid.NewGuid():N}.trrm");
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-en1-layout-{Guid.NewGuid():N}.json");
        var consumerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-en1-layout-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(providerArtifact,
                CreateC1M1ClassProviderWithField(providerIdentity, "Box",
                    "type.box", "field.box.value", schemaIdentity))
                .ConfigureAwait(true);
            var configuration = JsonSerializer.Serialize(new {
                schemaVersion = 2,
                providers = new[] {
                    new { schemaIdentity, path = providerArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = providerIdentity,
                        typeExportKeys = new[] { "type.box" },
                        fieldExportKeys = new[] { "field.box.value" },
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections, configuration,
                new UTF8Encoding(false)).ConfigureAwait(true);

            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes("void Consume(Box value) {}"),
                "compile-trrm", "-", "--identity",
                $"source-provider-cli-en1-layout-consumer-{Guid.NewGuid():N}",
                "--trrm-schema", schemaIdentity, "--trrm-source-providers",
                providerSelections, "--library", "-o", consumerArtifact,
                "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(exit, Is.Zero,
                $"{result}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            Assert.That(new FileInfo(consumerArtifact).Length, Is.GreaterThan(0));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(providerArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task CompileTrRmBindsVersionTwoC1ClassFieldSelectionAsync()
    {
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";
        const string providerIdentity
            = "source-provider-cli-c1-field-provider";
        const string consumerIdentity
            = "source-provider-cli-c1-field-consumer";
        var providerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-c1-field-{Guid.NewGuid():N}.trrm");
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-c1-field-{Guid.NewGuid():N}.json");
        var consumerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-c1-field-{Guid.NewGuid():N}.trrm");
        byte[] retained = [0xA5, 0x5A];
        try
        {
            await File.WriteAllBytesAsync(providerArtifact,
                CreateC1M1ClassProviderWithField(providerIdentity, "Box",
                    "type.box", "field.box.value", schemaIdentity))
                .ConfigureAwait(true);
            var configuration = JsonSerializer.Serialize(new {
                schemaVersion = 2,
                providers = new[] {
                    new { schemaIdentity, path = providerArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = providerIdentity,
                        typeExportKeys = new[] { "type.box" },
                        fieldExportKeys = new[] { "field.box.value" },
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections, configuration,
                new UTF8Encoding(false)).ConfigureAwait(true);

            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes("void Consume(Box value) {}"),
                "compile-trrm", "-", "--identity", consumerIdentity,
                "--trrm-schema", schemaIdentity, "--trrm-source-providers",
                providerSelections, "--library", "-o", consumerArtifact,
                "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(exit, Is.Zero,
                $"{result}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);

            await File.WriteAllBytesAsync(consumerArtifact, retained)
                .ConfigureAwait(true);
            var invalidConfiguration = JsonSerializer.Serialize(new {
                schemaVersion = 2,
                providers = new[] {
                    new { schemaIdentity, path = providerArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = providerIdentity,
                        typeExportKeys = new[] { "type.box" },
                        fieldExportKeys = new[] { "field.box.missing" },
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections,
                invalidConfiguration, new UTF8Encoding(false))
                .ConfigureAwait(true);
            var (invalidExit, invalidResult, invalidError)
                = await RunAsync(
                    Encoding.UTF8.GetBytes("this is not valid Terrum"),
                    "compile-trrm", "-", "--identity", consumerIdentity,
                    "--trrm-schema", schemaIdentity,
                    "--trrm-source-providers", providerSelections,
                    "--library", "-o", consumerArtifact, "--runtime",
                    "fail-fast", "--json").ConfigureAwait(false);
            Assert.That(invalidExit, Is.EqualTo(2),
                $"{invalidResult}{Environment.NewLine}{invalidError}");
            Assert.That(invalidError, Is.Empty);
            using (var document = JsonDocument.Parse(invalidResult))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2001"));
            }
            Assert.That(await File.ReadAllBytesAsync(consumerArtifact)
                    .ConfigureAwait(true),
                Is.EqualTo(retained));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(providerArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
        }
    }

    [TestCase(
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1")]
    [TestCase(
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1")]
    [TestCase(
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1")]
    [TestCase(
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1")]
    public async Task CompileTrRmComposesVersionTwoC1ClassSourceProvidersAsync(
        string schemaIdentity)
    {
        const string firstProviderIdentity
            = "source-provider-cli-c1-first-provider";
        const string secondProviderIdentity
            = "source-provider-cli-c1-second-provider";
        var firstProviderArtifact = Path.Combine(
            TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-c1-first-{Guid.NewGuid():N}.trrm");
        var secondProviderArtifact = Path.Combine(
            TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-c1-second-{Guid.NewGuid():N}.trrm");
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-c1-composed-{Guid.NewGuid():N}.json");
        var consumerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-c1-composed-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(firstProviderArtifact,
                CreateC1M1ClassProviderWithField(firstProviderIdentity,
                    "Box", "type.box", "field.box.value", schemaIdentity))
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(secondProviderArtifact,
                CreateC1M1ClassProviderWithField(secondProviderIdentity,
                    "Other", "type.other", "field.other.value",
                    schemaIdentity)).ConfigureAwait(true);
            var configuration = JsonSerializer.Serialize(new {
                schemaVersion = 2,
                providers = new[] {
                    new { schemaIdentity, path = firstProviderArtifact },
                    new { schemaIdentity, path = secondProviderArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = firstProviderIdentity,
                        typeExportKeys = new[] { "type.box" },
                        fieldExportKeys = new[] { "field.box.value" },
                    },
                    new {
                        providerInputOrdinal = 1,
                        moduleIdentity = secondProviderIdentity,
                        typeExportKeys = new[] { "type.other" },
                        fieldExportKeys = new[] { "field.other.value" },
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections, configuration,
                new UTF8Encoding(false)).ConfigureAwait(true);

            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes(
                    "void Consume(Box box, Other other) {}"),
                "compile-trrm", "-", "--identity",
                "source-provider-cli-c1-composed-consumer",
                "--trrm-schema", schemaIdentity, "--trrm-source-providers",
                providerSelections, "--library", "-o", consumerArtifact,
                "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(exit, Is.Zero,
                $"{result}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            Assert.That(new FileInfo(consumerArtifact).Length, Is.GreaterThan(0));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(firstProviderArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(secondProviderArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
        }
    }

    [TestCase(
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1")]
    [TestCase(
        "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1")]
    public async Task CompileTrRmComposesVersionTwoConstructionClassProvidersAsync(
        string schemaIdentity)
    {
        var firstProviderIdentity
            = $"source-provider-cli-construction-first-{Guid.NewGuid():N}";
        var secondProviderIdentity
            = $"source-provider-cli-construction-second-{Guid.NewGuid():N}";
        var firstProviderArtifact = Path.Combine(
            TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-construction-first-{Guid.NewGuid():N}.trrm");
        var secondProviderArtifact = Path.Combine(
            TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-construction-second-{Guid.NewGuid():N}.trrm");
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-construction-{Guid.NewGuid():N}.json");
        var consumerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-construction-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(firstProviderArtifact,
                CreateC1M1ClassProvider(firstProviderIdentity, "Box",
                    "type.box", schemaIdentity)).ConfigureAwait(true);
            await File.WriteAllBytesAsync(secondProviderArtifact,
                CreateC1M1ClassProvider(secondProviderIdentity, "Other",
                    "type.other", schemaIdentity)).ConfigureAwait(true);
            var configuration = JsonSerializer.Serialize(new {
                schemaVersion = 2,
                providers = new[] {
                    new { schemaIdentity, path = firstProviderArtifact },
                    new { schemaIdentity, path = secondProviderArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = firstProviderIdentity,
                        typeExportKeys = new[] { "type.box" },
                        fieldExportKeys = Array.Empty<string>(),
                    },
                    new {
                        providerInputOrdinal = 1,
                        moduleIdentity = secondProviderIdentity,
                        typeExportKeys = new[] { "type.other" },
                        fieldExportKeys = Array.Empty<string>(),
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections, configuration,
                new UTF8Encoding(false)).ConfigureAwait(true);

            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes(
                    "void Consume(Box box, Other other) {}"),
                "compile-trrm", "-", "--identity",
                $"source-provider-cli-construction-consumer-{Guid.NewGuid():N}",
                "--trrm-schema", schemaIdentity, "--trrm-source-providers",
                providerSelections, "--library", "-o", consumerArtifact,
                "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(exit, Is.Zero,
                $"{result}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            Assert.That(new FileInfo(consumerArtifact).Length, Is.GreaterThan(0));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(firstProviderArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(secondProviderArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task CompileTrRmBindsVersionTwoConstructionClassMethodProviderAsync()
    {
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1";
        const string providerIdentity
            = "source-provider-cli-construction-method-provider";
        var providerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-construction-method-{Guid.NewGuid():N}.trrm");
        var providerSelections = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-construction-method-{Guid.NewGuid():N}.json");
        var consumerArtifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"source-provider-cli-construction-method-{Guid.NewGuid():N}.trrm");
        try
        {
            var provider = CreateConstructionClassMethodProvider(
                providerIdentity,
                "Box", "type.box", "Box.Read", schemaIdentity);
            var reference = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, provider),
                providerIdentity, [], ["type.box"]);
            var method = reference.Functions.Single(function =>
                function.Name == "Box.Read");
            Assert.That(method.Variables[1].Name, Is.EqualTo("value"));
            await File.WriteAllBytesAsync(providerArtifact, provider)
                .ConfigureAwait(true);
            var configuration = JsonSerializer.Serialize(new {
                schemaVersion = 2,
                providers = new[] {
                    new { schemaIdentity, path = providerArtifact },
                },
                references = new[] {
                    new {
                        providerInputOrdinal = 0,
                        moduleIdentity = providerIdentity,
                        typeExportKeys = new[] { "type.box" },
                        fieldExportKeys = Array.Empty<string>(),
                    },
                },
            });
            await File.WriteAllTextAsync(providerSelections, configuration,
                new UTF8Encoding(false)).ConfigureAwait(true);

            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes("void Consume(Box value) {}"),
                "compile-trrm", "-", "--identity",
                "source-provider-cli-construction-method-consumer",
                "--trrm-schema", schemaIdentity, "--trrm-source-providers",
                providerSelections, "--library", "-o", consumerArtifact,
                "--runtime", "fail-fast", "--json")
                .ConfigureAwait(false);
            Assert.That(exit, Is.Zero,
                $"{result}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            Assert.That(new FileInfo(consumerArtifact).Length, Is.GreaterThan(0));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(providerArtifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(providerSelections)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(consumerArtifact)
                .ConfigureAwait(true);
        }
    }

    [Test]
    [TestCase(false, false, "Box")]
    [TestCase(true, false, "write Box")]
    [TestCase(true, true, "write Box")]
    public void EmitsConstruction2OrdinaryClassMethodCallBody(
        bool mutableType, bool mutableReceiver, string receiverType)
    {
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2";
        const string providerIdentity
            = "construction2-ordinary-call-provider";
        var provider = CreateConstructionClassMethodProvider(
            providerIdentity, "Box", "type.box", "Box.Read",
            schemaIdentity, mutableType: mutableType,
            mutableReceiver: mutableReceiver);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, provider),
            providerIdentity, [], ["type.box"]);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public int32 Consume({receiverType} box, int32 value) {{ return box.Read(value); }}"),
            reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        if (mutableReceiver)
        {
            var readonlyConsumer = Compilation.CreateLibrary(
                SyntaxTree.Parse(
                    "public int32 Consume(Box box, int32 value) { return box.Read(value); }"),
                reference);
            Assert.That(readonlyConsumer.HasErrors, Is.True,
                "A readonly class handle must not call a write-qualified receiver.");
        }

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumer.Program!,
            "construction2-ordinary-call-consumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        Assert.That(tables.Methods.Count, Is.EqualTo(4));
    }

    [Test]
    [TestCase("public int32 Consume(Box box, int32 value) { return box.Read(value, ()); }")]
    [TestCase("public int32 Consume(Box box, int32 value, void token) { return box.Read(value, token); }")]
    public void EmitsConstruction2OrdinaryClassCallWithUnitFormal(
        string consumerSource)
    {
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2";
        const string providerIdentity
            = "construction2-unit-call-provider";
        var provider = CreateConstructionClassMethodProvider(
            providerIdentity, "Box", "type.box", "Box.Read",
            schemaIdentity, unitParameter: true);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, provider),
            providerIdentity, [], ["type.box"]);
        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumer.Program!,
            "construction2-unit-call-consumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        Assert.That(tables.Methods.Count, Is.EqualTo(4));
    }

    [Test]
    public void EmitsConstruction2OrdinaryClassCallWithScalarConstantInput()
    {
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2";
        const string providerIdentity
            = "construction2-constant-call-provider";
        var provider = CreateConstructionClassMethodProvider(
            providerIdentity, "Box", "type.box", "Box.Read",
            schemaIdentity);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, provider),
            providerIdentity, [], ["type.box"]);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Consume(Box box) { return box.Read(42); }"),
            reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumer.Program!,
            "construction2-constant-call-consumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        Assert.That(tables.Methods.Count, Is.EqualTo(4));
    }

    [Test]
    public void EmitsConstruction2OrdinaryUnitClassMethodCallBody()
    {
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2";
        const string providerIdentity
            = "construction2-unit-result-provider";
        var provider = CreateConstructionClassMethodProvider(
            providerIdentity, "Box", "type.box", "Box.Touch",
            schemaIdentity, unitResult: true);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, provider),
            providerIdentity, [], ["type.box"]);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public void Consume(Box box) { box.Touch(); }"), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumer.Program!,
            "construction2-unit-result-consumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        Assert.That(tables.Methods.Count, Is.EqualTo(4));
    }

    [Test]
    public void Construction2RejectsWritableReceiverOnImmutableClass()
    {
        const string schemaIdentity
            = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2";
        var schema = TrRmArtifactSchema.C1M1Construction2Draft;
        var provider = CreateConstructionClassMethodProvider(
            "construction2-invalid-receiver-provider", "Box", "type.box",
            "Box.Read", schemaIdentity);
        var providerIndex
            = TrRmArtifactIndex.ForC1M1Construction2Draft(provider);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(providerIndex);
        var methodRow = Enumerable.Range(1, tables.Methods.Count).Single(
            row => tables.Methods.GetOwnerType(row) != 0
                && tables.Signatures.GetReceiverTypeSpec(
                    tables.Methods.GetSignature(row)) != 0
                && !tables.Contracts.HasConstructionClause(row));
        var signatureRow = tables.Methods.GetSignature(methodRow);
        var receiverTypeSpec
            = tables.Signatures.GetReceiverTypeSpec(signatureRow);
        var receiver = tables.TypeSpecs.GetTypeSpecRef(receiverTypeSpec);
        Assert.That(receiver.Form, Is.EqualTo(0x02));
        Assert.That(receiver.Qualifiers, Is.Zero);

        var typeSpecRows
            = new ReadOnlyMemory<byte>[tables.TypeSpecs.Count + 1];
        for (var row = 1; row <= tables.TypeSpecs.Count; row++)
        {
            typeSpecRows[row - 1]
                = tables.TypeSpecs.GetTypeSpecBytes(
                    checked((uint)row)).ToArray();
        }

        var writableReceiverTypeSpec = checked((uint)typeSpecRows.Length);
        typeSpecRows[^1] = TrRmTypeSpecTableWriter.Reference(0x02,
            checked((uint)tables.Methods.GetOwnerType(methodRow)), 1);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            tables.TypeDefs.Count, tables.Imports.Count,
            tables.GenericParams.Count,
            tables.NativeFunctionPointerSignatures.Count, schema,
            tables.Methods.Count, tables.PredicateCaptures.Count);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length, tables.TypeDefs.Count,
            tables.Imports.Count, tables.GenericParams.Count,
            tables.NativeFunctionPointerSignatures.Count, schema,
            tables.Methods.Count, tables.PredicateCaptures.Count);

        var methodSignatures
            = new TrRmMethodSigRow[tables.Signatures.Count];
        for (var row = 1; row <= methodSignatures.Length; row++)
        {
            var (paramFirst, paramCount)
                = tables.Signatures.GetParamRange(row);
            var (genericFirst, genericCount)
                = tables.Signatures.GetGenericRange(row);
            methodSignatures[row - 1] = new(
                tables.Signatures.GetResultTypeSpec(row),
                row == signatureRow
                    ? writableReceiverTypeSpec
                    : tables.Signatures.GetReceiverTypeSpec(row),
                paramFirst, paramCount, genericFirst, genericCount);
        }

        var methodSignatureBytes = TrRmMethodSigTableWriter.Encode(
            methodSignatures, typeSpecs, tables.Parameters.Count,
            tables.GenericParams.Count);
        var rewrittenSections
            = new TrRmSection[providerIndex.SectionCount];
        for (var section = 0; section < rewrittenSections.Length;
            section++)
        {
            providerIndex.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            var replacementBytes = kind switch
            {
                0x0011 => typeSpecBytes,
                0x0017 => methodSignatureBytes,
                _ => bytes.ToArray(),
            };
            rewrittenSections[section] = new TrRmSection(kind, flags,
                checked((uint)(kind == 0x0011
                    ? typeSpecRows.Length : recordCount)),
                replacementBytes);
        }

        var invalidProvider = TrRmArtifactWriter.Encode(rewrittenSections,
            schema);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(
                    invalidProvider)));
    }

    private static byte[] MoveMethodsToSecondModule(byte[] artifact,
        string firstModuleIdentity, string secondModuleIdentity)
    {
        var schema
            = TrRmArtifactSchema
                .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft;
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        if (tables.Modules.Count != 1
            || tables.ModuleRefs.Count != 0
            || tables.Imports.Count != 0
            || tables.TypeDefs.Count == 0
            || tables.Methods.Count == 0
            || tables.Modules.GetEntryMethod(1) != 0
            || !StringComparer.Ordinal.Equals(
                tables.Modules.GetIdentity(1), firstModuleIdentity)
            || !index.TryGetSection(0x0001, out var moduleSection,
                out var moduleCount)
            || moduleCount != 1)
        {
            throw new InvalidOperationException(
                "The co-located fixture requires one import-free library Module with TypeDefs and Methods.");
        }

        var textRows = new List<(string Text, int Encoding)>(
            tables.Strings.Count + 1);
        for (var row = 1; row <= tables.Strings.Count; row++)
        {
            textRows.Add((tables.Strings.DecodeNamespace(row), 1));
        }
        textRows.Add((secondModuleIdentity, 1));
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows(textRows);
        var strings = new TrRmStringTableReader(stringsBytes, textRows.Count);
        var firstModuleIdentityStringRow = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(moduleSection.Span));
        var secondModuleIdentityStringRow = textRows.Count;
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(1);
        var (moduleRefStart, moduleRefEnd)
            = tables.Modules.GetModuleRefRange(1);
        var (typeDefStart, typeDefEnd)
            = tables.Modules.GetTypeDefRange(1);
        var (methodDefStart, _) = tables.Modules.GetMethodDefRange(1);
        var (sourceFileStart, _) = tables.Modules.GetSourceFileRange(1);
        var (featureStart, featureCount)
            = tables.Modules.GetFeatureRange(1);
        var moduleRows = new[] {
            new TrRmModuleRow(firstModuleIdentityStringRow,
                targetRequirement, runtimePolicy, 0, moduleRefStart,
                typeDefStart, methodDefStart, sourceFileStart, featureStart,
                featureCount, tables.Modules.GetGoverningRuleEdition(1)),
            new TrRmModuleRow(secondModuleIdentityStringRow,
                targetRequirement, runtimePolicy, 0, moduleRefEnd,
                typeDefEnd, methodDefStart, sourceFileStart, featureStart,
                featureCount, tables.Modules.GetGoverningRuleEdition(1)),
        };
        var featureTableCount = index.TryGetSection(0x0004, out _,
            out var featureCountInSection) ? featureCountInSection : 0;
        var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows, strings,
            tables.ModuleRefs.Count, tables.TypeDefs.Count,
            tables.Methods.Count, tables.Modules.SourceFileCount,
            featureTableCount);

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            sections.Add(kind switch {
                0x0001 => new TrRmSection(kind, flags, 2, moduleBytes),
                0x0002 => new TrRmSection(kind, flags,
                    checked((uint)textRows.Count), stringsBytes),
                _ => new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()),
            });
        }

        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator.ValidateC1AndGetExecutionTables(
            new TrRmArtifactIndex(result, schema));
        return result;
    }

    private static byte[] CreateC1M1ClassProvider(string moduleIdentity,
        string typeName, string exportKey, string schemaIdentity)
    {
        var tree = SyntaxTree.Parse($"public class {typeName} {{}}");
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        return TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    declaration.Span, exportKey, []),
            ]);
    }

    private static byte[] CreateC1M1ClassProviderWithField(
        string moduleIdentity, string typeName, string exportKey,
        string fieldExportKey, string schemaIdentity)
    {
        var tree = SyntaxTree.Parse(
            $"public class {typeName} {{ public int32 Value; }}");
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var field = declaration.Children.OfType<SyntaxNode>().Single(
            static node => node.Kind == SyntaxKind.FieldDeclaration);
        return TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0, declaration.Span,
                    exportKey, [
                        new TrRmSourceClassMemberExport(0, field.Span,
                            fieldExportKey),
                    ]),
            ]);
    }

    private static byte[] CreateConstructionClassMethodProvider(
        string moduleIdentity, string typeName, string exportKey,
        string methodExportKey, string schemaIdentity,
        bool mutableType = false, bool mutableReceiver = false,
        bool unitParameter = false, bool unitResult = false)
    {
        var typeMutability = mutableType ? "mut " : string.Empty;
        var methodMutability = mutableReceiver ? "mut " : string.Empty;
        var methodParameters = unitParameter
            ? "int32 value, void token" : "int32 value";
        var methodDeclaration = unitResult
            ? $"public {methodMutability}void Touch() {{}}"
            : $"public {methodMutability}int32 Read({methodParameters}) {{ return value; }}";
        var tree = SyntaxTree.Parse(
            $"public {typeMutability}class {typeName} {{ public int32 Value; public {typeName}(int32 initial) {{ Value = initial; }} {methodDeclaration} }}");
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var field = declaration.Children.OfType<SyntaxNode>().Single(
            static node => node.Kind == SyntaxKind.FieldDeclaration);
        var constructor = declaration.Children.OfType<SyntaxNode>().Single(
            static node => node.Kind == SyntaxKind.ConstructorDeclaration);
        var method = declaration.Children.OfType<SyntaxNode>().Single(
            static node => node.Kind == SyntaxKind.FunctionDeclaration);
        return TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0, declaration.Span,
                    exportKey, [
                        new TrRmSourceClassMemberExport(0, field.Span,
                            $"field.{typeName}.value"),
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span,
                            $"member.{typeName}.constructor") {
                            ConstructionProjectionExportKey
                                = $"member.{typeName}.create",
                        },
                        new TrRmSourceClassMemberExport(0, method.Span,
                            methodExportKey),
                    ]),
            ]);
    }
}
