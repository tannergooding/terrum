using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class IntegerFloatConversionModuleTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task IntegerFloatConversionModuleMatchesHostedRoundingAsync(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var dependencyNames = new[] { "WideIntegerFacts", "IntegerFloatConversion" };
        var trees = new SyntaxTree[dependencyNames.Length + 1];
        for (var index = 0; index < dependencyNames.Length; index++)
        {
            var path = SelfHostPaths.ResolveFile(root, dependencyNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, path);
            Assert.That(trees[index].HasErrors, Is.False, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        trees[^1] = SyntaxTree.Parse(CreateVerificationSource(), "integer-float-conversion-module.terrum");
        var compilation = Compilation.Create(trees, policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
    }

    private static string CreateVerificationSource()
    {
        var source = new StringBuilder("""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
            """);
        var random = new Random(0xF10A7);
        var check = 1;

        foreach (var value in new[]
        {
            int.MinValue, int.MinValue + 1, -16_777_219, -16_777_217, -3, -1, 0, 1, 3,
            16_777_215, 16_777_216, 16_777_217, 16_777_219, int.MaxValue,
        })
        {
            AppendChecks(source, "Int32", "int32", value.ToString(CultureInfo.InvariantCulture),
                unchecked((uint)BitConverter.SingleToInt32Bits((float)value)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value)), ref check);
        }
        for (var index = 0; index < 24; index++)
        {
            var value = random.Next(int.MinValue, int.MaxValue);
            AppendChecks(source, "Int32", "int32", value.ToString(CultureInfo.InvariantCulture),
                unchecked((uint)BitConverter.SingleToInt32Bits((float)value)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value)), ref check);
        }

        foreach (var value in new uint[]
        {
            0, 1, 16_777_215, 16_777_216, 16_777_217, 0x8000_0000, uint.MaxValue,
        })
        {
            AppendChecks(source, "UInt32", "uint32", value.ToString(CultureInfo.InvariantCulture),
                unchecked((uint)BitConverter.SingleToInt32Bits((float)value)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value)), ref check);
        }
        for (var index = 0; index < 24; index++)
        {
            var value = (uint)random.NextInt64(0, 1L << 32);
            AppendChecks(source, "UInt32", "uint32", value.ToString(CultureInfo.InvariantCulture),
                unchecked((uint)BitConverter.SingleToInt32Bits((float)value)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value)), ref check);
        }

        foreach (var value in new long[]
        {
            long.MinValue, long.MinValue + 1, -9_007_199_254_740_995, -9_007_199_254_740_993,
            -16_777_219, -16_777_217, -1, 0, 1, 16_777_217, 16_777_219,
            9_007_199_254_740_993, 9_007_199_254_740_995, long.MaxValue,
        })
        {
            AppendChecks(source, "Int64", "int64", value.ToString(CultureInfo.InvariantCulture),
                unchecked((uint)BitConverter.SingleToInt32Bits((float)value)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value)), ref check);
        }
        for (var index = 0; index < 24; index++)
        {
            var value = NextInt64(random);
            AppendChecks(source, "Int64", "int64", value.ToString(CultureInfo.InvariantCulture),
                unchecked((uint)BitConverter.SingleToInt32Bits((float)value)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value)), ref check);
        }

        var twoTo63 = 1UL << 63;
        var float32HalfUlpAtTwoTo63 = 1UL << 39;
        var float32UlpAtTwoTo63 = 1UL << 40;
        var float64HalfUlpAtTwoTo63 = 1UL << 10;
        var float64UlpAtTwoTo63 = 1UL << 11;
        foreach (var value in new ulong[]
        {
            0, 1, 16_777_215, 16_777_216, 16_777_217, 16_777_219, 9_007_199_254_740_991,
            9_007_199_254_740_992, 9_007_199_254_740_993, 9_007_199_254_740_995,
            0x8000_0000_0000_0000, ulong.MaxValue,
            twoTo63 + float32HalfUlpAtTwoTo63 - 1,
            twoTo63 + float32HalfUlpAtTwoTo63,
            twoTo63 + float32HalfUlpAtTwoTo63 + 1,
            twoTo63 + float32UlpAtTwoTo63 + float32HalfUlpAtTwoTo63,
            twoTo63 + float64HalfUlpAtTwoTo63 - 1,
            twoTo63 + float64HalfUlpAtTwoTo63,
            twoTo63 + float64HalfUlpAtTwoTo63 + 1,
            twoTo63 + float64UlpAtTwoTo63 + float64HalfUlpAtTwoTo63,
        })
        {
            AppendChecks(source, "UInt64", "uint64", value.ToString(CultureInfo.InvariantCulture),
                unchecked((uint)BitConverter.SingleToInt32Bits((float)value)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value)), ref check);
        }
        for (var index = 0; index < 24; index++)
        {
            var value = NextUInt64(random);
            AppendChecks(source, "UInt64", "uint64", value.ToString(CultureInfo.InvariantCulture),
                unchecked((uint)BitConverter.SingleToInt32Bits((float)value)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value)), ref check);
        }

        source.AppendLine("return 0;");
        source.AppendLine("}");
        return source.ToString();
    }

    private static void AppendChecks(
        StringBuilder source,
        string prefix,
        string sourceType,
        string value,
        uint float32Bits,
        ulong float64Bits,
        ref int check)
    {
        source.Append("if (").Append(prefix).Append("ToFloat32Bits(")
            .Append(sourceType).Append('(').Append(value).Append(")) != uint32(0x")
            .Append(float32Bits.ToString("X8", CultureInfo.InvariantCulture))
            .Append(")) { return ").Append(check++).AppendLine("; }");
        source.Append("if (").Append(prefix).Append("ToFloat64Bits(")
            .Append(sourceType).Append('(').Append(value).Append(")) != uint64(0x")
            .Append(float64Bits.ToString("X16", CultureInfo.InvariantCulture))
            .Append(")) { return ").Append(check++).AppendLine("; }");
    }

    private static long NextInt64(Random random)
    {
        return unchecked((long)NextUInt64(random));
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
