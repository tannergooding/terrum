// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmArtifactWriterTests
{
    [Test]
    public void FramesAndOwnsTheWorkedMinimalModuleAndStringSections()
    {
        var module = Module();
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0)]);
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, module),
            new TrRmSection(0x0002, 0, 1, strings),
        ]);

        Assert.That(artifact.Length, Is.EqualTo(124));
        Assert.That(artifact[..4], Is.EqualTo("TRRM"u8.ToArray()));
        Assert.That(Read(artifact, 32 + 8), Is.EqualTo((uint)64));
        Assert.That(Read(artifact, 32 + 12), Is.EqualTo((uint)44));
        Assert.That(Read(artifact, 48 + 8), Is.EqualTo((uint)108));
        Assert.That(Read(artifact, 48 + 12), Is.EqualTo((uint)strings.Length));
        TrRmStructuralMetadataValidator.Validate(artifact);

        module[0] = 0;
        strings[12] = 0;
        TrRmStructuralMetadataValidator.Validate(artifact);
    }

    [Test]
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

    [Test]
    public void EncodesOnlyTheSupportedProvisionalFeatureAssignment()
    {
        var features = TrRmFeatureTableWriter.Encode([TrRmFeatureRow.ScalarInitOutput]);
        Assert.That(features.Length, Is.EqualTo(8));
        Assert.That(Read(features, 0), Is.EqualTo(TrRmFeatureTableReader.ScalarInitOutputFeatureId));
        Assert.That(Read(features, sizeof(uint)), Is.EqualTo(TrRmFeatureTableReader.ScalarInitOutputRuleRevision));
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0)]);
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, Module()),
            new TrRmSection(0x0002, 0, 1, strings),
            new TrRmSection(0x0004, 0, 1, features),
        ]);
        Assert.That(new TrRmArtifactIndex(artifact).TryGetSection(0x0004, out _, out _), Is.True);
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

    [Test]
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
