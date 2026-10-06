using System;
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
                if (Host.Source.Length < 2) { return -1; }
                if (!SourceRange.TryCreate(1, Host.Source.Length - 1, init var spelling)) { return -2; }
                if (!StringLiteral.TryCreate(spelling, init var literal)) { return -3; }
                int32 position = spelling.Start + int32(Host.Source[0]);
                if (!literal.TryReadScalar(position, init var scalar, init var next)) { return -4; }
                return scalar;
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

        var requestedConsumer = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return ReadRequestedStringScalar(); }
            """, "string-literal-position-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.False(requestedConsumer.HasErrors,
            string.Join(Environment.NewLine, requestedConsumer.Diagnostics));
        var requestedProgram = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(requestedConsumer.Program), producer);
        var requestedLinked = ModuleCompiler.Link(requestedProgram, producer);

        foreach (var (text, position, expected) in new (string Text, int Position, int Expected)[] {
            ("\"A\\u00E9B\"", 1, 65), ("\"A\\u00E9B\"", 2, 233),
            ("\"A\\u00E9B\"", 3, -4), ("\"A\\u00E9B\"", 7, -4),
            ("\"A\\u00E9B\"", 8, 66), ("\"A\\u00E9B\"", 9, -4),
            ("\"😀\"", 1, 0x1F600), ("\"😀\"", 2, -4), ("\"😀\"", 3, -4),
            ("\"\\U0001F600\"", 1, 0x1F600), ("\"\\U0001F600\"", 2, -4),
            ("\"\\U0001F600\"", 8, -4),
        })
        {
            var grant = new HostBuffer("Source", PrimitiveType.Char16,
                [position, .. text.Select(static unit => (int)unit)], false) {
                IsInvocationScoped = true,
            };
            Assert.Equal(expected, NativeJit.ExecuteWithSourceGrant(
                requestedLinked, [grant], (NativeRuntimePolicy)policy));
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
