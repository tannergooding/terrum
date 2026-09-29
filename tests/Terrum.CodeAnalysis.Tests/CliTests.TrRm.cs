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

    [Fact]
    public async Task ValidateTrRmCompactIndexedChecksBodiesAndSourceLocationsAsync()
    {
        var module = new byte[44];
        foreach (var offset in new[] { 0, 4, 28, 32, 36, 40 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(offset), 1);
        }

        var strings = TrRmStringTableWriter.EncodeNames(
            [("Module", 0), ("Run", 0), ("src/file", 0)]);
        byte[] typeSpec =
        [
            0, 0, 0, 0, 3, 0, 0, 0, 6, 0, 0, 0,
            1, 0, 1, 1, 0, 8,
        ];
        var signature = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(signature, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(12), 3);
        var parameters = new byte[48];
        for (var index = 0; index < 3; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(index * 16), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(index * 16 + 4), (uint)index);
            BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(index * 16 + 8),
                index == 0 ? 1u : 2u);
        }

        var method = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(24), 1);
        var body = TrRmBodyExampleTests.ReadExample("#### Compact indexed conditional sum");
        var sourceFile = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(sourceFile, 3);
        var sourceMap = new byte[52];
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(sourceMap.AsSpan(4), 0x0020);
        BinaryPrimitives.WriteUInt16LittleEndian(sourceMap.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(sourceMap.AsSpan(20), 0x001F);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(36), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(40), 2);
        byte[] Encode(byte[]? methodRows = null, int methodCount = 1,
            byte[]? bodyRows = null, int bodyCount = 1,
            byte[]? mapRows = null, int mapCount = 1) => TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, module),
            new TrRmSection(0x0002, 0, 3, strings),
            new TrRmSection(0x0011, 0, 2, typeSpec),
            new TrRmSection(0x0016, 0, (uint)methodCount, methodRows ?? method),
            new TrRmSection(0x0017, 0, 1, signature),
            new TrRmSection(0x0018, 0, 3, parameters),
            new TrRmSection(0x001F, 0, 1, sourceFile),
            new TrRmSection(0x0020, 0, (uint)bodyCount, bodyRows ?? body),
            new TrRmSection(0x7F00, 2, (uint)mapCount, mapRows ?? sourceMap),
        ]);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-trrm-indexed-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(path, Encode());
            var (exit, output, error) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.Equal(0, exit);
            Assert.Empty(error);
            using (var result = JsonDocument.Parse(output))
            {
                Assert.True(result.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("framing-structural-metadata-and-compact-indexed-shapes",
                    result.RootElement.GetProperty("scope").GetString());
            }

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 2);
            await File.WriteAllBytesAsync(path, Encode());
            var (structuralExit, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.Equal(0, structuralExit);
            var (invalidMapExit, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.Equal(1, invalidMapExit);

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(12), 4);
            await File.WriteAllBytesAsync(path, Encode());
            var (invalidBlockExit, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.Equal(1, invalidBlockExit);

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(12), 2);
            body[77] = 4;
            await File.WriteAllBytesAsync(path, Encode());
            var (opaqueExit, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.Equal(0, opaqueExit);
            var (invalidBodyExit, invalidOutput, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.Equal(1, invalidBodyExit);
            using var invalid = JsonDocument.Parse(invalidOutput);
            Assert.Equal("TRM5001", invalid.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString());

            body[77] = 2;
            var methodRows = new byte[56];
            method.CopyTo(methodRows, 0);
            method.CopyTo(methodRows, 28);
            BinaryPrimitives.WriteUInt32LittleEndian(methodRows.AsSpan(52), 2);
            byte[] linearRow =
            [
                0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 0, 0x83, 2,
            ];
            var bodyRows = new byte[12 + body.Length - 8 + linearRow.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(bodyRows.AsSpan(4), (uint)(body.Length - 8));
            BinaryPrimitives.WriteUInt32LittleEndian(bodyRows.AsSpan(8),
                (uint)(body.Length - 8 + linearRow.Length));
            body.AsSpan(8).CopyTo(bodyRows.AsSpan(12));
            linearRow.CopyTo(bodyRows, 12 + body.Length - 8);
            var mixedMap = new byte[84];
            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap, 3);
            sourceMap.AsSpan(4, 32).CopyTo(mixedMap.AsSpan(4));
            BinaryPrimitives.WriteUInt16LittleEndian(mixedMap.AsSpan(36), 0x0020);
            BinaryPrimitives.WriteUInt16LittleEndian(mixedMap.AsSpan(38), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap.AsSpan(40), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap.AsSpan(44), 1);
            sourceMap.AsSpan(36, 16).CopyTo(mixedMap.AsSpan(52));
            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap.AsSpan(68), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap.AsSpan(72), 2);
            await File.WriteAllBytesAsync(path, Encode(methodRows, 2, bodyRows, 2, mixedMap, 2));
            var (mixedExit, mixedOutput, _) = await RunAsync([],
                "validate-trrm-compact", path, "--json");
            Assert.Equal(0, mixedExit);
            using (var result = JsonDocument.Parse(mixedOutput))
            {
                Assert.Equal("framing-structural-metadata-and-compact-shapes",
                    result.RootElement.GetProperty("scope").GetString());
            }

            var (indexedOnlyExit, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            var (linearOnlyExit, _, _) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.Equal(1, indexedOnlyExit);
            Assert.Equal(1, linearOnlyExit);

            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap.AsSpan(48), 1);
            await File.WriteAllBytesAsync(path, Encode(methodRows, 2, bodyRows, 2, mixedMap, 2));
            var (invalidLinearLocationExit, _, _) = await RunAsync([],
                "validate-trrm-compact", path, "--json");
            Assert.Equal(1, invalidLinearLocationExit);

            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap.AsSpan(48), 0);
            bodyRows[12 + body.Length - 8] = 0;
            await File.WriteAllBytesAsync(path, Encode(methodRows, 2, bodyRows, 2, mixedMap, 2));
            var (unsupportedExit, _, _) = await RunAsync([],
                "validate-trrm-compact", path, "--json");
            Assert.Equal(1, unsupportedExit);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
