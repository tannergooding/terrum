using System;
using System.Collections.Generic;
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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StringLiteralModuleTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StringLiteralModuleReadsFreshSourceGrants(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] {
            "SourceRange", "CharacterFacts", "HexDigitValue", "Utf16CodeUnits", "Utf16Scalar",
            "StringEscape", "StringEscapePacked", "StringLiteral", "StringLiteralScalarCount",
            "StringLiteralScalarAt",
        };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = SelfHostPaths.ResolveFile(root, componentNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, path);
            Assert.That(trees[index].HasErrors, Is.False, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        trees[^1] = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            public int32 ReadFirstStringScalar() {
                if (!SourceRange.TryCreate(0, Host.Source.Length, init var spelling)) { return -1; }
                if (!StringLiteral.TryCreate(spelling, init var literal)) { return -2; }
                if (literal.ScalarCount == 0) { return 0; }
                if (!literal.TryReadScalar(spelling.Start + 1, init var scalar, init var next)) { return -3; }
                return scalar;
            }
            public int32 ReadRequestedStringScalar() {
                if (Host.Source.Length < 3) { return -1; }
                if (!SourceRange.TryCreate(2, Host.Source.Length - 2, init var spelling)) { return -2; }
                if (!StringLiteral.TryCreate(spelling, init var literal)) { return -3; }
                int32 position = spelling.Start + int32(Host.Source[0]);
                if (!literal.TryReadScalar(position, init var scalar, init var next)) { return -4; }
                if (next != spelling.Start + int32(Host.Source[1])) { return -5; }
                return scalar;
            }
            public int32 VerifyLiteralScalarRecords() {
                int32 sourceLength = Host.Source.Length;
                mut int32 at = 0;
                mut int32 item = 0;
                while (at < sourceLength) {
                    if (sourceLength - at < 4) { return -1; }
                    bool expectedValid = Host.Source[at] != char16(0);
                    int32 expectedScalar = int32(Host.Source[at + 1])
                        | (int32(Host.Source[at + 2]) << 16);
                    int32 spellingLength = int32(Host.Source[at + 3]);
                    at = at + 4;
                    if (spellingLength < 2 || spellingLength > sourceLength - at) { return -2; }
                    if (!SourceRange.TryCreate(at, spellingLength, init var spelling)) { return -3; }
                    if (StringLiteral.TryCreate(spelling, init var literal)) {
                        if (!expectedValid || literal.ScalarCount != 1
                            || !literal.TryReadScalar(at + 1, init var scalar, init var next)
                            || scalar != expectedScalar || next != at + spellingLength - 1
                            || GetStringLiteralScalarOrMinusOne(at, spellingLength, 0) != expectedScalar) {
                            return item + 1;
                        }
                        mut StringLiteralCursor cursor = StringLiteralCursor.Create(literal);
                        if (cursor.Position != at + 1
                            || !cursor.TryRead(init var cursorScalar, init var following)
                            || cursorScalar != expectedScalar
                            || following.Position != at + spellingLength - 1
                            || following.TryRead(init var extraScalar, init var afterExtra)) {
                            return item + 1;
                        }
                        for (mut int32 position = at + 2;
                            position < at + spellingLength - 1;
                            position = position + 1) {
                            if (literal.TryReadScalar(position, init var interiorScalar, init var interiorNext)) {
                                return item + 1;
                            }
                        }
                    } else if (expectedValid) {
                        return item + 1;
                    } else if (GetStringLiteralScalarOrMinusOne(at, spellingLength, 0) != -1) {
                        return item + 1;
                    }
                    mut int32 expectedScalarCount = -1;
                    if (expectedValid) { expectedScalarCount = 1; }
                    if (GetStringLiteralScalarCountOrMinusOne(at, spellingLength) != expectedScalarCount) {
                        return item + 1;
                    }
                    at = at + spellingLength;
                    item = item + 1;
                }
                return 0;
            }
            public int32 VerifyLiteralCountRecords() {
                int32 sourceLength = Host.Source.Length;
                mut int32 at = 0;
                mut int32 item = 0;
                while (at < sourceLength) {
                    if (sourceLength - at < 1) { return -1; }
                    int32 spellingLength = int32(Host.Source[at]);
                    at = at + 1;
                    if (spellingLength > sourceLength - at) { return -2; }
                    if (!SourceRange.TryCreate(at, spellingLength, init var spelling)) { return -3; }
                    mut int32 expectedScalarCount = -1;
                    if (StringLiteral.TryCreate(spelling, init var literal)) {
                        expectedScalarCount = literal.ScalarCount;
                        if (GetStringLiteralScalarCountOrMinusOne(at, spellingLength) != expectedScalarCount) {
                            return item + 1;
                        }
                        mut int32 scalarIndex = -1;
                        while (scalarIndex <= expectedScalarCount) {
                            mut int32 expectedScalar = -1;
                            if (scalarIndex >= 0 && scalarIndex < expectedScalarCount) {
                                mut int32 position = at + 1;
                                mut int32 currentIndex = 0;
                                while (currentIndex <= scalarIndex) {
                                    if (!literal.TryReadScalar(position, init var value, init var next)) {
                                        return -4;
                                    }
                                    if (currentIndex == scalarIndex) { expectedScalar = value; }
                                    position = next;
                                    currentIndex = currentIndex + 1;
                                }
                            }
                            if (GetStringLiteralScalarOrMinusOne(at, spellingLength, scalarIndex)
                                != expectedScalar) {
                                return item + 1;
                            }
                            scalarIndex = scalarIndex + 1;
                        }
                        if (GetStringLiteralScalarOrMinusOne(at, spellingLength, int32.MaxValue) != -1) {
                            return item + 1;
                        }
                    } else if (GetStringLiteralScalarCountOrMinusOne(at, spellingLength) != -1
                        || GetStringLiteralScalarOrMinusOne(at, spellingLength, -1) != -1
                        || GetStringLiteralScalarOrMinusOne(at, spellingLength, 0) != -1) {
                        return item + 1;
                    }
                    at = at + spellingLength;
                    item = item + 1;
                }
                return 0;
            }
            """, "string-literal-module.terrum");
        var producerCompilation = Compilation.Create(trees,
        [
            new HostBuffer("Source", PrimitiveType.Char16, [34, 120, 34], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = producerCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            [
                "Terrum.CodeAnalysis.ReadFirstStringScalar",
                "Terrum.CodeAnalysis.ReadRequestedStringScalar",
                "Terrum.CodeAnalysis.VerifyLiteralScalarRecords",
                "Terrum.CodeAnalysis.VerifyLiteralCountRecords",
            ]);

        var consumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return ReadFirstStringScalar(); }
            """, "string-literal-consumer.terrum");
        var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        var recordConsumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyLiteralScalarRecords(); }
            """, "string-literal-record-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.That(recordConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, recordConsumerCompilation.Diagnostics));
        var recordConsumer = ModuleCompiler.Link(
            ModuleCompiler.CreateConsumer(
                ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = recordConsumerCompilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), producer),
            producer);
        var countRecordConsumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyLiteralCountRecords(); }
            """, "string-literal-count-record-consumer.terrum"),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(countRecordConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, countRecordConsumerCompilation.Diagnostics));
        var countRecordConsumer = ModuleCompiler.Link(
            ModuleCompiler.CreateConsumer(
                ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = countRecordConsumerCompilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), producer),
            producer);

        foreach (var (text, scalar) in new (string Text, int? Scalar)[] {
            ("\"\"", 0), ("\"A\"", 65), ("\"é\"", 233), ("\"\\u00E9\"", 233),
            ("\"\\U0001F600\"", 0x1F600), ("\"😀\"", 0x1F600), ("\"\\n\"", 10),
            ("\"\\q\"", null), ("\"\\uD800\"", null), ("\"\uD800\"", null),
        })
        {
            var lexed = Lexer.Lex(SourceText.From(text));
            var expected = scalar ?? -2;
            if (scalar.HasValue)
            {
                Assert.That(lexed.Diagnostics, Is.Empty);
            }
            else
            {
                Assert.That(lexed.Diagnostics, Is.Not.Empty);
            }
            var grant = new HostBuffer("Source", PrimitiveType.Char16,
                [.. text.Select(static unit => (int)unit)], false) {
                IsInvocationScoped = true,
            };
            Assert.That(NativeJit.ExecuteWithSourceGrant(linked, [grant], (NativeRuntimePolicy)policy), Is.EqualTo(expected));
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
            Assert.That(NativeJit.ExecuteWithSourceGrant(
                recordConsumer, [grant], (NativeRuntimePolicy)policy), Is.EqualTo(0));
            records.Clear();
        }

        var emptyRecords = new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        };
        Assert.That(NativeJit.ExecuteWithSourceGrant(
            recordConsumer, [emptyRecords], (NativeRuntimePolicy)policy), Is.EqualTo(0));

        for (var value = (int)char.MinValue; value <= char.MaxValue; value++)
        {
            var codeUnit = (char)value;
            var spelling = codeUnit switch {
                '"' => "\"\\\"\"",
                '\\' => "\"\\\\\"",
                _ => new string(['"', codeUnit, '"']),
            };
            var lexed = Lexer.Lex(SourceText.From(spelling));
            if (lexed.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM0002"))
            {
                continue;
            }

            var valid = !char.IsSurrogate(codeUnit);
            if (valid)
            {
                Assert.That(lexed.Diagnostics, Is.Empty);
            }
            else
            {
                Assert.That(global::System.Linq.Enumerable.Any(lexed.Diagnostics,static diagnostic => diagnostic.Code == "TRM0001"),Is.True);
            }
            var recordLength = 4 + spelling.Length;
            if (records.Count + recordLength > maximumGrantLength)
            {
                ExecuteRecords();
            }

            records.Add(valid ? 1 : 0);
            var expectedScalar = valid ? value : 0;
            records.Add(expectedScalar & 0xFFFF);
            records.Add(expectedScalar >> 16);
            records.Add(spelling.Length);
            records.AddRange(spelling.Select(static unit => (int)unit));
        }

        ExecuteRecords();
        var countRecords = new List<int>();
        foreach (var spelling in new[] {
            "\"\"",
            "\"A\"",
            "\"\\0\"",
            "\"a\\n\\U0001F600b\"",
            "\"A\\q\"",
            "\"A\\uD800\"",
            "\"\\uFFFF\"",
            new string(['"', 'A', '\n', '"']),
            new string(['"', '\\', '"', '"']),
            new string(['"', '"', '"']),
            new string(['"', '\n', '"']),
            new string(['"', '\r', '"']),
            new string(['"', '\u0085', '"']),
            new string(['"', '\u2028', '"']),
            new string(['"', '\u2029', '"']),
            "\"\\q\"",
            "\"\\u12\"",
            "\"\\uD800\"",
            new string(['"', '\uD800', '"']),
            new string(['"', '\uD83D', '\uDE00', '"']),
            "\"\\uD83D\\uDE00\"",
            new string(['"', '\u000B', '\u000C', '"']),
            new string(['"', '\\', '"']),
            "\"\\u0085\"",
        })
        {
            countRecords.Add(spelling.Length);
            countRecords.AddRange(spelling.Select(static unit => (int)unit));
        }
        var countRecordGrant = new HostBuffer("Source", PrimitiveType.Char16, [.. countRecords], false) {
            IsInvocationScoped = true,
        };
        Assert.That(NativeJit.ExecuteWithSourceGrant(
            countRecordConsumer, [countRecordGrant], (NativeRuntimePolicy)policy), Is.EqualTo(0));

        const int maximumEscapedBmpScalarsPerBatch = 1_000;
        for (var scalarStart = (int)char.MinValue;
            scalarStart <= char.MaxValue;
            scalarStart += maximumEscapedBmpScalarsPerBatch)
        {
            var scalarCount = Math.Min(maximumEscapedBmpScalarsPerBatch,
                char.MaxValue + 1 - scalarStart);
            var batchText = new StringBuilder(scalarCount * 9);
            var invalidScalarCount = 0;
            for (var index = 0; index < scalarCount; index++)
            {
                var scalar = scalarStart + index;
                var spelling = $"\"\\u{scalar:X4}\"";
                batchText.Append(spelling).Append(' ');
                var valid = !char.IsSurrogate((char)scalar);
                if (!valid)
                {
                    invalidScalarCount++;
                }
                records.Add(valid ? 1 : 0);
                records.Add(valid ? scalar & 0xFFFF : 0);
                records.Add(valid ? scalar >> 16 : 0);
                records.Add(spelling.Length);
                records.AddRange(spelling.Select(static unit => (int)unit));
            }

            var lexed = Lexer.Lex(SourceText.From(batchText.ToString()));
            Assert.That(lexed.Diagnostics.Length, Is.EqualTo(invalidScalarCount));
            Assert.That(lexed.Tokens.Length, Is.EqualTo(scalarCount + 1));
            var diagnosticIndex = 0;
            for (var index = 0; index < scalarCount; index++)
            {
                var scalar = scalarStart + index;
                var spelling = $"\"\\u{scalar:X4}\"";
                Assert.That(lexed.Tokens[index].Kind, Is.EqualTo(TokenKind.StringLiteral));
                Assert.That(lexed.Tokens[index].Text, Is.EqualTo(spelling));
                if (char.IsSurrogate((char)scalar))
                {
                    Assert.That(lexed.Diagnostics[diagnosticIndex].Code, Is.EqualTo("TRM0005"));
                    Assert.That(lexed.Diagnostics[diagnosticIndex].Span.Start, Is.EqualTo(index * 9 + 1));
                    Assert.That(lexed.Diagnostics[diagnosticIndex].Span.Length, Is.EqualTo(6));
                    diagnosticIndex++;
                }
            }
            Assert.That(lexed.Tokens[^1].Kind, Is.EqualTo(TokenKind.EndOfFile));
            ExecuteRecords();
        }

        const int firstSupplementaryScalar = 0x10000;
        const int supplementaryScalarLimit = 0x110000;
        const int maximumScalarsPerBatch = 2_000;
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
                var spelling = $"\"{char.ConvertFromUtf32(scalarStart + index)}\"";
                batchText.Append(spelling).Append(' ');
                records.Add(1);
                records.Add((scalarStart + index) & 0xFFFF);
                records.Add((scalarStart + index) >> 16);
                records.Add(spelling.Length);
                records.AddRange(spelling.Select(static unit => (int)unit));
            }

            var lexed = Lexer.Lex(SourceText.From(batchText.ToString()));
            Assert.That(lexed.Diagnostics, Is.Empty);
            Assert.That(lexed.Tokens.Length, Is.EqualTo(scalarCount + 1));
            for (var index = 0; index < scalarCount; index++)
            {
                Assert.That(lexed.Tokens[index].Kind, Is.EqualTo(TokenKind.StringLiteral));
                Assert.That(lexed.Tokens[index].Text, Is.EqualTo($"\"{char.ConvertFromUtf32(scalarStart + index)}\""));
            }
            Assert.That(lexed.Tokens[^1].Kind, Is.EqualTo(TokenKind.EndOfFile));
            ExecuteRecords();
        }
        ExecuteRecords();

        const int maximumEscapedScalarsPerBatch = 1_000;
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
                var spelling = $"\"\\U{scalar:X8}\"";
                batchText.Append(spelling).Append(' ');
                records.Add(1);
                records.Add(scalar & 0xFFFF);
                records.Add(scalar >> 16);
                records.Add(spelling.Length);
                records.AddRange(spelling.Select(static unit => (int)unit));
            }

            var lexed = Lexer.Lex(SourceText.From(batchText.ToString()));
            Assert.That(lexed.Diagnostics, Is.Empty);
            Assert.That(lexed.Tokens.Length, Is.EqualTo(scalarCount + 1));
            for (var index = 0; index < scalarCount; index++)
            {
                Assert.That(lexed.Tokens[index].Kind, Is.EqualTo(TokenKind.StringLiteral));
                Assert.That(lexed.Tokens[index].Text, Is.EqualTo($"\"\\U{scalarStart + index:X8}\""));
            }
            Assert.That(lexed.Tokens[^1].Kind, Is.EqualTo(TokenKind.EndOfFile));
            ExecuteRecords();
        }

        const int firstSurrogateScalar = 0xD800;
        const int surrogateScalarLimit = 0xE000;
        const int maximumInvalidScalarsPerBatch = 1_000;
        for (var scalarStart = firstSurrogateScalar;
            scalarStart < surrogateScalarLimit;
            scalarStart += maximumInvalidScalarsPerBatch)
        {
            var scalarCount = Math.Min(maximumInvalidScalarsPerBatch,
                surrogateScalarLimit - scalarStart);
            var batchText = new StringBuilder(scalarCount * 13);
            for (var index = 0; index < scalarCount; index++)
            {
                var spelling = $"\"\\U{scalarStart + index:X8}\"";
                batchText.Append(spelling).Append(' ');
                records.Add(0);
                records.Add(0);
                records.Add(0);
                records.Add(spelling.Length);
                records.AddRange(spelling.Select(static unit => (int)unit));
            }

            var lexed = Lexer.Lex(SourceText.From(batchText.ToString()));
            Assert.That(lexed.Diagnostics.Length, Is.EqualTo(scalarCount));
            Assert.That(lexed.Tokens.Length, Is.EqualTo(scalarCount + 1));
            for (var index = 0; index < scalarCount; index++)
            {
                Assert.That(lexed.Diagnostics[index].Code, Is.EqualTo("TRM0005"));
                Assert.That(lexed.Diagnostics[index].Span.Start, Is.EqualTo(index * 13 + 1));
                Assert.That(lexed.Diagnostics[index].Span.Length, Is.EqualTo(10));
                Assert.That(lexed.Tokens[index].Kind, Is.EqualTo(TokenKind.StringLiteral));
                Assert.That(lexed.Tokens[index].Text, Is.EqualTo($"\"\\U{scalarStart + index:X8}\""));
            }
            Assert.That(lexed.Tokens[^1].Kind, Is.EqualTo(TokenKind.EndOfFile));
            ExecuteRecords();
        }

        var requestedConsumer = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return ReadRequestedStringScalar(); }
            """, "string-literal-position-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.That(requestedConsumer.HasErrors, Is.False, string.Join(Environment.NewLine, requestedConsumer.Diagnostics));
        var requestedProgram = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = requestedConsumer.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(), producer);
        var requestedLinked = ModuleCompiler.Link(requestedProgram, producer);

        foreach (var (text, position, next, expected) in new (string Text, int Position, int Next, int Expected)[] {
            ("\"A\\u00E9B\"", 1, 2, 65), ("\"A\\u00E9B\"", 2, 8, 233),
            ("\"A\\u00E9B\"", 3, 0, -4), ("\"A\\u00E9B\"", 7, 0, -4),
            ("\"A\\u00E9B\"", 8, 9, 66), ("\"A\\u00E9B\"", 9, 0, -4),
            ("\"😀\"", 1, 3, 0x1F600), ("\"😀\"", 2, 0, -4), ("\"😀\"", 3, 0, -4),
            ("\"\\U0001F600\"", 1, 11, 0x1F600), ("\"\\U0001F600\"", 2, 0, -4),
            ("\"\\U0001F600\"", 8, 0, -4),
        })
        {
            var grant = new HostBuffer("Source", PrimitiveType.Char16,
                [position, next, .. text.Select(static unit => (int)unit)], false) {
                IsInvocationScoped = true,
            };
            Assert.That(NativeJit.ExecuteWithSourceGrant(
                requestedLinked, [grant], (NativeRuntimePolicy)policy), Is.EqualTo(expected));
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
