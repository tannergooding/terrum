// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32ArithmeticBitsExport(int left, int right);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float64ArithmeticBitsExport(long left, long right);

    private static void AssertFloatingArithmeticExports(IntPtr handle)
    {
        var oracleSyntaxTree = SyntaxTree.Parse("""
            [NoThrow] public float32 Float32Add(float32 left, float32 right) { return left + right; }
            [NoThrow] public float32 Float32Subtract(float32 left, float32 right) { return left - right; }
            [NoThrow] public float32 Float32Multiply(float32 left, float32 right) { return left * right; }
            [NoThrow] public float32 Float32Divide(float32 left, float32 right) { return left / right; }
            [NoThrow] public float64 Float64Add(float64 left, float64 right) { return left + right; }
            [NoThrow] public float64 Float64Subtract(float64 left, float64 right) { return left - right; }
            [NoThrow] public float64 Float64Multiply(float64 left, float64 right) { return left * right; }
            [NoThrow] public float64 Float64Divide(float64 left, float64 right) { return left / right; }
            """, "floating-arithmetic-native-abi-oracle.terrum");
        Assert.That(oracleSyntaxTree.HasErrors, Is.False, string.Join(Environment.NewLine, oracleSyntaxTree.Diagnostics));
        var oracleCompilation = Compilation.CreateLibrary(oracleSyntaxTree);
        Assert.That(oracleCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, oracleCompilation.Diagnostics));
        var oracleProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = oracleCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();

        var oraclePath = Path.Combine(Path.GetTempPath(), $"terrum-floating-arithmetic-abi-{Guid.NewGuid():N}.dll");
        IntPtr oracleHandle = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(oraclePath, NativeEmitter.EmitLibrary(oracleProgram, [
                "Float32Add", "Float32Subtract", "Float32Multiply", "Float32Divide",
                "Float64Add", "Float64Subtract", "Float64Multiply", "Float64Divide",
            ], NativeRuntimePolicy.FailFast));
            oracleHandle = NativeLibrary.Load(oraclePath);

            var float32Operations = new (string Name, Float32ArithmeticBitsExport Candidate, Float32ArithmeticBitsExport Oracle)[]
            {
                ("Float32AddBits", LoadFloat32(handle, "Terrum.CodeAnalysis.Float32AddBits"), LoadFloat32(oracleHandle, "Float32Add")),
                ("Float32SubtractBits", LoadFloat32(handle, "Terrum.CodeAnalysis.Float32SubtractBits"), LoadFloat32(oracleHandle, "Float32Subtract")),
                ("Float32MultiplyBits", LoadFloat32(handle, "Terrum.CodeAnalysis.Float32MultiplyBits"), LoadFloat32(oracleHandle, "Float32Multiply")),
                ("Float32DivideBits", LoadFloat32(handle, "Terrum.CodeAnalysis.Float32DivideBits"), LoadFloat32(oracleHandle, "Float32Divide")),
            };
            var float64Operations = new (string Name, Float64ArithmeticBitsExport Candidate, Float64ArithmeticBitsExport Oracle)[]
            {
                ("Float64AddBits", LoadFloat64(handle, "Terrum.CodeAnalysis.Float64AddBits"), LoadFloat64(oracleHandle, "Float64Add")),
                ("Float64SubtractBits", LoadFloat64(handle, "Terrum.CodeAnalysis.Float64SubtractBits"), LoadFloat64(oracleHandle, "Float64Subtract")),
                ("Float64MultiplyBits", LoadFloat64(handle, "Terrum.CodeAnalysis.Float64MultiplyBits"), LoadFloat64(oracleHandle, "Float64Multiply")),
                ("Float64DivideBits", LoadFloat64(handle, "Terrum.CodeAnalysis.Float64DivideBits"), LoadFloat64(oracleHandle, "Float64Divide")),
            };

            uint[] float32Boundaries = [
                0, 0x80000000, 1, 0x80000001, 0x007FFFFF, 0x807FFFFF,
                0x00800000, 0x80800000, 0x3F800000, 0x3F800001, 0xBF800000,
                0xBF800001, 0x3F000000, 0x3F7FFFFF, 0x3FC00000, 0x40000000,
                0x7F7FFFFF, 0xFF7FFFFF, 0x7F800000, 0xFF800000,
                0x7F800001, 0xFF800001, 0x7FC12345, 0xFFCABCDE,
            ];
            var float32Pairs = new List<(uint Left, uint Right)>();
            foreach (var left in float32Boundaries)
            {
                foreach (var right in float32Boundaries)
                {
                    float32Pairs.Add((left, right));
                }
            }

            ulong[] float64Boundaries = [
                0, 0x8000000000000000, 1, 0x8000000000000001,
                0x000FFFFFFFFFFFFF, 0x800FFFFFFFFFFFFF,
                0x0010000000000000, 0x8010000000000000,
                0x3FF0000000000000, 0x3FF0000000000001,
                0xBFF0000000000000, 0xBFF0000000000001,
                0x3FE0000000000000, 0x3FEFFFFFFFFFFFFF,
                0x3FF8000000000000, 0x4000000000000000,
                0x7FEFFFFFFFFFFFFF, 0xFFEFFFFFFFFFFFFF,
                0x7FF0000000000000, 0xFFF0000000000000,
                0x7FF0000000000001, 0xFFF0000000000001,
                0x7FF8000000001234, 0xFFFABCDE00000000,
            ];
            var float64Pairs = new List<(ulong Left, ulong Right)>();
            foreach (var left in float64Boundaries)
            {
                foreach (var right in float64Boundaries)
                {
                    float64Pairs.Add((left, right));
                }
            }

            var random = new Random(0xF10A7A8);
            for (var index = 0; index < 128; index++)
            {
                float32Pairs.Add((
                    (uint)random.NextInt64(0, 1L << 32),
                    (uint)random.NextInt64(0, 1L << 32)));
                float64Pairs.Add((NextUInt64ForAbi(random), NextUInt64ForAbi(random)));
            }

            foreach (var (name, candidate, oracle) in float32Operations)
            {
                foreach (var pair in float32Pairs)
                {
                    var expected = unchecked((uint)oracle(unchecked((int)pair.Left), unchecked((int)pair.Right)));
                    var actual = unchecked((uint)candidate(unchecked((int)pair.Left), unchecked((int)pair.Right)));
                    if (IsFloat32NaN(expected))
                    {
                        Assert.That(IsFloat32NaN(actual), Is.True, $"{name}({pair.Left:X8}, {pair.Right:X8}) was not NaN.");
                    }
                    else
                    {
                        Assert.That(actual, Is.EqualTo(expected));
                    }
                }
            }
            foreach (var (name, candidate, oracle) in float64Operations)
            {
                foreach (var pair in float64Pairs)
                {
                    var expected = unchecked((ulong)oracle(unchecked((long)pair.Left), unchecked((long)pair.Right)));
                    var actual = unchecked((ulong)candidate(unchecked((long)pair.Left), unchecked((long)pair.Right)));
                    if (IsFloat64NaN(expected))
                    {
                        Assert.That(IsFloat64NaN(actual), Is.True, $"{name}({pair.Left:X16}, {pair.Right:X16}) was not NaN.");
                    }
                    else
                    {
                        Assert.That(actual, Is.EqualTo(expected));
                    }
                }
            }
        }
        finally
        {
            if (oracleHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(oracleHandle);
            }

            File.Delete(oraclePath);
        }
    }

    private static Float32ArithmeticBitsExport LoadFloat32(IntPtr handle, string name)
    {
        return Marshal.GetDelegateForFunctionPointer<Float32ArithmeticBitsExport>(
            NativeLibrary.GetExport(handle, name));
    }

    private static Float64ArithmeticBitsExport LoadFloat64(IntPtr handle, string name)
    {
        return Marshal.GetDelegateForFunctionPointer<Float64ArithmeticBitsExport>(
            NativeLibrary.GetExport(handle, name));
    }

    private static bool IsFloat32NaN(uint bits)
    {
        return (bits & 0x7F800000) == 0x7F800000 && (bits & 0x007FFFFF) != 0;
    }

    private static bool IsFloat64NaN(ulong bits)
    {
        return (bits & 0x7FF0000000000000) == 0x7FF0000000000000
            && (bits & 0x000FFFFFFFFFFFFF) != 0;
    }
}
