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
    internal static byte[] EmitForeignInvocationDraftNativeAddressLibrary(
        BoundProgram program, string moduleIdentity)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        if (program.EntryPoint < 0)
        {
            SemanticVerifier.VerifyLibraryWithDirectNativeImports(program);
        }
        else
        {
            SemanticVerifier.VerifyWithDirectNativeImports(program);
        }

        if (program.Functions.Length != 2
            || !program.Structures.IsEmpty || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty || !program.NativeUtf16ZLiterals.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty || program.RequiresSourceGrant
            || program.InvocationProfileId is not null
            || program.IsInvocationSourceBound
            || program.RuntimePolicy != BoundRuntimePolicy.FailFast
            || program.NativeFunctionPointerSignatures.Length != 1
            || !NativeFunctionPointerContract.IsValidTable(
                program.NativeFunctionPointerSignatures)
            || !NativePointerTypeContract.IsValidTable(program.NativePointerTypes,
                program.Structures, program.NativeFunctionPointerSignatures))
        {
            throw Unsupported(
                "Foreign-invocation NativeAddress emission requires two fail-fast functions and one pointer signature.");
        }

        var callerIndex = -1;
        var targetIndex = -1;
        BoundNativeFunctionAddress? address = null;
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            var addresses = new List<BoundNativeFunctionAddress>();
            if (!BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundNativeFunctionAddress nativeAddress)
                    {
                        addresses.Add(nativeAddress);
                    }
                }))
            {
                throw Unsupported("A function Body cannot be traversed for NativeAddress operations.");
            }

            if (addresses.Count == 0)
            {
                continue;
            }
            if (addresses.Count != 1 || address is not null)
            {
                throw Unsupported("This NativeAddress producer supports one formation operation.");
            }

            callerIndex = index;
            address = addresses[0];
            targetIndex = address.Function;
        }

        if (address is not BoundNativeFunctionAddress selectedAddress
            || targetIndex < 0 || targetIndex >= program.Functions.Length
            || callerIndex < 0 || callerIndex == targetIndex)
        {
            throw Unsupported("A NativeAddress Body must select one distinct target declaration.");
        }

        if (program.EntryPoint >= 0 && program.EntryPoint != callerIndex)
        {
            throw Unsupported(
                "The NativeAddress executable entry must be its caller method.");
        }

        var target = program.Functions[targetIndex];
        var caller = program.Functions[callerIndex];
        var moduleImport = target.ModuleImport;
        if (target.DirectNativeImport is not DirectNativeImport import
            || !DirectNativeImportContract.IsValid(target, program.Structures,
                program.NativeFunctionPointerSignatures, program.NativePointerTypes)
            || target.IsModuleReference != (moduleImport is not null)
            || (moduleImport is not null
                && (string.IsNullOrEmpty(moduleImport.ProducerIdentity)
                    || string.IsNullOrEmpty(moduleImport.ExportKey)
                    || StringComparer.Ordinal.Equals(
                        moduleImport.ProducerIdentity, moduleIdentity)))
            || !import.Outputs.IsEmpty
            || import.CallingConvention is < NativeCallingConvention.Unmanaged
                or > NativeCallingConvention.Vectorcall
            || target.DoesNotReturn
            || caller.IsExternal || caller.DirectNativeImport is not null
            || caller.DoesNotReturn || caller.RequiresUnsafeCaller
            || !caller.NativeOutputContracts.IsEmpty
            || !NativeFunctionPointerTypes.IsType(selectedAddress.Type))
        {
            throw Unsupported(
                "The NativeAddress producer supports a zero-argument, unit NativeImport and unit caller.");
        }

        var signatureIndex = NativeFunctionPointerTypes.Index(selectedAddress.Type);
        var signature = program.NativeFunctionPointerSignatures[signatureIndex];
        if (signature.CallingConvention != import.CallingConvention
            || signature.EffectsMask != 0
            || !signature.Selectors.IsEmpty || !signature.OutputStates.IsEmpty
            || !signature.Dependencies.IsEmpty)
        {
            throw Unsupported(
                "The selected NativeAddress signature exceeds the scalar NativeImport subset.");
        }
        if (!NativeFunctionPointerContract.TryCreateNativeImportSignature(target,
            program.NativeFunctionPointerSignatures, out var actualSignature)
            || actualSignature is null
            || !NativeFunctionPointerContract.AreEquivalent(signature, actualSignature))
        {
            throw Unsupported("The selected pointer signature differs from its NativeImport contract.");
        }
        if (!selectedAddress.IsUnsafeAuthorized)
        {
            throw Unsupported("NativeAddress formation requires bound unsafe authorization.");
        }
        var indirectCall = TryGetNativeAddressCall(
            caller, selectedAddress, signature, out var callHomeIndex,
            out var callResultHomeIndex);
        var emitsIndirectCall = indirectCall is not null;
        if (!emitsIndirectCall && caller.ParameterCount != 0)
        {
            throw Unsupported(
                "The discarded NativeAddress profile requires a parameterless caller.");
        }

        if (!emitsIndirectCall && !IsAddressOnlyVoidBody(caller, selectedAddress))
        {
            throw Unsupported(
                "The caller Body is outside the discarded-address or unit indirect-call profile.");
        }

        var targetMethodRow = targetIndex + 1;
        var callerMethodRow = callerIndex + 1;
        var anchorMethodRow = program.Functions.Length + 1;
        var typeSpecRows = new List<ReadOnlyMemory<byte>>
        {
            TrRmTypeSpecTableWriter.NativeFunctionPointer(
                (byte)signature.CallingConvention, checked((uint)anchorMethodRow)),
        };
        var signatureTypeSpecRefs = new Dictionary<PrimitiveType, uint>();
        uint GetSignatureTypeSpecRef(PrimitiveType type)
        {
            if (type == PrimitiveType.Void)
            {
                return 0xF200_0000;
            }

            byte[] typeSpecBytes;
            if (NativePointerTypes.IsType(type))
            {
                if (!NativePointerTypeContract.IsValidType(type,
                        program.NativePointerTypes))
                {
                    throw Unsupported(
                        $"The NativeAddress producer does not support pointer type {type}.");
                }

                var pointer = program.NativePointerTypes[
                    NativePointerTypes.Index(type)];
                var pointeeIndex = Array.IndexOf(s_types, pointer.PointeeType);
                if (pointeeIndex < 0
                    || (pointer.PointeeType != PrimitiveType.Void
                        && !DirectNativeImportContract.IsScalar(pointer.PointeeType)))
                {
                    throw Unsupported(
                        $"The NativeAddress producer does not support pointer pointee {pointer.PointeeType}.");
                }

                typeSpecBytes = TrRmTypeSpecTableWriter.InlineFamily(0x8C,
                    s_codes[pointeeIndex], pointer.IsWritable ? (byte)1 : (byte)0);
            }
            else
            {
                var scalarIndex = Array.IndexOf(s_types, type);
                if (scalarIndex <= 0 || !DirectNativeImportContract.IsScalar(type))
                {
                    throw Unsupported(
                        $"The NativeAddress producer does not support signature type {type}.");
                }

                typeSpecBytes
                    = TrRmTypeSpecTableWriter.Builtin(s_codes[scalarIndex]);
            }

            if (!signatureTypeSpecRefs.TryGetValue(type, out var typeSpecRef))
            {
                typeSpecRef = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(new ReadOnlyMemory<byte>(typeSpecBytes));
                signatureTypeSpecRefs.Add(type, typeSpecRef);
            }

            return typeSpecRef;
        }

        var callerParameterTypeSpecRefs = caller.Variables
            .Take(caller.ParameterCount)
            .Select(static variable => variable.Type)
            .Select(GetSignatureTypeSpecRef).ToArray();
        var targetParameterTypeSpecRefs
            = signature.ParameterTypes.Select(GetSignatureTypeSpecRef).ToArray();
        const int callerSignatureRow = 1;
        const int nativeSignatureRow = 2;
        var parameterRows = new List<TrRmParamRow>(
            callerParameterTypeSpecRefs.Length
                + targetParameterTypeSpecRefs.Length);
        for (var ordinal = 0;
            ordinal < callerParameterTypeSpecRefs.Length; ordinal++)
        {
            parameterRows.Add(new TrRmParamRow(callerSignatureRow, ordinal,
                callerParameterTypeSpecRefs[ordinal], 0));
        }

        var targetParameterFirst = targetParameterTypeSpecRefs.Length == 0
            ? 0 : parameterRows.Count + 1;
        for (var ordinal = 0;
            ordinal < targetParameterTypeSpecRefs.Length; ordinal++)
        {
            parameterRows.Add(new TrRmParamRow(nativeSignatureRow, ordinal,
                targetParameterTypeSpecRefs[ordinal], 0));
        }

        var signatureRows = new[]
        {
            new TrRmMethodSigRow(GetSignatureTypeSpecRef(caller.ReturnType), 0,
                callerParameterTypeSpecRefs.Length == 0 ? 0 : 1,
                callerParameterTypeSpecRefs.Length, 0, 0),
            new TrRmMethodSigRow(GetSignatureTypeSpecRef(signature.ReturnType), 0,
                targetParameterFirst, targetParameterTypeSpecRefs.Length, 0, 0),
        };
        var featureRows = new List<TrRmFeatureRow> { new(4, 1) };
        if (!import.Parameters.IsEmpty)
        {
            featureRows.Add(TrRmFeatureRow.ForeignInvocationContractsCandidate);
        }

        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows,
            TrRmArtifactSchema.ForeignInvocationDraft);
        var featureReader = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, TrRmArtifactSchema.ForeignInvocationDraft);

        var stringRows = new List<(string Text, int Encoding)> { ("", 0) };
        var stringIndexes = new Dictionary<(string Text, int Encoding), int>
        {
            [("", 0)] = 1,
        };
        var moduleNameRow = AddString(moduleIdentity, 1);
        var methodNameRows = new int[program.Functions.Length];
        for (var index = 0; index < program.Functions.Length; index++)
        {
            methodNameRows[index] = AddString(program.Functions[index].Name, 1);
        }
        var anchorNameRow = AddString(CreateAnchorName(program.Functions), 1);
        var libraryNameRow = AddString(import.LibraryName, 0);
        var exportNameRow = AddString(import.ExportName, 0);
        var providerIdentityRow = moduleImport is null
            ? 0 : AddString(moduleImport.ProducerIdentity, 1);
        var providerExportKeyRow = moduleImport is null
            ? 0 : AddString(moduleImport.ExportKey, 1);
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringsBytes, stringRows.Count);

        var moduleRefRows = new List<TrRmModuleRefRow>(
            moduleImport is null ? 0 : 1);
        if (moduleImport is not null)
        {
            moduleRefRows.Add(new TrRmModuleRefRow(providerIdentityRow,
                moduleImport.TargetRequirement, moduleImport.RuntimePolicy));
        }

        var entryMethodRow = program.EntryPoint < 0
            ? 0 : program.EntryPoint + 1;
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(moduleNameRow, 1, 0, entryMethodRow, 1, 1, 1, 1,
                1, featureRows.Count, 1),
        ], strings, moduleRefRows.Count, 0,
            program.Functions.Length + 1, 0, featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefRows.Count, 0, program.Functions.Length + 1, 0,
            featureRows.Count);
        var moduleRefBytes = moduleRefRows.Count == 0
            ? []
            : TrRmModuleRefTableWriter.Encode(moduleRefRows, strings, modules);
        var moduleRefs = moduleRefRows.Count == 0
            ? default
            : new TrRmModuleRefTableReader(moduleRefBytes,
                moduleRefRows.Count, strings, modules);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            0, 0, 0, 0, TrRmArtifactSchema.ForeignInvocationDraft,
            methodDefCount: program.Functions.Length + 1);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, 0, 0, 0, 0,
            TrRmArtifactSchema.ForeignInvocationDraft,
            methodDefCount: program.Functions.Length + 1);
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, parameterRows.Count, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Length, typeSpecs, parameterRows.Count, 0);
        var paramBytes = parameterRows.Count == 0
            ? []
            : TrRmParamTableWriter.Encode(parameterRows, signatures, typeSpecs,
                TrRmArtifactSchema.ForeignInvocationDraft);
        var parameters = parameterRows.Count == 0
            ? default
            : new TrRmParamTableReader(paramBytes, parameterRows.Count,
                signatures, typeSpecs,
                TrRmArtifactSchema.ForeignInvocationDraft);
        var nativeParameters = import.Parameters
            .OrderBy(static parameter => parameter.ParameterIndex).ToArray();
        var targetContractCount = nativeParameters.Length + 1;
        var targetOwner = 0x1600_0000u | (uint)targetMethodRow;
        var anchorOwner = 0x1600_0000u | (uint)anchorMethodRow;
        var contractRows = new List<TrRmContractRow>(
            1 + (nativeParameters.Length * 2));
        var contractPayload = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(contractPayload, 4);
        contractRows.Add(new TrRmContractRow(targetOwner, 1, contractPayload));
        var nativeParameterPayloads = new List<byte[]>(nativeParameters.Length);
        foreach (var parameter in nativeParameters)
        {
            if (parameter.Retention != NativeParameterRetention.NoEscape)
            {
                throw Unsupported(
                    "The NativeAddress producer requires NoEscape NativeParameter contracts.");
            }

            var payload = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(payload,
                checked((uint)parameter.ParameterIndex));
            payload[4] = checked((byte)((byte)parameter.Access + 1));
            payload[5] = 1;
            payload[6] = parameter.Extent == 0 ? (byte)0 : (byte)1;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8),
                parameter.Extent);
            nativeParameterPayloads.Add(payload);
            contractRows.Add(new TrRmContractRow(targetOwner, 0x0E, payload));
        }

        var anchorContractFirst = nativeParameters.Length == 0
            ? 0 : targetContractCount + 1;
        foreach (var payload in nativeParameterPayloads)
        {
            contractRows.Add(new TrRmContractRow(anchorOwner, 0x0E, payload));
        }

        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, contractRows.Count);
        var methodRows = new List<TrRmMethodDefRow>(program.Functions.Length + 1);
        var exportRows = new List<TrRmExportRow>();
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            var methodRow = index + 1;
            var visibility = function.IsPrivate ? 0u : function.IsInternal ? 1u : 2u;
            var isTarget = index == targetIndex;
            var isCaller = index == callerIndex;
            var contractFirst = isTarget ? 1 : 0;
            var methodContractCount = isTarget ? targetContractCount : 0;
            methodRows.Add(new TrRmMethodDefRow(0, methodNameRows[index],
                isCaller ? 1 : nativeSignatureRow,
                visibility, contractFirst, methodContractCount,
                isCaller ? 1 : 0));
            if (visibility == 2 && function.ModuleImport is null)
            {
                exportRows.Add(new TrRmExportRow(
                    0x1600_0000u | (uint)methodRow, methodNameRows[index]));
            }
        }
        methodRows.Add(new TrRmMethodDefRow(0, anchorNameRow,
            nativeSignatureRow, 0, anchorContractFirst,
            nativeParameters.Length, 0));
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows, strings,
            modules, typeDefs, signatures, contractRows.Count, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodRows.Count, strings, modules, typeDefs, signatures,
            contractRows.Count, 1);
        var importRows = new List<TrRmImportRow>(moduleImport is null ? 0 : 1);
        if (moduleImport is not null)
        {
            importRows.Add(new TrRmImportRow(1, providerExportKeyRow,
                0x1600_0000u | (uint)targetMethodRow));
        }

        var importBytes = importRows.Count == 0
            ? []
            : TrRmImportTableWriter.Encode(importRows, strings, moduleRefs,
                typeDefs, methods, default, default, default, default,
                featureReader, modules,
                TrRmArtifactSchema.ForeignInvocationDraft);
        var imports = importRows.Count == 0
            ? default
            : new TrRmImportTableReader(importBytes, importRows.Count,
                strings, moduleRefs, typeDefs, methods, default, default,
                default, default, featureReader, modules,
                TrRmArtifactSchema.ForeignInvocationDraft);
        var contractBytes = TrRmContractTableWriter.EncodeForeignInvocationDraft(
            contractRows, typeDefs, methods, signatures, parameters, typeSpecs,
            featureReader, modules, predicateGraphCount: 0);
        var contracts = new TrRmContractTableReader(contractBytes,
            contractRows.Count, typeDefs, methods, signatures, parameters,
            typeSpecs, 0, featureReader, modules,
            TrRmArtifactSchema.ForeignInvocationDraft);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layouts = new TrRmLayoutTableReader([], 0, typeDefs, targetProfiles, 0);
        var nativeImportBytes = TrRmNativeImportTableWriter.Encode([
            new TrRmNativeImportRow(1, (byte)import.ResolutionMode,
                (byte)import.ResolutionPolicy, (byte)import.CallingConvention,
                targetMethodRow, libraryNameRow, exportNameRow),
        ], strings, methods, default, typeDefs, imports, modules,
            signatures, parameters, typeSpecs, contracts, layouts);
        var stagedCalls = new List<BoundCall>(
            indirectCall?.Arguments.Length ?? 0);
        if (indirectCall is not null)
        {
            foreach (var argument in indirectCall.Arguments)
            {
                if (argument is BoundCall stagedCall)
                {
                    stagedCalls.Add(stagedCall);
                }
            }
        }

        var stagedResultHomeCount = Math.Max(0, stagedCalls.Count - 1);
        var stagedResultHomeStart = 1 + (callResultHomeIndex >= 0 ? 1 : 0);
        var stagedResultTypeMapToken = stagedResultHomeCount == 0
            ? 0u
            : 0x1100_0000u | GetSignatureTypeSpecRef(signature.ReturnType);
        var typeMapTokens = new List<uint> { 0x1100_0001 };
        if (callResultHomeIndex >= 0)
        {
            typeMapTokens.Add(0x1100_0000u
                | GetSignatureTypeSpecRef(caller.ReturnType));
        }
        if (stagedResultHomeCount > 0)
        {
            typeMapTokens.Add(stagedResultTypeMapToken);
        }

        foreach (var typeSpecRef in callerParameterTypeSpecRefs)
        {
            typeMapTokens.Add(0x1100_0000u | typeSpecRef);
        }

        var targetMethodMapToken = moduleImport is null
            ? 0x1600_0000u | (uint)targetMethodRow
            : 0x1C00_0001u;
        var maps = TrRmRegisterBodyMapBuilder.Create(typeMapTokens,
            [targetMethodMapToken]);
        var entryState = new byte[8];
        entryState[0] = 0x01;
        var nativeAddress = TrRmRegisterBodyRecordWriter.EncodeNativeAddress(
            emitsIndirectCall ? (byte)2 : (byte)1,
            emitsIndirectCall ? checked((ushort)callHomeIndex) : (ushort)15,
            maps.GetMethodMapIndex(targetMethodMapToken),
            maps.GetTypeMapIndex(0x1100_0001));
        var callLocalHomeCount = stagedResultHomeStart
            + stagedResultHomeCount;
        var homeMap = emitsIndirectCall
            ? new byte[(callerParameterTypeSpecRefs.Length
                + callLocalHomeCount) * 8]
            : [];
        var callInputs = new List<TrRmRegisterBodyCallInput>(
            indirectCall?.Arguments.Length ?? 0);
        if (emitsIndirectCall)
        {
            homeMap[0] = 0x03;
            BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(2),
                maps.GetTypeMapIndex(0x1100_0001));
            homeMap[1] = caller.Variables[caller.ParameterCount].IsMutable
                ? (byte)1 : (byte)0;
            if (callResultHomeIndex >= 0)
            {
                var resultOffset = callResultHomeIndex * 8;
                var resultTypeMapToken = 0x1100_0000u
                    | GetSignatureTypeSpecRef(caller.ReturnType);
                homeMap[resultOffset] = 0x03;
                homeMap[resultOffset + 1]
                    = caller.Variables[caller.ParameterCount + 1].IsMutable
                        ? (byte)1 : (byte)0;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    homeMap.AsSpan(resultOffset + 2),
                    maps.GetTypeMapIndex(resultTypeMapToken));
            }

            for (var stageHomeOrdinal = 0;
                stageHomeOrdinal < stagedResultHomeCount; stageHomeOrdinal++)
            {
                var offset = (stagedResultHomeStart + stageHomeOrdinal) * 8;
                homeMap[offset] = 0x03;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    homeMap.AsSpan(offset + 2),
                    maps.GetTypeMapIndex(stagedResultTypeMapToken));
            }

            for (var ordinal = 0;
                ordinal < callerParameterTypeSpecRefs.Length; ordinal++)
            {
                var offset = (ordinal + callLocalHomeCount) * 8;
                var typeMapToken = 0x1100_0000u
                    | callerParameterTypeSpecRefs[ordinal];
                homeMap[offset] = 0x02;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    homeMap.AsSpan(offset + 2),
                    maps.GetTypeMapIndex(typeMapToken));
                BinaryPrimitives.WriteUInt16LittleEndian(
                    homeMap.AsSpan(offset + 4), checked((ushort)ordinal));
            }

            var stagedArgumentCallOrdinal = 0;
            foreach (var argument in indirectCall!.Arguments)
            {
                if (argument is BoundRead read)
                {
                    callInputs.Add(new TrRmRegisterBodyCallInput(2, 1,
                        checked((ushort)(read.Variable + callLocalHomeCount))));
                }
                else if (argument is BoundCall)
                {
                    callInputs.Add(stagedArgumentCallOrdinal < stagedCalls.Count - 1
                        ? new TrRmRegisterBodyCallInput(2, 1,
                            checked((ushort)(stagedResultHomeStart
                                + stagedArgumentCallOrdinal)))
                        : new TrRmRegisterBodyCallInput(1, 1, 0));
                    stagedArgumentCallOrdinal++;
                }
                else
                {
                    throw Unsupported(
                        "The NativeAddress CallSite supports only formal reads and staged direct-call results.");
                }
            }
        }
        var records = new List<ReadOnlyMemory<byte>>
        {
            entryState,
            nativeAddress,
        };
        var code = new List<byte> { 0xE9, 0, 1, 0 };
        var returnsCallResult = indirectCall is not null
            && indirectCall.Type != PrimitiveType.Void;
        if (emitsIndirectCall)
        {
            for (var stagedInstructionOrdinal = 0;
                stagedInstructionOrdinal < stagedCalls.Count;
                stagedInstructionOrdinal++)
            {
                var stagedCall = stagedCalls[stagedInstructionOrdinal];
                var stagedInputs = new List<TrRmRegisterBodyCallInput>(
                    stagedCall.Arguments.Length);
                foreach (var argument in stagedCall.Arguments)
                {
                    var read = (BoundRead)argument;
                    stagedInputs.Add(new TrRmRegisterBodyCallInput(2, 1,
                        checked((ushort)(read.Variable + callLocalHomeCount))));
                }

                var stagedCallIndex = checked((ushort)records.Count);
                var isFinalStagedCall
                    = stagedInstructionOrdinal == stagedCalls.Count - 1;
                records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
                    1, isFinalStagedCall ? (byte)1 : (byte)2,
                    maps.GetMethodMapIndex(targetMethodMapToken),
                    isFinalStagedCall ? (ushort)0
                        : checked((ushort)(stagedResultHomeStart
                            + stagedInstructionOrdinal)),
                    stagedInputs, []));
                AppendCallSiteInstruction(code, stagedCallIndex);
            }

            var callResultKind = !returnsCallResult ? (byte)0
                : callResultHomeIndex >= 0 ? (byte)2 : (byte)1;
            var callResultIndex = callResultHomeIndex >= 0
                ? checked((ushort)callResultHomeIndex) : (ushort)0;
            var callSiteIndex = checked((ushort)records.Count);
            records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
                3, callResultKind, checked((ushort)callHomeIndex),
                callResultIndex, callInputs, []));
            AppendCallSiteInstruction(code, callSiteIndex);
        }

        if (returnsCallResult && callResultHomeIndex >= 0)
        {
            code.Add(0xB8);
            code.Add(checked((byte)callResultHomeIndex));
        }
        else
        {
            code.Add(returnsCallResult ? (byte)0x17 : (byte)0x18);
            code.Add(0);
        }
        var codeBytes = code.ToArray();
        var body = TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, default,
            [new TrRmRegisterBodyBlock(0, (uint)codeBytes.Length, 0)],
            records, codeBytes, contractRows.Count,
            TrRmArtifactSchema.ForeignInvocationDraft);
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, (uint)stringRows.Count, stringsBytes),
            new(0x0004, 0, (uint)featureRows.Count, featureBytes),
            new(0x0011, 0, (uint)typeSpecRows.Count, typeSpecBytes),
            new(0x0016, 0, (uint)methodRows.Count, methodBytes),
            new(0x0017, 0, (uint)signatureRows.Length, signatureBytes),
            new(0x001B, 0, (uint)contractRows.Count, contractBytes),
        };
        if (moduleRefRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0010, 0,
                (uint)moduleRefRows.Count, moduleRefBytes));
        }
        if (parameterRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0,
                (uint)parameterRows.Count, paramBytes));
        }
        if (importRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                (uint)importRows.Count, importBytes));
        }
        if (exportRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0, (uint)exportRows.Count,
                TrRmExportTableWriter.Encode(exportRows, strings, typeDefs,
                    methods, default, imports, default, default, default,
                    featureReader, modules,
                    TrRmArtifactSchema.ForeignInvocationDraft)));
        }
        sections.Add(new TrRmSection(0x0020, 0, 1,
            TrRmVariableTableWriter.EncodeBodies([body])));
        sections.Add(new TrRmSection(0x0021, 0, 1, nativeImportBytes));
        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));

        var artifact = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        tables.ValidateRegisterBodyMapsForMethod(callerMethodRow);
        return artifact;

        int AddString(string text, int encoding)
        {
            var key = (text, encoding);
            if (stringIndexes.TryGetValue(key, out var row))
            {
                return row;
            }

            row = stringRows.Count + 1;
            stringIndexes.Add(key, row);
            stringRows.Add(key);
            return row;
        }
    }

    private static bool IsAddressOnlyVoidBody(BoundFunction function,
        BoundNativeFunctionAddress address)
    {
        var statements = new List<BoundStatement>();
        if (!AppendStatements(function.Body, statements)
            || statements.Count != 2
            || statements[1] is not BoundReturn { Value: null })
        {
            return false;
        }

        if (statements[0] is BoundExpressionStatement
            { Expression: BoundNativeFunctionAddress expressionAddress })
        {
            return expressionAddress == address;
        }

        if (statements[0] is not BoundLocal
            {
                Initializer: BoundNativeFunctionAddress localAddress,
            } discard
            || localAddress != address
            || (uint)discard.Variable >= (uint)function.Variables.Length)
        {
            return false;
        }

        var variable = function.Variables[discard.Variable];
        if (variable.Name != "<discard>" || variable.Type != address.Type
            || variable.IsMutable || variable.IsOut || variable.IsInit
            || variable.IsInitializationLocal || variable.IsImplicitRef
            || variable.IsImplicitReferenceTemporary
            || variable.NativeFixedArrayLayout is not null)
        {
            return false;
        }

        var isRead = false;
        return BoundTreeTraversal.VisitExpressions(function.Body, expression =>
        {
            if (expression is BoundRead read && read.Variable == discard.Variable)
            {
                isRead = true;
            }
        }) && !isRead;
    }

    private static BoundIndirectCall? TryGetNativeAddressCall(
        BoundFunction function, BoundNativeFunctionAddress address,
        NativeFunctionPointerSignatureDefinition signature, out int homeIndex,
        out int resultHomeIndex)
    {
        homeIndex = -1;
        resultHomeIndex = -1;
        var statements = new List<BoundStatement>();
        if (!AppendStatements(function.Body, statements)
            || statements.Count is not (2 or 3)
            || statements[0] is not BoundLocal
                { Initializer: BoundNativeFunctionAddress localAddress } local
            || localAddress != address
            || (uint)local.Variable >= (uint)function.Variables.Length
            || local.Variable != function.ParameterCount
            || !function.Body.RetiredManagedLocals.IsEmpty)
        {
            return null;
        }

        BoundIndirectCall call;
        var returnsCallResult = false;
        var storesCallResult = false;
        if (statements.Count == 3
            && statements[1] is BoundExpressionStatement
                { Expression: BoundIndirectCall statementCall }
            && statements[2] is BoundReturn { Value: null })
        {
            call = statementCall;
            if (function.Variables.Length != function.ParameterCount + 1)
            {
                return null;
            }
        }
        else if (statements.Count == 2
            && statements[1] is BoundReturn
                { Value: BoundIndirectCall returnedCall })
        {
            call = returnedCall;
            returnsCallResult = true;
            if (function.Variables.Length != function.ParameterCount + 1)
            {
                return null;
            }
        }
        else if (statements.Count == 3
            && statements[1] is BoundLocal
                { Initializer: BoundIndirectCall initializedCall } resultLocal
            && statements[2] is BoundReturn
                { Value: BoundRead resultRead }
            && resultLocal.Variable == function.ParameterCount + 1
            && resultRead.Variable == resultLocal.Variable
            && resultRead.Type == initializedCall.Type)
        {
            call = initializedCall;
            returnsCallResult = true;
            storesCallResult = true;
            resultHomeIndex = 1;
            if (function.Variables.Length != function.ParameterCount + 2)
            {
                return null;
            }

            var resultVariable = function.Variables[resultLocal.Variable];
            if (resultVariable.Type != initializedCall.Type
                || resultVariable.IsOut || resultVariable.IsInit
                || resultVariable.IsImplicitRef
                || resultVariable.IsImplicitReferenceTemporary
                || resultVariable.IsInitializationLocal
                || resultVariable.NativeFixedArrayLayout is not null)
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        if (call.Target is not BoundRead targetRead
            || targetRead.Variable != local.Variable
            || targetRead.Type != address.Type
            || call.Arguments.Length != signature.ParameterTypes.Length
            || call.Arguments.Length != function.ParameterCount
            || !call.IsUnsafeAuthorized
            || (returnsCallResult
                ? call.Type == PrimitiveType.Void
                    || function.ReturnType != call.Type
                    || signature.ReturnType != call.Type
                : call.Type != PrimitiveType.Void
                    || function.ReturnType != PrimitiveType.Void
                    || signature.ReturnType != PrimitiveType.Void)
            || (storesCallResult != (resultHomeIndex >= 0)))
        {
            return null;
        }

        var variable = function.Variables[local.Variable];
        if (variable.Type != address.Type || variable.IsOut || variable.IsInit
            || variable.IsImplicitRef || variable.IsImplicitReferenceTemporary
            || variable.IsInitializationLocal
            || variable.NativeFixedArrayLayout is not null)
        {
            return null;
        }

        for (var ordinal = 0; ordinal < function.ParameterCount; ordinal++)
        {
            var formal = function.Variables[ordinal];
            if (formal.Type == PrimitiveType.Void
                || (!NativePointerTypes.IsType(formal.Type)
                    && !DirectNativeImportContract.IsScalar(formal.Type))
                || formal.IsOut || formal.IsInit || formal.IsImplicitRef
                || formal.IsImplicitReferenceTemporary
                || formal.IsInitializationLocal
                || formal.NativeFixedArrayLayout is not null
                || call.Arguments[ordinal].Type != signature.ParameterTypes[ordinal])
            {
                return null;
            }

            if (call.Arguments[ordinal] is BoundRead argument)
            {
                if ((uint)argument.Variable >= (uint)function.ParameterCount
                    || argument.Type != function.Variables[argument.Variable].Type)
                {
                    return null;
                }
            }
            else if (call.Arguments[ordinal] is BoundCall stagedCall)
            {
                if (stagedCall.Type != signature.ParameterTypes[ordinal]
                    || !IsSupportedNativeAddressStagedCall(stagedCall,
                        address.Function, function, signature))
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        homeIndex = 0;
        return call;
    }

    private static bool IsSupportedNativeAddressStagedCall(BoundCall call,
        int targetFunction, BoundFunction caller,
        NativeFunctionPointerSignatureDefinition signature)
    {
        if (call.Function != targetFunction || !call.IsUnsafeAuthorized
            || call.Type != signature.ReturnType
            || !DirectNativeImportContract.IsScalar(call.Type)
            || call.Arguments.Length != signature.ParameterTypes.Length
            || call.Arguments.Length != caller.ParameterCount)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < call.Arguments.Length; ordinal++)
        {
            var formal = caller.Variables[ordinal];
            if (call.Arguments[ordinal] is not BoundRead argument
                || (uint)argument.Variable >= (uint)caller.ParameterCount
                || argument.Type != formal.Type
                || argument.Type != signature.ParameterTypes[ordinal])
            {
                return false;
            }
        }

        return true;
    }

    private static bool AppendStatements(BoundBlock block,
        List<BoundStatement> statements)
    {
        if (!block.RetiredManagedLocals.IsEmpty)
        {
            return false;
        }

        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case BoundBlock nested:
                    if (!AppendStatements(nested, statements))
                    {
                        return false;
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    if (!AppendStatements(unsafeBlock.Body, statements))
                    {
                        return false;
                    }
                    break;
                default:
                    statements.Add(statement);
                    break;
            }
        }

        return true;
    }

    private static void AppendCallSiteInstruction(List<byte> code,
        ushort recordIndex)
    {
        code.Add(0xE0);
        code.Add(0);
        code.Add((byte)recordIndex);
        code.Add((byte)(recordIndex >> 8));
    }

    private static string CreateAnchorName(ImmutableArray<BoundFunction> functions)
    {
        var names = functions.Select(static function => function.Name)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 1; ; index++)
        {
            var name = $"__trrm_fnptr_anchor_{index}";
            if (!names.Contains(name))
            {
                return name;
            }
        }
    }
}
