// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers;
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
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.Children)]
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
    private delegate int SourceRangeGetEndExport(NativeSourceRange range);

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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32CheckedBinaryExport(int left, int right, ref int result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt32CheckedBinaryExport(uint left, uint right, ref uint result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32CheckedUnaryExport(int value, ref int result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MagnitudeInt32Export(int value);
    [TestCase(false)]
    [TestCase(true)]
    public async Task LibraryBuildPublishesSelectedExportsWithoutAnEntryPointAsync(bool multipleSources)
    {
        const string Entry = "public int32 Value() { return Helper(); }";
        const string Helper = "int32 Helper() { return 42; }";
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-library-cli-{Guid.NewGuid():N}");
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
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(multipleSources ? 2 : 1));
            Assert.That(document.RootElement.GetProperty("artifact").GetProperty("kind").GetString(), Is.EqualTo("dynamic-library"));
            var bytes = await File.ReadAllBytesAsync(outputPath);
            var compilation = multipleSources
                ? Compilation.CreateLibrary([SyntaxTree.Parse(Entry, "<stdin>"), SyntaxTree.Parse(Helper, helperPath)])
                : Compilation.CreateLibrary(SyntaxTree.Parse(input, "<stdin>"));
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.That(bytes, Is.EqualTo(NativeEmitter.EmitLibrary(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
                ["Value"], NativeRuntimePolicy.FailFast)));
            using var stream = new MemoryStream(bytes);
            using var reader = new PEReader(stream);
            var header = ((global::System.Func<PEHeader>)(() => { var __nunitActual1 = reader.PEHeaders.PEHeader; Assert.That(__nunitActual1, Is.TypeOf<PEHeader>()); return (PEHeader)__nunitActual1!; }))();
            Assert.That(header.AddressOfEntryPoint, Is.EqualTo(0));
            Assert.That(reader.PEHeaders.IsDll, Is.True);
            Assert.That(header.ExportTableDirectory.Size, Is.Not.EqualTo(0));
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
        Assert.That(Marshal.SizeOf<GuardedUInt64>(), Is.EqualTo(24));
        Assert.That(Marshal.OffsetOf<GuardedUInt64>(nameof(GuardedUInt64.Before)).ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<GuardedUInt64>(nameof(GuardedUInt64.Value)).ToInt32(), Is.EqualTo(8));
        Assert.That(Marshal.OffsetOf<GuardedUInt64>(nameof(GuardedUInt64.After)).ToInt32(), Is.EqualTo(16));

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
            Assert.That(actual, Is.EqualTo(expectedSuccess ? 1 : 0));
            Assert.That(guarded.Value, Is.EqualTo(expectedSuccess ? expected : outputSentinel));
            Assert.That(guarded.Before, Is.EqualTo(lowerCanary));
            Assert.That(guarded.After, Is.EqualTo(upperCanary));
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

        Assert.That(Marshal.SizeOf<GuardedInt64>(), Is.EqualTo(24));
        Assert.That(Marshal.OffsetOf<GuardedInt64>(nameof(GuardedInt64.Before)).ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<GuardedInt64>(nameof(GuardedInt64.Value)).ToInt32(), Is.EqualTo(8));
        Assert.That(Marshal.OffsetOf<GuardedInt64>(nameof(GuardedInt64.After)).ToInt32(), Is.EqualTo(16));

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
                    Assert.That(actual, Is.EqualTo(expectedSuccess ? 1 : 0));
                    Assert.That(guarded.Value, Is.EqualTo(expectedSuccess ? expected : signedOutputSentinel));
                    Assert.That(guarded.Before, Is.EqualTo(lowerSignedCanary));
                    Assert.That(guarded.After, Is.EqualTo(upperSignedCanary));
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
                    Assert.That(actual, Is.EqualTo(expectedSuccess ? 1 : 0));
                    Assert.That(guarded.Value, Is.EqualTo(expectedSuccess ? expected : unsignedOutputSentinel));
                    Assert.That(guarded.Before, Is.EqualTo(lowerUnsignedCanary));
                    Assert.That(guarded.After, Is.EqualTo(upperUnsignedCanary));
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
            Assert.That(negateResult, Is.EqualTo(expectedSuccess ? 1 : 0));
            Assert.That(negateGuard.Value, Is.EqualTo(expectedSuccess ? expectedNegation : signedOutputSentinel));
            Assert.That(negateGuard.Before, Is.EqualTo(lowerSignedCanary));
            Assert.That(negateGuard.After, Is.EqualTo(upperSignedCanary));

            var expectedAbsolute = value == long.MinValue ? 0 : (long)BigInteger.Abs(bigValue);
            var absoluteGuard = new GuardedInt64 {
                Before = lowerSignedCanary,
                Value = signedOutputSentinel,
                After = upperSignedCanary,
            };
            var absoluteResult = absolute(value, ref absoluteGuard.Value);
            Assert.That(absoluteResult, Is.EqualTo(expectedSuccess ? 1 : 0));
            Assert.That(absoluteGuard.Value, Is.EqualTo(expectedSuccess ? expectedAbsolute : signedOutputSentinel));
            Assert.That(absoluteGuard.Before, Is.EqualTo(lowerSignedCanary));
            Assert.That(absoluteGuard.After, Is.EqualTo(upperSignedCanary));
        }
    }

    private static void AssertIntegerCheckedArithmeticExports(nint handle)
    {
        const int LowerSignedCanary = 0x13579BDF;
        const int SignedOutputSentinel = unchecked((int)0xFEDCBA98);
        const int UpperSignedCanary = 0x0ECA8642;
        const uint LowerUnsignedCanary = 0x13579BDF;
        const uint UnsignedOutputSentinel = 0xFEDCBA98;
        const uint UpperUnsignedCanary = 0x0ECA8642;
        const string ExportPrefix = "Terrum.CodeAnalysis.Numerics64.";

        Assert.That(Marshal.SizeOf<GuardedInt32>(), Is.EqualTo(12));
        Assert.That(Marshal.OffsetOf<GuardedInt32>(nameof(GuardedInt32.Before)).ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<GuardedInt32>(nameof(GuardedInt32.Value)).ToInt32(), Is.EqualTo(4));
        Assert.That(Marshal.OffsetOf<GuardedInt32>(nameof(GuardedInt32.After)).ToInt32(), Is.EqualTo(8));
        Assert.That(Marshal.SizeOf<GuardedUInt32>(), Is.EqualTo(12));
        Assert.That(Marshal.OffsetOf<GuardedUInt32>(nameof(GuardedUInt32.Before)).ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<GuardedUInt32>(nameof(GuardedUInt32.Value)).ToInt32(), Is.EqualTo(4));
        Assert.That(Marshal.OffsetOf<GuardedUInt32>(nameof(GuardedUInt32.After)).ToInt32(), Is.EqualTo(8));

        Int32CheckedBinaryExport GetSignedBinary(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int32CheckedBinaryExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        UInt32CheckedBinaryExport GetUnsignedBinary(string name) =>
            Marshal.GetDelegateForFunctionPointer<UInt32CheckedBinaryExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));
        Int32CheckedUnaryExport GetSignedUnary(string name) =>
            Marshal.GetDelegateForFunctionPointer<Int32CheckedUnaryExport>(
                NativeLibrary.GetExport(handle, ExportPrefix + name));

        var signedOperations = new (string Name, Int32CheckedBinaryExport Function)[] {
            ("TryAddInt32", GetSignedBinary("TryAddInt32")),
            ("TrySubtractInt32", GetSignedBinary("TrySubtractInt32")),
            ("TryMultiplyInt32", GetSignedBinary("TryMultiplyInt32")),
            ("TryDivideInt32", GetSignedBinary("TryDivideInt32")),
            ("TryRemainderInt32", GetSignedBinary("TryRemainderInt32")),
        };
        var unsignedOperations = new (string Name, UInt32CheckedBinaryExport Function)[] {
            ("TryAddUInt32", GetUnsignedBinary("TryAddUInt32")),
            ("TrySubtractUInt32", GetUnsignedBinary("TrySubtractUInt32")),
            ("TryMultiplyUInt32", GetUnsignedBinary("TryMultiplyUInt32")),
            ("TryDivideUInt32", GetUnsignedBinary("TryDivideUInt32")),
            ("TryRemainderUInt32", GetUnsignedBinary("TryRemainderUInt32")),
        };
        var negate = GetSignedUnary("TryNegateInt32");
        var absolute = GetSignedUnary("TryAbsoluteInt32");
        var magnitude = Marshal.GetDelegateForFunctionPointer<MagnitudeInt32Export>(
            NativeLibrary.GetExport(handle, ExportPrefix + "MagnitudeInt32"));

        int[] signedValues = [
            int.MinValue, int.MinValue + 1, -65537, -65536, -65535, -1, 0, 1, 65535, 65536, 65537,
            int.MaxValue - 1, int.MaxValue,
        ];
        uint[] unsignedValues = [
            0, 1, 2, 65535, 65536, 65537, uint.MaxValue - 1, uint.MaxValue,
        ];

        bool TryExpectedSigned(string operation, int leftValue, int rightValue, out int expected)
        {
            var left = new BigInteger(leftValue);
            var right = new BigInteger(rightValue);
            BigInteger result;
            switch (operation)
            {
                case "TryAddInt32":
                    result = left + right;
                    break;
                case "TrySubtractInt32":
                    result = left - right;
                    break;
                case "TryMultiplyInt32":
                    result = left * right;
                    break;
                case "TryDivideInt32":
                    if (rightValue == 0 || (leftValue == int.MinValue && rightValue == -1))
                    {
                        expected = 0;
                        return false;
                    }
                    result = left / right;
                    break;
                case "TryRemainderInt32":
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

            if (result < int.MinValue || result > int.MaxValue)
            {
                expected = 0;
                return false;
            }

            expected = (int)result;
            return true;
        }

        bool TryExpectedUnsigned(string operation, uint leftValue, uint rightValue, out uint expected)
        {
            var left = new BigInteger(leftValue);
            var right = new BigInteger(rightValue);
            BigInteger result;
            switch (operation)
            {
                case "TryAddUInt32":
                    result = left + right;
                    break;
                case "TrySubtractUInt32":
                    result = left - right;
                    break;
                case "TryMultiplyUInt32":
                    result = left * right;
                    break;
                case "TryDivideUInt32":
                    if (rightValue == 0)
                    {
                        expected = 0;
                        return false;
                    }
                    result = left / right;
                    break;
                case "TryRemainderUInt32":
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

            if (result < BigInteger.Zero || result > uint.MaxValue)
            {
                expected = 0;
                return false;
            }

            expected = (uint)result;
            return true;
        }

        foreach (var (name, function) in signedOperations)
        {
            foreach (var left in signedValues)
            {
                foreach (var right in signedValues)
                {
                    var expectedSuccess = TryExpectedSigned(name, left, right, out var expected);
                    var guarded = new GuardedInt32 {
                        Before = LowerSignedCanary,
                        Value = SignedOutputSentinel,
                        After = UpperSignedCanary,
                    };
                    var actual = function(left, right, ref guarded.Value);
                    Assert.That(actual, Is.EqualTo(expectedSuccess ? 1 : 0));
                    Assert.That(guarded.Value, Is.EqualTo(expectedSuccess ? expected : SignedOutputSentinel));
                    Assert.That(guarded.Before, Is.EqualTo(LowerSignedCanary));
                    Assert.That(guarded.After, Is.EqualTo(UpperSignedCanary));
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
                    var guarded = new GuardedUInt32 {
                        Before = LowerUnsignedCanary,
                        Value = UnsignedOutputSentinel,
                        After = UpperUnsignedCanary,
                    };
                    var actual = function(left, right, ref guarded.Value);
                    Assert.That(actual, Is.EqualTo(expectedSuccess ? 1 : 0));
                    Assert.That(guarded.Value, Is.EqualTo(expectedSuccess ? expected : UnsignedOutputSentinel));
                    Assert.That(guarded.Before, Is.EqualTo(LowerUnsignedCanary));
                    Assert.That(guarded.After, Is.EqualTo(UpperUnsignedCanary));
                }
            }
        }

        foreach (var value in signedValues)
        {
            var expectedSuccess = value != int.MinValue;
            var expectedMagnitude = (uint)BigInteger.Abs(new BigInteger(value));
            Assert.That(magnitude(value), Is.EqualTo(expectedMagnitude));
            var expectedNegation = expectedSuccess ? -value : 0;
            var negateGuard = new GuardedInt32 {
                Before = LowerSignedCanary,
                Value = SignedOutputSentinel,
                After = UpperSignedCanary,
            };
            var negateResult = negate(value, ref negateGuard.Value);
            Assert.That(negateResult, Is.EqualTo(expectedSuccess ? 1 : 0));
            Assert.That(negateGuard.Value, Is.EqualTo(expectedSuccess ? expectedNegation : SignedOutputSentinel));
            Assert.That(negateGuard.Before, Is.EqualTo(LowerSignedCanary));
            Assert.That(negateGuard.After, Is.EqualTo(UpperSignedCanary));

            var absoluteGuard = new GuardedInt32 {
                Before = LowerSignedCanary,
                Value = SignedOutputSentinel,
                After = UpperSignedCanary,
            };
            var absoluteResult = absolute(value, ref absoluteGuard.Value);
            Assert.That(absoluteResult, Is.EqualTo(expectedSuccess ? 1 : 0));
            Assert.That(absoluteGuard.Value, Is.EqualTo(expectedSuccess ? (int)expectedMagnitude : SignedOutputSentinel));
            Assert.That(absoluteGuard.Before, Is.EqualTo(LowerSignedCanary));
            Assert.That(absoluteGuard.After, Is.EqualTo(UpperSignedCanary));
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

        Assert.That(Marshal.SizeOf<GuardedUInt32>(), Is.EqualTo(12));
        Assert.That(Marshal.OffsetOf<GuardedUInt32>(nameof(GuardedUInt32.Before)).ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<GuardedUInt32>(nameof(GuardedUInt32.Value)).ToInt32(), Is.EqualTo(4));
        Assert.That(Marshal.OffsetOf<GuardedUInt32>(nameof(GuardedUInt32.After)).ToInt32(), Is.EqualTo(8));
        Assert.That(Marshal.SizeOf<GuardedUInt64>(), Is.EqualTo(24));
        Assert.That(Marshal.OffsetOf<GuardedUInt64>(nameof(GuardedUInt64.Before)).ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<GuardedUInt64>(nameof(GuardedUInt64.Value)).ToInt32(), Is.EqualTo(8));
        Assert.That(Marshal.OffsetOf<GuardedUInt64>(nameof(GuardedUInt64.After)).ToInt32(), Is.EqualTo(16));

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
            Assert.That(leadingZeroCount32(value), Is.EqualTo(BitOperations.LeadingZeroCount(value)));
            Assert.That(trailingZeroCount32(value), Is.EqualTo(BitOperations.TrailingZeroCount(value)));
            Assert.That(popCount32(value), Is.EqualTo(BitOperations.PopCount(value)));
            Assert.That(reverseBits32(value), Is.EqualTo(ReverseBits32(value)));
            Assert.That(reverseEndianness32(value), Is.EqualTo(BinaryPrimitives.ReverseEndianness(value)));
            Assert.That(isPow232(value), Is.EqualTo(BitOperations.IsPow2(value) ? 1 : 0));
            foreach (var count in rotationCounts)
            {
                Assert.That(rotateLeft32(value, count), Is.EqualTo(BitOperations.RotateLeft(value, count)));
                Assert.That(rotateRight32(value, count), Is.EqualTo(BitOperations.RotateRight(value, count)));
            }
        }

        foreach (var value in values64)
        {
            Assert.That(leadingZeroCount64(value), Is.EqualTo(BitOperations.LeadingZeroCount(value)));
            Assert.That(trailingZeroCount64(value), Is.EqualTo(BitOperations.TrailingZeroCount(value)));
            Assert.That(popCount64(value), Is.EqualTo(BitOperations.PopCount(value)));
            Assert.That(reverseBits64(value), Is.EqualTo(ReverseBits64(value)));
            Assert.That(reverseEndianness64(value), Is.EqualTo(BinaryPrimitives.ReverseEndianness(value)));
            Assert.That(isPow264(value), Is.EqualTo(BitOperations.IsPow2(value) ? 1 : 0));
            foreach (var count in rotationCounts)
            {
                Assert.That(rotateLeft64(value, count), Is.EqualTo(BitOperations.RotateLeft(value, count)));
                Assert.That(rotateRight64(value, count), Is.EqualTo(BitOperations.RotateRight(value, count)));
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
            Assert.That(actual, Is.EqualTo(expectedSuccess ? 1 : 0));
            Assert.That(guarded.Value, Is.EqualTo(expectedSuccess ? expected : UInt32OutputSentinel));
            Assert.That(guarded.Before, Is.EqualTo(Lower32Canary));
            Assert.That(guarded.After, Is.EqualTo(Upper32Canary));
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
            Assert.That(actual, Is.EqualTo(expectedSuccess ? 1 : 0));
            Assert.That(guarded.Value, Is.EqualTo(expectedSuccess ? expected : UInt64OutputSentinel));
            Assert.That(guarded.Before, Is.EqualTo(Lower64Canary));
            Assert.That(guarded.After, Is.EqualTo(Upper64Canary));
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

        Assert.That(Marshal.SizeOf<GuardedInt32>(), Is.EqualTo(12));
        Assert.That(Marshal.OffsetOf<GuardedInt32>(nameof(GuardedInt32.Before)).ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<GuardedInt32>(nameof(GuardedInt32.Value)).ToInt32(), Is.EqualTo(4));
        Assert.That(Marshal.OffsetOf<GuardedInt32>(nameof(GuardedInt32.After)).ToInt32(), Is.EqualTo(8));
        Assert.That(Marshal.SizeOf<GuardedInt64>(), Is.EqualTo(24));
        Assert.That(Marshal.OffsetOf<GuardedInt64>(nameof(GuardedInt64.Before)).ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<GuardedInt64>(nameof(GuardedInt64.Value)).ToInt32(), Is.EqualTo(8));
        Assert.That(Marshal.OffsetOf<GuardedInt64>(nameof(GuardedInt64.After)).ToInt32(), Is.EqualTo(16));

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
                Assert.That(abs32(value), Is.EqualTo(Math.Abs(value)));
            }
            Assert.That(sign32(value), Is.EqualTo(Math.Sign(value)));

            var expectedSuccess = value != int.MinValue;
            var guarded = new GuardedInt32 {
                Before = LowerInt32Canary,
                Value = Int32OutputSentinel,
                After = UpperInt32Canary,
            };
            var actual = tryAbs32(value, ref guarded.Value);
            Assert.That(actual, Is.EqualTo(expectedSuccess ? 1 : 0));
            Assert.That(guarded.Value, Is.EqualTo(expectedSuccess ? Math.Abs(value) : Int32OutputSentinel));
            Assert.That(guarded.Before, Is.EqualTo(LowerInt32Canary));
            Assert.That(guarded.After, Is.EqualTo(UpperInt32Canary));
        }

        foreach (var left in values32)
        {
            foreach (var right in values32)
            {
                Assert.That(min32(left, right), Is.EqualTo(Math.Min(left, right)));
                Assert.That(max32(left, right), Is.EqualTo(Math.Max(left, right)));
                Assert.That(multiply32(left, right), Is.EqualTo((long)left * right));
                var expectedGcd = BigInteger.GreatestCommonDivisor(
                    BigInteger.Abs(new BigInteger(left)), BigInteger.Abs(new BigInteger(right)));
                Assert.That(gcd32(left, right), Is.EqualTo((uint)expectedGcd));
            }
        }

        (int Minimum, int Maximum)[] clampRanges32 = [
            (int.MinValue, int.MaxValue), (int.MinValue, -1), (-5, 5), (0, int.MaxValue), (int.MaxValue, int.MaxValue),
        ];
        foreach (var value in values32)
        {
            foreach (var (minimum, maximum) in clampRanges32)
            {
                Assert.That(clamp32(value, minimum, maximum), Is.EqualTo(Math.Clamp(value, minimum, maximum)));
            }
        }

        foreach (var value in values64)
        {
            if (value != long.MinValue)
            {
                Assert.That(abs64(value), Is.EqualTo(Math.Abs(value)));
            }
            Assert.That(sign64(value), Is.EqualTo(Math.Sign(value)));

            var expectedSuccess = value != long.MinValue;
            var guarded = new GuardedInt64 {
                Before = LowerInt64Canary,
                Value = Int64OutputSentinel,
                After = UpperInt64Canary,
            };
            var actual = tryAbs64(value, ref guarded.Value);
            Assert.That(actual, Is.EqualTo(expectedSuccess ? 1 : 0));
            Assert.That(guarded.Value, Is.EqualTo(expectedSuccess ? Math.Abs(value) : Int64OutputSentinel));
            Assert.That(guarded.Before, Is.EqualTo(LowerInt64Canary));
            Assert.That(guarded.After, Is.EqualTo(UpperInt64Canary));
        }

        foreach (var left in values64)
        {
            foreach (var right in values64)
            {
                Assert.That(min64(left, right), Is.EqualTo(Math.Min(left, right)));
                Assert.That(max64(left, right), Is.EqualTo(Math.Max(left, right)));
                var expectedGcd = BigInteger.GreatestCommonDivisor(
                    BigInteger.Abs(new BigInteger(left)), BigInteger.Abs(new BigInteger(right)));
                Assert.That(gcd64(left, right), Is.EqualTo((ulong)expectedGcd));
            }
        }

        (long Minimum, long Maximum)[] clampRanges64 = [
            (long.MinValue, long.MaxValue), (long.MinValue, -1), (-5, 5), (0, long.MaxValue), (long.MaxValue, long.MaxValue),
        ];
        foreach (var value in values64)
        {
            foreach (var (minimum, maximum) in clampRanges64)
            {
                Assert.That(clamp64(value, minimum, maximum), Is.EqualTo(Math.Clamp(value, minimum, maximum)));
            }
        }
    }
    [TestCase("Unknown")]
    [TestCase("Hidden")]
    [TestCase("\u03B1")]
    public async Task InvalidLibraryExportsPreservePriorArtifactsAsync(string export)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-library-export-{Guid.NewGuid():N}.dll");
        const string Source = "public int32 Value() { return 42; } int32 Hidden() { return 1; } public int32 \u03B1() { return 2; }";
        try
        {
            var (exit, _, _) = await RunAsync(Encoding.UTF8.GetBytes(Source), "build-library", "-", "--export", "Value",
                "-o", path, "--runtime", "fail-fast", "--json");
            Assert.That(exit, Is.EqualTo(0));
            var original = await File.ReadAllBytesAsync(path);
            var (failed, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), "build-library", "-", "--export", export,
                "-o", path, "--runtime", "fail-fast", "--json");
            Assert.That(failed, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM4002"));
            Assert.That(document.RootElement.TryGetProperty("artifact", out _), Is.False);
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase("analyze")]
    [TestCase("build")]
    [TestCase("jit")]
    public async Task SourceSetsResolveNamespacesAndValueMembersAcrossFilesAsync(string command)
    {
        const string Entry = "using Parts; int32 Main() { Box value = new Box(10); return value.Total(32); }";
        const string Library = "namespace Parts; [Blittable] [Layout(LayoutKind.Sequential)] public struct Box { public int32 Value; public Box(int32 value) { Value = value; } public int32 Total(int32 extra) { return Value + extra; } }";
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-set-{Guid.NewGuid():N}");
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
            Assert.That(error, Is.Empty);
            Assert.That(exit, Is.EqualTo(command == "jit" ? 42 : 0));
            if (command == "jit")
            {
                Assert.That(output, Is.Empty);
                return;
            }

            using var json = JsonDocument.Parse(output);
            Assert.That(json.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(json.RootElement.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(json.RootElement.GetProperty("command").GetString(), Is.EqualTo(command));
            var sources = json.RootElement.GetProperty("sources");
            Assert.That(sources.GetArrayLength(), Is.EqualTo(2));
            Assert.That(sources[0].GetProperty("path").GetString(), Is.EqualTo("<stdin>"));
            Assert.That(sources[1].GetProperty("path").GetString(), Is.EqualTo(library));
            Assert.That(sources[0].GetProperty("sourceLength").GetInt32(), Is.EqualTo(Entry.Length));
            Assert.That(sources[1].GetProperty("sourceLength").GetInt32(), Is.EqualTo(Library.Length));
            Assert.That(json.RootElement.TryGetProperty("contentId", out _), Is.False);
            if (command == "build")
            {
                var bytes = await File.ReadAllBytesAsync(image);
                Assert.That(json.RootElement.GetProperty("artifact").GetProperty("sha256").GetString(), Is.EqualTo(Convert.ToHexString(SHA256.HashData(bytes))));
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
                Assert.That(process.ExitCode, Is.EqualTo(42));
            }
        }
        finally
        {
            File.Delete(library);
            await TestFileCleanup.DeleteWithRetryAsync(image).ConfigureAwait(true);
        }
    }
    [TestCase("analyze",true)]
    [TestCase("build",true)]
    [TestCase("jit",true)]
    [TestCase("analyze",false)]
    public async Task SourceSetErrorsKeepTheirOwningSnapshotAndPreserveOutputAsync(string command, bool json)
    {
        const string Invalid = "int32 Helper() { return missing; }";
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-errors-{Guid.NewGuid():N}");
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
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(await File.ReadAllBytesAsync(image), Is.EqualTo(original));
            if (json)
            {
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
                Assert.That(document.RootElement.GetProperty("sources")[1].GetProperty("path").GetString(), Is.EqualTo(helper));
                var diagnostic = global::System.Linq.Enumerable.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
                Assert.That(diagnostic.GetProperty("sourceIndex").GetInt32(), Is.EqualTo(1));
                Assert.That(diagnostic.GetProperty("span").GetProperty("start").GetInt32(), Is.EqualTo(Invalid.IndexOf("missing", StringComparison.Ordinal)));
                Assert.That(document.RootElement.TryGetProperty("artifact", out _), Is.False);
            }
            else
            {
                Assert.That(output, Is.Empty);
                Assert.That(error, Does.Contain(helper + "(1,"));
                Assert.That(error, Does.Contain("missing"));
            }
        }
        finally
        {
            File.Delete(helper);
            File.Delete(image);
        }
    }

    [Test]
    public async Task MultiSourceJsonDiagnosticsKeepTheirSourceIndexesAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-index-{Guid.NewGuid():N}");
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

            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
            var sourceIndexes = document.RootElement.GetProperty("diagnostics").EnumerateArray()
                .Select(static diagnostic => diagnostic.GetProperty("sourceIndex").GetInt32())
                .OrderBy(static index => index);
            Assert.That(sourceIndexes, Is.EqualTo([0, 1, 2]));
        }
        finally
        {
            File.Delete(entry);
            File.Delete(first);
            File.Delete(second);
        }
    }
    [TestCase("build",".exe")]
    [TestCase("build-library",".dll")]
    public async Task SourceSetOptionsRejectDuplicatesAndProtectEveryInputAsync(string command, string extension)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-input-{Guid.NewGuid():N}");
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
            Assert.That(exit, Is.EqualTo(2));
            Assert.That(await File.ReadAllTextAsync(helper), Is.EqualTo(Helper));
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM2002"));

            var alias = Path.Combine(Path.GetDirectoryName(entry) ?? throw new InvalidOperationException(), ".", Path.GetFileName(entry));
            var (duplicate, duplicateOutput, _) = await RunAsync([], "analyze", entry, "--source", alias, "--json");
            Assert.That(duplicate, Is.EqualTo(2));
            using var duplicateDocument = JsonDocument.Parse(duplicateOutput);
            Assert.That(duplicateDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
        }
        finally
        {
            File.Delete(entry);
            File.Delete(helper);
        }
    }
    [TestCase("analyze - --source - --json")]
    [TestCase("analyze - --source --json")]
    [TestCase("analyze - --json --json")]
    [TestCase("analyze - --runtime unknown --json")]
    [TestCase("jit - --source another.terrum --json")]
    [TestCase("jit - -o output.exe --runtime fail-fast --json")]
    [TestCase("build - --runtime fail-fast --json")]
    [TestCase("build - -o output.exe --runtime fail-fast --runtime fail-fast --json")]
    [TestCase("build-library - --export Value --runtime fail-fast --json")]
    [TestCase("build-library - -o output.dll --runtime fail-fast --json")]
    [TestCase("build-library - -o output.dll --export Value --json")]
    [TestCase("build-library - -o output.dll --export Value --export Value --runtime fail-fast --json")]
    [TestCase("build - -o output.exe --export Value --runtime fail-fast --json")]
    public async Task SourceSetCommandsRejectMalformedOptionsBeforeReadingInputsAsync(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var (exit, output, error) = await RunAsync([], arguments.Split(' '));
        Assert.That(exit, Is.EqualTo(2));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
    }

    [Test]
    public async Task SourceSetsBoundInputCountBeforeReadingFilesAsync()
    {
        List<string> arguments = ["analyze", "-"];
        for (var index = 0; index < 256; index++)
        {
            arguments.AddRange(["--source", $"missing-{index}.terrum"]);
        }
        arguments.Add("--json");
        var (exit, output, _) = await RunAsync([], [.. arguments]);
        Assert.That(exit, Is.EqualTo(2));
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
    }

    [Test]
    public async Task ScannerPublishesExactIntegerMagnitudesWithoutDefaultTypesOrPrecisionLossAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        const string Source = "0 9223372036854775808 18446744073709551615 18446744073709551616 256 1.0 2e3 3.0";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), "scan-component",
            TerrumProjectPaths.SourceDirectory(root), "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(10));
        Assert.That(document.RootElement.GetProperty("component").GetString(), Is.EqualTo("scanner-10"));
        var integers = document.RootElement.GetProperty("integerValues");
        Assert.That(integers.GetArrayLength(), Is.EqualTo(5));
        string[] tags = ["", "", "", "", ""];
        string?[] magnitudes = ["0", "9223372036854775808", "18446744073709551615", null, "256"];
        for (var index = 0; index < tags.Length; index++)
        {
            Assert.That(integers[index].GetProperty("tokenIndex").GetInt32(), Is.EqualTo(index));
            Assert.That(integers[index].GetProperty("tag").GetString(), Is.EqualTo(tags[index]));
            Assert.That(integers[index].GetProperty("status").GetString(), Is.EqualTo(magnitudes[index] is null ? "overflow" : "exact"));
            if (magnitudes[index] is string magnitude)
            {
                Assert.That(integers[index].GetProperty("magnitude").ValueKind, Is.EqualTo(JsonValueKind.String));
                Assert.That(integers[index].GetProperty("magnitude").GetString(), Is.EqualTo(magnitude));
            }
            else
            {
                Assert.That(integers[index].TryGetProperty("magnitude", out _), Is.False);
            }
        }
    }

    [Test]
    public async Task ScannerWorkerPublishesCompiledTokenRecordsAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("\u03B12 + 42 \U00010400 \U0001F600"),
            "scan-component", TerrumProjectPaths.SourceDirectory(root), "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.EqualTo(""));
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(10));
        Assert.That(document.RootElement.GetProperty("component").GetString(), Is.EqualTo("scanner-10"));
        Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
        var diagnostic = global::System.Linq.Enumerable.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.That(diagnostic.GetProperty("code").GetString(), Is.EqualTo("TRM0001"));
        Assert.That(diagnostic.GetProperty("start").GetInt32(), Is.EqualTo(11));
        Assert.That(diagnostic.GetProperty("length").GetInt32(), Is.EqualTo(2));
        var tokens = document.RootElement.GetProperty("tokens").EnumerateArray().ToArray();
        string[] kinds = ["Identifier", "Plus", "NumericLiteral", "Identifier", "Bad", "EndOfFile"];
        int[] starts = [0, 3, 5, 8, 11, 13];
        int[] lengths = [2, 1, 2, 2, 2, 0];
        Assert.That(tokens.Select(static token => token.GetProperty("kind").GetString()), Is.EqualTo(kinds));
        Assert.That(tokens.Select(static token => token.GetProperty("start").GetInt32()), Is.EqualTo(starts));
        Assert.That(tokens.Select(static token => token.GetProperty("length").GetInt32()), Is.EqualTo(lengths));
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task BuildPublishesNativeArtifactAndFailedCompilationPreservesItAsync(bool nativeScanner)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-cli-{Guid.NewGuid():N}.exe");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        string[] arguments = nativeScanner
            ? ["build-component", TerrumProjectPaths.SourceDirectory(root), "-", "-o", path, "--runtime", "fail-fast", "--json"]
            : ["build", "-", "-o", path, "--runtime", "fail-fast", "--json"];

        try
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { require true; return 23; }"),
                arguments);
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(""));
            using var document = JsonDocument.Parse(output);
            var artifact = document.RootElement.GetProperty("artifact");
            Assert.That(artifact.GetProperty("target").GetString(), Is.EqualTo("windows-x64"));
            Assert.That(artifact.GetProperty("runtimePolicy").GetString(), Is.EqualTo("fail-fast"));
            var original = await File.ReadAllBytesAsync(path);
            Assert.That(artifact.GetProperty("length").GetInt32(), Is.EqualTo(original.Length));
            Assert.That(artifact.GetProperty("sha256").GetString(), Is.EqualTo(Convert.ToHexString(SHA256.HashData(original))));

            var (failure, failedOutput, _) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { return unknown; }"),
                arguments);
            Assert.That(failure, Is.EqualTo(1));
            using var failedDocument = JsonDocument.Parse(failedOutput);
            Assert.That(failedDocument.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(failedDocument.RootElement.TryGetProperty("artifact", out _), Is.False);
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));

            var oversized = "int32 Main() {" + string.Concat(Enumerable.Range(0, 257)
                .Select(static index => FormattableString.Invariant($"int32 v{index} = 0;"))) + "return 0;}";
            var (limitExit, limitOutput, _) = await RunAsync(Encoding.UTF8.GetBytes(oversized),
                arguments);
            Assert.That(limitExit, Is.EqualTo(1));
            using var limitDocument = JsonDocument.Parse(limitOutput);
            Assert.That(limitDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM3016"));
            Assert.That(limitDocument.RootElement.TryGetProperty("artifact", out _), Is.False);
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));

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

            Assert.That(process.ExitCode, Is.EqualTo(23));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
    [TestCase("/* first */ int32 Main() { int32 \u03B1 = 17; return \u03B1 + 25; } // end")]
    [TestCase("int32 F(int32 value) { if (value == 0) { return 1; } return value * F(value - 1); } int32 Main() { return F(5); }")]
    [TestCase("struct S { public int32 Value; public S(int32 value) { Value = value; } } int32 Main() { S value = new S(42); return value.Value; }")]
    [TestCase("struct Box<T> { T value; public Box(T value) { this.value = value; } public T Get() { return this.value; } } int32 Main() { Box<int32> value = new Box<int32>(42); return value.Get(); }")]
    [TestCase("int32 Main() { uint64 value = 18446744073709551615; return int32(value % 1000); }")]
    [TestCase("int32 Main() { int64 minimum = -9223372036854775808; return int32(minimum % -1); }")]
    [TestCase("int32 Main() { mut int32 sum = 0; for (mut int32 i = 0; i < 10; i = i + 1) { sum = sum + i; } return sum; }")]
    [TestCase("int32 End() { return 1; } int32 Main() { require OwnedInt32Buffer.TryCreate(1, out var raw); mut OwnedInt32Buffer buffer = move raw; require buffer.TryAppend(40); ReadonlyInt32BufferView prefix = buffer[..End()]; return prefix.Read(0) + 1; }")]
    [TestCase("int32 Start() { return 1; } int32 End() { return 2; } int32 Main() { require OwnedInt32Buffer.TryCreate(2, out var raw); mut OwnedInt32Buffer buffer = move raw; require buffer.TryAppend(40); require buffer.TryAppend(41); ReadonlyInt32BufferView view = buffer[Start()..End()]; return view.Read(0); }")]
    [TestCase("int32 Start() { return 1; } int32 End() { return 0; } int32 Main() { require OwnedInt32Buffer.TryCreate(2, out var raw); mut OwnedInt32Buffer buffer = move raw; require buffer.TryAppend(40); require buffer.TryAppend(41); ReadonlyInt32BufferView view = buffer[^Start()..^End()]; return view.Read(0); }")]
    [TestCase("float32 Echo(float32 value) { return value; } int32 Main() { _ = Echo(float32(0x1.fp+2)); return 42; }")]
    [TestCase("float64 Widen(float32 value) { return float64(value); } float32 Narrow(float64 value) { return float32(value); } int32 Main() { _ = Narrow(Widen(0.1)); return 42; }")]
    public async Task NativeScannerFeedsTheOfficialCompilerWithoutChangingEmissionAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var original = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-host-{Guid.NewGuid():N}.exe");
        var scanned = original + ".scanned.exe";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(source);
            var (hostExit, hostOutput, hostError) = await RunAsync(bytes, "build", "-", "-o", original,
                "--runtime", "fail-fast", "--json");
            Assert.That(hostExit, Is.EqualTo(0));
            Assert.That(hostError, Is.Empty);
            var (nativeExit, nativeOutput, nativeError) = await RunAsync(bytes, "build-component",
                TerrumProjectPaths.SourceDirectory(root), "-", "-o", scanned, "--runtime", "fail-fast", "--json");
            Assert.That(nativeExit, Is.EqualTo(0));
            Assert.That(nativeError, Is.Empty);
            using var host = JsonDocument.Parse(hostOutput);
            using var native = JsonDocument.Parse(nativeOutput);
            Assert.That(native.RootElement.GetProperty("contentId").GetString(), Is.EqualTo(host.RootElement.GetProperty("contentId").GetString()));
            Assert.That(native.RootElement.GetProperty("command").GetString(), Is.EqualTo("build-component"));
            Assert.That(await File.ReadAllBytesAsync(scanned), Is.EqualTo(await File.ReadAllBytesAsync(original)));
        }
        finally
        {
            File.Delete(original);
            File.Delete(scanned);
        }
    }
    [TestCase("int32 Main() { return 0; } /*","TRM0003")]
    [TestCase("int32 Main() { return 0; }\0","TRM0001")]
    [TestCase("int32 Main() { return 1e+; }","TRM0004")]
    [TestCase("int32 Main() { return 0 }","TRM1001")]
    public async Task NativeScannerBuildPreservesLexicalAndParserDiagnosticsAsync(string source, string code)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scanner-failure-{Guid.NewGuid():N}.exe");
        byte[] previous = [1, 2, 3, 4];
        try
        {
            await File.WriteAllBytesAsync(artifact, previous);
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "build-component",
                TerrumProjectPaths.SourceDirectory(root), "-", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(global::System.Linq.Enumerable.Any(document.RootElement.GetProperty("diagnostics").EnumerateArray(),diagnostic => diagnostic.GetProperty("code").GetString() == code),Is.True);
            Assert.That(await File.ReadAllBytesAsync(artifact), Is.EqualTo(previous));
        }
        finally
        {
            File.Delete(artifact);
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task ScannerBundleCompilationDiagnosticsKeepTheirSourceFileAsync(bool json)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var bundle = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scanner-bundle-{Guid.NewGuid():N}");
        var artifact = bundle + ".exe";
        var characterLiterals = Path.Combine(bundle, "Lexing", "CharacterLiteral.terrum");
        (string Source, string Output)[] components = [
            (Path.Combine("Text", "SourceRange.terrum"), Path.Combine("Text", "SourceRange.terrum")),
            (Path.Combine("Lexing", "CharacterFacts.terrum"), Path.Combine("Lexing", "CharacterFacts.terrum")),
            (Path.Combine("Text", "Utf16CodeUnits.terrum"), Path.Combine("Text", "Utf16CodeUnits.terrum")),
            (Path.Combine("Text", "Utf16Scalar.terrum"), Path.Combine("Text", "Utf16Scalar.terrum")),
            (Path.Combine("Lexing", "CharacterLiteral.terrum"), Path.Combine("Lexing", "CharacterLiteral.terrum")),
            (Path.Combine("Lexing", "StringEscape.terrum"), Path.Combine("Lexing", "StringEscape.terrum")),
            (Path.Combine("Lexing", "Scanner.terrum"), Path.Combine("Lexing", "Scanner.terrum")),
        ];
        try
        {
            Directory.CreateDirectory(bundle);
            foreach (var (sourcePath, outputName) in components)
            {
                var source = await File.ReadAllTextAsync(
                    TerrumProjectPaths.SourceFile(root, sourcePath)).ConfigureAwait(true);
                if (sourcePath == Path.Combine("Lexing", "CharacterLiteral.terrum"))
                {
                    source += "\nint32 Broken() { return 0 }\n";
                }
                var outputPath = Path.Combine(bundle, outputName);
                _ = Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllTextAsync(outputPath, source).ConfigureAwait(true);
            }

            string[] arguments = ["build-component", bundle, "-", "-o", artifact, "--runtime", "fail-fast"];
            if (json)
            {
                arguments = [.. arguments, "--json"];
            }
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                arguments);
            Assert.That(exit, Is.EqualTo(1));
            if (json)
            {
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
                var sources = document.RootElement.GetProperty("sources");
                var characterSourceIndex = Enumerable.Range(0, sources.GetArrayLength())
                    .Single(index => sources[index].GetProperty("path").GetString() == characterLiterals);
                Assert.That(global::System.Linq.Enumerable.Any(document.RootElement.GetProperty("diagnostics").EnumerateArray(),diagnostic => diagnostic.GetProperty("sourceIndex").GetInt32() == characterSourceIndex),Is.True);
            }
            else
            {
                Assert.That(output, Is.Empty);
                Assert.That(error, Does.Contain(characterLiterals + "("));
                Assert.That(error, Does.Contain("TRM1001"));
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

    [Test]
    public void CompilerPrimitivesLibraryExportsExpectedSymbols()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True, "Compiler-primitives exports require Windows x64.");

        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing configuration directory.");
        var root = RepositoryRoot();
        var libraryPath = Path.Combine(root, "artifacts", "bin", "sources", "Terrum.CodeAnalysis.Terrum", configuration,
            "terrum-compiler-primitives.dll");
        Assert.That(File.Exists(libraryPath), Is.True, $"Compiler-primitives library was not built: {libraryPath}");

        var handle = NativeLibrary.Load(libraryPath);
        var testHandle = IntPtr.Zero;
        try
        {
            testHandle = NativeLibrary.Load(s_compilerPrimitivesTestLibrary.Value);
            foreach (var export in new[] {
                "Terrum.CodeAnalysis.SourceRange.TryCreate",
                "Terrum.CodeAnalysis.SourceRange.GetEnd",
                "Terrum.CodeAnalysis.SourceRange.#get:End",
                "Terrum.CodeAnalysis.IsAsciiDigit",
                "Terrum.CodeAnalysis.IsLineBreak",
                "Terrum.CodeAnalysis.GetDecimalDigit",
                "Terrum.CodeAnalysis.TryGetHexDigit",
                "Terrum.CodeAnalysis.DecodeUtf16Scalar",
                "Terrum.CodeAnalysis.Utf16ScalarWidth",
                "Terrum.CodeAnalysis.SelectTokenCopyForAbiTest",
                "Terrum.CodeAnalysis.SelectRangeCopyForAbiTest",
                "Terrum.CodeAnalysis.TrySelectTokenCopyInitForAbiTest",
                "Terrum.CodeAnalysis.TrySelectTokenCopyForAbiTest",
                "Terrum.CodeAnalysis.CanAddInt32ForAbiTest",
                "Terrum.CodeAnalysis.CanSubtractInt32ForAbiTest",
                "Terrum.CodeAnalysis.CanMultiplyInt32ForAbiTest",
                "Terrum.CodeAnalysis.CanDivideInt32ForAbiTest",
                "Terrum.CodeAnalysis.CanRemainderInt32ForAbiTest",
                "Terrum.CodeAnalysis.CanNegateInt32ForAbiTest",
                "Terrum.CodeAnalysis.CanAbsoluteInt32ForAbiTest",
                "Terrum.CodeAnalysis.CanAddUInt32ForAbiTest",
                "Terrum.CodeAnalysis.CanSubtractUInt32ForAbiTest",
                "Terrum.CodeAnalysis.CanMultiplyUInt32ForAbiTest",
                "Terrum.CodeAnalysis.CanDivideUInt32ForAbiTest",
                "Terrum.CodeAnalysis.CanRemainderUInt32ForAbiTest",
                "Terrum.CodeAnalysis.CanAddInt64ForAbiTest",
                "Terrum.CodeAnalysis.CanSubtractInt64ForAbiTest",
                "Terrum.CodeAnalysis.CanMultiplyInt64ForAbiTest",
                "Terrum.CodeAnalysis.CanDivideInt64ForAbiTest",
                "Terrum.CodeAnalysis.CanRemainderInt64ForAbiTest",
                "Terrum.CodeAnalysis.CanNegateInt64ForAbiTest",
                "Terrum.CodeAnalysis.CanAbsoluteInt64ForAbiTest",
                "Terrum.CodeAnalysis.CanAddUInt64ForAbiTest",
                "Terrum.CodeAnalysis.CanSubtractUInt64ForAbiTest",
                "Terrum.CodeAnalysis.CanMultiplyUInt64ForAbiTest",
                "Terrum.CodeAnalysis.CanDivideUInt64ForAbiTest",
                "Terrum.CodeAnalysis.CanRemainderUInt64ForAbiTest",
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
                "Terrum.CodeAnalysis.Numerics64.TryAddInt32",
                "Terrum.CodeAnalysis.Numerics64.TrySubtractInt32",
                "Terrum.CodeAnalysis.Numerics64.TryNegateInt32",
                "Terrum.CodeAnalysis.Numerics64.TryAbsoluteInt32",
                "Terrum.CodeAnalysis.Numerics64.MagnitudeInt32",
                "Terrum.CodeAnalysis.Numerics64.TryMultiplyInt32",
                "Terrum.CodeAnalysis.Numerics64.TryAddUInt32",
                "Terrum.CodeAnalysis.Numerics64.TrySubtractUInt32",
                "Terrum.CodeAnalysis.Numerics64.TryMultiplyUInt32",
                "Terrum.CodeAnalysis.Numerics64.TryDivideInt32",
                "Terrum.CodeAnalysis.Numerics64.TryRemainderInt32",
                "Terrum.CodeAnalysis.Numerics64.TryDivideUInt32",
                "Terrum.CodeAnalysis.Numerics64.TryRemainderUInt32",
                "Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToInt64",
                "Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToUInt64",
                "Terrum.CodeAnalysis.Numerics64.TryConvertUInt32ToInt64",
                "Terrum.CodeAnalysis.Numerics64.TryConvertUInt32ToUInt64",
                "Terrum.CodeAnalysis.Numerics64.TryConvertUInt32ToInt32",
                "Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToUInt32",
                "Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToInt32",
                "Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToUInt32",
                "Terrum.CodeAnalysis.Numerics64.TryConvertUInt64ToInt32",
                "Terrum.CodeAnalysis.Numerics64.TryConvertUInt64ToUInt32",
                "Terrum.CodeAnalysis.Numerics64.TryConvertUInt64ToInt64",
                "Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToUInt64",
                "Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToChar16",
                "Terrum.CodeAnalysis.Numerics64.TryConvertUInt32ToChar16",
                "Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToChar16",
                "Terrum.CodeAnalysis.Numerics64.TryConvertUInt64ToChar16",
                "Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToInt32",
                "Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToUInt32",
                "Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToInt64",
                "Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToUInt64",
                "Terrum.CodeAnalysis.Numerics64.SaturateInt32ToInt64",
                "Terrum.CodeAnalysis.Numerics64.SaturateInt32ToUInt64",
                "Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToInt64",
                "Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToUInt64",
                "Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToInt32",
                "Terrum.CodeAnalysis.Numerics64.SaturateInt32ToUInt32",
                "Terrum.CodeAnalysis.Numerics64.SaturateInt64ToInt32",
                "Terrum.CodeAnalysis.Numerics64.SaturateInt64ToUInt32",
                "Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToInt32",
                "Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToUInt32",
                "Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToInt64",
                "Terrum.CodeAnalysis.Numerics64.SaturateInt64ToUInt64",
                "Terrum.CodeAnalysis.Numerics64.SaturateInt32ToChar16",
                "Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToChar16",
                "Terrum.CodeAnalysis.Numerics64.SaturateInt64ToChar16",
                "Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToChar16",
                "Terrum.CodeAnalysis.Numerics64.SaturateChar16ToInt32",
                "Terrum.CodeAnalysis.Numerics64.SaturateChar16ToUInt32",
                "Terrum.CodeAnalysis.Numerics64.SaturateChar16ToInt64",
                "Terrum.CodeAnalysis.Numerics64.SaturateChar16ToUInt64",
                "Terrum.CodeAnalysis.Numerics64.WrapInt32ToInt64",
                "Terrum.CodeAnalysis.Numerics64.WrapInt32ToUInt64",
                "Terrum.CodeAnalysis.Numerics64.WrapUInt32ToInt64",
                "Terrum.CodeAnalysis.Numerics64.WrapUInt32ToUInt64",
                "Terrum.CodeAnalysis.Numerics64.WrapInt32ToUInt32",
                "Terrum.CodeAnalysis.Numerics64.WrapUInt32ToInt32",
                "Terrum.CodeAnalysis.Numerics64.WrapInt64ToUInt32",
                "Terrum.CodeAnalysis.Numerics64.WrapInt64ToInt32",
                "Terrum.CodeAnalysis.Numerics64.WrapUInt64ToUInt32",
                "Terrum.CodeAnalysis.Numerics64.WrapUInt64ToInt32",
                "Terrum.CodeAnalysis.Numerics64.WrapInt64ToUInt64",
                "Terrum.CodeAnalysis.Numerics64.WrapUInt64ToInt64",
                "Terrum.CodeAnalysis.Numerics64.WrapInt32ToChar16",
                "Terrum.CodeAnalysis.Numerics64.WrapUInt32ToChar16",
                "Terrum.CodeAnalysis.Numerics64.WrapInt64ToChar16",
                "Terrum.CodeAnalysis.Numerics64.WrapUInt64ToChar16",
                "Terrum.CodeAnalysis.Numerics64.WrapChar16ToInt32",
                "Terrum.CodeAnalysis.Numerics64.WrapChar16ToUInt32",
                "Terrum.CodeAnalysis.Numerics64.WrapChar16ToInt64",
                "Terrum.CodeAnalysis.Numerics64.WrapChar16ToUInt64",
                "Terrum.CodeAnalysis.Numerics64.TryGetUInt64BaseLength",
                "Terrum.CodeAnalysis.Numerics64.TryGetInt64BaseLength",
                "Terrum.CodeAnalysis.Numerics64.TryGetUInt32BaseLength",
                "Terrum.CodeAnalysis.Numerics64.TryGetInt32BaseLength",
                "Terrum.CodeAnalysis.Numerics64.TryGetChar16BaseLength",
                "Terrum.CodeAnalysis.Numerics64.FormatUInt64",
                "Terrum.CodeAnalysis.Numerics64.FormatInt64",
                "Terrum.CodeAnalysis.Numerics64.FormatUInt32",
                "Terrum.CodeAnalysis.Numerics64.FormatInt32",
                "Terrum.CodeAnalysis.Numerics64.FormatChar16",
                "Terrum.CodeAnalysis.Numerics64.FormatUInt64Base",
                "Terrum.CodeAnalysis.Numerics64.FormatUInt64BaseUpper",
                "Terrum.CodeAnalysis.Numerics64.FormatInt64Base",
                "Terrum.CodeAnalysis.Numerics64.FormatInt64BaseUpper",
                "Terrum.CodeAnalysis.Numerics64.FormatUInt32Base",
                "Terrum.CodeAnalysis.Numerics64.FormatUInt32BaseUpper",
                "Terrum.CodeAnalysis.Numerics64.FormatInt32Base",
                "Terrum.CodeAnalysis.Numerics64.FormatInt32BaseUpper",
                "Terrum.CodeAnalysis.Numerics64.FormatChar16Base",
                "Terrum.CodeAnalysis.Numerics64.FormatChar16BaseUpper",
                "Terrum.CodeAnalysis.Numerics64.CreateFormattingWorkspaceForAbiTest",
                "Terrum.CodeAnalysis.Numerics64.ConsumeFormattingWorkspaceForAbiTest",
                "Terrum.CodeAnalysis.Token.#ctor",
                "Terrum.CodeAnalysis.SelectTokenForAbiTest",
                "Terrum.CodeAnalysis.Numerics64.AbsFloat32",
                "Terrum.CodeAnalysis.Numerics64.MinFloat32",
                "Terrum.CodeAnalysis.Numerics64.MaxFloat32",
                "Terrum.CodeAnalysis.Numerics64.ClampFloat32",
                "Terrum.CodeAnalysis.Numerics64.AbsFloat64",
                "Terrum.CodeAnalysis.Numerics64.MinFloat64",
                "Terrum.CodeAnalysis.Numerics64.MaxFloat64",
                "Terrum.CodeAnalysis.Numerics64.ClampFloat64",
            })
            {
                if (export.EndsWith("ForAbiTest", StringComparison.Ordinal))
                {
                    Assert.That(NativeLibrary.TryGetExport(handle, export, out _), Is.False,
                        $"The source artifact must not export test adapter '{export}'.");
                    Assert.That(NativeLibrary.TryGetExport(testHandle, export, out _), Is.True,
                        $"The test artifact is missing adapter '{export}'.");
                }
                else
                {
                    Assert.That(NativeLibrary.TryGetExport(handle, export, out _), Is.True,
                        $"Compiler-primitives library is missing export '{export}'.");
                }
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
                Assert.That(decode(first, second), Is.EqualTo(expected));
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
                Assert.That(scalarWidth(scalar), Is.EqualTo(expected));
            }

            Span<char> utf16 = stackalloc char[2];
            utf16[1] = '\uDC00';
            for (var codeUnit = (int)char.MinValue; codeUnit <= char.MaxValue; codeUnit++)
            {
                utf16[0] = (char)codeUnit;
                var status = Rune.DecodeFromUtf16(utf16, out var expected, out _);
                var expectedScalar = status == OperationStatus.Done ? expected.Value : -1;
                Assert.That(decode(utf16[0], utf16[1]), Is.EqualTo(expectedScalar));
                if (!char.IsSurrogate((char)codeUnit))
                {
                    Assert.That(scalarWidth(codeUnit), Is.EqualTo(1));
                }
            }

            utf16[1] = '\0';
            for (var codeUnit = 0xD800; codeUnit <= 0xDFFF; codeUnit++)
            {
                utf16[0] = (char)codeUnit;
                var status = Rune.DecodeFromUtf16(utf16, out var expected, out _);
                var expectedScalar = status == OperationStatus.Done ? expected.Value : -1;
                Assert.That(decode(utf16[0], utf16[1]), Is.EqualTo(expectedScalar));
            }

            for (var scalar = 0x10000; scalar < 0x110000; scalar++)
            {
                var expected = new Rune(scalar);
                Assert.That(expected.EncodeToUtf16(utf16), Is.EqualTo(2));
                Assert.That(decode(utf16[0], utf16[1]), Is.EqualTo(scalar));
                Assert.That(scalarWidth(scalar), Is.EqualTo(2));
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
                Assert.That(magnitudeInt64(value), Is.EqualTo(expected));
            }
            Assert.That(magnitudeInt64(long.MinValue), Is.EqualTo(0x8000000000000000UL));
            AssertWideIntegerAccumulatorExports(handle);
            AssertWideIntegerArithmeticExports(handle);
            AssertBitOperationsExports(handle);
            AssertIntegerMathExports(handle);
            AssertIntegerCheckedArithmeticExports(handle);
            AssertIntegerCheckedConversionExports(handle);
            AssertIntegerSaturationExports(handle);
            AssertIntegerWrappingExports(handle);
            AssertIntegerFormattingLengthExports(handle);
            AssertIntegerFormattingBufferExports(handle, testHandle);
            AssertFloatingMathSelectionExports(handle);
            AssertIntegerFloatConversionExports(handle);
            AssertFloatingFormatConversionExports(handle);
            AssertFloatingNegationExports(handle);
            AssertFloatingComparisonExports(handle);
            AssertFloatingArithmeticExports(handle);
            AssertFloatingIntegerConversionExports(handle);
            AssertTokenAggregateExports(handle, testHandle);

            var isAsciiDigit = Marshal.GetDelegateForFunctionPointer<Char16PredicateExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.IsAsciiDigit"));
            var isLineBreak = Marshal.GetDelegateForFunctionPointer<Char16PredicateExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.IsLineBreak"));
            var getDecimalDigit = Marshal.GetDelegateForFunctionPointer<Char16ValueExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.GetDecimalDigit"));
            for (var value = 0; value <= ushort.MaxValue; value++)
            {
                var character = (ushort)value;
                Assert.That(isAsciiDigit(character), Is.EqualTo(value >= 48 && value <= 57 ? 1 : 0));
                var expectedLineBreak = value == 10 || value == 13 || value == 133
                    || value == 8232 || value == 8233;
                Assert.That(isLineBreak(character), Is.EqualTo(expectedLineBreak ? 1 : 0));
                if (value >= 48 && value <= 57)
                {
                    Assert.That(getDecimalDigit(character), Is.EqualTo(value - 48));
                }
            }

            Assert.That(Marshal.SizeOf<NativeSourceRange>(), Is.EqualTo(8));
            Assert.That(Marshal.OffsetOf<NativeSourceRange>(nameof(NativeSourceRange.Start)).ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<NativeSourceRange>(nameof(NativeSourceRange.Length)).ToInt32(), Is.EqualTo(4));
            Assert.That(Marshal.SizeOf<GuardedSourceRange>(), Is.EqualTo(16));
            Assert.That(Marshal.OffsetOf<GuardedSourceRange>(nameof(GuardedSourceRange.Range)).ToInt32(), Is.EqualTo(4));
            Assert.That(Marshal.OffsetOf<GuardedSourceRange>(nameof(GuardedSourceRange.After)).ToInt32(), Is.EqualTo(12));

            var tryCreateRange = Marshal.GetDelegateForFunctionPointer<SourceRangeTryCreateExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.SourceRange.TryCreate"));
            var getRangeEnd = Marshal.GetDelegateForFunctionPointer<SourceRangeGetEndExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.SourceRange.GetEnd"));
            var getRangeEndProperty = Marshal.GetDelegateForFunctionPointer<SourceRangeGetEndExport>(
                NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.SourceRange.#get:End"));
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

                Assert.That(expectedRange.HasValue, Is.EqualTo(expectedSuccess));
                var guarded = new GuardedSourceRange {
                    Before = lowerRangeCanary,
                    Range = new NativeSourceRange { Start = sentinelStart, Length = sentinelLength },
                    After = upperRangeCanary,
                };
                Assert.That(tryCreateRange(start, length, ref guarded.Range), Is.EqualTo(expectedSuccess ? 1 : 0));
                Assert.That(guarded.Range.Start, Is.EqualTo(expectedSuccess ? expectedRange.GetValueOrDefault().Start : sentinelStart));
                Assert.That(guarded.Range.Length, Is.EqualTo(expectedSuccess ? expectedRange.GetValueOrDefault().Length : sentinelLength));
                Assert.That(guarded.Before, Is.EqualTo(lowerRangeCanary));
                Assert.That(guarded.After, Is.EqualTo(upperRangeCanary));
                if (expectedSuccess)
                {
                    var expectedEnd = expectedRange.GetValueOrDefault().End;
                    Assert.That(getRangeEnd(guarded.Range), Is.EqualTo(expectedEnd));
                    Assert.That(getRangeEndProperty(guarded.Range), Is.EqualTo(expectedEnd));
                }
            }

            var reusedRange = new GuardedSourceRange {
                Before = lowerRangeCanary,
                Range = new NativeSourceRange { Start = sentinelStart, Length = sentinelLength },
                After = upperRangeCanary,
            };
            Assert.That(tryCreateRange(-1, 0, ref reusedRange.Range), Is.EqualTo(0));
            Assert.That(reusedRange.Range.Start, Is.EqualTo(sentinelStart));
            Assert.That(reusedRange.Range.Length, Is.EqualTo(sentinelLength));
            Assert.That(tryCreateRange(5, 7, ref reusedRange.Range), Is.EqualTo(1));
            Assert.That(reusedRange.Range.Start, Is.EqualTo(5));
            Assert.That(reusedRange.Range.Length, Is.EqualTo(7));
            Assert.That(tryCreateRange(int.MaxValue, 1, ref reusedRange.Range), Is.EqualTo(0));
            Assert.That(reusedRange.Range.Start, Is.EqualTo(5));
            Assert.That(reusedRange.Range.Length, Is.EqualTo(7));
            Assert.That(tryCreateRange(0, int.MaxValue, ref reusedRange.Range), Is.EqualTo(1));
            Assert.That(reusedRange.Range.Start, Is.EqualTo(0));
            Assert.That(reusedRange.Range.Length, Is.EqualTo(int.MaxValue));
            Assert.That(reusedRange.Before, Is.EqualTo(lowerRangeCanary));
            Assert.That(reusedRange.After, Is.EqualTo(upperRangeCanary));

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
                Assert.That(tryGetHexDigit(character, ref words[1]), Is.EqualTo(1));
                Assert.That(words[1], Is.EqualTo(expected));
                Assert.That(words[0], Is.EqualTo(lowerCanary));
                Assert.That(words[2], Is.EqualTo(upperCanary));
            }

            foreach (var character in new ushort[] {
                (ushort)'/', (ushort)':', (ushort)'@', (ushort)'G', (ushort)'`', (ushort)'g',
                0, 0x0661, 0xD800, 0xFFFF,
            })
            {
                words[1] = sentinel;
                Assert.That(tryGetHexDigit(character, ref words[1]), Is.EqualTo(0));
                Assert.That(words[1], Is.EqualTo(sentinel));
                Assert.That(words[0], Is.EqualTo(lowerCanary));
                Assert.That(words[2], Is.EqualTo(upperCanary));
            }

            words[1] = sentinel;
            Assert.That(tryGetHexDigit(0, ref words[1]), Is.EqualTo(0));
            Assert.That(words[1], Is.EqualTo(sentinel));
            Assert.That(tryGetHexDigit((ushort)'f', ref words[1]), Is.EqualTo(1));
            Assert.That(words[1], Is.EqualTo(15));
            Assert.That(tryGetHexDigit((ushort)'g', ref words[1]), Is.EqualTo(0));
            Assert.That(words[1], Is.EqualTo(15));
        }
        finally
        {
            if (testHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(testHandle);
            }
            NativeLibrary.Free(handle);
        }
    }
    [TestCase("int32 Scan() { return 0; }",1)]
    [TestCase("int32 Scan() { require false; return 0; }",unchecked((int)0xE0540003))]
    public async Task ScannerFailureNeverFallsBackToTheHostLexerAsync(string scanner, int expected)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scanner-worker-{Guid.NewGuid():N}");
        var component = prefix + ".terrum";
        var artifact = prefix + ".exe";
        byte[] previous = [1, 2, 3, 4];
        try
        {
            await File.WriteAllTextAsync(component, scanner);
            await File.WriteAllBytesAsync(artifact, previous);
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                "build-component", component, "-", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(exit, Is.EqualTo(expected));
            Assert.That(error, Is.Empty);
            if (exit == 1)
            {
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            }
            Assert.That(await File.ReadAllBytesAsync(artifact), Is.EqualTo(previous));
        }
        finally
        {
            File.Delete(component);
            File.Delete(artifact);
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task ComponentBuildCannotOverwriteEitherSourceAsync(bool scannerIsDestination)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-component-input-{Guid.NewGuid():N}");
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
            Assert.That(exit, Is.EqualTo(2));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM2002"));
            Assert.That(await File.ReadAllTextAsync(scanner), Is.EqualTo(Scanner));
            Assert.That(await File.ReadAllTextAsync(input), Is.EqualTo(Source));
        }
        finally
        {
            File.Delete(scanner);
            File.Delete(input);
        }
    }
    [TestCase("build","-","-o","unused.exe","--json")]
    [TestCase("build","-","-o","unused.exe","--runtime","implicit","--json")]
    [TestCase("build-component","scanner.terrum","-","-o","unused.exe","--json")]
    [TestCase("build-component","scanner.terrum","-","-o","unused.exe","--runtime","implicit","--json")]
    [TestCase("build-component","-","-","-o","unused.exe","--runtime","fail-fast","--json")]
    [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "The xUnit data rows supply non-null argument arrays.")]
    public async Task BuildRequiresExplicitRuntimePolicyAsync(params string[] arguments)
    {
        var (exit, output, _) = await RunAsync([], arguments);
        Assert.That(exit, Is.EqualTo(2));
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
    }

    [Test]
    public async Task AnalyzeRunsBindingRatherThanOnlyParsingAsync()
    {
        var source = Encoding.UTF8.GetBytes("int32 Main() { var x = 1; return x; }");
        var (syntaxExit, _, _) = await RunAsync(source, "check", "-", "--json");
        Assert.That(syntaxExit, Is.EqualTo(0));
        var (semanticExit, output, _) = await RunAsync(source, "analyze", "-", "--json");
        Assert.That(semanticExit, Is.EqualTo(1));
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("profile").GetString(), Is.EqualTo("native-10"));
        Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
    }
    [TestCase("check")]
    [TestCase("analyze")]
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
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.True);
        if (command == "check")
        {
            Assert.That(document.RootElement.GetProperty("languageVersion").GetString(), Is.EqualTo(SyntaxTree.LanguageVersion));
        }
    }
    [TestCase("check")]
    [TestCase("tree")]
    public async Task RepeatedOptionalReportsPreciseStructuredDiagnosticAsync(string command)
    {
        const string Source = "void F(T? /* gap */\n? value) {}";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), command, "-", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.EqualTo(""));
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.That(root.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("languageVersion").GetString(), Is.EqualTo(SyntaxTree.LanguageVersion));
        var diagnostic = global::System.Linq.Enumerable.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.That(diagnostic.GetProperty("code").GetString(), Is.EqualTo("TRM1005"));
        Assert.That(diagnostic.GetProperty("span").GetProperty("start").GetInt32(), Is.EqualTo(Source.LastIndexOf('?', StringComparison.Ordinal)));
        Assert.That(diagnostic.GetProperty("span").GetProperty("length").GetInt32(), Is.EqualTo(1));

        if (command == "tree")
        {
            var reconstructed = string.Concat(root.GetProperty("tokens").EnumerateArray().Select(token =>
                string.Concat(token.GetProperty("leadingTrivia").EnumerateArray().Select(trivia => trivia.GetProperty("text").GetString())) + token.GetProperty("text").GetString()));
            Assert.That(reconstructed, Is.EqualTo(Source));
        }
    }
    [TestCase("name")]
    [TestCase("\"quote\\\" and \\\\ slash\"")]
    [TestCase("\"<tag> \u03B1 \U0001F600\"")]
    public async Task HumanTokensPreserveJsonEscapingAsync(string source)
    {
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "tokens", "-");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.EqualTo(""));
        Assert.That(output, Does.Contain(JsonSerializer.Serialize(source)));
    }
    [TestCase("check","void Main() { return; }",0)]
    [TestCase("tree","void Main() { return 1 }",1)]
    [TestCase("tokens"," \r\n\u03B1 + 12 // end",0)]
    [TestCase("tokens","1 \u0661",1)]
    [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "The xUnit data rows supply non-null source strings.")]
    public async Task EmitsStructuredResultsWithExpectedExitAsync(string command, string source, int exitCode)
    {
        (var exit, var output, var error) = await RunAsync(Encoding.UTF8.GetBytes(source), command, "-", "--json");
        Assert.That(exit, Is.EqualTo(exitCode));
        Assert.That(error, Is.EqualTo(""));
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("coordinateSystem").GetString(), Is.EqualTo("utf-16"));
        Assert.That(root.GetProperty("success").GetBoolean(), Is.EqualTo(exitCode == 0));
        Assert.That(root.GetProperty("sourceLength").GetInt32(), Is.EqualTo(source.Length));

        if (command != "check")
        {
            var reconstructed = string.Concat(root.GetProperty("tokens").EnumerateArray().Select(token =>
                string.Concat(token.GetProperty("leadingTrivia").EnumerateArray().Select(trivia => trivia.GetProperty("text").GetString())) + token.GetProperty("text").GetString()));
            Assert.That(reconstructed, Is.EqualTo(source));
        }

        if (command == "tree")
        {
            Assert.That(root.GetProperty("nodes")[0].GetProperty("kind").GetString(), Is.EqualTo("CompilationUnit"));
            Assert.That(((global::System.Func<string>)(() => { var __nunitActual2 = root.GetProperty("contentId").GetString(); Assert.That(__nunitActual2, Is.TypeOf<string>()); return (string)__nunitActual2!; }))().Length, Is.EqualTo(64));
        }
    }
    [TestCase("void Main() {\nreturn 1\n}","<stdin>(3,1): error TRM1001")]
    [TestCase("void F(T? /* gap */\n? value) {}","<stdin>(2,1): error TRM1005")]
    public async Task HumanErrorsUseOneBasedLocationsAndStderrAsync(string source, string diagnostic)
    {
        (var exit, var output, var error) = await RunAsync(Encoding.UTF8.GetBytes(source), "check", "-");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(output, Is.EqualTo(""));
        Assert.That(error, Does.Contain(diagnostic));
    }

    [Test]
    public async Task InputFailuresRemainStructuredAndDoNotClaimToParseAsync()
    {
        foreach (var arguments in new[] { new[] { "bad", "-", "--json" }, ["check", Path.Combine(TestArtifactPaths.DirectoryPath, Guid.NewGuid() + ".terrum"), "--json"] })
        {
            (var exit, var output, var error) = await RunAsync([], arguments);
            Assert.That(exit, Is.EqualTo(2));
            Assert.That(error, Is.EqualTo(""));
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Does.StartWith("TRM200"));
        }
    }

    [Test]
    public async Task StrictUtf8RejectsInvalidBytesAndAcceptsAnOptionalBomAsync()
    {
        (var exit, var output, _) = await RunAsync([0xFF, 0xFE, 0x00, 0xD8], "check", "-", "--json");
        Assert.That(exit, Is.EqualTo(2));
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM2001"));
        (var validExit, _, _) = await RunAsync([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("void Main() {}")], "check", "-");
        Assert.That(validExit, Is.EqualTo(0));
    }

    [Test]
    public async Task FlatTreeProtocolPreservesReferencesAndDeepTreesAsync()
    {
        var source = "void F() { return " + string.Join(" + ", Enumerable.Repeat("1", 500)) + "; }";
        (var exit, var output, _) = await RunAsync(Encoding.UTF8.GetBytes(source), "tree", "-", "--json");
        Assert.That(exit, Is.EqualTo(0));
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

        Assert.That(nodeReferences[0], Is.EqualTo(0));
        {var __nunitItems0 = nodeReferences.Skip(1);        Assert.Multiple(() => { foreach (var __nunitItem3 in __nunitItems0) { Assert.That(__nunitItem3, Is.EqualTo(1)); } });}
        {var __nunitItems1 = tokenReferences;        Assert.Multiple(() => { foreach (var __nunitItem4 in __nunitItems1) { Assert.That(__nunitItem4, Is.EqualTo(1)); } });}
        (var secondExit, var secondOutput, _) = await RunAsync(Encoding.UTF8.GetBytes(source), "tree", "-", "--json");
        Assert.That(secondExit, Is.EqualTo(exit));
        Assert.That(secondOutput, Is.EqualTo(output));
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
        Assert.That(File.Exists(cli), Is.True, $"CLI was not built: {cli}");
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
