// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmFieldDefTableWriterTests
{
    [Test]
    public void EmitsFieldOwnedBySecondModuleTypeInFramedArtifact()
    {
        var stringBytes = Names();
        var strings = new TrRmStringTableReader(stringBytes, 6);
        var moduleBytes = Modules(strings);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, strings, 0, 2, 0, 0);
        var typeBytes = Types(strings, modules);
        var types = new TrRmTypeDefTableReader(typeBytes, 2, strings, modules, 1, 0, 0, 0);
        var specBytes = TrRmTypeSpecTableWriter.Encode([new byte[] { 1, 0, 8 }], 2, 0, 0, 0);
        var specs = new TrRmTypeSpecTableReader(specBytes, 1, 2, 0, 0, 0);
        var rows = new[] { new TrRmFieldDefRow(2, 6, 1, 0, uint.MaxValue) };
        var fieldBytes = TrRmFieldDefTableWriter.Encode(rows, strings, types, specs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 1, strings, types, specs);

        Assert.That(fieldBytes.Length, Is.EqualTo(20));
        Assert.That(fields.GetName(1), Is.EqualTo("Value"));
        Assert.That(types.GetFieldOwner(1), Is.EqualTo(2));
        rows[0] = rows[0] with { OwnerTypeDefRow = 1 };
        Assert.That(fields.GetName(1), Is.EqualTo("Value"));

        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 2, moduleBytes),
            new TrRmSection(0x0002, 0, 6, stringBytes),
            new TrRmSection(0x0011, 0, 1, specBytes),
            new TrRmSection(0x0013, 0, 2, typeBytes),
            new TrRmSection(0x0014, 0, 1, fieldBytes),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);
    }

    [Test]
    public void RejectsInvalidOwnerNameSpecFlagsOffsetAndCount()
    {
        var row = new TrRmFieldDefRow(2, 6, 1, 0, uint.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode([]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { OwnerTypeDefRow = 1 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { NameStringRow = 0 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { TypeSpecRow = 2 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { Flags = 3 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { Flags = 8 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { ByteOffset = 0 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row, row]));
    }

    private static byte[] Encode(TrRmFieldDefRow[] rows)
    {
        var stringBytes = Names();
        var strings = new TrRmStringTableReader(stringBytes, 6);
        var moduleBytes = Modules(strings);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, strings, 0, 2, 0, 0);
        var typeBytes = Types(strings, modules);
        var types = new TrRmTypeDefTableReader(typeBytes, 2, strings, modules, 1, 0, 0, 0);
        var specBytes = TrRmTypeSpecTableWriter.Encode([new byte[] { 1, 0, 8 }], 2, 0, 0, 0);
        var specs = new TrRmTypeSpecTableReader(specBytes, 1, 2, 0, 0, 0);
        return TrRmFieldDefTableWriter.Encode(rows, strings, types, specs);
    }

    private static byte[] Names()
        => TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("ModuleA", 0), ("ModuleB", 0), ("Widget", 0), ("Gadget", 0), ("Value", 0),
        ]);

    private static byte[] Modules(TrRmStringTableReader strings)
        => TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1),
            new TrRmModuleRow(3, 0, 0, 0, 1, 2, 1, 1),
        ], strings, 0, 2, 0, 0);

    private static byte[] Types(TrRmStringTableReader strings, TrRmModuleTableReader modules)
        => TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 4, 4, 0, 0, 0, 0, 0, 0, 0),
            new TrRmTypeDefRow(1, 5, 4, 1, 1, 0, 0, 0, 0, 0),
        ], strings, modules, 1, 0, 0, 0);
}
