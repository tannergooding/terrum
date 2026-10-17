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

public sealed class IntegerWrappingModuleTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task WrappingCandidatesPersistAndMatchBigIntegerBoundariesAsync(
        BoundRuntimePolicy policy)
    {
        var path = SelfHostPaths.ResolveFile(RepositoryRoot(), "IntegerWrapping.terrum");
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
                var expected = Wrap(value, conversion.Destination);
                assertions.Append("if (!Check")
                    .Append(conversion.Source.Name).Append("To").Append(conversion.Destination.Name)
                    .Append('(').Append(Literal(value, conversion.Source)).Append(", ")
                    .Append(Literal(expected, conversion.Destination)).Append(")) { return ")
                    .Append(failureCode.ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
                checks.Add($"{conversion.FunctionName}({Literal(value, conversion.Source)})");
                failureCode++;
            }
        }
        checks.Add("all char16-to-integer widening inputs");
        assertions.Append("if (!CheckAllChar16Widenings()) { return ")
            .Append(failureCode.ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
        helpers.AppendLine("""
            bool CheckAllChar16Widenings() {
                mut int32 codeUnit = 0;
                while (codeUnit <= 65535) {
                    char16 value = char16(codeUnit);
                    if (WrapChar16ToInt32(value) != codeUnit
                        || WrapChar16ToUInt32(value) != uint32(codeUnit)
                        || WrapChar16ToInt64(value) != int64(codeUnit)
                        || WrapChar16ToUInt64(value) != uint64(codeUnit)) {
                        return false;
                    }
                    codeUnit = codeUnit + 1;
                }
                return true;
            }
            """);

        var consumerSource = new StringBuilder("using Terrum.CodeAnalysis.Numerics64;\nint32 Main() {\n");
        consumerSource.Append(assertions);
        consumerSource.AppendLine("return 0;");
        consumerSource.AppendLine("}");
        consumerSource.Append(helpers);
        var consumerTree = SyntaxTree.Parse(consumerSource.ToString(), "integer-wrapping-consumer.terrum");
        Assert.That(consumerTree.HasErrors, Is.False, string.Join(Environment.NewLine, consumerTree.Diagnostics));
        var consumerCompilation = Compilation.Create(consumerTree, reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);
        var actual = NativeJit.Execute(ModuleCompiler.Link(consumer, producer), (NativeRuntimePolicy)policy);
        Assert.That(actual == 0, Is.True, actual > 0 && actual <= checks.Count
                ? $"Wrapping boundary {actual} failed: {checks[actual - 1]}."
                : $"Expected all wrapping boundary checks to pass, got consumer result {actual}.");
    }

    private static List<Conversion> CreateConversions()
    {
        var int32 = new NumericType("Int32", "int32", int.MinValue, int.MaxValue,
            32, true, "int32.MinValue", "int32.MaxValue");
        var uint32 = new NumericType("UInt32", "uint32", BigInteger.Zero, new BigInteger(uint.MaxValue),
            32, false, "0", "uint32.MaxValue");
        var int64 = new NumericType("Int64", "int64", long.MinValue, long.MaxValue,
            64, true, "int64.MinValue", "int64.MaxValue");
        var uint64 = new NumericType("UInt64", "uint64", BigInteger.Zero, new BigInteger(ulong.MaxValue),
            64, false, "0", "uint64.MaxValue");
        var char16 = new NumericType("Char16", "char16", BigInteger.Zero, char.MaxValue,
            16, false, "char16(0)", "char16.MaxValue");
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

    private static BigInteger Wrap(BigInteger value, NumericType destination)
    {
        var modulus = BigInteger.One << destination.BitWidth;
        var wrapped = value % modulus;
        if (wrapped.Sign < 0)
        {
            wrapped += modulus;
        }
        if (destination.IsSigned && wrapped >= (BigInteger.One << (destination.BitWidth - 1)))
        {
            wrapped -= modulus;
        }
        return wrapped;
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
        int BitWidth, bool IsSigned, string MinimumLiteral, string MaximumLiteral);

    private sealed record Conversion(NumericType Source, NumericType Destination)
    {
        public string FunctionName => "Wrap" + Source.Name + "To" + Destination.Name;
    }
}
