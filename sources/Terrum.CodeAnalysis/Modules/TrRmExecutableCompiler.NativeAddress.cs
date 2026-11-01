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
    internal static byte[] EmitForeignInvocationDraftNativeAddressLibrary(
        BoundProgram program, string moduleIdentity,
        TrRmArtifactSchema schema = TrRmArtifactSchema.ForeignInvocationDraft,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        if (schema is not (TrRmArtifactSchema.ForeignInvocationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft))
        {
            throw new ArgumentOutOfRangeException(nameof(schema));
        }
        if (sourceTypeExports is { Count: > 0 }
            && (schema is not (
                    TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                    or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                    or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft)
                || program.EntryPoint != -1 || !program.Functions.IsEmpty))
        {
            throw Unsupported(
                "Explicit A2/A2E TypeDef exports require a type-only source library.");
        }
        if (program.EntryPoint < 0)
        {
            SemanticVerifier.VerifyLibraryWithDirectNativeImports(program);
        }
        else
        {
            SemanticVerifier.VerifyWithDirectNativeImports(program);
        }

        if ((schema is TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft)
            && program.EntryPoint < 0 && program.Functions.IsEmpty
            && !program.Structures.IsEmpty)
        {
            return EmitForeignInvocationStorageFixedArrayCopyDeclarations(
                program, moduleIdentity, schema, sourceTypeExports);
        }

        if (program.Functions.Length < 2
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

        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (index == callerIndex)
            {
                continue;
            }

            var importedFunction = program.Functions[index];
            if (importedFunction.DirectNativeImport is null
                || !importedFunction.IsExternal
                || (index != targetIndex
                    && importedFunction.ModuleImport is not null)
                || !importedFunction.DirectNativeImport.Outputs.IsEmpty
                || !DirectNativeImportContract.IsValid(importedFunction,
                    program.Structures,
                    program.NativeFunctionPointerSignatures,
                    program.NativePointerTypes))
            {
                throw Unsupported(
                    "The NativeAddress Body supports one caller and direct NativeImport declarations without output contracts.");
            }
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
        NativeAddressStorageCallPlan? storageCall = null;
        BoundIndirectCall? indirectCall;
        int callHomeIndex;
        int callResultHomeIndex;
        if (schema.UsesForeignInvocationStorageAddresses()
            && TryGetNativeAddressStorageCall(program, caller,
                selectedAddress, signature, schema,
                out var storageAddressCall)
            && storageAddressCall is not null)
        {
            storageCall = storageAddressCall;
            indirectCall = storageAddressCall.Call;
            callHomeIndex = storageAddressCall.TargetVariable
                - caller.ParameterCount;
            callResultHomeIndex = -1;
        }
        else
        {
            indirectCall = TryGetNativeAddressCall(
                caller, selectedAddress, signature, out callHomeIndex,
                out callResultHomeIndex);
        }
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
        var fixedArrayTypeSpecRefs = new Dictionary<int, uint>();
        var fixedArrayTypeStates
            = new byte[program.NativeFixedArrayTypes.Length];
        var fixedArrayLayoutRows = new List<TrRmFixedArrayLayoutRow>();
        var fixedArrayLayoutRowByType = new Dictionary<int, int>();
        uint GetFixedArrayTypeSpecRef(int arrayIndex)
        {
            if ((uint)arrayIndex
                >= (uint)program.NativeFixedArrayTypes.Length)
            {
                throw Unsupported(
                    "The NativeAddress producer received an invalid fixed-array type.");
            }
            if (fixedArrayTypeSpecRefs.TryGetValue(arrayIndex,
                    out var existingRow))
            {
                return existingRow;
            }
            if (fixedArrayTypeStates[arrayIndex] != 0)
            {
                throw Unsupported(
                    "The NativeAddress producer received a recursive fixed-array type.");
            }

            fixedArrayTypeStates[arrayIndex] = 1;
            var definition = program.NativeFixedArrayTypes[arrayIndex];
            var elementTypeSpecRef
                = GetSignatureTypeSpecRef(definition.ElementType);
            var row = checked((uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.FixedInlineArray(
                elementTypeSpecRef, definition.ElementCount));
            fixedArrayTypeSpecRefs.Add(arrayIndex, row);
            var layoutRow = fixedArrayLayoutRows.Count + 1;
            fixedArrayLayoutRowByType.Add(arrayIndex, layoutRow);
            fixedArrayLayoutRows.Add(new TrRmFixedArrayLayoutRow(1,
                checked((int)row), 1, definition.ElementStride,
                definition.ByteExtent, definition.Alignment));
            fixedArrayTypeStates[arrayIndex] = 2;
            return row;
        }

        uint GetSignatureTypeSpecRef(PrimitiveType type)
        {
            if (type == PrimitiveType.Void)
            {
                return 0xF200_0000;
            }

            if (NativeFixedArrayTypes.IsType(type))
            {
                if (schema is not (
                    TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                    or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                    or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft))
                {
                    throw Unsupported(
                        "Fixed-array Body types require the exact A2 schema.");
                }

                return GetFixedArrayTypeSpecRef(
                    NativeFixedArrayTypes.Index(type));
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
        var callerLocalTypeSpecRefs = caller.Variables
            .Skip(caller.ParameterCount)
            .Select(variable => NativeFunctionPointerTypes.IsType(variable.Type)
                ? 0u : GetSignatureTypeSpecRef(variable.Type))
            .ToArray();
        var targetParameterTypeSpecRefs
            = signature.ParameterTypes.Select(GetSignatureTypeSpecRef).ToArray();
        var functionSignatureRows = new int[program.Functions.Length];
        var signatureRows = new List<TrRmMethodSigRow>(
            program.Functions.Length + 1);
        var parameterRows = new List<TrRmParamRow>();
        for (var functionIndex = 0;
            functionIndex < program.Functions.Length;
            functionIndex++)
        {
            var function = program.Functions[functionIndex];
            var signatureRow = signatureRows.Count + 1;
            functionSignatureRows[functionIndex] = signatureRow;
            var parameterTypes = function.Variables
                .Take(function.ParameterCount)
                .Select(static variable => variable.Type)
                .Select(GetSignatureTypeSpecRef).ToArray();
            var parameterFirst = parameterTypes.Length == 0
                ? 0 : parameterRows.Count + 1;
            for (var ordinal = 0; ordinal < parameterTypes.Length; ordinal++)
            {
                parameterRows.Add(new TrRmParamRow(signatureRow, ordinal,
                    parameterTypes[ordinal], 0));
            }

            signatureRows.Add(new TrRmMethodSigRow(
                GetSignatureTypeSpecRef(function.ReturnType), 0,
                parameterFirst, parameterTypes.Length, 0, 0));
        }

        var anchorSignatureRow = functionSignatureRows[targetIndex];
        var featureRows = new List<TrRmFeatureRow>();
        if (fixedArrayLayoutRows.Count != 0)
        {
            featureRows.Add(TrRmFeatureRow.RootlessNativeValues);
        }
        featureRows.Add(new TrRmFeatureRow(4, 1));
        if (program.Functions.Any(static function =>
                function.DirectNativeImport is DirectNativeImport nativeImport
                && !nativeImport.Parameters.IsEmpty))
        {
            featureRows.Add(TrRmFeatureRow.ForeignInvocationContractsCandidate);
        }
        if (fixedArrayLayoutRows.Count != 0)
        {
            featureRows.Add(
                TrRmFeatureRow.FixedInlineArrayStorageCandidate);
        }

        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var featureReader = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema);

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
        var nativeImportLibraryNameRows = new int[program.Functions.Length];
        var nativeImportEntryNameRows = new int[program.Functions.Length];
        var nativeImportFunctionIndices = new List<int>();
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (program.Functions[index].DirectNativeImport
                is not DirectNativeImport nativeImport)
            {
                continue;
            }

            nativeImportLibraryNameRows[index]
                = AddString(nativeImport.LibraryName, 0);
            nativeImportEntryNameRows[index]
                = AddString(nativeImport.ExportName, 0);
            nativeImportFunctionIndices.Add(index);
        }

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
            0, 0, 0, 0, schema,
            methodDefCount: program.Functions.Length + 1);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, 0, 0, 0, 0, schema,
            methodDefCount: program.Functions.Length + 1);
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, parameterRows.Count, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count, typeSpecs, parameterRows.Count, 0);
        var paramBytes = parameterRows.Count == 0
            ? []
            : TrRmParamTableWriter.Encode(parameterRows, signatures, typeSpecs, schema);
        var parameters = parameterRows.Count == 0
            ? default
            : new TrRmParamTableReader(paramBytes, parameterRows.Count,
                signatures, typeSpecs, schema);
        var anchorOwner = 0x1600_0000u | (uint)anchorMethodRow;
        var contractRows = new List<TrRmContractRow>();
        var contractPayload = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(contractPayload, 4);
        var importContractFirstByFunction
            = new int[program.Functions.Length];
        var importContractCountByFunction
            = new int[program.Functions.Length];
        var selectedNativeParameters = import.Parameters
            .OrderBy(static parameter => parameter.ParameterIndex).ToArray();
        var selectedNativeParameterPayloads
            = new List<byte[]>(selectedNativeParameters.Length);
        foreach (var functionIndex in nativeImportFunctionIndices)
        {
            var nativeImport = program.Functions[functionIndex]
                .DirectNativeImport!;
            var nativeParameters = nativeImport.Parameters
                .OrderBy(static parameter => parameter.ParameterIndex)
                .ToArray();
            var owner = 0x1600_0000u
                | checked((uint)(functionIndex + 1));
            importContractFirstByFunction[functionIndex]
                = contractRows.Count + 1;
            contractRows.Add(new TrRmContractRow(owner, 1,
                contractPayload));
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
                contractRows.Add(new TrRmContractRow(owner, 0x0E, payload));
                if (functionIndex == targetIndex)
                {
                    selectedNativeParameterPayloads.Add(payload);
                }
            }

            importContractCountByFunction[functionIndex]
                = nativeParameters.Length + 1;
        }

        var anchorContractFirst = selectedNativeParameters.Length == 0
            ? 0 : contractRows.Count + 1;
        foreach (var payload in selectedNativeParameterPayloads)
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
            var isCaller = index == callerIndex;
            methodRows.Add(new TrRmMethodDefRow(0, methodNameRows[index],
                functionSignatureRows[index], visibility,
                importContractFirstByFunction[index],
                importContractCountByFunction[index],
                isCaller ? 1 : 0));
            if (visibility == 2 && function.ModuleImport is null)
            {
                exportRows.Add(new TrRmExportRow(
                    0x1600_0000u | (uint)methodRow, methodNameRows[index]));
            }
        }
        methodRows.Add(new TrRmMethodDefRow(0, anchorNameRow,
            anchorSignatureRow, 0, anchorContractFirst,
            selectedNativeParameters.Length, 0));
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
                schema);
        var imports = importRows.Count == 0
            ? default
            : new TrRmImportTableReader(importBytes, importRows.Count,
                strings, moduleRefs, typeDefs, methods, default, default,
                default, default, featureReader, modules,
                schema);
        var contractBytes = TrRmContractTableWriter.EncodeForeignInvocationDraft(
            contractRows, typeDefs, methods, signatures, parameters, typeSpecs,
            featureReader, modules, predicateGraphCount: 0, schema: schema);
        var contracts = new TrRmContractTableReader(contractBytes,
            contractRows.Count, typeDefs, methods, signatures, parameters,
            typeSpecs, 0, featureReader, modules,
            schema);
        var targetProfileBytes = fixedArrayLayoutRows.Count == 0
            ? []
            : TrRmTargetProfileTableWriter.Encode([
                new TrRmTargetProfileRow(
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader
                        .WindowsX64NativeProfileRuleRevision),
            ], schema);
        var targetProfiles = fixedArrayLayoutRows.Count == 0
            ? new TrRmTargetProfileTableReader([], 0)
            : new TrRmTargetProfileTableReader(targetProfileBytes, 1, schema);
        var layouts = new TrRmLayoutTableReader([], 0, typeDefs, targetProfiles, 0);
        var nativeImportRows = new List<TrRmNativeImportRow>(
            nativeImportFunctionIndices.Count);
        foreach (var functionIndex in nativeImportFunctionIndices)
        {
            var nativeImport = program.Functions[functionIndex]
                .DirectNativeImport!;
            nativeImportRows.Add(new TrRmNativeImportRow(1,
                (byte)nativeImport.ResolutionMode,
                (byte)nativeImport.ResolutionPolicy,
                (byte)nativeImport.CallingConvention, functionIndex + 1,
                nativeImportLibraryNameRows[functionIndex],
                nativeImportEntryNameRows[functionIndex]));
        }

        var nativeImportBytes = TrRmNativeImportTableWriter.Encode(
            nativeImportRows, strings, methods, default, typeDefs, imports, modules,
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
            : CreateStandardBodyTypeMapToken(
                GetSignatureTypeSpecRef(signature.ReturnType));
        var unitCallStorageArgumentCount = storageCall is null
            ? 0
            : storageCall.UnitInitializerCalls.Sum(static call =>
                call.Call.Arguments.Count(static argument =>
                    argument is BoundNativeStorageAddress));
        var unitCallTemporaryHomes = storageCall is null
            ? []
            : GetNativeAddressUnitCallHomes(storageCall.UnitInitializerCalls,
                storageCall.ConditionCaptures,
                callerLocalTypeSpecRefs.Length);
        var typeMapTokenCapacity = storageCall is null
            ? 1 + (callResultHomeIndex >= 0 ? 1 : 0)
                + stagedResultHomeCount + callerParameterTypeSpecRefs.Length
            : callerLocalTypeSpecRefs.Length
                + unitCallTemporaryHomes.Length
                + unitCallStorageArgumentCount
                + storageCall.StorageArguments.Length
                + callerParameterTypeSpecRefs.Length;
        var typeMapTokens = new List<uint>(typeMapTokenCapacity);
        if (storageCall is null)
        {
            typeMapTokens.Add(0x1100_0001);
            if (callResultHomeIndex >= 0)
            {
                typeMapTokens.Add(CreateStandardBodyTypeMapToken(
                    GetSignatureTypeSpecRef(caller.ReturnType)));
            }
            if (stagedResultHomeCount > 0)
            {
                typeMapTokens.Add(stagedResultTypeMapToken);
            }

            foreach (var typeSpecRef in callerParameterTypeSpecRefs)
            {
                typeMapTokens.Add(
                    CreateStandardBodyTypeMapToken(typeSpecRef));
            }
        }
        else
        {
            for (var localOrdinal = 0;
                localOrdinal < callerLocalTypeSpecRefs.Length; localOrdinal++)
            {
                var variable = caller.Variables[
                    caller.ParameterCount + localOrdinal];
                typeMapTokens.Add(NativeFunctionPointerTypes.IsType(variable.Type)
                    ? 0x1100_0001u
                    : CreateStandardBodyTypeMapToken(
                        callerLocalTypeSpecRefs[localOrdinal]));
            }

            foreach (var temporaryHome in unitCallTemporaryHomes)
            {
                typeMapTokens.Add(CreateStandardBodyTypeMapToken(
                    GetSignatureTypeSpecRef(temporaryHome.Type)));
            }

            foreach (var unitCall in storageCall.UnitInitializerCalls)
            {
                for (var argumentIndex = 0;
                    argumentIndex < unitCall.Call.Arguments.Length;
                    argumentIndex++)
                {
                    if (unitCall.Call.Arguments[argumentIndex]
                        is not BoundNativeStorageAddress)
                    {
                        continue;
                    }

                    var parameterIndex = GetBoundCallParameterIndex(
                        unitCall.Call, argumentIndex);
                    typeMapTokens.Add(CreateStandardBodyTypeMapToken(
                        GetSignatureTypeSpecRef(
                            unitCall.Signature.ParameterTypes[
                                parameterIndex])));
                }
            }

            foreach (var storageArgument in storageCall.StorageArguments)
            {
                typeMapTokens.Add(CreateStandardBodyTypeMapToken(
                    GetSignatureTypeSpecRef(signature.ParameterTypes[
                        storageArgument.ParameterOrdinal])));
            }

            foreach (var typeSpecRef in callerParameterTypeSpecRefs)
            {
                typeMapTokens.Add(
                    CreateStandardBodyTypeMapToken(typeSpecRef));
            }

            for (var localOrdinal = 0;
                localOrdinal < callerLocalTypeSpecRefs.Length; localOrdinal++)
            {
                var variable = caller.Variables[
                    caller.ParameterCount + localOrdinal];
                if (!NativeFixedArrayTypes.IsType(variable.Type))
                {
                    continue;
                }

                var arrayType = program.NativeFixedArrayTypes[
                    NativeFixedArrayTypes.Index(variable.Type)];
                if (arrayType.ElementCount == 0)
                {
                    continue;
                }

                typeMapTokens.Add(CreateStandardBodyTypeMapToken(
                    GetSignatureTypeSpecRef(arrayType.ElementType)));
            }
        }

        var targetMethodMapToken = moduleImport is null
            ? 0x1600_0000u | (uint)targetMethodRow
            : 0x1C00_0001u;
        var methodMapTokens = new List<uint>();
        var methodMapTokenSet = new HashSet<uint>();
        if (storageCall is null)
        {
            methodMapTokens.Add(targetMethodMapToken);
        }
        else
        {
            var callerStatements = new List<BoundStatement>();
            if (!AppendStatements(caller.Body, callerStatements))
            {
                throw Unsupported(
                    "The F+S caller Body cannot be flattened for its MethodMap.");
            }

            void AddCallMethodMapTokens(BoundCall call)
            {
                foreach (var argument in call.Arguments)
                {
                    if (argument is BoundCall nestedCall)
                    {
                        AddCallMethodMapTokens(nestedCall);
                    }
                }

                var methodToken = 0x1600_0000u
                    | checked((uint)(call.Function + 1));
                if (methodMapTokenSet.Add(methodToken))
                {
                    methodMapTokens.Add(methodToken);
                }
            }

            foreach (var statement in callerStatements)
            {
                if (statement is BoundLocal
                    { Initializer: BoundNativeFunctionAddress })
                {
                    if (methodMapTokenSet.Add(targetMethodMapToken))
                    {
                        methodMapTokens.Add(targetMethodMapToken);
                    }
                }
            }

            foreach (var unitCall in storageCall.UnitInitializerCalls)
            {
                AddCallMethodMapTokens(unitCall.Call);
            }
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeMapTokens,
            methodMapTokens);
        byte[] body;
        if (storageCall is not null)
        {
            body = EncodeForeignInvocationStorageAddressBody(caller,
                program, storageCall, signature, maps, targetMethodMapToken,
                contractRows.Count, GetSignatureTypeSpecRef,
                fixedArrayLayoutRowByType, schema);
        }
        else
        {
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
                var resultTypeMapToken = CreateStandardBodyTypeMapToken(
                    GetSignatureTypeSpecRef(caller.ReturnType));
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
                var typeMapToken = CreateStandardBodyTypeMapToken(
                    callerParameterTypeSpecRefs[ordinal]);
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
        body = TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, default,
            [new TrRmRegisterBodyBlock(0, (uint)codeBytes.Length, 0)],
            records, codeBytes, contractRows.Count,
            schema);
        }
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, (uint)stringRows.Count, stringsBytes),
            new(0x0004, 0, (uint)featureRows.Count, featureBytes),
            new(0x0011, 0, (uint)typeSpecRows.Count, typeSpecBytes),
            new(0x0016, 0, (uint)methodRows.Count, methodBytes),
            new(0x0017, 0, (uint)signatureRows.Count, signatureBytes),
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
                    schema)));
        }
        if (fixedArrayLayoutRows.Count != 0)
        {
            var fixedArrayLayoutBytes
                = TrRmFixedArrayLayoutTableWriter.Encode(
                    fixedArrayLayoutRows, typeSpecs, targetProfiles,
                    featureReader, modules, schema);
            sections.Add(new TrRmSection(0x0012, 0,
                (uint)fixedArrayLayoutRows.Count, fixedArrayLayoutBytes));
            sections.Add(new TrRmSection(0x0048, 0, 1,
                targetProfileBytes));
        }
        sections.Add(new TrRmSection(0x0020, 0, 1,
            TrRmVariableTableWriter.EncodeBodies([body])));
        sections.Add(new TrRmSection(0x0021, 0,
            (uint)nativeImportRows.Count, nativeImportBytes));
        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));

        var artifact = TrRmArtifactWriter.Encode(sections,
            schema);
        var artifactIndex = schema.UsesForeignInvocationStorageFixedArrays()
            ? schema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayGenericCopyDraft(artifact)
                : schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact)
                : schema.UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                    ? TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                            artifact)
                    : TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyDraft(artifact)
            : schema.UsesForeignInvocationStorageAddresses()
                ? TrRmArtifactIndex.ForForeignInvocationStorageAddressDraft(artifact)
                : TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = schema.UsesForeignInvocationStorageFixedArrays()
            ? schema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(artifactIndex)
                : schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
                    ? TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                            artifactIndex)
                : schema.UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                    ? TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyEmptyInitializationDraftAndGetExecutionTables(
                            artifactIndex)
                    : TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                            artifactIndex)
            : schema.UsesForeignInvocationStorageAddresses()
                ? TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(artifactIndex)
                : TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationDraftAndGetExecutionTables(artifactIndex);
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

    private readonly record struct NativeAddressStorageArgument(
        int ParameterOrdinal, BoundNativeStorageAddress Address);

    private readonly record struct NativeAddressUnitInitializerCall(
        BoundCall Call, NativeFunctionPointerSignatureDefinition Signature,
        int ResultHome, ImmutableArray<int> ArgumentCaptureHomes);

    private readonly record struct NativeAddressUnitConditionCapture(
        BoundBinary Condition, int Home);

    private readonly record struct NativeAddressUnitCallHome(
        int Index, PrimitiveType Type);

    private sealed record NativeAddressStorageCallPlan(
        BoundIndirectCall Call, int TargetVariable, int ReturnVariable,
        ImmutableArray<NativeAddressStorageArgument> StorageArguments,
        ImmutableArray<NativeAddressUnitInitializerCall> UnitInitializerCalls,
        ImmutableArray<NativeAddressUnitConditionCapture> ConditionCaptures,
        int ConditionalEdgeCount,
        int ArrayInitializationIndexVariable);

    private static ImmutableArray<NativeAddressUnitCallHome>
        GetNativeAddressUnitCallHomes(
            ImmutableArray<NativeAddressUnitInitializerCall> calls,
            ImmutableArray<NativeAddressUnitConditionCapture> captures,
            int localCount)
    {
        var homesByIndex = new SortedDictionary<int, PrimitiveType>();
        foreach (var call in calls)
        {
            if (call.ResultHome >= localCount
                && !homesByIndex.TryAdd(call.ResultHome, call.Call.Type))
            {
                throw Unsupported(
                    "The A2EU direct-call result Home is reused.");
            }

            for (var argumentIndex = 0;
                argumentIndex < call.ArgumentCaptureHomes.Length;
                argumentIndex++)
            {
                var home = call.ArgumentCaptureHomes[argumentIndex];
                if (home >= localCount
                    && !homesByIndex.TryAdd(home,
                        call.Call.Arguments[argumentIndex].Type))
                {
                    throw Unsupported(
                        "The A2EU argument capture Home is reused.");
                }
            }
        }

        foreach (var capture in captures)
        {
            if (capture.Home >= localCount
                && !homesByIndex.TryAdd(capture.Home,
                    capture.Condition.Left.Type))
            {
                throw Unsupported(
                    "The A2EU condition capture Home is reused.");
            }
        }

        var result = ImmutableArray.CreateBuilder<NativeAddressUnitCallHome>(
            homesByIndex.Count);
        var expectedHome = localCount;
        foreach (var entry in homesByIndex)
        {
            if (entry.Key != expectedHome++)
            {
                throw Unsupported(
                    "The A2EU temporary Homes are not contiguous after caller locals.");
            }

            result.Add(new NativeAddressUnitCallHome(entry.Key, entry.Value));
        }

        return result.MoveToImmutable();
    }

    private static bool TryGetNativeAddressStorageCall(BoundProgram program,
        BoundFunction function, BoundNativeFunctionAddress address,
        NativeFunctionPointerSignatureDefinition signature,
        TrRmArtifactSchema schema,
        out NativeAddressStorageCallPlan? plan)
    {
        plan = null;
        var statements = new List<BoundStatement>();
        if (!AppendStatements(function.Body, statements)
            || statements.Count < 4
            || statements[^1] is not BoundReturn
                { Value: BoundRead returnRead }
            || returnRead.Type != function.ReturnType
            || (uint)returnRead.Variable >= (uint)function.Variables.Length)
        {
            return false;
        }

        var callStatements = statements
            .Select((statement, index) => (statement, index))
            .Where(static item => item.statement is BoundExpressionStatement
                { Expression: BoundIndirectCall })
            .ToArray();
        if (callStatements.Length != 1
            || callStatements[0].statement is not BoundExpressionStatement
                { Expression: BoundIndirectCall call }
            || callStatements[0].index != statements.Count - 2
            || call.Target is not BoundRead targetRead
            || (uint)targetRead.Variable >= (uint)function.Variables.Length
            || targetRead.Type != address.Type
            || call.Arguments.Length != signature.ParameterTypes.Length
            || !call.IsUnsafeAuthorized
            || call.Type != signature.ReturnType
            || !NativeFunctionPointerTypes.IsType(address.Type))
        {
            return false;
        }

        var targetLocals = statements
            .Select(static statement => statement as BoundLocal)
            .Where(static local => local is not null
                && local.Initializer is BoundNativeFunctionAddress)
            .Cast<BoundLocal>()
            .ToArray();
        if (targetLocals.Length != 1
            || targetLocals[0].Initializer is not BoundNativeFunctionAddress
                { } localAddress
            || localAddress != address
            || targetLocals[0].Variable != targetRead.Variable
            || targetRead.Variable < function.ParameterCount
            || targetRead.Variable >= function.Variables.Length
            || call.Arguments.Any(static argument =>
                argument is BoundNativeOutputReservation))
        {
            return false;
        }

        var topLevelLocalDeclarations = statements
            .Take(callStatements[0].index)
            .OfType<BoundLocal>()
            .ToArray();
        var localDeclarations = new List<BoundLocal>();
        void CollectLocalDeclarations(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundLocal local:
                    localDeclarations.Add(local);
                    break;

                case BoundBlock block:
                    foreach (var nested in block.Statements)
                    {
                        CollectLocalDeclarations(nested);
                    }
                    break;

                case BoundIf branch:
                    CollectLocalDeclarations(branch.Then);
                    if (branch.Else is not null)
                    {
                        CollectLocalDeclarations(branch.Else);
                    }
                    break;

                case BoundUnsafe unsafeBlock:
                    CollectLocalDeclarations(unsafeBlock.Body);
                    break;
            }
        }

        foreach (var statement in statements.Take(callStatements[0].index))
        {
            CollectLocalDeclarations(statement);
        }

        if (localDeclarations.Count
                != function.Variables.Length - function.ParameterCount
            || statements.Take(callStatements[0].index)
                .Any(static statement => statement is not BoundLocal
                    and not BoundIf
                    and not BoundExpressionStatement
                        { Expression: BoundCall }
                    and not BoundExpressionStatement
                        { Expression: BoundConditional
                            { Type: PrimitiveType.Void } }))
        {
            return false;
        }

        var localVariables = new HashSet<int>();
        foreach (var declaration in localDeclarations)
        {
            if (declaration.Variable < function.ParameterCount
                || declaration.Variable >= function.Variables.Length
                || !localVariables.Add(declaration.Variable))
            {
                return false;
            }

            var variable = function.Variables[declaration.Variable];
            if (variable.IsOut || variable.IsInit || variable.IsImplicitRef
                || variable.IsImplicitReferenceTemporary
                || variable.IsInitializationLocal)
            {
                return false;
            }

            if (declaration.Variable == targetRead.Variable)
            {
                if (declaration.Initializer is not BoundNativeFunctionAddress
                    { } nativeAddress
                    || nativeAddress != address
                    || variable.Type != address.Type)
                {
                    return false;
                }
            }
            else if (NativeFixedArrayTypes.IsType(variable.Type))
            {
                if (schema is not (
                        TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft)
                    || !NativeFixedArrayContract.TryGetLayout(variable.Type,
                        program.Structures, program.NativeFixedArrayTypes,
                        out var arrayLayout)
                    || (arrayLayout.ElementCount != 0
                        && !variable.IsMutable)
                    || declaration.Initializer
                        is not BoundNativeFixedArrayInitialization arrayInitialization
                    || !IsSupportedNativeFixedArrayInitialization(
                        variable.Type, arrayInitialization, program, schema))
                {
                    return false;
                }
            }
            else if (declaration.Initializer is BoundRead read
                && read.Variable != declaration.Variable
                && (uint)read.Variable < (uint)function.Variables.Length
                && read.Type == variable.Type
                && read.Type == function.Variables[read.Variable].Type
                && (DirectNativeImportContract.IsScalar(read.Type)
                    || NativePointerTypes.IsType(read.Type))
                && (read.Variable < function.ParameterCount
                    || localVariables.Contains(read.Variable)))
            {
            }
            else if (declaration.Initializer is BoundCall localCall
                && localCall.Type == variable.Type
                && DirectNativeImportContract.IsScalar(variable.Type))
            {
            }
            else if (declaration.Initializer is BoundBinary scalarCheck
                && schema
                    .UsesForeignInvocationStorageFixedArrayUnitInitialization()
                && scalarCheck.Type == variable.Type
                && IsSupportedNativeUnitInitializerScalarCheck(scalarCheck)
                && scalarCheck.Left is BoundRead leftRead
                && scalarCheck.Right is BoundRead rightRead
                && leftRead.Variable != declaration.Variable
                && rightRead.Variable != declaration.Variable
                && (leftRead.Variable < function.ParameterCount
                    || localVariables.Contains(leftRead.Variable))
                && (rightRead.Variable < function.ParameterCount
                    || localVariables.Contains(rightRead.Variable)))
            {
            }
            else if (declaration.Initializer is not BoundConstant constant
                || constant.Type != variable.Type
                || !TryGetContextualInitializationRegister(constant,
                    variable.Type, schema, out _))
            {
                return false;
            }
        }

        if (returnRead.Variable < function.ParameterCount
            || !localVariables.Contains(returnRead.Variable)
            || returnRead.Variable == targetRead.Variable
            || function.Variables[returnRead.Variable].Type != function.ReturnType)
        {
            return false;
        }

        var storageArguments = ImmutableArray.CreateBuilder<
            NativeAddressStorageArgument>();
        for (var ordinal = 0; ordinal < call.Arguments.Length; ordinal++)
        {
            var argument = call.Arguments[ordinal];
            if (argument is BoundNativeStorageAddress storageAddress)
            {
                var nativeParameter = signature.NativeParameters
                    .FirstOrDefault(parameter => parameter.ParameterIndex == ordinal);
                var isFixedArray
                    = NativeFixedArrayTypes.IsType(storageAddress.CarrierType);
                var isSupportedStorageType = isFixedArray
                    ? (schema is
                        TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft)
                        && NativeFixedArrayContract.TryGetLayout(
                            storageAddress.CarrierType,
                            program.Structures, program.NativeFixedArrayTypes,
                            out var arrayLayout)
                        && NativeFixedArrayContract.MatchesType(
                            storageAddress.CarrierType, arrayLayout, program)
                        && NativeFixedArrayContract.MatchesLayout(
                            arrayLayout, program)
                        && storageAddress.ElementType == arrayLayout.ElementType
                        && storageAddress.ElementCount == arrayLayout.ElementCount
                        && storageAddress.ElementStride == arrayLayout.ElementStride
                        && arrayLayout.ElementCount > 0
                        && arrayLayout.ElementStride > 0
                        && arrayLayout.ByteExtent > 0
                        && storageAddress.ByteExtent > 0
                        && storageAddress.ByteExtent
                            % storageAddress.ElementStride == 0
                        && storageAddress.Index is BoundRead indexRead
                        && (uint)indexRead.Variable
                            < (uint)function.Variables.Length
                        && (indexRead.Variable < function.ParameterCount
                            || localVariables.Contains(indexRead.Variable))
                        && indexRead.Type
                            == function.Variables[indexRead.Variable].Type
                        && NativeFixedArrayContract.IsIndexType(indexRead.Type)
                    : storageAddress.CarrierType
                            == storageAddress.ElementType
                        && storageAddress.ElementCount == 1
                        && storageAddress.ElementStride > 0
                        && storageAddress.ByteExtent
                            == storageAddress.ElementStride;
                if (storageAddress.Variable < function.ParameterCount
                    || !localVariables.Contains(storageAddress.Variable)
                    || storageAddress.Variable == targetRead.Variable
                    || !isSupportedStorageType
                    || nativeParameter is null
                    || !DirectNativeImportContract.MatchesAddress(
                        storageAddress, nativeParameter, function, program)
                    || !NativePointerTypes.IsType(signature.ParameterTypes[ordinal]))
                {
                    return false;
                }

                storageArguments.Add(new NativeAddressStorageArgument(
                    ordinal, storageAddress));
            }
            else if (argument is BoundRead read
                && (uint)read.Variable < (uint)function.Variables.Length
                && read.Type == function.Variables[read.Variable].Type
                && read.Type == signature.ParameterTypes[ordinal])
            {
            }
            else
            {
                return false;
            }
        }

        if (storageArguments.Count == 0
            || !storageArguments.Any(argument =>
                argument.Address.Variable == returnRead.Variable))
        {
            return false;
        }

        var unitInitializerCalls
            = ImmutableArray.CreateBuilder<NativeAddressUnitInitializerCall>();
        var conditionCaptures
            = ImmutableArray.CreateBuilder<NativeAddressUnitConditionCapture>();
        var nextUnitCallResultHome
            = function.Variables.Length - function.ParameterCount;
        var conditionalEdgeCount = 0;
        bool AddUnitInitializerCall(BoundCall unitCall,
            int initializerArrayVariable, int resultVariable)
        {
            var argumentCaptureHomes = new int[unitCall.Arguments.Length];
            Array.Fill(argumentCaptureHomes, -1);
            for (var argumentIndex = 0;
                argumentIndex < unitCall.Arguments.Length;
                argumentIndex++)
            {
                var argument = unitCall.Arguments[argumentIndex];
                if (argument is BoundRead read
                    && read.Type != PrimitiveType.Void
                    && unitCall.Arguments
                        .Skip(argumentIndex + 1)
                        .Any(static candidate => candidate is BoundCall))
                {
                    if (!DirectNativeImportContract.IsScalar(read.Type)
                        && !NativePointerTypes.IsType(read.Type))
                    {
                        return false;
                    }

                    argumentCaptureHomes[argumentIndex]
                        = nextUnitCallResultHome++;
                }

                if (schema
                        .UsesForeignInvocationStorageFixedArrayUnitInitialization()
                    && IsSupportedNativeUnitInitializerScalarCheck(argument))
                {
                    argumentCaptureHomes[argumentIndex]
                        = nextUnitCallResultHome++;
                }

                if (argument is BoundCall nestedCall
                    && !AddUnitInitializerCall(nestedCall,
                        initializerArrayVariable, -1))
                {
                    return false;
                }
            }

            if ((uint)unitCall.Function >= (uint)program.Functions.Length)
            {
                return false;
            }

            var unitCallTarget = program.Functions[unitCall.Function];
            if (!NativeFunctionPointerContract.TryCreateNativeImportSignature(
                    unitCallTarget,
                    program.NativeFunctionPointerSignatures,
                    out var unitCallSignature)
                || unitCallSignature is null
                || !IsSupportedNativeUnitInitializerCall(unitCall,
                    unitCallTarget, unitCallSignature, function, program,
                    localVariables, initializerArrayVariable, schema))
            {
                return false;
            }

            var resultHome = -1;
            if (unitCall.Type != PrimitiveType.Void)
            {
                if (!DirectNativeImportContract.IsScalar(unitCall.Type))
                {
                    return false;
                }

                resultHome = resultVariable >= 0
                    ? resultVariable - function.ParameterCount
                    : nextUnitCallResultHome++;
            }
            else if (resultVariable >= 0)
            {
                return false;
            }

            unitInitializerCalls.Add(new NativeAddressUnitInitializerCall(
                unitCall, unitCallSignature, resultHome,
                [.. argumentCaptureHomes]));
            return true;
        }

        bool AddUnitInitializerExpression(BoundExpression expression,
            int initializerArrayVariable)
        {
            return expression switch {
                BoundCall unitCall => unitCall.Type == PrimitiveType.Void
                    && AddUnitInitializerCall(unitCall,
                        initializerArrayVariable, -1),
                BoundConstant {
                    Type: PrimitiveType.Void,
                    Bits.Low: 0,
                    Bits.High: 0,
                } => true,
                BoundConditional conditional
                    when conditional.Type == PrimitiveType.Void
                        && IsSupportedNativeUnitConditionalCondition(
                            conditional.Condition) =>
                    AddUnitConditionalControlFlow(
                        conditional.Condition,
                        () => AddUnitInitializerExpression(
                            conditional.WhenTrue,
                            initializerArrayVariable),
                        () => AddUnitInitializerExpression(
                            conditional.WhenFalse,
                            initializerArrayVariable)),
                _ => false,
            };
        }

        bool AddUnitConditionalControlFlow(BoundExpression condition,
            Func<bool> appendTrue, Func<bool> appendFalse)
        {
            if (!IsSupportedNativeUnitConditionalCondition(condition))
            {
                return false;
            }

            if (TryGetNativeAddressKnownBoolean(condition, out var selected))
            {
                return selected ? appendTrue() : appendFalse();
            }

            switch (condition)
            {
                case BoundRead read
                    when (uint)read.Variable
                            < (uint)function.Variables.Length
                        && read.Type == PrimitiveType.Bool
                        && (read.Variable < function.ParameterCount
                            || localVariables.Contains(read.Variable)):
                    conditionalEdgeCount++;
                    return appendTrue() && appendFalse();

                case BoundCall conditionCall
                    when conditionCall.Type == PrimitiveType.Bool:
                    if (!AddUnitInitializerCall(conditionCall, -1, -1))
                    {
                        return false;
                    }

                    conditionalEdgeCount++;
                    return appendTrue() && appendFalse();

                case BoundBinary comparison
                    when IsSupportedNativeUnitConditionalComparison(
                            comparison)
                        || IsSupportedNativeUnitConditionalBooleanXor(
                            comparison):
                    if (comparison.Left is BoundRead leftRead
                        && ((uint)leftRead.Variable
                                >= (uint)function.Variables.Length
                            || (leftRead.Variable >= function.ParameterCount
                                && !localVariables.Contains(leftRead.Variable))))
                    {
                        return false;
                    }

                    if (comparison.Right is BoundRead rightRead
                        && ((uint)rightRead.Variable
                                >= (uint)function.Variables.Length
                            || (rightRead.Variable >= function.ParameterCount
                                && !localVariables.Contains(rightRead.Variable))))
                    {
                        return false;
                    }

                    if (comparison.Left is BoundRead
                        && comparison.Right is BoundCall)
                    {
                        conditionCaptures.Add(
                            new NativeAddressUnitConditionCapture(
                                comparison, nextUnitCallResultHome++));
                    }

                    if (comparison.Left is BoundCall leftCall
                        && !AddUnitInitializerCall(leftCall, -1, -1))
                    {
                        return false;
                    }

                    if (comparison.Right is BoundCall rightCall
                        && !AddUnitInitializerCall(rightCall, -1, -1))
                    {
                        return false;
                    }

                    conditionalEdgeCount++;
                    return appendTrue() && appendFalse();

                case BoundUnary {
                    Operator: TokenKind.Bang,
                    Type: PrimitiveType.Bool,
                    Operand: var operand,
                }:
                    return AddUnitConditionalControlFlow(operand,
                        appendFalse, appendTrue);

                case BoundBinary {
                    Operator: TokenKind.AmpersandAmpersand
                        or TokenKind.PipePipe,
                    Type: PrimitiveType.Bool,
                    Left: var left,
                    Right: var right,
                }:
                    return condition is BoundBinary {
                        Operator: TokenKind.AmpersandAmpersand,
                    }
                        ? AddUnitConditionalControlFlow(left,
                            () => AddUnitConditionalControlFlow(right,
                                appendTrue, appendFalse), appendFalse)
                        : AddUnitConditionalControlFlow(left, appendTrue,
                            () => AddUnitConditionalControlFlow(right,
                                appendTrue, appendFalse));

                case BoundConditional {
                    Type: PrimitiveType.Bool,
                    Condition: var nestedCondition,
                    WhenTrue: var whenTrue,
                    WhenFalse: var whenFalse,
                }:
                    return AddUnitConditionalControlFlow(nestedCondition,
                        () => AddUnitConditionalControlFlow(whenTrue,
                            appendTrue, appendFalse),
                        () => AddUnitConditionalControlFlow(whenFalse,
                            appendTrue, appendFalse));

                default:
                    return false;
            }
        }

        bool AddUnitInitializerStatement(BoundStatement statement)
        {
            switch (statement)
            {
            case BoundBlock block when block.RetiredManagedLocals.IsEmpty:
                foreach (var nested in block.Statements)
                {
                    if (!AddUnitInitializerStatement(nested))
                    {
                        return false;
                    }
                }
                return true;

            case BoundLocal local:
                if (local.Initializer is BoundCall localCall)
                {
                    return AddUnitInitializerCall(localCall, -1,
                        local.Variable);
                }

                if (local.Initializer
                    is BoundNativeFixedArrayInitialization initialization)
                {
                    foreach (var element in initialization.Elements)
                    {
                        if (element is BoundCall initializerCall
                            && !AddUnitInitializerCall(initializerCall,
                                local.Variable, -1))
                        {
                            return false;
                        }
                        if (element is BoundConditional conditional
                            && !AddUnitInitializerExpression(conditional,
                                local.Variable))
                        {
                            return false;
                        }
                    }
                }
                return true;

            case BoundExpressionStatement
                { Expression: BoundCall call }:
                return call.Type == PrimitiveType.Void
                    && AddUnitInitializerCall(call, -1, -1);

            case BoundExpressionStatement
                { Expression: BoundConditional conditional }:
                return AddUnitInitializerExpression(conditional, -1);

            case BoundIf branch:
                return AddUnitConditionalControlFlow(branch.Condition,
                    () => AddUnitInitializerStatement(branch.Then),
                    () => branch.Else is null
                        || AddUnitInitializerStatement(branch.Else));

            default:
                return false;
            }
        }

        foreach (var statement in statements.Take(callStatements[0].index))
        {
            if (!AddUnitInitializerStatement(statement))
            {
            return false;
            }
        }

        var hasPositiveArrayLocal = topLevelLocalDeclarations.Any(declaration =>
            NativeFixedArrayTypes.IsType(
                function.Variables[declaration.Variable].Type)
            && program.NativeFixedArrayTypes[NativeFixedArrayTypes.Index(
                function.Variables[declaration.Variable].Type)]
                .ElementCount != 0);
        var arrayInitializationIndexVariable = -1;
        if (hasPositiveArrayLocal)
        {
            var localDeclarationIndex = topLevelLocalDeclarations
                .Select((declaration, index) => (declaration, index))
                .ToDictionary(static item => item.declaration.Variable,
                    static item => item.index);
            foreach (var declaration in topLevelLocalDeclarations)
            {
                var variable = function.Variables[declaration.Variable];
                if (NativeFixedArrayTypes.IsType(variable.Type))
                {
                    continue;
                }

                if (declaration.Initializer is BoundConstant constant
                    && variable.Type == PrimitiveType.Int32
                    && constant.Bits.Low == 0 && constant.Bits.High == 0)
                {
                    arrayInitializationIndexVariable = declaration.Variable;
                    break;
                }
            }

            if (arrayInitializationIndexVariable < 0
                || storageArguments.Any(argument =>
                    NativeFixedArrayTypes.IsType(
                        argument.Address.CarrierType)
                    && argument.Address.Index is not BoundRead))
            {
                return false;
            }

            var initializationIndex = localDeclarationIndex[
                arrayInitializationIndexVariable];
            if (topLevelLocalDeclarations
                .Select((declaration, index) => (declaration, index))
                .Any(item => NativeFixedArrayTypes.IsType(
                        function.Variables[item.declaration.Variable].Type)
                    && program.NativeFixedArrayTypes[
                        NativeFixedArrayTypes.Index(
                            function.Variables[item.declaration.Variable].Type)]
                        .ElementCount != 0
                    && item.index < initializationIndex))
            {
                return false;
            }
        }

        plan = new NativeAddressStorageCallPlan(call, targetRead.Variable,
            returnRead.Variable, storageArguments.ToImmutable(),
            unitInitializerCalls.ToImmutable(),
            conditionCaptures.ToImmutable(),
            conditionalEdgeCount,
            arrayInitializationIndexVariable);
        return true;
    }

    private static bool IsSupportedNativeUnitInitializerCall(
        BoundCall call, BoundFunction target,
        NativeFunctionPointerSignatureDefinition signature,
        BoundFunction caller, BoundProgram program,
        HashSet<int> localVariables, int initializerArrayVariable,
        TrRmArtifactSchema schema)
    {
        if (target.DirectNativeImport is null
            || target.ModuleImport is not null
            || !target.IsExternal
            || target.DoesNotReturn
            || !target.DirectNativeImport.Outputs.IsEmpty
            || !DirectNativeImportContract.IsValid(target,
                program.Structures, program.NativeFunctionPointerSignatures,
                program.NativePointerTypes)
            || (target.ReturnType != PrimitiveType.Void
                && !DirectNativeImportContract.IsScalar(target.ReturnType))
            || call.Type != target.ReturnType
            || signature.ReturnType != target.ReturnType
            || !call.IsUnsafeAuthorized
            || call.Arguments.Length != signature.ParameterTypes.Length
            || call.Arguments.Length != target.ParameterCount
            || (!call.ArgumentParameterIndices.IsDefault
                && call.ArgumentParameterIndices.Length
                    != call.Arguments.Length))
        {
            return false;
        }

        var seenParameters = new bool[signature.ParameterTypes.Length];
        for (var argumentIndex = 0;
            argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var parameterIndex = call.ArgumentParameterIndices.IsDefault
                ? argumentIndex
                : call.ArgumentParameterIndices[argumentIndex];
            if ((uint)parameterIndex >= (uint)seenParameters.Length
                || seenParameters[parameterIndex])
            {
                return false;
            }

            seenParameters[parameterIndex] = true;
            var argument = call.Arguments[argumentIndex];
            var parameterType = signature.ParameterTypes[parameterIndex];
            if (argument is BoundNativeStorageAddress storageAddress)
            {
                var nativeParameter = signature.NativeParameters
                    .FirstOrDefault(parameter =>
                        parameter.ParameterIndex == parameterIndex);
                var isFixedArray
                    = NativeFixedArrayTypes.IsType(
                        storageAddress.CarrierType);
                var isSupportedStorageType = isFixedArray
                    ? NativeFixedArrayContract.TryGetLayout(
                        storageAddress.CarrierType, program.Structures,
                        program.NativeFixedArrayTypes, out var arrayLayout)
                        && NativeFixedArrayContract.MatchesType(
                            storageAddress.CarrierType, arrayLayout, program)
                        && NativeFixedArrayContract.MatchesLayout(
                            arrayLayout, program)
                        && storageAddress.ElementType
                            == arrayLayout.ElementType
                        && storageAddress.ElementCount
                            == arrayLayout.ElementCount
                        && storageAddress.ElementStride
                            == arrayLayout.ElementStride
                        && arrayLayout.ElementCount > 0
                        && arrayLayout.ElementStride > 0
                        && arrayLayout.ByteExtent > 0
                        && storageAddress.ByteExtent > 0
                        && storageAddress.ByteExtent
                            % storageAddress.ElementStride == 0
                        && storageAddress.Index is BoundRead indexRead
                        && (uint)indexRead.Variable
                            < (uint)caller.Variables.Length
                        && (indexRead.Variable < caller.ParameterCount
                            || localVariables.Contains(indexRead.Variable))
                        && indexRead.Type
                            == caller.Variables[indexRead.Variable].Type
                        && NativeFixedArrayContract.IsIndexType(indexRead.Type)
                    : storageAddress.CarrierType
                            == storageAddress.ElementType
                        && storageAddress.ElementCount == 1
                        && storageAddress.ElementStride > 0
                        && storageAddress.ByteExtent
                            == storageAddress.ElementStride;
                if (storageAddress.Variable < caller.ParameterCount
                    || !localVariables.Contains(storageAddress.Variable)
                    || storageAddress.Variable == initializerArrayVariable
                    || !isSupportedStorageType
                    || nativeParameter is null
                    || !DirectNativeImportContract.MatchesAddress(
                        storageAddress, nativeParameter, caller, program)
                    || !NativePointerTypes.IsType(parameterType))
                {
                    return false;
                }
            }
            else if (argument is BoundRead read
                && (uint)read.Variable < (uint)caller.Variables.Length
                && read.Type == caller.Variables[read.Variable].Type
                && signature.NativeParameters.All(parameter =>
                    parameter.ParameterIndex != parameterIndex)
                && read.Type == parameterType)
            {
            }
            else if (argument is BoundCall nestedCall
                && nestedCall.Type == parameterType
                && (uint)nestedCall.Function
                    < (uint)program.Functions.Length
                && NativeFunctionPointerContract
                    .TryCreateNativeImportSignature(
                        program.Functions[nestedCall.Function],
                        program.NativeFunctionPointerSignatures,
                        out var nestedSignature)
                && nestedSignature is not null
                && IsSupportedNativeUnitInitializerCall(nestedCall,
                    program.Functions[nestedCall.Function], nestedSignature,
                    caller, program, localVariables,
                    initializerArrayVariable, schema))
            {
            }
            else if (argument is BoundBinary scalarCheck
                && scalarCheck.Type == parameterType
                && schema
                    .UsesForeignInvocationStorageFixedArrayUnitInitialization()
                && IsSupportedNativeUnitInitializerScalarCheck(scalarCheck)
                && signature.NativeParameters.All(parameter =>
                    parameter.ParameterIndex != parameterIndex))
            {
            }
            else if (argument is BoundConstant constant
                && (constant.Type == PrimitiveType.Void
                    ? parameterType == PrimitiveType.Void
                    : TryGetContextualInitializationRegister(constant,
                        parameterType, schema, out _)))
            {
            }
            else
            {
                return false;
            }
        }

        return seenParameters.All(static seen => seen);
    }

    private static bool IsSupportedNativeUnitInitializerScalarCheck(
        BoundExpression expression)
        => expression is BoundBinary {
            Operator: TokenKind.Slash or TokenKind.Percent,
            Type: PrimitiveType.Int32 or PrimitiveType.UInt32,
            Left: BoundRead { Type: var leftType },
            Right: BoundRead { Type: var rightType },
            OverflowBehavior: BoundIntegerOverflowBehavior.Checked,
        } binary && leftType == binary.Type && rightType == binary.Type;

    private static int GetBoundCallParameterIndex(BoundCall call,
        int argumentIndex)
        => call.ArgumentParameterIndices.IsDefault
            ? argumentIndex : call.ArgumentParameterIndices[argumentIndex];

    private static bool IsSupportedNativeFixedArrayInitialization(
        PrimitiveType arrayType,
        BoundNativeFixedArrayInitialization initialization,
        BoundProgram program, TrRmArtifactSchema schema)
    {
        if (!NativeFixedArrayContract.TryGetLayout(arrayType,
                program.Structures, program.NativeFixedArrayTypes,
                out var layout)
            || !NativeFixedArrayContract.MatchesType(arrayType, layout, program)
            || !NativeFixedArrayContract.MatchesLayout(layout, program)
            || initialization.Layout != layout
            || initialization.Elements.IsDefault)
        {
            return false;
        }

        if (layout.ElementCount == 0)
        {
            return schema
                    .UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                && initialization.Elements.IsEmpty
                && layout.ByteExtent == 0;
        }

        if ((ulong)initialization.Elements.Length != layout.ElementCount)
        {
            return false;
        }

        foreach (var element in initialization.Elements)
        {
            if (element is BoundConstant constant
                && TryGetContextualInitializationRegister(constant,
                    layout.ElementType, schema, out _))
            {
                continue;
            }

            if (layout.ElementType == PrimitiveType.Void
                && schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
                && element is BoundCall { Type: PrimitiveType.Void })
            {
                continue;
            }

            if (layout.ElementType == PrimitiveType.Void
                && schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
                && IsSupportedNativeUnitConditionalExpression(element))
            {
                continue;
            }

            if (!schema
                    .UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                || !NativeFixedArrayTypes.IsType(layout.ElementType)
                || element is not BoundNativeFixedArrayInitialization
                    emptyInitialization
                || !NativeFixedArrayContract.TryGetLayout(
                    layout.ElementType, program.Structures,
                    program.NativeFixedArrayTypes, out var elementLayout)
                || !NativeFixedArrayContract.MatchesType(
                    layout.ElementType, elementLayout, program)
                || !NativeFixedArrayContract.MatchesLayout(
                    elementLayout, program)
                || emptyInitialization.Layout != elementLayout
                || elementLayout.ElementCount != 0
                || elementLayout.ByteExtent != 0
                || emptyInitialization.Elements.IsDefault
                || !emptyInitialization.Elements.IsEmpty)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSupportedNativeUnitConditionalExpression(
        BoundExpression expression)
    {
        switch (expression)
        {
            case BoundConstant {
                Type: PrimitiveType.Void,
                Bits.Low: 0,
                Bits.High: 0,
            }:
            case BoundCall { Type: PrimitiveType.Void }:
                return true;

            case BoundConditional {
                Type: PrimitiveType.Void,
                Condition: var condition,
                WhenTrue: var whenTrue,
                WhenFalse: var whenFalse,
            }:
                if (!IsSupportedNativeUnitConditionalCondition(condition))
                {
                    return false;
                }

                if (TryGetNativeAddressKnownBoolean(condition,
                        out var selected))
                {
                    return IsSupportedNativeUnitConditionalExpression(
                        selected ? whenTrue : whenFalse);
                }

                return IsSupportedNativeUnitConditionalExpression(whenTrue)
                    && IsSupportedNativeUnitConditionalExpression(whenFalse);

            default:
                return false;
        }
    }

    private static bool IsSupportedNativeUnitConditionalCondition(
        BoundExpression condition)
    {
        if (condition.Type != PrimitiveType.Bool)
        {
            return false;
        }

        if (TryGetNativeAddressKnownBoolean(condition, out _))
        {
            return true;
        }

        return condition switch {
            BoundRead { Type: PrimitiveType.Bool } => true,
            BoundCall { Type: PrimitiveType.Bool } => true,
            BoundUnary {
                Operator: TokenKind.Bang,
                Type: PrimitiveType.Bool,
                Operand: var operand,
            } => IsSupportedNativeUnitConditionalCondition(operand),
            BoundBinary {
                Operator: TokenKind.AmpersandAmpersand
                    or TokenKind.PipePipe,
                Type: PrimitiveType.Bool,
                Left: var left,
                Right: var right,
            } => IsSupportedNativeUnitConditionalCondition(left)
                && IsSupportedNativeUnitConditionalCondition(right),
            BoundBinary comparison =>
                IsSupportedNativeUnitConditionalComparison(comparison)
                || IsSupportedNativeUnitConditionalBooleanXor(comparison),
            BoundConditional {
                Type: PrimitiveType.Bool,
                Condition: var nestedCondition,
                WhenTrue: var whenTrue,
                WhenFalse: var whenFalse,
            } => IsSupportedNativeUnitConditionalCondition(nestedCondition)
                && IsSupportedNativeUnitConditionalCondition(whenTrue)
                && IsSupportedNativeUnitConditionalCondition(whenFalse),
            _ => false,
        };
    }

    private static bool IsSupportedNativeUnitConditionalComparison(
        BoundBinary comparison)
    {
        if (comparison.Type != PrimitiveType.Bool
            || comparison.Left.Type != comparison.Right.Type)
        {
            return false;
        }

        if (comparison.Left.Type == PrimitiveType.Bool)
        {
            var isEquality = comparison.Operator is TokenKind.EqualsEquals
                or TokenKind.BangEquals;
            return isEquality
                && IsSupportedNativeUnitBooleanComparisonOperand(comparison.Left)
                && IsSupportedNativeUnitBooleanComparisonOperand(
                    comparison.Right);
        }

        if (comparison.Left is not (BoundRead or BoundCall)
            || comparison.Right is not (BoundRead or BoundCall))
        {
            return false;
        }

        return DirectNativeImportContract.IsScalar(comparison.Left.Type)
            && comparison.Operator is TokenKind.EqualsEquals
                or TokenKind.BangEquals or TokenKind.Less
                or TokenKind.LessEquals or TokenKind.Greater
                or TokenKind.GreaterEquals;
    }

    private static bool IsSupportedNativeUnitConditionalBooleanXor(
        BoundBinary binary)
        => binary.Operator == TokenKind.Caret
            && binary.Type == PrimitiveType.Bool
            && binary.Left.Type == PrimitiveType.Bool
            && binary.Right.Type == PrimitiveType.Bool
            && (binary.Left is BoundRead || binary.Left is BoundCall)
            && (binary.Right is BoundRead || binary.Right is BoundCall);

    private static bool IsSupportedNativeUnitBooleanComparisonOperand(
        BoundExpression expression)
        => expression is BoundRead or BoundCall or BoundConstant {
                Type: PrimitiveType.Bool,
                Bits.Low: <= 1,
                Bits.High: 0,
            };

    private static bool TryGetNativeAddressBooleanLiteralComparison(
        BoundBinary comparison, out BoundExpression operand, out bool negate)
    {
        operand = null!;
        negate = false;
        if (comparison.Type != PrimitiveType.Bool
            || comparison.Left.Type != PrimitiveType.Bool
            || comparison.Right.Type != PrimitiveType.Bool
            || comparison.Operator is not (
                TokenKind.EqualsEquals or TokenKind.BangEquals))
        {
            return false;
        }

        BoundConstant literal;
        if (comparison.Left is BoundConstant leftLiteral
            && comparison.Right is BoundRead or BoundCall)
        {
            literal = leftLiteral;
            operand = comparison.Right;
        }
        else if (comparison.Right is BoundConstant rightLiteral
            && comparison.Left is BoundRead or BoundCall)
        {
            literal = rightLiteral;
            operand = comparison.Left;
        }
        else
        {
            return false;
        }

        var literalValue = literal.Bits.Low == 1;
        negate = comparison.Operator == TokenKind.EqualsEquals
            ? !literalValue : literalValue;
        return true;
    }

    private static bool TryGetNativeAddressKnownBoolean(
        BoundExpression expression, out bool value)
    {
        switch (expression)
        {
            case BoundConstant {
                Type: PrimitiveType.Bool,
            } constant:
                value = constant.Bits.Low != 0;
                return true;

            case BoundUnary {
                Operator: TokenKind.Bang,
                Type: PrimitiveType.Bool,
                Operand: var operand,
            } when TryGetNativeAddressKnownBoolean(operand,
                out var operandValue):
                value = !operandValue;
                return true;

            case BoundBinary {
                Operator: TokenKind.AmpersandAmpersand,
                Type: PrimitiveType.Bool,
                Left: var left,
                Right: var right,
            } when TryGetNativeAddressKnownBoolean(left, out var andLeft):
                if (!andLeft)
                {
                    value = false;
                    return true;
                }
                if (TryGetNativeAddressKnownBoolean(right, out var andRight))
                {
                    value = andRight;
                    return true;
                }
                break;

            case BoundBinary {
                Operator: TokenKind.PipePipe,
                Type: PrimitiveType.Bool,
                Left: var left,
                Right: var right,
            } when TryGetNativeAddressKnownBoolean(left, out var orLeft):
                if (orLeft)
                {
                    value = true;
                    return true;
                }
                if (TryGetNativeAddressKnownBoolean(right, out var orRight))
                {
                    value = orRight;
                    return true;
                }
                break;

            case BoundConditional {
                Type: PrimitiveType.Bool,
                Condition: var condition,
                WhenTrue: var whenTrue,
                WhenFalse: var whenFalse,
            } when TryGetNativeAddressKnownBoolean(condition,
                out var selected):
                return TryGetNativeAddressKnownBoolean(
                    selected ? whenTrue : whenFalse, out value);
        }

        value = false;
        return false;
    }

    private static bool TryGetContextualInitializationRegister(
        BoundConstant constant, PrimitiveType type,
        TrRmArtifactSchema schema, out byte register)
    {
        register = 0;
        if (constant.Type != type)
        {
            return false;
        }

        if (type == PrimitiveType.Void)
        {
            if (!schema
                    .UsesForeignInvocationStorageFixedArrayUnitInitialization()
                || constant.Bits.Low != 0 || constant.Bits.High != 0)
            {
                return false;
            }

            register = 14;
            return true;
        }

        if (!DirectNativeImportContract.IsScalar(type))
        {
            return false;
        }

        if (constant.Bits.Low == 0 && constant.Bits.High == 0)
        {
            register = 14;
            return true;
        }

        var width = PrimitiveTypeFacts.ScalarWidth(type);
        if (constant.Bits.High != 0 || width is not (1 or 2 or 4 or 8))
        {
            return false;
        }

        var allBitsSet = width == sizeof(ulong)
            ? ulong.MaxValue
            : (1UL << (width * 8)) - 1;
        var isAllBitsConstant = type switch
        {
            PrimitiveType.Bool => constant.Bits.Low == 1,
            PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
                or PrimitiveType.Int64 or PrimitiveType.UInt8
                or PrimitiveType.UInt16 or PrimitiveType.UInt32
                or PrimitiveType.UInt64 or PrimitiveType.Float32
                or PrimitiveType.Float64 => constant.Bits.Low == allBitsSet,
            _ => false,
        };
        if (!isAllBitsConstant)
        {
            return false;
        }

        register = 15;
        return true;
    }

    private static uint CreateStandardBodyTypeMapToken(uint typeSpecRef)
    {
        var encoding = (byte)(typeSpecRef >> 24);
        return encoding is 0xF2 or 0xF3
            ? typeSpecRef : 0x1100_0000u | typeSpecRef;
    }

    private static byte[] EncodeForeignInvocationStorageAddressBody(
        BoundFunction caller, BoundProgram program,
        NativeAddressStorageCallPlan storageCall,
        NativeFunctionPointerSignatureDefinition signature,
        TrRmRegisterBodyMaps maps, uint targetMethodMapToken,
        int contractCount, Func<PrimitiveType, uint> getSignatureTypeSpecRef,
        Dictionary<int, int> fixedArrayLayoutRowByType,
        TrRmArtifactSchema schema)
    {
        var localCount = caller.Variables.Length - caller.ParameterCount;
        var unitCallTemporaryHomes = GetNativeAddressUnitCallHomes(
            storageCall.UnitInitializerCalls, storageCall.ConditionCaptures,
            localCount);
        var unitCallResultHomeEnd
            = localCount + unitCallTemporaryHomes.Length;
        var unitCallStorageHomeByArgument
            = new Dictionary<(int CallIndex, int ArgumentIndex), int>();
        var nextStorageHome = unitCallResultHomeEnd;
        for (var callIndex = 0;
            callIndex < storageCall.UnitInitializerCalls.Length;
            callIndex++)
        {
            var unitCall = storageCall.UnitInitializerCalls[callIndex].Call;
            for (var argumentIndex = 0;
                argumentIndex < unitCall.Arguments.Length;
                argumentIndex++)
            {
                if (unitCall.Arguments[argumentIndex]
                    is BoundNativeStorageAddress)
                {
                    unitCallStorageHomeByArgument.Add(
                        (callIndex, argumentIndex), nextStorageHome++);
                }
            }
        }

        var storageResultHomeStart = nextStorageHome;
        var formalHomeStart = storageResultHomeStart
            + storageCall.StorageArguments.Length;
        var homeCount = formalHomeStart + caller.ParameterCount;
        if (localCount <= 0 || homeCount > 65_536)
        {
            throw Unsupported(
                "The F+S Body has too many Homes for its local and storage-address results.");
        }

        var homeIndexByVariable = new int[caller.Variables.Length];
        Array.Fill(homeIndexByVariable, -1);
        var homeMap = new byte[homeCount * 8];
        for (var localOrdinal = 0; localOrdinal < localCount; localOrdinal++)
        {
            var variableIndex = caller.ParameterCount + localOrdinal;
            var variable = caller.Variables[variableIndex];
            var home = localOrdinal;
            homeIndexByVariable[variableIndex] = home;
            var offset = home * 8;
            homeMap[offset] = 3;
            homeMap[offset + 1] = variable.IsMutable ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                maps.GetTypeMapIndex(NativeFunctionPointerTypes.IsType(
                        variable.Type)
                    ? 0x1100_0001u
                    : CreateStandardBodyTypeMapToken(
                        getSignatureTypeSpecRef(variable.Type))));
        }

        foreach (var temporaryHome in unitCallTemporaryHomes)
        {
            var home = temporaryHome.Index;
            var offset = home * 8;
            homeMap[offset] = 3;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                maps.GetTypeMapIndex(CreateStandardBodyTypeMapToken(
                    getSignatureTypeSpecRef(temporaryHome.Type))));
        }

        var storageHomeByParameter = new Dictionary<int, int>();
        for (var index = 0;
            index < storageCall.StorageArguments.Length; index++)
        {
            var storageArgument = storageCall.StorageArguments[index];
            var home = storageResultHomeStart + index;
            storageHomeByParameter.Add(storageArgument.ParameterOrdinal, home);
            var offset = home * 8;
            homeMap[offset] = 3;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                maps.GetTypeMapIndex(CreateStandardBodyTypeMapToken(
                    getSignatureTypeSpecRef(signature.ParameterTypes[
                        storageArgument.ParameterOrdinal]))));
        }

        for (var callIndex = 0;
            callIndex < storageCall.UnitInitializerCalls.Length;
            callIndex++)
        {
            var unitCallPlan
                = storageCall.UnitInitializerCalls[callIndex];
            var call = unitCallPlan.Call;
            for (var argumentIndex = 0;
                argumentIndex < call.Arguments.Length;
                argumentIndex++)
            {
                if (!unitCallStorageHomeByArgument.TryGetValue(
                        (callIndex, argumentIndex), out var home))
                {
                    continue;
                }

                var parameterIndex = GetBoundCallParameterIndex(
                    call, argumentIndex);
                var offset = home * 8;
                homeMap[offset] = 3;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    homeMap.AsSpan(offset + 2),
                    maps.GetTypeMapIndex(CreateStandardBodyTypeMapToken(
                        getSignatureTypeSpecRef(
                            unitCallPlan.Signature.ParameterTypes[
                                parameterIndex]))));
            }
        }

        for (var ordinal = 0; ordinal < caller.ParameterCount; ordinal++)
        {
            var home = formalHomeStart + ordinal;
            var variable = caller.Variables[ordinal];
            var offset = home * 8;
            homeMap[offset] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                maps.GetTypeMapIndex(CreateStandardBodyTypeMapToken(
                    getSignatureTypeSpecRef(variable.Type))));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4), checked((ushort)ordinal));
        }

        var targetHome = homeIndexByVariable[storageCall.TargetVariable];
        if (targetHome < 0)
        {
            throw Unsupported("The F+S target pointer has no local Home.");
        }

        var entryState = new byte[8];
        entryState[0] = 0x01;
        var statements = new List<BoundStatement>();
        if (!AppendStatements(caller.Body, statements))
        {
            throw Unsupported("The F+S caller Body cannot be flattened.");
        }

        long recordCount = 3 + storageCall.StorageArguments.Length
            + storageCall.ConditionalEdgeCount
            + storageCall.UnitInitializerCalls.Length;
        foreach (var unitCall in storageCall.UnitInitializerCalls)
        {
            foreach (var argument in unitCall.Call.Arguments)
            {
                if (argument is not BoundNativeStorageAddress address)
                {
                    continue;
                }

                recordCount++;
                if (NativeFixedArrayTypes.IsType(address.CarrierType))
                {
                    recordCount++;
                }
            }
        }
        foreach (var statement in statements)
        {
            if (statement is BoundLocal
                {
                    Initializer: BoundNativeFixedArrayInitialization initialization,
                })
            {
                recordCount += initialization.Elements.IsEmpty
                    ? 2 : (long)initialization.Elements.Length * 2;
            }
        }

        foreach (var storageArgument in storageCall.StorageArguments)
        {
            if (NativeFixedArrayTypes.IsType(
                    storageArgument.Address.CarrierType))
            {
                recordCount++;
            }
        }
        if (recordCount > (long)ushort.MaxValue + 1)
        {
            throw Unsupported(
                "The A2 array initialization exceeds the Body record-index domain.");
        }

        var records = new List<ReadOnlyMemory<byte>>(
            checked((int)recordCount))
        {
            entryState,
            TrRmRegisterBodyRecordWriter.EncodeNativeAddress(2,
                checked((ushort)targetHome),
                maps.GetMethodMapIndex(targetMethodMapToken),
                maps.GetTypeMapIndex(0x1100_0001)),
        };
        var code = new List<byte>();
        var blocks = new List<TrRmRegisterBodyBlock>();
        var currentBlockStart = 0;
        void EndCurrentBlock()
        {
            if (code.Count <= currentBlockStart)
            {
                throw Unsupported("An A2EU control-flow block is empty.");
            }

            blocks.Add(new TrRmRegisterBodyBlock(
                checked((uint)currentBlockStart),
                checked((uint)(code.Count - currentBlockStart)), 0));
            currentBlockStart = code.Count;
        }

        int GetCallerHome(int variableIndex)
        {
            if ((uint)variableIndex >= (uint)homeIndexByVariable.Length)
            {
                throw Unsupported(
                    "The A2EU scalar check operand has no caller Home.");
            }

            var sourceHome = variableIndex < caller.ParameterCount
                ? formalHomeStart + variableIndex
                : homeIndexByVariable[variableIndex];
            if (sourceHome < 0)
            {
                throw Unsupported(
                    "The A2EU scalar check operand has no caller Home.");
            }

            return sourceHome;
        }

        void AppendA2EUCheckedScalarCapture(BoundBinary scalarCheck,
            int destinationHome)
        {
            if (!IsSupportedNativeUnitInitializerScalarCheck(scalarCheck)
                || scalarCheck.Left is not BoundRead leftRead
                || scalarCheck.Right is not BoundRead rightRead)
            {
                throw Unsupported(
                    "The A2EU scalar check is not an admitted checked division or remainder.");
            }

            AppendHomeLoad(code, 0,
                GetCallerHome(leftRead.Variable));
            AppendHomeLoad(code, 1,
                GetCallerHome(rightRead.Variable));
            code.Add(scalarCheck.Operator == TokenKind.Slash
                ? (byte)0x24 : (byte)0x25);
            code.Add(0x10);
            AppendHomeStore(code, 0, destinationHome);
        }

        var storageArguments = storageCall.StorageArguments
            .ToDictionary(static argument => argument.ParameterOrdinal);
        var unitCallPlanIndexesByCall = new Dictionary<BoundCall, Queue<int>>(
            ReferenceEqualityComparer.Instance);
        var conditionCaptureHomeByExpression
            = new Dictionary<BoundBinary, int>(
                ReferenceEqualityComparer.Instance);
        foreach (var conditionCapture in storageCall.ConditionCaptures)
        {
            if (!conditionCaptureHomeByExpression.TryAdd(
                    conditionCapture.Condition, conditionCapture.Home))
            {
                throw Unsupported(
                    "The A2EU condition capture is duplicated.");
            }
        }

        for (var callIndex = 0;
            callIndex < storageCall.UnitInitializerCalls.Length;
            callIndex++)
        {
            var unitCallPlan = storageCall.UnitInitializerCalls[callIndex];
            if (!unitCallPlanIndexesByCall.TryGetValue(unitCallPlan.Call,
                    out var callPlanIndexes))
            {
                callPlanIndexes = new Queue<int>();
                unitCallPlanIndexesByCall.Add(unitCallPlan.Call,
                    callPlanIndexes);
            }

            callPlanIndexes.Enqueue(callIndex);
        }

        var emittedUnitCallPlans = new HashSet<int>();

        int AppendUnitInitializerCall(BoundCall unitCall)
        {
            if (!unitCallPlanIndexesByCall.TryGetValue(unitCall,
                    out var callPlanIndexes)
                || !callPlanIndexes.TryDequeue(out var currentCallIndex))
            {
                throw Unsupported(
                    "The A2EU unit-call expression has no captured plan.");
            }

            _ = emittedUnitCallPlans.Add(currentCallIndex);

            var unitCallPlan
                = storageCall.UnitInitializerCalls[currentCallIndex];
            if (!ReferenceEquals(unitCallPlan.Call, unitCall))
            {
                throw Unsupported(
                    "The A2EU unit-call expression identity is inconsistent.");
            }

            var unitSignature = unitCallPlan.Signature;
            var argumentIndexByParameter = new int[
                unitSignature.ParameterTypes.Length];
            Array.Fill(argumentIndexByParameter, -1);
            for (var argumentIndex = 0;
                argumentIndex < unitCall.Arguments.Length;
                argumentIndex++)
            {
                var parameterIndex = GetBoundCallParameterIndex(
                    unitCall, argumentIndex);
                argumentIndexByParameter[parameterIndex] = argumentIndex;
            }

            var callInputs = new TrRmRegisterBodyCallInput[
                unitSignature.ParameterTypes.Length];
            var nestedCallResultHomes = new int[unitCall.Arguments.Length];
            Array.Fill(nestedCallResultHomes, -1);
            for (var argumentIndex = 0;
                argumentIndex < unitCall.Arguments.Length;
                argumentIndex++)
            {
                var argument = unitCall.Arguments[argumentIndex];
                if (argument is BoundCall nestedCall)
                {
                    nestedCallResultHomes[argumentIndex]
                        = AppendUnitInitializerCall(nestedCall);
                    continue;
                }

                var parameterIndex = GetBoundCallParameterIndex(
                    unitCall, argumentIndex);
                if (argument is BoundRead read
                    && unitCallPlan.ArgumentCaptureHomes[argumentIndex]
                        >= 0)
                {
                    var sourceHome
                        = read.Variable < caller.ParameterCount
                            ? formalHomeStart + read.Variable
                            : homeIndexByVariable[read.Variable];
                    var snapshotHome
                        = unitCallPlan.ArgumentCaptureHomes[argumentIndex];
                    if (sourceHome < 0)
                    {
                        throw Unsupported(
                            "The A2EU argument snapshot has no source Home.");
                    }

                    AppendHomeLoad(code, 0, sourceHome);
                    AppendHomeStore(code, 0, snapshotHome);
                    continue;
                }

                if (argument is BoundBinary scalarCheck
                    && unitCallPlan.ArgumentCaptureHomes[argumentIndex]
                        is var checkHome and >= 0)
                {
                    AppendA2EUCheckedScalarCapture(scalarCheck, checkHome);
                    continue;
                }

                if (argument is not BoundNativeStorageAddress storageAddress)
                {
                    continue;
                }

                if (!unitCallStorageHomeByArgument.TryGetValue(
                        (currentCallIndex, argumentIndex),
                        out var storageHome)
                    || homeIndexByVariable[storageAddress.Variable] < 0)
                {
                    throw Unsupported(
                        "The A2EU storage-address argument has no local or result Home.");
                }

                byte sourceKind;
                ushort sourceIndex;
                uint requiredElementCount;
                if (NativeFixedArrayTypes.IsType(
                        storageAddress.CarrierType))
                {
                    if (storageAddress.Index is not BoundRead indexRead
                        || (uint)indexRead.Variable
                            >= (uint)homeIndexByVariable.Length)
                    {
                        throw Unsupported(
                            "The A2EU array address requires an indexed local Home.");
                    }

                    var indexHome = indexRead.Variable < caller.ParameterCount
                        ? formalHomeStart + indexRead.Variable
                        : homeIndexByVariable[indexRead.Variable];
                    if (indexHome < 0
                        || !fixedArrayLayoutRowByType.TryGetValue(
                            NativeFixedArrayTypes.Index(
                                storageAddress.CarrierType),
                            out var fixedArrayLayoutRow)
                        || (uint)fixedArrayLayoutRow > 0xFF_FFFFu)
                    {
                        throw Unsupported(
                            "The A2EU array address has no mapped layout or index Home.");
                    }

                    var accessKind = storageAddress.IsReadonly
                        ? (byte)1 : (byte)2;
                    var placeSelectionIndex = checked((ushort)records.Count);
                    records.Add(
                        TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                            3, accessKind, 1, 2, 0,
                            checked((ushort)homeIndexByVariable[
                                storageAddress.Variable]),
                            2, 3, checked((ushort)indexHome),
                            checked((uint)fixedArrayLayoutRow),
                            maps.GetTypeMapIndex(
                                CreateStandardBodyTypeMapToken(
                                    getSignatureTypeSpecRef(
                                        storageAddress.ElementType))),
                            schema));
                    code.Add(0xD8);
                    code.Add(0);
                    code.Add((byte)placeSelectionIndex);
                    code.Add((byte)(placeSelectionIndex >> 8));
                    sourceKind = 1;
                    sourceIndex = 0;
                }
                else
                {
                    sourceKind = 2;
                    sourceIndex = checked((ushort)homeIndexByVariable[
                        storageAddress.Variable]);
                }

                requiredElementCount = checked((uint)(
                    storageAddress.ByteExtent
                    / storageAddress.ElementStride));
                var storageRecordIndex = checked((ushort)records.Count);
                records.Add(
                    TrRmRegisterBodyRecordWriter.EncodeStorageAddress(
                        sourceKind, 2, sourceIndex,
                        checked((ushort)storageHome),
                        maps.GetTypeMapIndex(CreateStandardBodyTypeMapToken(
                            getSignatureTypeSpecRef(
                                unitSignature.ParameterTypes[
                                    parameterIndex]))),
                        requiredElementCount));
                code.Add(0xEC);
                code.Add(0);
                code.Add((byte)storageRecordIndex);
                code.Add((byte)(storageRecordIndex >> 8));
                callInputs[parameterIndex]
                    = new TrRmRegisterBodyCallInput(2, 1,
                        checked((ushort)storageHome));
            }

            for (var parameterIndex = 0;
                parameterIndex < callInputs.Length;
                parameterIndex++)
            {
                var argumentIndex
                    = argumentIndexByParameter[parameterIndex];
                var parameterType
                    = unitSignature.ParameterTypes[parameterIndex];
                switch (unitCall.Arguments[argumentIndex])
                {
                    case BoundNativeStorageAddress:
                        break;

                    case BoundRead read:
                        var snapshotHome
                            = unitCallPlan.ArgumentCaptureHomes[argumentIndex];
                        var inputHome = snapshotHome >= 0
                            ? snapshotHome
                            : read.Variable < caller.ParameterCount
                                ? formalHomeStart + read.Variable
                                : homeIndexByVariable[read.Variable];
                        if (inputHome < 0)
                        {
                            throw Unsupported(
                                "The A2EU CallSite input has no completed Home.");
                        }

                        callInputs[parameterIndex]
                            = new TrRmRegisterBodyCallInput(2, 1,
                                checked((ushort)inputHome));
                        break;

                    case BoundBinary:
                        var checkHome
                            = unitCallPlan.ArgumentCaptureHomes[argumentIndex];
                        if (checkHome < 0)
                        {
                            throw Unsupported(
                                "The A2EU scalar check has no captured Home.");
                        }

                        callInputs[parameterIndex]
                            = new TrRmRegisterBodyCallInput(2, 1,
                                checked((ushort)checkHome));
                        break;

                    case BoundConstant { Type: PrimitiveType.Void }:
                        if (parameterType != PrimitiveType.Void)
                        {
                            throw Unsupported(
                                "An A2EU Unit argument does not match its formal.");
                        }

                        callInputs[parameterIndex]
                            = new TrRmRegisterBodyCallInput(0, 0, 0);
                        break;

                    case BoundConstant constant:
                        if (!TryGetContextualInitializationRegister(
                                constant, parameterType, schema,
                                out var constantRegister))
                        {
                            throw Unsupported(
                                "The A2EU CallSite constant is not admitted.");
                        }

                        callInputs[parameterIndex]
                            = new TrRmRegisterBodyCallInput(1, 1,
                                constantRegister);
                        break;

                    case BoundCall nestedCall:
                        if (nestedCall.Type == PrimitiveType.Void)
                        {
                            if (parameterType != PrimitiveType.Void)
                            {
                                throw Unsupported(
                                    "An A2EU nested Unit result does not match its formal.");
                            }

                            callInputs[parameterIndex]
                                = new TrRmRegisterBodyCallInput(0, 0, 0);
                        }
                        else if (nestedCall.Type == parameterType
                            && nestedCallResultHomes[argumentIndex]
                                is var nestedResultHome and >= 0)
                        {
                            callInputs[parameterIndex]
                                = new TrRmRegisterBodyCallInput(2, 1,
                                    checked((ushort)nestedResultHome));
                        }
                        else
                        {
                            throw Unsupported(
                                "An A2EU nested call result has no typed Home.");
                        }
                        break;

                    default:
                        throw Unsupported(
                            "A2EU direct calls require completed Homes, storage addresses, nested calls or contextual constants.");
                }
            }

            var resultKind = unitCallPlan.ResultHome < 0 ? (byte)0 : (byte)2;
            var unitCallMethodMapIndex = maps.GetMethodMapIndex(
                0x1600_0000u | checked((uint)(unitCall.Function + 1)));
            var callSiteIndex = checked((ushort)records.Count);
            records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
                1, resultKind, unitCallMethodMapIndex,
                unitCallPlan.ResultHome < 0 ? (ushort)0
                    : checked((ushort)unitCallPlan.ResultHome), callInputs,
                []));
            AppendCallSiteInstruction(code, callSiteIndex);
            return unitCallPlan.ResultHome;
        }

        void AppendUnitInitializerCondition(BoundExpression condition)
        {
            switch (condition)
            {
                case BoundRead read:
                    AppendHomeLoad(code, 0,
                        GetCallerHome(read.Variable));
                    break;

                case BoundCall conditionCall:
                    var conditionResultHome
                        = AppendUnitInitializerCall(conditionCall);
                    if (conditionResultHome < 0)
                    {
                        throw Unsupported(
                            "An A2EU branch condition call has no result Home.");
                    }

                    AppendHomeLoad(code, 0, conditionResultHome);
                    break;

                case BoundBinary comparison
                    when IsSupportedNativeUnitConditionalComparison(
                            comparison)
                        || IsSupportedNativeUnitConditionalBooleanXor(
                            comparison):
                    int leftHome;
                    if (comparison.Left is BoundRead leftRead)
                    {
                        leftHome = GetCallerHome(leftRead.Variable);
                        if (conditionCaptureHomeByExpression.TryGetValue(
                                comparison, out var captureHome))
                        {
                            AppendHomeLoad(code, 0, leftHome);
                            AppendHomeStore(code, 0, captureHome);
                            leftHome = captureHome;
                        }
                    }
                    else if (comparison.Left is BoundCall leftCall)
                    {
                        leftHome = AppendUnitInitializerCall(leftCall);
                    }
                    else
                    {
                        throw Unsupported(
                            "An A2EU comparison has no supported left operand.");
                    }

                    int rightHome;
                    if (comparison.Right is BoundRead rightRead)
                    {
                        rightHome = GetCallerHome(rightRead.Variable);
                    }
                    else if (comparison.Right is BoundCall rightCall)
                    {
                        rightHome = AppendUnitInitializerCall(rightCall);
                    }
                    else
                    {
                        throw Unsupported(
                            "An A2EU comparison has no supported right operand.");
                    }

                    if (leftHome < 0 || rightHome < 0)
                    {
                        throw Unsupported(
                            "An A2EU comparison operand has no result Home.");
                    }

                    AppendHomeLoad(code, 0, leftHome);
                    AppendHomeLoad(code, 1, rightHome);
                    if (comparison.Left.Type == PrimitiveType.Bool)
                    {
                        code.Add(0x32);
                        code.Add(0x10);
                        if (comparison.Operator
                            == TokenKind.EqualsEquals)
                        {
                            code.Add(0x33);
                            code.Add(0x00);
                        }
                    }
                    else
                    {
                        var predicateMask = comparison.Operator switch {
                            TokenKind.EqualsEquals => 2,
                            TokenKind.BangEquals => 13,
                            TokenKind.Less => 1,
                            TokenKind.LessEquals => 3,
                            TokenKind.Greater => 4,
                            TokenKind.GreaterEquals => 6,
                            _ => throw Unsupported(
                                "The A2EU comparison predicate has no assigned mask."),
                        };
                        code.Add(0x40);
                        code.Add(0x10);
                        code.Add(0x41);
                        code.Add(checked((byte)(predicateMask << 4)));
                    }
                    break;

                default:
                    throw Unsupported(
                        "A2EU conditional control flow requires an admitted Boolean condition or scalar comparison.");
            }
        }

        void AppendConditionalControlFlow(BoundExpression condition,
            Action appendTrue, Action appendFalse)
        {
            if (TryGetNativeAddressKnownBoolean(condition, out var selected))
            {
                (selected ? appendTrue : appendFalse)();
                return;
            }

            switch (condition)
            {
                case BoundBinary comparison
                    when TryGetNativeAddressBooleanLiteralComparison(
                        comparison, out var comparisonOperand,
                        out var negateComparison):
                    AppendConditionalControlFlow(comparisonOperand,
                        negateComparison ? appendFalse : appendTrue,
                        negateComparison ? appendTrue : appendFalse);
                    return;

                case BoundUnary {
                    Operator: TokenKind.Bang,
                    Type: PrimitiveType.Bool,
                    Operand: var operand,
                }:
                    AppendConditionalControlFlow(operand,
                        appendFalse, appendTrue);
                    return;

                case BoundBinary {
                    Operator: TokenKind.AmpersandAmpersand,
                    Type: PrimitiveType.Bool,
                    Left: var left,
                    Right: var right,
                }:
                    AppendConditionalControlFlow(left,
                        () => AppendConditionalControlFlow(right,
                            appendTrue, appendFalse),
                        appendFalse);
                    return;

                case BoundBinary {
                    Operator: TokenKind.PipePipe,
                    Type: PrimitiveType.Bool,
                    Left: var left,
                    Right: var right,
                }:
                    AppendConditionalControlFlow(left,
                        appendTrue,
                        () => AppendConditionalControlFlow(right,
                            appendTrue, appendFalse));
                    return;

                case BoundConditional {
                    Type: PrimitiveType.Bool,
                    Condition: var nestedCondition,
                    WhenTrue: var whenTrue,
                    WhenFalse: var whenFalse,
                }:
                    AppendConditionalControlFlow(nestedCondition,
                        () => AppendConditionalControlFlow(whenTrue,
                            appendTrue, appendFalse),
                        () => AppendConditionalControlFlow(whenFalse,
                            appendTrue, appendFalse));
                    return;
            }

            AppendUnitInitializerCondition(condition);
            if (records.Count > ushort.MaxValue)
            {
                throw Unsupported(
                    "The A2EU conditional edge exceeds the record-index domain.");
            }

            var conditionalEdgeIndex = checked((ushort)records.Count);
            var conditionalEdge
                = TrRmRegisterBodyRecordWriter.EncodeConditionalEdge(0, 1);
            records.Add(conditionalEdge);
            code.Add(0xE4);
            code.Add(0);
            code.Add((byte)conditionalEdgeIndex);
            code.Add((byte)(conditionalEdgeIndex >> 8));
            EndCurrentBlock();

            var trueBlockIndex = blocks.Count;
            appendTrue();
            var trueExitPatch = AppendBranchPlaceholder();
            EndCurrentBlock();

            var falseBlockIndex = blocks.Count;
            appendFalse();
            var falseExitPatch = AppendBranchPlaceholder();
            EndCurrentBlock();

            var joinBlockIndex = blocks.Count;
            if ((uint)trueBlockIndex > ushort.MaxValue
                || (uint)falseBlockIndex > ushort.MaxValue
                || (uint)joinBlockIndex > ushort.MaxValue)
            {
                throw Unsupported(
                    "The A2EU conditional exceeds the Block-index domain.");
            }

            BinaryPrimitives.WriteUInt16LittleEndian(
                conditionalEdge.AsSpan(4),
                checked((ushort)trueBlockIndex));
            BinaryPrimitives.WriteUInt16LittleEndian(
                conditionalEdge.AsSpan(6),
                checked((ushort)falseBlockIndex));
            PatchBranchTarget(trueExitPatch,
                checked((ushort)joinBlockIndex));
            PatchBranchTarget(falseExitPatch,
                checked((ushort)joinBlockIndex));
        }

        void AppendUnitInitializerExpression(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundCall unitCall
                    when unitCall.Type == PrimitiveType.Void:
                    _ = AppendUnitInitializerCall(unitCall);
                    return;

                case BoundConstant {
                    Type: PrimitiveType.Void,
                    Bits.Low: 0,
                    Bits.High: 0,
                }:
                    return;

                case BoundConditional conditional
                    when conditional.Type == PrimitiveType.Void
                        && IsSupportedNativeUnitConditionalCondition(
                            conditional.Condition):
                    if (TryGetNativeAddressKnownBoolean(
                            conditional.Condition, out var selected))
                    {
                        AppendUnitInitializerExpression(selected
                            ? conditional.WhenTrue : conditional.WhenFalse);
                        return;
                    }

                    AppendConditionalControlFlow(conditional.Condition,
                        () => AppendUnitInitializerExpression(
                            conditional.WhenTrue),
                        () => AppendUnitInitializerExpression(
                            conditional.WhenFalse));
                    return;

                default:
                    throw Unsupported(
                        "The A2EU unit initializer expression is outside the conditional capture subset.");
            }
        }

        void AppendUnitInitializerLocal(BoundLocal local)
        {
            var homeIndex = homeIndexByVariable[local.Variable];
            if (homeIndex < 0
                || local.Variable == storageCall.TargetVariable
                || NativeFixedArrayTypes.IsType(
                    caller.Variables[local.Variable].Type))
            {
                throw Unsupported(
                    "A conditional A2EU local capture has no supported Home.");
            }

            if (local.Initializer is BoundCall localCall)
            {
                _ = AppendUnitInitializerCall(localCall);
                return;
            }

            if (local.Initializer is BoundRead localRead)
            {
                var sourceHome
                    = localRead.Variable < caller.ParameterCount
                        ? formalHomeStart + localRead.Variable
                        : homeIndexByVariable[localRead.Variable];
                if (sourceHome < 0)
                {
                    throw Unsupported(
                        "A conditional A2EU local copy has no source Home.");
                }

                AppendHomeLoad(code, 0, sourceHome);
                AppendHomeStore(code, 0, homeIndex);
                return;
            }

            if (local.Initializer is BoundBinary scalarCheck
                && schema
                    .UsesForeignInvocationStorageFixedArrayUnitInitialization()
                && IsSupportedNativeUnitInitializerScalarCheck(scalarCheck))
            {
                AppendA2EUCheckedScalarCapture(scalarCheck, homeIndex);
                return;
            }

            if (local.Initializer is BoundConstant constant
                && TryGetContextualInitializationRegister(constant,
                    caller.Variables[local.Variable].Type, schema,
                    out var sourceRegister))
            {
                AppendHomeStore(code, sourceRegister, homeIndex);
                return;
            }

            throw Unsupported(
                "A conditional A2EU local initializer is outside the supported typed subset.");
        }

        void AppendUnitInitializerStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlock block when block.RetiredManagedLocals.IsEmpty:
                    foreach (var nested in block.Statements)
                    {
                        AppendUnitInitializerStatement(nested);
                    }
                    return;

                case BoundLocal local:
                    AppendUnitInitializerLocal(local);
                    return;

                case BoundExpressionStatement
                    { Expression: BoundCall call }
                    when call.Type == PrimitiveType.Void:
                    _ = AppendUnitInitializerCall(call);
                    return;

                case BoundExpressionStatement
                    { Expression: BoundConditional conditional }:
                    AppendUnitInitializerExpression(conditional);
                    return;

                case BoundIf branch:
                    AppendUnitInitializerIf(branch);
                    return;

                default:
                    throw Unsupported(
                        "A conditional A2EU branch contains an unsupported statement.");
            }
        }

        void AppendUnitInitializerIf(BoundIf branch)
        {
            if (!IsSupportedNativeUnitConditionalCondition(
                    branch.Condition))
            {
                throw Unsupported(
                    "A conditional A2EU branch requires an admitted Boolean condition.");
            }

            if (TryGetNativeAddressKnownBoolean(branch.Condition,
                    out var selected))
            {
                if (selected)
                {
                    AppendUnitInitializerStatement(branch.Then);
                }
                else if (branch.Else is not null)
                {
                    AppendUnitInitializerStatement(branch.Else);
                }
                return;
            }

            AppendConditionalControlFlow(branch.Condition,
                () => AppendUnitInitializerStatement(branch.Then),
                () =>
                {
                    if (branch.Else is not null)
                    {
                        AppendUnitInitializerStatement(branch.Else);
                    }
                });
        }

        int AppendBranchPlaceholder()
        {
            code.Add(0xE3);
            code.Add(0);
            var targetOffset = code.Count;
            code.Add(0);
            code.Add(0);
            return targetOffset;
        }

        void PatchBranchTarget(int targetOffset, ushort blockIndex)
        {
            code[targetOffset] = (byte)blockIndex;
            code[targetOffset + 1] = (byte)(blockIndex >> 8);
        }

        foreach (var statement in statements)
        {
            switch (statement)
            {
                case BoundLocal local:
                    var homeIndex = homeIndexByVariable[local.Variable];
                    if (homeIndex < 0)
                    {
                        throw Unsupported("The F+S local has no Home.");
                    }

                    if (local.Variable == storageCall.TargetVariable)
                    {
                        code.Add(0xE9);
                        code.Add(0);
                        code.Add(1);
                        code.Add(0);
                        break;
                    }

                    if (NativeFixedArrayTypes.IsType(
                            caller.Variables[local.Variable].Type))
                    {
                        if (local.Initializer
                                is not BoundNativeFixedArrayInitialization arrayInitialization
                            || !IsSupportedNativeFixedArrayInitialization(
                                caller.Variables[local.Variable].Type,
                                arrayInitialization, program, schema)
                            || !fixedArrayLayoutRowByType.TryGetValue(
                                NativeFixedArrayTypes.Index(
                                    caller.Variables[local.Variable].Type),
                                out var fixedArrayLayoutRow))
                        {
                            throw Unsupported(
                                "The A2 Body array initializer is not a complete supported local.");
                        }

                        var arrayType = caller.Variables[local.Variable].Type;
                        if (arrayInitialization.Layout.ElementCount == 0)
                        {
                            if (!schema
                                    .UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                                || arrayInitialization.Layout.ByteExtent != 0)
                            {
                                throw Unsupported(
                                    "The A2E nullary initialization requires an exact empty fixed array.");
                            }

                            var emptySelectionIndex
                                = checked((ushort)records.Count);
                            records.Add(
                                TrRmRegisterBodyRecordWriter
                                    .EncodePlaceSelection(1, 3, 1, 0, 2, 0,
                                        0, 0, 0,
                                        checked((uint)homeIndex),
                                        maps.GetTypeMapIndex(
                                            CreateStandardBodyTypeMapToken(
                                                getSignatureTypeSpecRef(
                                                    arrayType))),
                                        schema));
                            code.Add(0xD8);
                            code.Add(2);
                            code.Add((byte)emptySelectionIndex);
                            code.Add((byte)(emptySelectionIndex >> 8));

                            var emptyStoreIndex = checked((ushort)records.Count);
                            records.Add(
                                TrRmRegisterBodyRecordWriter
                                    .EncodeEmptyArrayInitialization(2, schema));
                            code.Add(0xD9);
                            code.Add(0);
                            code.Add((byte)emptyStoreIndex);
                            code.Add((byte)(emptyStoreIndex >> 8));
                            break;
                        }

                        foreach (var initializer in
                            arrayInitialization.Elements)
                        {
                            if (initializer is BoundCall unitInitializerCall)
                            {
                                _ = AppendUnitInitializerCall(
                                    unitInitializerCall);
                            }
                            else if (initializer is BoundConditional
                                unitConditional)
                            {
                                AppendUnitInitializerExpression(
                                    unitConditional);
                            }
                        }

                        var indexHome = homeIndexByVariable[
                            storageCall.ArrayInitializationIndexVariable];
                        if (indexHome < 0)
                        {
                            throw Unsupported(
                                "The A2 array initializer has no zero-valued index Home.");
                        }

                        if (storageCall.UnitInitializerCalls.IsEmpty)
                        {
                            AppendHomeLoad(code, 0, indexHome);
                        }
                        else
                        {
                            code.Add(0x10);
                            code.Add(0xE0);
                        }

                        code.Add(0x10);
                        code.Add(0x01);
                        code.Add(0x33);
                        code.Add(0x11);
                        code.Add(0x20);
                        code.Add(0x11);
                        var elementType
                            = arrayInitialization.Layout.ElementType;
                        if ((uint)fixedArrayLayoutRow > 0xFF_FFFFu)
                        {
                            throw Unsupported(
                                "The A2 array initializer has no representable layout row.");
                        }

                        for (var elementIndex = 0;
                            elementIndex < arrayInitialization.Elements.Length;
                            elementIndex++)
                        {
                            var initializationSelectionIndex
                                = checked((ushort)records.Count);
                            records.Add(
                                TrRmRegisterBodyRecordWriter
                                    .EncodePlaceSelection(3, 3, 1, 2, 2,
                                        checked((ushort)homeIndexByVariable[
                                            local.Variable]),
                                        1, 3, 0,
                                        checked((uint)
                                            fixedArrayLayoutRow),
                                        maps.GetTypeMapIndex(
                                            CreateStandardBodyTypeMapToken(
                                                getSignatureTypeSpecRef(
                                                    elementType))),
                                        schema));
                            code.Add(0xD8);
                            code.Add(2);
                            code.Add((byte)initializationSelectionIndex);
                            code.Add((byte)(initializationSelectionIndex
                                >> 8));

                            var initializationStoreIndex
                                = checked((ushort)records.Count);
                            if (arrayInitialization.Elements[elementIndex]
                                is BoundConstant initializerConstant)
                            {
                                if (!TryGetContextualInitializationRegister(
                                        initializerConstant, elementType,
                                        schema,
                                        out var initializerRegister))
                                {
                                    throw Unsupported(
                                        "The A2 array initializer requires admitted contextual scalar values.");
                                }

                                records.Add(
                                    TrRmRegisterBodyRecordWriter
                                        .EncodeSemanticStore(1, 1, 1, 1, 2,
                                            initializerRegister));
                            }
                            else
                            {
                                if (arrayInitialization.Elements[elementIndex]
                                    is BoundCall { Type: PrimitiveType.Void }
                                        or BoundConditional
                                            { Type: PrimitiveType.Void }
                                    && schema
                                        .UsesForeignInvocationStorageFixedArrayUnitInitialization())
                                {
                                    records.Add(
                                        TrRmRegisterBodyRecordWriter
                                            .EncodeSemanticStore(1, 1, 1, 1,
                                                2, 14));
                                }
                                else
                                {
                                    records.Add(
                                        TrRmRegisterBodyRecordWriter
                                            .EncodeEmptyArrayInitialization(2,
                                                schema));
                                }
                            }

                            code.Add(0xD9);
                            code.Add(0);
                            code.Add((byte)initializationStoreIndex);
                            code.Add((byte)(initializationStoreIndex >> 8));
                            if (elementIndex + 1
                                < arrayInitialization.Elements.Length)
                            {
                                code.Add(0x21);
                                code.Add(0x10);
                            }
                        }

                        break;
                    }

                    if (local.Initializer is BoundCall localCall)
                    {
                        _ = AppendUnitInitializerCall(localCall);
                        break;
                    }

                    if (local.Initializer is BoundRead localRead)
                    {
                        var sourceHome
                            = localRead.Variable < caller.ParameterCount
                                ? formalHomeStart + localRead.Variable
                                : homeIndexByVariable[localRead.Variable];
                        if (sourceHome < 0)
                        {
                            throw Unsupported(
                                "The F+S copied local has no initialized source Home.");
                        }

                        AppendHomeLoad(code, 0, sourceHome);
                        AppendHomeStore(code, 0, homeIndex);
                        break;
                    }

                    if (local.Initializer is BoundBinary scalarCheck
                        && schema
                            .UsesForeignInvocationStorageFixedArrayUnitInitialization()
                        && IsSupportedNativeUnitInitializerScalarCheck(
                            scalarCheck))
                    {
                        AppendA2EUCheckedScalarCapture(scalarCheck,
                            homeIndex);
                        break;
                    }

                    if (local.Initializer is not BoundConstant constant
                        || !TryGetContextualInitializationRegister(constant,
                            caller.Variables[local.Variable].Type, schema,
                            out var sourceRegister))
                    {
                        throw Unsupported(
                            "F+S local initialization requires an admitted typed contextual constant.");
                    }

                    AppendHomeStore(code, sourceRegister, homeIndex);
                    break;

                case BoundExpressionStatement
                    { Expression: BoundCall unitCall }:
                    _ = AppendUnitInitializerCall(unitCall);
                    break;

                case BoundIf branch:
                    AppendUnitInitializerIf(branch);
                    break;

                case BoundExpressionStatement
                    { Expression: BoundConditional unitConditional }:
                    AppendUnitInitializerExpression(unitConditional);
                    break;

                case BoundExpressionStatement
                    { Expression: BoundIndirectCall call }:
                    var callInputs = new List<TrRmRegisterBodyCallInput>(
                        call.Arguments.Length);
                    for (var ordinal = 0; ordinal < call.Arguments.Length;
                        ordinal++)
                    {
                        switch (call.Arguments[ordinal])
                        {
                            case BoundNativeStorageAddress storageAddress:
                                if (!storageArguments.TryGetValue(ordinal,
                                        out var storageArgument)
                                    || storageArgument.Address != storageAddress
                                    || homeIndexByVariable[
                                        storageAddress.Variable] < 0)
                                {
                                    throw Unsupported(
                                        "The F+S storage-address argument does not match its checked local place.");
                                }

                                var isFixedArray
                                    = NativeFixedArrayTypes.IsType(
                                        storageAddress.CarrierType);
                                byte sourceKind;
                                ushort sourceIndex;
                                uint requiredElementCount;
                                if (isFixedArray)
                                {
                                    if (storageAddress.Index is not BoundRead indexRead
                                        || (uint)indexRead.Variable
                                            >= (uint)homeIndexByVariable.Length)
                                    {
                                        throw Unsupported(
                                            "The A2 storage address requires an indexed local Home.");
                                    }

                                    var indexHome = indexRead.Variable
                                            < caller.ParameterCount
                                        ? formalHomeStart + indexRead.Variable
                                        : homeIndexByVariable[
                                            indexRead.Variable];
                                    if (indexHome < 0
                                        || !fixedArrayLayoutRowByType.TryGetValue(
                                            NativeFixedArrayTypes.Index(
                                                storageAddress.CarrierType),
                                            out var fixedArrayLayoutRow))
                                    {
                                        throw Unsupported(
                                            "The A2 storage address has no mapped array/index Home.");
                                    }

                                    var accessKind = storageAddress.IsReadonly
                                        ? (byte)1 : (byte)2;
                                    var placeSelectionIndex
                                        = checked((ushort)records.Count);
                                    var elementType = storageAddress.ElementType;
                                    if ((uint)fixedArrayLayoutRow
                                        > 0xFF_FFFFu)
                                    {
                                        throw Unsupported(
                                            "The A2 fixed-array layout row exceeds the PlaceSelection field.");
                                    }
                                    records.Add(
                                        TrRmRegisterBodyRecordWriter
                                            .EncodePlaceSelection(3,
                                                accessKind, 1, 2, 0,
                                                checked((ushort)
                                                    homeIndexByVariable[
                                                        storageAddress.Variable]),
                                                2,
                                                3,
                                                checked((ushort)indexHome),
                                                checked((uint)
                                                    fixedArrayLayoutRow),
                                                maps.GetTypeMapIndex(
                                                    CreateStandardBodyTypeMapToken(
                                                        getSignatureTypeSpecRef(
                                                            elementType))),
                                                schema));
                                    code.Add(0xD8);
                                    code.Add(0);
                                    code.Add((byte)placeSelectionIndex);
                                    code.Add((byte)(placeSelectionIndex >> 8));

                                    sourceKind = 1;
                                    sourceIndex = 0;
                                    requiredElementCount = checked((uint)(
                                        storageAddress.ByteExtent
                                        / storageAddress.ElementStride));
                                }
                                else
                                {
                                    sourceKind = 2;
                                    sourceIndex = checked((ushort)
                                        homeIndexByVariable[
                                            storageAddress.Variable]);
                                    requiredElementCount = checked((uint)(
                                        storageAddress.ByteExtent
                                        / storageAddress.ElementStride));
                                }

                                var storageRecordIndex
                                    = checked((ushort)records.Count);
                                records.Add(
                                    TrRmRegisterBodyRecordWriter
                                        .EncodeStorageAddress(
                                            sourceKind, 2,
                                            sourceIndex,
                                            checked((ushort)
                                                storageHomeByParameter[ordinal]),
                                            maps.GetTypeMapIndex(
                                                    CreateStandardBodyTypeMapToken(
                                                        getSignatureTypeSpecRef(
                                                        signature.ParameterTypes[
                                                                ordinal]))),
                                            requiredElementCount));
                                code.Add(0xEC);
                                code.Add(0);
                                code.Add((byte)storageRecordIndex);
                                code.Add((byte)(storageRecordIndex >> 8));
                                callInputs.Add(new TrRmRegisterBodyCallInput(2, 1,
                                    checked((ushort)storageHomeByParameter[ordinal])));
                                break;

                            case BoundRead read:
                                var inputHome = read.Variable < caller.ParameterCount
                                    ? formalHomeStart + read.Variable
                                    : homeIndexByVariable[read.Variable];
                                if (inputHome < 0)
                                {
                                    throw Unsupported(
                                        "The F+S CallSite input has no completed Home.");
                                }

                                callInputs.Add(new TrRmRegisterBodyCallInput(2, 1,
                                    checked((ushort)inputHome)));
                                break;

                            default:
                                throw Unsupported(
                                    "The F+S CallSite supports local storage addresses and completed Home reads.");
                        }
                    }

                    var callResultKind = call.Type == PrimitiveType.Void
                        ? (byte)0 : (byte)1;
                    var callSiteIndex = checked((ushort)records.Count);
                    records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
                        3, callResultKind, checked((ushort)targetHome), 0,
                        callInputs, []));
                    AppendCallSiteInstruction(code, callSiteIndex);
                    break;

                case BoundReturn { Value: BoundRead read }:
                    var returnHome = read.Variable < caller.ParameterCount
                        ? formalHomeStart + read.Variable
                        : homeIndexByVariable[read.Variable];
                    if (returnHome < 0 || read.Variable != storageCall.ReturnVariable)
                    {
                        throw Unsupported(
                            "The F+S return must read the storage local after the native call.");
                    }
                    if (returnHome > byte.MaxValue)
                    {
                        throw Unsupported(
                            "The assigned ReturnHome form supports only one-byte Home indexes.");
                    }

                    code.Add(0xB8);
                    code.Add(checked((byte)returnHome));
                    break;

                default:
                    throw Unsupported(
                        "The F+S caller Body contains an unsupported operation.");
            }
        }

        if (emittedUnitCallPlans.Count
            != storageCall.UnitInitializerCalls.Length)
        {
            throw Unsupported(
                "The A2EU unit-call plan does not match the encoded arrays.");
        }

        EndCurrentBlock();
        var codeBytes = code.ToArray();
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, default,
            blocks,
            records, codeBytes, contractCount,
            schema);
    }

    private static void AppendHomeStore(List<byte> code, byte sourceRegister,
        int homeIndex)
    {
        if (sourceRegister > 15 || (uint)homeIndex > ushort.MaxValue)
        {
            throw Unsupported("The F+S Home initialization operand is invalid.");
        }

        if (sourceRegister is 14 or 15 && homeIndex < 16)
        {
            code.Add(0x14);
            code.Add((byte)((sourceRegister << 4) | homeIndex));
            return;
        }

        code.Add(0xD6);
        code.Add((byte)(0x10 | sourceRegister));
        code.Add((byte)homeIndex);
        code.Add((byte)(homeIndex >> 8));
    }

    private static void AppendHomeLoad(List<byte> code,
        byte destinationRegister, int homeIndex)
    {
        if (destinationRegister > 13
            || (uint)homeIndex > ushort.MaxValue)
        {
            throw Unsupported("The A2 array index Home is invalid.");
        }

        code.Add(0xD5);
        code.Add(destinationRegister);
        code.Add((byte)homeIndex);
        code.Add((byte)(homeIndex >> 8));
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
            || NativeFixedArrayTypes.IsType(variable.Type))
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
                || NativeFixedArrayTypes.IsType(resultVariable.Type))
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
            || NativeFixedArrayTypes.IsType(variable.Type))
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
                || NativeFixedArrayTypes.IsType(formal.Type)
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
