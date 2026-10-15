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
            var importCount = 0;
            foreach (var item in module.Functions)
            {
                if (item.Import)
                {
                    importCount++;
                }
            }
            var imports = ImmutableArray.CreateBuilder<string>(importCount);
            foreach (var item in module.Functions)
            {
                if (item.Import)
                {
                    imports.Add(item.Name);
                }
            }
            imports.Sort(StringComparer.Ordinal);
            Imports = imports.MoveToImmutable();
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
        var functionCount = 0;
        for (var provider = 0; provider < references.Count; provider++)
        {
            var reference = references[provider];
            if (reference.RequiresSourceGrant)
            {
                throw new ModuleContractException("Native graphs do not support Host.Source requirements.");
            }
            if (reference.RuntimePolicy != references[0].RuntimePolicy)
            {
                throw new ModuleContractException("Graph dependency policies differ.");
            }
            functionCount = checked(functionCount + reference.Functions.Length);
        }
        var types = ImmutableArray.CreateBuilder<BoundStruct>();
        var functionNames = new HashSet<string>(functionCount, StringComparer.Ordinal);
        var originals = new Dictionary<string, (BoundStruct Type, ImmutableArray<BoundStruct> Table, string? Origin)>(
            StringComparer.Ordinal);
        for (var provider = 0; provider < references.Count; provider++)
        {
            var reference = references[provider];
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
        var functions = ImmutableArray.CreateBuilder<BoundFunction>(functionCount);
        foreach (var reference in references)
        {
            foreach (var function in reference.Functions)
            {
                if (originals.ContainsKey(function.Name) || !functionNames.Add(function.Name))
                {
                    throw new ModuleContractException($"Ambiguous graph export '{function.Name}'.");
                }
                functions.Add(RemapFunction(function, reference.Structures, combined,
                    destinationTypes: destinationTypes));
            }
        }
        return new CompilationReference(combined, functions.MoveToImmutable(), references[0].RuntimePolicy);
    }

    /// <summary>Stores a verified DLL implementation with symbolic imports and no fabricated entry point.</summary>
    internal static byte[] CreateGraphLibrary(BoundProgram program, IReadOnlyList<string> exports,
        CompilationReference dependencies)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(exports);
        SemanticVerifier.RejectSourceNativeImports(program, "semantic modules",
            allowDirectNativeImports: true);
        RejectCompilationLocalSpans(program);
        if (ContainsUnsupportedModuleCallerUnsafeContracts(program))
        {
            throw new ModuleContractException("Module contains an unsupported caller-unsafe member contract.");
        }
        SemanticVerifier.VerifyLibraryReferences(program);
        if (program.EntryPoint != -1 || program.RequiresSourceGrant || !program.Buffers.IsDefaultOrEmpty
            || dependencies.RequiresSourceGrant
            || program.RuntimePolicy != dependencies.RuntimePolicy)
        {
            throw new ModuleContractException("Graph libraries require compatible unbuffered library IR.");
        }
        var names = exports.ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0 || names.Count != exports.Count)
        {
            throw new ModuleContractException("Graph library exports must be distinct functions.");
        }
        var unmatchedExports = new HashSet<string>(names, StringComparer.Ordinal);
        IReadOnlyDictionary<string, BoundFunction>? available = null;
        var hasPrivateManagedState = HasPrivateManagedState(program);
        var ownershipTransfers = new bool[program.Functions.Length];
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            var isExport = names.Contains(function.Name);
            if (isExport)
            {
                _ = unmatchedExports.Remove(function.Name);
            }
            if (hasPrivateManagedState && (function.IsExternal || isExport)
                && !ManagedExportShape.Supported(function, program.Structures))
            {
                throw new ModuleContractException($"Managed graph boundary '{function.Name}' requires public Copy values.");
            }
            if (isExport && (function.IsExternal || !Exportable(function, program.Structures)))
            {
                throw new ModuleContractException($"Graph export '{function.Name}' is not a public implementation.");
            }
            if (function.IsExternal)
            {
                if (function.DirectNativeImport is not null)
                {
                    if (!DirectNativeImportContract.IsValid(function, program.Structures))
                    {
                        throw new ModuleContractException(
                            $"Graph library direct import '{function.Name}' is invalid.");
                    }
                    continue;
                }
                available ??= dependencies.Functions.IsEmpty
                    ? ImmutableDictionary<string, BoundFunction>.Empty
                    : dependencies.Functions.ToDictionary(static item => item.Name, StringComparer.Ordinal);
                if (available is null || !available.TryGetValue(function.Name, out var actual)
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
        if (unmatchedExports.Count != 0)
        {
            throw new ModuleContractException("Graph library must export owned functions.");
        }
        var required = RequiredStructures(program.Functions.Select(static item =>
            item.IsExternal ? Declaration(item) : item), program.Structures);
        var structures = SelectStructures(program.Structures, required);
        Dictionary<string, BoundStruct>? dependencyTypes = structures.IsEmpty || dependencies.Structures.IsEmpty
            ? null
            : dependencies.Structures.ToDictionary(static item => item.Name, StringComparer.Ordinal);
        var importedTypes = ImmutableArray.CreateBuilder<string>(structures.Length);
        HashSet<string>? importedTypeNames = null;
        foreach (var structure in structures)
        {
            if (dependencyTypes is not null && dependencyTypes.TryGetValue(structure.Name, out var actual))
            {
                if (structure.IsPrivate || structure.IsInternal
                    || !SameStructure(structure, actual, structures, dependencies.Structures))
                {
                    throw new ModuleContractException($"Incompatible imported value layout '{structure.Name}'.");
                }
                importedTypes.Add(structure.Name);
                importedTypeNames ??= new HashSet<string>(StringComparer.Ordinal);
                _ = importedTypeNames.Add(structure.Name);
            }
        }
        var publicTypes = RequiredStructures(program.Functions.Where(item => names.Contains(item.Name))
            .Select(Declaration), program.Structures);
        foreach (var structure in structures)
        {
            if (publicTypes.Contains(structure.Name) && structure.CleanupMethod is string cleanup
                && importedTypeNames?.Contains(structure.Name) != true && !names.Contains(cleanup))
            {
                throw new ModuleContractException($"Graph-owned type requires exported cleanup '{cleanup}'.");
            }
        }
        var functions = ImmutableArray.CreateBuilder<ModuleFunction>(program.Functions.Length);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            var remapped = RemapFunction(function, program.Structures, structures);
            var isGraphImport = function.IsExternal && function.DirectNativeImport is null;
            functions.Add(new ModuleFunction(function.Name,
                Contract(remapped, structures), isGraphImport ? null : remapped, isGraphImport) {
                HasOwnershipTransfer = isGraphImport
                    ? ownershipTransfers[index] : Compilation.HasOwnershipTransfer(function, program.Structures),
            });
        }
        var immutableFunctions = functions.MoveToImmutable();
        if (importedTypeNames is not null)
        {
            RejectImportedTypeMembers(immutableFunctions, importedTypeNames);
        }
        var classes = ImmutableArray.CreateBuilder<BoundClass>(program.Classes.Length);
        foreach (var @class in program.Classes)
        {
            classes.Add(RemapClass(@class, program.Structures, structures));
        }
        var module = new Module(true, -1, structures, immutableFunctions) {
            Classes = classes.MoveToImmutable(),
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
        var declarations = ImmutableArray.CreateBuilder<BoundFunction>(names.Count);
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
        var importedTypes = allowImportedCleanup && !module.ImportedStructures.IsEmpty
            ? module.ImportedStructures.ToHashSet(StringComparer.Ordinal) : null;
        foreach (var structure in module.Structures)
        {
            if (required.Contains(structure.Name) && structure.CleanupMethod is string cleanup
                && !names.Contains(cleanup)
                && importedTypes?.Contains(structure.Name) != true)
            {
                throw new ModuleContractException($"Graph export requires cleanup '{cleanup}'.");
            }
        }
        var structures = SelectStructures(module.Structures, required);
        var remappedDeclarations = ImmutableArray.CreateBuilder<BoundFunction>(declarations.Count);
        foreach (var declaration in declarations)
        {
            remappedDeclarations.Add(RemapFunction(declaration, module.Structures, structures));
        }
        return new CompilationReference(structures, remappedDeclarations.MoveToImmutable(),
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
        if (ValidateGraphImports(module, dependencies, hasPrivateManagedState,
            out var dependencyFunctions))
        {
            throw new ModuleContractException("Managed graph import crosses a non-Copy boundary.");
        }
        var indexes = new Dictionary<string, int>(module.Functions.Length, StringComparer.Ordinal);
        for (var index = 0; index < module.Functions.Length; index++)
        {
            indexes.Add(module.Functions[index].Name, index);
        }
        var functions = ImmutableArray.CreateBuilder<BoundFunction>(module.Functions.Length);
        for (var index = 0; index < module.Functions.Length; index++)
        {
            var item = module.Functions[index];
            functions.Add(item.Import
                ? RemapFunction(RecordedImportPromises(item.Contract, dependencyFunctions[item.Name]),
                    dependencies.Structures, module.Structures)
                : item.Function ?? throw new ModuleFormatException("Graph library implementation is missing."));
        }
        var program = new BoundProgram(functions.MoveToImmutable(), -1) {
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
        if (module.RequiresSourceGrant)
        {
            throw new ModuleContractException("Native graphs do not support Host.Source requirements.");
        }
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
        bool hasPrivateManagedState, out IReadOnlyDictionary<string, BoundFunction> dependencyFunctions)
    {
        if (module.RequiresSourceGrant || dependencies.RequiresSourceGrant)
        {
            throw new ModuleContractException("Native graphs do not support Host.Source requirements.");
        }
        if (module.RuntimePolicy != dependencies.RuntimePolicy)
        {
            throw new ModuleContractException("Graph library and dependency runtime policies differ.");
        }
        IReadOnlyDictionary<string, BoundFunction>? availableFunctions = null;
        IReadOnlyDictionary<string, BoundStruct> dependencyTypes = dependencies.Structures.IsEmpty
            ? ImmutableDictionary<string, BoundStruct>.Empty
            : dependencies.Structures.ToDictionary(static item => item.Name, StringComparer.Ordinal);
        var hasUnsupportedManagedImport = false;
        foreach (var item in module.Functions)
        {
            if (!item.Import)
            {
                continue;
            }
            availableFunctions ??= dependencies.Functions.IsEmpty
                ? ImmutableDictionary<string, BoundFunction>.Empty
                : dependencies.Functions.ToDictionary(static function => function.Name,
                    StringComparer.Ordinal);
            if (!availableFunctions.TryGetValue(item.Name, out var actual)
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
        dependencyFunctions = availableFunctions ?? ImmutableDictionary<string, BoundFunction>.Empty;
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
        foreach (var name in imported)
        {
            if (!dependencyTypes.ContainsKey(name))
            {
                throw new ModuleContractException("Graph dependency type is missing.");
            }
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
            using var reader = new ModuleReader(stream);
            var (policy, profile, requiresSourceGrant, invocationProfileId, invocationBuffers) =
                ReadModuleHeader(reader, expectedConsumer: true);
            if (requiresSourceGrant || invocationProfileId is not null || !invocationBuffers.IsEmpty)
            {
                throw new ModuleFormatException("Native graph libraries cannot require Host.Source.");
            }
            if (reader.ReadInt32() != -1)
            {
                throw new ModuleFormatException("Graph library cannot have an entry point.");
            }
            var (structures, names) = ReadStructures(reader);
            var classes = ReadClasses(reader, names);
            if (ContainsUnsupportedModuleCallerUnsafeFieldContracts(structures, classes))
            {
                throw new ModuleFormatException("Graph contains an unsupported caller-unsafe field contract.");
            }
            var imported = ReadImportedTypes(reader, structures.Length, names, expectedConsumer: true);
            var count = Count(reader, MaximumFunctions);
            if (count == 0)
            {
                throw new ModuleFormatException("Graph library has no functions.");
            }
            var functions = ReadFunctions(reader, count, expectedConsumer: true, structures, classes);
            var module = new Module(true, -1, structures, functions) {
                FormatVersion = reader.FormatVersion,
                Classes = classes,
                ImportedStructures = imported,
                RequiresSourceGrant = requiresSourceGrant,
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
