using System;
using System.Collections.Generic;
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

public sealed class StringLiteralModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StringLiteralModuleReadsFreshSourceGrants(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "SourceRange", "CharacterFacts", "Utf16CodeUnits", "Utf16Scalar", "StringEscape", "StringLiteral" };
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
            public int32 VerifyBmpLiteralRecords() {
                int32 sourceLength = Host.Source.Length;
                mut int32 at = 0;
                mut int32 item = 0;
                while (at < sourceLength) {
                    if (sourceLength - at < 3) { return -1; }
                    bool expectedValid = Host.Source[at] != char16(0);
                    int32 expectedScalar = int32(Host.Source[at + 1]);
                    int32 spellingLength = int32(Host.Source[at + 2]);
                    at = at + 3;
                    if (spellingLength < 2 || spellingLength > sourceLength - at) { return -2; }
                    if (!SourceRange.TryCreate(at, spellingLength, init var spelling)) { return -3; }
                    if (StringLiteral.TryCreate(spelling, init var literal)) {
                        if (!expectedValid || literal.ScalarCount != 1
                            || !literal.TryReadScalar(at + 1, init var scalar, init var next)
                            || scalar != expectedScalar || next != at + spellingLength - 1) {
                            return item + 1;
                        }
                    } else if (expectedValid) {
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
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            [
                "Terrum.CodeAnalysis.ReadFirstStringScalar",
                "Terrum.CodeAnalysis.ReadRequestedStringScalar",
                "Terrum.CodeAnalysis.VerifyBmpLiteralRecords",
            ]);

        var consumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return ReadFirstStringScalar(); }
            """, "string-literal-consumer.terrum");
        var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        var recordConsumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyBmpLiteralRecords(); }
            """, "string-literal-record-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.False(recordConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, recordConsumerCompilation.Diagnostics));
        var recordConsumer = ModuleCompiler.Link(
            ModuleCompiler.CreateConsumer(
                Assert.IsType<BoundProgram>(recordConsumerCompilation.Program), producer),
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
                Assert.Empty(lexed.Diagnostics);
            }
            else
            {
                Assert.NotEmpty(lexed.Diagnostics);
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
                Assert.Empty(lexed.Diagnostics);
            }
            else
            {
                Assert.Contains(lexed.Diagnostics, static diagnostic => diagnostic.Code == "TRM0001");
            }
            var recordLength = 3 + spelling.Length;
            if (records.Count + recordLength > maximumGrantLength)
            {
                ExecuteRecords();
            }

            records.Add(valid ? 1 : 0);
            records.Add(valid ? value : 0);
            records.Add(spelling.Length);
            records.AddRange(spelling.Select(static unit => (int)unit));
        }
        ExecuteRecords();

        var requestedConsumer = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return ReadRequestedStringScalar(); }
            """, "string-literal-position-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.False(requestedConsumer.HasErrors,
            string.Join(Environment.NewLine, requestedConsumer.Diagnostics));
        var requestedProgram = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(requestedConsumer.Program), producer);
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
            Assert.Equal(expected, NativeJit.ExecuteWithSourceGrant(
                requestedLinked, [grant], (NativeRuntimePolicy)policy));
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
