using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class IntegerSaturationModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SaturationCandidatesPersistAndMatchBigIntegerBoundariesAsync(
        BoundRuntimePolicy policy)
    {
        var path = Path.Combine(RepositoryRoot(), "self-host", "IntegerSaturation.terrum");
        var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));

        var producerCompilation = Compilation.CreateLibrary(tree, policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var conversions = CreateConversions();
        Assert.Equal(20, conversions.Count);
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            conversions.ConvertAll(static conversion =>
                "Terrum.CodeAnalysis.Numerics64." + conversion.FunctionName));
        var reference = ModuleCompiler.CreateReference(producer);

        var assertions = new StringBuilder();
        var helpers = new StringBuilder();
        var checks = new List<string>();
        var failureCode = 1;
        foreach (var conversion in conversions)
        {
            helpers.Append("bool Check")
                .Append(conversion.Source.Name).Append("To").Append(conversion.Destination.Name)
                .Append('(').Append(conversion.Source.Code).Append(" value, ")
                .Append(conversion.Destination.Code).AppendLine(" expected) {")
                .Append("    return ").Append(conversion.FunctionName)
                .AppendLine("(value) == expected;")
                .AppendLine("}");
            foreach (var value in CandidateInputs(conversion.Source, conversion.Destination))
            {
                var expected = value < conversion.Destination.Minimum
                    ? conversion.Destination.Minimum
                    : value > conversion.Destination.Maximum
                        ? conversion.Destination.Maximum : value;
                assertions.Append("if (!Check")
                    .Append(conversion.Source.Name).Append("To").Append(conversion.Destination.Name)
                    .Append('(').Append(Literal(value, conversion.Source)).Append(", ")
                    .Append(Literal(expected, conversion.Destination)).Append(")) { return ")
                    .Append(failureCode.ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
                checks.Add($"{conversion.FunctionName}({Literal(value, conversion.Source)})");
                failureCode++;
            }
        }
        assertions.Append("if (!CheckAllChar16Widenings()) { return ")
            .Append(failureCode.ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
        checks.Add("all 65,536 char16-to-integer widening results");
        helpers.AppendLine("bool CheckAllChar16Widenings() {")
            .AppendLine("    mut int32 codeUnit = 0;")
            .AppendLine("    while (codeUnit <= 65535) {")
            .AppendLine("        char16 value = char16(codeUnit);")
            .AppendLine("        if (SaturateChar16ToInt32(value) != codeUnit")
            .AppendLine("            || SaturateChar16ToUInt32(value) != uint32(codeUnit)")
            .AppendLine("            || SaturateChar16ToInt64(value) != int64(codeUnit)")
            .AppendLine("            || SaturateChar16ToUInt64(value) != uint64(codeUnit)) { return false; }")
            .AppendLine("        codeUnit = codeUnit + 1;")
            .AppendLine("    }")
            .AppendLine("    return true;")
            .AppendLine("}");

        var consumerSource = new StringBuilder("using Terrum.CodeAnalysis.Numerics64;\nint32 Main() {\n");
        consumerSource.Append(assertions);
        consumerSource.AppendLine("return 0;");
        consumerSource.AppendLine("}");
        consumerSource.Append(helpers);
        var consumerTree = SyntaxTree.Parse(consumerSource.ToString(), "integer-saturation-consumer.terrum");
        Assert.False(consumerTree.HasErrors, string.Join(Environment.NewLine, consumerTree.Diagnostics));
        var consumerCompilation = Compilation.Create(consumerTree, reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var actual = NativeJit.Execute(ModuleCompiler.Link(consumer, producer), (NativeRuntimePolicy)policy);
        Assert.True(actual == 0,
            actual > 0 && actual <= checks.Count
                ? $"Saturation boundary {actual} failed: {checks[actual - 1]}."
                : $"Expected all saturation boundary checks to pass, got consumer result {actual}.");
    }

    private static List<Conversion> CreateConversions()
    {
        var int32 = new NumericType("Int32", "int32", int.MinValue, int.MaxValue,
            "int32.MinValue", "int32.MaxValue");
        var uint32 = new NumericType("UInt32", "uint32", BigInteger.Zero, new BigInteger(uint.MaxValue),
            "0", "uint32.MaxValue");
        var int64 = new NumericType("Int64", "int64", long.MinValue, long.MaxValue,
            "int64.MinValue", "int64.MaxValue");
        var uint64 = new NumericType("UInt64", "uint64", BigInteger.Zero, new BigInteger(ulong.MaxValue),
            "0", "uint64.MaxValue");
        var char16 = new NumericType("Char16", "char16", BigInteger.Zero, char.MaxValue,
            "char16(0)", "char16.MaxValue");
        NumericType[] integerTypes = [int32, uint32, int64, uint64];
        var conversions = new List<Conversion>(20);
        foreach (var source in integerTypes)
        {
            foreach (var destination in integerTypes)
            {
                if (source != destination)
                {
                    conversions.Add(new Conversion(source, destination));
                }
            }
            conversions.Add(new Conversion(source, char16));
            conversions.Add(new Conversion(char16, source));
        }
        return conversions;
    }

    private static List<BigInteger> CandidateInputs(NumericType source, NumericType destination)
    {
        var values = new List<BigInteger>();
        var seen = new HashSet<BigInteger>();

        void Add(BigInteger value)
        {
            if (value >= source.Minimum && value <= source.Maximum && seen.Add(value))
            {
                values.Add(value);
            }
        }

        Add(source.Minimum);
        Add(source.Minimum + 1);
        Add(-1);
        Add(BigInteger.Zero);
        Add(BigInteger.One);
        Add(0xD7FF);
        Add(0xD800);
        Add(0xDFFF);
        Add(0xE000);
        Add(0xFFFF);
        Add(destination.Minimum - 1);
        Add(destination.Minimum);
        Add(destination.Minimum + 1);
        Add(destination.Maximum - 1);
        Add(destination.Maximum);
        Add(destination.Maximum + 1);
        Add(source.Maximum - 1);
        Add(source.Maximum);
        return values;
    }

    private static string Literal(BigInteger value, NumericType type)
    {
        if (value == type.Minimum)
        {
            return type.MinimumLiteral;
        }
        if (value == type.Maximum)
        {
            return type.MaximumLiteral;
        }
        if (type.Name == "Char16")
        {
            return "char16(" + value.ToString(CultureInfo.InvariantCulture) + ")";
        }
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record NumericType(string Name, string Code, BigInteger Minimum, BigInteger Maximum,
        string MinimumLiteral, string MaximumLiteral);

    private sealed record Conversion(NumericType Source, NumericType Destination)
    {
        public string FunctionName => "Saturate" + Source.Name + "To" + Destination.Name;
    }
}
