// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    private const string OverflowingMain = """
         int32 Main() {
            mut int32 value = 2147483647;
            value++;
            if (value < 0) { return 42; }
            return 0;
        }
        """;

    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task HostedAndFrontendCommandsUseTheSelectedArithmeticProfileAsync(string runtimePolicy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var input = Encoding.UTF8.GetBytes(OverflowingMain);
        var (developmentExit, developmentOutput, developmentError) = await RunAsync(input,
            "analyze", "-", "--runtime", runtimePolicy, "--json");
        Assert.That(developmentExit, Is.Zero);
        Assert.That(developmentError, Is.Empty);
        using (var development = JsonDocument.Parse(developmentOutput))
        {
            Assert.That(development.RootElement.GetProperty("success").GetBoolean(), Is.True);
        }

        var (developmentJitExit, developmentJitOutput, developmentJitError) = await RunAsync(input,
            "jit", "-", "--runtime", runtimePolicy, "--json");
        Assert.That(developmentJitExit, Is.EqualTo(unchecked((int)0xE0540001)));
        Assert.That(developmentJitOutput, Is.Empty);
        Assert.That(developmentJitError, Is.Empty);

        var (analysisExit, analysisOutput, analysisError) = await RunAsync(input,
            "analyze", "-", "--arithmetic-profile", "release", "--runtime", runtimePolicy, "--json");
        Assert.That(analysisExit, Is.Zero);
        Assert.That(analysisError, Is.Empty);
        using (var analysis = JsonDocument.Parse(analysisOutput))
        {
            Assert.That(analysis.RootElement.GetProperty("success").GetBoolean(), Is.True);
        }

        var (jitExit, jitOutput, jitError) = await RunAsync(input,
            "jit", "-", "--runtime", runtimePolicy, "--arithmetic-profile", "release", "--json");
        Assert.That(jitExit, Is.EqualTo(42));
        Assert.That(jitOutput, Is.Empty);
        Assert.That(jitError, Is.Empty);

        var frontend = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        var (componentExit, componentOutput, componentError) = await RunAsync(input,
            "frontend-component", frontend, "-", "--arithmetic-profile", "release",
            "--runtime", runtimePolicy, "--json");
        Assert.That(componentExit, Is.Zero, componentOutput + componentError);
        Assert.That(componentError, Is.Empty);
        using (var component = JsonDocument.Parse(componentOutput))
        {
            Assert.That(component.RootElement.GetProperty("success").GetBoolean(), Is.True);
        }

        var (frontendExit, frontendOutput, frontendError) = await RunAsync(input,
            "frontend-run", frontend, "-", "--runtime", runtimePolicy,
            "--arithmetic-profile", "release", "--json");
        Assert.That(frontendExit, Is.EqualTo(42));
        Assert.That(frontendOutput, Is.Empty);
        Assert.That(frontendError, Is.Empty);
    }

    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task BuildCommandsUseReleaseArithmeticForSubjectSourceAsync(string runtimePolicy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"arithmetic-profile-build-{Guid.NewGuid():N}");
        var hosted = prefix + ".hosted.exe";
        var nativeImport = prefix + ".native-import.exe";
        var nativeLibrary = prefix + ".native-library.dll";
        var scanner = prefix + ".scanner.exe";
        var frontend = prefix + ".frontend.exe";
        var frontendLibrary = prefix + ".frontend-library.dll";
        var source = Encoding.UTF8.GetBytes(OverflowingMain);
        var nativeImportSource = Path.Combine(RepositoryRoot(), "samples", "Win32MessageBox",
            "message-box.terrum");
        var librarySource = Encoding.UTF8.GetBytes(
            " public int32 Value() { mut int32 value = 2147483647; value++; return value < 0 ? 42 : 0; }");
        try
        {
            var (hostedExit, hostedOutput, hostedError) = await RunAsync(source,
                "build", "-", "-o", hosted, "--arithmetic-profile", "release",
                "--runtime", runtimePolicy, "--json");
            Assert.That(hostedExit, Is.Zero, hostedOutput + hostedError);
            Assert.That(await ExecuteNativeImageAsync(hosted).ConfigureAwait(true), Is.EqualTo(42));

            var (nativeImportExit, nativeImportOutput, nativeImportError) = await RunAsync([],
                "build-native-import", nativeImportSource, "--runtime", runtimePolicy,
                "--arithmetic-profile", "release", "-o", nativeImport, "--json");
            Assert.That(nativeImportExit, Is.Zero, nativeImportOutput + nativeImportError);

            var (libraryExit, libraryOutput, libraryError) = await RunAsync(librarySource,
                "build-library", "-", "--export", "Value", "-o", nativeLibrary,
                "--runtime", runtimePolicy, "--arithmetic-profile", "release", "--json");
            Assert.That(libraryExit, Is.Zero, libraryOutput + libraryError);

            var sourceDirectory = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
            var (scannerExit, scannerOutput, scannerError) = await RunAsync(source,
                "build-component", sourceDirectory, "-", "-o", scanner,
                "--arithmetic-profile", "release", "--runtime", runtimePolicy, "--json");
            Assert.That(scannerExit, Is.Zero, scannerOutput + scannerError);
            Assert.That(await ExecuteNativeImageAsync(scanner).ConfigureAwait(true), Is.EqualTo(42));

            var (frontendExit, frontendOutput, frontendError) = await RunAsync(source,
                "frontend-build", sourceDirectory, "-", "--runtime", runtimePolicy,
                "-o", frontend, "--arithmetic-profile", "release", "--json");
            Assert.That(frontendExit, Is.Zero, frontendOutput + frontendError);
            Assert.That(await ExecuteNativeImageAsync(frontend).ConfigureAwait(true), Is.EqualTo(42));

            var (frontendLibraryExit, frontendLibraryOutput, frontendLibraryError) = await RunAsync(
                librarySource, "frontend-build-library", sourceDirectory, "-", "-o", frontendLibrary,
                "--arithmetic-profile", "release", "--runtime", runtimePolicy, "--json");
            Assert.That(frontendLibraryExit, Is.Zero, frontendLibraryOutput + frontendLibraryError);
        }
        finally
        {
            foreach (var path in new[] { nativeLibrary, frontendLibrary })
            {
                File.Delete(path);
            }
            await TestFileCleanup.DeleteWithRetryAsync(hosted).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(nativeImport).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(scanner).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(frontend).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task SourceArtifactCommandsAcceptArithmeticProfileSelectionAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"arithmetic-profile-artifacts-{Guid.NewGuid():N}");
        var trrm = prefix + ".trrm";
        var moduleProvider = prefix + ".provider.trm";
        var moduleConsumer = prefix + ".consumer.trm";
        var nativeProvider = prefix + ".provider.trmn";
        var nativeConsumer = prefix + ".consumer.trmn";
        var nativeHelper = prefix + ".helper.trmn";
        var nativeSet = prefix + ".tnset";
        var graphMiddle = prefix + ".middle.trng";
        var graphRoot = prefix + ".root.trng";
        var providerSourcePath = prefix + ".provider.terrum";
        var consumerSourcePath = prefix + ".consumer.terrum";
        var nativeConsumerSourcePath = prefix + ".native-consumer.terrum";
        var helperSourcePath = prefix + ".helper.terrum";
        var setSourcePath = prefix + ".set.terrum";
        var middleSourcePath = prefix + ".middle.terrum";
        var rootSourcePath = prefix + ".root.terrum";
        var providerSource = Encoding.UTF8.GetBytes("""
             public int32 Compute() {
                mut int32 value = 2147483647;
                value++;
                return value < 0 ? 42 : 0;
            }
            """);
        const string ConsumerSource = """
             int32 Main() {
                mut int32 value = 2147483647;
                value++;
                return value < 0 ? Compute() : 0;
            }
            """;
        const string GraphRootSource = """
             int32 Main() {
                mut int32 value = 2147483647;
                value++;
                return value < 0 ? Middle() : 0;
            }
            """;
        const string SimpleConsumer = "int32 Main() { return Compute(); }";
        try
        {
            await File.WriteAllBytesAsync(providerSourcePath, providerSource);
            await File.WriteAllTextAsync(consumerSourcePath, SimpleConsumer);
            await File.WriteAllTextAsync(nativeConsumerSourcePath, ConsumerSource);
            await File.WriteAllTextAsync(helperSourcePath, "public int32 Helper() { return 0; }");
            await File.WriteAllTextAsync(setSourcePath, ConsumerSource);
            await File.WriteAllTextAsync(middleSourcePath,
                " public int32 Middle() { return Compute(); }");
            await File.WriteAllTextAsync(rootSourcePath, GraphRootSource);

            var (trrmExit, trrmOutput, trrmError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                "compile-trrm", "-", "--identity", "arithmetic-cli", "-o", trrm,
                "--runtime", "fail-fast", "--arithmetic-profile", "release", "--json");
            Assert.That(trrmExit, Is.Zero, trrmOutput + trrmError);

            var (moduleProviderExit, moduleProviderOutput, moduleProviderError) = await RunAsync(
                [], "compile-module", "producer", providerSourcePath, "Compute",
                "--arithmetic-profile", "release", "-o", moduleProvider,
                "--runtime", "checked-exceptions", "--json");
            Assert.That(moduleProviderExit, Is.Zero, moduleProviderOutput + moduleProviderError);
            var (moduleConsumerExit, moduleConsumerOutput, moduleConsumerError) = await RunAsync(
                [], "compile-module", "consumer", consumerSourcePath,
                moduleProvider, "-o", moduleConsumer, "--runtime", "checked-exceptions",
                "--arithmetic-profile", "development", "--json");
            Assert.That(moduleConsumerExit, Is.Zero, moduleConsumerOutput + moduleConsumerError);
            var (moduleRun, moduleOutput, moduleError) = await RunAsync([],
                "run-module", moduleConsumer, moduleProvider, "--runtime", "checked-exceptions");
            Assert.That(moduleRun, Is.EqualTo(42), moduleOutput + moduleError);

            var (nativeProviderExit, nativeProviderOutput, nativeProviderError) = await RunAsync(
                [], "compile-native", "producer", providerSourcePath, "ArithmeticCliProvider",
                "--export", "Compute", "-o", nativeProvider, "--runtime", "checked-exceptions",
                "--arithmetic-profile", "release", "--json");
            Assert.That(nativeProviderExit, Is.Zero, nativeProviderOutput + nativeProviderError);
            var (nativeConsumerExit, nativeConsumerOutput, nativeConsumerError) = await RunAsync(
                [], "compile-native", "consumer", nativeConsumerSourcePath,
                nativeProvider, "-o", nativeConsumer, "--arithmetic-profile", "release",
                "--runtime", "checked-exceptions", "--json");
            Assert.That(nativeConsumerExit, Is.Zero, nativeConsumerOutput + nativeConsumerError);
            var (nativeRun, nativeOutput, nativeError) = await RunAsync([],
                "run-native", nativeConsumer, nativeProvider, "--runtime", "checked-exceptions");
            Assert.That(nativeRun, Is.EqualTo(42), nativeOutput + nativeError);

            var (helperExit, helperOutput, helperError) = await RunAsync(
                [], "compile-native", "producer", helperSourcePath, "ArithmeticCliHelper",
                "--export", "Helper", "-o", nativeHelper, "--runtime", "checked-exceptions", "--json");
            Assert.That(helperExit, Is.Zero, helperOutput + helperError);
            var (setExit, setOutput, setError) = await RunAsync(
                [], "compile-native-set", setSourcePath,
                "--provider", nativeProvider, "--provider", nativeHelper, "-o", nativeSet,
                "--arithmetic-profile", "release", "--runtime", "checked-exceptions", "--json");
            Assert.That(setExit, Is.Zero, setOutput + setError);
            var (setRun, setOutputText, setErrorText) = await RunAsync([],
                "run-native-set", nativeSet, "--provider", nativeHelper,
                "--provider", nativeProvider, "--runtime", "checked-exceptions");
            Assert.That(setRun, Is.EqualTo(42), setOutputText + setErrorText);
            var (middleExit, middleOutput, middleError) = await RunAsync(
                [], "compile-native-graph", "producer", middleSourcePath, "ArithmeticCliMiddle",
                "--provider", nativeProvider, "--export", "Middle", "-o", graphMiddle,
                "--runtime", "checked-exceptions", "--arithmetic-profile", "development", "--json");
            Assert.That(middleExit, Is.Zero, middleOutput + middleError);
            var (rootExit, rootOutput, rootError) = await RunAsync(
                [], "compile-native-graph", "consumer", rootSourcePath,
                "--provider", graphMiddle, "-o", graphRoot, "--arithmetic-profile", "release",
                "--runtime", "checked-exceptions", "--json");
            Assert.That(rootExit, Is.Zero, rootOutput + rootError);
            var (graphRun, graphOutput, graphError) = await RunAsync([],
                "run-native-graph", graphRoot, "--provider", graphMiddle,
                "--provider", nativeProvider, "--runtime", "checked-exceptions");
            Assert.That(graphRun, Is.EqualTo(42), graphOutput + graphError);
        }
        finally
        {
            foreach (var path in new[] {
                trrm, moduleProvider, moduleConsumer, nativeProvider, nativeConsumer,
                nativeHelper, nativeSet, graphMiddle, graphRoot, providerSourcePath,
                consumerSourcePath, nativeConsumerSourcePath, helperSourcePath, setSourcePath,
                middleSourcePath, rootSourcePath,
            })
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public async Task ArithmeticProfileOptionRejectsMalformedAndIneligibleCommandsAsync()
    {
        var outputPath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"arithmetic-profile-preserved-{Guid.NewGuid():N}.exe");
        byte[] original = [1, 2, 3];
        try
        {
            await File.WriteAllBytesAsync(outputPath, original);
            var invalid = new[] {
                new[] { "analyze", "-", "--arithmetic-profile", "Development", "--json" },
                new[] { "analyze", "-", "--arithmetic-profile", "debug", "--json" },
                new[] { "analyze", "-", "--arithmetic-profile", "1", "--json" },
                new[] { "analyze", "-", "--arithmetic-profile=release", "--json" },
                new[] { "analyze", "-", "--arithmetic-profile", "--json" },
                new[] { "build", "-", "-o", outputPath, "--runtime", "fail-fast",
                    "--arithmetic-profile", "release", "--arithmetic-profile", "release", "--json" },
            };
            foreach (var arguments in invalid)
            {
                AssertUsageError(await RunAsync([], arguments).ConfigureAwait(true));
                Assert.That(await File.ReadAllBytesAsync(outputPath).ConfigureAwait(true), Is.EqualTo(original));
            }

            var ineligible = new[] {
                new[] { "check", "-", "--arithmetic-profile", "release", "--json" },
                new[] { "tokens", "-", "--arithmetic-profile", "release", "--json" },
                new[] { "tree", "-", "--arithmetic-profile", "release", "--json" },
                new[] { "scan-component", "missing-component", "-", "--runtime", "fail-fast",
                    "--arithmetic-profile", "release", "--json" },
                new[] { "validate-trrm-structure", "missing.trrm", "--arithmetic-profile", "release", "--json" },
                new[] { "validate-trrm-compact", "missing.trrm", "--arithmetic-profile", "release", "--json" },
                new[] { "build-trrm", "missing.trrm", "-o", "unused.exe", "--runtime", "fail-fast",
                    "--arithmetic-profile", "release", "--json" },
                new[] { "build-trrm-library", "missing.trrm", "--export", "Value", "-o", "unused.dll",
                    "--runtime", "fail-fast", "--arithmetic-profile", "release", "--json" },
                new[] { "run-trrm", "missing.trrm", "--runtime", "fail-fast",
                    "--arithmetic-profile", "release", "--json" },
                new[] { "link-module", "missing-consumer.trm", "missing-provider.trm", "-o", "unused.exe",
                    "--runtime", "fail-fast", "--arithmetic-profile", "release", "--json" },
                new[] { "run-module", "missing-consumer.trm", "missing-provider.trm",
                    "--runtime", "fail-fast", "--arithmetic-profile", "release", "--json" },
                new[] { "validate-native", "missing-consumer.trmn", "missing-provider.trmn",
                    "--arithmetic-profile", "release", "--json" },
                new[] { "run-native", "missing-consumer.trmn", "missing-provider.trmn",
                    "--runtime", "fail-fast", "--arithmetic-profile", "release", "--json" },
                new[] { "validate-native-set", "missing.tnset", "--provider", "first.trmn",
                    "--provider", "second.trmn", "--arithmetic-profile", "release", "--json" },
                new[] { "run-native-set", "missing.tnset", "--provider", "first.trmn",
                    "--provider", "second.trmn", "--runtime", "fail-fast",
                    "--arithmetic-profile", "release", "--json" },
                new[] { "validate-native-graph", "missing.trng", "--provider", "provider.trmn",
                    "--arithmetic-profile", "release", "--json" },
                new[] { "resolve-native-graph", "missing.trng", "--candidate", "provider.trmn",
                    "--arithmetic-profile", "release", "--json" },
                new[] { "run-native-graph", "missing.trng", "--provider", "provider.trmn",
                    "--runtime", "fail-fast", "--arithmetic-profile", "release", "--json" },
                new[] { "run-native-graph-jit", "missing.trng", "--provider", "provider.trmn",
                    "--runtime", "fail-fast", "--arithmetic-profile", "release", "--json" },
            };
            foreach (var arguments in ineligible)
            {
                AssertUsageError(await RunAsync([], arguments).ConfigureAwait(true));
            }
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Test]
    public async Task ArithmeticProfileOptionDoesNotConsumeOptionLikeSourcePathsAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory("arithmetic-profile-positional-");
        var optionPath = Path.Combine(directory, "--arithmetic-profile");
        try
        {
            await File.WriteAllTextAsync(optionPath, "int32 Main() { return 42; }");
            var (positionalExit, positionalOutput, positionalError) = await RunAsync([],
                ["analyze", "--arithmetic-profile", "--json"], directory);
            Assert.That(positionalExit, Is.Zero, positionalOutput + positionalError);
            await File.WriteAllTextAsync(optionPath, "int32 Helper() { return 42; }");
            var (sourceExit, sourceOutput, sourceError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { return Helper(); }"),
                ["analyze", "-", "--source", "--arithmetic-profile", "--json"], directory);
            Assert.That(sourceExit, Is.Zero, sourceOutput + sourceError);
        }
        finally
        {
            File.Delete(optionPath);
            Directory.Delete(directory);
        }
    }

    [Test]
    public async Task NormalBackingAgreementSelectsSupportedAndRejectsUnsupportedRoutesAsync()
    {
        var input = Encoding.UTF8.GetBytes("""
            int32 Main() {
                mut int32 value = int32(40);
                mut int32 second = int32(0);
                mut write ref<int32> target = ref value;
                mut int32 observed = int32(0);
                {
                    ref<int32> readonlyTarget = ref *target;
                    *target = int32(41);
                    observed = *readonlyTarget;
                }
                target = ref second;
                *target = int32(42);
                return observed + second;
            }
            """);
        var unsupportedInput = Encoding.UTF8.GetBytes("""
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            Leaf Create() {
                require Leaf.TryCreate(42, out var leaf);
                return leaf;
            }
            int32 Main() {
                var leaf = Create();
                return leaf.Value;
            }
            """);
        var agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var (analysisExit, analysisOutput, analysisError) = await RunAsync(input,
            "analyze", "-", "--native-lowering-agreement", agreement, "--json");
        Assert.That(analysisExit, Is.Zero, analysisOutput + analysisError);
        Assert.That(analysisError, Is.Empty);
        using (var analysis = JsonDocument.Parse(analysisOutput))
        {
            Assert.That(analysis.RootElement.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(analysis.RootElement.GetProperty("profile").ValueKind,
                Is.EqualTo(JsonValueKind.Null));
            Assert.That(analysis.RootElement.GetProperty("nativeLoweringAgreement")
                .GetString(), Is.EqualTo(agreement));
        }

        var outputPath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"normal-backing-{Guid.NewGuid():N}.exe");
        var nativeImportPath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"normal-backing-native-import-{Guid.NewGuid():N}.exe");
        var unsupportedOutputPath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"normal-backing-unsupported-{Guid.NewGuid():N}.exe");
        try
        {
            var (buildExit, buildOutput, buildError) = await RunAsync(input,
                "build", "-", "-o", outputPath, "--runtime", "fail-fast",
                "--native-lowering-agreement", agreement, "--json");
            Assert.That(buildExit, Is.Zero, buildOutput + buildError);
            Assert.That(buildError, Is.Empty);
            using (var build = JsonDocument.Parse(buildOutput))
            {
                Assert.That(build.RootElement.GetProperty("success").GetBoolean(), Is.True);
                Assert.That(build.RootElement.GetProperty("nativeLoweringAgreement")
                    .GetString(), Is.EqualTo(agreement));
            }
            Assert.That(await ExecuteNativeImageAsync(outputPath).ConfigureAwait(true), Is.EqualTo(83));

            var (nativeImportExit, nativeImportOutput, nativeImportError) =
                await RunAsync(input, "build-native-import", "-", "-o", nativeImportPath,
                    "--runtime", "fail-fast", "--native-lowering-agreement",
                    agreement, "--json");
            Assert.That(nativeImportExit, Is.EqualTo(1),
                nativeImportOutput + nativeImportError);
            Assert.That(nativeImportError, Is.Empty);
            using (var nativeImport = JsonDocument.Parse(nativeImportOutput))
            {
                Assert.That(nativeImport.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM4001"));
            }
            Assert.That(File.Exists(nativeImportPath), Is.False);

            var (jitExit, jitOutput, jitError) = await RunAsync(input,
                "jit", "-", "--runtime", "fail-fast", "--native-lowering-agreement",
                agreement, "--json");
            Assert.That(jitExit, Is.EqualTo(83), jitOutput + jitError);
            Assert.That(jitOutput, Is.Empty);
            Assert.That(jitError, Is.Empty);

            var (unsupportedBuildExit, unsupportedBuildOutput, unsupportedBuildError) =
                await RunAsync(unsupportedInput, "build", "-", "-o", unsupportedOutputPath,
                    "--runtime", "fail-fast", "--native-lowering-agreement",
                    agreement, "--json");
            Assert.That(unsupportedBuildExit, Is.EqualTo(1),
                unsupportedBuildOutput + unsupportedBuildError);
            Assert.That(unsupportedBuildError, Is.Empty);
            using (var unsupportedBuild = JsonDocument.Parse(unsupportedBuildOutput))
            {
                Assert.That(unsupportedBuild.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM4001"));
            }
            Assert.That(File.Exists(unsupportedOutputPath), Is.False);

            var invalid = new[] {
                new[] { "analyze", "-", "--native-lowering-agreement",
                    "terrum.experimental.windows-x64.Normal-backing", "--json" },
                new[] { "analyze", "-", "--native-lowering-agreement", agreement,
                    "--native-lowering-agreement", agreement, "--json" },
                new[] { "check", "-", "--native-lowering-agreement", agreement, "--json" },
                new[] { "analyze", "-", "--corelib", "--native-lowering-agreement",
                    agreement, "--json" },
                new[] { "build-library", "-", "--export", "Main", "-o",
                    outputPath, "--runtime", "fail-fast", "--native-lowering-agreement",
                    agreement, "--json" },
            };
            foreach (var arguments in invalid)
            {
                AssertUsageError(await RunAsync(input, arguments).ConfigureAwait(true));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(outputPath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(nativeImportPath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(unsupportedOutputPath).ConfigureAwait(true);
        }
    }

    private static void AssertUsageError((int ExitCode, string Output, string Error) result)
    {
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Error, Is.Empty);
        using var document = JsonDocument.Parse(result.Output);
        Assert.That(document.RootElement.GetProperty("diagnostics")[0]
            .GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
    }
}
