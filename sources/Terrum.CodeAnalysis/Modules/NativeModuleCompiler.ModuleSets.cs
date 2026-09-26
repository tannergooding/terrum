// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Checked metadata for one persisted native consumer set.</summary>
public sealed record NativeModuleSetInfo(string NativeProfileVersion, NativeRuntimePolicy RuntimePolicy,
    string ImageSha256, IReadOnlyList<string> Dependencies);

public static partial class NativeModuleCompiler
{
    private const int SetMaximumBytes = 16 * 1024 * 1024;
    private const int SetHeaderSize = 16;

    /// <summary>Combines independent declarations without reading or rebinding producer source.</summary>
    public static CompilationReference CreateReferenceSet(IReadOnlyList<byte[]> producers)
    {
        var packages = ReadProviders(producers);
        return ModuleCompiler.CreateReferenceSet([.. packages.Select(static item => item.Contract)]);
    }

    /// <summary>Emits the consumer once, binding its imports to checked producer identities.</summary>
    public static byte[] CreateConsumerSet(BoundProgram program, IReadOnlyList<byte[]> producers,
        NativeRuntimePolicy runtimePolicy)
    {
        ArgumentNullException.ThrowIfNull(program);
        CheckPolicy(runtimePolicy);
        var providers = ReadProviders(producers);
        if (program.RuntimePolicy != (BoundRuntimePolicy)runtimePolicy)
        {
            throw new ModuleContractException("Consumer and selected native runtime policies differ.");
        }
        var contracts = providers.Select(static item => item.Contract).ToArray();
        var semantic = ModuleCompiler.CreateConsumerSet(program, contracts);
        var imports = ModuleCompiler.GetConsumerSetDeclarations(semantic).ToHashSet(StringComparer.Ordinal);
        var bindings = new List<NativeImportBinding>();
        foreach (var provider in providers)
        {
            var reference = ModuleCompiler.CreateReference(provider.Contract);
            foreach (var function in reference.Functions)
            {
                if (program.Functions.Any(item => item.IsExternal && item.Name == function.Name))
                {
                    bindings.Add(new NativeImportBinding(function.Name, DllName(provider.Identity), function.Name));
                }
            }
        }
        if (bindings.Count == 0 || !imports.SetEquals(bindings.Select(static item => item.FunctionName)))
        {
            throw new ModuleContractException("Persisted consumer calls differ from native import bindings.");
        }
        var (image, used) = NativeEmitter.EmitWithImportUsage(program, bindings, runtimePolicy);
        var artifact = WriteSet(providers, runtimePolicy, semantic, image, used);
        ValidateSet(ReadSet(artifact), providers);
        return artifact;
    }

    /// <summary>Checks exact dependencies, semantic contracts and native import ownership without launching.</summary>
    public static void ValidateConsumerSet(byte[] consumer, IReadOnlyList<byte[]> producers)
    {
        var executable = ReadSet(consumer);
        ValidateSet(executable, ReadProviders(producers));
    }

    /// <summary>Reads and validates bounded set metadata and the persisted PE image.</summary>
    public static NativeModuleSetInfo InspectConsumerSet(byte[] consumer)
    {
        var set = ReadSet(consumer);
        var imports = ModuleCompiler.GetConsumerSetDeclarations(set.Contract).ToHashSet(StringComparer.Ordinal);
        if (!imports.SetEquals(set.Dependencies.SelectMany(static item => item.Imports)))
        {
            throw new ModuleFormatException("Native set import mapping differs from its semantic declarations.");
        }
        NativeModuleImageValidator.Validate(set.Image, true, Imports(set), [], set.Policy);
        return new NativeModuleSetInfo(ModuleCompiler.GetNativeProfileVersion(set.Contract, true), set.Policy,
            Convert.ToHexString(SHA256.HashData(set.Image)), [.. set.Dependencies.Select(static item => item.Identity)]);
    }

    /// <summary>Stages only checked image snapshots and executes one unchanged consumer with all selected DLLs.</summary>
    public static async Task<int> RunConsumerSetAsync(byte[] consumer, IReadOnlyList<byte[]> producers,
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
        var set = ReadSet(consumer);
        var providers = ReadProviders(producers);
        if (set.Policy != runtimePolicy)
        {
            throw new ModuleContractException("Selected runtime policy differs from the native consumer.");
        }
        ValidateSet(set, providers);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Directory.CreateTempSubdirectory("terrum-native-set-").FullName;
        var paths = new List<string>();
        try
        {
            var executable = Path.Combine(directory, "consumer.exe");
            paths.Add(executable);
            await WriteNewAsync(executable, set.Image, cancellationToken).ConfigureAwait(false);
            foreach (var provider in providers)
            {
                var path = Path.Combine(directory, DllName(provider.Identity));
                paths.Add(path);
                await WriteNewAsync(path, provider.Image, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            using var process = Process.Start(new ProcessStartInfo(executable) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Could not start native consumer.");
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

    private static Package[] ReadProviders(IReadOnlyList<byte[]> producers)
    {
        ArgumentNullException.ThrowIfNull(producers);
        if (producers.Count is < 2 or > 16)
        {
            throw new ModuleContractException("A native module set requires 2 to 16 producers.");
        }
        var packages = producers.Select(Read).ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            ValidateProducer(package);
            if (!names.Add(package.Identity))
            {
                throw new ModuleContractException("Duplicate or case-ambiguous provider identity.");
            }
            if (package.Policy != packages[0].Policy)
            {
                throw new ModuleContractException("Native module set runtime policies differ.");
            }
        }
        return [.. packages.OrderBy(static item => item.Identity, StringComparer.OrdinalIgnoreCase)];
    }

    private static void ValidateSet(SetPackage set, Package[] providers)
    {
        if (set.Dependencies.Length != providers.Length || set.Policy != providers[0].Policy)
        {
            throw new ModuleContractException("Missing, extra or policy-incompatible native dependency.");
        }
        for (var i = 0; i < providers.Length; i++)
        {
            if (set.Dependencies[i].Identity != providers[i].Identity)
            {
                throw new ModuleContractException("Missing, extra or identity-incompatible native dependency.");
            }
            var exports = ModuleCompiler.CreateReference(providers[i].Contract).Functions
                .Select(static item => item.Name).ToArray();
            NativeModuleImageValidator.Validate(providers[i].Image, false, DllName(providers[i].Identity),
                exports, providers[i].Policy);
        }
        ModuleCompiler.ValidateConsumerSet(set.Contract, [.. providers.Select(static item => item.Contract)]);
        var actual = ModuleCompiler.GetConsumerSetDeclarations(set.Contract).ToHashSet(StringComparer.Ordinal);
        var mapped = set.Dependencies.SelectMany(static item => item.Imports).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(mapped))
        {
            throw new ModuleFormatException("Native set import mapping omits a called semantic import.");
        }
        var declarations = ModuleCompiler.CreateReferenceSet([.. providers.Select(static item => item.Contract)]);
        foreach (var (dependency, provider) in set.Dependencies.Zip(providers))
        {
            var exported = ModuleCompiler.CreateReference(provider.Contract).Functions
                .Select(static item => item.Name).ToHashSet(StringComparer.Ordinal);
            if (dependency.Imports.Length == 0 || dependency.Imports.Any(name =>
                !exported.Contains(name) || !declarations.Functions.Any(function => function.Name == name)))
            {
                throw new ModuleFormatException("Native set import mapping differs from provider declarations.");
            }
        }
        if (set.HasImportUsage)
        {
            var used = set.Dependencies.SelectMany(static item => item.MachineImports).ToHashSet(StringComparer.Ordinal);
            if (!ModuleCompiler.GetConsumerCallImports(set.Contract).All(used.Contains))
            {
                throw new ModuleFormatException("Native set machine imports omit a retained semantic call.");
            }
        }
        NativeModuleImageValidator.Validate(set.Image, true, Imports(set), [], set.Policy);
    }

    private static Dictionary<string, IReadOnlyList<string>> Imports(SetPackage set) =>
        set.Dependencies.Where(item => !set.HasImportUsage || item.MachineImports.Length != 0)
            .ToDictionary(static item => DllName(item.Identity),
                item => (IReadOnlyList<string>)(set.HasImportUsage ? item.MachineImports : item.Imports),
                StringComparer.Ordinal);

    private static byte[] WriteSet(Package[] providers, NativeRuntimePolicy policy, byte[] contract, byte[] image,
        IReadOnlyList<NativeImportBinding> used)
    {
        var imports = ModuleCompiler.GetConsumerSetDeclarations(contract).ToHashSet(StringComparer.Ordinal);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("TNSP"u8);
        writer.Write((byte)2);
        writer.Write((byte)policy);
        writer.Write((ushort)providers.Length);
        writer.Write(contract.Length);
        writer.Write(image.Length);
        foreach (var provider in providers)
        {
            var names = ModuleCompiler.CreateReference(provider.Contract).Functions
                .Select(static item => item.Name).Where(imports.Contains).Order(StringComparer.Ordinal).ToArray();
            if (names.Length == 0)
            {
                throw new ModuleContractException("Every selected dependency must supply a called import.");
            }
            writer.Write((byte)provider.Identity.Length);
            writer.Write(Encoding.ASCII.GetBytes(provider.Identity));
            writer.Write((ushort)names.Length);
            foreach (var name in names)
            {
                var bytes = Encoding.UTF8.GetBytes(name);
                if (bytes.Length is < 1 or > 1024)
                {
                    throw new ModuleFormatException("Import name exceeds bounded length.");
                }
                writer.Write((ushort)bytes.Length);
                writer.Write(bytes);
            }
            var actual = used.Where(item => item.DllName == DllName(provider.Identity))
                .Select(static item => item.FunctionName).Order(StringComparer.Ordinal).ToArray();
            if (actual.Any(name => !names.Contains(name, StringComparer.Ordinal)))
            {
                throw new ModuleFormatException("Native set machine import has no declaration owner.");
            }
            writer.Write(checked((ushort)actual.Length));
            foreach (var name in actual)
            {
                PutGraphString(writer, name);
            }
        }
        if (used.Count != providers.Sum(provider => used.Count(item => item.DllName == DllName(provider.Identity))))
        {
            throw new ModuleFormatException("Native set machine import has no selected provider.");
        }
        writer.Write(contract);
        writer.Write(image);
        if (stream.Length + HashSize > SetMaximumBytes)
        {
            throw new ModuleFormatException("Native module set exceeds its artifact size limit.");
        }
        var bytesOut = stream.ToArray();
        return [.. bytesOut, .. SHA256.HashData(bytesOut)];
    }

    private static SetPackage ReadSet(byte[] artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Length is < SetHeaderSize + HashSize or > SetMaximumBytes)
        {
            throw new ModuleFormatException("Invalid native module set bounds.");
        }
        var bytes = (byte[])artifact.Clone();
        if (!bytes.AsSpan(0, 4).SequenceEqual("TNSP"u8) || bytes[4] is not (1 or 2)
            || bytes[5] is not ((byte)NativeRuntimePolicy.FailFast or (byte)NativeRuntimePolicy.CheckedExceptions)
            || !SHA256.HashData(bytes.AsSpan(0, bytes.Length - HashSize))
                .AsSpan().SequenceEqual(bytes.AsSpan(bytes.Length - HashSize)))
        {
            throw new ModuleFormatException("Invalid native module set version, policy or checksum.");
        }
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6));
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
        var imageLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
        if (count is < 2 or > 16 || contractLength is < 1 or > 4 * 1024 * 1024 || imageLength < 1
            || (long)contractLength + imageLength + SetHeaderSize + HashSize > bytes.Length)
        {
            throw new ModuleFormatException("Invalid native module set lengths or dependency count.");
        }
        var at = SetHeaderSize;
        var dependencies = new SetDependency[count];
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            if (at >= bytes.Length - HashSize || bytes[at] is < 1 or > 64
                || (long)at + 1 + bytes[at] + 2 > bytes.Length - HashSize)
            {
                throw new ModuleFormatException("Invalid native dependency identity bounds.");
            }
            var length = bytes[at++];
            var identity = Encoding.ASCII.GetString(bytes, at, length);
            at += length;
            if (!ValidIdentity(identity) || !identities.Add(identity)
                || (i != 0 && StringComparer.OrdinalIgnoreCase.Compare(dependencies[i - 1].Identity, identity) >= 0))
            {
                throw new ModuleFormatException("Invalid, duplicate or unsorted native dependency identity.");
            }
            var importsCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
            at += 2;
            if (importsCount is < 1 or > 1024)
            {
                throw new ModuleFormatException("Invalid native import count.");
            }
            var imports = new string[importsCount];
            for (var j = 0; j < importsCount; j++)
            {
                if (at + 2 > bytes.Length - HashSize)
                {
                    throw new ModuleFormatException("Truncated native import length.");
                }
                var size = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
                at += 2;
                if (size is < 1 or > 1024 || (long)at + size > bytes.Length - HashSize)
                {
                    throw new ModuleFormatException("Invalid native import bounds.");
                }
                string name;
                try
                {
                    name = new UTF8Encoding(false, true).GetString(bytes, at, size);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new ModuleFormatException("Invalid native import encoding.", exception);
                }
                at += size;
                if (!symbols.Add(name) || (j != 0 && StringComparer.Ordinal.Compare(imports[j - 1], name) >= 0))
                {
                    throw new ModuleFormatException("Duplicate or unsorted native import.");
                }
                imports[j] = name;
            }
            var machineImports = imports;
            if (bytes[4] == 2)
            {
                var actualCount = ReadGraphCount(bytes, ref at, 1024);
                machineImports = new string[actualCount];
                for (var j = 0; j < actualCount; j++)
                {
                    var name = ReadGraphString(bytes, ref at, 256);
                    if (!ValidGraphSymbol(name) || !imports.Contains(name, StringComparer.Ordinal)
                        || (j != 0 && StringComparer.Ordinal.Compare(machineImports[j - 1], name) >= 0))
                    {
                        throw new ModuleFormatException("Invalid or unordered native set machine import.");
                    }
                    machineImports[j] = name;
                }
            }
            dependencies[i] = new SetDependency(identity, imports) {
                MachineImports = machineImports,
            };
        }
        if ((long)at + contractLength + imageLength + HashSize != bytes.Length)
        {
            throw new ModuleFormatException("Invalid native set payload bounds.");
        }
        var contract = bytes.AsSpan(at, contractLength).ToArray();
        var (_, semanticPolicy) = ModuleCompiler.ReadNativeProfile(contract, true);
        if ((byte)semanticPolicy != bytes[5])
        {
            throw new ModuleFormatException("Native set and semantic runtime policies differ.");
        }
        if (bytes[4] == 2
            && (!ModuleCompiler.GetConsumerSetDeclarations(contract).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(dependencies.SelectMany(static item => item.Imports))
                || !ModuleCompiler.GetConsumerCallImports(contract).All(
                    name => dependencies.Any(item => item.MachineImports.Contains(name, StringComparer.Ordinal)))))
        {
            throw new ModuleFormatException("Native set declaration or machine imports differ from semantic contract.");
        }
        return new SetPackage((NativeRuntimePolicy)bytes[5], contract,
            bytes.AsSpan(at + contractLength, imageLength).ToArray(), dependencies) {
            HasImportUsage = bytes[4] == 2,
        };
    }

    private sealed record SetDependency(string Identity, string[] Imports)
    {
        public string[] MachineImports { get; init; } = [];
    }
    private sealed record SetPackage(NativeRuntimePolicy Policy, byte[] Contract, byte[] Image, SetDependency[] Dependencies)
    {
        public bool HasImportUsage { get; init; }
    }
}
