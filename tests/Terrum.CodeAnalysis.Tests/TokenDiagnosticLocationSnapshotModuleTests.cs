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

public sealed class TokenDiagnosticLocationSnapshotModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PersistedDiagnosticLocationsRebindFreshScannerGrantsAsync(BoundRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var componentNames = new[] {
            "SourceRange", "CharacterFacts", "Utf16CodeUnits", "Utf16Scalar", "CharacterLiteral",
            "StringEscape", "Token", "ManagedTokenChain", "Scanner", "ManagedScanner", "TokenSnapshot",
            "TokenTriviaSnapshot", "WideIntegerFacts", "IntegerParsing", "TokenIntegerSnapshot",
            "TokenDiagnosticSnapshot", "SourceText", "SourcePosition", "SourceLineIndex",
            "SourceDiagnosticLocation",
            "TokenDiagnosticLocationSnapshot",
        };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            trees[index] = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(trees[index].HasErrors,
                string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        trees[^1] = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            public int32 VerifyDiagnosticLocations() {
                int32 tokenCount = Scan();
                if (tokenCount < 1) { return -1; }
                if (!TokenDiagnosticLocationSnapshot.TryBuild(init var diagnostics)
                    || !TokenDiagnosticSnapshot.TryBuild(init var expected)) { return -2; }
                if (diagnostics.GetCount() != expected.GetCount()) { return -3; }
                mut int32 index = 0;
                while (index < expected.GetCount()) {
                    if (!expected.TryGet(index, init var expectedCode, init var expectedSpan)
                        || !diagnostics.TryGet(index, init var code, init var span,
                            init var line, init var column)) { return -4; }
                    if (code != expectedCode || span.Start != expectedSpan.Start
                        || span.Length != expectedSpan.Length
                        || !TryGetSourceDiagnosticLocation(span.Start,
                            init var expectedLine, init var expectedColumn)
                        || line != expectedLine || column != expectedColumn) { return -5; }
                    index = index + 1;
                }
                return tokenCount;
            }
            """, "token-diagnostic-location-module.terrum");

        var seed = SourceText.From("seed");
        var producerCompilation = Compilation.Create(trees,
            ScannerHost.CreateInvocationProfileBuffers(seed), policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.VerifyDiagnosticLocations"]);

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                int32 Main() { return VerifyDiagnosticLocations(); }
                """, "token-diagnostic-location-consumer.terrum"),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.True(linked.RequiresSourceGrant);

        var fixtures = new[] {
            "",
            "plain source",
            "\uD800",
            "\uD800\r\uD800",
            "a\r\n\uD800\n\uD800",
            "a\uD800\rx\uD800",
            "\uD800\r\n\"unterminated",
            string.Concat(Enumerable.Repeat("\uD800\r", ScannerHost.MaximumSourceLength / 2)),
        };
        foreach (var fixture in fixtures)
        {
            var source = SourceText.From(fixture);
            var expected = Lexer.Lex(source);
            var actual = NativeJit.ExecuteWithScannerProfile(linked, source, (NativeRuntimePolicy)policy);
            Assert.Equal(expected.Tokens.Select(static token => (token.Kind, token.Span)),
                actual.LexResult.Tokens.Select(static token => (token.Kind, token.Span)));
            Assert.Equal(expected.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span)),
                actual.LexResult.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span)));
        }
    }
}
