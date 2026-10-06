using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class DecimalFloatLiteralModuleTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() { return 0; }

        int64 ParseForFormatOrMinusOne(int32 format, int32 start, int32 length) {
            if (format == 32) {
                return GetDecimalFloat32LiteralBitsOrMinusOne(start, length);
            }
            if (format == 64) {
                return GetDecimalFloat64LiteralBitsOrMinusOne(start, length);
            }
            return -1;
        }

        public int32 VerifyRecords() {
            int32 sourceLength = Host.Source.Length;
            if (ParseForFormatOrMinusOne(32, -1, 1) >= 0
                || ParseForFormatOrMinusOne(64, 0, 0) >= 0
                || ParseForFormatOrMinusOne(32, sourceLength, 1) >= 0
                || ParseForFormatOrMinusOne(64, sourceLength + 1, 1) >= 0
                || ParseForFormatOrMinusOne(32, 1, sourceLength) >= 0) { return -5; }
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
                    if (ParseForFormatOrMinusOne(format, at, spellingLength) >= 0) {
                        return item + 1;
                    }
                } else {
                    int64 actualBits = ParseForFormatOrMinusOne(format, at, spellingLength);
                    if (actualBits < 0 || uint64(actualBits) != expectedBits) { return item + 1; }
                }

                at = at + spellingLength;
                item = item + 1;
                if (at < sourceLength && sourceLength - at < 7) { return -4; }
            }
            return 0;
        }
        """;

    private static readonly Fixture[] s_fixtures = CreateBoundaryFixtures();

    private static readonly string[] s_invalidSpellings = [
        "1_",
        "1__",
        "1._0",
        "1.0_",
        "1e",
        "1e+",
        "1e_1",
        "1e1_",
        "1e1e2",
        "0e",
        "0e+",
        "0e_1",
        "1e" + new string('9', 100) + "x",
        "0e-" + new string('9', 100) + "x",
        ".1",
        "1.",
        "+1",
        "-1",
        "1e1tail",
        "0x1p0",
        "1\0",
    ];

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task DecimalFloatLiteralModuleMatchesHostedTargetRoundingAsync(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentPath = Path.Combine(root, "self-host", "DecimalFloatLiteral.terrum");
        var componentText = await File.ReadAllTextAsync(componentPath).ConfigureAwait(true);
        var componentTree = SyntaxTree.Parse(componentText, componentPath);
        Assert.False(componentTree.HasErrors, string.Join(Environment.NewLine, componentTree.Diagnostics));

        var verificationTree = SyntaxTree.Parse(VerificationSource, "decimal-float-literal-module.terrum");
        var producerCompilation = Compilation.Create(
            [componentTree, verificationTree],
            [
                new HostBuffer("Source", PrimitiveType.Char16, [49], false) {
                    IsInvocationScoped = true,
                },
            ],
            policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(producerCompilation.Program), ["Terrum.CodeAnalysis.VerifyRecords"]);

        var consumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyRecords(); }
            """, "decimal-float-literal-consumer.terrum");
        var consumerCompilation = Compilation.Create(
            consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.Link(
            ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer),
            producer);

        var emptyGrant = new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        };
        Assert.Equal(0, NativeJit.ExecuteWithSourceGrant(consumer, [emptyGrant], (NativeRuntimePolicy)policy));

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
            s_invalidSpellings
                .SelectMany(static spelling => new[] { new Fixture(32, spelling, null), new Fixture(64, spelling, null) })
                .ToArray(),
            null));
        Assert.Equal(0, ExecuteRecords(consumer, records, (NativeRuntimePolicy)policy));

        var longFraction = "1." + new string('0', 15_990) + "e0";
        var longFixture = new[] { new Fixture(64, longFraction, 0x3FF0000000000000) };
        var longHostedBits = ReadHostedBits(longFixture);
        Assert.Equal(0x3FF0000000000000UL, longHostedBits[0]);
        Assert.True(longFraction.Length + 7 <= 16_384);
        Assert.Equal(0, ExecuteRecords(
            consumer, CreateRecords(longFixture, longHostedBits), (NativeRuntimePolicy)policy));

        var longExponent = "1e" + new string('9', 15_990);
        var longExponentFixture = new[] { new Fixture(64, longExponent, 0x7FF0000000000000) };
        var longExponentBits = ReadHostedBits(longExponentFixture);
        Assert.Equal(0x7FF0000000000000UL, longExponentBits[0]);
        Assert.Equal(0, ExecuteRecords(
            consumer, CreateRecords(longExponentFixture, longExponentBits), (NativeRuntimePolicy)policy));

        var longSignificand = new string('9', 15_990) + "e-15989";
        var longSignificandFixture = new[] { new Fixture(64, longSignificand, 0x4024000000000000) };
        var longSignificandBits = ReadHostedBits(longSignificandFixture);
        Assert.Equal(0x4024000000000000UL, longSignificandBits[0]);
        Assert.Equal(0, ExecuteRecords(
            consumer, CreateRecords(longSignificandFixture, longSignificandBits), (NativeRuntimePolicy)policy));

        var longFractionCancellation = "0." + new string('0', 15_990) + "1e15991";
        var longFractionCancellationFixture = new[] {
            new Fixture(64, longFractionCancellation, 0x3FF0000000000000),
        };
        var longFractionCancellationBits = ReadHostedBits(longFractionCancellationFixture);
        Assert.Equal(0x3FF0000000000000UL, longFractionCancellationBits[0]);
        Assert.Equal(0, ExecuteRecords(consumer,
            CreateRecords(longFractionCancellationFixture, longFractionCancellationBits),
            (NativeRuntimePolicy)policy));
    }

    private static Fixture[] CreateBoundaryFixtures()
    {
        var fixtures = new List<Fixture>
        {
            new(32, "0", 0),
            new(64, "0e999999", 0),
            new(32, "1", 0x3F800000),
            new(64, "1_234.5__6e+7_8", null),
            new(32, "0.1", 0x3DCCCCCD),
            new(64, "0.1", 0x3FB999999999999A),
            new(32, "1.000000059604644775390625", 0x3F800000),
            new(32, "1.000000178813934326171875", 0x3F800002),
            new(64, "1.00000000000000011102230246251565404236316680908203125",
                0x3FF0000000000000),
            new(32, FormatDyadicDecimal(BigInteger.One, -126), 0x00800000),
            new(64, FormatDyadicDecimal(BigInteger.One, -1022), 0x0010000000000000),
        };

        foreach (var exponent in new[] { -125, -32, 0, 32, 126 })
        {
            var exactBits = (ulong)(exponent + 127) << 23;
            fixtures.Add(new Fixture(32, FormatDyadicDecimal((BigInteger.One << 24) - 1, exponent - 24),
                ((ulong)(exponent + 126) << 23) | 0x007FFFFF));
            fixtures.Add(new Fixture(32, FormatDyadicDecimal(BigInteger.One, exponent), exactBits));
            fixtures.Add(new Fixture(32, FormatDyadicDecimal((BigInteger.One << 23) + 1, exponent - 23),
                exactBits + 1));
        }

        foreach (var exponent in new[] { -1021, -256, 0, 256, 1023 })
        {
            var exactBits = (ulong)(exponent + 1023) << 52;
            fixtures.Add(new Fixture(64, FormatDyadicDecimal((BigInteger.One << 53) - 1, exponent - 53),
                ((ulong)(exponent + 1022) << 52) | 0x000FFFFFFFFFFFFF));
            fixtures.Add(new Fixture(64, FormatDyadicDecimal(BigInteger.One, exponent), exactBits));
            fixtures.Add(new Fixture(64, FormatDyadicDecimal((BigInteger.One << 52) + 1, exponent - 52),
                exactBits + 1));
        }

        var float32HalfSubnormal = FormatDyadicDecimal(BigInteger.One, -150);
        fixtures.Add(new Fixture(32, float32HalfSubnormal, 0));
        fixtures.Add(new Fixture(32, AdjustLastDecimalPlace(float32HalfSubnormal, -1), 0));
        fixtures.Add(new Fixture(32, AdjustLastDecimalPlace(float32HalfSubnormal, 1), 1));

        var float64HalfSubnormal = FormatDyadicDecimal(BigInteger.One, -1075);
        fixtures.Add(new Fixture(64, float64HalfSubnormal, 0));
        fixtures.Add(new Fixture(64, AdjustLastDecimalPlace(float64HalfSubnormal, -1), 0));
        fixtures.Add(new Fixture(64, AdjustLastDecimalPlace(float64HalfSubnormal, 1), 1));

        fixtures.Add(new Fixture(32, FormatDyadicDecimal((BigInteger.One << 24) - 1, -150), 0x00800000));
        fixtures.Add(new Fixture(64, FormatDyadicDecimal((BigInteger.One << 53) - 1, -1075),
            0x0010000000000000));
        var float32NormalBoundaryCoefficient = ((BigInteger.One << 24) - 1) * BigInteger.Pow(5, 150);
        fixtures.Add(new Fixture(32,
            (float32NormalBoundaryCoefficient * 10 - 1).ToString(CultureInfo.InvariantCulture) + "e-151",
            0x007FFFFF));
        fixtures.Add(new Fixture(32,
            float32NormalBoundaryCoefficient.ToString(CultureInfo.InvariantCulture) + "e-150",
            0x00800000));
        fixtures.Add(new Fixture(32,
            (float32NormalBoundaryCoefficient * 10 + 1).ToString(CultureInfo.InvariantCulture) + "e-151",
            0x00800000));

        var float64MidpointCoefficient = ((BigInteger.One << 53) + 1) * BigInteger.Pow(5, 1075);
        var float64MidpointDigits = float64MidpointCoefficient.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(768, float64MidpointDigits.Length);
        fixtures.Add(new Fixture(64, float64MidpointDigits + "e-1075", 0x0010000000000000));
        fixtures.Add(new Fixture(64,
            (float64MidpointCoefficient * 10 - 1).ToString(CultureInfo.InvariantCulture) + "e-1076",
            0x0010000000000000));
        var float64AboveMidpointDigits = (float64MidpointCoefficient * 10 + 1)
            .ToString(CultureInfo.InvariantCulture);
        fixtures.Add(new Fixture(64, float64AboveMidpointDigits + "e-1076", 0x0010000000000001));
        fixtures.Add(new Fixture(64,
            (float64MidpointCoefficient * 10).ToString(CultureInfo.InvariantCulture) + "e-1076",
            0x0010000000000000));
        fixtures.Add(new Fixture(64,
            float64AboveMidpointDigits[..768] + "_" + float64AboveMidpointDigits[768..] + "e-1076",
            0x0010000000000001));
        var float64NormalBoundaryCoefficient = ((BigInteger.One << 53) - 1) * BigInteger.Pow(5, 1075);
        fixtures.Add(new Fixture(64,
            (float64NormalBoundaryCoefficient * 10 - 1).ToString(CultureInfo.InvariantCulture) + "e-1076",
            0x000FFFFFFFFFFFFF));
        fixtures.Add(new Fixture(64,
            float64NormalBoundaryCoefficient.ToString(CultureInfo.InvariantCulture) + "e-1075",
            0x0010000000000000));
        fixtures.Add(new Fixture(64,
            (float64NormalBoundaryCoefficient * 10 + 1).ToString(CultureInfo.InvariantCulture) + "e-1076",
            0x0010000000000000));

        var float32Maximum = FormatDyadicDecimal((BigInteger.One << 24) - 1, 104);
        var float32OverflowTie = FormatDyadicDecimal((BigInteger.One << 25) - 1, 103);
        fixtures.Add(new Fixture(32, float32Maximum, 0x7F7FFFFF));
        fixtures.Add(new Fixture(32, AdjustInteger(float32OverflowTie, -1), 0x7F7FFFFF));
        fixtures.Add(new Fixture(32, float32OverflowTie, 0x7F800000));
        fixtures.Add(new Fixture(32, AdjustInteger(float32OverflowTie, 1), 0x7F800000));

        var float64Maximum = FormatDyadicDecimal((BigInteger.One << 53) - 1, 971);
        var float64OverflowTie = FormatDyadicDecimal((BigInteger.One << 54) - 1, 970);
        fixtures.Add(new Fixture(64, float64Maximum, 0x7FEFFFFFFFFFFFFF));
        fixtures.Add(new Fixture(64, AdjustInteger(float64OverflowTie, -1), 0x7FEFFFFFFFFFFFFF));
        fixtures.Add(new Fixture(64, float64OverflowTie, 0x7FF0000000000000));
        fixtures.Add(new Fixture(64, AdjustInteger(float64OverflowTie, 1), 0x7FF0000000000000));
        fixtures.Add(new Fixture(64, "1e-999999", 0));
        fixtures.Add(new Fixture(32, "1e999999", 0x7F800000));
        fixtures.Add(new Fixture(64, "1e999999", 0x7FF0000000000000));
        return [.. fixtures];
    }

    private static string FormatDyadicDecimal(BigInteger significand, int binaryExponent)
    {
        if (binaryExponent >= 0)
        {
            return (significand << binaryExponent).ToString(CultureInfo.InvariantCulture);
        }

        var decimalScale = -binaryExponent;
        var coefficient = significand * BigInteger.Pow(5, decimalScale);
        return FormatDecimalCoefficient(coefficient, decimalScale);
    }

    private static string AdjustLastDecimalPlace(string spelling, int adjustment)
    {
        var decimalPoint = spelling.IndexOf('.');
        Assert.True(decimalPoint >= 0);
        var decimalScale = spelling.Length - decimalPoint - 1;
        var coefficient = BigInteger.Parse(spelling.Remove(decimalPoint, 1), CultureInfo.InvariantCulture);
        return FormatDecimalCoefficient(coefficient + adjustment, decimalScale);
    }

    private static string AdjustInteger(string spelling, int adjustment)
    {
        var value = BigInteger.Parse(spelling, CultureInfo.InvariantCulture);
        return (value + adjustment).ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatDecimalCoefficient(BigInteger coefficient, int decimalScale)
    {
        var digits = coefficient.ToString(CultureInfo.InvariantCulture);
        if (digits.Length <= decimalScale)
        {
            return "0." + new string('0', decimalScale - digits.Length) + digits;
        }

        var integerDigits = digits.Length - decimalScale;
        return digits[..integerDigits] + "." + digits[integerDigits..];
    }

    private static Fixture[] CreateRandomFixtures()
    {
        var random = new Random(0xD3C1);
        var fixtures = new Fixture[192];
        for (var index = 0; index < fixtures.Length; index++)
        {
            var precision = random.Next(2) == 0 ? 32 : 64;
            var digits = new char[random.Next(1, 65)];
            for (var digitIndex = 0; digitIndex < digits.Length; digitIndex++)
            {
                digits[digitIndex] = (char)('0' + random.Next(10));
            }

            var integerDigits = random.Next(1, digits.Length + 1);
            var mantissa = FormatDigits(digits.AsSpan(0, integerDigits), random);
            if (integerDigits < digits.Length)
            {
                mantissa += "." + FormatDigits(digits.AsSpan(integerDigits), random);
            }

            if (random.Next(2) != 0)
            {
                var exponent = random.Next(-400, 401);
                var sign = exponent < 0 ? "-" : random.Next(2) == 0 ? "+" : "";
                var exponentDigits = FormatDigits(
                    Math.Abs(exponent).ToString(CultureInfo.InvariantCulture).AsSpan(), random);
                mantissa += (random.Next(2) == 0 ? "e" : "E") + sign + exponentDigits;
            }

            fixtures[index] = new Fixture(precision, mantissa, null);
        }

        return fixtures;
    }

    private static string FormatDigits(ReadOnlySpan<char> digits, Random random)
    {
        var result = new System.Text.StringBuilder(digits.Length + digits.Length / 4);
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

    private static ulong[] ReadHostedBits(Fixture[] fixtures)
    {
        var trees = fixtures.Select((fixture, index) => {
            var type = fixture.Precision == 32 ? "float32" : "float64";
            var source = $"public {type} Read{index}() {{ return {fixture.Spelling}; }}";
            var lexed = Lexer.Lex(SourceText.From(fixture.Spelling));
            Assert.True(lexed.Diagnostics.IsEmpty,
                $"Hosted lexer rejected generated fixture '{fixture.Spelling}': "
                + string.Join(", ", lexed.Diagnostics));
            Assert.Equal(2, lexed.Tokens.Length);
            Assert.Equal(fixture.Spelling, lexed.Tokens[0].Text);
            return SyntaxTree.Parse(source, $"decimal-float-oracle-{index}.terrum");
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
        Assert.NotEmpty(records);
        for (var start = 0; start < records.Count;)
        {
            var end = start;
            while (end < records.Count)
            {
                Assert.True(records.Count - end >= 7);
                var recordLength = 7 + records[end + 2];
                Assert.InRange(recordLength, 8, 16_384);
                if (end > start && end - start + recordLength > 16_384)
                {
                    break;
                }

                end += recordLength;
            }

            var grant = new HostBuffer("Source", PrimitiveType.Char16, [.. records.GetRange(start, end - start)], false) {
                IsInvocationScoped = true,
            };
            var result = NativeJit.ExecuteWithSourceGrant(consumer, [grant], policy);
            if (result != 0)
            {
                return result;
            }
            start = end;
        }

        return 0;
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
