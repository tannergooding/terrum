// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Semantics;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task BuildLibraryEmitsSourceGrantManifestFromSelectedExportsAsync(string runtime)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var repositoryRoot = RepositoryRoot();
        var temporaryDirectory = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-cli-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(temporaryDirectory);
        var imagePath = Path.Combine(temporaryDirectory, "compiler-primitives.dll");
        var manifestPath = Path.Combine(temporaryDirectory, "compiler-primitives.abi.json");
        try
        {
            var (ExitCode, Output, Error) = await RunAsync([], [
                "build-library", TerrumProjectPaths.SourceFile(repositoryRoot, Path.Combine("Text", "Utf16CodeUnits.terrum")),
                "--source", TerrumProjectPaths.SourceFile(repositoryRoot, Path.Combine("Text", "Utf16Scalar.terrum")),
                "--export", "Terrum.CodeAnalysis.DecodeUtf16Scalar",
                "--export", "Terrum.CodeAnalysis.ReadUtf16ScalarChecked",
                "-o", imagePath, "--runtime", runtime,
                "--source-grant", "--abi-output", manifestPath,
            ]);
            Assert.That(ExitCode, Is.EqualTo(0));
            Assert.That(Error, Is.Empty);

            var image = File.ReadAllBytes(imagePath);
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var root = manifest.RootElement;
            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("sourceGrantAbiVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("maximumSourceCodeUnits").GetInt32(), Is.EqualTo(16_384));
            Assert.That(root.GetProperty("runtimePolicy").GetString(), Is.EqualTo(runtime));
            Assert.That(root.GetProperty("imageSha256").GetString(), Is.EqualTo(Convert.ToHexString(SHA256.HashData(image))));

            var exports = root.GetProperty("exports").EnumerateArray()
                .Select(static export => (
                    Name: export.GetProperty("name").GetString()
                        ?? throw new InvalidDataException("Missing export name."),
                    SourceDependent: export.GetProperty("sourceDependent").GetBoolean(),
                    ReturnType: Enum.Parse<PrimitiveType>(export.GetProperty("returnType").GetString()
                        ?? throw new InvalidDataException("Missing return type.")),
                    ParameterTypes: export.GetProperty("parameterTypes").EnumerateArray()
                        .Select(static type => Enum.Parse<PrimitiveType>(type.GetString()
                            ?? throw new InvalidDataException("Missing parameter type.")))
                        .ToImmutableArray()))
                .ToImmutableArray();

            var module = NativeLibrary.Load(imagePath);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module, exports);
                Assert.That(bindings.Invoke<int>("Terrum.CodeAnalysis.ReadUtf16ScalarChecked", ['\uD83D', '\uDE00'], 0, 2), Is.EqualTo(0x1F600));
                Assert.That(bindings.Invoke<int>("Terrum.CodeAnalysis.DecodeUtf16Scalar", null,
                        (ushort)'\uD83D', (ushort)'\uDE00'), Is.EqualTo(0x1F600));
                Assert.That(global::System.Linq.Enumerable.Single(exports, static export => export.Name == "Terrum.CodeAnalysis.ReadUtf16ScalarChecked").SourceDependent, Is.True);
                Assert.That(global::System.Linq.Enumerable.Single(exports, static export => export.Name == "Terrum.CodeAnalysis.DecodeUtf16Scalar").SourceDependent, Is.False);
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }
        finally
        {
            if (File.Exists(imagePath))
            {
                File.Delete(imagePath);
            }
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }
            Directory.Delete(temporaryDirectory);
        }
    }
    [TestCase("image")]
    [TestCase("manifest")]
    public async Task FailedSourceGrantArtifactReplacementPreservesTheExistingPairAsync(string lockedArtifact)
    {
        Assert.That(OperatingSystem.IsWindows(), Is.True);
        var temporaryDirectory = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-pair-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(temporaryDirectory);
        var sourcePath = Path.Combine(temporaryDirectory, "library.terrum");
        var imagePath = Path.Combine(temporaryDirectory, "library.dll");
        var manifestPath = Path.Combine(temporaryDirectory, "library.abi.json");
        try
        {
            File.WriteAllText(sourcePath, """
                namespace SourceGrant.Pair;
                public int32 GetValue(int32 value) { return value; }
                int32 ReadSource(int32 at) { return int32(Host.Source[at]); }
                """);
            var (initialExitCode, _, initialError) = await RunAsync([], [
                "build-library", sourcePath, "--export", "SourceGrant.Pair.GetValue",
                "-o", imagePath, "--runtime", "fail-fast", "--source-grant", "--abi-output", manifestPath,
            ]);
            Assert.That(initialExitCode, Is.EqualTo(0));
            Assert.That(initialError, Is.Empty);
            var originalImage = File.ReadAllBytes(imagePath);
            var originalManifest = File.ReadAllBytes(manifestPath);

            File.WriteAllText(sourcePath, """
                namespace SourceGrant.Pair;
                public int32 GetValue(int32 at) { return int32(Host.Source[at]); }
                """);
            var lockedPath = lockedArtifact == "image" ? imagePath : manifestPath;
            using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var (failedExitCode, output, error) = await RunAsync([], [
                    "build-library", sourcePath, "--export", "SourceGrant.Pair.GetValue",
                    "-o", imagePath, "--runtime", "fail-fast", "--source-grant", "--abi-output", manifestPath,
                ]);
                Assert.That(failedExitCode, Is.EqualTo(2));
                Assert.That(output + error, Does.Contain("TRM2002"));
            }

            Assert.That(File.ReadAllBytes(imagePath), Is.EqualTo(originalImage));
            Assert.That(File.ReadAllBytes(manifestPath), Is.EqualTo(originalManifest));
        }
        finally
        {
            if (File.Exists(imagePath))
            {
                File.Delete(imagePath);
            }
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }
            if (File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }
            Directory.Delete(temporaryDirectory);
        }
    }
}
