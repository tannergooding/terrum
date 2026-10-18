// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.Interop;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeOwnedCallAbiTests
{
    [ThreadStatic]
    private static int t_parentCleanupCalls;

    [ThreadStatic]
    private static bool t_cleanupOrderValid;

    [ThreadStatic]
    private static IntPtr t_cleanupSemaphore;

    [StructLayout(LayoutKind.Sequential)]
    private struct Ticket { public int Id; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WideTicket { public long Signed; public ulong Unsigned; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bundle { public Ticket Child; public int Tail; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WideBundle { public long Prefix; public Ticket Child; public uint Tail; }

    [Test]
    public async Task IndependentDotNetCallerChecksOwnerCarriersAndPublicationAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var program = Compile("owned-call-abi.terrum", NativeRuntimePolicy.CheckedExceptions);
        var exports = new[] {
            "Consume", "Relay", "RelayWide", "ConsumeWide", "RelayLast", "TryTicket", "TryWide",
            "TryPartial", "CatchOutputFailure", "LaterArgumentFailure", "MakeBundle", "RelayBundle",
            "ConsumeBundle", "RelayWideBundle", "ConsumeWideBundle", "TryWideBundle",
        };
        var directory = TestArtifactPaths.CreateDirectory("terrum-owned-call-abi-");
        var handle = IntPtr.Zero;
        try
        {
            var path = Path.Combine(directory, "owned-call-abi.dll");
            await File.WriteAllBytesAsync(path, NativeEmitter.EmitLibrary(program, exports,
                NativeRuntimePolicy.CheckedExceptions)).ConfigureAwait(false);
            handle = NativeLibrary.Load(path);
            AssertOwnerCalls(handle);
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory).ConfigureAwait(false);
        }
    }

    private static unsafe void AssertOwnerCalls(IntPtr handle)
    {
        Assert.That(sizeof(Ticket), Is.EqualTo(4));
        Assert.That(sizeof(WideTicket), Is.EqualTo(16));
        Assert.That(sizeof(Bundle), Is.EqualTo(8));
        Assert.That(sizeof(WideBundle), Is.EqualTo(16));
        var consume = (delegate* unmanaged<Ticket, int>)NativeLibrary.GetExport(handle, "Consume");
        var relay = (delegate* unmanaged<Ticket, Ticket>)NativeLibrary.GetExport(handle, "Relay");
        var relayWide = (delegate* unmanaged<WideTicket, WideTicket>)NativeLibrary.GetExport(handle, "RelayWide");
        var consumeWide = (delegate* unmanaged<WideTicket, int>)NativeLibrary.GetExport(handle, "ConsumeWide");
        var relayLast = (delegate* unmanaged<int, int, int, int, int, WideTicket, WideTicket>)
            NativeLibrary.GetExport(handle, "RelayLast");
        var tryTicket = (delegate* unmanaged<byte, Ticket*, byte>)NativeLibrary.GetExport(handle, "TryTicket");
        var tryWide = (delegate* unmanaged<byte, int, int, int, int, WideTicket*, byte>)
            NativeLibrary.GetExport(handle, "TryWide");
        var tryPartial = (delegate* unmanaged<Ticket*, WideTicket*, byte>)NativeLibrary.GetExport(handle, "TryPartial");
        var catchOutput = (delegate* unmanaged<Ticket*, byte>)NativeLibrary.GetExport(handle, "CatchOutputFailure");
        var laterArgument = (delegate* unmanaged<int>)NativeLibrary.GetExport(handle, "LaterArgumentFailure");
        var makeBundle = (delegate* unmanaged<Bundle>)NativeLibrary.GetExport(handle, "MakeBundle");
        var relayBundle = (delegate* unmanaged<Bundle, Bundle>)NativeLibrary.GetExport(handle, "RelayBundle");
        var consumeBundle = (delegate* unmanaged<Bundle, int>)NativeLibrary.GetExport(handle, "ConsumeBundle");
        var relayWideBundle = (delegate* unmanaged<WideBundle, WideBundle>)
            NativeLibrary.GetExport(handle, "RelayWideBundle");
        var consumeWideBundle = (delegate* unmanaged<WideBundle, int>)
            NativeLibrary.GetExport(handle, "ConsumeWideBundle");
        var tryWideBundle = (delegate* unmanaged<byte, WideBundle*, byte>)
            NativeLibrary.GetExport(handle, "TryWideBundle");
        Assert.That(consume(relay(new Ticket { Id = 42 })), Is.EqualTo(42));
        var wide = new WideTicket { Signed = long.MinValue, Unsigned = ulong.MaxValue };
        Assert.That(consumeWide(relayWide(wide)), Is.EqualTo(42));
        Assert.That(consumeWide(relayLast(1, 2, 3, 4, 5, wide)), Is.EqualTo(42));

        var storage = (byte*)NativeMemory.AlignedAlloc(48, 16);
        if (storage is null)
        {
            Assert.Fail("Could not allocate native owner ABI guard storage.");
        }
        try
        {
            // Guard bytes are vacant output storage, not additional live Terrum owners.
            var bytes = new Span<byte>(storage, 48);
            bytes.Fill(0xA5);
            var rawRelay = (delegate* unmanaged<WideTicket*, WideTicket, WideTicket*>)
                NativeLibrary.GetExport(handle, "RelayWide");
            var output = (WideTicket*)(storage + 16);
            Assert.That((nint)rawRelay(output, wide), Is.EqualTo((nint)output));
            Assert.That(bytes[..16].ToArray(), Is.All.EqualTo(0xA5));
            Assert.That(bytes[32..].ToArray(), Is.All.EqualTo(0xA5));
            Assert.That(consumeWide(*output), Is.EqualTo(42));

            var tickets = stackalloc Ticket[3];
            var ticketBytes = new Span<byte>(tickets, sizeof(Ticket) * 3);
            for (var iteration = 0; iteration < 2; iteration++)
            {
                ticketBytes.Fill(0xA5);
                bytes.Fill(0xA5);
                Assert.That(tryTicket(0, tickets + 1), Is.Zero);
                Assert.That(tryWide(0, 1, 2, 3, 4, output), Is.Zero);
                Assert.That(tryPartial(tickets + 1, output), Is.Zero);
                Assert.That(catchOutput(tickets + 1), Is.Zero);
                Assert.That(ticketBytes.ToArray(), Is.All.EqualTo(0xA5));
                Assert.That(bytes.ToArray(), Is.All.EqualTo(0xA5));
                Assert.That(tryTicket(1, tickets + 1), Is.EqualTo(1));
                Assert.That(consume(tickets[1]), Is.EqualTo(42));
                Assert.That(tryWide(1, 1, 2, 3, 4, output), Is.EqualTo(1));
                Assert.That(consumeWide(*output), Is.EqualTo(42));
                Assert.That(ticketBytes[..sizeof(Ticket)].ToArray(), Is.All.EqualTo(0xA5));
                Assert.That(ticketBytes[(2 * sizeof(Ticket))..].ToArray(), Is.All.EqualTo(0xA5));
                Assert.That(bytes[..16].ToArray(), Is.All.EqualTo(0xA5));
                Assert.That(bytes[32..].ToArray(), Is.All.EqualTo(0xA5));
                Assert.That(laterArgument(), Is.EqualTo(42));
                Assert.That(consumeBundle(relayBundle(makeBundle())), Is.EqualTo(42));

                bytes.Fill(0xA5);
                var bundleOutput = (WideBundle*)(storage + 16);
                Assert.That(tryWideBundle(0, bundleOutput), Is.Zero);
                Assert.That(bytes.ToArray(), Is.All.EqualTo(0xA5));
                Assert.That(tryWideBundle(1, bundleOutput), Is.EqualTo(1));
                Assert.That(consumeWideBundle(relayWideBundle(*bundleOutput)), Is.EqualTo(42));
                Assert.That(bytes[..16].ToArray(), Is.All.EqualTo(0xA5));
                Assert.That(bytes[32..].ToArray(), Is.All.EqualTo(0xA5));
            }
        }
        finally
        {
            NativeMemory.AlignedFree(storage);
        }
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CleanupCalleesCanOverwriteParentScratchWithoutChangingChildCleanupAsync(
        NativeRuntimePolicy policy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var program = Compile("cleanup-copy-abi.terrum", policy);
        var functions = program.Functions.Select(static function => function.Name is "Parent.Close" or "ReleaseCleanupSemaphore"
            ? function with { IsExternal = true, Body = new BoundBlock([], default) } : function).ToArray();
        program = program with { Functions = [.. functions] };
        var exports = new[] { "Check", "CheckReplacement", "CheckStaged", "CheckPendingReturn", "CheckNested" };
        var image = NativeEmitter.EmitLibraryWithImports(program, exports, [
            new NativeImportBinding("Parent.Close", "kernel32.dll", "GetSystemTimeAsFileTime"),
            new NativeImportBinding("ReleaseCleanupSemaphore", "kernel32.dll", "ReleaseSemaphore"),
        ], policy);
        var directory = TestArtifactPaths.CreateDirectory("terrum-cleanup-copy-abi-");
        var handle = IntPtr.Zero;
        var semaphore = IntPtr.Zero;
        try
        {
            var path = Path.Combine(directory, "cleanup-copy-abi.dll");
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(false);
            handle = NativeLibrary.Load(path);
            ObserveParentCleanupCalls(handle, image);
            semaphore = CreateSemaphoreW(IntPtr.Zero, 0, 4, IntPtr.Zero);
            Assert.That(semaphore, Is.Not.EqualTo(IntPtr.Zero),
                $"CreateSemaphoreW failed (Win32 {Marshal.GetLastPInvokeError()}).");
            foreach (var (name, expectedDrops) in new[] {
                ("Check", 1), ("CheckReplacement", 2), ("CheckStaged", 2),
                ("CheckPendingReturn", 2), ("CheckNested", 1),
            })
            {
                t_parentCleanupCalls = 0;
                t_cleanupOrderValid = true;
                t_cleanupSemaphore = semaphore;
                unsafe
                {
                    var check = (delegate* unmanaged<IntPtr, int>)NativeLibrary.GetExport(handle, name);
                    Assert.That(check(semaphore), Is.EqualTo(42), name);
                }
                Assert.That(t_parentCleanupCalls, Is.EqualTo(expectedDrops), name);
                Assert.That(t_cleanupOrderValid, Is.True, name);
                for (var index = 0; index < expectedDrops; index++)
                {
                    Assert.That(WaitForSingleObject(semaphore, 0), Is.Zero, name);
                }
                Assert.That(WaitForSingleObject(semaphore, 0), Is.EqualTo(258), name);
            }
        }
        finally
        {
            if (semaphore != IntPtr.Zero)
            {
                Assert.That(CloseHandle(semaphore), Is.True);
            }
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory).ConfigureAwait(false);
        }
    }

    private static unsafe void ObserveParentCleanupCalls(IntPtr module, byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var directory = pe.PEHeaders.PEHeader?.ImportAddressTableDirectory
            ?? throw new InvalidDataException("Missing PE import address table.");
        var kernel = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, "kernel32.dll"));
        try
        {
            var target = NativeLibrary.GetExport(kernel, "GetSystemTimeAsFileTime");
            var table = (IntPtr*)((byte*)module + directory.RelativeVirtualAddress);
            var matches = 0;
            var slot = (IntPtr*)null;
            for (var index = 0; index < directory.Size / IntPtr.Size; index++)
            {
                if (table[index] == target)
                {
                    slot = table + index;
                    matches++;
                }
            }
            Assert.That(matches, Is.EqualTo(1));
            Assert.That(VirtualProtect((IntPtr)slot, (nuint)IntPtr.Size, 4, out var protection), Is.True);
            try
            {
                *slot = (IntPtr)(delegate* unmanaged<void*, void>)&ObserveParentCleanup;
            }
            finally
            {
                Assert.That(VirtualProtect((IntPtr)slot, (nuint)IntPtr.Size, protection, out _), Is.True);
            }
        }
        finally
        {
            NativeLibrary.Free(kernel);
        }
    }

    [UnmanagedCallersOnly]
    private static unsafe void ObserveParentCleanup(void* value)
    {
        t_parentCleanupCalls++;
        // Inspect without consuming child drops, then let the real system API
        // clobber the indirect by-value scratch containing the child handle.
        var recorded = ReleaseSemaphore(t_cleanupSemaphore, 1, out var childCalls);
        var restored = recorded && WaitForSingleObject(t_cleanupSemaphore, 0) == 0;
        t_cleanupOrderValid &= restored && childCalls == t_parentCleanupCalls - 1
            && *(IntPtr*)value == t_cleanupSemaphore;
        GetSystemTimeAsFileTime(value);
    }

    private static BoundProgram Compile(string name, NativeRuntimePolicy policy)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Could not find the repository.");
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            File.ReadAllText(Path.Combine(root, "tests", "Terrum.CodeAnalysis.Tests", "Native",
                "Ownership", "Fixtures", name))), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation.Program ?? throw new AssertionException("Expected the native ABI library.");
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CreateSemaphoreW(IntPtr attributes, int initial, int maximum, IntPtr name);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseSemaphore(IntPtr semaphore, int count, out int previous);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern unsafe void GetSystemTimeAsFileTime(void* destination);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint previous);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
