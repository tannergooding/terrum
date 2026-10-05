// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root.

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Terrum.CodeAnalysis.Native;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Int32ToInt64SaturationExport(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Int32ToUInt64SaturationExport(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long UInt32ToInt64SaturationExport(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong UInt32ToUInt64SaturationExport(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt32ToInt32SaturationExport(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Int32ToUInt32SaturationExport(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int64ToInt32SaturationExport(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Int64ToUInt32SaturationExport(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt64ToInt32SaturationExport(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint UInt64ToUInt32SaturationExport(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long UInt64ToInt64SaturationExport(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Int64ToUInt64SaturationExport(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort Int32ToChar16SaturationExport(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort UInt32ToChar16SaturationExport(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort Int64ToChar16SaturationExport(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort UInt64ToChar16SaturationExport(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Char16ToInt32SaturationExport(ushort value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Char16ToUInt32SaturationExport(ushort value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Char16ToInt64SaturationExport(ushort value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Char16ToUInt64SaturationExport(ushort value);

    private static void AssertIntegerSaturationExports(nint handle)
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
        ushort[] char16Values = [0, 1, 0x7FFF, 0x8000, 0xD800, 0xDC00, ushort.MaxValue];

        var int32ToInt64 = GetSaturationExport<Int32ToInt64SaturationExport>(
            handle, ExportPrefix, "SaturateInt32ToInt64");
        var int32ToUInt64 = GetSaturationExport<Int32ToUInt64SaturationExport>(
            handle, ExportPrefix, "SaturateInt32ToUInt64");
        var uint32ToInt64 = GetSaturationExport<UInt32ToInt64SaturationExport>(
            handle, ExportPrefix, "SaturateUInt32ToInt64");
        var uint32ToUInt64 = GetSaturationExport<UInt32ToUInt64SaturationExport>(
            handle, ExportPrefix, "SaturateUInt32ToUInt64");
        var uint32ToInt32 = GetSaturationExport<UInt32ToInt32SaturationExport>(
            handle, ExportPrefix, "SaturateUInt32ToInt32");
        var int32ToUInt32 = GetSaturationExport<Int32ToUInt32SaturationExport>(
            handle, ExportPrefix, "SaturateInt32ToUInt32");
        var int64ToInt32 = GetSaturationExport<Int64ToInt32SaturationExport>(
            handle, ExportPrefix, "SaturateInt64ToInt32");
        var int64ToUInt32 = GetSaturationExport<Int64ToUInt32SaturationExport>(
            handle, ExportPrefix, "SaturateInt64ToUInt32");
        var uint64ToInt32 = GetSaturationExport<UInt64ToInt32SaturationExport>(
            handle, ExportPrefix, "SaturateUInt64ToInt32");
        var uint64ToUInt32 = GetSaturationExport<UInt64ToUInt32SaturationExport>(
            handle, ExportPrefix, "SaturateUInt64ToUInt32");
        var uint64ToInt64 = GetSaturationExport<UInt64ToInt64SaturationExport>(
            handle, ExportPrefix, "SaturateUInt64ToInt64");
        var int64ToUInt64 = GetSaturationExport<Int64ToUInt64SaturationExport>(
            handle, ExportPrefix, "SaturateInt64ToUInt64");
        var int32ToChar16 = GetSaturationExport<Int32ToChar16SaturationExport>(
            handle, ExportPrefix, "SaturateInt32ToChar16");
        var uint32ToChar16 = GetSaturationExport<UInt32ToChar16SaturationExport>(
            handle, ExportPrefix, "SaturateUInt32ToChar16");
        var int64ToChar16 = GetSaturationExport<Int64ToChar16SaturationExport>(
            handle, ExportPrefix, "SaturateInt64ToChar16");
        var uint64ToChar16 = GetSaturationExport<UInt64ToChar16SaturationExport>(
            handle, ExportPrefix, "SaturateUInt64ToChar16");
        var char16ToInt32 = GetSaturationExport<Char16ToInt32SaturationExport>(
            handle, ExportPrefix, "SaturateChar16ToInt32");
        var char16ToUInt32 = GetSaturationExport<Char16ToUInt32SaturationExport>(
            handle, ExportPrefix, "SaturateChar16ToUInt32");
        var char16ToInt64 = GetSaturationExport<Char16ToInt64SaturationExport>(
            handle, ExportPrefix, "SaturateChar16ToInt64");
        var char16ToUInt64 = GetSaturationExport<Char16ToUInt64SaturationExport>(
            handle, ExportPrefix, "SaturateChar16ToUInt64");

        AssertSaturation<int, long>("SaturateInt32ToInt64", signed32Values, static value => new BigInteger(value),
            signed64Minimum, signed64Maximum, static value => new BigInteger(value), value => int32ToInt64(value));
        AssertSaturation<int, ulong>("SaturateInt32ToUInt64", signed32Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned64Maximum, static value => new BigInteger(value), value => int32ToUInt64(value));
        AssertSaturation<uint, long>("SaturateUInt32ToInt64", unsigned32Values, static value => new BigInteger(value),
            signed64Minimum, signed64Maximum, static value => new BigInteger(value), value => uint32ToInt64(value));
        AssertSaturation<uint, ulong>("SaturateUInt32ToUInt64", unsigned32Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned64Maximum, static value => new BigInteger(value), value => uint32ToUInt64(value));
        AssertSaturation<uint, int>("SaturateUInt32ToInt32", unsigned32Values, static value => new BigInteger(value),
            signed32Minimum, signed32Maximum, static value => new BigInteger(value), value => uint32ToInt32(value));
        AssertSaturation<int, uint>("SaturateInt32ToUInt32", signed32Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned32Maximum, static value => new BigInteger(value), value => int32ToUInt32(value));
        AssertSaturation<long, int>("SaturateInt64ToInt32", signed64Values, static value => new BigInteger(value),
            signed32Minimum, signed32Maximum, static value => new BigInteger(value), value => int64ToInt32(value));
        AssertSaturation<long, uint>("SaturateInt64ToUInt32", signed64Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned32Maximum, static value => new BigInteger(value), value => int64ToUInt32(value));
        AssertSaturation<ulong, int>("SaturateUInt64ToInt32", unsigned64Values, static value => new BigInteger(value),
            signed32Minimum, signed32Maximum, static value => new BigInteger(value), value => uint64ToInt32(value));
        AssertSaturation<ulong, uint>("SaturateUInt64ToUInt32", unsigned64Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned32Maximum, static value => new BigInteger(value), value => uint64ToUInt32(value));
        AssertSaturation<ulong, long>("SaturateUInt64ToInt64", unsigned64Values, static value => new BigInteger(value),
            signed64Minimum, signed64Maximum, static value => new BigInteger(value), value => uint64ToInt64(value));
        AssertSaturation<long, ulong>("SaturateInt64ToUInt64", signed64Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned64Maximum, static value => new BigInteger(value), value => int64ToUInt64(value));
        AssertSaturation<int, ushort>("SaturateInt32ToChar16", signed32Values, static value => new BigInteger(value),
            BigInteger.Zero, char16Maximum, static value => new BigInteger(value), value => int32ToChar16(value));
        AssertSaturation<uint, ushort>("SaturateUInt32ToChar16", unsigned32Values, static value => new BigInteger(value),
            BigInteger.Zero, char16Maximum, static value => new BigInteger(value), value => uint32ToChar16(value));
        AssertSaturation<long, ushort>("SaturateInt64ToChar16", signed64Values, static value => new BigInteger(value),
            BigInteger.Zero, char16Maximum, static value => new BigInteger(value), value => int64ToChar16(value));
        AssertSaturation<ulong, ushort>("SaturateUInt64ToChar16", unsigned64Values, static value => new BigInteger(value),
            BigInteger.Zero, char16Maximum, static value => new BigInteger(value), value => uint64ToChar16(value));
        AssertSaturation<ushort, int>("SaturateChar16ToInt32", char16Values, static value => new BigInteger(value),
            signed32Minimum, signed32Maximum, static value => new BigInteger(value), value => char16ToInt32(value));
        AssertSaturation<ushort, uint>("SaturateChar16ToUInt32", char16Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned32Maximum, static value => new BigInteger(value), value => char16ToUInt32(value));
        AssertSaturation<ushort, long>("SaturateChar16ToInt64", char16Values, static value => new BigInteger(value),
            signed64Minimum, signed64Maximum, static value => new BigInteger(value), value => char16ToInt64(value));
        AssertSaturation<ushort, ulong>("SaturateChar16ToUInt64", char16Values, static value => new BigInteger(value),
            BigInteger.Zero, unsigned64Maximum, static value => new BigInteger(value), value => char16ToUInt64(value));
    }

    private static void AssertSaturation<TSource, TResult>(
        string name,
        TSource[] inputs,
        Func<TSource, BigInteger> sourceValue,
        BigInteger destinationMinimum,
        BigInteger destinationMaximum,
        Func<TResult, BigInteger> resultValue,
        Func<TSource, TResult> invoke)
    {
        foreach (var input in inputs)
        {
            var source = sourceValue(input);
            var expected = source < destinationMinimum
                ? destinationMinimum
                : source > destinationMaximum
                    ? destinationMaximum
                    : source;
            var actual = resultValue(invoke(input));
            Assert.True(actual == expected, $"{name} returned {actual} for input {input}; expected {expected}.");
        }
    }

    private static TDelegate GetSaturationExport<TDelegate>(nint handle, string prefix, string name)
        where TDelegate : Delegate =>
        Marshal.GetDelegateForFunctionPointer<TDelegate>(
            NativeLibrary.GetExport(handle, prefix + name));
}
