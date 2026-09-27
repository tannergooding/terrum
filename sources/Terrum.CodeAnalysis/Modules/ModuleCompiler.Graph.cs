// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    internal static CompilationReference MergeGraphReferences(IReadOnlyList<CompilationReference> references,
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? origins = null)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (references.Count is < 1 or > 16)
        {
            throw new ModuleContractException("A graph requires 1 to 16 direct dependencies.");
        }
        if (origins is not null && origins.Count != references.Count)
        {
            throw new ModuleContractException("Graph type origin tables differ from selected providers.");
        }
        var types = ImmutableArray.CreateBuilder<BoundStruct>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var originals = new Dictionary<string, (BoundStruct Type, ImmutableArray<BoundStruct> Table, string? Origin)>(
            StringComparer.Ordinal);
        for (var provider = 0; provider < references.Count; provider++)
        {
            var reference = references[provider];
            if (reference.RuntimePolicy != references[0].RuntimePolicy)
            {
                throw new ModuleContractException("Graph dependency policies differ.");
            }
            foreach (var structure in reference.Structures)
            {
                var origin = origins?[provider].GetValueOrDefault(structure.Name);
                if (originals.TryGetValue(structure.Name, out var existing))
                {
                    if (origin is null || existing.Origin is null || origin != existing.Origin
                        || !SameStructure(structure, existing.Type, reference.Structures, existing.Table))
                    {
                        throw new ModuleContractException($"Ambiguous graph type '{structure.Name}'.");
                    }
                    continue;
                }
                originals.Add(structure.Name, (structure, reference.Structures, origin));
                _ = names.Add(structure.Name);
                types.Add(structure);
            }
        }
        var combined = types.ToImmutable();
        for (var index = 0; index < types.Count; index++)
        {
            var original = originals[types[index].Name];
            types[index] = RemapStructure(original.Type, original.Table, combined);
        }
        combined = types.ToImmutable();
        var functions = ImmutableArray.CreateBuilder<BoundFunction>();
        foreach (var reference in references)
        {
            foreach (var function in reference.Functions)
            {
                if (!names.Add(function.Name))
                {
                    throw new ModuleContractException($"Ambiguous graph export '{function.Name}'.");
                }
                functions.Add(RemapFunction(function, reference.Structures, combined));
            }
        }
        return new CompilationReference(combined, functions.ToImmutable(), references[0].RuntimePolicy);
    }

    /// <summary>Stores a verified DLL implementation with symbolic imports and no fabricated entry point.</summary>
    internal static byte[] CreateGraphLibrary(BoundProgram program, IReadOnlyList<string> exports,
        CompilationReference dependencies)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(exports);
        SemanticVerifier.VerifyLibraryReferences(program);
        if (program.EntryPoint != -1 || !program.Buffers.IsDefaultOrEmpty
            || program.RuntimePolicy != dependencies.RuntimePolicy)
        {
            throw new ModuleContractException("Graph libraries require compatible unbuffered library IR.");
        }
        var names = exports.ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0 || names.Count != exports.Count)
        {
            throw new ModuleContractException("Graph library exports must be distinct functions.");
        }
        var allNames = program.Functions.Select(static item => item.Name).ToHashSet(StringComparer.Ordinal);
        var available = dependencies.Functions.ToDictionary(static item => item.Name, StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            if (HasPrivateManagedState(program) && (function.IsExternal || names.Contains(function.Name))
                && !ManagedExportShape.Supported(function, program.Structures))
            {
                throw new ModuleContractException($"Managed graph boundary '{function.Name}' requires public Copy values.");
            }
            if (names.Contains(function.Name) && (function.IsExternal || !Exportable(function, program.Structures)))
            {
                throw new ModuleContractException($"Graph export '{function.Name}' is not a public implementation.");
            }
            if (function.IsExternal && (!available.TryGetValue(function.Name, out var actual)
                || !CompatibleContract(Contract(function, program.Structures),
                    Contract(actual, dependencies.Structures))
                || Compilation.HasOwnershipTransfer(function, program.Structures)
                    != Compilation.HasOwnershipTransfer(actual, dependencies.Structures)))
            {
                throw new ModuleContractException($"Incompatible graph import '{function.Name}'.");
            }
        }
        if (!names.IsSubsetOf(allNames))
        {
            throw new ModuleContractException("Graph library must export owned functions.");
        }
        var required = RequiredStructures(program.Functions.Select(static item =>
            item.IsExternal ? Declaration(item) : item), program.Structures);
        var structures = SelectStructures(program.Structures, required);
        var importedTypes = ImmutableArray.CreateBuilder<string>();
        foreach (var structure in structures)
        {
            var actual = dependencies.Structures.FirstOrDefault(item => item.Name == structure.Name);
            if (actual is not null)
            {
                if (structure.IsPrivate || structure.IsInternal
                    || !SameStructure(structure, actual, structures, dependencies.Structures))
                {
                    throw new ModuleContractException($"Incompatible imported value layout '{structure.Name}'.");
                }
                importedTypes.Add(structure.Name);
            }
        }
        var publicTypes = RequiredStructures(program.Functions.Where(item => names.Contains(item.Name))
            .Select(Declaration), program.Structures);
        foreach (var structure in structures)
        {
            if (publicTypes.Contains(structure.Name) && structure.CleanupMethod is string cleanup
                && !importedTypes.Contains(structure.Name) && !names.Contains(cleanup))
            {
                throw new ModuleContractException($"Graph-owned type requires exported cleanup '{cleanup}'.");
            }
        }
        var functions = program.Functions.Select(function => {
            var remapped = RemapFunction(function, program.Structures, structures);
            return new ModuleFunction(function.Name,
                Contract(remapped, structures), function.IsExternal ? null : remapped, function.IsExternal) {
                HasOwnershipTransfer = Compilation.HasOwnershipTransfer(function, program.Structures),
            };
        }).ToImmutableArray();
        RejectImportedTypeMembers(functions, importedTypes.ToImmutable());
        var module = new Module(true, -1, structures, functions) {
            Classes = [.. program.Classes.Select(@class => RemapClass(@class, program.Structures, structures))],
            ImportedStructures = importedTypes.ToImmutable(),
            RuntimePolicy = program.RuntimePolicy,
        };
        var contract = Write(module, program);
        ValidateGraphLibrary(contract, exports, dependencies, allowImportedCleanup: true);
        return contract;
    }

    /// <summary>Reads exported declarations from a graph library, not its implementation bodies.</summary>
    internal static CompilationReference CreateGraphReference(byte[] contract, IReadOnlyList<string> exports,
        bool allowImportedCleanup = false)
    {
        var module = ReadGraphLibrary(contract);
        var names = exports.ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0 || names.Count != exports.Count)
        {
            throw new ModuleFormatException("Graph library requires distinct exported declarations.");
        }
        var declarations = ImmutableArray.CreateBuilder<BoundFunction>();
        foreach (var item in module.Functions)
        {
            if (names.Contains(item.Name))
            {
                if (item.Import || item.Function is not BoundFunction function
                    || !Exportable(function, module.Structures)
                    || (HasPrivateManagedState(module) && !ManagedExportShape.Supported(function, module.Structures)))
                {
                    throw new ModuleFormatException($"Graph export '{item.Name}' has no public implementation.");
                }
                declarations.Add(Declaration(function));
            }
        }
        if (declarations.Count != names.Count)
        {
            throw new ModuleFormatException("Graph export is missing from its semantic library.");
        }
        var required = RequiredStructures(declarations, module.Structures);
        foreach (var structure in module.Structures)
        {
            if (required.Contains(structure.Name) && structure.CleanupMethod is string cleanup
                && !names.Contains(cleanup)
                && (!allowImportedCleanup || !module.ImportedStructures.Contains(structure.Name)))
            {
                throw new ModuleContractException($"Graph export requires cleanup '{cleanup}'.");
            }
        }
        var structures = SelectStructures(module.Structures, required);
        return new CompilationReference(structures,
            [.. declarations.Select(item => RemapFunction(item, module.Structures, structures))],
            module.RuntimePolicy);
    }

    /// <summary>Validates a graph library against current dependency declarations, without emitting code.</summary>
    internal static void ValidateGraphLibrary(byte[] contract, IReadOnlyList<string> exports,
        CompilationReference dependencies, bool allowImportedCleanup = false)
    {
        var module = ReadGraphLibrary(contract);
        _ = CreateGraphReference(contract, exports, allowImportedCleanup);
        ValidateGraphImports(module, dependencies);
        if (HasPrivateManagedState(module) && module.Functions.Any(item => item.Import
            && (!dependencies.Functions.Any(function => function.Name == item.Name
                && ManagedExportShape.Supported(function, dependencies.Structures)))))
        {
            throw new ModuleContractException("Managed graph import crosses a non-Copy boundary.");
        }
        var indexes = module.Functions.Select((item, index) => (item.Name, index))
            .ToDictionary(static item => item.Name, static item => item.index, StringComparer.Ordinal);
        var external = dependencies.Functions.ToDictionary(static item => item.Name, StringComparer.Ordinal);
        var program = new BoundProgram([.. module.Functions.Select(item => item.Import
            ? RemapFunction(RecordedImportPromises(item.Contract, external[item.Name]),
                dependencies.Structures, module.Structures)
            : item.Function ?? throw new ModuleFormatException("Graph library implementation is missing."))], -1) {
            Structures = module.Structures,
            Classes = module.Classes,
            RuntimePolicy = module.RuntimePolicy,
        };
        try
        {
            SemanticVerifier.VerifyLibraryReferences(Resolve(program, indexes));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ModuleFormatException("Graph library IR failed semantic verification.", exception);
        }
    }

    internal static ImmutableArray<string> GetGraphImports(byte[] contract)
    {
        var module = ReadGraphLibrary(contract);
        return [.. module.Functions.Where(static item => item.Import).Select(static item => item.Name)
            .Order(StringComparer.Ordinal)];
    }

    internal static ImmutableArray<BoundStruct> GetGraphRetainedTypes(byte[] contract, bool consumer) =>
        consumer ? Read(contract, expectedConsumer: true).Structures : ReadGraphLibrary(contract).Structures;

    internal static ImmutableArray<string> GetGraphImportedTypes(byte[] contract, bool consumer) =>
        consumer ? Read(contract, expectedConsumer: true).ImportedStructures
            : ReadGraphLibrary(contract).ImportedStructures;

    internal static string GetGraphProfile(byte[] contract)
    {
        var module = ReadGraphLibrary(contract);
        return RetainedNativeProfile(module);
    }

    private static BoundFunction RecordedImportPromises(string contract, BoundFunction replacement)
    {
        if (!TryContractPromises(contract.AsSpan(), out _, out var noThrow, out var noReturn))
        {
            throw new ModuleFormatException("Invalid recorded import promises.");
        }
        return replacement with { IsNoThrow = noThrow, DoesNotReturn = noReturn };
    }

    private static void ValidateGraphImports(Module module, CompilationReference dependencies)
    {
        if (module.RuntimePolicy != dependencies.RuntimePolicy)
        {
            throw new ModuleContractException("Graph library and dependency runtime policies differ.");
        }
        var functions = dependencies.Functions.ToDictionary(static item => item.Name, StringComparer.Ordinal);
        foreach (var item in module.Functions.Where(static item => item.Import))
        {
            if (!functions.TryGetValue(item.Name, out var actual)
                || !CompatibleContract(item.Contract, Contract(actual, dependencies.Structures))
                || item.HasOwnershipTransfer != Compilation.HasOwnershipTransfer(actual, dependencies.Structures))
            {
                throw new ModuleContractException($"Incompatible graph import '{item.Name}'.");
            }
        }
        var imported = module.ImportedStructures.ToHashSet(StringComparer.Ordinal);
        foreach (var structure in module.Structures)
        {
            var actual = dependencies.Structures.FirstOrDefault(item => item.Name == structure.Name);
            if (actual is not null && (!imported.Contains(structure.Name)
                || !SameStructure(structure, actual, module.Structures, dependencies.Structures)))
            {
                throw new ModuleContractException($"Incompatible graph dependency layout '{structure.Name}'.");
            }
        }
        if (imported.Any(name => !dependencies.Structures.Any(item => item.Name == name)))
        {
            throw new ModuleContractException("Graph dependency type is missing.");
        }
        RejectImportedTypeMembers(module.Functions, module.ImportedStructures);
    }

    private static Module ReadGraphLibrary(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 17 or > MaximumBytes)
        {
            throw new ModuleFormatException("Graph semantic module length is invalid.");
        }
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, new UTF8Encoding(false, true), leaveOpen: true);
            var (version, policy, profile) = ReadModuleHeader(reader, expectedConsumer: true);
            if (reader.ReadInt32() != -1)
            {
                throw new ModuleFormatException("Graph library cannot have an entry point.");
            }
            var (structures, names) = ReadStructures(reader, version);
            if (version is >= ManagedClassVersion and not (LoopTransferVersion or ManagedGraphParticipantVersion))
            {
                throw new ModuleFormatException("Graph libraries cannot use the private class module version.");
            }
            var classes = version >= ManagedClassVersion ? ReadClasses(reader, names, version) : [];
            if (version != ManagedGraphParticipantVersion && !classes.IsEmpty)
            {
                throw new ModuleFormatException("Graph libraries cannot retain private classes.");
            }
            var imported = ReadImportedTypes(reader, structures.Length, names, expectedConsumer: true);
            var count = Count(reader, MaximumFunctions);
            if (count == 0)
            {
                throw new ModuleFormatException("Graph library has no functions.");
            }
            var functions = ReadFunctions(reader, count, expectedConsumer: true, version, structures, classes);
            var module = new Module(true, -1, structures, functions) {
                Classes = classes,
                ImportedStructures = imported,
                RuntimePolicy = policy,
                SerializedVersion = version,
                SerializedProfile = profile,
            };
            if ((version == ManagedGraphParticipantVersion && !HasPrivateManagedState(module))
                || stream.Position != stream.Length || profile != RetainedNativeProfile(module))
            {
                throw new ModuleFormatException("Graph semantic profile or payload is invalid.");
            }
            return module;
        }
        catch (Exception exception) when (exception is EndOfStreamException or DecoderFallbackException
            or OverflowException or ArgumentOutOfRangeException or IOException)
        {
            throw new ModuleFormatException("Malformed graph semantic module.", exception);
        }
    }
}
