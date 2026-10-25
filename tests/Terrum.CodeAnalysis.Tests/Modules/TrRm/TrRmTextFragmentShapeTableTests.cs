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
                1, 2, 3, 0, 1, 1, 4),
        ], textInputs.Shapes, textInputs.Captures, readers.TypeDefs, readers.Methods,
            readers.Formals, readers.Constraints, readers.TypeSpecs,
            readers.Interfaces, readers.InterfaceBases, readers.InterfaceMethods,
            readers.Witnesses, readers.WitnessMembers, readers.Imports);
        var reader = new TrRmTextFragmentShapeTableReader(fragments, 2,
            textInputs.Shapes, textInputs.Captures, readers.TypeDefs, readers.Methods,
            readers.Formals, readers.Constraints, readers.TypeSpecs,
            readers.Interfaces, readers.InterfaceBases, readers.InterfaceMethods,
            readers.Witnesses, readers.WitnessMembers, readers.Imports);

        Assert.That(reader.GetKind(1), Is.Zero);
        Assert.That(reader.GetWriterRequirementMethodDefRow(1), Is.EqualTo(2u));
        Assert.That(reader.GetValueCaptureRow(1), Is.Zero);
        Assert.That(reader.GetKind(2), Is.EqualTo((byte)1));
        Assert.That(reader.GetValueCaptureRow(2), Is.EqualTo(1u));
        Assert.That(reader.GetFormatterConstraintRow(2), Is.EqualTo(1u));
        Assert.That(reader.GetFormatterMethodDefRow(2), Is.EqualTo(1u));
        Assert.That(reader.GetWriterRequirementMethodDefRow(2), Is.EqualTo(4u));

        TrRmStructuralMetadataValidator.Validate(
            fixture.CreateArtifact(textInputs.ShapeBytes, textInputs.CaptureBytes, fragments));
    }

    [Test]
    public void AcceptsClosedWitnessEvidenceWithinTheSelectedWitnessTable()
    {
        var fixture = CreateFixture(closedWitness: true);
        var readers = CreateReaders(fixture);
        var textInputs = CreateTextInputs(fixture, readers, fragmentCount: 1);
        var fragments = TrRmTextFragmentShapeTableWriter.Encode([
            new TrRmTextFragmentShapeRow(1, 0, 1,
                1, 2, 3, 1, 0, 1, 4),
        ], textInputs.Shapes, textInputs.Captures, readers.TypeDefs, readers.Methods,
            readers.Formals, readers.Constraints, readers.TypeSpecs,
            readers.Interfaces, readers.InterfaceBases, readers.InterfaceMethods,
            readers.Witnesses, readers.WitnessMembers, readers.Imports);
        var reader = new TrRmTextFragmentShapeTableReader(fragments, 1,
            textInputs.Shapes, textInputs.Captures, readers.TypeDefs, readers.Methods,
            readers.Formals, readers.Constraints, readers.TypeSpecs,
            readers.Interfaces, readers.InterfaceBases, readers.InterfaceMethods,
            readers.Witnesses, readers.WitnessMembers, readers.Imports);

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
                    1, 2, 3, 0, 1, 1, 4),
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
            readers.Formals, readers.Constraints, readers.TypeSpecs,
            readers.Interfaces, readers.InterfaceBases, readers.InterfaceMethods,
            readers.Witnesses, readers.WitnessMembers, readers.Imports);
        BinaryPrimitives.WriteUInt32LittleEndian(fragments.AsSpan(28), 2);
        var shapeBytes = textInputs.ShapeBytes;
        var captureBytes = textInputs.CaptureBytes;
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                fixture.CreateArtifact(shapeBytes, captureBytes, fragments)));
    }

    [Test]
    public void RejectsMethodRowsOutsideTheInterfaceRequirementTables()
    {
        var fixture = CreateFixture();
        Assert.Throws<InvalidDataException>(() =>
            EncodeFragments(fixture, [
                new TrRmTextFragmentShapeRow(1, 0, 1,
                    1, 2, 3, 0, 1, 3, 2),
            ]));
        Assert.Throws<InvalidDataException>(() =>
            EncodeFragments(fixture, [
                new TrRmTextFragmentShapeRow(1, 0, 1,
                    1, 2, 3, 0, 1, 1, 3),
            ]));
        Assert.Throws<InvalidDataException>(() =>
            EncodeFragments(CreateFixture(constraintTypeSpecRef: 4), [
                new TrRmTextFragmentShapeRow(1, 0, 1,
                    1, 2, 3, 0, 1, 1, 2),
            ]));
    }

    [Test]
    public void SelectedProfileBindsExactFormatterAndWriterRequirementRows()
    {
        var fixture = CreateFixture();
        var artifact = CreateProfileArtifact(fixture);
        var profile = new TrRmTextConstructionProfile(1, 1, 1, 2, 2, 5, 4,
            fixture.TextSpanTypeSpecRef);

        TrRmStructuralMetadataValidator.ValidateTextConstructionProfile(artifact, [profile]);

        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateTextConstructionProfile(artifact, []));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateTextConstructionProfile(artifact, [
                profile with { WriterTryWriteFormattedMethodDefRow = 2 },
            ]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateTextConstructionProfile(artifact, [
                profile with { FormatterTryFormatMethodDefRow = 2 },
            ]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateTextConstructionProfile(artifact, [
                profile with { TextSpanTypeSpecRef = 1 },
            ]));
        foreach (var effectsMask in new uint[] { 0x02, 0x04 })
        {
            Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator.ValidateTextConstructionProfile(
                    CreateProfileArtifact(CreateFixture(formatterEffectsMask: effectsMask)),
                    [profile]));
        }

        var wrongResult = (byte[])fixture.Signatures.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(wrongResult, 0xF200_0008);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateTextConstructionProfile(
                CreateProfileArtifact(fixture with { Signatures = wrongResult }), [profile]));

        var wrongFormalConstraint = (byte[])fixture.Constraints.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongFormalConstraint.AsSpan(20), 3);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateTextConstructionProfile(
                CreateProfileArtifact(fixture with { Constraints = wrongFormalConstraint }),
                [profile]));
    }

    [Test]
    public void ClosedFormatterWitnessMustMatchTheCaptureAndExactRequirement()
    {
        Assert.Throws<InvalidDataException>(() =>
            EncodeFragments(CreateFixture(closedWitness: true,
                witnessImplementingTypeSpecRef: 3), [
                new TrRmTextFragmentShapeRow(1, 0, 1,
                    1, 2, 3, 1, 0, 1, 4),
            ]));
        Assert.Throws<InvalidDataException>(() =>
            EncodeFragments(CreateFixture(closedWitness: true,
                witnessRequirementMethodDef: 2), [
                new TrRmTextFragmentShapeRow(1, 0, 1,
                    1, 2, 3, 1, 0, 1, 4),
            ]));
        var inherited = CreateFixture(closedWitness: true, inheritedFormatter: true);
        var inheritedReaders = CreateReaders(inherited);
        var inheritedInputs = CreateTextInputs(inherited, inheritedReaders, fragmentCount: 1);
        var inheritedFragments = TrRmTextFragmentShapeTableWriter.Encode([
            new TrRmTextFragmentShapeRow(1, 0, 1,
                1, 2, 3, 1, 0, 1, 2),
        ], inheritedInputs.Shapes, inheritedInputs.Captures,
            inheritedReaders.TypeDefs, inheritedReaders.Methods,
            inheritedReaders.Formals, inheritedReaders.Constraints,
            inheritedReaders.TypeSpecs, inheritedReaders.Interfaces,
            inheritedReaders.InterfaceBases, inheritedReaders.InterfaceMethods,
            inheritedReaders.Witnesses, inheritedReaders.WitnessMembers,
            inheritedReaders.Imports);
        TrRmStructuralMetadataValidator.Validate(
            inherited.CreateArtifact(inheritedInputs.ShapeBytes,
                inheritedInputs.CaptureBytes, inheritedFragments));
    }

    private static byte[] EncodeFragments(Fixture fixture,
        TrRmTextFragmentShapeRow[] rows)
    {
        var readers = CreateReaders(fixture);
        var textInputs = CreateTextInputs(fixture, readers, rows.Length);
        return TrRmTextFragmentShapeTableWriter.Encode(rows,
            textInputs.Shapes, textInputs.Captures, readers.TypeDefs, readers.Methods,
            readers.Formals, readers.Constraints, readers.TypeSpecs,
            readers.Interfaces, readers.InterfaceBases, readers.InterfaceMethods,
            readers.Witnesses, readers.WitnessMembers, readers.Imports);
    }

    private static byte[] CreateProfileArtifact(Fixture fixture)
    {
        var readers = CreateReaders(fixture);
        var inputs = CreateTextInputs(fixture, readers);
        var fragments = TrRmTextFragmentShapeTableWriter.Encode([
            new TrRmTextFragmentShapeRow(1, 0, 0,
                0, 0, 0, 0, 0, 0, 2),
            new TrRmTextFragmentShapeRow(1, 1, 1,
                1, 2, 3, 0, 1, 1, 4),
        ], inputs.Shapes, inputs.Captures, readers.TypeDefs, readers.Methods,
            readers.Formals, readers.Constraints, readers.TypeSpecs,
            readers.Interfaces, readers.InterfaceBases, readers.InterfaceMethods,
            readers.Witnesses, readers.WitnessMembers, readers.Imports);
        return fixture.CreateArtifact(inputs.ShapeBytes, inputs.CaptureBytes, fragments);
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
            new TrRmTextCaptureShapeRow(1, 0, fixture.ValueCaptureTypeSpecRef, 2),
            new TrRmTextCaptureShapeRow(1, 1, 1, 1),
            new TrRmTextCaptureShapeRow(1, 2, fixture.TextSpanTypeSpecRef, 1),
        ], readers.TypeSpecs, shapes);
        var captures = new TrRmTextCaptureShapeTableReader(
            captureBytes, 3, readers.TypeSpecs, shapes);
        return new TextInputs(shapes, captures, shapeBytes, captureBytes);
    }

    private static Fixture CreateFixture(ushort constraintKind = 1,
        uint constraintTypeSpecRef = 3, bool closedWitness = false,
        uint witnessImplementingTypeSpecRef = 2,
        int witnessRequirementMethodDef = 1, bool inheritedFormatter = false,
        uint formatterEffectsMask = 0)
    {
        var typeDefCount = inheritedFormatter ? 5 : 4;
        var typeSpecCount = inheritedFormatter ? 13 : 12;
        var interfaceDefCount = inheritedFormatter ? 3 : 2;
        var interfaceBaseCount = inheritedFormatter ? 1 : 0;
        var contractCount = formatterEffectsMask == 0 ? 0 : 1;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Generated", 0), ("Formatter", 0),
            ("Writer", 0), ("Format", 0), ("Write", 0),
            ("Implementation", 0), ("T", 0), ("TextSpan", 0),
            ("DerivedFormatter", 0), ("WriteFormatted", 0), ("WriteScalar", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 13);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, typeDefCount, 5, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, typeDefCount, 5, 0, 0);
        var typeSpecRows = new System.Collections.Generic.List<ReadOnlyMemory<byte>> {
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 2),
            TrRmTypeSpecTableWriter.Reference(0x02, 3),
            TrRmTypeSpecTableWriter.Reference(0x04, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 4),
            TrRmTypeSpecTableWriter.Reference(0x04, 2),
            TrRmTypeSpecTableWriter.Reference(0x04, 3),
            TrRmTypeSpecTableWriter.Wrapped(0x07, 7, 0x01),
            TrRmTypeSpecTableWriter.Wrapped(0x07, 8),
            TrRmTypeSpecTableWriter.Builtin(5),
            TrRmTypeSpecTableWriter.Reference(0x02, 3, 0x01),
        };
        if (inheritedFormatter)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02, 5));
        }
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount, 0, 3, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecCount,
            typeDefCount, 0, 3, 0);
        var typeDefRows = new System.Collections.Generic.List<TrRmTypeDefRow> {
            new TrRmTypeDefRow(1, 3, 6, 0, 0, 1, 1, 1, 1, 0, 0),
            new TrRmTypeDefRow(1, 4, 14, 0, 0, 0, 0, 2, 1, 0, 0),
            new TrRmTypeDefRow(1, 5, 14, 0, 0, 0, 0, 3, 1, 0, 0),
            new TrRmTypeDefRow(1, 10, 6, 0, 0, 0, 0, 4, 1, 0, 0),
        };
        if (inheritedFormatter)
        {
            typeDefRows.Add(new TrRmTypeDefRow(1, 11, 14, 0, 0, 0, 0, 5, 1, 0, 0));
        }
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, 0, 3, typeDefCount, contractCount);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules, 0, 3, typeDefCount, contractCount);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutRows = new System.Collections.Generic.List<TrRmLayoutRow> {
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(3, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(4, 0, 0, 0, 0, 0, 0, 0),
        };
        if (inheritedFormatter)
        {
            layoutRows.Add(new TrRmLayoutRow(5, 0, 0, 0, 0, 0, 0, 0));
        }
        var layoutBytes = TrRmLayoutTableWriter.Encode(
            layoutRows, typeDefs, targetProfiles, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0001, 3, 1, 2, 2, 1),
            new TrRmMethodSigRow(0xF200_0001, 12, 3, 1, 0, 0),
            new TrRmMethodSigRow(0xF200_0001, 2, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF200_0001, 12, 4, 3, 3, 1),
            new TrRmMethodSigRow(0xF200_0001, 12, 7, 1, 0, 0),
        ], typeSpecs, 7, 3);
        var signatures = new TrRmMethodSigTableReader(
            signatureBytes, 5, typeSpecs, 7, 3);
        var parameterBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, 9, 0),
            new TrRmParamRow(1, 1, 6, 0),
            new TrRmParamRow(2, 0, 6, 0),
            new TrRmParamRow(4, 0, 10, 0),
            new TrRmParamRow(4, 1, 1, 0),
            new TrRmParamRow(4, 2, 6, 0),
            new TrRmParamRow(5, 0, 11, 0),
        ], signatures, typeSpecs);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(2, 6, 1, 2, contractCount == 0 ? 0 : 1,
                contractCount, 0),
            new TrRmMethodDefRow(3, 7, 2, 2, 0, 0, 0),
            new TrRmMethodDefRow(1, 8, 3, 2, 0, 0, 0),
            new TrRmMethodDefRow(3, 11, 4, 2, 0, 0, 0),
            new TrRmMethodDefRow(3, 12, 5, 2, 0, 0, 0),
        ], strings, modules, typeDefs, signatures, contractCount, 0);
        var methods = new TrRmMethodDefTableReader(methodBytes, 5,
            strings, modules, typeDefs, signatures, contractCount, 0);
        var contracts = Array.Empty<byte>();
        if (formatterEffectsMask != 0)
        {
            var effectPayload = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(effectPayload, formatterEffectsMask);
            var parameters = new TrRmParamTableReader(parameterBytes, 7, signatures, typeSpecs);
            contracts = TrRmContractTableWriter.Encode([
                new TrRmContractRow(0x1600_0001, 1, effectPayload),
            ], typeDefs, methods, signatures, parameters, typeSpecs);
        }
        var genericBytes = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1300_0001, 0, 9,
                1, 1, 1, 1, 1, 0, 0),
            new TrRmGenericParamRow(0x1600_0001, 0, 9,
                2, 1, 1, 1, 1, 0, 0),
            new TrRmGenericParamRow(0x1600_0004, 0, 9,
                3, 1, 1, 1, 1, 0, 0),
        ], strings, typeDefs, methods, signatures, typeSpecs, 3);
        var formals = new TrRmGenericParamTableReader(genericBytes, 3,
            strings, typeDefs, methods, signatures, typeSpecs, 3);
        var constraintBytes = TrRmConstraintTableWriter.Encode([
            new TrRmConstraintRow(1, constraintKind, constraintTypeSpecRef),
            new TrRmConstraintRow(2, 1, 4),
            new TrRmConstraintRow(3, 1, 3),
        ], formals, typeSpecs);

        var interfaceDefRows = new System.Collections.Generic.List<TrRmInterfaceDefRow> {
            new TrRmInterfaceDefRow(2, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0),
            new TrRmInterfaceDefRow(3, 0, 0, 0, 2, 3, 0, 0, 0, 0, 0, 0),
        };
        if (inheritedFormatter)
        {
            interfaceDefRows.Add(new TrRmInterfaceDefRow(5, 0, 1, 1, 0, 0,
                0, 0, 0, 0, 0, 0));
        }
        var interfaceDefBytes = TrRmInterfaceDefTableWriter.Encode(
            interfaceDefRows, typeDefs, formals, interfaceBaseCount, 4, 0, 0, 0);
        var interfaces = new TrRmInterfaceDefTableReader(interfaceDefBytes,
            interfaceDefCount, typeDefs, formals, interfaceBaseCount, 4, 0, 0, 0);
        var interfaceBaseBytes = TrRmInterfaceBaseTableWriter.Encode(
            inheritedFormatter
                ? [new TrRmInterfaceBaseRow(3, 0, 3)]
                : Array.Empty<TrRmInterfaceBaseRow>(), typeSpecs, interfaces);
        var interfaceBases = new TrRmInterfaceBaseTableReader(interfaceBaseBytes,
            interfaceBaseCount,
            typeSpecs, interfaces);
        var interfaceMethodBytes = TrRmInterfaceMethodTableWriter.Encode([
            new TrRmInterfaceMethodRow(1, 0, 1),
            new TrRmInterfaceMethodRow(2, 0, 2),
            new TrRmInterfaceMethodRow(2, 1, 4),
            new TrRmInterfaceMethodRow(2, 2, 5),
        ], interfaces, methods, signatures);
        var interfaceMethods = new TrRmInterfaceMethodTableReader(
            interfaceMethodBytes, 4, interfaces, methods, signatures);
        var witnessRows = closedWitness
            ? new[] {
                new TrRmInterfaceWitnessRow(
                    witnessImplementingTypeSpecRef, inheritedFormatter ? 13u : 3u,
                    1, 1, 0, 0, 0, 0),
            }
            : Array.Empty<TrRmInterfaceWitnessRow>();
        var witnessBytes = TrRmInterfaceWitnessTableWriter.Encode(
            witnessRows, typeSpecs, closedWitness ? 1 : 0, 0, 0);
        var witnesses = new TrRmInterfaceWitnessTableReader(
            witnessBytes, witnessRows.Length, typeSpecs, closedWitness ? 1 : 0, 0, 0);
        var witnessMemberRows = closedWitness
            ? new[] { new TrRmWitnessMemberRow(
                1, 3, witnessRequirementMethodDef, 2, 3) }
            : Array.Empty<TrRmWitnessMemberRow>();
        var witnessMemberBytes = TrRmWitnessMemberTableWriter.Encode(
            witnessMemberRows, witnesses, typeSpecs, methods);
        var witnessMembers = new TrRmWitnessMemberTableReader(
            witnessMemberBytes, witnessMemberRows.Length, witnesses, typeSpecs, methods);

        return new Fixture(stringBytes, moduleBytes, typeSpecBytes,
            typeDefBytes, layoutBytes, signatureBytes, parameterBytes, methodBytes,
            genericBytes, constraintBytes, contracts, interfaceDefBytes, interfaceBaseBytes,
            interfaceMethodBytes, witnessBytes, witnessMemberBytes,
            typeSpecCount, closedWitness ? 2u : 5u, 6u);
    }

    private static Readers CreateReaders(Fixture fixture)
    {
        var typeDefCount = fixture.TypeDefs.Length / 44;
        var layoutCount = fixture.Layouts.Length / 28;
        var contractCount = fixture.Contracts.Length == 0 ? 0 : 1;
        var interfaceDefCount = fixture.InterfaceDefs.Length / 48;
        var interfaceBaseCount = fixture.InterfaceBases.Length / 12;
        var interfaceMethodCount = fixture.InterfaceMethods.Length / 12;
        var witnessCount = fixture.Witnesses.Length / 32;
        var witnessMemberCount = fixture.WitnessMembers.Length / 20;
        var methodCount = fixture.Methods.Length / 28;
        var signatureCount = fixture.Signatures.Length / 24;
        var parameterCount = fixture.Params.Length / 16;
        var strings = new TrRmStringTableReader(fixture.Strings, 13);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, typeDefCount, methodCount, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs,
            fixture.TypeSpecCount, typeDefCount, 0, 3, 0);
        var typeDefs = new TrRmTypeDefTableReader(fixture.TypeDefs, typeDefCount,
            strings, modules, 0, 3, layoutCount, contractCount);
        var signatures = new TrRmMethodSigTableReader(
            fixture.Signatures, signatureCount, typeSpecs, parameterCount, 3);
        _ = new TrRmParamTableReader(fixture.Params, parameterCount, signatures, typeSpecs);
        var methods = new TrRmMethodDefTableReader(fixture.Methods, methodCount,
            strings, modules, typeDefs, signatures, contractCount, 0);
        var formals = new TrRmGenericParamTableReader(fixture.GenericParams, 3,
            strings, typeDefs, methods, signatures, typeSpecs, 3);
        var constraints = new TrRmConstraintTableReader(
            fixture.Constraints, 3, formals, typeSpecs);
        var interfaces = new TrRmInterfaceDefTableReader(fixture.InterfaceDefs,
            interfaceDefCount, typeDefs, formals, interfaceBaseCount,
            interfaceMethodCount, 0, 0, 0);
        var interfaceBases = new TrRmInterfaceBaseTableReader(fixture.InterfaceBases,
            interfaceBaseCount, typeSpecs, interfaces);
        var interfaceMethods = new TrRmInterfaceMethodTableReader(
            fixture.InterfaceMethods, interfaceMethodCount, interfaces, methods, signatures);
        var witnesses = new TrRmInterfaceWitnessTableReader(fixture.Witnesses,
            witnessCount, typeSpecs, witnessMemberCount, 0, 0);
        var witnessMembers = new TrRmWitnessMemberTableReader(fixture.WitnessMembers,
            witnessMemberCount, witnesses, typeSpecs, methods);
        return new Readers(typeSpecs, typeDefs, methods, formals, constraints,
            interfaces, interfaceBases, interfaceMethods, witnesses, witnessMembers,
            default);
    }

    private sealed record Fixture(
        byte[] Strings, byte[] Modules, byte[] TypeSpecs, byte[] TypeDefs,
        byte[] Layouts, byte[] Signatures, byte[] Params, byte[] Methods,
        byte[] GenericParams, byte[] Constraints, byte[] Contracts, byte[] InterfaceDefs,
        byte[] InterfaceBases, byte[] InterfaceMethods, byte[] Witnesses,
        byte[] WitnessMembers, int TypeSpecCount, uint ValueCaptureTypeSpecRef,
        uint TextSpanTypeSpecRef)
    {
        public byte[] CreateArtifact(byte[] shapes, byte[] captures, byte[] fragments)
        {
            var sections = new System.Collections.Generic.List<TrRmSection>
            {
                new TrRmSection(0x0001, 0, 1, Modules),
                new TrRmSection(0x0002, 0, 13, Strings),
                new TrRmSection(0x0011, 0, (uint)TypeSpecCount, TypeSpecs),
                new TrRmSection(0x0013, 0, (uint)(TypeDefs.Length / 44), TypeDefs),
                new TrRmSection(0x0015, 0, (uint)(Layouts.Length / 28), Layouts),
                new TrRmSection(0x0016, 0, (uint)(Methods.Length / 28), Methods),
                new TrRmSection(0x0017, 0, (uint)(Signatures.Length / 24), Signatures),
                new TrRmSection(0x0018, 0, (uint)(Params.Length / 16), Params),
                new TrRmSection(0x0019, 0, 3, GenericParams),
                new TrRmSection(0x001A, 0, 3, Constraints),
                new TrRmSection(0x0025, 0, (uint)(InterfaceDefs.Length / 48), InterfaceDefs),
                new TrRmSection(0x0024, 0, (uint)(InterfaceMethods.Length / 12), InterfaceMethods),
                new TrRmSection(0x002D, 0, 1, shapes),
                new TrRmSection(0x002E, 0, 3, captures),
                new TrRmSection(0x002F, 0, (uint)(fragments.Length / 40), fragments),
            };
            if (Contracts.Length != 0)
            {
                sections.Add(new TrRmSection(0x001B, 0, 1, Contracts));
            }
            if (InterfaceBases.Length != 0)
            {
                sections.Add(new TrRmSection(0x0026, 0,
                    (uint)(InterfaceBases.Length / 12), InterfaceBases));
            }
            if (Witnesses.Length != 0)
            {
                sections.Add(new TrRmSection(0x0028, 0,
                    (uint)(Witnesses.Length / 32), Witnesses));
            }
            if (WitnessMembers.Length != 0)
            {
                sections.Add(new TrRmSection(0x0029, 0,
                    (uint)(WitnessMembers.Length / 20), WitnessMembers));
            }

            sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
            return TrRmArtifactWriter.Encode(sections);
        }
    }

    private readonly ref struct Readers
    {
        public Readers(TrRmTypeSpecTableReader typeSpecs,
            TrRmTypeDefTableReader typeDefs, TrRmMethodDefTableReader methods,
            TrRmGenericParamTableReader formals, TrRmConstraintTableReader constraints,
            TrRmInterfaceDefTableReader interfaces,
            TrRmInterfaceBaseTableReader interfaceBases,
            TrRmInterfaceMethodTableReader interfaceMethods,
            TrRmInterfaceWitnessTableReader witnesses,
            TrRmWitnessMemberTableReader witnessMembers,
            TrRmImportTableReader imports)
        {
            TypeSpecs = typeSpecs;
            TypeDefs = typeDefs;
            Methods = methods;
            Formals = formals;
            Constraints = constraints;
            Interfaces = interfaces;
            InterfaceBases = interfaceBases;
            InterfaceMethods = interfaceMethods;
            Witnesses = witnesses;
            WitnessMembers = witnessMembers;
            Imports = imports;
        }

        public TrRmTypeSpecTableReader TypeSpecs { get; }
        public TrRmTypeDefTableReader TypeDefs { get; }
        public TrRmMethodDefTableReader Methods { get; }
        public TrRmGenericParamTableReader Formals { get; }
        public TrRmConstraintTableReader Constraints { get; }
        public TrRmInterfaceDefTableReader Interfaces { get; }
        public TrRmInterfaceBaseTableReader InterfaceBases { get; }
        public TrRmInterfaceMethodTableReader InterfaceMethods { get; }
        public TrRmInterfaceWitnessTableReader Witnesses { get; }
        public TrRmWitnessMemberTableReader WitnessMembers { get; }
        public TrRmImportTableReader Imports { get; }
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
