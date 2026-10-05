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
            ["Terrum.CodeAnalysis.ReadFirstStringScalar"]);

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
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
