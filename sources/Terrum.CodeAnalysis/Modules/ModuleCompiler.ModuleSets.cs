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
        var nativeFunctionPointerSignatures =
            MergeNativeFunctionPointerSignatures(references, out var functionPointerMappings,
                out var nativePointerMappings);
        var types = ImmutableArray.CreateBuilder<BoundStruct>(typeCount);
        var functions = ImmutableArray.CreateBuilder<BoundFunction>(functionCount);
        var names = new HashSet<string>(checked(typeCount + functionCount), StringComparer.Ordinal);
        foreach (var reference in references)
        {
            foreach (var structure in reference.Structures)
            {
                if (!names.Add(structure.Name))
                {
                    throw new ModuleContractException($"Ambiguous provider type '{structure.Name}'.");
                }
                types.Add(structure);
            }
        }
        var destinationTypes = IndexTypes(types);
        var nativeFixedArrayTypes = MergeNativeFixedArrayTypeDefinitions(
            references, types, nativePointerMappings, functionPointerMappings,
            out var fixedArrayMappings);
        var typeIndex = 0;
        for (var referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
        {
            var reference = references[referenceIndex];
            foreach (var structure in reference.Structures)
            {
                types[typeIndex++] = RemapNativeFunctionPointerTypes(
                    RemapNativePointerTypes(
                        BoundTreeTraversal.RewriteTypes(
                            RemapStructure(structure, reference.Structures, types,
                                destinationTypes),
                            type => RemapNativeFixedArrayType(type,
                                fixedArrayMappings[referenceIndex])),
                        nativePointerMappings[referenceIndex]),
                    functionPointerMappings[referenceIndex]);
            }
        }
        var allTypes = types.MoveToImmutable();
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
                functions.Add(BoundTreeTraversal.RewriteTypes(remappedFunction,
                    type => RemapGenericNominalType(referenceIndex, type)));
            }
        }
        var nativePointerTypes = MergeNativePointerTypeDefinitions(references,
            allTypes, nativePointerMappings, functionPointerMappings,
            fixedArrayMappings);
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
            return RemapNativeFixedArrayType(mapped,
                fixedArrayMappings[referenceIndex]);
        }
    }
    private static CompilationReference SetReference(Module module)
    {
        var declarations = ImportDeclarations(module);
        var projection = ProjectNativeContracts(declarations, module.Structures,
            module.NativePointerTypes,
            module.NativeFunctionPointerSignatures,
            module.NativeFixedArrayTypes);
        var structures = SelectStructures(module.Structures, projection.RequiredStructures);
        structures = [.. structures.Select(structure =>
            RemapNativePointerTypes(
                RemapNativeFunctionPointerTypes(structure, projection.FunctionPointerMapping),
                projection.PointerMapping))];
        var remapped = ImmutableArray.CreateBuilder<BoundFunction>(declarations.Length);
        foreach (var declaration in declarations)
        {
            var mapped = RemapFunction(declaration, module.Structures, structures);
            mapped = RemapNativeFunctionPointerTypes(mapped, projection.FunctionPointerMapping);
            remapped.Add(RemapNativePointerTypes(mapped, projection.PointerMapping));
        }
        return CreateProjectedReference(structures, remapped.MoveToImmutable(),
            module.RuntimePolicy, module.RequiresSourceGrant,
            module.InvocationProfileId, projection.FunctionPointerSignatures,
            projection.PointerTypes, []);
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
    internal static byte[] CreateConsumerSet(BoundProgram program, IReadOnlyList<byte[]> producers)
    {
        var reference = CreateReferenceSet(producers);
        return CreateConsumerAgainstReference(program, reference, allowManagedParticipant: true);
    }

    internal static byte[] CreateConsumerAgainstReference(BoundProgram program, CompilationReference reference,
        bool allowManagedParticipant = false)
    {
        ArgumentNullException.ThrowIfNull(program);
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
        SemanticVerifier.VerifyReferences(program);
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
                if (!CompatibleContract(contract,
                    Contract(export, reference.Structures, reference.NativeFunctionPointerSignatures,
                        reference.NativePointerTypes,
                        reference.NativeFixedArrayTypes))
                    || !SameNativeOutputContracts(function.NativeOutputRole,
                        function.NativeOutputContracts, export.NativeOutputRole,
                        export.NativeOutputContracts, program.Structures, reference.Structures,
                        program.NativeFunctionPointerSignatures, program.NativePointerTypes,
                        reference.NativeFunctionPointerSignatures, reference.NativePointerTypes)
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
            nativePointerTypes: program.NativePointerTypes,
            nativeFunctionPointerSignatures: program.NativeFunctionPointerSignatures,
            nativeFixedArrayTypes: program.NativeFixedArrayTypes);
        var structures = SelectStructures(program.Structures, required);
        var nativePointerTypes = RemapNativePointerTypeDefinitions(program.NativePointerTypes,
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
                        nativeFixedArrayTypes, reference.NativeFixedArrayTypes))
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
            NativeFunctionPointerSignatures = program.NativeFunctionPointerSignatures,
            NativePointerTypes = nativePointerTypes,
            NativeFixedArrayTypes = nativeFixedArrayTypes,
            RuntimePolicy = program.RuntimePolicy,
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
            reference.NativeFixedArrayTypes);
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
            if (!exports.TryGetValue(imported.Name, out var actual)
                || (actual.DirectNativeImport is not null
                    && !imported.Contract.Contains("|DirectNativeImport:", StringComparison.Ordinal))
                || !CompatibleContract(imported.Contract, Contract(actual, reference.Structures,
                    reference.NativeFunctionPointerSignatures,
                    reference.NativePointerTypes,
                    reference.NativeFixedArrayTypes))
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
        foreach (var expected in consumer.Structures)
        {
            if (providerStructures is not null && providerStructures.TryGetValue(expected.Name, out var actual)
                && (!importedNames.Contains(expected.Name)
                || !SameStructure(expected, actual, consumer.Structures, reference.Structures,
                    consumer.NativeFunctionPointerSignatures, reference.NativeFunctionPointerSignatures,
                    consumer.NativePointerTypes, reference.NativePointerTypes,
                    consumer.NativeFixedArrayTypes,
                    reference.NativeFixedArrayTypes)))
            {
                throw new ModuleContractException($"Incompatible provider value layout '{expected.Name}'.");
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
