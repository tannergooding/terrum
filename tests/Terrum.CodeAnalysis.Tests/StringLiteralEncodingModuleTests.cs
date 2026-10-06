using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StringLiteralEncodingModuleTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() { return 0; }

        bool TryGetLiteral(init ref<StringLiteral> literal) {
            if (Host.Source.Length < 2) { return false; }
            int32 expectedLength = int32(Host.Source[1]);
            mut int32 literalStart = 2;
            if (expectedLength != 65535) {
                if (expectedLength > Host.Source.Length - literalStart) { return false; }
                literalStart = literalStart + expectedLength;
            }
            if (!SourceRange.TryCreate(literalStart, Host.Source.Length - literalStart, init var spelling)) {
                return false;
            }
            return StringLiteral.TryCreate(spelling, init literal);
        }

        bool TryGetEncodedLength(
            StringLiteral literal,
            int32 encoding,
            init ref<int32> length
        ) {
            if (encoding == 0) { return literal.TryGetUtf8Length(init length); }
            if (encoding == 1) { return literal.TryGetUtf16Length(init length); }
            if (encoding == 2 || encoding == 3) { return literal.TryGetUtf16ByteLength(init length); }
            if (encoding == 4) { return literal.TryGetAsciiLength(init length); }
            if (encoding == 5) { return literal.TryGetLatin1Length(init length); }
            if (encoding == 6 || encoding == 7) { return literal.TryGetUtf32ByteLength(init length); }
            return false;
        }

        int32 GetSourceGrantEncoding(int32 encoding) {
            if (encoding == 0) { return 2; }
            if (encoding == 1) { return 3; }
            if (encoding == 2 || encoding == 3) { return 4; }
            if (encoding == 4) { return 0; }
            if (encoding == 5) { return 1; }
            return 5;
        }

        OwnedInt32Buffer Encode(
            StringLiteral literal,
            int32 encoding,
            OwnedInt32Buffer workspace
        ) {
            if (encoding == 0) { return literal.EncodeUtf8(move workspace); }
            if (encoding == 1) { return literal.EncodeUtf16(move workspace); }
            if (encoding == 2) { return literal.EncodeUtf16LeBytes(move workspace); }
            if (encoding == 3) { return literal.EncodeUtf16BeBytes(move workspace); }
            if (encoding == 4) { return literal.EncodeAscii(move workspace); }
            if (encoding == 5) { return literal.EncodeLatin1(move workspace); }
            if (encoding == 6) { return literal.EncodeUtf32LeBytes(move workspace); }
            require encoding == 7;
            return literal.EncodeUtf32BeBytes(move workspace);
        }

        int32 VerifyEncoding(int32 encoding) {
            if (Host.Source.Length < 2 || int32(Host.Source[0]) != encoding) { return -1; }
            if (!TryGetLiteral(init var literal)) { return -2; }

            int32 expectedLength = int32(Host.Source[1]);
            if (expectedLength == 65535) {
                if (encoding != 4 && encoding != 5) { return -3; }
                if (TryGetEncodedLength(literal, encoding, init var rejectedLength)) { return -4; }
                if (GetStringLiteralEncodedLengthOrMinusOne(
                    literal.Spelling.Start, literal.Spelling.Length, GetSourceGrantEncoding(encoding)) != -1) {
                    return -10;
                }
                return 0;
            }

            if (!TryGetEncodedLength(literal, encoding, init var measuredLength)) { return -5; }
            if (measuredLength != expectedLength) { return -6; }
            if (GetStringLiteralEncodedLengthOrMinusOne(
                literal.Spelling.Start, literal.Spelling.Length, GetSourceGrantEncoding(encoding))
                != measuredLength) { return -10; }
            if (!OwnedInt32Buffer.TryCreate(expectedLength, out var workspace)) { return -7; }
            mut OwnedInt32Buffer encoded = Encode(literal, encoding, move workspace);
            if (encoded.Count != expectedLength || encoded.Capacity != expectedLength) { return -8; }
            mut int32 index = 0;
            while (index < expectedLength) {
                if (encoded.Read(index) != int32(Host.Source[index + 2])) { return -9; }
                index = index + 1;
            }
            return 0;
        }

        public int32 VerifyUtf8() { return VerifyEncoding(0); }
        public int32 VerifyUtf16Units() { return VerifyEncoding(1); }
        public int32 VerifyUtf16LeBytes() { return VerifyEncoding(2); }
        public int32 VerifyUtf16BeBytes() { return VerifyEncoding(3); }
        public int32 VerifyAscii() { return VerifyEncoding(4); }
        public int32 VerifyLatin1() { return VerifyEncoding(5); }
        public int32 VerifyUtf32LeBytes() { return VerifyEncoding(6); }
        public int32 VerifyUtf32BeBytes() { return VerifyEncoding(7); }

        public int32 VerifyMaximumAsciiLiteral() {
            if (!SourceRange.TryCreate(0, Host.Source.Length, init var spelling)
                || !StringLiteral.TryCreate(spelling, init var literal)) { return -1; }
            int32 scalarCount = Host.Source.Length - 2;
            if (literal.ScalarCount != scalarCount) { return -2; }
            for (mut int32 encoding = 0; encoding < 8; encoding = encoding + 1) {
                if (!TryGetEncodedLength(literal, encoding, init var measured)) { return -3; }
                mut int32 width = 1;
                if (encoding == 2 || encoding == 3) { width = 2; }
                if (encoding == 6 || encoding == 7) { width = 4; }
                int32 expectedLength = scalarCount * width;
                if (GetStringLiteralEncodedLengthOrMinusOne(
                    literal.Spelling.Start, literal.Spelling.Length, GetSourceGrantEncoding(encoding))
                    != expectedLength) { return -7; }
                if (measured != expectedLength
                    || !OwnedInt32Buffer.TryCreate(expectedLength, out var workspace)) { return -4; }
                mut OwnedInt32Buffer encoded = Encode(literal, encoding, move workspace);
                if (encoded.Count != expectedLength || encoded.Capacity != expectedLength) { return -5; }
                for (mut int32 index = 0; index < expectedLength; index = index + 1) {
                    mut int32 expected = 97;
                    int32 byteIndex = index % width;
                    if ((encoding == 2 && byteIndex == 1)
                        || (encoding == 3 && byteIndex == 0)
                        || (encoding == 6 && byteIndex != 0)
                        || (encoding == 7 && byteIndex != 3)) { expected = 0; }
                    if (encoded.Read(index) != expected) { return -6; }
                }
            }
            return scalarCount;
        }

        public int32 VerifyMaximumUtf8Literal() {
            if (!SourceRange.TryCreate(0, Host.Source.Length, init var spelling)
                || !StringLiteral.TryCreate(spelling, init var literal)) { return -1; }
            int32 scalarCount = Host.Source.Length - 2;
            if (literal.ScalarCount != scalarCount) { return -2; }
            int32 expectedLength = scalarCount * 3;
            if (!literal.TryGetUtf8Length(init var measured) || measured != expectedLength) { return -3; }
            if (GetStringLiteralEncodedLengthOrMinusOne(
                literal.Spelling.Start, literal.Spelling.Length, 2) != expectedLength) { return -4; }
            if (!OwnedInt32Buffer.TryCreate(expectedLength, out var workspace)) { return -5; }
            mut OwnedInt32Buffer encoded = literal.EncodeUtf8(move workspace);
            if (encoded.Count != expectedLength || encoded.Capacity != expectedLength) { return -6; }
            for (mut int32 index = 0; index < expectedLength; index = index + 1) {
                mut int32 expected = 128;
                if (index % 3 == 0) { expected = 224; }
                else if (index % 3 == 1) { expected = 160; }
                if (encoded.Read(index) != expected) { return -7; }
            }
            return expectedLength;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StringLiteralEncodingModuleMatchesHostEncodersAcrossFreshSourceGrants(
        BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] {
            "SourceRange", "CharacterFacts", "HexDigitValue", "Utf16CodeUnits", "Utf16Scalar",
            "StringEscape", "StringEscapePacked", "StringLiteral", "StringLiteralScalarCount",
            "StringLiteralEncodedLength",
        };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, path);
            Assert.False(trees[index].HasErrors, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }
        trees[^1] = SyntaxTree.Parse(VerificationSource, "string-literal-encoding-module.terrum");

        var producerCompilation = Compilation.Create(trees,
        [
            new HostBuffer("Source", PrimitiveType.Char16, [0, 0, 34, 34], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));

        var exports = new[] {
            "VerifyUtf8",
            "VerifyUtf16Units",
            "VerifyUtf16LeBytes",
            "VerifyUtf16BeBytes",
            "VerifyAscii",
            "VerifyLatin1",
            "VerifyUtf32LeBytes",
            "VerifyUtf32BeBytes",
            "VerifyMaximumAsciiLiteral",
            "VerifyMaximumUtf8Literal",
        };
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            [.. exports.Select(static name => "Terrum.CodeAnalysis." + name)]);
        var consumers = new BoundProgram[exports.Length];
        for (var index = 0; index < exports.Length; index++)
        {
            var consumerTree = SyntaxTree.Parse(
                $"namespace Terrum.CodeAnalysis; int32 Main() {{ return {exports[index]}(); }}",
                "string-literal-encoding-consumer.terrum");
            var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
            Assert.False(consumerCompilation.HasErrors,
                string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumer = ModuleCompiler.CreateConsumer(
                Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
            consumers[index] = ModuleCompiler.Link(consumer, producer);
        }

        var strictUtf8 = new UTF8Encoding(false, true);
        var strictUtf16Le = new UnicodeEncoding(false, false, true);
        var strictUtf16Be = new UnicodeEncoding(true, false, true);
        var strictUtf32Le = new UTF32Encoding(false, false, true);
        var strictUtf32Be = new UTF32Encoding(true, false, true);
        var strictAscii = Encoding.GetEncoding(Encoding.ASCII.CodePage,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var strictLatin1 = Encoding.GetEncoding(28591,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var fixtures = new[] {
            new LiteralFixture("", "\"\""),
            new LiteralFixture("ASCII", "\"ASCII\""),
            new LiteralFixture("é", "\"é\""),
            new LiteralFixture("é", "\"\\u00E9\""),
            new LiteralFixture("😀", "\"😀\""),
            new LiteralFixture("😀", "\"\\U0001F600\""),
            new LiteralFixture("Aé😀\0", "\"A\\u00E9\\U0001F600\\0\""),
            new LiteralFixture("\U00010000", "\"\\U00010000\""),
            new LiteralFixture("\U0010FFFF", "\"\\U0010FFFF\""),
            new LiteralFixture("\U00010000\U0010FFFF", "\"\U00010000\U0010FFFF\""),
            new LiteralFixture("\u007F\u0080\u07FF\u0800\uFFFF",
                "\"\\u007F\\u0080\\u07FF\\u0800\\uFFFF\""),
        };

        void VerifyFixture(string text, string spelling, string context)
        {
            Assert.True(spelling.Length + 2 <= 16_384);
            Assert.Empty(Lexer.Lex(SourceText.From(spelling)).Diagnostics);

            var expected = new int[]?[] {
                strictUtf8.GetBytes(text).Select(static value => (int)value).ToArray(),
                text.Select(static unit => (int)unit).ToArray(),
                strictUtf16Le.GetBytes(text).Select(static value => (int)value).ToArray(),
                strictUtf16Be.GetBytes(text).Select(static value => (int)value).ToArray(),
                TryEncode(strictAscii, text),
                TryEncode(strictLatin1, text),
                strictUtf32Le.GetBytes(text).Select(static value => (int)value).ToArray(),
                strictUtf32Be.GetBytes(text).Select(static value => (int)value).ToArray(),
            };

            for (var encoding = 0; encoding < expected.Length; encoding++)
            {
                var grant = CreateSourceGrant(encoding, expected[encoding], spelling);
                var actual = NativeJit.ExecuteWithSourceGrant(consumers[encoding], [grant],
                    (NativeRuntimePolicy)policy);
                Assert.True(actual == 0,
                    $"{exports[encoding]} failed for {context}: verification returned {actual}.");
            }
        }

        foreach (var fixture in fixtures)
        {
            VerifyFixture(fixture.Text, fixture.Spelling, fixture.Spelling);
        }

        const int firstSupplementaryScalar = 0x10000;
        const int supplementaryScalarLimit = 0x110000;
        const int maximumScalarsPerBatch = 2_700;
        for (var scalarStart = firstSupplementaryScalar;
            scalarStart < supplementaryScalarLimit;
            scalarStart += maximumScalarsPerBatch)
        {
            var scalarCount = Math.Min(maximumScalarsPerBatch,
                supplementaryScalarLimit - scalarStart);
            var textBuilder = new StringBuilder(scalarCount * 2);
            for (var index = 0; index < scalarCount; index++)
            {
                textBuilder.Append(new Rune(scalarStart + index).ToString());
            }

            var text = textBuilder.ToString();
            var spelling = $"\"{text}\"";
            Assert.True(scalarCount * 6 + 4 <= 16_384);
            VerifyFixture(text, spelling, $"supplementary scalar batch starting at U+{scalarStart:X}");
        }

        const int maximumBmpScalarsPerBatch = 1_600;
        var bmpTextBuilder = new StringBuilder(maximumBmpScalarsPerBatch);
        var bmpSpellingBuilder = new StringBuilder(maximumBmpScalarsPerBatch * 6);
        var bmpScalarCount = 0;
        var bmpBatchStart = 0;
        void VerifyBmpBatch(int scalarStart)
        {
            var text = bmpTextBuilder.ToString();
            var spelling = $"\"{bmpSpellingBuilder}\"";
            Assert.True(bmpScalarCount * 10 + 4 <= 16_384);
            VerifyFixture(text, spelling, $"BMP scalar batch starting at U+{scalarStart:X4}");
            bmpTextBuilder.Clear();
            bmpSpellingBuilder.Clear();
            bmpScalarCount = 0;
        }

        for (var codeUnit = (int)char.MinValue; codeUnit <= char.MaxValue; codeUnit++)
        {
            if (char.IsSurrogate((char)codeUnit))
            {
                continue;
            }

            if (bmpScalarCount == 0)
            {
                bmpBatchStart = codeUnit;
            }
            bmpTextBuilder.Append((char)codeUnit);
            bmpSpellingBuilder.Append("\\u").Append(codeUnit.ToString("X4", CultureInfo.InvariantCulture));
            bmpScalarCount++;
            if (bmpScalarCount == maximumBmpScalarsPerBatch
                || codeUnit == 0x7F || codeUnit == 0xFF)
            {
                VerifyBmpBatch(bmpBatchStart);
            }
        }
        if (bmpScalarCount != 0)
        {
            VerifyBmpBatch(bmpBatchStart);
        }

        var maximumAsciiSpelling = "\"" + new string('a', ScannerHost.MaximumSourceLength - 2) + "\"";
        Assert.Equal(ScannerHost.MaximumSourceLength, maximumAsciiSpelling.Length);
        var maximumAsciiGrant = new HostBuffer("Source", PrimitiveType.Char16,
            [.. maximumAsciiSpelling.Select(static unit => (int)unit)], false) {
            IsInvocationScoped = true,
        };
        Assert.Equal(ScannerHost.MaximumSourceLength - 2,
            NativeJit.ExecuteWithSourceGrant(consumers[^2], [maximumAsciiGrant], (NativeRuntimePolicy)policy));

        var maximumUtf8Spelling = "\"" + new string('\u0800', ScannerHost.MaximumSourceLength - 2) + "\"";
        Assert.Equal(ScannerHost.MaximumSourceLength, maximumUtf8Spelling.Length);
        var maximumUtf8Grant = new HostBuffer("Source", PrimitiveType.Char16,
            [.. maximumUtf8Spelling.Select(static unit => (int)unit)], false) {
            IsInvocationScoped = true,
        };
        Assert.Equal(49_146,
            NativeJit.ExecuteWithSourceGrant(consumers[^1], [maximumUtf8Grant], (NativeRuntimePolicy)policy));
    }

    private static int[]? TryEncode(Encoding encoding, string value)
    {
        try
        {
            return encoding.GetBytes(value).Select(static item => (int)item).ToArray();
        }
        catch (EncoderFallbackException)
        {
            return null;
        }
    }

    private static HostBuffer CreateSourceGrant(int encoding, int[]? expected, string spelling)
    {
        Assert.True(expected is null || expected.Length < ushort.MaxValue);
        var values = ImmutableArray.CreateBuilder<int>();
        values.Add(encoding);
        values.Add(expected?.Length ?? ushort.MaxValue);
        if (expected is not null)
        {
            values.AddRange(expected);
        }
        values.AddRange(spelling.Select(static unit => (int)unit));
        Assert.True(values.Count <= 16_384);
        return new HostBuffer("Source", PrimitiveType.Char16, values.ToImmutable(), false) {
            IsInvocationScoped = true,
        };
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record LiteralFixture(string Text, string Spelling);
}
