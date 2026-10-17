// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("main.terrum")]
    [TestCase("main-hardware.terrum")]
    [TestCase("window-device-pipeline-stage.terrum")]
    [TestCase("window-device-hardware-pipeline-stage.terrum")]
    [TestCase("window-device-warp-resize-stage.terrum")]
    [TestCase("window-device-hardware-resize-stage.terrum")]
    [TestCase("window-device-warp-resize-mapped-pixel-evidence.terrum")]
    [TestCase("window-device-hardware-resize-mapped-pixel-evidence.terrum")]
    public async Task BuildNativeImportCompilesDirectDx11PipelineWithoutPrivateImportsAsync(string sourceName)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"terrum-dx11-{Guid.NewGuid():N}.exe");
        try
        {
            await BuildDx11PipelineAsync(sourceName, outputPath);
            var image = await File.ReadAllBytesAsync(outputPath);
            var importedLibraries = ReadImportedLibraries(image);
            Assert.That(importedLibraries, Is.Not.Empty);
            var allowedLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "kernel32.dll",
                "user32.dll",
                "ntdll.dll",
                Path.Combine(Environment.SystemDirectory, "user32.dll"),
            };
            Assert.That(importedLibraries, Has.None.Matches(library =>
                !allowedLibraries.Contains(library)));
            Assert.That(image.AsSpan().IndexOf("D3D11CreateDeviceAndSwapChain\0"u8) < 0, Is.True);
            Assert.That(image.AsSpan().IndexOf("d3d11.dll\0"u8) < 0, Is.True);
            Assert.That(image.AsSpan().IndexOf("D3DCompiler_47.dll\0"u8) < 0, Is.True);
            Assert.That(image.AsSpan().IndexOf("D3DCompileFromFile\0"u8) < 0, Is.True);
            Assert.That(image.AsSpan().IndexOf("LoadLibraryExW\0"u8) >= 0, Is.True);
            Assert.That(image.AsSpan().IndexOf("GetSystemDirectoryW\0"u8) >= 0, Is.True);
            Assert.That(image.AsSpan().IndexOf("GetProcAddress\0"u8) >= 0, Is.True);
            Assert.That(image.AsSpan().IndexOf("FreeLibrary\0"u8) >= 0, Is.True);
            Assert.That(image.AsSpan().IndexOf("GetModuleFileNameW\0"u8) >= 0, Is.True);
            Assert.That(image.AsSpan().IndexOf("GetLastError\0"u8) >= 0, Is.True);
            Assert.That(image.AsSpan().IndexOf("NativeFfi"u8) < 0, Is.True);
            Assert.That(image.AsSpan().IndexOf("PostMessageW\0"u8) >= 0, Is.EqualTo(sourceName is "main.terrum" or "main-hardware.terrum"
                or "window-device-warp-resize-stage.terrum" or "window-device-hardware-resize-stage.terrum"));
            if (sourceName is "main.terrum" or "main-hardware.terrum"
                || sourceName.Contains("-resize-stage", StringComparison.Ordinal)
                || sourceName.Contains("-resize-mapped-pixel-evidence", StringComparison.Ordinal))
            {
                Assert.That(image.AsSpan().IndexOf("PeekMessageW\0"u8) >= 0, Is.True);
            }
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private static string[] ReadImportedLibraries(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var header = pe.PEHeaders.PEHeader
            ?? throw new InvalidDataException("Missing PE optional header.");
        var directory = header.ImportTableDirectory;
        if (directory.RelativeVirtualAddress == 0 || directory.Size == 0)
        {
            throw new InvalidDataException("Missing PE import table.");
        }
        if (directory.Size % 20 != 0)
        {
            throw new InvalidDataException("Malformed PE import table size.");
        }

        var descriptors = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader();
        var libraries = new List<string>();
        for (var index = 0; index < directory.Size / 20; index++)
        {
            var lookup = descriptors.ReadInt32();
            var timestamp = descriptors.ReadInt32();
            var forwarderChain = descriptors.ReadInt32();
            var nameRva = descriptors.ReadInt32();
            var address = descriptors.ReadInt32();
            if (lookup == 0 && timestamp == 0 && forwarderChain == 0 && nameRva == 0 && address == 0)
            {
                return [.. libraries];
            }
            if (lookup == 0 || nameRva == 0)
            {
                throw new InvalidDataException("Malformed PE import descriptor.");
            }

            var nameReader = pe.GetSectionData(nameRva).GetReader();
            var name = new StringBuilder();
            for (var value = nameReader.ReadByte(); value != 0; value = nameReader.ReadByte())
            {
                _ = name.Append((char)value);
            }
            libraries.Add(name.ToString());
        }
        throw new InvalidDataException("Missing PE import-table terminator.");
    }

    [TestCase("main.terrum")]
    [TestCase("main-hardware.terrum")]
    public async Task BuildNativeImportRejectsFailFastDx11SampleAsync(string sourceName)
    {
        var source = Path.Combine(RepositoryRoot(), "samples", "design", "win32-dx11-triangle", sourceName);
        var outputPath = Path.Combine(Path.GetTempPath(), $"terrum-dx11-fail-fast-{Guid.NewGuid():N}.exe");
        try
        {
            var (exitCode, output, error) = await RunAsync([], "build-native-import", source,
                "-o", outputPath, "--runtime", "fail-fast", "--json");
            Assert.That(exitCode, Is.Not.EqualTo(0));
            Assert.That(error, Is.Empty);
            using var result = JsonDocument.Parse(output);
            Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(result.RootElement.GetProperty("diagnostics").EnumerateArray(), Has.Some.Matches(static diagnostic => diagnostic.GetProperty("code").GetString() == "TRM3030"));
            Assert.That(File.Exists(outputPath), Is.False);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public async Task BuildNativeImportDx11SampleRunsWarpResizeAndClosesNormallyAsync()
    {
        var directory = Directory.CreateTempSubdirectory("terrum-dx11-sample-").FullName;
        try
        {
            var executable = Path.Combine(directory, "triangle.exe");
            await BuildDx11PipelineAsync("main.terrum", executable);
            File.Copy(Path.Combine(RepositoryRoot(), "samples", "design", "win32-dx11-triangle", "triangle.hlsl"),
                Path.Combine(directory, "triangle.hlsl"));
            Assert.That(Directory.EnumerateFiles(directory, "*.dll"), Is.Empty);
            await NativeSourceImportTests.AssertDx11ResizeApplicationRunsAsync(
                executable, RepositoryRoot(), "WARP sample");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static async Task BuildDx11PipelineAsync(string sourceName, string outputPath)
    {
        var source = Path.Combine(RepositoryRoot(), "samples", "design", "win32-dx11-triangle", sourceName);
        var (exitCode, output, error) = await RunAsync([], "build-native-import", source,
            "-o", outputPath, "--runtime", "checked-exceptions", "--json");
        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using var result = JsonDocument.Parse(output);
        Assert.That(result.RootElement.GetProperty("command").GetString(), Is.EqualTo("build-native-import"));
        Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.True);
    }
}
