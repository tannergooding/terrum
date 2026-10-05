using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class SourceLineIndexModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SourceLineIndexModuleIndexesFreshSourceGrants(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "SourceRange", "CharacterFacts", "SourceText", "SourceLines", "SourceLineIndex" };
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
            public int32 GetIndexedLineCount() {
                if (!SourceLineIndex.TryCreate(init var index)) { return -1; }
                return index.GetLineCount();
            }
            public int32 GetIndexedLineRange(int32 line, bool includingBreak) {
                if (!SourceLineIndex.TryCreate(init var index)) { return -1; }
                if (includingBreak) {
                    if (!index.TryGetLineExtent(line, init var range)) { return -2; }
                    return range.Start * 256 + range.Length;
                }
                if (!index.TryGetLineContentExtent(line, init var content)) { return -2; }
                return content.Start * 256 + content.Length;
            }
            """, "source-line-index-module.terrum");
        var sample = "a\r\nb\rc\nd";
        var producerCompilation = Compilation.Create(trees,
        [
            new HostBuffer("Source", PrimitiveType.Char16, [.. sample.Select(static unit => (int)unit)], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.GetIndexedLineCount", "Terrum.CodeAnalysis.GetIndexedLineRange"]);

        var fixtures = new[] { "", "plain", "a\r\nb\rc\nd", "\uD800\r\nx" };
        var maximumLineCount = fixtures.Max(static text => SourceText.From(text).Lines.Length);
        var consumers = new BoundProgram[maximumLineCount * 2 + 1];
        for (var query = 0; query < consumers.Length; query++)
        {
            var line = (query - 1) / 2;
            var includingBreak = (query & 1) != 0 ? "true" : "false";
            var consumerBody = query == 0
                ? "return GetIndexedLineCount();"
                : $"return GetIndexedLineRange({line}, {includingBreak});";
            var consumerTree = SyntaxTree.Parse($$"""
                namespace Terrum.CodeAnalysis;
                int32 Main() { {{consumerBody}} }
                """, "source-line-index-consumer.terrum");
            var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
            Assert.False(consumerCompilation.HasErrors,
                string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
            consumers[query] = ModuleCompiler.Link(consumer, producer);
        }

        foreach (var text in fixtures)
        {
            var source = SourceText.From(text);
            var grant = new HostBuffer("Source", PrimitiveType.Char16,
                [.. text.Select(static unit => (int)unit)], false) {
                IsInvocationScoped = true,
            };
            Assert.Equal(source.Lines.Length,
                NativeJit.ExecuteWithSourceGrant(consumers[0], [grant], (NativeRuntimePolicy)policy));
            for (var line = 0; line < maximumLineCount; line++)
            {
                var includingBreak = line < source.Lines.Length
                    ? PackRange(source.Lines[line].SpanIncludingLineBreak)
                    : -2;
                var content = line < source.Lines.Length ? PackRange(source.Lines[line].Span) : -2;
                Assert.Equal(includingBreak, NativeJit.ExecuteWithSourceGrant(consumers[line * 2 + 1],
                    [grant], (NativeRuntimePolicy)policy));
                Assert.Equal(content, NativeJit.ExecuteWithSourceGrant(consumers[line * 2 + 2],
                    [grant], (NativeRuntimePolicy)policy));
            }
        }
    }

    private static int PackRange(TextSpan range) => range.Start * 256 + range.Length;

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
