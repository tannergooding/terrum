// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

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
            Assert.True(float.IsNaN(InvokeFloat32(abs, nan)));
            Assert.True(float.IsNaN(InvokeFloat32(min, nan, 1.0f)));
            Assert.True(float.IsNaN(InvokeFloat32(min, 1.0f, nan)));
            Assert.True(float.IsNaN(InvokeFloat32(max, nan, 1.0f)));
            Assert.True(float.IsNaN(InvokeFloat32(max, 1.0f, nan)));
            Assert.True(float.IsNaN(InvokeFloat32(clamp, nan, -1.0f, 1.0f)));
        }
        Assert.True(float.IsNaN(InvokeFloat32(min, nanValues[0], nanValues[1])));
        Assert.True(float.IsNaN(InvokeFloat32(max, nanValues[0], nanValues[1])));
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
            Assert.True(double.IsNaN(InvokeFloat64(abs, nan)));
            Assert.True(double.IsNaN(InvokeFloat64(min, nan, 1.0)));
            Assert.True(double.IsNaN(InvokeFloat64(min, 1.0, nan)));
            Assert.True(double.IsNaN(InvokeFloat64(max, nan, 1.0)));
            Assert.True(double.IsNaN(InvokeFloat64(max, 1.0, nan)));
            Assert.True(double.IsNaN(InvokeFloat64(clamp, nan, -1.0, 1.0)));
        }
        Assert.True(double.IsNaN(InvokeFloat64(min, nanValues[0], nanValues[1])));
        Assert.True(double.IsNaN(InvokeFloat64(max, nanValues[0], nanValues[1])));
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
        Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual));
    }

    private static void AssertFloat64Bits(double expected, double actual)
    {
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
    }
}
