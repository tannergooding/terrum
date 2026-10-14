// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmModuleRefTableWriterTests
{
    [Test]
    public void ComposesPackedAndExternalReferencesWithModulePartitions()
    {
        var nameBytes = TrRmStringTableWriter.EncodeNames([("A", 0), ("B", 2), ("X", 1)]);
        var names = new TrRmStringTableReader(nameBytes, 3);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1),
            new TrRmModuleRow(2, 1, 1, 0, 2, 1, 1, 1),
        ], names, 2, 0, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, names, 2, 0, 0, 0);
        var rows = new[]
        {
            new TrRmModuleRefRow(2, 1, 1),
            new TrRmModuleRefRow(3, 0, 0),
        };
        var refBytes = TrRmModuleRefTableWriter.Encode(rows, names, modules);
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 2, moduleBytes),
            new TrRmSection(0x0002, 0, 3, nameBytes),
            new TrRmSection(0x0010, 0, 2, refBytes),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);

        rows[0] = rows[0] with { ProducerIdentityStringRow = 3 };
        var refs = new TrRmModuleRefTableReader(refBytes, 2, names, modules);
        Assert.That(refs.GetProducerIdentity(1), Is.EqualTo("B"));
        Assert.That(refs.GetProducerIdentity(2), Is.EqualTo("X"));
        Assert.That(refs.GetOwnerModule(1), Is.EqualTo(1));
        Assert.That(refs.GetOwnerModule(2), Is.EqualTo(2));
    }

    [Test]
    public void RejectsInvalidProducerIdentityRequirementsAndModulePartition()
    {
        var packed = new TrRmModuleRefRow(2, 1, 1);
        var external = new TrRmModuleRefRow(3, 0, 0);
        Assert.Throws<InvalidDataException>(() =>
            Encode([packed with { ProducerIdentityStringRow = 0 }, external]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([packed with { ProducerIdentityStringRow = 4 }, external]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([packed with { RuntimePolicy = 0 }, external]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([packed with { TargetRequirement = 0 }, external]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([packed, external with { TargetRequirement = 2 }]));
        Assert.Throws<InvalidDataException>(() => Encode([packed]));
    }

    private static byte[] Encode(TrRmModuleRefRow[] rows)
    {
        var nameBytes = TrRmStringTableWriter.EncodeNames([("A", 0), ("B", 2), ("X", 1)]);
        var names = new TrRmStringTableReader(nameBytes, 3);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1),
            new TrRmModuleRow(2, 1, 1, 0, 2, 1, 1, 1),
        ], names, 2, 0, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, names, 2, 0, 0, 0);
        return TrRmModuleRefTableWriter.Encode(rows, names, modules);
    }
}
