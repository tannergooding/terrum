// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Utf16ScalarDecoderExport(ushort first, ushort second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Utf16ScalarWidthExport(int scalar);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HexDigitExport(ushort character, ref int digit);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Char16PredicateExport(ushort character);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Char16ValueExport(ushort character);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSourceRange
    {
        public int Start;
        public int Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuardedUInt64
    {
        public ulong Before;
        public ulong Value;
        public ulong After;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuardedUInt32
    {
        public uint Before;
        public uint Value;
        public uint After;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuardedInt32
    {
        public int Before;
        public int Value;
        public int After;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuardedInt64
    {
        public long Before;
        public long Value;
        public long After;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuardedSourceRange
    {
        public int Before;
        public NativeSourceRange Range;
        public int After;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SourceRangeTryCreateExport(int start, int length, ref NativeSourceRange range);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong MagnitudeInt64Export(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TryAccumulateDecimalExport(ulong value, uint digit, ulong maximum, ref ulong result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TryAccumulateBaseExport(
        ulong value, uint digit, uint radix, ulong maximum, ref ulong result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SignedInt64BinaryExport(long left, long right, ref long result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UnsignedUInt64BinaryExport(ulong left, ulong right, ref ulong result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int64UnaryExport(long value, ref long result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt32IntUnaryExport(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint UInt32UnaryExport(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint UInt32BinaryExport(uint value, int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt32PredicateExport(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt32RoundUpExport(uint value, ref uint result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt64IntUnaryExport(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong UInt64UnaryExport(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong UInt64BinaryExport(ulong value, int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt64PredicateExport(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt64RoundUpExport(ulong value, ref ulong result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32UnaryMathExport(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32TryAbsMathExport(int value, ref int result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32BinaryMathExport(int left, int right);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32ClampMathExport(int value, int minimum, int maximum);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Int32ProductMathExport(int left, int right);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Int32GcdMathExport(int left, int right);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Int64UnaryMathExport(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int64SignMathExport(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int64TryAbsMathExport(long value, ref long result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Int64BinaryMathExport(long left, long right);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Int64ClampMathExport(long value, long minimum, long maximum);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Int64GcdMathExport(long left, long right);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryBuildPublishesSelectedExportsWithoutAnEntryPointAsync(bool multipleSources)
    {
        const string Entry = "public int32 Value() { return Helper(); }";
        const string Helper = "int32 Helper() { return 42; }";
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-library-cli-{Guid.NewGuid():N}");
        var outputPath = prefix + ".dll";
        var helperPath = prefix + ".terrum";
        try
        {
            List<string> arguments = ["build-library", "-", "--export", "Value", "-o", outputPath, "--runtime", "fail-fast"];
            if (multipleSources)
            {
                await File.WriteAllTextAsync(helperPath, Helper);
                arguments.AddRange(["--source", helperPath]);
            }
            arguments.Add("--json");
            var input = multipleSources ? Entry : Entry + Helper;
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(input), [.. arguments]);
            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(multipleSources ? 2 : 1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("dynamic-library", document.RootElement.GetProperty("artifact").GetProperty("kind").GetString());
            var bytes = await File.ReadAllBytesAsync(outputPath);
            var compilation = multipleSources
                ? Compilation.CreateLibrary([SyntaxTree.Parse(Entry, "<stdin>"), SyntaxTree.Parse(Helper, helperPath)])
                : Compilation.CreateLibrary(SyntaxTree.Parse(input, "<stdin>"));
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.Equal(NativeEmitter.EmitLibrary(Assert.IsType<BoundProgram>(compilation.Program),
                ["Value"], NativeRuntimePolicy.FailFast), bytes);
            using var stream = new MemoryStream(bytes);
            using var reader = new PEReader(stream);
            var header = Assert.IsType<PEHeader>(reader.PEHeaders.PEHeader);
            Assert.Equal(0, header.AddressOfEntryPoint);
            Assert.True(reader.PEHeaders.IsDll);
            Assert.NotEqual(0, header.ExportTableDirectory.Size);
        }
        finally
        {
            File.Delete(outputPath);
            File.Delete(helperPath);
        }
    }

    private static void AssertWideIntegerAccumulatorExports(nint handle)
    {
        const ulong lowerCanary = 0x13579BDF2468ACE0UL;
        const ulong outputSentinel = 0xFEDCBA9876543210UL;
        const ulong upperCanary = 0x0ECA86425A5A5A5AUL;
        Assert.Equal(24, Marshal.SizeOf<GuardedUInt64>());
        Assert.Equal(0, Marshal.OffsetOf<GuardedUInt64>(nameof(GuardedUInt64.Before)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<GuardedUInt64>(nameof(GuardedUInt64.Value)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<GuardedUInt64>(nameof(GuardedUInt64.After)).ToInt32());

        var decimalAccumulator = Marshal.GetDelegateForFunctionPointer<TryAccumulateDecimalExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.TryAccumulateDecimal"));
        var baseAccumulator = Marshal.GetDelegateForFunctionPointer<TryAccumulateBaseExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.TryAccumulateBase"));
        ulong[] maxima = [
            0, 1, 9, 10, uint.MaxValue - 1UL, uint.MaxValue, (ulong)uint.MaxValue + 1,
            long.MaxValue - 1, long.MaxValue, (ulong)long.MaxValue + 1,
            ulong.MaxValue - 1, ulong.MaxValue,
        ];

        void AssertResult(bool expectedSuccess, ulong expected, int actual, GuardedUInt64 guarded)
        {
            Assert.Equal(expectedSuccess ? 1 : 0, actual);
            Assert.Equal(expectedSuccess ? expected : outputSentinel, guarded.Value);
            Assert.Equal(lowerCanary, guarded.Before);
            Assert.Equal(upperCanary, guarded.After);
        }

        bool TryGetExpected(ulong value, uint digit, uint radix, ulong maximum, out ulong expected)
        {
            if (radix is < 2 or > 36 || digit >= radix)
            {
                expected = 0;
                return false;
            }

            var result = new BigInteger(value) * radix + digit;
            if (result > new BigInteger(maximum))
            {
                expected = 0;
                return false;
            }

            expected = (ulong)result;
            return true;
        }

        void AssertDecimal(ulong value, uint digit, ulong maximum)
        {
            var expectedSuccess = TryGetExpected(value, digit, 10, maximum, out var expected);
            var guarded = new GuardedUInt64 {
                Before = lowerCanary,
                Value = outputSentinel,
                After = upperCanary,
            };
            var actual = decimalAccumulator(value, digit, maximum, ref guarded.Value);
            AssertResult(expectedSuccess, expected, actual, guarded);
        }

        void AssertBase(ulong value, uint digit, uint radix, ulong maximum)
        {
            var expectedSuccess = TryGetExpected(value, digit, radix, maximum, out var expected);
            var guarded = new GuardedUInt64 {
                Before = lowerCanary,
                Value = outputSentinel,
                After = upperCanary,
            };
            var actual = baseAccumulator(value, digit, radix, maximum, ref guarded.Value);
            AssertResult(expectedSuccess, expected, actual, guarded);
        }

        foreach (var maximum in maxima)
        {
            var cutoff = maximum / 10;
            ulong[] values = [
                0, 1, maximum, cutoff == 0 ? 0 : cutoff - 1, cutoff, cutoff + 1,
            ];
            foreach (var value in values.Distinct())
            {
                for (uint digit = 0; digit <= 10; digit++)
                {
                    AssertDecimal(value, digit, maximum);
                }
            }
            AssertDecimal(cutoff, uint.MaxValue, maximum);
        }

        for (uint radix = 2; radix <= 36; radix++)
        {
            foreach (var maximum in maxima)
            {
                var cutoff = maximum / radix;
                ulong[] values = [
                    0, 1, maximum, cutoff == 0 ? 0 : cutoff - 1, cutoff, cutoff + 1,
                ];
                foreach (var value in values.Distinct())
                {
                    for (uint digit = 0; digit <= radix; digit++)
                    {
                        AssertBase(value, digit, radix, maximum);
                    }
                }
                AssertBase(cutoff, uint.MaxValue, radix, maximum);
            }
        }

        foreach (var radix in new uint[] { 0, 1, 37, uint.MaxValue })
        {
            foreach (var maximum in maxima)
            {
                foreach (var digit in new uint[] { 0, 1, 10, uint.MaxValue })
                {
                    AssertBase(0, digit, radix, maximum);
                }
            }
        }
    }

    private static void AssertWideIntegerArithmeticExports(nint handle)
    {
        const long lowerSignedCanary = 0x13579BDF2468ACE0L;
        const long signedOutputSentinel = unchecked((long)0xFEDCBA9876543210UL);
        const long upperSignedCanary = 0x0ECA86425A5A5A5AL;
        const ulong lowerUnsignedCanary = 0x13579BDF2468ACE0UL;
        const ulong unsignedOutputSentinel = 0xFEDCBA9876543210UL;
        const ulong upperUnsignedCanary = 0x0ECA86425A5A5A5AUL;
        const string ExportPrefix = "Terrum.CodeAnalysis.Numerics64.";

        Assert.Equal(24, Marshal.SizeOf<GuardedInt64>());
        Assert.Equal(0, Marshal.OffsetOf<GuardedInt64>(nameof(GuardedInt64.Before)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<GuardedInt64>(nameof(GuardedInt64.Value)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<GuardedInt64>(nameof(GuardedInt64.After)).ToInt32());

        SignedInt64BinaryExport GetSignedBinary(string name) =>
            Marshal.GetDelegateForFunctionPointer<SignedInt64BinaryExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        UnsignedUInt64BinaryExport GetUnsignedBinary(string name) =>
            Marshal.GetDelegateForFunctionPointer<UnsignedUInt64BinaryExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));

        var signedOperations = new (string Name, SignedInt64BinaryExport Function)[] {
            ("TryAddInt64", GetSignedBinary("TryAddInt64")),
            ("TrySubtractInt64", GetSignedBinary("TrySubtractInt64")),
            ("TryMultiplyInt64", GetSignedBinary("TryMultiplyInt64")),
            ("TryDivideInt64", GetSignedBinary("TryDivideInt64")),
            ("TryRemainderInt64", GetSignedBinary("TryRemainderInt64")),
        };
        var unsignedOperations = new (string Name, UnsignedUInt64BinaryExport Function)[] {
            ("TryAddUInt64", GetUnsignedBinary("TryAddUInt64")),
            ("TrySubtractUInt64", GetUnsignedBinary("TrySubtractUInt64")),
            ("TryMultiplyUInt64", GetUnsignedBinary("TryMultiplyUInt64")),
            ("TryDivideUInt64", GetUnsignedBinary("TryDivideUInt64")),
            ("TryRemainderUInt64", GetUnsignedBinary("TryRemainderUInt64")),
        };
        var negate = Marshal.GetDelegateForFunctionPointer<Int64UnaryExport>(
            NativeLibrary.GetExport(handle, ExportPrefix + "TryNegateInt64"));
        var absolute = Marshal.GetDelegateForFunctionPointer<Int64UnaryExport>(
            NativeLibrary.GetExport(handle, ExportPrefix + "TryAbsoluteInt64"));
        long[] signedValues = [
            long.MinValue, long.MinValue + 1, -4294967297L, -4294967296L, -4294967295L,
            int.MinValue - 1L, int.MinValue, int.MinValue + 1L,
            -3037000501L, -3037000500L, -3037000499L, -1, 0, 1,
            3037000499L, 3037000500L, 3037000501L,
            int.MaxValue - 1L, int.MaxValue, int.MaxValue + 1L,
            4294967295L, 4294967296L, 4294967297L, long.MaxValue - 1, long.MaxValue,
        ];
        ulong[] unsignedValues = [
            0, 1, 2, uint.MaxValue - 1UL, uint.MaxValue, (ulong)uint.MaxValue + 1,
            (ulong)uint.MaxValue + 2, long.MaxValue - 1UL, long.MaxValue,
            (ulong)long.MaxValue + 1, (ulong)long.MaxValue + 2, ulong.MaxValue - 1, ulong.MaxValue,
        ];

        bool TryExpectedSigned(string operation, long leftValue, long rightValue, out long expected)
        {
            var left = new BigInteger(leftValue);
            var right = new BigInteger(rightValue);
            BigInteger result;
            switch (operation)
            {
                case "TryAddInt64":
                    result = left + right;
                    break;
                case "TrySubtractInt64":
                    result = left - right;
                    break;
                case "TryMultiplyInt64":
                    result = left * right;
                    break;
                case "TryDivideInt64":
                    if (rightValue == 0 || (leftValue == long.MinValue && rightValue == -1))
                    {
                        expected = 0;
                        return false;
                    }
                    result = left / right;
                    break;
                case "TryRemainderInt64":
                    if (rightValue == 0)
                    {
                        expected = 0;
                        return false;
                    }
                    result = left % right;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }

            if (result < long.MinValue || result > long.MaxValue)
            {
                expected = 0;
                return false;
            }

            expected = (long)result;
            return true;
        }

        bool TryExpectedUnsigned(string operation, ulong leftValue, ulong rightValue, out ulong expected)
        {
            var left = new BigInteger(leftValue);
            var right = new BigInteger(rightValue);
            BigInteger result;
            switch (operation)
            {
                case "TryAddUInt64":
                    result = left + right;
                    break;
                case "TrySubtractUInt64":
                    result = left - right;
                    break;
                case "TryMultiplyUInt64":
                    result = left * right;
                    break;
                case "TryDivideUInt64":
                    if (rightValue == 0)
                    {
                        expected = 0;
                        return false;
                    }
                    result = left / right;
                    break;
                case "TryRemainderUInt64":
                    if (rightValue == 0)
                    {
                        expected = 0;
                        return false;
                    }
                    result = left % right;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }

            if (result < BigInteger.Zero || result > ulong.MaxValue)
            {
                expected = 0;
                return false;
            }

            expected = (ulong)result;
            return true;
        }

        foreach (var (name, function) in signedOperations)
        {
            foreach (var left in signedValues)
            {
                foreach (var right in signedValues)
                {
                    var expectedSuccess = TryExpectedSigned(name, left, right, out var expected);
                    var guarded = new GuardedInt64 {
                        Before = lowerSignedCanary,
                        Value = signedOutputSentinel,
                        After = upperSignedCanary,
                    };
                    var actual = function(left, right, ref guarded.Value);
                    Assert.Equal(expectedSuccess ? 1 : 0, actual);
                    Assert.Equal(expectedSuccess ? expected : signedOutputSentinel, guarded.Value);
                    Assert.Equal(lowerSignedCanary, guarded.Before);
                    Assert.Equal(upperSignedCanary, guarded.After);
                }
            }
        }

        foreach (var (name, function) in unsignedOperations)
        {
            foreach (var left in unsignedValues)
            {
                foreach (var right in unsignedValues)
                {
                    var expectedSuccess = TryExpectedUnsigned(name, left, right, out var expected);
                    var guarded = new GuardedUInt64 {
                        Before = lowerUnsignedCanary,
                        Value = unsignedOutputSentinel,
                        After = upperUnsignedCanary,
                    };
                    var actual = function(left, right, ref guarded.Value);
                    Assert.Equal(expectedSuccess ? 1 : 0, actual);
                    Assert.Equal(expectedSuccess ? expected : unsignedOutputSentinel, guarded.Value);
                    Assert.Equal(lowerUnsignedCanary, guarded.Before);
                    Assert.Equal(upperUnsignedCanary, guarded.After);
                }
            }
        }

        foreach (var value in signedValues)
        {
            var bigValue = new BigInteger(value);
            var expectedNegation = value == long.MinValue ? 0 : (long)-bigValue;
            var expectedSuccess = value != long.MinValue;
            var negateGuard = new GuardedInt64 {
                Before = lowerSignedCanary,
                Value = signedOutputSentinel,
                After = upperSignedCanary,
            };
            var negateResult = negate(value, ref negateGuard.Value);
            Assert.Equal(expectedSuccess ? 1 : 0, negateResult);
            Assert.Equal(expectedSuccess ? expectedNegation : signedOutputSentinel, negateGuard.Value);
            Assert.Equal(lowerSignedCanary, negateGuard.Before);
            Assert.Equal(upperSignedCanary, negateGuard.After);

            var expectedAbsolute = value == long.MinValue ? 0 : (long)BigInteger.Abs(bigValue);
            var absoluteGuard = new GuardedInt64 {
                Before = lowerSignedCanary,
                Value = signedOutputSentinel,
                After = upperSignedCanary,
            };
            var absoluteResult = absolute(value, ref absoluteGuard.Value);
            Assert.Equal(expectedSuccess ? 1 : 0, absoluteResult);
            Assert.Equal(expectedSuccess ? expectedAbsolute : signedOutputSentinel, absoluteGuard.Value);
            Assert.Equal(lowerSignedCanary, absoluteGuard.Before);
            Assert.Equal(upperSignedCanary, absoluteGuard.After);
        }
    }

    private static void AssertBitOperationsExports(nint handle)
    {
        const uint Lower32Canary = 0x13579BDF;
        const uint UInt32OutputSentinel = 0xFEDCBA98;
        const uint Upper32Canary = 0x0ECA8642;
        const ulong Lower64Canary = 0x13579BDF2468ACE0UL;
        const ulong UInt64OutputSentinel = 0xFEDCBA9876543210UL;
        const ulong Upper64Canary = 0x0ECA86425A5A5A5AUL;
        const string ExportPrefix = "Terrum.CodeAnalysis.Numerics64.";

        Assert.Equal(12, Marshal.SizeOf<GuardedUInt32>());
        Assert.Equal(0, Marshal.OffsetOf<GuardedUInt32>(nameof(GuardedUInt32.Before)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<GuardedUInt32>(nameof(GuardedUInt32.Value)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<GuardedUInt32>(nameof(GuardedUInt32.After)).ToInt32());
        Assert.Equal(24, Marshal.SizeOf<GuardedUInt64>());
        Assert.Equal(0, Marshal.OffsetOf<GuardedUInt64>(nameof(GuardedUInt64.Before)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<GuardedUInt64>(nameof(GuardedUInt64.Value)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<GuardedUInt64>(nameof(GuardedUInt64.After)).ToInt32());

        UInt32IntUnaryExport GetUInt32Int(string name) =>
            Marshal.GetDelegateForFunctionPointer<UInt32IntUnaryExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        UInt32UnaryExport GetUInt32(string name) =>
            Marshal.GetDelegateForFunctionPointer<UInt32UnaryExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        UInt32BinaryExport GetUInt32Binary(string name) =>
            Marshal.GetDelegateForFunctionPointer<UInt32BinaryExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        UInt32PredicateExport GetUInt32Predicate(string name) =>
            Marshal.GetDelegateForFunctionPointer<UInt32PredicateExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        UInt32RoundUpExport GetUInt32RoundUp(string name) =>
            Marshal.GetDelegateForFunctionPointer<UInt32RoundUpExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        UInt64IntUnaryExport GetUInt64Int(string name) =>
            Marshal.GetDelegateForFunctionPointer<UInt64IntUnaryExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        UInt64UnaryExport GetUInt64(string name) =>
            Marshal.GetDelegateForFunctionPointer<UInt64UnaryExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        UInt64BinaryExport GetUInt64Binary(string name) =>
            Marshal.GetDelegateForFunctionPointer<UInt64BinaryExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        UInt64PredicateExport GetUInt64Predicate(string name) =>
            Marshal.GetDelegateForFunctionPointer<UInt64PredicateExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        UInt64RoundUpExport GetUInt64RoundUp(string name) =>
            Marshal.GetDelegateForFunctionPointer<UInt64RoundUpExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));

        var leadingZeroCount32 = GetUInt32Int("LeadingZeroCountUInt32");
        var trailingZeroCount32 = GetUInt32Int("TrailingZeroCountUInt32");
        var popCount32 = GetUInt32Int("PopCountUInt32");
        var rotateLeft32 = GetUInt32Binary("RotateLeftUInt32");
        var rotateRight32 = GetUInt32Binary("RotateRightUInt32");
        var reverseBits32 = GetUInt32("ReverseBitsUInt32");
        var reverseEndianness32 = GetUInt32("ReverseEndiannessUInt32");
        var isPow232 = GetUInt32Predicate("IsPow2UInt32");
        var roundUp32 = GetUInt32RoundUp("TryRoundUpToPowerOfTwoUInt32");
        var leadingZeroCount64 = GetUInt64Int("LeadingZeroCountUInt64");
        var trailingZeroCount64 = GetUInt64Int("TrailingZeroCountUInt64");
        var popCount64 = GetUInt64Int("PopCountUInt64");
        var rotateLeft64 = GetUInt64Binary("RotateLeftUInt64");
        var rotateRight64 = GetUInt64Binary("RotateRightUInt64");
        var reverseBits64 = GetUInt64("ReverseBitsUInt64");
        var reverseEndianness64 = GetUInt64("ReverseEndiannessUInt64");
        var isPow264 = GetUInt64Predicate("IsPow2UInt64");
        var roundUp64 = GetUInt64RoundUp("TryRoundUpToPowerOfTwoUInt64");

        var values32 = new List<uint> { 0, 1, 2, 3, 0x01234567, 0xAAAAAAAA, 0x55555555, uint.MaxValue };
        var values64 = new List<ulong> {
            0, 1, 2, 3, 0x0123456789ABCDEF, 0xAAAAAAAAAAAAAAAA, 0x5555555555555555, ulong.MaxValue,
        };
        for (var bit = 0; bit < 32; bit++)
        {
            var oneBit = 1U << bit;
            values32.Add(oneBit);
            values32.Add(~oneBit);
        }
        for (var bit = 0; bit < 64; bit++)
        {
            var oneBit = 1UL << bit;
            values64.Add(oneBit);
            values64.Add(~oneBit);
        }

        int[] rotationCounts = [int.MinValue, -65, -64, -33, -32, -1, 0, 1, 31, 32, 33, 63, 64, 65, int.MaxValue];
        foreach (var value in values32)
        {
            Assert.Equal(BitOperations.LeadingZeroCount(value), leadingZeroCount32(value));
            Assert.Equal(BitOperations.TrailingZeroCount(value), trailingZeroCount32(value));
            Assert.Equal(BitOperations.PopCount(value), popCount32(value));
            Assert.Equal(ReverseBits32(value), reverseBits32(value));
            Assert.Equal(BinaryPrimitives.ReverseEndianness(value), reverseEndianness32(value));
            Assert.Equal(BitOperations.IsPow2(value) ? 1 : 0, isPow232(value));
            foreach (var count in rotationCounts)
            {
                Assert.Equal(BitOperations.RotateLeft(value, count), rotateLeft32(value, count));
                Assert.Equal(BitOperations.RotateRight(value, count), rotateRight32(value, count));
            }
        }

        foreach (var value in values64)
        {
            Assert.Equal(BitOperations.LeadingZeroCount(value), leadingZeroCount64(value));
            Assert.Equal(BitOperations.TrailingZeroCount(value), trailingZeroCount64(value));
            Assert.Equal(BitOperations.PopCount(value), popCount64(value));
            Assert.Equal(ReverseBits64(value), reverseBits64(value));
            Assert.Equal(BinaryPrimitives.ReverseEndianness(value), reverseEndianness64(value));
            Assert.Equal(BitOperations.IsPow2(value) ? 1 : 0, isPow264(value));
            foreach (var count in rotationCounts)
            {
                Assert.Equal(BitOperations.RotateLeft(value, count), rotateLeft64(value, count));
                Assert.Equal(BitOperations.RotateRight(value, count), rotateRight64(value, count));
            }
        }

        var roundUpValues32 = new List<uint> { 0, 1, uint.MaxValue };
        for (var bit = 0; bit < 32; bit++)
        {
            var power = 1U << bit;
            roundUpValues32.Add(power - 1);
            roundUpValues32.Add(power);
            roundUpValues32.Add(power + 1);
        }
        foreach (var value in roundUpValues32)
        {
            var expected = BitOperations.RoundUpToPowerOf2(value);
            var expectedSuccess = expected != 0;
            var guarded = new GuardedUInt32 {
                Before = Lower32Canary,
                Value = UInt32OutputSentinel,
                After = Upper32Canary,
            };
            var actual = roundUp32(value, ref guarded.Value);
            Assert.Equal(expectedSuccess ? 1 : 0, actual);
            Assert.Equal(expectedSuccess ? expected : UInt32OutputSentinel, guarded.Value);
            Assert.Equal(Lower32Canary, guarded.Before);
            Assert.Equal(Upper32Canary, guarded.After);
        }

        var roundUpValues64 = new List<ulong> { 0, 1, ulong.MaxValue };
        for (var bit = 0; bit < 64; bit++)
        {
            var power = 1UL << bit;
            roundUpValues64.Add(power - 1);
            roundUpValues64.Add(power);
            roundUpValues64.Add(power + 1);
        }
        foreach (var value in roundUpValues64)
        {
            var expected = BitOperations.RoundUpToPowerOf2(value);
            var expectedSuccess = expected != 0;
            var guarded = new GuardedUInt64 {
                Before = Lower64Canary,
                Value = UInt64OutputSentinel,
                After = Upper64Canary,
            };
            var actual = roundUp64(value, ref guarded.Value);
            Assert.Equal(expectedSuccess ? 1 : 0, actual);
            Assert.Equal(expectedSuccess ? expected : UInt64OutputSentinel, guarded.Value);
            Assert.Equal(Lower64Canary, guarded.Before);
            Assert.Equal(Upper64Canary, guarded.After);
        }

        static uint ReverseBits32(uint value)
        {
            uint reversed = 0;
            for (var bit = 0; bit < 32; bit++)
            {
                reversed = (reversed << 1) | (value & 1);
                value >>= 1;
            }
            return reversed;
        }

        static ulong ReverseBits64(ulong value)
        {
            ulong reversed = 0;
            for (var bit = 0; bit < 64; bit++)
            {
                reversed = (reversed << 1) | (value & 1);
                value >>= 1;
            }
            return reversed;
        }
    }

    private static void AssertIntegerMathExports(nint handle)
    {
        const int LowerInt32Canary = 0x13579BDF;
        const int Int32OutputSentinel = unchecked((int)0xA5A55A5A);
        const int UpperInt32Canary = 0x0ECA8642;
        const long LowerInt64Canary = 0x13579BDF2468ACE0L;
        const long Int64OutputSentinel = unchecked((long)0xFEDCBA9876543210UL);
        const long UpperInt64Canary = 0x0ECA86425A5A5A5AL;
        const string ExportPrefix = "Terrum.CodeAnalysis.Numerics64.";

        Assert.Equal(12, Marshal.SizeOf<GuardedInt32>());
        Assert.Equal(0, Marshal.OffsetOf<GuardedInt32>(nameof(GuardedInt32.Before)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<GuardedInt32>(nameof(GuardedInt32.Value)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<GuardedInt32>(nameof(GuardedInt32.After)).ToInt32());
        Assert.Equal(24, Marshal.SizeOf<GuardedInt64>());
        Assert.Equal(0, Marshal.OffsetOf<GuardedInt64>(nameof(GuardedInt64.Before)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<GuardedInt64>(nameof(GuardedInt64.Value)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<GuardedInt64>(nameof(GuardedInt64.After)).ToInt32());

        Int32UnaryMathExport GetInt32Unary(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int32UnaryMathExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        Int32TryAbsMathExport GetInt32TryAbs(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int32TryAbsMathExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        Int32BinaryMathExport GetInt32Binary(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int32BinaryMathExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        Int32ClampMathExport GetInt32Clamp(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int32ClampMathExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        Int32ProductMathExport GetInt32Product(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int32ProductMathExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        Int32GcdMathExport GetInt32Gcd(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int32GcdMathExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        Int64UnaryMathExport GetInt64Unary(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int64UnaryMathExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        Int64SignMathExport GetInt64Sign(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int64SignMathExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        Int64TryAbsMathExport GetInt64TryAbs(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int64TryAbsMathExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        Int64BinaryMathExport GetInt64Binary(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int64BinaryMathExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        Int64ClampMathExport GetInt64Clamp(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int64ClampMathExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        Int64GcdMathExport GetInt64Gcd(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int64GcdMathExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));

        var abs32 = GetInt32Unary("AbsInt32");
        var tryAbs32 = GetInt32TryAbs("TryAbsInt32");
        var sign32 = GetInt32Unary("SignInt32");
        var min32 = GetInt32Binary("MinInt32");
        var max32 = GetInt32Binary("MaxInt32");
        var clamp32 = GetInt32Clamp("ClampInt32");
        var multiply32 = GetInt32Product("BigMultiplyInt32");
        var gcd32 = GetInt32Gcd("GreatestCommonDivisorInt32");
        var abs64 = GetInt64Unary("AbsInt64");
        var tryAbs64 = GetInt64TryAbs("TryAbsInt64");
        var sign64 = GetInt64Sign("SignInt64");
        var min64 = GetInt64Binary("MinInt64");
        var max64 = GetInt64Binary("MaxInt64");
        var clamp64 = GetInt64Clamp("ClampInt64");
        var gcd64 = GetInt64Gcd("GreatestCommonDivisorInt64");

        int[] values32 = [
            int.MinValue, int.MinValue + 1, -65537, -17, -1, 0, 1, 17, 65537,
            int.MaxValue - 1, int.MaxValue,
        ];
        long[] values64 = [
            long.MinValue, long.MinValue + 1, -4294967297L, -3037000501L,
            -17, -1, 0, 1, 17, 3037000500L, 4294967295L,
            long.MaxValue - 1, long.MaxValue,
        ];

        foreach (var value in values32)
        {
            if (value != int.MinValue)
            {
                Assert.Equal(Math.Abs(value), abs32(value));
            }
            Assert.Equal(Math.Sign(value), sign32(value));

            var expectedSuccess = value != int.MinValue;
            var guarded = new GuardedInt32 {
                Before = LowerInt32Canary,
                Value = Int32OutputSentinel,
                After = UpperInt32Canary,
            };
            var actual = tryAbs32(value, ref guarded.Value);
            Assert.Equal(expectedSuccess ? 1 : 0, actual);
            Assert.Equal(expectedSuccess ? Math.Abs(value) : Int32OutputSentinel, guarded.Value);
            Assert.Equal(LowerInt32Canary, guarded.Before);
            Assert.Equal(UpperInt32Canary, guarded.After);
        }

        foreach (var left in values32)
        {
            foreach (var right in values32)
            {
                Assert.Equal(Math.Min(left, right), min32(left, right));
                Assert.Equal(Math.Max(left, right), max32(left, right));
                Assert.Equal((long)left * right, multiply32(left, right));
                var expectedGcd = BigInteger.GreatestCommonDivisor(
                    BigInteger.Abs(new BigInteger(left)), BigInteger.Abs(new BigInteger(right)));
                Assert.Equal((uint)expectedGcd, gcd32(left, right));
            }
        }

        (int Minimum, int Maximum)[] clampRanges32 = [
            (int.MinValue, int.MaxValue), (int.MinValue, -1), (-5, 5), (0, int.MaxValue), (int.MaxValue, int.MaxValue),
        ];
        foreach (var value in values32)
        {
            foreach (var (minimum, maximum) in clampRanges32)
            {
                Assert.Equal(Math.Clamp(value, minimum, maximum), clamp32(value, minimum, maximum));
            }
        }

        foreach (var value in values64)
        {
            if (value != long.MinValue)
            {
                Assert.Equal(Math.Abs(value), abs64(value));
            }
            Assert.Equal(Math.Sign(value), sign64(value));

            var expectedSuccess = value != long.MinValue;
            var guarded = new GuardedInt64 {
                Before = LowerInt64Canary,
                Value = Int64OutputSentinel,
                After = UpperInt64Canary,
            };
            var actual = tryAbs64(value, ref guarded.Value);
            Assert.Equal(expectedSuccess ? 1 : 0, actual);
            Assert.Equal(expectedSuccess ? Math.Abs(value) : Int64OutputSentinel, guarded.Value);
            Assert.Equal(LowerInt64Canary, guarded.Before);
            Assert.Equal(UpperInt64Canary, guarded.After);
        }

        foreach (var left in values64)
        {
            foreach (var right in values64)
            {
                Assert.Equal(Math.Min(left, right), min64(left, right));
                Assert.Equal(Math.Max(left, right), max64(left, right));
                var expectedGcd = BigInteger.GreatestCommonDivisor(
                    BigInteger.Abs(new BigInteger(left)), BigInteger.Abs(new BigInteger(right)));
                Assert.Equal((ulong)expectedGcd, gcd64(left, right));
            }
        }

        (long Minimum, long Maximum)[] clampRanges64 = [
            (long.MinValue, long.MaxValue), (long.MinValue, -1), (-5, 5), (0, long.MaxValue), (long.MaxValue, long.MaxValue),
        ];
        foreach (var value in values64)
        {
            foreach (var (minimum, maximum) in clampRanges64)
            {
                Assert.Equal(Math.Clamp(value, minimum, maximum), clamp64(value, minimum, maximum));
            }
        }
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Hidden")]
    [InlineData("\u03B1")]
    public async Task InvalidLibraryExportsPreservePriorArtifactsAsync(string export)
    {
        var path = Path.Combine(Path.GetTempPath(), $"terrum-library-export-{Guid.NewGuid():N}.dll");
        const string Source = "public int32 Value() { return 42; } int32 Hidden() { return 1; } public int32 \u03B1() { return 2; }";
        try
        {
            var (exit, _, _) = await RunAsync(Encoding.UTF8.GetBytes(Source), "build-library", "-", "--export", "Value",
                "-o", path, "--runtime", "fail-fast", "--json");
            Assert.Equal(0, exit);
            var original = await File.ReadAllBytesAsync(path);
            var (failed, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), "build-library", "-", "--export", export,
                "-o", path, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, failed);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("TRM4002", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.False(document.RootElement.TryGetProperty("artifact", out _));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("analyze")]
    [InlineData("build")]
    [InlineData("jit")]
    public async Task SourceSetsResolveNamespacesAndValueMembersAcrossFilesAsync(string command)
    {
        const string Entry = "using Parts; int32 Main() { Box value = new Box(10); return value.Total(32); }";
        const string Library = "namespace Parts; public struct Box { public int32 Value; public Box(int32 value) { Value = value; } public int32 Total(int32 extra) { return Value + extra; } }";
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-source-set-{Guid.NewGuid():N}");
        var library = prefix + ".terrum";
        var image = prefix + ".exe";
        try
        {
            await File.WriteAllTextAsync(library, Library);
            List<string> arguments = [command, "-", "--source", library];
            if (command == "build")
            {
                arguments.AddRange(["-o", image]);
            }
            if (command != "analyze")
            {
                arguments.AddRange(["--runtime", "fail-fast"]);
            }
            arguments.Add("--json");
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Entry), [.. arguments]);
            Assert.Empty(error);
            Assert.Equal(command == "jit" ? 42 : 0, exit);
            if (command == "jit")
            {
                Assert.Empty(output);
                return;
            }

            using var json = JsonDocument.Parse(output);
            Assert.Equal(2, json.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.True(json.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(command, json.RootElement.GetProperty("command").GetString());
            var sources = json.RootElement.GetProperty("sources");
            Assert.Equal(2, sources.GetArrayLength());
            Assert.Equal("<stdin>", sources[0].GetProperty("path").GetString());
            Assert.Equal(library, sources[1].GetProperty("path").GetString());
            Assert.Equal(Entry.Length, sources[0].GetProperty("sourceLength").GetInt32());
            Assert.Equal(Library.Length, sources[1].GetProperty("sourceLength").GetInt32());
            Assert.False(json.RootElement.TryGetProperty("contentId", out _));
            if (command == "build")
            {
                var bytes = await File.ReadAllBytesAsync(image);
                Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),
                    json.RootElement.GetProperty("artifact").GetProperty("sha256").GetString());
                using var process = Process.Start(new ProcessStartInfo(image) { UseShellExecute = false, CreateNoWindow = true })
                    ?? throw new InvalidOperationException("Could not start source-set artifact.");
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                    throw;
                }
                Assert.Equal(42, process.ExitCode);
            }
        }
        finally
        {
            File.Delete(library);
            File.Delete(image);
        }
    }

    [Theory]
    [InlineData("analyze", true)]
    [InlineData("build", true)]
    [InlineData("jit", true)]
    [InlineData("analyze", false)]
    public async Task SourceSetErrorsKeepTheirOwningSnapshotAndPreserveOutputAsync(string command, bool json)
    {
        const string Invalid = "int32 Helper() { return missing; }";
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-source-errors-{Guid.NewGuid():N}");
        var helper = prefix + ".terrum";
        var image = prefix + ".exe";
        byte[] original = [1, 2, 3];
        try
        {
            await File.WriteAllTextAsync(helper, Invalid);
            await File.WriteAllBytesAsync(image, original);
            List<string> arguments = [command, "-", "--source", helper];
            if (command == "build")
            {
                arguments.AddRange(["-o", image]);
            }
            if (command != "analyze")
            {
                arguments.AddRange(["--runtime", "fail-fast"]);
            }
            if (json)
            {
                arguments.Add("--json");
            }
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { return Helper(); }"), [.. arguments]);
            Assert.Equal(1, exit);
            Assert.Equal(original, await File.ReadAllBytesAsync(image));
            if (json)
            {
                Assert.Empty(error);
                using var document = JsonDocument.Parse(output);
                Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.False(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal(helper, document.RootElement.GetProperty("sources")[1].GetProperty("path").GetString());
                var diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
                Assert.Equal(1, diagnostic.GetProperty("sourceIndex").GetInt32());
                Assert.Equal(Invalid.IndexOf("missing", StringComparison.Ordinal),
                    diagnostic.GetProperty("span").GetProperty("start").GetInt32());
                Assert.False(document.RootElement.TryGetProperty("artifact", out _));
            }
            else
            {
                Assert.Empty(output);
                Assert.Contains(helper + "(1,", error, StringComparison.Ordinal);
                Assert.Contains("missing", error, StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(helper);
            File.Delete(image);
        }
    }

    [Fact]
    public async Task MultiSourceJsonDiagnosticsKeepTheirSourceIndexesAsync()
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-source-index-{Guid.NewGuid():N}");
        var entry = prefix + "-entry.terrum";
        var first = prefix + "-first.terrum";
        var second = prefix + "-second.terrum";
        try
        {
            await File.WriteAllTextAsync(entry, "int32 Main() { return missingEntry; }");
            await File.WriteAllTextAsync(first, "int32 First() { return missingFirst; }");
            await File.WriteAllTextAsync(second, "int32 Second() { return missingSecond; }");

            var (exit, output, error) = await RunAsync([], "analyze", entry,
                "--source", first, "--source", second, "--json");

            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
            var sourceIndexes = document.RootElement.GetProperty("diagnostics").EnumerateArray()
                .Select(static diagnostic => diagnostic.GetProperty("sourceIndex").GetInt32())
                .OrderBy(static index => index);
            Assert.Equal([0, 1, 2], sourceIndexes);
        }
        finally
        {
            File.Delete(entry);
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Theory]
    [InlineData("build", ".exe")]
    [InlineData("build-library", ".dll")]
    public async Task SourceSetOptionsRejectDuplicatesAndProtectEveryInputAsync(string command, string extension)
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-source-input-{Guid.NewGuid():N}");
        var entry = prefix + ".terrum";
        var helper = prefix + extension;
        const string Helper = "int32 Helper() { return 42; }";
        try
        {
            await File.WriteAllTextAsync(entry, "int32 Main() { return Helper(); }");
            await File.WriteAllTextAsync(helper, Helper);
            List<string> arguments = [command, entry, "--source", helper, "-o", helper, "--runtime", "fail-fast"];
            if (command == "build-library")
            {
                arguments.AddRange(["--export", "Helper"]);
            }
            arguments.Add("--json");
            var (exit, output, _) = await RunAsync([], [.. arguments]);
            Assert.Equal(2, exit);
            Assert.Equal(Helper, await File.ReadAllTextAsync(helper));
            using var document = JsonDocument.Parse(output);
            Assert.Equal("TRM2002", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());

            var alias = Path.Combine(Path.GetDirectoryName(entry) ?? throw new InvalidOperationException(), ".", Path.GetFileName(entry));
            var (duplicate, duplicateOutput, _) = await RunAsync([], "analyze", entry, "--source", alias, "--json");
            Assert.Equal(2, duplicate);
            using var duplicateDocument = JsonDocument.Parse(duplicateOutput);
            Assert.Equal("TRM2000", duplicateDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        }
        finally
        {
            File.Delete(entry);
            File.Delete(helper);
        }
    }

    [Theory]
    [InlineData("analyze - --source - --json")]
    [InlineData("analyze - --source --json")]
    [InlineData("analyze - --json --json")]
    [InlineData("analyze - --runtime unknown --json")]
    [InlineData("jit - --source another.terrum --json")]
    [InlineData("jit - -o output.exe --runtime fail-fast --json")]
    [InlineData("build - --runtime fail-fast --json")]
    [InlineData("build - -o output.exe --runtime fail-fast --runtime fail-fast --json")]
    [InlineData("build-library - --export Value --runtime fail-fast --json")]
    [InlineData("build-library - -o output.dll --runtime fail-fast --json")]
    [InlineData("build-library - -o output.dll --export Value --json")]
    [InlineData("build-library - -o output.dll --export Value --export Value --runtime fail-fast --json")]
    [InlineData("build - -o output.exe --export Value --runtime fail-fast --json")]
    public async Task SourceSetCommandsRejectMalformedOptionsBeforeReadingInputsAsync(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var (exit, output, error) = await RunAsync([], arguments.Split(' '));
        Assert.Equal(2, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("TRM2000", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task SourceSetsBoundInputCountBeforeReadingFilesAsync()
    {
        List<string> arguments = ["analyze", "-"];
        for (var index = 0; index < 256; index++)
        {
            arguments.AddRange(["--source", $"missing-{index}.terrum"]);
        }
        arguments.Add("--json");
        var (exit, output, _) = await RunAsync([], [.. arguments]);
        Assert.Equal(2, exit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("TRM2000", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task ScannerPublishesExactIntegerMagnitudesWithoutDefaultTypesOrPrecisionLossAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        const string Source = "0 9223372036854775808 18446744073709551615 18446744073709551616 256 1.0 2e3 3.0";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), "scan-component",
            Path.Combine(root, "self-host"), "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(10, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("scanner-10", document.RootElement.GetProperty("component").GetString());
        var integers = document.RootElement.GetProperty("integerValues");
        Assert.Equal(5, integers.GetArrayLength());
        string[] tags = ["", "", "", "", ""];
        string?[] magnitudes = ["0", "9223372036854775808", "18446744073709551615", null, "256"];
        for (var index = 0; index < tags.Length; index++)
        {
            Assert.Equal(index, integers[index].GetProperty("tokenIndex").GetInt32());
            Assert.Equal(tags[index], integers[index].GetProperty("tag").GetString());
            Assert.Equal(magnitudes[index] is null ? "overflow" : "exact", integers[index].GetProperty("status").GetString());
            if (magnitudes[index] is string magnitude)
            {
                Assert.Equal(JsonValueKind.String, integers[index].GetProperty("magnitude").ValueKind);
                Assert.Equal(magnitude, integers[index].GetProperty("magnitude").GetString());
            }
            else
            {
                Assert.False(integers[index].TryGetProperty("magnitude", out _));
            }
        }
    }

    [Fact]
    public async Task ScannerWorkerPublishesCompiledTokenRecordsAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("\u03B12 + 42 \U00010400 \U0001F600"),
            "scan-component", Path.Combine(root, "self-host"), "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(10, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("scanner-10", document.RootElement.GetProperty("component").GetString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        var diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("TRM0001", diagnostic.GetProperty("code").GetString());
        Assert.Equal(11, diagnostic.GetProperty("start").GetInt32());
        Assert.Equal(2, diagnostic.GetProperty("length").GetInt32());
        var tokens = document.RootElement.GetProperty("tokens").EnumerateArray().ToArray();
        string[] kinds = ["Identifier", "Plus", "NumericLiteral", "Identifier", "Bad", "EndOfFile"];
        int[] starts = [0, 3, 5, 8, 11, 13];
        int[] lengths = [2, 1, 2, 2, 2, 0];
        Assert.Equal(kinds, tokens.Select(static token => token.GetProperty("kind").GetString()));
        Assert.Equal(starts, tokens.Select(static token => token.GetProperty("start").GetInt32()));
        Assert.Equal(lengths, tokens.Select(static token => token.GetProperty("length").GetInt32()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildPublishesNativeArtifactAndFailedCompilationPreservesItAsync(bool nativeScanner)
    {
        var path = Path.Combine(Path.GetTempPath(), $"terrum-cli-{Guid.NewGuid():N}.exe");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        string[] arguments = nativeScanner
            ? ["build-component", Path.Combine(root, "self-host"), "-", "-o", path, "--runtime", "fail-fast", "--json"]
            : ["build", "-", "-o", path, "--runtime", "fail-fast", "--json"];

        try
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { require true; return 23; }"),
                arguments);
            Assert.Equal(0, exit);
            Assert.Equal("", error);
            using var document = JsonDocument.Parse(output);
            var artifact = document.RootElement.GetProperty("artifact");
            Assert.Equal("windows-x64", artifact.GetProperty("target").GetString());
            Assert.Equal("fail-fast", artifact.GetProperty("runtimePolicy").GetString());
            var original = await File.ReadAllBytesAsync(path);
            Assert.Equal(original.Length, artifact.GetProperty("length").GetInt32());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(original)), artifact.GetProperty("sha256").GetString());

            var (failure, failedOutput, _) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { return unknown; }"),
                arguments);
            Assert.Equal(1, failure);
            using var failedDocument = JsonDocument.Parse(failedOutput);
            Assert.False(failedDocument.RootElement.GetProperty("success").GetBoolean());
            Assert.False(failedDocument.RootElement.TryGetProperty("artifact", out _));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));

            var oversized = "int32 Main() {" + string.Concat(Enumerable.Range(0, 257)
                .Select(static index => FormattableString.Invariant($"int32 v{index} = 0;"))) + "return 0;}";
            var (limitExit, limitOutput, _) = await RunAsync(Encoding.UTF8.GetBytes(oversized),
                arguments);
            Assert.Equal(1, limitExit);
            using var limitDocument = JsonDocument.Parse(limitOutput);
            Assert.Equal("TRM3016", limitDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.False(limitDocument.RootElement.TryGetProperty("artifact", out _));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));

            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Could not start native artifact.");

            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }

                throw;
            }

            Assert.Equal(23, process.ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("/* first */ int32 Main() { int32 \u03B1 = 17; return \u03B1 + 25; } // end")]
    [InlineData("int32 F(int32 value) { if (value == 0) { return 1; } return value * F(value - 1); } int32 Main() { return F(5); }")]
    [InlineData("struct S { public int32 Value; public S(int32 value) { Value = value; } } int32 Main() { S value = new S(42); return value.Value; }")]
    [InlineData("int32 Main() { uint64 value = 18446744073709551615; return int32(value % 1000); }")]
    [InlineData("int32 Main() { int64 minimum = -9223372036854775808; return int32(minimum % -1); }")]
    [InlineData("int32 Main() { mut int32 sum = 0; for (mut int32 i = 0; i < 10; i = i + 1) { sum = sum + i; } return sum; }")]
    [InlineData("int32 End() { return 1; } int32 Main() { require OwnedInt32Buffer.TryCreate(1, out var raw); mut OwnedInt32Buffer buffer = move raw; require buffer.TryAppend(40); ReadonlyInt32BufferView prefix = buffer[..End()]; return prefix.Read(0) + 1; }")]
    [InlineData("int32 Start() { return 1; } int32 End() { return 2; } int32 Main() { require OwnedInt32Buffer.TryCreate(2, out var raw); mut OwnedInt32Buffer buffer = move raw; require buffer.TryAppend(40); require buffer.TryAppend(41); ReadonlyInt32BufferView view = buffer[Start()..End()]; return view.Read(0); }")]
    [InlineData("int32 Start() { return 1; } int32 End() { return 0; } int32 Main() { require OwnedInt32Buffer.TryCreate(2, out var raw); mut OwnedInt32Buffer buffer = move raw; require buffer.TryAppend(40); require buffer.TryAppend(41); ReadonlyInt32BufferView view = buffer[^Start()..^End()]; return view.Read(0); }")]
    [InlineData("float32 Echo(float32 value) { return value; } int32 Main() { _ = Echo(float32(0x1.fp+2)); return 42; }")]
    [InlineData("float64 Widen(float32 value) { return float64(value); } float32 Narrow(float64 value) { return float32(value); } int32 Main() { _ = Narrow(Widen(0.1)); return 42; }")]
    public async Task NativeScannerFeedsTheOfficialCompilerWithoutChangingEmissionAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var original = Path.Combine(Path.GetTempPath(), $"terrum-host-{Guid.NewGuid():N}.exe");
        var scanned = original + ".scanned.exe";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(source);
            var (hostExit, hostOutput, hostError) = await RunAsync(bytes, "build", "-", "-o", original,
                "--runtime", "fail-fast", "--json");
            Assert.Equal(0, hostExit);
            Assert.Empty(hostError);
            var (nativeExit, nativeOutput, nativeError) = await RunAsync(bytes, "build-component",
                Path.Combine(root, "self-host"), "-", "-o", scanned, "--runtime", "fail-fast", "--json");
            Assert.Equal(0, nativeExit);
            Assert.Empty(nativeError);
            using var host = JsonDocument.Parse(hostOutput);
            using var native = JsonDocument.Parse(nativeOutput);
            Assert.Equal(host.RootElement.GetProperty("contentId").GetString(), native.RootElement.GetProperty("contentId").GetString());
            Assert.Equal("build-component", native.RootElement.GetProperty("command").GetString());
            Assert.Equal(await File.ReadAllBytesAsync(original), await File.ReadAllBytesAsync(scanned));
        }
        finally
        {
            File.Delete(original);
            File.Delete(scanned);
        }
    }

    [Theory]
    [InlineData("int32 Main() { return 0; } /*", "TRM0003")]
    [InlineData("int32 Main() { return 0; }\0", "TRM0001")]
    [InlineData("int32 Main() { return 1e+; }", "TRM0004")]
    [InlineData("int32 Main() { return 0 }", "TRM1001")]
    public async Task NativeScannerBuildPreservesLexicalAndParserDiagnosticsAsync(string source, string code)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var artifact = Path.Combine(Path.GetTempPath(), $"terrum-scanner-failure-{Guid.NewGuid():N}.exe");
        byte[] previous = [1, 2, 3, 4];
        try
        {
            await File.WriteAllBytesAsync(artifact, previous);
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "build-component",
                Path.Combine(root, "self-host"), "-", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("code").GetString() == code);
            Assert.Equal(previous, await File.ReadAllBytesAsync(artifact));
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScannerBundleCompilationDiagnosticsKeepTheirSourceFileAsync(bool json)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var selfHost = Path.Combine(root, "self-host");
        var bundle = Path.Combine(Path.GetTempPath(), $"terrum-scanner-bundle-{Guid.NewGuid():N}");
        var artifact = bundle + ".exe";
        var characterLiterals = Path.Combine(bundle, "CharacterLiteral.terrum");
        try
        {
            Directory.CreateDirectory(bundle);
            foreach (var name in new[] { "SourceRange.terrum", "CharacterFacts.terrum", "Utf16CodeUnits.terrum",
                "Utf16Scalar.terrum", "CharacterLiteral.terrum",
                "StringEscape.terrum", "Scanner.terrum" })
            {
                var source = await File.ReadAllTextAsync(Path.Combine(selfHost, name)).ConfigureAwait(true);
                if (name == "CharacterLiteral.terrum")
                {
                    source += "\nint32 Broken() { return 0 }\n";
                }
                await File.WriteAllTextAsync(Path.Combine(bundle, name), source).ConfigureAwait(true);
            }

            string[] arguments = ["build-component", bundle, "-", "-o", artifact, "--runtime", "fail-fast"];
            if (json)
            {
                arguments = [.. arguments, "--json"];
            }
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                arguments);
            Assert.Equal(1, exit);
            if (json)
            {
                Assert.Empty(error);
                using var document = JsonDocument.Parse(output);
                Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
                var sources = document.RootElement.GetProperty("sources");
                var characterSourceIndex = Enumerable.Range(0, sources.GetArrayLength())
                    .Single(index => sources[index].GetProperty("path").GetString() == characterLiterals);
                Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(),
                    diagnostic => diagnostic.GetProperty("sourceIndex").GetInt32() == characterSourceIndex);
            }
            else
            {
                Assert.Empty(output);
                Assert.Contains(characterLiterals + "(", error, StringComparison.Ordinal);
                Assert.Contains("TRM1001", error, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (Directory.Exists(bundle))
            {
                Directory.Delete(bundle, recursive: true);
            }
            File.Delete(artifact);
        }
    }

    [Fact]
    public void CompilerPrimitivesLibraryExportsExpectedSymbols()
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Compiler-primitives exports require Windows x64.");

        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing configuration directory.");
        var root = RepositoryRoot();
        var libraryPath = Path.Combine(root, "artifacts", "bin", "self-host", configuration,
            "terrum-compiler-primitives.dll");
        Assert.True(File.Exists(libraryPath), $"Compiler-primitives library was not built: {libraryPath}");

        var handle = NativeLibrary.Load(libraryPath);
        try
        {
            foreach (var export in new[] {
                "Terrum.CodeAnalysis.SourceRange.TryCreate",
                "Terrum.CodeAnalysis.IsAsciiDigit",
                "Terrum.CodeAnalysis.IsLineBreak",
                "Terrum.CodeAnalysis.GetDecimalDigit",
                "Terrum.CodeAnalysis.TryGetHexDigit",
                "Terrum.CodeAnalysis.DecodeUtf16Scalar",
                "Terrum.CodeAnalysis.Utf16ScalarWidth",
                "Terrum.CodeAnalysis.Numerics64.MagnitudeInt64",
                "Terrum.CodeAnalysis.Numerics64.TryAccumulateDecimal",
                "Terrum.CodeAnalysis.Numerics64.TryAccumulateBase",
                "Terrum.CodeAnalysis.Numerics64.TryAddInt64",
                "Terrum.CodeAnalysis.Numerics64.TrySubtractInt64",
                "Terrum.CodeAnalysis.Numerics64.TryMultiplyInt64",
                "Terrum.CodeAnalysis.Numerics64.TryDivideInt64",
                "Terrum.CodeAnalysis.Numerics64.TryRemainderInt64",
                "Terrum.CodeAnalysis.Numerics64.TryNegateInt64",
                "Terrum.CodeAnalysis.Numerics64.TryAbsoluteInt64",
                "Terrum.CodeAnalysis.Numerics64.TryAddUInt64",
                "Terrum.CodeAnalysis.Numerics64.TrySubtractUInt64",
                "Terrum.CodeAnalysis.Numerics64.TryMultiplyUInt64",
                "Terrum.CodeAnalysis.Numerics64.TryDivideUInt64",
                "Terrum.CodeAnalysis.Numerics64.TryRemainderUInt64",
                "Terrum.CodeAnalysis.Numerics64.LeadingZeroCountUInt32",
                "Terrum.CodeAnalysis.Numerics64.TrailingZeroCountUInt32",
                "Terrum.CodeAnalysis.Numerics64.PopCountUInt32",
                "Terrum.CodeAnalysis.Numerics64.RotateLeftUInt32",
                "Terrum.CodeAnalysis.Numerics64.RotateRightUInt32",
                "Terrum.CodeAnalysis.Numerics64.ReverseBitsUInt32",
                "Terrum.CodeAnalysis.Numerics64.ReverseEndiannessUInt32",
                "Terrum.CodeAnalysis.Numerics64.IsPow2UInt32",
                "Terrum.CodeAnalysis.Numerics64.TryRoundUpToPowerOfTwoUInt32",
                "Terrum.CodeAnalysis.Numerics64.LeadingZeroCountUInt64",
                "Terrum.CodeAnalysis.Numerics64.TrailingZeroCountUInt64",
                "Terrum.CodeAnalysis.Numerics64.PopCountUInt64",
                "Terrum.CodeAnalysis.Numerics64.RotateLeftUInt64",
                "Terrum.CodeAnalysis.Numerics64.RotateRightUInt64",
                "Terrum.CodeAnalysis.Numerics64.ReverseBitsUInt64",
                "Terrum.CodeAnalysis.Numerics64.ReverseEndiannessUInt64",
                "Terrum.CodeAnalysis.Numerics64.IsPow2UInt64",
                "Terrum.CodeAnalysis.Numerics64.TryRoundUpToPowerOfTwoUInt64",
                "Terrum.CodeAnalysis.Numerics64.AbsInt32",
                "Terrum.CodeAnalysis.Numerics64.TryAbsInt32",
                "Terrum.CodeAnalysis.Numerics64.SignInt32",
                "Terrum.CodeAnalysis.Numerics64.MinInt32",
                "Terrum.CodeAnalysis.Numerics64.MaxInt32",
                "Terrum.CodeAnalysis.Numerics64.ClampInt32",
                "Terrum.CodeAnalysis.Numerics64.BigMultiplyInt32",
                "Terrum.CodeAnalysis.Numerics64.GreatestCommonDivisorInt32",
                "Terrum.CodeAnalysis.Numerics64.AbsInt64",
                "Terrum.CodeAnalysis.Numerics64.TryAbsInt64",
                "Terrum.CodeAnalysis.Numerics64.SignInt64",
                "Terrum.CodeAnalysis.Numerics64.MinInt64",
                "Terrum.CodeAnalysis.Numerics64.MaxInt64",
                "Terrum.CodeAnalysis.Numerics64.ClampInt64",
                "Terrum.CodeAnalysis.Numerics64.GreatestCommonDivisorInt64",
            })
            {
                Assert.True(NativeLibrary.TryGetExport(handle, export, out _),
                    $"Compiler-primitives library is missing export '{export}'.");
            }

            var decode = Marshal.GetDelegateForFunctionPointer<Utf16ScalarDecoderExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.DecodeUtf16Scalar"));
            foreach (var (first, second, expected) in new (ushort First, ushort Second, int Expected)[] {
                (0, 0xDC00, 0),
                (0xFDD0, 0, 0xFDD0),
                (0xD800, 0xDC00, 0x10000),
                (0xDBFF, 0xDFFF, 0x10FFFF),
                (0xD800, (ushort)'A', -1),
                (0xD800, 0xDBFF, -1),
                (0xDC00, 0, -1),
            })
            {
                Assert.Equal(expected, decode(first, second));
            }

            var scalarWidth = Marshal.GetDelegateForFunctionPointer<Utf16ScalarWidthExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Utf16ScalarWidth"));
            foreach (var (scalar, expected) in new[] {
                (-1, 1),
                (0xFFFF, 1),
                (0x10000, 2),
                (0x10FFFF, 2),
            })
            {
                Assert.Equal(expected, scalarWidth(scalar));
            }

            var magnitudeInt64 = Marshal.GetDelegateForFunctionPointer<MagnitudeInt64Export>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.MagnitudeInt64"));
            long[] signedValues = [
                long.MinValue, long.MinValue + 1, -4294967297L, -4294967296L, -4294967295L,
                int.MinValue, -1, 0, 1, int.MaxValue, 4294967295L, 4294967296L,
                long.MaxValue - 1, long.MaxValue,
            ];
            foreach (var value in signedValues)
            {
                var expected = (ulong)BigInteger.Abs(new BigInteger(value));
                Assert.Equal(expected, magnitudeInt64(value));
            }
            Assert.Equal(0x8000000000000000UL, magnitudeInt64(long.MinValue));
            AssertWideIntegerAccumulatorExports(handle);
            AssertWideIntegerArithmeticExports(handle);
            AssertBitOperationsExports(handle);
            AssertIntegerMathExports(handle);

            var isAsciiDigit = Marshal.GetDelegateForFunctionPointer<Char16PredicateExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.IsAsciiDigit"));
            var isLineBreak = Marshal.GetDelegateForFunctionPointer<Char16PredicateExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.IsLineBreak"));
            var getDecimalDigit = Marshal.GetDelegateForFunctionPointer<Char16ValueExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.GetDecimalDigit"));
            for (var value = 0; value <= ushort.MaxValue; value++)
            {
                var character = (ushort)value;
                Assert.Equal(value >= 48 && value <= 57 ? 1 : 0, isAsciiDigit(character));
                var expectedLineBreak = value == 10 || value == 13 || value == 133
                    || value == 8232 || value == 8233;
                Assert.Equal(expectedLineBreak ? 1 : 0, isLineBreak(character));
                if (value >= 48 && value <= 57)
                {
                    Assert.Equal(value - 48, getDecimalDigit(character));
                }
            }

            Assert.Equal(8, Marshal.SizeOf<NativeSourceRange>());
            Assert.Equal(0, Marshal.OffsetOf<NativeSourceRange>(nameof(NativeSourceRange.Start)).ToInt32());
            Assert.Equal(4, Marshal.OffsetOf<NativeSourceRange>(nameof(NativeSourceRange.Length)).ToInt32());
            Assert.Equal(16, Marshal.SizeOf<GuardedSourceRange>());
            Assert.Equal(4, Marshal.OffsetOf<GuardedSourceRange>(nameof(GuardedSourceRange.Range)).ToInt32());
            Assert.Equal(12, Marshal.OffsetOf<GuardedSourceRange>(nameof(GuardedSourceRange.After)).ToInt32());

            var tryCreateRange = Marshal.GetDelegateForFunctionPointer<SourceRangeTryCreateExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.SourceRange.TryCreate"));
            const int lowerRangeCanary = 0x13579BDF;
            const int sentinelStart = 0x2468ACE0;
            const int sentinelLength = unchecked((int)0xECA86420);
            const int upperRangeCanary = 0x5A5A5A5A;
            foreach (var (start, length, expectedSuccess) in new (int Start, int Length, bool ExpectedSuccess)[] {
                (0, 0, true),
                (0, 1, true),
                (1, 0, true),
                (7, 19, true),
                (int.MaxValue, 0, true),
                (int.MaxValue - 1, 1, true),
                (0, int.MaxValue, true),
                (1, int.MaxValue - 1, true),
                (-1, 0, false),
                (0, -1, false),
                (-1, -1, false),
                (int.MinValue, 0, false),
                (0, int.MinValue, false),
                (int.MinValue, int.MaxValue, false),
                (int.MaxValue, int.MinValue, false),
                (int.MaxValue, 1, false),
                (1, int.MaxValue, false),
                (int.MaxValue - 1, 2, false),
                (2, int.MaxValue - 1, false),
                (int.MaxValue, int.MaxValue, false),
            })
            {
                TextSpan? expectedRange = null;
                try
                {
                    expectedRange = new TextSpan(start, length);
                }
                catch (ArgumentOutOfRangeException)
                {
                }

                Assert.Equal(expectedSuccess, expectedRange.HasValue);
                var guarded = new GuardedSourceRange {
                    Before = lowerRangeCanary,
                    Range = new NativeSourceRange { Start = sentinelStart, Length = sentinelLength },
                    After = upperRangeCanary,
                };
                Assert.Equal(expectedSuccess ? 1 : 0,
                    tryCreateRange(start, length, ref guarded.Range));
                Assert.Equal(expectedSuccess ? expectedRange.GetValueOrDefault().Start : sentinelStart,
                    guarded.Range.Start);
                Assert.Equal(expectedSuccess ? expectedRange.GetValueOrDefault().Length : sentinelLength,
                    guarded.Range.Length);
                Assert.Equal(lowerRangeCanary, guarded.Before);
                Assert.Equal(upperRangeCanary, guarded.After);
            }

            var reusedRange = new GuardedSourceRange {
                Before = lowerRangeCanary,
                Range = new NativeSourceRange { Start = sentinelStart, Length = sentinelLength },
                After = upperRangeCanary,
            };
            Assert.Equal(0, tryCreateRange(-1, 0, ref reusedRange.Range));
            Assert.Equal(sentinelStart, reusedRange.Range.Start);
            Assert.Equal(sentinelLength, reusedRange.Range.Length);
            Assert.Equal(1, tryCreateRange(5, 7, ref reusedRange.Range));
            Assert.Equal(5, reusedRange.Range.Start);
            Assert.Equal(7, reusedRange.Range.Length);
            Assert.Equal(0, tryCreateRange(int.MaxValue, 1, ref reusedRange.Range));
            Assert.Equal(5, reusedRange.Range.Start);
            Assert.Equal(7, reusedRange.Range.Length);
            Assert.Equal(1, tryCreateRange(0, int.MaxValue, ref reusedRange.Range));
            Assert.Equal(0, reusedRange.Range.Start);
            Assert.Equal(int.MaxValue, reusedRange.Range.Length);
            Assert.Equal(lowerRangeCanary, reusedRange.Before);
            Assert.Equal(upperRangeCanary, reusedRange.After);

            var tryGetHexDigit = Marshal.GetDelegateForFunctionPointer<HexDigitExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.TryGetHexDigit"));
            const int lowerCanary = 0x13579BDF;
            const int sentinel = 0x2468ACE0;
            const int upperCanary = unchecked((int)0xECA86420);
            int[] words = [lowerCanary, sentinel, upperCanary];
            foreach (var (character, expected) in new (ushort Character, int Expected)[] {
                ((ushort)'0', 0), ((ushort)'1', 1), ((ushort)'2', 2), ((ushort)'3', 3),
                ((ushort)'4', 4), ((ushort)'5', 5), ((ushort)'6', 6), ((ushort)'7', 7),
                ((ushort)'8', 8), ((ushort)'9', 9),
                ((ushort)'A', 10), ((ushort)'B', 11), ((ushort)'C', 12),
                ((ushort)'D', 13), ((ushort)'E', 14), ((ushort)'F', 15),
                ((ushort)'a', 10), ((ushort)'b', 11), ((ushort)'c', 12),
                ((ushort)'d', 13), ((ushort)'e', 14), ((ushort)'f', 15),
            })
            {
                words[1] = sentinel;
                Assert.Equal(1, tryGetHexDigit(character, ref words[1]));
                Assert.Equal(expected, words[1]);
                Assert.Equal(lowerCanary, words[0]);
                Assert.Equal(upperCanary, words[2]);
            }

            foreach (var character in new ushort[] {
                (ushort)'/', (ushort)':', (ushort)'@', (ushort)'G', (ushort)'`', (ushort)'g',
                0, 0x0661, 0xD800, 0xFFFF,
            })
            {
                words[1] = sentinel;
                Assert.Equal(0, tryGetHexDigit(character, ref words[1]));
                Assert.Equal(sentinel, words[1]);
                Assert.Equal(lowerCanary, words[0]);
                Assert.Equal(upperCanary, words[2]);
            }

            words[1] = sentinel;
            Assert.Equal(0, tryGetHexDigit(0, ref words[1]));
            Assert.Equal(sentinel, words[1]);
            Assert.Equal(1, tryGetHexDigit((ushort)'f', ref words[1]));
            Assert.Equal(15, words[1]);
            Assert.Equal(0, tryGetHexDigit((ushort)'g', ref words[1]));
            Assert.Equal(15, words[1]);
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }

    [Theory]
    [InlineData("int32 Scan() { return 0; }", 1)]
    [InlineData("int32 Scan() { require false; return 0; }", unchecked((int)0xE0540003))]
    public async Task ScannerFailureNeverFallsBackToTheHostLexerAsync(string scanner, int expected)
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-scanner-worker-{Guid.NewGuid():N}");
        var component = prefix + ".terrum";
        var artifact = prefix + ".exe";
        byte[] previous = [1, 2, 3, 4];
        try
        {
            await File.WriteAllTextAsync(component, scanner);
            await File.WriteAllBytesAsync(artifact, previous);
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                "build-component", component, "-", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.Equal(expected, exit);
            Assert.Empty(error);
            if (exit == 1)
            {
                using var document = JsonDocument.Parse(output);
                Assert.Equal("TRM5001", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            }
            Assert.Equal(previous, await File.ReadAllBytesAsync(artifact));
        }
        finally
        {
            File.Delete(component);
            File.Delete(artifact);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComponentBuildCannotOverwriteEitherSourceAsync(bool scannerIsDestination)
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-component-input-{Guid.NewGuid():N}");
        var scanner = prefix + "-scanner.exe";
        var input = prefix + "-source.exe";
        const string Scanner = "int32 Scan() { require false; return 0; }";
        const string Source = "int32 Main() { return 42; }";
        try
        {
            await File.WriteAllTextAsync(scanner, Scanner);
            await File.WriteAllTextAsync(input, Source);
            var (exit, output, error) = await RunAsync([], "build-component", scanner, input,
                "-o", scannerIsDestination ? scanner : input, "--runtime", "fail-fast", "--json");
            Assert.Equal(2, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("TRM2002", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal(Scanner, await File.ReadAllTextAsync(scanner));
            Assert.Equal(Source, await File.ReadAllTextAsync(input));
        }
        finally
        {
            File.Delete(scanner);
            File.Delete(input);
        }
    }

    [Theory]
    [InlineData("build", "-", "-o", "unused.exe", "--json")]
    [InlineData("build", "-", "-o", "unused.exe", "--runtime", "implicit", "--json")]
    [InlineData("build-component", "scanner.terrum", "-", "-o", "unused.exe", "--json")]
    [InlineData("build-component", "scanner.terrum", "-", "-o", "unused.exe", "--runtime", "implicit", "--json")]
    [InlineData("build-component", "-", "-", "-o", "unused.exe", "--runtime", "fail-fast", "--json")]
    [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "The xUnit data rows supply non-null argument arrays.")]
    public async Task BuildRequiresExplicitRuntimePolicyAsync(params string[] arguments)
    {
        var (exit, output, _) = await RunAsync([], arguments);
        Assert.Equal(2, exit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("TRM2000", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnalyzeRunsBindingRatherThanOnlyParsingAsync()
    {
        var source = Encoding.UTF8.GetBytes("int32 Main() { var x = 1; return x; }");
        var (syntaxExit, _, _) = await RunAsync(source, "check", "-", "--json");
        Assert.Equal(0, syntaxExit);
        var (semanticExit, output, _) = await RunAsync(source, "analyze", "-", "--json");
        Assert.Equal(1, semanticExit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("native-10", document.RootElement.GetProperty("profile").GetString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }

    [Theory]
    [InlineData("check")]
    [InlineData("analyze")]
    public async Task CliAcceptsPostTestedAndNamedLoopsAsync(string command)
    {
        const string source = """
            int32 Main() {
                mut int32 value = 0;
                outer: do {
                    do { value = value + 42; break outer; } while (true);
                } while (false);
                return value;
            }
            """;
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), command, "-", "--json");
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        if (command == "check")
        {
            Assert.Equal(SyntaxTree.LanguageVersion, document.RootElement.GetProperty("languageVersion").GetString());
        }
    }

    [Theory]
    [InlineData("check")]
    [InlineData("tree")]
    public async Task RepeatedOptionalReportsPreciseStructuredDiagnosticAsync(string command)
    {
        const string Source = "void F(T? /* gap */\n? value) {}";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), command, "-", "--json");
        Assert.Equal(1, exit);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(SyntaxTree.LanguageVersion, root.GetProperty("languageVersion").GetString());
        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("TRM1005", diagnostic.GetProperty("code").GetString());
        Assert.Equal(Source.LastIndexOf('?', StringComparison.Ordinal), diagnostic.GetProperty("span").GetProperty("start").GetInt32());
        Assert.Equal(1, diagnostic.GetProperty("span").GetProperty("length").GetInt32());

        if (command == "tree")
        {
            var reconstructed = string.Concat(root.GetProperty("tokens").EnumerateArray().Select(token =>
                string.Concat(token.GetProperty("leadingTrivia").EnumerateArray().Select(trivia => trivia.GetProperty("text").GetString())) + token.GetProperty("text").GetString()));
            Assert.Equal(Source, reconstructed);
        }
    }

    [Theory]
    [InlineData("name")]
    [InlineData("\"quote\\\" and \\\\ slash\"")]
    [InlineData("\"<tag> \u03B1 \U0001F600\"")]
    public async Task HumanTokensPreserveJsonEscapingAsync(string source)
    {
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "tokens", "-");
        Assert.Equal(0, exit);
        Assert.Equal("", error);
        Assert.Contains(JsonSerializer.Serialize(source), output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("check", "void Main() { return; }", 0)]
    [InlineData("tree", "void Main() { return 1 }", 1)]
    [InlineData("tokens", " \r\n\u03B1 + 12 // end", 0)]
    [InlineData("tokens", "1 \u0661", 1)]
    [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "The xUnit data rows supply non-null source strings.")]
    public async Task EmitsStructuredResultsWithExpectedExitAsync(string command, string source, int exitCode)
    {
        (var exit, var output, var error) = await RunAsync(Encoding.UTF8.GetBytes(source), command, "-", "--json");
        Assert.Equal(exitCode, exit);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("utf-16", root.GetProperty("coordinateSystem").GetString());
        Assert.Equal(exitCode == 0, root.GetProperty("success").GetBoolean());
        Assert.Equal(source.Length, root.GetProperty("sourceLength").GetInt32());

        if (command != "check")
        {
            var reconstructed = string.Concat(root.GetProperty("tokens").EnumerateArray().Select(token =>
                string.Concat(token.GetProperty("leadingTrivia").EnumerateArray().Select(trivia => trivia.GetProperty("text").GetString())) + token.GetProperty("text").GetString()));
            Assert.Equal(source, reconstructed);
        }

        if (command == "tree")
        {
            Assert.Equal("CompilationUnit", root.GetProperty("nodes")[0].GetProperty("kind").GetString());
            Assert.Equal(64, Assert.IsType<string>(root.GetProperty("contentId").GetString()).Length);
        }
    }

    [Theory]
    [InlineData("void Main() {\nreturn 1\n}", "<stdin>(3,1): error TRM1001")]
    [InlineData("void F(T? /* gap */\n? value) {}", "<stdin>(2,1): error TRM1005")]
    public async Task HumanErrorsUseOneBasedLocationsAndStderrAsync(string source, string diagnostic)
    {
        (var exit, var output, var error) = await RunAsync(Encoding.UTF8.GetBytes(source), "check", "-");
        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Contains(diagnostic, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputFailuresRemainStructuredAndDoNotClaimToParseAsync()
    {
        foreach (var arguments in new[] { new[] { "bad", "-", "--json" }, ["check", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".terrum"), "--json"] })
        {
            (var exit, var output, var error) = await RunAsync([], arguments);
            Assert.Equal(2, exit);
            Assert.Equal("", error);
            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.StartsWith("TRM200", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task StrictUtf8RejectsInvalidBytesAndAcceptsAnOptionalBomAsync()
    {
        (var exit, var output, _) = await RunAsync([0xFF, 0xFE, 0x00, 0xD8], "check", "-", "--json");
        Assert.Equal(2, exit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("TRM2001", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        (var validExit, _, _) = await RunAsync([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("void Main() {}")], "check", "-");
        Assert.Equal(0, validExit);
    }

    [Fact]
    public async Task FlatTreeProtocolPreservesReferencesAndDeepTreesAsync()
    {
        var source = "void F() { return " + string.Join(" + ", Enumerable.Repeat("1", 500)) + "; }";
        (var exit, var output, _) = await RunAsync(Encoding.UTF8.GetBytes(source), "tree", "-", "--json");
        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output);
        var nodes = document.RootElement.GetProperty("nodes");
        var tokens = document.RootElement.GetProperty("tokens");
        var nodeReferences = new int[nodes.GetArrayLength()];
        var tokenReferences = new int[tokens.GetArrayLength()];

        foreach (var node in nodes.EnumerateArray())
        {
            foreach (var child in node.GetProperty("children").EnumerateArray())
            {
                if (child.TryGetProperty("node", out var nodeId))
                {
                    nodeReferences[nodeId.GetInt32()]++;
                }
                else
                {
                    tokenReferences[child.GetProperty("token").GetInt32()]++;
                }
            }
        }

        Assert.Equal(0, nodeReferences[0]);
        Assert.All(nodeReferences.Skip(1), count => Assert.Equal(1, count));
        Assert.All(tokenReferences, count => Assert.Equal(1, count));
        (var secondExit, var secondOutput, _) = await RunAsync(Encoding.UTF8.GetBytes(source), "tree", "-", "--json");
        Assert.Equal(exit, secondExit);
        Assert.Equal(output, secondOutput);
    }

    private static async Task<int> ExecuteNativeImageAsync(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path) {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Could not start native executable.");
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            throw;
        }

        return process.ExitCode;
    }

    private static Task<(int ExitCode, string Output, string Error)> RunAsync(byte[] input, params string[] arguments) =>
        RunAsync(input, arguments, workingDirectory: null);

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(byte[] input, string[] arguments,
        string? workingDirectory)
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name ?? throw new InvalidOperationException("Missing configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var cli = Path.Combine(root, "artifacts", "bin", "sources", "terrumc", configuration, frameworkDirectory.Name, "terrumc.dll");
        Assert.True(File.Exists(cli), $"CLI was not built: {cli}");
        var start = new ProcessStartInfo("dotnet") {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (workingDirectory is not null)
        {
            start.WorkingDirectory = workingDirectory;
        }
        start.ArgumentList.Add(cli);

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start CLI.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.BaseStream.WriteAsync(input).ConfigureAwait(true);
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(true);
            }

            throw;
        }

        return (process.ExitCode, await output.ConfigureAwait(true), await error.ConfigureAwait(true));
    }
}
