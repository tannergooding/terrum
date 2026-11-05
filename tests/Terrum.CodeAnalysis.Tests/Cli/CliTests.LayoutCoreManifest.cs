// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string linkPath,
        string existingPath, IntPtr securityAttributes);

    private const string LayoutCoreSource = """
        [Blittable]
        [Layout(LayoutKind.Sequential, Packing: 1)]
        internal struct Pair {
            public mut uint8 Tag;
            public mut uint64 Value;
            internal Pair() {
                Tag = uint8(0);
                Value = uint64(0);
            }
        }
        public int32 Main() {
            mut Pair pair = new Pair();
            return 0;
        }
        """;

    private const string LayoutCoreNativeImportSource = """
        [Blittable]
        [Layout(LayoutKind.Sequential, Packing: 1)]
        internal struct Pair {
            public mut uint8 Tag;
            public mut uint64 Value;
            internal Pair() {
                Tag = uint8(0);
                Value = uint64(0);
            }
        }

        [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentThreadId")]
        extern unsafe uint32 CurrentThreadId();

        int32 Main() {
            mut Pair pair = new Pair();
            unsafe {
                if (CurrentThreadId() == 0) { return 1; }
            }
            return 0;
        }
        """;

    [Test]
    public async Task LayoutCoreManifestEnablesAnalyzeAndStandardTrRmCompilationAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-layout-core-manifest-");
        var manifestPath = Path.Combine(directory, "layout.json");
        var artifactPath = Path.Combine(directory, "layout.trrm");
        try
        {
            var manifestBytes = Encoding.UTF8.GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(CreateLayoutCoreManifest()))
                .ToArray();
            await File.WriteAllBytesAsync(manifestPath, manifestBytes).ConfigureAwait(false);
            var source = Encoding.UTF8.GetBytes(LayoutCoreSource);

            var (unregistered, unregisteredOutput, unregisteredError)
                = await RunAsync(source, "analyze", "-", "--json").ConfigureAwait(false);
            Assert.That(unregistered, Is.EqualTo(1),
                unregisteredOutput + unregisteredError);

            var (analyzed, analyzedOutput, analyzedError)
                = await RunAsync(source, "analyze", "-",
                    "--layout-core-manifest", manifestPath, "--json").ConfigureAwait(false);
            Assert.That(analyzed, Is.Zero, analyzedOutput + analyzedError);
            Assert.That(analyzedError, Is.Empty);
            using (var result = JsonDocument.Parse(analyzedOutput))
            {
                Assert.That(result.RootElement.GetProperty("success")
                    .GetBoolean(), Is.True);
            }

            var (compiled, compiledOutput, compiledError)
                = await RunAsync(source, "compile-trrm", "-",
                    "--identity", "sample.consumer", "-o", artifactPath,
                    "--runtime", "fail-fast",
                    "--trrm-schema",
                    "trrm/1-draft-local-aggregate-layout-execution-1",
                    "--layout-core-manifest", manifestPath, "--json").ConfigureAwait(false);
            Assert.That(compiled, Is.Zero,
                compiledOutput + compiledError);
            Assert.That(compiledError, Is.Empty);
            Assert.That(File.Exists(artifactPath), Is.True);
            Assert.That(new FileInfo(artifactPath).Length, Is.GreaterThan(0));

            var libraryArtifactPath = Path.Combine(directory,
                "layout-library.trrm");
            var (libraryCompiled, libraryOutput, libraryError)
                = await RunAsync(source, "compile-trrm", "-",
                    "--identity", "sample.consumer", "-o",
                    libraryArtifactPath, "--runtime", "fail-fast",
                    "--trrm-schema",
                    "trrm/1-draft-local-aggregate-layout-execution-1",
                    "--layout-core-manifest", manifestPath, "--library",
                    "--json").ConfigureAwait(false);
            Assert.That(libraryCompiled, Is.Zero,
                libraryOutput + libraryError);
            Assert.That(libraryError, Is.Empty);
            Assert.That(File.Exists(libraryArtifactPath), Is.True);
            Assert.That(new FileInfo(libraryArtifactPath).Length,
                Is.GreaterThan(0));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task LayoutCoreManifestFlowsThroughNativeAndLibraryFactoriesAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-layout-core-manifest-factories-");
        var manifestPath = Path.Combine(directory, "layout.json");
        var nativeManifestPath = Path.Combine(directory, "native.json");
        try
        {
            await File.WriteAllTextAsync(manifestPath,
                CreateLayoutCoreManifest()).ConfigureAwait(false);
            await File.WriteAllTextAsync(nativeManifestPath,
                CreateNativeCoreManifest(
                    coreAuthorityIdentity: "test-layout-authority"))
                .ConfigureAwait(false);
            var source = Encoding.UTF8.GetBytes(LayoutCoreSource);
            var commandCases = new[] {
                new {
                    Command = "build",
                    OutputName = "layout.exe",
                    Export = (string?)null,
                    Source = LayoutCoreSource,
                    NeedsNativeCore = false,
                },
                new {
                    Command = "build-native-import",
                    OutputName = "layout-native.exe",
                    Export = (string?)null,
                    Source = LayoutCoreNativeImportSource,
                    NeedsNativeCore = true,
                },
                new {
                    Command = "build-library",
                    OutputName = "layout.dll",
                    Export = (string?)"Main",
                    Source = LayoutCoreSource,
                    NeedsNativeCore = false,
                },
            };
            foreach (var commandCase in commandCases)
            {
                var outputPath = Path.Combine(directory,
                    commandCase.OutputName);
                var arguments = new List<string> {
                    commandCase.Command,
                    "-",
                    "--runtime",
                    "fail-fast",
                };
                if (commandCase.Export is string export)
                {
                    arguments.Add("--export");
                    arguments.Add(export);
                }
                arguments.Add("-o");
                arguments.Add(outputPath);
                arguments.Add("--layout-core-manifest");
                arguments.Add(manifestPath);
                if (commandCase.NeedsNativeCore)
                {
                    arguments.Add("--native-core-manifest");
                    arguments.Add(nativeManifestPath);
                }
                arguments.Add("--json");

                var commandSource = Encoding.UTF8.GetBytes(
                    commandCase.Source);
                var (exit, output, error) = await RunAsync(commandSource,
                    [.. arguments]).ConfigureAwait(false);
                Assert.That(exit, Is.Zero, output + error);
                Assert.That(error, Is.Empty);
                Assert.That(File.Exists(outputPath), Is.True);
                Assert.That(new FileInfo(outputPath).Length,
                    Is.GreaterThan(0));
            }

            var (jitExit, jitOutput, jitError) = await RunAsync(source,
                "jit", "-", "--runtime", "fail-fast",
                "--layout-core-manifest", manifestPath, "--json").ConfigureAwait(false);
            Assert.That(jitExit, Is.Zero, jitOutput + jitError);
            Assert.That(jitError, Is.Empty);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task LayoutCoreManifestRejectsInvalidNamesAndDecodedDuplicatePropertiesAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-layout-core-manifest-invalid-");
        var manifestPath = Path.Combine(directory, "layout.json");
        try
        {
            var malformedManifests = new[] {
                CreateLayoutCoreManifest().Replace(
                    "\"schemaVersion\":1",
                    "\"schemaVersion\":2",
                    StringComparison.Ordinal),
                CreateLayoutCoreManifest().Replace(
                    "\"schemaVersion\":1",
                    "\"schemaVersion\":1.0",
                    StringComparison.Ordinal),
                CreateLayoutCoreManifest().Replace(
                    "{\"schemaVersion\":1",
                    "{\"schemaVersion\":1,\"unexpected\":true",
                    StringComparison.Ordinal),
                CreateLayoutCoreManifest().Replace(
                    "\"schemaVersion\":1",
                    "\"schemaVersion\":1,\"schema\\u0056ersion\":1",
                    StringComparison.Ordinal),
                CreateLayoutCoreManifest().Replace(
                    "\"qualifiedName\":\"Layout\"",
                    "\"qualifiedName\":\"Layout; namespace Injected\"",
                    StringComparison.Ordinal),
                CreateLayoutCoreManifest().Replace(
                    "\"name\":\"Auto\"",
                    "\"name\":\"Auto(){}\"",
                    StringComparison.Ordinal),
                CreateLayoutCoreManifest().Replace(
                    "\"name\":\"Kind\"",
                    "\"name\":\"Kind\",\"unexpected\":true",
                    StringComparison.Ordinal),
                CreateLayoutCoreManifest().Replace(
                    "\"revision\":1",
                    "\"revision\":0",
                    StringComparison.Ordinal),
                CreateLayoutCoreManifest().Replace(
                    "\"targets\":[\"inline-struct\"]",
                    "\"targets\":[\"inline-struct\",\"inline-struct\"]",
                    StringComparison.Ordinal),
                CreateLayoutCoreManifest().Replace(
                    "\"coreAuthorityIdentity\":\"test-layout-authority\",",
                    "",
                    StringComparison.Ordinal),
            };
            foreach (var manifest in malformedManifests)
            {
                await File.WriteAllTextAsync(manifestPath, manifest).ConfigureAwait(false);
                var (exit, output, error) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main( {"), "analyze", "-",
                    "--layout-core-manifest", manifestPath, "--json").ConfigureAwait(false);

                Assert.That(exit, Is.EqualTo(2), output + error);
                AssertInputDiagnosticCode(output, "TRM2001");
            }

            var malformedManifestBytes = new[] {
                [0x7B, 0x22, 0xFF, 0x22, 0x3A, 0x31, 0x7D],
                [
                    .. Encoding.UTF8.GetPreamble()
,
                    .. Encoding.UTF8.GetPreamble(),
                    .. Encoding.UTF8.GetBytes(
                                CreateLayoutCoreManifest()),
                ],
                Encoding.UTF8.GetBytes(CreateLayoutCoreManifest() + " {}"),
                Encoding.UTF8.GetBytes(CreateLayoutCoreManifest()
                    .Replace("{\"schemaVersion\":1",
                        "{/*comment*/\"schemaVersion\":1",
                        StringComparison.Ordinal)),
            };
            foreach (var manifestBytes in malformedManifestBytes)
            {
                await File.WriteAllBytesAsync(manifestPath, manifestBytes).ConfigureAwait(false);
                var (exit, output, error) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { return 0; }"),
                    "analyze", "-", "--layout-core-manifest", manifestPath,
                    "--json").ConfigureAwait(false);

                Assert.That(exit, Is.EqualTo(2), output + error);
                AssertInputDiagnosticCode(output, "TRM2001");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task LayoutCoreManifestCannotAliasOutputOrCompileTrRmIdentityAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-layout-core-manifest-alias-");
        var outputPath = Path.Combine(directory, "layout.exe");
        var artifactPath = Path.Combine(directory, "layout.trrm");
        try
        {
            var manifest = CreateLayoutCoreManifest();
            var sentinel = Encoding.UTF8.GetBytes(manifest);
            await File.WriteAllBytesAsync(outputPath, sentinel).ConfigureAwait(false);

            var (aliasExit, aliasOutput, aliasError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { return 0; }"),
                "build", "-", "-o", outputPath, "--runtime", "fail-fast",
                "--layout-core-manifest", outputPath, "--json").ConfigureAwait(false);
            Assert.That(aliasExit, Is.EqualTo(2), aliasOutput + aliasError);
            AssertInputDiagnosticCode(aliasOutput, "TRM2002");
            Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false),
                Is.EqualTo(sentinel));

            if (OperatingSystem.IsWindows())
            {
                var hardLinkPath = Path.Combine(directory,
                    "layout-alias.json");
                if (!CreateHardLink(hardLinkPath, outputPath, IntPtr.Zero))
                {
                    throw new IOException(
                        "Could not create the test manifest hard link.",
                        new Win32Exception(Marshal.GetLastWin32Error()));
                }
                var (hardLinkExit, hardLinkOutput, hardLinkError)
                    = await RunAsync(
                        Encoding.UTF8.GetBytes(
                            "int32 Main() { return 0; }"),
                        "build", "-", "-o", outputPath, "--runtime",
                        "fail-fast", "--layout-core-manifest", hardLinkPath,
                        "--json").ConfigureAwait(false);
                Assert.That(hardLinkExit, Is.EqualTo(2),
                    hardLinkOutput + hardLinkError);
                AssertInputDiagnosticCode(hardLinkOutput, "TRM2002");
                Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false),
                    Is.EqualTo(sentinel));
            }

            await File.WriteAllTextAsync(outputPath, manifest).ConfigureAwait(false);
            var (identityExit, identityOutput, identityError) = await RunAsync(
                Encoding.UTF8.GetBytes(LayoutCoreSource), "compile-trrm", "-",
                "--identity", "different.consumer", "-o", artifactPath,
                "--runtime", "fail-fast",
                "--trrm-schema",
                "trrm/1-draft-local-aggregate-layout-execution-1",
                "--layout-core-manifest", outputPath, "--json").ConfigureAwait(false);
            Assert.That(identityExit, Is.EqualTo(2),
                identityOutput + identityError);
            AssertInputDiagnosticCode(identityOutput, "TRM2000");
            Assert.That(File.Exists(artifactPath), Is.False);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PrivateModuleManifestAdmissionPrecedesSourceAndPreservesOutputAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-layout-core-manifest-private-admission-");
        var manifestPath = Path.Combine(directory, "invalid-layout.json");
        var outputPath = Path.Combine(directory, "module.trm");
        var missingSource = Path.Combine(directory, "missing.terrum");
        var sentinel = new byte[] { 0x13, 0x37, 0x42 };
        try
        {
            await File.WriteAllTextAsync(manifestPath,
                CreateLayoutCoreManifest().Replace(
                    "\"schemaVersion\":1",
                    "\"schemaVersion\":2",
                    StringComparison.Ordinal)).ConfigureAwait(false);
            await File.WriteAllBytesAsync(outputPath, sentinel).ConfigureAwait(false);

            var (exit, output, error) = await RunAsync(
                [],
                "compile-module", "producer", missingSource, "Helper",
                "-o", outputPath, "--layout-core-manifest", manifestPath,
                "--json").ConfigureAwait(false);

            Assert.That(exit, Is.EqualTo(2), output + error);
            AssertInputDiagnosticCode(output, "TRM2001");
            Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false),
                Is.EqualTo(sentinel));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PrivateModuleManifestRejectsSourceProducerAndOutputAliasesAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-layout-core-manifest-private-alias-");
        var sourcePath = Path.Combine(directory, "source.terrum");
        var sourceAliasPath = Path.Combine(directory, "source-layout.json");
        var outputAliasPath = Path.Combine(directory, "module.trm");
        var producerAliasPath = Path.Combine(directory, "producer.trm");
        var outputPath = Path.Combine(directory, "consumer.trm");
        try
        {
            var manifest = CreateLayoutCoreManifest();
            await File.WriteAllTextAsync(sourcePath,
                "public int32 Helper() { return 1; }").ConfigureAwait(false);
            await File.WriteAllTextAsync(sourceAliasPath, manifest).ConfigureAwait(false);
            await File.WriteAllTextAsync(outputAliasPath, manifest).ConfigureAwait(false);
            await File.WriteAllTextAsync(producerAliasPath, manifest).ConfigureAwait(false);

            var (sourceExit, sourceOutput, sourceError) = await RunAsync(
                [], "compile-module", "producer",
                sourceAliasPath, "Helper", "-o",
                Path.Combine(directory, "source-alias-output.trm"),
                "--layout-core-manifest", sourceAliasPath, "--json").ConfigureAwait(false);
            Assert.That(sourceExit, Is.EqualTo(2),
                sourceOutput + sourceError);
            AssertInputDiagnosticCode(sourceOutput, "TRM2002");

            var (outputExit, output, outputError) = await RunAsync(
                [], "compile-module", "producer", sourcePath,
                "Helper", "-o", outputAliasPath,
                "--layout-core-manifest", outputAliasPath, "--json").ConfigureAwait(false);
            Assert.That(outputExit, Is.EqualTo(2), output + outputError);
            AssertInputDiagnosticCode(output, "TRM2002");

            var (producerExit, producerOutput, producerError) = await RunAsync(
                [], "compile-module", "consumer", sourcePath,
                producerAliasPath, "-o", outputPath,
                "--layout-core-manifest", producerAliasPath, "--json").ConfigureAwait(false);
            Assert.That(producerExit, Is.EqualTo(2),
                producerOutput + producerError);
            AssertInputDiagnosticCode(producerOutput, "TRM2002");
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task ScannerManifestAdmissionPrecedesInputsAndRejectsAliasesAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-layout-core-manifest-scanner-");
        var manifestPath = Path.Combine(directory, "layout.json");
        var scannerPath = Path.Combine(directory, "scanner.terrum");
        var subjectPath = Path.Combine(directory, "subject.terrum");
        var outputPath = Path.Combine(directory, "scanner.exe");
        try
        {
            var invalidManifest = CreateLayoutCoreManifest().Replace(
                "\"schemaVersion\":1",
                "\"schemaVersion\":2",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(manifestPath, invalidManifest).ConfigureAwait(false);
            var (invalidExit, invalidOutput, invalidError) = await RunAsync(
                Encoding.UTF8.GetBytes("input"), "scan-component",
                Path.Combine(directory, "missing-scanner.terrum"), "-",
                "--runtime", "fail-fast", "--layout-core-manifest",
                manifestPath, "--json").ConfigureAwait(false);
            Assert.That(invalidExit, Is.EqualTo(2),
                invalidOutput + invalidError);
            AssertInputDiagnosticCode(invalidOutput, "TRM2001");

            var manifest = CreateLayoutCoreManifest("sample.scanner");
            await File.WriteAllTextAsync(scannerPath, manifest).ConfigureAwait(false);
            var (scannerAliasExit, scannerAliasOutput, scannerAliasError)
                = await RunAsync(Encoding.UTF8.GetBytes("input"),
                    "scan-component", scannerPath, "-", "--runtime",
                    "fail-fast", "--layout-core-manifest", scannerPath,
                    "--json").ConfigureAwait(false);
            Assert.That(scannerAliasExit, Is.EqualTo(2),
                scannerAliasOutput + scannerAliasError);
            AssertInputDiagnosticCode(scannerAliasOutput, "TRM2002");

            await File.WriteAllTextAsync(subjectPath, manifest).ConfigureAwait(false);
            var (subjectAliasExit, subjectAliasOutput, subjectAliasError)
                = await RunAsync(Encoding.UTF8.GetBytes("input"),
                    "scan-component", scannerPath, subjectPath, "--runtime",
                    "fail-fast", "--layout-core-manifest", subjectPath,
                    "--json").ConfigureAwait(false);
            Assert.That(subjectAliasExit, Is.EqualTo(2),
                subjectAliasOutput + subjectAliasError);
            AssertInputDiagnosticCode(subjectAliasOutput, "TRM2002");

            await File.WriteAllTextAsync(outputPath, manifest).ConfigureAwait(false);
            var outputSentinel = await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false);
            var (outputAliasExit, outputAliasOutput, outputAliasError)
                = await RunAsync(Encoding.UTF8.GetBytes("input"),
                    "build-component", scannerPath, "-", "-o", outputPath,
                    "--runtime", "fail-fast", "--layout-core-manifest",
                    outputPath, "--json").ConfigureAwait(false);
            Assert.That(outputAliasExit, Is.EqualTo(2),
                outputAliasOutput + outputAliasError);
            AssertInputDiagnosticCode(outputAliasOutput, "TRM2002");
            Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false),
                Is.EqualTo(outputSentinel));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task FrontendComponentRequiresExplicitLayoutCoreManifestAsync()
    {
        var root = RepositoryRoot();
        var frontendDirectory = TerrumProjectPaths.SourceDirectory(root);
        var sourcePath = TerrumProjectPaths.SourceFile(root,
            Path.Combine("Text", "SourceRange.terrum"));
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-frontend-component-layout-manifest-");
        var subjectPath = Path.Combine(directory, "subject.json");
        try
        {
            var source = Encoding.UTF8.GetBytes(
                "int32 Main() { return 42; }");
            var (unconfiguredExit, unconfiguredOutput, unconfiguredError)
                = await RunAsync(source, "frontend-component",
                    frontendDirectory, "-", "--runtime", "fail-fast",
                    "--json").ConfigureAwait(false);
            Assert.That(unconfiguredExit, Is.EqualTo(1),
                unconfiguredOutput + unconfiguredError);
            Assert.That(unconfiguredOutput, Does.Contain("TRM3005"));

            var projection = await ReadFrontendComponentProjectionAsync(
                source, "fail-fast").ConfigureAwait(false);
            Assert.That(projection.GetProperty("valid").GetBoolean(),
                Is.True);

            var (sourceAliasExit, sourceAliasOutput, sourceAliasError)
                = await RunAsync(source, "frontend-component",
                    frontendDirectory, "-", "--runtime", "fail-fast",
                    "--layout-core-manifest", sourcePath, "--json").ConfigureAwait(false);
            Assert.That(sourceAliasExit, Is.EqualTo(2),
                sourceAliasOutput + sourceAliasError);
            AssertInputDiagnosticCode(sourceAliasOutput, "TRM2002");

            var manifest = CreateLayoutCoreManifest();
            await File.WriteAllTextAsync(subjectPath, manifest)
                .ConfigureAwait(true);
            var (subjectAliasExit, subjectAliasOutput, subjectAliasError)
                = await RunAsync(source, "frontend-component",
                    frontendDirectory, subjectPath, "--runtime",
                    "fail-fast", "--layout-core-manifest", subjectPath,
                    "--json").ConfigureAwait(false);
            Assert.That(subjectAliasExit, Is.EqualTo(2),
                subjectAliasOutput + subjectAliasError);
            AssertInputDiagnosticCode(subjectAliasOutput, "TRM2002");
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task WideLayoutBuildDeclinesBeforeReplacingExistingOutputAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-wide-layout-native-capacity-");
        var manifestPath = Path.Combine(directory, "layout.json");
        var outputPath = Path.Combine(directory, "wide.exe");
        var sentinel = new byte[] { 0x13, 0x37, 0x42 };
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Wide {
                public [uint8; 4294967297] Payload;
                public static int32 Hidden() { return 7; }
            }

            int32 Main() {
                init Wide value;
                return Wide.Hidden();
            }
            """;
        try
        {
            await File.WriteAllTextAsync(manifestPath,
                CreateLayoutCoreManifest()).ConfigureAwait(false);
            await File.WriteAllBytesAsync(outputPath, sentinel).ConfigureAwait(false);

            var (exit, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "build", "-", "-o",
                outputPath, "--runtime", "fail-fast",
                "--layout-core-manifest", manifestPath, "--json").ConfigureAwait(false);

            Assert.That(exit, Is.EqualTo(1), output + error);
            AssertInputDiagnosticCode(output, "TRM4001");
            Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(false),
                Is.EqualTo(sentinel));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task SyntaxOnlyCommandsDoNotAcceptLayoutCoreManifestAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-layout-core-manifest-check-");
        var manifestPath = Path.Combine(directory, "layout.json");
        try
        {
            await File.WriteAllTextAsync(manifestPath,
                CreateLayoutCoreManifest()).ConfigureAwait(false);
            foreach (var command in new[] { "check", "tokens", "tree" })
            {
                var (exit, output, error) = await RunAsync(
                    Encoding.UTF8.GetBytes(LayoutCoreSource), command, "-",
                    "--layout-core-manifest", manifestPath, "--json").ConfigureAwait(false);

                Assert.That(exit, Is.EqualTo(2), output + error);
                AssertInputDiagnosticCode(output, "TRM2000");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task FrontendFallbackCommandsDoNotAcceptLayoutCoreManifestAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-layout-core-manifest-frontend-");
        var manifestPath = Path.Combine(directory, "layout.json");
        try
        {
            await File.WriteAllTextAsync(manifestPath,
                CreateLayoutCoreManifest()).ConfigureAwait(false);
            var source = Encoding.UTF8.GetBytes(LayoutCoreSource);
            var commandCases = new[] {
                new[] {
                    "build", "-", "--frontend-command", "frontend-build",
                    "--source-name", "input.terrum", "-o",
                    Path.Combine(directory, "layout.exe"), "--runtime",
                    "fail-fast", "--layout-core-manifest", manifestPath,
                    "--json",
                },
                [
                    "build-library", "-", "--frontend-command",
                    "frontend-build-library", "--source-name", "input.terrum",
                    "--export", "Main", "-o",
                    Path.Combine(directory, "layout.dll"), "--runtime",
                    "fail-fast", "--layout-core-manifest", manifestPath,
                    "--json",
                ],
                [
                    "jit", "-", "--frontend-command", "frontend-run",
                    "--source-name", "input.terrum", "--runtime",
                    "fail-fast", "--layout-core-manifest", manifestPath,
                    "--json",
                ],
            };
            foreach (var arguments in commandCases)
            {
                var (exit, output, error) = await RunAsync(source, arguments).ConfigureAwait(false);

                Assert.That(exit, Is.EqualTo(2), output + error);
                AssertInputDiagnosticCode(output, "TRM2000");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task NativeModuleCommandsDoNotAcceptLayoutCoreManifestAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-layout-core-manifest-private-");
        var manifestPath = Path.Combine(directory, "layout.json");
        try
        {
            await File.WriteAllTextAsync(manifestPath,
                CreateLayoutCoreManifest()).ConfigureAwait(false);
            var source = Encoding.UTF8.GetBytes(LayoutCoreSource);
            var commandCases = new[] {
                new[] {
                    "compile-native", "producer", "-", "sample.provider",
                    "--export", "Main", "-o",
                    Path.Combine(directory, "native.trmn"), "--runtime",
                    "fail-fast", "--layout-core-manifest", manifestPath,
                    "--json",
                },
                [
                    "compile-native-set", "-",
                    "--provider", Path.Combine(directory, "first.trmn"),
                    "--provider", Path.Combine(directory, "second.trmn"),
                    "-o", Path.Combine(directory, "consumer.tnset"),
                    "--runtime", "fail-fast", "--layout-core-manifest",
                    manifestPath, "--json",
                ],
                [
                    "compile-native-graph", "producer", "-",
                    "sample.provider", "--provider",
                    Path.Combine(directory, "dependency.trmn"),
                    "--export", "Main", "-o",
                    Path.Combine(directory, "producer.trng"), "--runtime",
                    "fail-fast", "--layout-core-manifest", manifestPath,
                    "--json",
                ],
            };
            foreach (var arguments in commandCases)
            {
                var (exit, output, error) = await RunAsync(source, arguments).ConfigureAwait(false);

                Assert.That(exit, Is.EqualTo(2), output + error);
                AssertInputDiagnosticCode(output, "TRM2000");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task SourceFreeArtifactCommandsDoNotAcceptLayoutCoreManifestAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-layout-core-manifest-artifact-");
        var manifestPath = Path.Combine(directory, "layout.json");
        try
        {
            await File.WriteAllTextAsync(manifestPath,
                CreateLayoutCoreManifest()).ConfigureAwait(false);
            var source = Encoding.UTF8.GetBytes(LayoutCoreSource);
            var commandCases = new[] {
                new[] {
                    "build-trrm", Path.Combine(directory, "module.trrm"),
                    "-o", Path.Combine(directory, "program.exe"), "--runtime",
                    "fail-fast", "--layout-core-manifest", manifestPath,
                    "--json",
                },
                [
                    "run-trrm", Path.Combine(directory, "module.trrm"),
                    "--runtime", "fail-fast", "--layout-core-manifest",
                    manifestPath, "--json",
                ],
                [
                    "link-module", Path.Combine(directory, "consumer.trm"),
                    Path.Combine(directory, "producer.trm"), "-o",
                    Path.Combine(directory, "module.exe"), "--runtime",
                    "fail-fast", "--layout-core-manifest", manifestPath,
                    "--json",
                ],
                [
                    "run-module", Path.Combine(directory, "consumer.trm"),
                    Path.Combine(directory, "producer.trm"), "--runtime",
                    "fail-fast", "--layout-core-manifest", manifestPath,
                    "--json",
                ],
            };
            foreach (var arguments in commandCases)
            {
                var (exit, output, error) = await RunAsync(source, arguments).ConfigureAwait(false);

                Assert.That(exit, Is.EqualTo(2), output + error);
                AssertInputDiagnosticCode(output, "TRM2000");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task LayoutCoreManifestOptionRejectsDuplicatesAndInvalidOperandsAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-layout-core-manifest-options-");
        var manifestPath = Path.Combine(directory, "layout.json");
        try
        {
            await File.WriteAllTextAsync(manifestPath,
                CreateLayoutCoreManifest()).ConfigureAwait(false);
            var source = Encoding.UTF8.GetBytes(LayoutCoreSource);
            var invalidArguments = new[] {
                new[] {
                    "analyze", "-", "--layout-core-manifest", manifestPath,
                    "--layout-core-manifest", manifestPath, "--json",
                },
                [
                    "analyze", "-", "--layout-core-manifest=" + manifestPath,
                    "--json",
                ],
                [
                    "analyze", "-", "--layout-core-manifest", "",
                    "--json",
                ],
                [
                    "analyze", "-", "--layout-core-manifest", "-layout.json",
                    "--json",
                ],
                [
                    "compile-module", "producer", "-", "Main", "-o",
                    Path.Combine(directory, "module.trm"),
                    "--layout-core-manifest", manifestPath,
                    "--layout-core-manifest", manifestPath, "--json",
                ],
                [
                    "compile-module", "producer", "-", "Main", "-o",
                    Path.Combine(directory, "module.trm"),
                    "--layout-core-manifest=" + manifestPath, "--json",
                ],
                [
                    "compile-module", "producer", "-", "Main", "-o",
                    Path.Combine(directory, "module.trm"),
                    "--layout-core-manifest", "", "--json",
                ],
                [
                    "compile-module", "producer", "-", "Main", "-o",
                    Path.Combine(directory, "module.trm"),
                    "--layout-core-manifest", "-layout.json", "--json",
                ],
            };
            foreach (var arguments in invalidArguments)
            {
                var (exit, output, error) = await RunAsync(source, arguments).ConfigureAwait(false);
                Assert.That(exit, Is.EqualTo(2), output + error);
                AssertInputDiagnosticCode(output, "TRM2000");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    internal static string CreateLayoutCoreManifest(
        string usingModuleIdentity = "sample.consumer")
    {
        const string providerIdentity = "sample.layout-core";
        var layoutKindsIdentity = new {
            providerIdentity,
            exportKey = "LayoutKind",
            kind = "enum-type",
        };
        object CreateMember(string name, int value)
        {
            return new {
                identity = new {
                    providerIdentity,
                    exportKey = $"LayoutKind.{name}",
                    kind = "enum-member",
                },
                declaringEnum = layoutKindsIdentity,
                declarationKey = $"layout-kind/member/{name}",
                name,
                value,
                isPublic = true,
            };
        }

        var layoutArguments = new object[] {
            new {
                name = "Kind",
                kind = "layout-kind",
                enumType = (object?)layoutKindsIdentity,
                isRequired = true,
                allowsPositional = true,
                defaultUInt32 = (uint?)null,
            },
            new {
                name = "Packing",
                kind = "uint32",
                enumType = (object?)null,
                isRequired = false,
                allowsPositional = false,
                defaultUInt32 = (uint?)16,
            },
            new {
                name = "Alignment",
                kind = "uint32",
                enumType = (object?)null,
                isRequired = false,
                allowsPositional = false,
                defaultUInt32 = (uint?)null,
            },
        };
        var manifest = new {
            schemaVersion = 1,
            coreAuthorityIdentity = "test-layout-authority",
            usingModuleIdentity,
            providers = new[] {
                new {
                    moduleIdentity = providerIdentity,
                    revisionIdentity = "revision-1",
                    schema = new {
                        identity = "layout-registration",
                        revision = 1,
                    },
                    governingSemanticEdition = 1,
                    targetRequirement = 0,
                    runtimePolicy = 0,
                },
            },
            layoutKinds = new {
                identity = layoutKindsIdentity,
                qualifiedName = "LayoutKind",
                underlyingType = "int32",
                members = new[] {
                    CreateMember("Auto", 0),
                    CreateMember("Sequential", 1),
                    CreateMember("Union", 2),
                    CreateMember("Transparent", 3),
                },
                genericArity = 0,
                isPublic = true,
                isPrimary = true,
                isFlags = false,
                blittableTargetRequirement = 0,
            },
            layout = new {
                identity = new {
                    providerIdentity,
                    exportKey = "Layout",
                    kind = "attribute-schema",
                },
                qualifiedName = "Layout",
                arguments = layoutArguments,
                targets = new[] { "inline-struct" },
                isPrimaryTargetOnly = true,
                genericArity = 0,
                isPublic = true,
                isPrimary = true,
                isRepeatable = false,
            },
            blittable = new {
                identity = new {
                    providerIdentity,
                    exportKey = "Blittable",
                    kind = "attribute-schema",
                },
                qualifiedName = "Blittable",
                arguments = Array.Empty<object>(),
                targets = new[] {
                    "inline-struct",
                    "integer-backed-enum",
                },
                isPrimaryTargetOnly = true,
                genericArity = 0,
                isPublic = true,
                isPrimary = true,
                isRepeatable = false,
            },
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static async Task<(int ExitCode, string Output, string Error)>
        RunScannerCommandWithLayoutAsync(byte[] input,
            params string[] arguments)
    {
        if (arguments.Length == 0
            || arguments[0] is not ("scan-component" or "build-component")
            || arguments.Contains("--layout-core-manifest",
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "The scanner test helper requires an unconfigured scanner-component command.",
                nameof(arguments));
        }

        var directory = TestArtifactPaths.CreateDirectory(
            "scanner-layout-manifest-");
        var manifestPath = Path.Combine(directory, "layout.json");
        try
        {
            await File.WriteAllTextAsync(manifestPath,
                CreateLayoutCoreManifest("sample.scanner-tests"))
                .ConfigureAwait(true);
            var trailingJson = arguments[^1] == "--json";
            var optionCount = arguments.Length - (trailingJson ? 1 : 0);
            var configuredArguments = new List<string>(arguments.Length + 2);
            configuredArguments.AddRange(arguments.Take(optionCount));
            configuredArguments.Add("--layout-core-manifest");
            configuredArguments.Add(manifestPath);
            if (trailingJson)
            {
                configuredArguments.Add("--json");
            }

            return await RunAsync(input, [.. configuredArguments])
                .ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    private static void AssertInputDiagnosticCode(string output,
        string expectedCode)
    {
        using var result = JsonDocument.Parse(output);
        Assert.That(result.RootElement.GetProperty("diagnostics")
                .EnumerateArray()
                .Any(diagnostic => StringComparer.Ordinal.Equals(
                    diagnostic.GetProperty("code").GetString(),
                    expectedCode)),
            Is.True, output);
    }
}
