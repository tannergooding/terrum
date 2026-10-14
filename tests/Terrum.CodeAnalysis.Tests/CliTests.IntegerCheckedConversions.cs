// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GuardedConversionResult<T>
    {
        public T Before;
        public T Value;
        public T After;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32ToInt64ConversionExport(int value, ref long result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32ToUInt64ConversionExport(int value, ref ulong result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt32ToInt64ConversionExport(uint value, ref long result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt32ToUInt64ConversionExport(uint value, ref ulong result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt32ToInt32ConversionExport(uint value, ref int result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32ToUInt32ConversionExport(int value, ref uint result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int64ToInt32ConversionExport(long value, ref int result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int64ToUInt32ConversionExport(long value, ref uint result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt64ToInt32ConversionExport(ulong value, ref int result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt64ToUInt32ConversionExport(ulong value, ref uint result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt64ToInt64ConversionExport(ulong value, ref long result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int64ToUInt64ConversionExport(long value, ref ulong result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32ToChar16ConversionExport(int value, ref ushort result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt32ToChar16ConversionExport(uint value, ref ushort result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int64ToChar16ConversionExport(long value, ref ushort result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt64ToChar16ConversionExport(ulong value, ref ushort result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Char16ToInt32ConversionExport(ushort value, ref int result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Char16ToUInt32ConversionExport(ushort value, ref uint result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Char16ToInt64ConversionExport(ushort value, ref long result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Char16ToUInt64ConversionExport(ushort value, ref ulong result);

    private delegate int CheckedConversionInvoker<TSource, TResult>(TSource value, ref TResult result);

    private static void AssertIntegerCheckedConversionExports(nint handle)
    {
        const string ExportPrefix = "Terrum.CodeAnalysis.Numerics64.";

        var signed32Minimum = new BigInteger(int.MinValue);
        var signed32Maximum = new BigInteger(int.MaxValue);
        var unsigned32Maximum = new BigInteger(uint.MaxValue);
        var signed64Minimum = new BigInteger(long.MinValue);
        var signed64Maximum = new BigInteger(long.MaxValue);
        var unsigned64Maximum = new BigInteger(ulong.MaxValue);
        var char16Maximum = new BigInteger(ushort.MaxValue);
        int[] signed32Values = [
            int.MinValue, int.MinValue + 1, -65537, -65536, -65535, -1, 0, 1,
            65534, 65535, 65536, 65537, int.MaxValue - 1, int.MaxValue,
        ];
        uint[] unsigned32Values = [
            0, 1, 65534, 65535, 65536, 65537, int.MaxValue, 0x80000000U,
            uint.MaxValue - 1, uint.MaxValue,
        ];
        long[] signed64Values = [
            long.MinValue, long.MinValue + 1, int.MinValue - 1L, int.MinValue,
            int.MinValue + 1L, -1, 0, 1, 65535, 65536, 2147483647L, 2147483648L,
            4294967295L, 4294967296L, long.MaxValue - 1, long.MaxValue,
        ];
        ulong[] unsigned64Values = [
            0, 1, 65535, 65536, 65537, int.MaxValue, 0x80000000UL,
            uint.MaxValue, (ulong)uint.MaxValue + 1, long.MaxValue,
            (ulong)long.MaxValue + 1, ulong.MaxValue - 1, ulong.MaxValue,
        ];
        ushort[] char16Values = new ushort[ushort.MaxValue + 1];
        for (var index = 0; index < char16Values.Length; index++)
        {
            char16Values[index] = (ushort)index;
        }

        var int32ToInt64 = GetConversionExport<Int32ToInt64ConversionExport>(
            handle, ExportPrefix, "TryConvertInt32ToInt64");
        var int32ToUInt64 = GetConversionExport<Int32ToUInt64ConversionExport>(
            handle, ExportPrefix, "TryConvertInt32ToUInt64");
        var uint32ToInt64 = GetConversionExport<UInt32ToInt64ConversionExport>(
            handle, ExportPrefix, "TryConvertUInt32ToInt64");
        var uint32ToUInt64 = GetConversionExport<UInt32ToUInt64ConversionExport>(
            handle, ExportPrefix, "TryConvertUInt32ToUInt64");
        var uint32ToInt32 = GetConversionExport<UInt32ToInt32ConversionExport>(
            handle, ExportPrefix, "TryConvertUInt32ToInt32");
        var int32ToUInt32 = GetConversionExport<Int32ToUInt32ConversionExport>(
            handle, ExportPrefix, "TryConvertInt32ToUInt32");
        var int64ToInt32 = GetConversionExport<Int64ToInt32ConversionExport>(
            handle, ExportPrefix, "TryConvertInt64ToInt32");
        var int64ToUInt32 = GetConversionExport<Int64ToUInt32ConversionExport>(
            handle, ExportPrefix, "TryConvertInt64ToUInt32");
        var uint64ToInt32 = GetConversionExport<UInt64ToInt32ConversionExport>(
            handle, ExportPrefix, "TryConvertUInt64ToInt32");
        var uint64ToUInt32 = GetConversionExport<UInt64ToUInt32ConversionExport>(
            handle, ExportPrefix, "TryConvertUInt64ToUInt32");
        var uint64ToInt64 = GetConversionExport<UInt64ToInt64ConversionExport>(
            handle, ExportPrefix, "TryConvertUInt64ToInt64");
        var int64ToUInt64 = GetConversionExport<Int64ToUInt64ConversionExport>(
            handle, ExportPrefix, "TryConvertInt64ToUInt64");
        var int32ToChar16 = GetConversionExport<Int32ToChar16ConversionExport>(
            handle, ExportPrefix, "TryConvertInt32ToChar16");
        var uint32ToChar16 = GetConversionExport<UInt32ToChar16ConversionExport>(
            handle, ExportPrefix, "TryConvertUInt32ToChar16");
        var int64ToChar16 = GetConversionExport<Int64ToChar16ConversionExport>(
            handle, ExportPrefix, "TryConvertInt64ToChar16");
        var uint64ToChar16 = GetConversionExport<UInt64ToChar16ConversionExport>(
            handle, ExportPrefix, "TryConvertUInt64ToChar16");
        var char16ToInt32 = GetConversionExport<Char16ToInt32ConversionExport>(
            handle, ExportPrefix, "TryConvertChar16ToInt32");
        var char16ToUInt32 = GetConversionExport<Char16ToUInt32ConversionExport>(
            handle, ExportPrefix, "TryConvertChar16ToUInt32");
        var char16ToInt64 = GetConversionExport<Char16ToInt64ConversionExport>(
            handle, ExportPrefix, "TryConvertChar16ToInt64");
        var char16ToUInt64 = GetConversionExport<Char16ToUInt64ConversionExport>(
            handle, ExportPrefix, "TryConvertChar16ToUInt64");

        AssertConversion<int, long>("TryConvertInt32ToInt64", signed32Values, static value => new BigInteger(value),
            signed64Minimum, signed64Maximum, static value => new BigInteger(value),
            unchecked((long)0xFEDCBA9876543210UL), 0x13579BDF2468ACE0L, 0x0ECA86425A5A5A5AL,
            (int value, ref long result) => int32ToInt64(value, ref result));
        AssertConversion<int, ulong>("TryConvertInt32ToUInt64", signed32Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned64Maximum, static value => new BigInteger(value),
            0xFEDCBA9876543210UL, 0x13579BDF2468ACE0UL, 0x0ECA86425A5A5A5AUL,
            (int value, ref ulong result) => int32ToUInt64(value, ref result));
        AssertConversion<uint, long>("TryConvertUInt32ToInt64", unsigned32Values, static value => new BigInteger(value),
            signed64Minimum, signed64Maximum, static value => new BigInteger(value),
            unchecked((long)0xFEDCBA9876543210UL), 0x13579BDF2468ACE0L, 0x0ECA86425A5A5A5AL,
            (uint value, ref long result) => uint32ToInt64(value, ref result));
        AssertConversion<uint, ulong>("TryConvertUInt32ToUInt64", unsigned32Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned64Maximum, static value => new BigInteger(value),
            0xFEDCBA9876543210UL, 0x13579BDF2468ACE0UL, 0x0ECA86425A5A5A5AUL,
            (uint value, ref ulong result) => uint32ToUInt64(value, ref result));
        AssertConversion<uint, int>("TryConvertUInt32ToInt32", unsigned32Values, static value => new BigInteger(value),
            signed32Minimum, signed32Maximum, static value => new BigInteger(value),
            unchecked((int)0xFEDCBA98), 0x13579BDF, 0x0ECA8642,
            (uint value, ref int result) => uint32ToInt32(value, ref result));
        AssertConversion<int, uint>("TryConvertInt32ToUInt32", signed32Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned32Maximum, static value => new BigInteger(value),
            0xFEDCBA98U, 0x13579BDFU, 0x0ECA8642U,
            (int value, ref uint result) => int32ToUInt32(value, ref result));
        AssertConversion<long, int>("TryConvertInt64ToInt32", signed64Values, static value => new BigInteger(value),
            signed32Minimum, signed32Maximum, static value => new BigInteger(value),
            unchecked((int)0xFEDCBA98), 0x13579BDF, 0x0ECA8642,
            (long value, ref int result) => int64ToInt32(value, ref result));
        AssertConversion<long, uint>("TryConvertInt64ToUInt32", signed64Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned32Maximum, static value => new BigInteger(value),
            0xFEDCBA98U, 0x13579BDFU, 0x0ECA8642U,
            (long value, ref uint result) => int64ToUInt32(value, ref result));
        AssertConversion<ulong, int>("TryConvertUInt64ToInt32", unsigned64Values, static value => new BigInteger(value),
            signed32Minimum, signed32Maximum, static value => new BigInteger(value),
            unchecked((int)0xFEDCBA98), 0x13579BDF, 0x0ECA8642,
            (ulong value, ref int result) => uint64ToInt32(value, ref result));
        AssertConversion<ulong, uint>("TryConvertUInt64ToUInt32", unsigned64Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned32Maximum, static value => new BigInteger(value),
            0xFEDCBA98U, 0x13579BDFU, 0x0ECA8642U,
            (ulong value, ref uint result) => uint64ToUInt32(value, ref result));
        AssertConversion<ulong, long>("TryConvertUInt64ToInt64", unsigned64Values, static value => new BigInteger(value),
            signed64Minimum, signed64Maximum, static value => new BigInteger(value),
            unchecked((long)0xFEDCBA9876543210UL), 0x13579BDF2468ACE0L, 0x0ECA86425A5A5A5AL,
            (ulong value, ref long result) => uint64ToInt64(value, ref result));
        AssertConversion<long, ulong>("TryConvertInt64ToUInt64", signed64Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned64Maximum, static value => new BigInteger(value),
            0xFEDCBA9876543210UL, 0x13579BDF2468ACE0UL, 0x0ECA86425A5A5A5AUL,
            (long value, ref ulong result) => int64ToUInt64(value, ref result));
        AssertConversion<int, ushort>("TryConvertInt32ToChar16", signed32Values, static value => new BigInteger(value),
            BigInteger.Zero, char16Maximum, static value => new BigInteger(value),
            (ushort)0xFEDC, (ushort)0x1357, (ushort)0x0ECA,
            (int value, ref ushort result) => int32ToChar16(value, ref result));
        AssertConversion<uint, ushort>("TryConvertUInt32ToChar16", unsigned32Values, static value => new BigInteger(value),
            BigInteger.Zero, char16Maximum, static value => new BigInteger(value),
            (ushort)0xFEDC, (ushort)0x1357, (ushort)0x0ECA,
            (uint value, ref ushort result) => uint32ToChar16(value, ref result));
        AssertConversion<long, ushort>("TryConvertInt64ToChar16", signed64Values, static value => new BigInteger(value),
            BigInteger.Zero, char16Maximum, static value => new BigInteger(value),
            (ushort)0xFEDC, (ushort)0x1357, (ushort)0x0ECA,
            (long value, ref ushort result) => int64ToChar16(value, ref result));
        AssertConversion<ulong, ushort>("TryConvertUInt64ToChar16", unsigned64Values, static value => new BigInteger(value),
            BigInteger.Zero, char16Maximum, static value => new BigInteger(value),
            (ushort)0xFEDC, (ushort)0x1357, (ushort)0x0ECA,
            (ulong value, ref ushort result) => uint64ToChar16(value, ref result));
        AssertConversion<ushort, int>("TryConvertChar16ToInt32", char16Values, static value => new BigInteger(value),
            signed32Minimum, signed32Maximum, static value => new BigInteger(value),
            unchecked((int)0xFEDCBA98), 0x13579BDF, 0x0ECA8642,
            (ushort value, ref int result) => char16ToInt32(value, ref result));
        AssertConversion<ushort, uint>("TryConvertChar16ToUInt32", char16Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned32Maximum, static value => new BigInteger(value),
            0xFEDCBA98U, 0x13579BDFU, 0x0ECA8642U,
            (ushort value, ref uint result) => char16ToUInt32(value, ref result));
        AssertConversion<ushort, long>("TryConvertChar16ToInt64", char16Values, static value => new BigInteger(value),
            signed64Minimum, signed64Maximum, static value => new BigInteger(value),
            unchecked((long)0xFEDCBA9876543210UL), 0x13579BDF2468ACE0L, 0x0ECA86425A5A5A5AL,
            (ushort value, ref long result) => char16ToInt64(value, ref result));
        AssertConversion<ushort, ulong>("TryConvertChar16ToUInt64", char16Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned64Maximum, static value => new BigInteger(value),
            0xFEDCBA9876543210UL, 0x13579BDF2468ACE0UL, 0x0ECA86425A5A5A5AUL,
            (ushort value, ref ulong result) => char16ToUInt64(value, ref result));
    }

    private static void AssertConversion<TSource, TResult>(
        string name,
        TSource[] inputs,
        Func<TSource, BigInteger> sourceValue,
        BigInteger destinationMinimum,
        BigInteger destinationMaximum,
        Func<TResult, BigInteger> resultValue,
        TResult sentinel,
        TResult lowerCanary,
        TResult upperCanary,
        CheckedConversionInvoker<TSource, TResult> invoke)
    {
        foreach (var input in inputs)
        {
            var source = sourceValue(input);
            var expectedSuccess = source >= destinationMinimum && source <= destinationMaximum;
            var guarded = new GuardedConversionResult<TResult> {
                Before = lowerCanary,
                Value = sentinel,
                After = upperCanary,
            };
            var status = invoke(input, ref guarded.Value);
            Assert.That(status == (expectedSuccess ? 1 : 0), Is.True, $"{name} returned status {status} for input {input}.");
            Assert.That(resultValue(guarded.Value) == (expectedSuccess ? source : resultValue(sentinel)), Is.True, $"{name} returned an unexpected output for input {input}.");
            if (!expectedSuccess)
            {
                Assert.That(guarded.Value, Is.EqualTo(sentinel));
            }
            Assert.That(EqualityComparer<TResult>.Default.Equals(lowerCanary, guarded.Before), Is.True, $"{name} changed the lower canary for input {input}.");
            Assert.That(EqualityComparer<TResult>.Default.Equals(upperCanary, guarded.After), Is.True, $"{name} changed the upper canary for input {input}.");
        }
    }

    private static TDelegate GetConversionExport<TDelegate>(nint handle, string prefix, string name)
        where TDelegate : Delegate =>
        Marshal.GetDelegateForFunctionPointer<TDelegate>(
            NativeLibrary.GetExport(handle, prefix + name));
}
