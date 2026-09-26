// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
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
        var image = NativeEmitter.EmitLibraryWithImports(program, exports, NativeBindings(bindings),
            runtimePolicy);
        var package = new GraphPackage(false, identity, runtimePolicy,
            [.. exports.Order(StringComparer.Ordinal)], bindings, contract, image);
        var result = WriteGraphPackage(package);
        ValidateGraphDirect(ReadGraphPackage(result), providers);
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
        var contract = ModuleCompiler.CreateConsumerAgainstReference(program, GraphReference(providers));
        var imports = ModuleCompiler.GetConsumerSetDeclarations(contract);
        var bindings = GraphBindings(providers, imports);
        var image = NativeEmitter.EmitWithImports(program, NativeBindings(bindings), runtimePolicy);
        var package = new GraphPackage(true, null, runtimePolicy, [], bindings, contract, image);
        var result = WriteGraphPackage(package);
        ValidateGraphDirect(ReadGraphPackage(result), providers);
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
        CheckGraph(ReadGraphPackage(consumer), ReadGraphProviders(producers));
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
        CheckGraph(executable, providers);
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

    private static void CheckGraph(GraphPackage consumer, GraphPackage[] providers)
    {
        if (!consumer.Consumer || consumer.IsLeaf || providers[0].Policy != consumer.Policy)
        {
            throw new ModuleContractException("Native graph root or runtime policy is invalid.");
        }
        var byIdentity = providers.ToDictionary(static item => item.Identity!,
            StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in consumer.Dependencies)
        {
            Visit(dependency.Identity);
        }
        if (visited.Count != providers.Length)
        {
            throw new ModuleContractException("Native graph includes unexpected producers.");
        }
        ValidateGraphDirect(consumer, ResolveDirect(consumer, byIdentity));

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
        var reference = GraphReference(providers);
        if (package.Consumer)
        {
            ModuleCompiler.ValidateConsumerAgainstReference(package.Contract, reference);
        }
        else
        {
            ModuleCompiler.ValidateGraphLibrary(package.Contract, package.Exports, reference);
        }
        foreach (var (dependency, provider) in package.Dependencies.Zip(providers))
        {
            if (dependency.Imports.Any(name => !provider.Exports.Contains(name, StringComparer.Ordinal)))
            {
                throw new ModuleContractException($"Graph import does not belong to '{dependency.Identity}'.");
            }
        }
    }

    private static CompilationReference GraphReference(GraphPackage[] providers) =>
        ModuleCompiler.MergeGraphReferences([.. providers.Select(provider => provider.IsLeaf
            ? ModuleCompiler.CreateReference(provider.Contract)
            : ModuleCompiler.CreateGraphReference(provider.Contract, provider.Exports))]);

    private static NativeImportBinding[] NativeBindings(GraphDependency[] dependencies) =>
        [.. dependencies.SelectMany(item => item.Imports.Select(name =>
            new NativeImportBinding(name, DllName(item.Identity), name)))];
}
