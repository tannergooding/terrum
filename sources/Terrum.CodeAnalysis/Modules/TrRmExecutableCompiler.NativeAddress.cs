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
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports = null,
        TrRmA3LayoutResourceLedger? a3LayoutResourceLedger = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        if (schema is not (TrRmArtifactSchema.ForeignInvocationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft))
        {
            throw new ArgumentOutOfRangeException(nameof(schema));
        }
        if (sourceTypeExports is { Count: > 0 }
            && (schema is not (
                    TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                    or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                    or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                    or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft)
                || program.EntryPoint != -1 || !program.Functions.IsEmpty))
        {
            throw Unsupported(
                "Explicit A2/A2E TypeDef exports require a type-only source library.");
        }
        a3LayoutResourceLedger = schema
            == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? a3LayoutResourceLedger
                    ?? new TrRmA3LayoutResourceLedger(moduleIdentity)
                : null;
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
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft)
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
            a3LayoutResourceLedger?.ConsumeWork();
            var function = program.Functions[index];
            ReserveA3Collection(a3LayoutResourceLedger, 0);
            var addresses = new List<BoundNativeFunctionAddress>();
            if (!BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    a3LayoutResourceLedger?.ConsumeWork();
                    if (expression is BoundNativeFunctionAddress nativeAddress)
                    {
                        a3LayoutResourceLedger?.ConsumeWork();
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
                a3LayoutResourceLedger,
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
                caller, selectedAddress, signature,
                a3LayoutResourceLedger, out callHomeIndex,
                out callResultHomeIndex);
        }
        var emitsIndirectCall = indirectCall is not null;
        var scalarValues = schema.UsesScalarFrozenValues()
            ? new TrRmFrozenScalarValueBuilder() : null;
        if (!emitsIndirectCall && caller.ParameterCount != 0)
        {
            throw Unsupported(
                "The discarded NativeAddress profile requires a parameterless caller.");
        }

        if (!emitsIndirectCall && !IsAddressOnlyVoidBody(caller,
                selectedAddress, a3LayoutResourceLedger))
        {
            throw Unsupported(
                "The caller Body is outside the discarded-address or unit indirect-call profile.");
        }

        var targetMethodRow = targetIndex + 1;
        var callerMethodRow = callerIndex + 1;
        var anchorMethodRow = program.Functions.Length + 1;
        ReserveA3Collection(a3LayoutResourceLedger, 1);
        ReserveA3Buffer(a3LayoutResourceLedger, 7);
        var typeSpecRows = new List<ReadOnlyMemory<byte>>(1)
        {
            TrRmTypeSpecTableWriter.NativeFunctionPointer(
                (byte)signature.CallingConvention, checked((uint)anchorMethodRow)),
        };
        var signatureTypeSpecRefs = new Dictionary<PrimitiveType, uint>();
        var pointerTypeSpecTypesInProgress = new HashSet<PrimitiveType>();
        a3LayoutResourceLedger?.ConsumeWork();
        var fixedArrayTypeSpecRefs = new Dictionary<int, uint>();
        ReserveA3Buffer(a3LayoutResourceLedger,
            program.NativeFixedArrayTypes.Length);
        var fixedArrayTypeStates
            = new byte[program.NativeFixedArrayTypes.Length];
        a3LayoutResourceLedger?.ConsumeWork();
        var fixedArrayLayoutRows = new List<TrRmFixedArrayLayoutRow>();
        a3LayoutResourceLedger?.ConsumeWork();
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
                    or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                    or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft))
                {
                    throw Unsupported(
                        "Fixed-array Body types require the exact A2 schema.");
                }

                return GetFixedArrayTypeSpecRef(
                    NativeFixedArrayTypes.Index(type));
            }

            a3LayoutResourceLedger?.ConsumeWork();
            if (signatureTypeSpecRefs.TryGetValue(type, out var typeSpecRef))
            {
                return typeSpecRef;
            }

            byte[] typeSpecBytes;
            if (NativePointerTypes.IsType(type))
            {
                a3LayoutResourceLedger?.ConsumeWork();
                if (!NativePointerTypeContract.IsValidType(type,
                        program.NativePointerTypes))
                {
                    throw Unsupported(
                        $"The NativeAddress producer does not support pointer type {type}.");
                }

                var pointer = program.NativePointerTypes[
                    NativePointerTypes.Index(type)];
                var pointeeIndex = Array.IndexOf(s_types, pointer.PointeeType);
                if (!pointerTypeSpecTypesInProgress.Add(type))
                {
                    throw Unsupported(
                        "The NativeAddress producer received a recursive pointer TypeSpec.");
                }

                try
                {
                    if (pointeeIndex >= 0
                        && (pointer.PointeeType == PrimitiveType.Void
                            || DirectNativeImportContract.IsScalar(
                                pointer.PointeeType)))
                    {
                        a3LayoutResourceLedger?.ConsumeWork(5);
                        typeSpecBytes = TrRmTypeSpecTableWriter.InlineFamily(
                            0x8C, s_codes[pointeeIndex],
                            pointer.IsWritable ? (byte)1 : (byte)0);
                    }
                    else if (NativePointerTypes.IsType(
                                pointer.PointeeType)
                        || NativeFixedArrayTypes.IsType(
                            pointer.PointeeType))
                    {
                        var pointeeTypeSpecRef
                            = GetSignatureTypeSpecRef(pointer.PointeeType);
                        a3LayoutResourceLedger?.ConsumeWork(5);
                        typeSpecBytes
                            = TrRmTypeSpecTableWriter.IndexedFamily(0x8C,
                                pointeeTypeSpecRef,
                                pointer.IsWritable ? (byte)1 : (byte)0);
                    }
                    else
                    {
                        throw Unsupported(
                            $"The NativeAddress producer does not support pointer pointee {pointer.PointeeType}.");
                    }
                }
                finally
                {
                    _ = pointerTypeSpecTypesInProgress.Remove(type);
                }
            }
            else
            {
                var scalarIndex = Array.IndexOf(s_types, type);
                var typeCode = type switch
                {
                    PrimitiveType.Int128
                        when schema.UsesForeignInvocationStorageFixedArrays()
                            => (byte)0x0A,
                    PrimitiveType.UInt128
                        when schema.UsesForeignInvocationStorageFixedArrays()
                            => (byte)0x0F,
                    _ when scalarIndex > 0
                        && DirectNativeImportContract.IsScalar(type)
                            => s_codes[scalarIndex],
                    _ => throw Unsupported(
                        $"The NativeAddress producer does not support signature type {type}."),
                };
                a3LayoutResourceLedger?.ConsumeWork(4);
                typeSpecBytes
                    = TrRmTypeSpecTableWriter.Builtin(typeCode);
            }

            typeSpecRef = checked((uint)typeSpecRows.Count + 1);
            a3LayoutResourceLedger?.ConsumeWork(3);
            typeSpecRows.Add(new ReadOnlyMemory<byte>(typeSpecBytes));
            signatureTypeSpecRefs.Add(type, typeSpecRef);
            return typeSpecRef;
        }

        ReserveA3Array(a3LayoutResourceLedger, caller.ParameterCount,
            sizeof(uint));
        var callerParameterTypeSpecRefs = new uint[caller.ParameterCount];
        for (var ordinal = 0; ordinal < callerParameterTypeSpecRefs.Length;
            ordinal++)
        {
            a3LayoutResourceLedger?.ConsumeWork();
            callerParameterTypeSpecRefs[ordinal]
                = GetSignatureTypeSpecRef(caller.Variables[ordinal].Type);
        }

        var callerLocalCount = caller.Variables.Length - caller.ParameterCount;
        ReserveA3Array(a3LayoutResourceLedger, callerLocalCount,
            sizeof(uint));
        var callerLocalTypeSpecRefs = new uint[callerLocalCount];
        for (var ordinal = 0; ordinal < callerLocalTypeSpecRefs.Length;
            ordinal++)
        {
            a3LayoutResourceLedger?.ConsumeWork();
            var type = caller.Variables[caller.ParameterCount + ordinal].Type;
            callerLocalTypeSpecRefs[ordinal]
                = NativeFunctionPointerTypes.IsType(type)
                    ? 0u : GetSignatureTypeSpecRef(type);
        }

        ReserveA3Array(a3LayoutResourceLedger, signature.ParameterTypes.Length,
            sizeof(uint));
        var targetParameterTypeSpecRefs
            = new uint[signature.ParameterTypes.Length];
        for (var ordinal = 0; ordinal < targetParameterTypeSpecRefs.Length;
            ordinal++)
        {
            a3LayoutResourceLedger?.ConsumeWork();
            targetParameterTypeSpecRefs[ordinal]
                = GetSignatureTypeSpecRef(signature.ParameterTypes[ordinal]);
        }

        ReserveA3Array(a3LayoutResourceLedger, program.Functions.Length,
            sizeof(int));
        var functionSignatureRows = new int[program.Functions.Length];
        ReserveA3Collection(a3LayoutResourceLedger,
            program.Functions.Length + 1);
        var signatureRows = new List<TrRmMethodSigRow>(
            program.Functions.Length + 1);
        ReserveA3Collection(a3LayoutResourceLedger, 0);
        var parameterRows = new List<TrRmParamRow>();
        for (var functionIndex = 0;
            functionIndex < program.Functions.Length;
            functionIndex++)
        {
            a3LayoutResourceLedger?.ConsumeWork();
            var function = program.Functions[functionIndex];
            var signatureRow = signatureRows.Count + 1;
            functionSignatureRows[functionIndex] = signatureRow;
            ReserveA3Array(a3LayoutResourceLedger, function.ParameterCount,
                sizeof(uint));
            var parameterTypes = new uint[function.ParameterCount];
            for (var ordinal = 0; ordinal < parameterTypes.Length; ordinal++)
            {
                a3LayoutResourceLedger?.ConsumeWork();
                parameterTypes[ordinal] = GetSignatureTypeSpecRef(
                    function.Variables[ordinal].Type);
            }
            var parameterFirst = parameterTypes.Length == 0
                ? 0 : parameterRows.Count + 1;
            for (var ordinal = 0; ordinal < parameterTypes.Length; ordinal++)
            {
                a3LayoutResourceLedger?.ConsumeWork();
                parameterRows.Add(new TrRmParamRow(signatureRow, ordinal,
                    parameterTypes[ordinal], 0));
            }

            a3LayoutResourceLedger?.ConsumeWork();
            signatureRows.Add(new TrRmMethodSigRow(
                GetSignatureTypeSpecRef(function.ReturnType), 0,
                parameterFirst, parameterTypes.Length, 0, 0));
        }

        var anchorSignatureRow = functionSignatureRows[targetIndex];
        ReserveA3Collection(a3LayoutResourceLedger, 0);
        var featureRows = new List<TrRmFeatureRow>();
        if (fixedArrayLayoutRows.Count != 0)
        {
            a3LayoutResourceLedger?.ConsumeWork();
            featureRows.Add(TrRmFeatureRow.RootlessNativeValues);
        }
        a3LayoutResourceLedger?.ConsumeWork();
        featureRows.Add(new TrRmFeatureRow(4, 1));
        var hasNativeImportParameters = false;
        foreach (var function in program.Functions)
        {
            a3LayoutResourceLedger?.ConsumeWork();
            if (function.DirectNativeImport is DirectNativeImport nativeImport
                && !nativeImport.Parameters.IsEmpty)
            {
                hasNativeImportParameters = true;
                break;
            }
        }
        if (hasNativeImportParameters)
        {
            a3LayoutResourceLedger?.ConsumeWork();
            featureRows.Add(TrRmFeatureRow.ForeignInvocationContractsCandidate);
        }
        if (fixedArrayLayoutRows.Count != 0)
        {
            a3LayoutResourceLedger?.ConsumeWork();
            featureRows.Add(
                TrRmFeatureRow.FixedInlineArrayStorageCandidate);
        }
        if (storageCall?.UsesScalarFrozenValues == true)
        {
            if (!schema.UsesScalarFrozenValues())
            {
                throw Unsupported(
                    "Numeric comparison literals require the exact A2EU+SF1 schema.");
            }

            a3LayoutResourceLedger?.ConsumeWork();
            featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
        }

        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema,
            a3LayoutResourceLedger);
        var featureReader = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema, a3LayoutResourceLedger);

        ReserveA3Collection(a3LayoutResourceLedger, 0);
        var stringRows = new List<(string Text, int Encoding)>();
        AddA3Staged(stringRows, ("", 0), a3LayoutResourceLedger);
        ReserveA3Collection(a3LayoutResourceLedger, 0);
        var stringIndexes = new Dictionary<(string Text, int Encoding), int>();
        AddA3Staged(stringIndexes, ("", 0), 1,
            a3LayoutResourceLedger);
        var moduleNameRow = AddString(moduleIdentity, 1);
        ReserveA3Array(a3LayoutResourceLedger, program.Functions.Length,
            sizeof(int));
        var methodNameRows = new int[program.Functions.Length];
        for (var index = 0; index < program.Functions.Length; index++)
        {
            a3LayoutResourceLedger?.ConsumeWork();
            methodNameRows[index] = AddString(program.Functions[index].Name, 1);
        }
        var anchorNameRow = AddString(
            CreateAnchorName(program.Functions, a3LayoutResourceLedger), 1);
        ReserveA3Array(a3LayoutResourceLedger, program.Functions.Length,
            sizeof(int));
        var nativeImportLibraryNameRows = new int[program.Functions.Length];
        ReserveA3Array(a3LayoutResourceLedger, program.Functions.Length,
            sizeof(int));
        var nativeImportEntryNameRows = new int[program.Functions.Length];
        ReserveA3Collection(a3LayoutResourceLedger, 0);
        var nativeImportFunctionIndices = new List<int>();
        for (var index = 0; index < program.Functions.Length; index++)
        {
            a3LayoutResourceLedger?.ConsumeWork();
            if (program.Functions[index].DirectNativeImport
                is not DirectNativeImport nativeImport)
            {
                continue;
            }

            nativeImportLibraryNameRows[index]
                = AddString(nativeImport.LibraryName, 0);
            nativeImportEntryNameRows[index]
                = AddString(nativeImport.ExportName, 0);
            AddA3Staged(nativeImportFunctionIndices, index,
                a3LayoutResourceLedger);
        }

        var providerIdentityRow = moduleImport is null
            ? 0 : AddString(moduleImport.ProducerIdentity, 1);
        var providerExportKeyRow = moduleImport is null
            ? 0 : AddString(moduleImport.ExportKey, 1);
        var stringsBytes = a3LayoutResourceLedger is null
            ? TrRmStringTableWriter.EncodeTextRows(stringRows)
            : TrRmStringTableWriter.EncodeTextRows(stringRows,
                a3LayoutResourceLedger);
        var strings = new TrRmStringTableReader(stringsBytes, stringRows.Count,
            a3LayoutResourceLedger);

        var moduleRefCapacity = moduleImport is null ? 0 : 1;
        ReserveA3Collection(a3LayoutResourceLedger, moduleRefCapacity);
        var moduleRefRows = new List<TrRmModuleRefRow>(moduleRefCapacity);
        if (moduleImport is not null)
        {
            AddA3Staged(moduleRefRows,
                new TrRmModuleRefRow(providerIdentityRow,
                    moduleImport.TargetRequirement, moduleImport.RuntimePolicy),
                a3LayoutResourceLedger);
        }

        var entryMethodRow = program.EntryPoint < 0
            ? 0 : program.EntryPoint + 1;
        ReserveA3Array(a3LayoutResourceLedger, 1, 11 * sizeof(uint));
        var moduleRows = new[]
        {
            new TrRmModuleRow(moduleNameRow, 1, 0, entryMethodRow, 1, 1, 1, 1,
                1, featureRows.Count, 1),
        };
        var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows,
            strings, moduleRefRows.Count, 0,
            program.Functions.Length + 1, 0, featureRows.Count,
            a3LayoutResourceLedger);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefRows.Count, 0, program.Functions.Length + 1, 0,
            featureRows.Count, a3LayoutResourceLedger);
        var moduleRefBytes = moduleRefRows.Count == 0
            ? []
            : TrRmModuleRefTableWriter.Encode(moduleRefRows, strings, modules,
                a3LayoutResourceLedger);
        var moduleRefs = moduleRefRows.Count == 0
            ? default
            : new TrRmModuleRefTableReader(moduleRefBytes,
                moduleRefRows.Count, strings, modules,
                a3LayoutResourceLedger);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            0, 0, 0, 0, schema,
            methodDefCount: program.Functions.Length + 1,
            resourceLedger: a3LayoutResourceLedger);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, 0, 0, 0, 0, schema,
            methodDefCount: program.Functions.Length + 1,
            resourceLedger: a3LayoutResourceLedger);
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, parameterRows.Count, 0,
            a3LayoutResourceLedger);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count, typeSpecs, parameterRows.Count, 0,
            a3LayoutResourceLedger);
        var paramBytes = parameterRows.Count == 0
            ? []
            : TrRmParamTableWriter.Encode(parameterRows, signatures, typeSpecs,
                schema, a3LayoutResourceLedger);
        var parameters = parameterRows.Count == 0
            ? default
            : new TrRmParamTableReader(paramBytes, parameterRows.Count,
                signatures, typeSpecs, schema, a3LayoutResourceLedger);
        var anchorOwner = 0x1600_0000u | (uint)anchorMethodRow;
        ReserveA3Collection(a3LayoutResourceLedger, 0);
        var contractRows = new List<TrRmContractRow>();
        ReserveA3Buffer(a3LayoutResourceLedger, sizeof(uint));
        var contractPayload = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(contractPayload, 4);
        ReserveA3Array(a3LayoutResourceLedger, program.Functions.Length,
            sizeof(int));
        var importContractFirstByFunction
            = new int[program.Functions.Length];
        ReserveA3Array(a3LayoutResourceLedger, program.Functions.Length,
            sizeof(int));
        var importContractCountByFunction
            = new int[program.Functions.Length];
        ReserveA3SortWork(a3LayoutResourceLedger,
            import.Parameters.Length);
        ReserveA3Array(a3LayoutResourceLedger, import.Parameters.Length);
        var selectedNativeParameters = import.Parameters
            .OrderBy(static parameter => parameter.ParameterIndex).ToArray();
        ReserveA3Collection(a3LayoutResourceLedger,
            selectedNativeParameters.Length);
        var selectedNativeParameterPayloads
            = new List<byte[]>(selectedNativeParameters.Length);
        foreach (var functionIndex in nativeImportFunctionIndices)
        {
            a3LayoutResourceLedger?.ConsumeWork();
            var nativeImport = program.Functions[functionIndex]
                .DirectNativeImport!;
            ReserveA3SortWork(a3LayoutResourceLedger,
                nativeImport.Parameters.Length);
            ReserveA3Array(a3LayoutResourceLedger,
                nativeImport.Parameters.Length);
            var nativeParameters = nativeImport.Parameters
                .OrderBy(static parameter => parameter.ParameterIndex)
                .ToArray();
            var owner = 0x1600_0000u
                | checked((uint)(functionIndex + 1));
            importContractFirstByFunction[functionIndex]
                = contractRows.Count + 1;
            AddA3Staged(contractRows,
                new TrRmContractRow(owner, 1, contractPayload),
                a3LayoutResourceLedger);
            foreach (var parameter in nativeParameters)
            {
                a3LayoutResourceLedger?.ConsumeWork();
                if (parameter.Retention != NativeParameterRetention.NoEscape)
                {
                    throw Unsupported(
                        "The NativeAddress producer requires NoEscape NativeParameter contracts.");
                }

                ReserveA3Buffer(a3LayoutResourceLedger, 12);
                var payload = new byte[12];
                BinaryPrimitives.WriteUInt32LittleEndian(payload,
                    checked((uint)parameter.ParameterIndex));
                payload[4] = checked((byte)((byte)parameter.Access + 1));
                payload[5] = 1;
                payload[6] = parameter.Extent == 0 ? (byte)0 : (byte)1;
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8),
                    parameter.Extent);
                AddA3Staged(contractRows,
                    new TrRmContractRow(owner, 0x0E, payload),
                    a3LayoutResourceLedger);
                if (functionIndex == targetIndex)
                {
                    AddA3Staged(selectedNativeParameterPayloads, payload,
                        a3LayoutResourceLedger);
                }
            }

            importContractCountByFunction[functionIndex]
                = nativeParameters.Length + 1;
        }

        var anchorContractFirst = selectedNativeParameters.Length == 0
            ? 0 : contractRows.Count + 1;
        foreach (var payload in selectedNativeParameterPayloads)
        {
            AddA3Staged(contractRows,
                new TrRmContractRow(anchorOwner, 0x0E, payload),
                a3LayoutResourceLedger);
        }

        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, contractRows.Count, schema,
            a3LayoutResourceLedger);
        ReserveA3Collection(a3LayoutResourceLedger,
            program.Functions.Length + 1);
        var methodRows = new List<TrRmMethodDefRow>(program.Functions.Length + 1);
        ReserveA3Collection(a3LayoutResourceLedger, 0);
        var exportRows = new List<TrRmExportRow>();
        for (var index = 0; index < program.Functions.Length; index++)
        {
            a3LayoutResourceLedger?.ConsumeWork();
            var function = program.Functions[index];
            var methodRow = index + 1;
            var visibility = function.IsPrivate ? 0u : function.IsInternal ? 1u : 2u;
            var isCaller = index == callerIndex;
            AddA3Staged(methodRows,
                new TrRmMethodDefRow(0, methodNameRows[index],
                    functionSignatureRows[index], visibility,
                    importContractFirstByFunction[index],
                    importContractCountByFunction[index],
                    isCaller ? 1 : 0),
                a3LayoutResourceLedger);
            if (visibility == 2 && function.ModuleImport is null)
            {
                AddA3Staged(exportRows,
                    new TrRmExportRow(
                        0x1600_0000u | (uint)methodRow,
                        methodNameRows[index]),
                    a3LayoutResourceLedger);
            }
        }
        AddA3Staged(methodRows,
            new TrRmMethodDefRow(0, anchorNameRow, anchorSignatureRow, 0,
                anchorContractFirst, selectedNativeParameters.Length, 0),
            a3LayoutResourceLedger);
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows, strings,
            modules, typeDefs, signatures, contractRows.Count, 1,
            a3LayoutResourceLedger);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodRows.Count, strings, modules, typeDefs, signatures,
            contractRows.Count, 1, a3LayoutResourceLedger);
        var importRowCapacity = moduleImport is null ? 0 : 1;
        ReserveA3Collection(a3LayoutResourceLedger, importRowCapacity);
        var importRows = new List<TrRmImportRow>(importRowCapacity);
        if (moduleImport is not null)
        {
            AddA3Staged(importRows,
                new TrRmImportRow(1, providerExportKeyRow,
                    0x1600_0000u | (uint)targetMethodRow),
                a3LayoutResourceLedger);
        }

        var importBytes = importRows.Count == 0
            ? []
            : TrRmImportTableWriter.Encode(importRows, strings, moduleRefs,
                typeDefs, methods, default, default, default, default,
                featureReader, modules,
                schema, resourceLedger: a3LayoutResourceLedger);
        var imports = importRows.Count == 0
            ? default
            : new TrRmImportTableReader(importBytes, importRows.Count,
                strings, moduleRefs, typeDefs, methods, default, default,
                default, default, featureReader, modules,
                schema, resourceLedger: a3LayoutResourceLedger);
        var contractBytes = TrRmContractTableWriter.EncodeForeignInvocationDraft(
            contractRows, typeDefs, methods, signatures, parameters, typeSpecs,
            featureReader, modules, predicateGraphCount: 0, schema: schema,
            resourceLedger: a3LayoutResourceLedger);
        var contracts = new TrRmContractTableReader(contractBytes,
            contractRows.Count, typeDefs, methods, signatures, parameters,
            typeSpecs, 0, featureReader, modules,
            schema, resourceLedger: a3LayoutResourceLedger);
        var targetProfileBytes = fixedArrayLayoutRows.Count == 0
            ? []
            : TrRmTargetProfileTableWriter.Encode([
                new TrRmTargetProfileRow(
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader
                        .WindowsX64NativeProfileRuleRevision),
            ], schema, a3LayoutResourceLedger);
        var targetProfiles = fixedArrayLayoutRows.Count == 0
            ? new TrRmTargetProfileTableReader([], 0, schema,
                a3LayoutResourceLedger)
            : new TrRmTargetProfileTableReader(targetProfileBytes, 1, schema,
                a3LayoutResourceLedger);
        var layouts = new TrRmLayoutTableReader([], 0, typeDefs,
            targetProfiles, 0, a3LayoutResourceLedger);
        ReserveA3Collection(a3LayoutResourceLedger,
            nativeImportFunctionIndices.Count);
        var nativeImportRows = new List<TrRmNativeImportRow>(
            nativeImportFunctionIndices.Count);
        foreach (var functionIndex in nativeImportFunctionIndices)
        {
            a3LayoutResourceLedger?.ConsumeWork();
            var nativeImport = program.Functions[functionIndex]
                .DirectNativeImport!;
            AddA3Staged(nativeImportRows,
                new TrRmNativeImportRow(1,
                    (byte)nativeImport.ResolutionMode,
                    (byte)nativeImport.ResolutionPolicy,
                    (byte)nativeImport.CallingConvention, functionIndex + 1,
                    nativeImportLibraryNameRows[functionIndex],
                    nativeImportEntryNameRows[functionIndex]),
                a3LayoutResourceLedger);
        }

        var nativeImportBytes = TrRmNativeImportTableWriter.Encode(
            nativeImportRows, strings, methods, default, typeDefs, imports, modules,
            signatures, parameters, typeSpecs, contracts, layouts,
            a3LayoutResourceLedger);
        var stagedCallCapacity = indirectCall?.Arguments.Length ?? 0;
        ReserveA3Collection(a3LayoutResourceLedger, stagedCallCapacity);
        var stagedCalls = new List<BoundCall>(stagedCallCapacity);
        if (indirectCall is not null)
        {
            foreach (var argument in indirectCall.Arguments)
            {
                if (argument is BoundCall stagedCall)
                {
                    AddA3Staged(stagedCalls, stagedCall,
                        a3LayoutResourceLedger);
                }
            }
        }

        var stagedResultHomeCount = Math.Max(0, stagedCalls.Count - 1);
        var stagedResultHomeStart = 1 + (callResultHomeIndex >= 0 ? 1 : 0);
        var stagedResultTypeMapToken = stagedResultHomeCount == 0
            ? 0u
            : CreateStandardBodyTypeMapToken(
                GetSignatureTypeSpecRef(signature.ReturnType));
        var unitCallStorageArgumentCount = 0;
        if (storageCall is not null)
        {
            foreach (var unitCall in storageCall.UnitInitializerCalls)
            {
                a3LayoutResourceLedger?.ConsumeWork();
                foreach (var argument in unitCall.Call.Arguments)
                {
                    a3LayoutResourceLedger?.ConsumeWork();
                    if (argument is BoundNativeStorageAddress)
                    {
                        unitCallStorageArgumentCount++;
                    }
                }
            }
        }
        var unitCallTemporaryHomes = storageCall is null
            ? []
            : GetNativeAddressUnitCallHomes(storageCall.UnitInitializerCalls,
                storageCall.ConditionCaptures,
                callerLocalTypeSpecRefs.Length,
                a3LayoutResourceLedger);
        var arrayElementTypeMapTokenCount = 0;
        if (storageCall is not null)
        {
            for (var localOrdinal = 0;
                localOrdinal < callerLocalTypeSpecRefs.Length; localOrdinal++)
            {
                a3LayoutResourceLedger?.ConsumeWork();
                var arrayType = caller.Variables[
                    caller.ParameterCount + localOrdinal].Type;
                var remainingArrayTypes
                    = program.NativeFixedArrayTypes.Length;
                while (NativeFixedArrayTypes.IsType(arrayType))
                {
                    if (remainingArrayTypes-- <= 0)
                    {
                        throw Unsupported(
                            "The A2 array type graph contains a cycle.");
                    }

                    var arrayDefinition = program.NativeFixedArrayTypes[
                        NativeFixedArrayTypes.Index(arrayType)];
                    if (arrayDefinition.ElementCount == 0)
                    {
                        break;
                    }

                    arrayElementTypeMapTokenCount = checked(
                        arrayElementTypeMapTokenCount + 1);
                    arrayType = arrayDefinition.ElementType;
                }
            }
        }
        var typeMapTokenCapacity = storageCall is null
            ? 1 + (callResultHomeIndex >= 0 ? 1 : 0)
                + stagedResultHomeCount + callerParameterTypeSpecRefs.Length
            : callerLocalTypeSpecRefs.Length
                + unitCallTemporaryHomes.Length
                + unitCallStorageArgumentCount
                + storageCall.StorageArguments.Length
                + callerParameterTypeSpecRefs.Length
                + arrayElementTypeMapTokenCount;
        ReserveA3Collection(a3LayoutResourceLedger, typeMapTokenCapacity);
        var typeMapTokens = new List<uint>(typeMapTokenCapacity);
        if (storageCall is null)
        {
            AddA3Staged(typeMapTokens, 0x1100_0001u,
                a3LayoutResourceLedger);
            if (callResultHomeIndex >= 0)
            {
                AddA3Staged(typeMapTokens,
                    CreateStandardBodyTypeMapToken(
                        GetSignatureTypeSpecRef(caller.ReturnType)),
                    a3LayoutResourceLedger);
            }
            if (stagedResultHomeCount > 0)
            {
                AddA3Staged(typeMapTokens, stagedResultTypeMapToken,
                    a3LayoutResourceLedger);
            }

            foreach (var typeSpecRef in callerParameterTypeSpecRefs)
            {
                AddA3Staged(typeMapTokens,
                    CreateStandardBodyTypeMapToken(typeSpecRef),
                    a3LayoutResourceLedger);
            }
        }
        else
        {
            for (var localOrdinal = 0;
                localOrdinal < callerLocalTypeSpecRefs.Length; localOrdinal++)
            {
                a3LayoutResourceLedger?.ConsumeWork();
                var variable = caller.Variables[
                    caller.ParameterCount + localOrdinal];
                AddA3Staged(typeMapTokens,
                    NativeFunctionPointerTypes.IsType(variable.Type)
                        ? 0x1100_0001u
                        : CreateStandardBodyTypeMapToken(
                            callerLocalTypeSpecRefs[localOrdinal]),
                    a3LayoutResourceLedger);
            }

            foreach (var temporaryHome in unitCallTemporaryHomes)
            {
                AddA3Staged(typeMapTokens,
                    CreateStandardBodyTypeMapToken(
                        GetSignatureTypeSpecRef(temporaryHome.Type)),
                    a3LayoutResourceLedger);
            }

            foreach (var unitCall in storageCall.UnitInitializerCalls)
            {
                for (var argumentIndex = 0;
                    argumentIndex < unitCall.Call.Arguments.Length;
                    argumentIndex++)
                {
                    a3LayoutResourceLedger?.ConsumeWork();
                    if (unitCall.Call.Arguments[argumentIndex]
                        is not BoundNativeStorageAddress)
                    {
                        continue;
                    }

                    var parameterIndex = GetBoundCallParameterIndex(
                        unitCall.Call, argumentIndex);
                    AddA3Staged(typeMapTokens,
                        CreateStandardBodyTypeMapToken(
                            GetSignatureTypeSpecRef(
                                unitCall.Signature.ParameterTypes[
                                    parameterIndex])),
                        a3LayoutResourceLedger);
                }
            }

            foreach (var storageArgument in storageCall.StorageArguments)
            {
                AddA3Staged(typeMapTokens,
                    CreateStandardBodyTypeMapToken(
                        GetSignatureTypeSpecRef(signature.ParameterTypes[
                            storageArgument.ParameterOrdinal])),
                    a3LayoutResourceLedger);
            }

            foreach (var typeSpecRef in callerParameterTypeSpecRefs)
            {
                AddA3Staged(typeMapTokens,
                    CreateStandardBodyTypeMapToken(typeSpecRef),
                    a3LayoutResourceLedger);
            }

            for (var localOrdinal = 0;
                localOrdinal < callerLocalTypeSpecRefs.Length; localOrdinal++)
            {
                a3LayoutResourceLedger?.ConsumeWork();
                var variable = caller.Variables[
                    caller.ParameterCount + localOrdinal];
                if (!NativeFixedArrayTypes.IsType(variable.Type))
                {
                    continue;
                }

                var arrayType = variable.Type;
                var remainingArrayTypes
                    = program.NativeFixedArrayTypes.Length;
                while (NativeFixedArrayTypes.IsType(arrayType))
                {
                    if (remainingArrayTypes-- <= 0)
                    {
                        throw Unsupported(
                            "The A2 array type graph contains a cycle.");
                    }

                    var arrayDefinition = program.NativeFixedArrayTypes[
                        NativeFixedArrayTypes.Index(arrayType)];
                    if (arrayDefinition.ElementCount == 0)
                    {
                        break;
                    }

                    AddA3Staged(typeMapTokens,
                        CreateStandardBodyTypeMapToken(
                            GetSignatureTypeSpecRef(
                                arrayDefinition.ElementType)),
                        a3LayoutResourceLedger);
                    arrayType = arrayDefinition.ElementType;
                }
            }
        }

        var targetMethodMapToken = moduleImport is null
            ? 0x1600_0000u | (uint)targetMethodRow
            : 0x1C00_0001u;
        ReserveA3Collection(a3LayoutResourceLedger, 0);
        var methodMapCapacity = storageCall is null
            ? 1 : checked(storageCall.UnitInitializerCalls.Length + 1);
        ReserveA3Collection(a3LayoutResourceLedger, methodMapCapacity);
        var methodMapTokens = new List<uint>(methodMapCapacity);
        ReserveA3Collection(a3LayoutResourceLedger, methodMapCapacity);
        var methodMapTokenSet = new HashSet<uint>(methodMapCapacity);
        if (storageCall is null)
        {
            AddA3Staged(methodMapTokens, targetMethodMapToken,
                a3LayoutResourceLedger);
        }
        else
        {
            ReserveA3Collection(a3LayoutResourceLedger, 0);
            var callerStatements = new List<BoundStatement>();
            if (!AppendStatements(caller.Body, callerStatements,
                    a3LayoutResourceLedger))
            {
                throw Unsupported(
                    "The F+S caller Body cannot be flattened for its MethodMap.");
            }

            void AddCallMethodMapTokens(BoundCall call)
            {
                a3LayoutResourceLedger?.ConsumeWork();
                foreach (var argument in call.Arguments)
                {
                    a3LayoutResourceLedger?.ConsumeWork();
                    if (argument is BoundCall nestedCall)
                    {
                        AddCallMethodMapTokens(nestedCall);
                    }
                }

                var methodToken = 0x1600_0000u
                    | checked((uint)(call.Function + 1));
                if (AddA3Staged(methodMapTokenSet, methodToken,
                        a3LayoutResourceLedger))
                {
                    AddA3Staged(methodMapTokens, methodToken,
                        a3LayoutResourceLedger);
                }
            }

            foreach (var statement in callerStatements)
            {
                a3LayoutResourceLedger?.ConsumeWork();
                if (statement is BoundLocal
                    { Initializer: BoundNativeFunctionAddress })
                {
                    if (AddA3Staged(methodMapTokenSet,
                            targetMethodMapToken,
                            a3LayoutResourceLedger))
                    {
                        AddA3Staged(methodMapTokens, targetMethodMapToken,
                            a3LayoutResourceLedger);
                    }
                }
            }

            foreach (var unitCall in storageCall.UnitInitializerCalls)
            {
                AddCallMethodMapTokens(unitCall.Call);
            }
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeMapTokens,
            methodMapTokens, a3LayoutResourceLedger);
        byte[] body;
        if (storageCall is not null)
        {
            body = EncodeForeignInvocationStorageAddressBody(caller,
                program, storageCall, signature, maps, targetMethodMapToken,
                contractRows.Count, GetSignatureTypeSpecRef,
                fixedArrayLayoutRowByType, schema,
                scalarValues, a3LayoutResourceLedger);
        }
        else
        {
        ReserveA3Buffer(a3LayoutResourceLedger, 8);
        var entryState = new byte[8];
        entryState[0] = 0x01;
        var nativeAddress = TrRmRegisterBodyRecordWriter.EncodeNativeAddress(
            emitsIndirectCall ? (byte)2 : (byte)1,
            emitsIndirectCall ? checked((ushort)callHomeIndex) : (ushort)15,
            maps.GetMethodMapIndex(targetMethodMapToken),
            maps.GetTypeMapIndex(0x1100_0001),
            a3LayoutResourceLedger);
        var callLocalHomeCount = stagedResultHomeStart
            + stagedResultHomeCount;
        var homeMapLength = emitsIndirectCall
            ? checked((callerParameterTypeSpecRefs.Length
                + callLocalHomeCount) * 8)
            : 0;
        ReserveA3Buffer(a3LayoutResourceLedger, homeMapLength);
        var homeMap = emitsIndirectCall
            ? new byte[homeMapLength]
            : [];
        var callInputCapacity = indirectCall?.Arguments.Length ?? 0;
        ReserveA3Collection(a3LayoutResourceLedger, callInputCapacity);
        var callInputs = new List<TrRmRegisterBodyCallInput>(
            callInputCapacity);
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
                    AddA3Staged(callInputs,
                        new TrRmRegisterBodyCallInput(2, 1,
                            checked((ushort)(read.Variable
                                + callLocalHomeCount))),
                        a3LayoutResourceLedger);
                }
                else if (argument is BoundCall)
                {
                    AddA3Staged(callInputs,
                        stagedArgumentCallOrdinal < stagedCalls.Count - 1
                            ? new TrRmRegisterBodyCallInput(2, 1,
                                checked((ushort)(stagedResultHomeStart
                                    + stagedArgumentCallOrdinal)))
                            : new TrRmRegisterBodyCallInput(1, 1, 0),
                        a3LayoutResourceLedger);
                    stagedArgumentCallOrdinal++;
                }
                else
                {
                    throw Unsupported(
                        "The NativeAddress CallSite supports only formal reads and staged direct-call results.");
                }
            }
        }
        var recordCapacity = 2 + (emitsIndirectCall
            ? stagedCalls.Count + 1 : 0);
        ReserveA3Collection(a3LayoutResourceLedger, recordCapacity);
        var records = new List<ReadOnlyMemory<byte>>(recordCapacity);
        AddA3Staged(records, entryState, a3LayoutResourceLedger);
        AddA3Staged(records, nativeAddress, a3LayoutResourceLedger);
        var codeCapacity = checked(6 + (emitsIndirectCall
            ? 4 * (stagedCalls.Count + 1) : 0));
        ReserveA3Buffer(a3LayoutResourceLedger, codeCapacity);
        var code = new List<byte>(codeCapacity) { 0xE9, 0, 1, 0 };
        var returnsCallResult = indirectCall is not null
            && indirectCall.Type != PrimitiveType.Void;
        if (emitsIndirectCall)
        {
            for (var stagedInstructionOrdinal = 0;
                stagedInstructionOrdinal < stagedCalls.Count;
                stagedInstructionOrdinal++)
            {
                var stagedCall = stagedCalls[stagedInstructionOrdinal];
                ReserveA3Collection(a3LayoutResourceLedger,
                    stagedCall.Arguments.Length);
                var stagedInputs = new List<TrRmRegisterBodyCallInput>(
                    stagedCall.Arguments.Length);
                foreach (var argument in stagedCall.Arguments)
                {
                    a3LayoutResourceLedger?.ConsumeWork();
                    var read = (BoundRead)argument;
                    AddA3Staged(stagedInputs,
                        new TrRmRegisterBodyCallInput(2, 1,
                            checked((ushort)(read.Variable
                                + callLocalHomeCount))),
                        a3LayoutResourceLedger);
                }

                var stagedCallIndex = checked((ushort)records.Count);
                var isFinalStagedCall
                    = stagedInstructionOrdinal == stagedCalls.Count - 1;
                AddA3Staged(records,
                    TrRmRegisterBodyRecordWriter.EncodeCallSite(
                        1, isFinalStagedCall ? (byte)1 : (byte)2,
                        maps.GetMethodMapIndex(targetMethodMapToken),
                        isFinalStagedCall ? (ushort)0
                            : checked((ushort)(stagedResultHomeStart
                                + stagedInstructionOrdinal)),
                        stagedInputs, [], a3LayoutResourceLedger),
                    a3LayoutResourceLedger);
                AppendCallSiteInstruction(code, stagedCallIndex,
                    a3LayoutResourceLedger);
            }

            var callResultKind = !returnsCallResult ? (byte)0
                : callResultHomeIndex >= 0 ? (byte)2 : (byte)1;
            var callResultIndex = callResultHomeIndex >= 0
                ? checked((ushort)callResultHomeIndex) : (ushort)0;
            var callSiteIndex = checked((ushort)records.Count);
            AddA3Staged(records,
                TrRmRegisterBodyRecordWriter.EncodeCallSite(
                    3, callResultKind, checked((ushort)callHomeIndex),
                    callResultIndex, callInputs, [],
                    a3LayoutResourceLedger),
                a3LayoutResourceLedger);
            AppendCallSiteInstruction(code, callSiteIndex,
                a3LayoutResourceLedger);
        }

        if (returnsCallResult && callResultHomeIndex >= 0)
        {
            a3LayoutResourceLedger?.ConsumeWork(2);
            code.Add(0xB8);
            code.Add(checked((byte)callResultHomeIndex));
        }
        else
        {
            a3LayoutResourceLedger?.ConsumeWork(2);
            code.Add(returnsCallResult ? (byte)0x17 : (byte)0x18);
            code.Add(0);
        }
        a3LayoutResourceLedger?.ConsumeWork(checked(
            (ulong)code.Count + 1UL));
        var codeBytes = code.ToArray();
        a3LayoutResourceLedger?.ConsumeWork(2);
        ReserveA3Array(a3LayoutResourceLedger, 1,
            3 * sizeof(uint));
        var bodyBlocks = new[]
        {
            new TrRmRegisterBodyBlock(0, (uint)codeBytes.Length, 0),
        };
        body = TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, default,
            bodyBlocks,
            records, codeBytes, contractRows.Count,
            schema, a3LayoutResourceLedger);
        }
        var scalarValueTables = scalarValues?.Encode(typeSpecs);
        var hasScalarValues
            = scalarValueTables is TrRmFrozenScalarValueTables {
                ConstantSourceCount: > 0,
            };
        if ((storageCall?.UsesScalarFrozenValues == true) != hasScalarValues)
        {
            throw new InvalidOperationException(
                "The A2EU+SF1 constant plan differs from its emitted value tables.");
        }

        ReserveA3Collection(a3LayoutResourceLedger, 15);
        var sections = new List<TrRmSection>(15);
        AddA3Staged(sections, new TrRmSection(0x0001, 0, 1, moduleBytes),
            a3LayoutResourceLedger);
        AddA3Staged(sections, new TrRmSection(0x0002, 0,
            (uint)stringRows.Count, stringsBytes), a3LayoutResourceLedger);
        if (scalarValueTables is TrRmFrozenScalarValueTables frozenValues)
        {
            if (frozenValues.BlobCount != 0)
            {
                AddA3Staged(sections, new TrRmSection(0x0003, 0,
                    (uint)frozenValues.BlobCount, frozenValues.BlobSection),
                    a3LayoutResourceLedger);
            }

            if (frozenValues.ConstantSourceCount != 0)
            {
                AddA3Staged(sections, new TrRmSection(0x0030, 0,
                    (uint)frozenValues.ConstantSourceCount,
                    frozenValues.ConstantSourceSection),
                    a3LayoutResourceLedger);
                AddA3Staged(sections, new TrRmSection(0x003F, 0,
                    (uint)frozenValues.FrozenValueNodeCount,
                    frozenValues.FrozenValueNodeSection),
                    a3LayoutResourceLedger);
            }
        }
        AddA3Staged(sections, new TrRmSection(0x0004, 0,
            (uint)featureRows.Count, featureBytes), a3LayoutResourceLedger);
        AddA3Staged(sections, new TrRmSection(0x0011, 0,
            (uint)typeSpecRows.Count, typeSpecBytes),
            a3LayoutResourceLedger);
        AddA3Staged(sections, new TrRmSection(0x0016, 0,
            (uint)methodRows.Count, methodBytes), a3LayoutResourceLedger);
        AddA3Staged(sections, new TrRmSection(0x0017, 0,
            (uint)signatureRows.Count, signatureBytes),
            a3LayoutResourceLedger);
        AddA3Staged(sections, new TrRmSection(0x001B, 0,
            (uint)contractRows.Count, contractBytes),
            a3LayoutResourceLedger);
        if (moduleRefRows.Count != 0)
        {
            AddA3Staged(sections, new TrRmSection(0x0010, 0,
                (uint)moduleRefRows.Count, moduleRefBytes),
                a3LayoutResourceLedger);
        }
        if (parameterRows.Count != 0)
        {
            AddA3Staged(sections, new TrRmSection(0x0018, 0,
                (uint)parameterRows.Count, paramBytes),
                a3LayoutResourceLedger);
        }
        if (importRows.Count != 0)
        {
            AddA3Staged(sections, new TrRmSection(0x001C, 0,
                (uint)importRows.Count, importBytes),
                a3LayoutResourceLedger);
        }
        if (exportRows.Count != 0)
        {
            AddA3Staged(sections,
                new TrRmSection(0x001D, 0, (uint)exportRows.Count,
                    TrRmExportTableWriter.Encode(exportRows, strings,
                        typeDefs, methods, default, imports, default, default,
                        default, featureReader, modules, schema,
                        resourceLedger: a3LayoutResourceLedger)),
                a3LayoutResourceLedger);
        }
        if (fixedArrayLayoutRows.Count != 0)
        {
            var fixedArrayLayoutBytes
                = TrRmFixedArrayLayoutTableWriter.Encode(
                    fixedArrayLayoutRows, typeSpecs, targetProfiles,
                    featureReader, modules, schema);
            AddA3Staged(sections, new TrRmSection(0x0012, 0,
                (uint)fixedArrayLayoutRows.Count, fixedArrayLayoutBytes),
                a3LayoutResourceLedger);
            AddA3Staged(sections,
                new TrRmSection(0x0048, 0, 1, targetProfileBytes),
                a3LayoutResourceLedger);
        }
        ReserveA3Array(a3LayoutResourceLedger, 1);
        var bodyRows = new ReadOnlyMemory<byte>[] { body };
        a3LayoutResourceLedger?.ConsumeWork(2);
        AddA3Staged(sections, new TrRmSection(0x0020, 0, 1,
            a3LayoutResourceLedger is null
                ? TrRmVariableTableWriter.EncodeBodies(bodyRows)
                : TrRmVariableTableWriter.EncodeBodies(bodyRows,
                    a3LayoutResourceLedger)), a3LayoutResourceLedger);
        AddA3Staged(sections, new TrRmSection(0x0021, 0,
            (uint)nativeImportRows.Count, nativeImportBytes),
            a3LayoutResourceLedger);
        a3LayoutResourceLedger?.ConsumeWork(checked(
            (ulong)sections.Count * (ulong)sections.Count));
        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));

        var artifact = TrRmArtifactWriter.Encode(sections,
            schema, a3LayoutResourceLedger);
        var artifactIndex = a3LayoutResourceLedger is not null
            ? new TrRmArtifactIndex(artifact, schema,
                a3LayoutResourceLedger)
            : schema.UsesForeignInvocationStorageFixedArrays()
            ? schema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayGenericCopyDraft(artifact)
                : schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
                    && schema.UsesScalarFrozenValues()
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                        artifact)
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
        var tables = a3LayoutResourceLedger is not null
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    artifactIndex, a3LayoutResourceLedger,
                    createA3ResourceLedger: false)
            : schema.UsesForeignInvocationStorageFixedArrays()
            ? schema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(artifactIndex)
                : schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
                    && schema.UsesScalarFrozenValues()
                ? TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                        artifactIndex)
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
            a3LayoutResourceLedger?.ConsumeWork();
            if (stringIndexes.TryGetValue(key, out var row))
            {
                return row;
            }

            row = stringRows.Count + 1;
            AddA3Staged(stringIndexes, key, row,
                a3LayoutResourceLedger);
            AddA3Staged(stringRows, key, a3LayoutResourceLedger);
            return row;
        }
    }

    private readonly record struct NativeAddressStorageArgument(
        int ParameterOrdinal, BoundNativeStorageAddress Address);

    private readonly record struct NativeAddressLocalDeclaration(
        int Variable, BoundExpression? Initializer, bool IsVacant);

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
        int ArrayInitializationIndexVariable,
        bool UsesScalarFrozenValues);

    private static ImmutableArray<NativeAddressUnitCallHome>
        GetNativeAddressUnitCallHomes(
            ImmutableArray<NativeAddressUnitInitializerCall> calls,
            ImmutableArray<NativeAddressUnitConditionCapture> captures,
            int localCount,
            TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        ReserveA3Collection(resourceLedger, 0);
        var homesByIndex = new SortedDictionary<int, PrimitiveType>();
        foreach (var call in calls)
        {
            resourceLedger?.ConsumeWork();
            if (call.ResultHome >= localCount
                && !TryAddA3Staged(homesByIndex, call.ResultHome,
                    call.Call.Type, resourceLedger))
            {
                throw Unsupported(
                    "The A2EU direct-call result Home is reused.");
            }

            for (var argumentIndex = 0;
                argumentIndex < call.ArgumentCaptureHomes.Length;
                argumentIndex++)
            {
                resourceLedger?.ConsumeWork();
                var home = call.ArgumentCaptureHomes[argumentIndex];
                if (home >= localCount
                    && !TryAddA3Staged(homesByIndex, home,
                        call.Call.Arguments[argumentIndex].Type,
                        resourceLedger))
                {
                    throw Unsupported(
                        "The A2EU argument capture Home is reused.");
                }
            }
        }

        foreach (var capture in captures)
        {
            resourceLedger?.ConsumeWork();
            if (capture.Home >= localCount
                && !TryAddA3Staged(homesByIndex, capture.Home,
                    capture.Condition.Left.Type, resourceLedger))
            {
                throw Unsupported(
                    "The A2EU condition capture Home is reused.");
            }
        }

        ReserveA3Array(resourceLedger, homesByIndex.Count,
            sizeof(int) + sizeof(PrimitiveType));
        var result = ImmutableArray.CreateBuilder<NativeAddressUnitCallHome>(
            homesByIndex.Count);
        var expectedHome = localCount;
        foreach (var entry in homesByIndex)
        {
            resourceLedger?.ConsumeWork();
            if (entry.Key != expectedHome++)
            {
                throw Unsupported(
                    "The A2EU temporary Homes are not contiguous after caller locals.");
            }

            result.Add(new NativeAddressUnitCallHome(entry.Key, entry.Value));
        }

        resourceLedger?.ConsumeWork(checked(1UL
            + ((ulong)result.Count
                * (sizeof(int) + sizeof(PrimitiveType)))));
        return result.MoveToImmutable();
    }

    private static bool TryGetNativeAddressStorageCall(BoundProgram program,
        BoundFunction function, BoundNativeFunctionAddress address,
        NativeFunctionPointerSignatureDefinition signature,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger,
        out NativeAddressStorageCallPlan? plan)
    {
        plan = null;
        ReserveA3Collection(resourceLedger, 0);
        var statements = new List<BoundStatement>();
        if (!AppendStatements(function.Body, statements, resourceLedger)
            || statements.Count < 4
            || statements[^1] is not BoundReturn
                { Value: BoundRead returnRead }
            || returnRead.Type != function.ReturnType
            || (uint)returnRead.Variable >= (uint)function.Variables.Length)
        {
            return false;
        }

        ReserveA3Array(resourceLedger, statements.Count);
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

        ReserveA3Array(resourceLedger, statements.Count);
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

        ReserveA3Array(resourceLedger, callStatements[0].index);
        var topLevelLocalDeclarations = statements
            .Take(callStatements[0].index)
            .OfType<BoundLocal>()
            .ToArray();
        ReserveA3Collection(resourceLedger, 0);
        var localDeclarations
            = new List<NativeAddressLocalDeclaration>();
        void CollectLocalDeclarations(BoundStatement statement)
        {
            resourceLedger?.ConsumeWork();
            switch (statement)
            {
                case BoundLocal local:
                    AddA3Staged(localDeclarations,
                        new NativeAddressLocalDeclaration(local.Variable,
                            local.Initializer, false),
                        resourceLedger);
                    break;

                case BoundVacantLocal vacant:
                    AddA3Staged(localDeclarations,
                        new NativeAddressLocalDeclaration(vacant.Variable,
                            null, true),
                        resourceLedger);
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

        ReserveA3Collection(resourceLedger, 0);
        var nullaryArrayInitializationVariables = new HashSet<int>();
        bool IsA2EEmptyArrayInitialization(BoundAssignment assignment,
            out BoundNativeFixedArrayInitialization initialization)
        {
            return IsSupportedA2EEmptyArrayInitialization(assignment,
                function, program, schema, out initialization);
        }

        bool CollectA2EEmptyArrayInitializations(
            BoundStatement statement)
        {
            resourceLedger?.ConsumeWork();
            switch (statement)
            {
                case BoundAssignment assignment:
                    if (IsA2EEmptyArrayInitialization(assignment,
                            out _))
                    {
                        _ = AddA3Staged(
                            nullaryArrayInitializationVariables,
                            assignment.Variable, resourceLedger);
                        return true;
                    }

                    return assignment.Value is BoundRead;

                case BoundBlock block:
                    foreach (var nested in block.Statements)
                    {
                        if (!CollectA2EEmptyArrayInitializations(nested))
                        {
                            return false;
                        }
                    }
                    return true;

                case BoundUnsafe unsafeBlock:
                    return CollectA2EEmptyArrayInitializations(
                        unsafeBlock.Body);

                case BoundIf branch:
                    return CollectA2EEmptyArrayInitializations(branch.Then)
                        && (branch.Else is null
                            || CollectA2EEmptyArrayInitializations(
                                branch.Else));

                default:
                    return true;
            }
        }

        bool IsSupportedStorageCallStatement(BoundStatement statement)
        {
            return statement is BoundLocal or BoundVacantLocal or BoundIf
                or BoundExpressionStatement
                    { Expression: BoundCall }
                or BoundExpressionStatement
                    { Expression: BoundConditional
                        { Type: PrimitiveType.Void } };
        }

        foreach (var statement in statements.Take(callStatements[0].index))
        {
            if (!CollectA2EEmptyArrayInitializations(statement))
            {
                return false;
            }
        }

        if (localDeclarations.Count
                != function.Variables.Length - function.ParameterCount
            || statements.Take(callStatements[0].index)
                .Any(statement => !IsSupportedStorageCallStatement(statement)
                    && statement is not BoundAssignment
                        { Value: BoundNativeFixedArrayInitialization or BoundRead }))
        {
            return false;
        }

        ReserveA3Collection(resourceLedger, 0);
        var localVariables = new HashSet<int>();
        foreach (var declaration in localDeclarations)
        {
            if (declaration.Variable < function.ParameterCount
                || declaration.Variable >= function.Variables.Length
                || !AddA3Staged(localVariables, declaration.Variable,
                    resourceLedger))
            {
                return false;
            }

            var variable = function.Variables[declaration.Variable];
            if (variable.IsOut || variable.IsInit || variable.IsImplicitRef
                || variable.IsImplicitReferenceTemporary
                || variable.IsInitializationLocal != declaration.IsVacant)
            {
                return false;
            }

            if (declaration.IsVacant)
            {
                if (!NativeFixedArrayTypes.IsType(variable.Type)
                    || !nullaryArrayInitializationVariables.Contains(
                        declaration.Variable)
                    || !NativeFixedArrayContract.TryGetLayout(variable.Type,
                        program.Structures, program.NativeFixedArrayTypes,
                        out var emptyArrayLayout)
                    || emptyArrayLayout.ElementCount != 0)
                {
                    return false;
                }

                continue;
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
                var hasSupportedArrayCopy
                    = declaration.Initializer is BoundRead arrayRead
                        && arrayRead.Variable != declaration.Variable
                        && (uint)arrayRead.Variable
                            < (uint)function.Variables.Length
                        && localVariables.Contains(arrayRead.Variable)
                        && arrayRead.Type == variable.Type
                        && arrayRead.Type
                            == function.Variables[arrayRead.Variable].Type;
                if (schema is not (
                        TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft)
                    || !NativeFixedArrayContract.TryGetLayout(variable.Type,
                        program.Structures, program.NativeFixedArrayTypes,
                            out _)
                    || (declaration.Initializer
                            is not BoundNativeFixedArrayInitialization
                                arrayInitialization
                        && !hasSupportedArrayCopy)
                    || (declaration.Initializer
                            is BoundNativeFixedArrayInitialization selectedArrayInitialization
                        && !IsSupportedNativeFixedArrayInitialization(
                            variable.Type, selectedArrayInitialization, program,
                            schema)))
                {
                    return false;
                }
            }
            else if (declaration.Initializer is BoundRead read
                && read.Variable != declaration.Variable
                && (uint)read.Variable < (uint)function.Variables.Length
                && read.Type == variable.Type
                && read.Type == function.Variables[read.Variable].Type
                && IsSupportedNativeAddressValueType(read.Type)
                && (read.Variable < function.ParameterCount
                    || localVariables.Contains(read.Variable)))
            {
            }
            else if (declaration.Initializer is BoundNativePointerAddress
                    or BoundNativeFixedArrayPointerAddress
                && NativePointerTypes.IsType(variable.Type)
                && declaration.Initializer.Type == variable.Type)
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
            else if (declaration.Initializer is BoundExpression arrayRead
                && arrayRead.Type == variable.Type
                && IsSupportedNativeFixedArrayRead(arrayRead, function,
                    program, localVariables))
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

        foreach (var statement in statements.Take(callStatements[0].index))
        {
            if (statement is BoundAssignment assignment
                && !IsA2EEmptyArrayInitialization(assignment, out _)
                && !IsSupportedNativeFixedArrayCopyAssignment(assignment,
                    function, program))
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

        ReserveA3Collection(resourceLedger, 0);
        var storageArguments = ImmutableArray.CreateBuilder<
            NativeAddressStorageArgument>();
        for (var ordinal = 0; ordinal < call.Arguments.Length; ordinal++)
        {
            resourceLedger?.ConsumeWork();
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
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft)
                        && IsSupportedNativeAddressStorageArray(
                            storageAddress, function, program,
                            localVariables)
                    : storageAddress.CarrierType
                            == storageAddress.ElementType
                        && storageAddress.ElementCount == 1
                        && storageAddress.ElementStride > 0
                        && storageAddress.ByteExtent
                            == storageAddress.ElementStride;
                if (isFixedArray && !isSupportedStorageType)
                {
                    throw Unsupported(
                    "Debug: the A2 nested address path failed its storage profile.");
                }

                if (nativeParameter is null
                    || !DirectNativeImportContract.MatchesAddress(
                    storageAddress, nativeParameter, function, program))
                {
                    throw Unsupported(
                    "Debug: the A2 storage address failed its NativeParameter contract.");
                }
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

                resourceLedger?.ConsumeWork();
                storageArguments.Add(new NativeAddressStorageArgument(
                    ordinal, storageAddress));
            }
            else if (argument is BoundRead read
                && (uint)read.Variable < (uint)function.Variables.Length
                && read.Type == function.Variables[read.Variable].Type
                && read.Type == signature.ParameterTypes[ordinal])
            {
            }
            else if (argument is BoundNativeFixedArrayElement
                    or BoundNativeFixedArrayPathElement
                && argument.Type == signature.ParameterTypes[ordinal]
                && IsSupportedNativeAddressValueType(argument.Type)
                && IsSupportedNativeFixedArrayRead(argument, function,
                    program, localVariables))
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

        ReserveA3Collection(resourceLedger, 0);
        var unitInitializerCalls
            = ImmutableArray.CreateBuilder<NativeAddressUnitInitializerCall>();
        ReserveA3Collection(resourceLedger, 0);
        var conditionCaptures
            = ImmutableArray.CreateBuilder<NativeAddressUnitConditionCapture>();
        var nextUnitCallResultHome
            = function.Variables.Length - function.ParameterCount;
        var conditionalEdgeCount = 0;
        var usesScalarFrozenValues = false;
        bool AddUnitInitializerCall(BoundCall unitCall,
            int initializerArrayVariable, int resultVariable)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            ReserveA3Array(resourceLedger, unitCall.Arguments.Length,
                sizeof(int));
            var argumentCaptureHomes = new int[unitCall.Arguments.Length];
            Array.Fill(argumentCaptureHomes, -1);
            for (var argumentIndex = 0;
                argumentIndex < unitCall.Arguments.Length;
                argumentIndex++)
            {
                resourceLedger?.ConsumeWork();
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

                if (argument is BoundNativeFixedArrayElement
                    or BoundNativeFixedArrayPathElement)
                {
                    if (!IsSupportedNativeAddressValueType(argument.Type)
                        || !IsSupportedNativeFixedArrayRead(argument,
                            function, program, localVariables))
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

            ReserveA3Array(resourceLedger, argumentCaptureHomes.Length,
                sizeof(int));
            resourceLedger?.ConsumeWork();
            unitInitializerCalls.Add(new NativeAddressUnitInitializerCall(
                unitCall, unitCallSignature, resultHome,
                [.. argumentCaptureHomes]));
            return true;
        }

        bool AddUnitInitializerExpression(BoundExpression expression,
            int initializerArrayVariable)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
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
                            conditional.Condition, schema) =>
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

        bool AddArrayInitializerCalls(
            BoundNativeFixedArrayInitialization initialization,
            int rootArrayVariable)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            foreach (var element in initialization.Elements)
            {
                resourceLedger?.ConsumeWork();
                if (element is BoundCall initializerCall)
                {
                    if (!AddUnitInitializerCall(initializerCall,
                            rootArrayVariable, -1))
                    {
                        return false;
                    }
                }
                else if (element is BoundConditional {
                        Type: PrimitiveType.Void,
                    } unitConditional)
                {
                    if (!AddUnitInitializerExpression(unitConditional,
                            rootArrayVariable))
                    {
                        return false;
                    }
                }
                else if (element
                    is BoundNativeFixedArrayInitialization nested
                    && !AddArrayInitializerCalls(nested,
                        rootArrayVariable))
                {
                    return false;
                }
            }

            return true;
        }

        bool AddUnitConditionalControlFlow(BoundExpression condition,
            Func<bool> appendTrue, Func<bool> appendFalse)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            if (!IsSupportedNativeUnitConditionalCondition(condition, schema))
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
                            comparison, schema)
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

                    if (comparison.Left.Type != PrimitiveType.Bool
                        && (comparison.Left is BoundConstant
                            || comparison.Right is BoundConstant))
                    {
                        usesScalarFrozenValues = true;
                    }

                    if (comparison.Left is BoundRead
                        && comparison.Right is BoundCall)
                    {
                        resourceLedger?.ConsumeWork();
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
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            switch (statement)
            {
            case BoundBlock block when block.RetiredManagedLocals.IsEmpty:
                foreach (var nested in block.Statements)
                {
                    resourceLedger?.ConsumeWork();
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
                    if (!AddArrayInitializerCalls(initialization,
                            local.Variable))
                    {
                        return false;
                    }
                }
                return true;

            case BoundVacantLocal:
                return true;

            case BoundAssignment assignment:
                return IsA2EEmptyArrayInitialization(assignment, out _)
                    || IsSupportedNativeFixedArrayCopyAssignment(
                        assignment, function, program);

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
                    && !IsSupportedNativeAddressStorageIndex(
                        argument.Address.Index, function, localVariables)))
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
            arrayInitializationIndexVariable,
            usesScalarFrozenValues);
        return true;
    }

    private static bool IsSupportedNativeAddressStorageArray(
        BoundNativeStorageAddress address, BoundFunction caller,
        BoundProgram program, HashSet<int> localVariables)
    {
        if (!NativeFixedArrayTypes.IsType(address.CarrierType)
            || (uint)address.Variable >= (uint)caller.Variables.Length
            || caller.Variables[address.Variable].Type
                != address.CarrierType
            || address.Variable < caller.ParameterCount
            || !localVariables.Contains(address.Variable)
            || !NativeFixedArrayContract.TryGetLayout(
                address.CarrierType, program.Structures,
                program.NativeFixedArrayTypes, out var rootLayout))
        {
            return false;
        }

        BoundNativeFixedArrayLayout leafLayout;
        if (address.Projections.IsDefaultOrEmpty)
        {
            leafLayout = rootLayout;
            if (!IsSupportedNativeAddressStorageIndex(address.Index,
                    caller, localVariables))
            {
                return false;
            }
        }
        else
        {
            if (!NativeFixedArrayContract.MatchesPath(
                    address.CarrierType, address.Projections, program,
                    out var pathRootLayout, out leafLayout)
                || pathRootLayout != rootLayout
                || address.Index != address.Projections[^1].Index)
            {
                return false;
            }

            foreach (var projection in address.Projections)
            {
                if (projection.Layout.ElementCount == 0
                    || projection.Layout.ElementStride == 0
                    || projection.Layout.ByteExtent == 0
                    || !IsSupportedNativeAddressStorageIndex(
                        projection.Index, caller, localVariables))
                {
                    return false;
                }
            }
        }

        var requiredElementCount = address.ElementStride == 0
            ? 0 : address.ByteExtent / address.ElementStride;
        return leafLayout.ElementCount > 0
            && leafLayout.ElementStride > 0
            && leafLayout.ByteExtent > 0
            && address.ElementType == leafLayout.ElementType
            && address.ElementCount == leafLayout.ElementCount
            && address.ElementStride == leafLayout.ElementStride
            && address.ByteExtent > 0
            && address.ByteExtent % address.ElementStride == 0
            && requiredElementCount > 0
            && requiredElementCount <= leafLayout.ElementCount;
    }

    private static bool IsSupportedNativeAddressStorageIndex(
        BoundExpression index, BoundFunction caller,
        HashSet<int> localVariables)
    {
        var variable = index switch
        {
            BoundRead read => read.Variable,
            BoundUpdate { Place: BoundRead read } => read.Variable,
            _ => -1,
        };
        if ((uint)variable >= (uint)caller.Variables.Length
            || (variable >= caller.ParameterCount
                && !localVariables.Contains(variable)))
        {
            return false;
        }

        var variableType = caller.Variables[variable].Type;
        if (index.Type != variableType
            || !NativeFixedArrayContract.IsIndexType(index.Type))
        {
            return false;
        }

        return index switch
        {
            BoundRead => true,
            BoundUpdate update => caller.Variables[variable].IsMutable
                && update.Operator is TokenKind.PlusPlus
                    or TokenKind.MinusMinus,
            _ => false,
        };
    }

    private static bool IsSupportedNativeFixedArrayRead(
        BoundExpression expression, BoundFunction caller,
        BoundProgram program, HashSet<int> localVariables)
    {
        int variable;
        PrimitiveType elementType;
        ImmutableArray<BoundNativeFixedArrayProjection> projections;
        switch (expression)
        {
            case BoundNativeFixedArrayElement element:
                variable = element.Variable;
                elementType = element.Layout.ElementType;
                projections = [];
                if (!NativeFixedArrayContract.MatchesType(
                        element.Layout.CarrierType, element.Layout, program)
                    || !IsSupportedNativeAddressStorageIndex(element.Index,
                        caller, localVariables))
                {
                    return false;
                }
                break;

            case BoundNativeFixedArrayPathElement pathElement:
                variable = pathElement.Variable;
                elementType = pathElement.Type;
                projections = pathElement.Projections;
                if (projections.IsDefaultOrEmpty
                    || !NativeFixedArrayContract.MatchesPath(
                        (uint)variable < (uint)caller.Variables.Length
                            ? caller.Variables[variable].Type
                            : PrimitiveType.Int32,
                        projections, program, out _, out var leafLayout)
                    || leafLayout.ElementType != pathElement.Type)
                {
                    return false;
                }

                foreach (var projection in projections)
                {
                    if (!IsSupportedNativeAddressStorageIndex(
                            projection.Index, caller, localVariables))
                    {
                        return false;
                    }
                }
                break;

            default:
                return false;
        }

        return (uint)variable < (uint)caller.Variables.Length
            && variable >= caller.ParameterCount
            && localVariables.Contains(variable)
            && !caller.Variables[variable].IsInitializationLocal
            && NativeFixedArrayTypes.IsType(
                caller.Variables[variable].Type)
            && IsSupportedNativeAddressValueType(elementType);
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
                    ? IsSupportedNativeAddressStorageArray(storageAddress,
                        caller, program, localVariables)
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

    private static bool IsSupportedA2EEmptyArrayInitialization(
        BoundAssignment assignment, BoundFunction function,
        BoundProgram program, TrRmArtifactSchema schema,
        out BoundNativeFixedArrayInitialization initialization)
    {
        initialization = null!;
        if (!schema
                .UsesForeignInvocationStorageFixedArrayEmptyInitialization()
            || (uint)assignment.Variable >= (uint)function.Variables.Length
            || !function.Variables[assignment.Variable]
                .IsInitializationLocal
            || !NativeFixedArrayTypes.IsType(
                function.Variables[assignment.Variable].Type)
            || assignment.Value
                is not BoundNativeFixedArrayInitialization candidate
            || candidate.Layout.ElementCount != 0
            || !candidate.Elements.IsEmpty
            || !IsSupportedNativeFixedArrayInitialization(
                function.Variables[assignment.Variable].Type, candidate,
                program, schema))
        {
            return false;
        }

        initialization = candidate;
        return true;
    }

    private static bool IsSupportedNativeFixedArrayCopyAssignment(
        BoundAssignment assignment, BoundFunction function,
        BoundProgram program)
        => (uint)assignment.Variable
            < (uint)function.Variables.Length
            && assignment.Variable >= function.ParameterCount
            && function.Variables[assignment.Variable].IsMutable
            && NativeFixedArrayTypes.IsType(
                function.Variables[assignment.Variable].Type)
            && assignment.Value is BoundRead arrayRead
            && (uint)arrayRead.Variable
                < (uint)function.Variables.Length
            && arrayRead.Variable >= function.ParameterCount
            && arrayRead.Type
                == function.Variables[assignment.Variable].Type
            && arrayRead.Type
                == function.Variables[arrayRead.Variable].Type
            && NativeFixedArrayContract.TryGetLayout(arrayRead.Type,
                program.Structures, program.NativeFixedArrayTypes, out _);

    private static int GetBoundCallParameterIndex(BoundCall call,
        int argumentIndex)
        => call.ArgumentParameterIndices.IsDefault
            ? argumentIndex : call.ArgumentParameterIndices[argumentIndex];

    private static bool IsSupportedNativeFixedArrayInitialization(
        PrimitiveType arrayType,
        BoundNativeFixedArrayInitialization initialization,
        BoundProgram program, TrRmArtifactSchema schema)
    {
        var pending = new Stack<(PrimitiveType ArrayType,
            BoundNativeFixedArrayInitialization Initialization, int Depth)>();
        pending.Push((arrayType, initialization, 1));
        while (pending.TryPop(out var current))
        {
            if (current.Depth > SemanticVerifier.MaximumDepth
                || !NativeFixedArrayContract.TryGetLayout(
                    current.ArrayType, program.Structures,
                    program.NativeFixedArrayTypes, out var layout)
                || !NativeFixedArrayContract.MatchesType(
                    current.ArrayType, layout, program)
                || !NativeFixedArrayContract.MatchesLayout(layout, program)
                || current.Initialization.Type != current.ArrayType
                || current.Initialization.Layout != layout
                || current.Initialization.Elements.IsDefault)
            {
                return false;
            }

            if (layout.ElementCount == 0)
            {
                if (!schema
                        .UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                    || !current.Initialization.Elements.IsEmpty
                    || layout.ByteExtent != 0)
                {
                    return false;
                }

                continue;
            }

            if ((ulong)current.Initialization.Elements.Length
                != layout.ElementCount)
            {
                return false;
            }

            foreach (var element in current.Initialization.Elements)
            {
                if (element is BoundCall initializerCall
                    && initializerCall.Type == layout.ElementType
                    && DirectNativeImportContract.IsScalar(
                        layout.ElementType))
                {
                    continue;
                }

                if (element is BoundRead initializerRead
                    && initializerRead.Type == layout.ElementType
                    && IsSupportedNativeAddressValueType(
                        layout.ElementType))
                {
                    continue;
                }

                if (NativePointerTypes.IsType(layout.ElementType)
                    && element.Type == layout.ElementType
                    && element is BoundNativePointerAddress
                        or BoundNativeFixedArrayPointerAddress
                        or BoundNativeFixedArrayElement
                        or BoundNativeFixedArrayPathElement)
                {
                    continue;
                }

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
                    && IsSupportedNativeUnitConditionalExpression(element,
                        schema))
                {
                    continue;
                }

                if (!schema.UsesForeignInvocationStorageFixedArrays()
                    || !NativeFixedArrayTypes.IsType(layout.ElementType)
                    || element
                        is not BoundNativeFixedArrayInitialization nested
                    || element.Type != layout.ElementType)
                {
                    return false;
                }

                pending.Push((layout.ElementType, nested,
                    checked(current.Depth + 1)));
            }
        }

        return true;
    }

    private static bool IsSupportedNativeUnitConditionalExpression(
        BoundExpression expression, TrRmArtifactSchema schema)
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
                if (!IsSupportedNativeUnitConditionalCondition(condition,
                        schema))
                {
                    return false;
                }

                if (TryGetNativeAddressKnownBoolean(condition,
                        out var selected))
                {
                    return IsSupportedNativeUnitConditionalExpression(
                        selected ? whenTrue : whenFalse, schema);
                }

                return IsSupportedNativeUnitConditionalExpression(whenTrue,
                        schema)
                    && IsSupportedNativeUnitConditionalExpression(whenFalse,
                        schema);

            default:
                return false;
        }
    }

    private static bool IsSupportedNativeUnitConditionalCondition(
        BoundExpression condition, TrRmArtifactSchema schema)
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
            } => IsSupportedNativeUnitConditionalCondition(operand, schema),
            BoundBinary {
                Operator: TokenKind.AmpersandAmpersand
                    or TokenKind.PipePipe,
                Type: PrimitiveType.Bool,
                Left: var left,
                Right: var right,
            } => IsSupportedNativeUnitConditionalCondition(left, schema)
                && IsSupportedNativeUnitConditionalCondition(right, schema),
            BoundBinary comparison =>
                IsSupportedNativeUnitConditionalComparison(comparison,
                    schema)
                || IsSupportedNativeUnitConditionalBooleanXor(comparison),
            BoundConditional {
                Type: PrimitiveType.Bool,
                Condition: var nestedCondition,
                WhenTrue: var whenTrue,
                WhenFalse: var whenFalse,
            } => IsSupportedNativeUnitConditionalCondition(nestedCondition,
                    schema)
                && IsSupportedNativeUnitConditionalCondition(whenTrue, schema)
                && IsSupportedNativeUnitConditionalCondition(whenFalse,
                    schema),
            _ => false,
        };
    }

    private static bool IsSupportedNativeUnitConditionalComparison(
        BoundBinary comparison, TrRmArtifactSchema schema)
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

        if (!DirectNativeImportContract.IsScalar(comparison.Left.Type)
            || comparison.Operator is not (TokenKind.EqualsEquals
                or TokenKind.BangEquals or TokenKind.Less
                or TokenKind.LessEquals or TokenKind.Greater
                or TokenKind.GreaterEquals))
        {
            return false;
        }

        var leftIsReadOrCall
            = comparison.Left is BoundRead or BoundCall;
        var rightIsReadOrCall
            = comparison.Right is BoundRead or BoundCall;
        if (leftIsReadOrCall && rightIsReadOrCall)
        {
            return true;
        }

        return schema.UsesScalarFrozenValues()
            && IsSupportedNativeUnitConditionalLiteralType(
                comparison.Left.Type)
            && (leftIsReadOrCall
                || comparison.Left is BoundConstant)
            && (rightIsReadOrCall
                || comparison.Right is BoundConstant)
            && (comparison.Left is BoundConstant
                || comparison.Right is BoundConstant);
    }

    private static bool IsSupportedNativeUnitConditionalLiteralType(
        PrimitiveType type)
        => type is PrimitiveType.Int8 or PrimitiveType.Int16
            or PrimitiveType.Int32 or PrimitiveType.Int64
            or PrimitiveType.UInt8 or PrimitiveType.UInt16
            or PrimitiveType.UInt32 or PrimitiveType.UInt64
            or PrimitiveType.Float32 or PrimitiveType.Float64;

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

        if (!DirectNativeImportContract.IsScalar(type)
            && !(schema.UsesForeignInvocationStorageFixedArrays()
                && NativeFixedArrayContract.IsIndexType(type)))
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
        TrRmArtifactSchema schema,
        TrRmFrozenScalarValueBuilder? scalarValues,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        var localCount = caller.Variables.Length - caller.ParameterCount;
        var bodyConstantMapRows
            = new List<TrRmFrozenScalarValueReference>();
        var bodyConstantMapIndexByValue
            = new Dictionary<(PrimitiveType Type, ScalarBits Bits), ushort>();
        ushort GetConstantMapIndex(BoundConstant constant)
        {
            if (scalarValues is null
                || !IsSupportedNativeUnitConditionalLiteralType(
                    constant.Type))
            {
                throw Unsupported(
                    "The selected Body has no typed numeric ConstantMap profile.");
            }

            var key = (constant.Type, constant.Bits);
            if (bodyConstantMapIndexByValue.TryGetValue(key,
                    out var existingIndex))
            {
                return existingIndex;
            }

            if (bodyConstantMapRows.Count == 65_536)
            {
                throw Unsupported(
                    "The A2EU+SF1 Body ConstantMap exceeds its index domain.");
            }

            var valueReference = scalarValues.Add(constant);
            var index = checked((ushort)bodyConstantMapRows.Count);
            bodyConstantMapRows.Add(valueReference);
            bodyConstantMapIndexByValue.Add(key, index);
            return index;
        }

        byte[] EncodeBodyConstantMap()
        {
            var result = new byte[bodyConstantMapRows.Count * 8];
            for (var index = 0; index < bodyConstantMapRows.Count; index++)
            {
                var valueReference = bodyConstantMapRows[index];
                var offset = index * 8;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    result.AsSpan(offset), valueReference.TypeSpecRef);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    result.AsSpan(offset + 4),
                    valueReference.ConstantSourceRow);
            }

            return result;
        }

        var unitCallTemporaryHomesBuilder
            = GetNativeAddressUnitCallHomes(
                storageCall.UnitInitializerCalls,
                storageCall.ConditionCaptures, localCount,
                resourceLedger).ToBuilder();
        var pointerValueHomeByExpression
            = new Dictionary<BoundExpression, int>(
                ReferenceEqualityComparer.Instance);
        var pointerValueAddresses = new List<BoundExpression>();
        void AddPointerValueHome(BoundExpression expression)
        {
            if (!NativePointerTypes.IsType(expression.Type)
                || expression is not (BoundNativePointerAddress
                    or BoundNativeFixedArrayPointerAddress
                    or BoundNativeFixedArrayElement
                    or BoundNativeFixedArrayPathElement))
            {
                throw Unsupported(
                    "The A2 pointer initializer has no admitted pointer-value producer.");
            }

            var home = checked(localCount
                + unitCallTemporaryHomesBuilder.Count);
            if (!TryAddA3Staged(pointerValueHomeByExpression, expression,
                    home, resourceLedger))
            {
                throw Unsupported(
                    "The A2 pointer initializer value Home is reused.");
            }
            pointerValueAddresses.Add(expression);
            unitCallTemporaryHomesBuilder.Add(new NativeAddressUnitCallHome(
                home, expression.Type));
        }

        void CollectPointerArrayInitializerValues(
            BoundNativeFixedArrayInitialization initialization)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            foreach (var element in initialization.Elements)
            {
                resourceLedger?.ConsumeWork();
                if (element is BoundNativeFixedArrayInitialization nested)
                {
                    CollectPointerArrayInitializerValues(nested);
                }
                else if (element is BoundNativePointerAddress
                    or BoundNativeFixedArrayPointerAddress
                    or BoundNativeFixedArrayElement
                    or BoundNativeFixedArrayPathElement)
                {
                    AddPointerValueHome(element);
                }
            }
        }

        void CollectPointerValueHomes(BoundStatement statement)
        {
            resourceLedger?.ConsumeWork();
            switch (statement)
            {
                case BoundLocal local when local.Initializer
                    is BoundNativeFixedArrayInitialization initialization:
                    CollectPointerArrayInitializerValues(initialization);
                    break;

                case BoundLocal local when local.Initializer
                    is BoundNativePointerAddress
                        or BoundNativeFixedArrayPointerAddress:
                    AddPointerValueHome(local.Initializer);
                    break;

                case BoundBlock block:
                    foreach (var nested in block.Statements)
                    {
                        CollectPointerValueHomes(nested);
                    }
                    break;

                case BoundUnsafe unsafeBlock:
                    CollectPointerValueHomes(unsafeBlock.Body);
                    break;

                case BoundIf branch:
                    CollectPointerValueHomes(branch.Then);
                    if (branch.Else is not null)
                    {
                        CollectPointerValueHomes(branch.Else);
                    }
                    break;
            }
        }

        foreach (var statement in caller.Body.Statements)
        {
            CollectPointerValueHomes(statement);
        }

        var unitCallTemporaryHomes
            = unitCallTemporaryHomesBuilder.ToImmutable();
        var unitCallResultHomeEnd
            = localCount + unitCallTemporaryHomes.Length;
        var indexCaptureHomesByAddress
            = new Dictionary<BoundExpression, int[]>(
                ReferenceEqualityComparer.Instance);
        var indexCaptureHomes
            = new List<NativeAddressUnitCallHome>();
        var nextIndexCaptureHome = unitCallResultHomeEnd;
        void AddIndexCaptureHomes(BoundExpression address,
            PrimitiveType carrierType, BoundExpression index,
            ImmutableArray<BoundNativeFixedArrayProjection> projections)
        {
            if (!NativeFixedArrayTypes.IsType(carrierType))
            {
                return;
            }

            var projectionCount = projections.IsDefaultOrEmpty
                ? 1 : projections.Length;
            ReserveA3Array(resourceLedger, projectionCount, sizeof(int));
            var homes = new int[projectionCount];
            Array.Fill(homes, -1);
            for (var projectionIndex = 0;
                projectionIndex < projectionCount; projectionIndex++)
            {
                resourceLedger?.ConsumeWork();
                var projectionIndexExpression = projections.IsDefaultOrEmpty
                    ? index : projections[projectionIndex].Index;
                if (projectionIndexExpression
                    is not BoundUpdate { IsPrefix: false } update)
                {
                    continue;
                }

                var home = nextIndexCaptureHome++;
                homes[projectionIndex] = home;
                AddA3Staged(indexCaptureHomes,
                    new NativeAddressUnitCallHome(home, update.Type),
                    resourceLedger);
            }

            if (!TryAddA3Staged(indexCaptureHomesByAddress, address, homes,
                    resourceLedger))
            {
                throw Unsupported(
                    "A storage-address expression is reused across call arguments.");
            }
        }

        void AddStorageAddressIndexCaptureHomes(
            BoundNativeStorageAddress address)
        {
            AddIndexCaptureHomes(address, address.CarrierType, address.Index,
                address.Projections);
        }

        void AddPointerAddressIndexCaptureHomes(
            BoundNativeFixedArrayPointerAddress address)
        {
            var carrierType = address.Projections.IsDefaultOrEmpty
                ? address.Layout.CarrierType
                : address.Projections[0].Layout.CarrierType;
            AddIndexCaptureHomes(address, carrierType, address.Index,
                address.Projections);
        }

        foreach (var unitCall in storageCall.UnitInitializerCalls)
        {
            resourceLedger?.ConsumeWork();
            foreach (var argument in unitCall.Call.Arguments)
            {
                resourceLedger?.ConsumeWork();
                if (argument is BoundNativeStorageAddress address)
                {
                    AddStorageAddressIndexCaptureHomes(address);
                }
            }
        }

        foreach (var storageArgument in storageCall.StorageArguments)
        {
            resourceLedger?.ConsumeWork();
            AddStorageAddressIndexCaptureHomes(storageArgument.Address);
        }

        foreach (var pointerAddress in pointerValueAddresses)
        {
            resourceLedger?.ConsumeWork();
            if (pointerAddress is BoundNativeFixedArrayPointerAddress arrayAddress)
            {
                AddPointerAddressIndexCaptureHomes(arrayAddress);
            }
        }

        var storageArgumentCount = 0;
        foreach (var unitCall in storageCall.UnitInitializerCalls)
        {
            resourceLedger?.ConsumeWork();
            foreach (var argument in unitCall.Call.Arguments)
            {
                resourceLedger?.ConsumeWork();
                if (argument is BoundNativeStorageAddress)
                {
                    storageArgumentCount++;
                }
            }
        }

        ReserveA3Collection(resourceLedger, storageArgumentCount);
        var unitCallStorageHomeByArgument
            = new Dictionary<(int CallIndex, int ArgumentIndex), int>(
                storageArgumentCount);
        var nextStorageHome = nextIndexCaptureHome;
        for (var callIndex = 0;
            callIndex < storageCall.UnitInitializerCalls.Length;
            callIndex++)
        {
            resourceLedger?.ConsumeWork();
            var unitCall = storageCall.UnitInitializerCalls[callIndex].Call;
            for (var argumentIndex = 0;
                argumentIndex < unitCall.Arguments.Length;
                argumentIndex++)
            {
                resourceLedger?.ConsumeWork();
                if (unitCall.Arguments[argumentIndex]
                    is BoundNativeStorageAddress)
                {
                    AddA3Staged(unitCallStorageHomeByArgument,
                        (callIndex, argumentIndex), nextStorageHome++,
                        resourceLedger);
                }
            }
        }

        var usesArrayInitializationCounterHomes
            = !storageCall.UnitInitializerCalls.IsEmpty
                && !schema
                    .UsesForeignInvocationStorageFixedArrayUnitInitialization();
        var arrayInitializationCounterDepths = new SortedSet<int>();
        void CollectArrayInitializationCounterDepths(
            BoundNativeFixedArrayInitialization initialization, int depth)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            if (depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "The A2 nested array initializer exceeds the semantic depth limit.");
            }

            if ((depth >= 11 && initialization.Layout.ElementCount > 1)
                || (usesArrayInitializationCounterHomes
                    && initialization.Layout.ElementCount != 0))
            {
                _ = arrayInitializationCounterDepths.Add(depth);
            }

            foreach (var element in initialization.Elements)
            {
                resourceLedger?.ConsumeWork();
                if (element
                    is BoundNativeFixedArrayInitialization nested
                    && nested.Layout.ElementCount > 0)
                {
                    CollectArrayInitializationCounterDepths(nested,
                        checked(depth + 1));
                }
            }
        }

        if (!BoundTreeTraversal.VisitStatements(caller.Body, statement =>
                {
                    if (statement is BoundLocal {
                        Initializer: BoundNativeFixedArrayInitialization
                            initialization,
                    })
                    {
                        CollectArrayInitializationCounterDepths(
                            initialization, 0);
                    }
                }))
        {
            throw Unsupported(
                "The A2 Body array initializers exceed the bound-tree analysis limits.");
        }

        var storageResultHomeStart = nextStorageHome;
        var arrayInitializationCounterHomeByDepth
            = new Dictionary<int, int>(arrayInitializationCounterDepths.Count);
        var nextArrayInitializationCounterHome = storageResultHomeStart
            + storageCall.StorageArguments.Length;
        foreach (var depth in arrayInitializationCounterDepths)
        {
            arrayInitializationCounterHomeByDepth.Add(depth,
                nextArrayInitializationCounterHome++);
        }

        var formalHomeStart = nextArrayInitializationCounterHome;
        var homeCount = formalHomeStart + caller.ParameterCount;
        if (localCount <= 0 || homeCount > 65_536)
        {
            throw Unsupported(
                "The F+S Body has too many Homes for its local and storage-address results.");
        }

        ReserveA3Array(resourceLedger, caller.Variables.Length,
            sizeof(int));
        var homeIndexByVariable = new int[caller.Variables.Length];
        Array.Fill(homeIndexByVariable, -1);
        var homeMapLength = checked(homeCount * 8);
        ReserveA3Buffer(resourceLedger, homeMapLength);
        var homeMap = new byte[homeMapLength];
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

        foreach (var temporaryHome in indexCaptureHomes)
        {
            var home = temporaryHome.Index;
            var offset = home * 8;
            homeMap[offset] = 3;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                maps.GetTypeMapIndex(CreateStandardBodyTypeMapToken(
                    getSignatureTypeSpecRef(temporaryHome.Type))));
        }

        ReserveA3Collection(resourceLedger,
            storageCall.StorageArguments.Length);
        var storageHomeByParameter = new Dictionary<int, int>(
            storageCall.StorageArguments.Length);
        for (var index = 0;
            index < storageCall.StorageArguments.Length; index++)
        {
            var storageArgument = storageCall.StorageArguments[index];
            var home = storageResultHomeStart + index;
            AddA3Staged(storageHomeByParameter,
                storageArgument.ParameterOrdinal, home, resourceLedger);
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

        foreach (var counterHome in arrayInitializationCounterHomeByDepth.Values)
        {
            var offset = counterHome * 8;
            homeMap[offset] = 3;
            homeMap[offset + 1] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                maps.GetTypeMapIndex(CreateStandardBodyTypeMapToken(
                    getSignatureTypeSpecRef(PrimitiveType.Int32))));
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

        ReserveA3Buffer(resourceLedger, 8);
        var entryState = new byte[8];
        entryState[0] = 0x01;
        ReserveA3Collection(resourceLedger, 0);
        var statements = new List<BoundStatement>();
        if (!AppendStatements(caller.Body, statements, resourceLedger))
        {
            throw Unsupported("The F+S caller Body cannot be flattened.");
        }

        long CountFixedArrayInitializationRecords(
            BoundNativeFixedArrayInitialization initialization, int depth)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            if (initialization.Elements.IsEmpty)
            {
                return 2;
            }

            var count = checked((long)depth
                * Math.Max(0, initialization.Elements.Length - 1));
            foreach (var element in initialization.Elements)
            {
                resourceLedger?.ConsumeWork();
                if (element
                        is BoundNativeFixedArrayInitialization nested
                    && nested.Layout.ElementCount > 0)
                {
                    count = checked(count + 1
                        + CountFixedArrayInitializationRecords(nested,
                            checked(depth + 1)));
                }
                else
                {
                    count = checked(count + 2);
                }
            }

            return count;
        }

        long CountArrayInitializationRecords(BoundStatement statement)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            return statement switch
            {
                BoundLocal
                {
                    Initializer: BoundNativeFixedArrayInitialization initialization,
                } => CountFixedArrayInitializationRecords(initialization, 0),
                BoundLocal
                {
                    Initializer: BoundNativeFixedArrayElement,
                } => 1,
                BoundLocal
                {
                    Initializer: BoundNativeFixedArrayPathElement pathElement,
                } => pathElement.Projections.Length,
                BoundAssignment assignment
                    when IsSupportedA2EEmptyArrayInitialization(
                        assignment, caller, program, schema, out _) => 2,
                BoundBlock block => CountNestedArrayInitializationRecords(
                    block.Statements),
                BoundUnsafe unsafeBlock
                    => CountArrayInitializationRecords(unsafeBlock.Body),
                BoundIf branch => checked(
                    CountArrayInitializationRecords(branch.Then)
                    + (branch.Else is null ? 0
                        : CountArrayInitializationRecords(branch.Else))),
                _ => 0,
            };
        }

        long CountNestedArrayInitializationRecords(
            ImmutableArray<BoundStatement> nestedStatements)
        {
            long count = 0;
            foreach (var nested in nestedStatements)
            {
                count = checked(count
                    + CountArrayInitializationRecords(nested));
            }

            return count;
        }

        long recordCount = 3 + storageCall.StorageArguments.Length
            + storageCall.ConditionalEdgeCount
            + storageCall.UnitInitializerCalls.Length;
        foreach (var unitCall in storageCall.UnitInitializerCalls)
        {
            resourceLedger?.ConsumeWork();
            foreach (var argument in unitCall.Call.Arguments)
            {
                resourceLedger?.ConsumeWork();
                if (argument is not BoundNativeStorageAddress address)
                {
                    continue;
                }

                recordCount++;
                if (NativeFixedArrayTypes.IsType(address.CarrierType))
                {
                    recordCount = checked(recordCount
                        + (address.Projections.IsDefaultOrEmpty
                            ? 1 : address.Projections.Length));
                }
            }
        }
        foreach (var statement in statements)
        {
            recordCount = checked(recordCount
                + CountArrayInitializationRecords(statement));
        }

        foreach (var storageArgument in storageCall.StorageArguments)
        {
            resourceLedger?.ConsumeWork();
            if (NativeFixedArrayTypes.IsType(
                    storageArgument.Address.CarrierType))
            {
                recordCount = checked(recordCount
                    + (storageArgument.Address.Projections.IsDefaultOrEmpty
                        ? 1 : storageArgument.Address.Projections.Length));
            }
        }
        if (recordCount > (long)ushort.MaxValue + 1)
        {
            throw Unsupported(
                "The A2 array initialization exceeds the Body record-index domain.");
        }

        ReserveA3Collection(resourceLedger, checked((int)recordCount));
        var records = new TrRmA3StagedList<ReadOnlyMemory<byte>>(
            resourceLedger, checked((int)recordCount));
        records.Add(entryState);
        records.Add(
            TrRmRegisterBodyRecordWriter.EncodeNativeAddress(2,
                checked((ushort)targetHome),
                maps.GetMethodMapIndex(targetMethodMapToken),
                maps.GetTypeMapIndex(0x1100_0001), resourceLedger));
        ReserveA3Collection(resourceLedger, 0);
        var code = new TrRmA3StagedList<byte>(resourceLedger);
        ReserveA3Collection(resourceLedger, 0);
        var blocks = new TrRmA3StagedList<TrRmRegisterBodyBlock>(
            resourceLedger);
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

        void AppendScalarArithmetic(byte operation,
            byte destinationRegister, byte source0, byte source1)
        {
            if (destinationRegister > 13 || source0 > 15 || source1 > 15)
            {
                throw Unsupported(
                    "The A2 index update has an invalid arithmetic register.");
            }

            var payload = (ushort)(destinationRegister
                | (15 << 4) | (source0 << 8) | (source1 << 12));
            code.Add(0xC0);
            code.Add(operation);
            code.Add(unchecked((byte)payload));
            code.Add((byte)(payload >> 8));
        }

        int AppendNativeAddressIndexUpdate(BoundUpdate update,
            int captureHome, byte valueRegister)
        {
            if (update.Place is not BoundRead place
                || update.Type != place.Type
                || !NativeFixedArrayContract.IsIndexType(update.Type)
                || (update.Operator is not (TokenKind.PlusPlus
                    or TokenKind.MinusMinus))
                || (!update.IsPrefix && captureHome < localCount))
            {
                throw Unsupported(
                    "The A2 storage-address index update is unsupported.");
            }

            var sourceHome = GetCallerHome(place.Variable);
            if (sourceHome < 0)
            {
                throw Unsupported(
                "The A2 storage-address index has no writable Home.");
            }

            AppendHomeLoad(code, valueRegister, sourceHome);
            if (!update.IsPrefix)
            {
                AppendHomeStore(code, valueRegister, captureHome);
            }

            var operation = (update.Operator, update.OverflowBehavior) switch
            {
                (TokenKind.PlusPlus, BoundIntegerOverflowBehavior.Checked)
                    => (byte)0x01,
                (TokenKind.MinusMinus, BoundIntegerOverflowBehavior.Checked)
                    => (byte)0x02,
                (TokenKind.PlusPlus, BoundIntegerOverflowBehavior.Wrapping)
                    => (byte)0x05,
                (TokenKind.MinusMinus, BoundIntegerOverflowBehavior.Wrapping)
                    => (byte)0x06,
                _ => throw Unsupported(
                    "The A2 storage-address index update has an unsupported overflow policy."),
            };
            AppendScalarArithmetic(operation, valueRegister, valueRegister,
                15);
            AppendHomeStore(code, valueRegister, sourceHome);
            if (update.IsPrefix)
            {
                return sourceHome;
            }

            return captureHome;
        }

        void AppendNativeStorageAddress(BoundNativeStorageAddress address,
            PrimitiveType pointerType, int resultHome,
            BoundExpression? indexCaptureIdentity = null)
        {
            if ((uint)address.Variable >= (uint)homeIndexByVariable.Length
                || address.Variable < caller.ParameterCount
                || homeIndexByVariable[address.Variable] < 0
                || address.ElementStride == 0
                || address.ByteExtent == 0
                || address.ByteExtent % address.ElementStride != 0)
            {
                throw Unsupported(
                    "The F+S storage address has invalid local geometry.");
            }

            var requiredElementCount = checked((uint)(
                address.ByteExtent / address.ElementStride));
            byte sourceKind;
            ushort sourceIndex;
            if (NativeFixedArrayTypes.IsType(address.CarrierType))
            {
                var projections = address.Projections;
                if (projections.IsDefaultOrEmpty)
                {
                    if (!NativeFixedArrayContract.TryGetLayout(
                            address.CarrierType, program.Structures,
                            program.NativeFixedArrayTypes,
                            out var rootLayout))
                    {
                        throw Unsupported(
                            "The A2 storage address has no root array layout.");
                    }

                    projections = [
                        new BoundNativeFixedArrayProjection(rootLayout,
                            address.Index),
                    ];
                }
                else if (!NativeFixedArrayContract.MatchesPath(
                            address.CarrierType, projections, program,
                            out _, out _)
                    || address.Index != projections[^1].Index)
                {
                    throw Unsupported(
                        "The A2 storage address has an unsupported array path.");
                }

                if (!indexCaptureHomesByAddress.TryGetValue(
                        indexCaptureIdentity ?? address,
                        out var projectionCaptureHomes)
                    || projectionCaptureHomes.Length != projections.Length)
                {
                    throw Unsupported(
                        "The A2 storage address has no index-capture map.");
                }

                if (projections.Length > SemanticVerifier.MaximumDepth)
                {
                    throw Unsupported(
                        "The A2 storage address exceeds the semantic depth limit.");
                }

                var rootHome = homeIndexByVariable[address.Variable];
                if (rootHome < 0)
                {
                    throw Unsupported(
                        "The A2 storage address has no local root Home.");
                }

                var accessKind = address.IsReadonly ? (byte)1 : (byte)2;
                var previousRegister = -1;
                for (var projectionIndex = 0;
                    projectionIndex < projections.Length; projectionIndex++)
                {
                    resourceLedger?.ConsumeWork();
                    var projection = projections[projectionIndex];
                    int indexHome;
                    if (projection.Index is BoundRead indexRead)
                    {
                        if ((uint)indexRead.Variable
                                >= (uint)caller.Variables.Length
                            || indexRead.Type
                                != caller.Variables[indexRead.Variable].Type
                            || !NativeFixedArrayContract.IsIndexType(
                                indexRead.Type))
                        {
                            throw Unsupported(
                                "The A2 storage address index has an invalid local type.");
                        }

                        indexHome = GetCallerHome(indexRead.Variable);
                    }
                    else if (projection.Index is BoundUpdate update)
                    {
                        var captureHome
                            = projectionCaptureHomes[projectionIndex];
                        indexHome = AppendNativeAddressIndexUpdate(update,
                            captureHome,
                            checked((byte)projectionIndex));
                    }
                    else
                    {
                        throw Unsupported(
                            "The A2 storage address has an unsupported index expression.");
                    }

                    if (indexHome < 0
                        || !fixedArrayLayoutRowByType.TryGetValue(
                            NativeFixedArrayTypes.Index(
                                projection.Layout.CarrierType),
                            out var layoutRow)
                        || layoutRow == 0 || (uint)layoutRow > 0xFF_FFFFu)
                    {
                        throw Unsupported(
                            "The A2 storage address has no mapped layout or index Home.");
                    }

                    var destinationRegister
                        = checked((byte)(projectionIndex % 2));
                    var baseKind = projectionIndex == 0 ? (byte)2 : (byte)1;
                    var baseIndex = projectionIndex == 0
                        ? rootHome : (projectionIndex - 1) % 2;
                    if (baseIndex < 0 || (uint)baseIndex > ushort.MaxValue)
                    {
                        throw Unsupported(
                            "The A2 storage address path has no selected parent.");
                    }

                    var placeSelectionIndex = checked((ushort)records.Count);
                    records.Add(
                        TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                            3, accessKind, 1, baseKind,
                            destinationRegister, checked((ushort)baseIndex),
                            2, 3, checked((ushort)indexHome),
                            checked((uint)layoutRow),
                            maps.GetTypeMapIndex(
                                CreateStandardBodyTypeMapToken(
                                    getSignatureTypeSpecRef(
                                        projection.Layout.ElementType))),
                            schema, resourceLedger));
                    code.Add(0xD8);
                    code.Add(destinationRegister);
                    code.Add(unchecked((byte)placeSelectionIndex));
                    code.Add((byte)(placeSelectionIndex >> 8));
                    previousRegister = destinationRegister;
                }

                sourceKind = 1;
                sourceIndex = checked((ushort)previousRegister);
            }
            else
            {
                sourceKind = 2;
                sourceIndex = checked((ushort)homeIndexByVariable[
                    address.Variable]);
            }

            var storageRecordIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodeStorageAddress(
                    sourceKind, 2, sourceIndex, checked((ushort)resultHome),
                    maps.GetTypeMapIndex(CreateStandardBodyTypeMapToken(
                        getSignatureTypeSpecRef(pointerType))),
                    requiredElementCount, resourceLedger));
            code.Add(0xEC);
            code.Add(0);
            code.Add(unchecked((byte)storageRecordIndex));
            code.Add((byte)(storageRecordIndex >> 8));
        }

        void AppendNativePointerValueAddress(
            BoundExpression expression, int resultHome)
        {
            if (!pointerValueHomeByExpression.TryGetValue(expression,
                    out var expectedHome)
                || expectedHome != resultHome
                || !NativePointerTypeContract.IsValidType(expression.Type,
                    program.NativePointerTypes))
            {
                throw Unsupported(
                    "The A2 pointer value has no valid result Home or typed pointer.");
            }

            var pointer = program.NativePointerTypes[
                NativePointerTypes.Index(expression.Type)];
            switch (expression)
            {
                case BoundNativeFixedArrayPointerAddress arrayPointerAddress:
                    var rootType = arrayPointerAddress.Projections.IsDefaultOrEmpty
                        ? arrayPointerAddress.Layout.CarrierType
                        : arrayPointerAddress.Projections[0].Layout.CarrierType;
                    var arrayAddress = new BoundNativeStorageAddress(
                        arrayPointerAddress.Variable,
                        arrayPointerAddress.Index, rootType,
                        arrayPointerAddress.Layout.ElementType,
                        arrayPointerAddress.Type,
                        arrayPointerAddress.Layout.ElementCount,
                        arrayPointerAddress.Layout.ElementStride,
                        arrayPointerAddress.Layout.ElementStride,
                        !pointer.IsWritable, arrayPointerAddress.Span) {
                            Projections = arrayPointerAddress.Projections,
                        };
                    AppendNativeStorageAddress(arrayAddress,
                        arrayPointerAddress.Type, resultHome,
                        arrayPointerAddress);
                    return;

                case BoundNativePointerAddress scalarPointerAddress:
                    if ((uint)scalarPointerAddress.Variable
                            >= (uint)caller.Variables.Length)
                    {
                        throw Unsupported(
                            "The A2 pointer value has no source local.");
                    }

                    var sourceType = caller.Variables[
                        scalarPointerAddress.Variable].Type;
                    var stride = DirectNativeImportContract.IsScalar(
                        sourceType)
                        ? PrimitiveTypeFacts.ScalarWidth(sourceType)
                        : sourceType == PrimitiveType.NativeRawPointer
                            || NativePointerTypes.IsType(sourceType)
                            || NativeFunctionPointerTypes.IsType(sourceType)
                                ? sizeof(ulong) : 0;
                    if (stride <= 0)
                    {
                        throw Unsupported(
                            "The A2 pointer value source has no admitted scalar or pointer-cell stride.");
                    }

                    var scalarAddress = new BoundNativeStorageAddress(
                        scalarPointerAddress.Variable,
                        new BoundConstant(0, PrimitiveType.Int32,
                            scalarPointerAddress.Span),
                        sourceType, sourceType, scalarPointerAddress.Type, 1,
                        (ulong)stride, (ulong)stride, !pointer.IsWritable,
                        scalarPointerAddress.Span);
                    AppendNativeStorageAddress(scalarAddress,
                        scalarPointerAddress.Type, resultHome,
                        scalarPointerAddress);
                    return;

                default:
                    throw Unsupported(
                        "The A2 pointer value source is not an admitted address formation.");
            }
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

        void AppendEmptyArrayHomeInitialization(int variable,
            BoundNativeFixedArrayInitialization initialization)
        {
            if (!schema
                    .UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                || (uint)variable >= (uint)caller.Variables.Length
                || !NativeFixedArrayTypes.IsType(
                    caller.Variables[variable].Type)
                || !IsSupportedNativeFixedArrayInitialization(
                    caller.Variables[variable].Type, initialization, program,
                    schema)
                || initialization.Layout.ElementCount != 0
                || !fixedArrayLayoutRowByType.ContainsKey(
                    NativeFixedArrayTypes.Index(
                        caller.Variables[variable].Type)))
            {
                throw Unsupported(
                    "A2E empty initialization requires an exact local array layout.");
            }

            var homeIndex = homeIndexByVariable[variable];
            if (homeIndex < 0)
            {
                throw Unsupported(
                    "The A2E empty initialization has no local Home.");
            }

            var selectionIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodePlaceSelection(1, 3,
                    1, 0, 2, 0, 0, 0, 0, checked((uint)homeIndex),
                    maps.GetTypeMapIndex(CreateStandardBodyTypeMapToken(
                        getSignatureTypeSpecRef(
                            caller.Variables[variable].Type))),
                    schema));
            AppendRecordPlaceSelectionInstruction(code, selectionIndex, 2);

            var storeIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodeEmptyArrayInitialization(
                    2, schema));
            AppendNullaryArrayStoreInstruction(code, storeIndex);
        }

        ReserveA3Collection(resourceLedger,
            storageCall.StorageArguments.Length);
        var storageArguments
            = new Dictionary<int, NativeAddressStorageArgument>(
                storageCall.StorageArguments.Length);
        foreach (var storageArgument in storageCall.StorageArguments)
        {
            resourceLedger?.ConsumeWork();
            AddA3Staged(storageArguments,
                storageArgument.ParameterOrdinal, storageArgument,
                resourceLedger);
        }

        ReserveA3Collection(resourceLedger,
            storageCall.UnitInitializerCalls.Length);
        var unitCallPlanIndexesByCall
            = new Dictionary<BoundCall, Queue<int>>(
                storageCall.UnitInitializerCalls.Length,
                ReferenceEqualityComparer.Instance);
        ReserveA3Collection(resourceLedger,
            storageCall.ConditionCaptures.Length);
        var conditionCaptureHomeByExpression
            = new Dictionary<BoundBinary, int>(
                storageCall.ConditionCaptures.Length,
                ReferenceEqualityComparer.Instance);
        foreach (var conditionCapture in storageCall.ConditionCaptures)
        {
            resourceLedger?.ConsumeWork();
            if (!TryAddA3Staged(conditionCaptureHomeByExpression,
                    conditionCapture.Condition, conditionCapture.Home,
                    resourceLedger))
            {
                throw Unsupported(
                    "The A2EU condition capture is duplicated.");
            }
        }

        for (var callIndex = 0;
            callIndex < storageCall.UnitInitializerCalls.Length;
            callIndex++)
        {
            resourceLedger?.ConsumeWork();
            var unitCallPlan = storageCall.UnitInitializerCalls[callIndex];
            if (!unitCallPlanIndexesByCall.TryGetValue(unitCallPlan.Call,
                    out var callPlanIndexes))
            {
                ReserveA3Collection(resourceLedger, 0);
                callPlanIndexes = new Queue<int>();
                AddA3Staged(unitCallPlanIndexesByCall, unitCallPlan.Call,
                    callPlanIndexes, resourceLedger);
            }

            resourceLedger?.ConsumeWork();
            callPlanIndexes.Enqueue(callIndex);
        }

        ReserveA3Collection(resourceLedger,
            storageCall.UnitInitializerCalls.Length);
        var emittedUnitCallPlans = new HashSet<int>(
            storageCall.UnitInitializerCalls.Length);
        var arrayInitializerCallResultHomes
            = new Dictionary<BoundCall, int>(
                ReferenceEqualityComparer.Instance);

        int AppendUnitInitializerCall(BoundCall unitCall)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            if (!unitCallPlanIndexesByCall.TryGetValue(unitCall,
                    out var callPlanIndexes)
                || !callPlanIndexes.TryDequeue(out var currentCallIndex))
            {
                throw Unsupported(
                    "The A2EU unit-call expression has no captured plan.");
            }

            _ = AddA3Staged(emittedUnitCallPlans, currentCallIndex,
                resourceLedger);

            var unitCallPlan
                = storageCall.UnitInitializerCalls[currentCallIndex];
            if (!ReferenceEquals(unitCallPlan.Call, unitCall))
            {
                throw Unsupported(
                    "The A2EU unit-call expression identity is inconsistent.");
            }

            var unitSignature = unitCallPlan.Signature;
            ReserveA3Array(resourceLedger,
                unitSignature.ParameterTypes.Length, sizeof(int));
            var argumentIndexByParameter
                = new int[unitSignature.ParameterTypes.Length];
            Array.Fill(argumentIndexByParameter, -1);
            for (var argumentIndex = 0;
                argumentIndex < unitCall.Arguments.Length;
                argumentIndex++)
            {
                resourceLedger?.ConsumeWork();
                var parameterIndex = GetBoundCallParameterIndex(
                    unitCall, argumentIndex);
                argumentIndexByParameter[parameterIndex] = argumentIndex;
            }

            ReserveA3Array(resourceLedger,
                unitSignature.ParameterTypes.Length,
                (2 * sizeof(byte)) + sizeof(ushort));
            var callInputs = new TrRmRegisterBodyCallInput[
                unitSignature.ParameterTypes.Length];
            ReserveA3Array(resourceLedger, unitCall.Arguments.Length,
                sizeof(int));
            var nestedCallResultHomes = new int[unitCall.Arguments.Length];
            Array.Fill(nestedCallResultHomes, -1);
            for (var argumentIndex = 0;
                argumentIndex < unitCall.Arguments.Length;
                argumentIndex++)
            {
                resourceLedger?.ConsumeWork();
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

                if (argument is BoundNativeFixedArrayElement
                    or BoundNativeFixedArrayPathElement)
                {
                    var captureHome
                        = unitCallPlan.ArgumentCaptureHomes[argumentIndex];
                    if (captureHome < 0
                        || !IsSupportedNativeAddressValueType(
                            argument.Type))
                    {
                        throw Unsupported(
                            "The A2EU array-value argument has no typed capture Home.");
                    }

                    AppendNativeFixedArrayRead(argument, captureHome);
                    continue;
                }

                if (argument is not BoundNativeStorageAddress storageAddress)
                {
                    continue;
                }

                if (!unitCallStorageHomeByArgument.TryGetValue(
                        (currentCallIndex, argumentIndex),
                        out var storageHome))
                {
                    throw Unsupported(
                        "The A2EU storage-address argument has no local or result Home.");
                }

                AppendNativeStorageAddress(storageAddress,
                    unitSignature.ParameterTypes[parameterIndex],
                    storageHome);
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

                    case BoundNativeFixedArrayElement
                        or BoundNativeFixedArrayPathElement:
                        var arrayReadHome
                            = unitCallPlan.ArgumentCaptureHomes[argumentIndex];
                        if (arrayReadHome < 0
                            || unitCall.Arguments[argumentIndex].Type
                                != parameterType)
                        {
                            throw Unsupported(
                                "The A2EU array-value argument has no matching captured Home.");
                        }

                        callInputs[parameterIndex]
                            = new TrRmRegisterBodyCallInput(2, 1,
                                checked((ushort)arrayReadHome));
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
                [], resourceLedger));
            AppendCallSiteInstruction(code, callSiteIndex);
            return unitCallPlan.ResultHome;
        }

        void AppendBodyConstantLoad(byte destinationRegister,
            ushort constantIndex)
        {
            if (destinationRegister > 13)
            {
                throw Unsupported(
                    "The A2EU comparison constant has no writable register.");
            }

            if (destinationRegister == 0 && constantIndex == 0)
            {
                code.Add(0x09);
            }
            else if (constantIndex <= 15)
            {
                code.Add(0x16);
                code.Add(checked((byte)((constantIndex << 4)
                    | destinationRegister)));
            }
            else
            {
                code.Add(0xD4);
                code.Add(destinationRegister);
                code.Add(unchecked((byte)constantIndex));
                code.Add((byte)(constantIndex >> 8));
            }
        }

        void AppendUnitInitializerCondition(BoundExpression condition)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
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
                            comparison, schema)
                        || IsSupportedNativeUnitConditionalBooleanXor(
                            comparison):
                    var leftHome = -1;
                    var leftConstantIndex = (ushort)0;
                    var leftIsConstant = false;
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
                    else if (comparison.Left is BoundConstant leftConstant
                        && comparison.Left.Type != PrimitiveType.Bool)
                    {
                        leftConstantIndex
                            = GetConstantMapIndex(leftConstant);
                        leftIsConstant = true;
                    }
                    else
                    {
                        throw Unsupported(
                            "An A2EU comparison has no supported left operand.");
                    }

                    var rightHome = -1;
                    var rightConstantIndex = (ushort)0;
                    var rightIsConstant = false;
                    if (comparison.Right is BoundRead rightRead)
                    {
                        rightHome = GetCallerHome(rightRead.Variable);
                    }
                    else if (comparison.Right is BoundCall rightCall)
                    {
                        rightHome = AppendUnitInitializerCall(rightCall);
                    }
                    else if (comparison.Right is BoundConstant rightConstant
                        && comparison.Right.Type != PrimitiveType.Bool)
                    {
                        rightConstantIndex
                            = GetConstantMapIndex(rightConstant);
                        rightIsConstant = true;
                    }
                    else
                    {
                        throw Unsupported(
                            "An A2EU comparison has no supported right operand.");
                    }

                    if ((leftHome < 0 && !leftIsConstant)
                        || (rightHome < 0 && !rightIsConstant))
                    {
                        throw Unsupported(
                            "An A2EU comparison operand has no result Home.");
                    }

                    if (leftIsConstant)
                    {
                        AppendBodyConstantLoad(0, leftConstantIndex);
                    }
                    else
                    {
                        AppendHomeLoad(code, 0, leftHome);
                    }

                    if (rightIsConstant)
                    {
                        AppendBodyConstantLoad(1, rightConstantIndex);
                    }
                    else
                    {
                        AppendHomeLoad(code, 1, rightHome);
                    }
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
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
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
                = TrRmRegisterBodyRecordWriter.EncodeConditionalEdge(0, 1,
                    resourceLedger);
            records.Add(conditionalEdge);
            code.Add(0xE4);
            code.Add(0);
            code.Add(unchecked((byte)conditionalEdgeIndex));
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
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
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
                            conditional.Condition, schema):
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

        void AppendNativeFixedArrayRead(BoundExpression expression,
            int destinationHome)
        {
            var variable = expression switch {
                BoundNativeFixedArrayElement element => element.Variable,
                BoundNativeFixedArrayPathElement pathElement =>
                    pathElement.Variable,
                _ => -1,
            };
            var projections = expression switch {
                BoundNativeFixedArrayElement element =>
                    ImmutableArray.Create(
                        new BoundNativeFixedArrayProjection(element.Layout,
                            element.Index)),
                BoundNativeFixedArrayPathElement pathElement =>
                    pathElement.Projections,
                _ => [],
            };
            if ((uint)variable >= (uint)caller.Variables.Length
                || variable < caller.ParameterCount
                || !NativeFixedArrayTypes.IsType(
                    caller.Variables[variable].Type)
                || homeIndexByVariable[variable] < 0
                || projections.IsDefaultOrEmpty
                || projections.Length > SemanticVerifier.MaximumDepth
                || destinationHome < 0)
            {
                throw Unsupported(
                    "The A2 nested array read has no bounded Body Home path.");
            }

            if (projections.Length == 1)
            {
                if (expression is not BoundNativeFixedArrayElement element
                    || element.Layout.CarrierType
                        != caller.Variables[variable].Type
                    || !NativeFixedArrayContract.MatchesLayout(
                        element.Layout, program)
                    || element.Layout.ElementType != expression.Type
                    || !IsSupportedNativeAddressValueType(
                        element.Layout.ElementType))
                {
                    throw Unsupported(
                        "The A2 array read is outside the scalar PlaceLoad subset.");
                }
            }
            else if (expression
                        is not BoundNativeFixedArrayPathElement pathElement
                    || !NativeFixedArrayContract.MatchesPath(
                        caller.Variables[variable].Type, projections,
                        program, out _, out var leafLayout)
                    || pathElement.Type != leafLayout.ElementType
                    || !IsSupportedNativeAddressValueType(
                        leafLayout.ElementType))
            {
                throw Unsupported(
                    "The A2 nested array read is outside the scalar PlaceLoad subset.");
            }

            var rootHome = homeIndexByVariable[variable];
            for (var projectionIndex = 0;
                projectionIndex < projections.Length; projectionIndex++)
            {
                resourceLedger?.ConsumeWork();
                var projection = projections[projectionIndex];
                if (projection.Index is not BoundRead indexRead
                    || !NativeFixedArrayContract.IsIndexType(
                        indexRead.Type)
                    || (uint)indexRead.Variable
                        >= (uint)caller.Variables.Length
                    || indexRead.Type
                        != caller.Variables[indexRead.Variable].Type
                    || !fixedArrayLayoutRowByType.TryGetValue(
                        NativeFixedArrayTypes.Index(
                            projection.Layout.CarrierType),
                        out var layoutRow)
                    || layoutRow == 0 || (uint)layoutRow > 0xFF_FFFFu)
                {
                    throw Unsupported(
                        "The A2 nested array read has no mapped layout or index Home.");
                }

                var indexHome = GetCallerHome(indexRead.Variable);
                if (indexHome < 0)
                {
                    throw Unsupported(
                        "The A2 nested array read index has no caller Home.");
                }

                var destinationRegister
                    = checked((byte)(projectionIndex % 2));
                var baseKind = projectionIndex == 0 ? (byte)2 : (byte)1;
                var baseIndex = projectionIndex == 0
                    ? rootHome : (projectionIndex - 1) % 2;
                var placeSelectionIndex = checked((ushort)records.Count);
                records.Add(
                    TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                        3, 1, 1, baseKind, destinationRegister,
                        checked((ushort)baseIndex), 2, 3,
                        checked((ushort)indexHome),
                        checked((uint)layoutRow),
                        maps.GetTypeMapIndex(
                            CreateStandardBodyTypeMapToken(
                                getSignatureTypeSpecRef(
                                    projection.Layout.ElementType))),
                        schema, resourceLedger));
                code.Add(projectionIndex + 1 == projections.Length
                    ? (byte)0xDA : (byte)0xD8);
                code.Add(destinationRegister);
                code.Add(unchecked((byte)placeSelectionIndex));
                code.Add((byte)(placeSelectionIndex >> 8));
            }

            AppendHomeStore(code,
                checked((byte)((projections.Length - 1) % 2)),
                destinationHome);
        }

        void AppendUnitInitializerLocal(BoundLocal local)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
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

            if (local.Initializer
                is BoundNativeFixedArrayElement or
                    BoundNativeFixedArrayPathElement)
            {
                AppendNativeFixedArrayRead(local.Initializer, homeIndex);
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
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            switch (statement)
            {
                case BoundBlock block when block.RetiredManagedLocals.IsEmpty:
                    foreach (var nested in block.Statements)
                    {
                        resourceLedger?.ConsumeWork();
                        AppendUnitInitializerStatement(nested);
                    }
                    return;

                case BoundLocal local:
                    AppendUnitInitializerLocal(local);
                    return;

                case BoundVacantLocal:
                    return;

                case BoundAssignment assignment:
                    if (IsSupportedA2EEmptyArrayInitialization(assignment,
                            caller, program, schema,
                            out var initialization))
                    {
                        AppendEmptyArrayHomeInitialization(
                            assignment.Variable, initialization);
                        return;
                    }

                    if (!IsSupportedNativeFixedArrayCopyAssignment(
                            assignment, caller, program)
                        || assignment.Value is not BoundRead copySource)
                    {
                        throw Unsupported(
                            "F+S supports only exact whole-array Copy assignments.");
                    }

                    var targetHome
                        = homeIndexByVariable[assignment.Variable];
                    var sourceHome
                        = homeIndexByVariable[copySource.Variable];
                    if (targetHome < 0 || sourceHome < 0)
                    {
                        throw Unsupported(
                            "The F+S whole-array Copy assignment has no source or target Home.");
                    }

                    AppendHomeLoad(code, 0, sourceHome);
                    AppendHomeStore(code, 0, targetHome);
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
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            if (!IsSupportedNativeUnitConditionalCondition(
                    branch.Condition, schema))
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

        int GetArrayInitializationDepth(
            BoundNativeFixedArrayInitialization initialization)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            var depth = 1;
            foreach (var element in initialization.Elements)
            {
                resourceLedger?.ConsumeWork();
                if (element
                        is BoundNativeFixedArrayInitialization nested
                    && nested.Layout.ElementCount > 0)
                {
                    depth = Math.Max(depth,
                        checked(1 + GetArrayInitializationDepth(nested)));
                }
            }

            return depth;
        }

        void AppendZeroArrayIndex(int indexHome, byte destinationRegister)
        {
            if (storageCall!.UnitInitializerCalls.IsEmpty)
            {
                AppendHomeLoad(code, destinationRegister, indexHome);
                return;
            }

            code.Add(0x10);
            code.Add((byte)((14 << 4) | destinationRegister));
        }

        void AppendArrayInitializerEffects(
            BoundNativeFixedArrayInitialization initialization,
            int rootVariable)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            foreach (var element in initialization.Elements)
            {
                resourceLedger?.ConsumeWork();
                switch (element)
                {
                    case BoundCall initializerCall:
                        var resultHome
                            = AppendUnitInitializerCall(initializerCall);
                        if (initializerCall.Type
                            != PrimitiveType.Void
                            && (resultHome < 0
                                || !TryAddA3Staged(
                                    arrayInitializerCallResultHomes,
                                    initializerCall, resultHome,
                                    resourceLedger)))
                        {
                            throw Unsupported(
                                "The A2 nested scalar initializer has no unique result Home.");
                        }
                        break;

                    case BoundNativePointerAddress
                        or BoundNativeFixedArrayPointerAddress:
                        if (!pointerValueHomeByExpression.TryGetValue(
                                element, out var addressValueHome))
                        {
                            throw Unsupported(
                                "The A2 nested pointer initializer has no result Home.");
                        }
                        AppendNativePointerValueAddress(element,
                            addressValueHome);
                        break;

                    case BoundNativeFixedArrayElement pointerElement
                        when NativePointerTypes.IsType(pointerElement.Type):
                    case BoundNativeFixedArrayPathElement pointerPathElement
                        when NativePointerTypes.IsType(pointerPathElement.Type):
                        if (!pointerValueHomeByExpression.TryGetValue(
                                element, out var pointerReadHome))
                        {
                            throw Unsupported(
                                "The A2 nested pointer read has no result Home.");
                        }
                        AppendNativeFixedArrayRead(element, pointerReadHome);
                        break;

                    case BoundConditional
                        { Type: PrimitiveType.Void } unitConditional:
                        AppendUnitInitializerExpression(unitConditional);
                        break;

                    case BoundNativeFixedArrayInitialization nested:
                        AppendArrayInitializerEffects(nested, rootVariable);
                        break;
                }
            }
        }

        var arrayInitializationPath
            = new List<(PrimitiveType ArrayType, byte IndexKind,
                ushort IndexIndex)>();
        (byte PlaceRegister, ushort SelectionIndex)
            AppendArrayInitializationSelection(int depth,
            PrimitiveType arrayType, byte indexKind, ushort indexIndex,
            int rootHome)
        {
            resourceLedger?.ConsumeWork();
            var arrayTypeIndex = NativeFixedArrayTypes.Index(arrayType);
            if (!fixedArrayLayoutRowByType.TryGetValue(arrayTypeIndex,
                    out var layoutRow)
                || layoutRow == 0 || (uint)layoutRow > 0xFF_FFFFu)
            {
                throw Unsupported(
                    "The A2 nested array initializer has no exact selected layout.");
            }

            var placeRegister = checked((byte)(depth % 2));
            var baseKind = depth == 0 ? (byte)2 : (byte)1;
            var baseIndex = depth == 0
                ? rootHome : (depth - 1) % 2;
            if (baseIndex < 0 || (uint)baseIndex > ushort.MaxValue)
            {
                throw Unsupported(
                    "The A2 nested array initializer has no parent place.");
            }

            var elementType = program.NativeFixedArrayTypes[
                arrayTypeIndex].ElementType;
            var selectionIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodePlaceSelection(3, 3,
                    1, baseKind, placeRegister,
                    checked((ushort)baseIndex), indexKind, 3, indexIndex,
                    checked((uint)layoutRow),
                    maps.GetTypeMapIndex(
                        CreateStandardBodyTypeMapToken(
                            getSignatureTypeSpecRef(elementType))),
                    schema, resourceLedger));
            return (placeRegister, selectionIndex);
        }

        void AppendArrayInitializationElements(
            BoundNativeFixedArrayInitialization initialization,
            int rootVariable, int depth, int maximumDepth, int indexHome)
        {
            using var frame = resourceLedger?.EnterFrame();
            resourceLedger?.ConsumeWork();
            var arrayType = initialization.Type;
            if (depth >= maximumDepth
                || !NativeFixedArrayContract.TryGetLayout(
                    arrayType, program.Structures,
                    program.NativeFixedArrayTypes, out var layout)
                || !NativeFixedArrayContract.MatchesType(
                    arrayType, layout, program)
                || !NativeFixedArrayContract.MatchesLayout(layout, program)
                || initialization.Layout != layout
                || layout.ElementCount == 0
                || (ulong)initialization.Elements.Length
                    != layout.ElementCount
                || !fixedArrayLayoutRowByType.TryGetValue(
                    NativeFixedArrayTypes.Index(arrayType),
                    out var layoutRow)
                || (uint)layoutRow > 0xFF_FFFFu)
            {
                throw Unsupported(
                    "The A2 nested array initializer has no exact selected layout.");
            }

            byte indexKind;
            ushort indexIndex;
            if (depth < 11 && !usesArrayInitializationCounterHomes)
            {
                var indexRegister = checked((byte)(depth + 2));
                indexKind = 1;
                indexIndex = indexRegister;
                if (depth > 0)
                {
                    AppendZeroArrayIndex(indexHome, indexRegister);
                }
            }
            else if (layout.ElementCount > 1
                || usesArrayInitializationCounterHomes)
            {
                if (!arrayInitializationCounterHomeByDepth.TryGetValue(
                        depth, out var counterHome))
                {
                    throw Unsupported(
                        "The A2 nested array initializer has no counter Home.");
                }

                indexKind = 2;
                indexIndex = checked((ushort)counterHome);
                AppendHomeStore(code, 14, counterHome, forceLong: true);
            }
            else
            {
                indexKind = 2;
                indexIndex = checked((ushort)indexHome);
            }

            var rootHome = homeIndexByVariable[rootVariable];
            if (rootHome < 0 || arrayInitializationPath.Count != depth)
            {
                throw Unsupported(
                    "The A2 nested array initializer has no complete parent path.");
            }
            var elementType = layout.ElementType;

            for (var elementIndex = 0;
                elementIndex < initialization.Elements.Length;
                elementIndex++)
            {
                resourceLedger?.ConsumeWork();
                var element = initialization.Elements[elementIndex];
                var isNullaryEmptyChild
                    = element
                        is BoundNativeFixedArrayInitialization {
                            Layout.ElementCount: 0,
                            Elements.IsEmpty: true,
                        };
                if (elementIndex > 0 && depth > 0)
                {
                        for (var pathIndex = 0;
                            pathIndex < arrayInitializationPath.Count;
                            pathIndex++)
                        {
                            var (parentArrayType, parentIndexKind,
                                parentIndexIndex)
                                = arrayInitializationPath[pathIndex];
                            var (parentPlaceRegister,
                                parentSelectionIndex)
                                = AppendArrayInitializationSelection(
                                    pathIndex, parentArrayType,
                                    parentIndexKind, parentIndexIndex,
                                    rootHome);
                            code.Add(0xD8);
                            code.Add(parentPlaceRegister);
                            code.Add(unchecked((byte)
                                parentSelectionIndex));
                            code.Add((byte)(
                                parentSelectionIndex >> 8));
                        }
                }

                var (placeRegister, selectionIndex)
                        = AppendArrayInitializationSelection(depth, arrayType,
                            indexKind, indexIndex, rootHome);
                if (isNullaryEmptyChild)
                {
                    AppendRecordPlaceSelectionInstruction(code, selectionIndex,
                            placeRegister);
                }
                else
                {
                    code.Add(0xD8);
                    code.Add(placeRegister);
                    code.Add(unchecked((byte)selectionIndex));
                    code.Add((byte)(selectionIndex >> 8));
                }

                if (element
                            is BoundNativeFixedArrayInitialization nested)
                {
                    if (nested.Layout.ElementCount == 0)
                    {
                        if (!schema
                                .UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                            || !nested.Elements.IsEmpty)
                        {
                            throw Unsupported(
                                "The A2 nested empty child requires the exact A2E schema.");
                        }

                        var storeIndex = checked((ushort)records.Count);
                        records.Add(
                            TrRmRegisterBodyRecordWriter
                                .EncodeEmptyArrayInitialization(
                                    placeRegister, schema));
                        AppendNullaryArrayStoreInstruction(code, storeIndex);
                    }
                    else
                    {
                        arrayInitializationPath.Add(
                            (arrayType, indexKind, indexIndex));
                        AppendArrayInitializationElements(nested,
                            rootVariable, depth + 1, maximumDepth,
                            indexHome);
                        arrayInitializationPath.RemoveAt(
                            arrayInitializationPath.Count - 1);
                    }
                }
                else
                {
                    var storeIndex = checked((ushort)records.Count);
                    byte sourceRegister;
                    if (element is BoundConstant initializerConstant
                        && TryGetContextualInitializationRegister(
                            initializerConstant, elementType, schema,
                            out var initializerRegister))
                    {
                        sourceRegister = initializerRegister;
                    }
                    else if (element is BoundCall initializerCall
                        && initializerCall.Type == elementType
                        && DirectNativeImportContract.IsScalar(elementType)
                        && arrayInitializerCallResultHomes.TryGetValue(
                            initializerCall, out var callResultHome))
                    {
                        AppendHomeLoad(code, 13, callResultHome);
                        sourceRegister = 13;
                    }
                    else if (element is BoundRead initializerRead
                        && initializerRead.Type == elementType
                        && (DirectNativeImportContract.IsScalar(elementType)
                            || NativePointerTypes.IsType(elementType)))
                    {
                        var sourceHome = GetCallerHome(
                            initializerRead.Variable);
                        if (sourceHome < 0)
                        {
                            throw Unsupported(
                                "The A2 nested scalar initializer read has no source Home.");
                        }

                        AppendHomeLoad(code, 13, sourceHome);
                        sourceRegister = 13;
                    }
                    else if (NativePointerTypes.IsType(elementType)
                        && pointerValueHomeByExpression.TryGetValue(
                            element, out var pointerValueHome))
                    {
                        AppendHomeLoad(code, 13, pointerValueHome);
                        sourceRegister = 13;
                    }
                    else if (element is BoundCall
                            { Type: PrimitiveType.Void }
                        or BoundConditional { Type: PrimitiveType.Void }
                        && schema
                            .UsesForeignInvocationStorageFixedArrayUnitInitialization())
                    {
                        sourceRegister = 14;
                    }
                    else
                    {
                        throw Unsupported(
                            "The A2 nested array element has no admitted store encoding.");
                    }

                    records.Add(
                        TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                            1, 1, 1, 1, placeRegister,
                            sourceRegister));
                    code.Add(0xD9);
                    code.Add(0);
                    code.Add(unchecked((byte)storeIndex));
                    code.Add((byte)(storeIndex >> 8));
                }

                if (elementIndex + 1
                    < initialization.Elements.Length)
                {
                    var indexRegister = indexKind == 1
                        ? checked((byte)indexIndex) : (byte)13;
                    if (indexKind == 2)
                    {
                        AppendHomeLoad(code, indexRegister, indexIndex);
                    }

                    var unaryIndexOperand = (byte)((indexRegister << 4)
                        | indexRegister);
                    code.Add(0x33);
                    code.Add(unaryIndexOperand);
                    code.Add(0x20);
                    code.Add(unaryIndexOperand);
                    if (indexKind == 2)
                    {
                        AppendHomeStore(code, indexRegister, indexIndex);
                    }
                }
            }
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
                case BoundVacantLocal:
                    break;

                case BoundAssignment assignment:
                    if (IsSupportedA2EEmptyArrayInitialization(assignment,
                            caller, program, schema,
                            out var emptyInitialization))
                    {
                        AppendEmptyArrayHomeInitialization(
                            assignment.Variable, emptyInitialization);
                        break;
                    }

                    if (!IsSupportedNativeFixedArrayCopyAssignment(
                            assignment, caller, program)
                        || assignment.Value is not BoundRead copySourceRead)
                    {
                        throw Unsupported(
                            "F+S supports only exact whole-array Copy assignments.");
                    }

                    var targetArrayHome
                        = homeIndexByVariable[assignment.Variable];
                    var sourceArrayHome
                        = homeIndexByVariable[copySourceRead.Variable];
                    if (targetArrayHome < 0 || sourceArrayHome < 0)
                    {
                        throw Unsupported(
                            "The F+S whole-array Copy assignment has no source or target Home.");
                    }

                    AppendHomeLoad(code, 0, sourceArrayHome);
                    AppendHomeStore(code, 0, targetArrayHome);
                    break;

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
                            is BoundRead arrayCopySource)
                        {
                            if ((uint)arrayCopySource.Variable
                                    >= (uint)caller.Variables.Length
                                || arrayCopySource.Variable
                                    < caller.ParameterCount
                                || arrayCopySource.Type
                                    != caller.Variables[local.Variable].Type
                                || arrayCopySource.Type
                                    != caller.Variables[
                                        arrayCopySource.Variable].Type)
                            {
                                throw Unsupported(
                                    "The A2 whole-array copy has no exact initialized source Home.");
                            }

                            var sourceHome = homeIndexByVariable[
                                arrayCopySource.Variable];
                            if (sourceHome < 0 || sourceHome == homeIndex)
                            {
                                throw Unsupported(
                                    "The A2 whole-array copy has no exact initialized source Home.");
                            }

                            AppendHomeLoad(code, 0, sourceHome);
                            AppendHomeStore(code, 0, homeIndex);
                            break;
                        }

                        if (local.Initializer
                                is not BoundNativeFixedArrayInitialization arrayInitialization
                            || !IsSupportedNativeFixedArrayInitialization(
                                caller.Variables[local.Variable].Type,
                                arrayInitialization, program, schema))
                        {
                            throw Unsupported(
                                "The A2 Body array initializer is not a complete supported local.");
                        }

                        if (arrayInitialization.Layout.ElementCount == 0)
                        {
                            AppendEmptyArrayHomeInitialization(
                                local.Variable, arrayInitialization);
                            break;
                        }

                        var indexHome = homeIndexByVariable[
                            storageCall.ArrayInitializationIndexVariable];
                        if (indexHome < 0)
                        {
                            throw Unsupported(
                                "The A2 array initializer has no zero-valued index Home.");
                        }

                        AppendArrayInitializerEffects(arrayInitialization,
                            local.Variable);
                        var maximumDepth
                            = GetArrayInitializationDepth(arrayInitialization);
                        if (maximumDepth > SemanticVerifier.MaximumDepth)
                        {
                            throw Unsupported(
                                "The A2 nested array initializer exceeds the semantic depth limit.");
                        }

                        if (!usesArrayInitializationCounterHomes)
                        {
                            AppendZeroArrayIndex(indexHome, 2);
                        }

                        AppendArrayInitializationElements(
                            arrayInitialization, local.Variable, 0,
                            maximumDepth, indexHome);

                        break;
                    }

                    if (local.Initializer is BoundCall localCall)
                    {
                        _ = AppendUnitInitializerCall(localCall);
                        break;
                    }

                    if (local.Initializer is BoundNativePointerAddress
                        or BoundNativeFixedArrayPointerAddress)
                    {
                        if (!pointerValueHomeByExpression.TryGetValue(
                                local.Initializer,
                                out var pointerValueHome))
                        {
                            throw Unsupported(
                                "The A2 pointer local initializer has no result Home.");
                        }

                        AppendNativePointerValueAddress(local.Initializer,
                            pointerValueHome);
                        AppendHomeLoad(code, 13, pointerValueHome);
                        AppendHomeStore(code, 13, homeIndex);
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

                    if (local.Initializer
                        is BoundNativeFixedArrayElement or
                            BoundNativeFixedArrayPathElement)
                    {
                        AppendNativeFixedArrayRead(local.Initializer,
                            homeIndex);
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
                    ReserveA3Collection(resourceLedger,
                        call.Arguments.Length);
                    var callInputs
                        = new TrRmA3StagedList<TrRmRegisterBodyCallInput>(
                            resourceLedger, call.Arguments.Length);
                    for (var ordinal = 0; ordinal < call.Arguments.Length;
                        ordinal++)
                    {
                        resourceLedger?.ConsumeWork();
                        switch (call.Arguments[ordinal])
                        {
                            case BoundNativeStorageAddress storageAddress:
                                if (!storageArguments.TryGetValue(ordinal,
                                        out var storageArgument)
                                    || storageArgument.Address != storageAddress)
                                {
                                    throw Unsupported(
                                        "The F+S storage-address argument does not match its checked local place.");
                                }

                                AppendNativeStorageAddress(storageAddress,
                                    signature.ParameterTypes[ordinal],
                                    storageHomeByParameter[ordinal]);
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
                        callInputs, [], resourceLedger));
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
        ReserveA3Buffer(resourceLedger, code.Count);
        var codeBytes = code.ToArray();
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, EncodeBodyConstantMap(),
            blocks,
            records, codeBytes, contractCount,
            schema, resourceLedger);
    }

    private static void AppendHomeStore(TrRmA3StagedList<byte> code,
        byte sourceRegister,
        int homeIndex, bool forceLong = false)
    {
        if (sourceRegister > 15 || (uint)homeIndex > ushort.MaxValue)
        {
            throw Unsupported("The F+S Home initialization operand is invalid.");
        }

        if (!forceLong && sourceRegister is 14 or 15 && homeIndex < 16)
        {
            code.Add(0x14);
            code.Add((byte)((sourceRegister << 4) | homeIndex));
            return;
        }

        code.Add(0xD6);
        code.Add((byte)(0x10 | sourceRegister));
        code.Add(unchecked((byte)homeIndex));
        code.Add((byte)(homeIndex >> 8));
    }

    private static void AppendHomeLoad(TrRmA3StagedList<byte> code,
        byte destinationRegister, int homeIndex)
    {
        if (destinationRegister > 13
            || (uint)homeIndex > ushort.MaxValue)
        {
            throw Unsupported("The A2 array index Home is invalid.");
        }

        code.Add(0xD5);
        code.Add(destinationRegister);
        code.Add(unchecked((byte)homeIndex));
        code.Add((byte)(homeIndex >> 8));
    }

    private static bool IsAddressOnlyVoidBody(BoundFunction function,
        BoundNativeFunctionAddress address,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        ReserveA3Collection(resourceLedger, 0);
        var statements = new List<BoundStatement>();
        if (!AppendStatements(function.Body, statements, resourceLedger)
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
            resourceLedger?.ConsumeWork();
            if (expression is BoundRead read && read.Variable == discard.Variable)
            {
                isRead = true;
            }
        }) && !isRead;
    }

    private static BoundIndirectCall? TryGetNativeAddressCall(
        BoundFunction function, BoundNativeFunctionAddress address,
        NativeFunctionPointerSignatureDefinition signature,
        TrRmA3LayoutResourceLedger? resourceLedger, out int homeIndex,
        out int resultHomeIndex)
    {
        homeIndex = -1;
        resultHomeIndex = -1;
        ReserveA3Collection(resourceLedger, 0);
        var statements = new List<BoundStatement>();
        if (!AppendStatements(function.Body, statements, resourceLedger)
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
            resourceLedger?.ConsumeWork();
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

            resourceLedger?.ConsumeWork();
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
                        address.Function, function, signature,
                        resourceLedger))
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
        NativeFunctionPointerSignatureDefinition signature,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
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
            resourceLedger?.ConsumeWork();
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

    private static bool IsSupportedNativeAddressValueType(
        PrimitiveType type) =>
            DirectNativeImportContract.IsScalar(type)
            || NativePointerTypes.IsType(type);

    private static bool AppendStatements(BoundBlock block,
        List<BoundStatement> statements,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        using var frame = resourceLedger?.EnterFrame();
        if (!block.RetiredManagedLocals.IsEmpty)
        {
            return false;
        }

        foreach (var statement in block.Statements)
        {
            resourceLedger?.ConsumeWork();
            switch (statement)
            {
                case BoundBlock nested:
                    if (!AppendStatements(nested, statements, resourceLedger))
                    {
                        return false;
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    if (!AppendStatements(unsafeBlock.Body, statements,
                            resourceLedger))
                    {
                        return false;
                    }
                    break;
                default:
                    AddA3Staged(statements, statement, resourceLedger);
                    break;
            }
        }

        return true;
    }

    private static void AppendCallSiteInstruction(List<byte> code,
        ushort recordIndex,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        resourceLedger?.ConsumeWork(4);
        code.Add(0xE0);
        code.Add(0);
        code.Add(unchecked((byte)recordIndex));
        code.Add((byte)(recordIndex >> 8));
    }

    private static void AppendCallSiteInstruction(
        TrRmA3StagedList<byte> code, ushort recordIndex)
    {
        code.Add(0xE0);
        code.Add(0);
        code.Add(unchecked((byte)recordIndex));
        code.Add((byte)(recordIndex >> 8));
    }

    private static void AppendRecordPlaceSelectionInstruction(
        TrRmA3StagedList<byte> code, ushort recordIndex,
        byte destinationRegister)
    {
        if (destinationRegister > 13)
        {
            throw Unsupported(
                "The F+S PlaceSelection destination is not a value register.");
        }

        if (recordIndex <= 0x0F)
        {
            code.Add(0x91);
            code.Add((byte)((recordIndex << 4) | destinationRegister));
            return;
        }

        code.Add(0xD8);
        code.Add(destinationRegister);
        code.Add(unchecked((byte)recordIndex));
        code.Add((byte)(recordIndex >> 8));
    }

    private static void AppendNullaryArrayStoreInstruction(
        TrRmA3StagedList<byte> code, ushort recordIndex)
    {
        if (recordIndex <= byte.MaxValue)
        {
            code.Add(0x92);
            code.Add((byte)recordIndex);
            return;
        }

        code.Add(0xD9);
        code.Add(0);
        code.Add(unchecked((byte)recordIndex));
        code.Add((byte)(recordIndex >> 8));
    }

    private static string CreateAnchorName(
        ImmutableArray<BoundFunction> functions,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        ReserveA3Collection(resourceLedger, functions.Length);
        var names = functions.Select(static function => function.Name)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 1; ; index++)
        {
            resourceLedger?.ConsumeWork();
            const string prefix = "__trrm_fnptr_anchor_";
            var digitCount = 1;
            for (var value = index; value >= 10; value /= 10)
            {
                digitCount++;
            }

            resourceLedger?.ConsumeWork(checked(
                (ulong)(prefix.Length + digitCount)));
            var name = $"{prefix}{index}";
            if (!names.Contains(name))
            {
                return name;
            }
        }
    }

    private static void ReserveA3Collection(
        TrRmA3LayoutResourceLedger? resourceLedger, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        resourceLedger?.ConsumeWork(checked(1UL + (ulong)capacity));
    }

    private static void ReserveA3Array(
        TrRmA3LayoutResourceLedger? resourceLedger, int count,
        int bytesPerElement = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegative(bytesPerElement);
        resourceLedger?.ConsumeWork(checked(1UL + (ulong)count
            + ((ulong)count * (ulong)bytesPerElement)));
    }

    private static void ReserveA3Buffer(
        TrRmA3LayoutResourceLedger? resourceLedger, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        resourceLedger?.ConsumeWork(checked(1UL + (ulong)length));
    }

    private static void ReserveA3SortWork(
        TrRmA3LayoutResourceLedger? resourceLedger, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count < 2)
        {
            return;
        }

        var levels = 0;
        for (var value = count - 1; value > 0; value >>= 1)
        {
            levels++;
        }

        resourceLedger?.ConsumeWork(checked((ulong)count * (ulong)levels));
    }

    private static void AddA3Staged<T>(List<T> rows, T row,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork();
        rows.Add(row);
    }

    private static void AddA3Staged<TKey, TValue>(
        Dictionary<TKey, TValue> rows, TKey key, TValue value,
        TrRmA3LayoutResourceLedger? resourceLedger)
        where TKey : notnull
    {
        resourceLedger?.ConsumeWork(2);
        rows.Add(key, value);
    }

    private static bool AddA3Staged<T>(HashSet<T> values, T value,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork();
        return values.Add(value);
    }

    private static bool TryAddA3Staged<TKey, TValue>(
        Dictionary<TKey, TValue> values, TKey key, TValue value,
        TrRmA3LayoutResourceLedger? resourceLedger)
        where TKey : notnull
    {
        resourceLedger?.ConsumeWork(2);
        return values.TryAdd(key, value);
    }

    private static bool TryAddA3Staged<TKey, TValue>(
        SortedDictionary<TKey, TValue> values, TKey key, TValue value,
        TrRmA3LayoutResourceLedger? resourceLedger)
        where TKey : notnull
    {
        resourceLedger?.ConsumeWork(2);
        return values.TryAdd(key, value);
    }

    private sealed class TrRmA3StagedList<T>(
        TrRmA3LayoutResourceLedger? resourceLedger, int capacity = 0)
        : List<T>(capacity)
    {
        public new void Add(T item)
        {
            resourceLedger?.ConsumeWork();
            base.Add(item);
        }
    }
}
