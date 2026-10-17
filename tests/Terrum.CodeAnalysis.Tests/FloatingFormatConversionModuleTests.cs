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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class FloatingFormatConversionModuleTests
{
    [TestCase(true,BoundRuntimePolicy.FailFast)]
    [TestCase(true,BoundRuntimePolicy.CheckedExceptions)]
    [TestCase(false,BoundRuntimePolicy.FailFast)]
    [TestCase(false,BoundRuntimePolicy.CheckedExceptions)]
    public async Task FloatingFormatConversionMatchesHostedIeeeRoundingAsync(
        bool widen,
        BoundRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var path = SelfHostPaths.ResolveFile(root, "FloatingFormatConversion.terrum");
        var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
        var component = SyntaxTree.Parse(text, path);
        Assert.That(component.HasErrors, Is.False, string.Join(Environment.NewLine, component.Diagnostics));

        var verificationSource = CreateVerificationSource(widen);
        var verification = SyntaxTree.Parse(verificationSource,
            widen ? "float32-to-float64-module.terrum" : "float64-to-float32-module.terrum");
        Assert.That(verification.HasErrors, Is.False, string.Join(Environment.NewLine,
            verification.Diagnostics.Select(diagnostic =>
                $"{diagnostic} near \"{GetContext(verificationSource, diagnostic.Span.Start)}\"")));
        var compilation = Compilation.Create([component, verification], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var result = NativeJit.Execute(program, (NativeRuntimePolicy)policy);
        Assert.That(result == 0, Is.True, $"Floating conversion fixture {result} failed.");
    }

    private static string CreateVerificationSource(bool widen)
    {
        var source = new StringBuilder("""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
            """);
        var check = 1;
        if (widen)
        {
            foreach (var bits in GetFloat32Inputs())
            {
                var value = BitConverter.Int32BitsToSingle(unchecked((int)bits));
                AppendFloat32ToFloat64Check(source, bits, value, ref check);
            }
        }
        else
        {
            foreach (var bits in GetFloat64Inputs())
            {
                var value = BitConverter.Int64BitsToDouble(unchecked((long)bits));
                AppendFloat64ToFloat32Check(source, bits, value, ref check);
            }
        }

        source.AppendLine("return 0;");
        source.AppendLine("}");
        return source.ToString();
    }

    private static void AppendFloat32ToFloat64Check(
        StringBuilder source,
        uint bits,
        float value,
        ref int check)
    {
        var index = check++;
        if (float.IsNaN(value))
        {
            var result = "result" + index.ToString(CultureInfo.InvariantCulture);
            source.Append("uint64 ").Append(result).Append(" = Float32BitsToFloat64Bits(uint32(0x")
                .Append(bits.ToString("X8", CultureInfo.InvariantCulture)).AppendLine("));");
            source.Append("if ((").Append(result).Append(" & uint64(0x7FF0000000000000))")
                .Append(" != uint64(0x7FF0000000000000) || (").Append(result)
                .Append(" & uint64(0x000FFFFFFFFFFFFF)) == 0) { return ")
                .Append(index).AppendLine("; }");
            return;
        }

        var expected = unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value));
        source.Append("if (Float32BitsToFloat64Bits(uint32(0x")
            .Append(bits.ToString("X8", CultureInfo.InvariantCulture))
            .Append(")) != uint64(0x")
            .Append(expected.ToString("X16", CultureInfo.InvariantCulture))
            .Append(")) { return ").Append(index).AppendLine("; }");
    }

    private static void AppendFloat64ToFloat32Check(
        StringBuilder source,
        ulong bits,
        double value,
        ref int check)
    {
        var index = check++;
        if (double.IsNaN(value))
        {
            var result = "result" + index.ToString(CultureInfo.InvariantCulture);
            source.Append("uint32 ").Append(result).Append(" = Float64BitsToFloat32Bits(uint64(0x")
                .Append(bits.ToString("X16", CultureInfo.InvariantCulture)).AppendLine("));");
            source.Append("if ((").Append(result).Append(" & uint32(0x7F800000))")
                .Append(" != uint32(0x7F800000) || (").Append(result)
                .Append(" & uint32(0x007FFFFF)) == 0) { return ")
                .Append(index).AppendLine("; }");
            return;
        }

        var expected = unchecked((uint)BitConverter.SingleToInt32Bits((float)value));
        source.Append("if (Float64BitsToFloat32Bits(uint64(0x")
            .Append(bits.ToString("X16", CultureInfo.InvariantCulture))
            .Append(")) != uint32(0x")
            .Append(expected.ToString("X8", CultureInfo.InvariantCulture))
            .Append(")) { return ").Append(index).AppendLine("; }");
    }

    private static string GetContext(string source, int position)
    {
        var start = Math.Max(0, position - 40);
        var length = Math.Min(80, source.Length - start);
        return source.Substring(start, length);
    }

    private static List<uint> GetFloat32Inputs()
    {
        var values = new List<uint>();
        Add(0);
        Add(0x80000000);
        Add(1);
        Add(0x80000001);
        Add(0x007FFFFF);
        Add(0x807FFFFF);
        Add(0x00555555);
        Add(0x80555555);
        Add(0x00800000);
        Add(0x80800000);
        Add(0x3F800000);
        Add(0xBF800000);
        Add(0x3DCCCCCD);
        Add(0xBDCCCCCD);
        Add(0x7F7FFFFF);
        Add(0xFF7FFFFF);
        Add(0x7F800000);
        Add(0xFF800000);
        Add(0x7FC00000);
        Add(0x7F800001);
        Add(0xFFC00001);
        for (var bit = 0; bit < 23; bit++)
        {
            Add(1u << bit);
        }

        var random = new Random(0x32F10A7);
        for (var index = 0; index < 16; index++)
        {
            Add((uint)random.NextInt64(0, 1L << 32));
        }

        return values;

        void Add(uint bits)
        {
            if (!values.Contains(bits))
            {
                values.Add(bits);
            }
        }
    }

    private static List<ulong> GetFloat64Inputs()
    {
        var values = new List<ulong>();
        AddSigned(0);
        AddSigned(1);
        AddSigned(0x000FFFFFFFFFFFFF);
        AddSigned(0x0010000000000000);
        AddSigned(DoubleBits(0.1));
        AddSigned(DoubleBits(1.0));
        AddSigned(DoubleBits(double.MaxValue));
        Add(0x7FF0000000000000);
        Add(0xFFF0000000000000);
        Add(0x7FF8000000000000);
        Add(0x7FF0000000000001);
        Add(0xFFF8000000000001);

        ulong[] midpoints =
        [
            0x3FF0000010000000,
            0x3FF0000030000000,
            0x3FFFFFFFF0000000,
            0x47EFFFFFF0000000,
            0x3690000000000000,
            0x36A8000000000000,
            0x380FFFFFE0000000,
        ];
        foreach (var midpoint in midpoints)
        {
            for (ulong offset = 0; offset < 3; offset++)
            {
                AddSigned(midpoint - 1 + offset);
            }
        }

        var random = new Random(0x64F10A7);
        for (var index = 0; index < 16; index++)
        {
            Add(NextUInt64(random));
        }

        return values;

        void Add(ulong bits)
        {
            if (!values.Contains(bits))
            {
                values.Add(bits);
            }
        }

        void AddSigned(ulong bits)
        {
            Add(bits);
            Add(bits | 0x8000000000000000);
        }
    }

    private static ulong DoubleBits(double value) =>
        unchecked((ulong)BitConverter.DoubleToInt64Bits(value));

    private static ulong NextUInt64(Random random)
    {
        var low = (uint)random.NextInt64(0, 1L << 32);
        var high = (uint)random.NextInt64(0, 1L << 32);
        return ((ulong)high << 32) | low;
    }
}
