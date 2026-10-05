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
        foreach (var reference in references)
        {
            if (reference.RuntimePolicy != references[0].RuntimePolicy)
            {
                throw new ModuleContractException("Native module set runtime policies differ.");
            }
            typeCount = checked(typeCount + reference.Structures.Length);
            functionCount = checked(functionCount + reference.Functions.Length);
        }
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
        var typeIndex = 0;
        foreach (var reference in references)
        {
            foreach (var structure in reference.Structures)
            {
                types[typeIndex++] = RemapStructure(structure, reference.Structures, types, destinationTypes);
            }
        }
        var allTypes = types.MoveToImmutable();
        foreach (var reference in references)
        {
            foreach (var function in reference.Functions)
            {
                if (!names.Add(function.Name))
                {
                    throw new ModuleContractException($"Ambiguous provider export '{function.Name}'.");
                }
                functions.Add(RemapFunction(function, reference.Structures, allTypes,
                    destinationTypes: destinationTypes));
            }
        }
        return new CompilationReference(allTypes, functions.MoveToImmutable(), references[0].RuntimePolicy);
    }
    private static CompilationReference SetReference(Module module)
    {
        var declarations = ImportDeclarations(module);
        var required = RequiredStructures(declarations, module.Structures);
        var structures = SelectStructures(module.Structures, required);
        var remapped = ImmutableArray.CreateBuilder<BoundFunction>(declarations.Length);
        foreach (var declaration in declarations)
        {
            remapped.Add(RemapFunction(declaration, module.Structures, structures));
        }
        return new CompilationReference(structures, remapped.MoveToImmutable(), module.RuntimePolicy);
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
        RejectCompilationLocalSpans(program);
        if (ContainsUnsupportedModuleCallerUnsafeContracts(program))
        {
            throw new ModuleContractException("Only top-level caller-unsafe functions are supported in persisted semantic modules.");
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
                var contract = Contract(function, program.Structures);
                if (!CompatibleContract(contract, Contract(export, reference.Structures)))
                {
                    throw new ModuleContractException($"Incompatible or missing provider declaration '{function.Name}'.");
                }
                var hasOwnershipTransfer = Compilation.HasOwnershipTransfer(function, program.Structures);
                if (hasOwnershipTransfer != Compilation.HasOwnershipTransfer(export, reference.Structures))
                {
                    throw new ModuleContractException($"Incompatible or missing provider declaration '{function.Name}'.");
                }
                declarations.Add(new ModuleFunction(function.Name, contract, function, true) {
                    HasOwnershipTransfer = hasOwnershipTransfer,
                });
            }
            else
            {
                declarations.Add(new ModuleFunction(function.Name, Contract(function, program.Structures), function, false) {
                    HasOwnershipTransfer = Compilation.HasOwnershipTransfer(function, program.Structures),
                });
            }
        }
        var declarationArray = declarations.MoveToImmutable();
        var required = RequiredStructures(program.Functions, program.Structures);
        var structures = SelectStructures(program.Structures, required);
        var referenceStructures = IndexStructures(reference.Structures);
        var imported = ImmutableArray.CreateBuilder<string>(structures.Length);
        foreach (var structure in structures)
        {
            if (referenceStructures.TryGetValue(structure.Name, out var match))
            {
                if (structure.IsPrivate || structure.IsInternal
                    || !SameStructure(structure, match, structures, reference.Structures))
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

    private static BoundProgram RestoreConsumerAgainstReference(Module consumer, CompilationReference reference)
    {
        if (consumer.RuntimePolicy != reference.RuntimePolicy)
        {
            throw new ModuleContractException("Consumer and provider runtime policies differ.");
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
                || !CompatibleContract(imported.Contract, Contract(actual, reference.Structures))
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
        var importedNames = new HashSet<string>(StringComparer.Ordinal);
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
                || !SameStructure(expected, actual, consumer.Structures, reference.Structures)))
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
                ? RemapFunction(RecordedImportPromises(item.Contract, providerFunctions[item.Name]),
                    reference.Structures, consumer.Structures)
                : item.Function ?? throw new ModuleFormatException("Missing consumer body."));
        }
        var program = new BoundProgram(functions.MoveToImmutable(), consumer.EntryPoint) {
            Structures = consumer.Structures,
            Classes = consumer.Classes,
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

    private static Dictionary<string, BoundStruct> IndexStructures(IEnumerable<BoundStruct> structures)
    {
        var result = new Dictionary<string, BoundStruct>(StringComparer.Ordinal);
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
        if (functions.Any(item => !item.Import && item.Function?.DeclaringStruct is string owner
            && imported.Contains(owner)))
        {
            throw new ModuleContractException("Consumer cannot define members of an imported value type.");
        }
    }
}
