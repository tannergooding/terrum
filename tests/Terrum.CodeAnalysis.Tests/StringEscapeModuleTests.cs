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

public sealed class StringEscapeModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StringEscapeModuleAnalyzesFreshSourceGrants(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "SourceRange", "CharacterFacts", "Utf16Scalar", "StringEscape" };
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
            ["Terrum.CodeAnalysis.AnalyzeFirstEscape"]);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return AnalyzeFirstEscape(); }
            """, "string-escape-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        foreach (var (escape, errorCode, scalar) in new (string Escape, int ErrorCode, int Scalar)[] {
            ("", -1, 0), ("plain", -1, 0), ("\\n", 0, 10), ("\\r", 0, 13), ("\\t", 0, 9),
            ("\\\\", 0, 92), ("\\\"", 0, 34), ("\\0", 0, 0), ("\\u00E9", 0, 233),
            ("\\U0010FFFF", 0, 0x10FFFF), ("\\q", 5, 0), ("\\u", 5, 0), ("\\u123", 5, 0),
            ("\\u12xz", 5, 0), ("\\uD800", 5, 0), ("\\U00110000", 5, 0), ("\\U1234567", 5, 0),
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
    }

    private static int ExpectedResult(string escape, int errorCode, int scalar)
    {
        if (escape.Length == 0 || escape[0] != '\\')
        {
            return -1;
        }

        var lexed = Lexer.Lex(SourceText.From("\"" + escape + "\""));
        if (errorCode == 0)
        {
            Assert.Empty(lexed.Diagnostics);
            return PackResult(0, scalar, escape.Length, 0);
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
