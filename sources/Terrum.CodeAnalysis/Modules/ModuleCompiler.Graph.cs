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
    internal sealed class GraphContractSnapshot
    {
        private readonly Module _module;

        internal GraphContractSnapshot(Module module, CompilationReference? reference,
            bool hasPrivateManagedState, string profile)
        {
            _module = module;
            Reference = reference;
            RuntimePolicy = module.RuntimePolicy;
            Profile = profile;
            Imports = [.. module.Functions.Where(static item => item.Import).Select(static item => item.Name)
                .Order(StringComparer.Ordinal)];
            Structures = module.Structures;
            ImportedStructures = module.ImportedStructures;
            HasPrivateManagedState = hasPrivateManagedState;
        }

        internal CompilationReference? Reference { get; }
        internal BoundRuntimePolicy RuntimePolicy { get; }
        internal string Profile { get; }
        internal ImmutableArray<string> Imports { get; }
        internal ImmutableArray<BoundStruct> Structures { get; }
        internal ImmutableArray<string> ImportedStructures { get; }
        private bool HasPrivateManagedState { get; }

        internal void ValidateConsumerAgainstReference(CompilationReference reference)
        {
            _ = RestoreConsumerAgainstReference(_module, reference);
        }

        internal void ValidateGraphLibrary(CompilationReference dependencies)
        {
            var reference = Reference
                ?? throw new InvalidOperationException("Native graph provider reference is missing.");
            ModuleCompiler.ValidateGraphLibrary(_module, dependencies, reference, HasPrivateManagedState);
        }
    }

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
        var destinationTypes = IndexTypes(types);
        for (var index = 0; index < types.Count; index++)
        {
            var original = originals[types[index].Name];
            types[index] = RemapStructure(original.Type, original.Table, types, destinationTypes);
        }
        var combined = types.ToImmutable();
        var functions = ImmutableArray.CreateBuilder<BoundFunction>();
        foreach (var reference in references)
        {
            foreach (var function in reference.Functions)
            {
                if (!names.Add(function.Name))
                {
                    throw new ModuleContractException($"Ambiguous graph export '{function.Name}'.");
                }
                functions.Add(RemapFunction(function, reference.Structures, combined,
                    destinationTypes: destinationTypes));
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
        RejectCompilationLocalSpans(program);
        if (ContainsUnsupportedModuleCallerUnsafeContracts(program))
        {
            throw new ModuleContractException("Only top-level caller-unsafe functions are supported in persisted semantic modules.");
        }
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
        var hasPrivateManagedState = HasPrivateManagedState(program);
        var ownershipTransfers = new bool[program.Functions.Length];
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            if (hasPrivateManagedState && (function.IsExternal || names.Contains(function.Name))
                && !ManagedExportShape.Supported(function, program.Structures))
            {
                throw new ModuleContractException($"Managed graph boundary '{function.Name}' requires public Copy values.");
            }
            if (names.Contains(function.Name) && (function.IsExternal || !Exportable(function, program.Structures)))
            {
                throw new ModuleContractException($"Graph export '{function.Name}' is not a public implementation.");
            }
            if (function.IsExternal)
            {
                if (!available.TryGetValue(function.Name, out var actual)
                    || !CompatibleContract(Contract(function, program.Structures),
                        Contract(actual, dependencies.Structures)))
                {
                    throw new ModuleContractException($"Incompatible graph import '{function.Name}'.");
                }
                var hasOwnershipTransfer = Compilation.HasOwnershipTransfer(function, program.Structures);
                if (hasOwnershipTransfer != Compilation.HasOwnershipTransfer(actual, dependencies.Structures))
                {
                    throw new ModuleContractException($"Incompatible graph import '{function.Name}'.");
                }
                ownershipTransfers[index] = hasOwnershipTransfer;
            }
        }
        if (!names.IsSubsetOf(allNames))
        {
            throw new ModuleContractException("Graph library must export owned functions.");
        }
        var required = RequiredStructures(program.Functions.Select(static item =>
            item.IsExternal ? Declaration(item) : item), program.Structures);
        var structures = SelectStructures(program.Structures, required);
        var dependencyTypes = dependencies.Structures.ToDictionary(static item => item.Name,
            StringComparer.Ordinal);
        var importedTypes = ImmutableArray.CreateBuilder<string>();
        var importedTypeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var structure in structures)
        {
            if (dependencyTypes.TryGetValue(structure.Name, out var actual))
            {
                if (structure.IsPrivate || structure.IsInternal
                    || !SameStructure(structure, actual, structures, dependencies.Structures))
                {
                    throw new ModuleContractException($"Incompatible imported value layout '{structure.Name}'.");
                }
                importedTypes.Add(structure.Name);
                _ = importedTypeNames.Add(structure.Name);
            }
        }
        var publicTypes = RequiredStructures(program.Functions.Where(item => names.Contains(item.Name))
            .Select(Declaration), program.Structures);
        foreach (var structure in structures)
        {
            if (publicTypes.Contains(structure.Name) && structure.CleanupMethod is string cleanup
                && !importedTypeNames.Contains(structure.Name) && !names.Contains(cleanup))
            {
                throw new ModuleContractException($"Graph-owned type requires exported cleanup '{cleanup}'.");
            }
        }
        var functions = program.Functions.Select((function, index) => {
            var remapped = RemapFunction(function, program.Structures, structures);
            return new ModuleFunction(function.Name,
                Contract(remapped, structures), function.IsExternal ? null : remapped, function.IsExternal) {
                HasOwnershipTransfer = function.IsExternal
                    ? ownershipTransfers[index] : Compilation.HasOwnershipTransfer(function, program.Structures),
            };
        }).ToImmutableArray();
        RejectImportedTypeMembers(functions, importedTypeNames);
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
        return CreateGraphReference(ReadGraphLibrary(contract), exports, allowImportedCleanup,
            out _);
    }

    private static CompilationReference CreateGraphReference(Module module, IReadOnlyList<string> exports,
        bool allowImportedCleanup, out bool hasPrivateManagedState)
    {
        var names = exports.ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0 || names.Count != exports.Count)
        {
            throw new ModuleFormatException("Graph library requires distinct exported declarations.");
        }
        hasPrivateManagedState = HasPrivateManagedState(module);
        var declarations = ImmutableArray.CreateBuilder<BoundFunction>();
        foreach (var item in module.Functions)
        {
            if (names.Contains(item.Name))
            {
                if (item.Import || item.Function is not BoundFunction function
                    || !Exportable(function, module.Structures)
                    || (hasPrivateManagedState && !ManagedExportShape.Supported(function, module.Structures)))
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
        var importedTypes = module.ImportedStructures.ToHashSet(StringComparer.Ordinal);
        foreach (var structure in module.Structures)
        {
            if (required.Contains(structure.Name) && structure.CleanupMethod is string cleanup
                && !names.Contains(cleanup)
                && (!allowImportedCleanup || !importedTypes.Contains(structure.Name)))
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
        var reference = CreateGraphReference(module, exports, allowImportedCleanup,
            out var hasPrivateManagedState);
        ValidateGraphLibrary(module, dependencies, reference, hasPrivateManagedState);
    }

    private static void ValidateGraphLibrary(Module module, CompilationReference dependencies,
        CompilationReference reference, bool hasPrivateManagedState)
    {
        var dependencyFunctions = dependencies.Functions.ToDictionary(static item => item.Name,
            StringComparer.Ordinal);
        if (ValidateGraphImports(module, dependencies, dependencyFunctions, hasPrivateManagedState))
        {
            throw new ModuleContractException("Managed graph import crosses a non-Copy boundary.");
        }
        var indexes = module.Functions.Select((item, index) => (item.Name, index))
            .ToDictionary(static item => item.Name, static item => item.index, StringComparer.Ordinal);
        var program = new BoundProgram([.. module.Functions.Select(item => item.Import
            ? RemapFunction(RecordedImportPromises(item.Contract, dependencyFunctions[item.Name]),
                dependencies.Structures, module.Structures)
            : item.Function ?? throw new ModuleFormatException("Graph library implementation is missing."))], -1) {
            Structures = module.Structures,
            Classes = module.Classes,
            RuntimePolicy = module.RuntimePolicy,
        };
        if (ContainsUnsupportedModuleCallerUnsafeContracts(program))
        {
            throw new ModuleFormatException("Graph library contains an unsupported caller-unsafe contract.");
        }
        try
        {
            SemanticVerifier.VerifyLibraryReferences(Resolve(program, indexes));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ModuleFormatException("Graph library IR failed semantic verification.", exception);
        }
    }

    internal static GraphContractSnapshot ReadGraphConsumerContract(byte[] contract)
    {
        var module = Read(contract, expectedConsumer: true, out var profile);
        return CreateGraphContractSnapshot(module, reference: null,
            HasPrivateManagedState(module), profile);
    }

    internal static GraphContractSnapshot ReadGraphProviderContract(byte[] contract,
        IReadOnlyList<string> exports)
    {
        var module = ReadGraphLibrary(contract, out var profile);
        var reference = CreateGraphReference(module, exports, allowImportedCleanup: true,
            out var hasPrivateManagedState);
        return CreateGraphContractSnapshot(module, reference, hasPrivateManagedState, profile);
    }

    internal static GraphContractSnapshot ReadGraphProviderSnapshot(byte[] contract)
    {
        var module = ReadGraphLibrary(contract, out var profile);
        return CreateGraphContractSnapshot(module, reference: null,
            HasPrivateManagedState(module), profile);
    }

    internal static string GetGraphProfile(byte[] contract)
    {
        _ = ReadGraphLibrary(contract, out var profile);
        return profile;
    }

    private static GraphContractSnapshot CreateGraphContractSnapshot(Module module,
        CompilationReference? reference, bool hasPrivateManagedState, string profile) =>
        new(module, reference, hasPrivateManagedState, profile);

    private static BoundFunction RecordedImportPromises(string contract, BoundFunction replacement)
    {
        if (!TryContractPromises(contract.AsSpan(), out _, out var noThrow, out var noReturn,
            out var unsafeCaller))
        {
            throw new ModuleFormatException("Invalid recorded import promises.");
        }
        return replacement with {
            IsNoThrow = noThrow,
            DoesNotReturn = noReturn,
            RequiresUnsafeCaller = unsafeCaller,
        };
    }

    private static bool ValidateGraphImports(Module module, CompilationReference dependencies,
        IReadOnlyDictionary<string, BoundFunction> dependencyFunctions, bool hasPrivateManagedState)
    {
        if (module.RuntimePolicy != dependencies.RuntimePolicy)
        {
            throw new ModuleContractException("Graph library and dependency runtime policies differ.");
        }
        var dependencyTypes = dependencies.Structures.ToDictionary(static item => item.Name,
            StringComparer.Ordinal);
        var hasUnsupportedManagedImport = false;
        foreach (var item in module.Functions)
        {
            if (!item.Import)
            {
                continue;
            }
            if (!dependencyFunctions.TryGetValue(item.Name, out var actual)
                || !CompatibleContract(item.Contract, Contract(actual, dependencies.Structures))
                || item.HasOwnershipTransfer != Compilation.HasOwnershipTransfer(actual, dependencies.Structures))
            {
                throw new ModuleContractException($"Incompatible graph import '{item.Name}'.");
            }
            if (hasPrivateManagedState && !ManagedExportShape.Supported(actual, dependencies.Structures))
            {
                hasUnsupportedManagedImport = true;
            }
        }
        var imported = module.ImportedStructures.ToHashSet(StringComparer.Ordinal);
        foreach (var structure in module.Structures)
        {
            if (dependencyTypes.TryGetValue(structure.Name, out var actual)
                && (!imported.Contains(structure.Name)
                || !SameStructure(structure, actual, module.Structures, dependencies.Structures)))
            {
                throw new ModuleContractException($"Incompatible graph dependency layout '{structure.Name}'.");
            }
        }
        if (imported.Any(name => !dependencyTypes.ContainsKey(name)))
        {
            throw new ModuleContractException("Graph dependency type is missing.");
        }
        RejectImportedTypeMembers(module.Functions, imported);
        return hasUnsupportedManagedImport;
    }

    private static Module ReadGraphLibrary(byte[] bytes) =>
        ReadGraphLibrary(bytes, out _);

    private static Module ReadGraphLibrary(byte[] bytes, out string profileVersion)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 17 or > MaximumBytes)
        {
            throw new ModuleFormatException("Graph semantic module length is invalid.");
        }
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8, leaveOpen: true);
            var (policy, profile) = ReadModuleHeader(reader, expectedConsumer: true);
            if (reader.ReadInt32() != -1)
            {
                throw new ModuleFormatException("Graph library cannot have an entry point.");
            }
            var (structures, names) = ReadStructures(reader);
            var classes = ReadClasses(reader, names);
            var imported = ReadImportedTypes(reader, structures.Length, names, expectedConsumer: true);
            var count = Count(reader, MaximumFunctions);
            if (count == 0)
            {
                throw new ModuleFormatException("Graph library has no functions.");
            }
            var functions = ReadFunctions(reader, count, expectedConsumer: true, structures, classes);
            if (functions.Any(HasUnsupportedModuleCallerUnsafeContract))
            {
                throw new ModuleFormatException("Graph library contains an unsupported caller-unsafe contract.");
            }
            var module = new Module(true, -1, structures, functions) {
                Classes = classes,
                ImportedStructures = imported,
                RuntimePolicy = policy,
            };
            var retainedProfile = RetainedNativeProfile(module);
            if ((profile == "native-20" && classes.IsEmpty)
                || stream.Position != stream.Length || profile != retainedProfile)
            {
                throw new ModuleFormatException("Graph semantic profile or payload is invalid.");
            }
            profileVersion = retainedProfile;
            return module;
        }
        catch (Exception exception) when (exception is EndOfStreamException or DecoderFallbackException
            or OverflowException or ArgumentOutOfRangeException or IOException)
        {
            throw new ModuleFormatException("Malformed graph semantic module.", exception);
        }
    }
}
