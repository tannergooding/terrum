// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmAssociatedTypeDefTableTests
{
    [Test]
    public void ValidatesInterfaceOwnershipSelfFormalAndCompleteDefault()
    {
        var fixture = CreateFixture();
        TrRmInterfaceDefRow[] interfaceRows =
        [
            new(1, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0),
        ];
        var interfaceBytes = CreateInterfaceDefs(fixture, interfaceRows, 1);
        TrRmAssociatedTypeDefRow[] associatedTypeRows = [new(1, 4, 1)];
        var associatedTypeBytes = CreateAssociatedTypeDefs(
            fixture, interfaceBytes, associatedTypeRows);
        var artifact = CreateArtifact(fixture, interfaceBytes, associatedTypeBytes);

        TrRmStructuralMetadataValidator.Validate(artifact);

        var (OwnerTypeDef, SelfGenericParam, AssociatedTypeFirst, AssociatedTypeCount, AssociatedTypeOwner, AssociatedTypeName, DefaultTypeSpecRef) = ReadTables(
            fixture, interfaceBytes, associatedTypeBytes);
        Assert.That(OwnerTypeDef, Is.EqualTo(1));
        Assert.That(SelfGenericParam, Is.EqualTo(1));
        Assert.That((AssociatedTypeFirst, AssociatedTypeCount),
            Is.EqualTo((1, 1)));
        Assert.That(AssociatedTypeOwner, Is.EqualTo(1));
        Assert.That(AssociatedTypeName, Is.EqualTo(4));
        Assert.That(DefaultTypeSpecRef, Is.EqualTo(1u));
    }

    [Test]
    public void RejectsOrphanedRangesWrongOwnersAndInvalidDefaults()
    {
        var fixture = CreateFixture();
        var interfaceRow = new TrRmInterfaceDefRow(1, 1, 0, 0, 0, 0, 1, 1,
            0, 0, 0, 0);
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateInterfaceDefs(CreateFixture(typeFlags: 4), [interfaceRow], 1));
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateInterfaceDefs(fixture, [interfaceRow with { SelfGenericParamRow = 2 }], 1));
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateInterfaceDefs(fixture, [interfaceRow with {
                AssociatedTypeFirst = 0,
                AssociatedTypeCount = 0,
            }], 1));

        var interfaceBytes = CreateInterfaceDefs(fixture, [interfaceRow], 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateAssociatedTypeDefs(fixture, interfaceBytes, [new(2, 4, 1)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateAssociatedTypeDefs(fixture, interfaceBytes, [new(1, 0, 1)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateAssociatedTypeDefs(fixture, interfaceBytes, [new(1, 4, 0xFF00_0001)]));
    }

    [Test]
    public void StructuralValidatorRejectsMalformedInterfaceAndAssociatedRows()
    {
        var fixture = CreateFixture();
        var interfaceBytes = CreateInterfaceDefs(fixture, [
            new TrRmInterfaceDefRow(1, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0),
        ], 1);
        var associatedTypeBytes = CreateAssociatedTypeDefs(fixture,
            interfaceBytes, [new(1, 4, 1)]);

        var malformedInterface = (byte[])interfaceBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedInterface.AsSpan(sizeof(uint)), 2);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateArtifact(fixture, malformedInterface, associatedTypeBytes)));

        var malformedAssociatedType = (byte[])associatedTypeBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformedAssociatedType, 2);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateArtifact(fixture, interfaceBytes, malformedAssociatedType)));

        malformedAssociatedType = (byte[])associatedTypeBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedAssociatedType.AsSpan(2 * sizeof(uint)), 0xFF00_0001);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateArtifact(fixture, interfaceBytes, malformedAssociatedType)));
    }

    private static Fixture CreateFixture(uint typeFlags = 12)
    {
        var stringBytes = TrRmStringTableWriter.EncodeNames([
            ("Module", 0), ("Interface", 0), ("Self", 0), ("Item", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 4);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 0, 0);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Reference(0x04, 1),
        ], 1, 0, 1, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1, 1, 0, 1, 0);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 2, typeFlags,
                0, 0, 1, 1, 1, 1, 0, 0),
        ], strings, modules, 0, 1, 1, 0);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 1,
            strings, modules, 0, 1, 1, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var genericParamBytes = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1300_0001, 0, 3, 0, 0, 1, 1, 1, 0, 0),
        ], strings, types, methods, signatures, typeSpecs, 0);
        return new Fixture(stringBytes, moduleBytes, typeSpecBytes,
            typeDefBytes, layoutBytes, genericParamBytes);
    }

    private static byte[] CreateInterfaceDefs(Fixture fixture,
        IReadOnlyList<TrRmInterfaceDefRow> rows, int associatedTypeCount)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 4);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 1, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs, 1, 1, 0, 1, 0);
        var types = new TrRmTypeDefTableReader(fixture.TypeDefs, 1,
            strings, modules, 0, 1, 1, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var formals = new TrRmGenericParamTableReader(fixture.GenericParams, 1,
            strings, types, methods, signatures, typeSpecs, 0);
        return TrRmInterfaceDefTableWriter.Encode(rows, types, formals,
            0, 0, associatedTypeCount, 0, 0);
    }

    private static byte[] CreateAssociatedTypeDefs(Fixture fixture,
        byte[] interfaceBytes, TrRmAssociatedTypeDefRow[] rows)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 4);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 1, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs, 1, 1, 0, 1, 0);
        var types = new TrRmTypeDefTableReader(fixture.TypeDefs, 1,
            strings, modules, 0, 1, 1, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var formals = new TrRmGenericParamTableReader(fixture.GenericParams, 1,
            strings, types, methods, signatures, typeSpecs, 0);
        var interfaces = new TrRmInterfaceDefTableReader(interfaceBytes, 1,
            types, formals, 0, 0, rows.Length, 0, 0);
        return TrRmAssociatedTypeDefTableWriter.Encode(rows,
            strings, typeSpecs, interfaces);
    }

    private static byte[] CreateArtifact(Fixture fixture,
        byte[] interfaceBytes, byte[] associatedTypeBytes)
        => TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, fixture.Modules),
            new TrRmSection(0x0002, 0, 4, fixture.Strings),
            new TrRmSection(0x0011, 0, 1, fixture.TypeSpecs),
            new TrRmSection(0x0013, 0, 1, fixture.TypeDefs),
            new TrRmSection(0x0015, 0, 1, fixture.Layouts),
            new TrRmSection(0x0019, 0, 1, fixture.GenericParams),
            new TrRmSection(0x0025, 0, 1, interfaceBytes),
            new TrRmSection(0x0027, 0, 1, associatedTypeBytes),
        ]);

    private static (int OwnerTypeDef, int SelfGenericParam,
        int AssociatedTypeFirst, int AssociatedTypeCount, int AssociatedTypeOwner,
        int AssociatedTypeName, uint DefaultTypeSpecRef) ReadTables(
        Fixture fixture, byte[] interfaceBytes, byte[] associatedTypeBytes)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 4);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 1, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs, 1, 1, 0, 1, 0);
        var types = new TrRmTypeDefTableReader(fixture.TypeDefs, 1,
            strings, modules, 0, 1, 1, 0);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = new TrRmMethodDefTableReader([], 0,
            strings, modules, types, signatures, 0, 0);
        var formals = new TrRmGenericParamTableReader(fixture.GenericParams, 1,
            strings, types, methods, signatures, typeSpecs, 0);
        var interfaces = new TrRmInterfaceDefTableReader(interfaceBytes, 1,
            types, formals, 0, 0, 1, 0, 0);
        var associatedTypes = new TrRmAssociatedTypeDefTableReader(
            associatedTypeBytes, 1, strings, typeSpecs, interfaces);
        var (First, Count) = interfaces.GetAssociatedTypeRange(1);
        return (interfaces.GetOwnerTypeDef(1), interfaces.GetSelfGenericParam(1),
            First, Count, interfaces.GetAssociatedTypeOwner(1),
            associatedTypes.GetNameStringRow(1),
            associatedTypes.GetDefaultTypeSpecRef(1));
    }

    private sealed record Fixture(
        byte[] Strings, byte[] Modules, byte[] TypeSpecs, byte[] TypeDefs,
        byte[] Layouts, byte[] GenericParams);
}
