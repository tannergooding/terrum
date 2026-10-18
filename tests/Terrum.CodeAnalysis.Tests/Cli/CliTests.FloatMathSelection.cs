// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32UnarySelectionExport(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32BinarySelectionExport(int left, int right);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32ClampSelectionExport(int value, int minimum, int maximum);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float64UnarySelectionExport(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float64BinarySelectionExport(long left, long right);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float64ClampSelectionExport(long value, long minimum, long maximum);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Int32ToFloat32BitsExport(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Int32ToFloat64BitsExport(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint UInt32ToFloat32BitsExport(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong UInt32ToFloat64BitsExport(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Int64ToFloat32BitsExport(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Int64ToFloat64BitsExport(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint UInt64ToFloat32BitsExport(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong UInt64ToFloat64BitsExport(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Char16ToFloat32BitsExport(ushort value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Char16ToFloat64BitsExport(ushort value);

    private static void AssertFloatingMathSelectionExports(IntPtr handle)
    {
        var absFloat32 = Marshal.GetDelegateForFunctionPointer<Float32UnarySelectionExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.AbsFloat32"));
        var minFloat32 = Marshal.GetDelegateForFunctionPointer<Float32BinarySelectionExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.MinFloat32"));
        var maxFloat32 = Marshal.GetDelegateForFunctionPointer<Float32BinarySelectionExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.MaxFloat32"));
        var clampFloat32 = Marshal.GetDelegateForFunctionPointer<Float32ClampSelectionExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.ClampFloat32"));
        var absFloat64 = Marshal.GetDelegateForFunctionPointer<Float64UnarySelectionExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.AbsFloat64"));
        var minFloat64 = Marshal.GetDelegateForFunctionPointer<Float64BinarySelectionExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.MinFloat64"));
        var maxFloat64 = Marshal.GetDelegateForFunctionPointer<Float64BinarySelectionExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.MaxFloat64"));
        var clampFloat64 = Marshal.GetDelegateForFunctionPointer<Float64ClampSelectionExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.ClampFloat64"));

        AssertFloat32Selection(absFloat32, minFloat32, maxFloat32, clampFloat32);
        AssertFloat64Selection(absFloat64, minFloat64, maxFloat64, clampFloat64);
    }

    private static void AssertIntegerFloatConversionExports(IntPtr handle)
    {
        var int32ToFloat32 = Marshal.GetDelegateForFunctionPointer<Int32ToFloat32BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Int32ToFloat32Bits"));
        var int32ToFloat64 = Marshal.GetDelegateForFunctionPointer<Int32ToFloat64BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Int32ToFloat64Bits"));
        var uint32ToFloat32 = Marshal.GetDelegateForFunctionPointer<UInt32ToFloat32BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.UInt32ToFloat32Bits"));
        var uint32ToFloat64 = Marshal.GetDelegateForFunctionPointer<UInt32ToFloat64BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.UInt32ToFloat64Bits"));
        var int64ToFloat32 = Marshal.GetDelegateForFunctionPointer<Int64ToFloat32BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Int64ToFloat32Bits"));
        var int64ToFloat64 = Marshal.GetDelegateForFunctionPointer<Int64ToFloat64BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Int64ToFloat64Bits"));
        var uint64ToFloat32 = Marshal.GetDelegateForFunctionPointer<UInt64ToFloat32BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.UInt64ToFloat32Bits"));
        var uint64ToFloat64 = Marshal.GetDelegateForFunctionPointer<UInt64ToFloat64BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.UInt64ToFloat64Bits"));
        var char16ToFloat32 = Marshal.GetDelegateForFunctionPointer<Char16ToFloat32BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Char16ToFloat32Bits"));
        var char16ToFloat64 = Marshal.GetDelegateForFunctionPointer<Char16ToFloat64BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Char16ToFloat64Bits"));

        foreach (var value in new[] {
            int.MinValue, int.MinValue + 1, -16_777_219, -16_777_217, -1, 0, 1,
            16_777_215, 16_777_216, 16_777_217, int.MaxValue,
        })
        {
            Assert.That(int32ToFloat32(value), Is.EqualTo(BitConverter.SingleToUInt32Bits((float)value)));
            Assert.That(int32ToFloat64(value), Is.EqualTo(BitConverter.DoubleToUInt64Bits((double)value)));
        }
        foreach (var value in new uint[] {
            0, 1, 16_777_215, 16_777_216, 16_777_217, 0x8000_0000, uint.MaxValue,
        })
        {
            Assert.That(uint32ToFloat32(value), Is.EqualTo(BitConverter.SingleToUInt32Bits((float)value)));
            Assert.That(uint32ToFloat64(value), Is.EqualTo(BitConverter.DoubleToUInt64Bits((double)value)));
        }
        foreach (var value in new long[] {
            long.MinValue, long.MinValue + 1, -9_007_199_254_740_995, -9_007_199_254_740_993,
            -16_777_219, -16_777_217, -1, 0, 1, 16_777_217, 16_777_219,
            9_007_199_254_740_993, 9_007_199_254_740_995, long.MaxValue,
        })
        {
            Assert.That(int64ToFloat32(value), Is.EqualTo(BitConverter.SingleToUInt32Bits((float)value)));
            Assert.That(int64ToFloat64(value), Is.EqualTo(BitConverter.DoubleToUInt64Bits((double)value)));
        }
        var twoTo63 = 1UL << 63;
        var float32HalfUlpAtTwoTo63 = 1UL << 39;
        var float32UlpAtTwoTo63 = 1UL << 40;
        var float64HalfUlpAtTwoTo63 = 1UL << 10;
        var float64UlpAtTwoTo63 = 1UL << 11;
        foreach (var value in new ulong[] {
            0, 1, 16_777_215, 16_777_216, 16_777_217, 16_777_219,
            9_007_199_254_740_991, 9_007_199_254_740_992, 9_007_199_254_740_993,
            9_007_199_254_740_995, twoTo63, ulong.MaxValue,
            twoTo63 + float32HalfUlpAtTwoTo63 - 1,
            twoTo63 + float32HalfUlpAtTwoTo63,
            twoTo63 + float32HalfUlpAtTwoTo63 + 1,
            twoTo63 + float32UlpAtTwoTo63 + float32HalfUlpAtTwoTo63,
            twoTo63 + float64HalfUlpAtTwoTo63 - 1,
            twoTo63 + float64HalfUlpAtTwoTo63,
            twoTo63 + float64HalfUlpAtTwoTo63 + 1,
            twoTo63 + float64UlpAtTwoTo63 + float64HalfUlpAtTwoTo63,
        })
        {
            Assert.That(uint64ToFloat32(value), Is.EqualTo(BitConverter.SingleToUInt32Bits((float)value)));
            Assert.That(uint64ToFloat64(value), Is.EqualTo(BitConverter.DoubleToUInt64Bits((double)value)));
        }
        for (var codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
        {
            var value = (char)codeUnit;
            var carrier = (ushort)codeUnit;
            Assert.That(char16ToFloat32(carrier), Is.EqualTo(BitConverter.SingleToUInt32Bits((float)value)));
            Assert.That(char16ToFloat64(carrier), Is.EqualTo(BitConverter.DoubleToUInt64Bits((double)value)));
        }

        var random = new Random(0xF10A7);
        for (var index = 0; index < 24; index++)
        {
            var signed32 = (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
            var unsigned32 = (uint)random.NextInt64(0, 1L << 32);
            var signed64 = unchecked((long)NextUInt64ForAbi(random));
            var unsigned64 = NextUInt64ForAbi(random);
            Assert.That(int32ToFloat32(signed32), Is.EqualTo(BitConverter.SingleToUInt32Bits((float)signed32)));
            Assert.That(int32ToFloat64(signed32), Is.EqualTo(BitConverter.DoubleToUInt64Bits((double)signed32)));
            Assert.That(uint32ToFloat32(unsigned32), Is.EqualTo(BitConverter.SingleToUInt32Bits((float)unsigned32)));
            Assert.That(uint32ToFloat64(unsigned32), Is.EqualTo(BitConverter.DoubleToUInt64Bits((double)unsigned32)));
            Assert.That(int64ToFloat32(signed64), Is.EqualTo(BitConverter.SingleToUInt32Bits((float)signed64)));
            Assert.That(int64ToFloat64(signed64), Is.EqualTo(BitConverter.DoubleToUInt64Bits((double)signed64)));
            Assert.That(uint64ToFloat32(unsigned64), Is.EqualTo(BitConverter.SingleToUInt32Bits((float)unsigned64)));
            Assert.That(uint64ToFloat64(unsigned64), Is.EqualTo(BitConverter.DoubleToUInt64Bits((double)unsigned64)));
        }
    }

    private static ulong NextUInt64ForAbi(Random random)
    {
        var high = (uint)random.NextInt64(0, 1L << 32);
        var low = (uint)random.NextInt64(0, 1L << 32);
        return ((ulong)high << 32) | low;
    }

    private static void AssertFloat32Selection(
        Float32UnarySelectionExport abs,
        Float32BinarySelectionExport min,
        Float32BinarySelectionExport max,
        Float32ClampSelectionExport clamp)
    {
        float[] values = [
            float.NegativeInfinity,
            float.MinValue,
            BitConverter.Int32BitsToSingle(unchecked((int)0xFF7FFFFE)),
            -123.5f,
            -2.25f,
            -1.0f,
            BitConverter.Int32BitsToSingle(unchecked((int)0x80800001)),
            BitConverter.Int32BitsToSingle(unchecked((int)0x80800000)),
            BitConverter.Int32BitsToSingle(unchecked((int)0x807FFFFF)),
            -float.Epsilon,
            float.Epsilon,
            BitConverter.Int32BitsToSingle(0x007FFFFF),
            BitConverter.Int32BitsToSingle(0x00800000),
            BitConverter.Int32BitsToSingle(0x00800001),
            0.5f,
            BitConverter.Int32BitsToSingle(0x3F7FFFFF),
            1.0f,
            BitConverter.Int32BitsToSingle(0x3F800001),
            2.25f,
            123.5f,
            float.MaxValue,
            float.PositiveInfinity,
        ];

        foreach (var value in values)
        {
            var expectedAbs = BitConverter.Int32BitsToSingle(
                BitConverter.SingleToInt32Bits(value) & int.MaxValue);
            AssertFloat32Bits(expectedAbs, InvokeFloat32(abs, value));

            var expectedClamp = value < -2.25f ? -2.25f : value > 2.25f ? 2.25f : value;
            AssertFloat32Bits(expectedClamp, InvokeFloat32(clamp, value, -2.25f, 2.25f));

            foreach (var other in values)
            {
                AssertFloat32Bits(value < other ? value : other, InvokeFloat32(min, value, other));
                AssertFloat32Bits(value > other ? value : other, InvokeFloat32(max, value, other));
            }
        }

        var positiveZero = 0.0f;
        var negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        AssertFloat32Bits(positiveZero, InvokeFloat32(abs, positiveZero));
        AssertFloat32Bits(positiveZero, InvokeFloat32(abs, negativeZero));

        foreach (var left in new[] { positiveZero, negativeZero })
        {
            foreach (var right in new[] { positiveZero, negativeZero })
            {
                var leftBits = BitConverter.SingleToInt32Bits(left);
                var rightBits = BitConverter.SingleToInt32Bits(right);
                var expectedMin = (leftBits | rightBits) < 0 ? negativeZero : positiveZero;
                var expectedMax = (leftBits & rightBits) < 0 ? negativeZero : positiveZero;
                AssertFloat32Bits(expectedMin, InvokeFloat32(min, left, right));
                AssertFloat32Bits(expectedMax, InvokeFloat32(max, left, right));

                foreach (var value in new[] { positiveZero, negativeZero })
                {
                    AssertFloat32Bits(value, InvokeFloat32(clamp, value, left, right));
                }
            }
        }

        foreach (var bound in new[] { positiveZero, negativeZero })
        {
            AssertFloat32Bits(bound, InvokeFloat32(clamp, -1.0f, bound, 1.0f));
            AssertFloat32Bits(bound, InvokeFloat32(clamp, 1.0f, -1.0f, bound));
        }

        AssertFloat32Bits(1.25f, InvokeFloat32(clamp, 1.25f, float.NegativeInfinity, float.PositiveInfinity));
        AssertFloat32Bits(float.NegativeInfinity,
            InvokeFloat32(clamp, -1.0f, float.NegativeInfinity, float.NegativeInfinity));
        AssertFloat32Bits(float.PositiveInfinity,
            InvokeFloat32(clamp, 1.0f, float.PositiveInfinity, float.PositiveInfinity));

        var nanValues = new[] {
            BitConverter.Int32BitsToSingle(0x7FC00001),
            BitConverter.Int32BitsToSingle(unchecked((int)0xFFC12345)),
        };
        foreach (var nan in nanValues)
        {
            Assert.That(float.IsNaN(InvokeFloat32(abs, nan)), Is.True);
            Assert.That(float.IsNaN(InvokeFloat32(min, nan, 1.0f)), Is.True);
            Assert.That(float.IsNaN(InvokeFloat32(min, 1.0f, nan)), Is.True);
            Assert.That(float.IsNaN(InvokeFloat32(max, nan, 1.0f)), Is.True);
            Assert.That(float.IsNaN(InvokeFloat32(max, 1.0f, nan)), Is.True);
            Assert.That(float.IsNaN(InvokeFloat32(clamp, nan, -1.0f, 1.0f)), Is.True);
        }
        Assert.That(float.IsNaN(InvokeFloat32(min, nanValues[0], nanValues[1])), Is.True);
        Assert.That(float.IsNaN(InvokeFloat32(max, nanValues[0], nanValues[1])), Is.True);
    }

    private static void AssertFloat64Selection(
        Float64UnarySelectionExport abs,
        Float64BinarySelectionExport min,
        Float64BinarySelectionExport max,
        Float64ClampSelectionExport clamp)
    {
        double[] values = [
            double.NegativeInfinity,
            double.MinValue,
            BitConverter.Int64BitsToDouble(unchecked((long)0xFFEFFFFFFFFFFFFEUL)),
            -123.5,
            -2.25,
            -1.0,
            BitConverter.Int64BitsToDouble(unchecked((long)0x8010000000000001UL)),
            BitConverter.Int64BitsToDouble(unchecked((long)0x8010000000000000UL)),
            BitConverter.Int64BitsToDouble(unchecked((long)0x800FFFFFFFFFFFFFUL)),
            -double.Epsilon,
            double.Epsilon,
            BitConverter.Int64BitsToDouble(0x000FFFFFFFFFFFFF),
            BitConverter.Int64BitsToDouble(0x0010000000000000),
            BitConverter.Int64BitsToDouble(0x0010000000000001),
            0.5,
            BitConverter.Int64BitsToDouble(0x3FEFFFFFFFFFFFFF),
            1.0,
            BitConverter.Int64BitsToDouble(0x3FF0000000000001),
            2.25,
            123.5,
            double.MaxValue,
            double.PositiveInfinity,
        ];

        foreach (var value in values)
        {
            var expectedAbs = BitConverter.Int64BitsToDouble(
                BitConverter.DoubleToInt64Bits(value) & long.MaxValue);
            AssertFloat64Bits(expectedAbs, InvokeFloat64(abs, value));

            var expectedClamp = value < -2.25 ? -2.25 : value > 2.25 ? 2.25 : value;
            AssertFloat64Bits(expectedClamp, InvokeFloat64(clamp, value, -2.25, 2.25));

            foreach (var other in values)
            {
                AssertFloat64Bits(value < other ? value : other, InvokeFloat64(min, value, other));
                AssertFloat64Bits(value > other ? value : other, InvokeFloat64(max, value, other));
            }
        }

        var positiveZero = 0.0;
        var negativeZero = BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000UL));
        AssertFloat64Bits(positiveZero, InvokeFloat64(abs, positiveZero));
        AssertFloat64Bits(positiveZero, InvokeFloat64(abs, negativeZero));

        foreach (var left in new[] { positiveZero, negativeZero })
        {
            foreach (var right in new[] { positiveZero, negativeZero })
            {
                var leftBits = BitConverter.DoubleToInt64Bits(left);
                var rightBits = BitConverter.DoubleToInt64Bits(right);
                var expectedMin = (leftBits | rightBits) < 0 ? negativeZero : positiveZero;
                var expectedMax = (leftBits & rightBits) < 0 ? negativeZero : positiveZero;
                AssertFloat64Bits(expectedMin, InvokeFloat64(min, left, right));
                AssertFloat64Bits(expectedMax, InvokeFloat64(max, left, right));

                foreach (var value in new[] { positiveZero, negativeZero })
                {
                    AssertFloat64Bits(value, InvokeFloat64(clamp, value, left, right));
                }
            }
        }

        foreach (var bound in new[] { positiveZero, negativeZero })
        {
            AssertFloat64Bits(bound, InvokeFloat64(clamp, -1.0, bound, 1.0));
            AssertFloat64Bits(bound, InvokeFloat64(clamp, 1.0, -1.0, bound));
        }

        AssertFloat64Bits(1.25, InvokeFloat64(clamp, 1.25, double.NegativeInfinity, double.PositiveInfinity));
        AssertFloat64Bits(double.NegativeInfinity,
            InvokeFloat64(clamp, -1.0, double.NegativeInfinity, double.NegativeInfinity));
        AssertFloat64Bits(double.PositiveInfinity,
            InvokeFloat64(clamp, 1.0, double.PositiveInfinity, double.PositiveInfinity));

        var nanValues = new[] {
            BitConverter.Int64BitsToDouble(0x7FF8000000000001),
            BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8123456789ABCUL)),
        };
        foreach (var nan in nanValues)
        {
            Assert.That(double.IsNaN(InvokeFloat64(abs, nan)), Is.True);
            Assert.That(double.IsNaN(InvokeFloat64(min, nan, 1.0)), Is.True);
            Assert.That(double.IsNaN(InvokeFloat64(min, 1.0, nan)), Is.True);
            Assert.That(double.IsNaN(InvokeFloat64(max, nan, 1.0)), Is.True);
            Assert.That(double.IsNaN(InvokeFloat64(max, 1.0, nan)), Is.True);
            Assert.That(double.IsNaN(InvokeFloat64(clamp, nan, -1.0, 1.0)), Is.True);
        }
        Assert.That(double.IsNaN(InvokeFloat64(min, nanValues[0], nanValues[1])), Is.True);
        Assert.That(double.IsNaN(InvokeFloat64(max, nanValues[0], nanValues[1])), Is.True);
    }

    private static float InvokeFloat32(Float32UnarySelectionExport export, float value)
    {
        return BitConverter.Int32BitsToSingle(export(BitConverter.SingleToInt32Bits(value)));
    }

    private static float InvokeFloat32(Float32BinarySelectionExport export, float left, float right)
    {
        return BitConverter.Int32BitsToSingle(export(
            BitConverter.SingleToInt32Bits(left),
            BitConverter.SingleToInt32Bits(right)));
    }

    private static float InvokeFloat32(
        Float32ClampSelectionExport export,
        float value,
        float minimum,
        float maximum)
    {
        return BitConverter.Int32BitsToSingle(export(
            BitConverter.SingleToInt32Bits(value),
            BitConverter.SingleToInt32Bits(minimum),
            BitConverter.SingleToInt32Bits(maximum)));
    }

    private static double InvokeFloat64(Float64UnarySelectionExport export, double value)
    {
        return BitConverter.Int64BitsToDouble(export(BitConverter.DoubleToInt64Bits(value)));
    }

    private static double InvokeFloat64(Float64BinarySelectionExport export, double left, double right)
    {
        return BitConverter.Int64BitsToDouble(export(
            BitConverter.DoubleToInt64Bits(left),
            BitConverter.DoubleToInt64Bits(right)));
    }

    private static double InvokeFloat64(
        Float64ClampSelectionExport export,
        double value,
        double minimum,
        double maximum)
    {
        return BitConverter.Int64BitsToDouble(export(
            BitConverter.DoubleToInt64Bits(value),
            BitConverter.DoubleToInt64Bits(minimum),
            BitConverter.DoubleToInt64Bits(maximum)));
    }

    private static void AssertFloat32Bits(float expected, float actual)
    {
        Assert.That(BitConverter.SingleToInt32Bits(actual), Is.EqualTo(BitConverter.SingleToInt32Bits(expected)));
    }

    private static void AssertFloat64Bits(double expected, double actual)
    {
        Assert.That(BitConverter.DoubleToInt64Bits(actual), Is.EqualTo(BitConverter.DoubleToInt64Bits(expected)));
    }
}
