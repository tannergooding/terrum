// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.Interop;

internal sealed partial class NativeLoaderInteropTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Nested { public Pair Value; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AlignedInner { public ushort Unit; public byte Valid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AlignedOuter { public ushort Prefix; public AlignedInner Value; public ushort Suffix; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Three { public byte First; public byte Second; public byte Third; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Six { public ushort First; public ushort Second; public ushort Third; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Wide { public Twelve Value; public int Tail; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Sequential { public byte Present; public int Number; public AlignedInner Inner; public ushort Suffix; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BoolCarrier { public byte Value; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CharCarrier { public ushort Value; }

    [StructLayout(LayoutKind.Sequential)]
    private struct IntCarrier { public int Value; }

    [StructLayout(LayoutKind.Sequential)]
    private struct UIntCarrier { public uint Value; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LongCarrier { public long Value; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ULongCarrier { public ulong Value; }

    [Test]
    public async Task IndependentDotNetCallerChecksAggregateAbiSampleAsync()
    {
        AssumeWindowsX64();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }
        var source = Path.Combine(root?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the repository."),
            "tests", "Terrum.CodeAnalysis.Tests", "Native", "Interop", "Fixtures", "aggregate-abi.terrum");
        var compilation = Compilation.Create(SyntaxTree.Parse(await File.ReadAllTextAsync(source).ConfigureAwait(false)));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected the aggregate ABI program.");
        var directory = TestArtifactPaths.CreateDirectory("terrum-aggregate-abi-");
        var handle = IntPtr.Zero;
        try
        {
            var path = Path.Combine(directory, "aggregate-abi.exe");
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program,
                NativeRuntimePolicy.FailFast)).ConfigureAwait(false);
            handle = LoadLibraryExW(path, IntPtr.Zero, 1);
            Assert.That(handle, Is.Not.EqualTo(IntPtr.Zero),
                $"LoadLibraryExW failed (Win32 {Marshal.GetLastPInvokeError()}).");
            var rvas = FunctionRvas(program);
            AssertScalarAndNestedCalls(handle, rvas);
            AssertIndirectAggregateCalls(handle, rvas);
            AssertIntegerAndReceiverCalls(handle, rvas);
            AssertCarrierAndSequentialCalls(handle, rvas);
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

    private static unsafe void AssertScalarAndNestedCalls(IntPtr handle, Dictionary<string, int> rvas)
    {
        var make = (delegate* unmanaged<int, int, Pair>)((byte*)handle + rvas["MakePair"]);
        var read = (delegate* unmanaged<Pair, int>)((byte*)handle + rvas["ReadPair"]);
        var attempt = (delegate* unmanaged<int, int, Pair*, byte>)((byte*)handle + rvas["TryPair"]);
        var value = make(17, 25);
        Assert.That((value.First, value.Second, read(value)), Is.EqualTo((17, 25, 42)));
        var published = new Pair { First = -7, Second = -8 };
        Assert.That(attempt(4, 5, &published), Is.EqualTo(1));
        Assert.That((published.First, published.Second), Is.EqualTo((4, 5)));
        published = new Pair { First = -7, Second = -8 };
        Assert.That(attempt(-1, 5, &published), Is.Zero);
        Assert.That((published.First, published.Second), Is.EqualTo((-7, -8)));

        var attemptInt = (delegate* unmanaged<int*, byte>)((byte*)handle + rvas["TryInt32"]);
        var attemptChar = (delegate* unmanaged<ushort*, byte>)((byte*)handle + rvas["TryChar16"]);
        var attemptBool = (delegate* unmanaged<byte*, byte>)((byte*)handle + rvas["TryBool"]);
        var integers = stackalloc int[4] { 123, 123, 123, 123 };
        var units = stackalloc ushort[6] { 123, 123, 123, 123, 123, 123 };
        var booleans = stackalloc byte[10] { 123, 123, 123, 123, 123, 123, 123, 123, 123, 123 };
        Assert.That(attemptInt(integers + 1), Is.EqualTo(1));
        Assert.That(attemptChar(units + 1), Is.EqualTo(1));
        Assert.That(attemptBool(booleans + 1), Is.EqualTo(1));
        Assert.That(new ReadOnlySpan<int>(integers, 4).ToArray(), Is.EqualTo([123, int.MinValue, 123, 123]));
        Assert.That(new ReadOnlySpan<ushort>(units, 6).ToArray(), Is.EqualTo(new ushort[] { 123, 55296, 123, 123, 123, 123 }));
        Assert.That(new ReadOnlySpan<byte>(booleans, 10).ToArray(), Is.EqualTo(new byte[] { 123, 1, 123, 123, 123, 123, 123, 123, 123, 123 }));
        // A 64-bit caller deliberately leaves dirty upper bits in the narrow input carriers.
        var readChar = (delegate* unmanaged<ulong, int>)((byte*)handle + rvas["ReadChar16"]);
        var readBool = (delegate* unmanaged<ulong, int>)((byte*)handle + rvas["ReadBool"]);
        Assert.That(readChar(0x123456789ABCFFFF), Is.EqualTo(65535));
        Assert.That(readBool(0x123456789ABCFF00), Is.Zero);
        Assert.That(readBool(0x123456789ABCFF01), Is.EqualTo(1));

        var makeNested = (delegate* unmanaged<int, int, Nested>)((byte*)handle + rvas["MakeNested"]);
        var readNested = (delegate* unmanaged<Nested, Nested, Nested, Nested, Nested, int>)((byte*)handle + rvas["ReadNested"]);
        var tryNested = (delegate* unmanaged<byte, Nested*, byte>)((byte*)handle + rvas["TryNested"]);
        var nested = makeNested(17, 25);
        Assert.That((nested.Value.First, nested.Value.Second), Is.EqualTo((17, 25)));
        Assert.That(readNested(nested, nested, nested, nested, nested), Is.EqualTo(109));
        var guarded = stackalloc Nested[3];
        new Span<byte>(guarded, sizeof(Nested) * 3).Fill(0xA5);
        Assert.That(tryNested(1, guarded + 1), Is.EqualTo(1));
        Assert.That((guarded[1].Value.First, guarded[1].Value.Second), Is.EqualTo((17, 25)));
        Assert.That(tryNested(0, guarded + 1), Is.Zero);
        Assert.That((guarded[1].Value.First, guarded[1].Value.Second), Is.EqualTo((17, 25)));
        Assert.That(new ReadOnlySpan<byte>(guarded, sizeof(Nested)).ToArray(), Is.All.EqualTo(0xA5));
        Assert.That(new ReadOnlySpan<byte>(guarded + 2, sizeof(Nested)).ToArray(), Is.All.EqualTo(0xA5));
        var makeAligned = (delegate* unmanaged<AlignedOuter>)((byte*)handle + rvas["MakeAligned"]);
        var readAligned = (delegate* unmanaged<AlignedOuter, int>)((byte*)handle + rvas["ReadAligned"]);
        var aligned = makeAligned();
        Assert.That((aligned.Prefix, aligned.Value.Unit, aligned.Value.Valid, aligned.Suffix),
            Is.EqualTo(((ushort)1, (ushort)39, (byte)1, (ushort)2)));
        Assert.That(readAligned(aligned), Is.EqualTo(42));
    }

    private static unsafe void AssertIndirectAggregateCalls(IntPtr handle, Dictionary<string, int> rvas)
    {
        Assert.That((sizeof(Three), sizeof(Six), sizeof(Twelve), sizeof(Wide)), Is.EqualTo((3, 6, 12, 16)));
        var makeThree = (delegate* unmanaged<Three>)((byte*)handle + rvas["MakeThree"]);
        var readThree = (delegate* unmanaged<Three, int>)((byte*)handle + rvas["ReadThree"]);
        var makeSix = (delegate* unmanaged<Six>)((byte*)handle + rvas["MakeSix"]);
        var readSix = (delegate* unmanaged<Six, int>)((byte*)handle + rvas["ReadSix"]);
        var makeTwelve = (delegate* unmanaged<int, int, int, Twelve>)((byte*)handle + rvas["MakeTwelve"]);
        var readMixed = (delegate* unmanaged<Twelve, Three, Pair, Six, Twelve, int, int>)((byte*)handle + rvas["ReadMixed"]);
        var makeWide = (delegate* unmanaged<int, Wide>)((byte*)handle + rvas["MakeWide"]);
        var readWide = (delegate* unmanaged<Wide, int>)((byte*)handle + rvas["ReadWide"]);
        var echoWide = (delegate* unmanaged<Wide, Wide, Wide, Wide, Wide, Wide>)((byte*)handle + rvas["EchoWide"]);
        var recurseWide = (delegate* unmanaged<int, Wide>)((byte*)handle + rvas["RecurseWide"]);
        var shiftedWide = (delegate* unmanaged<int, int, int, int, int, Wide>)((byte*)handle + rvas["MakeShiftedWide"]);
        var three = makeThree();
        var six = makeSix();
        var twelve = makeTwelve(1, 2, 7);
        var wide = makeWide(9);
        Assert.That((three.First, three.Second, three.Third), Is.EqualTo(((byte)1, (byte)0, (byte)1)));
        Assert.That(readThree(three), Is.EqualTo(42));
        Assert.That((six.First, six.Second, six.Third), Is.EqualTo(((ushort)4, (ushort)5, (ushort)6)));
        Assert.That(readSix(six), Is.EqualTo(15));
        Assert.That((twelve.First, twelve.Second, twelve.Third), Is.EqualTo((1, 2, 7)));
        Assert.That(readMixed(twelve, three, new Pair { First = 17, Second = 25 }, six, twelve, 19), Is.EqualTo(60));
        Assert.That((wide.Value.First, wide.Value.Second, wide.Value.Third, wide.Tail), Is.EqualTo((10, 11, 12, 9)));
        Assert.That(readWide(wide), Is.EqualTo(42));
        Assert.That(readWide(echoWide(wide, wide, wide, wide, wide)), Is.EqualTo(42));
        Assert.That(readWide(recurseWide(5)), Is.EqualTo(42));
        Assert.That(readWide(shiftedWide(1, 2, 3, 1, 2)), Is.EqualTo(42));

        var storage = (byte*)NativeMemory.AlignedAlloc(48, 16);
        if (storage is null)
        {
            Assert.Fail("Could not allocate indirect aggregate ABI guard storage.");
        }
        try
        {
            var bytes = new Span<byte>(storage, 48);
            foreach (var (name, size, first, second, third) in new[] {
                ("MakeThree", 3, 0, 0, 0), ("MakeSix", 6, 0, 0, 0),
                ("MakeTwelve", 12, 1, 2, 7), ("MakeWide", 16, 9, 0, 0),
            })
            {
                bytes.Fill(0xA5);
                var raw = (delegate* unmanaged<void*, int, int, int, void*>)((byte*)handle + rvas[name]);
                Assert.That((nint)raw(storage + 16, first, second, third), Is.EqualTo((nint)(storage + 16)));
                Assert.That(bytes[..16].ToArray(), Is.All.EqualTo(0xA5));
                Assert.That(bytes[(16 + size)..].ToArray(), Is.All.EqualTo(0xA5));
                AssertAggregatePayload(storage + 16, size);
            }
            foreach (var (name, size) in new[] {
                ("TryThree", 3), ("TrySix", 6), ("TryTwelve", 12), ("TryWide", 16), ("ForwardWide", 16),
            })
            {
                bytes.Fill(0xA5);
                var attempt = (delegate* unmanaged<byte, void*, byte>)((byte*)handle + rvas[name]);
                Assert.That(attempt(1, storage + 16), Is.EqualTo(1));
                var published = bytes.ToArray();
                Assert.That(attempt(0, storage + 16), Is.Zero);
                Assert.That(bytes.ToArray(), Is.EqualTo(published));
                Assert.That(bytes[..16].ToArray(), Is.All.EqualTo(0xA5));
                Assert.That(bytes[(16 + size)..].ToArray(), Is.All.EqualTo(0xA5));
                AssertAggregatePayload(storage + 16, size);
                Assert.That(name switch {
                    "TryThree" => readThree(*(Three*)(storage + 16)),
                    "TrySix" => readSix(*(Six*)(storage + 16)),
                    "TryTwelve" => ((Twelve*)(storage + 16))->Third,
                    _ => readWide(*(Wide*)(storage + 16)),
                }, Is.EqualTo(name == "TrySix" ? 15 : name == "TryTwelve" ? 7 : 42));
            }
        }
        finally
        {
            NativeMemory.AlignedFree(storage);
        }
    }

    private static unsafe void AssertAggregatePayload(byte* storage, int size)
    {
        switch (size)
        {
            case 3:
                var three = (Three*)storage;
                Assert.That((three->First, three->Second, three->Third),
                    Is.EqualTo(((byte)1, (byte)0, (byte)1)));
                break;
            case 6:
                var six = (Six*)storage;
                Assert.That((six->First, six->Second, six->Third),
                    Is.EqualTo(((ushort)4, (ushort)5, (ushort)6)));
                break;
            case 12:
                var twelve = (Twelve*)storage;
                Assert.That((twelve->First, twelve->Second, twelve->Third), Is.EqualTo((1, 2, 7)));
                break;
            case 16:
                var wide = (Wide*)storage;
                Assert.That((wide->Value.First, wide->Value.Second, wide->Value.Third, wide->Tail),
                    Is.EqualTo((10, 11, 12, 9)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(size));
        }
    }

    private static unsafe void AssertIntegerAndReceiverCalls(IntPtr handle, Dictionary<string, int> rvas)
    {
        var sum = (delegate* unmanaged<long, long, long, long, long, long, long>)((byte*)handle + rvas["SumInt64"]);
        var echo = (delegate* unmanaged<ulong, ulong>)((byte*)handle + rvas["EchoUInt64"]);
        var make = (delegate* unmanaged<long, ulong, IntegerPair>)((byte*)handle + rvas["MakeWideIntegerPair"]);
        var read = (delegate* unmanaged<IntegerPair, long, long, long, long, int>)((byte*)handle + rvas["ReadWideIntegerPair"]);
        var attempt = (delegate* unmanaged<byte, IntegerPair*, byte>)((byte*)handle + rvas["TryWideIntegerPair"]);
        var value = make(long.MinValue, ulong.MaxValue);
        Assert.That(sum(-2147483649, 2147483648, 1, 2, 3, 37), Is.EqualTo(42));
        Assert.That(echo(ulong.MaxValue), Is.EqualTo(ulong.MaxValue));
        Assert.That((value.Signed, value.Unsigned), Is.EqualTo((long.MinValue, ulong.MaxValue)));
        Assert.That(read(value, -2147483649, 2147483648, 1, 42), Is.EqualTo(42));
        var storage = (byte*)NativeMemory.AlignedAlloc(48, 16);
        if (storage is null)
        {
            Assert.Fail("Could not allocate integer ABI guard storage.");
        }
        try
        {
            var bytes = new Span<byte>(storage, 48);
            bytes.Fill(0xA5);
            var output = (IntegerPair*)(storage + 16);
            var raw = (delegate* unmanaged<IntegerPair*, long, ulong, IntegerPair*>)((byte*)handle + rvas["MakeWideIntegerPair"]);
            Assert.That((nint)raw(output, long.MinValue, ulong.MaxValue), Is.EqualTo((nint)output));
            Assert.That((output->Signed, output->Unsigned), Is.EqualTo((long.MinValue, ulong.MaxValue)));
            Assert.That(attempt(1, output), Is.EqualTo(1));
            var published = bytes.ToArray();
            Assert.That(attempt(0, output), Is.Zero);
            Assert.That(bytes.ToArray(), Is.EqualTo(published));
            Assert.That((output->Signed, output->Unsigned), Is.EqualTo((long.MinValue, ulong.MaxValue)));
            Assert.That(bytes[..16].ToArray(), Is.All.EqualTo(0xA5));
            Assert.That(bytes[32..].ToArray(), Is.All.EqualTo(0xA5));
        }
        finally
        {
            NativeMemory.AlignedFree(storage);
        }
        var small = stackalloc int[3] { -1, 42, -2 };
        var word = ulong.MaxValue;
        var large = value;
        var readSmall = (delegate* unmanaged<int*, int>)((byte*)handle + rvas["SmallOwner.Read"]);
        var readWord = (delegate* unmanaged<ulong*, ulong>)((byte*)handle + rvas["WordOwner.Read"]);
        var readLarge = (delegate* unmanaged<IntegerPair*, ulong>)((byte*)handle + rvas["LargeOwner.Read"]);
        var pack = (delegate* unmanaged<int*, long, long, long, ulong, ulong, IntegerPair>)((byte*)handle + rvas["SmallOwner.Pack"]);
        var packed = pack(small + 1, -4294967296, 4294967296, 42, ulong.MaxValue - 42, 42);
        Assert.That(readSmall(small + 1), Is.EqualTo(42));
        Assert.That(readWord(&word), Is.EqualTo(ulong.MaxValue));
        Assert.That(readLarge(&large), Is.EqualTo(ulong.MaxValue));
        Assert.That((packed.Signed, packed.Unsigned), Is.EqualTo((84L, ulong.MaxValue)));
        Assert.That(new ReadOnlySpan<int>(small, 3).ToArray(), Is.EqualTo([-1, 42, -2]));
        Assert.That(word, Is.EqualTo(ulong.MaxValue));
        Assert.That((large.Signed, large.Unsigned), Is.EqualTo((long.MinValue, ulong.MaxValue)));
    }

    private static unsafe void AssertCarrierAndSequentialCalls(IntPtr handle, Dictionary<string, int> rvas)
    {
        var echoBool = (delegate* unmanaged<BoolCarrier, BoolCarrier>)((byte*)handle + rvas["EchoBoolCarrier"]);
        var echoChar = (delegate* unmanaged<CharCarrier, CharCarrier>)((byte*)handle + rvas["EchoCharCarrier"]);
        var echoInt = (delegate* unmanaged<IntCarrier, IntCarrier>)((byte*)handle + rvas["EchoIntCarrier"]);
        var echoUInt = (delegate* unmanaged<UIntCarrier, UIntCarrier>)((byte*)handle + rvas["EchoUIntCarrier"]);
        var echoLong = (delegate* unmanaged<LongCarrier, LongCarrier>)((byte*)handle + rvas["EchoLongCarrier"]);
        var echoULong = (delegate* unmanaged<ULongCarrier, ULongCarrier>)((byte*)handle + rvas["EchoULongCarrier"]);
        Assert.That((sizeof(BoolCarrier), sizeof(CharCarrier), sizeof(IntCarrier), sizeof(UIntCarrier),
            sizeof(LongCarrier), sizeof(ULongCarrier)), Is.EqualTo((1, 2, 4, 4, 8, 8)));
        Assert.That(echoBool(new BoolCarrier { Value = 1 }).Value, Is.EqualTo(1));
        Assert.That(echoChar(new CharCarrier { Value = 55296 }).Value, Is.EqualTo(55296));
        Assert.That(echoInt(new IntCarrier { Value = int.MinValue }).Value, Is.EqualTo(int.MinValue));
        Assert.That(echoUInt(new UIntCarrier { Value = uint.MaxValue }).Value, Is.EqualTo(uint.MaxValue));
        Assert.That(echoLong(new LongCarrier { Value = long.MinValue }).Value, Is.EqualTo(long.MinValue));
        Assert.That(echoULong(new ULongCarrier { Value = ulong.MaxValue }).Value, Is.EqualTo(ulong.MaxValue));
        Assert.That((sizeof(AlignedInner), sizeof(Sequential)), Is.EqualTo((4, 16)));
        Assert.That((Marshal.OffsetOf<AlignedInner>(nameof(AlignedInner.Unit)),
            Marshal.OffsetOf<AlignedInner>(nameof(AlignedInner.Valid))), Is.EqualTo(((nint)0, (nint)2)));
        Assert.That((Marshal.OffsetOf<Sequential>(nameof(Sequential.Present)),
            Marshal.OffsetOf<Sequential>(nameof(Sequential.Number)),
            Marshal.OffsetOf<Sequential>(nameof(Sequential.Inner)),
            Marshal.OffsetOf<Sequential>(nameof(Sequential.Suffix))), Is.EqualTo(((nint)0, (nint)4, (nint)8, (nint)12)));
        var make = (delegate* unmanaged<int, Sequential>)((byte*)handle + rvas["MakeSequential"]);
        var read = (delegate* unmanaged<Sequential, int>)((byte*)handle + rvas["ReadSequential"]);
        var attempt = (delegate* unmanaged<byte, Sequential*, byte>)((byte*)handle + rvas["TrySequential"]);
        var value = make(37);
        Assert.That((value.Present, value.Number, value.Inner.Unit, value.Inner.Valid, value.Suffix),
            Is.EqualTo(((byte)1, 37, (ushort)2, (byte)1, (ushort)3)));
        Assert.That(read(value), Is.EqualTo(42));
        var storage = (byte*)NativeMemory.AlignedAlloc(48, 16);
        if (storage is null)
        {
            Assert.Fail("Could not allocate sequential aggregate ABI guard storage.");
        }
        try
        {
            var bytes = new Span<byte>(storage, 48);
            bytes.Fill(0xA5);
            var output = (Sequential*)(storage + 16);
            Assert.That(attempt(1, output), Is.EqualTo(1));
            Assert.That(read(*output), Is.EqualTo(42));
            var published = bytes.ToArray();
            Assert.That(attempt(0, output), Is.Zero);
            Assert.That(bytes.ToArray(), Is.EqualTo(published));
            Assert.That(bytes[..16].ToArray(), Is.All.EqualTo(0xA5));
            Assert.That(bytes[32..].ToArray(), Is.All.EqualTo(0xA5));
        }
        finally
        {
            NativeMemory.AlignedFree(storage);
        }
    }
}
