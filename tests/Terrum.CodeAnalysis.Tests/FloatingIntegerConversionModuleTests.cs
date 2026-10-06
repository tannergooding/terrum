using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class FloatingIntegerConversionModuleTests
{
    [Theory]
    [InlineData(true, true, BoundRuntimePolicy.FailFast)]
    [InlineData(true, true, BoundRuntimePolicy.CheckedExceptions)]
    [InlineData(true, false, BoundRuntimePolicy.FailFast)]
    [InlineData(true, false, BoundRuntimePolicy.CheckedExceptions)]
    [InlineData(false, true, BoundRuntimePolicy.FailFast)]
    [InlineData(false, true, BoundRuntimePolicy.CheckedExceptions)]
    [InlineData(false, false, BoundRuntimePolicy.FailFast)]
    [InlineData(false, false, BoundRuntimePolicy.CheckedExceptions)]
    public async Task FloatingIntegerConversionModuleMatchesHostedTruncationAsync(
        bool isFloat32,
        bool isSigned,
        BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, "self-host", "FloatingIntegerConversion.terrum");
        var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
        var component = SyntaxTree.Parse(text, path);
        Assert.False(component.HasErrors, string.Join(Environment.NewLine, component.Diagnostics));

        var verificationSource = CreateVerificationSource(isFloat32, isSigned);
        var verification = SyntaxTree.Parse(verificationSource,
            isFloat32 ? "floating32-integer-conversion-module.terrum" : "floating64-integer-conversion-module.terrum");
        Assert.False(verification.HasErrors, string.Join(Environment.NewLine,
            verification.Diagnostics.Select(diagnostic => $"{diagnostic} near \"{GetContext(verificationSource, diagnostic.Span.Start)}\"")));
        var compilation = Compilation.Create([component, verification], policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(0, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
    }

    private static string CreateVerificationSource(bool isFloat32, bool isSigned)
    {
        var source = new StringBuilder("""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
            """);
        var check = 1;
        if (isFloat32)
        {
            foreach (var bits in GetFloat32Inputs())
            {
                float value = BitConverter.Int32BitsToSingle(unchecked((int)bits));
                AppendFloat32Checks(source, bits, value, isSigned, ref check);
            }
        }
        else
        {
            foreach (var bits in GetFloat64Inputs())
            {
                double value = BitConverter.Int64BitsToDouble(unchecked((long)bits));
                AppendFloat64Checks(source, bits, value, isSigned, ref check);
            }
        }

        source.AppendLine("return 0;");
        source.AppendLine("}");
        return source.ToString();
    }

    private static void AppendFloat32Checks(
        StringBuilder source,
        uint bits,
        float value,
        bool isSigned,
        ref int check)
    {
        if (isSigned)
        {
            AppendCheck(source, "TryFloat32BitsToInt32", "uint32", bits,
                TryConvert(() => checked((int)value), out int int32Result), FormatExpected(int32Result), ref check);
            AppendCheck(source, "TryFloat32BitsToInt64", "uint32", bits,
                TryConvert(() => checked((long)value), out long int64Result), FormatExpected(int64Result), ref check);
        }
        else
        {
            AppendCheck(source, "TryFloat32BitsToUInt32", "uint32", bits,
                TryConvert(() => checked((uint)value), out uint uint32Result), FormatExpected(uint32Result), ref check);
            AppendCheck(source, "TryFloat32BitsToUInt64", "uint32", bits,
                TryConvert(() => checked((ulong)value), out ulong uint64Result), FormatExpected(uint64Result), ref check);
        }
    }

    private static void AppendFloat64Checks(
        StringBuilder source,
        ulong bits,
        double value,
        bool isSigned,
        ref int check)
    {
        if (isSigned)
        {
            AppendCheck(source, "TryFloat64BitsToInt32", "uint64", bits,
                TryConvert(() => checked((int)value), out int int32Result), FormatExpected(int32Result), ref check);
            AppendCheck(source, "TryFloat64BitsToInt64", "uint64", bits,
                TryConvert(() => checked((long)value), out long int64Result), FormatExpected(int64Result), ref check);
        }
        else
        {
            AppendCheck(source, "TryFloat64BitsToUInt32", "uint64", bits,
                TryConvert(() => checked((uint)value), out uint uint32Result), FormatExpected(uint32Result), ref check);
            AppendCheck(source, "TryFloat64BitsToUInt64", "uint64", bits,
                TryConvert(() => checked((ulong)value), out ulong uint64Result), FormatExpected(uint64Result), ref check);
        }
    }

    private static void AppendCheck(
        StringBuilder source,
        string function,
        string sourceType,
        ulong bits,
        bool expectedSuccess,
        string expectedValue,
        ref int check)
    {
        var index = check++;
        var local = "result" + index.ToString(CultureInfo.InvariantCulture);
        var bitWidth = sourceType == "uint32" ? 8 : 16;
        source.Append("if (");
        if (expectedSuccess)
        {
            source.Append('!');
        }

        source.Append(function).Append('(')
            .Append(sourceType).Append("(0x")
            .Append(bits.ToString(bitWidth == 8 ? "X8" : "X16", CultureInfo.InvariantCulture))
            .Append("), init var ").Append(local).Append(')');
        if (expectedSuccess)
        {
            source.Append(" || ").Append(local).Append(" != ").Append(expectedValue);
        }
        else
        {
            source.Append(" || ").Append(local).Append(" is init");
        }

        source.Append(") { return ").Append(index + 1).AppendLine("; }");
    }

    private static bool TryConvert<T>(Func<T> convert, out T result) where T : struct
    {
        try
        {
            result = convert();
            return true;
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }

    private static string FormatExpected(object value)
    {
        return value switch
        {
            int number when number == int.MinValue => "int32.MinValue",
            int number => $"int32({number.ToString(CultureInfo.InvariantCulture)})",
            uint number => $"uint32({number.ToString(CultureInfo.InvariantCulture)})",
            long number when number == long.MinValue => "int64.MinValue",
            long number => $"int64({number.ToString(CultureInfo.InvariantCulture)})",
            ulong number => $"uint64({number.ToString(CultureInfo.InvariantCulture)})",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private static string GetContext(string source, int position)
    {
        var start = Math.Max(0, position - 40);
        var length = Math.Min(80, source.Length - start);
        return source.Substring(start, length);
    }

    private static List<uint> GetFloat32Inputs()
    {
        var values = new List<uint>
        {
            0, 0x80000000, 1, 0x80000001,
            0x3F000000, 0xBF000000, 0x3F7FFFFF, 0xBF7FFFFF,
            0x3F800000, 0xBF800000, 0x3FF33333, 0xBFF33333,
            0x4EFFFFFF, 0x4F000000, 0xCF000000, 0xCF000001,
            0x4F7FFFFF, 0x4F800000, 0x5EFFFFFF, 0x5F000000,
            0xDEFFFFFF, 0xDF000000, 0xDF000001, 0x5F7FFFFF, 0x5F800000,
            0x7F800000, 0xFF800000, 0x7FC00000, 0x7FA00001, 0xFFC00001,
        };
        var random = new Random(0xF10A7);
        for (var index = 0; index < 16; index++)
        {
            values.Add((uint)random.NextInt64(0, 1L << 32));
        }

        return values;
    }

    private static List<ulong> GetFloat64Inputs()
    {
        var values = new List<ulong>
        {
            0, 0x8000000000000000, 1, 0x8000000000000001,
            DoubleBits(0.5), DoubleBits(-0.5), DoubleBits(Math.BitDecrement(1.0)),
            DoubleBits(Math.BitDecrement(-1.0)), DoubleBits(1.0), DoubleBits(-1.0),
            DoubleBits(1.9), DoubleBits(-1.9), DoubleBits(-2147483648.9),
            DoubleBits(2147483647.9), DoubleBits(-2147483649.0), DoubleBits(2147483648.0),
            DoubleBits(-0.5), DoubleBits(-1.0), DoubleBits(4294967295.75), DoubleBits(4294967296.0),
            DoubleBits((double)long.MinValue), DoubleBits(Math.BitDecrement((double)long.MinValue)),
            DoubleBits(Math.BitDecrement(9223372036854775808.0)), DoubleBits(9223372036854775808.0),
            DoubleBits(Math.BitDecrement(18446744073709551616.0)), DoubleBits(18446744073709551616.0),
            0x7FF0000000000000, 0xFFF0000000000000, 0x7FF8000000000000,
            0x7FF0000000000001, 0xFFF8000000000001,
        };
        var random = new Random(0x64F10A7);
        for (var index = 0; index < 16; index++)
        {
            values.Add(NextUInt64(random));
        }

        return values;
    }

    private static ulong DoubleBits(double value)
    {
        return unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
    }

    private static ulong NextUInt64(Random random)
    {
        var high = (uint)random.NextInt64(0, 1L << 32);
        var low = (uint)random.NextInt64(0, 1L << 32);
        return ((ulong)high << 32) | low;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Terrum.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate Terrum.slnx.");
    }
}
