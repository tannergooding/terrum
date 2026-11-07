// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmImmutableDataTableTests
{
    private const TrRmArtifactSchema s_schema
        = TrRmArtifactSchema.GeneralPredicateGraphsDraft;

    [Test]
    public void ReadsClosedDefinitionFamiliesAndCompleteOwners()
    {
        var typeSpecs = default(TrRmTypeSpecTableReader);
        var bytes = TrRmImmutableDataTableWriter.Encode([
            new TrRmImmutableDataRow(1, 0x2200_0001, 0),
            new TrRmImmutableDataRow(1, 0x2300_0001, 0xF200_0008),
            new TrRmImmutableDataRow(2, 0x2400_0001, 0),
        ], interfaceConstDefCount: 1, witnessAssociatedConstCount: 1,
            staticFieldCount: 1, typeSpecs, s_schema);
        var reader = new TrRmImmutableDataTableReader(bytes, 3,
            interfaceConstDefCount: 1, witnessAssociatedConstCount: 1,
            staticFieldCount: 1, typeSpecs, s_schema);

        Assert.That(reader.GetKind(1), Is.EqualTo((byte)1));
        Assert.That(reader.GetDeclarationDefId(1), Is.EqualTo(0x2200_0001u));
        Assert.That(reader.GetOwnerTypeSpecRef(2), Is.EqualTo(0xF200_0008u));
        Assert.That(reader.GetDeclarationDefId(3), Is.EqualTo(0x2400_0001u));
    }

    [Test]
    public void RejectsWrongKindsBoundsReservedBitsAndUnselectedProfiles()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeRow(1, 0x2300_0001, 0, 0, 0, 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeRow(1, 0x2200_0000, 0, 1, 0, 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeRow(2, 0x2400_0001, 0, 0, 0, 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeRow(1, 0x2200_0001, 0, 1, 0, 0, flags: 1));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeRow(1, 0x2200_0001, 0, 1, 0, 0, reserved: 1));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmImmutableDataTableWriter.Encode([
                new TrRmImmutableDataRow(1, 0x2200_0001, 0),
            ], 1, 0, 0, default, TrRmArtifactSchema.Standard));

        static byte[] EncodeRow(byte kind, uint definition, uint owner,
            int interfaceConstCount, int witnessConstCount, int staticCount,
            byte flags = 0, ushort reserved = 0)
        {
            var bytes = new byte[12];
            bytes[0] = kind;
            bytes[1] = flags;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), reserved);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), definition);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), owner);
            return new TrRmImmutableDataTableReader(bytes, 1,
                interfaceConstCount, witnessConstCount, staticCount,
                default, s_schema).Count == 1
                ? bytes
                : throw new InvalidDataException();
        }
    }

    [Test]
    public void RequiresFeature19InTheModuleReadingImmutableData()
    {
        var supportedArtifact = CreateImmutableDataPredicateArtifact(
            includeFeature19: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(supportedArtifact));
        Assert.That(tables.ImmutableData.Count, Is.EqualTo(1));

        var missingFeatureArtifact = CreateImmutableDataPredicateArtifact(
            includeFeature19: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(missingFeatureArtifact)));

        var supportedBoundOnly = CreateImmutableDataPredicateArtifact(
            includeFeature19: true, allowedBoundOnly: true);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(supportedBoundOnly)));

        var missingBoundFeature = CreateImmutableDataPredicateArtifact(
            includeFeature19: false, allowedBoundOnly: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(missingBoundFeature)));

        var supportedWitnessBinding = CreateImmutableDataPredicateArtifact(
            includeFeature19: true, useWitnessIdentity: true);
        var witnessTables = TrRmStructuralMetadataValidator
            .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(supportedWitnessBinding));
        Assert.That(witnessTables.WitnessAssociatedConsts.Count, Is.EqualTo(1));

        var missingWitnessFeature = CreateImmutableDataPredicateArtifact(
            includeFeature19: false, useWitnessIdentity: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(missingWitnessFeature)));

        var unexpectedOwner = CreateImmutableDataPredicateArtifact(
            includeFeature19: true, ownerTypeSpecRef: 0xF200_0008);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(unexpectedOwner)));
    }

    private static byte[] CreateImmutableDataPredicateArtifact(
        bool includeFeature19, bool allowedBoundOnly = false,
        bool useWitnessIdentity = false, uint ownerTypeSpecRef = 0)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("I", 0), ("Predicate", 0), ("Answer", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 5);
        var featureRows = includeFeature19
            ? new[] {
                TrRmFeatureRow.GeneralPredicateGraphsCandidate,
                TrRmFeatureRow.ImmutableDataIdentitiesCandidate,
                TrRmFeatureRow.TypedFrozenValues,
            }
            : [
                TrRmFeatureRow.GeneralPredicateGraphsCandidate,
                TrRmFeatureRow.TypedFrozenValues,
            ];
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, s_schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1, featureRows.Length),
        ], strings, moduleRefCount: 0, typeDefCount: 1, methodDefCount: 1,
            sourceFileCount: 0, featureCount: featureRows.Length);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 1, 1, 0, featureRows.Length);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 14, 0, 0, 0, 0, 1, 1, 0, 0),
        ], strings, modules, fieldCount: 0, genericParamCount: 0,
            layoutCount: 1, contractCount: 1);
        var typeDefs = new TrRmTypeDefTableReader(
            typeDefBytes, 1, strings, modules, 0, 0, 1, 1);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], typeDefs, targetProfiles, layoutFieldCount: 0);
        var typeSpecBytes = useWitnessIdentity
            ? TrRmTypeSpecTableWriter.Encode([
                TrRmTypeSpecTableWriter.Builtin(8),
                TrRmTypeSpecTableWriter.Reference(0x02, 1),
            ], typeDefCount: 1, importCount: 0, genericParamCount: 0)
            : [];
        var typeSpecs = useWitnessIdentity
            ? new TrRmTypeSpecTableReader(typeSpecBytes, 2, 1, 0, 0, 0)
            : default;
        var genericParams = default(TrRmGenericParamTableReader);
        var interfaceDefBytes = TrRmInterfaceDefTableWriter.Encode([
            new TrRmInterfaceDefRow(1, 0, 0, 0, 0, 0, 0, 0,
                useWitnessIdentity ? 1 : 0, useWitnessIdentity ? 1 : 0, 1, 1),
        ], typeDefs, genericParams, interfaceBaseCount: 0,
            interfaceMethodCount: 0, associatedTypeCount: 0,
            associatedConstCount: useWitnessIdentity ? 1 : 0,
            interfaceConstCount: 1);
        var interfaces = new TrRmInterfaceDefTableReader(interfaceDefBytes,
            1, typeDefs, genericParams, 0, 0, 0,
            useWitnessIdentity ? 1 : 0, 1);
        var associatedConstDefs = useWitnessIdentity
            ? new TrRmAssociatedConstDefTableReader(
                TrRmAssociatedConstDefTableWriter.Encode([
                    new TrRmAssociatedConstDefRow(1, 5, 0xF200_0008),
                ], strings, typeSpecs, interfaces),
                1, strings, typeSpecs, interfaces)
            : default;
        var interfaceWitnessesBytes = useWitnessIdentity
            ? TrRmInterfaceWitnessTableWriter.Encode([
                new TrRmInterfaceWitnessRow(2, 2, 0, 0, 0, 0, 1, 1),
            ], typeSpecs, memberCount: 0, associatedTypeCount: 0,
                associatedConstCount: 1)
            : [];
        var interfaceWitnesses = useWitnessIdentity
            ? new TrRmInterfaceWitnessTableReader(
                interfaceWitnessesBytes, 1, typeSpecs, 0, 0, 1)
            : default;
        var interfaceBases = useWitnessIdentity
            ? new TrRmInterfaceBaseTableReader([], 0, typeSpecs, interfaces)
            : default;
        var blobBytes = TrRmVariableTableWriter.EncodeBlobs([
            new ReadOnlyMemory<byte>([42, 0, 0, 0]),
        ]);
        var blobs = new TrRmBlobTableReader(blobBytes, 1);
        var frozenValueNodeBytes = TrRmFrozenValueNodeTableWriter.Encode([
            new TrRmFrozenValueNodeRow(0xF200_0008, 1, 1, 0, 0, 0),
        ], typeSpecs, blobs, childCount: 0, unionCaseDefCount: 0);
        var constantSourceBytes = TrRmConstantSourceTableWriter.Encode([
            new TrRmConstantSourceRow(1, 1),
        ], frozenValueNodeCount: 1, constantExecutionCount: 0,
            targetValueBindingCount: 0);
        var associatedConstDefBytes = useWitnessIdentity
            ? TrRmAssociatedConstDefTableWriter.Encode([
                new TrRmAssociatedConstDefRow(1, 5, 0xF200_0008),
            ], strings, typeSpecs, interfaces)
            : [];
        var interfaceConstDefBytes = TrRmInterfaceConstDefTableWriter.Encode([
            new TrRmInterfaceConstDefRow(1, 5, 0xF200_0008, 1),
        ], strings, typeSpecs, interfaces, constantSourceCount: 1);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0001, 0, 0, 0, 0, 0),
        ], typeSpecs, paramCount: 0, genericParamCount: 0);
        var signatures = new TrRmMethodSigTableReader(
            signatureBytes, 1, typeSpecs, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(1, 4, 1, 0, 1, 1, 0),
        ], strings, modules, typeDefs, signatures, contractCount: 1, bodyCount: 0);
        var methods = new TrRmMethodDefTableReader(
            methodBytes, 1, strings, modules, typeDefs, signatures, 1, 0);
        var contractPayload = new byte[allowedBoundOnly ? 4 : 12];
        if (allowedBoundOnly)
        {
            contractPayload[0] = 1;
        }
        else
        {
            contractPayload[0] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(contractPayload.AsSpan(8), 1);
        }
        var contracts = TrRmContractTableWriter.EncodeGeneralPredicateGraphsDraft([
            new TrRmContractRow(0x1600_0001,
                allowedBoundOnly ? (ushort)0x0A : (ushort)0x0B, contractPayload),
        ], typeDefs, methods, signatures, default, typeSpecs,
            fieldPathCount: 0, features: new TrRmFeatureTableReader(
                featureBytes, featureRows.Length, s_schema), modules,
            predicateGraphCount: allowedBoundOnly ? 0 : 1);
        var graphBytes = allowedBoundOnly
            ? []
            : TrRmPredicateGraphTableWriter.Encode([
                new TrRmPredicateGraphRow(1, 1),
            ], nodeCount: 1, readFootprintCount: 1);
        var footprintBytes = TrRmPredicateReadFootprintTableWriter.Encode([
            new TrRmPredicateReadFootprintRow(1, 1, 0, 0),
        ], rootCount: 1, dependencyCount: 0);
        var readRootBytes = TrRmPredicateReadRootTableWriter.Encode([
            new TrRmPredicateReadRootRow(6, 1),
        ], immutableDataCount: 1, captureCount: 0, s_schema);
        var immutableDataBytes = TrRmImmutableDataTableWriter.Encode([
            useWitnessIdentity
                ? new TrRmImmutableDataRow(1, 0x2300_0001, ownerTypeSpecRef)
                : new TrRmImmutableDataRow(1, 0x2200_0001, ownerTypeSpecRef),
        ], interfaceConstDefCount: 1,
            witnessAssociatedConstCount: useWitnessIdentity ? 1 : 0,
            staticFieldCount: 0, typeSpecs, s_schema);
        var operandCounts = new TrRmPredicateTableRowCounts(
            1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var operands = allowedBoundOnly
            ? TrRmPredicateNodeOperandTableWriter.Encode(
                Array.Empty<TrRmPredicateNodeOperandRow>(), operandCounts, s_schema)
            : TrRmPredicateNodeOperandTableWriter.Encode([
                new TrRmPredicateNodeOperandRow(1, 1),
            ], operandCounts, s_schema);
        var nodes = allowedBoundOnly
            ? []
            : TrRmPredicateNodeTableWriter.Encode([
                new TrRmPredicateNodeRow(0x11, 0xF200_0001, 1, 1),
            ], operandCount: 1, typeSpecs, s_schema);
        var evaluationReadBounds = allowedBoundOnly
            ? TrRmEvaluationReadBoundTableWriter.Encode([
                new TrRmEvaluationReadBoundRow(1, 1),
            ], methodDefCount: 1, readFootprintCount: 1, s_schema)
            : [];
        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, modulesBytes),
            new(0x0002, 0, 5, stringBytes),
            new(0x0003, 0, 1, blobBytes),
            new(0x0004, 0, (uint)featureRows.Length, featureBytes),
            new(0x0013, 0, 1, typeDefBytes),
            new(0x0015, 0, 1, layoutBytes),
            new(0x0016, 0, 1, methodBytes),
            new(0x0017, 0, 1, signatureBytes),
            new(0x001B, 0, 1, contracts),
            new(0x0025, 0, 1, interfaceDefBytes),
            new(0x0030, 0, 1, constantSourceBytes),
            new(0x0031, 0, 1, interfaceConstDefBytes),
            new(0x0038, 0, 1, footprintBytes),
            new(0x0039, 0, 1, readRootBytes),
            new(0x003F, 0, 1, frozenValueNodeBytes),
            new(0x0041, 0, 1, immutableDataBytes),
        };
        if (useWitnessIdentity)
        {
            var candidateImports = default(TrRmImportTableReader);
            var constantSources = new TrRmConstantSourceTableReader(
                constantSourceBytes, 1, 1, 0, 0);
            var witnessAssociatedConstBytes =
                TrRmWitnessAssociatedConstTableWriter.Encode([
                    new TrRmWitnessAssociatedConstRow(
                        1, 2, 0x2500_0001, 1),
                ], interfaceWitnesses, typeSpecs, typeDefs, interfaces,
                    interfaceBases, associatedConstDefs, candidateImports,
                    constantSources, s_schema);
            sections.Add(new TrRmSection(0x0011, 0, 2, typeSpecBytes));
            sections.Add(new TrRmSection(0x0028, 0, 1, interfaceWitnessesBytes));
            sections.Add(new TrRmSection(0x0032, 0, 1, associatedConstDefBytes));
            sections.Add(new TrRmSection(0x0033, 0, 1, witnessAssociatedConstBytes));
        }
        if (!allowedBoundOnly)
        {
            sections.Add(new TrRmSection(0x0037, 0, 1, graphBytes));
            sections.Add(new TrRmSection(0x003B, 0, 1, nodes));
            sections.Add(new TrRmSection(0x003C, 0, 1, operands));
        }
        else
        {
            sections.Add(new TrRmSection(0x0051, 0, 1, evaluationReadBounds));
        }
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, s_schema);
    }
}
