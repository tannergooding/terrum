// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmTypeDefTableWriterTests
{
    [Fact]
    public void EmitsMultiModuleTypePartitionsAndFieldOwnership()
    {
        var stringBytes = Names();
        var strings = new TrRmStringTableReader(stringBytes, 5);
        var moduleBytes = Modules(strings);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, strings, 0, 2, 0, 0);
        var rows = new[]
        {
            new TrRmTypeDefRow(1, 4, 4, 0, 0, 0, 0, 0, 0, 0),
            new TrRmTypeDefRow(1, 5, 4, 1, 1, 0, 0, 0, 0, 0),
        };
        var bytes = TrRmTypeDefTableWriter.Encode(rows, strings, modules, 1, 0, 0, 0);
        var types = new TrRmTypeDefTableReader(bytes, 2, strings, modules, 1, 0, 0, 0);
        Assert.Equal(80, bytes.Length);
        Assert.Equal("", types.GetNamespace(1));
        Assert.Equal("Widget", types.GetName(1));
        Assert.Equal("Gadget", types.GetName(2));
        Assert.Equal((0, 0), types.GetFieldRange(1));
        Assert.Equal((1, 2), types.GetFieldRange(2));
        Assert.Equal(2, types.GetFieldOwner(1));
        Assert.Equal(1, types.GetOwnerModule(1));
        Assert.Equal(2, types.GetOwnerModule(2));

        rows[1] = rows[1] with { FieldFirst = 0, FieldCount = 0 };
        Assert.Equal((1, 2), types.GetFieldRange(2));
        var noFields = TrRmTypeDefTableWriter.Encode(rows, strings, modules, 0, 0, 0, 0);
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 2, moduleBytes),
            new TrRmSection(0x0002, 0, 5, stringBytes),
            new TrRmSection(0x0013, 0, 2, noFields),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);
    }

    [Fact]
    public void RejectsInvalidNamesFlagsRangesAndMissingOwners()
    {
        var first = new TrRmTypeDefRow(1, 4, 4, 0, 0, 0, 0, 0, 0, 0);
        var second = new TrRmTypeDefRow(1, 5, 4, 1, 1, 0, 0, 0, 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode([]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([first with { NamespaceStringRow = 0 }, second]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([first with { NameStringRow = 1 }, second]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([first, second with { Flags = 0 }]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([first with { FieldFirst = 1, FieldCount = 1 }, second]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([first, second with { FieldFirst = 0, FieldCount = 0 }]));
        Assert.Throws<InvalidDataException>(() => Encode([first]));
    }

    private static byte[] Encode(TrRmTypeDefRow[] rows)
    {
        var names = Names();
        var strings = new TrRmStringTableReader(names, 5);
        var moduleBytes = Modules(strings);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, strings, 0, 2, 0, 0);
        return TrRmTypeDefTableWriter.Encode(rows, strings, modules, 1, 0, 0, 0);
    }

    private static byte[] Names()
        => TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("ModuleA", 0), ("ModuleB", 0), ("Widget", 0), ("Gadget", 0),
        ]);

    private static byte[] Modules(TrRmStringTableReader strings)
        => TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1),
            new TrRmModuleRow(3, 0, 0, 0, 1, 2, 1, 1),
        ], strings, 0, 2, 0, 0);
}
