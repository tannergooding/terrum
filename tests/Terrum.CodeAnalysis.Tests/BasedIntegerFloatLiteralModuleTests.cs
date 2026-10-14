using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class BasedIntegerFloatLiteralModuleTests
{
    private const string Float32BitsExport = "Terrum.CodeAnalysis.GetBasedIntegerFloat32BitsOrMinusOne";
    private const string Float64BitsExport = "Terrum.CodeAnalysis.GetBasedIntegerFloat64BitsOrMinusOne";

    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() { return 0; }

        int64 ParseForFormatOrMinusOne(int32 format, int32 start, int32 length) {
            if (format == 32) {
                return GetBasedIntegerFloat32BitsOrMinusOne(start, length);
            }
            if (format == 64) {
                return GetBasedIntegerFloat64BitsOrMinusOne(start, length);
            }
            return -1;
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

    private static readonly Fixture[] s_fixtures = [
        new(32, "0x0", 0),
        new(64, "0b0", 0),
        new(32, "0x__1", 0x3F800000),
        new(64, "0B__1", 0x3FF0000000000000),
        new(32, "0x1__2", 0x41900000),
        new(64, "0b1__01", 0x4014000000000000),
        new(32, "0xFFFFFF", 0x4B7FFFFF),
        new(32, "0x1000000", 0x4B800000),
        new(32, "0b1" + new string('0', 23) + "1", 0x4B800000),
        new(32, "0x1000003", 0x4B800002),
        new(64, "0x1FFFFFFFFFFFFF", 0x433FFFFFFFFFFFFF),
        new(64, "0x20000000000001", 0x4340000000000000),
        new(64, "0b1" + new string('0', 52) + "1", 0x4340000000000000),
        new(64, "0x20000000000003", 0x4340000000000002),
        new(32, "0x1" + new string('0', 32), 0x7F800000),
        new(64, "0b1" + new string('0', 1100), 0x7FF0000000000000),
    ];

    private static readonly string[] s_invalidSpellings = [
        "0x",
        "0b",
        "0x_",
        "0x__",
        "0x1_",
        "0x1__",
        "0b1_",
        "0b2",
        "0b102",
        "0xG",
        "0x1p0",
        "0x1.0",
        "-0x1",
        "0x1\0",
    ];
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task BasedIntegerFloatLiteralModuleMatchesHostedTargetRoundingAsync(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "HexDigitValue", "BasedIntegerFloatLiteral" };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, path);
            Assert.That(trees[index].HasErrors, Is.False, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        trees[^1] = SyntaxTree.Parse(VerificationSource, "based-integer-float-literal-module.terrum");
        var producerCompilation = Compilation.Create(trees, [
            new HostBuffer("Source", PrimitiveType.Char16, [48, 120, 49], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = producerCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), ["Terrum.CodeAnalysis.VerifyRecords"]);

        var consumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyRecords(); }
            """, "based-integer-float-literal-consumer.terrum");
        var consumerCompilation = Compilation.Create(
            consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.Link(
            ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer),
            producer);

        var fixtures = s_fixtures.Concat(CreateRandomFixtures()).ToArray();
        var hostedBits = ReadHostedBits(fixtures);
        for (var index = 0; index < s_fixtures.Length; index++)
        {
            Assert.That(hostedBits[index], Is.EqualTo(s_fixtures[index].KnownBits));
        }

        var records = CreateRecords(fixtures, hostedBits);
        records.AddRange(CreateRecords(
            s_invalidSpellings.Select(static spelling => new Fixture(32, spelling, null)).ToArray(),
            null));
        Assert.That(ExecuteRecords(consumer, records, (NativeRuntimePolicy)policy), Is.EqualTo(0));

        var longSpelling = "0x1" + new string('0', 16_000);
        var longFixture = new[] { new Fixture(64, longSpelling, null) };
        var longHostedBits = ReadHostedBits(longFixture);
        Assert.That(longHostedBits[0], Is.EqualTo(0x7FF0000000000000UL));
        Assert.That(longSpelling.Length + 7 <= 16_384, Is.True);
        Assert.That(ExecuteRecords(
            consumer, CreateRecords(longFixture, longHostedBits), (NativeRuntimePolicy)policy), Is.EqualTo(0));

        foreach (var separatorSpelling in new[]
        {
            "0x" + new string('_', 16_000) + "1",
            "0x1" + new string('_', 16_000) + "1",
        })
        {
            var separatorFixture = new[] { new Fixture(64, separatorSpelling, null) };
            var separatorHostedBits = ReadHostedBits(separatorFixture);
            Assert.That(ExecuteRecords(
                consumer, CreateRecords(separatorFixture, separatorHostedBits), (NativeRuntimePolicy)policy), Is.EqualTo(0));
        }
    }

    [Test]
    public void MaintainedSourceGrantCompanionMatchesHostedBasedIntegerFloatBits()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Missing configuration directory.");
        var imagePath = Path.Combine(RepositoryRoot(), "artifacts", "bin", "self-host", configuration,
            "terrum-compiler-primitives-source-grant.dll");
        Assert.That(File.Exists(imagePath), Is.True, $"Source-grant compiler-primitives companion was not built: {imagePath}");

        var exports = new[]
        {
            (Float32BitsExport, true, PrimitiveType.Int64,
                System.Collections.Immutable.ImmutableArray.Create(PrimitiveType.Int32, PrimitiveType.Int32)),
            (Float64BitsExport, true, PrimitiveType.Int64,
                System.Collections.Immutable.ImmutableArray.Create(PrimitiveType.Int32, PrimitiveType.Int32)),
        };
        var module = NativeLibrary.Load(imagePath);
        try
        {
            var bindings = new NativeSourceGrantTestBindings(module, [.. exports]);
            var fixtures = s_fixtures.Concat(CreateRandomFixtures()).ToArray();
            var hostedBits = ReadHostedBits(fixtures);
            for (var index = 0; index < fixtures.Length; index++)
            {
                var fixture = fixtures[index];
                var export = fixture.Precision == 32 ? Float32BitsExport : Float64BitsExport;
                Assert.That(bindings.Invoke<long>(export, fixture.Spelling.ToCharArray(), 0, fixture.Spelling.Length), Is.EqualTo(unchecked((long)hostedBits[index])));
            }

            foreach (var spelling in s_invalidSpellings)
            {
                var source = spelling.ToCharArray();
                Assert.That(bindings.Invoke<long>(
                    Float32BitsExport, source, 0, source.Length), Is.EqualTo(-1L));
                Assert.That(bindings.Invoke<long>(
                    Float64BitsExport, source, 0, source.Length), Is.EqualTo(-1L));
            }

            var longSpelling = "0x1" + new string('0', 16_000);
            Assert.That(bindings.Invoke<long>(Float64BitsExport, longSpelling.ToCharArray(), 0, longSpelling.Length), Is.EqualTo(0x7FF0000000000000L));
            foreach (var separatorSpelling in new[]
            {
                "0x" + new string('_', 16_000) + "1",
                "0x1" + new string('_', 16_000) + "1",
            })
            {
                var separatorFixture = new[] { new Fixture(64, separatorSpelling, null) };
                var separatorHostedBits = ReadHostedBits(separatorFixture);
                Assert.That(bindings.Invoke<long>(
                        Float64BitsExport, separatorSpelling.ToCharArray(), 0, separatorSpelling.Length), Is.EqualTo(unchecked((long)separatorHostedBits[0])));
            }
            Assert.That(bindings.Invoke<long>(Float64BitsExport, "0x1".ToCharArray(), -1, 3), Is.EqualTo(-1L));
            Assert.That(bindings.Invoke<long>(Float64BitsExport, "0x1".ToCharArray(), 0, 4), Is.EqualTo(-1L));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                bindings.Invoke<long>(Float64BitsExport, new char[16_385], 0, 3))!;
        }
        finally
        {
            NativeLibrary.Free(module);
        }
    }

    private static ulong[] ReadHostedBits(Fixture[] fixtures)
    {
        var trees = fixtures.Select((fixture, index) => {
            var type = fixture.Precision == 32 ? "float32" : "float64";
            return SyntaxTree.Parse(
                $"public {type} Read{index}() {{ return {fixture.Spelling}; }}",
                $"based-integer-float-oracle-{index}.terrum");
        });
        var compilation = Compilation.CreateLibrary(trees);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        var results = new ulong[fixtures.Length];
        for (var index = 0; index < fixtures.Length; index++)
        {
            var function = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == $"Read{index}");
            var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual3 = global::System.Linq.Enumerable.Single(function.Body.Statements); Assert.That(__nunitActual3, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual3!; }))();
            var constant = ((global::System.Func<BoundConstant>)(() => { var __nunitActual4 = result.Value; Assert.That(__nunitActual4, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual4!; }))();
            Assert.That(constant.Type, Is.EqualTo(fixtures[index].Precision == 32 ? PrimitiveType.Float32 : PrimitiveType.Float64));
            results[index] = unchecked((ulong)constant.Value);
        }

        return results;
    }

    private static Fixture[] CreateRandomFixtures()
    {
        const string HexDigits = "0123456789abcdef";
        var random = new Random(0xBA5ED);
        var fixtures = new Fixture[192];
        for (var index = 0; index < fixtures.Length; index++)
        {
            var precision = random.Next(2) == 0 ? 32 : 64;
            var isHexadecimal = random.Next(2) == 0;
            var digits = new char[random.Next(1, 49)];
            for (var digitIndex = 0; digitIndex < digits.Length; digitIndex++)
            {
                digits[digitIndex] = isHexadecimal
                    ? HexDigits[random.Next(HexDigits.Length)]
                    : random.Next(2) == 0 ? '0' : '1';
            }

            var prefix = isHexadecimal ? "0x" : "0b";
            var body = FormatDigits(digits, random);
            if (random.Next(4) == 0)
            {
                body = "__" + body;
            }
            fixtures[index] = new Fixture(precision, prefix + body, null);
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
        Assert.That(records.Count, Is.InRange(1, 16_384));
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
