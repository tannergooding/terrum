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
        PrimitiveType ElementType, ulong ElementCount,
        int ArrayInstanceKey, ulong SelectedElementIndex);

    private readonly record struct NativeAddressRegisterConstant(
        PrimitiveType Type, uint Bits);

    private readonly record struct NativeAddressPendingComparison(
        int Destination, BoundRead Left, BoundRead Right);

    private readonly record struct NativeAddressPendingBooleanXor(
        int Destination, BoundRead Left, BoundRead Right);

    private sealed record NativeAddressConditionalReadState(
        bool[] LocalInitializedByHome,
        Dictionary<int, NativeStorageAddressFacts> StorageAddressesByHome,
        HashSet<int> ConsumedStorageHomes,
        List<int> CallArgumentHomeOrder,
        Dictionary<int, long> LocalConstantByHome,
        Dictionary<int, NativeArrayPlaceSelectionFacts> PlaceSelectionsByRegister,
        Dictionary<int, NativeAddressRegisterConstant> RegisterConstants,
        Dictionary<int, BoundExpression> RegisterExpressions,
        Dictionary<int, List<BoundExpression>> ArrayInitializationElementsByHome);

    private sealed class NativeAddressConditionalFrame(
        BoundExpression condition,
        NativeAddressConditionalReadState initialState,
        List<BoundStatement> parentStatements)
    {
        internal BoundExpression Condition { get; } = condition;

        internal NativeAddressConditionalReadState InitialState { get; }
            = initialState;

        internal List<BoundStatement> ParentStatements { get; }
            = parentStatements;

        internal List<BoundStatement> TrueStatements { get; } = [];

        internal List<BoundStatement> FalseStatements { get; } = [];

        internal NativeAddressConditionalReadState? TrueState { get; set; }

        internal bool HasElse { get; set; }
    }

    private readonly record struct NativeAddressDirectCallTarget(
        int FunctionIndex, ImmutableArray<PrimitiveType> ParameterTypes,
        PrimitiveType ReturnType,
        ImmutableArray<DirectNativeParameter> NativeParameters);

    internal static BoundProgram LoadForeignInvocationDraftNativeAddressLibrary(
        ReadOnlySpan<byte> artifact)
        => LoadForeignInvocationDraftNativeAddressLibrary(artifact, 1, null,
            verifySemantics: true,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);

    private static BoundProgram LoadForeignInvocationDraftNativeAddressLibrary(
        ReadOnlySpan<byte> artifact,
        IReadOnlyDictionary<int, BoundModuleImport>? selectedModuleImports,
        bool verifySemantics)
        => LoadForeignInvocationDraftNativeAddressLibrary(artifact, 1,
            selectedModuleImports, verifySemantics,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);

    private static BoundProgram LoadForeignInvocationDraftNativeAddressLibrary(
        ReadOnlySpan<byte> artifact, int selectedModuleRow,
        IReadOnlyDictionary<int, BoundModuleImport>? selectedModuleImports,
        bool verifySemantics,
        TrRmArtifactSchema schema = TrRmArtifactSchema.ForeignInvocationDraft,
        TrRmA3LayoutResourceLedger? a3LayoutResourceLedger = null,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings
            = default)
    {
        var index = a3LayoutResourceLedger is not null
            ? new TrRmArtifactIndex(artifact, schema,
                a3LayoutResourceLedger)
            : schema.UsesForeignInvocationStorageFixedArrays()
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
                    .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                        index, a3LayoutResourceLedger,
                        createA3ResourceLedger: a3LayoutResourceLedger is null)
                : schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
                    ? TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                            index)
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
        return LoadForeignInvocationDraftNativeAddressLibrary(index, tables,
            selectedModuleRow, selectedModuleImports, verifySemantics, schema,
            a3LayoutResourceLedger, typeProviderBindings);
    }

    private static BoundProgram LoadForeignInvocationDraftNativeAddressLibrary(
        TrRmArtifactIndex index,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int selectedModuleRow,
        IReadOnlyDictionary<int, BoundModuleImport>? selectedModuleImports,
        bool verifySemantics,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? a3LayoutResourceLedger,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings)
    {
        ValidateNativeAddressArtifactSections(index, tables, schema);

        var selectedModuleIsValid = selectedModuleRow > 0
            && selectedModuleRow <= tables.Modules.Count;
        var (methodStart, methodEnd) = selectedModuleIsValid
            ? tables.Modules.GetMethodDefRange(selectedModuleRow)
            : (0, 0);
        var (moduleRefStart, moduleRefEnd) = selectedModuleIsValid
            ? tables.Modules.GetModuleRefRange(selectedModuleRow)
            : (0, 0);
        var selectedImportCount = 0;
        var selectedImportRows = new Dictionary<int, int>();
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
                    var expectedDefinition = tables.Imports
                        .GetExpectedDefinitionForImportRow(importRow);
                    if ((expectedDefinition >> 24) == 0x16)
                    {
                        var methodRow = checked((int)
                            (expectedDefinition & 0x00FF_FFFF));
                        if (!selectedImportRows.TryAdd(methodRow, importRow))
                        {
                            throw Invalid(
                                "A root MethodDef has more than one retained Import.");
                        }
                    }
                }
            }
        }

        if ((schema is TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft)
            && tables.TypeDefs.Count != 0 && methodEnd == methodStart
            && selectedModuleImports is null)
        {
            return LoadForeignInvocationFixedArrayTypeLibrary(tables,
                selectedModuleRow, verifySemantics, typeProviderBindings);
        }

        ValidateNativeAddressArtifactTables(tables);
        if (tables.Schema != schema
            || !selectedModuleIsValid
            || tables.Modules.GetRequirements(selectedModuleRow) != (1u, 0u)
            || methodEnd - methodStart < 3
            || (selectedModuleImports is null
                ? moduleRefEnd != moduleRefStart || selectedImportCount != 0
                : moduleRefEnd == moduleRefStart
                    || selectedImportCount != selectedModuleImports.Count
                    || selectedImportRows.Count
                        != selectedModuleImports.Count))
        {
            throw Unsupported(
                "The source-free loader supports one caller Body, one address anchor and direct NativeImport methods under the selected profile.");
        }

        if (selectedModuleImports is not null)
        {
            foreach (var (methodRow, selectedModuleImport)
                in selectedModuleImports)
            {
                if (!selectedImportRows.TryGetValue(methodRow,
                        out var importRow))
                {
                    throw Invalid(
                        "A selected ModuleImport has no matching root Import.");
                }

                var moduleRefRow = tables.Imports
                    .GetModuleRefForImportRow(importRow);
                var expectedDefinition = tables.Imports
                    .GetExpectedDefinitionForImportRow(importRow);
                if (expectedDefinition != (0x1600_0000u | (uint)methodRow)
                    || moduleRefRow < moduleRefStart
                    || moduleRefRow >= moduleRefEnd
                    || tables.ModuleRefs.GetOwnerModule(moduleRefRow)
                        != selectedModuleRow
                    || !tables.Imports.TryGetExpectedBinding(
                        expectedDefinition, out var expectedBinding))
                {
                    throw Invalid(
                        "A selected ModuleImport does not match its root Import.");
                }

                var moduleRefIdentity
                    = tables.ModuleRefs.GetProducerIdentity(moduleRefRow);
                var (moduleRefTargetRequirement, moduleRefRuntimePolicy)
                    = tables.ModuleRefs.GetRequirements(moduleRefRow);
                if (!StringComparer.Ordinal.Equals(
                        moduleRefIdentity,
                        expectedBinding.ProducerIdentity)
                    || moduleRefTargetRequirement
                        != expectedBinding.TargetRequirement
                    || moduleRefRuntimePolicy
                        != expectedBinding.RuntimePolicy
                    || !StringComparer.Ordinal.Equals(
                        expectedBinding.ProducerIdentity,
                        selectedModuleImport.ProducerIdentity)
                    || !StringComparer.Ordinal.Equals(
                        expectedBinding.ExportKey,
                        selectedModuleImport.ExportKey)
                    || expectedBinding.TargetRequirement
                        != selectedModuleImport.TargetRequirement
                    || expectedBinding.RuntimePolicy
                        != selectedModuleImport.RuntimePolicy)
                {
                    throw Invalid(
                        "A selected ModuleImport differs from its retained Import expectation.");
                }
            }
        }

        var callerMethodRow = 0;
        var anchorMethodRow = 0;
        var nativeImportMethodRows = new List<int>(
            tables.StandardNativeImports.Count);
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
                nativeImportMethodRows.Add(row);
            }
            else if (tables.Methods.GetBodyRow(row) != 0)
            {
                if (callerMethodRow != 0)
                {
                    throw Unsupported("Multiple caller Bodies are unsupported.");
                }

                callerMethodRow = row;
            }
            else
            {
                throw Unsupported(
                    "The source-free NativeAddress loader supports only direct NativeImports, one caller and one anchor.");
            }
        }

        var importedNativeImportCount = selectedImportRows.Count;
        var ownedNativeImportCount = nativeImportMethodRows.Count
            - importedNativeImportCount;
        var isExpandedFProviderModule
            = schema == TrRmArtifactSchema.ForeignInvocationDraft
                && selectedModuleImports is { Count: > 0 }
                && selectedModuleImports.Count == importedNativeImportCount
                && importedNativeImportCount <= nativeImportMethodRows.Count
                && selectedImportRows.Keys.All(
                    nativeImportMethodRows.Contains)
                && ownedNativeImportCount is 0 or 1;
        if (nativeImportMethodRows.Count
                != methodEnd - methodStart - 2
            || (nativeImportMethodRows.Count > 1
                && !schema.UsesForeignInvocationStorageAddresses()
                && !isExpandedFProviderModule)
            || callerMethodRow == 0 || anchorMethodRow == 0
            || callerMethodRow == anchorMethodRow
            || tables.Methods.GetOwnerType(callerMethodRow) != 0
            || tables.Methods.GetOwnerType(anchorMethodRow) != 0
            || tables.Methods.GetOwnerModule(callerMethodRow) != selectedModuleRow
            || tables.Methods.GetOwnerModule(anchorMethodRow) != selectedModuleRow
            || tables.Methods.GetFlags(anchorMethodRow) != 0
            || tables.Methods.GetBodyRow(anchorMethodRow) != 0)
        {
            throw Invalid("Invalid bounded foreign-invocation method roles.");
        }

        if (schema == TrRmArtifactSchema.ForeignInvocationDraft
            && nativeImportMethodRows.Count > 1)
        {
            var functionNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var importMethodRow in nativeImportMethodRows)
            {
                if (!functionNames.Add(
                        tables.Methods.GetName(importMethodRow)))
                {
                    throw Unsupported(
                        "The expanded F Module requires distinct native callable MethodDef names.");
                }
            }

            if (!functionNames.Add(tables.Methods.GetName(callerMethodRow)))
            {
                throw Unsupported(
                    "The expanded F Module requires the caller name to differ from every native callable MethodDef name.");
            }
        }

        foreach (var importMethodRow in nativeImportMethodRows)
        {
            if (tables.Methods.GetOwnerType(importMethodRow) != 0
                || tables.Methods.GetOwnerModule(importMethodRow)
                    != selectedModuleRow
                || tables.Methods.GetBodyRow(importMethodRow) != 0)
            {
                throw Invalid(
                    "A direct NativeImport method has an invalid owner or Body.");
            }
        }

        tables.ValidateRegisterBodyMapsForMethod(callerMethodRow);

        var entryMethodRow = tables.Modules.GetEntryMethod(selectedModuleRow);
        if (entryMethodRow != 0 && entryMethodRow != callerMethodRow)
        {
            throw Unsupported(
                "The source-free NativeAddress loader supports the caller as the Module entry.");
        }

        foreach (var importMethodRow in nativeImportMethodRows)
        {
            EnsureNativeAddressContracts(tables.Methods, tables.Contracts,
                importMethodRow,
                tables.Contracts.GetNativeParameters(importMethodRow).Length + 1,
                allowEffects: true);
            if (tables.Contracts.GetEffectsMask(importMethodRow) != 4
                || !tables.Contracts.GetNativeOutputs(importMethodRow).IsEmpty)
            {
                throw Unsupported(
                    "The source-free NativeAddress loader supports caller-unsafe imports without output contracts or additional effects.");
            }
        }

        EnsureNativeAddressContracts(tables.Methods, tables.Contracts,
            callerMethodRow, 0, allowEffects: false);
        EnsureNativeAddressContracts(tables.Methods, tables.Contracts,
            anchorMethodRow, tables.Contracts.GetNativeParameters(anchorMethodRow).Length,
            allowEffects: false);
        if (tables.Contracts.GetEffectsMask(callerMethodRow) != 0
            || tables.Contracts.GetEffectsMask(anchorMethodRow) != 0
            || !tables.Contracts.GetNativeOutputs(anchorMethodRow).IsEmpty)
        {
            throw Unsupported(
                "The source-free NativeAddress loader supports caller-unsafe imports without output contracts or additional effects.");
        }

        return ReconstructForeignInvocationNativeAddressLibrary(tables,
            selectedModuleRow, selectedModuleImports, verifySemantics, schema,
            methodStart, methodEnd, selectedImportRows,
            nativeImportMethodRows, callerMethodRow, anchorMethodRow,
            entryMethodRow);
    }

    private static void ValidateNativeAddressArtifactSections(
        TrRmArtifactIndex index,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        TrRmArtifactSchema schema)
    {
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            if (kind is not (0x0001 or 0x0002 or 0x0004 or 0x0010 or 0x0011
                or 0x0016 or 0x0017 or 0x0018 or 0x001B or 0x001C
                or 0x001D or 0x0020 or 0x0021)
                && !(kind == 0x0012
                    && (schema is
                        TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft))
                && !((schema
                        is TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft)
                    && tables.TypeDefs.Count != 0
                    && (kind == 0x0013 || kind == 0x0014
                        || kind == 0x0015 || kind == 0x0023))
                && !(kind == 0x0048
                    && (tables.FixedArrayLayouts.Count != 0
                        || tables.TypeDefs.Count != 0)
                    && (schema is
                        TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                        or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft)))
            {
                throw Unsupported(
                    $"Section 0x{kind:X4} is outside the bounded foreign-invocation source-free loader.");
            }
        }
    }

    private static void ValidateNativeAddressArtifactTables(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
    {
        if (tables.TypeDefs.Count != 0
            || tables.Fields.Count != 0
            || tables.EnumDefs.Count != 0
            || tables.EnumMembers.Count != 0
            || tables.Interfaces.Count != 0
            || tables.NativeFunctionPointerSignatures.Count != 0
            || tables.NativeCallables.Count != 0
            || tables.NativeVTables.Count != 0
            || tables.NativeExports.Count != 0)
        {
            throw Unsupported(
                "The source-free loader supports one caller Body, one address anchor and direct NativeImport methods under the selected profile.");
        }
    }

    private static BoundProgram ReconstructForeignInvocationNativeAddressLibrary(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int selectedModuleRow,
        IReadOnlyDictionary<int, BoundModuleImport>? selectedModuleImports,
        bool verifySemantics, TrRmArtifactSchema schema,
        int methodStart, int methodEnd,
        Dictionary<int, int> selectedImportRows,
        List<int> nativeImportMethodRows, int callerMethodRow,
        int anchorMethodRow, int entryMethodRow)
    {
        var candidateBody = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerMethodRow),
            tables.Contracts.Count);
        var targetMethodRow = ReadForeignInvocationNativeAddressTargetMethod(
            candidateBody, tables.Imports);
        if (!nativeImportMethodRows.Contains(targetMethodRow))
        {
            throw Invalid(
                "The NativeAddress target is not a selected direct NativeImport.");
        }

        if (selectedModuleImports is not null
            && selectedImportRows.ContainsKey(targetMethodRow)
            && !selectedModuleImports.ContainsKey(targetMethodRow))
        {
            throw Invalid(
                "The NativeAddress target has no selected provider binding.");
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

        var body = candidateBody;
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
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft)
                ? ReadForeignInvocationFixedArrayTypes(tables,
                    selectedModuleRow, nativePointerTypeDefinitions,
                    signatureIndexByAnchorMethod, structureTypeByTypeDef,
                    [], out arrayTypeByTypeSpecRow)
                : [];
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
        var directCallTargets
            = new Dictionary<int, NativeAddressDirectCallTarget>(
                nativeImportMethodRows.Count);
        var directNativeImportsByMethodRow
            = new Dictionary<int, DirectNativeImport>(
                nativeImportMethodRows.Count);
        foreach (var importMethodRow in nativeImportMethodRows)
        {
            var importSignatureRow
                = tables.Methods.GetSignature(importMethodRow);
            if (!TrRmNativeImportTypeContract.TryGetDirectNativeImportResultType(
                    tables.TypeSpecs,
                    tables.Signatures.GetResultTypeSpec(importSignatureRow),
                    out _))
            {
                throw Unsupported(
                    "A direct NativeImport result type is unsupported.");
            }

            var parameterTypes = ReadForeignInvocationParameterTypes(
                importMethodRow, tables.Methods, tables.Signatures,
                tables.Parameters, tables.TypeSpecs,
                nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
            var resultType = ReadForeignInvocationType(tables.TypeSpecs,
                tables.Signatures.GetResultTypeSpec(importSignatureRow),
                nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                structureTypeByTypeDef);
            if (!IsForeignInvocationValueType(resultType, allowVoid: true))
            {
                throw Unsupported(
                    "A direct NativeImport result type is unsupported.");
            }

            var nativeParameters = ReadForeignInvocationNativeParameters(
                importMethodRow, tables.Methods, tables.Signatures,
                tables.Parameters, tables.TypeSpecs, tables.Contracts,
                tables.Layouts, nativePointerTypeDefinitions,
                structureTypeByTypeDef);
            var importRow
                = tables.StandardNativeImports.GetImportForMethod(
                    importMethodRow);
            directCallTargets.Add(importMethodRow,
                new NativeAddressDirectCallTarget(
                    functionIndexByMethodRow[importMethodRow],
                    parameterTypes, resultType, nativeParameters));
            directNativeImportsByMethodRow.Add(importMethodRow,
                new DirectNativeImport(
                    tables.StandardNativeImports.GetLibraryName(importRow),
                    tables.StandardNativeImports.GetEntryName(importRow), [])
                {
                    CallingConvention
                        = (NativeCallingConvention)importRow.CallConv,
                    ResolutionMode
                        = (NativeImportResolutionMode)importRow.ResolutionMode,
                    ResolutionPolicy
                        = (NativeImportResolutionPolicy)importRow.ResolutionPolicy,
                    Parameters = nativeParameters,
                });
        }

        var (callerBody, callerVariables) = ReadForeignInvocationCallerBody(
            body, tables.TypeSpecs, tables.Imports,
            tables.TargetProfiles, tables.FixedArrayLayouts,
            arrayTypeByTypeSpecRow,
            nativeFixedArrayTypes, selectedModuleRow,
            targetMethodRow, targetIndex,
            callerParameterTypes, callerResultType,
            targetParameterTypes, functionPointerSignatures[0],
            nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
            structureTypeByTypeDef, directCallTargets);
        var typeArray = nativePointerTypeDefinitions.ToImmutableArray();
        if (!NativePointerTypeContract.IsValidTable(typeArray, [],
                functionPointerSignatures))
        {
            throw Invalid("Invalid native pointer type table.");
        }

        var functions = ImmutableArray.CreateBuilder<BoundFunction>(
            methodEnd - methodStart - 1);
        for (var row = methodStart; row < methodEnd; row++)
        {
            if (row == anchorMethodRow)
            {
                continue;
            }

            var flags = tables.Methods.GetFlags(row);
            BoundFunction function;
            if (row == callerMethodRow)
            {
                function = new BoundFunction(tables.Methods.GetName(row),
                    callerResultType, callerVariables,
                    callerParameterTypes.Length, callerBody, default)
                {
                    IsPrivate = flags == 0,
                    IsInternal = flags == 1,
                };
            }
            else
            {
                if (!directCallTargets.TryGetValue(row,
                        out var importedTarget))
                {
                    throw Invalid(
                        "A non-caller MethodDef has no NativeImport binding.");
                }

                var selectedModuleImport
                    = selectedModuleImports is not null
                        && selectedModuleImports.TryGetValue(row,
                            out var moduleImport)
                            ? moduleImport : null;
                function = new BoundFunction(tables.Methods.GetName(row),
                    importedTarget.ReturnType,
                    CreateVariables(importedTarget.ParameterTypes, "arg"),
                    importedTarget.ParameterTypes.Length,
                    new BoundBlock([], default), default)
                {
                    DirectNativeImport
                        = directNativeImportsByMethodRow[row],
                    ModuleImport = selectedModuleImport,
                    IsModuleReference = selectedModuleImport is not null,
                    IsExternal = true,
                    RequiresUnsafeCaller = true,
                    IsPrivate = flags == 0,
                    IsInternal = flags == 1,
                };
            }
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
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef,
        IReadOnlyDictionary<int, NativeAddressDirectCallTarget>
            directCallTargets)
    {
        if (body.RecordCount < 2)
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
                signatureIndexByAnchorMethod, structureTypeByTypeDef,
                directCallTargets);
        }

        if (body.BlockCount != 1)
        {
            throw Unsupported(
                "The caller Body is outside the bounded NativeAddress subset.");
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
            int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef,
            IReadOnlyDictionary<int, NativeAddressDirectCallTarget>
                directCallTargets)
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
        var directUnitCallRecordIndexes = new HashSet<int>();
        var nativeAddressRecordIndex = -1;
        var callSiteRecordIndex = -1;
        var returnHome = -1;
        var rawInstructions
            = ReadForeignInvocationStorageBodyInstructions(body);
        for (var instructionIndex = 0;
            instructionIndex < rawInstructions.Count; instructionIndex++)
        {
            var instruction = rawInstructions[instructionIndex].AsSpan();
            switch (instruction[0])
            {
                case 0xFC:
                    var conditionalRecordIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    if (!referencedRecords.Add(conditionalRecordIndex))
                    {
                        throw Invalid(
                            "An A2EU ConditionalEdge record is referenced more than once.");
                    }
                    instructions.Add((0xFC, conditionalRecordIndex,
                        instruction[1]));
                    break;

                case 0xFD:
                case 0xFE:
                    instructions.Add((instruction[0], 0, 0));
                    break;

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
                    if ((storeControl >> 4) != 1)
                    {
                        throw Unsupported(
                            "F+S local initialization requires an admitted CopyRead source.");
                    }
                    instructions.Add((0xD6,
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]), checked((byte)longSource)));
                    break;

                case 0x13:
                case 0x10:
                case 0x20:
                case 0x21:
                case 0x24:
                case 0x25:
                case 0x32:
                case 0x33:
                    if (instruction[0] is 0x24 or 0x25
                        && !body.Schema
                            .UsesForeignInvocationStorageFixedArrayUnitInitialization())
                    {
                        throw Unsupported(
                            "Checked scalar-call argument capture requires the exact A2EU schema.");
                    }
                    instructions.Add((instruction[0], instruction[1], 0));
                    break;

                case 0x40:
                case 0x41:
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

                case 0x91:
                    var shortPlaceRecordIndex = instruction[1] >> 4;
                    if (!referencedRecords.Add(shortPlaceRecordIndex))
                    {
                        throw Invalid(
                            "An F+S place record is referenced more than once.");
                    }
                    instructions.Add((0xD8, shortPlaceRecordIndex,
                        checked((byte)(instruction[1] & 0x0F))));
                    break;

                case 0x92:
                    var shortStoreRecordIndex = instruction[1];
                    if (!referencedRecords.Add(shortStoreRecordIndex))
                    {
                        throw Invalid(
                            "An F+S place record is referenced more than once.");
                    }
                    instructions.Add((0xD9, shortStoreRecordIndex, 0));
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
                        var callSiteTargetKind
                            = body.GetRecord(recordIndex)[4];
                        if (callSiteTargetKind == 3)
                        {
                            if (callSiteRecordIndex != -1)
                            {
                                throw Unsupported(
                                    "The F+S Body supports one indirect CallSite.");
                            }
                            callSiteRecordIndex = recordIndex;
                        }
                        else if (callSiteTargetKind == 1
                            && body.Schema
                                .UsesForeignInvocationStorageFixedArrayUnitInitialization())
                        {
                            if (!directUnitCallRecordIndexes.Add(recordIndex))
                            {
                                throw Invalid(
                                    "An A2EU direct CallSite is referenced more than once.");
                            }
                        }
                        else
                        {
                            throw Unsupported(
                                "The F+S Body contains an unsupported direct CallSite.");
                        }
                    }
                    instructions.Add((instruction[0], recordIndex, 0));
                    break;

                case 0xB8:
                    if (returnHome != -1
                        || instructionIndex + 1 != rawInstructions.Count)
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

        var nonLocalHomeResults = new HashSet<int>();
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] == 0x0E && record[5] == 2)
            {
                _ = nonLocalHomeResults.Add(
                    BinaryPrimitives.ReadUInt16LittleEndian(record[8..]));
            }
        }

        var localInitializedByHome = new bool[body.HomeCount];
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            if (body.HomeMap[homeIndex * 8] != 3
                || nonLocalHomeResults.Contains(homeIndex))
            {
                continue;
            }

            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap[((homeIndex * 8) + 2)..]);
            var localType = ReadForeignInvocationType(typeSpecs,
                ReadForeignInvocationBodyTypeSpec(body, typeMapIndex),
                nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                structureTypeByTypeDef,
                arrayTypeByTypeSpecRow: arrayTypeByTypeSpecRow);
            var localVariable = variables.Count;
            localVariableByHome[homeIndex] = localVariable;
            variables.Add(new BoundVariable(
                $"__trrm_home_{homeIndex}", localType,
                body.HomeMap[(homeIndex * 8) + 1] != 0, default));
        }

        var storageAddressesByHome
            = new Dictionary<int, NativeStorageAddressFacts>();
        var consumedStorageHomes = new HashSet<int>();
        var callArgumentHomeOrder = new List<int>();
        var localConstantByHome = new Dictionary<int, long>();
        var placeSelectionsByRegister
            = new Dictionary<int, NativeArrayPlaceSelectionFacts>();
        var registerConstants
            = new Dictionary<int, NativeAddressRegisterConstant>();
        var registerExpressions = new Dictionary<int, BoundExpression>();
        if (directUnitCallRecordIndexes.Count > 0)
        {
            registerConstants.Add(14, new NativeAddressRegisterConstant(
                PrimitiveType.Int32, 0));
        }
        var arrayInitializationElementsByHome
            = new Dictionary<int, List<BoundExpression>>();
        var rootStatements = new List<BoundStatement>();
        var currentStatements = rootStatements;
        var conditionalFrames
            = new Stack<NativeAddressConditionalFrame>();
        var sawCallSite = false;
        var sawReturn = false;
        var instructionOrdinal = 0;
        NativeAddressPendingComparison? pendingComparison = null;
        NativeAddressPendingBooleanXor? pendingBooleanXor = null;

        void AppendArrayInitializationElement(
            TrRmRegisterBodyReader bodyReader,
            int arrayInstanceKey, ulong elementIndex, BoundExpression element)
        {
            var rootHome = -1;
            var arrayType = PrimitiveType.Void;
            var elementCount = 0UL;
            NativeArrayPlaceSelectionFacts? childSelection = null;
            if (arrayInstanceKey < 0)
            {
                rootHome = checked(-arrayInstanceKey - 1);
                if ((uint)rootHome >= (uint)localVariableByHome.Length
                    || localVariableByHome[rootHome] < 0)
                {
                    throw Invalid(
                        "The F+S root array initialization has no local Home.");
                }

                arrayType = variables[localVariableByHome[rootHome]].Type;
            }
            else
            {
                foreach (var placeSelection in placeSelectionsByRegister.Values)
                {
                    if (placeSelection.RecordIndex == arrayInstanceKey)
                    {
                        childSelection = placeSelection;
                        break;
                    }
                }

                if (childSelection is not { } selectedChild
                    || !NativeFixedArrayTypes.IsType(
                        selectedChild.ElementType))
                {
                    throw Invalid(
                        "The F+S nested array initialization has no selected child Home.");
                }

                rootHome = selectedChild.BaseHome;
                arrayType = selectedChild.ElementType;
                elementCount = nativeFixedArrayTypes[
                    NativeFixedArrayTypes.Index(arrayType)].ElementCount;
            }

            if (arrayInstanceKey < 0)
            {
                if (!NativeFixedArrayTypes.IsType(arrayType))
                {
                    throw Invalid(
                        "The F+S root array initialization Home has a non-array type.");
                }

                elementCount = nativeFixedArrayTypes[
                    NativeFixedArrayTypes.Index(arrayType)].ElementCount;
            }

            if (localInitializedByHome[rootHome] || elementCount == 0
                || elementIndex
                    != (ulong)GetArrayInitializationElementCount(
                        arrayInstanceKey,
                        arrayInitializationElementsByHome)
                || elementIndex >= elementCount)
            {
                throw Invalid(
                    "The F+S nested array elements are not complete and source-ordered.");
            }

            if (!arrayInitializationElementsByHome.TryGetValue(
                    arrayInstanceKey, out var elements))
            {
                elements = [];
                arrayInitializationElementsByHome.Add(arrayInstanceKey,
                    elements);
            }
            elements.Add(element);
            if ((ulong)elements.Count != elementCount)
            {
                return;
            }

            var arrayLayout = nativeFixedArrayTypes[
                NativeFixedArrayTypes.Index(arrayType)];
            var initialization
                = new BoundNativeFixedArrayInitialization(
                    new BoundNativeFixedArrayLayout(arrayType,
                        arrayLayout.ElementType, arrayLayout.ElementCount,
                        arrayLayout.ElementStride, arrayLayout.ByteExtent,
                        arrayLayout.Alignment,
                        arrayLayout.RequiredAlignment),
                    [.. elements], default);
            _ = arrayInitializationElementsByHome.Remove(arrayInstanceKey);
            if (arrayInstanceKey < 0)
            {
                AddInitializedArrayLocal(arrayType, rootHome, elementCount,
                    elements, nativeFixedArrayTypes,
                    localVariableByHome, localInitializedByHome,
                    currentStatements);
                return;
            }

            var completedChild = childSelection!.Value;
            if (completedChild.ArrayInstanceKey == arrayInstanceKey)
            {
                throw Invalid(
                    "The F+S nested array selection has a self-parent.");
            }

            var placeRegister = completedChild.DestinationRegister;
            if (!placeSelectionsByRegister.TryGetValue(placeRegister,
                    out var liveChild)
                || liveChild.RecordIndex != arrayInstanceKey)
            {
                throw Invalid(
                    "The F+S nested array initialization lost its child place.");
            }
            _ = placeSelectionsByRegister.Remove(placeRegister);
            var completedElementIndex
                = ReadArrayInitializationElementIndex(completedChild,
                    bodyReader,
                    registerConstants, localConstantByHome);
            AppendArrayInitializationElement(bodyReader,
                completedChild.ArrayInstanceKey, completedElementIndex,
                initialization);
        }

        NativeAddressConditionalReadState CaptureConditionalState()
        {
            return new NativeAddressConditionalReadState(
                (bool[])localInitializedByHome.Clone(),
                new Dictionary<int, NativeStorageAddressFacts>(
                    storageAddressesByHome),
                [.. consumedStorageHomes],
                [.. callArgumentHomeOrder],
                new Dictionary<int, long>(localConstantByHome),
                new Dictionary<int, NativeArrayPlaceSelectionFacts>(
                    placeSelectionsByRegister),
                new Dictionary<int, NativeAddressRegisterConstant>(
                    registerConstants),
                new Dictionary<int, BoundExpression>(registerExpressions),
                arrayInitializationElementsByHome.ToDictionary(
                    static pair => pair.Key,
                    static pair => (List<BoundExpression>)[.. pair.Value]));
        }

        void RestoreConditionalState(
            NativeAddressConditionalReadState state)
        {
            Array.Copy(state.LocalInitializedByHome, localInitializedByHome,
                localInitializedByHome.Length);

            storageAddressesByHome.Clear();
            foreach (var pair in state.StorageAddressesByHome)
            {
                storageAddressesByHome.Add(pair.Key, pair.Value);
            }

            consumedStorageHomes.Clear();
            consumedStorageHomes.UnionWith(state.ConsumedStorageHomes);
            callArgumentHomeOrder.Clear();
            callArgumentHomeOrder.AddRange(state.CallArgumentHomeOrder);

            localConstantByHome.Clear();
            foreach (var pair in state.LocalConstantByHome)
            {
                localConstantByHome.Add(pair.Key, pair.Value);
            }

            placeSelectionsByRegister.Clear();
            foreach (var pair in state.PlaceSelectionsByRegister)
            {
                placeSelectionsByRegister.Add(pair.Key, pair.Value);
            }

            registerConstants.Clear();
            foreach (var pair in state.RegisterConstants)
            {
                registerConstants.Add(pair.Key, pair.Value);
            }

            registerExpressions.Clear();
            foreach (var pair in state.RegisterExpressions)
            {
                registerExpressions.Add(pair.Key, pair.Value);
            }

            arrayInitializationElementsByHome.Clear();
            foreach (var pair in state.ArrayInitializationElementsByHome)
            {
                arrayInitializationElementsByHome.Add(pair.Key,
                    [.. pair.Value]);
            }
        }

        void MergeConditionalStates(
            NativeAddressConditionalFrame frame,
            NativeAddressConditionalReadState falseState)
        {
            var trueState = frame.TrueState
                ?? throw Invalid("The A2EU conditional has no true-branch state.");
            var initial = frame.InitialState;
            if (!trueState.CallArgumentHomeOrder.SequenceEqual(
                    initial.CallArgumentHomeOrder)
                || !falseState.CallArgumentHomeOrder.SequenceEqual(
                    initial.CallArgumentHomeOrder)
                || !DictionaryEquals(trueState.PlaceSelectionsByRegister,
                    initial.PlaceSelectionsByRegister)
                || !DictionaryEquals(falseState.PlaceSelectionsByRegister,
                    initial.PlaceSelectionsByRegister)
                || !ArrayInitializationElementsEqual(
                    trueState.ArrayInitializationElementsByHome,
                    initial.ArrayInitializationElementsByHome)
                || !ArrayInitializationElementsEqual(
                    falseState.ArrayInitializationElementsByHome,
                    initial.ArrayInitializationElementsByHome))
            {
                throw Invalid(
                    "The A2EU conditional branches leave incompatible pending Body state.");
            }

            RestoreConditionalState(initial);
            for (var homeIndex = 0;
                homeIndex < localInitializedByHome.Length; homeIndex++)
            {
                localInitializedByHome[homeIndex]
                    = initial.LocalInitializedByHome[homeIndex]
                    || (trueState.LocalInitializedByHome[homeIndex]
                        && falseState.LocalInitializedByHome[homeIndex]);
            }

            foreach (var pair in trueState.StorageAddressesByHome)
            {
                if (!storageAddressesByHome.TryAdd(pair.Key, pair.Value)
                    && storageAddressesByHome[pair.Key] != pair.Value)
                {
                    throw Invalid(
                        "A2EU conditional branches assign one storage Home differently.");
                }
            }
            foreach (var pair in falseState.StorageAddressesByHome)
            {
                if (!storageAddressesByHome.TryAdd(pair.Key, pair.Value)
                    && storageAddressesByHome[pair.Key] != pair.Value)
                {
                    throw Invalid(
                        "A2EU conditional branches assign one storage Home differently.");
                }
            }

            consumedStorageHomes.UnionWith(
                trueState.ConsumedStorageHomes);
            consumedStorageHomes.UnionWith(
                falseState.ConsumedStorageHomes);
            foreach (var pair in trueState.LocalConstantByHome)
            {
                if (falseState.LocalConstantByHome.TryGetValue(pair.Key,
                        out var falseValue)
                    && falseValue == pair.Value)
                {
                    localConstantByHome[pair.Key] = pair.Value;
                }
            }
        }

        static bool DictionaryEquals<TKey, TValue>(
            Dictionary<TKey, TValue> left, Dictionary<TKey, TValue> right)
            where TKey : notnull
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var value)
                    || !EqualityComparer<TValue>.Default.Equals(
                        pair.Value, value))
                {
                    return false;
                }
            }

            return true;
        }

        static bool ArrayInitializationElementsEqual(
            Dictionary<int, List<BoundExpression>> left,
            Dictionary<int, List<BoundExpression>> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var elements)
                    || !pair.Value.SequenceEqual(elements))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var instruction in instructions)
        {
            if (pendingComparison is not null
                && instruction.Opcode != 0x41)
            {
                throw Invalid(
                    "The F+S comparison result is not immediately tested.");
            }

            if (pendingBooleanXor is { } booleanXor
                && instruction.Opcode is not (0x33 or 0xFC))
            {
                throw Invalid(
                    "The F+S Boolean XOR is not immediately used as a condition.");
            }

            if (pendingBooleanXor is { } branchXor
                && instruction.Opcode == 0xFC
                && instruction.Register != branchXor.Destination)
            {
                throw Invalid(
                    "The F+S Boolean XOR condition uses a different register.");
            }

            switch (instruction.Opcode)
            {
                case 0xFC:
                    BoundExpression condition;
                    if (instruction.Register >= 14
                        || !registerExpressions.TryGetValue(
                            instruction.Register, out var conditionExpression)
                        || conditionExpression.Type != PrimitiveType.Bool)
                    {
                        throw Invalid(
                            "The A2EU conditional register is not a checked Boolean.");
                    }

                    pendingBooleanXor = null;
                    condition = conditionExpression;
                    var conditionalFrame = new NativeAddressConditionalFrame(
                        condition, CaptureConditionalState(),
                        currentStatements);
                    conditionalFrames.Push(conditionalFrame);
                    currentStatements = conditionalFrame.TrueStatements;
                    break;

                case 0xFD:
                    if (!conditionalFrames.TryPeek(out var elseFrame)
                        || elseFrame.HasElse)
                    {
                        throw Invalid(
                            "The A2EU conditional has an unexpected false-branch marker.");
                    }

                    elseFrame.TrueState = CaptureConditionalState();
                    elseFrame.HasElse = true;
                    RestoreConditionalState(elseFrame.InitialState);
                    currentStatements = elseFrame.FalseStatements;
                    break;

                case 0xFE:
                    if (!conditionalFrames.TryPeek(out var endFrame)
                        || !endFrame.HasElse
                        || endFrame.TrueState is null)
                    {
                        throw Invalid(
                            "The A2EU conditional has an incomplete branch structure.");
                    }

                    var falseState = CaptureConditionalState();
                    MergeConditionalStates(endFrame, falseState);
                    _ = conditionalFrames.Pop();
                    currentStatements = endFrame.ParentStatements;
                    currentStatements.Add(new BoundIf(endFrame.Condition,
                        new BoundBlock([.. endFrame.TrueStatements], default),
                        new BoundBlock([.. endFrame.FalseStatements], default),
                        default));
                    break;

                case 0x14:
                case 0xD6:
                    var localHome = instruction.Index;
                    if ((uint)localHome >= (uint)body.HomeCount
                        || body.HomeMap[localHome * 8] != 3
                        || localVariableByHome[localHome] < 0
                        || localInitializedByHome[localHome]
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
                    BoundExpression localValue;
                    if (registerExpressions.Remove(instruction.Register,
                            out var copiedHomeExpression))
                    {
                        if (copiedHomeExpression.Type != localType
                            || (!DirectNativeImportContract.IsScalar(localType)
                                && !NativePointerTypes.IsType(localType)))
                        {
                            throw Invalid(
                                "The F+S Home copy has a mismatched source type.");
                        }

                        localValue = copiedHomeExpression;
                    }
                    else
                    {
                        if ((!DirectNativeImportContract.IsScalar(localType)
                                && !(localType == PrimitiveType.Void
                                    && body.Schema
                                        .UsesForeignInvocationStorageFixedArrayUnitInitialization()))
                            || !TryGetContextualConstant(instruction.Register,
                                localType, body.Schema, out var initialValue))
                        {
                            throw Unsupported(
                                "The F+S Home store has no admitted typed source.");
                        }

                        localValue = new BoundConstant(initialValue,
                            localType, default);
                        localConstantByHome.Add(localHome, initialValue);
                    }

                    var localVariable = localVariableByHome[localHome];
                    localInitializedByHome[localHome] = true;
                    currentStatements.Add(new BoundLocal(localVariable, localValue,
                        default));
                    break;

                case 0x13:
                    var shortLoadDestination = instruction.Index & 0x0F;
                    var shortLoadHome = instruction.Index >> 4;
                    if (shortLoadHome >= body.HomeCount
                        || localVariableByHome[shortLoadHome] < 0
                        || !localInitializedByHome[shortLoadHome]
                        || !localConstantByHome.TryGetValue(shortLoadHome,
                            out var shortLoadValue)
                        || variables[localVariableByHome[shortLoadHome]].Type
                            != PrimitiveType.Int32)
                    {
                        throw Invalid(
                            "The F+S array-index register load is unsupported.");
                    }

                    _ = registerExpressions.Remove(shortLoadDestination);
                    registerConstants[shortLoadDestination]
                        = new NativeAddressRegisterConstant(
                            PrimitiveType.Int32,
                            unchecked((uint)shortLoadValue));
                    break;

                case 0xD5:
                    var longLoadHome = instruction.Index;
                    var longLoadDestination = instruction.Register;
                    if (longLoadHome >= body.HomeCount
                        || longLoadDestination >= 14)
                    {
                        throw Invalid(
                            "The F+S Home load is invalid.");
                    }

                    var longLoadFormal
                        = formalOrdinalByHome[longLoadHome];
                    var longLoadVariable = longLoadFormal >= 0
                        ? longLoadFormal
                        : localVariableByHome[longLoadHome];
                    if (longLoadVariable < 0
                        || (longLoadFormal < 0
                            && !localInitializedByHome[longLoadHome]))
                    {
                        throw Invalid(
                            "The F+S Home load has no completed source.");
                    }

                    var longLoadType = variables[longLoadVariable].Type;
                    if (longLoadType == PrimitiveType.Int32
                        && localConstantByHome.TryGetValue(longLoadHome,
                            out var longLoadValue))
                    {
                        registerConstants[longLoadDestination]
                            = new NativeAddressRegisterConstant(
                                PrimitiveType.Int32,
                                unchecked((uint)longLoadValue));
                    }
                    else
                    {
                        _ = registerConstants.Remove(longLoadDestination);
                    }

                    registerExpressions[longLoadDestination] = new BoundRead(
                        longLoadVariable, longLoadType, default);
                    break;

                case 0x40:
                    var comparisonOperand
                        = checked((byte)instruction.Index);
                    var comparisonDestination = comparisonOperand & 0x0F;
                    var comparisonRight = comparisonOperand >> 4;
                    if (comparisonDestination >= 14
                        || comparisonRight >= 14
                        || !registerExpressions.TryGetValue(
                            comparisonDestination, out var comparisonLeftValue)
                        || comparisonLeftValue is not BoundRead comparisonLeft
                        || !registerExpressions.TryGetValue(comparisonRight,
                            out var comparisonRightValue)
                        || comparisonRightValue is not BoundRead comparisonRightRead
                        || comparisonLeft.Type != comparisonRightRead.Type
                        || comparisonLeft.Type == PrimitiveType.Bool
                        || !DirectNativeImportContract.IsScalar(
                            comparisonLeft.Type))
                    {
                        throw Invalid(
                            "The F+S comparison has unsupported scalar operands.");
                    }

                    _ = registerConstants.Remove(comparisonDestination);
                    _ = registerExpressions.Remove(comparisonDestination);
                    pendingComparison = new NativeAddressPendingComparison(
                        comparisonDestination, comparisonLeft,
                        comparisonRightRead);
                    break;

                case 0x32:
                    var booleanXorOperand
                        = checked((byte)instruction.Index);
                    var booleanXorDestination = booleanXorOperand & 0x0F;
                    var booleanXorRight = booleanXorOperand >> 4;
                    if (booleanXorDestination >= 14
                        || booleanXorRight >= 14
                        || !registerExpressions.TryGetValue(
                            booleanXorDestination, out var booleanXorLeftValue)
                        || booleanXorLeftValue is not BoundRead booleanXorLeft
                        || booleanXorLeft.Type != PrimitiveType.Bool
                        || !registerExpressions.TryGetValue(booleanXorRight,
                            out var booleanXorRightValue)
                        || booleanXorRightValue is not BoundRead booleanXorRightRead
                        || booleanXorRightRead.Type != PrimitiveType.Bool)
                    {
                        throw Invalid(
                            "The F+S Boolean XOR has unsupported operands.");
                    }

                    _ = registerConstants.Remove(booleanXorDestination);
                    registerExpressions[booleanXorDestination] = new BoundBinary(
                        TokenKind.Caret, booleanXorLeft, booleanXorRightRead,
                        PrimitiveType.Bool, default);
                    pendingBooleanXor = new NativeAddressPendingBooleanXor(
                        booleanXorDestination, booleanXorLeft,
                        booleanXorRightRead);
                    break;

                case 0x41:
                    var predicateOperand
                        = checked((byte)instruction.Index);
                    var predicateDestination = predicateOperand & 0x0F;
                    var predicateMask = predicateOperand >> 4;
                    if (pendingComparison is not { } comparison
                        || comparison.Destination != predicateDestination)
                    {
                        throw Invalid(
                            "The F+S predicate has no matching comparison.");
                    }

                    var comparisonOperator = predicateMask switch {
                        1 => TokenKind.Less,
                        2 => TokenKind.EqualsEquals,
                        3 => TokenKind.LessEquals,
                        4 => TokenKind.Greater,
                        6 => TokenKind.GreaterEquals,
                        13 => TokenKind.BangEquals,
                        _ => throw Unsupported(
                            "The F+S predicate mask has no admitted source comparison."),
                    };
                    _ = registerConstants.Remove(predicateDestination);
                    registerExpressions[predicateDestination] = new BoundBinary(
                        comparisonOperator, comparison.Left, comparison.Right,
                        PrimitiveType.Bool, default);
                    pendingComparison = null;
                    break;

                case 0x24:
                case 0x25:
                    var arithmeticOperand
                        = checked((byte)instruction.Index);
                    var arithmeticDestination = arithmeticOperand & 0x0F;
                    var arithmeticRight = arithmeticOperand >> 4;
                    if (arithmeticDestination >= 14
                        || arithmeticRight >= 14
                        || !registerExpressions.TryGetValue(
                            arithmeticDestination, out var arithmeticLeftValue)
                        || !registerExpressions.TryGetValue(arithmeticRight,
                            out var arithmeticRightValue)
                        || arithmeticLeftValue is not BoundRead arithmeticLeft
                        || arithmeticRightValue is not BoundRead
                        || arithmeticLeft.Type is not (
                            PrimitiveType.Int32 or PrimitiveType.UInt32)
                        || arithmeticRightValue.Type != arithmeticLeft.Type)
                    {
                        throw Invalid(
                            "The F+S checked division or remainder has unsupported operands.");
                    }

                    _ = registerConstants.Remove(arithmeticDestination);
                    registerExpressions[arithmeticDestination]
                        = new BoundBinary(instruction.Opcode == 0x24
                                ? TokenKind.Slash : TokenKind.Percent,
                            arithmeticLeft, arithmeticRightValue,
                            arithmeticLeft.Type, default) {
                            OverflowBehavior
                                = BoundIntegerOverflowBehavior.Checked,
                        };
                    break;

                case 0x10:
                case 0x20:
                case 0x21:
                case 0x33:
                    var registerOperand = checked((byte)instruction.Index);
                    var destinationRegister = registerOperand & 0x0F;
                    var rightRegister = registerOperand >> 4;
                    if (pendingBooleanXor is { } equalityXor)
                    {
                        if (destinationRegister != equalityXor.Destination
                            || rightRegister != equalityXor.Destination)
                        {
                            throw Invalid(
                                "The F+S Boolean equality has a mismatched NOT register.");
                        }

                        _ = registerConstants.Remove(destinationRegister);
                        registerExpressions[destinationRegister]
                            = new BoundBinary(TokenKind.EqualsEquals,
                                equalityXor.Left, equalityXor.Right,
                                PrimitiveType.Bool, default);
                        pendingBooleanXor = null;
                        break;
                    }

                    if (instruction.Opcode == 0x10)
                    {
                        if (!registerConstants.TryGetValue(rightRegister,
                                out var moveSource))
                        {
                            throw Invalid(
                                "The F+S array-index move has no constant source.");
                        }

                        registerConstants[destinationRegister] = moveSource;
                        if (registerExpressions.TryGetValue(rightRegister,
                                out var movedExpression))
                        {
                            registerExpressions[destinationRegister]
                                = movedExpression;
                        }
                        else
                        {
                            _ = registerExpressions.Remove(
                                destinationRegister);
                        }
                        break;
                    }

                    _ = registerExpressions.Remove(destinationRegister);
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
                            instruction.Index, instruction.Register,
                            placeSelectionsByRegister));
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
                            || localInitializedByHome[
                                initializationSelection.BaseHome])
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

                            var emptyArrayVariable
                                = localVariableByHome[emptyArrayHome];
                            if (emptyArrayVariable < 0)
                            {
                                throw Invalid(
                                    "The A2E empty Home has no local variable.");
                            }

                            localInitializedByHome[emptyArrayHome] = true;
                            currentStatements.Add(new BoundLocal(
                                emptyArrayVariable, emptyInitialization,
                                default));
                        }
                        else
                        {
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

                            AppendArrayInitializationElement(body,
                                initializationSelection.ArrayInstanceKey,
                                selectedElementIndex, emptyInitialization);
                        }
                    }
                    else
                    {
                        if (initializationSelection.TargetKind != 3
                            || initializationSelection.ElementCount == 0
                            || localInitializedByHome[
                                initializationSelection.BaseHome]
                            || !TryGetContextualConstant(
                                checked((byte)sourceRegister),
                                initializationSelection.ElementType,
                                body.Schema,
                                out var initializationValue))
                        {
                            throw Invalid(
                                "The F+S SemanticStore does not initialize its selected array element.");
                        }

                        var selectedElementIndex
                            = ReadArrayInitializationElementIndex(
                                initializationSelection, body,
                                registerConstants, localConstantByHome);
                        if (selectedElementIndex
                                >= initializationSelection.ElementCount)
                        {
                            throw Invalid(
                                "The F+S array element initializations are not complete and source-ordered.");
                        }

                        AppendArrayInitializationElement(body,
                            initializationSelection.ArrayInstanceKey,
                            selectedElementIndex,
                            new BoundConstant(initializationValue,
                                initializationSelection.ElementType, default));
                    }

                    _ = placeSelectionsByRegister.Remove(placeRegister);
                    break;

                case 0xE9:
                    if (instruction.Index != nativeAddressRecordIndex
                        || localVariableByHome[targetHome] < 0
                        || localInitializedByHome[targetHome]
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

                    var targetVariable = localVariableByHome[targetHome];
                    localInitializedByHome[targetHome] = true;
                    currentStatements.Add(new BoundLocal(targetVariable,
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
                            || !localInitializedByHome[sourceHome]
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
                            || !localInitializedByHome[
                                selectedPlace.BaseHome]
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
                            || (formalOrdinalByHome[
                                    selectedPlace.IndexHome] < 0
                                && !localInitializedByHome[
                                    selectedPlace.IndexHome])
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
                    callArgumentHomeOrder.Add(resultHome);
                    break;

                case 0xE0:
                    if (directUnitCallRecordIndexes.Contains(
                            instruction.Index))
                    {
                        if (sawCallSite)
                        {
                            throw Invalid(
                                "An A2EU unit initializer CallSite follows the indirect call.");
                        }

                        var directUnitCall
                            = ReadForeignInvocationUnitInitializerCall(
                                body, body.GetRecord(instruction.Index),
                                imports, directCallTargets,
                                callerParameterTypes, formalOrdinalByHome,
                                localVariableByHome,
                                localInitializedByHome, variables,
                                storageAddressesByHome,
                                consumedStorageHomes,
                                callArgumentHomeOrder,
                                out var directCallResultHome);
                        if (directCallResultHome >= 0)
                        {
                            if ((uint)directCallResultHome
                                    >= (uint)body.HomeCount
                                || body.HomeMap[directCallResultHome * 8] != 3
                                || localVariableByHome[
                                    directCallResultHome] < 0
                                || localInitializedByHome[
                                    directCallResultHome]
                                || storageAddressesByHome.ContainsKey(
                                    directCallResultHome))
                            {
                                throw Invalid(
                                    "The A2EU direct-call result Home is invalid.");
                            }

                            var resultTypeMapIndex
                                = BinaryPrimitives.ReadUInt16LittleEndian(
                                    body.HomeMap[((directCallResultHome * 8) + 2)..]);
                            var resultHomeType = ReadForeignInvocationType(
                                typeSpecs,
                                ReadForeignInvocationBodyTypeSpec(body,
                                    resultTypeMapIndex),
                                nativePointerTypeDefinitions,
                                signatureIndexByAnchorMethod,
                                structureTypeByTypeDef,
                                arrayTypeByTypeSpecRow:
                                    arrayTypeByTypeSpecRow);
                            if (resultHomeType != directUnitCall.Type)
                            {
                                throw Invalid(
                                    "The A2EU direct-call result Home has the wrong type.");
                            }

                            var resultVariable = localVariableByHome[
                                directCallResultHome];
                            localInitializedByHome[directCallResultHome] = true;
                            currentStatements.Add(new BoundLocal(resultVariable,
                                directUnitCall, default));
                        }
                        else
                        {
                            currentStatements.Add(new BoundExpressionStatement(
                                directUnitCall, default));
                        }
                        for (var register = 0; register < 14; register++)
                        {
                            _ = registerConstants.Remove(register);
                            _ = registerExpressions.Remove(register);
                        }
                        localConstantByHome.Clear();
                        break;
                    }

                    if (sawCallSite
                        || instruction.Index != callSiteRecordIndex
                        || localVariableByHome[targetHome] < 0
                        || !localInitializedByHome[targetHome])
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
                    var finalCallStorageHomes = new HashSet<int>();
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
                            _ = finalCallStorageHomes.Add(selectorIndex);
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
                            || !localInitializedByHome[selectorIndex]
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

                    for (var eventIndex = callArgumentHomeOrder.Count - 1;
                        eventIndex >= 0; eventIndex--)
                    {
                        if (finalCallStorageHomes.Contains(
                                callArgumentHomeOrder[eventIndex]))
                        {
                            callArgumentHomeOrder.RemoveAt(eventIndex);
                        }
                    }

                    currentStatements.Add(new BoundExpressionStatement(
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
                        || localVariableByHome[returnHome] < 0
                        || !localInitializedByHome[returnHome])
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
                    currentStatements.Add(new BoundReturn(new BoundRead(
                        returnVariable, callerResultType, default), default));
                    sawReturn = true;
                    break;
            }

            instructionOrdinal++;
        }

        if (pendingComparison is not null || pendingBooleanXor is not null)
        {
            throw Invalid(
                "The F+S comparison or Boolean XOR has no condition consumer.");
        }

        if (!sawCallSite || !sawReturn || conditionalFrames.Count != 0
            || placeSelectionsByRegister.Count != 0
            || arrayInitializationElementsByHome.Count != 0
            || callArgumentHomeOrder.Count != 0
            || storageAddressesByHome.Keys.Any(home =>
                !consumedStorageHomes.Contains(home)))
        {
            throw Invalid("The F+S caller Body is incomplete.");
        }

        var reconstructedBody = new BoundBlock([.. rootStatements], default);
        var initializedLocalVariables = new HashSet<int>();
        if (!BoundTreeTraversal.VisitStatements(reconstructedBody,
                statement =>
                {
                    if (statement is BoundLocal local)
                    {
                        _ = initializedLocalVariables.Add(local.Variable);
                    }
                }))
        {
            throw Unsupported(
                "The reconstructed F+S caller exceeds the bound-tree analysis limits.");
        }

        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            if (body.HomeMap[homeIndex * 8] != 3)
            {
                continue;
            }

            if (localVariableByHome[homeIndex] >= 0)
            {
                if (!initializedLocalVariables.Contains(
                        localVariableByHome[homeIndex]))
                {
                    throw Invalid(
                        "An F+S local Home has no initialization operation.");
                }
            }
            else if (!storageAddressesByHome.ContainsKey(homeIndex))
            {
                throw Invalid("An F+S local Home has no initialization operation.");
            }
        }

        return (new BoundBlock([
                new BoundUnsafe(reconstructedBody,
                    default),
            ], default),
            variables.ToImmutable());
    }

    private static List<byte[]> ReadForeignInvocationStorageBodyInstructions(
        TrRmRegisterBodyReader body)
    {
        var schema = body.Schema;
        var blockCount = body.BlockCount;
        var codeByBlock = new byte[blockCount][];
        var entryStateRecordIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetBlock(0)[8..]);
        var entryState = body.GetRecord(entryStateRecordIndex);
        if (entryState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(entryState[4..]) != 0)
        {
            throw Invalid("The F+S caller has unexpected entry registers.");
        }

        var terminalOpcodes = new byte[blockCount];
        var terminalOffsets = new int[blockCount];
        var terminalLengths = new int[blockCount];
        var successors = new int[blockCount][];
        var edgeRecordIndexes = new ushort[blockCount];
        var conditionRegisters = new byte[blockCount];
        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            if (BinaryPrimitives.ReadUInt16LittleEndian(block[8..])
                != entryStateRecordIndex)
            {
                throw Unsupported(
                    "The F+S conditional subset requires one shared entry state.");
            }

            var code = body.GetBlockCode(blockIndex);
            codeByBlock[blockIndex] = code.ToArray();
            var position = 0;
            var terminalOffset = -1;
            var terminalLength = 0;
            while (position < code.Length)
            {
                terminalOffset = position;
                terminalLength = TrRmRegisterBodyReader.InstructionLength(
                    code[position], schema);
                position += terminalLength;
            }

            if (terminalOffset < 0 || position != code.Length)
            {
                throw Invalid("The F+S Body contains an empty or truncated Block.");
            }

            var opcode = code[terminalOffset];
            terminalOpcodes[blockIndex] = opcode;
            terminalOffsets[blockIndex] = terminalOffset;
            terminalLengths[blockIndex] = terminalLength;
            if (opcode == 0xE3)
            {
                var target = BinaryPrimitives.ReadUInt16LittleEndian(
                    code[(terminalOffset + 2)..]);
                if (code[terminalOffset + 1] != 0
                    || target <= blockIndex || target >= blockCount)
                {
                    throw Invalid("The F+S unconditional branch is invalid.");
                }

                successors[blockIndex] = [target];
            }
            else if (opcode == 0xE4)
            {
                if (!schema
                        .UsesForeignInvocationStorageFixedArrayUnitInitialization())
                {
                    throw Unsupported(
                        "Conditional Body control flow requires the exact A2EU schema.");
                }

                var control = code[terminalOffset + 1];
                var edgeRecordIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(terminalOffset + 2)..]);
                var edge = body.GetRecord(edgeRecordIndex);
                if ((control & 0xF0) != 0 || (control & 0x0F) >= 14
                    || edge[0] != 0x06
                    || edge.Length != 8)
                {
                    throw Invalid("The A2EU conditional branch record is invalid.");
                }

                var trueBlock = BinaryPrimitives.ReadUInt16LittleEndian(
                    edge[4..]);
                var falseBlock = BinaryPrimitives.ReadUInt16LittleEndian(
                    edge[6..]);
                if (trueBlock == falseBlock || trueBlock <= blockIndex
                    || falseBlock <= blockIndex
                || trueBlock >= blockCount
                || falseBlock >= blockCount)
                {
                    throw Invalid("The A2EU ConditionalEdge targets are invalid.");
                }

                edgeRecordIndexes[blockIndex] = edgeRecordIndex;
                conditionRegisters[blockIndex] = (byte)(control & 0x0F);
                successors[blockIndex] = [trueBlock, falseBlock];
            }
            else if (opcode == 0xB8)
            {
                successors[blockIndex] = [];
            }
            else
            {
                throw Unsupported(
                    "Each F+S Block must end in a supported branch or ReturnHome.");
            }
        }

        var remainingWork = 1_000_000;
        HashSet<int> GetReachableBlocks(int startBlock)
        {
            var reachable = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(startBlock);
            while (pending.TryPop(out var blockIndex))
            {
                if (--remainingWork < 0)
                {
                    throw Unsupported(
                        "The A2EU conditional CFG exceeds its bounded analysis work.");
                }

                if (!reachable.Add(blockIndex))
                {
                    continue;
                }

                foreach (var successor in successors[blockIndex])
                {
                    pending.Push(successor);
                }
            }

            return reachable;
        }

        int FindConditionalJoin(int blockIndex)
        {
            var trueTarget = successors[blockIndex][0];
            var falseTarget = successors[blockIndex][1];
            var trueReachable = GetReachableBlocks(trueTarget);
            var falseReachable = GetReachableBlocks(falseTarget);
            var join = int.MaxValue;
            foreach (var candidate in trueReachable)
            {
                if (candidate > blockIndex
                    && candidate < join
                    && falseReachable.Contains(candidate))
                {
                    join = candidate;
                }
            }

            if (join == int.MaxValue)
            {
                throw Invalid(
                    "The A2EU conditional branches have no normal join Block.");
            }

            return join;
        }

        var flattened = new List<byte[]>();
        var visitedBlocks = new HashSet<int>();
        void AppendBlockBody(int blockIndex)
        {
            var code = codeByBlock[blockIndex].AsSpan();
            var position = 0;
            var end = terminalOffsets[blockIndex];
            while (position < end)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], schema);
                flattened.Add(code.Slice(position, instructionLength)
                    .ToArray());
                position += instructionLength;
            }
        }

        void AppendConditionalRegion(int startBlock, int stopBlock,
            int depth)
        {
            if (depth > 128)
            {
                throw Unsupported(
                    "The A2EU conditional nesting exceeds the bounded Body reader.");
            }

            var blockIndex = startBlock;
            while (blockIndex != stopBlock)
            {
                if (blockIndex >= stopBlock
                    || !visitedBlocks.Add(blockIndex))
                {
                    throw Invalid(
                        "The A2EU conditional regions overlap or are not forward structured.");
                }

                AppendBlockBody(blockIndex);
                var opcode = terminalOpcodes[blockIndex];
                if (opcode == 0xE3)
                {
                    blockIndex = successors[blockIndex][0];
                    continue;
                }

                if (opcode != 0xE4)
                {
                    throw Invalid(
                        "An A2EU conditional arm exits before its join Block.");
                }

                var joinBlock = FindConditionalJoin(blockIndex);
                if (joinBlock >= stopBlock)
                {
                    throw Invalid(
                        "A nested A2EU conditional escapes its containing region.");
                }

                flattened.Add([
                    0xFC,
                    conditionRegisters[blockIndex],
                    (byte)edgeRecordIndexes[blockIndex],
                    (byte)(edgeRecordIndexes[blockIndex] >> 8),
                ]);
                AppendConditionalRegion(successors[blockIndex][0],
                    joinBlock, depth + 1);
                flattened.Add([0xFD, 0, 0, 0]);
                AppendConditionalRegion(successors[blockIndex][1],
                    joinBlock, depth + 1);
                flattened.Add([0xFE, 0, 0, 0]);
                blockIndex = joinBlock;
            }
        }

        var currentBlock = 0;
        while (true)
        {
            if (!visitedBlocks.Add(currentBlock))
            {
                throw Invalid("The A2EU caller CFG contains a cycle.");
            }

            AppendBlockBody(currentBlock);
            var opcode = terminalOpcodes[currentBlock];
            if (opcode == 0xE3)
            {
                currentBlock = successors[currentBlock][0];
                continue;
            }

            if (opcode == 0xE4)
            {
                var joinBlock = FindConditionalJoin(currentBlock);
                flattened.Add([
                    0xFC,
                    conditionRegisters[currentBlock],
                    (byte)edgeRecordIndexes[currentBlock],
                    (byte)(edgeRecordIndexes[currentBlock] >> 8),
                ]);
                AppendConditionalRegion(successors[currentBlock][0],
                    joinBlock, 1);
                flattened.Add([0xFD, 0, 0, 0]);
                AppendConditionalRegion(successors[currentBlock][1],
                    joinBlock, 1);
                flattened.Add([0xFE, 0, 0, 0]);
                currentBlock = joinBlock;
                continue;
            }

            if (opcode != 0xB8)
            {
                throw Invalid("The A2EU caller has no terminal ReturnHome.");
            }

            var terminalCode = codeByBlock[currentBlock].AsSpan();
            flattened.Add(terminalCode.Slice(
                terminalOffsets[currentBlock],
                terminalLengths[currentBlock]).ToArray());
            break;
        }

        if (visitedBlocks.Count != blockCount)
        {
            throw Invalid("The A2EU caller contains unreachable Blocks.");
        }

        return flattened;
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
            int ownerModule, int recordIndex, int destinationRegister,
            Dictionary<int, NativeArrayPlaceSelectionFacts>
                placeSelectionsByRegister)
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
                emptyArrayType, emptyElementType, 0,
                GetArrayInitializationInstanceKey(homeIndex), 0);
        }

        if (targetKind != 3 || accessKind is < 1 or > 3
            || baseKind is not (1 or 2)
            || (baseKind == 2
                ? baseHome >= body.HomeCount : baseHome > 13)
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

        int rootHome;
        int arrayInstanceKey;
        if (baseKind == 2)
        {
            var baseHomeMap = body.HomeMap.Slice(baseHome * 8, 8);
            var arrayHomeType = ReadForeignInvocationType(typeSpecs,
                ReadForeignInvocationBodyTypeSpec(body,
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        baseHomeMap[2..])),
                nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                structureTypeByTypeDef,
                arrayTypeByTypeSpecRow: arrayTypeByTypeSpecRow);
            if (baseHomeMap[0] != 3 || arrayHomeType != arrayType
                || !NativeFixedArrayTypes.IsType(arrayType))
            {
                throw Invalid(
                    "The F+S PlaceSelection base Home is invalid.");
            }

            rootHome = baseHome;
            arrayInstanceKey
                = GetArrayInitializationInstanceKey(rootHome);
        }
        else
        {
            if (!placeSelectionsByRegister.TryGetValue(baseHome,
                    out var basePlace)
                || !NativeFixedArrayTypes.IsType(arrayType)
                || basePlace.ElementType != arrayType
                || basePlace.BaseHome < 0
                || basePlace.BaseHome >= body.HomeCount)
            {
                throw Invalid(
                    "The F+S nested PlaceSelection has no selected array base.");
            }

            rootHome = basePlace.BaseHome;
            arrayInstanceKey = basePlace.RecordIndex;
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
            destinationRegister, targetKind, accessKind, rootHome, indexKind,
            indexHome, indexRegister, fixedArrayLayout.ArrayTypeSpecRow,
            checked((int)layoutRow), arrayType, elementType, elementCount,
            arrayInstanceKey, 0);
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

    private static int GetArrayInitializationInstanceKey(int homeIndex)
    {
        if (homeIndex < 0)
        {
            throw Invalid("An F+S array Home index is invalid.");
        }

        return checked(-homeIndex - 1);
    }

    private static void AddInitializedArrayLocal(PrimitiveType arrayType,
        int home, ulong elementCount, List<BoundExpression> elements,
        ImmutableArray<BoundNativeFixedArrayType> nativeFixedArrayTypes,
        int[] localVariableByHome, bool[] localInitializedByHome,
        List<BoundStatement> statements)
    {
        if (!NativeFixedArrayTypes.IsType(arrayType)
            || (uint)home >= (uint)localVariableByHome.Length
            || localVariableByHome[home] < 0
            || localInitializedByHome[home])
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

        var variable = localVariableByHome[home];
        localInitializedByHome[home] = true;
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
        PrimitiveType type, TrRmArtifactSchema schema, out long value)
    {
        value = 0;
        if (type == PrimitiveType.Void)
        {
            return schema
                    .UsesForeignInvocationStorageFixedArrayUnitInitialization()
                && sourceRegister == 14;
        }

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

    private static BoundCall ReadForeignInvocationUnitInitializerCall(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> record,
        TrRmImportTableReader imports,
        IReadOnlyDictionary<int, NativeAddressDirectCallTarget> callTargets,
        ImmutableArray<PrimitiveType> callerParameterTypes,
        int[] formalOrdinalByHome, int[] localVariableByHome,
        bool[] localInitializedByHome,
        ImmutableArray<BoundVariable>.Builder variables,
        Dictionary<int, NativeStorageAddressFacts> storageAddressesByHome,
        HashSet<int> consumedStorageHomes,
        List<int> callArgumentHomeOrder, out int resultHome)
    {
        resultHome = -1;
        var inputCount = BinaryPrimitives.ReadUInt32LittleEndian(
            record[12..]);
        var preparationCount = BinaryPrimitives.ReadUInt32LittleEndian(
            record[16..]);
        var targetMethodRow = ReadForeignInvocationBodyMethodDef(body,
            imports, BinaryPrimitives.ReadUInt16LittleEndian(record[8..]));
        var callResultKind = record[5];
        var callResultHome = BinaryPrimitives.ReadUInt16LittleEndian(
            record[10..]);
        if (!body.Schema
                .UsesForeignInvocationStorageFixedArrayUnitInitialization()
            || !callTargets.TryGetValue(targetMethodRow,
                out var callTarget)
            || record[0] != 0x02 || record[4] != 1
            || inputCount != (uint)callTarget.ParameterTypes.Length
            || preparationCount != 0
            || (long)record.Length != 20L + (inputCount * 4L))
        {
            throw Invalid(
                "The A2EU direct CallSite does not match a retained NativeImport.");
        }

        if (callTarget.ReturnType == PrimitiveType.Void)
        {
            if (callResultKind != 0 || callResultHome != 0)
            {
                throw Invalid(
                    "The A2EU Unit CallSite has an invalid result selector.");
            }
        }
        else
        {
            if (!DirectNativeImportContract.IsScalar(callTarget.ReturnType)
                || callResultKind != 2
                || callResultHome >= body.HomeCount)
            {
                throw Invalid(
                    "The A2EU direct CallSite has an unsupported result Home.");
            }

            resultHome = callResultHome;
        }

        var targetParameterTypes = callTarget.ParameterTypes;
        var argumentByParameter = new BoundExpression?[
            targetParameterTypes.Length];
        var specialParameterByHome = new Dictionary<int, int>();
        for (var ordinal = 0; ordinal < targetParameterTypes.Length; ordinal++)
        {
            var inputOffset = 20 + (ordinal * 4);
            var selectorKind = record[inputOffset];
            var sourceUse = record[inputOffset + 1];
            var selectorIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                record[(inputOffset + 2)..]);
            var parameterType = targetParameterTypes[ordinal];
            var nativeParameter = callTarget.NativeParameters
                .FirstOrDefault(parameter => parameter.ParameterIndex == ordinal);
            if (selectorKind == 0)
            {
                if (sourceUse != 0 || selectorIndex != 0
                    || parameterType != PrimitiveType.Void
                    || nativeParameter is not null)
                {
                    throw Invalid(
                        "The A2EU absent selector is not an exact Unit argument.");
                }

                argumentByParameter[ordinal] = new BoundConstant(
                    0, PrimitiveType.Void, default);
                continue;
            }

            if (selectorKind == 1)
            {
                if (sourceUse != 1 || selectorIndex is not (14 or 15)
                    || nativeParameter is not null
                    || !TryGetContextualConstant(
                        checked((byte)selectorIndex), parameterType,
                        body.Schema, out var value))
                {
                    throw Invalid(
                        "The A2EU contextual argument is not admitted for its formal.");
                }

                argumentByParameter[ordinal]
                    = new BoundConstant(value, parameterType, default);
                continue;
            }

            if (selectorKind != 2 || sourceUse != 1
                || selectorIndex >= body.HomeCount)
            {
                throw Invalid(
                    "The A2EU CallSite input selector is invalid.");
            }

            if (storageAddressesByHome.TryGetValue(selectorIndex,
                    out var addressFacts))
            {
                if (nativeParameter is null
                    || addressFacts.PointerType != parameterType
                    || addressFacts.RequiredElementCount
                        != (nativeParameter.Extent == 0
                            ? 1u : nativeParameter.Extent)
                    || nativeParameter.Extent > addressFacts.ElementCount
                    || (!NativeFixedArrayTypes.IsType(
                            addressFacts.CarrierType)
                        && nativeParameter.Extent > 1)
                    || ((nativeParameter.Access
                            is NativeParameterAccess.Write
                                or NativeParameterAccess.ReadWrite)
                        && !addressFacts.PointerIsWritable)
                    || addressFacts.ElementStride == 0
                    || !consumedStorageHomes.Add(selectorIndex))
                {
                    throw Invalid(
                        "The A2EU storage address does not satisfy its NativeParameter.");
                }

                var byteExtent = checked((ulong)
                    addressFacts.RequiredElementCount
                    * addressFacts.ElementStride);
                argumentByParameter[ordinal]
                    = new BoundNativeStorageAddress(
                        addressFacts.SourceVariable, addressFacts.Index,
                        addressFacts.CarrierType, addressFacts.ElementType,
                        PrimitiveType.NativeStorageAddress,
                        addressFacts.ElementCount, addressFacts.ElementStride,
                        byteExtent,
                        nativeParameter.Access == NativeParameterAccess.Read,
                        default);
                specialParameterByHome.Add(selectorIndex, ordinal);
                continue;
            }

            if (nativeParameter is not null)
            {
                throw Invalid(
                    "The A2EU NativeParameter input has no storage-address result.");
            }

            var formalOrdinal = formalOrdinalByHome[selectorIndex];
            var inputVariable = formalOrdinal >= 0
                ? formalOrdinal
                : localVariableByHome[selectorIndex];
            if (inputVariable < 0
                || (formalOrdinal < 0
                    && !localInitializedByHome[selectorIndex]))
            {
                throw Invalid(
                    "The A2EU CallSite input selects no initialized Home.");
            }

            var inputType = variables[inputVariable].Type;
            if (inputType != parameterType
                || (formalOrdinal >= 0
                    && inputType != callerParameterTypes[formalOrdinal]))
            {
                throw Invalid(
                    "The A2EU CallSite input type differs from its formal.");
            }

            argumentByParameter[ordinal] = new BoundRead(inputVariable,
                inputType, default);
        }

        var arguments = ImmutableArray.CreateBuilder<BoundExpression>(
            targetParameterTypes.Length);
        var argumentParameterIndices = ImmutableArray.CreateBuilder<int>(
            targetParameterTypes.Length);
        var addedParameters = new bool[targetParameterTypes.Length];
        var consumedEventHomes = new HashSet<int>();
        foreach (var argumentHome in callArgumentHomeOrder)
        {
            if (specialParameterByHome.TryGetValue(argumentHome,
                    out var parameterIndex))
            {
                arguments.Add(argumentByParameter[parameterIndex]!);
                argumentParameterIndices.Add(parameterIndex);
                addedParameters[parameterIndex] = true;
                if (!consumedEventHomes.Add(argumentHome))
                {
                    throw Invalid(
                        "An A2EU direct-call argument Home is reused.");
                }
            }
        }

        if (consumedEventHomes.Count != specialParameterByHome.Count)
        {
            throw Invalid(
                "The A2EU direct-call arguments are not in capture order.");
        }

        for (var parameterIndex = 0;
            parameterIndex < targetParameterTypes.Length;
            parameterIndex++)
        {
            if (addedParameters[parameterIndex])
            {
                continue;
            }

            arguments.Add(argumentByParameter[parameterIndex]!);
            argumentParameterIndices.Add(parameterIndex);
        }

        for (var eventIndex = callArgumentHomeOrder.Count - 1;
            eventIndex >= 0; eventIndex--)
        {
            if (consumedEventHomes.Contains(
                    callArgumentHomeOrder[eventIndex]))
            {
                callArgumentHomeOrder.RemoveAt(eventIndex);
            }
        }

        var isIdentityMapping = true;
        for (var argumentIndex = 0;
            argumentIndex < argumentParameterIndices.Count;
            argumentIndex++)
        {
            isIdentityMapping &= argumentParameterIndices[argumentIndex]
                == argumentIndex;
        }

        return new BoundCall(callTarget.FunctionIndex, arguments.MoveToImmutable(),
            callTarget.ReturnType, default)
        {
            ArgumentParameterIndices = isIdentityMapping
                ? default : argumentParameterIndices.MoveToImmutable(),
            IsUnsafeAuthorized = true,
        };
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
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft)
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
        var kind = (byte)(token >> 24);
        if (kind is 0xF2 or 0xF3)
        {
            return token;
        }

        if (kind != 0x11 || (token & 0x00FF_FFFF) == 0)
        {
            throw Unsupported("The bounded loader requires TypeSpec Body map tokens.");
        }

        return token & 0x00FF_FFFF;
    }

    private static int ReadForeignInvocationNativeAddressTargetMethod(
        TrRmRegisterBodyReader body, TrRmImportTableReader imports)
    {
        var targetMethodRow = 0;
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], body.Schema);
                var instruction = code.Slice(position, instructionLength);
                if (instruction[0] == 0xE9)
                {
                    if (instruction[1] != 0 || targetMethodRow != 0)
                    {
                        throw Invalid(
                            "The NativeAddress Body has an invalid target formation.");
                    }

                    var recordIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    var record = body.GetRecord(recordIndex);
                    if (record[0] != 0x0D)
                    {
                        throw Invalid(
                            "The NativeAddress formation has an invalid record.");
                    }

                    targetMethodRow = ReadForeignInvocationBodyMethodDef(body,
                        imports,
                        BinaryPrimitives.ReadUInt16LittleEndian(record[8..]));
                }

                position += instructionLength;
            }
        }

        return targetMethodRow != 0 ? targetMethodRow
            : throw Invalid("The caller Body has no NativeAddress target.");
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
