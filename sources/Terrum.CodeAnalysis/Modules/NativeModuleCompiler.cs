// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Metadata for a checked, persisted native producer or consumer package.</summary>
public sealed record NativeModuleInfo(string Identity, bool IsConsumer, string ImageSha256, int ImageLength, int ContractLength)
{
    /// <summary>The checked-failure policy encoded in this package and its semantic contract.</summary>
    public NativeRuntimePolicy RuntimePolicy { get; init; }

    /// <summary>The native semantic profile encoded in the checked package and its retained value contracts.</summary>
    public string NativeProfileVersion { get; init; } = Compilation.ProfileVersion;
}

/// <summary>Creates and checks single-provider Windows x64 native packages; this is not an authenticated code loader.</summary>
public static partial class NativeModuleCompiler
{
    private const int Version = 2;
    private const int MaximumBytes = 16 * 1024 * 1024;
    private const int MaximumContractBytes = 4 * 1024 * 1024;
    private const int HeaderSize = 18;
    private const int HashSize = 32;

    /// <summary>Stores a verified semantic producer contract and its independently emitted DLL.</summary>
    public static byte[] CreateProducer(BoundProgram program, IReadOnlyList<string> exports, string identity,
        NativeRuntimePolicy runtimePolicy)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(exports);
        CheckIdentity(identity);
        CheckPolicy(runtimePolicy);
        var names = exports.ToArray();
        var contract = ModuleCompiler.CreateProducer(program, names);
        var image = NativeEmitter.EmitLibrary(program, names, runtimePolicy);
        var package = Write(isConsumer: false, identity, runtimePolicy, contract, image);
        ValidateProducer(Read(package));
        return package;
    }

    /// <summary>Exposes the persisted producer declarations without reading source or emitting machine code.</summary>
    public static CompilationReference CreateReference(byte[] producer)
    {
        var package = Read(producer);
        return ValidateProducer(package);
    }

    /// <summary>Stores a verified semantic consumer and a native EXE bound by name to one producer DLL.</summary>
    public static byte[] CreateConsumer(BoundProgram program, byte[] producer, NativeRuntimePolicy runtimePolicy)
    {
        ArgumentNullException.ThrowIfNull(program);
        var provider = ReadAndValidateProducer(producer);
        var package = provider.Package;
        CheckPolicy(runtimePolicy);
        var contract = ModuleCompiler.CreateConsumer(program, package.Contract);
        var contractSnapshot = ModuleCompiler.ReadConsumerContractSnapshot(contract);
        var dll = DllName(package.Identity);
        var bindings = new List<NativeImportBinding>();
        var bindingNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            if (!function.IsExternal)
            {
                continue;
            }
            var binding = new NativeImportBinding(function.Name, dll, function.Name);
            bindings.Add(binding);
            bindingNames.Add(binding.FunctionName);
        }
        var imports = contractSnapshot.CallImports;
        foreach (var import in imports)
        {
            if (!bindingNames.Contains(import))
            {
                throw new ModuleContractException("Persisted consumer calls differ from native import bindings.");
            }
        }
        var (image, used) = NativeEmitter.EmitWithImportUsage(program, bindings, runtimePolicy);
        var machineImports = new string[used.Length];
        for (var i = 0; i < machineImports.Length; i++)
        {
            machineImports[i] = used[i].FunctionName;
        }
        var consumer = Write(isConsumer: true, package.Identity, runtimePolicy, contract, image,
            machineImports, contractSnapshot);
        ValidatePair(Read(consumer), provider);
        return consumer;
    }

    /// <summary>Checks package integrity, PE shape and semantic replacement contracts without emitting or launching code.</summary>
    public static void Validate(byte[] consumer, byte[] producer)
    {
        ValidatePair(Read(consumer), Read(producer));
    }

    /// <summary>Returns metadata after checking package integrity and the contained image structure.</summary>
    public static NativeModuleInfo Inspect(byte[] artifact)
    {
        var package = Read(artifact);
        if (package.IsConsumer)
        {
            ValidateConsumerImage(package);
        }
        else
        {
            ValidateProducer(package);
        }
        return new NativeModuleInfo(package.Identity, package.IsConsumer,
            Convert.ToHexString(SHA256.HashData(package.Image)), package.Image.Length, package.Contract.Length) {
            RuntimePolicy = package.Policy,
            NativeProfileVersion = package.Profile,
        };
    }

    /// <summary>Checks contracts, then starts only the already persisted images in a fresh private staging directory.</summary>
    public static Task<int> RunAsync(byte[] consumer, byte[] producer, TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(consumer, producer, timeout, null, cancellationToken);

    private static async Task<int> RunCoreAsync(byte[] consumer, byte[] producer, TimeSpan timeout,
        NativeRuntimePolicy? runtimePolicy, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            != System.Runtime.InteropServices.Architecture.X64)
        {
            throw new PlatformNotSupportedException("Native module execution requires Windows x64.");
        }
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Native execution requires a positive deadline of at most two minutes.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var executable = Read(consumer);
        var provider = Read(producer);
        if (runtimePolicy is NativeRuntimePolicy selected && executable.Policy != selected)
        {
            throw new ModuleContractException("Selected runtime policy differs from the native package.");
        }
        ValidatePair(executable, provider);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Directory.CreateTempSubdirectory("terrum-native-" + provider.Identity + "-").FullName;
        var consumerPath = Path.Combine(directory, "consumer.exe");
        var producerPath = Path.Combine(directory, DllName(provider.Identity));
        try
        {
            await WriteNewAsync(consumerPath, executable.Image, cancellationToken).ConfigureAwait(false);
            await WriteNewAsync(producerPath, provider.Image, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(consumerPath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start native consumer.");
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
            await DeleteImageAsync(consumerPath).ConfigureAwait(false);
            await DeleteImageAsync(producerPath).ConfigureAwait(false);
            Directory.Delete(directory);
        }
    }

    /// <summary>Runs only if the explicitly requested checked-failure policy matches the persisted packages.</summary>
    public static Task<int> RunAsync(byte[] consumer, byte[] producer, TimeSpan timeout,
        NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default)
    {
        CheckPolicy(runtimePolicy);
        return RunCoreAsync(consumer, producer, timeout, runtimePolicy, cancellationToken);
    }

    private static async Task DeleteImageAsync(string path)
    {
        // A signaled Windows process can still have an image section being torn down.
        // Retry only access/sharing failures for one second; persistent failures remain visible.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (Exception exception) when (attempt < 20
                && exception is IOException or UnauthorizedAccessException
                && (exception.HResult & 0xFFFF) is 5 or 32 or 33)
            {
                await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteNewAsync(string path, byte[] contents, CancellationToken cancellationToken)
    {
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous);
        await using (file.ConfigureAwait(false))
        {
            await file.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidatePair(Package consumer, Package producer)
    {
        var producerReference = ValidatePairContracts(consumer, producer);
        var exports = new string[producerReference.Functions.Length];
        for (var i = 0; i < exports.Length; i++)
        {
            exports[i] = producerReference.Functions[i].Name;
        }
        ValidateProducerImage(producer, exports);
        ValidateConsumerImage(consumer);
    }

    private static void ValidatePair(Package consumer, ValidatedProducer producer)
    {
        _ = ValidatePairContracts(consumer, producer.Package);
        ValidateConsumerImage(consumer);
    }

    private static CompilationReference ValidatePairContracts(Package consumer, Package producer)
    {
        if (!consumer.IsConsumer || producer.IsConsumer)
        {
            throw new ModuleFormatException("Native module package kinds do not form a consumer/producer pair.");
        }
        if (!string.Equals(consumer.Identity, producer.Identity, StringComparison.Ordinal))
        {
            throw new ModuleContractException("Native provider identity differs from the consumer binding.");
        }
        if (consumer.Policy != producer.Policy)
        {
            throw new ModuleContractException("Native runtime policies differ.");
        }
        var consumerSnapshot = consumer.ContractSnapshot
            ?? throw new InvalidOperationException("Native consumer contract snapshot is missing.");
        var providerSnapshot = producer.ProviderSnapshot
            ?? throw new InvalidOperationException("Native provider contract snapshot is missing.");
        if (ModuleCompiler.Link(consumerSnapshot, providerSnapshot).RuntimePolicy != (BoundRuntimePolicy)consumer.Policy)
        {
            throw new ModuleFormatException("Native package and linked semantic policies differ.");
        }
        return providerSnapshot.GetReference();
    }

    private static void ValidateProducerImage(Package producer, IReadOnlyList<string> exports)
    {
        NativeModuleImageValidator.Validate(producer.Image, isConsumer: false, DllName(producer.Identity), exports,
            producer.Policy, allowOwnedBufferRuntime: AllowsOwnedBufferRuntime(producer.Profile),
            allowManagedRuntimeData: AllowsManagedRuntimeData(producer.Profile));
    }

    private static void ValidateConsumerImage(Package consumer)
    {
        var contractSnapshot = consumer.ContractSnapshot
            ?? throw new InvalidOperationException("Native consumer contract snapshot is missing.");
        IReadOnlyList<string> imports = consumer.MachineImports;
        var groups = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (imports.Count != 0)
        {
            groups.Add(DllName(consumer.Identity), imports);
        }
        var profile = contractSnapshot.Profile;
        NativeModuleImageValidator.Validate(consumer.Image, isConsumer: true, groups, [], consumer.Policy,
            allowOwnedBufferRuntime: AllowsOwnedBufferRuntime(profile),
            allowManagedRuntimeData: AllowsManagedRuntimeData(profile));
    }

    private static CompilationReference ValidateProducer(Package producer)
    {
        if (producer.IsConsumer)
        {
            throw new ModuleFormatException("Expected a native producer package.");
        }
        var providerSnapshot = producer.ProviderSnapshot
            ?? throw new InvalidOperationException("Native provider contract snapshot is missing.");
        var reference = providerSnapshot.GetReference();
        if (reference.RuntimePolicy != (BoundRuntimePolicy)producer.Policy)
        {
            throw new ModuleFormatException("Native package and semantic contract policies differ.");
        }
        var exports = new string[reference.Functions.Length];
        for (var i = 0; i < exports.Length; i++)
        {
            exports[i] = reference.Functions[i].Name;
        }
        ValidateProducerImage(producer, exports);
        return reference;
    }

    private static ValidatedProducer ReadAndValidateProducer(byte[] producer)
    {
        var package = Read(producer);
        ValidateProducer(package);
        return new ValidatedProducer(package);
    }

    private static byte[] Write(bool isConsumer, string identity, NativeRuntimePolicy policy, byte[] contract,
        byte[] image, string[]? machineImports = null,
        ModuleCompiler.ConsumerContractSnapshot? contractSnapshot = null)
    {
        CheckIdentity(identity);
        CheckPolicy(policy);
        var nameLength = identity.Length;
        if (isConsumer != (machineImports is not null))
        {
            throw new ModuleFormatException("Native package kind and machine import table differ.");
        }
        var consumerSnapshot = isConsumer
            ? contractSnapshot ?? ModuleCompiler.ReadConsumerContractSnapshot(contract)
            : null;
        var profile = Encoding.ASCII.GetBytes(consumerSnapshot is not null ? consumerSnapshot.Profile
            : ModuleCompiler.GetNativeProfileVersion(contract, false));
        var imports = machineImports ?? [];
        if (machineImports is not null)
        {
            if (machineImports.Length > 1024)
            {
                throw new ModuleFormatException("Native consumer machine imports differ from retained declarations.");
            }
            var declarations = consumerSnapshot?.Declarations
                ?? throw new InvalidOperationException("Native consumer contract snapshot is missing.");
            foreach (var imported in machineImports)
            {
                if (!declarations.Contains(imported))
                {
                    throw new ModuleFormatException("Native consumer machine imports differ from retained declarations.");
                }
            }
            Array.Sort(imports, StringComparer.Ordinal);
        }
        using var importStream = new MemoryStream();
        using (var importWriter = new BinaryWriter(importStream, Encoding.UTF8, leaveOpen: true))
        {
            importWriter.Write(checked((ushort)imports.Length));
            foreach (var imported in imports)
            {
                PutGraphString(importWriter, imported);
            }
        }
        var importTable = importStream.ToArray();
        var length = checked(HeaderSize + nameLength + profile.Length + importTable.Length
            + contract.Length + image.Length + HashSize);
        if (contract.Length is < 1 or > MaximumContractBytes || image.Length == 0 || length > MaximumBytes)
        {
            throw new NotSupportedException("Native module package exceeds its bounded artifact limits.");
        }
        var result = new byte[length];
        "TNPK"u8.CopyTo(result);
        result[4] = Version;
        result[5] = isConsumer ? (byte)2 : (byte)1;
        result[6] = (byte)policy;
        result[7] = (byte)nameLength;
        result[8] = checked((byte)profile.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(10), contract.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(14), image.Length);
        _ = Encoding.ASCII.GetBytes(identity.AsSpan(), result.AsSpan(HeaderSize, nameLength));
        profile.CopyTo(result, HeaderSize + nameLength);
        importTable.CopyTo(result, HeaderSize + nameLength + profile.Length);
        contract.CopyTo(result, HeaderSize + nameLength + profile.Length + importTable.Length);
        image.CopyTo(result, HeaderSize + nameLength + profile.Length + importTable.Length + contract.Length);
        SHA256.HashData(result.AsSpan(0, length - HashSize)).CopyTo(result, length - HashSize);
        return result;
    }

    private static void WriteAsciiIdentity(BinaryWriter writer, string identity)
    {
        Span<byte> bytes = stackalloc byte[64];
        var length = Encoding.ASCII.GetBytes(identity.AsSpan(), bytes);
        writer.Write(bytes[..length]);
    }

    private static Package Read(byte[] artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Length is < HeaderSize + HashSize or > MaximumBytes)
        {
            throw new ModuleFormatException("Native module package exceeds its bounded artifact limits.");
        }
        var bytes = (byte[])artifact.Clone();
        if (!bytes.AsSpan(0, 4).SequenceEqual("TNPK"u8)
            || bytes[4] != Version
            || bytes[5] is not (1 or 2)
            || bytes[6] is not ((byte)NativeRuntimePolicy.FailFast or (byte)NativeRuntimePolicy.CheckedExceptions)
            || bytes[9] != 0)
        {
            throw new ModuleFormatException("Unknown or malformed native module package header.");
        }
        var nameLength = bytes[7];
        var profileLength = bytes[8];
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10, 4));
        var imageLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(14, 4));
        var required = (long)HeaderSize + nameLength + profileLength + contractLength + imageLength + HashSize;
        if (nameLength is < 1 or > 64 || profileLength is < 1 or > 32
            || contractLength is < 1 or > MaximumContractBytes || imageLength < 1
            || required + 2 > bytes.Length
            || !HasValidChecksum(bytes))
        {
            throw new ModuleFormatException("Invalid native module package bounds or checksum.");
        }
        var name = Encoding.ASCII.GetString(bytes, HeaderSize, nameLength);
        var profile = Encoding.ASCII.GetString(bytes, HeaderSize + nameLength, profileLength);
        if (!ValidIdentity(name))
        {
            throw new ModuleFormatException("Invalid provider identity.");
        }
        var at = HeaderSize + nameLength + profileLength;
        var importCount = ReadGraphCount(bytes, ref at, 1024);
        var machineImports = new string[importCount];
        for (var index = 0; index < importCount; index++)
        {
            var imported = ReadGraphString(bytes, ref at, 256);
            if (!ValidGraphSymbol(imported)
                || (index != 0 && StringComparer.Ordinal.Compare(machineImports[index - 1], imported) >= 0))
            {
                throw new ModuleFormatException("Duplicate, unordered or invalid native machine import.");
            }
            machineImports[index] = imported;
        }
        if ((bytes[5] == 1 && machineImports.Length != 0)
            || (long)at + contractLength + imageLength + HashSize != bytes.Length)
        {
            throw new ModuleFormatException("Invalid native machine import table bounds.");
        }
        var contract = bytes.AsSpan(at, contractLength).ToArray();
        ModuleCompiler.ConsumerContractSnapshot? contractSnapshot = null;
        ModuleCompiler.ProviderContractSnapshot? providerSnapshot = null;
        string contractProfile;
        BoundRuntimePolicy contractPolicy;
        if (bytes[5] == 2)
        {
            contractSnapshot = ModuleCompiler.ReadConsumerContractSnapshot(contract);
            contractProfile = contractSnapshot.Profile;
            contractPolicy = contractSnapshot.RuntimePolicy;
        }
        else
        {
            providerSnapshot = ModuleCompiler.ReadProviderContractSnapshot(contract);
            contractProfile = providerSnapshot.Profile;
            contractPolicy = providerSnapshot.RuntimePolicy;
        }
        if (profile != contractProfile || bytes[6] != (byte)contractPolicy)
        {
            throw new ModuleFormatException("Native package and semantic contract profiles or runtime policies differ.");
        }
        if (bytes[5] == 2)
        {
            if (contractSnapshot is null)
            {
                throw new ModuleFormatException("Native machine imports differ from retained semantic declarations or calls.");
            }
            var machineImportSet = machineImports.ToHashSet(StringComparer.Ordinal);
            if (!machineImportSet.IsSubsetOf(contractSnapshot.Declarations))
            {
                throw new ModuleFormatException("Native machine imports differ from retained semantic declarations or calls.");
            }
            foreach (var import in contractSnapshot.CallImports)
            {
                if (!machineImportSet.Contains(import))
                {
                    throw new ModuleFormatException("Native machine imports differ from retained semantic declarations or calls.");
                }
            }
        }
        var image = bytes.AsSpan(at + contractLength, imageLength).ToArray();
        return new Package(bytes[5] == 2, name, (NativeRuntimePolicy)bytes[6], contract, image, profile) {
            MachineImports = machineImports,
            ContractSnapshot = contractSnapshot,
            ProviderSnapshot = providerSnapshot,
        };
    }

    private static string DllName(string identity) => "terrum-" + identity + ".dll";

    private static void CheckPolicy(NativeRuntimePolicy policy)
    {
        if (policy is not (NativeRuntimePolicy.FailFast or NativeRuntimePolicy.CheckedExceptions))
        {
            throw new NotSupportedException("Unsupported native module runtime policy.");
        }
    }

    private static void CheckIdentity(string identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!ValidIdentity(identity))
        {
            throw new ArgumentException("Provider identity must start with a letter and contain only ASCII letters, digits or underscores (1 to 64 characters).", nameof(identity));
        }
    }

    private static bool ValidIdentity(string identity) => identity.Length is >= 1 and <= 64
        && (identity[0] is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'))
        && identity.All(static character => character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z')
            or (>= '0' and <= '9') or '_');

    private sealed record Package(bool IsConsumer, string Identity, NativeRuntimePolicy Policy, byte[] Contract, byte[] Image, string Profile)
    {
        public string[] MachineImports { get; init; } = [];
        public ModuleCompiler.ConsumerContractSnapshot? ContractSnapshot { get; init; }
        public ModuleCompiler.ProviderContractSnapshot? ProviderSnapshot { get; init; }
    }

    private sealed record ValidatedProducer(Package Package);
}
