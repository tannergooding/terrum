// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmArtifactWriterTests
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

        Assert.That(artifact.Length, Is.EqualTo(128));
        Assert.That(artifact[..4], Is.EqualTo("TRRM"u8.ToArray()));
        Assert.That(Read(artifact, 32 + 8), Is.EqualTo((uint)64));
        Assert.That(Read(artifact, 32 + 12), Is.EqualTo((uint)48));
        Assert.That(Read(artifact, 48 + 8), Is.EqualTo((uint)112));
        Assert.That(Read(artifact, 48 + 12), Is.EqualTo((uint)strings.Length));
        TrRmStructuralMetadataValidator.Validate(artifact);

        module[0] = 0;
        strings[12] = 0;
        TrRmStructuralMetadataValidator.Validate(artifact);
    }

    [Test]
    public void A3ArtifactWriterChargesFramingAndOutputAtTheExactWorkBoundary()
    {
        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var artifact = TrRmArtifactWriter.Encode([
                new TrRmSection(0x0001, 0, 1, Module()),
            ],
                TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft,
                ledger);
            Assert.That(artifact.Length, Is.EqualTo(96));
        });
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
        _ = Assert.Throws<InvalidDataException>(() => TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, Module()),
            new TrRmSection(0x0002, 0, 1, TrRmStringTableWriter.EncodeNames([("A", 0)])),
            new TrRmSection(0x8000, 2, 1, extension),
        ]));
    }

    [Test]
    public void EncodesCanonicalExactFeatureAssignments()
    {
        var features = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.LayoutQueries,
            TrRmFeatureRow.NativeBoundaryContracts,
            TrRmFeatureRow.NativeBoundaryContracts,
        ]);
        Assert.That(features.Length, Is.EqualTo(16));
        Assert.That(Read(features, 0), Is.EqualTo(TrRmFeatureTableReader.NativeBoundaryContractsFeatureId));
        Assert.That(Read(features, sizeof(uint)), Is.EqualTo(TrRmFeatureTableReader.RuleRevision));
        Assert.That(Read(features, 8), Is.EqualTo(TrRmFeatureTableReader.LayoutQueriesFeatureId));
        Assert.That(Read(features, 12), Is.EqualTo(TrRmFeatureTableReader.RuleRevision));
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0)]);
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, Module()),
            new TrRmSection(0x0002, 0, 1, strings),
            new TrRmSection(0x0004, 0, 2, features),
        ]);
        Assert.That(new TrRmArtifactIndex(artifact).TryGetSection(0x0004, out _, out _), Is.True);
        _ = Assert.Throws<InvalidDataException>(() => TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, Module()),
            new TrRmSection(0x0002, 0, 1, strings),
            new TrRmSection(0x0004, 1, 2, features),
        ]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmFeatureTableWriter.Encode([new TrRmFeatureRow(8, 1)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmFeatureTableWriter.Encode([new TrRmFeatureRow(0, 1)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmFeatureTableWriter.Encode([new TrRmFeatureRow(1, 0)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmFeatureTableWriter.Encode([new TrRmFeatureRow(1,
                TrRmFeatureTableReader.RuleRevision + 1)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmFeatureTableWriter.Encode([
                new TrRmFeatureRow(1, 1), new TrRmFeatureRow(1, 2),
            ]));
    }

    [Test]
    public void RejectsInvalidOrderCountsAndDirectoryPolicies()
    {
        var module = new TrRmSection(0x0001, 0, 1, Module());
        var strings = new TrRmSection(0x0002, 0, 1,
            TrRmStringTableWriter.EncodeNames([("A", 0)]));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => TrRmArtifactWriter.Encode([]));
        _ = Assert.Throws<ArgumentException>(() => TrRmArtifactWriter.Encode([strings, module]));
        _ = Assert.Throws<ArgumentException>(() => TrRmArtifactWriter.Encode([module, module]));
        _ = Assert.Throws<ArgumentException>(() => TrRmArtifactWriter.Encode([
            module with { RecordCount = 0 }, strings,
        ]));
        _ = Assert.Throws<ArgumentException>(() => TrRmArtifactWriter.Encode([
            module with { Bytes = ReadOnlyMemory<byte>.Empty }, strings,
        ]));
        _ = Assert.Throws<InvalidDataException>(() => TrRmArtifactWriter.Encode([strings]));
        _ = Assert.Throws<InvalidDataException>(() => TrRmArtifactWriter.Encode([
            module, strings with { Flags = 1 },
        ]));
    }

    private static byte[] Module()
    {
        var row = new byte[48];
        Write(row, 0, 1);
        Write(row, 4, 1);
        Write(row, 32, 1);
        Write(row, 36, 1);
        Write(row, 40, 1);
        Write(row, 44, 1);
        return row;
    }

    private static uint Read(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteU16(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
}
