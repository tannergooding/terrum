using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class IntegerParsingRadixModuleTests
{
    private const int MaximumSourceCodeUnits = 16_384;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task IntegerParsingRadixModuleMatchesHostOracleAcrossFreshSourceGrantsAsync(
        BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "WideIntegerFacts", "IntegerParsing" };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, path);
            Assert.False(trees[index].HasErrors, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        trees[^1] = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis.Numerics64;
            int32 Main() { return 0; }

            bool MatchesUInt64(uint64 value, int32 expectedStart) {
                return int32(value & uint64(65535)) == int32(Host.Source[expectedStart])
                    && int32((value >> 16) & uint64(65535)) == int32(Host.Source[expectedStart + 1])
                    && int32((value >> 32) & uint64(65535)) == int32(Host.Source[expectedStart + 2])
                    && int32((value >> 48) & uint64(65535)) == int32(Host.Source[expectedStart + 3]);
            }
            bool MatchesInt64(int64 value, int32 expectedStart) {
                return int32(value & int64(65535)) == int32(Host.Source[expectedStart])
                    && int32((value >> 16) & int64(65535)) == int32(Host.Source[expectedStart + 1])
                    && int32((value >> 32) & int64(65535)) == int32(Host.Source[expectedStart + 2])
                    && int32((value >> 48) & int64(65535)) == int32(Host.Source[expectedStart + 3]);
            }
            bool MatchesUInt32(uint32 value, int32 expectedStart) {
                return int32(value & uint32(65535)) == int32(Host.Source[expectedStart])
                    && int32((value >> 16) & uint32(65535)) == int32(Host.Source[expectedStart + 1])
                    && Host.Source[expectedStart + 2] == char16(0)
                    && Host.Source[expectedStart + 3] == char16(0);
            }
            bool MatchesInt32(int32 value, int32 expectedStart) {
                return (value & int32(65535)) == int32(Host.Source[expectedStart])
                    && ((value >> 16) & int32(65535)) == int32(Host.Source[expectedStart + 1])
                    && Host.Source[expectedStart + 2] == char16(0)
                    && Host.Source[expectedStart + 3] == char16(0);
            }
            bool MatchesChar16(char16 value, int32 expectedStart) {
                return int32(value) == int32(Host.Source[expectedStart])
                    && Host.Source[expectedStart + 1] == char16(0)
                    && Host.Source[expectedStart + 2] == char16(0)
                    && Host.Source[expectedStart + 3] == char16(0);
            }

            public int32 VerifyBatch() {
                mut int32 position = 0;
                mut int32 caseIndex = 0;
                while (position < Host.Source.Length) {
                    if (Host.Source.Length - position < 8) { return -1; }
                    int32 api = int32(Host.Source[position]);
                    uint32 radix = uint32(Host.Source[position + 1]);
                    int32 textLength = int32(Host.Source[position + 2]);
                    int32 expectedSuccess = int32(Host.Source[position + 3]);
                    int32 textStart = position + 8;
                    if (textLength > Host.Source.Length - textStart) { return -2; }
                    if (api == 0) {
                        if (TryParseUInt64Base(textStart, textLength, radix, init var parsed)) {
                            if (expectedSuccess != 1 || !MatchesUInt64(parsed, position + 4)) { return caseIndex + 1; }
                        } else if (expectedSuccess != 0) { return caseIndex + 1; }
                    } else if (api == 1) {
                        if (TryParseInt64Base(textStart, textLength, radix, init var parsed)) {
                            if (expectedSuccess != 1 || !MatchesInt64(parsed, position + 4)) { return caseIndex + 1; }
                        } else if (expectedSuccess != 0) { return caseIndex + 1; }
                    } else if (api == 2) {
                        if (TryParseUInt32Base(textStart, textLength, radix, init var parsed)) {
                            if (expectedSuccess != 1 || !MatchesUInt32(parsed, position + 4)) { return caseIndex + 1; }
                        } else if (expectedSuccess != 0) { return caseIndex + 1; }
                    } else if (api == 3) {
                        if (TryParseInt32Base(textStart, textLength, radix, init var parsed)) {
                            if (expectedSuccess != 1 || !MatchesInt32(parsed, position + 4)) { return caseIndex + 1; }
                        } else if (expectedSuccess != 0) { return caseIndex + 1; }
                    } else if (api == 4) {
                        if (TryParseChar16Base(textStart, textLength, radix, init var parsed)) {
                            if (expectedSuccess != 1 || !MatchesChar16(parsed, position + 4)) { return caseIndex + 1; }
                        } else if (expectedSuccess != 0) { return caseIndex + 1; }
                    } else {
                        return -3;
                    }
                    position = textStart + textLength;
                    caseIndex = caseIndex + 1;
                }
                return 0;
            }
            """, "integer-parsing-radix-module.terrum");

        var producerCompilation = Compilation.Create(trees,
        [
            new HostBuffer("Source", PrimitiveType.Char16, [0], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.Numerics64.VerifyBatch"]);

        var consumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis.Numerics64;
            int32 Main() { return VerifyBatch(); }
            """, "integer-parsing-radix-consumer.terrum");
        var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        foreach (var radix in Enumerable.Range(2, 35))
        {
            var fixtures = FixturesForRadix(radix).ToArray();
            ExecuteBatch(fixtures, linked, policy, $"radix {radix}");
        }

        var invalidRadixFixtures = (
            from radix in new[] { 0, 1, 37 }
            from api in Enum.GetValues<ParserApi>()
            select new ParseFixture(api, radix, "1")).ToArray();
        ExecuteBatch(invalidRadixFixtures, linked, policy, "invalid radices");

        var maximumLengthZeros = new string('0', MaximumSourceCodeUnits - 8);
        var maximumLengthNines = new string('9', MaximumSourceCodeUnits - 8);
        Assert.Equal(MaximumSourceCodeUnits, 8 + maximumLengthZeros.Length);
        foreach (var api in Enum.GetValues<ParserApi>())
        {
            ExecuteBatch([new ParseFixture(api, 10, maximumLengthZeros)],
                linked, policy, $"maximum-length zero digits for {api}");
            ExecuteBatch([new ParseFixture(api, 10, maximumLengthNines)],
                linked, policy, $"maximum-length overflowing digits for {api}");
        }
    }

    private static void ExecuteBatch(ParseFixture[] fixtures, BoundProgram linked,
        BoundRuntimePolicy policy, string description)
    {
        var values = new List<int>(fixtures.Sum(static fixture => 8 + fixture.Text.Length));
        for (var index = 0; index < fixtures.Length; index++)
        {
            var fixture = fixtures[index];
            var expected = ParseOracle(fixture);
            values.Add((int)fixture.Api);
            values.Add(fixture.Radix);
            values.Add(fixture.Text.Length);
            values.Add(expected.HasValue ? 1 : 0);

            var bits = expected.GetValueOrDefault();
            if (bits.Sign < 0)
            {
                bits += BigInteger.One << BitWidth(fixture.Api);
            }
            for (var part = 0; part < 4; part++)
            {
                values.Add((int)((bits >> (part * 16)) & ushort.MaxValue));
            }
            values.AddRange(fixture.Text.Select(static codeUnit => (int)codeUnit));
        }

        Assert.InRange(values.Count, 1, MaximumSourceCodeUnits);
        var grant = new HostBuffer("Source", PrimitiveType.Char16, [.. values], false) {
            IsInvocationScoped = true,
        };
        var actual = NativeJit.ExecuteWithSourceGrant(linked, [grant], (NativeRuntimePolicy)policy);
        var failure = actual > 0 && actual <= fixtures.Length
            ? $"case {actual - 1}: {fixtures[actual - 1]}"
            : $"unexpected module result {actual}";
        Assert.True(actual == 0, $"{description}: {failure}.");
    }

    private static IEnumerable<ParseFixture> FixturesForRadix(int radix)
    {
        foreach (var api in Enum.GetValues<ParserApi>())
        {
            yield return new(api, radix, "0");
            yield return new(api, radix, "?");
            yield return new(api, radix, "1_0");
            yield return new(api, radix, "1__0");
            yield return new(api, radix, "1_");
            yield return new(api, radix, "");
            yield return new(api, radix, "-1");
            if (radix > 10)
            {
                yield return new(api, radix, Digit(radix - 1).ToString().ToUpperInvariant());
            }
            if (radix < 36)
            {
                yield return new(api, radix, Digit(radix).ToString());
            }
        }

        yield return new(ParserApi.UInt64, radix, ToBase(ulong.MaxValue, radix));
        yield return new(ParserApi.UInt64, radix, ToBase((BigInteger)ulong.MaxValue + 1, radix));
        yield return new(ParserApi.Int64, radix, ToBase(long.MaxValue, radix));
        yield return new(ParserApi.Int64, radix, "-" + ToBase(BigInteger.One << 63, radix));
        yield return new(ParserApi.Int64, radix, ToBase((BigInteger)long.MaxValue + 1, radix));
        yield return new(ParserApi.Int64, radix, "-" + ToBase((BigInteger.One << 63) + 1, radix));
        yield return new(ParserApi.UInt32, radix, ToBase(uint.MaxValue, radix));
        yield return new(ParserApi.UInt32, radix, ToBase((BigInteger)uint.MaxValue + 1, radix));
        yield return new(ParserApi.Int32, radix, ToBase(int.MaxValue, radix));
        yield return new(ParserApi.Int32, radix, ToBase((BigInteger)int.MaxValue + 1, radix));
        yield return new(ParserApi.Int32, radix, "-" + ToBase((BigInteger)int.MaxValue + 1, radix));
        yield return new(ParserApi.Int32, radix, "-" + ToBase(int.MaxValue, radix));
        yield return new(ParserApi.Int32, radix, "-" + ToBase((BigInteger)int.MaxValue + 2, radix));
        yield return new(ParserApi.Int32, radix, "-1");
        yield return new(ParserApi.Char16, radix, ToBase((int)char.MaxValue, radix));
        yield return new(ParserApi.Char16, radix, ToBase((BigInteger)(int)char.MaxValue + 1, radix));
        yield return new(ParserApi.Char16, radix, ToBase(55296, radix));

        yield return new(ParserApi.Int64, radix, "-0");
        yield return new(ParserApi.Int32, radix, "-0");
        yield return new(ParserApi.UInt64, radix, "-0");
        yield return new(ParserApi.UInt32, radix, "-0");
        yield return new(ParserApi.Char16, radix, "-0");

        yield return new(ParserApi.UInt64, radix, "_1");
        yield return new(ParserApi.Int64, radix, "_1");
        yield return new(ParserApi.UInt32, radix, "_1");
        yield return new(ParserApi.Int32, radix, "_1");
        yield return new(ParserApi.Char16, radix, "_1");
    }

    private static BigInteger? ParseOracle(ParseFixture fixture)
    {
        if (fixture.Radix is < 2 or > 36)
        {
            return null;
        }

        var text = fixture.Text;
        var start = 0;
        var negative = text.StartsWith("-", StringComparison.Ordinal);
        if (negative)
        {
            if (fixture.Api is not (ParserApi.Int64 or ParserApi.Int32))
            {
                return null;
            }
            start = 1;
        }
        if (start == text.Length)
        {
            return null;
        }

        var value = BigInteger.Zero;
        var hasDigit = false;
        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '_')
            {
                if (fixture.Radix is not (2 or 10 or 16)
                    || (fixture.Radix == 10 && index == start))
                {
                    return null;
                }
                var next = index + 1;
                while (next < text.Length && text[next] == '_')
                {
                    next++;
                }
                if (next == text.Length)
                {
                    return null;
                }
                index = next - 1;
                continue;
            }

            var digit = character switch {
                >= '0' and <= '9' => character - '0',
                >= 'A' and <= 'Z' => character - 'A' + 10,
                >= 'a' and <= 'z' => character - 'a' + 10,
                _ => -1,
            };
            if (digit < 0 || digit >= fixture.Radix)
            {
                return null;
            }
            hasDigit = true;
            value = value * fixture.Radix + digit;
        }

        if (!hasDigit)
        {
            return null;
        }
        if (negative)
        {
            value = -value;
        }

        var (minimum, maximum) = fixture.Api switch {
            ParserApi.UInt64 => (BigInteger.Zero, (BigInteger)ulong.MaxValue),
            ParserApi.Int64 => ((BigInteger)long.MinValue, (BigInteger)long.MaxValue),
            ParserApi.UInt32 => (BigInteger.Zero, (BigInteger)uint.MaxValue),
            ParserApi.Int32 => ((BigInteger)int.MinValue, (BigInteger)int.MaxValue),
            ParserApi.Char16 => (BigInteger.Zero, (BigInteger)(int)char.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(fixture)),
        };
        return value < minimum || value > maximum ? null : value;
    }

    private static int BitWidth(ParserApi api) => api switch {
        ParserApi.UInt64 or ParserApi.Int64 => 64,
        ParserApi.UInt32 or ParserApi.Int32 => 32,
        ParserApi.Char16 => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(api)),
    };

    private static char Digit(int value) => "0123456789abcdefghijklmnopqrstuvwxyz"[value];

    private static string ToBase(BigInteger value, int radix)
    {
        var characters = new char[128];
        var start = characters.Length;
        do
        {
            value = BigInteger.DivRem(value, radix, out var remainder);
            characters[--start] = Digit((int)remainder);
        }
        while (value > BigInteger.Zero);
        return new string(characters, start, characters.Length - start);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private enum ParserApi
    {
        UInt64,
        Int64,
        UInt32,
        Int32,
        Char16,
    }

    private sealed record ParseFixture(ParserApi Api, int Radix, string Text);
}
