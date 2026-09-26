// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Terrum.CodeAnalysis.Native;

internal interface INativeJitContinuationRegistration : IDisposable
{
    void Register(nint memory, int textLength, int[] offsets);
}

internal sealed class NativeJitContinuationRegistration : INativeJitContinuationRegistration
{
    private readonly Process _process;
    private readonly NativeJitPipeRegistration _protocol;

    internal NativeJitContinuationRegistration(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("Trusted native JIT registrar assembly is missing.", assemblyPath);
        }
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),
            "..", "..", "..", "dotnet.exe"));
        if (!File.Exists(host))
        {
            throw new NotSupportedException("CET native JIT requires the framework-dependent .NET host for target registration.");
        }
        var start = new ProcessStartInfo(host) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(assemblyPath);
        start.ArgumentList.Add("--native-jit-registrar");
        _process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the native JIT continuation registrar.");
        _protocol = new NativeJitPipeRegistration(_process.StandardOutput.BaseStream,
            _process.StandardInput.BaseStream, TimeSpan.FromSeconds(10), leaveOpen: true);
    }

    public void Register(nint memory, int textLength, int[] offsets) =>
        _protocol.Register(memory, textLength, offsets);

    public void Dispose()
    {
        Exception? failure = null;
        try
        {
            _protocol.Dispose();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                if (failure is not null && !_process.HasExited)
                {
                    _process.Kill();
                }
                if (!_process.WaitForExit(TimeSpan.FromSeconds(10)))
                {
                    failure = Combine(failure, new TimeoutException("Native JIT continuation registrar did not exit after removal."));
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                    }
                    if (!_process.WaitForExit(TimeSpan.FromSeconds(10)))
                    {
                        failure = Combine(failure, new TimeoutException("Native JIT continuation registrar did not exit after termination."));
                    }
                }
                if (failure is null && _process.ExitCode != 0)
                {
                    failure = new InvalidOperationException(
                        $"Native JIT continuation registrar failed after acknowledging target removal (exit code {_process.ExitCode}).");
                }
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                failure = Combine(failure, exception);
            }
            try
            {
                _process.StandardInput.Dispose();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                failure = Combine(failure, exception);
            }
            try
            {
                _process.StandardOutput.Dispose();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                failure = Combine(failure, exception);
            }
            _process.Dispose();
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal static Exception Combine(Exception? earlier, Exception current) =>
        earlier is null ? current : new AggregateException(earlier, current);
}

/// <summary>Out-of-process registration of dynamic CET exception continuation targets.</summary>
public static class NativeJitContinuationRegistrar
{
    /// <summary>
    /// Serves a trusted JIT process from a separate framework-dependent .NET host. This process must
    /// not load or execute the JIT's code or any provider images. The owning process supervises its lifetime.
    /// </summary>
    public static int Serve()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)
            || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("Remote CET continuation registration requires Windows x64 build 20348 or later.");
        }
        if (!JitWindows.HasDynamicEHContinuationTargets())
        {
            throw new PlatformNotSupportedException("The Windows dynamic CET continuation registration API is unavailable.");
        }
        var status = JitWindows.NtQueryInformationProcess(JitWindows.GetCurrentProcess(), 0,
            out var processInformation, Marshal.SizeOf<JitProcessBasicInformation>(), out _);
        if (status < 0 || processInformation._parentProcessId <= 0)
        {
            throw new InvalidOperationException("Could not identify the native JIT registrar's creator process.");
        }
        var handle = JitWindows.OpenProcess(0x101600, false, checked((uint)processInformation._parentProcessId));
        if (handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        JitContinuationTarget[] registered = [];
        Exception? failure = null;
        var result = 2;
        try
        {
            result = ServeRequest(handle, ref registered);
        }
        catch (EndOfStreamException) when (JitWindows.WaitForSingleObject(handle, 1000) == 0)
        {
            // Process exit has already discarded its registered targets and executable pages.
            registered = [];
            result = 0;
        }
        catch (Exception exception) when (exception is IOException or Win32Exception
            or InvalidOperationException or OverflowException)
        {
            failure = exception;
        }
        finally
        {
            if (registered.Length != 0 && !Remove(handle, ref registered))
            {
                failure = NativeJitContinuationRegistration.Combine(failure,
                    new Win32Exception(Marshal.GetLastPInvokeError(),
                        "Native JIT continuation targets remain registered after cleanup."));
            }
            if (!JitWindows.CloseHandle(handle))
            {
                failure = NativeJitContinuationRegistration.Combine(failure,
                    new Win32Exception(Marshal.GetLastPInvokeError(), "Could not close the native JIT creator process handle."));
            }
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return result;
    }

    private static int ServeRequest(nint handle, ref JitContinuationTarget[] registered)
    {
        using var reader = new BinaryReader(Console.OpenStandardInput());
        using var writer = new BinaryWriter(Console.OpenStandardOutput());
        if (reader.ReadByte() != 1)
        {
            return 2;
        }
        var count = reader.ReadUInt16();
        var baseAddress = reader.ReadInt64();
        var codeLength = reader.ReadInt32();
        if (count == 0 || baseAddress <= 0 || codeLength is < 1 or > 16 * 1024 * 1024)
        {
            throw new InvalidDataException("Native JIT continuation request has invalid code bounds.");
        }
        var targets = new JitContinuationTarget[count];
        var unique = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            var offset = reader.ReadInt32();
            if (offset <= 0 || offset >= codeLength || !unique.Add(offset))
            {
                throw new InvalidDataException("Native JIT continuation request has duplicate or out-of-code targets.");
            }
            targets[index] = new JitContinuationTarget(checked((nint)(baseAddress + offset)), 1);
        }
        JitWindows.ValidateExecutableContinuationRegion(handle, checked((nint)baseAddress), codeLength);
        var added = JitWindows.SetProcessDynamicEHContinuationTargets(handle,
            count, targets);
        if (!added || targets.Any(static target => (target._flags & 2) == 0))
        {
            var error = Marshal.GetLastPInvokeError();
            var processed = targets.Where(static target => (target._flags & 2) != 0).ToArray();
            if (processed.Length != 0 && !Remove(handle, ref processed))
            {
                registered = processed;
                writer.Write((byte)2);
                writer.Flush();
                return 2;
            }
            writer.Write((byte)0);
            writer.Flush();
            if (added)
            {
                throw new InvalidOperationException("Native JIT continuation API reported incomplete target registration.");
            }
            throw new Win32Exception(error);
        }
        registered = targets;
        writer.Write((byte)1);
        writer.Flush();
        if (reader.ReadByte() != 2)
        {
            return 2;
        }
        if (!Remove(handle, ref registered))
        {
            writer.Write((byte)2);
            writer.Flush();
            return 2;
        }
        writer.Write((byte)1);
        writer.Flush();
        return 0;
    }

    internal static bool Remove(nint handle, ref JitContinuationTarget[] targets)
    {
        var requested = new JitContinuationTarget[targets.Length];
        for (var index = 0; index < targets.Length; index++)
        {
            requested[index] = new JitContinuationTarget(targets[index]._address, 0);
        }
        var removed = JitWindows.SetProcessDynamicEHContinuationTargets(handle,
            checked((ushort)requested.Length), requested);
        var error = Marshal.GetLastPInvokeError();
        targets = [.. requested.Where(static target => (target._flags & 2) == 0)
            .Select(static target => new JitContinuationTarget(target._address, 1))];
        Marshal.SetLastPInvokeError(error);
        return removed && targets.Length == 0;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct JitContinuationTarget(nint address, nuint flags)
{
    internal readonly nint _address = address;
    internal nuint _flags = flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct JitProcessBasicInformation
{
    internal nint _exitStatus;
    internal nint _pebBaseAddress;
    internal nuint _affinityMask;
    internal nint _basePriority;
    internal nint _processId;
    internal nint _parentProcessId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct JitMemoryBasicInformation
{
    internal nint _baseAddress;
    internal nint _allocationBase;
    internal uint _allocationProtect;
    internal ushort _partitionId;
    internal nuint _regionSize;
    internal uint _state;
    internal uint _protect;
    internal uint _type;
}

internal static partial class JitWindows
{
    internal static void ValidateExecutableContinuationRegion(nint process, nint address, int length)
    {
        var size = checked((nuint)Marshal.SizeOf<JitMemoryBasicInformation>());
        if (VirtualQueryEx(process, address, out var region, size) != size)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        var start = (ulong)address;
        var regionStart = (ulong)region._baseAddress;
        if (region._state != 0x1000 || region._protect != 0x20 || start < regionStart
            || checked(start + (uint)length) > checked(regionStart + region._regionSize))
        {
            throw new InvalidOperationException("Native JIT continuation targets are not within one committed RX code range.");
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nuint VirtualQueryEx(nint process, nint address,
        out JitMemoryBasicInformation information, nuint length);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ntdll.dll", EntryPoint = "NtQueryInformationProcess")]
    internal static partial int NtQueryInformationProcess(nint process, int informationClass,
        out JitProcessBasicInformation information, int length, out int returnLength);

    internal static bool HasDynamicEHContinuationTargets()
    {
        if (!NativeLibrary.TryLoad("kernel32.dll", typeof(NativeJit).Assembly,
            DllImportSearchPath.System32, out var kernel32))
        {
            return false;
        }
        try
        {
            return NativeLibrary.TryGetExport(kernel32, "SetProcessDynamicEHContinuationTargets", out _);
        }
        finally
        {
            NativeLibrary.Free(kernel32);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDynamicEHContinuationTargets(nint process,
        ushort count, [In, Out] JitContinuationTarget[] targets);
}
