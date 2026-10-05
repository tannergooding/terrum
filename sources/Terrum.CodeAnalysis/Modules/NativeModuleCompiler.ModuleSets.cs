// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
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

    private sealed record ValidatedProvider(Package Package, CompilationReference Reference);

    /// <summary>Combines independent declarations without reading or rebinding producer source.</summary>
    public static CompilationReference CreateReferenceSet(IReadOnlyList<byte[]> producers)
    {
        var packages = ReadProviders(producers);
        return CreateReferenceSet(packages);
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
        var referenceSet = CreateReferenceSet(providers);
        var semantic = ModuleCompiler.CreateConsumerAgainstReference(program, referenceSet,
            allowManagedParticipant: true);
        var contractSnapshot = ModuleCompiler.ReadConsumerContractSnapshot(semantic);
        var imports = contractSnapshot.Declarations.ToHashSet(StringComparer.Ordinal);
        var externalFunctionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            if (function.IsExternal)
            {
                _ = externalFunctionNames.Add(function.Name);
            }
        }
        var bindings = new List<NativeImportBinding>(imports.Count);
        foreach (var provider in providers)
        {
            foreach (var function in provider.Reference.Functions)
            {
                if (externalFunctionNames.Contains(function.Name))
                {
                    bindings.Add(new NativeImportBinding(function.Name, DllName(provider.Package.Identity), function.Name));
                }
            }
        }
        if (bindings.Count == 0 || !imports.SetEquals(bindings.Select(static item => item.FunctionName)))
        {
            throw new ModuleContractException("Persisted consumer calls differ from native import bindings.");
        }
        var (image, used) = NativeEmitter.EmitWithImportUsage(program, bindings, runtimePolicy);
        var artifact = WriteSet(providers, runtimePolicy, semantic, image, used, contractSnapshot);
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
        var profile = set.ContractSnapshot.Profile;
        NativeModuleImageValidator.Validate(set.Image, true, Imports(set), [], set.Policy,
            allowOwnedBufferRuntime: AllowsOwnedBufferRuntime(profile),
            allowManagedRuntimeData: AllowsManagedRuntimeData(profile));
        var dependencies = new string[set.Dependencies.Length];
        for (var i = 0; i < dependencies.Length; i++)
        {
            dependencies[i] = set.Dependencies[i].Identity;
        }
        return new NativeModuleSetInfo(profile, set.Policy,
            Convert.ToHexString(SHA256.HashData(set.Image)), dependencies);
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
        var paths = new List<string>(providers.Length + 1);
        try
        {
            var executable = Path.Combine(directory, "consumer.exe");
            paths.Add(executable);
            await WriteNewAsync(executable, set.Image, cancellationToken).ConfigureAwait(false);
            foreach (var provider in providers)
            {
                var path = Path.Combine(directory, DllName(provider.Package.Identity));
                paths.Add(path);
                await WriteNewAsync(path, provider.Package.Image, cancellationToken).ConfigureAwait(false);
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

    private static ValidatedProvider[] ReadProviders(IReadOnlyList<byte[]> producers)
    {
        ArgumentNullException.ThrowIfNull(producers);
        if (producers.Count is < 2 or > 16)
        {
            throw new ModuleContractException("A native module set requires 2 to 16 producers.");
        }
        var packages = new Package[producers.Count];
        for (var i = 0; i < packages.Length; i++)
        {
            packages[i] = Read(producers[i]);
        }
        var providers = new ValidatedProvider[packages.Length];
        var names = new HashSet<string>(packages.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < packages.Length; i++)
        {
            var package = packages[i];
            var reference = ValidateProducer(package);
            if (!names.Add(package.Identity))
            {
                throw new ModuleContractException("Duplicate or case-ambiguous provider identity.");
            }
            if (package.Policy != packages[0].Policy)
            {
                throw new ModuleContractException("Native module set runtime policies differ.");
            }
            providers[i] = new ValidatedProvider(package, reference);
        }
        Array.Sort(providers, static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Package.Identity, right.Package.Identity));
        return providers;
    }

    private static void ValidateSet(SetPackage set, ValidatedProvider[] providers)
    {
        if (set.Dependencies.Length != providers.Length || set.Policy != providers[0].Package.Policy)
        {
            throw new ModuleContractException("Missing, extra or policy-incompatible native dependency.");
        }
        var providerExports = new HashSet<string>[providers.Length];
        for (var i = 0; i < providers.Length; i++)
        {
            var provider = providers[i];
            if (set.Dependencies[i].Identity != provider.Package.Identity)
            {
                throw new ModuleContractException("Missing, extra or identity-incompatible native dependency.");
            }
            var functions = provider.Reference.Functions;
            var exported = new HashSet<string>(functions.Length, StringComparer.Ordinal);
            foreach (var function in functions)
            {
                _ = exported.Add(function.Name);
            }
            providerExports[i] = exported;
        }
        var referenceSet = CreateReferenceSet(providers);
        set.ContractSnapshot.ValidateAgainstReference(referenceSet);
        for (var i = 0; i < providers.Length; i++)
        {
            var dependency = set.Dependencies[i];
            var exported = providerExports[i];
            foreach (var name in dependency.Imports)
            {
                if (!exported.Contains(name))
                {
                    throw new ModuleFormatException("Native set import mapping differs from provider declarations.");
                }
            }
        }
        var profile = set.ContractSnapshot.Profile;
        NativeModuleImageValidator.Validate(set.Image, true, Imports(set), [], set.Policy,
            allowOwnedBufferRuntime: AllowsOwnedBufferRuntime(profile),
            allowManagedRuntimeData: AllowsManagedRuntimeData(profile));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Imports(SetPackage set)
    {
        var count = 0;
        foreach (var dependency in set.Dependencies)
        {
            if (dependency.MachineImports.Length != 0)
            {
                count++;
            }
        }
        if (count == 0)
        {
            return ImmutableDictionary<string, IReadOnlyList<string>>.Empty;
        }
        var imports = new Dictionary<string, IReadOnlyList<string>>(count, StringComparer.Ordinal);
        foreach (var dependency in set.Dependencies)
        {
            if (dependency.MachineImports.Length != 0)
            {
                imports.Add(DllName(dependency.Identity), dependency.MachineImports);
            }
        }
        return imports;
    }

    private static byte[] WriteSet(ValidatedProvider[] providers, NativeRuntimePolicy policy, byte[] contract, byte[] image,
        IReadOnlyList<NativeImportBinding> used, ModuleCompiler.ConsumerContractSnapshot contractSnapshot)
    {
        var imports = contractSnapshot.Declarations.ToHashSet(StringComparer.Ordinal);
        var usedByDll = new Dictionary<string, List<string>>(Math.Min(providers.Length, used.Count),
            StringComparer.Ordinal);
        foreach (var import in used)
        {
            if (!usedByDll.TryGetValue(import.DllName, out var names))
            {
                names = [];
                usedByDll.Add(import.DllName, names);
            }
            names.Add(import.FunctionName);
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("TNSP"u8);
        writer.Write((byte)2);
        writer.Write((byte)policy);
        writer.Write((ushort)providers.Length);
        writer.Write(contract.Length);
        writer.Write(image.Length);
        var matchedImportCount = 0;
        foreach (var provider in providers)
        {
            var functions = provider.Reference.Functions;
            var nameCount = 0;
            foreach (var function in functions)
            {
                if (imports.Contains(function.Name))
                {
                    nameCount++;
                }
            }
            if (nameCount == 0)
            {
                throw new ModuleContractException("Every selected dependency must supply a called import.");
            }
            var names = new string[nameCount];
            var nameIndex = 0;
            foreach (var function in functions)
            {
                if (imports.Contains(function.Name))
                {
                    names[nameIndex++] = function.Name;
                }
            }
            Array.Sort(names, StringComparer.Ordinal);
            writer.Write((byte)provider.Package.Identity.Length);
            WriteAsciiIdentity(writer, provider.Package.Identity);
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
            var actualCount = 0;
            if (usedByDll.TryGetValue(DllName(provider.Package.Identity), out var providerImports))
            {
                providerImports.Sort(StringComparer.Ordinal);
                actualCount = providerImports.Count;
                matchedImportCount += actualCount;
                foreach (var name in providerImports)
                {
                    if (Array.BinarySearch(names, name, StringComparer.Ordinal) < 0)
                    {
                        throw new ModuleFormatException("Native set machine import has no declaration owner.");
                    }
                }
            }
            writer.Write(checked((ushort)actualCount));
            if (providerImports is not null)
            {
                foreach (var name in providerImports)
                {
                    PutGraphString(writer, name);
                }
            }
        }
        if (used.Count != matchedImportCount)
        {
            throw new ModuleFormatException("Native set machine import has no selected provider.");
        }
        writer.Write(contract);
        writer.Write(image);
        if (stream.Length + HashSize > SetMaximumBytes)
        {
            throw new ModuleFormatException("Native module set exceeds its artifact size limit.");
        }
        return AppendChecksum(stream);
    }

    private static CompilationReference CreateReferenceSet(ValidatedProvider[] providers)
    {
        var references = new CompilationReference[providers.Length];
        for (var i = 0; i < references.Length; i++)
        {
            references[i] = providers[i].Reference;
        }
        return ModuleCompiler.CreateReferenceSetFromReferences(references);
    }

    private static SetPackage ReadSet(byte[] artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Length is < SetHeaderSize + HashSize or > SetMaximumBytes)
        {
            throw new ModuleFormatException("Invalid native module set bounds.");
        }
        var bytes = (byte[])artifact.Clone();
        if (!bytes.AsSpan(0, 4).SequenceEqual("TNSP"u8) || bytes[4] != 2
            || bytes[5] is not ((byte)NativeRuntimePolicy.FailFast or (byte)NativeRuntimePolicy.CheckedExceptions)
            || !HasValidChecksum(bytes))
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
        var identities = new HashSet<string>(count, StringComparer.OrdinalIgnoreCase);
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        var allMachineImports = new HashSet<string>(StringComparer.Ordinal);
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
                    name = StrictUtf8.GetString(bytes, at, size);
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
            var actualCount = ReadGraphCount(bytes, ref at, 1024);
            var machineImports = new string[actualCount];
            for (var j = 0; j < actualCount; j++)
            {
                var name = ReadGraphString(bytes, ref at, 256);
                if (!ValidGraphSymbol(name) || Array.BinarySearch(imports, name, StringComparer.Ordinal) < 0
                    || (j != 0 && StringComparer.Ordinal.Compare(machineImports[j - 1], name) >= 0))
                {
                    throw new ModuleFormatException("Invalid or unordered native set machine import.");
                }
                _ = allMachineImports.Add(name);
                machineImports[j] = name;
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
        var contractSnapshot = ModuleCompiler.ReadConsumerContractSnapshot(contract);
        if ((byte)contractSnapshot.RuntimePolicy != bytes[5])
        {
            throw new ModuleFormatException("Native set and semantic runtime policies differ.");
        }
        if (!symbols.SetEquals(contractSnapshot.Declarations)
            || !contractSnapshot.CallImports.All(allMachineImports.Contains))
        {
            throw new ModuleFormatException("Native set declaration or machine imports differ from semantic contract.");
        }
        return new SetPackage((NativeRuntimePolicy)bytes[5], contract,
            bytes.AsSpan(at + contractLength, imageLength).ToArray(), dependencies, contractSnapshot);
    }

    private sealed record SetDependency(string Identity, string[] Imports)
    {
        public string[] MachineImports { get; init; } = [];
    }
    private sealed record SetPackage(NativeRuntimePolicy Policy, byte[] Contract, byte[] Image,
        SetDependency[] Dependencies, ModuleCompiler.ConsumerContractSnapshot ContractSnapshot);
}
