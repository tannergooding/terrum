// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
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
}
