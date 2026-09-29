// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Fact]
    public async Task ValidateTrRmStructureAcceptsMinimalArtifactAndRejectsCorruptionAsync()
    {
        var module = new byte[44];
        foreach (var offset in new[] { 0, 4, 28, 32, 36, 40 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(offset), 1);
        }
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0)]);
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, module),
            new TrRmSection(0x0002, 0, 1, strings),
        ]);
        var path = Path.Combine(Path.GetTempPath(), $"terrum-trrm-structure-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(path, artifact);
            var (exit, output, error) = await RunAsync([], "validate-trrm-structure", path, "--json");
            Assert.Equal(0, exit);
            Assert.Empty(error);
            using (var result = JsonDocument.Parse(output))
            {
                Assert.True(result.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("framing-and-structural-metadata",
                    result.RootElement.GetProperty("scope").GetString());
            }

            var stringStart = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(48 + 8)));
            artifact[stringStart + strings.Length - 1] = 0xFF;
            await File.WriteAllBytesAsync(path, artifact);
            var (invalidExit, invalidOutput, invalidError) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.Equal(1, invalidExit);
            Assert.Empty(invalidError);
            using var invalid = JsonDocument.Parse(invalidOutput);
            Assert.False(invalid.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("TRM5001", invalid.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ValidateTrRmStructureReportsMissingInputAndUsageAsync()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"terrum-trrm-missing-{Guid.NewGuid():N}.trrm");
        var (missingExit, missingOutput, _) = await RunAsync([],
            "validate-trrm-structure", missing, "--json");
        Assert.Equal(2, missingExit);
        using (var result = JsonDocument.Parse(missingOutput))
        {
            Assert.Equal("TRM2001", result.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString());
        }
        var (usageExit, usageOutput, _) = await RunAsync([],
            "validate-trrm-structure", "-", "--json");
        Assert.Equal(2, usageExit);
        using var usage = JsonDocument.Parse(usageOutput);
        Assert.Equal("TRM2000", usage.RootElement.GetProperty("diagnostics")[0]
            .GetProperty("code").GetString());
    }

    [Fact]
    public async Task ValidateTrRmCompactLinearChecksBodiesWithoutChangingStructuralValidationAsync()
    {
        var module = new byte[44];
        foreach (var offset in new[] { 0, 4, 28, 32, 36, 40 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(offset), 1);
        }

        var strings = TrRmStringTableWriter.EncodeNames(
            [("Module", 0), ("Run", 0), ("src/file", 0)]);
        byte[] typeSpec = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        var signature = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(signature, 1);
        var method = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(24), 1);
        byte[] body =
        [
            0, 0, 0, 0, 13, 0, 0, 0,
            0x04, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1,
        ];
        var sourceFile = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(sourceFile, 3);
        var sourceMap = new byte[52];
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(sourceMap.AsSpan(4), 0x0020);
        BinaryPrimitives.WriteUInt16LittleEndian(sourceMap.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(sourceMap.AsSpan(20), 0x001F);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(36), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(40), 2);
        byte[] Encode() => TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, module),
            new TrRmSection(0x0002, 0, 3, strings),
            new TrRmSection(0x0011, 0, 1, typeSpec),
            new TrRmSection(0x0016, 0, 1, method),
            new TrRmSection(0x0017, 0, 1, signature),
            new TrRmSection(0x001F, 0, 1, sourceFile),
            new TrRmSection(0x0020, 0, 1, body),
            new TrRmSection(0x7F00, 2, 1, sourceMap),
        ]);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-trrm-compact-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(path, Encode());
            var (exit, output, error) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.Equal(0, exit);
            Assert.Empty(error);
            using (var result = JsonDocument.Parse(output))
            {
                Assert.True(result.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("framing-structural-metadata-and-compact-linear-shapes",
                    result.RootElement.GetProperty("scope").GetString());
            }

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 0);
            await File.WriteAllBytesAsync(path, Encode());
            var (operationExit, _, _) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.Equal(0, operationExit);

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 2);
            await File.WriteAllBytesAsync(path, Encode());
            var (structuralMapExit, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.Equal(0, structuralMapExit);
            var (invalidMapExit, _, _) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.Equal(1, invalidMapExit);

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(12), 2);
            await File.WriteAllBytesAsync(path, Encode());
            var (invalidBlockExit, _, _) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.Equal(1, invalidBlockExit);

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(12), 1);
            body[8] = 0x08;
            await File.WriteAllBytesAsync(path, Encode());
            var (structuralExit, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.Equal(0, structuralExit);
            var (invalidExit, invalidOutput, invalidError) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.Equal(1, invalidExit);
            Assert.Empty(invalidError);
            using var invalid = JsonDocument.Parse(invalidOutput);
            Assert.Equal("TRM5001", invalid.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
