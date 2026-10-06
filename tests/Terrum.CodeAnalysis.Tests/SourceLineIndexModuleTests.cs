using System;
using System.Collections.Generic;
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
            public int32 GetIndexedPosition(int32 position) {
                if (!SourceLineIndex.TryCreate(init var index)) { return -1; }
                if (!index.TryGetPosition(position, init var line, init var character)) { return -1; }
                return line * 256 + character;
            }
            public int32 GetIndexedOffset(int32 line, int32 character) {
                if (!SourceLineIndex.TryCreate(init var index)) { return -1; }
                if (!index.TryGetOffset(line, character, init var offset)) { return -1; }
                return offset;
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
            [
                "Terrum.CodeAnalysis.GetIndexedLineCount",
                "Terrum.CodeAnalysis.GetIndexedLineRange",
                "Terrum.CodeAnalysis.GetIndexedPosition",
                "Terrum.CodeAnalysis.GetIndexedOffset",
            ]);

        var fixtures = new[] {
            "",
            "plain",
            "a\r\nb\rc\nd",
            "\uD800\r\nx",
            "a\u0085b\u2028c\u2029d",
            "x\r\n",
            "x\r",
            "x\n",
            "x\u0085",
            "x\u2028",
            "x\u2029",
        };
        var maximumLineCount = fixtures.Max(static text => SourceText.From(text).Lines.Length);
        var maximumSourceLength = fixtures.Max(static text => text.Length);
        var consumers = new BoundProgram[maximumLineCount * 2 + 1];
        BoundProgram CreateConsumer(string body)
        {
            var consumerTree = SyntaxTree.Parse($$"""
                namespace Terrum.CodeAnalysis;
                int32 Main() { {{body}} }
                """, "source-line-index-consumer.terrum");
            var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
            Assert.False(consumerCompilation.HasErrors,
                string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
            return ModuleCompiler.Link(consumer, producer);
        }

        for (var query = 0; query < consumers.Length; query++)
        {
            var line = (query - 1) / 2;
            var includingBreak = (query & 1) != 0 ? "true" : "false";
            var consumerBody = query == 0
                ? "return GetIndexedLineCount();"
                : $"return GetIndexedLineRange({line}, {includingBreak});";
            consumers[query] = CreateConsumer(consumerBody);
        }

        var positionInputs = Enumerable.Range(-1, maximumSourceLength + 3)
            .Append(int.MaxValue)
            .ToArray();
        var positionConsumers = positionInputs
            .Select(static position => $"return GetIndexedPosition({position});")
            .Select(CreateConsumer)
            .ToArray();
        var offsetInputs = new List<(int Line, int Character)>();
        void AddOffsetInput(int line, int character)
        {
            var input = (line, character);
            if (!offsetInputs.Contains(input))
            {
                offsetInputs.Add(input);
            }
        }
        foreach (var text in fixtures)
        {
            var source = SourceText.From(text);
            for (var position = 0; position <= source.Length; position++)
            {
                var location = source.GetLinePosition(position);
                AddOffsetInput(location.Line, location.Character);
            }
            for (var line = 0; line < source.Lines.Length; line++)
            {
                var lineEnd = source.Lines[line].SpanIncludingLineBreak.Length;
                AddOffsetInput(line, lineEnd);
                AddOffsetInput(line, lineEnd + 1);
            }
        }
        AddOffsetInput(-1, 0);
        AddOffsetInput(maximumLineCount, 0);
        AddOffsetInput(int.MaxValue, 0);
        AddOffsetInput(0, -1);
        AddOffsetInput(0, int.MaxValue);
        var offsetConsumers = offsetInputs
            .Select(static input => $"return GetIndexedOffset({input.Line}, {input.Character});")
            .Select(CreateConsumer)
            .ToArray();

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
            for (var query = 0; query < positionInputs.Length; query++)
            {
                var position = positionInputs[query];
                var expectedPosition = position < 0 || position > source.Length
                    ? -1
                    : PackPosition(source.GetLinePosition(position));
                Assert.Equal(expectedPosition, NativeJit.ExecuteWithSourceGrant(positionConsumers[query],
                    [grant], (NativeRuntimePolicy)policy));
            }
            for (var query = 0; query < offsetInputs.Count; query++)
            {
                var input = offsetInputs[query];
                var expectedOffset = source.TryGetOffset(input.Line, input.Character, out var offset)
                    ? offset
                    : -1;
                Assert.Equal(expectedOffset, NativeJit.ExecuteWithSourceGrant(offsetConsumers[query],
                    [grant], (NativeRuntimePolicy)policy));
            }
        }
    }

    private static int PackRange(TextSpan range) => range.Start * 256 + range.Length;

    private static int PackPosition(LinePosition position) => position.Line * 256 + position.Character;

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
