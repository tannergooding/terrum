// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmTextFragmentShapeTableTests
{
    [Test]
    public void ValidatesLiteralAndFormattedRowsWithOpenConstraintEvidence()
    {
        var fixture = CreateFixture();
        var readers = CreateReaders(fixture);
        var textInputs = CreateTextInputs(fixture, readers);
        var fragments = TrRmTextFragmentShapeTableWriter.Encode([
            new TrRmTextFragmentShapeRow(1, 0, 0,
                0, 0, 0, 0, 0, 0, 2),
            new TrRmTextFragmentShapeRow(1, 1, 1,
                1, 2, 3, 0, 1, 1, 2),
        ], textInputs.Shapes, textInputs.Captures, readers.TypeDefs, readers.Methods,
            readers.Formals, readers.Constraints, interfaceWitnessCount: 0);
        var reader = new TrRmTextFragmentShapeTableReader(fragments, 2,
            textInputs.Shapes, textInputs.Captures, readers.TypeDefs, readers.Methods,
            readers.Formals, readers.Constraints, interfaceWitnessCount: 0);

        Assert.That(reader.GetKind(1), Is.Zero);
        Assert.That(reader.GetWriterRequirementMethodDefRow(1), Is.EqualTo(2u));
        Assert.That(reader.GetValueCaptureRow(1), Is.Zero);
        Assert.That(reader.GetKind(2), Is.EqualTo((byte)1));
        Assert.That(reader.GetValueCaptureRow(2), Is.EqualTo(1u));
        Assert.That(reader.GetFormatterConstraintRow(2), Is.EqualTo(1u));
        Assert.That(reader.GetFormatterMethodDefRow(2), Is.EqualTo(1u));

        TrRmStructuralMetadataValidator.Validate(
            fixture.CreateArtifact(textInputs.ShapeBytes, textInputs.CaptureBytes, fragments));
    }

    [Test]
    public void AcceptsClosedWitnessEvidenceWithinTheSelectedWitnessTable()
    {
        var fixture = CreateFixture();
        var readers = CreateReaders(fixture);
        var textInputs = CreateTextInputs(fixture, readers, fragmentCount: 1);
        var fragments = TrRmTextFragmentShapeTableWriter.Encode([
            new TrRmTextFragmentShapeRow(1, 0, 1,
                1, 2, 3, 1, 0, 1, 2),
        ], textInputs.Shapes, textInputs.Captures, readers.TypeDefs, readers.Methods,
            readers.Formals, readers.Constraints, interfaceWitnessCount: 1);
        var reader = new TrRmTextFragmentShapeTableReader(fragments, 1,
            textInputs.Shapes, textInputs.Captures, readers.TypeDefs, readers.Methods,
            readers.Formals, readers.Constraints, interfaceWitnessCount: 1);

        Assert.That(reader.GetFormatterWitnessRow(1), Is.EqualTo(1u));
        Assert.That(reader.GetFormatterConstraintRow(1), Is.Zero);
    }

    [Test]
    public void RejectsInvalidConditionalReferencesAndConstraintOwners()
    {
        var fixture = CreateFixture();
        Assert.Throws<InvalidDataException>(() =>
            EncodeFragments(fixture, [
                new TrRmTextFragmentShapeRow(1, 0, 0,
                    1, 0, 0, 0, 0, 0, 2),
            ]));
        Assert.Throws<InvalidDataException>(() =>
            EncodeFragments(fixture, [
                new TrRmTextFragmentShapeRow(1, 0, 1,
                    1, 2, 3, 0, 0, 1, 2),
            ]));
        Assert.Throws<InvalidDataException>(() =>
            EncodeFragments(fixture, [
                new TrRmTextFragmentShapeRow(1, 0, 1,
                    1, 2, 3, 1, 1, 1, 2),
            ]));
        Assert.Throws<InvalidDataException>(() =>
            EncodeFragments(fixture, [
                new TrRmTextFragmentShapeRow(1, 0, 1,
                    1, 2, 3, 0, 2, 1, 2),
            ]));
        Assert.Throws<InvalidDataException>(() =>
            EncodeFragments(fixture, [
                new TrRmTextFragmentShapeRow(1, 0, 1,
                    1, 1, 3, 0, 1, 1, 2),
            ]));
        Assert.Throws<InvalidDataException>(() =>
            EncodeFragments(CreateFixture(constraintKind: 2), [
                new TrRmTextFragmentShapeRow(1, 0, 1,
                    1, 2, 3, 0, 1, 1, 2),
            ]));
        Assert.Throws<InvalidDataException>(() =>
            EncodeFragments(fixture, [
                new TrRmTextFragmentShapeRow(1, 0, 1,
                    1, 2, 3, 0, 1, 3, 2),
            ]));
    }

    [Test]
    public void StructuralValidatorRejectsMalformedFragmentEvidence()
    {
        var fixture = CreateFixture();
        var readers = CreateReaders(fixture);
        var textInputs = CreateTextInputs(fixture, readers, fragmentCount: 1);
        var fragments = TrRmTextFragmentShapeTableWriter.Encode([
            new TrRmTextFragmentShapeRow(1, 0, 1,
                1, 2, 3, 0, 1, 1, 2),
        ], textInputs.Shapes, textInputs.Captures, readers.TypeDefs, readers.Methods,
            readers.Formals, readers.Constraints, interfaceWitnessCount: 0);
        BinaryPrimitives.WriteUInt32LittleEndian(fragments.AsSpan(28), 2);
        var shapeBytes = textInputs.ShapeBytes;
        var captureBytes = textInputs.CaptureBytes;
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                fixture.CreateArtifact(shapeBytes, captureBytes, fragments)));
    }

    private static byte[] EncodeFragments(Fixture fixture,
        TrRmTextFragmentShapeRow[] rows)
    {
        var readers = CreateReaders(fixture);
        var textInputs = CreateTextInputs(fixture, readers, rows.Length);
        return TrRmTextFragmentShapeTableWriter.Encode(rows,
            textInputs.Shapes, textInputs.Captures, readers.TypeDefs, readers.Methods,
            readers.Formals, readers.Constraints, interfaceWitnessCount: 0);
    }

    private static TextInputs CreateTextInputs(
        Fixture fixture, Readers readers, int fragmentCount = 2)
    {
        var shapeBytes = TrRmTextContentShapeTableWriter.Encode([
            new TrRmTextContentShapeRow(1, 1, 3,
                fragmentCount == 0 ? 0 : 1, fragmentCount),
        ], readers.TypeDefs, 3, fragmentCount);
        var shapes = new TrRmTextContentShapeTableReader(
            shapeBytes, 1, readers.TypeDefs, 3, fragmentCount);
        var captureBytes = TrRmTextCaptureShapeTableWriter.Encode([
            new TrRmTextCaptureShapeRow(1, 0, 2, 1),
            new TrRmTextCaptureShapeRow(1, 1, 1, 1),
            new TrRmTextCaptureShapeRow(1, 2, 1, 1),
        ], readers.TypeSpecs, shapes);
        var captures = new TrRmTextCaptureShapeTableReader(
            captureBytes, 3, readers.TypeSpecs, shapes);
        return new TextInputs(shapes, captures, shapeBytes, captureBytes);
    }

    private static Fixture CreateFixture(ushort constraintKind = 1)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Generated", 0), ("Format", 0),
            ("Write", 0), ("T", 0), ("External", 0), ("FormatKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 8);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 1, 1, 2, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            1, 1, 2, 0, 0);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(7, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(
            moduleRefBytes, 1, strings, modules);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x04, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
        ], 1, 0, 1, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 3,
            1, 0, 1, 0);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 6, 0, 0, 1, 1, 1, 1, 0, 0),
        ], strings, modules, 0, 1, 1, 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 1,
            strings, modules, 0, 1, 1, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], typeDefs, targetProfiles, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 1, 0, 0, 0, 0),
            new TrRmMethodSigRow(1, 1, 0, 0, 0, 0),
        ], typeSpecs, 0, 1);
        var signatures = new TrRmMethodSigTableReader(
            signatureBytes, 2, typeSpecs, 0, 1);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(1, 4, 1, 2, 0, 0, 0),
            new TrRmMethodDefRow(1, 5, 2, 2, 0, 0, 0),
        ], strings, modules, typeDefs, signatures, 0, 0);
        var methods = new TrRmMethodDefTableReader(methodBytes, 2,
            strings, modules, typeDefs, signatures, 0, 0);
        var importBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 8, 0x1600_0001),
        ], strings, moduleRefs, typeDefs, methods,
            default, default, default, default);
        var imports = new TrRmImportTableReader(importBytes, 1,
            strings, moduleRefs, typeDefs, methods,
            default, default, default, default);
        var genericBytes = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1300_0001, 0, 6,
                1, 1, 1, 1, 1, 0, 0),
        ], strings, typeDefs, methods, signatures, typeSpecs, 1);
        var formals = new TrRmGenericParamTableReader(genericBytes, 1,
            strings, typeDefs, methods, signatures, typeSpecs, 1);
        var constraintBytes = TrRmConstraintTableWriter.Encode([
            new TrRmConstraintRow(1, constraintKind, 3),
        ], formals, typeSpecs);
        return new Fixture(stringBytes, moduleBytes, typeSpecBytes,
            typeDefBytes, moduleRefBytes, importBytes, layoutBytes,
            signatureBytes, methodBytes,
            genericBytes, constraintBytes);
    }

    private static Readers CreateReaders(Fixture fixture)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 8);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            1, 1, 2, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs,
            3, 1, 0, 1, 0);
        var typeDefs = new TrRmTypeDefTableReader(fixture.TypeDefs, 1,
            strings, modules, 0, 1, 1, 0);
        var signatures = new TrRmMethodSigTableReader(
            fixture.Signatures, 2, typeSpecs, 0, 1);
        var methods = new TrRmMethodDefTableReader(fixture.Methods, 2,
            strings, modules, typeDefs, signatures, 0, 0);
        var formals = new TrRmGenericParamTableReader(fixture.GenericParams, 1,
            strings, typeDefs, methods, signatures, typeSpecs, 1);
        var constraints = new TrRmConstraintTableReader(
            fixture.Constraints, 1, formals, typeSpecs);
        return new Readers(typeSpecs, typeDefs, methods, formals, constraints);
    }

    private sealed record Fixture(
        byte[] Strings, byte[] Modules, byte[] TypeSpecs, byte[] TypeDefs,
        byte[] ModuleRefs, byte[] Imports, byte[] Layouts, byte[] Signatures,
        byte[] Methods, byte[] GenericParams, byte[] Constraints)
    {
        public byte[] CreateArtifact(byte[] shapes, byte[] captures, byte[] fragments)
            => TrRmArtifactWriter.Encode([
                new TrRmSection(0x0001, 0, 1, Modules),
                new TrRmSection(0x0002, 0, 8, Strings),
                new TrRmSection(0x0010, 0, 1, ModuleRefs),
                new TrRmSection(0x0011, 0, 3, TypeSpecs),
                new TrRmSection(0x0013, 0, 1, TypeDefs),
                new TrRmSection(0x0015, 0, 1, Layouts),
                new TrRmSection(0x0016, 0, 2, Methods),
                new TrRmSection(0x0017, 0, 2, Signatures),
                new TrRmSection(0x0019, 0, 1, GenericParams),
                new TrRmSection(0x001A, 0, 1, Constraints),
                new TrRmSection(0x001C, 0, 1, Imports),
                new TrRmSection(0x002D, 0, 1, shapes),
                new TrRmSection(0x002E, 0, 3, captures),
                new TrRmSection(0x002F, 0, (uint)(fragments.Length / 40), fragments),
            ]);
    }

    private readonly ref struct Readers
    {
        public Readers(TrRmTypeSpecTableReader typeSpecs,
            TrRmTypeDefTableReader typeDefs, TrRmMethodDefTableReader methods,
            TrRmGenericParamTableReader formals, TrRmConstraintTableReader constraints)
        {
            TypeSpecs = typeSpecs;
            TypeDefs = typeDefs;
            Methods = methods;
            Formals = formals;
            Constraints = constraints;
        }

        public TrRmTypeSpecTableReader TypeSpecs { get; }
        public TrRmTypeDefTableReader TypeDefs { get; }
        public TrRmMethodDefTableReader Methods { get; }
        public TrRmGenericParamTableReader Formals { get; }
        public TrRmConstraintTableReader Constraints { get; }
    }

    private readonly ref struct TextInputs
    {
        public TextInputs(TrRmTextContentShapeTableReader shapes,
            TrRmTextCaptureShapeTableReader captures,
            byte[] shapeBytes, byte[] captureBytes)
        {
            Shapes = shapes;
            Captures = captures;
            ShapeBytes = shapeBytes;
            CaptureBytes = captureBytes;
        }

        public TrRmTextContentShapeTableReader Shapes { get; }
        public TrRmTextCaptureShapeTableReader Captures { get; }
        public byte[] ShapeBytes { get; }
        public byte[] CaptureBytes { get; }
    }
}
