// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    /// <summary>Combines independent producer declarations for binding; overlapping public names are ambiguous.</summary>
    public static CompilationReference CreateReferenceSet(IReadOnlyList<byte[]> producers)
    {
        var modules = ReadSet(producers);
        var references = new CompilationReference[modules.Length];
        for (var index = 0; index < modules.Length; index++)
        {
            references[index] = SetReference(modules[index]);
        }
        return CreateReferenceSetFromReferences(references);
    }

    internal static CompilationReference CreateReferenceSetFromReferences(
        IReadOnlyList<CompilationReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (references.Count is < 2 or > 16)
        {
            throw new ModuleContractException("A native module set requires 2 to 16 producers.");
        }
        var typeCount = 0;
        var functionCount = 0;
        var coreTaskType = default(CoreTaskTypeIdentity?);
        var genericNominalDefinitions = ImmutableArray.CreateBuilder<BoundGenericNominalTypeDefinition>();
        var genericNominalDefinitionIdentities = new HashSet<BoundGenericNominalTypeIdentity>();
        foreach (var reference in references)
        {
            if (reference.RequiresSourceGrant)
            {
                throw new ModuleContractException("Native module sets do not support Host.Source requirements.");
            }
            if (reference.RuntimePolicy != references[0].RuntimePolicy)
            {
                throw new ModuleContractException("Native module set runtime policies differ.");
            }
            typeCount = checked(typeCount + reference.Structures.Length);
            functionCount = checked(functionCount + reference.Functions.Length);
            foreach (var definition in reference.GenericNominalTypeDefinitions)
            {
                if (!genericNominalDefinitionIdentities.Add(definition.Identity))
                {
                    throw new ModuleContractException(
                        $"Ambiguous generic nominal identity '{definition.Identity}'.");
                }
                genericNominalDefinitions.Add(definition);
            }
            if (reference.CoreTaskType is CoreTaskTypeIdentity selectedCoreTask)
            {
                if (coreTaskType is CoreTaskTypeIdentity existingCoreTask
                    && existingCoreTask != selectedCoreTask)
                {
                    throw new ModuleContractException(
                        "A module set cannot select conflicting Core Task definitions.");
                }
                coreTaskType = selectedCoreTask;
            }
        }
        var allEnumDefinitions = MergePrivateEnumDefinitions(references,
            out var enumMappings);
        var classMappings = MergeClassTypeMappings(references,
            out var sourceClasses);
        var nativeFunctionPointerSignatures =
            MergeNativeFunctionPointerSignatures(references,
                out var functionPointerMappings,
                out var nativePointerMappings, classMappings,
                sourceClasses, enumMappings: enumMappings);
        var types = ImmutableArray.CreateBuilder<BoundStruct>(typeCount);
        var functions = ImmutableArray.CreateBuilder<BoundFunction>(functionCount);
        var typeReferenceIndexes = new int[typeCount];
        var names = new HashSet<string>(checked(typeCount + functionCount
            + sourceClasses.Length), StringComparer.Ordinal);
        var typeReferenceIndex = 0;
        for (var referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
        {
            foreach (var structure in references[referenceIndex].Structures)
            {
                if (!names.Add(structure.Name))
                {
                    throw new ModuleContractException($"Ambiguous provider type '{structure.Name}'.");
                }
                types.Add(structure);
                typeReferenceIndexes[typeReferenceIndex++] = referenceIndex;
            }
        }
        foreach (var @class in sourceClasses)
        {
            if (!names.Add(@class.Name))
            {
                throw new ModuleContractException(
                    $"Ambiguous provider class '{@class.Name}'.");
            }
        }
        var destinationTypes = IndexTypes(types);
        var nativeFixedArrayTypes = MergeNativeFixedArrayTypeDefinitions(
            references, types, nativePointerMappings, functionPointerMappings,
            out var fixedArrayMappings, enumMappings);
        var typeIndex = 0;
        for (var referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
        {
            var reference = references[referenceIndex];
            foreach (var structure in reference.Structures)
            {
                types[typeIndex] = RemapNativeFunctionPointerTypes(
                    RemapNativePointerTypes(
                        BoundTreeTraversal.RewriteTypes(
                            RemapStructure(structure, reference.Structures, types,
                                destinationTypes),
                            type => RemapNativeFixedArrayType(type,
                                fixedArrayMappings[referenceIndex])),
                        nativePointerMappings[referenceIndex]),
                    functionPointerMappings[referenceIndex]);
                types[typeIndex] = BoundTreeTraversal.RewriteTypes(
                    types[typeIndex], type => RemapManagedClassType(type,
                        classMappings[referenceIndex]));
                types[typeIndex] = RemapEnumTypes(types[typeIndex],
                    enumMappings[referenceIndex]);
                typeIndex++;
            }
        }
        var allTypes = types.MoveToImmutable();
        var checkedReferenceMappings = MergeCheckedReferenceTypeDefinitions(
            references, allTypes, destinationTypes, nativePointerMappings,
            functionPointerMappings, fixedArrayMappings,
            classMappings,
            out var allCheckedReferenceTypes);
        var allTypesBuilder = allTypes.ToBuilder();
        for (var index = 0; index < allTypesBuilder.Count; index++)
        {
            var referenceIndex = typeReferenceIndexes[index];
            allTypesBuilder[index] = BoundTreeTraversal.RewriteTypes(
                allTypesBuilder[index], type => RemapCheckedReferenceType(
                    type, checkedReferenceMappings[referenceIndex]));
        }
        allTypes = allTypesBuilder.ToImmutable();
        var allClassesBuilder = ImmutableArray.CreateBuilder<BoundClass>(
            sourceClasses.Length);
        var addedClassMappings = new HashSet<int>();
        for (var referenceIndex = 0;
            referenceIndex < references.Count; referenceIndex++)
        {
            var reference = references[referenceIndex];
            for (var sourceClassIndex = 0;
                sourceClassIndex < reference.Classes.Length;
                sourceClassIndex++)
            {
                var destinationClassIndex
                    = classMappings[referenceIndex][sourceClassIndex];
                if (!addedClassMappings.Add(destinationClassIndex))
                {
                    continue;
                }
                if (destinationClassIndex != allClassesBuilder.Count)
                {
                    throw new ModuleContractException(
                        "Provider class mappings are not in canonical order.");
                }
                var sourceClass = reference.Classes[sourceClassIndex];
                var mappedClass = RemapClass(sourceClass,
                    reference.Structures, allTypes,
                    destinationTypes: destinationTypes);
                mappedClass = RemapNativeFixedArrayTypes(mappedClass,
                    fixedArrayMappings[referenceIndex]);
                mappedClass = RemapNativeFunctionPointerTypes(mappedClass,
                    functionPointerMappings[referenceIndex]);
                mappedClass = RemapNativePointerTypes(mappedClass,
                    nativePointerMappings[referenceIndex]);
                mappedClass = BoundTreeTraversal.RewriteTypes(mappedClass,
                    type => RemapManagedClassType(type,
                        classMappings[referenceIndex]));
                mappedClass = BoundTreeTraversal.RewriteTypes(mappedClass,
                    type => RemapCheckedReferenceType(type,
                        checkedReferenceMappings[referenceIndex]));
                mappedClass = RemapEnumTypes(mappedClass,
                    enumMappings[referenceIndex]);
                allClassesBuilder.Add(mappedClass);
            }
        }
        var allClasses = allClassesBuilder.MoveToImmutable();
        var allGenericNominalDefinitions = genericNominalDefinitions.ToImmutable();
        var allGenericNominalInstances =
            ImmutableArray.CreateBuilder<BoundGenericNominalTypeInstance>();
        var genericNominalTypeMappings = new int[references.Count][];
        var genericNominalTypeStates = new byte[references.Count][];
        var genericNominalTypeIndexes =
            new Dictionary<string, PrimitiveType>(StringComparer.Ordinal);
        for (var referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
        {
            var sourceInstances = references[referenceIndex].GenericNominalTypeInstances;
            genericNominalTypeMappings[referenceIndex] = new int[sourceInstances.Length];
            Array.Fill(genericNominalTypeMappings[referenceIndex], -1);
            genericNominalTypeStates[referenceIndex] = new byte[sourceInstances.Length];
            for (var instanceIndex = 0; instanceIndex < sourceInstances.Length; instanceIndex++)
            {
                _ = MapGenericNominalTypeInstance(referenceIndex, instanceIndex);
            }
        }
        for (var referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
        {
            var reference = references[referenceIndex];
            foreach (var function in reference.Functions)
            {
                if (!names.Add(function.Name))
                {
                    throw new ModuleContractException($"Ambiguous provider export '{function.Name}'.");
                }
                var remappedFunction = RemapNativeFunctionPointerTypes(
                    RemapNativePointerTypes(
                        RemapNativeFixedArrayTypes(
                            RemapFunction(function, reference.Structures, allTypes,
                                destinationTypes: destinationTypes),
                            fixedArrayMappings[referenceIndex]),
                        nativePointerMappings[referenceIndex]),
                    functionPointerMappings[referenceIndex]);
                remappedFunction = RemapEnumTypes(remappedFunction,
                    enumMappings[referenceIndex]);
                functions.Add(BoundTreeTraversal.RewriteTypes(
                    BoundTreeTraversal.RewriteTypes(
                        BoundTreeTraversal.RewriteTypes(remappedFunction,
                            type => RemapGenericNominalType(referenceIndex,
                                type)),
                        type => RemapManagedClassType(type,
                            classMappings[referenceIndex])),
                    type => RemapCheckedReferenceType(type,
                        checkedReferenceMappings[referenceIndex])));
            }
        }
        var nominalOriginsByIdentity =
            new Dictionary<(string Module, string Key),
                BoundPrivateNominalTypeOrigin>();
        var nominalExportKeys = new HashSet<(string Module, string Key)>();
        for (var referenceIndex = 0;
            referenceIndex < references.Count; referenceIndex++)
        {
            var reference = references[referenceIndex];
            var origins = RemapPrivateNominalTypeOrigins(
                reference.PrivateNominalTypeOrigins,
                reference.Structures, allTypes,
                classMapping: classMappings[referenceIndex],
                sourceEnumDefinitions: reference.EnumDefinitions,
                destinationEnumDefinitions: allEnumDefinitions,
                enumMapping: enumMappings[referenceIndex]);
            foreach (var origin in origins)
            {
                var identity = (origin.UsingModuleIdentity,
                    origin.LocalDeclarationKey);
                if (nominalOriginsByIdentity.TryGetValue(identity,
                        out var existing))
                {
                    if (existing != origin)
                    {
                        throw new ModuleContractException(
                            "Provider set has conflicting private nominal identity origins.");
                    }
                    continue;
                }
                if (origin.OriginKind
                        == BoundPrivateNominalOriginKind.OwnedDefinition
                    && origin.IsExported
                    && !ManagedClassTypes.IsClass(origin.PrivateType)
                    && !nominalExportKeys.Add((
                        origin.UsingModuleIdentity,
                        origin.LocalDeclarationKey)))
                {
                    throw new ModuleContractException(
                        "Provider set has an ambiguous private nominal export key.");
                }
                nominalOriginsByIdentity.Add(identity, origin);
            }
        }
        var allNominalOrigins = nominalOriginsByIdentity.Values
            .OrderBy(static origin => origin.UsingModuleIdentity,
                StringComparer.Ordinal)
            .ThenBy(static origin => origin.LocalDeclarationKey,
                StringComparer.Ordinal)
            .ThenBy(static origin => (int)origin.PrivateType)
            .ToImmutableArray();
        var allPrivateClassReferences =
            ImmutableArray.CreateBuilder<BoundPrivateClassReference>();
        for (var referenceIndex = 0;
            referenceIndex < references.Count; referenceIndex++)
        {
            var reference = references[referenceIndex];
            allPrivateClassReferences.AddRange(
                RemapPrivateClassReferences(
                    reference.PrivateClassReferences,
                    reference.Structures, allTypes,
                    classMappings[referenceIndex], destinationTypes,
                    nativePointerMappings[referenceIndex],
                    functionPointerMappings[referenceIndex],
                    fixedArrayMappings[referenceIndex],
                    [.. checkedReferenceMappings[referenceIndex]],
                    enumMappings[referenceIndex]));
        }
        var nativePointerTypes = MergeNativePointerTypeDefinitions(references,
            allTypes, nativePointerMappings, functionPointerMappings,
            fixedArrayMappings, classMappings, enumMappings);
        if (!NativeFixedArrayContract.IsValidTable(allTypes,
                nativeFixedArrayTypes)
            || !NativePointerTypeContract.IsValidTable(nativePointerTypes,
                allTypes, nativeFunctionPointerSignatures,
                nativeFixedArrayTypes))
        {
            throw new ModuleContractException(
                "Combined private array and pointer metadata is invalid.");
        }
        return new CompilationReference(allTypes, functions.MoveToImmutable(), references[0].RuntimePolicy,
            nativeFunctionPointerSignatures: nativeFunctionPointerSignatures,
            nativePointerTypes: nativePointerTypes,
            nativeFixedArrayTypes: nativeFixedArrayTypes,
            checkedReferenceTypes: allCheckedReferenceTypes,
            enumDefinitions: allEnumDefinitions,
            privateNominalTypeOrigins: allNominalOrigins,
            classes: allClasses,
            privateClassReferences:
                allPrivateClassReferences.ToImmutable(),
            genericNominalTypeDefinitions: allGenericNominalDefinitions,
            genericNominalTypeInstances: allGenericNominalInstances.ToImmutable(),
            coreTaskType: coreTaskType);

        int MapGenericNominalTypeInstance(int referenceIndex, int instanceIndex)
        {
            var sourceInstances = references[referenceIndex].GenericNominalTypeInstances;
            if ((uint)instanceIndex >= (uint)sourceInstances.Length)
            {
                throw new ModuleContractException(
                    "A generic nominal type reference has an invalid source instance index.");
            }
            if (genericNominalTypeStates[referenceIndex][instanceIndex] == 1)
            {
                throw new ModuleContractException("Generic nominal type substitutions are cyclic.");
            }
            if (genericNominalTypeStates[referenceIndex][instanceIndex] == 2)
            {
                return genericNominalTypeMappings[referenceIndex][instanceIndex];
            }

            genericNominalTypeStates[referenceIndex][instanceIndex] = 1;
            var sourceInstance = sourceInstances[instanceIndex];
            var remappedInstance = BoundTreeTraversal.RewriteTypes(sourceInstance,
                type => RemapGenericNominalTypeArgument(referenceIndex, type));
            var key = BoundGenericNominalTypeContract.CreateUseKey(
                remappedInstance.Definition, remappedInstance.Arguments,
                remappedInstance.Qualifiers, remappedInstance.TypeSpecForm);
            if (!genericNominalTypeIndexes.TryGetValue(key, out var type))
            {
                if (allGenericNominalInstances.Count
                    >= BoundGenericNominalTypeTypes.MaximumCount)
                {
                    throw new ModuleContractException(
                        "The merged generic nominal type instance limit was exceeded.");
                }
                type = BoundGenericNominalTypeTypes.Type(
                    allGenericNominalInstances.Count);
                allGenericNominalInstances.Add(remappedInstance);
                genericNominalTypeIndexes.Add(key, type);
            }

            var mappedIndex = BoundGenericNominalTypeTypes.Index(type);
            genericNominalTypeMappings[referenceIndex][instanceIndex] = mappedIndex;
            genericNominalTypeStates[referenceIndex][instanceIndex] = 2;
            return mappedIndex;
        }

        PrimitiveType RemapGenericNominalType(int referenceIndex, PrimitiveType type)
        {
            if (BoundGenericNominalTypeTypes.IsType(type))
            {
                var mappedIndex = MapGenericNominalTypeInstance(
                    referenceIndex, BoundGenericNominalTypeTypes.Index(type));
                return BoundGenericNominalTypeTypes.Type(mappedIndex);
            }

            return type;
        }

        PrimitiveType RemapGenericNominalTypeArgument(
            int referenceIndex, PrimitiveType type)
        {
            if (BoundGenericNominalTypeTypes.IsType(type))
            {
                return RemapGenericNominalType(referenceIndex, type);
            }

            var reference = references[referenceIndex];
            var mapped = RemapType(type, reference.Structures, allTypes,
                destinationTypes: destinationTypes);
            mapped = RemapNativePointerType(mapped,
                nativePointerMappings[referenceIndex]);
            mapped = RemapNativeFunctionPointerType(mapped,
                functionPointerMappings[referenceIndex]);
            mapped = RemapNativeFixedArrayType(mapped,
                fixedArrayMappings[referenceIndex]);
            return RemapManagedClassType(mapped,
                classMappings[referenceIndex]);
        }
    }

    private static int[][] MergeCheckedReferenceTypeDefinitions(
        IReadOnlyList<CompilationReference> references,
        ImmutableArray<BoundStruct> destinationStructures,
        IReadOnlyDictionary<string, PrimitiveType>? destinationTypes,
        ImmutableArray<ImmutableArray<PrimitiveType>> nativePointerMappings,
        ImmutableArray<ImmutableArray<PrimitiveType>> functionPointerMappings,
        ImmutableArray<ImmutableArray<PrimitiveType>> fixedArrayMappings,
        ImmutableArray<ImmutableArray<int>> classMappings,
        out ImmutableArray<CheckedReferenceTypeDefinition> merged)
    {
        var mappings = new int[references.Count][];
        var states = new byte[references.Count][];
        var definitions = ImmutableArray.CreateBuilder<CheckedReferenceTypeDefinition>();
        var indexes = new Dictionary<CheckedReferenceTypeDefinition, int>();
        for (var referenceIndex = 0; referenceIndex < references.Count;
            referenceIndex++)
        {
            var source = references[referenceIndex].CheckedReferenceTypes;
            if (!CheckedReferenceTypeContract.IsValidTable(source))
            {
                throw new ModuleContractException(
                    "Provider checked-reference type table is invalid.");
            }
            mappings[referenceIndex] = new int[source.Length];
            Array.Fill(mappings[referenceIndex], -1);
            states[referenceIndex] = new byte[source.Length];
            for (var typeIndex = 0; typeIndex < source.Length; typeIndex++)
            {
                _ = MapReferenceType(referenceIndex, typeIndex);
            }
        }

        merged = definitions.ToImmutable();
        if (!CheckedReferenceTypeContract.IsValidTable(merged))
        {
            throw new ModuleContractException(
                "Combined checked-reference type table is invalid.");
        }
        return mappings;

        int MapReferenceType(int referenceIndex, int typeIndex)
        {
            var source = references[referenceIndex].CheckedReferenceTypes;
            if ((uint)typeIndex >= (uint)source.Length)
            {
                throw new ModuleContractException(
                    "A provider checked-reference type index is invalid.");
            }
            if (states[referenceIndex][typeIndex] == 1)
            {
                throw new ModuleContractException(
                    "Provider checked-reference types are cyclic.");
            }
            if (states[referenceIndex][typeIndex] == 2)
            {
                return mappings[referenceIndex][typeIndex];
            }

            states[referenceIndex][typeIndex] = 1;
            var definition = source[typeIndex];
            var mappedDefinition = definition with {
                ReferentType = MapType(referenceIndex,
                    definition.ReferentType),
            };
            if (!indexes.TryGetValue(mappedDefinition, out var mappedIndex))
            {
                if (definitions.Count
                    >= SemanticVerifier.MaximumAnalysisVisits)
                {
                    throw new ModuleContractException(
                        "Combined checked-reference type limit was exceeded.");
                }
                mappedIndex = definitions.Count;
                definitions.Add(mappedDefinition);
                indexes.Add(mappedDefinition, mappedIndex);
            }
            mappings[referenceIndex][typeIndex] = mappedIndex;
            states[referenceIndex][typeIndex] = 2;
            return mappedIndex;
        }

        PrimitiveType MapType(int referenceIndex, PrimitiveType type)
        {
            if (CheckedReferenceTypes.IsType(type))
            {
                return CheckedReferenceTypes.Type(MapReferenceType(
                    referenceIndex, CheckedReferenceTypes.Index(type)));
            }

            var reference = references[referenceIndex];
            var mappedType = RemapType(type, reference.Structures,
                destinationStructures, destinationTypes: destinationTypes);
            if (!classMappings.IsDefaultOrEmpty)
            {
                mappedType = RemapManagedClassType(mappedType,
                    classMappings[referenceIndex]);
            }
            mappedType = RemapNativePointerType(mappedType,
                nativePointerMappings[referenceIndex]);
            mappedType = RemapNativeFunctionPointerType(mappedType,
                functionPointerMappings[referenceIndex]);
            return RemapNativeFixedArrayType(mappedType,
                fixedArrayMappings[referenceIndex]);
        }
    }

    private static ImmutableArray<ImmutableArray<int>>
        MergeClassTypeMappings(
            IReadOnlyList<CompilationReference> references,
            out ImmutableArray<BoundClass> classes)
    {
        var classNames = new Dictionary<string,
            (int DestinationIndex, int ReferenceIndex, int ClassIndex,
                (string Module, string Key)? Owner)>(StringComparer.Ordinal);
        var classOwners = new Dictionary<(string Module, string Key),
            (int DestinationIndex, int ReferenceIndex, int ClassIndex,
                string Name)>();
        var classBuilder = ImmutableArray.CreateBuilder<BoundClass>();
        var mappings = ImmutableArray.CreateBuilder<ImmutableArray<int>>(
            references.Count);
        var comparisonOrigins = references.Select(
            CanonicalClassComparisonOrigins).ToArray();
        for (var referenceIndex = 0;
            referenceIndex < references.Count; referenceIndex++)
        {
            var reference = references[referenceIndex];
            var mapping = ImmutableArray.CreateBuilder<int>(
                reference.Classes.Length);
            for (var classIndex = 0;
                classIndex < reference.Classes.Length; classIndex++)
            {
                var @class = reference.Classes[classIndex];
                var owner = ClassOwner(reference, classIndex);
                if (classNames.TryGetValue(@class.Name,
                        out var sameName))
                {
                    if (owner is null || sameName.Owner != owner)
                    {
                        throw new ModuleContractException(
                            $"Ambiguous provider class '{@class.Name}'.");
                    }
                    RequireSameClass(sameName.ReferenceIndex,
                        sameName.ClassIndex, referenceIndex, classIndex,
                        owner.Value);
                    mapping.Add(sameName.DestinationIndex);
                    continue;
                }

                if (owner.HasValue
                    && classOwners.TryGetValue(owner.Value,
                        out var sameOwner))
                {
                    if (sameOwner.Name != @class.Name)
                    {
                        throw new ModuleContractException(
                            "One private class export has conflicting lookup names.");
                    }
                    RequireSameClass(sameOwner.ReferenceIndex,
                        sameOwner.ClassIndex, referenceIndex, classIndex,
                        owner.Value);
                    classNames.Add(@class.Name, (
                        sameOwner.DestinationIndex, referenceIndex,
                        classIndex, owner));
                    mapping.Add(sameOwner.DestinationIndex);
                    continue;
                }

                if (classBuilder.Count >= 256)
                {
                    throw new ModuleContractException(
                        "Combined private managed class layouts exceed the supported limit.");
                }
                var destinationIndex = classBuilder.Count;
                classNames.Add(@class.Name, (destinationIndex,
                    referenceIndex, classIndex, owner));
                if (owner.HasValue)
                {
                    classOwners.Add(owner.Value, (destinationIndex,
                        referenceIndex, classIndex, @class.Name));
                }
                mapping.Add(destinationIndex);
                classBuilder.Add(@class);
            }
            mappings.Add(mapping.MoveToImmutable());
        }
        classes = classBuilder.ToImmutable();
        return mappings.MoveToImmutable();

        void RequireSameClass(int leftReferenceIndex, int leftClassIndex,
            int rightReferenceIndex, int rightClassIndex,
            (string Module, string Key) owner)
        {
            var left = references[leftReferenceIndex];
            var right = references[rightReferenceIndex];
            if (!SameClass(
                    left.Classes[leftClassIndex],
                    right.Classes[rightClassIndex],
                    left.Structures, right.Structures,
                    left.Classes, right.Classes,
                    left.NativeFunctionPointerSignatures,
                    right.NativeFunctionPointerSignatures,
                    left.CheckedReferenceTypes,
                    right.CheckedReferenceTypes,
                    left.NativePointerTypes, right.NativePointerTypes,
                    left.NativeFixedArrayTypes,
                    right.NativeFixedArrayTypes,
                    comparisonOrigins[leftReferenceIndex],
                    comparisonOrigins[rightReferenceIndex],
                    left.EnumDefinitions, right.EnumDefinitions))
            {
                throw new ModuleContractException(
                    $"Private class '{owner.Key}' differs across its selected snapshots.");
            }
        }

        static (string Module, string Key)? ClassOwner(
            CompilationReference reference, int classIndex)
        {
            var owners = new HashSet<(string Module, string Key)>();
            foreach (var origin in reference.PrivateNominalTypeOrigins)
            {
                if (!ManagedClassTypes.IsClass(origin.PrivateType)
                    || ManagedClassTypes.ClassIndex(origin.PrivateType)
                        != classIndex)
                {
                    continue;
                }
                (string Module, string Key) owner
                    = origin.OriginKind switch
                {
                    BoundPrivateNominalOriginKind.OwnedDefinition
                        => (origin.UsingModuleIdentity,
                            origin.LocalDeclarationKey),
                    BoundPrivateNominalOriginKind.ImportedExpectation
                        when origin.SelectedModuleIdentity is string module
                            && origin.SelectedExportKey is string key
                        => (module, key),
                    _ => throw new ModuleContractException(
                        "A private class reference has no selected logical owner."),
                };
                _ = owners.Add(owner);
            }
            if (owners.Count > 1)
            {
                throw new ModuleContractException(
                    "One private class row selects conflicting logical owners.");
            }
            return owners.Count == 0 ? null : owners.Single();
        }

        static ImmutableArray<BoundPrivateNominalTypeOrigin>
            CanonicalClassComparisonOrigins(
                CompilationReference reference)
        {
            var origins = ImmutableArray.CreateBuilder<
                BoundPrivateNominalTypeOrigin>(
                    reference.PrivateNominalTypeOrigins.Length);
            foreach (var origin in reference.PrivateNominalTypeOrigins)
            {
                if (origin.OriginKind
                    != BoundPrivateNominalOriginKind.ImportedExpectation)
                {
                    origins.Add(origin);
                    continue;
                }
                if (origin.SelectedModuleIdentity is not string module
                    || origin.SelectedExportKey is not string key)
                {
                    throw new ModuleContractException(
                        "A private class expectation has no selected owner.");
                }
                origins.Add(origin with {
                    OriginKind
                        = BoundPrivateNominalOriginKind.OwnedDefinition,
                    IsExported = true,
                    UsingModuleIdentity = module,
                    LocalDeclarationKey = key,
                    SelectedModuleIdentity = null,
                    SelectedExportKey = null,
                });
            }
            return origins.MoveToImmutable();
        }
    }

    private static PrimitiveType RemapCheckedReferenceType(PrimitiveType type,
        int[] mapping)
    {
        if (!CheckedReferenceTypes.IsType(type))
        {
            return type;
        }
        var index = CheckedReferenceTypes.Index(type);
        if ((uint)index >= (uint)mapping.Length || mapping[index] < 0)
        {
            throw new ModuleContractException(
                "A checked-reference type has no merged provider identity.");
        }
        return CheckedReferenceTypes.Type(mapping[index]);
    }
    private static CompilationReference SetReference(Module module)
    {
        var declarations = ImportDeclarations(module);
        var additionalTypes = PublicStructureNominalRoots(
                module.NominalTypeOrigins, module.Structures)
            .Concat(PublicEnumNominalRoots(module.NominalTypeOrigins,
                module.EnumDefinitions))
            .Concat(Private105NativeAddressContractRoots(
                module.Functions.Where(static item => item.Import),
                module.NativeFunctionPointerSignatures));
        var classes = ProjectPrivateClassDefinitions(module.Classes,
            module.NominalTypeOrigins, out var classMapping);
        var projection = ProjectNativeContracts(declarations, module.Structures,
            module.NativePointerTypes,
            module.NativeFunctionPointerSignatures,
            module.NativeFixedArrayTypes,
            module.CheckedReferenceTypes,
            classes, additionalTypes,
            enumDefinitions: module.EnumDefinitions);
        var checkedReferenceMapping
            = projection.CheckedReferenceMapping.ToArray();
        var structures = SelectStructures(module.Structures, projection.RequiredStructures);
        structures = [.. structures.Select(structure =>
            BoundTreeTraversal.RewriteTypes(RemapNativePointerTypes(
                RemapNativeFunctionPointerTypes(
                    RemapNativeFixedArrayTypes(structure,
                        projection.FixedArrayMapping),
                    projection.FunctionPointerMapping),
                projection.PointerMapping),
                type => RemapEnumType(
                    RemapCheckedReferenceType(type,
                        checkedReferenceMapping),
                    projection.EnumMapping)))];
        classes = [.. classes.Select(@class =>
            BoundTreeTraversal.RewriteTypes(RemapNativePointerTypes(
                RemapNativeFunctionPointerTypes(
                    RemapNativeFixedArrayTypes(
                        RemapClass(@class, module.Structures, structures),
                        projection.FixedArrayMapping),
                    projection.FunctionPointerMapping),
                projection.PointerMapping),
                type => RemapEnumType(
                    RemapCheckedReferenceType(type,
                        checkedReferenceMapping),
                    projection.EnumMapping)))];
        var remapped = ImmutableArray.CreateBuilder<BoundFunction>(declarations.Length);
        foreach (var declaration in declarations)
        {
            var mapped = RemapEnumTypes(
                RemapFunction(declaration, module.Structures, structures),
                IdentityEnumMapping(module.EnumDefinitions.Length));
            mapped = RemapNativeFixedArrayTypes(mapped,
                projection.FixedArrayMapping);
            mapped = RemapNativeFunctionPointerTypes(mapped, projection.FunctionPointerMapping);
            mapped = RemapNativePointerTypes(mapped,
                projection.PointerMapping);
            mapped = BoundTreeTraversal.RewriteTypes(mapped,
                type => RemapEnumType(
                    RemapCheckedReferenceType(type,
                        checkedReferenceMapping),
                    projection.EnumMapping));
            remapped.Add(mapped);
        }
        var nominalTypeOrigins = ProjectPrivateNominalTypeOrigins(
            module.NominalTypeOrigins, module.Structures, structures,
            classMapping, module.EnumDefinitions,
            projection.EnumDefinitions, projection.EnumMapping);
        var privateClassReferences = CreatePrivateClassReferences(
            module.Structures, module.Classes, module.CheckedReferenceTypes,
            module.NativeFunctionPointerSignatures,
            module.NativePointerTypes, module.NativeFixedArrayTypes,
            module.NominalTypeOrigins, classMapping,
            moduleFunctions: module.Functions,
            nativePointerMapping: projection.PointerMapping,
            functionPointerMapping: projection.FunctionPointerMapping,
            fixedArrayMapping: projection.FixedArrayMapping,
            checkedReferenceMapping:
                projection.CheckedReferenceMapping,
            enumDefinitions: module.EnumDefinitions,
            enumMapping: projection.EnumMapping);
        return CreateProjectedReference(structures, remapped.MoveToImmutable(),
            module.RuntimePolicy, module.RequiresSourceGrant,
            module.InvocationProfileId, projection.FunctionPointerSignatures,
            projection.PointerTypes, projection.FixedArrayTypes,
            projection.CheckedReferenceTypes,
            nominalTypeOrigins, classes, privateClassReferences,
            enumDefinitions: projection.EnumDefinitions);
    }

    private static Module[] ReadSet(IReadOnlyList<byte[]> producers)
    {
        ArgumentNullException.ThrowIfNull(producers);
        if (producers.Count is < 2 or > 16)
        {
            throw new ModuleContractException("A native module set requires 2 to 16 producers.");
        }
        var modules = new Module[producers.Count];
        for (var i = 0; i < modules.Length; i++)
        {
            modules[i] = Read(producers[i], expectedConsumer: false);
            if (modules[i].RequiresSourceGrant)
            {
                throw new ModuleContractException("Native module sets do not support Host.Source requirements.");
            }
            ValidateProducer(modules[i]);
            if (modules[i].RuntimePolicy != modules[0].RuntimePolicy)
            {
                throw new ModuleContractException("Native module set runtime policies differ.");
            }
        }
        return modules;
    }

    /// <summary>Persists only the consumer and selected imports; no provider implementation is embedded.</summary>
    internal static byte[] CreateConsumerSet(BoundProgram program,
        IReadOnlyList<byte[]> producers) =>
        CreateConsumerSet(program, producers, identityInputs: null);

    internal static byte[] CreateConsumerSet(BoundProgram program,
        IReadOnlyList<byte[]> producers, PrivateModuleIdentityInputs? identityInputs)
    {
        var reference = CreateReferenceSet(producers);
        return CreateConsumerAgainstReference(program, reference,
            allowManagedParticipant: true, identityInputs: identityInputs);
    }

    internal static byte[] CreateConsumerAgainstReference(BoundProgram program, CompilationReference reference,
        bool allowManagedParticipant = false,
        PrivateModuleIdentityInputs? identityInputs = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (identityInputs is not null && !identityInputs.IsFor(program))
        {
            throw new ArgumentException(
                "Private nominal identity inputs were captured for another bound-program snapshot.",
                nameof(identityInputs));
        }
        var sourceProgram = program;
        program = AddPrivateNativeAddressFunctionPointerSignatures(
            program);
        SemanticVerifier.VerifyReferences(program);
        program = CanonicalizePrivateImplicitReferences(program);
        if (!ReferenceEquals(sourceProgram, program))
        {
            SemanticVerifier.VerifyReferences(program);
            if (identityInputs is not null)
            {
                identityInputs = identityInputs.Rebind(sourceProgram,
                    program);
            }
        }
        ValidateAddressOnlyNestedBacking(program);
        _ = MapNativeFunctionPointerTypes(
            program.NativeFunctionPointerSignatures, reference.NativeFunctionPointerSignatures,
            program.Structures, reference.Structures, program.NativePointerTypes,
            reference.NativePointerTypes, program.NativeFixedArrayTypes,
            reference.NativeFixedArrayTypes);
        _ = MapNativePointerTypes(program.NativePointerTypes, program.Structures,
            program.NativeFunctionPointerSignatures, reference.NativePointerTypes, reference.Structures,
            reference.NativeFunctionPointerSignatures,
            program.NativeFixedArrayTypes, reference.NativeFixedArrayTypes);
        SemanticVerifier.RejectSourceNativeImports(program, "semantic module sets",
            allowDirectNativeImports: true, allowFixedArrayStorage: true);
        if (reference.RequiresSourceGrant)
        {
            throw new ModuleContractException("Native module sets do not support Host.Source requirements.");
        }
        RejectCompilationLocalSpans(program);
        if (ContainsUnsupportedModuleCallerUnsafeContracts(program))
        {
            throw new ModuleContractException("Module contains an unsupported caller-unsafe member contract.");
        }
        if (!allowManagedParticipant)
        {
            RejectManagedNodes(program);
        }
        if (!program.Buffers.IsDefaultOrEmpty)
        {
            throw new ModuleFormatException("Host buffers are not serializable module dependencies.");
        }
        if (program.RuntimePolicy != reference.RuntimePolicy)
        {
            throw new ModuleContractException("Consumer and producer checked-failure policies differ.");
        }
        var privateNominalRoots = PrivateNominalInputRoots(program,
            identityInputs);
        IReadOnlyDictionary<string, BoundFunction>? available = null;
        var hasPrivateManagedState = HasPrivateManagedState(program);
        var declarations = ImmutableArray.CreateBuilder<ModuleFunction>(program.Functions.Length);
        foreach (var function in program.Functions)
        {
            if (function.IsExternal)
            {
                if (function.DirectNativeImport is not null && !function.IsModuleReference)
                {
                    if (!DirectNativeImportContract.IsValidPrivateWindows(function, program.Structures,
                        program.NativeFunctionPointerSignatures, program.NativePointerTypes)
                        || reference.Functions.Any(export => export.Name == function.Name))
                    {
                        throw new ModuleContractException(
                            $"Direct native import '{function.Name}' is invalid or ambiguous.");
                    }
                    declarations.Add(new ModuleFunction(function.Name,
                        Contract(function, program.Structures, program.NativeFunctionPointerSignatures,
                            program.NativePointerTypes,
                            program.NativeFixedArrayTypes),
                        function, false) {
                        HasOwnershipTransfer = Compilation.HasOwnershipTransfer(function, program.Structures),
                        NativeOutputRole = function.NativeOutputRole,
                        NativeOutputContracts = function.NativeOutputContracts,
                    });
                    continue;
                }
                available ??= reference.Functions.IsEmpty
                    ? ImmutableDictionary<string, BoundFunction>.Empty
                    : reference.Functions.ToDictionary(static item => item.Name, StringComparer.Ordinal);
                if (hasPrivateManagedState && !ManagedExportShape.Supported(function, program.Structures))
                {
                    throw new ModuleContractException($"Managed graph import '{function.Name}' crosses a non-Copy boundary.");
                }
                if (!available.TryGetValue(function.Name, out var export))
                {
                    throw new ModuleContractException($"Incompatible or missing provider declaration '{function.Name}'.");
                }
                var contract = Contract(function, program.Structures, program.NativeFunctionPointerSignatures,
                    program.NativePointerTypes, program.NativeFixedArrayTypes);
                var expectedDirectNativeImport
                    = ExpectedDirectNativeImport(function);
                var expectedCopyOriginSummary =
                    GetPrivate105CopyOriginSummaryData(function,
                        program.Structures,
                        program.CheckedReferenceTypes);
                var expectedRawUnionReturnCoverage
                    = function.RawUnionReturnCoverage;
                if (!CompatibleContract(contract,
                    Contract(export, reference.Structures, reference.NativeFunctionPointerSignatures,
                        reference.NativePointerTypes,
                        reference.NativeFixedArrayTypes))
                    || !SameNativeOutputContracts(function.NativeOutputRole,
                        function.NativeOutputContracts, export.NativeOutputRole,
                        export.NativeOutputContracts, program.Structures, reference.Structures,
                        program.NativeFunctionPointerSignatures, program.NativePointerTypes,
                        reference.NativeFunctionPointerSignatures, reference.NativePointerTypes)
                    || !SameDirectNativeImportDescriptor(
                        expectedDirectNativeImport, program.Structures,
                        program.NativeFunctionPointerSignatures,
                        program.NativePointerTypes, export.DirectNativeImport,
                        reference.Structures,
                        reference.NativeFunctionPointerSignatures,
                        reference.NativePointerTypes)
                    || !Private105CopyOriginSummarySatisfies(
                        expectedCopyOriginSummary,
                        GetPrivate105CopyOriginSummaryData(export,
                            reference.Structures,
                            reference.CheckedReferenceTypes))
                    || !Private106RawUnionReturnCoverageSatisfies(
                        expectedRawUnionReturnCoverage,
                        function.ReturnType, program,
                        export.RawUnionReturnCoverage, export.ReturnType,
                        CreatePrivate106CoverageProgram(reference))
                    || !CompatibleImportedNativeVTableMethod(
                        function.ModuleImport is null ? null : function.NativeVTableMethod,
                        export.NativeVTableMethod))
                {
                    throw new ModuleContractException($"Incompatible or missing provider declaration '{function.Name}'.");
                }
                var importedNativeVTableMethod = function.ModuleImport is null
                    ? null : function.NativeVTableMethod;
                var hasOwnershipTransfer = Compilation.HasOwnershipTransfer(function, program.Structures);
                if (hasOwnershipTransfer != Compilation.HasOwnershipTransfer(export, reference.Structures))
                {
                    throw new ModuleContractException($"Incompatible or missing provider declaration '{function.Name}'.");
                }
                declarations.Add(new ModuleFunction(function.Name, contract, function, true) {
                    HasOwnershipTransfer = hasOwnershipTransfer,
                    ImportedNativeVTableMethod = importedNativeVTableMethod,
                    NativeOutputRole = function.NativeOutputRole,
                    NativeOutputContracts = function.NativeOutputContracts,
                    ExpectedDirectNativeImport = expectedDirectNativeImport,
                    ExpectedCopyOriginSummaryData =
                        expectedCopyOriginSummary,
                    HasDeclaredRawUnionReturnCoverage
                        = function.HasRawUnionReturnCoverage,
                    DeclaredRawUnionReturnCoverage
                        = expectedRawUnionReturnCoverage,
                });
            }
            else
            {
                declarations.Add(new ModuleFunction(function.Name,
                    Contract(function, program.Structures, program.NativeFunctionPointerSignatures,
                        program.NativePointerTypes,
                        program.NativeFixedArrayTypes), function, false) {
                    HasOwnershipTransfer = Compilation.HasOwnershipTransfer(function, program.Structures),
                    NativeOutputRole = function.NativeOutputRole,
                    NativeOutputContracts = function.NativeOutputContracts,
                });
            }
        }
        var declarationArray = declarations.MoveToImmutable();
        var required = RequiredStructures(program.Functions, program.Structures,
            classes: program.Classes,
            nativePointerTypes: program.NativePointerTypes,
            nativeFunctionPointerSignatures: program.NativeFunctionPointerSignatures,
            nativeFixedArrayTypes: program.NativeFixedArrayTypes,
            checkedReferenceTypes: program.CheckedReferenceTypes,
            additionalTypes: privateNominalRoots);
        var structures = SelectStructures(program.Structures, required);
        var nativePointerTypes = RemapNativePointerTypeDefinitions(program.NativePointerTypes,
            program.Structures, structures);
        var nativeFunctionPointerSignatures
            = RemapNativeFunctionPointerSignatureTypes(
                program.NativeFunctionPointerSignatures,
                program.Structures, structures);
        var nativeFixedArrayTypes = RemapNativeFixedArrayTypeDefinitions(
            program.NativeFixedArrayTypes, program.Structures, structures);
        var referenceStructures = IndexStructures(reference.Structures);
        var imported = ImmutableArray.CreateBuilder<string>(structures.Length);
        foreach (var structure in structures)
        {
            if (referenceStructures.TryGetValue(structure.Name, out var match))
            {
                if (structure.IsPrivate || structure.IsInternal
                    || !SameStructure(structure, match, structures, reference.Structures,
                        program.NativeFunctionPointerSignatures, reference.NativeFunctionPointerSignatures,
                        nativePointerTypes, reference.NativePointerTypes,
                        nativeFixedArrayTypes, reference.NativeFixedArrayTypes,
                        program.Classes, reference.Classes))
                {
                    throw new ModuleContractException($"Incompatible imported value layout '{structure.Name}'.");
                }
                imported.Add(structure.Name);
            }
        }
        RejectImportedTypeMembers(declarationArray, imported);
        var functions = ImmutableArray.CreateBuilder<ModuleFunction>(declarationArray.Length);
        foreach (var declaration in declarationArray)
        {
            functions.Add(declaration with {
                Function = declaration.Import ? null : RemapFunction(declaration.Function
                    ?? throw new ModuleFormatException("Missing consumer function."), program.Structures, structures),
                NativeOutputContracts = RemapNativeOutputContracts(
                    declaration.NativeOutputContracts, program.Structures, structures),
                ExpectedDirectNativeImport =
                    RemapExpectedDirectNativeImport(
                        declaration.ExpectedDirectNativeImport,
                        program.Structures, structures),
            });
        }
        var classes = ImmutableArray.CreateBuilder<BoundClass>(program.Classes.Length);
        foreach (var @class in program.Classes)
        {
            classes.Add(RemapClass(@class, program.Structures, structures));
        }
        var module = new Module(true, program.EntryPoint, structures, functions.MoveToImmutable()) {
            Classes = classes.MoveToImmutable(),
            ImportedStructures = imported.ToImmutable(),
            NativeFunctionPointerSignatures = nativeFunctionPointerSignatures,
            NativePointerTypes = nativePointerTypes,
            NativeFixedArrayTypes = nativeFixedArrayTypes,
            CheckedReferenceTypes = RemapCheckedReferenceTypeDefinitions(
                program.CheckedReferenceTypes, program.Structures,
                structures),
            RuntimePolicy = program.RuntimePolicy,
        };
        module = module with {
            NominalTypeOrigins = BuildPrivateNominalTypeOrigins(sourceProgram,
                module, identityInputs),
        };
        var result = Write(module, program);
        ValidateConsumerAgainstReference(result, reference);
        return result;
    }

    /// <summary>Checks retained consumer contracts against each selected, independently verified leaf provider.</summary>
    internal static void ValidateConsumerSet(byte[] consumerBytes, IReadOnlyList<byte[]> producerBytes)
    {
        var reference = CreateReferenceSet(producerBytes);
        ValidateConsumerAgainstReference(consumerBytes, reference);
    }

    internal static void ValidateConsumerAgainstReference(byte[] consumerBytes, CompilationReference reference)
    {
        _ = RestoreConsumerAgainstReference(consumerBytes, reference);
    }

    internal static BoundProgram RestoreConsumerAgainstReference(byte[] consumerBytes, CompilationReference reference)
    {
        var consumer = Read(consumerBytes, expectedConsumer: true);
        return RestoreConsumerAgainstReference(consumer, reference);
    }

    private static BoundProgram RestoreConsumerAgainstReference(Module consumer,
        CompilationReference reference, bool allowPrivateVTableSlotChanges = false)
    {
        var providerFunctionPointerMapping = MapNativeFunctionPointerTypes(
            consumer.NativeFunctionPointerSignatures, reference.NativeFunctionPointerSignatures,
            consumer.Structures, reference.Structures, consumer.NativePointerTypes,
            reference.NativePointerTypes, consumer.NativeFixedArrayTypes,
            reference.NativeFixedArrayTypes,
            allowUnmatchedSourceSignatures: true);
        var providerNativePointerMapping = MapNativePointerTypes(consumer.NativePointerTypes,
            consumer.Structures, consumer.NativeFunctionPointerSignatures, reference.NativePointerTypes,
            reference.Structures, reference.NativeFunctionPointerSignatures,
            consumer.NativeFixedArrayTypes, reference.NativeFixedArrayTypes);
        if (consumer.RuntimePolicy != reference.RuntimePolicy)
        {
            throw new ModuleContractException("Consumer and provider runtime policies differ.");
        }
        if (consumer.RequiresSourceGrant != reference.RequiresSourceGrant)
        {
            throw new ModuleContractException("Consumer and provider Host.Source requirements differ.");
        }
        if (consumer.InvocationProfileId != reference.InvocationProfileId)
        {
            throw new ModuleContractException("Consumer and provider invocation profiles differ.");
        }
        ValidatePrivateNominalImportsAgainstReference(consumer,
            reference);
        IReadOnlyDictionary<string, BoundFunction>? exports = null;
        var hasPrivateManagedState = HasPrivateManagedState(consumer);
        var hasUnsupportedManagedImport = false;
        foreach (var imported in consumer.Functions)
        {
            if (!imported.Import)
            {
                continue;
            }
            exports ??= reference.Functions.IsEmpty
                ? ImmutableDictionary<string, BoundFunction>.Empty
                : reference.Functions.ToDictionary(static function => function.Name, StringComparer.Ordinal);
            if (!exports.TryGetValue(imported.Name, out var actual))
            {
                throw new ModuleContractException(
                    $"Incompatible or missing provider contract for '{imported.Name}'.");
            }

            var actualContract = Contract(actual, reference.Structures,
                reference.NativeFunctionPointerSignatures,
                reference.NativePointerTypes,
                reference.NativeFixedArrayTypes,
                private105: consumer.FormatVersion
                    == CheckedReferenceVersion);
            if (consumer.FormatVersion == CheckedReferenceVersion)
            {
                actualContract = RemapPrivate105FunctionPointerReferences(
                    actualContract, providerFunctionPointerMapping);
            }
            var actualReturnTypeInConsumer = actual.ReturnType;
            if ((int)actualReturnTypeInConsumer is >= 100
                and < (int)BoundEnumTypes.First)
            {
                actualReturnTypeInConsumer = RemapType(
                    actualReturnTypeInConsumer, reference.Structures,
                    consumer.Structures);
            }
            if (!CompatibleContract(imported.Contract, actualContract))
            {
                throw new ModuleContractException(
                    $"Private function contract mismatch for '{imported.Name}': expected '{imported.Contract}', actual '{actualContract}'.");
            }
            if ((consumer.FormatVersion == CheckedReferenceVersion
                    ? !SameDirectNativeImportDescriptor(
                        imported.ExpectedDirectNativeImport,
                        consumer.Structures,
                        consumer.NativeFunctionPointerSignatures,
                        consumer.NativePointerTypes,
                        actual.DirectNativeImport,
                        reference.Structures,
                        reference.NativeFunctionPointerSignatures,
                        reference.NativePointerTypes)
                    : actual.DirectNativeImport is not null
                        && !imported.Contract.Contains(
                            "|DirectNativeImport:",
                            StringComparison.Ordinal))
                || (consumer.FormatVersion == CheckedReferenceVersion
                    && !Private105CopyOriginSummarySatisfies(
                        imported.ExpectedCopyOriginSummaryData,
                        GetPrivate105CopyOriginSummaryData(actual,
                            reference.Structures,
                            reference.CheckedReferenceTypes)))
                || (consumer.FormatVersion == CheckedReferenceVersion
                    && (!imported.HasDeclaredRawUnionReturnCoverage
                        || !Private106RawUnionReturnCoverageSatisfies(
                        imported.DeclaredRawUnionReturnCoverage,
                        actualReturnTypeInConsumer,
                        CreatePrivate106CoverageProgram(consumer),
                        actual.RawUnionReturnCoverage,
                        actual.ReturnType,
                        CreatePrivate106CoverageProgram(reference))))
                || !SameNativeOutputContracts(imported.NativeOutputRole,
                    imported.NativeOutputContracts, actual.NativeOutputRole,
                    actual.NativeOutputContracts, consumer.Structures, reference.Structures,
                    consumer.NativeFunctionPointerSignatures, consumer.NativePointerTypes,
                    reference.NativeFunctionPointerSignatures, reference.NativePointerTypes)
                || !(allowPrivateVTableSlotChanges
                    ? CompatibleImportedNativeVTableMethodForPrivateReplacement(
                        imported.ImportedNativeVTableMethod,
                        actual.NativeVTableMethod)
                    : CompatibleImportedNativeVTableMethod(
                        imported.ImportedNativeVTableMethod,
                        actual.NativeVTableMethod))
                || imported.HasOwnershipTransfer != Compilation.HasOwnershipTransfer(actual, reference.Structures))
            {
                throw new ModuleContractException($"Incompatible or missing provider contract for '{imported.Name}'.");
            }
            if (hasPrivateManagedState && !hasUnsupportedManagedImport
                && !ManagedExportShape.Supported(actual, reference.Structures))
            {
                hasUnsupportedManagedImport = true;
            }
        }
        var providerStructures = consumer.Structures.IsEmpty && consumer.ImportedStructures.IsEmpty
            ? null
            : IndexStructures(reference.Structures);
        var importedNames = new HashSet<string>(consumer.ImportedStructures.Length, StringComparer.Ordinal);
        foreach (var name in consumer.ImportedStructures)
        {
            if (providerStructures is null || !providerStructures.ContainsKey(name))
            {
                throw new ModuleContractException("Missing imported value layout.");
            }
            _ = importedNames.Add(name);
        }
        var classSnapshotOnlyStructures
            = PrivateClassSnapshotOnlyStructureNames(consumer);
        foreach (var expected in consumer.Structures)
        {
            if (providerStructures is null
                || !providerStructures.TryGetValue(expected.Name,
                    out var actual))
            {
                continue;
            }
            if (!importedNames.Contains(expected.Name))
            {
                if (!classSnapshotOnlyStructures.Contains(expected.Name))
                {
                    throw new ModuleContractException(
                        $"Incompatible provider value layout '{expected.Name}'.");
                }
                continue;
            }
            if (!SameStructure(expected, actual, consumer.Structures,
                reference.Structures,
                consumer.NativeFunctionPointerSignatures,
                reference.NativeFunctionPointerSignatures,
                consumer.NativePointerTypes, reference.NativePointerTypes,
                consumer.NativeFixedArrayTypes,
                reference.NativeFixedArrayTypes, consumer.Classes,
                reference.Classes))
            {
                throw new ModuleContractException(
                    $"Incompatible provider value layout '{expected.Name}'.");
            }
        }
        RejectImportedTypeMembers(consumer.Functions, importedNames);
        if (hasPrivateManagedState && hasUnsupportedManagedImport)
        {
            throw new ModuleContractException("Managed graph import crosses a non-Copy boundary.");
        }
        var providerFunctions = exports ?? ImmutableDictionary<string, BoundFunction>.Empty;
        var names = new Dictionary<string, int>(consumer.Functions.Length, StringComparer.Ordinal);
        for (var index = 0; index < consumer.Functions.Length; index++)
        {
            names.Add(consumer.Functions[index].Name, index);
        }
        var functions = ImmutableArray.CreateBuilder<BoundFunction>(consumer.Functions.Length);
        foreach (var item in consumer.Functions)
        {
            functions.Add(item.Import
                ? RemapNativePointerTypes(
                    RemapNativeFunctionPointerTypes(
                        RemapFunction(RecordedImportPromises(item.Contract, providerFunctions[item.Name]),
                        reference.Structures, consumer.Structures),
                        providerFunctionPointerMapping),
                    providerNativePointerMapping)
                : item.Function ?? throw new ModuleFormatException("Missing consumer body."));
        }
        var program = new BoundProgram(functions.MoveToImmutable(), consumer.EntryPoint) {
            Structures = consumer.Structures,
            Classes = consumer.Classes,
            NativeFunctionPointerSignatures = consumer.NativeFunctionPointerSignatures,
            NativePointerTypes = consumer.NativePointerTypes,
            NativeFixedArrayTypes = consumer.NativeFixedArrayTypes,
            CheckedReferenceTypes = consumer.CheckedReferenceTypes,
            EnumDefinitions = consumer.EnumDefinitions,
            PrivateNominalTypeOrigins = consumer.NominalTypeOrigins,
            PrivateClassReferences = CreatePrivateClassReferences(
                consumer.Structures, consumer.Classes,
                consumer.CheckedReferenceTypes,
                consumer.NativeFunctionPointerSignatures,
                consumer.NativePointerTypes,
                consumer.NativeFixedArrayTypes,
                consumer.NominalTypeOrigins,
                moduleFunctions: consumer.Functions,
                isCurrentSnapshot: true,
                enumDefinitions: consumer.EnumDefinitions),
            RuntimePolicy = consumer.RuntimePolicy,
        };
        if (ContainsUnsupportedModuleCallerUnsafeContracts(program))
        {
            throw new ModuleFormatException("Consumer contains an unsupported caller-unsafe contract.");
        }
        try
        {
            var resolved = Resolve(program, names);
            SemanticVerifier.VerifyReferences(resolved);
            return resolved;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ModuleFormatException("Consumer module IR failed semantic verification.", exception);
        }
    }

    internal static ImmutableArray<string> GetConsumerSetDeclarations(byte[] consumerBytes)
    {
        return ReadConsumerContractSnapshot(consumerBytes).Declarations;
    }

    private static Dictionary<string, BoundStruct> IndexStructures(IReadOnlyCollection<BoundStruct> structures)
    {
        var result = new Dictionary<string, BoundStruct>(structures.Count, StringComparer.Ordinal);
        foreach (var structure in structures)
        {
            _ = result.TryAdd(structure.Name, structure);
        }
        return result;
    }

    private static void RejectImportedTypeMembers(IEnumerable<ModuleFunction> functions,
        IEnumerable<string> importedTypes)
    {
        RejectImportedTypeMembers(functions, importedTypes.ToHashSet(StringComparer.Ordinal));
    }

    private static void RejectImportedTypeMembers(IEnumerable<ModuleFunction> functions,
        HashSet<string> imported)
    {
        foreach (var item in functions)
        {
            if (!item.Import && item.Function?.DeclaringStruct is string owner && imported.Contains(owner))
            {
                throw new ModuleContractException("Consumer cannot define members of an imported value type.");
            }
        }
    }
}
