// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmInterfaceConstantTableTests
{
    [Test]
    public void ValidatesInterfaceAndAssociatedConstantDeclarations()
    {
        var fixture = CreateFixture();

        TrRmStructuralMetadataValidator.Validate(fixture.Artifact);
        AssertRows(fixture);
    }

    [Test]
    public void RejectsMalformedConstantRowsAndOrphanedRangesInArtifacts()
    {
        var fixture = CreateFixture();
        var malformedInterfaceConst = (byte[])fixture.InterfaceConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformedInterfaceConst, 2);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            malformedInterfaceConst, fixture.AssociatedConstDefs);

        malformedInterfaceConst = (byte[])fixture.InterfaceConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedInterfaceConst.AsSpan(sizeof(uint)), 0);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            malformedInterfaceConst, fixture.AssociatedConstDefs);

        malformedInterfaceConst = (byte[])fixture.InterfaceConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedInterfaceConst.AsSpan(2 * sizeof(uint)), 0);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            malformedInterfaceConst, fixture.AssociatedConstDefs);

        malformedInterfaceConst = (byte[])fixture.InterfaceConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedInterfaceConst.AsSpan(3 * sizeof(uint)), 2);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            malformedInterfaceConst, fixture.AssociatedConstDefs);

        var malformedAssociatedConst = (byte[])fixture.AssociatedConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformedAssociatedConst, 2);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            fixture.InterfaceConstDefs, malformedAssociatedConst);

        malformedAssociatedConst = (byte[])fixture.AssociatedConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedAssociatedConst.AsSpan(sizeof(uint)), 0);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            fixture.InterfaceConstDefs, malformedAssociatedConst);

        malformedAssociatedConst = (byte[])fixture.AssociatedConstDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedAssociatedConst.AsSpan(2 * sizeof(uint)), 0);
        AssertInvalid(fixture, fixture.InterfaceDefs,
            fixture.InterfaceConstDefs, malformedAssociatedConst);

        var malformedInterface = (byte[])fixture.InterfaceDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedInterface.AsSpan(10 * sizeof(uint)), 0);
        AssertInvalid(fixture, malformedInterface,
            fixture.InterfaceConstDefs, fixture.AssociatedConstDefs);

        malformedInterface = (byte[])fixture.InterfaceDefs.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedInterface.AsSpan(8 * sizeof(uint)), 0);
        AssertInvalid(fixture, malformedInterface,
            fixture.InterfaceConstDefs, fixture.AssociatedConstDefs);
    }

    private static Fixture CreateFixture()
    {
        var stringBytes = TrRmStringTableWriter.EncodeNames([
            ("Module", 0), ("Interface", 0), ("Associated", 0), ("Fixed", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 4);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 0, 0);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            new byte[] { 1, 0, 8 },
        ], 0, 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1, 0, 0, 0, 0);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 2, 12, 0, 0, 0, 0, 1, 1, 0, 0),
        ], strings, modules, 0, 0, 1, 0);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 1,
            strings, modules, 0, 0, 1, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var generics = new TrRmGenericParamTableReader([], 0,
            strings, types, methods, signatures, typeSpecs, 0, 1);
        var interfaceDefBytes = TrRmInterfaceDefTableWriter.Encode([
            new TrRmInterfaceDefRow(1, 0, 0, 0, 0, 0,
                0, 0, 1, 1, 1, 1),
        ], types, generics, 0, 0, 0, 1, 1);
        var interfaces = new TrRmInterfaceDefTableReader(interfaceDefBytes, 1,
            types, generics, 0, 0, 0, 1, 1);
        var interfaceConstDefBytes = TrRmInterfaceConstDefTableWriter.Encode([
            new TrRmInterfaceConstDefRow(1, 4, 1, 1),
        ], strings, typeSpecs, interfaces, 1);
        var associatedConstDefBytes = TrRmAssociatedConstDefTableWriter.Encode([
            new TrRmAssociatedConstDefRow(1, 3, 1),
        ], strings, typeSpecs, interfaces);
        var constantSourceBytes = new byte[2 * sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(constantSourceBytes, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            constantSourceBytes.AsSpan(sizeof(uint)), 1);
        var fixture = new Fixture(stringBytes, moduleBytes, typeSpecBytes,
            typeDefBytes, layoutBytes, interfaceDefBytes, interfaceConstDefBytes,
            associatedConstDefBytes, constantSourceBytes);
        return fixture with
        {
            Artifact = CreateArtifact(fixture, interfaceDefBytes,
                interfaceConstDefBytes, associatedConstDefBytes),
        };
    }

    private static void AssertRows(Fixture fixture)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 4);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 1, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs, 1, 0, 0, 0, 0);
        var types = new TrRmTypeDefTableReader(fixture.TypeDefs, 1,
            strings, modules, 0, 0, 1, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layouts = new TrRmLayoutTableReader(fixture.Layouts, 1,
            types, targetProfiles, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var generics = new TrRmGenericParamTableReader([], 0,
            strings, types, methods, signatures, typeSpecs, 0, 1);
        var interfaces = new TrRmInterfaceDefTableReader(fixture.InterfaceDefs,
            1, types, generics, 0, 0, 0, 1, 1);
        var interfaceConsts = new TrRmInterfaceConstDefTableReader(
            fixture.InterfaceConstDefs, 1, strings, typeSpecs, interfaces, 1);
        var associatedConsts = new TrRmAssociatedConstDefTableReader(
            fixture.AssociatedConstDefs, 1, strings, typeSpecs, interfaces);

        Assert.That(interfaceConsts.GetOwnerInterfaceDef(1), Is.EqualTo(1));
        Assert.That(interfaceConsts.GetNameStringRow(1), Is.EqualTo(4));
        Assert.That(interfaceConsts.GetConstantTypeSpecRef(1), Is.EqualTo(1u));
        Assert.That(interfaceConsts.GetConstantSourceRow(1), Is.EqualTo(1));
        Assert.That(associatedConsts.GetOwnerInterfaceDef(1), Is.EqualTo(1));
        Assert.That(associatedConsts.GetNameStringRow(1), Is.EqualTo(3));
        Assert.That(associatedConsts.GetConstantTypeSpecRef(1), Is.EqualTo(1u));
        Assert.That(layouts.Count, Is.EqualTo(1));
    }

    private static void AssertInvalid(Fixture fixture, byte[] interfaceDefBytes,
        byte[] interfaceConstDefBytes, byte[] associatedConstDefBytes)
        => Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateArtifact(
                fixture, interfaceDefBytes, interfaceConstDefBytes,
                associatedConstDefBytes)));

    private static byte[] CreateArtifact(Fixture fixture, byte[] interfaceDefBytes,
        byte[] interfaceConstDefBytes, byte[] associatedConstDefBytes)
    {
        var blobBytes = TrRmVariableTableWriter.EncodeBlobs(
            [new byte[] { 0, 0, 0, 0 }]);
        var frozenValueBytes = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(frozenValueBytes, 1);
        frozenValueBytes[4] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(
            frozenValueBytes.AsSpan(8), 1);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, fixture.Modules),
            new TrRmSection(0x0002, 0, 4, fixture.Strings),
            new TrRmSection(0x0003, 0, 1, blobBytes),
            new TrRmSection(0x0011, 0, 1, fixture.TypeSpecs),
            new TrRmSection(0x0013, 0, 1, fixture.TypeDefs),
            new TrRmSection(0x0015, 0, 1, fixture.Layouts),
            new TrRmSection(0x0025, 0, 1, interfaceDefBytes),
            new TrRmSection(0x0030, 0, 1, fixture.ConstantSources),
            new TrRmSection(0x0031, 0, 1, interfaceConstDefBytes),
            new TrRmSection(0x0032, 0, 1, associatedConstDefBytes),
            new TrRmSection(0x003F, 0, 1, frozenValueBytes),
        ]);
    }

    private sealed record Fixture(
        byte[] Strings,
        byte[] Modules,
        byte[] TypeSpecs,
        byte[] TypeDefs,
        byte[] Layouts,
        byte[] InterfaceDefs,
        byte[] InterfaceConstDefs,
        byte[] AssociatedConstDefs,
        byte[] ConstantSources)
    {
        public byte[] Artifact { get; init; } = [];
    }
}
