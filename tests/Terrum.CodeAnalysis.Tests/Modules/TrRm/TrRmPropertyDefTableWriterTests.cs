// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmPropertyDefTableWriterTests
{
    [Test]
    public void EmitsGetterIdentityIndependentOfImplementationName()
    {
        var stringBytes = Names();
        var strings = new TrRmStringTableReader(stringBytes, 5);
        var moduleBytes = Modules(strings);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 0, 1, 1, 0);
        var typeBytes = Types(strings, modules);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings, modules, 0, 0, 0, 0);
        var specBytes = Specs();
        var specs = new TrRmTypeSpecTableReader(specBytes, 2, 1, 0, 0, 0);
        var sigBytes = Signatures(specs);
        var signatures = new TrRmMethodSigTableReader(sigBytes, 1, specs, 0, 0);
        var methodBytes = Methods(strings, modules, types, signatures);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1,
            strings, modules, types, signatures, 0, 0);
        var rows = new[] { new TrRmPropertyDefRow(1, 5, 1, 1, 0) };
        var propertyBytes = TrRmPropertyDefTableWriter.Encode(
            rows, strings, types, specs, methods, signatures);
        var properties = new TrRmPropertyDefTableReader(propertyBytes, 1,
            strings, types, specs, methods, signatures);
        Assert.That(propertyBytes.Length, Is.EqualTo(20));
        Assert.That(properties.GetName(1), Is.EqualTo("Value"));
        Assert.That(methods.GetName(properties.GetGetterMethod(1)), Is.EqualTo("Read"));

        rows[0] = rows[0] with { GetterMethodDefRow = 0 };
        Assert.That(properties.GetGetterMethod(1), Is.EqualTo(1));

        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 5, stringBytes),
            new TrRmSection(0x0011, 0, 2, specBytes),
            new TrRmSection(0x0013, 0, 1, typeBytes),
            new TrRmSection(0x0016, 0, 1, methodBytes),
            new TrRmSection(0x0017, 0, 1, sigBytes),
            new TrRmSection(0x001E, 0, 1, propertyBytes),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);
    }

    [Test]
    public void RejectsInvalidOwnerNameTypeGetterAndVisibility()
    {
        var row = new TrRmPropertyDefRow(1, 5, 1, 1, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode([]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { OwnerTypeDefRow = 0 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { NameStringRow = 0 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { TypeSpecRow = 2 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { GetterMethodDefRow = 0 }]));
        Assert.Throws<InvalidDataException>(() => Encode([row with { Flags = 3 }]));
    }

    private static byte[] Encode(TrRmPropertyDefRow[] rows)
    {
        var stringBytes = Names();
        var strings = new TrRmStringTableReader(stringBytes, 5);
        var moduleBytes = Modules(strings);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 0, 1, 1, 0);
        var typeBytes = Types(strings, modules);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings, modules, 0, 0, 0, 0);
        var specBytes = Specs();
        var specs = new TrRmTypeSpecTableReader(specBytes, 2, 1, 0, 0, 0);
        var sigBytes = Signatures(specs);
        var signatures = new TrRmMethodSigTableReader(sigBytes, 1, specs, 0, 0);
        var methodBytes = Methods(strings, modules, types, signatures);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1,
            strings, modules, types, signatures, 0, 0);
        return TrRmPropertyDefTableWriter.Encode(rows, strings, types, specs, methods, signatures);
    }

    private static byte[] Names()
        => TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Read", 0), ("Value", 0),
        ]);

    private static byte[] Modules(TrRmStringTableReader strings)
        => TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 1, 0);

    private static byte[] Types(TrRmStringTableReader strings, TrRmModuleTableReader modules)
        => TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 4, 0, 0, 0, 0, 0, 0, 0),
        ], strings, modules, 0, 0, 0, 0);

    private static byte[] Specs()
        => TrRmTypeSpecTableWriter.Encode([
            new byte[] { 1, 0, 8 },
            new byte[] { 2, 0, 1, 0, 0, 0 },
        ], 1, 0, 0, 0);

    private static byte[] Signatures(TrRmTypeSpecTableReader specs)
        => TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 2, 0, 0, 0, 0),
        ], specs, 0, 0);

    private static byte[] Methods(TrRmStringTableReader strings, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types, TrRmMethodSigTableReader signatures)
        => TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(1, 4, 1, 0, 0, 0, 0),
        ], strings, modules, types, signatures, 0, 0);
}
