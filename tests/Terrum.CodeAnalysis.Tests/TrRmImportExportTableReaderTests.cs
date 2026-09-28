// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmImportExportTableReaderTests
{
    [Fact]
    public void RetainedExpectationsAndPublishedDefinitionsUseDistinctModules()
    {
        Assert.True(Read(Import(), Export(0x1600_0001)));
        Assert.True(Read(Import(), Export(0x1300_0001)));
        Assert.True(Read(Import(), Export(0x1300_0001, 0x1300_0002)));
        var twoProducers = Export(0x1300_0001, 0x1300_0002);
        Write(twoProducers, 12, 8);
        Assert.True(Read(Import(), twoProducers));
        Assert.False(Read(Import(0x1300_0002), Export(0x1300_0001)));
        Assert.False(Read([], Export(0x1600_0001)));
    }

    [Fact]
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

    [Fact]
    public void RejectsUnpublishedVisibilityImportedExportsAndDuplicateKeys()
    {
        Assert.Throws<InvalidDataException>(() => Read(Import(), Export(0x1600_0002)));
        Assert.Throws<InvalidDataException>(() => Read(Import(), Export(0x1600_0001),
            producerVisibility: 1));
        Assert.Throws<InvalidDataException>(() => Read(Import(), Export(0x1300_0001),
            producerTypeVisibility: 1));
        Assert.Throws<InvalidDataException>(() => Read(Import(), Export(0x1600_0001),
            producerTypeVisibility: 1, producerMethodOwned: true));
        Assert.True(Read(Import(), Export(0x1600_0001), producerMethodOwned: true));
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

    [Fact]
    public void PropertyExportRequiresPublicPropertyAndOwner()
    {
        Assert.True(Read(Import(), Export(0x1E00_0001), propertyFlags: 2));
        Assert.Throws<InvalidDataException>(() =>
            Read(Import(), Export(0x1E00_0001), propertyFlags: 1));
        Assert.Throws<InvalidDataException>(() =>
            Read(Import(), Export(0x1E00_0001), propertyFlags: 2,
                producerTypeVisibility: 1));
    }

    private static bool Read(byte[] importRows, byte[] exportRows,
        uint producerVisibility = 2, uint producerTypeVisibility = 2, uint propertyFlags = 0,
        bool producerOwnsRef = false, bool producerMethodOwned = false)
    {
        var names = new TrRmStringTableReader(TrRmStringTableWriter.EncodeTextRows(
            [("", 0), ("Producer", 0), ("Consumer", 0), ("Widget", 0),
             ("Key", 0), ("Execute", 0), ("Value", 0), ("Key", 1)]), 8);
        var modules = new TrRmModuleTableReader(Modules(producerOwnsRef), 2, names, 1, 2, 2, 0);
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
        Write(propertyRows, 12, propertyFlags);
        Write(propertyRows, 16, 1);
        var properties = propertyFlags == 0
            ? default
            : new TrRmPropertyDefTableReader(propertyRows, 1,
                names, types, typeSpecs, methods, signatures);
        var references = new TrRmModuleRefTableReader(ModuleRef(), 1, names, modules);
        var imports = new TrRmImportTableReader(importRows, importRows.Length / 12,
            names, references, types, methods, properties);
        _ = new TrRmExportTableReader(exportRows, exportRows.Length / 8,
            names, types, methods, properties, imports);
        return imports.IsExpectedDefinition(0x1600_0002);
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
