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

public sealed class CharacterLiteralModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CharacterLiteralModuleUsesFreshSourceGrantsAsync(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "SourceRange", "CharacterFacts", "Utf16CodeUnits", "Utf16Scalar", "CharacterLiteral" };
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
            public int32 DecodeWholeSource() {
                if (!SourceRange.TryCreate(0, Host.Source.Length, init var spelling)) { return int32.MinValue; }
                if (!CharacterLiteralDecodeResult.TryAnalyze(spelling, init var decoded)) { return int32.MinValue; }
                if (decoded.ErrorCode == 0) { return decoded.Scalar; }
                return 0 - decoded.ErrorCode * 1_000_000
                    + decoded.ErrorSpan.Start * 128 + decoded.ErrorSpan.Length;
            }
            public int32 VerifyCharacterLiteralRecords() {
                int32 sourceLength = Host.Source.Length;
                mut int32 at = 0;
                mut int32 item = 0;
                while (at < sourceLength) {
                    if (sourceLength - at < 6) { return -1; }
                    int32 expectedError = int32(Host.Source[at]);
                    int32 expectedScalar = int32(Host.Source[at + 1])
                        | (int32(Host.Source[at + 2]) << 16);
                    int32 expectedErrorStart = int32(Host.Source[at + 3]);
                    int32 expectedErrorLength = int32(Host.Source[at + 4]);
                    int32 spellingLength = int32(Host.Source[at + 5]);
                    at = at + 6;
                    if (spellingLength < 2 || spellingLength > sourceLength - at) { return -2; }
                    if (!SourceRange.TryCreate(at, spellingLength, init var spelling)
                        || !CharacterLiteralDecodeResult.TryAnalyze(spelling, init var decoded)) {
                        return -3;
                    }
                    int32 absoluteErrorStart = expectedError == 0 ? 0 : at + expectedErrorStart;
                    if (decoded.ErrorCode != expectedError || decoded.Scalar != expectedScalar
                        || decoded.ErrorSpan.Start != absoluteErrorStart
                        || decoded.ErrorSpan.Length != expectedErrorLength) {
                        return item + 1;
                    }
                    at = at + spellingLength;
                    item = item + 1;
                }
                return 0;
            }
            """, "character-literal-module.terrum");
        var producerCompilation = Compilation.Create(trees,
        [
            new HostBuffer("Source", PrimitiveType.Char16, [39, 120, 39], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.DecodeWholeSource", "Terrum.CodeAnalysis.VerifyCharacterLiteralRecords"]);

        var consumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return DecodeWholeSource(); }
            """, "character-literal-consumer.terrum");
        var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        var recordConsumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyCharacterLiteralRecords(); }
            """, "character-literal-record-consumer.terrum");
        var recordConsumerCompilation = Compilation.Create(
            recordConsumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.False(recordConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, recordConsumerCompilation.Diagnostics));
        var recordConsumer = ModuleCompiler.Link(
            ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(recordConsumerCompilation.Program), producer),
            producer);

        foreach (var (text, scalar) in new (string Text, int? Scalar)[] {
            ("'A'", 65), ("'é'", 233), ("'\\u00E9'", 233), ("'\\U0010FFFF'", 0x10FFFF),
            ("'\\n'", 10), ("'\\r'", 13), ("'\\t'", 9), ("'\\\\'", 92),
            ("'\\\"'", 34), ("'\\''", 39), ("'\\0'", 0), ("'\\u0000'", 0),
            ("'\\uFFFF'", 0xFFFF), ("'\\U00000000'", 0), ("'\\U0000FFFF'", 0xFFFF),
            ("'\\U00010000'", 0x10000), ("'\\U0010FFFF'", 0x10FFFF), ("'￿'", 0xFFFF),
            ("''", null), ("'\\q'", null), ("'\\x41'", null),
            ("'\\uD800'", null), ("'\\uDFFF'", null), ("'\\U00110000'", null),
            ("'\\u00G0'", null), ("'\\uFFF'", null), ("'\\u00000'", null),
            ("'\\U0000000G'", null), ("'ab'", null), ("'😀A'", null),
            ("'á'", null),
        })
        {
            var lexed = Lexer.Lex(SourceText.From(text));
            var expected = scalar ?? ExpectedInvalidResult(Assert.Single(lexed.Diagnostics));
            if (scalar.HasValue)
            {
                Assert.Empty(lexed.Diagnostics);
            }
            var grant = new HostBuffer("Source", PrimitiveType.Char16,
                [.. text.Select(static unit => (int)unit)], false) {
                IsInvocationScoped = true,
            };
            Assert.Equal(expected, NativeJit.ExecuteWithSourceGrant(linked, [grant], (NativeRuntimePolicy)policy));
        }

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
            var spelling = codeUnit switch {
                '\'' => "'\\''",
                '\\' => "'\\\\'",
                _ => new string(['\'', codeUnit, '\'']),
            };
            var lexed = Lexer.Lex(SourceText.From(spelling));
            var errorCode = 0;
            var errorStart = 0;
            var errorLength = 0;
            if (!lexed.Diagnostics.IsEmpty)
            {
                if (lexed.Diagnostics.All(static diagnostic => diagnostic.Code == "TRM0002"))
                {
                    continue;
                }
                var diagnostic = Assert.Single(lexed.Diagnostics);
                Assert.Equal("TRM0001", diagnostic.Code);
                errorCode = 1;
                errorStart = diagnostic.Span.Start;
                errorLength = diagnostic.Span.Length;
            }
            Assert.Equal(TokenKind.CharacterLiteral, lexed.Tokens[0].Kind);
            Assert.Equal(spelling, lexed.Tokens[0].Text);
            Assert.Equal(TokenKind.EndOfFile, lexed.Tokens[1].Kind);
            var recordLength = 6 + spelling.Length;
            if (records.Count + recordLength > maximumGrantLength)
            {
                ExecuteRecords();
            }

            records.Add(errorCode);
            var expectedScalar = errorCode == 0 ? value : 0;
            records.Add(expectedScalar & 0xFFFF);
            records.Add(expectedScalar >> 16);
            records.Add(errorStart);
            records.Add(errorLength);
            records.Add(spelling.Length);
            records.AddRange(spelling.Select(static unit => (int)unit));
        }

        const int firstSupplementaryScalar = 0x10000;
        const int supplementaryScalarLimit = 0x110000;
        const int maximumScalarsPerBatch = 1_600;
        ExecuteRecords();
        for (var scalarStart = firstSupplementaryScalar;
            scalarStart < supplementaryScalarLimit;
            scalarStart += maximumScalarsPerBatch)
        {
            var scalarCount = Math.Min(maximumScalarsPerBatch,
                supplementaryScalarLimit - scalarStart);
            var batchText = new StringBuilder(scalarCount * 5);
            for (var index = 0; index < scalarCount; index++)
            {
                var spelling = $"'{char.ConvertFromUtf32(scalarStart + index)}'";
                batchText.Append(spelling).Append(' ');
                records.Add(0);
                records.Add((scalarStart + index) & 0xFFFF);
                records.Add((scalarStart + index) >> 16);
                records.Add(0);
                records.Add(0);
                records.Add(spelling.Length);
                records.AddRange(spelling.Select(static unit => (int)unit));
            }

            var lexed = Lexer.Lex(SourceText.From(batchText.ToString()));
            Assert.Empty(lexed.Diagnostics);
            Assert.Equal(scalarCount + 1, lexed.Tokens.Length);
            for (var index = 0; index < scalarCount; index++)
            {
                Assert.Equal(TokenKind.CharacterLiteral, lexed.Tokens[index].Kind);
                Assert.Equal($"'{char.ConvertFromUtf32(scalarStart + index)}'",
                    lexed.Tokens[index].Text);
            }
            Assert.Equal(TokenKind.EndOfFile, lexed.Tokens[^1].Kind);
            ExecuteRecords();
        }
        ExecuteRecords();

        const int maximumEscapedScalarsPerBatch = 900;
        for (var scalarStart = firstSupplementaryScalar;
            scalarStart < supplementaryScalarLimit;
            scalarStart += maximumEscapedScalarsPerBatch)
        {
            var scalarCount = Math.Min(maximumEscapedScalarsPerBatch,
                supplementaryScalarLimit - scalarStart);
            var batchText = new StringBuilder(scalarCount * 13);
            for (var index = 0; index < scalarCount; index++)
            {
                var scalar = scalarStart + index;
                var spelling = $"'\\U{scalar:X8}'";
                batchText.Append(spelling).Append(' ');
                records.Add(0);
                records.Add(scalar & 0xFFFF);
                records.Add(scalar >> 16);
                records.Add(0);
                records.Add(0);
                records.Add(spelling.Length);
                records.AddRange(spelling.Select(static unit => (int)unit));
            }

            var lexed = Lexer.Lex(SourceText.From(batchText.ToString()));
            Assert.Empty(lexed.Diagnostics);
            Assert.Equal(scalarCount + 1, lexed.Tokens.Length);
            for (var index = 0; index < scalarCount; index++)
            {
                Assert.Equal(TokenKind.CharacterLiteral, lexed.Tokens[index].Kind);
                Assert.Equal($"'\\U{scalarStart + index:X8}'",
                    lexed.Tokens[index].Text);
            }
            Assert.Equal(TokenKind.EndOfFile, lexed.Tokens[^1].Kind);
            ExecuteRecords();
        }
        ExecuteRecords();
    }

    private static int ExpectedInvalidResult(Diagnostic diagnostic)
    {
        var errorCode = diagnostic.Code switch {
            "TRM0001" => 1,
            "TRM0005" => 5,
            _ => throw new InvalidOperationException($"Unexpected character-literal diagnostic {diagnostic.Code}."),
        };
        return 0 - errorCode * 1_000_000 + diagnostic.Span.Start * 128 + diagnostic.Span.Length;
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
