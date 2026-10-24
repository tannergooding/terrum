// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmLayoutTableWriterTests
{
    [Test]
    public void EmitsDefaultAndBlittableTargetVariantsWithCompleteFieldLists()
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Value", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 4);
        var featureBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.NativeBoundaryContracts,
        ]);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, 1, 0, 0, 1);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 0, 0, 1);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 1, 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1, 1, 0, 0, 0);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 4, 1, 1, 0, 0, 1, 2, 0, 0),
        ], strings, modules, 1, 0, 2, 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 1, strings, modules,
            1, 0, 2, 0);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 4, 1, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 1, strings,
            typeDefs, typeSpecs);
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ]);
        var targetProfiles = new TrRmTargetProfileTableReader(targetProfileBytes, 1);
        var layoutRows = new[]
        {
            new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 1, 1),
            new TrRmLayoutRow(1, 1, 1, 1, 0, 16, 2, 1),
        };
        var layoutBytes = TrRmLayoutTableWriter.Encode(
            layoutRows, typeDefs, targetProfiles, 2);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 2,
            typeDefs, targetProfiles, 2);
        var layoutFieldRows = new[]
        {
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(2, 0, 1),
        };
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, typeDefs, fields);
        var layoutFields = new TrRmLayoutFieldTableReader(layoutFieldBytes, 2,
            layouts, typeDefs, fields);

        Assert.That(layoutBytes.Length, Is.EqualTo(56));
        Assert.That(layoutFields.GetFieldDef(2), Is.EqualTo(1));
        Assert.That(layoutFields.GetOrdinal(2), Is.Zero);
        Assert.That(layouts.GetLayoutForProfile(1, 0), Is.EqualTo(1));
        Assert.That(layouts.GetLayoutForProfile(1, 1), Is.EqualTo(2));
        Assert.That(layouts.HasBlittableLayout(1, 1, 1), Is.True);

        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 4, stringsBytes),
            new TrRmSection(0x0004, 0, 1, featureBytes),
            new TrRmSection(0x0011, 0, 1, typeSpecBytes),
            new TrRmSection(0x0013, 0, 1, typeDefBytes),
            new TrRmSection(0x0014, 0, 1, fieldBytes),
            new TrRmSection(0x0015, 0, 2, layoutBytes),
            new TrRmSection(0x0023, 0, 2, layoutFieldBytes),
            new TrRmSection(0x0048, 0, 1, targetProfileBytes),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);
    }

    [Test]
    public void RejectsUnassignedProfilesAndIncompleteVariants()
    {
        Assert.Throws<InvalidDataException>(() =>
            TrRmTargetProfileTableWriter.Encode([new TrRmTargetProfileRow(2, 1)]));

        Assert.Throws<InvalidDataException>(() => EncodeLayoutRows(TypeDefRows(1), 1,
            [], [new TrRmLayoutRow(1, 0, 1, 1, 0, 16, 0, 0)], 0));
        Assert.Throws<InvalidDataException>(() => EncodeLayoutRows(TypeDefRows(2), 2,
            [], [
                new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 0, 0),
                new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 0, 0),
            ], 0));
    }

    private static byte[] Names()
        => TrRmStringTableWriter.EncodeTextRows([("Module", 0), ("Widget", 0)]);

    private static byte[] Module(TrRmStringTableReader strings, int typeDefCount)
        => TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, typeDefCount, 0, 0);

    private static byte[] TypeDefRows(int layoutCount)
    {
        var row = new byte[44];
        Write(row, 0, 2);
        Write(row, 4, 2);
        Write(row, 8, 4);
        Write(row, 28, 1);
        Write(row, 32, (uint)layoutCount);
        return row;
    }

    private static void EncodeLayoutRows(byte[] typeDefBytes, int layoutCount,
        byte[] profileBytes, TrRmLayoutRow[] rows, int layoutFieldCount)
    {
        var typeDefCount = typeDefBytes.Length / 44;
        var names = new TrRmStringTableReader(Names(), 2);
        var modules = new TrRmModuleTableReader(Module(names, typeDefCount), 1, names,
            0, typeDefCount, 0, 0);
        var types = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            names, modules, 0, 0, layoutCount, 0);
        var profiles = new TrRmTargetProfileTableReader(profileBytes,
            profileBytes.Length / sizeof(ulong));
        _ = TrRmLayoutTableWriter.Encode(rows, types, profiles, layoutFieldCount);
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
