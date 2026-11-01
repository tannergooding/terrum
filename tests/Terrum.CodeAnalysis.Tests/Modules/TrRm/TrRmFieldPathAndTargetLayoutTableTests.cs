// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmFieldPathAndTargetLayoutTableTests
{
    [Test]
    public void ReadsAndWritesFieldPathsAndExclusiveLayoutObservationRanges()
    {
        var fixture = CreateFixture();
        var readers = CreateReaders(fixture);
        var fieldPaths = TrRmFieldPathTableWriter.Encode([
            new TrRmFieldPathRow(1, [0x1400_0001, 0x1C00_0002]),
        ], readers.TypeSpecs, readers.Fields, readers.Imports);
        var fieldPathReader = new TrRmFieldPathTableReader(fieldPaths, 1,
            readers.TypeSpecs, readers.Fields, readers.Imports);
        var fieldReferences = TrRmTargetLayoutDependencyFieldTableWriter.Encode([
            0x1400_0001, 0x1C00_0002,
        ], readers.Fields, readers.Imports);
        var dependencyBytes = TrRmTargetLayoutDependencyTableWriter.Encode([
            new TrRmTargetLayoutDependencyRow(1, 0, 0, 1, 1, 0, 0),
            new TrRmTargetLayoutDependencyRow(3, 0, 0, 1, 1, 1, 2),
        ], readers.TypeSpecs, readers.TargetProfiles, 2);
        var dependencies = new TrRmTargetLayoutDependencyTableReader(
            dependencyBytes, 2, readers.TypeSpecs, readers.TargetProfiles, 2);

        Assert.That(fieldPathReader.Count, Is.EqualTo(1));
        Assert.That(fieldPathReader.GetRootTypeSpecRef(1), Is.EqualTo(1u));
        Assert.That(fieldPathReader.GetFieldCount(1), Is.EqualTo(2));
        Assert.That(fieldPathReader.GetFieldReference(1, 1), Is.EqualTo(0x1C00_0002u));
        Assert.That(dependencies.GetQueryKind(1), Is.EqualTo(1));
        Assert.That(dependencies.GetTargetProfileRow(2), Is.EqualTo(1));
        Assert.That(dependencies.GetFieldRange(2), Is.EqualTo((1, 2)));

        TrRmStructuralMetadataValidator.Validate(fixture.CreateArtifact(
            dependencyBytes, fieldReferences, fieldPaths));
    }

    [Test]
    public void TargetLayoutReadersChargeRowsAndStateAtExactWorkBoundaries()
    {
        var fixture = CreateFixture();
        var readers = CreateReaders(fixture);

        var exactFieldLedger = new TrRmA3LayoutResourceLedger();
        exactFieldLedger.ConsumeWork(
            TrRmA3LayoutResourceLedger.WorkLimit - 15);
        var exactFields = new TrRmTargetLayoutDependencyFieldTableReader(
            fixture.DependencyFieldBytes, 2, readers.Fields, readers.Imports,
            exactFieldLedger);
        Assert.That(exactFields.GetFieldReference(2),
            Is.EqualTo(0x1C00_0002u));
        Assert.That(exactFieldLedger.WorkRemaining, Is.Zero);

        var shortFieldLedger = new TrRmA3LayoutResourceLedger();
        shortFieldLedger.ConsumeWork(
            TrRmA3LayoutResourceLedger.WorkLimit - 14);
        var shortFields = new TrRmTargetLayoutDependencyFieldTableReader(
            fixture.DependencyFieldBytes, 2, readers.Fields, readers.Imports,
            shortFieldLedger);
        TrRmResourceLimitException? fieldException = null;
        try
        {
            _ = shortFields.GetFieldReference(2);
        }
        catch (TrRmResourceLimitException caught)
        {
            fieldException = caught;
        }

        Assert.That(fieldException!.Required,
            Is.EqualTo(new System.Numerics.BigInteger(
            TrRmA3LayoutResourceLedger.WorkLimit + 1)));

        var exactDependencyLedger = new TrRmA3LayoutResourceLedger();
        exactDependencyLedger.ConsumeWork(
            TrRmA3LayoutResourceLedger.WorkLimit - 85);
        var exactDependencies = new TrRmTargetLayoutDependencyTableReader(
            fixture.DependencyBytes, 2, readers.TypeSpecs,
            readers.TargetProfiles, 2, exactDependencyLedger);
        Assert.That(exactDependencies.GetQueryKind(1), Is.EqualTo((byte)1));
        Assert.That(exactDependencies.GetTargetProfileRow(2), Is.EqualTo(1));
        Assert.That(exactDependencies.GetFieldRange(2), Is.EqualTo((1, 2)));
        Assert.That(exactDependencyLedger.WorkRemaining, Is.Zero);

        var shortDependencyLedger = new TrRmA3LayoutResourceLedger();
        shortDependencyLedger.ConsumeWork(
            TrRmA3LayoutResourceLedger.WorkLimit - 84);
        var shortDependencies = new TrRmTargetLayoutDependencyTableReader(
            fixture.DependencyBytes, 2, readers.TypeSpecs,
            readers.TargetProfiles, 2, shortDependencyLedger);
        TrRmResourceLimitException? dependencyException = null;
        try
        {
            _ = shortDependencies.GetQueryKind(1);
            _ = shortDependencies.GetTargetProfileRow(2);
            _ = shortDependencies.GetFieldRange(2);
        }
        catch (TrRmResourceLimitException caught)
        {
            dependencyException = caught;
        }

        Assert.That(dependencyException!.Required,
            Is.EqualTo(new System.Numerics.BigInteger(
            TrRmA3LayoutResourceLedger.WorkLimit + 1)));
    }

    [Test]
    public void FieldPathReaderChargesRowsAndFieldsAtTheExactWorkBoundary()
    {
        var fixture = CreateFixture();
        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var readers = CreateReaders(fixture);
            var reader = new TrRmFieldPathTableReader(
                fixture.FieldPathBytes, 1, readers.TypeSpecs, readers.Fields,
                readers.Imports, ledger);
            Assert.That(reader.GetFieldReference(1, 1),
                Is.EqualTo(0x1C00_0002u));
        });
    }

    [Test]
    public void RejectsInvalidFieldReferencesAndFieldPathShapes()
    {
        var fixture = CreateFixture();
        Assert.Throws<InvalidDataException>(() =>
            EncodeFieldReferences(fixture, 0x1D00_0001));
        Assert.Throws<InvalidDataException>(() =>
            EncodeFieldReferences(fixture, 0x1C00_0001));
        Assert.Throws<InvalidDataException>(() =>
            EncodeFieldPath(fixture, new TrRmFieldPathRow(0, [0x1400_0001])));
        Assert.Throws<InvalidDataException>(() =>
            EncodeFieldPath(fixture, new TrRmFieldPathRow(1, [0x1400_0003])));
        Assert.Throws<InvalidDataException>(() =>
            EncodeFieldPath(fixture, new TrRmFieldPathRow(1, [])));

        var malformedPath = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(malformedPath.AsSpan(4), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(malformedPath.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(malformedPath.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(malformedPath.AsSpan(16), 0x1D00_0001);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(fixture.CreateArtifact(
                fixture.DependencyBytes, fixture.DependencyFieldBytes,
                malformedPath, fieldPathCount: 1)));
    }

    [Test]
    public void RejectsInvalidQueryRangesProfilesAndFieldRangeOwnership()
    {
        var fixture = CreateFixture();
        Assert.Throws<InvalidDataException>(() => EncodeDependencies(fixture, [
            new TrRmTargetLayoutDependencyRow(1, 0, 0, 1, 1, 1, 1),
        ], 1));
        Assert.Throws<InvalidDataException>(() => EncodeDependencies(fixture, [
            new TrRmTargetLayoutDependencyRow(3, 0, 0, 1, 1, 0, 0),
        ], 0));
        Assert.Throws<InvalidDataException>(() => EncodeDependencies(fixture, [
            new TrRmTargetLayoutDependencyRow(1, 0, 0, 1, 2, 0, 0),
        ], 0));
        Assert.Throws<InvalidDataException>(() => EncodeDependencies(fixture, [
            new TrRmTargetLayoutDependencyRow(3, 0, 0, 1, 1, 1, 1),
            new TrRmTargetLayoutDependencyRow(4, 0, 0, 1, 1, 1, 1),
        ], 1));
        Assert.Throws<InvalidDataException>(() => EncodeDependencies(fixture, [
            new TrRmTargetLayoutDependencyRow(3, 0, 0, 1, 1, 1, 1),
        ], 2));
        Assert.Throws<InvalidDataException>(() => EncodeDependencies(fixture, [
            new TrRmTargetLayoutDependencyRow(5, 0, 0, 1, 1, 0, 0),
        ], 0));
    }

    [Test]
    public void StructuralValidatorRejectsMalformedLayoutObservations()
    {
        var fixture = CreateFixture();
        var malformed = (byte[])fixture.DependencyBytes.Clone();
        malformed[0] = 5;
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(fixture.CreateArtifact(
                malformed, fixture.DependencyFieldBytes, fixture.FieldPathBytes)));

        malformed = (byte[])fixture.DependencyBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(8), 2);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(fixture.CreateArtifact(
                malformed, fixture.DependencyFieldBytes, fixture.FieldPathBytes)));

        malformed = (byte[])fixture.DependencyBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(16), 2);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(fixture.CreateArtifact(
                malformed, fixture.DependencyFieldBytes, fixture.FieldPathBytes)));
    }

    private static byte[] EncodeDependencies(Fixture fixture,
        TrRmTargetLayoutDependencyRow[] rows, int fieldReferenceCount)
    {
        var readers = CreateReaders(fixture);
        return TrRmTargetLayoutDependencyTableWriter.Encode(
            rows, readers.TypeSpecs, readers.TargetProfiles, fieldReferenceCount);
    }

    private static byte[] EncodeFieldReferences(Fixture fixture, uint reference)
    {
        var readers = CreateReaders(fixture);
        return TrRmTargetLayoutDependencyFieldTableWriter.Encode(
            [reference], readers.Fields, readers.Imports);
    }

    private static byte[] EncodeFieldPath(Fixture fixture, TrRmFieldPathRow row)
    {
        var readers = CreateReaders(fixture);
        return TrRmFieldPathTableWriter.Encode(
            [row], readers.TypeSpecs, readers.Fields, readers.Imports);
    }

    private static Fixture CreateFixture()
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Provider", 0), ("Consumer", 0), ("Widget", 0),
            ("Field", 0), ("TypeKey", 0), ("FieldKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 7);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
            new TrRmModuleRow(3, 1, 0, 0, 1, 2, 1, 1),
        ], strings, 1, 2, 0, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, strings,
            1, 2, 0, 0, 0);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 2, 1, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1, 2, 1, 0, 0);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 4, 6, 1, 1, 0, 0, 1, 1, 0, 0),
            new TrRmTypeDefRow(1, 4, 6, 2, 1, 0, 0, 2, 1, 0, 0),
        ], strings, modules, 2, 0, 2, 0);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 2,
            strings, modules, 2, 0, 2, 0);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 5, 1, 10),
            new TrRmFieldDefRow(2, 5, 1, 10),
        ], strings, types, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 2,
            strings, types, typeSpecs);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(2, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(
            moduleRefBytes, 1, strings, modules);
        var importsBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 6, 0x1300_0002),
            new TrRmImportRow(1, 7, 0x1400_0002),
        ], strings, moduleRefs, types, default, default,
            fields: fields);
        var imports = new TrRmImportTableReader(importsBytes, 2,
            strings, moduleRefs, types, default, default, fields: fields);
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ]);
        var targetProfiles = new TrRmTargetProfileTableReader(targetProfileBytes, 1);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var dependencyFieldBytes = TrRmTargetLayoutDependencyFieldTableWriter.Encode([
            0x1400_0001, 0x1C00_0002,
        ], fields, imports);
        var dependencyBytes = TrRmTargetLayoutDependencyTableWriter.Encode([
            new TrRmTargetLayoutDependencyRow(1, 0, 0, 1, 1, 0, 0),
            new TrRmTargetLayoutDependencyRow(3, 0, 0, 1, 1, 1, 2),
        ], typeSpecs, targetProfiles, 2);
        var fieldPathBytes = TrRmFieldPathTableWriter.Encode([
            new TrRmFieldPathRow(1, [0x1400_0001, 0x1C00_0002]),
        ], typeSpecs, fields, imports);
        return new Fixture(moduleBytes, stringBytes, typeSpecBytes, typeDefBytes,
            fieldBytes, moduleRefBytes, importsBytes, layoutBytes,
            targetProfileBytes, dependencyBytes, dependencyFieldBytes, fieldPathBytes);
    }

    private static Readers CreateReaders(Fixture fixture)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 7);
        var modules = new TrRmModuleTableReader(fixture.Modules, 2, strings,
            1, 2, 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecBytes, 1,
            2, 1, 0, 0);
        var types = new TrRmTypeDefTableReader(fixture.TypeDefs, 2,
            strings, modules, 2, 0, 2, 0);
        var fields = new TrRmFieldDefTableReader(fixture.FieldsBytes, 2,
            strings, types, typeSpecs);
        var moduleRefs = new TrRmModuleRefTableReader(fixture.ModuleRefs,
            1, strings, modules);
        var imports = new TrRmImportTableReader(fixture.ImportsBytes, 2,
            strings, moduleRefs, types, default, default, fields: fields);
        var targetProfiles = new TrRmTargetProfileTableReader(
            fixture.TargetProfileBytes, 1);
        return new Readers(typeSpecs, fields, imports, targetProfiles);
    }

    private sealed record Fixture(
        byte[] Modules,
        byte[] Strings,
        byte[] TypeSpecBytes,
        byte[] TypeDefs,
        byte[] FieldsBytes,
        byte[] ModuleRefs,
        byte[] ImportsBytes,
        byte[] Layouts,
        byte[] TargetProfileBytes,
        byte[] DependencyBytes,
        byte[] DependencyFieldBytes,
        byte[] FieldPathBytes)
    {
        public byte[] CreateArtifact(byte[] dependencies,
            byte[] dependencyFields, byte[] fieldPaths, int fieldPathCount = 1)
            => TrRmArtifactWriter.Encode([
                new TrRmSection(0x0001, 0, 2, Modules),
                new TrRmSection(0x0002, 0, 7, Strings),
                new TrRmSection(0x0010, 0, 1, ModuleRefs),
                new TrRmSection(0x0011, 0, 1, TypeSpecBytes),
                new TrRmSection(0x0013, 0, 2, TypeDefs),
                new TrRmSection(0x0014, 0, 2, FieldsBytes),
                new TrRmSection(0x0015, 0, 2, Layouts),
                new TrRmSection(0x001C, 0, 2, ImportsBytes),
                new TrRmSection(0x0045, 0, (uint)(dependencies.Length / 20), dependencies),
                new TrRmSection(0x0048, 0, 1, TargetProfileBytes),
                new TrRmSection(0x004A, 0,
                    (uint)(dependencyFields.Length / sizeof(uint)), dependencyFields),
                new TrRmSection(0x004B, 0, (uint)fieldPathCount, fieldPaths),
            ]);
    }

    private readonly ref struct Readers
    {
        public Readers(TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields, TrRmImportTableReader imports,
            TrRmTargetProfileTableReader targetProfiles)
        {
            TypeSpecs = typeSpecs;
            Fields = fields;
            Imports = imports;
            TargetProfiles = targetProfiles;
        }

        public TrRmTypeSpecTableReader TypeSpecs { get; }
        public TrRmFieldDefTableReader Fields { get; }
        public TrRmImportTableReader Imports { get; }
        public TrRmTargetProfileTableReader TargetProfiles { get; }
    }
}
