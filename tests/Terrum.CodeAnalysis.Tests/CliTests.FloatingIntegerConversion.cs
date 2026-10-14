// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root.

using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32ToInt32ConversionExport(uint bits, ref int result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32ToUInt32ConversionExport(uint bits, ref uint result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32ToInt64ConversionExport(uint bits, ref long result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32ToUInt64ConversionExport(uint bits, ref ulong result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float64ToInt32ConversionExport(ulong bits, ref int result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float64ToUInt32ConversionExport(ulong bits, ref uint result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float64ToInt64ConversionExport(ulong bits, ref long result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float64ToUInt64ConversionExport(ulong bits, ref ulong result);

    private static void AssertFloatingIntegerConversionExports(IntPtr handle)
    {
        const string ExportPrefix = "Terrum.CodeAnalysis.";
        var float32ToInt32 = Load<Float32ToInt32ConversionExport>(handle, ExportPrefix + "TryFloat32BitsToInt32");
        var float32ToUInt32 = Load<Float32ToUInt32ConversionExport>(handle, ExportPrefix + "TryFloat32BitsToUInt32");
        var float32ToInt64 = Load<Float32ToInt64ConversionExport>(handle, ExportPrefix + "TryFloat32BitsToInt64");
        var float32ToUInt64 = Load<Float32ToUInt64ConversionExport>(handle, ExportPrefix + "TryFloat32BitsToUInt64");
        var float64ToInt32 = Load<Float64ToInt32ConversionExport>(handle, ExportPrefix + "TryFloat64BitsToInt32");
        var float64ToUInt32 = Load<Float64ToUInt32ConversionExport>(handle, ExportPrefix + "TryFloat64BitsToUInt32");
        var float64ToInt64 = Load<Float64ToInt64ConversionExport>(handle, ExportPrefix + "TryFloat64BitsToInt64");
        var float64ToUInt64 = Load<Float64ToUInt64ConversionExport>(handle, ExportPrefix + "TryFloat64BitsToUInt64");

        uint[] float32Inputs = [
            0, 0x80000000, 1, 0x80000001, 0x3F000000, 0xBF000000,
            0x3F7FFFFF, 0xBF7FFFFF, 0x3F800000, 0xBF800000,
            0x3FF33333, 0xBFF33333, 0x4EFFFFFF, 0x4F000000,
            0xCF000000, 0xCF000001, 0x4F7FFFFF, 0x4F800000,
            0x5EFFFFFF, 0x5F000000, 0xDEFFFFFF, 0xDF000000,
            0xDF000001, 0x5F7FFFFF, 0x5F800000,
            0x7F800000, 0xFF800000, 0x7FC00000, 0x7FA00001, 0xFFC00001,
        ];
        ulong[] float64Inputs = [
            0, 0x8000000000000000, 1, 0x8000000000000001,
            BitConverter.DoubleToUInt64Bits(0.5), BitConverter.DoubleToUInt64Bits(-0.5),
            BitConverter.DoubleToUInt64Bits(Math.BitDecrement(1.0)),
            BitConverter.DoubleToUInt64Bits(Math.BitDecrement(-1.0)),
            BitConverter.DoubleToUInt64Bits(1.0), BitConverter.DoubleToUInt64Bits(-1.0),
            BitConverter.DoubleToUInt64Bits(1.9), BitConverter.DoubleToUInt64Bits(-1.9),
            BitConverter.DoubleToUInt64Bits(-2147483648.9),
            BitConverter.DoubleToUInt64Bits(2147483647.9),
            BitConverter.DoubleToUInt64Bits(-2147483649.0),
            BitConverter.DoubleToUInt64Bits(2147483648.0),
            BitConverter.DoubleToUInt64Bits(4294967295.75),
            BitConverter.DoubleToUInt64Bits(4294967296.0),
            BitConverter.DoubleToUInt64Bits((double)long.MinValue),
            BitConverter.DoubleToUInt64Bits(Math.BitDecrement((double)long.MinValue)),
            BitConverter.DoubleToUInt64Bits(Math.BitDecrement(9223372036854775808.0)),
            BitConverter.DoubleToUInt64Bits(9223372036854775808.0),
            BitConverter.DoubleToUInt64Bits(Math.BitDecrement(18446744073709551616.0)),
            BitConverter.DoubleToUInt64Bits(18446744073709551616.0),
            0x7FF0000000000000, 0xFFF0000000000000, 0x7FF8000000000000,
            0x7FF0000000000001, 0xFFF8000000000001,
        ];

        var random = new Random(0xF10A1C);
        for (var index = 0; index < 128; index++)
        {
            Array.Resize(ref float32Inputs, float32Inputs.Length + 1);
            float32Inputs[^1] = (uint)random.NextInt64(0, 1L << 32);
            Array.Resize(ref float64Inputs, float64Inputs.Length + 1);
            float64Inputs[^1] = NextUInt64ForAbi(random);
        }

        foreach (var bits in float32Inputs)
        {
            AssertFloatingConversion(bits,
                static value => checked((int)BitConverter.UInt32BitsToSingle(value)),
                (uint value, ref int result) => float32ToInt32(value, ref result),
                unchecked((int)0xA5C39E17), 0x13579BDF, 0x2468ACE0);
            AssertFloatingConversion(bits,
                static value => checked((uint)BitConverter.UInt32BitsToSingle(value)),
                (uint value, ref uint result) => float32ToUInt32(value, ref result),
                0xA5C39E17U, 0x13579BDFU, 0x2468ACE0U);
            AssertFloatingConversion(bits,
                static value => checked((long)BitConverter.UInt32BitsToSingle(value)),
                (uint value, ref long result) => float32ToInt64(value, ref result),
                unchecked((long)0xA5C39E1789ABCDEF), 0x13579BDF2468ACE0L,
                0x2468ACE013579BDFL);
            AssertFloatingConversion(bits,
                static value => checked((ulong)BitConverter.UInt32BitsToSingle(value)),
                (uint value, ref ulong result) => float32ToUInt64(value, ref result),
                0xA5C39E1789ABCDEFUL, 0x13579BDF2468ACE0UL,
                0x2468ACE013579BDFUL);
        }

        foreach (var bits in float64Inputs)
        {
            AssertFloatingConversion(bits,
                static value => checked((int)BitConverter.UInt64BitsToDouble(value)),
                (ulong value, ref int result) => float64ToInt32(value, ref result),
                unchecked((int)0xA5C39E17), 0x13579BDF, 0x2468ACE0);
            AssertFloatingConversion(bits,
                static value => checked((uint)BitConverter.UInt64BitsToDouble(value)),
                (ulong value, ref uint result) => float64ToUInt32(value, ref result),
                0xA5C39E17U, 0x13579BDFU, 0x2468ACE0U);
            AssertFloatingConversion(bits,
                static value => checked((long)BitConverter.UInt64BitsToDouble(value)),
                (ulong value, ref long result) => float64ToInt64(value, ref result),
                unchecked((long)0xA5C39E1789ABCDEF), 0x13579BDF2468ACE0L,
                0x2468ACE013579BDFL);
            AssertFloatingConversion(bits,
                static value => checked((ulong)BitConverter.UInt64BitsToDouble(value)),
                (ulong value, ref ulong result) => float64ToUInt64(value, ref result),
                0xA5C39E1789ABCDEFUL, 0x13579BDF2468ACE0UL,
                0x2468ACE013579BDFUL);
        }
    }

    private static void AssertFloatingConversion<TSource, TResult>(
        TSource input,
        Func<TSource, TResult> expectedConversion,
        CheckedConversionInvoker<TSource, TResult> conversion,
        TResult sentinel,
        TResult lowerCanary,
        TResult upperCanary)
        where TResult : struct
    {
        var expectedSuccess = TryConvert(() => expectedConversion(input), out TResult expected);
        var guarded = new GuardedConversionResult<TResult> {
            Before = lowerCanary,
            Value = sentinel,
            After = upperCanary,
        };
        var status = conversion(input, ref guarded.Value);
        Assert.That(status == (expectedSuccess ? 1 : 0), Is.True, $"Conversion returned status {status} for input {input}.");
        if (expectedSuccess)
        {
            Assert.That(guarded.Value, Is.EqualTo(expected));
        }
        else
        {
            Assert.That(guarded.Value, Is.EqualTo(sentinel));
        }
        Assert.That(guarded.Before, Is.EqualTo(lowerCanary));
        Assert.That(guarded.After, Is.EqualTo(upperCanary));
    }

    private static bool TryConvert<T>(Func<T> conversion, out T result)
        where T : struct
    {
        try
        {
            result = conversion();
            return true;
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }

    private static TDelegate Load<TDelegate>(IntPtr handle, string name)
        where TDelegate : Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(NativeLibrary.GetExport(handle, name));
    }
}
