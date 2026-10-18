// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt64BaseLengthExport(ulong value, uint radix, ref int length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int64BaseLengthExport(long value, uint radix, ref int length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt32BaseLengthExport(uint value, uint radix, ref int length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32BaseLengthExport(int value, uint radix, ref int length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Char16BaseLengthExport(ushort value, uint radix, ref int length);

    private delegate int BaseLengthQuery(ref int length);

    private static void AssertIntegerFormattingLengthExports(IntPtr handle)
    {
        var uint64Length = Marshal.GetDelegateForFunctionPointer<UInt64BaseLengthExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.TryGetUInt64BaseLength"));
        var int64Length = Marshal.GetDelegateForFunctionPointer<Int64BaseLengthExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.TryGetInt64BaseLength"));
        var uint32Length = Marshal.GetDelegateForFunctionPointer<UInt32BaseLengthExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.TryGetUInt32BaseLength"));
        var int32Length = Marshal.GetDelegateForFunctionPointer<Int32BaseLengthExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.TryGetInt32BaseLength"));
        var char16Length = Marshal.GetDelegateForFunctionPointer<Char16BaseLengthExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.TryGetChar16BaseLength"));

        ulong[] unsigned64Values = [0, 1, 9, 10, 35, 36, ulong.MaxValue];
        long[] signed64Values = [long.MinValue, long.MinValue + 1, -123, -10, -1, 0, 1, 10, long.MaxValue];
        uint[] unsigned32Values = [0, 1, 9, 10, 35, 36, 0xFFFF, uint.MaxValue];
        int[] signed32Values = [int.MinValue, int.MinValue + 1, -123, -10, -1, 0, 1, 10, int.MaxValue];
        ushort[] char16Values = [0, 9, 10, 35, 36, 0xD800, 0xDFFF, 0xFFFF];

        for (uint radix = 2; radix <= 36; radix++)
        {
            foreach (var value in unsigned64Values)
            {
                var expected = ExpectedBaseLength(new BigInteger(value), radix);
                AssertBaseLength((ref int length) => uint64Length(value, radix, ref length), expected);
            }
            foreach (var value in signed64Values)
            {
                var expected = ExpectedBaseLength(new BigInteger(value), radix);
                AssertBaseLength((ref int length) => int64Length(value, radix, ref length), expected);
            }
            foreach (var value in unsigned32Values)
            {
                var expected = ExpectedBaseLength(new BigInteger(value), radix);
                AssertBaseLength((ref int length) => uint32Length(value, radix, ref length), expected);
            }
            foreach (var value in signed32Values)
            {
                var expected = ExpectedBaseLength(new BigInteger(value), radix);
                AssertBaseLength((ref int length) => int32Length(value, radix, ref length), expected);
            }
            foreach (var value in char16Values)
            {
                var expected = ExpectedBaseLength(new BigInteger(value), radix);
                AssertBaseLength((ref int length) => char16Length(value, radix, ref length), expected);
            }
        }

        foreach (var radix in new uint[] { 0, 1, 37, uint.MaxValue })
        {
            AssertBaseLength((ref int length) => uint64Length(ulong.MaxValue, radix, ref length), expectedLength: null);
            AssertBaseLength((ref int length) => int64Length(long.MinValue, radix, ref length), expectedLength: null);
            AssertBaseLength((ref int length) => uint32Length(uint.MaxValue, radix, ref length), expectedLength: null);
            AssertBaseLength((ref int length) => int32Length(int.MinValue, radix, ref length), expectedLength: null);
            AssertBaseLength((ref int length) => char16Length(0xFFFF, radix, ref length), expectedLength: null);
        }
    }

    private static int ExpectedBaseLength(BigInteger value, uint radix)
    {
        var remaining = BigInteger.Abs(value);
        var length = 1;
        while (remaining >= radix)
        {
            remaining /= radix;
            length++;
        }

        return value.Sign < 0 ? length + 1 : length;
    }

    private static void AssertBaseLength(BaseLengthQuery query, int? expectedLength)
    {
        const int before = 0x13579BDF;
        const int after = 0x2468ACE0;
        const int sentinel = 0x76543210;
        var guarded = new GuardedInt32 {
            Before = before,
            Value = sentinel,
            After = after,
        };

        var status = query(ref guarded.Value);

        Assert.That(status, Is.EqualTo(expectedLength.HasValue ? 1 : 0));
        Assert.That(guarded.Value, Is.EqualTo(expectedLength ?? sentinel));
        Assert.That(guarded.Before, Is.EqualTo(before));
        Assert.That(guarded.After, Is.EqualTo(after));
    }
}
