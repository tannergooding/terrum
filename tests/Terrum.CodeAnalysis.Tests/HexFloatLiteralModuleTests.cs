using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class HexFloatLiteralModuleTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() { return 0; }

        bool TryParseForFormat(int32 format, int32 start, int32 length, init ref<uint64> bits) {
            if (format == 32) {
                if (!TryParseHexFloat32Literal(start, length, init var parsed)) { return false; }
                bits = uint64(parsed);
                return true;
            }
            if (format == 64) {
                return TryParseHexFloat64Literal(start, length, init bits);
            }
            return false;
        }

        public int32 VerifyRecords() {
            int32 sourceLength = Host.Source.Length;
            mut int32 at = 0;
            mut int32 item = 0;
            while (at < sourceLength) {
                if (sourceLength - at < 7) { return -1; }
                int32 format = int32(Host.Source[at]);
                int32 valid = int32(Host.Source[at + 1]);
                int32 spellingLength = int32(Host.Source[at + 2]);
                if ((format != 32 && format != 64) || (valid != 0 && valid != 1)
                    || spellingLength <= 0) { return -2; }
                uint64 expectedBits = uint64(Host.Source[at + 3])
                    | (uint64(Host.Source[at + 4]) << 16)
                    | (uint64(Host.Source[at + 5]) << 32)
                    | (uint64(Host.Source[at + 6]) << 48);
                at = at + 7;
                if (spellingLength > sourceLength - at) { return -3; }

                if (valid == 0) {
                    if (TryParseForFormat(format, at, spellingLength, init var unexpectedBits)) {
                        return item + 1;
                    }
                } else {
                    if (!TryParseForFormat(format, at, spellingLength, init var actualBits)
                        || actualBits != expectedBits) { return item + 1; }
                }

                at = at + spellingLength;
                item = item + 1;
                if (at < sourceLength && sourceLength - at < 7) { return -4; }
            }
            return 0;
        }
        """;

    private static readonly Fixture[] s_fixtures = [
        new(32, "0x1p0", 0x3F800000),
        new(64, "0x1p0", 0x3FF0000000000000),
        new(32, "0X1.8P+1", 0x40400000),
        new(64, "0x1.fp+2", 0x401F000000000000),
        new(32, "0x1_f.0_fP+1_0", 0x46F87800),
        new(32, "0x__1p0", 0x3F800000),
        new(32, "0x1__2p0", 0x41900000),
        new(32, "0x1.0__8p0", 0x3F840000),
        new(64, "0x1.0__8p0", 0x3FF0800000000000),
        new(32, "0x1p1__0", 0x44800000),
        new(64, "0x1p1__0", 0x4090000000000000),
        new(32, "0x1.p0", 0x3F800000),
        new(64, "0x.8p+1", 0x3FF0000000000000),
        new(32, "0x1.000001p0", 0x3F800000),
        new(32, "0x1.000003p0", 0x3F800002),
        new(32, "0x1p-149", 0x00000001),
        new(32, "0x1p-150", 0x00000000),
        new(32, "0x3p-150", 0x00000002),
        new(32, "0x1.000001p-150", 0x00000001),
        new(32, "0x1.fffffcp-127", 0x007FFFFF),
        new(32, "0x1.fffffep-127", 0x00800000),
        new(32, "0x1p-126", 0x00800000),
        new(32, "0x1.fffffep+127", 0x7F7FFFFF),
        new(32, "0x1.ffffffp+127", 0x7F800000),
        new(64, "0x1.00000000000008p0", 0x3FF0000000000000),
        new(64, "0x1p-1074", 0x0000000000000001),
        new(64, "0x1p-1075", 0x0000000000000000),
        new(64, "0x3p-1075", 0x0000000000000002),
        new(64, "0x1.00000000000008p-1075", 0x0000000000000001),
        new(64, "0x1.ffffffffffffep-1023", 0x000FFFFFFFFFFFFF),
        new(64, "0x1.fffffffffffffp-1023", 0x0010000000000000),
        new(64, "0x1p-1022", 0x0010000000000000),
        new(64, "0x1.fffffffffffffp+1023", 0x7FEFFFFFFFFFFFFF),
        new(64, "0x1.fffffffffffff8p+1023", 0x7FF0000000000000),
        new(32, "0x0p999999", 0x00000000),
        new(64, "0x1p-999999", 0x0000000000000000),
    ];

    private static readonly string[] s_invalidSpellings = [
        "0x1",
        "0x.p0",
        "0x1..0p0",
        "0x1._0p0",
        "0x1_p0",
        "0x1p",
        "0x1p+",
        "0x1p_0",
        "0x1p0_",
        "0x1p0p1",
        "0x1\0p0",
    ];

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task HexFloatLiteralModuleMatchesHostedTargetRoundingAsync(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "HexDigitValue", "HexFloatLiteral" };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, path);
            Assert.False(trees[index].HasErrors, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        trees[^1] = SyntaxTree.Parse(VerificationSource, "hex-float-literal-module.terrum");
        var producerCompilation = Compilation.Create(trees, [
            new HostBuffer("Source", PrimitiveType.Char16, [48, 120, 49, 112, 48], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(producerCompilation.Program), ["Terrum.CodeAnalysis.VerifyRecords"]);

        var consumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyRecords(); }
            """, "hex-float-literal-consumer.terrum");
        var consumerCompilation = Compilation.Create(
            consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.Link(
            ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer),
            producer);

        var fixtures = s_fixtures.Concat(CreateRandomFixtures()).ToArray();
        var hostedBits = ReadHostedBits(fixtures);
        for (var index = 0; index < s_fixtures.Length; index++)
        {
            if (s_fixtures[index].KnownBits is ulong knownBits)
            {
                Assert.Equal(knownBits, hostedBits[index]);
            }
        }

        var records = CreateRecords(fixtures, hostedBits);
        records.AddRange(CreateRecords(
            s_invalidSpellings.Select(static spelling => new Fixture(32, spelling, null)).ToArray(),
            null));
        Assert.Equal(0, ExecuteRecords(consumer, records, (NativeRuntimePolicy)policy));

        var longFraction = "0x1." + new string('0', 16_000) + "p0";
        var longFixture = new[] { new Fixture(64, longFraction, 0x3FF0000000000000) };
        var longHostedBits = ReadHostedBits(longFixture);
        Assert.Equal(0x3FF0000000000000UL, longHostedBits[0]);
        Assert.True(longFraction.Length + 7 <= 16_384);
        Assert.Equal(0, ExecuteRecords(
            consumer, CreateRecords(longFixture, longHostedBits), (NativeRuntimePolicy)policy));
    }

    private static ulong[] ReadHostedBits(Fixture[] fixtures)
    {
        var trees = fixtures.Select((fixture, index) => {
            var type = fixture.Precision == 32 ? "float32" : "float64";
            var source = $"public {type} Read{index}() {{ return {fixture.Spelling}; }}";
            var lexed = Lexer.Lex(SourceText.From(fixture.Spelling));
            Assert.True(lexed.Diagnostics.IsEmpty,
                $"Hosted lexer rejected generated fixture '{fixture.Spelling}': {string.Join(", ", lexed.Diagnostics)}");
            Assert.Equal(2, lexed.Tokens.Length);
            Assert.Equal(fixture.Spelling, lexed.Tokens[0].Text);
            return SyntaxTree.Parse(source, $"hex-float-oracle-{index}.terrum");
        });
        var compilation = Compilation.CreateLibrary(trees);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var results = new ulong[fixtures.Length];
        for (var index = 0; index < fixtures.Length; index++)
        {
            var function = Assert.Single(program.Functions, function => function.Name == $"Read{index}");
            var result = Assert.IsType<BoundReturn>(Assert.Single(function.Body.Statements));
            var constant = Assert.IsType<BoundConstant>(result.Value);
            Assert.Equal(fixtures[index].Precision == 32 ? PrimitiveType.Float32 : PrimitiveType.Float64,
                constant.Type);
            results[index] = unchecked((ulong)constant.Value);
        }

        return results;
    }

    private static Fixture[] CreateRandomFixtures()
    {
        const string HexDigits = "0123456789abcdef";
        var random = new Random(0x6EED);
        var fixtures = new Fixture[192];
        for (var index = 0; index < fixtures.Length; index++)
        {
            var precision = random.Next(2) == 0 ? 32 : 64;
            var digits = new char[random.Next(1, 25)];
            for (var digitIndex = 0; digitIndex < digits.Length; digitIndex++)
            {
                digits[digitIndex] = HexDigits[random.Next(HexDigits.Length)];
            }

            var hasPoint = random.Next(4) != 0;
            var pointIndex = hasPoint ? random.Next(digits.Length + 1) : digits.Length;
            var mantissa = hasPoint
                ? FormatDigits(digits.AsSpan(0, pointIndex), random)
                    + "." + FormatDigits(digits.AsSpan(pointIndex), random)
                : FormatDigits(digits, random);
            var prefix = random.Next(2) == 0 ? "0x" : "0X";
            if (random.Next(4) == 0 && (!hasPoint || pointIndex > 0))
            {
                prefix += "__";
            }

            var exponent = random.Next(-1300, 1301);
            var exponentDigits = Math.Abs(exponent).ToString(CultureInfo.InvariantCulture);
            var exponentSpelling = FormatDigits(exponentDigits.AsSpan(), random);
            var sign = exponent < 0 ? "-" : random.Next(2) == 0 ? "+" : "";
            var marker = random.Next(2) == 0 ? "p" : "P";
            fixtures[index] = new Fixture(
                precision,
                prefix + mantissa + marker + sign + exponentSpelling,
                null);
        }

        return fixtures;
    }

    private static string FormatDigits(ReadOnlySpan<char> digits, Random random)
    {
        var result = new System.Text.StringBuilder(digits.Length + (digits.Length / 4));
        for (var index = 0; index < digits.Length; index++)
        {
            result.Append(digits[index]);
            if (index + 1 < digits.Length && random.Next(5) == 0)
            {
                result.Append('_');
            }
        }

        return result.ToString();
    }

    private static List<int> CreateRecords(Fixture[] fixtures, ulong[]? expectedBits)
    {
        var records = new List<int>();
        for (var index = 0; index < fixtures.Length; index++)
        {
            var fixture = fixtures[index];
            var bits = expectedBits is null ? 0 : expectedBits[index];
            records.Add(fixture.Precision);
            records.Add(expectedBits is null ? 0 : 1);
            records.Add(fixture.Spelling.Length);
            records.Add((int)(bits & 0xFFFF));
            records.Add((int)((bits >> 16) & 0xFFFF));
            records.Add((int)((bits >> 32) & 0xFFFF));
            records.Add((int)((bits >> 48) & 0xFFFF));
            records.AddRange(fixture.Spelling.Select(static unit => (int)unit));
        }

        return records;
    }

    private static int ExecuteRecords(BoundProgram consumer, List<int> records, NativeRuntimePolicy policy)
    {
        Assert.InRange(records.Count, 1, 16_384);
        var grant = new HostBuffer("Source", PrimitiveType.Char16, [.. records], false) {
            IsInvocationScoped = true,
        };
        return NativeJit.ExecuteWithSourceGrant(consumer, [grant], policy);
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

    private sealed record Fixture(int Precision, string Spelling, ulong? KnownBits);
}
