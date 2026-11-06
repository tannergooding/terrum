// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
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
                    }
                }))
            {
                throw Unsupported(
                    "A function Body cannot be traversed for NativeOutput calls.");
            }
        }

        if (targetIndex != 0 || callerIndex != 1 || outputCallCount == 0
            || (program.EntryPoint >= 0 && program.EntryPoint != callerIndex))
        {
            throw Unsupported(
                "The direct NativeOutput Body requires sequential direct import calls.");
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
                && !import.Parameters.IsEmpty))
        {
            throw Unsupported(
                "The direct NativeOutput call has unsupported declaration or argument facts.");
        }

        var outputCalls = CollectDirectNativeOutputCalls(caller);
        if (outputCalls.Count != outputCallCount
            || outputCalls.Any(call => call.Function != targetIndex))
        {
            throw Unsupported(
                "The direct NativeOutput Body calls are not a supported sequential call sequence.");
        }

        var outputHomeCount = (long)outputCalls.Count
            * target.NativeOutputContracts.Length;
        var nativeParameterHomeCount = 0L;
        foreach (var call in outputCalls)
        {
            foreach (var parameter in import.Parameters)
            {
                if ((uint)parameter.ParameterIndex
                        < (uint)call.Arguments.Length
                    && call.Arguments[parameter.ParameterIndex]
                        is BoundNativeStorageAddress)
                {
                    nativeParameterHomeCount++;
                }
            }
        }
        var conditionalCount = 0;
        if (!BoundTreeTraversal.VisitStatements(caller.Body, statement =>
            {
                if (statement is BoundIf)
                {
                    conditionalCount++;
                }
            }))
        {
            throw Unsupported(
                "The direct NativeOutput Body cannot be traversed for conditional statements.");
        }

        var minimumHomeCount = caller.ParameterCount + outputHomeCount
            + nativeParameterHomeCount;
        var expectedRecordCount = 1L + outputCalls.Count
            + outputHomeCount + nativeParameterHomeCount + conditionalCount;
        var expectedBlockCount = 1L + (3L * conditionalCount);
        if (minimumHomeCount > 65_536 || expectedRecordCount > 65_536
            || expectedBlockCount > 65_536)
        {
            throw Unsupported(
                "The direct NativeOutput Body exceeds the selected Home, record or Block index range.");
        }

        var outputVariablesByCall = new int[outputCalls.Count][];
        var outputVariableByFormalByCall
            = new Dictionary<int, int>[outputCalls.Count];
        var seenOutputVariables = new HashSet<int>();
        for (var callIndex = 0; callIndex < outputCalls.Count; callIndex++)
        {
            var selectedCall = outputCalls[callIndex];
            if (selectedCall.Arguments.Length != target.ParameterCount
                || selectedCall.Type != target.ReturnType
                || (!selectedCall.ArgumentParameterIndices.IsDefaultOrEmpty
                    && selectedCall.ArgumentParameterIndices.Length
                        != selectedCall.Arguments.Length))
            {
                throw Unsupported(
                    "A direct NativeOutput call has unsupported argument facts.");
            }

            var callOutputVariables
                = new int[target.NativeOutputContracts.Length];
            var outputVariableByFormal = new Dictionary<int, int>(
                callOutputVariables.Length);
            for (var outputIndex = 0;
                outputIndex < callOutputVariables.Length;
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
                    || (uint)reservation.Variable
                        >= (uint)caller.Variables.Length
                    || !caller.Variables[reservation.Variable]
                        .IsInitializationLocal)
                {
                    throw Unsupported(
                        "Each direct NativeOutput call requires distinct vacant locals for its output formals.");
                }

                if (!outputVariableByFormal.TryAdd(formal,
                        reservation.Variable)
                    || (!selectedCall.ArgumentParameterIndices.IsDefaultOrEmpty
                        && selectedCall.ArgumentParameterIndices[formal]
                            != formal))
                {
                    throw Unsupported(
                        "The direct NativeOutput Body requires distinct formal-order output arguments.");
                }

                callOutputVariables[outputIndex] = reservation.Variable;
            }

            outputVariablesByCall[callIndex] = callOutputVariables;
            outputVariableByFormalByCall[callIndex]
                = outputVariableByFormal;
        }

        var outputVariables = outputVariablesByCall
            .SelectMany(static variables => variables).ToArray();
        var backingLocals = ValidateDirectNativeOutputCaller(caller,
            outputCalls, seenOutputVariables);
        if (minimumHomeCount + backingLocals.Length > 65_536)
        {
            throw Unsupported(
                "The direct NativeOutput Body exceeds the selected Home index range.");
        }

        var backingLocalByVariable = backingLocals.ToDictionary(
            static local => local.Variable);
        var nativeParameterByFormal = import.Parameters.ToDictionary(
            static parameter => parameter.ParameterIndex);
        var nativeParameterAddressByCallFormal
            = new Dictionary<(int CallIndex, int Formal),
                BoundNativeStorageAddress>(
                checked(import.Parameters.Length * outputCalls.Count));
        for (var callIndex = 0; callIndex < outputCalls.Count; callIndex++)
        {
            var selectedCall = outputCalls[callIndex];
            var outputVariableByFormal
                = outputVariableByFormalByCall[callIndex];
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
                        || !DirectNativeImportContract
                            .MatchesNativeParameterFormalType(
                                target.Variables[formal].Type,
                                nativeParameter.PointeeType,
                                nativeParameter.Access,
                                program.NativePointerTypes))
                    {
                        throw Unsupported(
                            "A direct NativeParameter Body requires its selected storage-address schema and matching pointer formal.");
                    }

                    if (selectedCall.Arguments[formal]
                        is BoundNativeStorageAddress address)
                    {
                        if (address.Variable < caller.ParameterCount
                            || !backingLocalByVariable.ContainsKey(
                                address.Variable)
                            || address.Type
                                != PrimitiveType.NativeStorageAddress
                            || address.ElementType
                                != nativeParameter.PointeeType
                            || address.CarrierType != address.ElementType
                            || address.ElementCount != 1
                            || nativeParameter.Extent > address.ElementCount
                            || address.ElementStride == 0
                            || address.ElementStride != address.ByteExtent
                            || address.Index is not BoundConstant index
                            || index.Type != PrimitiveType.Int32
                            || index.Bits.Low != 0 || index.Bits.High != 0
                            || address.IsReadonly
                                != (nativeParameter.Access
                                    == NativeParameterAccess.Read)
                            || caller.Variables[address.Variable].Type
                                != nativeParameter.PointeeType
                            || ((nativeParameter.Access
                                    is NativeParameterAccess.Write
                                        or NativeParameterAccess.ReadWrite)
                                && !caller.Variables[address.Variable]
                                    .IsMutable))
                        {
                            throw Unsupported(
                                "A direct NativeParameter storage address requires sufficient stable backing from an initialized local.");
                        }

                        _ = nativeParameterAddressByCallFormal.TryAdd(
                            (callIndex, formal), address);
                    }
                    else if (selectedCall.Arguments[formal] is not BoundRead read
                        || read.Variable < 0
                        || (read.Variable >= caller.ParameterCount
                            && !backingLocalByVariable.ContainsKey(
                                read.Variable))
                        || read.Type != target.Variables[formal].Type
                        || read.Type != caller.Variables[read.Variable].Type)
                    {
                        throw Unsupported(
                            "A direct NativeParameter input must read an opaque pointer formal or its local copy.");
                    }
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
        for (var index = 0; index < outputTypeMapTokens.Length; index++)
        {
            var outputIndex = index
                % target.NativeOutputContracts.Length;
            var type = target.NativeOutputContracts[outputIndex].PointeeType;
            var typeSpecRef = GetTypeSpecRef(type);
            outputTypeMapTokens[index] = (typeSpecRef >> 24) is 0xF2 or 0xF3
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
        var nativeParameterFormals = import.Parameters
            .Select(static parameter => parameter.ParameterIndex)
            .Order().ToArray();
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
        var storageHomeCount
            = nativeParameterAddressByCallFormal.Count;
        var homeMap = new byte[checked((storageHomeStart
            + storageHomeCount) * 8)];
        var homeByVariable = new Dictionary<int, ushort>();
        var storageHomeByCallFormal
            = new Dictionary<(int CallIndex, int Formal), ushort>(
                storageHomeCount);
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

        var storageHomeOrdinal = 0;
        for (var callIndex = 0; callIndex < outputCalls.Count; callIndex++)
        {
            for (var formal = 0; formal < target.ParameterCount; formal++)
            {
                if (!nativeParameterAddressByCallFormal.ContainsKey(
                        (callIndex, formal)))
                {
                    continue;
                }

                var home = checked((ushort)(storageHomeStart
                    + storageHomeOrdinal++));
                storageHomeByCallFormal.Add((callIndex, formal), home);
                var offset = home * 8;
                homeMap[offset] = 3;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    homeMap.AsSpan(offset + 2),
                    maps.GetTypeMapIndex(CreateStandardBodyTypeMapToken(
                        targetParameterTypeSpecRefs[formal])));
            }
        }

        var records = new List<ReadOnlyMemory<byte>>(
            checked(1 + outputCalls.Count
                + (outputVariables.Length + storageHomeCount))) {
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        };
        var callSiteRecordIndices = new ushort[outputCalls.Count];
        for (var callIndex = 0; callIndex < outputCalls.Count; callIndex++)
        {
            var selectedCall = outputCalls[callIndex];
            var callInputs
                = new TrRmRegisterBodyCallInput[target.ParameterCount];
            var outputVariableByFormal
                = outputVariableByFormalByCall[callIndex];
            for (var formal = 0; formal < callInputs.Length; formal++)
            {
                if (outputVariableByFormal.TryGetValue(formal,
                        out var outputVariable))
                {
                    callInputs[formal] = new TrRmRegisterBodyCallInput(2, 6,
                        homeByVariable[outputVariable]);
                }
                else if (storageHomeByCallFormal.TryGetValue(
                        (callIndex, formal), out var storageHome))
                {
                    callInputs[formal] = new TrRmRegisterBodyCallInput(2, 1,
                        storageHome);
                }
                else if (selectedCall.Arguments[formal] is BoundRead read
                    && homeByVariable.TryGetValue(read.Variable,
                        out var inputHome))
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

            callSiteRecordIndices[callIndex]
                = checked((ushort)records.Count);
            records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 1,
                maps.GetMethodMapIndex(0x1600_0000u
                    | checked((uint)targetMethodRow)), 0, callInputs, [],
                schema: schema));
        }

        var storageRecordByCallFormal
            = new Dictionary<(int CallIndex, int Formal), ushort>(
                storageHomeCount);
        var reservationRecordByCallFormal
            = new Dictionary<(int CallIndex, int Formal), ushort>(
                outputVariables.Length);
        for (var callIndex = 0; callIndex < outputCalls.Count; callIndex++)
        {
            var outputVariableByFormal
                = outputVariableByFormalByCall[callIndex];
            for (var formal = 0; formal < target.ParameterCount; formal++)
            {
                if (nativeParameterAddressByCallFormal.TryGetValue(
                        (callIndex, formal), out var address))
                {
                    var storageRecordIndex = checked((ushort)records.Count);
                    var pointerTypeMapIndex = maps.GetTypeMapIndex(
                        CreateStandardBodyTypeMapToken(
                            targetParameterTypeSpecRefs[formal]));
                    records.Add(
                        TrRmRegisterBodyRecordWriter.EncodeStorageAddress(
                            2, 2, homeByVariable[address.Variable],
                            storageHomeByCallFormal[(callIndex, formal)],
                            pointerTypeMapIndex,
                            checked((uint)address.ElementCount)));
                    storageRecordByCallFormal.Add((callIndex, formal),
                        storageRecordIndex);
                }

                if (outputVariableByFormal.TryGetValue(formal,
                        out var outputVariable))
                {
                    var reservationRecordIndex = checked((ushort)records.Count);
                    records.Add(TrRmRegisterBodyRecordWriter
                        .EncodeNativeOutputReservation(
                            callSiteRecordIndices[callIndex],
                            homeByVariable[outputVariable],
                            checked((uint)formal)));
                    reservationRecordByCallFormal.Add((callIndex, formal),
                        reservationRecordIndex);
                }
            }
        }

        var codeBlocks = new List<List<byte>> {
            new(checked((backingLocals.Length * 8)
                + checked((outputVariables.Length + storageHomeCount) * 4)
                + checked(outputCalls.Count * 6) + 2)),
        };
        var currentCode = codeBlocks[0];
        foreach (var local in backingLocals)
        {
            if (local.Initializer is not BoundRead source)
            {
                throw Unsupported(
                    "A direct NativeOutput local has no caller-formal source.");
            }

            var sourceHome = homeByVariable[source.Variable];
            var destinationHome = homeByVariable[local.Variable];
            currentCode.Add(0xD5);
            currentCode.Add(0);
            currentCode.Add((byte)sourceHome);
            currentCode.Add((byte)(sourceHome >> 8));
            currentCode.Add(0xD6);
            currentCode.Add(0x10);
            currentCode.Add((byte)destinationHome);
            currentCode.Add((byte)(destinationHome >> 8));
        }

        var callIndexByExpression = new Dictionary<BoundCall, int>(
            ReferenceEqualityComparer.Instance);
        for (var callIndex = 0; callIndex < outputCalls.Count; callIndex++)
        {
            callIndexByExpression.Add(outputCalls[callIndex], callIndex);
        }

        int CreateBlock()
        {
            if (codeBlocks.Count >= 65_536)
            {
                throw Unsupported(
                    "The direct NativeOutput Body exceeds the Block index range.");
            }

            var block = codeBlocks.Count;
            codeBlocks.Add([]);
            return block;
        }

        void SelectBlock(int blockIndex)
        {
            if ((uint)blockIndex >= (uint)codeBlocks.Count)
            {
                throw Unsupported(
                    "A direct NativeOutput branch targets an unknown Block.");
            }

            currentCode = codeBlocks[blockIndex];
        }

        void AppendBranch(int targetBlock)
        {
            currentCode.Add(0xE3);
            currentCode.Add(0);
            currentCode.Add((byte)targetBlock);
            currentCode.Add(checked((byte)(targetBlock >> 8)));
        }

        void AppendConditionalFlow(BoundExpression condition,
            int trueBlock, int falseBlock)
        {
            while (condition is BoundUnsafeExpression unsafeExpression)
            {
                condition = unsafeExpression.Expression;
            }

            if (condition is BoundRead read
                && read.Type == PrimitiveType.Bool
                && homeByVariable.TryGetValue(read.Variable,
                    out var conditionHome))
            {
                AppendLoadHome(0, conditionHome);
            }
            else if (condition is BoundBinary binary
                && binary.Type == PrimitiveType.Bool
                && binary.Operator is TokenKind.BangEquals
                    or TokenKind.EqualsEquals
                    or TokenKind.Less
                    or TokenKind.LessEquals
                    or TokenKind.Greater
                    or TokenKind.GreaterEquals
                && binary.Right is BoundConstant zero
                && zero.Type == binary.Left.Type
                && zero.Bits.Low == 0 && zero.Bits.High == 0
                && PrimitiveTypeFacts.IsInteger(binary.Left.Type))
            {
                var left = binary.Left;
                while (left is BoundUnsafeExpression leftUnsafe)
                {
                    left = leftUnsafe.Expression;
                }

                if (left is BoundCall call)
                {
                    EmitCall(call);
                }
                else if (left is BoundRead valueRead
                    && homeByVariable.TryGetValue(valueRead.Variable,
                        out var valueHome))
                {
                    AppendLoadHome(0, valueHome);
                }
                else
                {
                    throw Unsupported(
                        "A direct NativeOutput comparison has no call-result or Home input.");
                }

                currentCode.Add(0xC2);
                currentCode.Add(0);
                const ushort comparePayload
                    = (ushort)((15 << 4) | (14 << 12));
                currentCode.Add(unchecked((byte)comparePayload));
                currentCode.Add((byte)(comparePayload >> 8));
                currentCode.Add(0xC3);
                currentCode.Add(binary.Operator switch {
                    TokenKind.Less => 1,
                    TokenKind.EqualsEquals => 2,
                    TokenKind.LessEquals => 3,
                    TokenKind.Greater => 4,
                    TokenKind.GreaterEquals => 6,
                    TokenKind.BangEquals => 13,
                    _ => throw Unsupported(
                        "A direct NativeOutput comparison has no selected predicate."),
                });
                currentCode.Add(0);
                currentCode.Add(0xFF);
            }
            else
            {
                throw Unsupported(
                    "A direct NativeOutput conditional requires a Boolean Home or a selected zero comparison.");
            }

            var edgeRecordIndex = checked((ushort)records.Count);
            records.Add(TrRmRegisterBodyRecordWriter.EncodeConditionalEdge(
                checked((ushort)trueBlock), checked((ushort)falseBlock)));
            currentCode.Add(0xE4);
            currentCode.Add(0);
            currentCode.Add((byte)edgeRecordIndex);
            currentCode.Add((byte)(edgeRecordIndex >> 8));
        }

        bool AlwaysTerminates(BoundStatement statement)
        {
            return statement switch {
                BoundReturn => true,
                BoundBlock block => block.Statements.Any(AlwaysTerminates),
                BoundUnsafe unsafeBlock => AlwaysTerminates(
                    unsafeBlock.Body),
                BoundIf branch => branch.Else is not null
                    && AlwaysTerminates(branch.Then)
                    && AlwaysTerminates(branch.Else),
                _ => false,
            };
        }

        void AppendLoadHome(int register, int home)
        {
            if ((uint)register >= 14u)
            {
                throw Unsupported(
                    "A direct NativeOutput Home load requires a general register.");
            }

            currentCode.Add(0xD5);
            currentCode.Add(checked((byte)register));
            currentCode.Add(unchecked((byte)home));
            currentCode.Add(checked((byte)(home >> 8)));
        }

        void EmitCall(BoundCall call)
        {
            if (!callIndexByExpression.TryGetValue(call,
                    out var callIndex))
            {
                throw Unsupported(
                    "A direct NativeOutput call has no selected CallSite.");
            }

            for (var formal = 0; formal < target.ParameterCount; formal++)
            {
                if (storageRecordByCallFormal.TryGetValue(
                        (callIndex, formal), out var storageRecordIndex))
                {
                    currentCode.Add(0xEC);
                    currentCode.Add(0);
                    currentCode.Add((byte)storageRecordIndex);
                    currentCode.Add((byte)(storageRecordIndex >> 8));
                }

                if (reservationRecordByCallFormal.TryGetValue(
                        (callIndex, formal),
                        out var reservationRecordIndex))
                {
                    currentCode.Add(0xDF);
                    currentCode.Add(0);
                    currentCode.Add((byte)reservationRecordIndex);
                    currentCode.Add((byte)(reservationRecordIndex >> 8));
                }
            }

            AppendCallSiteInstruction(currentCode,
                callSiteRecordIndices[callIndex]);
        }

        bool EmitStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlock block:
                    for (var index = 0; index < block.Statements.Length;
                        index++)
                    {
                        if (EmitStatement(block.Statements[index]))
                        {
                            if (index + 1 < block.Statements.Length)
                            {
                                throw Unsupported(
                                    "A direct NativeOutput Body has statements after a terminating branch.");
                            }

                            return true;
                        }
                    }

                    return false;

                case BoundVacantLocal:
                case BoundLocal:
                    return false;

                case BoundUnsafe unsafeBlock:
                    return EmitStatement(unsafeBlock.Body);

                case BoundIf branch:
                {
                    var thenTerminates = AlwaysTerminates(branch.Then);
                    var elseTerminates = branch.Else is not null
                        && AlwaysTerminates(branch.Else);
                    var thenBlock = CreateBlock();
                    var elseBlock = CreateBlock();
                    var joinBlock = thenTerminates && elseTerminates
                        ? -1 : CreateBlock();
                    AppendConditionalFlow(branch.Condition, thenBlock,
                        elseBlock);

                    SelectBlock(thenBlock);
                    var thenReturned = EmitStatement(branch.Then);
                    if (thenReturned != thenTerminates)
                    {
                        throw Unsupported(
                            "Direct NativeOutput then-branch termination differs from its bound shape.");
                    }
                    if (!thenReturned)
                    {
                        AppendBranch(joinBlock);
                    }

                    SelectBlock(elseBlock);
                    var elseReturned = branch.Else is not null
                        && EmitStatement(branch.Else);
                    if (elseReturned != elseTerminates)
                    {
                        throw Unsupported(
                            "Direct NativeOutput else-branch termination differs from its bound shape.");
                    }
                    if (!elseReturned)
                    {
                        AppendBranch(joinBlock);
                    }

                    if (joinBlock < 0)
                    {
                        return true;
                    }

                    SelectBlock(joinBlock);
                    return false;
                }

                case BoundExpressionStatement {
                    Expression: BoundCall call,
                }:
                    EmitCall(call);
                    return false;

                case BoundReturn { Value: BoundCall call }:
                    EmitCall(call);
                    currentCode.Add(0x17);
                    currentCode.Add(0);
                    return true;

                case BoundReturn { Value: null }:
                    currentCode.Add(0x18);
                    currentCode.Add(0);
                    return true;

                default:
                    throw Unsupported(
                        "A direct NativeOutput caller contains an unsupported control-flow statement.");
            }
        }

        if (caller.Body is not BoundBlock callerBody)
        {
            throw Unsupported(
                "The direct NativeOutput caller requires a block Body.");
        }

        var terminated = false;
        foreach (var statement in callerBody.Statements)
        {
            if (terminated)
            {
                throw Unsupported(
                    "A direct NativeOutput caller has statements after termination.");
            }

            terminated = EmitStatement(statement);
        }

        if (!terminated)
        {
            currentCode.Add(caller.ReturnType == PrimitiveType.Void
                ? (byte)0x18 : throw Unsupported(
                    "A direct NativeOutput caller has no returned call result."));
            currentCode.Add(0);
        }

        var blocks = new List<TrRmRegisterBodyBlock>(codeBlocks.Count);
        var bodyCode = new List<byte>();
        foreach (var blockCode in codeBlocks)
        {
            if (blockCode.Count == 0)
            {
                throw Unsupported(
                    "A direct NativeOutput CFG block is empty.");
            }

            blocks.Add(new TrRmRegisterBodyBlock(
                checked((uint)bodyCode.Count),
                checked((uint)blockCode.Count), 0));
            bodyCode.AddRange(blockCode);
        }

        var body = TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, default, blocks, records,
            bodyCode.ToArray(), contractRows.Count, schema);

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

    private static List<BoundCall> CollectDirectNativeOutputCalls(
        BoundFunction caller)
    {
        if (caller.Body is not BoundBlock body)
        {
            throw Unsupported(
                "The direct NativeOutput caller requires a block Body.");
        }

        var calls = new List<BoundCall>();
        void VisitExpression(BoundExpression expression)
        {
            if (expression is BoundCall call)
            {
                AddCall(call);
            }

            foreach (var nested in expression.NestedExpressions)
            {
                VisitExpression(nested);
            }
        }

        void VisitStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlock block:
                    foreach (var nested in block.Statements)
                    {
                        VisitStatement(nested);
                    }
                    return;

                case BoundUnsafe unsafeBlock:
                    VisitStatement(unsafeBlock.Body);
                    return;

                case BoundIf branch:
                    VisitExpression(branch.Condition);
                    VisitStatement(branch.Then);
                    if (branch.Else is not null)
                    {
                        VisitStatement(branch.Else);
                    }
                    return;

                case BoundExpressionStatement {
                    Expression: BoundCall expressionCall,
                }:
                    AddCall(expressionCall);
                    return;

                case BoundReturn { Value: BoundCall returnedCall }:
                    AddCall(returnedCall);
                    return;
            }
        }

        void AddCall(BoundCall call)
        {
            if (call.Arguments.Any(static argument =>
                        argument is BoundNativeOutputReservation))
            {
                calls.Add(call);
            }
        }

        foreach (var statement in body.Statements)
        {
            VisitStatement(statement);
        }

        return calls;
    }

    private static ImmutableArray<BoundLocal> ValidateDirectNativeOutputCaller(
        BoundFunction caller, List<BoundCall> calls,
        HashSet<int> outputVariables)
    {
        if (caller.Body is not BoundBlock body)
        {
            throw Unsupported(
                "The direct NativeOutput caller requires a block Body.");
        }

        var declaredVacant = new HashSet<int>();
        var backingLocalByVariable = new HashSet<int>();
        var backingLocals = ImmutableArray.CreateBuilder<BoundLocal>();
        var expressionCalls = new HashSet<BoundCall>(
            ReferenceEqualityComparer.Instance);
        var returnCalls = new HashSet<BoundCall>(
            ReferenceEqualityComparer.Instance);
        var expectedCalls = new HashSet<BoundCall>(calls,
            ReferenceEqualityComparer.Instance);
        if (expectedCalls.Count != calls.Count)
        {
            throw Unsupported(
                "The direct NativeOutput caller repeats a call expression.");
        }

        var outerUnitReturnCount = 0;
        var nextCallIndex = 0;
        void ValidateConditionalCondition(BoundExpression expression,
            bool insideUnsafe)
        {
            var conditionIsUnsafe = false;
            while (expression is BoundUnsafeExpression unsafeExpression)
            {
                conditionIsUnsafe = true;
                expression = unsafeExpression.Expression;
            }

            if (expression is BoundRead booleanRead
                && booleanRead.Type == PrimitiveType.Bool)
            {
                if ((uint)booleanRead.Variable
                        >= (uint)caller.Variables.Length
                    || caller.Variables[booleanRead.Variable].Type
                        != PrimitiveType.Bool
                    || (booleanRead.Variable >= caller.ParameterCount
                        && !backingLocalByVariable.Contains(
                            booleanRead.Variable)))
                {
                    throw Unsupported(
                        "A direct NativeOutput Body Boolean condition has no caller Home.");
                }

                return;
            }

            if (expression is not BoundBinary binary
                || binary.Type != PrimitiveType.Bool
                || binary.Operator is not (TokenKind.BangEquals
                    or TokenKind.EqualsEquals or TokenKind.GreaterEquals
                    or TokenKind.Less or TokenKind.LessEquals
                    or TokenKind.Greater))
            {
                throw Unsupported(
                    "A direct NativeOutput Body condition must be a Boolean Home read or an admitted zero comparison.");
            }

            var left = binary.Left;
            while (left is BoundUnsafeExpression leftUnsafe)
            {
                conditionIsUnsafe = true;
                left = leftUnsafe.Expression;
            }

            if (binary.Right is not BoundConstant zero
                || zero.Type != left.Type
                || zero.Bits.Low != 0 || zero.Bits.High != 0
                || !PrimitiveTypeFacts.IsInteger(left.Type))
            {
                throw Unsupported(
                    "A direct NativeOutput Body comparison requires a typed zero.");
            }

            if (left is BoundCall call
                && (insideUnsafe || conditionIsUnsafe)
                && expectedCalls.Contains(call)
                && call.Arguments.Any(static argument =>
                    argument is BoundNativeOutputReservation)
                && expressionCalls.Add(call))
            {
                ValidateCallOrder(call);
                return;
            }

            if (left is BoundRead valueRead
                && (uint)valueRead.Variable
                    < (uint)caller.Variables.Length
                && valueRead.Type == caller.Variables[valueRead.Variable].Type
                && (valueRead.Variable < caller.ParameterCount
                    || backingLocalByVariable.Contains(valueRead.Variable)
                    || outputVariables.Contains(valueRead.Variable)))
            {
                return;
            }

            throw Unsupported(
                "A direct NativeOutput Body comparison does not select a caller value or output result.");
        }

        void VisitStatement(BoundStatement statement, bool insideUnsafe,
            bool insideConditional, bool topLevel)
        {
            switch (statement)
            {
                case BoundVacantLocal vacant when topLevel:
                    if (!declaredVacant.Add(vacant.Variable))
                    {
                        throw Unsupported(
                            "The direct NativeOutput caller declares a vacant local twice.");
                    }
                    return;

                case BoundLocal local when topLevel
                    && local.Initializer is BoundRead read:
                    var sourceIsAvailable = read.Variable >= 0
                        && read.Variable < caller.Variables.Length
                        && (read.Variable < caller.ParameterCount
                            || backingLocalByVariable.Contains(
                                read.Variable));
                    if ((uint)local.Variable >= (uint)caller.Variables.Length
                            || local.Variable < caller.ParameterCount
                            || caller.Variables[local.Variable].IsInitializationLocal
                        || outputVariables.Contains(local.Variable)
                        || !sourceIsAvailable
                        || read.Type != caller.Variables[local.Variable].Type
                        || read.Type != caller.Variables[read.Variable].Type
                        || !backingLocalByVariable.Add(local.Variable))
                    {
                        throw Unsupported(
                            "A direct NativeOutput local must copy a caller formal or an earlier copied local.");
                    }

                    backingLocals.Add(local);
                    return;

                case BoundUnsafe unsafeBlock when !insideUnsafe:
                    VisitStatement(unsafeBlock.Body, true,
                        insideConditional, false);
                    return;

                case BoundBlock block:
                    foreach (var nested in block.Statements)
                    {
                        VisitStatement(nested, insideUnsafe,
                            insideConditional, false);
                    }
                    return;

                case BoundIf branch:
                    ValidateConditionalCondition(branch.Condition,
                        insideUnsafe);
                    VisitStatement(branch.Then, insideUnsafe, true, false);
                    if (branch.Else is not null)
                    {
                        VisitStatement(branch.Else, insideUnsafe, true, false);
                    }
                    return;

                case BoundExpressionStatement {
                    Expression: BoundCall expressionCall,
                } when insideUnsafe
                    && expectedCalls.Contains(expressionCall)
                    && expressionCalls.Add(expressionCall):
                    ValidateCallOrder(expressionCall);
                    return;

                case BoundReturn {
                    Value: BoundCall returnedCall,
                } when insideUnsafe && !insideConditional
                    && expectedCalls.Contains(returnedCall)
                    && returnCalls.Add(returnedCall):
                    ValidateCallOrder(returnedCall);
                    return;

                case BoundReturn { Value: null } when topLevel:
                    outerUnitReturnCount++;
                    return;

                default:
                    throw Unsupported(
                        "The direct NativeOutput caller contains an unsupported statement.");
            }
        }

        void ValidateCallOrder(BoundCall call)
        {
            if (nextCallIndex >= calls.Count
                || !ReferenceEquals(calls[nextCallIndex], call))
            {
                throw Unsupported(
                    "The direct NativeOutput Body call order differs from source order.");
            }

            nextCallIndex++;
        }

        foreach (var statement in body.Statements)
        {
            VisitStatement(statement, false, false, true);
        }

        var returnsCallResult = caller.ReturnType != PrimitiveType.Void;
        if ((returnsCallResult
                ? returnCalls.Count != 1
                    || expressionCalls.Count != calls.Count - 1
                    || !returnCalls.Contains(calls[^1])
                : expressionCalls.Count != calls.Count
                    || returnCalls.Count != 0)
            || expressionCalls.Count + returnCalls.Count != expectedCalls.Count
            || nextCallIndex != calls.Count
            || declaredVacant.Count != outputVariables.Count
            || outputVariables.Any(variable =>
                !declaredVacant.Contains(variable))
            || caller.Variables.Length
                != caller.ParameterCount + outputVariables.Count
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
