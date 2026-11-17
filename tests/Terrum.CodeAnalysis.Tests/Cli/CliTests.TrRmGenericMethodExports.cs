// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    private const string GenericMethodCallsParameterLabelsSchemaIdentity
        = "trrm/1-draft-generic-method-calls-parameter-labels-1";
    private const string C1L1R1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-parameter-labels-1-receiver-access-1";
    private const string C1M1L1R1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-parameter-labels-1-receiver-access-1";
    private const string Construction1L1R1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1";
    private const string Construction2L1R1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1";

    [TestCase(GenericMethodCallsParameterLabelsSchemaIdentity,
        TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft)]
    public async Task CompileTrRmTransportsVersionThreeInterfaceExportsAsync(
        string schemaIdentity, TrRmArtifactSchema schema)
    {
        const string source = """
            namespace Example {
                public interface I<T> {
                    public T Read(T value);
                    public void Clear();
                }
                public T Echo<T>(T input) { return input; }
            }
            """;
        const string interfaceExportKey = "example.interface/I";
        const string methodExportKey = "example.interface/I.Read";
        const string genericMethodExportKey = "example.Echo";
        var sourcePath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-g1-interface-source-{Guid.NewGuid():N}.terrum");
        var exportFile = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-g1-interface-exports-{Guid.NewGuid():N}.json");
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-g1-interface-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllTextAsync(sourcePath, source,
                new UTF8Encoding(false)).ConfigureAwait(true);
            var tree = SyntaxTree.Parse(source, sourcePath);
            var compilation = Compilation.CreateLibrary(tree);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var members = compilation.Program!.Interfaces.Single().Members;
            var readMember = members.Single(static member =>
                member.Name == "Read");
            var clearMember = members.Single(static member =>
                member.Name == "Clear");
            var genericMethod = compilation.Program!
                .GenericMethodDefinitions.Single();
            var configuration = JsonSerializer.Serialize(new {
                schemaVersion = 3,
                sources = new[] {
                    new {
                        path = sourcePath,
                        snapshotSha256 = LowercaseHex(SHA256.HashData(
                            Encoding.Unicode.GetBytes(
                                tree.Root.ToFullString()))),
                    },
                },
                typeExports = new[] {
                    new {
                        @namespace = "Example",
                        name = "I",
                        genericArity = 1,
                        exportKey = interfaceExportKey,
                        interfaceMethodExports = new[] {
                            new {
                                sourceInputOrdinal
                                    = readMember.SourceInputOrdinal,
                                declarationStart
                                    = readMember.SourceDeclarationSpan.Start,
                                declarationLength
                                    = readMember.SourceDeclarationSpan.Length,
                                exportKey = methodExportKey,
                            },
                            new {
                                sourceInputOrdinal
                                    = clearMember.SourceInputOrdinal,
                                declarationStart
                                    = clearMember.SourceDeclarationSpan.Start,
                                declarationLength
                                    = clearMember.SourceDeclarationSpan.Length,
                                exportKey = "example.interface/I.Clear",
                            },
                        },
                    },
                },
                genericMethodExports = new[] {
                    new {
                        sourceInputOrdinal
                            = genericMethod.SourceInputOrdinal,
                        declarationStart = genericMethod.Span.Start,
                        declarationLength = genericMethod.Span.Length,
                        exportKey = genericMethodExportKey,
                    },
                },
            });
            await File.WriteAllTextAsync(exportFile, configuration,
                new UTF8Encoding(false)).ConfigureAwait(true);

            var (exit, output, error) = await RunAsync([], "compile-trrm",
                sourcePath, "--identity", "interface.provider",
                "--trrm-schema", schemaIdentity,
                "--trrm-source-generic-method-exports", exportFile,
                "--library", "-o", artifact, "--runtime", "fail-fast",
                "--json").ConfigureAwait(false);
            Assert.That(exit, Is.Zero,
                $"{output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);

            var artifactBytes = await File.ReadAllBytesAsync(artifact)
                .ConfigureAwait(true);
            {
                var tables = TrRmStructuralMetadataValidator
                    .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                        new TrRmArtifactIndex(artifactBytes, schema));
                Assert.That(tables.Interfaces.Count, Is.EqualTo(1));
                Assert.That(tables.InterfaceMethods.Count, Is.EqualTo(2));
                Assert.That(tables.Features.TryGetFeature(26, out _), Is.True);
                Assert.That(tables.Features.TryGetFeature(23, out _), Is.True);
                var requirementMethod = tables.InterfaceMethods
                    .GetMethodDefRow(1);
                Assert.That(tables.MethodParameterLabels.TryGetNameStringRow(
                    requirementMethod, 0, out var parameterNameRow), Is.True);
                Assert.That(tables.Strings.DecodeName(parameterNameRow),
                    Is.EqualTo("value"));
                Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                    interfaceExportKey, out var interfaceDefId), Is.True);
                Assert.That(interfaceDefId, Is.EqualTo(0x1300_0001));
                Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                    methodExportKey, out var methodDefId), Is.True);
                Assert.That(methodDefId >> 24, Is.EqualTo(0x16u));
                Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                    genericMethodExportKey, out var genericMethodDefId),
                    Is.True);
                Assert.That(genericMethodDefId >> 24, Is.EqualTo(0x16u));
                Assert.That(genericMethodDefId, Is.Not.EqualTo(methodDefId));
                var genericMethodRow = checked((int)(
                    genericMethodDefId & 0x00FF_FFFF));
                Assert.That(tables.MethodParameterLabels.TryGetNameStringRow(
                    genericMethodRow, 0, out var genericParameterNameRow),
                    Is.True);
                Assert.That(tables.Strings.DecodeName(
                    genericParameterNameRow), Is.EqualTo("input"));
            }

            var reference
                = schema == TrRmArtifactSchema
                    .GenericMethodCallsParameterLabelsDraft
                    ? TrRmExecutableCompiler
                        .CreateGenericMethodCallsDraftReference(
                            artifactBytes, "interface.provider")
                    : TrRmExecutableCompiler.CreateStandardReference(
                        new TrRmStandardArtifactInput(schemaIdentity,
                            artifactBytes),
                        "interface.provider", []);
            var importedInterface = reference.Interfaces.Single();
            Assert.That(importedInterface.ModuleImport!.ExportKey,
                Is.EqualTo(interfaceExportKey));
            var importedRead = importedInterface.Members.Single(
                static member => member.Name == "Read");
            var importedClear = importedInterface.Members.Single(
                static member => member.Name == "Clear");
            Assert.That(importedRead.DeclarationKey,
                Is.EqualTo(methodExportKey));
            Assert.That(importedRead.Parameters.Single().Name,
                Is.EqualTo("value"));
            Assert.That(importedClear.ResultType!.PrimitiveType,
                Is.EqualTo(PrimitiveType.Void));
            var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
                namespace Example {
                    struct S : I<S> {
                        int32 Field;
                        public S Read(S value) { return value; }
                        public void Clear() { }
                    }
                }
                int32 Main() { return 42; }
                """), reference);
            Assert.That(consumerCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    consumerCompilation.Diagnostics));
            SemanticVerifier.Verify(consumerCompilation.Program!);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(sourcePath)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(exportFile)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(artifact)
                .ConfigureAwait(true);
        }
    }

    [TestCase(GenericMethodCallsParameterLabelsSchemaIdentity,
        TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft)]
    public async Task CompileTrRmTransportsVersionOneGenericMethodExportsAsync(
        string schemaIdentity, TrRmArtifactSchema schema)
    {
        const string source = """
            namespace Example {
                public T Identity<T>(T value) { return value; }
            }
            """;
        const string exportKey = "example.Identity";
        var sourcePath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-g1-method-source-{Guid.NewGuid():N}.terrum");
        var exportFile = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-g1-method-exports-{Guid.NewGuid():N}.json");
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-g1-method-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllTextAsync(sourcePath, source,
                new UTF8Encoding(false)).ConfigureAwait(true);
            var tree = SyntaxTree.Parse(source, sourcePath);
            var compilation = Compilation.CreateLibrary(tree);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var method = compilation.Program!.GenericMethodDefinitions
                .Single();
            var configuration = JsonSerializer.Serialize(new {
                schemaVersion = 1,
                sources = new[] {
                    new {
                        path = sourcePath,
                        snapshotSha256 = LowercaseHex(SHA256.HashData(
                            Encoding.Unicode.GetBytes(
                                tree.Root.ToFullString()))),
                    },
                },
                exports = new[] {
                    new {
                        sourceInputOrdinal = method.SourceInputOrdinal,
                        declarationStart = method.Span.Start,
                        declarationLength = method.Span.Length,
                        exportKey,
                    },
                },
            });
            await File.WriteAllTextAsync(exportFile, configuration,
                new UTF8Encoding(false)).ConfigureAwait(true);

            var (exit, output, error) = await RunAsync([], "compile-trrm",
                sourcePath, "--identity", "generic.provider",
                "--trrm-schema", schemaIdentity,
                "--trrm-source-generic-method-exports", exportFile,
                "--library", "-o", artifact, "--runtime", "fail-fast",
                "--json").ConfigureAwait(false);
            Assert.That(exit, Is.Zero,
                $"{output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);

            var artifactBytes = await File.ReadAllBytesAsync(artifact)
                .ConfigureAwait(true);
            var tables = TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                    new TrRmArtifactIndex(artifactBytes, schema));
            Assert.That(tables.Features.TryGetFeature(23, out _), Is.True);
            Assert.That(tables.Features.TryGetFeature(26, out _), Is.True);
            Assert.That(tables.Exports.TryGetDefinitionForKey(1, exportKey,
                out var methodDefId), Is.True);
            Assert.That(methodDefId, Is.EqualTo(0x1600_0001));
            Assert.That(tables.MethodParameterLabels.TryGetNameStringRow(
                checked((int)(methodDefId & 0x00FF_FFFF)), 0,
                out var parameterNameRow), Is.True);
            Assert.That(tables.Strings.DecodeName(parameterNameRow),
                Is.EqualTo("value"));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(sourcePath)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(exportFile)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(artifact)
                .ConfigureAwait(true);
        }
    }

    [TestCase(C1L1R1SchemaIdentity, 1)]
    [TestCase(C1L1R1SchemaIdentity, 3)]
    [TestCase(C1M1L1R1SchemaIdentity, 1)]
    [TestCase(C1M1L1R1SchemaIdentity, 3)]
    [TestCase(Construction1L1R1SchemaIdentity, 1)]
    [TestCase(Construction1L1R1SchemaIdentity, 3)]
    [TestCase(Construction2L1R1SchemaIdentity, 1)]
    [TestCase(Construction2L1R1SchemaIdentity, 3)]
    public async Task CompileTrRmRejectsG1SourceMapsForC1LabelProfilesAsync(
        string schemaIdentity, int schemaVersion)
    {
        const string source = """
            public T Identity<T>(T value) { return value; }
            """;
        const string exportKey = "Example.Identity";
        var sourcePath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-g1-map-c1-reject-source-{Guid.NewGuid():N}.terrum");
        var exportFile = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-g1-map-c1-reject-exports-{Guid.NewGuid():N}.json");
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-g1-map-c1-reject-{Guid.NewGuid():N}.trrm");
        byte[] retained = [0xA7, 0x3C];
        try
        {
            await File.WriteAllTextAsync(sourcePath, source,
                new UTF8Encoding(false)).ConfigureAwait(true);
            var tree = SyntaxTree.Parse(source, sourcePath);
            var compilation = Compilation.CreateLibrary(tree);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var method = compilation.Program!.GenericMethodDefinitions
                .Single();
            var sourceAssertion = new {
                path = sourcePath,
                snapshotSha256 = LowercaseHex(SHA256.HashData(
                    Encoding.Unicode.GetBytes(tree.Root.ToFullString()))),
            };
            var selector = new {
                sourceInputOrdinal = method.SourceInputOrdinal,
                declarationStart = method.Span.Start,
                declarationLength = method.Span.Length,
                exportKey,
            };
            var configuration = schemaVersion switch {
                1 => JsonSerializer.Serialize(new {
                    schemaVersion,
                    sources = new[] { sourceAssertion },
                    exports = new[] { selector },
                }),
                3 => JsonSerializer.Serialize(new {
                    schemaVersion,
                    sources = new[] { sourceAssertion },
                    typeExports = Array.Empty<object>(),
                    genericMethodExports = new[] { selector },
                }),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion)),
            };
            await File.WriteAllTextAsync(exportFile, configuration,
                new UTF8Encoding(false)).ConfigureAwait(true);
            await File.WriteAllBytesAsync(artifact, retained)
                .ConfigureAwait(true);

            var (exit, output, error) = await RunAsync([], "compile-trrm",
                sourcePath, "--identity", "generic.provider",
                "--trrm-schema", schemaIdentity,
                "--trrm-source-generic-method-exports", exportFile,
                "--library", "-o", artifact, "--runtime", "fail-fast",
                "--json").ConfigureAwait(false);
            Assert.That(exit, Is.Not.Zero,
                $"{output}{Environment.NewLine}{error}");
            Assert.That(await File.ReadAllBytesAsync(artifact)
                .ConfigureAwait(true), Is.EqualTo(retained));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(sourcePath)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(exportFile)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(artifact)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task CompileTrRmRejectsInvalidVersionThreeInterfaceMapsWithoutReplacingOutputAsync()
    {
        const string source = """
            namespace Example {
                public interface I<T> {
                    public T Read(T value);
                }
            }
            """;
        var sourcePath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-g1-interface-invalid-source-{Guid.NewGuid():N}.terrum");
        var exportFile = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-g1-interface-invalid-exports-{Guid.NewGuid():N}.json");
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-g1-interface-invalid-{Guid.NewGuid():N}.trrm");
        byte[] retained = [0xA7, 0x3C];
        try
        {
            await File.WriteAllTextAsync(sourcePath, source,
                new UTF8Encoding(false)).ConfigureAwait(true);
            var tree = SyntaxTree.Parse(source, sourcePath);
            var compilation = Compilation.CreateLibrary(tree);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var member = compilation.Program!.Interfaces.Single()
                .Members.Single();
            var snapshotSha256 = LowercaseHex(SHA256.HashData(
                Encoding.Unicode.GetBytes(tree.Root.ToFullString())));
            var validMember = new {
                sourceInputOrdinal = member.SourceInputOrdinal,
                declarationStart = member.SourceDeclarationSpan.Start,
                declarationLength = member.SourceDeclarationSpan.Length,
                exportKey = "example.interface/I.Read",
            };
            var baseConfiguration = new {
                schemaVersion = 3,
                sources = new[] {
                    new { path = sourcePath, snapshotSha256 },
                },
            };
            var configurations = new[] {
                JsonSerializer.Serialize(new {
                    baseConfiguration.schemaVersion,
                    baseConfiguration.sources,
                    typeExports = new[] {
                        new {
                            @namespace = "Example",
                            name = "I",
                            genericArity = 1,
                            exportKey = "example.interface/I",
                            interfaceMethodExports = Array.Empty<object>(),
                        },
                    },
                    genericMethodExports = Array.Empty<object>(),
                }),
                JsonSerializer.Serialize(new {
                    baseConfiguration.schemaVersion,
                    baseConfiguration.sources,
                    typeExports = new[] {
                        new {
                            @namespace = "Example",
                            name = "I",
                            genericArity = 1,
                            exportKey = "example.interface/I",
                            interfaceMethodExports = new[] {
                                validMember with {
                                    declarationStart
                                        = validMember.declarationStart + 1,
                                },
                            },
                        },
                    },
                    genericMethodExports = Array.Empty<object>(),
                }),
                JsonSerializer.Serialize(new {
                    baseConfiguration.schemaVersion,
                    baseConfiguration.sources,
                    typeExports = new[] {
                        new {
                            @namespace = "Example",
                            name = "I",
                            genericArity = 1,
                            exportKey = "duplicate",
                            interfaceMethodExports = new[] {
                                validMember with {
                                    exportKey = "duplicate",
                                },
                            },
                        },
                    },
                    genericMethodExports = Array.Empty<object>(),
                }),
            };

            foreach (var configuration in configurations)
            {
                await File.WriteAllTextAsync(exportFile, configuration,
                    new UTF8Encoding(false)).ConfigureAwait(true);
                await File.WriteAllBytesAsync(artifact, retained)
                    .ConfigureAwait(true);
                var (exit, output, error) = await RunAsync([],
                    "compile-trrm", sourcePath, "--identity",
                    "interface.provider", "--trrm-schema",
                    "trrm/1-draft-generic-method-calls-parameter-labels-1",
                    "--trrm-source-generic-method-exports", exportFile,
                    "--library", "-o", artifact, "--runtime", "fail-fast",
                    "--json").ConfigureAwait(false);
                Assert.That(exit, Is.Not.Zero,
                    $"{output}{Environment.NewLine}{error}");
                Assert.That(await File.ReadAllBytesAsync(artifact)
                    .ConfigureAwait(true), Is.EqualTo(retained));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(sourcePath)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(exportFile)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(artifact)
                .ConfigureAwait(true);
        }
    }
}
