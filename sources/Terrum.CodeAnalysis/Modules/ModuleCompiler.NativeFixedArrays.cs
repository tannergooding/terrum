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
                        NativeFixedArrayTypes.IsType(variable.Type))))
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
                _ = VisitArray(NativeFixedArrayTypes.Index(type), depth);
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

    private static bool IsAddressOnlyNestedBackingType(
        PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        IReadOnlyList<NativePointerTypeDefinition> pointerTypes,
        IReadOnlyList<NativeFunctionPointerSignatureDefinition>
            functionPointers)
    {
        var depth = 0;
        var current = type;
        while (NativeFixedArrayTypes.IsType(current))
        {
            if (++depth > SemanticVerifier.MaximumDepth
                || !NativeFixedArrayContract.TryGetLayout(current,
                    structures, fixedArrays, out var layout)
                || !NativeFixedArrayContract.MatchesLayout(layout,
                    structures, fixedArrays)
                || layout.ElementCount == 0
                || layout.ElementStride == 0
                || layout.ByteExtent == 0)
            {
                return false;
            }

            if (NativeFixedArrayTypes.IsType(layout.ElementType))
            {
                current = layout.ElementType;
                continue;
            }

            return depth > 1
                && IsRevision100RuntimeArrayLayout(layout, structures,
                    fixedArrays, pointerTypes, functionPointers);
        }

        return false;
    }

    private static bool IsAddressOnlyNestedBackingInitializer(
        BoundExpression initializer,
        PrimitiveType expectedType,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        IReadOnlyList<NativePointerTypeDefinition> pointerTypes,
        IReadOnlyList<NativeFunctionPointerSignatureDefinition>
            functionPointers)
    {
        var pending = new Stack<(BoundExpression Initializer,
            PrimitiveType ExpectedType, int Depth)>();
        pending.Push((initializer, expectedType, 1));
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pending.TryPop(out var item))
        {
            if (--remaining < 0
                || item.Depth > SemanticVerifier.MaximumDepth
                || item.Initializer.Type != item.ExpectedType
                || !NativeFixedArrayTypes.IsType(item.ExpectedType)
                || !NativeFixedArrayContract.TryGetLayout(item.ExpectedType,
                    structures, fixedArrays, out var layout)
                || !NativeFixedArrayContract.MatchesType(item.ExpectedType,
                    layout, structures, fixedArrays)
                || !NativeFixedArrayContract.MatchesLayout(layout,
                    structures, fixedArrays)
                || layout.ElementCount == 0
                || layout.ElementStride == 0
                || layout.ByteExtent == 0)
            {
                return false;
            }

            if (NativeFixedArrayTypes.IsType(layout.ElementType))
            {
                if (item.Initializer is BoundNativeFixedArrayInitialization
                    initialization)
                {
                    if (initialization.Elements.IsDefault
                        || (ulong)initialization.Elements.Length
                            != layout.ElementCount)
                    {
                        return false;
                    }

                    foreach (var element in initialization.Elements)
                    {
                        if (--remaining < 0
                            || element.Type != layout.ElementType)
                        {
                            return false;
                        }

                        pending.Push((element, layout.ElementType,
                            item.Depth + 1));
                    }
                }
                else if (item.Initializer
                            is BoundNativeFixedArrayRepeatInitialization
                                repeat
                    && repeat.Layout == layout
                    && repeat.Seed.Type == layout.ElementType
                    && --remaining >= 0)
                {
                    pending.Push((repeat.Seed, layout.ElementType,
                        item.Depth + 1));
                }
                else
                {
                    return false;
                }
                continue;
            }

            if (!IsRevision100RuntimeArrayLayout(layout, structures,
                    fixedArrays, pointerTypes, functionPointers))
            {
                return false;
            }

            if (item.Initializer is BoundNativeFixedArrayInitialization
                    leafInitialization)
            {
                if (leafInitialization.Elements.IsDefault
                    || (ulong)leafInitialization.Elements.Length
                        != layout.ElementCount)
                {
                    return false;
                }

                foreach (var element in leafInitialization.Elements)
                {
                    if (--remaining < 0
                        || element.Type != layout.ElementType
                        || NativeFixedArrayTypes.IsType(element.Type))
                    {
                        return false;
                    }
                }
            }
            else if (item.Initializer
                        is BoundNativeFixedArrayRepeatInitialization
                            leafRepeat)
            {
                if (--remaining < 0
                    || leafRepeat.Layout != layout
                    || leafRepeat.Seed is null
                    || leafRepeat.Seed.Type != layout.ElementType
                    || NativeFixedArrayTypes.IsType(
                        leafRepeat.Seed.Type))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsAddressToVariable(
        BoundExpression expression, int variable)
    {
        var pending = new Stack<(BoundExpression? Expression, int Depth)>();
        pending.Push((expression, 0));
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pending.TryPop(out var item))
        {
            if (item.Expression is null
                || item.Depth > SemanticVerifier.MaximumDepth
                || --remaining < 0)
            {
                return true;
            }

            if (item.Expression is BoundNativeStorageAddress address
                && address.Variable == variable)
            {
                return true;
            }

            foreach (var nested in item.Expression.NestedExpressions)
            {
                pending.Push((nested, item.Depth + 1));
            }
        }

        return false;
    }

    private static bool TryGetAddressOnlyNestedBackingVariables(
        BoundProgram program, BoundFunction function,
        out HashSet<int> variables, out string reason)
    {
        var nestedBackingVariables = new HashSet<int>();
        variables = nestedBackingVariables;
        reason = string.Empty;
        for (var variable = function.ParameterCount
                + (function.IsConstructor ? 1 : 0);
            variable < function.Variables.Length; variable++)
        {
            if (IsAddressOnlyNestedBackingType(
                    function.Variables[variable].Type,
                    program.Structures, program.NativeFixedArrayTypes,
                    program.NativePointerTypes,
                    program.NativeFunctionPointerSignatures))
            {
                _ = nestedBackingVariables.Add(variable);
            }
        }

        if (nestedBackingVariables.Count == 0)
        {
            return true;
        }

        var initializers
            = new Dictionary<int, BoundExpression>(
                nestedBackingVariables.Count);
        var valid = true;
        var failure
            = "a nested array local is not completely initialized backing";
        reason = "a nested array local is not completely initialized backing";
        if (!BoundTreeTraversal.VisitStatements(function.Body, statement =>
            {
                switch (statement)
                {
                    case BoundLocal local when nestedBackingVariables.Contains(
                        local.Variable):
                        var initializer = local.Initializer;
                        if ((initializer is not
                                BoundNativeFixedArrayInitialization
                                && initializer is not
                                    BoundNativeFixedArrayRepeatInitialization)
                            || !initializers.TryAdd(local.Variable,
                                initializer)
                            || !IsAddressOnlyNestedBackingInitializer(
                                initializer,
                                function.Variables[local.Variable].Type,
                                program.Structures,
                                program.NativeFixedArrayTypes,
                                program.NativePointerTypes,
                                program.NativeFunctionPointerSignatures)
                            || ContainsAddressToVariable(initializer,
                                local.Variable))
                        {
                            valid = false;
                        }
                        break;

                    case BoundVacantLocal vacant
                        when nestedBackingVariables.Contains(
                        vacant.Variable):
                        valid = false;
                        break;

                    case BoundAssignment assignment
                        when nestedBackingVariables.Contains(
                        assignment.Variable):
                        valid = false;
                        break;

                    case BoundDrop drop
                        when nestedBackingVariables.Contains(drop.Variable):
                    case BoundDropConstructedFields constructedDrop
                        when nestedBackingVariables.Contains(
                            constructedDrop.Variable):
                    case BoundDropSpan spanDrop
                        when nestedBackingVariables.Contains(
                            spanDrop.Variable):
                        valid = false;
                        break;
                }
            }))
        {
            reason = "the function statement traversal exceeds the analysis limit";
            return false;
        }

        if (!valid)
        {
            reason = failure;
            return false;
        }

        foreach (var variable in nestedBackingVariables)
        {
            if (!initializers.ContainsKey(variable))
            {
                reason = "a nested array local has no initialized declaration";
                return false;
            }
        }

        var functionIndexByName = new Dictionary<string, int>(
            program.Functions.Length, StringComparer.Ordinal);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (!functionIndexByName.TryAdd(program.Functions[index].Name,
                    index))
            {
                reason = "the module function table is ambiguous";
                return false;
            }
        }

        var permittedAddressUses = new Dictionary<int, int>(
            nestedBackingVariables.Count);
        if (!BoundTreeTraversal.VisitExpressions(function.Body, expression =>
            {
                if (expression is BoundNativeFixedArrayPathWrite pathWrite
                    && nestedBackingVariables.Contains(
                        pathWrite.Variable))
                {
                    if (pathWrite.FieldPlace is null
                        && !pathWrite.LeafFields.IsDefaultOrEmpty
                        && NativeFixedArrayContract.MatchesPath(
                            function.Variables[pathWrite.Variable].Type,
                            pathWrite.Projections, program, out _,
                            out var leafLayout)
                        && IsRevision100RuntimeArrayLayout(leafLayout,
                            program.Structures,
                            program.NativeFixedArrayTypes,
                            program.NativePointerTypes,
                            program.NativeFunctionPointerSignatures)
                        && TryGetPrivateFixedArrayLeafFieldType(
                            leafLayout.ElementType, pathWrite.LeafFields,
                            program.Structures,
                            program.NativeFixedArrayTypes,
                            program.NativePointerTypes,
                            program.NativeFunctionPointerSignatures,
                            out var terminalFieldType)
                        && terminalFieldType == pathWrite.Value.Type)
                    {
                        permittedAddressUses[pathWrite.Variable]
                            = permittedAddressUses.TryGetValue(
                                pathWrite.Variable, out var pathWriteCount)
                                ? pathWriteCount + 1 : 1;
                    }
                    else
                    {
                        valid = false;
                        failure
                            = "a nested field write is not an admitted local field-suffixed path";
                    }
                    return;
                }

                ImmutableArray<BoundExpression> arguments;
                ImmutableArray<int> argumentParameterIndices;
                BoundFunction? target = null;
                switch (expression)
                {
                    case BoundCall call:
                        if ((uint)call.Function
                            >= (uint)program.Functions.Length)
                        {
                            arguments = call.Arguments;
                            argumentParameterIndices
                                = call.ArgumentParameterIndices;
                        }
                        else
                        {
                            target = program.Functions[call.Function];
                            arguments = call.Arguments;
                            argumentParameterIndices
                                = call.ArgumentParameterIndices;
                        }
                        break;

                    case SymbolicCall call:
                        if (!functionIndexByName.TryGetValue(call.Name,
                                out var targetIndex))
                        {
                            arguments = call.Arguments;
                            argumentParameterIndices
                                = call.ArgumentParameterIndices;
                        }
                        else
                        {
                            target = program.Functions[targetIndex];
                            arguments = call.Arguments;
                            argumentParameterIndices
                                = call.ArgumentParameterIndices;
                        }
                        break;

                    default:
                        return;
                }

                if (target is null)
                {
                    if (arguments.Any(argument =>
                            argument is BoundNativeStorageAddress address
                            && nestedBackingVariables.Contains(
                                address.Variable)))
                    {
                        valid = false;
                        failure = "a nested address call target is out of range";
                    }
                    return;
                }

                if (!argumentParameterIndices.IsDefault
                    && argumentParameterIndices.Length != arguments.Length)
                {
                    valid = false;
                    failure = "a nested address call has an invalid parameter map";
                    return;
                }

                for (var argumentIndex = 0;
                    argumentIndex < arguments.Length; argumentIndex++)
                {
                    if (arguments[argumentIndex]
                        is not BoundNativeStorageAddress address
                        || !nestedBackingVariables.Contains(
                            address.Variable))
                    {
                        continue;
                    }

                    var parameter = DirectNativeImportContract
                        .ParameterForParameter(target,
                            argumentParameterIndices.IsDefault
                                ? argumentIndex
                                : argumentParameterIndices[argumentIndex]);
                    if (target.DirectNativeImport is null
                        || parameter is not DirectNativeParameter nativeParameter
                        || nativeParameter.Retention
                            != NativeParameterRetention.NoEscape
                        || address.Projections.IsDefaultOrEmpty
                        || address.Projections.Length < 2
                        || address.Index
                            != address.Projections[^1].Index
                        || NativeFixedArrayTypes.IsType(address.ElementType)
                        || !DirectNativeImportContract.MatchesAddress(
                            address, nativeParameter, function, program)
                        || !NativeFixedArrayContract.MatchesPath(
                            function.Variables[address.Variable].Type,
                            address.Projections, program, out _,
                            out var leafLayout)
                        || leafLayout != address.Projections[^1].Layout
                        || !IsRevision100RuntimeArrayLayout(leafLayout,
                            program.Structures,
                            program.NativeFixedArrayTypes,
                            program.NativePointerTypes,
                            program.NativeFunctionPointerSignatures))
                    {
                        valid = false;
                        failure
                            = "a nested address is not a direct NoEscape NativeParameter";
                        continue;
                    }

                    permittedAddressUses[address.Variable]
                        = permittedAddressUses.TryGetValue(
                            address.Variable, out var count)
                            ? count + 1 : 1;
                }
            }))
        {
            reason = "the function expression traversal exceeds the analysis limit";
            return false;
        }

        if (!valid)
        {
            reason = failure;
            return false;
        }

        var addressOccurrences = new Dictionary<int, int>(
            nestedBackingVariables.Count);
        if (!BoundTreeTraversal.VisitExpressions(function.Body, expression =>
            {
                switch (expression)
                {
                    case BoundNativeStorageAddress address
                        when nestedBackingVariables.Contains(
                            address.Variable):
                        addressOccurrences[address.Variable]
                            = addressOccurrences.TryGetValue(
                                address.Variable, out var addressCount)
                                ? addressCount + 1 : 1;
                        break;

                    case BoundRead read when nestedBackingVariables.Contains(
                        read.Variable):
                    case BoundMove move when nestedBackingVariables.Contains(
                        move.Variable):
                    case BoundOutArgument output
                        when nestedBackingVariables.Contains(
                            output.Variable):
                    case BoundNativeFixedArrayElement arrayElement
                        when nestedBackingVariables.Contains(
                            arrayElement.Variable):
                    case BoundNativeFixedArrayPathElement pathElement
                        when nestedBackingVariables.Contains(
                            pathElement.Variable):
                    case BoundNativeFixedArrayPointerAddress pointerAddress
                        when nestedBackingVariables.Contains(
                            pointerAddress.Variable):
                    case BoundNativeFixedArrayWrite arrayWrite
                        when nestedBackingVariables.Contains(
                            arrayWrite.Variable):
                        valid = false;
                        failure
                            = "a nested array is used outside address or field-store adaptation";
                        break;
                    case BoundNativeFixedArrayPathWrite pathWrite
                        when nestedBackingVariables.Contains(
                            pathWrite.Variable):
                        if (pathWrite.FieldPlace is null
                            && !pathWrite.LeafFields.IsDefaultOrEmpty)
                        {
                            addressOccurrences[pathWrite.Variable]
                                = addressOccurrences.TryGetValue(
                                    pathWrite.Variable,
                                    out var pathWriteCount)
                                    ? pathWriteCount + 1 : 1;
                            break;
                        }

                        valid = false;
                        failure
                            = "a nested array is used outside address or field-store adaptation";
                        break;
                }
            }))
        {
            reason = "the function expression traversal exceeds the analysis limit";
            return false;
        }

        if (!valid)
        {
            reason = failure;
            return false;
        }

        foreach (var variable in nestedBackingVariables)
        {
            var addressCount
                = addressOccurrences.TryGetValue(variable, out var currentAddressCount)
                    ? currentAddressCount : 0;
            var permittedCount
                = permittedAddressUses.TryGetValue(variable,
                    out var currentPermittedCount)
                    ? currentPermittedCount : 0;
            if (permittedCount != addressCount)
            {
                valid = false;
                reason = $"nested local {variable} has {addressCount} address nodes but {permittedCount} direct native arguments";
                break;
            }
        }

        return valid;
    }

    private static void ValidateAddressOnlyNestedBacking(Module module)
    {
        var functions = ImmutableArray.CreateBuilder<BoundFunction>(
            module.Functions.Length);
        foreach (var item in module.Functions)
        {
            functions.Add(item.Function
                ?? new BoundFunction(item.Name, PrimitiveType.Void, [], 0,
                    new BoundBlock([], default), default) {
                    IsExternal = true,
                });
        }

        var program = new BoundProgram(functions.MoveToImmutable(),
            module.EntryPoint) {
            Structures = module.Structures,
            Classes = module.Classes,
            NativeFunctionPointerSignatures
                = module.NativeFunctionPointerSignatures,
            NativePointerTypes = module.NativePointerTypes,
            NativeFixedArrayTypes = module.NativeFixedArrayTypes,
            RuntimePolicy = module.RuntimePolicy,
        };
        if (!TryValidateAddressOnlyNestedBacking(program,
                out var functionName, out var reason))
        {
            throw new ModuleFormatException(
                $"Module function '{functionName}' has nested fixed-array backing outside the revision-102 nested-backing profile: {reason}.");
        }
    }

    private static void ValidateAddressOnlyNestedBacking(
        BoundProgram program)
    {
        if (!TryValidateAddressOnlyNestedBacking(program,
                out var functionName, out var reason))
        {
            throw new ModuleContractException(
                $"Function '{functionName}' has nested fixed-array backing outside the revision-102 nested-backing profile: {reason}.");
        }
    }

    private static bool TryValidateAddressOnlyNestedBacking(
        BoundProgram program, out string functionName, out string reason)
    {
        foreach (var function in program.Functions)
        {
            if (!TryGetAddressOnlyNestedBackingVariables(program, function,
                    out _, out reason))
            {
                functionName = function.Name;
                return false;
            }
        }

        functionName = string.Empty;
        reason = string.Empty;
        return true;
    }
}
