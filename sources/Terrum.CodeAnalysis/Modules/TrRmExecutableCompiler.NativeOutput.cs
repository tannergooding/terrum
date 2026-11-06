// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    internal static bool HasDirectNativeOutputCall(BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        foreach (var function in program.Functions)
        {
            var found = false;
            if (!BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && (uint)call.Function < (uint)program.Functions.Length
                        && !program.Functions[call.Function]
                            .NativeOutputContracts.IsEmpty
                        && call.Arguments.Any(static argument =>
                            argument is BoundNativeOutputReservation))
                    {
                        found = true;
                    }
                }))
            {
                throw Unsupported(
                    "A function Body cannot be traversed for NativeOutput calls.");
            }

            if (found)
            {
                return true;
            }
        }

        return false;
    }

    internal static byte[] EmitStandardNativeOutputCallProgram(
        BoundProgram program, string moduleIdentity,
        TrRmArtifactSchema schema)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        if (schema is not (TrRmArtifactSchema.ForeignInvocationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft)
            || program.Functions.Length != 2
            || program.Structures.Any(static structure =>
                structure.BlittableTargetRequirement
                    != FixedLayoutStorage.WindowsX64TargetRequirement
                || structure.LayoutKind is not (1 or 2 or 3)
                || structure.CleanupMethod is not null
                || structure.IsUnique
                || structure.Fields.IsDefaultOrEmpty
                || structure.StandardTypeOrigin is not null)
            || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty
            || !program.NativeFixedArrayTypes.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || program.RuntimePolicy != BoundRuntimePolicy.FailFast
            || program.RequiresSourceGrant || program.InvocationProfileId is not null
            || program.IsInvocationSourceBound)
        {
            throw Unsupported(
                "Direct NativeOutput Body emission requires a bounded F scalar/pointer program.");
        }

        var targetIndex = -1;
        var callerIndex = -1;
        var outputCallCount = 0;
        BoundCall? outputCall = null;
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            if (function.DirectNativeImport is not null
                && function.IsExternal
                && !function.NativeOutputContracts.IsEmpty)
            {
                if (targetIndex >= 0)
                {
                    throw Unsupported(
                        "A direct NativeOutput Body supports one imported declaration.");
                }

                targetIndex = index;
            }
            else if (!function.IsExternal)
            {
                if (callerIndex >= 0)
                {
                    throw Unsupported(
                        "A direct NativeOutput Body supports one caller.");
                }

                callerIndex = index;
            }
            else
            {
                throw Unsupported(
                    "The direct NativeOutput Body contains an unrelated external function.");
            }

            if (!BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.Arguments.Any(static argument =>
                            argument is BoundNativeOutputReservation))
                    {
                        outputCallCount++;
                        outputCall = call;
                    }
                }))
            {
                throw Unsupported(
                    "A function Body cannot be traversed for NativeOutput calls.");
            }
        }

        if (targetIndex != 0 || callerIndex != 1 || outputCallCount != 1
            || outputCall is not BoundCall selectedCall
            || selectedCall.Function != targetIndex
            || (program.EntryPoint >= 0 && program.EntryPoint != callerIndex))
        {
            throw Unsupported(
                "The direct NativeOutput Body requires one direct import call.");
        }

        var target = program.Functions[targetIndex];
        var caller = program.Functions[callerIndex];
        if (!SemanticVerifierIsValidNativeOutputTarget(target, program)
            || caller.IsExternal || caller.DirectNativeImport is not null
            || caller.ParameterCount < 0
            || caller.ParameterCount > caller.Variables.Length
            || (caller.ReturnType != PrimitiveType.Void
                && caller.ReturnType != target.ReturnType)
            || !caller.NativeOutputContracts.IsEmpty
            || target.ReturnType is not (PrimitiveType.Int32 or PrimitiveType.UInt32)
            || target.DirectNativeImport is not DirectNativeImport import
            || (schema == TrRmArtifactSchema.ForeignInvocationDraft
                && !import.Parameters.IsEmpty)
            || selectedCall.Arguments.Length != target.ParameterCount
            || selectedCall.Type != target.ReturnType
            || (!selectedCall.ArgumentParameterIndices.IsDefaultOrEmpty
                && selectedCall.ArgumentParameterIndices.Length
                    != selectedCall.Arguments.Length))
        {
            throw Unsupported(
                "The direct NativeOutput call has unsupported declaration or argument facts.");
        }

        var outputVariables = new int[target.NativeOutputContracts.Length];
        var outputVariableByFormal = new Dictionary<int, int>(
            outputVariables.Length);
        var seenOutputVariables = new HashSet<int>();
        for (var outputIndex = 0; outputIndex < outputVariables.Length;
            outputIndex++)
        {
            var output = target.NativeOutputContracts[outputIndex];
            var formal = output.FormalOrdinal;
            if ((uint)formal >= (uint)target.ParameterCount
                || selectedCall.Arguments[formal]
                    is not BoundNativeOutputReservation reservation
                || reservation.PointeeType != output.PointeeType
                || !seenOutputVariables.Add(reservation.Variable)
                || reservation.Variable < caller.ParameterCount
                || (uint)reservation.Variable >= (uint)caller.Variables.Length
                || !caller.Variables[reservation.Variable].IsInitializationLocal)
            {
                throw Unsupported(
                    "The direct NativeOutput call requires one vacant local per output formal.");
            }

            if (!outputVariableByFormal.TryAdd(formal, reservation.Variable))
            {
                throw Unsupported(
                    "The direct NativeOutput call repeats an output formal.");
            }

            if (!selectedCall.ArgumentParameterIndices.IsDefaultOrEmpty
                && selectedCall.ArgumentParameterIndices[formal] != formal)
            {
                throw Unsupported(
                    "The direct NativeOutput Body requires formal-order arguments.");
            }

            outputVariables[outputIndex] = reservation.Variable;
        }

        var backingLocals = ValidateDirectNativeOutputCaller(caller,
            selectedCall, outputVariables);
        var backingLocalByVariable = backingLocals.ToDictionary(
            static local => local.Variable);
        var nativeParameterByFormal = import.Parameters.ToDictionary(
            static parameter => parameter.ParameterIndex);
        var nativeParameterAddressByFormal
            = new Dictionary<int, BoundNativeStorageAddress>(
                import.Parameters.Length);
        for (var formal = 0; formal < target.ParameterCount; formal++)
        {
            if (outputVariableByFormal.ContainsKey(formal))
            {
                continue;
            }

            if (!selectedCall.ArgumentParameterIndices.IsDefaultOrEmpty
                && selectedCall.ArgumentParameterIndices[formal] != formal)
            {
                throw Unsupported(
                    "The direct NativeOutput Body requires formal-order input arguments.");
            }

            if (nativeParameterByFormal.TryGetValue(formal,
                    out var nativeParameter))
            {
                if (schema
                        != TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                    || nativeParameter.Retention
                        != NativeParameterRetention.NoEscape
                    || nativeParameter.Extent != 0
                    || selectedCall.Arguments[formal]
                        is not BoundNativeStorageAddress address
                    || address.Variable < caller.ParameterCount
                    || !backingLocalByVariable.ContainsKey(address.Variable)
                    || address.Type != PrimitiveType.NativeStorageAddress
                    || address.ElementType != nativeParameter.PointeeType
                    || address.CarrierType != address.ElementType
                    || address.ElementCount != 1
                    || address.ElementStride == 0
                    || address.ElementStride != address.ByteExtent
                    || address.Index is not BoundConstant index
                    || index.Type != PrimitiveType.Int32
                    || index.Bits.Low != 0 || index.Bits.High != 0
                    || address.IsReadonly
                        != (nativeParameter.Access == NativeParameterAccess.Read)
                    || caller.Variables[address.Variable].Type
                        != nativeParameter.PointeeType
                    || ((nativeParameter.Access
                            is NativeParameterAccess.Write
                                or NativeParameterAccess.ReadWrite)
                        && !caller.Variables[address.Variable].IsMutable)
                    || !DirectNativeImportContract
                        .MatchesNativeParameterFormalType(
                            target.Variables[formal].Type,
                            nativeParameter.PointeeType,
                            nativeParameter.Access, program.NativePointerTypes))
                {
                    throw Unsupported(
                        "A direct NativeParameter Body requires stable backing from an initialized local.");
                }

                _ = nativeParameterAddressByFormal.TryAdd(formal,
                    (BoundNativeStorageAddress)selectedCall.Arguments[formal]);
            }
            else if (selectedCall.Arguments[formal] is not BoundRead read
                || read.Variable < 0
                || (read.Variable >= caller.ParameterCount
                    && !backingLocalByVariable.ContainsKey(read.Variable))
                || read.Type != target.Variables[formal].Type
                || read.Type != caller.Variables[read.Variable].Type)
            {
                throw Unsupported(
                    "A direct NativeOutput input must read a caller formal or its local copy.");
            }
        }

        if (!NativePointerTypeContract.IsValidTable(program.NativePointerTypes,
                program.Structures, program.NativeFunctionPointerSignatures))
        {
            throw Invalid("The direct NativeOutput program has invalid pointer types.");
        }
        _ = FixedLayoutStorage.Validate(program.Structures,
            program.NativeFixedArrayTypes);

        if (program.EntryPoint < 0)
        {
            SemanticVerifier.VerifyLibraryWithDirectNativeImports(program);
        }
        else
        {
            SemanticVerifier.VerifyWithDirectNativeImports(program);
        }

        var typeSpecRows = new List<ReadOnlyMemory<byte>>();
        var nativePointerTypeRows = new Dictionary<PrimitiveType, int>();
        var nativeFunctionPointerTypeRows
            = new Dictionary<PrimitiveType, int>();
        var typeDefTypeSpecRows = new Dictionary<int, int>();
        var structureTypeDefRows = new Dictionary<int, int>(
            program.Structures.Length);
        for (var structure = 0; structure < program.Structures.Length;
            structure++)
        {
            structureTypeDefRows.Add(structure, structure + 1);
        }
        var nativeRawPointerTypeRow = 0;
        if (program.NativePointerTypes.Any(static pointer =>
                pointer.PointeeType == PrimitiveType.NativeRawPointer))
        {
            nativeRawPointerTypeRow = typeSpecRows.Count + 1;
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

            var structureIndex = (int)type - 100;
            if (structureTypeDefRows.TryGetValue(structureIndex,
                    out var typeDefRow))
            {
                return checked((uint)TypeDefTypeSpecRow(typeSpecRows,
                    typeDefTypeSpecRows, typeDefRow));
            }

            var scalarIndex = Array.IndexOf(s_types, type);
            if (scalarIndex < 0 || !DirectNativeImportContract.IsScalar(type))
            {
                throw Unsupported(
                    $"The direct NativeOutput Body does not support TypeSpec {type}.");
            }

            return 0xF200_0000u | s_codes[scalarIndex];
        }

        var outputTypeMapTokens = new uint[outputVariables.Length];
        for (var formal = 0; formal < outputVariables.Length; formal++)
        {
            var type = target.NativeOutputContracts[formal].PointeeType;
            var typeSpecRef = GetTypeSpecRef(type);
            outputTypeMapTokens[formal] = (typeSpecRef >> 24) is 0xF2 or 0xF3
                ? typeSpecRef : 0x1100_0000u | typeSpecRef;
        }

        var targetParameterTypeSpecRefs = new uint[target.ParameterCount];
        for (var formal = 0; formal < targetParameterTypeSpecRefs.Length;
            formal++)
        {
            targetParameterTypeSpecRefs[formal]
                = GetTypeSpecRef(target.Variables[formal].Type);
        }

        var callerParameterTypeSpecRefs = new uint[caller.ParameterCount];
        for (var formal = 0; formal < callerParameterTypeSpecRefs.Length;
            formal++)
        {
            callerParameterTypeSpecRefs[formal]
                = GetTypeSpecRef(caller.Variables[formal].Type);
        }

        var targetResultTypeSpecRef = GetTypeSpecRef(target.ReturnType);
        var callerResultTypeSpecRef = GetTypeSpecRef(caller.ReturnType);
        var stringsRows = new List<(string Text, int Encoding)> {
            ("", 0),
            (moduleIdentity, 0),
            (target.Name, 0),
            (caller.Name, 0),
            (import.LibraryName, 0),
            (import.ExportName, 0),
        };
        int AddString(string text, int encoding)
        {
            var row = stringsRows.Count + 1;
            stringsRows.Add((text, encoding));
            return row;
        }

        var typeDefRows = new List<TrRmTypeDefRow>(program.Structures.Length);
        var fieldRows = new List<TrRmFieldDefRow>();
        var layoutRows = new List<TrRmLayoutRow>(
            checked(program.Structures.Length * 2));
        var layoutFieldRows = new List<TrRmLayoutFieldRow>();
        for (var structureIndex = 0;
            structureIndex < program.Structures.Length; structureIndex++)
        {
            var structure = program.Structures[structureIndex];
            var separator = structure.Name.LastIndexOf('.',
                StringComparison.Ordinal);
            var typeNamespace = separator < 0
                ? "" : structure.Name[..separator];
            var typeName = separator < 0
                ? structure.Name : structure.Name[(separator + 1)..];
            if (typeName.Length == 0)
            {
                throw Unsupported(
                    $"The direct NativeOutput TypeDef identity '{structure.Name}' is invalid.");
            }

            var firstField = structure.Fields.IsEmpty
                ? 0 : fieldRows.Count + 1;
            foreach (var field in structure.Fields)
            {
                var fieldTypeSpecRef = GetTypeSpecRef(field.Type);
                var visibility = field.IsPrivate ? 0u
                    : field.IsInternal ? 1u : 2u;
                var fieldFlags = visibility | (field.IsMutable ? 4u : 0u);
                fieldRows.Add(new TrRmFieldDefRow(structureIndex + 1,
                    AddString(field.Name, 0), fieldTypeSpecRef, fieldFlags));
            }

            var firstLayout = layoutRows.Count + 1;
            var declaredAlignment = structure.DeclaredAlignment is int alignment
                ? checked((uint)alignment) : 0u;
            var pack = checked((uint)structure.Packing);
            for (var variant = 0; variant < 2; variant++)
            {
                var layoutRow = layoutRows.Count + 1;
                var firstLayoutField = structure.Fields.IsEmpty
                    ? 0 : layoutFieldRows.Count + 1;
                layoutRows.Add(new TrRmLayoutRow(structureIndex + 1,
                    variant == 0 ? 0u : 1u, structure.LayoutKind,
                    variant == 0 ? (byte)0 : (byte)1,
                    declaredAlignment, pack, firstLayoutField,
                    structure.Fields.Length));
                for (var field = 0; field < structure.Fields.Length; field++)
                {
                    layoutFieldRows.Add(new TrRmLayoutFieldRow(layoutRow,
                        field, firstField + field));
                }
            }

            var visibilityFlags = structure.IsPrivate ? 0u
                : structure.IsInternal ? 1u : 2u;
            var typeFlags = visibilityFlags | 4u;
            typeDefRows.Add(new TrRmTypeDefRow(
                AddString(typeNamespace, 0), AddString(typeName, 0),
                typeFlags, firstField, structure.Fields.Length,
                0, 0, firstLayout, 2, 0, 0));
        }

        var typeSpecBytes = typeSpecRows.Count == 0 ? []
            : TrRmTypeSpecTableWriter.Encode(typeSpecRows,
                program.Structures.Length, 0, 0, 0, schema,
                methodDefCount: program.Functions.Length);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, program.Structures.Length, 0, 0, 0, schema,
            program.Functions.Length);
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows(stringsRows);
        var strings = new TrRmStringTableReader(stringsBytes, stringsRows.Count);

        var featureRows = new List<TrRmFeatureRow> {
            new(1, 1),
            new(2, 1),
            new(3, 1),
            TrRmFeatureRow.NativeOutputCellsCandidate,
            new(10, 1),
        };
        if (!import.Parameters.IsEmpty)
        {
            featureRows.Add(
                TrRmFeatureRow.ForeignInvocationContractsCandidate);
        }
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema);

        var methodRowByFunction = new int[program.Functions.Length];
        methodRowByFunction[targetIndex] = 1;
        methodRowByFunction[callerIndex] = 2;
        var targetMethodRow = methodRowByFunction[targetIndex];
        var callerMethodRow = methodRowByFunction[callerIndex];
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                0, program.EntryPoint < 0 ? 0 : callerMethodRow,
                1, 1, 1, 1, 1, featureRows.Count),
        ], strings, 0, program.Structures.Length,
            program.Functions.Length, 0,
            featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, program.Structures.Length, program.Functions.Length, 0,
            featureRows.Count);

        var parameterRows = new List<TrRmParamRow>(
            target.ParameterCount + caller.ParameterCount);
        for (var formal = 0; formal < target.ParameterCount; formal++)
        {
            parameterRows.Add(new TrRmParamRow(1, formal,
                targetParameterTypeSpecRefs[formal], 0));
        }

        var callerParameterFirst = caller.ParameterCount == 0
            ? 0 : parameterRows.Count + 1;
        for (var formal = 0; formal < caller.ParameterCount; formal++)
        {
            parameterRows.Add(new TrRmParamRow(2, formal,
                callerParameterTypeSpecRefs[formal], 0));
        }

        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(targetResultTypeSpecRef, 0,
                1, target.ParameterCount, 0, 0),
            new TrRmMethodSigRow(callerResultTypeSpecRef, 0,
                callerParameterFirst, caller.ParameterCount, 0, 0),
        ], typeSpecs, parameterRows.Count, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 2,
            typeSpecs, parameterRows.Count, 0);
        var parameterBytes = TrRmParamTableWriter.Encode(parameterRows,
            signatures, typeSpecs, schema);
        var parameters = new TrRmParamTableReader(parameterBytes,
            parameterRows.Count, signatures, typeSpecs);

        var contractRows = new List<TrRmContractRow>();
        var contractOwner = 0x1600_0000u | checked((uint)targetMethodRow);
        contractRows.Add(new TrRmContractRow(contractOwner, 1,
            new byte[] { 4, 0, 0, 0 }));
        foreach (var nativeParameter in import.Parameters
            .OrderBy(static parameter => parameter.ParameterIndex))
        {
            var nativeParameterPayload = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(nativeParameterPayload,
                checked((uint)nativeParameter.ParameterIndex));
            nativeParameterPayload[4]
                = checked((byte)((byte)nativeParameter.Access + 1));
            nativeParameterPayload[5] = 1;
            nativeParameterPayload[6]
                = nativeParameter.Extent == 0 ? (byte)0 : (byte)1;
            BinaryPrimitives.WriteUInt32LittleEndian(
                nativeParameterPayload.AsSpan(8), nativeParameter.Extent);
            contractRows.Add(new TrRmContractRow(contractOwner, 0x0E,
                nativeParameterPayload));
        }

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

            var predicatePayload = new byte[12];
            predicatePayload[0] = 3;
            BinaryPrimitives.WriteUInt32LittleEndian(
                predicatePayload.AsSpan(4), phaseOrdinal++);
            BinaryPrimitives.WriteUInt32LittleEndian(
                predicatePayload.AsSpan(8), checked((uint)graphRow));
            contractRows.Add(new TrRmContractRow(contractOwner, 0x0B,
                predicatePayload));
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
                var dependencyPayload = new byte[12];
                dependencyPayload[0] = dependency.DestinationKind;
                dependencyPayload[1] = (byte)dependency.SourceKind;
                dependencyPayload[2] = (byte)dependency.Relationship;
                dependencyPayload[3] = (byte)dependency.Phase;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    dependencyPayload.AsSpan(4),
                    checked((uint)dependency.DestinationOrdinal));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    dependencyPayload.AsSpan(8),
                    checked((uint)dependency.SourceOrdinal));
                contractRows.Add(new TrRmContractRow(contractOwner, 4,
                    dependencyPayload));
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

        var typeDefBytes = typeDefRows.Count == 0 ? []
            : TrRmTypeDefTableWriter.Encode(typeDefRows, strings, modules,
                fieldRows.Count, 0, layoutRows.Count, contractRows.Count,
                schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            typeDefRows.Count, strings, modules, fieldRows.Count, 0,
            layoutRows.Count, contractRows.Count, schema);
        var fieldBytes = fieldRows.Count == 0 ? []
            : TrRmFieldDefTableWriter.Encode(fieldRows, strings, typeDefs,
                typeSpecs);
        var fields = fieldRows.Count == 0 ? default
            : new TrRmFieldDefTableReader(fieldBytes, fieldRows.Count,
                strings, typeDefs, typeSpecs);
        var targetProfileBytes = typeDefRows.Count == 0 ? []
            : TrRmTargetProfileTableWriter.Encode([
                new TrRmTargetProfileRow(
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader
                        .WindowsX64NativeProfileRuleRevision),
            ], schema);
        var targetProfiles = typeDefRows.Count == 0
            ? new TrRmTargetProfileTableReader([], 0, schema)
            : new TrRmTargetProfileTableReader(targetProfileBytes, 1, schema);
        var layoutBytes = layoutRows.Count == 0 ? []
            : TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
                targetProfiles, layoutFieldRows.Count);
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutRows.Count,
            typeDefs, targetProfiles, layoutFieldRows.Count);
        var layoutFieldBytes = layoutFieldRows.Count == 0 ? []
            : TrRmLayoutFieldTableWriter.Encode(layoutFieldRows, layouts,
                typeDefs, fields);
        var layoutFields = layoutFieldRows.Count == 0 ? default
            : new TrRmLayoutFieldTableReader(layoutFieldBytes,
                layoutFieldRows.Count, layouts, typeDefs, fields);
        var methodRows = new TrRmMethodDefRow[program.Functions.Length];
        methodRows[targetIndex] = new TrRmMethodDefRow(0, 3, 1,
            Visibility(target), 1, contractRows.Count, 0);
        methodRows[callerIndex] = new TrRmMethodDefRow(0, 4, 2,
            Visibility(caller), 0, 0, 1);
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows, strings,
            modules, typeDefs, signatures, contractRows.Count, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodRows.Length, strings, modules, typeDefs, signatures,
            contractRows.Count, 1);

        var contractBytes = TrRmContractTableWriter.EncodeForeignInvocationDraft(
            contractRows, typeDefs, methods, signatures, parameters, typeSpecs,
            features, modules, predicateGraphs.Count, schema);
        var contracts = new TrRmContractTableReader(contractBytes,
            contractRows.Count, typeDefs, methods, signatures, parameters,
            typeSpecs, 0, features, modules, schema, predicateGraphs.Count);

        var nativeImportBytes = TrRmNativeImportTableWriter.Encode([
            new TrRmNativeImportRow(1, (byte)import.ResolutionMode,
                (byte)import.ResolutionPolicy,
                (byte)import.CallingConvention, targetMethodRow, 5, 6),
        ], strings, methods, fields, typeDefs, default, modules,
            signatures, parameters, typeSpecs, contracts, layouts);

        var callerParameterTypeMapTokens = callerParameterTypeSpecRefs
            .Select(CreateStandardBodyTypeMapToken).ToArray();
        var backingLocalTypeMapTokens = backingLocals.Select(local =>
            CreateStandardBodyTypeMapToken(GetTypeSpecRef(
                caller.Variables[local.Variable].Type))).ToArray();
        var nativeParameterFormals = nativeParameterAddressByFormal.Keys
            .Order()
            .ToArray();
        var nativeParameterTypeMapTokens = nativeParameterFormals.Select(
            formal => CreateStandardBodyTypeMapToken(
                targetParameterTypeSpecRefs[formal])).ToArray();
        var bodyTypeMapTokens = outputTypeMapTokens
            .Concat(callerParameterTypeMapTokens)
            .Concat(backingLocalTypeMapTokens)
            .Concat(nativeParameterTypeMapTokens);
        var maps = TrRmRegisterBodyMapBuilder.Create(
            bodyTypeMapTokens,
            [0x1600_0000u | checked((uint)targetMethodRow)]);
        var parameterHomeStart = outputVariables.Length;
        var backingHomeStart = parameterHomeStart + caller.ParameterCount;
        var storageHomeStart = backingHomeStart + backingLocals.Length;
        var homeMap = new byte[checked((storageHomeStart
            + nativeParameterFormals.Length) * 8)];
        var homeByVariable = new Dictionary<int, ushort>();
        var storageHomeByFormal = new Dictionary<int, ushort>(
            nativeParameterFormals.Length);
        for (var formal = 0; formal < outputVariables.Length; formal++)
        {
            var variable = outputVariables[formal];
            var home = checked((ushort)formal);
            homeByVariable.Add(variable, home);
            var offset = formal * 8;
            homeMap[offset] = 3;
            homeMap[offset + 1] = caller.Variables[variable].IsMutable
                ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(offset + 2),
                maps.GetTypeMapIndex(outputTypeMapTokens[formal]));
        }

        for (var formal = 0; formal < caller.ParameterCount; formal++)
        {
            var home = checked((ushort)(parameterHomeStart + formal));
            homeByVariable.Add(formal, home);
            var offset = home * 8;
            homeMap[offset] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(offset + 2),
                maps.GetTypeMapIndex(callerParameterTypeMapTokens[formal]));
            BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(offset + 4),
                checked((ushort)formal));
        }

        for (var index = 0; index < backingLocals.Length; index++)
        {
            var local = backingLocals[index];
            var home = checked((ushort)(backingHomeStart + index));
            homeByVariable.Add(local.Variable, home);
            var offset = home * 8;
            homeMap[offset] = 3;
            homeMap[offset + 1]
                = caller.Variables[local.Variable].IsMutable
                    ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(offset + 2),
                maps.GetTypeMapIndex(backingLocalTypeMapTokens[index]));
        }

        for (var index = 0; index < nativeParameterFormals.Length; index++)
        {
            var formal = nativeParameterFormals[index];
            var home = checked((ushort)(storageHomeStart + index));
            storageHomeByFormal.Add(formal, home);
            var offset = home * 8;
            homeMap[offset] = 3;
            BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(offset + 2),
                maps.GetTypeMapIndex(nativeParameterTypeMapTokens[index]));
        }

        var callInputs = new TrRmRegisterBodyCallInput[target.ParameterCount];
        for (var formal = 0; formal < callInputs.Length; formal++)
        {
            if (outputVariableByFormal.TryGetValue(formal,
                    out var outputVariable))
            {
                callInputs[formal] = new TrRmRegisterBodyCallInput(2, 6,
                    homeByVariable[outputVariable]);
            }
            else if (storageHomeByFormal.TryGetValue(formal,
                    out var storageHome))
            {
                callInputs[formal] = new TrRmRegisterBodyCallInput(2, 1,
                    storageHome);
            }
            else if (selectedCall.Arguments[formal] is BoundRead read
                && homeByVariable.TryGetValue(read.Variable, out var inputHome))
            {
                callInputs[formal] = new TrRmRegisterBodyCallInput(2, 1,
                    inputHome);
            }
            else
            {
                throw Unsupported(
                    "A direct NativeOutput input has no completed caller Home.");
            }
        }

        const ushort callSiteRecordIndex = 1;
        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 1,
            maps.GetMethodMapIndex(0x1600_0000u
                | checked((uint)targetMethodRow)), 0, callInputs, [],
            schema: schema);
        var records = new List<ReadOnlyMemory<byte>>(2
            + outputVariables.Length + nativeParameterFormals.Length) {
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
            callSite,
        };
        var code = new List<byte>(checked((backingLocals.Length * 8)
            + ((outputVariables.Length + nativeParameterFormals.Length) * 4)
            + 6));
        foreach (var local in backingLocals)
        {
            if (local.Initializer is not BoundRead source)
            {
                throw Unsupported(
                    "A direct NativeOutput local has no caller-formal source.");
            }

            var sourceHome = homeByVariable[source.Variable];
            var destinationHome = homeByVariable[local.Variable];
            code.Add(0xD5);
            code.Add(0);
            code.Add((byte)sourceHome);
            code.Add((byte)(sourceHome >> 8));
            code.Add(0xD6);
            code.Add(0x10);
            code.Add((byte)destinationHome);
            code.Add((byte)(destinationHome >> 8));
        }

        for (var formal = 0; formal < target.ParameterCount; formal++)
        {
            if (nativeParameterAddressByFormal.TryGetValue(formal,
                    out var address))
            {
                var storageRecordIndex = checked((ushort)records.Count);
                var pointerTypeMapIndex = maps.GetTypeMapIndex(
                    CreateStandardBodyTypeMapToken(
                        targetParameterTypeSpecRefs[formal]));
                records.Add(TrRmRegisterBodyRecordWriter.EncodeStorageAddress(
                    2, 2, homeByVariable[address.Variable],
                    storageHomeByFormal[formal], pointerTypeMapIndex,
                    checked((uint)address.ElementCount)));
                code.Add(0xEC);
                code.Add(0);
                code.Add((byte)storageRecordIndex);
                code.Add((byte)(storageRecordIndex >> 8));
            }

            if (outputVariableByFormal.TryGetValue(formal,
                    out var outputVariable))
            {
                var reservationRecordIndex = checked((ushort)records.Count);
                records.Add(TrRmRegisterBodyRecordWriter
                    .EncodeNativeOutputReservation(callSiteRecordIndex,
                        homeByVariable[outputVariable],
                        checked((uint)formal)));
                code.Add(0xDF);
                code.Add(0);
                code.Add((byte)reservationRecordIndex);
                code.Add((byte)(reservationRecordIndex >> 8));
            }
        }

        AppendCallSiteInstruction(code, callSiteRecordIndex);
        code.Add(caller.ReturnType == target.ReturnType ? (byte)0x17 : (byte)0x18);
        code.Add(0);
        var bodyCode = code.ToArray();
        var body = TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, default, [
                new TrRmRegisterBodyBlock(0, checked((uint)bodyCode.Length), 0),
            ], records, bodyCode, contractRows.Count, schema);

        var predicateGraphBytes = TrRmPredicateGraphTableWriter.Encode(
            predicateGraphs, predicateNodes.Count, predicateFootprints.Count);
        var predicateFootprintBytes
            = TrRmPredicateReadFootprintTableWriter.Encode(
                predicateFootprints, predicateRoots.Count, 0);
        var predicateRootBytes = TrRmPredicateReadRootTableWriter.Encode(
            predicateRoots, 0, 0, schema);
        var predicateNodeBytes = TrRmPredicateNodeTableWriter.Encode(
            predicateNodes, predicateOperands.Count, typeSpecs, schema);
        var predicateOperandCounts = new TrRmPredicateTableRowCounts(
            predicateRoots.Count, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var predicateOperandBytes
            = TrRmPredicateNodeOperandTableWriter.Encode(predicateOperands,
                predicateOperandCounts, schema);

        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, checked((uint)stringsRows.Count), stringsBytes),
            new(0x0004, 0, checked((uint)featureRows.Count), featureBytes),
            new(0x0011, 0, checked((uint)typeSpecRows.Count), typeSpecBytes),
            new(0x0016, 0, checked((uint)methodRows.Length), methodBytes),
            new(0x0017, 0, 2, signatureBytes),
            new(0x0018, 0, checked((uint)parameterRows.Count), parameterBytes),
            new(0x001B, 0, checked((uint)contractRows.Count), contractBytes),
            new(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([body])),
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
        if (typeDefRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0013, 0,
                checked((uint)typeDefRows.Count), typeDefBytes));
            sections.Add(new TrRmSection(0x0014, 0,
                checked((uint)fieldRows.Count), fieldBytes));
            sections.Add(new TrRmSection(0x0015, 0,
                checked((uint)layoutRows.Count), layoutBytes));
            sections.Add(new TrRmSection(0x0023, 0,
                checked((uint)layoutFieldRows.Count), layoutFieldBytes));
            sections.Add(new TrRmSection(0x0048, 0, 1,
                targetProfileBytes));
        }
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections, schema);
        var artifactIndex = schema
            == TrRmArtifactSchema.ForeignInvocationDraft
                ? TrRmArtifactIndex.ForForeignInvocationDraft(artifact)
                : TrRmArtifactIndex
                    .ForForeignInvocationStorageAddressDraft(artifact);
        var tables = schema
            == TrRmArtifactSchema.ForeignInvocationDraft
                ? TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationDraftAndGetExecutionTables(
                        artifactIndex)
                : TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                        artifactIndex);
        tables.ValidateRegisterBodyMapsForMethod(callerMethodRow);
        return artifact;

        static uint Visibility(BoundFunction function)
        {
            return function.IsPrivate ? 0u
                : function.IsInternal ? 1u : 2u;
        }
    }

    private static bool SemanticVerifierIsValidNativeOutputTarget(
        BoundFunction target, BoundProgram program)
        => target.DirectNativeImport is not null
            && DirectNativeImportContract.IsValid(target,
                program.Structures, program.NativeFunctionPointerSignatures,
                program.NativePointerTypes)
            && NativeOutputContractValidator.IsValid(target,
                program.Structures, program.NativePointerTypes,
                program.NativeFunctionPointerSignatures);

    private static ImmutableArray<BoundLocal> ValidateDirectNativeOutputCaller(
        BoundFunction caller, BoundCall call,
        int[] outputVariables)
    {
        if (caller.Body is not BoundBlock body)
        {
            throw Unsupported(
                "The direct NativeOutput caller requires a block Body.");
        }

        var declaredVacant = new HashSet<int>();
        var backingLocalByVariable = new HashSet<int>();
        var backingLocals = ImmutableArray.CreateBuilder<BoundLocal>();
        var unsafeCallCount = 0;
        var unsafeReturnCallCount = 0;
        var outerUnitReturnCount = 0;
        foreach (var statement in body.Statements)
        {
            switch (statement)
            {
                case BoundVacantLocal vacant:
                    if (!declaredVacant.Add(vacant.Variable))
                    {
                        throw Unsupported(
                            "The direct NativeOutput caller declares a vacant local twice.");
                    }
                    break;

                case BoundLocal local when local.Initializer is BoundRead read:
                    if ((uint)local.Variable >= (uint)caller.Variables.Length
                        || local.Variable < caller.ParameterCount
                        || caller.Variables[local.Variable].IsInitializationLocal
                        || outputVariables.Contains(local.Variable)
                        || !backingLocalByVariable.Add(local.Variable)
                        || read.Variable < 0
                        || read.Variable >= caller.ParameterCount
                        || read.Type != caller.Variables[local.Variable].Type)
                    {
                        throw Unsupported(
                            "A direct NativeOutput local must copy one caller formal.");
                    }

                    backingLocals.Add(local);
                    break;

                case BoundUnsafe { Body: BoundBlock unsafeBody }:
                    foreach (var nested in unsafeBody.Statements)
                    {
                        if (nested is BoundExpressionStatement {
                            Expression: BoundCall nestedCall,
                        } && ReferenceEquals(nestedCall, call))
                        {
                            unsafeCallCount++;
                        }
                        else if (nested is BoundReturn {
                            Value: BoundCall returnedCall,
                        } && ReferenceEquals(returnedCall, call)
                            && caller.ReturnType == call.Type)
                        {
                            unsafeReturnCallCount++;
                        }
                        else if (nested is not BoundReturn { Value: null })
                        {
                            throw Unsupported(
                                "The direct NativeOutput unsafe block contains an unsupported operation.");
                        }
                    }
                    break;

                case BoundReturn { Value: null }:
                    outerUnitReturnCount++;
                    break;

                default:
                    throw Unsupported(
                        "The direct NativeOutput caller contains an unsupported statement.");
            }
        }

        var returnsCallResult = caller.ReturnType == call.Type;
        if ((returnsCallResult
                ? unsafeReturnCallCount != 1 || unsafeCallCount != 0
                : unsafeCallCount != 1 || unsafeReturnCallCount != 0)
            || declaredVacant.Count != outputVariables.Length
            || outputVariables.Any(variable => !declaredVacant.Contains(variable))
            || caller.Variables.Length
                != caller.ParameterCount + outputVariables.Length
                    + backingLocals.Count
            || (returnsCallResult && outerUnitReturnCount != 0)
            || (!returnsCallResult && outerUnitReturnCount > 1))
        {
            throw Unsupported(
                "The direct NativeOutput caller must reserve each vacant local exactly once.");
        }

        return backingLocals.ToImmutable();
    }
}
