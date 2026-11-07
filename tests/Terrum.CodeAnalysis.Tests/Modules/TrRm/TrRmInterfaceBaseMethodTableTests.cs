// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmInterfaceBaseMethodTableTests
{
    [Test]
    public void ValidatesCompleteBaseAndDirectInstanceMethodRows()
    {
        var fixture = CreateFixture();

        TrRmStructuralMetadataValidator.Validate(fixture.Artifact);

        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            fixture.InterfaceBaseBytes.AsSpan(8)), Is.EqualTo(1u));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            fixture.InterfaceMethodBytes.AsSpan(4)), Is.Zero);
    }

    [Test]
    public void InterfaceBaseAndMethodReadersChargeAtExactWorkBoundary()
    {
        var fixture = CreateFixture();
        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var readers = CreateReaders(fixture);
            var bases = new TrRmInterfaceBaseTableReader(
                fixture.InterfaceBaseBytes, 1, readers.TypeSpecs,
                readers.Interfaces, ledger);
            Assert.That(bases.GetBaseTypeSpecRef(1), Is.EqualTo(1u));

            var methods = new TrRmInterfaceMethodTableReader(
                fixture.InterfaceMethodBytes, 1, readers.Interfaces,
                readers.Methods, readers.Signatures, ledger);
            Assert.That(methods.GetMethodDefRow(1), Is.EqualTo(1));
        });
    }

    [Test]
    public void RejectsMalformedBaseReferencesAndOrdinalsInArtifacts()
    {
        var fixture = CreateFixture();
        var malformedBase = (byte[])fixture.InterfaceBaseBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedBase.AsSpan(sizeof(uint)), 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateArtifact(fixture, malformedBase, fixture.InterfaceMethodBytes,
                    fixture.SignatureBytes)));

        malformedBase = (byte[])fixture.InterfaceBaseBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedBase.AsSpan(2 * sizeof(uint)), 0);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateArtifact(fixture, malformedBase, fixture.InterfaceMethodBytes,
                    fixture.SignatureBytes)));
    }

    [Test]
    public void RejectsMethodOwnerOrdinalAndInstanceMismatchesInArtifacts()
    {
        var fixture = CreateFixture();
        var malformedMethod = (byte[])fixture.InterfaceMethodBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformedMethod, 2);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateArtifact(fixture, fixture.InterfaceBaseBytes, malformedMethod,
                    fixture.SignatureBytes)));

        malformedMethod = (byte[])fixture.InterfaceMethodBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedMethod.AsSpan(sizeof(uint)), 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateArtifact(fixture, fixture.InterfaceBaseBytes, malformedMethod,
                    fixture.SignatureBytes)));

        var staticFixture = CreateFixture(receiverTypeSpecRef: 0);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(staticFixture.Artifact));

        var topLevelFixture = CreateFixture(methodOwnerTypeDef: 0, receiverTypeSpecRef: 0);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(topLevelFixture.Artifact));
    }

    private static Fixture CreateFixture(int methodOwnerTypeDef = 1,
        uint receiverTypeSpecRef = 1)
    {
        var stringBytes = TrRmStringTableWriter.EncodeNames([
            ("Module", 0), ("Interface", 0), ("Self", 0), ("Item", 0), ("Method", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 5);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 1, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 1, 0);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Reference(0x04, 1),
        ], 1, 0, 1, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1, 1, 0, 1, 0);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 2, 12,
                0, 0, 1, 1, 1, 1, 0, 0),
        ], strings, modules, 0, 1, 1, 0);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 1,
            strings, modules, 0, 1, 1, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0008, receiverTypeSpecRef, 0, 0, 0, 0),
        ], typeSpecs, 0, 1);
        var signatures = new TrRmMethodSigTableReader(
            signatureBytes, 1, typeSpecs, 0, 1);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(methodOwnerTypeDef, 5, 1, 0, 0, 0, 0),
        ], strings, modules, types, signatures, 0, 0);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1,
            strings, modules, types, signatures, 0, 0);
        var genericParamBytes = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1300_0001, 0, 3, 0, 0, 1, 1, 1, 0, 0),
        ], strings, types, methods, signatures, typeSpecs, 0);
        var formals = new TrRmGenericParamTableReader(genericParamBytes, 1,
            strings, types, methods, signatures, typeSpecs, 0);
        var interfaceDefBytes = TrRmInterfaceDefTableWriter.Encode([
            new TrRmInterfaceDefRow(1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0),
        ], types, formals, 1, 1, 0, 0, 0);
        var interfaces = new TrRmInterfaceDefTableReader(interfaceDefBytes, 1,
            types, formals, 1, 1, 0, 0, 0);
        var interfaceBaseBytes = TrRmInterfaceBaseTableWriter.Encode([
            new TrRmInterfaceBaseRow(1, 0, 1),
        ], typeSpecs, interfaces);

        byte[] interfaceMethodBytes;
        if (methodOwnerTypeDef == 1 && receiverTypeSpecRef != 0)
        {
            interfaceMethodBytes = TrRmInterfaceMethodTableWriter.Encode([
                new TrRmInterfaceMethodRow(1, 0, 1),
            ], interfaces, methods, signatures);
        }
        else
        {
            interfaceMethodBytes = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(interfaceMethodBytes.AsSpan(8), 1);
        }

        var fixture = new Fixture(stringBytes, moduleBytes, typeSpecBytes, typeDefBytes,
            layoutBytes, signatureBytes, methodBytes, genericParamBytes, interfaceDefBytes,
            interfaceBaseBytes, interfaceMethodBytes);
        return fixture with
        {
            Artifact = CreateArtifact(fixture, interfaceBaseBytes,
                interfaceMethodBytes, signatureBytes),
        };
    }

    private static Readers CreateReaders(Fixture fixture)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 5);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 1, 1, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs, 1,
            1, 0, 1, 0);
        var types = new TrRmTypeDefTableReader(fixture.TypeDefs, 1,
            strings, modules, 0, 1, 1, 0);
        var signatures = new TrRmMethodSigTableReader(
            fixture.SignatureBytes, 1, typeSpecs, 0, 1);
        var methods = new TrRmMethodDefTableReader(fixture.Methods, 1,
            strings, modules, types, signatures, 0, 0);
        var genericParams = new TrRmGenericParamTableReader(
            fixture.GenericParams, 1, strings, types, methods,
            signatures, typeSpecs, 0);
        var interfaces = new TrRmInterfaceDefTableReader(
            fixture.InterfaceDefs, 1, types, genericParams,
            1, 1, 0, 0, 0);
        return new Readers(typeSpecs, signatures, methods, interfaces);
    }

    private static byte[] CreateArtifact(Fixture fixture,
        byte[] interfaceBaseBytes, byte[] interfaceMethodBytes, byte[] signatureBytes)
        => TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, fixture.Modules),
            new TrRmSection(0x0002, 0, 5, fixture.Strings),
            new TrRmSection(0x0011, 0, 1, fixture.TypeSpecs),
            new TrRmSection(0x0013, 0, 1, fixture.TypeDefs),
            new TrRmSection(0x0015, 0, 1, fixture.Layouts),
            new TrRmSection(0x0016, 0, 1, fixture.Methods),
            new TrRmSection(0x0017, 0, 1, signatureBytes),
            new TrRmSection(0x0019, 0, 1, fixture.GenericParams),
            new TrRmSection(0x0024, 0, 1, interfaceMethodBytes),
            new TrRmSection(0x0025, 0, 1, fixture.InterfaceDefs),
            new TrRmSection(0x0026, 0, 1, interfaceBaseBytes),
        ]);

    private sealed record Fixture(
        byte[] Strings,
        byte[] Modules,
        byte[] TypeSpecs,
        byte[] TypeDefs,
        byte[] Layouts,
        byte[] SignatureBytes,
        byte[] Methods,
        byte[] GenericParams,
        byte[] InterfaceDefs,
        byte[] InterfaceBaseBytes,
        byte[] InterfaceMethodBytes)
    {
        public byte[] Artifact { get; init; } = [];
    }

    private readonly ref struct Readers
    {
        public Readers(TrRmTypeSpecTableReader typeSpecs,
            TrRmMethodSigTableReader signatures,
            TrRmMethodDefTableReader methods,
            TrRmInterfaceDefTableReader interfaces)
        {
            TypeSpecs = typeSpecs;
            Signatures = signatures;
            Methods = methods;
            Interfaces = interfaces;
        }

        public TrRmTypeSpecTableReader TypeSpecs { get; }
        public TrRmMethodSigTableReader Signatures { get; }
        public TrRmMethodDefTableReader Methods { get; }
        public TrRmInterfaceDefTableReader Interfaces { get; }
    }
}
