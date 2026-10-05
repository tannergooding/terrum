// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Native;

/// <summary>The native entry result and an owned snapshot of its compiler-supplied data buffers.</summary>
/// <param name="ExitCode">The callable entry's int32 result.</param>
/// <param name="Data">A copy of the initialized host-buffer data after successful execution.</param>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The caller owns an independent snapshot after native memory is freed.")]
public sealed record NativeExecutionResult(int ExitCode, byte[] Data)
{
    /// <summary>Byte offsets of host buffers in Data, indexed by their bound-program order.</summary>
    public ImmutableArray<int> BufferOffsets { get; init; } = [];
}

internal sealed record NativeJitModule(string DllName, string FullPath, IReadOnlyList<string> Exports);

/// <summary>Executes verified native code in the current Windows x64 process.</summary>
/// <remarks>
/// Unhandled checked failures terminate the process; recovery occurs only within generated Terrum regions.
/// Run untrusted programs only in a separately supervised process; this method provides no timeout or isolation.
/// </remarks>
public static unsafe class NativeJit
{
    private const int MaximumInvocationSourceCodeUnits = 16_384;
    private static readonly List<INativeJitContinuationRegistration> s_retainedRegistrations = [];

    /// <summary>Emits and executes a host-neutral callable entry point, returning its int32 result.</summary>
    /// <exception cref="ArgumentException">The program requires an invocation-scoped source grant.</exception>
    /// <exception cref="PlatformNotSupportedException">The host is not Windows x64.</exception>
    public static int Execute(BoundProgram program, NativeRuntimePolicy runtimePolicy,
        string? registrarAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.RequiresSourceGrant)
        {
            throw new ArgumentException("A source-dependent program requires ExecuteWithSourceGrant.", nameof(program));
        }
        var code = EmitCode(program, runtimePolicy);
        return ExecuteCode(code, [], [], registrarAssembly, null, null, false, out _);
    }

    /// <summary>Executes a source-dependent program with one fresh, immutable UTF-16 source grant.</summary>
    /// <param name="program">The verified program requiring <c>Host.Source</c>.</param>
    /// <param name="sourceGrants">Exactly one readonly invocation-scoped <c>Host.Source</c> buffer containing at most 16,384 code units.</param>
    /// <param name="runtimePolicy">The generated native runtime policy.</param>
    /// <param name="registrarAssembly">Trusted CLI assembly serving remote CET continuation registration
    /// when the current host enforces CET and generated code uses checked exception continuations.</param>
    /// <exception cref="ArgumentException">The program or source grant does not match the supported source profile.</exception>
    /// <exception cref="PlatformNotSupportedException">The host is not Windows x64.</exception>
    public static int ExecuteWithSourceGrant(BoundProgram program, ImmutableArray<HostBuffer> sourceGrants,
        NativeRuntimePolicy runtimePolicy, string? registrarAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (!program.RequiresSourceGrant || program.InvocationProfileId is not null || program.Buffers.Length != 1
            || !IsSourceGrant(program.Buffers[0]))
        {
            throw new ArgumentException("The program does not have the supported Host.Source dependency.", nameof(program));
        }
        if (sourceGrants.IsDefaultOrEmpty || sourceGrants.Length != 1
            || sourceGrants[0] is not HostBuffer source || !IsSourceGrant(source)
            || source.Values.IsDefault || source.Values.Length > MaximumInvocationSourceCodeUnits)
        {
            throw new ArgumentException("Exactly one readonly Host.Source grant of at most 16,384 code units is required.",
                nameof(sourceGrants));
        }
        foreach (var codeUnit in source.Values)
        {
            if (codeUnit is < 0 or > ushort.MaxValue)
            {
                throw new ArgumentException("Host.Source contains a value outside the UTF-16 code-unit range.",
                    nameof(sourceGrants));
            }
        }

        var invocationProgram = program with {
            Buffers = [source with { Values = ImmutableArray.CreateRange(source.Values.ToArray()) }],
            IsInvocationSourceBound = true,
        };
        var code = EmitCode(invocationProgram, runtimePolicy);
        return ExecuteCode(code, [], [], registrarAssembly, null, null, false, out _);
    }

    /// <summary>Executes a persisted scanner-profile program against fresh source and output buffers.</summary>
    /// <param name="program">The linked program retaining the scanner invocation profile.</param>
    /// <param name="source">The source snapshot to scan.</param>
    /// <param name="runtimePolicy">The generated native runtime policy.</param>
    /// <param name="registrarAssembly">Trusted CLI assembly serving remote CET continuation registration
    /// when the current host enforces CET and generated code uses checked exception continuations.</param>
    /// <returns>Owned lexical and integer results validated against the scanner interchange.</returns>
    public static ScannerInvocationResult ExecuteWithScannerProfile(BoundProgram program, SourceText source,
        NativeRuntimePolicy runtimePolicy, string? registrarAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(source);
        if (!program.RequiresSourceGrant || program.InvocationProfileId != ScannerHost.InvocationProfileIdentity
            || !ScannerHost.IsInvocationProfileBufferSet(program.Buffers, program.InvocationProfileId))
        {
            throw new ArgumentException("The program does not retain the supported scanner invocation profile.",
                nameof(program));
        }

        var buffers = ScannerHost.CreateInvocationProfileBuffers(source);
        var invocationProgram = program with {
            Buffers = buffers,
            IsInvocationSourceBound = true,
        };
        var code = EmitCode(invocationProgram, runtimePolicy);
        var tokenCount = ExecuteCode(code, [], [], registrarAssembly, null, null, true, out var data);
        var execution = new NativeExecutionResult(tokenCount, data) { BufferOffsets = code.BufferOffsets };
        return ScannerHost.ReadInvocationResult(source, tokenCount, execution, buffers);
    }

    /// <summary>Executes the entry point and copies compiler-supplied buffers before releasing native memory.</summary>
    /// <exception cref="PlatformNotSupportedException">The host is not Windows x64.</exception>
    /// <param name="program">The verified program to execute.</param>
    /// <param name="runtimePolicy">The generated native runtime policy.</param>
    /// <param name="registrarAssembly">Trusted CLI assembly serving remote CET continuation registration
    /// when the current host enforces CET and generated code uses checked exception continuations.</param>
    public static NativeExecutionResult ExecuteWithData(BoundProgram program, NativeRuntimePolicy runtimePolicy,
        string? registrarAssembly = null)
    {
        var code = EmitCode(program, runtimePolicy);
        var exitCode = ExecuteCode(code, [], [], registrarAssembly, null, null, true, out var data);
        return new NativeExecutionResult(exitCode, data) { BufferOffsets = code.BufferOffsets };
    }

    private static NativeCode EmitCode(BoundProgram program, NativeRuntimePolicy runtimePolicy)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("Native JIT requires a Windows x64 process.");
        }

        var code = NativeEmitter.EmitCode(program, runtimePolicy);
        ValidateBufferMetadata(program, code);
        return code;
    }

    private static bool IsSourceGrant(HostBuffer buffer) =>
        buffer is not null && buffer.Name == "Source" && buffer.ElementType == PrimitiveType.Char16
            && !buffer.IsWritable && buffer.IsInvocationScoped && buffer.InvocationProfileId is null;

    internal static int ExecuteImportedCode(NativeCode code, IReadOnlyList<NativeJitModule> modules,
        IReadOnlyList<NativeImportBinding> expectedImports, string? registrarAssembly = null,
        string? registrarInputPipe = null, string? registrarOutputPipe = null)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("Native JIT requires a Windows x64 process.");
        }
        if (!code.BufferOffsets.IsEmpty || code.HostDataLength != 0 || code.ReadOnlyDataLength != 0
            || (code.ManagedRuntimeOffset < 0
                ? code.Data.Length != 0
                : code.ManagedRuntimeOffset != 0 || code.Data.Length != 24
                    || code.Data.AsSpan().IndexOfAnyExcept((byte)0) >= 0))
        {
            throw new ArgumentException("Native graph consumers cannot contain host buffers.", nameof(code));
        }
        return ExecuteCode(code, modules, expectedImports, registrarAssembly,
            registrarInputPipe, registrarOutputPipe, false, out _);
    }

    private static void ValidateBufferMetadata(BoundProgram program, NativeCode code)
    {
        var buffers = program.Buffers;
        long readOnlyLength = 0;
        long totalLength = 0;
        foreach (var buffer in buffers)
        {
            var length = (long)buffer.Values.Length * sizeof(int);
            totalLength += length;
            if (!buffer.IsWritable)
            {
                readOnlyLength += length;
            }
        }

        var runtimeOffset = code.ManagedRuntimeOffset < 0 ? (int)totalLength
            : checked(((int)totalLength + 7) & ~7);
        if (code.BufferOffsets.Length != buffers.Length || totalLength != code.HostDataLength
            || readOnlyLength != code.ReadOnlyDataLength
            || code.Data.Length != runtimeOffset + (code.ManagedRuntimeOffset < 0 ? 0 : 24)
            || (code.ManagedRuntimeOffset >= 0 && code.ManagedRuntimeOffset != runtimeOffset))
        {
            throw new ArgumentException("Native buffer metadata does not match the bound program.", nameof(code));
        }

        var readOnlyOffset = 0;
        var writableOffset = code.ReadOnlyDataLength;
        for (var index = 0; index < buffers.Length; index++)
        {
            var buffer = buffers[index];
            var expected = buffer.IsWritable ? writableOffset : readOnlyOffset;
            if (code.BufferOffsets[index] != expected)
            {
                throw new ArgumentException("Native buffer order does not match the bound program.", nameof(code));
            }

            var bufferData = buffer.Values.Length == 0 ? Span<byte>.Empty : code.Data.AsSpan(expected);
            foreach (var value in buffer.Values)
            {
                if (BinaryPrimitives.ReadInt32LittleEndian(bufferData[..sizeof(int)]) != value)
                {
                    throw new ArgumentException("Native buffer contents do not match the bound program.", nameof(code));
                }
                bufferData = bufferData[sizeof(int)..];
                expected += sizeof(int);
            }

            if (buffer.IsWritable)
            {
                writableOffset = expected;
            }
            else
            {
                readOnlyOffset = expected;
            }
        }
    }

    private static int ExecuteCode(NativeCode code, IReadOnlyList<NativeJitModule> modules,
        IReadOnlyList<NativeImportBinding> expectedImports, string? registrarAssembly,
        string? registrarInputPipe, string? registrarOutputPipe, bool captureHostData, out byte[] dataSnapshot)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(expectedImports);
        var text = code.Code;
        var data = code.Data;
        if (text.Length == 0 || code.EntryPoint < 0 || code.EntryPoint >= text.Length || code.Functions.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Invalid native code layout.", nameof(code));
        }

        if (code.ReadOnlyDataLength < 0 || code.ReadOnlyDataLength > data.Length
            || (code.ReadOnlyDataLength & 3) != 0 || (data.Length & 3) != 0)
        {
            throw new ArgumentException("Invalid native data partition.", nameof(code));
        }

        foreach (var offset in code.BufferOffsets)
        {
            if (offset < 0 || offset > data.Length || (offset & 3) != 0)
            {
                throw new ArgumentException("Invalid native buffer offset.", nameof(code));
            }
        }

        var functions = code.Functions.AsSpan();
        NativeFunctionRange[]? sortedFunctions = null;
        for (var index = 1; index < functions.Length; index++)
        {
            if (functions[index - 1].Start >= functions[index].Start)
            {
                sortedFunctions = code.Functions.ToArray();
                Array.Sort(sortedFunctions, static (left, right) => left.Start.CompareTo(right.Start));
                functions = sortedFunctions;
                break;
            }
        }
        var previousEnd = 0;
        var containsEntry = false;
        foreach (var function in functions)
        {
            var prologLength = function.UsesFramePointer ? 11 : 7;
            if (function.Start < previousEnd || function.Start < 0 || function.End > text.Length
                || function.End - function.Start < prologLength || function.StackSize is < 8 or > 0xF00
                || (function.StackSize & 15) != 8
                || (!function.ExceptionScopes.IsEmpty && (function.ExceptionHandler < 0
                    || function.ExceptionHandler > text.Length - 6
                    || function.ExceptionScopes.Length > 100_000))
                || (function.UsesParentFrame && !function.UsesFramePointer)
                || (function.UsesFramePointer
                    ? text[function.Start] != 0x55
                        || text[function.Start + 1] != 0x48 || text[function.Start + 2] != 0x81
                        || text[function.Start + 3] != 0xEC
                        || BinaryPrimitives.ReadInt32LittleEndian(text.AsSpan(function.Start + 4, 4)) != function.StackSize - 8
                        || text[function.Start + 8] != 0x48 || text[function.Start + 9] != 0x89
                        || text[function.Start + 10] != (function.UsesParentFrame ? 0xD5 : 0xE5)
                    : text[function.Start] != 0x48 || text[function.Start + 1] != 0x81 || text[function.Start + 2] != 0xEC
                        || BinaryPrimitives.ReadInt32LittleEndian(text.AsSpan(function.Start + 3, 4)) != function.StackSize))
            {
                throw new ArgumentException("Invalid native function range or prolog.", nameof(code));
            }
            foreach (var scope in function.ExceptionScopes)
            {
                if (scope.Begin < function.Start || scope.End > function.End || scope.Begin >= scope.End
                    || scope.Handler < 0 || scope.Handler >= text.Length
                    || (scope.Target != 0 && (scope.Target < function.Start || scope.Target >= function.End)))
                {
                    throw new ArgumentException("Invalid native exception scope.", nameof(code));
                }
            }

            containsEntry |= code.EntryPoint >= function.Start && code.EntryPoint < function.End;
            previousEnd = function.End;
        }

        if (!containsEntry)
        {
            throw new ArgumentException("Native entry point must have unwind metadata.", nameof(code));
        }

        var pointerOffset = Align(text.Length, 8);
        var tableOffset = checked(pointerOffset + (sizeof(nint) * (1 + code.NativeImports.Length)));
        var unwindOffset = checked(tableOffset + (functions.Length * NativeUnwind.RuntimeFunctionSize));
        var pageSize = Environment.SystemPageSize;
        var infoSize = 0;
        foreach (var function in functions)
        {
            infoSize = checked(infoSize + NativeUnwind.InfoSize(function));
        }
        var codeSize = Align(checked(unwindOffset + infoSize), pageSize);
        var readOnlyOffset = codeSize;
        var writableOffset = Align(checked(readOnlyOffset + code.ReadOnlyDataLength), pageSize);
        var writableLength = data.Length - code.ReadOnlyDataLength;
        var size = Align(checked(writableOffset + writableLength), pageSize);

        var displacementBytes = new HashSet<int>();
        foreach (var fixup in code.ExitProcessFixups)
        {
            if (fixup < 0 || fixup > text.Length - sizeof(int))
            {
                throw new ArgumentException("Invalid ExitProcess displacement.", nameof(code));
            }
            for (var offset = fixup; offset < fixup + sizeof(int); offset++)
            {
                if (!displacementBytes.Add(offset))
                {
                    throw new ArgumentException("Overlapping native displacement fields.", nameof(code));
                }
            }
        }

        foreach (var fixup in code.DataFixups)
        {
            if (fixup.DisplacementOffset < 0 || fixup.DisplacementOffset > text.Length - sizeof(int)
                || fixup.DataOffset < 0 || fixup.DataOffset > data.Length)
            {
                throw new ArgumentException("Invalid native data displacement.", nameof(code));
            }
            for (var offset = fixup.DisplacementOffset; offset < fixup.DisplacementOffset + sizeof(int); offset++)
            {
                if (!displacementBytes.Add(offset))
                {
                    throw new ArgumentException("Overlapping native displacement fields.", nameof(code));
                }
            }
        }
        var moduleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var providersByName = new Dictionary<string, NativeJitModule>(modules.Count, StringComparer.Ordinal);
        foreach (var module in modules)
        {
            if (string.IsNullOrEmpty(module.DllName) || !moduleNames.Add(module.DllName)
                || !Path.IsPathFullyQualified(module.FullPath))
            {
                throw new ArgumentException("Native JIT providers require unique DLL names and full paths.",
                    nameof(modules));
            }
            providersByName.Add(module.DllName, module);
        }
        var allowed = new HashSet<(string Dll, string Export)>();
        foreach (var binding in expectedImports)
        {
            if (!providersByName.TryGetValue(binding.DllName, out var module)
                || !ContainsSortedExport(module.Exports, binding.ExportName)
                || !allowed.Add((binding.DllName, binding.ExportName)))
            {
                throw new ArgumentException("Native JIT import has no unique validated provider.", nameof(expectedImports));
            }
        }
        var emitted = new HashSet<(string Dll, string Export)>();
        var hasRaiseException = false;
        foreach (var fixup in code.NativeImports)
        {
            if (fixup.DisplacementOffset < 0 || fixup.DisplacementOffset > text.Length - sizeof(int)
                || (fixup.DeclarationIndex >= 0
                    ? !allowed.Contains((fixup.DllName, fixup.ExportName))
                    : !((fixup.DllName.Equals("KERNEL32.dll", StringComparison.OrdinalIgnoreCase)
                            && fixup.ExportName is "RaiseException" or "ExitProcess" or "GetProcessHeap"
                                or "HeapAlloc" or "HeapFree")
                        || (fixup.DllName.Equals("NTDLL.dll", StringComparison.OrdinalIgnoreCase)
                            && fixup.ExportName == "__C_specific_handler"))))
            {
                throw new ArgumentException("Invalid or unowned JIT import.", nameof(code));
            }
            if (fixup.ExportName == "RaiseException")
            {
                hasRaiseException = true;
            }
            if (fixup.DeclarationIndex >= 0)
            {
                _ = emitted.Add((fixup.DllName, fixup.ExportName));
            }
            for (var offset = fixup.DisplacementOffset; offset < fixup.DisplacementOffset + sizeof(int); offset++)
            {
                if (!displacementBytes.Add(offset))
                {
                    throw new ArgumentException("Overlapping native displacement fields.", nameof(code));
                }
            }
        }
        if (!emitted.SetEquals(allowed))
        {
            throw new ArgumentException("Native JIT fixups differ from persisted machine imports.", nameof(code));
        }
        var cetEnforced = false;
        if (hasRaiseException
            && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            if (!JitWindows.GetProcessMitigationPolicy(JitWindows.GetCurrentProcess(), 15,
                out var shadowStackPolicy, sizeof(uint)))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            cetEnforced = (shadowStackPolicy & 0x05) != 0;
        }
        var continuations = cetEnforced
            ? (sortedFunctions ?? functions.ToArray()).SelectMany(static function => function.ExceptionScopes)
                .Where(static scope => scope.Target != 0).Select(static scope => scope.Target)
                .Distinct().ToArray()
            : [];
        if (continuations.Length != 0
            && (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)
                || !JitWindows.HasDynamicEHContinuationTargets()
                || (registrarAssembly is null && (registrarInputPipe is null || registrarOutputPipe is null))
                || continuations.Length > ushort.MaxValue))
        {
            throw new NotSupportedException("Checked native JIT under CET requires the Windows build 20348 dynamic continuation API and a trusted remote registrar.");
        }

        var exitProcessModule = NativeLibrary.Load("kernel32.dll", typeof(NativeJit).Assembly, DllImportSearchPath.System32);
        nint handlerModule = 0;
        nint memory = 0;
        var registered = false;
        INativeJitContinuationRegistration? continuationRegistration = null;
        var loaded = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var module in modules)
            {
                var handle = JitWindows.LoadLibraryEx(module.FullPath, 0, 0x100 | 0x800);
                if (handle == 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
                loaded.Add(module.DllName, handle);
            }
            var exitProcess = NativeLibrary.GetExport(exitProcessModule, "ExitProcess");
            var resolvedExports = new Dictionary<(nint Module, string Export), nint>
            {
                [(exitProcessModule, "ExitProcess")] = exitProcess,
            };
            memory = JitWindows.VirtualAlloc(0, (nuint)size, 0x3000, 0x04);
            if (memory == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            var bytes = new Span<byte>((void*)memory, size);
            text.CopyTo(bytes);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(pointerOffset, sizeof(nint)), exitProcess);
            for (var index = 0; index < code.NativeImports.Length; index++)
            {
                var fixup = code.NativeImports[index];
                if (fixup.DllName.Equals("NTDLL.dll", StringComparison.OrdinalIgnoreCase) && handlerModule == 0)
                {
                    handlerModule = NativeLibrary.Load("ntdll.dll", typeof(NativeJit).Assembly, DllImportSearchPath.System32);
                }
                var module = fixup.DeclarationIndex >= 0 ? loaded[fixup.DllName]
                    : fixup.DllName.Equals("NTDLL.dll", StringComparison.OrdinalIgnoreCase)
                        ? handlerModule : exitProcessModule;
                var key = (module, fixup.ExportName);
                if (!resolvedExports.TryGetValue(key, out var target))
                {
                    target = NativeLibrary.GetExport(module, fixup.ExportName);
                    resolvedExports.Add(key, target);
                }
                var pointer = pointerOffset + (sizeof(nint) * (index + 1));
                BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(pointer, sizeof(nint)), target);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(fixup.DisplacementOffset, sizeof(int)),
                    checked(pointer - (fixup.DisplacementOffset + sizeof(int))));
            }
            data.AsSpan(0, code.ReadOnlyDataLength).CopyTo(bytes[readOnlyOffset..]);
            data.AsSpan(code.ReadOnlyDataLength).CopyTo(bytes[writableOffset..]);

            foreach (var fixup in code.ExitProcessFixups)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(fixup, sizeof(int)), checked(pointerOffset - (fixup + 4)));
            }

            foreach (var fixup in code.DataFixups)
            {
                var target = fixup.DataOffset < code.ReadOnlyDataLength
                    ? checked(readOnlyOffset + fixup.DataOffset)
                    : checked(writableOffset + (fixup.DataOffset - code.ReadOnlyDataLength));
                BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(fixup.DisplacementOffset, sizeof(int)),
                    checked(target - (fixup.DisplacementOffset + sizeof(int))));
            }

            var infoOffset = 0;
            for (var index = 0; index < functions.Length; index++)
            {
                var function = functions[index];
                var table = bytes.Slice(tableOffset + (index * NativeUnwind.RuntimeFunctionSize), NativeUnwind.RuntimeFunctionSize);
                var unwindRva = unwindOffset + infoOffset;
                NativeUnwind.WriteFunction(table, function, 0, unwindRva);
                NativeUnwind.WriteInfo(bytes.Slice(unwindRva, NativeUnwind.InfoSize(function)), function);
                infoOffset += NativeUnwind.InfoSize(function);
            }

            if (!JitWindows.VirtualProtect(memory, (nuint)codeSize, 0x20, out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            if (code.ReadOnlyDataLength != 0
                && !JitWindows.VirtualProtect(memory + readOnlyOffset, (nuint)(writableOffset - readOnlyOffset), 0x02, out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            if (!JitWindows.FlushInstructionCache(JitWindows.GetCurrentProcess(), memory, (nuint)codeSize))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            var tableAddress = memory + tableOffset;
            if (!JitWindows.RtlAddFunctionTable(tableAddress, (uint)functions.Length, memory))
            {
                throw new InvalidOperationException("RtlAddFunctionTable failed.");
            }
            registered = true;

            // Resolve every range before invoking code, catching malformed registration without
            // ever running a frame whose unwind metadata is unavailable.
            for (var index = 0; index < functions.Length; index++)
            {
                var entry = JitWindows.RtlLookupFunctionEntry(memory + functions[index].Start, out var imageBase, 0);
                if (entry != tableAddress + (index * NativeUnwind.RuntimeFunctionSize) || imageBase != memory)
                {
                    throw new InvalidOperationException("Native unwind registration could not be resolved.");
                }
            }

            if (continuations.Length != 0)
            {
                continuationRegistration = registrarInputPipe is not null && registrarOutputPipe is not null
                    ? new NativeJitPipeRegistration(registrarInputPipe, registrarOutputPipe)
                    : new NativeJitContinuationRegistration(registrarAssembly!);
                continuationRegistration.Register(memory, text.Length, continuations);
            }

            var entryPoint = (delegate* unmanaged[Stdcall]<int>)(memory + code.EntryPoint);
            var exitCode = entryPoint();
            if (captureHostData)
            {
                dataSnapshot = new byte[code.HostDataLength];
                new ReadOnlySpan<byte>((void*)(memory + readOnlyOffset), code.ReadOnlyDataLength).CopyTo(dataSnapshot);
                var hostWritableLength = code.HostDataLength - code.ReadOnlyDataLength;
                new ReadOnlySpan<byte>((void*)(memory + writableOffset), hostWritableLength)
                    .CopyTo(dataSnapshot.AsSpan(code.ReadOnlyDataLength));
            }
            else
            {
                dataSnapshot = Array.Empty<byte>();
            }
            return exitCode;
        }
        finally
        {
            Release(memory, tableOffset, registered, continuationRegistration,
                exitProcessModule, handlerModule, loaded, modules);
        }
    }

    private static bool ContainsSortedExport(IReadOnlyList<string> exports, string name)
    {
        // Graph package decoding guarantees ordinally sorted exports.
        var low = 0;
        var high = exports.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var comparison = StringComparer.Ordinal.Compare(exports[middle], name);
            if (comparison == 0)
            {
                return true;
            }
            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return false;
    }

    private static void Release(nint memory, int tableOffset, bool registered,
        INativeJitContinuationRegistration? continuationRegistration, nint module, nint handlerModule,
        Dictionary<string, nint> loaded, IReadOnlyList<NativeJitModule> modules)
    {
        // Never free a still-registered table: another thread could unwind through its address.
        var removed = !registered || JitWindows.RtlDeleteFunctionTable(memory + tableOffset);
        if (!removed)
        {
            if (continuationRegistration is not null)
            {
                // An abandoned registrar pipe would remove EH targets while the unwind table still exists.
                lock (s_retainedRegistrations)
                {
                    s_retainedRegistrations.Add(continuationRegistration);
                }
            }
            throw new InvalidOperationException("RtlDeleteFunctionTable failed; native memory remains allocated.");
        }
        continuationRegistration?.Dispose();
        var released = memory == 0 || JitWindows.VirtualFree(memory, 0, 0x8000);
        var error = Marshal.GetLastPInvokeError();
        if (!released)
        {
            throw new Win32Exception(error);
        }
        for (var index = modules.Count - 1; index >= 0; index--)
        {
            if (loaded.TryGetValue(modules[index].DllName, out var handle))
            {
                NativeLibrary.Free(handle);
            }
        }
        if (handlerModule != 0)
        {
            NativeLibrary.Free(handlerModule);
        }
        NativeLibrary.Free(module);
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) & (-alignment));
}

internal static partial class JitWindows
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint LoadLibraryEx(string path, nint file, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint VirtualAlloc(nint address, nuint size, uint allocationType, uint protection);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool VirtualProtect(nint address, nuint size, uint newProtection, out uint oldProtection);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool VirtualFree(nint address, nuint size, uint freeType);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FlushInstructionCache(nint process, nint address, nuint size);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    internal static partial nint GetCurrentProcess();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessMitigationPolicy(nint process, int policy, out uint flags, nuint length);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "RtlAddFunctionTable")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool RtlAddFunctionTable(nint table, uint count, nint baseAddress);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "RtlDeleteFunctionTable")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool RtlDeleteFunctionTable(nint table);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "RtlLookupFunctionEntry")]
    internal static partial nint RtlLookupFunctionEntry(nint controlPc, out nint imageBase, nint historyTable);
}
