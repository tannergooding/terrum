// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Owned artifact snapshots for the one compatible closure selected from a local catalog.</summary>
/// <remarks>Candidate indexes refer to the caller's catalog. Convert snapshots to arrays for existing runners,
/// which independently revalidate their inputs before execution.</remarks>
public sealed record NativeGraphCatalogSelection(ImmutableArray<byte> Consumer,
    ImmutableArray<ImmutableArray<byte>> Producers, ImmutableArray<int> CandidateIndexes);

public static partial class NativeModuleCompiler
{
    private const int MaximumCatalogCandidates = 32;
    private const int MaximumCatalogBytes = 64 * 1024 * 1024;
    private const int MaximumCatalogAssignments = 4096;

    private sealed record CatalogCandidate(byte[] Bytes, GraphPackage Package, int Index);

    /// <summary>Selects exactly one complete compatible native graph from explicitly supplied local packages.</summary>
    /// <remarks>All inputs are snapshotted and validated before selection. Byte-identical catalog entries are one
    /// choice; distinct implementations remain distinct even when their public contracts agree. No artifact is
    /// executed or written, and the existing graph runners revalidate the returned owned byte snapshots.</remarks>
    public static NativeGraphCatalogSelection ResolveGraphCatalog(byte[] consumer, IReadOnlyList<byte[]> candidates)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count is < 1 or > MaximumCatalogCandidates)
        {
            throw new ModuleContractException("Native graph catalog requires 1 to 32 candidate packages.");
        }
        if (consumer.Length is < GraphHeaderSize + HashSize or > GraphMaximumBytes)
        {
            throw new ModuleFormatException("Native graph catalog root has invalid artifact bounds.");
        }
        var rootBytes = (byte[])consumer.Clone();
        var snapshots = new byte[candidates.Count][];
        long totalBytes = rootBytes.Length;
        for (var index = 0; index < snapshots.Length; index++)
        {
            var candidate = candidates[index]
                ?? throw new ArgumentException("Native graph catalog candidates cannot be null.", nameof(candidates));
            if (candidate.Length is < 1 or > GraphMaximumBytes)
            {
                throw new ModuleFormatException("Native graph catalog candidate has invalid artifact bounds.");
            }
            totalBytes += candidate.Length;
            if (totalBytes > MaximumCatalogBytes)
            {
                throw new ModuleContractException("Native graph catalog exceeds the 64 MiB aggregate input limit.");
            }
            snapshots[index] = (byte[])candidate.Clone();
        }

        var root = ReadGraphPackage(rootBytes);
        if (!root.Consumer || root.IsLeaf)
        {
            throw new ModuleContractException("Native graph catalog requires a persisted consumer root.");
        }
        var catalog = new Dictionary<string, List<CatalogCandidate>>(StringComparer.Ordinal);
        var catalogIdentities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguousCatalogIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < snapshots.Length; index++)
        {
            var snapshot = snapshots[index];
            var package = ReadGraphPackage(snapshot);
            if (package.Consumer || package.Identity is not string identity)
            {
                throw new ModuleContractException("Native graph catalog contains a consumer package.");
            }
            if (!catalog.TryGetValue(identity, out var choices))
            {
                choices = [];
                catalog.Add(identity, choices);
                if (catalogIdentities.TryGetValue(identity, out var existing))
                {
                    if (existing != identity)
                    {
                        _ = ambiguousCatalogIdentities.Add(identity);
                    }
                }
                else
                {
                    catalogIdentities.Add(identity, identity);
                }
            }
            if (!choices.Any(choice => choice.Bytes.AsSpan().SequenceEqual(snapshot)))
            {
                choices.Add(new CatalogCandidate(snapshot, package, index));
            }
        }

        var selected = new Dictionary<string, CatalogCandidate>(StringComparer.Ordinal);
        GraphPackage[]?[] providerBuffers = new GraphPackage[MaximumGraphProviderCount + 1][];
        // Candidate search backtracks synchronously, so cycle checks can share these traversal sets.
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        CatalogCandidate[]? resolved = null;
        var assignments = 0;
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var incompatible = false;
        var cycle = false;
        Search();
        if (resolved is null)
        {
            if (missing.Count != 0)
            {
                throw new ModuleContractException($"Missing graph catalog dependency '{missing.Min}'.");
            }
            if (cycle)
            {
                throw new ModuleContractException("Native graph catalog has no compatible closure because of a cycle.");
            }
            throw new ModuleContractException(incompatible
                ? "Native graph catalog has no compatible complete closure."
                : "Native graph catalog has no complete closure.");
        }
        return new NativeGraphCatalogSelection([.. rootBytes],
            [.. resolved.Select(static candidate => (ImmutableArray<byte>)[.. candidate.Bytes])],
            [.. resolved.Select(static candidate => candidate.Index)]);

        void Search()
        {
            string? next = null;
            foreach (var dependency in root.Dependencies)
            {
                ConsiderNext(dependency.Identity);
            }
            foreach (var candidate in selected.Values)
            {
                foreach (var dependency in candidate.Package.Dependencies)
                {
                    ConsiderNext(dependency.Identity);
                }
            }
            if (next is null)
            {
                var providers = providerBuffers[selected.Count];
                if (providers is null)
                {
                    providers = new GraphPackage[selected.Count];
                    providerBuffers[selected.Count] = providers;
                }
                var providerIndex = 0;
                foreach (var candidate in selected.Values)
                {
                    providers[providerIndex++] = candidate.Package;
                }
                Array.Sort(providers, static (left, right) =>
                    StringComparer.OrdinalIgnoreCase.Compare(left.Identity, right.Identity));
                try
                {
                    _ = CheckGraph(root, providers);
                }
                catch (ModuleContractException)
                {
                    incompatible = true;
                    return;
                }
                if (resolved is not null)
                {
                    throw new ModuleContractException("Native graph catalog has multiple compatible closures.");
                }
                resolved = [.. selected.Values.OrderBy(static item => item.Package.Identity, StringComparer.Ordinal)];
                return;
            }
            if (!catalog.TryGetValue(next, out var options))
            {
                if (catalogIdentities.ContainsKey(next))
                {
                    incompatible = true;
                }
                else
                {
                    _ = missing.Add(next);
                }
                return;
            }
            if (ambiguousCatalogIdentities.Contains(next))
            {
                throw new ModuleContractException($"Case-ambiguous graph catalog identity '{next}'.");
            }
            if (selected.Count == MaximumGraphProviderCount)
            {
                incompatible = true;
                return;
            }
            foreach (var option in options)
            {
                if (option.Package.Policy != root.Policy)
                {
                    incompatible = true;
                    continue;
                }
                if (assignments == MaximumCatalogAssignments)
                {
                    throw new ModuleContractException(
                        "Native graph catalog exceeded 4096 candidate assignments; uniqueness is unproven.");
                }
                assignments++;
                selected.Add(next, option);
                // Every recursive branch is acyclic on entry, so only the new provider can introduce a cycle.
                if (HasCycle(next))
                {
                    cycle = true;
                }
                else
                {
                    Search();
                }
                _ = selected.Remove(next);
            }

            void ConsiderNext(string identity)
            {
                if (!selected.ContainsKey(identity)
                    && (next is null || StringComparer.Ordinal.Compare(identity, next) < 0))
                {
                    next = identity;
                }
            }
        }

        bool HasCycle(string identity)
        {
            visiting.Clear();
            visited.Clear();
            return Visit(identity);

            bool Visit(string identity)
            {
                if (visited.Contains(identity))
                {
                    return false;
                }
                if (!visiting.Add(identity))
                {
                    return true;
                }
                foreach (var dependency in selected[identity].Package.Dependencies)
                {
                    if (selected.ContainsKey(dependency.Identity) && Visit(dependency.Identity))
                    {
                        return true;
                    }
                }
                _ = visiting.Remove(identity);
                _ = visited.Add(identity);
                return false;
            }
        }
    }
}
