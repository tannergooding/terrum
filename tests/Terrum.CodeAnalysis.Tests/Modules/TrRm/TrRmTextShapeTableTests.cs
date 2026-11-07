// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmTextShapeTableTests
{
    [Test]
    public void ValidatesExclusiveShapeRangesAndTypedCaptureRows()
    {
        var fixture = CreateFixture();
        var readers = CreateReaders(fixture);
        var shapes = TrRmTextContentShapeTableWriter.Encode([
            new TrRmTextContentShapeRow(1, 1, 1, 0, 0),
        ], readers.TypeDefs, 1, 0);
        var shapeReader = new TrRmTextContentShapeTableReader(
            shapes, 1, readers.TypeDefs, 1, 0);
        var captures = TrRmTextCaptureShapeTableWriter.Encode([
            new TrRmTextCaptureShapeRow(1, 0, 0xF200_0008, 2),
        ], readers.TypeSpecs, shapeReader);
        var captureReader = new TrRmTextCaptureShapeTableReader(
            captures, 1, readers.TypeSpecs, shapeReader);

        Assert.That(shapeReader.GetGeneratedTypeDef(1), Is.EqualTo(1));
        Assert.That(shapeReader.GetCaptureRange(1), Is.EqualTo((1, 1)));
        Assert.That(shapeReader.GetFragmentRange(1), Is.EqualTo((0, 0)));
        Assert.That(captureReader.GetTypeSpecRef(1), Is.EqualTo(0xF200_0008u));
        Assert.That(captureReader.GetMode(1), Is.EqualTo((byte)2));

        TrRmStructuralMetadataValidator.Validate(
            fixture.CreateArtifact(shapes, captures));
    }

    [Test]
    public void TextShapeReadersChargeRangesAndCapturesAtExactWorkBoundary()
    {
        var fixture = CreateFixture();
        var setup = CreateReaders(fixture);
        var shapeBytes = TrRmTextContentShapeTableWriter.Encode([
            new TrRmTextContentShapeRow(1, 1, 1, 0, 0),
        ], setup.TypeDefs, 1, 0);
        var setupShapes = new TrRmTextContentShapeTableReader(
            shapeBytes, 1, setup.TypeDefs, 1, 0);
        var captureBytes = TrRmTextCaptureShapeTableWriter.Encode([
            new TrRmTextCaptureShapeRow(1, 0, 0xF200_0008, 2),
        ], setup.TypeSpecs, setupShapes);

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var readers = CreateReaders(fixture);
            var shapes = new TrRmTextContentShapeTableReader(shapeBytes, 1,
            readers.TypeDefs, 1, 0, ledger);
            var captures = new TrRmTextCaptureShapeTableReader(captureBytes, 1,
            readers.TypeSpecs, shapes, ledger);
            Assert.That(shapes.GetCaptureRange(1), Is.EqualTo((1, 1)));
            Assert.That(captures.GetMode(1), Is.EqualTo((byte)2));
        });
    }

    [Test]
    public void RejectsInvalidShapeOwnersRangesCaptureModesAndTypes()
    {
        var fixture = CreateFixture();
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeShapes(fixture, [
                new TrRmTextContentShapeRow(0, 0, 0, 0, 0),
            ], 0, 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeShapes(fixture, [
                new TrRmTextContentShapeRow(1, 0, 1, 0, 0),
            ], 1, 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeShapes(fixture, [
                new TrRmTextContentShapeRow(1, 1, 1, 0, 0),
                new TrRmTextContentShapeRow(1, 1, 1, 0, 0),
            ], 1, 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeShapes(fixture, [
                new TrRmTextContentShapeRow(1, 0, 0, 0, 0),
            ], 1, 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeShapes(fixture, [
                new TrRmTextContentShapeRow(1, 0, 0, 0, 0),
            ], 0, 1));

        _ = Assert.Throws<InvalidDataException>(() => EncodeCaptures(fixture,
            [new TrRmTextCaptureShapeRow(2, 0, 1, 1)]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeCaptures(fixture,
            [new TrRmTextCaptureShapeRow(1, 1, 1, 1)]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeCaptures(fixture,
            [new TrRmTextCaptureShapeRow(1, 0, 0, 1)]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeCaptures(fixture,
            [new TrRmTextCaptureShapeRow(1, 0, 0xF400_0008, 1)]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeCaptures(fixture,
            [new TrRmTextCaptureShapeRow(1, 0, 1, 0)]));
    }

    [Test]
    public void StructuralValidatorRejectsMalformedCaptureRows()
    {
        var fixture = CreateFixture();
        var shapes = EncodeShapes(fixture, [
            new TrRmTextContentShapeRow(1, 1, 1, 0, 0),
        ], 1, 0);
        var captures = EncodeCaptures(fixture, [
            new TrRmTextCaptureShapeRow(1, 0, 1, 1),
        ]);
        BinaryPrimitives.WriteUInt32LittleEndian(captures.AsSpan(8), 0xF400_0008);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                fixture.CreateArtifact(shapes, captures)));

        captures = EncodeCaptures(fixture, [
            new TrRmTextCaptureShapeRow(1, 0, 1, 1),
        ]);
        captures[13] = 1;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                fixture.CreateArtifact(shapes, captures)));
    }

    private static byte[] EncodeShapes(Fixture fixture,
        TrRmTextContentShapeRow[] rows, int captureCount, int fragmentCount)
    {
        var readers = CreateReaders(fixture);
        return TrRmTextContentShapeTableWriter.Encode(
            rows, readers.TypeDefs, captureCount, fragmentCount);
    }

    private static byte[] EncodeCaptures(Fixture fixture,
        TrRmTextCaptureShapeRow[] rows)
    {
        var readers = CreateReaders(fixture);
        var shapes = TrRmTextContentShapeTableWriter.Encode([
            new TrRmTextContentShapeRow(1, 1, 1, 0, 0),
        ], readers.TypeDefs, 1, 0);
        var shapeReader = new TrRmTextContentShapeTableReader(
            shapes, 1, readers.TypeDefs, 1, 0);
        return TrRmTextCaptureShapeTableWriter.Encode(rows,
            readers.TypeSpecs, shapeReader);
    }

    private static Fixture CreateFixture()
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Generated", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
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
        var strings = new TrRmStringTableReader(fixture.Strings, 3);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 1, 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs,
            1, 1, 0, 0, 0);
        var typeDefs = new TrRmTypeDefTableReader(fixture.TypeDefs, 1,
            strings, modules, 0, 0, 1, 0);
        return new Readers(typeSpecs, typeDefs);
    }

    private sealed record Fixture(
        byte[] Strings, byte[] Modules, byte[] TypeSpecs,
        byte[] TypeDefs, byte[] Layouts)
    {
        public byte[] CreateArtifact(byte[] shapes, byte[] captures)
            => TrRmArtifactWriter.Encode([
                new TrRmSection(0x0001, 0, 1, Modules),
                new TrRmSection(0x0002, 0, 3, Strings),
                new TrRmSection(0x0011, 0, 1, TypeSpecs),
                new TrRmSection(0x0013, 0, 1, TypeDefs),
                new TrRmSection(0x0015, 0, 1, Layouts),
                new TrRmSection(0x002D, 0,
                    (uint)(shapes.Length / 20), shapes),
                new TrRmSection(0x002E, 0,
                    (uint)(captures.Length / 16), captures),
            ]);
    }

    private readonly ref struct Readers
    {
        public Readers(TrRmTypeSpecTableReader typeSpecs,
            TrRmTypeDefTableReader typeDefs)
        {
            TypeSpecs = typeSpecs;
            TypeDefs = typeDefs;
        }

        public TrRmTypeSpecTableReader TypeSpecs { get; }
        public TrRmTypeDefTableReader TypeDefs { get; }
    }
}
