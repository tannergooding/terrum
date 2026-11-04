// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static byte[] EmitLocalAggregateLayoutRegisterBodies(
        BoundProgram program, string moduleIdentity)
    {
        if (program.EntryPoint < -1
            || (program.EntryPoint >= 0
                && ((uint)program.EntryPoint
                        >= (uint)program.Functions.Length
                    || program.Functions[program.EntryPoint].ReturnType
                        != PrimitiveType.Int32
                    || program.Functions[program.EntryPoint]
                        .ParameterCount != 0))
            || program.RuntimePolicy != BoundRuntimePolicy.FailFast
            || !program.Classes.IsEmpty || !program.Buffers.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || !program.NativePointerTypes.IsEmpty
            || !program.NativeUtf16ZLiterals.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty
            || program.RequiresSourceGrant
            || program.InvocationProfileId is not null
            || program.IsInvocationSourceBound
            || program.Structures.Any(static structure =>
                structure.IsUnique
                || structure.CleanupMethod is not null))
        {
            throw Unsupported(
                "L1 executable Body emission requires local rootless Copy layouts and a fail-fast entry point.");
        }

        var methodImportEdgesByFunction
            = new Dictionary<int, BoundStandardMethodProviderEdge>();
        BoundStandardMethodProviderModule? methodProviderRootModule = null;
        if (program.StandardMethodProviderClosure is { } providerClosure)
        {
            var rootModules = providerClosure.Modules.Where(module =>
                    module.ArtifactIndex
                        == providerClosure.RootArtifactIndex
                    && module.ModuleRow == providerClosure.RootModuleRow)
                .ToArray();
            if (providerClosure.RootArtifactIndex != 0
                || !StringComparer.Ordinal.Equals(
                    providerClosure.RootModuleIdentity, moduleIdentity)
                || providerClosure.Artifacts.IsDefaultOrEmpty
                || providerClosure.Artifacts.Any(static artifact =>
                    !StringComparer.Ordinal.Equals(artifact.SchemaIdentity,
                        StandardLocalAggregateLayoutSchemaIdentity))
                || rootModules.Length != 1
                || !StringComparer.Ordinal.Equals(rootModules[0].Identity,
                    moduleIdentity))
            {
                throw Unsupported(
                    "L1 MethodDef Import re-emission requires its exact retained root and provider Module selections.");
            }
            var rootModule = rootModules[0];
            methodProviderRootModule = rootModule;

            foreach (var edge in providerClosure.MethodEdges)
            {
                if (edge.ConsumerArtifactIndex
                        != providerClosure.RootArtifactIndex
                    || edge.ConsumerModuleRow
                        != providerClosure.RootModuleRow)
                {
                    continue;
                }

                var methodRowIsValid
                    = (uint)edge.Expected.MethodDefRow
                        < (uint)rootModule.FunctionIndexByMethodRow.Length;
                var methodFunctionIndex = methodRowIsValid
                    ? rootModule.FunctionIndexByMethodRow[
                        edge.Expected.MethodDefRow] : -1;
                var importRowIsValid = edge.ImportRow > 0
                    && (uint)edge.ImportRow
                        < (uint)rootModule.FunctionIndexByImportRow.Length;
                var importFunctionIndex = importRowIsValid
                    ? rootModule.FunctionIndexByImportRow[edge.ImportRow] : -1;
                var methodFunctionIsValid
                    = (uint)methodFunctionIndex
                        < (uint)rootModule.Program.Functions.Length;
                var importFunctionIsValid
                    = (uint)importFunctionIndex
                        < (uint)rootModule.Program.Functions.Length;
                var methodFunction = methodFunctionIsValid
                    ? rootModule.Program.Functions[methodFunctionIndex]
                    : null;
                var importFunction = importFunctionIsValid
                    ? rootModule.Program.Functions[importFunctionIndex]
                    : null;
                var import = importFunction?.ModuleImport;
                if (edge.Expected.ArtifactIndex
                        != providerClosure.RootArtifactIndex
                    || edge.Expected.ModuleRow
                        != providerClosure.RootModuleRow
                    || edge.Expected.MethodDefRow <= 0
                    || edge.Expected.DefinitionId
                        != (0x1600_0000u
                            | checked((uint)edge.Expected.MethodDefRow))
                    || !methodRowIsValid || !importRowIsValid
                    || importFunctionIndex != edge.Expected.FunctionIndex
                    || !methodFunctionIsValid || !importFunctionIsValid
                    || (uint)edge.Expected.FunctionIndex
                        >= (uint)program.Functions.Length
                    || methodFunction is not { IsExternal: true }
                    || importFunction is not {
                        IsExternal: true, IsModuleReference: true,
                    }
                    || import is null
                    || !StringComparer.Ordinal.Equals(
                        import.ProducerIdentity, edge.ProducerIdentity)
                    || !StringComparer.Ordinal.Equals(import.ExportKey,
                        edge.ExportKey)
                    || import.TargetRequirement != edge.TargetRequirement
                    || import.RuntimePolicy != edge.RuntimePolicy
                    || methodFunction.ReturnType
                        != importFunction.ReturnType
                    || methodFunction.ParameterCount
                        != importFunction.ParameterCount
                    || !methodFunction.Variables.SequenceEqual(
                        importFunction.Variables)
                    || !StringComparer.Ordinal.Equals(
                        edge.ConsumerModuleIdentity, moduleIdentity)
                    || !methodImportEdgesByFunction.TryAdd(
                        edge.Expected.FunctionIndex, edge))
                {
                    throw Invalid(
                        "An L1 MethodDef Import edge has inconsistent root ownership or function correspondence.");
                }
            }
        }

        var methodDefRowByFunction = new int[program.Functions.Length];
        if (methodProviderRootModule is null)
        {
            for (var functionIndex = 0;
                functionIndex < methodDefRowByFunction.Length;
                functionIndex++)
            {
                methodDefRowByFunction[functionIndex] = functionIndex + 1;
            }
        }
        else
        {
            var nextMethodDefRow = 1;
            for (var methodRow = methodProviderRootModule.MethodDefStart;
                methodRow < methodProviderRootModule.MethodDefEnd;
                methodRow++)
            {
                if ((uint)methodRow
                        >= (uint)methodProviderRootModule
                            .FunctionIndexByMethodRow.Length)
                {
                    throw Invalid(
                        "The L1 root Module has an invalid retained MethodDef range.");
                }

                var functionIndex = methodProviderRootModule
                    .FunctionIndexByMethodRow[methodRow];
                if ((uint)functionIndex >= (uint)program.Functions.Length
                    || methodDefRowByFunction[functionIndex] != 0)
                {
                    throw Invalid(
                        "The L1 root Module has an invalid MethodDef-to-function mapping.");
                }

                methodDefRowByFunction[functionIndex]
                    = nextMethodDefRow++;
            }
        }
        var methodFunctionIndexByRow
            = new int[methodDefRowByFunction.Length + 1];
        Array.Fill(methodFunctionIndexByRow, -1);
        for (var functionIndex = 0;
            functionIndex < methodDefRowByFunction.Length;
            functionIndex++)
        {
            var methodRow = methodDefRowByFunction[functionIndex];
            if (methodRow != 0)
            {
                methodFunctionIndexByRow[methodRow] = functionIndex;
            }
        }

        var importedMethodDefinitionFunctions
            = new bool[program.Functions.Length];
        var methodImportEdgesByDefinitionFunction
            = new Dictionary<int, BoundStandardMethodProviderEdge>();
        foreach (var edge in methodImportEdgesByFunction.Values)
        {
            var methodFunctionIndex
                = methodProviderRootModule!.FunctionIndexByMethodRow[
                    edge.Expected.MethodDefRow];
            importedMethodDefinitionFunctions[methodFunctionIndex] = true;
            if (!methodImportEdgesByDefinitionFunction.TryAdd(
                    methodFunctionIndex, edge))
            {
                throw Invalid(
                    "The L1 root has multiple Import edges for one expected MethodDef.");
            }
        }

        var importedMethodFunctions = new bool[program.Functions.Length];
        for (var functionIndex = 0;
            functionIndex < program.Functions.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            var hasImportIdentity = function.ModuleImport is not null
                || function.IsModuleReference || function.IsExternal;
            if (hasImportIdentity)
            {
                if (importedMethodDefinitionFunctions[functionIndex]
                    && !methodImportEdgesByFunction.ContainsKey(
                        functionIndex))
                {
                    var definitionImport = function.ModuleImport;
                    var hasMatchingEdge
                        = methodImportEdgesByDefinitionFunction.TryGetValue(
                            functionIndex, out var definitionEdge)
                            && definitionImport is not null
                            && StringComparer.Ordinal.Equals(
                                definitionImport.ProducerIdentity,
                                definitionEdge.ProducerIdentity)
                            && StringComparer.Ordinal.Equals(
                                definitionImport.ExportKey,
                                definitionEdge.ExportKey)
                            && definitionImport.TargetRequirement
                                == definitionEdge.TargetRequirement
                            && definitionImport.RuntimePolicy
                                == definitionEdge.RuntimePolicy;
                    if (!function.IsExternal || !function.IsModuleReference
                        || function.IsPrivate || function.IsInternal
                        || function.DeclaringStruct is not null
                        || !function.Body.Statements.IsEmpty
                        || function.DirectNativeImport is not null
                        || function.NativeFunctionPointerType is not null
                        || function.NativeCallableEntryName is not null
                        || function.NativeVTableMethod is not null
                        || function.IsConstructor
                        || function.IsClassConstructor
                        || function.IsCleanup || function.IsOperator
                        || function.Intrinsic != BoundIntrinsic.None
                        || function.PropertyName is not null
                        || function.IndexedBody is not null
                        || function.RequiresUnsafeCaller
                        || function.DoesNotReturn || function.NoFailureProof
                        || !hasMatchingEdge)
                    {
                        throw Unsupported(
                            "L1 bodyless MethodDef Imports require an exact retained provider edge to a public expected MethodDef.");
                    }

                    importedMethodFunctions[functionIndex] = true;
                    continue;
                }

                if (!function.IsExternal || !function.IsModuleReference
                    || function.ModuleImport is not { } moduleImport
                    || !methodImportEdgesByFunction.TryGetValue(
                        functionIndex, out var edge)
                    || methodProviderRootModule is null
                    || (uint)functionIndex
                        >= (uint)methodProviderRootModule.Program.Functions.Length
                    || !StringComparer.Ordinal.Equals(function.Name,
                        methodProviderRootModule.Program.Functions[
                            functionIndex].Name)
                    || function.ReturnType
                        != methodProviderRootModule.Program.Functions[
                            functionIndex].ReturnType
                    || function.ParameterCount
                        != methodProviderRootModule.Program.Functions[
                            functionIndex].ParameterCount
                    || !function.Variables.SequenceEqual(
                        methodProviderRootModule.Program.Functions[
                            functionIndex].Variables)
                    || function.ModuleImport
                        != methodProviderRootModule.Program.Functions[
                            functionIndex].ModuleImport
                    || function.DirectNativeImport is not null
                    || function.NativeFunctionPointerType is not null
                    || function.NativeCallableEntryName is not null
                    || function.NativeVTableMethod is not null
                    || function.IsConstructor
                    || function.IsClassConstructor
                    || function.IsPrivate || function.IsInternal
                    || function.DeclaringStruct is not null
                    || function.Body.Statements.Length != 0
                    || function.IsCleanup || function.IsOperator
                    || function.Intrinsic != BoundIntrinsic.None
                    || function.PropertyName is not null
                    || function.IndexedBody is not null
                    || !function.CapturedParameters.IsEmpty
                    || !function.ResultDependencyParameters.IsEmpty
                    || function.ResultDependencyIncludesReceiver
                    || function.ReturnsForwardedBacking
                    || function.NativeOutputRole is not null
                    || !function.NativeOutputContracts.IsEmpty
                    || function.NativeCallableConvention is not null
                    || function.CopyOriginSummary is not null
                    || !function.RawUnionReturnCoverage.IsEmpty
                    || !function.DependencyContracts.IsEmpty
                    || function.RequiresUnsafeCaller
                    || function.DoesNotReturn
                    || function.NoFailureProof
                    || function.Variables.Any(static variable =>
                        variable.IsOut || variable.IsInit
                        || variable.IsImplicitRef
                        || variable.IsImplicitReferenceTemporary)
                    || !StringComparer.Ordinal.Equals(
                        moduleImport.ProducerIdentity,
                        edge.ProducerIdentity)
                    || !StringComparer.Ordinal.Equals(moduleImport.ExportKey,
                        edge.ExportKey)
                    || moduleImport.TargetRequirement
                        != edge.TargetRequirement
                    || moduleImport.RuntimePolicy != edge.RuntimePolicy)
                {
                    throw Unsupported(
                        "L1 bodyless MethodDef Imports require a public expected MethodDef, an exact retained provider edge, and no native/effect/receiver contract.");
                }

                importedMethodFunctions[functionIndex] = true;
                continue;
            }

            if (methodImportEdgesByFunction.ContainsKey(functionIndex)
                || function.DirectNativeImport is not null
                || function.NativeFunctionPointerType is not null
                || function.NativeCallableEntryName is not null
                || function.NativeVTableMethod is not null
                || function.RequiresUnsafeCaller || function.DoesNotReturn
                || function.NoFailureProof || function.IsClassConstructor
                || function.IsCleanup || function.IsOperator
                || function.Intrinsic != BoundIntrinsic.None
                || function.DeclaringStruct is not null
                || function.PropertyName is not null
                || function.IndexedBody is not null
                || !function.CapturedParameters.IsEmpty
                || !function.ResultDependencyParameters.IsEmpty
                || function.ResultDependencyIncludesReceiver
                || !function.DependencyContracts.IsEmpty
                || function.Variables.Any(static variable =>
                    variable.IsOut || variable.IsInit || variable.IsImplicitRef
                    || variable.IsImplicitReferenceTemporary))
            {
                throw Unsupported(
                    "L1 executable Body emission supports ordinary local methods and the selected bodyless MethodDef Import subset.");
            }
        }
        if ((program.EntryPoint >= 0
                && (methodDefRowByFunction[program.EntryPoint] == 0
                    || program.Functions[program.EntryPoint].IsExternal))
            || (program.EntryPoint < 0
                && !Enumerable.Range(0, program.Functions.Length).Any(
                    functionIndex => methodDefRowByFunction[
                            functionIndex] != 0
                        && !program.Functions[functionIndex].IsExternal)))
        {
            throw Unsupported(
                "L1 executable entries and no-entry libraries require at least one body-bearing root MethodDef.");
        }

        var hasUnsupportedSignatureType = false;
        foreach (var function in program.Functions)
        {
            hasUnsupportedSignatureType |=
                function.ReturnType != PrimitiveType.Void
                && !IsLocalAggregateLayoutValueType(program,
                    function.ReturnType);
            for (var variableIndex = 0;
                variableIndex < function.Variables.Length; variableIndex++)
            {
                var type = function.Variables[variableIndex].Type;
                if (variableIndex < function.ParameterCount
                    && type == PrimitiveType.Void)
                {
                    continue;
                }

                hasUnsupportedSignatureType |=
                    !IsLocalAggregateLayoutValueType(program, type);
            }
        }
        if (hasUnsupportedSignatureType)
        {
            throw Unsupported(
                "L1 executable Body signatures require scalar, local nominal, or fixed-array value types.");
        }
        if (program.Functions.Any(static function =>
                NativeFixedArrayTypes.IsType(function.ReturnType)
                || function.Variables.Take(function.ParameterCount)
                    .Any(static parameter =>
                        NativeFixedArrayTypes.IsType(parameter.Type))))
        {
            throw Unsupported(
                "L1 local fixed-array Homes are supported, but whole-array MethodSig parameters and results remain outside the selected ABI gate.");
        }

        ValidateA2SourceTypeDeclarationMap(program);
        _ = FixedLayoutStorage.Validate(program.Structures,
            program.NativeFixedArrayTypes);

        var declarationProgram = program with {
            Functions = [],
            EntryPoint = -1,
        };
        var declarationArtifact
            = EmitForeignInvocationStorageFixedArrayCopyDeclarations(
                declarationProgram, moduleIdentity,
                TrRmArtifactSchema.LocalAggregateLayoutDraft);
        var declarationIndex
            = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(
                declarationArtifact);
        var declarationTables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    declarationIndex);
        if (declarationTables.Contracts.Count != 0
            || declarationTables.TypeDefs.Count != program.Structures.Length)
        {
            throw Unsupported(
                "L1 executable Body production requires the selected TypeDef contract closure.");
        }
        var typeImports = ReadImportRows(declarationIndex);
        if (typeImports.Any(static import =>
                import.ExpectedLocalDefId >> 24 != 0x13))
        {
            throw Unsupported(
                "The L1 type declaration artifact contains a non-TypeDef Import.");
        }

        var stringsRows = new List<(string Text, int Encoding)>(
            declarationTables.Strings.Count + program.Functions.Length);
        var stringsByText = new Dictionary<string, int>(
            declarationTables.Strings.Count + program.Functions.Length,
            StringComparer.Ordinal);
        for (var row = 1; row <= declarationTables.Strings.Count; row++)
        {
            var text = declarationTables.Strings.DecodeNamespace(row);
            stringsRows.Add((text, 1));
            _ = stringsByText.TryAdd(text, row);
        }

        int GetStringRow(string text)
        {
            if (!stringsByText.TryGetValue(text, out var row))
            {
                row = stringsRows.Count + 1;
                stringsRows.Add((text, 1));
                stringsByText.Add(text, row);
            }

            return row;
        }

        var moduleRefRows = new List<TrRmModuleRefRow>(checked(
            declarationTables.ModuleRefs.Count
                + methodImportEdgesByFunction.Count));
        var moduleRefRowByBinding = new Dictionary<(
            string Identity, uint TargetRequirement, uint RuntimePolicy), int>();
        var requirementsByProviderIdentity = new Dictionary<string,
            (uint TargetRequirement, uint RuntimePolicy)>(
            StringComparer.Ordinal);
        for (var moduleRefRow = 1;
            moduleRefRow <= declarationTables.ModuleRefs.Count;
            moduleRefRow++)
        {
            var identity
                = declarationTables.ModuleRefs.GetProducerIdentity(
                    moduleRefRow);
            var (targetRequirement, runtimePolicy)
                = declarationTables.ModuleRefs.GetRequirements(moduleRefRow);
            if (requirementsByProviderIdentity.TryGetValue(identity,
                    out var existingRequirements)
                && (existingRequirements.TargetRequirement
                        != targetRequirement
                    || existingRequirements.RuntimePolicy != runtimePolicy))
            {
                throw Unsupported(
                    "L1 ModuleRefs retain conflicting requirements for one provider identity.");
            }
            _ = requirementsByProviderIdentity.TryAdd(identity,
                (targetRequirement, runtimePolicy));
            moduleRefRows.Add(new TrRmModuleRefRow(GetStringRow(identity),
                targetRequirement, runtimePolicy));
            _ = moduleRefRowByBinding.TryAdd(
                (identity, targetRequirement, runtimePolicy), moduleRefRow);
        }

        var allImportRows = new List<TrRmImportRow>(
            checked(typeImports.Count + methodImportEdgesByFunction.Count));
        allImportRows.AddRange(typeImports);
        var methodImportRowByFunction = new Dictionary<int, int>(
            methodImportEdgesByFunction.Count);
        var methodMapTokenByFunction = new uint[program.Functions.Length];
        for (var functionIndex = 0;
            functionIndex < program.Functions.Length; functionIndex++)
        {
            var methodRow = methodDefRowByFunction[functionIndex];
            methodMapTokenByFunction[functionIndex] = methodRow == 0 ? 0
                : 0x1600_0000u | checked((uint)methodRow);
        }

        var importedMethodEdges = methodImportEdgesByFunction.Values
            .OrderBy(static edge => edge.ImportRow).ToArray();
        var seenMethodImportRows = new HashSet<int>();
        foreach (var edge in importedMethodEdges)
        {
            if (edge.ImportRow <= 0
                || !seenMethodImportRows.Add(edge.ImportRow))
            {
                throw Invalid(
                    "The L1 root has duplicate or invalid MethodDef Import edges.");
            }

            var binding = (edge.ProducerIdentity, edge.TargetRequirement,
                edge.RuntimePolicy);
            if (requirementsByProviderIdentity.TryGetValue(
                    edge.ProducerIdentity, out var existingRequirements)
                && (existingRequirements.TargetRequirement
                        != edge.TargetRequirement
                    || existingRequirements.RuntimePolicy
                        != edge.RuntimePolicy))
            {
                throw Invalid(
                    "L1 TypeDef and MethodDef Imports disagree on one provider's requirements.");
            }
            _ = requirementsByProviderIdentity.TryAdd(
                edge.ProducerIdentity,
                (edge.TargetRequirement, edge.RuntimePolicy));

            if (!moduleRefRowByBinding.TryGetValue(binding,
                    out var moduleRefRow))
            {
                moduleRefRows.Add(new TrRmModuleRefRow(
                    GetStringRow(edge.ProducerIdentity),
                    edge.TargetRequirement, edge.RuntimePolicy));
                moduleRefRow = moduleRefRows.Count;
                moduleRefRowByBinding.Add(binding, moduleRefRow);
            }

            var functionIndex = edge.Expected.FunctionIndex;
            var expectedMethodFunctionIndex
                = methodProviderRootModule!.FunctionIndexByMethodRow[
                    edge.Expected.MethodDefRow];
            var expectedMethodRow
                = methodDefRowByFunction[expectedMethodFunctionIndex];
            if (expectedMethodRow == 0)
            {
                throw Invalid(
                    "An L1 MethodDef Import has no emitted expected MethodDef row.");
            }
            var importRow = checked(allImportRows.Count + 1);
            allImportRows.Add(new TrRmImportRow(moduleRefRow,
                GetStringRow(edge.ExportKey),
                0x1600_0000u | checked((uint)expectedMethodRow)));
            methodImportRowByFunction.Add(functionIndex, importRow);
            methodMapTokenByFunction[functionIndex] = 0x1C00_0000u
                | checked((uint)importRow);
            methodMapTokenByFunction[expectedMethodFunctionIndex]
                = 0x1C00_0000u | checked((uint)importRow);
            importedMethodFunctions[expectedMethodFunctionIndex] = true;
        }
        var expectedImportedMethodFunctions
            = new HashSet<int>(methodImportRowByFunction.Keys);
        expectedImportedMethodFunctions.UnionWith(
            methodImportEdgesByDefinitionFunction.Keys);
        if (methodImportRowByFunction.Count
                != methodImportEdgesByFunction.Count
            || importedMethodFunctions.Count(static imported => imported)
                != expectedImportedMethodFunctions.Count)
        {
            throw Invalid(
                "The L1 root MethodDef Import edges do not match its bodyless stubs.");
        }

        var methodNames = new int[program.Functions.Length];
        var signatureRows = new List<TrRmMethodSigRow>(
            program.Functions.Length);
        var parameterRows = new List<TrRmParamRow>();
        var methodRows = new List<TrRmMethodDefRow>(
            program.Functions.Length);
        var bodyRowByFunction = new int[program.Functions.Length];
        var exportRows = ReadExportRows(declarationIndex);
        var exportedNames = new HashSet<string>(StringComparer.Ordinal);
        var methodContractFirst = new int[program.Functions.Length];
        var methodContractCount = new int[program.Functions.Length];
        var contractRows = new List<TrRmContractRow>();
        var hasRawUnionReturnCoverage = false;
        foreach (var export in exportRows)
        {
            _ = exportedNames.Add(
                declarationTables.Strings.DecodeName(
                    export.ExportKeyStringRow));
        }

        var typeSpecRows = ReadVariableRows(
            GetRequiredSection(declarationIndex, 0x0011),
            declarationTables.TypeSpecs.Count, 2, 1);
        var typeSpecRefs = CreateLocalAggregateTypeSpecMap(program,
            declarationTables.TypeSpecs, typeImports, typeSpecRows);
        var scalarValues = new TrRmFrozenScalarValueBuilder();
        var bodies = new List<ReadOnlyMemory<byte>>(
            program.Functions.Length);
        var fieldFirstByStructure = new int[program.Structures.Length];
        var nextFieldRow = 1;
        for (var structureIndex = 0;
            structureIndex < program.Structures.Length; structureIndex++)
        {
            fieldFirstByStructure[structureIndex]
                = program.Structures[structureIndex].Fields.IsEmpty
                    ? 0 : nextFieldRow;
            nextFieldRow = checked(nextFieldRow
                + program.Structures[structureIndex].Fields.Length);
        }

        for (var functionIndex = 0;
            functionIndex < program.Functions.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            var methodRow = methodDefRowByFunction[functionIndex];
            if (methodRow == 0)
            {
                continue;
            }

            var signatureRow = signatureRows.Count + 1;
            if (signatureRow != methodRow)
            {
                throw Invalid(
                    "L1 MethodDef and MethodSig rows lost their retained order.");
            }
            var firstParameter = function.ParameterCount == 0
                ? 0 : parameterRows.Count + 1;
            for (var ordinal = 0; ordinal < function.ParameterCount;
                ordinal++)
            {
                var parameter = function.Variables[ordinal];
                parameterRows.Add(new TrRmParamRow(signatureRow, ordinal,
                    typeSpecRefs[parameter.Type], 0));
            }

            signatureRows.Add(new TrRmMethodSigRow(
                typeSpecRefs[function.ReturnType], 0, firstParameter,
                function.ParameterCount, 0, 0));
            methodNames[functionIndex] = GetStringRow(function.Name);
            if (!function.RawUnionReturnCoverage.IsDefaultOrEmpty)
            {
                if (!SemanticVerifier.IsRawUnionType(program,
                        function.ReturnType))
                {
                    throw Unsupported(
                        "A ReturnCoverage summary is attached to a non-Union result.");
                }

                var initializedBytes = function.RawUnionReturnCoverage;
                var guaranteedBytes
                    = SemanticVerifier
                        .RawUnionGuaranteedInitializedPayloadBytes(
                            function.ReturnType, program);
                if (initializedBytes.Length != guaranteedBytes.Length)
                {
                    throw Unsupported(
                        "A ReturnCoverage summary differs from its selected Union layout.");
                }

                var strongerThanGuarantee = false;
                for (var offset = 0; offset < initializedBytes.Length;
                    offset++)
                {
                    strongerThanGuarantee |= initializedBytes[offset] != 0
                        && guaranteedBytes[offset] == 0;
                }
                if (strongerThanGuarantee)
                {
                    methodContractFirst[functionIndex]
                        = contractRows.Count + 1;
                    contractRows.Add(new TrRmContractRow(
                        0x1600_0000u | checked((uint)methodRow), 0x0F,
                        CreateReturnCoveragePayload(
                            initializedBytes.AsSpan())));
                    methodContractCount[functionIndex] = 1;
                    hasRawUnionReturnCoverage = true;
                }
            }

            var visibility = function.IsPrivate ? 0u
                : function.IsInternal ? 1u : 2u;
            methodRows.Add(new TrRmMethodDefRow(0,
                methodNames[functionIndex], signatureRow, visibility,
                methodContractFirst[functionIndex],
                methodContractCount[functionIndex], 0));
            if (!importedMethodDefinitionFunctions[functionIndex]
                && visibility == 2)
            {
                if (!exportedNames.Add(function.Name))
                {
                    throw Unsupported(
                        "L1 MethodDef export names must be unique within the local Module.");
                }

                exportRows.Add(new TrRmExportRow(
                    0x1600_0000u | checked((uint)methodRow),
                    methodNames[functionIndex]));
            }
        }

        var moduleNameRow = GetStringRow(moduleIdentity);
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringsRows);
        var strings = new TrRmStringTableReader(stringBytes,
            stringsRows.Count);
        var featureRows = new List<TrRmFeatureRow>(
            declarationTables.Features.Count + 1);
        for (var row = 1; row <= declarationTables.Features.Count; row++)
        {
            featureRows.Add(declarationTables.Features.GetFeature(row));
        }
        if (hasRawUnionReturnCoverage)
        {
            if (!featureRows.Contains(TrRmFeatureRow.RootlessNativeValues))
            {
                featureRows.Add(TrRmFeatureRow.RootlessNativeValues);
            }
            if (!featureRows.Contains(
                    TrRmFeatureRow.RawUnionValueCoverageCandidate))
            {
                featureRows.Add(
                    TrRmFeatureRow.RawUnionValueCoverageCandidate);
            }
        }
        if (!featureRows.Contains(TrRmFeatureRow.TypedFrozenValues))
        {
            featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
        }
        featureRows.Sort(static (left, right)
            => left.FeatureId.CompareTo(right.FeatureId));
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, TrRmArtifactSchema.LocalAggregateLayoutDraft);

        var moduleRefCount = moduleRefRows.Count;
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(moduleNameRow, 1, 0,
                program.EntryPoint < 0 ? 0
                    : methodDefRowByFunction[program.EntryPoint],
                1, 1, 1, 1,
                featureRows.Count == 0 ? 0 : 1, featureRows.Count),
        ], strings, moduleRefCount, declarationTables.TypeDefs.Count,
            methodRows.Count, 0, featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount, declarationTables.TypeDefs.Count,
            methodRows.Count, 0,
            featureRows.Count);
        var moduleRefBytes = moduleRefRows.Count == 0 ? []
            : TrRmModuleRefTableWriter.Encode(moduleRefRows, strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes,
            moduleRefCount, strings, modules);
        var typeDefBytes = declarationTables.TypeDefs.Count == 0 ? []
            : GetRequiredSection(declarationIndex, 0x0013);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            declarationTables.TypeDefs.Count, strings, modules,
            declarationTables.Fields.Count,
            declarationTables.GenericParams.Count,
            declarationTables.Layouts.Count,
            contractRows.Count,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefs.Count, allImportRows.Count, 0, 0,
            TrRmArtifactSchema.LocalAggregateLayoutDraft,
            methodDefCount: methodRows.Count);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, typeDefs.Count, allImportRows.Count, 0, 0,
            TrRmArtifactSchema.LocalAggregateLayoutDraft,
            methodDefCount: methodRows.Count);
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, parameterRows.Count, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count, typeSpecs, parameterRows.Count, 0);
        var parameterBytes = parameterRows.Count == 0
            ? [] : TrRmParamTableWriter.Encode(parameterRows, signatures,
                typeSpecs, TrRmArtifactSchema.LocalAggregateLayoutDraft);
        var parameters = parameterRows.Count == 0
            ? default
            : new TrRmParamTableReader(parameterBytes,
                parameterRows.Count, signatures, typeSpecs,
                TrRmArtifactSchema.LocalAggregateLayoutDraft);

        for (var functionIndex = 0;
            functionIndex < program.Functions.Length; functionIndex++)
        {
            if (methodDefRowByFunction[functionIndex] == 0
                || importedMethodDefinitionFunctions[functionIndex])
            {
                continue;
            }

            bodies.Add(EncodeLocalAggregateLayoutBody(program,
                program.Functions[functionIndex], methodMapTokenByFunction,
                importedMethodFunctions, typeSpecRefs,
                fieldFirstByStructure, declarationTables.FixedArrayLayouts,
                scalarValues, contractRows.Count));
            bodyRowByFunction[functionIndex] = bodies.Count;
        }
        for (var methodRow = 1; methodRow <= methodRows.Count;
            methodRow++)
        {
            var functionIndex = methodFunctionIndexByRow[methodRow];
            if (functionIndex < 0)
            {
                throw Invalid(
                    "The L1 MethodDef row has no retained function mapping.");
            }

            var isImportedMethod = importedMethodDefinitionFunctions[
                functionIndex];
            if (isImportedMethod
                ? bodyRowByFunction[functionIndex] != 0
                : bodyRowByFunction[functionIndex] == 0)
            {
                throw Unsupported(
                    "L1 MethodDef Body ownership differs from its selected local/import identity.");
            }

            methodRows[methodRow - 1] = methodRows[methodRow - 1] with {
                BodyRow = bodyRowByFunction[functionIndex],
            };
        }

        var scalarTables = scalarValues.Encode(typeSpecs);
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows,
            strings, modules, typeDefs, signatures,
            contractRows.Count,
            bodies.Count);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodRows.Count, strings, modules, typeDefs, signatures,
            contractRows.Count, bodies.Count);
        var contractBytes = contractRows.Count == 0 ? []
            : TrRmContractTableWriter.EncodeForeignInvocationDraft(
                contractRows, typeDefs, methods, signatures,
                parameters, typeSpecs, features, modules,
                predicateGraphCount: 0,
                schema: TrRmArtifactSchema.LocalAggregateLayoutDraft);
        var fieldsBytes = GetOptionalSection(declarationIndex, 0x0014);
        var fields = declarationTables.Fields.Count == 0 ? default
            : new TrRmFieldDefTableReader(fieldsBytes,
                declarationTables.Fields.Count, strings, typeDefs,
                typeSpecs);
        var importBytes = allImportRows.Count == 0 ? []
            : TrRmImportTableWriter.Encode(allImportRows, strings, moduleRefs,
                typeDefs, methods, default, default, default, fields,
                features, modules,
                TrRmArtifactSchema.LocalAggregateLayoutDraft);
        var imports = allImportRows.Count == 0
            ? default
            : new TrRmImportTableReader(importBytes, allImportRows.Count,
                strings, moduleRefs, typeDefs, methods, default, default,
                default, fields, features, modules,
                TrRmArtifactSchema.LocalAggregateLayoutDraft);
        var exportBytes = exportRows.Count == 0
            ? [] : TrRmExportTableWriter.Encode(exportRows, strings,
                typeDefs, methods, default, imports, default, default,
                fields, features, modules,
                TrRmArtifactSchema.LocalAggregateLayoutDraft);

        var sections = new List<TrRmSection>(
            declarationIndex.SectionCount + 8);
        for (var sectionIndex = 0;
            sectionIndex < declarationIndex.SectionCount; sectionIndex++)
        {
            declarationIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var data, out var count);
            if (kind is 0x0001 or 0x0002 or 0x0004 or 0x0010 or 0x0011
                or 0x0016 or 0x0017 or 0x0018 or 0x001D or 0x0020
                or 0x0003 or 0x0030 or 0x003F or 0x001B or 0x001C)
            {
                continue;
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), data.ToArray()));
        }

        sections.Add(new TrRmSection(0x0001, 0, 1, moduleBytes));
        sections.Add(new TrRmSection(0x0002, 0,
            checked((uint)stringsRows.Count), stringBytes));
        sections.Add(new TrRmSection(0x0004, 0,
            checked((uint)featureRows.Count), featureBytes));
        if (moduleRefRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0010, 0,
                checked((uint)moduleRefRows.Count), moduleRefBytes));
        }
        sections.Add(new TrRmSection(0x0011, 0,
            checked((uint)typeSpecRows.Count), typeSpecBytes));
        sections.Add(new TrRmSection(0x0016, 0,
            checked((uint)methodRows.Count), methodBytes));
        sections.Add(new TrRmSection(0x0017, 0,
            checked((uint)signatureRows.Count), signatureBytes));
        if (parameterRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0,
                checked((uint)parameterRows.Count), parameterBytes));
        }
        if (allImportRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                checked((uint)allImportRows.Count), importBytes));
        }
        if (contractRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001B, 0,
                checked((uint)contractRows.Count), contractBytes));
        }
        if (exportRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                checked((uint)exportRows.Count), exportBytes));
        }
        sections.Add(new TrRmSection(0x0020, 0,
            checked((uint)bodies.Count),
            TrRmVariableTableWriter.EncodeBodies(bodies)));
        if (scalarTables.BlobCount != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                checked((uint)scalarTables.BlobCount),
                scalarTables.BlobSection));
        }
        if (scalarTables.ConstantSourceCount != 0)
        {
            sections.Add(new TrRmSection(0x0030, 0,
                checked((uint)scalarTables.ConstantSourceCount),
                scalarTables.ConstantSourceSection));
            sections.Add(new TrRmSection(0x003F, 0,
                checked((uint)scalarTables.FrozenValueNodeCount),
                scalarTables.FrozenValueNodeSection));
        }
        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));

        var artifact = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
        var artifactIndex
            = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var validatedTables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    artifactIndex);
        for (var methodRow = 1; methodRow <= validatedTables.Methods.Count;
            methodRow++)
        {
            if (validatedTables.Methods.GetBodyRow(methodRow) != 0)
            {
                validatedTables.ValidateRegisterBodyMapsForMethod(methodRow);
            }
        }

        return artifact;
    }

    private static Dictionary<PrimitiveType, uint>
        CreateLocalAggregateTypeSpecMap(BoundProgram program,
            TrRmTypeSpecTableReader typeSpecs,
            List<TrRmImportRow> importRows,
            List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        var result = new Dictionary<PrimitiveType, uint>();
        for (var row = 1; row <= typeSpecs.Count; row++)
        {
            var typeSpec = typeSpecs.GetRow(row);
            if (typeSpec.Length == 3 && typeSpec[0] == 0x01
                && typeSpec[1] == 0)
            {
                var codeIndex = Array.IndexOf(s_codes, typeSpec[2]);
                if (codeIndex >= 0)
                {
                    _ = result.TryAdd(s_types[codeIndex],
                        checked((uint)row));
                }
            }
            else if (typeSpec.Length == 6 && typeSpec[0] == 0x02
                && typeSpec[1] == 0)
            {
                var typeDefRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(typeSpec[2..]));
                if ((uint)typeDefRow <= (uint)program.Structures.Length
                    && typeDefRow != 0)
                {
                    _ = result.TryAdd(
                        (PrimitiveType)(99 + typeDefRow),
                        checked((uint)row));
                }
            }
            else if (typeSpec.Length == 6 && typeSpec[0] == 0x03
                && typeSpec[1] == 0 && importRows.Count != 0)
            {
                var importRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(typeSpec[2..]));
                if (importRow <= 0 || importRow > importRows.Count)
                {
                    throw Unsupported(
                        "An L1 TypeSpec Import does not select a retained local expectation.");
                }
                var expectedDefinition
                    = importRows[importRow - 1].ExpectedLocalDefId;
                var typeDefRow = checked((int)(expectedDefinition
                    & 0x00FF_FFFF));
                if (expectedDefinition >> 24 == 0x13
                    && (uint)typeDefRow <= (uint)program.Structures.Length
                    && typeDefRow != 0)
                {
                    _ = result.TryAdd(
                        (PrimitiveType)(99 + typeDefRow),
                        checked((uint)row));
                }
            }
        }

        for (var structureIndex = 0;
            structureIndex < program.Structures.Length; structureIndex++)
        {
            var type = (PrimitiveType)(100 + structureIndex);
            if (result.ContainsKey(type))
            {
                continue;
            }

            var typeSpec = TrRmTypeSpecTableWriter.Reference(0x02,
                checked((uint)(structureIndex + 1)));
            typeSpecRows.Add(typeSpec);
            result.Add(type, checked((uint)typeSpecRows.Count));
        }

        for (var arrayIndex = 0;
            arrayIndex < program.NativeFixedArrayTypes.Length; arrayIndex++)
        {
            _ = GetLocalAggregateTypeSpecRef(program, typeSpecs,
                typeSpecRows, result, NativeFixedArrayTypes.Type(arrayIndex));
        }
        foreach (var function in program.Functions)
        {
            _ = GetLocalAggregateTypeSpecRef(program, typeSpecs,
                typeSpecRows, result, function.ReturnType);
            foreach (var variable in function.Variables)
            {
                _ = GetLocalAggregateTypeSpecRef(program, typeSpecs,
                    typeSpecRows, result, variable.Type);
            }
        }

        return result;
    }

    private static uint GetLocalAggregateTypeSpecRef(BoundProgram program,
        TrRmTypeSpecTableReader typeSpecs,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        Dictionary<PrimitiveType, uint> typeSpecRefs,
        PrimitiveType type)
    {
        if (type == PrimitiveType.Void)
        {
            typeSpecRefs[type] = 0xF200_0000;
            return 0xF200_0000;
        }
        if (typeSpecRefs.TryGetValue(type, out var existingTypeSpec))
        {
            return existingTypeSpec;
        }
        if (NativeFixedArrayTypes.IsType(type))
        {
            var arrayIndex = NativeFixedArrayTypes.Index(type);
            if ((uint)arrayIndex >= (uint)program.NativeFixedArrayTypes.Length)
            {
                throw Unsupported(
                    "An L1 signature refers to an unregistered fixed-array type.");
            }

            var definition = program.NativeFixedArrayTypes[arrayIndex];
            var elementTypeRef = GetLocalAggregateTypeSpecRef(program,
                typeSpecs, typeSpecRows, typeSpecRefs, definition.ElementType);
            for (var row = 1; row <= typeSpecs.Count; row++)
            {
                if (typeSpecs.TryGetFixedInlineArray((uint)row,
                        out var candidateElementRef,
                        out var candidateCount)
                    && candidateCount == definition.ElementCount
                    && typeSpecs.AreEquivalentTypeSpecRefs(
                        candidateElementRef, elementTypeRef))
                {
                    typeSpecRefs.Add(type, checked((uint)row));
                    return checked((uint)row);
                }
            }

            throw Unsupported(
                "L1 executable array signatures require an emitted fixed-array layout row.");
        }

        var scalarIndex = Array.IndexOf(s_types, type);
        if (scalarIndex >= 0)
        {
            var inlineTypeSpecRef = 0xF200_0000u | s_codes[scalarIndex];
            typeSpecRefs.Add(type, inlineTypeSpecRef);
            return inlineTypeSpecRef;
        }

        throw Unsupported($"L1 signature type {type} has no selected TypeSpec.");
    }

    private static List<ReadOnlyMemory<byte>> ReadVariableRows(
        ReadOnlySpan<byte> table, int rowCount, int minimumRowBytes,
        int alignment)
    {
        var index = new TrRmVariableTableIndex(table, rowCount,
            minimumRowBytes, alignment);
        var rows = new List<ReadOnlyMemory<byte>>(rowCount);
        for (var row = 1; row <= rowCount; row++)
        {
            rows.Add(index.GetRow(row).ToArray());
        }

        return rows;
    }

    private static bool IsLocalAggregateLayoutValueType(BoundProgram program,
        PrimitiveType type)
    {
        if (NativeFixedArrayTypes.IsType(type))
        {
            return (uint)NativeFixedArrayTypes.Index(type)
                < (uint)program.NativeFixedArrayTypes.Length;
        }

        var structureIndex = (int)type - 100;
        return PrimitiveTypeFacts.ScalarWidth(type) >= 0
            || (structureIndex >= 0
                && structureIndex < program.Structures.Length);
    }

    private static byte[] EncodeLocalAggregateLayoutBody(
        BoundProgram program, BoundFunction function,
        uint[] methodMapTokenByFunction, bool[] importedMethodFunctions,
        Dictionary<PrimitiveType, uint> typeSpecRefs,
        int[] fieldFirstByStructure,
        TrRmFixedArrayLayoutTableReader arrayLayouts,
        TrRmFrozenScalarValueBuilder scalarValues,
        int contractCount)
    {
        return EncodeLocalAggregateLayoutBodyCore(program, function,
            methodMapTokenByFunction, importedMethodFunctions, typeSpecRefs,
            fieldFirstByStructure, arrayLayouts, scalarValues, contractCount);
    }

    private static byte[] EncodeLocalAggregateLayoutBodyCore(
        BoundProgram program, BoundFunction function,
        uint[] methodMapTokenByFunction, bool[] importedMethodFunctions,
        Dictionary<PrimitiveType, uint> typeSpecRefs,
        int[] fieldFirstByStructure,
        TrRmFixedArrayLayoutTableReader arrayLayouts,
        TrRmFrozenScalarValueBuilder scalarValues,
        int contractCount)
    {
        var localCount = function.Variables.Length
            - function.ParameterCount;
        var temporaryHomeByExpression
            = new Dictionary<BoundExpression, int>(
                ReferenceEqualityComparer.Instance);
        var temporaryTypes = new List<PrimitiveType>();
        void AddTemporary(BoundExpression expression)
        {
            if (temporaryHomeByExpression.ContainsKey(expression))
            {
                return;
            }
            if (expression.Type == PrimitiveType.Void)
            {
                return;
            }
            if (!IsLocalAggregateLayoutValueType(program,
                    expression.Type))
            {
                throw Unsupported(
                    "L1 Body capture Homes require a supported non-unit value type.");
            }

            var home = checked(localCount + temporaryTypes.Count);
            temporaryHomeByExpression.Add(expression, home);
            temporaryTypes.Add(expression.Type);
        }

        void CollectArrayInitializerTemporaries(
            BoundNativeFixedArrayInitialization init)
        {
            foreach (var element in init.Elements)
            {
                switch (element)
                {
                    case BoundNativeFixedArrayInitialization nested:
                        AddTemporary(nested);
                        CollectArrayInitializerTemporaries(nested);
                        break;
                    case BoundNativeFixedArrayRepeatInitialization repeat:
                        AddTemporary(repeat);
                        AddTemporary(repeat.Seed);
                        break;
                    default:
                        if (element.Type != PrimitiveType.Void)
                        {
                            AddTemporary(element);
                        }
                        break;
                }
            }
        }

        var methodMapTokens = new List<uint>();
        var methodMapTokenSet = new HashSet<uint>();
        if (!BoundTreeTraversal.VisitExpressions(function.Body,
            expression =>
            {
                switch (expression)
                {
                    case BoundNativeFixedArrayElement element
                        when element.Index is not BoundRead:
                        AddTemporary(element.Index);
                        break;
                    case BoundNativeFixedArrayWrite write
                        when write.Index is not BoundRead:
                        AddTemporary(write.Index);
                        break;
                    case BoundNativeFixedArrayPathElement pathElement:
                        foreach (var projection in pathElement.Projections)
                        {
                            if (projection.Index is not BoundRead)
                            {
                                AddTemporary(projection.Index);
                            }
                        }
                        break;
                    case BoundNativeFixedArrayPathWrite pathWrite:
                        foreach (var projection in pathWrite.Projections)
                        {
                            if (projection.Index is not BoundRead)
                            {
                                AddTemporary(projection.Index);
                            }
                        }
                        break;
                    case BoundNativeFixedArrayInitialization arrayInit:
                        AddTemporary(arrayInit);
                        CollectArrayInitializerTemporaries(arrayInit);
                        break;
                    case BoundNativeFixedArrayRepeatInitialization repeat:
                        AddTemporary(repeat);
                        AddTemporary(repeat.Seed);
                        break;
                    case BoundCall call:
                        if ((uint)call.Function
                                >= (uint)program.Functions.Length
                            || call.Arguments.Length
                                != program.Functions[call.Function]
                                    .ParameterCount
                            || call.Type
                                != program.Functions[call.Function]
                                    .ReturnType)
                        {
                            throw Unsupported(
                                "An L1 direct call does not match its selected local MethodDef.");
                        }

                        var token = methodMapTokenByFunction[call.Function];
                        if (methodMapTokenSet.Add(token))
                        {
                            methodMapTokens.Add(token);
                        }
                        if (call.Type != PrimitiveType.Void
                            && !DirectNativeImportContract.IsScalar(call.Type))
                        {
                            AddTemporary(call);
                        }
                        foreach (var argument in call.Arguments)
                        {
                            AddTemporary(argument);
                        }
                        break;
                    case BoundBinary binary:
                        AddTemporary(binary.Left);
                        break;
                    case BoundUpdate update when !update.IsPrefix:
                        AddTemporary(update);
                        break;
                    case BoundConditional conditional
                        when conditional.Type != PrimitiveType.Void:
                        AddTemporary(conditional);
                        break;
                    case BoundIndirectCall:
                    case BoundNativeFunctionAddress:
                    case BoundNativePointerAddress:
                    case BoundNativeFixedArrayPointerAddress:
                    case BoundNativeStorageAddress:
                        throw Unsupported(
                            "L1 executable Bodies do not contain native address or indirect-call operations.");
                }
            }))
        {
            throw Unsupported(
                "L1 Body capture analysis could not traverse the function.");
        }

        var formalHomeStart = checked(localCount + temporaryTypes.Count);
        var homeCount = checked(formalHomeStart
            + function.ParameterCount);
        if (homeCount > ushort.MaxValue + 1)
        {
            throw Unsupported("An L1 Body exceeds the Home index domain.");
        }

        uint GetTypeSpecRef(PrimitiveType type)
        {
            if (type == PrimitiveType.Void)
            {
                return 0xF200_0000;
            }
            if (typeSpecRefs.TryGetValue(type, out var typeSpecRef))
            {
                return typeSpecRef;
            }

            var scalarIndex = Array.IndexOf(s_types, type);
            if (scalarIndex < 0)
            {
                throw Unsupported(
                    $"L1 Body type {type} has no selected TypeSpec.");
            }

            return 0xF200_0000u | s_codes[scalarIndex];
        }

        uint TypeToken(PrimitiveType type)
        {
            return CreateStandardBodyTypeMapToken(GetTypeSpecRef(type));
        }

        var typeTokens = new List<uint>();
        for (var index = 0; index < function.Variables.Length; index++)
        {
            var type = function.Variables[index].Type;
            typeTokens.Add(TypeToken(type));
        }
        foreach (var target in program.Functions)
        {
            if (target.ReturnType != PrimitiveType.Void)
            {
                typeTokens.Add(TypeToken(target.ReturnType));
            }
            for (var parameter = 0; parameter < target.ParameterCount;
                parameter++)
            {
                typeTokens.Add(TypeToken(target.Variables[parameter].Type));
            }
        }
        if (function.ReturnType != PrimitiveType.Void)
        {
            typeTokens.Add(TypeToken(function.ReturnType));
        }
        foreach (var structure in program.Structures)
        {
            foreach (var field in structure.Fields)
            {
                typeTokens.Add(TypeToken(field.Type));
            }
        }
        foreach (var arrayType in program.NativeFixedArrayTypes)
        {
            typeTokens.Add(TypeToken(arrayType.ElementType));
        }
        if (!BoundTreeTraversal.VisitExpressions(function.Body,
            expression =>
            {
                if (expression.Type != PrimitiveType.Void)
                {
                    typeTokens.Add(TypeToken(expression.Type));
                }
            }))
        {
            throw Unsupported(
                "L1 Body type-map collection could not traverse the function.");
        }
        foreach (var type in temporaryTypes)
        {
            typeTokens.Add(TypeToken(type));
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            methodMapTokens);
        var homeMap = new byte[checked(homeCount * 8)];
        for (var variable = function.ParameterCount;
            variable < function.Variables.Length; variable++)
        {
            var homeIndex = variable - function.ParameterCount;
            var variableInfo = function.Variables[variable];
            var offset = homeIndex * 8;
            homeMap[offset] = 0x03;
            homeMap[offset + 1] = variableInfo.IsMutable ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                maps.GetTypeMapIndex(TypeToken(variableInfo.Type)));
        }
        for (var temporary = 0; temporary < temporaryTypes.Count;
            temporary++)
        {
            var offset = checked((localCount + temporary) * 8);
            homeMap[offset] = 0x03;
            homeMap[offset + 1] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                maps.GetTypeMapIndex(TypeToken(temporaryTypes[temporary])));
        }
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var offset = checked((formalHomeStart + parameter) * 8);
            var variableInfo = function.Variables[parameter];
            homeMap[offset] = 0x02;
            homeMap[offset + 1] = variableInfo.IsMutable ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                maps.GetTypeMapIndex(TypeToken(variableInfo.Type)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4), checked((ushort)parameter));
        }

        var constantRows = new List<TrRmFrozenScalarValueReference>();
        var constantMapIndexByValue
            = new Dictionary<(PrimitiveType Type, ScalarBits Bits), ushort>();
        ushort GetConstantMapIndex(BoundConstant constant)
        {
            var key = (constant.Type, constant.Bits);
            if (constantMapIndexByValue.TryGetValue(key,
                    out var existingIndex))
            {
                return existingIndex;
            }
            if (constantRows.Count == 65_536)
            {
                throw Unsupported(
                    "An L1 Body ConstantMap exceeds its selected index domain.");
            }

            var value = scalarValues.Add(constant);
            var mapIndex = checked((ushort)constantRows.Count);
            constantRows.Add(value);
            constantMapIndexByValue.Add(key, mapIndex);
            return mapIndex;
        }

        var records = new List<ReadOnlyMemory<byte>>();
        var entryState = new byte[8];
        entryState[0] = 0x01;
        records.Add(entryState);
        var code = new TrRmA3StagedList<byte>(null);
        var codeBlocks = new List<TrRmA3StagedList<byte>> {
            code,
        };
        var currentBlockIndex = 0;
        var currentMustLiveHomes = Enumerable.Range(formalHomeStart,
            function.ParameterCount).ToHashSet();
        var currentMayLiveHomes = new HashSet<int>(currentMustLiveHomes);
        var blockEntryHomeStates
            = new List<(HashSet<int> MustLive, HashSet<int> MayLive)?> {
                (new HashSet<int>(currentMustLiveHomes),
                    new HashSet<int>(currentMayLiveHomes)),
        };
        var blockEntryStateRecordIndices = new List<ushort> { 0 };
        (int Register, PrimitiveType Type, byte AccessKind)?
            pendingPlaceRegisterState = null;
        void SetBlockEntryHomes(int targetBlock)
        {
            if ((uint)targetBlock >= (uint)blockEntryHomeStates.Count)
            {
                throw Unsupported("An L1 branch targets an unknown Block.");
            }

            var incomingMustLive = new HashSet<int>(currentMustLiveHomes);
            var incomingMayLive = new HashSet<int>(currentMayLiveHomes);
            var existing = blockEntryHomeStates[targetBlock];
            if (existing is null)
            {
                blockEntryHomeStates[targetBlock]
                    = (incomingMustLive, incomingMayLive);
            }
            else
            {
                var mergedMustLive
                    = new HashSet<int>(existing.Value.MustLive);
                mergedMustLive.IntersectWith(incomingMustLive);
                var mergedMayLive
                    = new HashSet<int>(existing.Value.MayLive);
                mergedMayLive.UnionWith(incomingMayLive);
                blockEntryHomeStates[targetBlock]
                    = (mergedMustLive, mergedMayLive);
            }
        }

        int CreateBlock()
        {
            if (codeBlocks.Count >= ushort.MaxValue)
            {
                throw Unsupported("An L1 Body exceeds the Block index domain.");
            }

            var block = codeBlocks.Count;
            codeBlocks.Add(new TrRmA3StagedList<byte>(null));
            blockEntryHomeStates.Add(null);
            if (pendingPlaceRegisterState is { } placeState)
            {
                var entryState = new byte[13];
                entryState[0] = 0x01;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    entryState.AsSpan(4), 1);
                entryState[8] = checked((byte)placeState.Register);
                entryState[9] = 1;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    entryState.AsSpan(10),
                    maps.GetTypeMapIndex(TypeToken(placeState.Type)));
                entryState[12] = placeState.AccessKind;
                blockEntryStateRecordIndices.Add(
                    checked((ushort)records.Count));
                records.Add(entryState);
            }
            else
            {
                blockEntryStateRecordIndices.Add(0);
            }
            return block;
        }

        void SelectBlock(int blockIndex)
        {
            if ((uint)blockIndex >= (uint)codeBlocks.Count)
            {
                throw Unsupported("An L1 branch targets an unknown Block.");
            }
            if (blockEntryHomeStates[blockIndex] is not { } entryHomes)
            {
                throw Unsupported(
                    "An L1 CFG block has no incoming Home state.");
            }

            currentBlockIndex = blockIndex;
            currentMustLiveHomes = [.. entryHomes.MustLive];
            currentMayLiveHomes = [.. entryHomes.MayLive];
            code = codeBlocks[blockIndex];
        }

        void AppendBranch(int targetBlock)
        {
            if ((uint)targetBlock >= (uint)codeBlocks.Count
                || targetBlock > ushort.MaxValue)
            {
                throw Unsupported(
                    "An L1 unconditional branch target exceeds the Block index domain.");
            }

            SetBlockEntryHomes(targetBlock);
            code.Add(0xE3);
            code.Add(0);
            code.Add(unchecked((byte)targetBlock));
            code.Add((byte)(targetBlock >> 8));
        }

        int HomeForVariable(int variable)
        {
            if ((uint)variable >= (uint)function.Variables.Length)
            {
                throw Unsupported("An L1 Body variable has no Home.");
            }
            return variable < function.ParameterCount
                ? formalHomeStart + variable
                : variable - function.ParameterCount;
        }

        void AppendLoadConstant(BoundConstant constant, byte destination)
        {
            AppendConstantMapLoad(code, destination,
                        GetConstantMapIndex(constant));
        }

        void AppendPlaceSelection(ushort recordIndex, byte destination)
        {
            AppendRecordPlaceSelectionInstruction(code, recordIndex,
                        destination);
        }

        void AppendPlaceLoad(ushort recordIndex, byte destination)
        {
            code.Add(0xDA);
            code.Add(destination);
            code.Add(unchecked((byte)recordIndex));
            code.Add((byte)(recordIndex >> 8));
        }

        void AppendSemanticStore(ushort recordIndex)
        {
            code.Add(0xD9);
            code.Add(0);
            code.Add(unchecked((byte)recordIndex));
            code.Add((byte)(recordIndex >> 8));
        }

        void AppendHomeStoreToCurrentBlock(byte sourceRegister,
            int homeIndex)
        {
            if (currentMayLiveHomes.Contains(homeIndex)
                && !currentMustLiveHomes.Contains(homeIndex))
            {
                throw Unsupported(
                    "L1 HomeStore cannot target storage that is live on only some incoming paths.");
            }

            if (!currentMustLiveHomes.Contains(homeIndex))
            {
                _ = currentMustLiveHomes.Add(homeIndex);
                _ = currentMayLiveHomes.Add(homeIndex);
            }

            AppendHomeStore(code, sourceRegister, homeIndex);
        }

        void RequireVacantHomeInitialization(int homeIndex)
        {
            if (currentMayLiveHomes.Contains(homeIndex))
            {
                throw Unsupported(
                    "An L1 Home initialization does not have a vacant destination on every incoming path.");
            }
        }

        void MarkHomeLive(int homeIndex)
        {
            _ = currentMustLiveHomes.Add(homeIndex);
            _ = currentMayLiveHomes.Add(homeIndex);
        }

        void MarkHomeMaybeLive(int homeIndex)
        {
            _ = currentMayLiveHomes.Add(homeIndex);
        }

        void RequireVacantCallResultHome(int homeIndex)
        {
            RequireVacantHomeInitialization(homeIndex);
        }

        int FieldRow(PrimitiveType ownerType, int fieldIndex,
            out PrimitiveType fieldType)
        {
            var ownerIndex = (int)ownerType - 100;
            if ((uint)ownerIndex >= (uint)program.Structures.Length
                || (uint)fieldIndex
                    >= (uint)program.Structures[ownerIndex].Fields.Length
                || fieldFirstByStructure[ownerIndex] <= 0)
            {
                throw Unsupported(
                    "An L1 Body field place has no exact local FieldDef.");
            }

            fieldType = program.Structures[ownerIndex].Fields[fieldIndex].Type;
            return checked(fieldFirstByStructure[ownerIndex] + fieldIndex);
        }

        List<BoundFieldRead> GetFieldPath(BoundExpression expression,
            out BoundExpression root)
        {
            var path = new List<BoundFieldRead>();
            while (expression is BoundFieldRead fieldRead)
            {
                path.Add(fieldRead);
                expression = fieldRead.Receiver;
            }
            path.Reverse();
            root = expression;
            return path;
        }

        int CaptureIndexHome(BoundExpression index)
        {
            if (!NativeFixedArrayContract.IsIndexType(index.Type))
            {
                throw Unsupported(
                    "An L1 fixed-array index has no selected integer carrier.");
            }
            if (index is BoundUpdate update)
            {
                var indexHome = TemporaryHomeFor(update);
                AppendUpdate(update, 0, indexHome);
                return indexHome;
            }
            if (index is BoundRead read)
            {
                return HomeForVariable(read.Variable);
            }
            if (!temporaryHomeByExpression.TryGetValue(index,
                    out var home))
            {
                throw Unsupported(
                    "An L1 fixed-array index has no capture Home.");
            }

            AppendValue(index, 0);
            AppendHomeStoreToCurrentBlock(0, home);
            return home;
        }

        var arrayLayoutRows = new Dictionary<PrimitiveType, int>(
            program.NativeFixedArrayTypes.Length);
        for (var arrayIndex = 0;
            arrayIndex < program.NativeFixedArrayTypes.Length; arrayIndex++)
        {
            var arrayType = NativeFixedArrayTypes.Type(arrayIndex);
            var layoutRow = arrayLayouts.FindRow(1,
                GetTypeSpecRef(arrayType), 1);
            if (layoutRow <= 0)
            {
                throw Unsupported(
                    "An L1 fixed-array Body use has no selected layout row.");
            }
            arrayLayoutRows.Add(arrayType, layoutRow);
        }

        int GetArrayLayoutRow(PrimitiveType arrayType)
        {
            return arrayLayoutRows.TryGetValue(arrayType, out var layoutRow)
                ? layoutRow : throw Unsupported(
                    "An L1 Body refers to an unmapped fixed-array type.");
        }

        int TemporaryHomeFor(BoundExpression expression)
        {
            return temporaryHomeByExpression.TryGetValue(expression,
                out var home)
                ? home
                : throw Unsupported(
                    "An L1 Body expression has no preplanned snapshot Home.");
        }

        int SelectFieldPath(List<BoundFieldRead> path, int rootHome,
            PrimitiveType rootType, byte intermediateAccessKind,
            byte finalAccessKind, byte finalOpcode, byte finalRegister)
        {
            return SelectFieldPathFromBase(path, 2,
                        checked((ushort)rootHome), rootType,
                        intermediateAccessKind, finalAccessKind, finalOpcode,
                        finalRegister);
        }

        int SelectFieldPathFromBase(List<BoundFieldRead> path,
            byte rootBaseKind, ushort rootBaseIndex,
            PrimitiveType rootType, byte intermediateAccessKind,
            byte finalAccessKind, byte finalOpcode,
            byte finalRegister)
        {
            if (path.Count == 0)
            {
                throw Unsupported("An L1 field path cannot be empty.");
            }

            var ownerType = rootType;
            var previousRegister = rootBaseKind == 1
                ? rootBaseIndex : -1;
            for (var depth = 0; depth < path.Count; depth++)
            {
                var fieldRead = path[depth];
                if (fieldRead.Receiver.Type != ownerType)
                {
                    throw Unsupported(
                        "An L1 field path does not match its nominal owner type.");
                }

                var fieldRow = FieldRow(ownerType, fieldRead.Field,
                    out var fieldType);
                var isFinal = depth == path.Count - 1;
                var destination = isFinal
                    ? (finalRegister == previousRegister
                        ? ((finalRegister == 1) ? (byte)2 : (byte)1)
                        : finalRegister)
                    : checked((byte)((depth % 2) + 1));
                var baseKind = depth == 0 ? rootBaseKind : (byte)1;
                var baseIndex = depth == 0
                    ? rootBaseIndex : checked((ushort)previousRegister);
                var selectionIndex = checked((ushort)records.Count);
                records.Add(
                    TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                        targetKind: 2,
                        accessKind: isFinal ? finalAccessKind
                            : intermediateAccessKind,
                        destinationKind: 1, baseKind: baseKind,
                        destinationIndex: destination,
                        baseIndex: baseIndex,
                        indexKind: 0, targetRefKind: 1, indexIndex: 0,
                        targetRef: checked((uint)fieldRow),
                        referentTypeMapIndex: maps.GetTypeMapIndex(
                            TypeToken(fieldType)),
                        TrRmArtifactSchema.LocalAggregateLayoutDraft));
                if (isFinal)
                {
                    if (finalOpcode == 0xD8)
                    {
                        AppendPlaceSelection(selectionIndex, destination);
                    }
                    else
                    {
                        AppendPlaceLoad(selectionIndex, destination);
                    }
                }
                else
                {
                    AppendPlaceSelection(selectionIndex, destination);
                }

                previousRegister = destination;
                ownerType = fieldType;
            }

            return previousRegister;
        }

        void AppendFieldRead(BoundFieldRead fieldRead)
        {
            var path = GetFieldPath(fieldRead, out var root);
            var rootPlace = GetPlaceRoot(root, 1, 1);
            _ = SelectFieldPathFromBase(path, rootPlace.BaseKind,
                rootPlace.BaseIndex, rootPlace.Type, 1, 1, 0xDA, 0);
        }

        (byte SourceKind, ushort SourceIndex) GetStoreSource(
            BoundExpression value)
        {
            if (value is BoundRead read)
            {
                return (2, checked((ushort)HomeForVariable(read.Variable)));
            }
            if (value is BoundConstant constant)
            {
                if (constant.Type == PrimitiveType.Void
                    && constant.Bits.Low == 0
                    && constant.Bits.High == 0)
                {
                    return (0, 0);
                }

                AppendLoadConstant(constant, 0);
                return (1, 0);
            }
            if (value is BoundNativeFixedArrayInitialization
                or BoundNativeFixedArrayRepeatInitialization)
            {
                return (2, checked((ushort)CaptureValueHome(value)));
            }
            if (value is BoundCall call
                && call.Type != PrimitiveType.Void
                && !DirectNativeImportContract.IsScalar(call.Type))
            {
                if (!temporaryHomeByExpression.TryGetValue(call,
                        out var resultHome))
                {
                    throw Unsupported(
                        "An L1 aggregate call result has no typed snapshot Home.");
                }

                AppendCall(call, 2, checked((ushort)resultHome));
                return (2, checked((ushort)resultHome));
            }

            AppendValue(value, 0);
            return (1, 0);
        }

        void AppendFieldAssignment(BoundFieldAssignment assignment)
        {
            var rootHome = HomeForVariable(assignment.Variable);
            var rootType = function.Variables[assignment.Variable].Type;
            var initializesVacantHome
                = assignment.Variable == function.ConstructionHomeIndex
                    || function.Variables[assignment.Variable]
                        .IsInitializationLocal;
            var accessKind = initializesVacantHome ? (byte)3 : (byte)2;
            var fields = new List<int>(assignment.NestedFields.Length + 1) {
                assignment.Field,
            };
            fields.AddRange(assignment.NestedFields);
            var path = new List<BoundFieldRead>(fields.Count);
            BoundExpression receiver = new BoundRead(assignment.Variable,
                rootType, assignment.Span);
            var ownerType = rootType;
            foreach (var fieldIndex in fields)
            {
                var fieldRow = FieldRow(ownerType, fieldIndex,
                    out var fieldType);
                _ = fieldRow;
                var fieldRead = new BoundFieldRead(receiver, fieldIndex,
                    fieldType, assignment.Span);
                path.Add(fieldRead);
                receiver = fieldRead;
                ownerType = fieldType;
            }

            const byte storePlaceRegister = 13;
            var placeRegister = checked((byte)SelectFieldPath(path,
                rootHome, rootType, accessKind, accessKind, 0xD8,
                storePlaceRegister));
            var (SourceKind, SourceIndex) = GetStoreSource(assignment.Value);
            var transition = initializesVacantHome ? (byte)1 : (byte)2;
            if (SourceKind == 0)
            {
                AppendUnitSemanticStore(placeRegister, transition);
                if (transition == 1)
                {
                    MarkHomeMaybeLive(rootHome);
                }
                return;
            }

            var storeIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                    1, SourceKind, transition, 1,
                    placeRegister, SourceIndex,
                    TrRmArtifactSchema.LocalAggregateLayoutDraft));
            AppendSemanticStore(storeIndex);
            if (transition == 1)
            {
                MarkHomeMaybeLive(rootHome);
            }
        }

        (byte BaseKind, ushort BaseIndex) GetArrayBase(
            int variable, BoundExpression? fieldPlace, byte accessKind,
            byte destinationRegister)
        {
            if (fieldPlace is null)
            {
                return (2, checked((ushort)HomeForVariable(variable)));
            }

            var path = GetFieldPath(fieldPlace, out var root);
            var rootPlace = GetPlaceRoot(root, accessKind,
                destinationRegister == 1 ? (byte)2 : (byte)1);
            var selected = SelectFieldPathFromBase(path,
                rootPlace.BaseKind, rootPlace.BaseIndex, rootPlace.Type,
                accessKind, accessKind, 0xD8, destinationRegister);
            return (1, checked((ushort)selected));
        }

        ushort AddArrayElementSelection(PrimitiveType arrayType,
            PrimitiveType elementType, BoundExpression index,
            int variable, BoundExpression? fieldPlace, byte accessKind,
            byte baseRegister, byte destinationRegister,
            out byte placeRegister)
        {
            var (BaseKind, BaseIndex) = GetArrayBase(variable, fieldPlace,
                accessKind, baseRegister);
            var indexHome = CaptureIndexHome(index);
            return AddArraySelection(arrayType, elementType,
                BaseKind, BaseIndex, accessKind, 2,
                checked((ushort)indexHome), destinationRegister,
                out placeRegister);
        }

        ushort AddArraySelection(PrimitiveType arrayType,
            PrimitiveType elementType, byte baseKind, ushort baseIndex,
            byte accessKind, byte indexKind, ushort indexIndex,
            byte destinationRegister, out byte placeRegister)
        {
            var layoutRow = GetArrayLayoutRow(arrayType);
            placeRegister = baseKind == 1
                    && destinationRegister == baseIndex
                ? destinationRegister == 1 ? (byte)2 : (byte)1
                : destinationRegister;
            var selectionIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                    targetKind: 3, accessKind: accessKind,
                    destinationKind: 1, baseKind: baseKind,
                    destinationIndex: placeRegister,
                    baseIndex: baseIndex,
                    indexKind: indexKind,
                    targetRefKind: 3,
                    indexIndex: indexIndex,
                    targetRef: checked((uint)layoutRow),
                    referentTypeMapIndex: maps.GetTypeMapIndex(
                        TypeToken(elementType)),
                    TrRmArtifactSchema.LocalAggregateLayoutDraft));
            return selectionIndex;
        }

        (byte BaseKind, ushort BaseIndex, PrimitiveType Type) GetPlaceRoot(
            BoundExpression expression, byte accessKind,
            byte destinationRegister)
        {
            switch (expression)
            {
                case BoundRead read:
                    return (2,
                        checked((ushort)HomeForVariable(read.Variable)),
                        read.Type);
                case BoundNativeFixedArrayElement element:
                {
                    var baseRegister = destinationRegister == 1
                        ? (byte)2 : (byte)1;
                    var selectionIndex = AddArrayElementSelection(
                        element.Layout.CarrierType,
                        element.Layout.ElementType, element.Index,
                        element.Variable, element.FieldPlace, accessKind,
                        baseRegister, destinationRegister,
                        out var placeRegister);
                    AppendPlaceSelection(selectionIndex, placeRegister);
                    return (1, placeRegister, element.Type);
                }
                case BoundNativeFixedArrayPathElement element:
                {
                    if (element.Projections.IsDefaultOrEmpty)
                    {
                        throw Unsupported(
                            "L1 nested-array field roots require a captured dimension.");
                    }

                    var baseRegister = destinationRegister == 1
                        ? (byte)2 : (byte)1;
                    var (BaseKind, BaseIndex) = GetArrayBase(element.Variable,
                        element.FieldPlace, accessKind, baseRegister);
                    var baseKind = BaseKind;
                    var baseIndex = BaseIndex;
                    var selectedType = element.FieldPlace?.Type
                        ?? function.Variables[element.Variable].Type;
                    for (var depth = 0;
                        depth < element.Projections.Length; depth++)
                    {
                        var projection = element.Projections[depth];
                        if (projection.Layout.CarrierType != selectedType)
                        {
                            throw Unsupported(
                                "L1 nested-array field roots have mismatched dimensions.");
                        }

                        var indexHome = CaptureIndexHome(projection.Index);
                        var requestedRegister = (depth
                                == (element.Projections.Length - 1))
                            ? destinationRegister
                            : checked((byte)((depth % 2) + 1));
                        var selectionIndex = AddArraySelection(
                            projection.Layout.CarrierType,
                            projection.Layout.ElementType, baseKind,
                            baseIndex, accessKind, 2,
                            checked((ushort)indexHome), requestedRegister,
                            out var placeRegister);
                        AppendPlaceSelection(selectionIndex, placeRegister);
                        baseKind = 1;
                        baseIndex = placeRegister;
                        selectedType = projection.Layout.ElementType;
                    }

                    return (baseKind, baseIndex, element.Type);
                }
                case BoundCall call
                    when call.Type != PrimitiveType.Void
                        && !DirectNativeImportContract.IsScalar(call.Type):
                {
                    var resultHome = TemporaryHomeFor(call);
                    AppendCall(call, 2, checked((ushort)resultHome));
                    return (2, checked((ushort)resultHome), call.Type);
                }
                case BoundNativeFixedArrayInitialization
                    or BoundNativeFixedArrayRepeatInitialization:
                {
                    var home = CaptureValueHome(expression);
                    return (2, checked((ushort)home), expression.Type);
                }
                default:
                    throw Unsupported(
                        "An L1 field or array path has no captured place root.");
            }
        }
        void AppendArrayElementRead(BoundNativeFixedArrayElement element,
            byte destination)
        {
            const byte baseRegister = 12;
            var selectionIndex = AddArrayElementSelection(
                element.Layout.CarrierType, element.Layout.ElementType,
                element.Index, element.Variable, element.FieldPlace, 1,
                baseRegister, destination, out var readRegister);
            AppendPlaceLoad(selectionIndex, readRegister);
            if (readRegister != destination)
            {
                code.Add(0x10);
                code.Add((byte)((readRegister << 4) | destination));
            }
        }

        void AppendArrayPathRead(BoundNativeFixedArrayPathElement element,
            byte destination)
        {
            if (element.Projections.IsDefaultOrEmpty)
            {
                throw Unsupported(
                    "L1 nested-array reads require at least one captured dimension.");
            }

            var (BaseKind, BaseIndex) = GetArrayBase(element.Variable,
                element.FieldPlace, 1, 12);
            var baseKind = BaseKind;
            var baseIndex = BaseIndex;
            var selectedType = element.FieldPlace?.Type
                ?? function.Variables[element.Variable].Type;
            for (var depth = 0; depth < element.Projections.Length; depth++)
            {
                var projection = element.Projections[depth];
                if (projection.Layout.CarrierType != selectedType)
                {
                    throw Unsupported(
                        "L1 nested-array read projections do not match their source type.");
                }

                var indexHome = CaptureIndexHome(projection.Index);
                var isFinal = depth
                    == (element.Projections.Length - 1);
                var requestedRegister = isFinal
                    ? destination
                    : baseKind == 1 && baseIndex == 12
                        ? (byte)13 : (byte)12;
                var selectionIndex = AddArraySelection(
                    projection.Layout.CarrierType,
                    projection.Layout.ElementType,
                    baseKind, baseIndex, 1, 2,
                    checked((ushort)indexHome), requestedRegister,
                    out var placeRegister);
                if (isFinal)
                {
                    AppendPlaceLoad(selectionIndex, placeRegister);
                    if (placeRegister != destination)
                    {
                        code.Add(0x10);
                        code.Add((byte)((placeRegister << 4) | destination));
                    }
                }
                else
                {
                    AppendPlaceSelection(selectionIndex, placeRegister);
                    baseKind = 1;
                    baseIndex = placeRegister;
                    selectedType = projection.Layout.ElementType;
                }
            }
        }

        void AppendArrayElementWrite(BoundNativeFixedArrayWrite write)
        {
            const byte storePlaceRegister = 13;
            const byte basePlaceRegister = 12;
            var isInitialization = write.Variable
                    == function.ConstructionHomeIndex
                || function.Variables[write.Variable]
                    .IsInitializationLocal;
            var accessKind = isInitialization ? (byte)3 : (byte)2;
            var homeIndex = HomeForVariable(write.Variable);
            var selectionIndex = AddArrayElementSelection(
                write.Layout.CarrierType, write.Layout.ElementType,
                write.Index, write.Variable, write.FieldPlace, accessKind,
                basePlaceRegister, storePlaceRegister,
                out var placeRegister);
            AppendPlaceSelection(selectionIndex, placeRegister);
            var (SourceKind, SourceIndex) = GetStoreSource(write.Value);
            if (isInitialization)
            {
                if (SourceKind == 0)
                {
                    AppendUnitSemanticStore(placeRegister, 1);
                    MarkHomeMaybeLive(homeIndex);
                    return;
                }

                var storeSourceKind = SourceKind;
                var storeSourceIndex = SourceIndex;
                if (storeSourceKind == 2
                    && !NativeFixedArrayTypes.IsType(
                        write.Layout.ElementType))
                {
                    AppendHomeLoad(code, 0, storeSourceIndex);
                    storeSourceKind = 1;
                    storeSourceIndex = 0;
                }
                var storeIndex = checked((ushort)records.Count);
                records.Add(
                    TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                        1, storeSourceKind, 1, 1, placeRegister,
                        storeSourceIndex,
                        TrRmArtifactSchema.LocalAggregateLayoutDraft));
                AppendSemanticStore(storeIndex);
                MarkHomeMaybeLive(homeIndex);
                return;
            }

            if (SourceKind == 0)
            {
                AppendUnitSemanticStore(placeRegister, 2);
                return;
            }

            var replacementStoreIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                    1, SourceKind, 2, 1, placeRegister,
                    SourceIndex,
                    TrRmArtifactSchema.LocalAggregateLayoutDraft));
            AppendSemanticStore(replacementStoreIndex);
        }

        void AppendArrayPathWrite(BoundNativeFixedArrayPathWrite write)
        {
            const byte storePlaceRegister = 13;
            const byte basePlaceRegister = 12;
            if (write.Projections.IsDefaultOrEmpty)
            {
                throw Unsupported(
                    "L1 nested-array writes require at least one captured dimension.");
            }

            var isInitialization = write.Variable
                    == function.ConstructionHomeIndex
                || function.Variables[write.Variable]
                    .IsInitializationLocal;
            var accessKind = isInitialization ? (byte)3 : (byte)2;
            var homeIndex = HomeForVariable(write.Variable);
            var (BaseKind, BaseIndex) = GetArrayBase(write.Variable, write.FieldPlace,
                accessKind, basePlaceRegister);
            var baseKind = BaseKind;
            var baseIndex = BaseIndex;
            var selectedType = write.FieldPlace?.Type
                ?? function.Variables[write.Variable].Type;
            var previousRegister = -1;
            var lastArrayElementType = PrimitiveType.Void;
            for (var depth = 0; depth < write.Projections.Length; depth++)
            {
                var projection = write.Projections[depth];
                if (projection.Layout.CarrierType != selectedType)
                {
                    throw Unsupported(
                        "L1 nested-array write projections do not match their source type.");
                }

                var indexHome = CaptureIndexHome(projection.Index);
                var isFinal = depth
                    == (write.Projections.Length - 1);
                var destination = isFinal
                    ? baseKind == 1 && baseIndex == storePlaceRegister
                        ? basePlaceRegister : storePlaceRegister
                    : baseKind == 1 && baseIndex == basePlaceRegister
                        ? storePlaceRegister : basePlaceRegister;
                var selectionIndex = AddArraySelection(
                    projection.Layout.CarrierType,
                    projection.Layout.ElementType,
                    baseKind, baseIndex, accessKind, 2,
                    checked((ushort)indexHome), destination,
                    out destination);
                AppendPlaceSelection(selectionIndex, destination);
                previousRegister = destination;
                selectedType = projection.Layout.ElementType;
                lastArrayElementType = selectedType;
                baseKind = 1;
                baseIndex = destination;
            }

            var targetType = lastArrayElementType;
            for (var depth = 0; depth < write.LeafFields.Length; depth++)
            {
                var fieldProjection = write.LeafFields[depth];
                if (fieldProjection.OwnerType != targetType)
                {
                    throw Unsupported(
                        "L1 nested-array leaf fields do not match their selected aggregate.");
                }

                var fieldRow = FieldRow(targetType, fieldProjection.Field,
                    out var fieldType);
                var isFinal = depth == write.LeafFields.Length - 1;
                var destination = isFinal
                    ? previousRegister == storePlaceRegister
                        ? basePlaceRegister : storePlaceRegister
                    : previousRegister == 1 ? (byte)2 : (byte)1;
                var selectionIndex = checked((ushort)records.Count);
                records.Add(
                    TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                        targetKind: 2, accessKind: accessKind,
                        destinationKind: 1, baseKind: 1,
                        destinationIndex: destination,
                        baseIndex: checked((ushort)previousRegister),
                        indexKind: 0, targetRefKind: 1, indexIndex: 0,
                        targetRef: checked((uint)fieldRow),
                        referentTypeMapIndex: maps.GetTypeMapIndex(
                            TypeToken(fieldType)),
                        TrRmArtifactSchema.LocalAggregateLayoutDraft));
                AppendPlaceSelection(selectionIndex, destination);
                previousRegister = destination;
                targetType = fieldType;
            }

            pendingPlaceRegisterState
                = (previousRegister, targetType, accessKind);
            var (SourceKind, SourceIndex) = GetStoreSource(write.Value);
            pendingPlaceRegisterState = null;
            if (isInitialization)
            {
                if (SourceKind == 0)
                {
                    AppendUnitSemanticStore(checked((byte)previousRegister),
                        1);
                    MarkHomeMaybeLive(homeIndex);
                    return;
                }

                var sourceKind = SourceKind;
                var sourceIndex = SourceIndex;
                if (sourceKind == 2
                    && !NativeFixedArrayTypes.IsType(targetType))
                {
                    AppendHomeLoad(code, 0, sourceIndex);
                    sourceKind = 1;
                    sourceIndex = 0;
                }
                var storeIndex = checked((ushort)records.Count);
                records.Add(
                    TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                        1, sourceKind, 1, 1,
                        checked((ushort)previousRegister), sourceIndex,
                        TrRmArtifactSchema.LocalAggregateLayoutDraft));
                AppendSemanticStore(storeIndex);
                MarkHomeMaybeLive(homeIndex);
                return;
            }

            if (SourceKind == 0)
            {
                AppendUnitSemanticStore(checked((byte)previousRegister), 2);
                return;
            }
            var finalStoreIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                    1, SourceKind, 2, 1,
                    checked((ushort)previousRegister), SourceIndex,
                    TrRmArtifactSchema.LocalAggregateLayoutDraft));
            AppendSemanticStore(finalStoreIndex);
        }

        void AppendRepeatInitialization(int homeIndex,
            BoundNativeFixedArrayRepeatInitialization initialization)
        {
            RequireVacantHomeInitialization(homeIndex);
            var seedHome = CaptureValueHome(initialization.Seed);
            var selectionIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                    1, 3, 1, 0, 1, 0, 0, 0, 0,
                    checked((uint)homeIndex),
                    maps.GetTypeMapIndex(TypeToken(initialization.Type)),
                    TrRmArtifactSchema.LocalAggregateLayoutDraft));
            AppendPlaceSelection(selectionIndex, 1);
            var storeIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                    1,
                    initialization.Seed.Type == PrimitiveType.Void
                        ? (byte)1 : (byte)2,
                    1, 6, 1,
                    initialization.Seed.Type == PrimitiveType.Void
                        ? (ushort)14 : checked((ushort)seedHome),
                    TrRmArtifactSchema.LocalAggregateLayoutDraft));
            AppendSemanticStore(storeIndex);
            MarkHomeLive(homeIndex);
        }

        void AppendCall(BoundCall call, byte resultKind,
            ushort resultIndex)
        {
            if ((uint)call.Function >= (uint)program.Functions.Length)
            {
                throw Unsupported(
                    "An L1 CallSite target is outside the root MethodDef table.");
            }

            var target = program.Functions[call.Function];
            var methodToken = methodMapTokenByFunction[call.Function];
            if (target.DirectNativeImport is not null
                || call.Arguments.Length != target.ParameterCount
                || (!call.ArgumentParameterIndices.IsDefault
                    && call.ArgumentParameterIndices.Length
                        != call.Arguments.Length))
            {
                throw Unsupported(
                    "An L1 CallSite does not match its selected MethodDef signature.");
            }
            if (importedMethodFunctions[call.Function]
                ? !target.IsExternal || !target.IsModuleReference
                    || target.ModuleImport is null
                    || methodToken >> 24 != 0x1C
                : target.IsExternal || target.ModuleImport is not null
                    || methodToken >> 24 != 0x16)
            {
                throw Unsupported(
                    "An L1 CallSite target has no selected local MethodDef or exact MethodDef Import token.");
            }
            if (resultKind == 2)
            {
                RequireVacantCallResultHome(resultIndex);
            }

            var inputs = new TrRmRegisterBodyCallInput[
                call.Arguments.Length];
            var assignedFormals = new bool[call.Arguments.Length];
            for (var argumentIndex = 0;
                argumentIndex < call.Arguments.Length; argumentIndex++)
            {
                var argument = call.Arguments[argumentIndex];
                var formalIndex = call.ArgumentParameterIndices.IsDefault
                    ? argumentIndex
                    : call.ArgumentParameterIndices[argumentIndex];
                if ((uint)formalIndex >= (uint)inputs.Length
                    || assignedFormals[formalIndex]
                    || argument.Type != target.Variables[formalIndex].Type)
                {
                    throw Unsupported(
                        "An L1 CallSite input differs from its selected formal TypeSpec.");
                }
                assignedFormals[formalIndex] = true;

                if (argument.Type == PrimitiveType.Void)
                {
                    AppendUnitExpression(argument);
                    inputs[formalIndex] = new TrRmRegisterBodyCallInput(
                        0, 0, 0);
                    continue;
                }

                if (!temporaryHomeByExpression.TryGetValue(argument,
                        out var argumentHome))
                {
                    throw Unsupported(
                        "L1 CallSite arguments require a typed snapshot Home.");
                }

                if (argument is BoundCall nestedCall
                    && nestedCall.Type != PrimitiveType.Void)
                {
                    AppendCall(nestedCall, 2,
                        checked((ushort)argumentHome));
                }
                else if (argument
                    is BoundNativeFixedArrayInitialization arrayInit)
                {
                    CaptureArrayInitializerOperands(arrayInit);
                    BuildArrayElementsFromCaptured(arrayInit,
                        argumentHome);
                }
                else if (argument
                    is BoundNativeFixedArrayRepeatInitialization repeat)
                {
                    AppendRepeatInitialization(argumentHome, repeat);
                }
                else
                {
                    AppendValue(argument, 0);
                    AppendHomeStoreToCurrentBlock(0, argumentHome);
                }

                inputs[formalIndex] = new TrRmRegisterBodyCallInput(2, 1,
                    checked((ushort)argumentHome));
            }
            if (assignedFormals.Any(static assigned => !assigned))
            {
                throw Unsupported(
                    "An L1 CallSite does not provide every formal exactly once.");
            }

            var callSite = checked((ushort)records.Count);
            records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
                1, resultKind, maps.GetMethodMapIndex(methodToken),
                resultIndex, inputs, []));
            AppendCallSiteInstruction(code, callSite);
            if (resultKind == 2)
            {
                MarkHomeLive(resultIndex);
            }
        }

        void AppendC0(byte operation, byte destination, byte left,
            byte right)
        {
            var payload = (ushort)(destination | (0x0F << 4)
                | (left << 8) | (right << 12));
            code.Add(0xC0);
            code.Add(operation);
            code.Add(unchecked((byte)payload));
            code.Add((byte)(payload >> 8));
        }

        void AppendBinary(BoundBinary binary, byte destination)
        {
            if (binary.Operator is TokenKind.AmpersandAmpersand
                or TokenKind.PipePipe)
            {
                throw Unsupported(
                    "L1 computed values do not eagerly lower short-circuit Boolean operators.");
            }

            var leftHome = temporaryHomeByExpression[binary.Left];
            AppendValue(binary.Left, 0);
            AppendHomeStoreToCurrentBlock(0, leftHome);
            var rightRegister = destination == 0 ? (byte)1 : (byte)0;
            AppendValue(binary.Right, rightRegister);
            AppendHomeLoad(code, destination, leftHome);

            if (binary.Operator is TokenKind.Plus or TokenKind.Minus
                or TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
            {
                byte operation;
                if (binary.Operator is TokenKind.Slash or TokenKind.Percent)
                {
                    if (!IsInteger(binary.Type))
                    {
                        throw Unsupported(
                            "L1 register arithmetic supports integer division and remainder only.");
                    }

                    operation = binary.Operator == TokenKind.Slash
                        ? (byte)0x0C : (byte)0x0D;
                }
                else if (IsFloat(binary.Type))
                {
                    operation = binary.Operator switch {
                        TokenKind.Plus => 0x20,
                        TokenKind.Minus => 0x21,
                        TokenKind.Star => 0x22,
                        _ => throw Unsupported(
                            "L1 floating Body operation is outside the strict arithmetic set."),
                    };
                }
                else if (IsInteger(binary.Type))
                {
                    operation = (binary.Operator,
                        binary.OverflowBehavior) switch {
                        (TokenKind.Plus,
                            BoundIntegerOverflowBehavior.Checked) => 0x01,
                        (TokenKind.Minus,
                            BoundIntegerOverflowBehavior.Checked) => 0x02,
                        (TokenKind.Star,
                            BoundIntegerOverflowBehavior.Checked) => 0x03,
                        (TokenKind.Plus,
                            BoundIntegerOverflowBehavior.Wrapping) => 0x05,
                        (TokenKind.Minus,
                            BoundIntegerOverflowBehavior.Wrapping) => 0x06,
                        (TokenKind.Star,
                            BoundIntegerOverflowBehavior.Wrapping) => 0x07,
                        _ => throw Unsupported(
                            "L1 integer arithmetic has no selected C0 operation."),
                    };
                }
                else
                {
                    throw Unsupported(
                        "L1 arithmetic requires a registered integer or floating type.");
                }

                AppendC0(operation, destination, destination,
                    rightRegister);
                return;
            }

            if (binary.Operator is TokenKind.Ampersand or TokenKind.Pipe
                or TokenKind.Caret)
            {
                if (binary.Type == PrimitiveType.Bool)
                {
                    var operation = binary.Operator switch {
                        TokenKind.Ampersand => (byte)0x30,
                        TokenKind.Pipe => (byte)0x31,
                        _ => (byte)0x32,
                    };
                    code.Add(operation);
                    code.Add((byte)((rightRegister << 4) | destination));
                }
                else
                {
                    var operation = binary.Operator switch {
                        TokenKind.Ampersand => (byte)0x01,
                        TokenKind.Pipe => (byte)0x02,
                        _ => (byte)0x03,
                    };
                    code.Add(0xC1);
                    code.Add(operation);
                    var payload = (ushort)(destination | (0x0F << 4)
                        | (destination << 8) | (rightRegister << 12));
                    code.Add(unchecked((byte)payload));
                    code.Add((byte)(payload >> 8));
                }
                return;
            }

            if (binary.Operator is TokenKind.EqualsEquals
                or TokenKind.BangEquals or TokenKind.Less
                or TokenKind.LessEquals or TokenKind.Greater
                or TokenKind.GreaterEquals)
            {
                if (binary.Left.Type == PrimitiveType.Bool)
                {
                    if (binary.Operator is not (TokenKind.EqualsEquals
                        or TokenKind.BangEquals))
                    {
                        throw Unsupported(
                            "L1 Boolean comparison supports equality and inequality only.");
                    }

                    var payload = (ushort)(destination | (0x0F << 4)
                        | (destination << 8) | (rightRegister << 12));
                    code.Add(0xC2);
                    code.Add(binary.Operator == TokenKind.EqualsEquals
                        ? (byte)1 : (byte)2);
                    code.Add(unchecked((byte)payload));
                    code.Add((byte)(payload >> 8));
                    return;
                }

                var relationPayload = (ushort)(destination | (0x0F << 4)
                    | (destination << 8) | (rightRegister << 12));
                code.Add(0xC2);
                code.Add(0);
                code.Add(unchecked((byte)relationPayload));
                code.Add((byte)(relationPayload >> 8));
                var predicate = binary.Operator switch {
                    TokenKind.EqualsEquals => 2,
                    TokenKind.BangEquals => 13,
                    TokenKind.Less => 1,
                    TokenKind.LessEquals => 3,
                    TokenKind.Greater => 4,
                    TokenKind.GreaterEquals => 6,
                    _ => throw Unsupported(
                        "L1 comparison has no registered relation predicate."),
                };
                code.Add(0xC3);
                code.Add(checked((byte)predicate));
                var setPayload = (ushort)(destination | (destination << 4)
                    | (0x0F << 8) | (0x0F << 12));
                code.Add(unchecked((byte)setPayload));
                code.Add((byte)(setPayload >> 8));
                return;
            }

            throw Unsupported(
                $"L1 binary operation {binary.Operator} has no selected Body form.");
        }

        void AppendValue(BoundExpression expression, byte destination)
        {
            switch (expression)
            {
                case BoundRead read:
                    AppendHomeLoad(code, destination,
                        HomeForVariable(read.Variable));
                    return;
                case BoundUnsafeExpression unsafeExpression:
                    AppendValue(unsafeExpression.Expression, destination);
                    return;
                case BoundCast cast:
                    AppendValue(cast.Operand, destination);
                    if (cast.Operand.Type == cast.Type)
                    {
                        return;
                    }

                    var targetTypeMapIndex = maps.GetTypeMapIndex(
                        TypeToken(cast.Type));
                    code.Add(0xD0);
                    code.Add((byte)(destination | (destination << 4)));
                    code.Add((byte)targetTypeMapIndex);
                    code.Add((byte)(targetTypeMapIndex >> 8));
                    return;
                case BoundConstant constant:
                    AppendLoadConstant(constant, destination);
                    return;
                case BoundUnary unary when unary.Operator == TokenKind.Bang
                    && unary.Type == PrimitiveType.Bool:
                    AppendValue(unary.Operand, destination);
                    code.Add(0x33);
                    code.Add((byte)((destination << 4) | destination));
                    return;
                case BoundUnary unary when unary.Operator == TokenKind.Minus
                    && IsInteger(unary.Type):
                    AppendValue(unary.Operand, destination);
                    AppendC0(unary.OverflowBehavior switch {
                        BoundIntegerOverflowBehavior.Checked => 0x04,
                        BoundIntegerOverflowBehavior.Wrapping => 0x08,
                        _ => throw Unsupported(
                            "L1 integer negate does not have a saturating form."),
                    }, destination, destination, 15);
                    return;
                case BoundUnary unary when unary.Operator == TokenKind.Minus
                    && IsFloat(unary.Type):
                    AppendValue(unary.Operand, destination);
                    AppendC0(0x25, destination, destination, 15);
                    return;
                case BoundUpdate update:
                    var updateResultHome = update.IsPrefix
                        ? -1 : TemporaryHomeFor(update);
                    AppendUpdate(update, destination, updateResultHome);
                    if (updateResultHome >= 0)
                    {
                        AppendHomeLoad(code, destination, updateResultHome);
                    }
                    return;
                case BoundBinary binary:
                    AppendBinary(binary, destination);
                    return;
                case BoundConditional conditional
                    when conditional.Type != PrimitiveType.Void:
                    AppendConditionalValue(conditional, destination);
                    return;
                case BoundFieldRead fieldRead:
                    AppendFieldRead(fieldRead);
                    if (destination != 0)
                    {
                        code.Add(0x10);
                        code.Add(destination);
                    }
                    return;
                case BoundNativeFixedArrayElement arrayElement:
                    AppendArrayElementRead(arrayElement, destination);
                    return;
                case BoundNativeFixedArrayPathElement arrayPathElement:
                    AppendArrayPathRead(arrayPathElement, destination);
                    return;
                case BoundCall call when DirectNativeImportContract.IsScalar(
                    call.Type):
                    AppendCall(call, 1, destination);
                    return;
                case BoundCall call:
                    if (!temporaryHomeByExpression.TryGetValue(call,
                            out var resultHome))
                    {
                        throw Unsupported(
                            "An L1 aggregate call expression has no typed result Home.");
                    }

                    AppendCall(call, 2, checked((ushort)resultHome));
                    AppendHomeLoad(code, destination, resultHome);
                    return;
                case BoundNativeFixedArrayInitialization
                    or BoundNativeFixedArrayRepeatInitialization:
                    AppendHomeLoad(code, destination,
                        CaptureValueHome(expression));
                    return;
                default:
                    throw Unsupported(
                        "L1 Body expression is outside the local Home/field/array/call subset.");
            }
        }

        void AppendUpdate(BoundUpdate update, byte destination,
            int resultHome = -1)
        {
            if (destination >= 14
                || update.Place is not BoundRead read
                || (uint)read.Variable >= (uint)function.Variables.Length
                || update.Type != read.Type
                || !NativeFixedArrayContract.IsIndexType(update.Type)
                || !function.Variables[read.Variable].IsMutable
                || update.Operator is not (TokenKind.PlusPlus
                    or TokenKind.MinusMinus))
            {
                throw Unsupported(
                    "L1 Body updates require a writable local fixed-array index.");
            }

            var sourceHome = HomeForVariable(read.Variable);
            AppendHomeLoad(code, destination, sourceHome);
            var valueRegister = destination;
            if (update.IsPrefix)
            {
                var oneRegister = valueRegister == 3 ? (byte)4 : (byte)3;
                AppendLoadConstant(
                    new BoundConstant(1, update.Type, update.Span),
                    oneRegister);
                var operation = GetUpdateOperation(update);
                AppendC0(operation, valueRegister, valueRegister,
                    oneRegister);
                AppendHomeStoreToCurrentBlock(valueRegister, sourceHome);
                if (resultHome >= 0)
                {
                    AppendHomeStoreToCurrentBlock(valueRegister, resultHome);
                }
                return;
            }

            if (resultHome >= 0)
            {
                AppendHomeStoreToCurrentBlock(destination, resultHome);
            }

            valueRegister = destination == 3 ? (byte)4 : (byte)3;
            code.Add(0x10);
            code.Add((byte)((destination << 4) | valueRegister));
            var oneRegisterForPostfix = valueRegister == 3
                ? (byte)4 : (byte)3;
            AppendLoadConstant(
                new BoundConstant(1, update.Type, update.Span),
                oneRegisterForPostfix);
            var postfixOperation = GetUpdateOperation(update);
            AppendC0(postfixOperation, valueRegister, valueRegister,
                oneRegisterForPostfix);
            AppendHomeStoreToCurrentBlock(valueRegister, sourceHome);
        }

        static byte GetUpdateOperation(BoundUpdate update)
        {
            return (update.Operator, update.OverflowBehavior) switch
            {
                (TokenKind.PlusPlus, BoundIntegerOverflowBehavior.Checked)
                    => 0x01,
                (TokenKind.MinusMinus, BoundIntegerOverflowBehavior.Checked)
                    => 0x02,
                (TokenKind.PlusPlus, BoundIntegerOverflowBehavior.Wrapping)
                    => 0x05,
                (TokenKind.MinusMinus, BoundIntegerOverflowBehavior.Wrapping)
                    => 0x06,
                _ => throw Unsupported(
                    "L1 Body updates have no selected overflow operation."),
            };
        }

        int CaptureValueHome(BoundExpression expression)
        {
            if (expression.Type == PrimitiveType.Void)
            {
                return -1;
            }

            if (expression is BoundNativeFixedArrayInitialization init)
            {
                return CaptureArrayValueHome(init);
            }
            if (expression
                is BoundNativeFixedArrayRepeatInitialization repeat)
            {
                var repeatHome = TemporaryHomeFor(repeat);
                var seedHome = CaptureValueHome(repeat.Seed);
                BuildRepeatArrayFromCaptured(repeat, repeatHome, seedHome);
                return repeatHome;
            }

            var home = TemporaryHomeFor(expression);
            if (expression is BoundCall call
                && call.Type != PrimitiveType.Void
                && !DirectNativeImportContract.IsScalar(call.Type))
            {
                AppendCall(call, 2, checked((ushort)home));
            }
            else
            {
                AppendValue(expression, 0);
                AppendHomeStoreToCurrentBlock(0, home);
            }

            return home;
        }

        void CaptureArrayInitializerOperands(
            BoundNativeFixedArrayInitialization initialization)
        {
            foreach (var element in initialization.Elements)
            {
                switch (element)
                {
                    case BoundNativeFixedArrayInitialization nested:
                        CaptureArrayInitializerOperands(nested);
                        break;
                    case BoundNativeFixedArrayRepeatInitialization repeat:
                        if (repeat.Seed.Type != PrimitiveType.Void)
                        {
                            _ = CaptureValueHome(repeat.Seed);
                        }
                        break;
                    default:
                        if (element.Type != PrimitiveType.Void)
                        {
                            _ = CaptureValueHome(element);
                        }
                        break;
                }
            }
        }

        void AppendEmptyArrayHome(int homeIndex, PrimitiveType arrayType)
        {
            RequireVacantHomeInitialization(homeIndex);
            var selectionIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                    1, 3, 1, 0, 1, 0, 0, 0, 0,
                    checked((uint)homeIndex),
                    maps.GetTypeMapIndex(TypeToken(arrayType)),
                    TrRmArtifactSchema.LocalAggregateLayoutDraft));
            AppendPlaceSelection(selectionIndex, 1);
            var storeIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodeEmptyArrayInitialization(
                    1, TrRmArtifactSchema.LocalAggregateLayoutDraft));
            AppendNullaryArrayStoreInstruction(code, storeIndex);
            MarkHomeLive(homeIndex);
        }

        PrimitiveType GetHomeType(int homeIndex)
        {
            if (homeIndex < 0)
            {
                throw Unsupported("An L1 value Home index is invalid.");
            }
            if (homeIndex < localCount)
            {
                return function.Variables[
                    function.ParameterCount + homeIndex].Type;
            }
            if (homeIndex < formalHomeStart)
            {
                return temporaryTypes[homeIndex - localCount];
            }
            var parameterIndex = homeIndex - formalHomeStart;
            if ((uint)parameterIndex < (uint)function.ParameterCount)
            {
                return function.Variables[parameterIndex].Type;
            }

            throw Unsupported("An L1 value Home index is invalid.");
        }

        void BuildRepeatArrayFromCaptured(
            BoundNativeFixedArrayRepeatInitialization initialization,
            int homeIndex, int seedHome)
        {
            RequireVacantHomeInitialization(homeIndex);
            if (!NativeFixedArrayContract.TryGetLayout(
                    initialization.Type, program.Structures,
                    program.NativeFixedArrayTypes, out var layout)
                || !NativeFixedArrayContract.MatchesType(
                    initialization.Type, layout, program)
                || !NativeFixedArrayContract.MatchesLayout(layout, program)
                || initialization.Layout != layout
                || initialization.Seed.Type != layout.ElementType)
            {
                throw Unsupported(
                    "L1 RepeatCopy requires the exact selected array layout and seed element type.");
            }
            if (initialization.Seed.Type != PrimitiveType.Void
                && GetHomeType(seedHome) != initialization.Seed.Type)
            {
                throw Unsupported(
                    "L1 RepeatCopy seed Home does not preserve the exact element type.");
            }

            var selectionIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                    1, 3, 1, 0, 1, 0, 0, 0, 0,
                    checked((uint)homeIndex),
                    maps.GetTypeMapIndex(TypeToken(initialization.Type)),
                    TrRmArtifactSchema.LocalAggregateLayoutDraft));
            AppendPlaceSelection(selectionIndex, 1);
            var storeIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                    1, initialization.Seed.Type == PrimitiveType.Void
                        ? (byte)1 : (byte)2,
                    1, 6, 1,
                    initialization.Seed.Type == PrimitiveType.Void
                        ? (ushort)14 : checked((ushort)seedHome),
                    TrRmArtifactSchema.LocalAggregateLayoutDraft));
            AppendSemanticStore(storeIndex);
            MarkHomeLive(homeIndex);
        }

        void BuildArrayElementsFromCaptured(
            BoundNativeFixedArrayInitialization initialization,
            int homeIndex)
        {
            RequireVacantHomeInitialization(homeIndex);
            var arrayType = initialization.Type;
            if (!NativeFixedArrayContract.TryGetLayout(arrayType,
                    program.Structures, program.NativeFixedArrayTypes,
                    out var layout)
                || !NativeFixedArrayContract.MatchesType(arrayType, layout,
                    program)
                || !NativeFixedArrayContract.MatchesLayout(layout, program)
                || initialization.Layout != layout
                || (ulong)initialization.Elements.Length
                    != layout.ElementCount)
            {
                throw Unsupported(
                    "L1 list initialization requires a complete exact fixed-array layout.");
            }

            if (layout.ElementCount == 0)
            {
                if (!initialization.Elements.IsEmpty)
                {
                    throw Unsupported(
                        "A zero-length L1 array must have an empty source list.");
                }

                AppendEmptyArrayHome(homeIndex, arrayType);
                return;
            }

            for (var elementIndex = 0;
                elementIndex < initialization.Elements.Length;
                elementIndex++)
            {
                var element = initialization.Elements[elementIndex];
                if (element is BoundNativeFixedArrayInitialization nested)
                {
                    BuildArrayElementsFromCaptured(nested,
                        TemporaryHomeFor(nested));
                }
                else if (element
                    is BoundNativeFixedArrayRepeatInitialization repeat)
                {
                    BuildRepeatArrayFromCaptured(repeat,
                        TemporaryHomeFor(repeat),
                        repeat.Seed.Type == PrimitiveType.Void
                            ? -1 : TemporaryHomeFor(repeat.Seed));
                }
            }

            for (var elementIndex = 0;
                elementIndex < initialization.Elements.Length;
                elementIndex++)
            {
                var element = initialization.Elements[elementIndex];
                var index = new BoundConstant(elementIndex,
                    PrimitiveType.UInt64, initialization.Span);
                AppendLoadConstant(index, 13);
                var placeRegister = (byte)1;
                var selectionIndex = AddArraySelection(arrayType,
                    layout.ElementType, 2, checked((ushort)homeIndex), 3,
                    1, 13, placeRegister, out _);
                AppendPlaceSelection(selectionIndex, placeRegister);

                var sourceHome = element.Type == PrimitiveType.Void
                    ? -1 : TemporaryHomeFor(element);
                if (element.Type == PrimitiveType.Void)
                {
                    AppendUnitSemanticStore(placeRegister, transition: 1);
                }
                else if (!NativeFixedArrayTypes.IsType(element.Type))
                {
                    AppendHomeLoad(code, 0, sourceHome);
                    var storeIndex
                        = checked((ushort)records.Count);
                    records.Add(
                        TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                            1, 1, 1, 1, placeRegister, 0,
                            TrRmArtifactSchema.LocalAggregateLayoutDraft));
                    AppendSemanticStore(storeIndex);
                }
                else if (NativeFixedArrayTypes.IsType(element.Type))
                {
                    var storeIndex
                        = checked((ushort)records.Count);
                    records.Add(
                        TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                            1, 2, 1, 1, placeRegister,
                            checked((ushort)sourceHome),
                            TrRmArtifactSchema.LocalAggregateLayoutDraft));
                    AppendSemanticStore(storeIndex);
                }
            }
            MarkHomeLive(homeIndex);
        }

        int CaptureArrayValueHome(
            BoundNativeFixedArrayInitialization initialization)
        {
            var home = TemporaryHomeFor(initialization);
            CaptureArrayInitializerOperands(initialization);
            BuildArrayElementsFromCaptured(initialization, home);
            return home;
        }

        void AppendUnitSemanticStore(byte placeRegister, byte transition)
        {
            if (transition != 1)
            {
                throw Unsupported(
                    "L1 unit field values support initialization only.");
            }

            var record = new byte[20];
            record[0] = 0x05;
            record[4] = 1;
            record[5] = 0;
            record[6] = 1;
            record[7] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8),
                placeRegister);
            var storeIndex = checked((ushort)records.Count);
            records.Add(record);
            AppendSemanticStore(storeIndex);
        }

        void AppendReturn(BoundReturn returnStatement)
        {
            if (returnStatement.Value is null)
            {
                code.Add(0x18);
                code.Add(0);
                return;
            }

            if (returnStatement.Value is BoundRead read)
            {
                var homeIndex = HomeForVariable(read.Variable);
                if (homeIndex > byte.MaxValue)
                {
                    throw Unsupported(
                        "L1 ReturnHome indexes are limited to one byte.");
                }
                code.Add(0xB8);
                code.Add(checked((byte)homeIndex));
                return;
            }

            if (returnStatement.Value is BoundCall call
                && call.Type != PrimitiveType.Void
                && !DirectNativeImportContract.IsScalar(call.Type))
            {
                if (!temporaryHomeByExpression.TryGetValue(call,
                        out var resultHome)
                    || resultHome > byte.MaxValue)
                {
                    throw Unsupported(
                        "An L1 aggregate CallSite return has no ReturnHome.");
                }

                AppendCall(call, 2, checked((ushort)resultHome));
                code.Add(0xB8);
                code.Add(checked((byte)resultHome));
                return;
            }
            if (!DirectNativeImportContract.IsScalar(
                    returnStatement.Value.Type))
            {
                throw Unsupported(
                    "L1 aggregate returns require an existing typed Home.");
            }
            AppendValue(returnStatement.Value, 0);
            code.Add(0x17);
            code.Add(0);
        }

        bool AlwaysTerminates(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundReturn:
                    return true;
                case BoundBlock block:
                    foreach (var nested in block.Statements)
                    {
                        if (AlwaysTerminates(nested))
                        {
                            return true;
                        }
                    }
                    return false;
                case BoundIf branch:
                    return branch.Else is not null
                        && AlwaysTerminates(branch.Then)
                        && AlwaysTerminates(branch.Else);
                default:
                    return false;
            }
        }

        void AppendConditionalFlow(BoundExpression condition,
            int trueBlock, int falseBlock)
        {
            if (condition.Type != PrimitiveType.Bool)
            {
                throw Unsupported(
                    "L1 ConditionalEdge requires a checked Boolean condition.");
            }

            switch (condition)
            {
                case BoundUnary {
                    Operator: TokenKind.Bang,
                    Operand: var operand,
                }:
                    AppendConditionalFlow(operand, falseBlock, trueBlock);
                    return;
                case BoundBinary {
                    Operator: TokenKind.AmpersandAmpersand,
                    Left: var left,
                    Right: var right,
                }:
                {
                    var rightBlock = CreateBlock();
                    AppendConditionalFlow(left, rightBlock, falseBlock);
                    SelectBlock(rightBlock);
                    AppendConditionalFlow(right, trueBlock, falseBlock);
                    return;
                }
                case BoundBinary {
                    Operator: TokenKind.PipePipe,
                    Left: var left,
                    Right: var right,
                }:
                {
                    var rightBlock = CreateBlock();
                    AppendConditionalFlow(left, trueBlock, rightBlock);
                    SelectBlock(rightBlock);
                    AppendConditionalFlow(right, trueBlock, falseBlock);
                    return;
                }
                case BoundConditional {
                    Type: PrimitiveType.Bool,
                    Condition: var nestedCondition,
                    WhenTrue: var whenTrue,
                    WhenFalse: var whenFalse,
                }:
                {
                    var trueValueBlock = CreateBlock();
                    var falseValueBlock = CreateBlock();
                    AppendConditionalFlow(nestedCondition,
                        trueValueBlock, falseValueBlock);
                    SelectBlock(trueValueBlock);
                    AppendConditionalFlow(whenTrue, trueBlock, falseBlock);
                    SelectBlock(falseValueBlock);
                    AppendConditionalFlow(whenFalse, trueBlock, falseBlock);
                    return;
                }
                default:
                    AppendValue(condition, 0);
                    SetBlockEntryHomes(trueBlock);
                    SetBlockEntryHomes(falseBlock);
                    var edgeIndex = checked((ushort)records.Count);
                    records.Add(
                        TrRmRegisterBodyRecordWriter.EncodeConditionalEdge(
                            checked((ushort)trueBlock),
                            checked((ushort)falseBlock)));
                    code.Add(0xE4);
                    code.Add(0);
                    code.Add(unchecked((byte)edgeIndex));
                    code.Add((byte)(edgeIndex >> 8));
                    return;
            }
        }

        bool AppendIf(BoundIf branch)
        {
            var thenTerminates = AlwaysTerminates(branch.Then);
            var elseTerminates = branch.Else is not null
                && AlwaysTerminates(branch.Else);
            var thenBlock = CreateBlock();
            var elseBlock = CreateBlock();
            var joinBlock = thenTerminates && elseTerminates
                ? -1 : CreateBlock();

            AppendConditionalFlow(branch.Condition, thenBlock, elseBlock);

            SelectBlock(thenBlock);
            var thenReturned = AppendStatement(branch.Then);
            if (thenReturned != thenTerminates)
            {
                throw Unsupported(
                    "L1 branch termination differs from its checked Body shape.");
            }
            if (!thenReturned)
            {
                AppendBranch(joinBlock);
            }

            SelectBlock(elseBlock);
            var elseReturned = branch.Else is not null
                && AppendStatement(branch.Else);
            if (elseReturned != elseTerminates)
            {
                throw Unsupported(
                    "L1 else-branch termination differs from its checked Body shape.");
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

        void AppendConditionalValue(BoundConditional conditional,
            byte destination)
        {
            if (conditional.WhenTrue.Type != conditional.Type
                || conditional.WhenFalse.Type != conditional.Type)
            {
                throw Unsupported(
                    "An L1 value Conditional has inconsistent arm types.");
            }

            var resultHome = TemporaryHomeFor(conditional);
            var trueBlock = CreateBlock();
            var falseBlock = CreateBlock();
            var joinBlock = CreateBlock();
            AppendConditionalFlow(conditional.Condition, trueBlock,
                falseBlock);

            SelectBlock(trueBlock);
            AppendValue(conditional.WhenTrue, 0);
            AppendHomeStoreToCurrentBlock(0, resultHome);
            AppendBranch(joinBlock);

            SelectBlock(falseBlock);
            AppendValue(conditional.WhenFalse, 0);
            AppendHomeStoreToCurrentBlock(0, resultHome);
            AppendBranch(joinBlock);

            SelectBlock(joinBlock);
            if (!currentMustLiveHomes.Contains(resultHome))
            {
                throw Unsupported(
                    "An L1 Conditional result Home is not initialized on every incoming path.");
            }
            AppendHomeLoad(code, destination, resultHome);
        }

        void AppendUnitExpression(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundCall call when call.Type == PrimitiveType.Void:
                    AppendCall(call, 0, 0);
                    return;
                case BoundConstant {
                    Type: PrimitiveType.Void,
                    Bits.Low: 0,
                    Bits.High: 0,
                }:
                    return;
                case BoundConditional {
                    Type: PrimitiveType.Void,
                } conditional:
                    var trueBlock = CreateBlock();
                    var falseBlock = CreateBlock();
                    var joinBlock = CreateBlock();
                    AppendConditionalFlow(conditional.Condition,
                        trueBlock, falseBlock);
                    SelectBlock(trueBlock);
                    AppendUnitExpression(conditional.WhenTrue);
                    AppendBranch(joinBlock);
                    SelectBlock(falseBlock);
                    AppendUnitExpression(conditional.WhenFalse);
                    AppendBranch(joinBlock);
                    SelectBlock(joinBlock);
                    return;
                default:
                    throw Unsupported(
                        "L1 unit Conditional expressions require local unit Calls or unit values.");
            }
        }

        bool AppendStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlock block:
                    return AppendStatements(block);
                case BoundUnsafe unsafeBlock:
                    return AppendStatements(unsafeBlock.Body);
                case BoundVacantLocal:
                case BoundLifetimeEnd { CleanupFunction: -1 }:
                    return false;
                case BoundLocal local:
                    var homeIndex = HomeForVariable(local.Variable);
                    switch (local.Initializer)
                    {
                        case BoundCall call:
                            if (DirectNativeImportContract.IsScalar(call.Type))
                            {
                                AppendCall(call, 1, 0);
                                AppendHomeStoreToCurrentBlock(0, homeIndex);
                            }
                            else
                            {
                                AppendCall(call, 2,
                                    checked((ushort)homeIndex));
                            }
                            break;
                        case BoundNativeFixedArrayInitialization arrayInit:
                            CaptureArrayInitializerOperands(arrayInit);
                            BuildArrayElementsFromCaptured(arrayInit,
                                homeIndex);
                            break;
                        case BoundNativeFixedArrayRepeatInitialization repeat:
                            AppendRepeatInitialization(homeIndex, repeat);
                            break;
                        default:
                            AppendValue(local.Initializer, 0);
                            AppendHomeStoreToCurrentBlock(0, homeIndex);
                            break;
                    }
                    return false;
                case BoundAssignment assignment:
                    AppendValue(assignment.Value, 0);
                    AppendHomeStoreToCurrentBlock(0,
                        HomeForVariable(assignment.Variable));
                    return false;
                case BoundFieldAssignment fieldAssignment:
                    AppendFieldAssignment(fieldAssignment);
                    return false;
                case BoundExpressionStatement {
                    Expression: BoundNativeFixedArrayWrite arrayWrite,
                }:
                    AppendArrayElementWrite(arrayWrite);
                    return false;
                case BoundExpressionStatement {
                    Expression: BoundNativeFixedArrayPathWrite pathWrite,
                }:
                    AppendArrayPathWrite(pathWrite);
                    return false;
                case BoundExpressionStatement {
                    Expression: BoundConditional {
                        Type: PrimitiveType.Void,
                    } conditional,
                }:
                    AppendUnitExpression(conditional);
                    return false;
                case BoundExpressionStatement {
                    Expression: BoundCall call,
                }:
                    if (call.Type == PrimitiveType.Void)
                    {
                        AppendCall(call, 0, 0);
                    }
                    else if (DirectNativeImportContract.IsScalar(call.Type))
                    {
                        AppendCall(call, 1, 15);
                    }
                    else if (temporaryHomeByExpression.TryGetValue(call,
                        out var resultHome))
                    {
                        AppendCall(call, 2,
                            checked((ushort)resultHome));
                    }
                    else
                    {
                        throw Unsupported(
                            "An unused L1 aggregate call has no typed result Home.");
                    }
                    return false;
                case BoundReturn returnStatement:
                    AppendReturn(returnStatement);
                    return true;
                case BoundIf conditional:
                    return AppendIf(conditional);
                default:
                    throw Unsupported(
                        "L1 Body statement is outside the local construction/store/call/return/CFG subset.");
            }
        }

        bool AppendStatements(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                throw Unsupported(
                    "L1 Body emission does not discard managed cleanup obligations.");
            }

            for (var statementIndex = 0;
                statementIndex < block.Statements.Length; statementIndex++)
            {
                var statement = block.Statements[statementIndex];
                var terminates = AppendStatement(statement);
                if (!terminates)
                {
                    continue;
                }

                for (var unreachableIndex = statementIndex + 1;
                    unreachableIndex < block.Statements.Length;
                    unreachableIndex++)
                {
                    if (block.Statements[unreachableIndex] is not
                        (BoundVacantLocal or
                            BoundLifetimeEnd { CleanupFunction: -1 }))
                    {
                        throw Unsupported(
                            "L1 Body emission does not serialize reachable statements after a terminal operation.");
                    }
                }
                return true;
            }

            return false;
        }

        var returned = AppendStatements(function.Body);
        if (!returned)
        {
            if (function.ReturnType != PrimitiveType.Void)
            {
                throw Unsupported(
                    "L1 non-unit functions require an explicit normal return.");
            }
            code.Add(0x18);
            code.Add(0);
            returned = true;
        }
        if (!returned)
        {
            throw Unsupported("An L1 function Body has no terminal operation.");
        }

        var constantMap = new byte[checked(constantRows.Count * 8)];
        for (var index = 0; index < constantRows.Count; index++)
        {
            var value = constantRows[index];
            var offset = index * 8;
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap.AsSpan(offset),
                value.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset + 4), value.ConstantSourceRow);
        }
        var codeBytes = new List<byte>();
        var blocks = new List<TrRmRegisterBodyBlock>(codeBlocks.Count);
        for (var blockIndex = 0; blockIndex < codeBlocks.Count; blockIndex++)
        {
            var blockCode = codeBlocks[blockIndex];
            if (blockCode.Count == 0)
            {
                throw Unsupported("An L1 Body contains an empty CFG block.");
            }

            var codeOffset = checked((uint)codeBytes.Count);
            codeBytes.AddRange(blockCode);
            blocks.Add(new TrRmRegisterBodyBlock(codeOffset,
                checked((uint)blockCode.Count),
                blockEntryStateRecordIndices[blockIndex]));
        }
        var flattenedCode = codeBytes.ToArray();
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, constantMap,
            blocks, records, flattenedCode, contractCount,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
    }
}
