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

public sealed class FloatingNegationModuleTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task FloatingNegationFlipsOnlyTheSignBitAsync(BoundRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var path = SelfHostPaths.ResolveFile(root, "FloatingNegation.terrum");
        var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
        var component = SyntaxTree.Parse(text, path);
        Assert.That(component.HasErrors, Is.False, string.Join(Environment.NewLine, component.Diagnostics));

        var verificationSource = CreateVerificationSource();
        var verification = SyntaxTree.Parse(verificationSource, "floating-negation-module.terrum");
        Assert.That(verification.HasErrors, Is.False, string.Join(Environment.NewLine,
            verification.Diagnostics.Select(diagnostic =>
                $"{diagnostic} near \"{GetContext(verificationSource, diagnostic.Span.Start)}\"")));
        var compilation = Compilation.Create([component, verification], policy);
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
        var check = 1;
        foreach (var bits in GetFloat32Inputs())
        {
            AppendCheck(source, "NegateFloat32Bits", "uint32", bits,
                unchecked(bits ^ 0x80000000u), ref check);
        }

        foreach (var bits in GetFloat64Inputs())
        {
            AppendCheck(source, "NegateFloat64Bits", "uint64", bits,
                bits ^ 0x8000000000000000, ref check);
        }

        source.AppendLine("return 0;");
        source.AppendLine("}");
        return source.ToString();
    }

    private static void AppendCheck(
        StringBuilder source,
        string function,
        string type,
        ulong bits,
        ulong expected,
        ref int check)
    {
        var index = check++;
        var width = type == "uint32" ? 8 : 16;
        source.Append("if (").Append(function).Append('(').Append(type).Append("(0x")
            .Append(bits.ToString(width == 8 ? "X8" : "X16", CultureInfo.InvariantCulture))
            .Append(")) != ").Append(type).Append("(0x")
            .Append(expected.ToString(width == 8 ? "X8" : "X16", CultureInfo.InvariantCulture))
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
        var values = new List<uint>
        {
            0, 0x80000000, 1, 0x80000001, 0x007FFFFF, 0x807FFFFF,
            0x00800000, 0x80800000, 0x3F800000, 0xBF800000,
            0x7F800000, 0xFF800000, 0x7FC00000, 0xFFC12345,
            0x7F800001, 0xFF800001, 0x7FA12345, 0xFFA12345,
        };
        var random = new Random(0xF10A7);
        for (var index = 0; index < 24; index++)
        {
            var bits = (uint)random.NextInt64(0, 1L << 32);
            if (!values.Contains(bits))
            {
                values.Add(bits);
            }
        }

        return values;
    }

    private static List<ulong> GetFloat64Inputs()
    {
        var values = new List<ulong>
        {
            0, 0x8000000000000000, 1, 0x8000000000000001,
            0x000FFFFFFFFFFFFF, 0x800FFFFFFFFFFFFF,
            0x0010000000000000, 0x8010000000000000,
            0x3FF0000000000000, 0xBFF0000000000000,
            0x7FF0000000000000, 0xFFF0000000000000,
            0x7FF8000000000000, 0xFFF8123456789ABC,
            0x7FF0000000000001, 0xFFF0000000000001,
        };
        var random = new Random(0x64F10A7);
        for (var index = 0; index < 24; index++)
        {
            var bits = NextUInt64(random);
            if (!values.Contains(bits))
            {
                values.Add(bits);
            }
        }

        return values;
    }

    private static ulong NextUInt64(Random random)
    {
        var low = (uint)random.NextInt64(0, 1L << 32);
        var high = (uint)random.NextInt64(0, 1L << 32);
        return ((ulong)high << 32) | low;
    }
}
