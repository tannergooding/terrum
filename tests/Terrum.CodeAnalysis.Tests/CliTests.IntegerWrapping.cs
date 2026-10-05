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
    private delegate long Int32ToInt64WrappingExport(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Int32ToUInt64WrappingExport(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long UInt32ToInt64WrappingExport(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong UInt32ToUInt64WrappingExport(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Int32ToUInt32WrappingExport(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt32ToInt32WrappingExport(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Int64ToUInt32WrappingExport(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int64ToInt32WrappingExport(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint UInt64ToUInt32WrappingExport(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt64ToInt32WrappingExport(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Int64ToUInt64WrappingExport(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long UInt64ToInt64WrappingExport(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort Int32ToChar16WrappingExport(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort UInt32ToChar16WrappingExport(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort Int64ToChar16WrappingExport(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort UInt64ToChar16WrappingExport(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Char16ToInt32WrappingExport(ushort value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Char16ToUInt32WrappingExport(ushort value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Char16ToInt64WrappingExport(ushort value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Char16ToUInt64WrappingExport(ushort value);

    private static void AssertIntegerWrappingExports(nint handle)
    {
        const string ExportPrefix = "Terrum.CodeAnalysis.Numerics64.";

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

        var int32ToInt64 = GetWrappingExport<Int32ToInt64WrappingExport>(
            handle, ExportPrefix, "WrapInt32ToInt64");
        var int32ToUInt64 = GetWrappingExport<Int32ToUInt64WrappingExport>(
            handle, ExportPrefix, "WrapInt32ToUInt64");
        var uint32ToInt64 = GetWrappingExport<UInt32ToInt64WrappingExport>(
            handle, ExportPrefix, "WrapUInt32ToInt64");
        var uint32ToUInt64 = GetWrappingExport<UInt32ToUInt64WrappingExport>(
            handle, ExportPrefix, "WrapUInt32ToUInt64");
        var int32ToUInt32 = GetWrappingExport<Int32ToUInt32WrappingExport>(
            handle, ExportPrefix, "WrapInt32ToUInt32");
        var uint32ToInt32 = GetWrappingExport<UInt32ToInt32WrappingExport>(
            handle, ExportPrefix, "WrapUInt32ToInt32");
        var int64ToUInt32 = GetWrappingExport<Int64ToUInt32WrappingExport>(
            handle, ExportPrefix, "WrapInt64ToUInt32");
        var int64ToInt32 = GetWrappingExport<Int64ToInt32WrappingExport>(
            handle, ExportPrefix, "WrapInt64ToInt32");
        var uint64ToUInt32 = GetWrappingExport<UInt64ToUInt32WrappingExport>(
            handle, ExportPrefix, "WrapUInt64ToUInt32");
        var uint64ToInt32 = GetWrappingExport<UInt64ToInt32WrappingExport>(
            handle, ExportPrefix, "WrapUInt64ToInt32");
        var int64ToUInt64 = GetWrappingExport<Int64ToUInt64WrappingExport>(
            handle, ExportPrefix, "WrapInt64ToUInt64");
        var uint64ToInt64 = GetWrappingExport<UInt64ToInt64WrappingExport>(
            handle, ExportPrefix, "WrapUInt64ToInt64");
        var int32ToChar16 = GetWrappingExport<Int32ToChar16WrappingExport>(
            handle, ExportPrefix, "WrapInt32ToChar16");
        var uint32ToChar16 = GetWrappingExport<UInt32ToChar16WrappingExport>(
            handle, ExportPrefix, "WrapUInt32ToChar16");
        var int64ToChar16 = GetWrappingExport<Int64ToChar16WrappingExport>(
            handle, ExportPrefix, "WrapInt64ToChar16");
        var uint64ToChar16 = GetWrappingExport<UInt64ToChar16WrappingExport>(
            handle, ExportPrefix, "WrapUInt64ToChar16");
        var char16ToInt32 = GetWrappingExport<Char16ToInt32WrappingExport>(
            handle, ExportPrefix, "WrapChar16ToInt32");
        var char16ToUInt32 = GetWrappingExport<Char16ToUInt32WrappingExport>(
            handle, ExportPrefix, "WrapChar16ToUInt32");
        var char16ToInt64 = GetWrappingExport<Char16ToInt64WrappingExport>(
            handle, ExportPrefix, "WrapChar16ToInt64");
        var char16ToUInt64 = GetWrappingExport<Char16ToUInt64WrappingExport>(
            handle, ExportPrefix, "WrapChar16ToUInt64");

        AssertWrapping<int, long>("WrapInt32ToInt64", signed32Values, 64, true,
            static value => new BigInteger(value), static value => new BigInteger(value), value => int32ToInt64(value));
        AssertWrapping<int, ulong>("WrapInt32ToUInt64", signed32Values, 64, false,
            static value => new BigInteger(value), static value => new BigInteger(value), value => int32ToUInt64(value));
        AssertWrapping<uint, long>("WrapUInt32ToInt64", unsigned32Values, 64, true,
            static value => new BigInteger(value), static value => new BigInteger(value), value => uint32ToInt64(value));
        AssertWrapping<uint, ulong>("WrapUInt32ToUInt64", unsigned32Values, 64, false,
            static value => new BigInteger(value), static value => new BigInteger(value), value => uint32ToUInt64(value));
        AssertWrapping<int, uint>("WrapInt32ToUInt32", signed32Values, 32, false,
            static value => new BigInteger(value), static value => new BigInteger(value), value => int32ToUInt32(value));
        AssertWrapping<uint, int>("WrapUInt32ToInt32", unsigned32Values, 32, true,
            static value => new BigInteger(value), static value => new BigInteger(value), value => uint32ToInt32(value));
        AssertWrapping<long, uint>("WrapInt64ToUInt32", signed64Values, 32, false,
            static value => new BigInteger(value), static value => new BigInteger(value), value => int64ToUInt32(value));
        AssertWrapping<long, int>("WrapInt64ToInt32", signed64Values, 32, true,
            static value => new BigInteger(value), static value => new BigInteger(value), value => int64ToInt32(value));
        AssertWrapping<ulong, uint>("WrapUInt64ToUInt32", unsigned64Values, 32, false,
            static value => new BigInteger(value), static value => new BigInteger(value), value => uint64ToUInt32(value));
        AssertWrapping<ulong, int>("WrapUInt64ToInt32", unsigned64Values, 32, true,
            static value => new BigInteger(value), static value => new BigInteger(value), value => uint64ToInt32(value));
        AssertWrapping<long, ulong>("WrapInt64ToUInt64", signed64Values, 64, false,
            static value => new BigInteger(value), static value => new BigInteger(value), value => int64ToUInt64(value));
        AssertWrapping<ulong, long>("WrapUInt64ToInt64", unsigned64Values, 64, true,
            static value => new BigInteger(value), static value => new BigInteger(value), value => uint64ToInt64(value));
        AssertWrapping<int, ushort>("WrapInt32ToChar16", signed32Values, 16, false,
            static value => new BigInteger(value), static value => new BigInteger(value), value => int32ToChar16(value));
        AssertWrapping<uint, ushort>("WrapUInt32ToChar16", unsigned32Values, 16, false,
            static value => new BigInteger(value), static value => new BigInteger(value), value => uint32ToChar16(value));
        AssertWrapping<long, ushort>("WrapInt64ToChar16", signed64Values, 16, false,
            static value => new BigInteger(value), static value => new BigInteger(value), value => int64ToChar16(value));
        AssertWrapping<ulong, ushort>("WrapUInt64ToChar16", unsigned64Values, 16, false,
            static value => new BigInteger(value), static value => new BigInteger(value), value => uint64ToChar16(value));
        AssertWrapping<ushort, int>("WrapChar16ToInt32", char16Values, 32, true,
            static value => new BigInteger(value), static value => new BigInteger(value), value => char16ToInt32(value));
        AssertWrapping<ushort, uint>("WrapChar16ToUInt32", char16Values, 32, false,
            static value => new BigInteger(value), static value => new BigInteger(value), value => char16ToUInt32(value));
        AssertWrapping<ushort, long>("WrapChar16ToInt64", char16Values, 64, true,
            static value => new BigInteger(value), static value => new BigInteger(value), value => char16ToInt64(value));
        AssertWrapping<ushort, ulong>("WrapChar16ToUInt64", char16Values, 64, false,
            static value => new BigInteger(value), static value => new BigInteger(value), value => char16ToUInt64(value));
    }

    private static void AssertWrapping<TSource, TResult>(
        string name,
        TSource[] inputs,
        int destinationWidth,
        bool destinationIsSigned,
        Func<TSource, BigInteger> sourceValue,
        Func<TResult, BigInteger> resultValue,
        Func<TSource, TResult> invoke)
    {
        var modulus = BigInteger.One << destinationWidth;
        var signBit = BigInteger.One << (destinationWidth - 1);
        foreach (var input in inputs)
        {
            var expected = sourceValue(input) % modulus;
            if (expected.Sign < 0)
            {
                expected += modulus;
            }
            if (destinationIsSigned && expected >= signBit)
            {
                expected -= modulus;
            }

            var actual = resultValue(invoke(input));
            Assert.True(actual == expected, $"{name} returned {actual} for input {input}; expected {expected}.");
        }
    }

    private static TDelegate GetWrappingExport<TDelegate>(nint handle, string prefix, string name)
        where TDelegate : Delegate =>
        Marshal.GetDelegateForFunctionPointer<TDelegate>(
            NativeLibrary.GetExport(handle, prefix + name));
}
