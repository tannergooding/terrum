// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Modules.TrRm;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int TrRmAdd(int left, int right);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate long TrRmIdentity(long value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int StandardFNativeFunction();

    [Test]
    public async Task CompileTrRmSelectsTheExactNominalEnumsSchemaAsync()
    {
        const string source = "public int32 GetValue() { return 42; }";
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-schema-selection-{Guid.NewGuid():N}.trrm");
        try
        {
            var (exit, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-",
                "--identity", "scalar-module",
                "--trrm-schema", "trrm/1-draft-nominal-enums-1",
                "--library", "-o", artifact, "--runtime", "fail-fast",
                "--json");
            Assert.That(exit, Is.Zero, $"{output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);

            var tables = TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForNominalEnumsDraft(
                        await File.ReadAllBytesAsync(artifact).ConfigureAwait(true)));
            Assert.That(tables.EnumDefs.Count, Is.Zero);
            Assert.That(tables.Features.Count, Is.EqualTo(1));
            Assert.That(tables.Features.GetFeature(1),
                Is.EqualTo(TrRmFeatureRow.TypedFrozenValues));
            var loaded = TrRmExecutableCompiler
                .LoadNominalEnumsDraftScalarReturns(
                    await File.ReadAllBytesAsync(artifact).ConfigureAwait(true));
            var result = (BoundConstant)((BoundReturn)
                loaded.Functions[0].Body.Statements[0]).Value!;
            Assert.That(loaded.EnumDefinitions, Is.Empty);
            Assert.That(result.Bits.Low, Is.EqualTo(42UL));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(artifact)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task CompileTrRmRejectsInvalidSchemaSelectionWithoutReplacingOutputAsync()
    {
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-schema-selection-invalid-{Guid.NewGuid():N}.trrm");
        byte[] retained = [0xAA, 0x55];
        await File.WriteAllBytesAsync(artifact, retained).ConfigureAwait(true);
        try
        {
            var (exit, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("public int32 GetValue() { return 42; }"),
                "compile-trrm", "-", "--identity", "scalar-module",
                "--trrm-schema", "TRRM/1-draft-nominal-enums-1",
                "--library", "-o", artifact, "--runtime", "fail-fast",
                "--json");
            Assert.That(exit, Is.EqualTo(2), $"{output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
            Assert.That(await File.ReadAllBytesAsync(artifact).ConfigureAwait(true),
                Is.EqualTo(retained));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(artifact)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task CompileTrRmRejectsFlagFreeA3IdentityWithoutReplacingOutputAsync()
    {
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-flag-free-a3-selection-{Guid.NewGuid():N}.trrm");
        byte[] retained = [0xAA, 0x55];
        await File.WriteAllBytesAsync(artifact, retained).ConfigureAwait(true);
        try
        {
            var (exit, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("public int32 GetValue() { return 42; }"),
                "compile-trrm", "-", "--identity", "scalar-module",
                "--trrm-schema",
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3",
                "--library", "-o", artifact, "--runtime", "fail-fast",
                "--json");
            Assert.That(exit, Is.EqualTo(2), $"{output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
            Assert.That(await File.ReadAllBytesAsync(artifact).ConfigureAwait(true),
                Is.EqualTo(retained));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(artifact)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task CompileTrRmSchemaOptionRejectsMalformedAndWrongCommandUseAsync()
    {
        async Task AssertUsageAsync(params string[] arguments)
        {
            var (exit, output, error) = await RunAsync([], arguments)
                .ConfigureAwait(true);
            Assert.That(exit, Is.EqualTo(2), $"{output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
        }

        await AssertUsageAsync("compile-trrm", "-", "--identity", "module",
            "--trrm-schema", "--library", "-o", "unused.trrm", "--runtime",
            "fail-fast", "--json");
        await AssertUsageAsync("compile-trrm", "-", "--identity", "module",
            "--trrm-schema", "", "-o", "unused.trrm", "--runtime",
            "fail-fast", "--json");
        await AssertUsageAsync("compile-trrm", "-", "--identity", "module",
            "--trrm-schema", "trrm/1-draft-layout-queries-1",
            "--trrm-schema", "trrm/1-draft-nominal-enums-1", "--library",
            "-o", "unused.trrm", "--runtime", "fail-fast", "--json");
        await AssertUsageAsync("build-trrm", "unused.trrm",
            "--trrm-schema", "trrm/1-draft-nominal-enums-1", "-o",
            "unused.exe", "--runtime", "fail-fast", "--json");
    }

    [Test]
    public async Task TrRmStandardSelectionPreflightsOptionsBeforeReadingAsync()
    {
        async Task AssertUsageAsync(params string[] arguments)
        {
            var (exit, output, error) = await RunAsync([], arguments)
                .ConfigureAwait(true);
            Assert.That(exit, Is.EqualTo(2),
                $"{output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
        }

        await AssertUsageAsync("run-trrm", "missing.trrm", "--runtime",
            "fail-fast", "--trrm-schema",
            "TRRM/1-draft-foreign-invocation-contracts-1", "--trrm-module",
            "root", "--json");
        await AssertUsageAsync("run-trrm", "missing.trrm", "--runtime",
            "fail-fast", "--trrm-module", "root", "--json");
        await AssertUsageAsync("run-trrm", "missing.trrm", "--runtime",
            "fail-fast", "--trrm-provider",
            "trrm/1-draft-foreign-invocation-contracts-1", "missing-provider.trrm",
            "--json");
        await AssertUsageAsync("run-trrm", "missing.trrm", "--runtime",
            "fail-fast", "--trrm-schema",
            "trrm/1-draft-foreign-invocation-contracts-1", "--trrm-module",
            "root", "--trrm-provider", "unsupported-schema",
            "missing-provider.trrm", "--json");
        await AssertUsageAsync("run-trrm", "missing.trrm", "--runtime",
            "fail-fast", "--trrm-schema",
            "trrm/1-draft-register-exception-regions-1", "--trrm-module",
            "root", "--trrm-provider",
            "trrm/1-draft-foreign-invocation-contracts-1",
            "missing-provider.trrm", "--json");
        await AssertUsageAsync("run-trrm", "missing.trrm", "--runtime",
            "fail-fast", "--trrm-core-exception-anchor", "root",
            "unknown-role", "core", "Interface", "--json");
        await AssertUsageAsync("run-trrm", "missing.trrm", "--runtime",
            "fail-fast", "--trrm-schema",
            "trrm/1-draft-register-exception-regions-1", "--trrm-module",
            "root", "--trrm-core-exception-anchor", "root",
            "exception-interface", "core", "Interface", "--json");
        await AssertUsageAsync("run-trrm", "missing.trrm", "--runtime",
            "fail-fast", "--trrm-schema",
            "trrm/1-draft-register-exception-regions-1", "--trrm-module",
            "root", "--trrm-core-exception-anchor", "root",
            "exception-interface", "core", "Interface",
            "--trrm-core-exception-anchor", "root",
            "exception-interface", "core", "OtherInterface", "--json");
        await AssertUsageAsync("compile-trrm", "-", "--identity", "root",
            "--trrm-core-exception-anchor", "root",
            "exception-interface", "core", "Interface", "--json");

        var collidingPath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-provider-output-{Guid.NewGuid():N}.trrm");
        await AssertUsageAsync("build-trrm", "missing.trrm", "-o",
            collidingPath, "--runtime", "fail-fast", "--trrm-schema",
            "trrm/1-draft-foreign-invocation-contracts-1", "--trrm-module",
            "root", "--trrm-provider",
            "trrm/1-draft-foreign-invocation-contracts-1", collidingPath,
            "--json");
    }

    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1",
        false, false)]
    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1",
        true, false)]
    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1",
        true, true)]
    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1",
        true, false)]
    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1",
        true, true)]
    public async Task BuildTrRmSelectsEmptyArrayArtifactSchemaAsync(
        string schemaIdentity, bool includesUnitValues,
        bool includesDistinctUnitCalls)
    {
        const string moduleIdentity = "a2e-cli-selection";
        var artifactPath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"a2e-cli-{Guid.NewGuid():N}.trrm");
        var imagePath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"a2e-cli-{Guid.NewGuid():N}.exe");
        try
        {
            var options = new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            };
            var markImports = includesDistinctUnitCalls ? """
                [Terrum.Interop.NativeImport(
                    "example.dll",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                internal extern void MarkFirst();

                [Terrum.Interop.NativeImport(
                    "example.dll",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                internal extern void MarkSecond();
                """ : "";
            var unitInitializers = includesDistinctUnitCalls
                ? "MarkFirst(), MarkSecond()" : "(), ()";
            var source = includesUnitValues ? $$"""
                {{markImports}}
                [Terrum.Interop.NativeImport(
                    "example.dll",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
                [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
                internal extern void Observe(
                    ptr<uint32> value,
                    write ptr<int32> output);

                int32 Main() {
                    int32 zeroIndex = 0;
                    [uint32; 0] empty = [];
                    mut uint32 input = 0;
                    mut int32 output = 0;
                    unsafe {
                        mut [void; 2] units = [{{unitInitializers}}];
                        var callback = &Observe;
                        callback(&input, &output);
                    }
                    return output;
                }
                """ : """
                [Terrum.Interop.NativeImport(
                    "example.dll",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
                [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
                internal extern void Observe(
                    ptr<uint32> value,
                    write ptr<int32> output);

                int32 Main() {
                    [uint32; 0] empty = [];
                    mut uint32 input = 0;
                    mut int32 output = 0;
                    unsafe {
                        var callback = &Observe;
                        callback(&input, &output);
                    }
                    return output;
                }
                """;
            var compilation = Compilation.Create(SyntaxTree.Parse(source), options);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var artifact = TrRmExecutableCompiler.EmitStandard(
                compilation.Program!, moduleIdentity, schemaIdentity, 1);
            await File.WriteAllBytesAsync(artifactPath, artifact)
                .ConfigureAwait(true);

            var (exit, output, error) = await RunAsync([], "build-trrm",
                artifactPath, "--runtime", "fail-fast", "--trrm-schema",
                schemaIdentity, "--trrm-module", moduleIdentity, "-o",
                imagePath, "--json").ConfigureAwait(true);
            Assert.That(exit, Is.Zero,
                $"{output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            Assert.That(File.Exists(imagePath), Is.True);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success")
                .GetBoolean(), Is.True);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(artifactPath)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedTrRmRunsAndBuildsWithoutProducerSourceAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-consumer-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { mut int32 x = 20; x = x + 22; return x; }"),
                "compile-trrm", "-", "--identity", "consumer", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);
            Assert.That(File.Exists(artifact), Is.True);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 42, Is.True, $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(42));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task RunTrRmExecutesUnsignedNestedIndexUpdatesInChildWorkerAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        foreach (var unsignedIndexType in new[] {
            PrimitiveType.UInt8,
            PrimitiveType.UInt16,
            PrimitiveType.UInt32,
            PrimitiveType.UInt64,
            PrimitiveType.UInt128,
        })
        {
            var moduleIdentity
                = $"a2-{unsignedIndexType.ToString().ToLowerInvariant()}-cli-worker";
            var artifactPath = Path.Combine(TestArtifactPaths.DirectoryPath,
                $"trrm-{unsignedIndexType.ToString().ToLowerInvariant()}-index-{Guid.NewGuid():N}.trrm");
            try
            {
                var input = TrRmForeignInvocationAddressEmitterTests
                    .CreateA2NestedArrayReadbackInput(moduleIdentity,
                        0, 0, 1, out _, effectfulIndices: true,
                        counterObservedIndexUpdates: true,
                        unsignedIndexType: unsignedIndexType);
                await File.WriteAllBytesAsync(artifactPath,
                    input.Artifact.ToArray()).ConfigureAwait(true);

                var (exit, output, error) = await RunAsync([], "run-trrm",
                    artifactPath, "--runtime", "fail-fast",
                    "--trrm-schema", schemaIdentity, "--trrm-module",
                    moduleIdentity, "--json").ConfigureAwait(true);
                Assert.That(exit, Is.EqualTo(0xFFFF),
                    $"{unsignedIndexType}: {output}{Environment.NewLine}{error}");
                Assert.That(output, Is.Empty);
                Assert.That(error, Is.Empty);
            }
            finally
            {
                await TestFileCleanup.DeleteWithRetryAsync(artifactPath)
                    .ConfigureAwait(true);
            }
        }
    }

    [Test]
    public async Task RunTrRmExecutesNestedNativeParameterSuffixCasesInChildWorkerAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string a2euSchema
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string a2euScalarFrozenValuesSchema
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
        var cases = new[] {
            (Name: "q2-positive", Schema: a2euScalarFrozenValuesSchema,
                Extent: 2u, CounterObserved: false, ExpectedExitCode: -1,
                BuildLibrary: true),
            (Name: "q3-positive", Schema: a2euSchema,
                Extent: 3u, CounterObserved: true, ExpectedExitCode: 0xFFFF,
                BuildLibrary: true),
            (Name: "q4-short-suffix", Schema: a2euSchema,
                Extent: 4u, CounterObserved: true,
                ExpectedExitCode: unchecked((int)0xE0540004),
                BuildLibrary: false),
        };
        foreach (var testCase in cases)
        {
            var moduleIdentity = $"a2-{testCase.Name}-cli-worker";
            var artifactPath = Path.Combine(TestArtifactPaths.DirectoryPath,
                $"trrm-{testCase.Name}-{Guid.NewGuid():N}.trrm");
            var imagePath = Path.ChangeExtension(artifactPath, ".exe");
            var libraryPath = Path.ChangeExtension(artifactPath, ".dll");
            var libraryArtifactPath
                = Path.ChangeExtension(artifactPath, ".library.trrm");
            try
            {
                var input = TrRmForeignInvocationAddressEmitterTests
                    .CreateA2NestedArrayReadbackInput(moduleIdentity,
                        0, 0, 1, out _, effectfulIndices: true,
                        counterObservedIndexUpdates:
                            testCase.CounterObserved,
                        nativeParameterExtent: testCase.Extent,
                        schemaIdentityOverride: testCase.Schema);
                Assert.That(input.SchemaIdentity,
                    Is.EqualTo(testCase.Schema));
                await File.WriteAllBytesAsync(artifactPath,
                    input.Artifact.ToArray()).ConfigureAwait(true);

                var (exit, output, error) = await RunAsync([], "run-trrm",
                    artifactPath, "--runtime", "fail-fast", "--trrm-schema",
                    testCase.Schema, "--trrm-module", moduleIdentity, "--json")
                    .ConfigureAwait(true);
                Assert.That(exit, Is.EqualTo(testCase.ExpectedExitCode),
                    $"{testCase.Name}: {output}{Environment.NewLine}{error}");
                Assert.That(output, Is.Empty);
                Assert.That(error, Is.Empty);

                var (built, buildOutput, buildError) = await RunAsync([],
                    "build-trrm", artifactPath, "-o", imagePath, "--runtime",
                    "fail-fast", "--trrm-schema", testCase.Schema,
                    "--trrm-module", moduleIdentity, "--json")
                    .ConfigureAwait(true);
                Assert.That(built, Is.Zero,
                    $"{testCase.Name}: {buildOutput}{Environment.NewLine}{buildError}");
                Assert.That(buildError, Is.Empty);
                using (var buildResult = JsonDocument.Parse(buildOutput))
                {
                    Assert.That(buildResult.RootElement
                        .GetProperty("success").GetBoolean(), Is.True);
                }
                Assert.That(await ExecuteTrRmImageAsync(imagePath)
                    .ConfigureAwait(true),
                    Is.EqualTo(testCase.ExpectedExitCode));

                if (testCase.BuildLibrary)
                {
                    var libraryInput
                        = TrRmForeignInvocationAddressEmitterTests
                            .CreateA2NestedArrayReadbackInput(moduleIdentity,
                                0, 0, 1, out _, effectfulIndices: true,
                                counterObservedIndexUpdates:
                                    testCase.CounterObserved,
                                nativeParameterExtent: testCase.Extent,
                                schemaIdentityOverride: testCase.Schema,
                                isLibrary: true);
                    await File.WriteAllBytesAsync(libraryArtifactPath,
                        libraryInput.Artifact.ToArray())
                        .ConfigureAwait(true);
                    var (libraryBuilt, libraryOutput, libraryError)
                        = await RunAsync([], "build-trrm-library",
                            libraryArtifactPath, "--export", "Main", "-o",
                            libraryPath, "--runtime", "fail-fast",
                            "--trrm-schema", testCase.Schema, "--trrm-module",
                            moduleIdentity, "--json").ConfigureAwait(true);
                    Assert.That(libraryBuilt, Is.Zero,
                        $"{testCase.Name}: {libraryOutput}{Environment.NewLine}{libraryError}");
                    Assert.That(libraryError, Is.Empty);
                    using (var libraryResult
                           = JsonDocument.Parse(libraryOutput))
                    {
                        Assert.That(libraryResult.RootElement
                            .GetProperty("success").GetBoolean(), Is.True);
                    }

                    var library = NativeLibrary.Load(libraryPath);
                    try
                    {
                        var main = Marshal.GetDelegateForFunctionPointer<
                            StandardFNativeFunction>(NativeLibrary.GetExport(
                                library, "Main"));
                        Assert.That(main(),
                            Is.EqualTo(testCase.ExpectedExitCode));
                    }
                    finally
                    {
                        NativeLibrary.Free(library);
                    }
                }
            }
            finally
            {
                await TestFileCleanup.DeleteWithRetryAsync(artifactPath)
                    .ConfigureAwait(true);
                await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                    .ConfigureAwait(true);
                await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                    .ConfigureAwait(true);
                await TestFileCleanup.DeleteWithRetryAsync(
                    libraryArtifactPath).ConfigureAwait(true);
            }
        }
    }

    [Test]
    public async Task RunBuildAndLibraryTrRmResolveA2NativeAddressProvidersAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string providerIdentity = "a2-cli-native-address-provider";
        const string consumerIdentity = "a2-cli-native-address-consumer";
        const string libraryIdentity = "a2-cli-native-address-library";
        const string providerSource = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            public extern void Observe(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            internal int32 ProviderBody() {
                int32 zeroIndex = 0;
                mut [int32; 2] values = [0, -1];
                mut int32 observed = values[zeroIndex];
                uint64 byteCount = 0;
                unsafe {
                    var callback = &Observe;
                    callback(&observed, &values[zeroIndex], byteCount);
                }
                return observed;
            }
            """;

        static string CreateConsumerSource(string functionDeclaration)
            => $$"""
                {{functionDeclaration}} {
                    int32 zeroIndex = 0;
                    mut int32 sequence = 0;
                    mut [[[int32; 2]; 2]; 2] values = [
                        [[0, -1], [-1, 0]],
                        [[-1, 0], [0, -1]]];
                    uint64 byteCount = 0;
                    unsafe {
                        var callback = &Observe;
                        callback(&sequence,
                            &values[sequence++][zeroIndex][zeroIndex],
                            byteCount);
                    }
                    return sequence;
                }
                """;

        var coreReference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests
                .CreateRegistration());
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), coreReference);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var incompatibleProviderSource = providerSource
            .Replace("NativeParameter(1, Write, NoEscape, 1)",
                "NativeParameter(1, Read, NoEscape, 1)",
                StringComparison.Ordinal)
            .Replace("write ptr<int32> destination",
                "ptr<int32> destination", StringComparison.Ordinal);
        var incompatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(incompatibleProviderSource), coreReference);
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleProviderCompilation.Program!, providerIdentity,
                schemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse(CreateConsumerSource("int32 Main()")),
            providerReference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program!, consumerIdentity, schemaIdentity, 1);
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(CreateConsumerSource("public int32 Exercise()")),
            providerReference, options);
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var libraryArtifact = TrRmExecutableCompiler.EmitStandard(
            libraryCompilation.Program!, libraryIdentity, schemaIdentity, 1);

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-a2-native-address-provider-");
        try
        {
            var providerPath = Path.Combine(directory, "provider.trrm");
            var incompatibleProviderPath
                = Path.Combine(directory, "incompatible-provider.trrm");
            var consumerPath = Path.Combine(directory, "consumer.trrm");
            var executablePath = Path.Combine(directory, "consumer.exe");
            var libraryArtifactPath
                = Path.Combine(directory, "library.trrm");
            var libraryPath = Path.Combine(directory, "consumer.dll");
            await File.WriteAllBytesAsync(providerPath, providerArtifact)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(incompatibleProviderPath,
                incompatibleProviderArtifact).ConfigureAwait(true);
            await File.WriteAllBytesAsync(consumerPath, consumerArtifact)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(libraryArtifactPath, libraryArtifact)
                .ConfigureAwait(true);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm",
                consumerPath, "--runtime", "fail-fast", "--trrm-schema",
                schemaIdentity, "--trrm-module", consumerIdentity,
                "--trrm-provider", schemaIdentity, providerPath, "--json")
                .ConfigureAwait(true);
            Assert.That(ran, Is.EqualTo(1),
                $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm", consumerPath, "-o", executablePath, "--runtime",
                "fail-fast", "--trrm-schema", schemaIdentity, "--trrm-module",
                consumerIdentity, "--trrm-provider", schemaIdentity,
                providerPath, "--json").ConfigureAwait(true);
            Assert.That(built, Is.Zero,
                $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            using (var buildResult = JsonDocument.Parse(buildOutput))
            {
                Assert.That(buildResult.RootElement
                    .GetProperty("success").GetBoolean(), Is.True);
            }
            Assert.That(await ExecuteTrRmImageAsync(executablePath)
                .ConfigureAwait(true), Is.EqualTo(1));

            var (libraryBuilt, libraryOutput, libraryError) = await RunAsync(
                [], "build-trrm-library", libraryArtifactPath, "--export",
                "Exercise", "-o", libraryPath, "--runtime", "fail-fast",
                "--trrm-schema", schemaIdentity, "--trrm-module",
                libraryIdentity, "--trrm-provider", schemaIdentity,
                providerPath, "--json").ConfigureAwait(true);
            Assert.That(libraryBuilt, Is.Zero,
                $"{libraryOutput}{Environment.NewLine}{libraryError}");
            Assert.That(libraryError, Is.Empty);
            using (var libraryResult = JsonDocument.Parse(libraryOutput))
            {
                Assert.That(libraryResult.RootElement
                    .GetProperty("success").GetBoolean(), Is.True);
            }

            var library = NativeLibrary.Load(libraryPath);
            try
            {
                var exercise = Marshal.GetDelegateForFunctionPointer<
                    StandardFNativeFunction>(NativeLibrary.GetExport(
                        library, "Exercise"));
                Assert.That(exercise(), Is.EqualTo(1));
            }
            finally
            {
                NativeLibrary.Free(library);
            }

            var retainedOutputPath
                = Path.Combine(directory, "retained.exe");
            byte[] retainedOutput = [0xA5, 0x5A, 0xC3, 0x3C];
            await File.WriteAllBytesAsync(retainedOutputPath, retainedOutput)
                .ConfigureAwait(true);
            var (rejected, rejectionOutput, rejectionError) = await RunAsync(
                [], "build-trrm", consumerPath, "-o", retainedOutputPath,
                "--runtime", "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", consumerIdentity, "--trrm-provider",
                schemaIdentity, incompatibleProviderPath, "--json")
                .ConfigureAwait(true);
            Assert.That(rejected, Is.EqualTo(1),
                $"{rejectionOutput}{Environment.NewLine}{rejectionError}");
            Assert.That(rejectionError, Is.Empty);
            using (var rejectionDocument = JsonDocument.Parse(
                       rejectionOutput))
            {
                Assert.That(rejectionDocument.RootElement
                        .GetProperty("diagnostics")[0]
                        .GetProperty("code").GetString(),
                    Is.EqualTo("TRM5001"));
            }
            Assert.That(await File.ReadAllBytesAsync(retainedOutputPath)
                .ConfigureAwait(true), Is.EqualTo(retainedOutput));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task RegisterExceptionRegionsStandardProfileRunsAndBuildsScalarBodiesAsync()
    {
        const string schemaIdentity
            = "trrm/1-draft-register-exception-regions-1";
        const string moduleIdentity = "e1-cli-module";
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-e1-profile-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                "compile-trrm", "-", "--identity", moduleIdentity,
                "--trrm-schema", schemaIdentity, "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled, Is.Zero,
                $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm",
                artifact, "--runtime", "fail-fast", "--trrm-schema",
                schemaIdentity, "--trrm-module", moduleIdentity, "--json");
            Assert.That(ran, Is.EqualTo(42),
                $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (invalidSelection, selectionOutput, selectionError)
                = await RunAsync([], "build-trrm", artifact, "-o", executable,
                    "--runtime", "fail-fast", "--trrm-schema",
                    schemaIdentity, "--trrm-module", moduleIdentity,
                    "--trrm-core-exception-anchor", moduleIdentity,
                    "exception-interface", moduleIdentity, "missing-interface",
                    "--trrm-core-exception-anchor", moduleIdentity,
                    "unreachable-payload", moduleIdentity,
                    "missing-unreachable", "--json");
            Assert.That(invalidSelection, Is.EqualTo(1),
                $"{selectionOutput}{Environment.NewLine}{selectionError}");
            Assert.That(selectionError, Is.Empty);
            using (var selectionDocument = JsonDocument.Parse(selectionOutput))
            {
                Assert.That(selectionDocument.RootElement
                    .GetProperty("diagnostics")[0].GetProperty("code")
                    .GetString(), Is.EqualTo("TRM5001"));
            }
            Assert.That(File.Exists(executable), Is.False);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm",
                artifact, "-o", executable, "--runtime", "fail-fast",
                "--trrm-schema", schemaIdentity, "--trrm-module",
                moduleIdentity, "--json");
            Assert.That(built, Is.Zero,
                $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable),
                Is.EqualTo(42));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(artifact)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(executable)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task SelectedFProfileRunsBuildsAndEmitsLibraryWithDirectNativeImportAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "cli-standard-native-address";
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern int32 GetCurrentProcessId();

            int32 Main() {
                unsafe {
                    var callback = &GetCurrentProcessId;
                    return callback();
                }
            }
            """), core);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-f-profile-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        var libraryPrefix = prefix + "-library";
        var libraryArtifact = libraryPrefix + ".trrm";
        var library = libraryPrefix + ".dll";
        try
        {
            await File.WriteAllBytesAsync(artifact,
                TrRmExecutableCompiler.EmitStandard(compilation.Program!,
                    moduleIdentity, schemaIdentity, 1)).ConfigureAwait(true);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm",
                artifact, "--runtime", "fail-fast", "--trrm-schema",
                schemaIdentity, "--trrm-module", moduleIdentity, "--json")
                .ConfigureAwait(true);
            Assert.That(ran, Is.GreaterThan(0),
                $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm", artifact, "-o", executable, "--runtime",
                "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", moduleIdentity, "--json")
                .ConfigureAwait(true);
            Assert.That(built, Is.Zero,
                $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            using var document = JsonDocument.Parse(buildOutput);
            Assert.That(document.RootElement.GetProperty("success")
                .GetBoolean(), Is.True);
            Assert.That(await ExecuteTrRmImageAsync(executable),
                Is.GreaterThan(0));

            var libraryCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse("""
                    [Terrum.Interop.NativeImport(
                        "kernel32.dll",
                        "GetCurrentProcessId",
                        CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                    internal extern int32 GetCurrentProcessId();

                    public int32 ReadProcessId() {
                        unsafe {
                            var callback = &GetCurrentProcessId;
                            return callback();
                        }
                    }
                    """), core);
            Assert.That(libraryCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    libraryCompilation.Diagnostics));
            await File.WriteAllBytesAsync(libraryArtifact,
                TrRmExecutableCompiler.EmitStandard(
                    libraryCompilation.Program!,
                    "cli-standard-native-address-library",
                    schemaIdentity, 1)).ConfigureAwait(true);
            var (libraryBuilt, libraryOutput, libraryError) = await RunAsync(
                [], "build-trrm-library", libraryArtifact, "--export",
                "ReadProcessId", "-o", library, "--runtime", "fail-fast",
                "--trrm-schema", schemaIdentity, "--trrm-module",
                "cli-standard-native-address-library", "--json");
            Assert.That(libraryBuilt, Is.Zero,
                $"{libraryOutput}{Environment.NewLine}{libraryError}");
            Assert.That(libraryError, Is.Empty);
            Assert.That(Encoding.ASCII.GetString(
                await File.ReadAllBytesAsync(library).ConfigureAwait(true)),
                Does.Contain("GetCurrentProcessId")
                    .And.Contain("KERNEL32.dll"));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(libraryArtifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(library)
                .ConfigureAwait(true);
        }
    }

    [TestCase("trrm/1-draft-foreign-invocation-storage-addresses-1")]
    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-2")]
    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1")]
    public async Task SelectedStorageProfileRunsAndBuildsNativeParameterMutationAsync(
        string schemaIdentity)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "cli-native-storage-address";
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetSystemTimeAdjustment",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(3, Write, NoEscape, 1)]
            internal extern int32 GetSystemTimeAdjustment(
                write ptr<uint32> adjustment,
                write ptr<uint32> increment,
                write ptr<int32> disabled);

            int32 Main() {
                mut uint32 adjustment = 0;
                mut uint32 increment = 0;
                mut int32 disabled = 0;
                unsafe {
                    var callback = &GetSystemTimeAdjustment;
                    callback(&adjustment, &increment, &disabled);
                }
                return disabled;
            }
            """), core, BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-f-plus-storage-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        var malformedArtifact = prefix + "-malformed.trrm";
        var retainedOutput = prefix + "-retained.exe";
        byte[] retainedBytes = [0xA5, 0x5A, 0xC3, 0x3C];
        try
        {
            await File.WriteAllBytesAsync(artifact,
                TrRmExecutableCompiler.EmitStandard(compilation.Program!,
                    moduleIdentity, schemaIdentity, 1)).ConfigureAwait(true);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm",
                artifact, "--runtime", "fail-fast", "--trrm-schema",
                schemaIdentity, "--trrm-module", moduleIdentity, "--json");
            Assert.That(ran, Is.EqualTo(1),
                $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm", artifact, "-o", executable, "--runtime",
                "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", moduleIdentity, "--json");
            Assert.That(built, Is.Zero,
                $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            using var document = JsonDocument.Parse(buildOutput);
            Assert.That(document.RootElement.GetProperty("success")
                .GetBoolean(), Is.True);
            Assert.That(await ExecuteTrRmImageAsync(executable)
                .ConfigureAwait(true),
                Is.EqualTo(1));

            var malformed = TrRmForeignInvocationAddressEmitterTests
                .RewriteStorageAddressArtifact(
                    await File.ReadAllBytesAsync(artifact).ConfigureAwait(true),
                    "oversized-scalar-extent");
            await File.WriteAllBytesAsync(malformedArtifact, malformed)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(retainedOutput, retainedBytes)
                .ConfigureAwait(true);
            var (rejected, rejectedOutput, rejectedError) = await RunAsync([],
                "build-trrm", malformedArtifact, "-o", retainedOutput,
                "--runtime", "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", moduleIdentity, "--json")
                .ConfigureAwait(true);
            Assert.That(rejected, Is.Not.Zero,
                $"{rejectedOutput}{Environment.NewLine}{rejectedError}");
            Assert.That(rejectedError, Is.Empty);
            using (var rejection = JsonDocument.Parse(rejectedOutput))
            {
                Assert.That(rejection.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            }
            Assert.That(await File.ReadAllBytesAsync(retainedOutput)
                .ConfigureAwait(true), Is.EqualTo(retainedBytes));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(malformedArtifact);
            await TestFileCleanup.DeleteWithRetryAsync(retainedOutput)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(executable)
                .ConfigureAwait(true);
        }
    }

    [TestCase("invalid", "TRM5001", 1)]
    [TestCase("unsupported", "TRM5006", 1)]
    [TestCase("overflow", "TRM5005", 1)]
    [TestCase("resource", "TRM5007", 2)]
    public async Task A3LayoutAdmissionFailureMapsToItsDedicatedCliCodeAsync(
        string failure, string expectedCode, int expectedExitCode)
    {
        var artifactBytes = failure switch
        {
            "invalid" => TrRmFixedArrayLayoutTableTests
                .CreateA3DirectCycleArtifact(),
            "unsupported" => TrRmFixedArrayLayoutTableTests
                .CreateA3UnsupportedConstraintArtifact(),
            "overflow" => TrRmFixedArrayLayoutTableTests
                .CreateA3NestedClosedArrayExtentOverflowArtifact(),
            "resource" => TrRmFixedArrayLayoutTableTests
                .CreateA3GrowingRecursiveLayoutArtifact(),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-a3-failure-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        byte[] retainedOutput = [0xA5, 0x5A, 0xC3, 0x3C];
        try
        {
            await File.WriteAllBytesAsync(artifact, artifactBytes)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(executable, retainedOutput)
                .ConfigureAwait(true);
            var (exit, output, error) = await RunAsync([], "build-trrm",
                artifact, "-o", executable, "--runtime", "fail-fast",
                "--trrm-schema",
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1",
                "--trrm-module", "Module", "--json");

            Assert.That(exit, Is.EqualTo(expectedExitCode),
                $"{output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success")
                .GetBoolean(), Is.False);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo(expectedCode));
            Assert.That(await File.ReadAllBytesAsync(executable)
                .ConfigureAwait(true), Is.EqualTo(retainedOutput));

            var (runExit, runOutput, runError) = await RunAsync([],
                "run-trrm", artifact, "--runtime", "fail-fast",
                "--trrm-schema",
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1",
                "--trrm-module", "Module", "--json");
            Assert.That(runExit, Is.EqualTo(expectedExitCode),
                $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runError, Is.Empty);
            using var runDocument = JsonDocument.Parse(runOutput);
            Assert.That(runDocument.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo(expectedCode));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task SelectedFProviderRunsBuildsAndEmitsLibraryAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string providerIdentity = "cli-selected-native-provider";
        const string consumerIdentity = "cli-selected-native-consumer";
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        const string providerSource = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            public extern int32 GetCurrentProcessId();

            internal int32 ProviderBody() {
                unsafe {
                    var callback = &GetCurrentProcessId;
                    return callback();
                }
            }
            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), core);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerBytes = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerBytes);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);

        var executableCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    var callback = &GetCurrentProcessId;
                    return callback();
                }
            }
            """), providerReference);
        Assert.That(executableCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                executableCompilation.Diagnostics));
        var executableBytes = TrRmExecutableCompiler.EmitStandard(
            executableCompilation.Program!, consumerIdentity,
            schemaIdentity, 1);

        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 ReadProcessId() {
                    unsafe {
                        var callback = &GetCurrentProcessId;
                        return callback();
                    }
                }
                """), providerReference);
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, libraryCompilation.Diagnostics));
        var libraryBytes = TrRmExecutableCompiler.EmitStandard(
            libraryCompilation.Program!, "cli-selected-native-library",
            schemaIdentity, 1);
        Assert.Throws<ArgumentException>(() =>
            NativeModuleCompiler.EmitStandardLibrary(
                new TrRmStandardArtifactInput(schemaIdentity, libraryBytes),
                "cli-selected-native-library", [providerInput],
                ["GetCurrentProcessId"], NativeRuntimePolicy.FailFast));

        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-f-provider-{Guid.NewGuid():N}");
        var providerPath = prefix + "-provider.trrm";
        var executablePath = prefix + ".trrm";
        var imagePath = prefix + ".exe";
        var libraryPath = prefix + "-library.trrm";
        var libraryImagePath = prefix + ".dll";
        var incompatibleProviderPath = prefix + "-incompatible-provider.trrm";
        var retainedOutputPath = prefix + "-retained.exe";
        byte[] retainedOutput = [0xA5, 0x5A, 0xC3, 0x3C];
        try
        {
            await File.WriteAllBytesAsync(providerPath, providerBytes)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(executablePath, executableBytes)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(libraryPath, libraryBytes)
                .ConfigureAwait(true);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm",
                executablePath, "--runtime", "fail-fast", "--trrm-schema",
                schemaIdentity, "--trrm-module", consumerIdentity,
                "--trrm-provider", schemaIdentity, providerPath, "--json");
            Assert.That(ran, Is.GreaterThan(0),
                $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm", executablePath, "-o", imagePath, "--runtime",
                "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", consumerIdentity, "--trrm-provider",
                schemaIdentity, providerPath, "--json");
            Assert.That(built, Is.Zero,
                $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(imagePath),
                Is.GreaterThan(0));

            var (libraryBuilt, libraryOutput, libraryError) = await RunAsync(
                [], "build-trrm-library", libraryPath, "--export",
                "ReadProcessId", "-o", libraryImagePath, "--runtime",
                "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", "cli-selected-native-library",
                "--trrm-provider", schemaIdentity, providerPath, "--json");
            Assert.That(libraryBuilt, Is.Zero,
                $"{libraryOutput}{Environment.NewLine}{libraryError}");
            Assert.That(libraryError, Is.Empty);
            Assert.That(Encoding.ASCII.GetString(
                await File.ReadAllBytesAsync(libraryImagePath)
                    .ConfigureAwait(true)),
                Does.Contain("GetCurrentProcessId")
                    .And.Contain("KERNEL32.dll"));

            var incompatibleProviderCompilation
                = Compilation.CreateLibrary(SyntaxTree.Parse(
                    providerSource.Replace("kernel32.dll", "user32.dll",
                        StringComparison.Ordinal)), core);
            Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    incompatibleProviderCompilation.Diagnostics));
            await File.WriteAllBytesAsync(incompatibleProviderPath,
                TrRmExecutableCompiler.EmitStandard(
                    incompatibleProviderCompilation.Program!,
                    providerIdentity, schemaIdentity, 1))
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(retainedOutputPath, retainedOutput)
                .ConfigureAwait(true);
            var (rejected, rejectionOutput, rejectionError) = await RunAsync(
                [], "build-trrm", executablePath, "-o", retainedOutputPath,
                "--runtime", "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", consumerIdentity, "--trrm-provider",
                schemaIdentity, incompatibleProviderPath, "--json")
                .ConfigureAwait(true);
            Assert.That(rejected, Is.EqualTo(1),
                $"{rejectionOutput}{Environment.NewLine}{rejectionError}");
            Assert.That(rejectionError, Is.Empty);
            using var rejectionDocument = JsonDocument.Parse(rejectionOutput);
            Assert.That(rejectionDocument.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(retainedOutputPath)
                .ConfigureAwait(true), Is.EqualTo(retainedOutput));
        }
        finally
        {
            File.Delete(providerPath);
            File.Delete(executablePath);
            File.Delete(libraryPath);
            File.Delete(incompatibleProviderPath);
            File.Delete(retainedOutputPath);
            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(libraryImagePath)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task SelectedFMultipleProvidersRunAndBuildAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var (rootArtifact, firstProviderArtifact, secondProviderArtifact, _)
            = TrRmForeignInvocationAddressEmitterTests
                .CreateMultipleRootMethodImportArtifacts(
                    targetMethodName: "Second");
        WriteStandardFArtifactEvidence("multi-provider-root",
            rootIdentity, rootArtifact);
        WriteStandardFArtifactEvidence("multi-provider-first", "First",
            firstProviderArtifact);
        WriteStandardFArtifactEvidence("multi-provider-second", "Second",
            secondProviderArtifact);
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-f-multi-provider-{Guid.NewGuid():N}");
        var rootPath = prefix + "-root.trrm";
        var firstProviderPath = prefix + "-first.trrm";
        var secondProviderPath = prefix + "-second.trrm";
        var imagePath = prefix + ".exe";
        var retainedOutputPath = prefix + "-retained.exe";
        byte[] retainedOutput = [0xA5, 0x5A, 0xC3, 0x3C];
        try
        {
            await File.WriteAllBytesAsync(rootPath, rootArtifact)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(firstProviderPath,
                firstProviderArtifact).ConfigureAwait(true);
            await File.WriteAllBytesAsync(secondProviderPath,
                secondProviderArtifact).ConfigureAwait(true);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm",
                rootPath, "--runtime", "fail-fast", "--trrm-schema",
                schemaIdentity, "--trrm-module", rootIdentity,
                "--trrm-provider", schemaIdentity, firstProviderPath,
                "--trrm-provider", schemaIdentity, secondProviderPath,
                "--json");
            TestContext.WriteLine(
                $"Non-first multi-provider CLI JIT exit: {ran}");
            Assert.That(ran, Is.GreaterThan(0),
                $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm", rootPath, "-o", imagePath, "--runtime",
                "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", rootIdentity, "--trrm-provider",
                schemaIdentity, firstProviderPath, "--trrm-provider",
                schemaIdentity, secondProviderPath, "--json");
            Assert.That(built, Is.Zero,
                $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            var imageResult = await ExecuteTrRmImageAsync(imagePath)
                .ConfigureAwait(true);
            TestContext.WriteLine(
                $"Non-first multi-provider emitted EXE result: {imageResult}");
            Assert.That(imageResult, Is.GreaterThan(0));

            await File.WriteAllBytesAsync(retainedOutputPath, retainedOutput)
                .ConfigureAwait(true);
            var (rejected, rejectionOutput, rejectionError) = await RunAsync(
                [], "build-trrm", rootPath, "-o", retainedOutputPath,
                "--runtime", "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", rootIdentity, "--trrm-provider",
                schemaIdentity, firstProviderPath, "--json")
                .ConfigureAwait(true);
            Assert.That(rejected, Is.EqualTo(1),
                $"{rejectionOutput}{Environment.NewLine}{rejectionError}");
            Assert.That(rejectionError, Is.Empty);
            using var rejectionDocument = JsonDocument.Parse(rejectionOutput);
            Assert.That(rejectionDocument.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(retainedOutputPath)
                .ConfigureAwait(true), Is.EqualTo(retainedOutput));
        }
        finally
        {
            File.Delete(rootPath);
            File.Delete(firstProviderPath);
            File.Delete(secondProviderPath);
            File.Delete(retainedOutputPath);
            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task SelectedFProviderClosureRunsBuildsAndRejectsDeepFailureAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "Root";
        const string providerIdentity = "Owned";
        const string leafIdentity = "Leaf";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var rootArtifact = TrRmForeignInvocationAddressEmitterTests
            .CreateRootMethodImportArtifact(rootIdentity, "Owned",
                providerIdentity, "Owned", core);
        var providerArtifact = TrRmForeignInvocationAddressEmitterTests
            .CreateMixedImportProviderArtifact(providerIdentity, "Owned",
                "GetCurrentProcessId", "Leaf", "GetCurrentProcessId",
                leafIdentity, "Leaf", core);
        var leafCompilation
            = TrRmForeignInvocationAddressEmitterTests
                .CreateMultipleImportProviderCompilation(
                    "Leaf", "GetCurrentProcessId", core);
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        var incompatibleLeafCompilation
            = TrRmForeignInvocationAddressEmitterTests
                .CreateMultipleImportProviderCompilation(
                    "Leaf", "GetCurrentThreadId", core);
        var incompatibleLeafArtifact = TrRmExecutableCompiler.EmitStandard(
            incompatibleLeafCompilation.Program!, leafIdentity,
            schemaIdentity, 1);
        WriteStandardFArtifactEvidence("root", rootIdentity, rootArtifact);
        WriteStandardFArtifactEvidence("provider", providerIdentity,
            providerArtifact);
        WriteStandardFArtifactEvidence("leaf", leafIdentity, leafArtifact);
        WriteStandardFArtifactEvidence("incompatible-leaf", leafIdentity,
            incompatibleLeafArtifact);

        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-f-provider-closure-{Guid.NewGuid():N}");
        var rootPath = prefix + "-root.trrm";
        var providerPath = prefix + "-provider.trrm";
        var leafPath = prefix + "-leaf.trrm";
        var incompatibleLeafPath = prefix + "-incompatible-leaf.trrm";
        var imagePath = prefix + ".exe";
        var retainedOutputPath = prefix + "-retained.exe";
        byte[] retainedOutput = [0xA5, 0x5A, 0xC3, 0x3C];
        try
        {
            await File.WriteAllBytesAsync(rootPath, rootArtifact)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(providerPath, providerArtifact)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(leafPath, leafArtifact)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(incompatibleLeafPath,
                incompatibleLeafArtifact).ConfigureAwait(true);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm",
                rootPath, "--runtime", "fail-fast", "--trrm-schema",
                schemaIdentity, "--trrm-module", rootIdentity,
                "--trrm-provider", schemaIdentity, providerPath,
                "--trrm-provider", schemaIdentity, leafPath, "--json")
                .ConfigureAwait(true);
            TestContext.WriteLine($"Root->Owned->Leaf CLI JIT exit: {ran}");
            Assert.That(ran, Is.GreaterThan(0),
                $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm", rootPath, "-o", imagePath, "--runtime",
                "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", rootIdentity, "--trrm-provider",
                schemaIdentity, providerPath, "--trrm-provider",
                schemaIdentity, leafPath, "--json").ConfigureAwait(true);
            Assert.That(built, Is.Zero,
                $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            var imageResult = await ExecuteTrRmImageAsync(imagePath)
                .ConfigureAwait(true);
            TestContext.WriteLine(
                $"Root->Owned->Leaf emitted EXE result: {imageResult}");
            Assert.That(imageResult, Is.GreaterThan(0));

            await File.WriteAllBytesAsync(retainedOutputPath, retainedOutput)
                .ConfigureAwait(true);
            var (missingLeaf, missingOutput, missingError) = await RunAsync(
                [], "build-trrm", rootPath, "-o", retainedOutputPath,
                "--runtime", "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", rootIdentity, "--trrm-provider",
                schemaIdentity, providerPath, "--json").ConfigureAwait(true);
            TestContext.WriteLine(
                $"Missing deep provider build diagnostic exit: {missingLeaf}");
            Assert.That(missingLeaf, Is.EqualTo(1),
                $"{missingOutput}{Environment.NewLine}{missingError}");
            Assert.That(missingError, Is.Empty);
            using (var missingDocument = JsonDocument.Parse(missingOutput))
            {
                Assert.That(missingDocument.RootElement
                        .GetProperty("diagnostics")[0]
                        .GetProperty("code").GetString(),
                    Is.EqualTo("TRM5001"));
            }

            Assert.That(await File.ReadAllBytesAsync(retainedOutputPath)
                .ConfigureAwait(true), Is.EqualTo(retainedOutput));
            var (rejected, rejectionOutput, rejectionError) = await RunAsync(
                [], "build-trrm", rootPath, "-o", retainedOutputPath,
                "--runtime", "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", rootIdentity, "--trrm-provider",
                schemaIdentity, providerPath, "--trrm-provider",
                schemaIdentity, incompatibleLeafPath, "--json")
                .ConfigureAwait(true);
            TestContext.WriteLine(
                $"Incompatible deep provider build diagnostic exit: {rejected}");
            Assert.That(rejected, Is.EqualTo(1),
                $"{rejectionOutput}{Environment.NewLine}{rejectionError}");
            Assert.That(rejectionError, Is.Empty);
            using var rejectionDocument = JsonDocument.Parse(rejectionOutput);
            Assert.That(rejectionDocument.RootElement
                    .GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(),
                Is.EqualTo("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(retainedOutputPath)
                .ConfigureAwait(true), Is.EqualTo(retainedOutput));
        }
        finally
        {
            File.Delete(rootPath);
            File.Delete(providerPath);
            File.Delete(leafPath);
            File.Delete(incompatibleLeafPath);
            File.Delete(retainedOutputPath);
            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    private static void WriteStandardFArtifactEvidence(string role,
        string moduleIdentity, byte[] artifact)
    {
        TestContext.WriteLine(
            $"Standard F input {role}: module={moduleIdentity}; bytes={artifact.Length}; sha256={Convert.ToHexString(SHA256.HashData(artifact))}");
    }

    [Test]
    public async Task SelectedFProviderDiamondRunsBuildsAndExportsAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        const string leafIdentity = "Leaf";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var (rootArtifact, _, _, _) =
            TrRmForeignInvocationAddressEmitterTests
                .CreateMultipleRootMethodImportArtifacts(
                    targetMethodName: "Second");
        var firstProviderArtifact
            = TrRmForeignInvocationAddressEmitterTests
                .CreateMixedImportProviderArtifact("First", "First",
                    "GetCurrentProcessId", "Leaf", "GetTickCount",
                    leafIdentity, "Leaf", core);
        var secondProviderArtifact
            = TrRmForeignInvocationAddressEmitterTests
                .CreateMixedImportProviderArtifact("Second", "Second",
                    "GetCurrentThreadId", "Leaf", "GetTickCount",
                    leafIdentity, "Leaf", core);
        var leafCompilation
            = TrRmForeignInvocationAddressEmitterTests
                .CreateMultipleImportProviderCompilation(
                    "Leaf", "GetTickCount", core);
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        WriteStandardFArtifactEvidence("diamond-root", rootIdentity,
            rootArtifact);
        WriteStandardFArtifactEvidence("diamond-first", "First",
            firstProviderArtifact);
        WriteStandardFArtifactEvidence("diamond-second", "Second",
            secondProviderArtifact);
        WriteStandardFArtifactEvidence("diamond-leaf", leafIdentity,
            leafArtifact);

        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-f-provider-diamond-{Guid.NewGuid():N}");
        var rootPath = prefix + "-root.trrm";
        var firstPath = prefix + "-first.trrm";
        var secondPath = prefix + "-second.trrm";
        var leafPath = prefix + "-leaf.trrm";
        var imagePath = prefix + ".exe";
        var libraryPath = prefix + ".dll";
        try
        {
            await File.WriteAllBytesAsync(rootPath, rootArtifact)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(firstPath, firstProviderArtifact)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(secondPath, secondProviderArtifact)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(leafPath, leafArtifact)
                .ConfigureAwait(true);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm",
                rootPath, "--runtime", "fail-fast", "--trrm-schema",
                schemaIdentity, "--trrm-module", rootIdentity,
                "--trrm-provider", schemaIdentity, firstPath,
                "--trrm-provider", schemaIdentity, secondPath,
                "--trrm-provider", schemaIdentity, leafPath, "--json")
                .ConfigureAwait(true);
            TestContext.WriteLine($"Diamond CLI JIT exit: {ran}");
            Assert.That(ran, Is.GreaterThan(0),
                $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm", rootPath, "-o", imagePath, "--runtime",
                "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", rootIdentity, "--trrm-provider",
                schemaIdentity, firstPath, "--trrm-provider", schemaIdentity,
                secondPath, "--trrm-provider", schemaIdentity, leafPath,
                "--json").ConfigureAwait(true);
            Assert.That(built, Is.Zero,
                $"{buildOutput}{Environment.NewLine}{buildError}");
            var imageResult = await ExecuteTrRmImageAsync(imagePath)
                .ConfigureAwait(true);
            TestContext.WriteLine($"Diamond emitted EXE result: {imageResult}");
            Assert.That(imageResult, Is.GreaterThan(0));

            var secondInput = new TrRmStandardArtifactInput(schemaIdentity,
                secondProviderArtifact);
            var leafInput = new TrRmStandardArtifactInput(schemaIdentity,
                leafArtifact);
            var library = NativeModuleCompiler.EmitStandardLibrary(
                secondInput, "Second", [leafInput], ["Second"],
                NativeRuntimePolicy.FailFast);
            await File.WriteAllBytesAsync(libraryPath, library)
                .ConfigureAwait(true);
            var handle = NativeLibrary.Load(libraryPath);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<
                    StandardFNativeFunction>(NativeLibrary.GetExport(handle,
                    "Second"));
                var result = invoke();
                TestContext.WriteLine(
                    $"Diamond no-entry DLL export result: {result}");
                Assert.That(result, Is.GreaterThan(0));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }

            _ = Assert.Throws<ArgumentException>(() =>
                NativeModuleCompiler.EmitStandardLibrary(secondInput,
                    "Second", [leafInput], ["Leaf"],
                    NativeRuntimePolicy.FailFast));
        }
        finally
        {
            File.Delete(rootPath);
            File.Delete(firstPath);
            File.Delete(secondPath);
            File.Delete(leafPath);
            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task SelectedFProviderCycleRunsBuildsAndExportsAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "CycleRoot";
        const string firstIdentity = "CycleP";
        const string secondIdentity = "CycleQ";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var rootArtifact = TrRmForeignInvocationAddressEmitterTests
            .CreateRootMethodImportArtifact(rootIdentity, firstIdentity,
                firstIdentity, firstIdentity, core);
        var firstProviderArtifact = TrRmForeignInvocationAddressEmitterTests
            .CreateMixedImportProviderArtifact(firstIdentity, firstIdentity,
                "GetCurrentProcessId", secondIdentity,
                "GetCurrentProcessId", secondIdentity, secondIdentity, core);
        var secondProviderArtifact = TrRmForeignInvocationAddressEmitterTests
            .CreateMixedImportProviderArtifact(secondIdentity, secondIdentity,
                "GetCurrentProcessId", firstIdentity,
                "GetCurrentProcessId", firstIdentity, firstIdentity, core);
        WriteStandardFArtifactEvidence("cycle-root", rootIdentity,
            rootArtifact);
        WriteStandardFArtifactEvidence("cycle-first", firstIdentity,
            firstProviderArtifact);
        WriteStandardFArtifactEvidence("cycle-second", secondIdentity,
            secondProviderArtifact);

        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-f-provider-cycle-{Guid.NewGuid():N}");
        var rootPath = prefix + "-root.trrm";
        var firstPath = prefix + "-first.trrm";
        var secondPath = prefix + "-second.trrm";
        var imagePath = prefix + ".exe";
        var libraryPath = prefix + ".dll";
        try
        {
            await File.WriteAllBytesAsync(rootPath, rootArtifact)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(firstPath, firstProviderArtifact)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(secondPath, secondProviderArtifact)
                .ConfigureAwait(true);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm",
                rootPath, "--runtime", "fail-fast", "--trrm-schema",
                schemaIdentity, "--trrm-module", rootIdentity,
                "--trrm-provider", schemaIdentity, firstPath,
                "--trrm-provider", schemaIdentity, secondPath, "--json")
                .ConfigureAwait(true);
            TestContext.WriteLine($"Cycle CLI JIT exit: {ran}");
            Assert.That(ran, Is.GreaterThan(0),
                $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm", rootPath, "-o", imagePath, "--runtime",
                "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", rootIdentity, "--trrm-provider",
                schemaIdentity, firstPath, "--trrm-provider", schemaIdentity,
                secondPath, "--json").ConfigureAwait(true);
            Assert.That(built, Is.Zero,
                $"{buildOutput}{Environment.NewLine}{buildError}");
            var imageResult = await ExecuteTrRmImageAsync(imagePath)
                .ConfigureAwait(true);
            TestContext.WriteLine($"Cycle emitted EXE result: {imageResult}");
            Assert.That(imageResult, Is.GreaterThan(0));

            var firstInput = new TrRmStandardArtifactInput(schemaIdentity,
                firstProviderArtifact);
            var secondInput = new TrRmStandardArtifactInput(schemaIdentity,
                secondProviderArtifact);
            var library = NativeModuleCompiler.EmitStandardLibrary(
                firstInput, firstIdentity, [secondInput], [firstIdentity],
                NativeRuntimePolicy.FailFast);
            await File.WriteAllBytesAsync(libraryPath, library)
                .ConfigureAwait(true);
            var handle = NativeLibrary.Load(libraryPath);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<
                    StandardFNativeFunction>(NativeLibrary.GetExport(handle,
                    firstIdentity));
                var result = invoke();
                TestContext.WriteLine(
                    $"Mutual-cycle no-entry DLL export result: {result}");
                Assert.That(result, Is.GreaterThan(0));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            File.Delete(rootPath);
            File.Delete(firstPath);
            File.Delete(secondPath);
            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task SelectedFCoLocatedProviderRunsAndBuildsAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string providerIdentity = "cli-co-located-native-provider";
        const string consumerIdentity = "cli-co-located-native-consumer";
        var providerCompilation
            = TrRmForeignInvocationAddressEmitterTests
                .CreateCurrentProcessIdCompilation();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, providerArtifact),
            providerIdentity, []);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    var callback = &GetCurrentProcessId;
                    return callback();
                }
            }
            """), providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program!, consumerIdentity, schemaIdentity, 1);
        var artifactBytes
            = TrRmForeignInvocationAddressEmitterTests
                .CombinePopulatedNativeAddressModules(
                    consumerArtifact, providerArtifact);

        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-f-colocated-provider-{Guid.NewGuid():N}");
        var artifactPath = prefix + ".trrm";
        var imagePath = prefix + ".exe";
        try
        {
            await File.WriteAllBytesAsync(artifactPath, artifactBytes)
                .ConfigureAwait(true);
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm",
                artifactPath, "--runtime", "fail-fast", "--trrm-schema",
                schemaIdentity, "--trrm-module", consumerIdentity, "--json")
                .ConfigureAwait(true);
            Assert.That(ran, Is.GreaterThan(0),
                $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm", artifactPath, "-o", imagePath, "--runtime",
                "fail-fast", "--trrm-schema", schemaIdentity,
                "--trrm-module", consumerIdentity, "--json")
                .ConfigureAwait(true);
            Assert.That(built, Is.Zero,
                $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(imagePath)
                .ConfigureAwait(true), Is.GreaterThan(0));
        }
        finally
        {
            File.Delete(artifactPath);
            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedInitOutputCallsRunAndBuildWithoutProducerSourceAsync()
    {
        const string source = """
            void Fill(int32 value, init ref<int32> result) {
                result = value;
            }
            int32 Observe(bool value) { return value ? 42 : 0; }
            int32 ObserveOne(bool value) { return value ? 1 : 0; }
            bool Enabled() { return true; }
            float64 Scale() { return float64(2.5); }
            int32 One32() { return 1; }
            int64 FortyTwo64() { return int64(42); }
            int64 NegativeFive() { return int64(-5); }
            int64 Two64() { return int64(2); }
            int64 MinusTwo64() { return int64(-2); }
            int64 MinusOne64() { return int64(-1); }
            int64 Zero64() { return int64(0); }
            int64 Minimum64() { return int64.MinValue; }
            int32 Main() {
                init int32 result;
                Fill(Observe(Enabled() == true) + Observe(Scale() < float64(3.5))
                    + Observe(Scale() * (float64(3.5) - Scale()) == Scale())
                    + Observe(NegativeFive() / Two64() == MinusTwo64())
                    + Observe(NegativeFive() % Two64() == MinusOne64())
                    + Observe(Minimum64() % MinusOne64() == Zero64())
                    + ObserveOne(int32(FortyTwo64()) == 42)
                    + ObserveOne(float32(One32()) == float32(1.0)), init result);
                return result;
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-init-output-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(254));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(254));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedInitOutputSupportsMixedScalarReferentsAsync()
    {
        const string source = """
            uint32 Fill(bool enabled, init ref<bool> enabledResult,
                char16 character, init ref<char16> characterResult,
                uint64 count, init ref<uint64> countResult,
                float64 amount, init ref<float64> amountResult) {
                enabledResult = enabled;
                characterResult = character;
                countResult = count;
                amountResult = amount;
                return uint32.MaxValue;
            }
            int32 Check(bool enabled, char16 character, uint64 count, float64 amount,
                uint32 returned, int32 conditionalFalse, int32 conditionalTrue) {
                return enabled && character == char16(65) && count == uint64.MaxValue
                    && amount == float64(2.5) && returned == uint32.MaxValue
                    && conditionalFalse == 0 && conditionalTrue == 42 ? 42 : 0;
            }
            int32 CheckConditional(bool flag, char16 marker, int32 result,
                uint32 smallCount, int64 signedCount, uint64 wideCount,
                float32 ratio, float64 amount) {
                return !flag && marker == char16(65) && result == 0
                    && smallCount == uint32.MaxValue && signedCount == int64.MinValue
                    && wideCount == 0 && ratio == float32(1.5)
                    && amount == 0.0 ? 42 : 1;
            }
            bool TryFill(bool success, init ref<bool> flag,
                init ref<char16> marker, init ref<int32> result,
                init ref<uint32> smallCount, init ref<int64> signedCount,
                init ref<uint64> wideCount, init ref<float32> ratio,
                init ref<float64> conditionalAmount) {
                flag = false;
                marker = char16(65);
                result = 0;
                smallCount = uint32.MaxValue;
                signedCount = int64.MinValue;
                wideCount = 0;
                ratio = float32(1.5);
                conditionalAmount = 0.0;
                return success;
            }
            int32 RunConditional(bool success) {
                init bool flag;
                init char16 marker;
                init int32 result;
                init uint32 smallCount;
                init int64 signedCount;
                init uint64 wideCount;
                init float32 ratio;
                init float64 amount;
                if (TryFill(success, init flag, init marker, init result,
                    init smallCount, init signedCount, init wideCount, init ratio,
                    init amount)) {
                    return CheckConditional(flag, marker, result, smallCount,
                        signedCount, wideCount, ratio, amount);
                } else {
                    return 0;
                }
            }
            int32 Main() {
                init bool enabled;
                init char16 character;
                init uint64 count;
                init float64 amount;
                uint32 returned = Fill(true, init enabled, char16(65), init character,
                    uint64.MaxValue, init count, float64(2.5), init amount);
                int32 conditionalFalse = RunConditional(false);
                int32 conditionalTrue = RunConditional(true);
                return Check(enabled, character, count, amount, returned,
                    conditionalFalse, conditionalTrue);
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-init-output-scalars-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output-scalars", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled, Is.EqualTo(0));
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(42));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built, Is.EqualTo(0));
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(42));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }
    [TestCase(true)]
    [TestCase(false)]
    public async Task PersistedInitOutputConditionalFailurePreservesFailFastStatusAsync(
        bool failOnTrue)
    {
        var condition = failOnTrue ? "true" : "false";
        var trueArm = failOnTrue ? "require false;" : "return 42;";
        var falseArm = failOnTrue ? "return 42;" : "require false;";
        var source = $$"""
            bool Select(init ref<int32> result) {
                result = 7;
                return {{condition}};
            }
            int32 Main() {
                init int32 result;
                if (Select(init result)) {
                    {{trueArm}}
                } else {
                    {{falseArm}}
                }
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-init-output-conditional-failure-{failOnTrue}-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output-conditional-failure", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (jitted, jitOutput, jitError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "jit", "-", "--runtime", "fail-fast", "--json");
            Assert.That(jitted, Is.EqualTo(unchecked((int)0xE0540003)));
            Assert.That(jitOutput, Is.Empty);
            Assert.That(jitError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(unchecked((int)0xE0540003)));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(unchecked((int)0xE0540003)));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }
    [TestCase(true,true)]
    [TestCase(false,false)]
    public async Task PersistedInitOutputConditionalValueCallPreservesFailFastStatusAsync(
        bool zeroArgumentCall, bool failOnTrue)
    {
        var selected = failOnTrue ? "true" : "false";
        var signature = zeroArgumentCall ? "()" : "(bool selected)";
        var returnExpression = zeroArgumentCall ? selected : "selected";
        var invocation = zeroArgumentCall ? "Select()" : $"Select({selected})";
        var trueArm = failOnTrue ? "require false;" : "return initialized;";
        var falseArm = failOnTrue ? "return initialized;" : "require false;";
        const int expected = unchecked((int)0xE0540003);
        var source = $$"""
            bool Select{{signature}} { return {{returnExpression}}; }
            int32 Main() {
                init int32 initialized;
                init int32 vacant;
                initialized = 42;
                if ({{invocation}}) {
                    {{trueArm}}
                } else {
                    {{falseArm}}
                }
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-init-output-value-condition-{zeroArgumentCall}-{failOnTrue}-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output-value-condition", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (jitted, jitOutput, jitError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "jit", "-", "--runtime", "fail-fast", "--json");
            Assert.That(jitted, Is.EqualTo(expected));
            Assert.That(jitOutput, Is.Empty);
            Assert.That(jitError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(expected));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(expected));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedInitOutputSignedNegationPreservesOverflowStatusAsync()
    {
        const string source = """
            void Fill(int32 value, init ref<int32> result) { result = value; }
            int32 Main() {
                init int32 result;
                Fill(-int32.MinValue, init result);
                return result;
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-init-negate-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output-negate", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(unchecked((int)0xE0540001)));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Test]
    public async Task PersistedInitOutputIntegerConversionPreservesOverflowStatusAsync()
    {
        const string source = """
            void Fill(int32 value, init ref<int32> result) { result = value; }
            uint64 Maximum() { return uint64.MaxValue; }
            int32 Main() {
                init int32 result;
                Fill(int32(Maximum()), init result);
                return result;
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-init-convert-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output-convert", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(unchecked((int)0xE0540001)));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Test]
    public async Task PersistedInitOutputFloatToIntegerConversionPreservesOverflowStatusAsync()
    {
        const string source = """
            void Fill(int32 value, init ref<int32> result) { result = value; }
            float64 TooLarge() { return float64(4294967296.0); }
            int32 Main() {
                init int32 result;
                Fill(int32(TooLarge()), init result);
                return result;
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-init-float-convert-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output-float-convert", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(unchecked((int)0xE0540001)));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Test]
    public async Task PersistedNestedAcyclicBreaksRunAndBuildWithoutProducerSourceAsync()
    {
        const string source = """
            int32 Main() {
                mut int32 result = 0;
                do {
                    do {
                        result = 40;
                        break;
                    } while (Fail());
                    result += 2;
                    break;
                } while (Fail());
                return result;
            }
            bool Fail() { require false; return false; }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-nested-acyclic-breaks-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity", "nested-acyclic-breaks",
                "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 42, Is.True, $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(42));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedUnsafeCallBeforeAcyclicReturnKeepsFailureStatusAsync()
    {
        const string source = """
            unsafe void Danger() { require false; }
            int32 Main() {
                unsafe {
                    do {
                        Danger();
                        return 42;
                    } while (false);
                }
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-unsafe-acyclic-prefix-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity", "unsafe-acyclic-prefix",
                "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(unchecked((int)0xE0540003)));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(unchecked((int)0xE0540003)));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedDirectCallsAgreeAcrossJitAndExecutableAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-calls-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            const string source = """
                int32 Main() { return Recursive(5); }
                int32 Recursive(int32 n) {
                    if (n == 0) { return 1; }
                    else { return n * Recursive(n - 1); }
                }
                """;
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "recursive-calls", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 120, Is.True, $"{runOutput}{Environment.NewLine}{runError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(120));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedNoThrowAndDoesNotReturnEffectsAreAuditedAsync()
    {
        const string source = """
             int32 Identity(int32 value) { return value; }
            [DoesNotReturn] void Never() { while (true) {} }
            [DoesNotReturn] void Stop() { Never(); }
            int32 Main() { return Identity(42); }
            """;
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-effects-{Guid.NewGuid():N}.trrm");
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "effects", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 42, Is.True, $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Test]
    public async Task PersistedNonReturningCallKeepsRequirementFailureStatusAsync()
    {
        const string source = "[DoesNotReturn] void Stop() { require false; } int32 Main() { Stop(); }";
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-no-return-{Guid.NewGuid():N}.trrm");
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "no-return", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(unchecked((int)0xE0540003)));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Test]
    public async Task IndexedFailureReasonsMapToTheExistingNativeStatusesAsync()
    {
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-failure-reasons-{Guid.NewGuid():N}.trrm");
        try
        {
            var (compiled, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { require false; return 0; }"),
                "compile-trrm", "-", "--identity", "failure-reasons", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            var artifactBytes = await File.ReadAllBytesAsync(artifact);
            var reasonOffset = IndexedFailureReasonOffset(artifactBytes);
            foreach (var (reason, expected) in new[]
            {
                (1, unchecked((int)0xE0540003)),
                (2, unchecked((int)0xE0540001)),
                (3, unchecked((int)0xE0540002)),
                (4, unchecked((int)0xE0540001)),
            })
            {
                artifactBytes[reasonOffset] = (byte)reason;
                await File.WriteAllBytesAsync(artifact, artifactBytes);
                var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                    "--runtime", "fail-fast", "--json");
                Assert.That(ran, Is.EqualTo(expected));
                Assert.That(runOutput, Is.Empty);
                Assert.That(runError, Is.Empty);
            }
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    private static int IndexedFailureReasonOffset(byte[] artifact)
    {
        var sectionCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(12));
        for (var section = 0; section < sectionCount; section++)
        {
            var entry = artifact.AsSpan(32 + section * 16);
            if (BinaryPrimitives.ReadUInt16LittleEndian(entry) != 0x20)
            {
                continue;
            }
            var rowCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var rowStart = offset + (rowCount + 1) * sizeof(uint);
            var rowLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(offset + rowCount * sizeof(uint)));
            var rowEnd = rowStart + rowLength;
            var marker = Array.LastIndexOf(artifact, (byte)0x87, rowEnd - 1, rowEnd - rowStart);
            if (marker < rowStart || artifact[marker + 1] != 1)
            {
                throw new InvalidOperationException("Indexed failure terminator was not found.");
            }
            return marker + 1;
        }
        throw new InvalidOperationException("TRRM Body section was not found.");
    }

    [Test]
    public async Task PersistedLibraryExportCanCallPrivateSixArgumentHelperAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-calls-library-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var library = prefix + ".dll";
        try
        {
            const string source = """
                public int32 Add(int32 first, int32 second) {
                    return Helper(first, second, 3, 4, 5, 6);
                }
                int32 Helper(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) {
                    return a + b + c + d + e + f;
                }
                """;
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "export-calls", "-o", artifact,
                "--runtime", "fail-fast", "--library", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm-library", artifact, "--export", "Add", "-o", library,
                "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            var handle = NativeLibrary.Load(library);
            try
            {
                var add = Marshal.GetDelegateForFunctionPointer<TrRmAdd>(
                    NativeLibrary.GetExport(handle, "Add"));
                Assert.That(add(10, 13), Is.EqualTo(41));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(library);
        }
    }
    [Test]
    public async Task PersistedTrRmLibraryPublishesTwoIndependentNativeExportsAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-library-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var library = prefix + ".dll";
        try
        {
            const string source = """
                public int32 Add(int32 left, int32 right) { return left + right; }
                public int64 Identity(int64 value) { return value; }
                """;
            var (compiled, stdout, stderr) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "two-exports", "-o", artifact,
                "--runtime", "fail-fast", "--library", "--json");
            Assert.That(compiled == 0, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.That(stderr, Is.Empty);
            var (built, output, error) = await RunAsync([], "build-trrm-library", artifact,
                "--export", "Add", "--export", "Identity", "-o", library,
                "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            var handle = NativeLibrary.Load(library);
            try
            {
                var add = Marshal.GetDelegateForFunctionPointer<TrRmAdd>(
                    NativeLibrary.GetExport(handle, "Add"));
                var identity = Marshal.GetDelegateForFunctionPointer<TrRmIdentity>(
                    NativeLibrary.GetExport(handle, "Identity"));
                Assert.That(add(20, 22), Is.EqualTo(42));
                Assert.That(identity(long.MinValue), Is.EqualTo(long.MinValue));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(library);
        }
    }
    [TestCase("int32 Main() { return 2147483647 + 1; }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { return 1 / 0; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { return int32(uint32.MaxValue); }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { _ = int32(1) / 0; return 42; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { int32 x = 1 / 0; return 42; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { mut int32 x = int32.MaxValue; do { x += 1; break; } while (false); return x; }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { mut int32 value = 42; mut int32 divisor = 0; do { value /= divisor; break; } while (false); return value; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { do { int32 value = Fail(); break; } while (false); return 42; } int32 Fail() { require false; return 0; }",unchecked((int)0xE0540003))]
    [TestCase("int32 Main() { do { require false; return 42; } while (false); }",unchecked((int)0xE0540003))]
    [TestCase("int32 Main() { do { Fail(); break; } while (false); return 42; } void Fail() { require false; }",unchecked((int)0xE0540003))]
    [TestCase("int32 Main() { do { Fail(); } while (false); return 42; } void Fail() { require false; }",unchecked((int)0xE0540003))]
    [TestCase("int32 Main() { for (mut int32 i = 0; true; i = 1 / 0) { i = int32.MaxValue + 1; break; } return 42; }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { return (1 / 0) + (int32.MaxValue + 1); }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { return (int32.MaxValue + 1) + (1 / 0); }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { Fail(); return 42; } void Fail() { int32 x = 1 / 0; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { _ = Fail(); return 42; } int32 Fail() { return 1 / 0; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { return F(1 / 0, int32.MaxValue + 1); } int32 F(int32 a, int32 b) { return 42; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { return F(int32.MaxValue + 1, 1 / 0); } int32 F(int32 a, int32 b) { return 42; }",unchecked((int)0xE0540001))]
    public async Task TrRmCheckedFailureKeepsFailFastPolicyAfterPersistenceAsync(string source, int failure)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-failure-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, stdout, stderr) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "failure", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.That(stderr, Is.Empty);
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == failure, Is.True, $"Expected {failure:X8}, received {ran:X8}: {runOutput}{Environment.NewLine}{runError}");
            var (built, output, error) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(failure));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    private static async Task<int> ExecuteTrRmImageAsync(string executable)
    {
        using var process = Process.Start(new ProcessStartInfo(executable) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Executable was not started.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(true);
        }
        catch (TimeoutException exception)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(true);
            }

            var output = await stdout.ConfigureAwait(true);
            var error = await stderr.ConfigureAwait(true);
            throw new TimeoutException(
                $"TRRM executable exceeded its 10-second deadline. {output}{Environment.NewLine}{error}",
                exception);
        }

        Assert.That(await stdout, Is.Empty);
        Assert.That(await stderr, Is.Empty);
        return process.ExitCode;
    }
    [TestCase("compile-trrm")]
    [TestCase("run-trrm")]
    [TestCase("build-trrm")]
    [TestCase("build-trrm-library")]
    public async Task TrRmRejectsCheckedExceptionsAtEveryExecutionBoundaryAsync(string command)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-policy-{Guid.NewGuid():N}.trrm");
        var arguments = command switch {
            "compile-trrm" => new[] { command, "-", "--identity", "policy", "-o", path, "--runtime", "checked-exceptions", "--json" },
            "run-trrm" => [command, path, "--runtime", "checked-exceptions", "--json"],
            "build-trrm" => [command, path, "-o", Path.ChangeExtension(path, ".exe"), "--runtime", "checked-exceptions", "--json"],
            _ => [command, path, "--export", "Value", "-o", Path.ChangeExtension(path, ".dll"), "--runtime", "checked-exceptions", "--json"],
        };
        var (exit, stdout, stderr) = await RunAsync(
            Encoding.UTF8.GetBytes("int32 Main() { return 42; }"), arguments);
        Assert.That(exit == 1, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
        Assert.That(stderr, Is.Empty);
        using var result = JsonDocument.Parse(stdout);
        Assert.That(result.RootElement.GetProperty("diagnostics")[0]
            .GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
        Assert.That(result.RootElement.GetProperty("diagnostics")[0]
            .GetProperty("message").GetString(), Does.Contain("fail-fast"));
    }
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { int32 inner = x; init int32 vacant; x = inner + 1; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; if (x == 0) { int32 inner = x; init int32 vacant; x = inner + 1; } return x; }")]
    public async Task TrRmSourceRejectionDoesNotOverwritePreviousArtifactAsync(string source)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-reject-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(path, [0xAA, 0x55]);
            var (exit, stdout, stderr) = await RunAsync(
                Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "reject", "-o", path,
                "--runtime", "fail-fast", "--json");
            Assert.That(exit == 1, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.That(stderr, Is.Empty);
            Assert.That(stdout, Does.Contain("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(new byte[] { 0xAA, 0x55 }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task TrRmIndependentBytesExecuteInFreshProcessAsync()
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-independent-{Guid.NewGuid():N}.trrm");
        try
        {
            byte[] body = [4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1];
            await File.WriteAllBytesAsync(path, TrRmExecutionAuditTests.Artifact(body));
            var (exit, stdout, stderr) = await RunAsync([], "run-trrm", path, "--runtime", "fail-fast", "--json");
            Assert.That(exit == 42, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.That(stdout, Is.Empty);
            Assert.That(stderr, Is.Empty);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task TrRmInvalidEffectsPreserveNativeOutputAndRejectExecutionAsync(bool library)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-invalid-effects-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var output = prefix + (library ? ".dll" : ".exe");
        try
        {
            byte[] body = [4, 1, 1, 0, 0, 0, 3,
                1, 1, 1, 20, 1, 2, 1, 22, 0x15, 3, 1, 1, 2, 0x83, 3];
            await File.WriteAllBytesAsync(artifact,
                TrRmExecutionAuditTests.Artifact(body, entry: !library, effect: 1, exported: library));
            await File.WriteAllBytesAsync(output, [0xAA, 0x55]);
            var arguments = library
                ? new[] { "build-trrm-library", artifact, "--export", "Main", "-o", output, "--runtime", "fail-fast", "--json" }
                : ["build-trrm", artifact, "-o", output, "--runtime", "fail-fast", "--json"];
            var (exit, stdout, stderr) = await RunAsync([], arguments);
            Assert.That(exit == 1, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.That(stderr, Is.Empty);
            Assert.That(stdout, Does.Contain("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(new byte[] { 0xAA, 0x55 }));
            var (run, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(run == 1, Is.True, $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Does.Contain("TRM5001"));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(output);
        }
    }

    [Test]
    public async Task TrRmMissingInputHasInputErrorStatusAsync()
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-absent-{Guid.NewGuid():N}.trrm");
        var (exit, stdout, stderr) = await RunAsync([], "run-trrm", path, "--runtime", "fail-fast", "--json");
        Assert.That(exit == 2, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
        Assert.That(stderr, Is.Empty);
        Assert.That(stdout, Does.Contain("TRM2001"));
    }

    [Test]
    public async Task TrRmInvalidUtf8PreservesOutputAndReportsInputFailureAsync()
    {
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-invalid-utf8-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(output, [0xAA, 0x55]);
            var (exit, stdout, stderr) = await RunAsync([0xC3, 0x28],
                "compile-trrm", "-", "--identity", "invalid-utf8", "-o", output,
                "--runtime", "fail-fast", "--json");
            Assert.That(exit == 2, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.That(stderr, Is.Empty);
            Assert.That(stdout, Does.Contain("TRM2001"));
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(new byte[] { 0xAA, 0x55 }));
        }
        finally
        {
            File.Delete(output);
        }
    }
}
