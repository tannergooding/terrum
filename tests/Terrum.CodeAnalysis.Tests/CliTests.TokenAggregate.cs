// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeToken
    {
        public int Kind;
        public NativeSourceRange Range;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeToken TokenConstructorExport(int kind, NativeSourceRange range);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeToken TokenSelectorExport(
        NativeToken first,
        NativeToken second,
        NativeToken third,
        NativeToken fourth,
        int selector);

    private static void AssertTokenAggregateExports(IntPtr handle)
    {
        Assert.Equal(8, Marshal.SizeOf<NativeSourceRange>());
        Assert.Equal(12, Marshal.SizeOf<NativeToken>());
        Assert.Equal(0, Marshal.OffsetOf<NativeToken>(nameof(NativeToken.Kind)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<NativeToken>(nameof(NativeToken.Range)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<NativeSourceRange>(nameof(NativeSourceRange.Length)).ToInt32());
        Assert.Equal(8,
            Marshal.OffsetOf<NativeToken>(nameof(NativeToken.Range)).ToInt32()
            + Marshal.OffsetOf<NativeSourceRange>(nameof(NativeSourceRange.Length)).ToInt32());

        var tryCreateRange = Marshal.GetDelegateForFunctionPointer<SourceRangeTryCreateExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.SourceRange.TryCreate"));
        var construct = Marshal.GetDelegateForFunctionPointer<TokenConstructorExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Token.#ctor"));
        var select = Marshal.GetDelegateForFunctionPointer<TokenSelectorExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.SelectTokenForAbiTest"));

        NativeToken CreateToken(int kind, int start, int length)
        {
            var range = default(NativeSourceRange);
            Assert.Equal(1, tryCreateRange(start, length, ref range));
            return construct(kind, range);
        }

        foreach (var (kind, start, length) in new (int Kind, int Start, int Length)[] {
            (int.MinValue, 0, 0),
            (7, 2, 3),
            (int.MaxValue, int.MaxValue, 0),
            (-123456789, int.MaxValue - 1, 1),
        })
        {
            var expected = new NativeToken
            {
                Kind = kind,
                Range = new NativeSourceRange { Start = start, Length = length },
            };
            var actual = CreateToken(kind, start, length);
            AssertNativeToken(expected, actual, "Token.#ctor");
        }

        var first = CreateToken(int.MinValue, 0, 1);
        var second = CreateToken(-17, 2, 5);
        var third = CreateToken(42, 123456789, 987654321);
        var fourth = CreateToken(int.MaxValue, int.MaxValue - 1, 1);
        var firstBefore = first;
        var secondBefore = second;
        var thirdBefore = third;
        var fourthBefore = fourth;
        foreach (var (expected, selector) in new[] {
            (first, 0),
            (second, 1),
            (third, 2),
            (fourth, 3),
        })
        {
            var actual = select(first, second, third, fourth, selector);
            AssertNativeToken(expected, actual, $"SelectTokenForAbiTest({selector})");
        }

        AssertNativeToken(firstBefore, first, "first caller value");
        AssertNativeToken(secondBefore, second, "second caller value");
        AssertNativeToken(thirdBefore, third, "third caller value");
        AssertNativeToken(fourthBefore, fourth, "fourth caller value");
    }

    private static void AssertNativeToken(NativeToken expected, NativeToken actual, string operation)
    {
        Assert.True(expected.Kind == actual.Kind,
            $"{operation}: expected kind {expected.Kind}, got {actual.Kind}.");
        Assert.True(expected.Range.Start == actual.Range.Start,
            $"{operation}: expected start {expected.Range.Start}, got {actual.Range.Start}.");
        Assert.True(expected.Range.Length == actual.Range.Length,
            $"{operation}: expected length {expected.Range.Length}, got {actual.Range.Length}.");
    }
}
