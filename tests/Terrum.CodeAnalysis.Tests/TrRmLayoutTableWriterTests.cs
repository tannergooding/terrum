// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmLayoutTableWriterTests
{
    [Fact]
    public void EmitsOwnedLayoutAndFixedOffsetFieldInFramedArtifact()
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Value", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 4);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 0, 1, 0, 0);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x14, 1, 1, 0, 0, 1, 0, 0),
        ], strings, modules, 1, 0, 1, 0);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings, modules, 1, 0, 1, 0);
        var rows = new[] { new TrRmLayoutRow(1, 1, 4, 4, 0, 0) };
        var layoutBytes = TrRmLayoutTableWriter.Encode(rows, types);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 1, types);
        Assert.Equal(4u, layouts.GetSize(1));
        rows[0] = rows[0] with { OwnerTypeDefRow = 0 };
        _ = new TrRmLayoutTableReader(layoutBytes, 1, types);

        var specBytes = TrRmTypeSpecTableWriter.Encode([new byte[] { 1, 0, 8 }], 1, 0, 0, 0);
        var specs = new TrRmTypeSpecTableReader(specBytes, 1, 1, 0, 0, 0);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 4, 1, 0, 0),
        ], strings, types, specs, layouts);
        Assert.Throws<InvalidDataException>(() => EncodeFixedField(4, true));
        Assert.Throws<InvalidDataException>(() => EncodeFixedField(0, false));
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 4, stringBytes),
            new TrRmSection(0x0011, 0, 1, specBytes),
            new TrRmSection(0x0013, 0, 1, typeBytes),
            new TrRmSection(0x0014, 0, 1, fieldBytes),
            new TrRmSection(0x0015, 0, 1, layoutBytes),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);

        var invalidFieldBytes = (byte[])fieldBytes.Clone();
        invalidFieldBytes[16] = 4;
        var invalidArtifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 4, stringBytes),
            new TrRmSection(0x0011, 0, 1, specBytes),
            new TrRmSection(0x0013, 0, 1, typeBytes),
            new TrRmSection(0x0014, 0, 1, invalidFieldBytes),
            new TrRmSection(0x0015, 0, 1, layoutBytes),
        ]);
        Assert.Throws<InvalidDataException>(() => TrRmStructuralMetadataValidator.Validate(invalidArtifact));
    }

    [Fact]
    public void RejectsUnownedRowsUnassignedTargetsAndReservedFlags()
    {
        var row = new TrRmLayoutRow(1, 1, 4, 4, 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode([]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { OwnerTypeDefRow = 0 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { OwnerTypeDefRow = 2 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { TargetRequirement = 2 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { Flags = 1 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row, row]));
    }

    private static byte[] Encode(TrRmLayoutRow[] rows)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([("", 0), ("Module", 0), ("Widget", 0)]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 0, 1, 0, 0);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x14, 0, 0, 0, 0, 1, 0, 0),
        ], strings, modules, 0, 0, 1, 0);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings, modules, 0, 0, 1, 0);
        return TrRmLayoutTableWriter.Encode(rows, types);
    }

    private static byte[] EncodeFixedField(uint byteOffset, bool includeLayouts)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Value", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 4);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 0, 1, 0, 0);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x14, 1, 1, 0, 0, 1, 0, 0),
        ], strings, modules, 1, 0, 1, 0);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings, modules, 1, 0, 1, 0);
        var specBytes = TrRmTypeSpecTableWriter.Encode([new byte[] { 1, 0, 8 }], 1, 0, 0, 0);
        var specs = new TrRmTypeSpecTableReader(specBytes, 1, 1, 0, 0, 0);
        var row = new TrRmFieldDefRow(1, 4, 1, 0, byteOffset);
        if (!includeLayouts)
        {
            return TrRmFieldDefTableWriter.Encode([row], strings, types, specs);
        }

        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 1, 4, 4, 0, 0),
        ], types);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 1, types);
        return TrRmFieldDefTableWriter.Encode([row], strings, types, specs, layouts);
    }
}
