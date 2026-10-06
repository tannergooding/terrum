// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeSourceGrantExperimentTests
{
    private const string ReadExport = "Terrum.CodeAnalysis.ReadUtf16ScalarForSourceAbiTest";
    private const string LengthExport = "Terrum.CodeAnalysis.SourceLengthForSourceAbiTest";
    private const string BoundaryExport = "Terrum.CodeAnalysis.SourceBoundaryForSourceAbiTest";
    private const string FreeExport = "Terrum.CodeAnalysis.SourceFreeForSourceAbiTest";
    private const string SignedExport = "Terrum.CodeAnalysis.SignedSourceUnitForSourceAbiTest";
    private const string UnsignedExport = "Terrum.CodeAnalysis.UnsignedSourceUnitForSourceAbiTest";
    private const string FreeWideExport = "Terrum.CodeAnalysis.SourceFreeWideForSourceAbiTest";
    private const int InvalidRange = -2;
    private const int Mismatch = -3;
    private const string Adapter = """
        namespace Terrum.CodeAnalysis;
        int32 Main() { return 0; }
        public int32 ReadUtf16ScalarForSourceAbiTest(int32 at, int32 end, int32 expectedScalar) {
            if (at < 0 || at >= end || end > Host.Source.Length) { return -2; }
            return at == 0
                ? CheckScalar(expectedScalar: expectedScalar, end: end, at: at)
                : CheckScalar(at, end, expectedScalar);
        }
        int32 CheckScalar(int32 at, int32 end, int32 expectedScalar) {
            int32 first = ReadUtf16Scalar(at, end);
            int32 clobber = SourceFreeForSourceAbiTest(1, 2, 3, 4, 5);
            int32 second = ReadUtf16Scalar(at, end);
            if (first == expectedScalar && second == expectedScalar && clobber == 54321) {
                return second;
            }
            return -3;
        }
        public int32 SourceLengthForSourceAbiTest() {
            int32 clobber = SourceFreeForSourceAbiTest(1, 2, 3, 4, 5);
            return Host.Source.Length + clobber - 54321;
        }
        public int32 SourceBoundaryForSourceAbiTest(int32 at) { return int32(Host.Source[at]); }
        public int64 SignedSourceUnitForSourceAbiTest(int32 at) { return ReadSignedSourceUnit(at); }
        int64 ReadSignedSourceUnit(int32 at) { return int64(Host.Source[at]); }
        public uint64 UnsignedSourceUnitForSourceAbiTest(int32 at) { return ReadUnsignedSourceUnit(at); }
        uint64 ReadUnsignedSourceUnit(int32 at) { return uint64(Host.Source[at]); }
        public int32 SourceFreeForSourceAbiTest(int32 a, int32 b, int32 c, int32 d, int32 e) {
            if (a == 1 && b == 2 && c == 3 && d == 4 && e == 5) { return 54321; }
            if (a == 5 && b == 4 && c == 3 && d == 2 && e == 1) { return 12345; }
            return -3;
        }
        public uint64 SourceFreeWideForSourceAbiTest(uint64 value) { return value; }
        """;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadScalar(nint source, uint count, int at, int end, int expectedScalar);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadLength(nint source, uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadBoundary(nint source, uint count, int at);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SourceFree(int a, int b, int c, int d, int e);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long ReadSignedSourceUnit(nint source, uint count, int at);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong ReadUnsignedSourceUnit(nint source, uint count, int at);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong SourceFreeWide(ulong value);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void ReusesOneDllWithInvocationScopedUtf16Storage(BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var program = Compile(policy, [0x1357, 0x2468, 0xABCD]);
        var (image, code) = EmitExperiment(program,
            [ReadExport, LengthExport, BoundaryExport, FreeExport, SignedExport, UnsignedExport, FreeWideExport],
            (NativeRuntimePolicy)policy);
        InspectArtifact(program, image, code, policy);
        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-experiment-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var read = Marshal.GetDelegateForFunctionPointer<ReadScalar>(NativeLibrary.GetExport(module, ReadExport));
                var length = Marshal.GetDelegateForFunctionPointer<ReadLength>(NativeLibrary.GetExport(module, LengthExport));
                var boundary = Marshal.GetDelegateForFunctionPointer<ReadBoundary>(NativeLibrary.GetExport(module, BoundaryExport));
                var free = Marshal.GetDelegateForFunctionPointer<SourceFree>(NativeLibrary.GetExport(module, FreeExport));
                var signed = Marshal.GetDelegateForFunctionPointer<ReadSignedSourceUnit>(
                    NativeLibrary.GetExport(module, SignedExport));
                var unsigned = Marshal.GetDelegateForFunctionPointer<ReadUnsignedSourceUnit>(
                    NativeLibrary.GetExport(module, UnsignedExport));
                var freeWide = Marshal.GetDelegateForFunctionPointer<SourceFreeWide>(
                    NativeLibrary.GetExport(module, FreeWideExport));
                Assert.Equal(54321, free(1, 2, 3, 4, 5));
                Assert.Equal(12345, free(5, 4, 3, 2, 1));
                Assert.Equal(0xFEDCBA9876543210UL, freeWide(0xFEDCBA9876543210UL));
                Assert.Equal(0, WithPinnedSource(null, 0, length.Invoke));
                Assert.Equal(InvalidRange, WithPinnedSource(null, 0, (pointer, count) => read(pointer, count, 0, 0, 0)));
                Assert.Equal(0, WithPinnedSource([], 0, length.Invoke));
                if (policy == BoundRuntimePolicy.CheckedExceptions)
                {
                    var readableStorage = new[] { 'A' };
                    WithPinnedSource(readableStorage, 0, (pointer, count) =>
                    {
                        Assert.Throws<SEHException>(() => boundary(pointer, count, 0));
                        return 0;
                    });
                }

                string[] fixtures = [
                    "A", "\u00E9", "\0", "\r", "\n", "\r\n", "\u0085", "\u2028", "\u2029",
                    "\u000B", "\u000C", "\uD83D\uDE00", "\uD800\uDC00", "\uDBFF\uDFFF",
                    "\uD800", "\uDC00", "\uD800A", "\uD800\uDBFF", "A\uD83D\uDE00B",
                    "\uFDD0\uFFFF", new string('x', 16_384),
                ];
                foreach (var text in fixtures)
                {
                    var storage = text.ToCharArray();
                    WithPinnedSource(storage, (uint)storage.Length, (pointer, count) =>
                    {
                        Assert.Equal(storage.Length, length(pointer, count));
                        for (var at = 0; at < storage.Length; at++)
                        {
                            // The decoder's end bounds lookahead, not the first read.
                            foreach (var end in new[] { at + 1, storage.Length }.Distinct())
                            {
                                var expected = ExpectedScalar(storage.AsSpan(at, end - at));
                                Assert.Equal(expected, read(pointer, count, at, end, expected));
                            }
                            Assert.Equal((long)storage[at], signed(pointer, count, at));
                            Assert.Equal((ulong)storage[at], unsigned(pointer, count, at));
                        }
                        Assert.Equal(Mismatch, read(pointer, count, 0, storage.Length, int.MaxValue));
                        Assert.Equal(InvalidRange, read(pointer, count, -1, storage.Length, 0));
                        Assert.Equal(InvalidRange, read(pointer, count, storage.Length, storage.Length, 0));
                        Assert.Equal(InvalidRange, read(pointer, count, 0, storage.Length + 1, 0));
                        return 0;
                    });
                }

                const int codeUnitBatchSize = 16_384;
                for (var start = 0; start <= char.MaxValue; start += codeUnitBatchSize)
                {
                    var count = Math.Min(codeUnitBatchSize, char.MaxValue + 1 - start);
                    var storage = new char[count];
                    for (var offset = 0; offset < count; offset++)
                    {
                        storage[offset] = (char)(start + offset);
                    }

                    Assert.Equal(0, WithPinnedSource(storage, (uint)storage.Length, (pointer, sourceCount) =>
                    {
                        Assert.Equal(storage.Length, length(pointer, sourceCount));
                        for (var at = 0; at < storage.Length; at++)
                        {
                            Assert.Equal((long)storage[at], signed(pointer, sourceCount, at));
                            Assert.Equal((ulong)storage[at], unsigned(pointer, sourceCount, at));
                            var expectedScalar = char.IsSurrogate(storage[at]) ? -1 : storage[at];
                            Assert.Equal(expectedScalar,
                                read(pointer, sourceCount, at, at + 1, expectedScalar));
                        }
                        return 0;
                    }));
                }

                const int firstSupplementaryScalar = 0x10000;
                const int supplementaryScalarLimit = 0x110000;
                const int maximumPairsPerGrant = 8_191;
                var pairBuffer = new char[2];
                for (var scalarStart = firstSupplementaryScalar;
                    scalarStart < supplementaryScalarLimit;
                    scalarStart += maximumPairsPerGrant)
                {
                    var pairCount = Math.Min(maximumPairsPerGrant,
                        supplementaryScalarLimit - scalarStart);
                    var storage = new char[2 + pairCount * 2];
                    storage[0] = (char)(scalarStart & 0xFFFF);
                    storage[1] = (char)(scalarStart >> 16);
                    for (var index = 0; index < pairCount; index++)
                    {
                        _ = new Rune(scalarStart + index).EncodeToUtf16(pairBuffer);
                        storage[2 + index * 2] = pairBuffer[0];
                        storage[3 + index * 2] = pairBuffer[1];
                    }

                    Assert.Equal(0, WithPinnedSource(storage, (uint)storage.Length, (pointer, sourceCount) =>
                    {
                        Assert.Equal(storage.Length, length(pointer, sourceCount));
                        for (var index = 0; index < pairCount; index++)
                        {
                            var at = 2 + index * 2;
                            var expected = scalarStart + index;
                            Assert.Equal(-1, read(pointer, sourceCount, at, at + 1, -1));
                            Assert.Equal(expected, read(pointer, sourceCount, at, at + 2, expected));
                        }
                        return 0;
                    }));
                }

                var sameStorage = "\uD83D\uDE00Z".ToCharArray();
                WithPinnedSource(sameStorage, 3, (pointer, _) =>
                {
                    foreach (var count in new uint[] { 0, 1, 2, 3 })
                    {
                        Assert.Equal((int)count, length(pointer, count));
                        var expected = count == 1 ? -1 : 0x1F600;
                        Assert.Equal(count == 0 ? InvalidRange : expected,
                            read(pointer, count, 0, (int)Math.Min(count, 2), expected));
                    }
                    return 0;
                });
                var left = new[] { 'A' };
                var right = new[] { '\uFFFF' };
                WithPinnedSource(left, 1, (leftPointer, count) =>
                    WithPinnedSource(right, 1, (rightPointer, _) =>
                    {
                        Assert.NotEqual(leftPointer, rightPointer);
                        Assert.Equal('A', read(leftPointer, count, 0, 1, 'A'));
                        Assert.Equal(65535, read(rightPointer, count, 0, 1, 65535));
                        Assert.Equal('A', read(leftPointer, count, 0, 1, 'A'));
                        return 0;
                    }));

                Assert.Throws<ArgumentException>(() => InvokeChecked(0, 1, length.Invoke));
                Assert.Throws<ArgumentOutOfRangeException>(() => InvokeChecked(0, 16_385, length.Invoke));
                Assert.Throws<ArgumentOutOfRangeException>(() => WithPinnedSource(new char[16_385], 16_385, length.Invoke));
                Assert.Equal(54321, free(1, 2, 3, 4, 5));
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsInvalidCallShapesBeforeInvokingNativeCode()
    {
        var calls = 0;
        int Invoke(nint pointer, uint count)
        {
            calls++;
            return 0;
        }
        Assert.Throws<ArgumentException>(() => InvokeChecked(0, 1, Invoke));
        Assert.Throws<ArgumentException>(() => InvokeChecked(1, 0, Invoke));
        Assert.Throws<ArgumentOutOfRangeException>(() => InvokeChecked(0, uint.MaxValue, Invoke));
        Assert.Throws<ArgumentOutOfRangeException>(() => WithPinnedSource(['A'], 2, Invoke));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void KeepsProductionEmissionAndUnsupportedExperimentShapesRejected()
    {
        var program = Compile(BoundRuntimePolicy.FailFast, []);
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(program, [ReadExport], NativeRuntimePolicy.FailFast));
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithImports(program, [ReadExport], [], NativeRuntimePolicy.FailFast));
        Assert.Throws<NotSupportedException>(() => NativeEmitter.Emit(program with { EntryPoint = 0 }, NativeRuntimePolicy.FailFast));
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitCode(program with { EntryPoint = 0 }, NativeRuntimePolicy.FailFast));
        Assert.Throws<NotSupportedException>(() => NativeModuleCompiler.CreateProducer(program, [ReadExport], "source_test",
            NativeRuntimePolicy.FailFast));
        var scanner = program with { };
        var profileProperty = Assert.IsType<PropertyInfo>(typeof(BoundProgram).GetProperty(
            nameof(BoundProgram.InvocationProfileId)), exactMatch: false);
        profileProperty.SetValue(scanner, "scanner");
        Assert.Throws<ArgumentException>(() => EmitExperiment(scanner, [ReadExport], NativeRuntimePolicy.FailFast));
        Assert.Throws<ArgumentException>(() => EmitExperiment(
            program with { EntryPoint = 0 }, [ReadExport], NativeRuntimePolicy.FailFast));

        foreach (var extra in new[] {
            "int32 Loop() { mut int32 n = 0; while (n < Host.Source.Length) { n = n + 1; } return n; }",
            "int32 Cleanup() { try { return int32(Host.Source[0]); } finally { int32 n = 0; } }",
            "public struct Pair { public int32 A; public int32 B; } public Pair Aggregate(Pair value) { return value; }",
        })
        {
            var unsupported = Compile(BoundRuntimePolicy.FailFast, [], extra);
            Assert.Throws<NotSupportedException>(() => EmitExperiment(
                unsupported, [ReadExport], NativeRuntimePolicy.FailFast));
        }
        var external = program.Functions[0] with { IsExternal = true, Body = new BoundBlock([], default) };
        Assert.Throws<ArgumentException>(() => EmitExperiment(
            program with { Functions = program.Functions.SetItem(0, external) }, [ReadExport], NativeRuntimePolicy.FailFast));
    }

    private static BoundProgram Compile(BoundRuntimePolicy policy, ImmutableArray<int> sample, string extra = "")
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var trees = new[] { "Utf16CodeUnits", "Utf16Scalar" }.Select(name =>
        {
            var path = Path.Combine(root, "self-host", name + ".terrum");
            return SyntaxTree.Parse(File.ReadAllText(path), path);
        }).Append(SyntaxTree.Parse(Adapter + extra, "source-abi-test.terrum"));
        var compilation = Compilation.Create(trees,
            [new HostBuffer("Source", PrimitiveType.Char16, sample, false) { IsInvocationScoped = true }], policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        return Assert.IsType<BoundProgram>(compilation.Program) with { EntryPoint = -1 };
    }

    private static void InspectArtifact(BoundProgram program, byte[] image, NativeCode code, BoundRuntimePolicy policy)
    {
        Assert.Empty(code.Data);
        Assert.Empty(code.DataFixups);
        Assert.Empty(code.BufferOffsets);
        Assert.Equal(0, code.HostDataLength);
        Assert.Equal(0, code.ReadOnlyDataLength);
        Assert.Equal(-1, code.ManagedRuntimeOffset);
        var other = Compile(policy, [0xFEFF, 0xD800, 0, 0xFFFF, 0xDC00]);
        Assert.Equal(image, EmitExperiment(other,
            [ReadExport, LengthExport, BoundaryExport, FreeExport, SignedExport, UnsignedExport, FreeWideExport],
            (NativeRuntimePolicy)policy).Image);
        using var reader = new PEReader(new MemoryStream(image));
        var header = Assert.IsType<PEHeader>(reader.PEHeaders.PEHeader);
        Assert.Equal(0, header.ThreadLocalStorageTableDirectory.Size);
        Assert.Equal(0, header.BaseRelocationTableDirectory.Size);
        Assert.DoesNotContain(reader.PEHeaders.SectionHeaders, section => section.Name is ".data" or ".rdata");

        var classifier = Assert.IsType<Type>(typeof(NativeEmitter).Assembly.GetType(
            "Terrum.CodeAnalysis.Native.NativeSourceGrantExperiment"), exactMatch: false);
        var classify = Assert.IsType<MethodInfo>(classifier.GetMethod("Classify", BindingFlags.NonPublic | BindingFlags.Static),
            exactMatch: false);
        var dependent = Assert.IsType<bool[]>(classify.Invoke(null, [program]));
        Span<byte> bounds = stackalloc byte[6];
        Span<byte> load = stackalloc byte[11];
        foreach (var function in program.Functions)
        {
            var index = program.Functions.IndexOf(function);
            Assert.Equal(function.Name is ReadExport or LengthExport
                or BoundaryExport or SignedExport or UnsignedExport
                or "Terrum.CodeAnalysis.CheckScalar" or "Terrum.CodeAnalysis.ReadUtf16Scalar"
                or "Terrum.CodeAnalysis.ReadSignedSourceUnit" or "Terrum.CodeAnalysis.ReadUnsignedSourceUnit",
                dependent[index]);
            if (dependent[index])
            {
                var start = code.SourceFunctionOffsets[index];
                // push rbp; sub rsp,imm32; mov rbp,rsp precede the two context stores.
                Assert.Equal(new byte[] { 0x48, 0x89, 0x8D }, code.Code.AsSpan(start + 11, 3).ToArray());
                Assert.Equal(new byte[] { 0x89, 0x95 }, code.Code.AsSpan(start + 18, 2).ToArray());
                var pointerSlot = BinaryPrimitives.ReadInt32LittleEndian(code.Code.AsSpan(start + 14, 4));
                var countSlot = BinaryPrimitives.ReadInt32LittleEndian(code.Code.AsSpan(start + 20, 4));
                Assert.Equal(pointerSlot + 8, countSlot);
                if (function.Name == "Terrum.CodeAnalysis.ReadUtf16Scalar")
                {
                    var decoderRange = Assert.Single(code.Functions, range => range.Start == start);
                    var decoder = code.Code.AsSpan(start, decoderRange.End - start);
                    bounds[0] = 0x3B;
                    bounds[1] = 0x85;
                    BinaryPrimitives.WriteInt32LittleEndian(bounds[2..], countSlot);
                    var boundsAt = decoder.IndexOf(bounds);
                    Assert.True(boundsAt >= 0);
                    Assert.Equal(new byte[] { 0x0F, 0x82 }, decoder.Slice(boundsAt + bounds.Length, 2).ToArray());
                    Assert.Equal(0xE8, decoder[boundsAt + bounds.Length + 6]);
                    load[0] = 0x48;
                    load[1] = 0x8B;
                    load[2] = 0x95;
                    BinaryPrimitives.WriteInt32LittleEndian(load[3..7], pointerSlot);
                    new byte[] { 0x0F, 0xB7, 0x04, 0x42 }.CopyTo(load[7..]);
                    Assert.True(decoder.IndexOf(load) >= 0);
                    var text = Assert.Single(reader.PEHeaders.SectionHeaders, section => section.Name == ".text");
                    Assert.Equal(decoder.ToArray(), reader.GetSectionData(text.VirtualAddress).GetContent(
                        start, decoder.Length).ToArray());
                }
            }
        }
        var freeFunction = Assert.Single(program.Functions, function => function.Name == FreeExport);
        var freeWideFunction = Assert.Single(program.Functions, function => function.Name == FreeWideExport);
        var mainFunction = Assert.Single(program.Functions, function => function.Name == "Terrum.CodeAnalysis.Main");
        var freeProgram = new BoundProgram([freeFunction, freeWideFunction, mainFunction], 2) { RuntimePolicy = policy };
        var freeCode = NativeEmitter.EmitCode(freeProgram, (NativeRuntimePolicy)policy);
        foreach (var (function, originalIndex) in new[] { (freeFunction, 0), (freeWideFunction, 1) })
        {
            var index = program.Functions.IndexOf(function);
            var offset = code.SourceFunctionOffsets[index];
            var range = Assert.Single(code.Functions, range => range.Start == offset);
            var originalOffset = freeCode.SourceFunctionOffsets[originalIndex];
            var originalRange = Assert.Single(freeCode.Functions, range => range.Start == originalOffset);
            Assert.Equal(freeCode.Code.AsSpan(originalOffset, originalRange.End - originalOffset).ToArray(),
                code.Code.AsSpan(offset, range.End - offset).ToArray());
        }
    }

    private static (byte[] Image, NativeCode Code) EmitExperiment(
        BoundProgram program, string[] exports, NativeRuntimePolicy policy)
    {
        var method = Assert.IsType<MethodInfo>(typeof(NativeEmitter).GetMethod("EmitSourceGrantLibraryForTest",
            BindingFlags.NonPublic | BindingFlags.Static), exactMatch: false);
        try
        {
            return Assert.IsType<(byte[], NativeCode)>(method.Invoke(null, [program, exports, policy]));
        }
        catch (TargetInvocationException exception) when (exception.InnerException is Exception inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }

    private static int ExpectedScalar(ReadOnlySpan<char> source)
    {
        return Rune.DecodeFromUtf16(source, out var rune, out _) == OperationStatus.Done ? rune.Value : -1;
    }

    private static int WithPinnedSource(char[]? storage, uint count, Func<nint, uint, int> invoke)
    {
        if (count > 16_384 || count > (uint)(storage?.Length ?? 0))
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        if (storage is null || storage.Length == 0)
        {
            return InvokeChecked(0, count, invoke);
        }
        var pinned = GCHandle.Alloc(storage, GCHandleType.Pinned);
        try
        {
            return InvokeChecked(pinned.AddrOfPinnedObject(), count, invoke);
        }
        finally
        {
            pinned.Free();
        }
    }

    private static int InvokeChecked(nint pointer, uint count, Func<nint, uint, int> invoke)
    {
        if (count > 16_384)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        if (pointer == 0 && count != 0 || (pointer & 1) != 0)
        {
            throw new ArgumentException("Source storage must be nonnull for nonzero counts and two-byte aligned.", nameof(pointer));
        }

        return invoke(pointer, count);
    }
}
