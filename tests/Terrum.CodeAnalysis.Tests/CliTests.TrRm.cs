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
}
