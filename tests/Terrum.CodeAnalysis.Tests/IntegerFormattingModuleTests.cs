using System;
using System.Collections.Generic;
using System.Globalization;
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

public sealed class IntegerFormattingModuleTests
{
    private const int MaximumSourceCodeUnits = 16_384;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task IntegerFormattingModuleMatchesFreshSourceGrantsAsync(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "WideIntegerFacts", "IntegerFormatting" };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, path);
            Assert.False(trees[index].HasErrors, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        var fixtures = Fixtures();
        var calls = string.Join(Environment.NewLine, fixtures.Select((fixture, index) =>
            FormattableString.Invariant(
                $"position = Verify{fixture.Kind}({fixture.TerrumValue}, position, radix, uppercase, useBase);")
            + Environment.NewLine
            + $"if (position < 0) {{ return {index + 1}; }}"));
        trees[^1] = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis.Numerics64;
            int32 Main() { return 0; }

            int32 VerifyDigits(OwnedInt32Buffer digits, int32 position) {
                if (position < 0 || position > Host.Source.Length
                    || Host.Source.Length - position <= digits.Count) { return -1; }
                if (digits.Capacity != digits.Count) { return -1; }
                for (mut int32 index = 0; index < digits.Count; index = index + 1) {
                    if (digits.Read(index) != int32(Host.Source[position + index])) { return -1; }
                }
                if (Host.Source[position + digits.Count] != char16(0)) { return -1; }
                return position + digits.Count + 1;
            }

            int32 VerifyUInt64(uint64 value, int32 position, uint32 radix, bool uppercase, bool useBase) {
                if (!TryGetUInt64BaseLength(value, radix, init var capacity)) { return -1; }
                require OwnedInt32Buffer.TryCreate(capacity, out var workspace);
                if (!useBase) { return VerifyDigits(FormatUInt64(value, move workspace), position); }
                if (uppercase) {
                    return VerifyDigits(FormatUInt64BaseUpper(value, radix, move workspace), position);
                }
                return VerifyDigits(FormatUInt64Base(value, radix, move workspace), position);
            }

            int32 VerifyInt64(int64 value, int32 position, uint32 radix, bool uppercase, bool useBase) {
                if (!TryGetInt64BaseLength(value, radix, init var capacity)) { return -1; }
                require OwnedInt32Buffer.TryCreate(capacity, out var workspace);
                if (!useBase) { return VerifyDigits(FormatInt64(value, move workspace), position); }
                if (uppercase) {
                    return VerifyDigits(FormatInt64BaseUpper(value, radix, move workspace), position);
                }
                return VerifyDigits(FormatInt64Base(value, radix, move workspace), position);
            }

            int32 VerifyUInt32(uint32 value, int32 position, uint32 radix, bool uppercase, bool useBase) {
                if (!TryGetUInt32BaseLength(value, radix, init var capacity)) { return -1; }
                require OwnedInt32Buffer.TryCreate(capacity, out var workspace);
                if (!useBase) { return VerifyDigits(FormatUInt32(value, move workspace), position); }
                if (uppercase) {
                    return VerifyDigits(FormatUInt32BaseUpper(value, radix, move workspace), position);
                }
                return VerifyDigits(FormatUInt32Base(value, radix, move workspace), position);
            }

            int32 VerifyInt32(int32 value, int32 position, uint32 radix, bool uppercase, bool useBase) {
                if (!TryGetInt32BaseLength(value, radix, init var capacity)) { return -1; }
                require OwnedInt32Buffer.TryCreate(capacity, out var workspace);
                if (!useBase) { return VerifyDigits(FormatInt32(value, move workspace), position); }
                if (uppercase) {
                    return VerifyDigits(FormatInt32BaseUpper(value, radix, move workspace), position);
                }
                return VerifyDigits(FormatInt32Base(value, radix, move workspace), position);
            }

            int32 VerifyChar16(char16 value, int32 position, uint32 radix, bool uppercase, bool useBase) {
                if (!TryGetChar16BaseLength(value, radix, init var capacity)) { return -1; }
                require OwnedInt32Buffer.TryCreate(capacity, out var workspace);
                if (!useBase) { return VerifyDigits(FormatChar16(value, move workspace), position); }
                if (uppercase) {
                    return VerifyDigits(FormatChar16BaseUpper(value, radix, move workspace), position);
                }
                return VerifyDigits(FormatChar16Base(value, radix, move workspace), position);
            }

            public int32 VerifySnapshot() {
                if (Host.Source.Length < 3) { return -1; }
                int32 mode = int32(Host.Source[0]);
                if (mode < 0 || mode > 1) { return -1; }
                uint32 radix = uint32(Host.Source[1]);
                bool uppercase = Host.Source[2] == char16(1);
                bool useBase = mode == 1;
                mut int32 position = 3;
                {{calls}}
                if (position != Host.Source.Length) { return -2; }
                return 0;
            }
            """, "integer-formatting-module.terrum");

        var producerCompilation = Compilation.Create(trees,
        [
            new HostBuffer("Source", PrimitiveType.Char16, [1, 10, 0, 48, 0], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.Numerics64.VerifySnapshot"]);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis.Numerics64;
            int32 Main() { return VerifySnapshot(); }
            """, "integer-formatting-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        foreach (var (mode, radix, uppercase) in Snapshots())
        {
            var codeUnits = new List<int> { mode, radix, uppercase ? 1 : 0 };
            foreach (var fixture in fixtures)
            {
                var expected = ExpectedText(fixture.Value, radix, uppercase, mode == 1);
                codeUnits.AddRange(expected.Select(static character => (int)character));
                codeUnits.Add(0);
            }
            Assert.InRange(codeUnits.Count, 1, MaximumSourceCodeUnits);

            var grant = new HostBuffer("Source", PrimitiveType.Char16, [.. codeUnits], false) {
                IsInvocationScoped = true,
            };
            var actual = NativeJit.ExecuteWithSourceGrant(linked, [grant], (NativeRuntimePolicy)policy);
            Assert.True(actual == 0,
                $"mode {mode}, radix {radix}, uppercase {uppercase}: verifier returned {actual}.");
        }
    }

    private static IEnumerable<FormatFixture> Fixtures()
    {
        yield return new(FormatterKind.UInt64, "uint64(0)", BigInteger.Zero);
        yield return new(FormatterKind.UInt64, "uint64.MaxValue", new BigInteger(ulong.MaxValue));
        yield return new(FormatterKind.Int64, "int64.MinValue", new BigInteger(long.MinValue));
        yield return new(FormatterKind.Int64, "int64.MaxValue", new BigInteger(long.MaxValue));
        yield return new(FormatterKind.Int64, "int64(-1)", BigInteger.MinusOne);
        yield return new(FormatterKind.Int64, "int64(0)", BigInteger.Zero);
        yield return new(FormatterKind.UInt32, "uint32(0)", BigInteger.Zero);
        yield return new(FormatterKind.UInt32, "uint32.MaxValue", new BigInteger(uint.MaxValue));
        yield return new(FormatterKind.Int32, "int32.MinValue", new BigInteger(int.MinValue));
        yield return new(FormatterKind.Int32, "int32.MaxValue", new BigInteger(int.MaxValue));
        yield return new(FormatterKind.Int32, "int32(-1)", BigInteger.MinusOne);
        yield return new(FormatterKind.Int32, "int32(0)", BigInteger.Zero);
        yield return new(FormatterKind.Char16, "char16(0)", BigInteger.Zero);
        yield return new(FormatterKind.Char16, "char16(55296)", new BigInteger(55296));
        yield return new(FormatterKind.Char16, "char16.MaxValue", new BigInteger(char.MaxValue));
    }

    private static IEnumerable<(int Mode, int Radix, bool Uppercase)> Snapshots()
    {
        yield return (0, 10, false);
        foreach (var radix in Enumerable.Range(2, 35))
        {
            yield return (1, radix, false);
            yield return (1, radix, true);
        }
    }

    private static string ExpectedText(BigInteger value, int radix, bool uppercase, bool useBase)
    {
        if (!useBase)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var alphabet = uppercase ? Digits.ToUpperInvariant() : Digits;
        var negative = value.Sign < 0;
        var remaining = BigInteger.Abs(value);
        var characters = new char[65];
        var start = characters.Length;
        do
        {
            remaining = BigInteger.DivRem(remaining, radix, out var remainder);
            characters[--start] = alphabet[(int)remainder];
        }
        while (remaining > BigInteger.Zero);
        if (negative)
        {
            characters[--start] = '-';
        }
        return new string(characters, start, characters.Length - start);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private enum FormatterKind
    {
        UInt64,
        Int64,
        UInt32,
        Int32,
        Char16,
    }

    private sealed record FormatFixture(FormatterKind Kind, string TerrumValue, BigInteger Value);
}
