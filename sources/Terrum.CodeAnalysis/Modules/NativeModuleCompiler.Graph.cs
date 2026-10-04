// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Checked graph artifact metadata; identities name DLLs rather than pinning implementations.</summary>
public sealed record NativeGraphInfo(string? Identity, bool IsConsumer, NativeRuntimePolicy RuntimePolicy,
    string NativeProfileVersion, string ImageSha256, IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Exports);

public static partial class NativeModuleCompiler
{
    /// <summary>Binds source against independently persisted direct leaf or graph DLL declarations.</summary>
    public static CompilationReference CreateGraphReference(IReadOnlyList<byte[]> dependencies) =>
        GraphReference(ReadGraphProviders(dependencies));

    /// <summary>Produces one DLL with real exported implementations and explicit imports of selected DLLs.</summary>
    public static byte[] CreateGraphProducer(BoundProgram program, IReadOnlyList<string> exports, string identity,
        IReadOnlyList<byte[]> dependencies, NativeRuntimePolicy runtimePolicy)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(exports);
        CheckIdentity(identity);
        CheckPolicy(runtimePolicy);
        var providers = ReadGraphProviders(dependencies);
        if (providers.Any(item => item.Identity!.Equals(identity, StringComparison.OrdinalIgnoreCase))
            || providers[0].Policy != runtimePolicy)
        {
            throw new ModuleContractException("Graph producer identity or runtime policy conflicts with a dependency.");
        }
        var reference = GraphReference(providers);
        var contract = ModuleCompiler.CreateGraphLibrary(program, exports, reference);
        var imports = ModuleCompiler.GetGraphImports(contract);
        var bindings = GraphBindings(providers, imports);
        var declarations = NativeBindings(bindings);
        var (image, used) = declarations.Length == 0
            ? (NativeEmitter.EmitLibrary(program, exports, runtimePolicy),
                ImmutableArray<NativeImportBinding>.Empty)
            : NativeEmitter.EmitLibraryWithImportUsage(program, exports, declarations, runtimePolicy);
        var package = new GraphPackage(false, identity, runtimePolicy,
            [.. exports.Order(StringComparer.Ordinal)], WithGraphUsage(bindings, used), contract, image) {
            Origins = CreateGraphOrigins(contract, false, identity, providers),
        };
        var result = WriteGraphPackage(package);
        ValidateGraphDirect(ReadGraphPackageSnapshot(result), providers);
        return result;
    }

    /// <summary>Persists one executable and its direct dependency identities without embedding DLL implementations.</summary>
    public static byte[] CreateGraphConsumer(BoundProgram program, IReadOnlyList<byte[]> dependencies,
        NativeRuntimePolicy runtimePolicy)
    {
        ArgumentNullException.ThrowIfNull(program);
        CheckPolicy(runtimePolicy);
        var providers = ReadGraphProviders(dependencies);
        if (providers[0].Policy != runtimePolicy || program.RuntimePolicy != (BoundRuntimePolicy)runtimePolicy)
        {
            throw new ModuleContractException("Graph consumer and dependency runtime policies differ.");
        }
        var contract = ModuleCompiler.CreateConsumerAgainstReference(program, GraphReference(providers),
            allowManagedParticipant: true);
        var imports = ModuleCompiler.GetConsumerSetDeclarations(contract);
        var bindings = GraphBindings(providers, imports);
        var declarations = NativeBindings(bindings);
        var (image, used) = declarations.Length == 0
            ? (NativeEmitter.Emit(program, runtimePolicy),
                ImmutableArray<NativeImportBinding>.Empty)
            : NativeEmitter.EmitWithImportUsage(program, declarations, runtimePolicy);
        var package = new GraphPackage(true, null, runtimePolicy, [],
            WithGraphUsage(bindings, used), contract, image) {
            Origins = CreateGraphOrigins(contract, true, null, providers),
        };
        var result = WriteGraphPackage(package);
        ValidateGraphDirect(ReadGraphPackageSnapshot(result), providers);
        return result;
    }

    /// <summary>Inspects a checked graph package or an unchanged leaf package.</summary>
    public static NativeGraphInfo InspectGraphArtifact(byte[] artifact)
    {
        var package = ReadGraphPackage(artifact);
        var profile = package.IsLeaf
            ? ModuleCompiler.GetNativeProfileVersion(package.Contract, false)
            : package.Consumer ? ModuleCompiler.GetNativeProfileVersion(package.Contract, true)
                : ModuleCompiler.GetGraphProfile(package.Contract);
        return new NativeGraphInfo(package.Identity, package.Consumer, package.Policy, profile,
            Convert.ToHexString(SHA256.HashData(package.Image)),
            [.. package.Dependencies.Select(static item => item.Identity)], package.Exports);
    }

    /// <summary>Checks the complete selected closure, every edge and every PE import before execution.</summary>
    public static void ValidateGraph(byte[] consumer, IReadOnlyList<byte[]> producers)
    {
        _ = CheckGraph(ReadGraphPackage(consumer), ReadGraphProviders(producers));
    }

    /// <summary>Runs unchanged checked EXE and DLL snapshots, with a bounded deadline and no recompilation.</summary>
    public static async Task<int> RunGraphAsync(byte[] consumer, IReadOnlyList<byte[]> producers,
        TimeSpan timeout, NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default)
    {
        CheckPolicy(runtimePolicy);
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("Native module execution requires Windows x64.");
        }
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var executable = ReadGraphPackage(consumer);
        var providers = ReadGraphProviders(producers);
        if (executable.Policy != runtimePolicy)
        {
            throw new ModuleContractException("Selected graph runtime policy differs from persisted consumer.");
        }
        _ = CheckGraph(executable, providers);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Directory.CreateTempSubdirectory("terrum-native-graph-").FullName;
        var paths = new List<string>();
        try
        {
            var executablePath = Path.Combine(directory, "consumer.exe");
            paths.Add(executablePath);
            await WriteNewAsync(executablePath, executable.Image, cancellationToken).ConfigureAwait(false);
            foreach (var provider in providers)
            {
                var path = Path.Combine(directory, DllName(provider.Identity!));
                paths.Add(path);
                await WriteNewAsync(path, provider.Image, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            using var process = Process.Start(new ProcessStartInfo(executablePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Could not start native graph consumer.");
            try
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException) when (process.HasExited)
                    {
                    }
                }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            return process.ExitCode;
        }
        finally
        {
            foreach (var path in paths)
            {
                await DeleteImageAsync(path).ConfigureAwait(false);
            }
            Directory.Delete(directory);
        }
    }

    private static GraphPackage[] CheckGraph(GraphPackage consumer, GraphPackage[] providers)
    {
        if (!consumer.Consumer || consumer.IsLeaf || providers[0].Policy != consumer.Policy)
        {
            throw new ModuleContractException("Native graph root or runtime policy is invalid.");
        }
        var byIdentity = providers.ToDictionary(static item => item.Identity!,
            StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<GraphPackage>(providers.Length);
        foreach (var dependency in consumer.Dependencies)
        {
            Visit(dependency.Identity);
        }
        if (visited.Count != providers.Length)
        {
            throw new ModuleContractException("Native graph includes unexpected producers.");
        }
        ValidateGraphDirect(consumer, ResolveDirect(consumer, byIdentity));
        return [.. order];

        void Visit(string identity)
        {
            if (!byIdentity.TryGetValue(identity, out var node))
            {
                throw new ModuleContractException($"Missing graph dependency '{identity}'.");
            }
            if (!visiting.Add(identity))
            {
                throw new ModuleContractException("Native dependency graph contains a cycle.");
            }
            if (!visited.Contains(identity))
            {
                foreach (var dependency in node.Dependencies)
                {
                    Visit(dependency.Identity);
                }
                if (!node.IsLeaf)
                {
                    ValidateGraphDirect(node, ResolveDirect(node, byIdentity));
                }
                _ = visited.Add(identity);
                order.Add(node);
            }
            _ = visiting.Remove(identity);
        }
    }

    private static GraphPackage[] ResolveDirect(GraphPackage package,
        IReadOnlyDictionary<string, GraphPackage> byIdentity)
    {
        var result = new GraphPackage[package.Dependencies.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var name = package.Dependencies[i].Identity;
            if (!byIdentity.TryGetValue(name, out var provider)
                || !string.Equals(name, provider.Identity, StringComparison.Ordinal))
            {
                throw new ModuleContractException($"Missing or identity-incompatible graph provider '{name}'.");
            }
            result[i] = provider;
        }
        return result;
    }

    private static void ValidateGraphDirect(GraphPackage package, GraphPackage[] providers)
    {
        if (package.IsLeaf || providers.Length != package.Dependencies.Length
            || providers.Any(item => item.Policy != package.Policy)
            || !providers.Select(static item => item.Identity).SequenceEqual(
                package.Dependencies.Select(static item => (string?)item.Identity)))
        {
            throw new ModuleContractException("Graph direct dependencies or policies differ.");
        }
        var references = providers.Select(GetValidatedProviderReference).ToArray();
        var provenance = providers.Select((provider, index) =>
            ProviderOrigins(provider, references[index])).ToArray();
        var reference = GraphReference(providers, references, provenance);
        if (package.Consumer)
        {
            ModuleCompiler.ValidateConsumerAgainstReference(package.Contract, reference);
        }
        else
        {
            ModuleCompiler.ValidateGraphLibrary(package.Contract, package.Exports, reference,
                allowImportedCleanup: true);
        }
        foreach (var (dependency, provider) in package.Dependencies.Zip(providers))
        {
            if (dependency.Declarations.Any(name =>
                Array.BinarySearch(provider.Exports, name, StringComparer.Ordinal) < 0))
            {
                throw new ModuleContractException($"Graph declaration does not belong to '{dependency.Identity}'.");
            }
        }
        var expected = CreateGraphOrigins(package.Contract, package.Consumer, package.Identity,
            providers, provenance);
        if (!package.Origins.SequenceEqual(expected))
        {
            throw new ModuleContractException("Graph type origin or cleanup ownership differs from selected providers.");
        }
    }

    private static CompilationReference GraphReference(GraphPackage[] providers)
    {
        var references = providers.Select(GetValidatedProviderReference).ToArray();
        var provenance = providers.Select((provider, index) =>
            ProviderOrigins(provider, references[index])).ToArray();
        return GraphReference(providers, references, provenance);
    }

    private static CompilationReference GraphReference(GraphPackage[] providers,
        CompilationReference[] references, Dictionary<string, GraphTypeOrigin?>[] provenance)
    {
        var origins = references.Select((reference, index) => (IReadOnlyDictionary<string, string?>)
            reference.Structures.ToDictionary(static item => item.Name,
                item => provenance[index].GetValueOrDefault(item.Name)?.Identity, StringComparer.Ordinal)).ToArray();
        var cleanupOwners = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 0; index < references.Length; index++)
        {
            foreach (var structure in references[index].Structures)
            {
                var cleanup = provenance[index].GetValueOrDefault(structure.Name)?.CleanupIdentity;
                if (cleanupOwners.TryGetValue(structure.Name, out var previous) && previous != cleanup)
                {
                    throw new ModuleContractException($"Ambiguous graph cleanup owner for '{structure.Name}'.");
                }
                cleanupOwners[structure.Name] = cleanup;
            }
        }
        var result = ModuleCompiler.MergeGraphReferences(references, origins);
        if (result.Structures.Any(static item => item.CleanupMethod is not null))
        {
            var cleanupExports = providers.ToDictionary(static provider => provider.Identity!,
                static provider => provider.Exports.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
            var cleanupFunctions = result.Functions.Where(static function => function.IsCleanup)
                .Select(static function => (function.DeclaringStruct, function.Name)).ToHashSet();
            foreach (var structure in result.Structures)
            {
                if (structure.CleanupMethod is not string cleanup)
                {
                    continue;
                }
                if (!cleanupOwners.TryGetValue(structure.Name, out var owner) || owner is null
                    || !cleanupExports.TryGetValue(owner, out var exports) || !exports.Contains(cleanup)
                    || !cleanupFunctions.Contains((structure.Name, cleanup)))
                {
                    throw new ModuleContractException($"Graph unique type '{structure.Name}' requires its origin cleanup provider.");
                }
            }
        }
        return result;
    }

    private static CompilationReference GetValidatedProviderReference(GraphPackage provider) =>
        provider.ValidatedReference
            ?? throw new InvalidOperationException("Native graph provider was not semantically validated.");

    private static NativeImportBinding[] NativeBindings(GraphDependency[] dependencies) =>
        [.. dependencies.SelectMany(item => item.Declarations.Select(name =>
            new NativeImportBinding(name, DllName(item.Identity), name)))];

    private static Dictionary<string, GraphTypeOrigin?> ProviderOrigins(GraphPackage provider,
        CompilationReference? reference = null)
    {
        reference ??= GetValidatedProviderReference(provider);
        if (provider.IsLeaf)
        {
            return reference.Structures.ToDictionary(static item => item.Name,
                item => (GraphTypeOrigin?)new GraphTypeOrigin(item.Name, provider.Identity!,
                    item.CleanupMethod is not null ? provider.Identity : null), StringComparer.Ordinal);
        }
        var retained = provider.Origins.ToDictionary(static item => item.Name, StringComparer.Ordinal);
        return reference.Structures.ToDictionary(static item => item.Name,
            item => (GraphTypeOrigin?)(retained.TryGetValue(item.Name, out var origin)
                ? origin : throw new ModuleFormatException($"Missing graph origin for '{item.Name}'.")),
            StringComparer.Ordinal);
    }

    private static GraphTypeOrigin[] CreateGraphOrigins(byte[] contract, bool consumer, string? identity,
        GraphPackage[] providers, Dictionary<string, GraphTypeOrigin?>[]? provenance = null)
    {
        var imported = ModuleCompiler.GetGraphImportedTypes(contract, consumer)
            .ToHashSet(StringComparer.Ordinal);
        var structures = ModuleCompiler.GetGraphRetainedTypes(contract, consumer);
        provenance ??= providers.Select(provider => ProviderOrigins(provider)).ToArray();
        var result = new List<GraphTypeOrigin>(structures.Length);
        foreach (var structure in structures)
        {
            if (!imported.Contains(structure.Name))
            {
                var owner = identity ?? "@";
                result.Add(new GraphTypeOrigin(structure.Name, owner,
                    structure.CleanupMethod is not null ? owner : null));
                continue;
            }
            var origins = provenance.Select(table => table.GetValueOrDefault(structure.Name))
                .Where(static item => item is not null).ToArray();
            if (origins.Length == 0 || origins.Any(origin => origin != origins[0]))
            {
                throw new ModuleContractException($"Graph imported type '{structure.Name}' has no unambiguous origin.");
            }
            var origin = origins[0]!;
            if (!providers.Any(provider => provider.Identity == origin.Identity)
                || (structure.CleanupMethod is not null && (origin.CleanupIdentity != origin.Identity
                    || !providers.Any(provider => provider.Identity == origin.Identity
                        && Array.BinarySearch(provider.Exports, structure.CleanupMethod,
                            StringComparer.Ordinal) >= 0))))
            {
                throw new ModuleContractException($"Graph imported type '{structure.Name}' lacks its origin or cleanup.");
            }
            result.Add(origin);
        }
        return [.. result.OrderBy(static item => item.Name, StringComparer.Ordinal)];
    }
}
