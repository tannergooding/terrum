// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmForeignInterfaceParameterSurfaceTests
{
    [Test]
    public void ReadsRequiredLabelAndAbsentDefaultFromSixteenByteSurface()
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;
        var stringBytes = TrRmStringTableWriter.EncodeNames([
            ("Module", 0), ("Method", 0), ("value", 0), ("count", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 4);
        var featureBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.TypedFrozenValues,
            TrRmFeatureRow.MethodParameterLabelsCandidate,
        ], schema);
        var features = new TrRmFeatureTableReader(featureBytes, 2, schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1, 1, 2),
        ], strings, 0, 0, 1, 0, 2);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 1, 0, 2);
        var typeSpecRows = TrRmTypeSpecTableWriter.Encode([
            new ReadOnlyMemory<byte>(TrRmTypeSpecTableWriter.Builtin(0x08)),
        ], 0, 0, 0, 0, schema, methodDefCount: 1);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecRows, 1,
            0, 0, 0, 0, schema, 1);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0000, 0, 1, 2, 0, 0),
        ], typeSpecs, 2, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 2, 0);
        var parameterBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, 1, 0),
            new TrRmParamRow(1, 1, 1, 0),
        ], signatures, typeSpecs, schema);
        var parameters = new TrRmParamTableReader(parameterBytes, 2,
            signatures, typeSpecs, schema);
        var types = default(TrRmTypeDefTableReader);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 2, 1, 0, 0, 0, 0),
        ], strings, modules, types, signatures, 0, 0, schema);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1, strings,
            modules, types, signatures, 0, 0, schema);
        var constantSourceBytes = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(constantSourceBytes, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            constantSourceBytes.AsSpan(4), 1);
        var constantSources = new TrRmConstantSourceTableReader(
            constantSourceBytes, 1, 1, 0, 0, 0, 0, schema);
        var blobBytes = TrRmVariableTableWriter.EncodeBlobs([
            new ReadOnlyMemory<byte>([42, 0, 0, 0]),
        ]);
        var blobs = new TrRmBlobTableReader(blobBytes, 1);
        var frozenValueNodeBytes = TrRmFrozenValueNodeTableWriter.Encode([
            new TrRmFrozenValueNodeRow(1, 1, 1, 0, 0, 0),
        ], typeSpecs, blobs, 0, 0);
        var frozenValueNodes = new TrRmFrozenValueNodeTableReader(
            frozenValueNodeBytes, 1, typeSpecs, 1, 0, 0);
        var surfaceBytes
            = TrRmForeignInterfaceParameterSurfaceTableWriter.Encode([
                new TrRmForeignInterfaceParameterSurfaceRow(1, 1, 4, 0),
                new TrRmForeignInterfaceParameterSurfaceRow(1, 0, 3, 1),
            ], strings, methods, signatures, parameters, typeSpecs,
                constantSources, frozenValueNodes, blobs, features, modules,
                schema);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            surfaceBytes.AsSpan(4)), Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            surfaceBytes.AsSpan(20)), Is.EqualTo(1u));

        var surfaces = new TrRmMethodParameterLabelTableReader(surfaceBytes,
            2, strings, methods, signatures, parameters, typeSpecs,
            constantSources, frozenValueNodes, blobs, features, modules,
            schema);

        Assert.That(surfaces.Count, Is.EqualTo(2));
        Assert.That(surfaces.TryGetNameStringRow(1, 0, out var nameRow),
            Is.True);
        Assert.That(nameRow, Is.EqualTo(3));
        Assert.That(surfaces.TryGetDefaultConstantSourceRow(1, 0,
            out var defaultValueRow), Is.True);
        Assert.That(defaultValueRow, Is.EqualTo(1));
        Assert.That(surfaces.TryGetDefaultConstantSourceRow(1, 1,
            out defaultValueRow), Is.True);
        Assert.That(defaultValueRow, Is.Zero);
        Assert.That(frozenValueNodes.GetScalarPayload(1, blobs).ToArray(),
            Is.EqualTo(new byte[] { 42, 0, 0, 0 }));

        var noDefaultFeatureBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.MethodParameterLabelsCandidate,
        ], schema);
        var noDefaultFeatures = new TrRmFeatureTableReader(
            noDefaultFeatureBytes, 1, schema);
        var noDefaultModuleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1, 1, 1),
        ], strings, 0, 0, 1, 0, 1);
        var noDefaultModules = new TrRmModuleTableReader(
            noDefaultModuleBytes, 1, strings, 0, 0, 1, 0, 1);
        var noDefaultMethods = new TrRmMethodDefTableReader(methodBytes, 1,
            strings, noDefaultModules, types, signatures, 0, 0, schema);
        var noDefaultSurfaceBytes
            = TrRmForeignInterfaceParameterSurfaceTableWriter.Encode([
                new TrRmForeignInterfaceParameterSurfaceRow(1, 0, 3, 0),
                new TrRmForeignInterfaceParameterSurfaceRow(1, 1, 4, 0),
            ], strings, noDefaultMethods, signatures, parameters, typeSpecs,
                constantSources, frozenValueNodes, blobs, noDefaultFeatures,
                noDefaultModules, schema);
        var noDefaultSurfaces = new TrRmMethodParameterLabelTableReader(
            noDefaultSurfaceBytes, 2, strings, noDefaultMethods, signatures,
            parameters, typeSpecs, constantSources, frozenValueNodes, blobs,
            noDefaultFeatures, noDefaultModules, schema);
        Assert.That(noDefaultSurfaces.TryGetDefaultConstantSourceRow(1, 0,
            out defaultValueRow), Is.True);
        Assert.That(defaultValueRow, Is.Zero);
    }
}
