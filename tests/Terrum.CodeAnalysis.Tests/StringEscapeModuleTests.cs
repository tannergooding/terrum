using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StringEscapeModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StringEscapeModuleAnalyzesFreshSourceGrants(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] {
            "SourceRange", "CharacterFacts", "Utf16CodeUnits", "Utf16Scalar", "StringEscape",
            "HexDigitValue", "StringEscapePacked",
        };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, path);
            Assert.False(trees[index].HasErrors, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        trees[^1] = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            public int32 AnalyzeFirstEscape() {
                if (!SourceRange.TryCreate(0, Host.Source.Length, init var spelling)) { return -2; }
                if (!StringEscapeDecodeResult.TryAnalyze(spelling, init var decoded)) { return -1; }
                return decoded.ErrorCode * 100_000_000 + (decoded.Scalar + 1) * 64
                    + decoded.ConsumedLength * 4 + decoded.ErrorLength;
            }
            public int32 VerifyEscapeRecords() {
                int32 sourceLength = Host.Source.Length;
                mut int32 at = 0;
                mut int32 item = 0;
                while (at < sourceLength) {
                    if (sourceLength - at < 7) { return -1; }
                    int32 expectedError = int32(Host.Source[at]);
                    int32 expectedScalar = int32(Host.Source[at + 1])
                        | (int32(Host.Source[at + 2]) << 16);
                    bool expectedUnpaired = Host.Source[at + 3] != char16(0);
                    int32 expectedConsumedLength = int32(Host.Source[at + 4]);
                    int32 expectedErrorLength = int32(Host.Source[at + 5]);
                    int32 spellingLength = int32(Host.Source[at + 6]);
                    at = at + 7;
                    if (spellingLength < 1 || spellingLength > sourceLength - at) { return -2; }
                    if (!SourceRange.TryCreate(at, spellingLength, init var spelling)
                        || !StringEscapeDecodeResult.TryAnalyze(spelling, init var decoded)) {
                        return -3;
                    }
                    bool scalarMismatch = expectedUnpaired
                        ? decoded.Scalar != -1
                        : decoded.Scalar != expectedScalar;
                    mut uint32 errorBit = 0;
                    if (decoded.ErrorCode == 5) { errorBit = 1; }
                    uint32 expectedPacked = (uint32(decoded.Scalar + 1) << 5)
                        | (uint32(decoded.ConsumedLength) << 1) | errorBit;
                    if (decoded.ErrorCode != expectedError || scalarMismatch
                        || decoded.ConsumedLength != expectedConsumedLength
                        || decoded.ErrorLength != expectedErrorLength
                        || AnalyzeStringEscapePacked(at, spellingLength) != expectedPacked) {
                        return item + 1;
                    }
                    at = at + spellingLength;
                    item = item + 1;
                }
                return 0;
            }
            """, "string-escape-module.terrum");
        var producerCompilation = Compilation.Create(trees,
        [
            new HostBuffer("Source", PrimitiveType.Char16, [92, 110], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.AnalyzeFirstEscape", "Terrum.CodeAnalysis.VerifyEscapeRecords"]);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return AnalyzeFirstEscape(); }
            """, "string-escape-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        var recordConsumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyEscapeRecords(); }
            """, "string-escape-record-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.False(recordConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, recordConsumerCompilation.Diagnostics));
        var recordConsumer = ModuleCompiler.Link(
            ModuleCompiler.CreateConsumer(
                Assert.IsType<BoundProgram>(recordConsumerCompilation.Program), producer),
            producer);

        foreach (var (escape, errorCode, scalar) in new (string Escape, int ErrorCode, int Scalar)[] {
            ("", -1, 0), ("plain", -1, 0), ("\\n", 0, 10), ("\\r", 0, 13), ("\\t", 0, 9),
            ("\\\\", 0, 92), ("\\\"", 0, 34), ("\\0", 0, 0), ("\\u00E9", 0, 233),
            ("\\u0000", 0, 0), ("\\uFFFF", 0, 0xFFFF), ("\\U0001F600", 0, 0x1F600),
            ("\\U0010FFFF", 0, 0x10FFFF), ("\\q", 5, 0), ("\\u", 5, 0), ("\\u123", 5, 0),
            ("\\'", 5, 0), ("\\u12xz", 5, 0), ("\\uD800", 5, 0), ("\\uDC00", 5, 0),
            ("\\U00110000", 5, 0), ("\\U1234567", 5, 0), ("\\U0000000G", 5, 0),
            ("\\\uD800", 5, -1), ("\\\uD800\uDC00", 5, 0),
        })
        {
            var expected = ExpectedResult(escape, errorCode, scalar);
            var grant = new HostBuffer("Source", PrimitiveType.Char16,
                [.. escape.Select(static unit => (int)unit)], false) {
                IsInvocationScoped = true,
            };
            Assert.Equal(expected, NativeJit.ExecuteWithSourceGrant(linked, [grant], (NativeRuntimePolicy)policy));
        }

        const string escapeWithTrailingText = "\\u00E9suffix";
        var trailingTextGrant = new HostBuffer("Source", PrimitiveType.Char16,
            [.. escapeWithTrailingText.Select(static unit => (int)unit)], false) {
            IsInvocationScoped = true,
        };
        Assert.Equal(ExpectedResult(escapeWithTrailingText, 0, 233, expectedConsumedLength: 6),
            NativeJit.ExecuteWithSourceGrant(linked, [trailingTextGrant], (NativeRuntimePolicy)policy));

        const int maximumGrantLength = 16_384;
        var records = new List<int>(maximumGrantLength);
        void ExecuteRecords()
        {
            if (records.Count == 0)
            {
                return;
            }
            var grant = new HostBuffer("Source", PrimitiveType.Char16,
                [.. records], false) { IsInvocationScoped = true };
            Assert.Equal(0, NativeJit.ExecuteWithSourceGrant(
                recordConsumer, [grant], (NativeRuntimePolicy)policy));
            records.Clear();
        }

        var emptyRecords = new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        };
        Assert.Equal(0, NativeJit.ExecuteWithSourceGrant(
            recordConsumer, [emptyRecords], (NativeRuntimePolicy)policy));

        for (var value = (int)char.MinValue; value <= char.MaxValue; value++)
        {
            var codeUnit = (char)value;
            var escape = codeUnit switch {
                'u' => "\\u0041Z",
                'U' => "\\U00000041Z",
                _ => string.Concat('\\', codeUnit, 'Z'),
            };
            var lexed = Lexer.Lex(SourceText.From("\"" + escape + "\""));
            if (lexed.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM0002"))
            {
                continue;
            }

            var errorCode = 0;
            var scalar = codeUnit switch {
                'n' => 10,
                'r' => 13,
                't' => 9,
                '\\' => 92,
                '"' => 34,
                '0' => 0,
                'u' or 'U' => 65,
                _ => 0,
            };
            var consumedLength = codeUnit is 'u' or 'U' ? escape.Length - 1 : 2;
            var errorLength = 0;
            if (!lexed.Diagnostics.IsEmpty)
            {
                var diagnostic = Assert.Single(
                    lexed.Diagnostics.Where(static item => item.Code == "TRM0005"));
                Assert.Equal(1, diagnostic.Span.Start);
                errorCode = 5;
                scalar = char.IsSurrogate(codeUnit) ? -1 : 0;
                consumedLength = diagnostic.Span.Length;
                errorLength = diagnostic.Span.Length;
            }

            var recordLength = 7 + escape.Length;
            if (records.Count + recordLength > maximumGrantLength)
            {
                ExecuteRecords();
            }

            records.Add(errorCode);
            records.Add(scalar < 0 ? 0 : scalar & 0xFFFF);
            records.Add(scalar < 0 ? 0 : scalar >> 16);
            records.Add(scalar < 0 ? 1 : 0);
            records.Add(consumedLength);
            records.Add(errorLength);
            records.Add(escape.Length);
            records.AddRange(escape.Select(static unit => (int)unit));
        }
        ExecuteRecords();

        const int maximumBmpEscapesPerBatch = 1_000;
        for (var scalarStart = (int)char.MinValue;
            scalarStart <= char.MaxValue;
            scalarStart += maximumBmpEscapesPerBatch)
        {
            var scalarCount = Math.Min(maximumBmpEscapesPerBatch,
                char.MaxValue + 1 - scalarStart);
            var batchText = new StringBuilder(scalarCount * 9);
            var invalidScalarCount = 0;
            for (var index = 0; index < scalarCount; index++)
            {
                var scalar = scalarStart + index;
                var escape = $"\\u{scalar:X4}";
                batchText.Append('"').Append(escape).Append("\" ");
                var valid = !char.IsSurrogate((char)scalar);
                if (!valid)
                {
                    invalidScalarCount++;
                }
                records.Add(valid ? 0 : 5);
                records.Add(valid ? scalar & 0xFFFF : 0);
                records.Add(valid ? scalar >> 16 : 0);
                records.Add(0);
                records.Add(escape.Length);
                records.Add(valid ? 0 : escape.Length);
                records.Add(escape.Length);
                records.AddRange(escape.Select(static unit => (int)unit));
            }

            var lexed = Lexer.Lex(SourceText.From(batchText.ToString()));
            Assert.Equal(invalidScalarCount, lexed.Diagnostics.Length);
            Assert.Equal(scalarCount + 1, lexed.Tokens.Length);
            var diagnosticIndex = 0;
            for (var index = 0; index < scalarCount; index++)
            {
                var scalar = scalarStart + index;
                var spelling = $"\"\\u{scalar:X4}\"";
                Assert.Equal(TokenKind.StringLiteral, lexed.Tokens[index].Kind);
                Assert.Equal(spelling, lexed.Tokens[index].Text);
                if (char.IsSurrogate((char)scalar))
                {
                    Assert.Equal("TRM0005", lexed.Diagnostics[diagnosticIndex].Code);
                    Assert.Equal(index * 9 + 1, lexed.Diagnostics[diagnosticIndex].Span.Start);
                    Assert.Equal(6, lexed.Diagnostics[diagnosticIndex].Span.Length);
                    diagnosticIndex++;
                }
            }
            Assert.Equal(TokenKind.EndOfFile, lexed.Tokens[^1].Kind);
            ExecuteRecords();
        }

        const int firstSupplementaryScalar = 0x10000;
        const int supplementaryScalarLimit = 0x110000;
        const int maximumScalarsPerBatch = 960;
        for (var scalarStart = firstSupplementaryScalar;
            scalarStart < supplementaryScalarLimit;
            scalarStart += maximumScalarsPerBatch)
        {
            var scalarCount = Math.Min(maximumScalarsPerBatch,
                supplementaryScalarLimit - scalarStart);
            var batchText = new StringBuilder(scalarCount * 14);
            for (var index = 0; index < scalarCount; index++)
            {
                var scalar = scalarStart + index;
                var escape = $"\\U{scalar:X8}";
                batchText.Append('"').Append(escape).Append("\" ");
                records.Add(0);
                records.Add(scalar & 0xFFFF);
                records.Add(scalar >> 16);
                records.Add(0);
                records.Add(escape.Length);
                records.Add(0);
                records.Add(escape.Length);
                records.AddRange(escape.Select(static unit => (int)unit));
            }

            var lexed = Lexer.Lex(SourceText.From(batchText.ToString()));
            Assert.Empty(lexed.Diagnostics);
            Assert.Equal(scalarCount + 1, lexed.Tokens.Length);
            for (var index = 0; index < scalarCount; index++)
            {
                Assert.Equal(TokenKind.StringLiteral, lexed.Tokens[index].Kind);
                Assert.Equal($"\"\\U{scalarStart + index:X8}\"",
                    lexed.Tokens[index].Text);
            }
            Assert.Equal(TokenKind.EndOfFile, lexed.Tokens[^1].Kind);
            ExecuteRecords();
        }

        const int firstSurrogateScalar = 0xD800;
        const int surrogateScalarLimit = 0xE000;
        const int maximumInvalidScalarsPerBatch = 900;
        for (var scalarStart = firstSurrogateScalar;
            scalarStart < surrogateScalarLimit;
            scalarStart += maximumInvalidScalarsPerBatch)
        {
            var scalarCount = Math.Min(maximumInvalidScalarsPerBatch,
                surrogateScalarLimit - scalarStart);
            var batchText = new StringBuilder(scalarCount * 13);
            for (var index = 0; index < scalarCount; index++)
            {
                var escape = $"\\U{scalarStart + index:X8}";
                batchText.Append('"').Append(escape).Append("\" ");
                records.Add(5);
                records.Add(0);
                records.Add(0);
                records.Add(0);
                records.Add(escape.Length);
                records.Add(escape.Length);
                records.Add(escape.Length);
                records.AddRange(escape.Select(static unit => (int)unit));
            }

            var lexed = Lexer.Lex(SourceText.From(batchText.ToString()));
            Assert.Equal(scalarCount, lexed.Diagnostics.Length);
            Assert.Equal(scalarCount + 1, lexed.Tokens.Length);
            for (var index = 0; index < scalarCount; index++)
            {
                Assert.Equal("TRM0005", lexed.Diagnostics[index].Code);
                Assert.Equal(index * 13 + 1, lexed.Diagnostics[index].Span.Start);
                Assert.Equal(10, lexed.Diagnostics[index].Span.Length);
                Assert.Equal(TokenKind.StringLiteral, lexed.Tokens[index].Kind);
                Assert.Equal($"\"\\U{scalarStart + index:X8}\"", lexed.Tokens[index].Text);
            }
            Assert.Equal(TokenKind.EndOfFile, lexed.Tokens[^1].Kind);
            ExecuteRecords();
        }
    }

    private static int ExpectedResult(string escape, int errorCode, int scalar,
        int? expectedConsumedLength = null)
    {
        if (escape.Length == 0 || escape[0] != '\\')
        {
            return -1;
        }

        var lexed = Lexer.Lex(SourceText.From("\"" + escape + "\""));
        if (errorCode == 0)
        {
            Assert.Empty(lexed.Diagnostics);
            return PackResult(0, scalar, expectedConsumedLength ?? escape.Length, 0);
        }

        var diagnostic = Assert.Single(lexed.Diagnostics.Where(static item => item.Code == "TRM0005"));
        Assert.Equal("TRM0005", diagnostic.Code);
        Assert.Equal(1, diagnostic.Span.Start);
        return PackResult(errorCode, scalar, diagnostic.Span.Length, diagnostic.Span.Length);
    }

    // Escape widths fit in six low bits; the scalar payload stays below each 100-million error-code band.
    private static int PackResult(int errorCode, int scalar, int consumedLength, int errorLength) =>
        errorCode * 100_000_000 + (scalar + 1) * 64 + consumedLength * 4 + errorLength;

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
