// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class NativeModuleCompiler
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int MaximumGraphProviderCount = 16;
    private const int GraphHeaderSize = 19;
    private const int GraphMaximumBytes = 16 * 1024 * 1024;

    private sealed record GraphDependency(string Identity, string[] Declarations, string[] Imports);
    private sealed record GraphTypeOrigin(string Name, string Identity, string? CleanupIdentity);
    private sealed record GraphPackage(bool Consumer, string? Identity, NativeRuntimePolicy Policy,
        string[] Exports, GraphDependency[] Dependencies, byte[] Contract, byte[] Image)
    {
        public bool IsLeaf { get; init; }
        public GraphTypeOrigin[] Origins { get; init; } = [];
        public CompilationReference? ValidatedReference { get; init; }
    }

    private static GraphPackage[] ReadGraphProviders(IReadOnlyList<byte[]> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ValidateGraphProviderCount(artifacts.Count);
        return ValidateGraphProviders(artifacts.Select(ReadGraphPackage).ToArray());
    }

    private static GraphPackage[] ReadGraphProviderSnapshots(IReadOnlyList<byte[]> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ValidateGraphProviderCount(snapshots.Count);
        return ValidateGraphProviders(snapshots.Select(ReadGraphPackageSnapshot).ToArray());
    }

    private static GraphPackage[] ValidateGraphProviders(GraphPackage[] providers)
    {
        ValidateGraphProviderCount(providers.Length);
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (provider.Consumer || provider.Identity is not string identity || !identities.Add(identity))
            {
                throw new ModuleContractException("Duplicate, ambiguous or consumer graph dependency.");
            }
            if (provider.Policy != providers[0].Policy)
            {
                throw new ModuleContractException("Native graph runtime policies differ.");
            }
        }
        return [.. providers.OrderBy(static item => item.Identity, StringComparer.OrdinalIgnoreCase)];
    }

    private static void ValidateGraphProviderCount(int count)
    {
        if (count is < 1 or > MaximumGraphProviderCount)
        {
            throw new ModuleContractException("Native graphs require 1 to 16 selected producers.");
        }
    }

    private static GraphPackage ReadGraphPackage(byte[] artifact) => ReadGraphPackage(artifact, isSnapshot: false);

    private static GraphPackage ReadGraphPackageSnapshot(byte[] snapshot) =>
        ReadGraphPackage(snapshot, isSnapshot: true);

    private static GraphPackage ReadGraphPackage(byte[] artifact, bool isSnapshot)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.AsSpan().StartsWith("TNPK"u8))
        {
            var leaf = Read(artifact);
            var leafReference = ValidateProducer(leaf);
            return new GraphPackage(false, leaf.Identity, leaf.Policy,
                [.. leafReference.Functions.Select(static item => item.Name)
                    .Order(StringComparer.Ordinal)], [], leaf.Contract, leaf.Image) {
                IsLeaf = true,
                ValidatedReference = leafReference,
            };
        }
        if (artifact.Length is < GraphHeaderSize + HashSize or > GraphMaximumBytes)
        {
            throw new ModuleFormatException("Native graph package has invalid bounds.");
        }
        var bytes = isSnapshot ? artifact : (byte[])artifact.Clone();
        if (!bytes.AsSpan(0, 4).SequenceEqual("TNGP"u8) || bytes[4] != 2
            || bytes[5] is not (1 or 2)
            || bytes[6] is not ((byte)NativeRuntimePolicy.FailFast or (byte)NativeRuntimePolicy.CheckedExceptions)
            || !SHA256.HashData(bytes.AsSpan(0, bytes.Length - HashSize))
                .AsSpan().SequenceEqual(bytes.AsSpan(bytes.Length - HashSize)))
        {
            throw new ModuleFormatException("Unknown graph package version, kind, policy or checksum.");
        }
        var consumer = bytes[5] == 2;
        var identityLength = bytes[7];
        var dependencyCount = bytes[8];
        var exportCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(9));
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(11));
        var imageLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(15));
        if ((consumer && identityLength != 0) || (!consumer && identityLength is < 1 or > 64)
            || dependencyCount is < 1 or > MaximumGraphProviderCount || exportCount > 1024
            || (consumer ? exportCount != 0 : exportCount == 0)
            || contractLength is < 1 or > MaximumContractBytes || imageLength < 1
            || (long)GraphHeaderSize + identityLength + contractLength + imageLength + HashSize > bytes.Length)
        {
            throw new ModuleFormatException("Graph package counts and lengths are invalid.");
        }
        var at = GraphHeaderSize;
        var identity = identityLength == 0 ? null : Encoding.ASCII.GetString(bytes, at, identityLength);
        if (identity is not null && !ValidIdentity(identity))
        {
            throw new ModuleFormatException("Invalid graph provider identity.");
        }
        at += identityLength;
        var dependencies = new GraphDependency[dependencyCount];
        var seenImports = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < dependencies.Length; i++)
        {
            var dependencyName = ReadGraphString(bytes, ref at, 64);
            if (!ValidIdentity(dependencyName)
                || (i != 0 && StringComparer.OrdinalIgnoreCase.Compare(dependencies[i - 1].Identity, dependencyName) >= 0))
            {
                throw new ModuleFormatException("Duplicate or unordered graph dependency identity.");
            }
            var importCount = ReadGraphCount(bytes, ref at, 1024);
            var declarations = new string[importCount];
            for (var j = 0; j < declarations.Length; j++)
            {
                var name = ReadGraphString(bytes, ref at, 256);
                if (!ValidGraphSymbol(name) || !seenImports.Add(name)
                    || (j != 0 && StringComparer.Ordinal.Compare(declarations[j - 1], name) >= 0))
                {
                    throw new ModuleFormatException("Duplicate, unordered or invalid graph declaration.");
                }
                declarations[j] = name;
            }
            var actualCount = ReadGraphCount(bytes, ref at, 1024);
            var imports = new string[actualCount];
            for (var j = 0; j < imports.Length; j++)
            {
                var name = ReadGraphString(bytes, ref at, 256);
                if (!ValidGraphSymbol(name)
                    || Array.BinarySearch(declarations, name, StringComparer.Ordinal) < 0
                    || (j != 0 && StringComparer.Ordinal.Compare(imports[j - 1], name) >= 0))
                {
                    throw new ModuleFormatException("Graph machine import is not a declared dependency.");
                }
                imports[j] = name;
            }
            dependencies[i] = new GraphDependency(dependencyName, declarations, imports);
        }
        if (seenImports.Count > 1024)
        {
            throw new ModuleFormatException("Graph import total exceeds its bound.");
        }
        var exports = new string[exportCount];
        for (var i = 0; i < exports.Length; i++)
        {
            var name = ReadGraphString(bytes, ref at, 256);
            if (!ValidGraphSymbol(name)
                || (i != 0 && StringComparer.Ordinal.Compare(exports[i - 1], name) >= 0))
            {
                throw new ModuleFormatException("Duplicate, unordered or invalid graph export.");
            }
            exports[i] = name;
        }
        var origins = new GraphTypeOrigin[ReadGraphCount(bytes, ref at, 1024)];
        for (var i = 0; i < origins.Length; i++)
        {
            var name = ReadGraphString(bytes, ref at, 256);
            var owner = ReadGraphString(bytes, ref at, 64);
            var cleanup = ReadGraphCount(bytes, ref at, 64);
            string? cleanupOwner = null;
            if (cleanup != 0)
            {
                if ((long)at + cleanup > bytes.Length - HashSize)
                {
                    throw new ModuleFormatException("Truncated graph cleanup owner.");
                }
                cleanupOwner = Encoding.ASCII.GetString(bytes, at, cleanup);
                at += cleanup;
            }
            if (!ValidGraphSymbol(name)
                || (owner != "@" && !ValidIdentity(owner))
                || (cleanupOwner is not null && cleanupOwner != "@" && !ValidIdentity(cleanupOwner))
                || (i != 0 && StringComparer.Ordinal.Compare(origins[i - 1].Name, name) >= 0))
            {
                throw new ModuleFormatException("Invalid graph type origin metadata.");
            }
            origins[i] = new GraphTypeOrigin(name, owner, cleanupOwner);
        }
        if ((long)at + contractLength + imageLength + HashSize != bytes.Length)
        {
            throw new ModuleFormatException("Graph package payload bounds differ from its header.");
        }
        var contract = bytes.AsSpan(at, contractLength).ToArray();
        var policy = (NativeRuntimePolicy)bytes[6];
        CompilationReference? reference = null;
        BoundRuntimePolicy contractPolicy;
        if (consumer)
        {
            contractPolicy = ModuleCompiler.ReadNativeProfile(contract, true).RuntimePolicy;
        }
        else
        {
            reference = ModuleCompiler.CreateGraphReference(contract, exports, allowImportedCleanup: true);
            contractPolicy = reference.RuntimePolicy;
        }
        if (contractPolicy != (BoundRuntimePolicy)policy)
        {
            throw new ModuleFormatException("Graph package and semantic runtime policies differ.");
        }
        var image = bytes.AsSpan(at + contractLength, imageLength).ToArray();
        var package = new GraphPackage(consumer, identity, policy, exports, dependencies, contract, image) {
            Origins = origins,
            ValidatedReference = reference,
        };
        var imported = consumer ? ModuleCompiler.GetConsumerSetDeclarations(contract)
            : ModuleCompiler.GetGraphImports(contract);
        if (!seenImports.SetEquals(imported))
        {
            throw new ModuleFormatException("Graph declaration mapping differs from semantic declarations.");
        }
        var structures = ModuleCompiler.GetGraphRetainedTypes(contract, consumer);
        if (structures.Length != origins.Length)
        {
            throw new ModuleFormatException("Graph origin table differs from retained semantic types.");
        }
        var originsByName = origins.ToDictionary(static item => item.Name, StringComparer.Ordinal);
        if (structures.Any(item => !originsByName.TryGetValue(item.Name, out var origin)
            || (item.CleanupMethod is not null ? origin.CleanupIdentity is null : origin.CleanupIdentity is not null)))
        {
            throw new ModuleFormatException("Graph origin table differs from retained semantic types.");
        }
        var importedTypes = ModuleCompiler.GetGraphImportedTypes(contract, consumer)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string>? dependencyIdentities = null;
        foreach (var origin in origins)
        {
            var local = consumer ? "@" : identity;
            if (importedTypes.Contains(origin.Name))
            {
                dependencyIdentities ??= dependencies.Select(static item => item.Identity)
                    .ToHashSet(StringComparer.Ordinal);
                if (!dependencyIdentities.Contains(origin.Identity))
                {
                    throw new ModuleFormatException("Graph type origin has no declared owner.");
                }
            }
            else if (origin.Identity != local)
            {
                throw new ModuleFormatException("Graph type origin has no declared owner.");
            }
            if (origin.CleanupIdentity is not null && origin.CleanupIdentity != origin.Identity)
            {
                throw new ModuleFormatException("Graph cleanup owner differs from type origin.");
            }
        }
        if (dependencies.Any(static item => item.Declarations.Length == 0))
        {
            var originIdentities = origins.Select(static item => item.Identity)
                .ToHashSet(StringComparer.Ordinal);
            if (dependencies.Any(item => item.Declarations.Length == 0
                && !originIdentities.Contains(item.Identity)))
            {
                throw new ModuleFormatException("Graph dependency supplies neither declarations nor types.");
            }
        }
        var profile = consumer
            ? ModuleCompiler.GetNativeProfileVersion(contract, true)
            : ModuleCompiler.GetGraphProfile(contract);
        NativeModuleImageValidator.Validate(image, consumer, GraphImports(package), exports, policy,
            allowOwnedBufferRuntime: AllowsOwnedBufferRuntime(profile),
            allowManagedRuntimeData: AllowsManagedRuntimeData(profile));
        return package;
    }

    private static byte[] WriteGraphPackage(GraphPackage package)
    {
        CheckPolicy(package.Policy);
        if (package.Consumer ? package.Identity is not null || package.Exports.Length != 0
            : package.Identity is not string name || !ValidIdentity(name) || package.Exports.Length == 0)
        {
            throw new ModuleFormatException("Graph package kind has invalid identity or exports.");
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("TNGP"u8);
        writer.Write((byte)2);
        writer.Write(package.Consumer ? (byte)2 : (byte)1);
        writer.Write((byte)package.Policy);
        writer.Write(checked((byte)(package.Identity?.Length ?? 0)));
        writer.Write(checked((byte)package.Dependencies.Length));
        writer.Write(checked((ushort)package.Exports.Length));
        writer.Write(package.Contract.Length);
        writer.Write(package.Image.Length);
        if (package.Identity is string identity)
        {
            writer.Write(Encoding.ASCII.GetBytes(identity));
        }
        foreach (var dependency in package.Dependencies)
        {
            PutGraphString(writer, dependency.Identity);
            writer.Write(checked((ushort)dependency.Declarations.Length));
            foreach (var imported in dependency.Declarations)
            {
                PutGraphString(writer, imported);
            }
            writer.Write(checked((ushort)dependency.Imports.Length));
            foreach (var imported in dependency.Imports)
            {
                PutGraphString(writer, imported);
            }
        }
        foreach (var exported in package.Exports)
        {
            PutGraphString(writer, exported);
        }
        writer.Write(checked((ushort)package.Origins.Length));
        foreach (var origin in package.Origins)
        {
            PutGraphString(writer, origin.Name);
            PutGraphString(writer, origin.Identity);
            if (origin.CleanupIdentity is string cleanup)
            {
                PutGraphString(writer, cleanup);
            }
            else
            {
                writer.Write((ushort)0);
            }
        }
        writer.Write(package.Contract);
        writer.Write(package.Image);
        if (stream.Length + HashSize > GraphMaximumBytes)
        {
            throw new ModuleFormatException("Native graph package exceeds its artifact size limit.");
        }
        var result = stream.ToArray();
        return [.. result, .. SHA256.HashData(result)];
    }

    private static GraphDependency[] GraphBindings(IReadOnlyList<GraphPackage> providers,
        IReadOnlyCollection<string> declarations)
    {
        var unmatched = declarations.ToHashSet(StringComparer.Ordinal);
        var result = new List<GraphDependency>();
        foreach (var provider in providers)
        {
            // Every leaf and graph package exposes ordinally sorted exports.
            var imports = provider.Exports.Where(unmatched.Remove).ToArray();
            result.Add(new GraphDependency(provider.Identity
                ?? throw new ModuleFormatException("Graph dependency has no identity."), imports, []));
        }
        if (unmatched.Count != 0)
        {
            throw new ModuleContractException("Missing graph declaration.");
        }
        return [.. result.OrderBy(static item => item.Identity, StringComparer.OrdinalIgnoreCase)];
    }

    private static Dictionary<string, IReadOnlyList<string>> GraphImports(GraphPackage package) =>
        package.Dependencies.Where(static item => item.Imports.Length != 0)
            .ToDictionary(static item => DllName(item.Identity),
            static item => (IReadOnlyList<string>)item.Imports, StringComparer.Ordinal);

    private static GraphDependency[] WithGraphUsage(GraphDependency[] declarations,
        IReadOnlyList<NativeImportBinding> used)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var usedByDll = new Dictionary<string, List<NativeImportBinding>>(StringComparer.Ordinal);
        foreach (var import in used)
        {
            if (!usedByDll.TryGetValue(import.DllName, out var imports))
            {
                imports = [];
                usedByDll.Add(import.DllName, imports);
            }
            imports.Add(import);
        }
        var result = new GraphDependency[declarations.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var dependency = declarations[i];
            var actual = usedByDll.TryGetValue(DllName(dependency.Identity), out var providerImports)
                ? providerImports : [];
            var imports = new string[actual.Count];
            for (var index = 0; index < actual.Count; index++)
            {
                var import = actual[index];
                if (import.ExportName != import.FunctionName
                    || !dependency.Declarations.Contains(import.FunctionName, StringComparer.Ordinal)
                    || !names.Add(import.FunctionName))
                {
                    throw new ModuleFormatException("Emitted graph import has no declaration owner.");
                }
                imports[index] = import.FunctionName;
            }
            Array.Sort(imports, StringComparer.Ordinal);
            result[i] = dependency with { Imports = imports };
        }
        if (names.Count != used.Count)
        {
            throw new ModuleFormatException("Emitted graph import has no selected dependency.");
        }
        return result;
    }

    private static int ReadGraphCount(byte[] bytes, ref int at, int maximum)
    {
        if ((long)at + 2 > bytes.Length - HashSize)
        {
            throw new ModuleFormatException("Truncated graph count.");
        }
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
        at += 2;
        if (count > maximum)
        {
            throw new ModuleFormatException("Graph count exceeds its bound.");
        }
        return count;
    }

    private static string ReadGraphString(byte[] bytes, ref int at, int maximum)
    {
        var length = ReadGraphCount(bytes, ref at, maximum);
        if (length == 0 || (long)at + length > bytes.Length - HashSize)
        {
            throw new ModuleFormatException("Graph string exceeds payload bounds.");
        }
        try
        {
            var result = StrictUtf8.GetString(bytes, at, length);
            at += length;
            return result;
        }
        catch (DecoderFallbackException exception)
        {
            throw new ModuleFormatException("Invalid graph string encoding.", exception);
        }
    }

    private static void PutGraphString(BinaryWriter writer, string value)
    {
        var bytes = StrictUtf8.GetBytes(value);
        if (bytes.Length is < 1 or > 256)
        {
            throw new ModuleFormatException("Graph string exceeds its bound.");
        }
        writer.Write(checked((ushort)bytes.Length));
        writer.Write(bytes);
    }

    private static bool ValidGraphSymbol(string value) => value.Length is >= 1 and <= 256
        && value.All(static character => character is >= '!' and <= '~');
}
