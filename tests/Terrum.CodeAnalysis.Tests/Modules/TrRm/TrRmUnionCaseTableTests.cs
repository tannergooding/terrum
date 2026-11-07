// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmUnionCaseTableTests
{
    [Test]
    public void ValidatesUnionCasePayloadRangesAndCompleteFieldTypes()
    {
        var fixture = CreateFixture();
        var readers = CreateReaders(fixture);
        var cases = TrRmUnionCaseDefTableWriter.Encode([
            new TrRmUnionCaseDefRow(1, 0, 4, 0, 0),
            new TrRmUnionCaseDefRow(1, 1, 5, 1, 2),
        ], readers.Strings, readers.TypeDefs, 2);
        var caseReader = new TrRmUnionCaseDefTableReader(
            cases, 2, readers.Strings, readers.TypeDefs, 2);
        var fields = TrRmUnionCaseFieldTableWriter.Encode([
            new TrRmUnionCaseFieldRow(2, 0, 6, 0xF200_0008),
            new TrRmUnionCaseFieldRow(2, 1, 7, 1),
        ], readers.Strings, readers.TypeSpecs, caseReader);
        var fieldReader = new TrRmUnionCaseFieldTableReader(
            fields, 2, readers.Strings, readers.TypeSpecs, caseReader);

        Assert.That(caseReader.Count, Is.EqualTo(2));
        Assert.That(caseReader.GetPayloadRange(1), Is.EqualTo((0, 0)));
        Assert.That(caseReader.GetPayloadRange(2), Is.EqualTo((1, 2)));
        Assert.That(fieldReader.GetOwnerUnionCaseDef(2), Is.EqualTo(2));
        Assert.That(fieldReader.GetOrdinal(2), Is.EqualTo(1u));
        Assert.That(fieldReader.GetTypeSpecRef(1), Is.EqualTo(0xF200_0008u));

        TrRmStructuralMetadataValidator.Validate(
            fixture.CreateArtifact(cases, fields));
    }

    [Test]
    public void UnionCaseReadersChargePayloadOwnershipAtExactWorkBoundary()
    {
        var fixture = CreateFixture();
        var setup = CreateReaders(fixture);
        var caseBytes = TrRmUnionCaseDefTableWriter.Encode([
            new TrRmUnionCaseDefRow(1, 0, 4, 0, 0),
            new TrRmUnionCaseDefRow(1, 1, 5, 1, 2),
        ], setup.Strings, setup.TypeDefs, 2);
        var setupCases = new TrRmUnionCaseDefTableReader(
            caseBytes, 2, setup.Strings, setup.TypeDefs, 2);
        var fieldBytes = TrRmUnionCaseFieldTableWriter.Encode([
            new TrRmUnionCaseFieldRow(2, 0, 6, 0xF200_0008),
            new TrRmUnionCaseFieldRow(2, 1, 7, 1),
        ], setup.Strings, setup.TypeSpecs, setupCases);

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var readers = CreateReaders(fixture);
            var cases = new TrRmUnionCaseDefTableReader(caseBytes, 2,
                readers.Strings, readers.TypeDefs, 2, ledger);
            var fields = new TrRmUnionCaseFieldTableReader(fieldBytes, 2,
                readers.Strings, readers.TypeSpecs, cases, ledger);
            Assert.That(cases.GetPayloadRange(2), Is.EqualTo((1, 2)));
            Assert.That(fields.GetTypeSpecRef(2), Is.EqualTo(1u));
        });
    }

    [Test]
    public void RejectsMalformedCaseRangesOwnersAndFieldRows()
    {
        var fixture = CreateFixture();
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeCases(fixture, [
                new TrRmUnionCaseDefRow(0, 0, 4, 0, 0),
            ], 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeCases(fixture, [
                new TrRmUnionCaseDefRow(1, 0, 0, 0, 0),
            ], 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeCases(fixture, [
                new TrRmUnionCaseDefRow(1, 0, 4, 1, 1),
                new TrRmUnionCaseDefRow(1, 1, 5, 1, 1),
            ], 1));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeCases(fixture, [
                new TrRmUnionCaseDefRow(1, 0, 4, 0, 0),
                new TrRmUnionCaseDefRow(1, 1, 5, 1, 1),
            ], 2));

        _ = Assert.Throws<InvalidDataException>(() => EncodeFields(fixture, [
            new TrRmUnionCaseDefRow(1, 0, 4, 1, 1),
        ], [
            new TrRmUnionCaseFieldRow(2, 0, 6, 1),
        ]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeFields(fixture, [
            new TrRmUnionCaseDefRow(1, 0, 4, 1, 1),
        ], [
            new TrRmUnionCaseFieldRow(1, 1, 6, 1),
        ]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeFields(fixture, [
            new TrRmUnionCaseDefRow(1, 0, 4, 1, 1),
        ], [
            new TrRmUnionCaseFieldRow(1, 0, 0, 1),
        ]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeFields(fixture, [
            new TrRmUnionCaseDefRow(1, 0, 4, 1, 1),
        ], [
            new TrRmUnionCaseFieldRow(1, 0, 6, 0xF400_0008),
        ]));
    }

    [Test]
    public void StructuralValidatorRejectsMalformedUnionCaseRows()
    {
        var fixture = CreateFixture();
        var cases = EncodeCases(fixture, [
            new TrRmUnionCaseDefRow(1, 0, 4, 1, 1),
            new TrRmUnionCaseDefRow(1, 1, 5, 2, 1),
        ], 2);
        var fields = EncodeFields(fixture, [
            new TrRmUnionCaseDefRow(1, 0, 4, 1, 1),
            new TrRmUnionCaseDefRow(1, 1, 5, 2, 1),
        ], [
            new TrRmUnionCaseFieldRow(1, 0, 6, 1),
            new TrRmUnionCaseFieldRow(2, 0, 7, 1),
        ]);

        var malformedCases = (byte[])cases.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformedCases.AsSpan(12), 2);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                fixture.CreateArtifact(malformedCases, fields)));

        var malformedFields = (byte[])fields.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedFields.AsSpan(12), 0xF400_0008);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                fixture.CreateArtifact(cases, malformedFields)));
    }

    private static byte[] EncodeCases(Fixture fixture,
        TrRmUnionCaseDefRow[] rows, int payloadFieldCount)
    {
        var readers = CreateReaders(fixture);
        return TrRmUnionCaseDefTableWriter.Encode(
            rows, readers.Strings, readers.TypeDefs, payloadFieldCount);
    }

    private static byte[] EncodeFields(Fixture fixture,
        TrRmUnionCaseDefRow[] caseRows, TrRmUnionCaseFieldRow[] fieldRows)
    {
        var readers = CreateReaders(fixture);
        var cases = TrRmUnionCaseDefTableWriter.Encode(caseRows,
            readers.Strings, readers.TypeDefs, fieldRows.Length);
        var caseReader = new TrRmUnionCaseDefTableReader(cases,
            caseRows.Length, readers.Strings, readers.TypeDefs, fieldRows.Length);
        return TrRmUnionCaseFieldTableWriter.Encode(
            fieldRows, readers.Strings, readers.TypeSpecs, caseReader);
    }

    private static Fixture CreateFixture()
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Empty", 0),
            ("Pair", 0), ("Left", 0), ("Right", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 7);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 0, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 0, 0, 0);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 1, 0, 0, 0);

        _ = new TrRmTypeSpecTableReader(typeSpecBytes, 1,
            1, 0, 0, 0);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 6, 0, 0, 0, 0, 1, 1, 0, 0),
        ], strings, modules, 0, 0, 1, 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 1,
            strings, modules, 0, 0, 1, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], typeDefs, targetProfiles, 0);
        return new Fixture(stringBytes, moduleBytes, typeSpecBytes,
            typeDefBytes, layoutBytes);
    }

    private static Readers CreateReaders(Fixture fixture)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 7);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 1, 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs,
            1, 1, 0, 0, 0);
        var typeDefs = new TrRmTypeDefTableReader(fixture.TypeDefs, 1,
            strings, modules, 0, 0, 1, 0);
        return new Readers(strings, typeSpecs, typeDefs);
    }

    private sealed record Fixture(
        byte[] Strings, byte[] Modules, byte[] TypeSpecs,
        byte[] TypeDefs, byte[] Layouts)
    {
        public byte[] CreateArtifact(byte[] cases, byte[] fields)
            => TrRmArtifactWriter.Encode([
                new TrRmSection(0x0001, 0, 1, Modules),
                new TrRmSection(0x0002, 0, 7, Strings),
                new TrRmSection(0x0011, 0, 1, TypeSpecs),
                new TrRmSection(0x0013, 0, 1, TypeDefs),
                new TrRmSection(0x0015, 0, 1, Layouts),
                new TrRmSection(0x0034, 0,
                    (uint)(cases.Length / 20), cases),
                new TrRmSection(0x0035, 0,
                    (uint)(fields.Length / 16), fields),
            ]);
    }

    private readonly ref struct Readers
    {
        public Readers(TrRmStringTableReader strings,
            TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs)
        {
            Strings = strings;
            TypeSpecs = typeSpecs;
            TypeDefs = typeDefs;
        }

        public TrRmStringTableReader Strings { get; }
        public TrRmTypeSpecTableReader TypeSpecs { get; }
        public TrRmTypeDefTableReader TypeDefs { get; }
    }
}
