// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

/// <summary>The native entry result and an owned snapshot of its compiler-supplied data buffers.</summary>
/// <param name="ExitCode">The callable entry's int32 result.</param>
/// <param name="Data">A copy of the initialized data image after successful execution.</param>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The caller owns an independent snapshot after native memory is freed.")]
public sealed record NativeExecutionResult(int ExitCode, byte[] Data)
{
    /// <summary>Byte offsets of host buffers in Data, indexed by their bound-program order.</summary>
    public ImmutableArray<int> BufferOffsets { get; init; } = [];
}

/// <summary>Executes verified native code in the current Windows x64 process.</summary>
/// <remarks>
/// Unhandled checked failures terminate the process; recovery occurs only within generated Terrum regions.
/// Run untrusted programs only in a separately supervised process; this method provides no timeout or isolation.
/// </remarks>
public static unsafe class NativeJit
{
    /// <summary>Emits and executes the callable entry point, returning its int32 result.</summary>
    /// <exception cref="PlatformNotSupportedException">The host is not Windows x64.</exception>
    public static int Execute(BoundProgram program, NativeRuntimePolicy runtimePolicy) =>
        ExecuteWithData(program, runtimePolicy).ExitCode;

    /// <summary>Executes the entry point and copies compiler-supplied buffers before releasing native memory.</summary>
    /// <exception cref="PlatformNotSupportedException">The host is not Windows x64.</exception>
    public static NativeExecutionResult ExecuteWithData(BoundProgram program, NativeRuntimePolicy runtimePolicy)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("Native JIT requires a Windows x64 process.");
        }

        var code = NativeEmitter.EmitCode(program, runtimePolicy);
        ValidateBufferMetadata(program, code);
        return ExecuteCode(code);
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

        if (code.BufferOffsets.Length != buffers.Length || readOnlyLength != code.ReadOnlyDataLength
            || totalLength != code.Data.Length)
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

            foreach (var value in buffer.Values)
            {
                if (BinaryPrimitives.ReadInt32LittleEndian(code.Data.AsSpan(expected, sizeof(int))) != value)
                {
                    throw new ArgumentException("Native buffer contents do not match the bound program.", nameof(code));
                }
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

    private static NativeExecutionResult ExecuteCode(NativeCode code)
    {
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

        var functions = new NativeFunctionRange[code.Functions.Length];
        code.Functions.CopyTo(functions);
        Array.Sort(functions, static (left, right) => left.Start.CompareTo(right.Start));
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
        foreach (var fixup in code.NativeImports)
        {
            if (fixup.DisplacementOffset < 0 || fixup.DisplacementOffset > text.Length - sizeof(int)
                || !((fixup.DllName.Equals("KERNEL32.dll", StringComparison.OrdinalIgnoreCase)
                        && fixup.ExportName == "RaiseException")
                    || (fixup.DllName.Equals("NTDLL.dll", StringComparison.OrdinalIgnoreCase)
                        && fixup.ExportName == "__C_specific_handler")))
            {
                throw new ArgumentException("Invalid JIT runtime import.", nameof(code));
            }
            for (var offset = fixup.DisplacementOffset; offset < fixup.DisplacementOffset + sizeof(int); offset++)
            {
                if (!displacementBytes.Add(offset))
                {
                    throw new ArgumentException("Overlapping native displacement fields.", nameof(code));
                }
            }
        }

        var exitProcessModule = NativeLibrary.Load("kernel32.dll", typeof(NativeJit).Assembly, DllImportSearchPath.System32);
        nint handlerModule = 0;
        nint memory = 0;
        var registered = false;

        try
        {
            var exitProcess = NativeLibrary.GetExport(exitProcessModule, "ExitProcess");
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
                var module = fixup.DllName.Equals("NTDLL.dll", StringComparison.OrdinalIgnoreCase)
                    ? handlerModule : exitProcessModule;
                var target = NativeLibrary.GetExport(module, fixup.ExportName);
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

            var entryPoint = (delegate* unmanaged[Stdcall]<int>)(memory + code.EntryPoint);
            var exitCode = entryPoint();
            var snapshot = new byte[data.Length];
            new ReadOnlySpan<byte>((void*)(memory + readOnlyOffset), code.ReadOnlyDataLength).CopyTo(snapshot);
            new ReadOnlySpan<byte>((void*)(memory + writableOffset), writableLength).CopyTo(snapshot.AsSpan(code.ReadOnlyDataLength));
            return new NativeExecutionResult(exitCode, snapshot) { BufferOffsets = code.BufferOffsets };
        }
        finally
        {
            Release(memory, tableOffset, registered, exitProcessModule, handlerModule);
        }
    }

    private static void Release(nint memory, int tableOffset, bool registered, nint module, nint handlerModule)
    {
        // Never free a still-registered table: another thread could unwind through its address.
        var removed = !registered || JitWindows.RtlDeleteFunctionTable(memory + tableOffset);
        var released = removed && (memory == 0 || JitWindows.VirtualFree(memory, 0, 0x8000));
        var error = Marshal.GetLastPInvokeError();
        NativeLibrary.Free(module);
        if (handlerModule != 0)
        {
            NativeLibrary.Free(handlerModule);
        }
        if (!removed)
        {
            throw new InvalidOperationException("RtlDeleteFunctionTable failed; native memory remains allocated.");
        }
        if (!released)
        {
            throw new Win32Exception(error);
        }
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) & (-alignment));
}

internal static partial class JitWindows
{
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
