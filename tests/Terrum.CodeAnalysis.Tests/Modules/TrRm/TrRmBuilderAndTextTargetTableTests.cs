// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmBuilderAndTextTargetTableTests
{
    [Test]
    public void ReadsAndWritesBuilderAndTextTargetTypeReferences()
    {
        var fixture = CreateFixture();
        var readers = CreateReaders(fixture);
        var builderBytes = TrRmBuilderRegistrationTableWriter.Encode([
            new TrRmBuilderRegistrationRow(1, 1, 1),
        ], readers.TypeDefs, readers.TypeSpecs);
        var builderReader = new TrRmBuilderRegistrationTableReader(
            builderBytes, 1, readers.TypeDefs, readers.TypeSpecs);
        var targetBytes = TrRmTextTargetTableWriter.Encode([
            new TrRmTextTargetRow(1, 1, 0),
        ], readers.Methods, readers.Signatures, readers.TypeSpecs);
        var targetReader = new TrRmTextTargetTableReader(targetBytes, 1,
            readers.Methods, readers.Signatures, readers.TypeSpecs);

        Assert.That(builderReader.GetTargetTypeDef(1), Is.EqualTo(1));
        Assert.That(builderReader.GetSchemaTypeSpecRef(1), Is.EqualTo(1u));
        Assert.That(builderReader.GetRecipeTypeSpecRef(1), Is.EqualTo(1u));
        Assert.That(targetReader.GetOwnerMethodDef(1), Is.EqualTo(1));
        Assert.That(targetReader.GetSchemaTypeSpecRef(1), Is.EqualTo(1u));
        Assert.That(targetReader.GetParameterOrdinal(1), Is.Zero);

        TrRmStructuralMetadataValidator.Validate(fixture.CreateArtifact(
            builderBytes, targetBytes));
    }

    [Test]
    public void BuilderAndTextTargetReadersChargeAtExactWorkBoundary()
    {
        var fixture = CreateFixture();
        var setup = CreateReaders(fixture);
        var builderBytes = TrRmBuilderRegistrationTableWriter.Encode([
            new TrRmBuilderRegistrationRow(1, 1, 1),
        ], setup.TypeDefs, setup.TypeSpecs);
        var targetBytes = TrRmTextTargetTableWriter.Encode([
            new TrRmTextTargetRow(1, 1, 0),
        ], setup.Methods, setup.Signatures, setup.TypeSpecs);

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var readers = CreateReaders(fixture);
            var builders = new TrRmBuilderRegistrationTableReader(
                builderBytes, 1, readers.TypeDefs, readers.TypeSpecs, ledger);
            var targets = new TrRmTextTargetTableReader(targetBytes, 1,
                readers.Methods, readers.Signatures, readers.TypeSpecs, ledger);

            Assert.That(builders.GetRecipeTypeSpecRef(1), Is.EqualTo(1u));
            Assert.That(targets.GetParameterOrdinal(1), Is.Zero);
        });
    }

    [Test]
    public void RejectsInvalidBuilderAndTextTargetReferences()
    {
        var fixture = CreateFixture();
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeBuilders(fixture, [new TrRmBuilderRegistrationRow(2, 1, 1)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeBuilders(fixture, [new TrRmBuilderRegistrationRow(1, 0, 1)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeBuilders(fixture, [new TrRmBuilderRegistrationRow(1, 1, 0)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeTargets(fixture, [new TrRmTextTargetRow(2, 1, 0)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeTargets(fixture, [new TrRmTextTargetRow(1, 0, 0)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeTargets(fixture, [new TrRmTextTargetRow(1, 1, 1)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeTargets(fixture, [
                new TrRmTextTargetRow(1, 1, 0),
                new TrRmTextTargetRow(1, 1, 0),
            ]));
    }

    [Test]
    public void StructuralValidatorRejectsMalformedBuilderAndTextTargetRows()
    {
        var fixture = CreateFixture();
        var builderBytes = EncodeBuilders(fixture, [
            new TrRmBuilderRegistrationRow(1, 1, 1),
        ]);
        var targetBytes = EncodeTargets(fixture, [
            new TrRmTextTargetRow(1, 1, 0),
        ]);

        var malformedBuilder = (byte[])builderBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformedBuilder.AsSpan(4), 0xF400_0008);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(fixture.CreateArtifact(
                malformedBuilder, targetBytes)));

        var malformedTarget = (byte[])targetBytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformedTarget.AsSpan(8), 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(fixture.CreateArtifact(
                builderBytes, malformedTarget)));
    }

    private static byte[] EncodeBuilders(Fixture fixture,
        TrRmBuilderRegistrationRow[] rows)
    {
        var readers = CreateReaders(fixture);
        return TrRmBuilderRegistrationTableWriter.Encode(
            rows, readers.TypeDefs, readers.TypeSpecs);
    }

    private static byte[] EncodeTargets(Fixture fixture, TrRmTextTargetRow[] rows)
    {
        var readers = CreateReaders(fixture);
        return TrRmTextTargetTableWriter.Encode(
            rows, readers.Methods, readers.Signatures, readers.TypeSpecs);
    }

    private static Fixture CreateFixture()
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Write", 0), ("Content", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 5);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 1, 0, 0);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 1, 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1,
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
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 1, 1, 0, 0),
        ], typeSpecs, 1, 0);
        var signatures = new TrRmMethodSigTableReader(
            signatureBytes, 1, typeSpecs, 1, 0);
        var parameterBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, 1, 0),
        ], signatures, typeSpecs);

        _ = new TrRmParamTableReader(
            parameterBytes, 1, signatures, typeSpecs);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(1, 4, 1, 2, 0, 0, 0),
        ], strings, modules, typeDefs, signatures, 0, 0);
        return new Fixture(stringBytes, moduleBytes, typeSpecBytes,
            typeDefBytes, layoutBytes, signatureBytes, parameterBytes, methodBytes);
    }

    private static Readers CreateReaders(Fixture fixture)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 5);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 1, 1, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs,
            1, 1, 0, 0, 0);
        var typeDefs = new TrRmTypeDefTableReader(fixture.TypeDefs, 1,
            strings, modules, 0, 0, 1, 0);
        var signatures = new TrRmMethodSigTableReader(
            fixture.Signatures, 1, typeSpecs, 1, 0);
        var methods = new TrRmMethodDefTableReader(fixture.Methods, 1,
            strings, modules, typeDefs, signatures, 0, 0);
        return new Readers(typeDefs, typeSpecs, methods, signatures);
    }

    private sealed record Fixture(
        byte[] Strings, byte[] Modules, byte[] TypeSpecs, byte[] TypeDefs,
        byte[] Layouts, byte[] Signatures, byte[] Parameters, byte[] Methods)
    {
        public byte[] CreateArtifact(byte[] builderRegistrations, byte[] textTargets)
            => TrRmArtifactWriter.Encode([
                new TrRmSection(0x0001, 0, 1, Modules),
                new TrRmSection(0x0002, 0, 5, Strings),
                new TrRmSection(0x0011, 0, 1, TypeSpecs),
                new TrRmSection(0x0013, 0, 1, TypeDefs),
                new TrRmSection(0x0015, 0, 1, Layouts),
                new TrRmSection(0x0016, 0, 1, Methods),
                new TrRmSection(0x0017, 0, 1, Signatures),
                new TrRmSection(0x0018, 0, 1, Parameters),
                new TrRmSection(0x002B, 0,
                    (uint)(builderRegistrations.Length / 12), builderRegistrations),
                new TrRmSection(0x002C, 0, (uint)(textTargets.Length / 12), textTargets),
            ]);
    }

    private readonly ref struct Readers
    {
        public Readers(TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs, TrRmMethodDefTableReader methods,
            TrRmMethodSigTableReader signatures)
        {
            TypeDefs = typeDefs;
            TypeSpecs = typeSpecs;
            Methods = methods;
            Signatures = signatures;
        }

        public TrRmTypeDefTableReader TypeDefs { get; }
        public TrRmTypeSpecTableReader TypeSpecs { get; }
        public TrRmMethodDefTableReader Methods { get; }
        public TrRmMethodSigTableReader Signatures { get; }
    }
}
