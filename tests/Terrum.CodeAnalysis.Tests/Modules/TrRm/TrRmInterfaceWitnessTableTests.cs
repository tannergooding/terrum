// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmInterfaceWitnessTableTests
{
    [Test]
    public void ValidatesWitnessTypePairsRangesAndSelectedMemberRows()
    {
        var fixture = CreateFixture();
        var readers = CreateReaders(fixture);
        var witnesses = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], readers.TypeSpecs, 1, 0, 0);
        var witnessReader = new TrRmInterfaceWitnessTableReader(
            witnesses, 1, readers.TypeSpecs, 1, 0, 0);
        var members = TrRmWitnessMemberTableWriter.Encode([
            new TrRmWitnessMemberRow(1, 2, 1, 1, 2),
        ], witnessReader, readers.TypeSpecs, readers.Methods);
        var memberReader = new TrRmWitnessMemberTableReader(
            members, 1, witnessReader, readers.TypeSpecs, readers.Methods);

        Assert.That(witnessReader.GetImplementingTypeSpecRef(1), Is.EqualTo(1u));
        Assert.That(witnessReader.GetInterfaceTypeSpecRef(1), Is.EqualTo(2u));
        Assert.That(witnessReader.GetMemberRange(1), Is.EqualTo((1, 1)));
        Assert.That(memberReader.GetDeclaringInterfaceTypeSpecRef(1), Is.EqualTo(2u));
        Assert.That(memberReader.GetRequirementMethodDef(1), Is.EqualTo(1));
        Assert.That(memberReader.GetSelectedMethodDef(1), Is.EqualTo(2));

        TrRmStructuralMetadataValidator.Validate(
            fixture.CreateArtifact(witnesses, members));
    }

    [Test]
    public void RejectsInvalidWitnessRangesTypesAndMemberReferences()
    {
        var fixture = CreateFixture();
        Assert.Throws<InvalidDataException>(() => EncodeWitnesses(fixture, [
            new TrRmInterfaceWitnessRow(0, 2, 0, 0, 0, 0, 0, 0),
        ], 0));
        Assert.Throws<InvalidDataException>(() => EncodeWitnesses(fixture, [
            new TrRmInterfaceWitnessRow(1, 0xF400_0008, 0, 0, 0, 0, 0, 0),
        ], 0));
        Assert.Throws<InvalidDataException>(() => EncodeWitnesses(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 0, 1, 0, 0, 0, 0),
        ], 1));
        Assert.Throws<InvalidDataException>(() => EncodeWitnesses(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], 1));
        Assert.Throws<InvalidDataException>(() => EncodeWitnesses(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 0, 0, 0, 0, 0, 0),
        ], 1));
        Assert.Throws<InvalidDataException>(() => EncodeWitnesses(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 0, 0, 1, 1, 0, 0),
        ], 0));

        Assert.Throws<InvalidDataException>(() => EncodeMembers(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], [
            new TrRmWitnessMemberRow(2, 2, 1, 1, 2),
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeMembers(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], [
            new TrRmWitnessMemberRow(1, 0, 1, 1, 2),
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeMembers(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], [
            new TrRmWitnessMemberRow(1, 2, 3, 1, 2),
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeMembers(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], [
            new TrRmWitnessMemberRow(1, 2, 1, 0xF400_0008, 2),
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeMembers(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], [
            new TrRmWitnessMemberRow(1, 2, 1, 1, 3),
        ]));
    }

    [Test]
    public void StructuralValidatorRejectsMalformedWitnessMemberRows()
    {
        var fixture = CreateFixture();
        var witnessBytes = EncodeWitnesses(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], 1);
        var memberBytes = EncodeMembers(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], [
            new TrRmWitnessMemberRow(1, 2, 1, 1, 2),
        ]);
        BinaryPrimitives.WriteUInt32LittleEndian(memberBytes.AsSpan(16), 3);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                fixture.CreateArtifact(witnessBytes, memberBytes)));
    }

    private static byte[] EncodeWitnesses(Fixture fixture,
        TrRmInterfaceWitnessRow[] rows, int memberCount)
    {
        var readers = CreateReaders(fixture);
        return TrRmInterfaceWitnessTableWriter.Encode(
            rows, readers.TypeSpecs, memberCount, 0, 0);
    }

    private static byte[] EncodeMembers(Fixture fixture,
        TrRmInterfaceWitnessRow[] witnessRows, TrRmWitnessMemberRow[] memberRows)
    {
        var readers = CreateReaders(fixture);
        var witnessBytes = TrRmInterfaceWitnessTableWriter.Encode(
            witnessRows, readers.TypeSpecs, memberRows.Length, 0, 0);
        var witnesses = new TrRmInterfaceWitnessTableReader(
            witnessBytes, witnessRows.Length, readers.TypeSpecs, memberRows.Length, 0, 0);
        return TrRmWitnessMemberTableWriter.Encode(
            memberRows, witnesses, readers.TypeSpecs, readers.Methods);
    }

    private static Fixture CreateFixture()
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Implementer", 0), ("IExample", 0),
            ("Requirement", 0), ("Implementation", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 6);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 2, 2, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 2, 2, 0, 0);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 2),
        ], 2, 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 2,
            2, 0, 0, 0);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 6, 0, 0, 0, 0, 1, 1, 0, 0),
            new TrRmTypeDefRow(1, 4, 14, 0, 0, 0, 0, 2, 1, 0, 0),
        ], strings, modules, 0, 0, 2, 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 2,
            strings, modules, 0, 0, 2, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
        ], typeDefs, targetProfiles, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 2, 0, 0, 0, 0),
            new TrRmMethodSigRow(1, 1, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(
            signatureBytes, 2, typeSpecs, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(2, 5, 1, 2, 0, 0, 0),
            new TrRmMethodDefRow(1, 6, 2, 2, 0, 0, 0),
        ], strings, modules, typeDefs, signatures, 0, 0);
        var methods = new TrRmMethodDefTableReader(methodBytes, 2,
            strings, modules, typeDefs, signatures, 0, 0);
        var generics = new TrRmGenericParamTableReader([], 0,
            strings, typeDefs, methods, signatures, typeSpecs, 0);
        var interfaceBytes = TrRmInterfaceDefTableWriter.Encode([
            new TrRmInterfaceDefRow(2, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0),
        ], typeDefs, generics, 0, 1, 0, 0, 0);
        var interfaces = new TrRmInterfaceDefTableReader(interfaceBytes, 1,
            typeDefs, generics, 0, 1, 0, 0, 0);
        var interfaceMethodBytes = TrRmInterfaceMethodTableWriter.Encode([
            new TrRmInterfaceMethodRow(1, 0, 1),
        ], interfaces, methods, signatures);
        return new Fixture(stringBytes, moduleBytes, typeSpecBytes,
            typeDefBytes, layoutBytes, signatureBytes, methodBytes,
            interfaceBytes, interfaceMethodBytes);
    }

    private static Readers CreateReaders(Fixture fixture)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 6);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 2, 2, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs,
            2, 2, 0, 0, 0);
        var typeDefs = new TrRmTypeDefTableReader(fixture.TypeDefs, 2,
            strings, modules, 0, 0, 2, 0);
        var signatures = new TrRmMethodSigTableReader(
            fixture.Signatures, 2, typeSpecs, 0, 0);
        var methods = new TrRmMethodDefTableReader(fixture.Methods, 2,
            strings, modules, typeDefs, signatures, 0, 0);
        return new Readers(typeSpecs, methods);
    }

    private sealed record Fixture(
        byte[] Strings, byte[] Modules, byte[] TypeSpecs, byte[] TypeDefs,
        byte[] Layouts, byte[] Signatures, byte[] Methods,
        byte[] InterfaceDefs, byte[] InterfaceMethods)
    {
        public byte[] CreateArtifact(byte[] witnesses, byte[] members)
            => TrRmArtifactWriter.Encode([
                new TrRmSection(0x0001, 0, 1, Modules),
                new TrRmSection(0x0002, 0, 6, Strings),
                new TrRmSection(0x0011, 0, 2, TypeSpecs),
                new TrRmSection(0x0013, 0, 2, TypeDefs),
                new TrRmSection(0x0015, 0, 2, Layouts),
                new TrRmSection(0x0016, 0, 2, Methods),
                new TrRmSection(0x0017, 0, 2, Signatures),
                new TrRmSection(0x0024, 0, 1, InterfaceMethods),
                new TrRmSection(0x0025, 0, 1, InterfaceDefs),
                new TrRmSection(0x0028, 0, (uint)(witnesses.Length / 32), witnesses),
                new TrRmSection(0x0029, 0, (uint)(members.Length / 20), members),
            ]);
    }

    private readonly ref struct Readers
    {
        public Readers(TrRmTypeSpecTableReader typeSpecs,
            TrRmMethodDefTableReader methods)
        {
            TypeSpecs = typeSpecs;
            Methods = methods;
        }

        public TrRmTypeSpecTableReader TypeSpecs { get; }
        public TrRmMethodDefTableReader Methods { get; }
    }
}
