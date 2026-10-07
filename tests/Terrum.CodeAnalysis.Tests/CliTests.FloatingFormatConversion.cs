// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Float32BitsToFloat64BitsExport(uint bits);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Float64BitsToFloat32BitsExport(ulong bits);

    private static void AssertFloatingFormatConversionExports(IntPtr handle)
    {
        var widen = Marshal.GetDelegateForFunctionPointer<Float32BitsToFloat64BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float32BitsToFloat64Bits"));
        var narrow = Marshal.GetDelegateForFunctionPointer<Float64BitsToFloat32BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Float64BitsToFloat32Bits"));

        uint[] float32Inputs = [
            0, 0x80000000, 1, 0x80000001, 0x007FFFFF, 0x807FFFFF, 0x00555555,
            0x80555555, 0x00800000, 0x80800000, 0x3F800000, 0xBF800000,
            0x3DCCCCCD, 0xBDCCCCCD, 0x7F7FFFFF, 0xFF7FFFFF,
            0x7F800000, 0xFF800000, 0x7FC00000, 0x7F800001, 0xFFC00001,
        ];
        var random = new Random(0x32F10A7);
        for (var index = 0; index < 24; index++)
        {
            Array.Resize(ref float32Inputs, float32Inputs.Length + 1);
            float32Inputs[^1] = (uint)random.NextInt64(0, 1L << 32);
        }
        foreach (var bits in float32Inputs)
        {
            var value = BitConverter.UInt32BitsToSingle(bits);
            var actual = widen(bits);
            if (float.IsNaN(value))
            {
                Assert.Equal(0x7FF0000000000000UL, actual & 0x7FF0000000000000UL);
                Assert.NotEqual(0UL, actual & 0x000FFFFFFFFFFFFFUL);
            }
            else
            {
                Assert.Equal(BitConverter.DoubleToUInt64Bits((double)value), actual);
            }
        }

        ulong[] float64Inputs = [
            0, 0x8000000000000000, 1, 0x8000000000000001,
            0x000FFFFFFFFFFFFF, 0x800FFFFFFFFFFFFF,
            0x0010000000000000, 0x8010000000000000,
            0x3FF0000000000000, 0xBFF0000000000000,
            0x7FEFFFFFFFFFFFFF, 0xFFEFFFFFFFFFFFFF,
            0x7FF0000000000000, 0xFFF0000000000000,
            0x7FF8000000000000, 0x7FF0000000000001, 0xFFF8000000000001,
            0x3FF0000010000000, 0x3FF0000030000000, 0x3FFFFFFFF0000000,
            0x47EFFFFFF0000000, 0x3690000000000000, 0x36A8000000000000,
            0x380FFFFFE0000000,
        ];
        random = new Random(0x64F10A7);
        for (var index = 0; index < 24; index++)
        {
            Array.Resize(ref float64Inputs, float64Inputs.Length + 1);
            float64Inputs[^1] = NextUInt64ForAbi(random);
        }
        foreach (var midpoint in new ulong[] {
            0x3FF0000010000000, 0x3FF0000030000000, 0x3FFFFFFFF0000000,
            0x47EFFFFFF0000000, 0x3690000000000000, 0x36A8000000000000,
            0x380FFFFFE0000000,
        })
        {
            for (ulong offset = 0; offset < 3; offset++)
            {
                var value = midpoint - 1 + offset;
                Array.Resize(ref float64Inputs, float64Inputs.Length + 2);
                float64Inputs[^2] = value;
                float64Inputs[^1] = value | 0x8000000000000000;
            }
        }
        foreach (var bits in float64Inputs)
        {
            var value = BitConverter.UInt64BitsToDouble(bits);
            var actual = narrow(bits);
            if (double.IsNaN(value))
            {
                Assert.Equal(0x7F800000U, actual & 0x7F800000U);
                Assert.NotEqual(0U, actual & 0x007FFFFFU);
            }
            else
            {
                Assert.Equal(BitConverter.SingleToUInt32Bits((float)value), actual);
            }
        }
    }
}
