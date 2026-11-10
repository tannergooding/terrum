// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmGenericConstantCarrierFeatureTests
{
    [Test]
    public void EnumDeclarationsRequireFeature18AndItsFeature11Prerequisite()
    {
        Assert.That(ValidateEnumFeature(includeFeature18: true), Is.True);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateEnumFeature(includeFeature18: false));
    }

    [Test]
    public void TransparentDomainRequiresFeature24AndValidatesItsCarrierField()
    {
        Assert.That(ValidateWrapper(includeFeature24: true,
            includeNativeBoundaryFeature: false, blittableLayout: false,
            carrierField: 1), Is.EqualTo((true, false)));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateWrapper(includeFeature24: false,
                includeNativeBoundaryFeature: false, blittableLayout: false,
                carrierField: 1));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateWrapper(includeFeature24: true,
                includeNativeBoundaryFeature: false, blittableLayout: false,
                carrierField: 2));
    }

    [Test]
    public void BlittableLayoutsRetainTheirNativeBoundaryFeatureRequirement()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateWrapper(includeFeature24: true,
                includeNativeBoundaryFeature: false, blittableLayout: true,
                carrierField: 1));
        Assert.That(ValidateWrapper(includeFeature24: true,
            includeNativeBoundaryFeature: true, blittableLayout: true,
            carrierField: 1), Is.EqualTo((true, true)));
    }

    private static bool ValidateEnumFeature(bool includeFeature18)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.GenericConstantMethodsDraft;
        var featureRows = includeFeature18
            ? new[] { new TrRmFeatureRow(18, 1) }
            : Array.Empty<TrRmFeatureRow>();
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Length, schema);
        var stringsBytes = Strings();
        var strings = new TrRmStringTableReader(stringsBytes, 5);
        var moduleBytes = Module(strings, featureRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 0, 0, featureRows.Length);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x12, 0, 0, 0, 0, 1, 1, 0, 0),
        ], strings, modules, 0, 0, 1, 0, schema);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings,
            modules, 0, 0, 1, 0, schema);
        var typeSpecs = TypeSpecs(types.Count, 0, schema);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0, schema);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 1, types,
            targetProfiles, 0);
        var enumDefs = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(enumDefs, 1);

        TrRmStructuralMetadataValidator.ValidateGenericConstantCarrierFeatureAssignments(
            enumDefs, 1, [], 0, types, default, typeSpecs, layouts,
            features, modules, schema);
        return TrRmFeatureManifestValidator.Requires(features, modules,
            1, TrRmFeatureTableReader.TypedFrozenValuesFeatureId, schema);
    }

    private static (bool ConstantGenericFeature, bool NativeBoundaryFeature)
        ValidateWrapper(bool includeFeature24,
            bool includeNativeBoundaryFeature, bool blittableLayout,
            uint carrierField)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.GenericConstantMethodsDraft;
        var featureRows = new System.Collections.Generic.List<TrRmFeatureRow>();
        if (includeFeature24)
        {
            featureRows.Add(TrRmFeatureRow.ConstantGenericTermsCandidate);
        }
        if (includeNativeBoundaryFeature)
        {
            featureRows.Add(TrRmFeatureRow.NativeBoundaryContracts);
        }

        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema);
        var stringsBytes = Strings();
        var strings = new TrRmStringTableReader(stringsBytes, 5);
        var layoutCount = blittableLayout ? 2 : 1;
        var moduleBytes = Module(strings, featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 0, 0, featureRows.Count);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 4, 0x06, 1, 1, 0, 0,
                1, layoutCount, 0, 0),
        ], strings, modules, 1, 0, layoutCount, 0, schema);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings,
            modules, 1, 0, layoutCount, 0, schema);
        var typeSpecs = TypeSpecs(types.Count, 0, schema);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 5, 1, 0),
        ], strings, types, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 1, strings,
            types, typeSpecs);
        var targetProfileBytes = blittableLayout
            ? new byte[] { 1, 0, 0, 0, 1, 0, 0, 0 }
            : [];
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, blittableLayout ? 1 : 0, schema);
        TrRmLayoutRow[] layoutRows = blittableLayout
            ? [
                new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
                new TrRmLayoutRow(1, 1, 0, 1, 0, 0, 0, 0),
            ]
            : [new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0)];
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows,
            types, targetProfiles, 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes,
            layoutRows.Length, types, targetProfiles, 0);
        var transparentDomain = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(transparentDomain, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            transparentDomain.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            transparentDomain.AsSpan(8), carrierField);

        TrRmStructuralMetadataValidator.ValidateGenericConstantCarrierFeatureAssignments(
            [], 0, transparentDomain, 1, types, fields, typeSpecs,
            layouts, features, modules, schema);
        return (
            TrRmFeatureManifestValidator.Requires(features, modules, 1,
                TrRmFeatureTableReader.ConstantGenericTermsFeatureId, schema),
            TrRmFeatureManifestValidator.Requires(features, modules, 1,
                TrRmFeatureTableReader.NativeBoundaryContractsFeatureId, schema));
    }

    private static byte[] Strings()
        => TrRmStringTableWriter.EncodeTextRows([
            ("Module", 0), ("", 0), ("Enum", 0), ("Wrapper", 0),
            ("carrier", 0),
        ]);

    private static byte[] Module(TrRmStringTableReader strings,
        int featureCount)
        => TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1,
                featureCount == 0 ? 0 : 1, featureCount),
        ], strings, 0, 1, 0, 0, featureCount);

    private static TrRmTypeSpecTableReader TypeSpecs(int typeDefCount,
        int genericParamCount, TrRmArtifactSchema schema)
    {
        var bytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], typeDefCount, 0, genericParamCount, 0, schema);
        return new TrRmTypeSpecTableReader(bytes, 1, typeDefCount,
            0, genericParamCount, 0, schema);
    }
}
