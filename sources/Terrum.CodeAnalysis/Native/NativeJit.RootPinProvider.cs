// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Terrum.CodeAnalysis.Native;

public static unsafe partial class NativeJit
{
    private const string RootPinRuntimeLibraryName = "terrum-root-pin-runtime.dll";
    private const uint RootPinFileIdInfoClass = 18;
    private const uint RootPinGenericRead = 0x80000000;
    private const uint RootPinFileShareRead = 0x00000001;
    private const uint RootPinOpenExisting = 3;
    private const uint RootPinFileAttributeNormal = 0x00000080;
    private const uint RootPinGetModuleHandleFromAddress = 0x00000004;
    private const uint RootPinGetModuleHandlePin = 0x00000001;
    private const uint RootPinMemoryCommit = 0x00001000;
    private const uint RootPinMemoryImage = 0x01000000;
    private const uint RootPinPageGuard = 0x00000100;
    private const uint RootPinPageReadOnly = 0x02;
    private const uint RootPinPageReadWrite = 0x04;
    private const uint RootPinPageWriteCopy = 0x08;
    private const uint RootPinPageExecute = 0x10;
    private const uint RootPinPageExecuteRead = 0x20;
    private const uint RootPinPageExecuteReadWrite = 0x40;
    private const uint RootPinPageExecuteWriteCopy = 0x80;
    private static readonly string[] s_rootPinRuntimeExports = [
        "TerrumRootPinAdmitLoadedMethodsV1",
        "TerrumRootPinAdmitLoadedTypesV2",
        "TerrumRootPinGetRuntimeV1",
    ];
    private static readonly object s_rootPinRuntimeGate = new();
    private static readonly HashSet<nint> s_retainedRootPinRuntimeModules = [];
    private static RootPinRuntimeBindingState s_rootPinRuntimeBindingState;
    private static string? s_rootPinRuntimePath;
    private static RootPinFileIdentity s_rootPinRuntimeFileIdentity;
    private static SafeFileHandle? s_rootPinRuntimeFileHandle;
    private static nint s_rootPinRuntimeModule;
    private static RootPinRuntimeSnapshot? s_rootPinRuntimeSnapshot;

    private enum RootPinRuntimeBindingState : byte
    {
        Unselected,
        Loading,
        Ready,
        Failed,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RootPinRuntimeFunctionTableV1
    {
        internal uint AbiRevision;
        internal uint ByteSize;
        internal nint RootExact;
        internal nint PinExact;
        internal nint PinObject;
        internal nint ResolveExact;
        internal nint RetainSupport;
        internal nint ReleaseSupport;
        internal nint EnterFrame;
        internal nint ExitFrame;
        internal nint Poll;
        internal nint BeginCall;
        internal nint ReturnSupport;
        internal nint FinishCall;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RootPinRuntimeInfoV1
    {
        internal nint Domain;
        internal RootPinRuntimeFunctionTableV1 FunctionTable;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct RootPinFileIdInfo
    {
        internal ulong VolumeSerialNumber;
        internal fixed byte FileId[16];
    }

    private readonly record struct RootPinFileIdentity(
        ulong VolumeSerialNumber, ulong FileIdLow, ulong FileIdHigh);

    private readonly record struct RootPinRuntimeSnapshot(
        nint RuntimeInfo, nint Domain, nint GetRuntime,
        nint AdmitLoadedTypes, nint AdmitLoadedMethods,
        RootPinRuntimeFunctionTableV1 FunctionTable);

    private static IReadOnlyList<NativeJitModule> IncludeRootPinRuntimeProvider(
        NativeCode code, IReadOnlyList<NativeJitModule> modules)
    {
        string? path = null;
        foreach (var fixup in code.NativeImports)
        {
            if (!IsRootPinRuntimeLibrary(fixup.DllName))
            {
                continue;
            }
            if (fixup.DeclarationIndex >= 0)
            {
                throw new NotSupportedException(
                    "RootPin is an internal runtime import, not an authored NativeImport.");
            }
            if (!IsValidRootPinRuntimePath(fixup.DllName)
                || !IsRootPinRuntimeExport(fixup.ExportName))
            {
                throw new NotSupportedException(
                    "A RootPin runtime import requires the selected absolute provider path and an assigned export.");
            }
            if (path is not null && !StringComparer.Ordinal.Equals(path, fixup.DllName))
            {
                throw new NotSupportedException(
                    "One JIT image cannot select multiple RootPin provider paths.");
            }
            path = fixup.DllName;
        }

        var rootPinModules = new List<int>();
        for (var index = 0; index < modules.Count; index++)
        {
            if (IsRootPinRuntimeLibrary(modules[index].DllName)
                || IsRootPinRuntimeLibrary(modules[index].FullPath))
            {
                rootPinModules.Add(index);
            }
        }
        if (path is null)
        {
            if (rootPinModules.Count != 0)
            {
                throw new ArgumentException(
                    "A RootPin provider cannot be loaded without an assigned runtime import.",
                    nameof(modules));
            }
            return modules;
        }

        var provider = new NativeJitModule(path, path, s_rootPinRuntimeExports);
        if (rootPinModules.Count == 0)
        {
            var result = new List<NativeJitModule>(modules.Count + 1);
            result.AddRange(modules);
            result.Add(provider);
            return result;
        }
        if (rootPinModules.Count != 1
            || !StringComparer.Ordinal.Equals(modules[rootPinModules[0]].DllName, path)
            || !StringComparer.Ordinal.Equals(modules[rootPinModules[0]].FullPath, path))
        {
            throw new NotSupportedException(
                "RootPin JIT imports must resolve through their one exact selected provider path.");
        }

        var replaced = modules.ToArray();
        replaced[rootPinModules[0]] = provider;
        return replaced;
    }

    private static bool IsRootPinRuntimeLibrary(string dllName)
        => !string.IsNullOrEmpty(dllName)
            && StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFileName(dllName), RootPinRuntimeLibraryName);

    private static bool IsRootPinRuntimeExport(string exportName)
        => exportName is "TerrumRootPinGetRuntimeV1"
            or "TerrumRootPinAdmitLoadedTypesV2"
            or "TerrumRootPinAdmitLoadedMethodsV1";

    private static bool IsRootPinRuntimeImport(
        NativeImportFixup fixup, Dictionary<string, NativeJitModule> providers)
        => fixup.DeclarationIndex < 0
            && IsValidRootPinRuntimePath(fixup.DllName)
            && IsRootPinRuntimeExport(fixup.ExportName)
            && providers.TryGetValue(fixup.DllName, out var provider)
            && StringComparer.Ordinal.Equals(provider.FullPath, fixup.DllName)
            && ContainsSortedExport(provider.Exports, fixup.ExportName);

    private static bool IsValidRootPinRuntimePath(string path)
    {
        if (!Path.IsPathFullyQualified(path)
            || path.Length < 4
            || !IsAsciiDriveLetter(path[0])
            || path[1] != ':'
            || path[2] != '\\'
            || path.Contains('/', StringComparison.Ordinal)
            || path.IndexOf(':', 2, StringComparison.Ordinal) >= 0
            || path.StartsWith("\\\\", StringComparison.Ordinal)
            || path.Any(static character => character is < ' ' or > '~')
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFileName(path), RootPinRuntimeLibraryName))
        {
            return false;
        }

        return StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(path), path);
    }

    private static bool IsAsciiDriveLetter(char character)
        => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

    private static nint LoadRootPinRuntimeProvider(string path)
    {
        if (!IsValidRootPinRuntimePath(path))
        {
            throw new NotSupportedException("The RootPin provider path is not a supported absolute ASCII path.");
        }

        var (fileIdentity, bootstrap) = ReserveRootPinRuntimeBinding(path);
        var module = JitWindows.LoadLibraryEx(path, 0, 0x100 | 0x800);
        if (module == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (bootstrap)
            {
                FailRootPinRuntimeBootstrap(path, 0);
            }
            throw new Win32Exception(error);
        }

        if (bootstrap)
        {
            var committed = false;
            try
            {
                var snapshot = ValidateRootPinRuntimeProvider(module, path, fileIdentity);
                if (!JitWindows.GetModuleHandleExW(
                    RootPinGetModuleHandleFromAddress | RootPinGetModuleHandlePin,
                    snapshot.GetRuntime, out var pinnedModule)
                    || pinnedModule != module)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                lock (s_rootPinRuntimeGate)
                {
                    if (s_rootPinRuntimeBindingState != RootPinRuntimeBindingState.Loading
                        || !StringComparer.Ordinal.Equals(s_rootPinRuntimePath, path)
                        || s_rootPinRuntimeFileIdentity != fileIdentity)
                    {
                        throw new InvalidOperationException(
                            "The RootPin provider selection changed during bootstrap.");
                    }
                    s_rootPinRuntimeModule = module;
                    s_rootPinRuntimeSnapshot = snapshot;
                    s_rootPinRuntimeBindingState = RootPinRuntimeBindingState.Ready;
                    Monitor.PulseAll(s_rootPinRuntimeGate);
                }
                committed = true;
                return module;
            }
            finally
            {
                if (!committed)
                {
                    FailRootPinRuntimeBootstrap(path, module);
                }
            }
        }

        var succeeded = false;
        try
        {
            var snapshot = ValidateRootPinRuntimeProvider(module, path, fileIdentity);
            lock (s_rootPinRuntimeGate)
            {
                if (s_rootPinRuntimeBindingState != RootPinRuntimeBindingState.Ready
                    || !StringComparer.Ordinal.Equals(s_rootPinRuntimePath, path)
                    || s_rootPinRuntimeModule != module
                    || s_rootPinRuntimeSnapshot is not RootPinRuntimeSnapshot existing
                    || !SameRootPinRuntimeSnapshot(snapshot, existing))
                {
                    _ = s_retainedRootPinRuntimeModules.Add(module);
                    throw new NotSupportedException(
                        "A later JIT attachment resolved a different RootPin provider instance.");
                }
            }
            succeeded = true;
            return module;
        }
        finally
        {
            if (!succeeded && !ShouldKeepRootPinRuntimeModule(module))
            {
                NativeLibrary.Free(module);
            }
        }
    }

    private static (RootPinFileIdentity FileIdentity, bool Bootstrap)
        ReserveRootPinRuntimeBinding(string path)
    {
        lock (s_rootPinRuntimeGate)
        {
            while (s_rootPinRuntimeBindingState == RootPinRuntimeBindingState.Loading)
            {
                _ = Monitor.Wait(s_rootPinRuntimeGate);
            }
            if (s_rootPinRuntimeBindingState == RootPinRuntimeBindingState.Failed)
            {
                throw new NotSupportedException(
                    "The process RootPin provider selection has already failed.");
            }
            if (s_rootPinRuntimeBindingState == RootPinRuntimeBindingState.Ready)
            {
                if (!StringComparer.Ordinal.Equals(s_rootPinRuntimePath, path))
                {
                    throw new NotSupportedException(
                        "A process cannot select a second RootPin provider path.");
                }
                if (s_rootPinRuntimeFileHandle is null
                    || s_rootPinRuntimeFileHandle.IsInvalid
                    || s_rootPinRuntimeFileHandle.IsClosed)
                {
                    throw new InvalidOperationException(
                        "The retained RootPin provider file handle is unavailable.");
                }
                using var assertionFile = OpenRootPinProviderFile(path);
                var assertionIdentity = ReadRootPinFileIdentity(assertionFile);
                if (assertionIdentity != s_rootPinRuntimeFileIdentity)
                {
                    throw new NotSupportedException(
                        "A later RootPin path assertion resolves to a different file identity.");
                }
                return (s_rootPinRuntimeFileIdentity, false);
            }

            SafeFileHandle? file = null;
            try
            {
                file = OpenRootPinProviderFile(path);
                var identity = ReadRootPinFileIdentity(file);
                s_rootPinRuntimePath = path;
                s_rootPinRuntimeFileHandle = file;
                file = null;
                s_rootPinRuntimeFileIdentity = identity;
                s_rootPinRuntimeBindingState = RootPinRuntimeBindingState.Loading;
                return (identity, true);
            }
            catch (Win32Exception)
            {
                file?.Dispose();
                s_rootPinRuntimePath = path;
                s_rootPinRuntimeBindingState = RootPinRuntimeBindingState.Failed;
                Monitor.PulseAll(s_rootPinRuntimeGate);
                throw;
            }
            catch
            {
                file?.Dispose();
                throw;
            }
        }
    }

    private static RootPinRuntimeSnapshot ValidateRootPinRuntimeProvider(
        nint module, string path, RootPinFileIdentity expectedFileIdentity)
    {
        if (!IsValidRootPinRuntimeLayout())
        {
            throw new NotSupportedException("The host RootPin runtime layouts do not match the selected ABI.");
        }
        if (!NativeLibrary.TryGetExport(module, "TerrumRootPinGetRuntimeV1",
                out var getRuntime)
            || !NativeLibrary.TryGetExport(module, "TerrumRootPinAdmitLoadedTypesV2",
                out var admitLoadedTypes)
            || !NativeLibrary.TryGetExport(module, "TerrumRootPinAdmitLoadedMethodsV1",
                out var admitLoadedMethods)
            || !IsExecutableRootPinImageRange(module, getRuntime)
            || !IsExecutableRootPinImageRange(module, admitLoadedTypes)
            || !IsExecutableRootPinImageRange(module, admitLoadedMethods))
        {
            throw new NotSupportedException(
                "The selected RootPin provider lacks same-image runtime or admission exports.");
        }

        var getter = (delegate* unmanaged[Cdecl]<RootPinRuntimeInfoV1*>)getRuntime;
        var runtimeInfo = getter();
        if (runtimeInfo == null || ((nuint)runtimeInfo & 7) != 0
            || !IsReadOnlyRootPinImageRange(module, (nint)runtimeInfo,
                (nuint)sizeof(RootPinRuntimeInfoV1)))
        {
            throw new NotSupportedException(
                "RootPinRuntimeInfoV1 is not an aligned immutable provider-image record.");
        }

        var info = *runtimeInfo;
        if (info.Domain == 0 || ((nuint)info.Domain & 7) != 0
            || !IsReadableRootPinImageRange(module, info.Domain, 1)
            || info.FunctionTable.AbiRevision != 1
            || info.FunctionTable.ByteSize != 104
            || !IsExecutableRootPinTable(module, info.FunctionTable))
        {
            throw new NotSupportedException(
                "The selected RootPin runtime info, domain or function table is unsupported.");
        }

        var secondInfo = getter();
        if (secondInfo != runtimeInfo
            || !IsReadOnlyRootPinImageRange(module, (nint)secondInfo,
                (nuint)sizeof(RootPinRuntimeInfoV1))
            || !SameRootPinRuntimeInfo(info, *secondInfo))
        {
            throw new NotSupportedException(
                "The selected RootPin provider did not return one immutable runtime binding.");
        }

        if (GetMappedRootPinFileIdentity(module) != expectedFileIdentity)
        {
            throw new NotSupportedException(
                "The mapped RootPin provider file differs from its selected path identity.");
        }

        return new RootPinRuntimeSnapshot((nint)runtimeInfo, info.Domain,
            getRuntime, admitLoadedTypes, admitLoadedMethods, info.FunctionTable);
    }

    private static bool IsValidRootPinRuntimeLayout()
        => sizeof(RootPinRuntimeFunctionTableV1) == 104
            && sizeof(RootPinRuntimeInfoV1) == 112
            && Marshal.OffsetOf<RootPinRuntimeInfoV1>(
                nameof(RootPinRuntimeInfoV1.FunctionTable)).ToInt32() == 8
            && Marshal.OffsetOf<RootPinRuntimeFunctionTableV1>(
                nameof(RootPinRuntimeFunctionTableV1.RootExact)).ToInt32() == 8
            && Marshal.OffsetOf<RootPinRuntimeFunctionTableV1>(
                nameof(RootPinRuntimeFunctionTableV1.FinishCall)).ToInt32() == 96;

    private static bool IsExecutableRootPinTable(
        nint module, RootPinRuntimeFunctionTableV1 table)
    {
        nint[] entries = [
            table.RootExact,
            table.PinExact,
            table.PinObject,
            table.ResolveExact,
            table.RetainSupport,
            table.ReleaseSupport,
            table.EnterFrame,
            table.ExitFrame,
            table.Poll,
            table.BeginCall,
            table.ReturnSupport,
            table.FinishCall,
        ];
        foreach (var entry in entries)
        {
            if (!IsExecutableRootPinImageRange(module, entry))
            {
                return false;
            }
        }
        return true;
    }

    private static bool SameRootPinRuntimeInfo(
        RootPinRuntimeInfoV1 left, RootPinRuntimeInfoV1 right)
        => left.Domain == right.Domain
            && SameRootPinRuntimeTable(left.FunctionTable, right.FunctionTable);

    private static bool SameRootPinRuntimeTable(
        RootPinRuntimeFunctionTableV1 left, RootPinRuntimeFunctionTableV1 right)
        => left.AbiRevision == right.AbiRevision
            && left.ByteSize == right.ByteSize
            && left.RootExact == right.RootExact
            && left.PinExact == right.PinExact
            && left.PinObject == right.PinObject
            && left.ResolveExact == right.ResolveExact
            && left.RetainSupport == right.RetainSupport
            && left.ReleaseSupport == right.ReleaseSupport
            && left.EnterFrame == right.EnterFrame
            && left.ExitFrame == right.ExitFrame
            && left.Poll == right.Poll
            && left.BeginCall == right.BeginCall
            && left.ReturnSupport == right.ReturnSupport
            && left.FinishCall == right.FinishCall;

    private static bool SameRootPinRuntimeSnapshot(
        RootPinRuntimeSnapshot left, RootPinRuntimeSnapshot right)
        => left.RuntimeInfo == right.RuntimeInfo
            && left.Domain == right.Domain
            && left.GetRuntime == right.GetRuntime
            && left.AdmitLoadedTypes == right.AdmitLoadedTypes
            && left.AdmitLoadedMethods == right.AdmitLoadedMethods
            && SameRootPinRuntimeTable(left.FunctionTable, right.FunctionTable);

    private static bool IsExecutableRootPinImageRange(nint module, nint address)
        => IsRootPinImageRange(module, address, 1, requireExecutable: true,
            requireImmutable: false);

    private static bool IsReadableRootPinImageRange(
        nint module, nint address, nuint length)
        => IsRootPinImageRange(module, address, length, requireExecutable: false,
            requireImmutable: false);

    private static bool IsReadOnlyRootPinImageRange(
        nint module, nint address, nuint length)
        => IsRootPinImageRange(module, address, length, requireExecutable: false,
            requireImmutable: true);

    private static bool IsRootPinImageRange(
        nint module, nint address, nuint length,
        bool requireExecutable, bool requireImmutable)
    {
        if (address == 0)
        {
            return false;
        }

        var size = checked((nuint)Marshal.SizeOf<JitMemoryBasicInformation>());
        if (JitWindows.VirtualQueryEx(JitWindows.GetCurrentProcess(), address,
                out var region, size) != size
            || region._allocationBase != module || region._state != RootPinMemoryCommit
            || region._type != RootPinMemoryImage || (region._protect & RootPinPageGuard) != 0)
        {
            return false;
        }

        var start = (ulong)address;
        var regionStart = (ulong)region._baseAddress;
        if (start < regionStart)
        {
            return false;
        }
        var offset = checked((nuint)(start - regionStart));
        if (offset > region._regionSize || length > region._regionSize - offset)
        {
            return false;
        }

        var protection = region._protect & 0xFF;
        var executable = protection is RootPinPageExecute or RootPinPageExecuteRead
            or RootPinPageExecuteReadWrite or RootPinPageExecuteWriteCopy;
        if (requireExecutable)
        {
            return executable;
        }
        return requireImmutable
            ? protection is RootPinPageReadOnly or RootPinPageExecuteRead
            : protection is RootPinPageReadOnly or RootPinPageReadWrite
                or RootPinPageWriteCopy or RootPinPageExecuteRead
                or RootPinPageExecuteReadWrite or RootPinPageExecuteWriteCopy;
    }

    private static SafeFileHandle OpenRootPinProviderFile(string path)
    {
        var file = JitWindows.CreateFileW(path, RootPinGenericRead,
            RootPinFileShareRead, 0, RootPinOpenExisting,
            RootPinFileAttributeNormal, 0);
        if (file.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            file.Dispose();
            throw new Win32Exception(error);
        }
        return file;
    }

    private static RootPinFileIdentity ReadRootPinFileIdentity(SafeFileHandle file)
    {
        if (!JitWindows.GetFileInformationByHandleEx(file, RootPinFileIdInfoClass,
                out var information, (uint)sizeof(RootPinFileIdInfo)))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var fileId = information.FileId;
        var bytes = new ReadOnlySpan<byte>(fileId, 16);
        return new RootPinFileIdentity(information.VolumeSerialNumber,
            BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]));
    }

    private static RootPinFileIdentity GetMappedRootPinFileIdentity(nint module)
    {
        var buffer = new char[32768];
        uint length;
        fixed (char* fileName = buffer)
        {
            length = JitWindows.GetMappedFileNameW(JitWindows.GetCurrentProcess(),
                module, fileName, (uint)buffer.Length);
        }
        if (length == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        if (length >= buffer.Length)
        {
            throw new NotSupportedException("The mapped RootPin provider path was truncated.");
        }

        var mappedPath = new string(buffer, 0, checked((int)length));
        if (!mappedPath.StartsWith("\\Device\\", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "The mapped RootPin provider path is not a supported local device path.");
        }
        using var mappedFile = OpenRootPinProviderFile("\\\\?\\GLOBALROOT" + mappedPath);
        return ReadRootPinFileIdentity(mappedFile);
    }

    private static void FailRootPinRuntimeBootstrap(string path, nint module)
    {
        lock (s_rootPinRuntimeGate)
        {
            s_rootPinRuntimePath ??= path;
            s_rootPinRuntimeModule = module;
            s_rootPinRuntimeBindingState = RootPinRuntimeBindingState.Failed;
            if (module != 0)
            {
                _ = s_retainedRootPinRuntimeModules.Add(module);
            }
            Monitor.PulseAll(s_rootPinRuntimeGate);
        }
    }

    private static bool ShouldKeepRootPinRuntimeModule(nint module)
    {
        lock (s_rootPinRuntimeGate)
        {
            return s_retainedRootPinRuntimeModules.Contains(module);
        }
    }
}

internal static partial class JitWindows
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial SafeFileHandle CreateFileW(string path, uint desiredAccess,
        uint shareMode, nint securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, nint templateFile);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFileInformationByHandleEx(
        SafeFileHandle file, uint fileInformationClass,
        out NativeJit.RootPinFileIdInfo information, uint bufferSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleExW",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetModuleHandleExW(
        uint flags, nint address, out nint module);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("psapi.dll", EntryPoint = "GetMappedFileNameW",
        SetLastError = true)]
    internal static unsafe partial uint GetMappedFileNameW(
        nint process, nint address, char* fileName, uint size);
}
