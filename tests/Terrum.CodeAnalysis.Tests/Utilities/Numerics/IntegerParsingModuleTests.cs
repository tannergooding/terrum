using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Utilities.Numerics;

public sealed class IntegerParsingModuleTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task IntegerParsingModuleReadsFreshSourceGrants(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/WideIntegerFacts", "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerParsing" };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = TerrumProjectPaths.RepositoryFile(root, componentNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, path);
            Assert.That(trees[index].HasErrors, Is.False, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        trees[^1] = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis.Numerics64;
            int32 Main() { return 0; }
            public int32 ParseUInt64() {
                if (!TryParseUInt64Base(0, Host.Source.Length, 10, init var parsed)) { return -100; }
                if (parsed == uint64.MaxValue) { return -101; }
                if (parsed > uint64(int32.MaxValue)) { return -102; }
                return int32(parsed);
            }
            public int32 ParseInt64() {
                if (!TryParseInt64Base(0, Host.Source.Length, 10, init var parsed)) { return -100; }
                if (parsed == int64.MinValue) { return -101; }
                if (parsed < int64(int32.MinValue) || parsed > int64(int32.MaxValue)) { return -102; }
                return int32(parsed);
            }
            public int32 ParseUInt32() {
                if (!TryParseUInt32Base(0, Host.Source.Length, 10, init var parsed)) { return -100; }
                if (parsed == uint32.MaxValue) { return -101; }
                if (parsed > uint32(int32.MaxValue)) { return -102; }
                return int32(parsed);
            }
            public int32 ParseInt32() {
                if (!TryParseInt32Base(0, Host.Source.Length, 10, init var parsed)) { return -100; }
                return parsed;
            }
            public int32 ParseChar16() {
                if (!TryParseChar16Base(0, Host.Source.Length, 10, init var parsed)) { return -100; }
                return int32(parsed);
            }
            """, "integer-parsing-module.terrum");
        var producerCompilation = Compilation.Create(trees,
        [
            new HostBuffer("Source", PrimitiveType.Char16, [48], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var exports = new[] {
            "ParseUInt64",
            "ParseInt64",
            "ParseUInt32",
            "ParseInt32",
            "ParseChar16",
        };
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = producerCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            [.. exports.Select(static name => "Terrum.CodeAnalysis.Numerics64." + name)]);

        var consumers = new BoundProgram[exports.Length];
        for (var index = 0; index < exports.Length; index++)
        {
            var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
                $"namespace Terrum.CodeAnalysis.Numerics64; int32 Main() {{ return {exports[index]}(); }}",
                "integer-parsing-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
            Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumer = ModuleCompiler.CreateConsumer(
                ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);
            consumers[index] = ModuleCompiler.Link(consumer, producer);
        }

        foreach (var fixture in new[] {
            new ParseFixture("", false),
            new ParseFixture("0", true),
            new ParseFixture("-0", true),
            new ParseFixture("42", true),
            new ParseFixture("-17", true),
            new ParseFixture("1_000", true),
            new ParseFixture("1__2", true),
            new ParseFixture("2147483647", true),
            new ParseFixture("-2147483648", true),
            new ParseFixture("2147483648", true),
            new ParseFixture("4294967295", true),
            new ParseFixture("4294967296", true),
            new ParseFixture("9223372036854775807", true),
            new ParseFixture("-9223372036854775808", true),
            new ParseFixture("18446744073709551615", true),
            new ParseFixture("18446744073709551616", true),
            new ParseFixture("65535", true),
            new ParseFixture("65536", true),
            new ParseFixture("55296", true),
            new ParseFixture("_1", false),
            new ParseFixture("1_", false),
            new ParseFixture("+1", false),
            new ParseFixture("12x", false),
        })
        {
            var source = new HostBuffer("Source", PrimitiveType.Char16,
                [.. fixture.Text.Select(static unit => (int)unit)], false) {
                IsInvocationScoped = true,
            };
            for (var parser = 0; parser < consumers.Length; parser++)
            {
                var expected = ExpectedResult(fixture, parser);
                var actual = NativeJit.ExecuteWithSourceGrant(consumers[parser], [source],
                    (NativeRuntimePolicy)policy);
                Assert.That(expected == actual, Is.True, $"{exports[parser]} on '{fixture.Text}': expected {expected}, got {actual}.");
            }
        }
    }

    private static int ExpectedResult(ParseFixture fixture, int parser)
    {
        if (!fixture.IsDecimalSpelling
            || !BigInteger.TryParse(fixture.Text.Replace("_", string.Empty), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var value))
        {
            return -100;
        }

        if ((parser is 0 or 2 or 4) && fixture.Text.StartsWith("-", StringComparison.Ordinal))
        {
            return -100;
        }

        return parser switch {
            0 when value < 0 || value > ulong.MaxValue => -100,
            0 when value == ulong.MaxValue => -101,
            0 when value > int.MaxValue => -102,
            0 => (int)value,
            1 when value < long.MinValue || value > long.MaxValue => -100,
            1 when value == long.MinValue => -101,
            1 when value < int.MinValue || value > int.MaxValue => -102,
            1 => (int)value,
            2 when value < 0 || value > uint.MaxValue => -100,
            2 when value == uint.MaxValue => -101,
            2 when value > int.MaxValue => -102,
            2 => (int)value,
            3 when value < int.MinValue || value > int.MaxValue => -100,
            3 => (int)value,
            4 when value < 0 || value > char.MaxValue => -100,
            4 => (int)value,
            _ => throw new ArgumentOutOfRangeException(nameof(parser)),
        };
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record ParseFixture(string Text, bool IsDecimalSpelling);
}
