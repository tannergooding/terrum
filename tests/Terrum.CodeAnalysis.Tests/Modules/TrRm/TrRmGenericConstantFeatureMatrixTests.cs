// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmGenericConstantFeatureMatrixTests
{
    [Test]
    public void ExpectedAndProviderTypeViewsRequireFeature24PerOwningModule()
    {
        var supported = CreateContext(moduleOneFormal: true,
            moduleTwoFormal: true, moduleOneFeature24: true,
            moduleTwoFeature24: true, constantSourceKind: 1,
            includeExpectedTypeImport: true);
        ValidateFormals(supported);
        Assert.That(supported.Imports.IsExpectedDefinition(0x1300_0002),
            Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(supported.Features,
            supported.Modules, 1,
            TrRmFeatureTableReader.ConstantGenericTermsFeatureId,
            TrRmArtifactSchema.GenericConstantMethodsDraft), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(supported.Features,
            supported.Modules, 2,
            TrRmFeatureTableReader.ConstantGenericTermsFeatureId,
            TrRmArtifactSchema.GenericConstantMethodsDraft), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(supported.Features,
            supported.Modules, 1,
            TrRmFeatureTableReader.GenericMethodCallsFeatureId,
            TrRmArtifactSchema.GenericConstantMethodsDraft), Is.False);
        ValidateTypeUse(supported,
            checked((uint)supported.ConstructedTypeTwoRow), 2);

        var missingExpectedFeature = CreateContext(moduleOneFormal: true,
            moduleTwoFormal: true, moduleOneFeature24: true,
            moduleTwoFeature24: false, constantSourceKind: 1,
            includeExpectedTypeImport: true);
        var rejected = false;
        try
        {
            ValidateFormals(missingExpectedFeature);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        Assert.That(rejected, Is.True);
    }

    [Test]
    public void UnreferencedGlobalConstantSourcesDoNotRequireEveryModuleFeature()
    {
        var context = CreateContext(moduleOneFormal: true,
            moduleTwoFormal: false, moduleOneFeature24: true,
            moduleTwoFeature24: false, constantSourceKind: 4);

        ValidateFormals(context);
        TrRmGenericConstantFeatureUseValidator.ValidateConstantSourceRows(
            context.ConstantSources, context.GenericParams,
            TrRmArtifactSchema.GenericConstantMethodsDraft);
        Assert.That(TrRmFeatureManifestValidator.Requires(context.Features,
            context.Modules, 2,
            TrRmFeatureTableReader.ConstantGenericTermsFeatureId,
            TrRmArtifactSchema.GenericConstantMethodsDraft), Is.False);

        var rejected = false;
        try
        {
            ValidateSourceUse(context, 2);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        Assert.That(rejected, Is.True);
    }

    [Test]
    public void TypeSpecAndBodyUsesRequireFeature24InTheirUsingModule()
    {
        var missingTypeUseFeature = CreateContext(moduleOneFormal: true,
            moduleTwoFormal: false, moduleOneFeature24: true,
            moduleTwoFeature24: false, constantSourceKind: 1);
        var rejected = false;
        try
        {
            ValidateTypeUse(missingTypeUseFeature,
                checked((uint)missingTypeUseFeature.ConstructedTypeOneRow), 2);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        Assert.That(rejected, Is.True);

        var supportedTypeUse = CreateContext(moduleOneFormal: true,
            moduleTwoFormal: false, moduleOneFeature24: true,
            moduleTwoFeature24: true, constantSourceKind: 1);
        ValidateTypeUse(supportedTypeUse,
            checked((uint)supportedTypeUse.ConstructedTypeOneRow), 2);

        var missingBodyFeature = CreateContext(moduleOneFormal: true,
            moduleTwoFormal: false, moduleOneFeature24: true,
            moduleTwoFeature24: false, moduleTwoFeature23: true,
            constantSourceKind: 1);
        rejected = false;
        try
        {
            ValidateGenericCallUse(missingBodyFeature, 2);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        Assert.That(rejected, Is.True);

        var supportedBodyFeature = CreateContext(moduleOneFormal: true,
            moduleTwoFormal: false, moduleOneFeature24: true,
            moduleTwoFeature24: true, moduleTwoFeature23: true,
            constantSourceKind: 1);
        ValidateGenericCallUse(supportedBodyFeature, 2);
    }

    [Test]
    public void EnumFeature18AndItsFeature11PrerequisiteArePerModule()
    {
        Assert.That(ValidateEnumModuleMatrix(moduleTwoFeature18: true),
            Is.True);
        var rejected = false;
        try
        {
            _ = ValidateEnumModuleMatrix(moduleTwoFeature18: false);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        Assert.That(rejected, Is.True);
    }

    [Test]
    public void WrapperAndBlittableLayoutFeaturesArePerExpectedModuleView()
    {
        Assert.That(ValidateWrapperModuleMatrix(
            moduleTwoFeature24: true, moduleTwoFeature1: true),
            Is.True);
        var missingWrapperFeature = false;
        try
        {
            _ = ValidateWrapperModuleMatrix(moduleTwoFeature24: false,
                moduleTwoFeature1: true);
        }
        catch (InvalidDataException)
        {
            missingWrapperFeature = true;
        }
        Assert.That(missingWrapperFeature, Is.True);

        var missingLayoutFeature = false;
        try
        {
            _ = ValidateWrapperModuleMatrix(moduleTwoFeature24: true,
                moduleTwoFeature1: false);
        }
        catch (InvalidDataException)
        {
            missingLayoutFeature = true;
        }
        Assert.That(missingLayoutFeature, Is.True);
    }

    private static void ValidateFormals(Context context)
    {
        TrRmFeatureManifestValidator.Validate(context.Features,
            context.Modules, TrRmArtifactSchema.GenericConstantMethodsDraft);
        TrRmStructuralMetadataValidator.ValidateGenericConstantFeatureAssignments(
            context.Features, context.Modules, context.Types,
            context.Methods, context.GenericParams,
            TrRmArtifactSchema.GenericConstantMethodsDraft);
        TrRmStructuralMetadataValidator.ValidateGenericMethodFeatureAssignments(
            context.Features, context.Modules, context.Methods,
            context.Signatures,
            TrRmArtifactSchema.GenericConstantMethodsDraft);
    }

    private static void ValidateSourceUse(Context context, int ownerModule)
        => TrRmGenericConstantFeatureUseValidator.ValidateConstantSourceUse(
            1, ownerModule, context.ConstantSources,
            context.GenericParams, context.Features, context.Modules,
            TrRmArtifactSchema.GenericConstantMethodsDraft);

    private static void ValidateTypeUse(Context context, uint typeSpecRef,
        int ownerModule)
        => TrRmGenericConstantFeatureUseValidator.ValidateTypeSpec(
            typeSpecRef, ownerModule, context.TypeSpecs, context.Types,
            context.GenericParams, context.Imports, context.ConstantSources,
            context.Features, context.Modules,
            TrRmArtifactSchema.GenericConstantMethodsDraft,
            new HashSet<int>());

    private static void ValidateGenericCallUse(Context context, int ownerModule)
    {
        var methodMap = new byte[sizeof(uint)];
        Write(methodMap, 0, 0x1600_0001);
        var schema = TrRmArtifactSchema.GenericConstantMethodsDraft;
        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(
            4, 0, 2, 0, [], [], schema: schema);
        var target = TrRmRegisterBodyRecordWriter.EncodeGenericCallTarget(
            1, 0, 0, 0, 0,
            [new TrRmRegisterBodyGenericCallArgument(2, 1)], schema);
        var entryState = new byte[8];
        entryState[0] = 1;
        byte[] code = [0x90, 1, 0x18, 0];
        var bodyBytes = TrRmRegisterBodyWriter.Encode(
            Array.Empty<byte>(), Array.Empty<byte>(), methodMap,
            Array.Empty<byte>(), Array.Empty<byte>(),
            [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
            [entryState, callSite, target], code, schema: schema);
        var body = new TrRmRegisterBodyReader(bodyBytes, schema: schema);
        TrRmGenericConstantFeatureUseValidator.ValidateBody(body,
            ownerModule, context.Types, context.TypeSpecs,
            context.GenericParams, context.Imports,
            context.ConstantSources, context.Features, context.Modules,
            new HashSet<int>());
    }

    private static Context CreateContext(bool moduleOneFormal,
        bool moduleTwoFormal, bool moduleOneFeature24,
        bool moduleTwoFeature24, uint constantSourceKind,
        bool includeExpectedTypeImport = false,
        bool moduleTwoFeature23 = false)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.GenericConstantMethodsDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("Provider", 0), ("Consumer", 0), ("", 0), ("Generic", 0),
            ("P1", 0), ("P2", 0), ("GenericKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 7);
        var featureRows = new List<TrRmFeatureRow>();
        if (moduleTwoFeature23)
        {
            featureRows.Add(TrRmFeatureRow.GenericMethodCallsCandidate);
        }
        if (moduleOneFeature24 || moduleTwoFeature24)
        {
            featureRows.Add(TrRmFeatureRow.ConstantGenericTermsCandidate);
        }

        var feature24Row = featureRows.FindIndex(static feature
            => feature.FeatureId
                == TrRmFeatureTableReader.ConstantGenericTermsFeatureId) + 1;
        var feature23Row = featureRows.FindIndex(static feature
            => feature.FeatureId
                == TrRmFeatureTableReader.GenericMethodCallsFeatureId) + 1;
        var moduleOneFeatureFirst = moduleOneFeature24
            ? feature24Row : 0;
        var moduleOneFeatureCount = moduleOneFeature24 ? 1 : 0;
        var moduleTwoFeatureFirst = moduleTwoFeature23
            ? feature23Row
            : moduleTwoFeature24 ? feature24Row : 0;
        var moduleTwoFeatureCount = moduleTwoFeature23 && moduleTwoFeature24
            ? 2 : (moduleTwoFeature23 || moduleTwoFeature24) ? 1 : 0;
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema);

        var moduleRefCount = includeExpectedTypeImport ? 1 : 0;
        var moduleRows = new[]
        {
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1,
                moduleOneFeatureFirst, moduleOneFeatureCount),
            new TrRmModuleRow(2, 1, 0, 0, 1, 2, 1, 1,
                moduleTwoFeatureFirst, moduleTwoFeatureCount),
        };
        var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows,
            strings, moduleRefCount, 2, 0, 0, featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 2,
            strings, moduleRefCount, 2, 0, 0, featureRows.Count);

        var genericCount = (moduleOneFormal ? 1 : 0)
            + (moduleTwoFormal ? 1 : 0);
        var moduleTwoFormalFirst = moduleTwoFormal
            ? (moduleOneFormal ? 2 : 1) : 0;
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(3, 4, 0x06, 0, 0,
                moduleOneFormal ? 1 : 0, moduleOneFormal ? 1 : 0,
                0, 0, 0, 0),
            new TrRmTypeDefRow(3, 4, 0x06, 0, 0,
                moduleTwoFormalFirst, moduleTwoFormal ? 1 : 0,
                0, 0, 0, 0),
        ], strings, modules, 0, genericCount, 0, 0, schema);
        var types = new TrRmTypeDefTableReader(typeBytes, 2, strings,
            modules, 0, genericCount, 0, 0, schema);

        ReadOnlyMemory<byte>[] typeSpecRows = [
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(
                includeExpectedTypeImport ? (byte)0x03 : (byte)0x02,
                includeExpectedTypeImport ? 1u : 2u),
        ];
        var constructedTypeOneRow = 0;
        var constructedTypeTwoRow = 0;
        var mutableTypeSpecRows = new List<ReadOnlyMemory<byte>>(typeSpecRows);
        if (moduleOneFormal && constantSourceKind != 0)
        {
            mutableTypeSpecRows.Add(
                TrRmTypeSpecTableWriter.Constructed(2, [1]));
            constructedTypeOneRow = mutableTypeSpecRows.Count;
        }
        if (moduleTwoFormal && constantSourceKind != 0)
        {
            mutableTypeSpecRows.Add(
                TrRmTypeSpecTableWriter.Constructed(3, [1]));
            constructedTypeTwoRow = mutableTypeSpecRows.Count;
        }

        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(mutableTypeSpecRows,
            types.Count, moduleRefCount, genericCount, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            mutableTypeSpecRows.Count, types.Count, moduleRefCount,
            genericCount, 0, schema);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 0, 0);
        var methods = new TrRmMethodDefTableReader([], 0, strings, modules,
            types, signatures, 0, 0);

        var genericRows = new List<TrRmGenericParamRow>();
        if (moduleOneFormal)
        {
            genericRows.Add(new TrRmGenericParamRow(0x1300_0001, 0, 5,
                0, 0, 2, 0, 0, 1, 0));
        }
        if (moduleTwoFormal)
        {
            genericRows.Add(new TrRmGenericParamRow(0x1300_0002, 0, 6,
                0, 0, 2, 0, 0, 1, 0));
        }
        var sourceCount = constantSourceKind == 0 ? 0 : 1;
        var genericParamBytes = genericRows.Count == 0 ? Array.Empty<byte>()
            : TrRmGenericParamTableWriter.Encode(genericRows, strings, types,
                methods, signatures, typeSpecs, 0, sourceCount);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamBytes, genericRows.Count, strings, types, methods,
            signatures, typeSpecs, 0, sourceCount);

        var frozenCount = constantSourceKind == 1 ? 1 : 0;
        var sourceFormalCount = constantSourceKind == 4 ? genericCount : 0;
        var templateCount = constantSourceKind == 5 ? 1 : 0;
        var sourceRows = constantSourceKind == 0
            ? Array.Empty<TrRmConstantSourceRow>()
            : [new TrRmConstantSourceRow(constantSourceKind, 1)];
        var sourceBytes = TrRmConstantSourceTableWriter.Encode(sourceRows,
            frozenCount, 0, 0, sourceFormalCount, templateCount, schema);
        var constantSources = new TrRmConstantSourceTableReader(sourceBytes,
            sourceRows.Length, frozenCount, 0, 0, sourceFormalCount,
            templateCount, schema);

        var moduleRefs = default(TrRmModuleRefTableReader);
        var imports = default(TrRmImportTableReader);
        if (includeExpectedTypeImport)
        {
            var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
                new TrRmModuleRefRow(1, 1, 0),
            ], strings, modules);
            moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes, 1,
                strings, modules);
            var importBytes = TrRmImportTableWriter.Encode([
                new TrRmImportRow(1, 7, 0x1300_0002),
            ], strings, moduleRefs, types, methods, default);
            imports = new TrRmImportTableReader(importBytes, 1, strings,
                moduleRefs, types, methods, default);
        }

        return new Context(features, modules, types, typeSpecs, signatures,
            methods, genericParams, constantSources, imports,
            constructedTypeOneRow, constructedTypeTwoRow);
    }

    private static bool ValidateEnumModuleMatrix(bool moduleTwoFeature18)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.GenericConstantMethodsDraft;
        var featureBytes = TrRmFeatureTableWriter.Encode([
            new TrRmFeatureRow(18, 1),
        ], schema);
        var features = new TrRmFeatureTableReader(featureBytes, 1, schema);
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("ModuleOne", 0), ("ModuleTwo", 0), ("", 0),
            ("EnumOne", 0), ("EnumTwo", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 5);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1, 1, 1),
            new TrRmModuleRow(2, 1, 0, 0, 1, 2, 1, 1,
                moduleTwoFeature18 ? 1 : 0,
                moduleTwoFeature18 ? 1 : 0),
        ], strings, 0, 2, 0, 0, 1);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, strings,
            0, 2, 0, 0, 1);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(3, 4, 0x12, 0, 0, 0, 0, 1, 1, 0, 0),
            new TrRmTypeDefRow(3, 5, 0x12, 0, 0, 0, 0, 2, 1, 0, 0),
        ], strings, modules, 0, 0, 2, 0, schema);
        var types = new TrRmTypeDefTableReader(typeBytes, 2, strings,
            modules, 0, 0, 2, 0, schema);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 2, 0, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1,
            2, 0, 0, 0, schema);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0, schema);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 2, types,
            targetProfiles, 0);
        var enumDefs = new byte[40];
        Write(enumDefs, 0, 1);
        Write(enumDefs, 20, 2);

        TrRmStructuralMetadataValidator.ValidateGenericConstantCarrierFeatureAssignments(
            enumDefs, 2, [], 0, types, default, typeSpecs, layouts,
            features, modules, schema);
        return TrRmFeatureManifestValidator.Requires(features, modules,
            2, TrRmFeatureTableReader.TypedFrozenValuesFeatureId, schema);
    }

    private static bool ValidateWrapperModuleMatrix(bool moduleTwoFeature24,
        bool moduleTwoFeature1)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.GenericConstantMethodsDraft;
        var featureRows = new List<TrRmFeatureRow>();
        if (moduleTwoFeature1)
        {
            featureRows.Add(TrRmFeatureRow.NativeBoundaryContracts);
        }
        featureRows.Add(TrRmFeatureRow.ConstantGenericTermsCandidate);
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema);
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("Provider", 0), ("Consumer", 0), ("", 0),
            ("Wrapper", 0), ("carrier", 0), ("WrapperKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 6);
        var moduleTwoFeatureFirst = moduleTwoFeature1 ? 1
            : moduleTwoFeature24 ? featureRows.Count : 0;
        var moduleTwoFeatureCount = moduleTwoFeature1
            ? (moduleTwoFeature24 ? 2 : 1)
            : moduleTwoFeature24 ? 1 : 0;
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1,
                featureRows.Count, 1),
            new TrRmModuleRow(2, 1, 0, 0, 1, 2, 1, 1,
                moduleTwoFeatureFirst, moduleTwoFeatureCount),
        ], strings, 1, 2, 0, 0, featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, strings,
            1, 2, 0, 0, featureRows.Count);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(3, 4, 0x06, 1, 1, 0, 0, 1, 1, 0, 0),
            new TrRmTypeDefRow(3, 4, 0x06, 2, 1, 0, 0, 2, 2, 0, 0),
        ], strings, modules, 2, 0, 3, 0, schema);
        var types = new TrRmTypeDefTableReader(typeBytes, 2, strings,
            modules, 2, 0, 3, 0, schema);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 2, 1, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1,
            2, 1, 0, 0, schema);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 5, 1, 0),
            new TrRmFieldDefRow(2, 5, 1, 0),
        ], strings, types, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 2, strings,
            types, typeSpecs);
        byte[] targetProfileBytes = [1, 0, 0, 0, 1, 0, 0, 0];
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, 1, schema);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 1, 0, 1, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 3, types,
            targetProfiles, 0);
        var domains = new byte[24];
        Write(domains, 0, 1);
        Write(domains, 4, 1);
        Write(domains, 8, 1);
        Write(domains, 12, 2);
        Write(domains, 16, 1);
        Write(domains, 20, 2);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(1, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(
            moduleRefBytes, 1, strings, modules);
        var importBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 6, 0x1300_0002),
        ], strings, moduleRefs, types, default, default);
        var imports = new TrRmImportTableReader(importBytes, 1, strings,
            moduleRefs, types, default, default);
        Assert.That(imports.IsExpectedDefinition(0x1300_0002), Is.True);

        TrRmStructuralMetadataValidator.ValidateGenericConstantCarrierFeatureAssignments(
            [], 0, domains, 2, types, fields, typeSpecs, layouts,
            features, modules, schema);
        return TrRmFeatureManifestValidator.Requires(features, modules,
            2, TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
            schema);
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private ref struct Context(
        TrRmFeatureTableReader features,
        TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodSigTableReader signatures,
        TrRmMethodDefTableReader methods,
        TrRmGenericParamTableReader genericParams,
        TrRmConstantSourceTableReader constantSources,
        TrRmImportTableReader imports,
        int constructedTypeOneRow,
        int constructedTypeTwoRow)
    {
        internal TrRmFeatureTableReader Features { get; } = features;
        internal TrRmModuleTableReader Modules { get; } = modules;
        internal TrRmTypeDefTableReader Types { get; } = types;
        internal TrRmTypeSpecTableReader TypeSpecs { get; } = typeSpecs;
        internal TrRmMethodSigTableReader Signatures { get; } = signatures;
        internal TrRmMethodDefTableReader Methods { get; } = methods;
        internal TrRmGenericParamTableReader GenericParams { get; }
            = genericParams;
        internal TrRmConstantSourceTableReader ConstantSources { get; }
            = constantSources;
        internal TrRmImportTableReader Imports { get; } = imports;
        internal int ConstructedTypeOneRow { get; } = constructedTypeOneRow;
        internal int ConstructedTypeTwoRow { get; } = constructedTypeTwoRow;
    }
}
