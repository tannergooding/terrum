// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmGenericConstantFeatureUseTests
{
    [Test]
    public void ConstructedConstantSlotsRequireFeature24EvenForFrozenValues()
    {
        ValidateConstructedUse(includeFeature24: true, constantSourceKind: 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateConstructedUse(includeFeature24: false,
                constantSourceKind: 1));
    }

    [Test]
    public void SelectedCg1StructuralValidatorEnforcesTypeDefFormalFeature()
    {
        var supportedArtifact = CreateTypeDefConstantFormalArtifact(
            includeFeature24: true);
        TrRmStructuralMetadataValidator.Validate(
            TrRmArtifactIndex.ForGenericConstantMethodsDraft(
                supportedArtifact));

        var missingFeatureArtifact = CreateTypeDefConstantFormalArtifact(
            includeFeature24: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                TrRmArtifactIndex.ForGenericConstantMethodsDraft(
                    missingFeatureArtifact)));
    }

    [Test]
    public void SelectedCg1StructuralValidatorGatesOnlyRetainedTemplateUses()
    {
        var unreferencedTemplate = CreateConstantTemplateBodyArtifact(
            includeFeature24: false, useTemplate: false);
        TrRmStructuralMetadataValidator.Validate(
            TrRmArtifactIndex.ForGenericConstantMethodsDraft(
                unreferencedTemplate));

        var missingFeature = CreateConstantTemplateBodyArtifact(
            includeFeature24: false, useTemplate: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                TrRmArtifactIndex.ForGenericConstantMethodsDraft(
                    missingFeature)));

        var retainedTemplate = CreateConstantTemplateBodyArtifact(
            includeFeature24: true, useTemplate: true);
        TrRmStructuralMetadataValidator.Validate(
            TrRmArtifactIndex.ForGenericConstantMethodsDraft(
                retainedTemplate));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(retainedTemplate));
    }

    [Test]
    public void RetainedConstantSourceTagsRequireFeature24ButUnreferencedRowsDoNot()
    {
        ValidateSourceUse(includeFeature24: true, constantSourceKind: 4);
        ValidateSourceUse(includeFeature24: true, constantSourceKind: 5);
        ValidateSourceUse(includeFeature24: false, constantSourceKind: 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateSourceUse(includeFeature24: false, constantSourceKind: 4));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateSourceUse(includeFeature24: false, constantSourceKind: 5));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateSourceUse(includeFeature24: true, constantSourceKind: 4,
                formalKind: 1));

        _ = CreateContext(includeFeature24: false, constantSourceKind: 5);
    }

    [Test]
    public void ConstantSourceTag4MustNameAConstantFormalEvenWhenUnreferenced()
    {
        var constantFormal = CreateContext(includeFeature24: false,
            constantSourceKind: 4);
        TrRmGenericConstantFeatureUseValidator.ValidateConstantSourceRows(
            constantFormal.ConstantSources, constantFormal.GenericParams,
            TrRmArtifactSchema.GenericConstantMethodsDraft);

        var typeFormal = CreateContext(includeFeature24: false,
            constantSourceKind: 4, formalKind: 1);
        var rejected = false;
        try
        {
            ValidateConstantSourceFormalKind(typeFormal);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        Assert.That(rejected, Is.True);
    }

    [Test]
    public void GenericCallConstantActualsAndConstructedTypeActualsRequireFeature24()
    {
        ValidateGenericCallUse(includeFeature23: true, includeFeature24: true,
            argumentKind: 2,
            argumentReference: 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateGenericCallUse(includeFeature23: true,
                includeFeature24: false, argumentKind: 2,
                argumentReference: 1));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateGenericCallUse(includeFeature23: true,
                includeFeature24: false, argumentKind: 1,
                argumentReference: 3));
    }

    [Test]
    public void BodyTypeMapAndConstantMapUsesRequireTheirOwnFeatureTriggers()
    {
        ValidateBodyTypeMap(includeFeature24: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateBodyTypeMap(includeFeature24: false));

        ValidateBodyConstantMap(includeFeature24: true,
            constantSourceKind: 4);
        ValidateBodyConstantMap(includeFeature24: true,
            constantSourceKind: 5);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateBodyConstantMap(includeFeature24: false,
                constantSourceKind: 4));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateBodyConstantMap(includeFeature24: false,
                constantSourceKind: 5));
    }

    [Test]
    public void RetainedGenericCallBodyRequiresFeatures23And24Independently()
    {
        ValidateBodyGenericCall(includeFeature23: true, includeFeature24: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateBodyGenericCall(includeFeature23: true,
                includeFeature24: false));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateBodyGenericCall(includeFeature23: false,
                includeFeature24: true));
    }

    private static void ValidateConstructedUse(bool includeFeature24,
        uint constantSourceKind)
    {
        var context = CreateContext(includeFeature24, constantSourceKind);
        TrRmGenericConstantFeatureUseValidator.ValidateTypeSpec(3, 1,
            context.TypeSpecs, context.Types, context.GenericParams,
            default, context.ConstantSources, context.Features,
            context.Modules, TrRmArtifactSchema.GenericConstantMethodsDraft,
            []);
    }

    private static void ValidateSourceUse(bool includeFeature24,
        uint constantSourceKind, byte formalKind = 2)
    {
        var context = CreateContext(includeFeature24, constantSourceKind,
            formalKind: formalKind);
        TrRmGenericConstantFeatureUseValidator.ValidateConstantSourceUse(1, 1,
            context.ConstantSources, context.GenericParams, context.Features,
            context.Modules,
            TrRmArtifactSchema.GenericConstantMethodsDraft);
    }

    private static void ValidateConstantSourceFormalKind(Context context)
        => TrRmGenericConstantFeatureUseValidator.ValidateConstantSourceRows(
            context.ConstantSources, context.GenericParams,
            TrRmArtifactSchema.GenericConstantMethodsDraft);

    private static void ValidateGenericCallUse(bool includeFeature23,
        bool includeFeature24, byte argumentKind, uint argumentReference)
    {
        var context = CreateContext(includeFeature24, constantSourceKind: 1,
            includeFeature23: includeFeature23);
        var payload = new byte[24];
        payload[0] = 1;
        Write(payload, 12, 1);
        payload[16] = argumentKind;
        Write(payload, 20, argumentReference);
        TrRmGenericConstantFeatureUseValidator.ValidateGenericCall(payload, 1,
            context.TypeSpecs, context.Types, context.GenericParams, default,
            context.ConstantSources, context.Features, context.Modules,
            TrRmArtifactSchema.GenericConstantMethodsDraft,
            []);
    }

    private static void ValidateBodyTypeMap(bool includeFeature24)
    {
        var context = CreateContext(includeFeature24, constantSourceKind: 1);
        var typeMap = new byte[sizeof(uint)];
        Write(typeMap, 0, 0x1100_0003);
        ValidateBody(context, typeMap, [],
            [], []);
    }

    private static void ValidateBodyConstantMap(bool includeFeature24,
        uint constantSourceKind)
    {
        var context = CreateContext(includeFeature24, constantSourceKind);
        var constantMap = new byte[2 * sizeof(uint)];
        Write(constantMap, 0, 1);
        Write(constantMap, sizeof(uint), 1);
        ValidateBody(context, [], [],
            constantMap, []);
    }

    private static void ValidateBodyGenericCall(bool includeFeature23,
        bool includeFeature24)
    {
        var context = CreateContext(includeFeature24, constantSourceKind: 1,
            includeFeature23: includeFeature23);
        var methodMap = new byte[sizeof(uint)];
        Write(methodMap, 0, 0x1600_0001);
        var schema = TrRmArtifactSchema.GenericConstantMethodsDraft;
        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(
            4, 0, 2, 0, [], [], schema: schema);
        var target = TrRmRegisterBodyRecordWriter.EncodeGenericCallTarget(
            1, 0, 0, 0, 0,
            [new TrRmRegisterBodyGenericCallArgument(2, 1)], schema);
        ValidateBody(context, [], methodMap,
            [],
            [callSite, target]);
    }

    private static void ValidateBody(Context context, byte[] typeMap,
        byte[] methodMap, byte[] constantMap, byte[][] additionalRecords)
    {
        var entryState = new byte[8];
        entryState[0] = 1;
        var records = new ReadOnlyMemory<byte>[additionalRecords.Length + 1];
        records[0] = entryState;
        for (var index = 0; index < additionalRecords.Length; index++)
        {
            records[index + 1] = additionalRecords[index];
        }

        byte[] code = additionalRecords.Length == 0
            ? [0x18, 0]
            : [0x90, 1, 0x18, 0];
        var bodyBytes = TrRmRegisterBodyWriter.Encode(default, typeMap,
            methodMap, default, constantMap,
            [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
            records, code,
            schema: TrRmArtifactSchema.GenericConstantMethodsDraft);
        var body = new TrRmRegisterBodyReader(bodyBytes,
            schema: TrRmArtifactSchema.GenericConstantMethodsDraft);
        TrRmGenericConstantFeatureUseValidator.ValidateBody(body, 1,
            context.Types, context.TypeSpecs, context.GenericParams, default,
            context.ConstantSources, context.Features, context.Modules,
            []);
    }

    private static Context CreateContext(bool includeFeature24,
        uint constantSourceKind, bool includeFeature23 = false,
        byte formalKind = 2)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.GenericConstantMethodsDraft;
        var featureRows = new List<TrRmFeatureRow>();
        if (includeFeature23)
        {
            featureRows.Add(TrRmFeatureRow.GenericMethodCallsCandidate);
        }
        if (includeFeature24)
        {
            featureRows.Add(TrRmFeatureRow.ConstantGenericTermsCandidate);
        }
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema);
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("Module", 0), ("", 0), ("Generic", 0), ("P", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 4);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1,
                featureRows.Count == 0 ? 0 : 1, featureRows.Count),
        ], strings, 0, 1, 0, 0, featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 0, 0, featureRows.Count);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(2, 3, 0x06, 0, 0, 1, 1, 0, 0, 0, 0),
        ], strings, modules, 0, 1, 0, 0, schema);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings,
            modules, 0, 1, 0, 0, schema);
        ReadOnlyMemory<byte>[] typeSpecRows = [
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Constructed(2, [1]),
        ];
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            types.Count, 0, 1, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length, types.Count, 0, 1, 0, schema);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 0, 0);
        var methods = new TrRmMethodDefTableReader([], 0, strings, modules,
            types, signatures, 0, 0);
        var genericParam = formalKind == 2
            ? new TrRmGenericParamRow(0x1300_0001, 0, 4, 0, 0,
                2, 0, 0, 1, 0)
            : new TrRmGenericParamRow(0x1300_0001, 0, 4, 0, 0,
                1, 1, 1, 0, 0);
        var genericParamsBytes = TrRmGenericParamTableWriter.Encode([
            genericParam,
        ], strings, types, methods, signatures, typeSpecs, 0,
            constantSourceCount: 1);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamsBytes, 1, strings, types, methods, signatures,
            typeSpecs, 0, 1);
        var frozenCount = constantSourceKind == 1 ? 1 : 0;
        var genericParamCount = constantSourceKind == 4 ? 1 : 0;
        var templateCount = constantSourceKind == 5 ? 1 : 0;
        var sourceBytes = TrRmConstantSourceTableWriter.Encode([
            new TrRmConstantSourceRow(constantSourceKind, 1),
        ], frozenCount, 0, 0, genericParamCount, templateCount, schema);
        var constantSources = new TrRmConstantSourceTableReader(sourceBytes,
            1, frozenCount, 0, 0, genericParamCount, templateCount, schema);
        return new Context(features, modules, types, typeSpecs, genericParams,
            constantSources);
    }

    private static byte[] CreateTypeDefConstantFormalArtifact(
        bool includeFeature24)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.GenericConstantMethodsDraft;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Generic", 0), ("P", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 4);
        var featureRows = includeFeature24
            ? new[] { TrRmFeatureRow.ConstantGenericTermsCandidate }
            : [];
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                featureRows.Length == 0 ? 0 : 1, featureRows.Length),
        ], strings, 0, 1, 0, 0, featureRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 0, 0, featureRows.Length);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x06, 0, 0, 1, 1, 1, 1, 0, 0),
        ], strings, modules, 0, 1, 1, 0, schema);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings,
            modules, 0, 1, 1, 0, schema);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], types.Count, 0, 1, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1,
            types.Count, 0, 1, 0, schema);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0, schema);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 0, 0);
        var methods = new TrRmMethodDefTableReader([], 0, strings, modules,
            types, signatures, 0, 0);
        var genericParamBytes = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1300_0001, 0, 4, 0, 0,
                2, 0, 0, 1, 0),
        ], strings, types, methods, signatures, typeSpecs, 0);
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 4, stringBytes),
        };
        if (featureRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                (uint)featureRows.Length, featureBytes));
        }

        sections.AddRange([
            new(0x0011, 0, 1, typeSpecBytes),
            new(0x0013, 0, 1, typeBytes),
            new(0x0015, 0, 1, layoutBytes),
            new(0x0017, 0, 1, signatureBytes),
            new(0x0019, 0, 1, genericParamBytes),
        ]);
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] CreateConstantTemplateBodyArtifact(
        bool includeFeature24, bool useTemplate)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.GenericConstantMethodsDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("ConstEval", 0), ("Template", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 4);
        var featureRows = includeFeature24
            ? new[] { TrRmFeatureRow.ConstantGenericTermsCandidate }
            : [];
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                featureRows.Length == 0 ? 0 : 1, featureRows.Length),
        ], strings, 0, 0, 1, 0, featureRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 1, 0, featureRows.Length);
        var types = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0, schema);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 0, 0, 0, 0, schema, methodDefCount: 1);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1,
            0, 0, 0, 0, schema, methodDefCount: 1);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 0, 0, 0, 1),
        ], strings, modules, types, signatures, 0, 1);
        var templates = TrRmConstantTemplateTableWriter.Encode([
            new TrRmConstantTemplateRow(0x1600_0001, 1, 0, []),
        ], schema);
        var constantSources = TrRmConstantSourceTableWriter.Encode([
            new TrRmConstantSourceRow(5, 1),
        ], 0, 0, 0, 0, 1, schema);
        var constantMap = useTemplate
            ? new byte[2 * sizeof(uint)]
            : [];
        if (useTemplate)
        {
            Write(constantMap, 0, 1);
            Write(constantMap, sizeof(uint), 1);
        }

        var entryState = new byte[8];
        entryState[0] = 1;
        byte[] code = [0x18, 0];
        var bodyBytes = TrRmRegisterBodyWriter.Encode(
            Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>(),
            Array.Empty<byte>(), constantMap,
            [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
            [entryState], code, schema: schema);
        var bodySection = TrRmVariableTableWriter.EncodeBodies([bodyBytes]);

        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 4, stringsBytes),
        };
        if (featureRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                (uint)featureRows.Length, featureBytes));
        }

        sections.AddRange([
            new(0x0011, 0, 1, typeSpecBytes),
            new(0x0016, 0, 1, methodBytes),
            new(0x0017, 0, 1, signatureBytes),
            new(0x0020, 0, 1, bodySection),
            new(0x0030, 0, 1, constantSources),
            new(0x0064, 0, 1, templates),
        ]);
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private readonly ref struct Context(
        TrRmFeatureTableReader features,
        TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmConstantSourceTableReader constantSources)
    {
        internal TrRmFeatureTableReader Features { get; } = features;
        internal TrRmModuleTableReader Modules { get; } = modules;
        internal TrRmTypeDefTableReader Types { get; } = types;
        internal TrRmTypeSpecTableReader TypeSpecs { get; } = typeSpecs;
        internal TrRmGenericParamTableReader GenericParams { get; }
            = genericParams;
        internal TrRmConstantSourceTableReader ConstantSources { get; }
            = constantSources;
    }
}
