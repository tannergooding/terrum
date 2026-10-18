// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Native.Libraries;
using Terrum.CodeAnalysis.Tests.Native.Pe;

namespace Terrum.CodeAnalysis.Tests.Native.Interop;

[NonParallelizable]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed partial class NativeLoaderInteropTests
{
    private const ulong PreferredBase = 0x140000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Pair { public int First; public int Second; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Twelve { public int First; public int Second; public int Third; }

    [StructLayout(LayoutKind.Sequential)]
    private struct IntegerPair { public long Signed; public ulong Unsigned; }

    [Test]
    public async Task SystemLoaderCallsAndUnloadsIndependentNativeLibrariesAsync()
    {
        AssumeWindowsX64();
        var directory = TestArtifactPaths.CreateDirectory("terrum-library-loader-");
        var reservation = IntPtr.Zero;
        try
        {
            reservation = ReservePreferredBase();
            var firstProgram = NativeLibraryTests.Compile();
            foreach (var (name, program, expected) in new[] {
                ("first.dll", firstProgram, 42),
                ("second.dll", NativeLibraryTests.Compile(NativeLibraryTests.Source.Replace(
                    "Hidden() { return 5;", "Hidden() { return 6;", StringComparison.Ordinal)), 43),
            })
            {
                var path = Path.Combine(directory, name);
                await File.WriteAllBytesAsync(path, NativeEmitter.EmitLibrary(program,
                    NativeLibraryTests.Exports, NativeRuntimePolicy.FailFast)).ConfigureAwait(false);
                var handle = NativeLibrary.Load(path);
                try
                {
                    Assert.That((ulong)(nuint)handle, Is.Not.EqualTo(PreferredBase));
                    Assert.That(NativeLibrary.TryGetExport(handle, "Hidden", out _), Is.False);
                    Assert.That(NativeLibrary.TryGetExport(handle, "Unknown", out _), Is.False);
                    AssertLibraryCalls(handle, expected);
                }
                finally
                {
                    NativeLibrary.Free(handle);
                }
                Assert.That(GetModuleHandleW(path), Is.EqualTo(IntPtr.Zero));
            }

            var producer = ModuleCompiler.CreateProducer(firstProgram, NativeLibraryTests.Exports);
            var bindings = NativeLibraryTests.Exports.Select(static name =>
                new NativeImportBinding(name, "first.dll", name)).ToArray();
            var reference = ModuleCompiler.CreateReference(producer);
            var validConsumer = Compilation.Create(SyntaxTree.Parse("""
                bool Forward(bool success, out Wide result) { return TryWide(success, out result); }
                int32 Recurse(int32 count) {
                    if (count == 0) {
                        return Evaluate(MakeTwelve(10, 11, 12),
                            MakeWide(int64.MinValue, uint64.MaxValue), 2147483648, 1, 2);
                    }
                    return Recurse(count - 1);
                }
                int32 Main() {
                    require Forward(true, out var first);
                    if (Forward(false, out var ignored)) { return -1; }
                    require Forward(true, out var second);
                    require first.Signed == int64.MinValue && second.Unsigned == uint64.MaxValue;
                    return Recurse(3);
                }
                """), reference);
            Assert.That(validConsumer.HasErrors, Is.False,
                string.Join(Environment.NewLine, validConsumer.Diagnostics));
            var unchangedImage = NativeEmitter.EmitWithImports(
                validConsumer.Program!, bindings, NativeRuntimePolicy.FailFast);
            var unchangedPath = Path.Combine(directory, "library-consumer.exe");
            await File.WriteAllBytesAsync(unchangedPath, unchangedImage).ConfigureAwait(false);
            await RunWorkerAsync(unchangedPath, 42).ConfigureAwait(false);
            File.Copy(Path.Combine(directory, "second.dll"), Path.Combine(directory, "first.dll"), overwrite: true);
            await RunWorkerAsync(unchangedPath, 43).ConfigureAwait(false);
            Assert.That(await File.ReadAllBytesAsync(unchangedPath).ConfigureAwait(false), Is.EqualTo(unchangedImage));

            var consumer = Compilation.Create(SyntaxTree.Parse("""
                int32 Main() {
                    return Evaluate(MakeTwelve(10, 11, 12), MakeWide(0, 0), 2147483648, 1, 2);
                }
                """), reference);
            Assert.That(consumer.HasErrors, Is.False, string.Join(Environment.NewLine, consumer.Diagnostics));
            var image = NativeEmitter.EmitWithImports(consumer.Program!, bindings, NativeRuntimePolicy.FailFast);
            var executable = Path.Combine(directory, "library-failure.exe");
            await File.WriteAllBytesAsync(executable, image).ConfigureAwait(false);
            await RunWorkerAsync(executable, unchecked((int)0xE0540003)).ConfigureAwait(false);
        }
        finally
        {
            ReleaseReservation(reservation);
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory).ConfigureAwait(false);
        }
    }

    private static unsafe void AssertLibraryCalls(IntPtr handle, int expected)
    {
        var makeTwelve = (delegate* unmanaged<int, int, int, Twelve>)NativeLibrary.GetExport(handle, "MakeTwelve");
        var makeWide = (delegate* unmanaged<long, ulong, IntegerPair>)NativeLibrary.GetExport(handle, "MakeWide");
        var evaluate = (delegate* unmanaged<Twelve, IntegerPair, long, int, int, int>)
            NativeLibrary.GetExport(handle, "Evaluate");
        var tryWide = (delegate* unmanaged<byte, IntegerPair*, byte>)NativeLibrary.GetExport(handle, "TryWide");
        var twelve = makeTwelve(10, 11, 12);
        var wide = makeWide(long.MinValue, ulong.MaxValue);
        Assert.That((twelve.First, twelve.Second, twelve.Third), Is.EqualTo((10, 11, 12)));
        Assert.That((wide.Signed, wide.Unsigned), Is.EqualTo((long.MinValue, ulong.MaxValue)));
        Assert.That(evaluate(twelve, wide, 2147483648, 1, 2), Is.EqualTo(expected));
        var storage = (byte*)NativeMemory.AlignedAlloc(48, 16);
        if (storage is null)
        {
            Assert.Fail("Could not allocate native library ABI guard storage.");
        }
        try
        {
            var bytes = new Span<byte>(storage, 48);
            bytes.Fill(0xA5);
            var output = (IntegerPair*)(storage + 16);
            Assert.That(tryWide(1, output), Is.EqualTo(1));
            Assert.That((output->Signed, output->Unsigned), Is.EqualTo((long.MinValue, ulong.MaxValue)));
            var published = bytes.ToArray();
            Assert.That(tryWide(0, output), Is.Zero);
            Assert.That(bytes.ToArray(), Is.EqualTo(published));
            Assert.That(bytes[..16].ToArray(), Is.All.EqualTo(0xA5));
            Assert.That(bytes[32..].ToArray(), Is.All.EqualTo(0xA5));
        }
        finally
        {
            NativeMemory.AlignedFree(storage);
        }
        AssertUnwindEntry(handle, (IntPtr)evaluate);
    }

    [Test]
    public async Task SystemLoaderMapsDistinctRelocatedExecutableCodeAndDataAsync()
    {
        AssumeWindowsX64();
        var directory = TestArtifactPaths.CreateDirectory("terrum-image-loader-");
        var handles = new IntPtr[2];
        var reservation = IntPtr.Zero;
        try
        {
            reservation = ReservePreferredBase();
            for (var index = 0; index < handles.Length; index++)
            {
                var program = PeRelocationTests.Compile(index == 0 ? PeRelocationTests.Source
                    : PeRelocationTests.Source.Replace("Recursive(5)", "Recursive(4)", StringComparison.Ordinal));
                var path = Path.Combine(directory, $"relocation-{index}.exe");
                await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program,
                    NativeRuntimePolicy.FailFast)).ConfigureAwait(false);
                var handle = LoadLibraryExW(path, IntPtr.Zero, 1);
                handles[index] = handle;
                Assert.That(handle, Is.Not.EqualTo(IntPtr.Zero),
                    $"LoadLibraryExW failed (Win32 {Marshal.GetLastPInvokeError()}).");
                Assert.That((ulong)(nuint)handle, Is.Not.EqualTo(PreferredBase));
                if (index != 0)
                {
                    Assert.That(handle, Is.Not.EqualTo(handles[0]));
                }
                AssertExecutableCalls(handle, FunctionRvas(program));
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                }
            }
            ReleaseReservation(reservation);
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory).ConfigureAwait(false);
        }
    }

    private static unsafe void AssertExecutableCalls(IntPtr handle, Dictionary<string, int> rvas)
    {
        var make = (delegate* unmanaged<int, int, Pair>)((byte*)handle + rvas["MakePair"]);
        var use = (delegate* unmanaged<Pair, int, int, int, int, int>)((byte*)handle + rvas["UsePair"]);
        var read = (delegate* unmanaged<int>)((byte*)handle + rvas["ReadData"]);
        var write = (delegate* unmanaged<int, int>)((byte*)handle + rvas["WriteData"]);
        var recurse = (delegate* unmanaged<int, int>)((byte*)handle + rvas["Recursive"]);
        var pair = make(17, 25);
        Assert.That((pair.First, pair.Second), Is.EqualTo((17, 25)));
        Assert.That(use(pair, 1, 2, 3, 11), Is.EqualTo(59));
        Assert.That(read(), Is.EqualTo(7));
        Assert.That(write(39), Is.EqualTo(42));
        Assert.That(recurse(5), Is.EqualTo(42));
        AssertUnwindEntry(handle, (IntPtr)((byte*)handle + 0x1000));
        AssertUnwindEntry(handle, (IntPtr)recurse);
    }

    [Test]
    public async Task DebugEventsObserveRelocatedProcessBasesAndActualExitStatusesAsync()
    {
        AssumeWindowsX64();
        var directory = TestArtifactPaths.CreateDirectory("terrum-process-relocation-");
        try
        {
            foreach (var (source, expected, name) in new[] {
                (PeRelocationTests.Source, 42U, "first"),
                (PeRelocationTests.Source.Replace("Recursive(5)", "Recursive(4)", StringComparison.Ordinal), 42U, "second"),
                ("int32 Main() { require false; return 0; }", 0xE0540003U, "failure"),
            })
            {
                var path = Path.Combine(directory, name + ".exe");
                await File.WriteAllBytesAsync(path, NativeEmitter.Emit(PeRelocationTests.Compile(source),
                    NativeRuntimePolicy.FailFast)).ConfigureAwait(false);
                ObserveDebuggedProcess(path, expected);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory).ConfigureAwait(false);
        }
    }

    private static void ObserveDebuggedProcess(string path, uint expected)
    {
        var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };
        Assert.That(CreateProcessW(path, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, false,
            0x08000002, IntPtr.Zero, Path.GetDirectoryName(path), ref startup, out var information),
            Is.True, $"CreateProcessW failed (Win32 {Marshal.GetLastPInvokeError()}).");
        var exited = false;
        var imageBase = IntPtr.Zero;
        var exitCode = 0U;
        try
        {
            var timer = Stopwatch.StartNew();
            while (!exited && timer.Elapsed < TimeSpan.FromSeconds(10))
            {
                var remaining = (uint)Math.Max(1, 10_000 - timer.ElapsedMilliseconds);
                Assert.That(WaitForDebugEvent(out var debugEvent, remaining), Is.True,
                    $"WaitForDebugEvent failed (Win32 {Marshal.GetLastPInvokeError()}).");
                try
                {
                    if (debugEvent.Code == 3)
                    {
                        imageBase = debugEvent.ImageBase;
                    }
                    if (debugEvent.Code is 3 or 6 && debugEvent.File != IntPtr.Zero)
                    {
                        Assert.That(CloseHandle(debugEvent.File), Is.True);
                    }
                    if (debugEvent.Code == 5)
                    {
                        exited = true;
                        exitCode = debugEvent.ExitCode;
                    }
                }
                finally
                {
                    Assert.That(ContinueDebugEvent(debugEvent.ProcessId, debugEvent.ThreadId, 0x00010002),
                        Is.True);
                }
            }
            Assert.That(exited, Is.True, "The debugged process exceeded its deadline.");
            Assert.That(imageBase, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That((ulong)(nuint)imageBase, Is.Not.EqualTo(PreferredBase));
            Assert.That(exitCode, Is.EqualTo(expected));
        }
        finally
        {
            try
            {
                if (!exited)
                {
                    var detached = DebugActiveProcessStop(information.ProcessId);
                    var detachError = Marshal.GetLastPInvokeError();
                    var terminated = TerminateProcess(information.Process, 1);
                    var terminationError = Marshal.GetLastPInvokeError();
                    Assert.That(WaitForSingleObject(information.Process, 10_000), Is.Zero,
                        $"The owned process did not exit after TerminateProcess returned {terminated} (Win32 {terminationError}).");
                    Assert.That(detached, Is.True,
                        $"DebugActiveProcessStop failed (Win32 {detachError}).");
                }
                else
                {
                    Assert.That(WaitForSingleObject(information.Process, 10_000), Is.Zero);
                }
            }
            finally
            {
                var threadClosed = CloseHandle(information.Thread);
                var processClosed = CloseHandle(information.Process);
                Assert.That(threadClosed, Is.True);
                Assert.That(processClosed, Is.True);
            }
        }
    }

    private static Dictionary<string, int> FunctionRvas(BoundProgram program)
    {
        var ranges = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast).Functions
            .OrderBy(static range => range.Start).TakeLast(program.Functions.Length).ToArray();
        Assert.That(ranges, Has.Length.EqualTo(program.Functions.Length));
        return program.Functions.Select((function, index) => (function.Name, Rva: 0x1000 + ranges[index].Start))
            .ToDictionary(static entry => entry.Name, static entry => entry.Rva, StringComparer.Ordinal);
    }

    private static void AssumeWindowsX64() => Assume.That(OperatingSystem.IsWindows()
        && RuntimeInformation.ProcessArchitecture == Architecture.X64);

    private static IntPtr ReservePreferredBase()
    {
        var preferred = new IntPtr((long)PreferredBase);
        Assert.That(VirtualQuery(preferred, out var region, (nuint)Marshal.SizeOf<MemoryInformation>()),
            Is.EqualTo((nuint)Marshal.SizeOf<MemoryInformation>()));
        if (region.State != 0x10000)
        {
            return IntPtr.Zero;
        }
        var reservation = VirtualAlloc(preferred, 0x1000000, 0x2000, 1);
        Assert.That(reservation, Is.EqualTo(preferred),
            $"VirtualAlloc failed (Win32 {Marshal.GetLastPInvokeError()}).");
        return reservation;
    }

    private static void ReleaseReservation(IntPtr reservation)
    {
        if (reservation != IntPtr.Zero)
        {
            Assert.That(VirtualFree(reservation, 0, 0x8000), Is.True);
        }
    }

    private static void AssertUnwindEntry(IntPtr image, IntPtr address)
    {
        var entry = RtlLookupFunctionEntry((ulong)(nuint)address, out var imageBase, IntPtr.Zero);
        Assert.That(entry, Is.Not.EqualTo(IntPtr.Zero));
        Assert.That(imageBase, Is.EqualTo((ulong)(nuint)image));
        var rva = (long)address - (long)image;
        Assert.That(Marshal.ReadInt32(entry), Is.LessThanOrEqualTo(rva));
        Assert.That(Marshal.ReadInt32(entry, 4), Is.GreaterThan(rva));
    }

    private static async Task RunWorkerAsync(string path, int expected)
    {
        using var process = Process.Start(new ProcessStartInfo(path) {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path),
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Could not start the native library consumer.");
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.EqualTo(expected));
            Assert.That(await errors.ConfigureAwait(false), Is.Empty);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct MemoryInformation { [FieldOffset(32)] public uint State; }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
        public IntPtr Reserved; public IntPtr Desktop; public IntPtr Title;
        public uint X; public uint Y; public uint Width; public uint Height;
        public uint XChars; public uint YChars; public uint Fill; public uint Flags;
        public ushort Show; public ushort ReservedSize;
        public IntPtr ReservedBytes; public IntPtr Input; public IntPtr Output; public IntPtr Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process; public IntPtr Thread; public uint ProcessId; public uint ThreadId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 176)]
    private struct DebugEvent
    {
        [FieldOffset(0)] public uint Code;
        [FieldOffset(4)] public uint ProcessId;
        [FieldOffset(8)] public uint ThreadId;
        [FieldOffset(16)] public IntPtr File;
        [FieldOffset(16)] public uint ExitCode;
        [FieldOffset(40)] public IntPtr ImageBase;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetModuleHandleW(string path);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr RtlLookupFunctionEntry(ulong address, out ulong imageBase, IntPtr history);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nuint VirtualQuery(IntPtr address, out MemoryInformation information, nuint length);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr VirtualAlloc(IntPtr address, nuint size, uint allocation, uint protection);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFree(IntPtr address, nuint size, uint operation);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, IntPtr commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment,
        string? directory, ref StartupInfo startup, out ProcessInformation information);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WaitForDebugEvent(out DebugEvent debugEvent, uint milliseconds);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ContinueDebugEvent(uint processId, uint threadId, uint status);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DebugActiveProcessStop(uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
