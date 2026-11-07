// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmInterfaceWitnessTableTests
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
    public void InterfaceWitnessAndMemberReadersChargeAtExactWorkBoundary()
    {
        var fixture = CreateFixture();
        var setup = CreateReaders(fixture);
        var witnessBytes = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], setup.TypeSpecs, 1, 0, 0);
        var setupWitnesses = new TrRmInterfaceWitnessTableReader(
            witnessBytes, 1, setup.TypeSpecs, 1, 0, 0);
        var memberBytes = TrRmWitnessMemberTableWriter.Encode([
            new TrRmWitnessMemberRow(1, 2, 1, 1, 2),
        ], setupWitnesses, setup.TypeSpecs, setup.Methods);

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var readers = CreateReaders(fixture);
            var witnesses = new TrRmInterfaceWitnessTableReader(
                witnessBytes, 1, readers.TypeSpecs, 1, 0, 0, ledger);
            var members = new TrRmWitnessMemberTableReader(memberBytes, 1,
                witnesses, readers.TypeSpecs, readers.Methods, ledger);
            Assert.That(witnesses.GetMemberRange(1), Is.EqualTo((1, 1)));
            Assert.That(members.GetSelectedMethodDef(1), Is.EqualTo(2));
        });
    }

    [Test]
    public void WitnessAssociatedTypeReaderChargesClosureAtExactWorkBoundary()
    {
        var fixture = CreateFixture(includeAssociatedType: true,
            defaultTypeSpecRef: 1, includeBaseInterface: true);
        var setup = CreateReaders(fixture);
        var witnessBytes = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(1, 2, 0, 0, 1, 1, 0, 0),
        ], setup.TypeSpecs, 0, 1, 0);
        var setupWitnesses = new TrRmInterfaceWitnessTableReader(
            witnessBytes, 1, setup.TypeSpecs, 0, 1, 0);
        var associatedTypeBytes
            = TrRmWitnessAssociatedTypeTableWriter.Encode([
                new TrRmWitnessAssociatedTypeRow(1, 3, 0x2000_0001, 1, 1),
            ], setupWitnesses, setup.TypeSpecs, setup.TypeDefs,
                setup.Interfaces, setup.InterfaceBases,
                setup.AssociatedTypeDefs, default);

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var readers = CreateReaders(fixture);
            var witnesses = new TrRmInterfaceWitnessTableReader(
                witnessBytes, 1, readers.TypeSpecs, 0, 1, 0, ledger);
            var associatedTypes = new TrRmWitnessAssociatedTypeTableReader(
                associatedTypeBytes, 1, witnesses, readers.TypeSpecs,
                readers.TypeDefs, readers.Interfaces, readers.InterfaceBases,
                readers.AssociatedTypeDefs, default, ledger);
            Assert.That(associatedTypes.GetBindingOrigin(1),
                Is.EqualTo(1u));
        });
    }

    [Test]
    public void RejectsInvalidWitnessRangesTypesAndMemberReferences()
    {
        var fixture = CreateFixture();
        _ = Assert.Throws<InvalidDataException>(() => EncodeWitnesses(fixture, [
            new TrRmInterfaceWitnessRow(0, 2, 0, 0, 0, 0, 0, 0),
        ], 0));
        _ = Assert.Throws<InvalidDataException>(() => EncodeWitnesses(fixture, [
            new TrRmInterfaceWitnessRow(1, 0xF400_0008, 0, 0, 0, 0, 0, 0),
        ], 0));
        _ = Assert.Throws<InvalidDataException>(() => EncodeWitnesses(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 0, 1, 0, 0, 0, 0),
        ], 1));
        _ = Assert.Throws<InvalidDataException>(() => EncodeWitnesses(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], 1));
        _ = Assert.Throws<InvalidDataException>(() => EncodeWitnesses(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 0, 0, 0, 0, 0, 0),
        ], 1));
        _ = Assert.Throws<InvalidDataException>(() => EncodeWitnesses(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 0, 0, 1, 1, 0, 0),
        ], 0));

        _ = Assert.Throws<InvalidDataException>(() => EncodeMembers(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], [
            new TrRmWitnessMemberRow(2, 2, 1, 1, 2),
        ]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeMembers(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], [
            new TrRmWitnessMemberRow(1, 0, 1, 1, 2),
        ]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeMembers(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], [
            new TrRmWitnessMemberRow(1, 2, 3, 1, 2),
        ]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeMembers(fixture, [
            new TrRmInterfaceWitnessRow(1, 2, 1, 1, 0, 0, 0, 0),
        ], [
            new TrRmWitnessMemberRow(1, 2, 1, 0xF400_0008, 2),
        ]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeMembers(fixture, [
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
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                fixture.CreateArtifact(witnessBytes, memberBytes)));
    }

    [Test]
    public void ValidatesWitnessAssociatedTypeRowsAndSelectionOrigins()
    {
        var fixture = CreateFixture(includeAssociatedType: true,
            defaultTypeSpecRef: 1, includeBaseInterface: true);
        var readers = CreateReaders(fixture);
        var witnessBytes = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(1, 2, 0, 0, 1, 1, 0, 0),
        ], readers.TypeSpecs, 0, 1, 0);
        var witnesses = new TrRmInterfaceWitnessTableReader(
            witnessBytes, 1, readers.TypeSpecs, 0, 1, 0);
        var rows = TrRmWitnessAssociatedTypeTableWriter.Encode([
            new TrRmWitnessAssociatedTypeRow(1, 3, 0x2000_0001, 1,
                1),
        ], witnesses, readers.TypeSpecs, readers.TypeDefs, readers.Interfaces,
            readers.InterfaceBases, readers.AssociatedTypeDefs, default);
        var reader = new TrRmWitnessAssociatedTypeTableReader(rows, 1,
            witnesses, readers.TypeSpecs, readers.TypeDefs, readers.Interfaces,
            readers.InterfaceBases, readers.AssociatedTypeDefs, default);

        Assert.That(reader.GetWitnessRow(1), Is.EqualTo(1));
        Assert.That(reader.GetDeclaringInterfaceTypeSpecRef(1), Is.EqualTo(3u));
        Assert.That(reader.GetAssociatedTypeDefReference(1), Is.EqualTo(0x2000_0001u));
        Assert.That(reader.GetValueTypeSpecRef(1), Is.EqualTo(1u));
        Assert.That(reader.GetBindingOrigin(1), Is.EqualTo(1u));
        TrRmStructuralMetadataValidator.Validate(
            fixture.CreateArtifact(witnessBytes, [], rows));

        var defaultRows = TrRmWitnessAssociatedTypeTableWriter.Encode([
            new TrRmWitnessAssociatedTypeRow(1, 3, 0x2000_0001, 1,
                2),
        ], witnesses, readers.TypeSpecs, readers.TypeDefs, readers.Interfaces,
            readers.InterfaceBases, readers.AssociatedTypeDefs, default);
        Assert.That(defaultRows.Length, Is.EqualTo(20));
    }

    [Test]
    public void RejectsMalformedWitnessAssociatedTypeRowsAndSelectionOrigins()
    {
        var fixture = CreateFixture(includeAssociatedType: true,
            includeBaseInterface: true);
        var readers = CreateReaders(fixture);
        var witnessBytes = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(1, 2, 0, 0, 1, 1, 0, 0),
        ], readers.TypeSpecs, 0, 1, 0);
        var witnesses = new TrRmInterfaceWitnessTableReader(
            witnessBytes, 1, readers.TypeSpecs, 0, 1, 0);
        static TrRmWitnessAssociatedTypeRow ValidRow(uint origin)
        {
            return new(1, 3, 0x2000_0001, 1, origin);
        }

        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeWitnessAssociatedTypes(fixture, witnessBytes, [
                ValidRow(1) with { WitnessRow = 2 },
            ]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeWitnessAssociatedTypes(fixture, witnessBytes, [
                ValidRow(1) with { DeclaringInterfaceTypeSpecRef = 1 },
            ]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeWitnessAssociatedTypes(fixture, witnessBytes, [
                ValidRow(1) with { AssociatedTypeDefReference = 0x1300_0001 },
            ]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeWitnessAssociatedTypes(fixture, witnessBytes, [ValidRow(3)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeWitnessAssociatedTypes(fixture, witnessBytes, [ValidRow(0)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeWitnessAssociatedTypes(fixture, witnessBytes, [
                ValidRow(1) with { DeclaringInterfaceTypeSpecRef = 0xF200_0008 },
            ]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeWitnessAssociatedTypes(fixture, witnessBytes, [
                ValidRow(1) with { AssociatedTypeDefReference = 0x1C00_0001 },
            ]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeWitnessAssociatedTypes(fixture, witnessBytes, [
                ValidRow(1) with { ValueTypeSpecRef = 0 },
            ]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeWitnessAssociatedTypes(fixture, witnessBytes, [ValidRow(2)]));

        var validRows = TrRmWitnessAssociatedTypeTableWriter.Encode([
            ValidRow(1),
        ], witnesses, readers.TypeSpecs, readers.TypeDefs, readers.Interfaces,
            readers.InterfaceBases, readers.AssociatedTypeDefs, default);
        BinaryPrimitives.WriteUInt32LittleEndian(validRows.AsSpan(16), 3);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                fixture.CreateArtifact(witnessBytes, [], validRows)));
    }

    [Test]
    public void RejectsAssociatedTypeDeclaredOutsideWitnessInterfaceClosure()
    {
        var fixture = CreateFixture(includeAssociatedType: true);
        var readers = CreateReaders(fixture);
        var witnessBytes = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(1, 2, 0, 0, 1, 1, 0, 0),
        ], readers.TypeSpecs, 0, 1, 0);
        var malformedRows = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(malformedRows, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(malformedRows.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(malformedRows.AsSpan(8), 0x2000_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(malformedRows.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(malformedRows.AsSpan(16), 1);

        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeWitnessAssociatedTypes(fixture, witnessBytes, [
                new TrRmWitnessAssociatedTypeRow(1, 3, 0x2000_0001, 1, 1),
            ]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                fixture.CreateArtifact(witnessBytes, [], malformedRows)));
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

    private static byte[] EncodeWitnessAssociatedTypes(Fixture fixture,
        byte[] witnessBytes, TrRmWitnessAssociatedTypeRow[] rows)
    {
        var readers = CreateReaders(fixture);
        var witnesses = new TrRmInterfaceWitnessTableReader(
            witnessBytes, witnessBytes.Length / 32, readers.TypeSpecs, 0, rows.Length, 0);
        return TrRmWitnessAssociatedTypeTableWriter.Encode(rows, witnesses,
            readers.TypeSpecs, readers.TypeDefs, readers.Interfaces,
            readers.InterfaceBases, readers.AssociatedTypeDefs, default);
    }

    private static Fixture CreateFixture(bool includeAssociatedType = false,
        uint defaultTypeSpecRef = 0, bool includeBaseInterface = false)
    {
        var hasBaseInterface = includeAssociatedType || includeBaseInterface;
        var typeDefCount = hasBaseInterface ? 3 : 2;
        var stringRows = new List<(string Text, int Encoding)>
        {
            ("", 0), ("Module", 0), ("Implementer", 0), ("IExample", 0),
            ("Requirement", 0), ("Implementation", 0),
        };
        if (hasBaseInterface)
        {
            stringRows.Add(("IBase", 0));
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var stringCount = hasBaseInterface ? 7 : 6;
        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, typeDefCount, 2, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, typeDefCount, 2, 0, 0);
        var typeSpecRows = new List<ReadOnlyMemory<byte>>
        {
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 2),
        };
        if (hasBaseInterface)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02, 3));
        }

        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(
            typeSpecRows, typeDefCount, 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, typeDefCount, 0, 0, 0);
        var typeDefRows = new List<TrRmTypeDefRow>
        {
            new TrRmTypeDefRow(1, 3, 6, 0, 0, 0, 0, 1, 1, 0, 0),
            new TrRmTypeDefRow(1, 4, 14, 0, 0, 0, 0, 2, 1, 0, 0),
        };
        if (hasBaseInterface)
        {
            typeDefRows.Add(new TrRmTypeDefRow(1, 7, 14,
                0, 0, 0, 0, 3, 1, 0, 0));
        }

        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, 0, 0, typeDefCount, 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules, 0, 0, typeDefCount, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutRows = new List<TrRmLayoutRow>
        {
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
        };
        if (hasBaseInterface)
        {
            layoutRows.Add(new TrRmLayoutRow(3, 0, 0, 0, 0, 0, 0, 0));
        }

        var layoutBytes = TrRmLayoutTableWriter.Encode(
            layoutRows, typeDefs, targetProfiles, 0);
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
        var associatedTypeCount = includeAssociatedType ? 1 : 0;
        var baseCount = includeBaseInterface ? 1 : 0;
        var interfaceRows = hasBaseInterface
            ? new[]
            {
                new TrRmInterfaceDefRow(2, 0, baseCount == 0 ? 0 : 1,
                    baseCount, 1, 1, 0, 0, 0, 0, 0, 0),
                new TrRmInterfaceDefRow(3, 0, 0, 0, 0, 0,
                    associatedTypeCount == 0 ? 0 : 1, associatedTypeCount,
                    0, 0, 0, 0),
            }
            :
            [
                new TrRmInterfaceDefRow(2, 0, 0, 0, 1, 1,
                    associatedTypeCount == 0 ? 0 : 1, associatedTypeCount,
                    0, 0, 0, 0),
            ];
        var interfaceBytes = TrRmInterfaceDefTableWriter.Encode(interfaceRows,
            typeDefs, generics, baseCount, 1, associatedTypeCount, 0, 0);
        var interfaces = new TrRmInterfaceDefTableReader(interfaceBytes,
            interfaceRows.Length, typeDefs, generics, baseCount, 1,
            associatedTypeCount, 0, 0);
        var interfaceBaseBytes = TrRmInterfaceBaseTableWriter.Encode(
            includeBaseInterface
                ? [new TrRmInterfaceBaseRow(1, 0, 3)]
                : [],
            typeSpecs, interfaces);
        var associatedTypeDefBytes = TrRmAssociatedTypeDefTableWriter.Encode(
            includeAssociatedType
                ? [new TrRmAssociatedTypeDefRow(
                    hasBaseInterface ? 2 : 1, 5, defaultTypeSpecRef)]
                : [],
            strings, typeSpecs, interfaces);
        var interfaceMethodBytes = TrRmInterfaceMethodTableWriter.Encode([
            new TrRmInterfaceMethodRow(1, 0, 1),
        ], interfaces, methods, signatures);
        return new Fixture(stringBytes, stringCount, moduleBytes, typeSpecBytes,
            typeDefBytes, layoutBytes, signatureBytes, methodBytes,
            interfaceBytes, interfaceBaseBytes, associatedTypeDefBytes,
            interfaceMethodBytes);
    }

    private static Readers CreateReaders(Fixture fixture)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, fixture.StringCount);
        var typeDefCount = fixture.TypeDefs.Length / 44;
        var interfaceCount = fixture.InterfaceDefs.Length / 48;
        var interfaceBaseCount = fixture.InterfaceBases.Length / 12;
        var associatedTypeCount = fixture.AssociatedTypeDefs.Length / 12;
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, typeDefCount, 2, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs,
            interfaceCount + 1, typeDefCount, 0, 0, 0);
        var typeDefs = new TrRmTypeDefTableReader(fixture.TypeDefs, typeDefCount,
            strings, modules, 0, 0, fixture.Layouts.Length / 28, 0);
        var signatures = new TrRmMethodSigTableReader(
            fixture.Signatures, 2, typeSpecs, 0, 0);
        var methods = new TrRmMethodDefTableReader(fixture.Methods, 2,
            strings, modules, typeDefs, signatures, 0, 0);
        var generics = new TrRmGenericParamTableReader([], 0,
            strings, typeDefs, methods, signatures, typeSpecs, 0);
        var interfaces = new TrRmInterfaceDefTableReader(fixture.InterfaceDefs,
            interfaceCount, typeDefs, generics, interfaceBaseCount, 1,
            associatedTypeCount, 0, 0);
        var interfaceBases = new TrRmInterfaceBaseTableReader(
            fixture.InterfaceBases, interfaceBaseCount, typeSpecs, interfaces);
        var associatedTypeDefs = new TrRmAssociatedTypeDefTableReader(
            fixture.AssociatedTypeDefs, associatedTypeCount, strings, typeSpecs, interfaces);
        return new Readers(typeSpecs, typeDefs, methods, interfaces,
            interfaceBases, associatedTypeDefs);
    }

    private sealed record Fixture(
        byte[] Strings, int StringCount, byte[] Modules, byte[] TypeSpecs, byte[] TypeDefs,
        byte[] Layouts, byte[] Signatures, byte[] Methods,
        byte[] InterfaceDefs, byte[] InterfaceBases, byte[] AssociatedTypeDefs,
        byte[] InterfaceMethods)
    {
        public byte[] CreateArtifact(byte[] witnesses, byte[] members,
            byte[]? witnessAssociatedTypes = null)
        {
            var sections = new System.Collections.Generic.List<TrRmSection>
            {
                new(0x0001, 0, 1, Modules),
                new(0x0002, 0, (uint)StringCount, Strings),
                new(0x0011, 0, (uint)((InterfaceDefs.Length / 48) + 1), TypeSpecs),
                new(0x0013, 0, (uint)(TypeDefs.Length / 44), TypeDefs),
                new(0x0015, 0, (uint)(Layouts.Length / 28), Layouts),
                new(0x0016, 0, 2, Methods),
                new(0x0017, 0, 2, Signatures),
                new(0x0024, 0, 1, InterfaceMethods),
                new(0x0025, 0, (uint)(InterfaceDefs.Length / 48), InterfaceDefs),
            };
            if (InterfaceBases.Length != 0)
            {
                sections.Add(new TrRmSection(0x0026, 0,
                    (uint)(InterfaceBases.Length / 12), InterfaceBases));
            }
            if (AssociatedTypeDefs.Length != 0)
            {
                sections.Add(new TrRmSection(0x0027, 0,
                    (uint)(AssociatedTypeDefs.Length / 12), AssociatedTypeDefs));
            }
            if (witnesses.Length != 0)
            {
                sections.Add(new TrRmSection(0x0028, 0,
                    (uint)(witnesses.Length / 32), witnesses));
            }
            if (members.Length != 0)
            {
                sections.Add(new TrRmSection(0x0029, 0,
                    (uint)(members.Length / 20), members));
            }
            if (witnessAssociatedTypes is { Length: > 0 })
            {
                sections.Add(new TrRmSection(0x002A, 0,
                    (uint)(witnessAssociatedTypes.Length / 20), witnessAssociatedTypes));
            }

            return TrRmArtifactWriter.Encode(sections);
        }
    }

    private readonly ref struct Readers
    {
        public Readers(TrRmTypeSpecTableReader typeSpecs,
            TrRmTypeDefTableReader typeDefs, TrRmMethodDefTableReader methods,
            TrRmInterfaceDefTableReader interfaces,
            TrRmInterfaceBaseTableReader interfaceBases,
            TrRmAssociatedTypeDefTableReader associatedTypeDefs)
        {
            TypeSpecs = typeSpecs;
            TypeDefs = typeDefs;
            Methods = methods;
            Interfaces = interfaces;
            InterfaceBases = interfaceBases;
            AssociatedTypeDefs = associatedTypeDefs;
        }

        public TrRmTypeSpecTableReader TypeSpecs { get; }
        public TrRmTypeDefTableReader TypeDefs { get; }
        public TrRmMethodDefTableReader Methods { get; }
        public TrRmInterfaceDefTableReader Interfaces { get; }
        public TrRmInterfaceBaseTableReader InterfaceBases { get; }
        public TrRmAssociatedTypeDefTableReader AssociatedTypeDefs { get; }
    }
}
