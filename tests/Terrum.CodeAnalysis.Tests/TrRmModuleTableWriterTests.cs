// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmModuleTableWriterTests
{
    [Fact]
    public void EmitsOwnedMultiModulePartitionsAndValidatesFraming()
    {
        var names = TrRmStringTableWriter.EncodeNames([("A", 0), ("B", 2)]);
        var rows = new[]
        {
            new TrRmModuleRow(1, 1, 0, 1, 1, 1, 1, 1),
            new TrRmModuleRow(2, 0, 1, 0, 2, 1, 2, 1),
        };
        var strings = new TrRmStringTableReader(names, 2);
        var bytes = TrRmModuleTableWriter.Encode(rows, strings, 2, 0, 1, 0);
        var modules = new TrRmModuleTableReader(bytes, 2, strings, 2, 0, 1, 0);
        Assert.Equal(88, bytes.Length);
        Assert.Equal("A", modules.GetIdentity(1));
        Assert.Equal("B", modules.GetIdentity(2));
        Assert.Equal((1, 2), modules.GetModuleRefRange(1));
        Assert.Equal((2, 3), modules.GetModuleRefRange(2));
        Assert.Equal((1, 2), modules.GetMethodDefRange(1));
        Assert.Equal((2, 2), modules.GetMethodDefRange(2));
        Assert.Equal((1, 1), modules.GetTypeDefRange(2));
        Assert.Equal((1u, 0u), modules.GetRequirements(1));
        Assert.Equal((0u, 1u), modules.GetRequirements(2));

        rows[0] = rows[0] with { IdentityStringRow = 2 };
        Assert.Equal("A", modules.GetIdentity(1));

        var minimalNames = TrRmStringTableWriter.EncodeNames([("Only", 0)]);
        var minimalStrings = new TrRmStringTableReader(minimalNames, 1);
        var minimal = TrRmModuleTableWriter.Encode(
            [new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1)], minimalStrings, 0, 0, 0, 0);
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, minimal),
            new TrRmSection(0x0002, 0, 1, minimalNames),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);
    }

    [Fact]
    public void RejectsInvalidIdentitiesRangesPoliciesAndEntryOwnership()
    {
        var first = new TrRmModuleRow(1, 0, 0, 1, 1, 1, 1, 1);
        var second = new TrRmModuleRow(2, 0, 0, 0, 2, 1, 2, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Encode([]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([first with { IdentityStringRow = 0 }, second]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([first, second with { IdentityStringRow = 1 }]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([first with { RuntimePolicy = 2 }, second]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([first, second with { ModuleRefStart = 0 }]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([first with { EntryMethodDefRow = 2 }, second]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([first, second], moduleRefCount: 0));
    }

    private static byte[] Encode(TrRmModuleRow[] rows, int moduleRefCount = 2)
    {
        var names = TrRmStringTableWriter.EncodeNames([("A", 0), ("B", 0)]);
        var strings = new TrRmStringTableReader(names, 2);
        return TrRmModuleTableWriter.Encode(rows, strings, moduleRefCount, 0, 1, 0);
    }
}
