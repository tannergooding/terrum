using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task DecimalFloatLiteralModuleMatchesHostedTargetRoundingAsync(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentPath = SelfHostPaths.ResolveFile(root, "DecimalFloatLiteral.terrum");
        var componentText = await File.ReadAllTextAsync(componentPath).ConfigureAwait(true);
        var componentTree = SyntaxTree.Parse(componentText, componentPath);
        Assert.That(componentTree.HasErrors, Is.False, string.Join(Environment.NewLine, componentTree.Diagnostics));

        var verificationTree = SyntaxTree.Parse(VerificationSource, "decimal-float-literal-module.terrum");
        var producerCompilation = Compilation.Create(
            [componentTree, verificationTree],
            [
                new HostBuffer("Source", PrimitiveType.Char16, [49], false) {
                    IsInvocationScoped = true,
                },
            ],
            policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = producerCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), ["Terrum.CodeAnalysis.VerifyRecords"]);

        var consumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyRecords(); }
            """, "decimal-float-literal-consumer.terrum");
        var consumerCompilation = Compilation.Create(
            consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.Link(
            ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer),
            producer);

        var emptyGrant = new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        };
        Assert.That(NativeJit.ExecuteWithSourceGrant(consumer, [emptyGrant], (NativeRuntimePolicy)policy), Is.EqualTo(0));

        var fixtures = s_fixtures.Concat(CreateRandomFixtures()).ToArray();
        var hostedBits = ReadHostedBits(fixtures);
        for (var index = 0; index < s_fixtures.Length; index++)
        {
            if (s_fixtures[index].KnownBits is ulong knownBits)
            {
                Assert.That(hostedBits[index], Is.EqualTo(knownBits));
            }
        }

        var records = CreateRecords(fixtures, hostedBits);
        records.AddRange(CreateRecords(
            s_invalidSpellings
                .SelectMany(static spelling => new[] { new Fixture(32, spelling, null), new Fixture(64, spelling, null) })
                .ToArray(),
            null));
        Assert.That(ExecuteRecords(consumer, records, (NativeRuntimePolicy)policy), Is.EqualTo(0));

        var longFraction = "1." + new string('0', 15_990) + "e0";
        var longFixture = new[] { new Fixture(64, longFraction, 0x3FF0000000000000) };
        var longHostedBits = ReadHostedBits(longFixture);
        Assert.That(longHostedBits[0], Is.EqualTo(0x3FF0000000000000UL));
        Assert.That(longFraction.Length + 7 <= 16_384, Is.True);
        Assert.That(ExecuteRecords(
            consumer, CreateRecords(longFixture, longHostedBits), (NativeRuntimePolicy)policy), Is.EqualTo(0));

        var longExponent = "1e" + new string('9', 15_990);
        var longExponentFixture = new[] { new Fixture(64, longExponent, 0x7FF0000000000000) };
        var longExponentBits = ReadHostedBits(longExponentFixture);
        Assert.That(longExponentBits[0], Is.EqualTo(0x7FF0000000000000UL));
        Assert.That(ExecuteRecords(
            consumer, CreateRecords(longExponentFixture, longExponentBits), (NativeRuntimePolicy)policy), Is.EqualTo(0));

        var longSignificand = new string('9', 15_990) + "e-15989";
        var longSignificandFixture = new[] { new Fixture(64, longSignificand, 0x4024000000000000) };
        var longSignificandBits = ReadHostedBits(longSignificandFixture);
        Assert.That(longSignificandBits[0], Is.EqualTo(0x4024000000000000UL));
        Assert.That(ExecuteRecords(
            consumer, CreateRecords(longSignificandFixture, longSignificandBits), (NativeRuntimePolicy)policy), Is.EqualTo(0));

        var longFractionCancellation = "0." + new string('0', 15_990) + "1e15991";
        var longFractionCancellationFixture = new[] {
            new Fixture(64, longFractionCancellation, 0x3FF0000000000000),
        };
        var longFractionCancellationBits = ReadHostedBits(longFractionCancellationFixture);
        Assert.That(longFractionCancellationBits[0], Is.EqualTo(0x3FF0000000000000UL));
        Assert.That(ExecuteRecords(consumer,
            CreateRecords(longFractionCancellationFixture, longFractionCancellationBits),
            (NativeRuntimePolicy)policy), Is.EqualTo(0));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void DecimalFloatLiteralExportsPreserveScalarSourceGrantAbi(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = RepositoryRoot();
        var componentPath = SelfHostPaths.ResolveFile(root, "DecimalFloatLiteral.terrum");
        var componentTree = SyntaxTree.Parse(File.ReadAllText(componentPath), componentPath);
        var sourceBuffer = new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        };
        var compilation = Compilation.CreateLibrary([componentTree], [sourceBuffer], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        const string Float32Export = "Terrum.CodeAnalysis.GetDecimalFloat32LiteralBitsOrMinusOne";
        const string Float64Export = "Terrum.CodeAnalysis.GetDecimalFloat64LiteralBitsOrMinusOne";
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(program,
            [Float32Export, Float64Export], (NativeRuntimePolicy)policy);
        foreach (var descriptor in artifact.Exports)
        {
            Assert.That(descriptor.SourceDependent, Is.True);
            Assert.That(descriptor.ReturnType, Is.EqualTo(PrimitiveType.Int64));
            Assert.That(descriptor.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        }

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-decimal-float-source-grant-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static export => (export.Name, export.SourceDependent,
                        export.ReturnType, export.ParameterTypes))]);
                var fixtures = s_fixtures.Concat(CreateRandomFixtures()).ToArray();
                var expectedBits = ReadHostedBits(fixtures);
                for (var index = 0; index < fixtures.Length; index++)
                {
                    var fixture = fixtures[index];
                    Assert.That(fixture.Spelling.Length, Is.InRange(1, 16_384));
                    var source = fixture.Spelling.ToCharArray();
                    var export = fixture.Precision == 32 ? Float32Export : Float64Export;
                    var actual = bindings.Invoke<long>(export, source, 0, source.Length);
                    Assert.That(unchecked((ulong)actual), Is.EqualTo(expectedBits[index]));
                }

                foreach (var spelling in s_invalidSpellings)
                {
                    var source = spelling.ToCharArray();
                    Assert.That(bindings.Invoke<long>(Float32Export, source, 0, source.Length), Is.EqualTo(-1));
                    Assert.That(bindings.Invoke<long>(Float64Export, source, 0, source.Length), Is.EqualTo(-1));
                }

                var maximumSpelling = MaximumLengthDecimalOne();
                Assert.That(maximumSpelling.Length, Is.EqualTo(16_384));
                var maximumSource = maximumSpelling.ToCharArray();
                Assert.That(bindings.Invoke<long>(Float32Export, maximumSource, 0, maximumSource.Length), Is.EqualTo(0x3F800000L));
                Assert.That(bindings.Invoke<long>(Float64Export, maximumSource, 0, maximumSource.Length), Is.EqualTo(0x3FF0000000000000L));

                var shortSource = "1.25".ToCharArray();
                Assert.That(bindings.Invoke<long>(Float32Export, null, 0, 1), Is.EqualTo(-1));
                Assert.That(bindings.Invoke<long>(Float64Export, shortSource, int.MinValue, 1), Is.EqualTo(-1));
                Assert.That(bindings.Invoke<long>(Float32Export, shortSource, 0, int.MaxValue), Is.EqualTo(-1));
                Assert.That(bindings.Invoke<long>(Float64Export, shortSource, 4, 1), Is.EqualTo(-1));
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<long>(Float32Export, new char[16_385], 0, 1))!;
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string MaximumLengthDecimalOne()
    {
        const int FractionalDigits = 15_000;
        const int Separators = 1_380;
        var spelling = new System.Text.StringBuilder(16_384);
        spelling.Append("1.");
        for (var index = 0; index < FractionalDigits; index++)
        {
            spelling.Append('0');
            if (index < Separators)
            {
                spelling.Append('_');
            }
        }
        spelling.Append("e0");
        return spelling.ToString();
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
        Assert.That(float64MidpointDigits.Length, Is.EqualTo(768));
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
        Assert.That(decimalPoint >= 0, Is.True);
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
            Assert.That(lexed.Diagnostics.IsEmpty, Is.True, $"Hosted lexer rejected generated fixture '{fixture.Spelling}': "
                + string.Join(", ", lexed.Diagnostics));
            Assert.That(lexed.Tokens.Length, Is.EqualTo(2));
            Assert.That(lexed.Tokens[0].Text, Is.EqualTo(fixture.Spelling));
            return SyntaxTree.Parse(source, $"decimal-float-oracle-{index}.terrum");
        });
        var compilation = Compilation.CreateLibrary(trees);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        var results = new ulong[fixtures.Length];
        for (var index = 0; index < fixtures.Length; index++)
        {
            var function = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == $"Read{index}");
            var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual4 = global::System.Linq.Enumerable.Single(function.Body.Statements); Assert.That(__nunitActual4, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual4!; }))();
            var constant = ((global::System.Func<BoundConstant>)(() => { var __nunitActual5 = result.Value; Assert.That(__nunitActual5, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual5!; }))();
            Assert.That(constant.Type, Is.EqualTo(fixtures[index].Precision == 32 ? PrimitiveType.Float32 : PrimitiveType.Float64));
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
        Assert.That(records, Is.Not.Empty);
        for (var start = 0; start < records.Count;)
        {
            var end = start;
            while (end < records.Count)
            {
                Assert.That(records.Count - end >= 7, Is.True);
                var recordLength = 7 + records[end + 2];
                Assert.That(recordLength, Is.InRange(8, 16_384));
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
