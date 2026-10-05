using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

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
    private delegate int TokenCopyOutputExport(
        int index, NativeToken first, NativeToken second, ref NativeToken result);

    [Fact]
    public void CompilerPrimitivesLibraryExecutesConcreteCopyAlgorithms()
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Compiler-primitives exports require Windows x64.");

        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing configuration directory.");
        var libraryPath = Path.Combine(RepositoryRoot(), "artifacts", "bin", "self-host", configuration,
            "terrum-compiler-primitives.dll");
        Assert.True(File.Exists(libraryPath), $"Compiler-primitives library was not built: {libraryPath}");

        Assert.Equal(12, Marshal.SizeOf<NativeToken>());
        Assert.Equal(0, Marshal.OffsetOf<NativeToken>(nameof(NativeToken.Kind)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<NativeToken>(nameof(NativeToken.Range)).ToInt32());
        Assert.Equal(8, Marshal.SizeOf<NativeSourceRange>());
        Assert.Equal(4, Marshal.OffsetOf<NativeSourceRange>(nameof(NativeSourceRange.Length)).ToInt32());
        Assert.Equal(0, Marshal.OffsetOf<GuardedNativeToken>(nameof(GuardedNativeToken.Before)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<GuardedNativeToken>(nameof(GuardedNativeToken.Value)).ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<GuardedNativeToken>(nameof(GuardedNativeToken.After)).ToInt32());
        Assert.Equal(28, Marshal.SizeOf<GuardedNativeToken>());

        var handle = NativeLibrary.Load(libraryPath);
        try
        {
            var construct = Marshal.GetDelegateForFunctionPointer<TokenConstructorExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Token.#ctor"));
            var tryCreateRange = Marshal.GetDelegateForFunctionPointer<SourceRangeTryCreateExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.SourceRange.TryCreate"));
            var select = Marshal.GetDelegateForFunctionPointer<TokenCopySelectorExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.SelectTokenCopyForAbiTest"));
            var selectInit = Marshal.GetDelegateForFunctionPointer<TokenCopyOutputExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.TrySelectTokenCopyInitForAbiTest"));
            var selectOut = Marshal.GetDelegateForFunctionPointer<TokenCopyOutputExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.TrySelectTokenCopyForAbiTest"));

            NativeToken CreateToken(int kind, int start, int length)
            {
                var range = default(NativeSourceRange);
                Assert.Equal(1, tryCreateRange(start, length, ref range));
                return construct(kind, range);
            }

            var first = CreateToken(-17, 2, 5);
            var second = CreateToken(42, int.MaxValue - 1, 1);
            var sentinel = CreateToken(123456789, 101, 202);
            var firstBefore = first;
            var secondBefore = second;
            AssertNativeToken(first, select(true, first, second), "SelectTokenCopyForAbiTest(true)");
            AssertNativeToken(second, select(false, first, second), "SelectTokenCopyForAbiTest(false)");

            const ulong BeforeCanary = 0x13579BDF2468ACE0UL;
            const ulong AfterCanary = 0x0ECA86425A5A5A5AUL;
            foreach (var (expected, index) in new[] { (first, 0), (second, 1) })
            {
                var guarded = new GuardedNativeToken
                {
                    Before = BeforeCanary,
                    Value = sentinel,
                    After = AfterCanary,
                };
                Assert.Equal(1, selectInit(index, first, second, ref guarded.Value));
                Assert.Equal(BeforeCanary, guarded.Before);
                AssertNativeToken(expected, guarded.Value, $"TrySelectTokenCopyInitForAbiTest({index})");
                Assert.Equal(AfterCanary, guarded.After);
            }

            foreach (var index in new[] { -1, 2, int.MinValue, int.MaxValue })
            {
                var guarded = new GuardedNativeToken
                {
                    Before = BeforeCanary,
                    Value = sentinel,
                    After = AfterCanary,
                };
                Assert.Equal(0, selectInit(index, first, second, ref guarded.Value));
                Assert.Equal(BeforeCanary, guarded.Before);
                AssertNativeToken(sentinel, guarded.Value,
                    $"TrySelectTokenCopyInitForAbiTest({index}) failure storage");
                Assert.Equal(AfterCanary, guarded.After);
            }

            foreach (var (expected, index) in new[] { (first, 0), (second, 1) })
            {
                var guarded = new GuardedNativeToken
                {
                    Before = BeforeCanary,
                    Value = sentinel,
                    After = AfterCanary,
                };
                Assert.Equal(1, selectOut(index, first, second, ref guarded.Value));
                Assert.Equal(BeforeCanary, guarded.Before);
                AssertNativeToken(expected, guarded.Value, $"TrySelectTokenCopyForAbiTest({index})");
                Assert.Equal(AfterCanary, guarded.After);
            }

            foreach (var index in new[] { -1, 2, int.MinValue, int.MaxValue })
            {
                var guarded = new GuardedNativeToken
                {
                    Before = BeforeCanary,
                    Value = sentinel,
                    After = AfterCanary,
                };
                Assert.Equal(0, selectOut(index, first, second, ref guarded.Value));
                Assert.Equal(BeforeCanary, guarded.Before);
                AssertNativeToken(sentinel, guarded.Value,
                    $"TrySelectTokenCopyForAbiTest({index}) failure storage");
                Assert.Equal(AfterCanary, guarded.After);
            }

            AssertNativeToken(firstBefore, first, "first input");
            AssertNativeToken(secondBefore, second, "second input");
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }
}
