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
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeSourceGrantExperimentTests
{
    private const string ReadExport = "Terrum.CodeAnalysis.ReadUtf16ScalarForSourceAbiTest";
    private const string FlowExport = "Terrum.CodeAnalysis.SourceFlowForSourceAbiTest";
    private const string LineCountExport = "Terrum.CodeAnalysis.SourceLineCountForSourceAbiTest";
    private const string LinePositionExport = "Terrum.CodeAnalysis.SourceLinePositionForSourceAbiTest";
    private const string LineOffsetExport = "Terrum.CodeAnalysis.SourceLineOffsetForSourceAbiTest";
    private const string LineContentExtentExport = "Terrum.CodeAnalysis.SourceLineContentExtentForSourceAbiTest";
    private const string LineExtentExport = "Terrum.CodeAnalysis.SourceLineExtentForSourceAbiTest";
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
            int32 cycleValue = SourceCycleFirst(3, at);
            return at == 0
                ? CheckScalar(expectedScalar: expectedScalar, end: end, at: at, cycleValue: cycleValue)
                : CheckScalar(at, end, expectedScalar, cycleValue);
        }
        int32 CheckScalar(int32 at, int32 end, int32 expectedScalar, int32 cycleValue) {
            int32 first = ReadUtf16Scalar(at, end);
            int32 clobber = SourceFreeForSourceAbiTest(1, 2, 3, 4, 5);
            int32 second = ReadUtf16Scalar(at, end);
            if (first == expectedScalar && second == expectedScalar && clobber == 54321
                && cycleValue == int32(Host.Source[at])) {
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
        int32 SourceCycleFirst(int32 remaining, int32 at) {
            if (remaining == 0) { return 0; }
            return SourceCycleSecond(remaining - 1, at);
        }
        int32 SourceCycleSecond(int32 remaining, int32 at) {
            if (remaining == 0) { return int32(Host.Source[at]); }
            return SourceCycleFirst(remaining - 1, at);
        }
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void ReusesOneDllWithInvocationScopedUtf16Storage(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var program = Compile(policy, [0x1357, 0x2468, 0xABCD]);
        var emitted = EmitExperiment(program,
            [ReadExport, LengthExport, BoundaryExport, FreeExport, SignedExport, UnsignedExport, FreeWideExport],
            (NativeRuntimePolicy)policy);
        var image = emitted.Image;
        var code = emitted.Code;
        InspectArtifact(program, image, code, policy);
        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-experiment-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var generated = new NativeSourceGrantTestBindings(module, emitted.Exports);
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
                Assert.That(generated.Invoke<int>(ReadExport, ['A'], 0, 1, (int)'A'), Is.EqualTo((int)'A'));
                Assert.That(generated.Invoke<int>(LengthExport, null), Is.EqualTo(0));
                Assert.That(generated.Invoke<int>(LengthExport, []), Is.EqualTo(0));
                Assert.That(generated.Invoke<int>(FreeExport, null, 1, 2, 3, 4, 5), Is.EqualTo(54321));
                Assert.That(generated.Invoke<ulong>(FreeWideExport, null, 0xFEDCBA9876543210UL), Is.EqualTo(0xFEDCBA9876543210UL));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    generated.Invoke<int>(LengthExport, new char[16_385]));
                Assert.That(emitted.Exports.Select(static export => export.Name).ToArray(), Is.EqualTo(new[] { ReadExport, LengthExport, BoundaryExport, FreeExport, SignedExport,
                    UnsignedExport, FreeWideExport }.OrderBy(static name => name, StringComparer.Ordinal)));
                Assert.That(free(1, 2, 3, 4, 5), Is.EqualTo(54321));
                Assert.That(free(5, 4, 3, 2, 1), Is.EqualTo(12345));
                Assert.That(freeWide(0xFEDCBA9876543210UL), Is.EqualTo(0xFEDCBA9876543210UL));
                Assert.That(WithPinnedSource(null, 0, length.Invoke), Is.EqualTo(0));
                Assert.That(WithPinnedSource(null, 0, (pointer, count) => read(pointer, count, 0, 0, 0)), Is.EqualTo(InvalidRange));
                Assert.That(WithPinnedSource([], 0, length.Invoke), Is.EqualTo(0));
                if (policy == BoundRuntimePolicy.CheckedExceptions)
                {
                    var readableStorage = new[] { 'A' };
                    WithPinnedSource(readableStorage, 0, (pointer, count) => {
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
                    WithPinnedSource(storage, (uint)storage.Length, (pointer, count) => {
                        Assert.That(length(pointer, count), Is.EqualTo(storage.Length));
                        for (var at = 0; at < storage.Length; at++)
                        {
                            // The decoder's end bounds lookahead, not the first read.
                            foreach (var end in new[] { at + 1, storage.Length }.Distinct())
                            {
                                var expected = ExpectedScalar(storage.AsSpan(at, end - at));
                                Assert.That(read(pointer, count, at, end, expected), Is.EqualTo(expected));
                            }
                            Assert.That(signed(pointer, count, at), Is.EqualTo((long)storage[at]));
                            Assert.That(unsigned(pointer, count, at), Is.EqualTo((ulong)storage[at]));
                        }
                        Assert.That(read(pointer, count, 0, storage.Length, int.MaxValue), Is.EqualTo(Mismatch));
                        Assert.That(read(pointer, count, -1, storage.Length, 0), Is.EqualTo(InvalidRange));
                        Assert.That(read(pointer, count, storage.Length, storage.Length, 0), Is.EqualTo(InvalidRange));
                        Assert.That(read(pointer, count, 0, storage.Length + 1, 0), Is.EqualTo(InvalidRange));
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

                    Assert.That(WithPinnedSource(storage, (uint)storage.Length, (pointer, sourceCount) => {
                        Assert.That(length(pointer, sourceCount), Is.EqualTo(storage.Length));
                        for (var at = 0; at < storage.Length; at++)
                        {
                            Assert.That(signed(pointer, sourceCount, at), Is.EqualTo((long)storage[at]));
                            Assert.That(unsigned(pointer, sourceCount, at), Is.EqualTo((ulong)storage[at]));
                            var expectedScalar = char.IsSurrogate(storage[at]) ? -1 : storage[at];
                            Assert.That(read(pointer, sourceCount, at, at + 1, expectedScalar), Is.EqualTo(expectedScalar));
                        }
                        return 0;
                    }), Is.EqualTo(0));
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

                    Assert.That(WithPinnedSource(storage, (uint)storage.Length, (pointer, sourceCount) => {
                        Assert.That(length(pointer, sourceCount), Is.EqualTo(storage.Length));
                        for (var index = 0; index < pairCount; index++)
                        {
                            var at = 2 + index * 2;
                            var expected = scalarStart + index;
                            Assert.That(read(pointer, sourceCount, at, at + 1, -1), Is.EqualTo(-1));
                            Assert.That(read(pointer, sourceCount, at, at + 2, expected), Is.EqualTo(expected));
                        }
                        return 0;
                    }), Is.EqualTo(0));
                }

                var sameStorage = "\uD83D\uDE00Z".ToCharArray();
                WithPinnedSource(sameStorage, 3, (pointer, _) => {
                    foreach (var count in new uint[] { 0, 1, 2, 3 })
                    {
                        Assert.That(length(pointer, count), Is.EqualTo((int)count));
                        var expected = count == 1 ? -1 : 0x1F600;
                        Assert.That(read(pointer, count, 0, (int)Math.Min(count, 2), expected), Is.EqualTo(count == 0 ? InvalidRange : expected));
                    }
                    return 0;
                });
                var left = new[] { 'A' };
                var right = new[] { '\uFFFF' };
                WithPinnedSource(left, 1, (leftPointer, count) =>
                    WithPinnedSource(right, 1, (rightPointer, _) => {
                        Assert.That(rightPointer, Is.Not.EqualTo(leftPointer));
                        Assert.That(read(leftPointer, count, 0, 1, 'A'), Is.EqualTo('A'));
                        Assert.That(read(rightPointer, count, 0, 1, 65535), Is.EqualTo(65535));
                        Assert.That(read(leftPointer, count, 0, 1, 'A'), Is.EqualTo('A'));
                        return 0;
                    }));

                Assert.Throws<ArgumentException>(() => InvokeChecked(0, 1, length.Invoke));
                Assert.Throws<ArgumentOutOfRangeException>(() => InvokeChecked(0, 16_385, length.Invoke));
                Assert.Throws<ArgumentOutOfRangeException>(() => WithPinnedSource(new char[16_385], 16_385, length.Invoke));
                Assert.That(free(1, 2, 3, 4, 5), Is.EqualTo(54321));
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

    [Test]
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
        Assert.That(calls, Is.EqualTo(0));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void GeneratedBindingsFollowPrivateCalleeSourceDependence(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var sourceFreeProgram = CompileBindingFixture(policy, privateCalleeReadsSource: false);
        var sourceFree = EmitExperiment(sourceFreeProgram, [FlowExport], (NativeRuntimePolicy)policy);
        var sourceFreeExport = global::System.Linq.Enumerable.Single(sourceFree.Exports);
        Assert.That(sourceFreeExport.SourceDependent, Is.False);
        Assert.That(global::System.Linq.Enumerable.Single(sourceFreeExport.ParameterTypes), Is.EqualTo(PrimitiveType.Int32));
        Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitLibrary(sourceFreeProgram, [FlowExport], (NativeRuntimePolicy)policy));

        var sourceFreePath = Path.Combine(Path.GetTempPath(),
            $"terrum-source-grant-binding-free-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(sourceFreePath, sourceFree.Image);
        try
        {
            var module = NativeLibrary.Load(sourceFreePath);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module, sourceFree.Exports);
                Assert.That(bindings.Invoke<int>(FlowExport, null, 42), Is.EqualTo(42));
                Assert.Throws<ArgumentException>(() => bindings.Invoke<int>(FlowExport, ['A'], 42));
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }
        finally
        {
            File.Delete(sourceFreePath);
        }

        var sourceDependentProgram = CompileBindingFixture(policy, privateCalleeReadsSource: true);
        var sourceDependent = EmitExperiment(sourceDependentProgram, [FlowExport], (NativeRuntimePolicy)policy);
        var sourceDependentExport = global::System.Linq.Enumerable.Single(sourceDependent.Exports);
        Assert.That(sourceDependentExport.SourceDependent, Is.True);
        Assert.That(global::System.Linq.Enumerable.Single(sourceDependentExport.ParameterTypes), Is.EqualTo(PrimitiveType.Int32));
        Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitLibrary(sourceDependentProgram, [FlowExport], (NativeRuntimePolicy)policy));

        var sourceDependentPath = Path.Combine(Path.GetTempPath(),
            $"terrum-source-grant-binding-dependent-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(sourceDependentPath, sourceDependent.Image);
        try
        {
            var module = NativeLibrary.Load(sourceDependentPath);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module, sourceDependent.Exports);
                var source = new[] { 'A' };
                Assert.That(bindings.Invoke<int>(FlowExport, source, 0), Is.EqualTo((int)source[0]));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<int>(FlowExport, new char[16_385], 0));
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }
        finally
        {
            File.Delete(sourceDependentPath);
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void GeneratedBindingsMatchBoundedSourceLineQueriesAgainstHostedSourceText(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        const string LineQueryFixture = """
            public int32 SourceLineCountForSourceAbiTest() {
                mut int32 lineCount = 1;
                mut int32 at = 0;
                while (at < Host.Source.Length) {
                    char16 current = Host.Source[at];
                    if (current == char16(13) && at + 1 < Host.Source.Length
                        && Host.Source[at + 1] == char16(10)) {
                        lineCount = lineCount + 1;
                        at = at + 1;
                    }
                    else if (current == char16(10) || current == char16(13)
                        || current == char16(133) || current == char16(8232)
                        || current == char16(8233)) {
                        lineCount = lineCount + 1;
                    }
                    at = at + 1;
                }
                return lineCount;
            }
            public int32 SourceLinePositionForSourceAbiTest(int32 position) {
                mut int32 line = 0;
                mut int32 start = 0;
                mut int32 at = 0;
                while (at < Host.Source.Length) {
                    char16 current = Host.Source[at];
                    mut int32 breakLength = 0;
                    if (current == char16(13)) {
                        breakLength = 1;
                        if (at + 1 < Host.Source.Length && Host.Source[at + 1] == char16(10)) {
                            breakLength = 2;
                        }
                    }
                    else if (current == char16(10) || current == char16(133)
                        || current == char16(8232) || current == char16(8233)) {
                        breakLength = 1;
                    }
                    if (breakLength != 0) {
                        int32 next = at + breakLength;
                        if (position < next) { return line * 65536 + (position - start); }
                        line = line + 1;
                        start = next;
                        at = next;
                    }
                    else {
                        at = at + 1;
                    }
                }
                return line * 65536 + (position - start);
            }
            public int32 SourceLineOffsetForSourceAbiTest(int32 line, int32 character) {
                if (line < 0 || character < 0) { return -1; }
                mut int32 currentLine = 0;
                mut int32 start = 0;
                mut int32 at = 0;
                while (true) {
                    mut int32 breakLength = 0;
                    mut bool hasBreak = false;
                    while (at < Host.Source.Length && !hasBreak) {
                        char16 current = Host.Source[at];
                        if (current == char16(13)) {
                            breakLength = 1;
                            if (at + 1 < Host.Source.Length && Host.Source[at + 1] == char16(10)) {
                                breakLength = 2;
                            }
                            hasBreak = true;
                        }
                        else if (current == char16(10) || current == char16(133)
                            || current == char16(8232) || current == char16(8233)) {
                            breakLength = 1;
                            hasBreak = true;
                        }
                        else {
                            at = at + 1;
                        }
                    }
                    if (currentLine == line) {
                        int32 lineLength = at - start + breakLength;
                        if (hasBreak && character >= lineLength) { return -1; }
                        if (!hasBreak && character > lineLength) { return -1; }
                        return start + character;
                    }
                    if (!hasBreak) { return -1; }
                    at = at + breakLength;
                    start = at;
                    currentLine = currentLine + 1;
                }
            }
            int32 PackSourceLineExtent(int32 line, bool includeBreak) {
                mut int32 currentLine = 0;
                mut int32 start = 0;
                mut int32 at = 0;
                while (at < Host.Source.Length) {
                    char16 current = Host.Source[at];
                    mut int32 breakLength = 0;
                    if (current == char16(13)) {
                        breakLength = 1;
                        if (at + 1 < Host.Source.Length && Host.Source[at + 1] == char16(10)) {
                            breakLength = 2;
                        }
                    }
                    else if (current == char16(10) || current == char16(133)
                        || current == char16(8232) || current == char16(8233)) {
                        breakLength = 1;
                    }
                    if (breakLength != 0) {
                        if (currentLine == line) {
                            mut int32 length = at - start;
                            if (includeBreak) { length = length + breakLength; }
                            return start * 32768 + length;
                        }
                        at = at + breakLength;
                        start = at;
                        currentLine = currentLine + 1;
                    }
                    else {
                        at = at + 1;
                    }
                }
                if (currentLine == line) {
                    return start * 32768 + (Host.Source.Length - start);
                }
                return -1;
            }
            public int32 SourceLineContentExtentForSourceAbiTest(int32 line) {
                return PackSourceLineExtent(line, false);
            }
            public int32 SourceLineExtentForSourceAbiTest(int32 line) {
                return PackSourceLineExtent(line, true);
            }
            """;
        var program = Compile(policy, [], LineQueryFixture);
        var function = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == LineCountExport);
        var positionFunction = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == LinePositionExport);
        var offsetFunction = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == LineOffsetExport);
        var lineContentExtentFunction = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == LineContentExtentExport);
        var lineExtentFunction = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == LineExtentExport);
        var dependencies = Classify(program);
        Assert.That(dependencies[program.Functions.IndexOf(function)], Is.True);
        Assert.That(dependencies[program.Functions.IndexOf(positionFunction)], Is.True);
        Assert.That(dependencies[program.Functions.IndexOf(offsetFunction)], Is.True);
        Assert.That(dependencies[program.Functions.IndexOf(lineContentExtentFunction)], Is.True);
        Assert.That(dependencies[program.Functions.IndexOf(lineExtentFunction)], Is.True);

        var emitted = EmitExperiment(program,
            [LineCountExport, LinePositionExport, LineOffsetExport, LineContentExtentExport, LineExtentExport],
            (NativeRuntimePolicy)policy);
        var lineCountExport = global::System.Linq.Enumerable.Single(emitted.Exports, static export => export.Name == LineCountExport);
        Assert.That(lineCountExport.SourceDependent, Is.True);
        Assert.That(lineCountExport.ParameterTypes, Is.Empty);
        var linePositionExport = global::System.Linq.Enumerable.Single(emitted.Exports, static export => export.Name == LinePositionExport);
        Assert.That(linePositionExport.SourceDependent, Is.True);
        Assert.That(global::System.Linq.Enumerable.Single(linePositionExport.ParameterTypes), Is.EqualTo(PrimitiveType.Int32));
        var lineOffsetExport = global::System.Linq.Enumerable.Single(emitted.Exports, static export => export.Name == LineOffsetExport);
        Assert.That(lineOffsetExport.SourceDependent, Is.True);
        Assert.That(lineOffsetExport.ParameterTypes.Length, Is.EqualTo(2));
        {var __nunitItems0 = lineOffsetExport.ParameterTypes;        Assert.Multiple(() => { foreach (var __nunitItem0 in __nunitItems0) { Assert.That(__nunitItem0, Is.EqualTo(PrimitiveType.Int32)); } });}
        var lineContentExtentExport = global::System.Linq.Enumerable.Single(emitted.Exports, static export => export.Name == LineContentExtentExport);
        Assert.That(lineContentExtentExport.SourceDependent, Is.True);
        Assert.That(global::System.Linq.Enumerable.Single(lineContentExtentExport.ParameterTypes), Is.EqualTo(PrimitiveType.Int32));
        var lineExtentExport = global::System.Linq.Enumerable.Single(emitted.Exports, static export => export.Name == LineExtentExport);
        Assert.That(lineExtentExport.SourceDependent, Is.True);
        Assert.That(global::System.Linq.Enumerable.Single(lineExtentExport.ParameterTypes), Is.EqualTo(PrimitiveType.Int32));
        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-lines-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, emitted.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module, emitted.Exports);
                string[] fixtures = [
                    "", "plain", "\r", "\n", "\u0085", "\u2028", "\u2029", "\r\n",
                    "\r\r\n\n\r", "a\r\nb\rc\nd\u0085e\u2028f\u2029",
                    "\0\uD800\r\n\uDC00\u000B\u000C", new string('x', 16_384),
                    new string('\n', 16_384), string.Concat(Enumerable.Repeat("\r\n", 8_192)),
                    new string('x', 16_383) + "\r",
                    new string('a', 8_191) + "\r\n" + new string('b', 8_191),
                ];
                foreach (var text in fixtures)
                {
                    var source = text.ToCharArray();
                    var snapshot = SourceText.From(text);
                    Assert.That(bindings.Invoke<int>(LineCountExport, source), Is.EqualTo(snapshot.Lines.Length));
                    int[] positions = text.Length <= 256
                        ? Enumerable.Range(0, text.Length + 1).ToArray()
                        : [0, 1, text.Length / 2, text.Length - 1, text.Length];
                    foreach (var position in positions)
                    {
                        var expected = snapshot.GetLinePosition(position);
                        var packed = bindings.Invoke<int>(LinePositionExport, source, position);
                        Assert.That(packed >> 16, Is.EqualTo(expected.Line));
                        Assert.That(packed & 0xFFFF, Is.EqualTo(expected.Character));
                        Assert.That(snapshot.TryGetOffset(expected.Line, expected.Character, out var expectedOffset), Is.True);
                        Assert.That(expectedOffset, Is.EqualTo(position));
                        Assert.That(bindings.Invoke<int>(LineOffsetExport, source, expected.Line, expected.Character), Is.EqualTo(expectedOffset));
                    }
                    int[] lineIndices = snapshot.Lines.Length <= 256
                        ? Enumerable.Range(0, snapshot.Lines.Length).ToArray()
                        : [0, snapshot.Lines.Length / 2, snapshot.Lines.Length - 1];
                    foreach (var lineIndex in lineIndices)
                    {
                        var expected = snapshot.Lines[lineIndex];
                        var content = bindings.Invoke<int>(LineContentExtentExport, source, lineIndex);
                        Assert.That(content >> 15, Is.EqualTo(expected.Span.Start));
                        Assert.That(content & 0x7FFF, Is.EqualTo(expected.Span.Length));
                        var includingBreak = bindings.Invoke<int>(LineExtentExport, source, lineIndex);
                        Assert.That(includingBreak >> 15, Is.EqualTo(expected.SpanIncludingLineBreak.Start));
                        Assert.That(includingBreak & 0x7FFF, Is.EqualTo(expected.SpanIncludingLineBreak.Length));
                    }
                }
                var singleCodeUnitSource = new char[1];
                for (var codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
                {
                    singleCodeUnitSource[0] = (char)codeUnit;
                    var snapshot = SourceText.From(new string(singleCodeUnitSource));
                    Assert.That(bindings.Invoke<int>(LineCountExport, singleCodeUnitSource), Is.EqualTo(snapshot.Lines.Length));
                    for (var position = 0; position <= 1; position++)
                    {
                        var expected = snapshot.GetLinePosition(position);
                        var packed = bindings.Invoke<int>(LinePositionExport, singleCodeUnitSource, position);
                        Assert.That(packed >> 16, Is.EqualTo(expected.Line));
                        Assert.That(packed & 0xFFFF, Is.EqualTo(expected.Character));
                        Assert.That(snapshot.TryGetOffset(expected.Line, expected.Character, out var expectedOffset), Is.True);
                        Assert.That(expectedOffset, Is.EqualTo(position));
                        Assert.That(bindings.Invoke<int>(LineOffsetExport, singleCodeUnitSource,
                            expected.Line, expected.Character), Is.EqualTo(expectedOffset));
                    }
                }
                var crPrefixSource = new[] { '\r', '\0' };
                for (var codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
                {
                    crPrefixSource[1] = (char)codeUnit;
                    var snapshot = SourceText.From(new string(crPrefixSource));
                    Assert.That(bindings.Invoke<int>(LineCountExport, crPrefixSource), Is.EqualTo(snapshot.Lines.Length));
                    for (var position = 0; position <= 2; position++)
                    {
                        var expected = snapshot.GetLinePosition(position);
                        var packed = bindings.Invoke<int>(LinePositionExport, crPrefixSource, position);
                        Assert.That(packed >> 16, Is.EqualTo(expected.Line));
                        Assert.That(packed & 0xFFFF, Is.EqualTo(expected.Character));
                        Assert.That(snapshot.TryGetOffset(expected.Line, expected.Character, out var expectedOffset), Is.True);
                        Assert.That(expectedOffset, Is.EqualTo(position));
                        Assert.That(bindings.Invoke<int>(LineOffsetExport, crPrefixSource,
                            expected.Line, expected.Character), Is.EqualTo(expectedOffset));
                    }
                }
                Assert.That(bindings.Invoke<int>(LineCountExport, null), Is.EqualTo(1));
                Assert.That(bindings.Invoke<int>(LineCountExport, []), Is.EqualTo(1));
                Assert.That(bindings.Invoke<int>(LineCountExport, new string('x', 16_384).ToCharArray()), Is.EqualTo(1));
                Assert.That(bindings.Invoke<int>(LineCountExport, new string('\n', 16_384).ToCharArray()), Is.EqualTo(16_385));
                Assert.That(bindings.Invoke<int>(LineCountExport,
                    string.Concat(Enumerable.Repeat("\r\n", 8_192)).ToCharArray()), Is.EqualTo(8_193));
                Assert.That(bindings.Invoke<int>(LineCountExport,
                    (new string('x', 16_383) + "\r").ToCharArray()), Is.EqualTo(2));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<int>(LineCountExport, new char[16_385]));
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

    [Test]
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
        var profileProperty = ((global::System.Func<PropertyInfo>)(() => {
            var __nunitActual1 = typeof(BoundProgram).GetProperty(
            nameof(BoundProgram.InvocationProfileId));
            Assert.That(__nunitActual1, Is.InstanceOf<PropertyInfo>());
            return (PropertyInfo)__nunitActual1!;
        }))();
        profileProperty.SetValue(scanner, "scanner");
        Assert.Throws<ArgumentException>(() => EmitExperiment(scanner, [ReadExport], NativeRuntimePolicy.FailFast));
        Assert.Throws<ArgumentException>(() => EmitExperiment(
            program with { EntryPoint = 0 }, [ReadExport], NativeRuntimePolicy.FailFast));

        foreach (var extra in new[] {
            "int32 Loop() { mut int32 n = 0; do { n = n + 1; } while (n < Host.Source.Length); return n; }",
            "int32 Loop() { mut int32 n = 0; while (n < Host.Source.Length) { n = n + 1; break; } return n; }",
            "int32 Loop() { mut int32 n = 0; for (mut int32 i = 0; i < Host.Source.Length; i = i + 1) { n = n + 1; continue; } return n; }",
            "int32 Cleanup() { try { return int32(Host.Source[0]); } finally { int32 n = 0; } }",
            "[Blittable] [Layout(LayoutKind.Sequential)] public struct Pair { public int32 A; public int32 B; } public Pair Aggregate(Pair value) { return value; }",
        })
        {
            var unsupported = Compile(BoundRuntimePolicy.FailFast, [], extra);
            var exception = TestExceptionCapture.Capture(() =>
                EmitExperiment(unsupported, [ReadExport], NativeRuntimePolicy.FailFast));
            Assert.That(exception is NotSupportedException, Is.True, $"{extra}: {exception}");
        }
        var external = program.Functions[0] with { IsExternal = true, Body = new BoundBlock([], default) };
        Assert.Throws<ArgumentException>(() => EmitExperiment(
            program with { Functions = program.Functions.SetItem(0, external) }, [ReadExport], NativeRuntimePolicy.FailFast));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void PropagatesSourceDependenceAcrossMutuallyRecursiveCalls(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        const int at = 1;
        var source = new[] { (char)0x1357, (char)0x2468, (char)0xABCD };
        var program = Compile(policy, [.. source.Select(static value => (int)value)],
            recursiveEntryPoint: true);
        var dependencies = Classify(program);
        Assert.That(dependencies[program.Functions.IndexOf(
            global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Terrum.CodeAnalysis.SourceCycleFirst"))], Is.True);
        Assert.That(dependencies[program.Functions.IndexOf(
            global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Terrum.CodeAnalysis.SourceCycleSecond"))], Is.True);

        var image = EmitExperiment(program, [ReadExport], (NativeRuntimePolicy)policy).Image;
        var referenceProgram = program with {
            EntryPoint = program.Functions.IndexOf(
                global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Terrum.CodeAnalysis.Main")),
        };
        var grant = new HostBuffer("Source", PrimitiveType.Char16,
            [.. source.Select(static value => (int)value)], false) { IsInvocationScoped = true };
        var expected = NativeJit.ExecuteWithSourceGrant(referenceProgram, [grant], (NativeRuntimePolicy)policy);
        Assert.That(expected, Is.EqualTo(source[at]));

        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-cycle-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var read = Marshal.GetDelegateForFunctionPointer<ReadScalar>(
                    NativeLibrary.GetExport(module, ReadExport));
                var actual = WithPinnedSource(source, (uint)source.Length,
                    (pointer, count) => read(pointer, count, at, at + 1, expected));
                Assert.That(actual, Is.EqualTo(expected));
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

    private static BoundProgram Compile(BoundRuntimePolicy policy, ImmutableArray<int> sample, string extra = "",
        bool recursiveEntryPoint = false)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var trees = new[] { "Utf16CodeUnits", "Utf16Scalar" }.Select(name => {
            var path = Path.Combine(root, "self-host", name + ".terrum");
            return SyntaxTree.Parse(File.ReadAllText(path), path);
        }).Append(SyntaxTree.Parse((recursiveEntryPoint
            ? Adapter.Replace("int32 Main() { return 0; }",
                "int32 Main() { return ReadUtf16ScalarForSourceAbiTest(1, 2, 9320); }", StringComparison.Ordinal)
            : Adapter) + extra, "source-abi-test.terrum"));
        var compilation = Compilation.Create(trees,
            [new HostBuffer("Source", PrimitiveType.Char16, sample, false) { IsInvocationScoped = true }], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        return program with { EntryPoint = -1 };
    }

    private static BoundProgram CompileBindingFixture(BoundRuntimePolicy policy, bool privateCalleeReadsSource)
    {
        var privateCallee = privateCalleeReadsSource
            ? "return int32(Host.Source[at]);"
            : "return at;";
        var source = """
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            public int32 SourceFlowForSourceAbiTest(int32 at) { return SourceFlowLeaf(at); }
            int32 SourceFlowLeaf(int32 at) { BODY }
            int32 KeepSourceGrant() { return Host.Source.Length; }
            """.Replace("BODY", privateCallee, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source, "source-flow-test.terrum"),
            [new HostBuffer("Source", PrimitiveType.Char16, [], false) { IsInvocationScoped = true }], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))() with { EntryPoint = -1 };
    }

    private static void InspectArtifact(BoundProgram program, byte[] image, NativeCode code, BoundRuntimePolicy policy)
    {
        Assert.That(code.Data, Is.Empty);
        Assert.That(code.DataFixups, Is.Empty);
        Assert.That(code.BufferOffsets, Is.Empty);
        Assert.That(code.HostDataLength, Is.EqualTo(0));
        Assert.That(code.ReadOnlyDataLength, Is.EqualTo(0));
        Assert.That(code.ManagedRuntimeOffset, Is.EqualTo(-1));
        var other = Compile(policy, [0xFEFF, 0xD800, 0, 0xFFFF, 0xDC00]);
        Assert.That(EmitExperiment(other,
            [ReadExport, LengthExport, BoundaryExport, FreeExport, SignedExport, UnsignedExport, FreeWideExport],
            (NativeRuntimePolicy)policy).Image, Is.EqualTo(image));
        using var reader = new PEReader(new MemoryStream(image));
        var header = ((global::System.Func<PEHeader>)(() => { var __nunitActual4 = reader.PEHeaders.PEHeader; Assert.That(__nunitActual4, Is.TypeOf<PEHeader>()); return (PEHeader)__nunitActual4!; }))();
        Assert.That(header.ThreadLocalStorageTableDirectory.Size, Is.EqualTo(0));
        Assert.That(header.BaseRelocationTableDirectory.Size, Is.EqualTo(0));
        Assert.That(global::System.Linq.Enumerable.Any(reader.PEHeaders.SectionHeaders,section => section.Name is ".data" or ".rdata"),Is.False);

        var dependent = Classify(program);
        Span<byte> bounds = stackalloc byte[6];
        Span<byte> load = stackalloc byte[11];
        foreach (var function in program.Functions)
        {
            var index = program.Functions.IndexOf(function);
            Assert.That(dependent[index], Is.EqualTo(function.Name is ReadExport or LengthExport
                or BoundaryExport or SignedExport or UnsignedExport
                or "Terrum.CodeAnalysis.CheckScalar" or "Terrum.CodeAnalysis.ReadUtf16Scalar"
                or "Terrum.CodeAnalysis.ReadUtf16ScalarChecked"
                or "Terrum.CodeAnalysis.ReadSignedSourceUnit" or "Terrum.CodeAnalysis.ReadUnsignedSourceUnit"
                or "Terrum.CodeAnalysis.SourceCycleFirst" or "Terrum.CodeAnalysis.SourceCycleSecond"));
            if (dependent[index])
            {
                var start = code.SourceFunctionOffsets[index];
                // push rbp; sub rsp,imm32; mov rbp,rsp precede the two context stores.
                Assert.That(code.Code.AsSpan(start + 11, 3).ToArray(), Is.EqualTo(new byte[] { 0x48, 0x89, 0x8D }));
                Assert.That(code.Code.AsSpan(start + 18, 2).ToArray(), Is.EqualTo(new byte[] { 0x89, 0x95 }));
                var pointerSlot = BinaryPrimitives.ReadInt32LittleEndian(code.Code.AsSpan(start + 14, 4));
                var countSlot = BinaryPrimitives.ReadInt32LittleEndian(code.Code.AsSpan(start + 20, 4));
                Assert.That(countSlot, Is.EqualTo(pointerSlot + 8));
                if (function.Name == "Terrum.CodeAnalysis.ReadUtf16Scalar")
                {
                    var decoderRange = global::System.Linq.Enumerable.Single(code.Functions, range => range.Start == start);
                    var decoder = code.Code.AsSpan(start, decoderRange.End - start);
                    bounds[0] = 0x3B;
                    bounds[1] = 0x85;
                    BinaryPrimitives.WriteInt32LittleEndian(bounds[2..], countSlot);
                    var boundsAt = decoder.IndexOf(bounds);
                    Assert.That(boundsAt >= 0, Is.True);
                    Assert.That(decoder.Slice(boundsAt + bounds.Length, 2).ToArray(), Is.EqualTo(new byte[] { 0x0F, 0x82 }));
                    Assert.That(decoder[boundsAt + bounds.Length + 6], Is.EqualTo(0xE8));
                    load[0] = 0x48;
                    load[1] = 0x8B;
                    load[2] = 0x95;
                    BinaryPrimitives.WriteInt32LittleEndian(load[3..7], pointerSlot);
                    new byte[] { 0x0F, 0xB7, 0x04, 0x42 }.CopyTo(load[7..]);
                    Assert.That(decoder.IndexOf(load) >= 0, Is.True);
                    var text = global::System.Linq.Enumerable.Single(reader.PEHeaders.SectionHeaders, section => section.Name == ".text");
                    Assert.That(reader.GetSectionData(text.VirtualAddress).GetContent(
                        start, decoder.Length).ToArray(), Is.EqualTo(decoder.ToArray()));
                }
            }
        }
        var freeFunction = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == FreeExport);
        var freeWideFunction = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == FreeWideExport);
        var mainFunction = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Terrum.CodeAnalysis.Main");
        var freeProgram = new BoundProgram([freeFunction, freeWideFunction, mainFunction], 2) { RuntimePolicy = policy };
        var freeCode = NativeEmitter.EmitCode(freeProgram, (NativeRuntimePolicy)policy);
        foreach (var (function, originalIndex) in new[] { (freeFunction, 0), (freeWideFunction, 1) })
        {
            var index = program.Functions.IndexOf(function);
            var offset = code.SourceFunctionOffsets[index];
            var range = global::System.Linq.Enumerable.Single(code.Functions, range => range.Start == offset);
            var originalOffset = freeCode.SourceFunctionOffsets[originalIndex];
            var originalRange = global::System.Linq.Enumerable.Single(freeCode.Functions, range => range.Start == originalOffset);
            Assert.That(code.Code.AsSpan(offset, range.End - offset).ToArray(), Is.EqualTo(freeCode.Code.AsSpan(originalOffset, originalRange.End - originalOffset).ToArray()));
        }
    }

    private static (byte[] Image, NativeCode Code,
        ImmutableArray<(string Name, bool SourceDependent, PrimitiveType ReturnType,
            ImmutableArray<PrimitiveType> ParameterTypes)> Exports) EmitExperiment(
        BoundProgram program, string[] exports, NativeRuntimePolicy policy)
    {
        var method = ((global::System.Func<MethodInfo>)(() => {
            var __nunitActual5 = typeof(NativeEmitter).GetMethod("EmitSourceGrantLibraryForTest",
            BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(__nunitActual5, Is.InstanceOf<MethodInfo>());
            return (MethodInfo)__nunitActual5!;
        }))();
        try
        {
            return ((global::System.Func<(byte[], NativeCode,
                ImmutableArray<(string, bool, PrimitiveType, ImmutableArray<PrimitiveType>)>)>)(() => {
                    var __nunitActual6 = method.Invoke(null, [program, exports, policy]);
                    Assert.That(__nunitActual6, Is.TypeOf<(byte[], NativeCode,
                ImmutableArray<(string, bool, PrimitiveType, ImmutableArray<PrimitiveType>)>)>());
                    return ((byte[], NativeCode,
                ImmutableArray<(string, bool, PrimitiveType, ImmutableArray<PrimitiveType>)>))__nunitActual6!;
                }))();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is Exception inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }

    private static bool[] Classify(BoundProgram program)
    {
        var classifier = ((global::System.Func<Type>)(() => {
            var __nunitActual7 = typeof(NativeEmitter).Assembly.GetType(
            "Terrum.CodeAnalysis.Native.NativeSourceGrantExperiment");
            Assert.That(__nunitActual7, Is.InstanceOf<Type>());
            return (Type)__nunitActual7!;
        }))();
        var classify = ((global::System.Func<MethodInfo>)(() => {
            var __nunitActual8 = classifier.GetMethod("Classify",
            BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(__nunitActual8, Is.InstanceOf<MethodInfo>());
            return (MethodInfo)__nunitActual8!;
        }))();
        return ((global::System.Func<bool[]>)(() => { var __nunitActual9 = classify.Invoke(null, [program]); Assert.That(__nunitActual9, Is.TypeOf<bool[]>()); return (bool[])__nunitActual9!; }))();
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
