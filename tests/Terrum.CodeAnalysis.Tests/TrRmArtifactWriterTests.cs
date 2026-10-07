// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmArtifactWriterTests
{
    [Fact]
    public void FramesAndOwnsTheWorkedMinimalModuleAndStringSections()
    {
        var module = Module();
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0)]);
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, module),
            new TrRmSection(0x0002, 0, 1, strings),
        ]);

        Assert.Equal(124, artifact.Length);
        Assert.Equal("TRRM"u8.ToArray(), artifact[..4]);
        Assert.Equal((uint)64, Read(artifact, 32 + 8));
        Assert.Equal((uint)44, Read(artifact, 32 + 12));
        Assert.Equal((uint)108, Read(artifact, 48 + 8));
        Assert.Equal((uint)strings.Length, Read(artifact, 48 + 12));
        TrRmStructuralMetadataValidator.Validate(artifact);

        module[0] = 0;
        strings[12] = 0;
        TrRmStructuralMetadataValidator.Validate(artifact);
    }

    [Fact]
    public void ValidatesOptionalReferenceSlotsAgainstTheCompletedDirectory()
    {
        var extension = new byte[21];
        Write(extension, 0, 1);
        WriteU16(extension, 4, 0x0002);
        Write(extension, 8, 1);
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, Module()),
            new TrRmSection(0x0002, 0, 1, TrRmStringTableWriter.EncodeNames([("A", 0)])),
            new TrRmSection(0x8000, 2, 1, extension),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);

        Write(extension, 8, 2);
        Assert.Throws<InvalidDataException>(() => TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, Module()),
            new TrRmSection(0x0002, 0, 1, TrRmStringTableWriter.EncodeNames([("A", 0)])),
            new TrRmSection(0x8000, 2, 1, extension),
        ]));
    }

    [Fact]
    public void EncodesOnlyTheSupportedProvisionalFeatureAssignment()
    {
        var features = TrRmFeatureTableWriter.Encode([TrRmFeatureRow.ScalarInitOutput]);
        Assert.Equal(8, features.Length);
        Assert.Equal(TrRmFeatureTableReader.ScalarInitOutputFeatureId, Read(features, 0));
        Assert.Equal(TrRmFeatureTableReader.ScalarInitOutputRuleRevision, Read(features, sizeof(uint)));
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0)]);
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, Module()),
            new TrRmSection(0x0002, 0, 1, strings),
            new TrRmSection(0x0004, 0, 1, features),
        ]);
        Assert.True(new TrRmArtifactIndex(artifact).TryGetSection(0x0004, out _, out _));
        Assert.Throws<InvalidDataException>(() => TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, Module()),
            new TrRmSection(0x0002, 0, 1, strings),
            new TrRmSection(0x0004, 1, 1, features),
        ]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmFeatureTableWriter.Encode([new TrRmFeatureRow(0, 1)]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmFeatureTableWriter.Encode([new TrRmFeatureRow(1, 0)]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmFeatureTableWriter.Encode([new TrRmFeatureRow(1,
                TrRmFeatureTableReader.ScalarInitOutputRuleRevision + 1)]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmFeatureTableWriter.Encode([
                TrRmFeatureRow.ScalarInitOutput, TrRmFeatureRow.ScalarInitOutput,
            ]));
    }

    [Fact]
    public void RejectsInvalidOrderCountsAndDirectoryPolicies()
    {
        var module = new TrRmSection(0x0001, 0, 1, Module());
        var strings = new TrRmSection(0x0002, 0, 1,
            TrRmStringTableWriter.EncodeNames([("A", 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrRmArtifactWriter.Encode([]));
        Assert.Throws<ArgumentException>(() => TrRmArtifactWriter.Encode([strings, module]));
        Assert.Throws<ArgumentException>(() => TrRmArtifactWriter.Encode([module, module]));
        Assert.Throws<ArgumentException>(() => TrRmArtifactWriter.Encode([
            module with { RecordCount = 0 }, strings,
        ]));
        Assert.Throws<ArgumentException>(() => TrRmArtifactWriter.Encode([
            module with { Bytes = ReadOnlyMemory<byte>.Empty }, strings,
        ]));
        Assert.Throws<InvalidDataException>(() => TrRmArtifactWriter.Encode([strings]));
        Assert.Throws<InvalidDataException>(() => TrRmArtifactWriter.Encode([
            module, strings with { Flags = 1 },
        ]));
    }

    private static byte[] Module()
    {
        var row = new byte[44];
        Write(row, 0, 1);
        Write(row, 4, 1);
        Write(row, 28, 1);
        Write(row, 32, 1);
        Write(row, 36, 1);
        Write(row, 40, 1);
        return row;
    }

    private static uint Read(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteU16(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
}
