using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt32BinaryPredicateExport(uint left, uint right);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int64BinaryPredicateExport(long left, long right);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt64BinaryPredicateExport(ulong left, ulong right);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int64UnaryPredicateExport(long value);

    [Test]
    public void CompilerPrimitivesLibraryExecutesIntegerFactsPredicates()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True, "Compiler-primitives exports require Windows x64.");

        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing configuration directory.");
        var libraryPath = Path.Combine(RepositoryRoot(), "artifacts", "bin", "self-host", configuration,
            "terrum-compiler-primitives.dll");
        Assert.That(File.Exists(libraryPath), Is.True, $"Compiler-primitives library was not built: {libraryPath}");

        var handle = NativeLibrary.Load(libraryPath);
        try
        {
            const string Prefix = "Terrum.CodeAnalysis.";

            Int32BinaryMathExport GetInt32Binary(string name) =>
                Marshal.GetDelegateForFunctionPointer<Int32BinaryMathExport>(
                    NativeLibrary.GetExport(handle, Prefix + name));
            UInt32BinaryPredicateExport GetUInt32Binary(string name) =>
                Marshal.GetDelegateForFunctionPointer<UInt32BinaryPredicateExport>(
                    NativeLibrary.GetExport(handle, Prefix + name));
            Int64BinaryPredicateExport GetInt64Binary(string name) =>
                Marshal.GetDelegateForFunctionPointer<Int64BinaryPredicateExport>(
                    NativeLibrary.GetExport(handle, Prefix + name));
            UInt64BinaryPredicateExport GetUInt64Binary(string name) =>
                Marshal.GetDelegateForFunctionPointer<UInt64BinaryPredicateExport>(
                    NativeLibrary.GetExport(handle, Prefix + name));

            var int32Checks = new (string Name, Int32BinaryMathExport Function, string Operation)[] {
                ("CanAddInt32ForAbiTest", GetInt32Binary("CanAddInt32ForAbiTest"), "add"),
                ("CanSubtractInt32ForAbiTest", GetInt32Binary("CanSubtractInt32ForAbiTest"), "subtract"),
                ("CanMultiplyInt32ForAbiTest", GetInt32Binary("CanMultiplyInt32ForAbiTest"), "multiply"),
                ("CanDivideInt32ForAbiTest", GetInt32Binary("CanDivideInt32ForAbiTest"), "divide"),
                ("CanRemainderInt32ForAbiTest", GetInt32Binary("CanRemainderInt32ForAbiTest"), "remainder"),
            };
            var uint32Checks = new (string Name, UInt32BinaryPredicateExport Function, string Operation)[] {
                ("CanAddUInt32ForAbiTest", GetUInt32Binary("CanAddUInt32ForAbiTest"), "add"),
                ("CanSubtractUInt32ForAbiTest", GetUInt32Binary("CanSubtractUInt32ForAbiTest"), "subtract"),
                ("CanMultiplyUInt32ForAbiTest", GetUInt32Binary("CanMultiplyUInt32ForAbiTest"), "multiply"),
                ("CanDivideUInt32ForAbiTest", GetUInt32Binary("CanDivideUInt32ForAbiTest"), "divide"),
                ("CanRemainderUInt32ForAbiTest", GetUInt32Binary("CanRemainderUInt32ForAbiTest"), "remainder"),
            };
            var int64Checks = new (string Name, Int64BinaryPredicateExport Function, string Operation)[] {
                ("CanAddInt64ForAbiTest", GetInt64Binary("CanAddInt64ForAbiTest"), "add"),
                ("CanSubtractInt64ForAbiTest", GetInt64Binary("CanSubtractInt64ForAbiTest"), "subtract"),
                ("CanMultiplyInt64ForAbiTest", GetInt64Binary("CanMultiplyInt64ForAbiTest"), "multiply"),
                ("CanDivideInt64ForAbiTest", GetInt64Binary("CanDivideInt64ForAbiTest"), "divide"),
                ("CanRemainderInt64ForAbiTest", GetInt64Binary("CanRemainderInt64ForAbiTest"), "remainder"),
            };
            var uint64Checks = new (string Name, UInt64BinaryPredicateExport Function, string Operation)[] {
                ("CanAddUInt64ForAbiTest", GetUInt64Binary("CanAddUInt64ForAbiTest"), "add"),
                ("CanSubtractUInt64ForAbiTest", GetUInt64Binary("CanSubtractUInt64ForAbiTest"), "subtract"),
                ("CanMultiplyUInt64ForAbiTest", GetUInt64Binary("CanMultiplyUInt64ForAbiTest"), "multiply"),
                ("CanDivideUInt64ForAbiTest", GetUInt64Binary("CanDivideUInt64ForAbiTest"), "divide"),
                ("CanRemainderUInt64ForAbiTest", GetUInt64Binary("CanRemainderUInt64ForAbiTest"), "remainder"),
            };

            var int32Values = new[] {
                int.MinValue, int.MinValue + 1, -46341, -1, 0, 1, 46340, 46341,
                int.MaxValue - 1, int.MaxValue,
            };
            foreach (var left in int32Values)
            {
                foreach (var right in int32Values)
                {
                    foreach (var (name, function, operation) in int32Checks)
                    {
                        var expected = IsSignedPredicate(operation, left, right, int.MinValue, int.MaxValue);
                        var actual = function(left, right);
                        Assert.That(actual == (expected ? 1 : 0), Is.True, $"{name}({left}, {right}) returned {actual}; expected {expected}.");
                    }
                }
            }

            var uint32Values = new[] {
                0U, 1U, 2U, 65535U, 65536U, 2147483647U, 2147483648U, uint.MaxValue - 1, uint.MaxValue,
            };
            foreach (var left in uint32Values)
            {
                foreach (var right in uint32Values)
                {
                    foreach (var (name, function, operation) in uint32Checks)
                    {
                        var expected = IsUnsignedPredicate(operation, left, right, uint.MaxValue);
                        var actual = function(left, right);
                        Assert.That(actual == (expected ? 1 : 0), Is.True, $"{name}({left}, {right}) returned {actual}; expected {expected}.");
                    }
                }
            }

            var int64Values = new[] {
                long.MinValue, long.MinValue + 1, -3037000500L, -1L, 0L, 1L, 3037000499L,
                3037000500L, long.MaxValue - 1, long.MaxValue,
            };
            foreach (var left in int64Values)
            {
                foreach (var right in int64Values)
                {
                    foreach (var (name, function, operation) in int64Checks)
                    {
                        var expected = IsSignedPredicate(operation, left, right, long.MinValue, long.MaxValue);
                        var actual = function(left, right);
                        Assert.That(actual == (expected ? 1 : 0), Is.True, $"{name}({left}, {right}) returned {actual}; expected {expected}.");
                    }
                }
            }

            var uint64Values = new[] {
                0UL, 1UL, 2UL, 4294967295UL, 4294967296UL, 9223372036854775807UL,
                9223372036854775808UL, ulong.MaxValue - 1, ulong.MaxValue,
            };
            foreach (var left in uint64Values)
            {
                foreach (var right in uint64Values)
                {
                    foreach (var (name, function, operation) in uint64Checks)
                    {
                        var expected = IsUnsignedPredicate(operation, left, right, ulong.MaxValue);
                        var actual = function(left, right);
                        Assert.That(actual == (expected ? 1 : 0), Is.True, $"{name}({left}, {right}) returned {actual}; expected {expected}.");
                    }
                }
            }

            var canNegateInt32 = Marshal.GetDelegateForFunctionPointer<Int32UnaryMathExport>(
                NativeLibrary.GetExport(handle, Prefix + "CanNegateInt32ForAbiTest"));
            var canAbsoluteInt32 = Marshal.GetDelegateForFunctionPointer<Int32UnaryMathExport>(
                NativeLibrary.GetExport(handle, Prefix + "CanAbsoluteInt32ForAbiTest"));
            var canNegateInt64 = Marshal.GetDelegateForFunctionPointer<Int64UnaryPredicateExport>(
                NativeLibrary.GetExport(handle, Prefix + "CanNegateInt64ForAbiTest"));
            var canAbsoluteInt64 = Marshal.GetDelegateForFunctionPointer<Int64UnaryPredicateExport>(
                NativeLibrary.GetExport(handle, Prefix + "CanAbsoluteInt64ForAbiTest"));

            foreach (var value in int32Values)
            {
                var expected = value != int.MinValue ? 1 : 0;
                Assert.That(canNegateInt32(value), Is.EqualTo(expected));
                Assert.That(canAbsoluteInt32(value), Is.EqualTo(expected));
            }
            foreach (var value in int64Values)
            {
                var expected = value != long.MinValue ? 1 : 0;
                Assert.That(canNegateInt64(value), Is.EqualTo(expected));
                Assert.That(canAbsoluteInt64(value), Is.EqualTo(expected));
            }
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }

    private static bool IsSignedPredicate(
        string operation, BigInteger left, BigInteger right, BigInteger minimum, BigInteger maximum) =>
        operation switch {
            "add" => IsInRange(left + right, minimum, maximum),
            "subtract" => IsInRange(left - right, minimum, maximum),
            "multiply" => IsInRange(left * right, minimum, maximum),
            "divide" => !right.IsZero && !(left == minimum && right == -BigInteger.One),
            "remainder" => !right.IsZero,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    private static bool IsUnsignedPredicate(string operation, BigInteger left, BigInteger right, BigInteger maximum) =>
        operation switch {
            "add" => left + right <= maximum,
            "subtract" => left >= right,
            "multiply" => left * right <= maximum,
            "divide" or "remainder" => !right.IsZero,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    private static bool IsInRange(BigInteger value, BigInteger minimum, BigInteger maximum) =>
        value >= minimum && value <= maximum;
}
