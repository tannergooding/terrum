// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static ImmutableArray<BoundInterfaceDefinition>
        CreateExportedForeignNativeInterfaceDefinitions(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow)
    {
        var (moduleRefStart, moduleRefEnd)
            = tables.Modules.GetModuleRefRange(moduleRow);
        if (moduleRefStart != moduleRefEnd)
        {
            throw Unsupported(
                "The source-free F+I reader does not yet bind imported interface expectations.");
        }

        var typeExportKeys = new Dictionary<uint, string>();
        var methodExportKeys = new Dictionary<uint, List<string>>();
        foreach (var (definition, exportKey)
            in tables.Exports.GetExportsForModule(moduleRow))
        {
            if ((definition >> 24) == 0x13)
            {
                if (!typeExportKeys.TryAdd(definition, exportKey))
                {
                    throw Invalid(
                        "A ForeignNativeInterface TypeDef has multiple ExportKeys.");
                }
            }
            else if ((definition >> 24) == 0x16)
            {
                if (!methodExportKeys.TryGetValue(definition,
                        out var keys))
                {
                    keys = [];
                    methodExportKeys.Add(definition, keys);
                }

                keys.Add(exportKey);
            }
        }

        var pointerTypeIndexes = context.ForeignNativeInterfacePointerTypes;
        var signatureIndexByAnchorMethod
            = new int[tables.Methods.Count + 1];
        Array.Fill(signatureIndexByAnchorMethod, -1);
        var structureTypeByTypeDef
            = new int[tables.TypeDefs.Count + 1];
        Array.Fill(structureTypeByTypeDef, -1);
        var foreignInterfaceRowsByOwner
            = new Dictionary<int, int>(tables.ForeignNativeInterfaces.Count);
        for (var foreignInterfaceRow = 1;
            foreignInterfaceRow <= tables.ForeignNativeInterfaces.Count;
            foreignInterfaceRow++)
        {
            var interfaceDef = checked((int)
                tables.ForeignNativeInterfaces.GetOwnerInterfaceDef(
                    foreignInterfaceRow));
            if (!foreignInterfaceRowsByOwner.TryAdd(interfaceDef,
                    foreignInterfaceRow))
            {
                throw Invalid(
                    "A ForeignNativeInterface InterfaceDef has duplicate layout rows.");
            }
        }

        var (firstTypeDef, endTypeDef)
            = tables.Modules.GetTypeDefRange(moduleRow);
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(moduleRow);
        var definitions = ImmutableArray.CreateBuilder<
            BoundInterfaceDefinition>();
        for (var typeDef = firstTypeDef; typeDef < endTypeDef; typeDef++)
        {
            var interfaceDef
                = tables.Interfaces.GetInterfaceDefForTypeDef(typeDef);
            if (interfaceDef == 0)
            {
                continue;
            }

            var typeDefId = 0x1300_0000u | checked((uint)typeDef);
            if (!typeExportKeys.TryGetValue(typeDefId,
                    out var typeExportKey))
            {
                var (unexportedFirstMethod, unexportedMethodCount)
                    = tables.Interfaces.GetMethodRange(interfaceDef);
                for (var methodOrdinal = 0;
                    methodOrdinal < unexportedMethodCount;
                    methodOrdinal++)
                {
                    var methodRow = tables.InterfaceMethods
                        .GetMethodDefRow(unexportedFirstMethod
                            + methodOrdinal);
                    var methodId = 0x1600_0000u
                        | checked((uint)methodRow);
                    if (methodExportKeys.ContainsKey(methodId))
                    {
                        throw Invalid(
                            "A ForeignNativeInterface MethodDef is exported without its owner TypeDef.");
                    }
                }

                continue;
            }

            if (definitions.Count >= SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Unsupported(
                    "The F+I source view exceeds the supported declaration limit.");
            }

            var (firstMethod, methodCount)
                = tables.Interfaces.GetMethodRange(interfaceDef);
            var methodKeys = new ImmutableArray<string>[methodCount];
            for (var methodOrdinal = 0; methodOrdinal < methodCount;
                methodOrdinal++)
            {
                var interfaceMethod = firstMethod + methodOrdinal;
                if (tables.InterfaceMethods.GetOwnerInterfaceDef(
                        interfaceMethod) != interfaceDef
                    || tables.InterfaceMethods.GetOrdinal(interfaceMethod)
                        != methodOrdinal)
                {
                    throw Invalid(
                        "A ForeignNativeInterface InterfaceMethod range is not declaration-ordered.");
                }

                var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                    interfaceMethod);
                var methodId = 0x1600_0000u
                    | checked((uint)methodRow);
                if (!methodExportKeys.TryGetValue(methodId,
                        out var exportsForMethod)
                    || exportsForMethod.Count == 0)
                {
                    throw Invalid(
                        "A ForeignNativeInterface MethodDef has no ExportKey.");
                }

                methodKeys[methodOrdinal] = [.. exportsForMethod
                    .OrderBy(static key => key, StringComparer.Ordinal)];
            }

            var interfaceName = tables.TypeDefs.GetName(typeDef);
            var interfaceNamespace = tables.TypeDefs.GetNamespace(typeDef);
            var qualifiedName = interfaceNamespace.Length == 0
                ? interfaceName
                : string.Concat(interfaceNamespace, ".", interfaceName);
            if (!foreignInterfaceRowsByOwner.TryGetValue(interfaceDef,
                    out var foreignInterfaceRow))
            {
                throw Invalid(
                    "A ForeignNativeInterface InterfaceDef has no layout row.");
            }

            var definition = ReadForeignNativeInterfaceDefinition(tables,
                context, interfaceDef, typeDef, foreignInterfaceRow,
                qualifiedName, typeExportKey, targetRequirement,
                runtimePolicy, methodKeys, pointerTypeIndexes,
                signatureIndexByAnchorMethod, structureTypeByTypeDef,
                tables.Modules.GetIdentity(moduleRow));
            if (!context.InterfaceDefinitionsByTypeDef.TryAdd(
                    (artifactIndex, moduleRow, typeDef), definition))
            {
                throw Invalid(
                    "A ForeignNativeInterface TypeDef has multiple declaration views.");
            }

            if (!context.ExportedInterfaceDefinitionsByKey.TryAdd(
                    (artifactIndex, moduleRow, typeExportKey), definition))
            {
                throw Invalid(
                    "A ForeignNativeInterface TypeDef ExportKey selects multiple declarations.");
            }

            definitions.Add(definition);
        }

        var result = definitions.ToImmutable();
        context.ExportedInterfaceDefinitions.Add(
            (artifactIndex, moduleRow), result);
        return result;
    }

    private static BoundInterfaceDefinition
        ReadForeignNativeInterfaceDefinition(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int interfaceDef, int typeDef,
        int foreignInterfaceRow, string qualifiedName, string typeExportKey,
        uint targetRequirement, uint runtimePolicy,
        ImmutableArray<string>[] methodExportKeys,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] signatureIndexByAnchorMethod, int[] structureTypeByTypeDef,
        string moduleIdentity)
    {
        var (fieldStart, fieldEnd)
            = tables.TypeDefs.GetFieldRange(typeDef);
        var (_, genericCount) = tables.TypeDefs.GetGenericRange(typeDef);
        var (_, typeContractCount)
            = tables.TypeDefs.GetContractRange(typeDef);
        if (tables.TypeDefs.GetKind(typeDef) != 3
            || fieldStart != fieldEnd || genericCount != 0
            || typeContractCount != 0
            || tables.Interfaces.GetMethodRange(interfaceDef).Count
                != methodExportKeys.Length
            || tables.ForeignNativeInterfaces.GetTargetProfileRef(
                foreignInterfaceRow) == 0)
        {
            throw Invalid(
                "A ForeignNativeInterface declaration has an unsupported TypeDef or owner shape.");
        }

        var (firstMethod, methodCount)
            = tables.Interfaces.GetMethodRange(interfaceDef);
        var members = ImmutableArray.CreateBuilder<
            BoundInterfaceMemberDefinition>(methodCount);
        for (var methodOrdinal = 0; methodOrdinal < methodCount;
            methodOrdinal++)
        {
            var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                firstMethod + methodOrdinal);
            var signatureRow = tables.Methods.GetSignature(methodRow);
            var parameterTypes = ReadForeignInvocationParameterTypes(
                methodRow, tables.Methods, tables.Signatures,
                tables.Parameters, tables.TypeSpecs,
                nativePointerTypeDefinitions, signatureIndexByAnchorMethod,
                structureTypeByTypeDef, imports: tables.Imports);
            var (firstParameter, parameterCount)
                = tables.Signatures.GetParamRange(signatureRow);
            var parameters = ImmutableArray.CreateBuilder<
                BoundInterfaceParameter>(parameterCount);
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var parameterRow = firstParameter + ordinal;
                var typeSpec = tables.Parameters.GetTypeSpec(parameterRow);
                var parameterType = CreateForeignNativeInterfaceType(
                    tables.TypeSpecs, typeSpec, parameterTypes[ordinal],
                    nativePointerTypeDefinitions);
                BoundConstant? defaultValue = null;
                string parameterName;
                if (context.Schema
                    .UsesForeignNativeInterfaceParameterSurface())
                {
                    if (!tables.MethodParameterLabels.TryGetNameStringRow(
                            methodRow, checked((uint)ordinal),
                            out var nameStringRow)
                        || !tables.MethodParameterLabels
                            .TryGetDefaultConstantSourceRow(methodRow,
                                checked((uint)ordinal),
                                out var constantSourceRow))
                    {
                        throw Invalid(
                            "A PS1 ForeignNativeInterface parameter has no complete surface row.");
                    }

                    parameterName = tables.Strings.DecodeName(nameStringRow);
                    if (constantSourceRow != 0)
                    {
                        var ownerModule
                            = tables.Methods.GetOwnerModule(methodRow);
                        var (ownerTarget, _)
                            = tables.Modules.GetRequirements(ownerModule);
                        defaultValue
                            = TrRmConstantSourceValueValidator
                                .MaterializeFrozenScalar(
                                    checked((int)constantSourceRow), typeSpec,
                                    ownerModule, ownerTarget,
                                    tables.ConstantSources,
                                    tables.FrozenValueNodes, tables.Blobs,
                                    tables.TypeSpecs, tables.Features,
                                    tables.Modules,
                                    tables.A3LayoutResourceLedger);
                        if (parameterType.Kind
                                != BoundInterfaceTypeKind.Primitive
                            || defaultValue.Type
                                != parameterType.PrimitiveType)
                        {
                            throw Invalid(
                                "A PS1 ForeignNativeInterface default does not match its parameter type.");
                        }
                    }
                }
                else
                {
                    throw Unsupported(
                        "F+I source-free references cannot expose explicit parameters.");
                }

                parameters.Add(new BoundInterfaceParameter(parameterName,
                    parameterType, BoundInterfaceParameterMode.Value, [], []) {
                    DefaultValue = defaultValue,
                });
            }

            var resultTypeSpec = tables.Signatures.GetResultTypeSpec(
                signatureRow);
            var resultType = ReadForeignInvocationType(tables.TypeSpecs,
                resultTypeSpec, nativePointerTypeDefinitions,
                signatureIndexByAnchorMethod, structureTypeByTypeDef,
                imports: tables.Imports);
            var boundResultType = CreateForeignNativeInterfaceType(
                tables.TypeSpecs, resultTypeSpec, resultType,
                nativePointerTypeDefinitions);
            var nativeCallableConvention
                = (NativeCallingConvention)tables.Contracts
                    .GetNativeCallableConvention(methodRow);
            var effects = tables.Contracts.GetEffectsMask(methodRow);
            var nativeOutputs = ReadForeignInvocationNativeOutputs(methodRow,
                parameterTypes, nativePointerTypeDefinitions, tables,
                out var dependencies, allowGeneralDependencies: true,
                methodResultType: resultType);
            if (!nativeOutputs.IsEmpty && (effects & 2) != 0)
            {
                throw Invalid(
                    "A ForeignNativeInterface NativeOutput method does not have a normal return.");
            }

            var keys = methodExportKeys[methodOrdinal];
            members.Add(new BoundInterfaceMemberDefinition(methodOrdinal,
                tables.Methods.GetName(methodRow), keys[0],
                BoundInterfaceMemberKind.Callable,
                BoundInterfaceAccessibility.Public, false, false, false,
                (effects & 4) != 0, (effects & 2) != 0, false, false,
                [], [], [], default, boundResultType, [], [],
                parameters.MoveToImmutable(), dependencies, [], null, null,
                null) {
                RequiresMutableReceiver
                    = (tables.Methods.GetFlags(methodRow) & 4) != 0,
                DeclaredRequiresUnsafeCaller = (effects & 4) != 0,
                NativeCallableConvention = nativeCallableConvention,
                NativeOutputContracts = nativeOutputs,
                AdditionalDeclarationKeys = keys.RemoveAt(0),
            });
        }

        return new BoundInterfaceDefinition(qualifiedName, true, false,
            [], [], [], members.MoveToImmutable()) {
            Accessibility = BoundInterfaceAccessibility.Public,
            ModuleImport = new BoundModuleImport(moduleIdentity,
                typeExportKey, targetRequirement, runtimePolicy),
            ForeignNativeInterface = new BoundForeignNativeInterfaceLayout(
                tables.ForeignNativeInterfaces.GetVTableByteOffset(
                    foreignInterfaceRow)),
        };
    }

    private static BoundInterfaceType CreateForeignNativeInterfaceType(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpec,
        PrimitiveType primitiveType,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions)
    {
        if (NativeFunctionPointerTypes.IsType(primitiveType))
        {
            throw Unsupported(
                "F+I source-free references do not yet decode function-pointer anchors.");
        }

        var isWritable = NativePointerTypes.IsType(primitiveType)
            ? nativePointerTypeDefinitions[
                NativePointerTypes.Index(primitiveType)].IsWritable
            : primitiveType == PrimitiveType.NativeRawPointer
                && typeSpecs.GetTypeSpecRef(typeSpec).Qualifiers != 0;
        return new BoundInterfaceType(BoundInterfaceTypeKind.Primitive,
            primitiveType, null, -1, [], isWritable, false, false, false);
    }
}
