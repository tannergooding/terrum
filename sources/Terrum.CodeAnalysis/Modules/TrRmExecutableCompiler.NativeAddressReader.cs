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
    private readonly record struct NativeStorageAddressFacts(
        int SourceVariable, BoundExpression Index,
        PrimitiveType CarrierType, PrimitiveType ElementType,
        PrimitiveType PointerType, bool PointerIsWritable,
        ulong ElementCount, ulong ElementStride,
        uint RequiredElementCount);

    private readonly record struct NativeArrayPlaceSelectionFacts(
        int RecordIndex, int DestinationRegister, byte TargetKind,
        byte AccessKind,
        int BaseHome, byte IndexKind, int IndexHome, int IndexRegister,
        int ArrayTypeSpecRow, int FixedArrayLayoutRow, PrimitiveType ArrayType,
        PrimitiveType ElementType, ulong ElementCount);

    private readonly record struct NativeAddressRegisterConstant(
        PrimitiveType Type, uint Bits);

    internal static BoundProgram LoadForeignInvocationDraftNativeAddressLibrary(
        ReadOnlySpan<byte> artifact)
        => LoadForeignInvocationDraftNativeAddressLibrary(artifact, 1, null,
            verifySemantics: true,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);

    private static BoundProgram LoadForeignInvocationDraftNativeAddressLibrary(
        ReadOnlySpan<byte> artifact, BoundModuleImport? selectedModuleImport,
        bool verifySemantics)
        => LoadForeignInvocationDraftNativeAddressLibrary(artifact, 1,
            selectedModuleImport, verifySemantics,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);

    private static BoundProgram LoadForeignInvocationDraftNativeAddressLibrary(
        ReadOnlySpan<byte> artifact, int selectedModuleRow,
        BoundModuleImport? selectedModuleImport, bool verifySemantics,
        TrRmArtifactSchema schema = TrRmArtifactSchema.ForeignInvocationDraft,
        TrRmA3LayoutResourceLedger? a3LayoutResourceLedger = null,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings
            = default)
    {
        var index = schema.UsesForeignInvocationStorageFixedArrays()
            ? schema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(artifact)
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
                    .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                        index, a3LayoutResourceLedger,
                        createA3ResourceLedger: a3LayoutResourceLedger is null)
                : schema.UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                    ? TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyEmptyInitializationDraftAndGetExecutionTables(
                            index)
                    : TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                            index)
            : schema.UsesForeignInvocationStorageAddresses()
                ? TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(index)
                : TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            if (kind is not (0x0001 or 0x0002 or 0x0004 or 0x0010 or 0x0011
                or 0x0016 or 0x0017 or 0x0018 or 0x001B or 0x001C
                or 0x001D or 0x0020 or 0x0021)
                && !(kind == 0x0012
                    && (schema is
                        TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft))
                && !((schema
                        is TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft)
                    && tables.TypeDefs.Count != 0
                    && (kind == 0x0013 || kind == 0x0014
                        || kind == 0x0015 || kind == 0x0023))
                && !(kind == 0x0048
                    && (tables.FixedArrayLayouts.Count != 0
                        || tables.TypeDefs.Count != 0)
                    && (schema is
                        TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft)))
            {
                throw Unsupported(
                    $"Section 0x{kind:X4} is outside the bounded foreign-invocation source-free loader.");
            }
        }

        var selectedModuleIsValid = selectedModuleRow > 0
            && selectedModuleRow <= tables.Modules.Count;
        var (methodStart, methodEnd) = selectedModuleIsValid
            ? tables.Modules.GetMethodDefRange(selectedModuleRow)
            : (0, 0);
        var (moduleRefStart, moduleRefEnd) = selectedModuleIsValid
            ? tables.Modules.GetModuleRefRange(selectedModuleRow)
            : (0, 0);
        var selectedImportCount = 0;
        var selectedImportRow = 0;
        if (selectedModuleIsValid)
        {
            for (var importRow = 1; importRow <= tables.Imports.Count;
                importRow++)
            {
                var moduleRefRow
                    = tables.Imports.GetModuleRefForImportRow(importRow);
                if (tables.ModuleRefs.GetOwnerModule(moduleRefRow)
                    == selectedModuleRow)
                {
                    selectedImportCount++;
                    selectedImportRow = importRow;
                }
            }
        }

        if ((schema is TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft)
            && tables.TypeDefs.Count != 0 && methodEnd == methodStart
            && selectedModuleImport is null)
        {
            return LoadForeignInvocationFixedArrayTypeLibrary(tables,
                selectedModuleRow, verifySemantics, typeProviderBindings);
        }

        if (tables.Schema != schema
            || !selectedModuleIsValid
            || tables.TypeDefs.Count != 0
            || tables.Fields.Count != 0
            || tables.EnumDefs.Count != 0
            || tables.EnumMembers.Count != 0
            || tables.Interfaces.Count != 0
            || tables.Modules.GetRequirements(selectedModuleRow) != (1u, 0u)
            || methodEnd - methodStart != 3
            || tables.NativeFunctionPointerSignatures.Count != 0
            || tables.NativeCallables.Count != 0
            || tables.NativeVTables.Count != 0
            || tables.NativeExports.Count != 0
            || (selectedModuleImport is null
                ? moduleRefEnd != moduleRefStart || selectedImportCount != 0
                : moduleRefEnd - moduleRefStart != 1
                    || selectedImportCount != 1))
        {
            throw Unsupported(
                "The source-free foreign-invocation loader supports one Windows x64 direct NativeImport, one address anchor and one caller Body.");
        }

        var targetMethodRow = 0;
        var callerMethodRow = 0;
        var anchorMethodRow = 0;
        for (var row = methodStart; row < methodEnd; row++)
        {
            if (tables.NativeFunctionPointerAnchors.IsAnchorMethodDef(row))
            {
                if (anchorMethodRow != 0)
                {
                    throw Unsupported("Multiple function-pointer anchors are unsupported.");
                }

                anchorMethodRow = row;
            }
            else if (tables.StandardNativeImports.HasImportForMethod(row))
            {
                if (targetMethodRow != 0)
                {
                    throw Unsupported("Multiple direct NativeImports are unsupported.");
                }

                targetMethodRow = row;
            }
            else if (tables.Methods.GetBodyRow(row) != 0)
            {
                if (callerMethodRow != 0)
                {
                    throw Unsupported("Multiple caller Bodies are unsupported.");
                }

                callerMethodRow = row;
            }
        }

        if (targetMethodRow == 0 || callerMethodRow == 0 || anchorMethodRow == 0
            || targetMethodRow == callerMethodRow
            || targetMethodRow == anchorMethodRow
            || callerMethodRow == anchorMethodRow
            || tables.Methods.GetOwnerType(targetMethodRow) != 0
            || tables.Methods.GetOwnerType(callerMethodRow) != 0
            || tables.Methods.GetOwnerType(anchorMethodRow) != 0
            || tables.Methods.GetOwnerModule(targetMethodRow) != selectedModuleRow
            || tables.Methods.GetOwnerModule(callerMethodRow) != selectedModuleRow
            || tables.Methods.GetOwnerModule(anchorMethodRow) != selectedModuleRow
            || tables.Methods.GetFlags(anchorMethodRow) != 0
            || tables.Methods.GetBodyRow(targetMethodRow) != 0
            || tables.Methods.GetBodyRow(anchorMethodRow) != 0)
        {
            throw Invalid("Invalid bounded foreign-invocation method roles.");
        }

        var entryMethodRow = tables.Modules.GetEntryMethod(selectedModuleRow);
        if (entryMethodRow != 0 && entryMethodRow != callerMethodRow)
        {
            throw Unsupported(
                "The source-free NativeAddress loader supports the caller as the Module entry.");
        }

        if (selectedModuleImport is not null)
        {
            var expectedDefinition = 0x1600_0000u | (uint)targetMethodRow;
            if (selectedImportRow == 0
                || tables.Imports.GetExpectedDefinitionForImportRow(
                    selectedImportRow) != expectedDefinition
                || tables.Imports.GetModuleRefForImportRow(selectedImportRow)
                    != moduleRefStart
                || !tables.Imports.TryGetExpectedBinding(expectedDefinition,
                    out var expectedBinding)
                || !StringComparer.Ordinal.Equals(
                    expectedBinding.ProducerIdentity,
                    selectedModuleImport.ProducerIdentity)
                || !StringComparer.Ordinal.Equals(expectedBinding.ExportKey,
                    selectedModuleImport.ExportKey)
                || expectedBinding.TargetRequirement
                    != selectedModuleImport.TargetRequirement
                || expectedBinding.RuntimePolicy
                    != selectedModuleImport.RuntimePolicy)
            {
                throw Invalid(
                    "The selected ModuleImport differs from the retained Import expectation.");
            }
        }

        EnsureNativeAddressContracts(tables.Methods, tables.Contracts,
            targetMethodRow, tables.Contracts.GetNativeParameters(targetMethodRow).Length + 1,
            allowEffects: true);
        EnsureNativeAddressContracts(tables.Methods, tables.Contracts,
            callerMethodRow, 0, allowEffects: false);
        EnsureNativeAddressContracts(tables.Methods, tables.Contracts,
            anchorMethodRow, tables.Contracts.GetNativeParameters(anchorMethodRow).Length,
            allowEffects: false);
        if (tables.Contracts.GetEffectsMask(targetMethodRow) != 4
            || tables.Contracts.GetEffectsMask(callerMethodRow) != 0
            || tables.Contracts.GetEffectsMask(anchorMethodRow) != 0
            || !tables.Contracts.GetNativeOutputs(targetMethodRow).IsEmpty
            || !tables.Contracts.GetNativeOutputs(anchorMethodRow).IsEmpty)
        {
            throw Unsupported(
                "The source-free NativeAddress loader supports caller-unsafe imports without output contracts or additional effects.");
        }

        var structureTypeByTypeDef = new int[tables.TypeDefs.Count + 1];
        Array.Fill(structureTypeByTypeDef, -1);
        var nativePointerTypeDefinitions = new List<NativePointerTypeDefinition>();
        var signatureIndexByAnchorMethod = new int[tables.Methods.Count + 1];
        Array.Fill(signatureIndexByAnchorMethod, -1);
        var anchorTypeSpecRow = 0;
        for (var row = 1; row <= tables.TypeSpecs.Count; row++)
        {
            if (!tables.NativeFunctionPointerAnchors.HasAnchorForTypeSpec(row))
            {
                continue;
            }

            if (tables.NativeFunctionPointerAnchors
                    .GetAnchorMethodDefRow(row) != anchorMethodRow)
            {
                continue;
            }

            if (anchorTypeSpecRow != 0)
            {
                throw Unsupported("The source-free loader supports one pointer TypeSpec anchor.");
            }

            anchorTypeSpecRow = row;
            signatureIndexByAnchorMethod[anchorMethodRow] = 0;
        }

        if (anchorTypeSpecRow == 0)
        {
            throw Invalid("The NativeAddress type has no function-pointer anchor.");
        }

        var anchorSignatureRow = tables.Methods.GetSignature(anchorMethodRow);
        var anchorParameterTypes = ReadForeignInvocationParameterTypes(
            anchorMethodRow, tables.Methods, tables.Signatures, tables.Parameters,
            tables.TypeSpecs, nativePointerTypeDefinitions,
            signatureIndexByAnchorMethod, structureTypeByTypeDef);
        var anchorResultType = ReadForeignInvocationType(tables.TypeSpecs,
            tables.Signatures.GetResultTypeSpec(anchorSignatureRow),
            nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
            structureTypeByTypeDef);
        if (!IsForeignInvocationValueType(anchorResultType, allowVoid: true))
        {
            throw Unsupported("The pointer anchor result type is unsupported.");
        }

        var anchorNativeParameters = ReadForeignInvocationNativeParameters(
            anchorMethodRow, tables.Methods, tables.Signatures, tables.Parameters,
            tables.TypeSpecs, tables.Contracts, tables.Layouts,
            nativePointerTypeDefinitions, structureTypeByTypeDef);
        var functionPointerSignatures =
            ImmutableArray.Create(new NativeFunctionPointerSignatureDefinition(
                (NativeCallingConvention)tables.NativeFunctionPointerAnchors
                    .GetCallingConvention(anchorTypeSpecRow),
                anchorResultType, anchorParameterTypes)
            {
                EffectsMask = 0,
                NativeParameters = anchorNativeParameters,
            });

        if (!NativeFunctionPointerContract.IsValidTable(functionPointerSignatures))
        {
            throw Invalid("Invalid foreign function-pointer signature.");
        }

        var targetSignatureRow = tables.Methods.GetSignature(targetMethodRow);
        if (!TrRmNativeImportTypeContract.TryGetDirectNativeImportResultType(
                tables.TypeSpecs,
                tables.Signatures.GetResultTypeSpec(targetSignatureRow), out _))
        {
            throw Unsupported("The direct NativeImport result type is unsupported.");
        }

        var targetParameterTypes = ReadForeignInvocationParameterTypes(
            targetMethodRow, tables.Methods, tables.Signatures, tables.Parameters,
            tables.TypeSpecs, nativePointerTypeDefinitions,
            signatureIndexByAnchorMethod, structureTypeByTypeDef);
        var targetResultType = ReadForeignInvocationType(tables.TypeSpecs,
            tables.Signatures.GetResultTypeSpec(targetSignatureRow),
            nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
            structureTypeByTypeDef);
        if (!IsForeignInvocationValueType(targetResultType, allowVoid: true))
        {
            throw Unsupported("The direct NativeImport result type is unsupported.");
        }

        var targetNativeParameters = ReadForeignInvocationNativeParameters(
            targetMethodRow, tables.Methods, tables.Signatures, tables.Parameters,
            tables.TypeSpecs, tables.Contracts, tables.Layouts,
            nativePointerTypeDefinitions, structureTypeByTypeDef);
        var nativeImportRow
            = tables.StandardNativeImports.GetImportForMethod(targetMethodRow);
        var directNativeImport = new DirectNativeImport(
            tables.StandardNativeImports.GetLibraryName(nativeImportRow),
            tables.StandardNativeImports.GetEntryName(nativeImportRow), [])
        {
            CallingConvention = (NativeCallingConvention)nativeImportRow.CallConv,
            ResolutionMode = (NativeImportResolutionMode)nativeImportRow.ResolutionMode,
            ResolutionPolicy = (NativeImportResolutionPolicy)nativeImportRow.ResolutionPolicy,
            Parameters = targetNativeParameters,
        };
        var nativePointerTypes = nativePointerTypeDefinitions.ToImmutableArray();
        if (!NativePointerTypeContract.IsValidTable(nativePointerTypes, [],
                functionPointerSignatures)
            || !NativeFunctionPointerContract.TryCreateNativeImportSignature(
                new BoundFunction(tables.Methods.GetName(targetMethodRow),
                    targetResultType,
                    CreateVariables(targetParameterTypes, "arg"),
                    targetParameterTypes.Length, new BoundBlock([], default), default)
                {
                    DirectNativeImport = directNativeImport,
                    IsExternal = true,
                    RequiresUnsafeCaller = true,
                },
                functionPointerSignatures, out var actualSignature)
            || actualSignature is null
            || !NativeFunctionPointerContract.AreEquivalent(
                functionPointerSignatures[0], actualSignature))
        {
            throw Invalid(
                "The NativeAddress anchor does not preserve the direct NativeImport contract.");
        }

        var bodyRow = tables.Methods.GetBodyRow(callerMethodRow);
        tables.ValidateRegisterBodyMapsForMethod(callerMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow, tables.Contracts.Count);
        var callerSignatureRow = tables.Methods.GetSignature(callerMethodRow);
        var callerParameterTypes = ReadForeignInvocationParameterTypes(
            callerMethodRow, tables.Methods, tables.Signatures, tables.Parameters,
            tables.TypeSpecs, nativePointerTypeDefinitions,
            signatureIndexByAnchorMethod, structureTypeByTypeDef);
        var callerResultType = ReadForeignInvocationType(tables.TypeSpecs,
            tables.Signatures.GetResultTypeSpec(callerSignatureRow),
            nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
            structureTypeByTypeDef);
        if (!IsForeignInvocationValueType(callerResultType, allowVoid: true))
        {
            throw Unsupported("The caller result type is unsupported.");
        }

        var arrayTypeByTypeSpecRow = new Dictionary<int, PrimitiveType>();
        var nativeFixedArrayTypes = (schema is
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft)
                ? ReadForeignInvocationFixedArrayTypes(tables,
                    selectedModuleRow, nativePointerTypeDefinitions,
                    signatureIndexByAnchorMethod, structureTypeByTypeDef,
                    [], out arrayTypeByTypeSpecRow)
                : [];
        var typeArray = nativePointerTypeDefinitions.ToImmutableArray();
        if (!NativePointerTypeContract.IsValidTable(typeArray, [],
                functionPointerSignatures))
        {
            throw Invalid("Invalid native pointer type table.");
        }

        var functions = ImmutableArray.CreateBuilder<BoundFunction>(2);
        var functionIndexByMethodRow = new int[tables.Methods.Count + 1];
        Array.Fill(functionIndexByMethodRow, -1);
        var functionIndex = 0;
        for (var row = methodStart; row < methodEnd; row++)
        {
            if (row != anchorMethodRow)
            {
                functionIndexByMethodRow[row] = functionIndex;
                functionIndex++;
            }
        }

        var targetIndex = functionIndexByMethodRow[targetMethodRow];
        var (callerBody, callerVariables) = ReadForeignInvocationCallerBody(
            body, tables.TypeSpecs, tables.Imports,
            tables.TargetProfiles, tables.FixedArrayLayouts,
            arrayTypeByTypeSpecRow,
            nativeFixedArrayTypes, selectedModuleRow,
            targetMethodRow, targetIndex,
            callerParameterTypes, callerResultType,
            targetParameterTypes, functionPointerSignatures[0],
            nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
            structureTypeByTypeDef);
        for (var row = methodStart; row < methodEnd; row++)
        {
            if (row == anchorMethodRow)
            {
                continue;
            }

            var flags = tables.Methods.GetFlags(row);
            var isTarget = row == targetMethodRow;
            var parameterTypes = isTarget ? targetParameterTypes : callerParameterTypes;
            var variables = isTarget
                ? CreateVariables(parameterTypes, "arg")
                : callerVariables;
            var function = new BoundFunction(tables.Methods.GetName(row),
                isTarget ? targetResultType : callerResultType,
                variables, parameterTypes.Length,
                isTarget ? new BoundBlock([], default) : callerBody, default)
            {
                DirectNativeImport = isTarget ? directNativeImport : null,
                ModuleImport = isTarget ? selectedModuleImport : null,
                IsModuleReference = isTarget && selectedModuleImport is not null,
                IsExternal = isTarget,
                RequiresUnsafeCaller = isTarget,
                IsPrivate = flags == 0,
                IsInternal = flags == 1,
            };
            functions.Add(function);
        }

        var callerFunctionIndex = functionIndexByMethodRow[callerMethodRow];
        if (callerFunctionIndex < 0)
        {
            throw Invalid("The selected caller MethodDef has no reconstructed function.");
        }

        var entryPoint = entryMethodRow == 0 ? -1 : callerFunctionIndex;
        var program = new BoundProgram(functions.MoveToImmutable(), entryPoint)
        {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeFunctionPointerSignatures = functionPointerSignatures,
            NativePointerTypes = typeArray,
            NativeFixedArrayTypes = nativeFixedArrayTypes,
        };
        if (verifySemantics)
        {
            if (entryPoint < 0)
            {
                SemanticVerifier.VerifyLibraryWithDirectNativeImports(program);
            }
            else
            {
                SemanticVerifier.VerifyWithDirectNativeImports(program);
            }
        }

        return program;
    }

    private static (BoundBlock Body, ImmutableArray<BoundVariable> Variables)
        ReadForeignInvocationCallerBody(
        TrRmRegisterBodyReader body, TrRmTypeSpecTableReader typeSpecs,
        TrRmImportTableReader imports,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmFixedArrayLayoutTableReader fixedArrayLayouts,
        IReadOnlyDictionary<int, PrimitiveType> arrayTypeByTypeSpecRow,
        ImmutableArray<BoundNativeFixedArrayType> nativeFixedArrayTypes,
        int selectedModuleRow,
        int targetMethodRow, int targetIndex,
        ImmutableArray<PrimitiveType> callerParameterTypes,
        PrimitiveType callerResultType,
        ImmutableArray<PrimitiveType> targetParameterTypes,
        NativeFunctionPointerSignatureDefinition signature,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef)
    {
        if (body.BlockCount != 1 || body.RecordCount < 2)
        {
            throw Unsupported("The caller Body is outside the bounded NativeAddress subset.");
        }

        var hasStorageAddressRecords = false;
        if (body.Schema.UsesForeignInvocationStorageAddresses())
        {
            for (var recordIndex = 0; recordIndex < body.RecordCount;
                recordIndex++)
            {
                if (body.GetRecord(recordIndex)[0] == 0x0E)
                {
                    hasStorageAddressRecords = true;
                    break;
                }
            }
        }

        if (hasStorageAddressRecords)
        {
            return ReadForeignInvocationStorageAddressCallerBody(body,
                typeSpecs, imports, targetProfiles, fixedArrayLayouts,
                arrayTypeByTypeSpecRow, nativeFixedArrayTypes,
                selectedModuleRow, targetMethodRow, targetIndex,
                callerParameterTypes, callerResultType, targetParameterTypes,
                signature, nativePointerTypeDefinitions,
                signatureIndexByAnchorMethod, structureTypeByTypeDef);
        }

        var nativeAddress = body.GetRecord(1);
        if (nativeAddress[0] != 0x0D)
        {
            throw Invalid("The NativeAddress instruction references no address record.");
        }

        var pointerTypeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            nativeAddress[10..]);
        var pointerTypeSpecRef = ReadForeignInvocationBodyTypeSpec(body,
            pointerTypeMapIndex);
        var pointerType = ReadForeignInvocationType(typeSpecs,
            pointerTypeSpecRef, nativePointerTypeDefinitions,
            signatureIndexByAnchorMethod, structureTypeByTypeDef);
        if (pointerType != NativeFunctionPointerTypes.Type(0)
            || ReadForeignInvocationBodyMethodDef(body, imports,
                BinaryPrimitives.ReadUInt16LittleEndian(nativeAddress[8..]))
                != targetMethodRow)
        {
            throw Invalid("The NativeAddress record does not select the retained target and anchor.");
        }

        var address = new BoundNativeFunctionAddress(targetIndex, pointerType, default)
        {
            IsUnsafeAuthorized = true,
        };
        if (body.RecordCount == 2)
        {
            var code = body.GetBlockCode(0);
            if (callerParameterTypes.Length != 0
                || callerResultType != PrimitiveType.Void
                || body.HomeMap.Length != 0
                || nativeAddress[4] != 1
                || BinaryPrimitives.ReadUInt16LittleEndian(nativeAddress[6..]) != 15
                || !code.SequenceEqual(new byte[] { 0xE9, 0, 1, 0, 0x18, 0 }))
            {
                throw Unsupported("The discarded NativeAddress Body shape is unsupported.");
            }

            var variables = CreateVariables(callerParameterTypes, "arg")
                .Add(new BoundVariable("<discard>", pointerType, false, default));
            return (new BoundBlock([
                    new BoundUnsafe(new BoundBlock([
                        new BoundLocal(callerParameterTypes.Length, address, default),
                        new BoundReturn(null, default),
                    ], default), default),
                ], default), variables);
        }

        var codeWithCall = body.GetBlockCode(0);
        var callSiteCount = body.RecordCount - 2;
        var returnInstructionOffset = 4 + (callSiteCount * 4);
        if (nativeAddress[4] != 2
            || BinaryPrimitives.ReadUInt16LittleEndian(nativeAddress[6..]) != 0
            || codeWithCall.Length != returnInstructionOffset + 2
            || codeWithCall[0] != 0xE9
            || codeWithCall[1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(codeWithCall[2..]) != 1)
        {
            throw Unsupported("The caller Body is outside the bounded NativeAddress CallSite subset.");
        }

        for (var index = 0; index < callSiteCount; index++)
        {
            var instructionOffset = 4 + (index * 4);
            if (codeWithCall[instructionOffset] != 0xE0
                || codeWithCall[instructionOffset + 1] != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    codeWithCall[(instructionOffset + 2)..]) != index + 2)
            {
                throw Invalid("The NativeAddress Body callsite sequence is not canonical.");
            }
        }

        var callSiteRecordIndex = body.RecordCount - 1;
        var callSite = body.GetRecord(callSiteRecordIndex);
        var inputCount = BinaryPrimitives.ReadUInt32LittleEndian(callSite[12..]);
        var preparationCount = BinaryPrimitives.ReadUInt32LittleEndian(callSite[16..]);
        var resultKind = callSite[5];
        var resultIndex = BinaryPrimitives.ReadUInt16LittleEndian(callSite[10..]);
        var stagedCallCount = callSiteCount - 1;
        var stagedResultHomeCount = Math.Max(0, stagedCallCount - 1);
        var stagedResultHomeStart = 1 + (resultKind == 2 ? 1 : 0);
        var localHomeCount = stagedResultHomeStart + stagedResultHomeCount;
        if (callSite[4] != 3
            || BinaryPrimitives.ReadUInt16LittleEndian(callSite[8..]) != 0
            || preparationCount != 0
            || inputCount != (uint)targetParameterTypes.Length
            || body.HomeMap.Length != (callerParameterTypes.Length + localHomeCount) * 8
            || body.HomeMap[0] != 3
            || BinaryPrimitives.ReadUInt16LittleEndian(body.HomeMap[2..])
                != pointerTypeMapIndex
            || BinaryPrimitives.ReadUInt16LittleEndian(body.HomeMap[4..]) != 0)
        {
            throw Invalid("The NativeAddress CallSite has unsupported Homes or inputs.");
        }

        var resultType = signature.ReturnType;
        var returnsCallResult = resultType != PrimitiveType.Void;
        var hasResultHome = resultKind == 2;
        if ((!returnsCallResult && (resultKind != 0 || resultIndex != 0))
            || (returnsCallResult && resultKind == 0)
            || (resultKind == 1 && resultIndex != 0)
            || (hasResultHome && resultIndex != 1)
            || (returnsCallResult && callerResultType != resultType)
            || (!returnsCallResult && callerResultType != PrimitiveType.Void)
            || (hasResultHome
                && (body.HomeMap[8] != 3
                    || ReadForeignInvocationType(typeSpecs,
                        ReadForeignInvocationBodyTypeSpec(body,
                            BinaryPrimitives.ReadUInt16LittleEndian(body.HomeMap[10..])),
                        nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                        structureTypeByTypeDef) != resultType)))
        {
            throw Invalid("The NativeAddress CallSite result does not match its saved signature.");
        }

        for (var stagedIndex = 0;
            stagedIndex < stagedResultHomeCount; stagedIndex++)
        {
            var homeIndex = stagedResultHomeStart + stagedIndex;
            var offset = homeIndex * 8;
            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap[(offset + 2)..]);
            if (body.HomeMap[offset] != 3
                || ReadForeignInvocationType(typeSpecs,
                    ReadForeignInvocationBodyTypeSpec(body, typeMapIndex),
                    nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                    structureTypeByTypeDef) != signature.ReturnType)
            {
                throw Invalid("A staged CallSite result Home has the wrong type.");
            }
        }

        var formalOrdinalByHome = new int[callerParameterTypes.Length
            + localHomeCount];
        Array.Fill(formalOrdinalByHome, -1);
        for (var ordinal = 0; ordinal < callerParameterTypes.Length; ordinal++)
        {
            var homeIndex = ordinal + localHomeCount;
            var offset = homeIndex * 8;
            if (body.HomeMap[offset] != 2
                || BinaryPrimitives.ReadUInt16LittleEndian(body.HomeMap[(offset + 2)..])
                    != GetTypeMapIndexForType(body, callerParameterTypes[ordinal],
                        typeSpecs, nativePointerTypeDefinitions,
                        signatureIndexByAnchorMethod, structureTypeByTypeDef)
                || BinaryPrimitives.ReadUInt16LittleEndian(body.HomeMap[(offset + 4)..])
                    != ordinal)
            {
                throw Invalid("The NativeAddress CallSite formal Home map is invalid.");
            }

            formalOrdinalByHome[homeIndex] = ordinal;
        }

        var stagedCalls = new BoundCall[stagedCallCount];
        for (var stagedIndex = 0; stagedIndex < stagedCallCount; stagedIndex++)
        {
            var isFinalStagedCall = stagedIndex == stagedCallCount - 1;
            stagedCalls[stagedIndex] = ReadForeignInvocationStagedCall(body,
                body.GetRecord(stagedIndex + 2), imports,
                targetMethodRow, targetIndex,
                callerParameterTypes, targetParameterTypes, signature,
                formalOrdinalByHome,
                isFinalStagedCall ? (byte)1 : (byte)2,
                isFinalStagedCall
                    ? (ushort)0
                    : checked((ushort)(stagedResultHomeStart + stagedIndex)));
        }

        var callArguments = ImmutableArray.CreateBuilder<BoundExpression>(
            targetParameterTypes.Length);
        var stagedCallUsed = new bool[stagedCallCount];
        for (var ordinal = 0; ordinal < targetParameterTypes.Length; ordinal++)
        {
            var inputOffset = 20 + (ordinal * 4);
            var selectorKind = callSite[inputOffset];
            var sourceUse = callSite[inputOffset + 1];
            var selectorIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                callSite[(inputOffset + 2)..]);
            if (selectorKind == 1)
            {
                if (sourceUse != 1 || selectorIndex != 0
                    || stagedCallCount == 0)
                {
                    throw Invalid("The CallSite register input has no staged call result.");
                }

                var stagedIndex = stagedCallCount - 1;
                var stagedCall = stagedCalls[stagedIndex];
                if (stagedCallUsed[stagedIndex]
                    || stagedCall.Type != targetParameterTypes[ordinal])
                {
                    throw Invalid("The staged CallSite result type differs from its formal.");
                }

                callArguments.Add(stagedCall);
                stagedCallUsed[stagedIndex] = true;
                continue;
            }

            if (selectorKind != 2 || sourceUse != 1)
            {
                throw Unsupported("Only direct formal-Home CallSite inputs are supported.");
            }

            if (selectorIndex >= formalOrdinalByHome.Length
                || formalOrdinalByHome[selectorIndex] < 0)
            {
                var stagedIndex = selectorIndex >= stagedResultHomeStart
                    ? selectorIndex - stagedResultHomeStart
                    : ushort.MaxValue;
                if (stagedIndex >= stagedResultHomeCount
                    || stagedCallUsed[stagedIndex]
                    || stagedCalls[stagedIndex].Type
                        != targetParameterTypes[ordinal])
                {
                    throw Invalid(
                        "The NativeAddress CallSite input does not select a formal or staged result Home.");
                }

                callArguments.Add(stagedCalls[stagedIndex]);
                stagedCallUsed[stagedIndex] = true;
                continue;
            }

            var formalOrdinal = formalOrdinalByHome[selectorIndex];
            var type = callerParameterTypes[formalOrdinal];
            if (type != targetParameterTypes[ordinal])
            {
                throw Invalid("The NativeAddress CallSite input type differs from its formal.");
            }

            callArguments.Add(new BoundRead(formalOrdinal, type, default));
        }

        for (var stagedIndex = 0;
            stagedIndex < stagedCallUsed.Length; stagedIndex++)
        {
            if (!stagedCallUsed[stagedIndex])
            {
                throw Invalid(
                    "A staged direct call result is not consumed by the indirect call.");
            }
        }

        if (codeWithCall[returnInstructionOffset] == 0xB8)
        {
            if (!hasResultHome
                || codeWithCall[returnInstructionOffset + 1] != resultIndex)
            {
                throw Invalid("ReturnHome does not select the CallSite result Home.");
            }
        }
        else if (codeWithCall[returnInstructionOffset] == 0x17)
        {
            if (!returnsCallResult || hasResultHome
                || codeWithCall[returnInstructionOffset + 1] != 0)
            {
                throw Invalid("ReturnRegister does not select the CallSite result.");
            }
        }
        else if (codeWithCall[returnInstructionOffset] == 0x18)
        {
            if (returnsCallResult
                || codeWithCall[returnInstructionOffset + 1] != 0)
            {
                throw Invalid("ReturnUnit does not match the CallSite result.");
            }
        }
        else
        {
            throw Unsupported("The NativeAddress caller has an unsupported return instruction.");
        }

        var addressLocal = new BoundVariable("__trrm_native_address",
            pointerType, body.HomeMap[1] != 0, default);
        var callerLocalCount = 1 + (hasResultHome ? 1 : 0);
        var locals = ImmutableArray.CreateBuilder<BoundVariable>(
            callerParameterTypes.Length + callerLocalCount);
        locals.AddRange(CreateVariables(callerParameterTypes, "arg"));
        locals.Add(addressLocal);
        if (hasResultHome)
        {
            locals.Add(new BoundVariable("__trrm_result", resultType,
                body.HomeMap[9] != 0, default));
        }

        var indirectCall = new BoundIndirectCall(
            new BoundRead(callerParameterTypes.Length, pointerType, default),
            callArguments.MoveToImmutable(), resultType, default)
        {
            IsUnsafeAuthorized = true,
        };
        var statements = ImmutableArray.CreateBuilder<BoundStatement>(
            resultKind == 1 ? 2 : 3);
        statements.Add(new BoundLocal(callerParameterTypes.Length, address, default));
        if (resultKind == 0)
        {
            statements.Add(new BoundExpressionStatement(indirectCall, default));
            statements.Add(new BoundReturn(null, default));
        }
        else if (resultKind == 1)
        {
            statements.Add(new BoundReturn(indirectCall, default));
        }
        else
        {
            statements.Add(new BoundLocal(callerParameterTypes.Length + 1,
                indirectCall, default));
            statements.Add(new BoundReturn(new BoundRead(
                callerParameterTypes.Length + 1, resultType, default), default));
        }

        return (new BoundBlock([
                new BoundUnsafe(new BoundBlock(statements.MoveToImmutable(), default),
                    default),
            ], default),
            locals.MoveToImmutable());
    }

    private static (BoundBlock Body, ImmutableArray<BoundVariable> Variables)
        ReadForeignInvocationStorageAddressCallerBody(
            TrRmRegisterBodyReader body, TrRmTypeSpecTableReader typeSpecs,
            TrRmImportTableReader imports,
            TrRmTargetProfileTableReader targetProfiles,
            TrRmFixedArrayLayoutTableReader fixedArrayLayouts,
            IReadOnlyDictionary<int, PrimitiveType> arrayTypeByTypeSpecRow,
            ImmutableArray<BoundNativeFixedArrayType> nativeFixedArrayTypes,
            int selectedModuleRow,
            int targetMethodRow, int targetIndex,
            ImmutableArray<PrimitiveType> callerParameterTypes,
            PrimitiveType callerResultType,
            ImmutableArray<PrimitiveType> targetParameterTypes,
            NativeFunctionPointerSignatureDefinition signature,
            List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
            int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef)
    {
        if (body.MapMask != 0x07 || body.RecordCount < 4)
        {
            throw Unsupported(
                "The F+S caller requires Home, Type and Method maps and at least one storage address.");
        }

        var block = body.GetBlock(0);
        var entryState = body.GetRecord(
            BinaryPrimitives.ReadUInt16LittleEndian(block[8..]));
        if (entryState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(entryState[4..]) != 0)
        {
            throw Invalid("The F+S caller has unexpected entry registers.");
        }

        var instructions = new List<(byte Opcode, int Index, byte Register)>();
        var referencedRecords = new HashSet<int>
        {
            BinaryPrimitives.ReadUInt16LittleEndian(block[8..]),
        };
        var storageRecordIndexes = new HashSet<int>();
        var nativeAddressRecordIndex = -1;
        var callSiteRecordIndex = -1;
        var returnHome = -1;
        var code = body.GetBlockCode(0);
        var position = 0;
        while (position < code.Length)
        {
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                code[position], body.Schema);
            var instruction = code.Slice(position, instructionLength);
            switch (instruction[0])
            {
                case 0x14:
                    var shortSource = instruction[1] >> 4;
                    if (shortSource is not (14 or 15))
                    {
                        throw Unsupported(
                            "F+S local initialization requires an admitted contextual register.");
                    }
                    instructions.Add((0x14, instruction[1] & 0x0F,
                        checked((byte)shortSource)));
                    break;

                case 0xD6:
                    var storeControl = instruction[1];
                    var longSource = storeControl & 0x0F;
                    if ((storeControl >> 4) != 1
                        || longSource is not (14 or 15))
                    {
                        throw Unsupported(
                            "F+S local initialization requires CopyRead from R14 or R15.");
                    }
                    instructions.Add((0xD6,
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]), checked((byte)longSource)));
                    break;

                case 0x13:
                case 0x10:
                case 0x20:
                case 0x21:
                case 0x33:
                    instructions.Add((instruction[0], instruction[1], 0));
                    break;

                case 0xD5:
                    if ((instruction[1] & 0xF0) != 0)
                    {
                        throw Invalid("Invalid F+S Home-load control.");
                    }
                    instructions.Add((0xD5,
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]),
                        checked((byte)(instruction[1] & 0x0F))));
                    break;

                case 0xD8:
                case 0xD9:
                    var placeRecordControl = instruction[1];
                    if (instruction[0] == 0xD8
                        ? (placeRecordControl & 0xF0) != 0
                        : placeRecordControl != 0)
                    {
                        throw Invalid("Invalid F+S place instruction control.");
                    }

                    var placeRecordIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    if (!referencedRecords.Add(placeRecordIndex))
                    {
                        throw Invalid("An F+S place record is referenced more than once.");
                    }
                    instructions.Add((instruction[0], placeRecordIndex,
                        instruction[0] == 0xD8
                            ? checked((byte)(placeRecordControl & 0x0F))
                            : (byte)0));
                    break;

                case 0xE9:
                case 0xEC:
                case 0xE0:
                    if (instruction[1] != 0)
                    {
                        throw Invalid("Invalid F+S record instruction control.");
                    }

                    var recordIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                        instruction[2..]);
                    if (!referencedRecords.Add(recordIndex))
                    {
                        throw Invalid("An F+S record is referenced more than once.");
                    }
                    if (instruction[0] == 0xE9)
                    {
                        if (nativeAddressRecordIndex != -1)
                        {
                            throw Unsupported(
                                "The F+S Body supports one NativeAddress formation.");
                        }
                        nativeAddressRecordIndex = recordIndex;
                    }
                    else if (instruction[0] == 0xEC)
                    {
                        if (!storageRecordIndexes.Add(recordIndex))
                        {
                            throw Invalid(
                                "A storage-address record is reused by multiple instructions.");
                        }
                    }
                    else
                    {
                        if (callSiteRecordIndex != -1)
                        {
                            throw Unsupported(
                                "The F+S Body supports one indirect CallSite.");
                        }
                        callSiteRecordIndex = recordIndex;
                    }
                    instructions.Add((instruction[0], recordIndex, 0));
                    break;

                case 0xB8:
                    if (returnHome != -1
                        || position + instructionLength != code.Length)
                    {
                        throw Unsupported(
                            "The F+S Body supports one terminal ReturnHome.");
                    }
                    returnHome = instruction[1];
                    instructions.Add((0xB8, returnHome, 0));
                    break;

                default:
                    throw Unsupported(
                        "The F+S Body contains an unsupported instruction.");
            }

            position += instructionLength;
        }

        if (nativeAddressRecordIndex <= 0 || callSiteRecordIndex <= 0
            || returnHome < 0 || storageRecordIndexes.Count == 0
            || referencedRecords.Count != body.RecordCount)
        {
            throw Invalid("The F+S Body operation sequence is incomplete.");
        }

        var nativeAddress = body.GetRecord(nativeAddressRecordIndex);
        var targetHomeValue = BinaryPrimitives.ReadUInt16LittleEndian(
            nativeAddress[6..]);
        if (nativeAddress[0] != 0x0D
            || nativeAddress[4] != 2
            || targetHomeValue >= body.HomeCount
            || ReadForeignInvocationBodyMethodDef(body, imports,
                BinaryPrimitives.ReadUInt16LittleEndian(nativeAddress[8..]))
                != targetMethodRow)
        {
            throw Invalid("The F+S NativeAddress record has the wrong target.");
        }

        var targetHome = checked((int)targetHomeValue);
        var pointerTypeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            nativeAddress[10..]);
        var pointerType = ReadForeignInvocationType(typeSpecs,
            ReadForeignInvocationBodyTypeSpec(body, pointerTypeMapIndex),
            nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
            structureTypeByTypeDef);
        if (pointerType != NativeFunctionPointerTypes.Type(0))
        {
            throw Invalid("The F+S NativeAddress type differs from its anchor.");
        }

        var localVariableByHome = new int[body.HomeCount];
        Array.Fill(localVariableByHome, -1);
        var formalOrdinalByHome = new int[body.HomeCount];
        Array.Fill(formalOrdinalByHome, -1);
        var variables = ImmutableArray.CreateBuilder<BoundVariable>(
            callerParameterTypes.Length + body.HomeCount);
        variables.AddRange(CreateVariables(callerParameterTypes, "arg"));
        var seenFormalOrdinals = new bool[callerParameterTypes.Length];
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            if (home[0] != 2)
            {
                continue;
            }

            var ordinal = BinaryPrimitives.ReadUInt16LittleEndian(home[4..]);
            if (ordinal >= callerParameterTypes.Length
                || seenFormalOrdinals[ordinal]
                || ReadForeignInvocationType(typeSpecs,
                    ReadForeignInvocationBodyTypeSpec(body,
                        BinaryPrimitives.ReadUInt16LittleEndian(home[2..])),
                    nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                    structureTypeByTypeDef) != callerParameterTypes[ordinal])
            {
                throw Invalid("The F+S formal Home map is invalid.");
            }
            formalOrdinalByHome[homeIndex] = ordinal;
            seenFormalOrdinals[ordinal] = true;
        }

        for (var ordinal = 0; ordinal < seenFormalOrdinals.Length; ordinal++)
        {
            if (!seenFormalOrdinals[ordinal])
            {
                throw Invalid("The F+S Body omits a formal Home.");
            }
        }

        var storageAddressesByHome
            = new Dictionary<int, NativeStorageAddressFacts>();
        var consumedStorageHomes = new HashSet<int>();
        var localConstantByHome = new Dictionary<int, long>();
        var placeSelectionsByRegister
            = new Dictionary<int, NativeArrayPlaceSelectionFacts>();
        var registerConstants
            = new Dictionary<int, NativeAddressRegisterConstant>();
        var arrayInitializationElementsByHome
            = new Dictionary<int, List<BoundExpression>>();
        var statements = ImmutableArray.CreateBuilder<BoundStatement>();
        var sawCallSite = false;
        var sawReturn = false;
        var instructionOrdinal = 0;
        foreach (var instruction in instructions)
        {
            switch (instruction.Opcode)
            {
                case 0x14:
                case 0xD6:
                    var localHome = instruction.Index;
                    if ((uint)localHome >= (uint)body.HomeCount
                        || body.HomeMap[localHome * 8] != 3
                        || localVariableByHome[localHome] != -1
                        || storageAddressesByHome.ContainsKey(localHome))
                    {
                        throw Invalid(
                            "A contextual Home store must initialize one vacant local Home.");
                    }

                    var localMapType = BinaryPrimitives.ReadUInt16LittleEndian(
                        body.HomeMap[((localHome * 8) + 2)..]);
                    var localType = ReadForeignInvocationType(typeSpecs,
                        ReadForeignInvocationBodyTypeSpec(body, localMapType),
                        nativePointerTypeDefinitions,
                        signatureIndexByAnchorMethod, structureTypeByTypeDef);
                    if (!DirectNativeImportContract.IsScalar(localType)
                        || !TryGetContextualConstant(instruction.Register,
                            localType, out var initialValue))
                    {
                        throw Unsupported(
                            "The F+S Home store has no admitted scalar contextual constant.");
                    }

                    var localVariable = variables.Count;
                    localVariableByHome[localHome] = localVariable;
                    variables.Add(new BoundVariable(
                        $"__trrm_home_{localHome}", localType,
                        body.HomeMap[(localHome * 8) + 1] != 0, default));
                    statements.Add(new BoundLocal(localVariable,
                        new BoundConstant(initialValue, localType, default),
                        default));
                    localConstantByHome.Add(localHome, initialValue);
                    break;

                case 0x13:
                    var shortLoadDestination = instruction.Index & 0x0F;
                    var shortLoadHome = instruction.Index >> 4;
                    if (shortLoadHome >= body.HomeCount
                        || localVariableByHome[shortLoadHome] < 0
                        || !localConstantByHome.TryGetValue(shortLoadHome,
                            out var shortLoadValue)
                        || variables[localVariableByHome[shortLoadHome]].Type
                            != PrimitiveType.Int32)
                    {
                        throw Invalid(
                            "The F+S array-index register load is unsupported.");
                    }

                    registerConstants[shortLoadDestination]
                        = new NativeAddressRegisterConstant(
                            PrimitiveType.Int32,
                            unchecked((uint)shortLoadValue));
                    break;

                case 0xD5:
                    var longLoadHome = instruction.Index;
                    var longLoadDestination = instruction.Register;
                    if (longLoadHome >= body.HomeCount
                        || localVariableByHome[longLoadHome] < 0
                        || !localConstantByHome.TryGetValue(longLoadHome,
                            out var longLoadValue)
                        || variables[localVariableByHome[longLoadHome]].Type
                            != PrimitiveType.Int32)
                    {
                        throw Invalid(
                            "The F+S array-index register load is unsupported.");
                    }

                    registerConstants[longLoadDestination]
                        = new NativeAddressRegisterConstant(
                            PrimitiveType.Int32,
                            unchecked((uint)longLoadValue));
                    break;

                case 0x10:
                case 0x20:
                case 0x21:
                case 0x33:
                    var registerOperand = checked((byte)instruction.Index);
                    var destinationRegister = registerOperand & 0x0F;
                    var rightRegister = registerOperand >> 4;
                    if (instruction.Opcode == 0x10)
                    {
                        if (!registerConstants.TryGetValue(rightRegister,
                                out var moveSource))
                        {
                            throw Invalid(
                                "The F+S array-index move has no constant source.");
                        }

                        registerConstants[destinationRegister] = moveSource;
                        break;
                    }

                    if (!registerConstants.TryGetValue(destinationRegister,
                            out var leftValue)
                        || leftValue.Type != PrimitiveType.Int32)
                    {
                        throw Invalid(
                            "The F+S array-index operation has no int32 destination.");
                    }

                    if (instruction.Opcode == 0x33)
                    {
                        if (!registerConstants.TryGetValue(rightRegister,
                                out var notSource)
                            || notSource.Type != PrimitiveType.Int32)
                        {
                            throw Invalid(
                                "The F+S array-index NOT has no int32 source.");
                        }

                        registerConstants[destinationRegister]
                            = notSource with { Bits = ~notSource.Bits };
                    }
                    else if (instruction.Opcode == 0x20)
                    {
                        if (!registerConstants.TryGetValue(rightRegister,
                                out var negateSource)
                            || negateSource.Type != PrimitiveType.Int32)
                        {
                            throw Invalid(
                                "The F+S array-index negate has no int32 source.");
                        }

                        registerConstants[destinationRegister]
                            = negateSource with {
                                Bits = unchecked(0u - negateSource.Bits),
                            };
                    }
                    else
                    {
                        if (!registerConstants.TryGetValue(rightRegister,
                                out var addSource)
                            || addSource.Type != PrimitiveType.Int32)
                        {
                            throw Invalid(
                                "The F+S array-index addition has no int32 source.");
                        }

                        registerConstants[destinationRegister]
                            = leftValue with {
                                Bits = unchecked(leftValue.Bits
                                    + addSource.Bits),
                            };
                    }
                    break;

                case 0xD8:
                    if (placeSelectionsByRegister.ContainsKey(
                            instruction.Register))
                    {
                        throw Invalid(
                            "An F+S PlaceHandle is replaced before its operation.");
                    }

                    placeSelectionsByRegister.Add(instruction.Register,
                        ReadForeignInvocationArrayPlaceSelection(
                            body, typeSpecs, targetProfiles, fixedArrayLayouts,
                            arrayTypeByTypeSpecRow, nativeFixedArrayTypes,
                            nativePointerTypeDefinitions,
                            signatureIndexByAnchorMethod,
                            structureTypeByTypeDef, selectedModuleRow,
                            instruction.Index, instruction.Register));
                    break;

                case 0xD9:
                    var storeRecord = body.GetRecord(instruction.Index);
                    var storePayload = storeRecord[4..];
                    var previousInstruction = instructionOrdinal == 0
                        ? default : instructions[instructionOrdinal - 1];
                    var placeRegister
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            storePayload[4..]);
                    var sourceRegister
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            storePayload[6..]);
                    var isEmptyArrayInitialization
                        = storePayload[1] == 0;
                    if (instructionOrdinal == 0
                        || previousInstruction.Opcode != 0xD8
                        || previousInstruction.Register != placeRegister
                        || storeRecord[0] != 0x05
                        || storePayload[0] != 1
                        || storePayload[2] != 1
                        || storePayload[3] != (isEmptyArrayInitialization
                            ? 0 : 1)
                        || BinaryPrimitives.ReadUInt32LittleEndian(
                            storePayload[8..]) != 0
                        || storePayload[12] != 0 || storePayload[13] != 0
                        || BinaryPrimitives.ReadUInt16LittleEndian(
                            storePayload[14..]) != 0
                        || (isEmptyArrayInitialization
                            ? !body.Schema
                                .UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                                || sourceRegister != 0
                            : storePayload[1] != 1
                                || sourceRegister is not (14 or 15))
                        || !placeSelectionsByRegister.TryGetValue(
                            placeRegister, out var initializationSelection)
                        || initializationSelection.RecordIndex
                            != previousInstruction.Index
                        || initializationSelection.AccessKind != 3)
                    {
                        throw Invalid(
                            "The F+S SemanticStore does not initialize its selected array element.");
                    }

                    if (isEmptyArrayInitialization)
                    {
                        var emptyArrayType
                            = initializationSelection.TargetKind == 1
                                ? initializationSelection.ArrayType
                                : initializationSelection.ElementType;
                        if (!NativeFixedArrayTypes.IsType(emptyArrayType))
                        {
                            throw Invalid(
                                "The A2E SemanticStore does not target an empty array type.");
                        }

                        var emptyArrayLayout = nativeFixedArrayTypes[
                            NativeFixedArrayTypes.Index(emptyArrayType)];
                        if (emptyArrayLayout.ElementCount != 0
                            || emptyArrayLayout.ByteExtent != 0
                            || initializationSelection.TargetKind is not (1 or 3)
                            || localVariableByHome[
                                initializationSelection.BaseHome] != -1)
                        {
                            throw Invalid(
                                "The A2E SemanticStore does not initialize a vacant empty array.");
                        }

                        var emptyInitialization
                            = new BoundNativeFixedArrayInitialization(
                                new BoundNativeFixedArrayLayout(
                                    emptyArrayType, emptyArrayLayout.ElementType,
                                    emptyArrayLayout.ElementCount,
                                    emptyArrayLayout.ElementStride,
                                    emptyArrayLayout.ByteExtent,
                                    emptyArrayLayout.Alignment,
                                    emptyArrayLayout.RequiredAlignment),
                                [], default);
                        if (initializationSelection.TargetKind == 1)
                        {
                            if (initializationSelection.ElementCount != 0)
                            {
                                throw Invalid(
                                    "The A2E Home selection has an invalid element count.");
                            }

                            var emptyArrayHome
                                = initializationSelection.BaseHome;
                            if ((uint)emptyArrayHome >= (uint)body.HomeCount
                                || body.HomeMap[emptyArrayHome * 8] != 3)
                            {
                                throw Invalid(
                                    "The A2E Home selection has no local Home.");
                            }

                            var emptyArrayVariable = variables.Count;
                            localVariableByHome[emptyArrayHome]
                                = emptyArrayVariable;
                            variables.Add(new BoundVariable(
                                $"__trrm_home_{emptyArrayHome}",
                                emptyArrayType,
                                body.HomeMap[(emptyArrayHome * 8) + 1] != 0,
                                default));
                            statements.Add(new BoundLocal(
                                emptyArrayVariable, emptyInitialization,
                                default));
                        }
                        else
                        {
                            var initializedArrayHome
                                = initializationSelection.BaseHome;
                            var selectedElementIndex
                                = ReadArrayInitializationElementIndex(
                                    initializationSelection, body,
                                    registerConstants, localConstantByHome);
                            if (initializationSelection.ElementCount == 0
                                || selectedElementIndex
                                    >= initializationSelection.ElementCount)
                            {
                                throw Invalid(
                                    "The A2E child initialization has an invalid array index.");
                            }

                            if (!arrayInitializationElementsByHome.TryGetValue(
                                    initializedArrayHome,
                                    out var initializedElements))
                            {
                                initializedElements = [];
                                arrayInitializationElementsByHome.Add(
                                    initializedArrayHome,
                                    initializedElements);
                            }
                            if (selectedElementIndex
                                    != (ulong)initializedElements.Count
                                || body.HomeMap[initializedArrayHome * 8] != 3)
                            {
                                throw Invalid(
                                    "The A2E child initializations are not complete and source-ordered.");
                            }

                            initializedElements.Add(emptyInitialization);
                            if ((ulong)initializedElements.Count
                                == initializationSelection.ElementCount)
                            {
                                AddInitializedArrayLocal(
                                    initializationSelection.ArrayType,
                                    initializedArrayHome,
                                    initializationSelection.ElementCount,
                                    initializedElements, body,
                                    nativeFixedArrayTypes,
                                    localVariableByHome, variables,
                                    statements);
                                _ = arrayInitializationElementsByHome.Remove(
                                    initializedArrayHome);
                            }
                        }
                    }
                    else
                    {
                        if (initializationSelection.TargetKind != 3
                            || initializationSelection.ElementCount == 0
                            || localVariableByHome[
                                initializationSelection.BaseHome] != -1
                            || !TryGetContextualConstant(
                                checked((byte)sourceRegister),
                                initializationSelection.ElementType,
                                out var initializationValue))
                        {
                            throw Invalid(
                                "The F+S SemanticStore does not initialize its selected array element.");
                        }

                        var initializedArrayHome
                            = initializationSelection.BaseHome;
                        var selectedElementIndex
                            = ReadArrayInitializationElementIndex(
                                initializationSelection, body,
                                registerConstants, localConstantByHome);

                        if (selectedElementIndex
                                != (ulong)GetArrayInitializationElementCount(
                                    initializedArrayHome,
                                    arrayInitializationElementsByHome)
                            || selectedElementIndex
                                >= initializationSelection.ElementCount
                            || body.HomeMap[initializedArrayHome * 8] != 3)
                        {
                            throw Invalid(
                                "The F+S array element initializations are not complete and source-ordered.");
                        }

                        if (!arrayInitializationElementsByHome.TryGetValue(
                                initializedArrayHome,
                                out var initializedElements))
                        {
                            initializedElements = [];
                            arrayInitializationElementsByHome.Add(
                                initializedArrayHome, initializedElements);
                        }
                        initializedElements.Add(new BoundConstant(
                            initializationValue,
                            initializationSelection.ElementType, default));
                        if ((ulong)initializedElements.Count
                            == initializationSelection.ElementCount)
                        {
                            AddInitializedArrayLocal(
                                initializationSelection.ArrayType,
                                initializedArrayHome,
                                initializationSelection.ElementCount,
                                initializedElements, body,
                                nativeFixedArrayTypes, localVariableByHome,
                                variables, statements);
                            _ = arrayInitializationElementsByHome.Remove(
                                initializedArrayHome);
                        }
                    }

                    _ = placeSelectionsByRegister.Remove(placeRegister);
                    break;

                case 0xE9:
                    if (instruction.Index != nativeAddressRecordIndex
                        || localVariableByHome[targetHome] != -1
                        || body.HomeMap[targetHome * 8] != 3
                        || ReadForeignInvocationType(typeSpecs,
                            ReadForeignInvocationBodyTypeSpec(body,
                                BinaryPrimitives.ReadUInt16LittleEndian(
                                    body.HomeMap[((targetHome * 8) + 2)..])),
                            nativePointerTypeDefinitions,
                            signatureIndexByAnchorMethod,
                            structureTypeByTypeDef) != pointerType)
                    {
                        throw Invalid("The F+S NativeAddress result Home is invalid.");
                    }

                    var targetVariable = variables.Count;
                    localVariableByHome[targetHome] = targetVariable;
                    variables.Add(new BoundVariable(
                        $"__trrm_native_address_{targetHome}", pointerType,
                        body.HomeMap[(targetHome * 8) + 1] != 0, default));
                    statements.Add(new BoundLocal(targetVariable,
                        new BoundNativeFunctionAddress(targetIndex,
                            pointerType, default)
                        {
                            IsUnsafeAuthorized = true,
                        }, default));
                    break;

                case 0xEC:
                    var storageRecord = body.GetRecord(instruction.Index);
                    var storageSourceKind = storageRecord[4];
                    var storageResultKind = storageRecord[5];
                    if (storageRecord[0] != 0x0E
                        || storageSourceKind is not (1 or 2)
                        || storageResultKind != 2)
                    {
                        throw Invalid("The F+S storage-address record is unsupported.");
                    }

                    var storageSourceIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                        storageRecord[6..]);
                    var resultHome = BinaryPrimitives.ReadUInt16LittleEndian(
                        storageRecord[8..]);
                    var addressTypeMapIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            storageRecord[10..]);
                    var requiredElementCount
                        = BinaryPrimitives.ReadUInt32LittleEndian(
                            storageRecord[12..]);
                    if (resultHome >= body.HomeCount
                        || storageAddressesByHome.ContainsKey(resultHome)
                        || localVariableByHome[resultHome] >= 0
                        || requiredElementCount == 0)
                    {
                        throw Invalid("The F+S storage-address Home pair is invalid.");
                    }

                    var addressPointerType = ReadForeignInvocationType(
                        typeSpecs,
                        ReadForeignInvocationBodyTypeSpec(body,
                            addressTypeMapIndex),
                        nativePointerTypeDefinitions,
                        signatureIndexByAnchorMethod, structureTypeByTypeDef,
                        arrayTypeByTypeSpecRow: arrayTypeByTypeSpecRow);
                    if (!NativePointerTypes.IsType(addressPointerType))
                    {
                        throw Invalid("The F+S result is not a native pointer.");
                    }

                    var pointerDefinition = nativePointerTypeDefinitions[
                        NativePointerTypes.Index(addressPointerType)];
                    NativeStorageAddressFacts storageFacts;
                    if (storageSourceKind == 2)
                    {
                        var sourceHome = checked((int)storageSourceIndex);
                        if (sourceHome >= body.HomeCount
                            || localVariableByHome[sourceHome] < 0
                            || requiredElementCount != 1)
                        {
                            throw Invalid(
                                "The F+S scalar storage-address Home is invalid.");
                        }

                        var sourceTypeMapIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                body.HomeMap[((sourceHome * 8) + 2)..]);
                        var sourceType = ReadForeignInvocationType(typeSpecs,
                            ReadForeignInvocationBodyTypeSpec(body,
                                sourceTypeMapIndex),
                            nativePointerTypeDefinitions,
                            signatureIndexByAnchorMethod,
                            structureTypeByTypeDef,
                            arrayTypeByTypeSpecRow: arrayTypeByTypeSpecRow);
                        if (!DirectNativeImportContract.IsScalar(sourceType)
                            || (pointerDefinition.PointeeType
                                    != PrimitiveType.Void
                                && pointerDefinition.PointeeType
                                    != sourceType))
                        {
                            throw Unsupported(
                                "The F+S source-free reader supports scalar local storage for HomePlace addresses.");
                        }

                        var stride = PrimitiveTypeFacts.ScalarWidth(sourceType);
                        if (stride <= 0)
                        {
                            throw Invalid(
                                "The F+S scalar storage address has no positive stride.");
                        }

                        storageFacts = new NativeStorageAddressFacts(
                            localVariableByHome[sourceHome],
                            new BoundConstant(0, PrimitiveType.Int32, default),
                            sourceType, sourceType, addressPointerType,
                            pointerDefinition.IsWritable, 1, (ulong)stride,
                            requiredElementCount);
                    }
                    else
                    {
                        var previousPlaceInstruction = instructionOrdinal == 0
                            ? default : instructions[instructionOrdinal - 1];
                        if (storageSourceIndex > 13
                            || previousPlaceInstruction.Opcode != 0xD8
                            || previousPlaceInstruction.Register
                                != storageSourceIndex
                            || !placeSelectionsByRegister.TryGetValue(
                                storageSourceIndex, out var selectedPlace)
                            || selectedPlace.RecordIndex
                                != previousPlaceInstruction.Index
                            || selectedPlace.AccessKind is not (1 or 2)
                            || selectedPlace.IndexKind != 2
                            || selectedPlace.BaseHome >= body.HomeCount
                            || localVariableByHome[
                                selectedPlace.BaseHome] < 0
                            || requiredElementCount
                                > selectedPlace.ElementCount)
                        {
                            throw Invalid(
                                "The F+S RegisterPlace address has no adjacent fixed-array selection.");
                        }

                        var pointerPointee = pointerDefinition.PointeeType;
                        if (pointerPointee != PrimitiveType.Void
                            && pointerPointee != selectedPlace.ElementType)
                        {
                            throw Invalid(
                                "The F+S pointer type differs from its selected array element.");
                        }

                        var indexVariable = formalOrdinalByHome[
                            selectedPlace.IndexHome] >= 0
                            ? formalOrdinalByHome[selectedPlace.IndexHome]
                            : localVariableByHome[selectedPlace.IndexHome];
                        if (indexVariable < 0
                            || !NativeFixedArrayContract.IsIndexType(
                                variables[indexVariable].Type))
                        {
                            throw Invalid(
                                "The F+S array selection index has no reconstructed variable.");
                        }

                        var arrayDefinition = nativeFixedArrayTypes[
                            NativeFixedArrayTypes.Index(
                                selectedPlace.ArrayType)];
                        if (arrayDefinition.ElementCount
                                != selectedPlace.ElementCount
                            || arrayDefinition.ElementStride == 0)
                        {
                            throw Invalid(
                                "The F+S selected array layout is invalid.");
                        }

                        storageFacts = new NativeStorageAddressFacts(
                            localVariableByHome[selectedPlace.BaseHome],
                            new BoundRead(indexVariable,
                                variables[indexVariable].Type, default),
                            selectedPlace.ArrayType,
                            selectedPlace.ElementType, addressPointerType,
                            pointerDefinition.IsWritable,
                            arrayDefinition.ElementCount,
                            arrayDefinition.ElementStride,
                            requiredElementCount);
                        _ = placeSelectionsByRegister.Remove(
                            storageSourceIndex);
                    }

                    storageAddressesByHome.Add(resultHome, storageFacts);
                    break;

                case 0xE0:
                    if (sawCallSite
                        || instruction.Index != callSiteRecordIndex
                        || localVariableByHome[targetHome] < 0)
                    {
                        throw Invalid("The F+S indirect CallSite is misplaced.");
                    }

                    var callSite = body.GetRecord(instruction.Index);
                    var inputCount = BinaryPrimitives.ReadUInt32LittleEndian(
                        callSite[12..]);
                    var preparationCount
                        = BinaryPrimitives.ReadUInt32LittleEndian(
                            callSite[16..]);
                    var callResultKind = callSite[5];
                    var callResultIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(callSite[10..]);
                    if (callSite[0] != 0x02 || callSite[4] != 3
                        || BinaryPrimitives.ReadUInt16LittleEndian(callSite[8..])
                            != targetHome
                        || inputCount != (uint)targetParameterTypes.Length
                        || preparationCount != 0
                        || (signature.ReturnType == PrimitiveType.Void
                            ? callResultKind != 0 || callResultIndex != 0
                            : callResultKind != 1 || callResultIndex != 0))
                    {
                        throw Invalid("The F+S CallSite does not match its signature.");
                    }

                    var callArguments = ImmutableArray.CreateBuilder<BoundExpression>(
                        targetParameterTypes.Length);
                    for (var ordinal = 0;
                        ordinal < targetParameterTypes.Length; ordinal++)
                    {
                        var inputOffset = 20 + (ordinal * 4);
                        var selectorKind = callSite[inputOffset];
                        var sourceUse = callSite[inputOffset + 1];
                        var selectorIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                callSite[(inputOffset + 2)..]);
                        if (selectorKind != 2 || sourceUse != 1
                            || selectorIndex >= body.HomeCount)
                        {
                            throw Unsupported(
                                "The F+S CallSite requires completed Home inputs.");
                        }

                        if (storageAddressesByHome.TryGetValue(selectorIndex,
                                out var addressFacts))
                        {
                            var nativeParameter = signature.NativeParameters
                                .FirstOrDefault(parameter =>
                                    parameter.ParameterIndex == ordinal);
                            if (nativeParameter is null
                                || addressFacts.PointerType
                                    != targetParameterTypes[ordinal]
                                || addressFacts.RequiredElementCount
                                    != (nativeParameter.Extent == 0
                                        ? 1u : nativeParameter.Extent)
                                || nativeParameter.Extent
                                    > addressFacts.ElementCount
                                || (!NativeFixedArrayTypes.IsType(
                                        addressFacts.CarrierType)
                                    && nativeParameter.Extent > 1)
                                || ((nativeParameter.Access
                                        is NativeParameterAccess.Write
                                            or NativeParameterAccess.ReadWrite)
                                    && !addressFacts.PointerIsWritable))
                            {
                                throw Invalid(
                                    "The F+S storage address does not satisfy its NativeParameter.");
                            }

                            if (addressFacts.ElementStride == 0)
                            {
                                throw Invalid(
                                    "The F+S source has no positive element stride.");
                            }

                            var byteExtent = checked((ulong)
                                addressFacts.RequiredElementCount
                                * addressFacts.ElementStride);
                            callArguments.Add(new BoundNativeStorageAddress(
                                addressFacts.SourceVariable,
                                addressFacts.Index,
                                addressFacts.CarrierType,
                                addressFacts.ElementType,
                                PrimitiveType.NativeStorageAddress,
                                addressFacts.ElementCount,
                                addressFacts.ElementStride, byteExtent,
                                nativeParameter.Access == NativeParameterAccess.Read,
                                default));
                            if (!consumedStorageHomes.Add(selectorIndex))
                            {
                                throw Invalid(
                                    "An F+S storage-address result is consumed more than once.");
                            }
                            continue;
                        }

                        var formalOrdinal = formalOrdinalByHome[selectorIndex];
                        if (formalOrdinal >= 0)
                        {
                            var formalType = callerParameterTypes[formalOrdinal];
                            if (formalType != targetParameterTypes[ordinal])
                            {
                                throw Invalid(
                                    "The F+S formal Home type differs from its target parameter.");
                            }
                            callArguments.Add(new BoundRead(formalOrdinal,
                                formalType, default));
                            continue;
                        }

                        var inputLocalVariable
                            = localVariableByHome[selectorIndex];
                        if (inputLocalVariable < 0
                            || variables[inputLocalVariable].Type
                                != targetParameterTypes[ordinal])
                        {
                            throw Invalid(
                                "The F+S CallSite input selects no compatible local or formal Home.");
                        }
                        callArguments.Add(new BoundRead(inputLocalVariable,
                            variables[inputLocalVariable].Type, default));
                    }

                    if (consumedStorageHomes.Count
                        != storageAddressesByHome.Count)
                    {
                        throw Invalid(
                            "Every F+S storage-address result must feed the CallSite.");
                    }

                    statements.Add(new BoundExpressionStatement(
                        new BoundIndirectCall(
                            new BoundRead(localVariableByHome[targetHome],
                                pointerType, default),
                            callArguments.MoveToImmutable(),
                            signature.ReturnType, default)
                        {
                            IsUnsafeAuthorized = true,
                        }, default));
                    sawCallSite = true;
                    break;

                case 0xB8:
                    if (sawReturn || instruction.Index != returnHome
                        || !sawCallSite
                        || (uint)returnHome >= (uint)body.HomeCount
                        || localVariableByHome[returnHome] < 0)
                    {
                        throw Invalid("The F+S ReturnHome is invalid.");
                    }

                    var returnVariable = localVariableByHome[returnHome];
                    if (variables[returnVariable].Type != callerResultType
                        || !storageAddressesByHome.Values.Any(facts =>
                            facts.SourceVariable == returnVariable))
                    {
                        throw Invalid(
                            "The F+S caller must return a fresh read of addressed storage.");
                    }
                    statements.Add(new BoundReturn(new BoundRead(
                        returnVariable, callerResultType, default), default));
                    sawReturn = true;
                    break;
            }

            instructionOrdinal++;
        }

        if (!sawCallSite || !sawReturn
            || placeSelectionsByRegister.Count != 0
            || arrayInitializationElementsByHome.Count != 0
            || storageAddressesByHome.Keys.Any(home =>
                !consumedStorageHomes.Contains(home)))
        {
            throw Invalid("The F+S caller Body is incomplete.");
        }

        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            if (body.HomeMap[homeIndex * 8] == 3
                && localVariableByHome[homeIndex] < 0
                && !storageAddressesByHome.ContainsKey(homeIndex))
            {
                throw Invalid("An F+S local Home has no initialization operation.");
            }
        }

        return (new BoundBlock([
                new BoundUnsafe(new BoundBlock(statements.ToImmutable(), default),
                    default),
            ], default),
            variables.ToImmutable());
    }

    private static NativeArrayPlaceSelectionFacts
        ReadForeignInvocationArrayPlaceSelection(
            TrRmRegisterBodyReader body, TrRmTypeSpecTableReader typeSpecs,
            TrRmTargetProfileTableReader targetProfiles,
            TrRmFixedArrayLayoutTableReader fixedArrayLayouts,
            IReadOnlyDictionary<int, PrimitiveType> arrayTypeByTypeSpecRow,
            ImmutableArray<BoundNativeFixedArrayType> nativeFixedArrayTypes,
            List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
            int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef,
            int ownerModule, int recordIndex, int destinationRegister)
    {
        var record = body.GetRecord(recordIndex);
        if (record[0] != 0x04 || record.Length != 24)
        {
            throw Invalid("The F+S place instruction selects no PlaceSelection.");
        }

        var payload = record[4..];
        var targetKind = payload[0];
        var accessKind = payload[1];
        var destinationKind = payload[2];
        var baseKind = payload[3];
        var selectedRegister = BinaryPrimitives.ReadUInt16LittleEndian(
            payload[4..]);
        var baseHome = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
        var indexKind = payload[8];
        var targetRefKind = payload[9];
        var indexIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload[10..]);
        var targetRef = BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]);
        var layoutRow = BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]);
        var referentTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(payload[16..]);
        if (destinationKind != 1
            || selectedRegister != destinationRegister
            || payload[17] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(payload[18..]) != 0)
        {
            throw Invalid("The F+S PlaceSelection shape is unsupported.");
        }

        if (targetKind == 1)
        {
            if (!body.Schema
                    .UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                || accessKind != 3 || baseKind != 0 || baseHome != 0
                || indexKind != 0 || targetRefKind != 0 || indexIndex != 0
                || targetRef >= (uint)body.HomeCount)
            {
                throw Invalid(
                    "The A2E Home PlaceSelection shape is unsupported.");
            }

            var homeIndex = checked((int)targetRef);
            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            var emptyArrayTypeSpecRef = ReadForeignInvocationBodyTypeSpec(body,
                BinaryPrimitives.ReadUInt16LittleEndian(home[2..]));
            var arrayTypeReference = typeSpecs.GetTypeSpecRef(
                emptyArrayTypeSpecRef);
            if (home[0] != 3 || !arrayTypeReference.IsRow
                || arrayTypeReference.Form != 0x11
                || !arrayTypeByTypeSpecRow.TryGetValue(
                    arrayTypeReference.Row, out var emptyArrayType)
                || !NativeFixedArrayTypes.IsType(emptyArrayType)
                || !typeSpecs.TryGetFixedInlineArray(
                    (uint)arrayTypeReference.Row,
                    out var emptyElementTypeSpecRef,
                    out var emptyElementCount)
                || emptyElementCount != 0
                || !targetProfiles.TryGetProfile(
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader
                        .WindowsX64NativeProfileRuleRevision,
                    out var profileRow))
            {
                throw Invalid(
                    "The A2E Home PlaceSelection does not name an empty local array.");
            }

            var emptyArrayLayoutRow = fixedArrayLayouts.FindRow(ownerModule,
                (uint)arrayTypeReference.Row, (uint)profileRow);
            if (emptyArrayLayoutRow == 0)
            {
                throw Invalid(
                    "The A2E Home PlaceSelection has no exact fixed-array layout.");
            }

            var emptyArrayLayout = fixedArrayLayouts.GetRow(
                emptyArrayLayoutRow);
            var emptyArrayDefinition = nativeFixedArrayTypes[
                NativeFixedArrayTypes.Index(emptyArrayType)];
            var emptyElementType = ReadForeignInvocationType(typeSpecs,
                emptyElementTypeSpecRef, nativePointerTypeDefinitions,
                signatureIndexByAnchorMethod, structureTypeByTypeDef,
                arrayTypeByTypeSpecRow: arrayTypeByTypeSpecRow);
            if (emptyArrayLayout.OwnerModuleRow != ownerModule
                || emptyArrayLayout.ArrayTypeSpecRow
                    != arrayTypeReference.Row
                || emptyArrayLayout.ByteExtent != 0
                || emptyArrayLayout.Alignment == 0
                || emptyArrayDefinition.ElementType != emptyElementType
                || emptyArrayDefinition.ElementCount != 0
                || emptyArrayDefinition.ElementStride
                    != emptyArrayLayout.ElementStride
                || emptyArrayDefinition.ByteExtent
                    != emptyArrayLayout.ByteExtent
                || emptyArrayDefinition.Alignment
                    != emptyArrayLayout.Alignment
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    ReadForeignInvocationBodyTypeSpec(body,
                        referentTypeMapIndex), emptyArrayTypeSpecRef))
            {
                throw Invalid(
                    "The A2E Home PlaceSelection does not match its empty-array layout.");
            }

            return new NativeArrayPlaceSelectionFacts(recordIndex,
                destinationRegister, targetKind, accessKind, homeIndex, 0,
                -1, -1, arrayTypeReference.Row, emptyArrayLayoutRow,
                emptyArrayType, emptyElementType, 0);
        }

        if (targetKind != 3 || accessKind is < 1 or > 3
            || baseKind != 2 || baseHome >= body.HomeCount
            || indexKind is not (1 or 2)
            || (indexKind == 1
                ? indexIndex > 13
                : indexIndex >= body.HomeCount)
            || targetRefKind != 3 || layoutRow == 0
            || layoutRow > (uint)fixedArrayLayouts.Count)
        {
            throw Invalid("The F+S PlaceSelection shape is unsupported.");
        }

        var fixedArrayLayout = fixedArrayLayouts.GetRow(
            checked((int)layoutRow));
        if (fixedArrayLayout.OwnerModuleRow != ownerModule
            || !arrayTypeByTypeSpecRow.TryGetValue(
                fixedArrayLayout.ArrayTypeSpecRow, out var arrayType)
            || !typeSpecs.TryGetFixedInlineArray(
                (uint)fixedArrayLayout.ArrayTypeSpecRow,
                out var elementTypeSpecRef, out var elementCount))
        {
            throw Invalid("The F+S PlaceSelection has no owned array type.");
        }

        var baseHomeMap = body.HomeMap.Slice(baseHome * 8, 8);
        var arrayHomeType = ReadForeignInvocationType(typeSpecs,
            ReadForeignInvocationBodyTypeSpec(body,
                BinaryPrimitives.ReadUInt16LittleEndian(baseHomeMap[2..])),
            nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
            structureTypeByTypeDef,
            arrayTypeByTypeSpecRow: arrayTypeByTypeSpecRow);
        if (baseHomeMap[0] != 3 || arrayHomeType != arrayType
            || !NativeFixedArrayTypes.IsType(arrayType))
        {
            throw Invalid("The F+S PlaceSelection base Home is invalid.");
        }

        var arrayTypeIndex = NativeFixedArrayTypes.Index(arrayType);
        var indexHome = indexKind == 2 ? (int)indexIndex : -1;
        var indexRegister = indexKind == 1 ? (int)indexIndex : -1;
        if ((uint)arrayTypeIndex >= (uint)nativeFixedArrayTypes.Length)
        {
            throw Invalid("The F+S PlaceSelection array type is invalid.");
        }

        if (indexKind == 2)
        {
            var indexHomeMap = body.HomeMap.Slice(indexHome * 8, 8);
            var indexType = ReadForeignInvocationType(typeSpecs,
                ReadForeignInvocationBodyTypeSpec(body,
                    BinaryPrimitives.ReadUInt16LittleEndian(indexHomeMap[2..])),
                nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                structureTypeByTypeDef,
                arrayTypeByTypeSpecRow: arrayTypeByTypeSpecRow);
            if (indexHomeMap[0] is not (2 or 3)
                || !NativeFixedArrayContract.IsIndexType(indexType))
            {
                throw Invalid(
                    "The F+S PlaceSelection index Home is invalid.");
            }
        }

        var arrayDefinition = nativeFixedArrayTypes[arrayTypeIndex];
        var elementType = ReadForeignInvocationType(typeSpecs,
            elementTypeSpecRef, nativePointerTypeDefinitions,
            signatureIndexByAnchorMethod, structureTypeByTypeDef,
            arrayTypeByTypeSpecRow: arrayTypeByTypeSpecRow);
        if (arrayDefinition.ElementType != elementType
            || arrayDefinition.ElementCount != elementCount
            || arrayDefinition.ElementStride
                != fixedArrayLayout.ElementStride
            || arrayDefinition.ByteExtent != fixedArrayLayout.ByteExtent
            || arrayDefinition.Alignment != fixedArrayLayout.Alignment
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                ReadForeignInvocationBodyTypeSpec(body,
                    referentTypeMapIndex), elementTypeSpecRef))
        {
            throw Invalid("The F+S PlaceSelection does not match its A2 layout.");
        }

        return new NativeArrayPlaceSelectionFacts(recordIndex,
            destinationRegister, targetKind, accessKind, baseHome, indexKind,
            indexHome, indexRegister, fixedArrayLayout.ArrayTypeSpecRow,
            checked((int)layoutRow), arrayType, elementType, elementCount);
    }

    private static ulong ReadArrayInitializationElementIndex(
        NativeArrayPlaceSelectionFacts selection,
        TrRmRegisterBodyReader body,
        Dictionary<int, NativeAddressRegisterConstant> registerConstants,
        Dictionary<int, long> localConstantByHome)
    {
        if (selection.IndexKind == 1)
        {
            if (!registerConstants.TryGetValue(selection.IndexRegister,
                    out var registerIndex)
                || registerIndex.Type != PrimitiveType.Int32
                || registerIndex.Bits > int.MaxValue)
            {
                throw Invalid(
                    "The F+S initializer index register is invalid.");
            }

            return registerIndex.Bits;
        }

        if (selection.IndexKind == 2
            && (uint)selection.IndexHome < (uint)body.HomeCount
            && localConstantByHome.TryGetValue(selection.IndexHome,
                out var homeIndex)
            && homeIndex >= 0)
        {
            return checked((ulong)homeIndex);
        }

        throw Invalid("The F+S initializer has an unsupported index selector.");
    }

    private static int GetArrayInitializationElementCount(int home,
        Dictionary<int, List<BoundExpression>> elementsByHome)
        => elementsByHome.TryGetValue(home, out var elements)
            ? elements.Count : 0;

    private static void AddInitializedArrayLocal(PrimitiveType arrayType,
        int home, ulong elementCount, List<BoundExpression> elements,
        TrRmRegisterBodyReader body,
        ImmutableArray<BoundNativeFixedArrayType> nativeFixedArrayTypes,
        int[] localVariableByHome,         ImmutableArray<BoundVariable>.Builder variables,
        ImmutableArray<BoundStatement>.Builder statements)
    {
        if (!NativeFixedArrayTypes.IsType(arrayType)
            || (uint)home >= (uint)body.HomeCount
            || body.HomeMap[home * 8] != 3
            || localVariableByHome[home] != -1)
        {
            throw Invalid("The F+S array Home cannot be initialized.");
        }

        var arrayIndex = NativeFixedArrayTypes.Index(arrayType);
        if ((uint)arrayIndex >= (uint)nativeFixedArrayTypes.Length)
        {
            throw Invalid("The F+S array initialization type is invalid.");
        }

        var layout = nativeFixedArrayTypes[arrayIndex];
        if (layout.ElementCount != elementCount
            || (ulong)elements.Count != elementCount)
        {
            throw Invalid(
                "The F+S array initialization does not provide every element.");
        }

        var variable = variables.Count;
        localVariableByHome[home] = variable;
        variables.Add(new BoundVariable($"__trrm_home_{home}", arrayType,
            body.HomeMap[(home * 8) + 1] != 0, default));
        statements.Add(new BoundLocal(variable,
            new BoundNativeFixedArrayInitialization(
                new BoundNativeFixedArrayLayout(arrayType,
                    layout.ElementType, layout.ElementCount,
                    layout.ElementStride, layout.ByteExtent,
                    layout.Alignment, layout.RequiredAlignment),
                [.. elements], default),
            default));
    }

    private static bool TryGetContextualConstant(byte sourceRegister,
        PrimitiveType type, out long value)
    {
        value = 0;
        if (sourceRegister == 14)
        {
            return DirectNativeImportContract.IsScalar(type);
        }

        if (sourceRegister != 15 || !DirectNativeImportContract.IsScalar(type))
        {
            return false;
        }

        var width = PrimitiveTypeFacts.ScalarWidth(type);
        if (width is not (1 or 2 or 4 or 8))
        {
            return false;
        }
        var allBitsSet = width == sizeof(ulong)
            ? ulong.MaxValue
            : (1UL << (width * 8)) - 1;
        switch (type)
        {
            case PrimitiveType.Bool:
                value = 1;
                return true;
            case PrimitiveType.Int8:
            case PrimitiveType.Int16:
            case PrimitiveType.Int32:
            case PrimitiveType.Int64:
                value = -1;
                return true;
            case PrimitiveType.UInt8:
            case PrimitiveType.UInt16:
            case PrimitiveType.UInt32:
                value = checked((long)allBitsSet);
                return true;
            case PrimitiveType.UInt64:
            case PrimitiveType.Float32:
            case PrimitiveType.Float64:
                value = unchecked((long)allBitsSet);
                return true;
            default:
                return false;
        }
    }

    private static BoundCall ReadForeignInvocationStagedCall(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> record,
        TrRmImportTableReader imports,
        int targetMethodRow, int targetIndex,
        ImmutableArray<PrimitiveType> callerParameterTypes,
        ImmutableArray<PrimitiveType> targetParameterTypes,
        NativeFunctionPointerSignatureDefinition signature,
        int[] formalOrdinalByHome, byte expectedResultKind,
        ushort expectedResultIndex)
    {
        var inputCount = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        var preparationCount = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
        if (record[0] != 0x02 || record[4] != 1
            || record[5] != expectedResultKind
            || BinaryPrimitives.ReadUInt16LittleEndian(record[10..])
                != expectedResultIndex
            || inputCount != (uint)targetParameterTypes.Length
            || preparationCount != 0
            || (long)record.Length != 20L + (inputCount * 4L)
            || ReadForeignInvocationBodyMethodDef(body, imports,
                BinaryPrimitives.ReadUInt16LittleEndian(record[8..]))
                != targetMethodRow
            || !DirectNativeImportContract.IsScalar(signature.ReturnType))
        {
            throw Invalid("The staged direct CallSite does not match the selected import.");
        }

        var arguments = ImmutableArray.CreateBuilder<BoundExpression>(
            targetParameterTypes.Length);
        for (var ordinal = 0; ordinal < targetParameterTypes.Length; ordinal++)
        {
            var inputOffset = 20 + (ordinal * 4);
            if (record[inputOffset] != 2 || record[inputOffset + 1] != 1)
            {
                throw Unsupported("Staged direct calls support formal-Home inputs only.");
            }

            var homeIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                record[(inputOffset + 2)..]);
            if (homeIndex >= formalOrdinalByHome.Length
                || formalOrdinalByHome[homeIndex] < 0)
            {
                throw Invalid("A staged direct CallSite input is not a formal Home.");
            }

            var formalOrdinal = formalOrdinalByHome[homeIndex];
            var type = callerParameterTypes[formalOrdinal];
            if (type != targetParameterTypes[ordinal])
            {
                throw Invalid("A staged direct CallSite input type differs from its formal.");
            }

            arguments.Add(new BoundRead(formalOrdinal, type, default));
        }

        return new BoundCall(targetIndex, arguments.MoveToImmutable(),
            signature.ReturnType, default)
        {
            IsUnsafeAuthorized = true,
        };
    }

    private static ImmutableArray<PrimitiveType> ReadForeignInvocationParameterTypes(
        int methodRow, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef)
    {
        var signatureRow = methods.GetSignature(methodRow);
        var (first, count) = signatures.GetParamRange(signatureRow);
        var result = ImmutableArray.CreateBuilder<PrimitiveType>(count);
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var parameterRow = first + ordinal;
            var type = ReadForeignInvocationType(typeSpecs,
                parameters.GetTypeSpec(parameterRow),
                nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
            if (parameters.GetMode(parameterRow) != 0
                || !IsForeignInvocationValueType(type, allowVoid: false))
            {
                throw Unsupported("The source-free loader supports scalar and typed-pointer parameters.");
            }

            result.Add(type);
        }

        return result.MoveToImmutable();
    }

    private static ImmutableArray<DirectNativeParameter>
        ReadForeignInvocationNativeParameters(int methodRow,
            TrRmMethodDefTableReader methods,
            TrRmMethodSigTableReader signatures,
            TrRmParamTableReader parameters,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmContractTableReader contracts,
            TrRmLayoutTableReader layouts,
            List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
            int[] structureTypeByTypeDef)
    {
        var nativeParameters = contracts.GetNativeParameters(methodRow);
        var result = ImmutableArray.CreateBuilder<DirectNativeParameter>(
            nativeParameters.Length);
        var (first, parameterCount) = signatures.GetParamRange(
            methods.GetSignature(methodRow));
        foreach (var nativeParameter in nativeParameters)
        {
            if (nativeParameter.ParameterOrdinal >= (uint)parameterCount
                || nativeParameter.Access is < 1 or > 3
                || nativeParameter.Retention != 1
                || nativeParameter.ExtentKind is not (0 or 1)
                || nativeParameter.PointerDepth != 1
                || nativeParameter.RequiredAlignment != 0)
            {
                throw Invalid("Invalid NativeParameter contract.");
            }

            var parameterRow = first
                + checked((int)nativeParameter.ParameterOrdinal);
            var typeSpecRef = parameters.GetTypeSpec(parameterRow);
            var formalTypeSpec = typeSpecs.GetTypeSpecBytes(typeSpecRef);
            if (formalTypeSpec.Length == 7 && formalTypeSpec[0] == 0x0D)
            {
                throw Unsupported("Nested function-pointer NativeParameters are unsupported.");
            }

            var pointeeType = NativeParameterPointee(typeSpecs, typeSpecRef,
                nativeParameter.Access, nativePointerTypeDefinitions,
                layouts, structureTypeByTypeDef);
            result.Add(new DirectNativeParameter(
                checked((int)nativeParameter.ParameterOrdinal), pointeeType,
                (NativeParameterAccess)(nativeParameter.Access - 1),
                NativeParameterRetention.NoEscape, nativeParameter.Extent));
        }

        return result.MoveToImmutable();
    }

    private static PrimitiveType ReadForeignInvocationType(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef,
        TrRmImportTableReader imports = default,
        IReadOnlyDictionary<int, PrimitiveType>? arrayTypeByTypeSpecRow = null)
    {
        var reference = typeSpecs.GetTypeSpecRef(typeSpecRef);
        if (reference.IsRow && reference.Form == 0x11
            && arrayTypeByTypeSpecRow is not null
            && arrayTypeByTypeSpecRow.TryGetValue(reference.Row,
                out var fixedArrayType))
        {
            return fixedArrayType;
        }

        var type = typeSpecs.GetTypeSpecBytes(typeSpecRef);
        if (type.Length == 7 && type[0] == 0x0D)
        {
            var anchorMethodRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(type[3..]));
            if (type[1] != 0
                || anchorMethodRow >= signatureIndexByAnchorMethod.Length
                || signatureIndexByAnchorMethod[anchorMethodRow] < 0)
            {
                throw Invalid("Invalid native function-pointer TypeSpec anchor.");
            }

            return NativeFunctionPointerTypes.Type(
                signatureIndexByAnchorMethod[anchorMethodRow]);
        }

        return FromNativeValueRow(typeSpecs, typeSpecRef,
            nativePointerTypeDefinitions, structureTypeByTypeDef, imports);
    }

    private static BoundProgram LoadForeignInvocationFixedArrayTypeLibrary(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int selectedModuleRow, bool verifySemantics,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings)
    {
        var (typeDefStart, typeDefEnd)
            = tables.Modules.GetTypeDefRange(selectedModuleRow);
        var (moduleRefStart, moduleRefEnd)
            = tables.Modules.GetModuleRefRange(selectedModuleRow);
        if (tables.Schema is not (
                TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft)
            || selectedModuleRow <= 0
            || selectedModuleRow > tables.Modules.Count
            || typeDefStart <= 0 || typeDefEnd <= typeDefStart
            || typeDefEnd - typeDefStart > tables.TypeDefs.Count
            || tables.Modules.GetRequirements(selectedModuleRow) != (1u, 0u)
            || tables.Modules.GetEntryMethod(selectedModuleRow) != 0
            || tables.TypeDefs.Count == 0 || tables.Fields.Count == 0
            || tables.Methods.Count != 0 || tables.Bodies.Count != 0
            || tables.Signatures.Count != 0
            || tables.Contracts.Count != 0 || tables.GenericParams.Count != 0
            || tables.Constraints.Count != 0 || tables.EnumDefs.Count != 0
            || tables.EnumMembers.Count != 0 || tables.Interfaces.Count != 0
            || tables.NativeFunctionPointerSignatures.Count != 0
            || tables.NativeCallables.Count != 0
            || tables.NativeVTables.Count != 0
            || tables.NativeExports.Count != 0
            || tables.StandardNativeImports.Count != 0)
        {
            throw Unsupported(
                "The bounded A2 type-library loader supports one local Copy declaration Module without executable or provider records.");
        }

        var bindingsByImportRow
            = new Dictionary<int, TrRmStandardTypeProviderBinding>();
        if (!typeProviderBindings.IsDefaultOrEmpty)
        {
            foreach (var binding in typeProviderBindings)
            {
                if (binding.ConsumerArtifactIndex != 0
                    || binding.ConsumerModuleRow != selectedModuleRow
                    || !bindingsByImportRow.TryAdd(binding.ImportRow, binding))
                {
                    if (binding.ConsumerArtifactIndex == 0
                        && binding.ConsumerModuleRow == selectedModuleRow)
                    {
                        throw Invalid(
                            "An A2 TypeDef Import has multiple provider bindings.");
                    }
                }
            }
        }

        var usedModuleRefs = new bool[tables.ModuleRefs.Count + 1];
        var typeDefImports = new HashSet<int>();
        for (var importRow = 1; importRow <= tables.Imports.Count;
            importRow++)
        {
            var moduleRefRow
                = tables.Imports.GetModuleRefForImportRow(importRow);
            if (tables.ModuleRefs.GetOwnerModule(moduleRefRow)
                != selectedModuleRow)
            {
                continue;
            }

            var expectedDefinition = tables.Imports
                .GetExpectedDefinitionForImportRow(importRow);
            if (expectedDefinition >> 24 != 0x13
                || !bindingsByImportRow.TryGetValue(importRow,
                    out var binding)
                || binding.ExpectedTypeDefRow != checked((int)(
                    expectedDefinition & 0x00FF_FFFF)))
            {
                throw Unsupported(
                    "The A2 type-library loader requires a checked provider binding for every TypeDef Import.");
            }

            _ = typeDefImports.Add(importRow);
            usedModuleRefs[moduleRefRow] = true;
        }

        for (var moduleRefRow = moduleRefStart;
            moduleRefRow < moduleRefEnd; moduleRefRow++)
        {
            if (!usedModuleRefs[moduleRefRow])
            {
                throw Invalid(
                    "The A2 type Module has an unconsumed ModuleRef.");
            }
        }
        if (typeDefImports.Count != bindingsByImportRow.Count)
        {
            throw Invalid(
                "A2 provider bindings do not belong to the selected type Module.");
        }

        if (!tables.TargetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision,
                out var profileRow))
        {
            throw Invalid("The A2 type library has no Windows x64 TargetProfile.");
        }

        var selectedTypeDefRows = new HashSet<int>();
        for (var typeDefRow = typeDefStart;
            typeDefRow < typeDefEnd; typeDefRow++)
        {
            _ = selectedTypeDefRows.Add(typeDefRow);
            var (genericFirst, genericCount)
                = tables.TypeDefs.GetGenericRange(typeDefRow);
            var (layoutFirst, layoutCount)
                = tables.TypeDefs.GetLayoutRange(typeDefRow);
            var layout = tables.Layouts.GetLayoutForProfile(typeDefRow,
                (uint)profileRow);
            if (tables.TypeDefs.GetOwnerModule(typeDefRow)
                    != selectedModuleRow
                || tables.TypeDefs.GetKind(typeDefRow) != 1
                || !tables.TypeDefs.HasDeclaredCopy(typeDefRow)
                || genericFirst != 0 || genericCount != 0
                || layoutCount != 2
                || !tables.Layouts.HasBlittableLayout(typeDefRow,
                    (uint)profileRow, 1)
                || tables.Layouts.GetFlags(layout) != 1
                || tables.Layouts.GetLayoutKind(layout) is not (1 or 3))
            {
                throw Unsupported(
                    "The bounded A2 type-library loader requires local nongeneric Copy structs with supported Blittable layouts.");
            }
        }

        var nativePointerTypeDefinitions
            = new List<NativePointerTypeDefinition>();
        var signatureIndexByAnchorMethod = new int[tables.Methods.Count + 1];
        Array.Fill(signatureIndexByAnchorMethod, -1);
        var fixedArrayTypes = new List<BoundNativeFixedArrayType>();
        var arrayTypeByTypeSpecRow = new Dictionary<int, PrimitiveType>();
        var materializedTypeDefRows = new HashSet<int>();
        var structures = LoadBlittableStructures(tables.TypeDefs,
            tables.Fields, tables.Layouts, tables.LayoutFields,
            tables.TargetProfiles, tables.TypeSpecs, [],
            nativePointerTypeDefinitions, out var structureTypeByTypeDef,
            fixedArrayTypes, arrayTypeByTypeSpecRow, materializedTypeDefRows,
            imports: tables.Imports,
            includedTypeDefRows: selectedTypeDefRows,
            allowDuplicateImportedViews: true);
        var materializedTypeDefs = new bool[tables.TypeDefs.Count + 1];
        var pendingArrayLayouts = new List<TrRmFixedArrayLayoutRow>();
        for (var row = 1; row <= tables.FixedArrayLayouts.Count; row++)
        {
            var layout = tables.FixedArrayLayouts.GetRow(row);
            if (layout.OwnerModuleRow == selectedModuleRow
                && layout.TargetProfileRef == (uint)profileRow)
            {
                pendingArrayLayouts.Add(layout);
            }
        }

        pendingArrayLayouts.Sort(static (left, right)
            => left.ArrayTypeSpecRow.CompareTo(right.ArrayTypeSpecRow));
        while (pendingArrayLayouts.Count != 0
            || materializedTypeDefRows.Count != selectedTypeDefRows.Count)
        {
            var madeProgress = false;
            for (var index = 0; index < pendingArrayLayouts.Count; index++)
            {
                if (!TryAddForeignInvocationFixedArrayType(
                        tables, pendingArrayLayouts[index],
                        nativePointerTypeDefinitions,
                        signatureIndexByAnchorMethod,
                        structureTypeByTypeDef, materializedTypeDefs,
                        structures, fixedArrayTypes,
                        arrayTypeByTypeSpecRow, tables.Imports))
                {
                    continue;
                }

                pendingArrayLayouts.RemoveAt(index);
                index--;
                madeProgress = true;
            }

            var newlyMaterializedRows = new List<int>();
            for (var typeDefRow = typeDefStart;
                typeDefRow < typeDefEnd; typeDefRow++)
            {
                if (!materializedTypeDefs[typeDefRow]
                    && ForeignInvocationA2TypeDefDependenciesAreReady(
                        tables, typeDefRow, (uint)profileRow,
                        materializedTypeDefs,
                        arrayTypeByTypeSpecRow, tables.Imports))
                {
                    newlyMaterializedRows.Add(typeDefRow);
                }
            }

            if (newlyMaterializedRows.Count != 0)
            {
                foreach (var typeDefRow in newlyMaterializedRows)
                {
                    _ = materializedTypeDefRows.Add(typeDefRow);
                }

                structures = LoadBlittableStructures(tables.TypeDefs,
                    tables.Fields, tables.Layouts, tables.LayoutFields,
                    tables.TargetProfiles, tables.TypeSpecs, [],
                    nativePointerTypeDefinitions, out var updatedTypeMap,
                    fixedArrayTypes, arrayTypeByTypeSpecRow,
                    materializedTypeDefRows, structures, tables.Imports,
                    selectedTypeDefRows,
                    allowDuplicateImportedViews: true);
                if (!structureTypeByTypeDef.SequenceEqual(updatedTypeMap))
                {
                    throw Invalid(
                        "A2 local TypeDef identities changed while materializing array dependencies.");
                }

                for (var row = 0; row < newlyMaterializedRows.Count; row++)
                {
                    materializedTypeDefs[newlyMaterializedRows[row]] = true;
                }

                madeProgress = true;
            }

            if (!madeProgress)
            {
                throw Invalid(
                    "The A2 type library contains cyclic or unresolved inline TypeDef/array dependencies.");
            }
        }

        var nativeFixedArrayTypes = fixedArrayTypes.ToImmutableArray();
        var nativePointerTypes = nativePointerTypeDefinitions.ToImmutableArray();
        if (!NativePointerTypeContract.IsValidTable(nativePointerTypes,
                structures, [], nativeFixedArrayTypes))
        {
            throw Invalid("Invalid A2 native pointer type table.");
        }

        var program = new BoundProgram([], -1)
        {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            Structures = structures,
            NativePointerTypes = nativePointerTypes,
            NativeFixedArrayTypes = nativeFixedArrayTypes,
        };
        if (!NativeFixedArrayContract.MatchesProgram(program))
        {
            throw Invalid("Invalid reconstructed A2 fixed-array type table.");
        }
        if (verifySemantics)
        {
            var duplicateNameViewIndexes = new HashSet<int>();
            foreach (var typeDefRow in materializedTypeDefRows)
            {
                if (tables.Imports.IsExpectedDefinition(
                        0x1300_0000u | checked((uint)typeDefRow)))
                {
                    _ = duplicateNameViewIndexes.Add(
                        structureTypeByTypeDef[typeDefRow]);
                }
            }

            SemanticVerifier.VerifyLibraryWithDirectNativeImports(program,
                duplicateNameViewIndexes: duplicateNameViewIndexes);
        }

        return program;
    }

    private static ImmutableArray<BoundNativeFixedArrayType>
        ReadForeignInvocationFixedArrayTypes(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int selectedModuleRow,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef,
        IReadOnlyList<BoundStruct> structures,
        out Dictionary<int, PrimitiveType> arrayTypeByTypeSpecRow)
    {
        arrayTypeByTypeSpecRow = [];
        if (tables.FixedArrayLayouts.Count == 0)
        {
            return [];
        }

        if (!tables.TargetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision,
                out var profileRow))
        {
            throw Invalid("The selected module has no Windows x64 array layout.");
        }

        var pendingLayouts = new List<TrRmFixedArrayLayoutRow>();
        for (var row = 1; row <= tables.FixedArrayLayouts.Count; row++)
        {
            var layout = tables.FixedArrayLayouts.GetRow(row);
            if (layout.OwnerModuleRow == selectedModuleRow
                && layout.TargetProfileRef == (uint)profileRow)
            {
                pendingLayouts.Add(layout);
            }
        }

        pendingLayouts.Sort(static (left, right)
            => left.ArrayTypeSpecRow.CompareTo(right.ArrayTypeSpecRow));
        if (pendingLayouts.Count == 0)
        {
            return [];
        }

        var fixedArrayTypes = new List<BoundNativeFixedArrayType>(
            pendingLayouts.Count);
        while (pendingLayouts.Count != 0)
        {
            var madeProgress = false;
            for (var index = 0; index < pendingLayouts.Count; index++)
            {
                if (!TryAddForeignInvocationFixedArrayType(
                        tables, pendingLayouts[index],
                        nativePointerTypeDefinitions,
                        signatureIndexByAnchorMethod,
                        structureTypeByTypeDef, null, structures,
                        fixedArrayTypes, arrayTypeByTypeSpecRow))
                {
                    continue;
                }

                pendingLayouts.RemoveAt(index);
                index--;
                madeProgress = true;
            }

            if (!madeProgress)
            {
                throw Invalid("Fixed-array TypeSpec dependencies are cyclic.");
            }
        }

        return [.. fixedArrayTypes];
    }

    private static bool TryAddForeignInvocationFixedArrayType(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        TrRmFixedArrayLayoutRow layoutRow,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef,
        bool[]? materializedTypeDefs, IReadOnlyList<BoundStruct> structures,
        List<BoundNativeFixedArrayType> fixedArrayTypes,
        Dictionary<int, PrimitiveType> arrayTypeByTypeSpecRow,
        TrRmImportTableReader imports = default)
    {
        if (!tables.TypeSpecs.TryGetFixedInlineArray(
                (uint)layoutRow.ArrayTypeSpecRow,
                out var elementTypeSpecRef, out var elementCount))
        {
            throw Invalid("A FixedArrayLayout row has no array TypeSpec.");
        }

        var elementTypeReference = tables.TypeSpecs.GetTypeSpecRef(
            elementTypeSpecRef);
        PrimitiveType elementType;
        if (elementTypeReference.IsRow
            && elementTypeReference.Form == 0x11)
        {
            if (!arrayTypeByTypeSpecRow.TryGetValue(
                    elementTypeReference.Row, out elementType))
            {
                return false;
            }
        }
        else
        {
            if (TryGetForeignInvocationTypeDefRow(tables.TypeSpecs,
                    elementTypeSpecRef, imports,
                    out var elementTypeDefRow))
            {
                if ((uint)elementTypeDefRow
                        >= (uint)structureTypeByTypeDef.Length
                    || structureTypeByTypeDef[elementTypeDefRow] < 0)
                {
                    throw Unsupported(
                        "The array element TypeDef is not a represented local struct.");
                }
                if (materializedTypeDefs is not null
                    && !materializedTypeDefs[elementTypeDefRow])
                {
                    return false;
                }
            }
            else if (elementTypeReference.IsRow
                && elementTypeReference.Form == 0x05)
            {
                throw Unsupported(
                    "The array element uses an unsupported nominal TypeSpec form.");
            }

            elementType = ReadForeignInvocationType(tables.TypeSpecs,
                elementTypeSpecRef, nativePointerTypeDefinitions,
                signatureIndexByAnchorMethod, structureTypeByTypeDef,
                imports);
        }

        if (!NativeFixedArrayContract.TryCreateType(elementType,
                elementCount, structures, fixedArrayTypes, out var arrayType,
                out var computedLayout))
        {
            throw Unsupported(
                "A fixed-array element type is outside the bounded source-free subset.");
        }

        if (computedLayout.ElementStride != layoutRow.ElementStride
            || computedLayout.ByteExtent != layoutRow.ByteExtent
            || computedLayout.Alignment != layoutRow.Alignment)
        {
            throw Invalid(
                "A FixedArrayLayout row differs from the reconstructed array geometry.");
        }

        if (!arrayTypeByTypeSpecRow.TryAdd(
                layoutRow.ArrayTypeSpecRow, arrayType))
        {
            throw Invalid("A fixed-array TypeSpec has duplicate layout rows.");
        }

        return true;
    }

    private static bool ForeignInvocationA2TypeDefDependenciesAreReady(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int typeDefRow, uint profileRow, bool[] materializedTypeDefs,
        Dictionary<int, PrimitiveType> arrayTypeByTypeSpecRow,
        TrRmImportTableReader imports)
    {
        var layoutRow = tables.Layouts.GetLayoutForProfile(typeDefRow,
            profileRow);
        var (first, count) = tables.Layouts.GetLayoutFieldRange(layoutRow);
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var fieldRow = tables.LayoutFields.GetFieldDef(first + ordinal);
            var typeSpecRef = tables.Fields.GetTypeSpecRef(fieldRow);
            var typeReference = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
            if (typeReference.IsRow && typeReference.Form == 0x11)
            {
                if (!arrayTypeByTypeSpecRow.ContainsKey(typeReference.Row))
                {
                    return false;
                }
            }
            else if (TryGetForeignInvocationTypeDefRow(tables.TypeSpecs,
                         typeSpecRef, imports, out var fieldTypeDefRow)
                && !materializedTypeDefs[fieldTypeDefRow])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetForeignInvocationTypeDefRow(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef,
        TrRmImportTableReader imports,
        out int typeDefRow)
    {
        var type = typeSpecs.GetTypeSpecBytes(typeSpecRef);
        if (type.Length == 6 && type[0] == 0x02 && type[1] == 0)
        {
            typeDefRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(type[2..]));
            return true;
        }

        if (type.Length == 6 && type[0] == 0x03 && type[1] == 0)
        {
            if (imports.Count == 0)
            {
                typeDefRow = 0;
                return false;
            }

            var importRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(type[2..]));
            var definition
                = imports.GetExpectedDefinitionForImportRow(importRow);
            if (definition >> 24 == 0x13)
            {
                typeDefRow = checked((int)(definition & 0x00FF_FFFF));
                return true;
            }
        }

        typeDefRow = 0;
        return false;
    }

    private static bool IsForeignInvocationValueType(PrimitiveType type,
        bool allowVoid)
        => (allowVoid && type == PrimitiveType.Void)
            || DirectNativeImportContract.IsScalar(type)
            || NativePointerTypes.IsType(type);

    private static ImmutableArray<BoundVariable> CreateVariables(
        ImmutableArray<PrimitiveType> parameterTypes, string prefix)
    {
        var variables = ImmutableArray.CreateBuilder<BoundVariable>(
            parameterTypes.Length);
        for (var ordinal = 0; ordinal < parameterTypes.Length; ordinal++)
        {
            variables.Add(new BoundVariable($"{prefix}{ordinal}",
                parameterTypes[ordinal], false, default));
        }

        return variables.MoveToImmutable();
    }

    private static void EnsureNativeAddressContracts(
        TrRmMethodDefTableReader methods, TrRmContractTableReader contracts,
        int methodRow, int expectedCount, bool allowEffects)
    {
        var (first, count) = methods.GetContractRange(methodRow);
        if (count != expectedCount)
        {
            throw Unsupported("Unsupported contracts on a NativeAddress method.");
        }

        for (var index = 0; index < count; index++)
        {
            var clause = contracts.GetRow(first + index);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(clause[4..]);
            if (kind != 0x000E && !(allowEffects && kind == 0x0001))
            {
                throw Unsupported("Unsupported contracts on a NativeAddress method.");
            }
        }
    }

    private static uint ReadForeignInvocationBodyTypeSpec(
        TrRmRegisterBodyReader body, ushort typeMapIndex)
    {
        var offset = typeMapIndex * sizeof(uint);
        var token = BinaryPrimitives.ReadUInt32LittleEndian(
            body.TypeMap[offset..]);
        if (token >> 24 != 0x11 || (token & 0x00FF_FFFF) == 0)
        {
            throw Unsupported("The bounded loader requires TypeSpec Body map tokens.");
        }

        return token & 0x00FF_FFFF;
    }

    private static int ReadForeignInvocationBodyMethodDef(
        TrRmRegisterBodyReader body, TrRmImportTableReader imports,
        ushort methodMapIndex)
    {
        var offset = methodMapIndex * sizeof(uint);
        var token = BinaryPrimitives.ReadUInt32LittleEndian(
            body.MethodMap[offset..]);
        var kind = token >> 24;
        var row = token & 0x00FF_FFFF;
        if (row == 0)
        {
            throw Invalid("A method map token has no row.");
        }

        if (kind == 0x16)
        {
            return checked((int)row);
        }
        if (kind == 0x1C)
        {
            var expectedDefinition = imports.GetExpectedDefinitionForImportRow(
                checked((int)row));
            if (expectedDefinition >> 24 != 0x16)
            {
                throw Invalid("A NativeAddress Import must expect a MethodDef.");
            }

            return checked((int)(expectedDefinition & 0x00FF_FFFF));
        }

        throw Unsupported("The bounded loader requires MethodDef or Import Body map tokens.");
    }

    private static ushort GetTypeMapIndexForType(
        TrRmRegisterBodyReader body, PrimitiveType type,
        TrRmTypeSpecTableReader typeSpecs,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef)
    {
        var result = ushort.MaxValue;
        for (var index = 0; index < body.TypeMap.Length / sizeof(uint); index++)
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap[(index * sizeof(uint))..]);
            if (token >> 24 != 0x11)
            {
                continue;
            }

            var typeSpecRef = token & 0x00FF_FFFF;
            if (ReadForeignInvocationType(typeSpecs, typeSpecRef,
                    nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                    structureTypeByTypeDef) == type)
            {
                result = checked((ushort)index);
                break;
            }
        }

        return result;
    }
}
