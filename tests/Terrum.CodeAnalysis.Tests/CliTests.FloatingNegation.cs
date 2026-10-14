// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint NegateFloat32BitsExport(uint bits);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong NegateFloat64BitsExport(ulong bits);

    private static void AssertFloatingNegationExports(IntPtr handle)
    {
        var negateFloat32 = Marshal.GetDelegateForFunctionPointer<NegateFloat32BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.NegateFloat32Bits"));
        var negateFloat64 = Marshal.GetDelegateForFunctionPointer<NegateFloat64BitsExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.NegateFloat64Bits"));

        uint[] float32Inputs = [
            0, 0x80000000, 1, 0x80000001, 0x007FFFFF, 0x807FFFFF,
            0x00800000, 0x80800000, 0x3F800000, 0xBF800000,
            0x7F800000, 0xFF800000, 0x7F800001, 0xFF800001,
            0x7FC12345, 0xFFCABCDE,
        ];
        ulong[] float64Inputs = [
            0, 0x8000000000000000, 1, 0x8000000000000001,
            0x000FFFFFFFFFFFFF, 0x800FFFFFFFFFFFFF,
            0x0010000000000000, 0x8010000000000000,
            0x3FF0000000000000, 0xBFF0000000000000,
            0x7FF0000000000000, 0xFFF0000000000000,
            0x7FF0000000000001, 0xFFF0000000000001,
            0x7FF8123456789ABC, 0xFFFABCDE12345678,
        ];

        var random = new Random(0x4E6A7E);
        for (var index = 0; index < 128; index++)
        {
            Array.Resize(ref float32Inputs, float32Inputs.Length + 1);
            float32Inputs[^1] = (uint)random.NextInt64(0, 1L << 32);
            Array.Resize(ref float64Inputs, float64Inputs.Length + 1);
            float64Inputs[^1] = NextUInt64ForAbi(random);
        }

        foreach (var bits in float32Inputs)
        {
            var result = negateFloat32(bits);
            Assert.That(result, Is.EqualTo(bits ^ 0x80000000));
            Assert.That(negateFloat32(result), Is.EqualTo(bits));
        }
        foreach (var bits in float64Inputs)
        {
            var result = negateFloat64(bits);
            Assert.That(result, Is.EqualTo(bits ^ 0x8000000000000000));
            Assert.That(negateFloat64(result), Is.EqualTo(bits));
        }
    }
}
