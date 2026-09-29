// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmMethodSigAndParamTableWriterTests
{
    [Fact]
    public void EmitsCallableRowsInAFramedArtifact()
    {
        var specBytes = TrRmTypeSpecTableWriter.Encode([new byte[] { 1, 0, 8 }], 0, 0, 0, 0);
        var types = new TrRmTypeSpecTableReader(specBytes, 1, 0, 0, 0, 0);
        var rows = new[] { new TrRmMethodSigRow(1, 0, 1, 2, 0, 0) };
        var sigBytes = TrRmMethodSigTableWriter.Encode(rows, types, 2, 0);
        var sigs = new TrRmMethodSigTableReader(sigBytes, 1, types, 2, 0);
        var paramRows = new[]
        {
            new TrRmParamRow(1, 0, 1, 0),
            new TrRmParamRow(1, 1, 1, 4),
        };
        var paramBytes = TrRmParamTableWriter.Encode(paramRows, sigs, types);
        var parameters = new TrRmParamTableReader(paramBytes, 2, sigs, types);
        Assert.Equal(24, sigBytes.Length);
        Assert.Equal(32, paramBytes.Length);
        Assert.Equal((byte)4, parameters.GetMode(2));

        rows[0] = rows[0] with { ResultTypeSpecRow = 0 };
        paramRows[1] = paramRows[1] with { Mode = 9 };
        Assert.Equal(1, sigs.GetResultTypeSpec(1));
        Assert.Equal((byte)4, parameters.GetMode(2));

        var nameBytes = TrRmStringTableWriter.EncodeNames([("Only", 0), ("Run", 0)]);
        var strings = new TrRmStringTableReader(nameBytes, 2);
        var moduleBytes = TrRmModuleTableWriter.Encode(
            [new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1)], strings, 0, 0, 1, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 0, 0, 1, 0);
        var nominalTypes = new TrRmTypeDefTableReader([], 0, strings, modules, 0, 0, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 2, 1, 0, 0, 0, 0),
        ], strings, modules, nominalTypes, sigs, 0, 0);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1, strings, modules,
            nominalTypes, sigs, 0, 0);
        Assert.Equal("Run", methods.GetName(1));
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 2, nameBytes),
            new TrRmSection(0x0011, 0, 1, specBytes),
            new TrRmSection(0x0016, 0, 1, methodBytes),
            new TrRmSection(0x0017, 0, 1, sigBytes),
            new TrRmSection(0x0018, 0, 2, paramBytes),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);
    }

    [Fact]
    public void SupportsDisjointParamRangesOutOfSignatureOrder()
    {
        var specBytes = TrRmTypeSpecTableWriter.Encode([new byte[] { 1, 0, 8 }], 0, 0, 0, 0);
        var types = new TrRmTypeSpecTableReader(specBytes, 1, 0, 0, 0, 0);
        var bytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 2, 1, 0, 0),
            new TrRmMethodSigRow(1, 0, 1, 1, 0, 0),
        ], types, 2, 0);
        var sigs = new TrRmMethodSigTableReader(bytes, 2, types, 2, 0);
        var parameters = TrRmParamTableWriter.Encode([
            new TrRmParamRow(2, 0, 1, 0),
            new TrRmParamRow(1, 0, 1, 4),
        ], sigs, types);
        _ = new TrRmParamTableReader(parameters, 2, sigs, types);
        Assert.Equal(2, sigs.GetParamOwner(1));
        Assert.Equal(1, sigs.GetParamOwner(2));
    }

    [Fact]
    public void RejectsBadSignatureReferencesRangesAndMissingParams()
    {
        var row = new TrRmMethodSigRow(1, 0, 1, 2, 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeSignature([]));
        Assert.Throws<InvalidDataException>(() => EncodeSignature([row with { ResultTypeSpecRow = 2 }]));
        Assert.Throws<InvalidDataException>(() => EncodeSignature([row with { ReceiverTypeSpecRow = 2 }]));
        Assert.Throws<InvalidDataException>(() => EncodeSignature([row with { ParamFirst = 0 }]));
        Assert.Throws<InvalidDataException>(() => EncodeSignature([row with { ParamCount = 1 }]));
        Assert.Throws<InvalidDataException>(() => EncodeSignature([row with { GenericFirst = 1 }]));
    }

    [Fact]
    public void RejectsBadParameterOwnershipOrdinalsTypesAndModes()
    {
        var first = new TrRmParamRow(1, 0, 1, 0);
        var second = new TrRmParamRow(1, 1, 1, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeParams([]));
        Assert.Throws<InvalidDataException>(() => EncodeParams([first with { OwnerMethodSigRow = 2 }, second]));
        Assert.Throws<InvalidDataException>(() => EncodeParams([first, second with { Ordinal = 0 }]));
        Assert.Throws<InvalidDataException>(() => EncodeParams([first, second with { TypeSpecRow = 0 }]));
        Assert.Throws<InvalidDataException>(() => EncodeParams([first, second with { Mode = 5 }]));
        Assert.Throws<InvalidDataException>(() => EncodeParams([first]));
    }

    [Fact]
    public void RejectsBadMethodNamesSignaturesAndUnownedBodies()
    {
        var row = new TrRmMethodDefRow(0, 2, 1, 0, 0, 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeMethods([]));
        Assert.Throws<InvalidDataException>(() => EncodeMethods([row with { OwnerTypeDefRow = 1 }]));
        Assert.Throws<InvalidDataException>(() => EncodeMethods([row with { NameStringRow = 0 }]));
        Assert.Throws<InvalidDataException>(() => EncodeMethods([row with { SignatureRow = 2 }]));
        Assert.Throws<InvalidDataException>(() => EncodeMethods([row with { Flags = 3 }]));
        Assert.Throws<InvalidDataException>(() => EncodeMethods([row with { ContractFirst = 1 }]));
        Assert.Throws<InvalidDataException>(() => EncodeMethods([row with { BodyRow = 1 }]));
        Assert.Throws<InvalidDataException>(() => EncodeMethods([row, row]));
    }

    private static byte[] EncodeSignature(TrRmMethodSigRow[] rows)
    {
        var specBytes = TrRmTypeSpecTableWriter.Encode([new byte[] { 1, 0, 8 }], 0, 0, 0, 0);
        var types = new TrRmTypeSpecTableReader(specBytes, 1, 0, 0, 0, 0);
        return TrRmMethodSigTableWriter.Encode(rows, types, 2, 0);
    }

    private static byte[] EncodeParams(TrRmParamRow[] rows)
    {
        var specBytes = TrRmTypeSpecTableWriter.Encode([new byte[] { 1, 0, 8 }], 0, 0, 0, 0);
        var types = new TrRmTypeSpecTableReader(specBytes, 1, 0, 0, 0, 0);
        var sigBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 1, 2, 0, 0),
        ], types, 2, 0);
        var sigs = new TrRmMethodSigTableReader(sigBytes, 1, types, 2, 0);
        return TrRmParamTableWriter.Encode(rows, sigs, types);
    }

    private static byte[] EncodeMethods(TrRmMethodDefRow[] rows)
    {
        var nameBytes = TrRmStringTableWriter.EncodeNames([("Only", 0), ("Run", 0)]);
        var strings = new TrRmStringTableReader(nameBytes, 2);
        var moduleBytes = TrRmModuleTableWriter.Encode(
            [new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1)], strings, 0, 0, 1, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 0, 0, 1, 0);
        var nominalTypes = new TrRmTypeDefTableReader([], 0, strings, modules, 0, 0, 0, 0);
        var specBytes = TrRmTypeSpecTableWriter.Encode([new byte[] { 1, 0, 8 }], 0, 0, 0, 0);
        var types = new TrRmTypeSpecTableReader(specBytes, 1, 0, 0, 0, 0);
        var sigBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 0, 0, 0, 0),
        ], types, 0, 0);
        var sigs = new TrRmMethodSigTableReader(sigBytes, 1, types, 0, 0);
        return TrRmMethodDefTableWriter.Encode(rows, strings, modules, nominalTypes, sigs, 0, 0);
    }
}
