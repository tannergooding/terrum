using System;
using System.IO;
using System.Linq;
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
        var componentNames = new[] { "SourceRange", "Utf16CodeUnits", "Utf16Scalar", "CharacterLiteral" };
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
            ["Terrum.CodeAnalysis.DecodeWholeSource"]);

        var consumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return DecodeWholeSource(); }
            """, "character-literal-consumer.terrum");
        var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        foreach (var (text, scalar) in new (string Text, int? Scalar)[] {
            ("'A'", 65), ("'é'", 233), ("'\\u00E9'", 233), ("'\\U0010FFFF'", 0x10FFFF),
            ("'\\n'", 10), ("''", null), ("'\\q'", null), ("'\\uD800'", null),
            ("'😀'", 0x1F600), ("'á'", null),
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
