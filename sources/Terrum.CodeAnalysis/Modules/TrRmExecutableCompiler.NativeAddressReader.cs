// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    internal static BoundProgram LoadForeignInvocationDraftNativeAddressLibrary(
        ReadOnlySpan<byte> artifact)
        => LoadForeignInvocationDraftNativeAddressLibrary(artifact, 1, null,
            verifySemantics: true);

    private static BoundProgram LoadForeignInvocationDraftNativeAddressLibrary(
        ReadOnlySpan<byte> artifact, BoundModuleImport? selectedModuleImport,
        bool verifySemantics)
        => LoadForeignInvocationDraftNativeAddressLibrary(artifact, 1,
            selectedModuleImport, verifySemantics);

    private static BoundProgram LoadForeignInvocationDraftNativeAddressLibrary(
        ReadOnlySpan<byte> artifact, int selectedModuleRow,
        BoundModuleImport? selectedModuleImport, bool verifySemantics)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            if (kind is not (0x0001 or 0x0002 or 0x0004 or 0x0010 or 0x0011
                or 0x0016 or 0x0017 or 0x0018 or 0x001B or 0x001C
                or 0x001D or 0x0020 or 0x0021))
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

        if (tables.Schema != TrRmArtifactSchema.ForeignInvocationDraft
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
        int targetMethodRow, int targetIndex,
        ImmutableArray<PrimitiveType> callerParameterTypes,
        PrimitiveType callerResultType,
        ImmutableArray<PrimitiveType> targetParameterTypes,
        NativeFunctionPointerSignatureDefinition signature,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef)
    {
        if (body.BlockCount != 1 || body.RecordCount < 2
            || body.RecordCount > targetParameterTypes.Length + 3)
        {
            throw Unsupported("The caller Body is outside the bounded NativeAddress subset.");
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

            return (new BoundBlock([
                    new BoundUnsafe(new BoundBlock([
                        new BoundExpressionStatement(address, default),
                        new BoundReturn(null, default),
                    ], default), default),
                ], default),
                CreateVariables(callerParameterTypes, "arg"));
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
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef)
    {
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
            nativePointerTypeDefinitions, structureTypeByTypeDef);
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
