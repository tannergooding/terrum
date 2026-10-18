// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmModuleTableWriterTests
{
    [Test]
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
        Assert.That(bytes.Length, Is.EqualTo(88));
        Assert.That(modules.GetIdentity(1), Is.EqualTo("A"));
        Assert.That(modules.GetIdentity(2), Is.EqualTo("B"));
        Assert.That(modules.GetModuleRefRange(1), Is.EqualTo((1, 2)));
        Assert.That(modules.GetModuleRefRange(2), Is.EqualTo((2, 3)));
        Assert.That(modules.GetMethodDefRange(1), Is.EqualTo((1, 2)));
        Assert.That(modules.GetMethodDefRange(2), Is.EqualTo((2, 2)));
        Assert.That(modules.GetTypeDefRange(2), Is.EqualTo((1, 1)));
        Assert.That(modules.GetRequirements(1), Is.EqualTo((1u, 0u)));
        Assert.That(modules.GetRequirements(2), Is.EqualTo((0u, 1u)));

        rows[0] = rows[0] with { IdentityStringRow = 2 };
        Assert.That(modules.GetIdentity(1), Is.EqualTo("A"));

        var minimalNames = TrRmStringTableWriter.EncodeNames([("Only", 0)]);
        var featureRow = new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1,
            FeatureFirst: 1, FeatureCount: 1);
        var featureBytes = TrRmModuleTableWriter.Encode([featureRow],
            new TrRmStringTableReader(minimalNames, 1), 0, 0, 0, 0, 1);
        var featureModules = new TrRmModuleTableReader(featureBytes, 1,
            new TrRmStringTableReader(minimalNames, 1), 0, 0, 0, 0, 1);
        Assert.That(featureModules.GetFeatureRange(1), Is.EqualTo((1, 1)));
        Assert.Throws<InvalidDataException>(() => TrRmModuleTableWriter.Encode(
            [featureRow with { FeatureFirst = 2 }],
            new TrRmStringTableReader(minimalNames, 1), 0, 0, 0, 0, 1));

        var minimalStrings = new TrRmStringTableReader(minimalNames, 1);
        var minimal = TrRmModuleTableWriter.Encode(
            [new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1)], minimalStrings, 0, 0, 0, 0);
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, minimal),
            new TrRmSection(0x0002, 0, 1, minimalNames),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);
    }

    [Test]
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
