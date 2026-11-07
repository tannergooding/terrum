// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmPublishedPredicateSelectorTests
{
    private const TrRmArtifactSchema s_schema
        = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;

    [Test]
    public void PublishedSelectorAndItsSelectedTablesChargeAtExactWorkBoundary()
    {
        var fixture = CreateFixture();

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
            Validate(fixture, ledger));
    }

    [Test]
    public void NativeOutputCellRequiresSuccessAndComplementSelectors()
    {
        var fixture = CreateNativeOutputFixture(includeComplement: true);
        Assert.That(ValidateNativeOutput(fixture),
            Is.EqualTo(new byte[] { 0, 3, 4 }));

        var incompleteFixture = CreateNativeOutputFixture(
            includeComplement: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateNativeOutput(incompleteFixture));
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateNativeOutputFixture(includeComplement: true,
                includeOutputFeature: false));
    }

    private static void Validate(Fixture fixture,
        TrRmA3LayoutResourceLedger resourceLedger)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 3,
            resourceLedger);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 0, 1, 0, 1, resourceLedger);
        var features = new TrRmFeatureTableReader(fixture.Features, 1, s_schema,
            resourceLedger);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs, 1,
            0, 0, 0, 0, s_schema, 1, 0, resourceLedger);
        var types = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 1, s_schema, resourceLedger);
        var signatures = new TrRmMethodSigTableReader(fixture.Signatures, 1,
            typeSpecs, 0, 0, resourceLedger);
        var methods = new TrRmMethodDefTableReader(fixture.Methods, 1,
            strings, modules, types, signatures, 1, 0, resourceLedger);
        var parameters = default(TrRmParamTableReader);
        var contracts = new TrRmContractTableReader(fixture.Contracts, 1,
            types, methods, signatures, parameters, typeSpecs,
            fieldPathCount: 0, features: features, modules: modules,
            schema: s_schema,
            predicateGraphCount: 1, resourceLedger: resourceLedger);
        var graphs = new TrRmPredicateGraphTableReader(fixture.Graphs, 1,
            1, 1, resourceLedger);
        var footprints = new TrRmPredicateReadFootprintTableReader(
            fixture.Footprints, 1, 1, 0, resourceLedger);
        var childEdges = new TrRmPredicateChildEdgeTableReader(
            [], 0, 1, graphs, resourceLedger);
        var captures = new TrRmPredicateCaptureTableReader(
            [], 0, 1, typeSpecs, resourceLedger);
        _ = new TrRmPredicateReadRootTableReader(
            fixture.ReadRoots, 1, 0, 0, s_schema, resourceLedger);
        var readDependencies = new TrRmPredicateReadDependencyTableReader(
            [], 0, 1, 0, 0, 0, resourceLedger);
        _ = new TrRmPredicateNodeTableReader(fixture.Nodes, 1, 1,
            typeSpecs, s_schema, resourceLedger);
        var operandCounts = new TrRmPredicateTableRowCounts(
            1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        _ = new TrRmPredicateNodeOperandTableReader(
            fixture.Operands, 1, operandCounts, s_schema, resourceLedger);

        var selectorKinds = TrRmPublishedPredicateSelectorValidator.Validate(
            fixture.ReadRoots, 1, fixture.Nodes, 1,
            fixture.Operands, 1, readDependencies.Count, graphs, footprints,
            childEdges, captures, methods, signatures, typeSpecs, contracts,
            resourceLedger);

        Assert.That(selectorKinds, Is.EqualTo(new byte[] { 0, 1 }));
    }

    private static Fixture CreateFixture()
    {
        const TrRmArtifactSchema schema = s_schema;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Run", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
        var featureBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.PublishedPredicates,
        ], schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, 0, 1, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 0, 1, 0, 1);
        var features = new TrRmFeatureTableReader(featureBytes, 1, schema);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(1),
        ], 0, 0, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1,
            0, 0, 0, 0, schema, 1);
        var types = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 1, schema);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 0, 0);
        var methodsBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 0, 1, 1, 0),
        ], strings, modules, types, signatures, 1, 0);
        var methods = new TrRmMethodDefTableReader(methodsBytes, 1,
            strings, modules, types, signatures, 1, 0);
        var predicatePayload = new byte[12];
        predicatePayload[0] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(
            predicatePayload.AsSpan(8), 1);
        var contractBytes = TrRmContractTableWriter
            .EncodeForeignInvocationDraft([
                new TrRmContractRow(0x1600_0001, 0x0B,
                    predicatePayload),
            ], types, methods, signatures, default, typeSpecs,
                features: features, modules: modules,
                predicateGraphCount: 1, schema: schema);
        var graphBytes = TrRmPredicateGraphTableWriter.Encode([
            new TrRmPredicateGraphRow(1, 1),
        ], 1, 1);
        var footprintBytes = TrRmPredicateReadFootprintTableWriter.Encode([
            new TrRmPredicateReadFootprintRow(1, 1, 0, 0),
        ], 1, 0);
        var readRootBytes = TrRmPredicateReadRootTableWriter.Encode([
            new TrRmPredicateReadRootRow(5, 0),
        ], 0, 0, schema);
        var nodeBytes = TrRmPredicateNodeTableWriter.Encode([
            new TrRmPredicateNodeRow(1, 0xF200_0001, 1, 1),
        ], 1, typeSpecs, schema);
        var operandCounts = new TrRmPredicateTableRowCounts(
            1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var operandBytes = TrRmPredicateNodeOperandTableWriter.Encode([
            new TrRmPredicateNodeOperandRow(1, 1),
        ], operandCounts, schema);

        return new Fixture(stringBytes, modulesBytes, featureBytes,
            typeSpecBytes, signatureBytes, methodsBytes, contractBytes,
            graphBytes, footprintBytes, readRootBytes, nodeBytes, operandBytes);
    }

    private static byte[] ValidateNativeOutput(NativeOutputFixture fixture)
    {
        var strings = new TrRmStringTableReader(fixture.Strings, 3);
        var modules = new TrRmModuleTableReader(fixture.Modules, 1, strings,
            0, 0, 1, 0, fixture.FeatureCount);
        var features = new TrRmFeatureTableReader(fixture.Features,
            fixture.FeatureCount,
            s_schema);
        var typeSpecs = new TrRmTypeSpecTableReader(fixture.TypeSpecs, 2,
            0, 0, 0, 0, s_schema, 1, 0);
        var types = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, fixture.ContractCount, s_schema);
        var signatures = new TrRmMethodSigTableReader(fixture.Signatures, 1,
            typeSpecs, 1, 0);
        var methods = new TrRmMethodDefTableReader(fixture.Methods, 1,
            strings, modules, types, signatures, fixture.ContractCount, 0);
        var parameters = new TrRmParamTableReader(fixture.Parameters, 1,
            signatures, typeSpecs, s_schema);
        var contracts = new TrRmContractTableReader(fixture.Contracts,
            fixture.ContractCount, types, methods, signatures, parameters,
            typeSpecs, fieldPathCount: 0, features: features, modules: modules,
            schema: s_schema, predicateGraphCount: 2);
        var graphs = new TrRmPredicateGraphTableReader(fixture.Graphs, 2, 2, 2);
        var footprints = new TrRmPredicateReadFootprintTableReader(
            fixture.Footprints, 2, 2, 0);
        var childEdges = new TrRmPredicateChildEdgeTableReader([], 0, 2,
            graphs);
        var captures = new TrRmPredicateCaptureTableReader([], 0, 2,
            typeSpecs);
        _ = new TrRmPredicateReadRootTableReader(fixture.ReadRoots, 2,
            0, 0, s_schema);
        var readDependencies = new TrRmPredicateReadDependencyTableReader(
            [], 0, 1, 0, 0, 0);
        _ = new TrRmPredicateNodeTableReader(fixture.Nodes, 2, 2,
            typeSpecs, s_schema);
        var operandCounts = new TrRmPredicateTableRowCounts(
            2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        _ = new TrRmPredicateNodeOperandTableReader(fixture.Operands, 2,
            operandCounts, s_schema);

        return TrRmPublishedPredicateSelectorValidator.Validate(
            fixture.ReadRoots, 2, fixture.Nodes, 2, fixture.Operands, 2,
            readDependencies.Count, graphs, footprints, childEdges, captures,
            methods, signatures, typeSpecs, contracts);
    }

    private static NativeOutputFixture CreateNativeOutputFixture(
        bool includeComplement, bool includeOutputFeature = true)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Run", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
        TrRmFeatureRow[] featureRows = includeOutputFeature
            ? [
                TrRmFeatureRow.NativeOutputCellsCandidate,
                TrRmFeatureRow.PublishedPredicates,
            ]
            : [TrRmFeatureRow.PublishedPredicates];
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, s_schema);
        var featureCount = featureRows.Length;
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: featureCount),
        ], strings, 0, 0, 1, 0, featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 1, 0, featureCount);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount,
            s_schema);
        var contractCount = includeComplement ? 4 : 3;
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.InlineFamily(0x8C, 8, 1),
        ], 0, 0, 0, 0, s_schema, methodDefCount: 1);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 2,
            0, 0, 0, 0, s_schema, 1);
        var types = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, contractCount, s_schema);
        var signaturesBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 1, 1, 0, 0),
        ], typeSpecs, 1, 0);
        var signatures = new TrRmMethodSigTableReader(signaturesBytes, 1,
            typeSpecs, 1, 0);
        var parametersBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, 2, 0),
        ], signatures, typeSpecs, s_schema);
        var parameters = new TrRmParamTableReader(parametersBytes, 1,
            signatures, typeSpecs, s_schema);
        var contractRows = new List<TrRmContractRow>
        {
            NativeOutputState(0, 1, 1),
            PredicateGraphContract(0, 1),
            PredicateGraphContract(1, 2),
        };
        if (includeComplement)
        {
            contractRows.Insert(1, NativeOutputState(0, 2, 0));
        }

        var methodsBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 0, 1, contractRows.Count, 0),
        ], strings, modules, types, signatures, contractRows.Count, 0);
        var methods = new TrRmMethodDefTableReader(methodsBytes, 1,
            strings, modules, types, signatures, contractRows.Count, 0);
        var contractBytes = TrRmContractTableWriter
            .EncodeForeignInvocationDraft(contractRows, types, methods,
                signatures, parameters, typeSpecs, features, modules,
                predicateGraphCount: 2, schema: s_schema);
        var graphBytes = TrRmPredicateGraphTableWriter.Encode([
            new TrRmPredicateGraphRow(1, 1),
            new TrRmPredicateGraphRow(2, 2),
        ], 2, 2);
        var footprintBytes = TrRmPredicateReadFootprintTableWriter.Encode([
            new TrRmPredicateReadFootprintRow(1, 1, 0, 0),
            new TrRmPredicateReadFootprintRow(2, 1, 0, 0),
        ], 2, 0);
        var readRootBytes = TrRmPredicateReadRootTableWriter.Encode([
            new TrRmPredicateReadRootRow(5, 0),
            new TrRmPredicateReadRootRow(5, 0),
        ], 0, 0, s_schema);
        var nodeBytes = TrRmPredicateNodeTableWriter.Encode([
            new TrRmPredicateNodeRow(3, 0xF200_0001, 1, 1),
            new TrRmPredicateNodeRow(4, 0xF200_0001, 2, 1),
        ], 2, typeSpecs, s_schema);
        var operandCounts = new TrRmPredicateTableRowCounts(
            2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var operandBytes = TrRmPredicateNodeOperandTableWriter.Encode([
            new TrRmPredicateNodeOperandRow(1, 1),
            new TrRmPredicateNodeOperandRow(1, 2),
        ], operandCounts, s_schema);

        return new NativeOutputFixture(stringBytes, moduleBytes, featureBytes,
            typeSpecBytes, signaturesBytes, parametersBytes, methodsBytes,
            contractBytes, graphBytes, footprintBytes, readRootBytes,
            nodeBytes, operandBytes, contractRows.Count, featureCount);
    }

    private static TrRmContractRow NativeOutputState(uint ordinal,
        uint predicateGraph, byte state)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, ordinal);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
            predicateGraph);
        payload[8] = state;
        return new TrRmContractRow(0x1600_0001, 2, payload);
    }

    private static TrRmContractRow PredicateGraphContract(uint ordinal,
        uint predicateGraph)
    {
        var payload = new byte[12];
        payload[0] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), ordinal);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8),
            predicateGraph);
        return new TrRmContractRow(0x1600_0001, 0x0B, payload);
    }

    private sealed record Fixture(byte[] Strings, byte[] Modules,
        byte[] Features, byte[] TypeSpecs, byte[] Signatures,
        byte[] Methods, byte[] Contracts, byte[] Graphs, byte[] Footprints,
        byte[] ReadRoots, byte[] Nodes, byte[] Operands);

    private sealed record NativeOutputFixture(byte[] Strings, byte[] Modules,
        byte[] Features, byte[] TypeSpecs, byte[] Signatures,
        byte[] Parameters, byte[] Methods, byte[] Contracts,
        byte[] Graphs, byte[] Footprints, byte[] ReadRoots, byte[] Nodes,
        byte[] Operands, int ContractCount, int FeatureCount);
}
