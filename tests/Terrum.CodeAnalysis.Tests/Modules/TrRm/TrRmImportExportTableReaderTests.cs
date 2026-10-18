// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmImportExportTableReaderTests
{
    [Test]
    public void RetainedExpectationsAndPublishedDefinitionsUseDistinctModules()
    {
        Assert.That(Read(Import(), Export(0x1600_0001)), Is.True);
        Assert.That(Read(Import(), Export(0x1300_0001)), Is.True);
        Assert.That(Read(Import(), Export(0x1300_0001, 0x1300_0002)), Is.True);
        var sharedModuleRef = new byte[24];
        Import(0x1300_0002).CopyTo(sharedModuleRef, 0);
        Import().CopyTo(sharedModuleRef, 12);
        Write(sharedModuleRef, 16, 8);
        Assert.That(Read(sharedModuleRef, [], additionalExpected: 0x1300_0002), Is.True);
        var twoProducers = Export(0x1300_0001, 0x1300_0002);
        Write(twoProducers, 12, 8);
        Assert.That(Read(Import(), twoProducers), Is.True);
        Assert.That(Read(Import(0x1300_0002), Export(0x1300_0001)), Is.False);
        Assert.That(Read([], Export(0x1600_0001)), Is.False);
    }

    [Test]
    public void RejectsInvalidImportedExpectationsAndDuplicateExpectedDefinitions()
    {
        foreach (var reference in new uint[] { 0, 1, 0x1400_0001, 0x1600_0001, 0x1600_0003 })
        {
            var import = Import();
            Write(import, 8, reference);
            Assert.Throws<InvalidDataException>(() => Read(import, []));
        }

        Assert.Throws<InvalidDataException>(() =>
            Read(Import(0x1600_0001), [], producerOwnsRef: true));
        var duplicate = new byte[24];
        Import().CopyTo(duplicate, 0);
        Import().CopyTo(duplicate, 12);
        Assert.Throws<InvalidDataException>(() => Read(duplicate, []));

        var missingModuleRef = Import();
        Write(missingModuleRef, 0, 0);
        Assert.Throws<InvalidDataException>(() => Read(missingModuleRef, []));
        var missingKey = Import();
        Write(missingKey, 4, 1);
        Assert.Throws<InvalidDataException>(() => Read(missingKey, []));
    }

    [Test]
    public void RejectsUnpublishedVisibilityImportedExportsAndDuplicateKeys()
    {
        Assert.Throws<InvalidDataException>(() => Read(Import(), Export(0x1600_0002)));
        Assert.Throws<InvalidDataException>(() => Read(Import(), Export(0x1600_0001),
            producerVisibility: 1));
        Assert.Throws<InvalidDataException>(() => Read(Import(), Export(0x1300_0001),
            producerTypeVisibility: 1));
        Assert.Throws<InvalidDataException>(() => Read(Import(), Export(0x1600_0001),
            producerTypeVisibility: 1, producerMethodOwned: true));
        Assert.That(Read(Import(), Export(0x1600_0001), producerMethodOwned: true), Is.True);
        Assert.Throws<InvalidDataException>(() =>
            Read(Import(), Export(0x1300_0001, 0x1600_0001)));
        var sameTextDifferentEncoding = Export(0x1300_0001, 0x1600_0001);
        Write(sameTextDifferentEncoding, 12, 8);
        Assert.Throws<InvalidDataException>(() => Read(Import(), sameTextDifferentEncoding));
        Assert.Throws<InvalidDataException>(() => Read(Import(), Export(0x1500_0001)));
        Assert.Throws<InvalidDataException>(() => Read(Import(), Export(0)));
        var absentKey = Export(0x1600_0001);
        Write(absentKey, 4, 0);
        Assert.Throws<InvalidDataException>(() => Read(Import(), absentKey));
        Assert.Throws<InvalidDataException>(() => Read(Import()[..^1], []));
    }

    [Test]
    public void PropertyExportRequiresPublicPropertyAndOwner()
    {
        Assert.That(Read(Import(), Export(0x1E00_0001), propertyFlags: 2), Is.True);
        Assert.Throws<InvalidDataException>(() =>
            Read(Import(), Export(0x1E00_0001), propertyFlags: 1));
        Assert.Throws<InvalidDataException>(() =>
            Read(Import(), Export(0x1E00_0001), propertyFlags: 2,
                producerTypeVisibility: 1));
    }

    [Test]
    public void PackedImportAndExportTablesComposeThroughTheArtifactValidator()
    {
        Assert.That(Read(Import(), Export(0x1600_0001), frameArtifact: true), Is.True);
        Assert.That(Read(Import(), Export(0x1E00_0001),
            propertyFlags: 2, frameArtifact: true), Is.True);
    }

    [Test]
    public void WrittenRetainedImportAndPublicExportComposeThroughTheArtifactValidator()
    {
        Assert.That(Read(Import(), Export(0x1600_0001),
            frameArtifact: true, writeRows: true), Is.True);
        Assert.That(Read(Import(), Export(0x1E00_0001),
            propertyFlags: 2, frameArtifact: true, writeRows: true), Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Read(Import(), [], writeRows: true));
        Assert.Throws<InvalidDataException>(() =>
            Read(Import(0x1600_0001), Export(0x1300_0001), writeRows: true));
        Assert.Throws<InvalidDataException>(() =>
            Read(Import(), Export(0x1600_0002), writeRows: true));
        Assert.Throws<InvalidDataException>(() =>
            Read(Import(), Export(0x1600_0001, 0x1300_0001), writeRows: true));
        Assert.Throws<InvalidDataException>(() =>
            Read(Import(), Export(0x1300_0001), producerTypeVisibility: 1, writeRows: true));
    }

    private static bool Read(byte[] importRows, byte[] exportRows,
        uint producerVisibility = 2, uint producerTypeVisibility = 2, uint propertyFlags = 0,
        bool producerOwnsRef = false, bool producerMethodOwned = false,
        bool frameArtifact = false, bool writeRows = false, uint? additionalExpected = null)
    {
        var nameBytes = TrRmStringTableWriter.EncodeTextRows(
            [("", 0), ("Producer", 0), ("Consumer", 0), ("Widget", 0),
             ("Key", 0), ("Execute", 0), ("Value", 0), ("Key", 1)]);
        var names = new TrRmStringTableReader(nameBytes, 8);
        var moduleRows = Modules(producerOwnsRef);
        var modules = new TrRmModuleTableReader(moduleRows, 2, names, 1, 2, 2, 0);
        var typeRows = new byte[80];
        for (var index = 0; index < 2; index++)
        {
            var offset = index * 40;
            Write(typeRows, offset, 1);
            Write(typeRows, offset + 4, 4);
            Write(typeRows, offset + 8, index == 0 ? 4u | producerTypeVisibility : 6u);
        }
        var types = new TrRmTypeDefTableReader(typeRows, 2, names, modules, 0, 0, 0, 0);
        byte[] typeSpecBytes = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1, 2, 0, 0, 0);
        var signatureRows = new byte[24];
        Write(signatureRows, 0, 1);
        var signatures = new TrRmMethodSigTableReader(signatureRows, 1, typeSpecs, 0, 0);
        var methodRows = new byte[56];
        Write(methodRows, 0, propertyFlags != 0 || producerMethodOwned ? 1u : 0u);
        Write(methodRows, 4, 6);
        Write(methodRows, 8, 1);
        Write(methodRows, 12, producerVisibility);
        Write(methodRows, 24, 1);
        Write(methodRows, 32, 6);
        Write(methodRows, 36, 1);
        Write(methodRows, 40, 2);
        var methods = new TrRmMethodDefTableReader(methodRows, 2, names,
            modules, types, signatures, 0, 1);
        var propertyRows = new byte[20];
        Write(propertyRows, 0, 1);
        Write(propertyRows, 4, 7);
        Write(propertyRows, 8, 1);
        Write(propertyRows, 12, 1);
        Write(propertyRows, 16, propertyFlags);
        var properties = propertyFlags == 0
            ? default
            : new TrRmPropertyDefTableReader(propertyRows, 1,
                names, types, typeSpecs, methods, signatures);
        var moduleRefRows = ModuleRef();
        var references = new TrRmModuleRefTableReader(moduleRefRows, 1, names, modules);
        if (writeRows)
        {
            var row = new TrRmImportRow(
                (int)BinaryPrimitives.ReadUInt32LittleEndian(importRows),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(importRows.AsSpan(4)),
                BinaryPrimitives.ReadUInt32LittleEndian(importRows.AsSpan(8)));
            importRows = TrRmImportTableWriter.Encode(
                [row], names, references, types, methods, properties);
        }
        var imports = new TrRmImportTableReader(importRows, importRows.Length / 12,
            names, references, types, methods, properties);
        if (writeRows)
        {
            var rows = new TrRmExportRow[exportRows.Length / 8];
            for (var index = 0; index < rows.Length; index++)
            {
                var offset = index * 8;
                rows[index] = new TrRmExportRow(
                    BinaryPrimitives.ReadUInt32LittleEndian(exportRows.AsSpan(offset)),
                    (int)BinaryPrimitives.ReadUInt32LittleEndian(exportRows.AsSpan(offset + 4)));
            }

            exportRows = TrRmExportTableWriter.Encode(
                rows, names, types, methods, properties, imports);
        }
        _ = new TrRmExportTableReader(exportRows, exportRows.Length / 8,
            names, types, methods, properties, imports);
        if (frameArtifact)
        {
            List<(ushort Kind, byte[] Bytes, int Count)> sections = [
                (0x0001, moduleRows, 2), (0x0002, nameBytes, 8),
                (0x0010, moduleRefRows, 1), (0x0011, typeSpecBytes, 1),
                (0x0013, typeRows, 2), (0x0016, methodRows, 2),
                (0x0017, signatureRows, 1),
                (0x001C, importRows, importRows.Length / 12),
                (0x001D, exportRows, exportRows.Length / 8),
            ];
            if (propertyFlags != 0)
            {
                sections.Add((0x001E, propertyRows, 1));
            }

            // This fixture checks Body framing and ownership, not executable instructions.
            byte[] body = [0, 0, 0, 0, 1, 0, 0, 0, 0];
            sections.Add((0x0020, body, 1));
            TrRmStructuralMetadataValidator.Validate(Artifact(sections));
        }

        return imports.IsExpectedDefinition(0x1600_0002)
            && (additionalExpected is null
                || imports.IsExpectedDefinition(additionalExpected.Value));
    }

    private static byte[] Artifact(List<(ushort Kind, byte[] Bytes, int Count)> sections)
    {
        var entries = new TrRmSection[sections.Count];
        for (var index = 0; index < sections.Count; index++)
        {
            var (kind, bytes, count) = sections[index];
            entries[index] = new TrRmSection(kind, 0, (uint)count, bytes);
        }

        return TrRmArtifactWriter.Encode(entries);
    }

    private static byte[] Modules(bool producerOwnsRef)
    {
        var rows = new byte[88];
        Write(rows, 0, 2);
        Write(rows, 4, 1);
        Write(rows, 28, 1);
        Write(rows, 32, 1);
        Write(rows, 36, 1);
        Write(rows, 40, 1);
        Write(rows, 44, 3);
        Write(rows, 48, 1);
        Write(rows, 72, producerOwnsRef ? 2u : 1u);
        Write(rows, 76, 2);
        Write(rows, 80, 2);
        Write(rows, 84, 1);
        return rows;
    }

    private static byte[] ModuleRef()
    {
        var row = new byte[12];
        Write(row, 0, 2);
        Write(row, 4, 1);
        return row;
    }

    private static byte[] Import(uint definition = 0x1600_0002)
    {
        var row = new byte[12];
        Write(row, 0, 1);
        Write(row, 4, 5);
        Write(row, 8, definition);
        return row;
    }

    private static byte[] Export(params uint[] definitions)
    {
        var rows = new byte[definitions.Length * 8];
        for (var index = 0; index < definitions.Length; index++)
        {
            Write(rows, index * 8, definitions[index]);
            Write(rows, index * 8 + 4, 5);
        }

        return rows;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
