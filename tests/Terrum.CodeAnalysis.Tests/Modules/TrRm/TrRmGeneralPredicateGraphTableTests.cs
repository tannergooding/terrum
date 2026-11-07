// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmGeneralPredicateGraphTableTests
{
    private const TrRmArtifactSchema s_schema
        = TrRmArtifactSchema.GeneralPredicateGraphsDraft;

    [Test]
    public void CandidateSectionsAreProfileSelectedAndKeepTheirAssignedShapes()
    {
        var featureBytes = TrRmFeatureTableWriter.Encode(
            [TrRmFeatureRow.GeneralPredicateGraphsCandidate], s_schema);
        var moduleBytes = new byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(moduleBytes.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(moduleBytes.AsSpan(28), 1);
        var strings = TrRmStringTableWriter.EncodeTextRows([("module", 0)]);
        var call = new byte[24];
        call[0] = 1;
        call[1] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(call.AsSpan(4), 1);
        var typePattern = new byte[12];
        typePattern[0] = 2;
        var sections = new TrRmSection[] {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 1, strings),
            new(0x0004, 0, 1, featureBytes),
            new(0x004D, 0, 1, new byte[4]),
            new(0x004E, 0, 1, TrRmVariableTableWriter.EncodeBlobs([call])),
            new(0x004F, 0, 1, new byte[4]),
            new(0x0050, 0, 1, new byte[8]),
            new(0x0051, 0, 1, new byte[12]),
            new(0x0052, 0, 1, TrRmVariableTableWriter.EncodeBlobs([typePattern])),
            new(0x0055, 0, 1, new byte[20]),
            new(0x0056, 0, 1, new byte[20]),
            new(0x0057, 0, 1, new byte[20]),
            new(0x0058, 0, 1, new byte[16]),
            new(0x0059, 0, 1, new byte[8]),
            new(0x005A, 0, 1, new byte[12]),
            new(0x005B, 0, 1, new byte[16]),
            new(0x005C, 0, 1, new byte[24]),
            new(0x005D, 0, 1, new byte[16]),
            new(0x005E, 0, 1, new byte[8]),
        };
        var artifact = TrRmArtifactWriter.Encode(sections, s_schema);
        var index = TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(artifact);
        foreach (var (kind, length) in new (ushort Kind, int Length)[] {
            (0x004D, 4), (0x004E, 32), (0x004F, 4), (0x0050, 8),
            (0x0051, 12), (0x0052, 20), (0x0055, 20), (0x0056, 20),
            (0x0057, 20), (0x0058, 16), (0x0059, 8), (0x005A, 12),
            (0x005B, 16), (0x005C, 24), (0x005D, 16), (0x005E, 8),
        })
        {
            Assert.That(index.TryGetSection(kind, out var section, out var count), Is.True);
            Assert.That(section.Length, Is.EqualTo(length));
            Assert.That(count, Is.EqualTo(1));
        }
        _ = Assert.Throws<InvalidDataException>(() => new TrRmArtifactIndex(artifact));
    }

    [Test]
    public void ReadsMixedConstantExecutionSubstitutionSlots()
    {
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], typeDefCount: 0, importCount: 0, genericParamCount: 0);
        var typeSpecs = new TrRmTypeSpecTableReader(
            typeSpecBytes, 1, typeDefCount: 0, importCount: 0,
            genericParamCount: 0);
        var bytes = TrRmConstantExecutionSubstitutionTableWriter.Encode([
            new TrRmConstantExecutionSubstitutionRow(1, 0xF200_0008),
            new TrRmConstantExecutionSubstitutionRow(2, 1),
        ], constantSourceCount: 1, typeSpecs, s_schema);
        var reader = new TrRmConstantExecutionSubstitutionTableReader(
            bytes, recordCount: 2, constantSourceCount: 1, typeSpecs, s_schema);

        Assert.That(reader.GetKind(1), Is.EqualTo((byte)1));
        Assert.That(reader.GetTypeSpecRef(1), Is.EqualTo(0xF200_0008));
        Assert.That(reader.GetKind(2), Is.EqualTo((byte)2));
        Assert.That(reader.GetReference(2), Is.EqualTo(1));
        var invalidTypeReferenceRejected = false;
        try
        {
            _ = TrRmConstantExecutionSubstitutionTableWriter.Encode([
                new TrRmConstantExecutionSubstitutionRow(1, 2),
            ], constantSourceCount: 1, typeSpecs, s_schema);
        }
        catch (InvalidDataException)
        {
            invalidTypeReferenceRejected = true;
        }
        Assert.That(invalidTypeReferenceRejected, Is.True);

        var invalidConstantReferenceRejected = false;
        try
        {
            _ = TrRmConstantExecutionSubstitutionTableWriter.Encode([
                new TrRmConstantExecutionSubstitutionRow(2, 2),
            ], constantSourceCount: 1, typeSpecs, s_schema);
        }
        catch (InvalidDataException)
        {
            invalidConstantReferenceRejected = true;
        }
        Assert.That(invalidConstantReferenceRejected, Is.True);
    }

    [Test]
    public void BindsCheckedBodySubstitutionsByOwnerThenMethodFormalKind()
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("method", 0), ("Type", 0),
            ("Method", 0), ("T", 0), ("N", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 7);
        var featureRows = new[]
        {
            TrRmFeatureRow.GeneralPredicateGraphsCandidate,
            TrRmFeatureRow.ConstantExecution,
        };
        Array.Sort(featureRows, static (left, right)
            => left.FeatureId.CompareTo(right.FeatureId));
        var featuresBytes = TrRmFeatureTableWriter.Encode(featureRows, s_schema);
        var features = new TrRmFeatureTableReader(
            featuresBytes, featureRows.Length, s_schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1, featureRows.Length),
        ], strings, moduleRefCount: 0, typeDefCount: 1, methodDefCount: 1,
            sourceFileCount: 0, featureCount: featureRows.Length);
        var modules = new TrRmModuleTableReader(
            modulesBytes, 1, strings, 0, 1, 1, 0, featureRows.Length);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 4, 4, 0, 0, 1, 1, 0, 0, 0, 0),
        ], strings, modules, fieldCount: 0, genericParamCount: 2,
            layoutCount: 0, contractCount: 1, s_schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 1,
            strings, modules, 0, 2, 0, 1, s_schema);
        var signaturesBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0008, 0, 0, 0, 2, 1),
        ], default, paramCount: 0, genericParamCount: 2);
        var signatures = new TrRmMethodSigTableReader(
            signaturesBytes, 1, default, paramCount: 0, genericParamCount: 2);
        var methodsBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(1, 5, 1, 0, 1, 1, 1),
        ], strings, modules, typeDefs, signatures, contractCount: 1,
            bodyCount: 1);
        var methods = new TrRmMethodDefTableReader(methodsBytes, 1, strings,
            modules, typeDefs, signatures, contractCount: 1, bodyCount: 1);
        var genericParamsBytes = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1300_0001, 0, 6, 0, 0,
                1, 1, 1, 0, 0),
            new TrRmGenericParamRow(0x1600_0001, 0, 7, 0, 0,
                2, 0, 0, 0xF200_0008, 0),
        ], strings, typeDefs, methods, signatures, default,
            constraintCount: 0, constantSourceCount: 1);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamsBytes, 2, strings, typeDefs, methods, signatures,
            default, constraintCount: 0, constantSourceCount: 1);
        var contractRows = new[]
        {
            new TrRmContractRow(0x1600_0001, 0x0A, new byte[] { 2, 0, 0, 0 }),
        };
        var contractBytes = TrRmContractTableWriter
            .EncodeGeneralPredicateGraphsDraft(contractRows, typeDefs,
                methods, signatures, default, default, fieldPathCount: 0,
                features, modules, predicateGraphCount: 0);
        var contracts = new TrRmContractTableReader(contractBytes, 1,
            typeDefs, methods, signatures, default, default,
            fieldPathCount: 0, features, modules, s_schema,
            predicateGraphCount: 0);
        var constantSourceBytes = TrRmConstantSourceTableWriter.Encode([
            new TrRmConstantSourceRow(1, 1),
        ], frozenValueNodeCount: 1, constantExecutionCount: 1,
            targetValueBindingCount: 0);
        var constantSources = new TrRmConstantSourceTableReader(
            constantSourceBytes, 1, frozenValueNodeCount: 1,
            constantExecutionCount: 1, targetValueBindingCount: 0);
        var substitutionsBytes =
            TrRmConstantExecutionSubstitutionTableWriter.Encode([
                new TrRmConstantExecutionSubstitutionRow(1, 0xF200_0008),
                new TrRmConstantExecutionSubstitutionRow(2, 1),
            ], constantSourceCount: 1, default, s_schema);
        var substitutions = new TrRmConstantExecutionSubstitutionTableReader(
            substitutionsBytes, 2, constantSourceCount: 1, default, s_schema);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);

        var executionBytes = TrRmConstantExecutionTableWriter.Encode([
            new TrRmConstantExecutionRow(1, 1, 0xF200_0008, 0,
                1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        ], bodyCount: 1, substitutions, constantSources, default, default,
            default, default, methods, typeDefs, signatures, genericParams,
            contracts, default, targetProfiles, default, default, features,
            modules, s_schema);

        Assert.That(executionBytes.Length, Is.EqualTo(80));

        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ]);
        var targetProfilesWithLayout = new TrRmTargetProfileTableReader(
            targetProfileBytes, 1);
        var targetTypeSpecs = default(TrRmTypeSpecTableReader);
        var targetLayoutBytes = TrRmTargetLayoutDependencyTableWriter.Encode([
            new TrRmTargetLayoutDependencyRow(1, 0, 0, 0xF200_0008, 1, 0, 0),
        ], targetTypeSpecs, targetProfilesWithLayout, fieldReferenceCount: 0);
        var targetLayoutDependencies = new TrRmTargetLayoutDependencyTableReader(
            targetLayoutBytes, 1, targetTypeSpecs, targetProfilesWithLayout,
            fieldReferenceCount: 0);
        var targetLayoutExecutionRows = new[]
        {
            new TrRmConstantExecutionRow(1, 1, 0xF200_0008, 1,
                1, 2, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0),
        };
        var missingLayoutQueriesRejected = false;
        try
        {
            _ = TrRmConstantExecutionTableWriter.Encode(targetLayoutExecutionRows,
                bodyCount: 1, substitutions, constantSources, default, default,
                default, targetLayoutDependencies, methods, typeDefs,
                signatures, genericParams, contracts, default,
                targetProfilesWithLayout, default, default, features, modules,
                s_schema);
        }
        catch (InvalidDataException)
        {
            missingLayoutQueriesRejected = true;
        }
        Assert.That(missingLayoutQueriesRejected, Is.True);

        var layoutFeatureRows = new[]
        {
            TrRmFeatureRow.GeneralPredicateGraphsCandidate,
            TrRmFeatureRow.ConstantExecution,
            TrRmFeatureRow.LayoutQueries,
        };
        Array.Sort(layoutFeatureRows, static (left, right)
            => left.FeatureId.CompareTo(right.FeatureId));
        var layoutFeaturesBytes = TrRmFeatureTableWriter.Encode(
            layoutFeatureRows, s_schema);
        var layoutFeatures = new TrRmFeatureTableReader(layoutFeaturesBytes,
            layoutFeatureRows.Length, s_schema);
        var layoutModulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1,
                layoutFeatureRows.Length),
        ], strings, moduleRefCount: 0, typeDefCount: 1, methodDefCount: 1,
            sourceFileCount: 0, featureCount: layoutFeatureRows.Length);
        var layoutModules = new TrRmModuleTableReader(layoutModulesBytes,
            1, strings, 0, 1, 1, 0, layoutFeatureRows.Length);
        var supportedTargetLayoutExecution
            = TrRmConstantExecutionTableWriter.Encode(
                targetLayoutExecutionRows, bodyCount: 1, substitutions,
                constantSources, default, default, default,
                targetLayoutDependencies, methods, typeDefs, signatures,
                genericParams, contracts, default, targetProfilesWithLayout,
                default, default, layoutFeatures, layoutModules, s_schema);
        Assert.That(supportedTargetLayoutExecution.Length, Is.EqualTo(80));

        var mismatchedSubstitutionBytes =
            TrRmConstantExecutionSubstitutionTableWriter.Encode([
                new TrRmConstantExecutionSubstitutionRow(2, 1),
                new TrRmConstantExecutionSubstitutionRow(1, 0xF200_0008),
            ], constantSourceCount: 1, default, s_schema);
        var mismatchedSubstitutions =
            new TrRmConstantExecutionSubstitutionTableReader(
                mismatchedSubstitutionBytes, 2, constantSourceCount: 1,
                default, s_schema);
        var mismatchedSubstitutionRejected = false;
        try
        {
            _ = TrRmConstantExecutionTableWriter.Encode([
                new TrRmConstantExecutionRow(1, 1, 0xF200_0008, 0,
                    1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            ], bodyCount: 1, mismatchedSubstitutions, constantSources,
                default, default, default, default, methods, typeDefs,
                signatures, genericParams, contracts, default, targetProfiles,
                default, default, features, modules, s_schema);
        }
        catch (InvalidDataException)
        {
            mismatchedSubstitutionRejected = true;
        }
        Assert.That(mismatchedSubstitutionRejected, Is.True);
    }

    [Test]
    public void CapturedTypeFormIsScopedToCandidateProfileAndCaptureRows()
    {
        var captured = TrRmTypeSpecTableWriter.PredicateCapturedType(1);
        var types = TrRmTypeSpecTableWriter.Encode([
            captured,
            TrRmTypeSpecTableWriter.PredicateCapturedType(1),
        ], 0, 0, 0, 0, s_schema, predicateCaptureCount: 1);
        var reader = new TrRmTypeSpecTableReader(types, 2, 0, 0, 0,
            nativeFunctionPointerSignatureCount: 0, schema: s_schema,
            predicateCaptureCount: 1);

        Assert.That(reader.GetTypeSpecRef(1).Form, Is.EqualTo((byte)0x10));
        Assert.That(reader.GetPredicateCaptureReference(2), Is.EqualTo(1));
        Assert.That(reader.AreEquivalentTypeSpecRefs(1, 2), Is.True);

        _ = Assert.Throws<InvalidDataException>(() => TrRmTypeSpecTableWriter.Encode([
            captured,
        ], 0, 0, 0, 0));
        _ = Assert.Throws<InvalidDataException>(() => TrRmTypeSpecTableWriter.Encode([
            captured,
        ], 0, 0, 0, 0, s_schema, predicateCaptureCount: 0));
        _ = Assert.Throws<InvalidDataException>(() => TrRmTypeSpecTableWriter.Encode([
            new byte[] { 0x10, 0x01, 1, 0, 0, 0 },
        ], 0, 0, 0, 0, s_schema, predicateCaptureCount: 1));
    }

    [Test]
    public void ValidatesGeneralNodeOperandsOperationsAndControlRows()
    {
        var typeBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeBytes, 1, 0, 0, 0);
        var operations = TrRmPredicateOperationTableWriter.Encode([
            new TrRmPredicateOperationRow(0x0014, 0),
        ], s_schema);
        var controls = TrRmPredicateControlTableWriter.Encode([
            new TrRmPredicateControlRow(1, uint.MaxValue),
            new TrRmPredicateControlRow(2, 3),
            new TrRmPredicateControlRow(3, 0),
        ], s_schema);
        var counts = new TrRmPredicateTableRowCounts(
            ReadRootCount: 1, FrozenValueNodeCount: 0, CaptureCount: 0,
            OperationCount: 1, CallCount: 0, TypeUseCount: 0,
            FieldPathCount: 0, PropertyCount: 0, UnionCaseCount: 0,
            ControlCount: 3, TypePatternCount: 0);
        var operands = TrRmPredicateNodeOperandTableWriter.Encode([
            new TrRmPredicateNodeOperandRow(4, 1),
            new TrRmPredicateNodeOperandRow(1, 1),
        ], counts, s_schema);
        var nodes = TrRmPredicateNodeTableWriter.Encode([
            new TrRmPredicateNodeRow(0x21, 1, 1, 1),
            new TrRmPredicateNodeRow(0x10, 1, 2, 1),
        ], operandCount: 2, typeSpecs, s_schema);
        var nodeReader = new TrRmPredicateNodeTableReader(
            nodes, 2, 2, typeSpecs, s_schema);
        var operandReader = new TrRmPredicateNodeOperandTableReader(
            operands, 2, counts, s_schema);
        var operationReader = new TrRmPredicateOperationTableReader(
            operations, 1, s_schema);
        var controlReader = new TrRmPredicateControlTableReader(
            controls, 3, s_schema);

        Assert.That(nodeReader.GetNodeKind(1), Is.EqualTo(0x21u));
        Assert.That(operandReader.GetOperandKind(1), Is.EqualTo((byte)4));
        Assert.That(operationReader.GetOperationCode(1), Is.EqualTo((ushort)0x0014));
        Assert.That(controlReader.GetValue(1), Is.EqualTo(uint.MaxValue));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateNodeOperandTableWriter.Encode([
                new TrRmPredicateNodeOperandRow(4, 0),
            ], counts, s_schema));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateOperationTableWriter.Encode([
                new TrRmPredicateOperationRow(0x0014, 1),
            ], s_schema));
        var rejectedUnknownNode = false;
        try
        {
            _ = TrRmPredicateNodeTableWriter.Encode([
                new TrRmPredicateNodeRow(0x07, 1, 0, 0),
            ], 0, typeSpecs, s_schema);
        }
        catch (InvalidDataException)
        {
            rejectedUnknownNode = true;
        }
        Assert.That(rejectedUnknownNode, Is.True);
    }

    [Test]
    public void ValidatesReadRootsDependenciesAndEvaluationBounds()
    {
        var roots = TrRmPredicateReadRootTableWriter.Encode([
            new TrRmPredicateReadRootRow(5, 0),
            new TrRmPredicateReadRootRow(8, 1),
        ], immutableDataCount: 0, captureCount: 1, s_schema);
        var dependencies = TrRmPredicateReadDependencyTableWriter.Encode([
            new TrRmPredicateReadDependencyRow(1, 1),
        ], methodDefCount: 1, constraintCount: 0, witnessCount: 0,
            witnessProjectionCount: 0);
        var rootReader = new TrRmPredicateReadRootTableReader(
            roots, 2, immutableDataCount: 0, captureCount: 1, s_schema);
        var dependencyReader = new TrRmPredicateReadDependencyTableReader(
            dependencies, 1, methodDefCount: 1, constraintCount: 0,
            witnessCount: 0, witnessProjectionCount: 0);
        var bounds = TrRmEvaluationReadBoundTableWriter.Encode([
            new TrRmEvaluationReadBoundRow(1, 1),
        ], methodDefCount: 1, readFootprintCount: 1, s_schema);
        var boundReader = new TrRmEvaluationReadBoundTableReader(
            bounds, 1, methodDefCount: 1, readFootprintCount: 1, s_schema);

        Assert.That(rootReader.GetRootKind(2), Is.EqualTo((byte)8));
        Assert.That(dependencyReader.GetDependencyKind(1), Is.EqualTo((byte)1));
        Assert.That(boundReader.GetReadFootprintRow(1), Is.EqualTo(1));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadRootTableWriter.Encode([
                new TrRmPredicateReadRootRow(8, 1),
            ], 0, 1, TrRmArtifactSchema.Standard));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadDependencyTableWriter.Encode([
                new TrRmPredicateReadDependencyRow(4, 1),
            ], 1, 0, 0, 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmEvaluationReadBoundTableWriter.Encode([
                new TrRmEvaluationReadBoundRow(1, 1),
                new TrRmEvaluationReadBoundRow(1, 1),
            ], 1, 1, s_schema));
    }

    [Test]
    public void ValidatesCallAndTypePatternExtentsAndRejectsRecipeCycles()
    {
        var typeBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeBytes, 1, 0, 0, 0);
        var dependencyBytes = TrRmPredicateReadDependencyTableWriter.Encode([
            new TrRmPredicateReadDependencyRow(1, 1),
        ], 1, 0, 0, 0);
        var dependencies = new TrRmPredicateReadDependencyTableReader(
            dependencyBytes, 1, 1, 0, 0, 0);
        var call = new byte[24];
        call[0] = 1;
        call[1] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(call.AsSpan(4), 1);
        var calls = TrRmPredicateCallTableWriter.Encode([call],
            readDependencyCount: 1, methodDefCount: 1, constantSourceCount: 0,
            dependencies, typeSpecs, s_schema);
        var callReader = new TrRmPredicateCallTableReader(calls, 1, 1, 1, 0,
            dependencies, typeSpecs, s_schema);
        Assert.That(callReader.GetDependencyRef(1), Is.EqualTo(1u));

        byte[] anyArgument = [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        byte[] readReference = [9, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0];
        var patterns = TrRmPredicateTypePatternTableWriter.Encode(
            [anyArgument, readReference], 0, 0, 0, s_schema);
        var patternReader = new TrRmPredicateTypePatternTableReader(
            patterns, 2, 0, 0, 0, s_schema);
        Assert.That(patternReader.GetKind(2), Is.EqualTo((byte)9));
        Assert.That(patternReader.GetArgumentRef(2, 0), Is.EqualTo(1u));

        byte[] firstCycleRow = [9, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0];
        byte[] secondCycleRow = [10, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0];
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateTypePatternTableWriter.Encode(
                [firstCycleRow, secondCycleRow], 0, 0, 0, s_schema));
    }

    [Test]
    public void RejectsPredicateCallsToNativeFunctionPointerAnchorMethods()
    {
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.NativeFunctionPointer(1, 2),
        ], typeDefCount: 0, importCount: 0, genericParamCount: 0,
            nativeFunctionPointerSignatureCount: 0, s_schema,
            methodDefCount: 2);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1,
            typeDefCount: 0, importCount: 0, genericParamCount: 0,
            nativeFunctionPointerSignatureCount: 0, s_schema,
            methodDefCount: 2);
        var dependencyBytes = TrRmPredicateReadDependencyTableWriter.Encode([
            new TrRmPredicateReadDependencyRow(1, 2),
        ], methodDefCount: 2, constraintCount: 0, witnessCount: 0,
            witnessProjectionCount: 0);
        var dependencies = new TrRmPredicateReadDependencyTableReader(
            dependencyBytes, 1, methodDefCount: 2, constraintCount: 0,
            witnessCount: 0, witnessProjectionCount: 0);
        var directCallRow = new byte[24];
        directCallRow[0] = 1;
        directCallRow[1] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(
            directCallRow.AsSpan(4), 1);
        var directCallBytes = TrRmPredicateCallTableWriter.Encode(
            [directCallRow], readDependencyCount: 1, methodDefCount: 2,
            constantSourceCount: 0, dependencies, typeSpecs, s_schema);
        var directCalls = new TrRmPredicateCallTableReader(directCallBytes,
            1, 1, 2, 0, dependencies, typeSpecs, s_schema);

        Assert.That(TrRmGeneralPredicateGraphValidator
                .IsNativeFunctionPointerAnchorMethodDef(2, typeSpecs),
            Is.True);
        Assert.That(TrRmGeneralPredicateGraphValidator
                .IsPredicateCallTargetCallable(1, directCalls, dependencies,
                    typeSpecs),
            Is.False);

        var ordinaryDependencyBytes
            = TrRmPredicateReadDependencyTableWriter.Encode([
                new TrRmPredicateReadDependencyRow(1, 1),
            ], methodDefCount: 2, constraintCount: 0, witnessCount: 0,
                witnessProjectionCount: 0);
        var ordinaryDependencies
            = new TrRmPredicateReadDependencyTableReader(
                ordinaryDependencyBytes, 1, methodDefCount: 2,
                constraintCount: 0, witnessCount: 0,
                witnessProjectionCount: 0);
        var ordinaryCallBytes = TrRmPredicateCallTableWriter.Encode(
            [directCallRow], readDependencyCount: 1, methodDefCount: 2,
            constantSourceCount: 0, ordinaryDependencies, typeSpecs, s_schema);
        var ordinaryCalls = new TrRmPredicateCallTableReader(
            ordinaryCallBytes, 1, 1, 2, 0, ordinaryDependencies, typeSpecs,
            s_schema);
        Assert.That(TrRmGeneralPredicateGraphValidator
                .IsPredicateCallTargetCallable(1, ordinaryCalls,
                    ordinaryDependencies, typeSpecs),
            Is.True);

        var requirementDependencyBytes
            = TrRmPredicateReadDependencyTableWriter.Encode([
                new TrRmPredicateReadDependencyRow(2, 1),
            ], methodDefCount: 2, constraintCount: 1, witnessCount: 0,
                witnessProjectionCount: 0);
        var requirementDependencies
            = new TrRmPredicateReadDependencyTableReader(
                requirementDependencyBytes, 1, methodDefCount: 2,
                constraintCount: 1, witnessCount: 0,
                witnessProjectionCount: 0);
        var requirementCallRow = new byte[24];
        requirementCallRow[0] = 2;
        requirementCallRow[1] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(
            requirementCallRow.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            requirementCallRow.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            requirementCallRow.AsSpan(12), 0xF200_0001);
        var requirementCallBytes = TrRmPredicateCallTableWriter.Encode(
            [requirementCallRow], readDependencyCount: 1,
            methodDefCount: 2, constantSourceCount: 0,
            requirementDependencies, typeSpecs, s_schema);
        var requirementCalls = new TrRmPredicateCallTableReader(
            requirementCallBytes, 1, 1, 2, 0,
            requirementDependencies, typeSpecs, s_schema);
        Assert.That(TrRmGeneralPredicateGraphValidator
                .IsPredicateCallTargetCallable(1, requirementCalls,
                    requirementDependencies, typeSpecs),
            Is.False);
    }

    [Test]
    public void ValidatedPredicateArtifactRejectsNativeFunctionPointerAnchorCalls()
    {
        var ordinaryArtifact = CreatePredicateGraphArtifact(0x2C,
            capturedTypePattern: false, duplicateBinder: false, omitBinder: false,
            includeDirectPredicateCall: true);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        ordinaryArtifact)));

        var anchorArtifact = CreatePredicateGraphArtifact(0x2C,
            capturedTypePattern: false, duplicateBinder: false, omitBinder: false,
            includeDirectPredicateCall: true,
            anchorDirectPredicateCallTarget: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        anchorArtifact)));
    }

    [Test]
    public void StandardFeatureTwoClosureIncludesPredicateValueCaptures()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        CreatePredicateValueCaptureArtifact(
                            includeRootlessFeature: false))));
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        CreatePredicateValueCaptureArtifact(
                            includeRootlessFeature: true))));
    }

    [Test]
    public void ValidatesAnAttachedGeneralGraphInItsSelectedArtifactProfile()
    {
        var artifact = CreatePredicateGraphArtifact(0x35,
            capturedTypePattern: false, duplicateBinder: false, omitBinder: false);
        var index = TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(index);
        Assert.That(tables.PredicateGraphs.Count, Is.EqualTo(1));
        Assert.That(tables.PredicateNodes.GetNodeKind(1), Is.EqualTo(0x35u));
        Assert.That(tables.StaticFields.Count, Is.EqualTo(1));
        Assert.That(tables.EagerGroups.Count, Is.EqualTo(1));
        Assert.That(tables.StaticFieldContracts.Count, Is.EqualTo(1));
        Assert.That(tables.Contracts.HasStaticInitializationRole(2), Is.True);
        Assert.That(tables.Contracts.GetStaticInitializationRole(2).RoleTag,
            Is.EqualTo((byte)3));
        Assert.That(tables.Contracts.GetStaticInitializationDemands(2).Length,
            Is.EqualTo(1));

        var phaseThreeSelector = CreateTypeIdentityGraphArtifact(1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(phaseThreeSelector)));
    }

    [Test]
    public void RequiresOnePositiveBindingSiteForEveryCapture()
    {
        var boundTypePattern = CreateCapturedTypePatternGraphArtifact(
            duplicateBinder: false, omitBinder: false);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(boundTypePattern)));

        var duplicateBinder = CreateCapturedTypePatternGraphArtifact(
            duplicateBinder: true, omitBinder: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(duplicateBinder)));

        var missingBinder = CreateCapturedTypePatternGraphArtifact(
            duplicateBinder: false, omitBinder: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(missingBinder)));
    }

    [Test]
    public void RejectsUnboundInitializationRolesAndDuplicateDemands()
    {
        _ = Assert.Throws<InvalidDataException>(() => {
            var unboundRole = CreatePredicateGraphArtifact(0x35,
                capturedTypePattern: false, duplicateBinder: false, omitBinder: false,
                    initializationRoleTag: 4);
            _ = TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(unboundRole));
        });

        _ = Assert.Throws<InvalidDataException>(() =>
            CreatePredicateGraphArtifact(0x35, capturedTypePattern: false,
                duplicateBinder: false, omitBinder: false,
                duplicateInitializationDemand: true));
    }

    [Test]
    public void RequiresFeature19ForRetainedConstantExecutionIdentities()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            CreatePredicateGraphArtifact(0x35,
                capturedTypePattern: false, duplicateBinder: false,
                omitBinder: false, includeConstantExecution: true,
                includeConstantSubstitutionCycle: true));

        var supported = CreatePredicateGraphArtifact(0x35,
            capturedTypePattern: false, duplicateBinder: false, omitBinder: false,
            includeConstantExecution: true, includeImmutableFeature: true,
            includeImmutableDependency: true,
            includePredicateTypeConstantExecution: true);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(supported)));

        var missingDependencyFeature = CreatePredicateGraphArtifact(0x35,
            capturedTypePattern: false, duplicateBinder: false, omitBinder: false,
            includeConstantExecution: true, includeImmutableDependency: true,
            includePredicateTypeConstantExecution: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        missingDependencyFeature)));

        var missingPatternTypeFeature = CreatePredicateGraphArtifact(0x34,
            capturedTypePattern: true, duplicateBinder: false, omitBinder: false,
            includeConstantExecution: true, includeImmutableDependency: true,
            includePredicateTypeConstantExecution: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        missingPatternTypeFeature)));

        var missingViewFeature = CreatePredicateGraphArtifact(0x35,
            capturedTypePattern: false, duplicateBinder: false, omitBinder: false,
            includeConstantExecution: true, includeLogicalViewArgument: true,
            includePredicateTypeConstantExecution: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        missingViewFeature)));

        var unusedView = CreatePredicateGraphArtifact(0x35,
            capturedTypePattern: false, duplicateBinder: false, omitBinder: false,
            includeConstantExecution: true, includeImmutableFeature: false,
            includeImmutableDependency: false, includeLogicalViewArgument: false);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(unusedView)));
    }

    private static byte[] CreateTypeIdentityGraphArtifact(uint nodeKind)
        => CreatePredicateGraphArtifact(nodeKind, capturedTypePattern: false,
            duplicateBinder: false, omitBinder: false);

    private static byte[] CreateCapturedTypePatternGraphArtifact(
        bool duplicateBinder, bool omitBinder)
        => CreatePredicateGraphArtifact(0x34, capturedTypePattern: true,
            duplicateBinder, omitBinder);

    private static byte[] CreatePredicateGraphArtifact(uint nodeKind,
        bool capturedTypePattern, bool duplicateBinder, bool omitBinder,
        byte initializationRoleTag = 3,
        bool duplicateInitializationDemand = false,
        bool includeConstantExecution = false,
        bool includeImmutableFeature = false,
        bool includeImmutableDependency = false,
        bool includeLogicalViewArgument = false,
        bool includePredicateTypeConstantExecution = false,
        bool includeConstantSubstitutionCycle = false,
        bool includeDirectPredicateCall = false,
        bool anchorDirectPredicateCallTarget = false)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("Owner", 0), ("Predicate", 0),
            ("Field", 0), ("Initializer", 0), ("Generic", 0), ("N", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 8);
        var featureRows = new List<TrRmFeatureRow> {
            TrRmFeatureRow.GeneralPredicateGraphsCandidate,
            TrRmFeatureRow.LayoutQueries,
        };
        if (anchorDirectPredicateCallTarget)
        {
            featureRows.Add(TrRmFeatureRow.NativeBoundaryContracts);
            featureRows.Add(TrRmFeatureRow.RootlessNativeValues);
        }
        if (includeConstantExecution)
        {
            featureRows.Add(TrRmFeatureRow.ConstantExecution);
        }
        if (includeImmutableFeature)
        {
            featureRows.Add(TrRmFeatureRow.ImmutableDataIdentitiesCandidate);
        }
        featureRows.Sort(static (left, right)
            => left.FeatureId.CompareTo(right.FeatureId));
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, s_schema);
        var featureCount = featureRows.Count;
        var typeDefCount = includeConstantExecution ? 2 : 1;
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1, featureCount),
        ], strings, moduleRefCount: includeConstantExecution ? 1 : 0,
            typeDefCount, methodDefCount: 2,
            sourceFileCount: 0, featureCount);
        var modules = new TrRmModuleTableReader(
            moduleBytes, 1, strings, includeConstantExecution ? 1 : 0,
            typeDefCount, 2, 0, featureCount);
        var contractCount = 3 + (duplicateInitializationDemand ? 1 : 0);
        var typeDefRows = new List<TrRmTypeDefRow> {
            new TrRmTypeDefRow(1, 3, 4, 0, 0, 0, 0, 1, 1, 0, 0),
        };
        if (includeConstantExecution)
        {
            typeDefRows.Add(new TrRmTypeDefRow(1, 7, 4, 0, 0, 1, 1,
                2, 1, 0, 0));
        }
        var genericParamCount = includeConstantExecution ? 1 : 0;
        var layoutCount = typeDefCount;
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldCount: 0, genericParamCount,
            layoutCount, contractCount);
        var typeDefs = new TrRmTypeDefTableReader(
            typeDefBytes, typeDefCount, strings, modules, 0,
            genericParamCount, layoutCount, contractCount);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutRows = new List<TrRmLayoutRow> {
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        };
        if (includeConstantExecution)
        {
            layoutRows.Add(new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0));
        }
        var layoutBytes = TrRmLayoutTableWriter.Encode(
            layoutRows, typeDefs, targetProfiles, layoutFieldCount: 0);
        var typeSpecRows = new List<ReadOnlyMemory<byte>>();
        if (includePredicateTypeConstantExecution)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02, 2));
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Constructed(1, [1]));
        }
        if (anchorDirectPredicateCallTarget)
        {
            typeSpecRows.Add(
                TrRmTypeSpecTableWriter.NativeFunctionPointer(1, 2));
        }
        if (capturedTypePattern)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.PredicateCapturedType(1));
        }
        var typeSpecBytes = typeSpecRows.Count == 0
            ? []
            : TrRmTypeSpecTableWriter.Encode(typeSpecRows,
                typeDefCount, includeConstantExecution ? 1 : 0,
                genericParamCount, 0, s_schema, methodDefCount: 2,
                predicateCaptureCount: capturedTypePattern ? 1 : 0);
        var typeSpecs = typeSpecRows.Count == 0
            ? default
            : new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecRows.Count,
                typeDefCount, includeConstantExecution ? 1 : 0,
                genericParamCount, nativeFunctionPointerSignatureCount: 0,
                schema: s_schema, methodDefCount: 2,
                predicateCaptureCount: capturedTypePattern ? 1 : 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0001, 0xF200_0008, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF200_0001, 0, 0, 0, 0, 0),
        ], typeSpecs, paramCount: 0, genericParamCount: 0);
        var signatures = new TrRmMethodSigTableReader(
            signatureBytes, 2, typeSpecs, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(1, 4, 1, 0, 1, 1, 0),
            new TrRmMethodDefRow(1, 6, 2, 0, 2, contractCount - 1, 0),
        ], strings, modules, typeDefs, signatures, contractCount, bodyCount: 0);
        var methods = new TrRmMethodDefTableReader(
            methodBytes, 2, strings, modules, typeDefs, signatures,
            contractCount, 0);
        var eagerGroupBytes = TrRmEagerGroupTableWriter.Encode([
            new TrRmEagerGroupRow(1, 1, 1, 0, 0, 2),
        ], typeDefCount: 1, initializerCount: 0, methodDefCount: 2, s_schema);
        var eagerGroups = new TrRmEagerGroupTableReader(eagerGroupBytes, 1,
            typeDefCount: 1, initializerCount: 0, methodDefCount: 2, s_schema);
        var staticFieldBytes = TrRmStaticFieldTableWriter.Encode([
            new TrRmStaticFieldRow(1, 5, 0xF200_0008, 0, 1),
        ], strings, typeDefs, typeSpecs, eagerGroups, s_schema);
        var staticFields = new TrRmStaticFieldTableReader(staticFieldBytes, 1,
            strings, typeDefs, typeSpecs, eagerGroups, s_schema);
        var staticPublicationCount = includeConstantExecution ? 1 : 0;
        var staticFieldContractBytes =
            TrRmStaticFieldContractTableWriter.Encode([
                new TrRmStaticFieldContractRow(1, 1, 0, 0, 0, 0,
                    staticPublicationCount),
            ], staticFieldCount: 1, lazyUnitCount: 0,
            publicationCount: staticPublicationCount,
            staticFields, s_schema);
        var staticFieldContracts = new TrRmStaticFieldContractTableReader(
            staticFieldContractBytes, 1, 1, 0, staticPublicationCount,
            staticFields, s_schema);
        var eagerInitializerBytes = TrRmEagerInitializerTableWriter.Encode(
            Array.Empty<TrRmEagerInitializerRow>(), eagerGroups, staticFields,
            staticFieldContracts, methodDefCount: 2, s_schema);
        var eagerInitializers = new TrRmEagerInitializerTableReader(
            eagerInitializerBytes, 0, eagerGroups, staticFields,
            staticFieldContracts, 2, s_schema);
        var lazyUnitBytes = TrRmLazyUnitTableWriter.Encode(
            Array.Empty<TrRmLazyUnitRow>(), eagerGroups, eagerInitializers,
            staticFields, staticFieldContracts, methodDefCount: 2, s_schema);
        var lazyUnits = new TrRmLazyUnitTableReader(lazyUnitBytes, 0,
            eagerGroups, eagerInitializers, staticFields, staticFieldContracts,
            2, s_schema);
        var constantSources = default(TrRmConstantSourceTableReader);
        var contractPayload = new byte[12];
        contractPayload[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(contractPayload.AsSpan(8), 1);
        byte[] initializationRolePayload = [
            initializationRoleTag, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0,
        ];
        byte[] initializationDemandPayload = [1, 0, 0, 0, 0, 0, 0, 0];
        var contractRows = new List<TrRmContractRow> {
            new TrRmContractRow(0x1600_0001, 0x0B, contractPayload),
            new TrRmContractRow(0x1600_0002, 0x13, initializationRolePayload),
            new TrRmContractRow(0x1600_0002, 0x14, initializationDemandPayload),
        };
        if (duplicateInitializationDemand)
        {
            contractRows.Add(new TrRmContractRow(
                0x1600_0002, 0x14, initializationDemandPayload));
        }
        var contracts = TrRmContractTableWriter.EncodeGeneralPredicateGraphsDraft(
            contractRows, typeDefs, methods, signatures, default, typeSpecs,
            fieldPathCount: 0, features: new TrRmFeatureTableReader(
                featureBytes, featureCount, s_schema), modules,
            predicateGraphCount: 1, eagerGroups: eagerGroups,
            staticFields: staticFields, staticFieldContracts: staticFieldContracts,
            eagerInitializers: eagerInitializers, lazyUnits: lazyUnits);
        var contractReader = new TrRmContractTableReader(contracts,
            contractRows.Count, typeDefs, methods, signatures, default, typeSpecs,
            fieldPathCount: 0,
            features: new TrRmFeatureTableReader(
                featureBytes, featureCount, s_schema),
            modules, s_schema, predicateGraphCount: 1,
            eagerGroups, staticFields, staticFieldContracts,
            eagerInitializers, lazyUnits);
        byte[] staticPublicationBytes = [];
        byte[] immutableDataBytes = [];
        byte[] constantSourceBytes = [];
        byte[] genericParamBytes = [];
        byte[] intrinsicRegistrationBytes = [];
        byte[] constantExecutionBytes = [];
        byte[] constantExecutionSubstitutionBytes = [];
        byte[] constantExecutionArgumentBytes = [];
        byte[] constantExecutionDependencyBytes = [];
        byte[] logicalInputViewBytes = [];
        byte[] moduleRefBytes = [];
        byte[] importsBytes = [];
        if (includeConstantExecution)
        {
            constantSourceBytes = TrRmConstantSourceTableWriter.Encode([
                new TrRmConstantSourceRow(2, 1),
            ], frozenValueNodeCount: 0, constantExecutionCount: 1,
                targetValueBindingCount: 0);
            var constantSourceReader = new TrRmConstantSourceTableReader(
                constantSourceBytes, 1, frozenValueNodeCount: 0,
                constantExecutionCount: 1, targetValueBindingCount: 0);
            genericParamBytes = TrRmGenericParamTableWriter.Encode([
                new TrRmGenericParamRow(0x1300_0002, 0, 8, 0, 0,
                    2, 0, 0, 0xF200_0008, 0),
            ], strings, typeDefs, methods, signatures, typeSpecs,
                constraintCount: 0, constantSourceCount: 1);
            staticPublicationBytes =
                TrRmStaticImmutablePublicationTableWriter.Encode([
                    new TrRmStaticImmutablePublicationRow(1, 1, 1, 1, 1,
                        0xF200_0008, 1, 0),
                ], staticFields, staticFieldContracts, typeSpecs, lazyUnits,
                    constantSources, s_schema);
            immutableDataBytes = TrRmImmutableDataTableWriter.Encode([
                new TrRmImmutableDataRow(2, 0x2400_0001, 0),
            ], interfaceConstDefCount: 0, witnessAssociatedConstCount: 0,
                staticFieldCount: 1, typeSpecs, s_schema);
            intrinsicRegistrationBytes =
                TrRmIntrinsicRegistrationTableWriter.Encode([
                    new TrRmIntrinsicRegistrationRow(1, 17, 1, 1, 2),
                    new TrRmIntrinsicRegistrationRow(1, 17, 1, 2, 2),
                ], modules, s_schema);
            var intrinsicRegistrations =
                new TrRmIntrinsicRegistrationTableReader(
                    intrinsicRegistrationBytes, 2, modules, s_schema);
            moduleRefBytes = TrRmModuleRefTableWriter.Encode([
                new TrRmModuleRefRow(3, 0, 0),
            ], strings, modules);
            var moduleRefs = new TrRmModuleRefTableReader(
                moduleRefBytes, 1, strings, modules);
            importsBytes = TrRmImportTableWriter.Encode([
                new TrRmImportRow(1, 5, 0x2600_0002),
            ], strings, moduleRefs,
                typeDefs, methods, default, interfaces: default,
                associatedTypeDefs: default, fields: default,
                features: new TrRmFeatureTableReader(
                    featureBytes, featureCount, s_schema),
                modules, s_schema,
                intrinsicRegistrations: intrinsicRegistrations);
            var imports = new TrRmImportTableReader(importsBytes, 1,
                strings, moduleRefs,
                typeDefs, methods, default, interfaces: default,
                associatedTypeDefs: default, fields: default,
                features: new TrRmFeatureTableReader(
                    featureBytes, featureCount, s_schema),
                modules, s_schema,
                staticFields: staticFields,
                staticFieldContracts: staticFieldContracts,
                intrinsicRegistrations: intrinsicRegistrations);
            var constantExecutionSubstitutionRows
                = includeConstantSubstitutionCycle
                    ? [new TrRmConstantExecutionSubstitutionRow(2, 1)]
                    : Array.Empty<TrRmConstantExecutionSubstitutionRow>();
            constantExecutionSubstitutionBytes
                = TrRmConstantExecutionSubstitutionTableWriter.Encode(
                    constantExecutionSubstitutionRows,
                    constantSourceCount: 1, typeSpecs, s_schema);
            var substitutions =
                new TrRmConstantExecutionSubstitutionTableReader(
                    constantExecutionSubstitutionBytes,
                    includeConstantSubstitutionCycle ? 1 : 0,
                    1, typeSpecs, s_schema);
            constantExecutionArgumentBytes = includeLogicalViewArgument
                ? TrRmConstantExecutionArgumentTableWriter.Encode([
                    new TrRmConstantExecutionArgumentRow(2, 1),
                ], frozenValueNodeCount: 0, logicalInputViewCount: 1,
                    constantExecutionCount: 1)
                : [];
            var arguments = new TrRmConstantExecutionArgumentTableReader(
                constantExecutionArgumentBytes, includeLogicalViewArgument ? 1 : 0,
                frozenValueNodeCount: 0, logicalInputViewCount: 1,
                constantExecutionCount: 1);
            constantExecutionDependencyBytes = includeImmutableDependency
                ? TrRmConstantExecutionDependencyTableWriter.Encode([
                    new TrRmConstantExecutionDependencyRow(6, 1),
                ], methodDefCount: 2, constraintCount: 0,
                    interfaceWitnessCount: 0, witnessProjectionCount: 0,
                    intrinsicRegistrationCount: 2, immutableDataCount: 1,
                    fieldDefCount: 0, propertyDefCount: 0, unionCaseDefCount: 0)
                : [];
            var dependencies = new TrRmConstantExecutionDependencyTableReader(
                constantExecutionDependencyBytes,
                includeImmutableDependency ? 1 : 0, methodDefCount: 2,
                constraintCount: 0, interfaceWitnessCount: 0,
                witnessProjectionCount: 0, intrinsicRegistrationCount: 2,
                immutableDataCount: 1, fieldDefCount: 0,
                propertyDefCount: 0, unionCaseDefCount: 0);
            var pathEntries = new TrRmLogicalViewPathEntryTableReader([], 0,
                default, default, imports);
            logicalInputViewBytes = TrRmLogicalInputViewTableWriter.Encode([
                new TrRmLogicalInputViewRow(2, 1, 0xF200_0008, 0, 0),
            ], frozenValueNodeCount: 0, immutableDataCount: 1,
                constantExecutionCount: 1, typeSpecs, pathEntries);
            var views = new TrRmLogicalInputViewTableReader(
                logicalInputViewBytes, 1, frozenValueNodeCount: 0,
                immutableDataCount: 1, constantExecutionCount: 1,
                typeSpecs, pathEntries);
            var targetLayouts = new TrRmTargetLayoutDependencyTableReader(
                [], 0, typeSpecs, targetProfiles, fieldReferenceCount: 0);
            constantExecutionBytes = TrRmConstantExecutionTableWriter.Encode([
                new TrRmConstantExecutionRow(2, 2, 0xF200_0008, 0,
                    includeConstantSubstitutionCycle ? 1 : 0,
                    includeConstantSubstitutionCycle ? 1 : 0,
                    includeLogicalViewArgument ? 1 : 0,
                    includeLogicalViewArgument ? 1 : 0,
                    includeImmutableDependency ? 1 : 0,
                    includeImmutableDependency ? 1 : 0,
                    0, 0, 0, 0, 0, 0),
            ], bodyCount: 0, substitutions, constantSourceReader,
                arguments, views, dependencies,
                targetLayouts, methods, typeDefs, signatures, default,
                contractReader,
                typeSpecs, targetProfiles, intrinsicRegistrations, imports,
                new TrRmFeatureTableReader(featureBytes, featureCount, s_schema),
                modules, s_schema);
        }
        var graphRootNode = capturedTypePattern ? 3 : 1;
        var dependencyCount = includeDirectPredicateCall ? 1 : 0;
        var dependencyBytes = includeDirectPredicateCall
            ? TrRmPredicateReadDependencyTableWriter.Encode([
                new TrRmPredicateReadDependencyRow(1, 2),
            ], methodDefCount: 2, constraintCount: 0, witnessCount: 0,
                witnessProjectionCount: 0)
            : [];
        var predicateDependencies = new TrRmPredicateReadDependencyTableReader(
            dependencyBytes, dependencyCount, methodDefCount: 2,
            constraintCount: 0, witnessCount: 0, witnessProjectionCount: 0);
        var callBytes = includeDirectPredicateCall
            ? TrRmPredicateCallTableWriter.Encode([
                EncodeDirectPredicateCall(),
            ], readDependencyCount: dependencyCount, methodDefCount: 2,
                constantSourceCount: 0, predicateDependencies, typeSpecs,
                s_schema)
            : [];
        var graphBytes = TrRmPredicateGraphTableWriter.Encode([
            new TrRmPredicateGraphRow(graphRootNode, 1),
        ], nodeCount: capturedTypePattern ? 3 : 1, readFootprintCount: 1);
        var footprintBytes = TrRmPredicateReadFootprintTableWriter.Encode([
            capturedTypePattern
                ? new TrRmPredicateReadFootprintRow(1, 1, 0, 0)
                : new TrRmPredicateReadFootprintRow(0, 0,
                    includeDirectPredicateCall ? 1 : 0, dependencyCount),
        ], rootCount: capturedTypePattern ? 1 : 0, dependencyCount);
        var readRootBytes = capturedTypePattern
            ? TrRmPredicateReadRootTableWriter.Encode([
                new TrRmPredicateReadRootRow(1, 0),
            ], immutableDataCount: 0, captureCount: 1, s_schema)
            : [];
        var typeUseBytes = capturedTypePattern
            ? TrRmPredicateTypeUseTableWriter.Encode([
                new TrRmPredicateTypeUseRow(
                    includePredicateTypeConstantExecution ? 2u : 0xF200_0008),
                new TrRmPredicateTypeUseRow(
                    includePredicateTypeConstantExecution ? 3u : 1u),
            ], typeSpecs, s_schema)
            : TrRmPredicateTypeUseTableWriter.Encode([
                new TrRmPredicateTypeUseRow(0xF200_0008),
                new TrRmPredicateTypeUseRow(
                    includePredicateTypeConstantExecution ? 2u : 0xF200_0008),
            ], typeSpecs, s_schema);
        byte[] typePatternBytes = [];
        if (capturedTypePattern)
        {
            var nominalPattern = new byte[duplicateBinder ? 20 : 16];
            nominalPattern[0] = 7;
            BinaryPrimitives.WriteUInt32LittleEndian(nominalPattern.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                nominalPattern.AsSpan(8), duplicateBinder ? 2u : 1u);
            BinaryPrimitives.WriteUInt32LittleEndian(nominalPattern.AsSpan(12), 2);
            var firstCapturePattern = new byte[12];
            firstCapturePattern[0] = omitBinder ? (byte)2 : (byte)5;
            if (!omitBinder)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(firstCapturePattern.AsSpan(4), 1);
            }

            var typePatternRows = new List<ReadOnlyMemory<byte>> {
                nominalPattern,
                firstCapturePattern,
            };
            if (duplicateBinder)
            {
                var secondCapturePattern = new byte[12];
                secondCapturePattern[0] = 6;
                BinaryPrimitives.WriteUInt32LittleEndian(secondCapturePattern.AsSpan(4), 1);
                BinaryPrimitives.WriteUInt32LittleEndian(nominalPattern.AsSpan(16), 3);
                typePatternRows.Add(secondCapturePattern);
            }

            typePatternBytes = TrRmPredicateTypePatternTableWriter.Encode(
                typePatternRows, captureCount: 1, constantSourceCount: 0,
                typeUseCount: 2, s_schema);
        }
        var operandCounts = new TrRmPredicateTableRowCounts(
            capturedTypePattern ? 1 : 0, 0, capturedTypePattern ? 1 : 0,
            0, includeDirectPredicateCall ? 1 : 0,
            capturedTypePattern ? 0 : 2, 0, 0, 0, 0,
            capturedTypePattern ? 2 : 0);
        var operandBytes = capturedTypePattern
            ? TrRmPredicateNodeOperandTableWriter.Encode([
                new TrRmPredicateNodeOperandRow(1, 1),
                new TrRmPredicateNodeOperandRow(11, 1),
            ], operandCounts, s_schema)
            : includeDirectPredicateCall
                ? TrRmPredicateNodeOperandTableWriter.Encode([
                    new TrRmPredicateNodeOperandRow(5, 1),
                ], operandCounts, s_schema)
                : TrRmPredicateNodeOperandTableWriter.Encode([
                new TrRmPredicateNodeOperandRow(6, 1),
                new TrRmPredicateNodeOperandRow(6, 2),
            ], operandCounts, s_schema);
        var nodeBytes = capturedTypePattern
            ? TrRmPredicateNodeTableWriter.Encode([
                new TrRmPredicateNodeRow(0x11, 0xF200_0008, 1, 1),
                new TrRmPredicateNodeRow(0x49, 0xF200_0001, 2, 1),
                new TrRmPredicateNodeRow(0x34, 0xF200_0001, 0, 0),
            ], 2, typeSpecs, s_schema)
            : TrRmPredicateNodeTableWriter.Encode([
                new TrRmPredicateNodeRow(
                    includeDirectPredicateCall ? 0x2C : nodeKind,
                    0xF200_0001, 1,
                    includeDirectPredicateCall ? 1u : 2u),
            ], includeDirectPredicateCall ? 1 : 2, typeSpecs, s_schema);
        var graphReader = new TrRmPredicateGraphTableReader(
            graphBytes, 1, capturedTypePattern ? 3 : 1, 1);
        var childEdgeBytes = capturedTypePattern
            ? TrRmPredicateChildEdgeTableWriter.Encode([
                new TrRmPredicateChildEdgeRow(3, 0, 1),
                new TrRmPredicateChildEdgeRow(3, 1, 2),
            ], 3, graphReader)
            : [];
        var captureBytes = capturedTypePattern
            ? TrRmPredicateCaptureTableWriter.Encode([
                new TrRmPredicateCaptureRow(1, 0, 3, 0,
                    includePredicateTypeConstantExecution ? 3u : 1u),
            ], 1, typeSpecs)
            : [];
        var sections = new List<TrRmSection> {
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 8, stringBytes),
            new TrRmSection(0x0004, 0, (uint)featureCount, featureBytes),
            new TrRmSection(0x0013, 0, (uint)typeDefCount, typeDefBytes),
            new TrRmSection(0x0015, 0, (uint)layoutCount, layoutBytes),
            new TrRmSection(0x0016, 0, 2, methodBytes),
            new TrRmSection(0x0017, 0, 2, signatureBytes),
            new TrRmSection(0x001B, 0, (uint)contractCount, contracts),
            new TrRmSection(0x0055, 0, 1, staticFieldBytes),
            new TrRmSection(0x0056, 0, 1, staticFieldContractBytes),
            new TrRmSection(0x0057, 0, 1, eagerGroupBytes),
        };
        if (includeConstantExecution)
        {
            sections.Add(new TrRmSection(0x0019, 0, 1, genericParamBytes));
            sections.Add(new TrRmSection(0x0030, 0, 1, constantSourceBytes));
            sections.Add(new TrRmSection(0x0041, 0, 1, immutableDataBytes));
            sections.Add(new TrRmSection(0x0042, 0, 1, constantExecutionBytes));
            if (includeConstantSubstitutionCycle)
            {
                sections.Add(new TrRmSection(0x005E, 0, 1,
                    constantExecutionSubstitutionBytes));
            }
            if (includeLogicalViewArgument)
            {
                sections.Add(new TrRmSection(
                    0x0043, 0, 1, constantExecutionArgumentBytes));
            }
            if (includeImmutableDependency)
            {
                sections.Add(new TrRmSection(
                    0x0044, 0, 1, constantExecutionDependencyBytes));
            }
            sections.Add(new TrRmSection(0x0046, 0, 1, logicalInputViewBytes));
            sections.Add(new TrRmSection(0x005C, 0, 1, staticPublicationBytes));
            sections.Add(new TrRmSection(0x0010, 0, 1, moduleRefBytes));
            sections.Add(new TrRmSection(0x001C, 0, 1, importsBytes));
            sections.Add(new TrRmSection(0x005D, 0, 2, intrinsicRegistrationBytes));
        }
        if (capturedTypePattern)
        {
            sections.Add(new TrRmSection(0x0039, 0, 1, readRootBytes));
            sections.Add(new TrRmSection(0x003D, 0, 2, childEdgeBytes));
            sections.Add(new TrRmSection(0x003E, 0, 1, captureBytes));
            sections.Add(new TrRmSection(0x0052, 0,
                duplicateBinder ? 3u : 2u, typePatternBytes));
        }

        if (typeSpecRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0011, 0,
                (uint)typeSpecRows.Count, typeSpecBytes));
        }

        sections.Add(new TrRmSection(0x0037, 0, 1, graphBytes));
        sections.Add(new TrRmSection(0x0038, 0, 1, footprintBytes));
        if (includeDirectPredicateCall)
        {
            sections.Add(new TrRmSection(0x003A, 0,
                (uint)dependencyCount, dependencyBytes));
        }
        sections.Add(new TrRmSection(0x003B, 0,
            capturedTypePattern ? 3u : 1u, nodeBytes));
        sections.Add(new TrRmSection(0x003C, 0,
            capturedTypePattern ? 2u : includeDirectPredicateCall ? 1u : 2u,
            operandBytes));
        sections.Add(new TrRmSection(0x004F, 0, 2, typeUseBytes));
        if (includeDirectPredicateCall)
        {
            sections.Add(new TrRmSection(0x004E, 0, 1, callBytes));
        }
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, s_schema);
    }

    private static byte[] EncodeDirectPredicateCall()
    {
        var row = new byte[24];
        row[0] = 1;
        row[1] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(4), 1);
        return row;
    }

    private static byte[] CreatePredicateValueCaptureArtifact(
        bool includeRootlessFeature)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("Predicate", 0), ("Anchor", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 4);
        var featureRows = includeRootlessFeature
            ? new[] {
                TrRmFeatureRow.NativeBoundaryContracts,
                TrRmFeatureRow.RootlessNativeValues,
                TrRmFeatureRow.LayoutQueries,
                TrRmFeatureRow.GeneralPredicateGraphsCandidate,
            }
            : [
                TrRmFeatureRow.NativeBoundaryContracts,
                TrRmFeatureRow.LayoutQueries,
                TrRmFeatureRow.GeneralPredicateGraphsCandidate,
            ];
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, s_schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1, 1,
                featureRows.Length, 1),
        ], strings, moduleRefCount: 0, typeDefCount: 0, methodDefCount: 2,
            sourceFileCount: 0, featureCount: featureRows.Length);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            moduleRefCount: 0, typeDefCount: 0, methodDefCount: 2,
            sourceFileCount: 0, featureCount: featureRows.Length);
        var typeSpecRows = new ReadOnlyMemory<byte>[] {
            TrRmTypeSpecTableWriter.NativeFunctionPointer(1, 2),
        };
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount: 0, importCount: 0, genericParamCount: 0,
            nativeFunctionPointerSignatureCount: 0, s_schema,
            methodDefCount: 2);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1,
            typeDefCount: 0, importCount: 0, genericParamCount: 0,
            nativeFunctionPointerSignatureCount: 0, s_schema,
            methodDefCount: 2);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            fieldCount: 0, genericParamCount: 0, layoutCount: 0,
            contractCount: 1, s_schema);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0001, 0, 1, 1, 0, 0),
            new TrRmMethodSigRow(0xF200_0008, 0, 0, 0, 0, 0),
        ], typeSpecs, paramCount: 1, genericParamCount: 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 2,
            typeSpecs, paramCount: 1, genericParamCount: 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 0, 1, 1, 0),
            new TrRmMethodDefRow(0, 4, 2, 0, 0, 0, 0),
        ], strings, modules, typeDefs, signatures, contractCount: 1,
            bodyCount: 0);
        var methods = new TrRmMethodDefTableReader(methodBytes, 2, strings,
            modules, typeDefs, signatures, contractCount: 1, bodyCount: 0);
        var paramBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, 0xF200_0008, 0),
        ], signatures, typeSpecs);
        var parameters = new TrRmParamTableReader(
            paramBytes, 1, signatures, typeSpecs, s_schema);
        var contractPayload = new byte[12];
        contractPayload[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(
            contractPayload.AsSpan(8), 1);
        var contractsBytes =
            TrRmContractTableWriter.EncodeGeneralPredicateGraphsDraft([
                new TrRmContractRow(0x1600_0001, 0x0B, contractPayload),
            ], typeDefs, methods, signatures, parameters, typeSpecs,
                fieldPathCount: 0,
                features: new TrRmFeatureTableReader(
                    featureBytes, featureRows.Length, s_schema),
                modules, predicateGraphCount: 1);

        _ = new TrRmContractTableReader(contractsBytes, 1,
                typeDefs, methods, signatures, parameters, typeSpecs,
                fieldPathCount: 0,
                features: new TrRmFeatureTableReader(
                    featureBytes, featureRows.Length, s_schema),
                modules, s_schema, predicateGraphCount: 1);
        var graphBytes = TrRmPredicateGraphTableWriter.Encode([
                new TrRmPredicateGraphRow(3, 1),
        ], nodeCount: 3, readFootprintCount: 1);
        var graphReader = new TrRmPredicateGraphTableReader(
                graphBytes, 1, nodeCount: 3, readFootprintCount: 1);
        var footprintBytes = TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(1, 1, 0, 0),
        ], rootCount: 1, dependencyCount: 0);
        var readRootBytes = TrRmPredicateReadRootTableWriter.Encode([
                new TrRmPredicateReadRootRow(2, 0),
        ], immutableDataCount: 0, captureCount: 1, s_schema);
        var captureBytes = TrRmPredicateCaptureTableWriter.Encode([
                new TrRmPredicateCaptureRow(1, 0, 1, 0, 1),
        ], graphCount: 1, typeSpecs);
        var typeUseBytes = TrRmPredicateTypeUseTableWriter.Encode([
                new TrRmPredicateTypeUseRow(1),
        ], typeSpecs, s_schema);
        var operandCounts = new TrRmPredicateTableRowCounts(
                1, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0);
        var operandBytes = TrRmPredicateNodeOperandTableWriter.Encode([
                new TrRmPredicateNodeOperandRow(1, 1),
                new TrRmPredicateNodeOperandRow(6, 1),
                new TrRmPredicateNodeOperandRow(3, 1),
        ], operandCounts, s_schema);
        var nodeBytes = TrRmPredicateNodeTableWriter.Encode([
                new TrRmPredicateNodeRow(0x10, 0xF200_0008, 1, 1),
                new TrRmPredicateNodeRow(0x45, 0xF200_0001, 2, 2),
                new TrRmPredicateNodeRow(0x34, 0xF200_0001, 0, 0),
        ], operandCount: 3, typeSpecs, s_schema);
        var childEdgeBytes = TrRmPredicateChildEdgeTableWriter.Encode([
                new TrRmPredicateChildEdgeRow(3, 0, 1),
                new TrRmPredicateChildEdgeRow(3, 1, 2),
        ], 3, graphReader);
        return TrRmArtifactWriter.Encode([
                new TrRmSection(0x0001, 0, 1, modulesBytes),
                new TrRmSection(0x0002, 0, 4, stringBytes),
                new TrRmSection(0x0004, 0, (uint)featureRows.Length, featureBytes),
                new TrRmSection(0x0011, 0, 1, typeSpecBytes),
                new TrRmSection(0x0016, 0, 2, methodBytes),
                new TrRmSection(0x0017, 0, 2, signatureBytes),
                new TrRmSection(0x0018, 0, 1, paramBytes),
                new TrRmSection(0x001B, 0, 1, contractsBytes),
                new TrRmSection(0x0037, 0, 1, graphBytes),
                new TrRmSection(0x0038, 0, 1, footprintBytes),
                new TrRmSection(0x0039, 0, 1, readRootBytes),
                new TrRmSection(0x003B, 0, 3, nodeBytes),
                new TrRmSection(0x003C, 0, 3, operandBytes),
                new TrRmSection(0x003D, 0, 2, childEdgeBytes),
                new TrRmSection(0x003E, 0, 1, captureBytes),
                new TrRmSection(0x004F, 0, 1, typeUseBytes),
        ], s_schema);
    }
}
