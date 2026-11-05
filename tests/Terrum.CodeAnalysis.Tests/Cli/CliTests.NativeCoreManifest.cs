// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    private const string NativeCoreImportSource = """
        [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentThreadId",
            CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
        extern unsafe int32 GetCurrentThreadId();

        int32 Main() {
            unsafe {
                if (GetCurrentThreadId() == 0) { return 1; }
            }
            return 0;
        }
        """;

    private const string NativeCoreCallableSource = """
        [Terrum.Interop.NativeCallable(
            CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
        public int32 Identity(int32 value) { return value; }

        unsafe int32 Invoke(
            [Terrum.Interop.NativeCallConv(
                Terrum.Interop.NativeCallingConvention.Cdecl)]
            fnptr<int32, int32> callback, int32 value) {
            unsafe { return callback(value); }
        }

        int32 Main() { return 0; }
        """;

    [Test]
    public async Task NativeCoreManifestReachesAllSelectedSourceCommandsAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-native-core-manifest-");
        var manifestPath = Path.Combine(directory, "native.json");
        try
        {
            await File.WriteAllTextAsync(manifestPath,
                CreateNativeCoreManifest()).ConfigureAwait(true);
            var importSource = Encoding.UTF8.GetBytes(NativeCoreImportSource);

            var (analyzed, analyzeOutput, analyzeError) = await RunAsync(
                importSource, "analyze", "-", "--native-core-manifest",
                manifestPath, "--json").ConfigureAwait(true);
            Assert.That(analyzed, Is.Zero, analyzeOutput + analyzeError);
            Assert.That(analyzeError, Is.Empty);
            using (var result = JsonDocument.Parse(analyzeOutput))
            {
                Assert.That(result.RootElement.GetProperty("success")
                    .GetBoolean(), Is.True);
            }

            var executablePath = Path.Combine(directory, "native-import.exe");
            var (builtImport, importOutput, importError) = await RunAsync(
                importSource, "build-native-import", "-",
                "--native-core-manifest", manifestPath,
                "-o", executablePath, "--runtime", "fail-fast",
                "--json").ConfigureAwait(true);
            Assert.That(builtImport, Is.Zero, importOutput + importError);
            Assert.That(importError, Is.Empty);
            Assert.That(File.Exists(executablePath), Is.True);
            Assert.That(new FileInfo(executablePath).Length, Is.GreaterThan(0));

            var executableProgramPath = Path.Combine(directory, "native.exe");
            var (built, buildOutput, buildError) = await RunAsync(
                Encoding.UTF8.GetBytes(NativeCoreCallableSource), "build", "-",
                "--native-core-manifest", manifestPath,
                "-o", executableProgramPath, "--runtime", "fail-fast",
                "--json").ConfigureAwait(true);
            Assert.That(built, Is.Zero, buildOutput + buildError);
            Assert.That(File.Exists(executableProgramPath), Is.True);

            var libraryPath = Path.Combine(directory, "native-callable.dll");
            var (builtLibrary, libraryOutput, libraryError) = await RunAsync(
                Encoding.UTF8.GetBytes(NativeCoreCallableSource),
                "build-library", "-", "--native-core-manifest", manifestPath,
                "--export", "Identity", "-o", libraryPath,
                "--runtime", "fail-fast", "--json").ConfigureAwait(true);
            Assert.That(builtLibrary, Is.Zero,
                libraryOutput + libraryError);
            Assert.That(File.Exists(libraryPath), Is.True);

            var nativeAddressSource = Encoding.UTF8.GetBytes("""
                [Terrum.Interop.NativeImport(
                    "example.dll",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                internal extern void Observe();

                internal void Form() {
                    unsafe { _ = &Observe; }
                }
                """);
            var (jitResult, jitOutput, jitError) = await RunAsync(
                Encoding.UTF8.GetBytes(NativeCoreCallableSource), "jit", "-",
                "--native-core-manifest", manifestPath,
                "--runtime", "fail-fast").ConfigureAwait(true);
            Assert.That(jitResult, Is.Zero, jitOutput + jitError);

            var trrmPath = Path.Combine(directory, "native-import.trrm");
            var (compiledTrRm, trrmOutput, trrmError) = await RunAsync(
                nativeAddressSource, "compile-trrm", "-", "--identity",
                "sample.consumer", "-o", trrmPath, "--runtime",
                "fail-fast", "--trrm-schema",
                "trrm/1-draft-foreign-invocation-contracts-1",
                "--native-core-manifest", manifestPath, "--library", "--json")
                .ConfigureAwait(true);
            Assert.That(compiledTrRm, Is.Zero, trrmOutput + trrmError);
            Assert.That(trrmError, Is.Empty);
            Assert.That(File.Exists(trrmPath), Is.True);
            Assert.That(new FileInfo(trrmPath).Length, Is.GreaterThan(0));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task NativeAndLayoutManifestsComposeForTheSameUsingModuleAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-core-manifest-composition-");
        var nativePath = Path.Combine(directory, "native.json");
        var layoutPath = Path.Combine(directory, "layout.json");
        var outputPath = Path.Combine(directory, "combined.exe");
        try
        {
            await File.WriteAllTextAsync(nativePath,
                CreateNativeCoreManifest(
                    coreAuthorityIdentity: "test-layout-authority"))
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(layoutPath,
                CreateLayoutCoreManifest()).ConfigureAwait(true);
            var source = Encoding.UTF8.GetBytes(LayoutCoreNativeImportSource);

            var (exit, output, error) = await RunAsync(source,
                "build-native-import", "-",
                "--layout-core-manifest", layoutPath,
                "--native-core-manifest", nativePath,
                "-o", outputPath, "--runtime", "fail-fast", "--json")
                .ConfigureAwait(true);
            Assert.That(exit, Is.Zero, output + error);
            Assert.That(File.Exists(outputPath), Is.True);

            var sharedNativePath = Path.Combine(directory,
                "shared-native.json");
            await File.WriteAllTextAsync(sharedNativePath,
                CreateNativeCoreManifest(
                    providerIdentity: "sample.layout-core",
                    revisionIdentity: "revision-1",
                    providerSchemaIdentity: "layout-registration"))
                .ConfigureAwait(true);
            var (sharedExit, sharedOutput, sharedError) = await RunAsync(
                source, "build-native-import", "-",
                "--layout-core-manifest", layoutPath,
                "--native-core-manifest", sharedNativePath,
                "-o", outputPath, "--runtime", "fail-fast", "--json")
                .ConfigureAwait(true);
            Assert.That(sharedExit, Is.Zero, sharedOutput + sharedError);

            var conflictingNativePath = Path.Combine(directory,
                "conflicting-native.json");
            await File.WriteAllTextAsync(conflictingNativePath,
                CreateNativeCoreManifest(usingModuleIdentity: "other.consumer",
                    coreAuthorityIdentity: "test-layout-authority"))
                .ConfigureAwait(true);
            var sentinelPath = Path.Combine(directory, "sentinel.exe");
            var sentinel = Encoding.ASCII.GetBytes("prior-output");
            await File.WriteAllBytesAsync(sentinelPath, sentinel)
                .ConfigureAwait(true);
            var (conflictExit, conflictOutput, conflictError) =
                await RunAsync(source, "build-native-import", "-",
                    "--layout-core-manifest", layoutPath,
                    "--native-core-manifest", conflictingNativePath,
                    "-o", sentinelPath, "--runtime", "fail-fast", "--json")
                    .ConfigureAwait(true);
            Assert.That(conflictExit, Is.EqualTo(2),
                conflictOutput + conflictError);
            AssertInputDiagnosticCode(conflictOutput, "TRM2001");
            Assert.That(await File.ReadAllBytesAsync(sentinelPath)
                .ConfigureAwait(true), Is.EqualTo(sentinel));

            var conflictingOriginPath = Path.Combine(directory,
                "conflicting-origin.json");
            await File.WriteAllTextAsync(conflictingOriginPath,
                CreateNativeCoreManifest(
                    coreAuthorityIdentity: "test-layout-authority",
                    providerIdentity: "sample.layout-core",
                    revisionIdentity: "different-revision",
                    providerSchemaIdentity: "layout-registration"))
                .ConfigureAwait(true);
            var (originExit, originOutput, originError) = await RunAsync(
                source, "build-native-import", "-",
                "--layout-core-manifest", layoutPath,
                "--native-core-manifest", conflictingOriginPath,
                "-o", sentinelPath, "--runtime", "fail-fast", "--json")
                .ConfigureAwait(true);
            Assert.That(originExit, Is.EqualTo(2), originOutput + originError);
            AssertInputDiagnosticCode(originOutput, "TRM2001");
            Assert.That(await File.ReadAllBytesAsync(sentinelPath)
                .ConfigureAwait(true), Is.EqualTo(sentinel));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task NativeCoreManifestRejectsAbsentMalformedAndUnselectedRolesAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-native-core-manifest-rejections-");
        var manifestPath = Path.Combine(directory, "native.json");
        var malformedPath = Path.Combine(directory, "malformed.json");
        var outputPath = Path.Combine(directory, "sentinel.exe");
        var layoutPath = Path.Combine(directory, "layout.json");
        var source = Encoding.UTF8.GetBytes(NativeCoreImportSource);
        var sentinel = Encoding.ASCII.GetBytes("prior-output");
        try
        {
            await File.WriteAllTextAsync(manifestPath,
                CreateNativeCoreManifest()).ConfigureAwait(true);
            await File.WriteAllTextAsync(layoutPath,
                CreateLayoutCoreManifest()).ConfigureAwait(true);
            await File.WriteAllBytesAsync(outputPath, sentinel)
                .ConfigureAwait(true);

            var (unregistered, unregisteredOutput, unregisteredError)
                = await RunAsync(source, "build-native-import", "-",
                    "-o", outputPath, "--runtime", "fail-fast", "--json")
                    .ConfigureAwait(true);
            Assert.That(unregistered, Is.EqualTo(1),
                unregisteredOutput + unregisteredError);
            Assert.That(await File.ReadAllBytesAsync(outputPath)
                .ConfigureAwait(true), Is.EqualTo(sentinel));

            string[] unregisteredForms = [
                """
                [Terrum.Interop.NativeImport("example.dll", "Entry")]
                extern unsafe int32 Entry();
                int32 Main() { unsafe { return Entry(); } }
                """,
                """
                [Terrum.Interop.NativeCallable]
                public int32 Identity(int32 value) { return value; }
                int32 Main() { return 0; }
                """,
            ];
            foreach (var unregisteredForm in unregisteredForms)
            {
                var (unregisteredFormExit, unregisteredFormOutput,
                    unregisteredFormError) = await RunAsync(
                        Encoding.UTF8.GetBytes(unregisteredForm), "analyze",
                        "-", "--json").ConfigureAwait(true);
                Assert.That(unregisteredFormExit, Is.EqualTo(1),
                    unregisteredFormOutput + unregisteredFormError);
                Assert.That(unregisteredFormOutput, Does.Contain("TRM3030"));
            }

            var (layoutOnly, layoutOnlyOutput, layoutOnlyError)
                = await RunAsync(source, "analyze", "-",
                    "--layout-core-manifest", layoutPath, "--json")
                    .ConfigureAwait(true);
            Assert.That(layoutOnly, Is.EqualTo(1),
                layoutOnlyOutput + layoutOnlyError);
            Assert.That(layoutOnlyOutput, Does.Contain("TRM3030"));

            var (corelibExit, corelibOutput, corelibError) =
                await RunAsync(source, "analyze", "-", "--corelib", "--json")
                    .ConfigureAwait(true);
            Assert.That(corelibExit, Is.EqualTo(1),
                corelibOutput + corelibError);
            Assert.That(corelibOutput, Does.Contain("TRM3030"));

            var manifestText = CreateNativeCoreManifest();
            var malformedManifests = new byte[][] {
                Encoding.UTF8.GetBytes(manifestText.Replace(
                    "\"schema\":\"terrumc.native-core-manifest/1\"",
                    "\"schema\":\"terrumc.native-core-manifest/1\","
                    + "\"schema\":\"terrumc.native-core-manifest/1\"",
                    StringComparison.Ordinal)),
                Encoding.UTF8.GetBytes(manifestText.Replace(
                    "\"schema\":\"terrumc.native-core-manifest/1\"",
                    "\"schema\":\"terrumc.native-core-manifest/1\","
                    + "\"\\u0073chema\":\"terrumc.native-core-manifest/1\"",
                    StringComparison.Ordinal)),
                Encoding.UTF8.GetBytes(manifestText.Replace(
                    "\"nativeCallConv\":{",
                    "\"nativeExport\":{},\"nativeCallConv\":{",
                    StringComparison.Ordinal)),
                Encoding.UTF8.GetBytes(manifestText.Replace(
                    "\"genericArity\":0,",
                    "\"genericArity\":0.0,",
                    StringComparison.Ordinal)),
                [0xFF],
                [0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF],
            };
            foreach (var malformedManifest in malformedManifests)
            {
                await File.WriteAllBytesAsync(malformedPath,
                    malformedManifest).ConfigureAwait(true);
                var (malformed, malformedOutput, malformedError)
                    = await RunAsync(source, "analyze", "-",
                        "--native-core-manifest", malformedPath, "--json")
                        .ConfigureAwait(true);
                Assert.That(malformed, Is.EqualTo(2),
                    malformedOutput + malformedError);
                AssertInputDiagnosticCode(malformedOutput, "TRM2001");
            }

            var (excluded, excludedOutput, excludedError)
                = await RunAsync(source, "check", "-",
                    "--native-core-manifest", manifestPath, "--json")
                    .ConfigureAwait(true);
            Assert.That(excluded, Is.EqualTo(2),
                excludedOutput + excludedError);
            AssertInputDiagnosticCode(excludedOutput, "TRM2000");

            string[][] invalidOptions = [
                ["analyze", "-", "--native-core-manifest",
                    manifestPath, "--native-core-manifest", manifestPath,
                    "--json"],
                ["analyze", "-", "--native-core-manifest=" + manifestPath,
                    "--json"],
                ["analyze", "-", "--native-core-manifest", "",
                    "--json"],
                ["analyze", "-", "--native-core-manifest",
                    "-native.json", "--json"],
                ["analyze", "-", "--machine", "terrum.cli/1",
                    "--native-core-manifest", manifestPath, "--json"],
                ["tokens", "-", "--native-core-manifest", manifestPath,
                    "--json"],
                ["run-trrm", "-", "--native-core-manifest", manifestPath,
                    "--json"],
                ["build-trrm", "-", "--native-core-manifest", manifestPath,
                    "--json"],
                ["build", "-", "--frontend-command", "frontend-build",
                    "--source-name", "component.terrum",
                    "--native-core-manifest", manifestPath,
                    "-o", Path.Combine(directory, "frontend.exe"),
                    "--runtime", "fail-fast", "--json"],
            ];
            foreach (var arguments in invalidOptions)
            {
                var (invalidExit, invalidOutput, invalidError) =
                    await RunAsync(source, arguments).ConfigureAwait(true);
                Assert.That(invalidExit, Is.EqualTo(2),
                    invalidOutput + invalidError);
                AssertInputDiagnosticCode(invalidOutput, "TRM2000");
            }

            var exportSource = Encoding.UTF8.GetBytes("""
                [Terrum.Interop.NativeExport]
                public int32 Identity(int32 value) { return value; }
                int32 Main() { return 0; }
                """);
            var (unselectedExport, exportOutput, exportError)
                = await RunAsync(exportSource, "analyze", "-",
                    "--native-core-manifest", manifestPath, "--json")
                    .ConfigureAwait(true);
            Assert.That(unselectedExport, Is.EqualTo(1),
                exportOutput + exportError);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task NativeCoreManifestRequiresCompileTrRmIdentityAgreementAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-native-core-manifest-identity-");
        var manifestPath = Path.Combine(directory, "native.json");
        var outputPath = Path.Combine(directory, "sentinel.trrm");
        var sentinel = Encoding.ASCII.GetBytes("prior-output");
        try
        {
            await File.WriteAllTextAsync(manifestPath,
                CreateNativeCoreManifest()).ConfigureAwait(true);
            await File.WriteAllBytesAsync(outputPath, sentinel)
                .ConfigureAwait(true);
            var (exit, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes(NativeCoreImportSource),
                "compile-trrm", "-", "--identity", "other.consumer",
                "-o", outputPath, "--runtime", "fail-fast",
                "--trrm-schema",
                "trrm/1-draft-foreign-invocation-contracts-1",
                "--native-core-manifest", manifestPath, "--json")
                .ConfigureAwait(true);
            Assert.That(exit, Is.EqualTo(2), output + error);
            AssertInputDiagnosticCode(output, "TRM2000");
            Assert.That(await File.ReadAllBytesAsync(outputPath)
                .ConfigureAwait(true), Is.EqualTo(sentinel));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task NativeCoreManifestReadsInOptionOrderAndRejectsAliasesAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-native-core-manifest-order-");
        var nativePath = Path.Combine(directory, "native.json");
        var invalidNativePath = Path.Combine(directory,
            "invalid-native.json");
        var layoutPath = Path.Combine(directory, "layout.json");
        var aliasOutputPath = Path.Combine(directory, "manifest-alias.exe");
        var source = Encoding.UTF8.GetBytes("int32 Main() { return 0; }");
        var malformedSource = new byte[] { 0xFF };
        try
        {
            await File.WriteAllTextAsync(nativePath,
                CreateNativeCoreManifest()).ConfigureAwait(true);
            await File.WriteAllTextAsync(layoutPath,
                "{\"schema\":\"invalid-layout\"}").ConfigureAwait(true);
            await File.WriteAllTextAsync(invalidNativePath,
                "{\"schema\":\"invalid-native\"}").ConfigureAwait(true);
            var manifestContents = await File.ReadAllBytesAsync(nativePath)
                .ConfigureAwait(true);
            if (OperatingSystem.IsWindows())
            {
                Assert.That(CreateHardLink(aliasOutputPath, nativePath,
                    IntPtr.Zero), Is.True);
                var (aliasExit, aliasOutput, aliasError) = await RunAsync(
                    source, "build", "-", "--native-core-manifest",
                    nativePath, "-o", aliasOutputPath, "--runtime",
                    "fail-fast", "--json").ConfigureAwait(true);
                Assert.That(aliasExit, Is.EqualTo(2),
                    aliasOutput + aliasError);
                AssertInputDiagnosticCode(aliasOutput, "TRM2002");
                Assert.That(await File.ReadAllBytesAsync(nativePath)
                    .ConfigureAwait(true), Is.EqualTo(manifestContents));

                var aliasSourcePath = Path.Combine(directory,
                    "manifest-alias.terrum");
                Assert.That(CreateHardLink(aliasSourcePath, nativePath,
                    IntPtr.Zero), Is.True);
                var (sourceAliasExit, sourceAliasOutput, sourceAliasError)
                    = await RunAsync([], "analyze", aliasSourcePath,
                        "--native-core-manifest", nativePath, "--json")
                        .ConfigureAwait(true);
                Assert.That(sourceAliasExit, Is.EqualTo(2),
                    sourceAliasOutput + sourceAliasError);
                AssertInputDiagnosticCode(sourceAliasOutput, "TRM2002");

                var aliasTrRmPath = Path.Combine(directory,
                    "manifest-alias.trrm");
                Assert.That(CreateHardLink(aliasTrRmPath, nativePath,
                    IntPtr.Zero), Is.True);
                var (trRmAliasExit, trRmAliasOutput, trRmAliasError) =
                    await RunAsync(source, "compile-trrm", "-",
                        "--identity", "sample.consumer", "-o",
                        aliasTrRmPath, "--runtime", "fail-fast",
                        "--trrm-schema",
                        "trrm/1-draft-foreign-invocation-contracts-1",
                        "--native-core-manifest", nativePath, "--library",
                        "--json").ConfigureAwait(true);
                Assert.That(trRmAliasExit, Is.EqualTo(2),
                    trRmAliasOutput + trRmAliasError);
                AssertInputDiagnosticCode(trRmAliasOutput, "TRM2002");
                Assert.That(await File.ReadAllBytesAsync(nativePath)
                    .ConfigureAwait(true), Is.EqualTo(manifestContents));
            }

            var (sameManifestExit, sameManifestOutput, sameManifestError) =
                await RunAsync(source, "analyze", "-",
                    "--layout-core-manifest", nativePath,
                    "--native-core-manifest", nativePath, "--json")
                    .ConfigureAwait(true);
            Assert.That(sameManifestExit, Is.EqualTo(2),
                sameManifestOutput + sameManifestError);
            AssertInputDiagnosticCode(sameManifestOutput, "TRM2002");

            var abiAliasPath = Path.Combine(directory, "native-library.dll");
            var (abiAliasExit, abiAliasOutput, abiAliasError) = await RunAsync(
                source, "build-library", "-", "--native-core-manifest",
                nativePath, "--export", "Main", "--source-grant",
                "--abi-output", nativePath, "-o", abiAliasPath,
                "--runtime", "fail-fast", "--json").ConfigureAwait(true);
            Assert.That(abiAliasExit, Is.EqualTo(2),
                abiAliasOutput + abiAliasError);
            AssertInputDiagnosticCode(abiAliasOutput, "TRM2002");
            Assert.That(await File.ReadAllBytesAsync(nativePath)
                .ConfigureAwait(true), Is.EqualTo(manifestContents));

            var (nativeFirstExit, nativeFirstOutput, nativeFirstError) =
                await RunAsync(malformedSource, "analyze", "-",
                    "--native-core-manifest", invalidNativePath,
                    "--layout-core-manifest", layoutPath, "--json")
                    .ConfigureAwait(true);
            Assert.That(nativeFirstExit, Is.EqualTo(2),
                nativeFirstOutput + nativeFirstError);
            Assert.That(nativeFirstOutput,
                Does.Contain(Path.GetFileName(invalidNativePath)));

            var (layoutFirstExit, layoutFirstOutput, layoutFirstError) =
                await RunAsync(malformedSource, "analyze", "-",
                    "--layout-core-manifest", layoutPath,
                    "--native-core-manifest", nativePath, "--json")
                    .ConfigureAwait(true);
            Assert.That(layoutFirstExit, Is.EqualTo(2),
                layoutFirstOutput + layoutFirstError);
            Assert.That(layoutFirstOutput,
                Does.Contain(Path.GetFileName(layoutPath)));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    internal static string CreateNativeCoreManifest(
        string usingModuleIdentity = "sample.consumer",
        string coreAuthorityIdentity = "test-layout-authority",
        string providerIdentity = "sample.native-core",
        string revisionIdentity = "native-provider-revision-1",
        string providerSchemaIdentity = "native-registration-schema")
    {
        object Identity(string exportKey, string kind)
        {
            return new { providerIdentity, exportKey, kind };
        }

        object EnumMember(object enumIdentity, string enumKey, string name,
            int value)
        {
            return new {
                identity = Identity(enumKey, "enum-member"),
                declaringEnum = enumIdentity,
                declarationKey = $"member-key/{enumKey}",
                name,
                value,
                isPublic = true,
            };
        }

        object Enum(string exportKey, string qualifiedName,
            (string Name, int Value)[] members)
        {
            var identity = Identity(exportKey, "enum-type");
            return new {
                identity,
                qualifiedName,
                underlyingType = "int32",
                members = members.Select(member =>
                    EnumMember(identity,
                        $"{exportKey}.{member.Name}", member.Name,
                        member.Value)).ToArray(),
                genericArity = 0,
                isPublic = true,
                isPrimary = true,
                isFlags = false,
                blittableTargetRequirement = 0,
            };
        }

        var conventions = Enum("NativeCallingConvention",
            "Terrum.Interop.NativeCallingConvention", [
                ("Unmanaged", 1), ("Cdecl", 2), ("Stdcall", 3),
                ("Vectorcall", 4),
            ]);
        var resolutionModes = Enum("NativeImportResolutionMode",
            "Terrum.Interop.NativeImportResolutionMode",
            [("Link", 1), ("Dynamic", 2)]);
        var resolutionPolicies = Enum("NativeImportResolutionPolicy",
            "Terrum.Interop.NativeImportResolutionPolicy",
            [("Default", 1), ("CompatibilitySearch", 2)]);
        var conventionsIdentity = Identity("NativeCallingConvention",
            "enum-type");
        var modesIdentity = Identity("NativeImportResolutionMode",
            "enum-type");
        var policiesIdentity = Identity("NativeImportResolutionPolicy",
            "enum-type");
        var unmanagedMember = Identity("NativeCallingConvention.Unmanaged",
            "enum-member");
        var linkMember = Identity("NativeImportResolutionMode.Link",
            "enum-member");
        var defaultPolicyMember = Identity(
            "NativeImportResolutionPolicy.Default", "enum-member");

        object EnumDefault(object member)
        {
            return new { kind = "enum-member", member };
        }
        var allImportContexts = new[] {
            "callable-link", "callable-dynamic", "data-link", "data-dynamic",
        };
        object Argument(string? name, string kind, object? enumType,
            bool isRequired, bool allowsPositional, bool allowsNamed,
            string[] contexts, object? @default)
        {
            return new {
                name,
                kind,
                enumType,
                isRequired,
                allowsPositional,
                allowsNamed,
                contexts,
                @default,
            };
        }

        var nativeImportArguments = new object[] {
            Argument("LibraryName", "library-name-literal", null, true,
                true, true, allImportContexts, null),
            Argument("EntryName", "entry-name-literal", null, false,
                true, true, allImportContexts,
                new { kind = "declaration-name" }),
            Argument("ResolutionMode", "enum", modesIdentity, false,
                false, true, allImportContexts, EnumDefault(linkMember)),
            Argument("ResolutionPolicy", "enum", policiesIdentity, false,
                false, true, ["callable-dynamic", "data-dynamic"],
                EnumDefault(defaultPolicyMember)),
            Argument("CallConv", "enum", conventionsIdentity, false,
                false, true, ["callable-link", "callable-dynamic"],
                EnumDefault(unmanagedMember)),
        };
        var nativeCallableArguments = new object[] {
            Argument("CallConv", "enum", conventionsIdentity, false,
                false, true, ["method", "interface"],
                EnumDefault(unmanagedMember)),
        };
        var nativeCallConvArguments = new object[] {
            Argument(null, "enum", conventionsIdentity, true,
                true, false, ["fnptr-type-use"], null),
        };

        object Schema(string exportKey, string qualifiedName, string role,
            string placement, object[] arguments, string[] targets)
        {
            return new {
                identity = Identity(exportKey, "attribute-schema"),
                qualifiedName,
                role,
                arguments,
                placement,
                targets,
                genericArity = 0,
                isPublic = true,
                isPrimary = true,
                isRepeatable = false,
            };
        }

        return JsonSerializer.Serialize(new {
            schema = "terrumc.native-core-manifest/1",
            coreAuthorityIdentity,
            usingModuleIdentity,
            providers = new[] {
                new {
                    moduleIdentity = providerIdentity,
                    revisionIdentity,
                    schema = new {
                        identity = providerSchemaIdentity,
                        revision = 1,
                    },
                    governingSemanticEdition = 1,
                    targetRequirement = 0,
                    runtimePolicy = 0,
                },
            },
            callingConventions = conventions,
            resolutionModes,
            resolutionPolicies,
            nativeImport = Schema("NativeImport",
                "Terrum.Interop.NativeImport", "native-import",
                "primary-declaration", nativeImportArguments,
                ["static-extern-callable", "static-extern-data"]),
            nativeCallable = Schema("NativeCallable",
                "Terrum.Interop.NativeCallable", "native-callable",
                "primary-declaration", nativeCallableArguments,
                ["ordinary-method", "interface"]),
            nativeCallConv = Schema("NativeCallConv",
                "Terrum.Interop.NativeCallConv", "native-callconv",
                "immediate-fnptr-type-use", nativeCallConvArguments,
                ["fnptr-type-use"]),
        });
    }
}
