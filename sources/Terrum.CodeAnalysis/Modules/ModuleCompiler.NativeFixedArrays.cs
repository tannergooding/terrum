// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    private static (Module Module, ImmutableArray<PrimitiveType> Mapping)
        CanonicalizeNativeFixedArrays(Module module)
    {
        var sourceArrays = module.NativeFixedArrayTypes;
        if (sourceArrays.IsDefaultOrEmpty)
        {
            if (module.Functions.Any(static item =>
                    item.Function is BoundFunction function
                    && function.Variables.Any(static variable =>
                        NativeFixedArrayTypes.IsType(variable.Type)
                        || variable.NativeFixedArrayLayout is not null)))
            {
                throw new ModuleFormatException(
                    "Module has a fixed-array local without a type-table row.");
            }
            return (module, []);
        }

        var mapping = new PrimitiveType?[sourceArrays.Length];
        var arrayStates = new byte[sourceArrays.Length];
        var pointerStates = new byte[module.NativePointerTypes.Length];
        var functionPointerStates = new byte[
            module.NativeFunctionPointerSignatures.Length];
        var canonicalArrays = new List<BoundNativeFixedArrayType>();
        var arrayIdentities = new Dictionary<(PrimitiveType ElementType,
            ulong ElementCount), int>();
        var remaining = MaximumNodes;

        void VisitType(PrimitiveType type, int depth)
        {
            if (depth > SemanticVerifier.MaximumDepth || --remaining < 0)
            {
                throw new ModuleContractException(
                    "Private fixed-array type closure exceeds its resource limits.");
            }

            if (NativeFixedArrayTypes.IsType(type))
            {
                _ = VisitArray(NativeFixedArrayTypes.Index(type), depth + 1);
                return;
            }
            if (NativePointerTypes.IsType(type))
            {
                var index = NativePointerTypes.Index(type);
                if ((uint)index >= (uint)module.NativePointerTypes.Length
                    || pointerStates[index] == 1)
                {
                    throw new ModuleFormatException(
                        "Invalid or cyclic native pointer type recipe.");
                }
                if (pointerStates[index] == 0)
                {
                    pointerStates[index] = 1;
                    VisitType(module.NativePointerTypes[index].PointeeType,
                        depth + 1);
                    pointerStates[index] = 2;
                }
                return;
            }
            if (NativeFunctionPointerTypes.IsType(type))
            {
                var index = NativeFunctionPointerTypes.Index(type);
                if ((uint)index
                        >= (uint)module.NativeFunctionPointerSignatures.Length
                    || functionPointerStates[index] == 1)
                {
                    throw new ModuleFormatException(
                        "Invalid or cyclic native function-pointer recipe.");
                }
                if (functionPointerStates[index] == 0)
                {
                    functionPointerStates[index] = 1;
                    var signature =
                        module.NativeFunctionPointerSignatures[index];
                    VisitType(signature.ReturnType, depth + 1);
                    foreach (var parameterType in signature.ParameterTypes)
                    {
                        VisitType(parameterType, depth + 1);
                    }
                    functionPointerStates[index] = 2;
                }
            }
        }

        PrimitiveType VisitArray(int index, int depth)
        {
            if ((uint)index >= (uint)sourceArrays.Length)
            {
                throw new ModuleFormatException(
                    "Invalid native fixed-array type reference.");
            }
            if (mapping[index] is PrimitiveType mapped)
            {
                return mapped;
            }
            if (arrayStates[index] != 0)
            {
                throw new ModuleFormatException(
                    "Cyclic native fixed-array type recipe.");
            }

            arrayStates[index] = 1;
            var source = sourceArrays[index];
            VisitType(source.ElementType, depth + 1);
            var elementType = MapType(source.ElementType);
            var identity = (elementType, source.ElementCount);
            var combinedIndex = canonicalArrays.Count;
            if (arrayIdentities.TryGetValue(identity, out var existingIndex))
            {
                if (canonicalArrays[existingIndex] != source with {
                        ElementType = elementType,
                    })
                {
                    throw new ModuleFormatException(
                        "Conflicting native fixed-array type identities.");
                }
                combinedIndex = existingIndex;
            }
            else
            {
                if (canonicalArrays.Count is >= NativeFixedArrayTypes.MaximumCount
                    or >= SemanticVerifier.MaximumAnalysisVisits)
                {
                    throw new ModuleContractException(
                        "Private fixed-array type count exceeds its limit.");
                }
                arrayIdentities.Add(identity, combinedIndex);
                canonicalArrays.Add(source with { ElementType = elementType });
            }

            var result = NativeFixedArrayTypes.Type(combinedIndex);
            mapping[index] = result;
            arrayStates[index] = 2;
            return result;
        }

        PrimitiveType MapType(PrimitiveType type)
        {
            if (!NativeFixedArrayTypes.IsType(type))
            {
                return type;
            }

            var index = NativeFixedArrayTypes.Index(type);
            return (uint)index < (uint)mapping.Length
                && mapping[index] is PrimitiveType mapped
                    ? mapped
                    : throw new ModuleFormatException(
                        "Unresolved native fixed-array type reference.");
        }

        foreach (var buffer in module.InvocationBuffers)
        {
            VisitType(buffer.ElementType, 0);
        }
        foreach (var structure in module.Structures)
        {
            foreach (var field in structure.Fields)
            {
                VisitType(field.Type, 0);
            }
        }
        foreach (var @class in module.Classes)
        {
            foreach (var field in @class.Fields)
            {
                VisitType(field.Type, 0);
            }
        }
        foreach (var signature in module.NativeFunctionPointerSignatures)
        {
            VisitType(signature.ReturnType, 0);
            foreach (var parameterType in signature.ParameterTypes)
            {
                VisitType(parameterType, 0);
            }
        }
        foreach (var pointerType in module.NativePointerTypes)
        {
            VisitType(pointerType.PointeeType, 0);
        }
        foreach (var item in module.Functions)
        {
            foreach (var output in item.NativeOutputContracts)
            {
                VisitType(output.PointeeType, 0);
            }
            if (item.ImportedNativeVTableMethod
                is NativeVTableMethodDefinition importedMethod)
            {
                VisitParameters(importedMethod.Parameters);
                VisitOutputs(importedMethod.Outputs);
            }
            if (item.Function is not BoundFunction function)
            {
                continue;
            }

            VisitType(function.ReturnType, 0);
            if (function.NativeFunctionPointerType is PrimitiveType functionPointerType)
            {
                VisitType(functionPointerType, 0);
            }
            if (function.NativeVTableMethod
                is NativeVTableMethodDefinition vtableMethod)
            {
                VisitParameters(vtableMethod.Parameters);
                VisitOutputs(vtableMethod.Outputs);
            }
            if (function.DirectNativeImport is DirectNativeImport import)
            {
                VisitParameters(import.Parameters);
                VisitOutputs(import.Outputs);
            }
            foreach (var variable in function.Variables)
            {
                VisitType(variable.Type, 0);
            }
            _ = Calls(function.Body,
                visitType: type => VisitType(type, 0));
        }

        if (canonicalArrays.Count == 0
            && module.Functions.Any(static item =>
                item.Function is BoundFunction function
                && function.Variables.Any(static variable =>
                    NativeFixedArrayTypes.IsType(variable.Type))))
        {
            throw new ModuleFormatException(
                "Private fixed-array local was not included in its type closure.");
        }
        void VisitParameters(ImmutableArray<DirectNativeParameter> parameters)
        {
            foreach (var parameter in parameters)
            {
                VisitType(parameter.PointeeType, 0);
            }
        }

        void VisitOutputs(ImmutableArray<DirectNativeOutput> outputs)
        {
            foreach (var output in outputs)
            {
                VisitType(output.PointeeType, 0);
            }
        }

        var remap = ImmutableArray.CreateBuilder<PrimitiveType>(mapping.Length);
        foreach (var type in mapping)
        {
            remap.Add(type ?? PrimitiveType.Int32);
        }
        var arrayMapping = remap.MoveToImmutable();
        ImmutableArray<NativeFunctionPointerSignatureDefinition> remappedSignatures =
            [.. module.NativeFunctionPointerSignatures
            .Select(signature => RemapNativeFixedArrayTypeDefinitions(
                signature, MapType))];
        ImmutableArray<ModuleFunction> remappedFunctions =
            [.. module.Functions.Select(item => item with {
            Function = item.Function is BoundFunction function
                ? RemapNativeFixedArrayTypes(function, arrayMapping) : null,
            ImportedNativeVTableMethod = item.ImportedNativeVTableMethod
                is NativeVTableMethodDefinition importedMethod
                    ? RemapNativeVTableMethod(importedMethod, MapType) : null,
            NativeOutputContracts = [.. item.NativeOutputContracts.Select(
                output => output with {
                    PointeeType = MapType(output.PointeeType),
                })],
        })];
        var result = module with {
            Structures = [.. module.Structures.Select(structure =>
                BoundTreeTraversal.RewriteTypes(structure, MapType))],
            Classes = [.. module.Classes.Select(@class =>
                BoundTreeTraversal.RewriteTypes(@class, MapType))],
            NativeFunctionPointerSignatures = remappedSignatures,
            NativePointerTypes = [.. module.NativePointerTypes.Select(pointer =>
                pointer with { PointeeType = MapType(pointer.PointeeType) })],
            NativeFixedArrayTypes = [.. canonicalArrays],
            InvocationBuffers = [.. module.InvocationBuffers.Select(buffer =>
                buffer with { ElementType = MapType(buffer.ElementType) })],
            Functions = remappedFunctions,
        };
        if (!NativeFixedArrayContract.IsValidTable(result.Structures,
                result.NativeFixedArrayTypes))
        {
            throw new ModuleContractException(
                "Canonical fixed-array identities do not preserve their layouts.");
        }
        return (result, arrayMapping);

        static NativeVTableMethodDefinition RemapNativeVTableMethod(
            NativeVTableMethodDefinition method,
            Func<PrimitiveType, PrimitiveType> remapType)
        {
            return method with {
                Parameters = [.. method.Parameters.Select(parameter =>
                    parameter with {
                        PointeeType = remapType(parameter.PointeeType),
                    })],
                Outputs = [.. method.Outputs.Select(output => output with {
                    PointeeType = remapType(output.PointeeType),
                })],
            };
        }
    }

    private static NativeFunctionPointerSignatureDefinition
        RemapNativeFixedArrayTypeDefinitions(
            NativeFunctionPointerSignatureDefinition signature,
            Func<PrimitiveType, PrimitiveType> remapType)
    {
        var changed = remapType(signature.ReturnType)
            != signature.ReturnType
            || signature.ParameterTypes.Any(type =>
                remapType(type) != type)
            || signature.NativeParameters.Any(parameter =>
                remapType(parameter.PointeeType)
                    != parameter.PointeeType);
        if (!changed)
        {
            return signature;
        }

        return signature with {
            ReturnType = remapType(signature.ReturnType),
            ParameterTypes = [.. signature.ParameterTypes.Select(remapType)],
            NativeParameters = [.. signature.NativeParameters.Select(parameter =>
                parameter with {
                    PointeeType = remapType(parameter.PointeeType),
                })],
        };
    }

    private static PrimitiveType RemapNativeFixedArrayType(
        PrimitiveType type,
        ImmutableArray<PrimitiveType> mapping)
    {
        if (!NativeFixedArrayTypes.IsType(type))
        {
            return type;
        }

        var index = NativeFixedArrayTypes.Index(type);
        if ((uint)index >= (uint)mapping.Length
            || !NativeFixedArrayTypes.IsType(mapping[index]))
        {
            throw new ModuleFormatException(
                "Unknown native fixed-array type.");
        }
        return mapping[index];
    }
}
