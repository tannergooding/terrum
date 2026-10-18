// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32ComparisonExport(uint left, uint right);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float64ComparisonExport(ulong left, ulong right);

    private static void AssertFloatingComparisonExports(IntPtr handle)
    {
        var float32Equal = Marshal.GetDelegateForFunctionPointer<Float32ComparisonExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float32EqualBits"));
        var float32NotEqual = Marshal.GetDelegateForFunctionPointer<Float32ComparisonExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float32NotEqualBits"));
        var float32LessThan = Marshal.GetDelegateForFunctionPointer<Float32ComparisonExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float32LessThanBits"));
        var float32LessThanOrEqual = Marshal.GetDelegateForFunctionPointer<Float32ComparisonExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float32LessThanOrEqualBits"));
        var float32GreaterThan = Marshal.GetDelegateForFunctionPointer<Float32ComparisonExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float32GreaterThanBits"));
        var float32GreaterThanOrEqual = Marshal.GetDelegateForFunctionPointer<Float32ComparisonExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float32GreaterThanOrEqualBits"));
        var float64Equal = Marshal.GetDelegateForFunctionPointer<Float64ComparisonExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float64EqualBits"));
        var float64NotEqual = Marshal.GetDelegateForFunctionPointer<Float64ComparisonExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float64NotEqualBits"));
        var float64LessThan = Marshal.GetDelegateForFunctionPointer<Float64ComparisonExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float64LessThanBits"));
        var float64LessThanOrEqual = Marshal.GetDelegateForFunctionPointer<Float64ComparisonExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float64LessThanOrEqualBits"));
        var float64GreaterThan = Marshal.GetDelegateForFunctionPointer<Float64ComparisonExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float64GreaterThanBits"));
        var float64GreaterThanOrEqual = Marshal.GetDelegateForFunctionPointer<Float64ComparisonExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float64GreaterThanOrEqualBits"));

        uint[] float32Boundaries = [
            0, 0x80000000, 1, 0x80000001, 0x007FFFFF, 0x807FFFFF,
            0x00800000, 0x80800000, 0x3F000000, 0xBF000000,
            0x3F800000, 0xBF800000, 0x7F7FFFFF, 0xFF7FFFFF,
            0x7F800000, 0xFF800000, 0x7F800001, 0xFF800001,
            0x7FC12345, 0xFFCABCDE,
        ];
        foreach (var left in float32Boundaries)
        {
            foreach (var right in float32Boundaries)
            {
                AssertFloat32Comparison(left, right, float32Equal, float32NotEqual,
                    float32LessThan, float32LessThanOrEqual, float32GreaterThan,
                    float32GreaterThanOrEqual);
            }
        }

        ulong[] float64Boundaries = [
            0, 0x8000000000000000, 1, 0x8000000000000001,
            0x000FFFFFFFFFFFFF, 0x800FFFFFFFFFFFFF,
            0x0010000000000000, 0x8010000000000000,
            0x3FE0000000000000, 0xBFE0000000000000,
            0x3FF0000000000000, 0xBFF0000000000000,
            0x7FEFFFFFFFFFFFFF, 0xFFEFFFFFFFFFFFFF,
            0x7FF0000000000000, 0xFFF0000000000000,
            0x7FF0000000000001, 0xFFF0000000000001,
            0x7FF8123456789ABC, 0xFFFABCDE12345678,
        ];
        foreach (var left in float64Boundaries)
        {
            foreach (var right in float64Boundaries)
            {
                AssertFloat64Comparison(left, right, float64Equal, float64NotEqual,
                    float64LessThan, float64LessThanOrEqual, float64GreaterThan,
                    float64GreaterThanOrEqual);
            }
        }

        var random = new Random(0xC04A2E);
        for (var index = 0; index < 128; index++)
        {
            var float32Left = (uint)random.NextInt64(0, 1L << 32);
            var float32Right = (uint)random.NextInt64(0, 1L << 32);
            AssertFloat32Comparison(float32Left, float32Right, float32Equal, float32NotEqual,
                float32LessThan, float32LessThanOrEqual, float32GreaterThan,
                float32GreaterThanOrEqual);

            var float64Left = NextUInt64ForAbi(random);
            var float64Right = NextUInt64ForAbi(random);
            AssertFloat64Comparison(float64Left, float64Right, float64Equal, float64NotEqual,
                float64LessThan, float64LessThanOrEqual, float64GreaterThan,
                float64GreaterThanOrEqual);
        }
    }

    private static void AssertFloat32Comparison(
        uint leftBits,
        uint rightBits,
        Float32ComparisonExport equal,
        Float32ComparisonExport notEqual,
        Float32ComparisonExport lessThan,
        Float32ComparisonExport lessThanOrEqual,
        Float32ComparisonExport greaterThan,
        Float32ComparisonExport greaterThanOrEqual)
    {
        var left = BitConverter.UInt32BitsToSingle(leftBits);
        var right = BitConverter.UInt32BitsToSingle(rightBits);
        Assert.That(equal(leftBits, rightBits), Is.EqualTo(left == right ? 1 : 0));
        Assert.That(notEqual(leftBits, rightBits), Is.EqualTo(left != right ? 1 : 0));
        Assert.That(lessThan(leftBits, rightBits), Is.EqualTo(left < right ? 1 : 0));
        Assert.That(lessThanOrEqual(leftBits, rightBits), Is.EqualTo(left <= right ? 1 : 0));
        Assert.That(greaterThan(leftBits, rightBits), Is.EqualTo(left > right ? 1 : 0));
        Assert.That(greaterThanOrEqual(leftBits, rightBits), Is.EqualTo(left >= right ? 1 : 0));
    }

    private static void AssertFloat64Comparison(
        ulong leftBits,
        ulong rightBits,
        Float64ComparisonExport equal,
        Float64ComparisonExport notEqual,
        Float64ComparisonExport lessThan,
        Float64ComparisonExport lessThanOrEqual,
        Float64ComparisonExport greaterThan,
        Float64ComparisonExport greaterThanOrEqual)
    {
        var left = BitConverter.UInt64BitsToDouble(leftBits);
        var right = BitConverter.UInt64BitsToDouble(rightBits);
        Assert.That(equal(leftBits, rightBits), Is.EqualTo(left == right ? 1 : 0));
        Assert.That(notEqual(leftBits, rightBits), Is.EqualTo(left != right ? 1 : 0));
        Assert.That(lessThan(leftBits, rightBits), Is.EqualTo(left < right ? 1 : 0));
        Assert.That(lessThanOrEqual(leftBits, rightBits), Is.EqualTo(left <= right ? 1 : 0));
        Assert.That(greaterThan(leftBits, rightBits), Is.EqualTo(left > right ? 1 : 0));
        Assert.That(greaterThanOrEqual(leftBits, rightBits), Is.EqualTo(left >= right ? 1 : 0));
    }
}
