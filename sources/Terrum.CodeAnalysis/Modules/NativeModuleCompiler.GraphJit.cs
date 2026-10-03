// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;

namespace Terrum.CodeAnalysis.Modules;

public static partial class NativeModuleCompiler
{
    /// <summary>Supervises a fresh managed JIT worker against validated, unchanged graph packages and DLLs.</summary>
    /// <remarks>
    /// The caller supplies a trusted terrumc apphost as <paramref name="workerApphost"/>. This API does
    /// not authenticate that executable or sandbox native code. It snapshots and validates the complete graph
    /// before process creation, and the worker validates the staged snapshots before loading any DLL.
    /// Cancellation or timeout terminates the worker before its private staging directory is removed.
    /// </remarks>
    public static async Task<int> RunGraphJitAsync(byte[] consumer, IReadOnlyList<byte[]> producers,
        string workerApphost, TimeSpan timeout, NativeRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(producers);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerApphost);
        if (producers.Count is < 1 or > 16)
        {
            throw new ModuleContractException("Native graphs require 1 to 16 selected producers.");
        }
        if (consumer.Length is < GraphHeaderSize + HashSize or > GraphMaximumBytes)
        {
            throw new ModuleFormatException("Native graph JIT root has invalid artifact bounds.");
        }
        var inputs = new byte[producers.Count][];
        for (var index = 0; index < inputs.Length; index++)
        {
            var bytes = producers[index]
                ?? throw new ArgumentException("Selected graph JIT providers cannot be null.", nameof(producers));
            if (bytes.Length is < 1 or > GraphMaximumBytes)
            {
                throw new ModuleFormatException("Native graph JIT provider has invalid artifact bounds.");
            }
            inputs[index] = bytes;
        }
        CheckPolicy(runtimePolicy);
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("Native graph JIT requires Windows x64.");
        }
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        var worker = Path.GetFullPath(workerApphost);
        if (!Path.GetFileName(worker).Equals("terrumc.exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(worker))
        {
            throw new ArgumentException("Native graph JIT requires a trusted terrumc apphost.", nameof(workerApphost));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var rootBytes = (byte[])consumer.Clone();
        var selected = inputs.Select(static bytes => (byte[])bytes.Clone()).ToArray();
        var root = ReadGraphPackage(rootBytes);
        var providers = ReadGraphProviders(selected);
        if (root.Policy != runtimePolicy)
        {
            throw new ModuleContractException("Selected graph JIT runtime policy differs from persisted consumer.");
        }
        _ = CheckGraph(root, providers);
        var indexed = selected.Select(bytes => (Bytes: bytes, Package: ReadGraphPackage(bytes)))
            .ToDictionary(static item => item.Package.Identity!, StringComparer.Ordinal);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Directory.CreateTempSubdirectory("terrum-graph-jit-").FullName;
        var paths = new List<string>();
        try
        {
            var rootPath = Path.Combine(directory, "consumer.trng");
            paths.Add(rootPath);
            await WriteNewAsync(rootPath, rootBytes, cancellationToken).ConfigureAwait(false);
            var packagePaths = new List<string>(providers.Length);
            foreach (var provider in providers)
            {
                var artifactPath = Path.Combine(directory, provider.Identity
                    + (provider.IsLeaf ? ".trmn" : ".trng"));
                packagePaths.Add(artifactPath);
                paths.Add(artifactPath);
                await WriteNewAsync(artifactPath, indexed[provider.Identity!].Bytes, cancellationToken)
                    .ConfigureAwait(false);
                var imagePath = Path.Combine(directory, DllName(provider.Identity!));
                paths.Add(imagePath);
                await WriteNewAsync(imagePath, provider.Image, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            using var requests = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            using var responses = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            // The worker uses absolute staged paths; keeping its current directory outside staging
            // lets the supervisor remove the staging directory as soon as the worker exits.
            var start = new ProcessStartInfo(worker) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(worker)
                    ?? throw new InvalidOperationException("Native graph JIT worker has no application directory."),
                RedirectStandardInput = true,
            };
            start.ArgumentList.Add("--native-graph-jit-worker");
            start.ArgumentList.Add(rootPath);
            foreach (var path in packagePaths)
            {
                start.ArgumentList.Add("--provider");
                start.ArgumentList.Add(path);
            }
            start.ArgumentList.Add("--runtime");
            start.ArgumentList.Add(runtimePolicy == NativeRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions");
            start.ArgumentList.Add("--registrar-input");
            start.ArgumentList.Add(responses.GetClientHandleAsString());
            start.ArgumentList.Add("--registrar-output");
            start.ArgumentList.Add(requests.GetClientHandleAsString());
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start native graph JIT worker.");
            requests.DisposeLocalCopyOfClientHandle();
            responses.DisposeLocalCopyOfClientHandle();
            process.StandardInput.Close();
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(timeout);
                try
                {
                    await ServiceGraphRegistrationAsync(process, requests, responses, deadline.Token)
                        .ConfigureAwait(false);
                    await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Native graph JIT worker exceeded its deadline.");
                }
            }
            catch
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

    /// <summary>Runs a staged, verified graph through root-only JIT code and exact selected DLL images.</summary>
    /// <remarks>
    /// This executes native code in the calling process and offers no timeout or isolation. Only a trusted,
    /// separately supervised worker should call it. It checks the complete package closure, staged DLL bytes
    /// and emitted imports before loading any DLL, then unloads DLLs after JIT unwind and code cleanup.
    /// Call <see cref="RunGraphJitAsync"/> to supervise execution of trusted native artifacts.
    /// </remarks>
    public static int ExecuteGraphJitWorker(string rootPath, IReadOnlyList<string> artifactPaths,
        NativeRuntimePolicy runtimePolicy, string? registrarAssembly = null,
        string? registrarInputPipe = null, string? registrarOutputPipe = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(artifactPaths);
        CheckPolicy(runtimePolicy);
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("Native graph JIT requires Windows x64.");
        }
        if (artifactPaths.Count is < 1 or > 16)
        {
            throw new ModuleContractException("Native graph JIT needs 1 to 16 selected producers.");
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(rootPath))
            ?? throw new ModuleFormatException("Native graph JIT root has no staging directory.");
        if (Path.GetFileName(rootPath) != "consumer.trng"
            || artifactPaths.Any(path => !string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),
                directory, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ModuleFormatException("Native graph JIT package paths differ from their staging directory.");
        }
        var root = ReadGraphPackage(ReadGraphSnapshot(rootPath));
        var providers = ReadGraphProviders([.. artifactPaths.Select(ReadGraphSnapshot)]);
        if (root.Policy != runtimePolicy)
        {
            throw new ModuleContractException("Native graph JIT runtime policy differs from persisted consumer.");
        }
        var order = CheckGraph(root, providers);
        var direct = ResolveDirect(root, providers.ToDictionary(static item => item.Identity!,
            StringComparer.OrdinalIgnoreCase));
        var program = ModuleCompiler.RestoreConsumerAgainstReference(root.Contract, GraphReference(direct));
        var declarations = NativeBindings(root.Dependencies);
        var code = declarations.Length == 0
            ? NativeEmitter.EmitCode(program, runtimePolicy)
            : NativeEmitter.EmitCodeWithImports(program, declarations, runtimePolicy);
        var used = root.Dependencies.SelectMany(item => item.Imports.Select(name =>
            new NativeImportBinding(name, DllName(item.Identity), name))).ToArray();
        var images = new List<FileStream>(order.Length);
        try
        {
            var modules = new List<NativeJitModule>(order.Length);
            foreach (var provider in order)
            {
                var imagePath = Path.Combine(directory, DllName(provider.Identity!));
                var file = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                images.Add(file);
                if (file.Length != provider.Image.Length)
                {
                    throw new ModuleFormatException("Staged graph JIT image length differs from its package.");
                }
                var bytes = new byte[provider.Image.Length];
                file.ReadExactly(bytes);
                if (!bytes.AsSpan().SequenceEqual(provider.Image))
                {
                    throw new ModuleFormatException("Staged graph JIT image differs from its validated package.");
                }
                modules.Add(new NativeJitModule(DllName(provider.Identity!), imagePath, provider.Exports));
            }
            return NativeJit.ExecuteImportedCode(code, modules, used, registrarAssembly,
                registrarInputPipe, registrarOutputPipe);
        }
        finally
        {
            foreach (var image in images)
            {
                image.Dispose();
            }
        }
    }

    private static async Task ServiceGraphRegistrationAsync(Process process, Stream requests, Stream responses,
        CancellationToken cancellationToken)
    {
        var header = new byte[15];
        if (await requests.ReadAsync(header.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) == 0)
        {
            return;
        }
        if (header[0] != 1)
        {
            throw new ModuleFormatException("Native graph JIT worker sent an invalid registration request.");
        }
        await requests.ReadExactlyAsync(header.AsMemory(1, 14), cancellationToken).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(1, 2));
        var baseAddress = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(3, 8));
        var codeLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(11, 4));
        if (count == 0 || baseAddress <= 0 || codeLength is < 1 or > 16 * 1024 * 1024)
        {
            throw new ModuleFormatException("Native graph JIT worker sent invalid continuation bounds.");
        }
        var data = new byte[count * sizeof(int)];
        await requests.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
        var targets = new JitContinuationTarget[count];
        var unique = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            var offset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(index * sizeof(int), sizeof(int)));
            if (offset <= 0 || offset >= codeLength || !unique.Add(offset))
            {
                throw new ModuleFormatException("Native graph JIT worker sent an invalid continuation offset.");
            }
            targets[index] = new JitContinuationTarget(checked((nint)(baseAddress + offset)), 1);
        }
        var handle = JitWindows.OpenProcess(0x1600, false, checked((uint)process.Id));
        if (handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        try
        {
            JitWindows.ValidateExecutableContinuationRegion(handle, checked((nint)baseAddress), codeLength);
            var added = JitWindows.SetProcessDynamicEHContinuationTargets(handle, count, targets);
            if (!added || targets.Any(static target => (target._flags & 2) == 0))
            {
                var error = Marshal.GetLastPInvokeError();
                var processed = targets.Where(static target => (target._flags & 2) != 0).ToArray();
                if (processed.Length != 0 && !NativeJitContinuationRegistrar.Remove(handle, ref processed))
                {
                    throw new InvalidOperationException("Native graph JIT continuation rollback failed; worker must be terminated.");
                }
                await WriteGraphRegistrationResponseAsync(responses, 0, cancellationToken).ConfigureAwait(false);
                if (!added)
                {
                    throw new Win32Exception(error);
                }
                throw new InvalidOperationException("Native graph JIT continuation registration did not process every target.");
            }
            await WriteGraphRegistrationResponseAsync(responses, 1, cancellationToken).ConfigureAwait(false);
            await responses.FlushAsync(cancellationToken).ConfigureAwait(false);
            var removalRequest = await requests.ReadAsync(header.AsMemory(0, 1), cancellationToken)
                .ConfigureAwait(false);
            if (removalRequest == 0)
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                    .ConfigureAwait(false);
                var status = unchecked((uint)process.ExitCode);
                if (status is >= 0xE0540001 and <= 0xE0540005)
                {
                    return;
                }
                throw new IOException("Native graph JIT worker exited without removing its continuations.");
            }
            if (header[0] != 2)
            {
                throw new IOException("Native graph JIT worker sent an invalid continuation removal request.");
            }
            if (!NativeJitContinuationRegistrar.Remove(handle, ref targets))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            await WriteGraphRegistrationResponseAsync(responses, 1, cancellationToken).ConfigureAwait(false);
            await responses.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (await requests.ReadAsync(header.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0)
            {
                throw new IOException("Native graph JIT worker sent more than one continuation lifecycle.");
            }
        }
        finally
        {
            _ = JitWindows.CloseHandle(handle);
        }
    }

    private static ValueTask WriteGraphRegistrationResponseAsync(Stream responses, byte status,
        CancellationToken cancellationToken) => responses.WriteAsync(new byte[] { status }, cancellationToken);

    private static byte[] ReadGraphSnapshot(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is < 1 or > GraphMaximumBytes)
        {
            throw new ModuleFormatException("Native graph JIT package exceeds its artifact bound.");
        }
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        return bytes;
    }
}
