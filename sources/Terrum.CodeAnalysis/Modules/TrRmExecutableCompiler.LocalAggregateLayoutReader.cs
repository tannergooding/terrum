// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private sealed record LocalAggregateLayoutTypeEnvironment(
        ImmutableArray<BoundStruct> Structures,
        ImmutableArray<BoundNativeFixedArrayType> FixedArrayTypes,
        int[] StructureTypeByTypeDef,
        Dictionary<int, PrimitiveType> ArrayTypeByTypeSpecRow,
        int TargetProfileRow);

    private sealed record LocalAggregateLayoutSelection(
        int TargetKind,
        int AccessKind,
        int DestinationRegister,
        int RootVariable,
        PrimitiveType ReferentType,
        ImmutableArray<int> FieldPath,
        BoundExpression? FieldPlace,
        BoundNativeFixedArrayLayout? ArrayLayout,
        BoundExpression? Index,
        int HomeIndex)
    {
        internal BoundExpression? SelectedValue { get; init; }
        internal ImmutableArray<BoundNativeFixedArrayProjection>
            ArrayPath { get; init; } = [];
        internal ImmutableArray<BoundNativeFixedArrayFieldProjection>
            LeafFields { get; init; } = [];
        internal ImmutableArray<BoundNativeFixedArrayProjection>
            ArrayBasePath { get; init; } = [];
        internal ImmutableArray<int> ArrayBaseFieldPath { get; init; } = [];
        internal ImmutableArray<BoundNativeFixedArrayFieldProjection>
            ArrayBaseLeafFields { get; init; } = [];
        internal BoundExpression? ArrayBaseFieldPlace { get; init; }
    }

    private sealed record LocalAggregateLayoutBlock(
        ImmutableArray<BoundStatement> Statements,
        BoundExpression? Condition,
        int TrueTarget,
        int FalseTarget,
        int JumpTarget,
        bool Returns);

    private sealed record LocalAggregateLayoutControlFlow(
        int[] ProcessingOrder,
        int[][] Successors,
        int[][] Predecessors,
        HashSet<int>[] LiveIn,
        bool IsAcyclic);

    private sealed record LocalAggregateLayoutLoop(
        int Header, HashSet<int> Members, int[] ExitTargets);

    private readonly record struct LocalAggregateLayoutRegisterEntry(
        byte CarrierKind, ushort TypeMapIndex, PrimitiveType Type,
        byte AccessKind);

    private readonly record struct LocalAggregateLayoutRegisterExit(
        byte CarrierKind, PrimitiveType Type, byte AccessKind,
        LocalAggregateLayoutSelection? Selection);

    private enum LocalAggregateLayoutHomeActionKind : byte
    {
        Initialize,
        Replace,
        InitializeOrReplace,
        CallPreparation,
    }

    private readonly record struct LocalAggregateLayoutHomeAction(
        int HomeIndex, LocalAggregateLayoutHomeActionKind Kind,
        byte PriorState, byte Transition);

    private sealed record LocalAggregateLayoutLoadedModule(
        BoundProgram Program, int[] FunctionIndexByMethodRow,
        int[] FunctionIndexByImportRow);

    private sealed record LocalAggregateLayoutPendingComparison(
        int Destination,
        BoundExpression Left,
        BoundExpression Right);

    private sealed class LocalAggregateArrayInitialization
    {
        internal required int RootVariable { get; init; }
        internal required BoundNativeFixedArrayLayout Layout { get; init; }
        internal required ImmutableArray<BoundNativeFixedArrayProjection>
            ArrayBasePath { get; init; }
        internal required ImmutableArray<int> ArrayBaseFieldPath { get; init; }
        internal required ImmutableArray<BoundNativeFixedArrayFieldProjection>
            ArrayBaseLeafFields { get; init; }
        internal required BoundExpression? ArrayBaseFieldPlace { get; init; }
        internal required int StatementCountAtStart { get; init; }
        internal Dictionary<ulong, BoundExpression> Elements { get; } = [];
        internal ulong NextElementIndex { get; set; }
    }

    private sealed class LocalAggregateLayoutBodyState
    {
        internal required int SelectedModuleRow { get; init; }
        internal required int CurrentBlockIndex { get; set; }
        internal required BoundFunction Declaration { get; init; }
        internal required ImmutableArray<BoundFunction>.Builder Functions { get; init; }
        internal required int[] FunctionIndexByMethodRow { get; init; }
        internal required int[] FunctionIndexByImportRow { get; init; }
        internal required LocalAggregateLayoutTypeEnvironment TypeEnvironment { get; init; }
        internal required int ParameterCount { get; init; }
        internal required int FormalHomeStart { get; init; }
        internal required int[] HomeVariable { get; init; }
        internal required ImmutableArray<BoundVariable>.Builder Variables { get; init; }
        internal required ImmutableArray<BoundStatement>.Builder Statements { get; set; }
        internal required Dictionary<int, BoundExpression> RegisterValues { get; init; }
        internal required Dictionary<int, LocalAggregateLayoutSelection> Selections { get; init; }
        internal required HashSet<int> InitializedHomes { get; init; }
        internal required HashSet<int> PossiblyInitializedHomes { get; init; }
        internal required HashSet<int> ConditionalInitializationHomes { get; init; }
        internal required Dictionary<int, BoundConstant> HomeConstants { get; init; }
        internal required List<LocalAggregateArrayInitialization>
            PendingArrayInitializations { get; init; }
        internal required bool UseRegisterSlots { get; init; }
        internal required bool UseFixedPointHomeState { get; init; }
        internal required Dictionary<(int Register, PrimitiveType Type), int>
            RegisterSlotVariables { get; init; }
        internal required Dictionary<
            (int Register, int Depth, PrimitiveType Type), int>
            PlaceIndexSlotVariables { get; init; }
        internal required List<LocalAggregateLayoutHomeAction>
            HomeStateActions { get; set; }
    }

    private static LocalAggregateLayoutLoadedModule
        LoadLocalAggregateLayoutExecution(
        TrRmArtifactIndex index,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int selectedModuleRow,
        IReadOnlyDictionary<int, BoundModuleImport>?
            selectedMethodImportsByImportRow = null,
        bool isRootModule = true)
    {
        if (index.Schema != TrRmArtifactSchema.LocalAggregateLayoutDraft
            || tables.Schema != TrRmArtifactSchema.LocalAggregateLayoutDraft
            || selectedModuleRow <= 0
            || selectedModuleRow > tables.Modules.Count
            || tables.Modules.GetRequirements(selectedModuleRow) != (1u, 0u)
            || tables.Modules.GetGoverningRuleEdition(selectedModuleRow) != 1
            || tables.NativeImports.Count != 0
            || tables.StandardNativeImports.Count != 0
            || tables.NativeFunctionPointerSignatures.Count != 0
            || tables.NativeCallables.Count != 0
            || tables.NativeVTables.Count != 0
            || tables.NativeExports.Count != 0)
        {
            throw Unsupported(
                "L1 executable loading requires one fail-fast local aggregate Module without native-call contracts or non-ReturnCoverage clauses.");
        }
        for (var contractRow = 1;
            contractRow <= tables.Contracts.Count;
            contractRow++)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(
                    tables.Contracts.GetRow(contractRow)[4..]) != 0x0F)
            {
                throw Unsupported(
                    "L1 executable loading currently admits only the inherited Raw Union ReturnCoverage contract.");
            }
        }

        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            if (kind is not (0x0001 or 0x0002 or 0x0003 or 0x0004
                or 0x0010 or 0x0011 or 0x0012 or 0x0013 or 0x0014
                or 0x0015 or 0x0016 or 0x0017 or 0x0018 or 0x001B
                or 0x001C or 0x001D or 0x0020 or 0x0021 or 0x0023
                or 0x0030 or 0x003F or 0x0048))
            {
                throw Unsupported(
                    $"Section 0x{kind:X4} is outside the L1 local-body consumer.");
            }
        }

        for (var typeSpecRow = 1; typeSpecRow <= tables.TypeSpecs.Count;
            typeSpecRow++)
        {
            if (tables.NativeFunctionPointerAnchors
                .HasAnchorForTypeSpec(typeSpecRow))
            {
                throw Unsupported(
                    "L1 executable artifacts do not admit function-pointer anchors.");
            }
        }

        var (methodStart, methodEnd)
            = tables.Modules.GetMethodDefRange(selectedModuleRow);
        var methodCount = methodEnd - methodStart;
        if (methodCount > SemanticVerifier.MaximumFunctions)
        {
            throw Unsupported(
                "L1 executable loading exceeds the bounded local MethodDef subset.");
        }

        var (typeDefStart, typeDefEnd)
            = tables.Modules.GetTypeDefRange(selectedModuleRow);
        if (typeDefStart <= 0 || typeDefEnd < typeDefStart
            || (tables.Modules.GetEntryMethod(selectedModuleRow) != 0
                && (tables.Modules.GetEntryMethod(selectedModuleRow)
                        < methodStart
                    || tables.Modules.GetEntryMethod(selectedModuleRow)
                        >= methodEnd)))
        {
            throw Invalid(
                "The L1 executable Module has invalid local type or entry-point ranges.");
        }

        var typeEnvironment = LoadLocalAggregateLayoutTypes(tables,
            selectedModuleRow);
        var methodImportCount = selectedMethodImportsByImportRow?.Count ?? 0;
        if (methodImportCount > SemanticVerifier.MaximumFunctions
            || methodCount > SemanticVerifier.MaximumFunctions
                - methodImportCount)
        {
            throw Unsupported(
                "L1 method Import declarations exceed verifier function bounds.");
        }

        var functions = ImmutableArray.CreateBuilder<BoundFunction>(
            checked(methodCount + methodImportCount));
        var functionIndexByMethodRow = new int[tables.Methods.Count + 1];
        Array.Fill(functionIndexByMethodRow, -1);
        var functionIndexByImportRow = new int[tables.Imports.Count + 1];
        Array.Fill(functionIndexByImportRow, -1);
        var methodImportRows = new HashSet<int>();
        var importRowsByMethodRow = new Dictionary<int, List<int>>();
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

            var definition = tables.Imports
                .GetExpectedDefinitionForImportRow(importRow);
            if ((definition >> 24) != 0x16)
            {
                continue;
            }

            var methodRow = checked((int)(definition & 0x00FF_FFFF));
            if (methodRow < methodStart || methodRow >= methodEnd
                || !tables.Imports.IsExpectedDefinition(definition))
            {
                throw Invalid(
                    "An L1 MethodDef Import does not name a local expected declaration.");
            }

            if (selectedMethodImportsByImportRow is null
                || !selectedMethodImportsByImportRow.ContainsKey(importRow))
            {
                throw Invalid(
                    "An L1 MethodDef Import has no checked provider selection.");
            }

            _ = methodImportRows.Add(importRow);
            if (!importRowsByMethodRow.TryGetValue(methodRow,
                    out var importRows))
            {
                importRows = [];
                importRowsByMethodRow.Add(methodRow, importRows);
            }

            importRows.Add(importRow);
        }

        if (methodImportRows.Count != methodImportCount
            || (selectedMethodImportsByImportRow is not null
                && selectedMethodImportsByImportRow.Keys.Any(importRow =>
                    !methodImportRows.Contains(importRow))))
        {
            throw Invalid(
                "The L1 selected MethodDef Imports do not match the Module's original Import rows.");
        }

        for (var methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            var methodDefinition
                = 0x1600_0000u | checked((uint)methodRow);
            var isImportedMethod
                = tables.Imports.IsExpectedDefinition(methodDefinition);
            var methodBodyRow = tables.Methods.GetBodyRow(methodRow);
            if (tables.Methods.GetOwnerModule(methodRow) != selectedModuleRow
                || tables.Methods.GetOwnerType(methodRow) != 0
                || tables.Methods.GetFlags(methodRow) > 2
                || (isImportedMethod && methodBodyRow != 0)
                || (!isImportedMethod && methodBodyRow == 0))
            {
                throw Unsupported(
                    "L1 Modules require local top-level Body-bearing Methods or bodyless imported MethodDef expectations.");
            }

            var signatureRow = tables.Methods.GetSignature(methodRow);
            if (tables.Signatures.GetReceiverTypeSpec(signatureRow) != 0
                || tables.Signatures.GetGenericCount(signatureRow) != 0)
            {
                throw Unsupported(
                    "L1 executable local methods require nongeneric value signatures without receivers.");
            }

            var (parameterStart, parameterCount)
                = tables.Signatures.GetParamRange(signatureRow);
            var parameterTypes
                = ImmutableArray.CreateBuilder<PrimitiveType>(
                    parameterCount);
            var implicitReferenceParameters = new bool[parameterCount];
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var parameterRow = parameterStart + ordinal;
                if (tables.Parameters.GetMode(parameterRow) != 0
                    || (tables.Parameters.GetFlags(parameterRow) & ~1) != 0)
                {
                    throw Unsupported(
                        "L1 executable local methods require ordinary value or implicit-reference parameters.");
                }

                implicitReferenceParameters[ordinal]
                    = (tables.Parameters.GetFlags(parameterRow) & 1) != 0;
                var parameterTypeSpec = tables.Parameters.GetTypeSpec(
                    parameterRow);
                if (implicitReferenceParameters[ordinal])
                {
                    parameterTypeSpec
                        = GetLocalAggregateImplicitReferenceReferentTypeSpec(
                            tables.TypeSpecs, parameterTypeSpec);
                }

                var parameterType = ReadLocalAggregateType(tables,
                    parameterTypeSpec, typeEnvironment);
                if (implicitReferenceParameters[ordinal]
                    && parameterType == PrimitiveType.Void)
                {
                    throw Unsupported(
                        "L1 implicit-reference parameters require a non-unit referent.");
                }

                parameterTypes.Add(parameterType);
            }

            var returnType = ReadLocalAggregateType(tables,
                tables.Signatures.GetResultTypeSpec(signatureRow),
                typeEnvironment);
            var variables = ImmutableArray.CreateBuilder<BoundVariable>(
                parameterCount);
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                variables.Add(new BoundVariable($"arg{ordinal}",
                    parameterTypes[ordinal], false, default) {
                    IsImplicitRef = implicitReferenceParameters[ordinal],
                });
            }

            var methodFlags = tables.Methods.GetFlags(methodRow);
            var functionIndex = functions.Count;
            functionIndexByMethodRow[methodRow] = functionIndex;
            BoundModuleImport? moduleImport = null;
            if (isImportedMethod)
            {
                if (!importRowsByMethodRow.TryGetValue(methodRow,
                        out var importRows)
                    || importRows.Count == 0
                    || selectedMethodImportsByImportRow is null)
                {
                    throw Invalid(
                        "An L1 bodyless MethodDef has no matching Import.");
                }

                moduleImport = selectedMethodImportsByImportRow[
                    importRows[0]];
            }

            functions.Add(new BoundFunction(
                tables.Methods.GetName(methodRow), returnType,
                variables.MoveToImmutable(), parameterCount,
                new BoundBlock([], default), default)
            {
                IsPrivate = methodFlags == 0,
                IsInternal = methodFlags == 1,
                IsStatic = true,
                IsExternal = isImportedMethod,
                IsModuleReference = isImportedMethod,
                ModuleImport = moduleImport,
                RawUnionReturnCoverage =
                    ReadLocalAggregateReturnCoverage(tables, methodRow,
                        returnType, typeEnvironment),
            });
        }

        if (selectedMethodImportsByImportRow is not null)
        {
            foreach (var importRow in selectedMethodImportsByImportRow.Keys
                .Order())
            {
                var definition
                    = tables.Imports.GetExpectedDefinitionForImportRow(
                        importRow);
                var methodRow = checked((int)(definition
                    & 0x00FF_FFFF));
                if ((definition >> 24) != 0x16
                    || (uint)methodRow
                        >= (uint)functionIndexByMethodRow.Length
                    || functionIndexByMethodRow[methodRow] < 0
                    || !functions[functionIndexByMethodRow[methodRow]]
                        .IsExternal)
                {
                    throw Invalid(
                        "An L1 MethodMap Import has no reconstructed expected MethodDef.");
                }

                functionIndexByImportRow[importRow] = functions.Count;
                var importFunctionName
                    = $"__trrm_import_{importRow}";
                while (functions.Any(function => StringComparer.Ordinal
                    .Equals(function.Name, importFunctionName)))
                {
                    importFunctionName += "_";
                }

                var importFunction
                    = functions[functionIndexByMethodRow[methodRow]];
                functions.Add(importFunction with {
                        Name = importFunctionName,
                        ModuleImport
                            = selectedMethodImportsByImportRow[importRow],
                    });
            }
        }

        var bodyRowsUsed = new bool[tables.Bodies.Count + 1];
        for (var methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            var functionIndex = functionIndexByMethodRow[methodRow];
            var function = functions[functionIndex];
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            if (function.IsExternal)
            {
                continue;
            }

            if (bodyRow <= 0 || bodyRow >= bodyRowsUsed.Length
                || bodyRowsUsed[bodyRow])
            {
                throw Invalid(
                    "An L1 local MethodDef has an absent, shared or invalid Body.");
            }

            bodyRowsUsed[bodyRow] = true;
            tables.Bodies.ValidateSelectedImplementation(bodyRow,
                tables.Contracts.Count);
            tables.ValidateRegisterBodyMapsForMethod(methodRow);
            var body = tables.Bodies.GetRegisterBody(bodyRow,
                tables.Contracts.Count);
            var decoded = DecodeLocalAggregateLayoutBody(body, tables,
                selectedModuleRow, methodRow, function,
                functions, functionIndexByMethodRow,
                functionIndexByImportRow, typeEnvironment);
            functions[functionIndex] = decoded;
        }

        var entryMethodRow
            = tables.Modules.GetEntryMethod(selectedModuleRow);
        var entryPoint = !isRootModule || entryMethodRow == 0 ? -1
            : entryMethodRow < functionIndexByMethodRow.Length
                ? functionIndexByMethodRow[entryMethodRow]
                : -1;
        if (entryMethodRow != 0
            && (entryMethodRow >= functionIndexByMethodRow.Length
                || functionIndexByMethodRow[entryMethodRow] < 0
                || functions[
                    functionIndexByMethodRow[entryMethodRow]].IsExternal
                || (isRootModule
                    && (functions[
                            functionIndexByMethodRow[entryMethodRow]]
                            .ReturnType != PrimitiveType.Int32
                        || functions[
                            functionIndexByMethodRow[entryMethodRow]]
                            .ParameterCount != 0))))
        {
            throw Invalid(
                "An L1 entry MethodDef must be local and satisfy the selected root entry shape.");
        }

        var program = new BoundProgram(functions.MoveToImmutable(),
            entryPoint)
        {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            Structures = typeEnvironment.Structures,
            NativeFixedArrayTypes = typeEnvironment.FixedArrayTypes,
        };
        if (!NativeFixedArrayContract.MatchesProgram(program))
        {
            throw Invalid(
                "The L1 executable's reconstructed fixed-array types are inconsistent.");
        }

        var hasModuleReferences = program.Functions.Any(static function =>
            function.IsExternal && function.IsModuleReference
                && function.ModuleImport is not null);
        try
        {
            if (hasModuleReferences)
            {
                if (entryPoint < 0)
                {
                    SemanticVerifier.VerifyLibraryReferences(program);
                }
                else
                {
                    SemanticVerifier.VerifyReferences(program);
                }
            }
            else if (entryPoint < 0)
            {
                SemanticVerifier.VerifyLibrary(program);
            }
            else
            {
                SemanticVerifier.Verify(program);
            }
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "The reconstructed L1 Body violates mandatory semantic verification.",
                exception);
        }

        return new LocalAggregateLayoutLoadedModule(program,
            functionIndexByMethodRow, functionIndexByImportRow);
    }

    private static LocalAggregateLayoutTypeEnvironment
        LoadLocalAggregateLayoutTypes(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int selectedModuleRow)
    {
        if (!tables.TargetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision,
                out var profileRow))
        {
            throw Invalid(
                "The L1 executable has no selected Windows x64 TargetProfile.");
        }

        var (typeDefStart, typeDefEnd)
            = tables.Modules.GetTypeDefRange(selectedModuleRow);
        var selectedTypeDefRows = new HashSet<int>();
        for (var typeDefRow = typeDefStart; typeDefRow < typeDefEnd;
            typeDefRow++)
        {
            _ = selectedTypeDefRows.Add(typeDefRow);
            var (genericFirst, genericCount)
                = tables.TypeDefs.GetGenericRange(typeDefRow);
            var (layoutFirst, layoutCount)
                = tables.TypeDefs.GetLayoutRange(typeDefRow);
            var layout = tables.Layouts.GetLayoutForProfile(typeDefRow,
                checked((uint)profileRow));
            if (tables.TypeDefs.GetOwnerModule(typeDefRow)
                    != selectedModuleRow
                || tables.TypeDefs.GetKind(typeDefRow) != 1
                || !tables.TypeDefs.HasDeclaredCopy(typeDefRow)
                || genericFirst != 0 || genericCount != 0
                || layoutCount != 2
                || !tables.Layouts.HasBlittableLayout(typeDefRow,
                    checked((uint)profileRow), 1)
                || tables.Layouts.GetFlags(layout) != 1
                || tables.Layouts.GetLayoutKind(layout) is not (1 or 2 or 3))
            {
                throw Unsupported(
                    "L1 executable types require local or bound nongeneric Copy structs with complete Blittable layouts.");
            }
        }

        var nativePointerTypes = new List<NativePointerTypeDefinition>();
        var signatureIndexByAnchorMethod
            = new int[tables.Methods.Count + 1];
        Array.Fill(signatureIndexByAnchorMethod, -1);
        var fixedArrayTypes = new List<BoundNativeFixedArrayType>();
        var arrayTypeByTypeSpecRow = new Dictionary<int, PrimitiveType>();
        var materializedTypeDefRows = new HashSet<int>();
        var materializedTypeDefs = new bool[tables.TypeDefs.Count + 1];
        var structures = LoadBlittableStructures(tables.TypeDefs,
            tables.Fields, tables.Layouts, tables.LayoutFields,
            tables.TargetProfiles, tables.TypeSpecs, [],
            nativePointerTypes, out var structureTypeByTypeDef,
            fixedArrayTypes, arrayTypeByTypeSpecRow,
            materializedTypeDefRows, imports: tables.Imports,
            includedTypeDefRows: selectedTypeDefRows,
            allowDuplicateImportedViews: true);
        if (!NativeFixedArrayContract.TryCreateTypeIdentityIndex(
            fixedArrayTypes, out var fixedArrayTypeIndexes))
        {
            throw Invalid(
                "The L1 fixed-array type table is not canonical.");
        }

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
            for (var layoutIndex = 0;
                layoutIndex < pendingArrayLayouts.Count; layoutIndex++)
            {
                if (!TryAddForeignInvocationFixedArrayType(tables,
                        pendingArrayLayouts[layoutIndex], nativePointerTypes,
                        signatureIndexByAnchorMethod,
                        structureTypeByTypeDef, materializedTypeDefs,
                        structures, fixedArrayTypes,
                        fixedArrayTypeIndexes,
                        arrayTypeByTypeSpecRow, tables.Imports))
                {
                    continue;
                }

                pendingArrayLayouts.RemoveAt(layoutIndex);
                layoutIndex--;
                madeProgress = true;
            }

            var readyTypeDefs = new List<int>();
            for (var typeDefRow = typeDefStart;
                typeDefRow < typeDefEnd; typeDefRow++)
            {
                if (!materializedTypeDefs[typeDefRow]
                    && ForeignInvocationA2TypeDefDependenciesAreReady(
                        tables, typeDefRow, checked((uint)profileRow),
                        materializedTypeDefs, arrayTypeByTypeSpecRow,
                        tables.Imports))
                {
                    readyTypeDefs.Add(typeDefRow);
                }
            }

            if (readyTypeDefs.Count != 0)
            {
                foreach (var typeDefRow in readyTypeDefs)
                {
                    _ = materializedTypeDefRows.Add(typeDefRow);
                }

                structures = LoadBlittableStructures(tables.TypeDefs,
                    tables.Fields, tables.Layouts, tables.LayoutFields,
                    tables.TargetProfiles, tables.TypeSpecs, [],
                    nativePointerTypes, out var updatedTypeMap,
                    fixedArrayTypes, arrayTypeByTypeSpecRow,
                    materializedTypeDefRows, structures, tables.Imports,
                    selectedTypeDefRows,
                    allowDuplicateImportedViews: true);
                if (!structureTypeByTypeDef.SequenceEqual(updatedTypeMap))
                {
                    throw Invalid(
                        "L1 TypeDef identities changed while resolving array dependencies.");
                }

                foreach (var typeDefRow in readyTypeDefs)
                {
                    materializedTypeDefs[typeDefRow] = true;
                }
                madeProgress = true;
            }

            if (!madeProgress)
            {
                throw Invalid(
                    "The L1 executable has cyclic or unresolved TypeDef and fixed-array dependencies.");
            }
        }

        var nativePointerTypeArray = nativePointerTypes.ToImmutableArray();
        var fixedArrayTypeArray = fixedArrayTypes.ToImmutableArray();
        if (!NativePointerTypeContract.IsValidTable(
                nativePointerTypeArray, structures, [],
                fixedArrayTypeArray))
        {
            throw Invalid(
                "The L1 executable contains an invalid native pointer type table.");
        }

        var typeProgram = new BoundProgram([], -1)
        {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            Structures = structures,
            NativePointerTypes = nativePointerTypeArray,
            NativeFixedArrayTypes = fixedArrayTypeArray,
        };
        if (!NativeFixedArrayContract.MatchesProgram(typeProgram))
        {
            throw Invalid(
                "The L1 executable fixed-array table does not match reconstructed types.");
        }
        SemanticVerifier.VerifyLibraryWithDirectNativeImports(typeProgram,
            duplicateNameViewIndexes: GetL1DuplicateImportedViews(
                tables, selectedTypeDefRows, structureTypeByTypeDef));

        return new LocalAggregateLayoutTypeEnvironment(structures,
            fixedArrayTypeArray, structureTypeByTypeDef,
            arrayTypeByTypeSpecRow, profileRow);
    }

    private static HashSet<int> GetL1DuplicateImportedViews(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        HashSet<int> selectedTypeDefRows,
        int[] structureTypeByTypeDef)
    {
        var result = new HashSet<int>();
        foreach (var typeDefRow in selectedTypeDefRows)
        {
            if (tables.Imports.IsExpectedDefinition(
                    0x1300_0000u | checked((uint)typeDefRow)))
            {
                _ = result.Add(structureTypeByTypeDef[typeDefRow]);
            }
        }

        return result;
    }

    private static PrimitiveType ReadLocalAggregateType(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        uint typeSpecRef,
        LocalAggregateLayoutTypeEnvironment typeEnvironment)
    {
        var nativePointerTypes = new List<NativePointerTypeDefinition>();
        var signatureIndexByAnchorMethod
            = new int[tables.Methods.Count + 1];
        Array.Fill(signatureIndexByAnchorMethod, -1);
        return ReadForeignInvocationType(tables.TypeSpecs, typeSpecRef,
            nativePointerTypes, signatureIndexByAnchorMethod,
            typeEnvironment.StructureTypeByTypeDef, tables.Imports,
            typeEnvironment.ArrayTypeByTypeSpecRow);
    }

    private static uint
        GetLocalAggregateImplicitReferenceReferentTypeSpec(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef)
    {
        if (!typeSpecs.TryGetImplicitReadonlyReferenceReferentTypeSpecRef(
                typeSpecRef, out var referentTypeSpecRef))
        {
            throw Unsupported(
                "L1 implicit-reference parameters require an ordinary reference TypeSpec.");
        }

        return referentTypeSpecRef;
    }

    private static BoundFunction DecodeLocalAggregateLayoutBody(
        TrRmRegisterBodyReader body,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int selectedModuleRow,
        int methodRow,
        BoundFunction declaration,
        ImmutableArray<BoundFunction>.Builder functions,
        int[] functionIndexByMethodRow,
        int[] functionIndexByImportRow,
        LocalAggregateLayoutTypeEnvironment typeEnvironment)
    {
        if (body.Schema != TrRmArtifactSchema.LocalAggregateLayoutDraft
            || body.BlockCount < 1
            || body.HasExceptionDirectory
            || (body.MapMask & ~0x17) != 0
            || (body.MapMask & 0x02) == 0
            || body.FieldCount != 0
            || body.RecordCount < 1)
        {
            throw Unsupported(
                "The L1 Body requires TypeMap, optional Home/Method/Constant maps, no FieldMap and no exception directory.");
        }

        var methodSignature = tables.Methods.GetSignature(methodRow);
        var (_, parameterCount)
            = tables.Signatures.GetParamRange(methodSignature);
        var formalHomeStart = body.HomeCount - parameterCount;
        if (formalHomeStart < 0)
        {
            throw Invalid(
                "An L1 Body has fewer Homes than its MethodSig formals.");
        }

        var controlFlow = AnalyzeLocalAggregateLayoutControlFlow(body);
        if (controlFlow.LiveIn[0].Count != 0)
        {
            throw Invalid(
                "The L1 entry Block cannot read an uninitialized incoming register.");
        }

        var blockEntries = ReadLocalAggregateBlockEntries(body, tables,
            typeEnvironment);
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            foreach (var register in controlFlow.LiveIn[blockIndex])
            {
                if (!blockEntries[blockIndex].TryGetValue(register,
                        out var entry))
                {
                    throw Invalid(
                        "A live L1 register is absent from its BlockEntryState.");
                }

                if (entry.CarrierKind == 2)
                {
                    throw Unsupported(
                        "L1 executable register joins do not yet admit TypeIdentity carriers.");
                }

                if (entry.CarrierKind == 0
                    && entry.Type == PrimitiveType.Void)
                {
                    throw Invalid(
                        "A live L1 value register has the unit type.");
                }
            }
        }

        var useRegisterSlots = !controlFlow.IsAcyclic
            || controlFlow.LiveIn.Any(static registers =>
                registers.Count != 0);
        var useFixedPointHomeState = !controlFlow.IsAcyclic;
        var homeVariable = new int[body.HomeCount];
        Array.Fill(homeVariable, -1);
        var localTypes = new PrimitiveType[formalHomeStart];
        var variables = declaration.Variables.ToBuilder();
        var formalSeen = new bool[parameterCount];
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            var offset = homeIndex * 8;
            var role = body.HomeMap[offset];
            var mutable = body.HomeMap[offset + 1] != 0;
            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap[(offset + 2)..]);
            var type = ReadLocalAggregateType(tables,
                ReadLocalAggregateBodyTypeSpec(body, typeMapIndex),
                typeEnvironment);
            if (homeIndex < formalHomeStart)
            {
                if (role != 3)
                {
                    throw Invalid(
                        "L1 nonformal Homes must be ordinary role-3 local storage.");
                }

                homeVariable[homeIndex] = variables.Count;
                localTypes[homeIndex] = type;
                variables.Add(new BoundVariable(
                    $"__trrm_home_{homeIndex}", type, mutable, default) {
                    IsInitializationLocal = useFixedPointHomeState,
                });
                continue;
            }

            if (role != 2)
            {
                throw Invalid(
                    "L1 formal Homes must be role-2 parameter storage.");
            }

            var ordinal = BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap[(offset + 4)..]);
            if (ordinal >= parameterCount || formalSeen[ordinal]
                || type != declaration.Variables[ordinal].Type)
            {
                throw Invalid(
                    "An L1 formal Home does not match its ordinary MethodSig parameter.");
            }

            homeVariable[homeIndex] = ordinal;
            formalSeen[ordinal] = true;
            variables[ordinal] = variables[ordinal] with {
                IsMutable = mutable,
            };
        }

        if (Array.Exists(formalSeen, static seen => !seen))
        {
            throw Invalid("An L1 Body omits a formal Home.");
        }

        var registerValues = new Dictionary<int, BoundExpression>();
        var selections
            = new Dictionary<int, LocalAggregateLayoutSelection>();
        var statements = ImmutableArray.CreateBuilder<BoundStatement>();
        var initializedHomes = new HashSet<int>();
        var possiblyInitializedHomes = new HashSet<int>();
        var conditionalInitializationHomes = new HashSet<int>();
        var homeConstants = new Dictionary<int, BoundConstant>();
        var pendingArrayInitializations
            = new List<LocalAggregateArrayInitialization>();
        var registerSlotVariables
            = new Dictionary<(int Register, PrimitiveType Type), int>();
        var placeIndexSlotVariables
            = new Dictionary<(int Register, int Depth, PrimitiveType Type),
                int>();
        var homeStateActions
            = new List<LocalAggregateLayoutHomeAction>();
        var state = new LocalAggregateLayoutBodyState {
            SelectedModuleRow = selectedModuleRow,
            CurrentBlockIndex = 0,
            Declaration = declaration,
            Functions = functions,
            FunctionIndexByMethodRow = functionIndexByMethodRow,
            FunctionIndexByImportRow = functionIndexByImportRow,
            TypeEnvironment = typeEnvironment,
            ParameterCount = parameterCount,
            FormalHomeStart = formalHomeStart,
            HomeVariable = homeVariable,
            Variables = variables,
            Statements = statements,
            RegisterValues = registerValues,
            Selections = selections,
            InitializedHomes = initializedHomes,
            PossiblyInitializedHomes = possiblyInitializedHomes,
            ConditionalInitializationHomes
                = conditionalInitializationHomes,
            HomeConstants = homeConstants,
            PendingArrayInitializations = pendingArrayInitializations,
            UseRegisterSlots = useRegisterSlots,
            UseFixedPointHomeState = useFixedPointHomeState,
            RegisterSlotVariables = registerSlotVariables,
            PlaceIndexSlotVariables = placeIndexSlotVariables,
            HomeStateActions = homeStateActions,
        };
        var decodedBlocks
            = new LocalAggregateLayoutBlock[body.BlockCount];
        var registerExitsByBlock
            = new Dictionary<int, LocalAggregateLayoutRegisterExit>[
                body.BlockCount];
        var entryPlaceSelectionsByBlock
            = new Dictionary<int, LocalAggregateLayoutSelection>[
                body.BlockCount];
        var homeActionsByBlock
            = new IReadOnlyList<LocalAggregateLayoutHomeAction>[
                body.BlockCount];
        var initializedHomesByBlock
            = new HashSet<int>[body.BlockCount];
        var possiblyInitializedHomesByBlock
            = new HashSet<int>[body.BlockCount];
        var returnedHomeVariables = new HashSet<int>();
        var hasOtherReturn = false;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            if (body.GetRecord(recordIndex)[0] == 0x0D)
            {
                throw Unsupported(
                    "L1 executable Bodies do not admit NativeAddress records.");
            }
        }

        foreach (var blockIndex in controlFlow.ProcessingOrder)
        {
            state.CurrentBlockIndex = blockIndex;
            state.Statements = ImmutableArray.CreateBuilder<BoundStatement>();
            registerValues.Clear();
            selections.Clear();
            state.HomeStateActions = [];
            homeActionsByBlock[blockIndex] = state.HomeStateActions;
            if (blockIndex == 0)
            {
                if (controlFlow.IsAcyclic
                    && controlFlow.Predecessors[blockIndex].Length != 0)
                {
                    throw Unsupported(
                        "The L1 CFG entry Block has a predecessor.");
                }

                initializedHomes.Clear();
                possiblyInitializedHomes.Clear();
            }
            else
            {
                var predecessors
                    = controlFlow.Predecessors[blockIndex];
                if (predecessors.Length == 0)
                {
                    throw Invalid(
                        "The L1 CFG contains an unreachable Block.");
                }

                initializedHomes.Clear();
                possiblyInitializedHomes.Clear();
                if (!useFixedPointHomeState)
                {
                    initializedHomes.UnionWith(
                        initializedHomesByBlock[predecessors[0]]);
                    foreach (var predecessor in predecessors)
                    {
                        possiblyInitializedHomes.UnionWith(
                            possiblyInitializedHomesByBlock[predecessor]);
                    }
                    for (var predecessorIndex = 1;
                        predecessorIndex < predecessors.Length;
                        predecessorIndex++)
                    {
                        initializedHomes.IntersectWith(
                            initializedHomesByBlock[
                                predecessors[predecessorIndex]]);
                    }
                }

                homeConstants.Clear();
            }

            var incomingPlaceSelections
                = new Dictionary<int, LocalAggregateLayoutSelection>();
            foreach (var register in controlFlow.LiveIn[blockIndex])
            {
                var entry = blockEntries[blockIndex][register];
                if (entry.CarrierKind == 0)
                {
                    registerValues.Add(register,
                        ReadLocalAggregateRegisterSlot(state, register,
                            entry.Type));
                    continue;
                }

                LocalAggregateLayoutSelection? incomingSelection = null;
                foreach (var predecessor
                    in controlFlow.Predecessors[blockIndex])
                {
                    var predecessorState
                        = registerExitsByBlock[predecessor];
                    if (predecessorState is not null
                        && predecessorState.TryGetValue(register,
                            out var exit)
                        && exit.CarrierKind == 1
                        && exit.Selection is not null)
                    {
                        incomingSelection
                            = exit.Selection with {
                                DestinationRegister = register,
                            };
                        break;
                    }
                }

                if (incomingSelection is null)
                {
                    throw Invalid(
                        "A live L1 PlaceHandle has no initialized incoming edge.");
                }

                if (incomingSelection.ReferentType != entry.Type
                    || incomingSelection.AccessKind != entry.AccessKind)
                {
                    throw Invalid(
                        "An L1 PlaceHandle edge disagrees with its BlockEntryState authority.");
                }

                incomingPlaceSelections.Add(register, incomingSelection);
                selections.Add(register, incomingSelection);
            }
            entryPlaceSelectionsByBlock[blockIndex]
                = incomingPlaceSelections;

            var block = body.GetBlock(blockIndex);

            var blockCondition = (BoundExpression?)null;
            var trueTarget = -1;
            var falseTarget = -1;
            var jumpTarget = -1;
            var blockReturns = false;
            var blockTerminated = false;
            LocalAggregateLayoutPendingComparison? pendingComparison = null;
            var code = body.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var opcode = code[position];
                var length = TrRmRegisterBodyReader.InstructionLength(opcode,
                    body.Schema);
                if (length > code.Length - position)
                {
                    throw Invalid("An L1 Body instruction is truncated.");
                }

                var instruction = code.Slice(position, length);
                position += length;
                if (blockTerminated)
                {
                    throw Invalid(
                        "An L1 Body contains instructions after its terminal transfer.");
                }
                if (pendingComparison is not null
                    && opcode is not (0xC3 or 0xE5)
                    && opcode is not (>= 0xA0 and <= 0xAF))
                {
                    throw Invalid(
                        "An L1 scalar comparison result is not immediately tested.");
                }

                switch (opcode)
                {
                case 0x10:
                {
                    var destination = instruction[1] & 0x0F;
                    var source = instruction[1] >> 4;
                    if (source is > 13)
                    {
                        throw Invalid(
                            "An L1 Move reads an undefined value register.");
                    }

                    if (selections.TryGetValue(source,
                            out var selectedPlace))
                    {
                        StoreLocalAggregateRegisterSelection(state,
                            CloneLocalAggregateSelectionForRegister(state,
                                selectedPlace, destination));
                    }
                    else if (registerValues.TryGetValue(source,
                                 out var value))
                    {
                        StoreLocalAggregateRegisterValue(state,
                            destination, value);
                    }
                    else
                    {
                        throw Invalid(
                            "An L1 Move reads an undefined value or PlaceHandle register.");
                    }
                    break;
                }
                case 0x13:
                {
                    var destination = instruction[1] & 0x0F;
                    var homeIndex = instruction[1] >> 4;
                    RegisterHomeRead(state, destination, homeIndex);
                    break;
                }
                case 0x14:
                {
                    var homeIndex = instruction[1] & 0x0F;
                    var source = instruction[1] >> 4;
                    var value = ReadRegisterOrContext(body, state, source,
                        homeIndex);
                    StoreHome(state, homeIndex, value);
                    break;
                }
                case 0x16:
                {
                    var destination = instruction[1] & 0x0F;
                    var constantMapIndex = instruction[1] >> 4;
                    StoreLocalAggregateRegisterValue(state, destination,
                        ReadConstant(body, tables, state,
                            constantMapIndex));
                    break;
                }
                case 0x17:
                    if (instruction[1] != 0
                        || declaration.ReturnType == PrimitiveType.Void
                        || !registerValues.TryGetValue(0,
                            out var registerResult)
                        || registerResult.Type != declaration.ReturnType)
                    {
                        throw Invalid(
                            "An L1 ReturnRegister does not match its MethodSig.");
                    }

                    state.Statements.Add(new BoundReturn(registerResult,
                        default));
                    hasOtherReturn = true;
                    blockReturns = true;
                    blockTerminated = true;
                    break;
                case 0x18:
                    if (instruction[1] != 0
                        || declaration.ReturnType != PrimitiveType.Void)
                    {
                        throw Invalid(
                            "An L1 ReturnUnit does not match its MethodSig.");
                    }

                    state.Statements.Add(new BoundReturn(null, default));
                    hasOtherReturn = true;
                    blockReturns = true;
                    blockTerminated = true;
                    break;
                case 0xB0:
                    jumpTarget = instruction[1];
                    if (jumpTarget >= body.BlockCount)
                    {
                        throw Invalid(
                            "An L1 short Branch target is outside the Block table.");
                    }

                    blockTerminated = true;
                    break;
                case 0xB1:
                {
                    var conditionRegister = instruction[1] & 0x0F;
                    var edgeRecordIndex = instruction[1] >> 4;
                    (trueTarget, falseTarget)
                        = ReadLocalAggregateConditionalEdge(body,
                            edgeRecordIndex);
                    if (!registerValues.TryGetValue(conditionRegister,
                            out var condition)
                        || condition.Type != PrimitiveType.Bool)
                    {
                        throw Invalid(
                            "An L1 Boolean branch condition is not a checked Boolean value.");
                    }

                    blockCondition = condition;
                    blockTerminated = true;
                    break;
                }
                case >= 0xA0 and <= 0xAF:
                {
                    var operand = instruction[1];
                    var conditionRegister = operand & 0x0F;
                    var predicateMask = opcode & 0x0F;
                    var edgeRecordIndex = operand >> 4;
                    (trueTarget, falseTarget)
                        = ReadLocalAggregateConditionalEdge(body,
                            edgeRecordIndex);
                    blockCondition = ReadLocalAggregatePredicateCondition(
                        predicateMask, conditionRegister, registerValues,
                        pendingComparison, body.Schema,
                        out var consumedComparison);
                    if (consumedComparison)
                    {
                        pendingComparison = null;
                    }

                    blockTerminated = true;
                    break;
                }
                case 0x63:
                {
                    var destination = instruction[1] & 0x0F;
                    var placeRegister = instruction[1] >> 4;
                    if (!selections.TryGetValue(placeRegister,
                            out var selection)
                        || selection.DestinationRegister != placeRegister
                        || selection.AccessKind != 1)
                    {
                        throw Invalid(
                            "An L1 PlaceLoad reads an undefined PlaceHandle.");
                    }

                    StoreLocalAggregateRegisterValue(state, destination,
                        ReadSelection(selection));
                    break;
                }
                case 0x64:
                {
                    var placeRegister = instruction[1] & 0x0F;
                    var sourceRegister = instruction[1] >> 4;
                    if (!selections.TryGetValue(placeRegister,
                            out var selection)
                        || selection.DestinationRegister != placeRegister
                        || !registerValues.TryGetValue(sourceRegister,
                            out var value))
                    {
                        throw Invalid(
                            "An L1 PlaceStore has an undefined place or source register.");
                    }

                    AppendPlaceStore(selection, value, transition: 2);
                    break;
                }
                case 0xD0:
                {
                    var control = instruction[1];
                    var sourceRegister = control & 0x0F;
                    var destination = control >> 4;
                    var typeMapIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    if (sourceRegister > 13 || destination > 13
                        || !registerValues.TryGetValue(sourceRegister,
                            out var source))
                    {
                        throw Invalid(
                            "An L1 checked conversion has an undefined register.");
                    }

                    var destinationType = ReadLocalAggregateType(tables,
                        ReadLocalAggregateBodyTypeSpec(body, typeMapIndex),
                        typeEnvironment);
                    if (destinationType == PrimitiveType.Void)
                    {
                        throw Invalid(
                            "An L1 conversion cannot produce unit.");
                    }

                    StoreLocalAggregateRegisterValue(state, destination,
                        new BoundCast(source, destinationType, default));
                    break;
                }
                case 0xC0:
                {
                    var control = instruction[1];
                    var payload
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    var destination = payload & 0x0F;
                    var result1 = (payload >> 4) & 0x0F;
                    var source0 = (payload >> 8) & 0x0F;
                    var source1 = payload >> 12;
                    var isUnary = control is 0x04 or 0x08 or 0x25;
                    var isFloating = control is >= 0x20 and <= 0x25;
                    var isSupportedOperation = control is (>= 0x01
                            and <= 0x08)
                        or 0x0C or 0x0D
                        or (>= 0x20 and <= 0x25);
                    if (!isSupportedOperation || destination > 13
                        || result1 != 15 || source0 > 13
                        || (isUnary ? source1 != 15 : source1 > 13)
                        || !registerValues.TryGetValue(source0,
                            out var left)
                        || (isFloating
                            ? !IsFloat(left.Type) : !IsInteger(left.Type)))
                    {
                        throw Invalid(
                            "An L1 scalar arithmetic operation has invalid registers, control or type.");
                    }

                    if (isUnary)
                    {
                        var overflowBehavior = control == 0x08
                            ? BoundIntegerOverflowBehavior.Wrapping
                            : BoundIntegerOverflowBehavior.Checked;
                        StoreLocalAggregateRegisterValue(state, destination,
                            new BoundUnary(TokenKind.Minus, left, left.Type,
                                default) {
                                OverflowBehavior = overflowBehavior,
                            });
                        break;
                    }

                    if (!registerValues.TryGetValue(source1,
                            out var right)
                        || right.Type != left.Type)
                    {
                        throw Invalid(
                            "An L1 scalar arithmetic operation has an undefined or mismatched right operand.");
                    }

                    var token = control switch {
                        0x01 or 0x05 or 0x20 => TokenKind.Plus,
                        0x02 or 0x06 or 0x21 => TokenKind.Minus,
                        0x03 or 0x07 or 0x22 => TokenKind.Star,
                        0x0C or 0x23 => TokenKind.Slash,
                        0x0D or 0x24 => TokenKind.Percent,
                        _ => throw Invalid(
                            "An L1 scalar arithmetic control has no binary operation."),
                    };
                    var binaryOverflowBehavior = control is 0x05 or 0x06
                        or 0x07 ? BoundIntegerOverflowBehavior.Wrapping
                        : BoundIntegerOverflowBehavior.Checked;
                    StoreLocalAggregateRegisterValue(state, destination,
                        new BoundBinary(token, left, right, left.Type,
                            default) {
                            OverflowBehavior = binaryOverflowBehavior,
                        });
                    break;
                }
                case 0xC1:
                {
                    var control = instruction[1];
                    var payload
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    var destination = payload & 0x0F;
                    var result1 = (payload >> 4) & 0x0F;
                    var source0 = (payload >> 8) & 0x0F;
                    var source1 = payload >> 12;
                    if (control is not (0x01 or 0x02 or 0x03)
                        || destination > 13 || result1 != 15
                        || source0 > 13 || source1 > 13
                        || !registerValues.TryGetValue(source0,
                            out var left)
                        || !registerValues.TryGetValue(source1,
                            out var right)
                        || left.Type != right.Type
                        || !PrimitiveTypeFacts.IsInteger(left.Type))
                    {
                        throw Invalid(
                            "An L1 bit operation has invalid registers, control or type.");
                    }

                    var token = control switch {
                        0x01 => TokenKind.Ampersand,
                        0x02 => TokenKind.Pipe,
                        _ => TokenKind.Caret,
                    };
                    StoreLocalAggregateRegisterValue(state, destination,
                        new BoundBinary(token, left, right, left.Type,
                            default));
                    break;
                }
                case 0xC2:
                {
                    var control = instruction[1];
                    var payload
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    var destination = payload & 0x0F;
                    var secondResult = (payload >> 4) & 0x0F;
                    var leftRegister = (payload >> 8) & 0x0F;
                    var rightRegister = payload >> 12;
                    if (destination > 13 || secondResult != 15
                        || !registerValues.TryGetValue(leftRegister,
                            out var left)
                        || !registerValues.TryGetValue(rightRegister,
                            out var right)
                        || left.Type != right.Type)
                    {
                        throw Invalid(
                            "An L1 comparison has invalid destination or operands.");
                    }

                    if (control == 0)
                    {
                        if (pendingComparison is not null)
                        {
                            throw Invalid(
                                "An L1 Body has overlapping pending comparisons.");
                        }

                        pendingComparison
                            = new LocalAggregateLayoutPendingComparison(
                                destination, left, right);
                    }
                    else
                    {
                        var token = control switch {
                            1 => TokenKind.EqualsEquals,
                            2 => TokenKind.BangEquals,
                            _ => throw Invalid(
                                "An L1 Compare has an unsupported direct predicate."),
                        };
                        StoreLocalAggregateRegisterValue(state, destination,
                            new BoundBinary(token, left, right,
                                PrimitiveType.Bool, default));
                    }
                    break;
                }
                case 0xC3:
                {
                    var control = instruction[1];
                    var payload
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    var destination = payload & 0x0F;
                    var conditionRegister = (payload >> 4) & 0x0F;
                    var unused0 = (payload >> 8) & 0x0F;
                    var unused1 = payload >> 12;
                    if (control is not (1 or 2 or 3 or 4 or 6 or 13)
                        || destination > 13
                        || unused0 != 15 || unused1 != 15
                        || pendingComparison is not
                            { } comparison
                        || comparison.Destination != conditionRegister)
                    {
                        throw Invalid(
                            "An L1 SetCondition has no matching raw comparison or uses an unsupported predicate.");
                    }

                    var token
                        = GetLocalAggregateComparisonOperator(control);
                    StoreLocalAggregateRegisterValue(state, destination,
                        new BoundBinary(token, comparison.Left,
                            comparison.Right, PrimitiveType.Bool, default));
                    pendingComparison = null;
                    break;
                }
                case 0x90:
                    AppendCall(body, tables, state, instruction[1]);
                    break;
                case 0x91:
                {
                    var destination = instruction[1] & 0x0F;
                    var recordIndex = instruction[1] >> 4;
                    var selection = ReadSelectionRecord(body, tables,
                        state, recordIndex);
                    if (selection.DestinationRegister != destination)
                    {
                        throw Invalid(
                            "An L1 PlaceSelection instruction destination differs from its record.");
                    }

                    StoreLocalAggregateRegisterSelection(state,
                        selection);
                    break;
                }
                case 0x92:
                    AppendSemanticStore(body, tables, state, instruction[1]);
                    break;
                case 0xB8:
                {
                    var homeIndex = instruction[1];
                    if (declaration.ReturnType == PrimitiveType.Void
                        || (uint)homeIndex >= (uint)homeVariable.Length)
                    {
                        throw Invalid(
                            "An L1 ReturnHome does not match its MethodSig.");
                    }

                    var variable = homeVariable[homeIndex];
                    if (variable < 0
                        || variables[variable].Type
                            != declaration.ReturnType)
                    {
                        throw Invalid(
                            "An L1 ReturnHome has the wrong complete value type.");
                    }

                    _ = returnedHomeVariables.Add(variable);
                    state.Statements.Add(new BoundReturn(
                        new BoundRead(variable, declaration.ReturnType,
                            default), default));
                    blockReturns = true;
                    blockTerminated = true;
                    break;
                }
                case 0xD4:
                {
                    var destination = instruction[1];
                    var constantMapIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    StoreLocalAggregateRegisterValue(state, destination,
                        ReadConstant(body, tables, state,
                            constantMapIndex));
                    break;
                }
                case 0xD5:
                {
                    var destination = instruction[1];
                    var homeIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    RegisterHomeRead(state, destination, homeIndex);
                    break;
                }
                case 0xD6:
                {
                    var sourceUse = instruction[1] >> 4;
                    var source = instruction[1] & 0x0F;
                    var homeIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    if (sourceUse != 1)
                    {
                        throw Unsupported(
                            "L1 local Home stores currently admit CopyRead only.");
                    }

                    StoreHome(state, homeIndex,
                        ReadRegisterOrContext(body, state, source,
                            homeIndex));
                    break;
                }
                case 0xD8:
                {
                    var destination = instruction[1];
                    var recordIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    var selection = ReadSelectionRecord(body, tables,
                        state, recordIndex);
                    if (selection.DestinationRegister != destination)
                    {
                        throw Invalid(
                            "An L1 PlaceSelection instruction destination differs from its record.");
                    }

                    StoreLocalAggregateRegisterSelection(state,
                        selection);
                    break;
                }
                case 0xD9:
                {
                    if (instruction[1] != 0)
                    {
                        throw Invalid(
                            "An L1 SemanticStore instruction has nonzero control bits.");
                    }

                    var recordIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    AppendSemanticStore(body, tables, state, recordIndex);
                    break;
                }
                case 0xDA:
                {
                    var destination = instruction[1];
                    var recordIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    var selection = ReadSelectionRecord(body, tables,
                        state, recordIndex);
                    if (selection.DestinationRegister != destination
                        || selection.AccessKind != 1)
                    {
                        throw Invalid(
                            "An L1 PlaceLoad destination or access differs from its PlaceSelection.");
                    }

                    StoreLocalAggregateRegisterValue(state, destination,
                        ReadSelection(selection));
                    break;
                }
                case 0xE0:
                {
                    if (instruction[1] != 0)
                    {
                        throw Invalid(
                            "An L1 CallSite instruction has nonzero control bits.");
                    }

                    var recordIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    AppendCall(body, tables, state, recordIndex);
                    break;
                }
                case 0xE3:
                {
                    if (instruction[1] != 0)
                    {
                        throw Invalid(
                            "An L1 Branch instruction has nonzero control bits.");
                    }

                    jumpTarget
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    if (jumpTarget >= body.BlockCount)
                    {
                        throw Invalid(
                            "An L1 Branch target is outside the Block table.");
                    }

                    blockTerminated = true;
                    break;
                }
                case 0xE4:
                {
                    if ((instruction[1] & 0xF0) != 0)
                    {
                        throw Invalid(
                            "An L1 Boolean branch has nonzero reserved control bits.");
                    }

                    var conditionRegister = instruction[1] & 0x0F;
                    var edgeRecordIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    (trueTarget, falseTarget)
                        = ReadLocalAggregateConditionalEdge(body,
                            edgeRecordIndex);
                    if (!registerValues.TryGetValue(conditionRegister,
                            out var condition)
                        || condition.Type != PrimitiveType.Bool)
                    {
                        throw Invalid(
                            "An L1 Boolean branch condition is not a checked Boolean value.");
                    }

                    blockCondition = condition;
                    blockTerminated = true;
                    break;
                }
                case 0xE5:
                {
                    var control = instruction[1];
                    var conditionRegister = control & 0x0F;
                    var predicateMask = control >> 4;
                    var edgeRecordIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    (trueTarget, falseTarget)
                        = ReadLocalAggregateConditionalEdge(body,
                            edgeRecordIndex);
                    blockCondition = ReadLocalAggregatePredicateCondition(
                        predicateMask, conditionRegister, registerValues,
                        pendingComparison, body.Schema,
                        out var consumedComparison);
                    if (consumedComparison)
                    {
                        pendingComparison = null;
                    }

                    blockTerminated = true;
                    break;
                }
                case 0xEB:
                {
                    if (instruction[1] != 0
                        || declaration.ReturnType == PrimitiveType.Void)
                    {
                        throw Invalid(
                            "An L1 ReturnHome instruction does not match its MethodSig.");
                    }

                    var homeIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    if (homeIndex >= homeVariable.Length)
                    {
                        throw Invalid(
                            "An L1 ReturnHome index is outside the HomeMap.");
                    }

                    var variable = homeVariable[homeIndex];
                    if (variable < 0
                        || variables[variable].Type
                            != declaration.ReturnType)
                    {
                        throw Invalid(
                            "An L1 ReturnHome has the wrong complete value type.");
                    }

                    _ = returnedHomeVariables.Add(variable);
                    state.Statements.Add(new BoundReturn(
                        new BoundRead(variable, declaration.ReturnType,
                            default), default));
                    blockReturns = true;
                    blockTerminated = true;
                    break;
                }
                default:
                    throw Unsupported(
                        $"L1 register Body opcode 0x{opcode:X2} is outside the admitted acyclic conditional consumer subset.");
                }
            }

            if (!blockTerminated)
            {
                throw Invalid(
                    "An L1 CFG block has no reconstructed terminal instruction.");
            }

            decodedBlocks[blockIndex] = new LocalAggregateLayoutBlock(
                state.Statements.ToImmutable(), blockCondition, trueTarget,
                falseTarget, jumpTarget, blockReturns);
            registerExitsByBlock[blockIndex]
                = GetLocalAggregateRegisterExitState(state);
            if (!initializedHomes.IsSubsetOf(
                    possiblyInitializedHomes))
            {
                throw Invalid(
                    "An L1 CFG Home-state summary marks a Home definitely live but not possibly live.");
            }

            initializedHomesByBlock[blockIndex]
                = [.. initializedHomes];
            possiblyInitializedHomesByBlock[blockIndex]
                = [.. possiblyInitializedHomes];
            if (pendingArrayInitializations.Count != 0)
            {
                throw Invalid(
                    "An L1 explicit-array initialization is incomplete at a Block boundary.");
            }
        }

        ValidateLocalAggregateRegisterEdges(controlFlow, blockEntries,
            registerExitsByBlock, entryPlaceSelectionsByBlock);
        if (useFixedPointHomeState)
        {
            ValidateLocalAggregateHomeStateFlow(controlFlow,
                formalHomeStart, homeActionsByBlock);
        }

        var bodyStatements = BuildLocalAggregateLayoutBody(decodedBlocks,
            controlFlow);

        var vacantHomes = new SortedSet<int>(
            state.ConditionalInitializationHomes);
        if (useFixedPointHomeState)
        {
            for (var homeIndex = 0; homeIndex < formalHomeStart;
                homeIndex++)
            {
                _ = vacantHomes.Add(homeVariable[homeIndex]);
            }
        }
        else
        {
            var homesInitializedInAnyBlock
                = new HashSet<int>();
            foreach (var blockHomes in possiblyInitializedHomesByBlock)
            {
                homesInitializedInAnyBlock.UnionWith(blockHomes);
            }

            for (var homeIndex = 0; homeIndex < formalHomeStart;
                homeIndex++)
            {
                if (!homesInitializedInAnyBlock.Contains(homeIndex))
                {
                    _ = vacantHomes.Add(homeVariable[homeIndex]);
                }
            }
        }

        foreach (var registerVariable in registerSlotVariables.Values)
        {
            _ = vacantHomes.Add(registerVariable);
        }
        foreach (var indexVariable in placeIndexSlotVariables.Values)
        {
            _ = vacantHomes.Add(indexVariable);
        }
        var constructionHomeIndex = -1;
        if (declaration.IsPrivate && !declaration.IsInternal
            && returnedHomeVariables.Count != 0 && !hasOtherReturn
            && returnedHomeVariables.All(variable =>
                variable == declaration.ParameterCount)
            && (int)declaration.ReturnType >= 100
            && (int)declaration.ReturnType < ManagedClassTypes.First
            && formalHomeStart > 0
            && localTypes[0] == declaration.ReturnType
            && variables[declaration.ParameterCount].IsMutable
            && HasLocalAggregateConstructionStore(bodyStatements,
                declaration.ParameterCount))
        {
            constructionHomeIndex = declaration.ParameterCount;
            _ = vacantHomes.Add(constructionHomeIndex);
        }

        foreach (var variable in vacantHomes)
        {
            if (variable < parameterCount
                || variable >= variables.Count)
            {
                throw Invalid(
                    "An L1 vacant local initializer does not select local storage.");
            }

            variables[variable] = variables[variable] with {
                IsInitializationLocal = true,
            };
        }

        if (vacantHomes.Count != 0)
        {
            var statementsWithVacantConstruction
                = ImmutableArray.CreateBuilder<BoundStatement>(
                    bodyStatements.Length + vacantHomes.Count);
            foreach (var variable in vacantHomes)
            {
                statementsWithVacantConstruction.Add(new BoundVacantLocal(
                    variable, default));
            }

            statementsWithVacantConstruction.AddRange(bodyStatements);
            bodyStatements = statementsWithVacantConstruction
                .MoveToImmutable();
        }

        return declaration with {
            Variables = variables.ToImmutable(),
            Body = new BoundBlock(bodyStatements, default),
            ConstructionHomeIndex = constructionHomeIndex,
        };

        static void RegisterHomeRead(LocalAggregateLayoutBodyState state,
            int destination, int homeIndex)
        {
            if ((uint)homeIndex >= (uint)state.HomeVariable.Length)
            {
                throw Invalid("An L1 HomeLoad index is outside the HomeMap.");
            }

            var variable = state.HomeVariable[homeIndex];
            if (variable < 0)
            {
                throw Invalid("An L1 HomeLoad has no reconstructed variable.");
            }

            StoreLocalAggregateRegisterValue(state, destination,
                new BoundRead(variable,
                    state.Variables[variable].Type, default));
        }

        static BoundExpression ReadRegisterOrContext(
            TrRmRegisterBodyReader body,
            LocalAggregateLayoutBodyState state,
            int register, int homeIndex)
        {
            if (state.RegisterValues.TryGetValue(register, out var value))
            {
                return value;
            }

            if (register is not (14 or 15)
                || (uint)homeIndex >= (uint)state.HomeVariable.Length)
            {
                throw Invalid(
                    "An L1 HomeStore reads an undefined value register.");
            }

            var variable = state.HomeVariable[homeIndex];
            if (variable < 0
                || !TryGetContextualConstant(checked((byte)register),
                    state.Variables[variable].Type, body.Schema,
                    out var contextualValue))
            {
                throw Invalid(
                    "An L1 contextual HomeStore has no selected scalar value.");
            }

            return new BoundConstant(contextualValue,
                state.Variables[variable].Type, default);
        }

        static void StoreHome(LocalAggregateLayoutBodyState state,
            int homeIndex, BoundExpression value)
        {
            if ((uint)homeIndex >= (uint)state.HomeVariable.Length
                || homeIndex >= state.FormalHomeStart)
            {
                throw Invalid(
                    "An L1 HomeStore does not select local storage.");
            }

            var variable = state.HomeVariable[homeIndex];
            if (variable < 0 || value.Type != state.Variables[variable].Type)
            {
                throw Invalid(
                    "An L1 HomeStore source differs from its destination type.");
            }

            if (state.UseFixedPointHomeState)
            {
                state.HomeStateActions.Add(
                    new LocalAggregateLayoutHomeAction(homeIndex,
                        LocalAggregateLayoutHomeActionKind
                            .InitializeOrReplace, 0, 0));
                state.Statements.Add(new BoundAssignment(variable, value,
                    default));
            }
            else if (state.InitializedHomes.Contains(homeIndex))
            {
                state.Statements.Add(new BoundAssignment(variable, value,
                    default));
            }
            else if (state.PossiblyInitializedHomes.Contains(homeIndex))
            {
                if (!state.Variables[variable].IsMutable)
                {
                    throw Invalid(
                        "An L1 HomeStore may initialize or replace a maybe-live Home only when the local is mutable.");
                }

                _ = state.InitializedHomes.Add(homeIndex);
                _ = state.ConditionalInitializationHomes.Add(variable);
                state.Statements.Add(new BoundAssignment(variable, value,
                    default));
            }
            else
            {
                _ = state.InitializedHomes.Add(homeIndex);
                _ = state.PossiblyInitializedHomes.Add(homeIndex);
                if (state.CurrentBlockIndex == 0)
                {
                    state.Statements.Add(new BoundLocal(variable, value,
                        default));
                }
                else
                {
                    _ = state.ConditionalInitializationHomes.Add(variable);
                    state.Statements.Add(new BoundAssignment(variable, value,
                        default));
                }
            }

            if (value is BoundConstant constant)
            {
                state.HomeConstants[variable] = constant;
            }
            else if (value is BoundRead read
                && state.HomeConstants.TryGetValue(read.Variable,
                    out var copiedConstant))
            {
                state.HomeConstants[variable] = copiedConstant;
            }
            else
            {
                _ = state.HomeConstants.Remove(variable);
            }
        }

        static BoundExpression ReadConstant(
            TrRmRegisterBodyReader body,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            LocalAggregateLayoutBodyState state,
            int constantMapIndex)
        {
            if (constantMapIndex >= body.ConstantCount)
            {
                throw Invalid(
                    "An L1 ConstantMap reference is outside its table.");
            }

            var offset = constantMapIndex * 8;
            var typeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(
                body.ConstantMap[offset..]);
            var constantSourceRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    body.ConstantMap[(offset + 4)..]));
            return TrRmConstantSourceValueValidator.MaterializeFrozenScalar(
                constantSourceRow, typeSpecRef, state.SelectedModuleRow,
                tables.Modules.GetRequirements(state.SelectedModuleRow)
                    .TargetRequirement,
                tables.ConstantSources, tables.FrozenValueNodes,
                tables.Blobs, tables.TypeSpecs, tables.Features,
                tables.Modules, tables.TypeDefs, tables.EnumDefs,
                tables.Imports, tables.A3LayoutResourceLedger);
        }

        static LocalAggregateLayoutSelection ReadSelectionRecord(
            TrRmRegisterBodyReader body,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            LocalAggregateLayoutBodyState state,
            int recordIndex)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x04 || record.Length != 24)
            {
                throw Invalid(
                    "An L1 place operation does not reference a PlaceSelection record.");
            }

            var targetKind = record[4];
            var accessKind = record[5];
            var destinationKind = record[6];
            var baseKind = record[7];
            var destinationIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
            var baseIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
            var indexKind = record[12];
            var targetRefKind = record[13];
            var indexIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(record[14..]);
            var targetRef
                = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            var referentTypeMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(record[20..]);
            var referentType = ReadLocalAggregateType(tables,
                ReadLocalAggregateBodyTypeSpec(body,
                    referentTypeMapIndex), state.TypeEnvironment);

            if (destinationKind != 1 || destinationIndex > 13
                || targetKind is < 1 or > 3)
            {
                throw Invalid(
                    "An L1 PlaceSelection has an unsupported destination or target.");
            }

            if (targetKind == 1)
            {
                if (accessKind != 3 || baseKind != 0
                    || baseIndex != 0 || indexKind != 0
                    || targetRefKind != 0 || indexIndex != 0
                    || targetRef >= body.HomeCount)
                {
                    throw Invalid(
                        "An L1 Home PlaceSelection has an invalid target shape.");
                }

                var homeIndex = checked((int)targetRef);
                if (homeIndex >= state.FormalHomeStart
                    || state.HomeVariable[homeIndex] < 0
                    || state.Variables[state.HomeVariable[homeIndex]].Type
                        != referentType)
                {
                    throw Invalid(
                        "An L1 Home PlaceSelection has a mismatched referent.");
                }

                return new LocalAggregateLayoutSelection(targetKind,
                    accessKind, destinationIndex,
                    state.HomeVariable[homeIndex], referentType, [],
                    null, null, null, homeIndex)
                {
                    SelectedValue = new BoundRead(
                        state.HomeVariable[homeIndex], referentType,
                        default),
                };
            }

            if (baseKind is not (1 or 2))
            {
                throw Invalid("An L1 aggregate PlaceSelection has no base.");
            }

            int rootVariable;
            BoundExpression? baseValue;
            ImmutableArray<int> fieldPath;
            BoundExpression? fieldPlace;
            ImmutableArray<BoundNativeFixedArrayProjection> arrayPath;
            ImmutableArray<BoundNativeFixedArrayFieldProjection> leafFields;
            PrimitiveType baseType;
            LocalAggregateLayoutSelection? parentSelection = null;
            if (baseKind == 2)
            {
                if (baseIndex >= state.HomeVariable.Length
                    || state.HomeVariable[baseIndex] < 0)
                {
                    throw Invalid(
                        "An L1 PlaceSelection base Home is invalid.");
                }

                rootVariable = state.HomeVariable[baseIndex];
                baseType = state.Variables[rootVariable].Type;
                baseValue = new BoundRead(rootVariable, baseType, default);
                fieldPath = [];
                fieldPlace = null;
                arrayPath = [];
                leafFields = [];
            }
            else
            {
                if (baseIndex > 13
                    || !state.Selections.TryGetValue(baseIndex,
                        out var selectedParent)
                    || selectedParent.RootVariable < 0
                    || selectedParent.SelectedValue is null)
                {
                    throw Invalid(
                        "An L1 nested PlaceSelection has no captured field base.");
                }

                parentSelection = CloneLocalAggregateSelectionForRegister(
                    state, selectedParent, destinationIndex);
                rootVariable = parentSelection.RootVariable;
                baseType = parentSelection.ReferentType;
                baseValue = parentSelection.SelectedValue;
                fieldPath = parentSelection.FieldPath;
                fieldPlace = parentSelection.FieldPlace;
                arrayPath = parentSelection.ArrayPath;
                leafFields = parentSelection.LeafFields;
            }

            if (targetKind == 2)
            {
                if (indexKind != 0 || targetRefKind != 1
                    || indexIndex != 0 || targetRef > int.MaxValue)
                {
                    throw Invalid(
                        "An L1 field PlaceSelection has an invalid FieldDef reference.");
                }

                var fieldRow = checked((int)targetRef);
                var fieldOrdinal = GetLocalAggregateFieldOrdinal(
                    tables, fieldRow, baseType, state.TypeEnvironment);
                var fieldType = ReadLocalAggregateType(tables,
                    tables.Fields.GetTypeSpecRef(fieldRow),
                    state.TypeEnvironment);
                if (fieldType != referentType
                    || fieldOrdinal < 0
                    || baseValue is null)
                {
                    throw Invalid(
                        "An L1 field PlaceSelection does not match its FieldDef owner and type.");
                }

                var selectedFieldValue
                    = new BoundFieldRead(baseValue, fieldOrdinal,
                        fieldType, default)
                    {
                        IsUnsafeAuthorized
                            = IsLocalAggregateUnionType(baseType,
                                state.TypeEnvironment),
                    };
                var selectedLeafFields = arrayPath.IsEmpty
                    ? leafFields
                    : leafFields.Add(
                        new BoundNativeFixedArrayFieldProjection(
                            baseType, fieldOrdinal));
                return new LocalAggregateLayoutSelection(targetKind,
                    accessKind, destinationIndex, rootVariable, referentType,
                    fieldPath.Add(fieldOrdinal),
                    fieldPlace, null, null, -1)
                {
                    SelectedValue = selectedFieldValue,
                    ArrayPath = arrayPath,
                    LeafFields = selectedLeafFields,
                };
            }

            if (targetRefKind != 3 || indexKind is not (1 or 2)
                || targetRef > int.MaxValue)
            {
                throw Invalid(
                    "An L1 element PlaceSelection has an invalid fixed-array target.");
            }

            var layoutRow = checked((int)targetRef);
            var fixedArrayLayout
                = tables.FixedArrayLayouts.GetRow(layoutRow);
            if (fixedArrayLayout.OwnerModuleRow != state.SelectedModuleRow
                || fixedArrayLayout.TargetProfileRef
                    != (uint)state.TypeEnvironment.TargetProfileRow
                || !state.TypeEnvironment.ArrayTypeByTypeSpecRow.TryGetValue(
                    fixedArrayLayout.ArrayTypeSpecRow,
                    out var arrayType)
                || baseType != arrayType
                || !tables.TypeSpecs.TryGetFixedInlineArray(
                    checked((uint)fixedArrayLayout.ArrayTypeSpecRow),
                    out var elementTypeSpecRef, out var elementCount)
                || !tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                    ReadLocalAggregateBodyTypeSpec(body,
                        referentTypeMapIndex), elementTypeSpecRef))
            {
                throw Invalid(
                    "An L1 element PlaceSelection does not match its FixedArrayLayout.");
            }

            var arrayDefinition = state.TypeEnvironment.FixedArrayTypes[
                NativeFixedArrayTypes.Index(arrayType)];
            if (arrayDefinition.ElementType != referentType
                || arrayDefinition.ElementCount != elementCount
                || fixedArrayLayout.ElementStride
                    != arrayDefinition.ElementStride
                || fixedArrayLayout.ByteExtent != arrayDefinition.ByteExtent
                || fixedArrayLayout.Alignment != arrayDefinition.Alignment)
            {
                throw Invalid(
                    "An L1 element PlaceSelection has mismatched derived array geometry.");
            }

            BoundExpression indexExpression;
            if (indexKind == 2)
            {
                if (indexIndex >= state.HomeVariable.Length
                    || state.HomeVariable[indexIndex] < 0)
                {
                    throw Invalid(
                        "An L1 fixed-array index Home is invalid.");
                }

                var indexVariable = state.HomeVariable[indexIndex];
                indexExpression = new BoundRead(indexVariable,
                    state.Variables[indexVariable].Type, default);
            }
            else if (indexIndex <= 13
                && state.RegisterValues.TryGetValue(indexIndex,
                    out var registerIndex))
            {
                indexExpression = registerIndex;
            }
            else
            {
                throw Invalid(
                    "An L1 fixed-array index register is undefined.");
            }

            indexExpression = CaptureLocalAggregatePlaceIndex(state,
                destinationIndex, arrayPath.Length, indexExpression);

            if (!NativeFixedArrayContract.IsIndexType(
                    indexExpression.Type))
            {
                throw Invalid(
                    "An L1 fixed-array index does not use an admitted integer type.");
            }

            var layout = new BoundNativeFixedArrayLayout(arrayType,
                arrayDefinition.ElementType, arrayDefinition.ElementCount,
                arrayDefinition.ElementStride, arrayDefinition.ByteExtent,
                arrayDefinition.Alignment,
                arrayDefinition.RequiredAlignment);
            var arrayBasePath
                = ImmutableArray<BoundNativeFixedArrayProjection>.Empty;
            var arrayBaseFieldPath = ImmutableArray<int>.Empty;
            var arrayBaseLeafFields
                = ImmutableArray<BoundNativeFixedArrayFieldProjection>.Empty;
            BoundExpression? arrayBaseFieldPlace = null;
            if (parentSelection is { TargetKind: 2 } fieldBase)
            {
                arrayBaseFieldPlace = fieldBase.FieldPlace;
                if (fieldBase.ArrayPath.IsEmpty)
                {
                    arrayBaseFieldPath = fieldBase.FieldPath;
                }
                else
                {
                    arrayBasePath = fieldBase.ArrayPath;
                    arrayBaseLeafFields = fieldBase.LeafFields;
                }

                fieldPlace = fieldBase.SelectedValue;
                arrayPath = [];
                fieldPath = [];
                leafFields = [];
            }
            else if (parentSelection is { TargetKind: 3 } elementBase)
            {
                arrayBasePath = elementBase.ArrayPath;
                arrayBaseLeafFields = elementBase.LeafFields;
                arrayBaseFieldPlace = elementBase.FieldPlace;
            }

            var selectedArrayPath = arrayPath.Add(
                new BoundNativeFixedArrayProjection(layout,
                    indexExpression));
            BoundExpression selectedElementValue
                = selectedArrayPath.Length == 1
                    ? new BoundNativeFixedArrayElement(rootVariable,
                        layout, indexExpression, default)
                    {
                        FieldPlace = fieldPlace,
                    }
                    : new BoundNativeFixedArrayPathElement(rootVariable,
                        selectedArrayPath, referentType, default)
                    {
                        FieldPlace = fieldPlace,
                    };
            return new LocalAggregateLayoutSelection(targetKind,
                accessKind, destinationIndex, rootVariable, referentType,
                [], fieldPlace, layout, indexExpression, -1)
            {
                SelectedValue = selectedElementValue,
                ArrayPath = selectedArrayPath,
                LeafFields = [],
                ArrayBasePath = arrayBasePath,
                ArrayBaseFieldPath = arrayBaseFieldPath,
                ArrayBaseLeafFields = arrayBaseLeafFields,
                ArrayBaseFieldPlace = arrayBaseFieldPlace,
            };
        }

        static BoundExpression ReadSelection(
            LocalAggregateLayoutSelection selection)
        {
            return selection.SelectedValue
                        ?? throw Invalid(
                            "An L1 PlaceSelection has no reconstructed value path.");
        }

        static void AppendSemanticStore(
            TrRmRegisterBodyReader body,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            LocalAggregateLayoutBodyState state,
            int recordIndex)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x05 || record.Length != 20)
            {
                throw Invalid(
                    "An L1 SemanticStore instruction does not reference a SemanticStore record.");
            }

            var payload = record[4..];
            var placeKind = payload[0];
            var sourceKind = payload[1];
            var transition = payload[2];
            var sourceUse = payload[3];
            var placeIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
            var sourceIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
            if (placeKind != 1 || transition is not (1 or 2)
                || sourceUse is not (0 or 1 or 6)
                || payload[12] != 0 || payload[13] != 0
                || BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]) != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(payload[14..]) != 0
                || placeIndex > 13
                || !state.Selections.TryGetValue(placeIndex,
                    out var selection)
                || selection.DestinationRegister != placeIndex)
            {
                throw Invalid(
                    "An L1 SemanticStore has an unsupported selector, authority or dependency.");
            }

            if (sourceKind == 0)
            {
                if (sourceUse != 0 || transition != 1
                    || sourceIndex != 0
                    || selection.AccessKind != 3
                    || selection.TargetKind is not (1 or 2 or 3)
                    || !NativeFixedArrayTypes.IsType(
                        selection.ReferentType)
                    || state.TypeEnvironment.FixedArrayTypes[
                        NativeFixedArrayTypes.Index(
                            selection.ReferentType)].ElementCount != 0)
                {
                    throw Invalid(
                        "An L1 None SemanticStore is only the exact empty-array initialization form.");
                }

                var emptyInitialization
                    = new BoundNativeFixedArrayInitialization(
                        GetLocalAggregateArrayLayout(
                            state.TypeEnvironment,
                            selection.ReferentType),
                        [], default);
                if (selection.TargetKind == 1)
                {
                    var variable = selection.RootVariable;
                    var homeIndex = variable - state.ParameterCount;
                    if (variable < state.ParameterCount
                        || homeIndex >= state.FormalHomeStart
                        || (!state.UseFixedPointHomeState
                            && state.PossiblyInitializedHomes
                                .Contains(homeIndex)))
                    {
                        throw Invalid(
                            "An L1 empty-array Home store does not initialize vacant local storage.");
                    }

                    if (state.UseFixedPointHomeState)
                    {
                        state.HomeStateActions.Add(
                            new LocalAggregateLayoutHomeAction(homeIndex,
                                LocalAggregateLayoutHomeActionKind.Initialize,
                                0, 0));
                    }
                    else
                    {
                        _ = state.InitializedHomes.Add(homeIndex);
                        _ = state.PossiblyInitializedHomes.Add(homeIndex);
                    }
                    _ = state.HomeConstants.Remove(variable);
                    if (state.UseFixedPointHomeState)
                    {
                        state.Statements.Add(new BoundAssignment(variable,
                            emptyInitialization, default));
                    }
                    else if (state.CurrentBlockIndex == 0)
                    {
                        state.Statements.Add(new BoundLocal(variable,
                            emptyInitialization, default));
                    }
                    else
                    {
                        _ = state.ConditionalInitializationHomes.Add(
                            variable);
                        state.Statements.Add(new BoundAssignment(variable,
                            emptyInitialization, default));
                    }
                }
                else if (selection.TargetKind == 2)
                {
                    if (selection.FieldPath.IsEmpty)
                    {
                        throw Invalid(
                            "An L1 empty-array field store has no FieldDef path.");
                    }

                    if (selection.ArrayPath.IsEmpty)
                    {
                        state.Statements.Add(new BoundFieldAssignment(
                            selection.RootVariable,
                            selection.FieldPath[0],
                            emptyInitialization, default)
                        {
                            NestedFields = [.. selection.FieldPath.Skip(1)],
                            IsUnsafeAuthorized
                                = IsLocalAggregateUnsafeSelection(selection,
                                    state),
                        });
                    }
                    else
                    {
                        state.Statements.Add(
                            new BoundExpressionStatement(
                                new BoundNativeFixedArrayPathWrite(
                                    selection.RootVariable,
                                    selection.ArrayPath,
                                    emptyInitialization, default)
                                {
                                    FieldPlace = selection.FieldPlace,
                                    LeafFields = selection.LeafFields,
                                    IsUnsafeAuthorized
                                        = IsLocalAggregateUnsafeSelection(
                                            selection, state),
                                }, default));
                    }
                }
                else
                {
                    if (selection.ArrayLayout?.ElementType
                        != selection.ReferentType)
                    {
                        throw Invalid(
                            "An L1 nullary element store does not select an empty-array element.");
                    }

                    AppendExplicitArrayInitializationElement(body, state,
                        selection, emptyInitialization);
                }

                _ = state.Selections.Remove(placeIndex);
                return;
            }

            BoundExpression source;
            if (sourceKind == 1)
            {
                if (sourceIndex <= 13)
                {
                    if (!state.RegisterValues.TryGetValue(sourceIndex,
                            out source!))
                    {
                        throw Invalid(
                            "An L1 SemanticStore source register is undefined.");
                    }
                }
                else if (sourceIndex is 14 or 15
                    && TryGetContextualConstant(
                        checked((byte)sourceIndex),
                        sourceUse == 6
                            && NativeFixedArrayTypes.IsType(
                                selection.ReferentType)
                            ? state.TypeEnvironment.FixedArrayTypes[
                                NativeFixedArrayTypes.Index(
                                    selection.ReferentType)].ElementType
                            : selection.ReferentType,
                        body.Schema,
                        out var contextualValue))
                {
                    var contextualType = sourceUse == 6
                        && NativeFixedArrayTypes.IsType(
                            selection.ReferentType)
                        ? state.TypeEnvironment.FixedArrayTypes[
                            NativeFixedArrayTypes.Index(
                                selection.ReferentType)].ElementType
                        : selection.ReferentType;
                    source = PrimitiveTypeFacts.ScalarWidth(
                            contextualType) > sizeof(ulong)
                        ? new BoundConstant(new ScalarBits(0, 0),
                            contextualType, default)
                        : new BoundConstant(contextualValue,
                            contextualType, default);
                }
                else
                {
                    throw Invalid(
                        "An L1 SemanticStore contextual source is invalid for its selected type.");
                }
            }
            else if (sourceKind == 2)
            {
                if (sourceIndex >= state.HomeVariable.Length
                    || state.HomeVariable[sourceIndex] < 0)
                {
                    throw Invalid(
                        "An L1 SemanticStore source Home is invalid.");
                }

                var sourceVariable = state.HomeVariable[sourceIndex];
                source = new BoundRead(sourceVariable,
                    state.Variables[sourceVariable].Type, default);
            }
            else
            {
                throw Unsupported(
                    "L1 SemanticStore sources must be a value register or Home.");
            }

            if (sourceUse == 6)
            {
                if (transition != 1 || selection.AccessKind != 3
                    || !NativeFixedArrayTypes.IsType(
                        selection.ReferentType))
                {
                    throw Invalid(
                        "The L1 RepeatCopy store is not a fixed-array initialization.");
                }

                var arrayType = selection.ReferentType;
                var arrayDefinition = state.TypeEnvironment.FixedArrayTypes[
                    NativeFixedArrayTypes.Index(arrayType)];
                if (arrayDefinition.ElementType != source.Type)
                {
                    throw Invalid(
                        "An L1 RepeatCopy seed differs from the selected array element type.");
                }

                var repeat = new BoundNativeFixedArrayRepeatInitialization(
                    new BoundNativeFixedArrayLayout(arrayType,
                        arrayDefinition.ElementType,
                        arrayDefinition.ElementCount,
                        arrayDefinition.ElementStride,
                        arrayDefinition.ByteExtent,
                        arrayDefinition.Alignment,
                        arrayDefinition.RequiredAlignment), source, default);
                if (selection.TargetKind == 1)
                {
                    var variable = selection.RootVariable;
                    if (variable < state.ParameterCount
                        || variable >= state.Variables.Count
                        || state.Variables[variable].Type != repeat.Type)
                    {
                        throw Invalid(
                            "An L1 RepeatCopy store has no matching Home destination.");
                    }

                    var homeIndex = variable - state.ParameterCount;
                    if (!state.UseFixedPointHomeState
                        && state.PossiblyInitializedHomes.Contains(homeIndex))
                    {
                        throw Invalid(
                            "An L1 RepeatCopy Home target is not vacant.");
                    }

                    if (state.UseFixedPointHomeState)
                    {
                        state.HomeStateActions.Add(
                            new LocalAggregateLayoutHomeAction(homeIndex,
                                LocalAggregateLayoutHomeActionKind.Initialize,
                                0, 0));
                    }
                    else
                    {
                        _ = state.InitializedHomes.Add(homeIndex);
                        _ = state.PossiblyInitializedHomes.Add(homeIndex);
                    }
                    _ = state.HomeConstants.Remove(variable);
                    if (state.UseFixedPointHomeState)
                    {
                        state.Statements.Add(new BoundAssignment(variable,
                            repeat, default));
                    }
                    else if (state.CurrentBlockIndex == 0)
                    {
                        state.Statements.Add(new BoundLocal(variable, repeat,
                            default));
                    }
                    else
                    {
                        _ = state.ConditionalInitializationHomes.Add(
                            variable);
                        state.Statements.Add(new BoundAssignment(variable,
                            repeat, default));
                    }
                }
                else if (selection.TargetKind == 2)
                {
                    if (selection.FieldPath.IsEmpty)
                    {
                        throw Invalid(
                            "An L1 RepeatCopy field store has no FieldDef path.");
                    }

                    if (selection.ArrayPath.IsEmpty)
                    {
                        state.Statements.Add(new BoundFieldAssignment(
                            selection.RootVariable,
                            selection.FieldPath[0], repeat, default)
                        {
                            NestedFields = [.. selection.FieldPath.Skip(1)],
                            IsUnsafeAuthorized
                                = IsLocalAggregateUnsafeSelection(selection,
                                    state),
                        });
                    }
                    else
                    {
                        state.Statements.Add(
                            new BoundExpressionStatement(
                                new BoundNativeFixedArrayPathWrite(
                                    selection.RootVariable,
                                    selection.ArrayPath, repeat, default)
                                {
                                    FieldPlace = selection.FieldPlace,
                                    LeafFields = selection.LeafFields,
                                    IsUnsafeAuthorized
                                        = IsLocalAggregateUnsafeSelection(
                                            selection, state),
                                }, default));
                    }
                }
                else if (selection.TargetKind == 3)
                {
                    if (selection.ArrayLayout?.ElementType
                        != selection.ReferentType)
                    {
                        throw Invalid(
                            "An L1 RepeatCopy element store does not select an array-valued element.");
                    }

                    AppendExplicitArrayInitializationElement(body, state,
                        selection, repeat);
                }
                else
                {
                    throw Unsupported(
                        "The L1 RepeatCopy place target is unsupported.");
                }

                _ = state.Selections.Remove(placeIndex);
                return;
            }

            if (selection.ReferentType != source.Type)
            {
                throw Invalid(
                    "An L1 SemanticStore source differs from its selected referent type.");
            }

            if ((transition == 1 && selection.AccessKind != 3)
                || (transition == 2 && selection.AccessKind != 2))
            {
                throw Invalid(
                    "An L1 SemanticStore transition disagrees with its PlaceSelection authority.");
            }

            if (selection.TargetKind == 2)
            {
                if (selection.FieldPath.IsEmpty)
                {
                    throw Invalid(
                        "An L1 field SemanticStore has no FieldDef path.");
                }

                if (selection.ArrayPath.IsEmpty)
                {
                    var assignment = new BoundFieldAssignment(
                        selection.RootVariable, selection.FieldPath[0],
                        source, default)
                    {
                        NestedFields = [.. selection.FieldPath.Skip(1)],
                        IsUnsafeAuthorized
                            = IsLocalAggregateUnsafeSelection(selection,
                                state),
                    };
                    state.Statements.Add(assignment);
                }
                else
                {
                    var assignment = new BoundNativeFixedArrayPathWrite(
                        selection.RootVariable, selection.ArrayPath,
                        source, default)
                    {
                        FieldPlace = selection.FieldPlace,
                        LeafFields = selection.LeafFields,
                        IsUnsafeAuthorized
                            = IsLocalAggregateUnsafeSelection(selection,
                                state),
                    };
                    state.Statements.Add(new BoundExpressionStatement(
                        assignment, default));
                }
            }
            else if (selection.TargetKind == 3)
            {
                if (transition == 1)
                {
                    AppendExplicitArrayInitializationElement(body, state,
                        selection, source);
                }
                else
                {
                    var write = selection.ArrayPath.Length == 1
                        ? (BoundExpression)new BoundNativeFixedArrayWrite(
                            selection.RootVariable,
                            selection.ArrayLayout
                                ?? throw Invalid(
                                    "An L1 element SemanticStore has no array layout."),
                            selection.Index
                                ?? throw Invalid(
                                    "An L1 element SemanticStore has no captured index."),
                            source, default)
                        {
                            FieldPlace = selection.FieldPlace,
                        }
                        : new BoundNativeFixedArrayPathWrite(
                            selection.RootVariable, selection.ArrayPath,
                            source, default)
                        {
                            FieldPlace = selection.FieldPlace,
                            LeafFields = selection.LeafFields,
                            IsUnsafeAuthorized
                                = IsLocalAggregateUnsafeSelection(selection,
                                    state),
                        };
                    state.Statements.Add(new BoundExpressionStatement(write,
                        default));
                }
            }
            else
            {
                throw Unsupported(
                    "The L1 SemanticStore target is outside the admitted field/element subset.");
            }

            _ = state.Selections.Remove(placeIndex);
        }

        static void AppendExplicitArrayInitializationElement(
            TrRmRegisterBodyReader body,
            LocalAggregateLayoutBodyState state,
            LocalAggregateLayoutSelection selection,
            BoundExpression source)
        {
            BoundConstant? indexConstant = null;
            if (selection.Index is BoundConstant directIndex)
            {
                indexConstant = directIndex;
            }
            else if (selection.Index is BoundRead { Variable: var indexVariable }
                && state.HomeConstants.TryGetValue(indexVariable,
                    out var capturedIndex))
            {
                indexConstant = capturedIndex;
            }

            if (selection.AccessKind != 3
                || selection.ArrayLayout is not BoundNativeFixedArrayLayout layout
                || indexConstant is null
                || !NativeFixedArrayContract.TryGetUnsignedIndex(
                    indexConstant, out var elementIndex)
                || elementIndex >= layout.ElementCount
                || layout.ElementCount > (ulong)body.RecordCount
                || source.Type != layout.ElementType)
            {
                throw Unsupported(
                    "L1 explicit-list initialization requires source-ordered stores to a captured constant element index.");
            }

            var initializesLocalHome
                = selection.ArrayBasePath.IsEmpty
                    && selection.ArrayBaseFieldPath.IsEmpty;
            if (initializesLocalHome
                && ((uint)selection.RootVariable
                        >= (uint)state.Variables.Count
                    || state.Variables[selection.RootVariable].Type
                        != layout.CarrierType))
            {
                throw Invalid(
                    "An L1 explicit-list target does not match its complete array carrier type.");
            }

            var pending = state.PendingArrayInitializations.FirstOrDefault(
                item => item.RootVariable == selection.RootVariable
                    && item.Layout == layout
                    && item.ArrayBasePath.SequenceEqual(
                        selection.ArrayBasePath)
                    && item.ArrayBaseFieldPath.SequenceEqual(
                        selection.ArrayBaseFieldPath)
                    && item.ArrayBaseLeafFields.SequenceEqual(
                        selection.ArrayBaseLeafFields)
                    && Equals(item.ArrayBaseFieldPlace,
                        selection.ArrayBaseFieldPlace));
            if (pending is null)
            {
                pending = new LocalAggregateArrayInitialization {
                    RootVariable = selection.RootVariable,
                    Layout = layout,
                    ArrayBasePath = selection.ArrayBasePath,
                    ArrayBaseFieldPath = selection.ArrayBaseFieldPath,
                    ArrayBaseLeafFields = selection.ArrayBaseLeafFields,
                    ArrayBaseFieldPlace = selection.ArrayBaseFieldPlace,
                    StatementCountAtStart = state.Statements.Count,
                };
                state.PendingArrayInitializations.Add(pending);
            }
            for (var statementIndex = pending.StatementCountAtStart;
                statementIndex < state.Statements.Count; statementIndex++)
            {
                if (state.Statements[statementIndex]
                    is not BoundLocal {
                        Initializer: BoundCall or BoundRead or BoundConstant
                            or BoundFieldRead
                            or BoundNativeFixedArrayElement,
                    })
                {
                    throw Unsupported(
                        "L1 explicit-list initialization cannot interleave stateful operations between element captures.");
                }
            }

            if (elementIndex != pending.NextElementIndex
                || !pending.Elements.TryAdd(elementIndex, source))
            {
                throw Invalid(
                    "L1 explicit-list stores must cover each element once in source order.");
            }

            pending.NextElementIndex = checked(elementIndex + 1);
            if ((ulong)pending.Elements.Count < layout.ElementCount)
            {
                return;
            }
            if ((ulong)pending.Elements.Count != layout.ElementCount
                || layout.ElementCount > int.MaxValue)
            {
                throw Unsupported(
                    "The L1 explicit-list Body does not provide bounded complete element coverage.");
            }

            var elements = ImmutableArray.CreateBuilder<BoundExpression>(
                pending.Elements.Count);
            for (var index = 0; index < pending.Elements.Count; index++)
            {
                if (!pending.Elements.TryGetValue((ulong)index,
                        out var element))
                {
                    throw Invalid(
                        "An L1 explicit-list Body has an uncovered element.");
                }

                elements.Add(element);
            }

            var initialization = new BoundNativeFixedArrayInitialization(
                layout, elements.MoveToImmutable(), default);
            if (pending.ArrayBasePath.IsEmpty
                && pending.ArrayBaseFieldPath.IsEmpty)
            {
                var homeIndex = pending.RootVariable
                    - state.ParameterCount;
                if (pending.RootVariable < state.ParameterCount
                    || homeIndex >= state.FormalHomeStart
                    || state.HomeVariable[homeIndex]
                        != pending.RootVariable
                    || (!state.UseFixedPointHomeState
                        && state.PossiblyInitializedHomes
                            .Contains(homeIndex)))
                {
                    throw Invalid(
                        "An L1 explicit-list local target is not a vacant local Home.");
                }

                if (state.UseFixedPointHomeState)
                {
                    state.HomeStateActions.Add(
                        new LocalAggregateLayoutHomeAction(homeIndex,
                            LocalAggregateLayoutHomeActionKind.Initialize,
                            0, 0));
                    state.Statements.Add(new BoundAssignment(
                        pending.RootVariable, initialization, default));
                }
                else
                {
                    _ = state.InitializedHomes.Add(homeIndex);
                    _ = state.PossiblyInitializedHomes.Add(homeIndex);
                    if (state.CurrentBlockIndex == 0)
                    {
                        state.Statements.Add(new BoundLocal(
                            pending.RootVariable, initialization, default));
                    }
                    else
                    {
                        _ = state.ConditionalInitializationHomes.Add(
                            pending.RootVariable);
                        state.Statements.Add(new BoundAssignment(
                            pending.RootVariable, initialization, default));
                    }
                }
            }
            else if (!pending.ArrayBaseFieldPath.IsEmpty
                && pending.ArrayBasePath.IsEmpty)
            {
                state.Statements.Add(new BoundFieldAssignment(
                    pending.RootVariable,
                    pending.ArrayBaseFieldPath[0],
                    initialization, default)
                {
                    NestedFields = [.. pending.ArrayBaseFieldPath.Skip(1)],
                    IsUnsafeAuthorized
                        = IsLocalAggregateUnsafeFieldPath(
                            state.Variables[pending.RootVariable].Type,
                            pending.ArrayBaseFieldPath,
                            state.TypeEnvironment),
                });
            }
            else
            {
                state.Statements.Add(new BoundExpressionStatement(
                    new BoundNativeFixedArrayPathWrite(
                        pending.RootVariable, pending.ArrayBasePath,
                        initialization, default)
                    {
                        FieldPlace = pending.ArrayBaseFieldPlace,
                        LeafFields = pending.ArrayBaseLeafFields,
                        IsUnsafeAuthorized
                            = IsLocalAggregateUnsafeFieldPath(
                                state.Variables[pending.RootVariable].Type,
                                pending.ArrayBaseFieldPath,
                                state.TypeEnvironment)
                            || HasLocalAggregateUnionOwner(
                                pending.ArrayBaseLeafFields,
                                state.TypeEnvironment),
                    }, default));
            }

            _ = state.PendingArrayInitializations.Remove(pending);
        }

        void AppendPlaceStore(LocalAggregateLayoutSelection selection,
            BoundExpression value, byte transition)
        {
            if (selection.ReferentType != value.Type
                || selection.AccessKind != 2 || transition != 2)
            {
                throw Invalid(
                    "An L1 direct place store is not a typed replacement.");
            }

            if (selection.TargetKind == 2)
            {
                if (selection.FieldPath.IsEmpty)
                {
                    throw Invalid(
                        "An L1 field replacement has no FieldDef path.");
                }

                if (selection.ArrayPath.IsEmpty)
                {
                    state.Statements.Add(new BoundFieldAssignment(
                        selection.RootVariable, selection.FieldPath[0],
                        value, default)
                    {
                        NestedFields = [.. selection.FieldPath.Skip(1)],
                        IsUnsafeAuthorized
                            = IsLocalAggregateUnsafeSelection(selection, state),
                    });
                }
                else
                {
                    state.Statements.Add(new BoundExpressionStatement(
                        new BoundNativeFixedArrayPathWrite(
                            selection.RootVariable, selection.ArrayPath,
                            value, default)
                        {
                            FieldPlace = selection.FieldPlace,
                            LeafFields = selection.LeafFields,
                            IsUnsafeAuthorized
                                = IsLocalAggregateUnsafeSelection(selection,
                                    state),
                        }, default));
                }
            }
            else if (selection.TargetKind == 3)
            {
                var write = selection.ArrayPath.Length == 1
                    ? (BoundExpression)new BoundNativeFixedArrayWrite(
                        selection.RootVariable,
                        selection.ArrayLayout
                            ?? throw Invalid(
                                "An L1 element replacement has no array layout."),
                        selection.Index
                            ?? throw Invalid(
                                "An L1 element replacement has no index."),
                        value, default)
                    {
                        FieldPlace = selection.FieldPlace,
                    }
                    : new BoundNativeFixedArrayPathWrite(
                        selection.RootVariable, selection.ArrayPath,
                        value, default)
                    {
                        FieldPlace = selection.FieldPlace,
                        LeafFields = selection.LeafFields,
                        IsUnsafeAuthorized
                            = IsLocalAggregateUnsafeSelection(selection, state),
                    };
                state.Statements.Add(new BoundExpressionStatement(write,
                    default));
            }
            else
            {
                throw Unsupported(
                    "The L1 direct place store target is outside the admitted field/element subset.");
            }
        }

        static void AppendCall(TrRmRegisterBodyReader body,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            LocalAggregateLayoutBodyState state, int recordIndex)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x02 || record.Length < 20)
            {
                throw Invalid(
                    "An L1 CallSite instruction does not reference a CallSite record.");
            }

            var targetKind = record[4];
            var resultKind = record[5];
            var methodMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
            var resultIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
            var inputCount = BinaryPrimitives.ReadUInt32LittleEndian(
                record[12..]);
            var preparationCount = BinaryPrimitives.ReadUInt32LittleEndian(
                record[16..]);
            if (targetKind != 1 || preparationCount > 1)
            {
                throw Unsupported(
                    "L1 CallSites require direct local MethodDef targets and at most one result-Home preparation.");
            }
            if (record[6] != 0 || record[7] != 0
                || methodMapIndex >= body.MethodCount
                || inputCount > int.MaxValue)
            {
                throw Invalid(
                    "An L1 CallSite has invalid reserved fields or indexes.");
            }
            if (20UL + (4UL * inputCount)
                    + (2UL * preparationCount)
                != (ulong)record.Length)
            {
                throw Invalid(
                    "An L1 CallSite record has an invalid input or preparation extent.");
            }

            var methodToken = BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap[(methodMapIndex * sizeof(uint))..]);
            var methodTokenKind = methodToken >> 24;
            var methodTokenRow = checked((int)(methodToken
                & 0x00FF_FFFF));
            int targetMethodRow;
            int targetFunctionIndex;
            if (methodTokenKind == 0x16)
            {
                targetMethodRow = methodTokenRow;
                if (targetMethodRow < tables.Modules.GetMethodDefRange(
                        state.SelectedModuleRow).Start
                    || targetMethodRow >= tables.Modules.GetMethodDefRange(
                        state.SelectedModuleRow).End
                    || targetMethodRow
                        >= state.FunctionIndexByMethodRow.Length
                    || state.FunctionIndexByMethodRow[targetMethodRow] < 0)
                {
                    throw Invalid(
                        "An L1 direct MethodMap target is outside the selected Module's MethodDefs.");
                }

                targetFunctionIndex
                    = state.FunctionIndexByMethodRow[targetMethodRow];
            }
            else if (methodTokenKind == 0x1C)
            {
                if ((uint)methodTokenRow
                    >= (uint)state.FunctionIndexByImportRow.Length
                    || state.FunctionIndexByImportRow[methodTokenRow] < 0
                    || tables.Imports.GetModuleRefForImportRow(
                        methodTokenRow) <= 0
                    || tables.ModuleRefs.GetOwnerModule(
                        tables.Imports.GetModuleRefForImportRow(
                            methodTokenRow))
                        != state.SelectedModuleRow)
                {
                    throw Invalid(
                        "An L1 MethodMap Import token has no exact selected Import row.");
                }

                var expectedDefinition = tables.Imports
                    .GetExpectedDefinitionForImportRow(methodTokenRow);
                if ((expectedDefinition >> 24) != 0x16)
                {
                    throw Invalid(
                        "An L1 MethodMap Import does not expect a MethodDef.");
                }

                targetMethodRow = checked((int)(expectedDefinition
                    & 0x00FF_FFFF));
                if (targetMethodRow < tables.Modules.GetMethodDefRange(
                        state.SelectedModuleRow).Start
                    || targetMethodRow >= tables.Modules.GetMethodDefRange(
                        state.SelectedModuleRow).End
                    || targetMethodRow
                        >= state.FunctionIndexByMethodRow.Length
                    || state.FunctionIndexByMethodRow[targetMethodRow] < 0)
                {
                    throw Invalid(
                        "An L1 MethodMap Import expectation is outside the using Module's MethodDefs.");
                }

                targetFunctionIndex
                    = state.FunctionIndexByImportRow[methodTokenRow];
            }
            else
            {
                throw Invalid(
                    "An L1 MethodMap target is neither a local MethodDef nor a selected Import.");
            }

            var targetFunction
                = state.Functions[targetFunctionIndex];
            if (targetFunction.ParameterCount != (int)inputCount
                || targetFunction.ReturnType == PrimitiveType.Void
                    != (resultKind == 0)
                || resultKind > 2
                || (resultKind == 0 && resultIndex != 0)
                || (preparationCount != 0 && resultKind != 2))
            {
                throw Invalid(
                    "An L1 CallSite's arguments or result differ from the selected local MethodSig.");
            }

            var arguments = ImmutableArray.CreateBuilder<BoundExpression>(
                checked((int)inputCount));
            var inputOffset = 20;
            for (var ordinal = 0; ordinal < (int)inputCount; ordinal++)
            {
                var selectorKind = record[inputOffset];
                var sourceUse = record[inputOffset + 1];
                var selectorIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        record[(inputOffset + 2)..]);
                inputOffset += 4;
                BoundExpression argument;
                var implicitReference
                    = targetFunction.Variables[ordinal].IsImplicitRef;
                if (selectorKind == 2)
                {
                    if (sourceUse != (implicitReference ? 3 : 1)
                        || selectorIndex >= state.HomeVariable.Length
                        || state.HomeVariable[selectorIndex] < 0)
                    {
                        throw Invalid(
                            "An L1 CallSite formal input does not select a Home with the selected source-use.");
                    }

                    var argumentVariable = state.HomeVariable[selectorIndex];
                    argument = new BoundRead(argumentVariable,
                        state.Variables[argumentVariable].Type, default);
                }
                else if (selectorKind == 1)
                {
                    if (selectorIndex > 13)
                    {
                        throw Invalid(
                            "An L1 CallSite register input exceeds the general register domain.");
                    }

                    if (implicitReference && sourceUse == 3
                        && state.Selections.TryGetValue(selectorIndex,
                            out var selection)
                        && selection.AccessKind is 1 or 2
                        && selection.ReferentType
                            == targetFunction.Variables[ordinal].Type)
                    {
                        argument = ReadSelection(selection);
                    }
                    else if (!implicitReference && sourceUse == 1
                        && state.RegisterValues.TryGetValue(selectorIndex,
                            out var value))
                    {
                        argument = value;
                    }
                    else
                    {
                        throw Invalid(
                            "An L1 CallSite register input is undefined or does not match its selected formal source-use.");
                    }
                }
                else if (selectorKind == 0)
                {
                    if (sourceUse != 0 || selectorIndex != 0
                        || targetFunction.Variables[ordinal].Type
                            != PrimitiveType.Void)
                    {
                        throw Invalid(
                            "An L1 CallSite None input is valid only for an exact unit formal.");
                    }

                    argument = new BoundConstant(0, PrimitiveType.Void,
                        default);
                }
                else
                {
                    throw Unsupported(
                        "L1 CallSite inputs must be captured value Homes or registers.");
                }

                if (argument.Type != targetFunction.Variables[ordinal].Type)
                {
                    throw Invalid(
                        "An L1 CallSite input type differs from its local formal.");
                }

                arguments.Add(argument);
            }

            var preparedResultHome = false;
            var preparedResultPriorState = (byte)0;
            var preparedResultTransition = (byte)0;
            if (preparationCount != 0)
            {
                var preparationRecordIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        record[inputOffset..]);
                var preparation = body.GetRecord(preparationRecordIndex);
                if (preparation[0] != 0x03 || preparation.Length != 20
                    || preparation[4] != 2
                    || preparation[7] != 0
                    || preparation[10] != 0
                    || preparation[11] != 0
                    || BinaryPrimitives.ReadUInt16LittleEndian(
                        preparation[8..]) != resultIndex
                    || BinaryPrimitives.ReadUInt16LittleEndian(
                        preparation[12..]) != 0
                    || BinaryPrimitives.ReadUInt16LittleEndian(
                        preparation[14..]) != 0
                    || BinaryPrimitives.ReadUInt32LittleEndian(
                        preparation[16..]) != 0)
                {
                    throw Invalid(
                        "An L1 DestinationPreparation does not select an uncorrelated result Home.");
                }

                preparedResultPriorState = preparation[5];
                preparedResultTransition = preparation[6];
                if (preparedResultPriorState is not (1 or 2)
                    || preparedResultTransition
                        != (preparedResultPriorState == 1 ? 1 : 2))
                {
                    throw Unsupported(
                        "L1 result-Home preparations admit only exact vacant initialization or live replacement.");
                }

                preparedResultHome = true;
            }

            var call = new BoundCall(
                targetFunctionIndex,
                arguments.MoveToImmutable(), targetFunction.ReturnType,
                default);
            if (resultKind == 0)
            {
                if (targetFunction.ReturnType != PrimitiveType.Void)
                {
                    throw Invalid(
                        "An L1 unit CallSite targets a non-unit method.");
                }

                state.Statements.Add(new BoundExpressionStatement(call,
                    default));
            }
            else if (resultKind == 1)
            {
                if (resultIndex == 15)
                {
                    state.Statements.Add(new BoundExpressionStatement(call,
                        default));
                }
                else if (resultIndex <= 13)
                {
                    if (state.UseRegisterSlots)
                    {
                        StoreLocalAggregateRegisterValue(state, resultIndex,
                            call);
                        return;
                    }

                    if (state.Variables.Count
                        >= SemanticVerifier.MaximumVariables)
                    {
                        throw Unsupported(
                            "L1 local-call value snapshots exceed verifier bounds.");
                    }

                    var resultVariable = state.Variables.Count;
                    state.Variables.Add(new BoundVariable(
                        $"__l1_call_value_{resultVariable}", call.Type,
                        false, default));
                    state.Statements.Add(new BoundLocal(resultVariable, call,
                        default));
                    StoreLocalAggregateRegisterValue(state, resultIndex,
                        new BoundRead(resultVariable, call.Type, default));
                }
                else
                {
                    throw Invalid(
                        "An L1 CallSite has an invalid register result.");
                }
            }
            else if (resultKind == 2)
            {
                if (resultIndex >= state.HomeVariable.Length
                    || state.HomeVariable[resultIndex] < 0
                    || targetFunction.ReturnType
                        != state.Variables[
                            state.HomeVariable[resultIndex]].Type)
                {
                    throw Invalid(
                        "An L1 CallSite has an invalid Home result.");
                }

                var variable = state.HomeVariable[resultIndex];
                var homeIndex = variable - state.ParameterCount;
                if (variable < state.ParameterCount
                    || variable >= state.Variables.Count
                    || state.Variables[variable].Type != call.Type)
                {
                    throw Invalid(
                        "An L1 CallSite Home result has no matching local destination.");
                }

                var isInitialized
                    = state.InitializedHomes.Contains(homeIndex);
                var isPossiblyInitialized
                    = state.PossiblyInitializedHomes.Contains(homeIndex);
                if (!state.UseFixedPointHomeState
                    && (preparedResultHome
                            != (preparedResultPriorState != 0)
                        || (preparedResultHome
                            && preparedResultPriorState == 1
                            && isPossiblyInitialized)
                        || (preparedResultHome
                            && preparedResultPriorState == 2
                            && !isInitialized)
                        || (!preparedResultHome && isPossiblyInitialized)))
                {
                    throw Invalid(
                        "An L1 CallSite result-Home preparation disagrees with its incoming Home state.");
                }

                if (state.UseFixedPointHomeState)
                {
                    var actionKind = preparedResultHome
                        ? LocalAggregateLayoutHomeActionKind.CallPreparation
                        : LocalAggregateLayoutHomeActionKind.Initialize;
                    state.HomeStateActions.Add(
                        new LocalAggregateLayoutHomeAction(homeIndex,
                            actionKind, preparedResultPriorState,
                            preparedResultTransition));
                    state.Statements.Add(new BoundAssignment(variable, call,
                        default));
                }
                else if (preparedResultTransition == 1
                    || !preparedResultHome)
                {
                    _ = state.InitializedHomes.Add(homeIndex);
                    _ = state.PossiblyInitializedHomes.Add(homeIndex);
                    if (state.CurrentBlockIndex == 0)
                    {
                        state.Statements.Add(new BoundLocal(variable, call,
                            default));
                    }
                    else
                    {
                        _ = state.ConditionalInitializationHomes.Add(
                            variable);
                        state.Statements.Add(new BoundAssignment(variable,
                            call, default));
                    }
                }
                else
                {
                    state.Statements.Add(new BoundAssignment(variable, call,
                        default));
                }

                _ = state.HomeConstants.Remove(variable);
            }
        }
    }

    private static (int TrueTarget, int FalseTarget)
        ReadLocalAggregateConditionalEdge(TrRmRegisterBodyReader body,
            int recordIndex)
    {
        var record = body.GetRecord(recordIndex);
        if (record[0] != 0x06 || record.Length != 8)
        {
            throw Invalid(
                "An L1 conditional branch does not reference a ConditionalEdge record.");
        }

        var trueTarget = BinaryPrimitives.ReadUInt16LittleEndian(
            record[4..]);
        var falseTarget = BinaryPrimitives.ReadUInt16LittleEndian(
            record[6..]);
        if (trueTarget >= body.BlockCount
            || falseTarget >= body.BlockCount)
        {
            throw Invalid(
                "An L1 ConditionalEdge target is outside the Block table.");
        }

        return (trueTarget, falseTarget);
    }

    private static LocalAggregateLayoutControlFlow
        AnalyzeLocalAggregateLayoutControlFlow(
        TrRmRegisterBodyReader body)
    {
        var successors = new int[body.BlockCount][];
        var predecessors = new List<int>[body.BlockCount];
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            predecessors[blockIndex] = [];
        }

        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            var lastInstructionOffset = -1;
            var lastInstructionLength = 0;
            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], body.Schema);
                if (instructionLength > code.Length - position)
                {
                    throw Invalid(
                        "An L1 CFG block contains a truncated instruction.");
                }

                lastInstructionOffset = position;
                lastInstructionLength = instructionLength;
                position += instructionLength;
            }

            if (lastInstructionOffset < 0)
            {
                throw Invalid("An L1 CFG block has no terminal instruction.");
            }

            var terminal = code.Slice(lastInstructionOffset,
                lastInstructionLength);
            int[] blockSuccessors;
            switch (terminal[0])
            {
            case 0xB0:
                blockSuccessors = [terminal[1]];
                break;
            case 0xB1:
            case >= 0xA0 and <= 0xAF:
            {
                var edgeRecordIndex = terminal[1] >> 4;
                var (trueTarget, falseTarget)
                    = ReadLocalAggregateConditionalEdge(body,
                        edgeRecordIndex);
                blockSuccessors = [trueTarget, falseTarget];
                break;
            }
            case 0xE3:
                if (terminal[1] != 0)
                {
                    throw Invalid(
                        "An L1 Branch instruction has nonzero control bits.");
                }

                blockSuccessors =
                [
                    BinaryPrimitives.ReadUInt16LittleEndian(terminal[2..]),
                ];
                break;
            case 0xE4:
            case 0xE5:
            {
                if (terminal[0] == 0xE4
                    && (terminal[1] & 0xF0) != 0)
                {
                    throw Invalid(
                        "An L1 Boolean branch has nonzero reserved control bits.");
                }

                var edgeRecordIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        terminal[2..]);
                var (trueTarget, falseTarget)
                    = ReadLocalAggregateConditionalEdge(body,
                        edgeRecordIndex);
                blockSuccessors = [trueTarget, falseTarget];
                break;
            }
            case 0x17:
            case 0x18:
            case 0xB8:
            case 0xEB:
                blockSuccessors = [];
                break;
            default:
                throw Unsupported(
                    $"L1 CFG terminal opcode 0x{terminal[0]:X2} is outside the admitted subset.");
            }

            successors[blockIndex] = [.. blockSuccessors.Distinct()];
            foreach (var target in successors[blockIndex])
            {
                if ((uint)target >= (uint)body.BlockCount)
                {
                    throw Invalid(
                        "An L1 CFG edge target is outside the Block table.");
                }

                predecessors[target].Add(blockIndex);
            }
        }

        var reachable = new bool[body.BlockCount];
        var pending = new Queue<int>();
        pending.Enqueue(0);
        var reachableCount = 0;
        while (pending.TryDequeue(out var blockIndex))
        {
            if (reachable[blockIndex])
            {
                continue;
            }

            reachable[blockIndex] = true;
            reachableCount++;
            foreach (var target in successors[blockIndex])
            {
                pending.Enqueue(target);
            }
        }

        if (reachableCount != body.BlockCount)
        {
            throw Invalid("The L1 CFG contains an unreachable Block.");
        }

        var inDegree = new int[body.BlockCount];
        var ready = new SortedSet<int>();
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            inDegree[blockIndex] = predecessors[blockIndex].Count;
            if (inDegree[blockIndex] == 0)
            {
                _ = ready.Add(blockIndex);
            }
        }

        var topologicalOrder = new int[body.BlockCount];
        var orderedCount = 0;
        while (ready.Count != 0)
        {
            var blockIndex = ready.Min;
            _ = ready.Remove(blockIndex);
            topologicalOrder[orderedCount++] = blockIndex;
            foreach (var target in successors[blockIndex])
            {
                if (--inDegree[target] == 0)
                {
                    _ = ready.Add(target);
                }
            }
        }

        var isAcyclic = orderedCount == body.BlockCount;
        var processingOrder = isAcyclic
            ? topologicalOrder
            : GetBreadthFirstOrder(successors);
        var successorArrays = successors;
        var predecessorArrays
            = predecessors.Select(static items => items.ToArray()).ToArray();
        return new LocalAggregateLayoutControlFlow(processingOrder,
            successorArrays, predecessorArrays,
            AnalyzeLocalAggregateLayoutRegisterLiveness(body,
                successorArrays), isAcyclic);
    }

    private static int[] GetBreadthFirstOrder(int[][] successors)
    {
        var order = new int[successors.Length];
        var visited = new bool[successors.Length];
        var pending = new Queue<int>();
        pending.Enqueue(0);
        var count = 0;
        while (pending.TryDequeue(out var blockIndex))
        {
            if (visited[blockIndex])
            {
                continue;
            }

            visited[blockIndex] = true;
            order[count++] = blockIndex;
            foreach (var target in successors[blockIndex])
            {
                pending.Enqueue(target);
            }
        }

        if (count != successors.Length)
        {
            throw Invalid("The L1 CFG contains an unreachable Block.");
        }

        return order;
    }

    private static HashSet<int>[] AnalyzeLocalAggregateLayoutRegisterLiveness(
        TrRmRegisterBodyReader body, int[][] successors)
    {
        var remainingWork = (long)SemanticVerifier.MaximumAnalysisVisits;
        var blockUses = new HashSet<int>[body.BlockCount];
        var blockDefinitions = new HashSet<int>[body.BlockCount];
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            if (remainingWork-- <= 0)
            {
                throw Unsupported(
                    "L1 register liveness exceeds bounded analysis work.");
            }

            var uses = new HashSet<int>();
            var definitions = new HashSet<int>();
            var pendingComparison = false;

            var code = body.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                if (remainingWork-- <= 0)
                {
                    throw Unsupported(
                        "L1 register liveness exceeds bounded analysis work.");
                }

                var opcode = code[position];
                var length = TrRmRegisterBodyReader.InstructionLength(
                    opcode, body.Schema);
                if (length > code.Length - position)
                {
                    throw Invalid(
                        "An L1 register-flow scan found a truncated instruction.");
                }

                var instruction = code.Slice(position, length);
                position += length;
                switch (opcode)
                {
                case 0x10:
                    AddLocalAggregateRegisterUse(uses, definitions,
                        instruction[1] >> 4);
                    AddLocalAggregateRegisterDefinition(definitions,
                        instruction[1] & 0x0F);
                    break;
                case 0x13:
                case 0x16:
                    AddLocalAggregateRegisterDefinition(definitions,
                        instruction[1] & 0x0F);
                    break;
                case 0x14:
                    AddLocalAggregateRegisterUse(uses, definitions,
                        instruction[1] >> 4);
                    break;
                case 0x17:
                    AddLocalAggregateRegisterUse(uses, definitions, 0);
                    break;
                case 0xB1:
                case 0xE4:
                    AddLocalAggregateRegisterUse(uses, definitions,
                        instruction[1] & 0x0F);
                    break;
                case >= 0xA0 and <= 0xAF:
                    if (pendingComparison)
                    {
                        pendingComparison = false;
                    }
                    else
                    {
                        AddLocalAggregateRegisterUse(uses, definitions,
                            instruction[1] & 0x0F);
                    }
                    break;
                case 0x63:
                    AddLocalAggregateRegisterUse(uses, definitions,
                        instruction[1] >> 4);
                    AddLocalAggregateRegisterDefinition(definitions,
                        instruction[1] & 0x0F);
                    break;
                case 0x64:
                    AddLocalAggregateRegisterUse(uses, definitions,
                        instruction[1] & 0x0F);
                    AddLocalAggregateRegisterUse(uses, definitions,
                        instruction[1] >> 4);
                    break;
                case 0xD0:
                    AddLocalAggregateRegisterUse(uses, definitions,
                        instruction[1] & 0x0F);
                    AddLocalAggregateRegisterDefinition(definitions,
                        instruction[1] >> 4);
                    break;
                case 0xC0:
                case 0xC1:
                {
                    var payload = BinaryPrimitives
                        .ReadUInt16LittleEndian(instruction[2..]);
                    var result0 = payload & 0x0F;
                    var result1 = (payload >> 4) & 0x0F;
                    var source0 = (payload >> 8) & 0x0F;
                    var source1 = payload >> 12;
                    AddLocalAggregateRegisterUse(uses, definitions,
                        source0);
                    if (source1 < 14)
                    {
                        AddLocalAggregateRegisterUse(uses, definitions,
                            source1);
                    }
                    AddLocalAggregateRegisterDefinition(definitions,
                        result0);
                    if (result1 < 14)
                    {
                        AddLocalAggregateRegisterDefinition(definitions,
                            result1);
                    }
                    break;
                }
                case 0xC2:
                {
                    var payload = BinaryPrimitives
                        .ReadUInt16LittleEndian(instruction[2..]);
                    AddLocalAggregateRegisterUse(uses, definitions,
                        (payload >> 8) & 0x0F);
                    AddLocalAggregateRegisterUse(uses, definitions,
                        payload >> 12);
                    if (instruction[1] == 0)
                    {
                        pendingComparison = true;
                    }
                    else
                    {
                        AddLocalAggregateRegisterDefinition(definitions,
                            payload & 0x0F);
                    }
                    break;
                }
                case 0xC3:
                    AddLocalAggregateRegisterDefinition(definitions,
                        BinaryPrimitives
                            .ReadUInt16LittleEndian(instruction[2..]) & 0x0F);
                    pendingComparison = false;
                    break;
                case 0x90:
                    AddLocalAggregateCallSiteRegisterEffects(body,
                        instruction[1], uses,
                        definitions, ref remainingWork);
                    break;
                case 0x91:
                    AddLocalAggregatePlaceSelectionRegisterEffects(body,
                        instruction[1] >> 4, uses, definitions);
                    break;
                case 0x92:
                    AddLocalAggregateSemanticStoreRegisterEffects(body,
                        instruction[1], uses, definitions);
                    break;
                case 0xD4:
                    AddLocalAggregateRegisterDefinition(definitions,
                        instruction[1]);
                    break;
                case 0xD5:
                    AddLocalAggregateRegisterDefinition(definitions,
                        instruction[1]);
                    break;
                case 0xD6:
                    AddLocalAggregateRegisterUse(uses, definitions,
                        instruction[1] & 0x0F);
                    break;
                case 0xD8:
                    AddLocalAggregatePlaceSelectionRegisterEffects(body,
                        BinaryPrimitives
                            .ReadUInt16LittleEndian(instruction[2..]), uses,
                        definitions);
                    break;
                case 0xD9:
                    AddLocalAggregateSemanticStoreRegisterEffects(body,
                        BinaryPrimitives
                            .ReadUInt16LittleEndian(instruction[2..]), uses,
                        definitions);
                    break;
                case 0xDA:
                    AddLocalAggregatePlaceSelectionRegisterEffects(body,
                        BinaryPrimitives
                            .ReadUInt16LittleEndian(instruction[2..]), uses,
                        definitions);
                    break;
                case 0xE0:
                    AddLocalAggregateCallSiteRegisterEffects(body,
                        BinaryPrimitives
                        .ReadUInt16LittleEndian(instruction[2..]), uses,
                        definitions, ref remainingWork);
                    break;
                case 0xE5:
                    if (pendingComparison)
                    {
                        pendingComparison = false;
                    }
                    else
                    {
                        AddLocalAggregateRegisterUse(uses, definitions,
                            instruction[1] & 0x0F);
                    }
                    break;
                default:
                    break;
                }
            }

            blockUses[blockIndex] = uses;
            blockDefinitions[blockIndex] = definitions;
        }

        var liveIn = new HashSet<int>[body.BlockCount];
        var liveOut = new HashSet<int>[body.BlockCount];
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            liveIn[blockIndex] = [];
            liveOut[blockIndex] = [];
        }

        var changed = true;
        while (changed)
        {
            if (remainingWork-- <= 0)
            {
                throw Unsupported(
                    "L1 register liveness exceeds bounded fixed-point work.");
            }

            changed = false;
            for (var blockIndex = body.BlockCount - 1;
                blockIndex >= 0; blockIndex--)
            {
                if (remainingWork-- <= 0)
                {
                    throw Unsupported(
                        "L1 register liveness exceeds bounded fixed-point work.");
                }

                var nextOut = new HashSet<int>();
                foreach (var successor in successors[blockIndex])
                {
                    if (remainingWork-- <= 0)
                    {
                        throw Unsupported(
                            "L1 register liveness exceeds bounded fixed-point work.");
                    }

                    nextOut.UnionWith(liveIn[successor]);
                }

                var nextIn = new HashSet<int>(blockUses[blockIndex]);
                foreach (var register in nextOut)
                {
                    if (remainingWork-- <= 0)
                    {
                        throw Unsupported(
                            "L1 register liveness exceeds bounded fixed-point work.");
                    }

                    if (!blockDefinitions[blockIndex].Contains(register))
                    {
                        _ = nextIn.Add(register);
                    }
                }

                if (!liveOut[blockIndex].SetEquals(nextOut))
                {
                    liveOut[blockIndex] = nextOut;
                    changed = true;
                }

                if (!liveIn[blockIndex].SetEquals(nextIn))
                {
                    liveIn[blockIndex] = nextIn;
                    changed = true;
                }
            }
        }

        return liveIn;
    }

    private static void AddLocalAggregateRegisterUse(
        HashSet<int> uses, HashSet<int> definitions, int register)
    {
        if (register <= 13 && !definitions.Contains(register))
        {
            _ = uses.Add(register);
        }
    }

    private static void AddLocalAggregateRegisterDefinition(
        HashSet<int> definitions, int register)
    {
        if (register <= 13)
        {
            _ = definitions.Add(register);
        }
    }

    private static void AddLocalAggregateCallSiteRegisterEffects(
        TrRmRegisterBodyReader body, int recordIndex, HashSet<int> uses,
        HashSet<int> definitions, ref long remainingWork)
    {
        var callSite = body.GetRecord(recordIndex);
        if (callSite[0] != 0x02 || callSite.Length < 20)
        {
            throw Invalid(
                "An L1 register-flow scan found a malformed CallSite.");
        }

        if (callSite[4] == 2)
        {
            AddLocalAggregateRegisterUse(uses, definitions,
                BinaryPrimitives.ReadUInt16LittleEndian(callSite[8..]));
        }

        var inputCount = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(callSite[12..]));
        for (var inputIndex = 0; inputIndex < inputCount; inputIndex++)
        {
            if (remainingWork-- <= 0)
            {
                throw Unsupported(
                    "L1 CallSite liveness exceeds bounded analysis work.");
            }

            var inputOffset = 20 + (inputIndex * 4);
            if (callSite[inputOffset] == 1)
            {
                AddLocalAggregateRegisterUse(uses, definitions,
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        callSite[(inputOffset + 2)..]));
            }
        }

        if (callSite[5] == 1)
        {
            AddLocalAggregateRegisterDefinition(definitions,
                BinaryPrimitives.ReadUInt16LittleEndian(callSite[10..]));
        }
    }

    private static void AddLocalAggregatePlaceSelectionRegisterEffects(
        TrRmRegisterBodyReader body, int recordIndex, HashSet<int> uses,
        HashSet<int> definitions)
    {
        var selection = body.GetRecord(recordIndex);
        if (selection[0] != 0x04 || selection.Length != 24)
        {
            throw Invalid(
                "An L1 register-flow scan found a malformed PlaceSelection.");
        }

        if (selection[7] == 1)
        {
            AddLocalAggregateRegisterUse(uses, definitions,
                BinaryPrimitives.ReadUInt16LittleEndian(selection[10..]));
        }

        if (selection[12] == 1)
        {
            AddLocalAggregateRegisterUse(uses, definitions,
                BinaryPrimitives.ReadUInt16LittleEndian(selection[14..]));
        }

        if (selection[6] == 1)
        {
            AddLocalAggregateRegisterDefinition(definitions,
                BinaryPrimitives.ReadUInt16LittleEndian(selection[8..]));
        }
    }

    private static void AddLocalAggregateSemanticStoreRegisterEffects(
        TrRmRegisterBodyReader body, int recordIndex, HashSet<int> uses,
        HashSet<int> definitions)
    {
        var store = body.GetRecord(recordIndex);
        if (store[0] != 0x05 || store.Length != 20)
        {
            throw Invalid(
                "An L1 register-flow scan found a malformed SemanticStore.");
        }

        var payload = store[4..];
        if (payload[0] == 1)
        {
            AddLocalAggregateRegisterUse(uses, definitions,
                BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]));
        }

        if (payload[1] == 1)
        {
            AddLocalAggregateRegisterUse(uses, definitions,
                BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]));
        }
    }

    private static Dictionary<int, LocalAggregateLayoutRegisterEntry>[]
        ReadLocalAggregateBlockEntries(
        TrRmRegisterBodyReader body,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        LocalAggregateLayoutTypeEnvironment typeEnvironment)
    {
        var result
            = new Dictionary<int, LocalAggregateLayoutRegisterEntry>[
                body.BlockCount];
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            var recordIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(block[8..]);
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x01 || record.Length < 8)
            {
                throw Invalid(
                    "An L1 Block does not select a valid BlockEntryState.");
            }

            var registerCount = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(record[4..]));
            if (8 + (registerCount * 5) != record.Length)
            {
                throw Invalid(
                    "An L1 BlockEntryState has an invalid register extent.");
            }

            var entries = new Dictionary<int,
                LocalAggregateLayoutRegisterEntry>(registerCount);
            for (var entryIndex = 0; entryIndex < registerCount;
                entryIndex++)
            {
                var offset = 8 + (entryIndex * 5);
                var register = record[offset];
                var carrierKind = record[offset + 1];
                var typeMapIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        record[(offset + 2)..]);
                var accessKind = record[offset + 4];
                var type = PrimitiveType.Void;
                if (carrierKind is 0 or 1)
                {
                    type = ReadLocalAggregateType(tables,
                        ReadLocalAggregateBodyTypeSpec(body, typeMapIndex),
                        typeEnvironment);
                }
                else if (carrierKind != 2)
                {
                    throw Unsupported(
                        "The L1 BlockEntryState uses an unassigned carrier kind.");
                }

                if (!entries.TryAdd(register,
                        new LocalAggregateLayoutRegisterEntry(
                            carrierKind, typeMapIndex, type, accessKind)))
                {
                    throw Invalid(
                        "An L1 BlockEntryState repeats a register.");
                }
            }

            result[blockIndex] = entries;
        }

        return result;
    }

    private static int GetLocalAggregateRegisterSlot(
        LocalAggregateLayoutBodyState state, int register,
        PrimitiveType type)
    {
        if (type == PrimitiveType.Void)
        {
            throw Invalid("An L1 value register cannot carry unit.");
        }

        var key = (register, type);
        if (state.RegisterSlotVariables.TryGetValue(key,
                out var variable))
        {
            return variable;
        }

        if (state.Variables.Count >= SemanticVerifier.MaximumVariables)
        {
            throw Unsupported(
                "L1 cross-block register merges exceed verifier variable bounds.");
        }

        variable = state.Variables.Count;
        state.Variables.Add(new BoundVariable(
            $"__l1_register_{register}_{variable}", type, true, default) {
            IsInitializationLocal = true,
        });
        state.RegisterSlotVariables.Add(key, variable);
        return variable;
    }

    private static BoundRead ReadLocalAggregateRegisterSlot(
        LocalAggregateLayoutBodyState state, int register,
        PrimitiveType type)
    {
        var variable = GetLocalAggregateRegisterSlot(state, register, type);
        return new BoundRead(variable, type, default);
    }

    private static void StoreLocalAggregateRegisterValue(
        LocalAggregateLayoutBodyState state, int destination,
        BoundExpression value)
    {
        _ = state.Selections.Remove(destination);
        if (!state.UseRegisterSlots || destination > 13)
        {
            state.RegisterValues[destination] = value;
            return;
        }

        var variable = GetLocalAggregateRegisterSlot(state, destination,
            value.Type);
        state.Statements.Add(new BoundAssignment(variable, value, default));
        state.RegisterValues[destination] = new BoundRead(variable,
            value.Type, default);
    }

    private static void StoreLocalAggregateRegisterSelection(
        LocalAggregateLayoutBodyState state,
        LocalAggregateLayoutSelection selection)
    {
        _ = state.RegisterValues.Remove(selection.DestinationRegister);
        state.Selections[selection.DestinationRegister] = selection;
    }

    private static BoundExpression CaptureLocalAggregatePlaceIndex(
        LocalAggregateLayoutBodyState state, int placeRegister, int depth,
        BoundExpression index)
    {
        if (index is BoundConstant)
        {
            return index;
        }

        var key = (placeRegister, depth, index.Type);
        if (!state.PlaceIndexSlotVariables.TryGetValue(key,
                out var variable))
        {
            if (state.Variables.Count >= SemanticVerifier.MaximumVariables)
            {
                throw Unsupported(
                    "L1 captured PlaceHandle indexes exceed verifier variable bounds.");
            }

            variable = state.Variables.Count;
            state.Variables.Add(new BoundVariable(
                $"__l1_place_index_{placeRegister}_{depth}_{variable}",
                index.Type, true, default) {
                IsInitializationLocal = true,
            });
            state.PlaceIndexSlotVariables.Add(key, variable);
        }

        state.Statements.Add(new BoundAssignment(variable, index, default));
        return new BoundRead(variable, index.Type, default);
    }

    private static LocalAggregateLayoutSelection
        CloneLocalAggregateSelectionForRegister(
        LocalAggregateLayoutBodyState state,
        LocalAggregateLayoutSelection selection, int destinationRegister)
    {
        var rewrittenIndexes
            = new Dictionary<BoundExpression, BoundExpression>();

        ImmutableArray<BoundNativeFixedArrayProjection> CapturePath(
            ImmutableArray<BoundNativeFixedArrayProjection> path)
        {
            if (path.IsDefaultOrEmpty)
            {
                return path;
            }

            var result
                = ImmutableArray.CreateBuilder<
                    BoundNativeFixedArrayProjection>(path.Length);
            for (var depth = 0; depth < path.Length; depth++)
            {
                var projection = path[depth];
                var capturedIndex = CaptureLocalAggregatePlaceIndex(state,
                    destinationRegister, depth, projection.Index);
                rewrittenIndexes[projection.Index] = capturedIndex;
                result.Add(projection with { Index = capturedIndex });
            }

            return result.MoveToImmutable();
        }

        var arrayPath = CapturePath(selection.ArrayPath);
        var arrayBasePath = CapturePath(selection.ArrayBasePath);
        var index = selection.Index is null
            ? null
            : CaptureLocalAggregatePlaceIndex(state,
                destinationRegister,
                Math.Max(0, arrayPath.Length - 1), selection.Index);
        if (selection.Index is not null)
        {
            rewrittenIndexes[selection.Index] = index!;
        }

        BoundExpression Rewrite(BoundExpression expression)
        {
            if (rewrittenIndexes.TryGetValue(expression,
                    out var replacement))
            {
                return replacement;
            }

            return expression.RewriteNestedExpressions(Rewrite);
        }

        return selection with {
            DestinationRegister = destinationRegister,
            Index = index,
            FieldPlace = selection.FieldPlace is null
                ? null : Rewrite(selection.FieldPlace),
            SelectedValue = selection.SelectedValue is null
                ? null : Rewrite(selection.SelectedValue),
            ArrayPath = arrayPath,
            ArrayBasePath = arrayBasePath,
            ArrayBaseFieldPlace = selection.ArrayBaseFieldPlace is null
                ? null : Rewrite(selection.ArrayBaseFieldPlace),
        };
    }

    private static Dictionary<int, LocalAggregateLayoutRegisterExit>
        GetLocalAggregateRegisterExitState(
        LocalAggregateLayoutBodyState state)
    {
        var result
            = new Dictionary<int, LocalAggregateLayoutRegisterExit>();
        foreach (var (register, selection) in state.Selections)
        {
            if (register <= 13)
            {
                result[register] = new LocalAggregateLayoutRegisterExit(1,
                    selection.ReferentType,
                    checked((byte)selection.AccessKind),
                    selection);
            }
        }

        foreach (var (register, value) in state.RegisterValues)
        {
            if (register <= 13)
            {
                result[register] = new LocalAggregateLayoutRegisterExit(0,
                    value.Type, 0, null);
            }
        }

        return result;
    }

    private static bool AreSameLocalAggregateSelection(
        LocalAggregateLayoutSelection left,
        LocalAggregateLayoutSelection right)
        => left.TargetKind == right.TargetKind
            && left.AccessKind == right.AccessKind
            && left.DestinationRegister == right.DestinationRegister
            && left.RootVariable == right.RootVariable
            && left.ReferentType == right.ReferentType
            && left.FieldPath.SequenceEqual(right.FieldPath)
            && Equals(left.FieldPlace, right.FieldPlace)
            && Equals(left.ArrayLayout, right.ArrayLayout)
            && Equals(left.Index, right.Index)
            && left.HomeIndex == right.HomeIndex
            && Equals(left.SelectedValue, right.SelectedValue)
            && left.ArrayPath.SequenceEqual(right.ArrayPath)
            && left.LeafFields.SequenceEqual(right.LeafFields)
            && left.ArrayBasePath.SequenceEqual(right.ArrayBasePath)
            && left.ArrayBaseFieldPath.SequenceEqual(
                right.ArrayBaseFieldPath)
            && left.ArrayBaseLeafFields.SequenceEqual(
                right.ArrayBaseLeafFields)
            && Equals(left.ArrayBaseFieldPlace,
                right.ArrayBaseFieldPlace);

    private static void ValidateLocalAggregateRegisterEdges(
        LocalAggregateLayoutControlFlow controlFlow,
        Dictionary<int, LocalAggregateLayoutRegisterEntry>[] entries,
        Dictionary<int, LocalAggregateLayoutRegisterExit>[] exits,
        Dictionary<int, LocalAggregateLayoutSelection>[] entrySelections)
    {
        for (var blockIndex = 0;
            blockIndex < controlFlow.Predecessors.Length; blockIndex++)
        {
            foreach (var register in controlFlow.LiveIn[blockIndex])
            {
                var entry = entries[blockIndex][register];
                foreach (var predecessor
                    in controlFlow.Predecessors[blockIndex])
                {
                    if (exits[predecessor] is not { } predecessorState
                        || !predecessorState.TryGetValue(register,
                            out var exit)
                        || exit.CarrierKind != entry.CarrierKind
                        || exit.Type != entry.Type
                        || exit.AccessKind != entry.AccessKind)
                    {
                        throw Invalid(
                            "An L1 live register edge disagrees with its BlockEntryState carrier, type or access.");
                    }

                    if (entry.CarrierKind == 1
                        && (exit.Selection is null
                            || !entrySelections[blockIndex].TryGetValue(
                                register, out var selectedEntry)
                            || !AreSameLocalAggregateSelection(
                                selectedEntry, exit.Selection)))
                    {
                        throw Invalid(
                            "An L1 PlaceHandle join changes its exact selected target or authority.");
                    }
                }
            }
        }
    }

    private static void ValidateLocalAggregateHomeStateFlow(
        LocalAggregateLayoutControlFlow controlFlow,
        int localHomeCount,
        IReadOnlyList<LocalAggregateLayoutHomeAction>[] actionsByBlock)
    {
        const byte vacant = 1;
        const byte live = 2;
        var stateCellCount = checked(
            (long)controlFlow.Predecessors.Length * localHomeCount);
        if (stateCellCount > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "L1 Home-state propagation exceeds bounded state capacity.");
        }

        var inputStates = new byte[controlFlow.Predecessors.Length][];
        var outputStates = new byte[controlFlow.Predecessors.Length][];
        for (var blockIndex = 0;
            blockIndex < inputStates.Length; blockIndex++)
        {
            inputStates[blockIndex] = new byte[localHomeCount];
            outputStates[blockIndex] = new byte[localHomeCount];
        }

        var remainingWork
            = (long)SemanticVerifier.MaximumAnalysisVisits;
        var changed = true;
        while (changed)
        {
            if (remainingWork-- <= 0)
            {
                throw Unsupported(
                    "L1 Home-state propagation exceeds bounded fixed-point work.");
            }

            changed = false;
            foreach (var blockIndex in controlFlow.ProcessingOrder)
            {
                if (remainingWork < localHomeCount + 1L)
                {
                    throw Unsupported(
                        "L1 Home-state propagation exceeds bounded fixed-point work.");
                }

                remainingWork -= localHomeCount + 1L;
                var input = new byte[localHomeCount];
                if (blockIndex == 0)
                {
                    Array.Fill(input, vacant);
                }

                foreach (var predecessor
                    in controlFlow.Predecessors[blockIndex])
                {
                    if (remainingWork < localHomeCount)
                    {
                        throw Unsupported(
                            "L1 Home-state propagation exceeds bounded fixed-point work.");
                    }

                    remainingWork -= localHomeCount;
                    for (var homeIndex = 0; homeIndex < localHomeCount;
                        homeIndex++)
                    {
                        input[homeIndex] |=
                            outputStates[predecessor][homeIndex];
                    }
                }

                var output = (byte[])input.Clone();
                foreach (var action in actionsByBlock[blockIndex])
                {
                    if (remainingWork-- <= 0)
                    {
                        throw Unsupported(
                            "L1 Home-state propagation exceeds bounded fixed-point work.");
                    }

                    if ((uint)action.HomeIndex >=
                        (uint)localHomeCount)
                    {
                        throw Invalid(
                            "An L1 Home-state transition selects a formal or an absent local Home.");
                    }

                    output[action.HomeIndex] = live;
                }

                if (!inputStates[blockIndex].AsSpan().SequenceEqual(input))
                {
                    inputStates[blockIndex] = input;
                    changed = true;
                }

                if (!outputStates[blockIndex].AsSpan().SequenceEqual(output))
                {
                    outputStates[blockIndex] = output;
                    changed = true;
                }
            }
        }

        foreach (var blockIndex in controlFlow.ProcessingOrder)
        {
            if (remainingWork < localHomeCount + 1L)
            {
                throw Unsupported(
                    "L1 Home-state validation exceeds bounded work.");
            }

            remainingWork -= localHomeCount + 1L;
            var state = (byte[])inputStates[blockIndex].Clone();
            foreach (var action in actionsByBlock[blockIndex])
            {
                if (remainingWork-- <= 0)
                {
                    throw Unsupported(
                        "L1 Home-state validation exceeds bounded work.");
                }

                if ((uint)action.HomeIndex >= (uint)localHomeCount)
                {
                    throw Invalid(
                        "An L1 Home-state transition selects a formal or an absent local Home.");
                }

                var prior = state[action.HomeIndex];
                switch (action.Kind)
                {
                case LocalAggregateLayoutHomeActionKind.Initialize:
                    if (prior != vacant)
                    {
                        throw Invalid(
                            "An L1 Home initialization is not vacant on every incoming path.");
                    }
                    break;

                case LocalAggregateLayoutHomeActionKind.Replace:
                    if (prior != live)
                    {
                        throw Invalid(
                            "An L1 Home replacement is not live on every incoming path.");
                    }
                    break;

                case LocalAggregateLayoutHomeActionKind.InitializeOrReplace:
                    if (prior == 0
                        || (prior & ~(vacant | live)) != 0)
                    {
                        throw Invalid(
                            "An L1 HomeStore has no complete vacant/live state on every incoming path.");
                    }
                    break;

                case LocalAggregateLayoutHomeActionKind.CallPreparation:
                    if (action.PriorState == 1
                        && action.Transition == 1)
                    {
                        if (prior != vacant)
                        {
                            throw Invalid(
                                "An L1 vacant result-Home preparation is not vacant on every incoming path.");
                        }
                    }
                    else if (action.PriorState == 2
                        && action.Transition == 2)
                    {
                        if (prior != live)
                        {
                            throw Invalid(
                                "An L1 live result-Home preparation is not live on every incoming path.");
                        }
                    }
                    else
                    {
                        throw Unsupported(
                            "L1 by-value Calls do not assign MaybeLive, Partial, Reserved or Publish preparation semantics.");
                    }
                    break;

                default:
                    throw Invalid(
                        "An L1 Home-state transition has an unknown kind.");
                }

                state[action.HomeIndex] = live;
            }
        }
    }

    private static TokenKind GetLocalAggregateComparisonOperator(
        int predicateMask)
        => predicateMask switch {
            1 => TokenKind.Less,
            2 => TokenKind.EqualsEquals,
            3 => TokenKind.LessEquals,
            4 => TokenKind.Greater,
            6 => TokenKind.GreaterEquals,
            13 => TokenKind.BangEquals,
            _ => throw Invalid(
                "The L1 comparison uses an unsupported relation predicate."),
        };

    private static BoundBinary ReadLocalAggregatePredicateCondition(
        int predicateMask, int conditionRegister,
        Dictionary<int, BoundExpression> registerValues,
        LocalAggregateLayoutPendingComparison? pendingComparison,
        TrRmArtifactSchema schema, out bool consumedComparison)
    {
        consumedComparison = false;
        if (pendingComparison is { } comparison)
        {
            if (comparison.Destination != conditionRegister)
            {
                throw Invalid(
                    "An L1 predicate branch does not match its pending comparison result.");
            }

            consumedComparison = true;
            return new BoundBinary(
                GetLocalAggregateComparisonOperator(predicateMask),
                comparison.Left, comparison.Right, PrimitiveType.Bool,
                default);
        }

        BoundExpression value;
        if (conditionRegister <= 13)
        {
            if (!registerValues.TryGetValue(conditionRegister, out value!))
            {
                throw Invalid(
                    "An L1 predicate branch condition register is undefined.");
            }
        }
        else if (conditionRegister is 14 or 15
            && TryGetContextualConstant(
                checked((byte)conditionRegister), PrimitiveType.UInt8,
                schema, out var contextualValue))
        {
            value = new BoundConstant(contextualValue,
                PrimitiveType.UInt8, default);
        }
        else
        {
            throw Invalid(
                "An L1 predicate branch condition register is invalid.");
        }

        if (predicateMask is < 1 or > 15
            || value.Type != PrimitiveType.UInt8)
        {
            throw Invalid(
                "An L1 predicate branch requires uint8 and a nonzero relation mask.");
        }

        var masked = new BoundBinary(TokenKind.Ampersand, value,
            new BoundConstant(predicateMask, PrimitiveType.UInt8, default),
            PrimitiveType.UInt8, default);
        return new BoundBinary(TokenKind.BangEquals, masked,
            new BoundConstant(0, PrimitiveType.UInt8, default),
            PrimitiveType.Bool, default);
    }

    private static ImmutableArray<BoundStatement>
        BuildLocalAggregateLayoutBody(
        LocalAggregateLayoutBlock[] blocks,
        LocalAggregateLayoutControlFlow controlFlow)
        => controlFlow.IsAcyclic
            ? BuildLocalAggregateLayoutAcyclicBody(blocks)
            : BuildLocalAggregateLayoutCyclicBody(blocks, controlFlow);

    private static ImmutableArray<BoundStatement>
        BuildLocalAggregateLayoutAcyclicBody(
        LocalAggregateLayoutBlock[] blocks)
    {
        var activeBlocks = new HashSet<int>();
        var visitedBlocks = new HashSet<int>();
        var visitBudget = SemanticVerifier.MaximumAnalysisVisits;
        var remainingReachabilityWork
            = SemanticVerifier.MaximumAnalysisVisits;

        Dictionary<int, int> GetReachableDistances(int start)
        {
            var distances = new Dictionary<int, int>();
            var pending = new Queue<(int Block, int Distance)>();
            pending.Enqueue((start, 0));
            while (pending.TryDequeue(out var item))
            {
                if (remainingReachabilityWork-- <= 0)
                {
                    throw Unsupported(
                        "The L1 control-flow graph exceeds bounded join reconstruction work.");
                }

                if (distances.ContainsKey(item.Block))
                {
                    continue;
                }

                distances.Add(item.Block, item.Distance);
                var block = blocks[item.Block];
                if (block.Returns)
                {
                    continue;
                }
                if (block.JumpTarget >= 0)
                {
                    pending.Enqueue((block.JumpTarget,
                        checked(item.Distance + 1)));
                }
                else if (block.Condition is not null)
                {
                    pending.Enqueue((block.TrueTarget,
                        checked(item.Distance + 1)));
                    pending.Enqueue((block.FalseTarget,
                        checked(item.Distance + 1)));
                }
            }

            return distances;
        }

        int FindCommonJoin(int first, int second, int stop)
        {
            if (first == second)
            {
                return first;
            }

            var left = GetReachableDistances(first);
            var right = GetReachableDistances(second);
            var join = -1;
            var bestMaxDistance = int.MaxValue;
            var bestDistanceSum = int.MaxValue;
            foreach (var (candidate, leftDistance) in left)
            {
                if (!right.TryGetValue(candidate, out var rightDistance))
                {
                    continue;
                }

                var maxDistance = Math.Max(leftDistance, rightDistance);
                var distanceSum = checked(leftDistance + rightDistance);
                if (maxDistance < bestMaxDistance
                    || (maxDistance == bestMaxDistance
                        && distanceSum < bestDistanceSum))
                {
                    join = candidate;
                    bestMaxDistance = maxDistance;
                    bestDistanceSum = distanceSum;
                }
            }

            if (join >= 0)
            {
                return join;
            }
            return stop >= 0
                && (left.ContainsKey(stop) || right.ContainsKey(stop))
                ? stop : -1;
        }

        ImmutableArray<BoundStatement> BuildPath(int start, int stop,
            int depth)
        {
            if (start == stop)
            {
                return [];
            }
            var invalidBlock = (uint)start >= (uint)blocks.Length;
            var excessiveDepth = depth > SemanticVerifier.MaximumDepth;
            var exhaustedVisits = visitBudget-- <= 0;
            var activeCycle = activeBlocks.Contains(start);
            var repeatedBlock = visitedBlocks.Contains(start);
            if (invalidBlock || excessiveDepth || exhaustedVisits
                || activeCycle || repeatedBlock)
            {
                throw Unsupported(
                    "The L1 control-flow graph is cyclic, irreducible or exceeds structured reconstruction bounds.");
            }
            _ = activeBlocks.Add(start);
            _ = visitedBlocks.Add(start);

            var block = blocks[start];
            var statements = ImmutableArray.CreateBuilder<BoundStatement>(
                block.Statements.Length);
            statements.AddRange(block.Statements);
            if (block.Returns)
            {
                _ = activeBlocks.Remove(start);
                return statements.ToImmutable();
            }

            if (block.JumpTarget >= 0)
            {
                if (block.JumpTarget != stop)
                {
                    statements.AddRange(BuildPath(block.JumpTarget, stop,
                        depth + 1));
                }

                _ = activeBlocks.Remove(start);
                return statements.ToImmutable();
            }

            if (block.Condition is BoundExpression condition)
            {
                var join = FindCommonJoin(block.TrueTarget,
                    block.FalseTarget, stop);
                var whenTrue = BuildPath(block.TrueTarget, join,
                    depth + 1);
                var whenFalse = BuildPath(block.FalseTarget, join,
                    depth + 1);
                statements.Add(new BoundIf(condition,
                    new BoundBlock(whenTrue, default),
                    new BoundBlock(whenFalse, default), default));
                if (join >= 0 && join != stop)
                {
                    statements.AddRange(BuildPath(join, stop,
                        depth + 1));
                }

                _ = activeBlocks.Remove(start);
                return statements.ToImmutable();
            }

            throw Invalid("An L1 CFG block has no supported terminal edge.");
        }

        var result = BuildPath(0, -1, 0);
        if (visitedBlocks.Count != blocks.Length)
        {
            throw Invalid(
                "The L1 Body contains unreachable or unconsumed Blocks.");
        }

        return result;
    }

    private static ImmutableArray<BoundStatement>
        BuildLocalAggregateLayoutCyclicBody(
        LocalAggregateLayoutBlock[] blocks,
        LocalAggregateLayoutControlFlow controlFlow)
    {
        var dominatorCellCount = checked((long)blocks.Length
            * blocks.Length);
        if (dominatorCellCount > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "The L1 cyclic CFG exceeds bounded dominator-state capacity.");
        }

        var dominators = new HashSet<int>[blocks.Length];
        var allBlocks = Enumerable.Range(0, blocks.Length).ToHashSet();
        dominators[0] = [0];
        for (var blockIndex = 1; blockIndex < blocks.Length; blockIndex++)
        {
            dominators[blockIndex] = [.. allBlocks];
        }

        var dominatorsChanged = true;
        var remainingDominatorWork
            = (long)SemanticVerifier.MaximumAnalysisVisits;
        while (dominatorsChanged)
        {
            if (remainingDominatorWork-- <= 0)
            {
                throw Unsupported(
                    "The L1 cyclic CFG exceeds bounded dominator analysis work.");
            }

            dominatorsChanged = false;
            for (var blockIndex = 1; blockIndex < blocks.Length;
                blockIndex++)
            {
                if (remainingDominatorWork < blocks.Length)
                {
                    throw Unsupported(
                        "The L1 cyclic CFG exceeds bounded dominator analysis work.");
                }

                remainingDominatorWork -= blocks.Length;
                var predecessors
                    = controlFlow.Predecessors[blockIndex];
                var next = new HashSet<int>(allBlocks);
                foreach (var predecessor in predecessors)
                {
                    next.IntersectWith(dominators[predecessor]);
                }

                _ = next.Add(blockIndex);
                if (!dominators[blockIndex].SetEquals(next))
                {
                    dominators[blockIndex] = next;
                    dominatorsChanged = true;
                }
            }
        }

        var backEdges = new HashSet<(int Source, int Target)>();
        for (var source = 0; source < blocks.Length; source++)
        {
            if (remainingDominatorWork-- <= 0)
            {
                throw Unsupported(
                    "The L1 cyclic CFG exceeds bounded dominator analysis work.");
            }

            foreach (var target in controlFlow.Successors[source])
            {
                if (dominators[source].Contains(target))
                {
                    _ = backEdges.Add((source, target));
                }
            }
        }

        if (backEdges.Count == 0)
        {
            throw Unsupported(
                "The L1 cyclic CFG has no dominating loop header.");
        }

        var nonBackEdgeInDegrees = new int[blocks.Length];
        for (var source = 0; source < blocks.Length; source++)
        {
            foreach (var target in controlFlow.Successors[source])
            {
                if (!backEdges.Contains((source, target)))
                {
                    nonBackEdgeInDegrees[target]++;
                }
            }
        }

        var ready = new Queue<int>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (nonBackEdgeInDegrees[blockIndex] == 0)
            {
                ready.Enqueue(blockIndex);
            }
        }

        var nonBackEdgeCount = 0;
        while (ready.TryDequeue(out var source))
        {
            if (remainingDominatorWork-- <= 0)
            {
                throw Unsupported(
                    "The L1 cyclic CFG exceeds bounded reducibility analysis work.");
            }

            nonBackEdgeCount++;
            foreach (var target in controlFlow.Successors[source])
            {
                if (backEdges.Contains((source, target)))
                {
                    continue;
                }

                if (--nonBackEdgeInDegrees[target] == 0)
                {
                    ready.Enqueue(target);
                }
            }
        }

        if (nonBackEdgeCount != blocks.Length)
        {
            throw Unsupported(
                "The L1 CFG contains an irreducible cycle without a dominating header.");
        }

        var remainingLoopBuildWork
            = (long)SemanticVerifier.MaximumAnalysisVisits;
        var loopsByHeader
            = new Dictionary<int, LocalAggregateLayoutLoop>();
        for (var header = 0; header < blocks.Length; header++)
        {
            if (remainingLoopBuildWork-- <= 0)
            {
                throw Unsupported(
                    "The L1 CFG natural-loop reconstruction exceeds bounded work.");
            }

            var latches = backEdges.Where(edge => edge.Target == header)
                .Select(static edge => edge.Source).ToArray();
            if (latches.Length == 0)
            {
                continue;
            }

            var members = new HashSet<int> { header };
            var pending = new Stack<int>(latches);
            while (pending.TryPop(out var member))
            {
                if (remainingLoopBuildWork-- <= 0)
                {
                    throw Unsupported(
                        "The L1 CFG natural-loop reconstruction exceeds bounded work.");
                }

                if (!members.Add(member))
                {
                    continue;
                }

                foreach (var predecessor
                    in controlFlow.Predecessors[member])
                {
                    if (remainingLoopBuildWork-- <= 0)
                    {
                        throw Unsupported(
                            "The L1 CFG natural-loop reconstruction exceeds bounded work.");
                    }

                    if (predecessor != header)
                    {
                        pending.Push(predecessor);
                    }
                }
            }

            var exitTargets = members
                .SelectMany(member => controlFlow.Successors[member]
                    .Where(target => !members.Contains(target)))
                .Distinct().Order().ToArray();
            var loop = new LocalAggregateLayoutLoop(header, members,
                exitTargets);
            loopsByHeader.Add(header, loop);
        }

        var loops = loopsByHeader.Values.ToArray();
        var remainingLoopValidationWork
            = (long)SemanticVerifier.MaximumAnalysisVisits;
        for (var leftIndex = 0; leftIndex < loops.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1;
                rightIndex < loops.Length; rightIndex++)
            {
                if (remainingLoopValidationWork-- <= 0)
                {
                    throw Unsupported(
                        "The L1 CFG natural-loop nesting exceeds bounded analysis work.");
                }

                var left = loops[leftIndex].Members;
                var right = loops[rightIndex].Members;
                if (left.Overlaps(right)
                    && !left.IsSubsetOf(right)
                    && !right.IsSubsetOf(left))
                {
                    throw Unsupported(
                        "The L1 CFG has overlapping non-nested natural loops.");
                }
            }
        }

        foreach (var loop in loops)
        {
            foreach (var member in loop.Members)
            {
                if (member == loop.Header)
                {
                    continue;
                }

                foreach (var predecessor
                    in controlFlow.Predecessors[member])
                {
                    if (!loop.Members.Contains(predecessor))
                    {
                        throw Unsupported(
                            "An L1 natural loop has an entry edge that bypasses its header.");
                    }
                }
            }
        }

        var activeBlocks = new HashSet<int>();
        var visitedBlocks = new HashSet<int>();
        var visitBudget = SemanticVerifier.MaximumAnalysisVisits;
        var remainingReachabilityWork
            = SemanticVerifier.MaximumAnalysisVisits;

        Dictionary<int, int> GetReachableDistances(int start,
            HashSet<int>? allowedBlocks)
        {
            var distances = new Dictionary<int, int>();
            var pending = new Queue<(int Block, int Distance)>();
            pending.Enqueue((start, 0));
            while (pending.TryDequeue(out var item))
            {
                if (remainingReachabilityWork-- <= 0)
                {
                    throw Unsupported(
                        "The L1 cyclic CFG exceeds bounded join reconstruction work.");
                }

                if ((allowedBlocks is not null
                        && !allowedBlocks.Contains(item.Block))
                    || distances.ContainsKey(item.Block))
                {
                    continue;
                }

                distances.Add(item.Block, item.Distance);
                var block = blocks[item.Block];
                if (block.Returns)
                {
                    continue;
                }
                if (block.JumpTarget >= 0)
                {
                    pending.Enqueue((block.JumpTarget,
                        checked(item.Distance + 1)));
                }
                else if (block.Condition is not null)
                {
                    pending.Enqueue((block.TrueTarget,
                        checked(item.Distance + 1)));
                    pending.Enqueue((block.FalseTarget,
                        checked(item.Distance + 1)));
                }
            }

            return distances;
        }

        int FindCommonJoin(int first, int second,
            HashSet<int>? allowedBlocks)
        {
            if (first == second)
            {
                return first;
            }

            var left = GetReachableDistances(first, allowedBlocks);
            var right = GetReachableDistances(second, allowedBlocks);
            var join = -1;
            var bestMaxDistance = int.MaxValue;
            var bestDistanceSum = int.MaxValue;
            foreach (var (candidate, leftDistance) in left)
            {
                if (!right.TryGetValue(candidate, out var rightDistance))
                {
                    continue;
                }

                var maxDistance = Math.Max(leftDistance, rightDistance);
                var distanceSum = checked(leftDistance + rightDistance);
                if (maxDistance < bestMaxDistance
                    || (maxDistance == bestMaxDistance
                        && distanceSum < bestDistanceSum))
                {
                    join = candidate;
                    bestMaxDistance = maxDistance;
                    bestDistanceSum = distanceSum;
                }
            }

            return join;
        }

        ImmutableArray<BoundStatement> BuildPath(int start, int stop,
            int depth, LocalAggregateLayoutLoop? activeLoop)
        {
            if (start == stop
                || (activeLoop is not null
                    && start == activeLoop.Header))
            {
                return [];
            }

            if (activeLoop is not null
                && !activeLoop.Members.Contains(start))
            {
                if (activeLoop.ExitTargets.Length == 1
                    && start == activeLoop.ExitTargets[0])
                {
                    return [new BoundBreak(default)];
                }

                throw Unsupported(
                    "An L1 loop edge leaves through an unstructured continuation.");
            }

            if (loopsByHeader.TryGetValue(start, out var nestedLoop)
                && (activeLoop is null
                    || nestedLoop.Header != activeLoop.Header))
            {
                return BuildLoop(nestedLoop, stop, depth, activeLoop);
            }

            if ((uint)start >= (uint)blocks.Length
                || depth > SemanticVerifier.MaximumDepth
                || visitBudget-- <= 0
                || !activeBlocks.Add(start)
                || !visitedBlocks.Add(start))
            {
                throw Unsupported(
                    "The L1 CFG is cyclic, irreducible or exceeds structured reconstruction bounds.");
            }

            var block = blocks[start];
            var statements = ImmutableArray.CreateBuilder<BoundStatement>(
                block.Statements.Length);
            statements.AddRange(block.Statements);
            if (block.Returns)
            {
                _ = activeBlocks.Remove(start);
                return statements.ToImmutable();
            }

            if (block.JumpTarget >= 0)
            {
                if (activeLoop is not null
                    && !activeLoop.Members.Contains(block.JumpTarget))
                {
                    if (activeLoop.ExitTargets.Length != 1
                        || block.JumpTarget
                            != activeLoop.ExitTargets[0])
                    {
                        throw Unsupported(
                            "An L1 loop has multiple or unstructured exit targets.");
                    }

                    statements.Add(new BoundBreak(default));
                }
                else if (block.JumpTarget != stop
                    && (activeLoop is null
                        || block.JumpTarget != activeLoop.Header))
                {
                    statements.AddRange(BuildPath(block.JumpTarget, stop,
                        depth + 1, activeLoop));
                }

                _ = activeBlocks.Remove(start);
                return statements.ToImmutable();
            }

            if (block.Condition is BoundExpression condition)
            {
                if (activeLoop is not null)
                {
                    var trueOutside
                        = !activeLoop.Members.Contains(block.TrueTarget);
                    var falseOutside
                        = !activeLoop.Members.Contains(block.FalseTarget);
                    if (trueOutside || falseOutside)
                    {
                        if (activeLoop.ExitTargets.Length != 1
                            || (trueOutside && block.TrueTarget
                                != activeLoop.ExitTargets[0])
                            || (falseOutside && block.FalseTarget
                                != activeLoop.ExitTargets[0]))
                        {
                            throw Unsupported(
                                "An L1 loop has multiple or unstructured exit targets.");
                        }

                        var truePath = trueOutside
                            ? [new BoundBreak(default)]
                            : BuildPath(block.TrueTarget,
                                activeLoop.Header, depth + 1,
                                activeLoop);
                        var falsePath = falseOutside
                            ? [new BoundBreak(default)]
                            : BuildPath(block.FalseTarget,
                                activeLoop.Header, depth + 1,
                                activeLoop);
                        statements.Add(new BoundIf(condition,
                            new BoundBlock(truePath, default),
                            new BoundBlock(falsePath, default), default));
                        _ = activeBlocks.Remove(start);
                        return statements.ToImmutable();
                    }

                    var loopJoin = FindCommonJoin(block.TrueTarget,
                        block.FalseTarget, activeLoop.Members);
                    if (loopJoin < 0)
                    {
                        throw Unsupported(
                            "An L1 in-loop conditional has no structured join.");
                    }

                    var loopWhenTrue = BuildPath(block.TrueTarget,
                        loopJoin, depth + 1, activeLoop);
                    var loopWhenFalse = BuildPath(block.FalseTarget,
                        loopJoin, depth + 1, activeLoop);
                    statements.Add(new BoundIf(condition,
                        new BoundBlock(loopWhenTrue, default),
                        new BoundBlock(loopWhenFalse, default), default));
                    if (loopJoin != activeLoop.Header
                        && loopJoin != stop)
                    {
                        statements.AddRange(BuildPath(loopJoin, stop,
                            depth + 1, activeLoop));
                    }

                    _ = activeBlocks.Remove(start);
                    return statements.ToImmutable();
                }

                var join = FindCommonJoin(block.TrueTarget,
                    block.FalseTarget, allowedBlocks: null);
                var whenTrue = BuildPath(block.TrueTarget, join,
                    depth + 1, activeLoop: null);
                var whenFalse = BuildPath(block.FalseTarget, join,
                    depth + 1, activeLoop: null);
                statements.Add(new BoundIf(condition,
                    new BoundBlock(whenTrue, default),
                    new BoundBlock(whenFalse, default), default));
                if (join >= 0 && join != stop)
                {
                    statements.AddRange(BuildPath(join, stop,
                        depth + 1, activeLoop: null));
                }

                _ = activeBlocks.Remove(start);
                return statements.ToImmutable();
            }

            throw Invalid("An L1 CFG block has no supported terminal edge.");
        }

        ImmutableArray<BoundStatement> BuildLoop(
            LocalAggregateLayoutLoop loop, int stop, int depth,
            LocalAggregateLayoutLoop? parentLoop)
        {
            if (loop.ExitTargets.Length > 1
                || depth > SemanticVerifier.MaximumDepth
                || !activeBlocks.Add(loop.Header)
                || !visitedBlocks.Add(loop.Header))
            {
                throw Unsupported(
                    "The L1 loop is irreducible or has unsupported nesting/exit structure.");
            }

            var header = blocks[loop.Header];
            var loopStatements
                = ImmutableArray.CreateBuilder<BoundStatement>(
                    header.Statements.Length + 1);
            loopStatements.AddRange(header.Statements);
            if (header.Returns)
            {
                throw Invalid(
                    "An L1 loop header cannot return before its loop transfer.");
            }

            if (header.Condition is BoundExpression condition)
            {
                var trueInside = loop.Members.Contains(header.TrueTarget);
                var falseInside = loop.Members.Contains(header.FalseTarget);
                if (trueInside != falseInside)
                {
                    var bodyTarget = trueInside
                        ? header.TrueTarget : header.FalseTarget;
                    var exitTarget = trueInside
                        ? header.FalseTarget : header.TrueTarget;
                    if (loop.ExitTargets.Length != 1
                        || exitTarget != loop.ExitTargets[0])
                    {
                        throw Unsupported(
                            "An L1 loop header does not have one exact exit target.");
                    }

                    var bodyPath = BuildPath(bodyTarget, loop.Header,
                        depth + 1, loop);
                    var whenTrue = trueInside
                        ? new BoundBlock(bodyPath, default)
                        : new BoundBlock([new BoundBreak(default)], default);
                    var whenFalse = falseInside
                        ? new BoundBlock(bodyPath, default)
                        : new BoundBlock([new BoundBreak(default)], default);
                    loopStatements.Add(new BoundIf(condition, whenTrue,
                        whenFalse, default));
                }
                else if (trueInside)
                {
                    var join = FindCommonJoin(header.TrueTarget,
                        header.FalseTarget, loop.Members);
                    if (join < 0)
                    {
                        throw Unsupported(
                            "An L1 loop-header conditional has no structured join.");
                    }

                    var whenTrue = BuildPath(header.TrueTarget, join,
                        depth + 1, loop);
                    var whenFalse = BuildPath(header.FalseTarget, join,
                        depth + 1, loop);
                    loopStatements.Add(new BoundIf(condition,
                        new BoundBlock(whenTrue, default),
                        new BoundBlock(whenFalse, default), default));
                    if (join != loop.Header)
                    {
                        loopStatements.AddRange(BuildPath(join,
                            loop.Header, depth + 1, loop));
                    }
                }
                else
                {
                    if (loop.ExitTargets.Length != 1
                        || header.TrueTarget != loop.ExitTargets[0]
                        || header.FalseTarget != loop.ExitTargets[0])
                    {
                        throw Unsupported(
                            "An L1 loop header has no internal successor or has multiple exits.");
                    }

                    loopStatements.Add(new BoundIf(condition,
                        new BoundBlock([new BoundBreak(default)], default),
                        new BoundBlock([new BoundBreak(default)], default),
                        default));
                }
            }
            else if (header.JumpTarget >= 0
                && loop.Members.Contains(header.JumpTarget))
            {
                loopStatements.AddRange(BuildPath(header.JumpTarget,
                    loop.Header, depth + 1, loop));
            }
            else
            {
                throw Invalid(
                    "An L1 natural loop header has no supported loop transfer.");
            }

            var result
                = ImmutableArray.CreateBuilder<BoundStatement>();
            result.Add(new BoundWhile(
                new BoundConstant(1, PrimitiveType.Bool, default),
                new BoundBlock(loopStatements.MoveToImmutable(), default),
                default));
            _ = activeBlocks.Remove(loop.Header);

            if (loop.ExitTargets.Length == 1
                && loop.ExitTargets[0] != stop)
            {
                result.AddRange(BuildPath(loop.ExitTargets[0], stop,
                    depth + 1, parentLoop));
            }

            return result.ToImmutable();
        }

        var body = BuildPath(0, -1, 0, activeLoop: null);
        if (visitedBlocks.Count != blocks.Length)
        {
            throw Invalid(
                "The L1 Body contains unreachable or unconsumed Blocks.");
        }

        return body;
    }

    private static bool HasLocalAggregateConstructionStore(
        ImmutableArray<BoundStatement> statements, int variable)
    {
        var found = false;
        _ = BoundTreeTraversal.VisitStatements(
            new BoundBlock(statements, default), statement =>
            {
                found |= statement is BoundFieldAssignment
                    { Variable: var fieldVariable }
                    && fieldVariable == variable;
                found |= statement is BoundExpressionStatement {
                    Expression: BoundNativeFixedArrayWrite {
                        Variable: var arrayVariable,
                    },
                } && arrayVariable == variable;
            });
        return found;
    }

    private static bool IsLocalAggregateUnionType(PrimitiveType type,
        LocalAggregateLayoutTypeEnvironment typeEnvironment)
    {
        var structureIndex = (int)type - 100;
        return !BoundEnumTypes.IsType(type)
            && (uint)structureIndex
                < (uint)typeEnvironment.Structures.Length
            && typeEnvironment.Structures[structureIndex].IsUnion;
    }

    private static bool IsLocalAggregateUnsafeFieldPath(
        PrimitiveType rootType, ImmutableArray<int> fieldPath,
        LocalAggregateLayoutTypeEnvironment typeEnvironment)
    {
        if (fieldPath.IsDefaultOrEmpty)
        {
            return false;
        }

        var ownerType = rootType;
        foreach (var fieldOrdinal in fieldPath)
        {
            if (IsLocalAggregateUnionType(ownerType, typeEnvironment))
            {
                return true;
            }

            var structureIndex = (int)ownerType - 100;
            if (BoundEnumTypes.IsType(ownerType)
                || (uint)structureIndex
                    >= (uint)typeEnvironment.Structures.Length
                || (uint)fieldOrdinal
                    >= (uint)typeEnvironment.Structures[
                        structureIndex].Fields.Length)
            {
                throw Invalid(
                    "An L1 unsafe-access path has an invalid nominal field step.");
            }

            ownerType = typeEnvironment.Structures[
                structureIndex].Fields[fieldOrdinal].Type;
        }

        return false;
    }

    private static bool IsLocalAggregateUnsafeSelection(
        LocalAggregateLayoutSelection selection,
        LocalAggregateLayoutBodyState state)
    {
        if ((uint)selection.RootVariable
            >= (uint)state.Variables.Count)
        {
            throw Invalid(
                "An L1 unsafe-access selection has an invalid root Home.");
        }

        var rootType = state.Variables[selection.RootVariable].Type;
        var typeEnvironment = state.TypeEnvironment;
        var selectedPathIsUnsafe = selection.ArrayPath.IsEmpty
            ? IsLocalAggregateUnsafeFieldPath(rootType,
                selection.FieldPath, typeEnvironment)
            : HasLocalAggregateUnionOwner(selection.LeafFields,
                typeEnvironment);
        return selectedPathIsUnsafe
            || IsLocalAggregateUnsafeFieldPath(rootType,
                selection.ArrayBaseFieldPath, typeEnvironment)
            || HasLocalAggregateUnionOwner(
                selection.ArrayBaseLeafFields, typeEnvironment);
    }

    private static bool HasLocalAggregateUnionOwner(
        ImmutableArray<BoundNativeFixedArrayFieldProjection> fields,
        LocalAggregateLayoutTypeEnvironment typeEnvironment)
    {
        if (fields.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var field in fields)
        {
            if (IsLocalAggregateUnionType(field.OwnerType,
                typeEnvironment))
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<byte> ReadLocalAggregateReturnCoverage(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int methodRow, PrimitiveType returnType,
        LocalAggregateLayoutTypeEnvironment typeEnvironment)
    {
        var contracts
            = tables.Contracts.GetReturnCoverageContracts(methodRow);
        if (contracts.IsEmpty)
        {
            return [];
        }
        if (!IsLocalAggregateUnionType(returnType, typeEnvironment))
        {
            throw Invalid(
                "L1 ReturnCoverage does not target a Raw Union result.");
        }

        var coverageLength = ValueLayout.Size(returnType,
            typeEnvironment.Structures);
        if (coverageLength is <= 0
            or > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "L1 ReturnCoverage exceeds the bounded in-memory coverage representation.");
        }

        var initializedBytes = new byte[coverageLength];
        var remainingWork = SemanticVerifier.MaximumAnalysisVisits;
        foreach (var contract in contracts)
        {
            if (contract.TargetProfileRow
                != typeEnvironment.TargetProfileRow)
            {
                throw Invalid(
                    "L1 ReturnCoverage selects a different TargetProfile.");
            }

            foreach (var region in contract.Regions)
            {
                MarkLocalAggregateReturnCoverage(
                    initializedBytes, region.Offset, region.Length,
                    region.Repetitions, 0, ref remainingWork);
            }
        }

        return [.. initializedBytes];
    }

    private static void MarkLocalAggregateReturnCoverage(
        byte[] initializedBytes, ulong offset, ulong length,
        ImmutableArray<TrRmReturnCoverageRepetition> repetitions,
        int repetitionIndex, ref int remainingWork)
    {
        if (--remainingWork < 0)
        {
            throw Unsupported(
                "L1 ReturnCoverage reconstruction exceeds the analysis work limit.");
        }

        if (repetitionIndex >= repetitions.Length)
        {
            if (offset > (ulong)initializedBytes.Length
                || length > (ulong)initializedBytes.Length - offset)
            {
                throw Invalid(
                    "L1 ReturnCoverage exceeds its exact result extent.");
            }

            Array.Fill(initializedBytes, (byte)1,
                checked((int)offset), checked((int)length));
            remainingWork = checked(remainingWork
                - Math.Min(remainingWork, checked((int)length)));
            return;
        }

        var repetition = repetitions[repetitionIndex];
        if (repetition.Count == 0)
        {
            throw Invalid(
                "L1 ReturnCoverage contains an empty repetition.");
        }

        var iterationCount = repetition.Stride == 0
            ? 1UL : repetition.Count;
        if (iterationCount > (ulong)remainingWork)
        {
            throw Unsupported(
                "L1 ReturnCoverage repetition exceeds the analysis work limit.");
        }

        for (ulong iteration = 0; iteration < iterationCount; iteration++)
        {
            if (iteration != 0
                && repetition.Stride
                    > (ulong.MaxValue - offset) / iteration)
            {
                throw Invalid(
                    "L1 ReturnCoverage repetition offset overflows.");
            }

            var repeatedOffset = offset
                + (iteration * repetition.Stride);
            MarkLocalAggregateReturnCoverage(initializedBytes,
                repeatedOffset, length, repetitions, repetitionIndex + 1,
                ref remainingWork);
        }
    }

    private static uint ReadLocalAggregateBodyTypeSpec(
        TrRmRegisterBodyReader body, int typeMapIndex)
    {
        if ((uint)typeMapIndex >= (uint)body.TypeCount)
        {
            throw Invalid("An L1 TypeMap index is outside its table.");
        }

        return ReadForeignInvocationBodyTypeSpec(body,
            checked((ushort)typeMapIndex));
    }

    private static BoundNativeFixedArrayLayout
        GetLocalAggregateArrayLayout(
        LocalAggregateLayoutTypeEnvironment typeEnvironment,
        PrimitiveType arrayType)
    {
        if (!NativeFixedArrayTypes.IsType(arrayType))
        {
            throw Invalid("An L1 array operation has a non-array type.");
        }

        var index = NativeFixedArrayTypes.Index(arrayType);
        if ((uint)index >= (uint)typeEnvironment.FixedArrayTypes.Length)
        {
            throw Invalid("An L1 array type has no reconstructed layout.");
        }

        var layout = typeEnvironment.FixedArrayTypes[index];
        return new BoundNativeFixedArrayLayout(arrayType,
            layout.ElementType, layout.ElementCount, layout.ElementStride,
            layout.ByteExtent, layout.Alignment, layout.RequiredAlignment);
    }

    private static int GetLocalAggregateFieldOrdinal(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int fieldRow, PrimitiveType expectedOwner,
        LocalAggregateLayoutTypeEnvironment typeEnvironment)
    {
        if (fieldRow <= 0 || fieldRow > tables.Fields.Count)
        {
            throw Invalid("An L1 PlaceSelection FieldDef row is invalid.");
        }

        var ownerTypeDefRow = tables.Fields.GetOwnerTypeDef(fieldRow);
        if ((uint)ownerTypeDefRow
                >= (uint)typeEnvironment.StructureTypeByTypeDef.Length
            || typeEnvironment.StructureTypeByTypeDef[ownerTypeDefRow]
                != (int)expectedOwner - 100)
        {
            throw Invalid(
                "An L1 PlaceSelection FieldDef has the wrong nominal owner.");
        }

        var layoutRow = tables.Layouts.GetLayoutForProfile(
            ownerTypeDefRow,
            checked((uint)typeEnvironment.TargetProfileRow));
        var (firstField, fieldCount)
            = tables.Layouts.GetLayoutFieldRange(layoutRow);
        for (var ordinal = 0; ordinal < fieldCount; ordinal++)
        {
            if (tables.LayoutFields.GetFieldDef(firstField + ordinal)
                == fieldRow)
            {
                return ordinal;
            }
        }

        throw Invalid(
            "An L1 PlaceSelection FieldDef is not in its selected Layout.");
    }
}
