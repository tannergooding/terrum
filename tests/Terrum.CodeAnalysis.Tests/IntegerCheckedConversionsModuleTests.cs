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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class IntegerCheckedConversionsModuleTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CheckedConversionHelpersPersistAndPublishOnlyRepresentableValuesAsync(
        BoundRuntimePolicy policy)
    {
        var path = Path.Combine(RepositoryRoot(), "self-host", "IntegerCheckedConversions.terrum");
        var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));

        var producerCompilation = Compilation.CreateLibrary(tree, policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var conversions = CreateConversions();
        Assert.That(conversions.Count, Is.EqualTo(20));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = producerCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            conversions.ConvertAll(static conversion =>
                "Terrum.CodeAnalysis.Numerics64." + conversion.FunctionName));
        var reference = ModuleCompiler.CreateReference(producer);

        var assertions = new StringBuilder();
        var checks = new List<string>();
        var failureCode = 1;
        foreach (var conversion in conversions)
        {
            foreach (var value in CandidateInputs(conversion.Source, conversion.Destination))
            {
                var succeeds = value >= conversion.Destination.Minimum
                    && value <= conversion.Destination.Maximum;
                var expected = succeeds ? value : BigInteger.Zero;
                assertions.Append("if (!Check")
                    .Append(conversion.Source.Name).Append("To").Append(conversion.Destination.Name)
                    .Append('(').Append(Literal(value, conversion.Source)).Append(", ")
                    .Append(Literal(expected, conversion.Destination)).Append(", ")
                    .Append(succeeds ? "true" : "false").Append(")) { return ")
                    .Append(failureCode.ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
                checks.Add($"{conversion.FunctionName}({Literal(value, conversion.Source)})");
                failureCode++;
            }
        }
        assertions.Append("if (!CheckAllChar16Widenings()) { return ")
            .Append(failureCode.ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
        checks.Add("all 65,536 char16-to-integer widening results");

        var consumerSource = new StringBuilder("using Terrum.CodeAnalysis.Numerics64;\nint32 Main() {\n");
        consumerSource.Append(assertions);
        consumerSource.AppendLine("return 0;");
        consumerSource.AppendLine("}");
        foreach (var conversion in conversions)
        {
            consumerSource.Append("bool Check")
                .Append(conversion.Source.Name).Append("To").Append(conversion.Destination.Name)
                .Append('(').Append(conversion.Source.Code).Append(" value, ")
                .Append(conversion.Destination.Code).AppendLine(" expected, bool shouldSucceed) {")
                .AppendLine("    if (shouldSucceed) {")
                .Append("        if (!").Append(conversion.FunctionName)
                .AppendLine("(value, init var actual)) { return false; }")
                .AppendLine("        return actual == expected;")
                .AppendLine("    }")
                .Append("    return !").Append(conversion.FunctionName)
                .AppendLine("(value, init var unpublished);")
                .AppendLine("}");
        }
        consumerSource.AppendLine("bool CheckAllChar16Widenings() {")
            .AppendLine("    mut int32 codeUnit = 0;")
            .AppendLine("    while (codeUnit <= 65535) {")
            .AppendLine("        char16 value = char16(codeUnit);")
            .AppendLine("        if (!TryConvertChar16ToInt32(value, init var actualInt32)) { return false; }")
            .AppendLine("        if (!TryConvertChar16ToUInt32(value, init var actualUInt32)) { return false; }")
            .AppendLine("        if (!TryConvertChar16ToInt64(value, init var actualInt64)) { return false; }")
            .AppendLine("        if (!TryConvertChar16ToUInt64(value, init var actualUInt64)) { return false; }")
            .AppendLine("        if (actualInt32 != codeUnit || actualUInt32 != uint32(codeUnit)")
            .AppendLine("            || actualInt64 != int64(codeUnit) || actualUInt64 != uint64(codeUnit))")
            .AppendLine("        { return false; }")
            .AppendLine("        codeUnit = codeUnit + 1;")
            .AppendLine("    }")
            .AppendLine("    return true;")
            .AppendLine("}");

        var consumerTree = SyntaxTree.Parse(consumerSource.ToString(), "integer-conversion-consumer.terrum");
        Assert.That(consumerTree.HasErrors, Is.False, string.Join(Environment.NewLine, consumerTree.Diagnostics));
        var consumerCompilation = Compilation.Create(consumerTree, reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var actual = NativeJit.Execute(linked, (NativeRuntimePolicy)policy);
        Assert.That(actual == 0, Is.True, actual > 0 && actual <= checks.Count
                ? $"Conversion boundary {actual} failed: {checks[actual - 1]}."
                : $"Expected all conversion checks to pass, got consumer result {actual}.");

        var invalidConsumer = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (!TryConvertInt32ToUInt64(-1, init var result)) { return int32(result); }
                return 0;
            }
            """, "integer-conversion-unpublished-output.terrum");
        var invalidCompilation = Compilation.Create(invalidConsumer, reference, policy);
        Assert.That(global::System.Linq.Enumerable.Any(invalidCompilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
        Assert.That(invalidCompilation.Program, Is.Null);
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
        Add(new BigInteger(0xD800));
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
        public string FunctionName => "TryConvert" + Source.Name + "To" + Destination.Name;
    }
}
