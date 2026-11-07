// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    internal static bool HasStandaloneNativeOutputProviderDeclaration(
        BoundProgram program)
        => program.EntryPoint < 0
            && program.Functions.Length == 1
            && program.Functions[0].IsExternal
            && program.Functions[0].DirectNativeImport is not null
            && !program.Functions[0].NativeOutputContracts.IsEmpty;

    internal static byte[] EmitStandardNativeOutputProviderDeclaration(
        BoundProgram program, string moduleIdentity, string schemaIdentity,
        uint moduleTargetRequirement, TrRmArtifactSchema schema)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        if (schema is not (TrRmArtifactSchema.ForeignInvocationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft)
            || moduleTargetRequirement
                != TrRmModuleTableReader.WindowsX64NativeTargetRequirement
            || program.EntryPoint >= 0
            || program.Functions.Length != 1
            || !program.Structures.IsEmpty
            || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty
            || !program.NativeFixedArrayTypes.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || program.RuntimePolicy != BoundRuntimePolicy.FailFast
            || program.RequiresSourceGrant
            || program.InvocationProfileId is not null
            || program.IsInvocationSourceBound)
        {
            throw Unsupported(
                "The standalone NativeOutput provider requires one bounded F/F+S declaration.");
        }

        var target = program.Functions[0];
        if (!target.IsExternal
            || target.IsPrivate || target.IsInternal
            || target.HasNativeExport
            || target.DirectNativeImport is not DirectNativeImport import
            || !import.Parameters.IsEmpty
            || target.NativeOutputContracts.IsEmpty
            || target.DoesNotReturn
            || target.ReturnType is not (PrimitiveType.Int32 or PrimitiveType.UInt32)
            || target.ParameterCount != target.Variables.Length
            || !DirectNativeImportContract.IsValid(target, program.Structures,
                program.NativeFunctionPointerSignatures,
                program.NativePointerTypes)
            || !NativeOutputContractValidator.IsValid(target,
                program.Structures, program.NativePointerTypes,
                program.NativeFunctionPointerSignatures)
            || !NativePointerTypeContract.IsValidTable(
                program.NativePointerTypes, program.Structures,
                program.NativeFunctionPointerSignatures))
        {
            throw Unsupported(
                "A standalone NativeOutput provider must be a public, bodyless, NativeParameter-free declaration.");
        }

        SemanticVerifier.VerifyLibraryWithDirectNativeImports(program);

        var typeSpecRows = new List<ReadOnlyMemory<byte>>();
        var nativePointerTypeRows = new Dictionary<PrimitiveType, int>();
        var nativeFunctionPointerTypeRows
            = new Dictionary<PrimitiveType, int>();
        var structureTypeDefRows = new Dictionary<int, int>();
        var typeDefTypeSpecRows = new Dictionary<int, int>();
        var nativeRawPointerTypeRow = 0;
        if (program.NativePointerTypes.Any(static pointer =>
                pointer.PointeeType == PrimitiveType.NativeRawPointer))
        {
            nativeRawPointerTypeRow = 1;
            typeSpecRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0));
        }

        uint GetTypeSpecRef(PrimitiveType type)
        {
            if (type == PrimitiveType.Void)
            {
                return 0xF200_0000;
            }

            if (NativePointerTypes.IsType(type))
            {
                return checked((uint)NativePointerTypeRow(type,
                    nativeRawPointerTypeRow, nativeFunctionPointerTypeRows,
                    typeSpecRows, nativePointerTypeRows,
                    program.NativePointerTypes, structureTypeDefRows,
                    typeDefTypeSpecRows));
            }

            if (type == PrimitiveType.NativeRawPointer)
            {
                if (nativeRawPointerTypeRow == 0)
                {
                    nativeRawPointerTypeRow = typeSpecRows.Count + 1;
                    typeSpecRows.Add(
                        TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0));
                }

                return checked((uint)nativeRawPointerTypeRow);
            }

            var scalarIndex = Array.IndexOf(s_types, type);
            if (scalarIndex < 0 || !DirectNativeImportContract.IsScalar(type))
            {
                throw Unsupported(
                    $"The standalone NativeOutput provider does not support TypeSpec {type}.");
            }

            return 0xF200_0000u | s_codes[scalarIndex];
        }

        var targetParameterTypeSpecRefs = new uint[target.ParameterCount];
        for (var formal = 0; formal < targetParameterTypeSpecRefs.Length;
            formal++)
        {
            targetParameterTypeSpecRefs[formal]
                = GetTypeSpecRef(target.Variables[formal].Type);
        }

        var targetResultTypeSpecRef = GetTypeSpecRef(target.ReturnType);
        var typeSpecBytes = typeSpecRows.Count == 0 ? []
            : TrRmTypeSpecTableWriter.Encode(typeSpecRows, 0, 0, 0, 0,
                schema, methodDefCount: 1);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, 0, 0, 0, 0, schema, 1);

        var stringsRows = new List<(string Text, int Encoding)> {
            ("", 0),
            (moduleIdentity, 0),
        };
        int AddString(string text)
        {
            var row = stringsRows.Count + 1;
            stringsRows.Add((text, 0));
            return row;
        }

        var methodNameStringRow = AddString(target.Name);
        var libraryNameStringRow = AddString(import.LibraryName);
        var entryNameStringRow = AddString(import.ExportName);
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows(stringsRows);
        var strings = new TrRmStringTableReader(stringsBytes, stringsRows.Count);
        var featureRows = new List<TrRmFeatureRow> {
            new(1, 1),
            new(2, 1),
            new(3, 1),
            TrRmFeatureRow.NativeOutputCellsCandidate,
            new(10, 1),
        };
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2,
                moduleTargetRequirement, 0, 0, 1, 1, 1, 1,
                1, featureRows.Count),
        ], strings, 0, 0, 1, 0, featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 1, 0, featureRows.Count);

        var parameterRows = new TrRmParamRow[target.ParameterCount];
        for (var formal = 0; formal < parameterRows.Length; formal++)
        {
            parameterRows[formal] = new TrRmParamRow(1, formal,
                targetParameterTypeSpecRefs[formal], 0);
        }

        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(targetResultTypeSpecRef, 0,
                parameterRows.Length == 0 ? 0 : 1,
                parameterRows.Length, 0, 0),
        ], typeSpecs, parameterRows.Length, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, parameterRows.Length, 0);
        var parameterBytes = TrRmParamTableWriter.Encode(parameterRows,
            signatures, typeSpecs, schema);
        var parameters = new TrRmParamTableReader(parameterBytes,
            parameterRows.Length, signatures, typeSpecs);

        var contractRows = new List<TrRmContractRow>();
        var contractOwner = 0x1600_0000u | 1u;
        contractRows.Add(new TrRmContractRow(contractOwner, 1,
            new byte[] { 4, 0, 0, 0 }));
        var predicateGraphs = new List<TrRmPredicateGraphRow>();
        var predicateFootprints
            = new List<TrRmPredicateReadFootprintRow>();
        var predicateRoots = new List<TrRmPredicateReadRootRow>();
        var predicateNodes = new List<TrRmPredicateNodeRow>();
        var predicateOperands
            = new List<TrRmPredicateNodeOperandRow>();
        var selectors = new SortedSet<byte>();
        foreach (var output in target.NativeOutputContracts)
        {
            _ = selectors.Add((byte)output.SuccessSelector);
            _ = selectors.Add((byte)output.ComplementSelector);
        }

        var graphBySelector = new Dictionary<byte, int>();
        uint phaseOrdinal = 0;
        foreach (var selector in selectors)
        {
            var graphRow = predicateGraphs.Count + 1;
            predicateGraphs.Add(new TrRmPredicateGraphRow(graphRow, graphRow));
            predicateFootprints.Add(
                new TrRmPredicateReadFootprintRow(graphRow, 1, 0, 0));
            predicateRoots.Add(new TrRmPredicateReadRootRow(5, 0));
            predicateNodes.Add(new TrRmPredicateNodeRow(selector,
                0xF200_0001, checked((uint)graphRow), 1));
            predicateOperands.Add(new TrRmPredicateNodeOperandRow(1,
                checked((uint)graphRow)));
            var payload = new byte[12];
            payload[0] = 3;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
                phaseOrdinal++);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8),
                checked((uint)graphRow));
            contractRows.Add(new TrRmContractRow(contractOwner, 0x0B,
                payload));
            graphBySelector.Add(selector, graphRow);
        }

        foreach (var output in target.NativeOutputContracts)
        {
            AddOutputState((byte)output.SuccessState,
                output.FormalOrdinal,
                graphBySelector[(byte)output.SuccessSelector]);
            AddOutputState((byte)output.ComplementState,
                output.FormalOrdinal,
                graphBySelector[(byte)output.ComplementSelector]);
            foreach (var dependency in output.Dependencies)
            {
                var payload = new byte[12];
                payload[0] = dependency.DestinationKind;
                payload[1] = (byte)dependency.SourceKind;
                payload[2] = (byte)dependency.Relationship;
                payload[3] = (byte)dependency.Phase;
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
                    checked((uint)dependency.DestinationOrdinal));
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8),
                    checked((uint)dependency.SourceOrdinal));
                contractRows.Add(new TrRmContractRow(contractOwner, 4,
                    payload));
            }
        }

        void AddOutputState(byte state, int formalOrdinal, int graphRow)
        {
            var payload = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(payload,
                checked((uint)formalOrdinal));
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
                checked((uint)graphRow));
            payload[8] = state;
            contractRows.Add(new TrRmContractRow(contractOwner, 2, payload));
        }

        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, contractRows.Count);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0, schema);
        var layouts = new TrRmLayoutTableReader([], 0, typeDefs,
            targetProfiles, 0);
        var methodRow = new TrRmMethodDefRow(0, methodNameStringRow, 1,
            Visibility(target), 1, contractRows.Count, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([methodRow], strings,
            modules, typeDefs, signatures, contractRows.Count, 0);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1, strings,
            modules, typeDefs, signatures, contractRows.Count, 0);
        var contractBytes = TrRmContractTableWriter
            .EncodeForeignInvocationDraft(contractRows, typeDefs, methods,
                signatures, parameters, typeSpecs, features, modules,
                predicateGraphs.Count, schema);
        var contracts = new TrRmContractTableReader(contractBytes,
            contractRows.Count, typeDefs, methods, signatures, parameters,
            typeSpecs, 0, features, modules, schema, predicateGraphs.Count);
        var nativeImportBytes = TrRmNativeImportTableWriter.Encode([
            new TrRmNativeImportRow(1, (byte)import.ResolutionMode,
                (byte)import.ResolutionPolicy,
                (byte)import.CallingConvention, 1,
                libraryNameStringRow, entryNameStringRow),
        ], strings, methods, default, typeDefs, default, modules,
            signatures, parameters, typeSpecs, contracts, layouts);
        var exportBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x1600_0001u, methodNameStringRow),
        ], strings, typeDefs, methods, default, default, default, default,
            default, features, modules, schema);
        var predicateGraphBytes = TrRmPredicateGraphTableWriter.Encode(
            predicateGraphs, predicateNodes.Count,
            predicateFootprints.Count);
        var predicateFootprintBytes
            = TrRmPredicateReadFootprintTableWriter.Encode(
                predicateFootprints, predicateRoots.Count, 0);
        var predicateRootBytes = TrRmPredicateReadRootTableWriter.Encode(
            predicateRoots, 0, 0, schema);
        var predicateNodeBytes = TrRmPredicateNodeTableWriter.Encode(
            predicateNodes, predicateOperands.Count, typeSpecs, schema);
        var operandCounts = new TrRmPredicateTableRowCounts(
            predicateRoots.Count, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var predicateOperandBytes
            = TrRmPredicateNodeOperandTableWriter.Encode(predicateOperands,
                operandCounts, schema);
        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, checked((uint)stringsRows.Count), stringsBytes),
            new(0x0004, 0, checked((uint)featureRows.Count), featureBytes),
            new(0x0011, 0, checked((uint)typeSpecRows.Count), typeSpecBytes),
            new(0x0016, 0, 1, methodBytes),
            new(0x0017, 0, 1, signatureBytes),
            new(0x0018, 0, checked((uint)parameterRows.Length), parameterBytes),
            new(0x001B, 0, checked((uint)contractRows.Count), contractBytes),
            new(0x001D, 0, 1, exportBytes),
            new(0x0021, 0, 1, nativeImportBytes),
            new(0x0037, 0, checked((uint)predicateGraphs.Count),
                predicateGraphBytes),
            new(0x0038, 0, checked((uint)predicateFootprints.Count),
                predicateFootprintBytes),
            new(0x0039, 0, checked((uint)predicateRoots.Count),
                predicateRootBytes),
            new(0x003B, 0, checked((uint)predicateNodes.Count),
                predicateNodeBytes),
            new(0x003C, 0, checked((uint)predicateOperands.Count),
                predicateOperandBytes),
        };
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections, schema);
        var index = schema == TrRmArtifactSchema.ForeignInvocationDraft
            ? TrRmArtifactIndex.ForForeignInvocationDraft(artifact)
            : TrRmArtifactIndex
                .ForForeignInvocationStorageAddressDraft(artifact);
        _ = schema == TrRmArtifactSchema.ForeignInvocationDraft
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(index)
            : TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                    index);
        return artifact;

        static uint Visibility(BoundFunction function)
        {
            return function.IsPrivate ? 0u
                : function.IsInternal ? 1u : 2u;
        }
    }
}
