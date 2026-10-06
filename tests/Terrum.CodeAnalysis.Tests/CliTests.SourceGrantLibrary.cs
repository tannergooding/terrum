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
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task BuildLibraryEmitsSourceGrantManifestFromSelectedExportsAsync(string runtime)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var sourceDirectory = Path.Combine(RepositoryRoot(), "self-host");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-cli-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(temporaryDirectory);
        var imagePath = Path.Combine(temporaryDirectory, "compiler-primitives.dll");
        var manifestPath = Path.Combine(temporaryDirectory, "compiler-primitives.abi.json");
        try
        {
            var (ExitCode, Output, Error) = await RunAsync([], [
                "build-library", Path.Combine(sourceDirectory, "Utf16CodeUnits.terrum"),
                "--source", Path.Combine(sourceDirectory, "Utf16Scalar.terrum"),
                "--export", "Terrum.CodeAnalysis.DecodeUtf16Scalar",
                "--export", "Terrum.CodeAnalysis.ReadUtf16ScalarChecked",
                "-o", imagePath, "--runtime", runtime,
                "--source-grant", "--abi-output", manifestPath,
            ]);
            Assert.Equal(0, ExitCode);
            Assert.Empty(Error);

            var image = File.ReadAllBytes(imagePath);
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var root = manifest.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(1, root.GetProperty("sourceGrantAbiVersion").GetInt32());
            Assert.Equal(16_384, root.GetProperty("maximumSourceCodeUnits").GetInt32());
            Assert.Equal(runtime, root.GetProperty("runtimePolicy").GetString());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(image)),
                root.GetProperty("imageSha256").GetString());

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
                Assert.Equal(0x1F600,
                    bindings.Invoke<int>("Terrum.CodeAnalysis.ReadUtf16ScalarChecked", ['\uD83D', '\uDE00'], 0, 2));
                Assert.Equal(0x1F600,
                    bindings.Invoke<int>("Terrum.CodeAnalysis.DecodeUtf16Scalar", null,
                        (ushort)'\uD83D', (ushort)'\uDE00'));
                Assert.True(Assert.Single(exports,
                    static export => export.Name == "Terrum.CodeAnalysis.ReadUtf16ScalarChecked").SourceDependent);
                Assert.False(Assert.Single(exports,
                    static export => export.Name == "Terrum.CodeAnalysis.DecodeUtf16Scalar").SourceDependent);
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

    [Theory]
    [InlineData("image")]
    [InlineData("manifest")]
    public async Task FailedSourceGrantArtifactReplacementPreservesTheExistingPairAsync(string lockedArtifact)
    {
        Assert.True(OperatingSystem.IsWindows());
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-pair-{Guid.NewGuid():N}");
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
            Assert.Equal(0, initialExitCode);
            Assert.Empty(initialError);
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
                Assert.Equal(2, failedExitCode);
                Assert.Contains("TRM2002", output + error, StringComparison.Ordinal);
            }

            Assert.Equal(originalImage, File.ReadAllBytes(imagePath));
            Assert.Equal(originalManifest, File.ReadAllBytes(manifestPath));
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
