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
        var types = ImmutableArray.CreateBuilder<BoundStruct>();
        var functions = ImmutableArray.CreateBuilder<BoundFunction>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var references = modules.Select(SetReference).ToArray();
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
        var allTypes = types.ToImmutable();
        var typeIndex = 0;
        foreach (var reference in references)
        {
            foreach (var structure in reference.Structures)
            {
                types[typeIndex++] = RemapStructure(structure, reference.Structures, allTypes);
            }
        }
        allTypes = types.ToImmutable();
        foreach (var reference in references)
        {
            foreach (var function in reference.Functions)
            {
                if (!names.Add(function.Name))
                {
                    throw new ModuleContractException($"Ambiguous provider export '{function.Name}'.");
                }
                functions.Add(RemapFunction(function, reference.Structures, allTypes));
            }
        }
        return new CompilationReference(allTypes, functions.ToImmutable(), modules[0].RuntimePolicy);
    }
    private static CompilationReference SetReference(Module module)
    {
        var declarations = module.Functions.Where(static item => item.Import)
            .Select(static item => Declaration(item.Function
                ?? throw new ModuleFormatException("Missing export declaration."))).ToImmutableArray();
        var required = RequiredStructures(declarations, module.Structures);
        var structures = SelectStructures(module.Structures, required);
        return new CompilationReference(structures,
            [.. declarations.Select(function => RemapFunction(function, module.Structures, structures))],
            module.RuntimePolicy);
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
        _ = ReadSet(producers);
        var reference = CreateReferenceSet(producers);
        var result = CreateConsumerAgainstReference(program, reference);
        ValidateConsumerSet(result, producers);
        return result;
    }

    internal static byte[] CreateConsumerAgainstReference(BoundProgram program, CompilationReference reference)
    {
        ArgumentNullException.ThrowIfNull(program);
        SemanticVerifier.VerifyReferences(program);
        if (!program.Buffers.IsDefaultOrEmpty)
        {
            throw new ModuleFormatException("Host buffers are not serializable module dependencies.");
        }
        if (program.RuntimePolicy != reference.RuntimePolicy)
        {
            throw new ModuleContractException("Consumer and producer checked-failure policies differ.");
        }
        var available = reference.Functions.ToDictionary(static function => function.Name, StringComparer.Ordinal);
        var declarations = ImmutableArray.CreateBuilder<ModuleFunction>();
        foreach (var function in program.Functions)
        {
            if (function.IsExternal)
            {
                if (!available.TryGetValue(function.Name, out var export)
                    || !CompatibleContract(Contract(function, program.Structures),
                        Contract(export, reference.Structures))
                    || Compilation.HasOwnershipTransfer(function, program.Structures)
                        != Compilation.HasOwnershipTransfer(export, reference.Structures))
                {
                    throw new ModuleContractException($"Incompatible or missing provider declaration '{function.Name}'.");
                }
                declarations.Add(new ModuleFunction(function.Name, Contract(function, program.Structures), function, true) {
                    HasOwnershipTransfer = Compilation.HasOwnershipTransfer(function, program.Structures),
                });
            }
            else
            {
                declarations.Add(new ModuleFunction(function.Name, Contract(function, program.Structures), function, false) {
                    HasOwnershipTransfer = Compilation.HasOwnershipTransfer(function, program.Structures),
                });
            }
        }
        var required = RequiredStructures(program.Functions.Select(static function =>
            function.IsExternal ? Declaration(function) : function), program.Structures);
        var structures = SelectStructures(program.Structures, required);
        var imported = ImmutableArray.CreateBuilder<string>();
        foreach (var structure in structures)
        {
            var match = reference.Structures.FirstOrDefault(item => item.Name == structure.Name);
            if (match is not null)
            {
                if (structure.IsPrivate || structure.IsInternal
                    || !SameStructure(structure, match, structures, reference.Structures))
                {
                    throw new ModuleContractException($"Incompatible imported value layout '{structure.Name}'.");
                }
                imported.Add(structure.Name);
            }
        }
        RejectImportedTypeMembers(declarations, imported.ToImmutable());
        var functions = declarations.Select(declaration => declaration with {
            Function = declaration.Import ? null : RemapFunction(declaration.Function
                ?? throw new ModuleFormatException("Missing consumer function."), program.Structures, structures),
        }).ToImmutableArray();
        var module = new Module(true, program.EntryPoint, structures, functions) {
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
        _ = ReadSet(producerBytes);
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
        if (consumer.RuntimePolicy != reference.RuntimePolicy)
        {
            throw new ModuleContractException("Consumer and provider runtime policies differ.");
        }
        var exports = reference.Functions.ToDictionary(static function => function.Name, StringComparer.Ordinal);
        foreach (var imported in consumer.Functions.Where(static item => item.Import))
        {
            if (!exports.TryGetValue(imported.Name, out var actual)
                || !CompatibleContract(imported.Contract, Contract(actual, reference.Structures))
                || imported.HasOwnershipTransfer != Compilation.HasOwnershipTransfer(actual, reference.Structures))
            {
                throw new ModuleContractException($"Incompatible or missing provider contract for '{imported.Name}'.");
            }
        }
        if (consumer.ImportedStructures.Any(name => !reference.Structures.Any(item => item.Name == name)))
        {
            throw new ModuleContractException("Missing imported value layout.");
        }
        var importedNames = consumer.ImportedStructures.ToHashSet(StringComparer.Ordinal);
        foreach (var expected in consumer.Structures)
        {
            var actual = reference.Structures.FirstOrDefault(item => item.Name == expected.Name);
            if (actual is not null && (!importedNames.Contains(expected.Name)
                || !SameStructure(expected, actual, consumer.Structures, reference.Structures)))
            {
                throw new ModuleContractException($"Incompatible provider value layout '{expected.Name}'.");
            }
        }
        RejectImportedTypeMembers(consumer.Functions, consumer.ImportedStructures);
        var names = consumer.Functions.Select((item, index) => (item.Name, index))
            .ToDictionary(static item => item.Name, static item => item.index, StringComparer.Ordinal);
        var program = new BoundProgram([.. consumer.Functions.Select(item => item.Import
            ? RemapFunction(RecordedImportPromises(item.Contract, exports[item.Name]),
                reference.Structures, consumer.Structures)
            : item.Function ?? throw new ModuleFormatException("Missing consumer body."))], consumer.EntryPoint) {
            Structures = consumer.Structures,
            RuntimePolicy = consumer.RuntimePolicy,
        };
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
        var consumer = Read(consumerBytes, expectedConsumer: true);
        return [.. consumer.Functions.Where(static item => item.Import).Select(static item => item.Name)
            .Order(StringComparer.Ordinal)];
    }

    private static void RejectImportedTypeMembers(IEnumerable<ModuleFunction> functions,
        IEnumerable<string> importedTypes)
    {
        var imported = importedTypes.ToHashSet(StringComparer.Ordinal);
        if (functions.Any(item => !item.Import && item.Function?.DeclaringStruct is string owner
            && imported.Contains(owner)))
        {
            throw new ModuleContractException("Consumer cannot define members of an imported value type.");
        }
    }
}
