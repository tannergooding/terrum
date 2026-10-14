using System;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct GuardedNativeToken
    {
        public ulong Before;
        public NativeToken Value;
        public ulong After;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeToken TokenCopySelectorExport(
        [MarshalAs(UnmanagedType.I1)] bool takeFirst, NativeToken first, NativeToken second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeSourceRange RangeCopySelectorExport(
        [MarshalAs(UnmanagedType.I1)] bool takeFirst, NativeSourceRange first, NativeSourceRange second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TokenCopyOutputExport(
        int index, NativeToken first, NativeToken second, ref NativeToken result);

    [Test]
    public void CompilerPrimitivesLibraryExecutesConcreteCopyAlgorithms()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True, "Compiler-primitives exports require Windows x64.");

        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing configuration directory.");
        var libraryPath = Path.Combine(RepositoryRoot(), "artifacts", "bin", "self-host", configuration,
            "terrum-compiler-primitives.dll");
        Assert.That(File.Exists(libraryPath), Is.True, $"Compiler-primitives library was not built: {libraryPath}");

        Assert.That(Marshal.SizeOf<NativeToken>(), Is.EqualTo(12));
        Assert.That(Marshal.OffsetOf<NativeToken>(nameof(NativeToken.Kind)).ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<NativeToken>(nameof(NativeToken.Range)).ToInt32(), Is.EqualTo(4));
        Assert.That(Marshal.SizeOf<NativeSourceRange>(), Is.EqualTo(8));
        Assert.That(Marshal.OffsetOf<NativeSourceRange>(nameof(NativeSourceRange.Length)).ToInt32(), Is.EqualTo(4));
        Assert.That(Marshal.OffsetOf<GuardedNativeToken>(nameof(GuardedNativeToken.Before)).ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<GuardedNativeToken>(nameof(GuardedNativeToken.Value)).ToInt32(), Is.EqualTo(8));
        Assert.That(Marshal.OffsetOf<GuardedNativeToken>(nameof(GuardedNativeToken.After)).ToInt32(), Is.EqualTo(20));
        Assert.That(Marshal.SizeOf<GuardedNativeToken>(), Is.EqualTo(28));

        var handle = NativeLibrary.Load(libraryPath);
        try
        {
            var construct = Marshal.GetDelegateForFunctionPointer<TokenConstructorExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Token.#ctor"));
            var tryCreateRange = Marshal.GetDelegateForFunctionPointer<SourceRangeTryCreateExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.SourceRange.TryCreate"));
            var select = Marshal.GetDelegateForFunctionPointer<TokenCopySelectorExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.SelectTokenCopyForAbiTest"));
            var selectRange = Marshal.GetDelegateForFunctionPointer<RangeCopySelectorExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.SelectRangeCopyForAbiTest"));
            var selectInit = Marshal.GetDelegateForFunctionPointer<TokenCopyOutputExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.TrySelectTokenCopyInitForAbiTest"));
            var selectOut = Marshal.GetDelegateForFunctionPointer<TokenCopyOutputExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.TrySelectTokenCopyForAbiTest"));

            NativeToken CreateToken(int kind, int start, int length)
            {
                var range = default(NativeSourceRange);
                Assert.That(tryCreateRange(start, length, ref range), Is.EqualTo(1));
                return construct(kind, range);
            }

            var first = CreateToken(-17, 2, 5);
            var second = CreateToken(42, int.MaxValue - 1, 1);
            var sentinel = CreateToken(123456789, 101, 202);
            var firstBefore = first;
            var secondBefore = second;
            var firstRange = new NativeSourceRange { Start = 2, Length = 5 };
            var secondRange = new NativeSourceRange { Start = int.MaxValue - 1, Length = 1 };
            var firstRangeBefore = firstRange;
            var secondRangeBefore = secondRange;
            AssertNativeToken(first, select(true, first, second), "SelectTokenCopyForAbiTest(true)");
            AssertNativeToken(second, select(false, first, second), "SelectTokenCopyForAbiTest(false)");
            AssertNativeSourceRange(firstRange, selectRange(true, firstRange, secondRange),
                "SelectRangeCopyForAbiTest(true)");
            AssertNativeSourceRange(secondRange, selectRange(false, firstRange, secondRange),
                "SelectRangeCopyForAbiTest(false)");

            const ulong BeforeCanary = 0x13579BDF2468ACE0UL;
            const ulong AfterCanary = 0x0ECA86425A5A5A5AUL;
            foreach (var (expected, index) in new[] { (first, 0), (second, 1) })
            {
                var guarded = new GuardedNativeToken {
                    Before = BeforeCanary,
                    Value = sentinel,
                    After = AfterCanary,
                };
                Assert.That(selectInit(index, first, second, ref guarded.Value), Is.EqualTo(1));
                Assert.That(guarded.Before, Is.EqualTo(BeforeCanary));
                AssertNativeToken(expected, guarded.Value, $"TrySelectTokenCopyInitForAbiTest({index})");
                Assert.That(guarded.After, Is.EqualTo(AfterCanary));
            }

            foreach (var index in new[] { -1, 2, int.MinValue, int.MaxValue })
            {
                var guarded = new GuardedNativeToken {
                    Before = BeforeCanary,
                    Value = sentinel,
                    After = AfterCanary,
                };
                Assert.That(selectInit(index, first, second, ref guarded.Value), Is.EqualTo(0));
                Assert.That(guarded.Before, Is.EqualTo(BeforeCanary));
                AssertNativeToken(sentinel, guarded.Value,
                    $"TrySelectTokenCopyInitForAbiTest({index}) failure storage");
                Assert.That(guarded.After, Is.EqualTo(AfterCanary));
            }

            foreach (var (expected, index) in new[] { (first, 0), (second, 1) })
            {
                var guarded = new GuardedNativeToken {
                    Before = BeforeCanary,
                    Value = sentinel,
                    After = AfterCanary,
                };
                Assert.That(selectOut(index, first, second, ref guarded.Value), Is.EqualTo(1));
                Assert.That(guarded.Before, Is.EqualTo(BeforeCanary));
                AssertNativeToken(expected, guarded.Value, $"TrySelectTokenCopyForAbiTest({index})");
                Assert.That(guarded.After, Is.EqualTo(AfterCanary));
            }

            foreach (var index in new[] { -1, 2, int.MinValue, int.MaxValue })
            {
                var guarded = new GuardedNativeToken {
                    Before = BeforeCanary,
                    Value = sentinel,
                    After = AfterCanary,
                };
                Assert.That(selectOut(index, first, second, ref guarded.Value), Is.EqualTo(0));
                Assert.That(guarded.Before, Is.EqualTo(BeforeCanary));
                AssertNativeToken(sentinel, guarded.Value,
                    $"TrySelectTokenCopyForAbiTest({index}) failure storage");
                Assert.That(guarded.After, Is.EqualTo(AfterCanary));
            }

            AssertNativeToken(firstBefore, first, "first input");
            AssertNativeToken(secondBefore, second, "second input");
            AssertNativeSourceRange(firstRangeBefore, firstRange, "first range input");
            AssertNativeSourceRange(secondRangeBefore, secondRange, "second range input");
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }

    private static void AssertNativeSourceRange(
        NativeSourceRange expected, NativeSourceRange actual, string operation)
    {
        Assert.That(expected.Start == actual.Start, Is.True, $"{operation}: expected start {expected.Start}, got {actual.Start}.");
        Assert.That(expected.Length == actual.Length, Is.True, $"{operation}: expected length {expected.Length}, got {actual.Length}.");
    }
}
